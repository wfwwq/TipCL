// 来源: Windows 版参考仓库 windos/src/TipCL/Pages/Layout/GridCanvasPanel.cs(L1 444 行;
//       L2@33b5fff 逐字节相同、WT-R31-MERGE@r32-r31-merge 也逐字节相同 —— 见 progress/R32-LV.md 的对照表)。
// 移植口径: 行为逐条对齐(行高下限 RowFallback=40、noMin 集合语义、每处 clamp 的上下限、两趟测量);
//           仅换 WPF → Avalonia 的机制(见下)。
// WPF → Avalonia 11 映射(本文件):
//   Panel.InternalChildren            → Children(Avalonia 的 Panel 没有 InternalChildren;本类自托管,见下)
//   UIElement                         → Control
//   protected override Size MeasureOverride(Size) / ArrangeOverride(Size)   → 同名同形
//   InvalidateMeasure/Arrange/Visual  → 同名(Avalonia 11 Layoutable/Visual)
//   protected override void OnRender(DrawingContext) → public override void Render(DrawingContext)
//   Brush.Freeze() / Pen.Freeze()     → 删(Avalonia 的笔刷/画笔没有 Freeze;不冻结也不变形)
//   base.ActualWidth/ActualHeight     → Bounds.Width/Bounds.Height
//   element is FrameworkElement fe → fe.Tag as string  → element is Control c → c.Tag as string
//   子元素解析(WPF 直判 LayoutCard.Spec.Id / LayoutCard.IsFolded) → ILayoutCanvasChild 接口缝(见下),
//   因为 Linux 树的 LayoutCard 在 LayoutEditor.cs(另一工作包),本文件不引用它。
//
// ⚠ 基类换掉的唯一一处硬差异(编译实测,见 progress/R32-LV.md §WPF 映射):
//   `Avalonia.Controls.Panel.Render(DrawingContext)` 是 **sealed**(CS0239)——Panel 派生类**无法**自绘,
//   而 `Visual.VisualChildren` / `StyledElement.LogicalChildren` 在 11.3.2 里也不是 virtual(CS0506)
//   ⇒ 没有"自己画 + 自己托管一堆子元素"的 Panel 写法。等价做法(本类的选择,语义逐条等价):
//   `GridCanvasPanel : Decorator`,Child = 一个**不做任何布局**的子元素宿主(ChildHost : Panel),
//   子元素集合由外部照旧通过 `Children` 添加/清空(类型仍是 Avalonia 的 `Controls`,与 Panel.Children 同);
//   测量/排布完全由本类决定(与源一致),自绘底纹走 `Render` —— Decorator 未封 Render,
//   且 Visual 的绘制顺序是"自己先画、子元素后画"⇒ 底纹仍在子元素之下,与源的 OnRender 同序。
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using TipCL.Core.Launcher.Layout;

namespace TipCL.App.Pages.Layout;

/// <summary>
/// 画布子元素 → 槽位信息的解析缝。WPF 版 <c>GridCanvasPanel</c> 直接判 <c>LayoutCard</c> 的
/// <c>Spec.Id</c> / <c>IsFolded</c>;Linux 树的 <c>LayoutCard</c> 住在 <c>LayoutEditor.cs</c>(另一工作包),
/// 本文件不引用它 ⇒ 改由这个接口解耦(等价语义,逐条见 <c>GridCanvasPanel.IdOf</c> / <c>FoldedOf</c>)。
/// <para>
/// <c>LayoutCard</c> 只要实现本接口(两个成员:块 id + 是否折叠)即可;没实现的子元素退回
/// 源文件本来就有的 Tag 兜底(块 id 取 <c>Tag as string</c>、折叠态取 false —— 测试宿主用的就是这条)。
/// </para>
/// </summary>
public interface ILayoutCanvasChild
{
	/// <summary>块的英文 id(对应布局文档里的 block id)。</summary>
	string? BlockId { get; }

	/// <summary>编辑态是否处于"折叠"(只占标题栏高度;排版据此不参与行高下限)。</summary>
	bool IsFolded { get; }
}

// grid / canvas 两种画布的自绘 Panel(§4.A 的对策:"Canvas vs 自绘 Panel" → 用自绘 Panel + 相对坐标,
// 虚拟高度由引擎算,不用 Canvas 控件)。
//
//   grid   模式:按网格单位的 (x,y,w,h) 排布;行高**两趟测量** —— 第一趟按列宽 + Infinite 高量出每块需求高,
//                 第二趟用 LayoutGrid.RowHeights 把"行高 = 该行最高块的需求高"算出来再 Arrange。
//                 row_h > 0 时直接用固定行高(内容超出按固定高裁,契约语义)。
//   canvas 模式:按归一化矩形排布;画布高取宿主给的高,拿不到有限高(滚动区里最常见)时用
//                 LayoutCanvas.RefHeight(600,对齐旧 DashHost 的兜底)。
//   R35 lin-l3(D-81 Q1 / GOAL-layout-v3 §5.3):canvas 退出界面。SetLayout 收到 mode=canvas 时按
//                 LayoutEngine.CanvasGridView 推导成网格(文档不动),ApplyTo 提交时才写成 grid;
//                 上面这条 canvas 排布路径因此不可达,代码留到合并后一起删。
//
// 三条纪律(与源一致):
//   1) 几何全部来自纯模型(LayoutGrid / LayoutCanvas),本文件不写任何坐标公式,只做 Measure/Arrange;
//   2) 子元素按 **块 id** 匹配槽位(不靠 Children 下标),因为 canvas 要按 z 序叠放,顺序与数据顺序不同;
//   3) 编辑态底纹画在 Render(在子元素之下),对齐旧 DashHost.RefreshGridLayer 的视觉;
//      **mode=stack 时这个 Panel 根本不进可视树** —— 默认渲染一个像素都不经过这里。
public sealed class GridCanvasPanel : Decorator
{
	// 子元素宿主:块卡片挂在这里(Panel ⇒ 可视树 / 命中测试 / z 序全走 Avalonia 标准路径),
	// 但**测量与排布完全由 GridCanvasPanel 显式决定**,所以这个宿主自己两件事都不做 ——
	// 否则 Panel 的默认行为(量 available、把每个孩子拉满整块面板)会覆盖掉画布算出来的矩形。
	private sealed class ChildHost : Panel
	{
		protected override Size MeasureOverride(Size available)
		{
			return default(Size);
		}

