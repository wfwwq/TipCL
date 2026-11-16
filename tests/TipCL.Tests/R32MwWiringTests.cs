// R32-MWG:自定义布局 v2「壳 / 接线层(L3)」单元测试,mac(Avalonia)树落地件。
//   底稿(逐行) = Linux 同层落地件,但 Linux 树**没有**独立的外壳钩子测试文件 ——
//                 实测 wt-linux-layout @2746f59 的 tests/TipCL.Tests/Layout/ 下只有
//                 LayoutEditorTests / LayoutEngineTests / LayoutGridCanvasTests /
//                 LayoutV2CompatTests / LayoutV2ModelTests / PageLayoutTests(六个),没有
//                 LayoutShellHooksTests;Linux 侧的外壳钩子是**没有单测**的(见 R32-MWG.md §未做到)。
//                 所以本文件不是逐行移植,而是按 Windows 真源 windos/src/TipCL/Pages/Layout/LayoutShellHooks.cs
//                 的每条判定在 mac 树上**实测重写**:哪些判定在 mac 可编译、哪些在无头环境下验不了,都在用例
//                 注释里写死(不伪造)。
//   行为真源 = Windows 冻结件 layout-shell-hooks @ fa1b3d7(与 wt-r31-merge @ 0a304a73 逐字相同)。
//
// 与 Windows 侧的机械差异(断言口径):
//   W-1 无 Windows 侧单测可对:Windows 测试宿主是 net8,WPF 控件碰不得(见 R32-McLayoutEditorTests 头注同一句),
//       外壳钩子那层 Windows 本来就只有手测与 --shot ⇒ 这些判定在 mac 上第一次被钉住。
//   W-2 菜单文本走 I18n.L(key):Windows 侧是 win.Toast / I18n.L 的同一个 key,mac 的 I18n.L 对这批中文
//       字面量就是原样返回 ⇒ 断言写法用 I18n.L("..."),改文案时源码与期望一起红。
//   W-3 打开菜单:Windows 是 menu.IsOpen = true;mac 是 menu.Open(pageHost)(MenuBase.IsOpen 无公开 setter)。
//       无头里弹出的 Popup 会被真开一遍 —— 用例只验"事件被吃掉 / 菜单项文本 / 点击效果",不验 Popup 的视觉。
//   W-4 滚轮量纲:Windows MouseWheelEventArgs.Delta 是 ±120/格;mac PointerWheelEventArgs.Delta.Y 是 ±1.0/格。
//       外壳钩子内部统一以"格"喂 ScrollBy,本文件也按格断言。
//   W-5 无头限制(实测修正 C2b 记的同一句话):headless 下 **原生 ScrollViewer** 的 Extent/Viewport 是
//       真值(100 高视口 + 高 400 的内容 ⇒ ScrollBarMaximum.Y == 300),"滚了多少像素"完全验得了;
//       C2b 记的"headless 下 Extent 恒为 0"只对 **SmoothScroll** 成立 —— 根因是它没写 StyleKeyOverride,
//       Avalonia 11 按派生类型找 ControlTheme 拿不到 ScrollViewer 那套主题,模板退化成 FuncControlTemplate
//       (+ContentPresenter),度量恒 (0,0)(实测见 R32-MWG.md §SmoothScroll)。本包已在 SmoothScroll 上补回
//       那一句(标 [layout-fix] R32-MWG),下面 SmoothScroll 那条用例因此也能断言"滚了多少"。
//   W-6 命名空间:Windows 在 windos/src/TipCL/Pages/Layout 下;mac 是**平铺** src/TipCL.App/Pages/
//       + namespace TipCL.Pages(D7,Mac 侧无 Pages/Layout 子目录)。
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// L3 壳 / 接线层测试:<see cref="LayoutShellHooks"/> 的挂/摘、右键让位四条、右键菜单文本与点击效果、
/// 「在鼠标处进入编辑」的宿主选取顺序、以及统一滚轮里那两个可观测的接线点。
///
/// 所有用例都在 <see cref="AvaloniaSession.Run(Action)"/> 的无头 UI 线程上跑(仓库既有约定),
/// 每个用例一个隔离数据目录(<see cref="C3aHome"/>)并截下 Toast(<see cref="C3aToasts"/>)。
/// </summary>
public class R32MwWiringTests
{
	// ───────────────────────────── 夹具 ─────────────────────────────

	private static BlockHost Host(string pageId, string id)
	{
		return C3aBuild.Host(pageId, C3aBuild.Spec(id));
	}

