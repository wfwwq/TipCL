using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TipCL.Services;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Pages.Layout;

// 页面区块的渲染 + 编辑宿主(L1 把原来 BlockHost 里的那一套搬进来):
//   常态:单列 = 与改动前完全相同的纵向流(SPanel + 每块一张零装饰卡),多列 = 行内 Grid(span 跨列);
//   编辑态:每块顶部操作条(拖拽手柄 + 标题 + 显隐)+ 底部隐藏托盘 + 悬浮工具条(完成 / 重置本页 / 导出 / 导入)。
// 三条边界:
//   1) 顺序 / 显隐 / span 的状态与几何全在纯模型层(LayoutPageState + LayoutEngine),本文件只做 WPF 组装与手势;
//   2) 持久化只走 LayoutStoreV2(v2 是唯一真源,v1 由它单向写通),写完清一次 PageLayoutStore 缓存;
//   3) 内容来源:spec.Content 优先 → 内容工厂(§4.F 的 custom: / plugin: 口子)→ 都没有则显示「区块不可用」占位。
public sealed class LayoutEditor
{
	private readonly string _pageId;

	private readonly List<BlockSpec> _specs = new List<BlockSpec>();

	private readonly List<LayoutBlockRef> _refs = new List<LayoutBlockRef>();

	private readonly Dictionary<string, BlockSpec> _byId = new Dictionary<string, BlockSpec>(StringComparer.Ordinal);

	// 每个区块一张卡(含编辑态操作条),常驻缓存,重排/显隐只动它在可视树里的位置
	private readonly Dictionary<string, LayoutCard> _cards = new Dictionary<string, LayoutCard>();

	// 页面对宿主的显式边距覆盖(构造第 3 参);null = 用布局文档的 pad
	private readonly Thickness? _marginOverride;

	// [layout-fix] 规格 §4-能力2:页面的「出厂默认布局」(构造第 4 参 defaultPage)。
	// 布局文件里还没有该页的记录时用它;「重置本页」也回到它(见 ResetPage)。null = 白名单默认布局(stack)。
	private readonly PageLayoutV2? _defaultPage;

	// [layout-fix] 规格 §3-问题7 + 规格2-问题2.3:stack 页进编辑态会被**临时**折算成 12 列网格。
	//   _preEditPage   = 折算前的原样快照(退出时若一次都没动过,原样还原 —— 磁盘上不该留下任何痕迹)
	//   _autoGrid      = 这次编辑态是"临时折算"出来的
	//   _gridTouched   = 这次编辑动过(拖 / 缩放 / 隐藏 / 恢复 / 落盘)⇒ 退出时保留网格
	private PageLayoutV2? _preEditPage;

	private bool _autoGrid;

	private bool _gridTouched;

	private readonly SPanel _list = new SPanel
	{
		Orientation = Orientation.Vertical,
		Spacing = LayoutSchema.GapDefault
	};

	private LayoutPageState _state;

	private readonly Border _trayHost = new Border();

	private readonly WrapPanel _tray = new WrapPanel();

	private readonly Border _barHost = new Border();

	private readonly Grid _root;

	private bool _edit;

	private LayoutCard? _dragCard;

	// 拖拽手势状态:手柄捕获鼠标期间按缓存高度计算相邻块中点
	private readonly Dictionary<string, double> _dragHeights = new Dictionary<string, double>();

	// ————— L2:grid/canvas 画布 + 编辑体验(多选 / 撤销重做 / 辅助线 / 框选 / 键盘) —————

	// 编辑会话:多选集合 + 撤销/重做(内存快照,落盘仍即时以保崩溃安全)+ 剪贴板 + 键盘意图;
	// 纯模型(零 WPF),见 LayoutEngine.LayoutEditSession —— 交互逻辑可在 net8 测试宿主里断言。
	private readonly LayoutEditSession _session = new LayoutEditSession();

	// 画布 Panel(grid/canvas 用自绘 Panel,不用 WPF 的 Canvas)+ 对齐辅助线/框选覆盖层
	private readonly GridCanvasPanel _box = new GridCanvasPanel();

	private readonly GuideLayer _guide = new GuideLayer();

	private readonly Grid _stage = Ui.G(null, null);

	// 两个可视宿主:stack 用 SPanel 纵向流(与 L1 逐像素一致),grid/canvas 用画布 Panel;按模式切换,不并存
	private readonly SmoothScroll _scroll;

	private readonly SmoothScroll _boxScroll;

	// 全局密度(§4.B;缺省 standard → 间距档 10 = 现状 BlockHost.Gap)
	private string _density = LayoutSchema.DensityStandard;

	// 宿主喂进来的"内容区可用宽"(>0 时覆盖 LayoutVariantHost.Width;0 = 用全局提示,再没有就不做变体判定)
	private double _widthHint;

	// 自己正在提交(内部守卫):本次保存触发的 LayoutChanged 不要再让自己重排一遍
	private bool _saving;

	// canvas 吸附档(px;编辑态可切 {0,4,8,16,24})
	private double _snapStep = LayoutCanvas.DefaultStep;

	// 一次画布手势:起手时的槽位快照 + 指针起点(用绝对值算位移,避免逐帧累积误差)
	private string _gesture = "";

	private Point _gestureStart;

	private string _gestureHandle = "";

	private readonly Dictionary<string, LayoutCell> _gestureCells = new Dictionary<string, LayoutCell>(StringComparer.Ordinal);

	// [layout-fix] 规格2-问题2.2:手势开始时的**全部**槽位快照 + 行高快照。
	// 每次鼠标移动都从这份快照重新算(而不是在上一次的结果上继续叠),否则反复经过同一格会越拖越乱;
	// 行高快照用来把"鼠标落在哪一行"折算成行号 —— 过去用"位移 ÷ 被拖卡片自身高度",
	// 于是高卡片几乎拖不动、矮卡片一拖跳好几行。
	private readonly Dictionary<string, LayoutCell> _gestureAll = new Dictionary<string, LayoutCell>(StringComparer.Ordinal);

	private double[] _gestureRows = new double[0];

	// 被拖块**原来的**列(左右互换的候选落点;见 LayoutGrid.Compact 的 swapX)
	private LayoutRect _gestureAnchor;

	// 工具条上的撤销/重做按钮(按栈的可用性置灰)
	private Button? _undoBtn;

	private Button? _redoBtn;

	// 吸附档按钮(只在 grid/canvas 编辑态可用)
	private Button? _snapBtn;

	// [layout-fix] 参考件v2-对齐:全部折叠 / 全部展开(仅编辑态)
	private Button? _foldAllBtn;

	// [layout-fix] 参考件v2-对齐:拖动 / 缩放时贴近可视区上下边缘的自动滚动
	private System.Windows.Threading.DispatcherTimer? _autoScroll;

	private Point _lastPointer;

	/// <summary>构造:pageId + 页面块描述 + 可选的页级边距覆盖(null = 用文档 pad,一般是 18,14,18,16)。</summary>
	/// <param name="defaultPage">
	/// [layout-fix] 规格 §4-能力2:该页的「出厂默认布局」。布局文件里还没有该页记录时用它(实例库就靠它默认左右排布),
	/// null = 白名单默认布局(单列 stack + 默认间距边距,与改动前完全一致)。
	/// </param>
	public LayoutEditor(string pageId, IReadOnlyList<BlockSpec> specs, Thickness? contentMargin = null, PageLayoutV2? defaultPage = null)
	{
		_pageId = pageId ?? "";
		_marginOverride = contentMargin;
		_defaultPage = ((defaultPage != null) ? defaultPage.Clone() : null);
		if (specs != null)
		{
			foreach (BlockSpec spec in specs)
			{
				if (spec != null && !string.IsNullOrEmpty(spec.Id))
				{
					_specs.Add(spec);
					_byId[spec.Id] = spec;
					_refs.Add(new LayoutBlockRef(spec.Id, spec.DefaultVisible));
				}
			}
		}
		_list.Margin = (_marginOverride ?? new Thickness(18.0, 14.0, 18.0, 16.0));
		// 底部托盘(编辑态):已隐藏区块以「+ 标题」chip 呈现,点击恢复
		SPanel sPanel = Ui.V(6.0, Ui.Muted(I18n.L("已隐藏的区块")), _tray);
		_trayHost.CornerRadius = new CornerRadius(8.0);
		_trayHost.Padding = new Thickness(10.0);
		_trayHost.Visibility = Visibility.Collapsed;
		_trayHost.SetResourceReference(Border.BackgroundProperty, "B.Paper2");
		_trayHost.Child = sPanel;
		// 底部悬浮工具条(编辑态):撤销 / 重做 / 完成 / 重置本页 / 导出布局 / 导入布局
		_undoBtn = Ui.Btn(I18n.L("撤销"), BtnKind.Normal, delegate
		{
			UndoEdit();
		});
		_undoBtn.IsEnabled = false;
		_redoBtn = Ui.Btn(I18n.L("重做"), BtnKind.Normal, delegate
		{
			RedoEdit();
		});
		_redoBtn.IsEnabled = false;
		// 吸附档(编辑态可切 {0,4,8,16,24},沿用旧 DashLayout.Grid 的档位):
		// 只在画布模式有意义,按钮常驻但内容/可用性随模式刷新
		_snapBtn = Ui.Btn(I18n.L("吸附"), BtnKind.Normal, delegate
		{
			CycleSnapStep();
		});
		_snapBtn.ToolTip = I18n.L("吸附到栅格");
		// [layout-fix] 参考件v2-对齐:一键把所有区块收成标题栏,方便在长页面上腾挪(仅编辑时)
		_foldAllBtn = Ui.Btn(I18n.L("全部折叠"), BtnKind.Normal, delegate
		{
			ToggleFoldAll();
		});
		_foldAllBtn.ToolTip = I18n.L("把所有区块收成标题栏,方便拖动排列(只在编辑时有效,不影响保存的布局)");
		SPanel sPanel2 = Ui.H(8.0, _undoBtn, _redoBtn, _snapBtn, _foldAllBtn, Ui.Btn(I18n.L("完成编辑"), BtnKind.Primary, delegate
		{
			ExitEdit();
		}), Ui.Btn(I18n.L("重置本页"), BtnKind.Normal, delegate
		{
			ResetPage();
		}), Ui.Btn(I18n.L("导出布局"), BtnKind.Normal, delegate
		{
			PageLayoutIO.ExportToFileDialog(_pageId);
		}), Ui.Btn(I18n.L("导入布局"), BtnKind.Normal, delegate
		{
			ImportLayout();
		}));
		sPanel2.HorizontalAlignment = HorizontalAlignment.Right;
		// [layout-fix] 规格2-问题6:工具条改成两列 Grid(*,Auto) —— 左列提示文字(宽度不够用省略号截断 +
		// 鼠标悬停显示完整内容),右列按钮组靠右。设置页这种窄区域里按钮不会被挤掉、提示也不会被裁一半。
		// [layout-fix] 规格 §3-问题7:提示文案「按住区块拖动移动 · 拖右/下边角缩放」。
		TextBlock hintText = Ui.Txt(I18n.L("按住区块拖动移动 · 拖右/下边角缩放"), 12.0, bold: false, "B.InkMuted");
		hintText.TextTrimming = TextTrimming.CharacterEllipsis;
		hintText.VerticalAlignment = VerticalAlignment.Center;
		hintText.ToolTip = I18n.L("按住区块拖动移动 · 拖右/下边角缩放");
		hintText.M(0.0, 0.0, 10.0, 0.0);
		Grid barGrid = Ui.G(null, "*,Auto");
		barGrid.Add(hintText, 0);
		barGrid.Add(sPanel2, 0, 1);
		_barHost.CornerRadius = new CornerRadius(10.0);
		_barHost.Padding = new Thickness(10.0, 8.0, 10.0, 8.0);
		_barHost.Visibility = Visibility.Collapsed;
		_barHost.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		_barHost.SetResourceReference(Border.BorderBrushProperty, "B.Line");
		_barHost.BorderThickness = new Thickness(1.0);
		_barHost.Child = barGrid;
		Motion.Shadow(_barHost, 16.0, 0.07, 3.0);
		// 根结构:上滚动内容区,下托盘/工具条(仅编辑态占位出现,不悬浮遮挡内容)
		SPanel bottom = Ui.V(8.0, _trayHost, _barHost);
		bottom.Margin = new Thickness(_list.Margin.Left, 8.0, _list.Margin.Right, _list.Margin.Bottom);
		_root = Ui.G("*,Auto");
		_scroll = Ui.Scroll(_list);
		_root.Add(_scroll, 0);
		_root.Add(bottom, 1);
		// grid/canvas 模式的可视宿主:自绘画布 Panel + 其上的对齐辅助线/框选覆盖层(同一格叠放)。
		// 注意:stack(缺省)时这个宿主根本不进可视树 —— 默认渲染与改动前逐像素一致由这一点保证。
		_stage.Children.Add(_box);
		_stage.Children.Add(_guide);
		_boxScroll = Ui.Scroll(_stage);
		// 键盘(§4.G):整个编辑器根元素接管按键 —— Esc / Delete / 方向键 / Ctrl+Z/Y/C/V
		_root.Focusable = true;
		_root.PreviewKeyDown += OnEditorKeyDown;
		_box.MouseLeftButtonDown += OnBoxMouseDown;
		LoadFromStore();
		Render();
		// dev-only:让 --shot 能拍到编辑态(右键菜单是独立 HWND,DevShot 点不到;仓库既有 dev 钩子同风格)。
		// 只有环境变量恰好等于 1 才生效,正常启动零影响。
		if (Environment.GetEnvironmentVariable("TIPCL_DEV_LAYOUT_EDIT") == "1" && CanEdit)
		{
			EnterEdit();
		}
	}

