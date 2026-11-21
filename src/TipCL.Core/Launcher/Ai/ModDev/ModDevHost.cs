// AI2-moddev-mcp:模组开发测试环境的注入点与后台任务表。 // i18n:ignore-file(这里的中文是工具结果 / 任务步骤文字,回给模型与外部 agent)
//
// 注入点放在这个独立静态类里(不放页面类):测试在任意线程设置它们都不会触发 App 层页面的静态初始化。
//   · GameInstaller:create_test_instance 装 MC 本体(默认走 Core 的 GameInstall.InstallGame;测试注入假的,不联网);
//   · DependencySources(AI3-moddev):依赖模组的来源链,默认 [Modrinth, CurseForge] —— 先查 Modrinth,找不到(或出错)
//     回落 CurseForge(复用 Mods.SearchCurseforge + Mods.InstallCurseforgeMod,与下载页同一套函数),两边都没有就如实报错;
//     测试注入假源,不联网。DependencyInstaller 是更早的整体替换口(设了就不走来源链),保留给老测试;
//   · LaunchDriverFactory:run_test_launch 怎么起游戏、读日志、关游戏(默认 BackendLaunchDriver:经 IApi 的 launch_game 起,
//     只关自己拉起的那个进程,见 LaunchedGames.cs;**测试一律注入假驱动,绝不真启动游戏**);
//   · HostTaskProbe:App 注入,按 task_id 读启动器自己的任务表(TaskStore),供 get_task_status 查
//     install_* / launch_game 起的任务,以及默认驱动读游戏输出。
//
// ModDevTasks:本包自己的后台任务(create_test_instance 的安装、run_test_launch)。外部 MCP 调用方拿到 task_id 后
// 用 get_task_status 查进度(选「查询工具」而不是 MCP progress notification:本机 MCP Server 只有 POST,没有 SSE 流,
// 推不了通知;查询工具可测、两种客户端都能用)。
// AI3-moddev:任务记录落盘到 <数据目录>/cache/moddev_tasks.json(最近 50 条,临时文件 + 原子替换),
// 启动器重启后 get_task_status 仍能查到已结束任务的结果;重启前还在跑的任务读回来时记为 interrupted。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>依赖模组的一个来源(Modrinth / CurseForge / 测试假源)。</summary>
public interface IDependencySource
{
    /// <summary>来源名(modrinth / curseforge …),写进结果的 source 字段。</summary>
    string Name { get; }

    /// <summary>
    /// 把 <paramref name="slug"/> 装进 <paramref name="modsDir"/>,返回装好的文件名。
    /// 这个来源上没有这个模组 ⇒ 抛 <see cref="DependencyNotFoundException"/>;其它失败(网络等)抛别的异常。
    /// </summary>
    string Install(Instance inst, string slug, string mc, string loader, string modsDir, Action<string> log, CancellationToken ct);
}

/// <summary>来源上找不到这个模组。</summary>
public sealed class DependencyNotFoundException : Exception
{
    public DependencyNotFoundException(string message) : base(message)
    {
    }
}

/// <summary>所有来源都没装上。<see cref="Tried"/> 是每个来源的尝试记录 {source, not_found, error}。</summary>
public sealed class DependencyInstallException : Exception
{
    public DependencyInstallException(string message, List<object> tried) : base(message) => Tried = tried;

    public List<object> Tried { get; }
}

/// <summary>默认来源一:Modrinth(Mods.InstallModFromSource,按 slug)。</summary>
public sealed class ModrinthDependencySource : IDependencySource
{
    public string Name => "modrinth";

    public string Install(Instance inst, string slug, string mc, string loader, string modsDir, Action<string> log, CancellationToken ct)
    {
        var dm = new DownloadManager(4L, (msg, cur, total) => { }, () => ct.IsCancellationRequested, null, ct);
        PyDict res;
        try
        {
            res = Mods.InstallModFromSource(dm, slug, inst, mc, loader, dm.on_progress, null, modsDir);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ModDevHost.LooksNotFound(ex.Message))
        {
            throw new DependencyNotFoundException("Modrinth 上没有「" + slug + "」(" + ex.Message + ")");
        }
        return ModDevHost.FirstFile(res, slug);
    }
}

/// <summary>默认来源二:CurseForge(按 slug 搜项目 id,纯数字直接当项目 id;再 Mods.InstallCurseforgeMod)。</summary>
public sealed class CurseForgeDependencySource : IDependencySource
{
    public string Name => "curseforge";

