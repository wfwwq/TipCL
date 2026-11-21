using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using TipCL.Services;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Pages.Layout;

// 自定义布局 v3「编辑态」(效果图 secA):内容是主角,编辑控件只在需要时出现。
//   - 顶部一条工具条:标题「编辑「<页名>」页」· 列表 | 网格 · 撤销/重做图标 · 添加(数字 = 隐藏了几项)· 重置本页 · 完成 · ⋯
//   - ⋯ 菜单:导出本页… / 导入到本页… / 整套布局与方案…(跳 设置 → 布局)
//   - 「添加」抽屉:已隐藏的内容(显示)+ 组件(新建 / 添加已有)+ 底部「网页、按钮组件默认关闭…」可点跳转
//   - 模式只剩 列表(stack)/ 网格(grid);存档里的 canvas 读取时按网格呈现(LayoutCanvasCompat),编辑后存为 grid
// 本文件只放 v3 新增的界面与入口;手势 / 落盘 / 撤销栈仍是 LayoutEditor.cs 里的那一套。
public sealed partial class LayoutEditor
{
	// ————— 顶部工具条 —————

	private readonly TextBlock _v3Title = new TextBlock();

	private readonly TextBlock _v3Hint = new TextBlock();

	private readonly Border _v3ModeList = new Border();

	private readonly Border _v3ModeGrid = new Border();

	private readonly TextBlock _v3ModeListText = new TextBlock();

	private readonly TextBlock _v3ModeGridText = new TextBlock();

	private Button? _v3AddBtn;

	private Border? _v3HidBadge;

	// ————— 「添加」抽屉(覆盖在内容区右侧) —————

	private readonly Border _v3Drawer = new Border();

	private readonly TextBlock _v3DrawerTitle = new TextBlock();

	private readonly SPanel _v3DrawerHidden = new SPanel
	{
		Orientation = Orientation.Vertical,
		Spacing = 6.0
	};

	private readonly SPanel _v3DrawerWidgets = new SPanel
	{
		Orientation = Orientation.Vertical,
		Spacing = 6.0
	};

	private bool _v3DrawerOpen;

	// 「网页和按钮组件」开关(设置 → 布局 → 高级,B 的 LayoutSettingsPrefs)变了就刷新抽屉:只在抽屉开着时订阅
	private Action? _v3PrefsChanged;

	// 便签 / 快捷启动需要一个参数:点「添加」后在该行下面展开一个输入框
	private string _v3PendingType = "";

	// ————— 单块页判定(LayoutShellHooks.CanEditPage 用):pageId → 页面交来的 BlockSpec 个数 —————

	private static readonly object _v3Gate = new object();

	private static readonly Dictionary<string, int> _v3SpecCounts = new Dictionary<string, int>(StringComparer.Ordinal);

	private static void RegisterSpecCount(string pageId, int count)
	{
		if (string.IsNullOrEmpty(pageId))
		{
			return;
		}
		lock (_v3Gate)
		{
			_v3SpecCounts[pageId] = count;
		}
	}

	/// <summary>某页构造时交来的区块数(页面还没构造过返回 -1)。</summary>
	internal static int SpecCountOf(string? pageId)
	{
		if (string.IsNullOrEmpty(pageId))
		{
			return -1;
		}
		lock (_v3Gate)
		{
			return _v3SpecCounts.TryGetValue(pageId!, out int n) ? n : -1;
		}
	}

	/// <summary>页面的显示名(工具条标题 / 抽屉标题用):优先布局入口表里的文案,其次常见页,兜底「本页」。</summary>
	internal static string PageLabelOf(string? pageId)
	{
		LayoutBlockEntryCatalog.Entry? entry = LayoutBlockEntryCatalog.Find(pageId);
		if (entry != null && !string.IsNullOrEmpty(entry.Label))
		{
			return I18n.L(entry.Label);
		}
		switch (pageId)
		{
		case "library:main":
			return I18n.L("实例库");
		case "plugins":
			return I18n.L("插件");
		case "plugins:market":
			return I18n.L("插件市场");
		default:
			return "";
		}
	}