	/// <summary>根可视元素(BlockHost 直接把它作为 Content)。</summary>
	public FrameworkElement Root
	{
		get
		{
			return _root;
		}
	}

	/// <summary>布局发生变更(保存后)时触发;BlockHost 转发成自己的 Changed。</summary>
	public event Action? Changed;

	/// <summary>是否处于编辑态。</summary>
	public bool IsEditing
	{
		get
		{
			return _edit;
		}
	}

	/// <summary>当前页状态(测试与调试用)。</summary>
	public LayoutPageState State
	{
		get
		{
			return _state;
		}
	}

	/// <summary>是否可以进编辑态(≥1 个块就能编辑:单块页也能调外观/标题/密度;L5 设置页的入口按钮直接用这里)。</summary>
	public bool CanEdit
	{
		get
		{
			return _specs.Count >= 1;
		}
	}

	/// <summary>是否正在提交自己的改动(供宿主在 LayoutChanged 处理器里跳过"自己引发的"那一次重排)。</summary>
	internal bool IsSaving
	{
		get
		{
			return _saving;
		}
	}

	/// <summary>重新从真源装载本页并重排(D1):设置页改完布局文档、或整机方案被应用之后,
	/// 已存在的编辑器实例靠它立刻刷新(不重建页面,页面自己的状态不丢)。</summary>
	public void ReloadFromStore()
	{
		LoadFromStore();
		// [layout-fix] 规格 §3-问题7:编辑态里文档被换掉(导入 / 别处保存)之后,临时网格要按新内容重折一次;
		// 已经动过(_gridTouched)就不动用户改出来的网格。
		if (_edit && _autoGrid && !_gridTouched)
		{
			ConvertStackToGrid();
		}
		Render();
	}

	/// <summary>
	/// 宿主喂入"内容区可用宽"(px):窄窗/宽窗变体(§0.1 Q6)据此在 <c>variants.narrow</c>
	/// 与主 body 之间切换。<paramref name="width"/> ≤ 0 = 未知宽 → 不做变体判定。
	/// </summary>
	public void ApplyVariant(double width)
	{
		_widthHint = ((width > 0.0 && !double.IsInfinity(width) && !double.IsNaN(width)) ? width : 0.0);
		ReloadFromStore();
	}

	/// <summary>进入编辑态。</summary>
	public void EnterEdit()
	{
		if (_edit || _specs.Count == 0)
		{
			return;
		}
		_edit = true;
		// 每次进编辑态都是一次新会话:多选与撤销栈清空(历史上的改动已经即时落盘,不需要跨会话撤销)
		_session.ClearHistory();
		_session.ClearSelection();
		// [layout-fix] 规格 §3-问题7:stack 页临时折算成 12 列网格 → 整块都能自由拖
		PrepareFreeDrag();
		// [layout-fix] 规格2-问题2.3:进编辑先压紧一次 —— 旧存档里被放到很靠下、看起来"消失"的区块
		// (录屏里实例库的「版本选择」、原版页的「筛选」)会浮回能看到的位置。**只在编辑态生效**,
		// 不进入编辑就不会改动任何既有网格布局(启动页刻意留出的空白必须保留)。
		CompactState();
		Render();
		_barHost.Visibility = Visibility.Visible;
		// 键盘(§4.G)需要根元素拿到焦点;拿不到也不影响鼠标交互
		try
		{
			_root.Focus();
		}
		catch
		{
		}
	}

	// [layout-fix] 规格 §3-问题7:进编辑态时把 stack 页面临时换成 12 列 grid(按现状逐行折算坐标),
	// 这样整块按住就能上下左右自由拖动、拖边缩放;一次都没动过就在退出时还原。
	private void PrepareFreeDrag()
	{
		_autoGrid = false;
		_gridTouched = false;
		_preEditPage = null;
		if (_specs.Count == 0 || _state?.Page == null || _state.Page.Mode != LayoutSchema.ModeStack)
		{
			return;
		}
		_preEditPage = _state.ToPage();
		_autoGrid = true;
		ConvertStackToGrid();
	}

	// [layout-fix] 规格 §3-问题7:按排布计划**逐行**折算到 12 列网格 —— 两栏 / 三栏的行各块按比例分列位
	// (列宽 = 原列位 × 12 ÷ 原列数),行号就是原来的行号,行高仍自适应(row_h = 0),所以画面看起来不变。
	// 排布计划里没有的块(隐藏块等)排到最后一行之后。
	private void ConvertStackToGrid()
	{
		if (_state?.Page == null || _state.Page.Mode != LayoutSchema.ModeStack)
		{
			return;
		}
		// 折算规则在纯模型里(LayoutEngine.StackToGrid):排布计划的行划分 → 12 栅格的列位 / 列跨度,
		// 行号不变、行高自适应(row_h = 0),所以画面看起来不变。这里只负责"换一份状态"。
		PageLayoutV2 grid = _state.ToPage();
		if (!LayoutEngine.StackToGrid(grid, _pageId))
		{
			return;
		}
		_state = LayoutEngine.LoadState(_pageId, grid, _refs, _state.ExistedInDoc);
	}

	// [layout-fix] 规格2-问题2.3:网格页压紧 —— 消除空洞(旧存档里被拖到很下面、看起来"消失"的区块会浮回可见区域)。
	// 只在编辑过程中被调用(EnterEdit / CommitBox);canvas 模式不参与。
	private void CompactState()
	{
		if (_state?.Page == null || _state.Page.Mode != LayoutSchema.ModeGrid)
		{
			return;
		}
		PageLayoutV2 page = _state.ToPage();
		List<LayoutCell> cells = LayoutGrid.Pack(page);
		// [layout-fix] 参考件v2-对齐:太窄的块(旧存档里被拉到 1 列、文字竖排的)至少放宽到 MinCols 列
		int cols = LayoutGrid.Columns(page);
		int minCols = Math.Min(cols, MinCols(cols));
		foreach (LayoutCell cell in cells)
		{
			if (cell.W < minCols)
			{
				cell.W = minCols;
				if (cell.X + cell.W > cols)
				{
					cell.X = Math.Max(0, cols - cell.W);
				}
			}
		}
		LayoutGrid.Compact(cells, cols, null, null);
		LayoutGrid.Apply(page, cells);
		_state = LayoutEngine.LoadState(_pageId, page, _refs, _state.ExistedInDoc);
	}

	/// <summary>退出编辑态(落盘 + Toast,与旧版一致)。</summary>
	public void ExitEdit()
	{
		if (!_edit)
		{
			return;
		}
		_edit = false;
		if (_autoGrid && !_gridTouched && _preEditPage != null)
		{
			// [layout-fix] 规格 §5「进入编辑后不动任何东西直接「完成编辑」,页面应该和之前完全一样」:
			// 这次只是临时折算成了网格,一个手势都没发生 → 把原来的 stack 放回去,不悄悄改用户的页面。
			_state = LayoutEngine.LoadState(_pageId, _preEditPage, _refs, _state.ExistedInDoc);
		}
		_autoGrid = false;
		_gridTouched = false;
		_preEditPage = null;
		Persist();
		Render();
		_session.ClearSelection();
		_session.ClearHistory();
		_trayHost.Visibility = Visibility.Collapsed;
		_barHost.Visibility = Visibility.Collapsed;
		AppServices.Window?.Toast(I18n.L("布局已保存"), "", ToastKind.Success);
	}

	/// <summary>按主题刷新编辑态蒙版。</summary>
	public void RefreshThemeVisuals()
	{
		foreach (LayoutCard value in _cards.Values)
		{
			value.RefreshTint();
		}
	}

	// ————— 装载 / 渲染 —————

