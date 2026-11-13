using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using TipCL.Services;
using TipCL.Themes;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C1c:门面 <c>Ui</c> / <c>UiExt</c> 在**真 Avalonia 平台**上的行为(headless + Skia,
/// 与 <c>--smoke</c> 同一套配置)。样式类名、动态资源的求值、布局位置这些都要真平台才算数,
/// 所以每条用例体都经 <see cref="AvaloniaSession.Run(Action)"/>(D-79 的强制纪律)。
///
/// 期望值来源逐条写在用例上方:
/// - **Windows 源**:`Services\Ui.cs` / `Services\UiExt.cs` 的参数默认值、结构、取色键;
/// - **C1b 样式表**:`Themes\Controls.axaml` 里类选择器设的字号/字重/圆角/颜色(本机读过那个文件);
/// - **任务书**:门面公开面必须与 Windows 版对齐这一条。
/// </summary>
public class C1cUiTests
{
    /// <summary>把一条用例体放进会话里跑(门面的所有断言都必须在 Avalonia 线程上)。</summary>
    private static void OnAvalonia(Action body)
    {
        AvaloniaSession.Run(body);
    }

    // ---------------------------------------------------------------- 文本

    /// <summary>
    /// 六个文本构建器 -> C1b 类名 + 字号 + 字重 + 前景色。
    /// 来源:C1b 的 Controls.axaml(t-h1 20 SemiBold / t-h2 17 SemiBold / t-h3 15 SemiBold /
    /// t-muted 12 InkMuted / t-small 11 InkFaint / t-mono 12 InkSoft)+ Windows 源 Ui.cs:96-133
    /// 里 H1/H2/H3/Muted/Small/Mono 各自用哪个键。
    /// </summary>
    [Theory]
    [InlineData("H1", "t-h1", 20.0, "SemiBold", "B.Ink")]
    [InlineData("H2", "t-h2", 17.0, "SemiBold", "B.Ink")]
    [InlineData("H3", "t-h3", 15.0, "SemiBold", "B.Ink")]
    [InlineData("Muted", "t-muted", 12.0, "Normal", "B.InkMuted")]
    [InlineData("Small", "t-small", 11.0, "Normal", "B.InkFaint")]
    [InlineData("Mono", "t-mono", 12.0, "Normal", "B.InkSoft")]
    public void Text_builders_carry_the_C1b_class_and_style_values(string builder, string token, double size, string weight, string foreground)
    {
        OnAvalonia(() =>
        {
            TextBlock block = builder switch
            {
                "H1" => Ui.H1("title"),
                "H2" => Ui.H2("title"),
                "H3" => Ui.H3("title"),
                "Muted" => Ui.Muted("title"),
                "Small" => Ui.Small("title"),
                "Mono" => Ui.Mono("title"),
                _ => throw new ArgumentOutOfRangeException(nameof(builder), builder, "未收录的构建器"),
            };

            Assert.Equal("title", block.Text);
            Assert.Contains(token, block.Classes);
            Assert.Equal(token, StyleKeys.Map(TokenToKey(token)));

            using UiHost host = UiHost.Show(block);
            Assert.Equal(size, block.FontSize);
            Assert.Equal(C1cExpected.Weight(weight), block.FontWeight);
            Assert.Equal(Palette.Color(foreground), C1cExpected.ColourOf(block.Foreground));
        });
    }

    /// <summary>把 C1b 类名翻回 WPF 键(顺带证明映射表两向一致,键名写错会红)。</summary>
    private static string TokenToKey(string token)
    {
        return StyleKeys.Table.First(pair => pair.Value == token).Key;
    }

    /// <summary>来源:Windows 源 Ui.cs:77-94 —— 默认字号 13、NoWrap、无类名(不带样式键)、无前景。</summary>
    [Fact]
    public void Txt_defaults_match_the_Windows_source()
    {
        OnAvalonia(() =>
        {
            TextBlock block = Ui.Txt();

            Assert.Equal("", block.Text);
            Assert.Equal(13.0, block.FontSize);
            Assert.Equal(TextWrapping.NoWrap, block.TextWrapping);
            Assert.Empty(block.Classes);
            // 前景**不设**:Windows 源 Ui.cs:89-92 只在 fg != null 时 SetResourceReference,
            // 隐式 TextBlock 样式(Windows Controls.xaml 里那份)也不设 Foreground —— 本机实测
            // Avalonia 的 TextBlock 默认前景是 Black,与 WPF 的默认一致,所以这里不断言具体颜色。
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:85-88(bold 用 SemiBold)+ Ui.cs:89-92(fg 走动态资源)。</summary>
    [Fact]
    public void Txt_bold_and_foreground()
    {
        OnAvalonia(() =>
        {
            TextBlock bold = Ui.Txt("\u4e2d\u6587", 15.0, bold: true, "B.AccentStrong");
            Assert.Equal("\u4e2d\u6587", bold.Text);
            Assert.Equal(15.0, bold.FontSize);
            Assert.Equal(FontWeight.SemiBold, bold.FontWeight);

            using UiHost host = UiHost.Show(bold);
            Assert.Equal(Palette.Color("B.AccentStrong"), C1cExpected.ColourOf(bold.Foreground));
        });
    }

    // ---------------------------------------------------------------- 图标

    /// <summary>
    /// 来源:Windows 源 Ui.cs:135-149(GlyphFont / 字号 / VCenter)+ GOAL §3 的图标字体决策
    /// (avares 的 C1a 字体族 + 两个 Windows 兜底族,与 Controls.axaml 的 F.Icon 同一串)。
    /// </summary>
    [Fact]
    public void Glyph_uses_the_icon_font_and_the_requested_size()
    {
        OnAvalonia(() =>
        {
            TextBlock glyph = Ui.Glyph(Ico.Gear, 18.0);

            Assert.Equal(18.0, glyph.FontSize);
            Assert.Equal(VerticalAlignment.Center, glyph.VerticalAlignment);
            Assert.NotNull(glyph.FontFamily);
            Assert.Equal(Palette.IconFontFamilyName, glyph.FontFamily!.Name);
            Assert.Equal(3, glyph.FontFamily.FamilyNames.Count);
            Assert.Equal(Palette.IconFontUri + ", Segoe Fluent Icons, Segoe MDL2 Assets", Ui.GlyphFontUri);
        });
    }

    /// <summary>来源:Services\Ico.cs 的码位表(C1a 已按字节级复制)+ Windows 源 Ui.cs:138(Text = glyph)。</summary>
    [Theory]
    [InlineData("Play", "\ue768")]
    [InlineData("Gear", "\ue713")]
    [InlineData("Export", "\uede1")]
    [InlineData("CloseWin", "\ue8bb")]
    public void Glyph_codepoints_come_from_Ico(string name, string expected)
    {
        OnAvalonia(() =>
        {
            string codepoint = name switch
            {
                "Play" => Ico.Play,
                "Gear" => Ico.Gear,
                "Export" => Ico.Export,
                "CloseWin" => Ico.CloseWin,
                _ => throw new ArgumentOutOfRangeException(nameof(name), name, "未收录的图标常量"),
            };

            Assert.Equal(expected, codepoint);
            Assert.Equal(expected, Ui.Glyph(codepoint).Text);
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:144-147(fg 走动态资源,其余不变)。</summary>
    [Fact]
    public void Glyph_foreground_follows_the_palette()
    {
        OnAvalonia(() =>
        {
            TextBlock glyph = Ui.Glyph(Ico.Star, 14.0, "B.Warn");

            using UiHost host = UiHost.Show(glyph);
            Assert.Equal(Palette.Color("B.Warn"), C1cExpected.ColourOf(glyph.Foreground));
        });
    }

    // ---------------------------------------------------------------- 面板 / 栅格

    /// <summary>来源:Windows 源 Ui.cs:151-179(纵向、Spacing、按顺序 Add、跳过 null)。</summary>
    [Fact]
    public void V_adds_children_in_order_and_skips_nulls()
    {
        OnAvalonia(() =>
        {
            var first = new Border();
            var second = new Border();

            SPanel panel = Ui.V(6.0, first, null, second);

            Assert.Equal(Orientation.Vertical, panel.Orientation);
            Assert.Equal(6.0, panel.Spacing);
            Assert.Equal(2, panel.Children.Count);
            Assert.Same(first, panel.Children[0]);
            Assert.Same(second, panel.Children[1]);
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:160-167(横向同 V,只换 Orientation)。</summary>
    [Fact]
    public void H_is_horizontal()
    {
        OnAvalonia(() =>
        {
            var only = new Border();

            SPanel panel = Ui.H(4.0, only);

            Assert.Equal(Orientation.Horizontal, panel.Orientation);
            Assert.Equal(4.0, panel.Spacing);
            Assert.Same(only, panel.Children[0]);
        });
    }

    /// <summary>
    /// 来源:Windows 源 SPanel.cs 的量/排规则(间距 = Spacing ×(可见子项数 − 1))+ 本机实测的
    /// Avalonia 布局结果(挂进窗口后读 Bounds)。
    /// </summary>
    [Fact]
    public void V_applies_the_spacing_inside_a_real_layout()
    {
        OnAvalonia(() =>
        {
            var first = new Border { Height = 20.0 };
            var second = new Border { Height = 30.0 };
            SPanel panel = Ui.V(10.0, first, second);

            using UiHost host = UiHost.Show(panel);
            UiHost.Flush();

            Assert.Equal(20.0, first.Bounds.Height, 3);
            Assert.Equal(30.0, second.Bounds.Y, 3);
            // 面板自身被窗口拉满,所以这里只钉子项的位置(间距 = 第二项起点 − 第一项高)。
        });
    }

    /// <summary>来源:Windows 源 SPanel.cs 的 ArrangeOverride(横向推进用宽度)。</summary>
    [Fact]
    public void H_applies_the_spacing_inside_a_real_layout()
    {
        OnAvalonia(() =>
        {
            var first = new Border { Width = 20.0 };
            var second = new Border { Width = 30.0 };
            SPanel panel = Ui.H(10.0, first, second);

            using UiHost host = UiHost.Show(panel);
            UiHost.Flush();

            Assert.Equal(30.0, second.Bounds.X, 3);
            // 面板自身被窗口拉满,只钉子项的横向位置。
        });
    }

    /// <summary>
    /// 来源:Windows 源 Ui.cs:181-199 —— <c>G("Auto,*","2*")</c> 得到 2 行 1 列,
    /// 行高分别是 Auto / 1* ,列宽是 2*。
    /// </summary>
    [Fact]
    public void G_builds_the_expected_row_and_column_definitions()
    {
        OnAvalonia(() =>
        {
            Grid grid = Ui.G("Auto,*", "2*");

            Assert.Equal(2, grid.RowDefinitions.Count);
            Assert.Equal(GridLength.Auto, grid.RowDefinitions[0].Height);
            Assert.Equal(new GridLength(1.0, GridUnitType.Star), grid.RowDefinitions[1].Height);
            Assert.Single(grid.ColumnDefinitions);
            Assert.Equal(new GridLength(2.0, GridUnitType.Star), grid.ColumnDefinitions[0].Width);
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:201-206(空/空白 spec 直接 yield break → 0 个定义)。</summary>
    [Fact]
    public void G_without_a_spec_has_no_definitions()
    {
        OnAvalonia(() =>
        {
            Grid grid = Ui.G(null, "");

            Assert.Empty(grid.RowDefinitions);
            Assert.Empty(grid.ColumnDefinitions);
        });
    }

    /// <summary>
    /// 来源:Windows 源 Ui.cs:207-233 的 Parse 分支:Auto / * / N* / 数字 / 认不出 → Auto。
    /// </summary>
    [Theory]
    [InlineData("Auto", 0)]
    [InlineData("*", 1)]
    [InlineData("3.5*", 2)]
    [InlineData("120", 3)]
    [InlineData("bogus", 0)]
    public void G_parses_every_spec_branch(string spec, int expectedKind)
    {
        OnAvalonia(() =>
        {
            Grid grid = Ui.G(spec);
            GridLength length = Assert.Single(grid.RowDefinitions).Height;

            switch (expectedKind)
            {
                case 0:
                    Assert.Equal(GridLength.Auto, length);
                    break;
                case 1:
                    Assert.Equal(new GridLength(1.0, GridUnitType.Star), length);
                    break;
                case 2:
                    Assert.Equal(new GridLength(3.5, GridUnitType.Star), length);
                    break;
                default:
                    Assert.Equal(new GridLength(120.0), length);
                    Assert.Equal(GridUnitType.Pixel, length.GridUnitType);
                    break;
            }
        });
    }

    /// <summary>来源:Windows 源 UiExt.cs:46-66(At 设四个附加属性,Add 顺手加进 Children)。</summary>
    [Fact]
    public void Add_places_a_child_with_row_column_and_spans()
    {
        OnAvalonia(() =>
        {
            Grid grid = Ui.G("Auto,Auto", "*,*");
            var child = new Border();

            Grid returned = grid.Add(child, 1, 1, 2, 0);

            Assert.Same(grid, returned);
            Assert.Same(child, Assert.Single(grid.Children));
            Assert.Equal(1, Grid.GetRow(child));
            Assert.Equal(1, Grid.GetColumn(child));
            Assert.Equal(2, Grid.GetRowSpan(child));
            Assert.Equal(1, Grid.GetColumnSpan(child));
        });
    }

    /// <summary>来源:Windows 源 UiExt.cs:46-59(span 为 1 时不设值,Avalonia 默认就是 1)。</summary>
    [Fact]
    public void At_leaves_defaults_for_span_one()
    {
        OnAvalonia(() =>
        {
            var child = new Border();

            Border returned = child.At(0);

            Assert.Same(child, returned);
            Assert.Equal(0, Grid.GetRow(child));
            Assert.Equal(0, Grid.GetColumn(child));
            Assert.Equal(1, Grid.GetRowSpan(child));
            Assert.Equal(1, Grid.GetColumnSpan(child));
        });
    }

    // ---------------------------------------------------------------- 卡片 / 分隔 / 标签

    /// <summary>
    /// 来源:Windows 源 Ui.cs:236-248(Card:类名 + Padding;内容为 null 时不给 Child)
    /// + C1b 的 Controls.axaml(Border.card:CR.Card = 12、B.Card 底、B.CardBorder 边)。
    /// </summary>
    [Fact]
    public void Card_has_the_class_padding_and_card_colours()
    {
        OnAvalonia(() =>
        {
            var body = new Border();
            Border card = Ui.Card(body, 14.0);

            Assert.Contains("card", card.Classes);
            Assert.Equal(new Thickness(14.0), card.Padding);
            Assert.Same(body, card.Child);

            using UiHost host = UiHost.Show(card);
            UiHost.Flush();

            Assert.Equal(new CornerRadius(12.0), card.CornerRadius);
            Assert.Equal(Palette.Color("B.Card"), C1cExpected.ColourOf(card.Background));
            Assert.Equal(Palette.Color("B.CardBorder"), C1cExpected.ColourOf(card.BorderBrush));
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:243-246(没给内容就不设 Child);样式表默认 padding 是 12。</summary>
    [Fact]
    public void Card_without_content_still_gets_the_default_padding()
    {
        OnAvalonia(() =>
        {
            Border card = Ui.Card();

            Assert.Null(card.Child);
            Assert.Equal(new Thickness(12.0), card.Padding);
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:250-263(Pane:圆角 / Padding / 背景走资源键,默认 B.Paper2)。</summary>
    [Fact]
    public void Pane_sets_radius_padding_and_background()
    {
        OnAvalonia(() =>
        {
            var body = new Border();
            // 背景键挑一个**实色**令牌(B.BannerFill 是渐变刷,不适用这里的颜色断言)。
            Border pane = Ui.Pane(body, "B.Paper", 6.0, 9.0);

            Assert.Equal(new CornerRadius(6.0), pane.CornerRadius);
            Assert.Equal(new Thickness(9.0), pane.Padding);
            Assert.Same(body, pane.Child);

            using UiHost host = UiHost.Show(pane);
            Assert.Equal(Palette.Color("B.Paper"), C1cExpected.ColourOf(pane.Background));
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:250-263(默认参数)与 C1b 里 Pane 的默认背景 B.Paper2。</summary>
    [Fact]
    public void Pane_defaults_use_Paper2_and_radius_eight()
    {
        OnAvalonia(() =>
        {
            Border pane = Ui.Pane(new Border());

            Assert.Equal(new CornerRadius(8.0), pane.CornerRadius);
            Assert.Equal(new Thickness(10.0), pane.Padding);

            using UiHost host = UiHost.Show(pane);
            Assert.Equal(Palette.Color("B.Paper2"), C1cExpected.ColourOf(pane.Background));
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:321-336(横线高 1 拉伸 / 竖线宽 1 拉伸 + B.LineSoft)。</summary>
    [Fact]
    public void Sep_handles_both_orientations()
    {
        OnAvalonia(() =>
        {
            Border horizontal = Ui.Sep();
            Border vertical = Ui.Sep(vertical: true);

            Assert.Equal(1.0, horizontal.Height);
            Assert.Equal(HorizontalAlignment.Stretch, horizontal.HorizontalAlignment);
            Assert.True(double.IsNaN(horizontal.Width));

            Assert.Equal(1.0, vertical.Width);
            Assert.Equal(VerticalAlignment.Stretch, vertical.VerticalAlignment);
            Assert.True(double.IsNaN(vertical.Height));

            using UiHost host = UiHost.Show(horizontal);
            Assert.Equal(Palette.Color("B.LineSoft"), C1cExpected.ColourOf(horizontal.Background));
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:338-345(0×0 的 Border,放在 H 里当弹簧)。</summary>
    [Fact]
    public void Spring_is_a_zero_sized_border()
    {
        OnAvalonia(() =>
        {
            Control spring = Ui.Spring();

            Assert.IsType<Border>(spring);
            Assert.Equal(0.0, spring.Width);
            Assert.Equal(0.0, spring.Height);
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:531-540(Tag 的圆角/Padding/VCenter + 子文本 11px 指定前景 + 背景键)。</summary>
    [Fact]
    public void Tag_layout_and_colours()
    {
        OnAvalonia(() =>
        {
            Border tag = Ui.Tag("beta", "B.Ok", "B.WarnSoft");

            Assert.Equal(new CornerRadius(8.0), tag.CornerRadius);
            Assert.Equal(new Thickness(6.0, 1.5, 6.0, 2.5), tag.Padding);
            Assert.Equal(VerticalAlignment.Center, tag.VerticalAlignment);

            TextBlock label = Assert.IsType<TextBlock>(tag.Child);
            Assert.Equal("beta", label.Text);
            Assert.Equal(11.0, label.FontSize);

            using UiHost host = UiHost.Show(tag);
            Assert.Equal(Palette.Color("B.Ok"), C1cExpected.ColourOf(label.Foreground));
            Assert.Equal(Palette.Color("B.WarnSoft"), C1cExpected.ColourOf(tag.Background));
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:531-540 的默认参数(fg = B.InkMuted / bg = B.LineSoft)。</summary>
    [Fact]
    public void Tag_defaults_use_ink_muted_on_line_soft()
    {
        OnAvalonia(() =>
        {
            Border tag = Ui.Tag("beta");

            using UiHost host = UiHost.Show(tag);
            Assert.Equal(Palette.Color("B.LineSoft"), C1cExpected.ColourOf(tag.Background));
            Assert.Equal(Palette.Color("B.InkMuted"), C1cExpected.ColourOf(((TextBlock)tag.Child!).Foreground));
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:542-562(999 圆角 / MinWidth 18 / 高 18 / 11px SemiBold / AccentStrong 底)。</summary>
    [Fact]
    public void Badge_layout_and_colours()
    {
        OnAvalonia(() =>
        {
            Border badge = Ui.Badge("7");

            Assert.Equal(new CornerRadius(999.0), badge.CornerRadius);
            Assert.Equal(new Thickness(6.0, 0.0, 6.0, 1.0), badge.Padding);
            Assert.Equal(18.0, badge.MinWidth);
            Assert.Equal(18.0, badge.Height);
            Assert.Equal(VerticalAlignment.Center, badge.VerticalAlignment);

            TextBlock label = Assert.IsType<TextBlock>(badge.Child);
            Assert.Equal(11.0, label.FontSize);
            Assert.Equal(FontWeight.SemiBold, label.FontWeight);
            Assert.Equal(HorizontalAlignment.Center, label.HorizontalAlignment);

            using UiHost host = UiHost.Show(badge);
            Assert.Equal(Palette.Color("B.AccentStrong"), C1cExpected.ColourOf(badge.Background));
            Assert.Equal(Palette.Color("B.OnAccentStrong"), C1cExpected.ColourOf(label.Foreground));
        });
    }

    // ---------------------------------------------------------------- 按钮

    /// <summary>
    /// 来源:Windows 源 Ui.cs:385-397(StyleOf 的 7 个分支)+ C1b 的类名映射表。
    /// </summary>
    [Theory]
    [InlineData(BtnKind.Primary, "btn-primary")]
    [InlineData(BtnKind.Soft, "btn-soft")]
    [InlineData(BtnKind.Normal, "btn-normal")]
    [InlineData(BtnKind.Ghost, "btn-ghost")]
    [InlineData(BtnKind.Danger, "btn-danger")]
    [InlineData(BtnKind.Chip, "btn-chip")]
    [InlineData(BtnKind.Icon, "btn-icon")]
    public void Btn_maps_every_kind_to_its_class(BtnKind kind, string token)
    {
        OnAvalonia(() =>
        {
            Button button = Ui.Btn("ok", kind);

            Assert.Contains(token, button.Classes);
            // C1b 的每个变体类自带 Btn.Base 的模板,所以门面只加变体类,不加 btn-base。
            Assert.DoesNotContain("btn-base", button.Classes);
            Assert.Equal("ok", button.Content);
        });
    }

    /// <summary>
    /// 来源:C1b 的 Controls.axaml(Button.btn-primary:Background AccentStrong / Foreground
    /// OnAccentStrong / FontWeight Bold)。
    /// </summary>
    [Fact]
    public void Primary_button_uses_the_accent_palette()
    {
        OnAvalonia(() =>
        {
            Button button = Ui.Btn("ok", BtnKind.Primary);

            using UiHost host = UiHost.Show(button);
            UiHost.Flush();

            Assert.Equal(Palette.Color("B.AccentStrong"), C1cExpected.ColourOf(button.Background));
            Assert.Equal(Palette.Color("B.OnAccentStrong"), C1cExpected.ColourOf(button.Foreground));
            Assert.Equal(FontWeight.Bold, button.FontWeight);
        });
    }

    /// <summary>
    /// 来源:Windows 源 Ui.cs:353 —— 有 glyph 时内容是 <c>H(6, Glyph(字号 13/12.5), Txt(text))</c>,
    /// text 为空则只有图标。
    /// </summary>
    [Fact]
    public void Btn_with_a_glyph_builds_the_icon_and_text_row()
    {
        OnAvalonia(() =>
        {
            Button primary = Ui.Btn("run", BtnKind.Primary, null, Ico.Play);
            SPanel row = Assert.IsType<SPanel>(primary.Content);
            Assert.Equal(Orientation.Horizontal, row.Orientation);
            Assert.Equal(6.0, row.Spacing);
            Assert.Equal(2, row.Children.Count);

            TextBlock icon = Assert.IsType<TextBlock>(row.Children[0]);
            Assert.Equal(Ico.Play, icon.Text);
            Assert.Equal(13.0, icon.FontSize);
            Assert.Equal("run", Assert.IsType<TextBlock>(row.Children[1]).Text);

            Button normal = Ui.Btn("", BtnKind.Normal, null, Ico.Play);
            SPanel iconOnly = Assert.IsType<SPanel>(normal.Content);
            Assert.Single(iconOnly.Children);
            Assert.Equal(12.5, Assert.IsType<TextBlock>(iconOnly.Children[0]).FontSize);
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:354-358(有文字就设自动化名,免得读屏读到图标私有码位)。</summary>
    [Fact]
    public void Btn_sets_the_automation_name_from_the_text()
    {
        OnAvalonia(() =>
        {
            Button button = Ui.Btn("start", BtnKind.Normal, null, Ico.Play);

            Assert.Equal("start", AutomationProperties.GetName(button));
        });
    }

    /// <summary>
    /// 来源:Windows 源 Ui.cs:359-362(<c>button.Click += click</c>)。程序化触发用
    /// <c>RaiseEvent(Button.ClickEvent)</c> —— 不模拟输入设备(headless 不造指针)。
    /// </summary>
    [Fact]
    public void Btn_wires_the_click_handler()
    {
        OnAvalonia(() =>
        {
            int clicks = 0;
            Button button = Ui.Btn("ok", BtnKind.Normal, (s, e) => clicks++);

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(1, clicks);
        });
    }

    /// <summary>
    /// 来源:Windows 源 Ui.cs:366-383(Btn.Icon 类 + 图标 + ToolTip + 自动化名 + 点击)。
    /// </summary>
    [Fact]
    public void IconBtn_sets_class_glyph_tip_and_click()
    {
        OnAvalonia(() =>
        {
            int clicks = 0;
            Button button = Ui.IconBtn(Ico.Refresh, "reload", (s, e) => clicks++, 16.0);

            Assert.Contains("btn-icon", button.Classes);
            TextBlock icon = Assert.IsType<TextBlock>(button.Content);
            Assert.Equal(Ico.Refresh, icon.Text);
            Assert.Equal(16.0, icon.FontSize);
            Assert.Equal("reload", ToolTip.GetTip(button));
            Assert.Equal("reload", AutomationProperties.GetName(button));

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, clicks);
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:366-383(tip 为 null 时不设 ToolTip / 自动化名)。</summary>
    [Fact]
    public void IconBtn_without_a_tip_leaves_the_tooltip_empty()
    {
        OnAvalonia(() =>
        {
            Button button = Ui.IconBtn(Ico.Refresh);

            Assert.Null(ToolTip.GetTip(button));
            Assert.Equal(14.0, Assert.IsType<TextBlock>(button.Content).FontSize);
        });
    }

    // ---------------------------------------------------------------- S / Res / 百分比

    /// <summary>
    /// 来源:Windows 源 Ui.cs:67-75(S(key) 取样式 / Res(key) 取刷子)+ mac 侧的差异决策:
    /// S 返回 C1b 的类名、Res 返回色板刷子,认不出的键都**不抛异常**(D-68 的兜底)。
    /// </summary>
    [Theory]
    [InlineData("Btn.Primary", "btn-primary")]
    [InlineData("T.PageTitle", "t-page-title")]
    [InlineData("Input.Multi", "input-multi")]
    public void S_returns_the_avalonia_class_token(string wpfKey, string token)
    {
        Assert.Equal(token, Ui.S(wpfKey));
    }

    /// <summary>来源:任务书要求「按键取样式」的键在这一层不许抛异常(C1b 的 StyleKeys.Map 对未知键返回空串)。</summary>
    [Fact]
    public void S_and_Res_do_not_throw_on_unknown_keys()
    {
        OnAvalonia(() =>
        {
            Assert.Equal("", Ui.S("No.Such.Key"));
            Assert.Equal("", Ui.S("BtnTpl"));

            Assert.Equal(Palette.Color("B.Ink"), C1cExpected.ColourOf(Ui.Res("B.Ink")));
            Assert.Equal(C1cExpected.Fallback, C1cExpected.ColourOf(Ui.Res("B.NoSuchKey")));
        });
    }

    /// <summary>
    /// 来源:Windows 源 Ui.cs:266-271 —— 宿主高 × pct,低于 floor 就取 floor,没有窗口时取 floor。
    /// 这里用可注入宿主高度的重载把四种分支钉死(纯算术,不需要平台)。
    /// </summary>
    [Theory]
    [InlineData(0.5, 100.0, 400.0, 200.0)]
    [InlineData(0.5, 300.0, 400.0, 300.0)]
    [InlineData(0.5, 100.0, 0.0, 100.0)]
    [InlineData(0.1, 0.0, 0.0, 0.0)]
    [InlineData(2.0, 100.0, 400.0, 800.0)]
    public void WindowPctOf_floors_the_result(double pct, double floor, double hostHeight, double expected)
    {
        Assert.Equal(expected, Ui.WindowPctOf(pct, floor, hostHeight));
    }

    /// <summary>来源:Windows 源 Ui.cs:17-38(转换器的算术与参数解析:double / 字符串 / 缺省 0)。</summary>
    [Fact]
    public void WindowPctConverter_converts_and_floors()
    {
        var converter = new Ui.WindowPctConverter(0.5);

        Assert.Equal(200.0, converter.Convert(400.0, typeof(double), 100.0, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(300.0, converter.Convert(400.0, typeof(double), "300", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(200.0, converter.Convert(400.0, typeof(double), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Same(BindingOperations.DoNothing, converter.ConvertBack(200.0, typeof(double), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>来源:Windows 源 Ui.cs:28-31(值不是正的 double 时返回 DoNothing,不参与绑定)。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("400")]
    [InlineData(0.0)]
    public void WindowPctConverter_returns_do_nothing_for_non_positive(object? value)
    {
        var converter = new Ui.WindowPctConverter(0.5);

        Assert.Same(BindingOperations.DoNothing, converter.Convert(value, typeof(double), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    // ---------------------------------------------------------------- 滚动

    /// <summary>来源:Windows 源 Ui.cs:273-280 + SmoothScroll.cs:41-47(纵向自动/横向禁用)。</summary>
    [Fact]
    public void Scroll_sets_content_padding_and_scrollbar_visibility()
    {
        OnAvalonia(() =>
        {
            var body = new Border();
            SmoothScroll scroll = Ui.Scroll(body, 8.0);

            Assert.Same(body, scroll.Content);
            Assert.Equal(new Thickness(8.0), scroll.Padding);
            Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
            Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
        });
    }

    /// <summary>
    /// 来源:Windows 源 SmoothScroll.cs:11-14 / 29-39(DependencyProperty Offset 设值即滚动)。
    /// 这里钉住"值能设进去、拿得出来,且没有抛异常";真滚动的位移需要真实内容尺寸,未验。
    /// </summary>
    [Fact]
    public void SmoothScroll_offset_round_trips()
    {
        OnAvalonia(() =>
        {
            SmoothScroll scroll = Ui.Scroll(new Border { Height = 2000.0 }, 0.0);
            scroll.Offset = 42.0;

            Assert.Equal(42.0, scroll.Offset);
        });
    }

    /// <summary>
    /// 来源:Windows 源 SmoothScroll.cs:16-25(MaxHeight 绑宿主窗口高度 × pct,不低于 floor)。
    /// Avalonia 的等价绑定在 <c>Bounds.Height</c> 上(WPF 的 ActualHeight 在 Avalonia 不存在)。
    /// mac 版这里用可注入宿主窗口的重载拿真实窗口验证接线;高度为 0 时结果等于 floor。
    /// </summary>
    [Fact]
    public void SmoothScroll_binds_max_height_to_the_host_window()
    {
        OnAvalonia(() =>
        {
            SmoothScroll scroll = Ui.Scroll(new Border(), 0.0);
            var body = new Border { Width = 100.0, Height = 10.0 };
            SPanel panel = Ui.V(0.0, scroll, body);

            using UiHost host = UiHost.Show(panel);
            scroll.BindWindowMaxHeight(host.Window, 0.5, 100.0);
            UiHost.Flush();

            double expected = Math.Max(100.0, host.Window.Bounds.Height * 0.5);
            Assert.Equal(expected, scroll.MaxHeight, 3);
        });
    }

    /// <summary>
    /// 来源:Windows 源 Ui.cs:282-319(VirtualList 的模板 + 裸容器 + 禁用横向滚动条)。
    /// Avalonia 侧的等价物是 FuncDataTemplate / ItemContainerTheme(见 PROGRESS.md 的差异表)。
    /// </summary>
    [Fact]
    public void VirtualList_wires_the_template_and_the_bare_container()
    {
        OnAvalonia(() =>
        {
            ListBox list = Ui.VirtualList<string>(item => Ui.Txt(item), 12.0);
            list.Items.Add("one");
            list.Items.Add("two");

            Assert.Contains("plain-list", list.Classes);
            Assert.False(list.Focusable);
            Assert.IsAssignableFrom<FuncDataTemplate<string>>(list.ItemTemplate);
            Assert.NotNull(list.ItemContainerTheme);
            Assert.Equal(typeof(ListBoxItem), list.ItemContainerTheme!.TargetType);

            using UiHost host = UiHost.Show(list);
            UiHost.Flush();

            // 容器真的被生成出来:模板换成了裸 ContentPresenter + 行间距 12 + 不可聚焦(先快照再断言)。
            var containers = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
            Assert.NotEmpty(containers);
            foreach (ListBoxItem container in containers)
            {
                Assert.Equal(new Thickness(0.0, 0.0, 0.0, 12.0), container.Margin);
                Assert.False(container.Focusable);
            }
        });
    }

    /// <summary>来源:同上的行模板 —— 工厂真的被调用并给出 TextBlock(而不是自证)。</summary>
    [Fact]
    public void VirtualList_row_factory_builds_the_row()
    {
        OnAvalonia(() =>
        {
            ListBox list = Ui.VirtualList<string>(item => Ui.Txt("row:" + item), 8.0);

            Control? row = list.ItemTemplate!.Build("x");

            Assert.Equal("row:x", Assert.IsType<TextBlock>(row).Text);
        });
    }

    /// <summary>来源:Windows 源 Ui.cs:291-293(横向滚动条禁用)。</summary>
    [Fact]
    public void VirtualList_disables_the_horizontal_scrollbar()
    {
        OnAvalonia(() =>
        {
            ListBox list = Ui.VirtualList<string>(item => Ui.Txt(item));

            Assert.Equal(
                Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                ScrollViewer.GetHorizontalScrollBarVisibility(list));
        });
    }

    // ---------------------------------------------------------------- UiExt

    /// <summary>来源:Windows 源 UiExt.cs:10-65(全部链式 helper 返回自身,便于连写)。</summary>
    [Fact]
    public void UiExt_fluent_helpers_return_the_same_instance()
    {
        OnAvalonia(() =>
        {
            var border = new Border();

            Assert.Same(border, border.M(1.0));
            Assert.Same(border, border.M(1.0, 2.0, 3.0, 4.0));
            Assert.Same(border, border.W(10.0));
            Assert.Same(border, border.MinW(5.0));
            Assert.Same(border, border.Hh(7.0));
            Assert.Same(border, border.At(0));
            Assert.Same(border, border.Left());
            Assert.Same(border, border.Right());
            Assert.Same(border, border.Center());
            Assert.Same(border, border.Stretch());
            Assert.Same(border, border.VCenter());
            Assert.Same(border, border.VTop());
            Assert.Same(border, border.Tag2("t"));
        });
    }

    /// <summary>来源:Windows 源 UiExt.cs:16-44(两种 M 重载与 W / MinW / Hh 直接写属性)。</summary>
    [Fact]
    public void UiExt_sets_margin_width_and_height()
    {
        OnAvalonia(() =>
        {
            var border = new Border().M(1.0, 2.0, 3.0, 4.0).W(11.0).MinW(5.0).Hh(9.0);
            Assert.Equal(new Thickness(1.0, 2.0, 3.0, 4.0), border.Margin);
            Assert.Equal(11.0, border.Width);
            Assert.Equal(5.0, border.MinWidth);
            Assert.Equal(9.0, border.Height);

            Assert.Equal(new Thickness(6.0), new Border().M(6.0).Margin);
        });
    }

    /// <summary>来源:Windows 源 UiExt.cs:68-102(六个对齐 helper)。</summary>
    [Fact]
    public void UiExt_sets_alignment()
    {
        OnAvalonia(() =>
        {
            Assert.Equal(HorizontalAlignment.Left, new Border().Left().HorizontalAlignment);
            Assert.Equal(HorizontalAlignment.Right, new Border().Right().HorizontalAlignment);
            Assert.Equal(HorizontalAlignment.Center, new Border().Center().HorizontalAlignment);
            Assert.Equal(HorizontalAlignment.Stretch, new Border().Stretch().HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Center, new Border().VCenter().VerticalAlignment);
            Assert.Equal(VerticalAlignment.Top, new Border().VTop().VerticalAlignment);
        });
    }

    /// <summary>来源:Windows 源 UiExt.cs:104-126(Wrap / Trim / Tip / Tag2)。</summary>
    [Fact]
    public void UiExt_wraps_trims_tips_and_tags()
    {
        OnAvalonia(() =>
        {
            TextBlock block = Ui.Muted("x").Wrap().Trim();
            Assert.Equal(TextWrapping.Wrap, block.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, block.TextTrimming);

            Border border = new Border().Tip("hint").Tag2(42);
            Assert.Equal("hint", ToolTip.GetTip(border));
            Assert.Equal(42, border.Tag);
        });
    }

    /// <summary>
    /// 来源:Windows 源 UiExt.cs:128-143(Hide / Show / Shown)。
    /// mac 侧 <c>Visibility.Collapsed</c> 就是 <c>IsVisible = false</c>(见差异表)。
    /// </summary>
    [Fact]
    public void UiExt_hides_and_shows()
    {
        OnAvalonia(() =>
        {
            var border = new Border();

            Assert.True(border.Shown());
            Assert.False(border.Hide().Shown());
            Assert.False(border.Show(false).IsVisible);
            Assert.True(border.Show().Shown());
            Assert.True(border.Hide(false).IsVisible);
        });
    }

    /// <summary>来源:Windows 源 UiExt.cs:145-169(Str / Fill)。</summary>
    [Fact]
    public void UiExt_str_and_fill()
    {
        OnAvalonia(() =>
        {
            ComboBox combo = Ui.Combo(new[] { "a", "b", "c" }, "b");

            Assert.Equal("b", combo.Str());

            combo.Fill(new[] { "x", "y" }, "y");
            Assert.Equal(new[] { "x", "y" }, combo.Items.Cast<string>().ToArray());
            Assert.Equal("y", combo.SelectedItem);

            combo.Fill(new[] { "x", "y" });
            Assert.Equal("y", combo.SelectedItem);

            combo.Fill(new[] { "p", "q" });
            Assert.Equal("p", combo.SelectedItem);

            combo.Fill(Array.Empty<string>());
            Assert.Empty(combo.Items);
            Assert.Equal("", combo.Str());
        });
    }

    /// <summary>
    /// 来源:Windows 源 UiExt.cs:150-154 的注释(items 可能是 c.Items 的活视图,必须先快照再 Clear)。
    /// </summary>
    [Fact]
    public void UiExt_fill_snapshots_a_live_view()
    {
        OnAvalonia(() =>
        {
            ComboBox combo = Ui.Combo(new[] { "a", "b", "c" }, "b");

            combo.Fill(combo.Items.Cast<string>(), "b");

            Assert.Equal(new[] { "a", "b", "c" }, combo.Items.Cast<string>().ToArray());
            Assert.Equal("b", combo.SelectedItem);
        });
    }

    /// <summary>来源:Windows 源 UiExt.cs:10-14(Dyn = SetResourceReference,动态资源)。</summary>
    [Fact]
    public void UiExt_dyn_binds_a_dynamic_resource()
    {
        OnAvalonia(() =>
        {
            TextBlock block = new TextBlock();
            Assert.Same(block, block.Dyn(TextBlock.ForegroundProperty, "B.Danger"));

            using UiHost host = UiHost.Show(block);
            Assert.Equal(Palette.Color("B.Danger"), C1cExpected.ColourOf(block.Foreground));
        });
    }
}
