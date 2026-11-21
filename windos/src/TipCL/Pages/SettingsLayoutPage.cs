using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TipCL.Pages.Layout;
using TipCL.Services;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Pages;

// 设置页第 8 个视图「布局」(v3:从配置清单改成按要做的事分组)。七个区块 id 保持不变(测试钉着):
//   appearance 页头 + 外观(区块间距三档分段)/ shell 导航(页签 chip:拖动排序 + 眼睛显隐;标题栏 AI / 主题 / 账号)/
//   blocks 页面(缩略图卡片:默认 / 已自定义、编辑、恢复默认)/ profiles 方案(卡片:正在使用描边、AI 备份单独标出)/
//   splits + widgets + json = 「高级」(默认折叠:分栏宽度一行、网页和按钮组件开关、自建组件管理、布局文件)。
// 三条硬规矩(沿用):
//   1) 落盘唯一入口 = LayoutStoreV2(load → 改 → Save);本文件绝不自己写布局文件(导出到用户选定的路径除外);
//   2) 方案应用 = **原子替换** layout(整个 LayoutBody 换掉)再 Save,v1 由 v2 单向写通(不往 v1 塞新字段);
//   3) 本视图自带 18px 左留白(PageBase.ScrollBody 的 SPanel Margin = 18,14,18,16)—— 设置页左导航是分栏,
//      _view.Margin = -18 归位、分隔条正好占住视图左边那 18px,新视图不留白就会被分隔条盖住。
// JSON 编辑改成单独窗口(Pages/Layout/LayoutJsonWindow.cs),校验 / 应用逻辑仍是下面的 LayoutJsonEditor。
//
// 纯逻辑(LayoutProfiles / LayoutJsonEditor)刻意零 WPF 依赖:net8 测试宿主能直接断言方案增删改查、
// 原子替换、导入校验与 JSON 编辑器的逐字段错误(见 tests/TipCL.Tests/LayoutProfileTests.cs)。
public sealed partial class SettingsPage
{
	/// <summary>「布局」视图在视图表/左导航里的下标(第 8 个)。</summary>
	internal const int LayoutView = 7;

	/// <summary>「布局」视图的区块宿主 pageId(区块:appearance / shell / blocks / splits / widgets / profiles / json)。</summary>
	internal const string LayoutViewPageId = "settings:layout";

	/// <summary>跳到「设置 → 布局」视图(全局右键菜单的「布局设置...」、编辑态「⋯ → 整套布局与方案…」用)。</summary>
	public void OpenLayoutView()
	{
		try
		{
			Select(LayoutView);
		}
		catch
		{
			// 页面还没构建完(极早期右键):忽略,不抛
		}
	}

	/// <summary>
	/// 跳到「设置 → 布局」并展开「高级」、滚到它那里(编辑态「添加」抽屉底部那句
	/// 「网页、按钮组件默认关闭,可在 设置 → 布局 → 高级 里开启」点进来用)。
	/// </summary>
	public void OpenLayoutAdvanced()
	{
		_layoutAdvOpen = true;
		OpenLayoutView();
		try
		{
			base.Dispatcher.InvokeAsync((Action)delegate
			{
				try
				{
					_layoutAdvAnchor?.BringIntoView();
				}
				catch
				{
				}
			}, System.Windows.Threading.DispatcherPriority.Loaded);
		}
		catch
		{
		}
	}

	/// <summary>
	/// 跳到入口表(<see cref="LayoutBlockEntryCatalog"/>)里登记的页面并进入编辑态;没登记返回 false。
	/// 设置页内的子视图(通用 / Java / 服务器…)会先切到那个视图再找宿主。
	/// 供 <c>LayoutShellHooks.BeginEditPage</c> 复用(那边拿到 SettingsPage 实例后直接调这里即可)。
	/// </summary>
	public bool OpenPageForLayoutEdit(string pageId)
	{
		LayoutBlockEntryCatalog.Entry? entry = LayoutBlockEntryCatalog.Find(pageId);
		if (entry == null)
		{
			return false;
		}
		OpenBlockEditor(entry.PageId, entry.NavId, entry.SettingsView, entry.Hint);
		return true;
	}

	// 页面区块入口表在 LayoutBlockEntryCatalog(纯数据、零 WPF 依赖,测试直接断言)。

	private bool _layoutRebuilding;

	// 「高级」展开状态 / 「页面」是否显示全部:跨视图重建保留(每次保存都会重建整个布局视图)
	private bool _layoutAdvOpen;

	private bool _layoutPagesExpanded;

	// OpenLayoutAdvanced 滚动定位用
	private FrameworkElement? _layoutAdvAnchor;

	// 页签 chip 拖动排序的状态(拖动时 chip 行本身抓住鼠标,chip 只在行内换位置;松手才落盘)
	private Border? _chipDrag;

	private Point _chipDragStart;

	private bool _chipDragMoved;

	/// <summary>「页面」小节默认只显示的常用几页(其余点「显示全部 N 个页面」展开)。</summary>
	private static readonly string[] LayoutPrimaryPages = new string[6] { "launch", "download", "account", "settings:general", "playtime", "multiplayer" };

	// Lucide 图标(24 视框,描边):眼睛 / 划掉的眼睛 / 锁 / 拖动手柄 / 上下箭头
	private const string IconEye = "M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0 M9 12a3 3 0 1 0 6 0a3 3 0 1 0-6 0";

	private const string IconEyeOff = "M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49 M14.084 14.158a3 3 0 0 1-4.242-4.242 M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143 M2 2l20 20";

	private const string IconLock = "M5 11h14a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2z M7 11V7a5 5 0 0 1 10 0v4";

	private const string IconGrip = "M9 5h.01 M15 5h.01 M9 12h.01 M15 12h.01 M9 19h.01 M15 19h.01";

	private const string IconChevronDown = "M6 9l6 6 6-6";

	private const string IconChevronUp = "M18 15l-6-6-6 6";

	// ————— 视图装配 —————

	/// <summary>构「布局」视图(每次切到该视图都重建,保证读到最新状态)。</summary>
	private UIElement BuildLayoutView()
	{
		_layoutRebuilding = true;
		try
		{
			// 每个小节(分组标题 + 卡片)一个区块,页标题与说明跟着第一块走;
			// 宿主默认边距 18,14,18,16 + 块间距 10 = 原 ScrollBody 的 SPanel(间距 10)。
			ShellLayout shell = ShellTabs.CurrentShell();
			LayoutV2 doc = SafeLoadLayout();
			_layoutAdvAnchor = null;
			SPanel first = Ui.V(10.0, PTitle(I18n.L("布局")), Ui.Muted(I18n.L("调整导航、各页面的排列，或者保存成方案。改动即时生效，随时可以恢复默认。")).Wrap().M(0.0, 0.0, 0.0, 4.0), LayoutSection(I18n.L("外观"), LayoutAppearanceRows(shell)));
			UIElement[] advanced = LayoutAdvancedParts(doc);
			return new BlockHost(LayoutViewPageId, new List<BlockSpec>
			{
				new BlockSpec { Id = "appearance", Title = I18n.L("外观"), Content = first },
				new BlockSpec { Id = "shell", Title = I18n.L("导航"), Content = LayoutSection(I18n.L("导航"), LayoutShellRows(shell)) },
				new BlockSpec { Id = "blocks", Title = I18n.L("页面"), Content = LayoutPagesSection(doc, shell) },
				new BlockSpec { Id = "profiles", Title = I18n.L("方案"), Content = LayoutProfilesSection(doc) },
				new BlockSpec { Id = "splits", Title = I18n.L("高级"), Content = advanced[0] },
				new BlockSpec { Id = "widgets", Title = I18n.L("网页和按钮组件"), Content = advanced[1] },
				new BlockSpec { Id = "json", Title = I18n.L("布局文件"), Content = advanced[2] }
			}).Root;
		}
		finally
		{
			_layoutRebuilding = false;
		}
	}

	/// <summary>重建「布局」视图;当前正显示它时同步换掉宿主内容,并尽量保持滚动位置(改完下面的设置不跳回顶部)。</summary>
	private void RefreshLayoutView()
	{
		object? current = _view?.Content;
		UIElement? old = _views[LayoutView];
		double offset = -1.0;
		try
		{
			ScrollViewer? oldScroll = ((old != null) ? FindLayoutScroll(old, 0) : null);
			if (oldScroll != null)
			{
				offset = oldScroll.VerticalOffset;
			}
		}
		catch
		{
			offset = -1.0;
		}
		UIElement fresh = BuildLayoutView();
		_views[LayoutView] = fresh;
		if (_view != null && ReferenceEquals(current, old))
		{
			_view.Content = fresh;
			if (offset > 0.5)
			{
				base.Dispatcher.InvokeAsync((Action)delegate
				{
					try
					{
						FindLayoutScroll(fresh, 0)?.ScrollToVerticalOffset(offset);
					}
					catch
					{
					}
				}, System.Windows.Threading.DispatcherPriority.Loaded);
			}
		}
	}

	private static ScrollViewer? FindLayoutScroll(DependencyObject root, int depth)
	{
		if (root == null || depth > 30)
		{
			return null;
		}
		if (root is ScrollViewer sv)
		{
			return sv;
		}
		int count = 0;
		try
		{
			count = VisualTreeHelper.GetChildrenCount(root);
		}
		catch
		{
			return null;
		}
		for (int i = 0; i < count; i++)
		{
			ScrollViewer? found = FindLayoutScroll(VisualTreeHelper.GetChild(root, i), depth + 1);
			if (found != null)
			{
				return found;
			}
		}
		return null;
	}

	private static LayoutV2 SafeLoadLayout()
	{
		try
		{
			return LayoutStoreV2.Load();
		}
		catch
		{
			return LayoutV2.CreateDefault();
		}
	}

	// 一个小节 = 分组标题 + 卡片(行与行之间有通到卡片两端的分隔线,与设置页其它小节同一套视觉)
	private static UIElement LayoutSection(string title, params FrameworkElement[] rows)
	{
		return Ui.V(10.0, Gt(title), Ui.Card(Rows(rows)));
	}

	// 一行设置:左边「标题 + 一句说明」,右边控件靠右(不再像 Ui.Field 那样把按钮拉满整行)
	private static Grid LayoutRow(string title, string desc, UIElement right)
	{
		Grid row = Ui.G(null, "*,Auto");
		SPanel left = Ui.V(2.0, Ui.Txt(title, 13.0, bold: true), (desc.Length > 0) ? Ui.Small(desc).Wrap() : null);
		left.VerticalAlignment = VerticalAlignment.Center;
		left.Margin = new Thickness(0.0, 0.0, 16.0, 0.0);
		row.Add(left, 0);
		right.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
		row.Add(right, 0, 1);
		row.Margin = new Thickness(0.0, 6.0, 0.0, 6.0);
		return row;
	}

	// 分组标题 + 右侧按钮(「页面 · 全部恢复默认」「方案 · 导入… / 导出整套布局… / 存为新方案」)
	private static Grid LayoutGroupHead(string title, UIElement? right)
	{
		Grid head = Ui.G(null, "*,Auto");
		head.Add(Gt(title).VCenter(), 0);
		if (right != null)
		{
			right.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
			head.Add(right, 0, 1);
		}
		return head;
	}

	// ————— 1 外观:区块间距(三档分段按钮) —————

	private FrameworkElement[] LayoutAppearanceRows(ShellLayout shell)
	{
		string current = NormalizeDensity(shell.Density);
		SPanel rail = Ui.H(0.0);
		foreach (string key in new string[3] { LayoutSchema.DensityCompact, LayoutSchema.DensityStandard, LayoutSchema.DensityLoose })
		{
			string density = key;
			System.Windows.Controls.Primitives.ToggleButton item = new System.Windows.Controls.Primitives.ToggleButton
			{
				Style = Ui.S("Seg.Item"),
				Content = DensityLabel(density),
				IsChecked = (density == current)
			};
			item.Click += delegate
			{
				if (_layoutRebuilding)
				{
					return;
				}
				if (density == current)
				{
					item.IsChecked = true;
					return;
				}
				WriteLayout(delegate(LayoutV2 doc)
				{
					doc.Layout.Shell.Density = density;
				}, I18n.L("间距：{0}", DensityLabel(density)));
				RefreshLayoutView();
			};
			rail.Children.Add(item);
		}
		Border seg = new Border
		{
			Style = Ui.S("Seg.Rail"),
			Child = rail,
			HorizontalAlignment = HorizontalAlignment.Right
		};
		return new FrameworkElement[1] { LayoutRow(I18n.L("区块间距"), I18n.L("所有可以自定义的页面一起生效"), seg) };
	}

	private static string NormalizeDensity(string? density)
	{
		return density switch
		{
			LayoutSchema.DensityCompact => LayoutSchema.DensityCompact,
			LayoutSchema.DensityLoose => LayoutSchema.DensityLoose,
			_ => LayoutSchema.DensityStandard
		};
	}

	private static string DensityLabel(string? density)
	{
		return density switch
		{
			LayoutSchema.DensityCompact => I18n.L("紧凑"),
			LayoutSchema.DensityLoose => I18n.L("宽松"),
			_ => I18n.L("标准")
		};
	}