	// 真源 = ui_layout_v2.json(缺失则回落到 v1 子集;都没有就是白名单默认布局)
	private void LoadFromStore()
	{
		LayoutV2 doc = LayoutStoreV2.Load();
		// 窄窗/宽窗变体(D1):可用宽低于阈值且 variants.narrow 里有这一页 → 用变体那一份,
		// 否则回落主 body(PageOf 内部就是这个回落规则)。宽度未知(≤0)时恒等于主 body,
		// 所以"没接线 / 首帧"的渲染与改动前完全一致。
		double width = ((_widthHint > 0.0) ? _widthHint : LayoutVariantHost.Width);
		PageLayoutV2? page = LayoutVariants.PageOf(doc.Layout, _pageId, width, LayoutVariantHost.Threshold);
		// 全局密度也来自 v2(缺省 standard → 间距档 10 = 现状)
		_density = ((doc.Layout?.Shell != null && LayoutSchema.IsKnownDensity(doc.Layout.Shell.Density)) ? doc.Layout.Shell.Density : LayoutSchema.DensityStandard);
		// [layout-fix] 规格 §4-能力2:真源里没有这一页时用该页的 defaultPage 兜底(实例库的默认左右排布就靠它)。
		// 注意 ExistedInDoc 要如实传 false —— 兜底布局只是"读到空气时的默认值",没有被写进任何文件。
		bool existed = page != null;
		PageLayoutV2? effective = (page ?? _defaultPage);
		_state = LayoutEngine.LoadState(_pageId, ((effective != null) ? effective.Clone() : null), _refs, existed);
	}

	// 当前是否 grid / canvas 模式(stack = 缺省,走与改动前完全相同的纵向流)
	private bool IsBoxMode
	{
		get
		{
			return _state.Page.Mode == LayoutSchema.ModeGrid || _state.Page.Mode == LayoutSchema.ModeCanvas;
		}
	}

	// 单列走与旧版完全相同的纵向流(每块一张卡直接挂 SPanel);多列才用行内 Grid
	private void Render()
	{
		_list.Children.Clear();
		LayoutPlan plan = _state.Plan();
		if (IsBoxMode)
		{
			// grid / canvas:整体交给画布 Panel(自绘 + 两趟测量);stack 的纵向流一行都不执行
			RenderBox();
			if (_edit)
			{
				RefreshTray();
			}
			RefreshToolbar();
			return;
		}
		ShowStackHost();
		_list.Spacing = LayoutAppearance.EffectiveGap(_density, plan.Gap);
		_list.Margin = (_marginOverride ?? PadOf(plan));
		if (plan.IsSingleColumn)
		{
			foreach (LayoutRow row in plan.Rows)
			{
				if (row.Slots.Count != 0)
				{
					LayoutCard card = CardFor(row.Slots[0].Id);
					card.SetBoxMode(LayoutSchema.ModeStack);
					card.SetEdit(_edit);
					// [layout-fix] 参考件v2-对齐:stack 一律"跟随内容高度"(不进块内滚动)
					card.SetFixedHeight(false);
					ApplyLook(card);
					_list.Children.Add(card);
				}
			}
		}
		else
		{
			foreach (LayoutRow row in plan.Rows)
			{
				_list.Children.Add(BuildRow(row, plan));
			}
		}
		if (_edit)
		{
			RefreshTray();
		}
		RefreshToolbar();
	}

	// grid / canvas 渲染:子元素按 z 序叠放(canvas 用),间距/边距走同一套密度与 pad 规则
	private void RenderBox()
	{
		ShowBoxHost();
		PageLayoutV2 page = _state.Page;
		double gap = LayoutAppearance.EffectiveGap(_density, page.Gap);
		_box.SetLayout(page.Mode, LayoutGrid.Columns(page), page.RowH, gap, page);
		_box.SnapStep = _snapStep;
		_box.EditMode = _edit;
		_box.Margin = (_marginOverride ?? PadOf(_state.Plan()));
		_box.Children.Clear();
		foreach (LayoutCell cell in LayoutCanvas.StackedOrder(_box.Cells))
		{
			LayoutCard card = CardFor(cell.Id);
			card.SetBoxMode(page.Mode);
			card.SetEdit(_edit);
			// [layout-fix] 参考件v2-对齐:只有"grid + 自动行高 + 有固定像素高"的块才进块内滚动
			card.SetFixedHeight(page.Mode == LayoutSchema.ModeGrid && page.RowH <= 0.0 && cell.HeightPx > 0.0);
			card.SetSelected(_edit && _session.IsSelected(cell.Id));
			ApplyLook(card);
			_box.Children.Add(card);
		}
		_box.Reflow();
	}

	// 可视宿主二选一:stack = SPanel 纵向流(与 L1 逐像素一致),grid/canvas = 画布 Panel
	private void ShowStackHost()
	{
		if (_root.Children.Count != 0 && ReferenceEquals(_root.Children[0], _scroll))
		{
			return;
		}
		_root.Children.RemoveAt(0);
		Grid.SetRow(_scroll, 0);
		_root.Children.Insert(0, _scroll);
		_guide.Clear();
	}

	private void ShowBoxHost()
	{
		if (_root.Children.Count != 0 && ReferenceEquals(_root.Children[0], _boxScroll))
		{
			return;
		}
		_root.Children.RemoveAt(0);
		Grid.SetRow(_boxScroll, 0);
		_root.Children.Insert(0, _boxScroll);
		_list.Children.Clear();
	}

	// 工具条上的撤销/重做/吸附档可用性
	private void RefreshToolbar()
	{
		if (_undoBtn != null)
		{
			_undoBtn.IsEnabled = _session.CanUndo;
		}
		if (_redoBtn != null)
		{
			_redoBtn.IsEnabled = _session.CanRedo;
		}
		if (_snapBtn != null)
		{
			_snapBtn.IsEnabled = IsBoxMode;
			_snapBtn.Content = I18n.L("吸附") + " " + _snapStep.ToString("0");
		}
	}

	// 吸附档循环:0 → 4 → 8 → 16 → 24 → 0(与旧 DashLayout.Grid 的档位表一致)
	private void CycleSnapStep()
	{
		double[] steps = LayoutCanvas.Steps;
		int index = 0;
		for (int i = 0; i < steps.Length; i++)
		{
			if (Math.Abs(steps[i] - _snapStep) < 0.001)
			{
				index = i;
				break;
			}
		}
		_snapStep = steps[(index + 1) % steps.Length];
		_box.SnapStep = _snapStep;
		_box.InvalidateVisual();
		RefreshToolbar();
	}

	// 区块外观参数(§4.B)落到卡上:style / 常态标题 / 每块边距;缺省(plain + false + null)= 现状零装饰
	private void ApplyLook(LayoutCard card)
	{
		BlockLayout? block = BlockOf(card.Spec.Id);
		card.SetLook(LayoutAppearance.StyleOf(block), LayoutAppearance.ShowsTitle(block), ((block != null) ? block.Pad : null));
	}

	// 取某块在本页 v2 条目里的字段(style / title / pad 的来源)
	private BlockLayout? BlockOf(string id)
	{
		if (_state.Page?.Blocks != null && !string.IsNullOrEmpty(id))
		{
			foreach (BlockLayout block in _state.Page.Blocks)
			{
				if (block != null && string.Equals(block.Id, id, StringComparison.Ordinal))
				{
					return block;
				}
			}
		}
		return null;
	}

