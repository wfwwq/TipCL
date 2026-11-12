using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TipCL.App.Common;

/// <summary>任务行状态机阶段：排队 → 进行中 → 完成/失败。</summary>
public enum TaskPhase
{
    Queued,
    Running,
    Finished
}

/// <summary>
/// 下载/安装任务卡数据源（移植自 Windows 版 src/TipCL/Services/TaskRow.cs + TaskStore.cs，
/// 去掉 WPF/I18n 依赖：文案直接用中文字面量）。纯 C#，页面与任务角标三方共用。
/// </summary>
public sealed class TaskRow
{
    public int LogLines;

    public string Id { get; init; } = "";

    public string Title { get; set; } = "";

    public string Status { get; set; } = "排队中…";

    public string Speed { get; set; } = "";

    public double Progress { get; set; }

    public bool Indeterminate { get; set; } = true;

    public bool Finished { get; set; }

    public bool Success { get; set; }

    public DateTime Started { get; } = DateTime.Now;

    public StringBuilder Log { get; } = new StringBuilder();

    /// <summary>派生阶段：Finished → Finished；Indeterminate 且 Progress≤0 视为排队。</summary>
    public TaskPhase Phase => Finished ? TaskPhase.Finished
        : (Indeterminate && Progress <= 0 && LogLines == 0 ? TaskPhase.Queued : TaskPhase.Running);

    public void Append(string line)
    {
        if (!string.IsNullOrEmpty(line))
        {
            Log.AppendLine(line);
            LogLines++;
            if (LogLines > 600)
            {
                string text = Log.ToString();
                int num = text.IndexOf('\n', text.Length / 3);
                Log.Clear();
                Log.Append((num > 0) ? text.Substring(num + 1) : text);
                LogLines = 400;
            }
        }
    }
}

/// <summary>
/// 后端事件的最小载体（对应 Windows 版 BridgeEvent 的任务相关子集：
/// task_added / progress / log / finished / game_started / game_exited / crash / login_*）。
/// </summary>
public sealed class TaskEvent
{
    public string Event = "";
    public string TaskId = "";
    public string Title = "";
    public string Message = "";
    public string Text = "";
    public long Current;
    public long Total;
    public bool Success;
}

/// <summary>进度消息里 "状态  |  速度" 的拆分（照 Windows 版 Services/Fmt.SplitMsg）。</summary>
public static class TaskMsg
{
    public static void SplitMsg(string? msg, out string status, out string speed)
    {
        status = msg ?? "";
        speed = "";
        if (!string.IsNullOrEmpty(status))
        {
            int i = status.IndexOf("  |  ", StringComparison.Ordinal);
            if (i >= 0)
            {
                speed = status.Substring(i + 5).Trim();
                status = status.Substring(0, i).Trim(' ', '·');
            }
        }
    }

    public static double Clamp(double v, double min, double max)
        => v < min ? min : (v > max ? max : v);
}

/// <summary>任务队列：状态机 + 计数角标，事件驱动 UI 刷新。</summary>
public static class TaskStore
{
    private static readonly List<TaskRow> _rows = new List<TaskRow>();

    public const string PlaceholderTitle = "任务";

    public static IReadOnlyList<TaskRow> Rows => _rows;

    /// <summary>未完成任务数（角标）。</summary>
    public static int RunningCount => _rows.Count((TaskRow r) => !r.Finished);

    public static TaskRow? Newest => _rows.LastOrDefault((TaskRow r) => !r.Finished) ?? _rows.LastOrDefault();

    /// <summary>事件统一回到 UI 线程派发(任务工作体跑在线程池)。</summary>
    private static void Emit(Action a)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) a();
        else Avalonia.Threading.Dispatcher.UIThread.Post(a);
    }

    public static event Action<TaskRow>? Added;

    public static event Action<TaskRow>? Updated;

    public static event Action? Cleared;

    /// <summary>任何变化（新增/更新/清理）都会触发，供角标一处订阅。</summary>
    public static event Action? Changed;

    public static TaskRow? Get(string id) => _rows.FirstOrDefault((TaskRow r) => r.Id == id);

    /// <summary>手动入队一个任务（页面本地任务也走这里）。</summary>
    public static TaskRow Add(string id, string title)
    {
        var row = new TaskRow { Id = id, Title = string.IsNullOrEmpty(title) ? PlaceholderTitle : title };
        _rows.Add(row);
        if (_rows.Count > 60) _rows.RemoveRange(0, _rows.Count - 60);
        Emit(() => { Added?.Invoke(row); Changed?.Invoke(); });
        return row;
    }

    public static void Update(TaskRow row)
    {
        Emit(() => { Updated?.Invoke(row); Changed?.Invoke(); });
    }

    public static void Finish(TaskRow row, bool success, string? message = null)
    {
        row.Finished = true;
        row.Success = success;
        row.Indeterminate = false;
        row.Progress = success ? 100.0 : row.Progress;
        row.Status = !string.IsNullOrEmpty(message) ? message : (success ? "已完成" : "失败");
        row.Speed = "";
        Emit(() => { Updated?.Invoke(row); Changed?.Invoke(); });
    }

    /// <summary>消费后端事件流（task_added/progress/log/finished），照 Windows 版语义。</summary>
    public static void Handle(TaskEvent ev)
    {
        switch (ev.Event)
        {
            case "task_added":
                if (!string.IsNullOrEmpty(ev.TaskId) && Get(ev.TaskId) == null)
                    Add(ev.TaskId, ev.Title);
                break;
            case "progress":
            {
                var row = Get(ev.TaskId);
                if (row != null)
                {
                    TaskMsg.SplitMsg(ev.Message, out string status, out string speed);
                    row.Status = status.Length > 0 ? status : row.Status;
                    row.Speed = speed;
                    row.Indeterminate = ev.Total <= 0;
                    if (ev.Total > 0)
                        row.Progress = TaskMsg.Clamp(ev.Current * 100.0 / ev.Total, 0.0, 100.0);
                    Emit(() => { Updated?.Invoke(row); Changed?.Invoke(); });
                }
                break;
            }
            case "log":
            {
                var row = Get(ev.TaskId);
                if (row != null)
                {
                    row.Append(ev.Text);
                    Emit(() => Updated?.Invoke(row));
                }
                break;
            }
            case "finished":
            {
                var row = Get(ev.TaskId);
                if (row != null)
                    Finish(row, ev.Success, ev.Message);
                break;
            }
        }
    }

    public static void ClearFinished()
    {
        _rows.RemoveAll((TaskRow r) => r.Finished);
        Emit(() => { Cleared?.Invoke(); Changed?.Invoke(); });
    }

    public static bool Remove(string id)
    {
        var row = Get(id);
        if (row == null || !row.Finished) return false;
        _rows.Remove(row);
        Emit(() => { Cleared?.Invoke(); Changed?.Invoke(); });
        return true;
    }
}
