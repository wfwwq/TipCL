using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TipCL.Services;

namespace TipCL.Pages.Layout;

/// <summary>
/// [layout-fix] 规格 §4-能力1 + §3-问题3 + 规格2-问题1:主窗口级的两个钩子(在 MainWindow 构造时挂一次)。
///   1) 全局右键「自定义布局...」:页面里任何**没有自带右键菜单**的地方(空白处 / 按钮 / 卡片 / 页头……)右键都弹菜单。
///      文本框、密码框和本来就有右键菜单的控件保持原样(区块宿主 BlockHost 自己不挂菜单,否则整页都被它挡住)。
///      菜单项 =「自定义布局...」(没在编辑时)/「完成编辑」「重置本页布局」(正在编辑时)/
///      分隔线 /「布局设置...」。目标的选取顺序 = 鼠标所在元素最内层的区块宿主 → 包含鼠标位置的最小宿主
///      → 页面里最大的宿主;当前页面完全没有可拖动的区块时给一句提示(指向「设置 → 布局」),而不是右键了没反应。
///   2) 统一滚轮:最内层能滚就交给原生逻辑(保留 SmoothScroll 的平滑动画);最内层滚不动(没高度 / 已经到头)
///      就把滚轮交给外层第一个还能滚的容器;鼠标在页头 / 分段栏 / 编辑工具条这类完全不在滚动容器里的位置时,
///      滚当前页面里最大的那个可滚区域 —— 也就是"整个页面任何位置都能滚动"。
///      按住 Ctrl / Shift 时、在 Slider 上时不接管。
/// 判定逻辑全部在零 WPF 的 <see cref="LayoutWheelRouting"/> / <see cref="LayoutHostPick"/> 里(可被 net8 单测断言),
/// 本文件只做 WPF 接线。
/// </summary>
public static class LayoutShellHooks
{
	private static MainWindow? _win;

	/// <summary>是否已经挂载(幂等:同一个窗口重复 Attach 不会重复挂钩子)。</summary>
	public static bool Attached
	{
		get
		{
			return _win != null;
		}
	}