    public string Install(Instance inst, string slug, string mc, string loader, string modsDir, Action<string> log, CancellationToken ct)
    {
        var dm = new DownloadManager(4L, (msg, cur, total) => { }, () => ct.IsCancellationRequested, null, ct);
        object id;
        if (slug.Length > 0 && slug.All(char.IsDigit))
        {
            id = long.Parse(slug, CultureInfo.InvariantCulture);
        }
        else
        {
            List<object> hits = Mods.SearchCurseforge(dm, null, 5L, null, 6L, slug);
            PyDict? hit = hits.OfType<PyDict>().FirstOrDefault(h => string.Equals(Py.S(h.Get("slug")), slug, StringComparison.OrdinalIgnoreCase))
                ?? hits.OfType<PyDict>().FirstOrDefault();
            if (hit == null || !Py.Truthy(hit.Get("id")))
            {
                throw new DependencyNotFoundException("CurseForge 上没有 slug 为「" + slug + "」的模组");
            }
            id = hit.Get("id")!;
        }
        ct.ThrowIfCancellationRequested();
        PyDict res = Mods.InstallCurseforgeMod(dm, id, inst, mc, loader, null, dm.on_progress, null, modsDir);
        return ModDevHost.FirstFile(res, slug);
    }
}

/// <summary>模组开发测试环境的注入点(测试替换;产品由 App 在启动时设 <see cref="HostTaskProbe"/>)。</summary>
public static class ModDevHost
{
    /// <summary>装 MC 本体 + 加载器:(实例, mc 版本, 加载器, 加载器版本, 日志, 取消) → 装好的版本 id。null = 用默认实现。</summary>
    public static Func<Instance, string, string, string, Action<string>, CancellationToken, string>? GameInstaller { get; set; }

    /// <summary>整体替换依赖安装:(实例, slug, mc 版本, 加载器, 日志, 取消) → 装好的文件名。设了就不走来源链。null = 走 <see cref="DependencySources"/>。</summary>
    public static Func<Instance, string, string, string, Action<string>, CancellationToken, string>? DependencyInstaller { get; set; }

    /// <summary>AI3-moddev:依赖来源链(按顺序尝试)。null = 默认 [Modrinth, CurseForge]。</summary>
    public static IReadOnlyList<IDependencySource>? DependencySources { get; set; }

