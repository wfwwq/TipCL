// R32-MED:来源 Windows 版参考仓库 windos/src/TipCL/Pages/Layout/GridCanvasPanel.cs(L1 444 行;
//       L2@33b5fff 逐字节相同、WT-R31-MERGE@r32-r31-merge 也逐字节相同);
//       Linux 同层落地点 wt-linux-layout/src/TipCL.App/Pages/Layout/GridCanvasPanel.cs(R32-LV)。
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
//   因为 LayoutCard 住在 LayoutEditor.cs(Linux 树把它划给另一工作包;本包两者都自己写,
//   但接口缝仍然保留 —— 它同时也是测试宿主放"假元素"的入口,Linux 版的 36 条断言就架在这条缝上)。
// 与 Linux 版唯一的差别:namespace TipCL.App.Pages.Layout → TipCL.Pages(mac D7 扁平);
//   `using TipCL.Core.Launcher.Layout;` 保留(PageLayoutV2 / LayoutSchema 在 mac 也在那个命名空间,
//   而 LayoutCell / LayoutRect / LayoutCanvas / LayoutGrid 在 mac 的 TipCL.Pages —— 两处都能解析,实测确认)。
//
// ⚠ 基类换掉的唯一一处硬差异(编译实测,见 progress/R32-LV.md §WPF 映射;mac 同构):
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

namespace TipCL.Pages;

/// <summary>
/// 画布子元素 → 槽位信息的解析缝。WPF 版 <c>GridCanvasPanel</c> 直接判 <c>LayoutCard</c> 的
/// <c>Spec.Id</c> / <c>IsFolded</c>;本文件与 <c>LayoutCard</c>(住在 <c>LayoutEditor.cs</c>)解耦,
/// 改由这个接口解耦(等价语义,逐条见 <c>GridCanvasPanel.IdOf</c> / <c>FoldedOf</c>)。
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

	private int _rows;

	/// <summary>页面模式(grid | canvas;stack 时本 Panel 不被使用)。</summary>
	public string Mode { get; private set; } = LayoutSchema.ModeStack;

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

	/// <summary>是否 canvas 模式。</summary>
	public bool IsCanvas
	{
		get
		{
			return string.Equals(Mode, LayoutSchema.ModeCanvas, StringComparison.Ordinal);
		}
	}

	/// <summary>按页面 v2 条目重建槽位(改上下文/换页时调)。</summary>
	public void SetLayout(string mode, int cols, double rowH, double gap, PageLayoutV2? page)
	{
		Mode = ((mode == LayoutSchema.ModeGrid || mode == LayoutSchema.ModeCanvas) ? mode : LayoutSchema.ModeStack);
		Cols = Math.Max(1, cols);
		RowH = rowH;
		Gap = ((gap >= 0.0) ? gap : 0.0);
		_cells = (IsCanvas ? LayoutCanvas.Load(page) : LayoutGrid.Pack(page));
		if (IsCanvas)
		{
			_cells = LayoutCanvas.StackedOrder(_cells);
		}
		_rows = LayoutGrid.RowCount(_cells);
		InvalidateMeasure();
		InvalidateVisual();
	}

	/// <summary>槽位被就地改过(拖拽/缩放)之后重新排布。</summary>
	public void Reflow()
	{
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
		if (IsCanvas || _cells.Count == 0)
		{
			return;
		}
		LayoutGrid.Compact(_cells, Cols, pinned, swapX);
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

	/// <summary>把槽位写回页面块(拖拽/缩放提交时调)。</summary>
	public void ApplyTo(PageLayoutV2? page)
	{
		if (IsCanvas)
		{
			LayoutCanvas.Apply(page, _cells);
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

	protected override Size MeasureOverride(Size available)
	{
		double width = ((double.IsInfinity(available.Width) || available.Width <= 0.0) ? 900.0 : available.Width);
		// 宿主自己不做任何测量(ChildHost),这里量它只是为了让它在 Arrange 前有有效的测量状态
		_host.Measure(available);
		if (IsCanvas)
		{
			double height = ResolveCanvasHeight(available.Height);
			_rects = LayoutCanvas.Rects(_cells, width, height);
			foreach (Control child in Children)
			{
				LayoutRect? rect = RectOf(IdOf(child));
				child.Measure((rect.HasValue ? new Size(rect.Value.W, rect.Value.H) : new Size(width, LayoutCanvas.RefHeight)));
			}
			return new Size(((double.IsInfinity(available.Width) || available.Width <= 0.0) ? width : available.Width), height);
		}
		_columnWidth = LayoutGrid.ColumnWidth(width, Cols, Gap);
		Dictionary<string, double> desired = new Dictionary<string, double>(StringComparer.Ordinal);
		HashSet<string> noMin = new HashSet<string>(StringComparer.Ordinal);
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
			child.Measure(new Size(Math.Max(1.0, cellWidth), double.PositiveInfinity));
			desired[id] = child.DesiredSize.Height;
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
		_rects = LayoutGrid.Rects(_cells, _columnWidth, _rowHeights, Gap);
		double total = 0.0;
		for (int i = 0; i < _rowHeights.Length; i++)
		{
			total += _rowHeights[i] + ((i < _rowHeights.Length - 1) ? Gap : 0.0);
		}
		return new Size(((double.IsInfinity(available.Width) || available.Width <= 0.0) ? width : available.Width), total);
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
			_rects = LayoutCanvas.Rects(_cells, width, height);
		}
		else
		{
			_columnWidth = LayoutGrid.ColumnWidth(width, Cols, Gap);
			_rects = LayoutGrid.Rects(_cells, _columnWidth, _rowHeights, Gap);
		}
		foreach (Control child in Children)
		{
			LayoutRect? rect = RectOf(IdOf(child));
			if (!rect.HasValue)
			{
				child.Arrange(new Rect(0.0, 0.0, 0.0, 0.0));
				continue;
			}
			LayoutRect value = rect.Value;
			child.Arrange(new Rect(value.X, value.Y, value.W, value.H));
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
		SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(38, accent.R, accent.G, accent.B));
		Pen pen = new Pen(brush, 0.6);
		double width = Math.Max(0.0, Bounds.Width);
		double height = Math.Max(0.0, Bounds.Height);
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
		for (int k = 1; k < Cols; k++)
		{
			double x = k * (_columnWidth + Gap) - Gap / 2.0;
			if (x > 0.0 && x < width)
			{
				context.DrawLine(pen, new Point(x, 0.0), new Point(x, height));
			}
		}
		double y2 = 0.0;
		for (int m = 0; m < _rowHeights.Length - 1; m++)
		{
			y2 += _rowHeights[m] + Gap;
			if (y2 > 0.0 && y2 < height)
			{
				context.DrawLine(pen, new Point(0.0, y2 - Gap / 2.0), new Point(width, y2 - Gap / 2.0));
			}
		}
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
