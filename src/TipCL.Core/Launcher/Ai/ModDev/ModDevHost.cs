// AI2-moddev-mcp:模组开发测试环境的注入点与后台任务表。 // i18n:ignore-file(这里的中文是工具结果 / 任务步骤文字,回给模型与外部 agent)
//
// 注入点放在这个独立静态类里(不放页面类):测试在任意线程设置它们都不会触发 App 层页面的静态初始化。
//   · GameInstaller / DependencyInstaller:create_test_instance 装 MC 本体与依赖模组(默认走 Core 的
//     GameInstall.InstallGame / Mods.InstallModFromSource;测试注入假的,不联网);
//   · LaunchDriverFactory:run_test_launch 怎么起游戏、读日志、关游戏(默认 BackendLaunchDriver 走 IApi 的
//     launch_game / stop_game;**测试一律注入假驱动,绝不真启动游戏**);
//   · HostTaskProbe:App 注入,按 task_id 读启动器自己的任务表(TaskStore),供 get_task_status 查
//     install_* / launch_game 起的任务,以及默认驱动读游戏输出。
//
// ModDevTasks:本包自己的后台任务(create_test_instance 的安装、run_test_launch)。外部 MCP 调用方拿到 task_id 后
// 用 get_task_status 查进度(选「查询工具」而不是 MCP progress notification:本机 MCP Server 只有 POST,没有 SSE 流,
// 推不了通知;查询工具可测、两种客户端都能用)。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>模组开发测试环境的注入点(测试替换;产品由 App 在启动时设 <see cref="HostTaskProbe"/>)。</summary>
public static class ModDevHost
{
    /// <summary>装 MC 本体 + 加载器:(实例, mc 版本, 加载器, 加载器版本, 日志, 取消) → 装好的版本 id。null = 用默认实现。</summary>
    public static Func<Instance, string, string, string, Action<string>, CancellationToken, string>? GameInstaller { get; set; }

    /// <summary>装一个依赖模组:(实例, slug, mc 版本, 加载器, 日志, 取消) → 装好的文件名。null = 用默认实现。</summary>
    public static Func<Instance, string, string, string, Action<string>, CancellationToken, string>? DependencyInstaller { get; set; }

    /// <summary>run_test_launch 的启动驱动工厂。null = 默认驱动(经 IApi 的 launch_game / stop_game)。</summary>
    public static Func<ToolRunner.IBackend, ITestLaunchDriver>? LaunchDriverFactory { get; set; }

    /// <summary>
    /// 按 task_id 读启动器任务表(App 的 TaskStore)。返回 {title, status, finished, success, progress, log};
    /// 没有这个任务返回 null。App 启动时注入;无界面 / 测试时为 null。
    /// </summary>
    public static Func<string, PyDict?>? HostTaskProbe { get; set; }

    /// <summary>把注入点全部复原(测试收尾用)。</summary>
    public static void Reset()
    {
        GameInstaller = null;
        DependencyInstaller = null;
        LaunchDriverFactory = null;
        HostTaskProbe = null;
    }

    internal static string InstallGame(Instance inst, string mc, string loader, string loaderVersion, Action<string> log, CancellationToken ct)
    {
        if (GameInstaller != null)
        {
            return GameInstaller(inst, mc, loader, loaderVersion, log, ct);
        }
        string lastMsg = "";
        var dm = new DownloadManager(8L, (msg, cur, total) =>
        {
            string m = (msg ?? "").Trim();
            if (m.Length > 0 && m != lastMsg)
            {
                lastMsg = m;
                log(m);
            }
        }, () => ct.IsCancellationRequested, null, ct);
        var installer = new Installer(inst, dm, dm.on_progress, () => ct.IsCancellationRequested);
        string label = loader switch
        {
            "fabric" => "fabric",
            "quilt" => "quilt",
            "forge" => "forge",
            "neoforge" => "neoforge",
            _ => "无",
        };
        return GameInstall.InstallGame(installer, mc, label, loaderVersion.Length > 0 ? loaderVersion : null);
    }

    internal static string InstallDependency(Instance inst, string slug, string mc, string loader, Action<string> log, CancellationToken ct)
    {
        if (DependencyInstaller != null)
        {
            return DependencyInstaller(inst, slug, mc, loader, log, ct);
        }
        var dm = new DownloadManager(4L, (msg, cur, total) => { }, () => ct.IsCancellationRequested, null, ct);
        string modsDir = PPath.Join(inst.path, "mods");
        Utils.EnsureDir(modsDir);
        PyDict res = Mods.InstallModFromSource(dm, slug, inst, mc, loader, dm.on_progress, null, modsDir);
        return Py.S(Py.Or(res.Get("file"), res.Get("filename"), slug));
    }
}

