// AI2-integration:合并后修的两处跨分支问题的回归。
// 1) AI 工具 install_* 把实例名 / slug / 来源放在参数顶层,InProcessApi 以前只读 extra → 参数丢失;
// 2) InProcessApi 的 install_* 直接返回任务 id 字符串,ToolRunner.Start 以前只认 {task_id} 字典 → task_id 为空。
// 不联网、不起任务:CatalogExtra 是纯函数;Start 用假 IApi 直接回字符串。
#nullable disable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TipCL.App.Common;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.App;

public class AiIntegration2Tests
{
    [Fact]
    public void CatalogExtra_TakesTopLevelArgs_WhenExtraMissing()
    {
        var args = PyDict.Of("name", "sodium", "instance", "my-pack", "source", "Modrinth");
        PyDict extra = InProcessApi.CatalogExtra(args);
        Assert.Equal("sodium", Py.S(extra.Get("name")));
        Assert.Equal("my-pack", Py.S(extra.Get("instance")));
        Assert.Equal("Modrinth", Py.S(extra.Get("source")));
    }

    [Fact]
    public void CatalogExtra_PrefersExtra_OverTopLevel()
    {
        var args = PyDict.Of(
            "name", "top-name",
            "instance", "top-inst",
            "extra", PyDict.Of("name", "page-name", "slug", "page-slug", "instance", "page-inst"));
        PyDict extra = InProcessApi.CatalogExtra(args);
        Assert.Equal("page-name", Py.S(extra.Get("name")));
        Assert.Equal("page-slug", Py.S(extra.Get("slug")));
        Assert.Equal("page-inst", Py.S(extra.Get("instance")));
    }

    [Fact]
    public void CatalogExtra_IgnoresEmptyTopLevelValues_AndUnknownKeys()
    {
        var args = PyDict.Of("name", "", "instance", "x", "unrelated", "y");
        PyDict extra = InProcessApi.CatalogExtra(args);
        Assert.False(Py.Truthy(extra.Get("name")));
        Assert.Equal("x", Py.S(extra.Get("instance")));
        Assert.Null(extra.Get("unrelated"));
    }

    [Theory]
    [InlineData("install_mod")]
    [InlineData("install_shader")]
    [InlineData("install_modpack")]
    public async Task InstallTools_ReportTaskId_WhenApiReturnsPlainString(string tool)
    {
        var api = new StringTaskApi();
        var runner = new ToolRunner(api) { PermissionMode = "yolo", Instance = "default" };
        string started = null;
        runner.TaskStarted = (id, _, _) => started = id;

        var call = new ToolCall("i2-" + tool, tool, new PyDict { ["name"] = "sodium", ["instance"] = "my-pack" });
        await runner.RunAsync(call);

        Assert.Equal(ToolCallStatus.COMPLETED, call.status);
        Assert.Equal("task-str-1", started);
        Assert.Equal("task-str-1", Py.S(Py.D(PyJson.Loads(call.result)).Get("task_id")));
        // 顶层参数照样传到了后端:实例名不再丢
        Assert.Equal("my-pack", api.LastInstance);
    }

    /// <summary>照 InProcessApi 的 install_* 行为:直接回任务 id 字符串。</summary>
    private sealed class StringTaskApi : ToolRunner.IBackend
    {
        public string LastInstance;
        public Task<T> CallAsync<T>(string method, object args = null, CancellationToken ct = default)
        {
            LastInstance = args is PyDict d
                ? Py.S(d.Get("instance"))
                : Py.S(args?.GetType().GetProperty("instance")?.GetValue(args));
            return Task.FromResult((T)(object)"task-str-1");
        }
    }
}