		protected override Size ArrangeOverride(Size final)
		{
			return final;
		}
	}

	private readonly ChildHost _host = new ChildHost();

	/// <summary>画布子元素集合(等价于源的 <c>Panel.InternalChildren</c> 的可写面)。
	/// 宿主(编辑器)照旧 <c>Children.Clear()</c> / <c>Children.Add(card)</c> 即可(类型与 Panel.Children 同)。</summary>
	public Avalonia.Controls.Controls Children
	{
		get
		{
			return _host.Children;
		}
	}

	private List<LayoutCell> _cells = new List<LayoutCell>();

	/// <summary>构造:把子元素宿主挂成唯一孩子(见 <see cref="ChildHost"/> 的说明)。</summary>
	public GridCanvasPanel()
	{
		Child = _host;
	}

	private List<LayoutRect> _rects = new List<LayoutRect>();

	private double[] _rowHeights = new double[0];

	private double _columnWidth = 1.0;

	// [audit/B] 上一趟测量用的宽度基准(Arrange 宽不同 ⇒ 行高要按新宽重量),
	// 以及"已经为这些宽度补过一次 InvalidateMeasure"的防抖集合(避免两趟布局互相触发、来回抖)
	private double _measuredWidth;

	private readonly HashSet<double> _remeasureWidths = new HashSet<double>();

	// 拖动期间列宽没变就不再按无限高度重测每张卡。按下时记下已经量过的高度，落点变了只重算行高。
	private bool _holdHeights;

	private readonly Dictionary<string, (double W, double H, double Desired)> _held = new Dictionary<string, (double W, double H, double Desired)>(StringComparer.Ordinal);

	private Color _penAccent;

	private Pen? _gridPen;

	private SolidColorBrush? _placeFill;

	private Pen? _placePen;

	private int _rows;

	/// <summary>页面模式(grid | canvas;stack 时本 Panel 不被使用)。</summary>
	public string Mode { get; private set; } = LayoutSchema.ModeStack;

	/// <summary>
	/// R35 lin-l3(D-81 Q1):当前槽位是不是从 canvas 页推导出来的网格。true 时 <see cref="Mode"/> 是 grid、
	/// 文档里该页仍是 canvas(原样不动);<see cref="ApplyTo"/> 提交时才把整页写成 grid。
	/// </summary>
	public bool DerivedFromCanvas { get; private set; }

	/// <summary>grid 列数。</summary>
	public int Cols { get; private set; } = LayoutSchema.ColsDefault;

	/// <summary>固定行高(0 = 自适应行)。</summary>
	public double RowH { get; private set; }

	/// <summary>块间距。</summary>
	public double Gap { get; private set; } = LayoutSchema.GapDefault;

	/// <summary>canvas 画布参考高(宿主给不出有限高时用;对齐旧 DashHost 的 600 兜底)。</summary>
	public double CanvasHeight { get; set; } = LayoutCanvas.RefHeight;

	/// <summary>编辑态栅格底纹的吸附档(px;0 = 不画底纹)。</summary>
	public double SnapStep { get; set; } = LayoutCanvas.DefaultStep;

	/// <summary>是否在编辑态(只有编辑态画底纹)。</summary>
	public bool EditMode { get; set; }

	/// <summary>
	/// [layout-fix 第三批]「吃满视口」的块 id(null = 没有)。grid 自动行高、可用高有限(宿主关掉了外层纵向滚动)、
	/// 该块没折叠也没拖过固定像素高时,它按 <see cref="LayoutGrid.FillHeight"/> 拿视口剩下的高度并按它测量 ——
	/// 自带滚动条的内容(AI 对话区)因此保住自身滚动与吸底。
	/// </summary>
	public string? FillId { get; set; }

	/// <summary>[layout-fix 第三批] 吃满视口块的最小高度(视口太矮时宁可整体被裁,也不把对话区压没)。</summary>
	public const double MinFillHeight = 120.0;

	// ————— [layout-fix 第三批·任务 B] 丝滑拖动:浮起 / 占位框 / FLIP / 落位 —————
	//
	// 浮起(floating):手势中的块不再按槽位排,而是排在宿主给的**像素矩形**上(拖动 = 起手时的矩形,
	//   再由宿主改 RenderTransform 平移跟手;改大小 = 跟手的像素矩形)。它的槽位照常参与让位与行高,
	//   槽位算出来的矩形就是**落点占位框**(半透明 + 主题色描边,画在所有卡片之下)。
	// FLIP:宿主在"槽位要变"之前 ArmFlip();下一次排布里位置变了的块先用平移放回原来看起来的位置,再补间收回 0。
	// 落位(landing):改大小松手后,卡片从像素矩形补间到吸附后的槽位矩形(位置 + 尺寸一起插值)。
	private readonly Dictionary<string, LayoutRect> _floating = new Dictionary<string, LayoutRect>(StringComparer.Ordinal);

