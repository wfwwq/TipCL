// M4-H P6/P8:插件外壳接线测试。
// 覆盖:界面贡献快照(PluginUi)、动态顶部页签(P8)、plugin: 页面路由、以及
// 「Linux 版没有插件运行时」这条诚实口径(启用/启动必须如实报错,不做假成功)。
// 插件目录写进测试隔离的 TIPCL_HOME,用唯一 id,收尾删掉。
using System;
using System.IO;
using System.Linq;
using TipCL.App.Common.Plugins;
using TipCL.App.Pages.Plugins;
using TipCL.App.Shell;
using TipCL.Core;
using TipCL.Core.Plugins;
using Xunit;

namespace TipCL.Tests.App;

public class PluginUiTests : IDisposable
{
    private readonly string _id = "t-ui-" + Guid.NewGuid().ToString("N").Substring(0, 6);
    private readonly PluginDirs _dirs = new PluginDirs();

    public void Dispose()
    {
        TestApp.Run(() =>
        {
            try { PluginRuntime.Uninstall(_id, deleteData: true); } catch (Exception) { }
            PluginUi.Refresh();
        });
    }

    private void WritePlugin(string contributesJson, string runtime = "node", string runtimeExtra = "")
    {
        string dir = _dirs.PluginDir(_id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, PluginManifest.FileName),
            "{\"id\":\"" + _id + "\",\"name\":\"测试面板\",\"version\":\"1.2.3\",\"runtime\":\"" + runtime + "\"," +
            "\"main\":\"index.js\",\"permissions\":[\"ui\"]" + runtimeExtra + ",\"contributes\":" + contributesJson + "}");
    }

    private const string TopPage = "{\"pages\":[{\"id\":\"panel\",\"label\":\"测试面板\",\"group\":\"top\",\"order\":3,\"url\":\"https://example.com/panel\"}]}";

    [Fact]
    public void PluginUi_ExposesManifestContributionsWithGroup() => TestApp.Run(() =>
    {
        WritePlugin(TopPage);
        PluginUi.Refresh();

        var c = PluginUi.FindPage("plugin:" + _id + ":panel");
        Assert.NotNull(c);
        Assert.Equal("page", c!.Kind);
        Assert.Equal("测试面板", c.Label);
        Assert.Equal("top", c.Group);
        Assert.Equal(3.0, c.Order);
        Assert.Contains(PluginUi.OfKind("page"), x => x.Key == c.Key);

        // 插件列表快照也要有(已安装页读它)
        var info = PluginUi.Plugin(_id);
        Assert.NotNull(info);
        Assert.Equal("1.2.3", info!.Version);
    });

    [Fact]
    public void PluginUi_MissingPageReturnsNull() => TestApp.Run(() =>
    {
        PluginUi.Refresh();
        Assert.Null(PluginUi.FindPage("plugin:" + _id + ":nope"));
    });

    /// <summary>P8:`group:"top"` 的插件页面变成顶部页签,点进去是 plugin: 页面。</summary>
    [Fact]
    public void TitleBar_ShowsPluginTabs() => TestApp.Run(() =>
    {
        var bar = new TitleBar();
        Assert.Empty(bar.PluginTabIds);
        bar.SetPluginTabs(new[] { ("plugin:" + _id + ":panel", "测试面板") });
        Assert.Equal(new[] { "plugin:" + _id + ":panel" }, bar.PluginTabIds);
        bar.SetPluginTabs(Array.Empty<(string, string)>());
        Assert.Empty(bar.PluginTabIds);
    });

    [Fact]
    public void MainWindow_NavigatesToPluginPage_AndMarksItGoneAfterUninstall() => TestApp.Run(() =>
    {
        WritePlugin(TopPage);
        var win = new MainWindow();
        string pageId = "plugin:" + _id + ":panel";

        win.Navigate(pageId);
        Assert.Equal(pageId, win.CurrentPageId);
        Assert.IsType<PluginHostPage>(win.CurrentPage);

        // 卸载后:当前这一页标成不可用(与参考版 MainWindow.Plugins.cs 同语义:
        // 当前页保留但显示「不可用」,非当前页直接从缓存里删掉,不留死页)
        PluginRuntime.Uninstall(_id, deleteData: true);
        win.RefreshPluginUi();
        var gone = Assert.IsType<PluginHostPage>(win.CurrentPage);
        Assert.True(gone.IsGone);
        // 离开再回来拿不到这个页面(_pages 里已经删掉)
        win.Navigate("launch");
        win.Navigate(pageId);
        Assert.Equal("launch", win.CurrentPageId);
    });

    /// <summary>没有 host 的运行环境:启动/启用必须如实说清「不会被执行」,不许假成功。</summary>
    [Fact]
    public void NodePlugin_StartReportsNoRuntime() => TestApp.Run(() =>
    {
        WritePlugin("{}");
        var started = PluginRuntime.Start(_id);
        Assert.False(Py.Truthy(started.Get("ok")));
        Assert.Contains("Node", Py.S(started.Get("error")));

        var enabled = PluginRuntime.SetEnabled(_id, true);
        Assert.False(Py.Truthy(enabled.Get("runnable")), "Linux 上没有插件运行时,runnable 必须是 false");
        Assert.Contains("不会被执行", Py.S(enabled.Get("reason")));
        Assert.True(PluginRuntime.Host.StateStore.Get(_id)!.Enabled, "启用状态本身要落盘(文件级管理是真的)");
    });

    /// <summary>.NET 插件的阻塞点在契约(WPF / net48),不是「缺个 Node」—— 文案要说对。</summary>
    [Fact]
    public void DotNetPlugin_StartExplainsContractBlocker() => TestApp.Run(() =>
    {
        WritePlugin("{}", runtime: "dotnet", runtimeExtra: ",\"assembly\":\"P.dll\",\"entryType\":\"P.Main\"");
        var m = PluginManifest.LoadDir(_dirs.PluginDir(_id));
        Assert.True(m.IsDotnet);
        string reason = CorePlugins_NoRuntime(m);
        Assert.Contains("WPF", reason);
        var started = PluginRuntime.Start(_id);
        Assert.False(Py.Truthy(started.Get("ok")));
        Assert.Contains("WPF", Py.S(started.Get("error")));
    });

    private static string CorePlugins_NoRuntime(PluginManifest m) => PluginHost.NoRuntimeReason(m);

    /// <summary>没有插件时快照刷新也要稳(空表,不抛)。</summary>
    [Fact]
    public void PluginUi_RefreshIsStableWithoutPlugins() => TestApp.Run(() =>
    {
        try { PluginRuntime.Uninstall(_id, deleteData: true); } catch (Exception) { }
        PluginUi.Refresh();
        Assert.DoesNotContain(PluginUi.All, c => c.Plugin == _id);
        Assert.NotNull(PluginUi.Plugins);
    });
}