	// 构造时调用一次:把工具条装进 _barHost、把抽屉准备好(此时 _root 还没建,抽屉由构造函数挂到 _root 上)
	private void BuildV3Chrome()
	{
		string label = PageLabelOf(_pageId);
		_v3Title.Text = (label.Length > 0) ? I18n.L("编辑「{0}」页", label) : I18n.L("编辑本页");
		_v3Title.FontSize = 13.0;
		_v3Title.FontWeight = FontWeights.Bold;
		_v3Title.VerticalAlignment = VerticalAlignment.Center;
		_v3Title.TextTrimming = TextTrimming.CharacterEllipsis;
		_v3Title.Margin = new Thickness(0.0, 0.0, 10.0, 0.0);
		_v3Title.SetResourceReference(TextBlock.ForegroundProperty, "B.Ink");
		// 列表 | 网格 分段(自绘:不依赖按钮模板能不能换底色)
		Border seg = new Border
		{
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(3.0),
			VerticalAlignment = VerticalAlignment.Center,
			Child = Ui.H(2.0, ModeItem(_v3ModeList, _v3ModeListText, LayoutIcons.ModeList, I18n.L("列表"), false), ModeItem(_v3ModeGrid, _v3ModeGridText, LayoutIcons.ModeGrid, I18n.L("网格"), true))
		};
		seg.SetResourceReference(Border.BackgroundProperty, "B.Paper2");
		_v3Hint.FontSize = 12.0;
		_v3Hint.VerticalAlignment = VerticalAlignment.Center;
		_v3Hint.TextTrimming = TextTrimming.CharacterEllipsis;
		_v3Hint.Margin = new Thickness(10.0, 0.0, 8.0, 0.0);
		_v3Hint.SetResourceReference(TextBlock.ForegroundProperty, "B.InkFaint");
		// 完整操作说明(效果图第 10 条)挂在提示上,悬停可看全文
		_v3Hint.ToolTip = I18n.L("拖动块调整顺序；网格模式下拖块的右边缘改宽度；悬停能看到块名和隐藏 / 删除按钮。");
		_undoBtn = IconButton(LayoutIcons.Undo, I18n.L("撤销 (Ctrl+Z)"), delegate
		{
			UndoEdit();
		}, "Geo.Undo");
		_redoBtn = IconButton(LayoutIcons.Redo, I18n.L("重做 (Ctrl+Y)"), delegate
		{
			RedoEdit();
		}, "Geo.Redo");
		Border vs = new Border
		{
			Width = 1.0,
			Height = 18.0,
			Margin = new Thickness(4.0, 0.0, 4.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		vs.SetResourceReference(Border.BackgroundProperty, "B.Line");
		_v3HidBadge = Ui.Badge("0");
		_v3HidBadge.Visibility = Visibility.Collapsed;
		Button add = new Button
		{
			Style = Ui.S("Btn.Soft"),
			Content = Ui.H(6.0, Ui.Glyph(Ico.Add, 12.5), Ui.Txt(I18n.L("添加")).VCenter(), _v3HidBadge),
			VerticalAlignment = VerticalAlignment.Center
		};
		System.Windows.Automation.AutomationProperties.SetName(add, I18n.L("添加"));
		add.Click += delegate
		{
			ToggleV3Drawer();
		};
		_v3AddBtn = add;
		Button reset = Ui.Btn(I18n.L("重置本页"), BtnKind.Normal, delegate
		{
			CloseV3Drawer();
			ResetPage();
		});
		reset.VerticalAlignment = VerticalAlignment.Center;
		Button done = Ui.Btn(I18n.L("完成"), BtnKind.Primary, delegate
		{
			ExitEdit();
		});
		done.VerticalAlignment = VerticalAlignment.Center;
		Button more = IconButton(LayoutIcons.More, I18n.L("更多"), null);
		more.Click += delegate
		{
			OpenV3MoreMenu(more);
		};
		Grid bar = Ui.G(null, "Auto,Auto,*,Auto,Auto,Auto,Auto,Auto,Auto,Auto");
		bar.Add(_v3Title, 0, 0);
		bar.Add(seg, 0, 1);
		bar.Add(_v3Hint, 0, 2);
		bar.Add(_undoBtn, 0, 3);
		bar.Add(_redoBtn, 0, 4);
		bar.Add(vs, 0, 5);
		bar.Add(add.M(0.0, 0.0, 6.0, 0.0), 0, 6);
		bar.Add(reset.M(0.0, 0.0, 6.0, 0.0), 0, 7);
		bar.Add(done.M(0.0, 0.0, 4.0, 0.0), 0, 8);
		bar.Add(more, 0, 9);
		bar.MinHeight = 34.0;
		_barHost.CornerRadius = new CornerRadius(12.0);
		_barHost.Padding = new Thickness(14.0, 5.0, 8.0, 5.0);
		_barHost.Child = bar;
		BuildV3Drawer();
	}

	// 分段里的一项:图标 + 文字;点一下切模式
	private Border ModeItem(Border item, TextBlock text, string icon, string label, bool grid)
	{
		text.Text = label;
		text.FontSize = 12.5;
		text.VerticalAlignment = VerticalAlignment.Center;
		item.CornerRadius = new CornerRadius(6.0);
		item.Padding = new Thickness(10.0, 3.0, 10.0, 3.0);
		item.Cursor = Cursors.Hand;
		item.Background = Brushes.Transparent;
		item.Child = Ui.H(6.0, Lucide.Icon(icon, 13.0, "B.InkMuted").VCenter(), text);
		item.ToolTip = grid ? I18n.L("网格：一行可以并排放几块，拖右边缘改宽度") : I18n.L("列表：每块占满一整行，拖动调整上下顺序");
		System.Windows.Automation.AutomationProperties.SetName(item, label);
		item.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
		{
			SetV3Mode(grid);
			e.Handled = true;
		};
		return item;
	}

	private static Button IconButton(string path, string tip, RoutedEventHandler? click, string? geoKey = null)
	{
		Button button = new Button
		{
			Style = Ui.S("Btn.Icon"),
			Content = (geoKey != null) ? LayoutIcons.Glyph(geoKey, path, 15.0, "B.InkSoft") : Lucide.Icon(path, 15.0, "B.InkSoft"),
			ToolTip = tip,
			VerticalAlignment = VerticalAlignment.Center
		};
		System.Windows.Automation.AutomationProperties.SetName(button, tip);
		if (click != null)
		{
			button.Click += click;
		}
		return button;
	}

	// RefreshToolbar 的尾巴:模式分段 / 提示 / 撤销重做可用性 / 隐藏数
	private void RefreshV3Bar()
	{
		bool grid = IsBoxMode;
		PaintModeItem(_v3ModeList, _v3ModeListText, !grid);
		PaintModeItem(_v3ModeGrid, _v3ModeGridText, grid);
		_v3Hint.Text = grid ? I18n.L("拖动换位置 · 拖右边缘改宽度") : I18n.L("拖动调整上下顺序");
		if (_undoBtn != null)
		{
			_undoBtn.Opacity = _undoBtn.IsEnabled ? 1.0 : 0.38;
		}
		if (_redoBtn != null)
		{
			_redoBtn.Opacity = _redoBtn.IsEnabled ? 1.0 : 0.38;
		}
		int hidden = HiddenCount();
		if (_v3HidBadge != null)
		{
			_v3HidBadge.Visibility = (hidden > 0) ? Visibility.Visible : Visibility.Collapsed;
			if (_v3HidBadge.Child is TextBlock count)
			{
				count.Text = hidden.ToString();
			}
		}
		if (_v3AddBtn != null)
		{
			_v3AddBtn.ToolTip = (hidden > 0) ? I18n.L("已隐藏 {0} 项，在这里可以找回；也可以添加组件", hidden) : I18n.L("添加组件");
		}
	}

	private static void PaintModeItem(Border item, TextBlock text, bool on)
	{
		if (on)
		{
			item.SetResourceReference(Border.BackgroundProperty, "B.Paper");
			text.FontWeight = FontWeights.Bold;
			text.SetResourceReference(TextBlock.ForegroundProperty, "B.Ink");
		}
		else
		{
			item.Background = Brushes.Transparent;
			text.FontWeight = FontWeights.Normal;
			text.SetResourceReference(TextBlock.ForegroundProperty, "B.InkMuted");
		}
	}

	private int HiddenCount()
	{
		int n = 0;
		foreach (string id in _state.Order)
		{
			if (_state.Hidden.Contains(id))
			{
				n++;
			}
		}
		return n;
	}

	// ————— 列表 / 网格 —————

	/// <summary>编辑态切换「列表 / 网格」(可撤销、即时落盘;不再提供自由画布)。</summary>
	internal void SetV3Mode(bool grid)
	{
		if (!_edit || grid == IsBoxMode)
		{
			return;
		}
		if (_gesture.Length != 0)
		{
			EndBoxGesture(false, false);
		}
		CancelDrag(_dragCard, _dragSrc);
		_box.ClearDragVisuals();
		_session.PushUndo(_state.ToPage());
		if (grid)
		{
			// stack → 12 列网格:每块先占满一行,画面不跳;之后可以拖右边缘改宽度、并排
			PrepareEditableGrid();
			// 用户主动选的网格:完成编辑时不能被"没动过就还原成 stack"的逻辑还原回去
			_stackConverted = false;
			_stackBackup = null;
		}
		else
		{
			ConvertGridToStack();
		}
		_session.ClearSelection();
		_layoutTouched = true;
		Persist();
		Render();
		if (grid)
		{
			CompactState(null);
		}
		AppServices.Window?.Toast(grid ? I18n.L("已切换到网格") : I18n.L("已切换到列表"), grid ? I18n.L("一行可以并排放几块") : I18n.L("每块占满一整行"), ToastKind.Success);
	}

	// grid → stack:可见块按 (行, 列) 排成上下顺序,隐藏项留在原槽位;span 归 1、列数回默认 = 单列纵向流
	private void ConvertGridToStack()
	{
		PageLayoutV2 page = _state.ToPage().Clone();
		List<BlockLayout> blocks = page.Blocks ?? new List<BlockLayout>();
		List<int> slots = new List<int>();
		List<BlockLayout> visible = new List<BlockLayout>();
		for (int i = 0; i < blocks.Count; i++)
		{
			BlockLayout b = blocks[i];
			if (b != null && !b.Hidden)
			{
				slots.Add(i);
				visible.Add(b);
			}
		}
		List<BlockLayout> sorted = visible.OrderBy((BlockLayout b) => b.Y).ThenBy((BlockLayout b) => b.X).ToList();
		for (int k = 0; k < slots.Count; k++)
		{
			blocks[slots[k]] = sorted[k];
		}
		foreach (BlockLayout b in blocks)
		{
			if (b != null)
			{
				b.Span = 1;
			}
		}
		page.Blocks = blocks;
		page.Mode = LayoutSchema.ModeStack;
		page.Cols = LayoutSchema.ColsDefault;
		page.RowH = 0.0;
		_state = LayoutEngine.LoadState(_pageId, page, _refs);
		_session.Retain(_state.Order);
	}

	// ————— 进 / 出编辑态的收尾 —————

	private void OnV3EnterEdit()
	{
		_v3PendingType = "";
		CloseV3Drawer();
		ApplyV3BarSurface();
		// 上一场留下的选中态(列表模式的 Render 不会逐卡复位)
		foreach (LayoutCard card in _cards.Values)
		{
			card.SetSelected(false);
		}
		RefreshV3Bar();
	}

	private void OnV3ExitEdit()
	{
		_v3PendingType = "";
		CloseV3Drawer();
	}

	// ————— R35 win-int:跨包接线 / 深色表面 / 拖宽读数 / 新块定位 —————

	/// <summary>进 / 出编辑态(true = 进入)。启动页用它压暗英雄区并显示「主区域固定」(GOAL §5.5;R34 WP-L5 越界需求)。</summary>
	public event Action<bool>? EditingChanged;

	private void RaiseEditingChanged(bool editing)
	{
		try
		{
			EditingChanged?.Invoke(editing);
		}
		catch
		{
			// 订阅方出错不能把编辑态卡在半路
		}
	}

	/// <summary>深色表面(GOAL §6.4 第 2 条:只按 pageId == "launch" 判断):编辑控件用深色玻璃配色。</summary>
	internal bool IsDarkSurface
	{
		get
		{
			return string.Equals(_pageId, "launch", StringComparison.Ordinal);
		}
	}

	// 编辑条底色:普通页 = B.EditBarBg / B.EditBarLine(R34 WP-L0 令牌);启动页 = 深色玻璃
	private void ApplyV3BarSurface()
	{
		bool dark = IsDarkSurface;
		_barHost.SetResourceReference(Border.BackgroundProperty, dark ? "B.EditGlass" : "B.EditBarBg");
		_barHost.SetResourceReference(Border.BorderBrushProperty, dark ? "B.EditGlassLine" : "B.EditBarLine");
		_v3Title.SetResourceReference(TextBlock.ForegroundProperty, dark ? "B.OnEditGlass" : "B.Ink");
		_v3Hint.SetResourceReference(TextBlock.ForegroundProperty, dark ? "B.OnEditGlassMuted" : "B.InkFaint");
	}

	/// <summary>打开「添加」抽屉(DevShot --pop layout-drawer 的正式入口)。</summary>
	internal void OpenAddDrawer()
	{
		if (!_edit && CanEdit)
		{
			EnterEdit();
		}
		OpenV3Drawer();
	}

	/// <summary>模拟选中某块(DevShot --select 的正式入口):块不在本页返回 false。</summary>
	internal bool DevSelect(string blockId)
	{
		if (string.IsNullOrEmpty(blockId) || !_cards.ContainsKey(blockId))
		{
			return false;
		}
		if (!_edit && CanEdit)
		{
			EnterEdit();
		}
		_session.Select(blockId, false);
		UpdateSelectionVisual();
		return true;
	}

	// 拖右边缘时的「N / 列数」读数(GOAL §5.1;样式 Layout.EditTip 由 R34 WP-L0 提供,找不到就用代码兜底)
	private Border? _v3SpanTip;

	private TextBlock? _v3SpanTipText;

	private void ShowV3SpanTip(int span, int cols)
	{
		if (_gestureHandle.IndexOf('e') < 0 || cols <= 0)
		{
			return;
		}
		if (_v3SpanTip == null)
		{
			_v3SpanTipText = new TextBlock
			{
				FontSize = 12.0,
				FontWeight = FontWeights.SemiBold
			};
			_v3SpanTip = new Border
			{
				Child = _v3SpanTipText,
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Top,
				IsHitTestVisible = false
			};
			if (Application.Current?.TryFindResource("Layout.EditTip") is Style tipStyle && tipStyle.TargetType.IsAssignableFrom(typeof(Border)))
			{
				_v3SpanTip.Style = tipStyle;
			}
			else
			{
				_v3SpanTip.CornerRadius = new CornerRadius(8.0);
				_v3SpanTip.Padding = new Thickness(8.0, 2.0, 8.0, 2.0);
				_v3SpanTip.SetResourceReference(Border.BackgroundProperty, "B.Ink");
				_v3SpanTipText.SetResourceReference(TextBlock.ForegroundProperty, "B.Paper");
			}
			Panel.SetZIndex(_v3SpanTip, 2000);
		}
		if (_v3SpanTip.Parent == null)
		{
			_stage.Children.Add(_v3SpanTip);
		}
		_v3SpanTipText!.Text = I18n.L("{0} / {1} 列", span, cols);
		Point at = Mouse.GetPosition(_stage);
		_v3SpanTip.Margin = new Thickness(Math.Max(0.0, at.X + 14.0), Math.Max(0.0, at.Y - 30.0), 0.0, 0.0);
		_v3SpanTip.Visibility = Visibility.Visible;
	}

	private void HideV3SpanTip()
	{
		if (_v3SpanTip != null)
		{
			_v3SpanTip.Visibility = Visibility.Collapsed;
		}
	}

	// 新块(「显示」或「添加」之后):滚过去并闪一下主题色光晕(GOAL §5.2)
	private void RevealBlock(string blockId)
	{
		if (string.IsNullOrEmpty(blockId) || !_cards.TryGetValue(blockId, out LayoutCard? card) || card == null)
		{
			return;
		}
		try
		{
			card.Dispatcher.BeginInvoke(new Action(delegate
			{
				try
				{
					card.BringIntoView();
					card.FlashGlow();
				}
				catch
				{
				}
			}), System.Windows.Threading.DispatcherPriority.Loaded);
		}
		catch
		{
		}
	}

	// ————— 块名 / 删除 —————

	/// <summary>块名:自建组件显示组件自己的标题(不再显示 w_note1 这类内部 id)。</summary>
	internal string DisplayTitle(BlockSpec spec)
	{
		if (spec == null)
		{
			return "";
		}
		string? widgetId = LayoutEngine.WidgetIdOf(spec.Id);
		if (widgetId != null)
		{
			WidgetDef? def = WidgetRegistry.FindWidgetById(widgetId);
			if (def != null)
			{
				return WidgetUI.TitleOf(def);
			}
			// 定义找不到:不暴露 id,给类型无关的通用名
			return I18n.L("组件");
		}
		if (LayoutEngine.TryParsePluginId(spec.Id, out string plugin, out string _))
		{
			return (!string.IsNullOrEmpty(spec.Title) && spec.Title != spec.Id) ? spec.Title : plugin;
		}
		return string.IsNullOrEmpty(spec.Title) ? spec.Id : spec.Title;
	}

	/// <summary>垃圾桶:把自建组件 / 插件块从本页删掉(组件定义保留,可在「添加」里再加回来;Ctrl+Z 撤销)。</summary>
	internal void DeleteCustomBlock(BlockSpec spec)
	{
		if (!_edit || spec == null || !LayoutEngine.IsImplicitRef(spec.Id) || _gesture.Length != 0)
		{
			return;
		}
		PageLayoutV2 page = _state.ToPage().Clone();
		int removed = page.Blocks.RemoveAll((BlockLayout b) => b != null && string.Equals(b.Id, spec.Id, StringComparison.Ordinal));
		if (removed == 0)
		{
			return;
		}
		string title = DisplayTitle(spec);
		_session.PushUndo(_state.ToPage());
		_state = LayoutEngine.LoadState(_pageId, page, _refs);
		_session.Retain(_state.Order);
		_layoutTouched = true;
		Persist();
		Render();
		AppServices.Window?.Toast(I18n.L("已删除「{0}」", title), I18n.L("Ctrl+Z 撤销"), ToastKind.Success);
	}

	// ————— ⋯ 菜单 —————

	private void OpenV3MoreMenu(Button anchor)
	{
		ContextMenu menu = new ContextMenu
		{
			PlacementTarget = anchor,
			Placement = PlacementMode.Bottom
		};
		MenuItem export = new MenuItem
		{
			Header = I18n.L("导出本页…"),
			ToolTip = I18n.L("弹出保存框，导出这一页的排列")
		};
		export.Click += delegate
		{
			ExportThisPage();
		};
		MenuItem import = new MenuItem
		{
			Header = I18n.L("导入到本页…"),
			ToolTip = I18n.L("选择文件，替换这一页的排列")
		};
		import.Click += delegate
		{
			ImportIntoThisPage();
		};
		MenuItem whole = new MenuItem
		{
			Header = I18n.L("整套布局与方案…"),
			ToolTip = I18n.L("跳到 设置 → 布局")
		};
		whole.Click += delegate
		{
			GoLayoutSettings(false);
		};
		menu.Items.Add(export);
		menu.Items.Add(import);
		menu.Items.Add(new Separator());
		menu.Items.Add(whole);
		menu.IsOpen = true;
	}

	// 跳 设置 → 布局(advanced = 尽量展开「高级」):先完成编辑(落盘),再复用 LayoutShellHooks 的既有跳转
	private void GoLayoutSettings(bool advanced)
	{
		try
		{
			if (_edit)
			{
				ExitEdit();
			}
		}
		catch
		{
		}
		LayoutShellHooks.OpenLayoutSettings(advanced);
	}

	private static string FileNameOf(string pageId)
	{
		string name = string.IsNullOrEmpty(pageId) ? "page" : pageId;
		foreach (char c in Path.GetInvalidFileNameChars())
		{
			name = name.Replace(c, '_');
		}
		return name;
	}

	// 导出本页:只写这一页的顺序 / 显隐(与既有「导出布局」同一文件格式,可被任何版本的导入读懂)
	private void ExportThisPage()
	{
		try
		{
			string? path = Dlg.SaveFile(I18n.L("页面布局 (*.json)|*.json|全部文件|*.*"), FileNameOf(_pageId) + ".json", I18n.L("导出本页"));
			if (path == null)
			{
				return;
			}
			PageLayoutDoc page = PageLayoutDoc.Default(new List<string>(_state.Order), _state.VisibleIds());
			Dictionary<string, PageLayoutDoc> pages = new Dictionary<string, PageLayoutDoc>
			{
				[_pageId] = page
			};
			File.WriteAllText(path, PageLayoutStore.ExportJson(pages), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			AppServices.Window?.Toast(I18n.L("已导出本页"), path, ToastKind.Success);
		}
		catch (Exception ex)
		{
			AppServices.Window?.Toast(I18n.L("导出失败"), ex.Message, ToastKind.Error);
		}
	}

	// 导入到本页:文件里有本页就用本页那一份;只有一页就把它套到本页;其余页面一概不动(可撤销)
	private void ImportIntoThisPage()
	{
		IReadOnlyDictionary<string, PageLayoutDoc> pages;
		try
		{
			string? path = Dlg.PickFile(I18n.L("页面布局 (*.json)|*.json|全部文件|*.*"), I18n.L("导入到本页"));
			if (path == null)
			{
				return;
			}
			pages = PageLayoutStore.ParseImport(File.ReadAllText(path));
		}
		catch (Exception ex)
		{
			AppServices.Window?.Toast(I18n.L("导入失败"), ex.Message, ToastKind.Error);
			return;
		}
		PageLayoutDoc? picked = null;
		if (pages.TryGetValue(_pageId, out PageLayoutDoc? own))
		{
			picked = own;
		}
		else if (pages.Count == 1)
		{
			picked = pages.Values.First();
		}
		if (picked == null)
		{
			AppServices.Window?.Toast(I18n.L("导入失败"), I18n.L("文件里没有这一页的布局"), ToastKind.Warning);
			return;
		}
		PageLayoutV2 before = _state.ToPage();
		PageLayoutV2 merged = LayoutEngine.MergeV1(before.Clone(), picked);
		_session.PushUndo(before);
		_state = LayoutEngine.LoadState(_pageId, LayoutCanvasCompat.AsGrid(merged), _refs);
		_session.Retain(_state.Order);
		_session.ClearSelection();
		_layoutTouched = true;
		Persist();
		Render();
		if (_edit)
		{
			CompactState(null);
		}
		AppServices.Window?.Toast(I18n.L("已导入到本页"), I18n.L("Ctrl+Z 撤销"), ToastKind.Success);
	}

	// ————— 「添加」抽屉 —————

	private void BuildV3Drawer()
	{
		_v3DrawerTitle.FontSize = 14.0;
		_v3DrawerTitle.FontWeight = FontWeights.Bold;
		_v3DrawerTitle.VerticalAlignment = VerticalAlignment.Center;
		_v3DrawerTitle.TextTrimming = TextTrimming.CharacterEllipsis;
		_v3DrawerTitle.SetResourceReference(TextBlock.ForegroundProperty, "B.Ink");
		string label = PageLabelOf(_pageId);
		_v3DrawerTitle.Text = (label.Length > 0) ? I18n.L("添加到「{0}」页", label) : I18n.L("添加到本页");
		Button close = IconButton(LayoutIcons.Close, I18n.L("关闭 (Esc)"), delegate
		{
			CloseV3Drawer();
		});
		Grid head = Ui.G(null, "*,Auto");
		head.Add(_v3DrawerTitle, 0, 0);
		head.Add(close, 0, 1);
		head.Margin = new Thickness(16.0, 12.0, 10.0, 6.0);
		TextBlock note = AdvancedNote();
		Border noteBox = new Border
		{
			CornerRadius = new CornerRadius(12.0),
			Padding = new Thickness(12.0, 10.0, 12.0, 10.0),
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0),
			Child = note
		};
		noteBox.SetResourceReference(Border.BackgroundProperty, "B.Paper2");
		SPanel list = Ui.V(0.0, SectionLabel(I18n.L("已隐藏的内容")), _v3DrawerHidden, SectionLabel(I18n.L("组件")), _v3DrawerWidgets, noteBox);
		list.Margin = new Thickness(16.0, 0.0, 16.0, 16.0);
		SmoothScroll scroll = Ui.Scroll(list);
		Grid body = Ui.G("Auto,*");
		body.Add(head, 0);
		body.Add(scroll, 1);
		_v3Drawer.Child = body;
		_v3Drawer.Width = 316.0;
		_v3Drawer.HorizontalAlignment = HorizontalAlignment.Right;
		_v3Drawer.VerticalAlignment = VerticalAlignment.Stretch;
		_v3Drawer.BorderThickness = new Thickness(1.0, 0.0, 0.0, 0.0);
		_v3Drawer.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		_v3Drawer.SetResourceReference(Border.BorderBrushProperty, "B.Line");
		_v3Drawer.Visibility = Visibility.Collapsed;
		Motion.Shadow(_v3Drawer, 24.0, 0.14, 4.0);
	}

	private static TextBlock SectionLabel(string text)
	{
		TextBlock t = Ui.Txt(text, 12.0, bold: false, "B.InkFaint");
		t.Margin = new Thickness(0.0, 10.0, 0.0, 6.0);
		return t;
	}

	// 「网页、按钮组件默认关闭,可在 设置 → 布局 → 高级 里开启。」—— 中间那段是可点的链接
	private TextBlock AdvancedNote()
	{
		string full = I18n.L("网页、按钮组件默认关闭，可在 设置 → 布局 → 高级 里开启。");
		string link = I18n.L("设置 → 布局 → 高级");
		TextBlock note = new TextBlock
		{
			FontSize = 12.0,
			TextWrapping = TextWrapping.Wrap
		};
		note.SetResourceReference(TextBlock.ForegroundProperty, "B.InkMuted");
		Hyperlink hyperlink = new Hyperlink(new Run(link))
		{
			FontWeight = FontWeights.Bold,
			TextDecorations = null
		};
		hyperlink.SetResourceReference(TextElement.ForegroundProperty, "B.AccentText");
		hyperlink.Click += delegate
		{
			GoLayoutSettings(true);
		};
		int at = full.IndexOf(link, StringComparison.Ordinal);
		if (at >= 0)
		{
			if (at > 0)
			{
				note.Inlines.Add(new Run(full.Substring(0, at)));
			}
			note.Inlines.Add(hyperlink);
			string tail = full.Substring(at + link.Length);
			if (tail.Length > 0)
			{
				note.Inlines.Add(new Run(tail));
			}
		}
		else
		{
			note.Inlines.Add(new Run(full + " "));
			note.Inlines.Add(hyperlink);
		}
		return note;
	}

	private void ToggleV3Drawer()
	{
		if (_v3DrawerOpen)
		{
			CloseV3Drawer();
		}
		else
		{
			OpenV3Drawer();
		}
	}

	private void OpenV3Drawer()
	{
		if (!_edit)
		{
			return;
		}
		_v3DrawerOpen = true;
		if (_v3PrefsChanged == null)
		{
			_v3PrefsChanged = delegate
			{
				try
				{
					_v3Drawer.Dispatcher.BeginInvoke(new Action(RefreshV3Drawer));
				}
				catch
				{
				}
			};
			LayoutSettingsPrefs.Changed += _v3PrefsChanged;
		}
		RefreshV3Drawer();
		_v3Drawer.Visibility = Visibility.Visible;
	}

	/// <summary>关抽屉;原来是开着的返回 true(Esc 先关抽屉,再按一次才退出编辑)。</summary>
	private bool CloseV3Drawer()
	{
		bool was = _v3DrawerOpen;
		_v3DrawerOpen = false;
		if (_v3PrefsChanged != null)
		{
			LayoutSettingsPrefs.Changed -= _v3PrefsChanged;
			_v3PrefsChanged = null;
		}
		_v3PendingType = "";
		_v3Drawer.Visibility = Visibility.Collapsed;
		return was;
	}

	// RefreshTray 的新落点:编辑态每次重画都同步一次抽屉内容(抽屉关着时只算隐藏数)
	private void RefreshV3Drawer()
	{
		RefreshV3Bar();
		if (!_v3DrawerOpen)
		{
			return;
		}
		_v3DrawerHidden.Children.Clear();
		foreach (string id in _state.Order)
		{
			if (!_state.Hidden.Contains(id))
			{
				continue;
			}
			BlockSpec spec = SpecOf(id);
			string kind = LayoutEngine.IsImplicitRef(id) ? I18n.L("组件") : I18n.L("页面内容");
			Button show = Ui.Btn(I18n.L("显示"), BtnKind.Soft, delegate
			{
				string title = DisplayTitle(spec);
				Restore(spec);
				AppServices.Window?.Toast(I18n.L("已恢复「{0}」", title), "", ToastKind.Success);
			});
			_v3DrawerHidden.Children.Add(DrawerItem(LayoutIcons.EyeOff, DisplayTitle(spec), kind, show, null));
		}
		if (_v3DrawerHidden.Children.Count == 0)
		{
			Border empty = new Border
			{
				CornerRadius = new CornerRadius(12.0),
				Padding = new Thickness(10.0),
				BorderThickness = new Thickness(1.0),
				Child = Ui.Txt(I18n.L("没有隐藏的内容"), 12.0, bold: false, "B.InkFaint").Center()
			};
			empty.SetResourceReference(Border.BorderBrushProperty, "B.Line");
			_v3DrawerHidden.Children.Add(empty);
		}
		_v3DrawerWidgets.Children.Clear();
		// 本页已经放了哪些类型(时钟 / 快捷启动 / 游戏时长 一页一个;便签 / 分隔线可以多张)
		HashSet<string> usedTypes = new HashSet<string>(StringComparer.Ordinal);
		Dictionary<string, WidgetDef> defs = new Dictionary<string, WidgetDef>(StringComparer.Ordinal);
		foreach (WidgetDef def in WidgetRegistry.AllWidgets())
		{
			if (def != null && !string.IsNullOrEmpty(def.Id))
			{
				defs[def.Id] = def;
			}
		}
		foreach (string id in _state.Order)
		{
			string? wid = LayoutEngine.WidgetIdOf(id);
			if (wid != null && !_state.Hidden.Contains(id) && defs.TryGetValue(wid, out WidgetDef? used) && used.Type != null)
			{
				usedTypes.Add(used.Type);
			}
		}
		foreach (string type in WidgetRegistry.SafeTypes)
		{
			bool multi = type == LayoutSchema.WidgetNote || type == LayoutSchema.WidgetDivider;
			bool taken = !multi && usedTypes.Contains(type);
			string capturedType = type;
			Button add = Ui.Btn(taken ? I18n.L("已添加") : I18n.L("添加"), taken ? BtnKind.Ghost : BtnKind.Soft, delegate
			{
				OnAddWidgetType(capturedType);
			});
			add.IsEnabled = !taken;
			_v3DrawerWidgets.Children.Add(DrawerItem(LayoutIcons.WidgetIcon(type), WidgetUI.TypeLabel(type), WidgetDescription(type), add, (_v3PendingType == type) ? PendingForm(type) : null));
		}
		// 网页 / 按钮:只有在 设置 → 布局 → 高级 打开「网页和按钮组件」后才出现(可以加多个)
		if (LayoutSettingsPrefs.DangerWidgetsEnabled)
		{
			foreach (string type in new string[2] { LayoutSchema.WidgetWeb, LayoutSchema.WidgetButton })
			{
				string capturedType = type;
				Button add = Ui.Btn(I18n.L("添加"), BtnKind.Soft, delegate
				{
					OnAddWidgetType(capturedType);
				});
				_v3DrawerWidgets.Children.Add(DrawerItem(LayoutIcons.WidgetIcon(type), WidgetUI.TypeLabel(type), WidgetDescription(type), add, (_v3PendingType == type) ? PendingForm(type) : null));
			}
		}
		// 以前建过、本页还没放的组件:直接加回来
		foreach (WidgetDef widget in AvailableWidgets())
		{
			string wid = widget.Id;
			Button add = Ui.Btn(I18n.L("添加"), BtnKind.Soft, delegate
			{
				AddCustomBlock(wid);
			});
			_v3DrawerWidgets.Children.Add(DrawerItem(LayoutIcons.WidgetIcon(widget.Type ?? ""), WidgetUI.TitleOf(widget), I18n.L("你建过的{0}", WidgetUI.TypeLabel(widget.Type ?? "")), add, null));
		}
	}

	private static string WidgetDescription(string type)
	{
		switch (type)
		{
		case LayoutSchema.WidgetNote:
			return I18n.L("记一句话，可以加多张");
		case LayoutSchema.WidgetClock:
			return I18n.L("当前时间和日期");
		case LayoutSchema.WidgetShortcut:
			return I18n.L("一键启动指定实例");
		case LayoutSchema.WidgetPlaytime:
			return I18n.L("本周每天玩了多久");
		case LayoutSchema.WidgetDivider:
			return I18n.L("把页面分成两段");
		case LayoutSchema.WidgetWeb:
			return I18n.L("嵌入一个网页（只接受 https）");
		case LayoutSchema.WidgetButton:
			return I18n.L("点一下执行一个动作");
		default:
			return "";
		}
	}

	private static Border DrawerItem(string icon, string title, string desc, Button action, UIElement? extra)
	{
		Border iconBox = new Border
		{
			Width = 40.0,
			Height = 40.0,
			CornerRadius = new CornerRadius(8.0),
			Child = Lucide.Icon(icon, 18.0, "B.InkSoft").Center().VCenter()
		};
		iconBox.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		TextBlock name = Ui.Txt(title, 13.0, bold: true).Trim();
		TextBlock sub = Ui.Txt(desc, 12.0, bold: false, "B.InkMuted").Trim();
		SPanel text = Ui.V(2.0, name, (desc.Length > 0) ? sub : null);
		text.VerticalAlignment = VerticalAlignment.Center;
		text.Margin = new Thickness(10.0, 0.0, 10.0, 0.0);
		action.VerticalAlignment = VerticalAlignment.Center;
		Grid row = Ui.G("Auto,Auto", "40,*,Auto");
		row.Add(iconBox, 0, 0);
		row.Add(text, 0, 1);
		row.Add(action, 0, 2);
		if (extra != null)
		{
			row.Add(extra, 1, 0, 1, 3);
		}
		Border box = new Border
		{
			CornerRadius = new CornerRadius(12.0),
			Padding = new Thickness(8.0),
			Child = row
		};
		box.SetResourceReference(Border.BackgroundProperty, "B.Paper2");
		return box;
	}

	// 便签要内容、快捷启动要实例名:其余类型点了直接加
	private void OnAddWidgetType(string type)
	{
		if (!_edit)
		{
			return;
		}
		if (WidgetLibrary.ParameterKey(type) == "text" || WidgetLibrary.ParameterKey(type) == "instance" || LayoutSchema.IsDangerousWidgetType(type))
		{
			_v3PendingType = (_v3PendingType == type) ? "" : type;
			RefreshV3Drawer();
			return;
		}
		CreateWidgetAndAdd(type, "");
	}

	private UIElement PendingForm(string type)
	{
		string placeholder;
		if (type == LayoutSchema.WidgetWeb)
		{
			placeholder = I18n.L("网页地址，以 https:// 开头");
		}
		else if (type == LayoutSchema.WidgetButton)
		{
			placeholder = I18n.L("动作，例如 page:launch");
		}
		else
		{
			placeholder = (WidgetLibrary.ParameterKey(type) == "text") ? I18n.L("便签内容") : I18n.L("实例名");
		}
		TextBox input = Ui.Input(placeholder);
		Button ok = Ui.Btn(I18n.L("确定"), BtnKind.Primary, null);
		Button cancel = Ui.Btn(I18n.L("取消"), BtnKind.Ghost, delegate
		{
			_v3PendingType = "";
			RefreshV3Drawer();
		});
		ok.Click += delegate
		{
			CreateWidgetAndAdd(type, input.Text ?? "");
		};
		input.KeyDown += delegate(object _, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)
			{
				CreateWidgetAndAdd(type, input.Text ?? "");
				e.Handled = true;
			}
		};
		Grid form = Ui.G(null, "*,Auto,Auto");
		form.Add(input, 0, 0);
		form.Add(ok.M(6.0, 0.0, 0.0, 0.0), 0, 1);
		form.Add(cancel.M(4.0, 0.0, 0.0, 0.0), 0, 2);
		form.Margin = new Thickness(0.0, 8.0, 0.0, 0.0);
		input.Loaded += delegate
		{
			try
			{
				input.Focus();
			}
			catch
			{
			}
		};
		return form;
	}

	// 新建组件定义(走 WidgetLibrary 的校验)→ 写进文档 → 追加到本页末尾(可撤销)
	private void CreateWidgetAndAdd(string type, string parameter)
	{
		if (!_edit)
		{
			return;
		}
		LayoutV2 doc = LayoutStoreV2.Load();
		string error;
		WidgetDef? widget = LayoutSchema.IsDangerousWidgetType(type) ? CreateDangerous(doc, type, parameter, out error) : WidgetLibrary.CreateFor(doc, type, WidgetUI.TypeLabel(type), parameter, "", out error);
		if (widget == null)
		{
			AppServices.Window?.Toast(I18n.L("没能添加组件"), error, ToastKind.Warning);
			return;
		}
		if (doc.Layout.Widgets == null)
		{
			doc.Layout.Widgets = new List<WidgetDef>();
		}
		doc.Layout.Widgets.Add(widget);
		bool saved;
		_saving = true;
		try
		{
			saved = LayoutStoreV2.Save(doc);
		}
		finally
		{
			_saving = false;
		}
		if (!saved)
		{
			AppServices.Window?.Toast(I18n.L("没能添加组件"), I18n.L("布局未能保存"), ToastKind.Warning);
			return;
		}
		_v3PendingType = "";
		AddCustomBlock(widget.Id);
	}

	// 网页 / 按钮(WidgetLibrary 只建 5 种安全类型):地址 / 动作先过 WidgetCommon 的同一套准入;
	// 用户在打开全局开关后亲手加的这一块直接 enabled = true(导入的布局里带的仍然默认关闭)。
	private static WidgetDef? CreateDangerous(LayoutV2 doc, string type, string parameter, out string error)
	{
		error = "";
		Dictionary<string, object?> settings = new Dictionary<string, object?>(StringComparer.Ordinal);
		if (type == LayoutSchema.WidgetWeb)
		{
			if (!WidgetCommon.TryAcceptWebUrl(parameter, out string url, out string reason))
			{
				error = reason;
				return null;
			}
			settings["url"] = url;
		}
		else
		{
			string action = (parameter ?? "").Trim();
			if (!WidgetCommon.TryParseAction(action, out string _, out string _, out string _, out string reason))
			{
				error = reason;
				return null;
			}
			settings["action"] = action;
		}
		settings[WidgetCommon.EnabledKey] = true;
		HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
		if (doc.Layout.Widgets != null)
		{
			foreach (WidgetDef existing in doc.Layout.Widgets)
			{
				if (existing != null && !string.IsNullOrEmpty(existing.Id))
				{
					used.Add(existing.Id);
				}
			}
		}
		string id = LayoutSchema.NewWidgetId();
		int suffix = 2;
		string candidate = id;
		while (used.Contains(candidate))
		{
			candidate = id + "-" + suffix;
			suffix++;
		}
		WidgetDef widget = new WidgetDef
		{
			Id = candidate,
			Type = type,
			Title = WidgetUI.TypeLabel(type)
		};
		foreach (KeyValuePair<string, object?> kv in settings)
		{
			widget.Settings[kv.Key] = kv.Value;
		}
		return widget;
	}
}

// 区块卡的 v3 编辑态外观:灰色虚线框(选中才用主题色)、悬停才出现的块名 / 眼睛或垃圾桶、网格模式右边缘拖宽手柄。
internal sealed partial class LayoutCard
{
	private readonly System.Windows.Shapes.Rectangle _v3Outline = new System.Windows.Shapes.Rectangle();

