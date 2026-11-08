using System;
using Avalonia;

namespace TipCL.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // 命令行参数解析骨架（M1-A1）。--smoke：离屏渲染主窗口截图到 smoke.png 后退出。
        if (Array.IndexOf(args, "--smoke") >= 0)
        {
            return Session.SmokeRunner.Run(BuildAvaloniaApp());
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}