	private readonly Dictionary<string, (LayoutRect From, double P)> _landing = new Dictionary<string, (LayoutRect From, double P)>(StringComparer.Ordinal);

	private readonly Dictionary<string, Rect> _lastArranged = new Dictionary<string, Rect>(StringComparer.Ordinal);

	private bool _flipArmed;

	/// <summary>是否画落点占位框(拖动 / 改大小期间为 true)。</summary>
	public bool ShowPlaceholder { get; set; }

	/// <summary>当前浮起的块(id → 像素矩形)。</summary>
	public IReadOnlyDictionary<string, LayoutRect> Floating
	{
		get
		{
			return _floating;
		}
	}

	/// <summary>让某块浮起在像素矩形上(只触发重排,不重新测量)。</summary>
	public void SetFloating(string id, LayoutRect rect)
	{
		if (string.IsNullOrEmpty(id))
		{
			return;
		}
		_floating[id] = rect;
		InvalidateArrange();
	}

	/// <summary>收起全部浮起(不做动画;落位动画由宿主另行 ArmFlip / BeginLanding)。</summary>
	public void ClearFloating()
	{
		if (_floating.Count == 0)
		{
			return;
		}
		_floating.Clear();
		InvalidateArrange();
		InvalidateVisual();
	}

	private double _flipMs = LayoutMotion.FlipMs;

	/// <summary>下一次排布里位置变了的块做 FLIP 让位动画(一次性;<paramref name="durationMs"/> 缺省 160ms)。</summary>
	public void ArmFlip(double durationMs = LayoutMotion.FlipMs)
	{
		_flipArmed = true;
		_flipMs = durationMs;
	}

	/// <summary>这一拍的吸附动画不要盖住正在跟手的平移。</summary>
	public void CancelFlipArm()
	{
		_flipArmed = false;
	}

	/// <summary>改大小松手:该块从像素矩形 <paramref name="from"/> 补间到它的槽位矩形(位置 + 尺寸)。</summary>
	public void BeginLanding(string id, LayoutRect from)
	{
		Control? child = ChildOf(id);
		if (child == null)
		{
			return;
		}
		_landing[id] = (from, 0.0);
		LayoutTween.Start(this, ("land", id), LayoutMotion.DropMs, delegate(double p)
		{
			if (_landing.ContainsKey(id))
			{
				_landing[id] = (from, p);
				InvalidateArrange();
			}
		}, delegate
		{
			_landing.Remove(id);
			InvalidateArrange();
		});
	}

	/// <summary>把所有在途动画推到终点(新手势开始 / 重置 / 退出编辑时调,保证命中测试与视觉一致)。</summary>
	public void FinishAnimations()
	{
		foreach (Control child in Children)
		{
			LayoutTween.Cancel(child, false);
			LayoutMotion.SetOffset(child, 0.0, 0.0);
		}
		foreach (string id in new List<string>(_landing.Keys))
		{
			LayoutTween.Cancel(("land", id), false);
		}
		_landing.Clear();
		_flipArmed = false;
		InvalidateArrange();
	}