	/// <summary>挂载到主窗口(重复调用是空操作)。</summary>
	public static void Attach(MainWindow win)
	{
		if (win == null || _win != null)
		{
			return;
		}
		_win = win;
		ContentControl pageHost = win.PageHost;
		if (pageHost != null)
		{
			pageHost.AddHandler(UIElement.MouseRightButtonUpEvent, new MouseButtonEventHandler(OnPageRightButtonUp), handledEventsToo: false);
		}
		// [layout-fix] 规格2-问题1:启动页等大面积背景不参与命中测试(背景图 IsHitTestVisible=false、
		// 根容器也没有背景色),右键会直接落到窗口上,PageHost 收不到。窗口层再兜底一次:点在页面区域内就按页面右键处理。
		win.AddHandler(UIElement.MouseRightButtonUpEvent, new MouseButtonEventHandler(OnWindowRightButtonUp), handledEventsToo: false);
		win.AddHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewWheel), handledEventsToo: false);
	}

	/// <summary>解挂(测试 / 换窗口用)。</summary>
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
			win.RemoveHandler(UIElement.MouseRightButtonUpEvent, new MouseButtonEventHandler(OnWindowRightButtonUp));
			win.RemoveHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewWheel));
			ContentControl pageHost = win.PageHost;
			if (pageHost != null)
			{
				pageHost.RemoveHandler(UIElement.MouseRightButtonUpEvent, new MouseButtonEventHandler(OnPageRightButtonUp));
			}
		}
		catch
		{
			// 窗口已经关了:无所谓
		}
	}

	// ───────────────────────── 右键菜单(§4-能力1 / 规格2-问题1) ─────────────────────────

	private static void OnWindowRightButtonUp(object? sender, MouseButtonEventArgs e)
	{
		MainWindow? win = _win;
		if (win == null || e.Handled)
		{
			return;
		}
		ContentControl? pageHost = win.PageHost;
		if (pageHost == null || !pageHost.IsVisible)
		{
			return;
		}
		// 来自页面内部的事件已由 PageHost 的处理器处理(或交给控件自带的菜单),这里只管"穿透到窗口"的情况
		for (DependencyObject? node = e.OriginalSource as DependencyObject; node != null; node = ParentOf(node))
		{
			if (ReferenceEquals(node, pageHost))
			{
				return;
			}
		}
		Point point = e.GetPosition(pageHost);
		if (point.X < 0.0 || point.Y < 0.0 || point.X > pageHost.ActualWidth || point.Y > pageHost.ActualHeight)
		{
			return;
		}
		OnPageRightButtonUp(sender, e);
	}

	private static void OnPageRightButtonUp(object? sender, MouseButtonEventArgs e)
	{
		MainWindow? win = _win;
		if (win == null || e.Handled)
		{
			return;
		}
		ContentControl? pageHost = win.PageHost;
		DependencyObject? source = e.OriginalSource as DependencyObject;
		// 自带右键菜单 / 文本输入框的地方保持原样(复制粘贴、版本行菜单、插件卡片的「更多」……)
		for (DependencyObject? node = source; node != null && !ReferenceEquals(node, pageHost); node = ParentOf(node))
		{
			if (node is TextBoxBase || node is PasswordBox)
			{
				return;
			}
			if (node is FrameworkElement element && element.ContextMenu != null)
			{
				return;
			}
			if (node is ContextMenu)
			{
				return;
			}
		}
		if (!(pageHost?.Content is FrameworkElement page))
		{
			return;
		}
		Point point = e.GetPosition(page);
		List<BlockHost> hosts = FindHosts(page);
		BlockHost? editing = hosts.Find(delegate(BlockHost host)
		{
			return host.Editor.IsEditing;
		});
		ContextMenu menu = new ContextMenu();
		// 编辑中不再给「自定义布局...」:点了只会落到已经在编辑的宿主上,什么也不发生
		if (editing == null)
		{
			MenuItem enter = new MenuItem
			{
				Header = I18n.L("自定义布局...")
			};
			enter.Click += delegate
			{
				EnterEditAt(win, page, source, point);
			};
			menu.Items.Add(enter);
		}
		else
		{
			MenuItem done = new MenuItem
			{
				Header = I18n.L("完成编辑")
			};
			done.Click += delegate
			{
				editing.Editor.ExitEdit();
			};
			menu.Items.Add(done);
			// [layout-fix] 参考件v2-对齐:工具栏「重置本页」之外再给一个右键入口
			MenuItem reset = new MenuItem
			{
				Header = I18n.L("重置本页布局")
			};
			reset.Click += delegate
			{
				editing.Editor.ResetPage();
			};
			menu.Items.Add(reset);
		}
		menu.Items.Add(new Separator());
		MenuItem settings = new MenuItem
		{
			Header = I18n.L("布局设置...")
		};
		settings.Click += delegate
		{
			OpenLayoutSettings(win);
		};
		menu.Items.Add(settings);
		menu.PlacementTarget = pageHost;
		menu.Placement = PlacementMode.MousePoint;
		menu.IsOpen = true;
		e.Handled = true;
	}

	/// <summary>右键菜单入口:进入最贴近鼠标位置的区块宿主的编辑态。返回是否真的进去了。</summary>
	public static bool EnterEditAt(MainWindow win, FrameworkElement page, DependencyObject? source, Point pointInPage)
	{
		List<BlockHost> hosts = FindHosts(page);
		// 候选矩形(相对页面)与"最内层"下标一起交给纯判定 LayoutHostPick.Resolve:
		//   最内层宿主 → 包含鼠标位置的最小宿主 → 页面里最大的宿主
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
		win.Toast(I18n.L("这个页面没有可拖动的区块"), I18n.L("页签、密度、分栏比例可以在「设置 → 布局」里调整"), ToastKind.Warning);
		return false;
	}

	/// <summary>跳到「设置 → 布局」(菜单项「布局设置...」)。</summary>
	public static void OpenLayoutSettings(MainWindow win)
	{
		win.Navigate("settings");
		if (win.GetPage("settings") is SettingsPage page)
		{
			page.OpenLayoutView();
		}
	}

	/// <summary>页面可视树里所有**可编辑**的区块宿主(不可见的子树整棵跳过;深度上限防御异常树)。</summary>
	public static List<BlockHost> FindHosts(DependencyObject root)
	{
		List<BlockHost> list = new List<BlockHost>();
		Collect(root, list, 0);
		return list;
	}

	private static void Collect(DependencyObject? node, List<BlockHost> list, int depth)
	{
		if (node == null || depth > 60)
		{
			return;
		}
		if (node is UIElement element && !element.IsVisible)
		{
			return;
		}
		if (node is BlockHost host && host.Editor.CanEdit)
		{
			list.Add(host);
		}
		int count;
		try
		{
			count = VisualTreeHelper.GetChildrenCount(node);
		}
		catch
		{
			return;
		}
		for (int i = 0; i < count; i++)
		{
			Collect(VisualTreeHelper.GetChild(node, i), list, depth + 1);
		}
	}

	// 宿主在页面坐标系里的矩形(算不出来时给空矩形 = 不参与命中)
	private static LayoutRect BoundsIn(FrameworkElement element, FrameworkElement page)
	{
		try
		{
			Rect rect = element.TransformToAncestor(page).TransformBounds(new Rect(0.0, 0.0, element.ActualWidth, element.ActualHeight));
			return new LayoutRect(rect.X, rect.Y, rect.Width, rect.Height);
		}
		catch
		{
			return default(LayoutRect);
		}
	}

	private static bool IsWithin(DependencyObject? node, DependencyObject? ancestor)
	{
		for (DependencyObject? walk = node; walk != null; walk = ParentOf(walk))
		{
			if (ReferenceEquals(walk, ancestor))
			{
				return true;
			}
		}
		return false;
	}

	private static DependencyObject? ParentOf(DependencyObject? node)
	{
		if (node == null)
		{
			return null;
		}
		if (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
		{
			DependencyObject? parent = VisualTreeHelper.GetParent(node);
			if (parent != null)
			{
				return parent;
			}
		}
		if (node is FrameworkContentElement contentElement)
		{
			return contentElement.Parent;
		}
		return LogicalTreeHelper.GetParent(node);
	}

	// ───────────────────────── 统一滚轮(§3-问题3) ─────────────────────────

	private static void OnPreviewWheel(object? sender, MouseWheelEventArgs e)
	{
		MainWindow? win = _win;
		if (win == null || e.Handled || e.Delta == 0)
		{
			return;
		}
		// Ctrl / Shift 不接管:通常是缩放 / 横向滚动
		if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0)
		{
			return;
		}
		DependencyObject? source = e.OriginalSource as DependencyObject;
		ContentControl? pageHost = win.PageHost;
		bool inPage = false;
		ScrollViewer? innermost = null;
		ScrollViewer? target = null;
		List<ScrollViewer> chain = new List<ScrollViewer>();
		List<bool> canScroll = new List<bool>();
		for (DependencyObject? node = source; node != null; node = ParentOf(node))
		{
			if (ReferenceEquals(node, pageHost))
			{
				inPage = true;
				break;
			}
			if (node is Slider)
			{
				// 滑块上不接管(拖动 / 滚轮微调是它自己的事)
				return;
			}
			if (node is ScrollViewer viewer)
			{
				if (innermost == null)
				{
					innermost = viewer;
				}
				bool can = CanScroll(viewer, e.Delta);
				chain.Add(viewer);
				canScroll.Add(can);
				if (target == null && can)
				{
					target = viewer;
				}
			}
		}
		// 从内到外的可滚性链交给纯判定:0 = 最内层自己滚(保持原生),i = 转交外层第 i 个,
		// -1 = 在滚动容器里但整条链都到头(不接管),-2 = 完全不在滚动容器里(滚页面主体最大的可滚区域)
		int route = LayoutWheelRouting.Route(canScroll, chain.Count != 0);
		if (route == 0)
		{
			return;
		}
		if (target != null)
		{
			ScrollBy(target, e.Delta);
			e.Handled = true;
			return;
		}
		if (!inPage || innermost != null || route != LayoutWheelRouting.OutsideAnyScroll)
		{
			// 鼠标在某个滚动容器里、整条链都已到头 → 保持原生行为,不去滚别的列表
			return;
		}
		// 鼠标所在位置没有任何能滚的容器(页头、分段栏、内层到底的列表……)→ 滚页面主体里最大的可滚区域
		if (pageHost?.Content is DependencyObject page)
		{
			ScrollViewer? main = FindMainScroller(page, e.Delta);
			if (main != null)
			{
				ScrollBy(main, e.Delta);
				e.Handled = true;
			}
		}
	}

	private static bool CanScroll(ScrollViewer viewer, int delta)
	{
		return LayoutWheelRouting.CanScroll(viewer.VerticalOffset, viewer.ScrollableHeight, delta)
			&& viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled;
	}

	private static void ScrollBy(ScrollViewer viewer, int delta)
	{
		if (viewer is SmoothScroll smooth)
		{
			// 外层容器被转交滚轮时也走同一套平滑动画(规格 §3-问题3 的 WheelBy)
			smooth.WheelBy(delta);
			return;
		}
		double notches = (double)delta / 120.0;
		double step = (viewer.CanContentScroll ? 3.0 : 48.0);
		double target = viewer.VerticalOffset - notches * step;
		viewer.ScrollToVerticalOffset(Math.Max(0.0, Math.Min(viewer.ScrollableHeight, target)));
	}

	// 页面里"最大的那个可滚区域"(面积最大且在当前方向还能滚)
	private static ScrollViewer? FindMainScroller(DependencyObject root, int delta)
	{
		List<ScrollViewer> found = new List<ScrollViewer>();
		CollectScrollViewers(root, found, 0);
		List<double> areas = new List<double>(found.Count);
		List<bool> canScroll = new List<bool>(found.Count);
		foreach (ScrollViewer viewer in found)
		{
			areas.Add(Math.Max(0.0, viewer.ActualWidth) * Math.Max(0.0, viewer.ActualHeight));
			canScroll.Add(CanScroll(viewer, delta));
		}
		int index = LayoutWheelRouting.Widest(areas, canScroll);
		return ((index >= 0 && index < found.Count) ? found[index] : null);
	}

	private static void CollectScrollViewers(DependencyObject? node, List<ScrollViewer> into, int depth)
	{
		if (node == null || depth > 60 || into.Count > 256)
		{
			return;
		}
		if (node is UIElement element && !element.IsVisible)
		{
			return;
		}
		if (node is ScrollViewer viewer)
		{
			into.Add(viewer);
		}
		int count;
		try
		{
			count = VisualTreeHelper.GetChildrenCount(node);
		}
		catch
		{
			return;
		}
		for (int i = 0; i < count; i++)
		{
			CollectScrollViewers(VisualTreeHelper.GetChild(node, i), into, depth + 1);
		}
	}
}