	/// <summary>把若干控件按左上角/尺寸摆进一张 400×400 的画布(用来给宿主造出确定的矩形)。</summary>
	private static Canvas Layout(params (Control Child, double X, double Y, double W, double H)[] items)
	{
		var canvas = new Canvas { Width = 400.0, Height = 400.0 };
		foreach ((Control child, double x, double y, double w, double h) in items)
		{
			child.Width = w;
			child.Height = h;
			Canvas.SetLeft(child, x);
			Canvas.SetTop(child, y);
			canvas.Children.Add(child);
		}
		return canvas;
	}

	// ───────────────────────────── 挂 / 摘 ─────────────────────────────

	/// <summary>外壳构造末尾就挂上了钩子(MainWindow 的接线点),重复挂是空操作,摘掉之后又能再挂。</summary>
	[Fact]
	public void Shell_construction_attaches_the_hooks_and_attach_detach_are_idempotent()
	{
		AvaloniaSession.Run(() =>
		{
			using var home = new C3aHome();
			using var fixture = ShellFixture.Create();

			// 接线点:MainWindow 构造末尾 LayoutShellHooks.Attach(this)(与 FileDrop.Attach 并排)
			Assert.True(LayoutShellHooks.Attached);

			// 幂等:再挂一次不该抛、也不该把状态翻掉
			LayoutShellHooks.Attach(fixture.Window);
			Assert.True(LayoutShellHooks.Attached);

			// 空窗守卫:null 不抛,也不改状态
			LayoutShellHooks.Attach(null!);
			Assert.True(LayoutShellHooks.Attached);

			// 摘:摘完可以再摘(空操作),然后再挂一次
			LayoutShellHooks.Detach();
			Assert.False(LayoutShellHooks.Attached);
			LayoutShellHooks.Detach();
			Assert.False(LayoutShellHooks.Attached);
			LayoutShellHooks.Attach(fixture.Window);
			Assert.True(LayoutShellHooks.Attached);
		});
	}

	// ───────────────────────────── 右键:让位四条 ─────────────────────────────

	/// <summary>让位四条(与 Windows 的 OriginalSource 链同口径):文本框(含密码框,D-75)、
	/// 自带 ContextMenu 的控件、菜单自身 → 让位;普通内容 → 不让位。</summary>
	[Fact]
	public void SkipRightClick_honours_the_four_yield_rules()
	{
		AvaloniaSession.Run(() =>
		{
			using var home = new C3aHome();
			using var fixture = ShellFixture.Create();
			PageHost pageHost = fixture.Window.Host!;
			var text = new TextBox();
			var ownedWrap = new Border { Child = new Border() };
			ownedWrap.ContextMenu = new ContextMenu();
			var menuItem = new MenuItem();
			var menu = new ContextMenu();
			var plain = new Border();
			Canvas page = Layout(
				(text, 0.0, 0.0, 100.0, 100.0),
				(ownedWrap, 150.0, 0.0, 100.0, 100.0),
				(plain, 300.0, 0.0, 100.0, 100.0)
			);
			pageHost.Content = page;
			UiHost.Flush();
			fixture.Window.CaptureRenderedFrame();

			// 1) 源就是 TextBox;以及源是 TextBox 模板里的更深一层(真命中测试拿到的是它)
			//    —— 祖先链走到 TextBox 就该让位(密码框也是 TextBox + PasswordChar,D-75)
			Assert.True(LayoutShellHooks.SkipRightClick(text, pageHost));
			Visual? innerText = text.GetVisualDescendants().FirstOrDefault();
			Assert.NotNull(innerText);
			Assert.True(LayoutShellHooks.SkipRightClick(innerText, pageHost));
			// 2) 源是"自带右键菜单的控件"的孩子 —— 祖先链走到那个控件就让位
			Visual ownedChild = (Visual)ownedWrap.Child!;
			Assert.True(LayoutShellHooks.SkipRightClick(ownedChild, pageHost));
			// 3) 菜单自身(菜单项 / 菜单容器)
			Assert.True(LayoutShellHooks.SkipRightClick(menuItem, pageHost));
			Assert.True(LayoutShellHooks.SkipRightClick(menu, pageHost));
			// 4) 普通内容不让位
			Assert.False(LayoutShellHooks.SkipRightClick(plain, pageHost));
			// 边界:源就是 pageHost 本身 / 源为 null(点到了没有源的地方)
			Assert.False(LayoutShellHooks.SkipRightClick(pageHost, pageHost));
			Assert.False(LayoutShellHooks.SkipRightClick(null, pageHost));
		});
	}