	// 缩略图里块与块的间隔跟着「区块间距」走(紧凑 / 标准 / 宽松)
	private static double ThumbGap(string? density)
	{
		return NormalizeDensity(density) switch
		{
			LayoutSchema.DensityCompact => 2.0,
			LayoutSchema.DensityLoose => 5.0,
			_ => 3.0
		};
	}

	// ————— 2 导航:顶部页签(chip)+ 标题栏 —————

	private FrameworkElement[] LayoutShellRows(ShellLayout shell)
	{
		Button reset = Ui.Btn(I18n.L("恢复默认"), BtnKind.Ghost, delegate
		{
			try
			{
				ShellTabs.ResetTabs();
				PageBase.Toast(I18n.L("页签已恢复默认"), "", ToastKind.Success);
			}
			catch (Exception ex)
			{
				PageBase.Toast(I18n.L("布局未能保存"), ex.Message, ToastKind.Error);
			}
			RefreshLayoutView();
		});
		reset.IsEnabled = !ShellTabs.IsDefaultTabs(shell);
		WrapPanel chips = BuildTabChips(shell);
		chips.Margin = new Thickness(0.0, 2.0, 0.0, 4.0);
		SPanel tabs = Ui.V(4.0, LayoutRow(I18n.L("顶部页签"), I18n.L("拖动调整顺序，点眼睛隐藏；「设置」不能隐藏"), reset), chips);
		// 标题栏:只留用户看得懂的三项;「桥接」不再出现在界面上(存储字段照旧保留,老存档读写不变)
		TitlebarLayout bar = shell.Titlebar ?? new TitlebarLayout();
		SPanel options = Ui.H(16.0, TitlebarCheck(I18n.L("AI 按钮"), bar.Ai, delegate(bool on)
		{
			ShellTabs.SetTitlebar(null, on);
		}), TitlebarCheck(I18n.L("主题切换"), bar.Theme, delegate(bool on)
		{
			ShellTabs.SetTitlebar(null, null, on);
		}), TitlebarCheck(I18n.L("账号"), bar.Account, delegate(bool on)
		{
			ShellTabs.SetTitlebar(null, null, null, on);
		}));
		return new FrameworkElement[2]
		{
			tabs,
			LayoutRow(I18n.L("标题栏"), I18n.L("隐藏 AI 按钮后仍可按 Ctrl+K 打开"), options)
		};
	}

	// 标题栏开关:写完 LayoutChanged 会让 MainWindow 立刻重排标题栏,本视图不必重建
	private CheckBox TitlebarCheck(string label, bool value, Action<bool> write)
	{
		CheckBox box = Ui.Check(label, value);
		box.VerticalAlignment = VerticalAlignment.Center;
		box.Checked += delegate
		{
			if (!_layoutRebuilding)
			{
				SafeWrite(delegate
				{
					write(obj: true);
				});
			}
		};
		box.Unchecked += delegate
		{
			if (!_layoutRebuilding)
			{
				SafeWrite(delegate
				{
					write(obj: false);
				});
			}
		};
		return box;
	}

	private static void SafeWrite(Action write)
	{
		try
		{
			write();
		}
		catch (Exception ex)
		{
			PageBase.Toast(I18n.L("布局未能保存"), ex.Message, ToastKind.Error);
		}
	}

	// chip 行:显示中的页签(用户顺序)在前,隐藏的在后(变淡);整行抓鼠标做拖动排序
	private WrapPanel BuildTabChips(ShellLayout shell)
	{
		WrapPanel panel = new WrapPanel
		{
			Orientation = Orientation.Horizontal,
			Background = Brushes.Transparent
		};
		List<string> visible = ShellTabs.OrderedKeys(shell);
		foreach (string key in ShellTabs.ChipOrder(shell))
		{
			panel.Children.Add(TabChip(panel, key, visible.Contains(key)));
		}
		panel.MouseMove += delegate(object sender, MouseEventArgs e)
		{
			OnChipDragMove(panel, e);
		};
		panel.MouseLeftButtonUp += delegate
		{
			EndChipDrag(panel);
		};
		panel.LostMouseCapture += delegate
		{
			EndChipDrag(panel);
		};
		return panel;
	}

	private Border TabChip(WrapPanel panel, string key, bool visible)
	{
		bool locked = !ShellTabs.CanHide(key);
		TextBlock name = Ui.Txt(ShellTabs.Label(key), 13.0, bold: false, visible ? "B.Ink" : "B.InkFaint");
		Button eye = new Button
		{
			Style = Ui.S("Btn.Icon"),
			Content = Lucide.Icon(locked ? IconLock : (visible ? IconEye : IconEyeOff), 14.0, "B.InkMuted"),
			Padding = new Thickness(4.0),
			MinWidth = 0.0,
			Cursor = Cursors.Hand
		};
		string tip = (locked && visible) ? I18n.L("「设置」不能隐藏") : (visible ? I18n.L("隐藏") : I18n.L("显示"));
		eye.ToolTip = tip;
		System.Windows.Automation.AutomationProperties.SetName(eye, tip);
		ToolTipService.SetShowOnDisabled(eye, value: true);
		// 「设置」锁定不能藏;但老存档里它已经被藏了的话,仍然允许点开(否则再也找不回来)
		eye.IsEnabled = !locked || !visible;
		eye.Click += delegate
		{
			ToggleTab(key, !visible);
		};
		Border chip = new Border
		{
			Tag = key,
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(8.0, 2.0, 2.0, 2.0),
			Margin = new Thickness(0.0, 0.0, 8.0, 8.0),
			BorderThickness = new Thickness(1.0),
			Cursor = Cursors.SizeAll,
			Opacity = (visible ? 1.0 : 0.7),
			Child = Ui.H(6.0, Lucide.Icon(IconGrip, 12.0, "B.InkFaint").VCenter(), name.VCenter(), eye)
		};
		chip.SetResourceReference(Border.BackgroundProperty, visible ? "B.Paper2" : "B.Card");
		chip.SetResourceReference(Border.BorderBrushProperty, "B.Line");
		chip.ToolTip = I18n.L("拖动调整顺序");
		chip.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
		{
			if (IsInsideButton(e.OriginalSource as DependencyObject, chip))
			{
				return;
			}
			_chipDrag = chip;
			_chipDragStart = e.GetPosition(panel);
			_chipDragMoved = false;
			panel.CaptureMouse();
			e.Handled = true;
		};
		return chip;
	}

	private static bool IsInsideButton(DependencyObject? node, DependencyObject stop)
	{
		for (DependencyObject? cur = node; cur != null && !ReferenceEquals(cur, stop); cur = ParentOf(cur))
		{
			if (cur is System.Windows.Controls.Primitives.ButtonBase)
			{
				return true;
			}
		}
		return false;
	}

