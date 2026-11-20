// v3(自定义布局 v3 效果图 · 编辑态,Linux 对等落地,代理 C):LayoutEditor 的 v3 编辑外壳部分。
// 拆成 partial 是为了不整文件重写 110KB 的 LayoutEditor.cs —— 主文件只改了几处挂接点,
// 新增的 UI / 动作都在这里:
//   1) 顶部单条编辑条:标题「编辑「<页名>」页」· 列表|网格分段 · 撤销/重做图标 · 添加(数字=隐藏了几项)·
//      重置本页 · 完成 · ⋯(导出本页… / 导入到本页… / 整套布局与方案…)。[R35] 「自动靠拢」从 ⋯ 菜单里也去掉(GOAL §5.2);
//   2) 「添加」抽屉(替代原来底部一整条「已隐藏的区块」托盘):已隐藏的内容 + 组件 + 去高级设置的提示;
//   3) 列表 / 网格两种排法(不再提供自由画布):Linux 的 stack 页进编辑态仍临时折成 12 列网格
//      (docs/README.md「Linux 编辑态与 Windows 的四处差异」#1,刻意保留),「列表」= 每块占满一整行的网格;
//   4) 自建组件用垃圾桶删除(可撤销),内置内容用眼睛隐藏;
//   5) 单块页面不提供编辑入口(CanEdit / LayoutShellHooks.CanEditPage)。
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TipCL.App.Common;
using TipCL.App.Ui;
using TipCL.Core.Launcher;
using TipCL.Core.Layout;
using TipCL.Core.Launcher.Layout;
using ToastKind = TipCL.Core.Layout.ToastKind;

namespace TipCL.App.Pages.Layout;

public sealed partial class LayoutEditor
{
	// ————————————— 单块页面判定(§1.A.8) —————————————

	// 每个页面最近一次装载时的块数(含隐藏块、自建块),供 LayoutShellHooks.CanEditPage 在页面还没上屏时回答。
	private static readonly Dictionary<string, int> _knownCounts = new Dictionary<string, int>(StringComparer.Ordinal);

	private static readonly object _knownGate = new object();

	/// <summary>[v3] 某页最近一次装载时有几块(没装载过返回 null)。</summary>
	internal static int? KnownBlockCount(string? pageId)
	{
		if (string.IsNullOrEmpty(pageId))
		{
			return null;
		}
		lock (_knownGate)
		{
			return _knownCounts.TryGetValue(pageId, out int count) ? count : null;
		}
	}

	private void RememberBlockCount()
	{
		if (string.IsNullOrEmpty(_pageId))
		{
			return;
		}
		// R35 lin-int(L12←L5):与 BlockHostRules 同口径 —— 只数页面交给宿主的内置块(含默认隐藏),
		// 不数文档里的自建 / 插件块(否则 1 个内置块 + 1 个导入组件的页会被当成可编辑,而 BlockHost.EnterEdit 拒绝)
		int count = _specs.Count;
		lock (_knownGate)
		{
			_knownCounts[_pageId] = count;
		}
	}

	// 入口清单里标了「单块」的页(下载与网络 / 反馈与关于),或者实际只有一块的页
	private bool IsSingleBlockPage()
	{
		LayoutBlockEntryCatalog.Entry? entry = LayoutBlockEntryCatalog.Find(_pageId);
		if (entry != null && entry.SingleBlock)
		{
			return true;
		}
		// R35 lin-int(L12←L5):计数口径 = BlockHostRules.OffersLayoutMenu(内置块含默认隐藏 ≥ 2)
		return !BlockHostRules.OffersLayoutMenu(_specs.Count);
	}

	// ————————————— 顶部编辑条(§1.A.3) —————————————

	private TextBlock? _titleText;

	private TextBlock? _hintText;

	private Button? _segList;

	private Button? _segGrid;

	private Button? _addBtn;

	private Border? _addBadge;

	private TextBlock? _addBadgeText;

	private Button? _moreBtn;

	// 当前排法:true = 列表(每块占满一整行),false = 网格
	private bool _listKind;

	// 切到「列表」前每块的列位 / 列宽,切回「网格」时还给它(只在本次编辑会话里有效)
	private readonly Dictionary<string, int[]> _gridWidths = new Dictionary<string, int[]>(StringComparer.Ordinal);

	private static string HintFor(bool list)
	{
		return list ? I18nCore.Tr("拖动调整上下顺序") : I18nCore.Tr("拖动换位置 · 拖右边缘改宽度");
	}

	/// <summary>§1.A.10 的完整提示文案(编辑条提示的悬停全文 / 进入编辑时的 Toast 正文)。</summary>
	internal static string FullHint()
	{
		return I18nCore.Tr("拖动块调整顺序；网格模式下拖块的右边缘改宽度；悬停能看到块名和隐藏 / 删除按钮。");
	}

