using System;
using System.Linq;
using System.Threading.Tasks;
using TipCL.App.Common;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>M2-B0：TaskStore 状态机与队列（页面 / 任务角标共用的数据源）。</summary>
public class TaskStoreTests
{
    [Fact]
    public void Add_Increments_Badge_And_Fires_Events() => TestApp.Run(() =>
    {
        TaskStore.ClearFinished();
        int before = TaskStore.RunningCount;
        int? badge = null;
        TaskRow? added = null;
        TaskStore.Changed += OnChanged;
        TaskStore.Added += OnAdded;
        try
        {
            var row = TaskStore.Add("t1", "下载资源");
            Assert.Equal(before + 1, badge);
            Assert.Equal("t1", added!.Id);
            Assert.Equal("下载资源", row.Title);
            Assert.Equal(TaskPhase.Queued, row.Phase);
        }
        finally
        {
            TaskStore.Changed -= OnChanged;
            TaskStore.Added -= OnAdded;
        }
        void OnChanged() => badge = TaskStore.RunningCount;
        void OnAdded(TaskRow r) => added = r;
    });

    [Fact]
    public void Progress_Splits_Status_And_Speed_And_Clamps() => TestApp.Run(() =>
    {
        TaskStore.ClearFinished();
        var row = TaskStore.Add("t2", "任务");
        TaskStore.Handle(new TaskEvent { Event = "progress", TaskId = "t2", Message = "下载中  |  2.1 MB/s", Current = 50, Total = 100 });
        Assert.Equal(TaskPhase.Running, row.Phase);
        Assert.Equal("下载中", row.Status);
        Assert.Equal("2.1 MB/s", row.Speed);
        Assert.Equal(50.0, row.Progress);
        TaskStore.Handle(new TaskEvent { Event = "progress", TaskId = "t2", Message = "", Current = 999, Total = 100 });
        Assert.Equal(100.0, row.Progress);
    });

    [Fact]
    public void Finished_Events_Move_State_Machine() => TestApp.Run(() =>
    {
        TaskStore.ClearFinished();
        var row = TaskStore.Add("t3", "任务");
        TaskStore.Handle(new TaskEvent { Event = "finished", TaskId = "t3", Success = true });
        Assert.True(row.Finished);
        Assert.True(row.Success);
        Assert.Equal(100.0, row.Progress);
        Assert.Equal(TaskPhase.Finished, row.Phase);
        Assert.Equal(0, TaskStore.Rows.Count(r => !r.Finished && r.Id == "t3"));

        // finished 前先入队再失败：状态用事件消息
        var row2 = TaskStore.Add("t4", "任务");
        TaskStore.Handle(new TaskEvent { Event = "finished", TaskId = "t4", Success = false, Message = "磁盘已满" });
        Assert.False(row2.Success);
        Assert.Equal("磁盘已满", row2.Status);
        TaskStore.ClearFinished();
        Assert.DoesNotContain(TaskStore.Rows, r => r.Id is "t3" or "t4");
    });

    /// <summary>
    /// 任务工作体在线程池上 Report 进度;订阅者(任务页、启动页任务卡)会直接改控件。
    /// 事件若在工作线程触发,Avalonia 会抛「Call from invalid thread」并把安装任务打断。
    /// </summary>
    [Fact]
    public Task Progress_FromWorkerThread_IsDeliveredOnUiThread() => TestApp.RunAsync(async () =>
    {
        TaskStore.ClearFinished();
        var row = TaskStore.Add("t-worker", "任务");
        bool? onUi = null;
        void OnUpdated(TaskRow r)
        {
            if (r.Id == "t-worker")
                onUi = Avalonia.Threading.Dispatcher.UIThread.CheckAccess();
        }
        TaskStore.Updated += OnUpdated;
        try
        {
            await Task.Run(() => TaskStore.Handle(new TaskEvent { Event = "progress", TaskId = "t-worker", Message = "下载中", Current = 1, Total = 2 }));
            for (int i = 0; i < 50 && onUi == null; i++)
                await Task.Delay(10);
            Assert.True(onUi, "进度事件没有回到 UI 线程");
            Assert.Equal(50.0, row.Progress);
        }
        finally
        {
            TaskStore.Updated -= OnUpdated;
            TaskStore.Handle(new TaskEvent { Event = "finished", TaskId = "t-worker", Success = true });
        }
    });

    [Fact]
    public void Log_Appends_And_Trims() => TestApp.Run(() =>
    {
        TaskStore.ClearFinished();
        var row = TaskStore.Add("t5", "任务");
        for (int i = 0; i < 700; i++) row.Append("行" + i);
        Assert.True(row.LogLines <= 601);
        Assert.True(row.Log.ToString().Length < 100_000);
    });

    [Fact]
    public void Remove_Only_Removes_Finished() => TestApp.Run(() =>
    {
        TaskStore.ClearFinished();
        TaskStore.Add("t6", "任务");
        Assert.False(TaskStore.Remove("t6"));
        TaskStore.Handle(new TaskEvent { Event = "finished", TaskId = "t6", Success = true });
        Assert.True(TaskStore.Remove("t6"));
        Assert.Null(TaskStore.Get("t6"));
    });

    [Fact]
    public void Rows_Capped_At_60() => TestApp.Run(() =>
    {
        TaskStore.ClearFinished();
        for (int i = 0; i < 70; i++) TaskStore.Add("cap-" + i, "任务");
        Assert.True(TaskStore.Rows.Count <= 60);
        TaskStore.ClearFinished();
    });

    [Fact]
    public void SplitMsg_And_Clamp() => TestApp.Run(() =>
    {
        TaskMsg.SplitMsg("解压  |  12 MB/s", out var status, out var speed);
        Assert.Equal("解压", status);
        Assert.Equal("12 MB/s", speed);
        TaskMsg.SplitMsg(null, out var s2, out var sp2);
        Assert.Equal("", s2);
        Assert.Equal("", sp2);
        Assert.Equal(5.0, TaskMsg.Clamp(1, 5, 10));
        Assert.Equal(10.0, TaskMsg.Clamp(11, 5, 10));
        Assert.Equal(7.0, TaskMsg.Clamp(7, 5, 10));
    });
}
