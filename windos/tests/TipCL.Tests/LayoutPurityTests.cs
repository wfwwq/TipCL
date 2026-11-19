using System;
using System.Collections.Generic;
using System.Text;
using TipCL.Core.Launcher.Layout;
using TipCL.Pages.Layout;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 布局模型层纯函数不变量(不建窗口):
///   P0 网格行号不被列数压回;P1 Compact 幂等;P2 canvas 矩形不越界 / NaN 防御 /
///   固定 height_px 的行不分视口余量 / 缩放预览不出画布。
/// </summary>
public class LayoutPurityTests
{
	private static LayoutCell Cell(string id, int x, int y, int w = 1, int h = 1)
	{
		return new LayoutCell { Id = id, X = x, Y = y, W = w, H = h };
	}

	private static List<LayoutCell> Clone(List<LayoutCell> src)
	{
		List<LayoutCell> copy = new List<LayoutCell>();
		foreach (LayoutCell cell in src)
		{
			copy.Add(cell.Clone());
		}
		return copy;
	}

	private static string Dump(List<LayoutCell> cells)
	{
		StringBuilder sb = new StringBuilder();
		foreach (LayoutCell cell in cells)
		{
			sb.Append(cell.Id).Append('=').Append(cell.X).Append(',').Append(cell.Y)
				.Append(',').Append(cell.W).Append(',').Append(cell.H).Append(' ');
		}
		return sb.ToString();
	}

	// ————— P0:网格行号 / 行跨度与列数无关 —————

