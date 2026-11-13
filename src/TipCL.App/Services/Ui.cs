using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
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

    /// <summary>
    /// 虚拟化列表。WPF 版用 <c>FrameworkElementFactory</c> + <c>IValueConverter</c> 造 DataTemplate,
    /// Avalonia 的等价物是 <c>FuncDataTemplate&lt;T&gt;</c>(工厂直接返回控件,不再绕转换器);
    /// 容器样式(WPF 的 <c>ItemContainerStyle</c>)在 Avalonia 是 <c>ItemContainerTheme</c>,
    /// 里面用一份最小 ControlTheme 把模板换成裸 <c>ContentPresenter</c> + 行间距 + 不可聚焦。
    /// <c>ScrollViewer.SetCanContentScroll</c> 在 Avalonia 没有对应设置(列表本来就虚拟化 + 像素滚动)。
    /// </summary>
    public static ListBox VirtualList<T>(Func<T, Control> row, double rowSpacing = 8.0)
    {
        ListBox list = new ListBox
        {
            Focusable = false,
            ItemTemplate = new FuncDataTemplate<T>((item, _) => row(item)),
            ItemContainerTheme = BareContainer(rowSpacing)
        };
        StyleKeys.Apply(list, "PlainList");
        ScrollViewer.SetHorizontalScrollBarVisibility(list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        return list;
    }

    private static ControlTheme BareContainer(double spacing)
    {
        return new ControlTheme(typeof(ListBoxItem))
        {
            TargetType = typeof(ListBoxItem),
            Setters =
            {
                // Template 是 TemplatedControl 上的属性,ListBoxItem 继承自 ContentControl。
                new Setter(ContentControl.TemplateProperty, new FuncControlTemplate<ListBoxItem>((_, _) => new ContentPresenter())),
                new Setter(Layoutable.MarginProperty, new Thickness(0.0, 0.0, 0.0, spacing)),
                new Setter(InputElement.FocusableProperty, false),
            },
        };
    }

    public static Border Sep(bool vertical = false)
    {
        Border border = new Border();
        border.Dyn(Border.BackgroundProperty, "B.LineSoft");
        if (vertical)
        {
            border.Width = 1.0;
            border.VerticalAlignment = VerticalAlignment.Stretch;
        }
        else
        {
            border.Height = 1.0;
            border.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        return border;
    }

    public static Control Spring()
    {
        return new Border
        {
            Width = 0.0,
            Height = 0.0
        };
    }

    public static TextBox Input(string placeholder = "", string text = "", double width = double.NaN)
    {
        TextBox textBox = new TextBox
        {
            Text = text,
            Tag = placeholder
        };
        StyleKeys.Apply(textBox, "Input");
        if (!double.IsNaN(width))
        {
            textBox.Width = width;
        }
        return textBox;
    }

    public static TextBox Multi(string placeholder = "", string text = "", double height = 90.0)
    {
        TextBox textBox = new TextBox
        {
            Tag = placeholder,
            Text = text,
            Height = height
        };
        StyleKeys.Apply(textBox, "Input.Multi");
        return textBox;
    }

    public static TextBox LogBox()
    {
        TextBox textBox = new TextBox();
        StyleKeys.Apply(textBox, "Input.Log");
        return textBox;
    }

    /// <summary>
    /// 密码框。**返回类型由 WPF 的 <c>PasswordBox</c> 改成 <c>TextBox</c>**:Avalonia 11 没有
    /// <c>PasswordBox</c> 控件(见 D-75),C1b 的 <c>Pw</c> 样式也是一份 <c>TextBox.pw</c> 类。
    /// 掩码字符取 WPF <c>PasswordBox</c> 的默认值 <c>●</c>(U+25CF),调用点若用别的字符直接改
    /// <c>PasswordChar</c> 即可。
    /// </summary>
    public static TextBox Pw(double width = double.NaN)
    {
        TextBox textBox = new TextBox
        {
            PasswordChar = '\u25cf'
        };
        StyleKeys.Apply(textBox, "Pw");
        if (!double.IsNaN(width))
        {
            textBox.Width = width;
        }
        return textBox;
    }

    public static ComboBox Combo(IEnumerable<string>? items = null, string? selected = null, double width = double.NaN, string placeholder = "", bool editable = false)
    {
        ComboBox comboBox = new ComboBox
        {
            Tag = placeholder,
            IsEditable = editable
        };
        StyleKeys.Apply(comboBox, "Combo");
        if (items != null)
        {
            foreach (string item in items)
            {
                comboBox.Items.Add(item);
            }
        }
        if (selected != null && comboBox.Items.Contains(selected))
        {
            comboBox.SelectedItem = selected;
        }
        else if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
        if (!double.IsNaN(width))
        {
            comboBox.Width = width;
        }
        return comboBox;
    }

    public static CheckBox Check(string label, bool value = false, EventHandler<RoutedEventArgs>? changed = null)
    {
        CheckBox checkBox = new CheckBox
        {
            Content = label,
            IsChecked = value
        };
        StyleKeys.Apply(checkBox, "Chk");
        if (changed != null)
        {
            // WPF 的 Checked / Unchecked 两个事件在 Avalonia 11.3 已标记过时(CS0618);
            // IsCheckedChanged 的签名与语义在两态控件上完全等价,所以接它。
            checkBox.IsCheckedChanged += changed;
        }
        return checkBox;
    }

    public static CheckBox Switch(bool value = false, EventHandler<RoutedEventArgs>? changed = null)
    {
        CheckBox checkBox = new CheckBox
        {
            IsChecked = value,
            VerticalAlignment = VerticalAlignment.Center
        };
        StyleKeys.Apply(checkBox, "Switch");
        if (changed != null)
        {
            // 同 Check():用不过时的 IsCheckedChanged。
            checkBox.IsCheckedChanged += changed;
        }
        return checkBox;
    }

    public static Slider Sld(double min, double max, double value, double tick = 1.0)
    {
        Slider slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Clamp.Of(value, min, max),
            TickFrequency = tick,
            IsSnapToTickEnabled = (tick > 0.0)
        };
        StyleKeys.Apply(slider, "Sld");
        return slider;
    }

    public static ProgressBar Prog(double value = 0.0)
    {
        ProgressBar progressBar = new ProgressBar
        {
            Value = value,
            Maximum = 100.0
        };
        StyleKeys.Apply(progressBar, "Prog");
        return progressBar;
    }

    public static Border Tag(string text, string fg = "B.InkMuted", string bg = "B.LineSoft")
    {
        Border border = new Border
        {
            CornerRadius = new CornerRadius(8.0),
            Padding = new Thickness(6.0, 1.5, 6.0, 2.5),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Txt(text, 11.0, bold: false, fg)
        };
        border.Dyn(Border.BackgroundProperty, bg);
        return border;
    }

    public static Border Badge(string text)
    {
        TextBlock textBlock = new TextBlock
        {
            Text = text,
            FontSize = 11.0,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        textBlock.Dyn(TextBlock.ForegroundProperty, "B.OnAccentStrong");
        Border border = new Border
        {
            CornerRadius = new CornerRadius(999.0),
            Padding = new Thickness(6.0, 0.0, 6.0, 1.0),
            MinWidth = 18.0,
            Height = 18.0,
            VerticalAlignment = VerticalAlignment.Center,
            Child = textBlock
        };
        border.Dyn(Border.BackgroundProperty, "B.AccentStrong");
        return border;
    }

    public static Grid Field(string label, Control control, string? hint = null, double labelWidth = 140.0)    {
        Grid grid = G(null, $"{labelWidth},*");
        SPanel panel = V(2.0, Txt(label), (hint == null) ? null : Small(hint).Wrap());
        panel.VerticalAlignment = VerticalAlignment.Center;
        panel.Margin = new Thickness(0.0, 0.0, 10.0, 0.0);
        control.VerticalAlignment = VerticalAlignment.Center;
        grid.Add(panel, 0);
        grid.Add(control, 0, 1);
        grid.Margin = new Thickness(0.0, 4.0, 0.0, 4.0);
        return grid;
    }

    public static Grid Section(string title, string? sub = null, Control? right = null)
    {
        Grid grid = G(null, "*,Auto");
        TextBlock? subBlock = (sub == null) ? null : Muted(sub);
        if (subBlock != null)
        {
            // WPF 的 TextWrapping.NoWrap 与 CharacterEllipsis 在 Avalonia 里同名。
            subBlock.TextWrapping = TextWrapping.NoWrap;
            subBlock.TextTrimming = TextTrimming.CharacterEllipsis;
        }
        SPanel el = V(2.0, H2(title), subBlock);
        grid.Add(el, 0);
        if (right != null)
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            grid.Add(right, 0, 1);
            // C5c:窄页面(被侧栏压到 ~668)下不给左列留位置 —— 摆不开就把右簇挪到第二行。
            // 宽页面永不触发,所以宽页面的像素一个字节不变。
            SectionNarrow.Watch(grid, el, right);
        }
        return grid;
    }

    public static SPanel Empty(string glyph, string title, string? hint = null)
    {
        TextBlock titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 13.0,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        titleBlock.Dyn(TextBlock.ForegroundProperty, "B.InkMuted");

        TextBlock? hintBlock = null;
        if (hint != null)
        {
            hintBlock = new TextBlock
            {
                Text = hint,
                FontSize = 12.0,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420.0,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            hintBlock.Dyn(TextBlock.ForegroundProperty, "B.InkFaint");
        }

        SPanel panel = V(8.0, Glyph(glyph, 24.0, "B.InkFaint"), titleBlock, hintBlock);
        panel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.VerticalAlignment = VerticalAlignment.Center;
        panel.Margin = new Thickness(0.0, 24.0, 0.0, 24.0);
        foreach (Control child in panel.Children)
        {
            child.HorizontalAlignment = HorizontalAlignment.Center;
        }
        return panel;
    }

    /// <summary>
    /// 骨架占位:灰底 + 一道 90px 的白光横扫(WPF 版用 <c>Motion.Loop</c> 起无限动画)。
    /// **mac 侧的 Motion 门面属 C2**(见 D-70),所以这里用 <c>Lucide.MotionEnabled</c> 这个已有的开关
    /// 顶上,并且与 Lucide 共用同一条纪律:**只在真正跑起来的应用生命周期下起动画** ——
    /// 离屏(--smoke)与测试进程里没有东西推进动画时钟,无限动画会把收尾挂住(见 D-69)。
    /// 被跳过动画时得到的就是一块静态灰条(等价 WPF 关掉动画时的样子)。
    /// </summary>
    /// C2b:上面那套"用 Lucide.MotionEnabled 顶上"的说法**已作废** —— Motion 门面(C2a)落地后,
    /// 这里改成与 Windows 版 <c>Ui.Skeleton</c> 一样的 <c>Motion.Loop(起横扫, 停横扫)</c>:
    /// Motion.Loop 自己按「用户开关 && 有人在推进动画时钟 && 可见 && 在树上」判断走哪个分支,
    /// 关掉时立刻落终态(横扫回到起点,得到静态灰条)。
    /// </summary>
    public static Border Skeleton(double height = 16.0, double width = double.NaN)
    {
        Border shell = new Border
        {
            Height = height,
            CornerRadius = new CornerRadius(6.0),
            ClipToBounds = true
        };
        if (!double.IsNaN(width))
        {
            shell.Width = width;
        }
        shell.Dyn(Border.BackgroundProperty, "B.LineSoft");

        Border glide = new Border
        {
            Width = 90.0,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.0, 0.0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1.0, 0.0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.0),
                    new GradientStop(Color.FromArgb(120, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.5),
                    new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 1.0),
                },
            },
        };

        TranslateTransform slide = new TranslateTransform(-90.0, 0.0);
        glide.RenderTransform = slide;
        shell.Child = glide;
        StartSkeletonGlide(shell, slide);
        return shell;
    }

    private static void StartSkeletonGlide(Border owner, TranslateTransform slide)
    {
        // C2b:走 Motion 门面(与 Windows 版 Ui.Skeleton 的 Motion.Loop 同一套状态机)。
        // 动画关掉时(u. a. 离屏 / 测试进程里没有东西推进时钟)立刻走 stop 分支:
        // 横扫回到起点 -90,看到的就是一块静态灰条。
        Motion.Loop(owner, delegate
        {
            StartGlide(owner, slide);
        }, delegate
        {
            slide.X = -90.0;
        });
    }

    private static void StartGlide(Border owner, TranslateTransform slide)
    {
        try
        {
            var animation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(1150.0),
                IterationCount = IterationCount.Infinite,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(TranslateTransform.XProperty, -90.0) } },
                    new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(TranslateTransform.XProperty, GlideDistance(owner)) } },
                },
            };

            _ = animation.RunAsync(slide);
        }
        catch (Exception)
        {
            // 动画是装饰性的,不把界面带崩(与 Lucide.Run 同一条纪律)。
        }
    }

    /// <summary>横扫距离:与 WPF 版的 <c>Math.Max(520, b.ActualWidth + 90)</c> 一致(未布局时按 520)。</summary>
    private static double GlideDistance(Border owner)
    {
        double actual = double.IsNaN(owner.Width) ? 0.0 : owner.Width;
        return Math.Max(520.0, actual + 90.0);
    }

    /// <summary>
    /// 可点击的行卡片。WPF 用的是 <c>MouseButtonEventHandler</c>(<c>MouseLeftButtonUp</c>)与
    /// <c>Cursors.Hand</c>;Avalonia 的等价物是 <c>PointerReleased</c> + <c>InitialPressMouseButton</c>
    /// 判左键、<c>new Cursor(StandardCursorType.Hand)</c>。
    /// </summary>
    public static Border RowCard(Control content, EventHandler<PointerReleasedEventArgs>? click = null, double padding = 10.0)
    {
        Border border = new Border
        {
            CornerRadius = new CornerRadius(8.0),
            Padding = new Thickness(padding),
            BorderThickness = new Thickness(1.0),
            Child = content,
            Cursor = ((click != null) ? new Cursor(StandardCursorType.Hand) : null)
        };
        border.Dyn(Border.BackgroundProperty, "B.Paper");
        border.Dyn(Border.BorderBrushProperty, "B.Line");
        if (click != null)
        {
            border.PointerReleased += delegate(object? sender, PointerReleasedEventArgs e)
            {
                // 与 WPF 的 MouseLeftButtonUp 等价:只有左键按下后的抬起才回调。
                if (e.InitialPressMouseButton == MouseButton.Left)
                {
                    click(sender, e);
                }
            };
        }
        return border;
    }

    /// <summary>
    /// 用系统默认程序打开链接。mac 上走 <c>TipCL.Core</c> 的 <c>Crash.ShellOpen</c>
    /// (非 Windows 分支就是 <c>open &lt;url&gt;</c>,B1 已落地),与 GOAL §3「插件网页界面用系统浏览器打开」一致。
    /// WPF 版失败时弹 <c>AppServices.Toast</c>;mac 版的 Toast 宿主属 C2,门面这一层不依赖外壳,
    /// 所以退化成调试输出(不抛异常、不改状态)。
    /// </summary>
    public static void OpenUrl(string url)
    {
        try
        {
            TipCL.Core.Launcher.Crash.ShellOpen(url);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Ui.OpenUrl 失败: " + ex.Message);
        }
    }
}