	// 一条横排:标题 · 列表|网格 · 提示(可截断)· 撤销 · 重做 | 添加 · 重置本页 · 完成 · ⋯
	private Control BuildEditBar()
	{
		_titleText = U.Txt(LayoutEditChrome.EditTitle(_pageId), 13.0, bold: true);
		_titleText.TextTrimming = TextTrimming.CharacterEllipsis;
		_titleText.MaxWidth = 240.0;
		_titleText.VerticalAlignment = VerticalAlignment.Center;
		_segList = SegButton(I18nCore.Tr("列表"), LayoutEditChrome.ListModeData, delegate
		{
			SetLayoutKind(list: true);
		});
		_segGrid = SegButton(I18nCore.Tr("网格"), LayoutEditChrome.GridModeData, delegate
		{
			SetLayoutKind(list: false);
		});
		// R35 lin-int(L12←L0):分段轨道 = Layout.SegRail(B.EditSegRail、圆角 12、内距 3),项 = Layout.SegItem(ToggleButton)
		Border seg = new Border { Child = U.H(2.0, _segList, _segGrid) };
		if (!LayoutVisualTokens.ApplyTheme(seg, LayoutVisualTokens.ThemeKeys.SegRail))
		{
			seg.CornerRadius = new CornerRadius(8.0);
			seg.Padding = new Thickness(3.0);
			seg.VerticalAlignment = VerticalAlignment.Center;
			seg.Dyn(Border.BackgroundProperty, "B.SegRail");
		}
		// 提示 = Layout.EditHint(12 B.InkMuted、省略号)
		_hintText = LayoutEditChrome.ThemedText(HintFor(_listKind), LayoutVisualTokens.ThemeKeys.EditHint, 12.0, bold: false, "B.InkMuted");
		if (_hintText.Theme == null)
		{
			_hintText.TextTrimming = TextTrimming.CharacterEllipsis;
			_hintText.VerticalAlignment = VerticalAlignment.Center;
		}
		_hintText.Margin = new Thickness(4.0, 0.0, 4.0, 0.0);
		ToolTip.SetTip(_hintText, FullHint());
		_undoBtn = LayoutEditChrome.IconButton(LayoutEditChrome.UndoData, I18nCore.Tr("撤销 (Ctrl+Z)"), delegate
		{
			UndoEdit();
		}, 15.0, "Geo.Undo");
		// 撤销 / 重做图标走 L0 门面 Glyph(15,线宽 1.5,描边跟随按钮前景 → 置灰自动变色)
		_undoBtn.Content = LayoutEditChrome.GlyphFor(_undoBtn, LayoutVisualTokens.GeoKeys.Undo, LayoutEditChrome.UndoData, LayoutVisualTokens.GlyphSizeUndoRedo);
		_undoBtn.IsEnabled = false;
		_redoBtn = LayoutEditChrome.IconButton(LayoutEditChrome.RedoData, I18nCore.Tr("重做 (Ctrl+Y)"), delegate
		{
			RedoEdit();
		}, 15.0, "Geo.Redo");
		_redoBtn.Content = LayoutEditChrome.GlyphFor(_redoBtn, LayoutVisualTokens.GeoKeys.Redo, LayoutEditChrome.RedoData, LayoutVisualTokens.GlyphSizeUndoRedo);
		_redoBtn.IsEnabled = false;
		// 竖分隔线 = Layout.EditBarSep(1×18、B.EditOutline)
		Border sep = new Border();
		if (!LayoutVisualTokens.ApplyTheme(sep, LayoutVisualTokens.ThemeKeys.EditBarSep))
		{
			sep.Width = 1.0;
			sep.Height = 18.0;
			sep.Margin = new Thickness(2.0, 0.0, 2.0, 0.0);
			sep.VerticalAlignment = VerticalAlignment.Center;
			sep.Dyn(Border.BackgroundProperty, "B.Line");
		}
		_addBtn = U.Btn(I18nCore.Tr("添加"), BtnKind.Soft, delegate
		{
			ShowDrawer(!IsDrawerOpen);
		}, "Add");
		ToolTip.SetTip(_addBtn, I18nCore.Tr("添加组件,或者找回隐藏的内容"));
		// 「添加」角标 = Layout.Count + Layout.CountText(字号 12、圆角 8,效果图 .cnt)
		_addBadgeText = new TextBlock();
		if (!LayoutVisualTokens.ApplyTheme(_addBadgeText, LayoutVisualTokens.ThemeKeys.CountText))
		{
			_addBadgeText.FontSize = 11.0;
			_addBadgeText.FontWeight = FontWeight.Bold;
			_addBadgeText.HorizontalAlignment = HorizontalAlignment.Center;
			_addBadgeText.VerticalAlignment = VerticalAlignment.Center;
			_addBadgeText.Dyn(TextBlock.ForegroundProperty, "B.OnAccent");
		}
		_addBadge = new Border { Child = _addBadgeText, IsVisible = false };
		if (!LayoutVisualTokens.ApplyTheme(_addBadge, LayoutVisualTokens.ThemeKeys.Count))
		{
			_addBadge.CornerRadius = new CornerRadius(999.0);
			_addBadge.MinWidth = 16.0;
			_addBadge.Height = 16.0;
			_addBadge.Padding = new Thickness(4.0, 0.0, 4.0, 0.0);
			_addBadge.Dyn(Border.BackgroundProperty, "B.Accent");
		}
		// 角标跟在「添加」文字后面(按钮内容是横排 StackPanel),竖直居中 —— 主题的 Right/Top 是给叠放用的
		_addBadge.HorizontalAlignment = HorizontalAlignment.Left;
		_addBadge.VerticalAlignment = VerticalAlignment.Center;
		if (_addBtn.Content is Panel addContent)
		{
			addContent.Children.Add(_addBadge);
		}
		Button reset = U.Btn(I18nCore.Tr("重置本页"), BtnKind.Normal, delegate
		{
			ResetPage();
		});
		Button done = U.Btn(I18nCore.Tr("完成"), BtnKind.Primary, delegate
		{
			ExitEdit();
		});
		ToolTip.SetTip(done, I18nCore.Tr("完成编辑并保存 (Esc)"));
		_moreBtn = U.IconBtn("More", I18nCore.Tr("更多"), delegate
		{
			OpenMoreMenu();
		});
		Grid bar = U.G(null, "Auto,Auto,*,Auto,Auto,Auto,Auto,Auto,Auto,Auto");
		Control[] items = new Control[10] { _titleText, seg, _hintText, _undoBtn, _redoBtn, sep, _addBtn, reset, done, _moreBtn };
		for (int i = 0; i < items.Length; i++)
		{
			items[i].VerticalAlignment = VerticalAlignment.Center;
			if (i > 0)
			{
				items[i].Margin = new Thickness((i == 2) ? 6.0 : ((i == 4) ? 2.0 : 8.0), 0.0, 0.0, 0.0);
			}
			bar.Add(items[i], 0, i);
		}
		_hintText.Margin = new Thickness(8.0, 0.0, 4.0, 0.0);
		return bar;
	}

