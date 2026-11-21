// AI2-moddev-mcp:create_test_instance / install_local_mod / revert_local_mod 的执行体。 // i18n:ignore-file(这里的中文全是回给模型 / 外部 agent 的工具结果与错误原因;界面文案在 ModDevText.cs 走 Tr)
//
// 定位(用户已明确):内置助手不写、不编译 Java 模组;外部编程工具(Claude Code、Cursor…)写代码并编译,
// 经本机 MCP Server 让 TipCL 当测试环境 —— 建测试实例、装本地 jar、起一次游戏看能不能进主菜单。
//
// 测试实例 = .instance.json 里 dev=true(名字带「[dev] 」前缀),另记 dev_loader / dev_version / dev_dependencies。
// install_local_mod 的准入:只收 .jar、必须是合法 zip、必须有模组元数据;加载器与实例不匹配就拒;
// 目标不是测试实例时风险更高:必须显式 allow_non_dev=true,执行器还会再强制弹一次确认(见 ToolRunner.ModDevMcp.cs)。
// 同 id 的旧 jar 先挪到 实例/.tipcl_dev/replaced/<change_id>/ 再放新 jar,整个改动先做 Checkpoint 快照
// (会话 id = moddev,轮次 id = change_id),revert_local_mod 按 change_id 一键撤回。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>模组开发测试环境的三个写工具的执行体(纯 Core,不碰界面)。</summary>
public static class ModDevOps
{
    /// <summary>install_local_mod 的检查点会话 id(revert_local_mod 按它 + change_id 回滚)。</summary>
    public const string CheckpointChat = "moddev";

    /// <summary>测试实例名前缀。</summary>
    public const string DevPrefix = "[dev] ";

    /// <summary>被替换下来的旧 jar 放在实例下的这个目录(不在 mods 里,加载器不会扫到)。</summary>
    public const string ReplacedDir = ".tipcl_dev/replaced";

    private static readonly Regex ChangeIdRx = new("^lm-[0-9]{14}-[0-9a-f]{6}$", RegexOptions.CultureInvariant);

    // ───────────────────────────── 实例 ─────────────────────────────

    /// <summary>按名字找一个**已存在**的实例(找不到抛 FileNotFoundError,不回落到别的实例)。</summary>
    public static Instance ResolveInstance(string name)
    {
        string want = (name ?? "").Trim();
        var inst = new Instance(want.Length > 0 ? want : null);
        bool renamed = want.Length > 0 && inst.name != want && want != Instances.RootName();
        if (renamed || !LocalFs.IsDir(inst.path))
        {
            throw new FileNotFoundError("实例不存在: " + (want.Length > 0 ? want : inst.name));
        }
        return inst;
    }

