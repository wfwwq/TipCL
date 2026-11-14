using System;
using Avalonia;

namespace TipCL.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // 文件日志必须先于任何界面代码:TIPCL_HOME/logs/tipcl-*.log(nohup 下 stdout 常为空)
        Common.AppLog.Init();

        try
        {
            // 命令行参数解析骨架(M1-A1)。--smoke:离屏渲染主窗口截图到 smoke.png 后退出。
            if (Array.IndexOf(args, "--smoke") >= 0)
                return Session.SmokeRunner.Run(BuildAvaloniaApp());

            // AI 功能自检(M4-H P5 尾巴):不起 GUI、不联网,结果打到 stdout 并落日志。
            if (Array.IndexOf(args, "--ai-selftest") >= 0)
                return Session.AiSelfTestCli.Run(sendOne: Array.IndexOf(args, "--ai-selftest-send") >= 0);

            // 桌面快捷方式入口(M4-H P3.5):-i <实例> launch <版本> [--account X|--username Y]。
            // 与 Core/Launcher/Shortcut.LaunchArgs 的输出严格对齐,走与 UI 相同的启动链路。
            var cli = Session.CommandLine.Parse(args);
            if (cli.Kind != Session.CliKind.None)
            {
                int code = Session.CommandLine.Run(cli);
                Common.AppLog.Info($"=== CLI 退出({cli.Kind},退出码 {code})===");
                return code;
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            Common.AppLog.Info("=== 正常退出 ===");
            return 0;
        }
        catch (Exception ex)
        {
            Common.AppLog.Error("启动/运行失败", ex);
            return 4;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace()
        .AfterSetup(_ =>
        {
            // 默认 Trace 汇出到文件;UI 线程未处理异常也落盘(否则窗口直接消失无迹可查)
            Avalonia.Logging.Logger.Sink = new Common.AppLogSink();
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                Common.AppLog.Error("unhandled(UI)", e.Exception);
                e.Handled = false;
            };
        });
}