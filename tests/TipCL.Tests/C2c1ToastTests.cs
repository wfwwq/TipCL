using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.Services;
using TipCL.Themes;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C2c1:Toast 卡片本身(<see cref="ToastCard"/>)与宿主管理器(<see cref="ToastLayer"/>)——
/// 结构、取色、堆叠、自动关闭、点一下关掉、悬停暂停、关掉动画时的终态、计时器不泄漏。
///
/// 纪律:一切 Avalonia 工作都在 <c>AvaloniaSession.Run</c> 里(D-79);事件用 <c>RaiseEvent</c>
/// 程序化触发(**不模拟输入设备**);自动关闭由 <see cref="FakeToastTimer"/> 确定性驱动
/// (**不用 <c>Thread.Sleep</c>**);宿主窗口、全局开关与计时器都在 <c>using</c>/finally 里收干净。
/// 期望值来自 <see cref="C2c1Expected"/>(每条都注明【Windows 源】/【任务书】)。
/// </summary>
public class C2c1ToastTests
{
    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1194-1200</c>:四种 kind 各自的图标码位与颜色令牌。</summary>
    [Theory]
    [InlineData(ToastKind.Success)]
    [InlineData(ToastKind.Warning)]
    [InlineData(ToastKind.Error)]
    [InlineData(ToastKind.Info)]
    public void Card_takes_the_icon_and_colour_of_its_kind(ToastKind kind)
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();
            ToastCard card = env.Layer.Show("标题", "正文", kind);
            UiHost.Flush();

            (string Glyph, string Tone) expected = (C2c1Expected.KindStyles.Single(entry => entry.Kind == kind).Glyph, C2c1Expected.KindStyles.Single(entry => entry.Kind == kind).Tone);

