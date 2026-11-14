using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
	protected override Size MeasureOverride(Size available)
	{
		double width = ((double.IsInfinity(available.Width) || available.Width <= 0.0) ? 900.0 : available.Width);
		if (IsCanvas)
		{
			double height = ResolveCanvasHeight(available.Height);
			_rects = LayoutCanvas.Rects(_cells, width, height);
			foreach (UIElement child in base.InternalChildren)
			{
				LayoutRect? rect = RectOf(IdOf(child));
				child.Measure((rect.HasValue ? new Size(rect.Value.W, rect.Value.H) : new Size(width, LayoutCanvas.RefHeight)));
			}
			return new Size(((double.IsInfinity(available.Width) || available.Width <= 0.0) ? width : available.Width), height);
		}
		_columnWidth = LayoutGrid.ColumnWidth(width, Cols, Gap);
		Dictionary<string, double> desired = new Dictionary<string, double>(StringComparer.Ordinal);
		foreach (UIElement child in base.InternalChildren)
		{
			string id = IdOf(child);
			LayoutCell? cell = CellOf(id);
			if (cell == null)
			{
				child.Measure(new Size(_columnWidth, LayoutCanvas.FallbackMinH));
				continue;
			}
			double cellWidth = _columnWidth * cell.W + Gap * Math.Max(0, cell.W - 1);
			child.Measure(new Size(Math.Max(1.0, cellWidth), double.PositiveInfinity));
			desired[id] = child.DesiredSize.Height;
		}
		_rows = LayoutGrid.RowCount(_cells);
		_rowHeights = LayoutGrid.RowHeights(_cells, _rows, RowH, desired, LayoutCanvas.FallbackMinH);
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
		foreach (UIElement child in base.InternalChildren)
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
		if (width <= 1.0 || _cells.Count == 0)
		{
			return;
		}
		for (int k = 1; k < Cols; k++)
		{
			double x = k * (_columnWidth + Gap) - Gap / 2.0;
			if (x > 0.0 && x < width)
			{
				dc.DrawLine(pen, new Point(x, 0.0), new Point(x, height));
			}
		}
		double y2 = 0.0;
		for (int m = 0; m < _rowHeights.Length - 1; m++)
		{
			y2 += _rowHeights[m] + Gap;
			if (y2 > 0.0 && y2 < height)
			{
				dc.DrawLine(pen, new Point(0.0, y2 - Gap / 2.0), new Point(width, y2 - Gap / 2.0));
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
