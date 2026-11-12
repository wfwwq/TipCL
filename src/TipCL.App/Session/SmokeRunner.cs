using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Avalonia.Threading;
using TipCL.App.Common;
using TipCL.App.Shell;

namespace TipCL.App.Session;

/// <summary>
/// --smoke 冒烟骨架（M1-A1）：无头平台启动，渲染 MainWindow 并保存 smoke.png 后退出。
/// A5 负责充实冒烟流程（输出路径参数、退出码约定、文档化）。
/// </summary>
internal static class SmokeRunner
{
    public static int Run(AppBuilder builder)
    {
        // SMOKE_NET=<url>:网络探针,打印 .NET 侧的 HTTP 结果后退出(真机网络排障用)
        var netUrl = Environment.GetEnvironmentVariable("SMOKE_NET");
        if (!string.IsNullOrEmpty(netUrl))
            return NetProbe(netUrl);

        // SMOKE_INSTALL=<version>:无头安装 E2E 钩子(真机排障/回归用)
        var installVer = Environment.GetEnvironmentVariable("SMOKE_INSTALL");
        if (!string.IsNullOrEmpty(installVer))
            return InstallProbe(installVer, Environment.GetEnvironmentVariable("SMOKE_INSTANCE") ?? "probe");

        int exitCode = 0;
        var output = Path.GetFullPath("smoke.png");

        // 静态截图不该被入场动画影响:FadeIn 会把子项初值设成 Opacity=0 再靠过渡补回,
        // 无头单帧捕获跑不动过渡 → 内容整片不可见。冒烟一律关动画。
        Motion.Enabled = false;

        // SMOKE_TASKS=1:塞两条假任务(进行中 + 失败),供任务页视觉回归(SMOKE_PAGE=tasks)
        if (Environment.GetEnvironmentVariable("SMOKE_TASKS") == "1")
        {
            var running = TaskStore.Add("smoke-run", "安装 26.3(冒烟)");
            running.Indeterminate = false;
            running.Progress = 42;
            running.Status = "下载 client.jar";
            running.Speed = "3.2 MB/s";
            running.Append("下载 client.jar …\n校验 sha1 …");
            var failed = TaskStore.Add("smoke-fail", "安装 fabric-loader(冒烟)");
            TaskStore.Finish(failed, false, "连接超时");
        }

        builder
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .AfterSetup(_ =>
            {
                try
                {
                    // 明暗只由 SMOKE_DARK 决定,且要进(内存里的)配置:外壳构造与设置页加载都按 ui_dark 取明暗,
                    // 只调 SetDark 的话设置页一加载就把深色帧改回浅色
                    TipCL.Core.Launcher.Config.CONFIG.Set("ui_dark", Environment.GetEnvironmentVariable("SMOKE_DARK") == "1");
                    var window = new Shell.MainWindow();
                    var page = Environment.GetEnvironmentVariable("SMOKE_PAGE");
                    window.Show();
                    if (!string.IsNullOrEmpty(page))
                        window.Navigate(page);
                    // SMOKE_SEG=1..6:下载页切到某个目录分段(Mod/整合包/数据包/资源包/光影/世界)
                    if (int.TryParse(Environment.GetEnvironmentVariable("SMOKE_SEG"), out var seg)
                        && window.CurrentPageId == "download"
                        && window.CurrentPage is TipCL.App.Pages.Download.DownloadPage dl)
                        dl.SelectSegment(seg);
                    // SMOKE_SETTINGS_TAB=0..6:设置页切到某个内层导航项(Java / 服务器 / 游戏时长 等)
                    if (int.TryParse(Environment.GetEnvironmentVariable("SMOKE_SETTINGS_TAB"), out var tab)
                        && window.CurrentPageId == "settings"
                        && window.CurrentPage is TipCL.App.Pages.Settings.SettingsPage st)
                        st.SelectTab(tab);
                    // R8:导航后只跑 RunJobs 不够——无头会话里 await / Thread.Sleep 都不触发
                    // Measure/Arrange,必须显式推渲染计时器,布局与重合成才跑得动
                    // (仓库先例:tests/TipCL.Tests/App/InstanceDetailCrashTests.cs 的 PassAsync)。
                    RenderPass();

                    // R8 真机现象:冷启动首批 8 页里前 4 页把上一页(launch)的帧原样写进新文件名。
                    // 成因候选:SMOKE_WAIT 默认 0 时零 settle,CaptureRenderedFrame 抓到尚未重合成的上一帧。
                    // 故未显式给 SMOKE_WAIT 时也补一个最小 settle;显式给了就仍等这么久(原语义)。
                    var waitRaw = Environment.GetEnvironmentVariable("SMOKE_WAIT");
                    var waitGiven = int.TryParse(waitRaw, out var waitSec);
                    var settleMs = waitGiven ? Math.Max(0, waitSec) * 1000 : DefaultSettleMs;
                    Settle(settleMs);

                    if (Environment.GetEnvironmentVariable("SMOKE_PROBE") == "1")
                    {
                        foreach (var v in window.GetVisualDescendants().Prepend(window))
                        {
                            Console.WriteLine($"PROBE {v.GetType().Name} cls=[{string.Join(",", (v as Avalonia.StyledElement)?.Classes ?? [])}] bounds={v.Bounds.Width:F1}x{v.Bounds.Height:F1}@{v.Bounds.X:F0},{v.Bounds.Y:F0}");
                        }
                    }
                    // 捕获前再显式推一次渲染 pass,然后捕获两次、丢弃第一帧、只保存第二帧:
                    // 第一张最有可能就是 R8 那个「尚未重合成的上一帧」。
                    RenderPass();
                    DiscardFirstFrame(window);
                    using var frame = window.CaptureRenderedFrame();
                    if (frame is null)
                    {
                        Console.Error.WriteLine("--smoke：未能捕获渲染帧");
                        exitCode = 1;
                    }
                    else
                    {
                        using var stream = File.Create(output);
                        frame.Save(stream);
                        Console.WriteLine($"--smoke：已输出 {output}");
                    }
                    window.Close();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"--smoke 失败: {ex}");
                    exitCode = 1;
                }
            })
            .SetupWithoutStarting();

