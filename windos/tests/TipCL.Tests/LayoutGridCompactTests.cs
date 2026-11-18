using System;
using System.Collections.Generic;
using TipCL.Core.Launcher.Layout;
using TipCL.Pages.Layout;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 录屏两轮修复里的网格/高度行为(纯模型层,不碰 WPF):
///   1. LayoutGrid.Compact —— 重力上浮(不留空洞)、被拖块固定、左右互换;
///   2. RebuildPage 保留坐标/尺寸/外观(拖完弹回原位的根因);
///   3. height_px 的读写往返。
/// </summary>
public class LayoutGridCompactTests
{
	private static LayoutCell Cell(string id, int x, int y, int w = 4, int h = 1)
	{
		return new LayoutCell
		{
			Id = id,
			X = x,
			Y = y,
			W = w,
			H = h
		};
	}

	[Fact]
	public void Compact_PullsBlocksUpIntoTheHoleLeftByDragging()
	{
		// B 被拖到第 2 行,留下第一行的空位:A 应该浮上去,C 也往上补,不留空洞
		List<LayoutCell> cells = new List<LayoutCell>
		{
			Cell("a", 0, 0),
			Cell("b", 4, 2),
			Cell("c", 8, 1)
		};
		LayoutGrid.Compact(cells, 12, new[] { "b" }, swapX: false);
		LayoutCell a = cells.Find(c => c.Id == "a");
		LayoutCell b = cells.Find(c => c.Id == "b");
		LayoutCell c = cells.Find(c => c.Id == "c");
		Assert.Equal(2, b.Y);              // 被拖块固定在自己被拖到的位置,不被压紧拽走
		Assert.Equal(0, a.Y);              // 其余块重力上浮
		Assert.Equal(0, c.Y);
	}

	[Fact]
	public void Compact_KeepsPinnedBlockWhereItWasDropped()
	{
		List<LayoutCell> cells = new List<LayoutCell>
		{
			Cell("a", 0, 0),
			Cell("b", 0, 3)
		};
		LayoutGrid.Compact(cells, 12, new[] { "b" }, swapX: false);
		LayoutCell b = cells.Find(c => c.Id == "b");
		Assert.Equal(3, b.Y);
		Assert.Equal(0, b.X);
	}

	[Fact]
	public void Compact_SwapX_MovesTheOtherBlockToTheVacatedColumn()
	{
		// 把 folders 从左边拖到右边:versions 应该被换到左边,而不是被压到下面
		List<LayoutCell> cells = new List<LayoutCell>
		{
			Cell("folders", 8, 0, 4),
			Cell("versions", 0, 0, 8)
		};
		LayoutGrid.Compact(cells, 12, new[] { "folders" }, swapX: true);
		LayoutCell folders = cells.Find(c => c.Id == "folders");
		LayoutCell versions = cells.Find(c => c.Id == "versions");
		Assert.Equal(8, folders.X);
		Assert.Equal(0, versions.X);
		Assert.Equal(0, versions.Y);
	}

	[Fact]
	public void Compact_NeverOverlaps()
	{
		List<LayoutCell> cells = new List<LayoutCell>
		{
			Cell("a", 0, 0, 6, 2),
			Cell("b", 0, 1, 6, 1),   // 与 a 重叠
			Cell("c", 0, 1, 12, 1)   // 与前两个都重叠
		};
		LayoutGrid.Compact(cells, 12, null, swapX: false);
		for (int i = 0; i < cells.Count; i++)
		{
			for (int j = i + 1; j < cells.Count; j++)
			{
				LayoutCell p = cells[i];
				LayoutCell q = cells[j];
				bool overlap = p.X < q.X + q.W && p.X + p.W > q.X && p.Y < q.Y + q.H && p.Y + p.H > q.Y;
				Assert.False(overlap, p.Id + " 与 " + q.Id + " 重叠");
			}
		}
	}

	[Fact]
	public void RebuildPage_KeepsGeometryStyleAndTitle()
	{
		// 这是"拖完弹回原位 / 区块叠在一起 / 筛选消失"的根因守卫。
		LayoutPageState state = LayoutEngine.LoadState("t", null, new[]
		{
			new LayoutBlockRef("a", true),
			new LayoutBlockRef("b", true)
		});
		state.Page.Mode = LayoutSchema.ModeGrid;
		state.Page.Cols = 12;
		state.RebuildPage();
		BlockLayout a = state.Page.Blocks.Find(b => b.Id == "a");
		a.X = 3.0;
		a.Y = 2.0;
		a.W = 5.0;
		a.H = 2.0;
		a.Style = LayoutSchema.StyleCard;
		a.Title = true;
		a.HeightPx = 240.0;
		state.RebuildPage();
		BlockLayout again = state.Page.Blocks.Find(b => b.Id == "a");
		Assert.Equal(3.0, again.X);
		Assert.Equal(2.0, again.Y);
		Assert.Equal(5.0, again.W);
		Assert.Equal(2.0, again.H);
		Assert.Equal(LayoutSchema.StyleCard, again.Style);
		Assert.True(again.Title);
		Assert.Equal(240.0, again.HeightPx);
	}

	[Fact]
	public void HeightPx_RoundTripsThroughJson()
	{
		// 走产品自己的 JSON 通路(不直接引 System.Text.Json,避免与 TipCL 引用的版本冲突)
		LayoutV2 doc = new LayoutV2();
		doc.Layout.Pages["t"] = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeGrid,
			Cols = 12,
			Blocks = new List<BlockLayout>
			{
				new BlockLayout
				{
					Id = "a",
					X = 0.0,
					Y = 0.0,
					W = 12.0,
					H = 1.0,
					HeightPx = 192.0
				}
			}
		};
		LayoutV2? back = LayoutV2.FromJsonText(doc.ToJson());
		Assert.NotNull(back);
		Assert.Equal(192.0, back!.Layout.Pages["t"].Blocks[0].HeightPx);
	}
}