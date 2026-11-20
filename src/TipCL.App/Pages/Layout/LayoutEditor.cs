// 来源: Windows 版参考仓库 src/TipCL/Pages/Layout/LayoutEditor.cs。
// 移植源头 = L1 ∪ L2 的并集;逐行对照件 = D:\项目\TipCL\wt-r31-merge @0a304a7 的
// `windos/src/TipCL/Pages/Layout/LayoutEditor.cs`(2362 行,含内嵌 LayoutCard),本文件按 Windows 的
// 组织方式一文件多类型(LayoutEditor + LayoutCard),行为一一对应。
//
// 三条边界(照 Windows 原注释):
//   1) 顺序 / 显隐 / span 的状态与几何全在纯模型层(LayoutPageState + LayoutEngine),本文件只做控件组装与手势;
//   2) 持久化只走 LayoutStoreV2(v2 是唯一真源,v1 由它单向写通);
//   3) 内容来源:spec.Content 优先 → 内容工厂(custom: / plugin: 口子)→ 都没有则显示「区块不可用」占位。
//
// WPF → Avalonia 的每一处非机械换法都就地标了 [Avalonia];逐条映射表见 progress/R32-LED.md。
// 与整合件的全部差异(机械 / 行为)也在该报告的对照表里逐条列出。
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.App.Common;
using TipCL.App.Ui;
using TipCL.Core.Launcher;
using TipCL.Core.Layout;
using TipCL.Core.Launcher.Layout;
// [Avalonia] ToastKind 在 TipCL.App.Common 与 TipCL.Core.Layout 各有一份;Windows 用的是 App 侧那一份,
// Linux 的 LayoutFx.ShowToast 收的是 Core 侧('TipCL.Core.Layout.ToastKind')⇒ 别名固定到 Core。
using ToastKind = TipCL.Core.Layout.ToastKind;

namespace TipCL.App.Pages.Layout;

// 页面区块的渲染 + 编辑宿主(L1 把原来 BlockHost 里的那一套搬进来):
//   常态:单列 = 纵向流(FillPanel + 每块一张零装饰卡),多列 = 行内 Grid(span 跨列);
//   编辑态:每块顶部操作条(拖动提示 + 标题 + 折叠 + 显隐)+ 底部隐藏托盘 + 悬浮工具条(完成 / 重置本页 / 导出 / 导入)。
// [v3] 编辑外壳(顶部单条编辑条 / 「添加」抽屉 / 列表|网格 / 删除与隐藏分开)在 partial 文件 LayoutEditor.V3.cs。
public sealed partial class LayoutEditor
{
	private readonly string _pageId;

	private readonly List<BlockSpec> _specs = new List<BlockSpec>();

	private readonly List<LayoutBlockRef> _refs = new List<LayoutBlockRef>();

	private readonly Dictionary<string, BlockSpec> _byId = new Dictionary<string, BlockSpec>(StringComparer.Ordinal);

	// 每个区块一张卡(含编辑态操作条),常驻缓存,重排/显隐只动它在可视树里的位置
	private readonly Dictionary<string, LayoutCard> _cards = new Dictionary<string, LayoutCard>(StringComparer.Ordinal);

	// 自建块(custom:)卡片建卡时的定义指纹(LayoutEngine.WidgetSignature):重新装载时指纹变了就把卡从 _cards 里丢掉,
	// 下一次 Render 用新定义重建;重建前是折叠的,记在 _refold 里给新卡接着折上
	private readonly Dictionary<string, string> _cardSigs = new Dictionary<string, string>(StringComparer.Ordinal);

	private readonly HashSet<string> _refold = new HashSet<string>(StringComparer.Ordinal);

	// 页面对宿主的显式边距覆盖(构造第 3 参);null = 用布局文档的 pad
	private readonly Thickness? _marginOverride;

	// [layout-fix] 规格 §4-能力2:页面的「出厂默认布局」(构造第 4 参 defaultPage)。
	// 布局文件里还没有该页的记录时用它;「重置本页」也回到它(见 ResetPage)。null = 白名单默认布局(stack)。
	private readonly PageLayoutV2? _defaultPage;

	// [layout-fix] 规格 §3-问题7 + 规格2-问题2.3:stack 页进编辑态会被**临时**折算成 12 列网格。
	//   _preEditPage   = 进编辑前的原样快照,各种模式都留(退出时若一次都没动过,原样还原、不写盘 ——
	//                    临时折算和进编辑时的压紧都只是编辑视图,磁盘上不该留下任何痕迹)
	//   _autoGrid      = 这次编辑态是"临时折算"出来的
	//   _gridTouched   = 这次编辑动过(拖 / 缩放 / 隐藏 / 恢复 / 落盘)⇒ 退出时保留网格
	private PageLayoutV2? _preEditPage;

	private bool _autoGrid;

	private bool _gridTouched;

	// [Avalonia] Windows 的 SPanel(Panel + Orientation + Spacing + FillChild)在 Linux 树不存在;
	// 等价物 = 本包兄弟件 FillPanel.cs(类名/成员与 Windows SPanel 逐字对齐)。
	private readonly FillPanel _list = new FillPanel
	{
		Orientation = Orientation.Vertical,
		Spacing = LayoutSchema.GapDefault
	};

	private LayoutPageState _state = null!;   // 构造里 LoadFromStore() 先赋值,Render() 才读;nullable 上按"由构造建立"声明

	private readonly Border _trayHost = new Border();

	private readonly WrapPanel _tray = new WrapPanel();

	private readonly Border _barHost = new Border();

	private readonly Grid _root;

	// [layout-fix 第三批] 底部托盘 + 工具条的外壳。它常驻时(子元素都隐藏)仍占 8 + 下边距的一条空带(约 24px):
	// 既有区块页的默认外观就是带着这条空带对齐过的(启动页的英雄区按剩余高度居中,去掉会整体下移),所以照旧;
	// 「吃满视口」的页(AI 页输入区要贴底,对齐拆块前的 Grid)自动在非编辑态把它收起;其它新拆块的页
	// (版本安装 / 版本管理 / 设置 → 布局)经 CollapseIdleBottom 显式要求收起,滚动视口才与拆块前同一条下沿。
	private readonly StackPanel _bottom;

	private bool _collapseIdleBottom;

	private bool _edit;

	private LayoutCard? _dragCard;

	// 拖拽手势状态:手柄捕获指针期间按缓存高度计算相邻块中点
	private readonly Dictionary<string, double> _dragHeights = new Dictionary<string, double>(StringComparer.Ordinal);

	// 按下手柄时的页面与块顺序:松手时顺序没变(只是点了一下)就什么都不做,变了才压撤销、落盘
	private PageLayoutV2? _dragBefore;

	private readonly List<string> _dragOrder = new List<string>();

	// ————— L2:grid/canvas 画布 + 编辑体验(多选 / 撤销重做 / 辅助线 / 框选 / 键盘) —————

	// 编辑会话:多选集合 + 撤销/重做(内存快照,落盘仍即时以保崩溃安全)+ 剪贴板 + 键盘意图;
	// 纯模型(零 UI),见 LayoutEngine.LayoutEditSession —— 交互逻辑可在 net8 测试宿主里断言。
	private readonly LayoutEditSession _session = new LayoutEditSession();

	// 画布 Panel(grid/canvas 用自绘 Panel)+ 对齐辅助线/框选覆盖层
	private readonly GridCanvasPanel _box = new GridCanvasPanel();

	private readonly GuideLayer _guide = new GuideLayer();

	// [R35 lin-l12] §5.1 拖动时的淡色列底纹(画在画布之下,只在拖动 / 改宽度期间显示)
	private readonly EditColumnShade _shade = new EditColumnShade();

	private readonly Grid _stage = U.G();

	// 两个可视宿主:stack 用 FillPanel 纵向流,grid/canvas 用画布 Panel;按模式切换,不并存
	private readonly ScrollViewer _scroll;

	private readonly ScrollViewer _boxScroll;

	// 全局密度(§4.B;缺省 standard → 间距档 10 = 现状 BlockHost.Gap)
	private string _density = LayoutSchema.DensityStandard;

	// 宿主喂进来的"内容区可用宽"(>0 时覆盖 LayoutVariantHost.Width;0 = 用全局提示,再没有就不做变体判定)
	private double _widthHint;

	// 本页这次读的是哪一份(LayoutVariants.SourceOf):窄窗下显示的是变体就写回变体,不覆盖宽版;空串 = 主 body
	private string _variant = "";

	// 自己正在提交(内部守卫):本次保存触发的 LayoutChanged 不要再让自己重排一遍
	private bool _saving;

	// canvas 吸附档(px;编辑态可切 {0,4,8,16,24})
	private double _snapStep = LayoutCanvas.DefaultStep;

	// 一次画布手势:起手时的槽位快照 + 指针起点(用绝对值算位移,避免逐帧累积误差)
	private string _gesture = "";

	private Point _gestureStart;

	private string _gestureHandle = "";

	// 起手时的页面:指针越过拖动距离才压进撤销栈。单击选中 / 点一下手柄不算改动 ——
	// 不留空撤销记录(PushUndo 会清空重做栈)、不落盘、不标记 _gridTouched。
	private PageLayoutV2? _gestureBefore;

	private bool _gestureMoved;

	private readonly Dictionary<string, LayoutCell> _gestureCells = new Dictionary<string, LayoutCell>(StringComparer.Ordinal);

	// [layout-fix] 规格2-问题2.2:手势开始时的**全部**槽位快照 + 行高快照。
	// 每次指针移动都从这份快照重新算(而不是在上一次的结果上继续叠),否则反复经过同一格会越拖越乱;
	// 行高快照用来把"指针落在哪一行"折算成行号 —— 过去用"位移 ÷ 被拖卡片自身高度",
	// 于是高卡片几乎拖不动、矮卡片一拖跳好几行。
	private readonly Dictionary<string, LayoutCell> _gestureAll = new Dictionary<string, LayoutCell>(StringComparer.Ordinal);

	private double[] _gestureRows = Array.Empty<double>();

	// 被拖块**原来的**列(左右互换的候选落点;见 LayoutGrid.Compact 的 swapX)
	private LayoutRect _gestureAnchor;

	// [layout-fix 第三批·任务 B] 丝滑拖动的手势状态:
	//   _gestureRects   = 起手时每个手势块的像素矩形(浮起的起点 / 改大小的基准)
	//   _gestureTarget  = 上一次算出的落点 / 吸附尺寸的指纹 —— 只有它变了才重新让位 + 排布(其余帧只改平移)
	//   _snapHad/Dx/Dy  = canvas 上一帧的磁性吸附状态(吸住 / 松开那一下用短动画过渡)
	private readonly Dictionary<string, LayoutRect> _gestureRects = new Dictionary<string, LayoutRect>(StringComparer.Ordinal);

	private string _gestureTarget = "";

	private bool _snapHad;

	private double _snapDx;

	private double _snapDy;

	// 工具条上的撤销/重做按钮(按栈的可用性置灰)
	private Button? _undoBtn;

	private Button? _redoBtn;

	// [layout-fix] 参考件v2-对齐:全部折叠 / 全部展开(仅编辑态)。[v3] 编辑条上不再放这个按钮(恒为 null),折叠逻辑保留
	private Button? _foldAllBtn;

	// [layout-fix] 参考件v2-对齐:拖动 / 缩放时贴近可视区上下边缘的自动滚动
	private DispatcherTimer? _autoScroll;

	private Point _lastPointer;

	// [Avalonia] Windows 用 Mouse.GetPosition(_boxScroll) + Mouse.LeftButton(没有全局指针单例),
	// 这里改成手势期间缓存"最后一次指针位置(相对 _boxScroll)"+"是否还按着左键"。
	private Point _lastPointerInScroll;

	private bool _pointerDown;

	// [Avalonia] Windows 用 _box.IsMouseCaptured / _box.ReleaseMouseCapture;Avalonia 走 IPointer.Capture,
	// 所以自己留着被捕获的指针句柄,收尾时 Capture(null) 释放。
	private IPointer? _boxPointer;

