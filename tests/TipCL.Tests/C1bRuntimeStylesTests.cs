using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.Themes;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C1b:控件样式表在**真 Avalonia 平台**上的表现(headless + Skia,与 A1 的 --smoke 同一套配置)。
///
/// 这一层验的是静态文本层看不出来的东西:类名选择器有没有命中、ControlTheme 有没有被用上、
/// 框架要求的模板部件(PART_TextPresenter、PART_Popup...)有没有被找到、/template/ 取部件的
/// 深层选择器能不能命中外层模板里的元素。
///
/// 期望值来源:
/// - **任务书**:类名映射与控件族覆盖要求;
/// - **本机实测**(Avalonia 11.3.23 headless):模板部件的实际名字与深层选择器的实际行为 ——
///   C1b 开工时先用一次性探针把 Fluent 自带主题的视觉树打出来,再照着实测出来的部件名写模板
///   (探针文件已删,结论记在 PROGRESS.md 的 C1b 小节里);
/// - **Windows 源**:字号/字重/颜色等 Setter 取值。
///
/// 纪律:样式里有 Transitions(淡化)的地方不断言"动画中的值",只断言没有过渡的属性
/// (变换、可见性、宽度等),免得头两帧的中间值把用例抖红。
/// </summary>
public class C1bRuntimeStylesTests
{
    /// <summary>把控件挂进真窗口(样式与主题只在挂进可视树之后才生效),用完自动收摊。</summary>
    private sealed class Hosted : IDisposable
    {
        private readonly ThemeVariant? _previousVariant;

        private Hosted(Window window, ThemeVariant? previousVariant)
        {
            Window = window;
            _previousVariant = previousVariant;
        }

        internal Window Window { get; }

        internal static Hosted Show(Control content)
        {
            C1aFixtures.EnsureAvalonia();

            // 色板挂在 ThemeDictionaries[Light/Dark] 下,样式里的 {DynamicResource B.*} 要有明确变体
            // 才有得解,所以这里显式选浅色(测试进程里 RequestedThemeVariant 默认是 Default),
            // 并在 Dispose 里还原 —— 全局静态改动必须在 finally/Dispose 里还回去(D-69 的纪律)。
            ThemeVariant? previous = Application.Current!.RequestedThemeVariant;
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;

            var window = new Window { Width = 600, Height = 400, Content = content };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            return new Hosted(window, previous);
        }

