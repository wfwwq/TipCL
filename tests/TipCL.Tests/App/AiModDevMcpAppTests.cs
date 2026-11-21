// AI2-moddev-mcp:界面侧接线 —— AI 页「MCP 已开启 · 最近外部调用」状态条、宿主给 Core 的任务表探针、
// 撤回本地模组不给「本对话都允许」。不起端口、不弹框。
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using TipCL.App.Common;
using TipCL.App.Common.Ai;
using TipCL.App.Pages.Ai;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.App;

public class AiModDevMcpAppTests
{
    [Fact]
    public void McpStatusText_MatchesTheWindowsWording()
    {
        Assert.Equal("", AiPage.McpStatusText(false, "install_local_mod", DateTime.Now));
        Assert.Equal(I18nCore.Tr("MCP 已开启"), AiPage.McpStatusText(true, "", default));
        string busy = AiPage.McpStatusText(true, "install_local_mod", new DateTime(2026, 11, 21, 12, 34, 56));
        Assert.Contains("install_local_mod", busy);
        Assert.Contains("12:34", busy);
        string tip = AiPage.McpStatusTip("http://127.0.0.1:25585/mcp", "run_test_launch", new DateTime(2026, 11, 21, 12, 34, 56));
        Assert.Contains("http://127.0.0.1:25585/mcp", tip);
        Assert.Contains("2026-11-21 12:34:56", tip);
    }

    [Fact]
    public void AiPage_HasTheMcpChip_HiddenWhileTheServerIsOff() => TestApp.Run(() =>
    {
        Assert.False(McpServer.Running);
        var page = new AiPage();
        Border chip = page.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "McpStatusChip");
        Assert.False(chip.IsVisible);
        page.SyncMcpChip();
        Assert.False(chip.IsVisible);
    });

    [Fact]
    public void HostTaskProbe_ReadsTaskStoreRows() => TestApp.Run(() =>
    {
        string id = "task-moddev-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        TaskRow row = TaskStore.Add(id, "启动游戏 1.20.1");
        try
        {
            row.Append("[Render thread/INFO]: Sound engine started");
            PyDict? d = McpServerHost.TaskProbe(id);
            Assert.NotNull(d);
            Assert.False(Py.Truthy(d!.Get("finished")));
            Assert.Contains("Sound engine started", Py.S(d.Get("log")));
            TaskStore.Finish(row, false, "游戏崩溃");
            PyDict done = McpServerHost.TaskProbe(id)!;
            Assert.True(Py.Truthy(done.Get("finished")));
            Assert.False(Py.Truthy(done.Get("success")));
            Assert.Equal("游戏崩溃", Py.S(done.Get("status")));
            Assert.Null(McpServerHost.TaskProbe("task-does-not-exist"));
        }
        finally
        {
            TaskStore.Finish(row, false);
            TaskStore.Remove(id);
        }
    });

    [Fact]
    public void RevertLocalMod_NeverGetsAllowAlways_InBothPlaces()
    {
        Assert.False(ConfirmCard.AllowAlways("revert_local_mod"));
        Assert.False(ToolRunner.AllowsAlways("revert_local_mod"));
        Assert.True(ConfirmCard.AllowAlways("install_local_mod"));
    }
}
