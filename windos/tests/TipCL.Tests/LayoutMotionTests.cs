using System;
using System.Collections.Generic;
using TipCL.Pages.Layout;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 网格拖动"丝滑化"的纯逻辑(LayoutMotion,零 WPF):
///   落点判定与改动前完全一致(左上角取最近列、指针所在行)、缩放预览矩形、像素高度 4px 吸附、
///   FLIP 反向偏移(含"旧动画还没走完又被让位"的接续)、ease-out 缓动与按毫秒计的进度(与帧率无关)。
/// 动画本身(TranslateTransform / CompositionTarget.Rendering)只能真机看,这里不假装覆盖。
/// </summary>
public class LayoutMotionTests
{
	// ————— 缓动 / 进度 —————

	[Fact]
	public void EaseOutCubicStartsFastEndsSlowAndClamps()
	{
		Assert.Equal(0.0, LayoutMotion.EaseOutCubic(0.0), 9);
		Assert.Equal(1.0, LayoutMotion.EaseOutCubic(1.0), 9);
		Assert.Equal(0.875, LayoutMotion.EaseOutCubic(0.5), 9);
		Assert.Equal(0.0, LayoutMotion.EaseOutCubic(-3.0), 9);
		Assert.Equal(1.0, LayoutMotion.EaseOutCubic(7.0), 9);
		Assert.Equal(0.0, LayoutMotion.EaseOutCubic(double.NaN), 9);
		// 单调递增,且前半程走得比线性快(ease-out)
		double prev = -1.0;
		for (int i = 0; i <= 100; i++)
		{
			double t = i / 100.0;
			double v = LayoutMotion.EaseOutCubic(t);
			Assert.True(v >= prev);
			if (t > 0.0 && t < 1.0)
			{
				Assert.True(v > t);
			}
			prev = v;
		}
	}

	[Fact]
	public void ProgressIsTimeBasedSoFrameRateDoesNotMatter()
	{
		Assert.Equal(0.5, LayoutMotion.Progress(85.0, 170.0), 9);
		Assert.Equal(1.0, LayoutMotion.Progress(500.0, 170.0), 9);
		Assert.Equal(0.0, LayoutMotion.Progress(-5.0, 170.0), 9);
		Assert.Equal(1.0, LayoutMotion.Progress(0.0, 0.0), 9);
		// 60Hz 与 240Hz 走到同一时刻,进度相同(只看经过的毫秒)
		double at60 = 0.0;
		for (int f = 0; f < 6; f++)
		{
			at60 += 1000.0 / 60.0;
		}
		double at240 = 0.0;
		for (int f = 0; f < 24; f++)
		{
			at240 += 1000.0 / 240.0;
		}
		Assert.Equal(LayoutMotion.Progress(at60, LayoutMotion.SettleMs), LayoutMotion.Progress(at240, LayoutMotion.SettleMs), 9);
	}

	[Fact]
	public void DurationsAreInTheRequestedRange()
	{
		Assert.InRange(LayoutMotion.FlipMs, 150.0, 180.0);
		Assert.InRange(LayoutMotion.SettleMs, 150.0, 180.0);
		Assert.InRange(LayoutMotion.SnapMs, 1.0, LayoutMotion.FlipMs);
	}

	[Fact]
	public void LerpRectMovesPositionAndSize()
	{
		LayoutRect a = new LayoutRect(0.0, 10.0, 100.0, 50.0);
		LayoutRect b = new LayoutRect(200.0, 30.0, 300.0, 150.0);
		Assert.Equal(a.ToString(), LayoutMotion.LerpRect(a, b, 0.0).ToString());
		Assert.Equal(b.ToString(), LayoutMotion.LerpRect(a, b, 1.0).ToString());
		LayoutRect mid = LayoutMotion.LerpRect(a, b, 0.5);
		Assert.Equal(100.0, mid.X, 9);
		Assert.Equal(20.0, mid.Y, 9);
		Assert.Equal(200.0, mid.W, 9);
		Assert.Equal(100.0, mid.H, 9);
	}

	// ————— FLIP —————

