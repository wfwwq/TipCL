// M4-H 回归:对话框「点按钮到底关不关窗」。
//
// 真机现象(Linux/X11):反馈同意框弹出来后,真的用鼠标点「暂不同意 / 同意」窗口不关,调用方
// await 永不返回、配置也写不进去;只有 WM_DELETE_WINDOW 能关掉,关掉后被当成「取消」。
// 根因:`Common/Dlg.cs` 的 Ask/Choose/Prompt 按钮 Click 里只 TrySetResult —— WinForms 参考版
// 由 DialogResult / 图层 Close 顺带完成的关窗,移植时丢了(只有 Panel 保留了 `win.Close()`)。
//
// 本文件把这条语义钉在 dotnet test 里(无头、不弹真窗口,和 InstanceDetailCrashTests 同一种做法):
// 真的开对话框 → 在它的视觉树里按文案找到按钮 → RaiseEvent(Click)(等价真鼠标点击)→
// 断言窗口真的关掉、且 Task 在超时内返回**符合按钮语义**的值。
//
// `SMOKE_PROBE=dialog-close`(Session/SmokeRunner.cs)是同一语义的真机版探针:新机器上装完包
// 跑一条命令就能验,不依赖测试宿主。
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.App.Common;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>
/// 与 <c>InstanceDetailCrashTests</c> 同一个串行集合:<see cref="Dlg.Owner"/> / <c>Dlg.AnyOpen</c>
/// 是进程级静态状态,不能和别的用例并行开对话框。
/// </summary>
[Collection("TipCL 真机交互回归")]
public class DlgCloseTests
{
    /// <summary>宿主窗口 + 还原 Dlg.Owner,收尾时把还开着的对话框关掉(别把模态留给后面的用例)。</summary>
    private sealed class Host : IDisposable
    {
        private readonly Window? _prevOwner;

        public Window Window { get; }

        public Host()
        {
            _prevOwner = Dlg.Owner;
            Window = new Window { Width = 420, Height = 220, Content = new Border() };
            Window.Show();
            Dlg.Owner = Window;
        }

        public void Dispose()
        {
            foreach (var w in Window.OwnedWindows.ToList())
            {
                try { w.Close(); }
                catch (Exception) { }
            }
            try { Window.Close(); }
            catch (Exception) { }
            Dlg.Owner = _prevOwner;
        }
    }

    /// <summary>边等边推渲染 pass:无头会话里不推计时器 / 不跑队列,布局与 await 续体都不会动。</summary>
    private static async Task<bool> PumpAsync(Func<bool> ready, int timeoutMs = 5000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < end)
        {
            if (ready()) return true;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        return ready();
    }

    /// <summary>对话框里按文案找按钮(先视觉树,退逻辑树)。</summary>
    private static Button FindButton(Window dlg, string text)
    {
        var btn = dlg.GetVisualDescendants().OfType<Button>()
                     .Concat(dlg.GetLogicalDescendants().OfType<Button>())
                     .FirstOrDefault(b => b.Content as string == text);
        Assert.NotNull(btn);
        return btn!;
    }

    private static async Task<(Task<T> Task, Window Dialog)> OpenAsync<T>(Host host, Func<Window, Task<T>> open)
    {
        var task = open(host.Window);
        Assert.True(await PumpAsync(() => host.Window.OwnedWindows.Count > 0), "对话框窗口没弹出来");
        return (task, host.Window.OwnedWindows.Last());
    }

    /// <summary>点下去之后:窗口必须关、模态必须退出、Task 必须返回期望值。</summary>
    private static async Task AssertClosedAsync<T>(Task<T> task, Window dlg, T expected)
    {
        Assert.True(await PumpAsync(() => task.IsCompleted), $"Task 在超时内没返回(窗口可见={dlg.IsVisible})");
        Assert.True(await PumpAsync(() => !dlg.IsVisible), "点完之后窗口没关");
        Assert.False(Dlg.AnyOpen, "模态状态(Dlg.AnyOpen)没退出");
        Assert.Equal(expected, await task);
    }

    [Fact]
    public void Confirm_ClickOk_ClosesWindowAndReturnsTrue() => TestApp.RunAsync(async () =>
    {
        using var host = new Host();
        var (task, dlg) = await OpenAsync(host, w => Dlg.Confirm("探针", "按钮应能关窗", "确定", "取消", owner: w));
        FindButton(dlg, "确定").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await AssertClosedAsync(task, dlg, expected: true);
    });

