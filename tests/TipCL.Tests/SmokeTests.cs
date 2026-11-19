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
        // 约定:两个 csproj 的 <Version> + CoreInfo.Version + AppInfo.APP_VERSION 必须是同一个产品版本
        // (启动横幅 / HTTP UA / 插件宿主默认版本都读这几个)。
        //
        // audit/D(2026-11-20):这里以前写死了 "1.2.0"。版本一抬,这条断言就**必然**红 ——
        // 于是每次发版都得改测试,而"改测试让它绿"正是最容易被改成假绿的地方。
        // 现在只钉「两处常量不许打架」;版本号的**数值**与另外三处(csproj / update.json /
        // update-linux.json)的一致性由 tests/TipCL.Tests/Repo/RepoConsistencyTests.cs 兜。
        Assert.Equal(TipCL.Core.Launcher.AppInfo.APP_VERSION, CoreInfo.Version);
        Assert.False(string.IsNullOrWhiteSpace(CoreInfo.Version));

        // 形状也看一眼:至少是 major.minor.patch,防止有人把它改成 "1.2.0-beta" 之类让
        // .deb 文件名 / 更新比较失配的写法(deb.sh 直接用这个值拼文件名)。
        Assert.Matches("^[0-9]+\\.[0-9]+\\.[0-9]+$", CoreInfo.Version);
    }
}