	private static Button SegButton(string text, string iconData, EventHandler<RoutedEventArgs> click)
	{
		// R35 lin-int(L12←L0):Layout.SegItem 的目标类型是 ToggleButton(Avalonia 11 里 ToggleButton : Button,字段类型不变);
		// 选中态改由 IsChecked 表达(RefreshEditBar),不再换 Btn.Normal / Btn.Ghost 两套主题。
		ToggleButton toggle = new ToggleButton();
		if (LayoutVisualTokens.ApplyTheme(toggle, LayoutVisualTokens.ThemeKeys.SegItem))
		{
			string geoKey = ReferenceEquals(iconData, LayoutEditChrome.ListModeData) ? LayoutVisualTokens.GeoKeys.ListMode : LayoutVisualTokens.GeoKeys.GridMode;
			toggle.Content = U.H(6.0, LayoutEditChrome.GlyphFor(toggle, geoKey, iconData, LayoutVisualTokens.GlyphSizeSeg), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
			// 点击时 ToggleButton 会自己翻 IsChecked;真正的选中态由 SetLayoutKind → RefreshEditBar 回写
			toggle.Click += click;
			return toggle;
		}
		Button button = U.Btn(text, BtnKind.Ghost, click);
		button.Content = U.H(6.0, LayoutEditChrome.BoundIcon(button, iconData, 13.0), U.Txt(text, 12.5));
		button.Padding = new Thickness(10.0, 2.0, 10.0, 2.0);
		button.MinHeight = 24.0;
		return button;
	}

	// 工具条状态:分段选中态 / 提示 / 「添加」上的数字
	private void RefreshEditBar()
	{
		if (_titleText != null)
		{
			_titleText.Text = LayoutEditChrome.EditTitle(_pageId);
		}
		if (_segList != null && _segGrid != null)
		{
			if (_segList is ToggleButton listToggle && _segGrid is ToggleButton gridToggle)
			{
				listToggle.IsChecked = _listKind;
				gridToggle.IsChecked = !_listKind;
			}
			else
			{
				_segList.Theme = U.Theme(_listKind ? "Btn.Normal" : "Btn.Ghost");
				_segGrid.Theme = U.Theme(_listKind ? "Btn.Ghost" : "Btn.Normal");
			}
		}
		if (_hintText != null)
		{
			_hintText.Text = HintFor(_listKind);
		}
		int hidden = HiddenCount();
		if (_addBadge != null && _addBadgeText != null)
		{
			_addBadge.IsVisible = hidden > 0;
			_addBadgeText.Text = hidden.ToString();
		}
	}

	private int HiddenCount()
	{
		int count = 0;
		foreach (string id in _state.Order)
		{
			if (_state.Hidden.Contains(id))
			{
				count++;
			}
		}
		return count;
	}

	// ⋯ 更多:导出本页… / 导入到本页… / 整套布局与方案…(跳 设置 → 布局)。[R35] GOAL §5.2 删掉「自动靠拢」界面(固定为开)
	private void OpenMoreMenu()
	{
		if (_moreBtn == null)
		{
			return;
		}
		ContextMenu menu = new ContextMenu();
		MenuItem export = new MenuItem { Header = I18nCore.Tr("导出本页…") };
		export.Click += delegate
		{
			PageLayoutIO.ExportToFileDialog(_pageId);
		};
		menu.Items.Add(export);
		MenuItem import = new MenuItem { Header = I18nCore.Tr("导入到本页…") };
		import.Click += delegate
		{
			ImportLayout();
		};
		menu.Items.Add(import);
		menu.Items.Add(new Separator());
		MenuItem whole = new MenuItem { Header = I18nCore.Tr("整套布局与方案…") };
		whole.Click += delegate
		{
			OpenLayoutSettingsFromEditor();
		};
		menu.Items.Add(whole);
		menu.PlacementTarget = _moreBtn;
		menu.Placement = Avalonia.Controls.PlacementMode.Bottom;
		menu.Open(_moreBtn);
	}

	// 「整套布局与方案…」/ 抽屉底部提示:跳到 设置 → 布局(沿用 LayoutShellHooks.OpenLayoutSettings →
	// SettingsPage.OpenLayoutView())。切走页面时 BlockHost.Unloaded 会自动收尾编辑态并保存。
	private void OpenLayoutSettingsFromEditor()
	{
		if (TopLevel.GetTopLevel(_root) is TipCL.App.Shell.MainWindow win)
		{
			ShowDrawer(false);
			LayoutShellHooks.OpenLayoutSettings(win);
		}
	}

	// 抽屉底部「…可在 设置 → 布局 → 高级 里开启」:跳到 设置 → 布局 并展开「高级」
	// (SettingsPage.OpenLayoutAdvanced() 由设置代理 D 新增,见 tmp/v3_handoff.md「D → C」)
	private void OpenLayoutAdvancedFromEditor()
	{
		if (TopLevel.GetTopLevel(_root) is not TipCL.App.Shell.MainWindow win)
		{
			return;
		}
		ShowDrawer(false);
		win.Navigate("settings");
		if (win.PageHostControl?.Content is TipCL.App.Pages.Settings.SettingsPage page)
		{
			page.OpenLayoutAdvanced();
		}
	}

	// ————————————— 列表 / 网格(§1.A.7) —————————————

	// 当前可见块是不是"每块占满一整行"的样子(stack 单列页进编辑态折出来就是这样)
	private bool IsListShape()
	{
		PageLayoutV2? page = _state?.Page;
		if (page == null || page.Mode != LayoutSchema.ModeGrid)
		{
			return page != null && page.Mode == LayoutSchema.ModeStack && LayoutEngine.StackColumns(page) <= 1;
		}
		int cols = LayoutGrid.Columns(page);
		foreach (LayoutCell cell in LayoutGrid.Pack(page))
		{
			if (cell.W < cols)
			{
				return false;
			}
		}
		return true;
	}

	/// <summary>[v3] 切换列表 / 网格(编辑态的分段按钮)。列表 = 每块一整行;网格 = 一行可以并排几块。</summary>
	internal void SetLayoutKind(bool list)
	{
		if (!_edit || list == _listKind || _state?.Page == null)
		{
			return;
		}
		if (_state.Page.Mode != LayoutSchema.ModeGrid)
		{
			// 极端情况(折算失败仍是 stack):只换标记,排布由 stack 自己的纵向流负责
			_listKind = list;
			RefreshEditBar();
			return;
		}
		if (_gesture.Length != 0)
		{
			EndBoxGesture(false, false);
		}
		PageLayoutV2 before = _state.ToPage();
		PageLayoutV2 page = _state.ToPage();
		int cols = LayoutGrid.Columns(page);
		List<LayoutCell> cells = LayoutGrid.Pack(page);
		cells.Sort(delegate(LayoutCell a, LayoutCell b)
		{
			int byRow = a.Y.CompareTo(b.Y);
			return (byRow != 0) ? byRow : a.X.CompareTo(b.X);
		});
		if (list)
		{
			_gridWidths.Clear();
			int row = 0;
			foreach (LayoutCell cell in cells)
			{
				_gridWidths[cell.Id] = new int[2] { cell.X, cell.W };
				cell.X = 0;
				cell.W = cols;
				cell.Y = row;
				row += Math.Max(1, cell.H);
			}
		}
		else
		{
			foreach (LayoutCell cell in cells)
			{
				if (_gridWidths.TryGetValue(cell.Id, out int[]? xw))
				{
					cell.W = LayoutSchema.ClampInt(xw[1], 1, cols);
					cell.X = LayoutSchema.ClampInt(xw[0], 0, Math.Max(0, cols - cell.W));
				}
			}
			LayoutGrid.Compact(cells, cols, null, null);
		}
		LayoutGrid.Apply(page, cells);
		_session.PushUndo(before);
		_box.ArmFlip();
		_listKind = list;
		_state = LayoutEngine.LoadState(_pageId, page, _refs, _state.ExistedInDoc);
		_session.Retain(_state.Order);
		Persist();
		Render();
		LayoutFx.ShowToast(list ? I18nCore.Tr("已切换到列表") : I18nCore.Tr("已切换到网格"), list ? I18nCore.Tr("每块占满一整行") : I18nCore.Tr("一行可以并排放几块,拖右边缘改宽度"), ToastKind.Info);
	}

	// 撤销 / 重做之后排法以实际形状为准:列表态下撤销出了并排的块 → 回到网格
	private void SyncLayoutKindAfterSnapshot()
	{
		if (_listKind && !IsListShape())
		{
			_listKind = false;
		}
	}

	// 新组件默认占几列(按 12 列制给,换算到本页列数;列表排法下一律整行)
	private int DefaultWidgetCols(string type, int cols)
	{
		if (_listKind)
		{
			return cols;
		}
		int of12 = type switch
		{
			LayoutSchema.WidgetDivider => 12,
			LayoutSchema.WidgetPlaytime => 6,
			LayoutSchema.WidgetWeb => 8,
			_ => 4
		};
		return LayoutSchema.ClampInt((int)Math.Round(of12 * (cols / 12.0)), Math.Min(cols, 2), cols);
	}

	// ————————————— 「添加」抽屉(§1.A.5) —————————————

	private Border? _drawer;

	private TextBlock? _drawerTitle;

	// [R35 lin-l12] 抽屉从右侧滑出(效果图 transform .26s ease-out);底部「网页、按钮组件默认关闭」提示只在开关关着时显示
	private readonly TranslateTransform _drawerSlide = new TranslateTransform(LayoutAddDrawer.DrawerWidth + 24.0, 0.0);

	private Control? _dangerNote;

	private bool _drawerOpen;

	private int _drawerAnim;

	private readonly StackPanel _drawerHidden = new StackPanel { Spacing = 6.0 };

	private readonly StackPanel _drawerWidgets = new StackPanel { Spacing = 6.0 };

	private bool IsDrawerOpen => _drawer != null && _drawerOpen;

	/// <summary>[R35] 「添加」抽屉是否开着(测试 / 宿主用)。</summary>
	internal bool DrawerOpen => IsDrawerOpen;

	/// <summary>
	/// [R35 lin-int] 正式入口:选中某块(截图开关 <c>--select &lt;blockId&gt;</c> 经 SmokeRunner.SelectBlockHook 调用)。
	/// 只在编辑态、且该块在本页时生效;返回是否选中成功。
	/// </summary>
	internal bool SelectBlock(string blockId)
	{
		if (!_edit || string.IsNullOrEmpty(blockId))
		{
			return false;
		}
		bool onPage = false;
		foreach (string id in _state.Order)
		{
			if (string.Equals(id, blockId, StringComparison.Ordinal))
			{
				onPage = true;
				break;
			}
		}
		if (!onPage)
		{
			return false;
		}
		_session.Select(blockId, false);
		UpdateSelectionVisual();
		return true;
	}

	/// <summary>[R35 lin-int] 正式入口:打开「添加」抽屉(截图开关 <c>--pop layout-drawer</c> 经 SmokeRunner.OpenAddDrawerHook 调用)。</summary>
	internal bool OpenAddDrawer()
	{
		ShowDrawer(true);
		return IsDrawerOpen;
	}

	private Control BuildDrawer()
	{
		// R35 lin-int:抽屉标题 = Layout.DrawerTitle(15 ExtraBold)
		_drawerTitle = LayoutEditChrome.ThemedText(LayoutEditChrome.DrawerTitle(_pageId), LayoutVisualTokens.ThemeKeys.DrawerTitle, 15.0, bold: true);
		if (_drawerTitle.Theme == null)
		{
			_drawerTitle.TextTrimming = TextTrimming.CharacterEllipsis;
			_drawerTitle.VerticalAlignment = VerticalAlignment.Center;
		}
		Button close = U.IconBtn("Close", I18nCore.Tr("关闭 (Esc)"), delegate
		{
			ShowDrawer(false);
		}, 12.0);
		Grid head = U.G(null, "*,Auto");
		head.Add(_drawerTitle, 0);
		head.Add(close, 0, 1);
		head.Margin = new Thickness(16.0, 14.0, 12.0, 10.0);
		TextBlock note = U.Txt(I18nCore.Tr("网页、按钮组件默认关闭，可在 设置 → 布局 → 高级 里开启。"), 12.0, bold: false, "B.InkSoft");
		note.TextWrapping = TextWrapping.Wrap;
		Grid noteRow = U.G(null, "*,Auto");
		noteRow.Add(note, 0);
		Control chevron = IconLib.Make("ChevronRight", "B.AccentText", 12);
		chevron.VerticalAlignment = VerticalAlignment.Center;
		chevron.Margin = new Thickness(6.0, 0.0, 0.0, 0.0);
		noteRow.Add(chevron, 0, 1);
		Border noteCard = U.RowCard(noteRow, delegate
		{
			OpenLayoutAdvancedFromEditor();
		}, 10.0);
		ToolTip.SetTip(noteCard, I18nCore.Tr("跳到 设置 → 布局 → 高级"));
		noteCard.Margin = new Thickness(0.0, 12.0, 0.0, 0.0);
		_dangerNote = noteCard;
		StackPanel body = U.V(6.0, SectionLabel(I18nCore.Tr("已隐藏的内容")), _drawerHidden, SectionLabel(I18nCore.Tr("组件")), _drawerWidgets, noteCard);
		body.Margin = new Thickness(16.0, 0.0, 16.0, 16.0);
		ScrollViewer list = new ScrollViewer
		{
			Content = body,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto
		};
		Grid root = U.G("Auto,*");
		root.Add(head, 0);
		root.Add(list, 1);
		_drawerSlide.Transitions = new Avalonia.Animation.Transitions
		{
			new Avalonia.Animation.DoubleTransition
			{
				Property = TranslateTransform.XProperty,
				Duration = TimeSpan.FromMilliseconds(LayoutAddDrawer.SlideMs),
				Easing = new Avalonia.Animation.Easings.CubicEaseOut()
			}
		};
		_drawer = new Border
		{
			Child = root,
			IsVisible = false,
			ZIndex = 100,
			RenderTransform = _drawerSlide
		};
		// [R35] 底色读 L0 的 B.EditBarBg(兜底 B.Paper);L0 的 Layout.Drawer ControlTheme 存在就套上。
		// [R35 lin-int] 主题 = 宽 316、贴右、左 1px 细线、Fx.Drawer 阴影、无圆角(效果图 .drawer 贴边);
		// 套上后不再写同名本地属性(会盖过主题)。套不上时沿用 C 的兜底外观。
		if (!LayoutVisualTokens.ApplyTheme(_drawer, LayoutVisualTokens.ThemeKeys.Drawer))
		{
			_drawer.Width = LayoutAddDrawer.DrawerWidth;
			_drawer.HorizontalAlignment = HorizontalAlignment.Right;
			_drawer.VerticalAlignment = VerticalAlignment.Stretch;
			_drawer.CornerRadius = new CornerRadius(12.0, 0.0, 0.0, 12.0);
			_drawer.BorderThickness = new Thickness(1.0, 1.0, 0.0, 1.0);
			_drawer.BoxShadow = new BoxShadows(new BoxShadow
			{
				Blur = 32.0,
				OffsetX = -12.0,
				Color = Color.FromArgb(36, 0, 0, 0)
			});
			_drawer.Dyn(Border.BackgroundProperty, LayoutEditChrome.KeyOr("B.EditBarBg", "B.Paper"));
			_drawer.Dyn(Border.BorderBrushProperty, "B.Line");
		}
		return _drawer;
	}

	private static TextBlock SectionLabel(string text)
	{
		// R35 lin-int:分节小字 = Layout.DrawerSection(12 B.InkMuted、上 10 下 6)
		TextBlock label = LayoutEditChrome.ThemedText(text, LayoutVisualTokens.ThemeKeys.DrawerSection, 12.0, bold: false, "B.InkMuted");
		if (label.Theme == null)
		{
			label.Margin = new Thickness(0.0, 10.0, 0.0, 0.0);
		}
		return label;
	}

	/// <summary>[v3] 打开 / 关上「添加」抽屉(只在编辑态有意义)。</summary>
	internal void ShowDrawer(bool open)
	{
		if (_drawer == null)
		{
			return;
		}
		bool show = open && _edit;
		int version = ++_drawerAnim;
		if (show)
		{
			RefreshDrawer();
			_drawerOpen = true;
			_drawer.IsVisible = true;
			// 从右侧滑入:X 从「抽屉宽 + 24」过渡到 0(DoubleTransition 260ms 缓出)
			_drawerSlide.X = 0.0;
			return;
		}
		bool wasOpen = _drawerOpen;
		_drawerOpen = false;
		_drawerSlide.X = LayoutAddDrawer.DrawerWidth + 24.0;
		if (!wasOpen || !_edit)
		{
			// 退出编辑 / 本来就关着:立刻收起,不播动画
			_drawer.IsVisible = false;
			return;
		}
		// 滑出结束后再真正隐藏(期间又被打开就不隐藏)
		DispatcherTimer.RunOnce(delegate
		{
			if (version == _drawerAnim && !_drawerOpen && _drawer != null)
			{
				_drawer.IsVisible = false;
			}
		}, TimeSpan.FromMilliseconds(LayoutAddDrawer.SlideMs));
	}

	// 抽屉内容:已隐藏的内容(点一下放回原位)+ 组件(点一下加到页面末尾)。
	// 「组件」一节要查 widgets[] 定义(读一次布局文档),只在抽屉开着时刷新;「已隐藏」一节很便宜,每次都刷。
	private void RefreshDrawer(bool widgets = true)
	{
		if (_drawerTitle != null)
		{
			_drawerTitle.Text = LayoutEditChrome.DrawerTitle(_pageId);
		}
		_drawerHidden.Children.Clear();
		foreach (string id in _state.Order)
		{
			if (!_state.Hidden.Contains(id))
			{
				continue;
			}
			BlockSpec spec = SpecOf(id);
			_drawerHidden.Children.Add(DrawerRow(LayoutEditChrome.Icon(LayoutEditChrome.EyeOffData, "B.InkSoft", 18.0), spec.Title, I18nCore.Tr("点一下放回原来的位置"), I18nCore.Tr("显示"), enabled: true, delegate
			{
				Restore(spec);
			}));
		}
		if (_drawerHidden.Children.Count == 0)
		{
			_drawerHidden.Children.Add(EmptyBox(I18nCore.Tr("没有隐藏的内容")));
		}
		if (_dangerNote != null)
		{
			// 开关打开后不再提示「默认关闭」
			_dangerNote.IsVisible = !LayoutAddDrawer.IsDangerAllowed();
		}
		if (!widgets)
		{
			return;
		}
		_drawerWidgets.Children.Clear();
		List<string?> onPage = WidgetTypesOnPage();
		foreach (string type in LayoutAddDrawer.Types())
		{
			string captured = type;
			bool used = LayoutAddDrawer.IsAdded(type, onPage);
			_drawerWidgets.Children.Add(DrawerRow(WidgetIcon(type), WidgetUI.TypeLabel(type), LayoutAddDrawer.DescriptionOf(type), used ? I18nCore.Tr("已添加") : I18nCore.Tr("添加"), !used, delegate
			{
				AddWidgetToPage(captured);
			}));
		}
	}

	// [R35 lin-l12] §5.2「没有隐藏项时显示一个虚线空状态框」(效果图 .dempty:1px dashed、圆角 12、居中灰字)
	private static Control EmptyBox(string text)
	{
		TextBlock label = LayoutEditChrome.ThemedText(text, LayoutVisualTokens.ThemeKeys.DrawerSection, 12.0, bold: false, "B.InkMuted");
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.Margin = new Thickness(10.0);
		// R35 lin-int(L12←L0):虚线框 = Layout.DrawerEmpty(1px 4,3 虚线 B.EditOutline、圆角 12);套不上时沿用本地写法
		Avalonia.Controls.Shapes.Rectangle dash = new Avalonia.Controls.Shapes.Rectangle();
		if (!LayoutVisualTokens.ApplyTheme(dash, LayoutVisualTokens.ThemeKeys.DrawerEmpty))
		{
			dash.RadiusX = 12.0;
			dash.RadiusY = 12.0;
			dash.StrokeThickness = 1.0;
			dash.StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4.0, 3.0 };
			dash.IsHitTestVisible = false;
			dash.Dyn(Avalonia.Controls.Shapes.Shape.StrokeProperty, LayoutEditChrome.KeyOr("B.EditOutline", "B.Line"));
		}
		Grid box = new Grid();
		box.Children.Add(dash);
		box.Children.Add(label);
		return box;
	}