	// 多列行:按 plan.Columns 平分列宽,块占 span 列;列间留 gap(最后一块不留)
	private FrameworkElement BuildRow(LayoutRow row, LayoutPlan plan)
	{
		int columns = Math.Max(1, plan.Columns);
		Grid grid = Ui.G(null, null);
		for (int i = 0; i < columns; i++)
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(1.0, GridUnitType.Star)
			});
		}
		for (int k = 0; k < row.Slots.Count; k++)
		{
			LayoutSlot slot = row.Slots[k];
			LayoutCard card = CardFor(slot.Id);
			card.SetBoxMode(LayoutSchema.ModeStack);
			card.SetEdit(_edit);
			ApplyLook(card);
			int column = LayoutSchema.ClampInt(slot.Column, 0, columns - 1);
			Grid.SetColumn(card, column);
			Grid.SetColumnSpan(card, LayoutSchema.ClampInt(slot.Span, 1, columns - column));
			card.Margin = new Thickness(0.0, 0.0, ((k == row.Slots.Count - 1) ? 0.0 : plan.Gap), 0.0);
			grid.Children.Add(card);
		}
		return grid;
	}

	private Thickness PadOf(LayoutPlan plan)
	{
		double[] pad = ((plan != null) ? plan.Pad : null) ?? LayoutSchema.PadDefaultCopy();
		return new Thickness(pad[0], pad[1], pad[2], pad[3]);
	}

	// 卡片常驻:内容在这里解析(spec.Content → 内容工厂 → 「区块不可用」占位)
	private LayoutCard CardFor(string id)
	{
		if (_cards.TryGetValue(id, out LayoutCard? card))
		{
			return card;
		}
		BlockSpec spec = SpecOf(id);
		card = new LayoutCard(this, spec, ResolveContent(spec));
		_cards[id] = card;
		return card;
	}

	// 页面 spec 优先;布局文档里的 custom: / plugin: 块可能没有对应 spec(§4.F),用 id 兜一个标题
	private BlockSpec SpecOf(string id)
	{
		if (_byId.TryGetValue(id, out BlockSpec? spec))
		{
			return spec;
		}
		BlockSpec synthetic = new BlockSpec
		{
			Id = id,
			Title = id
		};
		string? widget = LayoutEngine.WidgetIdOf(id);
		if (widget != null)
		{
			synthetic.Title = widget;
		}
		else if (LayoutEngine.TryParsePluginId(id, out string plugin, out string inner))
		{
			synthetic.Title = plugin + " · " + inner;
		}
		return synthetic;
	}

	// 内容解析:内置块保持旧行为(没给内容就是没内容);custom:/plugin: 查注册表,查不到给「不可用」占位
	private UIElement? ResolveContent(BlockSpec spec)
	{
		if (spec.Content != null)
		{
			return spec.Content;
		}
		if (LayoutEngine.Classify(spec.Id) == LayoutContentKind.Builtin)
		{
			return null;
		}
		object? built = LayoutContentRegistry.Build(spec.Id, _pageId);
		if (built is UIElement element)
		{
			return element;
		}
		return Unavailable(spec);
	}

	// §4.F:插件卸载 / 自建块类型缺失时数据保留、只显示「不可用」(照抄 PluginDashCards 的既有语义)
	private static UIElement Unavailable(BlockSpec spec)
	{
		// 注意:「区块不可用」是 L1 新串,en/zh_CN 词表补键由 L6/L8 负责(本包写范围不含 locales)
		return Ui.V(4.0, Ui.Muted(I18n.L("区块不可用")), Ui.Muted(spec.Title ?? "").Wrap()).M(10.0);
	}

	// ————— 编辑操作 —————

	private void RefreshTray()
	{
		_tray.Children.Clear();
		foreach (string id in _state.Order)
		{
			if (!_state.Hidden.Contains(id))
			{
				continue;
			}
			BlockSpec spec = SpecOf(id);
			Button button = Ui.Btn(spec.Title, BtnKind.Chip, delegate
			{
				Restore(spec);
			}, Ico.Add);
			button.M(0.0, 0.0, 8.0, 6.0);
			_tray.Children.Add(button);
		}
		_trayHost.Visibility = ((_edit && _tray.Children.Count > 0) ? Visibility.Visible : Visibility.Collapsed);
	}

	internal void HideBlock(BlockSpec spec)
	{
		if (!_edit || spec == null || _state.Hidden.Contains(spec.Id))
		{
			return;
		}
		_session.PushUndo(_state.ToPage());
		_state.Hide(spec.Id);
		Render();
		Persist();
	}

	// 从托盘恢复:隐藏项一直留在原槽位,插回原位置对应可视索引
	private void Restore(BlockSpec spec)
	{
		if (!_edit || spec == null || !_state.Hidden.Contains(spec.Id))
		{
			return;
		}
		// [layout-fix] 规格 §3-问题7:恢复也算"动过"(退出时保留网格,不再还原成 stack)
		_gridTouched = true;
		_session.PushUndo(_state.ToPage());
		_state.Restore(spec.Id);
		Render();
		Persist();
	}

	// 重置本页 = 回到该页出厂布局(含默认模式)并落盘。
	// [layout-fix] 规格 §3-问题7 + 参考件v2-对齐:对外公开(壳层右键菜单的「重置本页布局」也走它),
	// 出错时给一句 Toast 而不是把异常抛到 UI 线程外面。
	public void ResetPage()
	{
		try
		{
			ResetPageCore();
		}
		catch (Exception ex)
		{
			AppServices.Window?.Toast(I18n.L("重置失败"), ex.Message, ToastKind.Error);
		}
	}

	private void ResetPageCore()
	{
		// 手势进行中先收干净(否则松手时会把旧坐标写回去)
		if (_gesture.Length != 0)
		{
			EndBoxGesture(false, false);
		}
		if (_box.IsMouseCaptured)
		{
			_box.ReleaseMouseCapture();
		}
		_session.PushUndo(_state.ToPage());
		_state = LayoutEngine.LoadState(_pageId, ((_defaultPage != null) ? _defaultPage.Clone() : null), _refs, false);
		_autoGrid = false;
		_gridTouched = false;
		_preEditPage = null;
		// 折叠只是编辑时的临时视图,重置时一并展开
		foreach (LayoutCard card in _cards.Values)
		{
			card.SetFolded(false);
		}
		Persist();
		if (_edit)
		{
			PrepareFreeDrag();
		}
		RefreshFoldAllBtn();
		Render();
		AppServices.Window?.Toast(I18n.L("布局已重置"), I18n.L("本页已恢复默认布局"), ToastKind.Success);
	}

	// ————— 页面自己调整布局用的公开口子([layout-fix] 规格 §4-能力2) —————

	/// <summary>
	/// [layout-fix] 规格 §4-能力2:不进编辑态直接切换本页某个块的显隐(实例库右键菜单用它)。
	/// </summary>
	public void SetBlockHidden(string id, bool hidden)
	{
		if (string.IsNullOrEmpty(id) || !_state.Order.Contains(id))
		{
			return;
		}
		if (hidden)
		{
			_state.Hide(id);
		}
		else
		{
			_state.Restore(id);
		}
		Persist();
		Render();
	}

	/// <summary>
	/// [layout-fix] 规格 §4-能力2:把本页 v2 条目交给页面自己改一次(改页级参数或某块的几何),
	/// 改完立刻落盘并重排。实例库的「显示文件夹栏」用它一次改完"显隐 + 版本选择的列位"。
	/// </summary>
	public void MutatePage(Action<PageLayoutV2> mutate)
	{
		if (mutate == null)
		{
			return;
		}
		PageLayoutV2 page = _state.ToPage();
		mutate(page);
		_state = LayoutEngine.LoadState(_pageId, page, _refs, _state.ExistedInDoc);
		Persist();
		Render();
	}

	// 导入:PageLayoutIO 仍按 v1 语义写盘(整机 v2 方案导入由 L5 做);这里把 v1 的顺序/显隐并进 v2,
	// 其它 v2 字段按块 id 保留 —— 既保持旧行为,也不丢 v2 专属字段。
	private void ImportLayout()
	{
		int? num = PageLayoutIO.ImportFromFileDialog();
		if (num == null)
		{
			return;
		}
		PageLayoutStore.Reload();
		LayoutV2 doc = LayoutStoreV2.Load();
		foreach (KeyValuePair<string, PageLayoutDoc> kv in PageLayoutStore.LoadAll())
		{
			if (string.IsNullOrEmpty(kv.Key) || kv.Value == null)
			{
				continue;
			}
			PageLayoutV2? existing = null;
			if (doc.Layout.Pages.TryGetValue(kv.Key, out PageLayoutV2? stored))
			{
				existing = stored;
			}
			LayoutEngine.ApplyPage(doc, kv.Key, LayoutEngine.MergeV1(existing, kv.Value));
		}
		LayoutStoreV2.Save(doc);
		PageLayoutStore.Reload();
		LoadFromStore();
		Render();
		this.Changed?.Invoke();
	}

	// ————— 持久化 —————

	// 任何变更 → 取整份 v2 文档 → 覆盖本页 → Save(v2 + 写通 v1)→ 清 v1 缓存 → Changed
	// _saving 守卫:Save 会同步触发 LayoutChanged,自己不用被自己触发的那一次再重排一遍
	private void Persist()
	{
		// [layout-fix] 规格 §3-问题7:编辑期间的任何一次落盘都算"这次编辑动过" ——
		// 退出时据此决定是保留网格还是还原原来的 stack(见 ExitEdit)。
		if (_edit)
		{
			_gridTouched = true;
		}
		_saving = true;
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			LayoutEngine.ApplyPage(doc, _pageId, _state.ToPage());
			LayoutStoreV2.Save(doc);
			PageLayoutStore.Reload();
		}
		finally
		{
			_saving = false;
		}
		this.Changed?.Invoke();
	}

	// 操作条上的拖拽手柄:stack 模式 = 一维换序(旧行为),grid/canvas = 画布整体拖拽
	internal void BeginHandleDrag(LayoutCard card, UIElement src, MouseButtonEventArgs e)
	{
		if (IsBoxMode)
		{
			BeginBoxDrag(card, e);
			return;
		}
		BeginDrag(card, src, e);
	}

	// ————— 拖拽重排(编辑态) —————

	// 手柄按下:缓存各可视块高度,捕获鼠标,之后 MouseMove 按相邻块中点实时交换。
	// 不做动画插值,交换即落位——Motion.Enabled=false 也完全一致。
	internal void BeginDrag(LayoutCard card, UIElement src, MouseButtonEventArgs e)
	{
		if (!_edit || _dragCard != null || card == null)
		{
			return;
		}
		_dragCard = card;
		_dragHeights.Clear();
		foreach (UIElement child in _list.Children)
		{
			if (child is LayoutCard element && element.ActualHeight > 1.0)
			{
				_dragHeights[element.Spec.Id] = element.ActualHeight;
			}
		}
		card.Opacity = 0.75;
		src.CaptureMouse();
		src.MouseMove += OnDragMove;
		src.MouseLeftButtonUp += OnDragEnd;
		src.LostMouseCapture += OnDragLost;
		e.Handled = true;
	}

	private void OnDragMove(object sender, MouseEventArgs e)
	{
		LayoutCard card = _dragCard;
		if (card == null || e.LeftButton != MouseButtonState.Pressed)
		{
			return;
		}
		string id = card.Spec.Id;
		int before = _state.VisibleIndexOf(id);
		if (before < 0)
		{
			return;
		}
		double y = e.GetPosition(_list).Y;
		// 一次移动可能跨多格:纯模型层按"相邻块中点"逐格比较(算法移植自旧 BlockHost.OnDragMove)
		_state.DragReorder(id, y, _dragHeights);
		if (_state.VisibleIndexOf(id) != before)
		{
			InsertVisual(id);
		}
	}

	private void OnDragEnd(object sender, MouseButtonEventArgs e)
	{
		EndDrag((UIElement)sender);
	}

	private void OnDragLost(object sender, MouseEventArgs e)
	{
		EndDrag((UIElement)sender);
	}

	private void EndDrag(UIElement src)
	{
		LayoutCard card = _dragCard;
		_dragCard = null;
		src.MouseMove -= OnDragMove;
		src.MouseLeftButtonUp -= OnDragEnd;
		src.LostMouseCapture -= OnDragLost;
		if (src.IsMouseCaptured)
		{
			src.ReleaseMouseCapture();
		}
		if (card == null)
		{
			return;
		}
		card.Opacity = 1.0;
		Persist();
	}

	// 把某块插回可视列表的正确位置(单列时的增量搬家;多列直接整体重排)
	private void InsertVisual(string id)
	{
		LayoutPlan plan = _state.Plan();
		if (!plan.IsSingleColumn)
		{
			Render();
			return;
		}
		if (!_cards.TryGetValue(id, out LayoutCard? card))
		{
			return;
		}
		int index = plan.VisualIndexOf(id);
		if (index < 0)
		{
			return;
		}
		_list.Children.Remove(card);
		_list.Children.Insert(Math.Min(index, _list.Children.Count), card);
		card.SetEdit(_edit);
	}

	// ————————————— L2:画布手势 / 多选 / 撤销重做 / 键盘 —————————————
	//
	// 分工:判定与几何全在纯模型(LayoutEditSession / LayoutGrid / LayoutCanvas / LayoutGuides),
	// 这里只做"事件 → 意图 → 调模型 → 重画"。所有会改布局的操作都先 PushUndo(内存快照),
	// 但**落盘仍然是即时的**(每次手势结束就 Save)—— 崩溃安全优先于批量提交(§4.G / R6)。

	/// <summary>画布可用宽(px)。</summary>
	private double AxisWidth()
	{
		return Math.Max(1.0, _box.ActualWidth);
	}

	/// <summary>画布可用高(px);拿不到有限高时用画布参考高(与 Panel 的兜底一致)。</summary>
	private double AxisHeight()
	{
		double h = _box.ActualHeight;
		return ((h > 1.0) ? h : ((_box.CanvasHeight > 0.0) ? _box.CanvasHeight : LayoutCanvas.RefHeight));
	}

	// 起手:压一份撤销快照 + 记下所有选中块的起始槽位(用绝对值算位移,避免逐帧累积误差)
	private void StartGesture(string kind, LayoutCard card, MouseButtonEventArgs e)
	{
		// [layout-fix] 规格2-问题2.2:先留"全部槽位 + 行高"的快照 ——
		// 之后每一帧都从这份快照重算,让位不累积;行高快照用来把鼠标位置折算成行号。
		_gestureAll.Clear();
		foreach (LayoutCell cell in _box.Cells)
		{
			if (cell != null && !string.IsNullOrEmpty(cell.Id))
			{
				_gestureAll[cell.Id] = cell.Clone();
			}
		}
		_gestureRows = ((double[])(_box.RowHeightValues?.Clone() ?? new double[0]));
		_session.PushUndo(_state.ToPage());
		_gesture = kind;
		_gestureHandle = "";
		_gestureStart = e.GetPosition(_box);
		_gestureCells.Clear();
		foreach (string id in _session.OrderedSelection(_state.Order))
		{
			LayoutCell? cell2 = _box.CellOf(id);
			if (cell2 != null)
			{
				_gestureCells[id] = cell2.Clone();
			}
		}
		if (_gestureCells.Count == 0)
		{
			LayoutCell? own = _box.CellOf(card.Spec.Id);
			if (own != null)
			{
				_gestureCells[card.Spec.Id] = own.Clone();
			}
		}
		_gestureAnchor = (_box.RectOf(card.Spec.Id) ?? default(LayoutRect));
		_box.CaptureMouse();
		// [layout-fix] 参考件v2-对齐:贴近边缘拖动时自动滚动画布
		StartAutoScroll();
		_box.MouseMove += OnBoxDragMove;
		_box.MouseLeftButtonUp += OnBoxGestureEnd;
		_box.LostMouseCapture += OnBoxGestureLost;
	}

	// 整块拖拽(Ctrl = 只改选择;多选时整体移动)
	internal void BeginBoxDrag(LayoutCard card, MouseButtonEventArgs e)
	{
		if (!_edit || !IsBoxMode || _gesture.Length != 0 || card == null)
		{
			return;
		}
		if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != ModifierKeys.None)
		{
			_session.Toggle(card.Spec.Id);
			UpdateSelectionVisual();
			e.Handled = true;
			return;
		}
		if (!_session.IsSelected(card.Spec.Id))
		{
			_session.Select(card.Spec.Id, false);
		}
		StartGesture("move", card, e);
		UpdateSelectionVisual();
		e.Handled = true;
	}

	// 手柄缩放(8 向;grid 只用 e / s / se)
	internal void BeginBoxResize(LayoutCard card, string handle, MouseButtonEventArgs e)
	{
		if (!_edit || !IsBoxMode || _gesture.Length != 0 || card == null || string.IsNullOrEmpty(handle))
		{
			return;
		}
		_session.Select(card.Spec.Id, false);
		StartGesture("resize", card, e);
		_gestureHandle = handle;
		UpdateSelectionVisual();
		e.Handled = true;
	}

	// 画布空白处按下 = 框选开始(点在块上时块的处理器已经把事件吞了)
	private void OnBoxMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (!_edit || !IsBoxMode || _gesture.Length != 0)
		{
			return;
		}
		if (_box.HitId(e.GetPosition(_box)) != null)
		{
			return;
		}
		_session.ClearSelection();
		UpdateSelectionVisual();
		_gesture = "marquee";
		_gestureStart = e.GetPosition(_box);
		_box.CaptureMouse();
		_box.MouseMove += OnBoxMarqueeMove;
		_box.MouseLeftButtonUp += OnBoxGestureEnd;
		_box.LostMouseCapture += OnBoxGestureLost;
		e.Handled = true;
	}

	private void OnBoxMarqueeMove(object sender, MouseEventArgs e)
	{
		if (_gesture != "marquee" || e.LeftButton != MouseButtonState.Pressed)
		{
			return;
		}
		Point point = e.GetPosition(_box);
		LayoutRect marquee = new LayoutRect(Math.Min(_gestureStart.X, point.X), Math.Min(_gestureStart.Y, point.Y), Math.Abs(point.X - _gestureStart.X), Math.Abs(point.Y - _gestureStart.Y));
		_guide.ShowMarquee(marquee);
		_session.SelectMany(_box.IdsIn(marquee));
		UpdateSelectionVisual();
	}

	private void OnBoxDragMove(object sender, MouseEventArgs e)
	{
		if (_gesture.Length == 0 || _gesture == "marquee" || e.LeftButton != MouseButtonState.Pressed)
		{
			return;
		}
		ApplyPointer(e.GetPosition(_box));
	}

	// [layout-fix] 参考件v2-对齐:手势位移 → 意图 → 重排(自动滚动也用同一入口按当前指针重算一次)
	private void ApplyPointer(Point point)
	{
		if (_gesture.Length == 0 || _gesture == "marquee")
		{
			return;
		}
		_lastPointer = point;
		double dx = point.X - _gestureStart.X;
		double dy = point.Y - _gestureStart.Y;
		if (_box.IsCanvas)
		{
			ApplyCanvasGesture(dx, dy);
		}
		else
		{
			ApplyGridGesture(dx, dy);
		}
		_box.Reflow();
	}

	private void OnBoxGestureEnd(object sender, MouseButtonEventArgs e)
	{
		EndBoxGesture(true, false);
	}

	private void OnBoxGestureLost(object sender, MouseEventArgs e)
	{
		EndBoxGesture(false, false);
	}

	// 手势收尾:解绑事件 → (取消?回滚:落盘)→ 重画
	private void EndBoxGesture(bool persist, bool rollback)
	{
		bool active = _gesture.Length != 0;
		// [layout-fix] 规格 §3-问题7:拖 / 缩放过的块要"优先占住落点"(PromoteToFront),
		// 先把它们记下来 —— 下面立刻会清掉 _gesture / _gestureCells,清完就问不出来了。
		bool movedSomething = _gesture == "move" || _gesture == "resize";
		List<string> touchedIds = new List<string>(_gestureCells.Keys);
		_autoScroll?.Stop();
		_gesture = "";
		_gestureHandle = "";
		_gestureCells.Clear();
		_box.MouseMove -= OnBoxDragMove;
		_box.MouseMove -= OnBoxMarqueeMove;
		_box.MouseLeftButtonUp -= OnBoxGestureEnd;
		_box.LostMouseCapture -= OnBoxGestureLost;
		if (_box.IsMouseCaptured)
		{
			_box.ReleaseMouseCapture();
		}
		_guide.Clear();
		if (!active)
		{
			return;
		}
		if (rollback)
		{
			PageLayoutV2? back = _session.DiscardUndo();
			if (back != null)
			{
				_state = LayoutEngine.LoadState(_pageId, back, _refs);
				_session.Retain(_state.Order);
			}
			Render();
			return;
		}
		if (persist)
		{
			CommitBox(movedSomething ? touchedIds : null);
		}
		else
		{
			Render();
		}
	}

	// 画布槽位 → 页面块 → v2(唯一入口 LayoutStoreV2)并落盘
	private void CommitBox(List<string>? touchedIds = null)
	{
		PageLayoutV2 page = _state.ToPage();
		_box.ApplyTo(page);
		_state = LayoutEngine.LoadState(_pageId, page, _refs);
		if (touchedIds != null && touchedIds.Count > 0)
		{
			// [layout-fix] 规格 §3-问题7:拖到别人位置时,被拖的区块优先占住落点,其余区块自动往下让位。
			// grid 的自动流按块表顺序抢格子(见 LayoutPageState.PromoteToFront),所以把它们挪到最前面。
			_state.PromoteToFront(touchedIds);
			_gridTouched = true;
		}
		// [layout-fix] 规格2-问题2.3:松手后再压紧一次再保存(拖动过程中已经实时让位,这一步抹掉残留空洞)
		CompactState();
		_session.Retain(_state.Order);
		Persist();
		Render();
	}

	// canvas:整体拖拽 / 8 向缩放 + 对齐辅助线(阈值 4px 磁性吸附)
	private void ApplyCanvasGesture(double dx, double dy)
	{
		double width = AxisWidth();
		double height = AxisHeight();
		double step = LayoutCanvas.StepOf(_snapStep);
		string? primary = PrimaryId();
		foreach (KeyValuePair<string, LayoutCell> kv in _gestureCells)
		{
			LayoutCell? cell = _box.CellOf(kv.Key);
			if (cell == null)
			{
				continue;
			}
			LayoutCell start = kv.Value;
			cell.NX = start.NX;
			cell.NY = start.NY;
			cell.NW = start.NW;
			cell.NH = start.NH;
			if (_gesture == "resize")
			{
				LayoutCanvas.Resize(cell, _gestureHandle, dx, dy, width, height, step);
			}
			else
			{
				LayoutCanvas.Move(cell, dx, dy, width, height, step);
			}
		}
		LayoutRect? rect = CanvasRectOf(_box.CellOf(primary));
		if (primary != null && rect.HasValue)
		{
			LayoutGuideResult guides = LayoutGuides.Align(rect.Value, OtherRects(primary, true), LayoutGuides.Threshold);
			if (_gesture == "move" && guides.HasSnap)
			{
				// 磁性吸附:整组跟着一起吸(差量对所有选中块相同)
				foreach (KeyValuePair<string, LayoutCell> kv2 in _gestureCells)
				{
					LayoutCell? cell2 = _box.CellOf(kv2.Key);
					if (cell2 != null)
					{
						LayoutCanvas.Move(cell2, guides.Dx, guides.Dy, width, height, 0.0);
					}
				}
			}
			_guide.ShowLines(guides.Lines);
		}
		else
		{
			_guide.Clear();
		}
	}

	// [layout-fix] 规格2-问题2.2(重写网格拖动 / 缩放):
	//  - 列 = 卡片左上角跟随鼠标后的最近列;行 = 鼠标指针所在的行(按手势开始时的行高快照算),
	//    不再用"卡片自身高度"当行步长(高卡片几乎拖不动、矮卡片一动就跳好几行的根源)。
	//  - 每次移动都从全部槽位快照恢复,再以被拖块为固定点做一次压紧:其他块自动让位 / 上浮,不留空洞;
	//    同一行里被占位的块优先换到被拖块原来的列(左右互换)。
	//  - 对齐辅助线在 grid 模式去掉:格子吸附已经足够,辅助线吸附会和让位逻辑互相打架(canvas 模式仍保留)。
	private void ApplyGridGesture(double dx, double dy)
	{		int cols = LayoutGrid.Columns(_state.Page);
		double columnStep = Math.Max(1.0, _box.ColumnWidthValue + _box.Gap);
		string? primary = PrimaryId();
		if (primary == null || !_gestureCells.TryGetValue(primary, out LayoutCell? start))
		{
			return;
		}
		// ① 全部槽位回到起手时的样子(让位不累积)
		foreach (KeyValuePair<string, LayoutCell> kv in _gestureAll)
		{
			LayoutCell? cell = _box.CellOf(kv.Key);
			if (cell != null)
			{
				cell.X = kv.Value.X;
				cell.Y = kv.Value.Y;
				cell.W = kv.Value.W;
				cell.H = kv.Value.H;
			}
		}
		Dictionary<string, int>? swap = null;
		if (_gesture == "resize")
		{
			// [layout-fix] 规格2-问题2.2 + 参考件v2-对齐:宽度按列改;高度在**自动行高**的页面(由 stack 转来的都是)
			// 直接记**像素高度** HeightPx —— 原来按"行"改 H,可行高本来就跟着内容走,所以怎么拉高度都不变
			// (这正是录屏里"拉不了大小"的成因)。固定行高(row_h > 0)的页面仍然按行改。
			bool autoRows = _state.Page.RowH <= 0.0;
			int dr = (autoRows ? 0 : (int)Math.Round(dy / Math.Max(1.0, _state.Page.RowH + _box.Gap)));
			int dc = (int)Math.Round(dx / columnStep);
			int minCols = Math.Min(cols, MinCols(cols));
			foreach (KeyValuePair<string, LayoutCell> kv in _gestureCells)
			{
				LayoutCell? cell = _box.CellOf(kv.Key);
				if (cell == null)
				{
					continue;
				}
				LayoutGrid.ResizeCell(cell, _gestureHandle, dc, dr, cols);
				// 太窄的块(旧存档里被拉到 1 列、文字竖排的)至少放宽到 MinCols 列
				if (cell.W < minCols)
				{
					if (_gestureHandle.IndexOf('w') >= 0)
					{
						cell.X = Math.Max(0, cell.X + cell.W - minCols);
					}
					cell.W = minCols;
					if (cell.X + cell.W > cols)
					{
						cell.X = Math.Max(0, cols - cell.W);
					}
				}
				if (autoRows && _gestureHandle.IndexOf('s') >= 0)
				{
					double startH = (string.Equals(kv.Key, primary, StringComparison.Ordinal) ? _gestureAnchor.H : (_box.RectOf(kv.Key)?.H ?? _gestureAnchor.H));
					double step = Math.Max(1.0, _snapStep);
					double px = Math.Round(Math.Max(MinBlockPx, startH + dy) / step) * step;
					cell.HeightPx = Math.Max(MinBlockPx, px);
					CardFor(kv.Key).SetFixedHeight(true);
				}
			}
		}
		else
		{
			// 列:卡片左上角跟随鼠标 → 最近的列
			int nx = (int)Math.Round((_gestureAnchor.X + dx) / columnStep);
			nx = Math.Max(0, Math.Min(cols - start.W, nx));
			// 行:鼠标指针落在第几行(带半个间距的容差)
			int ny = LayoutGrid.RowAt(_gestureRows, _gestureStart.Y + dy, _box.Gap);
			int dcol = nx - start.X;
			int drow = ny - start.Y;
			foreach (KeyValuePair<string, LayoutCell> kv in _gestureCells)
			{
				LayoutCell? cell = _box.CellOf(kv.Key);
				if (cell != null)
				{
					cell.X = Math.Max(0, Math.Min(cols - cell.W, kv.Value.X + dcol));
					cell.Y = Math.Max(0, kv.Value.Y + drow);
				}
			}
			// 其余块的"备选列" = 被拖块**原来的**列:那样能放得更高就换过去(左右互换)
			swap = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, LayoutCell> kv in _gestureAll)
			{
				if (!_gestureCells.ContainsKey(kv.Key))
				{
					swap[kv.Key] = start.X;
				}
			}
		}
		// ② 钉住被拖 / 缩放中的块,其余块实时让位(重力上浮 + 左右互换)
		_box.CompactCells(new HashSet<string>(_gestureCells.Keys, StringComparer.Ordinal), swap);
		_guide.Clear();
	}

	// 手势主角:多选时取选择里的第一块(它带动整组)
	// [layout-fix] 参考件v2-对齐:块的最小像素高与最小列数(再窄内容就没法看了)
	private const double MinBlockPx = 56.0;

	/// <summary>[layout-fix] 参考件v2-对齐:最窄 2 列(12 列网格下约 1/6 宽)。</summary>
	private static int MinCols(int cols)
	{
		return ((cols >= 6) ? 2 : 1);
	}

	// [layout-fix] 参考件v2-对齐:拖动 / 缩放时贴近可视区上下边缘 → 自动滚动画布
	private void StartAutoScroll()
	{
		if (_autoScroll == null)
		{
			_autoScroll = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Input)
			{
				Interval = TimeSpan.FromMilliseconds(30.0)
			};
			_autoScroll.Tick += OnAutoScrollTick;
		}
		_autoScroll.Start();
	}

	private void OnAutoScrollTick(object? sender, EventArgs e)
	{
		if (_gesture.Length == 0 || _gesture == "marquee" || Mouse.LeftButton != MouseButtonState.Pressed || _boxScroll == null)
		{
			_autoScroll?.Stop();
			return;
		}
		Point point = Mouse.GetPosition(_boxScroll);
		double height = _boxScroll.ActualHeight;
		double delta = 0.0;
		if (point.Y > height - 48.0)
		{
			delta = Math.Min(28.0, (point.Y - (height - 48.0)) * 0.6 + 4.0);
		}
		else if (point.Y < 48.0)
		{
			delta = 0.0 - Math.Min(28.0, (48.0 - point.Y) * 0.6 + 4.0);
		}
		if (delta == 0.0)
		{
			return;
		}
		double before = _boxScroll.VerticalOffset;
		_boxScroll.ScrollToVerticalOffset(Math.Max(0.0, before + delta));
		_boxScroll.UpdateLayout();
		if (Math.Abs(_boxScroll.VerticalOffset - before) > 0.1)
		{
			ApplyPointer(Mouse.GetPosition(_box));
		}
	}

	// [layout-fix] 参考件v2-对齐:卡片折叠 / 展开之后重新排版
	internal void OnCardFoldChanged()
	{
		_box.Reflow();
		RefreshFoldAllBtn();
	}

	private void ToggleFoldAll()
	{
		if (!_edit)
		{
			return;
		}
		bool anyOpen = AnyCardOpen();
		foreach (LayoutCell cell in _box.Cells)
		{
			if (_cards.TryGetValue(cell.Id, out LayoutCard? card))
			{
				card.SetFolded(anyOpen);
			}
		}
		_box.Reflow();
		RefreshFoldAllBtn();
	}

	private bool AnyCardOpen()
	{
		foreach (LayoutCell cell in _box.Cells)
		{
			if (_cards.TryGetValue(cell.Id, out LayoutCard? card) && !card.IsFolded)
			{
				return true;
			}
		}
		return false;
	}

	private void RefreshFoldAllBtn()
	{
		if (_foldAllBtn == null)
		{
			return;
		}
		_foldAllBtn.Content = (AnyCardOpen() ? I18n.L("全部折叠") : I18n.L("全部展开"));
		_foldAllBtn.IsEnabled = IsBoxMode;
	}

	// [layout-fix] 参考件v2-对齐:双击区块下边 → 取消固定高度,恢复跟随内容
	internal void ClearFixedHeight(LayoutCard card)
	{
		if (!_edit || !IsBoxMode || card == null || _gesture.Length != 0)
		{
			return;
		}
		LayoutCell? cell = _box.CellOf(card.Spec.Id);
		if (cell == null || cell.HeightPx <= 0.0)
		{
			return;
		}
		_session.PushUndo(_state.ToPage());
		cell.HeightPx = 0.0;
		card.SetFixedHeight(false);
		CommitBox(new List<string> { card.Spec.Id });
	}
	private string? PrimaryId()
	{
		foreach (string id in _session.OrderedSelection(_state.Order))
		{
			if (_gestureCells.ContainsKey(id))
			{
				return id;
			}
		}
		foreach (string id in _gestureCells.Keys)
		{
			return id;
		}
		return null;
	}

	// 画布里的"其它块"矩形(对齐辅助线的比对对象);用纯函数当场算,不依赖尚未重排的 Arrange 结果
	private List<LayoutRect> OtherRects(string? excludedId, bool canvas)
	{
		List<LayoutRect> list = new List<LayoutRect>();
		foreach (LayoutCell cell in _box.Cells)
		{
			if (string.Equals(cell.Id, excludedId, StringComparison.Ordinal))
			{
				continue;
			}
			LayoutRect? rect = (canvas ? CanvasRectOf(cell) : GridRectOf(cell));
			if (rect.HasValue)
			{
				list.Add(rect.Value);
			}
		}
		return list;
	}

	private LayoutRect? GridRectOf(LayoutCell? cell)
	{
		if (cell == null)
		{
			return null;
		}
		return LayoutGrid.Place(cell, _box.ColumnWidthValue, _box.RowHeightValues, _box.Gap);
	}

	private LayoutRect? CanvasRectOf(LayoutCell? cell)
	{
		if (cell == null)
		{
			return null;
		}
		return LayoutCanvas.Place(cell, AxisWidth(), AxisHeight());
	}

	// 选择态可视化(多选:主题色加粗描边 + 更深的蒙版)
	private void UpdateSelectionVisual()
	{
		foreach (UIElement child in _box.Children)
		{
			if (child is LayoutCard card)
			{
				card.SetSelected(_edit && _session.IsSelected(card.Spec.Id));
			}
		}
	}

	// ————————————— 键盘 / 撤销重做 / 剪贴板 —————————————

	private void OnEditorKeyDown(object sender, KeyEventArgs e)
	{
		if (!_edit)
		{
			return;
		}
		bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
		bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
		LayoutKey key = LayoutEditSession.KeyOf(e.Key.ToString(), ctrl, shift);
		switch (key)
		{
		case LayoutKey.Escape:
			if (_gesture.Length != 0)
			{
				EndBoxGesture(false, true);
			}
			else
			{
				ExitEdit();
			}
			break;
		case LayoutKey.Delete:
			DeleteSelection();
			break;
		case LayoutKey.Left:
		case LayoutKey.Right:
		case LayoutKey.Up:
		case LayoutKey.Down:
			NudgeSelection(key, shift);
			break;
		case LayoutKey.Undo:
			UndoEdit();
			break;
		case LayoutKey.Redo:
			RedoEdit();
			break;
		case LayoutKey.Copy:
			CopyToClipboard();
			break;
		case LayoutKey.Paste:
			PasteFromClipboard();
			break;
		default:
			return;
		}
		e.Handled = true;
	}

	// Delete / Back:只移除**自建块 / 插件块**(内置块的内容属于页面,不允许从布局里删掉);
	// widgets[] 里的定义不删(数据保留,区块管理在 L6)
	private void DeleteSelection()
	{
		if (!IsBoxMode || _session.SelectedCount == 0)
		{
			return;
		}
		PageLayoutV2 page = _state.ToPage();
		List<BlockLayout> keep = new List<BlockLayout>();
		int removed = 0;
		foreach (BlockLayout block in page.Blocks)
		{
			if (block == null)
			{
				continue;
			}
			if (_session.IsSelected(block.Id) && LayoutEngine.IsImplicitRef(block.Id))
			{
				removed++;
				continue;
			}
			keep.Add(block);
		}
		if (removed == 0)
		{
			AppServices.Window?.Toast(I18n.L("内置区块不能移除"), "", ToastKind.Warning);
			return;
		}
		_session.PushUndo(_state.ToPage());
		page.Blocks = keep;
		_state = LayoutEngine.LoadState(_pageId, page, _refs);
		_session.Retain(_state.Order);
		Persist();
		Render();
	}

	// 方向键微调:grid 走"格",canvas 走"吸附格";Shift = 10px 档(§4.G)
	private void NudgeSelection(LayoutKey key, bool shift)
	{
		if (!IsBoxMode || _session.SelectedCount == 0)
		{
			return;
		}
		int dx = ((key == LayoutKey.Left) ? -1 : ((key == LayoutKey.Right) ? 1 : 0));
		int dy = ((key == LayoutKey.Up) ? -1 : ((key == LayoutKey.Down) ? 1 : 0));
		PageLayoutV2 page = _state.ToPage();
		int moved = LayoutEditSession.Nudge(page, _session.Selection, dx, dy, shift, AxisWidth(), AxisHeight(), _snapStep);
		if (moved == 0)
		{
			return;
		}
		_session.PushUndo(_state.ToPage());
		_state = LayoutEngine.LoadState(_pageId, page, _refs);
		_session.Retain(_state.Order);
		Persist();
		Render();
	}

	/// <summary>撤销(内存快照;落盘保持即时)。</summary>
	public void UndoEdit()
	{
		if (!_edit)
		{
			return;
		}
		PageLayoutV2? snapshot = _session.TakeUndo(_state.ToPage());
		if (snapshot == null)
		{
			AppServices.Window?.Toast(I18n.L("没有可撤销的操作"), "", ToastKind.Info);
			return;
		}
		ApplySnapshot(snapshot, I18n.L("已撤销"), ToastKind.Info);
	}

	/// <summary>重做。</summary>
	public void RedoEdit()
	{
		if (!_edit)
		{
			return;
		}
		PageLayoutV2? snapshot = _session.TakeRedo(_state.ToPage());
		if (snapshot == null)
		{
			AppServices.Window?.Toast(I18n.L("没有可重做的操作"), "", ToastKind.Info);
			return;
		}
		ApplySnapshot(snapshot, I18n.L("已重做"), ToastKind.Info);
	}

	private void ApplySnapshot(PageLayoutV2 page, string message, ToastKind kind)
	{
		_state = LayoutEngine.LoadState(_pageId, page, _refs);
		_session.Retain(_state.Order);
		Persist();
		Render();
		AppServices.Window?.Toast(message, "", kind);
	}

	// Ctrl+C:有选中就复制选中的块,没选中就复制整页(§4.G 的"复制粘贴布局")
	private void CopyToClipboard()
	{
		int count = _session.CopyFrom(_state.ToPage(), !IsBoxMode);
		if (count == 0)
		{
			return;
		}
		AppServices.Window?.Toast(I18n.L("已复制到剪贴板"), count.ToString(), ToastKind.Success);
	}

	// Ctrl+V:粘贴到本页末尾(内置块按目标页白名单过滤,按 id 去重)
	private void PasteFromClipboard()
	{
		if (!_session.HasClipboard)
		{
			return;
		}
		PageLayoutV2 page = _state.ToPage();
		int count = _session.PasteInto(page, _refs);
		if (count == 0)
		{
			AppServices.Window?.Toast(I18n.L("没有可粘贴的区块"), "", ToastKind.Info);
			return;
		}
		_session.PushUndo(_state.ToPage());
		_state = LayoutEngine.LoadState(_pageId, page, _refs);
		_session.Retain(_state.Order);
		Persist();
		Render();
	}

	internal static Color AccentColor()
	{
		if (!(Application.Current?.TryFindResource("B.Accent") is SolidColorBrush solidColorBrush))
		{
			return Color.FromRgb(46, 155, 107);
		}
		return solidColorBrush.Color;
	}
}

