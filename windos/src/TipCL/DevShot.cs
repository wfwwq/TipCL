using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TipCL.Pages;
using TipCL.Pages.Layout;
using TipCL.Services;
using WpfPath = System.Windows.Shapes.Path;
using WpfRect = System.Windows.Shapes.Rectangle;

namespace TipCL;

// 开发截图工具（dev-only）：--shot <pageId> --out <png> --theme <light|dark> [--size 默认档|WxH]
// 启动主窗口 → 应用主题 → 导航到指定页 → RenderTargetBitmap 存 PNG → 退出。
// 页面数据走真实 BridgeClient；"design-sheet" 为 WP1 样张页专用 id。
// R34 WP-L0 布局 v3 开关（参数解析见下方 DevShotArgs，纯函数、有单测）：
//   --edit                 截图前让目标页的区块宿主进编辑态
//   --select <blockId>     模拟选中某块（拍浮动标签和手柄；隐含 --edit）
//   --pop layout-drawer    打开「添加」抽屉（隐含 --edit）
//   --expand advanced      展开「高级」一类的折叠区（设置 → 布局）
//   --shot settings:<子页> 设置页子视图（general / account / java / network / servers / playtime / about / layout）；
//                          java / servers / playtime / feedback 不是顶栏页，按别名落到设置子视图
public static class DevShot
{
	// 合并后接线用的钩子（GOAL-layout-v3 §6.4 的并行约定：本文件不直接引用别的包新增的成员）。
	// 返回 true = 已处理；为 null 或返回 false 时走下面的兜底实现。
	/// <summary>选中某块：(宿主, 块 id) → 是否成功。</summary>
	public static Func<BlockHost, string, bool>? SelectHook;

	/// <summary>打开「添加」抽屉：宿主 → 是否成功。抽屉须是窗口内的元素（Popup 是独立 HWND，拍不到）。</summary>
	public static Func<BlockHost, bool>? LayoutDrawerHook;

	/// <summary>展开折叠区：(窗口, 区名，如 "advanced") → 是否成功。</summary>
	public static Func<Window, string, bool>? ExpandHook;

	public static int Run(string[] args)
	{
		DevShotArgs la = DevShotArgs.Parse(args);
		string pageId = la.ShotId;
		string outPath = ArgVal(args, "--out") ?? Path.GetFullPath($"shot-{Sanitize(pageId)}.png");
		bool dark = !string.Equals(ArgVal(args, "--theme"), "light", StringComparison.OrdinalIgnoreCase);
		double w = -1.0;
		double h = -1.0;
		string size = ArgVal(args, "--size") ?? "";
		if (size != "" && size != "default")
		{
			string[] parts = size.Split('x', 'X');
			if (parts.Length == 2 && double.TryParse(parts[0], out w) && double.TryParse(parts[1], out h))
			{
				w = Math.Max(320.0, w);
				h = Math.Max(240.0, h);
			}
			else
			{
				w = -1.0;
				h = -1.0;
			}
		}
		Motion.Enabled = false;
		MainWindow win = new MainWindow();
		win.ShowActivated = false;
		win.ShowInTaskbar = false;
		win.WindowStartupLocation = WindowStartupLocation.Manual;
		win.Left = SystemParameters.VirtualScreenLeft - win.Width - 200.0;
		win.Top = SystemParameters.VirtualScreenTop - win.Height - 200.0;
		win.Show();
		Application.Current.Dispatcher.InvokeAsync((Func<Task>)async delegate
		{
			int code = 0;
			try
			{
				await DriveAsync(win, la, dark, outPath, w, h, la.Pop, args);
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine("DevShot FAIL: " + ex);
				code = 3;
			}
			// 强制进程退出：BridgeHost 子进程等可能让常规 Shutdown 挂住，残留实例会锁住 obj 里的 pdb/exe
			Environment.Exit(code);
		}, DispatcherPriority.ApplicationIdle);
		return 0;
	}

