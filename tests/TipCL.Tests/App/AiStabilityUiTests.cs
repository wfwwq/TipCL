// AI2-stability:DlgCloseTests 偶发 ArgumentNullException 的根治回归。
//
// 基线现象(progress/AI2-stability.md 有复现率):ClickSweepTests 点到 AI 页的建议问题 → 真的向外网发对话请求 →
// 失败事件晚到,在无头会话 Dispatcher.ResetForUnitTests 冲刷旧队列时才执行 → 此刻 Application.Current 为空 →
// AiBubble 构造里 Res.Bind 抛 ArgumentNullException,记在下一个用例(DlgCloseTests)头上。
// 三层修法各有一条用例钉住:
//   1 测试进程不联网:AI 请求在 TestNetworkIsolation 闸门处同步失败(失败事件在发起它的用例里就落地);
//   2 AiUiPost:界面已不在 / 页面挂过的窗口已关 → 晚到的回调丢弃;
//   3 Res.Bind 对空资源宿主防御(不再抛)。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.App.Common;
using TipCL.App.Common.Ai;
using TipCL.App.Pages.Ai;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.App;

[Collection("TipCL 真机交互回归")]
public class AiStabilityUiTests
{
    private static async Task<bool> PumpAsync(Func<bool> ready, int timeoutMs = 5000)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < end)
        {
            if (ready())
            {
                return true;
            }
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        return ready();
    }

    [Fact]
    public void ResBind_WithoutResourceHost_GivesUnsetBinding_InsteadOfThrowing() => TestApp.Run(() =>
    {
        var border = new Border();
        border[!Border.BackgroundProperty] = Res.BindFrom(null, "B.Paper2");
        Assert.Null(border.Background);
        // 有宿主时照旧走资源绑定(不抛);AiBubble 正常构造
        var themed = new Border();
        themed[!Border.BackgroundProperty] = Res.Bind("B.Paper2");
        var win = new Window { Content = themed };
        win.Show();
        try
        {
            Assert.NotNull(new AiBubble("assistant", "ok"));
        }
        finally
        {
            win.Close();
        }
    });

    [Fact]
    public void AiUiPost_RunsWhileHostOpen_DropsAfterHostWindowClosed() => TestApp.RunAsync(async () =>
    {
        var owner = new Border();
        var win = new Window { Width = 200, Height = 100, Content = owner };
        win.Show();
        AiUiPost.Track(owner);
        AiUiPost.Track(owner); // 幂等
        bool ran = false;
        AiUiPost.Post(owner, () => ran = true);
        Assert.True(await PumpAsync(() => ran), "窗口还开着时回调应照常执行");
        Assert.False(AiUiPost.IsUnloaded(owner));

        win.Close();
        Assert.True(await PumpAsync(() => AiUiPost.IsUnloaded(owner)), "窗口关闭后应视为页面已卸载");
        int dropped = AiUiPost.DroppedCount;
        bool late = false;
        AiUiPost.Post(owner, () => late = true);
        await PumpAsync(() => AiUiPost.DroppedCount > dropped, 2000);
        Assert.False(late, "页面挂过的窗口关了,晚到的 Agent 回调不该再碰控件");
        Assert.True(AiUiPost.DroppedCount > dropped);

        // 重新挂到新窗口:恢复正常
        win.Content = null;
        var win2 = new Window { Width = 200, Height = 100, Content = owner };
        win2.Show();
        try
        {
            Assert.True(await PumpAsync(() => !AiUiPost.IsUnloaded(owner)));
            bool again = false;
            AiUiPost.Post(owner, () => again = true);
            Assert.True(await PumpAsync(() => again));
        }
        finally
        {
            win2.Close();
        }
    });

    [Fact]
    public void OutboundAiRequest_IsAnsweredLocally_InTheTestProcess()
    {
        int before = TestNetworkIsolation.BlockedAiRequests;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var session = new AiSession();
        AiResponse r = session.Post("https://ai-gateway.example.invalid/v1/chat/completions", PyDict.Of("Content-Type", "application/json"), PyDict.Of("x", 1L), 15, 15, stream: false);
        Assert.Equal(400, r.StatusCode); // 4xx:AiAgent 不重试,失败事件当场落地
        Assert.True(sw.ElapsedMilliseconds < 3000, "测试闸门应当场给答复,不等网络超时:" + sw.ElapsedMilliseconds + " ms");
        Assert.True(TestNetworkIsolation.BlockedAiRequests > before);
    }

    /// <summary>
    /// 原始现场的直接回放:AI 页点建议问题 → 请求被闸门挡下 → 失败气泡在**本用例内**就出来,不会拖到下一个用例。
    /// </summary>
    [Fact]
    public void ClickingASuggestion_FailsFastAndLandsInsideThisTest() => TestApp.RunAsync(async () =>
    {
        int blocked = TestNetworkIsolation.BlockedAiRequests;
        var page = new AiPage();
        var win = new Window { Width = 900, Height = 700, Content = page };
        win.Show();
        try
        {
            await PumpAsync(() => false, 100);
            Button? chip = page.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "内存设多少合适") || (b.Content as string) == "内存设多少合适");
            Assert.NotNull(chip);
            chip!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, chip));
            bool configured;
            try { configured = AiService.ResolveEndpoint() != null; }
            catch (Exception) { configured = false; }
            if (!configured)
            {
                // 这个构建没有内置网关:点了只提示「还没配置 AI」,本来就不发请求
                Assert.Equal(blocked, TestNetworkIsolation.BlockedAiRequests);
                return;
            }
            // 用户气泡 + 「对话失败」气泡都在本用例里出现(以前失败事件会在别的用例里才回来)
            Assert.True(await PumpAsync(() => page.GetVisualDescendants().OfType<AiBubble>().Count() >= 2, 8000),
                "失败事件没有在本用例内落地");
            Assert.True(TestNetworkIsolation.BlockedAiRequests > blocked, "AI 请求应被测试闸门挡下(不联网)");
        }
        finally
        {
            win.Close();
        }
        await PumpAsync(() => false, 100);
    });
}
