using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using TipCL.Services;
using TipCL.Themes;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C2c1:对话框卡片(<see cref="DialogCard"/>)与它承载的 confirm / message 卡片 ——
/// 标题、正文、按钮组、几何取色,以及"点确定 / 点取消 / 关掉(遮罩或 Esc)"分别把等待中的
/// <see cref="Dlg"/> 调用解析成什么结果。
///
/// 期望值来源:Windows 版 <c>Services\Dlg.cs</c> 的 <c>Shell</c>(185-213)与 <c>Ask</c>(220-243)、
/// <c>Alert</c>(245-272)、<c>Push</c>(83-181)里那几条分支 —— 每条都注明在用例上。
/// 纪律:一切 Avalonia 工作经 <c>AvaloniaSession.Run</c>(D-79),事件程序化触发,宿主在 finally 里还原。
/// </summary>
public class C2c1DialogCardTests
{
    /// <summary>【Windows 源】<c>Dlg.cs:202-211</c>:卡片的圆角/内边距/描边/宽度夹取/取色。</summary>
    [Fact]
    public void The_dialog_card_geometry_matches_the_windows_shell()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<bool> task = Dlg.Confirm("标题", "正文");
            UiHost.Flush();

            DialogCard card = Assert.IsType<DialogCard>(DlgHarness.Descendants(env.Host).OfType<DialogCard>().Single());

            Assert.Equal(new CornerRadius(C2c1Expected.DialogCornerRadius), card.CornerRadius);
            Assert.Equal(C2c1Expected.DialogPaddingValue, card.Padding);
            Assert.Equal(new Thickness(1.0), card.BorderThickness);
            Assert.Equal(C2c1Expected.ConfirmWidth, card.MaxWidth);
            Assert.Equal(C2c1Expected.MinWidthFloor, card.MinWidth);
            Assert.Equal(Palette.Color(C2c1Expected.DialogBackground), C1cExpected.ColourOf(card.Background));
            Assert.Equal(Palette.Color(C2c1Expected.DialogBorderBrush), C1cExpected.ColourOf(card.BorderBrush));
            // 三行布局:标题 / 正文 / 按钮行
            Assert.Equal(3, card.Layout.RowDefinitions.Count);
            Assert.Same(card.TitleBlock, card.Layout.Children[0]);
            Assert.Same(card.BodyScroll, card.Layout.Children[1]);
            Assert.Same(card.FooterRow, card.Layout.Children[2]);