	private static async Task DriveAsync(MainWindow win, DevShotArgs la, bool dark, string outPath, double w, double h, string pop, string[] args)
	{
		string pageId = la.NavId;
		try
		{
			await Task.WhenAny(win.BridgeSettled, Task.Delay(40000));
		}
		catch
		{
		}
		// FeedbackConsent 在桥就绪后延迟 400ms 弹出；等一下再清弹窗，保证截图不被首启弹窗污染
		await Task.Delay(700);
		await DismissWizard(win);
		if (args?.Contains("--probe") == true)
		{
			await ProbeAsync();
		}
		await win.Dispatcher.InvokeAsync(delegate
		{
			if (w > 0.0)
			{
				win.MinWidth = w;
				win.MinHeight = h;
				win.Width = w;
				win.Height = h;
			}
			// 设置子视图：SettingsPage 建页时读 TIPCL_DEV_SETTINGS_VIEW（等于视图号才生效），所以要在导航前设好
			if (la.SettingsView >= 0)
			{
				Environment.SetEnvironmentVariable("TIPCL_DEV_SETTINGS_VIEW", la.SettingsView.ToString());
			}
			win.Navigate(pageId, instant: true);
		}, DispatcherPriority.ContextIdle);
		PageBase page = win.GetPage(pageId);
		if (page != null)
		{
			Task load = page.EnsureLoadedAsync();
			if (await Task.WhenAny(load, Task.Delay(15000)) != load)
			{
				Console.Error.WriteLine("DevShot: 页面加载超时 " + pageId);
			}
			else
			{
				await load;
			}
			await win.Dispatcher.InvokeAsync(delegate
			{
				page.OnShown();
			}, DispatcherPriority.ContextIdle);
		}
		// --prefer <inst>|<ver>：模拟实例库「设为默认/双击版本」跳启动页的用户路径（ver 可空）
		string prefer = ArgVal(args, "--prefer") ?? "";
		if (prefer.Length > 0 && page is LaunchPage lp2)
		{
			string[] pp = prefer.Split('|');
			System.Reflection.FieldInfo? verFld = typeof(LaunchPage).GetField("_ver", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			var cb = (System.Windows.Controls.ComboBox?)verFld?.GetValue(lp2);
			lp2.PreferInstance(pp[0], (pp.Length > 1 && pp[1].Length > 0) ? pp[1] : null);
			int last = -1;
			for (int i = 0; i < 45; i++)
			{
				await Task.Delay(100);
				int n = -2;
				string th = "";
				await win.Dispatcher.InvokeAsync(delegate
				{
					n = cb?.Items.Count ?? -3;
					th = Environment.CurrentManagedThreadId.ToString();
				}, DispatcherPriority.Send);
				if (n != last)
				{
					Console.WriteLine("DBG POLL t=" + i + " count=" + n + " thread=" + th);
					if (n == 0 && last > 0)
					{
						Console.WriteLine("DROP STACK:\n" + Environment.StackTrace);
					}
					last = n;
				}
			}
		}
		// --pop plugin-install：插件安装权限确认弹窗（WP-S11）。Dlg 是窗口内浮层，RenderTargetBitmap 拍得到
		if (pop == "plugin-install")
		{
			await win.Dispatcher.InvokeAsync(delegate
			{
				JsonElement p = JsonDocument.Parse("""{"plugin":"fat-fish","name":"大肥鱼玩 Minecraft","version":"1.4.2","action":"install","runtime":"node","verified":false,"permissions":["launcher","ai.tools","ai.llm","credentials","ui","ui.overlay"],"newPermissions":["ai.llm","ui.overlay"]}""").RootElement;
				_ = TipCL.Pages.Plugins.PluginConfirm.ShowAsync(p);
			}, DispatcherPriority.ContextIdle);
			await Task.Delay(600);
		}
		// --pop <config|instances|news>：浮层打开态。Popup 是独立 HWND，RenderTargetBitmap 拍不到，
		// 把浮层内容临时挂进页面根 Grid 模拟浮层位置后再渲染。
		if (pop.Length > 0 && pop != DevShotArgs.PopLayoutDrawer && page is LaunchPage lp)
		{
			await win.Dispatcher.InvokeAsync(delegate
			{
				lp.OpenPopupForDevShot(pop);
			}, DispatcherPriority.ContextIdle);
			await Task.Delay(500);
			await win.Dispatcher.InvokeAsync(delegate
			{
				FrameworkElement? el = lp.DetachPopupForShot(pop);
				if (el != null && lp.Content is Grid root)
				{
					Grid host = new Grid();
					switch (pop)
					{
					case "config":
						host.HorizontalAlignment = HorizontalAlignment.Right;
						host.VerticalAlignment = VerticalAlignment.Top;
						host.Margin = new Thickness(0.0, 56.0, 16.0, 0.0);
						break;
					case "instances":
						host.HorizontalAlignment = HorizontalAlignment.Left;
						host.VerticalAlignment = VerticalAlignment.Center;
						host.Margin = new Thickness(56.0, 0.0, 0.0, 24.0);
						break;
					default:
						host.HorizontalAlignment = HorizontalAlignment.Center;
						host.VerticalAlignment = VerticalAlignment.Bottom;
						host.Margin = new Thickness(0.0, 0.0, 0.0, 44.0);
						break;
					}
					host.Children.Add(el);
					root.Children.Add(host);
				}
			}, DispatcherPriority.ContextIdle);
			await Task.Delay(250);
		}
		// --detail：实例详情页未注册导航（实例库「管理」走对话框），dev-shot 专用入口（WP-S8b）
		if (args?.Contains("--detail") == true)
		{
			var dclient = AppServices.Client;
			for (int i = 0; i < 50 && dclient == null; i++)
			{
				await Task.Delay(200);
				dclient = AppServices.Client;
			}
			var insts = dclient != null ? await dclient.TryCallAsync<System.Collections.Generic.List<TipCL.Models.InstanceInfo>>("get_instances", null, new System.Collections.Generic.List<TipCL.Models.InstanceInfo>()) : null;
			string dname = ArgVal(args, "--inst") ?? insts?.FirstOrDefault()?.Name ?? "";
			await win.Dispatcher.InvokeAsync(delegate
			{
				var host = (System.Windows.Controls.ContentControl?)typeof(MainWindow).GetField("PageHost", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(win);
				if (host != null && dname.Length > 0)
				{
					host.Content = new TipCL.Pages.InstanceDetailPage(new TipCL.Models.InstanceInfo
					{
						Name = dname
					}, delegate
					{
					});
				}
			}, DispatcherPriority.ContextIdle);
			await Task.Delay(1500);
			// --forceshared：注入一条 isolated=false 的假版本行，拍共享模式分段隐藏 + 灰字说明（WP-S8b）
			if (args.Contains("--forceshared"))
			{
				await win.Dispatcher.InvokeAsync(delegate
				{
					var phost = (System.Windows.Controls.ContentControl?)typeof(MainWindow).GetField("PageHost", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(win);
					var det = phost?.Content as TipCL.Pages.InstanceDetailPage;
					var vfld = typeof(TipCL.Pages.InstanceDetailPage).GetField("_vers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
					var cfld = typeof(TipCL.Pages.InstanceDetailPage).GetField("_verBox", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
					if (det != null && vfld?.GetValue(det) is System.Collections.Generic.List<TipCL.Models.VersionRowDto> lst && cfld?.GetValue(det) is System.Windows.Controls.ComboBox cb2)
					{
						lst.Insert(0, new TipCL.Models.VersionRowDto { Id = "26.3", IsolationLabel = "共用", Isolated = false });
						cb2.Items.Clear();
						foreach (var r in lst)
						{
							cb2.Items.Add(string.IsNullOrEmpty(r.IsolationLabel) ? r.Id : (r.Id + " · " + r.IsolationLabel));
						}
						cb2.SelectedIndex = 0;
					}
				}, DispatcherPriority.ContextIdle);
				await Task.Delay(1200);
			}
		}
		// --subview plugin-settings：插件页「⋯ → 插件设置」子视图（dev-shot 专用入口，Context menus 是独立弹层拍不到）
		string subview = ArgVal(args, "--subview") ?? "";
		if (subview == "plugin-settings" && page is TipCL.Pages.Plugins.PluginsPage plugPage)
		{
			plugPage.ShowSettingsSubViewForDevShot();
			await Task.Delay(1500);
		}
		// R34 WP-L0：设计样张页追加「布局 v3 编辑态」一节（样张页本体在 Pages/DesignSheetPage.cs，不归本包；
		// 合并后可把 LayoutV3Sheet() 挂进 DesignSheetPage.Sheet() 并删掉这段注入）。--no-v3-sheet 关掉。
		if (la.ShotId == "design-sheet" && page != null && args?.Contains("--no-v3-sheet") != true)
		{
			await win.Dispatcher.InvokeAsync(delegate
			{
				InjectLayoutV3Sheet(page);
			}, DispatcherPriority.ContextIdle);
			await Task.Delay(300);
		}
		await ApplyLayoutShotAsync(win, la);
		// 后端主题同步可能晚到，渲染前再钉一次主题
		await win.Dispatcher.InvokeAsync(delegate
		{
			win.ApplyThemeState(dark, persistLocal: false);
		}, DispatcherPriority.ContextIdle);
		await Task.Delay(250);
		Render(win, outPath);
		Console.WriteLine("DevShot OK: " + outPath);
	}

	// ————— R34 WP-L0：布局 v3 编辑态截图驱动（--edit / --select / --pop layout-drawer / --expand） —————

	private static async Task ApplyLayoutShotAsync(MainWindow win, DevShotArgs la)
	{
		if (la.Edit)
		{
			BlockHost? host = null;
			await win.Dispatcher.InvokeAsync(delegate
			{
				win.UpdateLayout();
				host = PickHost(win, la);
				if (host == null)
				{
					Console.Error.WriteLine("DevShot: --edit 找不到区块宿主（" + la.ShotId + "）");
				}
				else if (!host.IsEditing)
				{
					host.EnterEdit();
				}
			}, DispatcherPriority.ContextIdle);
			await Task.Delay(400);
			if (host != null && !string.IsNullOrEmpty(la.Select))
			{
				await win.Dispatcher.InvokeAsync(delegate
				{
					SelectBlock(host, la.Select!);
				}, DispatcherPriority.ContextIdle);
				await Task.Delay(250);
			}
			if (host != null && la.LayoutDrawer)
			{
				await win.Dispatcher.InvokeAsync(delegate
				{
					OpenLayoutDrawer(host);
				}, DispatcherPriority.ContextIdle);
				// 抽屉滑入动画（Motion.Enabled 已关，正常是瞬时；留余量给布局）
				await Task.Delay(600);
			}
		}
		if (!string.IsNullOrEmpty(la.Expand))
		{
			await win.Dispatcher.InvokeAsync(delegate
			{
				win.UpdateLayout();
				ExpandSection(win, la.Expand!);
			}, DispatcherPriority.ContextIdle);
			await Task.Delay(500);
		}
	}

	// 目标宿主：先按 id 精确匹配，再按「导航 id + 冒号前缀」匹配（library → library:main）；
	// 都没有时只在不指定宿主 id 的情况下退回第一个可见宿主
	private static BlockHost? PickHost(Window win, DevShotArgs la)
	{
		BlockHost? best = null;
		int bestScore = 0;
		BlockHost? firstVisible = null;
		foreach (BlockHost h in Descendants<BlockHost>(win))
		{
			if (!h.IsVisible)
			{
				continue;
			}
			firstVisible ??= h;
			int score = la.MatchScore(h.PageId);
			if (score > bestScore)
			{
				best = h;
				bestScore = score;
			}
		}
		if (best == null && la.HostId == null && la.SettingsView < 0 && firstVisible != null)
		{
			Console.Error.WriteLine("DevShot: 没有 id 匹配的宿主，改用第一个可见宿主 " + firstVisible.PageId);
			return firstVisible;
		}
		return best;
	}

	private static bool SelectBlock(BlockHost host, string blockId)
	{
		if (SelectHook != null)
		{
			try
			{
				if (SelectHook(host, blockId))
				{
					return true;
				}
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine("DevShot: SelectHook 失败 " + ex.Message);
			}
		}
		// TODO(R34 合并)：LayoutEditor 目前没有公开的「选中某块」入口，这里用反射调它自己的选择模型
		// (_session.Select + UpdateSelectionVisual，都是 r34/base 里已有的成员)。WP-L12 改了这两个名字时
		// 这里只会打印失败、不会抛异常；届时请给 LayoutEditor 加一个 internal 入口并接到 SelectHook。
		try
		{
			LayoutEditor editor = host.Editor;
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
			if (typeof(LayoutEditor).GetField("_cards", flags)?.GetValue(editor) is System.Collections.IDictionary cards && !cards.Contains(blockId))
			{
				Console.Error.WriteLine("DevShot: --select 的块 id 不在当前页：" + blockId);
			}
			if (typeof(LayoutEditor).GetField("_session", flags)?.GetValue(editor) is LayoutEditSession session)
			{
				session.Select(blockId, false);
				MethodInfo? refresh = typeof(LayoutEditor).GetMethod("UpdateSelectionVisual", flags, null, Type.EmptyTypes, null);
				if (refresh != null)
				{
					refresh.Invoke(editor, null);
					return true;
				}
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine("DevShot: --select 失败 " + ex.Message);
			return false;
		}
		Console.Error.WriteLine("DevShot: --select 没有可用的选中入口（见 DevShot.SelectHook）");
		return false;
	}

	private static bool OpenLayoutDrawer(BlockHost host)
	{
		if (LayoutDrawerHook != null)
		{
			try
			{
				if (LayoutDrawerHook(host))
				{
					return true;
				}
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine("DevShot: LayoutDrawerHook 失败 " + ex.Message);
			}
		}
		// TODO(R34 合并)：「添加」抽屉由 WP-L12 新建（LayoutAddDrawer.cs），本分支看不到它的入口。
		// 先按几个常见的无参方法名找一次；找不到就退回 R34 之前底部托盘的「+ 自建区块」展开态，并打印提示。
		// 合并后请把真正的入口接到 LayoutDrawerHook。
		try
		{
			LayoutEditor editor = host.Editor;
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
			foreach (string name in new string[3] { "OpenAddDrawer", "ShowAddDrawer", "OpenDrawer" })
			{
				MethodInfo? m = typeof(LayoutEditor).GetMethod(name, flags, null, Type.EmptyTypes, null);
				if (m != null)
				{
					m.Invoke(editor, null);
					return true;
				}
			}
			FieldInfo? open = typeof(LayoutEditor).GetField("_trayAddOpen", flags);
			MethodInfo? refresh = typeof(LayoutEditor).GetMethod("RefreshTray", flags, null, Type.EmptyTypes, null);
			if (open != null && refresh != null)
			{
				open.SetValue(editor, true);
				refresh.Invoke(editor, null);
				Console.Error.WriteLine("DevShot: 抽屉入口未接线，退回旧版托盘的展开态");
				return true;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine("DevShot: --pop layout-drawer 失败 " + ex.Message);
			return false;
		}
		Console.Error.WriteLine("DevShot: --pop layout-drawer 没有可用入口（见 DevShot.LayoutDrawerHook）");
		return false;
	}

	// 折叠区按标题文字找（GOAL-layout-v3 §5.4：「高级」组的卡片标题是「分栏、网页与按钮组件、布局文件」）：
	// 先找 Expander，再找内容里含这段文字的按钮（ToggleButton 置 IsChecked，普通按钮发 Click）
	private static bool ExpandSection(Window win, string key)
	{
		if (ExpandHook != null)
		{
			try
			{
				if (ExpandHook(win, key))
				{
					return true;
				}
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine("DevShot: ExpandHook 失败 " + ex.Message);
			}
		}
		string[] labels = ((key == DevShotArgs.ExpandAdvanced) ? new string[2]
		{
			I18n.L("分栏、网页与按钮组件、布局文件"),
			I18n.L("高级")
		} : new string[1] { key });
		foreach (string label in labels)
		{
			foreach (Expander ex in Descendants<Expander>(win))
			{
				if (ex.IsVisible && HasText(ex.Header, label))
				{
					ex.IsExpanded = true;
					ex.BringIntoView();
					return true;
				}
			}
			foreach (ButtonBase b in Descendants<ButtonBase>(win))
			{
				if (!b.IsVisible || !HasText(b.Content, label))
				{
					continue;
				}
				if (b is ToggleButton tb)
				{
					if (tb.IsChecked != true)
					{
						tb.IsChecked = true;
					}
				}
				else
				{
					b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
				}
				b.BringIntoView();
				return true;
			}
		}
		Console.Error.WriteLine("DevShot: --expand 找不到折叠区 " + key + "（见 DevShot.ExpandHook）");
		return false;
	}

	private static bool HasText(object? content, string label)
	{
		if (content is string s)
		{
			return s.Trim() == label;
		}
		if (content is TextBlock t)
		{
			return t.Text.Trim() == label;
		}
		if (content is DependencyObject d)
		{
			foreach (TextBlock tb in Descendants<TextBlock>(d))
			{
				if (tb.Text.Trim() == label)
				{
					return true;
				}
			}
		}
		return false;
	}

	// 可视树 + 逻辑树并集（刚建好、还没进可视树的内容走逻辑树也能找到）
	private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
	{
		HashSet<DependencyObject> seen = new HashSet<DependencyObject>();
		Stack<DependencyObject> todo = new Stack<DependencyObject>();
		todo.Push(root);
		while (todo.Count > 0)
		{
			DependencyObject cur = todo.Pop();
			if (!seen.Add(cur))
			{
				continue;
			}
			if (!ReferenceEquals(cur, root) && cur is T hit)
			{
				yield return hit;
			}
			if (cur is Visual || cur is System.Windows.Media.Media3D.Visual3D)
			{
				int n = VisualTreeHelper.GetChildrenCount(cur);
				for (int i = n - 1; i >= 0; i--)
				{
					todo.Push(VisualTreeHelper.GetChild(cur, i));
				}
			}
			foreach (object child in LogicalTreeHelper.GetChildren(cur))
			{
				if (child is DependencyObject dc)
				{
					todo.Push(dc);
				}
			}
		}
	}

	// ————— R34 WP-L0：设计样张「布局 v3 编辑态」一节 —————

	private static void InjectLayoutV3Sheet(PageBase page)
	{
		foreach (SPanel p in Descendants<SPanel>(page))
		{
			if (p.Orientation == Orientation.Vertical && p.Children.Count >= 5)
			{
				p.Children.Add(Ui.Card(Ui.V(8.0, Ui.Muted("布局 v3 编辑态（R34 WP-L0：Layout.* 样式 / Geo.* 图标 / B.Edit* 画刷）"), LayoutV3Sheet()), 12.0));
				return;
			}
		}
		Console.Error.WriteLine("DevShot: 样张页结构变了，没找到可追加的纵向面板");
	}

	/// <summary>
	/// 布局 v3 编辑态样张：编辑条、块的四种状态、落点框与列底纹、「添加」抽屉、六个矢量图标、
	/// 深色玻璃版（启动页）和琥珀提示。dev-only，文案不走词表（与 DesignSheetPage 一致）。
	/// 所有键都用 TryFindResource 取，缺键时元素照样能建出来（只是没有样式），不会抛异常。
	/// </summary>
	internal static UIElement LayoutV3Sheet()
	{
		Border glass = new Border
		{
			CornerRadius = new CornerRadius(12.0),
			Padding = new Thickness(16.0),
			Background = new LinearGradientBrush(Color.FromRgb(42, 51, 70), Color.FromRgb(90, 106, 132), 0.0),
			Child = Ui.V(14.0, SheetEditBar(onDark: true), SheetBlocks(onDark: true), SheetHero())
		};
		return Ui.V(14.0,
			Ui.Small("编辑条（Layout.EditBar / SegRail / SegItem / EditHint / EditBarSep / Count）"),
			SheetEditBar(onDark: false),
			Ui.Small("区块：常态 / 悬停 / 选中（含手柄与「N / 列数」提示）/ 拖动落点（Layout.EditOutline / EditLabel / IconAct / ResizeGrip / EditTip / DropBox / ColShade）"),
			SheetBlocks(onDark: false),
			Ui.Small("「添加」抽屉（Layout.Drawer / DrawerTitle / DrawerSection / DrawerItem / DrawerIcon / DrawerEmpty / DrawerNote）"),
			SheetDrawer(),
			Ui.Small("矢量图标 Geo.Eye / EyeOff / Lock / Undo / Redo / Grip（16 视框，14px 显示）"),
			SheetIcons(),
			Ui.Small("深色玻璃（启动页英雄图上：…OnDark 变体、LockNote、B.EditDim 压暗）"),
			glass,
			Ui.Small("琥珀提示（Layout.WarnNote）"),
			new Border
			{
				Style = Sty("Layout.WarnNote"),
				HorizontalAlignment = HorizontalAlignment.Left,
				Child = new TextBlock
				{
					Text = "导入的布局里如果带网页或按钮组件，仍然默认关闭，需要你逐个打开"
				}
			});
	}

	private static Style? Sty(string key)
	{
		return Application.Current?.TryFindResource(key) as Style;
	}

	private static Geometry? GeoOf(string key)
	{
		return Application.Current?.TryFindResource(key) as Geometry;
	}

	private static WpfPath Glyph(string geo, string style = "Layout.Glyph")
	{
		return new WpfPath
		{
			Style = Sty(style),
			Data = GeoOf(geo)
		};
	}

	private static string Suffix(bool onDark)
	{
		return onDark ? ".OnDark" : "";
	}

	private static UIElement SheetEditBar(bool onDark)
	{
		Border rail = new Border
		{
			Style = Sty("Layout.SegRail"),
			Child = Ui.H(2.0,
				new ToggleButton
				{
					Style = Sty("Layout.SegItem"),
					Content = Ui.H(6.0, Ui.Glyph(Ico.List, 12.0), new TextBlock { Text = "列表" })
				},
				new ToggleButton
				{
					Style = Sty("Layout.SegItem"),
					IsChecked = true,
					Content = Ui.H(6.0, Ui.Glyph(Ico.Grid, 12.0), new TextBlock { Text = "网格" })
				})
		};
		Button undo = Ui.IconBtn("", "撤销 (Ctrl+Z)");
		undo.Content = Glyph("Geo.Undo");
		Button redo = Ui.IconBtn("", "重做 (Ctrl+Y)");
		redo.Content = Glyph("Geo.Redo");
		redo.IsEnabled = false;
		Grid add = new Grid
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		add.Children.Add(Ui.Btn("添加", BtnKind.Soft, null, Ico.Add));
		add.Children.Add(new Border
		{
			Style = Sty("Layout.Count"),
			Margin = new Thickness(0.0, -6.0, -6.0, 0.0),
			Child = new TextBlock
			{
				Text = "2",
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			}
		});
		SPanel right = Ui.H(8.0, undo, redo, new Border { Style = Sty("Layout.EditBarSep" + Suffix(onDark)) }, add, Ui.Btn("重置本页", BtnKind.Normal), Ui.Btn("完成", BtnKind.Primary), Ui.IconBtn(Ico.More, "更多"));
		right.VerticalAlignment = VerticalAlignment.Center;
		DockPanel dock = new DockPanel
		{
			LastChildFill = true
		};
		TextBlock title = new TextBlock
		{
			Text = "编辑「下载」",
			Style = Sty("Layout.EditBarTitle"),
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
		};
		rail.Margin = new Thickness(0.0, 0.0, 8.0, 0.0);
		DockPanel.SetDock(title, Dock.Left);
		DockPanel.SetDock(rail, Dock.Left);
		DockPanel.SetDock(right, Dock.Right);
		dock.Children.Add(title);
		dock.Children.Add(rail);
		dock.Children.Add(right);
		dock.Children.Add(new TextBlock
		{
			Text = "拖动换位置 · 拖右边缘改宽度",
			Style = Sty("Layout.EditHint" + Suffix(onDark))
		});
		return new Border
		{
			Style = Sty("Layout.EditBar" + Suffix(onDark)),
			Child = dock
		};
	}

	private static UIElement SheetBlocks(bool onDark)
	{
		WrapPanel wrap = new WrapPanel();
		wrap.Children.Add(SheetBlock("", "版本筛选", component: false, onDark));
		wrap.Children.Add(SheetBlock("hover", "便签", component: true, onDark));
		wrap.Children.Add(SheetBlock("selected", "版本列表", component: false, onDark));
		Grid drop = new Grid
		{
			Width = 180.0,
			Height = 96.0,
			Margin = new Thickness(14.0, 22.0, 14.0, 14.0)
		};
		Grid cols = Ui.G(null, "*,*,*");
		for (int i = 0; i < 3; i++)
		{
			cols.Add(new Border
			{
				Style = Sty("Layout.ColShade" + Suffix(onDark)),
				Margin = new Thickness(3.0, -10.0, 3.0, -10.0)
			}, 0, i);
		}
		drop.Children.Add(cols);
		drop.Children.Add(new WpfRect { Style = Sty("Layout.DropBox" + Suffix(onDark)) });
		wrap.Children.Add(drop);
		return wrap;
	}

	private static UIElement SheetBlock(string state, string title, bool component, bool onDark)
	{
		Grid cell = new Grid
		{
			Width = 180.0,
			Height = 96.0,
			Margin = new Thickness(14.0, 22.0, (state == "selected") ? 70.0 : 14.0, 14.0)
		};
		Border card = new Border
		{
			CornerRadius = new CornerRadius(12.0),
			Padding = new Thickness(12.0),
			BorderThickness = new Thickness(1.0),
			Child = new TextBlock
			{
				Text = "区块内容",
				Opacity = 0.6
			}
		};
		card.SetResourceReference(Border.BackgroundProperty, onDark ? "B.EditGlass" : "B.Card");
		card.SetResourceReference(Border.BorderBrushProperty, onDark ? "B.EditGlassLine" : "B.CardBorder");
		card.SetResourceReference(TextElement.ForegroundProperty, onDark ? "B.OnEditGlass" : "B.Ink");
		cell.Children.Add(card);
		cell.Children.Add(new WpfRect
		{
			Style = Sty("Layout.EditOutline" + Suffix(onDark)),
			Tag = state
		});
		if (state.Length == 0)
		{
			return cell;
		}
		SPanel lab = Ui.H(6.0, new WpfPath { Style = Sty("Layout.Grip" + Suffix(onDark)) }, new TextBlock
		{
			Text = title,
			VerticalAlignment = VerticalAlignment.Center
		});
		if (component)
		{
			lab.Children.Add(new TextBlock
			{
				Text = "· 组件",
				Style = Sty("Layout.EditLabelNote" + Suffix(onDark))
			});
		}
		cell.Children.Add(new Border
		{
			Style = Sty("Layout.EditLabel" + Suffix(onDark)),
			Child = lab
		});
		Button act = new Button
		{
			Style = Sty((component ? "Layout.IconAct.Danger" : "Layout.IconAct") + Suffix(onDark))
		};
		if (component)
		{
			act.Content = Ico.Trash;
			act.ToolTip = "删除";
		}
		else
		{
			act.Content = Glyph("Geo.EyeOff");
			act.ToolTip = "隐藏（可在「添加」里找回）";
		}
		cell.Children.Add(new Border
		{
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(0.0, -14.0, 8.0, 0.0),
			Child = act
		});
		if (state == "selected")
		{
			cell.Children.Add(new Thumb
			{
				Style = Sty("Layout.ResizeGrip"),
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0.0, 0.0, -9.0, 0.0)
			});
			cell.Children.Add(new Thumb
			{
				Style = Sty("Layout.ResizeGrip.Bottom"),
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Bottom,
				Margin = new Thickness(0.0, 0.0, 0.0, -9.0)
			});
			cell.Children.Add(new Border
			{
				Style = Sty("Layout.EditTip"),
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Top,
				Margin = new Thickness(0.0, 36.0, -62.0, 0.0),
				Child = new TextBlock { Text = "4 / 12" }
			});
		}
		return cell;
	}

	private static UIElement SheetDrawer()
	{
		Button close = Ui.IconBtn(Ico.Close, "关闭");
		DockPanel head = new DockPanel
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 4.0)
		};
		DockPanel.SetDock(close, Dock.Right);
		head.Children.Add(close);
		head.Children.Add(new TextBlock
		{
			Text = "添加到「下载」",
			Style = Sty("Layout.DrawerTitle")
		});
		Grid empty = new Grid
		{
			Height = 40.0
		};
		empty.Children.Add(new WpfRect { Style = Sty("Layout.DrawerEmpty") });
		empty.Children.Add(Ui.Muted("没有隐藏的内容").Center().VCenter());
		Button added = Ui.Btn("已添加", BtnKind.Normal);
		added.IsEnabled = false;
		SPanel body = Ui.V(0.0,
			head,
			new TextBlock { Text = "已隐藏的内容", Style = Sty("Layout.DrawerSection") },
			SheetDrawerItem(Ico.List, "版本列表", "可安装的版本", Ui.Btn("显示", BtnKind.Normal)),
			empty,
			new TextBlock { Text = "组件", Style = Sty("Layout.DrawerSection") },
			SheetDrawerItem(Ico.Add, "便签", "记一句话，可以加多张", Ui.Btn("添加", BtnKind.Soft)),
			SheetDrawerItem(Ico.Clock, "时钟", "当前时间和日期", added),
			new Border
			{
				Style = Sty("Layout.DrawerNote"),
				Child = new TextBlock
				{
					Text = "想要网页或按钮组件？到「设置 → 布局 → 高级」里打开。",
					TextWrapping = TextWrapping.Wrap
				}
			});
		body.Margin = new Thickness(16.0, 14.0, 12.0, 16.0);
		return new Border
		{
			Style = Sty("Layout.Drawer"),
			HorizontalAlignment = HorizontalAlignment.Left,
			Margin = new Thickness(24.0, 0.0, 0.0, 0.0),
			Child = body
		};
	}

	private static UIElement SheetDrawerItem(string glyph, string name, string desc, Button action)
	{
		Grid g = Ui.G(null, "40,*,Auto");
		g.Add(new Border
		{
			Style = Sty("Layout.DrawerIcon"),
			Child = Ui.Glyph(glyph, 16.0).Center()
		}, 0);
		g.Add(Ui.V(0.0, Ui.Txt(name, 13.0, bold: true), Ui.Muted(desc)).VCenter().M(10.0, 0.0, 10.0, 0.0), 0, 1);
		g.Add(action.VCenter(), 0, 2);
		return new Border
		{
			Style = Sty("Layout.DrawerItem"),
			Child = g
		};
	}

	private static UIElement SheetIcons()
	{
		SPanel row = Ui.H(16.0);
		foreach (string key in new string[6] { "Geo.Eye", "Geo.EyeOff", "Geo.Lock", "Geo.Undo", "Geo.Redo", "Geo.Grip" })
		{
			WpfPath p = (key == "Geo.Grip") ? new WpfPath { Style = Sty("Layout.Grip") } : Glyph(key);
			if (key != "Geo.Grip")
			{
				p.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "B.Ink");
			}
			Border box = new Border
			{
				Width = 30.0,
				Height = 30.0,
				CornerRadius = new CornerRadius(8.0),
				BorderThickness = new Thickness(1.0),
				Child = p
			};
			p.HorizontalAlignment = HorizontalAlignment.Center;
			box.SetResourceReference(Border.BackgroundProperty, "B.EditBarBg");
			box.SetResourceReference(Border.BorderBrushProperty, "B.EditOutline");
			row.Children.Add(Ui.V(4.0, box, Ui.Small(key.Substring(4)).Center()));
		}
		return row;
	}

	private static UIElement SheetHero()
	{
		Grid hero = new Grid
		{
			Height = 90.0
		};
		hero.Children.Add(new TextBlock
		{
			Text = "英雄区（编辑态压暗、不可拖动）",
			FontSize = 15.0,
			FontWeight = FontWeights.ExtraBold,
			Foreground = Brushes.White,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(16.0, 0.0, 0.0, 0.0)
		});
		WpfRect dim = new WpfRect
		{
			RadiusX = 12.0,
			RadiusY = 12.0,
			IsHitTestVisible = false
		};
		dim.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "B.EditDim");
		hero.Children.Add(dim);
		WpfPath lockIcon = Glyph("Geo.Lock", "Layout.Glyph.Small");
		lockIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "B.OnEditGlass");
		hero.Children.Add(new Border
		{
			Style = Sty("Layout.LockNote"),
			Margin = new Thickness(0.0, 10.0, 10.0, 0.0),
			Child = Ui.H(6.0, lockIcon, new TextBlock
			{
				Text = "主区域固定，不能拖动或隐藏",
				VerticalAlignment = VerticalAlignment.Center
			})
		});
		return hero;
	}

	// dev-only：桥实测——对比 get_installed_versions 与 get_version_rows 的返回（只读）
	private static async Task ProbeAsync()
	{
		var client = AppServices.Client;
		for (int i = 0; i < 50 && client == null; i++)
		{
			await Task.Delay(200);
			client = AppServices.Client;
		}
		if (client == null)
		{
			Console.Error.WriteLine("PROBE: bridge client 未就绪");
			return;
		}
		try
		{
			var insts = await client.TryCallAsync<System.Collections.Generic.List<System.Text.Json.JsonElement>>("get_instances", null, new System.Collections.Generic.List<System.Text.Json.JsonElement>());
			Console.WriteLine("PROBE get_instances = " + System.Text.Json.JsonSerializer.Serialize(insts));
			var cfg = await client.TryCallAsync<System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>>("get_settings", null, new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>());
			Console.WriteLine("PROBE default_instance = " + ((cfg != null && cfg.TryGetValue("default_instance", out var di)) ? di.ToString() : "<missing>"));
			string instName = (cfg != null && cfg.TryGetValue("default_instance", out var di2) && di2.ValueKind == System.Text.Json.JsonValueKind.String && di2.GetString()!.Length > 0)
				? di2.GetString()!
				: ".minecraft";
			var iv = await client.TryCallAsync<System.Collections.Generic.List<System.Text.Json.JsonElement>>("get_installed_versions", new { instance = instName }, new System.Collections.Generic.List<System.Text.Json.JsonElement>());
			Console.WriteLine($"PROBE get_installed_versions(instance={instName}) = " + System.Text.Json.JsonSerializer.Serialize(iv));
			var ivAll = await client.TryCallAsync<System.Collections.Generic.List<System.Text.Json.JsonElement>>("get_installed_versions", new { }, new System.Collections.Generic.List<System.Text.Json.JsonElement>());
			Console.WriteLine("PROBE get_installed_versions() = " + System.Text.Json.JsonSerializer.Serialize(ivAll));
			var vr = await client.TryCallAsync<System.Collections.Generic.List<System.Text.Json.JsonElement>>("get_version_rows", null, new System.Collections.Generic.List<System.Text.Json.JsonElement>());
			Console.WriteLine("PROBE get_version_rows = " + System.Text.Json.JsonSerializer.Serialize(vr));
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine("PROBE FAIL: " + ex);
		}
	}

	// 首启向导/诊断同意弹窗会盖住截图：找到「跳过向导」或「暂不同意」按钮以程序方式点掉
	// （等价用户点击，不碰业务代码；consent 选「暂不同意」= 不上传，开发截图的安全默认）
	private static async Task DismissWizard(MainWindow win)
	{
		for (int i = 0; i < 4 && Dlg.AnyOpen; i++)
		{
			ButtonBase? skip = FindButton(Dlg.Host, "跳过向导") ?? FindButton(Dlg.Host, "暂不同意");
			if (skip == null)
			{
				break;
			}
			await win.Dispatcher.InvokeAsync(delegate
			{
				skip.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
			}, DispatcherPriority.ContextIdle);
			await Task.Delay(350);
			await win.Dispatcher.InvokeAsync(delegate
			{
			}, DispatcherPriority.ContextIdle);
		}
	}

	private static ButtonBase? FindButton(DependencyObject? root, string label)
	{
		if (root == null)
		{
			return null;
		}
		int count = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < count; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is ButtonBase b && (b.Content as string)?.Contains(label) == true)
			{
				return b;
			}
			ButtonBase? deep = FindButton(child, label);
			if (deep != null)
			{
				return deep;
			}
		}
		return null;
	}

	private static void Render(Window win, string outPath)
	{
		int pixelWidth = (int)Math.Max(1.0, win.ActualWidth);
		int pixelHeight = (int)Math.Max(1.0, win.ActualHeight);
		RenderTargetBitmap rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, 96.0, 96.0, PixelFormats.Pbgra32);
		rtb.Render(win);
		string? dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
		if (!string.IsNullOrEmpty(dir))
		{
			Directory.CreateDirectory(dir);
		}
		PngBitmapEncoder encoder = new PngBitmapEncoder();
		encoder.Frames.Add(BitmapFrame.Create(rtb));
		using FileStream stream = File.Create(outPath);
		encoder.Save(stream);
	}

	private static string? ArgVal(string[] args, string name)
	{
		int idx = Array.IndexOf(args, name);
		if (idx < 0 || idx + 1 >= args.Length)
		{
			return null;
		}
		return args[idx + 1];
	}

	private static string Sanitize(string id)
	{
		return new string(id.Select((char c) => (!Path.GetInvalidFileNameChars().Contains(c)) ? c : '_').ToArray());
	}
}