	[Fact]
	public void FlipOffsetKeepsTheBlockVisuallyInPlace()
	{
		// 上一次排在 100、没有动画;新位置 160 → 先挂 -60,看起来还在 100
		Assert.Equal(-60.0, LayoutMotion.FlipOffset(100.0, 0.0, 160.0), 9);
		Assert.True(LayoutMotion.NeedsAnimation(-60.0, 0.0));
		Assert.False(LayoutMotion.NeedsAnimation(0.2, -0.3));
	}

	[Fact]
	public void FlipRetargetMidAnimationContinuesFromWhatTheEyeSees()
	{
		// 第一次让位:0 → 100,偏移从 -100 动画到 0
		double offset1 = LayoutMotion.FlipOffset(0.0, 0.0, 100.0);
		Assert.Equal(-100.0, offset1, 9);
		// 走到一半(t=0.5)时当前平移值
		double current = offset1 * (1.0 - LayoutMotion.EaseOutCubic(0.5));
		double seen = 100.0 + current;
		// 又被让位到 200:新偏移把"正在进行的动画值"算进去,眼睛看到的位置不跳
		double offset2 = LayoutMotion.FlipOffset(100.0, current, 200.0);
		Assert.Equal(seen, 200.0 + offset2, 9);
	}

	// ————— 落点:与改动前的判定逐一相同 —————

	// 改动前 ApplyGridGesture 里的原公式(照抄),用来对照
	private static (int X, int Y) LegacyTarget(LayoutRect anchor, double dx, double dy, double columnStep, double[] rows, double gap, int cols, int w)
	{
		double left = anchor.X + dx;
		double top = anchor.Y + dy;
		int targetX = Math.Max(0, Math.Min(Math.Max(0, cols - w), (int)Math.Round(left / columnStep)));
		int targetY;
		if (rows.Length == 0)
		{
			targetY = 0;
		}
		else
		{
			double acc = 0.0;
			targetY = rows.Length;
			for (int k = 0; k < rows.Length; k++)
			{
				acc += rows[k] + gap;
				if (top < acc)
				{
					targetY = k;
					break;
				}
			}
		}
		return (targetX, Math.Max(0, targetY));
	}

	[Fact]
	public void GridDropTargetMatchesLegacyRuleExactly()
	{
		Random rnd = new Random(20261118);
		double[] rows = new double[5] { 120.0, 64.0, 300.0, 120.0, 41.0 };
		for (int i = 0; i < 2000; i++)
		{
			int cols = new[] { 2, 3, 4, 6, 12 }[rnd.Next(5)];
			int w = rnd.Next(1, cols + 1);
			double colStep = 40.0 + rnd.NextDouble() * 120.0;
			LayoutRect anchor = new LayoutRect(rnd.Next(0, cols) * colStep, rnd.NextDouble() * 600.0, colStep * w, 100.0);
			double dx = (rnd.NextDouble() - 0.5) * 1600.0;
			double dy = (rnd.NextDouble() - 0.5) * 1600.0;
			Assert.Equal(LegacyTarget(anchor, dx, dy, colStep, rows, 10.0, cols, w), LayoutMotion.GridDropTarget(anchor, dx, dy, colStep, rows, 10.0, cols, w));
		}
	}

	[Fact]
	public void GridDropTargetSnapsToNearestColumnAndPointerRow()
	{
		LayoutRect anchor = new LayoutRect(0.0, 0.0, 240.0, 100.0);
		double[] rows = new double[2] { 100.0, 50.0 };
		Assert.Equal((2, 0), LayoutMotion.GridDropTarget(anchor, 130.0, 0.0, 80.0, rows, 10.0, 12, 3));   // 1.625 列 → 2
		Assert.Equal((9, 0), LayoutMotion.GridDropTarget(anchor, 5000.0, 0.0, 80.0, rows, 10.0, 12, 3));  // 夹到 12-3
		Assert.Equal((0, 0), LayoutMotion.GridDropTarget(anchor, -300.0, -300.0, 80.0, rows, 10.0, 12, 3));
		Assert.Equal((0, 1), LayoutMotion.GridDropTarget(anchor, 0.0, 110.0, 80.0, rows, 10.0, 12, 3));   // 上沿进第 1 行
		Assert.Equal((0, 2), LayoutMotion.GridDropTarget(anchor, 0.0, 170.0, 80.0, rows, 10.0, 12, 3));   // 超过末行 = 末行+1
	}