            Assert.Equal(kind, card.Kind);
            Assert.Equal(expected.Glyph, card.Glyph);
            Assert.Equal(expected.Glyph, card.Icon.Text);
            Assert.Equal(expected.Tone, card.Tone);
            // 取色真的落到了色板的同一个键上(卡片在窗口里,动态资源已求值)
            Assert.Equal(Palette.Color(expected.Tone), C1cExpected.ColourOf(card.AccentBar.Background));
            Assert.Equal(Palette.Color(expected.Tone), C1cExpected.ColourOf(card.Icon.Foreground));
            // 图标用图标字体,不是正文字体
            Assert.Equal(Ui.GlyphFont, card.Icon.FontFamily);
            Assert.Equal(15.0, card.Icon.FontSize);
        });
    }

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1201-1219</c>:卡片的几何与取色逐项对拍。</summary>
    [Fact]
    public void Card_geometry_matches_the_windows_card()
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();
            ToastCard card = env.Layer.Show("标题", "正文", ToastKind.Info);
            UiHost.Flush();

            Assert.Equal(new CornerRadius(C2c1Expected.CardCornerRadius), card.CornerRadius);
            Assert.Equal(C2c1Expected.CardPaddingValue, card.Padding);
            Assert.Equal(new Thickness(1.0), card.BorderThickness);
            Assert.Equal(C2c1Expected.CardMarginValue, card.Margin);
            Assert.Equal(Palette.Color(C2c1Expected.CardBackground), C1cExpected.ColourOf(card.Background));
            Assert.Equal(Palette.Color(C2c1Expected.CardBorderBrush), C1cExpected.ColourOf(card.BorderBrush));
            // 整张卡片可点(Windows 的 Cursors.Hand → mac 的 StandardCursorType.Hand)。
            // Cursor 没有值相等语义,所以按它的标准光标名对拍(Arrow 之类会立刻不同)。
            Assert.NotNull(card.Cursor);
            Assert.Equal(new Cursor(StandardCursorType.Hand).ToString(), card.Cursor!.ToString());
            // 三个孩子:色条、图标、文字列
            Assert.Equal(3, card.Row.Count);
            Assert.Same(card.AccentBar, card.Row[0]);
            Assert.Same(card.Icon, card.Row[1]);
            Assert.Same(card.BodyPanel, card.Row[2]);
            Assert.Equal(C2c1Expected.AccentWidth, card.AccentBar.Width);
            Assert.Equal(new CornerRadius(2.0), card.AccentBar.CornerRadius);
            Assert.Equal(Orientation.Vertical, card.BodyPanel.Orientation); // Ui.V(2.0, 标题, 正文)
            Assert.Equal(C2c1Expected.BodyPanelMaxWidth, card.BodyPanel.MaxWidth);
        });
    }

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1207</c>:标题与正文之间 2px、图标与文字之间 10px。</summary>
    [Fact]
    public void Card_spacings_match_the_windows_card()
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();
            ToastCard card = env.Layer.Show("标题", "正文", ToastKind.Info);

            Assert.Equal(C2c1Expected.TextSpacing, card.BodyPanel.Spacing);
            Assert.Equal(C2c1Expected.RowSpacing, ((SPanel)card.Child!).Spacing);
        });
    }

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1207</c>:标题是 13 号半粗,正文走 <c>t-muted</c> 样式类。</summary>
    [Fact]
    public void Card_shows_the_title_and_body_it_was_given()
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();
            ToastCard card = env.Layer.Show("下载失败", "网络超时", ToastKind.Error);
            UiHost.Flush();

            Assert.Equal("下载失败", card.Title);
            Assert.Equal("下载失败", card.TitleBlock.Text);
            Assert.Equal(13.0, card.TitleBlock.FontSize);
            Assert.Equal(FontWeight.SemiBold, card.TitleBlock.FontWeight);
            Assert.Equal(TextWrapping.Wrap, card.TitleBlock.TextWrapping);
            Assert.Equal("网络超时", card.Body);
            Assert.NotNull(card.BodyBlock);
            Assert.Equal("网络超时", card.BodyShown);
            Assert.Contains("t-muted", card.BodyBlock!.Classes);
        });
    }

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1207</c>:正文空白时**不加**那一行。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Card_omits_the_body_row_when_the_body_is_blank(string body)
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();
            ToastCard card = env.Layer.Show("只有标题", body, ToastKind.Success);

            Assert.Null(card.BodyBlock);
            Assert.Single(card.BodyPanel.Children);
        });
    }

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1207</c>:超过 220 字留前 220 字 + "..."。</summary>
    [Fact]
    public void Card_truncates_a_long_body_at_220_characters()
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();
            string longBody = new string('好', C2c1Expected.BodyMaxLength + 40);
            ToastCard card = env.Layer.Show("标题", longBody, ToastKind.Info);

            Assert.Equal(longBody, card.Body); // 原文不动
            Assert.Equal(C2c1Expected.BodyMaxLength + 3, card.BodyShown.Length);
            Assert.StartsWith(new string('好', C2c1Expected.BodyMaxLength), card.BodyShown, StringComparison.Ordinal);
            Assert.EndsWith("...", card.BodyShown, StringComparison.Ordinal);
        });
    }

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1207</c>:正好 220 字不截断(边界)。</summary>
    [Fact]
    public void Card_keeps_a_body_of_exactly_220_characters_intact()
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();
            string exact = new string('好', C2c1Expected.BodyMaxLength);
            ToastCard card = env.Layer.Show("标题", exact, ToastKind.Info);

            Assert.Equal(exact, card.BodyShown);
            Assert.Equal(C2c1Expected.BodyMaxLength, card.BodyShown.Length);
        });
    }

    /// <summary>【Windows 源】<c>MainWindow.xaml</c>:88 + <c>MainWindow.xaml.cs:1221</c>:卡片进的是右上角宿主。</summary>
    [Fact]
    public void Show_puts_the_card_into_the_top_right_host()
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();
            ToastCard card = env.Layer.Show("标题");

            Assert.Single(env.Host.Children);
            Assert.Same(card, env.Host.Children[0]);
            Assert.True(env.Host.IsVisible);
            Assert.Equal(HorizontalAlignment.Right, env.Host.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Top, env.Host.VerticalAlignment);
            Assert.Equal(C2c1Expected.HostMarginValue, env.Host.Margin);
            // 宿主是竖排堆叠(新卡片接在最下面)
            Assert.Equal(Orientation.Vertical, env.Host.Orientation);
            Assert.Single(env.Layer.Cards);
        });
    }

    /// <summary>【任务书】堆叠顺序:按加入顺序排,间距 = 每张卡片的下边距 8。</summary>
    [Fact]
    public void Cards_stack_in_insertion_order_with_the_windows_spacing()
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();
            List<ToastCard> cards = new List<ToastCard>();
            for (int i = 0; i < 3; i++)
            {
                cards.Add(env.Layer.Show("第 " + i + " 张"));
            }

            Assert.Equal(3, env.Host.Children.Count);
            Assert.Equal(cards, env.Host.Children.OfType<ToastCard>().ToList());
            Assert.Equal(cards, env.Layer.Cards.ToList());
            Assert.All(cards, card => Assert.Equal(C2c1Expected.CardMarginValue, card.Margin));
            Assert.All(cards, card => Assert.Equal(ToastCard.StackSpacing, C2c1Expected.CardMarginValue.Bottom));
        });
    }

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1189</c>:最多 4 张,第 5 张进来先顶掉最老的那张。</summary>
    [Fact]
    public void At_most_four_cards_are_kept_and_the_oldest_is_evicted()
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();
            List<ToastCard> cards = new List<ToastCard>();
            for (int i = 0; i < C2c1Expected.MaxCards; i++)
            {
                cards.Add(env.Layer.Show("第 " + i + " 张"));
            }

            Assert.Equal(C2c1Expected.MaxCards, env.Host.Children.Count);

            ToastCard fifth = env.Layer.Show("第五张");
            Assert.Equal(C2c1Expected.MaxCards, env.Host.Children.Count);
            Assert.DoesNotContain(cards[0], env.Host.Children);
            Assert.Equal(cards.Skip(1).Append(fifth), env.Host.Children.OfType<ToastCard>().ToList());
            Assert.Equal(C2c1Expected.MaxCards, env.Layer.Cards.Count);
        });
    }

    /// <summary>
    /// 【Windows 源 + 本包的收严,见 D-105】顶掉最老那张时它的自动关闭计时器也要停掉
    /// —— Windows 只从宿主里摘孩子,那张卡片的 <c>DispatcherTimer</c> 会一直转到自己到点。
    /// </summary>
    [Fact]
    public void Evicting_the_oldest_card_stops_its_own_timer()
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();
            ToastCard first = env.Layer.Show("第 1 张", kind: ToastKind.Info);
            for (int i = 0; i < C2c1Expected.MaxCards; i++)
            {
                env.Layer.Show("填满 " + i);
            }

            Assert.DoesNotContain(first, env.Layer.Cards);
            Assert.Equal(C2c1Expected.MaxCards, env.Layer.LiveTimerCount);
            Assert.Equal(C2c1Expected.MaxCards, env.Timer.LiveCount);
        });
    }

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1225</c>:Info / Success / Warning 都是 4 秒。</summary>
    [Theory]
    [InlineData(ToastKind.Info)]
    [InlineData(ToastKind.Success)]
    [InlineData(ToastKind.Warning)]
    public void Auto_close_is_scheduled_after_four_seconds(ToastKind kind)
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();
            ToastCard card = env.Layer.Show("标题", kind: kind);

            Assert.Single(env.Timer.All);
            Assert.Equal(C2c1Expected.AutoCloseDelay(kind), env.Timer.Last.Delay);
            Assert.Equal(TimeSpan.FromSeconds(4.0), env.Timer.Last.Delay);
            Assert.Equal(1, env.Layer.LiveTimerCount);
            Assert.False(env.Timer.Last.Stopped);
        });
    }

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1225</c>:错误是 7 秒(留给用户看清)。</summary>
    [Fact]
    public void Auto_close_for_errors_waits_seven_seconds()
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();
            env.Layer.Show("出错了", "详情", ToastKind.Error);

            Assert.Single(env.Timer.All);
            Assert.Equal(TimeSpan.FromSeconds(7.0), env.Timer.Last.Delay);
        });
    }

    /// <summary>【任务书】自动关闭到点:卡片从宿主与活卡片表里都消失,计时器也不再挂着。</summary>
    [Fact]
    public void Firing_the_timer_removes_the_card_and_leaves_no_timer()
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();
            ToastCard card = env.Layer.Show("标题");

            env.Timer.Fire();

            Assert.Empty(env.Layer.Cards);
            Assert.DoesNotContain(card, env.Host.Children);
            Assert.Empty(env.Host.Children);
            Assert.Equal(0, env.Layer.LiveTimerCount);
            Assert.Equal(0, env.Timer.LiveCount);
        });
    }

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1232-1235</c>:点一下关掉;【任务书】只关掉那一张,其余重排。</summary>
    [Fact]
    public void Clicking_a_card_removes_exactly_one_and_reflows_the_rest()
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();
            ToastCard first = env.Layer.Show("第 1 张");
            ToastCard second = env.Layer.Show("第 2 张");
            ToastCard third = env.Layer.Show("第 3 张");

            C2c1Input.Click(second);

            Assert.DoesNotContain(second, env.Layer.Cards);
            Assert.DoesNotContain(second, env.Host.Children);
            Assert.Equal(new[] { first, third }, env.Host.Children.OfType<ToastCard>().ToArray());
            Assert.Equal(2, env.Layer.Cards.Count);
            // 第二张自己的计时器停了,另外两张还挂着
            Assert.Equal(2, env.Layer.LiveTimerCount);
            Assert.Equal(2, env.Timer.LiveCount);
            Assert.True(env.Timer.All[1].Stopped);
            Assert.False(env.Timer.All[0].Stopped);
            Assert.False(env.Timer.All[2].Stopped);
        });
    }

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1232</c>:只有左键才算(右键不关)。</summary>
    [Fact]
    public void Right_click_does_not_close_a_card()
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();
            ToastCard card = env.Layer.Show("标题");

            card.RaiseEvent(C2c1Input.Release(MouseButton.Right, card));

            Assert.Single(env.Layer.Cards);
            Assert.Contains(card, env.Host.Children);
        });
    }

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1236-1243</c>:悬停暂停、移开续上。</summary>
    [Fact]
    public void Hovering_pauses_auto_close_and_leaving_restarts_it()
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();
            ToastCard card = env.Layer.Show("标题", kind: ToastKind.Error);

            C2c1Input.Enter(card);
            Assert.Equal(0, env.Layer.LiveTimerCount);
            Assert.True(env.Timer.Last.Stopped);

            // 暂停期间到点也不该发生什么(那一张票已经停了,不再有点火路径)
            Assert.Equal(0, env.Timer.LiveCount);
            Assert.Contains(card, env.Host.Children);

            C2c1Input.Leave(card);
            Assert.Equal(1, env.Layer.LiveTimerCount);
            Assert.Equal(2, env.Timer.All.Count);
            Assert.Equal(TimeSpan.FromSeconds(7.0), env.Timer.Last.Delay);

            env.Timer.Fire();
            Assert.Empty(env.Layer.Cards);
        });
    }

    /// <summary>【任务书】<c>Motion.Enabled == false</c> 时进场的终态**同步**到达(没有悬着的动画)。</summary>
    [Fact]
    public void With_motion_disabled_the_enter_animation_lands_immediately()
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();

            Assert.False(Motion.Enabled);

            ToastCard card = env.Layer.Show("标题");

            Assert.Equal(1.0, card.Opacity);
            Assert.True(card.IsVisible);
            Assert.Contains(card, env.Host.Children);
        });
    }

    /// <summary>【任务书】<c>Motion.Enabled == false</c> 时关闭也**同步**落终态:卡片当场从宿主里摘掉。</summary>
    [Fact]
    public void With_motion_disabled_close_lands_immediately()
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();
            ToastCard card = env.Layer.Show("标题");

            Assert.True(env.Layer.Close(card));

            Assert.Empty(env.Host.Children);
            Assert.Empty(env.Layer.Cards);
            Assert.Equal(0, env.Layer.LiveTimerCount);
        });
    }

    /// <summary>【任务书】关掉同一张两次:第二次什么都不做(返回 false,不误伤别人)。</summary>
    [Fact]
    public void Closing_the_same_card_twice_is_a_no_op()
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();
            ToastCard card = env.Layer.Show("标题");

            Assert.True(env.Layer.Close(card));
            Assert.False(env.Layer.Close(card));
            Assert.Empty(env.Host.Children);
        });
    }

    /// <summary>【任务书】收摊:<c>Dispose</c> 停掉所有计时器并清空宿主(泄漏断言)。</summary>
    [Fact]
    public void Dispose_stops_every_timer_and_empties_the_host()
    {
        AvaloniaSession.Run(() =>
        {
            ToastHarness env = ToastHarness.Show();
            ToastCard card = env.Layer.Show("标题");
            Assert.Equal(1, env.Layer.LiveTimerCount);

            env.Layer.Dispose();

            Assert.Equal(0, env.Layer.LiveTimerCount);
            Assert.Equal(0, env.Timer.LiveCount);
            Assert.Empty(env.Host.Children);
            Assert.Empty(env.Layer.Cards);
            Assert.True(env.Timer.All.All(ticket => ticket.Stopped));
            // 收摊后再关也不炸
            Assert.False(env.Layer.Close(card));
            env.Dispose();
        });
    }

    /// <summary>
    /// 【本机实测】测试/离屏进程里没有时钟,<c>ToastLayer</c> 的默认计时器**不挂** DispatcherTimer
    /// —— 自动关闭在那里收不到 Tick(与 Windows 离屏一样),但也不留残留计时器(D-105)。
    /// </summary>
    [Fact]
    public void The_default_timer_arms_nothing_without_a_clock()
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();
            ToastLayer layer = new ToastLayer(env.Host);

            ToastCard card = layer.Show("标题");

            Assert.False(Motion.HasClock);
            Assert.Equal(0, layer.LiveTimerCount);
            Assert.Single(env.Host.Children);
            Assert.Same(card, env.Host.Children[0]);
            layer.Dispose();
        });
    }

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1199</c>:默认 kind 是 Info(<c>_ =&gt; ("\ue946", "B.Info")</c>)。</summary>
    [Fact]
    public void The_default_kind_is_info()
    {
        AvaloniaSession.Run(() =>
        {
            using ToastHarness env = ToastHarness.Show();
            ToastCard card = env.Layer.Show("标题");

            Assert.Equal(ToastKind.Info, card.Kind);
            Assert.Equal("\ue946", card.Glyph);
            Assert.Equal("B.Info", card.Tone);
        });
    }
}
