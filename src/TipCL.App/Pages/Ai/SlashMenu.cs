// M4-H P5+（R2）：斜杠命令候选列表。
//
// 输入框里打 "/" 就出现（照 Windows 版 AiPage 的斜杠浮层）：
// 上下键移动、Tab 或点一下选中、Esc 收起。Avalonia 侧不做浮层，改成贴在输入区上方的一条
// 候选条（与确认卡/选择题卡同一套「停靠区」思路，见 DECISIONS D-24）。
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using TipCL.App.Common;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Ai;

/// <summary>斜杠命令候选条。</summary>
internal sealed class SlashMenu : Border
{
    private readonly StackPanel _rows = UiClass.V(0);

    /// <summary>点某一条(或键盘选中)时回调。</summary>
    public Action<SlashCommand>? Picked { get; set; }

    public SlashMenu()
    {
        Child = new ScrollViewer
        {
            Content = _rows,
            MaxHeight = 190,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Padding = new Thickness(4);
        IsVisible = false;
        this[!BackgroundProperty] = Res.Bind("B.Paper2");
        this[!BorderBrushProperty] = Res.Bind("B.LineSoft");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        Margin = new Thickness(0, 0, 0, 6);
    }

    /// <summary>重建候选并高亮第 selected 项(负数 = 不高亮)。</summary>
    public void Show(IReadOnlyList<SlashCommand> items, int selected)
    {
        _rows.Children.Clear();
        if (items.Count == 0)
        {
            IsVisible = false;
            return;
        }
        for (int i = 0; i < items.Count; i++)
        {
            SlashCommand cmd = items[i];
            bool active = i == selected;
            var grid = UiClass.G(null, "Auto,*,Auto");
            var name = UiClass.Txt("/" + cmd.Name, 12.5, true, active ? "B.AccentText" : "B.Ink");
            grid.Add(name.VCenter(), 0, 0);
            var desc = UiClass.Txt(cmd.Desc, 11.5, false, "B.InkMuted");
            desc.Margin = new Thickness(8, 0, 0, 0);
            grid.Add(desc.VCenter(), 0, 1);
            grid.Add(UiClass.Txt(cmd.Local.Length > 0 ? "本地" : "发给助手", 10.5, false, "B.InkFaint").VCenter(), 0, 2);

            var row = new Border
            {
                Padding = new Thickness(8, 5, 8, 5),
                CornerRadius = new CornerRadius(7),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = grid,
            };
            if (active)
            {
                row[!BackgroundProperty] = Res.Bind("B.Hover");
            }
            SlashCommand captured = cmd;
            row.PointerReleased += (_, _) => Picked?.Invoke(captured);
            _rows.Children.Add(row);
        }
        IsVisible = true;
    }

    public void Hide() => IsVisible = false;
}
