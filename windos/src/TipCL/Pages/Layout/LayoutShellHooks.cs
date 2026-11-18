using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TipCL.Services;

namespace TipCL.Pages.Layout;

/// <summary>
/// 壳级交互兜底(MainWindow 构造时 <see cref="Install"/> 一次,进程内只挂一份):
///   1) <b>全局右键「自定义布局...」</b> —— 页面里任何没有自带右键菜单的位置(空白处、按钮、卡片、页头)
///      右键都能进编辑态。文本框/密码框/已有 ContextMenu 的控件保持原样。
///   2) <b>滚轮转交</b> —— 内层滚动容器滚不动(高度为 0 或已经到头)时把滚轮交给外层;
///      鼠标压根不在任何滚动容器里(页头、分段栏、工具条)时,滚当前页最大的可滚区域。
///      于是"必须把鼠标放到滚动条上才能滚"这件事不再发生。
/// </summary>
public static class LayoutShellHooks
{
	private static Window? _win;

	/// <summary>挂载(幂等:同一窗口重复调用不会重复订阅)。</summary>
	public static void Install(Window? win)
	{
		if (win == null || ReferenceEquals(_win, win))
		{
			return;
		}
		Uninstall();
		_win = win;
		win.PreviewMouseRightButtonUp += OnRightUp;
		win.PreviewMouseWheel += OnWheel;
		win.Closed += OnClosed;
	}

	private static void OnClosed(object? sender, EventArgs e)
	{
		Uninstall();
	}

	private static void Uninstall()
	{
		Window? win = _win;
		if (win == null)
		{
			return;
		}
		win.PreviewMouseRightButtonUp -= OnRightUp;
		win.PreviewMouseWheel -= OnWheel;
		win.Closed -= OnClosed;
		_win = null;
	}

	// ————— 1) 全局右键 —————

	private static void OnRightUp(object sender, MouseButtonEventArgs e)
	{
		try
		{
			DependencyObject? src = e.OriginalSource as DependencyObject;
			if (src == null)
			{
				return;
			}
			// 文本框/密码框:原生复制粘贴优先;自带 ContextMenu 的控件(版本行、文件夹行、插件卡片…):对方优先
			if (IsTextEntry(src) || HasOwnMenu(src))
			{
				return;
			}
			List<BlockHost> hosts = Hosts();
			BlockHost? target = FindTargetHost(src, hosts);
			if (target == null)
			{
				if (hosts.Count == 0)
				{
					// 这一页确实没有可拖动区块:给一句提示,而不是"右键没反应"
					AppServices.Toast(
						I18n.L("这个页面没有可拖动的区块"),
						I18n.L("页签、密度、分栏比例可以在「设置 → 布局」里调整"),
						ToastKind.Warning);
					e.Handled = true;
				}
				return;
			}
			ContextMenu menu = new ContextMenu();
			MenuItem open = new MenuItem
			{
				Header = I18n.L("自定义布局...")
			};
			open.Click += delegate
			{
				target.EnterEdit();
			};
			menu.Items.Add(open);
			BlockHost? editing = hosts.FirstOrDefault(delegate(BlockHost h)
			{
				return h.IsEditing;
			});
			if (editing != null)
			{
				MenuItem done = new MenuItem
				{
					Header = I18n.L("完成编辑")
				};
				done.Click += delegate
				{
					editing.ExitEdit();
				};
				menu.Items.Add(done);
			}
			MenuItem settings = new MenuItem
			{
				Header = I18n.L("布局设置...")
			};
			settings.Click += delegate
			{
				OpenLayoutSettings();
			};
			menu.Items.Add(settings);
			menu.PlacementTarget = ((src as UIElement) ?? _win) as UIElement;
			menu.IsOpen = true;
			e.Handled = true;
		}
		catch
		{
			// 右键兜底失败不能影响页面自身交互
		}
	}

	/// <summary>目标选择顺序:鼠标所在元素的最内层宿主 → 包含鼠标位置的最小宿主 → 页面里最大的宿主。</summary>
	private static BlockHost? FindTargetHost(DependencyObject src, List<BlockHost> hosts)
	{
		BlockHost? nearest = Ancestor<BlockHost>(src);
		if (nearest != null)
		{
			return nearest;
		}
		if (hosts.Count == 0)
		{
			return null;
		}
		Point point = Mouse.GetPosition(_win);
		BlockHost? containing = hosts
			.Where(delegate(BlockHost h)
			{
				return Contains(h, point);
			})
			.OrderBy(Area)
			.FirstOrDefault();
		return containing ?? hosts.OrderByDescending(Area).First();
	}

	private static double Area(BlockHost host)
	{
		return host.ActualWidth * host.ActualHeight;
	}

