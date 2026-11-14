using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Pages.Layout;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// L2 画布几何测试(纯模型层):grid 模式的自动流/跨列跨行/两趟行高、canvas 模式的归一化矩形/8 向手柄/
/// 栅格吸附(移植自旧 DashHost.SnapV)、每块最小尺寸、区块外观参数(style/title/pad/density)、响应式变体纯函数。
/// 测试宿主是 net8:只碰不依赖 WPF 的类型(LayoutGrid / LayoutCanvas / LayoutAppearance / LayoutVariants),
/// 与渲染有关的部分(WPF Panel/手柄/底纹)由 --shot 人工证据覆盖,单测不替代它。
/// </summary>
public class LayoutCanvasTests
{
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

	private static LayoutCell At(LayoutCell cell, int x, int y, int w, int h)
	{
		cell.X = x;
		cell.Y = y;
		cell.W = w;
		cell.H = h;
		return cell;
	}

	// ————— grid:自动流(没被画布编辑过的块按行优先铺满) —————

	[Fact]
	public void GridPacksDefaultBlocksRowFirst()
	{
		List<LayoutCell> cells = LayoutGrid.Pack(Grid(2, "a", "b", "c", "d"));
		Assert.Equal(4, cells.Count);
		Assert.Equal(new[] { 0, 1, 0, 1 }, cells.Select((LayoutCell c) => c.X).ToArray());
		Assert.Equal(new[] { 0, 0, 1, 1 }, cells.Select((LayoutCell c) => c.Y).ToArray());
		Assert.All(cells, (LayoutCell c) => Assert.Equal(1, c.W));
		Assert.Equal(2, LayoutGrid.RowCount(cells));
	}

	[Fact]
	public void GridTwelveColumnsPutsOneBlockPerColumn()
	{
		// 契约里 w 的缺省值就是 1(占 1 列):12 列栅格下不做任何放置会排成 12 列一行。
		// 这是契约语义,页面入口(L5)在切 grid 时应同时给出建议列数/块宽。
		List<LayoutCell> cells = LayoutGrid.Pack(Grid(12, "a", "b"));
		Assert.Equal(0, cells[0].X);
		Assert.Equal(1, cells[1].X);
		Assert.Equal(0, cells[1].Y);
		Assert.Equal(1, LayoutGrid.RowCount(cells));
	}

	[Fact]
	public void GridKeepsExplicitPositionsAndFillsTheRest()
	{
		PageLayoutV2 page = Grid(2, "a", "b");
		page.Blocks[0].X = 1.0; // a 显式声明在第 1 列
		page.Blocks[0].Y = 0.0;
		List<LayoutCell> cells = LayoutGrid.Pack(page);
		Assert.Equal(1, cells[0].X);
		Assert.Equal(0, cells[1].X); // b 自动流进唯一的空位
		Assert.Equal(0, cells[1].Y);
	}

	[Fact]
	public void GridPushesOverlappingExplicitBlocksDown()
	{
		PageLayoutV2 page = Grid(2, "a", "b");
		page.Blocks[0].W = 2.0; // 显式:整行
		page.Blocks[1].W = 2.0; // 显式:也想占整行 → 原位放不下,同列向下找
		List<LayoutCell> cells = LayoutGrid.Pack(page);
		Assert.Equal(0, cells[0].Y);
		Assert.Equal(1, cells[1].Y);
		Assert.Equal(2, LayoutGrid.RowCount(cells));
	}

	[Fact]
	public void GridTreatsSpanAsColumnSpanWhenWidthIsUntouched()
	{
		// stack 模式里块声明的 span 切到 grid 时延续成列跨度(用户已有的跨列不丢);W 一旦显式声明就以 W 为准
		PageLayoutV2 page = Grid(3, "a", "b", "c");
		page.Blocks[0].Span = 2;
		page.Blocks[2].W = 3.0; // 显式列跨度
		page.Blocks[2].Span = 1;
		List<LayoutCell> cells = LayoutGrid.Pack(page);
		Assert.Equal(2, cells[0].W); // span → 列跨度
		Assert.Equal(0, cells[0].X);
		Assert.Equal(0, cells[0].Y);
		Assert.Equal(2, cells[1].X); // b 自动流到仅剩的第 2 列
		Assert.Equal(0, cells[1].Y);
		Assert.Equal(1, cells[1].W);
		Assert.Equal(3, cells[2].W); // 显式 W 优先于 span
		Assert.Equal(0, cells[2].X);
		Assert.Equal(1, cells[2].Y);
		Assert.Equal(2, LayoutGrid.RowCount(cells));
	}