	// ───────────────────────────── 右键:菜单文本 ─────────────────────────────

	/// <summary>不在编辑态:菜单是「自定义布局...」/ 分隔 /「布局设置...」三项(与 Windows 同一批 I18n key)。</summary>
	[Fact]
	public void Menu_offers_enter_edit_and_layout_settings_when_nothing_is_being_edited()
	{
		AvaloniaSession.Run(() =>
		{
			using var home = new C3aHome();
			using var fixture = ShellFixture.Create();
			Canvas page = Layout((Host("wiring", "a"), 0.0, 0.0, 300.0, 300.0));
			fixture.Window.Host!.Content = page;
			UiHost.Flush();

			ContextMenu menu = LayoutShellHooks.BuildContextMenu(fixture.Window, page, null, new Point(10.0, 10.0));

			Assert.Equal(3, menu.Items.Count);
			Assert.Equal(I18n.L("自定义布局..."), ((MenuItem)menu.Items[0]!).Header);
			Assert.IsType<Separator>(menu.Items[1]);
			Assert.Equal(I18n.L("布局设置..."), ((MenuItem)menu.Items[2]!).Header);
		});
	}

	/// <summary>编辑中:不再给「自定义布局...」,换成「完成编辑」+「重置本页布局」,末尾仍是分隔 +「布局设置...」。</summary>
	[Fact]
	public void Menu_offers_finish_and_reset_while_editing()
	{
		AvaloniaSession.Run(() =>
		{
			using var home = new C3aHome();
			using var fixture = ShellFixture.Create();
			BlockHost host = Host("wiring", "a");
			Canvas page = Layout((host, 0.0, 0.0, 300.0, 300.0));
			fixture.Window.Host!.Content = page;
			UiHost.Flush();

			Assert.True(host.EnterEdit());
			Assert.True(host.Editor.IsEditing);

			ContextMenu menu = LayoutShellHooks.BuildContextMenu(fixture.Window, page, null, new Point(10.0, 10.0));

			Assert.Equal(4, menu.Items.Count);
			Assert.Equal(I18n.L("完成编辑"), ((MenuItem)menu.Items[0]!).Header);
			Assert.Equal(I18n.L("重置本页布局"), ((MenuItem)menu.Items[1]!).Header);
			Assert.IsType<Separator>(menu.Items[2]);
			Assert.Equal(I18n.L("布局设置..."), ((MenuItem)menu.Items[3]!).Header);

			// 点「完成编辑」= 退编辑(Windows 的 editing.Editor.ExitEdit())
			((MenuItem)menu.Items[0]!).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
			Assert.False(host.Editor.IsEditing);
		});
	}

	/// <summary>点「重置本页布局」= 回到出厂布局并落盘 + Toast(布局已重置 / 本页已恢复默认布局,Success);
	/// 重置不退出编辑态(ResetPageCore 里 _edit 为真会重新 PrepareFreeDrag)—— 与 Windows 同一句。</summary>
	[Fact]
	public void Menu_reset_button_restores_the_defaults_toasts_and_keeps_editing()
	{
		AvaloniaSession.Run(() =>
		{
			using var home = new C3aHome();
			using var fixture = ShellFixture.Create();
			using var toasts = new C3aToasts();
			BlockHost host = Host("wiring", "a");
			Canvas page = Layout((host, 0.0, 0.0, 300.0, 300.0));
			fixture.Window.Host!.Content = page;
			UiHost.Flush();

			Assert.True(host.EnterEdit());
			toasts.Items.Clear();

			ContextMenu menu = LayoutShellHooks.BuildContextMenu(fixture.Window, page, null, new Point(10.0, 10.0));
			((MenuItem)menu.Items[1]!).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

			Assert.Contains(toasts.Items, t => t.Title == I18n.L("布局已重置"));
			Assert.Contains(toasts.Items, t => t.Title == I18n.L("布局已重置") && t.Kind == ToastKind.Success);
			Assert.True(host.Editor.IsEditing);
		});
	}

