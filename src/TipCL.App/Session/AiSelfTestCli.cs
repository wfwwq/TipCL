// M4-H P5 尾巴:`--ai-selftest` 命令行入口。
//
// 参考版的 `AiSelfTest` 是 WPF 界面自检(开窗、点按钮、截图);Linux 侧是纯功能自检
// (`AiSelfTest.VerifyAsync` ← xunit `SelfTest_AllChecksPass` 一直在跑,但用户/真机侧
// 没有入口可调)。这里把它接成 CLI:不起 GUI、不联网,结果打到 stdout 并落日志,
// 退出码 0 = 全过,4 = 有用例失败(与 CliExit.Usage 同值但语义是「自检没过」)。
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TipCL.App.Common;
using TipCL.Core.Launcher.Ai;

namespace TipCL.App.Session;

/// <summary>`--ai-selftest` 的实现。</summary>
internal static class AiSelfTestCli
{
    /// <summary>自检失败时的退出码(与 CliExit.Usage 同值:命令行/环境有问题)。</summary>
    public const int SelftestFailed = 4;

    /// <summary>跑自检并把报告写出去,返回进程退出码。</summary>
    public static int Run(bool sendOne = false, TextWriter? output = null)
    {
        output ??= Console.Out;
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (Exception)
        {
        }

        try
        {
            AiSelfTestResult r = AiSelfTest.VerifyAsync().GetAwaiter().GetResult();
            output.WriteLine(r.Report());
            AppLog.Info("--ai-selftest:通过 " + r.Passed.Count + " / 失败 " + r.Failed.Count + " / 跳过 " + r.Skipped.Count);
            foreach (string f in r.Failed)
            {
                AppLog.Error("--ai-selftest 失败项: " + f);
            }
            if (sendOne)
            {
                // 真模型那一发需要 API key(对外调用),默认不做;显式加 --ai-selftest-send 才发。
                output.WriteLine("(真模型端到端未跑:需要真实 API key,属对外调用,留给用户决定)");
            }
            return r.Ok ? 0 : SelftestFailed;
        }
        catch (Exception ex)
        {
            output.WriteLine("AI 自检没能跑完:" + (ex is TipCL.Core.PyException ? ex.Message : ex.ToString()));
            AppLog.Error("AI 自检异常", ex);
            return SelftestFailed;
        }
    }
}
