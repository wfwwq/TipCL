using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using TipCL;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C2c1:<c>AppServices.ToastSink</c> 的接线 —— Toast 出现在窗口的浮层里、没有窗口时不抛、
/// 全局静态在 finally 里还原(D-95 要求 C2c 把这条接上;C2a 的遗留清单里它是最后一处)。
/// </summary>
public class C2c1WiringTests
{
    /// <summary>【任务书】外壳构造后 ToastSink 指向外壳的 Toast,<c>AppServices.Toast</c> 能在浮层里出卡片。</summary>
    [Fact]
    public void AppServices_Toast_lands_in_the_shell_overlay()
    {
        AvaloniaSession.Run(() =>
        {
            using ShellFixture shell = ShellFixture.Create();

            Assert.NotNull(AppServices.ToastSink);
            Assert.Empty(shell.Window.ToastHostPanel.Children);

            AppServices.Toast("标题", "正文", ToastKind.Warning);

            ToastCard card = Assert.IsType<ToastCard>(Assert.Single(shell.Window.ToastHostPanel.Children));
            Assert.Equal("标题", card.Title);
            Assert.Equal("正文", card.Body);
            Assert.Equal(ToastKind.Warning, card.Kind);
            Assert.Equal("B.Warn", card.Tone);
            Assert.Single(shell.Window.Toasts.Cards);
            // 测试进程里没有时钟(与 --smoke 一样),默认计时器**不挂**(见 D-105 与
            // C2c1ToastTests.The_default_timer_arms_nothing_without_a_clock);4 秒/7 秒那条
            // 由注入的假计时器在 C2c1ToastTests 里确定性验过。
            Assert.False(Motion.HasClock);
            Assert.Equal(0, shell.Window.Toasts.LiveTimerCount);
        });
    }

    /// <summary>【Windows 源】默认 kind 是 Info:<c>AppServices.Toast("标题")</c> 与 Windows 的默认参数一致。</summary>
    [Fact]
    public void AppServices_Toast_keeps_the_windows_default_arguments()
    {
        AvaloniaSession.Run(() =>
        {
            using ShellFixture shell = ShellFixture.Create();

            AppServices.Toast("只有标题");

            ToastCard card = Assert.IsType<ToastCard>(Assert.Single(shell.Window.ToastHostPanel.Children));
            Assert.Equal("", card.Body);
            Assert.Null(card.BodyBlock);
            Assert.Equal(ToastKind.Info, card.Kind);
        });
    }

    /// <summary>【任务书】多次 Toast 在外壳浮层里按顺序堆叠(上限 4 张仍在)。</summary>
    [Fact]
    public void Multiple_toasts_stack_in_the_shell_overlay()
    {
        AvaloniaSession.Run(() =>
        {
            using ShellFixture shell = ShellFixture.Create();

            for (int i = 0; i < 6; i++)
            {
                AppServices.Toast("第 " + i + " 张");
            }

            List<ToastCard> cards = shell.Window.ToastHostPanel.Children.OfType<ToastCard>().ToList();
            Assert.Equal(C2c1Expected.MaxCards, cards.Count);
            Assert.Equal(new[] { "第 2 张", "第 3 张", "第 4 张", "第 5 张" }, cards.Select(c => c.Title).ToArray());
        });
    }

    /// <summary>【任务书】没有外壳(没有窗口)时 <c>AppServices.Toast</c> 不抛、不改状态。</summary>
    [Fact]
    public void AppServices_Toast_without_a_window_does_not_throw()
    {
        AvaloniaSession.Run(() =>
        {
            Action<string, string, ToastKind>? previous = AppServices.ToastSink;
            try
            {
                AppServices.ToastSink = null;

                AppServices.Toast("没有宿主", "只写调试输出", ToastKind.Error); // 不抛就算过
            }
            finally
            {
                AppServices.ToastSink = previous;
            }
        });
    }

    /// <summary>
    /// 【任务书】外壳关掉之后 ToastSink 还指着它时也不能抛(前后台事件可能在关窗后到达),
    /// 而且不会再有计时器留下来 —— 关窗时 <c>MainWindow</c> 已经把 Toast 收摊了。
    /// </summary>
    [Fact]
    public void Toasting_after_the_window_closed_does_not_throw_and_leaves_no_timer()
    {
        AvaloniaSession.Run(() =>
        {
            ShellFixture shell = ShellFixture.Create();
            MainWindow window = shell.Window;
            AppServices.Toast("关窗前的");
            Assert.Single(window.ToastHostPanel.Children);

            shell.Dispose(); // 关窗(Closed → ToastLayer.Dispose)

            Assert.Equal(0, window.Toasts.LiveTimerCount);
            Assert.Empty(window.ToastHostPanel.Children);

            AppServices.Toast("关窗后的", "不该抛", ToastKind.Error); // 不抛就算过
            Assert.Empty(window.ToastHostPanel.Children);
        });
    }

