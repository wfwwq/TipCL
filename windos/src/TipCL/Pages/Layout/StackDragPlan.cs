using System;
using System.Collections.Generic;

namespace TipCL.Pages.Layout;

/// <summary>
/// stack(纵向流)拖拽的"落位序号"纯逻辑(零 WPF,net8 测试宿主可直接断言):
/// 指针在列表内的 y → 被拖块应排到第几个可视位置。
///
/// 为什么单独抽出来:
///   1) 判定要带**滞回**(<see cref="Hysteresis"/>):指针正好停在相邻块中点附近时,
///      1~2px 的手抖会让块来回跳(旧实现直接和裸中点比较,就是这个观感);
///   2) 抽成纯函数之后,"给定位移序列 → 落位序号序列"可以在测试里逐帧断言,
///      不用起 WPF 也能证明它不会来回翻;
///   3) 判定公式与 <see cref="LayoutPageState.DragReorder"/> 完全同源
///      (都用 <see cref="LayoutPageState.MidpointY"/>),不引入第二套几何。
/// </summary>
public static class StackDragPlan
{
	/// <summary>换位滞回阈值(px):必须越过相邻块中点这么多才算换。</summary>
	public const double Hysteresis = 6.0;

	/// <summary>没有高度缓存时的兜底块高(与模型层 <see cref="LayoutPageState.MidpointY"/> 的默认一致)。</summary>
	public const double FallbackHeight = 120.0;

	/// <summary>
	/// 指针 y(相对列表顶部,含 gap)→ 被拖块应处的可视下标。
	/// <paramref name="visible"/> 用**当前**顺序(每换一次位调用方就更新它一次),
	/// <paramref name="currentIndex"/> 是被拖块此刻在 <paramref name="visible"/> 里的下标。
	/// </summary>
	public static int TargetIndex(IReadOnlyList<string>? visible, IReadOnlyDictionary<string, double>? heights, double gap, double pointerY, int currentIndex, double hysteresis = Hysteresis, double fallbackHeight = FallbackHeight)
	{
		if (visible == null || visible.Count == 0)
		{
			return Math.Max(0, currentIndex);
		}
		int index = Math.Max(0, Math.Min(visible.Count - 1, currentIndex));
		double h = (double.IsNaN(hysteresis) || hysteresis < 0.0) ? 0.0 : hysteresis;
		// 向上:越过上一块的中点 + 滞回余量才换(顺序反过来也一样,避免"刚好在中点上"来回跳)
		while (index > 0 && pointerY < LayoutPageState.MidpointY(visible, heights, gap, index - 1, fallbackHeight) - h)
		{
			index--;
		}
		// 向下:越过下一块的中点 - 滞回余量才换
		while (index < visible.Count - 1 && pointerY > LayoutPageState.MidpointY(visible, heights, gap, index + 1, fallbackHeight) + h)
		{
			index++;
		}
		return index;
	}

	/// <summary>
	/// 一次指针位移序列 → 每次移动后的可视下标序列(测试与"预测落位"共用)。
	/// 第 0 项对应 <paramref name="startIndex"/> 在 <paramref name="visible"/> 里的初始位置,
	/// 之后每项都是"这一次移动之后"的下标;调用方(以及测试)可以据此确认不会左右横跳。
	/// </summary>
	public static List<int> IndexSeries(IReadOnlyList<string>? visible, IReadOnlyDictionary<string, double>? heights, double gap, IReadOnlyList<double> pointerSeries, int startIndex, double hysteresis = Hysteresis, double fallbackHeight = FallbackHeight)
	{
		List<int> list = new List<int>();
		int index = startIndex;
		if (pointerSeries == null)
		{
			return list;
		}
		foreach (double y in pointerSeries)
		{
			index = TargetIndex(visible, heights, gap, y, index, hysteresis, fallbackHeight);
			list.Add(index);
		}
		return list;
	}

	/// <summary>
	/// 指针在可视区上下边缘时,自动滚动应该推多少(px):<paramref name="zone"/> 内越靠边越快,
	/// 返回 0 = 不在边缘区。抽成纯函数是为了让"边缘多深 → 多快"有可断言的口径。
	/// </summary>
	public static double EdgeScrollStep(double viewportHeight, double pointerInViewport, double zone, double maxStep)
	{
		if (viewportHeight <= 1.0 || zone <= 0.0 || maxStep <= 0.0 || double.IsNaN(pointerInViewport))
		{
			return 0.0;
		}
		double z = Math.Min(zone, viewportHeight / 3.0);
		double depth;
		if (pointerInViewport < z)
		{
			depth = (z - Math.Max(0.0, pointerInViewport)) / z;
		}
		else if (pointerInViewport > viewportHeight - z)
		{
			depth = (Math.Min(viewportHeight, pointerInViewport) - (viewportHeight - z)) / z;
		}
		else
		{
			return 0.0;
		}
		depth = Math.Max(0.0, Math.Min(1.0, depth));
		return (0.25 + 0.75 * depth) * maxStep;
	}
}