	[Fact]
	public void GridSpansAndHiddenBlocks()
	{
		PageLayoutV2 page = Grid(4, "a", "b", "c");
		page.Blocks[0].W = 2.0;
		page.Blocks[1].Hidden = true;
		List<LayoutCell> cells = LayoutGrid.Pack(page);
		Assert.Equal(new[] { "a", "c" }, cells.Select((LayoutCell c) => c.Id).ToArray()); // 隐藏项不进画布
		Assert.Equal(2, cells[0].W);
		Assert.Equal(2, cells[1].X); // c 自动流到 a 右边
	}

	[Fact]
	public void GridClampsOversizedBlockIntoColumns()
	{
		PageLayoutV2 page = Grid(2, "a");
		page.Blocks[0].W = 99.0;
		List<LayoutCell> cells = LayoutGrid.Pack(page);
		Assert.Equal(2, Assert.Single(cells).W);
	}

	// ————— grid:两趟行高(解"块高不定") —————

	[Fact]
	public void FixedRowHeightWinsOverMeasuredHeights()
	{
		List<LayoutCell> cells = new List<LayoutCell>
		{
			new LayoutCell { Id = "a" },
			new LayoutCell { Id = "b", Y = 1 }
		};
		Dictionary<string, double> desired = new Dictionary<string, double>
		{
			["a"] = 300.0,
			["b"] = 40.0
		};
		double[] heights = LayoutGrid.RowHeights(cells, 2, 150.0, desired, LayoutCanvas.FallbackMinH);
		Assert.Equal(new[] { 150.0, 150.0 }, heights);
	}

	[Fact]
	public void AutoRowHeightIsTheTallestBlockInThatRow()
	{
		List<LayoutCell> cells = new List<LayoutCell>
		{
			new LayoutCell { Id = "a" },
			new LayoutCell { Id = "b", X = 1 },
			new LayoutCell { Id = "c", Y = 1 }
		};
		Dictionary<string, double> desired = new Dictionary<string, double>
		{
			["a"] = 100.0,
			["b"] = 300.0,
			["c"] = 80.0
		};
		double[] heights = LayoutGrid.RowHeights(cells, 2, 0.0, desired, LayoutCanvas.FallbackMinH);
		Assert.Equal(300.0, heights[0]);
		Assert.Equal(LayoutCanvas.FallbackMinH, heights[1]); // 80 < 兜底 120 → 抬到兜底
	}

	[Fact]
	public void RowSpanningBlockSplitsItsHeightAcrossRows()
	{
		List<LayoutCell> cells = new List<LayoutCell>
		{
			At(new LayoutCell { Id = "a" }, 0, 0, 1, 2)
		};
		Dictionary<string, double> desired = new Dictionary<string, double>
		{
			["a"] = 400.0
		};
		double[] heights = LayoutGrid.RowHeights(cells, 2, 0.0, desired, LayoutCanvas.FallbackMinH);
		Assert.Equal(new[] { 200.0, 200.0 }, heights);
	}

	[Fact]
	public void BlockMinHeightRaisesItsRow()
	{
		List<LayoutCell> cells = new List<LayoutCell>
		{
			new LayoutCell
			{
				Id = "a",
				MinH = 400.0
			}
		};
		Dictionary<string, double> desired = new Dictionary<string, double>
		{
			["a"] = 100.0
		};
		double[] heights = LayoutGrid.RowHeights(cells, 1, 0.0, desired, LayoutCanvas.FallbackMinH);
		Assert.Equal(400.0, heights[0]);
	}

	[Fact]
	public void MissingMeasuredHeightFallsBackToBlockMinimum()
	{
		List<LayoutCell> cells = new List<LayoutCell>
		{
			new LayoutCell { Id = "a" }
		};
		double[] heights = LayoutGrid.RowHeights(cells, 1, 0.0, null, LayoutCanvas.FallbackMinH);
		Assert.Equal(LayoutCanvas.FallbackMinH, heights[0]);
	}

	// ————— grid:列宽 / 像素矩形 / 命中 / 拖拽缩放 —————

	[Fact]
	public void ColumnWidthSubtractsGapsFirst()
	{
		Assert.Equal((1200.0 - 20.0) / 3.0, LayoutGrid.ColumnWidth(1200.0, 3, 10.0), 6);
	}

	[Fact]
	public void PlaceUsesGridUnitsAndGap()
	{
		LayoutCell cell = At(new LayoutCell { Id = "a" }, 1, 0, 2, 1);
		LayoutRect rect = LayoutGrid.Place(cell, 100.0, new double[1] { 50.0 }, 10.0);
		Assert.Equal(110.0, rect.X, 6); // 1 × (100 + 10)
		Assert.Equal(0.0, rect.Y, 6);
		Assert.Equal(210.0, rect.W, 6); // 2 × 100 + 1 × 10
		Assert.Equal(50.0, rect.H, 6);
	}

