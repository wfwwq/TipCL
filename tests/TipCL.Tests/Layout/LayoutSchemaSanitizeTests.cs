// 布局 v2 契约/损坏恢复的 schema 层单测(audit/D 补覆盖)。
//
// 为什么补:LayoutV2CompatTests 与 PageLayoutIOV2Tests 钉的是"文件层面"的迁移与损坏恢复
// (v1 投影、双文件互备、导入导出的取舍)。而**手改过的/被截断过的 JSON 里那些离谱数值**
// (cols=99、x=1e9、w=0.001、z=-5、重复 id)是在 schema 层被就地钳位的 —— 那一层此前只有
// SnapCols 的 5 条断言。这里把 SanitizePage / SanitizeBlock 的兜底口径整块钉住:
// 契约写的是"未知枚举回默认、越界值夹到边界、NaN/Infinity 回默认",钳错一档就是界面错位。
//
// 纯模型层,无 IO / 无 UI。命名空间与同目录既有 Layout 测试一致(不写 namespace)。
using System;
using TipCL.Core.Launcher.Layout;
using Xunit;

public class LayoutSchemaSanitizeTests
{
    private static PageLayoutV2 PageWith(params BlockLayout[] blocks)
    {
        PageLayoutV2 page = new PageLayoutV2();
        page.Blocks.AddRange(blocks);
        return page;
    }

    private static BlockLayout Block(string id) => new BlockLayout { Id = id };

    // ————— 页级 —————

    [Fact]
    public void SanitizePage_RejectsNullAndVersionMismatch()
    {
        Assert.Null(LayoutSchema.SanitizePage(null!));

        PageLayoutV2 stale = PageWith(Block("a"));
        stale.Version = 1;
        Assert.Null(LayoutSchema.SanitizePage(stale));
    }

    [Fact]
    public void SanitizePage_SnapsUnknownEnumsAndOutOfRangeNumbers()
    {
        PageLayoutV2 page = PageWith();
        page.Mode = "weird-mode";     // 未知模式 → stack
        page.Cols = 99;               // → 12
        page.RowH = -5;               // → 0
        page.Gap = 999;               // → 32
        page.Pad = new[] { -1.0, 1e6, double.NaN, 3.0 };

        PageLayoutV2? got = LayoutSchema.SanitizePage(page);

        Assert.NotNull(got);
        Assert.Equal(LayoutSchema.ModeStack, got!.Mode);
        Assert.Equal(LayoutSchema.ColsDefault, got.Cols);
        Assert.Equal(0.0, got.RowH);
        Assert.Equal(LayoutSchema.GapMax, got.Gap);
        Assert.Equal(LayoutSchema.PadMin, got.Pad[0]);
        Assert.Equal(LayoutSchema.PadMax, got.Pad[1]);
        Assert.Equal(LayoutSchema.PadDefaultCopy()[2], got.Pad[2]); // NaN → 默认值,不是 0
    }

    [Fact]
    public void SanitizePage_DropsNullEmptyAndDuplicateBlockIds_AndRenumbersZ()
    {
        BlockLayout dup1 = Block("dup");
        BlockLayout dup2 = Block("dup");
        PageLayoutV2 page = PageWith(
            Block(""),          // 空 id:丢
            Block("keep-1"),
            dup1,
            dup2,               // 重复 id:丢(保留先出现的那个)
            Block("keep-2"));

        PageLayoutV2 got = LayoutSchema.SanitizePage(page)!;

        Assert.Equal(3, got.Blocks.Count);
        Assert.Equal(new[] { "keep-1", "dup", "keep-2" }, new[] { got.Blocks[0].Id, got.Blocks[1].Id, got.Blocks[2].Id });
        Assert.Same(dup1, got.Blocks[1]);

        // 默认 Z = -1(未设置)→ 按顺序补齐
        Assert.Equal(0, got.Blocks[0].Z);
        Assert.Equal(1, got.Blocks[1].Z);
        Assert.Equal(2, got.Blocks[2].Z);
    }