	private readonly Border _v3Label = new Border();

	private readonly TextBlock _v3LabelText = new TextBlock();

	private readonly Border _v3Acts = new Border();

	private readonly Border _v3Grip = new Border();

	// 下边缘把手(GOAL Q3:网格保留右边缘 + 下边缘,悬停或选中才出现)
	private readonly Border _v3GripS = new Border();

	private bool _v3Hover;

	private static readonly DoubleCollection DashPattern = CreateDash();

	private static DoubleCollection CreateDash()
	{
		DoubleCollection dash = new DoubleCollection { 4.0, 3.0 };
		dash.Freeze();
		return dash;
	}

	private void BuildV3Chrome(Grid grid)
	{
		// 1) 虚线外框:比卡片向外让 4px(对齐效果图 outline-offset),不吃鼠标
		_v3Outline.IsHitTestVisible = false;
		_v3Outline.RadiusX = 12.0;
		_v3Outline.RadiusY = 12.0;
		_v3Outline.Margin = new Thickness(-4.0);
		_v3Outline.StrokeThickness = 1.5;
		_v3Outline.Fill = null;
		_v3Outline.Visibility = Visibility.Collapsed;
		grid.Add(_v3Outline, 0, 0, 3, 1);
		// 2) 块名(悬停 / 选中才出现):⋮⋮ + 标题(+「· 组件」);按住它也能拖(事件冒泡到卡片)
		_v3LabelText.FontSize = 12.0;
		_v3LabelText.FontWeight = FontWeights.Bold;
		_v3LabelText.VerticalAlignment = VerticalAlignment.Center;
		_v3LabelText.TextTrimming = TextTrimming.CharacterEllipsis;
		_v3LabelText.MaxWidth = 260.0;
		_v3LabelText.SetResourceReference(TextBlock.ForegroundProperty, "B.Ink");
		SPanel labelRow = Ui.H(6.0, LayoutIcons.Glyph("Geo.Grip", LayoutIcons.Grip, 12.0, "B.InkFaint").VCenter(), _v3LabelText);
		if (LayoutEngine.IsImplicitRef(Spec.Id))
		{
			labelRow.Children.Add(Ui.Txt(I18n.L("· 组件"), 12.0, bold: false, "B.InkFaint").VCenter());
		}
		_v3Label.Child = labelRow;
		_v3Label.Height = 24.0;
		_v3Label.Padding = new Thickness(6.0, 0.0, 10.0, 0.0);
		_v3Label.CornerRadius = new CornerRadius(8.0);
		_v3Label.BorderThickness = new Thickness(1.0);
		_v3Label.HorizontalAlignment = HorizontalAlignment.Left;
		_v3Label.VerticalAlignment = VerticalAlignment.Top;
		_v3Label.Margin = new Thickness(8.0, -14.0, 0.0, 0.0);
		_v3Label.SetResourceReference(Border.BackgroundProperty, _host.IsDarkSurface ? "B.EditGlassChip" : "B.EditBarBg");
		_v3Label.SetResourceReference(Border.BorderBrushProperty, _host.IsDarkSurface ? "B.EditGlassChipLine" : "B.EditBarLine");
		if (_host.IsDarkSurface)
		{
			_v3LabelText.SetResourceReference(TextBlock.ForegroundProperty, "B.OnEditGlass");
		}
		_v3Label.Visibility = Visibility.Collapsed;
		Panel.SetZIndex(_v3Label, 50);
		grid.Add(_v3Label, 0, 0, 3, 1);
		// 3) 右上角动作:内置内容 = 眼睛(隐藏,可在「添加」里找回);自建组件 / 插件块 = 垃圾桶(删除,Ctrl+Z 撤销)
		bool custom = LayoutEngine.IsImplicitRef(Spec.Id);
		Button act = new Button
		{
			Style = Ui.S("Btn.Icon"),
			Content = custom ? (UIElement)Lucide.Icon(LayoutIcons.Trash, 14.0, "B.Danger") : LayoutIcons.Glyph("Geo.EyeOff", LayoutIcons.EyeOff, 14.0, "B.InkSoft"),
			ToolTip = custom ? I18n.L("删除这个组件") : I18n.L("隐藏（可在「添加」里找回）"),
			Focusable = false
		};
		System.Windows.Automation.AutomationProperties.SetName(act, custom ? I18n.L("删除这个组件") : I18n.L("隐藏"));
		act.Click += delegate
		{
			if (custom)
			{
				_host.DeleteCustomBlock(Spec);
			}
			else
			{
				_host.HideBlock(Spec);
			}
		};
		_v3Acts.Child = act;
		_v3Acts.CornerRadius = new CornerRadius(8.0);
		_v3Acts.BorderThickness = new Thickness(1.0);
		_v3Acts.HorizontalAlignment = HorizontalAlignment.Right;
		_v3Acts.VerticalAlignment = VerticalAlignment.Top;
		_v3Acts.Margin = new Thickness(0.0, -14.0, 8.0, 0.0);
		_v3Acts.SetResourceReference(Border.BackgroundProperty, _host.IsDarkSurface ? "B.EditGlassChip" : "B.EditBarBg");
		_v3Acts.SetResourceReference(Border.BorderBrushProperty, _host.IsDarkSurface ? "B.EditGlassChipLine" : "B.EditBarLine");
		_v3Acts.Visibility = Visibility.Collapsed;
		Panel.SetZIndex(_v3Acts, 50);
		grid.Add(_v3Acts, 0, 0, 3, 1);
		// 4) 网格模式右边缘的拖宽手柄(宽度只按整数列吸附;拖动走既有的 e 向缩放手势)
		Border bar = new Border
		{
			Width = 2.0,
			Height = 16.0,
			CornerRadius = new CornerRadius(1.0),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			IsHitTestVisible = false
		};
		bar.SetResourceReference(Border.BackgroundProperty, "B.InkFaint");
		_v3Grip.Child = bar;
		_v3Grip.Width = 10.0;
		_v3Grip.Height = 36.0;
		_v3Grip.CornerRadius = new CornerRadius(5.0);
		_v3Grip.BorderThickness = new Thickness(1.0);
		_v3Grip.HorizontalAlignment = HorizontalAlignment.Right;
		_v3Grip.VerticalAlignment = VerticalAlignment.Center;
		_v3Grip.Margin = new Thickness(0.0, 0.0, -9.0, 0.0);
		_v3Grip.Cursor = Cursors.SizeWE;
		_v3Grip.ToolTip = I18n.L("拖动改宽度");
		_v3Grip.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		_v3Grip.SetResourceReference(Border.BorderBrushProperty, "B.Line");
		_v3Grip.Visibility = Visibility.Collapsed;
		_v3Grip.PreviewMouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e)
		{
			_host.BeginBoxResize(this, "e", e);
		};
		Panel.SetZIndex(_v3Grip, 60);
		grid.Add(_v3Grip, 0, 0, 3, 1);
		// 5) 下边缘把手:拖动改高度(像素高度,4px 吸附)· 双击恢复自动高度(走既有 s 向手势 / ClearFixedHeight)
		Border barS = new Border
		{
			Width = 16.0,
			Height = 2.0,
			CornerRadius = new CornerRadius(1.0),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			IsHitTestVisible = false
		};
		barS.SetResourceReference(Border.BackgroundProperty, "B.InkFaint");
		_v3GripS.Child = barS;
		_v3GripS.Width = 36.0;
		_v3GripS.Height = 10.0;
		_v3GripS.CornerRadius = new CornerRadius(5.0);
		_v3GripS.BorderThickness = new Thickness(1.0);
		_v3GripS.HorizontalAlignment = HorizontalAlignment.Center;
		_v3GripS.VerticalAlignment = VerticalAlignment.Bottom;
		_v3GripS.Margin = new Thickness(0.0, 0.0, 0.0, -9.0);
		_v3GripS.Cursor = Cursors.SizeNS;
		_v3GripS.ToolTip = I18n.L("拖动改高度 · 双击恢复自动高度");
		_v3GripS.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		_v3GripS.SetResourceReference(Border.BorderBrushProperty, "B.Line");
		_v3GripS.Visibility = Visibility.Collapsed;
		_v3GripS.PreviewMouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e)
		{
			if (e.ClickCount >= 2)
			{
				_host.ClearFixedHeight(Spec);
				e.Handled = true;
				return;
			}
			_host.BeginBoxResize(this, "s", e);
		};
		Panel.SetZIndex(_v3GripS, 60);
		grid.Add(_v3GripS, 0, 0, 3, 1);
		MouseEnter += delegate
		{
			_v3Hover = true;
			ApplyV3Chrome();
		};
		MouseLeave += delegate
		{
			_v3Hover = false;
			ApplyV3Chrome();
		};
	}

	// 块名每次进编辑态都重取一次(自建组件改过标题、定义晚于卡片出现时都能对上)
	private void RefreshV3Label()
	{
		_v3LabelText.Text = _host.DisplayTitle(Spec);
	}

	private void ApplyV3Chrome()
	{
		if (!_edit)
		{
			_v3Outline.Visibility = Visibility.Collapsed;
			_v3Label.Visibility = Visibility.Collapsed;
			_v3Acts.Visibility = Visibility.Collapsed;
			_v3Grip.Visibility = Visibility.Collapsed;
			return;
		}
		_v3Outline.Visibility = Visibility.Visible;
		if (_selected)
		{
			_v3Outline.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "B.Accent");
			_v3Outline.StrokeDashArray = new DoubleCollection();
			_v3Outline.StrokeThickness = 2.0;
		}
		else
		{
			bool dark = _host.IsDarkSurface;
			string key = _v3Hover ? (dark ? "B.EditOutlineHoverOnDark" : "B.EditOutlineHover") : (dark ? "B.EditOutlineOnDark" : "B.EditOutline");
			_v3Outline.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, key);
			_v3Outline.StrokeDashArray = DashPattern;
			_v3Outline.StrokeThickness = 1.5;
		}
		bool chrome = _v3Hover || _selected;
		_v3Label.Visibility = chrome ? Visibility.Visible : Visibility.Collapsed;
		_v3Acts.Visibility = chrome ? Visibility.Visible : Visibility.Collapsed;
		_v3Grip.Visibility = (chrome && _boxMode == LayoutSchema.ModeGrid) ? Visibility.Visible : Visibility.Collapsed;
		_v3GripS.Visibility = (chrome && _boxMode == LayoutSchema.ModeGrid) ? Visibility.Visible : Visibility.Collapsed;
	}

	/// <summary>新块落位后闪一下主题色光晕(约 0.9 秒淡出;关闭动效时只做滚动不闪)。</summary>
	internal void FlashGlow()
	{
		if (!Motion.Enabled)
		{
			return;
		}
		Color accent = LayoutEditor.AccentColor();
		System.Windows.Media.Effects.DropShadowEffect glow = new System.Windows.Media.Effects.DropShadowEffect
		{
			Color = accent,
			BlurRadius = 22.0,
			ShadowDepth = 0.0,
			Opacity = 0.0
		};
		System.Windows.Media.Effects.Effect? before = Effect;
		Effect = glow;
		System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames anim = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames();
		anim.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0.85, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));
		anim.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0.0, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))));
		anim.Completed += delegate
		{
			if (ReferenceEquals(Effect, glow))
			{
				Effect = before;
			}
		};
		glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, anim);
	}
}