	[Fact]
	public void PlaceAccumulatesRowHeightsAndGaps()
	{
		LayoutCell cell = At(new LayoutCell { Id = "a" }, 0, 2, 1, 1);
		LayoutRect rect = LayoutGrid.Place(cell, 100.0, new double[3] { 10.0, 20.0, 30.0 }, 5.0);
		Assert.Equal(10.0 + 5.0 + 20.0 + 5.0, rect.Y, 6);
		Assert.Equal(30.0, rect.H, 6);
	}

	[Fact]
	public void HitTestPrefersHighestZ()
	{
		List<LayoutCell> cells = new List<LayoutCell>
		{
			At(new LayoutCell { Id = "under", Z = 0 }, 0, 0, 2, 1),
			At(new LayoutCell { Id = "over", Z = 1 }, 0, 0, 2, 1)
		};
		List<LayoutRect> rects = LayoutGrid.Rects(cells, 100.0, new double[1] { 50.0 }, 0.0);
		Assert.Equal(1, LayoutGrid.HitIndex(cells, rects, 10.0, 10.0));
		Assert.Equal(-1, LayoutGrid.HitIndex(cells, rects, 500.0, 10.0));
		Assert.Equal("over", cells[LayoutGrid.HitIndex(cells, rects, 10.0, 10.0)].Id);
	}

	[Fact]
	public void MoveCellClampsInsideGrid()
	{
		LayoutCell cell = At(new LayoutCell { Id = "a" }, 0, 0, 2, 1);
		Assert.True(LayoutGrid.MoveCell(cell, 99, 0, 3, 0));
		Assert.Equal(1, cell.X); // 3 列 - 2 跨度 = 最多到第 1 列
		Assert.False(LayoutGrid.MoveCell(cell, 0, -3, 3, 0)); // 顶到 0 行后不再动
		Assert.Equal(0, cell.Y);
	}

	[Fact]
	public void ResizeCellEastWestSouthNorth()
	{
		LayoutCell cell = At(new LayoutCell { Id = "a" }, 1, 1, 2, 2);
		Assert.True(LayoutGrid.ResizeCell(cell, "e", 1, 0, 6));
		Assert.Equal(3, cell.W);
		Assert.True(LayoutGrid.ResizeCell(cell, "w", 1, 0, 6));
		Assert.Equal(2, cell.X);
		Assert.Equal(2, cell.W); // 右边界不动,左边界右移 1 格
		Assert.True(LayoutGrid.ResizeCell(cell, "s", 0, 1, 6));
		Assert.Equal(3, cell.H);
		Assert.True(LayoutGrid.ResizeCell(cell, "n", 0, 1, 6));
		Assert.Equal(2, cell.Y);
		Assert.Equal(2, cell.H);
	}

	[Fact]
	public void ResizeCellNeverCollapsesBelowOneCell()
	{
		LayoutCell cell = At(new LayoutCell { Id = "a" }, 0, 0, 1, 1);
		Assert.False(LayoutGrid.ResizeCell(cell, "e", -5, 0, 4));
		Assert.False(LayoutGrid.ResizeCell(cell, "n", 5, 0, 4) | false);
		Assert.Equal(1, cell.W);
		Assert.Equal(1, cell.H);
		Assert.Equal(0, cell.Y);
	}

	[Fact]
	public void ApplyWritesGridUnitsBackToBlocks()
	{
		PageLayoutV2 page = Grid(2, "a");
		List<LayoutCell> cells = LayoutGrid.Pack(page);
		LayoutGrid.MoveCell(cells[0], 1, 1, 2, 0);
		LayoutGrid.Apply(page, cells);
		Assert.Equal(1.0, page.Blocks[0].X);
		Assert.Equal(1.0, page.Blocks[0].Y);
		Assert.Equal(1.0, page.Blocks[0].W);
		Assert.Equal(1, page.Blocks[0].Span);
	}

	// ————— canvas:归一化矩形 / 吸附 / 8 向手柄 / 最小尺寸 —————