	/// <summary>离开可视树(切页 / 关窗)时把在途动画推到终点:不留半截平移,也不让补间驱动继续引用这些控件。</summary>
	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		FinishAnimations();
		base.OnDetachedFromVisualTree(e);
	}

	private Control? ChildOf(string id)
	{
		foreach (Control child in Children)
		{
			if (string.Equals(IdOf(child), id, StringComparison.Ordinal))
			{
				return child;
			}
		}
		return null;
	}

	// FLIP:从"看起来的位置"(上次排布 + 残留平移)补间回新位置;同一块上的旧动画先取消(Start 内部)
	private void StartFlip(Control child, Vector from)
	{
		LayoutMotion.SetOffset(child, from.X, from.Y);
		LayoutTween.Start(this, child, _flipMs, delegate(double p)
		{
			Vector left = LayoutMotion.FlipRemaining(from, p);
			LayoutMotion.SetOffset(child, left.X, left.Y);
		});
	}

	// ————— 排布结果(宿主与测试只读) —————

	/// <summary>可见块槽位(与页面里的可见顺序一致;canvas 已按 z 序叠放交付)。</summary>
	public IReadOnlyList<LayoutCell> Cells
	{
		get
		{
			return _cells;
		}
	}

	/// <summary>最近一次排布的像素矩形(与 <see cref="Cells"/> 一一对应)。</summary>
	public IReadOnlyList<LayoutRect> RectValues
	{
		get
		{
			return _rects;
		}
	}

	/// <summary>grid 行数。</summary>
	public int Rows
	{
		get
		{
			return _rows;
		}
	}

	/// <summary>最近一次使用的列宽(px)。</summary>
	public double ColumnWidthValue
	{
		get
		{
			return _columnWidth;
		}
	}

	/// <summary>最近一次算出的行高表(px;宿主算对齐辅助线时要用它把槽位换算成像素矩形)。</summary>
	public double[] RowHeightValues
	{
		get
		{
			return _rowHeights;
		}
	}

	/// <summary>是否 canvas 模式。R35 起恒为 false(canvas 页按网格推导,见 <see cref="DerivedFromCanvas"/>);保留是因为 LayoutEditor 与测试还在读它,合并后再删。</summary>
	public bool IsCanvas
	{
		get
		{
			return string.Equals(Mode, LayoutSchema.ModeCanvas, StringComparison.Ordinal);
		}
	}

	/// <summary>
	/// 按页面 v2 条目重建槽位(改上下文/换页时调)。
	/// R35 lin-l3(GOAL-layout-v3 §5.3):mode=canvas 不再按归一化矩形排,而是用
	/// <see cref="LayoutEngine.CanvasGridView"/> 推导成网格(先 y 后 x、span = clamp(round(w×cols), MinCols, cols));
	/// 槽位来自副本,<paramref name="page"/> 本身一个字节都不改。于是本 Panel 的 Mode 只会是 grid / stack,
	/// 下面所有 IsCanvas 分支都不可达(留着等合并后一起删)。
	/// </summary>
	public void SetLayout(string mode, int cols, double rowH, double gap, PageLayoutV2? page)
	{
		DerivedFromCanvas = string.Equals(mode, LayoutSchema.ModeCanvas, StringComparison.Ordinal);
		Mode = ((mode == LayoutSchema.ModeGrid || DerivedFromCanvas) ? LayoutSchema.ModeGrid : LayoutSchema.ModeStack);
		PageLayoutV2? derived = (DerivedFromCanvas ? LayoutEngine.CanvasGridView(page) : null);
		// 推导出来的网格用推导后的列数(SnapCols(原 cols));row_h 在 canvas 下不生效 ⇒ 推导后一律自适应行
		Cols = Math.Max(1, (derived != null) ? derived.Cols : cols);
		RowH = (DerivedFromCanvas ? 0.0 : rowH);
		Gap = ((gap >= 0.0) ? gap : 0.0);
		_cells = LayoutGrid.Pack(DerivedFromCanvas ? derived : page);
		_rows = LayoutGrid.RowCount(_cells);
		// 换了页 / 换了列数:上一套宽度下的"已补过失效"记录不再成立
		_remeasureWidths.Clear();
		InvalidateMeasure();
		InvalidateVisual();
	}

	/// <summary>[任务 B] Reflow 被调用的次数(测试用:落点没变的指针移动不该触发重新排布)。</summary>
	internal int ReflowCount { get; private set; }

	/// <summary>槽位被就地改过(拖拽/缩放)之后重新排布。</summary>
	public void Reflow()
	{
		ReflowCount++;
		_rows = LayoutGrid.RowCount(_cells);
		InvalidateMeasure();
		InvalidateArrange();
		InvalidateVisual();
	}

	/// <summary>
	/// [layout-fix] 规格2-问题2.2:拖动 / 缩放过程中的**实时让位** —— 把 <paramref name="pinned"/> 里的块
	/// (正在拖的那几个)钉在当前位置,其余块按重力上浮重新落格(判定在纯模型 <see cref="LayoutGrid.Compact"/>)。
	/// <paramref name="swapX"/> = 每块"备选列"(被拖块原来的列)⇒ 允许左右互换;canvas 模式不参与。
	/// 只在 grid 模式且编辑态被调用。
	/// </summary>
	public void CompactCells(ICollection<string>? pinned, IReadOnlyDictionary<string, int>? swapX)
	{
		CompactCells(pinned, swapX, gravity: true);
	}

	/// <summary>同上;<paramref name="gravity"/> = false = 「自动靠拢」关:只解冲突,不上浮、不左右互换。</summary>
	public void CompactCells(ICollection<string>? pinned, IReadOnlyDictionary<string, int>? swapX, bool gravity)
	{
		if (IsCanvas || _cells.Count == 0)
		{
			return;
		}
		LayoutGrid.Compact(_cells, Cols, pinned, (gravity ? swapX : null), gravity);
		Reflow();
	}

	/// <summary>取槽位;不存在返回 null。</summary>
	public LayoutCell? CellOf(string? id)
	{
		if (!string.IsNullOrEmpty(id))
		{
			foreach (LayoutCell cell in _cells)
			{
				if (string.Equals(cell.Id, id, StringComparison.Ordinal))
				{
					return cell;
				}
			}
		}
		return null;
	}

	/// <summary>取槽位下标;不存在返回 -1。</summary>
	public int IndexOf(string? id)
	{
		if (!string.IsNullOrEmpty(id))
		{
			for (int i = 0; i < _cells.Count; i++)
			{
				if (string.Equals(_cells[i].Id, id, StringComparison.Ordinal))
				{
					return i;
				}
			}
		}
		return -1;
	}

	/// <summary>取某块的像素矩形(排布结果);不存在返回 null。</summary>
	public LayoutRect? RectOf(string? id)
	{
		int index = IndexOf(id);
		if (index < 0 || index >= _rects.Count)
		{
			return null;
		}
		return _rects[index];
	}

	/// <summary>除某块之外的其它块矩形(对齐辅助线用)。</summary>
	public List<LayoutRect> OtherRects(string? excludedId)
	{
		List<LayoutRect> list = new List<LayoutRect>();
		for (int i = 0; i < _cells.Count && i < _rects.Count; i++)
		{
			if (!string.Equals(_cells[i].Id, excludedId, StringComparison.Ordinal))
			{
				list.Add(_rects[i]);
			}
		}
		return list;
	}

	/// <summary>按点命中:返回最上层(z 最大)的块 id;没命中返回 null。</summary>
	public string? HitId(Point point)
	{
		int index = LayoutGrid.HitIndex(_cells, _rects, point.X, point.Y);
		return ((index >= 0) ? _cells[index].Id : null);
	}

	/// <summary>矩形与一组块是否相交(框选:任一矩形被框到即选中;点选时只有一个矩形)。</summary>
	public List<string> IdsIn(LayoutRect marquee)
	{
		List<string> list = new List<string>();
		for (int i = 0; i < _cells.Count && i < _rects.Count; i++)
		{
			LayoutRect rect = _rects[i];
			if (rect.X < marquee.Right && rect.Right > marquee.X && rect.Y < marquee.Bottom && rect.Bottom > marquee.Y)
			{
				list.Add(_cells[i].Id);
			}
		}
		return list;
	}

	/// <summary>
	/// 把槽位写回页面块(拖拽/缩放提交时调)。
	/// R35 lin-l3:槽位是从 canvas 页推导来的(或传进来的页仍是 canvas)时,先用 <see cref="LayoutEngine.CanvasToGrid"/>
	/// 把整页(含隐藏块、自建块、插件块、白名单外的块)就地写成 grid,再用当前槽位覆盖可见块的几何 ——
	/// 这就是 Q1 的「编辑器里提交一次之后才写成 grid」。不是 canvas 页时与改动前完全一致。
	/// </summary>
	public void ApplyTo(PageLayoutV2? page)
	{
		if (DerivedFromCanvas || LayoutCanvasMigration.IsCanvas(page))
		{
			LayoutEngine.CanvasToGrid(page);
			LayoutGrid.Apply(page, _cells);
		}
		else
		{
			LayoutGrid.Apply(page, _cells);
		}
	}

	// ————— 两趟测量 / 排布 —————

	/// <summary>
	/// 第一趟:grid 用 (列宽 × Infinite) 量出每块需求高 → 算行高;canvas 直接按归一化矩形量。
	/// 返回值的高 = 内容总高(滚动区据此给出滚动条)。
	/// </summary>
	// [layout-fix] 参考件v2-对齐:自动行高的行高下限从 120 降到 40(折叠块 / 固定高度块要能压到很矮)
	private const double RowFallback = 40.0;

	/// <summary>拖动期间复用已经量过的卡片高度。列宽或尺寸变了才重测那一张。</summary>
	public void HoldHeights(bool hold)
	{
		_holdHeights = hold;
		if (!hold)
		{
			_held.Clear();
			return;
		}
		if (_held.Count > 0)
		{
			return;
		}
		foreach (Control child in Children)
		{
			string id = IdOf(child);
			if (id.Length == 0)
			{
				continue;
			}
			double width;
			double height;
			if (IsCanvas)
			{
				width = Math.Max(1.0, child.Bounds.Width);
				height = Math.Max(1.0, child.Bounds.Height);
			}
			else
			{
				LayoutCell? cell = CellOf(id);
				width = ((cell == null) ? Math.Max(1.0, child.Bounds.Width) : (_columnWidth * cell.W + Gap * Math.Max(0, cell.W - 1)));
				height = double.PositiveInfinity;
			}
			double desired = child.DesiredSize.Height;
			if (desired < 1.0)
			{
				continue;
			}
			_held[id] = (width, height, desired);
		}
	}

	private static bool SameAxis(double a, double b)
	{
		if (double.IsInfinity(a) && double.IsInfinity(b))
		{
			return true;
		}
		return Math.Abs(a - b) < 0.5;
	}

	private bool TryHeld(string id, double width, double height, out double desired)
	{
		desired = 0.0;
		if (!_holdHeights || id.Length == 0 || !_held.TryGetValue(id, out (double W, double H, double Desired) hit))
		{
			return false;
		}
		if (!SameAxis(hit.W, width) || !SameAxis(hit.H, height))
		{
			return false;
		}
		if (hit.Desired < 1.0)
		{
			return false;
		}
		desired = hit.Desired;
		return true;
	}

	private void Remember(string id, double width, double height, double desired)
	{
		if (_holdHeights && id.Length > 0)
		{
			_held[id] = (width, height, desired);
		}
	}

	private void MeasureHeld(Control child, string id, double width, double height)
	{
		if (TryHeld(id, width, height, out _))
		{
			return;
		}
		child.Measure(new Size(Math.Max(0.0, width), height));
		Remember(id, width, height, child.DesiredSize.Height);
	}

	private void EnsurePens(Color accent)
	{
		if (_gridPen != null && _penAccent == accent)
		{
			return;
		}
		_penAccent = accent;
		_gridPen = new Pen(new SolidColorBrush(Color.FromArgb(38, accent.R, accent.G, accent.B)), 0.6);
		_placeFill = new SolidColorBrush(Color.FromArgb(28, accent.R, accent.G, accent.B));
		_placePen = new Pen(new SolidColorBrush(Color.FromArgb(200, accent.R, accent.G, accent.B)), 1.5)
		{
			DashStyle = new DashStyle(new double[2] { 4.0, 3.0 }, 0.0)
		};
	}

	protected override Size MeasureOverride(Size available)
	{
		double width = ((double.IsInfinity(available.Width) || available.Width <= 0.0) ? 900.0 : available.Width);
		// 宿主自己不做任何测量(ChildHost),这里量它只是为了让它在 Arrange 前有有效的测量状态
		_host.Measure(available);
		if (IsCanvas)
		{
			double height = ResolveCanvasHeight(available.Height);
			RebuildCanvasRects(width, height);
			foreach (Control child in Children)
			{
				string id = IdOf(child);
				LayoutRect? rect = RectOf(id);
				if (rect.HasValue)
				{
					MeasureHeld(child, id, rect.Value.W, rect.Value.H);
				}
				else
				{
					MeasureHeld(child, id, width, LayoutCanvas.RefHeight);
				}
			}
			return new Size(((double.IsInfinity(available.Width) || available.Width <= 0.0) ? width : available.Width), height);
		}
		double gridTotal = MeasureGridChildren(width, available.Height);
		return new Size(((double.IsInfinity(available.Width) || available.Width <= 0.0) ? width : available.Width), gridTotal);
	}

	/// <summary>
	/// grid 的第一趟(按给定宽度):量每块需求高 → 算列宽 / 行高 / 矩形,返回内容总高。
	/// [audit/B] 抽成方法的原因与 Windows 参考件(<c>GridCanvasPanel.MeasureGridChildren</c>)一致:
	/// Arrange 拿到的宽度可能与测量时不同 —— 纵向滚动条一出现就吃掉 10px 可用宽,父级也可能按别的宽 Arrange。
	/// 那时行高必须按**新宽度**重新量,否则自动行高还是按旧的(更宽)列宽算的,内容被裁在矮行里
	/// (用户体感 = 滚动条一冒出来,卡片内容就被切一刀)。
	/// </summary>
	private double MeasureGridChildren(double width, double availableHeight)
	{
		_measuredWidth = width;
		_columnWidth = LayoutGrid.ColumnWidth(width, Cols, Gap);
		Dictionary<string, double> desired = new Dictionary<string, double>(StringComparer.Ordinal);
		HashSet<string> noMin = new HashSet<string>(StringComparer.Ordinal);
		// [layout-fix 第三批] 吃满视口块:先跳过,等其它块量完、行高算出来再按剩余高度量它
		bool fillOpen = RowH <= 0.0 && !string.IsNullOrEmpty(FillId) && !double.IsInfinity(availableHeight) && availableHeight > 0.0;
		Control? fillChild = null;
		LayoutCell? fillCell = null;
		double fillWidth = 0.0;
		foreach (Control child in Children)
		{
			string id = IdOf(child);
			LayoutCell? cell = CellOf(id);
			if (cell == null)
			{
				child.Measure(new Size(_columnWidth, LayoutCanvas.FallbackMinH));
				continue;
			}
			double cellWidth = _columnWidth * cell.W + Gap * Math.Max(0, cell.W - 1);
			if (fillOpen && fillChild == null && string.Equals(id, FillId, StringComparison.Ordinal) && !FoldedOf(child) && cell.HeightPx <= 0.0)
			{
				fillChild = child;
				fillCell = cell;
				fillWidth = cellWidth;
				desired[id] = 0.0;
				noMin.Add(cell.Id);
				continue;
			}
			// [layout-fix] 参考件v2-对齐:编辑态折叠的区块只占标题栏高度;拖出了固定高度(HeightPx)的区块
			// 按固定高度量(内容改在块内滚动),这两种都不参与行高下限(MinH),否则行高会被内容顶回去。
			bool folded = FoldedOf(child);
			if (!folded && RowH <= 0.0 && cell.HeightPx > 0.0)
			{
				child.Measure(new Size(Math.Max(1.0, cellWidth), cell.HeightPx));
				desired[id] = cell.HeightPx;
				noMin.Add(cell.Id);
				continue;
			}
			double measureWidth = Math.Max(1.0, cellWidth);
			if (TryHeld(id, measureWidth, double.PositiveInfinity, out double held))
			{
				desired[id] = held;
			}
			else
			{
				child.Measure(new Size(measureWidth, double.PositiveInfinity));
				desired[id] = child.DesiredSize.Height;
				Remember(id, measureWidth, double.PositiveInfinity, desired[id]);
			}
			if (folded)
			{
				noMin.Add(cell.Id);
			}
		}
		_rows = LayoutGrid.RowCount(_cells);
		List<LayoutCell> rowCells = _cells;
		if (noMin.Count > 0)
		{
			rowCells = new List<LayoutCell>(_cells.Count);
			foreach (LayoutCell cell2 in _cells)
			{
				if (cell2 != null && noMin.Contains(cell2.Id))
				{
					LayoutCell copy = cell2.Clone();
					copy.MinH = 0.0;
					rowCells.Add(copy);
				}
				else
				{
					// 与源一致:非 noMin 项原样放进行高计算的输入表(源里这里也可能是 null,
					// LayoutGrid.RowHeights 会跳过 null ⇒ 用 null-forgiving 保持逐字语义,不改变行为)
					rowCells.Add(cell2!);
				}
			}
		}
		_rowHeights = LayoutGrid.RowHeights(rowCells, _rows, RowH, desired, RowFallback);
		if (fillChild != null && fillCell != null)
		{
			// 不计填充块时的行高 → 它该拿的高度 → 按这个有限高度量它(它自带的滚动条因此有界)→ 重算行高
			double[] without = LayoutGrid.RowHeights(WithoutCell(rowCells, fillCell.Id), _rows, RowH, desired, RowFallback);
			double fillHeight = LayoutGrid.FillHeight(without, fillCell.Y, fillCell.H, Gap, availableHeight, MinFillHeight);
			fillChild.Measure(new Size(Math.Max(1.0, fillWidth), fillHeight));
			desired[fillCell.Id] = fillHeight;
			_rowHeights = LayoutGrid.RowHeights(rowCells, _rows, RowH, desired, RowFallback);
		}
		RebuildGridRects();
		return RowTotalHeight();
	}

	/// <summary>行高合计(含行间 gap)。</summary>
	private double RowTotalHeight()
	{
		double sum = 0.0;
		for (int i = 0; i < _rowHeights.Length; i++)
		{
			sum += _rowHeights[i] + ((i < _rowHeights.Length - 1) ? Gap : 0.0);
		}
		return sum;
	}

	// 整表排布结果:复用同一个 List(落点每变一次就重排,拖动期间不再每帧新分配一个 List)
	private void RebuildGridRects()
	{
		_rects.Clear();
		for (int i = 0; i < _cells.Count; i++)
		{
			_rects.Add(LayoutGrid.Place(_cells[i], _columnWidth, _rowHeights, Gap));
		}
	}

	private void RebuildCanvasRects(double width, double height)
	{
		_rects.Clear();
		for (int i = 0; i < _cells.Count; i++)
		{
			_rects.Add(LayoutCanvas.Place(_cells[i], width, height));
		}
	}

	/// <summary>
	/// 与测量趟同一套高度约束(否则测量与排布两趟约束不同,这块每次布局都要重量):
	/// canvas 用真实像素高;grid 里拖过固定像素高的块用 HeightPx;吃满视口的块用矩形高;其余给无限高(跟随内容)。
	/// 关键作用:落位动画期间 <paramref name="rectHeight"/> 是插值中的中间值,若拿它去 Measure,
	/// 每帧都会按新高度重排一次内容;这里换成稳定的约束 ⇒ 尺寸没变时 Measure 走缓存(空操作)。
	/// </summary>
	private double MeasureHeightFor(string id, double rectHeight)
	{
		if (IsCanvas)
		{
			return rectHeight;
		}
		if (RowH <= 0.0 && !string.IsNullOrEmpty(FillId) && string.Equals(id, FillId, StringComparison.Ordinal))
		{
			return rectHeight;
		}
		LayoutCell? cell = CellOf(id);
		if (RowH <= 0.0 && cell != null && cell.HeightPx > 0.0)
		{
			return cell.HeightPx;
		}
		return double.PositiveInfinity;
	}

	/// <summary>
	/// 第二趟:按最终宽度重算列宽(滚动条出现会改变可用宽),再把每块 Arrange 到自己的矩形上。
	/// 行高沿用第一趟量出来的值(块高不定时行高必须来自需求测量,不能靠 Arrange 反推)。
	/// </summary>
	protected override Size ArrangeOverride(Size final)
	{
		double width = Math.Max(1.0, final.Width);
		// 宿主铺满面板(它自己不排布孩子),随后每张卡拉到画布算出来的矩形上
		_host.Arrange(new Rect(0.0, 0.0, Math.Max(0.0, final.Width), Math.Max(0.0, final.Height)));
		if (IsCanvas)
		{
			double height = ResolveCanvasHeight(final.Height);
			RebuildCanvasRects(width, height);
		}
		else if (Math.Abs(width - _measuredWidth) > 0.5)
		{
			// [audit/B] Arrange 宽 ≠ 测量宽(滚动条出现 / 父级换了宽度):行高按新宽重量一遍。
			// 只在"更窄"且内容总高确实变了时补一次 InvalidateMeasure(更宽时内容只会变矮,不存在裁切;
			// 同一宽度只补一次,避免两趟布局之间来回抖动 —— 与 Windows 参考件的 _remeasureWidths 守卫同口径)。
			double before = RowTotalHeight();
			double previousWidth = _measuredWidth;
			double total = MeasureGridChildren(width, final.Height);
			if (Math.Abs(total - before) > 0.5 && width < previousWidth && _remeasureWidths.Add(Math.Round(width, 1)))
			{
				InvalidateMeasure();
			}
		}
		else
		{
			_columnWidth = LayoutGrid.ColumnWidth(width, Cols, Gap);
			RebuildGridRects();
		}
		bool flip = _flipArmed;
		_flipArmed = false;
		foreach (Control child in Children)
		{
			string id = IdOf(child);
			LayoutRect? rect = RectOf(id);
			if (!rect.HasValue)
			{
				child.Arrange(new Rect(0.0, 0.0, 0.0, 0.0));
				continue;
			}
			LayoutRect value = rect.Value;
			Rect target;
			bool freeSize = false;
			if (_floating.TryGetValue(id, out LayoutRect floatRect))
			{
				// [任务 B] 手势中的块:排在像素矩形上(跟手),不参与 FLIP
				target = new Rect(floatRect.X, floatRect.Y, Math.Max(0.0, floatRect.W), Math.Max(0.0, floatRect.H));
				freeSize = true;
			}
			else if (_landing.TryGetValue(id, out (LayoutRect From, double P) land))
			{
				// [任务 B] 改大小松手后的落位:像素矩形 → 槽位矩形,ease-out 插值
				LayoutRect r = LayoutMotion.LerpRect(land.From, value, LayoutMotion.EaseOutCubic(land.P));
				target = new Rect(r.X, r.Y, Math.Max(0.0, r.W), Math.Max(0.0, r.H));
				freeSize = true;
			}
			else
			{
				target = new Rect(value.X, value.Y, value.W, value.H);
				if (flip && _lastArranged.TryGetValue(id, out Rect prev))
				{
					Vector now = LayoutMotion.GetOffset(child);
					Vector from = LayoutMotion.FlipOffset(prev.X + now.X, prev.Y + now.Y, target.X, target.Y);
					if (from != default(Vector))
					{
						StartFlip(child, from);
					}
					else if (now != default(Vector))
					{
						// 起终点几乎重合(差 <0.5px)时不做动画,但手里残留的平移必须清掉,否则卡片会停在"槽位 + 拖动位移"处
						LayoutTween.Cancel(child, false);
						LayoutMotion.SetOffset(child, 0.0, 0.0);
					}
				}
			}
			// [audit/B] 浮起的块(拖动跟手 / 缩放预览 / 落位动画)与 canvas 的块:矩形尺寸变了就按新尺寸补量一次,
			// 否则内容(文本折行 / 卡内滚动区)还按旧宽度排着 —— 用户看到的正是"拉右边时卡片框变宽了、字却挤在
			// 老宽度里,松手才跳成新版式"。尺寸没变时 Measure 走缓存(空操作),动画期间不会让整张画布重量。
			if (freeSize || IsCanvas)
			{
				MeasureHeld(child, id, Math.Max(0.0, target.Width), MeasureHeightFor(id, target.Height));
			}
			child.Arrange(target);
			_lastArranged[id] = target;
			// 浮起的块盖在其它块上面
			// [v3] 编辑态悬停 / 选中的块也略微抬高:它的块名浮在卡片上沿外 14px,要盖住上一行的块
			child.ZIndex = (_floating.ContainsKey(id) ? 1000 : ((child is LayoutCard raised && raised.IsRaised) ? 10 : 0));
		}
		return final;
	}

	/// <summary>
	/// 编辑态底纹(在子元素之下):
	///   grid   画列分隔线 + 每行边界(为了让"块占了几行几列"看得见);
	///   canvas 画吸附档的方格(对齐旧 DashHost.<c>RefreshGridLayer</c> 的 DrawingBrush 视觉)。
	/// 非编辑态或吸附档为 0 时什么都不画。
	/// </summary>
	public override void Render(DrawingContext context)
	{
		base.Render(context);
		if (!EditMode || Children.Count == 0)
		{
			return;
		}
		Color accent = LayoutVisualTokens.AccentColor();
		// WPF 版在这里 brush.Freeze() / pen.Freeze();Avalonia 的笔刷与画笔没有 Freeze(不冻结也不变形)
		EnsurePens(accent);
		Pen pen = _gridPen!;
		double width = Math.Max(0.0, Bounds.Width);
		double height = Math.Max(0.0, Bounds.Height);
		if (IsCanvas)
		{
			DrawPlaceholders(context, accent);
			double step = LayoutCanvas.StepOf(SnapStep);
			if (step <= 0.0 || width <= 1.0 || height <= 1.0)
			{
				return;
			}
			int columns = (int)Math.Min(400.0, Math.Ceiling(width / step));
			int rows = (int)Math.Min(400.0, Math.Ceiling(height / step));
			for (int i = 1; i < columns; i++)
			{
				double x = i * step;
				context.DrawLine(pen, new Point(x, 0.0), new Point(x, height));
			}
			for (int j = 1; j < rows; j++)
			{
				double y = j * step;
				context.DrawLine(pen, new Point(0.0, y), new Point(width, y));
			}
			return;
		}
		if (width <= 1.0 || _cells.Count == 0)
		{
			return;
		}
		DrawPlaceholders(context, accent);
		// [v3] §1.A.1「编辑控件只在需要时出现」:网格底纹(列分隔线)只在拖动 / 改宽度的过程中画
		// (效果图 #winA.edit.grid.busy .cols);平时编辑态只有每块自己的灰色虚线框。行分隔线不再画。
		if (!ShowPlaceholder)
		{
			return;
		}
		for (int k = 1; k < Cols; k++)
		{
			double x = k * (_columnWidth + Gap) - Gap / 2.0;
			if (x > 0.0 && x < width)
			{
				context.DrawLine(pen, new Point(x, 0.0), new Point(x, height));
			}
		}
	}

	// [任务 B] 落点占位框:每个浮起块的**槽位矩形**(= 松手后会落进去的位置 / 吸附后的尺寸),
	// 半透明主题色底 + 主题色描边,画在所有卡片之下
	private void DrawPlaceholders(DrawingContext context, Color accent)
	{
		if (!ShowPlaceholder || _floating.Count == 0)
		{
			return;
		}
		EnsurePens(accent);
		SolidColorBrush fill = _placeFill!;
		Pen stroke = _placePen!;
		foreach (string id in _floating.Keys)
		{
			LayoutRect? rect = RectOf(id);
			if (rect.HasValue && rect.Value.W > 1.0 && rect.Value.H > 1.0)
			{
				LayoutRect r = rect.Value;
				context.DrawRectangle(fill, stroke, new RoundedRect(new Rect(r.X + 0.75, r.Y + 0.75, r.W - 1.5, r.H - 1.5), 10.0));
			}
		}
	}

	// [layout-fix 第三批] 行高输入表去掉某一块(填充块"不计它"的那一遍)
	private static List<LayoutCell> WithoutCell(List<LayoutCell> cells, string id)
	{
		List<LayoutCell> list = new List<LayoutCell>(cells.Count);
		foreach (LayoutCell cell in cells)
		{
			if (cell != null && !string.Equals(cell.Id, id, StringComparison.Ordinal))
			{
				list.Add(cell);
			}
		}
		return list;
	}

	// canvas 画布高:优先宿主给的有限高,否则用参考高(滚动区里量到的高是 Infinity)
	private double ResolveCanvasHeight(double availableHeight)
	{
		if (!double.IsInfinity(availableHeight) && availableHeight > 0.0)
		{
			double ratio = ((CanvasHeight > 0.0) ? CanvasHeight : LayoutCanvas.RefHeight);
			return Math.Max(ratio, availableHeight);
		}
		return ((CanvasHeight > 0.0) ? CanvasHeight : LayoutCanvas.RefHeight);
	}

	// 子元素的块 id:优先实现 ILayoutCanvasChild 的元素(Windows 版这里是 LayoutCard.Spec.Id),
	// 退到 Tag(测试宿主里的假元素用 Tag —— 与源文件同一条兜底)
	private static string IdOf(Control element)
	{
		if (element is ILayoutCanvasChild child)
		{
			return child.BlockId ?? "";
		}
		return ((element is Control control) ? (control.Tag as string ?? "") : "");
	}

	// 折叠态:Windows 版这里是 child is LayoutCard { IsFolded: true }
	private static bool FoldedOf(Control element)
	{
		return element is ILayoutCanvasChild child && child.IsFolded;
	}
}
