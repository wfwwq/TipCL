using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TipCL.Themes;

namespace TipCL.Services;

/// <summary>
/// C1c:Windows 版 <c>Services\Ui.cs</c>(WPF)到 Avalonia 11 的移植 —— 界面构建门面。
///
/// 移植原则(与 C1a / C1b 一致):
/// - **公开方法名、参数顺序、默认值、返回类型尽量照抄**,C2..C8 的页面照抄调用点即可;
///   只有"框架类型本身"与"WPF 没有等价机制的那几处"才改(逐条列在 PROGRESS.md 的 C1c 差异表里)。
/// - WPF 的 <c>Style="{StaticResource X}"</c> → <c>StyleKeys.Apply(control, "X")</c>(加类名,C1b 的
///   样式表用类选择器接住);WPF 的 <c>SetResourceReference</c> → Avalonia 的
///   <c>DynamicResourceExtension</c>(同样是动态资源,主题变体切换会重取);
///   <c>Visibility</c> → <c>IsVisible</c>;查找不到的键一律**不抛异常**(与 D-68 同一套兜底)。
/// - 类型替换:<c>UIElement</c>/<c>FrameworkElement</c> → <c>Control</c>,
///   <c>RoutedEventHandler</c> → <c>EventHandler&lt;RoutedEventArgs&gt;</c>,
///   <c>MouseButtonEventHandler</c> → <c>EventHandler&lt;PointerReleasedEventArgs&gt;</c>,
///   <c>PasswordBox</c> → <c>TextBox</c> + <c>PasswordChar</c>(Avalonia 11 没有 PasswordBox,见 D-75)。
/// - <see cref="S"/> 的返回值由 WPF 的 <c>Style</c> 改成**类名字符串**:Avalonia 没有"按键取 Style
///   挂到控件上"这套机制,硬留一个 <c>Style</c> 返回值只会是个什么也不做的空壳(那属于"把功能桩掉"),
///   所以这里如实返回 C1b 的类名(Avalonia 里样式就是靠类名命中的)。
/// </summary>
public static class Ui
{
    /// <summary>ActualHeight × pct(可带 floor 下限),浮层/滚动区 MaxHeight 绑窗口百分比的公共转换器。</summary>
    public sealed class WindowPctConverter : IValueConverter
    {
        private readonly double _pct;

        public WindowPctConverter(double pct)
        {
            _pct = pct;
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not double d || d <= 0.0)
            {
                // WPF 的 Binding.DoNothing 在 Avalonia 是 BindingOperations.DoNothing。
                return BindingOperations.DoNothing;
            }
            double floor = (parameter is double f) ? f : ((parameter is string s && double.TryParse(s, out double parsed)) ? parsed : 0.0);
            double v = d * _pct;
            return (v < floor) ? floor : v;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
    }

    /// <summary>
    /// 内置图标字体(avares 的 C1a 字体族)在前,两个 Windows 系统图标字体兜底 —— 后两者在本机
    /// (Windows)以外的系统上不存在,Avalonia 会顺着列表往下找,找不到才回落默认字体。
    /// 与 <c>Controls.axaml</c> 的 <c>F.Icon</c> 令牌是同一个字符串。
    /// </summary>
    internal const string GlyphFontUri = Palette.IconFontUri + ", Segoe Fluent Icons, Segoe MDL2 Assets";

    internal static readonly FontFamily GlyphFont = new FontFamily(GlyphFontUri);

    /// <summary>
    /// WPF 键 → C1b 的 Avalonia 类名(<c>StyleKeys.Map</c>)。认不出的键返回空串,**不抛异常**。
    /// 这是 WPF <c>S(key)</c> 在 mac 版上的等价物:样式不再"挂到控件上",而是"加一个类名"。
    /// </summary>
    public static string S(string key)
    {
        return StyleKeys.Map(key);
    }

    /// <summary>按色板键取刷子(WPF 的 <c>Application.Current.FindResource(key) as Brush</c>);取不到给中性灰兜底。</summary>
    public static IBrush Res(string key)
    {
        return Palette.Brush(key);
    }

    public static TextBlock Txt(string text = "", double size = 13.0, bool bold = false, string? fg = null)
    {
        TextBlock textBlock = new TextBlock
        {
            Text = text,
            FontSize = size,
            TextWrapping = TextWrapping.NoWrap
        };
        if (bold)
        {
            textBlock.FontWeight = FontWeight.SemiBold;
        }
        if (fg != null)
        {
            ApplyForeground(textBlock, fg);
        }
        return textBlock;
    }

    public static TextBlock H1(string text)
    {
        return Styled(text, "T.H1");
    }

    public static TextBlock H2(string text)
    {
        return Styled(text, "T.H2");
    }

    public static TextBlock H3(string text)
    {
        return Styled(text, "T.H3");
    }

    public static TextBlock Muted(string text)
    {
        return Styled(text, "T.Muted");
    }

    public static TextBlock Small(string text)
    {
        return Styled(text, "T.Small");
    }

    public static TextBlock Mono(string text)
    {
        return Styled(text, "T.Mono");
    }

    private static TextBlock Styled(string text, string key)
    {
        // WPF 是 Style = S(key);Avalonia 是要靠类选择器命中,所以只加类名(C1b 的样式表负责字号/字重/前景)。
        TextBlock textBlock = new TextBlock
        {
            Text = text
        };
        StyleKeys.Apply(textBlock, key);
        return textBlock;
    }