	[Fact]
	public void GridBlockRowBeyondColumnsSurvivesSanitizeAndRoundTrip()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{
		  "version": 2,
		  "kind": "tipcl-layout",
		  "layout": {
		    "pages": {
		      "p": { "version": 2, "mode": "grid", "cols": 6,
		              "blocks": [{ "id": "a", "x": 0, "y": 20, "w": 2, "h": 3 }] }
		    }
		  }
		}
		""");
		BlockLayout block = doc.Layout.Pages["p"].Blocks[0];
		Assert.Equal(20.0, block.Y);
		Assert.Equal(3.0, block.H);

		// 存盘/重读一次不得被压回 cols 行(旧实现每次 Save 都过 Sanitize)
		LayoutV2 again = LayoutStoreV2.ParseImport(LayoutStoreV2.ExportJson(doc));
		BlockLayout block2 = again.Layout.Pages["p"].Blocks[0];
		Assert.Equal(20.0, block2.Y);
		Assert.Equal(3.0, block2.H);
	}

	[Fact]
	public void GridBlockRowAndSpanUseDefensiveRowMaxUpperBound()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{
		  "version": 2,
		  "kind": "tipcl-layout",
		  "layout": {
		    "pages": {
		      "p": { "version": 2, "mode": "grid", "cols": 6,
		              "blocks": [{ "id": "a", "x": 0, "y": 1000000, "w": 2, "h": 1000000 }] }
		    }
		  }
		}
		""");
		BlockLayout block = doc.Layout.Pages["p"].Blocks[0];
		Assert.Equal(LayoutSchema.RowMax, block.Y);
		Assert.Equal(LayoutSchema.RowMax, block.H);
	}

	[Fact]
	public void GridPackAndResizeAllowRowsTallerThanColumns()
	{
		// Pack 以前把 h 钳到 cols:2 列网格里 5 行高的块一装载就变 2 行
		PageLayoutV2 page = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeGrid,
			Cols = 2,
			Blocks = new List<BlockLayout>
			{
				new BlockLayout { Id = "a", X = 0.0, Y = 0.0, W = 1.0, H = 5.0 }
			}
		};
		LayoutCell packed = Assert.Single(LayoutGrid.Pack(page));
		Assert.Equal(5, packed.H);

		LayoutCell cell = Cell("b", 0, 0, 1, 5);
		Assert.True(LayoutGrid.ResizeCell(cell, "s", 0, 3, 2));
		Assert.Equal(8, cell.H);
	}

	// ————— P1:Compact 幂等 —————

	[Fact]
	public void CompactIsIdempotentForTheSwapRegressionCase()
	{
		// 旧实现第二遍压紧会把 b0 从 (1,2) 挪到 (1,3):跨列块改变了排序顺序。
		List<LayoutCell> cells = new List<LayoutCell>
		{
			Cell("b0", 0, 3, 1, 2),
			Cell("b1", 0, 1, 1, 2),
			Cell("b2", 1, 1, 1, 1),
			Cell("b3", 0, 5, 2, 1),
			Cell("b4", 0, 1, 2, 1)
		};
		List<string> pinned = new List<string> { "b2" };
		List<LayoutCell> once = Clone(cells);
		LayoutGrid.Compact(once, 2, pinned, swapX: true, gravity: true);
		List<LayoutCell> twice = Clone(once);
		LayoutGrid.Compact(twice, 2, pinned, swapX: true, gravity: true);
		Assert.Equal(Dump(once), Dump(twice));
	}

	[Fact]
	public void CompactIsIdempotentOverDeterministicRandomGrids()
	{
		Random rng = new Random(20261119);
		for (int iter = 0; iter < 4000; iter++)
		{
			int cols = new[] { 2, 3, 4, 6, 12 }[rng.Next(5)];
			int n = 2 + rng.Next(6);
			List<LayoutCell> cells = new List<LayoutCell>();
			for (int i = 0; i < n; i++)
			{
				int w = 1 + rng.Next(cols);
				int h = 1 + rng.Next(3);
				cells.Add(Cell("b" + i, rng.Next(Math.Max(1, cols - w + 1)), rng.Next(8), w, h));
			}
			List<string> pinned = new List<string>();
			if (rng.Next(3) != 0)
			{
				pinned.Add("b" + rng.Next(n));
			}
			bool swap = rng.Next(2) == 0;
			bool gravity = rng.Next(5) != 0;
			List<LayoutCell> once = Clone(cells);
			LayoutGrid.Compact(once, cols, pinned, swap, gravity);
			List<LayoutCell> twice = Clone(once);
			LayoutGrid.Compact(twice, cols, pinned, swap, gravity);
			Assert.Equal(Dump(once), Dump(twice));
		}
	}

	[Fact]
	public void CompactWithoutGravityStillKeepsUserGaps()
	{
		// gravity:false 的既有语义不变:空位原样保留,不上浮、不左右互换。
		List<LayoutCell> cells = new List<LayoutCell>
		{
			Cell("a", 0, 0),
			Cell("b", 0, 5)
		};
		LayoutGrid.Compact(cells, 12, null, swapX: true, gravity: false);
		Assert.Equal(0, cells[0].Y);
		Assert.Equal(5, cells[1].Y);
		Assert.Equal(0, cells[1].X);
	}

	// ————— P2:canvas 不变量 x+w<=1 —————

	[Fact]
	public void SanitizeCanvasKeepsRectInsideUnitSquare()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{
		  "version": 2,
		  "kind": "tipcl-layout",
		  "layout": {
		    "pages": {
		      "p": { "version": 2, "mode": "canvas",
		              "blocks": [{ "id": "a", "x": 0.9, "y": 0.9, "w": 0.8, "h": 0.8 }] }
		    }
		  }
		}
		""");
		BlockLayout block = doc.Layout.Pages["p"].Blocks[0];
		Assert.True(block.X + block.W <= 1.0 + 1e-9);
		Assert.True(block.Y + block.H <= 1.0 + 1e-9);
		Assert.Equal(1.0 - 0.9, block.W, 9); // 宽度收到剩余空间(不小于 CanvasMinSize)
		Assert.Equal(1.0 - 0.9, block.H, 9);
	}

	[Fact]
	public void CanvasPlaceKeepsPixelRectInsideHost()
	{
		LayoutCell cell = new LayoutCell { Id = "a", NX = 0.9, NY = 0.8, NW = 0.5, NH = 0.6 };
		LayoutRect rect = LayoutCanvas.Place(cell, 1000.0, 800.0);
		Assert.True(rect.X >= 0.0 && rect.Y >= 0.0);
		Assert.True(rect.X + rect.W <= 1000.0 + 1e-9);
		Assert.True(rect.Y + rect.H <= 800.0 + 1e-9);
	}

	[Fact]
	public void CanvasResizeNeverLeavesTheUnitSquare()
	{
		LayoutCell cell = new LayoutCell
		{
			Id = "a", NX = 0.2, NY = 0.2, NW = 0.4, NH = 0.4, MinW = 0.0, MinH = 0.0
		};
		Assert.True(LayoutCanvas.Resize(cell, "w", -2000.0, 0.0, 1000.0, 800.0, 0.0));
		Assert.True(cell.NX >= 0.0);
		Assert.True(cell.NX + cell.NW <= 1.0 + 1e-9);

		cell = new LayoutCell
		{
			Id = "b", NX = 0.2, NY = 0.2, NW = 0.4, NH = 0.4, MinW = 0.0, MinH = 0.0
		};
		Assert.True(LayoutCanvas.Resize(cell, "n", 0.0, -2000.0, 1000.0, 800.0, 0.0));
		Assert.True(cell.NY >= 0.0);
		Assert.True(cell.NY + cell.NH <= 1.0 + 1e-9);
	}

	[Fact]
	public void CanvasResizeNearRightEdgeStillHonoursCanvasMinSize()
	{
		LayoutCell cell = new LayoutCell
		{
			Id = "a", NX = 0.99, NY = 0.0, NW = 0.01, NH = 0.2, MinW = 0.0, MinH = 0.0
		};
		LayoutCanvas.Resize(cell, "e", 0.0, 0.0, 1000.0, 800.0, 0.0);
		Assert.Equal(1.0 - LayoutSchema.CanvasMinSize, cell.NX, 9);
		Assert.Equal(LayoutSchema.CanvasMinSize, cell.NW, 9);
		Assert.True(cell.NX + cell.NW <= 1.0 + 1e-9);
	}

	// ————— P2:NaN / Infinity 防御 —————

	[Fact]
	public void FillRowsIgnoresNonFiniteRowHeightsAndGap()
	{
		List<LayoutCell> cells = new List<LayoutCell> { Cell("fill", 0, 0), Cell("other", 6, 0, 6) };
		double[] result = LayoutGrid.FillRows(
			cells,
			new double[] { double.NaN, 120.0 },
			new HashSet<string> { "fill" },
			double.NaN,
			800.0);
		foreach (double h in result)
		{
			Assert.True(double.IsFinite(h));
		}
		Assert.Equal(800.0, result[0] + result[1], 6); // gap 回落后总计恰好等于视口高
	}

	[Fact]
	public void ColumnWidthFallsBackOnNonFiniteInputs()
	{
		Assert.Equal(1.0, LayoutGrid.ColumnWidth(double.NaN, 3, 10.0), 9);
		Assert.Equal(1.0, LayoutGrid.ColumnWidth(double.PositiveInfinity, 3, 10.0), 9);
		Assert.Equal(400.0, LayoutGrid.ColumnWidth(1200.0, 3, double.NaN), 9); // gap 回落到 0
	}

	[Fact]
	public void RowHeightsIgnoresNonFiniteMeasuredAndMinHeights()
	{
		List<LayoutCell> cells = new List<LayoutCell> { Cell("a", 0, 0) };
		cells[0].MinH = double.NaN;
		double[] heights = LayoutGrid.RowHeights(cells, 1, double.NaN, new Dictionary<string, double> { ["a"] = double.NaN }, double.NaN);
		Assert.Single(heights);
		Assert.Equal(LayoutCanvas.FallbackMinH, heights[0], 9);
	}

	// ————— P2:height_px>0 的行不分视口余量 —————

	[Fact]
	public void FillRowsSkipsRowsWithFixedPixelHeight()
	{
		List<LayoutCell> cells = new List<LayoutCell> { Cell("fill", 0, 0), Cell("chat", 0, 1) };
		cells[0].HeightPx = 160.0; // 固定像素高的 Fill 行不能再被拉高
		double[] result = LayoutGrid.FillRows(
			cells,
			new double[] { 160.0, 120.0 },
			new HashSet<string> { "fill", "chat" },
			0.0,
			800.0);
		Assert.Equal(160.0, result[0], 6);
		Assert.Equal(640.0, result[1], 6);
	}

	[Fact]
	public void FillRowsLeavesOnlyFixedRowsUntouched()
	{
		List<LayoutCell> cells = new List<LayoutCell> { Cell("fill", 0, 0) };
		cells[0].HeightPx = 160.0;
		double[] rows = new double[] { 160.0 };
		double[] result = LayoutGrid.FillRows(cells, rows, new HashSet<string> { "fill" }, 0.0, 800.0);
		Assert.Equal(160.0, result[0], 6);
	}

	// ————— P2:缩放预览不出画布 —————

	[Fact]
	public void ResizePreviewClampsNegativeCoordinates()
	{
		LayoutRect anchor = new LayoutRect(10.0, 10.0, 100.0, 80.0);
		LayoutRect preview = LayoutMotion.ResizePreview(anchor, "nw", -50.0, -40.0, 50.0, 56.0);
		Assert.Equal(0.0, preview.X, 9);
		Assert.Equal(0.0, preview.Y, 9);
		Assert.Equal(110.0, preview.Right, 9);  // 对边保持不动
		Assert.Equal(90.0, preview.Bottom, 9);
	}

	[Fact]
	public void ResizePreviewClampsIntoGivenCanvasBounds()
	{
		// 归一化画布:canvas 传 1, 1 时即夹到 0..1
		LayoutRect anchor = new LayoutRect(0.1, 0.1, 0.8, 0.8);
		LayoutRect preview = LayoutMotion.ResizePreview(anchor, "e", 0.5, 0.5, 0.04, 0.04, 1.0, 1.0);
		Assert.True(preview.X >= 0.0 && preview.Y >= 0.0);
		Assert.True(preview.X + preview.W <= 1.0 + 1e-9);
		Assert.True(preview.Y + preview.H <= 1.0 + 1e-9);
	}
}