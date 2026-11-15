using System.Collections.Generic;
using TipCL.Core.Launcher.Layout;
using TipCL.Pages.Layout;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// R31 布局审计(progress/R31-LAYOUT-AUDIT.md)修掉的问题的回归测试,编号与报告一致。
/// 只用零 WPF 的类型(测试宿主是 net8,碰到 WPF 类型会加载失败)。
/// </summary>
public class AuditFixTests
{
	// ————— A-01:网格行号 / 行跨度不跟列数挂钩 —————

	[Fact]
	public void SanitizeKeepsGridRowsBeyondColumnCount()
	{
		// 2 列网格里第 5 行、跨 3 行的块:规范化后位置和尺寸都不变
		BlockLayout block = new BlockLayout { Id = "b", X = 1, Y = 5, W = 1, H = 3 };
		LayoutSchema.SanitizeBlock(block, LayoutSchema.ModeGrid, 2, 0);
		Assert.Equal(1.0, block.X);
		Assert.Equal(5.0, block.Y);
		Assert.Equal(1.0, block.W);
		Assert.Equal(3.0, block.H);
	}

	[Fact]
	public void SanitizeStillCapsAbsurdGridRows()
	{
		BlockLayout block = new BlockLayout { Id = "b", Y = 1e9, H = 1e9 };
		LayoutSchema.SanitizeBlock(block, LayoutSchema.ModeGrid, 12, 0);
		Assert.Equal(LayoutSchema.RowMax - 1, block.Y);
		Assert.Equal(LayoutSchema.RowMax, block.H);
	}

	// ————— B-01:行跨度在排版和两个竖直手柄上同一口径 —————

	[Fact]
	public void PackKeepsRowSpanTallerThanColumnCount()
	{
		PageLayoutV2 page = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeGrid,
			Cols = 2,
			Blocks = new List<BlockLayout>
			{
				new BlockLayout { Id = "tall", X = 0, Y = 0, W = 1, H = 4 },
				new BlockLayout { Id = "low", X = 1, Y = 6, W = 1, H = 1 }
			}
		};
		List<LayoutCell> cells = LayoutGrid.Pack(page);
		LayoutCell tall = cells.Find(c => c.Id == "tall")!;
		LayoutCell low = cells.Find(c => c.Id == "low")!;
		Assert.Equal(4, tall.H);
		Assert.Equal(6, low.Y);
	}

	[Fact]
	public void SouthHandleGrowsPastColumnCountLikeNorthHandle()
	{
		LayoutCell south = new LayoutCell { Id = "s", X = 0, Y = 0, W = 1, H = 2 };
		Assert.True(LayoutGrid.ResizeCell(south, "s", 0, 3, 2));
		Assert.Equal(5, south.H);

		LayoutCell north = new LayoutCell { Id = "n", X = 0, Y = 6, W = 1, H = 2 };
		Assert.True(LayoutGrid.ResizeCell(north, "n", 0, -3, 2));
		Assert.Equal(5, north.H);
		Assert.Equal(3, north.Y);
	}
}