    /// <summary>run_test_launch 的启动驱动工厂。null = 默认驱动(经 IApi 的 launch_game,只关自己拉起的进程)。</summary>
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
        DependencySources = null;
        LaunchDriverFactory = null;
        HostTaskProbe = null;
    }

    /// <summary>默认依赖来源链:先 Modrinth,再 CurseForge。</summary>
    public static IReadOnlyList<IDependencySource> DefaultDependencySources()
        => new IDependencySource[] { new ModrinthDependencySource(), new CurseForgeDependencySource() };

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

    /// <summary>
    /// 依赖写法:slug(先 Modrinth 后 CurseForge);「modrinth:slug」/「mr:slug」只查 Modrinth;
    /// 「curseforge:slug」/「cf:slug」/「cf:项目id」只查 CurseForge。返回 (来源限定, 去掉前缀的 slug)。
    /// </summary>
    public static (string Only, string Slug) ParseDependency(string spec)
    {
        string s = (spec ?? "").Trim();
        int colon = s.IndexOf(':');
        if (colon > 0)
        {
            string prefix = s.Substring(0, colon).Trim().ToLowerInvariant();
            string rest = s.Substring(colon + 1).Trim();
            if (prefix is "modrinth" or "mr")
            {
                return ("modrinth", rest);
            }
            if (prefix is "curseforge" or "cf" or "curse")
            {
                return ("curseforge", rest);
            }
        }
        return ("", s);
    }

    /// <summary>
    /// 装一个依赖,返回 {file, source, tried}。所有来源都没装上 ⇒ 抛 <see cref="DependencyInstallException"/>
    /// (消息写清每个来源的结果)。取消照常抛 OperationCanceledException。
    /// </summary>
    internal static PyDict InstallDependency(Instance inst, string spec, string mc, string loader, Action<string> log, CancellationToken ct)
    {
        if (DependencyInstaller != null)
        {
            return PyDict.Of("file", DependencyInstaller(inst, spec, mc, loader, log, ct), "source", "custom", "tried", new List<object>());
        }
        (string only, string slug) = ParseDependency(spec);
        if (slug.Length == 0)
        {
            throw new DependencyInstallException("依赖写法不对: " + spec, new List<object>());
        }
        List<IDependencySource> sources = (DependencySources ?? DefaultDependencySources())
            .Where(s => only.Length == 0 || string.Equals(s.Name, only, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (sources.Count == 0)
        {
            throw new DependencyInstallException("没有可用的依赖来源(" + (only.Length > 0 ? only : "全部") + ")", new List<object>());
        }
        string modsDir = ModDevOps.ModsDirOf(inst);
        Utils.EnsureDir(modsDir);
        var tried = new List<object>();
        foreach (IDependencySource src in sources)
        {
            ct.ThrowIfCancellationRequested();
            log("从 " + SourceLabel(src.Name) + " 安装依赖 " + slug);
            try
            {
                string file = src.Install(inst, slug, mc, loader, modsDir, log, ct);
                if (tried.Count > 0)
                {
                    log("依赖 " + slug + " 已从 " + SourceLabel(src.Name) + " 装好(前面的来源没有)");
                }
                return PyDict.Of("file", file, "source", src.Name, "tried", tried);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (DependencyNotFoundException ex)
            {
                tried.Add(PyDict.Of("source", src.Name, "not_found", true, "error", ex.Message));
                log(SourceLabel(src.Name) + " 上找不到 " + slug);
            }
            catch (Exception ex)
            {
                tried.Add(PyDict.Of("source", src.Name, "not_found", false, "error", ex.Message));
                log(SourceLabel(src.Name) + " 安装 " + slug + " 失败: " + ex.Message);
            }
        }
        bool allMissing = tried.OfType<PyDict>().All(t => Py.Truthy(t.Get("not_found")));
        string names = string.Join("、", sources.Select(s => SourceLabel(s.Name)));
        string detail = string.Join("; ", tried.OfType<PyDict>().Select(t => SourceLabel(Py.S(t.Get("source"))) + ": " + Py.S(t.Get("error"))));
        throw new DependencyInstallException(
            allMissing ? names + " 上都找不到依赖「" + slug + "」(" + detail + ")" : "依赖「" + slug + "」没装上(" + detail + ")",
            tried);
    }

    internal static string SourceLabel(string name) => name.ToLowerInvariant() switch
    {
        "modrinth" => "Modrinth",
        "curseforge" => "CurseForge",
        _ => name,
    };

    /// <summary>Modrinth 报错文字像不像「没有这个项目 / 没有可下载版本」(粗判;判不准也没关系,出错同样会回落 CurseForge)。</summary>
    internal static bool LooksNotFound(string message)
    {
        string m = (message ?? "").ToLowerInvariant();
        return m.Contains("404") || m.Contains("not found") || m.Contains("没有可下载") || m.Contains("找不到") || m.Contains("不存在");
    }

    /// <summary>安装结果里取装好的文件名(files[0] / file / filename),都没有就用 slug。</summary>
    internal static string FirstFile(PyDict res, string fallback)
    {
        if (res.Get("files") is List<object> files && files.Count > 0)
        {
            return Py.S(files[0]);
        }
        return Py.S(Py.Or(res.Get("file"), res.Get("filename"), fallback));
    }
}

/// <summary>本包的一个后台任务(create_test_instance 的安装 / run_test_launch)。</summary>
public sealed class ModDevTask
{
    private readonly object _gate = new();
    private readonly List<string> _log = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly double? _restoredElapsed;
    private DateTime _lastPersist = DateTime.MinValue;

    internal ModDevTask(string id, string kind, string title)
    {
        Id = id;
        Kind = kind;
        Title = title;
        StartedAt = DateTime.Now;
    }

    /// <summary>从落盘记录还原(启动器重启后读回来的;已结束,不会再变)。</summary>
    private ModDevTask(PyDict rec)
    {
        Id = Py.S(rec.Get("id"));
        Kind = Py.S(rec.Get("kind"));
        Title = Py.S(rec.Get("title"));
        Restored = true;
        _clock.Stop();
        StartedAt = ParseTime(Py.S(rec.Get("started_at"))) ?? DateTime.MinValue;
        FinishedAt = ParseTime(Py.S(rec.Get("finished_at")));
        _restoredElapsed = Py.Float(Py.Or(rec.Get("elapsed_s"), 0L));
        Step = Py.S(rec.Get("step"));
        Progress = (int)Py.Int(Py.Or(rec.Get("progress"), -1L));
        Result = rec.Get("result") as PyDict;
        Error = Py.S(rec.Get("error"));
        foreach (object l in Py.ListOr(rec.Get("log_tail")))
        {
            _log.Add(Py.S(l));
        }
        string state = Py.S(rec.Get("state"));
        if (state == "running" || state.Length == 0)
        {
            State = "interrupted";
            Error = "启动器在这个任务结束之前退出 / 重启了,任务没有跑完(读回记录时标为 interrupted);需要的话重新发起";
        }
        else
        {
            State = state;
        }
        Done.TrySetResult(true);
    }

    public string Id { get; }

    /// <summary>发起它的工具名(create_test_instance / run_test_launch)。</summary>
    public string Kind { get; }

    public string Title { get; }

    /// <summary>running / succeeded / failed / cancelled;启动器重启后读回的未完成任务为 interrupted。</summary>
    public string State { get; private set; } = "running";

    public string Step { get; private set; } = "";

    /// <summary>0..100;-1 = 不确定。</summary>
    public int Progress { get; private set; } = -1;

    public DateTime StartedAt { get; }

    public DateTime? FinishedAt { get; private set; }

    public PyDict? Result { get; private set; }

    public string Error { get; private set; } = "";

    public bool Finished => State != "running";

    /// <summary>这条是不是从落盘记录读回来的(上一次启动器进程里跑的)。</summary>
    public bool Restored { get; }

    internal CancellationTokenSource Cts { get; } = new();

    internal TaskCompletionSource<bool> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public double ElapsedSeconds => _restoredElapsed ?? Math.Round(_clock.Elapsed.TotalSeconds, 1);

    /// <summary>更新当前步骤(<paramref name="log"/> 为 true 时同时记一行日志)。步骤变化最多每 2 秒落一次盘。</summary>
    public void SetStep(string step, int progress = -1, bool log = true)
    {
        bool persist = false;
        lock (_gate)
        {
            Step = step ?? "";
            if (progress >= 0)
            {
                Progress = Math.Clamp(progress, 0, 100);
            }
            if (!Finished && DateTime.UtcNow - _lastPersist > TimeSpan.FromSeconds(2))
            {
                _lastPersist = DateTime.UtcNow;
                persist = true;
            }
        }
        if (log)
        {
            Log(step ?? "");
        }
        if (persist)
        {
            ModDevTasks.Persist();
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
        // 先落盘再通知等待方:等待方一醒来就能在盘上看到结果(之后启动器立刻退出也不丢)
        ModDevTasks.Persist();
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
                "started_at", FormatTime(StartedAt),
                "log_tail", _log.Skip(Math.Max(0, _log.Count - Math.Max(0, logLines))).Cast<object>().ToList());
            if (FinishedAt is { } fin)
            {
                d["finished_at"] = FormatTime(fin);
            }
            if (Restored)
            {
                d["restored"] = true;
            }
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

    /// <summary>落盘用的记录(日志只留最后 <paramref name="logLines"/> 行)。</summary>
    internal PyDict ToRecord(int logLines)
    {
        lock (_gate)
        {
            var d = PyDict.Of(
                "id", Id,
                "kind", Kind,
                "title", Title,
                "state", State,
                "step", Step,
                "progress", (long)Progress,
                "elapsed_s", ElapsedSeconds,
                "started_at", FormatTime(StartedAt),
                "error", Error,
                "log_tail", _log.Skip(Math.Max(0, _log.Count - logLines)).Cast<object>().ToList());
            if (FinishedAt is { } fin)
            {
                d["finished_at"] = FormatTime(fin);
            }
            if (Result != null)
            {
                d["result"] = Result;
            }
            return d;
        }
    }

    internal static ModDevTask? FromRecord(object? rec)
    {
        if (rec is not PyDict d || Py.S(d.Get("id")).Length == 0)
        {
            return null;
        }
        try
        {
            return new ModDevTask(d);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string FormatTime(DateTime t) => t == DateTime.MinValue ? "" : t.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    private static DateTime? ParseTime(string s)
        => DateTime.TryParseExact(s, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime t) ? t : null;
}

/// <summary>本包后台任务的登记表(进程内 + 落盘)+ get_task_status 的实现。</summary>
public static class ModDevTasks
{
    /// <summary>最多保留多少条(内存与落盘同一上限;没结束的不丢)。</summary>
    public const int MaxTasks = 50;

    /// <summary>落盘记录里每条任务留多少行日志。</summary>
    public const int PersistLogLines = 40;

    private static readonly object _gate = new();
    private static readonly object _fileGate = new();
    private static readonly object _loadGate = new();
    private static readonly List<ModDevTask> _tasks = new();
    private static volatile bool _loaded;

    /// <summary>任务记录文件:&lt;数据目录&gt;/cache/moddev_tasks.json(数据目录随 TIPCL_HOME)。</summary>
    public static string StorePath => PPath.Join(Utils.ROOT, "cache", "moddev_tasks.json");

    /// <summary>第一次用到时把上次落盘的记录读回来(重启前没跑完的记为 interrupted 并立即写回)。</summary>
    private static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }
        bool interrupted;
        // 整个读取放在 _loadGate 里:别的线程在读完之前不会拿一张缺了旧记录的表去写盘
        lock (_loadGate)
        {
            if (_loaded)
            {
                return;
            }
            var restored = new List<ModDevTask>();
            try
            {
                object? raw = Utils.ReadJson(StorePath);
                IEnumerable<object> rows = raw is PyDict root ? Py.ListOr(root.Get("tasks")) : (raw is List<object> l ? l : new List<object>());
                foreach (object row in rows)
                {
                    if (ModDevTask.FromRecord(row) is { } t)
                    {
                        restored.Add(t);
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.log.Warning("读取模组测试任务记录失败 %s: %s", StorePath, ex.Message);
            }
            interrupted = restored.Any(t => t.State == "interrupted");
            lock (_gate)
            {
                var have = new HashSet<string>(_tasks.Select(t => t.Id), StringComparer.Ordinal);
                _tasks.InsertRange(0, restored.Where(t => !have.Contains(t.Id)).TakeLast(MaxTasks));
                Trim();
            }
            _loaded = true;
        }
        if (interrupted)
        {
            Persist();
        }
    }

    /// <summary>只留最近 MaxTasks 个:没结束的不丢。调用方持有 _gate。</summary>
    private static void Trim()
    {
        while (_tasks.Count > MaxTasks)
        {
            int i = _tasks.FindIndex(t => t.Finished);
            if (i < 0)
            {
                break;
            }
            _tasks.RemoveAt(i);
        }
    }

    /// <summary>把当前任务表写盘(临时文件 + 原子替换,见 Utils.WriteJson)。失败只记日志,不影响任务本身。</summary>
    public static void Persist()
    {
        // 先确保读过盘上的记录:否则「还没读就写」会用一张空表盖掉上次的记录
        EnsureLoaded();
        lock (_fileGate)
        {
            List<object> rows;
            lock (_gate)
            {
                rows = _tasks.TakeLast(MaxTasks).Select(t => (object)t.ToRecord(PersistLogLines)).ToList();
            }
            try
            {
                Utils.WriteJson(StorePath, PyDict.Of("version", 1L, "tasks", rows));
            }
            catch (Exception ex)
            {
                Utils.log.Warning("写模组测试任务记录失败 %s: %s", StorePath, ex.Message);
            }
        }
    }

    /// <summary>
    /// 模拟启动器重启(只给测试用):丢掉进程内的表,下次访问时重新从 <see cref="StorePath"/> 读。
    /// 不取消正在跑的任务;被丢掉的任务之后再结束也不会写回(落盘写的是表里现有的任务)。
    /// </summary>
    public static void ReloadForTests()
    {
        lock (_gate)
        {
            _tasks.Clear();
            _loaded = false;
        }
    }

    /// <summary>
    /// 起一个后台任务:立即返回任务对象(id 可交给调用方),工作体在线程池上跑。
    /// 工作体返回结果字典;字典里 ok=false 记为 failed,抛异常记为 failed,取消记为 cancelled。
    /// </summary>
    public static ModDevTask Start(string kind, string title, Func<ModDevTask, CancellationToken, Task<PyDict>> work)
    {
        EnsureLoaded();
        var task = new ModDevTask("dev-" + Guid.NewGuid().ToString("N").Substring(0, 12), kind, title);
        lock (_gate)
        {
            _tasks.Add(task);
            Trim();
        }
        Persist();
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
        EnsureLoaded();
        lock (_gate)
        {
            return _tasks.FirstOrDefault(t => t.Id == id);
        }
    }

    /// <summary>某类任务里还没结束的那一个(run_test_launch 同一时间只允许一个)。</summary>
    public static ModDevTask? RunningOf(string kind)
    {
        EnsureLoaded();
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
    /// get_task_status:先查本包的任务表(含上次启动器进程落盘的记录),再查启动器任务表(HostTaskProbe);
    /// <paramref name="waitSeconds"/> &gt; 0 时最多等这么久直到任务结束再回(减少外部调用方的轮询次数)。
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
            "message", "没有这个任务。create_test_instance / run_test_launch 的记录只保留最近 " + MaxTasks + " 条(启动器重启后仍可查);"
            + "install_* / launch_game 起的启动器任务只在本次启动器运行期间可查。task_id 来自这些工具的返回");
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
