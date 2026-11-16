// R32-MED:自定义布局 v2「可视化底层件」单元测试(测量 / 排布 / 附加属性 / 自绘层),mac(Avalonia)树落地件。
//   底稿(逐行) = Linux 同层落地件 wt-linux-layout @7267d03 的
//                 tests/TipCL.Tests/Layout/LayoutGridCanvasTests.cs(1129 行,36 条)。
//   对象        = mac 树 src/TipCL.App/Pages/ 的 GridCanvasPanel.cs / GuideLayer.cs / FitBox.cs /
//                 LayoutFill.cs / FillPanel.cs;对照的 Windows 源 = 参考仓库 tests/TipCL.Tests/ 的
//                 {LayoutCanvasTests,LayoutRowClampTests}.cs 里**没覆盖**的渲染那层。
//
// 与 Linux 底稿的机械差异(断言 / 方法名 / 数字一字未改):
//   T-1 文件头:Linux 底稿的中文说明与 using 段 -> 本头注与 mac 的 using 段。
//   T-2 namespace:底稿无 namespace(全局,Linux 树单程序集约定)=> mac 的 namespace TipCL.Tests;
//       (TipCL.Tests.csproj 的 RootNamespace=TipCL.Tests,与本目录既有测试一致)。
//   T-3 using 增减:去掉底稿的 TipCL.App.Pages.Layout / TipCL.Core.Layout / TipCL.Tests.App;
//       加 TipCL.Pages。v1 模型 PageLayoutDoc / BlockState 在 mac 树位于 TipCL.Pages
//       (src/TipCL.App/Pages/PageBlock.cs,与 Linux 的 TipCL.Core.Layout 同源逐字实现)=> using TipCL.Core.Layout 不搬;
//       TipCL.Core.Launcher.Layout(LayoutV2 / PageLayoutV2 / BlockLayout / LayoutSchema /
//       LayoutStoreV2)保留;Avalonia.* 与 Xunit 原样保留。
//   T-4 无头会话门面:TestApp.Run( -> AvaloniaSession.Run(mac 的无头会话类,同测试程序集
//       AvaloniaSession.cs;Linux 侧对等物 = tests/TipCL.Tests/App/TestApp.cs 的 TestApp)。
//   T-5 v1 注入缝:LinuxV1Bridge -> R32McV1Bridge —— mac 树没有 Linux 的生产接线 PagesLayoutV1Bridge
//       那一层,改用同测试程序集里的等价实现(R32McLayoutV2Fixtures.cs:20 的 internal sealed class
//       R32McV1Bridge;语义逐条照抄 Linux 的 LayoutV1BridgeLinux.cs / LinuxV1Bridge)。
//   T-6 临时目录:Path.GetTempPath() -> TestHome.Root(mac 红线:测试临时文件必须落在仓库内,
//       R32-BRIEF §4;仍各自拼 Guid 子目录,隔离语义与实际路径长度无关)。
//   T-7 类名:加包号前缀 R32Mc,方法名 / 断言 / 数字一字未改。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TipCL.Core.Launcher.Layout;
using TipCL.Pages;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 布局画布底层件的测量/排布测试(GridCanvasPanel / GuideLayer / FitBox / LayoutFill / FillPanel)。
/// </summary>
public class R32McLayoutGridCanvasTests
{
	// ————— 测试替身与夹具 —————

	/// <summary>块卡片替身:实现 <see cref="ILayoutCanvasChild"/>(块 id + 折叠态),测量结果固定。</summary>
	private sealed class FakeCard : Control, ILayoutCanvasChild
	{
		private readonly Size _natural;

		public FakeCard(string id, double width, double height, bool folded = false)
		{
			BlockId = id;
			IsFolded = folded;
			_natural = new Size(width, height);
		}

		public string? BlockId { get; }

		public bool IsFolded { get; }

		protected override Size MeasureOverride(Size available)
		{
			return _natural;
		}
	}

	/// <summary>只用 Tag 报块 id 的替身(源文件里 id 解析的兜底分支)。</summary>
	private sealed class TagCard : Control
	{
		private readonly Size _natural;

		public TagCard(string id, double width, double height)
		{
			Tag = id;
			_natural = new Size(width, height);
		}

		protected override Size MeasureOverride(Size available)
		{
			return _natural;
		}
	}

	/// <summary>把测试体放到无头 UI 线程上跑(控件构造/测量/排布都有线程亲和)。</summary>
	private static void Ui(Action body)
	{
		AvaloniaSession.Run(body);
	}

