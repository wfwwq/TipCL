// 来源: Windows 版参考仓库 src/TipCL/Pages/Layout/LayoutShellLogic.cs(分支 layout-shell-hooks @ fa1b3d7;
// 与分支 fix/r31-ui-audit @ 33b5fff 逐字相同,无差异——纯判定层,零 WPF、零界面依赖。
using System;
using System.Collections.Generic;

namespace TipCL.App.Pages.Layout;

// [layout-fix] 规格 §3-问题3 + §4-能力1 的**纯判定**部分(零 WPF、零界面依赖):
//   1) 滚轮路由:从鼠标下的最内层滚动容器往外找"第一个还能滚的";都不在滚动容器里就滚当前页最大可滚区域;
//   2) 右键目标宿主的选取顺序:最内层区块宿主 → 包含鼠标位置的最小宿主 → 页面里最大的宿主。
// WPF 侧(TipCL.Pages.Layout.LayoutShellHooks)只负责把可视树上的事实(能不能滚 / 矩形 / 面积)喂进来,
// 判定本身放在这里 —— 这样 net8 测试宿主能直接断言(与 LayoutEngine / LayoutEditSession 同一条纪律)。

/// <summary>
/// [layout-fix] 规格 §3-问题3 的滚轮路由判定。
/// 说明:调用方按"从鼠标下的最内层到最外层"的顺序收集滚动容器,问两件事:
///   ① 每个容器在当前方向还能不能滚(见 <see cref="CanScroll"/>);
///   ② 鼠标是否落在任何一个滚动容器里(即使它滚不动)。
/// </summary>
public static class LayoutWheelRouting
{
	/// <summary>判定"还能滚"的容差(像素):滚动条末尾往往会停在 scrollable - 0.x,不能因此误判成"还能滚"。</summary>
	public const double Epsilon = 0.5;

	/// <summary>
	/// 该容器在当前滚轮方向上还能不能滚。<paramref name="delta"/> = WPF 的 MouseWheelEventArgs.Delta
	/// (负 = 向下/向后滚,正 = 向上)。
	/// </summary>
	public static bool CanScroll(double offset, double scrollable, double delta)
	{
		if (double.IsNaN(offset) || double.IsNaN(scrollable) || scrollable <= Epsilon)
		{
			return false;
		}
		if (offset < 0.0)
		{
			offset = 0.0;
		}
		double max = scrollable;
		if (delta < 0.0)
		{
			// 向下滚:还有下半截没露出来才算"能滚"
			return offset < max - Epsilon;
		}
		// 向上滚:不是停在顶部才算"能滚"
		return offset > Epsilon;
	}

	/// <summary>
	/// 路由结果:<c>≥ 0</c> = 用链上这个下标的容器(0 = 最内层,交给原生逻辑保留平滑滚动;
	/// &gt; 0 = 最内层滚不动,转交外层第 N 个还能滚的);<see cref="NotScrollable"/> = 鼠标在某个滚动容器里,
	/// 但它(以及外层)都滚不动了 → 不接管;<see cref="OutsideAnyScroll"/> = 鼠标完全不在滚动容器里
	/// (页头 / 分段栏 / 编辑工具条)→ 滚当前页面里最大的那个可滚区域。
	/// </summary>
	public const int NotScrollable = -1;

	/// <summary>鼠标不在任何滚动容器里(见 <see cref="Route"/> 的说明)。</summary>
	public const int OutsideAnyScroll = -2;

	/// <summary>
	/// 按"从内到外"的可滚性链决定路由。<paramref name="chainCanScroll"/> 为 null / 空 = 鼠标不在任何滚动容器里。
	/// </summary>
	public static int Route(IReadOnlyList<bool>? chainCanScroll, bool insideScrollContainer)
	{
		if (chainCanScroll != null)
		{
			for (int i = 0; i < chainCanScroll.Count; i++)
			{
				if (chainCanScroll[i])
				{
					return i;
				}
			}
		}
		if (!insideScrollContainer)
		{
			return OutsideAnyScroll;
		}
		return NotScrollable;
	}

