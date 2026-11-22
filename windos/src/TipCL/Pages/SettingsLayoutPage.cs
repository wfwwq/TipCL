using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TipCL.Pages.Layout;
using TipCL.Services;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Pages;

// 设置 → 布局(R34 布局 v3,对应 layout3 第二张):按「要做的事」分五组 ——
//   外观(区块间距三段)/ 导航(顶部页签可拖动排序 + 眼睛开关、标题栏三个勾选项)/
//   页面(15 张缩略图卡片:默认 / 已自定义 + 编辑 / 恢复默认)/ 方案(卡片:应用 / ⋯)/
//   高级(默认折叠:分栏宽度一行、网页和按钮组件的全局开关、组件管理、布局文件)。
// 三条硬规矩不变:
//   1) 落盘唯一入口 = LayoutStoreV2(load → 改 → Save);本文件绝不自己写布局文件(导出到用户选定的路径除外);
//   2) 方案应用 = **原子替换** layout(整个 LayoutBody 换掉)再 Save,v1 由 v2 单向写通(不往 v1 塞新字段);
//   3) 本视图自带 18px 左留白(PageBase.ScrollBody 的 SPanel Margin = 18,14,18,16)——设置页左导航是分栏,
//      _view.Margin = -18 归位、分隔条正好占住视图左边那 18px,新视图不留白就会被分隔条盖住。
// 区块 id 沿用 R33 的七个(appearance / shell / blocks / splits / widgets / profiles / json):用户已存过的
// 本页布局照旧对得上;「高级」一组拆在 splits(组标题 + 折叠头 + 分栏宽度)/ widgets / json 三块里,
// 折叠时后两块内容收起。界面文案一律不出现文件名、包编号、#编号、块 id 这类内部说法
// (SettingsLayoutV3Tests 扫描本文件全部 I18n.L 字面量把关)。
//
// 纯逻辑(LayoutPageCards / LayoutSplitSummary / LayoutProfiles / LayoutJsonEditor)刻意零 WPF 依赖:
// net8 测试宿主能直接断言页面卡片、已自定义判定、方案增删改查与 JSON 编辑器的逐字段错误。
public sealed partial class SettingsPage
{
	/// <summary>「布局」视图在视图表/左导航里的下标(第 8 个)。</summary>
	internal const int LayoutView = 7;

	/// <summary>「布局」视图的区块宿主 pageId(区块:appearance / shell / blocks / splits / widgets / profiles / json)。</summary>
	internal const string LayoutViewPageId = "settings:layout";

	/// <summary>跳到「设置 → 布局」视图(全局右键菜单的「布局设置...」、编辑条「⋯ → 整套布局与方案…」用)。</summary>
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

	/// <summary>展开 / 收起「高级」组(截图工具的 --expand advanced 用;默认折叠)。</summary>
	public void ExpandLayoutAdvanced(bool open = true)
	{
		if (_layoutAdvancedOpen == open)
		{
			return;
		}
		_layoutAdvancedOpen = open;
		RefreshLayoutView();
	}

	private bool _layoutRebuilding;

	// 以下开合状态跨视图重建保留(每次保存 / 刷新都会重建整个布局视图)
	private bool _layoutAdvancedOpen;

	private bool _layoutAllPages;

	private bool _layoutWidgetPanelOpen;

	private bool _layoutWidgetCreateOpen;

	// 顶部页签的标签行:拖动 / 方向键 / 眼睛开关只重画这一行(不重建整个视图,焦点留在标签上)
	private SPanel? _layoutTabChips;

	private Button? _layoutTabReset;

	private TabChipDrag? _tabChipDrag;

	// ————— 视图装配 —————

	/// <summary>构「布局」视图(每次切到该视图都重建,保证读到最新状态)。</summary>
	private UIElement BuildLayoutView()
	{
		_layoutRebuilding = true;
		try
		{
			// 每组(组标题 + 卡片)一个区块,页标题与说明跟着第一块走;宿主默认边距 18,14,18,16 + 块间距 10。
			SPanel first = Ui.V(10.0, PTitle(I18n.L("布局")), Ui.Muted(I18n.L("调整导航、各页面的排列，或者保存成方案。改动即时生效，随时可以恢复默认。")).Wrap().M(0.0, 0.0, 0.0, 4.0), LayoutGroup(I18n.L("外观"), Ui.Card(Rows(LayoutAppearanceRows()))));
			return new BlockHost(LayoutViewPageId, new List<BlockSpec>
			{
				new BlockSpec { Id = "appearance", Title = I18n.L("外观"), Content = first },
				new BlockSpec { Id = "shell", Title = I18n.L("导航"), Content = LayoutGroup(I18n.L("导航"), LayoutNavCard()) },
				new BlockSpec { Id = "blocks", Title = I18n.L("页面"), Content = LayoutPagesGroup() },
				new BlockSpec { Id = "profiles", Title = I18n.L("方案"), Content = LayoutProfilesGroup() },
				new BlockSpec { Id = "splits", Title = I18n.L("高级"), Content = LayoutAdvancedHead() },
				new BlockSpec { Id = "widgets", Title = I18n.L("网页、按钮与组件"), Content = LayoutAdvancedWidgets() },
				new BlockSpec { Id = "json", Title = I18n.L("布局文件"), Content = LayoutAdvancedFile() }
			}).Root;
		}
		finally
		{
			_layoutRebuilding = false;
		}
	}

	/// <summary>重建「布局」视图;当前正显示它时同步换掉宿主内容。</summary>
	private void RefreshLayoutView()
	{
		object? current = _view?.Content;
		UIElement? old = _views[LayoutView];
		UIElement fresh = BuildLayoutView();
		_views[LayoutView] = fresh;
		if (_view != null && ReferenceEquals(current, old))
		{
			_view.Content = fresh;
		}
	}

	// ————— 共用小件 —————

	// 组标题(卡片外的灰色小字)+ 右侧的小按钮
	private static FrameworkElement LayoutGroupHead(string title, params UIElement[] right)
	{
		Grid head = Ui.G(null, "*,Auto");
		TextBlock text = Gt(title);
		text.VerticalAlignment = VerticalAlignment.Bottom;
		head.Add(text, 0);
		if (right != null && right.Length > 0)
		{
			SPanel buttons = Ui.H(6.0, right);
			buttons.Margin = new Thickness(0.0, 0.0, 0.0, 4.0);
			buttons.VerticalAlignment = VerticalAlignment.Bottom;
			head.Add(buttons, 0, 1);
		}
		return head;
	}

	// 一组 = 组标题 + 内容(卡片 / 卡片网格),与设置页其它小节同一套间距
	private static UIElement LayoutGroup(string title, UIElement body, params UIElement[] right)
	{
		return Ui.V(10.0, LayoutGroupHead(title, right), body);
	}

	// 一行设置:左边粗体标题 + 一句灰色说明,右边控件
	private static Grid LayoutRow(string title, string? desc, UIElement? right)
	{
		Grid row = Ui.G(null, "*,Auto");
		TextBlock? sub = string.IsNullOrEmpty(desc) ? null : Ui.Small(desc!).Wrap();
		SPanel text = Ui.V(2.0, Ui.Txt(title, 13.0, bold: true), sub);
		text.VerticalAlignment = VerticalAlignment.Center;
		text.Margin = new Thickness(0.0, 0.0, 16.0, 0.0);
		row.Add(text, 0);
		if (right != null)
		{
			right.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
			row.Add(right, 0, 1);
		}
		row.Margin = new Thickness(0.0, 4.0, 0.0, 4.0);
		return row;
	}

	// 组标题旁的小按钮(高 26,比卡片里的按钮矮一档)
	private static Button SmallBtn(string text, BtnKind kind, RoutedEventHandler? click)
	{
		Button button = Ui.Btn(text, kind, click);
		button.Height = 26.0;
		button.Padding = new Thickness(10.0, 0.0, 10.0, 0.0);
		button.VerticalAlignment = VerticalAlignment.Center;
		return button;
	}

	// 「高级」折叠时 widgets / json 两块的内容:收起(宿主照常持有这两块,id 与已存布局对得上)
	private static UIElement CollapsedBlock()
	{
		return new Border
		{
			Visibility = Visibility.Collapsed
		};
	}

	// 资源键可能来自别的包(WP-L0 新增的令牌):有就动态引用,没有就用兜底键,绝不抛
	private static void SetBrushOr(FrameworkElement el, DependencyProperty property, string key, string fallback)
	{
		object? found = null;
		try
		{
			found = Application.Current?.TryFindResource(key);
		}
		catch
		{
			found = null;
		}
		el.SetResourceReference(property, (found != null) ? key : fallback);
	}

