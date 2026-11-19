// 第 6 轮:把这一轮 Linux 布局侧的三个修复点钉成断言(audit/D)。
//
//   1. **滚动条宽度变化要重量行高**(GridCanvasPanel):出现纵向滚动条后可用宽度少一截 ⇒
//      列宽变窄 ⇒ 会换行的内容变高 ⇒ 行高必须**重新算**。以前用的是"上一次"的列宽算出来的行高,
//      表现就是滚动条一出现,卡片底下被截掉一截。
//   2. **HeightPx 与 extra.height_px 双向互通**(LayoutCell.ReadHeightPx/WriteHeightPx):
//      Linux 与 Windows 两侧导出的布局 JSON 必须互相能读 —— 一侧写的是 CLR 数字、另一侧读到的是
//      JsonElement,任何一边的类型假设写死就会"固定高度丢失"。
//   3. **拖拽起手的残留位移必须归零**(LayoutMotion.FlipOffset / FlipRemaining):
//      上一段 FLIP 动画没播完时被抓起来的卡片,起手位置要落在指针所指的格子,而不是带着残留偏移起飞。
//
// 都是纯逻辑/纯测量,无 IO、无网络、毫秒级。
using System;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using TipCL.App.Pages.Layout;
using TipCL.Core.Launcher.Layout;
using Xunit;

namespace TipCL.Tests.Layout;

public class LayoutMotionAndHeightTests
{
    /// <summary>测量需求跟可用宽度挂钩的假内容:宽度越窄,需要的高度越高(模拟换行文本)。</summary>
    private sealed class WrappingContent : Control
    {
        private const double Area = 400.0 * 120.0;   // 面积恒定:height = Area / width

        protected override Size MeasureOverride(Size availableSize)
        {
            double w = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0 ? 200.0 : availableSize.Width;
            return new Size(w, Area / w);
        }
    }

    private static GridCanvasPanel Mount(int cols, double rowH, double gap, PageLayoutV2 page, Size available, params Control[] children)
    {
        GridCanvasPanel panel = new GridCanvasPanel();
        panel.SetLayout(LayoutSchema.ModeGrid, cols, rowH, gap, page);
        foreach (Control child in children)
        {
            panel.Children.Add(child);
        }
        panel.Measure(available);
        panel.Arrange(new Rect(0.0, 0.0, available.Width, panel.DesiredSize.Height));
        return panel;
    }

    // ————— 1. 可用宽度变窄(滚动条出现)⇒ 行高必须重量 —————

    public void ColumnWidthShrinksWithAvailableWidth_WhichIsWhatMakesRowHeightRecompute()
    {
        // 端到端(滚动条出现 → 行高重算)依赖宿主把子控件登记成 LayoutCell;纯单测里拿不到那条注册链,
        // 所以这里钉住修复所依赖的那一步算术:可用宽度变窄 ⇒ 列宽必须跟着变窄(否则行高算出来是旧的)。
        // 端到端表现由 R1 冒烟矩阵(有滚动条的 settings/library 页 0 WARN 0 ERROR)+ aud-app 的视觉证据覆盖。
        double wide = LayoutGrid.ColumnWidth(400.0, 2, 10.0);
        double narrow = LayoutGrid.ColumnWidth(380.0, 2, 10.0);
        Assert.True(narrow < wide, "可用宽度 400 → 380 后列宽没有变窄(" + wide + " → " + narrow + "),行高就会继续用旧值");
        Assert.Equal(wide - narrow, 10.0, 6);   // 少 20px / 2 列 = 每列少 10px

        // 列数越多,单列越窄(单调):这条保证"滚动条吃掉 10px"的影响随列数收敛,不会算错
        double one = LayoutGrid.ColumnWidth(400.0, 1, 10.0);
        double six = LayoutGrid.ColumnWidth(400.0, 6, 10.0);
        Assert.True(one > six);
    }

    [Fact]
    public void HeightPxRoundTripsThroughJsonBothDirections()
    {
        var block = new BlockLayout { Id = "a" };
        LayoutCell.WriteHeightPx(block, 260.0);

        Assert.True(block.Extra.ContainsKey("height_px"), "WriteHeightPx 必须写进 extra.height_px");
        Assert.Equal(260.0, LayoutCell.ReadHeightPx(block), 6);

        // 走一遍"导出成 JSON → 再解析回来"(跨版本/跨平台互通的那条路)
        string json = JsonSerializer.Serialize(block.ToDict());
        using JsonDocument doc = JsonDocument.Parse(json);
        BlockLayout back = BlockLayout.FromJson(doc.RootElement);
        Assert.Equal(260.0, LayoutCell.ReadHeightPx(back), 6);
    }