// 单个区块卡:常态零装饰(厚度 0 的透明描边 + 无底色),仅承载内容;
// 编辑态出现操作条(拖拽手柄 + 块标题 + 显隐按钮),主题色描边 + 10 透明度蒙版(对齐 DashHost.CardView)。
// L2 起还承载三件事(缺省都不改变现状渲染):
//   1) 外观参数(§4.B):style=card/divider、常态标题(title)、每块边距(pad);缺省 plain + 无标题 + 无边距;
//   2) 画布手势:grid/canvas 模式下块身可拖拽、右下角(及 canvas 的 8 向)手柄可缩放;
//   3) 多选态:主题色 2px 描边 + 26 透明度蒙版。
internal sealed class LayoutCard : Border
{
	private readonly LayoutEditor _host;

	private readonly SPanel _bar;

	private readonly SPanel _titleRow;

	private readonly Border _bodyWrap;

	private readonly Grid _handleLayer;

	private readonly Dictionary<string, Border> _handles = new Dictionary<string, Border>(StringComparer.Ordinal);

	private readonly ContentControl _body = new ContentControl
	{
		Focusable = false
	};

	private bool _edit;

	// [layout-fix] 参考件v2-对齐:编辑态折叠 / 固定高度 / 窄宽自适应
	//   折叠:只留标题栏,方便在长列表之间腾挪;**只在编辑时有效,不写进布局**(退出编辑自动展开)
	//   固定高度:grid 自动行高的页面里用户拖出来的像素高度(HeightPx),内容改在块内滚动
	//   FitBox:拉窄后"矮的一行/几行"整体等比缩小,而不是把右边裁掉
	private bool _folded;

