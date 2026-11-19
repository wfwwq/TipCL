using System;

namespace TipCL.Pages.Layout;

/// <summary>
/// 编辑态拖动 / 吸附的纯几何与缓动(零 WPF 依赖,net8 测试宿主直接断言):
///   - 落点:grid 拖动时按"被拖卡片左上角 → 最近列、指针所在行"算出将要落下的格子(与原来的判定完全相同);
///   - 缩放预览:拖右边 / 下边时卡片按像素实时变化的矩形,以及像素高度的 4px 吸附;
///   - FLIP:重新排版后给一个"旧位置 - 新位置"的反向偏移再动画归零(偏移要算上正在进行的动画的当前值,
///     这样同一块连续被让位时是从"眼睛看到的位置"接着走,不会跳);
///   - 缓动:ease-out(三次),进度按经过的毫秒算(不按帧数算,帧率多高都一样快,也就不需要限帧)。
/// </summary>
public static class LayoutMotion
{
	/// <summary>其他块让位 / 浮上来的动画时长(ms)。</summary>
	public const double FlipMs = 170.0;

	/// <summary>松手后被拖卡片落入占位框的动画时长(ms)。</summary>
	public const double SettleMs = 170.0;

	/// <summary>canvas 磁性吸附那一下的短动画时长(ms)。</summary>
	public const double SnapMs = 90.0;

	/// <summary>小于这个像素差的偏移不值得动画(避免亚像素抖动)。</summary>
	public const double MinOffset = 0.5;

	/// <summary>像素高度的吸附步长(px)。</summary>
	public const double HeightStep = 4.0;

	/// <summary>像素高度下限(px)。</summary>
	public const double MinHeightPx = 56.0;

	/// <summary>三次 ease-out:t ∈ [0,1] → [0,1],开头快、收尾慢。越界夹取。</summary>
	public static double EaseOutCubic(double t)
	{
		double x = Clamp01(t);
		double inv = 1.0 - x;
		return 1.0 - inv * inv * inv;
	}

	/// <summary>动画进度(0..1):按经过的毫秒算;时长 ≤ 0 视为瞬间完成。</summary>
	public static double Progress(double elapsedMs, double durationMs)
	{
		if (durationMs <= 0.0 || double.IsNaN(durationMs))
		{
			return 1.0;
		}
		if (double.IsNaN(elapsedMs))
		{
			return 0.0;
		}
		return Clamp01(elapsedMs / durationMs);
	}

	/// <summary>线性插值。</summary>
	public static double Lerp(double a, double b, double t)
	{
		return a + (b - a) * t;
	}

