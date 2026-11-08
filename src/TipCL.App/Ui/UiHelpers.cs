using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace TipCL.App.Ui;

/// <summary>按钮样式档位，对应 Windows 版 Services/Ui.cs 的 BtnKind。</summary>
public enum BtnKind { Normal, Primary, Soft, Ghost, Danger, Chip, Icon }

/// <summary>
/// UI 门面 API（移植自 Windows 版 src/TipCL/Services/Ui.cs，Avalonia 实现）。
/// 样式键名与 Themes/Controls.axaml 一致：Card / Btn.* / Input.* / Combo / Chk / Switch / T.*。
/// 主题色一律走 DynamicResource 观察绑定，明暗切换即时生效。
/// </summary>
public static class Ui
{
    public const string FontUi = "Microsoft YaHei UI, Microsoft YaHei, Noto Sans CJK SC, WenQuanYi Micro Hei, PingFang SC, sans-serif";
    public const string FontMono = "Cascadia Mono, Consolas, DejaVu Sans Mono, Noto Sans Mono CJK SC, monospace";
    public const string FontIcon = "Segoe Fluent Icons, Segoe MDL2 Assets, TipCL Icons";

    /// <summary>当前是否深色主题（ApplyTheme 维护）。</summary>
    public static bool IsDark { get; private set; }

    public static event Action? ThemeChanged;

    /// <summary>
    /// 兜底挂载：若 App.axaml 未静态合并 Themes/Controls.axaml，则在首次 ApplyTheme 时
    /// 以代码方式挂到 Application.Resources（A1 地盘的 App.axaml 归属不变，见 progress/M1-A2.md 依赖申请）。
    /// </summary>
    public static void EnsureMounted()
    {
        var app = Application.Current;
        if (app == null) return;
        var dicts = app.Resources.MergedDictionaries;
        foreach (var d in dicts)
            if (d is ResourceInclude ri && ri.Source?.OriginalString.Contains("Controls.axaml") == true)
                return;
        dicts.Add(new ResourceInclude(new Uri("avares://TipCL.App/Themes/Controls.axaml")));
    }

    /// <summary>替换 Application.Resources 里的 Palette 资源字典，实现明暗切换。</summary>
    public static void ApplyTheme(bool dark)
    {
        IsDark = dark;
        EnsureMounted();
        var app = Application.Current;
        if (app == null) return;
        var dicts = app.Resources.MergedDictionaries;
        var include = new ResourceInclude(
            new Uri($"avares://TipCL.App/Themes/{(dark ? "Palette.Dark.axaml" : "Palette.Light.axaml")}"));
        for (int i = 0; i < dicts.Count; i++)
        {
            if (dicts[i] is ResourceInclude ri && ri.Source?.OriginalString.Contains("Palette.") == true)
            {
                dicts[i] = include;
                ThemeChanged?.Invoke();
                return;
            }
        }
        dicts.Add(include);
        ThemeChanged?.Invoke();
    }

    /// <summary>按键取任意资源（刷子 / ControlTheme / 圆角等）。</summary>
    public static object? Res(string key)
        => Application.Current != null
           && Application.Current.TryGetResource(key, null, out var v) ? v : null;

    public static IBrush? Brush(string key) => Res(key) as IBrush;

    /// <summary>按键取 ControlTheme，赋给 control.Theme 用。</summary>
    public static ControlTheme? Theme(string key) => Res(key) as ControlTheme;

    /// <summary>把资源键以 DynamicResource 观察绑定挂到属性上（明暗切换即时跟随）。</summary>
    public static T Dyn<T>(this T control, AvaloniaProperty property, string key) where T : AvaloniaObject
    {
        Application.Current?.GetResourceObservable(key)
            .Subscribe(new ObserverAdapter(v => control.SetValue(property, v)));
        return control;
    }

    private sealed class ObserverAdapter : IObserver<object?>
    {
        private readonly Action<object?> _onNext;
        public ObserverAdapter(Action<object?> onNext) => _onNext = onNext;
        public void OnNext(object? value) => _onNext(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }

    // ===== 文本 =====