    public static TextBlock Glyph(string glyph, double size = 14.0, string? fg = null)
    {
        TextBlock textBlock = new TextBlock
        {
            Text = glyph,
            FontFamily = GlyphFont,
            FontSize = size,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (fg != null)
        {
            ApplyForeground(textBlock, fg);
        }
        return textBlock;
    }

    public static SPanel V(double spacing, params Control?[] kids)
    {
        return Fill(new SPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = spacing
        }, kids);
    }

    public static SPanel H(double spacing, params Control?[] kids)
    {
        return Fill(new SPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = spacing
        }, kids);
    }

    private static SPanel Fill(SPanel p, Control?[] kids)
    {
        foreach (Control? kid in kids)
        {
            if (kid != null)
            {
                p.Children.Add(kid);
            }
        }
        return p;
    }

    public static Grid G(string? rows = null, string? cols = null)
    {
        Grid grid = new Grid();
        foreach (GridLength length in Parse(rows))
        {
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = length
            });
        }
        foreach (GridLength length2 in Parse(cols))
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = length2
            });
        }
        return grid;
    }

    private static IEnumerable<GridLength> Parse(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            yield break;
        }
        string[] array = spec.Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < array.Length; i++)
        {
            string text = array[i].Trim();
            double result;
            double result2;
            if (text == "Auto")
            {
                yield return GridLength.Auto;
            }
            else if (text == "*")
            {
                yield return new GridLength(1.0, GridUnitType.Star);
            }
            else if (text.EndsWith('*') && double.TryParse(text.Substring(0, text.Length - 1), out result))
            {
                yield return new GridLength(result, GridUnitType.Star);
            }
            else if (double.TryParse(text, out result2))
            {
                yield return new GridLength(result2);
            }
            else
            {
                yield return GridLength.Auto;
            }
        }
    }

    public static Border Card(Control? content = null, double padding = 12.0, string style = "Card")
    {
        Border border = new Border
        {
            Padding = new Thickness(padding)
        };
        StyleKeys.Apply(border, style);
        if (content != null)
        {
            border.Child = content;
        }
        return border;
    }

    public static Border Pane(Control? content, string bg = "B.Paper2", double radius = 8.0, double padding = 10.0)
    {
        Border border = new Border
        {
            CornerRadius = new CornerRadius(radius),
            Padding = new Thickness(padding)
        };
        border.Dyn(Border.BackgroundProperty, bg);
        if (content != null)
        {
            border.Child = content;
        }
        return border;
    }

    /// <summary>当前宿主窗口高 × pct(不低于 floor)——给无法用绑定的字面量 MaxHeight 处用。</summary>
    public static double WindowPctOf(double pct, double floor)
    {
        // WPF 读的是 AppServices.Window(mac 版外壳属 C2)。这里直接问桌面生命周期的主窗口,
        // 没有窗口(离屏 / 测试)时按 0 处理 —— 结果就是 floor。
        // 高度用 Bounds.Height:Avalonia 没有 WPF 的 ActualWidth / ActualHeight(只有 Visual.Bounds)。
        Window? host = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        return WindowPctOf(pct, floor, host?.Bounds.Height ?? 0.0);
    }

    /// <summary>可注入宿主高度的重载(纯算术,便于单测;公开入口从主窗口取高度)。</summary>
    internal static double WindowPctOf(double pct, double floor, double hostHeight)
    {
        double v = (hostHeight > 0.0) ? hostHeight * pct : floor;
        return (v < floor) ? floor : v;
    }

    public static SmoothScroll Scroll(Control content, double padding = 0.0)
    {
        return new SmoothScroll
        {
            Content = content,
            Padding = new Thickness(padding)
        };
    }

    public static Button Btn(string text, BtnKind kind = BtnKind.Normal, EventHandler<RoutedEventArgs>? click = null, string? glyph = null)
    {
        Button button = new Button();
        StyleKeys.Apply(button, StyleOf(kind));
        button.Content = ((glyph == null) ? ((object)text) : H(6.0, Glyph(glyph, (kind == BtnKind.Primary) ? 13.0 : 12.5), string.IsNullOrEmpty(text) ? null : Txt(text)));
        if (!string.IsNullOrEmpty(text))
        {
            // 图文组合按钮的自动化名取文字(否则屏幕阅读器会读到图标字体的私有码位)
            AutomationProperties.SetName(button, text);
        }
        if (click != null)
        {
            button.Click += click;
        }
        return button;
    }

    public static Button IconBtn(string glyph, string? tip = null, EventHandler<RoutedEventArgs>? click = null, double size = 14.0)
    {
        Button button = new Button
        {
            Content = Glyph(glyph, size)
        };
        StyleKeys.Apply(button, "Btn.Icon");
        if (tip != null)
        {
            // WPF 的 Button.ToolTip 属性在 Avalonia 是 ToolTip.SetTip 附加属性。
            ToolTip.SetTip(button, tip);
            AutomationProperties.SetName(button, tip);
        }
        if (click != null)
        {
            button.Click += click;
        }
        return button;
    }

    private static string StyleOf(BtnKind k)
    {
        return k switch
        {
            BtnKind.Primary => "Btn.Primary",
            BtnKind.Soft => "Btn.Soft",
            BtnKind.Ghost => "Btn.Ghost",
            BtnKind.Danger => "Btn.Danger",
            BtnKind.Chip => "Btn.Chip",
            BtnKind.Icon => "Btn.Icon",
            _ => "Btn.Normal",
        };
    }

    /// <summary>WPF 的 <c>SetResourceReference</c>:走到动态资源,主题变体切换时颜色会重取。</summary>
    private static void ApplyForeground(Control control, string key)
    {
        control[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(key);
    }
}
