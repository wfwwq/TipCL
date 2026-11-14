// 来源: Windows 版参考仓库 src/TipCL/Pages/PlanCard.cs(M4-H P5)。
// 计划卡:标题「执行计划 · 3 / 5」+ 进度条 + 逐项状态(完成画勾并加删除线,进行中空心点,待办空圈)。
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.App.Common;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Ai;

/// <summary>计划卡(照 Windows 版 PlanCard)。</summary>
internal sealed class PlanCard : Border
{
    internal int Done { get; }

    internal int Total { get; }

    public PlanCard(List<(string Title, string Status)> items)
    {
        (Done, Total) = TipCL.Core.Launcher.Ai.AiUiText.PlanProgress(items);

        var bar = new ProgressBar
        {
            Height = 5,
            Width = 160,
            Minimum = 0,
            Maximum = 100,
            Value = Total > 0 ? (double)Done * 100.0 / Total : 0,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var head = UiClass.G(null, "Auto,Auto,Auto,*,Auto");
        head.Add(AiIcons.Icon(AiIcons.List, 14, "B.AccentText").VCenter(), 0, 0);
        var title = UiClass.Txt("执行计划", 12.5, true, "B.Ink");
        title.Margin = new Thickness(8, 0, 0, 0);
        head.Add(title.VCenter(), 0, 1);
        var count = UiClass.Txt($"· {Done} / {Total}", 12, false, "B.InkMuted");
        count.Margin = new Thickness(8, 0, 0, 0);
        head.Add(count.VCenter(), 0, 2);
        head.Add(bar, 0, 4);

        var body = UiClass.V(2, UiExt.Mk(head, 0, 0, 0, 6));
        foreach ((string itemTitle, string status) in items)
        {
            body.Children.Add(Row(itemTitle, status));
        }

        Child = body;
        Padding = new Thickness(13, 10, 13, 10);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        MaxWidth = 760;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        this[!Border.BorderBrushProperty] = Res.Bind("B.AccentSoft2");
        this[!Border.BackgroundProperty] = Res.Bind("B.Paper");
    }

    private static Control Row(string title, string status)
    {
        bool done = status is "completed" or "done";
        bool active = status is "in_progress";

        var dot = new Grid { Width = 16, Height = 16, Margin = new Thickness(0, 2, 0, 2) };
        var ring = new Ellipse { StrokeThickness = 1.5 };
        if (done)
        {
            ring[!Shape.FillProperty] = Res.Bind("B.Accent");
            ring[!Shape.StrokeProperty] = Res.Bind("B.Accent");
            dot.Children.Add(ring);
            var check = AiIcons.Icon(AiIcons.Check, 10, "B.OnAccentStrong");
            check.StrokeThickness = 3.5;
            check.HorizontalAlignment = HorizontalAlignment.Center;
            check.VerticalAlignment = VerticalAlignment.Center;
            dot.Children.Add(check);
        }
        else if (active)
        {
            ring[!Shape.StrokeProperty] = Res.Bind("B.Accent");
            dot.Children.Add(ring);
            var inner = new Ellipse { Width = 6, Height = 6 };
            inner[!Shape.FillProperty] = Res.Bind("B.Accent");
            inner.HorizontalAlignment = HorizontalAlignment.Center;
            inner.VerticalAlignment = VerticalAlignment.Center;
            dot.Children.Add(inner);
        }
        else
        {
            ring[!Shape.StrokeProperty] = Res.Bind("B.InkFaint");
            dot.Children.Add(ring);
        }

        var text = UiClass.Txt(title, 12.5, active, done ? "B.InkMuted" : "B.Ink").Wrap();
        if (done)
        {
            text.TextDecorations = TextDecorations.Strikethrough;
        }

        var row = UiClass.G(null, "Auto,*");
        row.Margin = new Thickness(0, 2, 0, 2);
        row.Add(dot, 0, 0);
        var label = text;
        label.Margin = new Thickness(9, 0, 0, 0);
        row.Add(label.VCenter(), 0, 1);
        return row;
    }
}
