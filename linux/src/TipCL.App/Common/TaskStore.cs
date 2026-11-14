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
    private readonly object _logGate = new();

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

    /// <summary>
    /// 日志正文的快照。页面上在 UI 线程读它,而 <c>Append</c> 跑在任务的工作线程上 ——
    /// <see cref="StringBuilder"/> 不是线程安全的,直接 <c>Log.ToString()</c> 会和 Append 撞车
    /// (撕裂的文本,极端情况抛异常)。读的只是一行要显示的摘要,给个快照最省事。
    /// </summary>
    public string LogText
    {
        get
        {
            lock (_logGate) return Log.ToString();
        }
    }

    /// <summary>派生阶段：Finished → Finished；Indeterminate 且 Progress≤0 视为排队。</summary>
    public TaskPhase Phase => Finished ? TaskPhase.Finished
        : (Indeterminate && Progress <= 0 && LogLines == 0 ? TaskPhase.Queued : TaskPhase.Running);

    public void Append(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        lock (_logGate)
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
    /// <summary>
    /// <c>_rows</c> 的守卫。这个列表**同时被 UI 线程和任务的工作线程碰**（见 D-54）:
    /// 工作线程在 <c>Add</c> / <c>ClearFinished</c> / <c>Remove</c> 里增删,UI 线程在
    /// <c>RunningCount</c> / <c>Rows</c> / <c>Newest</c> / <c>Get</c> 里遍历 —— 不加锁时
    /// 就是 <c>InvalidOperationException: Collection was modified</c>（R21b 验收跑第二遍
    /// 测试时真的撞上过一次,栈顶 <c>RunningCount</c> ← <c>TasksPage.Sync</c>）。
    /// </summary>
    private static readonly object _gate = new();

    private static readonly List<TaskRow> _rows = new List<TaskRow>();

    public const string PlaceholderTitle = "任务";

    /// <summary>队列快照。给的是 <c>ToArray()</c> 的结果,调用方遍历期间不受增删影响。</summary>
    public static IReadOnlyList<TaskRow> Rows
    {
        get
        {
            lock (_gate) return _rows.ToArray();
        }
    }

    /// <summary>未完成任务数（角标）。</summary>
    public static int RunningCount
    {
        get
        {
            lock (_gate)
            {
                int n = 0;
                foreach (TaskRow r in _rows)
                    if (!r.Finished) n++;
                return n;
            }
        }
    }

    public static TaskRow? Newest
    {
        get
        {
            lock (_gate)
            {
                for (int i = _rows.Count - 1; i >= 0; i--)
                    if (!_rows[i].Finished) return _rows[i];
                return _rows.Count > 0 ? _rows[_rows.Count - 1] : null;
            }
        }
    }

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

    public static TaskRow? Get(string id)
    {
        lock (_gate)
        {
            foreach (TaskRow r in _rows)
                if (r.Id == id) return r;
            return null;
        }
    }

    /// <summary>手动入队一个任务（页面本地任务也走这里）。</summary>
    public static TaskRow Add(string id, string title)
    {
        var row = new TaskRow { Id = id, Title = string.IsNullOrEmpty(title) ? PlaceholderTitle : title };
        lock (_gate)
        {
            _rows.Add(row);
            if (_rows.Count > 60) _rows.RemoveRange(0, _rows.Count - 60);
        }
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
        lock (_gate) _rows.RemoveAll((TaskRow r) => r.Finished);
        Emit(() => { Cleared?.Invoke(); Changed?.Invoke(); });
    }

    public static bool Remove(string id)
    {
        TaskRow? row;
        lock (_gate)
        {
            row = null;
            foreach (TaskRow r in _rows)
                if (r.Id == id) { row = r; break; }
            if (row == null || !row.Finished) return false;
            _rows.Remove(row);
        }
        Emit(() => { Cleared?.Invoke(); Changed?.Invoke(); });
        return true;
    }
}
