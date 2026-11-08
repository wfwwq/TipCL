// M3-C0 启动链门面：参数组装 / 进程拉起 / stdout 日志桥 / 崩溃回传。
// 逻辑对齐 Windows 版 Core/BackendApi.cs 的 _launch_game_impl（插件 launch.before/after 钩子未接入，
// Core/Plugins 宿主运行时按 M2 决定未搬，属 M3-C1 地盘）。
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;

namespace TipCL.App.Common.Launch;

/// <summary>
/// 启动链门面：IApi.launch_game 底层走这里。进程内拉起 Java 子进程，
/// stdout/stderr 逐行回传（TaskProgress 日志 + IApi log 事件），
/// 退出后崩溃分析，崩溃时抛 GameCrashError（Report 字典随任务失败信息回传 UI）。
/// </summary>
public static class LaunchCore
{
    private static readonly object _gameLock = new();
    private static Process _gameProc;

    /// <summary>当前游戏进程是否存活（is_game_running）。</summary>
    public static bool IsRunning
    {
        get
        {
            lock (_gameLock)
            {
                var p = _gameProc;
                if (p == null) return false;
                try { return !p.HasExited; }
                catch (Exception) { return false; }
            }
        }
    }

    /// <summary>杀掉当前游戏进程（stop_game）。</summary>
    public static bool Stop()
    {
        Process p;
        lock (_gameLock) p = _gameProc;
        if (p == null) return false;
        try
        {
            if (!p.HasExited) p.Kill(entireProcessTree: true);
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// 启动游戏主流程（在 IApi.StartTaskAsync 的 work 回调里执行）。
    /// args 键与 Windows 版 launch_game 一致：instance/version/account/username/memory_mb/width/height/java/extra_game_args/force。
    /// </summary>
    public static async Task RunAsync(TaskProgress progress, CancellationToken ct, IApi api, PyDict args)
    {
        void Log(string t) => progress.Log(t);

        string version = Py.S(args.Get("version"));
        if (version.Length == 0)
            throw new LaunchError(I18nCore.Tr("请先选择版本（到「版本」页安装）"));

        if (!Py.Truthy(Config.CONFIG.Get("allow_multi_instance", false)) && IsRunning)
            throw new LaunchError(I18nCore.Tr("游戏正在运行中\n若要同时运行多个游戏，请到设置开启「允许多开」"));

        object javaPref = args.Get("java");
        string javaExe = "";
        if (Py.Truthy(javaPref) && !Py.Eq(javaPref, "自动选择") && !Py.Eq(javaPref, "auto") && !Py.Eq(javaPref, "default") && !Py.Eq(javaPref, ""))
            javaExe = Py.Str(javaPref);

        Instance inst = InstancesApi.EnsureInstance(args.Get("instance"));
        PyDict pre = Preflight.CheckLaunch(inst, version, Py.Int(Py.Or(args.Get("memory_mb"), 0L)), javaExe);
        var items = Py.ListOr(Py.Or(pre.Get("items"), new List<object>())).Cast<PyDict>().ToList();
        foreach (var item in items)
        {
            object level = item.Get("level");
            if (Py.Eq(level, "error") || Py.Eq(level, "warn"))
                Log("[预检:" + Py.Str(level) + "] " + Py.Str(item.Get("title")) + ": " + Py.Str(item.Get("detail")));
        }
        if (!Py.Truthy(pre.Get("ok", true)))
        {
            var errors = items.Where((PyDict it) => Py.Eq(it.Get("level"), "error")).ToList();
            bool force = Py.Truthy(args.Get("force"));
            if (!force)
            {
                string detail = string.Join("\n\n", errors.Select((PyDict e) => "· " + Py.Str(e.Get("title")) + "\n" + Py.Str(e.Get("detail"))));
                if (detail.Length == 0) detail = I18nCore.Tr("启动预检未通过");
                throw new LaunchError(I18nCore.Tr("启动预检未通过") + "\n\n" + detail);
            }
            Log("[预检:强制启动] 忽略 " + errors.Count + " 条 error 强制启动：" + string.Join("; ", errors.Select((PyDict e) => Py.Str(e.Get("code")) + "·" + Py.Str(e.Get("title")))));
        }

        Log("实例: " + inst.name + " | 版本: " + version);
        Log("实例 Java 设置: " + inst.JavaPref());
        Config.CONFIG.Set("default_instance", inst.name);
        Config.CONFIG.Save();

        // 版本绑定账号优先
        object bound = Py.Or(VersionSettings.Load(inst, version).Get("login_account"), "");
        object account = args.Get("account");
        if (Py.Truthy(bound))
        {
            account = bound;
            Log("该版本绑定账号: " + Py.Str(bound));
        }
        var accounts = new AccountManager();
        PyDict accountDict = RequireLaunchAccount(accounts, account, args.Get("username"));
        PyDict props = accounts.LaunchProps(accountDict);
        string accountKind = Py.Eq(props.Get("user_type"), "msa") ? I18nCore.Tr("正版")
            : (Py.Truthy(props.Get("authlib_api")) ? I18nCore.Tr("皮肤站")
            : (Py.Truthy(props.Get("nide8_id")) ? I18nCore.Tr("统一通行证") : I18nCore.Tr("离线")));
        Log("账号: " + Py.Str(props.Get("name")) + " (" + accountKind + ")");

        object memoryMb = args.Get("memory_mb");
        object width = args.Get("width");
        object height = args.Get("height");
        Log("内存: " + Py.Str(memoryMb) + " MB | 分辨率: " + Py.Str(width) + "x" + Py.Str(height));

        PyDict prep = LaunchFlow.Prepare(inst, version, ArgList(args.Get("extra_game_args")), memoryMb);
        memoryMb = Py.Or(prep["memory_mb"], memoryMb);
        var extraGameArgs = (List<object>)prep["extra_game_args"];
        string gameDir = (string)prep["game_dir"];
        PyDict settings = (PyDict)prep["settings"];
        LaunchFlow.RunHook(Py.Str(Py.Or(settings.Get("pre_launch"), "")), gameDir, Log, Py.Truthy(prep.Get("pre_launch_wait", true)));

        ct.ThrowIfCancellationRequested();
        progress.Report(I18nCore.Tr("检查 Java"), 1, 4);

        PyDict versionJson0 = (Py.Or(inst.VersionJson(version), new PyDict()) as PyDict) ?? new PyDict();
        PyDict versionJson;
        try { versionJson = Manifest.ResolveInherits(versionJson0, (string pid) => inst.VersionJson(pid) as PyDict); }
        catch (Exception) { versionJson = versionJson0; }

        // Java 解析：参数 java → 版本设置 java → 实例 JavaPref → 全局 default_java
        object javaSel = null;
        object javaArg = args.Get("java");
        if (Py.Truthy(settings.Get("java")) && !Py.Eq(settings["java"], "自动选择"))
            javaArg = settings["java"];
        if (!Py.Truthy(javaArg) || Py.Eq(javaArg, "自动选择"))
            javaArg = inst.JavaPref();
        if (!Py.Truthy(javaArg) || Py.Eq(javaArg, "自动选择"))
            javaArg = Py.Or(Config.CONFIG.Get("default_java"), "");
        if (Py.Truthy(javaArg) && !Py.Eq(javaArg, "自动选择"))
        {
            foreach (PyDict j in Java.AllJavas())
            {
                if (Py.Eq(j.Get("name"), javaArg) || Py.Eq(j.Get("exe"), javaArg))
                {
                    javaSel = j.Get("exe");
                    break;
                }
            }
            if (!Py.Truthy(javaSel) && new PPath(Py.Str(javaArg)).IsFile())
                javaSel = javaArg;
        }

        long requiredMajor = Java.RequiredJavaMajor(versionJson);
        string javaPath = Java.ResolveLaunchJava(versionJson, javaSel, null, null, Log);
        if (!Java.JavaUsableFor(versionJson, javaPath))
        {
            Log("未找到 Java " + requiredMajor + "，自动下载中…");
            var dm = MakeDownloader(progress, ct);
            javaPath = Java.ResolveLaunchJava(versionJson, null, dm, dm.on_progress, Log);
        }
        string javaVersionLine = (from l in Py.SplitLines(Java.JavaVersionOutput(javaPath) ?? "")
                                  select Py.Strip(l)).FirstOrDefault((string l) => l.Length > 0) ?? "?";
        Log("Java -version: " + javaVersionLine);
        long? major = Java.GetJavaMajor(javaPath);
        Log("使用 Java " + ((major.HasValue && major.Value != 0) ? major.Value.ToString() : "?") + ": " + Py.Str(javaPath));

        progress.Report(I18nCore.Tr("构建启动参数"), 2, 4);
        if (Py.Truthy(props.Get("authlib_api")))
        {
            var dmA = MakeDownloader(progress, ct);
            Authlib.EnsureInjector(dmA, Log);
        }
        if (Py.Truthy(props.Get("nide8_id")) || Py.Truthy(prep.Get("nide8_id")))
        {
            var dmN = MakeDownloader(progress, ct);
            Nide8.EnsureJar(dmN, Log);
            if (Py.Truthy(prep.Get("nide8_id")) && !Py.Truthy(props.Get("nide8_id")))
            {
                props = new PyDict(props);
                props["nide8_id"] = prep["nide8_id"];
            }
        }

        object[] res = LaunchFlow.ResolveResolution(prep, width, height);
        var lc = McLauncher.BuildLaunchCommand(inst, version, props, javaPath, memoryMb, res[0], res[1], extraGameArgs, prep["jvm_args"], gameDir, props.Get("authlib_api"));
        gameDir = lc.game_dir;

        Log("实际启动: " + lc.cmd[0]);
        Log(I18nCore.Tr("正在启动游戏进程…"));
        progress.Report(I18nCore.Tr("游戏启动中"), 3, 4);

        string instName = inst.name;
        void GameLine(string line)
        {
            Log(line);
            api.RaiseEvent(new ApiEvent { Event = "log", Message = line });
        }
        var gp = new GameProcess(lc.cmd, gameDir, GameLine, null, Py.Str(prep["priority"]), Py.Str(Py.Or(prep.Get("window_title"), "")), start_readers: false);
        lock (_gameLock) _gameProc = gp.proc;
        long pid = 0;
        try { pid = gp.proc.Id; } catch (Exception) { }
        api.RaiseEvent(new ApiEvent { Event = "game_started", Message = instName, Text = version, Current = pid });
        gp.StartReaders();

        PlaytimeTracker playtime = null;
        try { playtime = new PlaytimeTracker(instName, version); }
        catch (Exception) { playtime = null; }
        playtime?.Start();
        object exitCode;
        try
        {
            exitCode = await Task.Run(() => (object)gp.Wait(), CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (playtime != null)
            {
                try
                {
                    long secs = playtime.Stop();
                    if (Py.Truthy(secs)) Log("本次游玩 " + Py.Str(Playtime.FormatDuration(secs)));
                }
                catch (Exception) { }
            }
            lock (_gameLock)
            {
                if (_gameProc == gp.proc) _gameProc = null;
            }
            api.RaiseEvent(new ApiEvent { Event = "game_exited", Message = instName, Text = version, Current = pid });
        }
        ct.ThrowIfCancellationRequested();

        Log("游戏已退出，退出码 " + Py.Str(exitCode));
        LaunchFlow.RunHook(Py.Str(Py.Or(settings.Get("post_launch"), "")), gameDir, Log);
        PyDict crash = Crash.AnalyzeLaunch(inst, exitCode, gp.LastLines(), gp.started_at, cancelled: false, version, new object[] { gameDir });
        if (Py.Truthy(crash.Get("is_crash")))
        {
            Log("[崩溃分析] " + Py.Str(Py.Or(crash.Get("summary"), crash.Get("headline"))));
            throw new GameCrashError(crash);
        }
        progress.Log(I18nCore.Tr("游戏已退出"));
    }

    /// <summary>带进度/取消的 DownloadManager（对齐 Windows 版 _dm）。</summary>
    public static DownloadManager MakeDownloader(TaskProgress progress, CancellationToken ct)
    {
        string lastKey = "";
        return new DownloadManager(
            Config.CONFIG.Get("download_threads", 8L),
            (message, done, total) =>
            {
                string text = message ?? "";
                progress.Report(text, done, total);
                if (text.Contains("  |  "))
                {
                    string head = Py.Strip(Py.Split(text, "  |  ", 1)[0], " ·");
                    if (head.Length > 0 && head != lastKey)
                    {
                        lastKey = head;
                        progress.Log(text);
                    }
                }
                else
                {
                    string t2 = Py.Strip(text);
                    if (t2.Length > 0 && t2 != lastKey)
                    {
                        lastKey = t2;
                        progress.Log(t2);
                    }
                }
            },
            () => ct.IsCancellationRequested,
            null, ct);
    }

    /// <summary>解析启动账号（对齐 Windows 版 _require_launch_account）。</summary>
    public static PyDict RequireLaunchAccount(AccountManager accounts, object account, object username)
    {
        if (!IsOfflineAccount(account))
        {
            PyDict a = accounts.GetAccount(account);
            if (a == null)
                throw new LaunchError(Py.Fmt(I18nCore.Tr("账号不存在: {0}"), account));
            return accounts.EnsureValid(a);
        }
        if (accounts.accounts.Count == 0)
            throw new LaunchError(I18nCore.Tr("启动游戏前请先创建一个离线用户（「账号」页 → 添加离线账号）"));
        string name = Py.Strip(Py.Str(Py.Or(username, "")));
        if (name.Length > 0)
        {
            PyDict a = accounts.GetAccount(name);
            if (a == null)
                throw new LaunchError(Py.Fmt(I18nCore.Tr("离线用户「{0}」还没创建，请先到「账号」页添加"), name));
            return accounts.EnsureValid(a);
        }
        return accounts.EnsureValid(accounts.GetActive() ?? ((PyDict)accounts.accounts[0]));
    }

    public static bool IsOfflineAccount(object account)
    {
        string s = Py.S(account);
        if (s.Length != 0 && s != "离线模式") return s == I18nCore.Tr("离线模式");
        return true;
    }

    /// <summary>归一化 Java 偏好：名字 → exe 路径；找不到原样返回（对齐 normalize_java_pref）。</summary>
    public static object NormalizeJavaPref(object java)
    {
        if (!Py.Truthy(java) || Py.Eq(java, "自动选择") || Py.Eq(java, "auto") || Py.Eq(java, "default"))
            return "自动选择";
        foreach (PyDict j in Java.AllJavas())
        {
            if (Py.Eq(j.Get("name"), java) || Py.Eq(j.Get("exe"), java))
                return j.Get("exe") ?? java;
        }
        return java;
    }

    private static List<object> ArgList(object o)
    {
        if (o == null) return null;
        if (o is IEnumerable<object> seq && o is not string)
            return seq.ToList();
        if (Py.Truthy(o)) return new List<object> { o };
        return null;
    }
}

/// <summary>实例解析助手（对齐 Windows 版 BackendApi._instance）。</summary>
public static class InstancesApi
{
    public static Instance EnsureInstance(object name = null)
    {
        name = Py.Or(name, Config.CONFIG.Get("default_instance", "default"));
        var inst = new Instance(Py.S(name));
        if (!LocalFs.IsDir(inst.path))
            inst.Create();
        else
            inst.EnsureStandardDirs();
        return inst;
    }
}
