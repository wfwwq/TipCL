using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core.Launcher.Layout;
using TipCL.Pages.Layout;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// stack(纵向流)拖拽"跟手 + 滞回换位"的纯逻辑:
///   - <see cref="StackDragPlan.TargetIndex"/>:指针 y → 落位下标,带 ±H 滞回(杀中点抖动);
///   - <see cref="StackDragPlan.IndexSeries"/>:位移序列 → 下标序列,逐帧断言不来回跳;
///   - <see cref="StackDragPlan.EdgeScrollStep"/>:边缘自动滚动的"多深 → 多快";
///   - <see cref="LayoutPageState.MoveVisibleTo"/>:换位只动顺序(span / 坐标 / 外观不变)。
/// 只碰零 WPF 的类型,net8 宿主直接跑;动画本身(TranslateTransform / FLIP)只能实机看。
/// </summary>
public class StackDragPlanTests
{
	private static readonly string[] Four = new string[4] { "a", "b", "c", "d" };

	private static Dictionary<string, double> EqualHeights(double h)
	{
		return Four.ToDictionary((string id) => id, (string _) => h);
	}

	// 等高三块 100 + gap 10 → 中点 a=50, b=160, c=270, d=380
	private const double Gap = 10.0;

	[Fact]
	public void TargetIndexNeedsToCrossMidpointPlusHysteresis()
	{
		Dictionary<string, double> heights = EqualHeights(100.0);
		// 拖 a 往下:换到 1 号位要越过 b 的中点(160)+ 6
		Assert.Equal(0, StackDragPlan.TargetIndex(Four, heights, Gap, 165.0, 0));
		Assert.Equal(1, StackDragPlan.TargetIndex(Four, heights, Gap, 167.0, 0));
	}

	[Fact]
	public void JitterAroundAMidpointNeverFlipsBack()
	{
		Dictionary<string, double> heights = EqualHeights(100.0);
		// 指针在 b 的中点附近抖:第一次越过阈值换位后,必须一直停在 1 号位
		double[] series = new double[8] { 170.0, 158.0, 168.0, 155.0, 166.0, 152.0, 169.0, 160.0 };
		List<int> indexes = StackDragPlan.IndexSeries(Four, heights, Gap, series, 0);
		Assert.Equal(new int[8] { 1, 1, 1, 1, 1, 1, 1, 1 }, indexes.ToArray());
	}

	[Fact]
	public void DownwardSweepIsMonotoneAndReachesTheEnd()
	{
		Dictionary<string, double> heights = EqualHeights(100.0);
		List<double> sweep = new List<double>();
		for (int y = 0; y <= 520; y += 4)
		{
			sweep.Add(y);
		}
		int[] indexes = StackDragPlan.IndexSeries(Four, heights, Gap, sweep, 0).ToArray();
		Assert.Equal(0, indexes[0]);
		Assert.Equal(3, indexes[indexes.Length - 1]);
		for (int i = 1; i < indexes.Length; i++)
		{
			Assert.True(indexes[i] >= indexes[i - 1], "向下的位移序列里下标不能回头");
		}
	}

	[Fact]
	public void UpwardSweepIsMonotoneAndReachesTheTop()
	{
		Dictionary<string, double> heights = EqualHeights(100.0);
		List<double> sweep = new List<double>();
		for (int y = 520; y >= 0; y -= 4)
		{
			sweep.Add(y);
		}
		int[] indexes = StackDragPlan.IndexSeries(Four, heights, Gap, sweep, 3).ToArray();
		Assert.Equal(0, indexes[indexes.Length - 1]);
		for (int i = 1; i < indexes.Length; i++)
		{
			Assert.True(indexes[i] <= indexes[i - 1], "向上的位移序列里下标不能回头");
		}
	}

	[Fact]
	public void UnevenHeightsUseTheCachedHeightOfEachBlock()
	{
		Dictionary<string, double> heights = new Dictionary<string, double>
		{
			["a"] = 100.0,
			["b"] = 300.0,
			["c"] = 100.0,
			["d"] = 100.0
		};
		// b 很高:它的中点 = 100 + 10 + 150 = 260;越过 266 才换到 1 号位
		Assert.Equal(0, StackDragPlan.TargetIndex(Four, heights, Gap, 260.0, 0));
		Assert.Equal(1, StackDragPlan.TargetIndex(Four, heights, Gap, 267.0, 0));
		// 没有高度缓存时按 120 兜底(a=60, b=190)
		Assert.Equal(0, StackDragPlan.TargetIndex(Four, null, Gap, 185.0, 0));
		Assert.Equal(1, StackDragPlan.TargetIndex(Four, null, Gap, 197.0, 0));
	}