    public static bool IsDev(Instance inst)
    {
        try
        {
            return Py.Truthy(inst.Meta().Get("dev"));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>实例的加载器:测试实例记在 meta 里;其余按已装版本名推断(Mods.DetectLoader)。</summary>
    public static string InstanceLoader(Instance inst)
    {
        string dev = ModJarInfo.NormLoader(Py.S(inst.Meta().Get("dev_loader")));
        if (dev.Length > 0)
        {
            return dev;
        }
        return ModJarInfo.NormLoader(Mods.DetectLoader(inst) ?? "");
    }

    public static string InstanceMc(Instance inst)
    {
        try
        {
            return Py.S(Mods.DetectMcVersion(inst));
        }
        catch (Exception)
        {
            return Py.S(inst.Meta().Get("mc_version"));
        }
    }

    // ───────────────────────────── create_test_instance ─────────────────────────────

    /// <summary>参数 → 最终实例名(带 [dev] 前缀,还没去重)。</summary>
    public static string DevName(PyDict args)
    {
        string mc = Py.S(args.Get("mc_version")).Trim();
        string loader = ModJarInfo.NormLoader(Py.S(args.Get("loader")));
        string name = Py.S(args.Get("name")).Trim();
        if (name.Length == 0)
        {
            name = mc + "-" + (loader.Length > 0 ? loader : "vanilla");
        }
        return name.StartsWith("[dev]", StringComparison.OrdinalIgnoreCase) ? name : DevPrefix + name;
    }

    public static List<string> Dependencies(PyDict args)
    {
        object? raw = args.Get("dependencies");
        IEnumerable<string> items = raw is List<object> list
            ? list.Select(Py.S)
            : Py.S(raw).Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        return items.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
    }

    /// <summary>
    /// 建测试实例(同步)+ 可选地起一个后台任务装 MC 本体 / 加载器 / 依赖模组。
    /// 返回 {ok, instance, path, dev, task_id?, ...}。
    /// </summary>
    public static PyDict CreateTestInstance(PyDict args)
    {
        string mc = Py.S(args.Get("mc_version")).Trim();
        string loaderRaw = Py.S(args.Get("loader")).Trim();
        string loaderVersion = Py.S(args.Get("loader_version")).Trim();
        if (mc.Length == 0)
        {
            throw new ValueError("缺少 mc_version(如 1.20.1)");
        }
        if (mc.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0 || mc.Length > 40)
        {
            throw new ValueError("mc_version 不像版本号: " + mc);
        }
        string loader = ModJarInfo.NormLoader(loaderRaw.Length > 0 ? loaderRaw : "vanilla");
        if (loader.Length == 0)
        {
            throw new ValueError("不认识的加载器: " + loaderRaw + "(可选 fabric / forge / neoforge / quilt / vanilla)");
        }
        List<string> deps = Dependencies(args);
        if (loader == "vanilla" && deps.Count > 0)
        {
            throw new ValueError("原版(vanilla)实例装不了依赖模组;请指定 loader");
        }
        bool install = args.Get("install") is not false;
        var meta = PyDict.Of(
            "dev", true,
            "mc_version", mc,
            "dev_loader", loader,
            "dev_loader_version", loaderVersion,
            "dev_dependencies", deps.Cast<object>().ToList(),
            "dev_created_by", "create_test_instance",
            "dev_created_at", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
        Instance inst = Instances.CreateUniqueInstance(DevName(args), "dev", meta);
        var result = PyDict.Of(
            "ok", true,
            "instance", inst.name,
            "path", inst.path,
            "dev", true,
            "mc_version", mc,
            "loader", loader,
            "dependencies", deps.Cast<object>().ToList());
        if (!install)
        {
            result["message"] = "测试实例已创建(没装游戏本体;install=false)。之后可用 install_game 装版本,再 install_local_mod 装你的 jar";
            return result;
        }
        ModDevTask task = ModDevTasks.Start("create_test_instance", "准备测试实例 " + inst.name,
            (t, ct) => Task.Run(() => PrepareInstance(inst, mc, loader, loaderVersion, deps, t, ct), ct));
        result["task_id"] = task.Id;
        result["message"] = "测试实例已创建,正在后台安装 Minecraft " + mc + (loader != "vanilla" ? " + " + loader : "")
            + (deps.Count > 0 ? " 与依赖 " + string.Join(", ", deps) : "")
            + "。用 get_task_status(task_id, wait_seconds=30) 查进度;装好后 install_local_mod 装你的 jar,再 run_test_launch";
        return result;
    }

    private static PyDict PrepareInstance(Instance inst, string mc, string loader, string loaderVersion, List<string> deps, ModDevTask t, CancellationToken ct)
    {
        int steps = 1 + deps.Count;
        t.SetStep("安装 Minecraft " + mc + (loader != "vanilla" ? " + " + loader : ""), 5);
        string versionId = ModDevHost.InstallGame(inst, mc, loader, loaderVersion, line => t.Log(line), ct);
        ct.ThrowIfCancellationRequested();
        if (versionId.Length > 0)
        {
            inst.SetMeta("dev_version", versionId);
        }
        var depRows = new List<object>();
        var failed = new List<string>();
        for (int i = 0; i < deps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            string slug = deps[i];
            t.SetStep("安装依赖 " + slug, 5 + (int)(90.0 * (i + 1) / steps));
            try
            {
                PyDict dep = ModDevHost.InstallDependency(inst, slug, mc, loader, line => t.Log(line), ct);
                depRows.Add(PyDict.Of("slug", slug, "ok", true, "file", Py.S(dep.Get("file")), "source", Py.S(dep.Get("source")),
                    "tried", Py.ListOr(dep.Get("tried"))));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (DependencyInstallException ex)
            {
                failed.Add(slug);
                t.Log("依赖 " + slug + " 安装失败: " + ex.Message);
                depRows.Add(PyDict.Of("slug", slug, "ok", false, "error", ex.Message, "tried", ex.Tried));
            }
            catch (Exception ex)
            {
                failed.Add(slug);
                t.Log("依赖 " + slug + " 安装失败: " + ex.Message);
                depRows.Add(PyDict.Of("slug", slug, "ok", false, "error", ex.Message));
            }
        }
        bool ok = failed.Count == 0;
        return PyDict.Of(
            "ok", ok,
            "instance", inst.name,
            "version_id", versionId,
            "loader", loader,
            "dependencies", depRows,
            "message", ok
                ? "测试实例已就绪:" + inst.name + " / " + versionId
                : "游戏本体已装好,但这些依赖没装上: " + string.Join(", ", failed) + "(每个依赖试过哪些来源见 dependencies[].tried,默认先 Modrinth 后 CurseForge;可以把依赖 jar 下载到本地后用 install_local_mod 装)");
    }

    /// <summary>create_test_instance 将要新建的实例目录(检查点 / 预览用)。</summary>
    public static string PlannedInstancePath(PyDict args)
    {
        try
        {
            return Instances.GetInstancePath(Instances.UniqueInstanceName(DevName(args), "dev"));
        }
        catch (Exception)
        {
            return "";
        }
    }

    // ───────────────────────────── install_local_mod ─────────────────────────────

    /// <summary>install_local_mod 的执行计划(执行、预览、检查点路径三处共用同一份)。</summary>
    public sealed class InstallPlan
    {
        public string Source = "";

        public Instance? Inst;

        public bool Dev;

        public ModJarInfo? Info;

        public string Loader = "";

        public string Mc = "";

        public string ModsDir = "";

        public string Target = "";

        /// <summary>要被挪走的同 id 旧 jar(完整路径)。</summary>
        public List<string> Replaced = new();

        public List<string> Warnings = new();

        /// <summary>拒绝原因(错误码, 说明);空 = 可以执行。</summary>
        public string ErrorCode = "";

        public string ErrorMessage = "";

        public bool SourceInModsDir;

        public bool Ok => ErrorCode.Length == 0;
    }

    private static InstallPlan Fail(InstallPlan p, string code, string message)
    {
        p.ErrorCode = code;
        p.ErrorMessage = message;
        return p;
    }

    /// <summary>算计划:参数校验 → 读 jar → 定实例 → 加载器 / 版本 → 找同 id 旧 jar。不改盘。</summary>
    public static InstallPlan Plan(PyDict args, string defaultInstance)
    {
        var p = new InstallPlan();
        string raw = Py.S(args.Get("jar_path")).Trim().Trim('"', '\'');
        p.Source = raw;
        if (raw.Length == 0)
        {
            return Fail(p, "bad_arguments", "缺少 jar_path(本地 jar 的绝对路径)");
        }
        if (!Path.IsPathRooted(raw))
        {
            return Fail(p, "bad_arguments", "jar_path 必须是绝对路径(外部工具的工作目录和启动器不同): " + raw);
        }
        if (!raw.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
        {
            return Fail(p, "bad_arguments", "只接受 .jar 文件: " + Path.GetFileName(raw));
        }
        string full;
        try
        {
            full = Path.GetFullPath(raw);
        }
        catch (Exception ex)
        {
            return Fail(p, "bad_arguments", "jar_path 不是合法路径: " + ex.Message);
        }
        p.Source = full;
        if (!File.Exists(full))
        {
            return Fail(p, "not_found", "jar 不存在: " + full);
        }
        ModJarInfo info = ModJarInfo.Read(full);
        p.Info = info;
        if (!info.ValidZip)
        {
            return Fail(p, "invalid_jar", info.Error);
        }
        if (!info.IsMod)
        {
            return Fail(p, "not_a_mod", info.Error.Length > 0 ? info.Error : "jar 里没有可识别的模组元数据");
        }
        string instName = Py.S(Py.Or(args.Get("instance"), defaultInstance));
        Instance inst;
        try
        {
            inst = ResolveInstance(instName);
        }
        catch (FileNotFoundError ex)
        {
            return Fail(p, "not_found", ex.Message);
        }
        p.Inst = inst;
        p.Dev = IsDev(inst);
        p.Loader = InstanceLoader(inst);
        p.Mc = InstanceMc(inst);
        if (!info.CompatibleWith(p.Loader, p.Mc, out string why))
        {
            return Fail(p, "loader_mismatch", why);
        }
        if (info.McVersion.Length > 0 && p.Mc.Length > 0 && ModJarInfo.McMatches(info.McVersion, p.Mc) == false)
        {
            p.Warnings.Add("jar 声明适用 MC " + info.McVersion + ",实例是 " + p.Mc + ",进游戏可能直接报版本不符");
        }
        foreach (PyDict dep in info.Depends)
        {
            p.Warnings.Add("依赖 " + Py.S(dep.Get("id")) + " " + Py.S(dep.Get("version")));
        }
        if (p.Warnings.Count > 0 && info.Depends.Count > 0)
        {
            p.Warnings.Add("(以上依赖不在实例里时游戏会拒绝启动;可在建实例时用 dependencies 装,或 install_local_mod 装本地 jar)");
        }
        p.ModsDir = ModsDirOf(inst);
        string fileName = Path.GetFileName(full);
        p.Target = Path.Combine(PPath.Abs(p.ModsDir), fileName);
        p.SourceInModsDir = string.Equals(Path.GetFullPath(p.Target), full, StringComparison.OrdinalIgnoreCase);
        string absMods = PPath.Abs(p.ModsDir);
        if (Directory.Exists(absMods))
        {
            foreach (string f in Directory.EnumerateFiles(absMods))
            {
                string lower = f.ToLowerInvariant();
                if (!lower.EndsWith(".jar") && !lower.EndsWith(".jar.disabled"))
                {
                    continue;
                }
                if (string.Equals(Path.GetFullPath(f), full, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                ModJarInfo other = ModJarInfo.Read(f);
                bool sameId = other.Id.Length > 0 && string.Equals(other.Id, info.Id, StringComparison.OrdinalIgnoreCase);
                bool sameName = string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase);
                if (sameId)
                {
                    p.Replaced.Add(f);
                }
                else if (sameName)
                {
                    return Fail(p, "file_exists", "mods 里已有同名文件 " + fileName + ",但它是另一个模组(" + (other.Id.Length > 0 ? other.Id : "认不出") + "),不覆盖;请改名后再装");
                }
            }
        }
        if (!p.Dev && !Py.Truthy(args.Get("allow_non_dev")))
        {
            return Fail(p, "needs_confirmation",
                "目标实例「" + inst.name + "」不是测试实例(dev)。往日常实例装本地 jar 风险更高:可以先 create_test_instance 建测试实例;"
                + "确定要装进这个实例时,带 allow_non_dev=true 重新调用(会再次请用户确认)");
        }
        return p;
    }

    /// <summary>
    /// 模组目录:版本开了 mods 隔离时在该版本的游戏目录下(与启动链同一口径 VersionSettings.ModsDir)。
    /// 版本取测试实例记下的 dev_version,否则实例里唯一的那个;都没有就是实例共享的 mods。
    /// </summary>
    public static string ModsDirOf(Instance inst)
    {
        try
        {
            string ver = Py.S(inst.Meta().Get("dev_version"));
            if (ver.Length == 0 || !inst.HasVersion(ver))
            {
                List<string> ids = inst.InstalledIds().Select(Py.S).ToList();
                ver = ids.Count == 1 ? ids[0] : "";
            }
            if (ver.Length > 0)
            {
                return VersionSettings.ModsDir(inst, ver);
            }
        }
        catch (Exception)
        {
        }
        return PPath.Join(inst.path, "mods");
    }

    /// <summary>这一步会动到的路径(检查点):目标 jar、被替换的旧 jar、备份目录。</summary>
    public static List<string> AffectedPaths(InstallPlan p, string? backupDir)
    {
        var paths = new List<string>();
        if (!p.Ok || p.Inst == null)
        {
            return paths;
        }
        if (!p.SourceInModsDir)
        {
            paths.Add(p.Target);
        }
        paths.AddRange(p.Replaced);
        paths.Add(backupDir ?? Path.Combine(PPath.Abs(p.Inst.path), ReplacedDir.Replace('/', Path.DirectorySeparatorChar)));
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static PyDict InstallLocalMod(PyDict args, string defaultInstance)
    {
        InstallPlan p = Plan(args, defaultInstance);
        if (!p.Ok)
        {
            var err = PyDict.Of("ok", false, "error", p.ErrorCode, "message", p.ErrorMessage, "jar_path", p.Source);
            if (p.Info != null && p.Info.ValidZip)
            {
                err["mod"] = p.Info.ToDict();
            }
            if (p.Inst != null)
            {
                err["instance"] = p.Inst.name;
                err["instance_loader"] = p.Loader;
                err["instance_mc"] = p.Mc;
                err["dev"] = p.Dev;
            }
            return err;
        }
        Instance inst = p.Inst!;
        ModJarInfo info = p.Info!;
        string changeId = "lm-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        string backupDir = Path.Combine(PPath.Abs(inst.path), ReplacedDir.Replace('/', Path.DirectorySeparatorChar), changeId);
        PyDict snap = Checkpoint.Snapshot(CheckpointChat, changeId, AffectedPaths(p, backupDir));
        if (!Py.Truthy(snap.Get("ok")))
        {
            return PyDict.Of("ok", false, "error", "checkpoint_failed", "message", "做不了检查点,为了能撤回,这次不装: " + Py.S(snap.Get("reason")));
        }
        Directory.CreateDirectory(PPath.Abs(p.ModsDir));
        var replaced = new List<object>();
        if (p.Replaced.Count > 0)
        {
            Directory.CreateDirectory(backupDir);
        }
        foreach (string old in p.Replaced)
        {
            string dest = Path.Combine(backupDir, Path.GetFileName(old));
            ModJarInfo oldInfo = ModJarInfo.Read(old);
            File.Move(old, dest);
            replaced.Add(PyDict.Of("file", Path.GetFileName(old), "version", oldInfo.Version, "moved_to", dest));
        }
        if (!p.SourceInModsDir)
        {
            File.Copy(p.Source, p.Target, overwrite: false);
        }
        var result = PyDict.Of(
            "ok", true,
            "instance", inst.name,
            "dev", p.Dev,
            "change_id", changeId,
            "installed", PyDict.Of("file", Path.GetFileName(p.Target), "path", p.Target, "id", info.Id, "version", info.Version),
            "mod", info.ToDict(),
            "instance_loader", p.Loader,
            "instance_mc", p.Mc,
            "replaced", replaced,
            "warnings", p.Warnings.Cast<object>().ToList(),
            "message", (replaced.Count > 0 ? "已替换旧版本并装好 " : "已装好 ") + info.Id + " " + info.Version
                + "。要撤回就调用 revert_local_mod(change_id=" + changeId + ");要验证就 run_test_launch");
        return result;
    }

    // ───────────────────────────── revert_local_mod ─────────────────────────────

    /// <summary>
    /// Tools.AffectedPaths 的本包部分(AI 页会话在写操作之前按它做检查点,配合「撤回」按钮):
    /// create_test_instance = 将要新建的实例目录;install_local_mod = 目标 jar + 同 id 旧 jar + 备份目录;
    /// revert_local_mod / run_test_launch / get_task_status 不做(前者本身就是回滚,后两者不改文件)。
    /// </summary>
    public static List<string> AffectedPathsFor(string name, PyDict? args, string instance)
    {
        args ??= new PyDict();
        try
        {
            switch (name)
            {
                case "create_test_instance":
                {
                    string path = PlannedInstancePath(args);
                    return path.Length > 0 ? new List<string> { path } : new List<string>();
                }
                case "install_local_mod":
                    return AffectedPaths(Plan(args, instance), null);
                default:
                    return new List<string>();
            }
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }

    // ───────────────────────────── revert_local_mod ─────────────────────────────

    public static PyDict RevertLocalMod(PyDict args)
    {
        string id = Py.S(args.Get("change_id")).Trim();
        if (!ChangeIdRx.IsMatch(id))
        {
            throw new ValueError("change_id 不对(应形如 lm-20261121120000-ab12cd,来自 install_local_mod 的返回)");
        }
        if (!Checkpoint.TurnHasOps(CheckpointChat, id))
        {
            return PyDict.Of("ok", false, "error", "not_found", "change_id", id, "message", "没有这次改动的检查点(可能已经撤回过,或检查点已被清理)");
        }
        PyDict rolled = Checkpoint.Rollback(CheckpointChat, id);
        bool ok = Py.Truthy(rolled.Get("ok"));
        return PyDict.Of(
            "ok", ok,
            "change_id", id,
            "restored_files", Py.Or(rolled.Get("restored_files"), 0L),
            "failures", Py.Or(rolled.Get("failures"), 0L),
            "message", ok ? "已撤回:新装的 jar 已移除,被替换的旧 jar 已放回 mods" : "部分文件没能还原(可能被游戏占用),关掉游戏后再试一次");
    }
}