	// 本页现有自建块的类型(判断「已添加」用)
	private List<string?> WidgetTypesOnPage()
	{
		List<string?> types = new List<string?>();
		foreach (string id in _state.Order)
		{
			if (LayoutEngine.WidgetIdOf(id) == null)
			{
				continue;
			}
			WidgetDef? def = WidgetRegistry.FindWidget(id);
			if (def != null)
			{
				types.Add(def.Type);
			}
		}
		return types;
	}

	// 抽屉里列出的组件类型 / 能否重复 / 一句说明:[R35] 纯规则挪到 LayoutAddDrawer.cs(LayoutAddDrawer.Types / AllowsMultiple /
	// DescriptionOf);网页 / 按钮仍按 LayoutProfiles.DangerousWidgetsEnabled(经 LayoutAddDrawer.DangerWidgetsAllowed 钩子)现读。
	private static bool IsDangerousType(string type)
	{
		return LayoutAddDrawer.IsDangerousType(type);
	}

	// 抽屉里的一行:整行是一个按钮(点哪儿都行),右边一个小药丸写「显示 / 添加」
	private static Button DrawerRow(Control icon, string title, string desc, string action, bool enabled, EventHandler<RoutedEventArgs> click)
	{
		// R35 lin-int(L12←L0):图标框 = Layout.DrawerIcon(40×40、圆角 8、B.EditBarBg);整行底板 = Layout.DrawerItem(内距 8、圆角 12、B.EditItemBg)
		Border iconBox = new Border { Child = icon.Center() };
		if (!LayoutVisualTokens.ApplyTheme(iconBox, LayoutVisualTokens.ThemeKeys.DrawerIcon))
		{
			iconBox.Width = 40.0;
			iconBox.Height = 40.0;
			iconBox.CornerRadius = new CornerRadius(8.0);
			iconBox.Dyn(Border.BackgroundProperty, "B.Paper");
		}
		TextBlock titleText = U.Txt(title, 13.0, bold: true);
		titleText.TextTrimming = TextTrimming.CharacterEllipsis;
		TextBlock descText = U.Txt(desc, 12.0, bold: false, "B.InkMuted");
		descText.TextTrimming = TextTrimming.CharacterEllipsis;
		StackPanel texts = U.V(1.0, titleText, descText);
		texts.VerticalAlignment = VerticalAlignment.Center;
		Border pill = new Border
		{
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(10.0, 4.0, 10.0, 4.0),
			VerticalAlignment = VerticalAlignment.Center,
			Child = U.Txt(action, 12.0, bold: true, enabled ? "B.AccentText" : "B.InkMuted")
		};
		pill.Dyn(Border.BackgroundProperty, enabled ? "B.AccentSoft" : "B.LineSoft");
		Grid grid = U.G(null, "40,*,Auto");
		grid.Add(iconBox, 0);
		grid.Add(texts, 0, 1);
		grid.Add(pill, 0, 2);
		texts.Margin = new Thickness(10.0, 0.0, 10.0, 0.0);
		Border row = new Border { Child = grid };
		if (LayoutVisualTokens.ApplyTheme(row, LayoutVisualTokens.ThemeKeys.DrawerItem))
		{
			// 行间距由外层 StackPanel(Spacing 6)给,主题里的下边距 6 归零免得翻倍
			row.Margin = new Thickness(0.0);
		}
		else
		{
			row.CornerRadius = new CornerRadius(12.0);
			row.Padding = new Thickness(8.0);
			row.Dyn(Border.BackgroundProperty, "B.Paper2");
		}
		Button button = new Button
		{
			Theme = U.Theme("Btn.Ghost"),
			Padding = new Thickness(0.0),
			HorizontalAlignment = HorizontalAlignment.Stretch,
			HorizontalContentAlignment = HorizontalAlignment.Stretch,
			Content = row,
			IsEnabled = enabled
		};
		Avalonia.Automation.AutomationProperties.SetName(button, action + " " + title);
		button.Click += click;
		return button;
	}

