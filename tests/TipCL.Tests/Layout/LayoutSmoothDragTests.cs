// 布局修复(Linux 第三批·任务 B):网格拖动吸附「丝滑」化。
//   1 纯函数(LayoutMotion):缓动 / 进度 / FLIP 位移 / 落点 / 像素改大小 / 吸附高度 / 尺寸文本 / 磁吸状态;
//   2 补间驱动(LayoutTween):同 key 新动画先取消旧动画、界面动画关闭时一步到位;
//   3 真指针手势(无头窗口):拖动中卡片只靠平移跟手、浮起 + 占位框、落点不变不重排、让位块走 FLIP、
//     松手立即提交且从手里的位置落位;改大小时像素跟手 + 尺寸提示、占位框是吸附尺寸、松手落位。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using TipCL.App.Common;
using TipCL.App.Pages.Layout;
using TipCL.Core.Launcher.Layout;
using TipCL.Core.Layout;
using TipCL.Tests.App;
using Xunit;

[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class LayoutMotionPureTests
{
	[Fact]
	public void EaseOutCubicIsMonotonicFromZeroToOneAndFrontLoaded()
	{
		Assert.Equal(0.0, LayoutMotion.EaseOutCubic(0.0), 9);
		Assert.Equal(1.0, LayoutMotion.EaseOutCubic(1.0), 9);
		Assert.Equal(0.0, LayoutMotion.EaseOutCubic(-3.0), 9);
		Assert.Equal(1.0, LayoutMotion.EaseOutCubic(7.0), 9);
		double prev = 0.0;
		for (int i = 1; i <= 20; i++)
		{
			double v = LayoutMotion.EaseOutCubic(i / 20.0);
			Assert.True(v >= prev);
			prev = v;
		}
		// ease-out:前半程走完的比一半多(起步快、收尾慢)
		Assert.True(LayoutMotion.EaseOutCubic(0.5) > 0.8);
	}

	[Fact]
	public void ProgressClampsAndZeroDurationIsInstant()
	{
		Assert.Equal(0.5, LayoutMotion.Progress(80.0, 160.0), 9);
		Assert.Equal(1.0, LayoutMotion.Progress(500.0, 160.0), 9);
		Assert.Equal(0.0, LayoutMotion.Progress(-5.0, 160.0), 9);
		Assert.Equal(1.0, LayoutMotion.Progress(0.0, 0.0), 9);
		Assert.InRange(LayoutMotion.FlipMs, 150.0, 180.0);
		Assert.InRange(LayoutMotion.DropMs, 150.0, 180.0);
	}

	[Fact]
	public void FlipOffsetInvertsTheJumpAndRemainingDecaysToZero()
	{
		Vector from = LayoutMotion.FlipOffset(100.0, 40.0, 220.0, 10.0);
		Assert.Equal(-120.0, from.X, 9);
		Assert.Equal(30.0, from.Y, 9);
		// 不足半像素不做动画
		Assert.Equal(default(Vector), LayoutMotion.FlipOffset(10.2, 5.0, 10.0, 5.3));
		Assert.Equal(from, LayoutMotion.FlipRemaining(from, 0.0));
		Assert.Equal(default(Vector), LayoutMotion.FlipRemaining(from, 1.0));
		Vector mid = LayoutMotion.FlipRemaining(from, 0.5);
		Assert.True(Math.Abs(mid.X) < Math.Abs(from.X) * 0.2, "ease-out 半程已经收掉大半");
	}

	[Fact]
	public void LerpRectInterpolatesPositionAndSize()
	{
		LayoutRect r = LayoutMotion.LerpRect(new LayoutRect(0, 0, 100, 50), new LayoutRect(20, 10, 200, 150), 0.5);
		Assert.Equal(10.0, r.X, 9);
		Assert.Equal(5.0, r.Y, 9);
		Assert.Equal(150.0, r.W, 9);
		Assert.Equal(100.0, r.H, 9);
	}

	[Fact]
	public void GridDropTargetKeepsTheOldRule()
	{
		double[] rows = { 100.0, 60.0, 80.0 };
		// 卡片左上角(x=0)右移 2.6 列 → 最近列 3;指针 y=130 落在第 1 行(100 + 间距 10 之后)
		(int x, int y) = LayoutMotion.GridDropTarget(0.0, 2.6 * 70.0, 70.0, 12, 4, rows, 130.0, 10.0);
		Assert.Equal(3, x);
		Assert.Equal(1, y);
		// 列钳在 [0, 列数 − 宽]
		Assert.Equal(8, LayoutMotion.GridDropTarget(0.0, 5000.0, 70.0, 12, 4, rows, 0.0, 10.0).X);
		Assert.Equal(0, LayoutMotion.GridDropTarget(140.0, -5000.0, 70.0, 12, 4, rows, 0.0, 10.0).X);
		// 越过最后一行 = 下一行(允许往下拖出新行)
		Assert.Equal(3, LayoutMotion.GridDropTarget(0.0, 0.0, 70.0, 12, 4, rows, 900.0, 10.0).Y);
	}

	[Fact]
	public void ResizePixelsFollowsTheHandleAndClamps()
	{
		LayoutRect start = new LayoutRect(10, 20, 200, 100);
		LayoutRect e = LayoutMotion.ResizePixels(start, "e", 37.0, 999.0, 50.0, 56.0, 0.0);
		Assert.Equal(237.0, e.W, 9);
		Assert.Equal(100.0, e.H, 9); // 只拉右边:高不变
		LayoutRect s = LayoutMotion.ResizePixels(start, "s", 999.0, -80.0, 50.0, 56.0, 0.0);
		Assert.Equal(200.0, s.W, 9);
		Assert.Equal(56.0, s.H, 9); // 不低于最小高
		LayoutRect se = LayoutMotion.ResizePixels(start, "se", 5000.0, 30.0, 50.0, 56.0, 400.0);
		Assert.Equal(400.0, se.W, 9); // 不超出画布右沿
		Assert.Equal(130.0, se.H, 9);
		Assert.Equal(10.0, se.X, 9);
		Assert.Equal(20.0, se.Y, 9);
		Assert.Equal(50.0, LayoutMotion.ResizePixels(start, "e", -1000.0, 0.0, 50.0, 56.0, 0.0).W, 9);
	}

	[Fact]
	public void SnapHeightPxRoundsToStepWithMinimum()
	{
		Assert.Equal(184.0, LayoutMotion.SnapHeightPx(150.0, 33.0, 8.0, 56.0), 9);
		Assert.Equal(56.0, LayoutMotion.SnapHeightPx(150.0, -300.0, 8.0, 56.0), 9);
		Assert.Equal(183.0, LayoutMotion.SnapHeightPx(150.0, 33.0, 0.0, 56.0), 9);
	}

	[Fact]
	public void SizeLabelAndSnapChanged()
	{
		Assert.Equal("640 × 221", LayoutMotion.SizeLabel(639.6, 220.6));
		Assert.False(LayoutMotion.SnapChanged(false, 0, 0, false, 0, 0));
		Assert.True(LayoutMotion.SnapChanged(false, 0, 0, true, 3, 0));
		Assert.True(LayoutMotion.SnapChanged(true, 3, 0, false, 0, 0));
		Assert.True(LayoutMotion.SnapChanged(true, 3, 0, true, -2, 0));
		Assert.False(LayoutMotion.SnapChanged(true, 3, 0, true, 3.2, 0));
	}

	[Fact]
	public void TweenNewAnimationCancelsTheOldOneOnTheSameKey() => TestApp.Run(() =>
	{
		Border anchor = new Border();
		Window window = new Window { Width = 200, Height = 200, Content = anchor };
		window.Show();
		bool motion = Motion.Enabled;
		Motion.Enabled = true;
		try
		{
			object key = new object();
			List<double> first = new List<double>();
			bool firstDone = false;
			LayoutTween.Start(anchor, key, 10000.0, p => first.Add(p), () => firstDone = true);
			Assert.True(LayoutTween.IsRunning(key));
			List<double> second = new List<double>();
			LayoutTween.Start(anchor, key, 10000.0, p => second.Add(p));
			int before = first.Count;
			LayoutTween.Cancel(key, true);
			// 旧动画:被新动画取消后既不再推进,也不回调 done;新动画被 Cancel(complete) 推到终点
			Assert.Equal(before, first.Count);
			Assert.False(firstDone);
			Assert.Equal(1.0, second.Last(), 9);
			Assert.False(LayoutTween.IsRunning(key));

			// 界面动画关闭:一步到位
			Motion.Enabled = false;
			List<double> instant = new List<double>();
			LayoutTween.Start(anchor, key, 10000.0, p => instant.Add(p));
			Assert.Equal(new[] { 1.0 }, instant.ToArray());
			Assert.False(LayoutTween.IsRunning(key));
		}
		finally
		{
			Motion.Enabled = motion;
			window.Close();
		}
	});
}

/// <summary>真指针手势(无头窗口 + HeadlessWindowExtensions.MouseDown/Move/Up),夹具口径同 LayoutEditorUiTests。</summary>
[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class LayoutSmoothDragUiTests
{
	private sealed class Store : IDisposable
	{
		public string Dir { get; } = Path.Combine(Path.GetTempPath(), "tipcl_smooth_" + Guid.NewGuid().ToString("N"));

		public Store()
		{
			Directory.CreateDirectory(Dir);
			LayoutStoreV2.DirOverride = Dir;
			LayoutStoreV2.V1Bridge = new LinuxV1Bridge();
			PageLayoutStore.DirOverride = Dir;
			LayoutStoreV2.ResetCache();
			LayoutStoreV2.RefreshV1Cache();
			LayoutVariantHost.Reset();
		}

		public void Dispose()
		{
			LayoutStoreV2.DirOverride = null;
			LayoutStoreV2.V1Bridge = null;
			PageLayoutStore.DirOverride = null;
			LayoutStoreV2.ResetCache();
			LayoutVariantHost.Reset();
			try
			{
				Directory.Delete(Dir, true);
			}
			catch (Exception)
			{
			}
		}
	}

	private sealed class Host : IDisposable
	{
		private readonly bool _motion;

		private readonly Func<long> _clock;

		public Window Window { get; }

		public LayoutEditor Editor { get; }

		public Host(string pageId, PageLayoutV2 page, params string[] ids)
		{
			_motion = Motion.Enabled;
			Motion.Enabled = true;
			// AI2-stability:冻住补间时钟 —— 无头输入内部会 RunJobs,用墙钟时机器一卡动画就被推走,
			// 「松手 / Esc 瞬间」的位移断言偶发失败(基线单跑本类 10 次失败 1 次)。动画只在 FinishAnimations 里推到终点。
			_clock = LayoutTween.Clock;
			long frozen = System.Diagnostics.Stopwatch.GetTimestamp();
			LayoutTween.Clock = () => frozen;
			BlockSpec[] specs = ids.Select(id => new BlockSpec { Id = id, Title = id, Content = new Border { Height = 60.0 } }).ToArray();
			Editor = new LayoutEditor(pageId, specs, new Thickness(0.0), page);
			Window = new Window { Width = 900.0, Height = 600.0, Content = Editor.Root };
			Window.Show();
			Window.UpdateLayout();
			Editor.EnterEdit();
			Window.UpdateLayout();
			Window.CaptureRenderedFrame();
		}

		public GridCanvasPanel Panel => Find<GridCanvasPanel>(Editor.Root, _ => true)!;

		public GuideLayer Guide => Find<GuideLayer>(Editor.Root, _ => true)!;

		public LayoutCard Card(string id) => Find<LayoutCard>(Editor.Root, c => c.Spec.Id == id)!;

		public Point At(Visual v, double fx, double fy) =>
			v.TranslatePoint(new Point(v.Bounds.Width * fx, v.Bounds.Height * fy), Window) ?? throw new InvalidOperationException();

		public void Dispose()
		{
			Panel.FinishAnimations();
			Motion.Enabled = _motion;
			LayoutTween.Clock = _clock;
			Window.Close();
		}
	}

	private static T? Find<T>(Visual? root, Func<T, bool> match) where T : Visual
	{
		if (root == null)
		{
			return null;
		}
		if (root is T typed && match(typed))
		{
			return typed;
		}
		foreach (Visual child in root.GetVisualChildren())
		{
			T? hit = Find(child, match);
			if (hit != null)
			{
				return hit;
			}
		}
		return null;
	}

	private static PageLayoutV2 GridPage(params (string Id, int X, int Y, int W, int H)[] blocks)
	{
		PageLayoutV2 page = new PageLayoutV2 { Mode = LayoutSchema.ModeGrid, Cols = 12 };
		int z = 0;
		foreach ((string id, int x, int y, int w, int h) in blocks)
		{
			page.Blocks.Add(new BlockLayout { Id = id, X = x, Y = y, W = w, H = h, Z = z++ });
		}
		return page;
	}

	[Fact]
	public void DraggingFollowsPointerByTranslateAndOnlyReflowsWhenTheDropCellChanges()
	{
		using Store store = new Store();
		TestApp.Run(() =>
		{
			using Host host = new Host("smooth:move", GridPage(("a", 0, 0, 4, 1), ("b", 4, 0, 4, 1)), "a", "b");
			GridCanvasPanel panel = host.Panel;
			double step = panel.ColumnWidthValue + panel.Gap;
			LayoutCard a = host.Card("a");
			LayoutCard b = host.Card("b");
			Rect startA = a.Bounds;
			Rect startB = b.Bounds;
			Point p0 = host.At(a, 0.5, 0.75);

			host.Window.MouseDown(p0, MouseButton.Left, RawInputModifiers.LeftMouseButton);
			// 第一段:移 0.3 列 —— 越过拖动阈值,但落点仍是原列
			host.Window.MouseMove(new Point(p0.X + step * 0.3, p0.Y), RawInputModifiers.LeftMouseButton);
			host.Window.UpdateLayout();
			int reflows = panel.ReflowCount;
			Assert.True(panel.Floating.ContainsKey("a"), "拖起来之后被拖块应当浮起");
			Assert.True(panel.ShowPlaceholder);
			// 卡片的排布矩形不变(没有每帧重新布局),跟手全靠平移
			Assert.Equal(startA, a.Bounds);
			Vector off = LayoutMotion.GetOffset(a);
			Assert.Equal(step * 0.3, off.X, 3);
			Assert.Equal(0.0, off.Y, 3);

			// 第二段:再移一点(仍在同一落点)—— 只改平移,不重排
			host.Window.MouseMove(new Point(p0.X + step * 0.4, p0.Y), RawInputModifiers.LeftMouseButton);
			host.Window.UpdateLayout();
			Assert.Equal(reflows, panel.ReflowCount);
			Assert.Equal(step * 0.4, LayoutMotion.GetOffset(a).X, 3);

			// 第三段:移到 b 的格子 —— 落点变了:b 让位(左右互换)并且走 FLIP(先被放回原处再滑过去)
			host.Window.MouseMove(new Point(p0.X + step * 4.0, p0.Y), RawInputModifiers.LeftMouseButton);
			host.Window.UpdateLayout();
			Assert.True(panel.ReflowCount > reflows);
			Assert.Equal(0, panel.CellOf("b")!.X);
			Assert.Equal(4, panel.CellOf("a")!.X);
			Vector flip = LayoutMotion.GetOffset(b);
			Assert.True(Math.Abs(b.Bounds.X + flip.X - startB.X) < 1.0, "FLIP 起点:让位块看起来还在原位,实际 = " + (b.Bounds.X + flip.X));
			// 占位框 = 被拖块的槽位矩形(落点),浮起的卡片仍排在起手矩形上
			LayoutRect place = panel.RectOf("a")!.Value;
			Assert.Equal(startB.X, place.X, 1.0 /*tolerance: 1px layout rounding*/);
			Assert.Equal(startA, a.Bounds);

			// 松手:状态立即提交;卡片从手里的位置落进占位框(FLIP 起点 = 指针下的位置)
			host.Window.MouseUp(new Point(p0.X + step * 4.0, p0.Y), MouseButton.Left, RawInputModifiers.None);
			host.Window.UpdateLayout();
			Assert.Equal(4.0, host.Editor.State.Page.Blocks.First(x => x.Id == "a").X, 6);
			Assert.Empty(panel.Floating);
			Assert.False(panel.ShowPlaceholder);
			Vector land = LayoutMotion.GetOffset(a);
			Assert.True(Math.Abs(a.Bounds.X + land.X - (startA.X + step * 4.0)) < 1.0,
				"落位动画起点应是松手时卡片所在的像素位置,实际 = " + (a.Bounds.X + land.X));

			// 动画推到终点后不留平移(静止态与改动前逐像素一致)
			panel.FinishAnimations();
			Assert.Null(a.RenderTransform);
			Assert.Null(b.RenderTransform);
		});
	}

	[Fact]
	public void ResizingFollowsPixelsShowsSizeAndSnapsOnRelease()
	{
		using Store store = new Store();
		TestApp.Run(() =>
		{
			using Host host = new Host("smooth:resize", GridPage(("a", 0, 0, 4, 1), ("b", 0, 1, 4, 1)), "a", "b");
			GridCanvasPanel panel = host.Panel;
			double step = panel.ColumnWidthValue + panel.Gap;
			LayoutCard a = host.Card("a");
			Rect startA = a.Bounds;
			Border east = Find<Border>(a, x => (x.Tag as string) == "e" && x.IsVisible)!;
			Point p0 = host.At(east, 0.5, 0.5);

			host.Window.MouseDown(p0, MouseButton.Left, RawInputModifiers.LeftMouseButton);
			host.Window.MouseMove(new Point(p0.X + step * 0.7, p0.Y), RawInputModifiers.LeftMouseButton);
			host.Window.MouseMove(new Point(p0.X + step * 1.3, p0.Y), RawInputModifiers.LeftMouseButton);
			host.Window.UpdateLayout();

			// 像素跟手:宽 = 起手宽 + 指针位移(不吸附)
			Assert.Equal(startA.Width + step * 1.3, a.Bounds.Width, 1.0 /*tolerance: 1px layout rounding*/);
			{
				// v3(§1.A.4):宽度只能是整数列 —— 拖右边缘时尺寸标签是「N / 12 列」(效果图 .rz b),不再是「宽 × 高」像素
				// (只拖下边时仍显示像素尺寸,见 LayoutEditor 改大小分支)。1.3 列 → 取整 1 列 ⇒ 4 + 1 = 5。
				Assert.Equal("5 / 12 列", host.Guide.SizeText);
				Assert.DoesNotContain("×", host.Guide.SizeText);
			}
			// 占位框 = 吸附后的尺寸(1.3 列 → 1 列)
			Assert.Equal(5, panel.CellOf("a")!.W);
			Assert.Equal(startA.Width + step, panel.RectOf("a")!.Value.W, 1.0 /*tolerance: 1px layout rounding*/);

			host.Window.MouseUp(new Point(p0.X + step * 1.3, p0.Y), MouseButton.Left, RawInputModifiers.None);
			host.Window.UpdateLayout();
			Assert.Equal(5.0, host.Editor.State.Page.Blocks.First(x => x.Id == "a").W, 6);
			Assert.Equal("", host.Guide.SizeText);
			// 落位动画起点 = 松手时的像素尺寸(还没缩回吸附尺寸)
			Assert.Equal(startA.Width + step * 1.3, a.Bounds.Width, 1.0 /*tolerance: 1px layout rounding*/);
			panel.FinishAnimations();
			host.Window.UpdateLayout();
			Assert.Equal(startA.Width + step, a.Bounds.Width, 1.0 /*tolerance: 1px layout rounding*/);
		});
	}

	[Fact]
	public void EscapeDuringSmoothDragSlidesBackAndPersistsNothing()
	{
		using Store store = new Store();
		TestApp.Run(() =>
		{
			using Host host = new Host("smooth:esc", GridPage(("a", 0, 0, 4, 1), ("b", 4, 0, 4, 1)), "a", "b");
			GridCanvasPanel panel = host.Panel;
			double step = panel.ColumnWidthValue + panel.Gap;
			LayoutCard a = host.Card("a");
			Point p0 = host.At(a, 0.5, 0.75);
			host.Window.MouseDown(p0, MouseButton.Left, RawInputModifiers.LeftMouseButton);
			host.Window.MouseMove(new Point(p0.X + step * 4.0, p0.Y), RawInputModifiers.LeftMouseButton);
			host.Window.UpdateLayout();
			host.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
			host.Window.UpdateLayout();

			Assert.Equal(0.0, host.Editor.State.Page.Blocks.First(x => x.Id == "a").X, 6);
			Assert.Empty(panel.Floating);
			// 从手里的位置滑回原位(FLIP 起点 = 当时看起来的位置)
			Assert.True(LayoutMotion.GetOffset(a).X > step * 3.0);
			Assert.False(File.Exists(Path.Combine(store.Dir, LayoutStoreV2.FileName)));
			host.Window.MouseUp(new Point(p0.X + step * 4.0, p0.Y), MouseButton.Left, RawInputModifiers.None);
		});
	}
}
