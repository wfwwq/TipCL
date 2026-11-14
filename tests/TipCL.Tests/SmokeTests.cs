using TipCL.Core;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 占位测试（M1-A1）：验证测试基建可用。A5 充实后可移除。
/// </summary>
public class SmokeTests
{
    [Fact]
    public void CoreVersion_MatchesTheUnifiedProductVersion()
    {
        // 2026-11-14 起移植侧产品版本统一为 1.1.0:两个 csproj 的 <Version> + CoreInfo.Version
        // (启动横幅 / HTTP UA / 插件宿主默认版本)都必须与 AppInfo.APP_VERSION 一致
        // (三处一致的约定见 docs/README.md)。
        Assert.Equal("1.1.0", CoreInfo.Version);
        // 顺带盯住"两个常量不许再打架"——这正是本次修的那个不一致
        Assert.Equal(TipCL.Core.Launcher.AppInfo.APP_VERSION, CoreInfo.Version);
    }
}