	private static DependencyObject? ParentOf(DependencyObject node)
	{
		try
		{
			if (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
			{
				DependencyObject parent = VisualTreeHelper.GetParent(node);
				if (parent != null)
				{
					return parent;
				}
			}
		}
		catch
		{
		}
		if (node is FrameworkContentElement fce)
		{
			return fce.Parent;
		}
		return (node as FrameworkElement)?.Parent;
	}

	private void OnChipDragMove(WrapPanel panel, MouseEventArgs e)
	{
		Border? chip = _chipDrag;
		if (chip == null || e.LeftButton != MouseButtonState.Pressed)
		{
			return;
		}
		Point p = e.GetPosition(panel);
		if (!_chipDragMoved)
		{
			if (Math.Abs(p.X - _chipDragStart.X) < 4.0 && Math.Abs(p.Y - _chipDragStart.Y) < 4.0)
			{
				return;
			}
			_chipDragMoved = true;
			chip.Opacity = 0.6;
			chip.SetResourceReference(Border.BorderBrushProperty, "B.Accent");
		}
		// 目标位置 = 指针「之后」的 chip 个数:在它下一行之下,或同一行且越过了它的中线
		int target = 0;
		foreach (UIElement child in panel.Children)
		{
			if (ReferenceEquals(child, chip) || !(child is FrameworkElement fe) || fe.ActualWidth <= 0.0)
			{
				continue;
			}
			Point o = fe.TranslatePoint(new Point(0.0, 0.0), panel);
			bool before = p.Y > o.Y + fe.ActualHeight || (p.Y >= o.Y && p.X > o.X + fe.ActualWidth / 2.0);
			if (before)
			{
				target++;
			}
		}
		int now = panel.Children.IndexOf(chip);
		if (now >= 0 && target != now)
		{
			panel.Children.Remove(chip);
			panel.Children.Insert(Math.Min(target, panel.Children.Count), chip);
		}
	}

	private void EndChipDrag(WrapPanel panel)
	{
		Border? chip = _chipDrag;
		if (chip == null)
		{
			return;
		}
		_chipDrag = null;
		bool moved = _chipDragMoved;
		_chipDragMoved = false;
		if (panel.IsMouseCaptured)
		{
			panel.ReleaseMouseCapture();
		}
		if (!moved)
		{
			return;
		}
		List<string> order = new List<string>();
		foreach (UIElement child in panel.Children)
		{
			if (child is FrameworkElement fe && fe.Tag is string key)
			{
				order.Add(key);
			}
		}
		try
		{
			List<string> visible = ShellTabs.OrderedKeys(ShellTabs.CurrentShell());
			ShellTabs.SetChipOrder(order, visible);
			PageBase.Toast(I18n.L("页签顺序已更新"), "", ToastKind.Success);
		}
		catch (Exception ex)
		{
			PageBase.Toast(I18n.L("布局未能保存"), ex.Message, ToastKind.Error);
		}
		// 松手处理完再重建(当前还在 MouseUp / LostMouseCapture 回调里)
		base.Dispatcher.InvokeAsync((Action)RefreshLayoutView, System.Windows.Threading.DispatcherPriority.Background);
	}

	private void ToggleTab(string key, bool show)
	{
		try
		{
			ShellLayout shell = ShellTabs.CurrentShell();
			List<string> chips = ShellTabs.ChipOrder(shell);
			HashSet<string> visible = new HashSet<string>(ShellTabs.OrderedKeys(shell), StringComparer.Ordinal);
			if (show)
			{
				visible.Add(key);
			}
			else
			{
				if (!ShellTabs.CanHide(key))
				{
					PageBase.Toast(I18n.L("「设置」不能隐藏"), "", ToastKind.Warning);
					return;
				}
				visible.Remove(key);
			}
			ShellTabs.SetChipOrder(chips, visible);
			PageBase.Toast(show ? I18n.L("已显示「{0}」页签", ShellTabs.Label(key)) : I18n.L("已隐藏「{0}」页签", ShellTabs.Label(key)), "", ToastKind.Success);
		}
		catch (Exception ex)
		{
			PageBase.Toast(I18n.L("布局未能保存"), ex.Message, ToastKind.Error);
		}
		RefreshLayoutView();
	}

	// ————— 3 页面:缩略图卡片(复用入口表 + LayoutShellHooks.BeginEditPage) —————

	/// <summary>
	/// 「页面」小节列出的页:入口表里不是单块页、不需要先手动打开子视图(Hint 为空)、也不是本视图自己,
	/// 且编辑态那边认为能编辑(<c>LayoutShellHooks.CanEditPage</c>);常用的几页排前面。
	/// </summary>
	private static List<LayoutBlockEntryCatalog.Entry> LayoutCardEntries()
	{
		List<LayoutBlockEntryCatalog.Entry> list = new List<LayoutBlockEntryCatalog.Entry>();
		foreach (string id in LayoutPrimaryPages)
		{
			LayoutBlockEntryCatalog.Entry? entry = LayoutBlockEntryCatalog.Find(id);
			if (entry != null && IsLayoutCardEntry(entry))
			{
				list.Add(entry);
			}
		}
		foreach (LayoutBlockEntryCatalog.Entry entry in LayoutBlockEntryCatalog.All)
		{
			if (!list.Contains(entry) && IsLayoutCardEntry(entry))
			{
				list.Add(entry);
			}
		}
		return list;
	}

	private static bool IsLayoutCardEntry(LayoutBlockEntryCatalog.Entry entry)
	{
		if (entry.SingleBlock || entry.Hint.Length > 0 || entry.PageId == LayoutViewPageId)
		{
			return false;
		}
		try
		{
			return LayoutShellHooks.CanEditPage(entry.PageId);
		}
		catch
		{
			return true;
		}
	}

	private UIElement LayoutPagesSection(LayoutV2 doc, ShellLayout shell)
	{
		List<LayoutBlockEntryCatalog.Entry> entries = LayoutCardEntries();
		bool anyCustom = false;
		foreach (LayoutBlockEntryCatalog.Entry entry in entries)
		{
			if (LayoutProfiles.IsPageCustomized(doc, entry.PageId))
			{
				anyCustom = true;
				break;
			}
		}
		Button resetAll = Ui.Btn(I18n.L("全部恢复默认"), BtnKind.Ghost, delegate
		{
			PageBase.Run(ResetAllPagesAsync);
		});
		resetAll.IsEnabled = anyCustom || LayoutProfiles.AnyPageCustomized(doc);
		int primary = Math.Min(LayoutPrimaryPages.Length, entries.Count);
		int shown = (_layoutPagesExpanded ? entries.Count : primary);
		double gap = ThumbGap(shell.Density);
		System.Windows.Controls.Primitives.UniformGrid grid = new System.Windows.Controls.Primitives.UniformGrid
		{
			Columns = 3,
			Margin = new Thickness(0.0, 0.0, -10.0, -10.0)
		};
		for (int i = 0; i < shown; i++)
		{
			grid.Children.Add(PageCard(doc, entries[i], gap));
		}
		TextBlock note = Ui.Small(SingleBlockNote()).Wrap();
		Button more = Ui.Btn(_layoutPagesExpanded ? I18n.L("收起") : I18n.L("显示全部 {0} 个页面", entries.Count), BtnKind.Ghost, delegate
		{
			_layoutPagesExpanded = !_layoutPagesExpanded;
			RefreshLayoutView();
		});
		more.Visibility = ((entries.Count > primary) ? Visibility.Visible : Visibility.Collapsed);
		Grid foot = Ui.G(null, "*,Auto");
		foot.Add(note.VCenter(), 0);
		foot.Add(more, 0, 1);
		return Ui.V(10.0, LayoutGroupHead(I18n.L("页面"), resetAll), grid, foot);
	}

	// 「「下载与网络」「反馈与关于」只有一块内容，不提供编辑。」—— 页名取自入口表的单块页
	private static string SingleBlockNote()
	{
		string names = "";
		foreach (LayoutBlockEntryCatalog.Entry entry in LayoutBlockEntryCatalog.All)
		{
			if (entry.SingleBlock)
			{
				names += I18n.L("「{0}」", I18n.L(entry.Label));
			}
		}
		return (names.Length > 0) ? I18n.L("{0}只有一块内容，不提供编辑。", names) : "";
	}

	private Border PageCard(LayoutV2 doc, LayoutBlockEntryCatalog.Entry entry, double gap)
	{
		bool custom = LayoutProfiles.IsPageCustomized(doc, entry.PageId);
		LayoutThumbPlan plan = ThumbPlanOf(doc, entry);
		Grid name = Ui.G(null, "*,Auto");
		TextBlock title = Ui.Txt(I18n.L(entry.Label), 13.5, bold: true);
		title.TextTrimming = TextTrimming.CharacterEllipsis;
		name.Add(title.VCenter(), 0);
		name.Add(custom ? Ui.Tag(I18n.L("已自定义"), "B.AccentText", "B.AccentSoft") : Ui.Tag(I18n.L("默认")), 0, 1);
		TextBlock sub = Ui.Small(custom ? plan.Note : I18n.L("和出厂时一样"));
		sub.TextTrimming = TextTrimming.CharacterEllipsis;
		string pageId = entry.PageId;
		string label = I18n.L(entry.Label);
		Button edit = Ui.Btn(I18n.L("编辑"), BtnKind.Soft, delegate
		{
			BeginLayoutEdit(pageId);
		});
		SPanel actions = Ui.H(6.0, edit);
		if (custom)
		{
			actions.Children.Add(Ui.Btn(I18n.L("恢复默认"), BtnKind.Normal, delegate
			{
				ResetOnePage(pageId, label);
			}));
		}
		Border card = Ui.Card(Ui.V(8.0, BuildThumb(plan, gap), name, sub, actions), 10.0);
		card.Margin = new Thickness(0.0, 0.0, 10.0, 10.0);
		return card;
	}

	// 「编辑」= 跳到那一页并直接进入编辑态(实现在编辑态那边:LayoutShellHooks.BeginEditPage)
	private static void BeginLayoutEdit(string pageId)
	{
		try
		{
			LayoutShellHooks.BeginEditPage(pageId);
		}
		catch (Exception ex)
		{
			PageBase.Toast(I18n.L("没能进入编辑"), ex.Message, ToastKind.Warning);
		}
	}

	private void ResetOnePage(string pageId, string label)
	{
		string error = LayoutProfiles.ResetPage(pageId);
		if (error.Length > 0)
		{
			PageBase.Toast(I18n.L("布局未能保存"), error, ToastKind.Warning);
			return;
		}
		PageBase.Toast(I18n.L("「{0}」已恢复默认", label), "", ToastKind.Success);
		RefreshLayoutView();
	}

	private async System.Threading.Tasks.Task ResetAllPagesAsync()
	{
		if (!await Dlg.Confirm(I18n.L("所有页面恢复默认？"), I18n.L("页签、分栏和方案不受影响。现在的排列会先自动留一份，可以从「方案」里切回。"), I18n.L("全部恢复默认"), I18n.L("取消"), danger: true))
		{
			return;
		}
		string error = LayoutProfiles.ResetAllPages(out string kept);
		if (error.Length > 0)
		{
			PageBase.Toast(I18n.L("布局未能保存"), error, ToastKind.Warning);
			return;
		}
		PageBase.Toast(I18n.L("所有页面已恢复默认"), (kept.Length > 0) ? I18n.L("原来的排列已留底为「{0}」，可以从「方案」里切回", kept) : I18n.L("可以从「方案」里切回"), ToastKind.Success);
		RefreshLayoutView();
	}

	// ——— 缩略图:有已打开的页面宿主就按它当前的排列画,否则按布局文件里存的,再不行画一个通用示意 ———

	private sealed class LayoutThumbItem
	{
		public int Span { get; set; } = 1;

		public bool Widget { get; set; }
	}

	private sealed class LayoutThumbPlan
	{
		public int Cols { get; set; } = 1;

		public List<LayoutThumbItem> Items { get; } = new List<LayoutThumbItem>();

		public string Note { get; set; } = "";
	}

	private LayoutThumbPlan ThumbPlanOf(LayoutV2 doc, LayoutBlockEntryCatalog.Entry entry)
	{
		LayoutThumbPlan plan = new LayoutThumbPlan();
		HashSet<string> widgetIds = new HashSet<string>(StringComparer.Ordinal);
		if (doc.Layout.Widgets != null)
		{
			foreach (WidgetDef w in doc.Layout.Widgets)
			{
				if (w != null && !string.IsNullOrEmpty(w.Id))
				{
					widgetIds.Add(w.Id);
				}
			}
		}
		string mode = LayoutSchema.ModeStack;
		int hidden = 0;
		int widgets = 0;
		bool filled = false;
		LayoutPageState? live = LiveLayoutState(entry);
		if (live != null && live.Order.Count > 0)
		{
			mode = live.Page.Mode ?? LayoutSchema.ModeStack;
			plan.Cols = (mode == LayoutSchema.ModeStack) ? 1 : Math.Max(1, live.Page.Cols);
			foreach (string id in live.Order)
			{
				if (live.Hidden.Contains(id))
				{
					hidden++;
					continue;
				}
				int span = plan.Cols;
				if (plan.Cols > 1 && live.Spans.TryGetValue(id, out int s) && s > 0)
				{
					span = Math.Min(plan.Cols, s);
				}
				bool isWidget = widgetIds.Contains(id);
				if (isWidget)
				{
					widgets++;
				}
				plan.Items.Add(new LayoutThumbItem { Span = span, Widget = isWidget });
			}
			filled = true;
		}
		else if (doc.Layout.Pages.TryGetValue(entry.PageId, out PageLayoutV2? page) && page != null && page.Blocks.Count > 0)
		{
			mode = page.Mode ?? LayoutSchema.ModeStack;
			plan.Cols = (mode == LayoutSchema.ModeStack) ? 1 : Math.Max(1, page.Cols);
			foreach (BlockLayout block in page.Blocks)
			{
				if (block == null)
				{
					continue;
				}
				if (block.Hidden)
				{
					hidden++;
					continue;
				}
				int span = (plan.Cols > 1 && block.Span > 0) ? Math.Min(plan.Cols, block.Span) : plan.Cols;
				bool isWidget = widgetIds.Contains(block.Id);
				if (isWidget)
				{
					widgets++;
				}
				plan.Items.Add(new LayoutThumbItem { Span = span, Widget = isWidget });
			}
			filled = true;
		}
		if (!filled || plan.Items.Count == 0)
		{
			plan.Cols = 1;
			plan.Items.Clear();
			for (int i = 0; i < 3; i++)
			{
				plan.Items.Add(new LayoutThumbItem { Span = 1 });
			}
		}
		List<string> bits = new List<string>();
		if (mode != LayoutSchema.ModeStack)
		{
			bits.Add(I18n.L("网格"));
		}
		if (widgets > 0)
		{
			bits.Add(I18n.L("{0} 个组件", widgets));
		}
		if (hidden > 0)
		{
			bits.Add(I18n.L("隐藏了 {0} 项", hidden));
		}
		plan.Note = (bits.Count > 0) ? string.Join(" · ", bits) : I18n.L("调整了顺序");
		return plan;
	}

	// 已构建的页面宿主(设置页子视图在 _views 里,顶栏页面在 MainWindow 的页面缓存里;没构建过就返回 null,不触发构建)
	private LayoutPageState? LiveLayoutState(LayoutBlockEntryCatalog.Entry entry)
	{
		try
		{
			DependencyObject? root = null;
			if (entry.SettingsView >= 0 && entry.SettingsView < _views.Length)
			{
				root = _views[entry.SettingsView];
			}
			else if (entry.NavId.Length > 0)
			{
				root = PageBase.Win?.PageInstance(entry.NavId);
			}
			if (root == null)
			{
				return null;
			}
			BlockHost? host = FindBlockHost(root, entry.PageId);
			return host?.Editor.State;
		}
		catch
		{
			return null;
		}
	}

	private static FrameworkElement BuildThumb(LayoutThumbPlan plan, double gap)
	{
		int cols = Math.Max(1, plan.Cols);
		SPanel rows = Ui.V(gap);
		int count = plan.Items.Count;
		double height = (count <= 1) ? 44.0 : ((count == 2) ? 22.0 : ((count <= 4) ? 13.0 : 9.0));
		Grid? row = null;
		int x = 0;
		int rowIndex = 0;
		foreach (LayoutThumbItem item in plan.Items)
		{
			int span = Math.Max(1, Math.Min(cols, item.Span));
			if (row == null || x + span > cols)
			{
				row = new Grid
				{
					Margin = new Thickness(0.0, 0.0, -gap, 0.0)
				};
				for (int c = 0; c < cols; c++)
				{
					row.ColumnDefinitions.Add(new ColumnDefinition
					{
						Width = new GridLength(1.0, GridUnitType.Star)
					});
				}
				rows.Children.Add(row);
				x = 0;
				rowIndex++;
			}
			Border bar = new Border
			{
				CornerRadius = new CornerRadius(3.0),
				Height = ((rowIndex == 1 && count > 2) ? height + 6.0 : height),
				Margin = new Thickness(0.0, 0.0, gap, 0.0)
			};
			bar.SetResourceReference(Border.BackgroundProperty, item.Widget ? "B.AccentSoft" : "B.LineSoft");
			Grid.SetColumn(bar, x);
			Grid.SetColumnSpan(bar, span);
			row.Children.Add(bar);
			x += span;
		}
		Border frame = new Border
		{
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(8.0),
			Height = 78.0,
			ClipToBounds = true,
			Child = rows
		};
		frame.SetResourceReference(Border.BackgroundProperty, "B.Paper2");
		return frame;
	}

	// 跳到目标页 → 找到那一页的 BlockHost → EnterEdit(等价于点右键菜单里的「自定义布局...」)
	private void OpenBlockEditor(string pageId, string navId, int settingsView, string hint)
	{
		MainWindow? win = PageBase.Win;
		if (win == null)
		{
			return;
		}
		// 先导航(已在该页时 Navigate 自己会直接返回),再切设置页子视图
		if (navId.Length > 0)
		{
			win.Navigate(navId);
		}
		if (settingsView >= 0)
		{
			Select(settingsView);
		}
		// 等页面挂进可视树再找宿主(BlockHost 在页面构造时就已建好,这里只是等一次布局)
		base.Dispatcher.InvokeAsync((Action)delegate
		{
			EnterBlockEdit(pageId, hint);
		}, System.Windows.Threading.DispatcherPriority.Loaded);
	}

	private void EnterBlockEdit(string pageId, string hint)
	{
		MainWindow? win = PageBase.Win;
		BlockHost? host = ((win != null) ? FindBlockHost(win, pageId) : null);
		if (host != null && host.Editor.CanEdit)
		{
			host.Editor.EnterEdit();
			return;
		}
		PageBase.Toast(I18n.L("没找到可编辑的内容"), (hint.Length > 0) ? I18n.L(hint) : I18n.L("可以在页面空白处右键「自定义布局…」"), ToastKind.Warning);
	}

	// 视觉树优先(页面已渲染),找不到再退回逻辑树(ElementName/Content 挂载的场合)
	private static BlockHost? FindBlockHost(DependencyObject root, string pageId)
	{
		BlockHost? byVisual = FindBlockHostVisual(root, pageId, 0);
		if (byVisual != null)
		{
			return byVisual;
		}
		return FindBlockHostLogical(root, pageId, 0);
	}

	private static BlockHost? FindBlockHostVisual(DependencyObject root, string pageId, int depth)
	{
		if (depth > 24 || root == null)
		{
			return null;
		}
		if (root is BlockHost host && host.Editor.State.PageId == pageId)
		{
			return host;
		}
		int count = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < count; i++)
		{
			BlockHost? found = FindBlockHostVisual(VisualTreeHelper.GetChild(root, i), pageId, depth + 1);
			if (found != null)
			{
				return found;
			}
		}
		return null;
	}