	/// <summary>点「布局设置...」= OpenLayoutSettings。
	/// mac 降级(实测 SettingsPage 没有 OpenLayoutView):只 Navigate("settings"),不切内层布局视图。</summary>
	[Fact]
	public void Menu_layout_settings_button_navigates_to_the_settings_page()
	{
		AvaloniaSession.Run(() =>
		{
			using var home = new C3aHome();
			using var fixture = ShellFixture.Create();
			PageHost pageHost = fixture.Window.Host!;
			Canvas page = Layout((Host("wiring", "a"), 0.0, 0.0, 300.0, 300.0));
			pageHost.Content = page;
			UiHost.Flush();

			ContextMenu menu = LayoutShellHooks.BuildContextMenu(fixture.Window, page, null, new Point(10.0, 10.0));
			((MenuItem)menu.Items[2]!).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

			Assert.Equal("settings", pageHost.CurrentId);
		});
	}

	// ───────────────────────────── 在鼠标处进入编辑:宿主选取顺序 ─────────────────────────────

	/// <summary>最内层优先:点在两个重叠宿主都在的范围内,而源属于上面那个(后加的)→ 选源所属的那个。</summary>
	[Fact]
	public void EnterEditAt_prefers_the_host_that_owns_the_event_source()
	{
		AvaloniaSession.Run(() =>
		{
			using var home = new C3aHome();
			using var fixture = ShellFixture.Create();
			BlockHost big = Host("wiring", "big");
			BlockHost small = Host("wiring", "small");
			Canvas page = Layout((big, 0.0, 0.0, 300.0, 300.0), (small, 50.0, 50.0, 200.0, 200.0));
			fixture.Window.Host!.Content = page;
			UiHost.Flush();

			// 源在 small 里(source = small 的可视内容根),点 (100,100) 两个矩形都含
			Visual source = (Visual)small.Content!;
			Assert.True(LayoutShellHooks.EnterEditAt(fixture.Window, page, source, new Point(100.0, 100.0)));

			Assert.True(small.Editor.IsEditing);
			Assert.False(big.Editor.IsEditing);
		});
	}

	/// <summary>源不在任何宿主里 → 取"包含点的最小宿主"(Windows 的第二档)。</summary>
	[Fact]
	public void EnterEditAt_picks_the_smallest_host_containing_the_point()
	{
		AvaloniaSession.Run(() =>
		{
			using var home = new C3aHome();
			using var fixture = ShellFixture.Create();
			BlockHost big = Host("wiring", "big");
			BlockHost small = Host("wiring", "small");
			Canvas page = Layout((big, 0.0, 0.0, 300.0, 300.0), (small, 50.0, 50.0, 200.0, 200.0));
			fixture.Window.Host!.Content = page;
			UiHost.Flush();

			// 源给画布本身(不属于任何宿主),点仍落在两个宿主的重叠区里
			Assert.True(LayoutShellHooks.EnterEditAt(fixture.Window, page, page, new Point(100.0, 100.0)));

			Assert.True(small.Editor.IsEditing);
			Assert.False(big.Editor.IsEditing);
		});
	}

	/// <summary>点不在任何宿主里 → 取"页面里最大的宿主"(Windows 的第三档)。</summary>
	[Fact]
	public void EnterEditAt_falls_back_to_the_largest_host()
	{
		AvaloniaSession.Run(() =>
		{
			using var home = new C3aHome();
			using var fixture = ShellFixture.Create();
			BlockHost big = Host("wiring", "big");
			BlockHost small = Host("wiring", "small");
			Canvas page = Layout((big, 0.0, 0.0, 300.0, 300.0), (small, 50.0, 50.0, 200.0, 200.0));
			fixture.Window.Host!.Content = page;
			UiHost.Flush();

			Assert.True(LayoutShellHooks.EnterEditAt(fixture.Window, page, page, new Point(380.0, 380.0)));

			Assert.True(big.Editor.IsEditing);
			Assert.False(small.Editor.IsEditing);
		});
	}

	/// <summary>页面里一个可编辑区块都没有 → 一句 Warning Toast + 返回 false(不弹菜单、不进编辑)。</summary>
	[Fact]
	public void EnterEditAt_warns_when_the_page_has_no_editable_block()
	{
		AvaloniaSession.Run(() =>
		{
			using var home = new C3aHome();
			using var fixture = ShellFixture.Create();
			using var toasts = new C3aToasts();
			var page = new Border { Width = 300.0, Height = 300.0 };
			fixture.Window.Host!.Content = page;
			UiHost.Flush();

			Assert.False(LayoutShellHooks.EnterEditAt(fixture.Window, page, page, new Point(10.0, 10.0)));
			Assert.Contains(toasts.Items, t => t.Title == I18n.L("这个页面没有可拖动的区块") && t.Kind == ToastKind.Warning);
		});
	}

