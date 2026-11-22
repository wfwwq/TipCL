// 来源: Windows 冻结件 windos/src/TipCL/Pages/Layout/LayoutShellHooks.cs(463 行,layout-shell-hooks @ fa1b3d7;
//       与 wt-r31-merge @ 0a304a73 逐字相同)= 真源;
//       Avalonia 机械模板 = wt-linux-layout @ 2746f59 的 src/TipCL.App/Pages/Layout/LayoutShellHooks.cs(471 行)。
// 主窗口级全局钩子(按 audit-fix E-01 口径):BlockHost 自己**不挂** ContextMenu(见 BlockHost.cs 头注释),
// 右键菜单与"统一滚轮"都在这一处挂:
//   1) 右键:页面内容区任意处右键 → 「自定义布局...」;编辑中 → 「完成编辑」「重置本页布局」;/ 分隔 /「布局设置...」;
//      文本框(含使用 TextBox + PasswordChar 的密码框,D-75)、自带 ContextMenu 的控件、菜单自身 → 让位;
//   2) 滚轮:鼠标下最内层可滚容器自己吃(route==0,不接管,保留原生平滑);最内层吃不下 → 接管并滚外层仍可滚的;
//      鼠标完全不在滚动容器里(页头 / 分段栏 / 编辑工具条)→ 滚当前页里最大的可滚区域;
//      Ctrl / Shift / Slider 一律不拦截。
// 目标宿主选取顺序(纯判定在 LayoutShellLogic):最内层区块宿主 → 包含鼠标位置的最小宿主 → 页面里最大的宿主。
//
// Windows → mac 差异映射(与 Linux 端口同一套实测口径,R32-LW.md §3;本文件在 mac 可编译的部分逐条保留):
//   MouseRightButtonUpEvent            → InputElement.PointerReleasedEvent + InitialPressMouseButton == Right
//   PreviewMouseWheelEvent             → InputElement.PointerWheelChangedEvent + RoutingStrategies.Tunnel(= WPF Preview)
//   **注册策略只给一路**:这两个事件在 Avalonia 的 RoutingStrategies 实测都是 Tunnel|Bubble ⇒ 一次注册
//     Tunnel|Bubble 会触发两遍;右键走 Bubble(pageHost 先于窗口),滚轮走 Tunnel(窗口先于内层,Handled 才能压掉内层二次滚动)
//   RoutedEventArgs 无 OriginalSource      → e.Source
//   DependencyObject 可视树               → Avalonia.VisualTree.VisualExtensions.GetVisualParent/GetVisualChildren
//   GetPosition(x)                        → e.GetPosition(x)(返回 Point)
//   TransformToAncestor().TransformBounds() → VisualExtensions.TranslatePoint(p, target) + Visual.Bounds(尺寸)
//   MouseWheelEventArgs.Delta(±120/格)     → PointerWheelEventArgs.Delta.Y(±1.0/格);本文件内部仍以"格"为单位喂 ScrollBy
//   ScrollViewer.VerticalOffset/ScrollableHeight → ScrollViewer.Offset.Y / ScrollBarMaximum.Y
//   WPF 的 SmoothScroll / CanContentScroll  → mac **有** SmoothScroll(Services\SmoothScroll.cs),所以外层容器被转交时
//     走 SmoothScroll.WheelBy(与 Windows 同一套 0.4/0.95/230ms 语义);普通 ScrollViewer 固定 48px/格
//     (mac 没有 CanContentScroll,Windows 的 3.0 分支无对应)
//   PlacementMode.MousePoint               → Avalonia.Controls.PlacementMode.Pointer
//   menu.IsOpen = true                     → menu.Open(pageHost)(Avalonia 的 MenuBase.IsOpen 无公开 setter)
//   win.Toast(title, body, kind)           → AppServices.Toast(title, body, kind)
//   I18n.L(key)                            → I18n.L(key)(同名同位置)
//   win.PageHost                           → win.Host(MainWindow 的 internal 属性,返回 Pages.PageHost)
//   win.GetPage("settings") is SettingsPage → mac SettingsPage **有**「布局」内层视图了
//     (SettingsPage.Layout.cs:`LayoutView = 7` + `public OpenLayoutView()`),OpenLayoutSettings 与 Windows 逐字同构。
//     [本包 R32-MACMORE 修掉的降级] 上一版 mac 只 Navigate("settings")、不切内层视图,见 R32-MWG.md §5。
//   BlockHost.Editor(FrameworkElement 侧 internal) → 本程序集内可见,右键链路直接读 Editor.IsEditing / CanEdit
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// 主窗口级布局钩子:全局右键「自定义布局...」+ 统一滚轮路由。
/// 装配点:MainWindow 构造末尾 LayoutShellHooks.Attach(this)(与 FileDrop.Attach 并排);窗口关闭时 Detach。
/// </summary>
internal static class LayoutShellHooks
{
	private static MainWindow? _win;