	private static BlockHost? FindBlockHostLogical(object root, string pageId, int depth)
	{
		if (depth > 24 || root == null)
		{
			return null;
		}
		if (root is BlockHost host && host.Editor.State.PageId == pageId)
		{
			return host;
		}
		if (root is DependencyObject dep)
		{
			foreach (object child in LogicalTreeHelper.GetChildren(dep))
			{
				BlockHost? found = FindBlockHostLogical(child, pageId, depth + 1);
				if (found != null)
				{
					return found;
				}
			}
		}
		return null;
	}

	// ————— 4 方案:卡片(正在使用描边、AI 备份单独标出;更多菜单:重命名 / 复制一份 / 删除) —————

	private UIElement LayoutProfilesSection(LayoutV2 doc)
	{
		Button import = Ui.Btn(I18n.L("导入…"), BtnKind.Ghost, delegate
		{
			PageBase.Run(ImportLayoutFileAsync);
		});
		Button export = Ui.Btn(I18n.L("导出整套布局…"), BtnKind.Ghost, delegate
		{
			PageBase.Run(ExportLayoutAsync);
		});
		Button save = Ui.Btn(I18n.L("存为新方案"), BtnKind.Soft, delegate
		{
			PageBase.Run(NewProfileAsync);
		});
		Grid head = LayoutGroupHead(I18n.L("方案"), Ui.H(6.0, import, export, save));
		List<string> names = OrderedProfileNames(doc);
		string active = doc.ActiveProfile ?? "";
		UIElement body;
		if (names.Count == 0)
		{
			body = Ui.Card(Ui.Muted(I18n.L("还没有方案。点「存为新方案」把现在这套布局存下来，以后可以一键切回。")).Wrap(), 14.0);
		}
		else
		{
			System.Windows.Controls.Primitives.UniformGrid grid = new System.Windows.Controls.Primitives.UniformGrid
			{
				Columns = 3,
				Margin = new Thickness(0.0, 0.0, -10.0, -10.0)
			};
			foreach (string name in names)
			{
				grid.Children.Add(ProfileCard(doc, name, name == active));
			}
			body = grid;
		}
		return Ui.V(10.0, head, body);
	}

	// 显示顺序:普通方案(文档顺序)→「上一次布局」留底 → AI 备份(新的在前)
	private static List<string> OrderedProfileNames(LayoutV2 doc)
	{
		List<string> normal = new List<string>();
		List<string> previous = new List<string>();
		List<string> ai = new List<string>();
		foreach (string name in doc.Profiles.Keys)
		{
			if (IsAiBackup(name))
			{
				ai.Add(name);
			}
			else if (name == LayoutProfiles.PreviousName)
			{
				previous.Add(name);
			}
			else
			{
				normal.Add(name);
			}
		}
		ai.Sort((string a, string b) => string.CompareOrdinal(b, a));
		normal.AddRange(previous);
		normal.AddRange(ai);
		return normal;
	}

	private static bool IsAiBackup(string name)
	{
		return name.StartsWith(TipCL.Core.Launcher.Ai.LayoutTools.BackupPrefix, StringComparison.Ordinal);
	}

	private Border ProfileCard(LayoutV2 doc, string name, bool active)
	{
		bool ai = IsAiBackup(name);
		bool previous = name == LayoutProfiles.PreviousName;
		Border tag = (active ? Ui.Tag(I18n.L("正在使用"), "B.OnAccentStrong", "B.AccentStrong") : (ai ? Ui.Tag(I18n.L("AI 备份"), "B.Info", "B.InfoSoft") : (previous ? Ui.Tag(I18n.L("自动留底")) : Ui.Tag(I18n.L("方案")))));
		string meta = (ai ? I18n.L("AI 改布局前自动保存，保留最近 {0} 份", TipCL.Core.Launcher.Ai.LayoutTools.MaxBackups) : ProfileMeta(doc.GetProfile(name)));
		TextBlock title = Ui.Txt(name, 13.5, bold: true);
		title.TextTrimming = TextTrimming.CharacterEllipsis;
		title.ToolTip = name;
		TextBlock sub = Ui.Small(meta).Wrap();
		Button apply = (active ? Ui.Btn(I18n.L("当前方案"), BtnKind.Ghost) : Ui.Btn(I18n.L("应用"), BtnKind.Soft, delegate
		{
			ApplyProfile(name);
		}));
		apply.IsEnabled = !active;
		Button more = Ui.IconBtn("\ue712", I18n.L("更多"), null, 14.0);
		more.Click += delegate
		{
			ShowProfileMenu(more, name, active);
		};
		Grid bottom = Ui.G(null, "*,Auto");
		bottom.Add(apply.Left(), 0);
		bottom.Add(more, 0, 1);
		Border card = Ui.Card(Ui.V(6.0, tag.Left(), title, sub, bottom), 12.0);
		card.Margin = new Thickness(0.0, 0.0, 10.0, 10.0);
		if (active)
		{
			card.SetResourceReference(Border.BorderBrushProperty, "B.Accent");
			card.BorderThickness = new Thickness(1.5);
		}
		return card;
	}

	// 「3 个页面自定义 · 标准间距」
	private static string ProfileMeta(LayoutBody? body)
	{
		if (body == null)
		{
			return "";
		}
		int pages = (body.Pages != null) ? body.Pages.Count : 0;
		string first = ((pages > 0) ? I18n.L("{0} 个页面自定义", pages) : I18n.L("页面都是默认排列"));
		return first + " · " + I18n.L("{0}间距", DensityLabel(body.Shell?.Density));
	}

	private void ShowProfileMenu(Button anchor, string name, bool active)
	{
		ContextMenu menu = new ContextMenu
		{
			PlacementTarget = anchor,
			Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
		};
		MenuItem rename = new MenuItem
		{
			Header = I18n.L("重命名")
		};
		rename.Click += delegate
		{
			PageBase.Run(() => RenameProfileAsync(name));
		};
		MenuItem copy = new MenuItem
		{
			Header = I18n.L("复制一份")
		};
		copy.Click += delegate
		{
			PageBase.Run(() => DuplicateProfileAsync(name));
		};
		MenuItem del = new MenuItem
		{
			Header = I18n.L("删除")
		};
		del.SetResourceReference(Control.ForegroundProperty, "B.Danger");
		del.Click += delegate
		{
			if (active)
			{
				PageBase.Toast(I18n.L("正在使用的方案不能删除"), I18n.L("先切到别的方案"), ToastKind.Warning);
				return;
			}
			PageBase.Run(() => DeleteProfileAsync(name));
		};
		menu.Items.Add(rename);
		menu.Items.Add(copy);
		menu.Items.Add(new Separator());
		menu.Items.Add(del);
		menu.IsOpen = true;
	}

	private async System.Threading.Tasks.Task NewProfileAsync()
	{
		string? name = await Dlg.Prompt(I18n.L("存为新方案"), I18n.L("方案名"), I18n.L("我的布局 {0}", DateTime.Now.ToString("MM-dd HH:mm")));
		if (name == null)
		{
			return;
		}
		string error = LayoutProfiles.Create(name, fromCurrent: true);
		if (error.Length > 0)
		{
			PageBase.Toast(I18n.L("布局未能保存"), error, ToastKind.Warning);
			return;
		}
		PageBase.Toast(I18n.L("已把当前布局存为「{0}」", name), "", ToastKind.Success);
		RefreshLayoutView();
	}

	private void ApplyProfile(string name)
	{
		string error = LayoutProfiles.Apply(name, out string kept);
		if (error.Length > 0)
		{
			PageBase.Toast(I18n.L("布局未能保存"), error, ToastKind.Warning);
			return;
		}
		// 应用前旧布局已自动留底成「上一次布局」:换错了能用同名方案切回
		PageBase.Toast(I18n.L("已切换到「{0}」，所有页面已重排", name), (kept.Length > 0) ? I18n.L("原布局已留底为「{0}」", kept) : "", ToastKind.Success);
		RefreshLayoutView();
	}

	private async System.Threading.Tasks.Task RenameProfileAsync(string name)
	{
		string? next = await Dlg.Prompt(I18n.L("重命名"), I18n.L("方案名"), name);
		if (next == null || next == name)
		{
			return;
		}
		string error = LayoutProfiles.Rename(name, next);
		if (error.Length > 0)
		{
			PageBase.Toast(I18n.L("布局未能保存"), error, ToastKind.Warning);
			return;
		}
		RefreshLayoutView();
	}

	private async System.Threading.Tasks.Task DuplicateProfileAsync(string name)
	{
		string? next = await Dlg.Prompt(I18n.L("复制一份"), I18n.L("方案名"), I18n.L("{0} 副本", name));
		if (next == null)
		{
			return;
		}
		string error = LayoutProfiles.Duplicate(name, next);
		if (error.Length > 0)
		{
			PageBase.Toast(I18n.L("布局未能保存"), error, ToastKind.Warning);
			return;
		}
		PageBase.Toast(I18n.L("已复制一份"), next, ToastKind.Success);
		RefreshLayoutView();
	}

	private async System.Threading.Tasks.Task DeleteProfileAsync(string name)
	{
		if (!await Dlg.Confirm(I18n.L("删除"), I18n.L("删除布局方案「{0}」?", name), I18n.L("删除"), I18n.L("取消"), danger: true))
		{
			return;
		}
		string error = LayoutProfiles.Delete(name);
		if (error.Length > 0)
		{
			PageBase.Toast(I18n.L("布局未能保存"), error, ToastKind.Warning);
			return;
		}
		PageBase.Toast(I18n.L("布局方案「{0}」已删除", name), "", ToastKind.Success);
		RefreshLayoutView();
	}

	// ——— 导入 / 导出(设置这里叫「导出整套布局」;编辑态里叫「导出本页」) ———

	private async System.Threading.Tasks.Task ExportLayoutAsync()
	{
		int choice = await Dlg.Choose(I18n.L("导出整套布局"), Ui.Muted(I18n.L("导出全部页签、页面排列、分栏和组件。旧版本只认页面顺序和显隐，需要给旧版本用时选「只导出页面顺序」。")).Wrap(), new string[3]
		{
			I18n.L("取消"),
			I18n.L("只导出页面顺序"),
			I18n.L("导出整套布局")
		}, 560.0);
		if (choice == 2)
		{
			string text = LayoutStoreV2.ExportJson(LayoutStoreV2.Load());
			string? path = Dlg.SaveFile(I18n.L("布局 (*.json)|*.json|全部文件|*.*"), "tipcl-layout-v2.json", I18n.L("导出整套布局"));
			if (path != null && WriteTextFile(path, text))
			{
				PageBase.Toast(I18n.L("已导出整套布局"), path, ToastKind.Success);
			}
			return;
		}
		if (choice == 1)
		{
			string text = PageLayoutStore.ExportJson(LayoutV1Projection.ProjectV1(LayoutStoreV2.Load()));
			string? path2 = Dlg.SaveFile(I18n.L("布局 (*.json)|*.json|全部文件|*.*"), "tipcl-page-layout.json", I18n.L("只导出页面顺序"));
			if (path2 != null && WriteTextFile(path2, text))
			{
				PageBase.Toast(I18n.L("已导出页面顺序"), path2, ToastKind.Success);
			}
		}
	}

	private async System.Threading.Tasks.Task ImportLayoutFileAsync()
	{
		string? path = Dlg.PickFile(I18n.L("布局 (*.json)|*.json|全部文件|*.*"), I18n.L("导入布局"));
		if (path == null)
		{
			return;
		}
		string text;
		try
		{
			text = File.ReadAllText(path);
		}
		catch (Exception ex)
		{
			PageBase.Toast(I18n.L("布局读取失败"), ex.Message, ToastKind.Error);
			return;
		}
		LayoutV2? whole = LayoutV2.FromJsonText(text);
		if (whole != null)
		{
			int choice = await Dlg.Choose(I18n.L("导入布局"), Ui.Muted(I18n.L("这份文件是一整套布局（页签、页面排列、分栏和组件）。要直接用，还是先存成方案？")).Wrap(), new string[3]
			{
				I18n.L("取消"),
				I18n.L("存为新方案"),
				I18n.L("应用为当前布局")
			}, 560.0);
			if (choice == 2)
			{
				string error = LayoutProfiles.ApplySnapshot(whole.Layout, out string kept);
				if (error.Length > 0)
				{
					PageBase.Toast(I18n.L("布局未能保存"), error, ToastKind.Warning);
					return;
				}
				PageBase.Toast(I18n.L("已应用导入的布局"), (kept.Length > 0) ? I18n.L("原布局已留底为「{0}」", kept) : System.IO.Path.GetFileName(path), ToastKind.Success);
				RefreshLayoutView();
			}
			else if (choice == 1)
			{
				string? name = await Dlg.Prompt(I18n.L("存为新方案"), I18n.L("方案名"), System.IO.Path.GetFileNameWithoutExtension(path));
				if (name == null)
				{
					return;
				}
				string error2 = LayoutProfiles.CreateFrom(name, whole.Layout);
				if (error2.Length > 0)
				{
					PageBase.Toast(I18n.L("布局未能保存"), error2, ToastKind.Warning);
					return;
				}
				PageBase.Toast(I18n.L("布局方案「{0}」已保存", name), "", ToastKind.Success);
				RefreshLayoutView();
			}
			return;
		}
		// 旧版本导出的页面布局:把每页的 {id,hidden} 与顺序并进当前文档的 pages
		try
		{
			IReadOnlyDictionary<string, PageLayoutDoc> pages = PageLayoutStore.ParseImport(text);
			LayoutV2 doc = LayoutStoreV2.Load();
			int num = LayoutProfiles.MergeV1Pages(doc, pages);
			if (!LayoutStoreV2.Save(doc))
			{
				PageBase.Toast(I18n.L("布局未能保存"), I18n.L("改动只保留在本次运行"), ToastKind.Warning);
				return;
			}
			PageBase.Toast(I18n.L("已导入 {0} 个页面的布局（下次打开对应页面生效）", num), System.IO.Path.GetFileName(path), ToastKind.Success);
			RefreshLayoutView();
		}
		catch (Exception ex2)
		{
			PageBase.Toast(I18n.L("不是有效的布局文件"), ex2.Message, ToastKind.Error);
		}
	}