	[Fact]
	public void CanvasLoadClampsAndDefaults()
	{
		PageLayoutV2 page = Canvas("a", "b");
		page.Blocks[0].X = 0.25;
		page.Blocks[0].Y = 0.5;
		page.Blocks[0].W = 0.5;
		page.Blocks[0].H = 0.25;
		page.Blocks[0].MinH = 300.0;
		page.Blocks[1].Hidden = true;
		List<LayoutCell> cells = LayoutCanvas.Load(page);
		LayoutCell cell = Assert.Single(cells);
		Assert.Equal(0.25, cell.NX, 6);
		Assert.Equal(0.5, cell.NY, 6);
		Assert.Equal(0.5, cell.NW, 6);
		Assert.Equal(0.25, cell.NH, 6);
		Assert.Equal(300.0, cell.MinH, 6);
		Assert.Equal(LayoutCanvas.FallbackMinW, cell.MinW, 6);
		Assert.Equal(0, cell.Z); // Z 缺省(-1)→ 顺序索引
	}

	[Fact]
	public void CanvasPlaceMapsToPixels()
	{
		LayoutCell cell = new LayoutCell
		{
			Id = "a",
			NX = 0.25,
			NY = 0.5,
			NW = 0.5,
			NH = 0.25
		};
		LayoutRect rect = LayoutCanvas.Place(cell, 800.0, 400.0);
		Assert.Equal(200.0, rect.X, 6);
		Assert.Equal(200.0, rect.Y, 6);
		Assert.Equal(400.0, rect.W, 6);
		Assert.Equal(100.0, rect.H, 6);
	}

	[Fact]
	public void SnapUsesOldDashHostFormula()
	{
		// 旧 DashHost.SnapV 的公式**逐字移植**:Clamp(v,0,1) → round(v × axisPx / step) × step / axisPx → clamp 0..1
		Assert.Equal(0.48, LayoutCanvas.Snap(0.5, 100.0, 8.0), 6);
		Assert.Equal(0.0, LayoutCanvas.Snap(-1.0, 100.0, 8.0), 6);
		// 轴长是吸附档的整数倍时,超出范围的值夹到 1 后仍是 1(满宽块不会被"吸"进来)
		Assert.Equal(1.0, LayoutCanvas.Snap(9.0, 800.0, 8.0), 6);
		// 轴长不是整数倍时,1.0 会被吸到最近的档位 —— 旧实现同样如此(.NET Math.Round 取偶:
		// round(100/8 = 12.5) = 12 → 12 × 8 / 100 = 0.96)。这条断言钉的是"移植没走样",不是实现缺陷。
		Assert.Equal(0.96, LayoutCanvas.Snap(1.0, 100.0, 8.0), 6);
		// 不吸附(step=0)/ 轴太小时原样返回
		Assert.Equal(0.53, LayoutCanvas.Snap(0.53, 100.0, 0.0), 6);
		Assert.Equal(0.53, LayoutCanvas.Snap(0.53, 1.0, 8.0), 6);
	}

	[Fact]
	public void SnapStepSnapsToContractChoices()
	{
		Assert.Equal(0.0, LayoutCanvas.StepOf(0.0), 6);
		Assert.Equal(8.0, LayoutCanvas.StepOf(7.0), 6);
		Assert.Equal(16.0, LayoutCanvas.StepOf(13.0), 6);
		Assert.True(LayoutCanvas.IsKnownStep(24.0));
		Assert.True(LayoutCanvas.IsKnownStep(0.0));
		Assert.False(LayoutCanvas.IsKnownStep(10.0));
		Assert.Equal(new double[] { 0.0, 4.0, 8.0, 16.0, 24.0 }, LayoutCanvas.Steps);
	}

	[Fact]
	public void HandleAtFindsCornersEdgesAndNothing()
	{
		LayoutRect rect = new LayoutRect(0.0, 0.0, 100.0, 100.0);
		Assert.Equal("nw", LayoutCanvas.HandleAt(rect, 2.0, 2.0, LayoutCanvas.HandleSize));
		Assert.Equal("se", LayoutCanvas.HandleAt(rect, 98.0, 98.0, LayoutCanvas.HandleSize));
		Assert.Equal("n", LayoutCanvas.HandleAt(rect, 50.0, 2.0, LayoutCanvas.HandleSize));
		Assert.Equal("e", LayoutCanvas.HandleAt(rect, 99.0, 50.0, LayoutCanvas.HandleSize));
		Assert.Equal("w", LayoutCanvas.HandleAt(rect, 0.0, 50.0, LayoutCanvas.HandleSize));
		Assert.Equal("s", LayoutCanvas.HandleAt(rect, 50.0, 100.0, LayoutCanvas.HandleSize));
		Assert.Equal(LayoutCanvas.HandleNone, LayoutCanvas.HandleAt(rect, 50.0, 50.0, LayoutCanvas.HandleSize));
		Assert.Equal(LayoutCanvas.HandleNone, LayoutCanvas.HandleAt(rect, 400.0, 400.0, LayoutCanvas.HandleSize));
		Assert.Equal(8, LayoutCanvas.HandleNames.Length);
	}