/// <summary>本包的一个后台任务(create_test_instance 的安装 / run_test_launch)。</summary>
public sealed class ModDevTask
{
    private readonly object _gate = new();
    private readonly List<string> _log = new();

    internal ModDevTask(string id, string kind, string title)
    {
        Id = id;
        Kind = kind;
        Title = title;
    }

    public string Id { get; }

    /// <summary>发起它的工具名(create_test_instance / run_test_launch)。</summary>
    public string Kind { get; }

    public string Title { get; }

    /// <summary>running / succeeded / failed / cancelled。</summary>
    public string State { get; private set; } = "running";

    public string Step { get; private set; } = "";

    /// <summary>0..100;-1 = 不确定。</summary>
    public int Progress { get; private set; } = -1;

    public DateTime StartedAt { get; } = DateTime.Now;

    public DateTime? FinishedAt { get; private set; }

    public PyDict? Result { get; private set; }

    public string Error { get; private set; } = "";

    public bool Finished => State != "running";

    internal CancellationTokenSource Cts { get; } = new();

    internal TaskCompletionSource<bool> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public double ElapsedSeconds => Math.Round(_clock.Elapsed.TotalSeconds, 1);

    /// <summary>更新当前步骤(<paramref name="log"/> 为 true 时同时记一行日志)。</summary>
    public void SetStep(string step, int progress = -1, bool log = true)
    {
        lock (_gate)
        {
            Step = step ?? "";
            if (progress >= 0)
            {
                Progress = Math.Clamp(progress, 0, 100);
            }
        }
        if (log)
        {
            Log(step ?? "");
        }
    }

    public void Log(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }
        lock (_gate)
        {
            _log.Add(line);
            if (_log.Count > 400)
            {
                _log.RemoveRange(0, _log.Count - 300);
            }
        }
    }

    public List<string> LogTail(int n)
    {
        lock (_gate)
        {
            return _log.Skip(Math.Max(0, _log.Count - Math.Max(0, n))).ToList();
        }
    }

    internal void Finish(string state, PyDict? result, string error)
    {
        lock (_gate)
        {
            if (Finished)
            {
                return;
            }
            Result = result;
            Error = error ?? "";
            State = state;
            if (state == "succeeded")
            {
                Progress = 100;
            }
            FinishedAt = DateTime.Now;
            _clock.Stop();
        }
        Done.TrySetResult(true);
    }

    public PyDict ToDict(int logLines)
    {
        lock (_gate)
        {
            var d = PyDict.Of(
                "ok", true,
                "task_id", Id,
                "source", "moddev",
                "kind", Kind,
                "title", Title,
                "state", State,
                "finished", Finished,
                "success", State == "succeeded",
                "step", Step,
                "progress", (long)Progress,
                "elapsed_s", ElapsedSeconds,
                "log_tail", _log.Skip(Math.Max(0, _log.Count - Math.Max(0, logLines))).Cast<object>().ToList());
            if (Result != null)
            {
                d["result"] = Result;
            }
            if (Error.Length > 0)
            {
                d["error_detail"] = Error;
            }
            return d;
        }
    }
}

/// <summary>本包后台任务的进程内登记表 + get_task_status 的实现。</summary>
public static class ModDevTasks
{
    private static readonly object _gate = new();
    private static readonly List<ModDevTask> _tasks = new();