	// 委托实例必须缓存:路由事件要用**同一个**委托才能 RemoveHandler 对称退订
	private static EventHandler<PointerReleasedEventArgs>? _pageHandler;

	private static EventHandler<PointerReleasedEventArgs>? _windowHandler;

	private static EventHandler<PointerWheelEventArgs>? _wheelHandler;

	/// <summary>是否已挂上(幂等保护)。</summary>
	public static bool Attached => _win != null;

	/// <summary>挂到主窗口(重复调用是空操作)。</summary>
	public static void Attach(MainWindow win)
	{
		if (win == null || _win != null)
		{
			return;
		}
		_win = win;
		PageHost? pageHost = win.Host;
		if (pageHost != null)
		{
			// 页面内容区:右键气泡链上先到 pageHost,弹完置 Handled ⇒ 窗口级那一遍自动让位
			_pageHandler = OnPageRightButtonUp;
			pageHost.AddHandler(InputElement.PointerReleasedEvent, _pageHandler, RoutingStrategies.Bubble, handledEventsToo: false);
		}
		// 窗口级兜底:源不在 pageHost 可视子树里(弹层 / 逻辑父)但落点仍在页面矩形内的情况
		_windowHandler = OnWindowRightButtonUp;
		win.AddHandler(InputElement.PointerReleasedEvent, _windowHandler, RoutingStrategies.Bubble, handledEventsToo: false);
		// 统一滚轮:窗口级 Tunnel(= WPF Preview),先于内层拿到事件,才能决定"接管并 Handled"或"放手"
		_wheelHandler = OnPreviewWheel;
		win.AddHandler(InputElement.PointerWheelChangedEvent, _wheelHandler, RoutingStrategies.Tunnel, handledEventsToo: false);
	}

	/// <summary>摘钩(窗口关闭 / 测试收尾)。</summary>
	public static void Detach()
	{
		MainWindow? win = _win;
		_win = null;
		if (win == null)
		{
			return;
		}
		try
		{
			if (_pageHandler != null && win.Host is { } pageHost)
			{
				pageHost.RemoveHandler(InputElement.PointerReleasedEvent, _pageHandler);
			}
			if (_windowHandler != null)
			{
				win.RemoveHandler(InputElement.PointerReleasedEvent, _windowHandler);
			}
			if (_wheelHandler != null)
			{
				win.RemoveHandler(InputElement.PointerWheelChangedEvent, _wheelHandler);
			}
		}
		catch
		{
			// 窗口正在销毁:摘钩失败也不能崩
		}
		_pageHandler = null;
		_windowHandler = null;
		_wheelHandler = null;
	}

	// ───────────────────────────── 右键 ─────────────────────────────

	private static void OnWindowRightButtonUp(object? sender, PointerReleasedEventArgs e)
	{
		MainWindow? win = _win;
		if (win == null || e.Handled || e.InitialPressMouseButton != MouseButton.Right)
		{
			return;
		}
		PageHost? pageHost = win.Host;
		if (pageHost == null || !pageHost.IsVisible)
		{
			return;
		}
		// 源本来就在页面内容区里 ⇒ pageHost 那一遍已经处理过(或已让位),这里不重复
		for (Visual? node = e.Source as Visual; node != null; node = ParentOf(node))
		{
			if (ReferenceEquals(node, pageHost))
			{
				return;
			}
		}
		Point point = e.GetPosition(pageHost);
		if (point.X < 0.0 || point.Y < 0.0 || point.X > pageHost.Bounds.Width || point.Y > pageHost.Bounds.Height)
		{
			return;
		}
		OnPageRightButtonUp(sender, e);
	}