	// [Avalonia] Windows 用 SystemParameters.Minimum{Horizontal,Vertical}DragDistance(无对应),
	// 按契约书 §2.0 的建议改成自定义常量 4.0(同一份语义:指针位移没超过它 = 只是点了一下)。
	private const double DragDistance = 4.0;

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
		foreach (BlockSpec spec in _specs)
		{
			if (LayoutFill.GetFillsViewport(spec.Content))
			{
				_collapseIdleBottom = true;
			}
		}
		// [v3] §1.A.5:底部不再有一整条「已隐藏的区块」托盘 —— 已隐藏的内容并进「添加」抽屉(LayoutEditor.V3.cs)。
		// 托盘外壳保留但永不显示:非编辑态底部那条约 24px 空带(见 _bottom 注释)与改动前逐像素一致。
		_trayHost.IsVisible = false;
		_trayHost.Child = _tray;
		// [v3] §1.A.3:底部 7 个文字按钮 → 顶部一条编辑条(标题 · 列表|网格 · 撤销/重做图标 · 添加 · 重置本页 · 完成 · ⋯)。
		// 「吸附 N」去掉(网格按整数列对齐);「全部折叠」去掉;「自动靠拢」开关搬进 ⋯ 菜单。
		_barHost.IsVisible = false;
		// [R35] 底色读 L0 的 B.EditBarBg(兜底 B.Paper);L0 的 Layout.EditBar ControlTheme 存在就套上。
		// [R35 lin-int] 套上主题后不再写圆角 / 内距 / 高度 / 底色 / 边框 / 阴影的本地值(否则本地值盖过主题,
		// 明暗切换时 Fx.EditBar / B.EditBarLine 也跟不上);深色表面仍用白底编辑条(pages3:只有虚线框与底纹换白色)。
		if (!LayoutVisualTokens.ApplyTheme(_barHost, LayoutVisualTokens.ThemeKeys.EditBar))
		{
			_barHost.CornerRadius = new CornerRadius(12.0);
			_barHost.Padding = new Thickness(14.0, 0.0, 8.0, 0.0);
			_barHost.MinHeight = 46.0;
			_barHost.Dyn(Border.BackgroundProperty, LayoutEditChrome.KeyOr("B.EditBarBg", "B.Paper"));
			_barHost.Dyn(Border.BorderBrushProperty, "B.Line");
			_barHost.BorderThickness = new Thickness(1.0);
			// [Avalonia] Windows 的 Motion.Shadow(_barHost, 16, 0.07, 3) 在 Linux 树没有等价门面 ⇒ 原生 Border.BoxShadow
			// (v3:贴近效果图的 0 1 3 rgba(0,0,0,.06))。这是视觉近似,不进任何回归判定。
			_barHost.BoxShadow = new BoxShadows(new BoxShadow
			{
				Blur = 3.0,
				OffsetY = 1.0,
				Color = Color.FromArgb(16, 0, 0, 0)
			});
		}
		_barHost.Child = BuildEditBar();
		_barHost.Margin = new Thickness(_list.Margin.Left, LayoutVisualTokens.EditBarGap, _list.Margin.Right, 0.0);
		// 根结构:上编辑条(仅编辑态),中滚动内容区,下空带(托盘外壳);「添加」抽屉叠在内容区右侧
		StackPanel bottom = U.V(8.0, _trayHost);
		bottom.Margin = new Thickness(_list.Margin.Left, 8.0, _list.Margin.Right, _list.Margin.Bottom);
		_bottom = bottom;
		_bottom.IsVisible = !_collapseIdleBottom;
		_root = U.G("Auto,*,Auto");
		// [Avalonia] Windows 的 SmoothScroll 在 Linux 树没有等价物 ⇒ 用原生 ScrollViewer(像素滚动,无平滑动画)。
		_scroll = new ScrollViewer
		{
			// 透明底:区块之间的空隙也能接到滚轮(null 底时滚轮穿透到页面背景,看起来「滑不动」)
			Background = Brushes.Transparent,
			Content = _list,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			IsScrollInertiaEnabled = true
		};
		// 注意:_root.Children[0] 必须一直是当前可视宿主(_scroll / _boxScroll),ShowStackHost / ShowBoxHost 靠下标 0 换它
		_root.Add(_scroll, 1);
		_root.Add(bottom, 2);
		_root.Add(_barHost, 0);
		Control drawer = BuildDrawer();
		_root.Add(drawer, 1);
		Grid.SetRowSpan(drawer, 2);
		// grid/canvas 模式的可视宿主:自绘画布 Panel + 其上的对齐辅助线/框选覆盖层(同一格叠放)。
		// 注意:stack(缺省)时这个宿主根本不进可视树 —— 默认渲染与改动前逐像素一致由这一点保证。
		_stage.Children.Add(_shade);
		_stage.Children.Add(_box);
		_stage.Children.Add(_guide);
		_boxScroll = new ScrollViewer
		{
			// 同上:画布空隙里的滚轮别穿透到页面背景
			Background = Brushes.Transparent,
			Content = _stage,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			IsScrollInertiaEnabled = true
		};
		// 键盘(§4.G):整个编辑器根元素接管按键 —— Esc / Delete / 方向键 / Ctrl+Z/Y/C/V
		_root.Focusable = true;
		// [Avalonia] WPF 的 PreviewKeyDown → AddHandler(Tunnel)(Avalonia 没有 PreviewXxx 属性写法)
		_root.AddHandler(InputElement.KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
		// [Avalonia] WPF 的 MouseLeftButtonDown → PointerPressed(同为冒泡)
		_box.PointerPressed += OnBoxMouseDown;
		LoadFromStore();
		Render();
		// dev-only:让取证钩子能拍到编辑态(右键菜单是独立窗口,截图工具点不到;仓库既有 dev 钩子同风格)。
		// 只有环境变量恰好等于 1 才生效,正常启动零影响。
		if (Environment.GetEnvironmentVariable("TIPCL_DEV_LAYOUT_EDIT") == "1" && CanEdit)
		{
			EnterEdit();
		}
	}

	/// <summary>根可视元素(BlockHost 直接把它作为 Content)。</summary>
	public Control Root
	{
		get
		{
			return _root;
		}
	}

	/// <summary>布局发生变更(保存后)时触发;BlockHost 转发成自己的 Changed。</summary>
	public event Action? Changed;

	/// <summary>
	/// [layout-fix 第三批] 非编辑态是否收起底部托盘 / 工具条外壳(收起 = 不占那条约 24px 的空带)。
	/// 缺省 false(既有区块页照旧);页面里有「吃满视口」块时自动为 true。
	/// </summary>
	public bool CollapseIdleBottom
	{
		get
		{
			return _collapseIdleBottom;
		}
		set
		{
			_collapseIdleBottom = value;
			if (!_edit)
			{
				_bottom.IsVisible = !value;
			}
		}
	}

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

	/// <summary>
	/// 是否提供编辑入口(右键「自定义布局...」/ 设置 → 布局的「编辑」/ LayoutShellHooks.CanEditPage)。
	/// [v3] §1.A.8:单块页面(入口清单里标了单块的「下载与网络」「反馈与关于」,以及任何只有一块的页)不提供。
	/// 注意 EnterEdit() 本身仍只要求 ≥1 个块(测试宿主 / dev 钩子可以直接进),入口的把关只在这里。
	/// </summary>
	public bool CanEdit
	{
		get
		{
			return _specs.Count >= 1 && !IsSingleBlockPage();
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

	/// <summary>[layout-fix] 这次编辑态是不是"stack 临时折算成 12 列网格"出来的(测试/调试用)。</summary>
	internal bool IsAutoGrid
	{
		get
		{
			return _autoGrid;
		}
	}

	/// <summary>重新从真源装载本页并重排(D1):设置页改完布局文档、或整机方案被应用之后,
	/// 已存在的编辑器实例靠它立刻刷新(不重建页面,页面自己的状态不丢)。</summary>
	public void ReloadFromStore()
	{
		LoadFromStore();
		// [layout-fix] 规格 §3-问题7:编辑态里文档被换掉(导入 / 别处保存 / 窗口宽窄换了变体)之后,
		// 还没动过就按新内容重取编辑前快照、重折临时网格 —— 退出时还原的才是新内容;
		// 已经动过(_gridTouched)就不动用户改出来的网格。
		if (_edit && !_gridTouched)
		{
			PrepareFreeDrag();
			_gridWidths.Clear();
			_listKind = IsListShape();
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
		// [v3] §1.A.7:进编辑时按现状判定排法 —— 每块都占满一整行(stack 单列页折出来的就是这样)= 列表,否则 = 网格
		_gridWidths.Clear();
		_listKind = IsListShape();
		Render();
		_barHost.IsVisible = true;
		_bottom.IsVisible = true;
		ShowDrawer(false);
		RefreshEditBar();
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
		_preEditPage = ((_state?.Page != null) ? _state.ToPage() : null);
		if (_specs.Count == 0 || _state?.Page == null || _state.Page.Mode != LayoutSchema.ModeStack)
		{
			return;
		}
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
		PageLayoutV2? untouched = (_gridTouched ? null : _preEditPage);
		if (untouched != null)
		{
			// [layout-fix] 规格 §5「进入编辑后不动任何东西直接「完成编辑」,页面应该和之前完全一样」:
			// 临时折算成网格、进编辑时的压紧都只是编辑视图,一个改动都没发生 → 放回原样,不写盘。
			_state = LayoutEngine.LoadState(_pageId, untouched, _refs, _state.ExistedInDoc);
		}
		_autoGrid = false;
		_gridTouched = false;
		_preEditPage = null;
		if (untouched == null)
		{
			Persist();
		}
		Render();
		_session.ClearSelection();
		_session.ClearHistory();
		_trayHost.IsVisible = false;
		_barHost.IsVisible = false;
		ShowDrawer(false);
		_bottom.IsVisible = !_collapseIdleBottom;
		if (untouched == null)
		{
			LayoutFx.ShowToast(I18nCore.Tr("布局已保存"), "", ToastKind.Success);
		}
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
		DropStaleWidgetCards(doc.Layout);
		// 窄窗/宽窗变体(D1):可用宽低于阈值且 variants.narrow 里有这一页 → 用变体那一份,
		// 否则回落主 body(PageOf 内部就是这个回落规则)。宽度未知(≤0)时恒等于主 body,
		// 所以"没接线 / 首帧"的渲染与改动前完全一致。
		double width = ((_widthHint > 0.0) ? _widthHint : LayoutVariantHost.Width);
		PageLayoutV2? page = LayoutVariants.PageOf(doc.Layout, _pageId, width, LayoutVariantHost.Threshold);
		string variant = LayoutVariants.SourceOf(doc.Layout, _pageId, width, LayoutVariantHost.Threshold);
		if (_edit && !string.Equals(variant, _variant, StringComparison.Ordinal))
		{
			// 编辑中途窗口宽窄换了来源:撤销栈里是另一份的快照,留着会在撤销时把它写进这一份;
			// 那一份的改动已经落盘,这一份从"没动过"重新算起(ReloadFromStore 会重取编辑前快照)
			_session.ClearHistory();
			_gridTouched = false;
		}
		_variant = variant;
		// 全局密度也来自 v2(缺省 standard → 间距档 10 = 现状)
		_density = ((doc.Layout?.Shell != null && LayoutSchema.IsKnownDensity(doc.Layout.Shell.Density)) ? doc.Layout.Shell.Density : LayoutSchema.DensityStandard);
		// [layout-fix] 规格 §4-能力2:真源里没有这一页时用该页的 defaultPage 兜底(实例库的默认左右排布就靠它)。
		// 注意 ExistedInDoc 要如实传 false —— 兜底布局只是"读到空气时的默认值",没有被写进任何文件。
		bool existed = page != null;
		PageLayoutV2? effective = (page ?? _defaultPage);
		PageLayoutV2? working = ((effective != null) ? effective.Clone() : null);
		// [v3] §1.A.7:不再提供自由画布。老存档里 mode = canvas 的页读取时按网格呈现(归一化坐标换算到 12 列整数列),
		// 只换内存里这一份 —— 磁盘上的 canvas 字段原样保留(Core 的 schema / 模型 / v1 兼容一个字段都不删),
		// 在编辑态真的动过之后才按 grid 落盘。
		if (working != null && working.Mode == LayoutSchema.ModeCanvas)
		{
			// R35 lin-int(L12←L3):推导时就认块自己的 BlockSpec.MinCols(换算到本页列数),不再只按全局 2 列兜底
			int deriveCols = LayoutSchema.SnapCols(working.Cols);
			LayoutEngine.CanvasToGrid(working, id => Math.Max(2, MinColsOf(id, deriveCols)));
		}
		_state = LayoutEngine.LoadState(_pageId, working, _refs, existed);
		RememberBlockCount();
	}

	// 自建块内容只在建卡时生成一次:定义改了(便签文字、时钟格式、快捷方式目标、网址、enabled...)就把那张卡从缓存里丢掉,
	// 下一次 Render 按新定义重建;没变的卡原样复用。老卡不在这里摘:重渲染清容器时自然离开可视树,
	// 带定时器的内容靠自己的 Unloaded 停掉或释放
	private void DropStaleWidgetCards(LayoutBody? body)
	{
		List<string>? stale = null;
		foreach (KeyValuePair<string, string> pair in _cardSigs)
		{
			if (!string.Equals(LayoutEngine.WidgetSignature(body, pair.Key) ?? "", pair.Value, StringComparison.Ordinal))
			{
				(stale ??= new List<string>()).Add(pair.Key);
			}
		}
		if (stale == null)
		{
			return;
		}
		foreach (string id in stale)
		{
			_cardSigs.Remove(id);
			if (_cards.TryGetValue(id, out LayoutCard? card))
			{
				_cards.Remove(id);
				if (card.IsFolded)
				{
					_refold.Add(id);
				}
			}
		}
	}

	// 当前是否 grid / canvas 模式(stack = 缺省,走纵向流)
	private bool IsBoxMode
	{
		get
		{
			return _state.Page.Mode == LayoutSchema.ModeGrid || _state.Page.Mode == LayoutSchema.ModeCanvas;
		}
	}

	// 单列走纵向流(每块一张卡直接挂 FillPanel);多列才用行内 Grid
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
			LayoutCard? lastCard = null;
			string lastId = "";
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
					_list.Children.Add(Detach(card));
					lastCard = card;
					lastId = row.Slots[0].Id;
				}
			}
			// [layout-fix] V-1:页面主体块(挂了 LayoutFill.FillsBody 的那一块)在非编辑态吃掉内容区剩余高度 ——
			// 修复"插件页空状态占位不再垂直居中"(改版前主体在页面根 Grid 的 * 行里,天然占满剩下的高度)。
			// 只认"它正好是最后一块"这一种情况;没挂标记的页面 FillChild = null ⇒ 渲染与改动前一模一样。
			_list.FillChild = ((_edit || lastCard == null) ? null : MarkedFill(lastId, lastCard));
			// [layout-fix 第三批]「吃满视口」块(AI 对话区):关掉外层纵向滚动,让纵向流拿到有限高度,
			// 该块按 Auto,*,Auto 的 * 行拿剩余高度 —— 它自己的滚动条与吸底才保得住。没有这种块的页一切照旧。
			string? viewportFill = ViewportFillId(plan.VisibleIds);
			if (viewportFill != null && _cards.TryGetValue(viewportFill, out LayoutCard? fillCard))
			{
				_list.FillChild = fillCard;
				_list.BoundFill = true;
			}
			else
			{
				_list.BoundFill = false;
			}
			_scroll.VerticalScrollBarVisibility = (_list.BoundFill ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
		}
		else
		{
			// 多列行不参与:行内 Grid 的高度由该行最高的一块决定,旧版也没有对应的"吃剩余高度"结构
			_list.FillChild = null;
			_list.BoundFill = false;
			_scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
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
		// R35 lin-int(L12←L3):canvas 页由面板按网格推导;推导时也认块自己的 MinCols
		_box.MinColsOf = id => Math.Max(2, MinColsOf(id, LayoutSchema.SnapCols(page.Cols)));
		_box.SetLayout(page.Mode, LayoutGrid.Columns(page), page.RowH, gap, page);
		SanitizeCellWidths();
		_box.SnapStep = _snapStep;
		_box.EditMode = _edit;
		// R35 lin-int(L3):启动页深色表面上落点框 / 网格线用白色半透明
		_box.DarkSurface = IsDarkSurface;
		// [layout-fix 第三批] grid 自动行高下的「吃满视口」块:外层纵向滚动关掉,画布拿到有限高度,该块所在行吃剩余高度
		// R35 lin-int:模式 / 行高一律读面板的实际值(canvas 页在面板里已是 grid、行高 0),canvas 页在编辑器里也按网格处理
		bool boxGrid = string.Equals(_box.Mode, LayoutSchema.ModeGrid, StringComparison.Ordinal);
		string? boxFill = ((boxGrid && _box.RowH <= 0.0) ? ViewportFillId(_state.VisibleIds()) : null);
		_box.FillId = boxFill;
		_boxScroll.VerticalScrollBarVisibility = ((boxFill != null) ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
		Thickness boxMargin = (_marginOverride ?? PadOf(_state.Plan()));
		if (_edit)
		{
			// [v3] 编辑态块名浮在卡片左上角上方 14px(效果图 .lab top:-14px):第一行上面留出这点地方,免得被滚动区裁掉
			boxMargin = new Thickness(boxMargin.Left, boxMargin.Top + 14.0, boxMargin.Right, boxMargin.Bottom);
		}
		_box.Margin = boxMargin;
		_box.Children.Clear();
		foreach (LayoutCell cell in LayoutCanvas.StackedOrder(_box.Cells))
		{
			LayoutCard card = CardFor(cell.Id);
			card.SetBoxMode(_box.Mode);
			card.SetEdit(_edit);
			// [v3] 列表排法下宽度固定为整行:不给右边缘改宽度
			card.SetWidthLocked(_edit && _listKind);
			// [layout-fix] 参考件v2-对齐:只有"grid + 自动行高 + 有固定像素高"的块才进块内滚动
			card.SetFixedHeight(boxGrid && _box.RowH <= 0.0 && cell.HeightPx > 0.0);
			card.SetSelected(_edit && _session.IsSelected(cell.Id));
			ApplyLook(card);
			_box.Children.Add(Detach(card));
		}
		_box.Reflow();
	}

	// 渲染前的宽度兜底(只改画布里的格子,不写回存档):
	// 旧存档 / 外部导入的布局里可能有被缩成 1 列的块 —— 常态下没人调 ClampMinWidth,
	// 内容(插件卡片网格)被 FitBox 缩到看不见。这里按「全局 2 列 + 块自己的 MinCols」撑回来,冲突交给压紧解决。
	// 对齐 Windows 2026-11-19 修复 #7(录屏 1:06–1:16「内容区块被压成一条细线、插件全看不见」)。
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
			_box.CompactCells(null, null, gravity: false);
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

	// 可视宿主二选一:stack = FillPanel 纵向流,grid/canvas = 画布 Panel
	private void ShowStackHost()
	{
		if (_root.Children.Count != 0 && ReferenceEquals(_root.Children[0], _scroll))
		{
			return;
		}
		_root.Children.RemoveAt(0);
		// [v3] 第 0 行现在是顶部编辑条,内容区在第 1 行
		Grid.SetRow(_scroll, 1);
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
		Grid.SetRow(_boxScroll, 1);
		_root.Children.Insert(0, _boxScroll);
		_list.Children.Clear();
		// [layout-fix] V-1:纵向流退场,填充块(见 Render)也一并失效,别留一个已脱离可视树的引用
		_list.FillChild = null;
	}

	// [layout-fix] V-1:这一块是不是"页面主体块"(页面对它的内容元素挂了 LayoutFill.FillsBody)。
	// 只有它才吃内容区的剩余高度;其余页面 / 其余块一律按内容高度排布(逐像素等价改动前)。
	private Control? MarkedFill(string blockId, Control card)
	{
		if (_byId.TryGetValue(blockId, out BlockSpec? spec) && spec.Content != null && LayoutFill.GetFillsBody(spec.Content))
		{
			return card;
		}
		return null;
	}

	// [layout-fix 第三批] 可见块里第一个「吃满视口」的块(内容挂了 LayoutFill.FillsViewport);没有返回 null
	private string? ViewportFillId(IEnumerable<string> visibleIds)
	{
		foreach (string id in visibleIds)
		{
			if (_byId.TryGetValue(id, out BlockSpec? spec) && LayoutFill.GetFillsViewport(spec.Content))
			{
				return id;
			}
		}
		return null;
	}

	// 工具条上的撤销/重做可用性 + [v3] 列表|网格分段 / 提示 / 「添加」上的隐藏数
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
		RefreshEditBar();
	}

	// [R35 lin-l12] GOAL §5.2:「自动靠拢」界面整体删除(C 曾挪进 ⋯ 菜单,本包去掉),编辑器固定按「开」处理;
	// LayoutEditPrefs.AutoPack 本身仍可读写(归 L4),界面上不再提供修改入口。
	private static readonly bool AutoPackOn = true;

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
	private Control BuildRow(LayoutRow row, LayoutPlan plan)
	{
		int columns = Math.Max(1, plan.Columns);
		Grid grid = U.G();
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
			Detach(card).Margin = new Thickness(0.0, 0.0, ((k == row.Slots.Count - 1) ? 0.0 : plan.Gap), 0.0);
			grid.Children.Add(card);
		}
		return grid;
	}

