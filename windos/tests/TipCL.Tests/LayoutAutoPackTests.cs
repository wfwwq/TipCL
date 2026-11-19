using System.Collections.Generic;
using TipCL.Pages.Layout;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 2026-11-19 录屏反馈:「自动靠到和它近的窗口上」要能关。
/// LayoutGrid.Compact(gravity:false) = 只解冲突:不上浮、不左右互换,用户拖出来的空隙保留。
/// </summary>
public class LayoutAutoPackTests
{
	private static LayoutCell Cell(string id, int x, int y, int w = 4, int h = 1)
	{
		return new LayoutCell { Id = id, X = x, Y = y, W = w, H = h };
	}

	[Fact]
	public void GravityOff_KeepsGapsWhereUserLeftThem()
	{
		List<LayoutCell> cells = new List<LayoutCell>
		{
			Cell("a", 0, 0),
			Cell("b", 4, 3),
			Cell("c", 8, 2)
		};
		LayoutGrid.Compact(cells, 12, new[] { "b" }, swapX: true, gravity: false);
		Assert.Equal(3, cells.Find(c => c.Id == "b")!.Y);
		Assert.Equal(2, cells.Find(c => c.Id == "c")!.Y); // 不上浮
		Assert.Equal(8, cells.Find(c => c.Id == "c")!.X); // 不互换
	}

	[Fact]
	public void GravityOff_StillResolvesOverlapByPushingDown()
	{
		List<LayoutCell> cells = new List<LayoutCell>
		{
			Cell("drag", 0, 1, 6),
			Cell("other", 2, 1, 4)
		};
		LayoutGrid.Compact(cells, 12, new[] { "drag" }, swapX: false, gravity: false);
		LayoutCell other = cells.Find(c => c.Id == "other")!;
		Assert.Equal(2, other.X);
		Assert.Equal(2, other.Y); // 被固定块占住,只在本列往下让一格
	}

	[Fact]
	public void GravityOn_IsTheOldBehaviour()
	{
		List<LayoutCell> a = new List<LayoutCell> { Cell("a", 0, 3), Cell("b", 4, 5) };
		List<LayoutCell> b = new List<LayoutCell> { Cell("a", 0, 3), Cell("b", 4, 5) };
		LayoutGrid.Compact(a, 12, null, swapX: false);
		LayoutGrid.Compact(b, 12, null, swapX: false, gravity: true);
		Assert.Equal(a[0].Y, b[0].Y);
		Assert.Equal(a[1].Y, b[1].Y);
		Assert.Equal(0, b[0].Y);
	}
}