	private readonly Button _foldBtn;

	private readonly ScrollViewer _fixedScroll = new ScrollViewer
	{
		VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
		HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
		Focusable = false
	};

	private readonly FitBox _fit = new FitBox();

	private readonly Dictionary<string, Border> _strips = new Dictionary<string, Border>(StringComparer.Ordinal);

	/// <summary>[layout-fix] 参考件v2-对齐:是否处于"折叠"态(仅编辑态;排版据此只给标题栏高度)。</summary>
	public bool IsFolded
	{
		get
		{
			return _folded && _edit;
		}
	}

	private bool _selected;

	private string _boxMode = LayoutSchema.ModeStack;

	private string _lookStyle = LayoutSchema.StylePlain;

	private bool _lookTitle;

	private double[]? _lookPad;

	// [layout-fix] 参考件v2-对齐:是否已经切到"固定高度 + 块内滚动"的呈现
	private bool _fixedHeight;

	public BlockSpec Spec { get; }

	public LayoutCard(LayoutEditor host, BlockSpec spec, UIElement? content)
	{
		_host = host;
		Spec = spec;
		CornerRadius = new CornerRadius(10.0);
		SetResourceReference(Border.BorderBrushProperty, "B.Accent");
		// [layout-fix] 规格 §3-问题7 + 规格2-问题7:去掉 ⇅「拖拽排序」按钮,换成一个**不可点**的拖动提示图标。
		// 拖动不再需要按住这个小按钮 —— 编辑态下整张卡片任意位置都能按住拖(见 OnCardMouseDown / SetEdit 的十字光标);
		// 图标本身不参与命中(IsHitTestVisible=false),免得它挡住它下面那块的按下事件。
		// 偏离说明:规格2-问题7 建议把码位换成 U+E76F,但那一位**不在**内置图标字体 TipCLIcons.ttf 里
		// (IconFontTests.EveryGlyphUsedInSourceIsInEmbeddedFont 守卫;缺字体时 Win7/精简系统会显示成方框)。
		// 补码位要重做 tools/iconfont 的子集字体,不在本轮范围内 ⇒ 仍然用 \ue8cb(⇅),但已经是"不可点的提示"而非按钮。
		TextBlock dragIcon = Ui.Glyph(Ico.Sort, 12.0);
		dragIcon.ToolTip = I18n.L("按住区块拖动移动");
		dragIcon.IsHitTestVisible = false;
		dragIcon.VerticalAlignment = VerticalAlignment.Center;
		Button button = Ui.IconBtn(Ico.Close, I18n.L("隐藏"), delegate
		{
			_host.HideBlock(Spec);
		}, 12.0);
		TextBlock textBlock = Ui.Txt(spec.Title, 12.5, bold: true);
		textBlock.VerticalAlignment = VerticalAlignment.Center;
		textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
		_foldBtn = Ui.IconBtn("\ue70e", I18n.L("折叠(仅编辑时)"), delegate
		{
			SetFolded(!_folded);
			_host.OnCardFoldChanged();
		}, 11.0);
		_bar = Ui.H(8.0, dragIcon, textBlock, Ui.Spring(), _foldBtn, button);
		_bar.Margin = new Thickness(6.0, 6.0, 6.0, 2.0);
		// 常态块标题(§4.B 的 title;缺省 false → 整行折叠,与改动前一致)
		TextBlock titleText = Ui.Txt(spec.Title, 12.5, bold: true);
		titleText.TextTrimming = TextTrimming.CharacterEllipsis;
		_titleRow = Ui.H(8.0, titleText, Ui.Spring());
		_titleRow.Margin = new Thickness(0.0, 0.0, 0.0, 6.0);
		_titleRow.Visibility = Visibility.Collapsed;
		// 内容体外壳:常态零边距(逐像素等价旧版),编辑态留一点呼吸边距
		_fit.Child = _body;
		_bodyWrap = new Border
		{
			Padding = new Thickness(0.0),
			Child = _fit
		};
		Grid grid = Ui.G("Auto,Auto,*");
		grid.Add(_bar, 0);
		grid.Add(_titleRow, 1);
		grid.Add(_bodyWrap, 2);
		// 画布模式的缩放手柄层(8 向;grid 用 e/s/se)。缺省与 stack 模式一律折叠 → 不影响默认渲染。
		_handleLayer = new Grid
		{
			Visibility = Visibility.Collapsed
		};
		// [layout-fix] 参考件v2-对齐:右边、下边**整条边**都可以拖(原来只有边中点的小方块,
		// 高区块的中点常在屏幕外 ⇒ 等于拖不了)
		AddStrip("e");
		AddStrip("s");
		string[] handleNames = LayoutCanvas.HandleNames;
		foreach (string name in handleNames)
		{
			AddHandle(name);
		}
		grid.Add(_handleLayer, 0, 0, 3, 1);
		Child = grid;
		_bar.Visibility = Visibility.Collapsed;
		// 画布模式:块身按下 = 整体拖拽(用冒泡事件而不是 Preview —— 操作条/手柄自己的处理器先跑并置 Handled)
		base.MouseLeftButtonDown += OnCardMouseDown;
		// 内容已挂在别的 ContentControl 上时先摘下来(对齐 DashHost.SetContent)
		if (content is FrameworkElement { Parent: ContentControl parent } && parent != _body)
		{
			parent.Content = null;
		}
		_body.Content = content;
	}