	/// <summary>滚后的目标偏移(夹 0..scrollable;用于把滚轮转交给普通 ScrollViewer 时的一次跳跃)。</summary>
	public static double OffsetAfter(double offset, double scrollable, double delta, double factor = 0.95)
	{
		double max = ((scrollable > 0.0) ? scrollable : 0.0);
		double target = ((offset < 0.0) ? 0.0 : offset) - delta * factor;
		if (target < 0.0)
		{
			return 0.0;
		}
		return ((target > max) ? max : target);
	}

	/// <summary>
	/// 鼠标不在滚动容器里时,选"当前页面里最大的那个可滚区域"。
	/// <paramref name="areas"/> = 每块候选的可视面积,<paramref name="canScroll"/> = 各自能不能滚(按同一方向)。
	/// 返回下标;没有可滚的返回 -1。面积相同取靠前的(确定性)。
	/// </summary>
	public static int Widest(IReadOnlyList<double>? areas, IReadOnlyList<bool>? canScroll)
	{
		if (areas == null)
		{
			return -1;
		}
		int best = -1;
		double bestArea = -1.0;
		for (int i = 0; i < areas.Count; i++)
		{
			if (canScroll != null && i < canScroll.Count && !canScroll[i])
			{
				continue;
			}
			double area = areas[i];
			if (double.IsNaN(area) || area <= 0.0)
			{
				continue;
			}
			if (area > bestArea)
			{
				bestArea = area;
				best = i;
			}
		}
		return best;
	}
}

/// <summary>
/// [layout-fix] 规格 §4-能力1:右键「自定义布局...」时目标区块宿主的选取顺序 ——
/// 鼠标所在元素最内层的区块宿主 → 包含鼠标位置的最小宿主 → 页面里最大的宿主。
/// </summary>
public static class LayoutHostPick
{
	/// <summary>命中不到任何宿主。</summary>
	public const int None = -1;

	/// <summary>
	/// 在候选宿主矩形里挑"包含鼠标位置的最小者"(面积最小;面积相同取靠前的)。
	/// <paramref name="typeRank"/> 可选:同类候选里再按类型排(数字小的优先,区块宿主本身 = 0)。
	/// 返回下标;没有包含鼠标位置的返回 <see cref="None"/>。
	/// </summary>
	public static int SmallestContaining(IReadOnlyList<LayoutRect>? rects, double x, double y, IReadOnlyList<int>? typeRank = null)
	{
		if (rects == null)
		{
			return None;
		}
		int best = None;
		double bestArea = double.MaxValue;
		int bestRank = int.MaxValue;
		for (int i = 0; i < rects.Count; i++)
		{
			LayoutRect rect = rects[i];
			if (rect.W <= 0.0 || rect.H <= 0.0 || !rect.Contains(x, y))
			{
				continue;
			}
			double area = rect.W * rect.H;
			int rank = ((typeRank != null && i < typeRank.Count) ? typeRank[i] : 0);
			if (rank < bestRank || (rank == bestRank && (best == None || area < bestArea)))
			{
				best = i;
				bestArea = area;
				bestRank = rank;
			}
		}
		return best;
	}

	/// <summary>挑页面里面积最大的宿主(面积相同取靠前的)。没有有效矩形返回 <see cref="None"/>。</summary>
	public static int Largest(IReadOnlyList<LayoutRect>? rects)
	{
		if (rects == null)
		{
			return None;
		}
		int best = None;
		double bestArea = 0.0;
		for (int i = 0; i < rects.Count; i++)
		{
			LayoutRect rect = rects[i];
			double area = rect.W * rect.H;
			if (double.IsNaN(area) || area <= 0.0)
			{
				continue;
			}
			if (best == None || area > bestArea)
			{
				best = i;
				bestArea = area;
			}
		}
		return best;
	}

	/// <summary>
	/// 完整选择顺序:最内层宿主(鼠标所在元素链上最近的区块宿主)优先;
	/// 其次"包含鼠标位置的最小宿主";最后"页面里最大的宿主"。
	/// <paramref name="innermost"/> = 最内层宿主下标(-1 表示鼠标不在任何区块宿主里)。
	/// </summary>
	public static int Resolve(int innermost, IReadOnlyList<LayoutRect>? rects, double x, double y)
	{
		if (innermost >= 0)
		{
			return innermost;
		}
		int containing = SmallestContaining(rects, x, y);
		if (containing != None)
		{
			return containing;
		}
		return Largest(rects);
	}
}