    /// <summary>
    /// 【Windows 源】<c>MainWindow.xaml.cs:1181-1188</c>:非 UI 线程调 <c>Toast</c> 先投回 UI 线程。
    /// 这里从一个普通线程调用,再让 Dispatcher 把排队的活儿跑掉(不模拟输入设备,只走事件投递)。
    /// </summary>
    [Fact]
    public void Toast_from_another_thread_is_marshalled_onto_the_ui_thread()
    {
        AvaloniaSession.Run(() =>
        {
            using ShellFixture shell = ShellFixture.Create();

            Thread worker = new Thread(() => shell.Window.Toast("跨线程", "正文", ToastKind.Success));
            worker.IsBackground = true;
            worker.Start();
            Assert.True(worker.Join(TimeSpan.FromSeconds(10.0)), "工作线程没有在 10 秒内返回");

            // 投递到 UI 线程的那一份还没跑:先确认"跨线程调用不当场改可视树",再把它跑掉
            for (int i = 0; i < 200 && shell.Window.ToastHostPanel.Children.Count == 0; i++)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }

            ToastCard card = Assert.IsType<ToastCard>(Assert.Single(shell.Window.ToastHostPanel.Children));
            Assert.Equal("跨线程", card.Title);
            Assert.Equal(ToastKind.Success, card.Kind);
        });
    }

    /// <summary>【任务书】ToastSink 在用例之间被还原/清掉,后面的用例看不到前面留下的宿主。</summary>
    [Fact]
    public void The_toast_sink_is_restored_when_the_shell_goes_away()
    {
        AvaloniaSession.Run(() =>
        {
            Action<string, string, ToastKind>? before = AppServices.ToastSink;
            MainWindow? window;
            using (ShellFixture shell = ShellFixture.Create())
            {
                window = shell.Window;
                Assert.Same(window, AppServices.Window);
                Assert.NotNull(AppServices.ToastSink);
                AppServices.Toast("临时");
                Assert.Single(window.ToastHostPanel.Children);
            }

            Assert.Equal(before, AppServices.ToastSink);
            // 还原之后 AppServices.Toast 不再往那个外壳里塞东西
            AppServices.Toast("还原之后");
            Assert.Empty(window!.ToastHostPanel.Children);
        });
    }

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1179</c>:<c>Toast</c> 的默认参数(body 空、kind Info)。</summary>
    [Fact]
    public void Window_toast_defaults_match_the_windows_signature()
    {
        AvaloniaSession.Run(() =>
        {
            using ShellFixture shell = ShellFixture.Create();

            shell.Window.Toast("只有标题");
            ToastCard card = Assert.IsType<ToastCard>(Assert.Single(shell.Window.ToastHostPanel.Children));

            Assert.Equal("", card.Body);
            Assert.Equal(ToastKind.Info, card.Kind);
        });
    }

    /// <summary>【任务书】点一下外壳里的卡片就能把它撤掉(手动关闭这一条在真宿主上也成立)。</summary>
    [Fact]
    public void A_toast_in_the_shell_can_be_dismissed_by_clicking_it()
    {
        AvaloniaSession.Run(() =>
        {
            using ShellFixture shell = ShellFixture.Create();
            shell.Window.Toast("一");
            shell.Window.Toast("二");

            C2c1Input.Click(shell.Window.ToastHostPanel.Children.OfType<ToastCard>().First());

            List<ToastCard> left = shell.Window.ToastHostPanel.Children.OfType<ToastCard>().ToList();
            Assert.Single(left);
            Assert.Equal("二", left[0].Title);
            Assert.Single(shell.Window.Toasts.Cards);
            Assert.Equal(0, shell.Window.Toasts.LiveTimerCount); // 无时钟:默认计时器本来就没挂
        });
    }

    /// <summary>【任务书】<c>ClearToasts</c> 一次收干净(窗口关闭与外壳动作都用它)。</summary>
    [Fact]
    public void ClearToasts_removes_every_card_and_timer()
    {
        AvaloniaSession.Run(() =>
        {
            using ShellFixture shell = ShellFixture.Create();
            shell.Window.Toast("一");
            shell.Window.Toast("二", "正文", ToastKind.Error);
            Assert.Equal(2, shell.Window.Toasts.Cards.Count);

            shell.Window.ClearToasts();

            Assert.Empty(shell.Window.ToastHostPanel.Children);
            Assert.Empty(shell.Window.Toasts.Cards);
            Assert.Equal(0, shell.Window.Toasts.LiveTimerCount);
        });
    }

    /// <summary>
    /// 【任务书】<c>Dlg.Form</c> 的「填写有误」提示(C2a 验收里点名的"听不见"那一处)现在能出声了。
    /// </summary>
    [Fact]
    public void The_form_validation_toast_is_audible_now()
    {
        AvaloniaSession.Run(() =>
        {
            using ShellFixture shell = ShellFixture.Create();
            using DlgHarness dlg = DlgHarness.Show();

            Task<string?> task = Dlg.Form<string>("标题", new TextBlock { Text = "正文" }, () => throw new InvalidOperationException("名称不能为空"));
            UiHost.Flush();

            C2c1Input.Press(DlgHarness.ButtonByText(dlg.Host, I18n.L("保存")));
            // 【C7a 追加修法 · D-152/D-151 同一族】原来是 `UiHost.Flush();` 固定刷一次;
            // 并发负载下那一次不够 ⇒ 下面 `Assert.Single(ToastHostPanel.Children)` 会拿到空集合
            // (实测只在 VSTest 通道红过、进程内通道同轮绿)。改成"**等效果 + 9000 截止**":
            // C3bFiles.Pump 内部就是"条件不成立就 UiHost.Flush() + 2ms、带截止"。**断言本身一字不改。**
            C3bFiles.Pump(() => shell.Window.ToastHostPanel.Children.Count >= 1, 9000);

            // 卡片没关(表单还在),浮层里多了一张「填写有误」的 Toast
            Assert.False(task.IsCompleted);
            Assert.Single(dlg.Host.Children);
            ToastCard card = Assert.IsType<ToastCard>(Assert.Single(shell.Window.ToastHostPanel.Children));
            Assert.Equal(I18n.L("填写有误"), card.Title);
            Assert.Equal("名称不能为空", card.Body);
            Assert.Equal(ToastKind.Warning, card.Kind);
            Assert.Equal(TimeSpan.FromSeconds(4.0), ToastCard.AutoCloseDelay(card.Kind));
        });
    }
}