	/// <summary>切画布模式(grid / canvas;stack = 缺省,手柄层折叠)。</summary>
	public void SetBoxMode(string mode)
	{
		_boxMode = ((mode == LayoutSchema.ModeGrid || mode == LayoutSchema.ModeCanvas) ? mode : LayoutSchema.ModeStack);
		// [layout-fix] 参考件v2-对齐:窄宽自适应只在画布模式开(stack 的默认渲染一个像素都不经过它)
		_fit.Enabled = _boxMode != LayoutSchema.ModeStack;
		if (!_edit)
		{
			_handleLayer.Visibility = Visibility.Collapsed;
		}
		else
		{
			UpdateHandles();
		}
	}

	public void SetEdit(bool edit)
	{
		_edit = edit;
		if (!edit)
		{
			// 折叠只是"编辑时的临时视图",退出编辑一定展开(也不写进布局)
			_folded = false;
		}
		ApplyFold();
		_bar.Show(edit);
		_body.IsHitTestVisible = !edit;
		// [layout-fix] 规格 §3-问题7:编辑态整卡可拖 → 光标变十字移动箭头(隐藏 × 按钮自己会盖成手型)
		Cursor = (edit ? Cursors.SizeAll : null);
		UpdateHandles();
		ApplyLook();
	}

