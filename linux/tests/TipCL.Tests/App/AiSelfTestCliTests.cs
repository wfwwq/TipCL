// M4-H P5 尾巴:`--ai-selftest` 命令行入口回归。
// 参考版是 WPF 界面自检,Linux 侧改成纯功能自检 + CLI;这里锁住「有入口、有报告、退出码对」。
using System.IO;
using TipCL.App.Session;
using Xunit;

namespace TipCL.Tests.App;

public class AiSelfTestCliTests
{
    [Fact]
    public void Run_PrintsReportAndExitsZero()
    {
        var output = new StringWriter();
        int code = AiSelfTestCli.Run(sendOne: false, output: output);

        Assert.Equal(0, code);
        string text = output.ToString();
        Assert.Contains("AI 自检", text);
        Assert.Contains("[OK]", text);
        Assert.DoesNotContain("[FAIL]", text);
        // 真模型端到端不在 CLI 里偷偷跑(需要 API key,属对外调用)
        Assert.DoesNotContain("GAME", text);
    }

    [Fact]
    public void Run_SendOne_OnlyMentionsItIsSkipped()
    {
        var output = new StringWriter();
        int code = AiSelfTestCli.Run(sendOne: true, output: output);
        Assert.Equal(0, code);
        Assert.Contains("真模型端到端未跑", output.ToString());
    }
}