	/// <summary>
	/// 让当前内容区按滚轮增量滚动(能滚才滚,返回是否滚了)。
	/// 给页面用:鼠标在区块宿主之外(启动页英雄区 / 背景图)时,滚轮也能滚动自定义布局区
	/// —— 对齐 Windows 2026-11-19 修复 #2(录屏 2:30「非自定义布局模式下无法滑动」)。
	/// [Avalonia] Windows 的 SmoothScroll 在本树没有等价物 ⇒ 直接改原生 ScrollViewer 的 Offset。
	/// </summary>
	public bool ScrollBy(int delta)
	{
		ScrollViewer host = (IsBoxMode ? _boxScroll : _scroll);
		if (!host.IsVisible || host.ScrollBarMaximum.Y <= 0.5)
		{
			return false;
		}
		double offset = host.Offset.Y;
		bool canMove = (delta < 0) ? (offset < host.ScrollBarMaximum.Y - 0.5) : (offset > 0.5);
		if (!canMove)
		{
			return false;
		}
		double next = offset - delta;
		host.Offset = new Vector(host.Offset.X, Math.Max(0.0, Math.Min(host.ScrollBarMaximum.Y, next)));
		return true;
	}

	// [Avalonia] WPF 用 card.Parent + VisualTreeHelper.GetParent;Avalonia 取逻辑父(Parent)与可视父
	// (Avalonia.VisualTree.VisualExtensions.GetVisualParent),两者都可能是 Panel 容器。
	private static LayoutCard Detach(LayoutCard card)
	{
		Panel? panel = ((card.Parent as Panel) ?? (card.GetVisualParent() as Panel));
		panel?.Children.Remove(card);
		card.Margin = default(Thickness);
		return card;
	}

	private Thickness PadOf(LayoutPlan? plan)
	{
		double[] pad = plan?.Pad ?? LayoutSchema.PadDefaultCopy();
		return new Thickness(pad[0], pad[1], pad[2], pad[3]);
	}

	// 卡片常驻:内容在这里解析(spec.Content → 内容工厂 → 「区块不可用」占位);
	// 自建块顺带记下定义指纹,定义变了由 DropStaleWidgetCards 丢缓存重建
	private LayoutCard CardFor(string id)
	{
		if (_cards.TryGetValue(id, out LayoutCard? card))
		{
			return card;
		}
		BlockSpec spec = SpecOf(id);
		if (LayoutEngine.WidgetIdOf(id) != null)
		{
			_cardSigs[id] = LayoutEngine.WidgetSignature(LayoutStoreV2.Load().Layout, id) ?? "";
		}
		card = new LayoutCard(this, spec, ResolveContent(spec));
		_cards[id] = card;
		if (_refold.Remove(id) && _edit)
		{
			card.SetEdit(true);
			card.SetFolded(true);
		}
		return card;
	}

	// 页面 spec 优先;布局文档里的 custom: / plugin: 块可能没有对应 spec(§4.F),兜一个标题。
	// [R35 lin-l12] GOAL §5.1「永远不显示 id」:兜底标题走 LayoutBlockTitles.Resolve —— 自建块取 widgets[] 的 title →
	// 类型名;插件块取插件给的标题(界面贡献 label)→「插件区块」;其它 →「区块」。原来的 Title = id /「plugin · inner」不再出现。
	private BlockSpec SpecOf(string id)
	{
		if (_byId.TryGetValue(id, out BlockSpec? spec))
		{
			return spec;
		}
		return new BlockSpec
		{
			Id = id,
			Title = SyntheticTitle(id)
		};
	}

	/// <summary>[R35] 没有页面 spec 的块的显示标题(纯规则见 LayoutBlockTitles.Resolve)。</summary>
	internal static string SyntheticTitle(string id)
	{
		return LayoutBlockTitles.Resolve(id, null, WidgetRegistry.FindWidget, PluginBlocks.TitleOf);
	}