/// <summary>
/// C5c:让 <see cref="Ui.Section"/> 在**窄页面**下不再是"标题被右列挤成 0 宽"。
///
/// 背景(实测):mac 的页面区被 196px 侧栏压到约 <b>668px</b>,而 Windows 版没有侧栏 ⇒ 这个问题只在移植后暴露。
/// <c>Section</c> 原来是 <c>G(null, "*,Auto")</c>:左列 <c>*</c> 没有最小宽度、右列那一簇(计数 + 搜索框 + 4 个按钮 ≈ 653px)
/// 也不会收缩 ⇒ 窄页面时左列被算成 <b>0</b>(探针实测:标题宽 0、副说明宽 0,见 C5c 的探针用例)。
///
/// 判定用两边的 <c>DesiredSize</c> 自身当阈值(不用猜魔数):**摆不开**(可用宽 &lt; 标题需要 + 右簇需要)才降级,
/// 摆得开就恢复 —— 所以**宽页面永远走原路,像素一个字节不变**。<see cref="Slack"/> 是回滞带宽:
/// 没有它,降级/恢复会在阈值上互相触发(改了列宽 → 触发再判 → 又改回去)。
/// </summary>
internal static class SectionNarrow
{
    /// <summary>回滞带宽(px):进入降级比退出降级苛刻一点,防止在阈值上抖动。</summary>
    private const double Slack = 6.0;

    /// <summary>盯住这个 Section 的宽度,必要时把右簇换到第二行。</summary>
    internal static void Watch(Grid grid, Control head, Control right)
    {
        // 行 0 = 标题那一块;行 1 = 备用(只在降级时被右簇占)。空行的 Auto 高度是 0,摆得开时不改变任何像素。
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        bool stacked = false;
        grid.SizeChanged += delegate
        {
            double need = head.DesiredSize.Width + right.DesiredSize.Width;
            double have = grid.Bounds.Width;
            if (!stacked && have < need - Slack)
            {
                // 降级:右簇换到第二行、跨两列、靠右(与原来"贴在标题右边"的观感一致)
                Grid.SetRow(right, 1);
                Grid.SetColumn(right, 0);
                Grid.SetColumnSpan(right, 2);
                right.HorizontalAlignment = HorizontalAlignment.Right;
                stacked = true;
            }
            else if (stacked && have > need + Slack)
            {
                // 恢复:回到"标题左、动作右"的原始两列排布
                Grid.SetRow(right, 0);
                Grid.SetColumn(right, 1);
                Grid.SetColumnSpan(right, 1);
                right.HorizontalAlignment = HorizontalAlignment.Stretch;
                stacked = false;
            }
        };
    }
}