            DlgHarness.Click(DlgHarness.ButtonByText(env.Host, I18n.L("确定")));
            Assert.True(task.Result);
        });
    }

    /// <summary>【Windows 源】<c>Dlg.cs:206-207</c>:<c>MinWidth = Math.Min(width, 380)</c> —— 比 380 窄时取 width 本身。</summary>
    [Fact]
    public void A_narrow_dialog_card_never_gets_a_min_width_above_its_width()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<bool> wide = Dlg.Ask("标题", new TextBlock { Text = "正文" }, width: 520.0);
            DialogCard card = Assert.IsType<DialogCard>(DlgHarness.Descendants(env.Host).OfType<DialogCard>().Single());
            Assert.Equal(520.0, card.MaxWidth);
            Assert.Equal(380.0, card.MinWidth);

            Task<bool> narrow = Dlg.Ask("窄的", new TextBlock { Text = "正文" }, width: 300.0);
            DialogCard narrowCard = DlgHarness.Descendants(env.Host).OfType<DialogCard>().Last();
            Assert.Equal(300.0, narrowCard.MaxWidth);
            Assert.Equal(300.0, narrowCard.MinWidth);

            // 两张都收干净(各自点自己的取消按钮,按顺序确定)
            C2c1Input.Press(DlgHarness.Buttons(card).First(b => (string?)b.Content == I18n.L("取消")));
            C2c1Input.Press(DlgHarness.Buttons(narrowCard).First(b => (string?)b.Content == I18n.L("取消")));
            Assert.False(wide.Result);
            Assert.False(narrow.Result);
            Assert.False(Dlg.AnyOpen);
        });
    }

    /// <summary>【Windows 源】<c>Dlg.cs:187-196</c>:标题是 H2、正文在滚动容器里(超出时自己滚)。</summary>
    [Fact]
    public void The_dialog_card_shows_the_title_and_body_it_was_given()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Dlg.Confirm("真的要删吗", "删了就没有了");
            UiHost.Flush();

            DialogCard card = Assert.IsType<DialogCard>(DlgHarness.Descendants(env.Host).OfType<DialogCard>().Single());

            Assert.Equal("真的要删吗", card.Title);
            Assert.Equal("真的要删吗", card.TitleBlock.Text);
            Assert.Contains("t-h2", card.TitleBlock.Classes); // Ui.H2 的样式类(C1b 的 T.H2 -> t-h2)
            Assert.Equal(ScrollBarVisibility.Auto, card.BodyScroll.VerticalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Disabled, card.BodyScroll.HorizontalScrollBarVisibility);
            Assert.Equal(C2c1Expected.SectionGap, card.BodyScroll.Margin.Top);
            Assert.Equal(C2c1Expected.SectionGap, card.FooterRow!.Margin.Top);
            // 正文是调用方给的那个控件:Confirm 给的是包了 t-muted 的换行文本
            TextBlock body = Assert.IsType<TextBlock>(card.BodyControl);
            Assert.Equal("删了就没有了", body.Text);
            Assert.Contains("t-muted", body.Classes);
        });
    }

    /// <summary>【Windows 源】<c>Dlg.cs:225</c>:确认卡片的按钮组 = 取消(普通)+ 确定(主按钮),取消在前。</summary>
    [Fact]
    public void The_confirm_card_shows_cancel_then_ok_on_the_right()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Dlg.Confirm("标题", "正文", "删掉", "算了", danger: true);
            UiHost.Flush();

            DialogCard card = Assert.IsType<DialogCard>(DlgHarness.Descendants(env.Host).OfType<DialogCard>().Single());
            List<Button> buttons = DlgHarness.Buttons(card);

            Assert.Equal(2, buttons.Count);
            Assert.Equal("算了", buttons[0].Content);
            Assert.Equal("删掉", buttons[1].Content);
            // 危险分支:确定用 Danger 变体(btn-danger),取消保持普通
            Assert.Contains("btn-danger", buttons[1].Classes);
            Assert.DoesNotContain("btn-primary", buttons[1].Classes);
            Assert.Equal(8.0, ((SPanel)card.FooterRow!).Spacing);
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, card.FooterRow.HorizontalAlignment);
        });
    }

    /// <summary>【Windows 源】<c>Dlg.cs:223</c>:不危险时确定是主按钮(btn-primary),取消是普通按钮。</summary>
    [Fact]
    public void A_plain_confirm_uses_the_primary_ok_button()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Dlg.Confirm("标题", "正文");
            UiHost.Flush();

            DialogCard card = Assert.IsType<DialogCard>(DlgHarness.Descendants(env.Host).OfType<DialogCard>().Single());
            List<Button> buttons = DlgHarness.Buttons(card);

            Assert.Contains("btn-primary", buttons[1].Classes);
            Assert.DoesNotContain("btn-danger", buttons[1].Classes);
            Assert.DoesNotContain("btn-primary", buttons[0].Classes);
        });
    }

    /// <summary>【Windows 源】<c>Dlg.cs:232-236</c>:点「确定」把等待中的调用解析成 true 并关掉浮层。</summary>
    [Fact]
    public void The_ok_button_resolves_the_confirm_with_true()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<bool> task = Dlg.Confirm("标题", "正文");
            Assert.True(Dlg.AnyOpen);
            Assert.False(task.IsCompleted);

            C2c1Input.Press(DlgHarness.ButtonByText(env.Host, I18n.L("确定")));

            Assert.True(task.IsCompleted);
            Assert.True(task.Result);
            Assert.False(Dlg.AnyOpen);
            Assert.Empty(env.Host.Children);
            Assert.False(env.Host.IsVisible);
        });
    }

    /// <summary>【Windows 源】<c>Dlg.cs:237-241</c>:点「取消」解析成 false 并关掉浮层。</summary>
    [Fact]
    public void The_cancel_button_resolves_the_confirm_with_false()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<bool> task = Dlg.Confirm("标题", "正文");

            C2c1Input.Press(DlgHarness.ButtonByText(env.Host, I18n.L("取消")));

            Assert.True(task.IsCompleted);
            Assert.False(task.Result);
            Assert.False(Dlg.AnyOpen);
            Assert.Empty(env.Host.Children);
        });
    }

    /// <summary>
    /// 【Windows 源】<c>Dlg.cs:126-147</c>:Esc 走 <c>onDismiss</c> —— confirm 的 onDismiss 就是"当成取消"
    /// (<c>tcs.TrySetResult(false)</c> + 关掉浮层)。
    /// </summary>
    [Fact]
    public void Escape_behaves_like_cancel()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<bool> task = Dlg.Confirm("标题", "正文");
            UiHost.Flush();

            C2c1Input.Escape(env.Host.Children.OfType<Grid>().Single());

            Assert.True(task.IsCompleted);
            Assert.False(task.Result);
            Assert.False(Dlg.AnyOpen);
            Assert.Empty(env.Host.Children);
        });
    }

    /// <summary>【Windows 源】<c>Dlg.cs:128-137</c>:点遮罩(左键)也走 <c>onDismiss</c> ⇒ confirm 当取消。</summary>
    [Fact]
    public void Clicking_the_mask_behaves_like_cancel()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<bool> task = Dlg.Confirm("标题", "正文");
            UiHost.Flush();

            Border mask = Assert.IsType<Border>(env.Host.Children.OfType<Grid>().Single().Children[0]);
            C2c1Input.PressMask(mask);

            Assert.True(task.IsCompleted);
            Assert.False(task.Result);
            Assert.False(Dlg.AnyOpen);
        });
    }

    /// <summary>【Windows 源】<c>Dlg.cs:262-265</c>:Alert 只有一个「知道了」,关掉它解析成 true。</summary>
    [Fact]
    public void The_message_card_shows_one_button_and_dismissing_it_resolves_true()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task task = Dlg.Alert("出错了", "详情在这里");
            UiHost.Flush();

            DialogCard card = Assert.IsType<DialogCard>(DlgHarness.Descendants(env.Host).OfType<DialogCard>().Single());
            Assert.Single(DlgHarness.Buttons(card));
            Assert.Equal(I18n.L("知道了"), DlgHarness.Buttons(card)[0].Content);
            Assert.Equal("详情在这里", Assert.IsType<TextBox>(card.BodyControl).Text);

            C2c1Input.Press(DlgHarness.Buttons(card)[0]);

            Assert.True(task.IsCompleted);
            Assert.False(Dlg.AnyOpen);
        });
    }

    /// <summary>【Windows 源】<c>Dlg.cs:262-265</c>:Alert 的 onDismiss 也是 true(关掉就算知道了)。</summary>
    [Fact]
    public void Dismissing_the_message_card_resolves_true()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task task = Dlg.Alert("出错了", "详情");
            UiHost.Flush();

            C2c1Input.Escape(env.Host.Children.OfType<Grid>().Single());

            Assert.True(task.IsCompleted);
            Assert.False(Dlg.AnyOpen);
        });
    }

    /// <summary>【Windows 源】<c>Dlg.cs:281-322</c>:Prompt 的确定返回去空格后的文本,取消返回 null。</summary>
    [Fact]
    public void The_prompt_card_returns_the_typed_text_or_null()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<string?> task = Dlg.Prompt("标题", "标签", "初始值");
            UiHost.Flush();

            DialogCard card = Assert.IsType<DialogCard>(DlgHarness.Descendants(env.Host).OfType<DialogCard>().Single());
            TextBox input = DlgHarness.Descendants(card).OfType<TextBox>().First();
            Assert.Equal("初始值", input.Text);

            input.Text = "  新值  ";
            C2c1Input.Press(DlgHarness.ButtonByText(env.Host, I18n.L("确定")));

            Assert.True(task.IsCompleted);
            Assert.Equal("新值", task.Result);

            // 第二次:确定不填(测试里只验取消那一路的返回值)
            Task<string?> cancelled = Dlg.Prompt("标题", "标签");
            UiHost.Flush();
            C2c1Input.Press(DlgHarness.ButtonByText(env.Host, I18n.L("取消")));
            Assert.Null(cancelled.Result);
        });
    }

    /// <summary>【Windows 源】<c>Dlg.cs:335-345</c>:Choose 每个选项一个按钮(最后一个主按钮),取消返回 -1。</summary>
    [Fact]
    public void The_choose_card_lists_every_option_and_dismissal_is_minus_one()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<int> task = Dlg.Choose("标题", new TextBlock { Text = "正文" }, new[] { "甲", "乙", "丙" });
            UiHost.Flush();

            DialogCard card = Assert.IsType<DialogCard>(DlgHarness.Descendants(env.Host).OfType<DialogCard>().Single());
            List<Button> buttons = DlgHarness.Buttons(card);
            Assert.Equal(new[] { "甲", "乙", "丙" }, buttons.Select(b => b.Content).ToArray());
            Assert.Contains("btn-primary", buttons[2].Classes);

            C2c1Input.Press(buttons[1]);
            Assert.Equal(1, task.Result);

            Task<int> dismissed = Dlg.Choose("标题", new TextBlock { Text = "正文" }, new[] { "甲", "乙" });
            UiHost.Flush();
            C2c1Input.Escape(env.Host.Children.OfType<Grid>().Last());
            Assert.Equal(-1, dismissed.Result);
        });
    }

    /// <summary>【Windows 源】<c>Dlg.cs:346-382</c>:Form 的确定把 collect() 的结果带回来。</summary>
    [Fact]
    public void The_form_card_returns_what_collect_produced()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<string?> task = Dlg.Form<string>("标题", new TextBlock { Text = "正文" }, () => "收集到的值");

            C2c1Input.Press(DlgHarness.ButtonByText(env.Host, I18n.L("保存")));

            Assert.True(task.IsCompleted);
            Assert.Equal("收集到的值", task.Result);
            Assert.False(Dlg.AnyOpen);
        });
    }

    /// <summary>【Windows 源】<c>Dlg.cs:367-371</c>:collect 抛异常时卡片不关、Toast 提示「填写有误」。</summary>
    [Fact]
    public void The_form_card_keeps_open_when_collect_throws()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<string?> task = Dlg.Form<string>("标题", new TextBlock { Text = "正文" }, () => throw new InvalidOperationException("数字不合法"));
            UiHost.Flush();

            C2c1Input.Press(DlgHarness.ButtonByText(env.Host, I18n.L("保存")));

            Assert.False(task.IsCompleted);
            Assert.True(Dlg.AnyOpen);
            Assert.Single(env.Host.Children);
            // 卡片被 Motion.Shake 抖了一下(测试进程里动画是关的,所以视为"落终态",这里只验它还在)
            Assert.NotNull(DlgHarness.Descendants(env.Host).OfType<DialogCard>().SingleOrDefault());
        });
    }

    /// <summary>
    /// 【Windows 源】<c>Dlg.cs:169-187</c> 的 <c>Layer.Card</c> 指回它刚建的那张卡片;
    /// <c>Panel</c> 用的仍是它自己那块内联 Border(**不是** <see cref="DialogCard"/>,Windows 也是这么写的)。
    /// </summary>
    [Fact]
    public void The_layer_points_at_the_very_card_it_created()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Dlg.Layer layer = Dlg.Panel("标题", new TextBlock { Text = "正文" });
            UiHost.Flush();

            Assert.IsType<Border>(layer.Card);
            Assert.IsNotType<DialogCard>(layer.Card);
            Assert.Single(DlgHarness.Buttons(layer.Card)); // Panel 的关闭按钮
            Assert.True(Dlg.AnyOpen);
            layer.Close();
            Assert.False(Dlg.AnyOpen);
        });
    }

    /// <summary>【任务书】对话框收摊后不留挂着的动画:<c>Motion.Enabled == false</c> 时 PopOut 同步落终态。</summary>
    [Fact]
    public void Closing_a_dialog_leaves_no_pending_animation()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<bool> task = Dlg.Confirm("标题", "正文");
            DialogCard card = Assert.IsType<DialogCard>(DlgHarness.Descendants(env.Host).OfType<DialogCard>().Single());

            Assert.False(Motion.Enabled);
            C2c1Input.Press(DlgHarness.ButtonByText(env.Host, I18n.L("取消")));

            Assert.False(task.Result);
            Assert.Empty(env.Host.Children);
            Assert.False(env.Host.IsVisible);
            Assert.Empty(DlgHarness.Descendants(env.Host).OfType<DialogCard>());
            Assert.DoesNotContain(card, DlgHarness.Descendants(env.Host));
        });
    }

    /// <summary>【Windows 源】<c>Dlg.ConfirmFooter</c> 的默认文案:C2a 移植时就与 <c>I18n.L</c> 的同一个键对拍。</summary>
    [Fact]
    public void The_default_button_labels_come_from_the_i18n_table()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Dlg.Confirm("标题", "正文");
            UiHost.Flush();

            DialogCard.ConfirmButtons buttons = DialogCard.ConfirmFooter(null, null, danger: false);
            Assert.Equal(I18n.L("确定"), buttons.Ok.Content);
            Assert.Equal(I18n.L("取消"), buttons.Cancel.Content);
            Assert.Equal(new[] { I18n.L("取消"), I18n.L("确定") }, buttons.Footer.Children.Select(c => ((Button)c).Content).ToArray());

            C2c1Input.Press(DlgHarness.ButtonByText(env.Host, I18n.L("取消")));
        });
    }

    /// <summary>【Windows 源】<c>Dlg.cs:390-410</c>:Panel 的卡片是另一块内联 Border(没有三行布局,标题旁有关闭按钮)。</summary>
    [Fact]
    public void The_panel_card_differs_from_the_confirm_card()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Dlg.Layer layer = Dlg.Panel("面板标题", new TextBlock { Text = "正文" });
            UiHost.Flush();

            Border card = Assert.IsType<Border>(layer.Card);
            Assert.IsNotType<DialogCard>(card);
            // Panel 用的是自己那块内联卡片:圆角/内边距与确认卡片不同(Windows 也是这么写的)
            Assert.Equal(new Thickness(20.0, 16.0, 20.0, 18.0), card.Padding);
            Assert.NotEqual(C2c1Expected.DialogPaddingValue, card.Padding);
            Assert.Equal(new CornerRadius(10.0), card.CornerRadius);
            Assert.Equal(720.0, card.MaxWidth);
            Assert.Equal(Palette.Color(C2c1Expected.DialogBackground), C1cExpected.ColourOf(card.Background));

            Button close = DlgHarness.Buttons(card).Single();
            Assert.Equal("\ue711", Assert.IsType<TextBlock>(close.Content).Text);

            layer.Close();
        });
    }

    /// <summary>【任务书】卡片被关掉之后 <c>Dlg.AnyOpen</c> 归 false 且遮罩效果撤掉(BlurTarget 还原)。</summary>
    [Fact]
    public void Closing_the_last_dialog_clears_the_blur_and_the_host_state()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Border blurTarget = new Border();
            Dlg.BlurTarget = blurTarget;
            Dlg.Layer first = Dlg.Panel("一", new TextBlock { Text = "正文" });
            Dlg.Layer second = Dlg.Panel("二", new TextBlock { Text = "正文" });

            Assert.NotNull(blurTarget.Effect);
            Assert.Equal(2, env.Host.Children.Count);

            first.Close();
            Assert.NotNull(blurTarget.Effect); // 还有一层开着,模糊不撤
            Assert.True(Dlg.AnyOpen);

            second.Close();
            Assert.Null(blurTarget.Effect);
            Assert.False(Dlg.AnyOpen);
            Assert.False(env.Host.IsVisible);
            Dlg.BlurTarget = null;
        });
    }
}