	private static bool WriteTextFile(string path, string text)
	{
		try
		{
			File.WriteAllText(path, text);
			return true;
		}
		catch (Exception ex)
		{
			PageBase.Toast(I18n.L("布局未能保存"), ex.Message, ToastKind.Error);
			return false;
		}
	}

	// ————— 5 高级(默认折叠):分栏宽度 / 网页和按钮组件(+ 自建组件管理)/ 布局文件 —————
	// 三个区块(splits / widgets / json)共用一个折叠开关:标题与开关在 splits 那块,另两块跟着显隐。

	private UIElement[] LayoutAdvancedParts(LayoutV2 doc)
	{
		System.Windows.Shapes.Path chevron = Lucide.Icon(_layoutAdvOpen ? IconChevronUp : IconChevronDown, 14.0, "B.InkMuted");
		Grid header = Ui.G(null, "*,Auto");
		header.Add(Ui.V(2.0, Ui.Txt(I18n.L("分栏、网页与按钮组件、布局文件"), 13.0, bold: true), Ui.Small(I18n.L("一般不需要动"))), 0);
		header.Add(chevron.VCenter(), 0, 1);
		Border headerHit = new Border
		{
			Background = Brushes.Transparent,
			Cursor = Cursors.Hand,
			Padding = new Thickness(0.0, 4.0, 0.0, 4.0),
			Child = header
		};
		System.Windows.Automation.AutomationProperties.SetName(headerHit, I18n.L("高级"));
		Border sep = new Border
		{
			Height = 1.0,
			Margin = new Thickness(-12.0, 8.0, -12.0, 1.0)
		};
		sep.SetResourceReference(Border.BackgroundProperty, "B.SegRail");
		Grid splitRow = LayoutSplitRow();
		splitRow.MinHeight = 40.0;
		SPanel splitBody = Ui.V(0.0, sep, splitRow);
		Border headCard = Ui.Card(Ui.V(0.0, headerHit, splitBody));
		UIElement part0 = Ui.V(10.0, Gt(I18n.L("高级")), headCard);
		Border part1 = Ui.Card(Rows(LayoutWidgetRows(doc)));
		Border part2 = Ui.Card(Rows(LayoutFileRow()));
		Action sync = delegate
		{
			Visibility shown = (_layoutAdvOpen ? Visibility.Visible : Visibility.Collapsed);
			splitBody.Visibility = shown;
			part1.Visibility = shown;
			part2.Visibility = shown;
			chevron.Data = Geometry.Parse(_layoutAdvOpen ? IconChevronUp : IconChevronDown);
		};
		headerHit.MouseLeftButtonUp += delegate
		{
			_layoutAdvOpen = !_layoutAdvOpen;
			sync();
		};
		sync();
		_layoutAdvAnchor = headCard;
		return new UIElement[3] { part0, part1, part2 };
	}

	// 分栏宽度:13 行的表 → 一行「已调整 N 处:… / 都是默认宽度」+「全部复位」
	private Grid LayoutSplitRow()
	{
		List<SplitSite> stored = SplitCatalog.StoredSites();
		string text;
		if (stored.Count == 0)
		{
			text = I18n.L("都是默认宽度。在页面上拖两栏中间的分隔线即可调整，双击复位。");
		}
		else
		{
			List<string> names = new List<string>();
			foreach (SplitSite site in stored)
			{
				string label = SplitCatalog.DisplayName(site);
				if (!names.Contains(label))
				{
					names.Add(label);
				}
			}
			text = I18n.L("已调整 {0} 处：{1}。在页面上拖两栏中间的分隔线即可调整，双击复位。", stored.Count, string.Join(I18n.L("、"), names));
		}
		Button resetAll = Ui.Btn(I18n.L("全部复位"), BtnKind.Normal, delegate
		{
			ResetAllSplits();
		});
		resetAll.IsEnabled = stored.Count > 0;
		return LayoutRow(I18n.L("分栏宽度"), text, resetAll);
	}

	private void ResetAllSplits()
	{
		int count = 0;
		foreach (SplitSite site in SplitCatalog.All)
		{
			if (site.IsTwoColumn && SplitRatioStore.Clear(site.PageId, site.SplitId))
			{
				count++;
			}
		}
		PageBase.Toast((count > 0) ? I18n.L("分栏宽度已全部复位") : I18n.L("没有需要复位的分栏"), (count > 0) ? I18n.L("已复位 {0} 处", count) : "", ToastKind.Success);
		RefreshLayoutView();
	}

	// 布局文件:打开所在文件夹 / 在编辑器中打开…(单独窗口)
	private FrameworkElement[] LayoutFileRow()
	{
		Button folder = Ui.Btn(I18n.L("打开所在文件夹"), BtnKind.Normal, delegate
		{
			OpenLayoutFolder();
		});
		Button json = Ui.Btn(I18n.L("在编辑器中打开…"), BtnKind.Normal, delegate
		{
			LayoutJsonWindow.ShowFor(RefreshLayoutView);
		});
		return new FrameworkElement[1] { LayoutRow(I18n.L("布局文件"), I18n.L("用 JSON 编辑整套布局，适合批量修改或分享给别人"), Ui.H(6.0, folder, json)) };
	}

	private static void OpenLayoutFolder()
	{
		try
		{
			string file = LayoutStoreV2.FilePath;
			if (File.Exists(file))
			{
				Process.Start("explorer.exe", "/select,\"" + file + "\"");
				return;
			}
			string dir = LayoutStoreV2.DirPath;
			if (!Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}
			Process.Start("explorer.exe", "\"" + dir + "\"");
		}
		catch (Exception ex)
		{
			PageBase.Toast(I18n.L("打不开文件夹"), ex.Message, ToastKind.Warning);
		}
	}

	// ——— 网页和按钮组件(全局开关)+ 自建组件管理(原「自建区块」小节,收进高级) ———

	// 面板开合状态跨视图重建保留
	private bool _layoutWidgetPanelOpen;

	private bool _layoutWidgetCreateOpen;

