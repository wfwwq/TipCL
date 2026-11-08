using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;

namespace TipCL.App.Common;

/// <summary>
/// 进程内 API 门面：M2 页面统一从这里调后端能力，方法名保持 Windows 版
/// Core/BackendApi.cs 的 snake_case 契约（M3 若补 Python 桥可直接换成 HTTP 实现）。
/// 底层直调 TipCL.Core 已搬类（Manifest/LoaderMeta/Installer/GameInstall/Instances/Mods/Config）。
/// </summary>
public interface IApi
{
    /// <summary>调用一个 snake_case 方法。不存在的语义/失败会抛异常。</summary>
    Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default);

    /// <summary>失败回默认值不报错（照 Windows 版 TryCallAsync 语义）。</summary>
    Task<T?> TryCallAsync<T>(string method, T? fallback = default, object? args = null, CancellationToken ct = default);

    /// <summary>
    /// 起一个后台任务：登记进 TaskStore、回调里 Report 进度，结束（含异常）自动 finished。
    /// 返回的 TaskRow 已入队，页面可立即用它刷新任务卡。
    /// </summary>
    Task<TaskRow> StartTaskAsync(string title, Func<TaskProgress, CancellationToken, Task> work, CancellationToken ct = default);

    /// <summary>事件流（progress/log/finished/game_started/game_exited/login_* 等），UI 线程外触发。</summary>
    event Action<ApiEvent>? BridgeEvent;

    /// <summary>触发一个事件（实现侧转调 BridgeEvent）。</summary>
    void RaiseEvent(ApiEvent ev);

    /// <summary>取消一个进行中的任务（cancel_task）。</summary>
    bool CancelTask(string taskId);
}

/// <summary>任务进度汇报器（StartTaskAsync 的 work 回调拿到）。</summary>
public sealed class TaskProgress
{
    private readonly string _taskId;
    private readonly IApi _api;

    internal TaskProgress(string taskId, IApi api) { _taskId = taskId; _api = api; }

    public string TaskId => _taskId;

    public void Report(string message, long current = 0, long total = 0)
    {
        var row = TaskStore.Get(_taskId);
        if (row != null)
        {
            TaskStore.Handle(new TaskEvent { Event = "progress", TaskId = _taskId, Message = message, Current = current, Total = total });
        }
        _api.RaiseEvent(new ApiEvent { Event = "progress", TaskId = _taskId, Message = message, Current = current, Total = total });
    }

    public void Log(string text)
    {
        TaskStore.Handle(new TaskEvent { Event = "log", TaskId = _taskId, Text = text });
        _api.RaiseEvent(new ApiEvent { Event = "log", TaskId = _taskId, Text = text });
    }
}

/// <summary>事件流载体（对应 Windows 版 BridgeEvent 字段子集）。</summary>
public sealed class ApiEvent
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

/// <summary>
/// IApi 的进程内默认实现。耗时操作一律 Task.Run。
/// M2 阶段尚未搬的后端能力（登录/启动游戏/新闻等）按方法名返回安全桩值，页面 TryCallAsync 路径不受影响。
/// </summary>
public sealed class InProcessApi : IApi
{
    public static InProcessApi Instance { get; } = new();

    public event Action<ApiEvent>? BridgeEvent;

    public void RaiseEvent(ApiEvent ev) => BridgeEvent?.Invoke(ev);

    private readonly Dictionary<string, CancellationTokenSource> _tasks = new();

    // ───────────────────────────── 调用入口 ─────────────────────────────

    public async Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
    {
        object? result = await Task.Run(() => CallCore(method, ToDict(args), ct), ct).ConfigureAwait(false);
        if (result is T typed) return typed;
        if (result is null) return default;
        // PyDict / List<object> 等动态结构：M2 页面按 object 接
        throw new InvalidCastException($"API {method} 返回 {result.GetType().Name}，无法转为 {typeof(T).Name}");
    }

