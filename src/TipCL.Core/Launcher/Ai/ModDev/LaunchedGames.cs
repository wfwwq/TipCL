// AI3-moddev:本启动器拉起的游戏进程登记表(按启动任务 task_id → 进程 PID)。 // i18n:ignore-file(只有日志 / 工具结果里的中文)
//
// 为什么要它:LaunchCore 原来只记「当前游戏进程」一个,stop_game 关的是它。开了「允许多开」、用户同时在玩
// 别的实例时,run_test_launch 结束时调 stop_game 可能关掉的是用户自己那一局。现在:
//   · LaunchCore(App)每拉起一个游戏进程就按它的启动任务 id 登记到这里,进程退出后注销;
//   · run_test_launch 的默认驱动只按**它自己那次 launch_game 返回的 task_id** 找进程、只杀那一个
//     (连同子进程树),找不到就不杀 —— 绝不退回到「关当前游戏」;
//   · 游戏进程还没起来(还在补文件 / 下 Java)时由驱动先取消那个启动任务,LaunchCore 登记后发现任务已取消会自己关掉。
// 测试用 ITrackedGameProcess 的假实现模拟多开(不真起任何进程)。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace TipCL.Core.Launcher.Ai;

/// <summary>被登记的游戏进程(产品是 <see cref="SystemGameProcess"/>;测试注入假的)。</summary>
public interface ITrackedGameProcess
{
    long Pid { get; }

    bool HasExited { get; }

    /// <summary>结束进程(连同它拉起的子进程)。</summary>
    void Kill();
}

/// <summary>System.Diagnostics.Process 的包装。</summary>
public sealed class SystemGameProcess : ITrackedGameProcess
{
    private readonly Process _proc;
    private readonly long _pid;

    public SystemGameProcess(Process proc)
    {
        _proc = proc;
        try
        {
            _pid = proc.Id;
        }
        catch (Exception)
        {
            _pid = 0;
        }
    }

    public long Pid => _pid;

    public bool HasExited
    {
        get
        {
            try
            {
                return _proc.HasExited;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    public void Kill()
    {
        if (!HasExited)
        {
            _proc.Kill(entireProcessTree: true);
        }
    }

    /// <summary>是不是包着这个 Process 对象(注销时只注销自己登记的那个)。</summary>
    public bool Wraps(Process p) => ReferenceEquals(_proc, p);
}

/// <summary>按启动任务 id 记录本启动器拉起的游戏进程。线程安全。</summary>
public static class LaunchedGames
{
    /// <summary>一条登记。</summary>
    public sealed class Entry
    {
        public string TaskId = "";
        public ITrackedGameProcess Proc = null!;
        public string Instance = "";
        public string Version = "";
        public DateTime StartedAt;
    }

    private static readonly object _gate = new();
    private static readonly Dictionary<string, Entry> _byTask = new(StringComparer.Ordinal);

    /// <summary>登记(同一任务重复登记以后一次为准)。taskId 为空时不登记。</summary>
    public static void Register(string taskId, ITrackedGameProcess proc, string instance = "", string version = "")
    {
        if (string.IsNullOrEmpty(taskId) || proc == null)
        {
            return;
        }
        lock (_gate)
        {
            _byTask[taskId] = new Entry { TaskId = taskId, Proc = proc, Instance = instance ?? "", Version = version ?? "", StartedAt = DateTime.Now };
        }
    }

    /// <summary>注销:只有登记的还是这个进程时才删(防止同 id 新登记被旧的收尾误删)。</summary>
    public static void Unregister(string taskId, ITrackedGameProcess proc)
    {
        if (string.IsNullOrEmpty(taskId))
        {
            return;
        }
        lock (_gate)
        {
            if (_byTask.TryGetValue(taskId, out Entry? e) && SameProc(e.Proc, proc))
            {
                _byTask.Remove(taskId);
            }
        }
    }

    private static bool SameProc(ITrackedGameProcess a, ITrackedGameProcess b)
        => ReferenceEquals(a, b) || (a.Pid != 0 && a.Pid == b.Pid);

    public static Entry? Get(string taskId)
    {
        if (string.IsNullOrEmpty(taskId))
        {
            return null;
        }
        lock (_gate)
        {
            return _byTask.TryGetValue(taskId, out Entry? e) ? e : null;
        }
    }

    /// <summary>某个启动任务拉起的游戏进程 PID;没有(还没起来 / 已退出注销 / 不是本启动器拉起的)= 0。</summary>
    public static long PidOf(string taskId) => Get(taskId)?.Proc.Pid ?? 0;

    /// <summary>该任务登记的进程还活着。</summary>
    public static bool IsAlive(string taskId)
    {
        Entry? e = Get(taskId);
        return e != null && !e.Proc.HasExited;
    }

    /// <summary>
    /// 只关这个启动任务拉起的那个进程。返回 stopped / already_exited / not_tracked / kill_failed。
    /// not_tracked 时**什么也不杀**(调用方不应退回到「关当前游戏」)。
    /// </summary>
    public static string StopTask(string taskId)
    {
        Entry? e = Get(taskId);
        return e == null ? "not_tracked" : KillEntry(e);
    }

    /// <summary>按 PID 关,但只关登记表里有的(不是本启动器拉起的进程一律不碰)。</summary>
    public static string StopPid(long pid)
    {
        if (pid <= 0)
        {
            return "not_tracked";
        }
        Entry? e;
        lock (_gate)
        {
            e = _byTask.Values.FirstOrDefault(x => x.Proc.Pid == pid);
        }
        return e == null ? "not_tracked" : KillEntry(e);
    }

    private static string KillEntry(Entry e)
    {
        if (e.Proc.HasExited)
        {
            return "already_exited";
        }
        try
        {
            e.Proc.Kill();
            return "stopped";
        }
        catch (Exception ex)
        {
            Utils.log.Warning("关闭游戏进程 %s 失败: %s", e.Proc.Pid, ex.Message);
            return e.Proc.HasExited ? "already_exited" : "kill_failed";
        }
    }

    /// <summary>还活着的登记(诊断 / 测试用)。</summary>
    public static List<Entry> Alive()
    {
        lock (_gate)
        {
            return _byTask.Values.Where(e => !e.Proc.HasExited).ToList();
        }
    }

    /// <summary>清空登记表(只给测试用;不杀任何进程)。</summary>
    public static void ResetForTests()
    {
        lock (_gate)
        {
            _byTask.Clear();
        }
    }
}