	private FrameworkElement[] LayoutWidgetRows(LayoutV2 doc)
	{
		List<FrameworkElement> rows = new List<FrameworkElement>();
		CheckBox danger = Ui.Switch(LayoutSettingsPrefs.DangerWidgetsEnabled);
		System.Windows.Automation.AutomationProperties.SetName(danger, I18n.L("网页和按钮组件"));
		Border warn = Ui.Pane(Ui.Txt(I18n.L("导入的布局里如果带网页或按钮组件，仍然默认关闭，需要你逐个打开。"), 12.0, bold: false, "B.InkSoft").Wrap(), "B.WarnSoft", 8.0, 8.0);
		warn.Visibility = ((danger.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed);
		danger.Checked += delegate
		{
			if (_layoutRebuilding)
			{
				return;
			}
			LayoutSettingsPrefs.DangerWidgetsEnabled = true;
			warn.Visibility = Visibility.Visible;
			PageBase.Toast(I18n.L("已开启网页和按钮组件"), I18n.L("页面编辑的「添加」里多了网页和按钮"), ToastKind.Success);
		};
		danger.Unchecked += delegate
		{
			if (_layoutRebuilding)
			{
				return;
			}
			LayoutSettingsPrefs.DangerWidgetsEnabled = false;
			warn.Visibility = Visibility.Collapsed;
			PageBase.Toast(I18n.L("已关闭网页和按钮组件"), "", ToastKind.Success);
		};
		rows.Add(Ui.V(8.0, LayoutRow(I18n.L("网页和按钮组件"), I18n.L("开启后可以添加嵌入网页（只接受 https）和执行动作的按钮"), danger), warn));
		rows.AddRange(LayoutWidgetAdminRows(doc));
		return rows.ToArray();
	}

	private FrameworkElement[] LayoutWidgetAdminRows(LayoutV2 doc)
	{
		List<WidgetDef> widgets = ((doc.Layout.Widgets != null) ? doc.Layout.Widgets : new List<WidgetDef>());
		List<FrameworkElement> rows = new List<FrameworkElement>();
		Button manage = Ui.Btn(_layoutWidgetPanelOpen ? I18n.L("收起") : I18n.L("管理…"), BtnKind.Normal, null);
		manage.Click += delegate
		{
			_layoutWidgetPanelOpen = !_layoutWidgetPanelOpen;
			if (!_layoutWidgetPanelOpen)
			{
				_layoutWidgetCreateOpen = false;
			}
			RefreshLayoutView();
		};
		string count = ((widgets.Count > 0) ? I18n.L("已保存 {0} 个", widgets.Count) : I18n.L("还没有"));
		rows.Add(LayoutRow(I18n.L("自建组件"), I18n.L("便签、快捷启动、时钟、分隔线、游戏时长。平时在页面编辑的「添加」里放进页面，这里改名字和内容。"), Ui.H(8.0, Ui.Small(count).VCenter(), manage)));
		if (!_layoutWidgetPanelOpen)
		{
			return rows.ToArray();
		}
		// 新建表单:只列出 5 种普通组件(网页 / 按钮没有创建入口)
		if (_layoutWidgetCreateOpen)
		{
			rows.Add(WidgetCreateForm());
		}
		else
		{
			Button create = Ui.Btn(I18n.L("新建组件"), BtnKind.Primary, delegate
			{
				_layoutWidgetCreateOpen = true;
				RefreshLayoutView();
			});
			rows.Add(LayoutRow(I18n.L("新建"), I18n.L("新建的组件不会自动出现在页面上，要在页面编辑的「添加」里放进去"), create));
		}
		foreach (WidgetDef widget in widgets)
		{
			if (widget != null)
			{
				rows.Add(WidgetAdminRow(doc, widget));
			}
		}
		return rows.ToArray();
	}

	// 新建表单:类型 / 标题 / 参数(按类型切占位与显隐)/ 创建
	private FrameworkElement WidgetCreateForm()
	{
		string[] labels = SafeTypeLabels();
		ComboBox type = Ui.Combo(labels, labels[0], 150.0);
		TextBox title = Ui.Input(I18n.L("例如:今天的待办"));
		TextBox param = Ui.Input("");
		param.Width = 220.0;
		TextBox version = Ui.Input(I18n.L("版本(可空)"));
		version.Width = 160.0;
		TextBlock hint = Ui.Small("").Wrap();
		SPanel paramRow = Ui.H(8.0, param, version);
		version.Visibility = Visibility.Collapsed;
		Action applyType = delegate
		{
			int index = Math.Max(0, Math.Min(type.SelectedIndex, WidgetRegistry.SafeTypes.Length - 1));
			string key = WidgetLibrary.ParameterKey(WidgetRegistry.SafeTypes[index]);
			bool needParam = (key.Length > 0);
			param.Visibility = (needParam ? Visibility.Visible : Visibility.Collapsed);
			version.Visibility = ((needParam && string.Equals(key, "instance", StringComparison.Ordinal)) ? Visibility.Visible : Visibility.Collapsed);
			if (string.Equals(key, "text", StringComparison.Ordinal))
			{
				param.Tag = I18n.L("文本内容");
				hint.Text = I18n.L("便签内容会显示在本页的卡片里");
			}
			else if (string.Equals(key, "instance", StringComparison.Ordinal))
			{
				param.Tag = I18n.L("实例名");
				hint.Text = I18n.L("实例名必填,版本可空");
			}
			else if (string.Equals(key, "format", StringComparison.Ordinal))
			{
				param.Tag = I18n.L("时间格式(留空默认 HH:mm)");
				hint.Text = I18n.L("按 .NET 时间格式填写,例如 HH:mm");
			}
			else
			{
				hint.Text = I18n.L("该类型不需要参数");
			}
		};
		type.SelectionChanged += delegate
		{
			if (!_layoutRebuilding)
			{
				applyType();
			}
		};
		applyType();
		Button create = Ui.Btn(I18n.L("创建"), BtnKind.Primary, delegate
		{
			int index = Math.Max(0, Math.Min(type.SelectedIndex, WidgetRegistry.SafeTypes.Length - 1));
			string error = "";
			LayoutV2 fresh = LayoutStoreV2.Load();
			WidgetDef? widget = WidgetLibrary.CreateFor(fresh, WidgetRegistry.SafeTypes[index], title.Text, param.Text, version.Text, out error);
			if (widget == null)
			{
				PageBase.Toast(I18n.L("组件未能保存"), error, ToastKind.Warning);
				return;
			}
			fresh.Layout.Widgets.Add(widget);
			if (SaveWidgetDoc(fresh, I18n.L("组件已创建")))
			{
				_layoutWidgetCreateOpen = false;
				RefreshLayoutView();
			}
		});
		Button cancel = Ui.Btn(I18n.L("取消"), BtnKind.Chip, delegate
		{
			_layoutWidgetCreateOpen = false;
			RefreshLayoutView();
		});
		return Ui.V(2.0,
			Ui.Field(I18n.L("类型"), type, I18n.L("便签、快捷启动、时钟、分隔线、游戏时长")),
			Ui.Field(I18n.L("标题"), title, I18n.L("列表里显示的名字")),
			Ui.Field(I18n.L("参数"), paramRow, ""),
			Ui.H(8.0, create, cancel),
			hint.M(0.0, 0.0, 0.0, 6.0));
	}

	// 已有定义:标题 / 类型 / 引用数 + 参数编辑 + 保存 / 删除
	private FrameworkElement WidgetAdminRow(LayoutV2 doc, WidgetDef widget)
	{
		int refs = WidgetLibrary.CountReferences(doc, widget.Id);
		bool safe = WidgetLibrary.IsSafeType(widget.Type);
		TextBox title = Ui.Input("", widget.Title ?? "");
		title.Width = 180.0;
		TextBlock refText = Ui.Small(I18n.L("被 {0} 个页面引用", refs));
		SPanel head = Ui.H(8.0, title, Ui.Tag(WidgetUI.TypeLabel(widget.Type)), refText.VCenter());
		Button save = Ui.Btn(I18n.L("保存"), BtnKind.Soft, null);
		Button del = Ui.Btn(I18n.L("删除"), BtnKind.Ghost, null);
		TextBox? paramBox = null;
		TextBox? versionBox = null;
		SPanel body;
		if (safe)
		{
			string key = WidgetLibrary.ParameterKey(widget.Type);
			paramBox = Ui.Input("", WidgetLibrary.ParameterOf(widget));
			paramBox.Width = 180.0;
			versionBox = Ui.Input(I18n.L("版本(可空)"), WidgetLibrary.VersionOf(widget));
			versionBox.Width = 140.0;
			bool isShortcut = string.Equals(widget.Type, LayoutSchema.WidgetShortcut, StringComparison.Ordinal);
			paramBox.Visibility = ((key.Length > 0) ? Visibility.Visible : Visibility.Collapsed);
			versionBox.Visibility = (isShortcut ? Visibility.Visible : Visibility.Collapsed);
			paramBox.Tag = ParamPlaceholder(key);
			body = Ui.V(6.0, head, Ui.H(8.0, Ui.Small(I18n.L("参数")).VCenter(), paramBox, versionBox, save, del));
		}
		else
		{
			body = Ui.V(6.0, head, Ui.H(8.0, Ui.Small(I18n.L("网页 / 按钮组件的参数请在布局文件里改")).VCenter(), save, del));
		}
		save.Click += delegate
		{
			string error = "";
			LayoutV2 fresh = LayoutStoreV2.Load();
			WidgetDef? target = FindWidget(fresh, widget.Id);
			if (target == null)
			{
				PageBase.Toast(I18n.L("组件不存在"), "", ToastKind.Warning);
				return;
			}
			if (!WidgetLibrary.TryRename(target, title.Text, out error))
			{
				PageBase.Toast(I18n.L("组件未能保存"), error, ToastKind.Warning);
				return;
			}
			if (safe)
			{
				string paramText = ((paramBox != null) ? paramBox.Text : "");
				string versionText = ((versionBox != null && versionBox.Visibility == Visibility.Visible) ? versionBox.Text : "");
				if (!WidgetLibrary.TryUpdateSettings(target, paramText, versionText, out error))
				{
					PageBase.Toast(I18n.L("组件未能保存"), error, ToastKind.Warning);
					return;
				}
			}
			if (SaveWidgetDoc(fresh, I18n.L("组件「{0}」已保存", target.Title)))
			{
				RefreshLayoutView();
			}
		};
		del.Click += delegate
		{
			PageBase.Run(async delegate
			{
				string name = widget.Title ?? widget.Id;
				string body2 = ((refs > 0) ? I18n.L("删除这个组件，并从 {0} 个页面上移除（不可撤销）", refs) : I18n.L("删除这个组件（不可撤销）"));
				if (!await Dlg.Confirm(I18n.L("删除组件「{0}」？", name), body2, I18n.L("删除"), I18n.L("取消"), danger: true))
				{
					return;
				}
				LayoutV2 fresh = LayoutStoreV2.Load();
				int removed = WidgetLibrary.Delete(fresh, widget.Id);
				if (removed < 0)
				{
					PageBase.Toast(I18n.L("组件不存在"), "", ToastKind.Warning);
					return;
				}
				if (SaveWidgetDoc(fresh, I18n.L("组件「{0}」已删除", name)))
				{
					RefreshLayoutView();
				}
			});
		};
		return body;
	}

	private static string ParamPlaceholder(string key)
	{
		if (string.Equals(key, "text", StringComparison.Ordinal))
		{
			return I18n.L("文本内容");
		}
		if (string.Equals(key, "instance", StringComparison.Ordinal))
		{
			return I18n.L("实例名");
		}
		if (string.Equals(key, "format", StringComparison.Ordinal))
		{
			return I18n.L("时间格式");
		}
		return "";
	}

	private static string[] SafeTypeLabels()
	{
		string[] labels = new string[WidgetRegistry.SafeTypes.Length];
		for (int i = 0; i < WidgetRegistry.SafeTypes.Length; i++)
		{
			labels[i] = WidgetUI.TypeLabel(WidgetRegistry.SafeTypes[i]);
		}
		return labels;
	}

	private static WidgetDef? FindWidget(LayoutV2 doc, string widgetId)
	{
		if (doc.Layout.Widgets == null)
		{
			return null;
		}
		foreach (WidgetDef widget in doc.Layout.Widgets)
		{
			if (widget != null && string.Equals(widget.Id, widgetId, StringComparison.Ordinal))
			{
				return widget;
			}
		}
		return null;
	}

	// 组件管理专用的落盘(与 WriteLayout 同规矩,但需要知道成功与否来刷新面板)
	private static bool SaveWidgetDoc(LayoutV2 doc, string toast)
	{
		try
		{
			if (!LayoutStoreV2.Save(doc))
			{
				PageBase.Toast(I18n.L("布局未能保存"), I18n.L("改动只保留在本次运行"), ToastKind.Warning);
				return false;
			}
			PageBase.Toast(toast, "", ToastKind.Success);
			return true;
		}
		catch (Exception ex)
		{
			PageBase.Toast(I18n.L("布局未能保存"), ex.Message, ToastKind.Error);
			return false;
		}
	}

	// ————— 共用:落盘唯一入口 = LayoutStoreV2 —————

	// load → 改 → Save;写不进去只提示,不崩界面(与 PageLayoutStore 的既有范式一致)
	private static void WriteLayout(Action<LayoutV2> edit, string toast)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			edit(doc);
			if (!LayoutStoreV2.Save(doc))
			{
				// 写盘失败(被独占/没有权限):内存里已是最新值,但不能说"已保存"
				PageBase.Toast(I18n.L("布局未能保存"), I18n.L("改动只保留在本次运行"), ToastKind.Warning);
				return;
			}
			PageBase.Toast(toast, "", ToastKind.Success);
		}
		catch (Exception ex)
		{
			PageBase.Toast(I18n.L("布局未能保存"), ex.Message, ToastKind.Error);
		}
	}
}

/// <summary>
/// 设置页「布局」视图里「页面区块」小节的入口表(纯数据、零 WPF 依赖,net8 测试宿主可直接断言):
/// 覆盖全仓 12 个 BlockHost 宿主。D1 把 <c>LayoutEditor.CanEdit</c> 从 specs ≥ 2 放宽到 ≥ 1 之后,
/// 单块页(settings:network / settings:about)同样能进编辑态,所以表里一并列出
/// —— 单块页能调的东西少(外观 / 标题 / 先隐藏),但入口不该缺。
/// </summary>
public static class LayoutBlockEntryCatalog
{
	/// <summary>一个页面区块入口:pageId + 导航 id + 设置页视图下标(-1 = 不在设置页里)+ 文案 key。</summary>
	public sealed class Entry
	{
		/// <summary>布局文档里的 pageId(与 BlockHost 构造时传的 pageId 一致)。</summary>
		public string PageId { get; set; } = "";

		/// <summary>MainWindow 的导航 id(先跳到该页,再找它的 BlockHost)。</summary>
		public string NavId { get; set; } = "";

		/// <summary>该页在设置页里时先切到哪个视图;≥ 0 表示先 Select(该视图)再找宿主,-1 = 不在设置页里。</summary>
		public int SettingsView { get; set; } = -1;

		/// <summary>入口文案的 I18n key(复用既有导航文案)。</summary>
		public string Label { get; set; } = "";

		/// <summary>是否单块页(只有一个块:能调外观 / 标题 / 隐藏,不能重排)。</summary>
		public bool SingleBlock { get; set; }

		/// <summary>
		/// 找不到宿主时的提示文案 key(空 = 通用提示)。安装页 / 实例详情里的版本管理这类"子视图"
		/// 不能直接跳过去,只能先把它打开再右键,提示就写清楚怎么打开。
		/// </summary>
		public string Hint { get; set; } = "";
	}

	/// <summary>17 个宿主:设置页内 8 个 + 顶栏导航 7 个 + 子视图 2 个(安装页、实例详情里内嵌的版本管理)。</summary>
	public static readonly Entry[] All = new Entry[17]
	{
		new Entry
		{
			PageId = "settings:general",
			NavId = "settings",
			SettingsView = 0,
			Label = "通用"
		},
		new Entry
		{
			PageId = "settings:network",
			NavId = "settings",
			SettingsView = 3,
			Label = "下载与网络",
			SingleBlock = true
		},
		new Entry
		{
			PageId = "settings:about",
			NavId = "settings",
			SettingsView = 6,
			Label = "反馈与关于",
			SingleBlock = true
		},
		new Entry
		{
			PageId = "java",
			NavId = "settings",
			SettingsView = 2,
			Label = "Java"
		},
		new Entry
		{
			PageId = "servers",
			NavId = "settings",
			SettingsView = 4,
			Label = "服务器"
		},
		new Entry
		{
			PageId = "playtime",
			NavId = "settings",
			SettingsView = 5,
			Label = "游戏时长"
		},
		new Entry
		{
			PageId = "feedback",
			NavId = "settings",
			SettingsView = 6,
			Label = "反馈与帮助"
		},
		new Entry
		{
			PageId = "launch",
			NavId = "launch",
			Label = "启动"
		},
		new Entry
		{
			PageId = "download",
			NavId = "download",
			Label = "下载"
		},
		new Entry
		{
			PageId = "account",
			NavId = "account",
			Label = "账号"
		},
		new Entry
		{
			PageId = "tasks",
			NavId = "tasks",
			Label = "下载任务"
		},
		new Entry
		{
			PageId = "multiplayer",
			NavId = "multiplayer",
			Label = "联机"
		},
		new Entry
		{
			PageId = "settings:layout",
			NavId = "settings",
			SettingsView = SettingsPage.LayoutView,
			Label = "布局"
		},
		new Entry
		{
			PageId = "ai",
			NavId = "ai",
			Label = "AI 助手"
		},
		new Entry
		{
			PageId = "version-manage",
			NavId = "instance",
			Label = "版本管理"
		},
		new Entry
		{
			PageId = "install",
			NavId = "download",
			Label = "版本安装",
			Hint = "先在「下载」页点某个版本的「安装」打开安装页,再在页面上右键「自定义布局...」"
		},
		new Entry
		{
			PageId = "version-manage:embedded",
			NavId = "library",
			Label = "实例详情 · 设置",
			Hint = "先在「实例库」打开实例详情并切到「设置」分段,再在页面上右键「自定义布局...」"
		}
	};

	/// <summary>单块页的 pageId 列表(报告与测试用)。</summary>
	public static List<string> SingleBlockPageIds()
	{
		List<string> list = new List<string>();
		foreach (Entry entry in All)
		{
			if (entry.SingleBlock)
			{
				list.Add(entry.PageId);
			}
		}
		return list;
	}

	/// <summary>按 pageId 取入口(没有返回 null)。</summary>
	public static Entry? Find(string? pageId)
	{
		if (string.IsNullOrEmpty(pageId))
		{
			return null;
		}
		foreach (Entry entry in All)
		{
			if (entry.PageId == pageId)
			{
				return entry;
			}
		}
		return null;
	}
}

/// <summary>
/// 整机布局方案(设计 §4.C):纯逻辑、零 WPF 依赖,落盘一律走 LayoutStoreV2。
/// 方法返回 "" = 成功,否则是给用户看的中文错误;应用方案 = 原子替换整个 layout 再 Save。
/// </summary>
public static class LayoutProfiles
{
	/// <summary>方案名长度上限(防御:避免超长 key 撑爆 JSON);界面与校验共用同一个数。</summary>
	public const int ProfileNameMax = 40;

	/// <summary>方案名列表(文档顺序)。</summary>
	public static List<string> Names()
	{
		try
		{
			return LayoutStoreV2.Load().Profiles.Keys.ToList();
		}
		catch
		{
			return new List<string>();
		}
	}