	// 内容解析:内置块保持旧行为(没给内容就是没内容);custom:/plugin: 查注册表,查不到给「不可用」占位
	private Control? ResolveContent(BlockSpec spec)
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
		if (built is Control element)
		{
			return element;
		}
		return Unavailable(spec);
	}

	// §4.F:插件卸载 / 自建块类型缺失时数据保留、只显示「不可用」
	private static Control Unavailable(BlockSpec spec)
	{
		StackPanel panel = U.V(4.0, U.Muted(I18nCore.Tr("区块不可用")), U.Muted(spec.Title ?? "").Wrap());
		panel.Margin = new Thickness(10.0);
		return panel;
	}

	// ————— 编辑操作 —————

	// [v3] §1.A.5:原来的底部托盘并进「添加」抽屉 —— 这里只刷新抽屉内容与编辑条上的数字(托盘外壳永不显示)
	private void RefreshTray()
	{
		_trayHost.IsVisible = false;
		RefreshDrawer(IsDrawerOpen);
		RefreshEditBar();
	}

	internal void HideBlock(BlockSpec spec)
	{
		if (!_edit || spec == null || _state.Hidden.Contains(spec.Id))
		{
			return;
		}
		_session.PushUndo(_state.ToPage());
		_state.Hide(spec.Id);
		// [v3] 隐藏后其余块补上空位(效果图:隐藏即重排);隐藏块自己的槽位留在文档里,找回时 Pack 再给它让位
		CompactState();
		_session.Retain(_state.VisibleIds());
		Render();
		Persist();
		// [v3] §1.A.6:内置内容用眼睛隐藏,随时能在「添加」里找回
		LayoutFx.ShowToast(string.Format(I18nCore.Tr("已隐藏「{0}」"), spec.Title), I18nCore.Tr("在「添加」里可以找回"), ToastKind.Info);
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
		// 抽屉开着就跟着刷新(Render 已刷过一遍;这里再补一句提示)
		// [R35 lin-l12] §5.2:抽屉里点「显示」之后滚到这块并闪一下主题色光晕(效果图 flash(id))
		FlashBlock(spec.Id);
		LayoutFx.ShowToast(string.Format(I18nCore.Tr("已恢复「{0}」"), spec.Title), "", ToastKind.Success);
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
			LayoutFx.ShowToast(I18nCore.Tr("重置失败"), ex.Message, ToastKind.Error);
		}
	}

	private void ResetPageCore()
	{
		// 手势进行中先收干净(否则松手时会把旧坐标写回去)
		if (_gesture.Length != 0)
		{
			EndBoxGesture(false, false);
		}
		_boxPointer?.Capture(null);
		_boxPointer = null;
		PageLayoutV2 before = _state.ToPage();
		_session.PushUndo(before);
		// [layout-fix 第三批] 出厂页 = 宿主默认布局(没有就是白名单 stack)+ 当前页的自建 / 插件块(只清几何、数据保留)。
		// 以前这里是 LoadState(pageId, defaultPage 或 null):出厂几何确实回来了,但 custom: / plugin: 块
		// 不在白名单里也不在出厂页里 ⇒ 被整条丢掉,接着 Persist 把这一页整页覆盖 —— 自建块从页面上消失、
		// 它在本页的外观 / 设置一并丢失。纯规则见 LayoutEngine.FactoryPage(有单测)。
		_state = LayoutEngine.LoadState(_pageId, LayoutEngine.FactoryPage(before, _defaultPage, _refs), _refs, true);
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
			_gridWidths.Clear();
			_listKind = IsListShape();
		}
		RefreshFoldAllBtn();
		Render();
		LayoutFx.ShowToast(I18nCore.Tr("布局已重置"), I18nCore.Tr("本页已恢复默认布局"), ToastKind.Success);
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

	// 导入:读文件、合并、落盘都在 PageLayoutIO(v2 文件按页整条覆盖,旧版 v1 文件只并入顺序与显隐)。
	// 导入算一次改动,能撤销;编辑态下按导入后的页面重新取进编辑快照(同 ResetPageCore),
	// 退出时就不会把导入前的样子还原回去。
	private void ImportLayout()
	{
		PageLayoutV2 before = _state.ToPage();
		if (PageLayoutIO.ImportFromFileDialog() == null)
		{
			return;
		}
		_session.PushUndo(before);
		LoadFromStore();
		if (_edit)
		{
			PrepareFreeDrag();
			_gridWidths.Clear();
			_listKind = IsListShape();
		}
		Render();
		this.Changed?.Invoke();
	}

	// ————— 持久化 —————

	// 任何变更 → 取整份 v2 文档 → 覆盖本页(显示的是窄窗变体就覆盖变体里的)→ Save(v2 + 写通 v1)→ 清 v1 缓存 → Changed
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
			LayoutEngine.ApplyPage(doc, _pageId, _state.ToPage(), _variant);
			LayoutStoreV2.Save(doc);
			// R32-LIN-IO:恢复 Windows :1038 的这一行(PARITY D-02 / D-21-1)。
			// 旧注释断言「Linux 的 PageLayoutStore 没有 Reload()」已被证伪 —— 它就在
			// Core/Layout/PageLayoutStore.cs:100。Save 只把 v2 单向写通 v1 **文件**,不动 v1 的
			// 进程内快照(V1Bridge.RefreshV1Cache() 只在明确时点被调用),所以仍走 v1 读取的调用方
			// 会拿到过期缓存;这里显式清一次,与 Windows 语义一致。
			PageLayoutStore.Reload();
		}
		finally
		{
			_saving = false;
		}
		this.Changed?.Invoke();
	}

	// 操作条上的拖拽手柄:stack 模式 = 一维换序(旧行为),grid/canvas = 画布整体拖拽
	internal void BeginHandleDrag(LayoutCard card, InputElement src, PointerPressedEventArgs e)
	{
		if (IsBoxMode)
		{
			BeginBoxDrag(card, e);
			return;
		}
		BeginDrag(card, src, e);
	}

	// ————— 拖拽重排(编辑态) —————

	// 手柄按下:缓存各可视块高度,捕获指针,之后 PointerMoved 按相邻块中点实时交换。
	// 不做动画插值,交换即落位——Motion.Enabled 关掉也完全一致。
	internal void BeginDrag(LayoutCard card, InputElement src, PointerPressedEventArgs e)
	{
		if (!_edit || _dragCard != null || card == null)
		{
			return;
		}
		_dragCard = card;
		_dragBefore = _state.ToPage();
		_dragOrder.Clear();
		_dragOrder.AddRange(_state.Order);
		_dragHeights.Clear();
		foreach (Control child in _list.Children)
		{
			if (child is LayoutCard element && element.Bounds.Height > 1.0)
			{
				_dragHeights[element.Spec.Id] = element.Bounds.Height;
			}
		}
		card.Opacity = 0.75;
		// [audit/B] stack 丝滑:起手先浮起(按像素跟手;关了动画或非单列时是空操作)
		BeginStackFloat(card, e);
		// [Avalonia] CaptureMouse/LostMouseCapture → e.Pointer.Capture(src) + PointerCaptureLost
		// (仓库既有先例:Pages/Layout/BlockHost.cs:333-392)
		src.PointerMoved += OnDragMove;
		src.PointerReleased += OnDragEnd;
		src.PointerCaptureLost += OnDragLost;
		e.Pointer.Capture(src);
		e.Handled = true;
	}

	private void OnDragMove(object? sender, PointerEventArgs e)
	{
		LayoutCard? card = _dragCard;
		if (card == null || sender is not InputElement src)
		{
			return;
		}
		// [Avalonia] e.LeftButton != Pressed → GetCurrentPoint(src).Properties.IsLeftButtonPressed
		if (!e.GetCurrentPoint(src).Properties.IsLeftButtonPressed)
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
		if (_stackFloat)
		{
			// [audit/B] 丝滑版:每帧只动被拖卡片的 RenderTransform(零布局);换位判定带 6px 滞回,
			// 只有"确实越过相邻块中点 ±H"才动模型 + 搬元素 + 给其余块补 FLIP(与 Windows 同序)。
			_stackPointerYLive = y;
			MoveStackFloat();
			AutoScrollNearEdgeStack(y);
			int target = StackDragPlan.TargetIndex(_state.VisibleIds(), _dragHeights, _state.Page.Gap, y, before);
			if (target != before)
			{
				ApplyStackReorder(id, target);
			}
			return;
		}
		// 浮起关闭(Motion 关 / 非单列):保持原来的"交换即落位"
		// 一次移动可能跨多格:纯模型层按"相邻块中点"逐格比较(算法移植自旧 BlockHost.OnDragMove)
		_state.DragReorder(id, y, _dragHeights);
		if (_state.VisibleIndexOf(id) != before)
		{
			InsertVisual(id);
		}
	}

	private void OnDragEnd(object? sender, PointerReleasedEventArgs e)
	{
		EndDrag(sender);
	}

	private void OnDragLost(object? sender, PointerCaptureLostEventArgs e)
	{
		EndDrag(sender);
	}

	private void EndDrag(object? sender)
	{
		LayoutCard? card = _dragCard;
		PageLayoutV2? before = _dragBefore;
		// [audit/B] 先收浮起拿到"松手那一刻的位移",再走统一收尾,最后补落位动画
		// (顺序反了的话落位动画会被收尾逻辑立刻清掉 —— 松手"啪"一下归位,没有收敛感)
		double settleFrom = StopStackFloat();
		_dragCard = null;
		_dragBefore = null;
		if (sender is InputElement src)
		{
			src.PointerMoved -= OnDragMove;
			src.PointerReleased -= OnDragEnd;
			src.PointerCaptureLost -= OnDragLost;
		}
		// [Avalonia] Windows 这里还有 src.ReleaseMouseCapture();Avalonia 的捕获由
		// PointerReleased / PointerCaptureLost 两条路径收尾(分别来自 OnDragEnd / OnDragLost),
		// 本方法就是这两条路径共同的终点 —— 不需要(也没有)显式的 IsMouseCaptured 检查。
		if (card == null)
		{
			return;
		}
		card.Opacity = 1.0;
		StartStackSettle(card, settleFrom);
		if (SameOrder(_dragOrder, _state.Order))
		{
			return;
		}
		_session.PushUndo(before);
		Persist();
		RefreshToolbar();
	}

	private static bool SameOrder(List<string> a, List<string> b)
	{
		if (a.Count != b.Count)
		{
			return false;
		}
		for (int i = 0; i < a.Count; i++)
		{
			if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
			{
				return false;
			}
		}
		return true;
	}

	// ————————————— stack 浮起跟手(丝滑版,对齐 Windows LayoutEditor.BeginStackFloat 等) —————————————
	//
	// 拖动时被拖卡片按像素跟手(RenderTransform 位移,零布局),跨中点(带 6px 滞回)才换位;
	// 换位时其余块用 160ms 的 FLIP 从旧位置滑到新位置,松手后落位动画收敛。
	// 记账:换位会把被拖卡片的"格子位置"搬走一格(元素被 Insert 到新下标),视觉上会瞬移,
	// 所以每次换位后把"格子位置的变化量"累进 _stackFloatBase 并加到它的 translate 上,
	// 让它停在你手指下面不动 —— 与 GridCanvasPanel 的 FLIP 是同一套补偿思路。

	/// <summary>是否处于 stack 浮起拖拽(仅单列 + 动画开启)。</summary>
	private bool _stackFloat;

	/// <summary>被拖卡片身上那个专用的平移(只由本文件创建/销毁)。</summary>
	private TranslateTransform? _stackFloatTt;

	/// <summary>起手时指针在 _list 内的 y。</summary>
	private double _stackPointerY;

	/// <summary>当前指针在 _list 内的 y(指针事件与自动滚动都会更新它)。</summary>
	private double _stackPointerYLive;

	/// <summary>换位补偿累加到被拖卡片上的基准位移。</summary>
	private double _stackFloatBase;

	/// <summary>换位前各卡片在 _list 内的 top(FLIP 用)。</summary>
	private readonly Dictionary<string, double> _stackTopsBefore = new Dictionary<string, double>(StringComparer.Ordinal);

	/// <summary>stack 拖拽的边缘自动滚动计时器(只在浮起时运行)。</summary>
	private DispatcherTimer? _stackScroll;

	private int _stackAutoDir;

	private double _stackAutoStep;

	private void BeginStackFloat(LayoutCard card, PointerPressedEventArgs e)
	{
		_stackFloat = false;
		if (!Motion.Enabled || !_state.Plan().IsSingleColumn)
		{
			return; // 多列 / 关了动画:走原来的"交换即落位",行为逐像素不变
		}
		_stackFloat = true;
		_stackPointerY = e.GetPosition(_list).Y;
		_stackPointerYLive = _stackPointerY;
		_stackFloatBase = 0.0;
		TranslateTransform tt = new TranslateTransform();
		card.RenderTransform = tt;
		_stackFloatTt = tt;
		card.ZIndex = 1000;
	}

	// 指针 → 卡片位移(只写 RenderTransform,不触发布局)
	private void MoveStackFloat()
	{
		TranslateTransform? tt = _stackFloatTt;
		if (tt == null)
		{
			return;
		}
		tt.Y = _stackFloatBase + (_stackPointerYLive - _stackPointerY);
	}

	// 换位:模型动一格 → 元素搬到新下标 → 其余块 FLIP;被拖块用基准位移抵消格子移动
	private void ApplyStackReorder(string id, int target)
	{
		CaptureStackTops();
		if (!_state.MoveVisibleTo(id, target))
		{
			return;
		}
		InsertVisual(id);
		FlipStackCards();
	}

	private void CaptureStackTops()
	{
		_stackTopsBefore.Clear();
		foreach (Control child in _list.Children)
		{
			if (child is not LayoutCard card || string.IsNullOrEmpty(card.Spec?.Id))
			{
				continue;
			}
			Point? top = card.TranslatePoint(new Point(0.0, 0.0), _list);
			if (top.HasValue)
			{
				_stackTopsBefore[card.Spec.Id] = top.Value.Y;
			}
		}
	}

	private void FlipStackCards()
	{
		foreach (KeyValuePair<string, double> item in _stackTopsBefore)
		{
			if (!_cards.TryGetValue(item.Key, out LayoutCard? card) || card == null)
			{
				continue;
			}
			Point? now = card.TranslatePoint(new Point(0.0, 0.0), _list);
			if (!now.HasValue)
			{
				continue;
			}
			double delta = item.Value - now.Value.Y;
			if (Math.Abs(delta) < 1.0)
			{
				continue;
			}
			if (ReferenceEquals(card, _dragCard))
			{
				// 被拖卡片:把格子位置的跳变折进基准位移,视觉上纹丝不动
				_stackFloatBase += delta;
				MoveStackFloat();
				continue;
			}
			StackFlipOnce(card, delta);
		}
	}

	// 让位动画:先挂上"看起来还在旧位置"的偏移,再 ease-out 归零(补间驱动复用 LayoutTween)
	private void StackFlipOnce(LayoutCard card, double delta)
	{
		LayoutTween.Start(_list, card, LayoutMotion.FlipMs, delegate(double p)
		{
			LayoutMotion.SetOffset(card, 0.0, delta * (1.0 - LayoutMotion.EaseOutCubic(p)));
		});
	}

	// 收掉浮起状态,返回"松手那一刻的 translate.Y"(给落位动画当起点);不动画、不落盘。
	private double StopStackFloat()
	{
		StopStackAutoScroll();
		if (!_stackFloat)
		{
			return 0.0;
		}
		_stackFloat = false;
		TranslateTransform? tt = _stackFloatTt;
		_stackFloatTt = null;
		LayoutCard? card = _dragCard;
		if (card == null || tt == null)
		{
			return 0.0;
		}
		double from = tt.Y;
		card.ZIndex = 0;
		card.RenderTransform = null;
		return from;
	}

	// 落位动画:从"手指位置"滑回它自己的格子(在收尾之后调,避免被收尾逻辑清掉)
	private void StartStackSettle(LayoutCard card, double from)
	{
		if (!Motion.Enabled || Math.Abs(from) < 1.0)
		{
			return;
		}
		LayoutTween.Start(_list, card, LayoutMotion.DropMs, delegate(double p)
		{
			LayoutMotion.SetOffset(card, 0.0, from * (1.0 - LayoutMotion.EaseOutCubic(p)));
		});
	}

	// stack 拖拽的边缘自动滚动:靠近可视区上下边缘就把 _scroll 推一点,并把"滚了多少"折回指针内容坐标
	private void AutoScrollNearEdgeStack(double pointerY)
	{
		if (!_stackFloat)
		{
			return;
		}
		double height = _scroll.Bounds.Height;
		if (height <= 1.0 || _scroll.ScrollBarMaximum.Y <= 0.5)
		{
			StopStackAutoScroll();
			return;
		}
		double inViewport = pointerY + _list.Margin.Top - _scroll.Offset.Y;
		double step = StackDragPlan.EdgeScrollStep(height, inViewport, 36.0, 18.0);
		if (step <= 0.0)
		{
			StopStackAutoScroll();
			return;
		}
		_stackAutoDir = (inViewport < height / 2.0) ? -1 : 1;
		_stackAutoStep = step;
		if (_stackScroll == null)
		{
			_stackScroll = new DispatcherTimer(DispatcherPriority.Input)
			{
				Interval = TimeSpan.FromMilliseconds(30.0)
			};
			_stackScroll.Tick += OnStackAutoScrollTick;
		}
		if (!_stackScroll.IsEnabled)
		{
			_stackScroll.Start();
		}
	}

	private void StopStackAutoScroll()
	{
		_stackAutoDir = 0;
		_stackAutoStep = 0.0;
		if (_stackScroll != null && _stackScroll.IsEnabled)
		{
			_stackScroll.Stop();
		}
	}

	private void OnStackAutoScrollTick(object? sender, EventArgs e)
	{
		LayoutCard? card = _dragCard;
		if (!_stackFloat || card == null || _stackAutoDir == 0)
		{
			StopStackAutoScroll();
			return;
		}
		double max = _scroll.ScrollBarMaximum.Y;
		if (max <= 0.5)
		{
			StopStackAutoScroll();
			return;
		}
		double before = _scroll.Offset.Y;
		double to = Math.Max(0.0, Math.Min(max, before + _stackAutoDir * _stackAutoStep));
		double moved = to - before;
		if (Math.Abs(moved) < 0.01)
		{
			return;
		}
		_scroll.Offset = new Vector(_scroll.Offset.X, to);
		// 物理指针没动,但它在列表里的位置跟着滚走了
		_stackPointerYLive += moved;
		MoveStackFloat();
		string id = card.Spec.Id;
		int index = _state.VisibleIndexOf(id);
		if (index < 0)
		{
			StopStackAutoScroll();
			return;
		}
		int target = StackDragPlan.TargetIndex(_state.VisibleIds(), _dragHeights, _state.Page.Gap, _stackPointerYLive, index);
		if (target != index)
		{
			ApplyStackReorder(id, target);
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
		// [Avalonia] ActualWidth → Bounds.Width
		return Math.Max(1.0, _box.Bounds.Width);
	}

	/// <summary>画布可用高(px);拿不到有限高时用画布参考高(与 Panel 的兜底一致)。</summary>
	private double AxisHeight()
	{
		double h = _box.Bounds.Height;
		return ((h > 1.0) ? h : ((_box.CanvasHeight > 0.0) ? _box.CanvasHeight : LayoutCanvas.RefHeight));
	}

	// 起手:记下手势前的页面(真拖起来才压进撤销栈)+ 所有选中块的起始槽位(用绝对值算位移,避免逐帧累积误差)
	private void StartGesture(string kind, LayoutCard card, PointerPressedEventArgs e)
	{
		// [任务 B] 上一段动画还没放完就开始新手势:先推到终点,保证起手矩形 / 命中与看到的一致
		// [audit/B] 但推到终点之前要把"眼睛看到的残留平移"记下来 —— 被抓起的这块必须从**看到的位置**接着走,
		// 否则抓起的一瞬间它先跳回槽位再跟手(用户体感就是"抓起来卡一下 / 跳位")。
		// 其它块按原样归位(它们本来就在 160ms 让位动画的最后一两像素上,肉眼看不出)。
		Dictionary<string, Vector> residue = new Dictionary<string, Vector>(StringComparer.Ordinal);
		foreach (Control child in _box.Children)
		{
			if (child is LayoutCard visible)
			{
				Vector offset = LayoutMotion.GetOffset(visible);
				if (offset != default(Vector))
				{
					residue[visible.Spec.Id] = offset;
				}
			}
		}
		_box.FinishAnimations();
		_gestureTarget = "";
		_snapHad = false;
		_snapDx = 0.0;
		_snapDy = 0.0;
		// [layout-fix] 规格2-问题2.2:先留"全部槽位 + 行高"的快照 ——
		// 之后每一帧都从这份快照重算,让位不累积;行高快照用来把指针位置折算成行号。
		_gestureAll.Clear();
		foreach (LayoutCell cell in _box.Cells)
		{
			if (cell != null && !string.IsNullOrEmpty(cell.Id))
			{
				_gestureAll[cell.Id] = cell.Clone();
			}
		}
		_gestureRows = ((double[])(_box.RowHeightValues?.Clone() ?? Array.Empty<double>()));
		_gestureBefore = _state.ToPage();
		_gestureMoved = false;
		_gesture = kind;
		_gestureHandle = "";
		_gestureStart = e.GetPosition(_box);
		_lastPointer = _gestureStart;
		_lastPointerInScroll = e.GetPosition(_boxScroll);
		_pointerDown = true;
		_gestureCells.Clear();
		foreach (string id in _session.OrderedSelection(_state.Order))
		{
			LayoutCell? selected = _box.CellOf(id);
			if (selected != null)
			{
				_gestureCells[id] = selected.Clone();
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
		_gestureRects.Clear();
		foreach (string id in _gestureCells.Keys)
		{
			LayoutRect? rect = _box.RectOf(id);
			if (rect.HasValue)
			{
				LayoutRect start = rect.Value;
				if (residue.TryGetValue(id, out Vector offset))
				{
					// 从"眼睛看到的位置"起手:把残留平移折进起手矩形
					start = LayoutMotion.Offset(start, offset.X, offset.Y);
					if (string.Equals(id, card.Spec.Id, StringComparison.Ordinal))
					{
						_gestureAnchor = start;
					}
				}
				_gestureRects[id] = start;
			}
		}
		// [Avalonia] CaptureMouse → e.Pointer.Capture(_box)
		_boxPointer = e.Pointer;
		e.Pointer.Capture(_box);
		// [layout-fix] 参考件v2-对齐:贴近边缘拖动时自动滚动画布
		StartAutoScroll();
		_box.PointerMoved += OnBoxDragMove;
		_box.PointerReleased += OnBoxGestureEnd;
		_box.PointerCaptureLost += OnBoxGestureLost;
	}

	// 整块拖拽(Ctrl = 只改选择;多选时整体移动)
	internal void BeginBoxDrag(LayoutCard card, PointerPressedEventArgs e)
	{
		if (!_edit || !IsBoxMode || _gesture.Length != 0 || card == null)
		{
			return;
		}
		if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != KeyModifiers.None)
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
	internal void BeginBoxResize(LayoutCard card, string handle, PointerPressedEventArgs e)
	{
		if (!_edit || !IsBoxMode || _gesture.Length != 0 || card == null || string.IsNullOrEmpty(handle))
		{
			return;
		}
		// [v3] 列表排法:每块固定整行,不给改宽度(右边缘热区也已隐藏;这里再兜一层)
		if (_listKind && (handle.IndexOf('e') >= 0 || handle.IndexOf('w') >= 0))
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
	private void OnBoxMouseDown(object? sender, PointerPressedEventArgs e)
	{
		if (!_edit || !IsBoxMode || _gesture.Length != 0)
		{
			return;
		}
		if (!e.GetCurrentPoint(_box).Properties.IsLeftButtonPressed)
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
		_lastPointer = _gestureStart;
		_lastPointerInScroll = e.GetPosition(_boxScroll);
		_pointerDown = true;
		_boxPointer = e.Pointer;
		e.Pointer.Capture(_box);
		_box.PointerMoved += OnBoxMarqueeMove;
		_box.PointerReleased += OnBoxGestureEnd;
		_box.PointerCaptureLost += OnBoxGestureLost;
		e.Handled = true;
	}

	private void OnBoxMarqueeMove(object? sender, PointerEventArgs e)
	{
		if (_gesture != "marquee" || !e.GetCurrentPoint(_box).Properties.IsLeftButtonPressed)
		{
			return;
		}
		Point point = e.GetPosition(_box);
		LayoutRect marquee = new LayoutRect(Math.Min(_gestureStart.X, point.X), Math.Min(_gestureStart.Y, point.Y), Math.Abs(point.X - _gestureStart.X), Math.Abs(point.Y - _gestureStart.Y));
		_guide.ShowMarquee(marquee);
		_session.SelectMany(_box.IdsIn(marquee));
		UpdateSelectionVisual();
	}

	private void OnBoxDragMove(object? sender, PointerEventArgs e)
	{
		if (_gesture.Length == 0 || _gesture == "marquee" || !e.GetCurrentPoint(_box).Properties.IsLeftButtonPressed)
		{
			return;
		}
		_lastPointerInScroll = e.GetPosition(_boxScroll);
		ApplyPointer(e.GetPosition(_box));
	}

	// [layout-fix] 参考件v2-对齐:手势位移 → 意图 → 重排(自动滚动也用同一入口按当前指针重算一次)
	private string CanvasGestureSig()
	{
		System.Text.StringBuilder sb = new System.Text.StringBuilder();
		foreach (string id in _gestureCells.Keys)
		{
			LayoutCell? cell = _box.CellOf(id);
			if (cell == null)
			{
				continue;
			}
			sb.Append(id).Append(':');
			sb.Append(cell.NX.ToString("0.####")).Append(',');
			sb.Append(cell.NY.ToString("0.####")).Append(',');
			sb.Append(cell.NW.ToString("0.####")).Append(',');
			sb.Append(cell.NH.ToString("0.####")).Append(';');
		}
		return sb.ToString();
	}

	private void ApplyPointer(Point point)
	{
		if (_gesture.Length == 0 || _gesture == "marquee")
		{
			return;
		}
		_lastPointer = point;
		double dx = point.X - _gestureStart.X;
		double dy = point.Y - _gestureStart.Y;
		if (!_gestureMoved)
		{
			// [Avalonia] SystemParameters.Minimum{Horizontal,Vertical}DragDistance → 自定义常量(见 DragDistance)
			if (Math.Abs(dx) < DragDistance && Math.Abs(dy) < DragDistance)
			{
				return;
			}
			_gestureMoved = true;
			_session.PushUndo(_gestureBefore);
			_box.HoldHeights(true);
		}
		if (_box.IsCanvas)
		{
			// 自由画布按像素跟手:卡片停在起手矩形上,再用平移跟指针。
			// 吸附后的格子仍写进模型,给辅助线和松手落位用,不再让卡片本身一格一格跳。
			if (_gesture == "move" && _box.Floating.Count == 0)
			{
				foreach (KeyValuePair<string, LayoutRect> kv in _gestureRects)
				{
					_box.SetFloating(kv.Key, kv.Value);
				}
				_box.ShowPlaceholder = true;
				ShowColumnShade(true);
			}
			if (_gesture == "move")
			{
				foreach (string id in _gestureCells.Keys)
				{
					if (_cards.TryGetValue(id, out LayoutCard? moving))
					{
						LayoutMotion.SetOffset(moving, dx, dy);
					}
				}
			}
			string before = CanvasGestureSig();
			ApplyCanvasGesture(dx, dy);
			_box.CancelFlipArm();
			// 吸附格子没变就不重排、不重画底纹。卡片只靠上面的平移跟手。
			if (before != CanvasGestureSig())
			{
				_box.Reflow();
			}
		}
		else
		{
			// [任务 B] grid:每帧只改被拖块的平移 / 像素尺寸;落点变了才在里面重新让位 + 排布
			ApplyGridGesture(dx, dy);
		}
	}

	private void OnBoxGestureEnd(object? sender, PointerReleasedEventArgs e)
	{
		EndBoxGesture(true, false);
	}

	private void OnBoxGestureLost(object? sender, PointerCaptureLostEventArgs e)
	{
		EndBoxGesture(false, false);
	}

	// 手势收尾:解绑事件 → (没真拖动:到此为止 / 取消或丢了指针捕获:回滚 / 松手:落盘)→ 重画
	private void EndBoxGesture(bool persist, bool rollback)
	{
		// [layout-fix] 规格 §3-问题7:拖 / 缩放过的块要"优先占住落点"(PromoteToFront),
		// 先把它们记下来 —— 下面立刻会清掉 _gesture / _gestureCells,清完就问不出来了。
		// 框选、没越过拖动距离的单击只改了选择:布局没动,撤销栈里也没压东西,不提交也不重画。
		bool movedSomething = _gestureMoved && (_gesture == "move" || _gesture == "resize");
		List<string> touchedIds = new List<string>(_gestureCells.Keys);
		// 先登记滑动，再撤掉浮起。顺序反了的话，撤浮起触发的排布会在平移还在时把卡片放到吸附点上，卡片被甩出去。
		if (movedSomething)
		{
			_box.ArmFlip();
		}
		// [任务 B] 收尾前记下浮起状态:改大小的块要从"手里的像素矩形"落位到吸附尺寸
		bool wasResize = _gesture == "resize";
		Dictionary<string, LayoutRect> floats = new Dictionary<string, LayoutRect>(_box.Floating, StringComparer.Ordinal);
		_box.HoldHeights(false);
		_box.ShowPlaceholder = false;
		ShowColumnShade(false);
		// [v3] 网格底纹只在拖动中画:松手立刻擦掉
		_box.InvalidateVisual();
		_box.ClearFloating();
		_gestureRects.Clear();
		_gestureTarget = "";
		_autoScroll?.Stop();
		_gesture = "";
		_gestureHandle = "";
		_gestureCells.Clear();
		_gestureMoved = false;
		_gestureBefore = null;
		_pointerDown = false;
		_box.PointerMoved -= OnBoxDragMove;
		_box.PointerMoved -= OnBoxMarqueeMove;
		_box.PointerReleased -= OnBoxGestureEnd;
		_box.PointerCaptureLost -= OnBoxGestureLost;
		// [Avalonia] ReleaseMouseCapture → IPointer.Capture(null)。释放会同步触发 PointerCaptureLost,
		// 那一刻 _gesture 已经清空 ⇒ 重入的 EndBoxGesture 会在 movedSomething=false 处直接返回(与 WPF 同构)。
		_boxPointer?.Capture(null);
		_boxPointer = null;
		_guide.Clear();
		// [audit/B] 手势收尾:预览期的裁剪一并收掉(缩小时临时裁在卡片里,落定 / 取消后要还回来)
		foreach (string id in touchedIds)
		{
			if (_cards.TryGetValue(id, out LayoutCard? done))
			{
				done.ClipToBounds = false;
			}
		}
		if (!movedSomething)
		{
			foreach (string id in touchedIds)
			{
				if (_cards.TryGetValue(id, out LayoutCard? idle))
				{
					LayoutMotion.SetOffset(idle, 0.0, 0.0);
				}
			}
			return;
		}
		if (rollback || !persist)
		{
			PageLayoutV2? back = _session.DiscardUndo();
			if (back != null)
			{
				_state = LayoutEngine.LoadState(_pageId, back, _refs);
				_session.Retain(_state.Order);
			}
			Render();
			if (wasResize)
			{
				BeginLandings(floats);
			}
			return;
		}
		CommitBox(touchedIds);
		if (wasResize)
		{
			BeginLandings(floats);
		}
	}

	// [任务 B] 改大小松手:每块从手里的像素矩形补间到吸附后的槽位矩形
	private void BeginLandings(Dictionary<string, LayoutRect> floats)
	{
		foreach (KeyValuePair<string, LayoutRect> kv in floats)
		{
			_box.BeginLanding(kv.Key, kv.Value);
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
			// [2026-11-19 修复 #3] 「自动靠拢」关:只画对齐辅助线,不做磁性吸附(不再"自动靠到旁边的块上")
			bool magnet = AutoPackOn && _gesture == "move" && guides.HasSnap;
			// [任务 B] 磁性吸附"吸住 / 松开 / 换吸附位"的那一帧:用短动画过渡这一下跳变,而不是瞬移
			bool snapping = magnet;
			if (LayoutMotion.SnapChanged(_snapHad, _snapDx, _snapDy, snapping, guides.Dx, guides.Dy))
			{
				_box.ArmFlip(LayoutMotion.SnapMs);
			}
			_snapHad = snapping;
			_snapDx = (snapping ? guides.Dx : 0.0);
			_snapDy = (snapping ? guides.Dy : 0.0);
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

	// [layout-fix] 规格2-问题2.2(重写网格拖动 / 缩放):
	//  - 列 = 卡片左上角跟随指针后的最近列;行 = 指针所在的行(按手势开始时的行高快照算),
	//    不再用"卡片自身高度"当行步长(高卡片几乎拖不动、矮卡片一动就跳好几行的根源)。
	//  - 每次移动都从全部槽位快照恢复,再以被拖块为固定点做一次压紧:其他块自动让位 / 上浮,不留空洞;
	//    同一行里被占位的块优先换到被拖块原来的列(左右互换)。
	//  - 对齐辅助线在 grid 模式去掉:格子吸附已经足够,辅助线吸附会和让位逻辑互相打架(canvas 模式仍保留)。
	private void ApplyGridGesture(double dx, double dy)
	{
		int cols = LayoutGrid.Columns(_state.Page);
		double columnStep = Math.Max(1.0, _box.ColumnWidthValue + _box.Gap);
		string? primary = PrimaryId();
		if (primary == null || !_gestureCells.TryGetValue(primary, out LayoutCell? start))
		{
			return;
		}
		// [任务 B] 第一次真拖起来:手势块浮起(排在起手的像素矩形上),画布开始画落点占位框
		if (_box.Floating.Count == 0)
		{
			foreach (KeyValuePair<string, LayoutRect> kv in _gestureRects)
			{
				_box.SetFloating(kv.Key, kv.Value);
			}
			_box.ShowPlaceholder = true;
			ShowColumnShade(true);
			_box.InvalidateVisual();
		}
		Dictionary<string, int>? swap = null;
		string target;
		if (_gesture == "resize")
		{
			// [layout-fix] 规格2-问题2.2 + 参考件v2-对齐:宽度按列改;高度在**自动行高**的页面(由 stack 转来的都是)
			// 直接记**像素高度** HeightPx —— 原来按"行"改 H,可行高本来就跟着内容走,所以怎么拉高度都不变
			// (这正是录屏里"拉不了大小"的成因)。固定行高(row_h > 0)的页面仍然按行改。
			bool autoRows = _state.Page.RowH <= 0.0;
			int dr = (autoRows ? 0 : (int)Math.Round(dy / Math.Max(1.0, _state.Page.RowH + _box.Gap)));
			int dc = (int)Math.Round(dx / columnStep);
			// [R35 lin-l12] §5.1:拖右边缘按整列变化,并受该块自己的 MinCols 限制(之前只看全局 2 列下限)
			int minCols = Math.Min(cols, Math.Max(MinCols(cols), MinColsOf(primary, cols)));
			// [任务 B] 卡片按**像素**跟手改大小(只重排这一张);占位框 = 吸附后的槽位尺寸
			double minW = _box.ColumnWidthValue * minCols + _box.Gap * Math.Max(0, minCols - 1);
			double full = _box.ColumnWidthValue * cols + _box.Gap * Math.Max(0, cols - 1);
			double minH = (autoRows ? MinBlockPx : Math.Max(1.0, _state.Page.RowH));
			foreach (KeyValuePair<string, LayoutRect> kv in _gestureRects)
			{
				_box.SetFloating(kv.Key, LayoutMotion.ResizePixels(kv.Value, _gestureHandle, dx, dy, minW, minH, full - kv.Value.X));
				// [audit/B] 缩小预览时内容可能比卡片高:先在卡片里裁住(不然内容溢出卡片边界糊出去),
				// 落定后由 EndBoxGesture 恢复 —— 对齐 Windows 参考件 ResizeFloat 的 ClipToBounds 处理。
				if (_cards.TryGetValue(kv.Key, out LayoutCard? shrinking))
				{
					shrinking.ClipToBounds = true;
				}
			}
			if (_box.Floating.TryGetValue(primary, out LayoutRect live))
			{
				Point corner = _box.TranslatePoint(new Point(live.X + live.W, live.Y + live.H), _guide) ?? new Point(live.X + live.W, live.Y + live.H);
				// [v3] §1.A.4:宽度只能是整数列 —— 拖右边缘时提示「N / 12 列」(效果图 .rz b);只拖下边时仍显示像素尺寸
				if (_gestureHandle.IndexOf('e') >= 0)
				{
					int predicted = LayoutSchema.ClampInt(start.W + dc, Math.Max(1, minCols), Math.Max(Math.Max(1, minCols), cols - start.X));
					_guide.ShowSize(corner, string.Format(I18nCore.Tr("{0} / {1} 列"), predicted, cols));
				}
				else
				{
					_guide.ShowSize(corner, LayoutMotion.SizeLabel(live.W, live.H));
				}
			}
			// 吸附后的目标(列数 / 行数 / 像素高)指纹:没变就不重新让位
			System.Text.StringBuilder key = new System.Text.StringBuilder("r");
			Dictionary<string, double> heights = new Dictionary<string, double>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, LayoutCell> kv in _gestureCells)
			{
				key.Append('|').Append(kv.Key);
				if (autoRows && _gestureHandle.IndexOf('s') >= 0)
				{
					double startH = (_gestureRects.TryGetValue(kv.Key, out LayoutRect r0) ? r0.H : _gestureAnchor.H);
					double px = LayoutMotion.SnapHeightPx(startH, dy, _snapStep, MinBlockPx);
					heights[kv.Key] = px;
					key.Append(':').Append(px.ToString("0"));
				}
			}
			key.Append('|').Append(dc).Append(',').Append(dr);
			target = key.ToString();
			if (target == _gestureTarget)
			{
				return;
			}
			RestoreGestureSnapshot();
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
				if (heights.TryGetValue(kv.Key, out double fixedPx))
				{
					cell.HeightPx = fixedPx;
					CardFor(kv.Key).SetFixedHeight(true);
				}
			}
		}
		else
		{
			// [任务 B] 被拖块按像素跟手:只改平移,不重新布局(浮起矩形 = 起手矩形,平移 = 指针位移)
			foreach (string id in _gestureCells.Keys)
			{
				if (_cards.TryGetValue(id, out LayoutCard? moving))
				{
					LayoutMotion.SetOffset(moving, dx, dy);
				}
			}
			// 落点规则不变(纯函数):列 = 卡片左上角跟随指针后的最近列;行 = 指针所在的行(手势开始时的行高快照)
			(int nx, int ny) = LayoutMotion.GridDropTarget(_gestureAnchor.X, dx, columnStep, cols, start.W, _gestureRows, _gestureStart.Y + dy, _box.Gap);
			// [2026-11-19 修复 #1] 并排落位:被拖块太宽、放在目标行会和别的块打架,但指针落在该行一段空列上 →
			// 收窄到那段空列,与那一行的块并排(录屏 1:22–1:30:「版本列表」拖到「筛选」右边松手,原来会弹回整行)。
			// 纯上下拖动不触发(左边停在第 0 列)。
			int fitX = -1;
			int fitW = 0;
			if (_gestureCells.Count == 1)
			{
				int rawX = (int)Math.Round((_gestureAnchor.X + dx) / columnStep);
				(fitX, fitW) = SideBySideSlot(primary, rawX, ny, start.W, start.H, cols);
				if (fitX >= 0)
				{
					nx = fitX;
				}
			}
			target = ((fitX >= 0) ? ("m|" + nx + "," + ny + "|w" + fitW) : ("m|" + nx + "," + ny));
			if (target == _gestureTarget)
			{
				return;
			}
			RestoreGestureSnapshot();
			if (fitX >= 0)
			{
				// 并排落位:只把被拖块收窄钉到那段空列上,其余块只解冲突(不互换)
				LayoutCell? lead = _box.CellOf(primary);
				if (lead != null)
				{
					lead.X = fitX;
					lead.Y = ny;
					lead.W = fitW;
					_guide.Clear();
					_gestureTarget = target;
					_box.ArmFlip();
					_box.CompactCells(new HashSet<string>(_gestureCells.Keys, StringComparer.Ordinal), null, AutoPackOn);
					return;
				}
			}
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
			_guide.Clear();
		}
		_gestureTarget = target;
		// 钉住被拖 / 缩放中的块,其余块实时让位(重力上浮 + 左右互换);让位的块走 FLIP 滑过去
		_box.ArmFlip();
		_box.CompactCells(new HashSet<string>(_gestureCells.Keys, StringComparer.Ordinal), swap, AutoPackOn);
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
		foreach (KeyValuePair<string, LayoutCell> kv in _gestureAll)
		{
			if (string.Equals(kv.Key, primary, StringComparison.Ordinal))
			{
				continue;
			}
			LayoutCell c = kv.Value;
			int top = c.Y;
			int bottom = c.Y + Math.Max(1, c.H);
			// 与被拖块将占的行区间 [row, row + height) 有交集的块都算「这一行的块」
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
			// 左边落在别的块上:只有越过那块的一半,才算「想放到它右边」(纯上下拖动时左边停在第 0 列,不触发)
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

	// 全部槽位回到起手时的样子(让位不累积;每次落点变了都从这份快照重算)
	private void RestoreGestureSnapshot()
	{
		foreach (KeyValuePair<string, LayoutCell> kv in _gestureAll)
		{
			LayoutCell? cell = _box.CellOf(kv.Key);
			if (cell != null)
			{
				cell.X = kv.Value.X;
				cell.Y = kv.Value.Y;
				cell.W = kv.Value.W;
				cell.H = kv.Value.H;
				cell.HeightPx = kv.Value.HeightPx;
			}
		}
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
			// [Avalonia] DispatcherTimer 同名同义(DispatcherPriority.Input 也有)
			_autoScroll = new DispatcherTimer(DispatcherPriority.Input)
			{
				Interval = TimeSpan.FromMilliseconds(30.0)
			};
			_autoScroll.Tick += OnAutoScrollTick;
		}
		_autoScroll.Start();
	}

	private void OnAutoScrollTick(object? sender, EventArgs e)
	{
		// [Avalonia] Mouse.LeftButton → 手势期间自己维护的 _pointerDown
		if (_gesture.Length == 0 || _gesture == "marquee" || !_pointerDown || _boxScroll == null)
		{
			_autoScroll?.Stop();
			return;
		}
		if (!_gestureMoved)
		{
			return;
		}
		// [Avalonia] Mouse.GetPosition(_boxScroll) → 手势期间缓存的最后位置(相对 _boxScroll)
		Point point = _lastPointerInScroll;
		double height = _boxScroll.Bounds.Height;
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
		double before = _boxScroll.Offset.Y;
		double target = Math.Max(0.0, before + delta);
		// [Avalonia] ScrollToVerticalOffset(y) → 直接写 Offset(WPF 的那个方法在 Avalonia 的
		// ScrollViewer 上不存在;Offset 是 Vector,可读可写)
		_boxScroll.Offset = new Vector(_boxScroll.Offset.X, target);
		_boxScroll.UpdateLayout();
		if (Math.Abs(_boxScroll.Offset.Y - before) > 0.1)
		{
			// [Avalonia] Mouse.GetPosition(_box) → 用"指针在滚动视口里的位置 − 画布原点在视口里的位置"
			// 现算一次(滚动之后画布原点会随偏移移动,与 WPF 每次现取坐标同构)
			Point origin = _box.TranslatePoint(default, _boxScroll) ?? default(Point);
			ApplyPointer(new Point(_lastPointerInScroll.X - origin.X, _lastPointerInScroll.Y - origin.Y));
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
		_foldAllBtn.Content = U.Txt(AnyCardOpen() ? I18nCore.Tr("全部折叠") : I18nCore.Tr("全部展开"));
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
		foreach (Control child in _box.Children)
		{
			if (child is LayoutCard card)
			{
				card.SetSelected(_edit && _session.IsSelected(card.Spec.Id));
			}
		}
	}

	// ————————————— 键盘 / 撤销重做 / 剪贴板 —————————————

	private void OnEditorKeyDown(object? sender, KeyEventArgs e)
	{
		if (!_edit)
		{
			return;
		}
		// [Avalonia] Keyboard.Modifiers → e.KeyModifiers(修饰键在事件参数上,不是静态类)
		bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
		bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
		// [Avalonia] Key.ToString() 的字符串契约与 WPF 一致(Avalonia 的 Key 枚举名:Escape/Delete/Back/
		// Left/Right/Up/Down/Z/Y/C/V);LayoutEditSession.KeyOf 认的就是这套名字,测试里逐键钉住。
		LayoutKey key = LayoutEditSession.KeyOf(e.Key.ToString(), ctrl, shift);
		switch (key)
		{
		case LayoutKey.Escape:
			if (_gesture.Length != 0)
			{
				EndBoxGesture(false, true);
			}
			else if (IsDrawerOpen)
			{
				// [v3] 抽屉开着:Esc 先关抽屉,再按一次才是完成编辑(与效果图一致)
				ShowDrawer(false);
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

	// Delete / Back([v3] §1.A.6 的键盘版):选中的**自建组件**删掉(只摘本页块引用,widgets[] 定义保留,能撤销);
	// 内置内容 / 插件块不删,改为隐藏(与悬停按钮一致:眼睛隐藏、垃圾桶删除),在「添加」里能找回
	private void DeleteSelection()
	{
		if (!IsBoxMode || _session.SelectedCount == 0)
		{
			return;
		}
		List<string> selected = _session.OrderedSelection(_state.Order);
		PageLayoutV2 before = _state.ToPage();
		int hidden = 0;
		foreach (string id in selected)
		{
			if (LayoutEngine.WidgetIdOf(id) == null && !_state.Hidden.Contains(id))
			{
				_state.Hide(id);
				hidden++;
			}
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
			if (selected.Contains(block.Id) && LayoutEngine.WidgetIdOf(block.Id) != null)
			{
				removed++;
				continue;
			}
			keep.Add(block);
		}
		if (removed == 0 && hidden == 0)
		{
			return;
		}
		_session.PushUndo(before);
		page.Blocks = keep;
		_box.ArmFlip();
		_state = LayoutEngine.LoadState(_pageId, page, _refs);
		_session.Retain(_state.VisibleIds());
		CompactState();
		Persist();
		Render();
		LayoutFx.ShowToast(
			(removed > 0) ? I18nCore.Tr("已删除选中的组件") : I18nCore.Tr("已隐藏选中的内容"),
			(removed > 0) ? I18nCore.Tr("按 Ctrl+Z 可以撤销") : I18nCore.Tr("在「添加」里可以找回"),
			ToastKind.Success);
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
		_box.ArmFlip();
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
			LayoutFx.ShowToast(I18nCore.Tr("没有可撤销的操作"), "", ToastKind.Info);
			return;
		}
		ApplySnapshot(snapshot, I18nCore.Tr("已撤销"), ToastKind.Info);
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
			LayoutFx.ShowToast(I18nCore.Tr("没有可重做的操作"), "", ToastKind.Info);
			return;
		}
		ApplySnapshot(snapshot, I18nCore.Tr("已重做"), ToastKind.Info);
	}

	private void ApplySnapshot(PageLayoutV2 page, string message, ToastKind kind)
	{
		// [任务 B] 撤销 / 重做也让块滑回去,而不是瞬移
		_box.ArmFlip();
		_state = LayoutEngine.LoadState(_pageId, page, _refs);
		_session.Retain(_state.Order);
		SyncLayoutKindAfterSnapshot();
		Persist();
		Render();
		LayoutFx.ShowToast(message, "", kind);
	}

	// Ctrl+C:有选中就复制选中的块,没选中就复制整页(§4.G 的"复制粘贴布局")
	private void CopyToClipboard()
	{
		int count = _session.CopyFrom(_state.ToPage(), !IsBoxMode);
		if (count == 0)
		{
			return;
		}
		LayoutFx.ShowToast(I18nCore.Tr("已复制到剪贴板"), count.ToString(), ToastKind.Success);
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
			LayoutFx.ShowToast(I18nCore.Tr("没有可粘贴的区块"), "", ToastKind.Info);
			return;
		}
		_session.PushUndo(_state.ToPage());
		_state = LayoutEngine.LoadState(_pageId, page, _refs);
		_session.Retain(_state.Order);
		Persist();
		Render();
	}

	// [Avalonia] Windows 从 Application.Current.TryFindResource("B.Accent") 取主题色;
	// Linux 侧主题资源走 Ui 门面(U.Brush → Application.Resources 的观察),拿不到用同一个兜底色。
	internal static Color AccentColor()
	{
		if (U.Brush("B.Accent") is SolidColorBrush brush)
		{
			return brush.Color;
		}
		return Color.FromRgb(46, 155, 107);
	}
}

// 单个区块卡:常态零装饰(厚度 0 的透明描边 + 无底色),仅承载内容;
// 编辑态出现操作条(拖动提示 + 块标题 + 折叠 + 显隐按钮),主题色描边 + 10 透明度蒙版。
// L2 起还承载三件事(缺省都不改变现状渲染):
//   1) 外观参数(§4.B):style=card/divider、常态标题(title)、每块边距(pad);缺省 plain + 无标题 + 无边距;
//   2) 画布手势:grid/canvas 模式下块身可拖拽、右边/下边整条边(及 canvas 的 8 向)可缩放;
//   3) 多选态:主题色 2px 描边 + 26 透明度蒙版。
internal sealed class LayoutCard : Border, ILayoutCanvasChild
{
	private readonly LayoutEditor _host;

	// [v3] §1.A.2 / §1.A.6:编辑态的块名(左上)与操作按钮(右上)浮在卡片上沿,只在悬停 / 选中时出现,不占内容高度
	private readonly Grid _bar;

	// [v3] §1.A.1:编辑态边框 = 灰色虚线(外扩 4px,对应效果图 outline-offset);选中 = 主题色实线
	private readonly Avalonia.Controls.Shapes.Rectangle _outline;

	// [v3] §1.A.4:网格模式右边缘的改宽度把手(效果图 .rz);整条右边热区仍是 _strips["e"]
	private readonly Border _pill;

	// [R35 lin-l12] Q3:网格模式下边缘的改高度把手(与右边缘把手同款,横放;悬停 / 选中时出现;双击 = 恢复跟随内容高度)
	private readonly Border _pillS;

	// [R35 lin-l12] §5.2 新块 / 找回的块闪一下主题色光晕(效果图 .blk.flash:0 0 0 4px glow → 0,0.9s)
	private DispatcherTimer? _flashTimer;

	private bool _hover;

	// [v3] 列表排法下宽度固定(不给右边缘把手)
	private bool _widthLocked;

	// 自建组件(custom:):块名后缀「· 组件」+ 垃圾桶删除;其余(内置 / 插件)用眼睛隐藏
	private readonly bool _custom;

	private readonly StackPanel _titleRow;

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

	// [Avalonia] 兄弟件 GridCanvasPanel 通过这个接口拿块 id 与折叠态(Windows 是直接 `is LayoutCard` 判类型)
	public string? BlockId
	{
		get
		{
			return Spec?.Id;
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

	public LayoutCard(LayoutEditor host, BlockSpec spec, Control? content)
	{
		_host = host;
		Spec = spec;
		CornerRadius = new CornerRadius(10.0);
		this.Dyn(Border.BorderBrushProperty, "B.Accent");
		// [Avalonia] 同时把块 id 写进 Tag:LayoutCard 之外的容器(以及测试宿主)也能按 Tag 认块
		Tag = spec.Id;
		// [layout-fix] 规格 §3-问题7 + 规格2-问题7:去掉 ⇅「拖拽排序」按钮,换成一个**不可点**的拖动提示图标。
		// 拖动不再需要按住这个小按钮 —— 编辑态下整张卡片任意位置都能按住拖(见 OnCardMouseDown / SetEdit 的十字光标);
		// 图标本身不参与命中(IsHitTestVisible=false),免得它挡住它下面那块的按下事件。
		// [Avalonia] Windows 用图标字体码位(Ico.Sort = ⇅,\ue70d/\ue70e = 折叠箭头);Linux 没有内置图标字体,
		// 一律走矢量图标门面 IconLib(语义键 "Sort" / "ChevronDown" / "ChevronUp" / "Close" / "Add"),
		// 不存在"码位缺字体显示成方块"的问题(对应 Windows 侧 §3-13 的 glyph 偏离)。
		// [layout-fix] 规格2-问题7 / 规格 §3-问题14(Linux 补修):原先这里仍是 "Sort"(⇅ 排序箭头),
		// 让人以为要点它;换成拖动手柄 "Grip"(两道横杠,对应 Windows 的 \ue76f)。
		Control dragIcon = IconLib.Make("Grip", "B.InkMuted", 12);
		if (LayoutEditChrome.HasResource("Geo.Grip"))
		{
			// [R35] L0 的六点拖拽图标(Geo.Grip)存在就用它(lin-int:走 L0 门面 Glyph,线宽 2.2×size/16),否则沿用 IconLib 的 Grip
			dragIcon = LayoutVisualTokens.Glyph(LayoutVisualTokens.GeoKeys.Grip, LayoutVisualTokens.GlyphSizeGrip, null, "B.InkMuted");
		}
		ToolTip.SetTip(dragIcon, I18nCore.Tr("按住区块拖动移动"));
		dragIcon.IsHitTestVisible = false;
		dragIcon.VerticalAlignment = VerticalAlignment.Center;
		// [v3] §1.A.6:隐藏和删除分开 —— 内置内容(含插件块)用眼睛隐藏,自建组件用垃圾桶删除
		_custom = LayoutEngine.WidgetIdOf(spec.Id) != null;
		Button button = (_custom
			? U.IconBtn("Trash", I18nCore.Tr("删除这个组件"), delegate
			{
				_host.DeleteBlock(Spec);
			}, 13.0)
			: LayoutEditChrome.IconButton(LayoutEditChrome.EyeOffData, I18nCore.Tr("隐藏"), delegate
			{
				_host.HideBlock(Spec);
			}, 13.0, "Geo.EyeOff"));
		// R35 lin-int(L12←L0):块上图标按钮套 Layout.IconAct(垃圾桶用 .Danger:悬停整块变红由主题负责)。
		// 主题模板自己画白底细边 + 阴影,所以套上后不再外包 Chip、不再写本地宽高 / 悬停底色(本地值会盖过主题)。
		bool actThemed = LayoutVisualTokens.ApplyTheme(button, _custom ? LayoutVisualTokens.ThemeKeys.IconActDanger : LayoutVisualTokens.ThemeKeys.IconAct);
		if (actThemed && !_custom)
		{
			// 眼睛划线:L0 门面 Glyph(14,线宽 1.6×size/16,描边跟随按钮前景 = 悬停加深)
			button.Content = LayoutVisualTokens.Glyph(LayoutVisualTokens.GeoKeys.EyeOff, LayoutVisualTokens.GlyphSizeIconAct, button);
		}
		// R35 lin-int:标签文字 = Layout.EditLabelText(12 粗体 B.Ink);套不上时退回 U.Txt(12, 粗体)
		TextBlock textBlock = LayoutEditChrome.ThemedText(spec.Title, LayoutVisualTokens.ThemeKeys.EditLabelText, 12.0, bold: true);
		textBlock.VerticalAlignment = VerticalAlignment.Center;
		textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
		textBlock.MaxWidth = 260.0;
		// [layout-fix] 折叠按钮保留对象(折叠逻辑仍在),[v3] 但不再放到卡片上(效果图的块名条只有块名 + 隐藏/删除)
		_foldBtn = U.IconBtn("ChevronDown", I18nCore.Tr("折叠(仅编辑时)"), delegate
		{
			SetFolded(!_folded);
			_host.OnCardFoldChanged();
		}, 11.0);
		StackPanel labelRow = U.H(6.0, dragIcon, textBlock);
		string suffix = LayoutBlockTitles.LabelSuffix(spec.Id);
		if (suffix.Length != 0)
		{
			TextBlock kind = LayoutEditChrome.ThemedText(suffix, LayoutVisualTokens.ThemeKeys.EditLabelNote, 12.0, bold: false, "B.InkMuted");
			kind.VerticalAlignment = VerticalAlignment.Center;
			labelRow.Children.Add(kind);
		}
		Border label = LayoutEditChrome.Chip(labelRow, new Thickness(6.0, 0.0, 10.0, 0.0));
		// Layout.EditLabel 自带 Margin 8,-14(相对卡片);这里标签已放在同样偏移的 _bar 里,只把外边距归零
		label.Margin = new Thickness(0.0);
		if (label.Theme == null)
		{
			label.Height = 24.0;
		}
		label.HorizontalAlignment = HorizontalAlignment.Left;
		Control acts;
		if (actThemed)
		{
			acts = button;
		}
		else
		{
			// 兜底外观(L0 主题不在):沿用 C 的白色小底板 + 本地悬停色
			button.Width = 26.0;
			button.Height = 24.0;
			Border chip = LayoutEditChrome.Chip(button, new Thickness(0.0), themeKey: null);
			if (_custom)
			{
				// 垃圾桶悬停变红(效果图 .acts button.del:hover)
				button.PointerEntered += delegate { chip.Dyn(Border.BackgroundProperty, "B.DangerSoft"); };
				button.PointerExited += delegate { chip.Dyn(Border.BackgroundProperty, "B.Paper"); };
			}
			acts = chip;
		}
		acts.HorizontalAlignment = HorizontalAlignment.Right;
		acts.VerticalAlignment = VerticalAlignment.Top;
		_bar = new Grid
		{
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(8.0, -14.0, 8.0, 0.0),
			ClipToBounds = false
		};
		_bar.Children.Add(label);
		_bar.Children.Add(acts);
		// 常态块标题(§4.B 的 title;缺省 false → 整行折叠,与改动前一致)
		TextBlock titleText = U.Txt(spec.Title, 12.5, bold: true);
		titleText.TextTrimming = TextTrimming.CharacterEllipsis;
		_titleRow = U.H(8.0, titleText, U.Spring());
		_titleRow.Margin = new Thickness(0.0, 0.0, 0.0, 6.0);
		_titleRow.IsVisible = false;
		// 内容体外壳:常态零边距(逐像素等价旧版),编辑态留一点呼吸边距
		_fit.Child = _body;
		_bodyWrap = new Border
		{
			Padding = new Thickness(0.0),
			Child = _fit
		};
		Grid grid = U.G("Auto,Auto,*");
		// [v3] 虚线框画在最底层、外扩 4px(不参与命中,不影响内容排版)
		_outline = new Avalonia.Controls.Shapes.Rectangle
		{
			RadiusX = 13.0,
			RadiusY = 13.0,
			StrokeThickness = 1.5,
			Margin = new Thickness(-4.0),
			IsHitTestVisible = false,
			IsVisible = false
		};
		grid.Children.Add(_outline);
		Grid.SetRowSpan(_outline, 3);
		grid.Add(_titleRow, 1);
		grid.Add(_bodyWrap, 2);
		// 画布模式的缩放手柄层(8 向;grid 用 e/s/se)。缺省与 stack 模式一律折叠 → 不影响默认渲染。
		_handleLayer = new Grid
		{
			IsVisible = false
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
		// [Avalonia] Windows 的 grid.Add(child, row, col, rowSpan, colSpan) 五参重载在 Linux 门面里没有,
		// 改成显式设附加属性
		Grid.SetRow(_handleLayer, 0);
		Grid.SetColumn(_handleLayer, 0);
		Grid.SetRowSpan(_handleLayer, 3);
		Grid.SetColumnSpan(_handleLayer, 1);
		grid.Children.Add(_handleLayer);
		// [v3] §1.A.4:右边缘中间的小把手(白底竖条,悬停 / 选中时出现;按下 = 拖右边缘改宽度)
		Border grip = new Border
		{
			Width = 2.0,
			CornerRadius = new CornerRadius(1.0),
			Margin = new Thickness(0.0, 10.0, 0.0, 10.0),
			HorizontalAlignment = HorizontalAlignment.Center,
			IsHitTestVisible = false
		};
		grip.Dyn(Border.BackgroundProperty, "B.InkMuted");
		// R35 lin-int(L12←L0):右 / 下边缘把手的外观读令牌 —— 底 B.EditBarBg、边 B.EditOutline、阴影 Fx.ResizeGrip、外伸 ResizeGripOutset;
		// 仍用 Border(换成 Layout.ResizeGrip 的 Thumb 要改整套按下 → BeginBoxResize 的事件链,不在集成里动)
		_pill = new Border
		{
			Width = LayoutVisualTokens.ResizeGripWidth,
			Height = LayoutVisualTokens.ResizeGripHeight,
			CornerRadius = new CornerRadius(8.0),
			BorderThickness = new Thickness(1.0),
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, -LayoutVisualTokens.ResizeGripOutset, 0.0),
			Cursor = new Cursor(StandardCursorType.SizeWestEast),
			Child = grip,
			IsVisible = false,
			BoxShadow = LayoutVisualTokens.ShadowOr(LayoutVisualTokens.ShadowKeys.ResizeGrip)
		};
		_pill.Dyn(Border.BackgroundProperty, LayoutEditChrome.KeyOr(LayoutVisualTokens.BrushKeys.EditBarBg, "B.Paper"));
		_pill.Dyn(Border.BorderBrushProperty, LayoutEditChrome.KeyOr(LayoutVisualTokens.BrushKeys.EditOutline, "B.Line"));
		ToolTip.SetTip(_pill, I18nCore.Tr("拖动改宽度"));
		_pill.AddHandler(InputElement.PointerPressedEvent, delegate(object? s, PointerPressedEventArgs e)
		{
			_host.BeginBoxResize(this, "e", e);
		}, RoutingStrategies.Tunnel);
		grid.Children.Add(_pill);
		Grid.SetRowSpan(_pill, 3);
		// [R35 lin-l12] Q3:下边缘把手(横放的同款小白条),按下 = 拖下边缘改高度;双击 = 恢复跟随内容高度
		Border gripS = new Border
		{
			Height = 2.0,
			CornerRadius = new CornerRadius(1.0),
			Margin = new Thickness(10.0, 0.0, 10.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center,
			IsHitTestVisible = false
		};
		gripS.Dyn(Border.BackgroundProperty, "B.InkMuted");
		_pillS = new Border
		{
			Width = LayoutVisualTokens.ResizeGripHeight,
			Height = LayoutVisualTokens.ResizeGripWidth,
			CornerRadius = new CornerRadius(8.0),
			BorderThickness = new Thickness(1.0),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Bottom,
			Margin = new Thickness(0.0, 0.0, 0.0, -LayoutVisualTokens.ResizeGripOutset),
			Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
			Child = gripS,
			IsVisible = false,
			BoxShadow = LayoutVisualTokens.ShadowOr(LayoutVisualTokens.ShadowKeys.ResizeGrip)
		};
		_pillS.Dyn(Border.BackgroundProperty, LayoutEditChrome.KeyOr(LayoutVisualTokens.BrushKeys.EditBarBg, "B.Paper"));
		_pillS.Dyn(Border.BorderBrushProperty, LayoutEditChrome.KeyOr(LayoutVisualTokens.BrushKeys.EditOutline, "B.Line"));
		// R35 lin-int:合并时补上提示文案(词条已入表)。双击行为与下边缘热区一致。
		ToolTip.SetTip(_pillS, I18nCore.Tr("拖动改高度 · 双击恢复自动高度"));
		_pillS.AddHandler(InputElement.PointerPressedEvent, delegate(object? s, PointerPressedEventArgs e)
		{
			if (e.ClickCount >= 2)
			{
				_host.ClearFixedHeight(this);
				e.Handled = true;
				return;
			}
			_host.BeginBoxResize(this, "s", e);
		}, RoutingStrategies.Tunnel);
		grid.Children.Add(_pillS);
		Grid.SetRowSpan(_pillS, 3);
		// 块名 / 操作按钮叠在最上层,跨三行(不参与 Auto 行的高度计算 → 出现 / 消失时内容不跳)
		grid.Children.Add(_bar);
		Grid.SetRowSpan(_bar, 3);
		Child = grid;
		_bar.IsVisible = false;
		this.PointerEntered += delegate
		{
			SetHover(true);
		};
		this.PointerExited += delegate
		{
			SetHover(false);
		};
		// 画布模式:块身按下 = 整体拖拽(用冒泡事件而不是 Tunnel —— 操作条/手柄自己的处理器先跑并置 Handled)
		this.PointerPressed += OnCardMouseDown;
		// 内容已挂在别的 ContentControl 上时先摘下来
		if (content is Control { Parent: ContentControl parent } && parent != _body)
		{
			parent.Content = null;
		}
		_body.Content = content;
	}

	/// <summary>切画布模式(grid / canvas;stack = 缺省,手柄层折叠)。</summary>
	public void SetBoxMode(string mode)
	{
		// R35 lin-int(L12←L3):canvas 只会以「面板推导出的网格」出现在编辑器里 —— 卡片一律按 grid 处理,
		// 8 向手柄(只给 canvas)因此不可达(GOAL §5.3「删不干净的留着不可达」)
		_boxMode = ((mode == LayoutSchema.ModeGrid || mode == LayoutSchema.ModeCanvas) ? LayoutSchema.ModeGrid : LayoutSchema.ModeStack);
		// [layout-fix] 参考件v2-对齐:窄宽自适应只在画布模式开(stack 的默认渲染一个像素都不经过它)
		// [layout-fix 第三批]「吃满视口」的内容(自带滚动条)不参与等比缩放:FitBox 会用无限高去量它的"自然尺寸",
		// 对话区一旦被判成"矮内容"就会被整体缩小
		_fit.Enabled = _boxMode != LayoutSchema.ModeStack && !LayoutFill.GetFillsViewport(_body.Content as Control);
		if (!_edit)
		{
			_handleLayer.IsVisible = false;
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
			_hover = false;
		}
		ApplyFold();
		_body.IsHitTestVisible = !edit;
		// [layout-fix] 规格 §3-问题7:编辑态整卡可拖 → 光标变十字移动箭头(隐藏 × 按钮自己会盖成手型)
		Cursor = (edit ? new Cursor(StandardCursorType.SizeAll) : null);
		UpdateHandles();
		ApplyLook();
	}

	/// <summary>[v3] 悬停 / 选中时才把块名、隐藏 / 删除按钮、右边缘把手亮出来(效果图:编辑控件只在需要时出现)。</summary>
	internal bool IsRaised
	{
		get
		{
			return _edit && (_hover || _selected);
		}
	}

	private void SetHover(bool hover)
	{
		if (_hover == hover)
		{
			return;
		}
		_hover = hover;
		if (_edit)
		{
			ApplyLook();
		}
	}

	/// <summary>[v3] 列表排法下宽度固定:藏起右边缘把手与右边热区。</summary>
	public void SetWidthLocked(bool locked)
	{
		if (_widthLocked == locked)
		{
			return;
		}
		_widthLocked = locked;
		UpdateHandles();
		UpdateChrome();
	}

	// 块名 / 操作按钮 / 右边缘把手的显隐,以及浮起层级(块名伸出卡片上沿 14px,要盖住上一行的块)
	private void UpdateChrome()
	{
		bool raised = IsRaised;
		_bar.IsVisible = raised;
		bool grid = string.Equals(_boxMode, LayoutSchema.ModeGrid, StringComparison.Ordinal);
		_pill.IsVisible = raised && !_widthLocked && !_folded && grid;
		// [R35] Q3:网格的右边缘 + 下边缘两个把手都只在悬停或选中时出现(右下角那个已去掉)
		_pillS.IsVisible = raised && !_folded && grid;
		if (ZIndex < 1000)
		{
			ZIndex = (raised ? 10 : 0);
		}
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
		_bodyWrap.IsVisible = !folded;
		if (_foldBtn != null)
		{
			// [Avalonia] Windows 用字形 \ue70d(展开)/ \ue70e(折叠);Linux 用 IconLib 的语义键
			_foldBtn.Content = IconLib.Make(folded ? "ChevronUp" : "ChevronDown", "B.Ink", 11);
			ToolTip.SetTip(_foldBtn, folded ? I18nCore.Tr("展开") : I18nCore.Tr("折叠(仅编辑时)"));
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
			// [Avalonia/移植差异 D-01] Windows 这里写的是 `_fit.Child = null;`(把**内容体**从 FitBox 上摘掉),
			// 于是卡片内容区变空;而且在 WPF 里 `_fixedScroll.Content = _fit` 会因为 _fit 仍是 _bodyWrap 的
			// 逻辑子元素而抛「元素已是另一元素的子元素」。本移植按这条代码的**意图**(内容进块内滚动)改成
			// 先摘 _bodyWrap 的子元素 —— 用户可感知行为 = 固定高度下内容仍在块内可滚(见报告差异表:行为差异 1/1)。
			_bodyWrap.Child = null;
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
			Cursor = StripCursor(east)
		};
		// [Avalonia] WPF 的 PreviewMouseLeftButtonDown → AddHandler(Tunnel)
		strip.AddHandler(InputElement.PointerPressedEvent, delegate(object? s, PointerPressedEventArgs e)
		{
			if (!east && e.ClickCount >= 2)
			{
				// 双击下边:恢复成「跟随内容高度」
				_host.ClearFixedHeight(this);
				e.Handled = true;
				return;
			}
			_host.BeginBoxResize(this, name, e);
		}, RoutingStrategies.Tunnel);
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

	// 编辑态蒙版:主题色 10 透明度平铺;跟着主题刷新
	public void RefreshTint()
	{
		ApplyLook();
	}

	// 手柄可见性:编辑态 + 画布模式才出现;grid 只给 e / s / se(canvas 才是 8 向)
	private void UpdateHandles()
	{
		bool box = _edit && _boxMode != LayoutSchema.ModeStack;
		_handleLayer.IsVisible = box;
		if (!box)
		{
			UpdateChrome();
			return;
		}
		foreach (KeyValuePair<string, Border> kv in _handles)
		{
			// [v3] §1.A.4 + §1.A.7:网格模式不再画 e / s / se 小方块(右边缘改宽度用 _pill + 整条右边热区);
			// 8 向手柄只剩自由画布在用,而 v3 起编辑态已不会出现 canvas(读取时折成网格)
			bool show = _boxMode == LayoutSchema.ModeCanvas;
			kv.Value.IsVisible = show;
		}
		// [layout-fix] 参考件v2-对齐:整条边的热区;折叠时下边不给拖(拖的是折叠后的假高度)
		foreach (KeyValuePair<string, Border> strip in _strips)
		{
			strip.Value.IsVisible = !((strip.Key == "s" && _folded) || (strip.Key == "e" && _widthLocked) || !_edit);
		}
		UpdateChrome();
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
		handle.Dyn(Border.BackgroundProperty, "B.Accent");
		handle.Dyn(Border.BorderBrushProperty, "B.Paper");
		string captured = name;
		handle.AddHandler(InputElement.PointerPressedEvent, delegate(object? s, PointerPressedEventArgs e)
		{
			_host.BeginBoxResize(this, captured, e);
		}, RoutingStrategies.Tunnel);
		_handles[name] = handle;
		_handleLayer.Children.Add(handle);
	}

	// [Avalonia] Windows 的 Cursors.SizeNWSE / SizeNESW / SizeNS / SizeWE;Avalonia 的
	// StandardCursorType 只认四个角名(TopLeftCorner = NW-SE 对角,TopRightCorner = NE-SW 对角)。
	private static Cursor HandleCursor(string name)
	{
		if (name == "nw" || name == "se")
		{
			return new Cursor(StandardCursorType.TopLeftCorner);
		}
		if (name == "ne" || name == "sw")
		{
			return new Cursor(StandardCursorType.TopRightCorner);
		}
		if (name == "n" || name == "s")
		{
			return new Cursor(StandardCursorType.SizeNorthSouth);
		}
		return new Cursor(StandardCursorType.SizeWestEast);
	}

	private static Cursor StripCursor(bool east)
	{
		return east ? new Cursor(StandardCursorType.SizeWestEast) : new Cursor(StandardCursorType.SizeNorthSouth);
	}

	private void OnCardMouseDown(object? sender, PointerPressedEventArgs e)
	{
		if (!_edit || _boxMode == LayoutSchema.ModeStack)
		{
			return;
		}
		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
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
		_titleRow.IsVisible = (_lookTitle && !_edit);
		// [v3] §1.A.1:编辑态不再给整块铺主题色描边 + 蒙版 —— 内容保持常态的样子(内容是主角),
		// 外面套一圈灰色虚线;只有选中的块换成主题色实线(见下方 ApplyEditOutline)
		if (card)
		{
			BorderThickness = new Thickness(1.0);
			CornerRadius = new CornerRadius(12.0);
			this.Dyn(Border.BorderBrushProperty, "B.Line");
			this.Dyn(Border.BackgroundProperty, "B.Paper");
		}
		else if (divider)
		{
			BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0);
			CornerRadius = new CornerRadius(0.0);
			this.Dyn(Border.BorderBrushProperty, "B.LineSoft");
			Background = null;
		}
		else
		{
			// plain = 现状(§4.B 明确:厚度 0、无底色)
			BorderThickness = new Thickness(0.0);
			CornerRadius = new CornerRadius(10.0);
			this.Dyn(Border.BorderBrushProperty, "B.Accent");
			Background = null;
		}
		_bodyWrap.Padding = BlockPad();
		ApplyEditOutline();
	}

	private void ApplyEditOutline()
	{
		if (!_edit)
		{
			_outline.IsVisible = false;
			UpdateChrome();
			return;
		}
		// 编辑态整卡可拖:没有底色的块给一个透明底,空白处也能接到指针
		if (Background == null)
		{
			Background = Brushes.Transparent;
		}
		_outline.IsVisible = true;
		// [R35 lin-l12] §5.1:常态 1.5px 灰色虚线(B.EditOutline)、悬停加深(B.EditOutlineHover)、选中 2px 主题色实线;
		// 启动页这类深色背景(pageId == "launch")用白色半透明(B.EditOutlineOnDark;悬停 .7、选中纯白,pages3 .win.edit.dark)。
		// 画刷直接按当前主题解析后赋值(不挂资源订阅:悬停时这里会被反复调用),主题切换走 RefreshTint → ApplyLook 重算。
		bool dark = _host.IsDarkSurface;
		if (_selected)
		{
			_outline.StrokeDashArray = null;
			_outline.StrokeThickness = 2.0;
			_outline.Stroke = dark ? new SolidColorBrush(Colors.White) : LayoutEditChrome.BrushOr("B.Accent", null, LayoutVisualTokens.AccentColor());
		}
		else
		{
			_outline.StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 3.0, 2.0 };
			_outline.StrokeThickness = 1.5;
			_outline.Stroke = OutlineBrush(dark, _hover);
		}
		UpdateChrome();
	}

	/// <summary>[R35] 未选中时的虚线颜色(L0 键 → 旧键 → 效果图数值)。</summary>
	internal static IBrush OutlineBrush(bool dark, bool hover)
	{
		if (dark)
		{
			return hover
				? new SolidColorBrush(Color.FromArgb(179, 255, 255, 255))
				: LayoutEditChrome.BrushOr("B.EditOutlineOnDark", null, Color.FromArgb(89, 255, 255, 255));
		}
		return hover
			? LayoutEditChrome.BrushOr("B.EditOutlineHover", "B.InkMuted", Color.FromArgb(87, 0, 0, 0))
			: LayoutEditChrome.BrushOr("B.EditOutline", "B.InkFaint", Color.FromArgb(41, 0, 0, 0));
	}

	/// <summary>[R35 lin-l12] 闪一下主题色光晕(0 0 0 4px glow → 透明,0.9s 缓出;效果图 .blk.flash)。</summary>
	public void Flash()
	{
		_flashTimer?.Stop();
		Color accent = LayoutVisualTokens.AccentColor();
		DateTime start = DateTime.UtcNow;
		const double durationMs = 900.0;
		void Paint(double t)
		{
			double k = 1.0 - Math.Min(1.0, Math.Max(0.0, t));
			k = k * k;
			byte alpha = (byte)Math.Round(71.0 * k);
			BoxShadow = (alpha == 0) ? default(BoxShadows) : new BoxShadows(new BoxShadow
			{
				Spread = 4.0 * k,
				Color = Color.FromArgb(alpha, accent.R, accent.G, accent.B)
			});
		}
		Paint(0.0);
		_flashTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(30.0), DispatcherPriority.Render, delegate
		{
			double t = (DateTime.UtcNow - start).TotalMilliseconds / durationMs;
			Paint(t);
			if (t >= 1.0)
			{
				_flashTimer?.Stop();
				_flashTimer = null;
			}
		});
		_flashTimer.Start();
	}

	/// <summary>[R35] 光晕动画是否还在播(测试用)。</summary>
	internal bool IsFlashing => _flashTimer != null;

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