        public void Dispose()
        {
            try
            {
                Window.Close();
            }
            catch (Exception)
            {
            }

            Application.Current!.RequestedThemeVariant = _previousVariant;
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static T ByName<T>(Visual root, string name) where T : Control
    {
        return root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
    }

    private static Color ColourOf(IBrush? brush)
    {
        Assert.NotNull(brush);
        return Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    }

    /// <summary>把伪类直接置位,用来验 :pointerover / :pressed / :focus-visible 这几条嵌套样式。</summary>
    private static IPseudoClasses Pseudo(StyledElement element)
    {
        return (IPseudoClasses)element.Classes;
    }

    /// <summary>样式表能被 Avalonia 真正加载出来(令牌、ControlTheme、选择器都在;语法错会在这一步抛)。</summary>
    [Fact]
    public void Controls_axaml_loads_through_avalonia_as_a_styles_object()
    {
        C1aFixtures.EnsureAvalonia();
        object? loaded = AvaloniaXamlLoader.Load(new Uri(TipCL.App.ControlsUri));
        Assert.IsAssignableFrom<Styles>(loaded);
    }

    /// <summary>应用启动路径(App.Initialize)已经把样式表挂在 Fluent 主题之后。</summary>
    [Fact]
    public void App_installs_the_controls_styles_after_the_fluent_theme()
    {
        C1aFixtures.EnsureAvalonia();
        var texts = Application.Current!.Styles.Select(s => s.GetType().FullName ?? s.GetType().Name).ToList();
        Assert.Contains("Avalonia.Themes.Fluent.FluentTheme", texts);
        Assert.Contains("Avalonia.Markup.Xaml.Styling.StyleInclude", texts);
    }

    /// <summary>颜色令牌解析出来的就是色板里的颜色(顺带证明 ThemeDictionaries 的引用链通了)。</summary>
    [Theory]
    [InlineData("B.Ink")]
    [InlineData("B.AccentStrong")]
    [InlineData("B.Card")]
    [InlineData("B.SegRail")]
    [InlineData("B.ScrollHover")]
    public void Palette_brushes_resolve_inside_a_hosted_control(string key)
    {
        var border = new Border { Classes = { "card" } };
        using Hosted host = Hosted.Show(border);

        object? value = null;
        bool found = host.Window.TryFindResource(key, ThemeVariant.Light, out value);
        Assert.True(found, "解析不到色板键 " + key);
        Assert.Equal(Palette.Color(key), ColourOf(value as IBrush));
    }

    /// <summary>字体令牌:GOAL 的 mac 字体列表要被 Avalonia 解析成"多个字体族"(不是一整串名字)。</summary>
    [Fact]
    public void Font_tokens_resolve_to_font_families_with_fallbacks()
    {
        var text = new TextBlock();
        using Hosted host = Hosted.Show(text);

        FontFamily ui = Assert.IsType<FontFamily>(host.Window.FindResource("F.Ui"));
        Assert.Equal("PingFang SC", ui.Name);
        Assert.Equal(new[] { "PingFang SC", "Hiragino Sans GB", "Microsoft YaHei UI", "sans-serif" }, ui.FamilyNames.ToArray());

        FontFamily mono = Assert.IsType<FontFamily>(host.Window.FindResource("F.Mono"));
        Assert.Equal(new[] { "Menlo", "Consolas", "monospace" }, mono.FamilyNames.ToArray());

        // F.Icon:C1a 的图标字体 + Windows 的两个兜底字体族折在同一行里(任务书要求)
        FontFamily icon = Assert.IsType<FontFamily>(host.Window.FindResource("F.Icon"));
        Assert.Equal(Palette.IconFontFamilyName, icon.Name);
        Assert.Contains("Segoe Fluent Icons", icon.FamilyNames.ToArray(), StringComparer.Ordinal);
        Assert.Contains("Segoe MDL2 Assets", icon.FamilyNames.ToArray(), StringComparer.Ordinal);
    }

    /// <summary>圆角/时长令牌在运行期是真类型,不是字符串。</summary>
    [Fact]
    public void Corner_radius_and_duration_tokens_resolve_to_real_values()
    {
        var border = new Border();
        using Hosted host = Hosted.Show(border);

        Assert.Equal(new CornerRadius(12), Assert.IsType<CornerRadius>(host.Window.FindResource("CR.Card")));
        Assert.Equal(new CornerRadius(8), Assert.IsType<CornerRadius>(host.Window.FindResource("CR.Ctl")));
        Assert.Equal(new CornerRadius(999), Assert.IsType<CornerRadius>(host.Window.FindResource("CR.Pill")));
        Assert.Equal(TimeSpan.FromMilliseconds(130), Assert.IsType<TimeSpan>(host.Window.FindResource("D.Fast")));
        Assert.Equal(TimeSpan.FromMilliseconds(220), Assert.IsType<TimeSpan>(host.Window.FindResource("D.Med")));
    }

    /// <summary>E.Out:真的是 ease-out(前半程已经走完一半以上)。</summary>
    [Fact]
    public void Ease_out_token_behaves_like_an_ease_out()
    {
        var border = new Border();
        using Hosted host = Hosted.Show(border);

        var easing = Assert.IsAssignableFrom<Avalonia.Animation.Easings.Easing>(host.Window.FindResource("E.Out"));
        Assert.True(easing.Ease(0.5) > 0.5, "E.Out 必须是 ease-out,实测 Ease(0.5)=" + easing.Ease(0.5));
        Assert.Equal(1.0, easing.Ease(1.0), 6);
    }

    /// <summary>隐式 TextBlock 样式:13px + F.Ui(与 Windows 版的隐式 TextBlock 样式一致)。</summary>
    [Fact]
    public void Implicit_textblock_style_applies()
    {
        var text = new TextBlock { Text = "TipCL" };
        using Hosted host = Hosted.Show(text);

        Assert.Equal(13.0, text.FontSize);
        Assert.Equal("PingFang SC", text.FontFamily?.Name);
    }

    /// <summary>卡片:类选择器把 Border 变成白底圆角 12 的卡片。</summary>
    [Fact]
    public void Card_class_restyles_a_border()
    {
        var border = new Border { Classes = { "card" } };
        using Hosted host = Hosted.Show(border);

        Assert.Equal(Palette.Color("B.Card"), ColourOf(border.Background));
        Assert.Equal(new CornerRadius(12), border.CornerRadius);
        Assert.Equal(new Thickness(12), border.Padding);
    }

    /// <summary>分段轨道:圆角 10、内边距 3。</summary>
    [Fact]
    public void Seg_rail_class_restyles_a_border()
    {
        var border = new Border { Classes = { "seg-rail" } };
        using Hosted host = Hosted.Show(border);

        Assert.Equal(new CornerRadius(10), border.CornerRadius);
        Assert.Equal(new Thickness(3), border.Padding);
        Assert.Equal(Palette.Color("B.SegRail"), ColourOf(border.Background));
    }

    /// <summary>文本样式:字号/字重逐条命中(期望值来自 Windows 源里的 Setter)。</summary>
    [Theory]
    [InlineData("t-h1", 20.0, FontWeight.SemiBold)]
    [InlineData("t-page-title", 21.0, FontWeight.ExtraBold)]
    [InlineData("t-h2", 17.0, FontWeight.SemiBold)]
    [InlineData("t-h3", 15.0, FontWeight.SemiBold)]
    [InlineData("t-group-title", 12.5, FontWeight.SemiBold)]
    [InlineData("t-muted", 12.0, FontWeight.Normal)]
    [InlineData("t-small", 11.0, FontWeight.Normal)]
    [InlineData("t-mono", 12.0, FontWeight.Normal)]
    public void Text_style_classes_match_the_windows_setters(string token, double fontSize, FontWeight weight)
    {
        var text = new TextBlock { Text = "x", Classes = { token } };
        using Hosted host = Hosted.Show(text);

        Assert.Equal(fontSize, text.FontSize);
        Assert.Equal(weight, text.FontWeight);
        Assert.NotNull(text.Foreground);
    }

    /// <summary>T.Mono 用等宽字体列表。</summary>
    [Fact]
    public void Mono_style_uses_the_monospace_list()
    {
        var text = new TextBlock { Text = "x", Classes = { "t-mono" } };
        using Hosted host = Hosted.Show(text);

        Assert.Equal("Menlo", text.FontFamily?.Name);
    }

    /// <summary>
    /// 按钮族的 12 个类各自都要把 Theme 换成我们的模板(等价 WPF 的 BasedOn),
    /// 并且模板真的实例化了(可视树里有模板根 Grid)。
    /// </summary>
    [Theory]
    [InlineData("btn-base")]
    [InlineData("btn-primary")]
    [InlineData("btn-soft")]
    [InlineData("btn-normal")]
    [InlineData("btn-ghost")]
    [InlineData("btn-danger")]
    [InlineData("btn-icon")]
    [InlineData("btn-chip")]
    [InlineData("btn-caption")]
    [InlineData("btn-caption-close")]
    [InlineData("btn-title-icon")]
    [InlineData("btn-title-chip")]
    public void Every_button_class_installs_our_control_theme(string token)
    {
        var button = new Button { Content = "x", Classes = { token } };
        using Hosted host = Hosted.Show(button);

        Assert.NotNull(button.Theme);
        Assert.Contains(button.GetVisualDescendants(), v => v is Grid);
    }

    /// <summary>按钮配色:变体类把 Background/Foreground 设成色板里的颜色。</summary>
    [Fact]
    public void Button_variant_classes_use_palette_colours()
    {
        var primary = new Button { Content = "x", Classes = { "btn-primary" } };
        using (Hosted.Show(primary))
        {
            Assert.Equal(Palette.Color("B.AccentStrong"), ColourOf(primary.Background));
            Assert.Equal(Palette.Color("B.OnAccentStrong"), ColourOf(primary.Foreground));
        }

        var danger = new Button { Content = "x", Classes = { "btn-danger" } };
        using (Hosted.Show(danger))
        {
            Assert.Equal(Palette.Color("B.DangerSoft"), ColourOf(danger.Background));
            Assert.Equal(Palette.Color("B.Danger"), ColourOf(danger.Foreground));
        }
    }

    /// <summary>BtnTpl 的四层都要在:根 Grid、底 Border、悬停遮罩、键盘焦点环、内容呈现器。</summary>
    [Fact]
    public void Button_template_has_the_four_layers_and_a_content_presenter()
    {
        var button = new Button { Content = "启动", Classes = { "btn-primary" } };
        using Hosted host = Hosted.Show(button);

        Assert.Equal("Root", ByName<Grid>(button, "Root").Name);
        Assert.NotNull(ByName<Border>(button, "Bd"));
        Assert.NotNull(ByName<Border>(button, "Ov"));
        Assert.NotNull(ByName<Border>(button, "Kf"));
        Assert.Equal("启动", ByName<ContentPresenter>(button, "PART_ContentPresenter").Content);
    }

    /// <summary>
    /// 按钮状态伪类::pressed 让模板根缩小(WPF 是 ScaleTransform 动画,Avalonia 是 RenderTransform),
    /// :focus-visible 让键盘焦点环亮起。
    /// </summary>
    [Fact]
    public void Pressed_and_focus_pseudo_classes_drive_the_button_template()
    {
        var button = new Button { Content = "x", Classes = { "btn-normal" } };
        using Hosted host = Hosted.Show(button);

        Assert.Null(ByName<Grid>(button, "Root").RenderTransform);
        Assert.Equal(0.0, ByName<Border>(button, "Kf").Opacity);

        Pseudo(button).Set(":pressed", true);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(ByName<Grid>(button, "Root").RenderTransform);

        Pseudo(button).Set(":pressed", false);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(ByName<Grid>(button, "Root").RenderTransform);

        Pseudo(button).Set(":focus-visible", true);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.9, ByName<Border>(button, "Kf").Opacity);
    }