	[Fact]
	public void CanvasMoveSnapsAndClamps()
	{
		LayoutCell cell = new LayoutCell
		{
			Id = "a",
			NX = 0.5,
			NY = 0.0,
			NW = 0.25,
			NH = 0.5
		};
		// 100px 轴上移动 10px = 0.1 → 0.6 → 吸附到 8px 档(round(60/8)=8)→ 0.64
		Assert.True(LayoutCanvas.Move(cell, 10.0, 0.0, 100.0, 100.0, 8.0));
		Assert.Equal(0.64, cell.NX, 6);
		Assert.Equal(0.0, cell.NY, 6);
		// 拖出右下角:夹在 1 - NW = 0.75 / 1 - NH = 0.5(块不许被拖出画布)
		Assert.True(LayoutCanvas.Move(cell, 500.0, 500.0, 100.0, 100.0, 8.0));
		Assert.Equal(0.75, cell.NX, 6);
		Assert.Equal(0.5, cell.NY, 6);
		// 已经贴在边界上再往外拖 = 没动(返回值语义 = "位置有没有变")
		Assert.False(LayoutCanvas.Move(cell, 40.0, 40.0, 100.0, 100.0, 8.0));
		Assert.Equal(0.75, cell.NX, 6);
	}

	[Fact]
	public void CanvasResizeRespectsMinimumSize()
	{
		LayoutCell cell = new LayoutCell
		{
			Id = "a",
			NX = 0.0,
			NY = 0.0,
			NW = 0.5,
			NH = 0.5,
			MinW = LayoutCanvas.FallbackMinW,
			MinH = LayoutCanvas.FallbackMinH
		};
		// 轴 1000×1000:最小尺寸 = max(0.04, 200/1000) = 0.2 宽、max(0.04, 120/1000) = 0.12 高
		Assert.True(LayoutCanvas.Resize(cell, "se", -5000.0, -5000.0, 1000.0, 1000.0, 0.0));
		Assert.Equal(0.2, cell.NW, 6);
		Assert.Equal(0.12, cell.NH, 6);
	}

	[Fact]
	public void CanvasResizeNorthWestKeepsOppositeCorner()
	{
		LayoutCell cell = new LayoutCell
		{
			Id = "a",
			NX = 0.2,
			NY = 0.2,
			NW = 0.5,
			NH = 0.5,
			MinW = 10.0,
			MinH = 10.0
		};
		double right = cell.NX + cell.NW;
		double bottom = cell.NY + cell.NH;
		Assert.True(LayoutCanvas.Resize(cell, "nw", 100.0, 100.0, 1000.0, 1000.0, 0.0));
		Assert.Equal(0.3, cell.NX, 6);
		Assert.Equal(0.3, cell.NY, 6);
		Assert.Equal(right, cell.NX + cell.NW, 6);
		Assert.Equal(bottom, cell.NY + cell.NH, 6);
	}

	[Fact]
	public void CanvasStackedOrderSortsByZ()
	{
		List<LayoutCell> cells = new List<LayoutCell>
		{
			new LayoutCell
			{
				Id = "top",
				Z = 2
			},
			new LayoutCell
			{
				Id = "bottom",
				Z = 0
			},
			new LayoutCell
			{
				Id = "middle",
				Z = 1
			}
		};
		Assert.Equal(new[] { "bottom", "middle", "top" }, LayoutCanvas.StackedOrder(cells).Select((LayoutCell c) => c.Id).ToArray());
	}

	[Fact]
	public void CanvasApplyWritesNormalizedRectBack()
	{
		PageLayoutV2 page = Canvas("a");
		// 先给块留出可移动空间:归一化满宽(1.0)的块在画布里本来就没有可移动的余地(1 - NW = 0)
		page.Blocks[0].W = 0.5;
		page.Blocks[0].H = 0.5;
		List<LayoutCell> cells = LayoutCanvas.Load(page);
		Assert.True(LayoutCanvas.Move(cells[0], 30.0, 20.0, 100.0, 100.0, 0.0));
		LayoutCanvas.Apply(page, cells);
		Assert.Equal(0.3, page.Blocks[0].X, 6);
		Assert.Equal(0.2, page.Blocks[0].Y, 6);
		Assert.Equal(0.5, page.Blocks[0].W, 6);
		Assert.Equal(0.5, page.Blocks[0].H, 6);
	}