    [Fact]
    public void SanitizePage_ClampsZIntoBlockCountRange()
    {
        BlockLayout a = Block("a");
        BlockLayout b = Block("b");
        a.Z = 99;
        b.Z = -3;

        PageLayoutV2 got = LayoutSchema.SanitizePage(PageWith(a, b))!;

        Assert.InRange(got.Blocks[0].Z, 0, 1);
        Assert.InRange(got.Blocks[1].Z, 0, 1);
    }

    // ————— 块级:栅格模式 —————

    [Fact]
    public void SanitizeBlock_GridClampsXAndWToColumns_ButLeavesRowsToRowMax()
    {
        BlockLayout block = Block("b");
        block.X = 99;      // 列向上限 = cols-1
        block.W = 99;      // 列向上限 = cols
        block.Y = 99999;   // 行向与列数无关,只挡防御上限 RowMax
        block.H = 0;       // 行跨下限 1
        block.Span = 99;   // 1..cols

        BlockLayout got = LayoutSchema.SanitizeBlock(block, LayoutSchema.ModeGrid, 6, 0);

        Assert.Equal(5.0, got.X);
        Assert.Equal(6.0, got.W);
        Assert.Equal(LayoutSchema.RowMax, got.Y);
        Assert.Equal(1.0, got.H);
        Assert.Equal(6, got.Span);
    }

    [Fact]
    public void SanitizeBlock_CanvasClampsToNormalizedRange_AndNanFallsBackToDefault()
    {
        BlockLayout block = Block("c");
        block.X = 5;                 // → 1.0
        block.Y = -1;                // → 0.0
        block.W = 0.001;             // → CanvasMinSize
        block.H = double.NaN;        // → 默认 1.0

        BlockLayout got = LayoutSchema.SanitizeBlock(block, LayoutSchema.ModeCanvas, 12, 0);

        Assert.Equal(LayoutSchema.CanvasMaxPos, got.X);
        Assert.Equal(0.0, got.Y);
        Assert.Equal(LayoutSchema.CanvasMinSize, got.W);
        Assert.Equal(1.0, got.H);
    }

    [Fact]
    public void SanitizeBlock_UnknownStyleFallsBackToPlain_AndNegativeZBecomesIndex()
    {
        BlockLayout block = Block("s");
        block.Style = "neon-glow";
        block.Z = -1;

        BlockLayout got = LayoutSchema.SanitizeBlock(block, LayoutSchema.ModeStack, 12, 7);

        Assert.Equal(LayoutSchema.StylePlain, got.Style);
        Assert.Equal(7, got.Z);
        Assert.NotNull(got.Settings);
        Assert.NotNull(got.Extra);
    }

    [Fact]
    public void SanitizeBlock_NullInputProducesUsableDefaultBlock()
    {
        BlockLayout got = LayoutSchema.SanitizeBlock(null!, LayoutSchema.ModeGrid, 4, 0);
        Assert.NotNull(got);
        Assert.Equal(LayoutSchema.StylePlain, got.Style);
        Assert.Equal(1, got.Span);
        Assert.NotNull(got.Settings);
    }

    [Fact]
    public void SnapCols_IsDeterministicAndTiesGoToTheSmallerChoice()
    {
        // {2,3,4,6,12} 的最近档;等距(如 5 → 4 与 6)取小档
        Assert.Equal(2, LayoutSchema.SnapCols(1));
        Assert.Equal(4, LayoutSchema.SnapCols(5));
        Assert.Equal(12, LayoutSchema.SnapCols(99));
        foreach (int c in new[] { -100, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 1000 })
        {
            int got = LayoutSchema.SnapCols(c);
            Assert.Contains(got, new[] { 2, 3, 4, 6, 12 });
            Assert.Equal(got, LayoutSchema.SnapCols(c)); // 同输入同输出
        }
    }
}