/// <summary>
/// DevShot 的参数解析（R34 WP-L0）。纯函数、不碰任何 WPF 类型，net8 测试宿主可以直接断言。
/// </summary>
public sealed class DevShotArgs
{
	public const string PopLayoutDrawer = "layout-drawer";

	public const string ExpandAdvanced = "advanced";

	// 设置页左导航的视图号（SettingsPage._nav 的顺序，7 = SettingsLayoutPage.LayoutView）
	private static readonly Dictionary<string, int> SettingsViews = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
	{
		["general"] = 0,
		["account"] = 1,
		["java"] = 2,
		["network"] = 3,
		["servers"] = 4,
		["playtime"] = 5,
		["about"] = 6,
		["feedback"] = 6,
		["layout"] = 7
	};

	// 设置子视图里各自的区块宿主 id（null = 这个子视图没有区块宿主）
	private static readonly Dictionary<string, string?> SettingsHosts = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
	{
		["general"] = "settings:general",
		["account"] = "account",
		["java"] = "java",
		["network"] = "settings:network",
		["servers"] = "servers",
		["playtime"] = "playtime",
		["about"] = "settings:about",
		["feedback"] = "feedback",
		["layout"] = null
	};

	// 不是顶栏页、只活在设置子视图里的页面：--shot java 等价于 --shot settings:java
	private static readonly HashSet<string> SettingsOnlyPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "java", "servers", "playtime", "feedback" };

	/// <summary>命令行给的页面 id（输出文件名也用它）。</summary>
	public string ShotId { get; private set; } = "launch";

	/// <summary>交给 MainWindow.Navigate 的导航 id。</summary>
	public string NavId { get; private set; } = "launch";

	/// <summary>要操作的区块宿主 id；null = 按 NavId 前缀找。</summary>
	public string? HostId { get; private set; }

	/// <summary>设置页子视图号；-1 = 不切子视图。</summary>
	public int SettingsView { get; private set; } = -1;

	public bool Edit { get; private set; }

	public string? Select { get; private set; }

	public string Pop { get; private set; } = "";

	public bool LayoutDrawer => Pop == PopLayoutDrawer;

	public string? Expand { get; private set; }

	public static DevShotArgs Parse(string[]? args)
	{
		args ??= new string[0];
		DevShotArgs a = new DevShotArgs();
		string? shot = Val(args, "--shot");
		if (shot == null && Array.IndexOf(args, "--design-sheet") >= 0)
		{
			// --design-sheet 单独出现时就是样张页（旧实现会把它后面的 --out 之类当成页 id）
			shot = Val(args, "--design-sheet") ?? "design-sheet";
		}
		a.ShotId = string.IsNullOrWhiteSpace(shot) ? "launch" : shot!.Trim();
		a.NavId = a.ShotId;
		string sub = "";
		if (a.ShotId.StartsWith("settings:", StringComparison.OrdinalIgnoreCase))
		{
			sub = a.ShotId.Substring("settings:".Length);
		}
		else if (SettingsOnlyPages.Contains(a.ShotId))
		{
			sub = a.ShotId;
		}
		if (sub.Length > 0)
		{
			a.NavId = "settings";
			if (SettingsViews.TryGetValue(sub, out int view))
			{
				a.SettingsView = view;
				a.HostId = SettingsHosts[sub];
			}
			else
			{
				// 不认识的子视图：不切视图，宿主按完整 id 找（例如将来新加的 settings:xxx 宿主）
				a.HostId = a.ShotId;
			}
		}
		else if (a.ShotId.IndexOf(':') > 0)
		{
			// library:main 这类「页:宿主」写法
			a.NavId = a.ShotId.Substring(0, a.ShotId.IndexOf(':'));
			a.HostId = a.ShotId;
		}
		a.Pop = Val(args, "--pop") ?? "";
		a.Select = Val(args, "--select");
		a.Expand = Val(args, "--expand");
		a.Edit = Array.IndexOf(args, "--edit") >= 0 || a.Select != null || a.LayoutDrawer;
		return a;
	}

	/// <summary>
	/// 区块宿主和本次截图目标的匹配度：2 = id 完全一致；1 = 没指定宿主时按「导航 id」或「导航 id:」前缀命中；0 = 不匹配。
	/// 指定了宿主却没有对应宿主的子视图（settings:layout）恒为 0。
	/// </summary>
	public int MatchScore(string? hostPageId)
	{
		string id = hostPageId ?? "";
		if (id.Length == 0)
		{
			return 0;
		}
		if (HostId != null)
		{
			return string.Equals(id, HostId, StringComparison.OrdinalIgnoreCase) ? 2 : 0;
		}
		if (SettingsView >= 0)
		{
			return 0;
		}
		if (string.Equals(id, NavId, StringComparison.OrdinalIgnoreCase))
		{
			return 2;
		}
		return id.StartsWith(NavId + ":", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
	}

	// 取开关后面的值；值缺失或下一个就是开关（以 "--" 开头）时返回 null
	private static string? Val(string[] args, string name)
	{
		int idx = Array.IndexOf(args, name);
		if (idx < 0 || idx + 1 >= args.Length)
		{
			return null;
		}
		string v = args[idx + 1];
		if (v.StartsWith("--", StringComparison.Ordinal))
		{
			return null;
		}
		return v;
	}
}