/// <summary>编辑态用到的线条图标(Lucide 风格,24 视框;用 <see cref="Lucide.Icon"/> 渲染,不依赖图标字体里有没有这个码位)。</summary>
internal static class LayoutIcons
{
	public const string Undo = "M9 14L4 9l5-5 M4 9h10.5a5.5 5.5 0 0 1 5.5 5.5a5.5 5.5 0 0 1-5.5 5.5H11";

	public const string Redo = "M15 14l5-5-5-5 M20 9H9.5A5.5 5.5 0 0 0 4 14.5A5.5 5.5 0 0 0 9.5 20H13";

	public const string More = "M5 10.5a1.5 1.5 0 1 0 0 3a1.5 1.5 0 1 0 0-3z M12 10.5a1.5 1.5 0 1 0 0 3a1.5 1.5 0 1 0 0-3z M19 10.5a1.5 1.5 0 1 0 0 3a1.5 1.5 0 1 0 0-3z";

	public const string Close = "M18 6L6 18 M6 6l12 12";

	public const string EyeOff = "M9.88 9.88a3 3 0 1 0 4.24 4.24 M10.73 5.08A10.43 10.43 0 0 1 12 5c7 0 10 7 10 7a13.16 13.16 0 0 1-1.67 2.68 M6.61 6.61A13.53 13.53 0 0 0 2 12s3 7 10 7a9.74 9.74 0 0 0 5.39-1.61 M2 2l20 20";

