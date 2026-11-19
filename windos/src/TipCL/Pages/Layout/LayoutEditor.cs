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

	private readonly SPanel _list = new SPanel
	{
		Orientation = Orientation.Vertical,
		Spacing = LayoutSchema.GapDefault
	};

	private LayoutPageState _state;

	// 当前显示的是哪一份布局(空串 = 主 body;LayoutVariants.Narrow = variants.narrow)。
	// 窄窗下读到的是变体,落盘也只能写变体,否则会把窄版盖到宽版上(P1)。
	private string _variant = "";

	// 「页面默认布局」:v2 文档里还没有这一页时的兜底(实例库的左右排布靠它)
	private readonly PageLayoutV2? _defaultPage;

	private readonly Border _trayHost = new Border();

	private readonly WrapPanel _tray = new WrapPanel();

	private readonly Border _barHost = new Border();

	private readonly Grid _root;

	private bool _edit;

	private LayoutCard? _dragCard;

	// stack 拖拽的鼠标捕获源(退出编辑时要解绑,否则残留的 MouseMove 还会改序)
	private UIElement? _dragSrc;

	// 本次 stack 拖拽是否真的换过序(只点一下手柄不该落盘)
	private bool _dragChanged;

	// 进编辑态时把 stack 临时折成 12 列网格(这样才能自由拖);没动过就在退出时还原
	private bool _stackConverted;
	private bool _layoutTouched;
	private PageLayoutV2? _stackBackup;

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

	private LayoutRect _gestureAnchor;

	// 手势开始时的全量槽位与行高快照:每次鼠标移动都从这里重算,避免累加误差让"越拖越乱"
	private readonly Dictionary<string, LayoutCell> _gestureSnapshot = new Dictionary<string, LayoutCell>(StringComparer.Ordinal);
	private double[] _gestureRows = Array.Empty<double>();

	// 下边整条边拖动 = 改固定像素高度(height_px),不再改"占几行"
	private bool _pixelHeightGesture;
	private double _gestureStartHeight;

	// 丝滑拖动(grid):上一次算出的落点 / 吸附后尺寸 —— 只有它变了才重排一次(其余帧只动 RenderTransform)
	private int _lastDropX = int.MinValue;
	private int _lastDropY = int.MinValue;
	// 并排落位的宽度单独比较(不再把 fitW 编进 _lastDropX:targetX=0,fitW=2 会与普通 targetX=2 撞键)
	private int _lastFitW = int.MinValue;
	private int _lastSnapW = int.MinValue;
	private int _lastSnapH = int.MinValue;
	private double _lastSnapPx = double.NaN;

	// 本次手势里指针是否真的动过(只点一下不算改布局,不落盘)
	private bool _gestureMoved;

	// canvas 磁性吸附上一帧的吸附位移(变了才给那一下短动画)
	private double _snapDx;
	private double _snapDy;

	// 工具条上的撤销/重做按钮(按栈的可用性置灰)
	private Button? _undoBtn;

	private Button? _redoBtn;

	// 吸附档按钮(只在 grid/canvas 编辑态可用)
	private Button? _snapBtn;

	// 「自动靠拢」开关按钮(grid 压紧上浮 / 左右互换 / canvas 磁性吸附;偏好存 UiPrefs.LayoutAutoPack)
	private Button? _autoPackBtn;

	// 「全部折叠」按钮:文案跟着卡片的实际折叠状态走(退出编辑自动展开后不再停在"全部展开")
	private Button? _foldAllBtn;

	// 底部托盘 + 工具条的外壳:只在编辑态占位(常态整行折叠,内容区高度与改动前一致)
	private readonly SPanel _bottom;

	// 「原生默认排版」(AI 页 / 版本管理页):没自定义过布局且不在编辑态时直接显示页面原来的排版根
	private readonly BlockNativeLayout? _native;

	// 布局文档里还没有这一页(= 应当显示原生默认排版);只有给了 _native 的页面才可能为 true
	private bool _nativeActive;

	// 原生排版当前是否挂在内容区(各块内容此时在原排版里,不在卡片里)
	private bool _nativeShown;

	// 占满剩余高度的区块(BlockSpec.Fill):内容自带滚动,网格里按可视高度分配余量
	private readonly HashSet<string> _fillIds = new HashSet<string>(StringComparer.Ordinal);

	/// <summary>构造:pageId + 页面块描述 + 可选的页级边距覆盖(null = 用文档 pad,一般是 18,14,18,16)。</summary>
	/// <param name="defaultPage">「页面默认布局」:v2 文档里还没有这一页时用它(实例库靠它默认左右排布)。</param>
	/// <param name="native">「原生默认排版」(可空):没自定义过布局时显示页面原来的排版根(默认外观逐像素不变)。</param>
	public LayoutEditor(string pageId, IReadOnlyList<BlockSpec> specs, Thickness? contentMargin = null, PageLayoutV2? defaultPage = null, BlockNativeLayout? native = null)
	{
		_pageId = pageId ?? "";
		_marginOverride = contentMargin;
		_defaultPage = defaultPage;
		_native = ((native != null && native.Root != null) ? native : null);
		if (specs != null)
		{
			foreach (BlockSpec spec in specs)
			{
				if (spec != null && !string.IsNullOrEmpty(spec.Id))
				{
					_specs.Add(spec);
					_byId[spec.Id] = spec;
					_refs.Add(new LayoutBlockRef(spec.Id, spec.DefaultVisible));
					if (spec.Fill)
					{
						_fillIds.Add(spec.Id);
					}
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
		_autoPackBtn = Ui.Btn(I18n.L("自动靠拢"), BtnKind.Normal, delegate
		{
			ToggleAutoPack();
		});
		_autoPackBtn.ToolTip = I18n.L("开:松手后区块自动向上靠拢、与相邻区块互换位置、靠近时磁性吸附;关:区块停在你放下的位置");
		// 按钮放进 WrapPanel:窄窗/高分屏下换行,不再像原来那样横向排一排被右侧裁掉半截。
		WrapPanel barButtons = new WrapPanel
		{
			Orientation = Orientation.Horizontal
		};
		barButtons.Children.Add(_undoBtn);
		barButtons.Children.Add(_redoBtn);
		barButtons.Children.Add(_snapBtn);
		barButtons.Children.Add(_autoPackBtn);
		barButtons.Children.Add(Ui.Btn(I18n.L("完成编辑"), BtnKind.Primary, delegate
		{
			ExitEdit();
		}));
		Button foldAll = Ui.Btn(I18n.L("全部折叠"), BtnKind.Normal, null);
		_foldAllBtn = foldAll;
		foldAll.Click += delegate
		{
			// 状态从卡片读(不再靠按钮局部 bool):有任意一块折叠着 = 这一步"全部展开",否则"全部折叠"。
			bool fold = !IsAnyCardCollapsed();
			foreach (LayoutCard value2 in _cards.Values)
			{
				value2.SetCollapsed(fold);
			}
			RefreshFoldAllBtn();
		};
		barButtons.Children.Add(foldAll);
		barButtons.Children.Add(Ui.Btn(I18n.L("重置本页"), BtnKind.Normal, delegate
		{
			ResetPage();
		}));
		barButtons.Children.Add(Ui.Btn(I18n.L("导出布局"), BtnKind.Normal, delegate
		{
			PageLayoutIO.ExportToFileDialog(_pageId);
		}));
		barButtons.Children.Add(Ui.Btn(I18n.L("导入布局"), BtnKind.Normal, delegate
		{
			ImportLayout();
		}));
		barButtons.HorizontalAlignment = HorizontalAlignment.Right;
		// 两列:左列提示(宽度不够用省略号截断,悬停看全文),右列按钮靠右 ——
		// 窄区域(设置页)里按钮不会被挤掉,提示也不会被裁一半。
		string hintText = I18n.L("按住区块拖动移动 · 拖右/下边角缩放");
		TextBlock barHint = Ui.Txt(hintText, 11.5, bold: false, "B.InkMuted");
		barHint.TextTrimming = TextTrimming.CharacterEllipsis;
		barHint.ToolTip = hintText;
		barHint.Margin = new Thickness(2.0, 0.0, 8.0, 0.0);
		Grid barGrid = Ui.G("*,Auto");
		barGrid.Add(barHint.VCenter(), 0, 0);
		barGrid.Add(barButtons.VCenter(), 0, 1);
		SPanel sPanel2 = Ui.V(0.0, barGrid);
		_barHost.CornerRadius = new CornerRadius(10.0);
		_barHost.Padding = new Thickness(10.0, 8.0, 10.0, 8.0);
		_barHost.Visibility = Visibility.Collapsed;
		_barHost.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		_barHost.SetResourceReference(Border.BorderBrushProperty, "B.Line");
		_barHost.BorderThickness = new Thickness(1.0);
		_barHost.Child = sPanel2;
		Motion.Shadow(_barHost, 16.0, 0.07, 3.0);
		// 根结构:上滚动内容区,下托盘/工具条(仅编辑态占位出现,不悬浮遮挡内容)
		// 常态整行折叠:原来这一行即使托盘和工具条都折叠了也占着 8 + 16 = 24px 的外边距,
		// 内容区因此比改动前矮 24px(铺满整页的页面,例如 AI 对话区,会被看出来)。
		SPanel bottom = Ui.V(8.0, _trayHost, _barHost);
		bottom.Margin = new Thickness(_list.Margin.Left, 8.0, _list.Margin.Right, _list.Margin.Bottom);
		bottom.Visibility = Visibility.Collapsed;
		_bottom = bottom;
		_root = Ui.G("*,Auto");
		_scroll = Ui.Scroll(_list);
		// 透明底:区块之间的空隙也能接到滚轮(null 底时滚轮穿透到页面背景,看起来"滑不动")
		_scroll.Background = Brushes.Transparent;
		_root.Add(_scroll, 0);
		_root.Add(bottom, 1);
		// grid/canvas 模式的可视宿主:自绘画布 Panel + 其上的对齐辅助线/框选覆盖层(同一格叠放)。
		// 注意:stack(缺省)时这个宿主根本不进可视树 —— 默认渲染与改动前逐像素一致由这一点保证。
		_stage.Children.Add(_box);
		_stage.Children.Add(_guide);
		_boxScroll = Ui.Scroll(_stage);
		_boxScroll.Background = Brushes.Transparent;
		// 占满剩余高度的区块(AI 对话区等):画布按可视高度把余量分给这些块所在的行
		_box.FillIds = _fillIds;
		_boxScroll.SizeChanged += delegate
		{
			UpdateFillHeight();
		};
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

	/// <summary>当前是否正显示页面的原生默认排版(没自定义过布局、也不在编辑态)。</summary>
	public bool IsNativeDefault
	{
		get
		{
			return _nativeActive && !_edit;
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

	/// <summary>
	/// 重新从真源装载本页并重排(D1):设置页改完布局文档、或整机方案被应用之后,
	/// 已存在的编辑器实例靠它立刻刷新(不重建页面,页面自己的状态不丢)。
	/// </summary>
	public void ReloadFromStore()
	{
		LoadFromStore();
		Render();
	}

	// stack → 12 列网格:按当前顺序逐行折算(每块占满一行),画面看起来不变,
	// 但块从此可以上下左右拖、拖边角改尺寸。
	private void PrepareEditableGrid()
	{
		_stackConverted = false;
		_layoutTouched = false;
		_stackBackup = null;
		PageLayoutV2 page = _state.Page;
		if (page == null || !string.Equals(page.Mode, LayoutSchema.ModeStack, StringComparison.Ordinal))
		{
			return;
		}
		_stackBackup = page.Clone();
		_stackConverted = true;
		page.Mode = LayoutSchema.ModeGrid;
		page.Cols = 12;
		page.RowH = 0.0;
		_state.RebuildPage();
		int row = 0;
		foreach (BlockLayout block in page.Blocks)
		{
			block.X = 0.0;
			block.Y = row;
			block.W = 12.0;
			block.H = 1.0;
			block.Span = 12;
			row++;
		}
	}

	// 整场没动过:把页模式、列数与各块几何还原成进编辑前那一份。
	private void RestoreStackAfterEdit()
	{
		PageLayoutV2? back = _stackBackup;
		if (back == null)
		{
			return;
		}
		PageLayoutV2 page = _state.Page;
		page.Mode = back.Mode;
		page.Cols = back.Cols;
		page.RowH = back.RowH;
		List<BlockLayout> blocks = new List<BlockLayout>();
		foreach (BlockLayout block in back.Blocks)
		{
			blocks.Add(block.Clone());
		}
		page.Blocks = blocks;
	}

	/// <summary>某块当前是否隐藏(页面自己读状态用,例如实例库的「显示文件夹栏」菜单)。</summary>
	public bool IsBlockHidden(string id)
	{
		return !string.IsNullOrEmpty(id) && _state.Hidden.Contains(id);
	}

	/// <summary>
	/// 直接显隐某块并落盘(不需要先进编辑态)。实例库的右键「显示/隐藏文件夹栏」走这里 ——
	/// 以前它写的是 v1 文件,v2 单向写通时会被覆盖,所以勾了也像没反应。
	/// </summary>
	public void SetBlockHidden(string id, bool hidden)
	{
		_layoutTouched = true;
		if (string.IsNullOrEmpty(id))
		{
			return;
		}
		bool isHidden = _state.Hidden.Contains(id);
		if (isHidden == hidden)
		{
			return;
		}
		_session.PushUndo(_state.ToPage());
		if (hidden)
		{
			_state.Hide(id);
		}
		else
		{
			_state.Restore(id);
		}
		// 先落盘再重画:原生默认排版的页面一落盘就成了"自定义过",重画时才会切到区块形态
		Persist();
		Render();
	}

	/// <summary>就地改当前页(页级参数或某块的 x/y/w/h)并落盘;给页面做"铺满整行"这类调整用。</summary>
	public void MutatePage(Action<PageLayoutV2> mutate)
	{
		_layoutTouched = true;
		if (mutate == null)
		{
			return;
		}
		_session.PushUndo(_state.ToPage());
		mutate(_state.Page);
		_state.RebuildPage();
		Persist();
		Render();
	}

	/// <summary>双击区块下边:取消固定高度,恢复跟随内容高度。</summary>
	internal void ClearFixedHeight(BlockSpec spec)
	{
		if (!_edit || spec == null)
		{
			return;
		}
		MutatePage(delegate(PageLayoutV2 page)
		{
			foreach (BlockLayout block in page.Blocks)
			{
				if (block.Id == spec.Id)
				{
					block.HeightPx = 0.0;
				}
			}
		});
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
		PrepareEditableGrid();
		CompactState(null);
		// 每次进编辑态都是一次新会话:多选与撤销栈清空(历史上的改动已经即时落盘,不需要跨会话撤销)
		_session.ClearHistory();
		_session.ClearSelection();
		_bottom.Visibility = Visibility.Visible;
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

	/// <summary>退出编辑态(落盘 + Toast,与旧版一致)。</summary>
	public void ExitEdit()
	{
		if (!_edit)
		{
			return;
		}
		_edit = false;
		// stack 拖拽可能还没收尾(比如被快捷键/外部调用结束编辑):必须解绑并释放捕获,
		// 否则之后鼠标一动还会改序、EndDrag 还会无条件落盘(P1)
		CancelDrag(_dragCard, _dragSrc);
		if (_gesture.Length != 0)
		{
			EndBoxGesture(false, false);
		}
		_box.ClearDragVisuals();
		// 原生默认排版的页面:整场什么都没动 → 不落盘,直接回到原排版(默认外观逐像素不变)
		bool backToNative = _nativeActive && !_layoutTouched;
		// 进编辑时把 stack 折成了网格:整场什么都没动就还原成原来的 stack,
		// 不因为"点进来看看"就把用户的纵向页面悄悄改成网格。
		if (_stackConverted && !_layoutTouched)
		{
			RestoreStackAfterEdit();
		}
		_stackConverted = false;
		_stackBackup = null;
		if (backToNative)
		{
			LoadFromStore();
		}
		else
		{
			Persist();
		}
		_trayHost.Visibility = Visibility.Collapsed;
		_barHost.Visibility = Visibility.Collapsed;
		_bottom.Visibility = Visibility.Collapsed;
		Render();
		_session.ClearSelection();
		_session.ClearHistory();
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
		// 显示的是哪一份,之后 Persist 就只能覆盖哪一份(P1:窄窗编辑原来会写穿到主 body)
		_variant = LayoutVariantWrite.SourceOf(doc.Layout, _pageId, width, LayoutVariantHost.Threshold);
		// 原生默认排版:文档里没有这一页 = 从没自定义过 → 常态直接显示页面原来的排版根
		_nativeActive = page == null && _native != null;
		// 「页面默认布局」(D-new):文档里还没有这一页时用宿主给的默认布局。
		// 实例库就是这样默认左右排布的;用户在编辑态拖过之后才写进 v2 文档。
		if (page == null && _defaultPage != null)
		{
			page = _defaultPage.Clone();
		}
		// 全局密度也来自 v2(缺省 standard → 间距档 10 = 现状)
		_density = ((doc.Layout?.Shell != null && LayoutSchema.IsKnownDensity(doc.Layout.Shell.Density)) ? doc.Layout.Shell.Density : LayoutSchema.DensityStandard);
		_state = LayoutEngine.LoadState(_pageId, page, _refs);
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
		// 原生默认排版(没自定义过、也不在编辑态):内容挂回页面原来的排版,区块卡片一律不出场
		if (_nativeActive && !_edit)
		{
			ShowNativeHost();
			RefreshToolbar();
			return;
		}
		LeaveNativeHost();
		_list.Children.Clear();
		LayoutPlan plan = _state.Plan();
		if (!IsBoxMode)
		{
			// 从 grid/canvas 回到 stack(完成编辑时还原、窄窗变体切换):卡片还挂在画布上,
			// 不先摘下来就会在下面 Add 时抛「指定的元素已经是另一个元素的逻辑子元素」。
			_box.Children.Clear();
		}
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
					card.EnsureContent();
					card.SetBoxMode(LayoutSchema.ModeStack);
					card.SetEdit(_edit);
					ApplyLook(card);
					AttachCard(_list, card);
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
		SanitizeCellWidths();
		_box.SnapStep = _snapStep;
		_box.EditMode = _edit;
		_box.Margin = (_marginOverride ?? PadOf(_state.Plan()));
		// 覆盖层与画布同坐标系:鼠标坐标取自 e.GetPosition(_box)(已扣掉 Margin),
		// 辅助线/框选层若不带同一个 Margin,整体会偏移 (padLeft, padTop),默认 (18,14)px(P2)
		_guide.Margin = _box.Margin;
		UpdateFillHeight();
		_box.Children.Clear();
		foreach (LayoutCell cell in LayoutCanvas.StackedOrder(_box.Cells))
		{
			LayoutCard card = CardFor(cell.Id);
			card.EnsureContent();
			card.SetBoxMode(page.Mode);
			card.SetEdit(_edit);
			card.SetSelected(_edit && _session.IsSelected(cell.Id));
			card.SetFixedHeight(PageHeightOf(cell.Id));
			ApplyLook(card);
			AttachCard(_box, card);
		}
		_box.ReattachOverlays();
		_box.Reflow();
	}

	// 渲染前的宽度兜底(只改画布里的格子,不写回存档):
	// 旧存档 / 外部导入的布局里可能有被缩成 1 列的块 —— 常态下没人调 ClampMinWidth,
	// 内容(插件卡片网格)被 FitBox 缩到看不见。这里按「全局 2 列 + 块自己的 MinCols」撑回来,冲突交给压紧解决。
	private void SanitizeCellWidths()
	{
		if (_box.IsCanvas)
		{
			return;
		}
		int cols = Math.Max(1, LayoutGrid.Columns(_state.Page));
		bool changed = false;
		foreach (LayoutCell cell in _box.Cells)
		{
			if (cell == null)
			{
				continue;
			}
			int min = Math.Min(cols, Math.Max(2, MinColsOf(cell.Id, cols)));
			if (cell.W < min)
			{
				cell.W = min;
				if (cell.X + cell.W > cols)
				{
					cell.X = Math.Max(0, cols - cell.W);
				}
				changed = true;
			}
		}
		if (changed)
		{
			// 撑宽后可能与同行的块重叠:只解冲突(不上浮),尽量保持用户原来的摆放
			_box.CompactCells(null, false, false);
		}
	}

	// BlockSpec.MinCols 按 12 列制给出,换算到本页列数
	private int MinColsOf(string id, int cols)
	{
		if (string.IsNullOrEmpty(id) || !_byId.TryGetValue(id, out BlockSpec? spec) || spec.MinCols <= 0)
		{
			return 0;
		}
		return (int)Math.Ceiling(spec.MinCols * (cols / 12.0));
	}

	// 占满剩余高度:画布可视高(滚动区高 - 画布上下边距)喂给画布,余量分给 Fill 块所在的行
	private void UpdateFillHeight()
	{
		if (_fillIds.Count == 0)
		{
			return;
		}
		double height = _boxScroll.ActualHeight - _box.Margin.Top - _box.Margin.Bottom;
		_box.FillHeight = ((height > 1.0) ? height : 0.0);
	}

	// ————— 原生默认排版 —————

	// 显示原生排版:先把内容从区块卡片里摘下来(同一个元素不能有两个父级),再交给页面挂回原位
	private void ShowNativeHost()
	{
		BlockNativeLayout? native = _native;
		if (native == null || native.Root == null)
		{
			return;
		}
		if (!_nativeShown)
		{
			_list.Children.Clear();
			_box.Children.Clear();
			_guide.Clear();
			foreach (LayoutCard card in _cards.Values)
			{
				card.ReleaseContent();
			}
			foreach (BlockSpec spec in _specs)
			{
				DetachFromParent(spec.Content);
			}
			native.Attach?.Invoke();
			_nativeShown = true;
		}
		if (_root.Children.Count != 0 && ReferenceEquals(_root.Children[0], native.Root))
		{
			return;
		}
		DetachFromParent(native.Root);
		if (_root.Children.Count != 0)
		{
			_root.Children.RemoveAt(0);
		}
		Grid.SetRow(native.Root, 0);
		_root.Children.Insert(0, native.Root);
	}

	// 离开原生排版:页面先把内容从原排版里摘下来,之后由区块卡片承载(Show*Host 会把根换成滚动宿主)
	private void LeaveNativeHost()
	{
		if (!_nativeShown)
		{
			return;
		}
		_nativeShown = false;
		_native?.Detach?.Invoke();
		foreach (BlockSpec spec in _specs)
		{
			DetachFromParent(spec.Content);
		}
	}

	/// <summary>把元素从它现在的父级上摘下来(ContentControl / Decorator / Panel 三种常见父级)。</summary>
	internal static void DetachFromParent(UIElement? element)
	{
		if (!(element is FrameworkElement framework) || framework.Parent == null)
		{
			return;
		}
		switch (framework.Parent)
		{
		case ContentControl contentControl:
			if (ReferenceEquals(contentControl.Content, element))
			{
				contentControl.Content = null;
			}
			break;
		case Decorator decorator:
			if (ReferenceEquals(decorator.Child, element))
			{
				decorator.Child = null;
			}
			break;
		case Panel panel:
			panel.Children.Remove(element);
			break;
		case ItemsControl items:
			items.Items.Remove(element);
			break;
		}
	}

	/// <summary>
	/// 把卡片挂到新的面板上:先从旧父级摘下来。
	/// 卡片是常驻缓存的,多列行(BuildRow 每次新建的 Grid)、画布、纵向流之间来回切换时
	/// 旧父级可能还持有它 —— 直接 Add 会抛「指定的元素已经是另一个元素的逻辑子元素。请先将其断开连接。」
	/// (2026-11-19 录屏 0:20:编辑态切页后回到启动页弹出的就是这个错误)。
	/// </summary>
	private static void AttachCard(Panel panel, LayoutCard card)
	{
		if (ReferenceEquals(card.Parent, panel) || ReferenceEquals(VisualTreeHelper.GetParent(card), panel))
		{
			if (!panel.Children.Contains(card))
			{
				panel.Children.Add(card);
			}
			return;
		}
		DetachFromParent(card);
		// 逻辑父级已断开但可视父级仍是别的 Panel(Panel.Children 被别处直接改过)时也摘一次
		if (VisualTreeHelper.GetParent(card) is Panel visualParent && !ReferenceEquals(visualParent, panel))
		{
			visualParent.Children.Remove(card);
		}
		panel.Children.Add(card);
	}

	/// <summary>
	/// 让当前内容区按滚轮增量滚动(能滚才滚,返回是否滚了)。
	/// 给页面用:鼠标在区块宿主之外(启动页英雄区、背景图)时,滚轮也能滚动自定义布局区。
	/// </summary>
	public bool ScrollBy(int delta)
	{
		if (_nativeActive && !_edit)
		{
			return false;
		}
		SmoothScroll host = (IsBoxMode ? _boxScroll : _scroll);
		if (!host.IsVisible || host.ScrollableHeight <= 0.5)
		{
			return false;
		}
		bool canMove = (delta < 0) ? (host.VerticalOffset < host.ScrollableHeight - 0.5) : (host.VerticalOffset > 0.5);
		if (!canMove)
		{
			return false;
		}
		host.WheelBy(delta);
		return true;
	}

	// 可视宿主二选一:stack = SPanel 纵向流(与 L1 逐像素一致),grid/canvas = 画布 Panel
	// 某块的固定像素高度(0 = 跟随内容)
	private double PageHeightOf(string id)
	{
		foreach (BlockLayout block in _state.Page.Blocks)
		{
			if (block.Id == id)
			{
				return block.HeightPx;
			}
		}
		return 0.0;
	}

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
		if (_autoPackBtn != null)
		{
			_autoPackBtn.IsEnabled = IsBoxMode;
			_autoPackBtn.Content = I18n.L("自动靠拢") + " " + (LayoutEditPrefs.AutoPack ? I18n.L("开") : I18n.L("关"));
		}
		RefreshFoldAllBtn();
	}

	// 「全部折叠/全部展开」文案 = 卡片的实际折叠状态(退出编辑会把卡片强制展开,文案必须跟着回去)
	internal void RefreshFoldAllBtn()
	{
		if (_foldAllBtn == null)
		{
			return;
		}
		_foldAllBtn.Content = I18n.L(IsAnyCardCollapsed() ? "全部展开" : "全部折叠");
	}

	private bool IsAnyCardCollapsed()
	{
		foreach (LayoutCard card in _cards.Values)
		{
			if (card.IsCollapsed)
			{
				return true;
			}
		}
		return false;
	}

	// 吸附档循环:0 → 4 → 8 → 16 → 24 → 0(与旧 DashLayout.Grid 的档位表一致)
	// 「自动靠拢」开关:全局偏好(所有页面共用),即时生效;关掉后不再自动上浮 / 互换 / 磁性吸附
	private void ToggleAutoPack()
	{
		LayoutEditPrefs.AutoPack = !LayoutEditPrefs.AutoPack;
		RefreshToolbar();
		AppServices.Window?.Toast(I18n.L(LayoutEditPrefs.AutoPack ? "自动靠拢已开启" : "自动靠拢已关闭"), LayoutEditPrefs.AutoPack ? I18n.L("区块松手后会自动靠拢相邻区块") : I18n.L("区块会停在你放下的位置"), ToastKind.Success);
	}

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
			card.EnsureContent();
			card.SetBoxMode(LayoutSchema.ModeStack);
			card.SetEdit(_edit);
			ApplyLook(card);
			int column = LayoutSchema.ClampInt(slot.Column, 0, columns - 1);
			Grid.SetColumn(card, column);
			Grid.SetColumnSpan(card, LayoutSchema.ClampInt(slot.Span, 1, columns - column));
			card.Margin = new Thickness(0.0, 0.0, ((k == row.Slots.Count - 1) ? 0.0 : plan.Gap), 0.0);
			AttachCard(grid, card);
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
		card.SetFill(_fillIds.Contains(id));
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
		_layoutTouched = true;
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
		_layoutTouched = true;
		_session.PushUndo(_state.ToPage());
		_state.Restore(spec.Id);
		Render();
		Persist();
	}

	// 重置本页 = 回到白名单默认布局(等价 PageLayoutDoc.Default)并落盘
	private void ResetPage()
	{
		try
		{
			// 先结束可能还没收尾的拖动手势并释放鼠标捕获:
			// 如果上一次拖动没收尾、鼠标还被区块抓着,工具条按钮根本收不到点击(录屏里"点了没反应")
			if (_gesture.Length != 0)
			{
				EndBoxGesture(false, false);
			}
			if (_box.IsMouseCaptured)
			{
				_box.ReleaseMouseCapture();
			}
			_session.PushUndo(_state.ToPage());
			_box.ClearDragVisuals();
			// 原生默认排版的页面:重置 = 把本页从布局文档里删掉,常态回到页面原来的排版(逐像素等价改动前)
			if (_native != null)
			{
				ResetToNative();
				return;
			}
			// 重置 = 回到本页出厂布局(含页模式),不能只重排顺序 —— 网格页的几何原样留着,看起来就是"点了没反应"。
			if (_defaultPage != null)
			{
				// 有「页面默认布局」的页(实例库)回到宿主给的那一份
				_state = LayoutEngine.LoadState(_pageId, _defaultPage.Clone(), _refs);
			}
			else
			{
				// 白名单默认 = 纵向 stack;Reset 清几何,自建/插件块的数据保留
				_state.Reset(_refs);
				PageLayoutV2 fresh = new PageLayoutV2();
				_state.Page.Mode = fresh.Mode;
				_state.Page.Cols = fresh.Cols;
				_state.Page.RowH = fresh.RowH;
				_state.Page.Gap = fresh.Gap;
				_state.Page.Pad = fresh.Pad;
			}
			_stackConverted = false;
			_stackBackup = null;
			Persist();
			if (_edit)
			{
				// 仍在编辑态:stack 默认页重新折成可拖网格;之后不再改动就完成编辑 = 落回默认 stack
				PrepareEditableGrid();
			}
			Render();
			// 重置后把所有折叠的区块展开(否则看起来像"没反应")
			foreach (LayoutCard card in _cards.Values)
			{
				card.SetCollapsed(false);
			}
			AppServices.Window?.Toast(I18n.L("布局已重置"), I18n.L("本页已恢复默认布局"), ToastKind.Success);
		}
		catch (Exception ex)
		{
			AppServices.Window?.Toast(I18n.L("重置失败"), ex.Message, ToastKind.Error);
		}
	}

	// 原生默认排版页的重置:删掉文档里的本页条目并落盘;仍在编辑态就按默认网格继续编辑,
	// 之后不再改动就完成编辑 = 回到原排版
	private void ResetToNative()
	{
		_saving = true;
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			// 窄窗下显示的是变体:重置也只删变体那一份(否则主 body 被删、窄版仍在,看起来"没重置")
			if (LayoutVariantWrite.Remove(doc, _pageId, _variant))
			{
				LayoutStoreV2.Save(doc);
				PageLayoutStore.Reload();
			}
		}
		finally
		{
			_saving = false;
		}
		LoadFromStore();
		_stackConverted = false;
		_stackBackup = null;
		if (_edit)
		{
			PrepareEditableGrid();
			CompactState(null);
		}
		_layoutTouched = false;
		Render();
		foreach (LayoutCard card in _cards.Values)
		{
			card.SetCollapsed(false);
		}
		this.Changed?.Invoke();
		AppServices.Window?.Toast(I18n.L("布局已重置"), I18n.L("本页已恢复默认布局"), ToastKind.Success);
	}

	/// <summary>给右键菜单用的重置入口(编辑态)。</summary>
	public void ResetPageFromMenu()
	{
		ResetPage();
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
		// 导入前的本页状态:导入后压进撤销栈,并且必须算"动过",否则完成编辑会走 RestoreStackAfterEdit
		// 把进编辑时的备份盖回来 —— 导入的内容看起来"回滚"了(P1)
		PageLayoutV2 before = _state.ToPage();
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
			PageLayoutV2 merged = LayoutEngine.MergeV1(existing, kv.Value);
			LayoutEngine.ApplyPage(doc, kv.Key, merged);
			// 窄窗变体里已有这一页:导入也要并进那一份,否则窄窗下看到的仍是旧变体(导入像"没生效")
			if (doc.Layout.Variants != null)
			{
				foreach (LayoutBody variantBody in doc.Layout.Variants.Values)
				{
					if (variantBody?.Pages != null && variantBody.Pages.TryGetValue(kv.Key, out PageLayoutV2? variantPage))
					{
						variantBody.Pages[kv.Key] = LayoutEngine.MergeV1(variantPage, kv.Value);
					}
				}
			}
		}
		LayoutStoreV2.Save(doc);
		PageLayoutStore.Reload();
		LoadFromStore();
		_layoutTouched = true;
		_session.PushUndo(before);
		_session.ClearSelection();
		Render();
		this.Changed?.Invoke();
	}

	// ————— 持久化 —————

	// 任何变更 → 取整份 v2 文档 → 覆盖本页 → Save(v2 + 写通 v1)→ 清 v1 缓存 → Changed
	// _saving 守卫:Save 会同步触发 LayoutChanged,自己不用被自己触发的那一次再重排一遍
	private void Persist()
	{
		_saving = true;
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			LayoutVariantWrite.Apply(doc, _pageId, _state.ToPage(), _variant);
			LayoutStoreV2.Save(doc);
			PageLayoutStore.Reload();
			// 写进文档之后本页就是"自定义过"的了:常态不再显示原生默认排版
			_nativeActive = false;
			// 任何落盘都算"动过"(撤销/重做/粘贴/微调/删块以前没置位,完成编辑时会被当成没动过而还原)
			_layoutTouched = true;
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
		_dragSrc = src;
		_dragChanged = false;
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
		if (!_edit || card == null || e.LeftButton != MouseButtonState.Pressed)
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
			_dragChanged = true;
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
		LayoutCard? card = _dragCard;
		bool changed = _dragChanged;
		CancelDrag(card, src);
		if (card == null)
		{
			return;
		}
		// 只点了一下手柄 / 已经退出编辑:没有换序就别落盘
		if (changed && _edit)
		{
			Persist();
		}
	}

	// 收掉 stack 拖拽的捕获与视觉(不落盘)。退出编辑 / 捕获丢失 / 正常松手都走这里。
	private void CancelDrag(LayoutCard? card, UIElement? src)
	{
		_dragCard = null;
		_dragSrc = null;
		_dragChanged = false;
		if (src != null)
		{
			src.MouseMove -= OnDragMove;
			src.MouseLeftButtonUp -= OnDragEnd;
			src.LostMouseCapture -= OnDragLost;
		}
		if (card != null)
		{
			card.Opacity = 1.0;
		}
		if (src != null && src.IsMouseCaptured)
		{
			src.ReleaseMouseCapture();
		}
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
		DetachFromParent(card);
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
		_session.PushUndo(_state.ToPage());
		_gesture = kind;
		_gestureHandle = "";
		_gestureStart = e.GetPosition(_box);
		_gestureCells.Clear();
		foreach (string id in _session.OrderedSelection(_state.Order))
		{
			LayoutCell? cell = _box.CellOf(id);
			if (cell != null)
			{
				_gestureCells[id] = cell.Clone();
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
		// 锚点取手势主角(多选时是选择里的第一块)的矩形:落点按"主角左上角 + 位移"算,与主角的起始格子对得上
		_gestureAnchor = (_box.RectOf(PrimaryId() ?? card.Spec.Id) ?? default(LayoutRect));
		_gestureSnapshot.Clear();
		foreach (LayoutCell c in _box.Cells)
		{
			if (c != null)
			{
				_gestureSnapshot[c.Id] = c.Clone();
			}
		}
		_gestureRows = _box.SnapshotRowHeights();
		_gestureMoved = false;
		_lastDropX = int.MinValue;
		_lastDropY = int.MinValue;
		_lastFitW = int.MinValue;
		_lastSnapW = int.MinValue;
		_lastSnapH = int.MinValue;
		_lastSnapPx = double.NaN;
		_snapDx = 0.0;
		_snapDy = 0.0;
		_box.CaptureMouse();
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
		// grid:被拖的卡片(多选时整组)浮起来按像素跟手,原格子上出现落点占位框
		if (!_box.IsCanvas)
		{
			_box.BeginFloat(new List<string>(_gestureCells.Keys), true);
		}
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
		_pixelHeightGesture = false;
		// 只有"行高跟随内容"的网格才用像素高度;固定行高的页面仍然按行缩放(规范:行步长用真实行高)
		if (IsBoxMode && !_box.IsCanvas && handle == "s" && _state.Page.RowH <= 0.0)
		{
			_pixelHeightGesture = true;
			LayoutRect? rect = _box.RectOf(card.Spec.Id);
			_gestureStartHeight = (rect.HasValue ? rect.Value.H : (card.ActualHeight > 0.0 ? card.ActualHeight : 120.0));
		}
		// grid:卡片按像素实时变大小(浮起),占位框显示吸附后的尺寸
		if (IsBoxMode && !_box.IsCanvas)
		{
			_box.BeginFloat(new string[1] { card.Spec.Id }, true);
		}
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
		Point point = e.GetPosition(_box);
		double dx = point.X - _gestureStart.X;
		double dy = point.Y - _gestureStart.Y;
		if (!_gestureMoved && Math.Abs(dx) < 2.0 && Math.Abs(dy) < 2.0)
		{
			return; // 手抖 / 单击选中:不算拖动
		}
		_gestureMoved = true;
		AutoScrollNearEdge(point);
		if (_box.IsCanvas)
		{
			// canvas:只改槽位 + 重新排(画布不重新测量;尺寸变了的那一块在 Arrange 里补量)
			ApplyCanvasGesture(dx, dy);
			_box.InvalidateArrange();
		}
		else
		{
			// grid:每帧只动被拖卡片的 RenderTransform / 预览矩形;落点或吸附尺寸变了才重排一次
			ApplyGridGesture(dx, dy);
		}
	}

	private void OnBoxGestureEnd(object sender, MouseButtonEventArgs e)
	{
		EndBoxGesture(true, false);
	}

	private void OnBoxGestureLost(object sender, MouseEventArgs e)
	{
		EndBoxGesture(false, false);
	}

	// 拖动/缩放时鼠标靠近可视区上下边缘 → 自动滚动(方便把很高的块拖短或拖远)
	private void AutoScrollNearEdge(Point pointInBox)
	{
		// grid/canvas 的可视宿主是 _boxScroll(原来误用了 stack 的 _scroll:它此时不在可视树里,视口高 0,自动滚动从没生效)
		SmoothScroll scroll = _boxScroll;
		if (!IsBoxMode || scroll == null)
		{
			return;
		}
		double height = scroll.ViewportHeight;
		if (height <= 0.0)
		{
			return;
		}
		double y = pointInBox.Y + _box.Margin.Top - scroll.VerticalOffset;
		double step = 0.0;
		if (y < 36.0)
		{
			step = -18.0;
		}
		else if (y > height - 36.0)
		{
			step = 18.0;
		}
		if (step == 0.0)
		{
			return;
		}
		double to = Math.Max(0.0, Math.Min(scroll.ScrollableHeight, scroll.VerticalOffset + step));
		if (Math.Abs(to - scroll.VerticalOffset) > 0.01)
		{
			scroll.ScrollToVerticalOffset(to);
		}
	}

	// 手势收尾:解绑事件 → (取消?回滚:落盘)→ 重画
	private void EndBoxGesture(bool persist, bool rollback)
	{
		bool active = _gesture.Length != 0;
		bool moved = _gestureMoved;
		bool pixelHeight = _pixelHeightGesture;
		string kind = _gesture;
		string? primary = PrimaryId();
		// 像素高度:拖动中只预览(占位框显示吸附后的高度,其它块不动),松手才写进槽位,再统一压紧 + 让位动画
		if (active && persist && moved && pixelHeight && primary != null && !double.IsNaN(_lastSnapPx))
		{
			LayoutCell? lead = _box.CellOf(primary);
			if (lead != null)
			{
				lead.HeightPx = _lastSnapPx;
			}
		}
		_gesture = "";
		_gestureHandle = "";
		_gestureCells.Clear();
		_gestureSnapshot.Clear();
		_gestureRows = Array.Empty<double>();
		_pixelHeightGesture = false;
		_gestureMoved = false;
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
		if (kind == "marquee")
		{
			return; // 框选只改选择,不动布局(原来这里也会提交一次)
		}
		// 浮起的卡片(grid)从鼠标位置动画落入它的格子,落定后去掉占位框;
		// 其它块因为最后一次压紧 / 回滚而移动的,由下面的 RequestFlip 从旧位置平滑过去。
		_box.Settle(null);
		_box.RequestFlip();
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
		if (!moved)
		{
			// 只是点了一下(选中),没拖:不算改布局,不落盘(原生默认排版的页面因此不会被"点一下"变成自定义布局)
			_session.DiscardUndo();
			RefreshToolbar();
			return;
		}
		if (persist)
		{
			CommitBox();
		}
		else
		{
			// 捕获丢失(切换窗口 / 外部抢焦点)不是"正常松手":回滚到起手快照并丢掉那份额外的撤销,
			// 否则撤销栈里会留下"等于当前状态"的空快照(P2)
			PageLayoutV2? back = _session.DiscardUndo();
			if (back != null)
			{
				_state = LayoutEngine.LoadState(_pageId, back, _refs);
				_session.Retain(_state.Order);
			}
			Render();
		}
	}

	// 画布槽位 → 页面块 → v2(唯一入口 LayoutStoreV2)并落盘
	// 进编辑 / 每次提交后压紧一次:旧存档里被放到很靠下、看起来"消失"的区块会浮回能看到的位置。
	// 只在编辑过程中生效 —— 不进编辑就不动用户原有布局(比如启动页刻意留出的空白)。
	private void CompactState(ICollection<string>? pinned)
	{
		if (!IsBoxMode || !_edit)
		{
			return;
		}
		_box.CompactCells(pinned, false, LayoutEditPrefs.AutoPack);
		int cols = LayoutGrid.Columns(_state.Page);
		foreach (LayoutCell cell in _box.Cells)
		{
			if (cell != null && cell.W < 2)
			{
				ClampMinWidth(cell, cols);
			}
		}
		_box.Reflow();
	}

	private void CommitBox()
	{
		_layoutTouched = true;
		CompactState(null);
		PageLayoutV2 page = _state.ToPage();
		_box.ApplyTo(page);
		_state = LayoutEngine.LoadState(_pageId, page, _refs);
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
			// 「自动靠拢」关:只画对齐辅助线,不做磁性吸附(不再"自动靠到旁边的块上")
			bool magnet = LayoutEditPrefs.AutoPack && _gesture == "move" && guides.HasSnap;
			// 磁性吸附"吸上 / 松开 / 换线"的那一下:不硬跳,被拖块用 90ms 短动画从上一帧的位置滑过去
			double snapDx = (magnet ? guides.Dx : 0.0);
			double snapDy = (magnet ? guides.Dy : 0.0);
			if (LayoutMotion.SnapChanged(_snapDx, _snapDy, snapDx, snapDy))
			{
				_box.RequestFlip(new List<string>(_gestureCells.Keys), LayoutMotion.SnapMs);
			}
			_snapDx = snapDx;
			_snapDy = snapDy;
			if (magnet)
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

	// grid 拖动 / 缩放(丝滑版):
	//   每帧:被拖卡片按像素跟手(浮起卡片的 RenderTransform / 缩放预览矩形),不触发重排;
	//   落点(或吸附后的尺寸)变了才:从手势快照恢复 → 移动/缩放被拖块 → 固定它压紧(其余块让位)→
	//   RequestFlip(让位的块从旧位置平滑动画到新位置)→ Reflow 一次。占位框始终画在被拖块的格子上。
	// 落点判定与原来相同:被拖块左上角取最近列、指针所在行(手势开始时的行高快照),见 LayoutMotion.GridDropTarget。
	// grid 下不显示对齐辅助线(格子吸附已足够,辅助线吸附会和让位逻辑互相干扰)。
	private void ApplyGridGesture(double dx, double dy)
	{
		int cols = LayoutGrid.Columns(_state.Page);
		double columnStep = Math.Max(1.0, _box.ColumnWidthValue + _box.Gap);
		string? primary = PrimaryId();
		if (primary == null)
		{
			return;
		}
		LayoutCell? from = (_gestureSnapshot.TryGetValue(primary, out LayoutCell? snap) ? snap : null);
		if (from == null)
		{
			return;
		}
		_guide.Clear();
		if (_gesture == "resize")
		{
			// 卡片按像素实时变大小(最窄 2 列,最矮 56px)
			double minW = Math.Max(1.0, _box.ColumnWidthValue * Math.Min(2, cols) + _box.Gap * (Math.Min(2, cols) - 1));
			LayoutRect live = LayoutMotion.ResizePreview(_gestureAnchor, _gestureHandle, dx, dy, minW, LayoutMotion.MinHeightPx);
			_box.ResizeFloat(primary, live);
			if (_pixelHeightGesture)
			{
				// 下边整条边 = 像素高度(4px 吸附,最小 56):占位框显示吸附后的高度;其它块松手后再让位
				double px = LayoutMotion.SnapPixelHeight(_gestureStartHeight, dy);
				if (px != _lastSnapPx)
				{
					_lastSnapPx = px;
					_box.SetPlaceholderRect(primary, new LayoutRect(_gestureAnchor.X, _gestureAnchor.Y, _gestureAnchor.W, px));
				}
				return;
			}
			// 按格缩放:列按列宽折,行按真实行高折(以前用被拖卡片自身高度,高卡片几乎拖不动)
			double rowStep = LayoutMotion.RowStep(_gestureRows, _box.Gap, from.Y + Math.Max(0, from.H - 1), LayoutCanvas.FallbackMinH);
			int stepX = (int)Math.Round(dx / columnStep);
			int stepY = (int)Math.Round(dy / rowStep);
			LayoutCell probe = from.Clone();
			LayoutGrid.ResizeCell(probe, _gestureHandle, stepX, stepY, cols);
			ClampMinWidth(probe, cols);
			if (probe.W == _lastSnapW && probe.H == _lastSnapH && probe.X == _lastDropX && probe.Y == _lastDropY)
			{
				return;
			}
			_lastSnapW = probe.W;
			_lastSnapH = probe.H;
			_lastDropX = probe.X;
			_lastDropY = probe.Y;
			RestoreGestureSnapshot();
			LayoutCell? lead = _box.CellOf(primary);
			if (lead != null)
			{
				lead.X = probe.X;
				lead.Y = probe.Y;
				lead.W = probe.W;
				lead.H = probe.H;
			}
			CompactAroundGesture(primary, false);
			return;
		}
		(int targetX, int targetY) = LayoutMotion.GridDropTarget(_gestureAnchor, dx, dy, columnStep, _gestureRows, _box.Gap, cols, from.W);
		_box.MoveFloat(dx, dy);
		bool multi0 = _gestureCells.Count > 1;
		// 并排落位:被拖块太宽、放在目标行会和别的块打架,但指针落在该行的一段空列上 →
		// 收窄到那段空列,与那一行的块并排(录屏:「版本列表」拖到「筛选」右边,松手应与筛选并排,原来会弹回整行)
		int fitX = -1;
		int fitW = 0;
		if (!multi0)
		{
			int rawX = (int)Math.Round((_gestureAnchor.X + dx) / columnStep);
			(fitX, fitW) = SideBySideSlot(primary, rawX, targetY, from.W, from.H, cols);
			if (fitX >= 0)
			{
				// 并排宽度不得低于这块的 MinCols:否则下次渲染 SanitizeCellWidths 会把它撑宽挪位 = "并排弹回"(P2)
				int minW = LayoutGestureRules.SideBySideMinWidth(MinColsOf(primary, cols), cols);
				if (fitW < minW)
				{
					fitX = -1;
					fitW = 0;
				}
				else
				{
					targetX = fitX;
				}
			}
		}
		// 落点键:并排用正数(targetX + 1),普通落位用负数,再单独比较 fitW ——
		// 原来 targetX*100 + fitW 会让 (targetX=0, fitW=2) 与普通 targetX=2 撞键(P2)
		bool fitting = fitX >= 0;
		int dropKey = (fitting ? (targetX + 1) : -(targetX + 1));
		int fitWKey = (fitting ? fitW : 0);
		if (LayoutGestureRules.SameDrop(dropKey, fitWKey, targetY, _lastDropX, _lastFitW, _lastDropY))
		{
			return; // 落点没变:这一帧只有被拖卡片在动
		}
		_lastDropX = dropKey;
		_lastFitW = fitWKey;
		_lastDropY = targetY;
		RestoreGestureSnapshot();
		if (fitX >= 0)
		{
			LayoutCell? lead0 = _box.CellOf(primary);
			if (lead0 != null)
			{
				lead0.X = fitX;
				lead0.Y = targetY;
				lead0.W = fitW;
				_box.SetPlaceholderRect(primary, null);
				CompactAroundGesture(primary, false);
				return;
			}
		}
		int stepX2 = targetX - from.X;
		int stepY2 = targetY - from.Y;
		bool multi = _gestureCells.Count > 1;
		if (stepX2 != 0 || stepY2 != 0)
		{
			if (multi)
			{
				foreach (KeyValuePair<string, LayoutCell> kv in _gestureCells)
				{
					LayoutCell? cell = _box.CellOf(kv.Key);
					if (cell != null)
					{
						LayoutGrid.MoveCell(cell, stepX2, stepY2, cols, 0);
					}
				}
			}
			else
			{
				LayoutCell? lead2 = _box.CellOf(primary);
				if (lead2 != null)
				{
					LayoutGrid.MoveCell(lead2, stepX2, stepY2, cols, 0);
				}
			}
		}
		CompactAroundGesture(primary, !multi);
	}

	/// <summary>
	/// 并排落位判定(纯格子运算,用手势开始时的快照):目标行 <paramref name="row"/> 上,
	/// 被拖块按原宽 <paramref name="w"/> 放不下(与其它块重叠或越界),但 <paramref name="rawX"/>(被拖块左边所在列)
	/// 落在一段 ≥ 2 列的空列区间里 → 返回 (起始列, 宽度),让它收窄后与该行的块并排;否则返回 (-1, 0)。
	/// </summary>
	private (int X, int W) SideBySideSlot(string primary, int rawX, int row, int w, int h, int cols)
	{
		if (cols < 2 || row < 0)
		{
			return (-1, 0);
		}
		int height = Math.Max(1, h);
		bool[] busy = new bool[cols];
		bool anyOther = false;
		foreach (KeyValuePair<string, LayoutCell> kv in _gestureSnapshot)
		{
			if (string.Equals(kv.Key, primary, StringComparison.Ordinal))
			{
				continue;
			}
			LayoutCell c = kv.Value;
			int top = c.Y;
			int bottom = c.Y + Math.Max(1, c.H);
			// 与被拖块将占的行区间 [row, row + height) 有交集的块都算"这一行的块"
			if (bottom <= row || top >= row + height)
			{
				continue;
			}
			anyOther = true;
			for (int x = Math.Max(0, c.X); x < Math.Min(cols, c.X + Math.Max(1, c.W)); x++)
			{
				busy[x] = true;
			}
		}
		if (!anyOther)
		{
			return (-1, 0);
		}
		int px = Math.Max(0, Math.Min(cols - 1, rawX));
		// 原宽本来就放得下(不重叠):不用收窄,走原逻辑
		int clampX = Math.Max(0, Math.Min(cols - Math.Max(1, w), px));
		bool fits = true;
		for (int x = clampX; x < clampX + Math.Max(1, w) && x < cols; x++)
		{
			if (busy[x])
			{
				fits = false;
				break;
			}
		}
		if (fits)
		{
			return (-1, 0);
		}
		if (busy[px])
		{
			// 左边落在别的块上:只有越过那块的一半,才算"想放到它右边"(纯上下拖动时左边停在第 0 列,不触发)
			int segStart = px;
			while (segStart > 0 && busy[segStart - 1])
			{
				segStart--;
			}
			int segEnd = px;
			while (segEnd < cols && busy[segEnd])
			{
				segEnd++;
			}
			if ((px - segStart) * 2 < (segEnd - segStart))
			{
				return (-1, 0);
			}
			int next = px;
			while (next < cols && busy[next])
			{
				next++;
			}
			if (next >= cols)
			{
				return (-1, 0);
			}
			px = next;
		}
		int start = px;
		while (start > 0 && !busy[start - 1])
		{
			start--;
		}
		int end = px;
		while (end < cols && !busy[end])
		{
			end++;
		}
		int span = end - start;
		if (span < 2)
		{
			return (-1, 0);
		}
		return (start, Math.Min(span, Math.Max(2, w)));
	}

	// 每次重算都从手势开始时的全量快照出发(避免在"当前值"上累加,越拖越乱)
	private void RestoreGestureSnapshot()
	{
		foreach (KeyValuePair<string, LayoutCell> kv in _gestureSnapshot)
		{
			LayoutCell? cell = _box.CellOf(kv.Key);
			if (cell != null)
			{
				LayoutCell from = kv.Value;
				cell.X = from.X;
				cell.Y = from.Y;
				cell.W = from.W;
				cell.H = from.H;
				cell.HeightPx = from.HeightPx;
			}
		}
	}

	// 固定被拖块(多选时整组)压紧:其余块让位 / 浮上来;位置变了的块从旧位置平滑动画过去(FLIP),只重排这一次
	private void CompactAroundGesture(string primary, bool swapX)
	{
		HashSet<string> pinned = new HashSet<string>(StringComparer.Ordinal);
		if (_gestureCells.Count > 1)
		{
			foreach (string id in _gestureCells.Keys)
			{
				pinned.Add(id);
			}
		}
		else
		{
			pinned.Add(primary);
		}
		_box.RequestFlip();
		_box.CompactCells(pinned, swapX, LayoutEditPrefs.AutoPack);
	}

	// 最小宽度:12 列网格下不小于 2 列(1 列宽会把标题挤成竖排)
	private static void ClampMinWidth(LayoutCell cell, int cols)
	{
		if (cols < 2 || cell == null)
		{
			return;
		}
		if (cell.W < 2)
		{
			cell.W = 2;
		}
		if (cell.X + cell.W > cols)
		{
			cell.X = Math.Max(0, cols - cell.W);
		}
	}

	// 手势主角:多选时取选择里的第一块(它带动整组)
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
		if (key == LayoutKey.None)
		{
			return; // 不抢页面自己的快捷键
		}
		// 手势进行中只放行 Esc(取消);Delete / 方向键 / Ctrl+Z/Y/C/V 一律等松手再响应
		if (!LayoutGestureRules.ShouldHandleKey(key, _gesture.Length != 0))
		{
			e.Handled = true;
			return;
		}
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
		// 先深拷贝再改,改成功才压撤销:原来先就地改 _state.Page(与 ToPage().Blocks 共享实例)再 PushUndo,
		// 撤销回去的是"改过之后"的状态,等于撤销错一格(P1)
		PageLayoutV2? page = LayoutNudge.Apply(_state.ToPage(), _session, _session.Selection, dx, dy, shift, AxisWidth(), AxisHeight(), _snapStep);
		if (page == null)
		{
			return;
		}
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

	private readonly ScrollViewer _bodyScroll;
	private readonly Grid _handleLayer;

	private readonly Border _edgeE;
	private readonly Border _edgeS;
	private readonly Button _collapseBtn;
	private readonly FitBox _fit = new FitBox();
	private bool _collapsed;

	private readonly Dictionary<string, Border> _handles = new Dictionary<string, Border>(StringComparer.Ordinal);

	private readonly ContentControl _body = new ContentControl
	{
		Focusable = false
	};

	private bool _edit;

	private bool _selected;

	// 卡片承载的内容(原生默认排版显示时会被临时摘走,回到区块形态时再挂回来)
	private readonly UIElement? _content;

	// 占满剩余高度的块:不套外层滚动、不做窄宽缩放,内容拿到有限高度后自己滚动
	private bool _fill;

	private string _boxMode = LayoutSchema.ModeStack;

	private string _lookStyle = LayoutSchema.StylePlain;

	private bool _lookTitle;

	private double[]? _lookPad;

	public BlockSpec Spec { get; }

	public LayoutCard(LayoutEditor host, BlockSpec spec, UIElement? content)
	{
		_host = host;
		Spec = spec;
		CornerRadius = new CornerRadius(10.0);
		SetResourceReference(Border.BorderBrushProperty, "B.Accent");
		// 操作条:拖动图标(不可点,纯提示)+ 块标题 + 显隐按钮。
		// ⇅ 原来是个按钮,让人以为"必须点它才能拖";现在编辑态下整张卡任意位置都能按住拖
		// (见 OnCardMouseDown),图标只留作提示。
		Button uIElement = Ui.IconBtn(Ico.Sort, I18n.L("按住拖动"), null, 12.0);
		uIElement.IsHitTestVisible = false;
		uIElement.Focusable = false;
		// 折叠按钮(只在编辑态出现):把区块收成一行标题栏,方便腾地方挪别的块;不写进布局。
		_collapseBtn = Ui.IconBtn(Ico.ChevronUp, I18n.L("折叠"), delegate
		{
			SetCollapsed(!_collapsed);
			_host.RefreshFoldAllBtn();
		}, 12.0);
		Button button = Ui.IconBtn(Ico.Close, I18n.L("隐藏"), delegate
		{
			_host.HideBlock(Spec);
		}, 12.0);
		TextBlock textBlock = Ui.Txt(spec.Title, 12.5, bold: true);
		textBlock.VerticalAlignment = VerticalAlignment.Center;
		textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
		_bar = Ui.H(8.0, uIElement, textBlock, Ui.Spring(), _collapseBtn, button);
		_bar.Margin = new Thickness(6.0, 6.0, 6.0, 2.0);
		// 常态块标题(§4.B 的 title;缺省 false → 整行折叠,与改动前一致)
		TextBlock titleText = Ui.Txt(spec.Title, 12.5, bold: true);
		titleText.TextTrimming = TextTrimming.CharacterEllipsis;
		_titleRow = Ui.H(8.0, titleText, Ui.Spring());
		_titleRow.Margin = new Thickness(0.0, 0.0, 0.0, 6.0);
		_titleRow.Visibility = Visibility.Collapsed;
		// 内容体外壳:常态零边距(逐像素等价旧版),编辑态留一点呼吸边距
		// 内容体:FitBox(窄了等比缩小,不裁切)+ ScrollViewer(设了 height_px 时内容在块内滚动)
		_fit.Child = _body;
		_bodyScroll = new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			Content = _fit
		};
		_bodyWrap = new Border
		{
			Padding = new Thickness(0.0),
			Child = _bodyScroll
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
		foreach (string name in LayoutCanvas.HandleNames)
		{
			AddHandle(name);
		}
		// 整条边也能拖(不再要求对准小方块):右边 → 改宽,下边 → 改高(双击 = 取消固定高度)
		_edgeE = EdgeStrip("e", Cursors.SizeWE);
		_handleLayer.Children.Add(_edgeE);
		_edgeS = EdgeStrip("s", Cursors.SizeNS);
		_edgeS.PreviewMouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e)
		{
			if (e.ClickCount >= 2)
			{
				_host.ClearFixedHeight(Spec);
				e.Handled = true;
			}
		};
		_handleLayer.Children.Add(_edgeS);
		grid.Add(_handleLayer, 0, 0, 3, 1);
		Child = grid;
		_bar.Visibility = Visibility.Collapsed;
		// 画布模式:块身按下 = 整体拖拽(用冒泡事件而不是 Preview —— 操作条/手柄自己的处理器先跑并置 Handled)
		base.MouseLeftButtonDown += OnCardMouseDown;
		// 内容已挂在别的父级上时先摘下来(对齐 DashHost.SetContent;原生默认排版里的父级可能是 Panel / Decorator)
		_content = content;
		LayoutEditor.DetachFromParent(content);
		_body.Content = content;
	}

	/// <summary>把内容还给页面(原生默认排版要把它挂回原位)。</summary>
	public void ReleaseContent()
	{
		if (_body.Content != null)
		{
			_body.Content = null;
		}
	}

	/// <summary>确保内容挂在本卡上(从原生默认排版回到区块形态时调用)。</summary>
	public void EnsureContent()
	{
		if (_content == null || ReferenceEquals(_body.Content, _content))
		{
			return;
		}
		LayoutEditor.DetachFromParent(_content);
		_body.Content = _content;
	}

	/// <summary>占满剩余高度(BlockSpec.Fill):内容区不再套滚动,内容自己的滚动 / 吸底照常工作。</summary>
	public void SetFill(bool fill)
	{
		_fill = fill;
		_bodyScroll.VerticalScrollBarVisibility = (fill ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
		_fit.MaxNaturalHeight = ((fill || _boxMode == LayoutSchema.ModeStack) ? 0.0 : 170.0);
	}

	/// <summary>折叠/展开(只在编辑态用;不写进布局)。</summary>
	public void SetCollapsed(bool collapsed)
	{
		_collapsed = collapsed;
		_bodyWrap.Visibility = (collapsed ? Visibility.Collapsed : Visibility.Visible);
		_collapseBtn.Content = Ui.Glyph(Ico.ChevronDown, 12.0);
		if (!collapsed)
		{
			_collapseBtn.Content = Ui.Glyph(Ico.ChevronUp, 12.0);
		}
	}

	public bool IsCollapsed => _collapsed;

	/// <summary>整条边的拖拽条(透明;光标在边上就变成双向箭头)。</summary>
	private Border EdgeStrip(string name, Cursor cursor)
	{
		Border strip = new Border
		{
			Background = Brushes.Transparent,
			Tag = name,
			Cursor = cursor,
			HorizontalAlignment = (name == "e" ? HorizontalAlignment.Right : HorizontalAlignment.Stretch),
			VerticalAlignment = (name == "s" ? VerticalAlignment.Bottom : VerticalAlignment.Stretch)
		};
		if (name == "e")
		{
			strip.Width = 7.0;
		}
		else
		{
			strip.Height = 7.0;
		}
		string captured = name;
		strip.PreviewMouseLeftButtonDown += delegate(object obj, MouseButtonEventArgs e)
		{
			_host.BeginBoxResize(this, captured, e);
		};
		return strip;
	}

	/// <summary>固定像素高度(0 = 跟随内容):内容超出时在块内滚动。</summary>
	public void SetFixedHeight(double px)
	{
		if (px > 0.0)
		{
			_bodyScroll.MaxHeight = px;
			_bodyScroll.VerticalScrollBarVisibility = (_fill ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
		}
		else
		{
			_bodyScroll.MaxHeight = double.PositiveInfinity;
		}
	}

	/// <summary>切画布模式(grid / canvas;stack = 缺省,手柄层折叠)。</summary>
	public void SetBoxMode(string mode)
	{
		_boxMode = ((mode == LayoutSchema.ModeGrid || mode == LayoutSchema.ModeCanvas) ? mode : LayoutSchema.ModeStack);
		// 窄宽等比缩放只在网格/自由画布里生效(常态纵向流按原样排版,逐像素等价旧版)
		_fit.MaxNaturalHeight = ((_fill || _boxMode == LayoutSchema.ModeStack) ? 0.0 : 170.0);
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
		_bar.Show(edit);
		_collapseBtn.Visibility = (edit ? Visibility.Visible : Visibility.Collapsed);
		_body.IsHitTestVisible = !edit;
		if (!edit)
		{
			SetCollapsed(false); // 退出编辑自动展开
		}
		// 编辑态整卡可拖:光标给十字移动箭头,提示"按住就能拖"
		Cursor = (edit ? Cursors.SizeAll : null);
		UpdateHandles();
		ApplyLook();
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
		if (_edgeE != null)
		{
			_edgeE.Visibility = Visibility.Visible;
		}
		if (_edgeS != null)
		{
			_edgeS.Visibility = Visibility.Visible;
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
		if (!_edit)
		{
			return;
		}
		// 编辑态:整张卡任意位置都能按住拖 —— stack 走一维换序(旧行为),grid/canvas 走画布整体拖拽。
		// 显隐按钮是 ButtonBase,自己会把 MouseLeftButtonDown 标 Handled,所以不会误触发拖动。
		if (_boxMode == LayoutSchema.ModeStack)
		{
			_host.BeginHandleDrag(this, this, e);
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

// ————— 编辑交互的纯规则(零 WPF;net8 测试宿主可直接断言) —————
// 为什么不放进 LayoutEngine.cs:那一份由并行的布局引擎 lane 负责,这里只放 LayoutEditor 自己的交互语义,
// 用独立的小类型收口,便于单测钉住规则而不需要实例化 WPF 控件。

/// <summary>手势 / 落点 / 键盘的纯规则。</summary>
public static class LayoutGestureRules
{
	/// <summary>手势进行中只放行 Esc(取消),其它布局快捷键(Delete / 方向键 / Ctrl+Z/Y/C/V)一律不响应。</summary>
	public static bool ShouldHandleKey(LayoutKey key, bool gestureActive)
	{
		return !gestureActive || key == LayoutKey.Escape;
	}

	/// <summary>
	/// 并排落位的最小宽度:<paramref name="scaledMinCols"/>(块自己的 MinCols 换算到本页列数)与全局 2 列取大。
	/// 空列 span 小于这个宽度就不并排 —— 否则下次渲染被 SanitizeCellWidths 按 MinCols 撑宽挪位("并排弹回")。
	/// </summary>
	public static int SideBySideMinWidth(int scaledMinCols, int cols)
	{
		return Math.Min(Math.Max(1, cols), Math.Max(2, scaledMinCols));
	}

	/// <summary>
	/// 这一帧的落点是否与上一帧相同。并排列(正数 targetX+1)与普通落位(负数 targetX+1)分开编码,
	/// fitW 单独比较 —— 原来的 targetX*100+fitW 会让 (targetX=0, fitW=2) 与普通 targetX=2 同键。
	/// </summary>
	public static bool SameDrop(int dropKey, int fitW, int targetY, int lastDropKey, int lastFitW, int lastTargetY)
	{
		return dropKey == lastDropKey && fitW == lastFitW && targetY == lastTargetY;
	}
}

/// <summary>
/// 窄窗变体的读写(纯规则):显示哪一份(主 body / variants.narrow)就只写哪一份,
/// 否则窄窗下的编辑会用窄版覆盖宽版。
/// </summary>
public static class LayoutVariantWrite
{
	/// <summary>这一页当前显示自哪一份布局:变体键名;空串 = 主 body(与 Linux 侧 LayoutVariants.SourceOf 同语义)。</summary>
	public static string SourceOf(LayoutBody? body, string pageId, double availableWidth, double threshold)
	{
		if (body == null || string.IsNullOrEmpty(pageId))
		{
			return "";
		}
		LayoutBody? variant = LayoutVariants.BodyOf(body, availableWidth, threshold);
		if (variant != null && variant.Pages != null && variant.Pages.TryGetValue(pageId, out PageLayoutV2? fromVariant) && fromVariant != null)
		{
			return LayoutVariants.NameOf(availableWidth, threshold);
		}
		return "";
	}

	/// <summary>把一页写回对应的 body(<paramref name="variant"/> 为空 = 主 body)。</summary>
	public static void Apply(LayoutV2 doc, string pageId, PageLayoutV2 page, string? variant)
	{
		if (!string.IsNullOrEmpty(variant) && doc != null && !string.IsNullOrEmpty(pageId) && page != null)
		{
			if (doc.Layout == null)
			{
				doc.Layout = new LayoutBody();
			}
			if (doc.Layout.Variants == null)
			{
				doc.Layout.Variants = new Dictionary<string, LayoutBody>(StringComparer.Ordinal);
			}
			if (!doc.Layout.Variants.TryGetValue(variant, out LayoutBody? body) || body == null)
			{
				body = new LayoutBody();
				doc.Layout.Variants[variant] = body;
			}
			if (body.Pages == null)
			{
				body.Pages = new Dictionary<string, PageLayoutV2>(StringComparer.Ordinal);
			}
			body.Pages[pageId] = page;
			return;
		}
		LayoutEngine.ApplyPage(doc, pageId, page);
	}

	/// <summary>从对应的 body 删掉一页;返回是否真的删了。窄窗重置 = 只删变体那份。</summary>
	public static bool Remove(LayoutV2 doc, string pageId, string? variant)
	{
		if (doc == null || doc.Layout == null || string.IsNullOrEmpty(pageId))
		{
			return false;
		}
		LayoutBody body = doc.Layout;
		if (!string.IsNullOrEmpty(variant) && doc.Layout.Variants != null && doc.Layout.Variants.TryGetValue(variant, out LayoutBody? variantBody) && variantBody != null)
		{
			body = variantBody;
		}
		return body.Pages != null && body.Pages.Remove(pageId);
	}
}

/// <summary>方向键微调的事务语义:先深拷贝、改动成功才压撤销(失败不留空快照,成功可精确退一格)。</summary>
public static class LayoutNudge
{
	/// <summary>
	/// 返回改好的页面;没有块真的移动时返回 null(不压撤销)。<paramref name="current"/> 不会被改动。
	/// </summary>
	public static PageLayoutV2? Apply(PageLayoutV2? current, LayoutEditSession? session, IEnumerable<string>? selection, int dx, int dy, bool shift, double canvasWidth, double canvasHeight, double snapStep)
	{
		if (current == null || session == null)
		{
			return null;
		}
		PageLayoutV2 work = current.Clone();
		if (LayoutEditSession.Nudge(work, selection, dx, dy, shift, canvasWidth, canvasHeight, snapStep) == 0)
		{
			return null;
		}
		session.PushUndo(current);
		return work;
	}
}
