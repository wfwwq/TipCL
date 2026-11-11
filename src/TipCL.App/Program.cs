using System;
using Avalonia;
using TipCL.Smoke;

namespace TipCL;

internal static class Program
{
    /// <summary>
    /// 不带 <c>--smoke</c> 时走正常桌面启动路径;带 <c>--smoke</c> 时走离屏截图路径。
    /// 退出码:0 成功;1 渲染或写盘失败;2 命令行参数错误。
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        if (HasFlag(args, "--smoke"))
        {
            if (!SmokeOptions.TryParse(args, out SmokeOptions options, out string error))
            {
                Console.Error.WriteLine("[smoke] 参数错误: " + error);
                Console.Error.WriteLine(SmokeOptions.Usage);
                return 2;
            }

            return SmokeRunner.Run(options);
        }

        BuildAppBuilder().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    internal static AppBuilder BuildAppBuilder()
    {
        return AppBuilder.Configure<App>().UsePlatformDetect();
    }

    private static bool HasFlag(string[] args, string flag)
    {
        foreach (string arg in args)
        {
            if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
