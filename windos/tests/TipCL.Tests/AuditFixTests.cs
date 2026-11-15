using System;
using System.Collections.Generic;
using TipCL.Core.Launcher.Layout;
using TipCL.Pages;
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

	// ————— D-04 / M-01 / G-01:编辑器工具条与实例库菜单共用的页面布局导入 —————

	[Fact]
	public void ImportV2FileReplacesWholePageAndBringsReferencedWidgets()
	{
		LayoutV2 file = new LayoutV2();
		file.Layout.Pages["p"] = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeGrid,
			Cols = 6,
			Blocks = new List<BlockLayout>
			{
				new BlockLayout { Id = "hero", X = 2, Y = 3, W = 4, H = 2 },
				new BlockLayout { Id = "custom:w-note", X = 0, Y = 0, W = 2, H = 1 },
				new BlockLayout { Id = "custom:w-mine", X = 0, Y = 1, W = 2, H = 1 },
			},
		};
		file.Layout.Variants["narrow"] = new LayoutBody();
		file.Layout.Variants["narrow"].Pages["p"] = new PageLayoutV2 { Blocks = new List<BlockLayout> { new BlockLayout { Id = "hero" } } };
		file.Layout.Widgets.Add(new WidgetDef { Id = "w-note", Type = "note", Title = "便签" });
		file.Layout.Widgets.Add(new WidgetDef { Id = "w-clock", Type = "clock", Title = "时钟" });
		file.Layout.Widgets.Add(new WidgetDef { Id = "w-mine", Type = "note", Title = "文件里的" });

		LayoutV2 doc = new LayoutV2();
		doc.Layout.Pages["p"] = new PageLayoutV2 { Blocks = new List<BlockLayout> { new BlockLayout { Id = "hero" } } };
		doc.Layout.Pages["other"] = new PageLayoutV2 { Blocks = new List<BlockLayout> { new BlockLayout { Id = "x" } } };
		doc.Layout.Widgets.Add(new WidgetDef { Id = "w-mine", Type = "note", Title = "我的" });

		Assert.Equal(1, PageLayoutIO.MergeImport(doc, LayoutStoreV2.ExportJson(file)));

		PageLayoutV2 page = doc.Layout.Pages["p"];
		Assert.Equal(LayoutSchema.ModeGrid, page.Mode);
		BlockLayout hero = page.Blocks.Find(b => b.Id == "hero")!;
		Assert.Equal(2.0, hero.X);
		Assert.Equal(3.0, hero.Y);
		Assert.Equal(4.0, hero.W);
		Assert.Equal(2.0, hero.H);
		Assert.True(doc.Layout.Pages.ContainsKey("other"));
		Assert.True(doc.Layout.Variants["narrow"].Pages.ContainsKey("p"));
		// 只带上页里引用到的自建区块;同 id 的保留用户现有的定义
		Assert.Contains(doc.Layout.Widgets, w => w.Id == "w-note");
		Assert.DoesNotContain(doc.Layout.Widgets, w => w.Id == "w-clock");
		Assert.Equal("我的", doc.Layout.Widgets.Find(w => w.Id == "w-mine")!.Title);
	}

	[Fact]
	public void ImportV1FileOnlyMergesOrderAndHidden()
	{
		LayoutV2 doc = new LayoutV2();
		doc.Layout.Pages["p"] = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeGrid,
			Cols = 6,
			Blocks = new List<BlockLayout>
			{
				new BlockLayout { Id = "a", X = 0, Y = 0, W = 3, H = 2 },
				new BlockLayout { Id = "b", X = 3, Y = 0, W = 3, H = 2 },
			},
		};
		string v1 = "{\"version\":1,\"kind\":\"tipcl-page-layout\",\"pages\":{\"p\":{\"version\":1,\"blocks\":[{\"id\":\"b\",\"hidden\":true},{\"id\":\"a\",\"hidden\":false}]}}}";

		Assert.Equal(1, PageLayoutIO.MergeImport(doc, v1));

		PageLayoutV2 page = doc.Layout.Pages["p"];
		Assert.Equal(LayoutSchema.ModeGrid, page.Mode);
		Assert.Equal(new[] { "b", "a" }, page.Blocks.ConvertAll(x => x.Id));
		Assert.True(page.Blocks[0].Hidden);
		Assert.Equal(3.0, page.Blocks[0].X);
		Assert.Equal(3.0, page.Blocks[1].W);
	}

	[Fact]
	public void ImportCountsZeroPagesAndRejectsUnknownFiles()
	{
		LayoutV2 doc = new LayoutV2();
		Assert.Equal(0, PageLayoutIO.MergeImport(doc, LayoutStoreV2.ExportJson(new LayoutV2())));
		Assert.Empty(doc.Layout.Pages);
		Assert.ThrowsAny<Exception>(() => PageLayoutIO.MergeImport(doc, "{\"kind\":\"something-else\"}"));
	}
}