	[Fact]
	public void TargetIndexClampsToTheListBounds()
	{
		Dictionary<string, double> heights = EqualHeights(100.0);
		Assert.Equal(0, StackDragPlan.TargetIndex(Four, heights, Gap, -5000.0, 2));
		Assert.Equal(3, StackDragPlan.TargetIndex(Four, heights, Gap, 99999.0, 1));
		// 在 2 号位、指针 y=100:高于 b 的中点(160)但低于 a 的中点(50)+滞回 → 上移到 1 号,不再往上
		Assert.Equal(1, StackDragPlan.TargetIndex(Four, heights, Gap, 100.0, 2));
		// 空列表 / 空可视集合不炸
		Assert.Equal(0, StackDragPlan.TargetIndex(new string[0], heights, Gap, 10.0, 0));
		Assert.Equal(3, StackDragPlan.TargetIndex(null, heights, Gap, 10.0, 3));
	}

	[Fact]
	public void ZeroHysteresisMatchesTheLegacyMidpointRule()
	{
		Dictionary<string, double> heights = EqualHeights(100.0);
		// 关掉滞回时就是旧行为:越过中点 160 立刻换
		Assert.Equal(0, StackDragPlan.TargetIndex(Four, heights, Gap, 159.0, 0, 0.0));
		Assert.Equal(1, StackDragPlan.TargetIndex(Four, heights, Gap, 161.0, 0, 0.0));
	}

	[Fact]
	public void EdgeScrollStepIsZeroInsideAndGrowsTowardsTheEdge()
	{
		Assert.Equal(0.0, StackDragPlan.EdgeScrollStep(600.0, 300.0, 36.0, 18.0), 9);
		Assert.Equal(0.0, StackDragPlan.EdgeScrollStep(0.0, 10.0, 36.0, 18.0), 9);
		Assert.Equal(0.0, StackDragPlan.EdgeScrollStep(600.0, 10.0, 0.0, 18.0), 9);
		double shallow = StackDragPlan.EdgeScrollStep(600.0, 34.0, 36.0, 18.0);
		double deep = StackDragPlan.EdgeScrollStep(600.0, 2.0, 36.0, 18.0);
		Assert.True(shallow > 0.0);
		Assert.True(deep > shallow);
		Assert.True(deep <= 18.0 + 1e-9);
		// 下边缘对称
		Assert.Equal(deep, StackDragPlan.EdgeScrollStep(600.0, 598.0, 36.0, 18.0), 9);
	}

	// ————— 模型层:MoveVisibleTo 只动顺序 —————

	private static LayoutPageState NewState(params string[] ids)
	{
		List<LayoutBlockRef> refs = ids.Select((string id) => new LayoutBlockRef(id, true)).ToList();
		PageLayoutV2 page = new PageLayoutV2();
		for (int i = 0; i < ids.Length; i++)
		{
			page.Blocks.Add(new BlockLayout
			{
				Id = ids[i],
				Span = 12,
				Y = i
			});
		}
		return LayoutEngine.LoadState("stack-drag-test", page, refs);
	}

	private static string[] Order(LayoutPageState state)
	{
		return state.VisibleIds().ToArray();
	}

	[Fact]
	public void MoveVisibleToReordersWithoutTouchingGeometry()
	{
		LayoutPageState state = NewState("a", "b", "c", "d");
		Assert.True(state.MoveVisibleTo("a", 2));
		Assert.Equal(new string[4] { "b", "c", "a", "d" }, Order(state));
		// span / 坐标 / 外观语义不变(只改顺序)
		BlockLayout? moved = state.Page.Blocks.FirstOrDefault((BlockLayout b) => b.Id == "a");
		Assert.NotNull(moved);
		Assert.Equal(12, moved!.Span);
		// 四块都还在(换位不增不减)
		Assert.Equal(4, state.Page.Blocks.Count);
	}

	[Fact]
	public void MoveVisibleToIsIdempotentAtTheSameIndex()
	{
		LayoutPageState state = NewState("a", "b", "c", "d");
		Assert.False(state.MoveVisibleTo("b", 1));
		Assert.Equal(new string[4] { "a", "b", "c", "d" }, Order(state));
		// 越界目标被夹到两端,而不是丢块
		Assert.True(state.MoveVisibleTo("a", 99));
		Assert.Equal(new string[4] { "b", "c", "d", "a" }, Order(state));
	}

	[Fact]
	public void MoveVisibleToKeepsHiddenBlocksInPlace()
	{
		LayoutPageState state = NewState("a", "b", "c", "d");
		state.Hide("b");
		Assert.True(state.MoveVisibleTo("a", 1));
		Assert.Equal(new string[3] { "c", "a", "d" }, Order(state));
		// 隐藏项还在(不被换位顺手丢掉)
		Assert.Contains("b", state.Order);
		Assert.Contains("b", state.Hidden);
	}

	[Fact]
	public void RepeatedSwapsMatchTheModelDragReorderSemantics()
	{
		// 等价性检查:用 MoveVisibleTo 一步步换到末尾,和旧的 DragReorder(指针一路拖到最底)结果一致
		LayoutPageState a = NewState("a", "b", "c", "d");
		for (int target = 1; target <= 3; target++)
		{
			a.MoveVisibleTo("a", target);
		}
		LayoutPageState b = NewState("a", "b", "c", "d");
		b.DragReorder("a", 9999.0, null);
		Assert.Equal(Order(b), Order(a));
	}
}