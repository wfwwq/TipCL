using System;
using System.Linq;
using TipCL.App.Common;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>M2-B0：TaskStore 状态机与队列（页面 / 任务角标共用的数据源）。</summary>
public class TaskStoreTests
{
    [Fact]
    public void Add_Increments_Badge_And_Fires_Events()
    {
        TaskStore.ClearFinished();
        int? badge = null;
        TaskRow? added = null;
        TaskStore.Changed += OnChanged;
        TaskStore.Added += OnAdded;
        try
        {
            var row = TaskStore.Add("t1", "下载资源");
            Assert.Equal(1, badge);
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
    }

    [Fact]
    public void Progress_Splits_Status_And_Speed_And_Clamps()
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
    }

    [Fact]
    public void Finished_Events_Move_State_Machine()
    {
        TaskStore.ClearFinished();
        var row = TaskStore.Add("t3", "任务");
        TaskStore.Handle(new TaskEvent { Event = "finished", TaskId = "t3", Success = true });
        Assert.True(row.Finished);
        Assert.True(row.Success);
        Assert.Equal(100.0, row.Progress);
        Assert.Equal(TaskPhase.Finished, row.Phase);
        Assert.Equal(0, TaskStore.RunningCount);

        // finished 前先入队再失败：状态用事件消息
        var row2 = TaskStore.Add("t4", "任务");
        TaskStore.Handle(new TaskEvent { Event = "finished", TaskId = "t4", Success = false, Message = "磁盘已满" });
        Assert.False(row2.Success);
        Assert.Equal("磁盘已满", row2.Status);
        TaskStore.ClearFinished();
        Assert.False(TaskStore.Rows.Any(r => r.Id is "t3" or "t4"));
    }

    [Fact]
    public void Log_Appends_And_Trims()
    {
        TaskStore.ClearFinished();
        var row = TaskStore.Add("t5", "任务");
        for (int i = 0; i < 700; i++) row.Append("行" + i);
        Assert.True(row.LogLines <= 601);
        Assert.True(row.Log.ToString().Length < 100_000);
    }

    [Fact]
    public void Remove_Only_Removes_Finished()
    {
        TaskStore.ClearFinished();
        TaskStore.Add("t6", "任务");
        Assert.False(TaskStore.Remove("t6"));
        TaskStore.Handle(new TaskEvent { Event = "finished", TaskId = "t6", Success = true });
        Assert.True(TaskStore.Remove("t6"));
        Assert.Null(TaskStore.Get("t6"));
    }

    [Fact]
    public void Rows_Capped_At_60()
    {
        TaskStore.ClearFinished();
        for (int i = 0; i < 70; i++) TaskStore.Add("cap-" + i, "任务");
        Assert.True(TaskStore.Rows.Count <= 60);
        TaskStore.ClearFinished();
    }

    [Fact]
    public void SplitMsg_And_Clamp()
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
    }
}
