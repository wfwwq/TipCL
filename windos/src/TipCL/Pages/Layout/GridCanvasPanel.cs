using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TipCL.Services;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Pages.Layout;

// grid / canvas 两种画布的自绘 Panel(§4.A 的对策:"Canvas vs 自绘 Panel" → 用自绘 Panel + 相对坐标,
// 虚拟高度由引擎算,不用 WPF 的 Canvas)。
//
//   grid   模式:按网格单位的 (x,y,w,h) 排布;行高**两趟测量** —— 第一趟按列宽 + Infinite 高量出每块需求高,
//                 第二趟用 LayoutGrid.RowHeights 把"行高 = 该行最高块的需求高"算出来再 Arrange。
//                 row_h > 0 时直接用固定行高(内容超出按固定高裁,契约语义)。
//   canvas 模式:按归一化矩形排布;画布高取宿主给的高,拿不到有限高(滚动区里最常见)时用
//                 LayoutCanvas.RefHeight(600,对齐旧 DashHost 的兜底)。
//   R34(D-81 Q1 / GOAL-layout-v3 §5.3):canvas 退出界面。SetLayout 收到 mode=canvas 时不再按归一化矩形排,
//                 而是用 LayoutCanvasMigration.ToGridPage 推导成网格(先 y 后 x、span = clamp(round(w×cols)))——
//                 文档不动;ApplyTo 提交时才经 LayoutGrid.CommitCanvasAsGrid 把整页写成 grid。
//                 于是本 Panel 的 Mode 只会是 grid,下面所有 IsCanvas 分支都不可达(留着等合并后一起删)。
//
// 三条纪律:
//   1) 几何全部来自纯模型(LayoutGrid / LayoutCanvas),本文件不写任何坐标公式,只做 Measure/Arrange;
//   2) 子元素按 **块 id** 匹配槽位(不靠 Children 下标),因为 canvas 要按 z 序叠放,顺序与数据顺序不同;
//   3) 编辑态底纹画在 OnRender(在子元素之下),对齐旧 DashHost.RefreshGridLayer 的视觉;
//      **mode=stack 时这个 Panel 根本不进可视树** —— 默认渲染一个像素都不经过这里。
public sealed class GridCanvasPanel : Panel
{
	private List<LayoutCell> _cells = new List<LayoutCell>();

	private List<LayoutRect> _rects = new List<LayoutRect>();

	private double[] _rowHeights = new double[0];

	private double _columnWidth = 1.0;

	// 这次 _columnWidth / _rowHeights 是按哪个宽度量的:Arrange 拿到的宽度不同就得重新量行高
	private double _measuredWidth;

	// 已经为哪些宽度补过 InvalidateMeasure(同一宽度只补一次,避免布局在两宽度间来回时反复失效)
	private readonly HashSet<double> _remeasureWidths = new HashSet<double>();

	private int _rows;

	public GridCanvasPanel()
	{
		// 兜底:面板离开可视树(切页 / 切回 stack)时 CompositionTarget.Rendering 的订阅还在跑 —— 在这里解掉
		Unloaded += OnUnloaded;
	}

	private void OnUnloaded(object sender, RoutedEventArgs e)
	{
		ClearDragVisuals();
	}

	/// <summary>页面模式(R34 起只会是 grid;stack 时本 Panel 不被使用)。canvas 页看 <see cref="DerivedFromCanvas"/>。</summary>
	public string Mode { get; private set; } = LayoutSchema.ModeStack;

	/// <summary>
	/// 当前槽位是不是从 canvas 页推导出来的网格(R34 / D-81 Q1)。
	/// true 时文档里该页仍是 canvas;<see cref="ApplyTo"/> 提交时会把整页写成 grid。
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

	/// <summary>Fill 块测量时的需求高(它们按可视高度分余量,不按内容需求高撑开)。</summary>
	public const double FillMinHeight = 160.0;

	private ICollection<string>? _fillIds;

	private double _fillHeight;

	/// <summary>「占满剩余高度」的块 id(BlockSpec.Fill;空 = 没有)。</summary>
	public ICollection<string>? FillIds
	{
		get
		{
			return _fillIds;
		}
		set
		{
			_fillIds = value;
			InvalidateMeasure();
		}
	}