    /// <summary>
    /// 输入框:框架必须能在我们的模板里找到 PART_ScrollViewer / PART_TextPresenter,
    /// 并且 Text 双向流通(先在 XAML 里写 Text,再改 TextBox.Text)。
    /// </summary>
    [Fact]
    public void TextBox_template_parts_are_found_and_text_flows()
    {
        var box = new TextBox { Text = "hello", Classes = { "input" } };
        using Hosted host = Hosted.Show(box);

        Assert.Equal("hello", ByName<TextPresenter>(box, "PART_TextPresenter").Text);

        box.Text = "world";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("world", ByName<TextPresenter>(box, "PART_TextPresenter").Text);
    }

    /// <summary>占位文字:Tag 里的提示语在空文本时可见、有文本时隐藏。</summary>
    [Fact]
    public void Input_placeholder_shows_only_when_empty()
    {
        var box = new TextBox { Classes = { "input" }, Tag = "请输入名称" };
        using Hosted host = Hosted.Show(box);

        Assert.True(ByName<TextBlock>(box, "Ph").IsVisible);
        box.Text = "x";
        Dispatcher.UIThread.RunJobs();
        Assert.False(ByName<TextBlock>(box, "Ph").IsVisible);
    }

    /// <summary>密码框:Pw 走同一套模板,PasswordChar 由调用点给(Avalonia 没有 PasswordBox)。</summary>
    [Fact]
    public void Password_class_reuses_the_input_template_with_masked_text()
    {
        var box = new TextBox { Classes = { "pw" }, PasswordChar = '*' };
        using Hosted host = Hosted.Show(box);

        box.Text = "secret";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal('*', ByName<TextPresenter>(box, "PART_TextPresenter").PasswordChar);
    }