	/// <summary>矩形插值(位置与尺寸一起走)。</summary>
	public static LayoutRect LerpRect(LayoutRect a, LayoutRect b, double t)
	{
		return new LayoutRect(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t), Lerp(a.W, b.W, t), Lerp(a.H, b.H, t));
	}

	/// <summary>平移矩形。</summary>
	public static LayoutRect Offset(LayoutRect r, double dx, double dy)
	{
		return new LayoutRect(r.X + dx, r.Y + dy, r.W, r.H);
	}

	/// <summary>
	/// FLIP 的反向偏移(单轴):元素上一次排在 <paramref name="oldPos"/>、身上还挂着值为 <paramref name="currentOffset"/>
	/// 的平移(可能是还没走完的旧动画),现在被排到 <paramref name="newPos"/>。
	/// 返回值 = 新位置上要先加的偏移,使它看起来仍在原处;之后把偏移动画到 0。
	/// </summary>
	public static double FlipOffset(double oldPos, double currentOffset, double newPos)
	{
		return oldPos + currentOffset - newPos;
	}

	/// <summary>偏移是否大到值得动画。</summary>
	public static bool NeedsAnimation(double offsetX, double offsetY)
	{
		return Math.Abs(offsetX) >= MinOffset || Math.Abs(offsetY) >= MinOffset;
	}

	/// <summary>像素 y → 行号(按行高表 + 行间 gap 累加;超过末行返回 末行+1;空表返回 0)。</summary>
	public static int RowAt(double[]? rows, double gap, double y)
	{
		if (rows == null || rows.Length == 0)
		{
			return 0;
		}
		double acc = 0.0;
		for (int k = 0; k < rows.Length; k++)
		{
			acc += rows[k] + gap;
			if (y < acc)
			{
				return k;
			}
		}
		return rows.Length;
	}

	/// <summary>第 index 行的行步长(行高 + gap);越界用末行兜底;空表用 <paramref name="fallback"/>。</summary>
	public static double RowStep(double[]? rows, double gap, int index, double fallback)
	{
		if (rows == null || rows.Length == 0)
		{
			return Math.Max(1.0, fallback);
		}
		int k = Math.Max(0, Math.Min(rows.Length - 1, index));
		return Math.Max(1.0, rows[k] + gap);
	}

	/// <summary>
	/// grid 拖动的落点格子:被拖卡片左上角(起手矩形 + 指针位移)取最近的列(夹在 0..cols-w),
	/// 行取"卡片上沿落在哪一行"(按手势开始时的行高快照算,不再用卡片自身高度当步长)。
	/// </summary>
	public static (int X, int Y) GridDropTarget(LayoutRect anchor, double dx, double dy, double columnStep, double[]? rows, double gap, int cols, int w)
	{
		double step = Math.Max(1.0, columnStep);
		int maxX = Math.Max(0, Math.Max(1, cols) - Math.Max(1, w));
		int x = (int)Math.Round((anchor.X + dx) / step);
		x = Math.Max(0, Math.Min(maxX, x));
		int y = Math.Max(0, RowAt(rows, gap, anchor.Y + dy));
		return (x, y);
	}

	/// <summary>
	/// 缩放时卡片的实时像素矩形:'e' 改宽、's' 改高('se' 两个都改);'w' / 'n' 同时移动左 / 上边。
	/// 宽高夹到下限,左 / 上边不越过对边。无画布边界时只保证不出现负坐标(像素坐标系)。
	/// </summary>
	public static LayoutRect ResizePreview(LayoutRect anchor, string? handle, double dx, double dy, double minW, double minH)
	{
		return ResizePreview(anchor, handle, dx, dy, minW, minH, double.PositiveInfinity, double.PositiveInfinity);
	}

	/// <summary>
	/// 同上,并把预览夹在画布 [0, canvasW] × [0, canvasH] 内(归一化画布传 1, 1 即为 0..1)。
	/// 西 / 北手柄拖过左上角时收敛到 0 并保持对边不动;东 / 南手柄拖出画布时收到边界。
	/// </summary>
	public static LayoutRect ResizePreview(LayoutRect anchor, string? handle, double dx, double dy, double minW, double minH, double canvasW, double canvasH)
	{
		string h = handle ?? "";
		double x = anchor.X;
		double y = anchor.Y;
		double w = anchor.W;
		double ht = anchor.H;
		double lowW = Math.Max(1.0, minW);
		double lowH = Math.Max(1.0, minH);
		if (h.IndexOf('e') >= 0)
		{
			w = Math.Max(lowW, anchor.W + dx);
		}
		if (h.IndexOf('w') >= 0)
		{
			double right = anchor.X + anchor.W;
			w = Math.Max(lowW, anchor.W - dx);
			x = right - w;
		}
		if (h.IndexOf('s') >= 0)
		{
			ht = Math.Max(lowH, anchor.H + dy);
		}
		if (h.IndexOf('n') >= 0)
		{
			double bottom = anchor.Y + anchor.H;
			ht = Math.Max(lowH, anchor.H - dy);
			y = bottom - ht;
		}
		// 预览是纯视觉瞬态,但也不该出现负坐标或越出画布:先把左/上边收到 0(对边保持),
		// 再把宽高收到画布剩余空间。
		if (x < 0.0)
		{
			w += x;
			x = 0.0;
		}
		if (y < 0.0)
		{
			ht += y;
			y = 0.0;
		}
		double maxW = ((Finite(canvasW) && canvasW > 0.0) ? canvasW : double.PositiveInfinity);
		double maxH = ((Finite(canvasH) && canvasH > 0.0) ? canvasH : double.PositiveInfinity);
		x = Math.Max(0.0, Math.Min(x, maxW));
		y = Math.Max(0.0, Math.Min(y, maxH));
		w = Math.Max(0.0, Math.Min(w, maxW - x));
		ht = Math.Max(0.0, Math.Min(ht, maxH - y));
		return new LayoutRect(x, y, w, ht);
	}

	/// <summary>拖下边记的像素高度:起始高 + 位移,按 4px 吸附,最小 56(与原来的规则相同)。</summary>
	public static double SnapPixelHeight(double startHeight, double dy)
	{
		return Math.Max(MinHeightPx, Math.Round((startHeight + dy) / HeightStep) * HeightStep);
	}

	/// <summary>canvas 磁性吸附的位移是否变了(吸上 / 松开 / 换了一条线):变了才给那一下短动画。</summary>
	public static bool SnapChanged(double prevDx, double prevDy, double dx, double dy)
	{
		return Math.Abs(prevDx - dx) >= MinOffset || Math.Abs(prevDy - dy) >= MinOffset;
	}

	// net48 没有 double.IsFinite
	private static bool Finite(double value)
	{
		return !double.IsNaN(value) && !double.IsInfinity(value);
	}

	private static double Clamp01(double v)
	{
		if (double.IsNaN(v) || v <= 0.0)
		{
			return 0.0;
		}
		return (v >= 1.0) ? 1.0 : v;
	}
}