    [Fact]
    public void Confirm_ClickCancel_ClosesWindowAndReturnsFalse() => TestApp.RunAsync(async () =>
    {
        using var host = new Host();
        var (task, dlg) = await OpenAsync(host, w => Dlg.Confirm("探针", "按钮应能关窗", "确定", "取消", owner: w));
        FindButton(dlg, "取消").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await AssertClosedAsync(task, dlg, expected: false);
    });

    /// <summary>兜底语义:只关窗(等价 WM_DELETE_WINDOW)算「取消」,调用方拿到 false 而不是卡住。</summary>
    [Fact]
    public void Confirm_WindowClosed_ReturnsFalse() => TestApp.RunAsync(async () =>
    {
        using var host = new Host();
        var (task, dlg) = await OpenAsync(host, w => Dlg.Confirm("探针", "关窗应算取消", "确定", "取消", owner: w));
        dlg.Close();
        await AssertClosedAsync(task, dlg, expected: false);
    });

    /// <summary>Prompt:确定 → 输入内容;取消 → null(对照 Windows 版的两个按钮)。</summary>
    [Fact]
    public void Prompt_ClickOk_ReturnsText_ClickCancel_ReturnsNull() => TestApp.RunAsync(async () =>
    {
        using (var host = new Host())
        {
            var (task, dlg) = await OpenAsync(host, w => Dlg.Prompt("探针", "名称", "abc", owner: w));
            FindButton(dlg, "确定").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await AssertClosedAsync(task, dlg, expected: "abc");
        }

        using (var host = new Host())
        {
            var (task, dlg) = await OpenAsync(host, w => Dlg.Prompt("探针", "名称", "abc", owner: w));
            FindButton(dlg, "取消").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await AssertClosedAsync(task, dlg, expected: (string?)null);
        }
    });

    /// <summary>Choose:点第几个就返回几;直接关窗是 -1。三分支确认全靠这条。</summary>
    [Fact]
    public void Choose_ClickButton_ReturnsIndex_AndCloses() => TestApp.RunAsync(async () =>
    {
        using var host = new Host();
        var (task, dlg) = await OpenAsync(host, w => Dlg.Choose("探针", new Border(), new[] { "取消", "安装最新", "安装所选" }, owner: w));
        FindButton(dlg, "安装最新").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await AssertClosedAsync(task, dlg, expected: 1);
    });

    /// <summary>
    /// Alert 照 Windows 版 Dlg.Alert:**只有一个「知道了」按钮**(以前会多渲染一个「取消」),
    /// 且点按钮、直接关窗都不能卡住(参考版 dismiss 也走 true)。
    /// </summary>
    [Fact]
    public void Alert_HasSingleKnowButton_AndNeverHangs() => TestApp.RunAsync(async () =>
    {
        using (var host = new Host())
        {
            var task = Dlg.Alert("探针", "提示框只该有一个按钮", owner: host.Window);
            Assert.True(await PumpAsync(() => host.Window.OwnedWindows.Count > 0), "提示框没弹出来");
            var dlg = host.Window.OwnedWindows.Last();
            var buttons = dlg.GetLogicalDescendants().OfType<Button>().ToList();
            Assert.Single(buttons);
            Assert.Equal("知道了", buttons[0].Content as string);

            buttons[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(await PumpAsync(() => task.IsCompleted), "点「知道了」后 Task 没返回");
            await task;
            Assert.True(await PumpAsync(() => !dlg.IsVisible), "点「知道了」后窗口没关");
            Assert.False(Dlg.AnyOpen, "模态状态没退出");
        }

        // 兜底:不点按钮、直接关窗也不能卡住
        using (var host = new Host())
        {
            var task = Dlg.Alert("探针", "关窗也不该卡", owner: host.Window);
            Assert.True(await PumpAsync(() => host.Window.OwnedWindows.Count > 0), "提示框没弹出来");
            host.Window.OwnedWindows.Last().Close();
            Assert.True(await PumpAsync(() => task.IsCompleted), "关窗后 Task 没返回");
            await task;
        }
    });
}