    /// <summary>输入框属性:Input.Multi 换行、Input.Log 只读 + 等宽。</summary>
    [Fact]
    public void Input_variants_set_the_windows_properties()
    {
        var multi = new TextBox { Classes = { "input-multi" } };
        using (Hosted.Show(multi))
        {
            Assert.True(multi.AcceptsReturn);
            Assert.Equal(TextWrapping.Wrap, multi.TextWrapping);
            Assert.Equal(VerticalAlignment.Top, multi.VerticalContentAlignment);
        }

        var log = new TextBox { Classes = { "input-log" } };
        using (Hosted.Show(log))
        {
            Assert.True(log.IsReadOnly);
            Assert.Equal(11.5, log.FontSize);
            Assert.Equal("Menlo", log.FontFamily?.Name);
        }
    }

    /// <summary>Chk:选中时填充层与勾都亮起来(伪类 :checked 的嵌套样式)。</summary>
    [Fact]
    public void Checkbox_checked_state_reveals_fill_and_tick()
    {
        var check = new CheckBox { Content = "x", Classes = { "chk" } };
        using Hosted host = Hosted.Show(check);

        Assert.Equal(0.0, ByName<Border>(check, "Fill").Opacity);
        check.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1.0, ByName<Border>(check, "Fill").Opacity);
        Assert.Equal(1.0, ByName<Avalonia.Controls.Shapes.Path>(check, "Tick").Opacity);
    }

