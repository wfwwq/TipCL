using System;
using System.Threading.Tasks;
using TipCL.App.Common;
using TipCL.Core;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>M2-B0：IApi 门面纯逻辑部分（参数归一 / 桩方法 / 任务框架），不触网。</summary>
public class ApiTests
{
    private readonly InProcessApi _api = InProcessApi.Instance;

    [Fact]
    public void ToDict_Accepts_Anonymous_Dict_And_PyDict() => TestApp.Run(() =>
    {
        var fromAnon = InProcessApi.ToDict(new { instance = "生存", version = "1.20.1" });
        Assert.Equal("生存", Py.S(fromAnon.Get("instance")));
        Assert.Equal("1.20.1", Py.S(fromAnon.Get("version")));

        var fromDict = InProcessApi.ToDict(new System.Collections.Generic.Dictionary<string, object> { ["key"] = 1L });
        Assert.Equal(1L, fromDict.Get("key"));

        var pd = new PyDict();
        pd["a"] = "b";
        var fromPd = InProcessApi.ToDict(pd);
        Assert.Equal("b", Py.S(fromPd.Get("a")));

        Assert.Equal(0, InProcessApi.ToDict(null).Count);
    });

    [Fact]
    public Task Unknown_Method_Throws_But_TryCall_Falls_Back() => TestApp.RunAsync(async () =>
    {
        await Assert.ThrowsAsync<NotImplementedException>(
            () => _api.CallAsync<object>("no_such_method", null));
        var fallback = await _api.TryCallAsync("no_such_method", 42);
        Assert.Equal(42, fallback);
    });

    [Fact]
    public Task Stub_Methods_Return_Safe_Values() => TestApp.RunAsync(async () =>
    {
        Assert.False(await _api.CallAsync<bool>("is_game_running"));
        // M3-C0：get_total_playtime 由安全桩转正（Playtime 后端随启动链搬入），改断非负数值
        long total = await _api.CallAsync<long>("get_total_playtime");
        Assert.True(total >= 0);
        var news = await _api.TryCallAsync("cached_news", new System.Collections.Generic.List<object>());
        Assert.NotNull(news);
    });

    [Fact]
    public Task CancelTask_Unknown_Id_Returns_False() => TestApp.RunAsync(async () =>
    {
        var d = await _api.CallAsync<PyDict>("cancel_task", new { id = "nope" });
        Assert.False(Py.Truthy(d!.Get("ok")));
    });

    [Fact]
    public Task StartTask_Runs_To_Finished_And_Reports_Progress() => TestApp.RunAsync(async () =>
    {
        TaskStore.ClearFinished();
        long progressSeen = 0;
        void Handler(ApiEvent ev)
        {
            if (ev.Event == "progress") progressSeen = ev.Current;
            if (ev.Event == "task_added") progressSeen = -1;
        }
        _api.BridgeEvent += Handler;
        var row = await _api.StartTaskAsync("测试任务", async (progress, ct) =>
        {
            progress.Report("进行中", current: 30, total: 100);
            await Task.Delay(10, ct);
        });
        // 等后台收尾
        for (int i = 0; i < 50 && !row.Finished; i++) await Task.Delay(20);
        _api.BridgeEvent -= Handler;
        Assert.True(row.Finished);
        Assert.True(row.Success);
        Assert.Equal(TaskPhase.Finished, row.Phase);
        Assert.Equal(30, progressSeen);
        Assert.Same(row, TaskStore.Get(row.Id));
        TaskStore.ClearFinished();
    });

    [Fact]
    public Task StartTask_Exception_Marks_Row_Failed() => TestApp.RunAsync(async () =>
    {
        TaskStore.ClearFinished();
        var row = await _api.StartTaskAsync("会失败的任务", (_, _) => Task.FromException(new InvalidOperationException("炸了")));
        for (int i = 0; i < 50 && !row.Finished; i++) await Task.Delay(20);
        Assert.True(row.Finished);
        Assert.False(row.Success);
        Assert.Equal("炸了", row.Status);
        TaskStore.ClearFinished();
    });
}