	/// <summary>FindHosts:只收"可见"且"可编辑(至少一个块)"的宿主;隐藏的、没有块的都不算。</summary>
	[Fact]
	public void FindHosts_keeps_only_visible_editable_hosts()
	{
		AvaloniaSession.Run(() =>
		{
			using var home = new C3aHome();
			using var fixture = ShellFixture.Create();
			BlockHost visible = Host("wiring", "a");
			BlockHost hidden = Host("wiring", "b");
			hidden.IsVisible = false;
			BlockHost empty = C3aBuild.Host("wiring");
			Canvas page = Layout(
				(visible, 0.0, 0.0, 100.0, 100.0),
				(hidden, 100.0, 0.0, 100.0, 100.0),
				(empty, 200.0, 0.0, 100.0, 100.0)
			);
			fixture.Window.Host!.Content = page;
			UiHost.Flush();

			List<BlockHost> hosts = LayoutShellHooks.FindHosts(page);
			Assert.Single(hosts);
			Assert.Same(visible, hosts[0]);
			Assert.False(empty.Editor.CanEdit);
		});
	}

	// ───────────────────────────── 右键事件通路(真事件路由) ─────────────────────────────

	/// <summary>真发一个右键抬起:源是普通内容 → 弹菜单并吃掉事件(Handled = true);
	/// 源是文本框 → 让位,事件不被吃(Handled = false)。</summary>
	[Fact]
	public void Right_button_up_is_swallowed_on_plain_content_and_left_alone_on_a_text_box()
	{
		AvaloniaSession.Run(() =>
		{
			using var home = new C3aHome();
			using var fixture = ShellFixture.Create();
			PageHost pageHost = fixture.Window.Host!;
			var plain = new Border { Width = 100.0, Height = 100.0 };
			var text = new TextBox { Width = 100.0, Height = 100.0 };
			Canvas page = Layout((plain, 0.0, 0.0, 100.0, 100.0), (text, 150.0, 0.0, 100.0, 100.0));
			pageHost.Content = page;
			UiHost.Flush();
			fixture.Window.CaptureRenderedFrame();

			PointerReleasedEventArgs onPlain = C2c1Input.Release(MouseButton.Right, plain);
			plain.RaiseEvent(onPlain);
			Assert.True(onPlain.Handled);

			PointerReleasedEventArgs onText = C2c1Input.Release(MouseButton.Right, text);
			text.RaiseEvent(onText);
			Assert.False(onText.Handled);

			// 左键抬起一律不管(Windows 只挂 MouseRightButtonUp)
			var left = C2c1Input.Release(MouseButton.Left, plain);
			plain.RaiseEvent(left);
			Assert.False(left.Handled);
		});
	}

	// ───────────────────────────── 统一滚轮:两个可观测的接线点 ─────────────────────────────

	/// <summary>ScrollBy 认得 SmoothScroll:外层容器被转交滚轮时走同一个 WheelBy(Windows 的同一套语义)。
	/// 测试进程没有动画时钟 ⇒ 走 WheelBy 的"不做动画、按 Windows 的 0.4 系数落一次"那条分支
	/// (实测 1 格 = 48px),偏移真的动、也真的夹在 [0, ScrollableHeight]。</summary>
	[Fact]
	public void ScrollBy_routes_a_SmoothScroll_through_WheelBy()
	{
		AvaloniaSession.Run(() =>
		{
			using var home = new C3aHome();
			var smooth = new SmoothScroll { Width = 100.0, Height = 100.0, Content = new Border { Height = 400.0 } };
			using var host = UiHost.Show(smooth);
			smooth.UpdateLayout();
			UiHost.Flush();

			// 补回 StyleKeyOverride 之后 SmoothScroll 也有真正的度量(此前恒 (0,0))
			Assert.Equal(300.0, smooth.ScrollBarMaximum.Y);
			// 测试进程无动画时钟 ⇒ ShouldSmoothWheel 为假,下面走的必然是 WheelBy 的"直接落一次"分支
			Assert.False(smooth.ShouldSmoothWheel);

			smooth.Offset = 37.0;
			Assert.Equal(37.0, smooth.Offset);
			Assert.Equal(37.0, ((ScrollViewer)smooth).Offset.Y);   // 隐藏的 double 写下去会同步到基类 Vector

			LayoutShellHooks.ScrollBy(smooth, -1.0);

			// 真的走到了 WheelBy:37 + 120×0.4 = 85(不是 plain 分支的 48px/格,也不是没动)
			Assert.Equal(85.0, smooth.Offset);
			Assert.Equal(85.0, ((ScrollViewer)smooth).Offset.Y);

			// 反向多格:夹到 ScrollableHeight(=300)与 0,不抛
			LayoutShellHooks.ScrollBy(smooth, -10.0);
			Assert.Equal(300.0, smooth.Offset);
			LayoutShellHooks.ScrollBy(smooth, 10.0);
			Assert.Equal(0.0, smooth.Offset);
		});
	}