    /// <summary>Switch:选中时轨道点亮。</summary>
    [Fact]
    public void Switch_checked_state_lights_the_track()
    {
        var toggle = new CheckBox { Classes = { "switch" } };
        using Hosted host = Hosted.Show(toggle);

        Assert.Equal(0.0, ByName<Border>(toggle, "TrOn").Opacity);
        toggle.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1.0, ByName<Border>(toggle, "TrOn").Opacity);
        Assert.Equal(Palette.Color("B.Accent"), ColourOf(ByName<Border>(toggle, "TrOn").Background));
    }

    /// <summary>分段项:选中时白底 + 粗体(伪类 :checked 里改的是模板内的 Border#Bd)。</summary>
    [Fact]
    public void Seg_item_checked_state_paints_the_white_pill()
    {
        var item = new ToggleButton { Content = "x", Classes = { "seg-item" } };
        using Hosted host = Hosted.Show(item);

        Assert.NotNull(item.Theme);
        item.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Palette.Color("B.SegOn"), ColourOf(ByName<Border>(item, "Bd").Background));
        Assert.Equal(FontWeight.Bold, item.FontWeight);
    }

    /// <summary>页签项:选中时底部指示条亮起。</summary>
    [Fact]
    public void Tab_top_checked_state_shows_the_underline_bar()
    {
        var item = new ToggleButton { Content = "x", Classes = { "tab-top" } };
        using Hosted host = Hosted.Show(item);

        Assert.Equal(0.0, ByName<Border>(item, "Bar").Opacity);
        item.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1.0, ByName<Border>(item, "Bar").Opacity);
        Assert.Equal(FontWeight.Bold, item.FontWeight);
    }

    /// <summary>TabItemBtn 用的是自己那份模板(下划线是会缩放的 Border#Un)。</summary>
    [Fact]
    public void Tab_item_button_uses_its_own_template()
    {
        var item = new ToggleButton { Content = "x", Classes = { "tab-item-btn" } };
        using Hosted host = Hosted.Show(item);

        Assert.Equal(0.0, ByName<Border>(item, "Un").Opacity);
        item.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1.0, ByName<Border>(item, "Un").Opacity);
        Assert.Equal(Palette.Color("B.AccentText"), ColourOf(item.Foreground));
    }

    /// <summary>
    /// 滑块:轨道换成我们自己的 Track 模板(18×18 白底 + 4px 主题色圆环),
    /// 并且布局仍然正确 —— 值到中位时滑块真的落到轨道中间(不是被样式卡在左端)。
    /// </summary>
    [Fact]
    public void Slider_uses_our_track_template_and_still_lays_out()
    {
        var slider = new Slider { Width = 200, Minimum = 0, Maximum = 100, Value = 0, Classes = { "sld" } };
        using Hosted host = Hosted.Show(slider);

        Track track = slider.GetVisualDescendants().OfType<Track>().First();
        Assert.NotNull(slider.Theme);
        Assert.Equal(typeof(Slider), slider.Theme!.TargetType);
        Assert.Same(slider.FindResource("SliderTheme"), slider.Theme);
        Thumb thumb = track.GetVisualDescendants().OfType<Thumb>().First();
        Assert.Equal(18.0, thumb.Width);
        Assert.Equal(18.0, thumb.Height);
        Assert.Equal(new CornerRadius(9), thumb.CornerRadius);
        Assert.Equal(Palette.Color("B.Accent"), ColourOf(thumb.BorderBrush));
        Assert.Equal(Colors.White, ColourOf(thumb.Background));

        double left = thumb.Bounds.X;
        slider.Value = 50;
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        double middle = thumb.Bounds.X;
        Assert.True(middle > left, "滑块必须随值移动:left=" + left + " middle=" + middle);
        Assert.True(middle > 40 && middle < 160, "值到中位时滑块应落在轨道中间,实测 x=" + middle);
    }

    /// <summary>滚动条:竖直方向 10px 宽、滑块用色板里的灰(WPF 的 ScrollThumb 样式)。</summary>
    [Fact]
    public void Scrollbar_is_ten_pixels_and_uses_the_scroll_brushes()
    {
        var content = new StackPanel();
        for (int i = 0; i < 60; i++)
        {
            content.Children.Add(new TextBlock { Text = "row " + i });
        }

        var viewer = new ScrollViewer { Height = 80, Content = content };
        using Hosted host = Hosted.Show(viewer);

        ScrollBar bar = host.Window.GetVisualDescendants().OfType<ScrollBar>().First(b => b.Classes.Contains(":vertical"));
        Assert.Equal(10.0, bar.Width);
        Thumb thumb = bar.GetVisualDescendants().OfType<Thumb>().First();
        Assert.Equal(Palette.Color("B.Scroll"), ColourOf(thumb.Background));
    }

    /// <summary>ScrollThumb 类本身也能单独用在 Thumb 上。</summary>
    [Fact]
    public void Scroll_thumb_class_styles_a_plain_thumb()
    {
        var thumb = new Thumb { Classes = { "scroll-thumb" }, Width = 8, Height = 40 };
        using Hosted host = Hosted.Show(thumb);

        Assert.Equal(Palette.Color("B.Scroll"), ColourOf(thumb.Background));
        Assert.Equal(new CornerRadius(4), thumb.CornerRadius);
    }

    /// <summary>进度条:高度 6、前景色与底色都取色板(PART_Indicator 由框架自己找)。</summary>
    [Fact]
    public void Progress_bar_uses_the_prog_style()
    {
        var bar = new ProgressBar { Value = 40, Width = 160, Classes = { "prog" } };
        using Hosted host = Hosted.Show(bar);

        Assert.Equal(6.0, bar.Height);
        Assert.Equal(Palette.Color("B.Accent"), ColourOf(bar.Foreground));
        Assert.Equal(Palette.Color("B.LineSoft"), ColourOf(bar.Background));
        Assert.Contains(bar.GetVisualDescendants(), v => v is Border b && b.Name == "PART_Indicator");
    }

    /// <summary>
    /// 下拉框:我们的模板里只有一个 Popup#PART_Popup,框架必须能找到它并把下拉项渲染出来
    /// (headless 下走窗口的 OverlayLayer,与 Fluent 自带主题同一套机制)。
    /// </summary>
    [Fact]
    public void ComboBox_dropdown_opens_with_our_popup_part()
    {
        var combo = new ComboBox { Width = 160, Classes = { "combo" }, ItemsSource = new[] { "a", "b" }, SelectedIndex = 0 };
        using Hosted host = Hosted.Show(combo);

        Assert.Empty(host.Window.GetVisualDescendants().OfType<ComboBoxItem>());
        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();

        List<ComboBoxItem> items = host.Window.GetVisualDescendants().OfType<ComboBoxItem>().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(new object[] { "a", "b" }, items.Select(i => i.Content).ToArray());
        Assert.Equal(combo.SelectionBoxItem, "a");
    }

    /// <summary>下拉项选中态:强调色底 + 强调色文字(:selected 的嵌套样式)。</summary>
    [Fact]
    public void ComboBox_item_selected_state_uses_accent_soft()
    {
        var item = new ComboBoxItem { Content = "x", Classes = { "combo-item" }, IsSelected = true };
        using Hosted host = Hosted.Show(item);

        Assert.Equal(Palette.Color("B.AccentSoft"), ColourOf(ByName<Border>(item, "Bd").Background));
        Assert.Equal(Palette.Color("B.AccentText"), ColourOf(item.Foreground));
    }

    /// <summary>列表行:选中时强调色软底(WPF 的 ListItemRow)。</summary>
    [Fact]
    public void List_item_row_selected_state_paints_the_soft_background()
    {
        var list = new ListBox { Height = 120, Classes = { "plain-list" }, ItemsSource = new[] { "a", "b" }, SelectedIndex = 0 };
        using Hosted host = Hosted.Show(list);

        ListBoxItem item = host.Window.GetVisualDescendants().OfType<ListBoxItem>().First();
        Assert.Equal(1.0, ByName<Border>(item, "Sel").Opacity);
        Assert.Equal(Palette.Color("B.AccentSoft"), ColourOf(ByName<Border>(item, "Sel").Background));
    }

    /// <summary>PlainList:无边框、无内边距、横向不滚动、行样式来自 ListItemRow。</summary>
    [Fact]
    public void Plain_list_class_strips_the_chrome()
    {
        var list = new ListBox { Classes = { "plain-list" } };
        using Hosted host = Hosted.Show(list);

        Assert.Equal(new Thickness(0), list.BorderThickness);
        Assert.Equal(new Thickness(0), list.Padding);
        Assert.Equal(ScrollBarVisibility.Disabled, ScrollViewer.GetHorizontalScrollBarVisibility(list));
        Assert.NotNull(list.ItemContainerTheme);
    }

    /// <summary>提示浮层:WPF 的重画版模板(Paper 底、1px 边、圆角 8、9,6 内边距)。</summary>
    [Fact]
    public void ToolTip_uses_the_ported_template()
    {
        var tip = new ToolTip { Content = "提示" };
        using Hosted host = Hosted.Show(tip);

        Border border = tip.GetVisualDescendants().OfType<Border>().First();
        Assert.Equal(Palette.Color("B.Paper"), ColourOf(border.Background));
        Assert.Equal(new CornerRadius(8), border.CornerRadius);
        Assert.Equal(new Thickness(9, 6, 9, 6), border.Padding);
    }

    /// <summary>
    /// 图标路径:Ico.Line / Ico.Caption 的 Stroke 跟随所在控件的 Foreground
    /// (WPF 用 RelativeSource AncestorType=Control 绑定,这里对应 $parent[Control])。
    /// </summary>
    [Fact]
    public void Icon_paths_follow_the_parent_foreground()
    {
        var path = new Avalonia.Controls.Shapes.Path { Classes = { "ico-line" }, Data = Geometry.Parse("M0,0 L8,8") };
        var button = new Button { Content = path, Classes = { "btn-ghost" } };
        using Hosted host = Hosted.Show(button);

        Assert.Equal(16.0, path.Width);
        Assert.Equal(1.7, path.StrokeThickness);
        Assert.Equal(Palette.Color("B.InkSoft"), ColourOf(path.Stroke));
    }

    /// <summary>Ico.Caption 是 10×10 / 线宽 1.1(WPF 里 BasedOn Ico.Line)。</summary>
    [Fact]
    public void Icon_caption_class_is_ten_by_ten()
    {
        var path = new Avalonia.Controls.Shapes.Path { Classes = { "ico-caption" }, Data = Geometry.Parse("M0,0 L8,8") };
        var button = new Button { Content = path };
        using Hosted host = Hosted.Show(button);

        Assert.Equal(10.0, path.Width);
        Assert.Equal(10.0, path.Height);
        Assert.Equal(1.1, path.StrokeThickness);
    }
}