	private static bool Contains(BlockHost host, Point pointInWindow)
	{
		try
		{
			if (!host.IsVisible || host.ActualWidth <= 0.0 || host.ActualHeight <= 0.0)
			{
				return false;
			}
			Point p = host.TranslatePoint(new Point(0.0, 0.0), _win);
			return pointInWindow.X >= p.X && pointInWindow.X <= p.X + host.ActualWidth && pointInWindow.Y >= p.Y && pointInWindow.Y <= p.Y + host.ActualHeight;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>窗口可视树里所有 BlockHost(通常是 1~3 个;页面缓存复用,所以数量很少)。</summary>
	private static List<BlockHost> Hosts()
	{
		List<BlockHost> list = new List<BlockHost>();
		Window? win = _win;
		if (win == null)
		{
			return list;
		}
		Walk(win, list);
		return list;
	}

	private static void Walk(DependencyObject node, List<BlockHost> into)
	{
		if (node is BlockHost host)
		{
			into.Add(host);
			return;
		}
		int count = 0;
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
			Walk(VisualTreeHelper.GetChild(node, i), into);
		}
	}

	private static T? Ancestor<T>(DependencyObject? node) where T : DependencyObject
	{
		for (DependencyObject? cur = node; cur != null; cur = Parent(cur))
		{
			if (cur is T match && !ReferenceEquals(cur, _win))
			{
				return match;
			}
		}
		return null;
	}

	private static DependencyObject? Parent(DependencyObject node)
	{
		try
		{
			DependencyObject p = VisualTreeHelper.GetParent(node);
			if (p != null)
			{
				return p;
			}
		}
		catch
		{
		}
		if (node is FrameworkElement fe)
		{
			return fe.Parent ?? fe.TemplatedParent;
		}
		return null;
	}

	/// <summary>自己或祖先上挂了 ContextMenu → 这是页面自带菜单,全局菜单不抢。</summary>
	private static bool HasOwnMenu(DependencyObject src)
	{
		for (DependencyObject? cur = src; cur != null; cur = Parent(cur))
		{
			if (cur is FrameworkElement { ContextMenu: not null })
			{
				return true;
			}
			if (cur is ContextMenu)
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsTextEntry(DependencyObject src)
	{
		for (DependencyObject? cur = src; cur != null; cur = Parent(cur))
		{
			if (cur is TextBoxBase || cur is PasswordBox)
			{
				return true;
			}
		}
		return false;
	}

	private static void OpenLayoutSettings()
	{
		try
		{
			if (_win is not MainWindow main)
			{
				return;
			}
			main.Navigate("settings");
			if (main.PageInstance("settings") is SettingsPage settings)
			{
				settings.OpenLayoutView();
			}
		}
		catch
		{
		}
	}

	// ————— 2) 滚轮转交 —————

	private static void OnWheel(object sender, MouseWheelEventArgs e)
	{
		try
		{
			// Ctrl/Shift + 滚轮是页面自己的语义(缩放 / 横向),不接管
			if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != ModifierKeys.None)
			{
				return;
			}
			DependencyObject? src = e.OriginalSource as DependencyObject;
			if (src == null || InSlider(src))
			{
				return;
			}
			ScrollViewer? inner = Ancestor<ScrollViewer>(src);
			if (inner != null && CanScroll(inner, e.Delta))
			{
				return; // 内层能滚:交给原生(保留 SmoothScroll 的平滑动画)
			}
			ScrollViewer? target = ((inner != null) ? NextScrollable(inner, e.Delta) : FirstScrollable(src, e.Delta))
				?? LargestScrollable(e.Delta);
			if (target == null)
			{
				return;
			}
			Apply(target, e.Delta);
			e.Handled = true;
		}
		catch
		{
		}
	}

	private static bool InSlider(DependencyObject src)
	{
		for (DependencyObject? cur = src; cur != null; cur = Parent(cur))
		{
			if (cur is Slider)
			{
				return true;
			}
		}
		return false;
	}

	private static bool CanScroll(ScrollViewer sv, int delta)
	{
		if (sv.ScrollableHeight <= 0.5 || !sv.IsVisible)
		{
			return false;
		}
		return (delta < 0) ? (sv.VerticalOffset < sv.ScrollableHeight - 0.5) : (sv.VerticalOffset > 0.5);
	}

	private static ScrollViewer? NextScrollable(ScrollViewer from, int delta)
	{
		for (DependencyObject? cur = Parent(from); cur != null; cur = Parent(cur))
		{
			if (cur is ScrollViewer sv && CanScroll(sv, delta))
			{
				return sv;
			}
		}
		return null;
	}

	private static ScrollViewer? FirstScrollable(DependencyObject src, int delta)
	{
		for (DependencyObject? cur = src; cur != null; cur = Parent(cur))
		{
			if (cur is ScrollViewer sv && CanScroll(sv, delta))
			{
				return sv;
			}
		}
		return null;
	}

	/// <summary>页面里最大的还能滚的区域(鼠标在页头/工具条这类位置时用)。</summary>
	private static ScrollViewer? LargestScrollable(int delta)
	{
		List<ScrollViewer> all = new List<ScrollViewer>();
		Window? win = _win;
		if (win == null)
		{
			return null;
		}
		CollectScrolls(win, all);
		ScrollViewer? best = null;
		double bestArea = 0.0;
		foreach (ScrollViewer sv in all)
		{
			if (!CanScroll(sv, delta))
			{
				continue;
			}
			double area = sv.ActualWidth * sv.ActualHeight;
			if (area > bestArea)
			{
				bestArea = area;
				best = sv;
			}
		}
		return best;
	}

	private static void CollectScrolls(DependencyObject node, List<ScrollViewer> into)
	{
		if (node is ScrollViewer sv)
		{
			into.Add(sv);
		}
		int count = 0;
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
			CollectScrolls(VisualTreeHelper.GetChild(node, i), into);
		}
	}

	private static void Apply(ScrollViewer sv, int delta)
	{
		if (sv is SmoothScroll smooth)
		{
			smooth.WheelBy(delta);
			return;
		}
		double to = Math.Max(0.0, Math.Min(sv.ScrollableHeight, sv.VerticalOffset - (double)delta * 0.95));
		sv.ScrollToVerticalOffset(to);
	}
}