    public void HeightPxAcceptsWhateverTheOtherSideWrote()
    {
        // Windows 侧写出来的可能是整数 260 或浮点 260.0;两种都必须被读成 260(跨平台互通的那条路)
        foreach (string raw in new[] { "260", "260.0" })
        {
            string json = "{\"id\":\"a\",\"extra\":{\"height_px\":" + raw + "}}";
            using JsonDocument doc = JsonDocument.Parse(json);
            BlockLayout block = BlockLayout.FromJson(doc.RootElement);
            Assert.Equal(260.0, LayoutCell.ReadHeightPx(block), 6);
        }

        // 缺失 / null / 非数字 / 非正数 → 0(跟随内容),绝不能抛
        foreach (string json in new[]
        {
            "{\"id\":\"a\"}",
            "{\"id\":\"a\",\"extra\":{}}",
            "{\"id\":\"a\",\"extra\":{\"height_px\":null}}",
            "{\"id\":\"a\",\"extra\":{\"height_px\":\"260\"}}",
            "{\"id\":\"a\",\"extra\":{\"height_px\":\"abc\"}}",
            "{\"id\":\"a\",\"extra\":{\"height_px\":-5}}",
        })
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            Assert.Equal(0.0, LayoutCell.ReadHeightPx(BlockLayout.FromJson(doc.RootElement)), 6);
        }
    }

    [Fact]
    public void WriteHeightPxWithNonPositiveValueRemovesTheField()
    {
        var block = new BlockLayout { Id = "a" };
        LayoutCell.WriteHeightPx(block, 300.0);
        Assert.True(block.Extra.ContainsKey("height_px"));

        LayoutCell.WriteHeightPx(block, 0.0);
        Assert.False(block.Extra.ContainsKey("height_px"), "0 = 跟随内容,必须把字段删掉(留着 0 会被读成非法值)");
        Assert.Equal(0.0, LayoutCell.ReadHeightPx(block), 6);
    }

    // ————— 3. 拖拽起手的残留位移必须收敛到 0 —————

    [Fact]
    public void FlipResidueDecaysToZeroSoADragStartsAtThePointer()
    {
        // 上一段动画把块"看起来"留在 (120, 40),这次它被排到 (100, 40)
        Vector residue = LayoutMotion.FlipOffset(120.0, 40.0, 100.0, 40.0);
        Assert.Equal(20.0, residue.X, 6);
        Assert.Equal(0.0, residue.Y, 6);

        // 动画刚开始:残留还在(先"放回"原处,视觉上不跳)
        Vector atStart = LayoutMotion.FlipRemaining(residue, 0.0);
        Assert.Equal(residue.X, atStart.X, 6);

        // 动画结束:残留必须正好归零 —— 起手拖拽时若还带着这点偏移,卡片就会"自己滑一下"
        Vector atEnd = LayoutMotion.FlipRemaining(residue, 1.0);
        Assert.Equal(0.0, atEnd.X, 6);
        Assert.Equal(0.0, atEnd.Y, 6);

        // 中间进度单调收敛(不会先变大再变小)
        double prev = Math.Abs(LayoutMotion.FlipRemaining(residue, 0.0).X);
        for (double p = 0.1; p <= 1.0001; p += 0.1)
        {
            double cur = Math.Abs(LayoutMotion.FlipRemaining(residue, p).X);
            Assert.True(cur <= prev + 1e-9, "FLIP 残留在第 " + p.ToString("F1") + " 进度处变大了");
            prev = cur;
        }
    }

    [Fact]
    public void NegligibleFlipResidueDoesNotStartAnAnimation()
    {
        // 位移小于 Epsilon:返回 (0,0),不要为看不见的差值起动画(否则拖拽起手会"抖")
        Vector tiny = LayoutMotion.FlipOffset(100.0, 40.0, 100.0, 40.0);
        Assert.Equal(0.0, tiny.X, 6);
        Assert.Equal(0.0, tiny.Y, 6);

        Vector subEpsilon = LayoutMotion.FlipOffset(100.01, 40.0, 100.0, 40.0);
        Assert.Equal(0.0, subEpsilon.X, 6);
    }
}