	/// <summary>方案个数。</summary>
	public static int Count()
	{
		return Names().Count;
	}

	/// <summary>当前激活的方案名(空 = 直接编辑态)。</summary>
	public static string ActiveName()
	{
		try
		{
			return LayoutStoreV2.Load().ActiveProfile ?? "";
		}
		catch
		{
			return "";
		}
	}

	/// <summary>取方案快照(null = 不存在)。</summary>
	public static LayoutBody? Snapshot(string? name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}
		try
		{
			return LayoutStoreV2.Load().GetProfile(name);
		}
		catch
		{
			return null;
		}
	}

	/// <summary>方案名校验:"" = 可用。</summary>
	public static string ValidateName(string? name, string? ignoreExisting)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return I18n.L("方案名不能为空");
		}
		string trimmed = name.Trim();
		if (trimmed.Length > ProfileNameMax)
		{
			return I18n.L("方案名太长(最多 {0} 个字)", ProfileNameMax);
		}
		if (trimmed != ignoreExisting && Names().Contains(trimmed))
		{
			return I18n.L("方案「{0}」已经存在", trimmed);
		}
		return "";
	}

	/// <summary>新建方案:fromCurrent = true 时以当前 layout 为基线(§4.C「新建(以当前为基线)」)。</summary>
	public static string Create(string? name, bool fromCurrent)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			string error = ValidateName(name, null);
			if (error.Length > 0)
			{
				return error;
			}
			string trimmed = name!.Trim();
			doc.Profiles[trimmed] = (fromCurrent ? doc.Layout.Clone() : new LayoutBody());
			doc.ActiveProfile = trimmed;
			return LayoutStoreV2.Save(doc) ? "" : I18n.L("布局未能保存");
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	/// <summary>用给定快照建方案(导入整机方案时用)。</summary>
	public static string CreateFrom(string? name, LayoutBody? body)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			string error = ValidateName(name, null);
			if (error.Length > 0)
			{
				return error;
			}
			string trimmed = name!.Trim();
			doc.Profiles[trimmed] = ((body != null) ? body.Clone() : new LayoutBody());
			doc.ActiveProfile = trimmed;
			return LayoutStoreV2.Save(doc) ? "" : I18n.L("布局未能保存");
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	/// <summary>
	/// 应用方案 / 导入整机方案前自动留底的方案名(同名覆盖,只留一份):当前布局没原样存在激活方案里
	/// (手调过,或本来就是直接编辑态)时,换掉之前先存到这里,换错了可以在方案列表里切回。
	/// </summary>
	public static string PreviousName
	{
		get
		{
			return I18n.L("上一次布局");
		}
	}

	/// <summary>应用方案:原子替换 layout(整块换掉)并把 active_profile 记成它。</summary>
	public static string Apply(string? name)
	{
		return Apply(name, out _);
	}

	/// <summary>同上;<paramref name="kept"/> = 替换前把当前布局留底成的方案名(不必留底时为 "")。</summary>
	public static string Apply(string? name, out string kept)
	{
		kept = "";
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			LayoutBody? body = doc.GetProfile(name ?? "");
			if (body == null)
			{
				return I18n.L("布局方案「{0}」不存在", name ?? "");
			}
			string target = name!.Trim();
			LayoutBody next = body.Clone();
			kept = KeepPrevious(doc);
			doc.Layout = next;
			// 应用的正是「上一次布局」= 和当前布局对调:这个名字里现在存的是刚换下来的那份,不再是正在用的
			doc.ActiveProfile = ((kept.Length > 0 && string.Equals(kept, target, StringComparison.Ordinal)) ? "" : target);
			return LayoutStoreV2.Save(doc) ? "" : I18n.L("布局未能保存");
		}
		catch (Exception ex)
		{
			kept = "";
			return ex.Message;
		}
	}

	/// <summary>直接替换当前布局(导入整机方案):原子替换 + 回到直接编辑态(active_profile 清空)。</summary>
	public static string ApplySnapshot(LayoutBody? body)
	{
		return ApplySnapshot(body, out _);
	}

	/// <summary>同上;<paramref name="kept"/> 含义同 <see cref="Apply(string, out string)"/>。</summary>
	public static string ApplySnapshot(LayoutBody? body, out string kept)
	{
		kept = "";
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			kept = KeepPrevious(doc);
			doc.Layout = ((body != null) ? body.Clone() : new LayoutBody());
			doc.ActiveProfile = "";
			return LayoutStoreV2.Save(doc) ? "" : I18n.L("布局未能保存");
		}
		catch (Exception ex)
		{
			kept = "";
			return ex.Message;
		}
	}

	// 当前布局已经原样存在激活方案里就不必留底;否则存成「上一次布局」(覆盖上一份),返回留底的方案名
	private static string KeepPrevious(LayoutV2 doc)
	{
		LayoutBody? active = doc.GetProfile(doc.ActiveProfile ?? "");
		if (active != null && SameBody(active, doc.Layout))
		{
			return "";
		}
		string name = PreviousName;
		doc.Profiles[name] = doc.Layout.Clone();
		return name;
	}

	private static bool SameBody(LayoutBody a, LayoutBody b)
	{
		return string.Equals(JsonSerializer.Serialize(a.ToDict()), JsonSerializer.Serialize(b.ToDict()), StringComparison.Ordinal);
	}

	/// <summary>重命名方案(被激活的方案跟着改 active_profile)。</summary>
	public static string Rename(string? oldName, string? newName)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			if (string.IsNullOrEmpty(oldName) || !doc.Profiles.ContainsKey(oldName))
			{
				return I18n.L("布局方案「{0}」不存在", oldName ?? "");
			}
			string error = ValidateName(newName, oldName);
			if (error.Length > 0)
			{
				return error;
			}
			string trimmed = newName!.Trim();
			if (trimmed == oldName)
			{
				return "";
			}
			// 保序重建:方案列表的显示顺序不能因为改名而跳
			Dictionary<string, LayoutBody> rebuilt = new Dictionary<string, LayoutBody>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, LayoutBody> kv in doc.Profiles)
			{
				rebuilt[(kv.Key == oldName) ? trimmed : kv.Key] = kv.Value;
			}
			doc.Profiles = rebuilt;
			if (doc.ActiveProfile == oldName)
			{
				doc.ActiveProfile = trimmed;
			}
			return LayoutStoreV2.Save(doc) ? "" : I18n.L("布局未能保存");
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	/// <summary>复制方案。</summary>
	public static string Duplicate(string? name, string? newName)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			LayoutBody? body = doc.GetProfile(name ?? "");
			if (body == null)
			{
				return I18n.L("布局方案「{0}」不存在", name ?? "");
			}
			string error = ValidateName(newName, null);
			if (error.Length > 0)
			{
				return error;
			}
			doc.Profiles[newName!.Trim()] = body.Clone();
			return LayoutStoreV2.Save(doc) ? "" : I18n.L("布局未能保存");
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	/// <summary>删除方案(删的是当前激活方案时,active_profile 回到直接编辑态)。</summary>
	public static string Delete(string? name)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			if (string.IsNullOrEmpty(name) || !doc.Profiles.Remove(name))
			{
				return I18n.L("布局方案「{0}」不存在", name ?? "");
			}
			if (doc.ActiveProfile == name)
			{
				doc.ActiveProfile = "";
			}
			return LayoutStoreV2.Save(doc) ? "" : I18n.L("布局未能保存");
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	// ————— v3「页面」小节:单页 / 全部恢复默认(纯逻辑,落盘仍只走 LayoutStoreV2) —————

	/// <summary>该页在当前布局里是否被自定义过(主布局或任一窄窗/宽窗变体里存了这一页)。</summary>
	public static bool IsPageCustomized(LayoutV2? doc, string? pageId)
	{
		if (doc?.Layout == null || string.IsNullOrEmpty(pageId))
		{
			return false;
		}
		if (doc.Layout.Pages != null && doc.Layout.Pages.ContainsKey(pageId))
		{
			return true;
		}
		if (doc.Layout.Variants != null)
		{
			foreach (LayoutBody variant in doc.Layout.Variants.Values)
			{
				if (variant?.Pages != null && variant.Pages.ContainsKey(pageId))
				{
					return true;
				}
			}
		}
		return false;
	}

	/// <summary>当前布局里是否有任何一页被自定义过(「全部恢复默认」按钮可不可点)。</summary>
	public static bool AnyPageCustomized(LayoutV2? doc)
	{
		if (doc?.Layout == null)
		{
			return false;
		}
		if (doc.Layout.Pages != null && doc.Layout.Pages.Count > 0)
		{
			return true;
		}
		if (doc.Layout.Variants != null)
		{
			foreach (LayoutBody variant in doc.Layout.Variants.Values)
			{
				if (variant?.Pages != null && variant.Pages.Count > 0)
				{
					return true;
				}
			}
		}
		return false;
	}

	/// <summary>把一页恢复默认:从主布局与各变体里删掉这一页(页签、分栏、组件定义、方案都不动)。返回 "" = 成功。</summary>
	public static string ResetPage(string? pageId)
	{
		if (string.IsNullOrEmpty(pageId))
		{
			return "";
		}
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			if (!RemovePage(doc.Layout, pageId!))
			{
				return "";
			}
			return LayoutStoreV2.Save(doc) ? "" : I18n.L("布局未能保存");
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	/// <summary>
	/// 所有页面恢复默认:清空主布局与各变体的 pages(页签 / 间距 / 分栏 / 组件定义 / 方案都不动)。
	/// 清之前与「应用方案」同一套留底规则:当前布局没原样存在激活方案里时,先存成「上一次布局」,<paramref name="kept"/> = 留底名。
	/// </summary>
	public static string ResetAllPages(out string kept)
	{
		kept = "";
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			if (!AnyPageCustomized(doc))
			{
				return "";
			}
			kept = KeepPrevious(doc);
			doc.Layout.Pages.Clear();
			if (doc.Layout.Variants != null)
			{
				foreach (LayoutBody variant in doc.Layout.Variants.Values)
				{
					variant?.Pages?.Clear();
				}
			}
			return LayoutStoreV2.Save(doc) ? "" : I18n.L("布局未能保存");
		}
		catch (Exception ex)
		{
			kept = "";
			return ex.Message;
		}
	}

	private static bool RemovePage(LayoutBody body, string pageId)
	{
		bool removed = body.Pages != null && body.Pages.Remove(pageId);
		if (body.Variants != null)
		{
			foreach (LayoutBody variant in body.Variants.Values)
			{
				if (variant?.Pages != null && variant.Pages.Remove(pageId))
				{
					removed = true;
				}
			}
		}
		return removed;
	}

	/// <summary>
	/// 把 v1 页面布局(冻结格式,只有 {id,hidden} 与顺序)并进当前文档的 pages:按 pageId 并顺序/显隐,
	/// v2 已有的页级参数与块几何原样保留;其余页不动;不落盘(调用方决定)。返回并入的页数。
	/// </summary>
	public static int MergeV1Pages(LayoutV2? doc, IReadOnlyDictionary<string, PageLayoutDoc>? pages)
	{
		if (doc?.Layout == null || pages == null)
		{
			return 0;
		}
		int num = 0;
		foreach (KeyValuePair<string, PageLayoutDoc> kv in pages)
		{
			if (string.IsNullOrEmpty(kv.Key) || kv.Value == null)
			{
				continue;
			}
			// 单页 v1 文档 → Core 的朴素块列表 → MergeV1Order:只并顺序与显隐,
		// v2 里已有的页级参数(mode/cols/gap)与每块几何/外观按 id 原样保留
		// (不能像以前那样用 FromV1 新建整页 —— 那会把网格页整页降级回 stack、几何全丢)。
		doc.Layout.Pages.TryGetValue(kv.Key, out PageLayoutV2? existing);
		PageLayoutV2 merged = LayoutStoreV2.MergeV1Order(existing, LayoutV1Projection.ToCoreBlocks(kv.Value));
		doc.Layout.Pages[kv.Key] = merged;
		num++;
		}
		return num;
	}
}

/// <summary>布局 JSON 编辑器的一条问题(具体字段路径 + 中文原因);Error = 阻止落盘的硬错误。</summary>
public sealed class LayoutJsonIssue
{
	/// <summary>是否硬错误(错误 = 不应用;警告 = 已按契约钳位 / 忽略后照常应用)。</summary>
	public bool Error { get; set; }

	/// <summary>字段路径(如 <c>layout.splits["library"]["folders"]</c>);空串表示整份文档的问题。</summary>
	public string Path { get; set; } = "";

	/// <summary>中文原因。</summary>
	public string Message { get; set; } = "";
}

/// <summary>
/// 布局 JSON 编辑器(设计 §4.G)的校验与应用:纯逻辑、零 WPF 依赖。
/// 校验按 §3.2 的契约逐字段来:version/kind 门禁不通过 = 硬错误;白名单外的取值 / 越界数值 =
/// 警告(会由 LayoutSchema.Sanitize 钳位或忽略),错误时**不落盘**。
/// </summary>
public static class LayoutJsonEditor
{
	/// <summary>编辑器文本上限(防御:超大粘贴直接把界面拖死)。</summary>
	public const int MaxTextLength = 524288;

	private static readonly string[] KnownTopKeys = new string[7] { "version", "kind", "saved_at", "app", "active_profile", "layout", "profiles" };