        return exitCode;
    }

    /// <summary>未显式给 SMOKE_WAIT 时的最小 settle 毫秒数:给外壳一次重新测量 + 重合成的机会。</summary>
    private const int DefaultSettleMs = 400;

    /// <summary>
    /// 显式推一次渲染 pass:计时器 tick + 跑 Dispatcher 队列。无头会话里只 RunJobs 不会触发
    /// Measure/Arrange,布局、模板、重合成都要靠这次 tick 才会跑(仓库先例见 DECISIONS.md D-39)。
    /// </summary>
    private static void RenderPass()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// settle:ms 毫秒内反复推渲染 pass(tick + RunJobs),让异步灌进来的数据、待跑布局与
    /// 重合成落地。ms 为 0 时仍至少推两次渲染 pass(不会退化成「什么都不做」)。
    /// </summary>
    private static void Settle(int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(Math.Max(0, ms));
        do
        {
            RenderPass();
            if (DateTime.UtcNow >= until) break;
            System.Threading.Thread.Sleep(30);
        } while (true);
    }

    /// <summary>捕获一次并丢弃(它最可能就是上一页的残留帧);失败只记一笔,不让冒烟直接退出。</summary>
    private static void DiscardFirstFrame(Window window)
    {
        try
        {
            using var warm = window.CaptureRenderedFrame();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"--smoke:丢弃的首帧捕获失败(忽略): {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int InstallProbe(string version, string instance)
    {
        try
        {
            InProcessApi.Instance.BridgeEvent += ev =>
            {
                if (ev.Event is "progress" or "log" or "finished" or "failed")
                    Console.WriteLine($"INSTALL {ev.Event}: {ev.Message}{ev.Text} ({ev.Current}/{ev.Total})");
            };
            InProcessApi.Instance.CallAsync<object?>("create_instance", new { name = instance }).GetAwaiter().GetResult();
            InProcessApi.Instance.CallAsync<object?>("install_game", new
            {
                version, loader = "", loader_version = (string?)null, instance,
            }).GetAwaiter().GetResult();
            Console.WriteLine("INSTALL OK");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("INSTALL FAIL " + ex.GetType().FullName + ": " + ex.Message);
            for (var e = ex.InnerException; e != null; e = e.InnerException)
                Console.WriteLine("  inner: " + e.GetType().Name + ": " + e.Message);
            Console.WriteLine(ex.StackTrace);
            return 3;
        }
    }

    private static int NetProbe(string url)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TipCL/0.1.0-m3");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var resp = http.GetAsync(url).GetAwaiter().GetResult();
            var body = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            Console.WriteLine($"NETPROBE ok status={(int)resp.StatusCode} bytes={body.Length} ms={sw.ElapsedMilliseconds}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"NETPROBE fail {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null) Console.WriteLine("NETPROBE inner: " + ex.InnerException.Message);
            return 2;
        }
    }
}