    public static TextBlock Txt(string text = "", double size = 13, bool bold = false, string? fg = null)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontFamily = new FontFamily(FontUi),
            TextWrapping = TextWrapping.NoWrap
        };
        if (bold) t.FontWeight = FontWeight.SemiBold;
        if (fg != null) t.Dyn(TextBlock.ForegroundProperty, fg);
        return t;
    }

    public static TextBlock PageTitle(string text) => Styled(text, "T.PageTitle");
    public static TextBlock H1(string text) => Styled(text, "T.H1");
    public static TextBlock H2(string text) => Styled(text, "T.H2");
    public static TextBlock H3(string text) => Styled(text, "T.H3");
    public static TextBlock Muted(string text) => Styled(text, "T.Muted");
    public static TextBlock Small(string text) => Styled(text, "T.Small");
    public static TextBlock Mono(string text) => Styled(text, "T.Mono");
    public static TextBlock GroupTitle(string text) => Styled(text, "T.GroupTitle");
    public static TextBlock Mega(string text) => Styled(text, "T.Mega");

    private static TextBlock Styled(string text, string key)
    {
        var t = new TextBlock { Text = text };
        t.Theme = Theme(key);
        return t;
    }

    /// <summary>图标字体字符（Linux 无内置图标字体，码位缺字体时回退为普通文本）。</summary>
    public static TextBlock Glyph(string glyph, double size = 14, string? fg = null)
    {
        var t = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily(FontIcon),
            FontSize = size,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (fg != null) t.Dyn(TextBlock.ForegroundProperty, fg);
        return t;
    }

    // ===== 布局 =====

    public static StackPanel V(double spacing, params Control?[] kids) =>
        Fill(new StackPanel { Spacing = spacing }, kids);

    public static StackPanel H(double spacing, params Control?[] kids) =>
        Fill(new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing }, kids);

    private static StackPanel Fill(StackPanel p, Control?[] kids)
    {
        foreach (var k in kids)
            if (k != null) p.Children.Add(k);
        return p;
    }

    /// <summary>按 "Auto,*,2*,140" 规格建 Grid。</summary>
    public static Grid G(string? rows = null, string? cols = null)
    {
        var g = new Grid();
        foreach (var len in ParseGridSpec(rows))
            g.RowDefinitions.Add(new RowDefinition { Height = len });
        foreach (var len in ParseGridSpec(cols))
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = len });
        return g;
    }

    public static Grid Add(this Grid g, Control child, int row = 0, int col = 0)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, col);
        g.Children.Add(child);
        return g;
    }

    private static IEnumerable<GridLength> ParseGridSpec(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) yield break;
        foreach (var raw in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var s = raw.Trim();
            if (s == "Auto") yield return GridLength.Auto;
            else if (s == "*") yield return new GridLength(1, GridUnitType.Star);
            else if (s.EndsWith('*') && double.TryParse(s[..^1], out var star))
                yield return new GridLength(star, GridUnitType.Star);
            else if (double.TryParse(s, out var px)) yield return new GridLength(px);
            else yield return GridLength.Auto;
        }
    }

    /// <summary>0×0 占位：放进横/竖排里配合 Alignment=Stretch 当弹簧。</summary>
    public static Control Spring() => new Border { Width = 0, Height = 0 };

    public static Border Sep(bool vertical = false)
    {
        var b = new Border();
        b.Dyn(Border.BackgroundProperty, "B.LineSoft");
        if (vertical) { b.Width = 1; b.VerticalAlignment = VerticalAlignment.Stretch; }
        else { b.Height = 1; b.HorizontalAlignment = HorizontalAlignment.Stretch; }
        return b;
    }

    // ===== 容器 =====

    public static Border Card(Control? content = null, double padding = 12, string style = "Card")
    {
        var b = new Border { Theme = Theme(style), Padding = new Thickness(padding) };
        if (content != null) b.Child = content;
        return b;
    }

    public static Border Pane(Control? content, string bg = "B.Paper2", double radius = 8, double padding = 10)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(radius),
            Padding = new Thickness(padding)
        };
        b.Dyn(Border.BackgroundProperty, bg);
        if (content != null) b.Child = content;
        return b;
    }

    public static Border RowCard(Control content, EventHandler<RoutedEventArgs>? click = null, double padding = 10)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(padding),
            BorderThickness = new Thickness(1),
            Child = content
        };
        b.Dyn(Border.BackgroundProperty, "B.Paper");
        b.Dyn(Border.BorderBrushProperty, "B.Line");
        if (click != null)
        {
            b.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
            b.PointerReleased += (_, e) => { click(b, new RoutedEventArgs()); e.Handled = true; };
        }
        return b;
    }

    // ===== 控件 =====

    private static string StyleOf(BtnKind k) => k switch
    {
        BtnKind.Primary => "Btn.Primary",
        BtnKind.Soft => "Btn.Soft",
        BtnKind.Ghost => "Btn.Ghost",
        BtnKind.Danger => "Btn.Danger",
        BtnKind.Chip => "Btn.Chip",
        BtnKind.Icon => "Btn.Icon",
        _ => "Btn.Normal",
    };

    public static Button Btn(string text, BtnKind kind = BtnKind.Normal, EventHandler<RoutedEventArgs>? click = null)
    {
        var b = new Button { Theme = Theme(StyleOf(kind)) };
        if (!string.IsNullOrEmpty(text))
            Avalonia.Automation.AutomationProperties.SetName(b, text);
        if (click != null) b.Click += click;
        b.Content = Txt(text);
        return b;
    }

    public static Button IconBtn(string glyph, string? tip = null, EventHandler<RoutedEventArgs>? click = null, double size = 14)
    {
        var b = new Button
        {
            Theme = Theme("Btn.Icon"),
            Content = Glyph(glyph, size)
        };
        if (tip != null)
        {
            ToolTip.SetTip(b, tip);
            Avalonia.Automation.AutomationProperties.SetName(b, tip);
        }
        if (click != null) b.Click += click;
        return b;
    }

    public static TextBox Input(string placeholder = "", string text = "", double width = double.NaN)
    {
        var tb = new TextBox { Tag = placeholder, Text = text };
        if (!double.IsNaN(width)) tb.Width = width;
        return tb;
    }

    public static TextBox Multi(string placeholder = "", string text = "", double height = 90)
    {
        var tb = new TextBox
        {
            Theme = Theme("Input.Multi"),
            Tag = placeholder,
            Text = text,
            Height = height
        };
        return tb;
    }

    public static TextBox LogBox() => new TextBox { Theme = Theme("Input.Log") };

    /// <summary>密码框：Avalonia 11 无 PasswordBox 控件，用 TextBox.PasswordChar 等价实现。</summary>
    public static TextBox Pw(double width = double.NaN)
    {
        var p = new TextBox { PasswordChar = '●' };
        if (!double.IsNaN(width)) p.Width = width;
        return p;
    }

    public static ComboBox Combo(IEnumerable<string>? items = null, string? selected = null, double width = double.NaN, string placeholder = "")
    {
        var cb = new ComboBox { Tag = placeholder };
        if (items != null)
            foreach (var it in items) cb.Items.Add(it);
        if (selected != null && cb.Items.Contains(selected))
            cb.SelectedItem = selected;
        else if (cb.Items.Count > 0)
            cb.SelectedIndex = 0;
        if (!double.IsNaN(width)) cb.Width = width;
        return cb;
    }

    public static CheckBox Check(string label, bool value = false, EventHandler<RoutedEventArgs>? changed = null)
    {
        var c = new CheckBox { Content = label, IsChecked = value };
        if (changed != null) c.IsCheckedChanged += changed;
        return c;
    }

    public static CheckBox Switch(bool value = false, EventHandler<RoutedEventArgs>? changed = null)
    {
        var c = new CheckBox
        {
            Theme = Theme("Switch"),
            IsChecked = value,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (changed != null) c.IsCheckedChanged += changed;
        return c;
    }

    public static Slider Sld(double min, double max, double value, double tick = 1)
        => new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            TickFrequency = tick,
            IsSnapToTickEnabled = tick > 0
        };

    public static ProgressBar Prog(double value = 0) => new ProgressBar { Value = value, Maximum = 100 };

    /// <summary>小标签：圆角 8 底色块 + 11px 小字（颜色可换键）。</summary>
    public static Border Tag(string text, string fg = "B.InkMuted", string bg = "B.LineSoft")
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 1.5, 6, 2.5),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Txt(text, 11, fg: fg)
        };
        b.Dyn(Border.BackgroundProperty, bg);
        return b;
    }

    /// <summary>数字角标：主题色药丸（CR.Pill），白字。</summary>
    public static Border Badge(string text)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        t.Dyn(TextBlock.ForegroundProperty, "B.OnAccentStrong");
        var b = new Border
        {
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(6, 0, 6, 1),
            MinWidth = 18,
            Height = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Child = t
        };
        b.Dyn(Border.BackgroundProperty, "B.AccentStrong");
        return b;
    }

    /// <summary>左标签 + 右控件的表单行。</summary>
    public static Grid Field(string label, Control control, string? hint = null, double labelWidth = 140)
    {
        var left = V(2, Txt(label), hint == null ? null : Small(hint));
        left.VerticalAlignment = VerticalAlignment.Center;
        left.Margin = new Thickness(0, 0, 10, 0);
        control.VerticalAlignment = VerticalAlignment.Center;
        var g = G(null, $"{labelWidth},*");
        g.Add(left, 0, 0);
        g.Add(control, 0, 1);
        g.Margin = new Thickness(0, 4, 0, 4);
        return g;
    }

    /// <summary>分组标题：H2 标题 + 可选副标题 + 可选右侧内容。</summary>
    public static Grid Section(string title, string? sub = null, Control? right = null)
    {
        var subBlock = sub == null ? null : Muted(sub);
        if (subBlock != null)
        {
            subBlock.TextWrapping = TextWrapping.NoWrap;
            subBlock.TextTrimming = TextTrimming.CharacterEllipsis;
        }
        var g = G(null, "*,Auto");
        g.Add(V(2, H2(title), subBlock), 0, 0);
        if (right != null)
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            g.Add(right, 0, 1);
        }
        return g;
    }

    /// <summary>空态：图标 + 标题 + 可选提示。</summary>
    public static StackPanel Empty(string glyph, string title, string? hint = null)
    {
        var hintBlock = hint == null ? null : new TextBlock
        {
            Text = hint,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        }.Dyn(TextBlock.ForegroundProperty, "B.InkFaint");
        var p = V(8, Glyph(glyph, 24, "B.InkFaint"),
            new TextBlock
            {
                Text = title,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center
            }.Dyn(TextBlock.ForegroundProperty, "B.InkMuted"),
            hintBlock);
        p.HorizontalAlignment = HorizontalAlignment.Center;
        p.VerticalAlignment = VerticalAlignment.Center;
        p.Margin = new Thickness(0, 24, 0, 24);
        foreach (var child in p.Children)
            child.HorizontalAlignment = HorizontalAlignment.Center;
        return p;
    }
}
