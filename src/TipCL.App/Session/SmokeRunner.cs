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
                    Dispatcher.UIThread.RunJobs();
                    var waitSec = int.TryParse(Environment.GetEnvironmentVariable("SMOKE_WAIT"), out var w) ? w : 0;
                    if (waitSec > 0)
                    {
                        var until = DateTime.UtcNow.AddSeconds(waitSec);
                        while (DateTime.UtcNow < until)
                        {
                            Dispatcher.UIThread.RunJobs();
                            System.Threading.Thread.Sleep(50);
                        }
                    }
                    if (Environment.GetEnvironmentVariable("SMOKE_PROBE") == "1")
                    {
                        foreach (var v in window.GetVisualDescendants().Prepend(window))
                        {
                            Console.WriteLine($"PROBE {v.GetType().Name} cls=[{string.Join(",", (v as Avalonia.StyledElement)?.Classes ?? [])}] bounds={v.Bounds.Width:F1}x{v.Bounds.Height:F1}@{v.Bounds.X:F0},{v.Bounds.Y:F0}");
                        }
                    }
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
