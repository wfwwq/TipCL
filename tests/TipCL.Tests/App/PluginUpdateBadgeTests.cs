// M4-H R4 测试:插件「有更新」徽标与市场详情截图入口。
//
// 市场索引要联网,这里只覆盖不联网的部分:徽标文案、PluginInfo 的默认值、
// 以及「市场版本比本机高才算有更新」这条判定(与 Core PluginMarket.Rows 的 updatable 同一口径)。
using TipCL.App.Pages.Plugins;
using TipCL.Core.Plugins;
using Xunit;

namespace TipCL.Tests.App;

public class PluginUpdateBadgeTests
{
    [Fact]
    public void BadgeTextShowsTargetVersionWhenKnown()
    {
        var p = new PluginInfo { Id = "sodium", Version = "1.0.0", UpdateAvailable = true, MarketVersion = "1.2.0" };
        Assert.Equal("有更新 → v1.2.0", PluginsPage.UpdateBadgeText(p));

        // 只有「有更新」标志、拿不到市场版本号时也要能显示
        var unknown = new PluginInfo { Id = "x", UpdateAvailable = true };
        Assert.Equal("有更新", PluginsPage.UpdateBadgeText(unknown));
    }

    [Fact]
    public void PluginInfoDefaultsHaveNoUpdate()
    {
        var p = new PluginInfo();
        Assert.False(p.UpdateAvailable);
        Assert.Equal("", p.MarketVersion);
    }

    [Fact]
    public void UpdatableFollowsMarketVersionComparison()
    {
        // 与 Core.PluginMarket.Rows 的 updatable 口径一致:市场版本 > 本机版本
        Assert.True(SemVer.Compare("1.2.0", "1.0.0") > 0);
        Assert.False(SemVer.Compare("1.0.0", "1.0.0") > 0);
        Assert.False(SemVer.Compare("0.9.0", "1.0.0") > 0);
        Assert.True(SemVer.Compare("1.0.1", "1.0.0") > 0);
    }
}
