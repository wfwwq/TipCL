using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.App.Shell;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>
/// M4-H R12 缺陷 1 的回归:两个启动提示(首启向导 / 反馈同意门)必须等主窗口**真正可见**之后再弹。
///
/// 真机现象(盒子 main,X11):每次启动都 `[WARN] 启动提示失败: Cannot show window with non-visible
/// owner.`,首启同意门一次都没弹出来。成因是 `App.OnFrameworkInitializationCompleted` 里
/// `desktop.MainWindow = win` 之后窗口还没 `Show()`, `FeedbackConsent.MaybeAskAsync(this)` 就把这个
/// 尚不可见的 MainWindow 当 owner,Avalonia 的 `ShowDialog` 直接抛,异常被吞成 WARN。
///
/// 这里验的是生产路径用的那段判定(`MainWindow.RunWhenVisibleAsync`,由 `RunStartupPromptsAsync`
/// 调用):窗口没 Show 时提示一次都不跑,Show 之后才跑;窗口永不显示时在等待上限内收摊。
/// 用计数器代替真提示 —— 真提示会写 config.json 的 first_run / feedback_consent,污染其它用例。
/// </summary>
public class StartupPromptVisibilityTests
{
    /// <summary>未 Show 的窗口 IsVisible 就是 false —— 这正是 Avalonia 对对话框 owner 的检查条件。</summary>
    [Fact]
    public void UnshownWindow_IsNotVisible() => TestApp.RunAsync(async () =>
    {
        var win = new Window { Content = new Border(), Width = 300, Height = 200 };
        Assert.False(win.IsVisible);
        win.Close();
        await Task.CompletedTask;
    });

    /// <summary>缺陷现场:主窗口构造好了但还没 Show(生命周期尚未 Show),提示不能跑。</summary>
    [Fact]
    public void Prompts_DoNotRun_BeforeMainWindowIsShown() => TestApp.RunAsync(async () =>
    {
        var win = new MainWindow();
        int runs = 0;
        var gate = MainWindow.RunWhenVisibleAsync(win, 5000, () => { runs++; return Task.CompletedTask; });

        await Task.Delay(200);                       // 够「不等可见就弹」的旧实现跑完
        Assert.False(gate.IsCompleted, "窗口还不可见,启动提示不该已经跑完(旧实现在这里就弹了)");
        Assert.Equal(0, runs);

        win.Show();                                  // 生命周期在 OnFrameworkInitializationCompleted 之后做的事
        Assert.True(await gate);
        Assert.Equal(1, runs);
        Assert.True(win.IsVisible);
        win.Close();
    });

    /// <summary>窗口已经可见时提示立刻跑,不再等。</summary>
    [Fact]
    public void Prompts_RunRightAway_WhenWindowAlreadyVisible() => TestApp.RunAsync(async () =>
    {
        var win = new Window { Content = new Border(), Width = 300, Height = 200 };
        win.Show();
        int runs = 0;
        Assert.True(await MainWindow.RunWhenVisibleAsync(win, 5000, () => { runs++; return Task.CompletedTask; }));
        Assert.Equal(1, runs);
        win.Close();
    });

    /// <summary>窗口永远不显示时在等待上限内收摊:返回 false、不跑提示、不永久挂起。</summary>
    [Fact]
    public void Prompts_GiveUp_WhenWindowNeverBecomesVisible() => TestApp.RunAsync(async () =>
    {
        var win = new Window { Content = new Border(), Width = 300, Height = 200 };
        int runs = 0;
        var started = DateTime.UtcNow;
        Assert.False(await MainWindow.RunWhenVisibleAsync(win, 120, () => { runs++; return Task.CompletedTask; }));
        Assert.Equal(0, runs);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5), "等待上限没生效");
        win.Close();
    });
}