	// 矢量图标(16×16 视框、描边绘制):优先用样式里统一提供的几何,找不到用这里的兜底路径
	private static FrameworkElement LayoutIcon(string geometryKey, string fallbackData, double size, string brushKey, double stroke = 1.4)
	{
		Geometry? geometry = null;
		try
		{
			geometry = Application.Current?.TryFindResource(geometryKey) as Geometry;
		}
		catch
		{
			geometry = null;
		}
		if (geometry == null)
		{
			try
			{
				geometry = Geometry.Parse(fallbackData);
			}
			catch
			{
				geometry = Geometry.Empty;
			}
		}
		System.Windows.Shapes.Path path = new System.Windows.Shapes.Path
		{
			Data = geometry,
			StrokeThickness = stroke,
			StrokeStartLineCap = PenLineCap.Round,
			StrokeEndLineCap = PenLineCap.Round,
			StrokeLineJoin = PenLineJoin.Round
		};
		path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, brushKey);
		Canvas box = new Canvas
		{
			Width = 16.0,
			Height = 16.0
		};
		box.Children.Add(path);
		return new Viewbox
		{
			Width = size,
			Height = size,
			Child = box,
			VerticalAlignment = VerticalAlignment.Center,
			SnapsToDevicePixels = true
		};
	}

	private const string GeoEyeFallback = "M1.5,8 C3.2,4.6 5.4,3.2 8,3.2 C10.6,3.2 12.8,4.6 14.5,8 C12.8,11.4 10.6,12.8 8,12.8 C5.4,12.8 3.2,11.4 1.5,8 Z M8,5.8 A2.2,2.2 0 1 0 8,10.2 A2.2,2.2 0 1 0 8,5.8 Z";

	private const string GeoEyeOffFallback = "M1.5,8 C3.2,4.6 5.4,3.2 8,3.2 C10.6,3.2 12.8,4.6 14.5,8 C12.8,11.4 10.6,12.8 8,12.8 C5.4,12.8 3.2,11.4 1.5,8 Z M2.5,2.5 L13.5,13.5";

	private const string GeoLockFallback = "M4,7.5 L12,7.5 L12,13.5 L4,13.5 Z M5.5,7.5 L5.5,5.5 A2.5,2.5 0 0 1 10.5,5.5 L10.5,7.5";

	private const string GeoGripFallback = "M6,4 L6.01,4 M10,4 L10.01,4 M6,8 L6.01,8 M10,8 L10.01,8 M6,12 L6.01,12 M10,12 L10.01,12";

	// 虚线空状态框(没有方案时)
	private static FrameworkElement EmptyBox(string text)
	{
		Grid grid = new Grid
		{
			MinHeight = 56.0
		};
		System.Windows.Shapes.Rectangle frame = new System.Windows.Shapes.Rectangle
		{
			RadiusX = 12.0,
			RadiusY = 12.0,
			StrokeThickness = 1.0,
			StrokeDashArray = new DoubleCollection { 4.0, 3.0 },
			SnapsToDevicePixels = true
		};
		frame.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "B.Line");
		TextBlock label = Ui.Small(text).Wrap();
		label.TextAlignment = TextAlignment.Center;
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.VerticalAlignment = VerticalAlignment.Center;
		label.Margin = new Thickness(12.0, 10.0, 12.0, 10.0);
		grid.Children.Add(frame);
		grid.Children.Add(label);
		return grid;
	}

	// 琥珀色提示条(网页 / 按钮组件开关打开后出现)
	private static Border WarnBox(string text)
	{
		TextBlock label = Ui.Txt(text, 12.0).Wrap();
		Border box = new Border
		{
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0),
			Child = label
		};
		SetBrushOr(box, Border.BackgroundProperty, "B.WarnSoft", "B.Paper2");
		SetBrushOr(label, TextBlock.ForegroundProperty, "B.WarnInk", "B.Warn");
		return box;
	}

	// ————— 1 外观 —————

	private FrameworkElement[] LayoutAppearanceRows()
	{
		ShellLayout shell = ShellTabs.CurrentShell();
		string current = LayoutPageCards.NormalizeDensity(shell.Density);
		string[] keys = LayoutPageCards.DensityKeys;
		SPanel rail = Ui.H(0.0);
		ToggleButton[] items = new ToggleButton[keys.Length];
		for (int i = 0; i < keys.Length; i++)
		{
			int k = i;
			ToggleButton item = new ToggleButton
			{
				Style = Ui.S("Seg.Item"),
				Content = LayoutPageCards.DensityLabel(keys[i]),
				IsChecked = string.Equals(keys[i], current, StringComparison.Ordinal)
			};
			item.Click += delegate
			{
				// 分段只认「选中哪一档」:点已选中的那档不会把它点成未选中
				for (int j = 0; j < items.Length; j++)
				{
					items[j].IsChecked = j == k;
				}
				if (_layoutRebuilding || string.Equals(keys[k], current, StringComparison.Ordinal))
				{
					return;
				}
				current = keys[k];
				string key = keys[k];
				WriteLayout(delegate(LayoutV2 doc)
				{
					doc.Layout.Shell.Density = key;
				}, I18n.L("区块间距:{0}", LayoutPageCards.DensityLabel(key)));
			};
			items[i] = item;
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

	// ————— 2 导航:顶部页签(拖动排序 + 眼睛开关,「设置」锁定)+ 标题栏三个勾选项 —————

	private Border LayoutNavCard()
	{
		_layoutTabReset = SmallBtn(I18n.L("恢复默认"), BtnKind.Ghost, delegate
		{
			ShellTabs.ResetTabs();
			RenderTabChips(null);
			PageBase.Toast(I18n.L("页签已恢复默认"), "", ToastKind.Success);
		});
		_layoutTabChips = Ui.H(6.0);
		_layoutTabChips.Margin = new Thickness(0.0, 0.0, 0.0, 4.0);
		RenderTabChips(null);
		FrameworkElement tabs = Ui.V(8.0, LayoutRow(I18n.L("顶部页签"), I18n.L("拖动调整顺序，点眼睛隐藏；「设置」不能隐藏"), _layoutTabReset), _layoutTabChips);
		ShellLayout shell = ShellTabs.CurrentShell();
		// 桥接按钮不再提供开关(用户既定:C 桥只在断开时显示);已存的值与 ShellTabs.SetTitlebar(bridge:) 都保留
		SPanel options = Ui.H(8.0, TitlebarOption(I18n.L("AI 按钮"), ShellTabs.ShowAi(shell), delegate(bool on)
		{
			ShellTabs.SetTitlebar(ai: on);
		}), TitlebarOption(I18n.L("主题切换"), ShellTabs.ShowTheme(shell), delegate(bool on)
		{
			ShellTabs.SetTitlebar(theme: on);
		}), TitlebarOption(I18n.L("账号"), ShellTabs.ShowAccount(shell), delegate(bool on)
		{
			ShellTabs.SetTitlebar(account: on);
		}));
		return Ui.Card(Rows(tabs, LayoutRow(I18n.L("标题栏"), I18n.L("隐藏 AI 按钮后仍可按 Ctrl+K 打开"), options)));
	}

	private FrameworkElement TitlebarOption(string label, bool value, Action<bool> write)
	{
		CheckBox box = Ui.Check(label, value);
		box.Checked += delegate
		{
			if (!_layoutRebuilding)
			{
				write(obj: true);
			}
		};
		box.Unchecked += delegate
		{
			if (!_layoutRebuilding)
			{
				write(obj: false);
			}
		};
		Border chip = new Border
		{
			CornerRadius = new CornerRadius(8.0),
			BorderThickness = new Thickness(1.0),
			Padding = new Thickness(8.0, 5.0, 10.0, 5.0),
			Child = box
		};
		chip.SetResourceReference(Border.BorderBrushProperty, "B.Line");
		return chip;
	}

	// 重画标签行(顺序 = ShellTabs.ChipOrder:正在显示的在前,隐藏的排在后面);focusKey 非空时把焦点还给那个标签
	private void RenderTabChips(string? focusKey)
	{
		SPanel? host = _layoutTabChips;
		if (host == null)
		{
			return;
		}
		_tabChipDrag = null;
		host.Children.Clear();
		ShellLayout shell = ShellTabs.CurrentShell();
		ContentControl? focus = null;
		foreach (string key in ShellTabs.ChipOrder(shell))
		{
			ContentControl chip = TabChip(key, ShellTabs.IsVisible(shell, key));
			host.Children.Add(chip);
			if (focusKey != null && string.Equals(key, focusKey, StringComparison.Ordinal))
			{
				focus = chip;
			}
		}
		if (_layoutTabReset != null)
		{
			_layoutTabReset.IsEnabled = !ShellTabs.IsDefaultTabs(shell);
		}
		if (focus != null)
		{
			ContentControl target = focus;
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				target.Focus();
				Keyboard.Focus(target);
			}, System.Windows.Threading.DispatcherPriority.Input);
		}
	}

	private ContentControl TabChip(string key, bool visible)
	{
		bool locked = !ShellTabs.CanHide(key);
		string label = ShellTabs.Label(key);
		Border face = new Border
		{
			CornerRadius = new CornerRadius(8.0),
			BorderThickness = new Thickness(1.0),
			Padding = new Thickness(8.0, 0.0, 4.0, 0.0),
			Height = 32.0
		};
		face.SetResourceReference(Border.BackgroundProperty, "B.Paper2");
		face.SetResourceReference(Border.BorderBrushProperty, "B.Line");
		TextBlock name = Ui.Txt(label, 13.0);
		name.VerticalAlignment = VerticalAlignment.Center;
		if (!visible)
		{
			name.TextDecorations = System.Windows.TextDecorations.Strikethrough;
		}
		string geometry = (visible && locked) ? "Geo.Lock" : (visible ? "Geo.Eye" : "Geo.EyeOff");
		string fallback = (visible && locked) ? GeoLockFallback : (visible ? GeoEyeFallback : GeoEyeOffFallback);
		Button eye = new Button
		{
			Style = Ui.S("Btn.Icon"),
			Width = 24.0,
			Height = 24.0,
			Padding = new Thickness(0.0),
			Content = LayoutIcon(geometry, fallback, 14.0, "B.InkSoft")
		};
		string tip = (visible && locked) ? I18n.L("「{0}」不能隐藏", label) : (visible ? I18n.L("隐藏「{0}」", label) : I18n.L("显示「{0}」", label));
		eye.ToolTip = tip;
		AutomationProperties.SetName(eye, tip);
		ToolTipService.SetShowOnDisabled(eye, true);
		eye.IsEnabled = !(visible && locked);
		eye.Click += delegate
		{
			ToggleTab(key, !visible);
		};
		face.Child = Ui.H(6.0, LayoutIcon("Geo.Grip", GeoGripFallback, 12.0, "B.InkFaint", 1.8), name, eye);
		ContentControl chip = new ContentControl
		{
			Content = face,
			Tag = key,
			Focusable = true,
			IsTabStop = true,
			FocusVisualStyle = null,
			Opacity = (visible ? 1.0 : 0.55)
		};
		AutomationProperties.SetName(chip, I18n.L("页签「{0}」", label));
		chip.GotKeyboardFocus += delegate
		{
			face.SetResourceReference(Border.BorderBrushProperty, "B.Accent");
		};
		chip.LostKeyboardFocus += delegate
		{
			if (!chip.IsKeyboardFocusWithin)
			{
				face.SetResourceReference(Border.BorderBrushProperty, "B.Line");
			}
		};
		if (!visible)
		{
			// 隐藏项的位置不进文档(shell.tabs 只存正在显示的页签),所以不参与排序
			chip.ToolTip = I18n.L("先点眼睛显示，再拖动调整位置");
			return chip;
		}
		chip.ToolTip = I18n.L("拖动调整顺序；选中后也可以按左右方向键移动");
		chip.Cursor = Cursors.SizeWE;
		chip.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
		{
			if (IsInsideButton(e.OriginalSource as DependencyObject, chip))
			{
				return;
			}
			BeginTabChipDrag(chip, key, e);
		};
		chip.MouseMove += delegate(object sender, MouseEventArgs e)
		{
			DragTabChip(e);
		};
		chip.MouseLeftButtonUp += delegate
		{
			EndTabChipDrag();
		};
		chip.LostMouseCapture += delegate
		{
			EndTabChipDrag();
		};
		chip.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
		{
			if (!ReferenceEquals(e.OriginalSource, chip))
			{
				return;
			}
			int delta = (e.Key == Key.Left) ? (-1) : ((e.Key == Key.Right) ? 1 : 0);
			if (delta == 0)
			{
				return;
			}
			e.Handled = true;
			MoveTabChip(key, delta);
		};
		return chip;
	}

	private static bool IsInsideButton(DependencyObject? source, DependencyObject stop)
	{
		DependencyObject? node = source;
		int guard = 0;
		while (node != null && !ReferenceEquals(node, stop) && guard++ < 32)
		{
			if (node is ButtonBase)
			{
				return true;
			}
			node = (node is Visual || node is System.Windows.Media.Media3D.Visual3D) ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
		}
		return false;
	}

	private void ToggleTab(string key, bool show)
	{
		if (!show && !ShellTabs.CanHide(key))
		{
			return;
		}
		string label = ShellTabs.Label(key);
		ShellTabs.SetTabVisible(key, show);
		RenderTabChips(key);
		PageBase.Toast(show ? I18n.L("已显示「{0}」页签", label) : I18n.L("已隐藏「{0}」页签", label), "", ToastKind.Success);
	}

	// 方向键:在正在显示的页签里左右挪一格,马上落盘(标题栏随 LayoutChanged 当场重建)
	private void MoveTabChip(string key, int delta)
	{
		List<string> order = ShellTabs.OrderedKeys(ShellTabs.CurrentShell());
		List<string> next = ShellTabs.MoveTab(order, key, delta);
		if (next.SequenceEqual(order))
		{
			return;
		}
		ShellTabs.SetTabs(next);
		RenderTabChips(key);
	}

	/// <summary>一次标签拖动的现场:按下时记下各标签的原位,拖动中只用位移预览,松手才落盘。</summary>
	private sealed class TabChipDrag
	{
		public string TabKey = "";

		public ContentControl? Chip;

		public double Grab;

		public double StartX;

		public bool Moved;

		public int Target;

		public List<string> Order = new List<string>();

		public Dictionary<string, Rect> Slots = new Dictionary<string, Rect>(StringComparer.Ordinal);

		public Dictionary<string, FrameworkElement> Chips = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);
	}

	private void BeginTabChipDrag(ContentControl chip, string key, MouseButtonEventArgs e)
	{
		SPanel? host = _layoutTabChips;
		if (host == null)
		{
			return;
		}
		ShellLayout shell = ShellTabs.CurrentShell();
		TabChipDrag drag = new TabChipDrag
		{
			TabKey = key,
			Chip = chip,
			Order = ShellTabs.OrderedKeys(shell)
		};
		foreach (UIElement child in host.Children)
		{
			if (child is FrameworkElement fe && fe.Tag is string tab && drag.Order.Contains(tab))
			{
				drag.Slots[tab] = LayoutInformation.GetLayoutSlot(fe);
				drag.Chips[tab] = fe;
			}
		}
		if (!drag.Slots.ContainsKey(key))
		{
			return;
		}
		Point p = e.GetPosition(host);
		drag.StartX = p.X;
		drag.Grab = p.X - drag.Slots[key].X;
		drag.Target = drag.Order.IndexOf(key);
		_tabChipDrag = drag;
		chip.Focus();
		chip.CaptureMouse();
		e.Handled = true;
	}

	private void DragTabChip(MouseEventArgs e)
	{
		TabChipDrag? drag = _tabChipDrag;
		SPanel? host = _layoutTabChips;
		if (drag == null || host == null || drag.Chip == null)
		{
			return;
		}
		double x = e.GetPosition(host).X;
		if (!drag.Moved)
		{
			if (Math.Abs(x - drag.StartX) < 4.0)
			{
				return;
			}
			drag.Moved = true;
			Panel.SetZIndex(drag.Chip, 10);
		}
		List<double> mids = new List<double>();
		foreach (string tab in drag.Order)
		{
			if (!string.Equals(tab, drag.TabKey, StringComparison.Ordinal) && drag.Slots.TryGetValue(tab, out Rect r))
			{
				mids.Add(r.X + r.Width / 2.0);
			}
		}
		drag.Target = ShellTabs.DropIndex(mids, x);
		// 预览:其它标签按「挪完之后」的顺序让位,被拖的那个跟手
		List<string> preview = ShellTabs.MoveTabTo(drag.Order, drag.TabKey, drag.Target);
		double cursor = drag.Slots[drag.Order[0]].X;
		foreach (string tab in preview)
		{
			if (!drag.Slots.TryGetValue(tab, out Rect slot))
			{
				continue;
			}
			if (!string.Equals(tab, drag.TabKey, StringComparison.Ordinal) && drag.Chips.TryGetValue(tab, out FrameworkElement? other))
			{
				other.RenderTransform = new TranslateTransform(cursor - slot.X, 0.0);
			}
			cursor += slot.Width + host.Spacing;
		}
		drag.Chip.RenderTransform = new TranslateTransform(x - drag.Grab - drag.Slots[drag.TabKey].X, 0.0);
	}

	private void EndTabChipDrag()
	{
		TabChipDrag? drag = _tabChipDrag;
		if (drag == null)
		{
			return;
		}
		_tabChipDrag = null;
		if (drag.Chip != null && drag.Chip.IsMouseCaptured)
		{
			drag.Chip.ReleaseMouseCapture();
		}
		if (!drag.Moved)
		{
			return;
		}
		List<string> next = ShellTabs.MoveTabTo(drag.Order, drag.TabKey, drag.Target);
		if (!next.SequenceEqual(drag.Order))
		{
			ShellTabs.SetTabs(next);
			PageBase.Toast(I18n.L("页签顺序已更新"), "", ToastKind.Success);
		}
		RenderTabChips(drag.TabKey);
	}

	// ————— 3 页面:缩略图卡片(编辑 = 跳到该页并进入编辑态,复用 LayoutEditor.EnterEdit) —————

	private UIElement LayoutPagesGroup()
	{
		LayoutV2 doc = LayoutStoreV2.Load();
		Button resetAll = SmallBtn(I18n.L("全部恢复默认"), BtnKind.Ghost, delegate
		{
			PageBase.Run(ResetAllPagesAsync);
		});
		resetAll.IsEnabled = LayoutPageCards.CustomizedCount(doc) > 0;
		UniformGrid grid = new UniformGrid
		{
			Columns = 3,
			Margin = new Thickness(-6.0)
		};
		foreach (LayoutBlockEntryCatalog.Entry entry in LayoutPageCards.Shown(_layoutAllPages))
		{
			grid.Children.Add(PageCard(doc, entry).M(6.0));
		}
		Grid foot = Ui.G(null, "*,Auto");
		foot.Add(Ui.Small(LayoutPageCards.SingleBlockNote()).Wrap().VCenter(), 0);
		Button more = SmallBtn(_layoutAllPages ? I18n.L("收起") : I18n.L("显示全部 {0} 个页面", LayoutPageCards.Cards().Count), BtnKind.Ghost, delegate
		{
			_layoutAllPages = !_layoutAllPages;
			RefreshLayoutView();
		});
		foot.Add(more, 0, 1);
		return Ui.V(10.0, LayoutGroupHead(I18n.L("页面"), resetAll), grid, foot);
	}

	private FrameworkElement PageCard(LayoutV2 doc, LayoutBlockEntryCatalog.Entry entry)
	{
		string pageId = entry.PageId;
		string navId = entry.NavId;
		int view = entry.SettingsView;
		string hint = entry.Hint;
		bool custom = LayoutPageCards.IsCustomized(doc, pageId);
		PageLayoutV2? page = LayoutPageCards.PageOf(doc, pageId) ?? LivePage(pageId);
		Grid name = Ui.G(null, "*,Auto");
		name.Add(Ui.Txt(I18n.L(entry.Label), 13.0, bold: true).Trim().VCenter(), 0);
		name.Add(custom ? Ui.Tag(I18n.L("已自定义"), "B.AccentText", "B.AccentSoft") : Ui.Tag(I18n.L("默认")), 0, 1);
		string text = custom ? LayoutPageCards.Summary(page) : I18n.L("和出厂时一样");
		TextBlock summary = Ui.Small(text).Trim();
		summary.ToolTip = text;
		Button edit = Ui.Btn(I18n.L("编辑"), BtnKind.Soft, delegate
		{
			OpenBlockEditor(pageId, navId, view, hint);
		});
		Grid acts = Ui.G(null, custom ? "*,6,*" : "*");
		acts.Add(edit, 0);
		if (custom)
		{
			Button reset = Ui.Btn(I18n.L("恢复默认"), BtnKind.Normal, delegate
			{
				PageBase.Run(() => ResetPageAsync(entry));
			});
			acts.Add(reset, 0, 2);
		}
		return Ui.Card(Ui.V(8.0, ThumbView(LayoutPageCards.ThumbOf(pageId, page)), name, summary, acts), 10.0);
	}

	// 缩略图:按该页当前布局画出各块的位置和宽度,组件块用主题色浅底
	private static FrameworkElement ThumbView(LayoutThumb thumb)
	{
		Grid grid = new Grid
		{
			VerticalAlignment = VerticalAlignment.Top
		};
		int cols = Math.Max(1, thumb.Cols);
		for (int c = 0; c < cols; c++)
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(1.0, GridUnitType.Star)
			});
		}
		int rows = 0;
		foreach (LayoutThumbCell cell in thumb.Cells)
		{
			rows = Math.Max(rows, cell.Row + cell.RowSpan);
		}
		for (int r = 0; r < rows; r++)
		{
			grid.RowDefinitions.Add(new RowDefinition
			{
				Height = GridLength.Auto
			});
		}
		foreach (LayoutThumbCell cell in thumb.Cells)
		{
			Border box = new Border
			{
				CornerRadius = new CornerRadius(4.0),
				Height = 14.0 * Math.Max(1, cell.RowSpan) + 4.0 * (Math.Max(1, cell.RowSpan) - 1),
				Margin = new Thickness(2.0),
				Opacity = (thumb.Placeholder ? 0.6 : 1.0)
			};
			box.SetResourceReference(Border.BackgroundProperty, cell.Widget ? "B.AccentSoft" : "B.Line");
			Grid.SetColumn(box, Math.Max(0, Math.Min(cell.Col, cols - 1)));
			Grid.SetColumnSpan(box, Math.Max(1, Math.Min(cell.Span, cols - Math.Max(0, Math.Min(cell.Col, cols - 1)))));
			Grid.SetRow(box, Math.Max(0, cell.Row));
			Grid.SetRowSpan(box, Math.Max(1, cell.RowSpan));
			grid.Children.Add(box);
		}
		Border frame = new Border
		{
			Height = 92.0,
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(6.0),
			ClipToBounds = true,
			Child = grid
		};
		frame.SetResourceReference(Border.BackgroundProperty, "B.Paper2");
		return frame;
	}

	// 没自定义过的页:页面已经建好(设置页的子视图 / 当前显示的页)就读它的默认排布,否则缩略图画成通用占位
	private PageLayoutV2? LivePage(string pageId)
	{
		try
		{
			BlockHost? host = null;
			foreach (UIElement? view in _views)
			{
				if (view != null)
				{
					host = FindBlockHostLogical(view, pageId, 0) ?? FindBlockHostVisual(view, pageId, 0);
					if (host != null)
					{
						break;
					}
				}
			}
			if (host == null && PageBase.Win != null)
			{
				host = FindBlockHost(PageBase.Win, pageId);
			}
			return host?.Editor.State.ToPage();
		}
		catch
		{
			return null;
		}
	}

	private async System.Threading.Tasks.Task ResetPageAsync(LayoutBlockEntryCatalog.Entry entry)
	{
		string name = I18n.L(entry.Label);
		if (!await Dlg.Confirm(I18n.L("「{0}」恢复默认？", name), I18n.L("这一页的排列、隐藏项和放上去的组件都会回到出厂时的样子；组件本身不会被删除。"), I18n.L("恢复默认"), I18n.L("取消")))
		{
			return;
		}
		LayoutV2 doc = LayoutStoreV2.Load();
		if (LayoutPageCards.Reset(doc, entry.PageId) == 0)
		{
			RefreshLayoutView();
			return;
		}
		if (!LayoutStoreV2.Save(doc))
		{
			PageBase.Toast(I18n.L("布局未能保存"), I18n.L("改动只保留在本次运行"), ToastKind.Warning);
			return;
		}
		PageLayoutStore.Reload();
		PageBase.Toast(I18n.L("「{0}」已恢复默认", name), "", ToastKind.Success);
		RefreshLayoutView();
	}

	private async System.Threading.Tasks.Task ResetAllPagesAsync()
	{
		int count = LayoutPageCards.CustomizedCount(LayoutStoreV2.Load());
		if (count == 0)
		{
			PageBase.Toast(I18n.L("所有页面都是默认布局"), "", ToastKind.Info);
			return;
		}
		if (!await Dlg.Confirm(I18n.L("所有页面恢复默认？"), I18n.L("{0} 个页面会回到出厂时的样子。现在这套布局会先存为方案「{1}」，需要时可以在「方案」里切回。", count, LayoutProfiles.PreviousName), I18n.L("全部恢复默认"), I18n.L("取消"), danger: true))
		{
			return;
		}
		string error = LayoutProfiles.ResetAllPages(out string kept, out int removed);
		if (error.Length > 0)
		{
			PageBase.Toast(I18n.L("布局未能保存"), error, ToastKind.Warning);
			return;
		}
		PageLayoutStore.Reload();
		PageBase.Toast(I18n.L("所有页面已恢复默认"), (kept.Length > 0) ? I18n.L("原来的布局已存为「{0}」", kept) : "", ToastKind.Success);
		RefreshLayoutView();
	}

	// 跳到目标页 → 找到那一页的 BlockHost → EnterEdit(等价于点右键菜单里的「自定义布局...」)
	private void OpenBlockEditor(string pageId, string navId, int settingsView, string hint)
	{
		MainWindow? win = PageBase.Win;
		if (win == null)
		{
			return;
		}
		if (settingsView >= 0)
		{
			Select(settingsView);
		}
		if (navId.Length > 0 && navId != Id)
		{
			win.Navigate(navId);
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
			PageBase.Toast(I18n.L("已进入自定义布局"), I18n.L("完成编辑后自动保存"), ToastKind.Success);
			return;
		}
		PageBase.Toast(I18n.L("没找到这个页面"), (hint.Length > 0) ? I18n.L(hint) : I18n.L("打开那个页面后，在空白处右键「自定义布局…」"), ToastKind.Warning);
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

	// ————— 4 方案:卡片(应用 / ⋯),组标题右边是导入 / 导出整套布局 / 存为新方案 —————

	private UIElement LayoutProfilesGroup()
	{
		Button import = SmallBtn(I18n.L("导入…"), BtnKind.Ghost, delegate
		{
			PageBase.Run(ImportLayoutFileAsync);
		});
		Button export = SmallBtn(I18n.L("导出整套布局…"), BtnKind.Ghost, delegate
		{
			PageBase.Run(ExportLayoutAsync);
		});
		Button save = SmallBtn(I18n.L("存为新方案"), BtnKind.Soft, delegate
		{
			PageBase.Run(NewProfileAsync);
		});
		List<LayoutProfileCard> cards = LayoutProfiles.Cards(LayoutStoreV2.Load());
		UIElement body;
		if (cards.Count == 0)
		{
			body = EmptyBox(I18n.L("还没有方案。点右上角「存为新方案」把现在这套布局存下来，以后随时切回。"));
		}
		else
		{
			UniformGrid grid = new UniformGrid
			{
				Columns = 4,
				Margin = new Thickness(-6.0)
			};
			foreach (LayoutProfileCard card in cards)
			{
				grid.Children.Add(ProfileCard(card).M(6.0));
			}
			body = grid;
		}
		return LayoutGroup(I18n.L("方案"), body, import, export, save);
	}

	private FrameworkElement ProfileCard(LayoutProfileCard card)
	{
		string tagText = card.IsActive ? I18n.L("正在使用") : (card.IsAiBackup ? I18n.L("AI 备份") : I18n.L("方案"));
		Border tag = card.IsActive ? Ui.Tag(tagText, "B.AccentText", "B.AccentSoft") : Ui.Tag(tagText);
		tag.HorizontalAlignment = HorizontalAlignment.Left;
		TextBlock name = Ui.Txt(card.DisplayName, 13.0, bold: true).Trim();
		name.ToolTip = card.Name;
		TextBlock info = Ui.Small(card.Info).Wrap();
		info.MinHeight = 34.0;
		Button apply = Ui.Btn(card.IsActive ? I18n.L("当前方案") : I18n.L("应用"), card.IsActive ? BtnKind.Ghost : BtnKind.Soft, delegate
		{
			ApplyProfile(card.Name);
		});
		apply.IsEnabled = !card.IsActive;
		Button more = Ui.IconBtn(Ico.More, I18n.L("更多"), null, 14.0);
		more.Click += delegate
		{
			ShowProfileMenu(more, card);
		};
		Grid acts = Ui.G(null, "*,Auto");
		acts.Add(apply, 0);
		acts.Add(more.M(6.0, 0.0, 0.0, 0.0), 0, 1);
		Border box = Ui.Card(Ui.V(4.0, tag, name, info, acts.M(0.0, 6.0, 0.0, 0.0)), 12.0);
		if (card.IsActive)
		{
			// 正在使用的方案:1.5px 主题色描边
			box.BorderThickness = new Thickness(1.5);
			box.SetResourceReference(Border.BorderBrushProperty, "B.Accent");
		}
		return box;
	}

	private void ShowProfileMenu(Button anchor, LayoutProfileCard card)
	{
		ContextMenu menu = new ContextMenu
		{
			PlacementTarget = anchor,
			Placement = PlacementMode.Bottom
		};
		MenuItem rename = new MenuItem
		{
			Header = I18n.L("重命名")
		};
		rename.Click += delegate
		{
			PageBase.Run(() => RenameProfileAsync(card.Name));
		};
		MenuItem copy = new MenuItem
		{
			Header = I18n.L("复制一份")
		};
		copy.Click += delegate
		{
			PageBase.Run(() => DuplicateProfileAsync(card.Name));
		};
		MenuItem del = new MenuItem
		{
			Header = I18n.L("删除"),
			IsEnabled = !card.IsActive
		};
		if (card.IsActive)
		{
			del.ToolTip = I18n.L("正在使用的方案不能删除，先切到别的方案");
			ToolTipService.SetShowOnDisabled(del, true);
		}
		del.Click += delegate
		{
			PageBase.Run(() => DeleteProfileAsync(card.Name));
		};
		menu.Items.Add(rename);
		menu.Items.Add(copy);
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
		PageBase.Toast(I18n.L("方案「{0}」已保存", name.Trim()), I18n.L("来自现在这套布局"), ToastKind.Success);
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
		PageBase.Toast(I18n.L("已切换到「{0}」", LayoutProfiles.DisplayName(name)), (kept.Length > 0) ? I18n.L("原来的布局已存为「{0}」", kept) : "", ToastKind.Success);
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
		RefreshLayoutView();
	}

	private async System.Threading.Tasks.Task DeleteProfileAsync(string name)
	{
		// 正在使用的方案不能删(菜单里已置灰,这里再兜一道)
		if (string.Equals(name, LayoutProfiles.ActiveName(), StringComparison.Ordinal))
		{
			PageBase.Toast(I18n.L("正在使用的方案不能删除，先切到别的方案"), "", ToastKind.Warning);
			return;
		}
		if (!await Dlg.Confirm(I18n.L("删除"), I18n.L("删除方案「{0}」？", LayoutProfiles.DisplayName(name)), I18n.L("删除"), I18n.L("取消"), danger: true))
		{
			return;
		}
		string error = LayoutProfiles.Delete(name);
		if (error.Length > 0)
		{
			PageBase.Toast(I18n.L("布局未能保存"), error, ToastKind.Warning);
			return;
		}
		PageBase.Toast(I18n.L("方案「{0}」已删除", LayoutProfiles.DisplayName(name)), "", ToastKind.Success);
		RefreshLayoutView();
	}

	// ————— 导入 / 导出整套布局 —————

	private async System.Threading.Tasks.Task ExportLayoutAsync()
	{
		int choice = await Dlg.Choose(I18n.L("导出整套布局"), Ui.Muted(I18n.L("完整格式带上页签、页面排列、分栏宽度和组件；兼容旧版本的格式只含每页区块的顺序和显示 / 隐藏，旧版本程序也能导入。")).Wrap(), new string[3]
		{
			I18n.L("取消"),
			I18n.L("导出完整格式"),
			I18n.L("导出兼容旧版本的格式")
		}, 560.0);
		if (choice == 1)
		{
			string text = LayoutStoreV2.ExportJson(LayoutStoreV2.Load());
			string? path = Dlg.SaveFile(I18n.L("布局 (*.json)|*.json|全部文件|*.*"), "tipcl-layout.json", I18n.L("导出整套布局"));
			if (path != null && WriteTextFile(path, text))
			{
				PageBase.Toast(I18n.L("已导出整套布局"), path, ToastKind.Success);
			}
			return;
		}
		if (choice == 2)
		{
			string text = PageLayoutStore.ExportJson(LayoutV1Projection.ProjectV1(LayoutStoreV2.Load()));
			string? path2 = Dlg.SaveFile(I18n.L("布局 (*.json)|*.json|全部文件|*.*"), "tipcl-page-layout.json", I18n.L("导出兼容旧版本的格式"));
			if (path2 != null && WriteTextFile(path2, text))
			{
				PageBase.Toast(I18n.L("已导出整套布局"), path2, ToastKind.Success);
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
			int choice = await Dlg.Choose(I18n.L("导入布局"), Ui.Muted(I18n.L("这是一整套布局（含页签、页面排列、分栏宽度和组件）。导入的网页和按钮组件默认关闭，需要你逐个打开。")).Wrap(), new string[3]
			{
				I18n.L("取消"),
				I18n.L("应用为当前布局"),
				I18n.L("存为新方案")
			}, 560.0);
			if (choice == 1)
			{
				string error = LayoutProfiles.ApplySnapshot(whole.Layout, out string kept);
				if (error.Length > 0)
				{
					PageBase.Toast(I18n.L("布局未能保存"), error, ToastKind.Warning);
					return;
				}
				PageBase.Toast(I18n.L("已应用导入的布局"), (kept.Length > 0) ? I18n.L("原来的布局已存为「{0}」", kept) : System.IO.Path.GetFileName(path), ToastKind.Success);
				RefreshLayoutView();
			}
			else if (choice == 2)
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
				PageBase.Toast(I18n.L("方案「{0}」已保存", name.Trim()), "", ToastKind.Success);
				RefreshLayoutView();
			}
			return;
		}
		// 兼容旧版本的页面布局:把每页的 {id,hidden} 与顺序并进当前文档的 pages
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
			PageLayoutStore.Reload();
			PageBase.Toast(I18n.L("已导入 {0} 个页面的排列", num), System.IO.Path.GetFileName(path), ToastKind.Success);
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

	// ————— 5 高级(默认折叠):分栏宽度 / 网页和按钮组件 / 组件管理 / 布局文件 —————

	private UIElement LayoutAdvancedHead()
	{
		Grid head = Ui.G(null, "*,Auto");
		head.Add(Ui.V(2.0, Ui.Txt(I18n.L("分栏、网页与按钮组件、布局文件"), 13.0, bold: true), Ui.Small(I18n.L("一般不需要动"))).VCenter(), 0);
		head.Add(Ui.Glyph(_layoutAdvancedOpen ? Ico.ChevronUp : Ico.ChevronDown, 12.0, "B.InkSoft").M(12.0, 0.0, 4.0, 0.0), 0, 1);
		Border hit = new Border
		{
			Background = Brushes.Transparent,
			Padding = new Thickness(0.0, 4.0, 0.0, 4.0),
			Child = head
		};
		ContentControl toggle = new ContentControl
		{
			Content = hit,
			Focusable = true,
			IsTabStop = true,
			Cursor = Cursors.Hand
		};
		AutomationProperties.SetName(toggle, _layoutAdvancedOpen ? I18n.L("收起高级设置") : I18n.L("展开高级设置"));
		toggle.MouseLeftButtonUp += delegate
		{
			ExpandLayoutAdvanced(!_layoutAdvancedOpen);
		};
		toggle.KeyDown += delegate(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter || e.Key == Key.Space)
			{
				e.Handled = true;
				ExpandLayoutAdvanced(!_layoutAdvancedOpen);
			}
		};
		List<FrameworkElement> rows = new List<FrameworkElement> { toggle };
		if (_layoutAdvancedOpen)
		{
			rows.Add(LayoutSplitRow());
		}
		return LayoutGroup(I18n.L("高级"), Ui.Card(Rows(rows.ToArray())));
	}

	// 分栏宽度:只剩一行「已调整 N 处 + 全部复位」(具体宽度在页面上拖分隔线调)
	private FrameworkElement LayoutSplitRow()
	{
		List<string> names = LayoutSplitSummary.AdjustedNames(LayoutStoreV2.Load());
		string text = (names.Count > 0) ? I18n.L("已调整 {0} 处：{1}。在页面上拖两栏中间的分隔线即可调整，双击复位。", names.Count, string.Join(I18n.L("、"), names)) : I18n.L("所有分栏都是默认宽度。在页面上拖两栏中间的分隔线即可调整，双击复位。");
		Button reset = Ui.Btn(I18n.L("全部复位"), BtnKind.Normal, delegate
		{
			ResetAllSplits();
		});
		reset.IsEnabled = names.Count > 0;
		return LayoutRow(I18n.L("分栏宽度"), text, reset);
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
		PageBase.Toast((count > 0) ? I18n.L("分栏宽度已全部复位") : I18n.L("所有分栏都是默认宽度"), (count > 0) ? I18n.L("已复位 {0} 处", count) : "", ToastKind.Success);
		RefreshLayoutView();
	}

	private UIElement LayoutAdvancedWidgets()
	{
		if (!_layoutAdvancedOpen)
		{
			return CollapsedBlock();
		}
		bool allow = LayoutEditPrefs.AllowDangerWidgets;
		CheckBox toggle = Ui.Switch(allow);
		Border warn = WarnBox(I18n.L("导入的布局里如果带网页或按钮组件，仍然默认关闭，需要你逐个打开。"));
		warn.Visibility = (allow ? Visibility.Visible : Visibility.Collapsed);
		toggle.Checked += delegate
		{
			if (_layoutRebuilding)
			{
				return;
			}
			LayoutEditPrefs.AllowDangerWidgets = true;
			warn.Visibility = Visibility.Visible;
			PageBase.Toast(I18n.L("已开启网页和按钮组件"), I18n.L("在页面的编辑态里点「添加」就能找到它们"), ToastKind.Success);
		};
		toggle.Unchecked += delegate
		{
			if (_layoutRebuilding)
			{
				return;
			}
			LayoutEditPrefs.AllowDangerWidgets = false;
			warn.Visibility = Visibility.Collapsed;
			PageBase.Toast(I18n.L("已关闭网页和按钮组件"), I18n.L("已经放在页面上的不受影响"), ToastKind.Success);
		};
		AutomationProperties.SetName(toggle, I18n.L("网页和按钮组件"));
		FrameworkElement danger = Ui.V(8.0, LayoutRow(I18n.L("网页和按钮组件"), I18n.L("开启后可以添加嵌入网页（只接受 https）和执行动作的按钮"), toggle), warn);
		LayoutV2 doc = LayoutStoreV2.Load();
		int count = (doc.Layout.Widgets != null) ? doc.Layout.Widgets.Count((WidgetDef w) => w != null) : 0;
		Button manage = Ui.Btn(_layoutWidgetPanelOpen ? I18n.L("收起") : I18n.L("管理…"), BtnKind.Normal, delegate
		{
			_layoutWidgetPanelOpen = !_layoutWidgetPanelOpen;
			if (!_layoutWidgetPanelOpen)
			{
				_layoutWidgetCreateOpen = false;
			}
			RefreshLayoutView();
		});
		List<FrameworkElement> rows = new List<FrameworkElement>
		{
			danger,
			LayoutRow(I18n.L("组件管理"), I18n.L("已创建 {0} 个组件", count), manage)
		};
		if (_layoutWidgetPanelOpen)
		{
			rows.AddRange(LayoutWidgetRows(doc));
		}
		return Ui.Card(Rows(rows.ToArray()));
	}

	// 组件管理的内联面板:新建表单 + 已有组件逐个编辑 / 删除
	private FrameworkElement[] LayoutWidgetRows(LayoutV2 doc)
	{
		List<WidgetDef> widgets = ((doc.Layout.Widgets != null) ? doc.Layout.Widgets : new List<WidgetDef>());
		List<FrameworkElement> rows = new List<FrameworkElement>();
		// 新建表单:只列出 5 种可直接创建的类型(网页 / 按钮的参数要在布局文件里配置)
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
			rows.Add(Ui.Field(I18n.L("新建"), create, I18n.L("新建的组件不会自动放到页面上；在页面的编辑态里点「添加」放进去")));
		}
		if (widgets.Count == 0)
		{
			rows.Add(Ui.Field(I18n.L("还没有组件"), Ui.Muted(I18n.L("点「新建组件」建一个便签、时钟等")), ""));
		}
		else
		{
			foreach (WidgetDef widget in widgets)
			{
				if (widget != null)
				{
					rows.Add(WidgetAdminRow(doc, widget));
				}
			}
		}
		rows.Add(Ui.Field(I18n.L("网页和按钮"), Ui.Muted(I18n.L("网页和按钮组件的地址、动作请在「布局文件」里编辑")).Wrap(), ""));
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
			Ui.Field(I18n.L("标题"), title, I18n.L("页面上显示的名字")),
			Ui.Field(I18n.L("参数"), paramRow, ""),
			Ui.H(8.0, create, cancel),
			hint.M(0.0, 0.0, 0.0, 6.0));
	}

	// 已有组件:标题 / 类型 / 用在几个页面 + 参数编辑 + 保存 / 删除
	private FrameworkElement WidgetAdminRow(LayoutV2 doc, WidgetDef widget)
	{
		int refs = WidgetLibrary.CountReferences(doc, widget.Id);
		bool safe = WidgetLibrary.IsSafeType(widget.Type);
		TextBox title = Ui.Input("", widget.Title ?? "");
		title.Width = 180.0;
		TextBlock refText = Ui.Small(I18n.L("用在 {0} 个页面上", refs));
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
			body = Ui.V(6.0, head, Ui.H(8.0, Ui.Small(I18n.L("网页和按钮组件的地址、动作请在「布局文件」里编辑")).VCenter(), save, del));
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
				string name = string.IsNullOrWhiteSpace(widget.Title) ? WidgetUI.TypeLabel(widget.Type) : widget.Title;
				string text = ((refs > 0) ? I18n.L("会从 {0} 个页面上拿掉它，删除后不能撤销。", refs) : I18n.L("删除后不能撤销。"));
				if (!await Dlg.Confirm(I18n.L("删除组件「{0}」？", name), text, I18n.L("删除"), I18n.L("取消"), danger: true))
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

	// 布局文件:打开所在文件夹 / 在独立窗口里用 JSON 编辑(页面上不再常驻 JSON 框)
	private UIElement LayoutAdvancedFile()
	{
		if (!_layoutAdvancedOpen)
		{
			return CollapsedBlock();
		}
		Button folder = Ui.Btn(I18n.L("打开所在文件夹"), BtnKind.Normal, delegate
		{
			OpenLayoutFolder();
		});
		Button editor = Ui.Btn(I18n.L("在编辑器中打开…"), BtnKind.Normal, delegate
		{
			LayoutJsonWindow.ShowFor(PageBase.Win);
		});
		return Ui.Card(Rows(LayoutRow(I18n.L("布局文件"), I18n.L("用 JSON 编辑整套布局，适合批量修改或分享给别人"), Ui.H(8.0, folder, editor))));
	}

	private static void OpenLayoutFolder()
	{
		try
		{
			string file = LayoutStoreV2.FilePath;
			if (File.Exists(file))
			{
				System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + file + "\"");
			}
			else
			{
				System.Diagnostics.Process.Start("explorer.exe", "\"" + LayoutStoreV2.DirPath + "\"");
			}
		}
		catch (Exception ex)
		{
			PageBase.Toast(I18n.L("没能打开文件夹"), ex.Message, ToastKind.Error);
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

/// <summary>页面卡片缩略图里的一块:网格坐标(列 / 行 / 跨列 / 跨行)+ 是否组件块(主题色浅底)。</summary>
public sealed class LayoutThumbCell
{
	public string Id { get; set; } = "";

	public int Col { get; set; }

	public int Row { get; set; }

	public int Span { get; set; } = 1;

	public int RowSpan { get; set; } = 1;

	/// <summary>自建组件 / 插件块(custom: / plugin:)。</summary>
	public bool Widget { get; set; }
}

/// <summary>一张页面卡片的缩略图:列数 + 各块;Placeholder = 没有可用的排布(没自定义过、页面也还没建),画成通用占位。</summary>
public sealed class LayoutThumb
{
	public int Cols { get; set; } = 1;

	public List<LayoutThumbCell> Cells { get; set; } = new List<LayoutThumbCell>();

	public bool Placeholder { get; set; }
}

/// <summary>
/// 设置 → 布局「页面」组的纯逻辑(零 WPF 依赖,net8 测试宿主直接断言):
/// 哪些页出卡片(入口表去掉单块页与「布局」页自己 = 15 张,默认先显示 6 张)、「已自定义」判定
/// (= LayoutStoreV2 文档里有没有这一页的条目,主体或窄窗变体任一份)、摘要、缩略图、恢复默认。
/// </summary>
public static class LayoutPageCards
{
	/// <summary>默认先显示的 6 张(其余在「显示全部」里)。</summary>
	public static readonly string[] FeaturedPageIds = new string[6] { "launch", "download", "library:main", "multiplayer", "account", "ai" };

	/// <summary>默认显示几张。</summary>
	public const int FeaturedCount = 6;

	/// <summary>「布局」页自己的 pageId(不出卡片:在本页里编辑本页没有意义)。</summary>
	public const string LayoutPageId = "settings:layout";

	/// <summary>区块间距三档的存储值(紧凑 6 / 标准 10 / 宽松 14,数值见 LayoutAppearance)。</summary>
	public static readonly string[] DensityKeys = new string[3] { LayoutSchema.DensityCompact, LayoutSchema.DensityStandard, LayoutSchema.DensityLoose };

	/// <summary>该入口出不出卡片:单块页(只有一块内容,不提供编辑)和「布局」页自己不出。</summary>
	public static bool IsCardPage(LayoutBlockEntryCatalog.Entry? entry)
	{
		return entry != null && !entry.SingleBlock && !string.Equals(entry.PageId, LayoutPageId, StringComparison.Ordinal);
	}

	/// <summary>全部卡片(显示顺序:默认 6 张在前,其余按入口表顺序)。</summary>
	public static List<LayoutBlockEntryCatalog.Entry> Cards()
	{
		List<LayoutBlockEntryCatalog.Entry> list = new List<LayoutBlockEntryCatalog.Entry>();
		foreach (string pageId in FeaturedPageIds)
		{
			LayoutBlockEntryCatalog.Entry? entry = LayoutBlockEntryCatalog.Find(pageId);
			if (IsCardPage(entry))
			{
				list.Add(entry!);
			}
		}
		foreach (LayoutBlockEntryCatalog.Entry entry2 in LayoutBlockEntryCatalog.All)
		{
			if (IsCardPage(entry2) && !list.Contains(entry2))
			{
				list.Add(entry2);
			}
		}
		return list;
	}

	/// <summary>当前要画的卡片:收起时只给前 6 张。</summary>
	public static List<LayoutBlockEntryCatalog.Entry> Shown(bool all)
	{
		List<LayoutBlockEntryCatalog.Entry> cards = Cards();
		return all ? cards : cards.Take(FeaturedCount).ToList();
	}

	/// <summary>卡片网格下方那一行说明:哪些页只有一块内容、不提供编辑。</summary>
	public static string SingleBlockNote()
	{
		List<string> names = new List<string>();
		foreach (LayoutBlockEntryCatalog.Entry entry in LayoutBlockEntryCatalog.All)
		{
			if (entry.SingleBlock)
			{
				names.Add(I18n.L("「{0}」", I18n.L(entry.Label)));
			}
		}
		return (names.Count > 0) ? I18n.L("{0}只有一块内容，不提供编辑。", string.Concat(names)) : "";
	}

	// 主 body + 各窄窗变体(变体只看一层,与 LayoutVariantWrite 同口径)
	private static IEnumerable<LayoutBody> Bodies(LayoutV2? doc)
	{
		if (doc?.Layout == null)
		{
			yield break;
		}
		yield return doc.Layout;
		if (doc.Layout.Variants == null)
		{
			yield break;
		}
		foreach (LayoutBody variant in doc.Layout.Variants.Values)
		{
			if (variant != null)
			{
				yield return variant;
			}
		}
	}

	/// <summary>文档里这一页的条目:主 body 优先,没有再看窄窗变体;都没有 = null(= 默认布局)。</summary>
	public static PageLayoutV2? PageOf(LayoutV2? doc, string? pageId)
	{
		if (string.IsNullOrEmpty(pageId))
		{
			return null;
		}
		foreach (LayoutBody body in Bodies(doc))
		{
			if (body.Pages != null && body.Pages.TryGetValue(pageId!, out PageLayoutV2? page) && page != null)
			{
				return page;
			}
		}
		return null;
	}

	/// <summary>「已自定义」= LayoutStoreV2 文档里有这一页的条目(主 body 或窄窗变体任一份)。</summary>
	public static bool IsCustomized(LayoutV2? doc, string? pageId)
	{
		return PageOf(doc, pageId) != null;
	}

	/// <summary>文档里有条目的页面数(主 body 与变体合并去重)。</summary>
	public static int CustomizedCount(LayoutV2? doc)
	{
		HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (LayoutBody body in Bodies(doc))
		{
			if (body.Pages == null)
			{
				continue;
			}
			foreach (KeyValuePair<string, PageLayoutV2> kv in body.Pages)
			{
				if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
				{
					ids.Add(kv.Key);
				}
			}
		}
		return ids.Count;
	}

	/// <summary>恢复默认:从主 body 与各变体删掉这一页的条目(不落盘,调用方 Save);返回删掉的份数。组件定义不动。</summary>
	public static int Reset(LayoutV2? doc, string? pageId)
	{
		if (string.IsNullOrEmpty(pageId))
		{
			return 0;
		}
		int removed = 0;
		foreach (LayoutBody body in Bodies(doc).ToList())
		{
			if (body.Pages != null && body.Pages.Remove(pageId!))
			{
				removed++;
			}
		}
		return removed;
	}

	/// <summary>全部恢复默认:清空主 body 与各变体的 pages(不落盘);返回涉及的页面数。页签、分栏、组件定义都不动。</summary>
	public static int ResetAll(LayoutV2? doc)
	{
		int count = CustomizedCount(doc);
		foreach (LayoutBody body in Bodies(doc).ToList())
		{
			body.Pages?.Clear();
		}
		return count;
	}

	/// <summary>已自定义页的一行摘要,例如「网格 · 1 个组件 · 隐藏 1 项」;什么都没多出来时写「调整了顺序」。</summary>
	public static string Summary(PageLayoutV2? page)
	{
		if (page == null)
		{
			return I18n.L("和出厂时一样");
		}
		List<string> bits = new List<string>();
		if (page.Mode == LayoutSchema.ModeGrid || page.Mode == LayoutSchema.ModeCanvas)
		{
			// 自由画布在界面上已经并入网格(按网格渲染),摘要同样写「网格」
			bits.Add(I18n.L("网格"));
		}
		int widgets = 0;
		int hidden = 0;
		if (page.Blocks != null)
		{
			foreach (BlockLayout block in page.Blocks)
			{
				if (block == null || string.IsNullOrEmpty(block.Id))
				{
					continue;
				}
				bool builtin = LayoutEngine.Classify(block.Id) == LayoutContentKind.Builtin;
				if (!builtin && !block.Hidden)
				{
					widgets++;
				}
				else if (builtin && block.Hidden)
				{
					hidden++;
				}
			}
		}
		if (widgets > 0)
		{
			bits.Add(I18n.L("{0} 个组件", widgets));
		}
		if (hidden > 0)
		{
			bits.Add(I18n.L("隐藏 {0} 项", hidden));
		}
		return (bits.Count > 0) ? string.Join(" · ", bits) : I18n.L("调整了顺序");
	}

	/// <summary>缩略图:网格按 LayoutGrid.Pack 的槽位;列表按 LayoutEngine.Plan 的行;自由画布按「先 y 后 x」推成网格。</summary>
	public static LayoutThumb ThumbOf(string? pageId, PageLayoutV2? page)
	{
		if (page == null || page.Blocks == null)
		{
			LayoutThumb placeholder = new LayoutThumb
			{
				Cols = 1,
				Placeholder = true
			};
			for (int i = 0; i < 3; i++)
			{
				placeholder.Cells.Add(new LayoutThumbCell
				{
					Id = "",
					Col = 0,
					Row = i,
					Span = 1
				});
			}
			return placeholder;
		}
		LayoutThumb thumb = new LayoutThumb();
		if (page.Mode == LayoutSchema.ModeGrid)
		{
			thumb.Cols = LayoutGrid.Columns(page);
			foreach (LayoutCell cell in LayoutGrid.Pack(page))
			{
				thumb.Cells.Add(new LayoutThumbCell
				{
					Id = cell.Id,
					Col = cell.X,
					Row = cell.Y,
					Span = cell.W,
					RowSpan = cell.H,
					Widget = LayoutEngine.Classify(cell.Id) != LayoutContentKind.Builtin
				});
			}
		}
		else if (page.Mode == LayoutSchema.ModeCanvas)
		{
			thumb.Cols = LayoutGrid.Columns(page);
			thumb.Cells.AddRange(CanvasAsRows(page, thumb.Cols));
		}
		else
		{
			LayoutPlan plan = LayoutEngine.Plan(pageId ?? "", page);
			thumb.Cols = Math.Max(1, plan.Columns);
			for (int r = 0; r < plan.Rows.Count; r++)
			{
				foreach (LayoutSlot slot in plan.Rows[r].Slots)
				{
					thumb.Cells.Add(new LayoutThumbCell
					{
						Id = slot.Id,
						Col = slot.Column,
						Row = r,
						Span = Math.Max(1, slot.Span),
						Widget = LayoutEngine.Classify(slot.Id) != LayoutContentKind.Builtin
					});
				}
			}
		}
		// 缩略图只有 92px 高:最多画前 8 行,够看出整体形状
		thumb.Cells = thumb.Cells.Where((LayoutThumbCell c) => c.Row < 8).ToList();
		return thumb;
	}

	// 自由画布(界面上已并入网格)的缩略:可见块按「先 y 后 x」排序,宽度按 w × 列数取整,依次铺满一行再换行
	private static List<LayoutThumbCell> CanvasAsRows(PageLayoutV2 page, int cols)
	{
		List<LayoutThumbCell> cells = new List<LayoutThumbCell>();
		List<KeyValuePair<int, BlockLayout>> visible = new List<KeyValuePair<int, BlockLayout>>();
		for (int i = 0; i < page.Blocks.Count; i++)
		{
			BlockLayout block = page.Blocks[i];
			if (block != null && !string.IsNullOrEmpty(block.Id) && !block.Hidden)
			{
				visible.Add(new KeyValuePair<int, BlockLayout>(i, block));
			}
		}
		visible.Sort(delegate(KeyValuePair<int, BlockLayout> a, KeyValuePair<int, BlockLayout> b)
		{
			int byY = a.Value.Y.CompareTo(b.Value.Y);
			if (byY != 0)
			{
				return byY;
			}
			int byX = a.Value.X.CompareTo(b.Value.X);
			return (byX != 0) ? byX : a.Key.CompareTo(b.Key);
		});
		int row = 0;
		int col = 0;
		foreach (KeyValuePair<int, BlockLayout> kv in visible)
		{
			double w = kv.Value.W;
			int span = (w > 1.0) ? (int)Math.Round(w) : (int)Math.Round(w * cols);
			span = Math.Max(1, Math.Min(cols, span));
			if (col > 0 && col + span > cols)
			{
				row++;
				col = 0;
			}
			cells.Add(new LayoutThumbCell
			{
				Id = kv.Value.Id,
				Col = col,
				Row = row,
				Span = span,
				Widget = LayoutEngine.Classify(kv.Value.Id) != LayoutContentKind.Builtin
			});
			col += span;
		}
		return cells;
	}

	/// <summary>把文档里的 density 归一到三档之一(未知 / 空 = 标准)。</summary>
	public static string NormalizeDensity(string? density)
	{
		foreach (string key in DensityKeys)
		{
			if (string.Equals(key, density, StringComparison.Ordinal))
			{
				return key;
			}
		}
		return LayoutSchema.DensityStandard;
	}

	/// <summary>区块间距三档的显示名。</summary>
	public static string DensityLabel(string? density)
	{
		return NormalizeDensity(density) switch
		{
			LayoutSchema.DensityCompact => I18n.L("紧凑"),
			LayoutSchema.DensityLoose => I18n.L("宽松"),
			_ => I18n.L("标准")
		};
	}
}

/// <summary>「高级 → 分栏宽度」那一行的纯逻辑:哪些左右分栏被拖过(文档里存了比例),以及它们给用户看的名字。</summary>
public static class LayoutSplitSummary
{
	/// <summary>存过比例的左右分栏(按 SplitCatalog 顺序)。</summary>
	public static List<SplitSite> Adjusted(LayoutV2? doc)
	{
		List<SplitSite> list = new List<SplitSite>();
		if (doc?.Layout?.Splits == null)
		{
			return list;
		}
		foreach (SplitSite site in SplitCatalog.All)
		{
			if (site.IsTwoColumn && doc.Layout.Splits.TryGetValue(site.PageId, out SplitState? split) && split?.Ratios != null && split.Ratios.ContainsKey(site.SplitId))
			{
				list.Add(site);
			}
		}
		return list;
	}

	/// <summary>存过比例的分栏名字,例如「设置左侧导航」「实例库文件夹栏」。</summary>
	public static List<string> AdjustedNames(LayoutV2? doc)
	{
		return Adjusted(doc).Select(NameOf).ToList();
	}

	/// <summary>一处分栏给用户看的名字(不出现 pageId / splitId 这类内部写法)。</summary>
	public static string NameOf(SplitSite site)
	{
		string where = (site?.PageId ?? "") + "/" + (site?.SplitId ?? "");
		switch (where)
		{
		case "library/folders":
			return I18n.L("实例库文件夹栏");
		case "settings/nav":
			return I18n.L("设置左侧导航");
		case "version-manage/nav":
			return I18n.L("版本管理左栏");
		case "ai/history":
			return I18n.L("AI 助手历史栏");
		case "feedback/side":
			return I18n.L("反馈页右栏");
		case "account/hero":
			return I18n.L("账号页头像卡");
		default:
		{
			LayoutBlockEntryCatalog.Entry? entry = LayoutBlockEntryCatalog.Find(site?.PageId);
			return (entry != null) ? I18n.L(entry.Label) : I18n.L("其它页面");
		}
		}
	}
}

/// <summary>「方案」组里的一张卡片(纯数据)。</summary>
public sealed class LayoutProfileCard
{
	/// <summary>方案名(文档里的键)。</summary>
	public string Name { get; set; } = "";

	/// <summary>显示名(AI 备份改写成「AI 改布局前 + 时间」)。</summary>
	public string DisplayName { get; set; } = "";

	/// <summary>一行信息(几个页面自定义过、间距档;AI 备份 / 自动留底写来源)。</summary>
	public string Info { get; set; } = "";

	/// <summary>正在使用(= active_profile):描边、不能删除。</summary>
	public bool IsActive { get; set; }

	/// <summary>AI 改布局前自动存的备份(名字以「AI 备份 」开头)。</summary>
	public bool IsAiBackup { get; set; }
}

/// <summary>
/// 设置页「布局」视图里「页面」一组的入口表(纯数据、零 WPF 依赖,net8 测试宿主可直接断言):
/// 覆盖全仓 18 个 BlockHost 宿主(R34 补上了实例库 library:main)。单块页(settings:network /
/// settings:about)仍留在表里并用 <see cref="Entry.SingleBlock"/> 标出来:页面卡片不列它们,
/// 只在卡片下方用一行字说明(见 <see cref="LayoutPageCards"/>);「布局」页自己也不出卡片。
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

		/// <summary>是否单块页(只有一块内容:不提供编辑,页面卡片里不列)。</summary>
		public bool SingleBlock { get; set; }

		/// <summary>
		/// 找不到宿主时的提示文案 key(空 = 通用提示)。安装页 / 实例详情里的版本管理这类"子视图"
		/// 不能直接跳过去,只能先把它打开再右键,提示就写清楚怎么打开。
		/// </summary>
		public string Hint { get; set; } = "";
	}

	/// <summary>18 个宿主:设置页内 8 个 + 顶栏导航 7 个 + 实例库 + 子视图 2 个(安装页、实例详情里内嵌的版本管理)。</summary>
	public static readonly Entry[] All = new Entry[18]
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
		// 实例库是区块宿主(文件夹列表 / 版本选择 / 实例状态,LibraryPage),导航用顶部页签 library
		new Entry
		{
			PageId = "library:main",
			NavId = "library",
			Label = "实例库"
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

	/// <summary>
	/// AI 改布局前自动备份的方案名前缀(与 Core 的 LayoutTools.BackupPrefix 同值,单测钉住两边不漂移):
	/// 方案卡片靠它识别「AI 备份」,并把显示名改成「AI 改布局前 + 时间」。
	/// </summary>
	public const string AiBackupPrefix = "AI 备份 ";

	/// <summary>是不是 AI 改布局前自动存的备份。</summary>
	public static bool IsAiBackup(string? name)
	{
		return !string.IsNullOrEmpty(name) && name!.StartsWith(AiBackupPrefix, StringComparison.Ordinal);
	}

	/// <summary>方案卡片上的显示名:AI 备份写成「AI 改布局前 MM-dd HH:mm」,其余原样。</summary>
	public static string DisplayName(string? name)
	{
		string text = name ?? "";
		if (!IsAiBackup(text))
		{
			return text;
		}
		string suffix = text.Substring(AiBackupPrefix.Length).Trim();
		if (DateTime.TryParseExact(suffix, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime when))
		{
			suffix = when.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
		}
		return I18n.L("AI 改布局前 {0}", suffix).Trim();
	}

	/// <summary>方案卡片上的一行信息:几个页面自定义过 + 间距档;AI 备份 / 自动留底写来源。</summary>
	public static string CardInfo(string? name, LayoutBody? body)
	{
		if (IsAiBackup(name))
		{
			return I18n.L("AI 改布局之前自动存的一份");
		}
		if (string.Equals(name, PreviousName, StringComparison.Ordinal))
		{
			return I18n.L("切换方案或恢复默认之前自动留的底");
		}
		int pages = (body?.Pages != null) ? body.Pages.Count((KeyValuePair<string, PageLayoutV2> kv) => !string.IsNullOrEmpty(kv.Key) && kv.Value != null) : 0;
		string first = ((pages > 0) ? I18n.L("{0} 个页面自定义过", pages) : I18n.L("各页面都是默认排列"));
		return first + " · " + I18n.L("{0}间距", LayoutPageCards.DensityLabel(body?.Shell?.Density));
	}

	/// <summary>「方案」组的卡片(文档顺序);正在使用的那张标出来(描边、不能删)。</summary>
	public static List<LayoutProfileCard> Cards(LayoutV2? doc)
	{
		List<LayoutProfileCard> list = new List<LayoutProfileCard>();
		if (doc?.Profiles == null)
		{
			return list;
		}
		string active = doc.ActiveProfile ?? "";
		foreach (KeyValuePair<string, LayoutBody> kv in doc.Profiles)
		{
			if (string.IsNullOrEmpty(kv.Key))
			{
				continue;
			}
			list.Add(new LayoutProfileCard
			{
				Name = kv.Key,
				DisplayName = DisplayName(kv.Key),
				Info = CardInfo(kv.Key, kv.Value),
				IsActive = (active.Length > 0 && string.Equals(kv.Key, active, StringComparison.Ordinal)),
				IsAiBackup = IsAiBackup(kv.Key)
			});
		}
		return list;
	}

	/// <summary>
	/// 「页面」组的「全部恢复默认」:先把当前布局留底成「上一次布局」(与应用方案同一套),再清空各页条目
	/// (主体与窄窗变体);页签、分栏、组件定义都不动,回到直接编辑态。没有自定义过的页时什么也不做。
	/// </summary>
	public static string ResetAllPages(out string kept, out int removed)
	{
		kept = "";
		removed = 0;
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			if (LayoutPageCards.CustomizedCount(doc) == 0)
			{
				return "";
			}
			kept = KeepPrevious(doc);
			removed = LayoutPageCards.ResetAll(doc);
			doc.ActiveProfile = "";
			return LayoutStoreV2.Save(doc) ? "" : I18n.L("布局未能保存");
		}
		catch (Exception ex)
		{
			kept = "";
			removed = 0;
			return ex.Message;
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