	private static void OnPageRightButtonUp(object? sender, PointerReleasedEventArgs e)
	{
		MainWindow? win = _win;
		if (win == null || e.Handled || e.InitialPressMouseButton != MouseButton.Right)
		{
			return;
		}
		PageHost? pageHost = win.Host;
		Visual? source = e.Source as Visual;
		if (SkipRightClick(source, pageHost))
		{
			return;
		}
		if (pageHost?.Content is not Visual page)
		{
			return;
		}
		Point point = e.GetPosition(page);
		ContextMenu menu = BuildContextMenu(win, page, source, point);
		menu.PlacementTarget = pageHost;
		menu.Placement = PlacementMode.Pointer;
		menu.Open(pageHost);
		e.Handled = true;
	}

	/// <summary>
	/// 让位判定:文本框(含密码框,D-75)/ 自带 ContextMenu 的控件 / 菜单自身 → 不弹布局菜单。
	/// 沿可视祖先链一直到 pageHost(与 Windows 的 OriginalSource 链同口径)。
	/// 抽成公开给程序集的判定,是为了让右键"让位"四条规则能直接被告知式地验。
	/// </summary>
	internal static bool SkipRightClick(Visual? source, Visual? pageHost)
	{
		for (Visual? node = source; node != null && !ReferenceEquals(node, pageHost); node = ParentOf(node))
		{
			if (node is TextBox)
			{
				return true;
			}
			if (node is Control element && element.ContextMenu != null)
			{
				return true;
			}
			if (node is ContextMenu || node is MenuItem || node is Menu)
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// 造右键菜单(不打开)。编辑中 → 「完成编辑」「重置本页布局」;否则 → 「自定义布局...」;
	/// 末尾一律 分隔线 +「布局设置...」。
	/// 抽出来是为了能在无头环境里断言菜单项文本与点击效果(菜单的 Open 会建弹出层,不适合单测)。
	/// </summary>
	internal static ContextMenu BuildContextMenu(MainWindow win, Visual page, Visual? source, Point pointInPage)
	{
		List<BlockHost> hosts = FindHosts(page);
		BlockHost? editing = hosts.Find(delegate(BlockHost host)
		{
			return host.Editor.IsEditing;
		});
		ContextMenu menu = new ContextMenu();
		// 编辑中不再给「自定义布局...」:点了只会落到已经在编辑的宿主上,什么也不发生
		if (editing == null)
		{
			MenuItem enter = new MenuItem { Header = I18n.L("自定义布局...") };
			enter.Click += delegate
			{
				EnterEditAt(win, page, source, pointInPage);
			};
			menu.Items.Add(enter);
		}
		else
		{
			MenuItem done = new MenuItem { Header = I18n.L("完成编辑") };
			done.Click += delegate
			{
				editing.Editor.ExitEdit();
			};
			menu.Items.Add(done);
			MenuItem reset = new MenuItem { Header = I18n.L("重置本页布局") };
			reset.Click += delegate
			{
				editing.Editor.ResetPage();
			};
			menu.Items.Add(reset);
		}
		menu.Items.Add(new Separator());
		MenuItem settings = new MenuItem { Header = I18n.L("布局设置...") };
		settings.Click += delegate
		{
			OpenLayoutSettings(win);
		};
		menu.Items.Add(settings);
		return menu;
	}

	/// <summary>
	/// 在鼠标处进入编辑态:选目标宿主(最内层 → 含点最小 → 页面最大)。
	/// 页面一个可编辑区块都没有时给提示并返回 false。
	/// </summary>
	public static bool EnterEditAt(MainWindow win, Visual page, Visual? source, Point pointInPage)
	{
		List<BlockHost> hosts = FindHosts(page);
		List<LayoutRect> rects = new List<LayoutRect>(hosts.Count);
		int innermost = LayoutHostPick.None;
		for (int i = 0; i < hosts.Count; i++)
		{
			BlockHost host = hosts[i];
			rects.Add(BoundsIn(host, page));
			if (innermost == LayoutHostPick.None && IsWithin(source, host))
			{
				innermost = i;
			}
		}
		int pick = LayoutHostPick.Resolve(innermost, rects, pointInPage.X, pointInPage.Y);
		BlockHost? target = ((pick >= 0 && pick < hosts.Count) ? hosts[pick] : null);
		if (target != null && target.EnterEdit())
		{
			return true;
		}
		// 与 Windows LayoutShellHooks.cs:223 逐字同文(入口「设置 → 布局」在本包已真实存在,不再是降级文案)
		AppServices.Toast(I18n.L("这个页面没有可拖动的区块"), I18n.L("页签、密度、分栏比例可以在「设置 → 布局」里调整"), ToastKind.Warning);
		return false;
	}

	/// <summary>打开「布局设置」。</summary>
	public static void OpenLayoutSettings(MainWindow win)
	{
		// Windows LayoutShellHooks.cs:228-235 同构:
		//   win.Navigate("settings") → 切到设置页;`GetPage("settings") is SettingsPage` → page.OpenLayoutView()
		//   → Select(LayoutView = 7) 切到内层「布局」视图(SettingsPage.Layout.cs)。
		// 映射说明:Windows 的 `SettingsPage.OpenLayoutView()` 与 mac 同名同签名,内部差距只有后者走
		// `Select(int)` 时**重建**布局视图(Windows SettingsPage.cs:825-830 也是这个行为)。
		win.Navigate("settings");
		if (win.GetPage("settings") is SettingsPage page)
		{
			page.OpenLayoutView();
		}
	}

	// ───────────────────────────── 可视树小工具 ─────────────────────────────

	/// <summary>页面(含嵌套)里所有"可编辑"的区块宿主。</summary>
	public static List<BlockHost> FindHosts(Visual root)
	{
		List<BlockHost> list = new List<BlockHost>();
		Collect(root, list, 0);
		return list;
	}

	private static void Collect(Visual? node, List<BlockHost> list, int depth)
	{
		if (node == null || depth > 60)
		{
			return;
		}
		if (!node.IsVisible)
		{
			return;
		}
		if (node is BlockHost host && host.Editor.CanEdit)
		{
			list.Add(host);
		}
		try
		{
			foreach (Visual child in node.GetVisualChildren())
			{
				Collect(child, list, depth + 1);
			}
		}
		catch
		{
			// 模板正在重建:这一层没走完也不影响已收集到的宿主
		}
	}

	/// <summary>把 element 的矩形换算到 page 的坐标系(等效 WPF TransformToAncestor+TransformBounds)。</summary>
	private static LayoutRect BoundsIn(Visual element, Visual page)
	{
		try
		{
			Point? origin = element.TranslatePoint(new Point(0.0, 0.0), page);
			if (origin is null)
			{
				return default(LayoutRect);
			}
			return new LayoutRect(origin.Value.X, origin.Value.Y, element.Bounds.Width, element.Bounds.Height);
		}
		catch
		{
			return default(LayoutRect);
		}
	}

	private static bool IsWithin(Visual? node, Visual? ancestor)
	{
		for (Visual? walk = node; walk != null; walk = ParentOf(walk))
		{
			if (ReferenceEquals(walk, ancestor))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>可视父(Avalonia 无 DependencyObject 通用父链;右键/滚轮判定都只看可视树)。</summary>
	private static Visual? ParentOf(Visual? node)
	{
		return node?.GetVisualParent();
	}

	// ───────────────────────────── 统一滚轮 ─────────────────────────────

	private static void OnPreviewWheel(object? sender, PointerWheelEventArgs e)
	{
		MainWindow? win = _win;
		if (win == null || e.Handled || e.Delta.Y == 0.0)
		{
			return;
		}
		// Ctrl(缩放)/ Shift(横向)不拦截
		if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != 0)
		{
			return;
		}
		Visual? source = e.Source as Visual;
		PageHost? pageHost = win.Host;
		bool inPage = false;
		ScrollViewer? innermost = null;
		ScrollViewer? target = null;
		List<ScrollViewer> chain = new List<ScrollViewer>();
		List<bool> canScroll = new List<bool>();
		for (Visual? node = source; node != null; node = ParentOf(node))
		{
			if (ReferenceEquals(node, pageHost))
			{
				inPage = true;
				break;
			}
			if (node is Slider)
			{
				// 滑块自己的滚轮语义(调值)优先
				return;
			}
			if (node is ScrollViewer viewer)
			{
				if (innermost == null)
				{
					innermost = viewer;
				}
				bool can = CanScroll(viewer, e.Delta.Y);
				chain.Add(viewer);
				canScroll.Add(can);
				if (target == null && can)
				{
					target = viewer;
				}
			}
		}
		int route = LayoutWheelRouting.Route(canScroll, chain.Count != 0);
		if (route == 0)
		{
			// 最内层自己还能滚:交给原生(保留 SmoothScroll 的平滑),不接管
			return;
		}
		if (target != null)
		{
			// 最内层滚不动了:滚外层仍可滚的那个,并吃掉事件(否则内层气泡还会再滚一次)
			ScrollBy(target, e.Delta.Y);
			e.Handled = true;
			return;
		}
		if (!inPage || innermost != null || route != LayoutWheelRouting.OutsideAnyScroll)
		{
			// 鼠标在某个滚动容器里、整条链都已到头 → 保持原生行为,不去滚别的列表
			return;
		}
		// 鼠标所在位置没有任何能滚的容器(页头、分段栏、内层到底的列表……)→ 滚页面主体里最大的可滚区域
		if (pageHost?.Content is Visual page)
		{
			ScrollViewer? main = FindMainScroller(page, e.Delta.Y);
			if (main != null)
			{
				ScrollBy(main, e.Delta.Y);
				e.Handled = true;
			}
		}
	}

	internal static bool CanScroll(ScrollViewer viewer, double delta)
	{
		return LayoutWheelRouting.CanScroll(viewer.Offset.Y, viewer.ScrollBarMaximum.Y, delta)
			&& viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled;
	}

	/// <summary>
	/// 滚一次。<paramref name="notches"/> 是 Avalonia 的格数(±1.0/格)。
	/// mac 有 SmoothScroll:外层容器被转交滚轮时也走同一套平滑动画(Windows 的 WheelBy);
	/// 普通 ScrollViewer 固定 48px/格(mac 没有 CanContentScroll,Windows 的 3.0 分支无对应)。
	/// </summary>
	internal static void ScrollBy(ScrollViewer viewer, double notches)
	{
		if (viewer is SmoothScroll smooth)
		{
			smooth.WheelBy(notches);
			return;
		}
		double max = viewer.ScrollBarMaximum.Y;
		double target = viewer.Offset.Y - notches * 48.0;
		if (target < 0.0)
		{
			target = 0.0;
		}
		else if (target > max)
		{
			target = max;
		}
		viewer.Offset = new Vector(viewer.Offset.X, target);
	}

	// 页面里"最大的那个可滚区域"(面积最大且在当前方向还能滚)
	private static ScrollViewer? FindMainScroller(Visual root, double delta)
	{
		List<ScrollViewer> found = new List<ScrollViewer>();
		CollectScrollViewers(root, found, 0);
		List<double> areas = new List<double>(found.Count);
		List<bool> canScroll = new List<bool>(found.Count);
		foreach (ScrollViewer viewer in found)
		{
			areas.Add(Math.Max(0.0, viewer.Bounds.Width) * Math.Max(0.0, viewer.Bounds.Height));
			canScroll.Add(CanScroll(viewer, delta));
		}
		int index = LayoutWheelRouting.Widest(areas, canScroll);
		return ((index >= 0 && index < found.Count) ? found[index] : null);
	}

	private static void CollectScrollViewers(Visual? node, List<ScrollViewer> into, int depth)
	{
		if (node == null || depth > 60 || into.Count > 256)
		{
			return;
		}
		if (!node.IsVisible)
		{
			return;
		}
		if (node is ScrollViewer viewer)
		{
			into.Add(viewer);
		}
		try
		{
			foreach (Visual child in node.GetVisualChildren())
			{
				CollectScrollViewers(child, into, depth + 1);
			}
		}
		catch
		{
			// 模板正在重建:跳过这一层
		}
	}
}