	/// <summary>可视高度(滚动区高扣掉画布上下边距;0 = 未知):grid 行高合计不足时,余量分给 Fill 块所在的行。</summary>
	public double FillHeight
	{
		get
		{
			return _fillHeight;
		}
		set
		{
			double v = ((value > 0.0 && !double.IsInfinity(value) && !double.IsNaN(value)) ? value : 0.0);
			if (Math.Abs(v - _fillHeight) < 0.5)
			{
				return;
			}
			_fillHeight = v;
			if (HasFill)
			{
				InvalidateMeasure();
			}
		}
	}

	private bool HasFill
	{
		get
		{
			return _fillIds != null && _fillIds.Count > 0;
		}
	}

	private bool IsFill(string id)
	{
		return _fillIds != null && !string.IsNullOrEmpty(id) && _fillIds.Contains(id);
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

	/// <summary>手势开始时拍一份行高快照(拖动时按指针落在哪一行算行号)。</summary>
	public double[] SnapshotRowHeights()
	{
		return (double[])_rowHeights.Clone();
	}

	/// <summary>是否 canvas 模式。R34 起恒为 false(canvas 页按网格推导,见 <see cref="DerivedFromCanvas"/>);保留是因为 LayoutEditor 还在读它。</summary>
	public bool IsCanvas
	{
		get
		{
			return string.Equals(Mode, LayoutSchema.ModeCanvas, StringComparison.Ordinal);
		}
	}

	/// <summary>
	/// 按页面 v2 条目重建槽位(改上下文/换页时调)。
	/// mode=canvas(R34 起界面不再提供)按网格推导:槽位来自 <see cref="LayoutCanvasMigration.ToGridPage"/> 的副本,
	/// <paramref name="page"/> 本身一个字节都不改。
	/// </summary>
	public void SetLayout(string mode, int cols, double rowH, double gap, PageLayoutV2? page)
	{
		DerivedFromCanvas = string.Equals(mode, LayoutSchema.ModeCanvas, StringComparison.Ordinal);
		Mode = ((mode == LayoutSchema.ModeGrid || DerivedFromCanvas) ? LayoutSchema.ModeGrid : LayoutSchema.ModeStack);
		Cols = Math.Max(1, cols);
		RowH = rowH;
		Gap = ((gap >= 0.0) ? gap : 0.0);
		_cells = LayoutGrid.Pack(DerivedFromCanvas ? LayoutCanvasMigration.ToGridPage(page) : page);
		_rows = LayoutGrid.RowCount(_cells);
		_remeasureWidths.Clear();
		InvalidateMeasure();
		InvalidateVisual();
	}

	/// <summary>
	/// 拖动/缩放过程中调用:先压紧(其余块重力上浮让位),再重排。
	/// <paramref name="pinned"/> = 正被拖/正在缩的块 id(固定在当前位置);<paramref name="swapX"/> = 允许左右互换。
	/// </summary>
	public void CompactCells(ICollection<string>? pinned, bool swapX)
	{
		CompactCells(pinned, swapX, gravity: true);
	}

	/// <summary>压紧;<paramref name="gravity"/> = false = 「自动靠拢」关:只解冲突,不上浮、不互换。</summary>
	public void CompactCells(ICollection<string>? pinned, bool swapX, bool gravity)
	{
		if (IsCanvas)
		{
			return; // 自由画布没有"格子"概念,不做压紧
		}
		LayoutGrid.Compact(_cells, Cols, pinned, gravity && swapX, gravity);
		Reflow();
	}

	/// <summary>槽位被就地改过(拖拽/缩放)之后重新排布。</summary>
	public void Reflow()
	{
		_rows = LayoutGrid.RowCount(_cells);
		_remeasureWidths.Clear();
		InvalidateMeasure();
		InvalidateArrange();
		InvalidateVisual();
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
	/// 槽位是从 canvas 页推导来的(或传进来的页仍是 canvas)时走 <see cref="LayoutGrid.CommitCanvasAsGrid"/>:
	/// 这就是 Q1 的「编辑器里提交一次之后才写成 grid」。
	/// </summary>
	public void ApplyTo(PageLayoutV2? page)
	{
		if (DerivedFromCanvas || LayoutCanvasMigration.IsCanvas(page))
		{
			LayoutGrid.CommitCanvasAsGrid(page, _cells);
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
	protected override Size MeasureOverride(Size available)
	{
		double width = ((double.IsInfinity(available.Width) || available.Width <= 0.0) ? 900.0 : available.Width);
		if (IsCanvas)
		{
			double height = ResolveCanvasHeight(available.Height);
			RebuildCanvasRects(width, height);
			foreach (UIElement child in base.InternalChildren)
			{
				if (child is DropPlaceholder placeholder)
				{
					// 占位框:测量尺寸与排布尺寸一致(按它将要排到的矩形量,而不是量 0 再按非零矩形排)
					MeasurePlaceholder(placeholder);
					continue;
				}
				LayoutRect? rect = RectOf(IdOf(child));
				child.Measure((rect.HasValue ? new Size(rect.Value.W, rect.Value.H) : new Size(width, LayoutCanvas.RefHeight)));
			}
			return new Size(((double.IsInfinity(available.Width) || available.Width <= 0.0) ? width : available.Width), height);
		}
		double total = MeasureGridChildren(width);
		return new Size(((double.IsInfinity(available.Width) || available.Width <= 0.0) ? width : available.Width), total);
	}

	/// <summary>
	/// grid 模式的第一趟(按给定宽度):量每块需求高 → 算列宽 / 行高 / 矩形。
	/// Measure 与「Arrange 拿到的宽度和测量时不一样」(滚动条出现 / 父级以别的宽度 Arrange)两处共用,
	/// 保证行高永远是按当前宽度量出来的。
	/// </summary>
	private double MeasureGridChildren(double width)
	{
		_measuredWidth = width;
		_columnWidth = LayoutGrid.ColumnWidth(width, Cols, Gap);
		Dictionary<string, double> desired = new Dictionary<string, double>(StringComparer.Ordinal);
		foreach (UIElement child in base.InternalChildren)
		{
			if (child is DropPlaceholder)
			{
				continue; // 占位框没有内容:等槽位矩形算出来再按目标矩形量(见方法末尾)
			}
			string id = IdOf(child);
			LayoutCell? cell = CellOf(id);
			if (cell == null)
			{
				child.Measure(new Size(_columnWidth, LayoutCanvas.FallbackMinH));
				continue;
			}
			double cellWidth = _columnWidth * cell.W + Gap * Math.Max(0, cell.W - 1);
			// height_px(拖下边设定的固定像素高)优先;没设才按内容需求高
			double fixedH = cell.HeightPx;
			if (fixedH <= 0.0 && IsFill(id) && RowH <= 0.0)
			{
				// 占满高度的块(内容自带滚动):按一个有限的最小高量,行高的余量后面再按可视高度分
				child.Measure(new Size(Math.Max(1.0, cellWidth), FillMinHeight));
				desired[id] = FillMinHeight;
				continue;
			}
			child.Measure(new Size(Math.Max(1.0, cellWidth), (fixedH > 0.0) ? fixedH : double.PositiveInfinity));
			desired[id] = ((fixedH > 0.0) ? fixedH : child.DesiredSize.Height);
		}
		_rows = LayoutGrid.RowCount(_cells);
		_rowHeights = LayoutGrid.RowHeights(_cells, _rows, RowH, desired, Math.Min(LayoutCanvas.FallbackMinH, 40.0));
		if (HasFill && RowH <= 0.0 && _fillHeight > 0.0)
		{
			_rowHeights = LayoutGrid.FillRows(_cells, _rowHeights, _fillIds, Gap, _fillHeight);
		}
		RebuildGridRects(_columnWidth, _rowHeights, Gap);
		if (HasFill)
		{
			// Fill 块按最终格子尺寸再量一次:内容(对话区的滚动)拿到的是真实可视高,滚动与吸底照常
			foreach (UIElement child in base.InternalChildren)
			{
				string id = IdOf(child);
				LayoutRect? rect = RectOf(id);
				if (rect.HasValue && IsFill(id))
				{
					child.Measure(new Size(rect.Value.W, rect.Value.H));
				}
			}
		}
		foreach (UIElement child in base.InternalChildren)
		{
			if (child is DropPlaceholder placeholder)
			{
				MeasurePlaceholder(placeholder);
			}
		}
		return RowTotalHeight();
	}

	/// <summary>行高合计(含行间 gap)。</summary>
	private double RowTotalHeight()
	{
		double total = 0.0;
		for (int i = 0; i < _rowHeights.Length; i++)
		{
			total += _rowHeights[i] + ((i < _rowHeights.Length - 1) ? Gap : 0.0);
		}
		return total;
	}

	// 整表排布结果:复用同一个 List(每次排布不再新分配);公式仍全部来自纯模型 Place
	private void RebuildGridRects(double columnWidth, double[] rowHeights, double gap)
	{
		_rects.Clear();
		for (int i = 0; i < _cells.Count; i++)
		{
			_rects.Add(LayoutGrid.Place(_cells[i], columnWidth, rowHeights, gap));
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

	// 占位框按"将要排到的矩形"测量:落点预览矩形优先,否则用它在槽位里的矩形
	private void MeasurePlaceholder(DropPlaceholder placeholder)
	{
		LayoutRect? rect = ((_placeholderRects.TryGetValue(placeholder.BlockId, out LayoutRect over)) ? over : RectOf(placeholder.BlockId));
		if (!rect.HasValue)
		{
			placeholder.Measure(default(Size));
			return;
		}
		LayoutRect value = rect.Value;
		placeholder.Measure(new Size(Math.Max(0.0, value.W), Math.Max(0.0, value.H)));
	}

	/// <summary>
	/// 第二趟:按最终宽度重算列宽(滚动条出现会改变可用宽),再把每块 Arrange 到自己的矩形上。
	/// 行高沿用第一趟量出来的值(块高不定时行高必须来自需求测量,不能靠 Arrange 反推)。
	/// </summary>
	protected override Size ArrangeOverride(Size final)
	{
		double width = Math.Max(1.0, final.Width);
		if (IsCanvas)
		{
			double height = ResolveCanvasHeight(final.Height);
			RebuildCanvasRects(width, height);
		}
		else if (Math.Abs(width - _measuredWidth) > 0.5)
		{
			// Arrange 宽和测量宽不一致(滚动条出现 / 父级以别的宽度 Arrange):行高必须按新宽度重新量一遍,
			// 否则 auto 行高还是按旧的(更宽)列宽算的 —— 内容会被裁在矮行里。
			double before = RowTotalHeight();
			double previousWidth = _measuredWidth;
			double total = MeasureGridChildren(width);
			// Arrange 更窄时内容总高只增不减 → 期望高变了就得让宿主下一趟按新的内容高给滚动区(否则底部滚不到)。
			// 只在"更窄"时补一次失效(更宽时内容只会变矮,不存在裁切);同一宽度只补一次,避免布局来回抖动。
			if (Math.Abs(total - before) > 0.5 && width < previousWidth && _remeasureWidths.Add(Math.Round(width, 1)))
			{
				InvalidateMeasure();
			}
		}
		else
		{
			_columnWidth = LayoutGrid.ColumnWidth(width, Cols, Gap);
			RebuildGridRects(_columnWidth, _rowHeights, Gap);
		}
		bool flip = _flipPending && Motion.Enabled;
		foreach (UIElement child in base.InternalChildren)
		{
			string id = IdOf(child);
			LayoutRect? rect;
			bool floating = false;
			if (child is DropPlaceholder placeholder)
			{
				// 落点占位框:优先用宿主给的预览矩形(像素高度缩放),否则就是被拖块在新排版里的格子
				rect = ((_placeholderRects.TryGetValue(placeholder.BlockId, out LayoutRect over)) ? over : RectOf(placeholder.BlockId));
				id = DropPlaceholder.Prefix + placeholder.BlockId;
			}
			else if (_float.TryGetValue(id, out LayoutRect floatRect))
			{
				// 浮起的卡片(正被拖 / 正落位):按像素矩形排,不跟格子走
				rect = floatRect;
				floating = true;
			}
			else
			{
				rect = RectOf(id);
			}
			if (!rect.HasValue)
			{
				child.Arrange(new Rect(0.0, 0.0, 0.0, 0.0));
				_arranged.Remove(child);
				continue;
			}
			LayoutRect value = rect.Value;
			Rect target = new Rect(value.X, value.Y, Math.Max(0.0, value.W), Math.Max(0.0, value.H));
			if (floating || IsCanvas)
			{
				// 尺寸可能在两次测量之间变了(缩放预览 / 落位动画 / canvas 拖边):按新尺寸补量这一块。
				// 尺寸没变时 Measure 是空操作 —— 动画期间不会每帧让整个画布重新测量。
				child.Measure(new Size(target.Width, MeasureHeightFor(id, target.Height)));
			}
			if (flip && !floating && (_flipOnly == null || _flipOnly.Contains(id)) && _arranged.TryGetValue(child, out Rect old))
			{
				StartFlip(child, old, target, _flipMs);
			}
			_arranged[child] = target;
			child.Arrange(target);
		}
		_flipPending = false;
		_flipOnly = null;
		return final;
	}

	/// <summary>
	/// 编辑态底纹(在子元素之下):
	///   grid   画列分隔线 + 每行边界(为了让"块占了几行几列"看得见);
	///   canvas 画吸附档的方格(对齐旧 DashHost.<c>RefreshGridLayer</c> 的 DrawingBrush 视觉)。
	/// 非编辑态或吸附档为 0 时什么都不画。
	/// </summary>
	protected override void OnRender(DrawingContext dc)
	{
		base.OnRender(dc);
		if (!EditMode || base.InternalChildren.Count == 0)
		{
			return;
		}
		Color accent = LayoutEditor.AccentColor();
		SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(38, accent.R, accent.G, accent.B));
		brush.Freeze();
		Pen pen = new Pen(brush, 0.6);
		pen.Freeze();
		double width = Math.Max(0.0, base.ActualWidth);
		double height = Math.Max(0.0, base.ActualHeight);
		if (IsCanvas)
		{
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
				dc.DrawLine(pen, new Point(x, 0.0), new Point(x, height));
			}
			for (int j = 1; j < rows; j++)
			{
				double y = j * step;
				dc.DrawLine(pen, new Point(0.0, y), new Point(width, y));
			}
			return;
		}
		// v3:网格模式不再铺主题色栅格底纹(效果图:内容是主角,只留灰色虚线框);对齐靠整数列吸附 + 落点占位框
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

	// ————————————— 拖动视觉:浮起跟手 / 落点占位框 / FLIP 让位动画 / 松手落位 —————————————
	//
	// 全部只动 RenderTransform 或单块的排布矩形:
	//   - 拖动中被拖卡片按像素跟随指针 = 改它的 TranslateTransform,不触发任何布局;
	//   - 落点格子变了才重排一次(宿主压紧后 RequestFlip + Reflow),其它块从旧位置平滑动画到新位置(FLIP);
	//   - 缩放预览 / 松手落位只改那一块的排布矩形 + InvalidateArrange(画布不重新测量,其它块的 Arrange 是空操作);
	//   - 帧驱动用 CompositionTarget.Rendering(跟显示器刷新走,不限帧率),进度按经过的毫秒算。

	// 上一次排布到的位置(FLIP 的"旧位置")
	private readonly Dictionary<UIElement, Rect> _arranged = new Dictionary<UIElement, Rect>();

	private bool _flipPending;

	private double _flipMs = LayoutMotion.FlipMs;

	private HashSet<string>? _flipOnly;

	// 浮起的卡片:id → 像素矩形(拖动起手矩形 / 缩放预览 / 落位动画的当前帧)
	private readonly Dictionary<string, LayoutRect> _float = new Dictionary<string, LayoutRect>(StringComparer.Ordinal);

	// 落点占位框(每个被拖块一个)与预览矩形覆盖
	private readonly Dictionary<string, DropPlaceholder> _placeholders = new Dictionary<string, DropPlaceholder>(StringComparer.Ordinal);

	private readonly Dictionary<string, LayoutRect> _placeholderRects = new Dictionary<string, LayoutRect>(StringComparer.Ordinal);

	// 松手落位动画
	private readonly Dictionary<string, LayoutRect> _settleFrom = new Dictionary<string, LayoutRect>(StringComparer.Ordinal);

	// 落位终点:等宿主提交后的那次排布完成再取一次并定住(提交时的压紧可能把格子再往上挪)
	private readonly Dictionary<string, LayoutRect> _settleTo = new Dictionary<string, LayoutRect>(StringComparer.Ordinal);

	private readonly System.Diagnostics.Stopwatch _settleClock = new System.Diagnostics.Stopwatch();

	private bool _settling;

	private Action? _settleDone;

	/// <summary>是否有浮起的卡片(正被拖或正在落位)。</summary>
	public bool HasFloating
	{
		get
		{
			return _float.Count > 0;
		}
	}

	/// <summary>
	/// 下一次排布时,位置变了的块从旧位置动画到新位置(FLIP)。
	/// <paramref name="only"/> = 只动这些块(canvas 吸附那一下只动被拖块;null = 除浮起卡片外全部);
	/// <paramref name="ms"/> = 动画时长(≤0 用默认 170ms)。
	/// </summary>
	public void RequestFlip(ICollection<string>? only = null, double ms = 0.0)
	{
		_flipPending = true;
		_flipMs = ((ms > 0.0) ? ms : LayoutMotion.FlipMs);
		_flipOnly = ((only != null) ? new HashSet<string>(only, StringComparer.Ordinal) : null);
	}

	/// <summary>让这些块浮起来(按当前排布矩形钉住,之后用 <see cref="MoveFloat"/> 跟手),并在原格子上显示落点占位框。</summary>
	public void BeginFloat(IEnumerable<string> ids, bool placeholder)
	{
		// 上一次松手的落位动画还没走完就又抓起一块:先让它直接落定,再开始新的
		StopSettle(true);
		foreach (string id in ids)
		{
			LayoutRect? rect = RectOf(id);
			UIElement? child = ChildOf(id);
			if (!rect.HasValue || child == null)
			{
				continue;
			}
			// 起手时如果还挂着上一次的让位动画,从"眼睛看到的位置"接着走
			TranslateTransform tt = TranslateOf(child);
			LayoutRect start = LayoutMotion.Offset(rect.Value, tt.X, tt.Y);
			StopAnimation(tt);
			tt.X = 0.0;
			tt.Y = 0.0;
			_float[id] = start;
			Panel.SetZIndex(child, 1000);
			if (placeholder)
			{
				ShowPlaceholder(id);
			}
		}
		InvalidateArrange();
	}

	/// <summary>拖动中:所有浮起卡片按像素平移(只改 RenderTransform,不触发布局)。</summary>
	public void MoveFloat(double dx, double dy)
	{
		foreach (string id in _float.Keys)
		{
			UIElement? child = ChildOf(id);
			if (child == null)
			{
				continue;
			}
			TranslateTransform tt = TranslateOf(child);
			// 浮起期间这块不会再被别人起 FLIP 动画(BeginFloat 已把动画停掉),这里逐帧撤动画是纯多余
			tt.X = dx;
			tt.Y = dy;
		}
	}

	/// <summary>缩放中:浮起卡片按像素实时改矩形(只重新排这一块)。</summary>
	public void ResizeFloat(string id, LayoutRect rect)
	{
		if (!_float.ContainsKey(id))
		{
			return;
		}
		_float[id] = rect;
		// 缩小预览时内容可能比卡片高:先裁在卡片里,落定后恢复
		UIElement? child = ChildOf(id);
		if (child != null)
		{
			child.ClipToBounds = true;
		}
		InvalidateArrange();
	}

	/// <summary>占位框改用给定的预览矩形(像素高度缩放:格子不动,只预览吸附后的尺寸);null = 回到格子矩形。</summary>
	public void SetPlaceholderRect(string id, LayoutRect? rect)
	{
		if (rect.HasValue)
		{
			_placeholderRects[id] = rect.Value;
		}
		else
		{
			_placeholderRects.Remove(id);
		}
		InvalidateArrange();
	}

	/// <summary>
	/// 松手落位:浮起卡片把身上的平移折进矩形(视觉不动),然后在 170ms 里 ease-out 动画到它的格子矩形
	/// (格子在宿主提交 / 重排之后才确定,所以每帧现取),落定后去掉占位框、清掉浮起状态,再回调 <paramref name="done"/>。
	/// </summary>
	public void Settle(Action? done)
	{
		StopSettle(false);
		_settleFrom.Clear();
		_settleTo.Clear();
		List<string> ids = new List<string>(_float.Keys);
		foreach (string id in ids)
		{
			UIElement? child = ChildOf(id);
			LayoutRect from = _float[id];
			if (child != null)
			{
				TranslateTransform tt = TranslateOf(child);
				from = LayoutMotion.Offset(from, tt.X, tt.Y);
				StopAnimation(tt);
				tt.X = 0.0;
				tt.Y = 0.0;
			}
			_float[id] = from;
			_settleFrom[id] = from;
		}
		_settleDone = done;
		if (_settleFrom.Count == 0 || !Motion.Enabled)
		{
			FinishSettle();
			return;
		}
		_settling = true;
		_settleClock.Restart();
		CompositionTarget.Rendering -= OnSettleFrame;
		CompositionTarget.Rendering += OnSettleFrame;
		InvalidateArrange();
	}

	/// <summary>RenderBox 清空 Children 后把拖动覆盖层(落点占位框)挂回来(放在最底层,卡片盖在它上面)。</summary>
	public void ReattachOverlays()
	{
		foreach (DropPlaceholder placeholder in _placeholders.Values)
		{
			if (!base.Children.Contains(placeholder))
			{
				base.Children.Insert(0, placeholder);
			}
		}
	}

	/// <summary>主题切换:栅格底纹重画(配色在 OnRender 里现取)+ 占位框重取主题色。</summary>
	public void RefreshThemeVisuals()
	{
		foreach (DropPlaceholder placeholder in _placeholders.Values)
		{
			placeholder.ApplyTheme();
		}
		InvalidateVisual();
	}

	/// <summary>收掉所有拖动视觉(浮起的卡片、落点占位框、进行中的动画),立即回到格子位置。</summary>
	public void ClearDragVisuals()
	{
		StopSettle(false);
		_settleDone = null;
		foreach (string id in new List<string>(_float.Keys))
		{
			UIElement? child = ChildOf(id);
			if (child != null)
			{
				TranslateTransform tt = TranslateOf(child);
				StopAnimation(tt);
				tt.X = 0.0;
				tt.Y = 0.0;
				Panel.SetZIndex(child, 0);
				child.ClipToBounds = false;
			}
		}
		_float.Clear();
		RemovePlaceholders();
		_flipPending = false;
		_flipOnly = null;
		InvalidateArrange();
	}

	private void OnSettleFrame(object? sender, EventArgs e)
	{
		if (!_settling)
		{
			CompositionTarget.Rendering -= OnSettleFrame;
			return;
		}
		if (_settleTo.Count == 0)
		{
			// 第一帧:宿主的提交 / 重排还没排完就先等一帧(最多等 100ms),然后把终点定住、从这一刻开始计时
			if ((!IsMeasureValid || !IsArrangeValid) && _settleClock.Elapsed.TotalMilliseconds < 100.0)
			{
				return;
			}
			foreach (KeyValuePair<string, LayoutRect> kv in _settleFrom)
			{
				_settleTo[kv.Key] = RectOf(kv.Key) ?? kv.Value;
			}
			_settleClock.Restart();
		}
		double t = LayoutMotion.Progress(_settleClock.Elapsed.TotalMilliseconds, LayoutMotion.SettleMs);
		double k = LayoutMotion.EaseOutCubic(t);
		foreach (KeyValuePair<string, LayoutRect> kv in _settleFrom)
		{
			LayoutRect to = (_settleTo.TryGetValue(kv.Key, out LayoutRect fixedTo) ? fixedTo : kv.Value);
			_float[kv.Key] = LayoutMotion.LerpRect(kv.Value, to, k);
		}
		InvalidateArrange();
		if (t >= 1.0)
		{
			FinishSettle();
		}
	}

	private void FinishSettle()
	{
		StopSettle(false);
		foreach (string id in new List<string>(_float.Keys))
		{
			UIElement? child = ChildOf(id);
			if (child != null)
			{
				Panel.SetZIndex(child, 0);
				child.ClipToBounds = false;
				// 落定后它的"旧位置"就是格子位置,下一次 FLIP 从这里算
				LayoutRect? slot = RectOf(id);
				if (slot.HasValue)
				{
					_arranged[child] = new Rect(slot.Value.X, slot.Value.Y, slot.Value.W, slot.Value.H);
				}
			}
		}
		_float.Clear();
		_settleFrom.Clear();
		_settleTo.Clear();
		RemovePlaceholders();
		InvalidateArrange();
		Action? done = _settleDone;
		_settleDone = null;
		done?.Invoke();
	}

	private void StopSettle(bool finish)
	{
		if (_settling)
		{
			_settling = false;
			_settleClock.Stop();
			CompositionTarget.Rendering -= OnSettleFrame;
			if (finish)
			{
				FinishSettle();
			}
		}
	}

	private void ShowPlaceholder(string id)
	{
		if (_placeholders.ContainsKey(id))
		{
			return;
		}
		DropPlaceholder placeholder = new DropPlaceholder(id);
		Panel.SetZIndex(placeholder, -1);
		_placeholders[id] = placeholder;
		base.Children.Insert(0, placeholder);
	}

	private void RemovePlaceholders()
	{
		foreach (DropPlaceholder placeholder in _placeholders.Values)
		{
			StopAnimation(TranslateOf(placeholder));
			_arranged.Remove(placeholder);
			base.Children.Remove(placeholder);
		}
		_placeholders.Clear();
		_placeholderRects.Clear();
	}

	// FLIP:先算"眼睛看到的旧位置 - 新位置"的反向偏移挂上去,再动画归零;先取消同一块的旧动画
	private static void StartFlip(UIElement child, Rect old, Rect target, double ms)
	{
		TranslateTransform tt = TranslateOf(child);
		double offX = LayoutMotion.FlipOffset(old.X, tt.X, target.X);
		double offY = LayoutMotion.FlipOffset(old.Y, tt.Y, target.Y);
		StopAnimation(tt);
		tt.X = 0.0;
		tt.Y = 0.0;
		if (!LayoutMotion.NeedsAnimation(offX, offY))
		{
			return;
		}
		Duration duration = new Duration(TimeSpan.FromMilliseconds(ms));
		CubicEase ease = new CubicEase
		{
			EasingMode = EasingMode.EaseOut
		};
		if (Math.Abs(offX) >= LayoutMotion.MinOffset)
		{
			tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(offX, 0.0, duration)
			{
				EasingFunction = ease,
				FillBehavior = FillBehavior.Stop
			});
		}
		if (Math.Abs(offY) >= LayoutMotion.MinOffset)
		{
			tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offY, 0.0, duration)
			{
				EasingFunction = ease,
				FillBehavior = FillBehavior.Stop
			});
		}
	}

	// 取消动画:先读出当前动画值(调用方已经用过),再撤掉动画时钟,让基值接管
	private static void StopAnimation(TranslateTransform tt)
	{
		tt.BeginAnimation(TranslateTransform.XProperty, null);
		tt.BeginAnimation(TranslateTransform.YProperty, null);
	}

	// 卡片的平移变换(只有本画布给区块卡片挂 RenderTransform;别人挂的不是 TranslateTransform 时换成我们的)
	private static TranslateTransform TranslateOf(UIElement child)
	{
		if (child.RenderTransform is TranslateTransform tt && !tt.IsFrozen)
		{
			return tt;
		}
		TranslateTransform fresh = new TranslateTransform();
		child.RenderTransform = fresh;
		return fresh;
	}

	// 与 MeasureOverride 同一套高度约束(否则测量与排布两趟约束不同,这一块每次布局都要重量)
	private double MeasureHeightFor(string id, double rectHeight)
	{
		if (IsCanvas || (IsFill(id) && RowH <= 0.0))
		{
			return rectHeight;
		}
		LayoutCell? cell = CellOf(id);
		if (cell != null && cell.HeightPx > 0.0)
		{
			return cell.HeightPx;
		}
		return double.PositiveInfinity;
	}

	private UIElement? ChildOf(string id)
	{
		foreach (UIElement child in base.InternalChildren)
		{
			if (!(child is DropPlaceholder) && string.Equals(IdOf(child), id, StringComparison.Ordinal))
			{
				return child;
			}
		}
		return null;
	}

	// 子元素的块 id:优先 LayoutCard 的 Spec.Id,退到 Tag(测试宿主里的假元素用 Tag)
	private static string IdOf(UIElement element)
	{
		if (element is LayoutCard card)
		{
			return card.Spec?.Id ?? "";
		}
		return ((element is FrameworkElement frameworkElement) ? (frameworkElement.Tag as string ?? "") : "");
	}
}

/// <summary>
/// 落点占位框:被拖块将要落下的格子上显示一个半透明框(主题色描边 + 浅色填充),不吃鼠标。
/// 由 <see cref="GridCanvasPanel"/> 自己管理(加在最底层,卡片盖在它上面)。
/// </summary>
internal sealed class DropPlaceholder : Border
{
	public const string Prefix = "__drop__:";

	public string BlockId { get; }

	// 当前配色对应的主题色:主题切换后重取一次(颜色没变时 ApplyTheme 什么都不做)
	private Color _accent;

	public DropPlaceholder(string blockId)
	{
		BlockId = blockId ?? "";
		IsHitTestVisible = false;
		Focusable = false;
		CornerRadius = new CornerRadius(10.0);
		BorderThickness = new Thickness(1.5);
		ApplyTheme();
		Tag = Prefix + BlockId;
	}

	/// <summary>按当前主题重取配色(占位框在构造时固化过配色,主题切换后必须刷新)。</summary>
	public void ApplyTheme()
	{
		Color accent = LayoutEditor.AccentColor();
		if (Background != null && accent == _accent)
		{
			return;
		}
		_accent = accent;
		SolidColorBrush fill = new SolidColorBrush(Color.FromArgb(28, accent.R, accent.G, accent.B));
		fill.Freeze();
		SolidColorBrush stroke = new SolidColorBrush(Color.FromArgb(200, accent.R, accent.G, accent.B));
		stroke.Freeze();
		Background = fill;
		BorderBrush = stroke;
	}
}