    /// <summary>
    /// 起一个后台任务:立即返回任务对象(id 可交给调用方),工作体在线程池上跑。
    /// 工作体返回结果字典;字典里 ok=false 记为 failed,抛异常记为 failed,取消记为 cancelled。
    /// </summary>
    public static ModDevTask Start(string kind, string title, Func<ModDevTask, CancellationToken, Task<PyDict>> work)
    {
        var task = new ModDevTask("dev-" + Guid.NewGuid().ToString("N").Substring(0, 12), kind, title);
        lock (_gate)
        {
            _tasks.Add(task);
            // 只留最近 50 个:没结束的不丢
            while (_tasks.Count > 50)
            {
                int i = _tasks.FindIndex(t => t.Finished);
                if (i < 0)
                {
                    break;
                }
                _tasks.RemoveAt(i);
            }
        }
        CancellationToken ct = task.Cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                PyDict result = await work(task, ct).ConfigureAwait(false);
                bool ok = !(result.Get("ok") is false);
                task.Finish(ok ? "succeeded" : "failed", result, ok ? "" : Py.S(Py.Or(result.Get("message"), result.Get("error"))));
            }
            catch (OperationCanceledException)
            {
                task.Finish("cancelled", null, "已取消");
            }
            catch (Exception ex)
            {
                task.Log("失败: " + ex.Message);
                task.Finish("failed", null, Py.ExcStr(ex));
            }
        }, CancellationToken.None);
        return task;
    }

    public static ModDevTask? Get(string id)
    {
        lock (_gate)
        {
            return _tasks.FirstOrDefault(t => t.Id == id);
        }
    }

    /// <summary>某类任务里还没结束的那一个(run_test_launch 同一时间只允许一个)。</summary>
    public static ModDevTask? RunningOf(string kind)
    {
        lock (_gate)
        {
            return _tasks.LastOrDefault(t => t.Kind == kind && !t.Finished);
        }
    }

    public static bool Cancel(string id)
    {
        ModDevTask? t = Get(id);
        if (t == null || t.Finished)
        {
            return false;
        }
        t.Cts.Cancel();
        return true;
    }

    /// <summary>等任务结束,最多 <paramref name="timeout"/>;返回是否已结束。</summary>
    public static async Task<bool> WaitAsync(ModDevTask task, TimeSpan timeout, CancellationToken ct = default)
    {
        if (task.Finished)
        {
            return true;
        }
        Task delay = Task.Delay(timeout < TimeSpan.Zero ? TimeSpan.Zero : timeout, ct);
        await Task.WhenAny(task.Done.Task, delay).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return task.Finished;
    }

    /// <summary>
    /// get_task_status:先查本包的任务表,再查启动器任务表(HostTaskProbe);<paramref name="waitSeconds"/> &gt; 0 时
    /// 最多等这么久直到任务结束再回(减少外部调用方的轮询次数)。
    /// </summary>
    public static async Task<PyDict> StatusAsync(string id, int logLines, double waitSeconds, CancellationToken ct = default)
    {
        id = (id ?? "").Trim();
        if (id.Length == 0)
        {
            throw new ValueError("缺少 task_id");
        }
        logLines = Math.Clamp(logLines, 0, 200);
        double wait = Math.Clamp(waitSeconds, 0, 60);
        ModDevTask? own = Get(id);
        if (own != null)
        {
            if (wait > 0)
            {
                await WaitAsync(own, TimeSpan.FromSeconds(wait), ct).ConfigureAwait(false);
            }
            return own.ToDict(logLines);
        }
        Func<string, PyDict?>? probe = ModDevHost.HostTaskProbe;
        if (probe != null)
        {
            var sw = Stopwatch.StartNew();
            while (true)
            {
                PyDict? row = probe(id);
                if (row == null)
                {
                    break;
                }
                bool finished = Py.Truthy(row.Get("finished"));
                if (finished || sw.Elapsed.TotalSeconds >= wait)
                {
                    return HostStatus(id, row, logLines);
                }
                await Task.Delay(250, ct).ConfigureAwait(false);
            }
        }
        return PyDict.Of("ok", false, "error", "not_found", "task_id", id,
            "message", "没有这个任务(可能是启动器重启过,或任务已被清理)。task_id 来自 install_* / launch_game / create_test_instance / run_test_launch 的返回");
    }

    private static PyDict HostStatus(string id, PyDict row, int logLines)
    {
        bool finished = Py.Truthy(row.Get("finished"));
        bool success = Py.Truthy(row.Get("success"));
        string log = Py.S(row.Get("log"));
        List<string> lines = log.Replace("\r", "").Split('\n').Where(l => l.Length > 0).ToList();
        return PyDict.Of(
            "ok", true,
            "task_id", id,
            "source", "launcher",
            "title", Py.S(row.Get("title")),
            "state", !finished ? "running" : (success ? "succeeded" : "failed"),
            "finished", finished,
            "success", finished && success,
            "step", Py.S(row.Get("status")),
            "progress", Py.Or(row.Get("progress"), -1L),
            "log_tail", lines.Skip(Math.Max(0, lines.Count - logLines)).Cast<object>().ToList());
    }
}