	public const string Trash = "M3 6h18 M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2 M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6";

	public const string Grip = "M9 5h.01 M15 5h.01 M9 12h.01 M15 12h.01 M9 19h.01 M15 19h.01";

	public const string ModeList = "M4 4h16v6H4z M4 14h16v6H4z";

	public const string ModeGrid = "M4 4h16v6H4z M4 14h9v6H4z M16 14h4v6h-4z";

	public const string Note = "M15.5 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V8.5z M15 3v6h6";

	public const string Clock = "M12 2a10 10 0 1 0 0 20a10 10 0 1 0 0-20z M12 6v6l4 2";

	public const string Play = "M6 3l14 9-14 9z";

	public const string Chart = "M3 3v18h18 M18 17V9 M13 17V5 M8 17v-3";

	public const string Line = "M5 12h14";

	public const string Block = "M4 4h16v16H4z";

	/// <summary>
	/// 优先用 R34 WP-L0 的矢量图标资源(<c>Geo.*</c>,16 视框描边);资源找不到时退回本类的 Lucide 路径(24 视框)。
	/// 用 TryFindResource,找不到不抛异常(GOAL §6.4 第 1 条)。
	/// </summary>
	public static System.Windows.Shapes.Path Glyph(string geoKey, string lucideFallback, double size, string brushKey)
	{
		if (Application.Current?.TryFindResource(geoKey) is Geometry geo)
		{
			double k = size / 16.0;
			System.Windows.Shapes.Path path = new System.Windows.Shapes.Path
			{
				Data = geo,
				Width = 16.0,
				Height = 16.0,
				Stretch = Stretch.None,
				StrokeThickness = 1.6,
				StrokeStartLineCap = PenLineCap.Round,
				StrokeEndLineCap = PenLineCap.Round,
				StrokeLineJoin = PenLineJoin.Round,
				LayoutTransform = new ScaleTransform(k, k),
				SnapsToDevicePixels = false,
				IsHitTestVisible = false
			};
			path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, brushKey);
			return path;
		}
		return Lucide.Icon(lucideFallback, size, brushKey);
	}

	public static string WidgetIcon(string type)
	{
		switch (type)
		{
		case LayoutSchema.WidgetNote:
			return Note;
		case LayoutSchema.WidgetClock:
			return Clock;
		case LayoutSchema.WidgetShortcut:
			return Play;
		case LayoutSchema.WidgetPlaytime:
			return Chart;
		case LayoutSchema.WidgetDivider:
			return Line;
		default:
			return Block;
		}
	}
}

/// <summary>
/// v3:界面只剩「列表 / 网格」。存档里 mode=canvas 的页面读取时按网格呈现 —— R35 合并后只保留一份推导实现:
/// R34 WP-L3 的 <see cref="LayoutCanvasMigration.ToGridPage"/>(先 y 后 x、span = clamp(round(w×cols), max(2, MinCols), cols))。
/// 本类只是给旧调用点留的薄壳。
/// </summary>
public static class LayoutCanvasCompat
{
	/// <summary>canvas 页 → 等价的 grid 页(返回副本);其它模式原样返回同一个对象。</summary>
	public static PageLayoutV2? AsGrid(PageLayoutV2? page)
	{
		return LayoutCanvasMigration.IsCanvas(page) ? LayoutCanvasMigration.ToGridPage(page) : page;
	}
}
