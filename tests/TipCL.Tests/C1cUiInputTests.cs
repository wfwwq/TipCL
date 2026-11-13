using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.Services;
using TipCL.Themes;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C1c:门面 <c>Ui</c> 的输入控件与复合构建器(输入框 / 下拉 / 勾选 / 开关 / 滑块 / 进度条 /
/// Field / Section / Empty / Skeleton / RowCard)。
///
/// 与 <see cref="C1cUiTests"/> 同一条纪律:一切 Avalonia 工作经 <see cref="AvaloniaSession.Run(Action)"/>,
/// 期望值来源逐条写在用例上方(Windows 源 Services\Ui.cs / C1b 的 Controls.axaml / 任务书)。
/// </summary>
public class C1cUiInputTests
{
    private static void OnAvalonia(Action body)
    {
        AvaloniaSession.Run(body);
    }

    // ---------------------------------------------------------------- 输入框

    /// <summary>来源:Windows 源 Ui.cs:399-412(Input:占位走 Tag,宽度只在给定且非 NaN 时设)。</summary>
    [Fact]
    public void Input_sets_tag_text_and_width()
    {
        OnAvalonia(() =>
        {
            TextBox box = Ui.Input("name", "TipCL", 220.0);

            Assert.Contains("input", box.Classes);
            Assert.Equal("name", box.Tag);
            Assert.Equal("TipCL", box.Text);
            Assert.Equal(220.0, box.Width);

            Assert.True(double.IsNaN(Ui.Input("name").Width));
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:414-423(Multi:高度参数 + 类 Input.Multi)。</summary>
    [Fact]
    public void Multi_sets_height_and_placeholder()
    {
        OnAvalonia(() =>
        {
            TextBox box = Ui.Multi("memo", "text", 120.0);

            Assert.Contains("input-multi", box.Classes);
            Assert.Equal("memo", box.Tag);
            Assert.Equal("text", box.Text);
            Assert.Equal(120.0, box.Height);
        });
    }

    /// <summary>
    /// 来源:C1b 的 Controls.axaml(TextBox.input-multi:AcceptsReturn=true / Wrap / 内容顶端对齐)
    /// + Windows 源 Ui.cs:414-423。
    /// </summary>
    [Fact]
    public void Multi_inherits_the_class_style()
    {
        OnAvalonia(() =>
        {
            TextBox box = Ui.Multi("memo");

            using UiHost host = UiHost.Show(box);
            UiHost.Flush();

            Assert.True(box.AcceptsReturn);
            Assert.Equal(TextWrapping.Wrap, box.TextWrapping);
            Assert.Equal(VerticalAlignment.Top, box.VerticalContentAlignment);
        });
    }

    /// <summary>
    /// 来源:Windows 源 Ui.cs:425-431(LogBox 无参数)+ C1b 的 Controls.axaml
    /// (input-log:只读 / F.Mono 11.5 / NoWrap / 前景 InkSoft)。
    /// </summary>
    [Fact]
    public void LogBox_is_readonly_mono()
    {
        OnAvalonia(() =>
        {
            TextBox box = Ui.LogBox();

            Assert.Contains("input-log", box.Classes);

            using UiHost host = UiHost.Show(box);
            UiHost.Flush();

            Assert.True(box.IsReadOnly);
            Assert.Equal(TextWrapping.NoWrap, box.TextWrapping);
            Assert.Equal(11.5, box.FontSize);
            Assert.Equal("Menlo", box.FontFamily!.Name);
            Assert.Equal(Palette.Color("B.InkSoft"), C1cExpected.ColourOf(box.Foreground));
        });
    }

    /// <summary>
    /// 来源:Windows 源 Ui.cs:433-444 —— **返回类型由 WPF 的 <c>PasswordBox</c> 改成 <c>TextBox</c>**
    /// (Avalonia 11 没有 PasswordBox,见 D-75);掩码字符取 WPF PasswordBox 的默认值 ●(U+25CF)。
    /// </summary>
    [Fact]
    public void Pw_returns_a_textbox_with_a_password_char()
    {
        OnAvalonia(() =>
        {
            TextBox box = Ui.Pw(180.0);

            Assert.Contains("pw", box.Classes);
            Assert.Equal('\u25cf', box.PasswordChar);
            Assert.Equal(180.0, box.Width);
            Assert.False(box.RevealPassword);

            Assert.True(double.IsNaN(Ui.Pw().Width));
        });
    }

    // ---------------------------------------------------------------- 下拉

    /// <summary>来源:Windows 源 Ui.cs:446-474(填项 / 选中 / 宽度 / 占位 / 可编辑)。</summary>
    [Fact]
    public void Combo_fills_items_and_selects_the_requested_one()
    {
        OnAvalonia(() =>
        {
            ComboBox combo = Ui.Combo(new[] { "a", "b", "c" }, "b", 150.0, "pick", editable: true);

            Assert.Contains("combo", combo.Classes);
            Assert.Equal(new[] { "a", "b", "c" }, combo.Items.Cast<string>().ToArray());
            Assert.Equal("b", combo.SelectedItem);
            Assert.Equal(150.0, combo.Width);
            Assert.Equal("pick", combo.Tag);
            Assert.True(combo.IsEditable);
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:461-468(请求项不在表里 → 退回第 0 项;空表 → 不选)。</summary>
    [Fact]
    public void Combo_falls_back_to_the_first_item()
    {
        OnAvalonia(() =>
        {
            ComboBox combo = Ui.Combo(new[] { "a", "b" }, "zzz");

            Assert.Equal(0, combo.SelectedIndex);
            Assert.Equal("a", combo.SelectedItem);

            ComboBox empty = Ui.Combo();
            Assert.Empty(empty.Items);
            Assert.Equal(-1, empty.SelectedIndex);
            Assert.True(double.IsNaN(empty.Width));
            Assert.False(empty.IsEditable);
        });
    }

    // ---------------------------------------------------------------- 勾选 / 开关 / 滑块 / 进度

    /// <summary>来源:Windows 源 Ui.cs:476-490(Check:类 Chk + Content + IsChecked + 变化回调)。</summary>
    [Fact]
    public void Check_sets_label_and_value()
    {
        OnAvalonia(() =>
        {
            CheckBox check = Ui.Check("enable", true);

            Assert.Contains("chk", check.Classes);
            Assert.Equal("enable", check.Content);
            Assert.True(check.IsChecked);
        });
    }

    /// <summary>
    /// 来源:Windows 源 Ui.cs:484-488(Checked / Unchecked 都接同一个回调)。
    /// mac 侧接等价且不过时的 <c>IsCheckedChanged</c>(见 PROGRESS.md 的差异表)。
    /// </summary>
    [Fact]
    public void Check_invokes_the_changed_handler()
    {
        OnAvalonia(() =>
        {
            int changes = 0;
            CheckBox check = Ui.Check("enable", true, (s, e) => changes++);

            check.IsChecked = false;
            Assert.Equal(1, changes);

            check.IsChecked = true;
            Assert.Equal(2, changes);
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:492-506(Switch:类 Switch + 垂直居中 + 变化回调)。</summary>
    [Fact]
    public void Switch_sets_class_and_alignment()
    {
        OnAvalonia(() =>
        {
            int changes = 0;
            CheckBox toggle = Ui.Switch(true, (s, e) => changes++);

            Assert.Contains("switch", toggle.Classes);
            Assert.True(toggle.IsChecked);
            Assert.Equal(VerticalAlignment.Center, toggle.VerticalAlignment);
            Assert.Null(toggle.Content);

            toggle.IsChecked = false;
            Assert.Equal(1, changes);
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:508-519(值按 [min,max] 夹住 + tick 大于 0 才吸附)。</summary>
    [Fact]
    public void Sld_clamps_the_value_and_enables_snapping()
    {
        OnAvalonia(() =>
        {
            Slider slider = Ui.Sld(0.0, 10.0, 12.0);

            Assert.Contains("sld", slider.Classes);
            Assert.Equal(0.0, slider.Minimum);
            Assert.Equal(10.0, slider.Maximum);
            Assert.Equal(10.0, slider.Value);
            Assert.Equal(1.0, slider.TickFrequency);
            Assert.True(slider.IsSnapToTickEnabled);
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:516-517(tick 传 0 时不吸附)。</summary>
    [Fact]
    public void Sld_with_zero_tick_disables_snapping()
    {
        OnAvalonia(() =>
        {
            Slider slider = Ui.Sld(-5.0, 5.0, -9.0, 0.0);

            Assert.Equal(-5.0, slider.Value);
            Assert.False(slider.IsSnapToTickEnabled);
        });
    }

    /// <summary>
    /// 来源:Windows 源 Ui.cs:521-529(Value / Maximum=100)+ C1b 的 Controls.axaml
    /// (ProgressBar.prog:高 6 / 前景 B.Accent / 底色 B.LineSoft)。
    /// </summary>
    [Fact]
    public void Prog_sets_value_and_the_prog_style()
    {
        OnAvalonia(() =>
        {
            ProgressBar bar = Ui.Prog(37.0);

            Assert.Contains("prog", bar.Classes);
            Assert.Equal(37.0, bar.Value);
            Assert.Equal(100.0, bar.Maximum);

            using UiHost host = UiHost.Show(bar);
            UiHost.Flush();

            Assert.Equal(6.0, bar.Height);
            Assert.Equal(Palette.Color("B.Accent"), C1cExpected.ColourOf(bar.Foreground));
            Assert.Equal(Palette.Color("B.LineSoft"), C1cExpected.ColourOf(bar.Background));
        });
    }

    // ---------------------------------------------------------------- 复合构建器

    /// <summary>
    /// 来源:Windows 源 Ui.cs:564-575(Field:"labelWidth,*" 两列;左列是 V(2, label, Small(hint).Wrap());
    /// 控件竖直居中;整个栅格上下留 4)。
    /// </summary>
    [Fact]
    public void Field_builds_label_hint_and_control()
    {
        OnAvalonia(() =>
        {
            var control = new TextBox();
            Grid grid = Ui.Field("name", control, "hint", 160.0);

            // Windows 源 Ui.cs:566 是 G(null, "160,*"):**只有两列,没有行定义**(行由栅格自动生成)。
            Assert.Empty(grid.RowDefinitions);
            Assert.Equal(2, grid.ColumnDefinitions.Count);
            Assert.Equal(new GridLength(160.0), grid.ColumnDefinitions[0].Width);
            Assert.Equal(new GridLength(1.0, GridUnitType.Star), grid.ColumnDefinitions[1].Width);
            Assert.Equal(new Thickness(0.0, 4.0, 0.0, 4.0), grid.Margin);

            SPanel label = Assert.IsType<SPanel>(grid.Children[0]);
            Assert.Equal(VerticalAlignment.Center, label.VerticalAlignment);
            Assert.Equal(new Thickness(0.0, 0.0, 10.0, 0.0), label.Margin);
            Assert.Equal(2, label.Children.Count);
            Assert.Equal("name", Assert.IsType<TextBlock>(label.Children[0]).Text);

            TextBlock hint = Assert.IsType<TextBlock>(label.Children[1]);
            Assert.Equal("hint", hint.Text);
            Assert.Equal(TextWrapping.Wrap, hint.TextWrapping);

            Assert.Same(control, grid.Children[1]);
            Assert.Equal(1, Grid.GetColumn(control));
            Assert.Equal(VerticalAlignment.Center, control.VerticalAlignment);
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:567(hint 为 null 时只放标签)。</summary>
    [Fact]
    public void Field_without_a_hint_has_one_label_row()
    {
        OnAvalonia(() =>
        {
            Grid grid = Ui.Field("name", new TextBox());
            SPanel label = Assert.IsType<SPanel>(grid.Children[0]);

            Assert.Single(label.Children);
            Assert.Equal(new GridLength(140.0), grid.ColumnDefinitions[0].Width);
        });
    }

    /// <summary>
    /// 来源:Windows 源 Ui.cs:577-594(Section:"*,Auto" 两列;左列 V(2, H2, Muted) 且副标题
    /// NoWrap + 字符省略;右列竖直居中)。
    /// </summary>
    [Fact]
    public void Section_builds_title_sub_and_right()
    {
        OnAvalonia(() =>
        {
            var right = new Button();
            Grid grid = Ui.Section("title", "sub", right);

            Assert.Equal(2, grid.ColumnDefinitions.Count);
            Assert.Equal(new GridLength(1.0, GridUnitType.Star), grid.ColumnDefinitions[0].Width);
            Assert.Equal(GridLength.Auto, grid.ColumnDefinitions[1].Width);

            SPanel left = Assert.IsType<SPanel>(grid.Children[0]);
            Assert.Equal(2, left.Children.Count);
            Assert.Equal("title", Assert.IsType<TextBlock>(left.Children[0]).Text);
            Assert.Contains("t-h2", ((TextBlock)left.Children[0]).Classes);

            TextBlock sub = Assert.IsType<TextBlock>(left.Children[1]);
            Assert.Equal("sub", sub.Text);
            Assert.Equal(TextWrapping.NoWrap, sub.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, sub.TextTrimming);

            Assert.Same(right, grid.Children[1]);
            Assert.Equal(1, Grid.GetColumn(right));
            Assert.Equal(VerticalAlignment.Center, right.VerticalAlignment);
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:580-592(sub / right 都为 null 时的最小形态)。</summary>
    [Fact]
    public void Section_without_sub_or_right_has_one_child()
    {
        OnAvalonia(() =>
        {
            Grid grid = Ui.Section("title");
            SPanel left = Assert.IsType<SPanel>(grid.Children[0]);

            Assert.Single(grid.Children);
            Assert.Single(left.Children);
        });
    }

    /// <summary>
    /// 来源:Windows 源 Ui.cs:596-620(Empty:V(8, Glyph 24 InkFaint, 13px InkMuted 标题,
    /// 12px InkFaint 提示:Wrap / MaxWidth 420 / 居中),整块居中 + 上下 24,子项全部水平居中)。
    /// </summary>
    [Fact]
    public void Empty_builds_the_glyph_title_and_hint()
    {
        OnAvalonia(() =>
        {
            SPanel panel = Ui.Empty(Ico.Box, "nothing", "try again");

            Assert.Equal(Orientation.Vertical, panel.Orientation);
            Assert.Equal(8.0, panel.Spacing);
            Assert.Equal(HorizontalAlignment.Center, panel.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Center, panel.VerticalAlignment);
            Assert.Equal(new Thickness(0.0, 24.0, 0.0, 24.0), panel.Margin);
            Assert.Equal(3, panel.Children.Count);

            TextBlock glyph = Assert.IsType<TextBlock>(panel.Children[0]);
            Assert.Equal(Ico.Box, glyph.Text);
            Assert.Equal(24.0, glyph.FontSize);

            TextBlock title = Assert.IsType<TextBlock>(panel.Children[1]);
            Assert.Equal("nothing", title.Text);
            Assert.Equal(13.0, title.FontSize);

            TextBlock hint = Assert.IsType<TextBlock>(panel.Children[2]);
            Assert.Equal("try again", hint.Text);
            Assert.Equal(12.0, hint.FontSize);
            Assert.Equal(TextWrapping.Wrap, hint.TextWrapping);
            Assert.Equal(TextAlignment.Center, hint.TextAlignment);
            Assert.Equal(420.0, hint.MaxWidth);

            foreach (Control child in panel.Children)
            {
                Assert.Equal(HorizontalAlignment.Center, child.HorizontalAlignment);
            }

            using UiHost host = UiHost.Show(panel);
            Assert.Equal(Palette.Color("B.InkFaint"), C1cExpected.ColourOf(glyph.Foreground));
            Assert.Equal(Palette.Color("B.InkMuted"), C1cExpected.ColourOf(title.Foreground));
            Assert.Equal(Palette.Color("B.InkFaint"), C1cExpected.ColourOf(hint.Foreground));
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:603(hint 为 null 时只有图标 + 标题)。</summary>
    [Fact]
    public void Empty_without_a_hint_has_two_children()
    {
        OnAvalonia(() =>
        {
            SPanel panel = Ui.Empty(Ico.Box, "nothing");

            Assert.Equal(2, panel.Children.Count);
        });
    }

    /// <summary>
    /// 来源:Windows 源 Ui.cs:622-660(骨架:高 / 可选宽 / 圆角 6 / ClipToBounds / 底色 B.LineSoft;
    /// 里面一条宽 90、左对齐、三段白的渐变条,初始位移 -90)。
    /// </summary>
    [Fact]
    public void Skeleton_builds_the_static_shell()
    {
        OnAvalonia(() =>
        {
            Border shell = Ui.Skeleton(20.0, 120.0);

            Assert.Equal(20.0, shell.Height);
            Assert.Equal(120.0, shell.Width);
            Assert.Equal(new CornerRadius(6.0), shell.CornerRadius);
            Assert.True(shell.ClipToBounds);

            Border glide = Assert.IsType<Border>(shell.Child);
            Assert.Equal(90.0, glide.Width);
            Assert.Equal(HorizontalAlignment.Left, glide.HorizontalAlignment);

            LinearGradientBrush gradient = Assert.IsType<LinearGradientBrush>(glide.Background);
            Assert.Equal(3, gradient.GradientStops.Count);
            Assert.Equal(0, gradient.GradientStops[0].Color.A);
            Assert.Equal(120, gradient.GradientStops[1].Color.A);
            Assert.Equal(0, gradient.GradientStops[2].Color.A);

            TranslateTransform slide = Assert.IsType<TranslateTransform>(glide.RenderTransform);
            Assert.Equal(-90.0, slide.X);
            Assert.Equal(0.0, slide.Y);

            using UiHost host = UiHost.Show(shell);
            Assert.Equal(Palette.Color("B.LineSoft"), C1cExpected.ColourOf(shell.Background));
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:622-632(默认高 16 / 不给宽时保持 NaN)。</summary>
    [Fact]
    public void Skeleton_defaults_are_sixteen_pixels_high()
    {
        OnAvalonia(() =>
        {
            Border shell = Ui.Skeleton();

            Assert.Equal(16.0, shell.Height);
            Assert.True(double.IsNaN(shell.Width));
        });
    }

    /// <summary>
    /// 来源:Windows 源 Ui.cs:662-679(RowCard:圆角 8 / Padding / 1px 边框 / 内容 /
    /// 有回调时手型光标 / 底色 B.Paper + 边框色 B.Line)+ mac 侧把 MouseLeftButtonUp 换成
    /// PointerReleased(只认左键)。
    /// </summary>
    [Fact]
    public void RowCard_wires_the_click()
    {
        OnAvalonia(() =>
        {
            int clicks = 0;
            PointerReleasedEventArgs? seen = null;
            var body = new Border();
            Border card = Ui.RowCard(body, (s, e) => { clicks++; seen = e; }, 14.0);

            Assert.Equal(new CornerRadius(8.0), card.CornerRadius);
            Assert.Equal(new Thickness(14.0), card.Padding);
            Assert.Equal(new Thickness(1.0), card.BorderThickness);
            Assert.Same(body, card.Child);
            Assert.NotNull(card.Cursor);

            card.RaiseEvent(Release(MouseButton.Left, card));

            Assert.Equal(1, clicks);
            Assert.NotNull(seen);
            Assert.Equal(MouseButton.Left, seen!.InitialPressMouseButton);

            using UiHost host = UiHost.Show(card);
            Assert.Equal(Palette.Color("B.Paper"), C1cExpected.ColourOf(card.Background));
            Assert.Equal(Palette.Color("B.Line"), C1cExpected.ColourOf(card.BorderBrush));
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:674-677(MouseLeftButtonUp 只认左键)。</summary>
    [Fact]
    public void RowCard_ignores_the_right_button()
    {
        OnAvalonia(() =>
        {
            int clicks = 0;
            Border card = Ui.RowCard(new Border(), (s, e) => clicks++);

            card.RaiseEvent(Release(MouseButton.Right, card));

            Assert.Equal(0, clicks);
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:670(没有回调时不设手型光标)。</summary>
    [Fact]
    public void RowCard_without_a_click_has_no_cursor()
    {
        OnAvalonia(() =>
        {
            Border card = Ui.RowCard(new Border());

            Assert.Null(card.Cursor);
        });
    }

    /// <summary>造一个程序化的指针抬起事件(headless 不模拟输入设备,只走事件路由)。</summary>
    private static PointerReleasedEventArgs Release(MouseButton button, Visual source)
    {
        return new PointerReleasedEventArgs(
            source,
            new Pointer(1, PointerType.Mouse, true),
            source,
            new Point(0.0, 0.0),
            0UL,
            new PointerPointProperties(),
            KeyModifiers.None,
            button);
    }
}
