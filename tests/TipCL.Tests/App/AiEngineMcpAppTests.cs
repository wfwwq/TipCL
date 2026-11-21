// AI-engine-mcp:界面侧的两处接线 —— MCP Server 宿主造的执行器一定开着「外部写操作必须确认」,
// 权限面板的「限定参数」说明与 Core 的实际匹配规则对得上(不再暗示「记了但不判」)。
using TipCL.App.Common.Ai;
using TipCL.App.Pages.Ai;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.App;

public class AiEngineMcpAppTests
{
    [Fact]
    public void McpServerHost_Runner_AlwaysForcesConfirmForWrites_AndHasAConfirmCallback()
    {
        ToolRunner runner = McpServerHost.CreateRunner();
        Assert.True(runner.ForceConfirmWrites);
        Assert.NotNull(runner.Confirm);
        // 档位留空 = 跟随用户在 AI 页选的档位(只看不动 / 不询问仍然直接拒绝)
        Assert.Equal("", runner.PermissionMode);
    }

    [Fact]
    public void PermissionPanel_ContentHint_DescribesTheRealMatchingRules()
    {
        string hint = AiPermissionPanel.ContentHint;
        Assert.Contains("限定参数", hint);
        // 说明里点名的参数键都真的在 Core 的候选键里
        foreach (string key in new[] { "path", "filename", "slug", "name", "version", "major" })
        {
            Assert.Contains(key, hint);
            Assert.Contains(key, PermissionRules.RULE_CONTENT_KEYS);
        }
        Assert.Contains("删除类", hint);
    }
}