	/// <summary>普通 ScrollViewer 走固定 48px/格(mac 没有 CanContentScroll,Windows 的 3.0 分支无对应),
	/// 并夹在 [0, ScrollBarMaximum.Y] —— 实测 100 高视口 + 高 400 内容 ⇒ 上限 300。</summary>
	[Fact]
	public void ScrollBy_clamps_a_plain_viewer_without_throw()
	{
		AvaloniaSession.Run(() =>
		{
			using var home = new C3aHome();
			var viewer = new ScrollViewer
			{
				Width = 100.0,
				Height = 100.0,
				Content = new Border { Height = 400.0 },
			};
			using var host = UiHost.Show(viewer);
			viewer.UpdateLayout();
			UiHost.Flush();

			Assert.Equal(300.0, viewer.ScrollBarMaximum.Y);

			LayoutShellHooks.ScrollBy(viewer, -1.0);
			Assert.Equal(48.0, viewer.Offset.Y);
			LayoutShellHooks.ScrollBy(viewer, -1.0);
			Assert.Equal(96.0, viewer.Offset.Y);
			LayoutShellHooks.ScrollBy(viewer, 1.0);
			Assert.Equal(48.0, viewer.Offset.Y);

			// 夹取:往下到上限、往上到 0
			LayoutShellHooks.ScrollBy(viewer, -10.0);
			Assert.Equal(300.0, viewer.Offset.Y);
			LayoutShellHooks.ScrollBy(viewer, 10.0);
			Assert.Equal(0.0, viewer.Offset.Y);
		});
	}

	/// <summary>CanScroll:竖滚动条被禁用时一律"滚不动",即使度量还有 300px 余量
	/// (实测:VerticalScrollBarVisibility=Disabled 时 Extent/Viewport 仍是 100×400/100×100、上限仍是 300,
	///  所以这里的 false 只可能来自那条 Disabled 判断)。</summary>
	[Fact]
	public void CanScroll_rejects_a_disabled_vertical_bar()
	{
		AvaloniaSession.Run(() =>
		{
			using var home = new C3aHome();
			var disabled = new ScrollViewer
			{
				Width = 100.0,
				Height = 100.0,
				VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
				Content = new Border { Height = 400.0 },
			};
			using var host = UiHost.Show(disabled);
			disabled.UpdateLayout();
			UiHost.Flush();

			Assert.Equal(300.0, disabled.ScrollBarMaximum.Y);
			Assert.False(LayoutShellHooks.CanScroll(disabled, -1.0));
			Assert.False(LayoutShellHooks.CanScroll(disabled, 1.0));
		});
	}

	/// <summary>CanScroll:"当前方向还有没有余量"(与 Windows 同一口径,负 delta = 向下)。
	/// W-5 修正后这条正例可验了:顶部只能往下、到底只能往上。</summary>
	[Fact]
	public void CanScroll_follows_the_remaining_extent()
	{
		AvaloniaSession.Run(() =>
		{
			using var home = new C3aHome();
			var viewer = new ScrollViewer
			{
				Width = 100.0,
				Height = 100.0,
				Content = new Border { Height = 400.0 },
			};
			using var host = UiHost.Show(viewer);
			viewer.UpdateLayout();
			UiHost.Flush();

			// 顶部:向下有余量,向上没有
			Assert.True(LayoutShellHooks.CanScroll(viewer, -1.0));
			Assert.False(LayoutShellHooks.CanScroll(viewer, 1.0));

			// 底部:反过来
			viewer.Offset = new Vector(0.0, 300.0);
			UiHost.Flush();
			Assert.Equal(300.0, viewer.Offset.Y);
			Assert.False(LayoutShellHooks.CanScroll(viewer, -1.0));
			Assert.True(LayoutShellHooks.CanScroll(viewer, 1.0));
		});
	}
}
