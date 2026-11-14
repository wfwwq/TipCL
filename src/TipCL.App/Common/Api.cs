using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.App.Common.Launch;

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

    /// <summary>构造任意 id 的进度汇报器（任务未登记时仅事件流可见，供 LaunchCore 直调/测试用）。</summary>
    public TaskProgress(string taskId, IApi api) { _taskId = taskId; _api = api; }

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
        AppLog.Info($"[{_taskId}] {text}");
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
    /// <summary>设备码登录用:login_code 事件里的用户码与验证地址。</summary>
    public string Code = "";
    public string Uri = "";
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

    public void RaiseEvent(ApiEvent ev)
    {
        // 下载/安装跑在线程池:事件订阅者(页面)要更新 UI,必须回到 UI 线程派发,
        // 否则 Avalonia 跨线程访问会抛异常并把任务打断。
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            BridgeEvent?.Invoke(ev);
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(() => BridgeEvent?.Invoke(ev));
    }

    private readonly Dictionary<string, CancellationTokenSource> _tasks = new();

    // ───────────────────────────── 调用入口 ─────────────────────────────

    public async Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
    {
        object? result = await Task.Run(() => CallCore(method, ToDict(args), ct), ct).ConfigureAwait(false);
        if (result is T typed) return typed;
        if (result is null) return default;
        if (Coerce(typeof(T), result) is T coerced) return coerced;
        throw new InvalidCastException($"API {method} 返回 {result.GetType().Name},无法转为 {typeof(T).Name}");
    }

    public async Task<T?> TryCallAsync<T>(string method, T? fallback = default, object? args = null, CancellationToken ct = default)
    {
        try
        {
            object? result = await Task.Run(() => CallCore(method, ToDict(args), ct), ct).ConfigureAwait(false);
            if (result is T typed) return typed;
            if (result is null) return fallback;
            return Coerce(typeof(T), result) is T coerced ? coerced : fallback;
        }
        catch (Exception ex)
        {
            // 不能静默:页面用 TryCallAsync 的地方(轮询、状态快照)失败后只会表现为「什么都不显示」,
            // 排查时无从下手(AppLog 值 0 也能看 Console)。
            AppLog.Warn($"TryCall {method} 失败,回落默认值: {ex.GetType().Name}: {ex.Message}");
            return fallback;
        }
    }

    public Task<TaskRow> StartTaskAsync(string title, Func<TaskProgress, CancellationToken, Task> work, CancellationToken ct = default)
        => Task.FromResult(StartDetached(title, work, ct));

    /// <summary>
    /// 起任务并**同步**拿到任务行(不回 Task)。登录这类「先拿到 task_id 再靠事件推进」的
    /// 流程需要它:CallCore 是同步方法,只能这样把 id 立刻返回给页面。
    /// </summary>
    private TaskRow StartDetached(string title, Func<TaskProgress, CancellationToken, Task> work, CancellationToken ct = default)
    {
        string id = "task-" + Guid.NewGuid().ToString("N").Substring(0, 12);
        var row = TaskStore.Add(id, title);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_tasks) _tasks[id] = cts;
        RaiseEvent(new ApiEvent { Event = "task_added", TaskId = id, Title = row.Title });
        _ = Task.Run(async () =>
        {
            var progress = new TaskProgress(id, this);
            AppLog.Info($"任务开始 {id} {title}");
            try
            {
                await work(progress, cts.Token).ConfigureAwait(false);
                TaskStore.Finish(row, true);
                AppLog.Info($"任务完成 {id} {title}");
                RaiseEvent(new ApiEvent { Event = "finished", TaskId = id, Success = true });
            }
            catch (OperationCanceledException)
            {
                TaskStore.Finish(row, false, "已取消");
                AppLog.Warn($"任务取消 {id} {title}");
                RaiseEvent(new ApiEvent { Event = "finished", TaskId = id, Success = false, Message = "已取消" });
            }
            catch (Exception ex)
            {
                TaskStore.Finish(row, false, ex.Message);
                AppLog.Error($"任务失败 {id} {title}", ex);
                // 崩溃报告(游戏异常退出)单独缓存一份,UI 侧按 crash 事件弹 CrashUi
                if (ex is GameCrashError gce) RememberCrash(id, gce.Report);
                RaiseEvent(new ApiEvent { Event = "finished", TaskId = id, Success = false, Message = ex.Message });
                if (ex is GameCrashError) RaiseEvent(new ApiEvent { Event = "crash", TaskId = id, Message = ex.Message });
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
            case "install_mod_direct":
            {
                // args: instance, urls（或 url）, filename? —— 下载到实例 mods 目录
                var inst = Inst();
                var urls = args.Get("urls") != null ? Mods.ModrinthDownloadUrls(args.Get("urls")) : new List<string> { Py.S(args.Get("url")) };
                if (urls.Count == 0) throw new ValueError("没有可用的下载地址");
                string modsDirDirect = Path.Combine(inst.path, "mods");
                Directory.CreateDirectory(modsDirDirect);
                string dest = Path.Combine(modsDirDirect, Py.S(args.Get("filename")) is { Length: > 0 } fn ? fn : Path.GetFileName(urls[0].Split('?')[0]));
                new DownloadManager(on_progress: (msg, cur, total) =>
                    RaiseEvent(new ApiEvent { Event = "progress", Message = msg, Current = cur, Total = total }))
                    .Download(urls[0], dest);
                return PyDict.Of("ok", true, "file", Path.GetFileName(dest));
            }
            case "list_mods":
            case "get_installed_mod_entries":
            {
                // version 非空 → 指到该版本的隔离 mods 目录(照 Windows get_installed_mod_entries)
                var modsInst = Inst();
                object modsVer = args.Get("version");
                return Py.Truthy(modsVer)
                    ? Mods.ListModEntriesAt(ModsFolder(modsInst, modsVer))
                    : (object)Mods.ListInstanceModEntries(modsInst);
            }
            case "enable_mod":
                return PyDict.Of("ok", true, "file", Mods.SetModEnabled(Inst(), args.Get("filename"), true, ModsFolder(Inst(), args.Get("version"))));
            case "disable_mod":
                return PyDict.Of("ok", true, "file", Mods.SetModEnabled(Inst(), args.Get("filename"), false, ModsFolder(Inst(), args.Get("version"))));
            case "delete_mod":
                Mods.DeleteMod(Inst(), args.Get("filename"), ModsFolder(Inst(), args.Get("version")));
                RaiseEvent(new ApiEvent { Event = "ui_changed" });
                return PyDict.Of("ok", true);

            // —— 设置 ——
            case "get_setting":
                return Config.CONFIG.Get(Py.S(args.Get("key")), args.Get("dflt"));
            case "get_settings":
                return Config.CONFIG;
            case "update_settings":
            case "save_settings":
            {
                // 照 Windows save_settings:`data` 字典(或散开的 kwargs)合并进配置后**必须落盘**,
                // 否则设置只在内存里生效,重启即丢。
                if (args.Get("data") is PyDict data)
                    foreach (var kv in data)
                        Config.CONFIG.Set(kv.Key, kv.Value);
                foreach (var kv in args)
                    if (kv.Key != "data")
                        Config.CONFIG.Set(kv.Key, kv.Value);
                Config.CONFIG.Save();
                return PyDict.Of("ok", true);
            }

            // —— 任务 ——
            case "cancel_task":
                return PyDict.Of("ok", CancelTask(Py.S(args.Get("id"))));

            // —— M3-C0：版本行 / 存档 / 内容文件 / 实例文件夹（B2 报告的 9 个缺口）——
            case "get_version_rows":
                return VersionRows(Inst(), Py.Truthy(args.Get("include_hidden")));
            case "list_saves":
                return Saves.ListSaves(Inst(), args.Get("version"));
            case "open_instance_folder":
            {
                var inst = Inst();
                Crash.ShellOpen(inst.path);
                return inst.path;
            }
            case "uninstall_version":
            {
                // spec 形如 "实例名 / 版本名" 或裸版本名（对齐 Windows 版 uninstall_version）
                string spec = Py.Strip(Py.Str(args.Get("spec") ?? args.Get("version") ?? ""));
                int idx = spec.IndexOf(" / ", StringComparison.Ordinal);
                string instName, ver;
                if (idx >= 0) { instName = spec.Substring(0, idx); ver = spec.Substring(idx + 3); }
                else { instName = Py.S(Config.CONFIG.Get("default_instance", "default")); ver = spec; }
                new Installer(InstancesApi.EnsureInstance(instName)).UninstallVersion(Py.Strip(ver));
                return PyDict.Of("ok", true);
            }
            case "import_existing_folder":
            {
                string src = PPath.Norm(Py.SS(args.Get("path")));
                if (src.Length == 0 || !LocalFs.IsDir(src) || !LocalFs.IsDir(PPath.Join(src, "versions")))
                    throw new ValueError(I18nCore.Tr("选中的文件夹不是游戏目录（需要包含 versions 子目录）"));
                string srcKey = PPath.Abs(src).TrimEnd('\\', '/');
                foreach (string item in Instances.ListInstances())
                {
                    string other = PPath.Abs(Instances.GetInstancePath(item)).TrimEnd('\\', '/');
                    if (string.Equals(other, srcKey, Utils.IS_WINDOWS ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        throw new ValueError(I18nCore.Tr("这个文件夹已经在实例列表里了"));
                }
                if (OfficialMigrate.ScanVersions(src).Count == 0)
                    throw new ValueError(I18nCore.Tr("这个文件夹的 versions 里没有可用的版本（需要 versions/<版本名>/<版本名>.json）"));
                string newName = Instances.UniqueInstanceName(
                    Py.Truthy(args.Get("name")) ? Py.SS(args.Get("name")) : new PPath(src).Name);
                List<object> imported = Py.ListOr(Py.Or(
                    OfficialMigrate.Migrate(src, newName, wantVersions: true, wantAssets: true, wantAccounts: false)["versions"], new List<object>()));
                Config.CONFIG.Set("default_instance", newName);
                Config.CONFIG.Save();
                return PyDict.Of("ok", true, "instance", newName, "versions", (long)imported.Count,
                    "message", Py.Fmt(I18nCore.Tr("已导入 {0} 个版本"), (long)imported.Count));
            }
            case "get_installed_resourcepacks":
                return ContentNames(Inst(), "resourcepacks", args.Get("version"));
            case "get_installed_shaders":
                return ContentNames(Inst(), "shaderpacks", args.Get("version"));
            case "delete_resourcepack":
                Mods.DeleteContentFile(Inst(), "resourcepacks", args.Get("filename"), args.Get("version"));
                RaiseEvent(new ApiEvent { Event = "ui_changed" });
                return PyDict.Of("ok", true);
            case "delete_shader":
                Mods.DeleteContentFile(Inst(), "shaderpacks", args.Get("filename"), args.Get("version"));
                RaiseEvent(new ApiEvent { Event = "ui_changed" });
                return PyDict.Of("ok", true);

            // —— M3-C0：Java 管理 ——
            case "get_javas":
            case "get_java_list":
            {
                List<object> javas = Py.Truthy(args.Get("scan_system")) ? Java.AllJavas() : Java.ListInstalledJavas();
                var rows = new List<object>();
                foreach (PyDict item in javas)
                {
                    rows.Add(PyDict.Of(
                        "name", Py.Or(item.Get("name"), "Java " + Py.Str(item.Get("major"))),
                        "major", Py.Str(Py.Or(item.Get("major"), "?")),
                        "path", Py.Or(Py.Or(item.Get("exe"), item.Get("path")), "")));
                }
                return rows;
            }
            case "normalize_java_pref":
                return LaunchCore.NormalizeJavaPref(args.Get("java"));
            case "java_combo_options":
            {
                // 照 Windows Core/BackendApi.java_combo_options:首项「自动选择」+ 去重后的已装 Java +
                // 实例已保存但不在列表里的那个(标注「已保存 (…)」)。
                var opts = new List<object> { PyDict.Of("label", "自动选择", "value", "自动选择") };
                var seen = new HashSet<string>();
                List<object> javas = Py.Truthy(args.Get("scan_system")) ? Java.AllJavas() : Java.ListInstalledJavas();
                foreach (PyDict item in javas)
                {
                    string path = Py.S(Py.Or(Py.Or(item.Get("exe"), item.Get("path")), ""));
                    if (path.Length > 0 && seen.Add(path))
                        opts.Add(PyDict.Of("label", Py.Or(item.Get("name"), path), "value", path));
                }
                string saved = new Instance(Py.S(args.Get("instance"))).JavaPref();
                if (saved != "自动选择" && !seen.Contains(saved))
                    opts.Add(PyDict.Of("label", "已保存 (" + saved + ")", "value", saved));
                return opts;
            }
            case "java_combo_label_for":
            {
                string cur = new Instance(Py.S(args.Get("instance"))).JavaPref();
                foreach (object item in Py.ListOr(args.Get("options")))
                    if (item is PyDict d && Py.Eq(d.Get("value"), cur))
                        return d.Get("label");
                return "自动选择";
            }
            case "get_instance_java":
                return new Instance(Py.S(args.Get("name"))).JavaPref();
            case "set_instance_java":
                new Instance(Py.S(args.Get("name"))).SetJavaPref(LaunchCore.NormalizeJavaPref(args.Get("java")));
                return PyDict.Of("ok", true);
            case "java_download":
            {
                // 同步下载（页面经 StartTaskAsync 包一层即得任务卡）；vendor 仅支持 adoptium，其余回落
                long major = Py.Int(args.Get("major"));
                string path = Java.InstallAdoptium(new DownloadManager(2L), major, (msg, cur, total) =>
                    RaiseEvent(new ApiEvent { Event = "progress", Message = msg, Current = cur, Total = total }));
                return PyDict.Of("ok", true, "path", path);
            }
            case "java_version_list":
                return Java.FindSystemJavas();

            // —— M4-H P3:Java 页(发行版列表 / 标签 / 下载运行时)——
            case "java_vendor_list":
                return Java.JavaVendorList();
            case "java_vendor_label":
                return Java.JavaVendorLabel(args.Get("vendor"));
            case "download_java":
            {
                // 照 Windows download_java:adoptium 走 Adoptium API,其它发行版转 install_java;
                // 两者都是后台任务 + 立刻返回 task_id。
                string vendor = Py.Strip(Py.S(Py.Or(args.Get("vendor"), "adoptium")));
                if (vendor.Length == 0) vendor = "adoptium";
                if (vendor != "adoptium") return InstallJavaVendorTask(Py.Int(args.Get("major")), vendor);
                long major = Py.Int(args.Get("major"));
                return StartDetached("下载 Java " + major, (prog, ct) =>
                {
                    var dm = new DownloadManager(2L, on_progress: (m, c, t) => prog.Report(m, c, t));
                    prog.Log("下载 Adoptium Java " + major);
                    string path = Java.InstallAdoptium(dm, major, dm.on_progress);
                    prog.Log("Java " + major + " 就绪: " + path);
                    RaiseEvent(new ApiEvent { Event = "ui_changed" });
                    return Task.CompletedTask;
                }).Id;
            }
            case "install_java":
            {
                string vendor = Py.S(Py.Or(args.Get("vendor"), "adoptium"));
                if (vendor.Length == 0) vendor = "adoptium";
                return InstallJavaVendorTask(Py.Int(args.Get("major")), vendor);
            }

            // —— M3-C0：启动 ——
            case "preflight_launch":
            {
                object java0 = args.Get("java");
                string jexe = "";
                if (Py.Truthy(java0) && !Py.Eq(java0, "自动选择") && !Py.Eq(java0, "auto") && !Py.Eq(java0, "default") && !Py.Eq(java0, ""))
                    jexe = Py.Str(java0);
                return Preflight.CheckLaunch(InstancesApi.EnsureInstance(args.Get("instance")),
                    Py.Or(args.Get("version"), ""), Py.Int(Py.Or(args.Get("memory_mb"), 0L)), jexe);
            }
            case "build_launch_command":
                return BuildLaunchCmd(args);
            case "launch_game":
            {
                // 立即返回 task_id，启动流程在 StartTaskAsync 里跑（对齐 Windows 版语义）
                string vid = Py.S(args.Get("version"));
                var row = StartTaskAsync(I18nCore.Tr("启动游戏") + " " + vid,
                    (p, tok) => LaunchCore.RunAsync(p, tok, this, args)).GetAwaiter().GetResult();
                return PyDict.Of("task_id", row.Id);
            }
            case "stop_game":
                return PyDict.Of("ok", LaunchCore.Stop());

            // —— M3-C0：游玩时长 / 运行状态（原 M2 桩转正）——
            case "is_game_running":
                return LaunchCore.IsRunning;
            case "get_playtime":
                return Playtime.GetPlaytime(Py.Or(args.Get("instance"), Config.CONFIG.Get("default_instance", "default")));
            case "get_all_playtime":
                return Playtime.GetAllPlaytime();
            case "get_total_playtime":
                return Playtime.GetTotalPlaytime();
            case "format_playtime":
                return Playtime.FormatDuration(args.Get("seconds"));
            // 线上新闻(照 Windows BackendApi.cached_news / fetch_news:同一份 News 语义 + 同一份缓存文件)。
            // fetch_news 跑在 CallAsync 的线程池线程上,内部以「两条源 → 缓存 → 空」降级,不抛。
            case "cached_news":
                return News.LoadCached();
            case "fetch_news":
                return News.Fetch();

            // —— 启动器自更新(照 Windows BackendApi.check_update / start_self_update;R22)——
            // check_update 会真发一次 HTTP(最多 12s)—— CallAsync 本身跑在 Task.Run 里,所以别在 UI 线程直接调它。
            case "check_update":
                return Updater.Check();
            case "start_self_update":
                return StartDetached("更新启动器", async (prog, ct) =>
                {
                    PyDict info = await Task.Run(() => Updater.Check(), ct);
                    if (!Py.Truthy(info["has_update"]))
                    {
                        prog.Log(Py.S(Py.Or(info["message"], "已是最新版本")));
                        return;
                    }
                    prog.Log(Py.S(Py.Or(info["message"], "下载更新")));
                    string pkg = await Task.Run(() => Updater.Download(info), ct);
                    prog.Log("更新包已下载: " + pkg);
                    // 交给外壳(MainWindow)去问"现在重启还是稍后":它订阅 update_staged 事件
                    RaiseEvent(new ApiEvent
                    {
                        Event = "update_staged",
                        Text = pkg,
                        Message = Py.S(Py.Or(info["latest"], "")),
                    });
                }).Id;
            case "apply_update":
            {
                // Linux 版是**当场替换**(见 Updater.ApplyStaged 的注释),所以这里同步做完就返回结论;
                // 目标目录不可写(.deb 装到 /opt)时返回 mode=download-only,由调用方把"怎么装完"告诉用户。
                PyDict res = Updater.ApplyStaged(Py.S(args.Get("package")), Py.Truthy(args.Get("restart")));
                RaiseEvent(new ApiEvent
                {
                    Event = "update_applied",
                    Message = Py.S(res["message"]),
                    Text = Py.S(res["mode"]),
                    Success = Py.Truthy(res["ok"]),
                });
                return res;
            }

            // —— 账号(设置页「账号」内层导航)——
            case "get_account_rows":
            {
                var am = new AccountManager();
                var rows = new List<object>();
                foreach (object it in am.accounts)
                {
                    var a = Py.D(it);
                    rows.Add(PyDict.Of(
                        "name", Py.Or(a.Get("name"), ""),
                        "type", Py.Or(a.Get("type"), "offline"),
                        "uuid", Py.Or(a.Get("uuid"), ""),
                        "api", Py.Or(a.Get("api"), ""),
                        "avatar", Skin.AvatarUrl(a),
                        "body", Skin.BodyUrl(a),
                        "skin_file", Py.Or(a.Get("skin_file"), ""),
                        "skin_model", Skin.SkinModel(a),
                        "active", Py.Eq(a.Get("name"), am.active)));
                }
                return rows;
            }
            case "authlib_presets":
                return Authlib.PRESETS
                    .Select(p => (object)PyDict.Of("name", p[0], "api", p[1]))
                    .ToList();
            case "skin_urls":
            {
                var a = new AccountManager().GetAccount(args.Get("account_name"));
                if (!Py.Truthy(a)) return PyDict.Of("avatar", Skin.SteveUrl(), "body", "");
                return PyDict.Of("avatar", Skin.AvatarUrl(a), "body", Skin.BodyUrl(a));
            }
            case "get_account_skin":
            {
                PyDict acc = new AccountManager().GetAccount(args.Get("name")) ?? new PyDict();
                byte[] png = Skin.LoadSkinPng(acc);
                return PyDict.Of(
                    "name", Py.S(args.Get("name")),
                    "skin_file", Py.Or(acc.Get("skin_file"), ""),
                    "skin_model", Skin.SkinModel(acc),
                    "slim", Py.Lower(Skin.SkinModel(acc)) == "slim",
                    "data_url", (png != null && png.Length > 0) ? "data:image/png;base64," + Convert.ToBase64String(png) : "");
            }
            case "set_account_skin":
            {
                var am = new AccountManager();
                PyDict acc = am.GetAccount(args.Get("name"));
                if (!Py.Truthy(acc))
                    throw new ValueError(I18nCore.Tr("没有这个账号:{0}").Replace("{0}", Py.S(args.Get("name"))));
                if (!Py.Eq(acc.Get("type"), "offline"))
                    throw new ValueError(I18nCore.Tr("自定义皮肤只对离线账号有效;正版和皮肤站账号的皮肤在各自的网站上改"));
                string path = Py.Strip(Py.S(args.Get("path")));
                string dataUrl = Py.Strip(Py.S(args.Get("data")));
                if (dataUrl.Length > 0)
                {
                    string raw = dataUrl.StartsWith("data:", StringComparison.Ordinal)
                        ? dataUrl.Substring(dataUrl.IndexOf(',') + 1)
                        : dataUrl;
                    byte[] bytes;
                    try { bytes = Convert.FromBase64String(raw); }
                    catch (FormatException) { throw new ValueError(I18nCore.Tr("皮肤数据不是有效的 base64")); }
                    Skin.ValidateSkin(bytes);
                    acc["skin_file"] = Skin.SaveSkinBytes(args.Get("name"), bytes);
                    acc["skin_model"] = Py.Lower(Py.S(args.Get("model"))) == "slim" ? "slim" : "classic";
                }
                else if (path.Length > 0)
                {
                    acc["skin_file"] = Skin.ImportSkin(args.Get("name"), path);
                    acc["skin_model"] = Py.Lower(Py.S(args.Get("model"))) == "slim" ? "slim" : "classic";
                }
                else
                {
                    Skin.RemoveSkin(acc);
                    acc.Remove("skin_file");
                    acc.Remove("skin_model");
                }
                am.Save();
                return PyDict.Of("ok", true);
            }

            // —— 三种登录(都是后台任务:id 立刻返回,进度/用户码走事件流)——
            case "start_microsoft_login":
                return StartDetached("微软登录", (prog, ct) =>
                {
                    var auth = new MicrosoftAuthenticator(Config.MsClientId());
                    PyDict acc = auth.Login(
                        on_code: (code, uri, exp) =>
                        {
                            RaiseEvent(new ApiEvent
                            {
                                Event = "login_code", TaskId = prog.TaskId,
                                Code = Py.S(code), Uri = Py.S(uri),
                            });
                            prog.Log($"请打开 {Py.S(uri)} 并输入代码 {Py.S(code)}");
                        },
                        on_status: st =>
                        {
                            RaiseEvent(new ApiEvent { Event = "login_status", TaskId = prog.TaskId, Text = st });
                            prog.Log(st);
                            prog.Report(st);
                        });
                    new AccountManager().AddAccount(acc);
                    prog.Log("登录成功:" + Py.S(acc.Get("name")));
                    return Task.CompletedTask;
                }).Id;
            case "start_authlib_login":
                return StartDetached("皮肤站登录", async (prog, ct) =>
                {
                    // 照 Windows:先确保 authlib-injector 就位(启动时要挂 javaagent),再登录
                    var dm = new DownloadManager();
                    await Task.Run(() => Authlib.EnsureInjector(dm, t => prog.Log(t)), ct);
                    PyDict acc = await Task.Run(() => Authlib.Login(args.Get("api"), args.Get("username"), args.Get("password")), ct);
                    new AccountManager().AddAccount(acc);
                    prog.Log("皮肤站登录成功:" + Py.S(acc.Get("name")));
                }).Id;
            case "start_nide8_login":
                return StartDetached("统一通行证登录", async (prog, ct) =>
                {
                    var dm = new DownloadManager();
                    await Task.Run(() => Nide8.EnsureJar(dm, t => prog.Log(t)), ct);
                    PyDict acc = await Task.Run(() => Nide8.Login(args.Get("server_id"), args.Get("username"), args.Get("password")), ct);
                    new AccountManager().AddAccount(acc);
                    prog.Log("统一通行证登录成功:" + Py.S(acc.Get("name")));
                }).Id;
            case "add_offline_account":
            {
                var am = new AccountManager();
                object skin = Py.Or(Py.Or(args.Get("skin"), Config.CONFIG.Get("offline_skin")), "default");
                PyDict acc = am.OfflineAccount(args.Get("username"), skin);
                am.AddAccount(new PyDict(acc) { ["type"] = "offline" });
                RaiseEvent(new ApiEvent { Event = "ui_changed" });
                return acc.Get("name");
            }
            case "remove_account":
            {
                new AccountManager().RemoveAccount(args.Get("name"));
                RaiseEvent(new ApiEvent { Event = "ui_changed" });
                return PyDict.Of("ok", true);
            }
            case "set_active_account":
            {
                object active = new AccountManager().SetActive(args.Get("name"));
                RaiseEvent(new ApiEvent { Event = "ui_changed" });
                return active;
            }

            // —— 全局 Java(设置页「Java」内层导航)——
            case "list_java":
            {
                string def = Py.S(Config.CONFIG.Get("default_java", ""));
                var rows = new List<object>();
                foreach (PyDict j in Java.AllJavas())
                {
                    string exe = Py.S(Py.Or(Py.Or(j.Get("exe"), j.Get("path")), ""));
                    rows.Add(PyDict.Of(
                        "name", Py.Or(j.Get("name"), ""),
                        "version", Py.Str(Py.Or(j.Get("major"), "?")),
                        "path", exe,
                        "is_default", exe.Length > 0 && exe == def));
                }
                return rows;
            }
            case "scan_java":
            {
                Java.ResetCaches();
                List<object> found = Java.FindSystemJavas();
                return PyDict.Of("ok", true, "count", (long)found.Count, "rows", found);
            }
            case "add_java":
            case "set_default_java":
            {
                string exe = ResolveJavaExe(Py.Strip(Py.S(args.Get("path"))));
                if (exe.Length == 0)
                    throw new ValueError(I18nCore.Tr("没找到可用的 java,请在目录里找到 bin/java(w) 再试"));
                Config.CONFIG.Set("default_java", exe);
                Config.CONFIG.Save();
                return PyDict.Of("ok", true, "path", exe);
            }

            // —— 服务器(设置页「服务器」内层导航)——
            case "list_servers":
                return Servers.ListServers(Inst());
            case "add_server":
                return Servers.AddServer(Inst(), args.Get("name"), args.Get("ip"), args.Get("port"), args.Get("description"));
            case "update_server":
            {
                var kw = new PyDict();
                foreach (string k in new[] { "name", "ip", "port", "description", "icon", "hidden" })
                    if (args.Get(k) != null)
                        kw[k] = args.Get(k);
                return Servers.UpdateServer(Inst(), args.Get("index"), kw);
            }
            case "delete_server":
                Servers.DeleteServer(Inst(), args.Get("index"));
                return PyDict.Of("ok", true);
            case "import_servers":
                return Servers.ImportServersTxt(Inst(), args.Get("text"));
            case "export_servers":
                return Servers.ExportServersTxt(Inst());

            // —— 清理 / 智能推荐(设置页「通用」)——
            case "cleaner_preview":
                return Cleaner.Preview();
            case "cleaner_apply":
                return Cleaner.Apply(args.Get("kinds"));
            case "get_smart_recommendation":
                return Sysinfo.GetSmartRecommendation();

            // —— 反馈(设置页「反馈与关于」)——
            case "feedback_has_consent":
                return Feedback.HasConsent();
            case "feedback_consent":
                Config.CONFIG.Set("feedback_consent", Py.Truthy(args.Get("allow")));
                Config.CONFIG.Save();
                return PyDict.Of("ok", true);
            case "feedback_submit":
                return Feedback.Submit(
                    Py.Or(args.Get("category"), "other"),
                    Py.Or(args.Get("title"), ""),
                    Py.Or(args.Get("text"), args.Get("body")),
                    args.Get("contact"),
                    Py.Or(args.Get("include_sysinfo"), true),
                    args.Get("crash"));
            case "feedback_history":
                return Feedback.History();
            case "feedback_submit_crash":
                // 崩溃报告与普通反馈同一条 ingest 通道(服务端 category 固定 crash)
                return Feedback.SubmitCrash(args.Get("report"), Py.Or(args.Get("extra"), ""));

            // —— 语言 / 翻译 ——
            case "get_language":
                return I18nCore.CurrentLanguage();
            case "set_language":
                I18nCore.SetLanguage(args.Get("lang"));
                return PyDict.Of("ok", true);
            case "available_languages":
                return I18nCore.AvailableLanguages();
            case "translate":
                return I18nCore.TrAny(args.Get("key"), (Py.Truthy(args.Get("lang")) ? args.Get("lang") : "") ?? "");

            // —— 多开 / AI 连接测试 ——
            case "allow_multi_instance":
                return Py.Truthy(Config.CONFIG.Get("allow_multi_instance", false));
            case "set_multi_instance":
                Config.CONFIG.Set("allow_multi_instance", Py.Truthy(args.Get("allow")));
                Config.CONFIG.Save();
                return PyDict.Of("ok", true);
            case "test_ai_connection":
                return TipCL.Core.Launcher.Ai.Client.TestConnection(Py.D(args.Get("settings")));

            // —— 主题包 ——
            case "list_themes":
                return Themes.ListThemes();
            case "save_theme":
                return Themes.SaveTheme(args.Get("name"));
            case "load_theme":
                return Themes.LoadTheme(args.Get("name"));
            case "delete_theme":
                Themes.DeleteTheme(args.Get("name"));
                return PyDict.Of("ok", true);
            case "import_theme":
                return Themes.ImportTheme(args.Get("path"));
            case "export_theme":
                return Themes.ExportTheme(args.Get("name"), args.Get("dest"));
            case "clear_playtime":
                Playtime.ClearPlaytime(args.Get("instance"), args.Get("version"));
                return PyDict.Of("ok", true);

            // —— 联机:局域网提示 / 陶瓦隧道(照 Windows BackendApi 的同名方法)——
            case "lan_hint":
                return Lan.LanHint(args.Get("port"));
            case "local_ips":
                return Lan.LocalIps();
            case "terracotta_player":
                return TerracottaPlayer();
            case "terracotta_snapshot":
                return Terracotta.Snapshot(TerracottaPlayer(), LaunchCore.IsRunning);
            case "terracotta_prepare":
                return StartDetached("准备陶瓦联机", async (prog, ct) =>
                {
                    var dm = new DownloadManager();
                    await Task.Run(() => Terracotta.Install(dm, t => prog.Log(t)), ct);
                    prog.Report(I18nCore.Tr("启动内核"), 1, 1);
                    await Task.Run(() => Terracotta.Start(t => prog.Log(t)), ct);
                    prog.Log(I18nCore.Tr("陶瓦联机已就绪"));
                }).Id;
            case "terracotta_host":
                Terracotta.SetScanning(TerracottaPlayer());
                return PyDict.Of("ok", true);
            case "terracotta_join":
                Terracotta.SetGuesting(args.Get("room"), TerracottaPlayer());
                return PyDict.Of("ok", true);
            case "terracotta_idle":
                Terracotta.SetWaiting();
                return PyDict.Of("ok", true);
            case "terracotta_allow_firewall":
                return Terracotta.AllowFirewall();
            case "terracotta_open_firewall_settings":
                Terracotta.OpenFirewallSettings();
                return PyDict.Of("ok", true);
            case "terracotta_shutdown":
                Terracotta.Stop();
                return PyDict.Of("ok", true);
            case "terracotta_enter_world":
            {
                PyDict snap = Terracotta.Snapshot(TerracottaPlayer(), LaunchCore.IsRunning);
                string url = Py.S(Py.Or(snap.Get("url"), ""));
                if (!Py.Eq(snap.Get("state"), "guest-ok") || url.Length == 0)
                    throw new TerracottaError(I18nCore.Tr("还没连上房间。请先输入邀请码加入。"));
                return LaunchIntoLobby(url);
            }
            case "terracotta_direct_connect":
            {
                var (host, port) = Terracotta.SplitJoinUrl(args.Get("address"));
                if (host.Length == 0 || host == "127.0.0.1" || host == "localhost")
                    throw new TerracottaError(I18nCore.Tr("请输入房主的公网地址,例如 1.2.3.4:25565"));
                return LaunchIntoLobby(host + ":" + port);
            }
            // —— M4-H P1:内容目录(下载页六分段 Mod / 整合包 / 数据包 / 资源包 / 光影 / 世界)——
            // 契约与 Windows 版 Core/BackendApi.cs 同名同参;搜索统一落到 CatalogFiles.SearchProjects
            // (Core 已搬,内部按 kind 分派 Modrinth/CurseForge 并做中文别名与排序)。
            case "search_mods":
            case "search_modpacks":
            case "search_datapacks":
            case "search_resourcepacks":
            case "search_shaders":
            {
                string kind = method switch
                {
                    "search_modpacks" => "modpack",
                    "search_datapacks" => "datapack",
                    "search_resourcepacks" => "resourcepack",
                    "search_shaders" => "shader",
                    _ => "mod",
                };
                return SearchCatalog(kind, args);
            }
            case "search_worlds":
                return SearchCatalog("world", args);
            case "list_catalog_files":
                return CatalogFiles.ListProjectFiles(new DownloadManager(2L), Py.D(args.Get("extra")));

            // 安装类一律「后台任务 + 立刻返回 task_id」(照 Windows 版 StartTask 语义),
            // 页面拿到 id 后即可跳任务页;进度经 TaskStore/事件流回灌。
            case "install_mod":
                return StartDetached(CatalogTitle("安装模组", args.Get("name")), (prog, ct) =>
                {
                    InstallModSync(prog, CatalogExtra(args));
                    return Task.CompletedTask;
                }).Id;
            case "install_shader":
            case "install_resourcepack":
            case "install_datapack":
            {
                string kind = method.Substring("install_".Length);
                string verb = kind switch { "shader" => "安装光影", "resourcepack" => "安装资源包", _ => "安装数据包" };
                return StartDetached(CatalogTitle(verb, args.Get("name")), (prog, ct) =>
                {
                    var extra = CatalogExtra(args);
                    extra.SetDefault("name", args.Get("name"));
                    extra.SetDefault("instance", args.Get("instance"));
                    InstallContentSync(prog, kind, extra);
                    return Task.CompletedTask;
                }).Id;
            }
            case "install_world":
                return StartDetached(CatalogTitle("安装世界", args.Get("name")), (prog, ct) =>
                {
                    var extra = CatalogExtra(args);
                    extra.SetDefault("name", args.Get("name"));
                    extra.SetDefault("instance", args.Get("instance"));
                    InstallWorldSync(prog, extra);
                    return Task.CompletedTask;
                }).Id;
            case "install_modpack":
                return StartDetached(CatalogTitle("安装整合包", args.Get("name")), (prog, ct) =>
                {
                    var extra = CatalogExtra(args);
                    extra.SetDefault("name", args.Get("name"));
                    extra.SetDefault("instance", args.Get("instance"));
                    extra.SetDefault("source", Py.Or(args.Get("source"), "Modrinth"));
                    InstallModpackSync(prog, extra);
                    return Task.CompletedTask;
                }).Id;

            // —— 已装内容(按实例 / 版本定位到 mods、shaderpacks、resourcepacks、datapacks、saves)——
            case "get_installed_mods":
            {
                var rows = Mods.ListModEntriesAt(ModsFolder(Inst(), args.Get("version")));
                return rows.OfType<PyDict>().Where((PyDict r) => Py.Truthy(r.Get("enabled"))).Select((PyDict r) => r.Get("filename")).ToList<object>();
            }
            case "get_installed_modpacks":
            {
                PyDict meta = Inst().Meta();
                if (meta.Get("modpack") is PyDict mp && Py.Truthy(mp.Get("name")))
                    return new List<object> { Py.Truthy(mp.Get("version")) ? Py.Str(mp.Get("name")) + " " + Py.Str(mp.Get("version")) : mp.Get("name") };
                return new List<object>();
            }
            case "get_installed_datapacks":
                return ContentNames(Inst(), "datapacks", args.Get("version"));
            case "get_mods_targets":
            {
                var inst = Inst();
                var rows = new List<object> { PyDict.Of("label", "实例共享 mods 目录", "value", "") };
                foreach (string ver in inst.InstalledIds())
                {
                    object iso = VersionSettings.Load(inst, ver).Get("isolation");
                    if (Py.Eq(iso, "mods") || Py.Eq(iso, "all"))
                        rows.Add(PyDict.Of("label", ver + " · 独立 mods", "value", ver));
                }
                return rows;
            }
            case "get_saves_targets":
            {
                var inst = Inst();
                var rows = new List<object> { PyDict.Of("label", "大锅饭(所有版本共用)", "value", "") };
                foreach (string ver in inst.InstalledIds())
                {
                    object iso = VersionSettings.Load(inst, ver).Get("isolation");
                    if (Py.Eq(iso, "saves") || Py.Eq(iso, "all"))
                        rows.Add(PyDict.Of("label", ver + " · 独立存档", "value", ver));
                }
                return rows;
            }
            case "open_mods_folder":
            {
                string dir = ModsFolder(Inst(), args.Get("version"));
                Directory.CreateDirectory(dir);
                Crash.ShellOpen(dir);
                return dir;
            }
            case "open_global_mods":
            {
                string dir = GlobalMods.Root();
                Directory.CreateDirectory(dir);
                Crash.ShellOpen(dir);
                return dir;
            }

            // —— 删除类 ——
            case "delete_datapack":
                Mods.DeleteContentFile(Inst(), "datapacks", args.Get("filename"), args.Get("version"));
                RaiseEvent(new ApiEvent { Event = "ui_changed" });
                return PyDict.Of("ok", true);
            case "delete_modpack":
            {
                var inst = Inst();
                PyDict meta = inst.Meta();
                if (meta.Get("modpack") is not PyDict)
                    throw new InstanceError("该实例没有已安装整合包");
                if (Py.Truthy(args.Get("purge_instance"))) inst.Delete();
                else inst.SetMeta("modpack", null!);
                RaiseEvent(new ApiEvent { Event = "ui_changed" });
                return PyDict.Of("ok", true);
            }

            // —— 收藏(配置里的 catalog_favorites,照 Windows 版键名)——
            case "catalog_favorites":
                return Py.ListCtor(Py.Or(Config.CONFIG.Get("catalog_favorites"), new List<object>()));
            case "toggle_favorite":
            {
                var favs = Py.ListCtor(Py.Or(Config.CONFIG.Get("catalog_favorites"), new List<object>()));
                var item = Py.D(args.Get("item"));
                string key = Py.S(item.Get("source")) + "|" + Py.S(Py.Or(Py.Or(item.Get("slug"), item.Get("id")), item.Get("name")));
                var kept = new List<object>();
                bool had = false;
                foreach (object old in favs)
                {
                    var od = Py.D(old);
                    string ok = Py.S(od.Get("source")) + "|" + Py.S(Py.Or(Py.Or(od.Get("slug"), od.Get("id")), od.Get("name")));
                    if (ok == key) had = true;
                    else kept.Add(old);
                }
                if (!had)
                    kept.Add(PyDict.Of("name", item.Get("name"), "source", item.Get("source"), "slug", item.Get("slug"), "id", item.Get("id")));
                Config.CONFIG.Set("catalog_favorites", kept);
                Config.CONFIG.Save();
                return kept;
            }

            // —— 导出(内容包导出,设置页/目录页共用)——
            case "default_export_dir":
                return ContentExport.DefaultExportDir();
            case "remember_export_dir":
                return ContentExport.RememberExportDir(args.Get("path"));
            case "export_content":
                return ContentExport.ExportOne(Inst(), args.Get("kind"), args.Get("name"), args.Get("dest_dir"), args.Get("version"));
            case "export_contents":
                return ContentExport.ExportMany(Inst(), args.Get("kind"), args.Get("names"), args.Get("dest_dir"), args.Get("version"));

            // —— 模组更新检查(目录页「检查更新」)——
            case "check_mod_updates":
                return ModUpdate.CheckUpdates(Inst());

            // —— 全局 mods(GlobalModsDialog 用)——
            case "list_global_mods":
                return GlobalMods.ListEntries();
            case "set_global_mod_enabled":
                return GlobalMods.SetEnabled(args.Get("filename"), args.Get("enabled"));

            // —— 等待任务结束(对照表用;超时返回 ok=false)——
            case "wait_task":
            {
                string tid = Py.S(args.Get("task_id"));
                long timeoutMs = Py.Int(Py.Or(args.Get("timeout"), 1800L)) * 1000L;
                var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(0, timeoutMs));
                while (DateTime.UtcNow < deadline)
                {
                    var r = TaskStore.Get(tid);
                    if (r == null || r.Finished) return PyDict.Of("ok", true, "finished", r != null);
                    Thread.Sleep(120);
                }
                return PyDict.Of("ok", false, "finished", false);
            }

            // —— M4-H P2:版本管理 / 版本设置 / 存档 / 官方启动器导入 ——
            // (契约名与参数照 Windows 版 Core/BackendApi.cs 同名方法)
            case "get_accounts":
                return new AccountManager().accounts.OfType<PyDict>()
                    .Select((PyDict a) => Py.Or(a.Get("name"), "")).ToList<object>();
            case "game_root_name":
                return Instances.RootName();
            case "set_game_dir":
            {
                // 照 Windows set_game_dir:先做一次写测试,通过才落进 instances_dir
                string raw = Py.AsStr(args.Get("path"), "__fspath__");
                if (raw.StartsWith("~", StringComparison.Ordinal))
                {
                    int cut = raw.IndexOfAny(new[] { '/', '\\' });
                    string tail = cut < 0 ? "" : raw[cut..];
                    raw = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + tail;
                }
                var p = new PPath(raw);
                string full = p.IsAbsolute ? p.ToString() : PPath.Join(Utils.ROOT, raw);
                try
                {
                    Directory.CreateDirectory(PPath.Abs(full));
                    string probe = Path.Combine(PPath.Abs(full), ".tipcl-write-test");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                }
                catch (Exception ex)
                {
                    throw new InstanceError(Py.Fmt(I18nCore.Tr("游戏目录不可写: {0}"), full) + "\n" + Py.ExcStr(ex));
                }
                Config.CONFIG.Set("instances_dir", p.IsAbsolute ? p.ToString() : raw);
                Config.CONFIG.Save();
                RaiseEvent(new ApiEvent { Event = "ui_changed" });
                return Config.CONFIG.instances_dir;
            }
            case "game_root_path":
                return Inst().path;
            case "open_media":
                return Crash.OpenPath(args.Get("path"));
            case "instance_java_label":
            {
                string cur = new Instance(Py.S(args.Get("name"))).JavaPref();
                if (cur == "自动选择") return "自动选择";
                foreach (PyDict j in Java.AllJavas())
                {
                    string exe = Py.S(Py.Or(Py.Or(j.Get("exe"), j.Get("path")), ""));
                    if (exe == cur) return Py.Or(j.Get("name"), cur);
                }
                return cur;
            }

            // —— 版本设置(隔离 / 内存 / JVM / 直连 / 绑定账号 / 隐藏)——
            case "get_version_settings":
                return VersionSettings.Load(Inst(), args.Get("version"));
            case "save_version_settings":
            {
                PyDict saved = VersionSettings.Save(Inst(), args.Get("version"), Py.D(args.Get("data")));
                RaiseEvent(new ApiEvent { Event = "ui_changed" });
                return saved;
            }
            case "get_version_isolation":
                return Py.Or(VersionSettings.Load(Inst(), args.Get("version")).Get("isolation"), "none");
            case "set_version_isolation":
                return SetVersionIsolation(Inst(), args.Get("version"), args.Get("mode"), Py.Truthy(args.Get("seed")));
            case "toggle_version_isolation":
                return SetVersionIsolation(Inst(), args.Get("version"),
                    Py.Truthy(args.Get("isolated")) ? "all" : "none", Py.Truthy(args.Get("seed")));
            case "rename_version":
                return VersionOps.RenameVersion(Inst(), args.Get("version"), args.Get("new_id"));
            case "copy_version":
                return VersionOps.CopyVersion(Inst(), args.Get("version"), args.Get("new_id"));
            case "hide_version":
            {
                PyDict r = VersionOps.SetHidden(Inst(), args.Get("version"), args.Get("hidden"));
                RaiseEvent(new ApiEvent { Event = "ui_changed" });
                return r;
            }
            case "open_version_folder":
                return VersionOps.OpenFolder(Inst(), args.Get("version"), Py.Or(args.Get("which"), "root"));

            // —— 版本维护(长任务,返回 task_id)——
            case "repair_version":
                return StartDetached("修复版本 " + Py.S(args.Get("version")), (prog, ct) =>
                {
                    var inst = Inst();
                    prog.Log("修复 " + Py.S(args.Get("version")) + ":校验并补全缺失文件");
                    string msg = Repair.RepairVersion(new Installer(inst), args.Get("version"));
                    prog.Log(msg);
                    RaiseEvent(new ApiEvent { Event = "ui_changed" });
                    return Task.CompletedTask;
                }).Id;
            case "export_launch_script":
                return StartDetached("导出启动脚本 " + Py.S(args.Get("version")), (prog, ct) =>
                {
                    // Windows 侧产 .bat(VersionOps.ExportLaunchBat);Linux 产可执行的 .sh
                    var cmd = BuildLaunchCmd(args);
                    string dest = Py.S(args.Get("dest"));
                    if (dest.Length == 0) throw new ValueError(I18nCore.Tr("请先选择保存位置"));
                    string inst = Py.S(Py.Or(args.Get("instance"), "")) is { Length: > 0 } ins ? ins : Instances.RootName();
                    string text = WriteLaunchScript(dest, Py.S(args.Get("version")), cmd, InstancesApi.EnsureInstance(inst).path);
                    prog.Log("已写入 " + text);
                    return Task.CompletedTask;
                }).Id;
            case "create_desktop_shortcut":
            {
                // 与 Windows BackendApi.create_desktop_shortcut 同名同参;Linux 侧产出 .desktop
                // (参数由 Shortcut.LaunchArgs 给,由 Program.cs 的 CLI 入口真的消费)
                string path = Shortcut.CreateLaunchShortcut(args.Get("instance"), args.Get("version"),
                    args.Get("username"), args.Get("account"), args.Get("name"));
                return path;
            }
            case "delete_desktop_shortcut":
            {
                // 参数用快捷方式「文件名」(不带扩展名),与 create 返回的路径互补
                string name = Py.S(args.Get("name"));
                if (name.Length == 0)
                    throw new ValueError(I18nCore.Tr("没有指定要删除的快捷方式"));
                return PyDict.Of("ok", Shortcut.RemoveLaunchShortcut(name), "path", Shortcut.ShortcutPathForName(name));
            }
            case "export_modpack":
                return StartDetached("导出整合包 " + Py.S(args.Get("instance")), (prog, ct) =>
                {
                    var inst = Inst();
                    string dest = Py.S(args.Get("dest"));
                    if (dest.Length == 0) throw new ValueError(I18nCore.Tr("请先选择保存位置"));
                    prog.Report("导出中", 0, 1);
                    string outPath = ExportPack.ExportMrpack(inst, dest,
                        new DownloadManager(2L, on_progress: (m, c, t) => prog.Report(m, c, t)), (m, c, t) => prog.Report(m, c, t));
                    prog.Log("已导出 " + outPath);
                    return Task.CompletedTask;
                }).Id;
            case "start_mod_updates":
                return StartDetached("检查模组更新 " + Py.S(args.Get("instance")), (prog, ct) =>
                {
                    var inst = Inst();
                    var dm = new DownloadManager(2L);
                    List<object> rows = ModUpdate.CheckUpdates(inst, dm);
                    if (rows.Count == 0)
                    {
                        prog.Log(I18nCore.Tr("没有可更新的模组"));
                        return Task.CompletedTask;
                    }
                    for (int i = 0; i < rows.Count; i++)
                    {
                        var row = Py.D(rows[i]);
                        prog.Report(Py.Str(Py.Or(row.Get("name"), "")), i + 1, rows.Count);
                        ModUpdate.ApplyUpdate(inst, row, dm);
                    }
                    prog.Log(Py.Fmt(I18nCore.Tr("已更新 {0} 个模组"), (long)rows.Count));
                    RaiseEvent(new ApiEvent { Event = "ui_changed" });
                    return Task.CompletedTask;
                }).Id;
            case "apply_mod_update":
                return ModUpdate.ApplyUpdate(Inst(), Py.D(args.Get("row")), new DownloadManager(2L));

            // —— 官方启动器导入 ——
            case "detect_official_launcher":
                return OfficialMigrate.DetectOfficial();
            case "official_launcher_dir":
                return OfficialMigrate.OfficialDir();
            case "scan_official_versions":
                return OfficialMigrate.ScanVersions(OfficialMigrate.OfficialDir());
            case "migrate_official_launcher":
                return StartDetached("导入官方启动器", (prog, ct) =>
                {
                    string src = OfficialMigrate.OfficialDir();
                    prog.Log("正在从 " + src + " 迁移...");
                    List<object> found = OfficialMigrate.ScanVersions(src);
                    if (found.Count == 0)
                    {
                        prog.Log(I18nCore.Tr("未发现版本"));
                        throw new InstanceError(I18nCore.Tr("无版本可导入"));
                    }
                    prog.Report(Py.Fmt(I18nCore.Tr("导入 {0} 个版本(含依赖库)"), (long)found.Count), 1, 3);
                    PyDict res = OfficialMigrate.Migrate(src, args.Get("instance"));
                    var versions = Py.ListOr(res.Get("versions"));
                    var accs = Py.ListOr(res.Get("accounts"));
                    prog.Report(I18nCore.Tr("完成"), 3, 3);
                    prog.Log($"已导入 {versions.Count} 个版本(含各版本用到的 libraries)");
                    if (accs.Count > 0) prog.Log("已导入账号: " + string.Join("、", accs.Select(Py.Str)));
                    RaiseEvent(new ApiEvent { Event = "ui_changed" });
                    return Task.CompletedTask;
                }).Id;

            // —— 存档 / 媒体 ——
            case "delete_save":
                Saves.DeleteSave(Inst(), args.Get("name"), args.Get("version"));
                RaiseEvent(new ApiEvent { Event = "ui_changed" });
                return PyDict.Of("ok", true);
            case "open_save":
                return Saves.OpenSave(Inst(), args.Get("name"), args.Get("version"));
            case "export_save":
                return Saves.ExportSave(Inst(), args.Get("name"), args.Get("dest"), args.Get("version"));
            case "install_datapack_into_save":
                return Saves.InstallDatapackIntoSave(Inst(), args.Get("filename"), args.Get("save_name"), args.Get("version"));
            case "list_media":
                return Saves.ListMedia(Inst(), args.Get("kind"), args.Get("version"));
            case "backup_save":
                return StartDetached("备份存档 " + Py.S(args.Get("name")), (prog, ct) =>
                {
                    Saves.BackupSave(Inst(), args.Get("name"), args.Get("version"),
                        (m, c, t) => prog.Report(m, c, t));
                    prog.Log("存档备份完成");
                    return Task.CompletedTask;
                }).Id;
            case "list_save_backups":
                return Saves.ListBackups(Inst(), args.Get("save_name"), args.Get("version"));
            case "restore_save_backup":
            {
                PyDict r = Saves.RestoreBackup(Inst(), args.Get("backup_name"), args.Get("version"),
                    args.Get("target_name"), Py.Truthy(args.Get("overwrite")));
                RaiseEvent(new ApiEvent { Event = "ui_changed" });
                return r;
            }
            case "delete_save_backup":
                Saves.DeleteBackup(Inst(), args.Get("backup_name"), args.Get("version"));
                return PyDict.Of("ok", true);

            // —— M4-H P4:反馈页 / 崩溃界面 / 首启向导 ——
            case "submit_feedback":
            {
                // Windows 同名契约返回 OpResult{ok,message};Linux 侧把 Core 的服务器响应归一成同一形状
                string cat = Py.Or(args.Get("category"), "other") as string ?? "other";
                PyDict r = Feedback.Submit(cat, Py.Or(args.Get("title"), ""),
                    Py.Or(args.Get("body"), args.Get("text")), args.Get("contact"),
                    Py.Or(args.Get("include_sysinfo"), true));
                return PyDict.Of("ok", true,
                    "message", Py.Or(r.Get("message"), I18nCore.Tr("已提交")),
                    "id", Py.Or(r.Get("id"), ""));
            }
            case "help_articles":
                return HelpContent.SearchArticles(args.Get("query")).Select(LocalizeArticle).ToList();
            case "help_article":
                return LocalizeArticle(HelpContent.GetArticle(args.Get("article_id")) ?? new PyDict());
            case "collect_sysinfo":
                return Sysinfo.Collect(Py.Truthy(args.Get("force")), Py.Truthy(args.Get("scan_system_java")));
            case "sysinfo_text":
                return Sysinfo.FormatText(args.Get("info"));
            case "stash_upload":
                return StashUpload(args.Get("name"), args.Get("data"));

            // 崩溃报告:进程内最近若干份(照 Windows BackendApi._crashes)
            case "get_crash":
                return GetCrash(Py.S(args.Get("task_id")));
            case "export_crash_report":
            {
                PyDict rep = GetCrash(Py.S(args.Get("task_id")));
                if (rep.Count == 0) throw new LaunchError(I18nCore.Tr("没有可导出的错误报告"));
                return Crash.ExportReport(rep, Py.Truthy(args.Get("dest")) ? Py.Str(args.Get("dest")) : null!);
            }
            case "open_crash_file":
            {
                object path = Py.Or(args.Get("path"), GetCrash(Py.S(args.Get("task_id"))).Get("direct_file") ?? "");
                if (!Py.Truthy(path)) throw new LaunchError(I18nCore.Tr("没有可打开的日志文件"));
                if (!Crash.OpenPath(path)) throw new LaunchError(Py.Fmt(I18nCore.Tr("无法打开: {0}"), path));
                return path;
            }
            case "apply_crash_action":
                return ApplyCrashAction(Py.D(args.Get("action")), Py.D(args.Get("report")));
            case "submit_crash_report":
            {
                PyDict rep = GetCrash(Py.S(args.Get("task_id")));
                if (rep.Count == 0) throw new LaunchError(I18nCore.Tr("没有可上传的崩溃报告"));
                PyDict r = Feedback.SubmitCrash(rep);
                return PyOr(r.Get("message"), I18nCore.Tr("已上传"));
            }
            case "submit_crash_feedback":
            {
                PyDict r = Feedback.SubmitCrash(Py.D(args.Get("report")), Py.Or(args.Get("extra"), ""));
                return PyDict.Of("ok", true, "message", PyOr(r.Get("message"), I18nCore.Tr("已反馈")), "id", Py.Or(r.Get("id"), ""));
            }

            default:
                throw new NotImplementedException($"IApi 尚未实现方法 {method}(M2-B0 方法表之外,属 M3 或未搬后端能力)");
        }
    }

    // ───────────────────── M4-H P1:内容目录(下载页六分段)辅助 ─────────────────────

    /// <summary>最近一次搜索结果缓存(按目录 kind),供 install_* 按名字回查 slug/id(照 Windows _mod_cache/_pack_cache)。</summary>
    private static readonly Dictionary<string, List<object>> CatalogCache = new();

    private static string CatalogTitle(string verb, object name)
        => verb + " " + new PPath(Py.Str(name)).Name;

    private static PyDict CatalogExtra(PyDict args) => new(Py.D(args.Get("extra")));

    /// <summary>实例 + 版本 → mods 目录(照 Windows BackendApi._mods_folder)。</summary>
    private static string ModsFolder(Instance inst, object version = null!)
        => Py.Truthy(version) ? VersionSettings.ModsDir(inst, version) : PPath.Join(inst.path, "mods");

    private static List<object> ContentNames(Instance inst, string subdir, object version = null!)
        => Mods.ListContentFiles(inst, subdir, version).Select((string p) => (object)new PPath(p).Name).ToList();

    private static List<object> SearchCatalog(string kind, PyDict args)
    {
        var rows = CatalogFiles.SearchProjects(new DownloadManager(2L), kind, args.Get("query"), args.Get("source"), Py.D(args.Get("extra")));
        var mapped = new List<object>();
        foreach (object r in rows)
            if (r is PyDict d)
                mapped.Add(CatalogRow(d, kind));
        CatalogCache[kind] = mapped;
        return mapped;
    }

    /// <summary>目录搜索结果行 → 页面模型(name/author/downloads/id/slug/source/description/tags/updated/icon_url/project_url)。</summary>
    private static PyDict CatalogRow(PyDict hit, string kind)
    {
        string src = Py.Lower(Py.S(hit.Get("source")));
        var row = PyDict.Of(
            "name", Py.Or(Py.Or(hit.Get("name"), hit.Get("title")), "?"),
            "author", Py.Or(hit.Get("author"), "?"),
            "downloads", Py.Int(Py.Or(hit.Get("downloads"), 0L)),
            "id", hit.Get("id"),
            "slug", hit.Get("slug"),
            "source", src,
            "description", Py.Or(Py.Or(hit.Get("description"), hit.Get("summary")), ""),
            "tags", Py.Or(hit.Get("tags"), new List<object>()),
            "updated", Py.Or(hit.Get("updated"), ""),
            "icon_url", Py.Or(hit.Get("icon_url"), ""));
        // 项目介绍页地址(curse 优先取 website_url,否则按目录类别拼 CurseForge / Modrinth 路径)。
        // 注:Windows 版 `_content_row` 一律按 kind="mod" 拼 Modrinth 地址,对资源包/光影是不对的;
        // 这里按真实 kind 拼(只影响点击行打开的网页,不影响任何布局),已在 DECISIONS D-18 记录。
        string url = "";
        bool curse = src.StartsWith("curse", StringComparison.Ordinal);
        if (curse)
        {
            url = Py.S(hit.Get("website_url"));
            string slug = Py.S(hit.Get("slug"));
            if (url.Length == 0 && slug.Length > 0)
            {
                string seg = kind switch { "modpack" => "modpacks", "world" => "worlds", _ => "mc-mods" };
                url = "https://www.curseforge.com/minecraft/" + seg + "/" + slug;
            }
        }
        else
        {
            string slug = Py.S(hit.Get("slug"));
            if (slug.Length > 0) url = "https://modrinth.com/" + kind + "/" + slug;
        }
        row["project_url"] = url;
        return row;
    }

    /// <summary>按名字/slug/id 在最近一次搜索结果里回查一行(照 Windows _lookup_mod/_lookup_pack)。</summary>
    private static PyDict CatalogLookup(string kind, object name, object source)
    {
        string want = Py.Strip(Py.Lower(Py.S(name)));
        if (CatalogCache.TryGetValue(kind, out var rows))
        {
            foreach (object it in rows)
            {
                if (it is not PyDict d) continue;
                string n = Py.Lower(Py.S(d.Get("name")));
                string s = Py.Lower(Py.S(d.Get("slug")));
                string i = Py.Lower(Py.Str(d.Get("id")));
                if (want.Length > 0 && (want == n || want == s || want == i)) return d;
            }
        }
        string src = Py.Lower(Py.S(source)).StartsWith("curse", StringComparison.Ordinal) ? "curseforge" : "modrinth";
        return PyDict.Of("name", name, "source", src, "slug", name);
    }

    private static DownloadManager CatalogDm(TaskProgress prog)
        => new(2L, on_progress: (msg, cur, total) =>
        {
            prog.Report(msg, cur, total);
            if (!string.IsNullOrWhiteSpace(msg)) prog.Log(msg);
        });

    /// <summary>install_mod 的实现(照 Windows BackendApi._install_mod_impl)。</summary>
    private static void InstallModSync(TaskProgress prog, PyDict extra)
    {
        var inst = new Instance(Py.S(extra.Get("instance")));
        var dm = CatalogDm(prog);
        string version = Py.S(extra.Get("version"));
        string modsDir = ModsFolder(inst, version);
        string source = Py.Lower(Py.S(extra.Get("source")));
        object versionId = extra.Get("version_id");
        object fileId = extra.Get("file_id");
        object mcVersion = Py.Or(extra.Get("game_version"), extra.Get("mc_version"));
        object pathOrUrl = Py.Or(extra.Get("path"), extra.Get("url"));
        var deps = new List<object>();
        if (pathOrUrl != null && Py.Truthy(pathOrUrl))
        {
            prog.Log("安装模组: " + Py.Str(pathOrUrl));
            Mods.InstallModFromSource(dm, Py.Str(pathOrUrl), inst, null!, null!, dm.on_progress, versionId, modsDir);
        }
        else
        {
            PyDict pick = Py.Truthy(extra.Get("slug"))
                ? extra
                : CatalogLookup("mod", Py.Or(extra.Get("name"), extra.Get("slug")), extra.Get("source"));
            if (Py.Truthy(pick.Get("id")) && Py.Lower(Py.S(Py.Or(pick.Get("source"), source))).StartsWith("curse", StringComparison.Ordinal))
            {
                prog.Log("从 CurseForge 安装模组 id=" + Py.Str(pick.Get("id")));
                PyDict res = Mods.InstallCurseforgeMod(dm, pick.Get("id"), inst, mcVersion, null!, null!, dm.on_progress,
                    Py.Or(fileId, extra.Get("version_id")), modsDir);
                deps = Py.ListOr(Py.Or(res.Get("dependencies"), new List<object>()));
            }
            else
            {
                object slug = Py.Or(pick.Get("slug"), extra.Get("name"));
                prog.Log("从 Modrinth 安装模组 " + Py.Str(slug));
                Mods.InstallModFromSource(dm, Py.Str(slug), inst, mcVersion, null!, dm.on_progress, versionId, modsDir);
            }
        }
        if (deps.Count > 0) prog.Log("已随装前置:" + string.Join("、", deps.Select(Py.Str)));
        prog.Log("模组安装完成");
    }

    /// <summary>install_shader / install_resourcepack / install_datapack 的实现。</summary>
    private static void InstallContentSync(TaskProgress prog, string kind, PyDict extra)
    {
        var inst = new Instance(Py.S(extra.Get("instance")));
        var dm = CatalogDm(prog);
        PyDict spec = Mods.CONTENT_KINDS[kind];
        string subdir = Py.S(spec.Get("subdir"));
        string version = Py.S(extra.Get("version"));
        prog.Log("安装到 " + Mods.ContentDir(inst, subdir, version));
        PyDict res = Mods.InstallContentFromSource(dm, inst, subdir, extra, dm.on_progress, version);
        var files = Py.ListOr(Py.Or(res.Get("files"), new List<object>()));
        prog.Log("完成: " + (files.Count > 0 ? string.Join(", ", files.Select(Py.Str)) : Py.Str(extra.Get("name"))));
        if (kind == "datapack")
        {
            // 下载页问的「装进存档」= extra["save"]:选中了哪个存档,装完就地复制进它的 datapacks。
            // Windows 侧这个键传下去后没人读(参考版缺陷,见 _cmp/r18-report.md 任务 B),
            // Linux 侧按 Progress 里写明的意图落地:能进就进,进不去只记一条带原因的日志(数据包本身已装好)。
            string save = Py.S(extra.Get("save"));
            if (save.Length > 0 && files.Count > 0)
            {
                foreach (object f in files)
                {
                    try
                    {
                        string target = Saves.InstallDatapackIntoSave(inst, f, save, version);
                        prog.Log("已装进存档 " + save + ": " + target);
                    }
                    catch (Exception ex)
                    {
                        prog.Log($"没能装进存档 {save}({Py.Str(f)}):{ex.Message}");
                    }
                }
            }
            else
            {
                prog.Log("数据包已放到实例 datapacks 目录,请复制到对应存档的 datapacks 文件夹后进入世界。");
            }
        }
    }

    /// <summary>install_world 的实现。</summary>
    private static void InstallWorldSync(TaskProgress prog, PyDict extra)
    {
        var inst = new Instance(Py.S(extra.Get("instance")));
        var dm = CatalogDm(prog);
        string version = Py.S(extra.Get("version"));
        prog.Log("安装世界到 " + Worlds.SavesRoot(inst, version));
        PyDict res = Worlds.InstallWorld(dm, extra, inst, dm.on_progress, version);
        var files = Py.ListOr(Py.Or(res.Get("files"), new List<object>()));
        string shown = files.Count > 0 ? string.Join(", ", files.Select(Py.Str)) : Py.Str(extra.Get("name"));
        prog.Log("完成: " + shown);
        prog.Report(Py.Fmt("已安装世界 {0}", shown), 1, 1);
    }

    /// <summary>install_modpack 的实现(照 Windows BackendApi._install_modpack_impl)。</summary>
    private static void InstallModpackSync(TaskProgress prog, PyDict extra)
    {
        var inst = new Instance(Py.S(extra.Get("instance")));
        var dm = CatalogDm(prog);
        object name = extra.Get("name");
        object source = Py.Or(extra.Get("source"), "Modrinth");
        object target = Py.Or(extra.Get("path"), name);
        string src = Py.Lower(Py.S(source));
        prog.Log("整合包安装引擎:按声明的 Forge/Fabric 版本直装(不依赖残缺的 Maven 列表)");
        PyDict? res = null;
        if (src.StartsWith("本地", StringComparison.Ordinal) || LocalFs.IsFile(Py.Str(target)))
        {
            string path = PPath.Norm(Py.Str(target));
            prog.Log("从本地文件安装: " + path);
            prog.Log("实例: " + inst.name + "  路径: " + inst.path);
            res = Modpack.InstallAuto(dm, path, inst, dm.on_progress, dm.cancel);
        }
        else
        {
            PyDict pick = Py.Truthy(extra.Get("slug")) || Py.Truthy(extra.Get("id"))
                ? extra
                : CatalogLookup("modpack", name, source);
            if (Py.Lower(Py.S(Py.Or(pick.Get("source"), src))).StartsWith("curse", StringComparison.Ordinal))
            {
                object id = pick.Get("id");
                object slug = pick.Get("slug");
                if (!Py.Truthy(id) && !Py.Truthy(slug))
                    throw new RuntimeError(Py.Fmt("无法解析整合包: {0}", name));
                prog.Log("从 CurseForge 安装 " + Py.Str(Py.Or(pick.Get("name"), name)) + " (id=" + Py.S(id) + " slug=" + Py.S(slug) + ")");
                prog.Log("实例: " + inst.name + "  路径: " + inst.path);
                res = Modpack.InstallCfModpack(dm, id, inst, Config.CurseforgeKey(), dm.on_progress, dm.cancel,
                    slug, Py.Or(extra.Get("file_id"), extra.Get("version_id")));
            }
            else
            {
                object slug = Py.Or(pick.Get("slug"), name);
                prog.Log("从 Modrinth 安装 " + Py.Str(Py.Or(pick.Get("name"), slug)) + " (" + Py.Str(slug) + ")");
                prog.Log("实例: " + inst.name + "  路径: " + inst.path);
                res = Modpack.InstallMrpackBySlug(dm, slug, inst, dm.on_progress, dm.cancel, false, null!, extra.Get("version_id"));
            }
        }
        if (res != null && Py.Truthy(res.Get("instance")))
        {
            Config.CONFIG.Set("default_instance", res.Get("instance"));
            Config.CONFIG.Save();
        }
        prog.Log("整合包安装完成: " + Py.Str(Py.Or(res != null ? res.Get("name") : null!, name)));
    }

    // ───────────────────── M4-H P2:版本管理辅助 ─────────────────────

    /// <summary>切换 / 设置版本隔离并广播 ui_changed(照 Windows set·toggle_version_isolation)。</summary>
    private PyDict SetVersionIsolation(Instance inst, object version, object mode, bool seed)
    {
        PyDict r = VersionSettings.SetIsolation(inst, version, mode, seed);
        RaiseEvent(new ApiEvent { Event = "ui_changed" });
        return r;
    }

    /// <summary>
    /// 组装某个版本的实际启动命令行(build_launch_command 与 export_launch_script 共用)。
    /// </summary>
    private static List<object> BuildLaunchCmd(PyDict args)
    {
        var inst = InstancesApi.EnsureInstance(args.Get("instance"));
        string vid = Py.S(args.Get("version"));
        if (vid.Length == 0) throw new LaunchError(I18nCore.Tr("请先选择版本"));
        var accounts = new AccountManager();
        PyDict acc = LaunchCore.RequireLaunchAccount(accounts, args.Get("account"), args.Get("username"));
        PyDict props = accounts.LaunchProps(acc);
        string authServer = Py.Strip(Py.Str(Py.Or(VersionSettings.Load(inst, vid).Get("auth_server"), "")));
        if (authServer.Length > 0 && !Py.Truthy(props.Get("authlib_api")))
        {
            props = new PyDict(props);
            props["authlib_api"] = authServer;
        }
        object java0 = Py.Or(args.Get("java"), "自动选择");
        var lc = McLauncher.BuildLaunchCommand(inst, vid, props,
            Py.Eq(java0, "自动选择") || Py.Eq(java0, "") ? "自动选择" : java0,
            args.Get("memory_mb"), args.Get("width"), args.Get("height"), new List<object>(), new PyDict(), "", props.Get("authlib_api"));
        return lc.cmd.Cast<object>().ToList();
    }

    /// <summary>
    /// 写一份可执行的启动脚本。Windows 用 `VersionOps.ExportLaunchBat`(批处理),
    /// Linux 上写 `#!/bin/sh` + exec(照同一调用链,只换脚本方言)。
    /// </summary>
    private static string WriteLaunchScript(string dest, string version, List<object> cmd, string cwd)
    {
        var lines = new List<string>
        {
            "#!/bin/sh",
            "# TipCL Linux 版本 " + version + " 启动脚本(由启动器导出)",
            "cd " + ShellQuote(cwd) + " || exit 1",
            "exec " + string.Join(" ", cmd.Select(x => ShellQuote(Py.Str(x)))),
            "",
        };
        Directory.CreateDirectory(PPath.Abs(new PPath(dest).Parent.ToString()));
        File.WriteAllText(PPath.Abs(dest), string.Join("\n", lines));
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                // CA1416 认得 OperatingSystem.IsWindows() 这个守卫(Utils.IS_WINDOWS 是运行时开关,分析器不认)
                string abs = PPath.Abs(dest);
                UnixFileMode mode = File.GetUnixFileMode(abs);
                File.SetUnixFileMode(abs,
                    mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
            catch (Exception) { /* 文件系统不支持权限位时忽略 */ }
        }
        return new PPath(dest).ToString();
    }

    private static string ShellQuote(string s)
        => s.Length > 0 && s.All(c => char.IsLetterOrDigit(c) || "-_./:=+".IndexOf(c) >= 0)
            ? s
            : "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>非 Adoptium 发行版的 Java 下载任务(照 Windows install_java)。</summary>
    private string InstallJavaVendorTask(long major, string vendor)
        => StartDetached("下载 " + vendor + " Java " + major, (prog, ct) =>
        {
            var dm = new DownloadManager(2L, on_progress: (m, c, t) => prog.Report(m, c, t));
            string path = Java.InstallJavaVendor(dm, major, vendor, dm.on_progress);
            prog.Log("Java 已安装: " + path);
            RaiseEvent(new ApiEvent { Event = "ui_changed" });
            return Task.CompletedTask;
        }).Id;

    // ───────────────────── M4-H P4:反馈页 / 崩溃界面辅助 ─────────────────────

    /// <summary>崩溃报告缓存(照 Windows BackendApi._crashes:按 task_id 存最近 40 份,超过裁到 20)。</summary>
    private static readonly Dictionary<string, PyDict> Crashes = new();

    private static readonly object CrashGate = new();

    /// <summary>记一份崩溃报告(任务失败时由 StartDetached 调用,也供 launch_game 直调)。</summary>
    internal static void RememberCrash(string taskId, PyDict report)
    {
        if (report == null || report.Count == 0) return;
        lock (CrashGate)
        {
            Crashes[taskId] = report;
            if (Crashes.Count > 40)
                foreach (string key in Crashes.Keys.Take(Crashes.Count - 20).ToList())
                    Crashes.Remove(key);
        }
    }

    private static PyDict GetCrash(string taskId)
    {
        lock (CrashGate)
        {
            if (taskId.Length > 0 && Crashes.TryGetValue(taskId, out var hit)) return hit;
            return Crashes.Count > 0 ? Crashes[Crashes.Keys.Last()] : new PyDict();
        }
    }

    private static object LocalizeArticle(object article)
    {
        if (article is not PyDict d || !Py.Truthy(d.Get("title"))) return article;
        var copy = new PyDict(d);
        copy["title"] = I18nCore.Tr(Py.Str(copy.Get("title")));
        return copy;
    }

    /// <summary>暂存上传附件(base64 → $TIPCL_HOME/uploads/,照 Windows stash_upload),返回落盘路径。</summary>
    private static string StashUpload(object name, object data)
    {
        string raw = Py.Strip(Py.AsStr(data, "strip"));
        if (raw.StartsWith("data:", StringComparison.Ordinal)) raw = raw[(raw.IndexOf(',') + 1)..];
        byte[] bytes;
        try { bytes = Convert.FromBase64String(raw); }
        catch (FormatException) { throw new ValueError(I18nCore.Tr("上传的数据不是有效的 base64")); }
        if (bytes.Length == 0) throw new ValueError(I18nCore.Tr("上传的文件是空的"));
        string input = Py.AsStr(name, "__fspath__");
        int cut = Math.Max(input.LastIndexOf('/'), input.LastIndexOf('\\'));
        string safe = Py.Strip(UploadBadChars.Replace(cut >= 0 ? input[(cut + 1)..] : input, "_"), " .");
        string dir = PPath.Join(Utils.ROOT, "uploads");
        Directory.CreateDirectory(PPath.Abs(dir));
        string dest = PPath.Join(dir, safe.Length > 0 ? safe : "upload.bin");
        if (LocalFs.Exists(dest))
        {
            var p = new PPath(dest);
            dest = PPath.Join(dir, p.Stem + "-" + Py.Int(Py.Time()) + p.Suffix);
        }
        File.WriteAllBytes(PPath.Abs(dest), bytes);
        return dest;
    }

    private static readonly System.Text.RegularExpressions.Regex UploadBadChars =
        new("[<>:\"/\\\\|?*\\x00-\\x1f]");

    /// <summary>执行崩溃报告里的一个修复动作(照 Windows BackendApi.apply_crash_action 的 8 个分支)。</summary>
    private PyDict ApplyCrashAction(PyDict action, PyDict report)
    {
        string id = Py.Strip(Py.AsStr(action.Get("id"), "strip"));
        object instance = Py.Or(Py.Or(action.Get("instance"), report.Get("instance")),
            Py.Or(Config.CONFIG.Get("default_instance"), "default"));
        object version = Py.Or(Py.Or(action.Get("version"), report.Get("version")), "");
        switch (id)
        {
            case "disable_mods":
            {
                var mods = Py.ListCtor(Py.Or(action.Get("mods"), new List<object>()));
                var ok = new List<object>();
                var failed = new List<string>();
                foreach (object m in mods)
                {
                    try
                    {
                        Mods.SetModEnabled(new Instance(Py.S(instance)), m, false, ModsFolder(new Instance(Py.S(instance)), Py.Truthy(version) ? version : null!));
                        ok.Add(m);
                    }
                    catch (Exception e) { failed.Add(Py.Str(m) + ": " + Py.ExcStr(e)); }
                }
                if (ok.Count == 0 && failed.Count > 0)
                    return PyDict.Of("ok", false, "message", I18nCore.Tr("未能禁用:") + string.Join("; ", failed));
                string msg = Py.Fmt(I18nCore.Tr("已禁用 {0} 个 Mod"), (long)ok.Count);
                if (failed.Count > 0) msg += I18nCore.Tr(";部分失败:") + string.Join("; ", failed);
                return PyDict.Of("ok", true, "message", msg);
            }
            case "repair_version":
            {
                if (!Py.Truthy(version)) return PyDict.Of("ok", false, "message", I18nCore.Tr("报告里没有版本号,无法修复"));
                Instance.RaiseEvent(new ApiEvent { Event = "ui_changed" });
                return PyDict.Of("ok", true,
                    "message", Py.Fmt(I18nCore.Tr("请到「版本管理」右键该版本点「修复(补全缺失文件)」")),
                    "version", version);
            }
            case "need_java":
            {
                long major = Py.Int(Py.Or(action.Get("major"), 17L));
                Instance.RaiseEvent(new ApiEvent { Event = "ui_changed" });
                return PyDict.Of("ok", true,
                    "message", Py.Fmt(I18nCore.Tr("请到「设置 → Java」下载 Java {0}"), major));
            }
            case "bump_memory":
            {
                long mb = Math.Max(1024, Math.Min(32768, Py.Int(Py.Or(action.Get("memory_mb"), 6144L))));
                Config.CONFIG.Set("memory_mb", mb);
                Config.CONFIG.Save();
                Instance.RaiseEvent(new ApiEvent { Event = "ui_changed" });
                return PyDict.Of("ok", true, "message", Py.Fmt(I18nCore.Tr("默认内存已设为 {0} MB"), mb));
            }
            case "open_mods_folder":
            {
                var inst = new Instance(Py.S(instance));
                string dir = ModsFolder(inst, Py.Truthy(version) ? version : null!);
                Directory.CreateDirectory(dir);
                Crash.OpenPath(dir);
                return PyDict.Of("ok", true, "message", I18nCore.Tr("已打开 Mods 文件夹"));
            }
            case "open_crash_file":
            {
                string path = Py.Strip(Py.AsStr(Py.Or(action.Get("path"), report.Get("direct_file") ?? ""), "strip"));
                if (path.Length == 0 || !new PPath(path).IsFile())
                    return PyDict.Of("ok", false, "message", I18nCore.Tr("没有可打开的崩溃文件"));
                Crash.OpenPath(path);
                return PyDict.Of("ok", true, "message", I18nCore.Tr("已打开崩溃报告"));
            }
            case "open_gpu_hint":
                return PyDict.Of("ok", true, "message",
                    I18nCore.Tr("显卡/OpenGL 相关崩溃:请更新显卡驱动,关闭独显强制、超采样/滤镜,并确认不是远程桌面/虚拟机缺 OpenGL。"));
            case "reset_jvm_args":
            {
                Config.CONFIG.Set("default_jvm_args", "");
                Config.CONFIG.Save();
                try
                {
                    var inst = new Instance(Py.S(instance));
                    if (Py.Truthy(version))
                    {
                        PyDict st = VersionSettings.Load(inst, version);
                        st["jvm_args"] = "";
                        VersionSettings.Save(inst, version, st);
                    }
                }
                catch (Exception) { }
                Instance.RaiseEvent(new ApiEvent { Event = "ui_changed" });
                return PyDict.Of("ok", true, "message", I18nCore.Tr("已清空自定义 JVM 参数"));
            }
            default:
                return PyDict.Of("ok", false, "message", Py.Fmt(I18nCore.Tr("未知动作: {0}"), id));
        }
    }

    /// <summary>Py.Or 的「非空字符串」版(给可能为 null 的 object 用)。</summary>
    private static object PyOr(object? a, object b) => a == null ? b : (Py.Truthy(a) ? a : b);

    /// <summary>当前账号名,没账号时用 "Player"(照 Windows terracotta_player)。</summary>
    private static string TerracottaPlayer()
    {
        PyDict a = new AccountManager().GetActive();
        return a != null && a.Count > 0 && Py.Truthy(a["name"]) ? Py.Str(a["name"]) : "Player";
    }

    /// <summary>
    /// 记住大厅地址并「直接连服」启动最新版本(照 Windows `_launch_into_server`)。
    /// 返回启动任务 id(页面按 `task-` 前缀判定),游戏已在跑则返回去大厅的提示文案。
    /// </summary>
    private string LaunchIntoLobby(string url)
    {
        var inst = new Instance(Config.CONFIG.Get("default_instance", ""));
        Terracotta.RememberLobby(url, inst.path);

        PyDict snap = Terracotta.Snapshot(TerracottaPlayer(), LaunchCore.IsRunning);
        if (Py.Truthy(snap.Get("game_running")))
            return I18nCore.Tr("请到游戏「多人游戏」双击「陶瓦联机大厅」。");

        List<object> versions = inst.InstalledIds();
        if (versions.Count == 0)
            throw new LaunchError(I18nCore.Tr("请先到「启动」页安装一个版本。"));

        string latest = Py.S(versions[0]);
        DateTime newest = DateTime.MinValue;
        foreach (object v in versions)
        {
            DateTime when = Directory.GetLastWriteTimeUtc(PPath.Join(inst.VersionsDir(), Py.S(v)));
            if (when > newest)
            {
                newest = when;
                latest = Py.S(v);
            }
        }

        var (host, port) = Terracotta.SplitJoinUrl(url);
        var account = new AccountManager().GetActive();
        object accountName = Py.Eq(Py.Or(account != null ? account.Get("type") : "", ""), "microsoft")
            ? Py.Or(account!["name"], I18nCore.Tr("离线模式"))
            : I18nCore.Tr("离线模式");
        var args = new PyDict
        {
            ["instance"] = inst.name,
            ["version"] = latest,
            ["account"] = accountName,
            ["username"] = Py.Or(account != null ? account.Get("name") : "", TerracottaPlayer()),
            ["extra_game_args"] = Py.L("--server", host, "--port", port.ToString()),
        };
        return StartDetached(I18nCore.Tr("启动游戏") + " " + latest, (p, tok) => LaunchCore.RunAsync(p, tok, this, args)).Id;
    }

    /// <summary>
    /// 把用户选的 Java 路径折成可执行文件:传目录就找 bin/java(w)(Windows 找 .exe);
    /// 传的就是可执行文件则原样返回;都不是则返回空串(调用方决定报错文案)。
    /// </summary>
    private static string ResolveJavaExe(string path)
    {
        if (path.Length == 0)
            return "";
        if (LocalFs.IsFile(path))
            return path;
        if (!LocalFs.IsDir(path))
            return "";
        foreach (string rel in new[] { "bin/javaw.exe", "bin/java.exe", "bin/java", "javaw.exe", "java.exe", "java" })
        {
            string candidate = PPath.Join(path, rel);
            if (LocalFs.IsFile(candidate))
                return candidate;
        }
        return "";
    }

    // ───────────────────────────── 版本行（对齐 Windows 版 get_version_rows）─────────────────────────────

    private static readonly string[][] LoaderTags =
    {
        new[] { "neoforge", "NeoForge", "#D84B28" },
        new[] { "fabric", "Fabric", "#7C5CD6" },
        new[] { "quilt", "Quilt", "#C25BD6" },
        new[] { "forge", "Forge", "#E8862E" },
        new[] { "optifine", "OptiFine", "#2E9B6B" },
        new[] { "liteloader", "LiteLoader", "#4C8BF5" },
    };

    private static List<object> LoaderOf(string versionId)
    {
        string low = Py.Lower(versionId);
        foreach (var tag in LoaderTags)
            if (low.Contains(tag[0]))
                return new List<object> { tag[1], tag[2] };
        return new List<object> { I18nCore.Tr("原版"), "#8A9099" };
    }

    /// <summary>先按版本名识别加载器；看不出来再读版本 json 的 mainClass / libraries。</summary>
    private static List<object> LoaderOfVersion(Instance inst, string versionId)
    {
        List<object> named = LoaderOf(versionId);
        string name = Py.Str(named[0]);
        if (LoaderTags.Any((string[] t) => t[1] == name))
            return named;
        try
        {
            var json = (inst.VersionJson(versionId) as PyDict) ?? new PyDict();
            var sb = new System.Text.StringBuilder();
            sb.Append(Py.Lower(Py.S(json.Get("mainClass")))).Append('\n');
            foreach (object item in Py.ListOr(Py.Or(json.Get("libraries"), new List<object>())))
                if (item is PyDict lib)
                    sb.Append(Py.Lower(Py.S(lib.Get("name")))).Append('\n');
            string blob = sb.ToString();
            foreach (var pair in new[]
                     {
                         new[] { "net.neoforged", "NeoForge" }, new[] { "org.quiltmc", "Quilt" },
                         new[] { "net.fabricmc", "Fabric" }, new[] { "net.minecraftforge", "Forge" },
                         new[] { "optifine", "OptiFine" }, new[] { "liteloader", "LiteLoader" },
                     })
            {
                if (blob.Contains(pair[0]))
                {
                    var tag = LoaderTags.First((string[] t) => t[1] == pair[1]);
                    return new List<object> { tag[1], tag[2] };
                }
            }
        }
        catch (Exception) { }
        return named;
    }

    private static long CountMods(string folder)
    {
        if (!LocalFs.IsDir(folder)) return 0;
        return LocalFs.Children(folder).LongCount((string f) => LocalFs.IsFile(f) && Py.Lower(new PPath(f).Name).EndsWith(".jar"));
    }

    private static List<object> VersionRows(Instance inst, bool includeHidden)
    {
        var rows = new List<object>();
        foreach (string id in inst.InstalledIds())
        {
            PyDict st = VersionSettings.Load(inst, id);
            bool hidden = Py.Truthy(st.Get("hidden"));
            if (!hidden || includeHidden || Py.Truthy(Config.CONFIG.Get("show_hidden_versions")))
            {
                bool isolated = VersionSettings.IsIsolated(st);
                string modsDir = VersionSettings.ModsDir(inst, id, st);
                List<object> loader = LoaderOfVersion(inst, id);
                object isolation = Py.Or(st.Get("isolation"), "none");
                object label = isolation is string key && VersionSettings.ISOLATION_LABELS.TryGetValue(key, out object v) ? v : "";
                rows.Add(PyDict.Of(
                    "id", id,
                    "loader", loader[0],
                    "loader_color", loader[1],
                    "mc", JsonId(inst, id),
                    "isolation", isolation,
                    "isolation_label", I18nCore.Tr(Py.S(label)),
                    "isolated", isolated,
                    "mods", CountMods(modsDir),
                    "mods_dir", modsDir,
                    "hidden", hidden,
                    "java", Py.Or(st.Get("java"), "自动选择"),
                    "memory_mb", Py.Or(st.Get("memory_mb"), 0L)));
            }
        }
        return rows;
    }

    private static string JsonId(Instance inst, string versionId)
    {
        try
        {
            var json = (inst.VersionJson(versionId) as PyDict) ?? new PyDict();
            return Py.Str(Py.Or(json.Get("inheritsFrom"), json.Get("id"), versionId));
        }
        catch (Exception) { return versionId; }
    }

    // ───────────────────────────── 参数工具 ─────────────────────────────

    /// <summary>匿名对象 / 字典 → PyDict（键保持原样，页面侧用 snake_case 传参）。</summary>
    public static PyDict ToDict(object? args)
    {
        var d = new PyDict();
        if (args == null) return d;
        if (args is PyDict pd)
        {
            foreach (var kv in pd) d[kv.Key] = Deep(kv.Value)!;
            return d;
        }
        if (args is IDictionary<string, object> dict)
        {
            foreach (var kv in dict) d[kv.Key] = Deep(kv.Value)!;
            return d;
        }
        foreach (var p in args.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (p.CanRead && p.GetValue(args) is { } v)
                d[p.Name] = Deep(v)!;
        return d;
    }

    /// <summary>
    /// 参数里的嵌套值也要落成 PyDict / List&lt;object&gt;。
    /// 页面普遍用匿名对象传参(如 `save_settings(new { data = new { ui_dark = true } })`),
    /// 只做一层浅转换的话 `args.Get("data") is PyDict` 判定失败 → 设置静默不生效。
    /// </summary>
    private static object? Deep(object? v)
    {
        switch (v)
        {
            case null:
                return null;
            case PyDict or string or bool or long or double or decimal:
                return v;
            // Core 按 Python 语义只认 long / double(如 Servers.IndexArg 见到 int 会抛 TypeError)
            case int or short or byte or sbyte or ushort or uint:
                return Convert.ToInt64(v);
            case float f:
                return (double)f;
            case IDictionary<string, string> ss:
            {
                var o = new PyDict();
                foreach (var kv in ss) o[kv.Key] = kv.Value;
                return o;
            }
            case IDictionary<string, object> dd:
            {
                var o = new PyDict();
                foreach (var kv in dd) o[kv.Key] = Deep(kv.Value)!;
                return o;
            }
            case System.Collections.IEnumerable e:
            {
                var list = new List<object?>();
                foreach (var item in e) list.Add(Deep(item));
                return list;
            }
            default:
            {
                var props = v.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                if (props.Length == 0) return v;
                var o = new PyDict();
                foreach (var p in props)
                    if (p.CanRead && p.GetValue(v) is { } pv)
                        o[p.Name] = Deep(pv)!;
                return o;
            }
        }
    }

    /// <summary>
    /// 把 Core 侧返回的 PyDict / List&lt;object&gt; 动态结构落成调用方要的 POCO / 字典。
    /// CallCore 统一返回 PyDict,而部分页面按 POCO 取结果(清理预览、智能推荐、语言表…);
    /// 没有这层转换,那些 POCO 永远是 null。`is T` 命中的路径不受影响。
    /// </summary>
    private static object? Coerce(Type target, object value)
    {
        try
        {
            string json = JsonSerializer.Serialize(Plain(value), JsonOpts);
            return JsonSerializer.Deserialize(json, target, JsonOpts);
        }
        catch (Exception ex)
        {
            // 不能静默。这一层的失败在页面上只表现为「空列表 / 默认值」,一行日志都没有时无从下手 ——
            // 「下载页装进存档」下拉永远弹不出来就是从这里开始无声无息的(SaveRow.Mtime 声明成 string,
            // 而 list_saves 给的是数字)。日志里带目标类型与异常原文,JsonException 会直接点名是哪个字段。
            AppLog.Warn($"API 结果转换失败({target.FullName}): {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>PyDict / List&lt;object&gt; → 纯字典 / 数组,交给 System.Text.Json 落 POCO。</summary>
    private static object? Plain(object? v)
    {
        switch (v)
        {
            case null:
                return null;
            case PyDict pd:
            {
                var o = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in pd) o[kv.Key] = Plain(kv.Value);
                return o;
            }
            case string or bool or long or int or double or float or decimal:
                return v;
            case System.Collections.IEnumerable e:
            {
                var list = new List<object?>();
                foreach (var item in e) list.Add(Plain(item));
                return list;
            }
            default:
                return v;
        }
    }
}