	[Fact]
	public void RowAtAndRowStepUseSnapshotRowHeights()
	{
		double[] rows = new double[2] { 100.0, 50.0 };
		Assert.Equal(0, LayoutMotion.RowAt(rows, 10.0, 0.0));
		Assert.Equal(0, LayoutMotion.RowAt(rows, 10.0, 109.9));
		Assert.Equal(1, LayoutMotion.RowAt(rows, 10.0, 110.0));
		Assert.Equal(2, LayoutMotion.RowAt(rows, 10.0, 170.0));
		Assert.Equal(0, LayoutMotion.RowAt(Array.Empty<double>(), 10.0, 999.0));
		Assert.Equal(110.0, LayoutMotion.RowStep(rows, 10.0, 0, 120.0), 9);
		Assert.Equal(60.0, LayoutMotion.RowStep(rows, 10.0, 9, 120.0), 9);   // 越界用末行
		Assert.Equal(120.0, LayoutMotion.RowStep(null, 10.0, 0, 120.0), 9);
	}

	// ————— 缩放预览 / 像素高度 —————

	[Fact]
	public void ResizePreviewFollowsThePointerPixelByPixel()
	{
		LayoutRect anchor = new LayoutRect(100.0, 50.0, 300.0, 200.0);
		LayoutRect e = LayoutMotion.ResizePreview(anchor, "e", 37.0, 99.0, 50.0, 56.0);
		Assert.Equal(new LayoutRect(100.0, 50.0, 337.0, 200.0).ToString(), e.ToString());
		LayoutRect s = LayoutMotion.ResizePreview(anchor, "s", 99.0, -13.0, 50.0, 56.0);
		Assert.Equal(new LayoutRect(100.0, 50.0, 300.0, 187.0).ToString(), s.ToString());
		LayoutRect se = LayoutMotion.ResizePreview(anchor, "se", -1000.0, -1000.0, 50.0, 56.0);
		Assert.Equal(new LayoutRect(100.0, 50.0, 50.0, 56.0).ToString(), se.ToString()); // 夹到下限
		// 西 / 北侧手柄:对边不动
		LayoutRect nw = LayoutMotion.ResizePreview(anchor, "nw", 20.0, 30.0, 50.0, 56.0);
		Assert.Equal(400.0, nw.Right, 9);
		Assert.Equal(250.0, nw.Bottom, 9);
		Assert.Equal(280.0, nw.W, 9);
		Assert.Equal(170.0, nw.H, 9);
	}

	[Fact]
	public void PixelHeightSnapsTo4pxWithFloor56()
	{
		Assert.Equal(124.0, LayoutMotion.SnapPixelHeight(120.0, 3.0), 9);
		Assert.Equal(120.0, LayoutMotion.SnapPixelHeight(120.0, 1.0), 9);
		Assert.Equal(56.0, LayoutMotion.SnapPixelHeight(60.0, -50.0), 9);
		Assert.Equal(0.0, LayoutMotion.SnapPixelHeight(333.0, 17.0) % 4.0, 9);
	}

	[Fact]
	public void SnapChangedOnlyWhenTheMagnetEngagesReleasesOrSwitches()
	{
		Assert.False(LayoutMotion.SnapChanged(0.0, 0.0, 0.0, 0.0));
		Assert.True(LayoutMotion.SnapChanged(0.0, 0.0, 3.0, 0.0));   // 吸上
		Assert.False(LayoutMotion.SnapChanged(3.0, 0.0, 3.2, 0.0));  // 还是同一条线
		Assert.True(LayoutMotion.SnapChanged(3.0, 0.0, 0.0, 0.0));   // 松开
		Assert.True(LayoutMotion.SnapChanged(0.0, -2.0, 0.0, 2.0));  // 换线
	}
}
