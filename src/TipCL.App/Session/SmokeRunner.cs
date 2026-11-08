using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace TipCL.App.Session;

/// <summary>
/// --smoke 冒烟骨架（M1-A1）：无头平台启动，渲染 MainWindow 并保存 smoke.png 后退出。
/// A5 负责充实冒烟流程（输出路径参数、退出码约定、文档化）。
/// </summary>
internal static class SmokeRunner
{
    public static int Run(AppBuilder builder)
    {
        int exitCode = 0;
        var output = Path.GetFullPath("smoke.png");

        builder
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .AfterSetup(_ =>
            {
                try
                {
                    var window = new MainWindow();
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
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
}