	// ————— 区块外观参数(§4.B):缺省必须等价现状 —————

	[Fact]
	public void DensityStepsAreNamedConstants()
	{
		Assert.Equal(6.0, LayoutAppearance.SpacingOf(LayoutSchema.DensityCompact), 6);
		Assert.Equal(10.0, LayoutAppearance.SpacingOf(LayoutSchema.DensityStandard), 6);
		Assert.Equal(14.0, LayoutAppearance.SpacingOf(LayoutSchema.DensityLoose), 6);
		// 缺省 / 未知密度 → 标准档(= 现状 BlockHost.Gap)
		Assert.Equal(10.0, LayoutAppearance.SpacingOf(null), 6);
		Assert.Equal(10.0, LayoutAppearance.SpacingOf("weird"), 6);
	}

	[Fact]
	public void DefaultDensityKeepsTodaysGap()
	{
		// 硬约束:密度缺省 standard + 页级 gap 缺省 10 → 有效间距 10(与改动前逐像素一致)
		Assert.Equal(LayoutSchema.GapDefault, LayoutAppearance.EffectiveGap(LayoutSchema.DensityStandard, LayoutSchema.GapDefault), 6);
		Assert.Equal(6.0, LayoutAppearance.EffectiveGap(LayoutSchema.DensityCompact, LayoutSchema.GapDefault), 6);
		// 页级 gap 显式偏离缺省值时,以页级为准(密度不再覆盖)
		Assert.Equal(12.0, LayoutAppearance.EffectiveGap(LayoutSchema.DensityCompact, 12.0), 6);
	}

	[Fact]
	public void StyleAndTitleDefaultToPlainAndNoTitle()
	{
		Assert.Equal(LayoutSchema.StylePlain, LayoutAppearance.StyleOf(null));
		Assert.Equal(LayoutSchema.StylePlain, LayoutAppearance.StyleOf(new BlockLayout
		{
			Id = "a"
		}));
		Assert.Equal(LayoutSchema.StylePlain, LayoutAppearance.StyleOf(new BlockLayout
		{
			Id = "a",
			Style = "unknown"
		}));
		Assert.Equal(LayoutSchema.StyleCard, LayoutAppearance.StyleOf(new BlockLayout
		{
			Id = "a",
			Style = LayoutSchema.StyleCard
		}));
		Assert.False(LayoutAppearance.ShowsTitle(null));
		Assert.False(LayoutAppearance.ShowsTitle(new BlockLayout
		{
			Id = "a"
		}));
		Assert.True(LayoutAppearance.ShowsTitle(new BlockLayout
		{
			Id = "a",
			Title = true
		}));
	}

	[Fact]
	public void BlockPadOverridesPagePadOnlyWhenSet()
	{
		double[] pagePad = new double[4] { 18.0, 14.0, 18.0, 16.0 };
		Assert.Equal(pagePad, LayoutAppearance.PadOf(new BlockLayout
		{
			Id = "a"
		}, pagePad));
		double[] own = new double[4] { 4.0, 5.0, 6.0, 7.0 };
		Assert.Equal(own, LayoutAppearance.PadOf(new BlockLayout
		{
			Id = "a",
			Pad = own
		}, pagePad));
		Assert.Equal(new double[4] { 18.0, 14.0, 18.0, 16.0 }, LayoutAppearance.PadOf(null, null));
	}

	// ————— 响应式变体(纯函数;界面监听留给 L7a 的 seam) —————

	[Fact]
	public void VariantResolvesByWidth()
	{
		Assert.Equal(LayoutVariants.Narrow, LayoutVariants.NameOf(600.0, LayoutVariants.NarrowWidthThreshold));
		Assert.Equal("", LayoutVariants.NameOf(1200.0, LayoutVariants.NarrowWidthThreshold));
		Assert.Equal("", LayoutVariants.NameOf(0.0, LayoutVariants.NarrowWidthThreshold)); // 宽度未知 → 主 body
		Assert.Equal(LayoutVariants.Narrow, LayoutVariants.NameOf(100.0, 200.0));
	}