	/// <summary>当前文档的 JSON 文本(编辑器的初始内容)。</summary>
	public static string Text(LayoutV2? doc)
	{
		try
		{
			LayoutV2 body = ((doc != null) ? doc : LayoutStoreV2.Load());
			return body.ToJson();
		}
		catch
		{
			return "{}";
		}
	}

	/// <summary>只校验(不落盘)。返回的问题里只要有一条 Error 就不该应用。</summary>
	public static List<LayoutJsonIssue> Inspect(string? json, out LayoutV2? doc)
	{
		doc = null;
		List<LayoutJsonIssue> issues = new List<LayoutJsonIssue>();
		string text = json ?? "";
		if (text.Trim().Length == 0)
		{
			issues.Add(Error("", I18n.L("内容是空的")));
			return issues;
		}
		if (text.Length > MaxTextLength)
		{
			issues.Add(Error("", I18n.L("内容太长(超过 {0} KB)", MaxTextLength / 1024)));
			return issues;
		}
		JsonDocument parsed;
		try
		{
			parsed = JsonDocument.Parse(text);
		}
		catch (JsonException ex)
		{
			issues.Add(Error("", I18n.L("不是合法的 JSON:{0}", ex.Message)));
			return issues;
		}
		catch (Exception ex2)
		{
			issues.Add(Error("", I18n.L("不是合法的 JSON:{0}", ex2.Message)));
			return issues;
		}
		try
		{
			JsonElement root = parsed.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				issues.Add(Error("", I18n.L("最外层必须是 JSON 对象")));
				return issues;
			}
			string kind = Text(root, "kind");
			if (kind.Length == 0)
			{
				issues.Add(Error("kind", I18n.L("缺少必填字段 kind(应为 {0})", LayoutSchema.Kind)));
			}
			else if (kind != LayoutSchema.Kind)
			{
				issues.Add(Error("kind", I18n.L("kind 必须是 {0}(当前 {1})", LayoutSchema.Kind, kind)));
			}
			if (!root.TryGetProperty("version", out JsonElement version))
			{
				issues.Add(Error("version", I18n.L("缺少必填字段 version(应为 {0})", LayoutSchema.CurrentVersion)));
			}
			else if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int versionValue) || versionValue != LayoutSchema.CurrentVersion)
			{
				issues.Add(Error("version", I18n.L("version 必须是 {0}(当前 {1})", LayoutSchema.CurrentVersion, version.ToString())));
			}
			if (issues.Any((LayoutJsonIssue i) => i.Error))
			{
				return issues;
			}
			foreach (JsonProperty prop in root.EnumerateObject())
			{
				if (!KnownTopKeys.Contains(prop.Name))
				{
					issues.Add(Warn(prop.Name, I18n.L("未知字段:保留但不生效")));
				}
			}
			if (LayoutJson.TryObject(root, "layout", out JsonElement layout))
			{
				InspectBody(layout, "layout", issues);
			}
			else
			{
				issues.Add(Warn("layout", I18n.L("缺少 layout:应用后会得到一份空布局")));
			}
			if (LayoutJson.TryObject(root, "profiles", out JsonElement profiles))
			{
				foreach (JsonProperty item in profiles.EnumerateObject())
				{
					if (item.Value.ValueKind == JsonValueKind.Object)
					{
						InspectBody(item.Value, "profiles[\"" + item.Name + "\"]", issues);
					}
				}
			}
			else if (root.TryGetProperty("profiles", out JsonElement rawProfiles) && rawProfiles.ValueKind != JsonValueKind.Object)
			{
				issues.Add(Warn("profiles", I18n.L("必须是对象:已按空处理")));
			}
			doc = LayoutV2.FromJsonText(text);
			if (doc == null)
			{
				issues.Add(Error("", I18n.L("布局文件无效")));
			}
		}
		finally
		{
			parsed.Dispose();
		}
		return issues;
	}

	/// <summary>校验 + 落盘:没有硬错误时 LayoutStoreV2.Save;返回问题列表(Error 存在即未落盘)。</summary>
	public static List<LayoutJsonIssue> Apply(string? json, out LayoutV2? doc)
	{
		List<LayoutJsonIssue> issues = Inspect(json, out doc);
		if (issues.Any((LayoutJsonIssue i) => i.Error))
		{
			return issues;
		}
		try
		{
			if (!LayoutStoreV2.Save(((doc != null) ? doc : LayoutStoreV2.Load())))
			{
				issues.Add(Error("", I18n.L("布局未能保存")));
			}
		}
		catch (Exception ex)
		{
			issues.Add(Error("", ex.Message));
		}
		return issues;
	}

	// ————— LAYOUT 对象逐字段检查 —————

	private static void InspectBody(JsonElement body, string path, List<LayoutJsonIssue> issues)
	{
		if (LayoutJson.TryObject(body, "shell", out JsonElement shell))
		{
			InspectShell(shell, path + ".shell", issues);
		}
		if (LayoutJson.TryObject(body, "pages", out JsonElement pages))
		{
			foreach (JsonProperty page in pages.EnumerateObject())
			{
				if (page.Value.ValueKind != JsonValueKind.Object)
				{
					issues.Add(Warn(path + ".pages[\"" + page.Name + "\"]", I18n.L("必须是对象:该页已忽略")));
					continue;
				}
				InspectPage(page.Value, path + ".pages[\"" + page.Name + "\"]", issues);
			}
		}
		if (LayoutJson.TryObject(body, "splits", out JsonElement splits))
		{
			foreach (JsonProperty page2 in splits.EnumerateObject())
			{
				if (page2.Value.ValueKind != JsonValueKind.Object)
				{
					issues.Add(Warn(path + ".splits[\"" + page2.Name + "\"]", I18n.L("必须是对象:该页分栏已忽略")));
					continue;
				}
				foreach (JsonProperty split in page2.Value.EnumerateObject())
				{
					string where = path + ".splits[\"" + page2.Name + "\"][\"" + split.Name + "\"]";
					if (split.Value.ValueKind != JsonValueKind.Number)
					{
						issues.Add(Warn(where, I18n.L("比例必须是数字:该处已忽略")));
						continue;
					}
					double ratio = split.Value.GetDouble();
					if (ratio < LayoutSchema.RatioMin || ratio > LayoutSchema.RatioMax)
					{
						issues.Add(Warn(where, I18n.L("比例夹到 {0}..{1}(当前 {2})", LayoutSchema.RatioMin, LayoutSchema.RatioMax, ratio)));
					}
				}
			}
		}
		if (LayoutJson.TryArray(body, "widgets", out JsonElement widgets))
		{
			int index = 0;
			foreach (JsonElement widget in widgets.EnumerateArray())
			{
				string where2 = path + ".widgets[" + index + "]";
				index++;
				if (widget.ValueKind != JsonValueKind.Object)
				{
					issues.Add(Warn(where2, I18n.L("必须是对象:该自建区块已忽略")));
					continue;
				}
				string type = Text(widget, "type");
				if (type.Length > 0 && !LayoutSchema.IsKnownWidgetType(type))
				{
					issues.Add(Warn(where2 + ".type", I18n.L("未知类型 {0}:块会保留为「不可用」", type)));
				}
			}
		}
	}

	private static void InspectShell(JsonElement shell, string path, List<LayoutJsonIssue> issues)
	{
		CheckChoice(shell, "density", new string[3] { LayoutSchema.DensityCompact, LayoutSchema.DensityStandard, LayoutSchema.DensityLoose }, path, issues);
		CheckChoice(shell, "nav_mode", new string[2] { LayoutSchema.NavModeTabs, LayoutSchema.NavModeSidebar }, path, issues);
		CheckChoice(shell, "nav_side", new string[2] { LayoutSchema.NavSideLeft, LayoutSchema.NavSideRight }, path, issues);
		CheckChoice(shell, "nav_style", new string[2] { LayoutSchema.NavStyleCompact, LayoutSchema.NavStyleGrouped }, path, issues);
		if (shell.TryGetProperty("nav_width", out JsonElement navWidth) && navWidth.ValueKind == JsonValueKind.Number)
		{
			double width = navWidth.GetDouble();
			if (width < LayoutSchema.NavWidthMin || width > LayoutSchema.NavWidthMax)
			{
				issues.Add(Warn(path + ".nav_width", I18n.L("侧栏宽夹到 {0}..{1}(当前 {2})", LayoutSchema.NavWidthMin, LayoutSchema.NavWidthMax, width)));
			}
		}
		if (shell.TryGetProperty("tabs", out JsonElement tabs))
		{
			if (tabs.ValueKind != JsonValueKind.Array)
			{
				issues.Add(Warn(path + ".tabs", I18n.L("必须是数组:已回落到默认页签")));
			}
			else
			{
				foreach (JsonElement tab in tabs.EnumerateArray())
				{
					string key = ((tab.ValueKind == JsonValueKind.String) ? (tab.GetString() ?? "") : "");
					if (!LayoutSchema.IsKnownTab(key))
					{
						issues.Add(Warn(path + ".tabs", I18n.L("未知页签 {0}:已丢弃", key)));
					}
				}
			}
		}
	}

	private static void InspectPage(JsonElement page, string path, List<LayoutJsonIssue> issues)
	{
		if (page.TryGetProperty("version", out JsonElement version))
		{
			if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int num) || num != LayoutSchema.CurrentVersion)
			{
				issues.Add(Warn(path + ".version", I18n.L("单页 version 必须是 {0}:该页已按默认处理", LayoutSchema.CurrentVersion)));
			}
		}
		if (page.TryGetProperty("mode", out JsonElement mode) && mode.ValueKind == JsonValueKind.String)
		{
			string text = mode.GetString() ?? "";
			if (!LayoutSchema.IsKnownMode(text))
			{
				issues.Add(Warn(path + ".mode", I18n.L("未知模式 {0}:已按 stack 处理", text)));
			}
		}
		if (page.TryGetProperty("cols", out JsonElement cols) && cols.ValueKind == JsonValueKind.Number && cols.TryGetInt32(out int colValue))
		{
			if (LayoutSchema.SnapCols(colValue) != colValue)
			{
				issues.Add(Warn(path + ".cols", I18n.L("列数只认 2/3/4/6/12:已吸附到 {0}", LayoutSchema.SnapCols(colValue))));
			}
		}
		CheckRange(page, "gap", LayoutSchema.GapMin, LayoutSchema.GapMax, path, issues);
		CheckRange(page, "row_h", LayoutSchema.RowHMin, LayoutSchema.RowHMax, path, issues);
		if (LayoutJson.TryArray(page, "blocks", out JsonElement blocks))
		{
			int index = 0;
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (JsonElement block in blocks.EnumerateArray())
			{
				string where = path + ".blocks[" + index + "]";
				index++;
				if (block.ValueKind != JsonValueKind.Object)
				{
					issues.Add(Warn(where, I18n.L("必须是对象:该块已忽略")));
					continue;
				}
				string id = Text(block, "id");
				if (id.Length == 0)
				{
					issues.Add(Warn(where + ".id", I18n.L("缺少块 id:该块已忽略")));
				}
				else if (!seen.Add(id))
				{
					issues.Add(Warn(where + ".id", I18n.L("块 id 重复({0}):重复的那块已忽略", id)));
				}
				if (block.TryGetProperty("style", out JsonElement style) && style.ValueKind == JsonValueKind.String)
				{
					string styleText = style.GetString() ?? "";
					if (!LayoutSchema.IsKnownStyle(styleText))
					{
						issues.Add(Warn(where + ".style", I18n.L("未知外观 {0}:已按 plain 处理", styleText)));
					}
				}
				if (block.TryGetProperty("hidden", out JsonElement hidden) && hidden.ValueKind != JsonValueKind.True && hidden.ValueKind != JsonValueKind.False)
				{
					issues.Add(Warn(where + ".hidden", I18n.L("只认 true / false:已按 false 处理")));
				}
				CheckRange(block, "min_h", LayoutSchema.MinHMin, LayoutSchema.MinHMax, where, issues);
			}
		}
	}

	private static void CheckChoice(JsonElement obj, string name, string[] allowed, string path, List<LayoutJsonIssue> issues)
	{
		if (!obj.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
		{
			return;
		}
		string text = value.GetString() ?? "";
		if (!allowed.Contains(text))
		{
			issues.Add(Warn(path + "." + name, I18n.L("取值不合法({0}):已回落到 {1}", text, allowed[0])));
		}
	}

	private static void CheckRange(JsonElement obj, string name, double min, double max, string path, List<LayoutJsonIssue> issues)
	{
		if (!obj.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
		{
			return;
		}
		double num = value.GetDouble();
		if (num < min || num > max)
		{
			issues.Add(Warn(path + "." + name, I18n.L("取值超出 {0}..{1}:已夹取(当前 {2})", min, max, num)));
		}
	}

	private static string Text(JsonElement obj, string name)
	{
		if (obj.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
		{
			return value.GetString() ?? "";
		}
		return "";
	}

	private static LayoutJsonIssue Error(string path, string message)
	{
		return new LayoutJsonIssue
		{
			Error = true,
			Path = path,
			Message = message
		};
	}

	private static LayoutJsonIssue Warn(string path, string message)
	{
		return new LayoutJsonIssue
		{
			Error = false,
			Path = path,
			Message = message
		};
	}
}