    public async Task<T?> TryCallAsync<T>(string method, T? fallback = default, object? args = null, CancellationToken ct = default)
    {
        try
        {
            object? result = await Task.Run(() => CallCore(method, ToDict(args), ct), ct).ConfigureAwait(false);
            if (result is T typed) return typed;
            return result is null ? fallback : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    public async Task<TaskRow> StartTaskAsync(string title, Func<TaskProgress, CancellationToken, Task> work, CancellationToken ct = default)
    {
        string id = "task-" + Guid.NewGuid().ToString("N").Substring(0, 12);
        var row = TaskStore.Add(id, title);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_tasks) _tasks[id] = cts;
        RaiseEvent(new ApiEvent { Event = "task_added", TaskId = id, Title = row.Title });
        _ = Task.Run(async () =>
        {
            var progress = new TaskProgress(id, this);
            try
            {
                await work(progress, cts.Token).ConfigureAwait(false);
                TaskStore.Finish(row, true);
                RaiseEvent(new ApiEvent { Event = "finished", TaskId = id, Success = true });
            }
            catch (OperationCanceledException)
            {
                TaskStore.Finish(row, false, "已取消");
                RaiseEvent(new ApiEvent { Event = "finished", TaskId = id, Success = false, Message = "已取消" });
            }
            catch (Exception ex)
            {
                TaskStore.Finish(row, false, ex.Message);
                RaiseEvent(new ApiEvent { Event = "finished", TaskId = id, Success = false, Message = ex.Message });
            }
            finally
            {
                lock (_tasks) _tasks.Remove(id);
            }
        }, CancellationToken.None);
        return row;
    }

    public bool CancelTask(string taskId)
    {
        lock (_tasks)
        {
            if (_tasks.TryGetValue(taskId, out var cts))
            {
                cts.Cancel();
                return true;
            }
        }
        return false;
    }

    // ───────────────────────────── snake_case 方法表 ─────────────────────────────

    private object? CallCore(string method, PyDict args, CancellationToken ct)
    {
        DownloadManager Dm() => new DownloadManager();
        Instance Inst()
        {
            var inst = new Instance(Py.S(args.Get("instance")));
            return inst;
        }

        switch (method)
        {
            // —— 版本清单 ——
            case "get_version_list":
                return Manifest.ListRemoteVersions(Dm(), force: false);
            case "fetch_version_list":
                return Manifest.ListRemoteVersions(Dm(), force: true);
            case "list_loader_versions":
                return LoaderMeta.ListLoaderVersions(Dm(), args.Get("mc_version") ?? args.Get("mc"), args.Get("loader"));

            // —— 实例 ——
            case "get_instances":
            {
                var rows = new List<object>();
                foreach (string name in Instances.ListInstances())
                {
                    var inst = new Instance(name);
                    var d = PyDict.Of(
                        "name", name,
                        "path", inst.path,
                        "is_root", inst.is_root);
                    try { d["versions"] = inst.InstalledIds(); } catch (Exception) { d["versions"] = new List<object>(); }
                    rows.Add(d);
                }
                return rows;
            }
            case "create_instance":
            {
                var inst = Instances.CreateUniqueInstance(args.Get("name"), Py.S(args.Get("fallback")) is { Length: > 0 } f ? f : "游戏");
                return PyDict.Of("name", inst.name, "path", inst.path);
            }
            case "rename_instance":
                Inst().Rename(args.Get("new_name"));
                return PyDict.Of("ok", true);
            case "delete_instance":
                Inst().Delete();
                return PyDict.Of("ok", true);
            case "get_installed_versions":
                return Inst().InstalledIds();

            // —— 下载/安装（长任务，页面应经 StartTask 路径使用同名方法）——
            case "install_game":
            {
                var installer = new Installer(Inst(), Dm(), (msg, cur, total) =>
                    RaiseEvent(new ApiEvent { Event = "progress", Message = msg, Current = cur, Total = total }));
                GameInstall.InstallGame(installer, args.Get("version"), args.Get("loader"), args.Get("loader_version"), args.Get("extra"));
                return PyDict.Of("ok", true);
            }
            case "install_mod":
            {
                // args: instance, urls（或 url）, filename? —— 下载到实例 mods 目录
                var inst = Inst();
                var urls = args.Get("urls") != null ? Mods.ModrinthDownloadUrls(args.Get("urls")) : new List<string> { Py.S(args.Get("url")) };
                if (urls.Count == 0) throw new ValueError("没有可用的下载地址");
                string modsDir = Path.Combine(inst.path, "mods");
                Directory.CreateDirectory(modsDir);
                string dest = Path.Combine(modsDir, Py.S(args.Get("filename")) is { Length: > 0 } fn ? fn : Path.GetFileName(urls[0].Split('?')[0]));
                new DownloadManager(on_progress: (msg, cur, total) =>
                    RaiseEvent(new ApiEvent { Event = "progress", Message = msg, Current = cur, Total = total }))
                    .Download(urls[0], dest);
                return PyDict.Of("ok", true, "file", Path.GetFileName(dest));
            }
            case "list_mods":
            {
                var entries = Mods.ListInstanceModEntries(Inst());
                return entries;
            }
            case "get_installed_mod_entries":
                return Mods.ListInstanceModEntries(Inst());
            case "enable_mod":
                return PyDict.Of("ok", true, "file", Mods.SetModEnabled(Inst(), args.Get("filename"), true));
            case "disable_mod":
                return PyDict.Of("ok", true, "file", Mods.SetModEnabled(Inst(), args.Get("filename"), false));

            // —— 设置 ——
            case "get_setting":
                return Config.CONFIG.Get(Py.S(args.Get("key")), args.Get("dflt"));
            case "get_settings":
                return Config.CONFIG;
            case "update_settings":
            case "save_settings":
            {
                if (args.Get("data") is PyDict data)
                    foreach (var kv in data)
                        Config.CONFIG.Set(kv.Key, kv.Value);
                return PyDict.Of("ok", true);
            }

            // —— 任务 ——
            case "cancel_task":
                return PyDict.Of("ok", CancelTask(Py.S(args.Get("id"))));

            // —— M2/M3 尚未落地的能力：安全桩（TryCallAsync 路径回默认值）——
            case "is_game_running":
                return false;
            case "get_total_playtime":
                return 0L;
            case "cached_news":
            case "fetch_news":
                return new List<object>();
            default:
                throw new NotImplementedException($"IApi 尚未实现方法 {method}（M2-B0 方法表之外，属 M3 或未搬后端能力）");
        }
    }

    // ───────────────────────────── 参数工具 ─────────────────────────────

    /// <summary>匿名对象 / 字典 → PyDict（键保持原样，页面侧用 snake_case 传参）。</summary>
    public static PyDict ToDict(object? args)
    {
        var d = new PyDict();
        if (args == null) return d;
        if (args is PyDict pd)
        {
            foreach (var kv in pd) d[kv.Key] = kv.Value;
            return d;
        }
        if (args is IDictionary<string, object> dict)
        {
            foreach (var kv in dict) d[kv.Key] = kv.Value;
            return d;
        }
        foreach (var p in args.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (p.CanRead && p.GetValue(args) is { } v)
                d[p.Name] = v;
        return d;
    }
}