	// [layout-fix] 参考件v2-对齐:编辑态折叠(只显示标题栏,不写入布局)
	public void SetFolded(bool folded)
	{
		_folded = folded && _edit;
		ApplyFold();
		UpdateHandles();
	}

	private void ApplyFold()
	{
		bool folded = _folded && _edit;
		_bodyWrap.Visibility = (folded ? Visibility.Collapsed : Visibility.Visible);
		if (_foldBtn != null)
		{
			_foldBtn.Content = Ui.Glyph(folded ? "\ue70d" : "\ue70e", 11.0);
			_foldBtn.ToolTip = (folded ? I18n.L("展开") : I18n.L("折叠(仅编辑时)"));
		}
	}

	// [layout-fix] 参考件v2-对齐:固定高度时把内容放进块内滚动容器(内容超出就在块里滚)
	public void SetFixedHeight(bool on)
	{
		if (_fixedHeight == on)
		{
			return;
		}
		_fixedHeight = on;
		if (on)
		{
			_fit.Child = null;
			_fixedScroll.Content = _fit;
			_bodyWrap.Child = _fixedScroll;
		}
		else
		{
			_fixedScroll.Content = null;
			_bodyWrap.Child = _fit;
		}
	}

	// [layout-fix] 参考件v2-对齐:右边 / 下边整条边的拖拽热区(透明的 8px 条带)
	private void AddStrip(string name)
	{
		bool east = name == "e";
		Border strip = new Border
		{
			Background = Brushes.Transparent,
			Tag = name,
			Width = (east ? 8.0 : double.NaN),
			Height = (east ? double.NaN : 8.0),
			HorizontalAlignment = (east ? HorizontalAlignment.Right : HorizontalAlignment.Stretch),
			VerticalAlignment = (east ? VerticalAlignment.Stretch : VerticalAlignment.Bottom),
			Margin = (east ? new Thickness(0.0, 10.0, -4.0, 10.0) : new Thickness(10.0, 0.0, 10.0, -4.0)),
			Cursor = (east ? Cursors.SizeWE : Cursors.SizeNS)
		};
		strip.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
		{
			if (!east && e.ClickCount >= 2)
			{
				// 双击下边:恢复成「跟随内容高度」
				_host.ClearFixedHeight(this);
				e.Handled = true;
				return;
			}
			_host.BeginBoxResize(this, name, e);
		};
		_strips[name] = strip;
		_handleLayer.Children.Add(strip);
	}

	/// <summary>外观参数(§4.B):style / 常态标题 / 每块边距。</summary>
	public void SetLook(string style, bool showTitle, double[]? pad)
	{
		_lookStyle = (LayoutSchema.IsKnownStyle(style) ? style : LayoutSchema.StylePlain);
		_lookTitle = showTitle;
		_lookPad = pad;
		ApplyLook();
	}

	/// <summary>多选态(仅画布模式有意义)。</summary>
	public void SetSelected(bool selected)
	{
		if (_selected == selected)
		{
			return;
		}
		_selected = selected;
		if (_edit)
		{
			ApplyLook();
		}
	}

	// 编辑态蒙版:主题色 10 透明度平铺(对齐 CardView.RefreshEditMask;跟着主题刷新)
	public void RefreshTint()
	{
		ApplyLook();
	}

	// 手柄可见性:编辑态 + 画布模式才出现;grid 只给 e / s / se(canvas 才是 8 向)
	private void UpdateHandles()
	{
		bool box = _edit && _boxMode != LayoutSchema.ModeStack;
		_handleLayer.Visibility = (box ? Visibility.Visible : Visibility.Collapsed);
		if (!box)
		{
			return;
		}
		foreach (KeyValuePair<string, Border> kv in _handles)
		{
			bool show = (_boxMode == LayoutSchema.ModeCanvas) || kv.Key == "e" || kv.Key == "s" || kv.Key == "se";
			kv.Value.Visibility = (show ? Visibility.Visible : Visibility.Collapsed);
		}
		// [layout-fix] 参考件v2-对齐:整条边的热区;折叠时下边不给拖(拖的是折叠后的假高度)
		foreach (KeyValuePair<string, Border> strip in _strips)
		{
			strip.Value.Visibility = (((strip.Key == "s" && _folded) || !_edit) ? Visibility.Collapsed : Visibility.Visible);
		}
	}

	private void AddHandle(string name)
	{
		Border handle = new Border
		{
			Width = LayoutCanvas.HandleSize,
			Height = LayoutCanvas.HandleSize,
			CornerRadius = new CornerRadius(3.0),
			BorderThickness = new Thickness(1.0),
			Tag = name,
			HorizontalAlignment = (name.IndexOf('w') >= 0 ? HorizontalAlignment.Left : (name.IndexOf('e') >= 0 ? HorizontalAlignment.Right : HorizontalAlignment.Center)),
			VerticalAlignment = (name.IndexOf('n') >= 0 ? VerticalAlignment.Top : (name.IndexOf('s') >= 0 ? VerticalAlignment.Bottom : VerticalAlignment.Center)),
			Cursor = HandleCursor(name),
			Margin = new Thickness(name.IndexOf('w') >= 0 ? -5.5 : (name.IndexOf('e') >= 0 ? -5.5 : 0.0), name.IndexOf('n') >= 0 ? -5.5 : (name.IndexOf('s') >= 0 ? -5.5 : 0.0), name.IndexOf('e') >= 0 ? -5.5 : 0.0, name.IndexOf('s') >= 0 ? -5.5 : 0.0)
		};
		handle.SetResourceReference(Border.BackgroundProperty, "B.Accent");
		handle.SetResourceReference(Border.BorderBrushProperty, "B.Paper");
		string captured = name;
		handle.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
		{
			_host.BeginBoxResize(this, captured, e);
		};
		_handles[name] = handle;
		_handleLayer.Children.Add(handle);
	}

	private static Cursor HandleCursor(string name)
	{
		if (name == "nw" || name == "se")
		{
			return Cursors.SizeNWSE;
		}
		if (name == "ne" || name == "sw")
		{
			return Cursors.SizeNESW;
		}
		if (name == "n" || name == "s")
		{
			return Cursors.SizeNS;
		}
		return Cursors.SizeWE;
	}

	private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (!_edit || _boxMode == LayoutSchema.ModeStack)
		{
			return;
		}
		_host.BeginBoxDrag(this, e);
	}

	// 外观 + 编辑态视觉的唯一落点:三处(SetEdit / SetLook / SetSelected / RefreshTint)都走这里,
	// 保证"缺省参数 = 现状渲染"这一条只有一个判断点。
	private void ApplyLook()
	{
		bool card = string.Equals(_lookStyle, LayoutSchema.StyleCard, StringComparison.Ordinal);
		bool divider = string.Equals(_lookStyle, LayoutSchema.StyleDivider, StringComparison.Ordinal);
		_titleRow.Visibility = ((_lookTitle && !_edit) ? Visibility.Visible : Visibility.Collapsed);
		if (_edit)
		{
			_bodyWrap.Padding = new Thickness(6.0, 0.0, 6.0, 8.0);
			BorderThickness = new Thickness(_selected ? 2.0 : 1.4);
			CornerRadius = new CornerRadius(10.0);
			Color color = LayoutEditor.AccentColor();
			Background = new SolidColorBrush(Color.FromArgb((byte)(_selected ? 26 : 10), color.R, color.G, color.B));
			return;
		}
		if (card)
		{
			BorderThickness = new Thickness(1.0);
			CornerRadius = new CornerRadius(12.0);
			SetResourceReference(Border.BorderBrushProperty, "B.Line");
			SetResourceReference(Border.BackgroundProperty, "B.Paper");
		}
		else if (divider)
		{
			BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0);
			CornerRadius = new CornerRadius(0.0);
			SetResourceReference(Border.BorderBrushProperty, "B.LineSoft");
			Background = null;
		}
		else
		{
			// plain = 现状(§4.B 明确:厚度 0、无底色)
			BorderThickness = new Thickness(0.0);
			CornerRadius = new CornerRadius(10.0);
			SetResourceReference(Border.BorderBrushProperty, "B.Accent");
			Background = null;
		}
		_bodyWrap.Padding = BlockPad();
	}

	private Thickness BlockPad()
	{
		double[]? pad = _lookPad;
		if (pad == null || pad.Length < 4)
		{
			return new Thickness(0.0);
		}
		return new Thickness(pad[0], pad[1], pad[2], pad[3]);
	}
}