	private static PageLayoutV2 Grid(int cols, params string[] ids)
	{
		PageLayoutV2 page = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeGrid,
			Cols = cols
		};
		foreach (string id in ids)
		{
			page.Blocks.Add(new BlockLayout
			{
				Id = id
			});
		}
		return page;
	}

	private static PageLayoutV2 Canvas(params string[] ids)
	{
		PageLayoutV2 page = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeCanvas
		};
		foreach (string id in ids)
		{
			page.Blocks.Add(new BlockLayout
			{
				Id = id
			});
		}
		return page;
	}

	/// <summary>照宿主的用法装配:SetLayout → 加子元素 → Measure → Arrange(排到 DesiredSize 的尺寸)。</summary>
	private static GridCanvasPanel Mount(string mode, int cols, double rowH, double gap, PageLayoutV2 page, Size available, params Control[] children)
	{
		GridCanvasPanel panel = new GridCanvasPanel();
		panel.SetLayout(mode, cols, rowH, gap, page);
		foreach (Control child in children)
		{
			panel.Children.Add(child);
		}
		panel.Measure(available);
		panel.Arrange(new Rect(0.0, 0.0, available.Width, panel.DesiredSize.Height));
		return panel;
	}

	/// <summary>真渲染到离屏位图并读回像素(Bgra8888 预乘 ⇒ 只断言 alpha)。</summary>
	private static Color[] Render(Visual visual, int width, int height)
	{
		using RenderTargetBitmap bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96.0, 96.0));
		bitmap.Render(visual);
		byte[] buffer = new byte[width * height * 4];
		GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
		try
		{
			bitmap.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), buffer.Length, width * 4);
		}
		finally
		{
			handle.Free();
		}
		Color[] colors = new Color[width * height];
		for (int i = 0; i < colors.Length; i++)
		{
			colors[i] = Color.FromArgb(buffer[i * 4 + 3], buffer[i * 4 + 2], buffer[i * 4 + 1], buffer[i * 4]);
		}
		return colors;
	}

	private static int Painted(Color[] pixels)
	{
		return pixels.Count(delegate(Color c)
		{
			return c.A != 0;
		});
	}

	// ————— grid:两趟测量(行高) —————

	[Fact]
	public void GridPanelLaysChildrenIntoColumnsAndRowsWithGap()
	{
		Ui(delegate
		{
			// 2 列 / 3 块(a、b 在第 0 行,c 折到第 1 行);列宽 = (300 - 10) / 2 = 145
			PageLayoutV2 page = Grid(2, "a", "b", "c");
			FakeCard a = new FakeCard("a", 10.0, 10.0);
			FakeCard b = new FakeCard("b", 10.0, 10.0);
			FakeCard c = new FakeCard("c", 10.0, 10.0);
			GridCanvasPanel panel = Mount(LayoutSchema.ModeGrid, 2, 0.0, 10.0, page, new Size(300.0, 1000.0), a, b, c);

			Assert.Equal(145.0, panel.ColumnWidthValue, 9);
			Assert.Equal(2, panel.Rows);
			Assert.False(panel.IsCanvas);
			// 需求高 10 < 块最小高(缺省 120)⇒ 行高抬到 120;两行都是
			Assert.Equal(new double[2] { 120.0, 120.0 }, panel.RowHeightValues);
			// 内容总高 = 120 + 10(gap) + 120
			Assert.Equal(300.0, panel.DesiredSize.Width, 9);
			Assert.Equal(250.0, panel.DesiredSize.Height, 9);
			// 子元素矩形:严格按"列宽 × 网格单位 + gap"
			Assert.Equal(0.0, a.Bounds.X, 9);
			Assert.Equal(0.0, a.Bounds.Y, 9);
			Assert.Equal(145.0, a.Bounds.Width, 9);
			Assert.Equal(120.0, a.Bounds.Height, 9);
			Assert.Equal(155.0, b.Bounds.X, 9);
			Assert.Equal(0.0, b.Bounds.Y, 9);
			Assert.Equal(145.0, b.Bounds.Width, 9);
			Assert.Equal(0.0, c.Bounds.X, 9);
			Assert.Equal(130.0, c.Bounds.Y, 9);
			Assert.Equal(145.0, c.Bounds.Width, 9);
			// 排布结果(供辅助线/命中用)与子元素 Bounds 一一对应
			Assert.Equal(a.Bounds.X, panel.RectValues[0].X, 9);
			Assert.Equal(b.Bounds.X, panel.RectValues[1].X, 9);
			Assert.Equal(c.Bounds.Y, panel.RectValues[2].Y, 9);
		});
	}

	[Fact]
	public void GridPanelArrangeRecomputesColumnWidthFromFinalWidth()
	{
		Ui(delegate
		{
			// 第二趟必须按最终宽重算列宽(滚动条出现会改变可用宽);行高沿用第一趟量出来的值
			PageLayoutV2 page = Grid(2, "a", "b");
			FakeCard a = new FakeCard("a", 10.0, 10.0);
			FakeCard b = new FakeCard("b", 10.0, 10.0);
			GridCanvasPanel panel = new GridCanvasPanel();
			panel.SetLayout(LayoutSchema.ModeGrid, 2, 0.0, 10.0, page);
			panel.Children.Add(a);
			panel.Children.Add(b);
			panel.Measure(new Size(300.0, 1000.0));
			Assert.Equal(145.0, panel.ColumnWidthValue, 9);
			panel.Arrange(new Rect(0.0, 0.0, 310.0, 120.0));

			Assert.Equal(150.0, panel.ColumnWidthValue, 9); // (310 - 10) / 2
			Assert.Equal(0.0, a.Bounds.X, 9);
			Assert.Equal(150.0, a.Bounds.Width, 9);
			Assert.Equal(160.0, b.Bounds.X, 9);
			Assert.Equal(150.0, b.Bounds.Width, 9);
		});
	}

	[Fact]
	public void MeasuredHeightDrivesAutoRowHeight()
	{
		Ui(delegate
		{
			// 行高 = 该行最高块的"需求高"(再抬到块最小高与兜底高)
			PageLayoutV2 page = Grid(2, "a", "b", "c");
			GridCanvasPanel panel = Mount(LayoutSchema.ModeGrid, 2, 0.0, 10.0, page, new Size(300.0, 1000.0),
				new FakeCard("a", 10.0, 300.0), new FakeCard("b", 10.0, 80.0), new FakeCard("c", 10.0, 80.0));

			Assert.Equal(300.0, panel.RowHeightValues[0], 9); // 300 > 120
			Assert.Equal(120.0, panel.RowHeightValues[1], 9); // 80 被抬到 120(块最小高)
			Assert.Equal(300.0 + 10.0 + 120.0, panel.DesiredSize.Height, 9);
		});
	}

	[Fact]
	public void FixedRowHeightWinsOverMeasuredHeights()
	{
		Ui(delegate
		{
			PageLayoutV2 page = Grid(2, "a", "b", "c");
			GridCanvasPanel panel = Mount(LayoutSchema.ModeGrid, 2, 150.0, 10.0, page, new Size(300.0, 1000.0),
				new FakeCard("a", 10.0, 300.0), new FakeCard("b", 10.0, 80.0), new FakeCard("c", 10.0, 80.0));

			Assert.Equal(new double[2] { 150.0, 150.0 }, panel.RowHeightValues);
			Assert.Equal(150.0 + 10.0 + 150.0, panel.DesiredSize.Height, 9);
			Assert.Equal(150.0, panel.RectValues[0].H, 9);
		});
	}

	[Fact]
	public void FoldedBlockDoesNotRaiseItsRowToBlockMinimum()
	{
		Ui(delegate
		{
			// 折叠块(只占标题栏高)不参与行高下限 MinH:cells 里被复制一份 MinH=0 ⇒ 行高只有兜底 40;
			// 同一份文档、同一块内容,不折叠时行高仍是 120(块最小高)。
			PageLayoutV2 page = Grid(2, "a");
			GridCanvasPanel foldedPanel = Mount(LayoutSchema.ModeGrid, 2, 0.0, 10.0, page, new Size(300.0, 1000.0),
				new FakeCard("a", 10.0, 30.0, folded: true));
			Assert.Equal(40.0, foldedPanel.RowHeightValues[0], 9); // RowFallback = 40(不是 MinH 120)

			GridCanvasPanel plainPanel = Mount(LayoutSchema.ModeGrid, 2, 0.0, 10.0, page, new Size(300.0, 1000.0),
				new FakeCard("a", 10.0, 30.0));
			Assert.Equal(120.0, plainPanel.RowHeightValues[0], 9); // 不折叠 ⇒ 抬到块最小高 120
		});
	}

	[Fact]
	public void FixedPixelHeightBlockIsMeasuredAtThatHeightAndSkippedFromMinimum()
	{
		Ui(delegate
		{
			// 用户拖出固定像素高(HeightPx,自动行高页面)的块:按该高度量、且不参与 MinH
			PageLayoutV2 page = Grid(1, "a");
			FakeCard a = new FakeCard("a", 10.0, 999.0);
			GridCanvasPanel panel = new GridCanvasPanel();
			panel.SetLayout(LayoutSchema.ModeGrid, 1, 0.0, 10.0, page);
			panel.Cells[0].HeightPx = 50.0;
			panel.Children.Add(a);
			panel.Measure(new Size(300.0, 1000.0));
			panel.Arrange(new Rect(0.0, 0.0, 300.0, 60.0));

			// 需求高 = HeightPx(50),MinH 被 noMin 归零 ⇒ 行高 max(40, 50) = 50(不是 120、也不是 999)
			Assert.Equal(50.0, panel.RowHeightValues[0], 9);
			Assert.Equal(50.0, a.Bounds.Height, 9);
			Assert.Equal(50.0, panel.DesiredSize.Height, 9);
		});
	}

	[Fact]
	public void ChildWithoutCellIsMeasuredWithFallbackAndCollapsed()
	{
		Ui(delegate
		{
			// 名不在页面里的子元素:量 (列宽, 120) 兜底、Arrange 到 0 尺寸(与源一致)
			PageLayoutV2 page = Grid(1, "a");
			FakeCard ghost = new FakeCard("ghost", 10.0, 10.0);
			GridCanvasPanel panel = Mount(LayoutSchema.ModeGrid, 1, 0.0, 10.0, page, new Size(300.0, 1000.0),
				new FakeCard("a", 10.0, 10.0), ghost);

			Assert.Equal(120.0, panel.RowHeightValues[0], 9); // ghost 不参与(没有槽位)
			Assert.Equal(0.0, ghost.Bounds.Width, 9);
			Assert.Equal(0.0, ghost.Bounds.Height, 9);
			Assert.Equal("a", panel.HitId(new Point(0.0, 0.0)));
			Assert.Null(panel.HitId(new Point(1000.0, 1000.0)));
		});
	}

	[Fact]
	public void IdResolutionFallsBackToTag()
	{
		Ui(delegate
		{
			// 源文件的兜底分支:没有实现 ILayoutCanvasChild 的子元素按 Tag 认块 id
			PageLayoutV2 page = Grid(1, "a");
			TagCard a = new TagCard("a", 10.0, 200.0);
			GridCanvasPanel panel = Mount(LayoutSchema.ModeGrid, 1, 0.0, 10.0, page, new Size(300.0, 1000.0), a);

			Assert.Equal(1, panel.Rows);
			Assert.Equal(200.0, panel.RowHeightValues[0], 9); // 认到了 a ⇒ 用 a 的需求高
			Assert.Equal(200.0, a.Bounds.Height, 9);
		});
	}

	[Fact]
	public void SetLayoutClampsColumnsGapAndUnknownMode()
	{
		Ui(delegate
		{
			PageLayoutV2 page = Grid(1, "a");
			GridCanvasPanel panel = new GridCanvasPanel();
			panel.SetLayout("weird", 0, 0.0, -5.0, page);

			Assert.Equal(LayoutSchema.ModeStack, panel.Mode); // 未知模式回落到 stack
			Assert.Equal(1, panel.Cols);                      // Math.Max(1, cols)
			Assert.Equal(0.0, panel.Gap, 9);                  // gap < 0 → 0
			panel.Children.Add(new FakeCard("a", 10.0, 10.0));
			panel.Measure(new Size(0.0, 100.0));              // 宽 <= 0 → 回落到 900
			Assert.Equal(900.0, panel.ColumnWidthValue, 9);
			// 注意(Avalonia 语义差异):DesiredSize 会被夹到可用尺寸,所以用无限宽再量一次看真实期望宽
			panel.Measure(new Size(double.PositiveInfinity, 100.0));
			Assert.Equal(900.0, panel.DesiredSize.Width, 9);
		});
	}

	[Fact]
	public void InfiniteAvailableWidthFallsBackTo900()
	{
		Ui(delegate
		{
			PageLayoutV2 page = Grid(1, "a");
			GridCanvasPanel panel = new GridCanvasPanel();
			panel.SetLayout(LayoutSchema.ModeGrid, 1, 0.0, 10.0, page);
			panel.Children.Add(new FakeCard("a", 10.0, 10.0));
			panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

			Assert.Equal(900.0, panel.ColumnWidthValue, 9);
			Assert.Equal(900.0, panel.DesiredSize.Width, 9);
			Assert.Equal(120.0, panel.DesiredSize.Height, 9);
		});
	}

	[Fact]
	public void ReflowKeepsRectsAfterCellsAreEditedInPlace()
	{
		Ui(delegate
		{
			// 拖拽/缩放会就地改槽位(改 X/Y/W/H)后调 Reflow()
			PageLayoutV2 page = Grid(2, "a", "b");
			FakeCard a = new FakeCard("a", 10.0, 10.0);
			GridCanvasPanel panel = new GridCanvasPanel();
			panel.SetLayout(LayoutSchema.ModeGrid, 2, 0.0, 10.0, page);
			panel.Children.Add(a);
			panel.Children.Add(new FakeCard("b", 10.0, 10.0));
			panel.Measure(new Size(300.0, 1000.0));
			panel.Arrange(new Rect(0.0, 0.0, 300.0, 120.0));
			Assert.Equal(0.0, a.Bounds.X, 9);

			panel.Cells[0].X = 1; // 把 a 挪到第 2 列
			panel.Reflow();
			panel.Measure(new Size(300.0, 1000.0));
			panel.Arrange(new Rect(0.0, 0.0, 300.0, 120.0));
			Assert.Equal(155.0, a.Bounds.X, 9);

			panel.ApplyTo(page); // 写回页面:列的网格单位
			Assert.Equal(1.0, page.Blocks[0].X);
		});
	}

	[Fact]
	public void HitRectAndMarqueeQueriesUseLaidOutRects()
	{
		Ui(delegate
		{
			PageLayoutV2 page = Grid(2, "a", "b");
			GridCanvasPanel panel = Mount(LayoutSchema.ModeGrid, 2, 0.0, 10.0, page, new Size(300.0, 1000.0),
				new FakeCard("a", 10.0, 10.0), new FakeCard("b", 10.0, 10.0));

			Assert.Equal("a", panel.HitId(new Point(1.0, 1.0)));
			Assert.Equal("b", panel.HitId(new Point(160.0, 1.0)));
			Assert.Null(panel.HitId(new Point(500.0, 500.0)));
			Assert.Equal("a", panel.CellOf("a")!.Id);
			Assert.Null(panel.CellOf("ghost"));
			Assert.Equal(1, panel.IndexOf("b"));
			Assert.Equal(-1, panel.IndexOf("ghost"));
			Assert.Equal(145.0, panel.RectOf("a")!.Value.W, 9);
			Assert.Null(panel.RectOf("ghost"));
			Assert.Equal(155.0, Assert.Single(panel.OtherRects("a")).X, 9); // 只剩 b(155.0 起)
			Assert.Equal(2, panel.OtherRects(null).Count);
			Assert.Equal(new string[2] { "a", "b" }, panel.IdsIn(new LayoutRect(0.0, 0.0, 400.0, 400.0)).ToArray());
			Assert.Empty(panel.IdsIn(new LayoutRect(500.0, 500.0, 10.0, 10.0)));
		});
	}

	[Fact]
	public void CompactCellsRelayoutsGridInPlace()
	{
		Ui(delegate
		{
			// 拖动期间的"实时让位"入口:把 b 从第 1 行压上去(不钉任何块)
			PageLayoutV2 page = Grid(2, "a", "b");
			FakeCard a = new FakeCard("a", 10.0, 10.0);
			FakeCard b = new FakeCard("b", 10.0, 10.0);
			GridCanvasPanel panel = Mount(LayoutSchema.ModeGrid, 2, 0.0, 10.0, page, new Size(300.0, 1000.0), a, b);
			Assert.Equal(new string[2] { "a", "b" }, panel.Cells.Select(delegate(LayoutCell c)
			{
				return c.Id;
			}).ToArray());

			panel.Cells[1].Y = 3; // 人为把 b 丢到第 4 行
			panel.CompactCells(new HashSet<string>(StringComparer.Ordinal), null);
			panel.Measure(new Size(300.0, 1000.0));
			panel.Arrange(new Rect(0.0, 0.0, 300.0, 120.0));
			Assert.Equal(0, panel.Cells[1].Y);   // 上浮回第 0 行
			Assert.Equal(0.0, b.Bounds.Y, 9);

			// canvas 模式不参与压实(源:IsCanvas 直接返回)
			PageLayoutV2 canvasPage = Canvas("x");
			canvasPage.Blocks[0].Y = 0.5;
			GridCanvasPanel canvasPanel = new GridCanvasPanel();
			canvasPanel.SetLayout(LayoutSchema.ModeCanvas, 12, 0.0, 10.0, canvasPage);
			canvasPanel.CompactCells(new HashSet<string>(StringComparer.Ordinal), null);
			Assert.Equal(0.5, canvasPanel.Cells[0].NY, 9);
		});
	}

	// ————— canvas:归一化矩形 / z 序 / 画布高 —————

	[Fact]
	public void CanvasModeMapsNormalizedRectsAndUsesHostHeight()
	{
		Ui(delegate
		{
			PageLayoutV2 page = Canvas("a");
			page.Blocks[0].X = 0.0;
			page.Blocks[0].Y = 0.0;
			page.Blocks[0].W = 0.5;
			page.Blocks[0].H = 0.5;
			FakeCard a = new FakeCard("a", 10.0, 10.0);
			GridCanvasPanel panel = Mount(LayoutSchema.ModeCanvas, 12, 0.0, 10.0, page, new Size(500.0, 300.0), a);

			Assert.True(panel.IsCanvas);
			// 画布高 = max(参考高 600, 宿主给的高 300)⇒ 矩形高 = 0.5 × 600 = 300
			Assert.Equal(300.0, panel.RectValues[0].H, 9);
			Assert.Equal(250.0, panel.RectValues[0].W, 9);
			Assert.Equal(250.0, a.Bounds.Width, 9);
			Assert.Equal(300.0, a.Bounds.Height, 9);
			// 宿主给的高(300)比参考高低 ⇒ DesiredSize 被夹到可用高(Avalonia 语义,见报告)
			Assert.Equal(300.0, panel.DesiredSize.Height, 9);
			// 拿不到有限高时(滚动区)才是参考高本身
			panel.Measure(new Size(500.0, double.PositiveInfinity));
			Assert.Equal(600.0, panel.DesiredSize.Height, 9);
		});
	}

	[Fact]
	public void CanvasModeWithInfiniteHeightFallsBackToReferenceHeight()
	{
		Ui(delegate
		{
			// 滚动区里最常给的就是无限高 ⇒ 用 CanvasHeight(缺省 600,对齐旧 DashHost 的兜底)
			PageLayoutV2 page = Canvas("a");
			page.Blocks[0].W = 0.5;
			page.Blocks[0].H = 0.5;
			FakeCard a = new FakeCard("a", 10.0, 10.0);
			GridCanvasPanel panel = new GridCanvasPanel();
			panel.SetLayout(LayoutSchema.ModeCanvas, 12, 0.0, 10.0, page);
			panel.Children.Add(a);
			panel.Measure(new Size(500.0, double.PositiveInfinity));
			panel.Arrange(new Rect(0.0, 0.0, 500.0, panel.DesiredSize.Height));

			Assert.Equal(600.0, panel.DesiredSize.Height, 9);
			Assert.Equal(300.0, a.Bounds.Height, 9);

			// CanvasHeight 可被宿主覆盖(画布参考高的入口;普通 CLR 属性 ⇒ 与源一致,要显式失效重排)
			panel.CanvasHeight = 800.0;
			panel.InvalidateMeasure();
			panel.Measure(new Size(500.0, double.PositiveInfinity));
			Assert.Equal(800.0, panel.DesiredSize.Height, 9);
		});
	}

	[Fact]
	public void CanvasModeDeliversChildrenInZOrderAndMatchesById()
	{
		Ui(delegate
		{
			PageLayoutV2 page = Canvas("top", "bottom", "middle");
			page.Blocks[0].Z = 2;
			page.Blocks[1].Z = 0;
			page.Blocks[2].Z = 1;
			page.Blocks[0].Y = 0.5;
			page.Blocks[2].Y = 0.25;
			FakeCard top = new FakeCard("top", 10.0, 10.0);
			FakeCard middle = new FakeCard("middle", 10.0, 10.0);
			FakeCard bottom = new FakeCard("bottom", 10.0, 10.0);
			// Children 的添加顺序刻意与 z 序相反:排布必须按块 id 配对,不看 Children 下标
			GridCanvasPanel panel = Mount(LayoutSchema.ModeCanvas, 12, 0.0, 10.0, page, new Size(400.0, 600.0), top, middle, bottom);

			Assert.Equal(new string[3] { "bottom", "middle", "top" }, panel.Cells.Select(delegate(LayoutCell c)
			{
				return c.Id;
			}).ToArray());
			Assert.Equal(0.0, bottom.Bounds.Y, 9);
			Assert.Equal(150.0, middle.Bounds.Y, 9);
			Assert.Equal(300.0, top.Bounds.Y, 9);
		});
	}

	// ————— 自绘底纹(真渲染取像素) —————

	[Fact]
	public void HatchIsDrawnOnlyInEditMode()
	{
		Ui(delegate
		{
			PageLayoutV2 page = Grid(2, "a", "b");
			GridCanvasPanel panel = new GridCanvasPanel();
			panel.SetLayout(LayoutSchema.ModeGrid, 2, 0.0, 10.0, page);
			panel.Children.Add(new FakeCard("a", 10.0, 10.0));
			panel.Children.Add(new FakeCard("b", 10.0, 10.0));
			panel.Measure(new Size(200.0, 200.0));
			panel.Arrange(new Rect(0.0, 0.0, 200.0, 120.0));

			// 非编辑态:一个像素都不画(默认渲染根本不经过这里)
			Assert.Equal(0, Painted(Render(panel, 200, 120)));

			panel.EditMode = true;
			panel.InvalidateVisual();
			Color[] on = Render(panel, 200, 120);
			int painted = Painted(on);
			// 底纹:主题色 38 透明度的线(0.6px)—— 2 列时唯一的列分隔线在 x=100.0
			// (k=1 → 1×(95+10) − 10/2);只有 1 行 ⇒ 不画横线(循环上界 _rowHeights.Length-1 = 0)
			Assert.True(painted > 0, "编辑态应当画出栅格底纹");
			Assert.True(painted <= 120 * 2, "底纹只应当是稀薄的线,而不是整块填充");
			Assert.True(on[60 * 200 + 99].A != 0 || on[60 * 200 + 100].A != 0, "列分隔线应当落在 x=100 附近那两列像素上");
			Assert.Equal(0, on[60 * 200 + 50].A); // 线以外仍是空的
			Assert.Equal(0, on[0].A);
		});
	}

	[Fact]
	public void CanvasHatchFollowsSnapStepAndSkipsWhenStepIsZero()
	{
		Ui(delegate
		{
			PageLayoutV2 page = Canvas("a");
			FakeCard a = new FakeCard("a", 10.0, 10.0);
			GridCanvasPanel panel = new GridCanvasPanel();
			panel.SetLayout(LayoutSchema.ModeCanvas, 12, 0.0, 10.0, page);
			panel.Children.Add(a);
			panel.EditMode = true;
			panel.SnapStep = 0.0; // 吸附档 0 ⇒ StepOf(0) = 0 ⇒ 不画底纹
			panel.Measure(new Size(100.0, 100.0));
			panel.Arrange(new Rect(0.0, 0.0, 100.0, 100.0));
			Assert.Equal(0, Painted(Render(panel, 100, 100)));

			panel.SnapStep = 8.0; // 缺省档:8px 方格(0..100 内 12 条竖线 + 12 条横线)
			Color[] on = Render(panel, 100, 100);
			int painted = Painted(on);
			Assert.True(painted > 0);
			// 24 条 0.6px 的线,抗锯齿后每条约占 2 列/行 ⇒ 观测 ~4.8k 像素;整块填充是 10000
			Assert.True(painted <= 5000, "方格底纹应当稀疏,不能是整块填充:" + painted);
			// 第一条竖线在 x=8(整列 100 高),第一条横线在 y=8
			Assert.NotEqual(0, on[50 * 100 + 8].A);
			Assert.NotEqual(0, on[8 * 100 + 50].A);
			// 而 (0,0) 附近不含线(线从 i=1 起)
			Assert.Equal(0, on[0].A);
		});
	}

	[Fact]
	public void PanelLaysOutAndDeliversPointerInsideARealWindow()
	{
		Ui(delegate
		{
			// 真窗口(Show + UpdateLayout)里过一遍布局系统:证明 Decorator + 宿主(Panel)的等价结构
			// 在真实可视树里也量得对、排得对、命中测试也能把指针送到卡片上(编辑器的拖动就靠这条)。
			PageLayoutV2 page = Grid(2, "a", "b");
			GridCanvasPanel panel = new GridCanvasPanel();
			panel.SetLayout(LayoutSchema.ModeGrid, 2, 0.0, 10.0, page);
			Border cardA = new Border
			{
				Tag = "a",
				Background = Brushes.Red
			};
			Border cardB = new Border
			{
				Tag = "b",
				Background = Brushes.Blue
			};
			panel.Children.Add(cardA);
			panel.Children.Add(cardB);
			Window window = new Window
			{
				Width = 300.0,
				Height = 200.0,
				Content = panel
			};
			try
			{
				window.Show();
				window.UpdateLayout();

				Assert.Equal(145.0, panel.ColumnWidthValue, 9);
				Assert.Equal(145.0, cardA.Bounds.Width, 9);
				Assert.Equal(120.0, cardA.Bounds.Height, 9);
				Assert.Equal(155.0, cardB.Bounds.X, 9);
				Assert.Equal(145.0, cardB.Bounds.Width, 9);

				// 窗口坐标 ↔ 画布坐标(编辑器的手势就是拿 e.GetPosition(_box) 算的)
				Point inWindow = cardA.TranslatePoint(new Point(5.0, 5.0), window)!.Value;
				Assert.Equal(cardA.Bounds.X + 5.0, inWindow.X, 9);

				// 指针真的落在卡片上(命中测试穿过 Decorator → 子元素宿主 → 卡片)
				int pressed = 0;
				cardA.PointerPressed += delegate
				{
					pressed++;
				};
				window.MouseDown(inWindow, MouseButton.Left, RawInputModifiers.None);
				window.MouseUp(inWindow, MouseButton.Left, RawInputModifiers.None);
				Assert.Equal(1, pressed);

				// 卡片之外(两列之间的 gap 里)不该落到任何卡片上
				int stray = 0;
				cardB.PointerPressed += delegate
				{
					stray++;
				};
				Point gap = panel.TranslatePoint(new Point(150.0, 60.0), window)!.Value;
				window.MouseDown(gap, MouseButton.Left, RawInputModifiers.None);
				window.MouseUp(gap, MouseButton.Left, RawInputModifiers.None);
				Assert.Equal(0, stray);
			}
			finally
			{
				window.Close();
			}
		});
	}

	// ————— FitBox(窄宽自适应缩放盒) —————

	private static FitBox MountFit(bool enabled, Size available, out FakeCard child)
	{
		child = new FakeCard("", 400.0, 40.0);
		FitBox box = new FitBox
		{
			Enabled = enabled,
			Child = child
		};
		box.Measure(available);
		return box;
	}

	[Fact]
	public void FitBoxDisabledPassesChildThrough()
	{
		Ui(delegate
		{
			FitBox box = MountFit(false, new Size(200.0, double.PositiveInfinity), out FakeCard child);

			// Avalonia 语义:DesiredSize 被夹到可用宽(WPF 不夹)⇒ 400 宽的孩子在这里报 200
			Assert.Equal(200.0, box.DesiredSize.Width, 9);
			Assert.Equal(40.0, box.DesiredSize.Height, 9);
			box.Arrange(new Rect(0.0, 0.0, 400.0, 40.0));
			Assert.False(box.ClipToBounds);
			Assert.Equal(400.0, child.Bounds.Width, 9); // 排布仍是父给的整块尺寸
			Assert.Null(child.RenderTransform);
		});
	}

	[Fact]
	public void FitBoxScalesNarrowContentAndClampsToMinScale()
	{
		Ui(delegate
		{
			// 自然宽 400 / 可用宽 200 ⇒ 0.5 < MinScale(0.55)⇒ 只缩到 0.55
			FitBox box = MountFit(true, new Size(200.0, double.PositiveInfinity), out FakeCard child);

			Assert.Equal(200.0, box.DesiredSize.Width, 9);
			Assert.Equal(40.0 * 0.55, box.DesiredSize.Height, 9);
			box.Arrange(new Rect(0.0, 0.0, 200.0, box.DesiredSize.Height));
			Assert.True(box.ClipToBounds);
			ScaleTransform transform = Assert.IsType<ScaleTransform>(child.RenderTransform);
			Assert.Equal(0.55, transform.ScaleX, 9);
			Assert.Equal(0.55, transform.ScaleY, 9);
			// 子元素按"放大回去"的尺寸排(再被 RenderTransform 缩回来);
			// 200 / 0.55 = 363.636… ⇒ Avalonia 的布局取整把排布矩形抬到 364(实测,见报告)
			Assert.Equal(200.0 / 0.55, child.Bounds.Width, 0);
			Assert.Equal(40.0, child.Bounds.Height, 9);
		});
	}

	[Fact]
	public void FitBoxScalesToAvailableWidthWhenAboveMinScale()
	{
		Ui(delegate
		{
			// 自然宽 400 / 可用宽 300 ⇒ 0.75 > 0.55 ⇒ 按 0.75 缩
			FitBox box = MountFit(true, new Size(300.0, double.PositiveInfinity), out FakeCard child);

			Assert.Equal(300.0, box.DesiredSize.Width, 9);
			Assert.Equal(30.0, box.DesiredSize.Height, 9);
			box.Arrange(new Rect(0.0, 0.0, 300.0, 30.0));
			ScaleTransform transform = Assert.IsType<ScaleTransform>(child.RenderTransform);
			Assert.Equal(0.75, transform.ScaleX, 9);
		});
	}

	[Fact]
	public void FitBoxLeavesTallContentAlone()
	{
		Ui(delegate
		{
			// 自然高 200 > MaxAdaptHeight(170)⇒ 当成"高内容"(列表),不缩放
			FakeCard child = new FakeCard("", 400.0, 200.0);
			FitBox box = new FitBox
			{
				Enabled = true,
				Child = child
			};
			box.Measure(new Size(200.0, double.PositiveInfinity));
			// 自然高 200 > MaxAdaptHeight(170)⇒ 当成"高内容"(列表),不缩放;
			// 宽按 Avalonia 的 DesiredSize 夹取语义(夹到可用宽 200)
			Assert.Equal(200.0, box.DesiredSize.Width, 9);
			Assert.Equal(200.0, box.DesiredSize.Height, 9);
			box.Arrange(new Rect(0.0, 0.0, 400.0, 200.0));
			Assert.False(box.ClipToBounds);
			Assert.Equal(400.0, child.Bounds.Width, 9);
			Assert.Null(child.RenderTransform);
		});
	}

	[Fact]
	public void FitBoxLeavesContentThatAlreadyFitsAlone()
	{
		Ui(delegate
		{
			FakeCard child = new FakeCard("", 100.0, 40.0);
			FitBox box = new FitBox
			{
				Enabled = true,
				Child = child
			};
			box.Measure(new Size(200.0, double.PositiveInfinity));
			Assert.Equal(100.0, box.DesiredSize.Width, 9);
			Assert.Equal(40.0, box.DesiredSize.Height, 9);
		});
	}

	[Fact]
	public void FitBoxSkipsScalingWhenWidthIsInfinite()
	{
		Ui(delegate
		{
			FitBox box = MountFit(true, new Size(double.PositiveInfinity, double.PositiveInfinity), out FakeCard child);

			Assert.Equal(400.0, box.DesiredSize.Width, 9);
			Assert.Equal(40.0, box.DesiredSize.Height, 9);
			box.Arrange(new Rect(0.0, 0.0, 400.0, 40.0));
			Assert.Null(child.RenderTransform);
		});
	}

	[Fact]
	public void FitBoxResetsScaleWhenWidthGrowsAgain()
	{
		Ui(delegate
		{
			FitBox box = MountFit(true, new Size(200.0, double.PositiveInfinity), out FakeCard child);
			box.Arrange(new Rect(0.0, 0.0, 200.0, box.DesiredSize.Height));
			Assert.True(box.ClipToBounds);

			// 窗口再变宽(块宽 >= 360):正常量一次就够,缩放复位(源:RenderTransform 从 ScaleTransform 复位)
			box.Measure(new Size(600.0, double.PositiveInfinity));
			box.Arrange(new Rect(0.0, 0.0, 400.0, 40.0));
			Assert.False(box.ClipToBounds);
			Assert.Null(child.RenderTransform);
		});
	}

	[Fact]
	public void FitBoxWithoutChildReturnsZeroAndFinalSize()
	{
		Ui(delegate
		{
			FitBox box = new FitBox();
			box.Measure(new Size(100.0, 100.0));
			Assert.Equal(0.0, box.DesiredSize.Width, 9);
			Assert.Equal(0.0, box.DesiredSize.Height, 9);
			box.Arrange(new Rect(0.0, 0.0, 100.0, 100.0));
			Assert.Equal(100.0, box.Bounds.Width, 9);
		});
	}

	// ————— LayoutFill(附加属性) —————

	[Fact]
	public void FillsBodyIsAttachedPropertyWithFalseDefault()
	{
		Ui(delegate
		{
			Border plain = new Border();
			Border marked = new Border();
			StackPanel other = new StackPanel();

			Assert.Equal("FillsBody", LayoutFill.FillsBodyProperty.Name);
			Assert.False(LayoutFill.GetFillsBody(plain));
			Assert.False(LayoutFill.GetFillsBody(marked));
			LayoutFill.SetFillsBody(marked, true);
			Assert.True(LayoutFill.GetFillsBody(marked));
			// 逐元素(附加属性而不是静态开关)
			Assert.False(LayoutFill.GetFillsBody(plain));
			Assert.False(LayoutFill.GetFillsBody(other));
			// 空值安全(编辑器调用点是 `spec.Content != null && GetFillsBody(spec.Content)`)
			Assert.False(LayoutFill.GetFillsBody(null));
			// 直接走 Avalonia 的属性面,证明它确实是挂在 Control 上的附加属性
			marked.SetValue(LayoutFill.FillsBodyProperty, false);
			Assert.False(LayoutFill.GetFillsBody(marked));
			other.SetValue(LayoutFill.FillsBodyProperty, true);
			Assert.True(LayoutFill.GetFillsBody(other));
		});
	}

	// ————— GuideLayer(辅助线层) —————

	[Fact]
	public void GuideLayerStateFollowsLinesAndMarquee()
	{
		Ui(delegate
		{
			GuideLayer guide = new GuideLayer();
			Assert.False(guide.IsHitTestVisible); // 拖动期间指针必须留在被拖块上
			Assert.False(guide.IsActive);
			guide.Clear(); // 空清不抛
			Assert.False(guide.IsActive);

			guide.ShowLines(new List<LayoutGuideLine>
			{
				new LayoutGuideLine
				{
					Vertical = true,
					Position = 40.0
				},
				new LayoutGuideLine
				{
					Vertical = false,
					Position = 20.0
				}
			});
			Assert.True(guide.IsActive);
			guide.ShowLines(null); // 清线(保留框选)
			Assert.False(guide.IsActive);

			guide.ShowLines(new List<LayoutGuideLine>
			{
				new LayoutGuideLine
				{
					Vertical = true,
					Position = 10.0
				}
			});
			guide.ShowMarquee(new LayoutRect(5.0, 5.0, 50.0, 50.0));
			Assert.True(guide.IsActive);
			guide.Clear();
			Assert.False(guide.IsActive);
			// 线表里的 null 项被忽略(源同样跳过)
			guide.ShowLines(new List<LayoutGuideLine>
			{
				null!,
				new LayoutGuideLine
				{
					Vertical = false,
					Position = 1.0
				}
			});
			Assert.True(guide.IsActive);
		});
	}

	[Fact]
	public void GuideLayerRendersLinesAndMarqueeWithinBounds()
	{
		Ui(delegate
		{
			GuideLayer guide = new GuideLayer
			{
				Width = 100.0,
				Height = 50.0
			};
			guide.Measure(new Size(100.0, 50.0));
			guide.Arrange(new Rect(0.0, 0.0, 100.0, 50.0));
			Assert.Equal(0, Painted(Render(guide, 100, 50)));

			// 竖线位置 -50 被夹到 0 ⇒ 画在 x=0.5(整条 50 高的线,恰好 1 列像素);
			// 这是源的 Clamp(line.Position, 0, width) + "+0.5 像素对齐"两条一起的效果
			guide.ShowLines(new List<LayoutGuideLine>
			{
				new LayoutGuideLine
				{
					Vertical = true,
					Position = -50.0
				}
			});
			Color[] line = Render(guide, 100, 50);
			Assert.Equal(50, Painted(line)); // 1px 宽 × 50 高
			Assert.NotEqual(0, line[49 * 100 + 0].A);
			Assert.Equal(0, line[49 * 100 + 1].A);

			// 水平线同理(位置超下界被夹到 0)
			guide.ShowLines(new List<LayoutGuideLine>
			{
				new LayoutGuideLine
				{
					Vertical = false,
					Position = -999.0
				}
			});
			Color[] hline = Render(guide, 100, 50);
			Assert.Equal(100, Painted(hline));
			Assert.NotEqual(0, hline[0].A);

			// 框选矩形:虚线边框(4,3)+ 16 透明度填充
			GuideLayer marquee = new GuideLayer
			{
				Width = 100.0,
				Height = 50.0
			};
			marquee.Measure(new Size(100.0, 50.0));
			marquee.ShowMarquee(new LayoutRect(10.0, 10.0, 30.0, 20.0));
			marquee.Arrange(new Rect(0.0, 0.0, 100.0, 50.0));
			Color[] box = Render(marquee, 100, 50);
			Assert.True(Painted(box) > 0);
			Assert.Equal(16, box[20 * 100 + 25].A); // 框内填充:Color.FromArgb(16, …)
			Assert.Equal(0, box[0].A);              // 画布外角落仍是空的
			Assert.Equal(0, box[49 * 100 + 99].A);  // 框右下角之外也是空的
		});
	}

	// ————— FillPanel(V-1 的宿主容器) —————

	[Fact]
	public void FillPanelStacksVerticallyWithSpacingAndContentSize()
	{
		Ui(delegate
		{
			FillPanel panel = new FillPanel
			{
				Spacing = 8.0
			};
			FakeCard a = new FakeCard("a", 50.0, 10.0);
			FakeCard b = new FakeCard("b", 60.0, 20.0);
			FakeCard c = new FakeCard("c", 70.0, 30.0);
			panel.Children.Add(a);
			panel.Children.Add(b);
			panel.Children.Add(c);
			panel.Measure(new Size(200.0, double.PositiveInfinity));

			Assert.Equal(70.0, panel.DesiredSize.Width, 9);                       // 内容宽 = 最宽的孩子
			Assert.Equal(10.0 + 20.0 + 30.0 + 16.0, panel.DesiredSize.Height, 9); // + 2 段 Spacing
			panel.Arrange(new Rect(0.0, 0.0, 200.0, 500.0));
			Assert.Equal(10.0, a.Bounds.Height, 9);
			Assert.Equal(200.0, a.Bounds.Width, 9); // 纵向排布:子元素拿满最终宽
			Assert.Equal(18.0, b.Bounds.Y, 9);
			Assert.Equal(46.0, c.Bounds.Y, 9);
			Assert.Equal(30.0, c.Bounds.Height, 9);
		});
	}

	[Fact]
	public void FillPanelFillChildEatsRemainingHeight()
	{
		Ui(delegate
		{
			// V-1 的核心:标记为 FillChild 的那一块吃掉"内容区剩下的高度"(max(自身期望高, 剩余高))
			FillPanel panel = new FillPanel
			{
				Spacing = 8.0
			};
			FakeCard head = new FakeCard("head", 50.0, 10.0);
			FakeCard tabs = new FakeCard("tabs", 60.0, 20.0);
			FakeCard body = new FakeCard("body", 70.0, 30.0);
			panel.Children.Add(head);
			panel.Children.Add(tabs);
			panel.Children.Add(body);
			panel.Measure(new Size(200.0, double.PositiveInfinity));
			panel.FillChild = body;
			panel.Arrange(new Rect(0.0, 0.0, 200.0, 500.0));

			Assert.Equal(10.0, head.Bounds.Height, 9);
			Assert.Equal(18.0, tabs.Bounds.Y, 9);
			Assert.Equal(20.0, tabs.Bounds.Height, 9);
			Assert.Equal(46.0, body.Bounds.Y, 9);
			Assert.Equal(454.0, body.Bounds.Height, 9); // 500 - 46
		});
	}

	[Fact]
	public void FillPanelWithoutFillChildKeepsContentHeights()
	{
		Ui(delegate
		{
			// 不设 FillChild(缺省 null)⇒ 与改动前的"按内容高排布"逐像素一致
			FillPanel panel = new FillPanel
			{
				Spacing = 8.0
			};
			FakeCard body = new FakeCard("body", 70.0, 30.0);
			panel.Children.Add(new FakeCard("head", 50.0, 10.0));
			panel.Children.Add(new FakeCard("tabs", 60.0, 20.0));
			panel.Children.Add(body);
			panel.Measure(new Size(200.0, double.PositiveInfinity));
			panel.Arrange(new Rect(0.0, 0.0, 200.0, 500.0));

			Assert.Null(panel.FillChild);
			Assert.Equal(30.0, body.Bounds.Height, 9);
			Assert.Equal(46.0, body.Bounds.Y, 9);
		});
	}

	[Fact]
	public void FillPanelFillChildKeepsItsOwnHeightWhenContentIsTaller()
	{
		Ui(delegate
		{
			FillPanel panel = new FillPanel();
			FakeCard tall = new FakeCard("tall", 50.0, 600.0);
			panel.Children.Add(tall);
			panel.Measure(new Size(200.0, double.PositiveInfinity));
			panel.FillChild = tall;
			panel.Arrange(new Rect(0.0, 0.0, 200.0, 500.0));

			Assert.Equal(600.0, tall.Bounds.Height, 9); // max(600, 500 - 0)
		});
	}

	[Fact]
	public void FillPanelSkipsInvisibleChildren()
	{
		Ui(delegate
		{
			FillPanel panel = new FillPanel
			{
				Spacing = 8.0
			};
			FakeCard a = new FakeCard("a", 50.0, 10.0);
			FakeCard hidden = new FakeCard("hidden", 60.0, 20.0)
			{
				IsVisible = false
			};
			FakeCard c = new FakeCard("c", 70.0, 30.0);
			panel.Children.Add(a);
			panel.Children.Add(hidden);
			panel.Children.Add(c);
			panel.Measure(new Size(200.0, double.PositiveInfinity));

			// 只有 2 个可见孩子 ⇒ 1 段 Spacing
			Assert.Equal(10.0 + 30.0 + 8.0, panel.DesiredSize.Height, 9);
			panel.Arrange(new Rect(0.0, 0.0, 200.0, 48.0));
			Assert.Equal(0.0, a.Bounds.Y, 9);
			Assert.Equal(18.0, c.Bounds.Y, 9);
			Assert.Equal(30.0, c.Bounds.Height, 9);
			Assert.Equal(0.0, hidden.Bounds.Width, 9); // 不可见的孩子不参与排布(Collapsed 语义)
		});
	}

	[Fact]
	public void FillPanelHorizontalOrientationUsesFinalHeight()
	{
		Ui(delegate
		{
			FillPanel panel = new FillPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 8.0
			};
			FakeCard a = new FakeCard("a", 50.0, 10.0);
			FakeCard b = new FakeCard("b", 60.0, 20.0);
			FakeCard c = new FakeCard("c", 70.0, 30.0);
			panel.Children.Add(a);
			panel.Children.Add(b);
			panel.Children.Add(c);
			panel.Measure(new Size(double.PositiveInfinity, 40.0));

			Assert.Equal(50.0 + 60.0 + 70.0 + 16.0, panel.DesiredSize.Width, 9);
			Assert.Equal(30.0, panel.DesiredSize.Height, 9); // 横向:最"高"的孩子
			panel.Arrange(new Rect(0.0, 0.0, 196.0, 40.0));
			Assert.Equal(0.0, a.Bounds.X, 9);
			Assert.Equal(58.0, b.Bounds.X, 9);
			Assert.Equal(126.0, c.Bounds.X, 9);
			Assert.Equal(40.0, c.Bounds.Height, 9); // 横向排布:子元素拿满最终高
			// FillChild 只在纵向生效(源同样如此)
			panel.FillChild = c;
			panel.Arrange(new Rect(0.0, 0.0, 196.0, 40.0));
			Assert.Equal(40.0, c.Bounds.Height, 9);
		});
	}
}
