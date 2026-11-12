// M2-B4：U 门面——A2 的 UiHelpers（类名 Ui 与命名空间 TipCL.App.Ui 同名，页面代码里裸写
// `Ui.` 会被命名空间遮蔽，B1/B2/B3 都踩了同一个坑）。这里集中转发并补一个 Windows 版语义的
// Btn(text, kind, click, glyph) 重载；等 UiHelpers 改名后可整体删掉。
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using UiF = TipCL.App.Ui.Ui;
using UiF2 = TipCL.App.Ui.Ui;
using TipCL.App.Ui;

namespace TipCL.App.Pages.Layout;

public static class U
{
    public static Button Btn(string text, BtnKind kind = BtnKind.Normal, EventHandler<Avalonia.Interactivity.RoutedEventArgs>? click = null, string? glyph = null)
    {
        var b = UiF.Btn(text, kind, click);
        if (!string.IsNullOrEmpty(glyph))
        {
            // 图标支持 IconLib 名(矢量 Path,描边跟随按钮前景色)与字体字形字符
            Control icon = Common.IconLib.All.ContainsKey(glyph!)
                ? Common.IconLib.Make(glyph!, "B.Ink", 13)
                : UiF.Glyph(glyph!, 13);
            if (icon is Avalonia.Controls.Shapes.Path path)
                path[!Avalonia.Controls.Shapes.Path.StrokeProperty] =
                    b.GetObservable(Button.ForegroundProperty).ToBinding();
            b.Content = UiF.H(6, icon, UiF.Txt(text));
        }
        return b;
    }

    public static Button IconBtn(string glyph, string? tip = null, EventHandler<Avalonia.Interactivity.RoutedEventArgs>? click = null, double size = 14)
        => UiF.IconBtn(glyph, tip, click, size);

    public static StackPanel V(double spacing, params Control?[] kids) => UiF.V(spacing, kids);

    public static StackPanel H(double spacing, params Control?[] kids) => UiF.H(spacing, kids);

    public static Grid G(string? rows = null, string? cols = null) => UiF.G(rows, cols);

    public static TextBlock Txt(string text = "", double size = 13, bool bold = false, string? fg = null) => UiF.Txt(text, size, bold, fg);

    public static TextBlock Muted(string text) => UiF.Muted(text);

    public static TextBlock Small(string text) => UiF.Small(text);

    public static TextBlock H2(string text) => UiF.H2(text);

    public static TextBlock H3(string text) => UiF.H3(text);

    public static Control Spring() => UiF.Spring();

    public static Border Card(Control? content = null, double padding = 12, string style = "Card") => UiF.Card(content, padding, style);

    public static Border Pane(Control? content, string bg = "B.Paper2", double radius = 8, double padding = 10) => UiF.Pane(content, bg, radius, padding);

    public static Border RowCard(Control content, EventHandler<Avalonia.Interactivity.RoutedEventArgs>? click = null, double padding = 10) => UiF.RowCard(content, click, padding);

    public static Border Tag(string text, string fg = "B.InkMuted", string bg = "B.LineSoft") => UiF.Tag(text, fg, bg);

    public static Grid Field(string label, Control control, string? hint = null, double labelWidth = 140) => UiF.Field(label, control, hint, labelWidth);

    public static Grid Section(string title, string? sub = null, Control? right = null) => UiF.Section(title, sub, right);

    public static StackPanel Empty(string glyph, string title, string? hint = null) => UiF.Empty(glyph, title, hint);

    public static TextBox Input(string placeholder = "", string text = "", double width = double.NaN) => UiF.Input(placeholder, text, width);

    public static TextBox Multi(string placeholder = "", string text = "", double height = 90) => UiF.Multi(placeholder, text, height);

    public static TextBox LogBox() => UiF.LogBox();

    public static CheckBox Switch(bool value = false, EventHandler<Avalonia.Interactivity.RoutedEventArgs>? changed = null) => UiF.Switch(value, changed);

    public static ControlTheme? Theme(string key) => UiF.Theme(key);

    public static IBrush? Brush(string key) => UiF.Brush(key);
}