	private static bool IsMultiWidget(string type)
	{
		return LayoutAddDrawer.AllowsMultiple(type);
	}

	private bool PageHasWidget(string type)
	{
		return WidgetTypesOnPage().Contains(type);
	}

	private static Control WidgetIcon(string type)
	{
		return type switch
		{
			LayoutSchema.WidgetNote => IconLib.Make("Edit", "B.InkSoft", 18),
			LayoutSchema.WidgetShortcut => IconLib.Make("Play", "B.InkSoft", 18),
			LayoutSchema.WidgetClock => IconLib.Make("Clock", "B.InkSoft", 18),
			LayoutSchema.WidgetPlaytime => IconLib.Make("Lightning", "B.InkSoft", 18),
			LayoutSchema.WidgetWeb => IconLib.Make("World", "B.InkSoft", 18),
			LayoutSchema.WidgetButton => IconLib.Make("Open", "B.InkSoft", 18),
			_ => LayoutEditChrome.Icon(LayoutEditChrome.LineData, "B.InkSoft", 18.0)
		};
	}

	/// <summary>[v3] 从抽屉添加一个组件到本页末尾(定义写进 widgets[],块引用接到本页;能撤销)。</summary>
	internal void AddWidgetToPage(string type)
	{
		if (!_edit || _state?.Page == null)
		{
			return;
		}
		if (!IsMultiWidget(type) && PageHasWidget(type))
		{
			return;
		}
		PageLayoutV2 before = _state.ToPage();
		string widgetId;
		_saving = true;
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			if (IsDangerousType(type))
			{
				// LayoutWidgetOps.AddWidget 只收安全类型;网页 / 按钮在这里自己建,settings.enabled 不写 = 默认关
				// (设置 → 布局 → 高级 的警示:导入或新加的网页 / 按钮组件仍默认关闭,需要逐个打开)
				WidgetDef def = new WidgetDef
				{
					Id = LayoutSchema.NewWidgetId(),
					Type = type,
					Title = WidgetUI.TypeLabel(type)
				};
				doc.Layout.Widgets.Add(def);
				widgetId = def.Id;
			}
			else
			{
				widgetId = LayoutWidgetOps.AddWidget(doc, type);
			}
			LayoutStoreV2.Save(doc);
		}
		catch (Exception ex)
		{
			LayoutFx.ShowToast(I18nCore.Tr("添加失败"), ex.Message, ToastKind.Error);
			return;
		}
		finally
		{
			_saving = false;
		}
		string blockId = LayoutWidgetOps.BlockIdOf(widgetId);
		PageLayoutV2 page = _state.ToPage();
		int bottomRow = LayoutGrid.RowCount(LayoutGrid.Pack(page));
		if (!LayoutWidgetOps.AppendBlock(page, blockId))
		{
			return;
		}
		if (page.Mode == LayoutSchema.ModeGrid)
		{
			int cols = LayoutGrid.Columns(page);
			foreach (BlockLayout block in page.Blocks)
			{
				if (block != null && string.Equals(block.Id, blockId, StringComparison.Ordinal))
				{
					int w = DefaultWidgetCols(type, cols);
					block.X = 0.0;
					block.Y = Math.Max(1, bottomRow);
					block.W = w;
					block.H = 1.0;
					block.Span = w;
					block.MinH = 1.0;
				}
			}
		}
		_session.PushUndo(before);
		_box.ArmFlip();
		_state = LayoutEngine.LoadState(_pageId, page, _refs, _state.ExistedInDoc);
		_session.Select(blockId, false);
		Persist();
		Render();
		RefreshDrawer();
		// [R35 lin-l12] §5.2:新块加到末尾 → 滚过去并闪一下主题色光晕
		FlashBlock(blockId);
		LayoutFx.ShowToast(string.Format(I18nCore.Tr("已添加「{0}」"), WidgetUI.TypeLabel(type)), I18nCore.Tr("放在了页面末尾"), ToastKind.Success);
	}

	private void ScrollBoxToEnd()
	{
		Dispatcher.UIThread.Post(delegate
		{
			try
			{
				_boxScroll.Offset = new Vector(_boxScroll.Offset.X, Math.Max(0.0, _boxScroll.ScrollBarMaximum.Y));
			}
			catch
			{
				// 页面正在销毁:滚不到也不影响
			}
		}, DispatcherPriority.Background);
	}

	/// <summary>
	/// [R35 lin-l12] 滚到某块并闪一下主题色光晕(抽屉里点「显示」/「添加」之后,效果图 flash(id))。
	/// 等这一轮排版落定再滚(新块刚进画布还没量过);块不在可视树上就退回到滚到末尾。
	/// </summary>
	internal void FlashBlock(string blockId)
	{
		if (string.IsNullOrEmpty(blockId))
		{
			return;
		}
		LastFlashedBlock = blockId;
		Dispatcher.UIThread.Post(delegate
		{
			try
			{
				if (!_cards.TryGetValue(blockId, out LayoutCard? card) || card.Parent == null)
				{
					ScrollBoxToEnd();
					return;
				}
				ScrollViewer host = (IsBoxMode ? _boxScroll : _scroll);
				Point? top = card.TranslatePoint(new Point(0.0, 0.0), host);
				if (top.HasValue && host.Viewport.Height > 1.0)
				{
					double y = host.Offset.Y + top.Value.Y;
					double h = card.Bounds.Height;
					double want = host.Offset.Y;
					if (top.Value.Y < 0.0)
					{
						want = y - 24.0;
					}
					else if (top.Value.Y + h > host.Viewport.Height)
					{
						want = y + h - host.Viewport.Height + 24.0;
					}
					host.Offset = new Vector(host.Offset.X, Math.Max(0.0, Math.Min(host.ScrollBarMaximum.Y, want)));
				}
				else
				{
					card.BringIntoView();
				}
				card.Flash();
			}
			catch
			{
				// 页面正在销毁:滚不到也不影响
			}
		}, DispatcherPriority.Background);
	}

	/// <summary>[R35] 最近一次「滚过去并闪一下」的块(测试用)。</summary>
	internal string? LastFlashedBlock { get; private set; }

	/// <summary>[R35 lin-l12] §6.4 第 2 条:启动页这类深色背景(pageId == "launch")的编辑控件用深色玻璃配色。</summary>
	internal bool IsDarkSurface => string.Equals(_pageId, "launch", StringComparison.Ordinal);

	/// <summary>[R35 lin-l12] 拖动 / 改宽度期间的淡色列底纹(只在网格画布上;canvas 老存档不画)。</summary>
	private void ShowColumnShade(bool show)
	{
		if (!show || _box.IsCanvas)
		{
			_shade.Hide();
			return;
		}
		_shade.Margin = _box.Margin;
		_shade.Show(_box.Cols, _box.ColumnWidthValue, _box.Gap, IsDarkSurface);
	}

	/// <summary>[R35] 列底纹当前是否显示(测试用)。</summary>
	internal bool ColumnShadeVisible => _shade.IsVisible;

	/// <summary>
	/// [R35] 编辑条上所有文字(测试用:断言没有「吸附」「自动靠拢」「全部折叠」这类已删界面、也没有块 id)。
	/// </summary>
	internal List<string> EditBarTexts()
	{
		List<string> texts = new List<string>();
		foreach (Avalonia.LogicalTree.ILogical node in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(_barHost))
		{
			if (node is TextBlock text && !string.IsNullOrEmpty(text.Text))
			{
				texts.Add(text.Text!);
			}
		}
		return texts;
	}

	// ————————————— 隐藏与删除分开(§1.A.6) —————————————

	/// <summary>[v3] 删除自建组件(垃圾桶):只摘本页的块引用,组件定义留在 widgets[] 里,Ctrl+Z 能撤销。</summary>
	internal void DeleteBlock(BlockSpec spec)
	{
		if (!_edit || spec == null || LayoutEngine.WidgetIdOf(spec.Id) == null)
		{
			return;
		}
		PageLayoutV2 before = _state.ToPage();
		PageLayoutV2 page = _state.ToPage();
		int removed = page.Blocks.RemoveAll((BlockLayout b) => b != null && string.Equals(b.Id, spec.Id, StringComparison.Ordinal));
		if (removed == 0)
		{
			return;
		}
		_session.PushUndo(before);
		_box.ArmFlip();
		_state = LayoutEngine.LoadState(_pageId, page, _refs, _state.ExistedInDoc);
		_session.Retain(_state.Order);
		CompactState();
		Persist();
		Render();
		LayoutFx.ShowToast(string.Format(I18nCore.Tr("已删除「{0}」"), spec.Title), I18nCore.Tr("按 Ctrl+Z 可以撤销"), ToastKind.Success);
	}

	// 自建组件的显示名:组件自己的标题(如「联机便签」),没写标题用类型名;定义找不到就叫「组件」(不露 custom:/w_xxx 内部 id)
	// [R35] 规则统一到 LayoutBlockTitles.Resolve(LayoutAddDrawer.cs),这里只留个转发
	private static string WidgetTitle(string blockId)
	{
		return LayoutBlockTitles.Resolve(blockId, null, WidgetRegistry.FindWidget, PluginBlocks.TitleOf);
	}
}