	[Fact]
	public void VariantPageFallsBackToMainBody()
	{
		LayoutBody body = new LayoutBody();
		PageLayoutV2 main = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeStack,
			Cols = 2
		};
		main.Blocks.Add(new BlockLayout
		{
			Id = "a"
		});
		body.Pages["tasks"] = main;
		// playtime 只存在于主 body(用来验"变体里没有这一页 → 回落主 body")
		PageLayoutV2 mainOnly = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeStack,
			Cols = 3
		};
		body.Pages["playtime"] = mainOnly;
		LayoutBody narrow = new LayoutBody();
		PageLayoutV2 narrowPage = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeGrid,
			Cols = 4
		};
		narrowPage.Blocks.Add(new BlockLayout
		{
			Id = "a",
			Hidden = true
		});
		narrow.Pages["tasks"] = narrowPage;
		body.Variants[LayoutVariants.Narrow] = narrow;

		// 窄窗 + 变体里有这一页 → 用变体
		PageLayoutV2? small = LayoutVariants.PageOf(body, "tasks", 600.0, LayoutVariants.NarrowWidthThreshold);
		Assert.NotNull(small);
		Assert.Equal(LayoutSchema.ModeGrid, small!.Mode);
		Assert.Equal(4, small.Cols);
		Assert.True(small.Blocks[0].Hidden);
		// 宽窗 → 主 body
		PageLayoutV2? wide = LayoutVariants.PageOf(body, "tasks", 1400.0, LayoutVariants.NarrowWidthThreshold);
		Assert.NotNull(wide);
		Assert.Equal(LayoutSchema.ModeStack, wide!.Mode);
		Assert.Equal(2, wide.Cols);
		// 窄窗但变体里没有这一页 → 回落主 body(而不是"变体优先就整页回默认")
		PageLayoutV2? fallback = LayoutVariants.PageOf(body, "playtime", 400.0, LayoutVariants.NarrowWidthThreshold);
		Assert.NotNull(fallback);
		Assert.Equal(LayoutSchema.ModeStack, fallback!.Mode);
		Assert.Equal(3, fallback.Cols);
		// 两处都没有这一页 → null(由页面白名单默认布局兜底)
		Assert.Null(LayoutVariants.PageOf(body, "servers", 400.0, LayoutVariants.NarrowWidthThreshold));
		Assert.Null(LayoutVariants.PageOf(null, "tasks", 400.0, LayoutVariants.NarrowWidthThreshold));
		// Variants 里没有 narrow 键(比如只有别的变体名)→ 也算"没有变体",回主 body
		LayoutBody other = new LayoutBody();
		other.Pages["tasks"] = main;
		other.Variants["wide"] = new LayoutBody();
		Assert.Equal(LayoutSchema.ModeStack, LayoutVariants.PageOf(other, "tasks", 400.0, LayoutVariants.NarrowWidthThreshold)!.Mode);
		// 直接取变体 body:宽窗时返回 null
		Assert.Null(LayoutVariants.BodyOf(body, 1400.0, LayoutVariants.NarrowWidthThreshold));
		Assert.NotNull(LayoutVariants.BodyOf(body, 400.0, LayoutVariants.NarrowWidthThreshold));
	}

	// ————— D1:宿主的"可用宽"提示(变体接线的纯函数部分) —————

	[Fact]
	public void VariantHostStartsUnknownAndStaysOnMainBody()
	{
		LayoutVariantHost.Reset();
		try
		{
			Assert.Equal(0.0, LayoutVariantHost.Width, 6);
			Assert.Equal("", LayoutVariantHost.CurrentName);
			// 未知宽(0)/ 非法宽一律不做变体判定 → 用主 body,于是"没接线/首帧"的渲染与改动前一致
			Assert.False(LayoutVariantHost.Apply(0.0));
			Assert.False(LayoutVariantHost.Apply(-5.0));
			Assert.False(LayoutVariantHost.Apply(double.NaN));
			Assert.False(LayoutVariantHost.Apply(double.PositiveInfinity));
			Assert.Equal(0.0, LayoutVariantHost.Width, 6);
			Assert.Equal("", LayoutVariantHost.CurrentName);
		}
		finally
		{
			LayoutVariantHost.Reset();
		}
	}

	[Fact]
	public void VariantHostFiresOnlyWhenTheVariantNameChanges()
	{
		LayoutVariantHost.Reset();
		List<string> seen = new List<string>();
		Action<string> handler = delegate(string name)
		{
			seen.Add(name);
		};
		LayoutVariantHost.VariantChanged += handler;
		try
		{
			// 宽窗(>760)→ 主 body:名字没变("" → ""),不触发事件
			Assert.False(LayoutVariantHost.Apply(900.0));
			Assert.Empty(seen);
			// 跨到窄窗 → 名字变了,触发一次
			Assert.True(LayoutVariantHost.Apply(700.0));
			Assert.Equal(new string[1] { LayoutVariants.Narrow }, seen.ToArray());
			Assert.Equal(LayoutVariants.Narrow, LayoutVariantHost.CurrentName);
			Assert.Equal(700.0, LayoutVariantHost.Width, 6);
			// 仍在窄窗一侧(拖动窗口)→ 不再重复触发(否则每帧重建)
			Assert.False(LayoutVariantHost.Apply(650.0));
			Assert.Single(seen);
			// 回到宽窗 → 再触发一次,名字回到空串
			Assert.True(LayoutVariantHost.Apply(1000.0));
			Assert.Equal(new string[2] { LayoutVariants.Narrow, "" }, seen.ToArray());
			Assert.False(LayoutVariantHost.Apply(1200.0));
			Assert.Equal(2, seen.Count);
		}
		finally
		{
			LayoutVariantHost.VariantChanged -= handler;
			LayoutVariantHost.Reset();
		}
	}

	[Fact]
	public void VariantHostThresholdIsConfigurableAndRecomputes()
	{
		LayoutVariantHost.Reset();
		try
		{
			Assert.Equal(LayoutVariants.NarrowWidthThreshold, LayoutVariantHost.Threshold, 6);
			// 显式阈值 500:600 宽不算窄窗
			Assert.False(LayoutVariantHost.Apply(600.0, 500.0));
			Assert.Equal("", LayoutVariantHost.CurrentName);
			// 400 宽 < 500 → 窄窗
			Assert.True(LayoutVariantHost.Apply(400.0, 500.0));
			Assert.Equal(LayoutVariants.Narrow, LayoutVariantHost.CurrentName);
			// 阈值改成 300 之后重新判定同一宽度:400 > 300 → 回到主 body
			LayoutVariantHost.Threshold = 300.0;
			Assert.Equal("", LayoutVariantHost.CurrentName);
			Assert.Equal(300.0, LayoutVariantHost.Threshold, 6);
			// 非法阈值 → 回落契约默认
			LayoutVariantHost.Threshold = 0.0;
			Assert.Equal(LayoutVariants.NarrowWidthThreshold, LayoutVariantHost.Threshold, 6);
		}
		finally
		{
			LayoutVariantHost.Reset();
		}
	}

	[Fact]
	public void VariantHostSurvivesThrowingSubscribers()
	{
		LayoutVariantHost.Reset();
		Action<string> bad = delegate(string name)
		{
			throw new InvalidOperationException("boom");
		};
		LayoutVariantHost.VariantChanged += bad;
		try
		{
			// 订阅者自己的异常不能影响窗口尺寸这条路径(照 LayoutStoreV2.RaiseChanged 的范式)
			Assert.True(LayoutVariantHost.Apply(600.0));
			Assert.Equal(LayoutVariants.Narrow, LayoutVariantHost.CurrentName);
		}
		finally
		{
			LayoutVariantHost.VariantChanged -= bad;
			LayoutVariantHost.Reset();
		}
	}

	[Fact]
	public void VariantHostWidthDrivesPageOfLikeTheEditorDoes()
	{
		// 编辑器 LoadFromStore 的取页逻辑 = PageOf(doc.Layout, pageId, 可用宽, 阈值);
		// 这条用例把"宿主喂宽度 → 取到哪一份页面"的因果关系钉死。
		LayoutVariantHost.Reset();
		try
		{
			LayoutBody body = new LayoutBody();
			body.Pages["tasks"] = new PageLayoutV2
			{
				Mode = LayoutSchema.ModeStack,
				Cols = 2
			};
			LayoutBody narrow = new LayoutBody();
			narrow.Pages["tasks"] = new PageLayoutV2
			{
				Mode = LayoutSchema.ModeGrid,
				Cols = 4
			};
			body.Variants[LayoutVariants.Narrow] = narrow;

			// 还没接线(宽未知)→ 主 body
			Assert.Equal(LayoutSchema.ModeStack, LayoutVariants.PageOf(body, "tasks", LayoutVariantHost.Width, LayoutVariantHost.Threshold)!.Mode);
			// 宿主喂 700 → 变体
			LayoutVariantHost.Apply(700.0);
			PageLayoutV2? small = LayoutVariants.PageOf(body, "tasks", LayoutVariantHost.Width, LayoutVariantHost.Threshold);
			Assert.NotNull(small);
			Assert.Equal(LayoutSchema.ModeGrid, small!.Mode);
			Assert.Equal(4, small.Cols);
			// 宿主喂 900 → 主 body
			LayoutVariantHost.Apply(900.0);
			Assert.Equal(LayoutSchema.ModeStack, LayoutVariants.PageOf(body, "tasks", LayoutVariantHost.Width, LayoutVariantHost.Threshold)!.Mode);
		}
		finally
		{
			LayoutVariantHost.Reset();
		}
	}
}
