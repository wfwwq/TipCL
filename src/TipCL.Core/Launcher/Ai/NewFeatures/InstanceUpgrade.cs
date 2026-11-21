// AI-new-features:整合包 / 实例升级(plan_instance_upgrade 只读 + apply_instance_upgrade 写)。
//
// 复用现有能力:
//   · 模组身份识别与「目标版本有没有」= ModUpdate 同一条路(Modrinth version_file(sha1) → project/version 过滤;
//     不认识时走 CurseForge 指纹 Mods.CfPost("/fingerprints") → Mods.CfFiles);
//   · 替代建议 = search_mods 背后的 Mods.SearchModrinthProjects(按目标 MC + 加载器过滤);
//   · 前置变化 = 对比当前版本与目标版本声明的 required 依赖;装完后的冲突复核交给 scan_mod_conflicts。
// 联网部分收在 IUpgradeSource 接口后面:测试塞假源,不联网。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TipCL.Core.Launcher.Ai;

/// <summary>升级所需的联网能力(默认实现走 Modrinth / CurseForge;测试替换成假源)。</summary>
public interface IUpgradeSource
{
    /// <summary>认出一个已装 jar:{source, project_id, version_id, version_number, name, deps:[required 依赖的 project_id]};认不出返回 null。</summary>
    PyDict? Identify(string jarPath);

    /// <summary>该项目在目标 MC + 加载器下最新的版本:{version_id, version_number, filename, url, sha1, size, deps:[...]};没有返回 null。</summary>
    PyDict? FindVersion(PyDict ident, string mcVersion, string loader);

    /// <summary>替代建议(按名字搜、只要支持目标版本的):[{name, slug, source}]。</summary>
    List<object> Alternatives(string name, string mcVersion, string loader, int limit);

    /// <summary>把 FindVersion 给出的文件下载到 destPath。</summary>
    void Download(PyDict version, string destPath);
}

/// <summary>默认联网源(Modrinth 优先,CurseForge 兜底;与 ModUpdate 同一套请求)。</summary>
public sealed class NetUpgradeSource : IUpgradeSource
{
    private readonly DownloadManager _dm;

    public NetUpgradeSource(DownloadManager? dm = null) => _dm = dm ?? new DownloadManager(4L);

    private static List<object> RequiredDeps(object? deps)
        => Py.ListOr(deps).Select(d => Py.D(d))
            .Where(d => Py.S(d.Get("dependency_type")) == "required" && Py.S(d.Get("project_id")).Length > 0)
            .Select(d => (object)Py.S(d.Get("project_id"))).Distinct().ToList();

    public PyDict? Identify(string jarPath)
    {
        string sha1;
        try
        {
            sha1 = Utils.Sha1File(jarPath);
        }
        catch (Exception)
        {
            return null;
        }
        try
        {
            if (_dm.FetchJson(ModUpdate.API + "/version_file/" + sha1, 12L) is PyDict v && Py.S(v.Get("project_id")).Length > 0)
            {
                return PyDict.Of(
                    "source", "modrinth",
                    "project_id", Py.S(v.Get("project_id")),
                    "version_id", Py.S(v.Get("id")),
                    "version_number", Py.S(Py.Or(v.Get("version_number"), v.Get("name"))),
                    "deps", RequiredDeps(v.Get("dependencies")));
            }
        }
        catch (Exception)
        {
        }
        try
        {
            long fp = ModUpdate.CfFingerprint(jarPath);
            object resp = Mods.CfPost(_dm, "/fingerprints", PyDict.Of("fingerprints", new List<object> { fp }), null, 20L);
            List<object> matches = Py.ListOr(Py.D(Py.D(resp).Get("data")).Get("exactMatches"));
            if (matches.Count > 0)
            {
                PyDict m = Py.D(matches[0]);
                PyDict file = Py.D(m.Get("file"));
                string modId = Py.S(Py.Or(m.Get("id"), file.Get("modId")));
                if (modId.Length > 0)
                {
                    return PyDict.Of(
                        "source", "curseforge",
                        "project_id", modId,
                        "version_id", Py.S(file.Get("id")),
                        "version_number", Py.S(Py.Or(file.Get("displayName"), file.Get("fileName"))),
                        "deps", Py.ListOr(file.Get("dependencies")).Select(d => Py.D(d))
                            .Where(d => Py.Int(Py.Or(d.Get("relationType"), 0L)) == 3)
                            .Select(d => (object)Py.S(d.Get("modId"))).ToList());
                }
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    private static long CfLoaderType(string loader) => loader switch
    {
        "forge" => 1,
        "fabric" => 4,
        "quilt" => 5,
        "neoforge" => 6,
        _ => 0,
    };

    public PyDict? FindVersion(PyDict ident, string mcVersion, string loader)
    {
        string project = Py.S(ident.Get("project_id"));
        if (Py.S(ident.Get("source")) == "modrinth")
        {
            var ps = new List<string> { "game_versions=[\"" + mcVersion + "\"]" };
            if (loader.Length > 0)
            {
                // Quilt 能吃 Fabric 模组:目标是 quilt 时两种都算
                ps.Add(loader == "quilt" ? "loaders=[\"quilt\",\"fabric\"]" : "loaders=[\"" + loader + "\"]");
            }
            if (_dm.FetchJson(ModUpdate.API + "/project/" + project + "/version?" + string.Join("&", ps), 15L) is not List<object> { Count: > 0 } list)
            {
                return null;
            }
            PyDict v = Py.D(list[0]);
            PyDict file = Py.D(Py.ListOr(v.Get("files")).FirstOrDefault(f => Py.Truthy(Py.D(f).Get("primary"))) ?? Py.ListOr(v.Get("files")).FirstOrDefault());
            return PyDict.Of(
                "version_id", Py.S(v.Get("id")),
                "version_number", Py.S(Py.Or(v.Get("version_number"), v.Get("name"))),
                "filename", Py.S(file.Get("filename")),
                "url", Py.S(file.Get("url")),
                "sha1", Py.S(Py.D(file.Get("hashes")).Get("sha1")),
                "size", Py.Int(Py.Or(file.Get("size"), 0L)),
                "deps", RequiredDeps(v.Get("dependencies")));
        }
        List<object> files;
        try
        {
            long lt = CfLoaderType(loader);
            files = Mods.CfFiles(_dm, project, null, mcVersion, lt > 0 ? (object)lt : null, 20L);
        }
        catch (Exception)
        {
            return null;
        }
        if (files.Count == 0)
        {
            return null;
        }
        PyDict f0 = Py.D(files[0]);
        string name = Py.S(f0.Get("fileName"));
        string url = Py.S(f0.Get("downloadUrl"));
        if (url.Length == 0)
        {
            List<object> urls = Mods.CfModDownloadUrls(project, f0.Get("id"), name);
            url = urls.Count > 0 ? Py.S(urls[0]) : "";
        }
        return PyDict.Of(
            "version_id", Py.S(f0.Get("id")),
            "version_number", Py.S(Py.Or(f0.Get("displayName"), name)),
            "filename", name,
            "url", url,
            "sha1", "",
            "size", Py.Int(Py.Or(f0.Get("fileLength"), 0L)),
            "deps", Py.ListOr(f0.Get("dependencies")).Select(d => Py.D(d))
                .Where(d => Py.Int(Py.Or(d.Get("relationType"), 0L)) == 3)
                .Select(d => (object)Py.S(d.Get("modId"))).ToList());
    }

    public List<object> Alternatives(string name, string mcVersion, string loader, int limit)
    {
        try
        {
            object? cats = loader.Length > 0 ? new List<object> { loader } : null;
            return Mods.SearchModrinthProjects(_dm, name, "mod", limit, mcVersion, cats)
                .Select(r => (object)PyDict.Of("name", Py.S(Py.D(r).Get("title")), "slug", Py.S(Py.D(r).Get("slug")), "source", "modrinth"))
                .ToList();
        }
        catch (Exception)
        {
            return new List<object>();
        }
    }

    public void Download(PyDict version, string destPath)
    {
        string url = Py.S(version.Get("url"));
        if (url.Length == 0)
        {
            throw new RuntimeError("没有可下载的地址: " + Py.S(version.Get("filename")));
        }
        string sha1 = Py.S(version.Get("sha1"));
        long size = Py.Int(Py.Or(version.Get("size"), 0L));
        _dm.Download(url, destPath, sha1.Length > 0 ? sha1 : null, size > 0 ? size : (long?)null);
    }
}

/// <summary>实例升级:出计划(只读)与执行(写,默认「复制为新实例再升级」)。</summary>
public static class InstanceUpgrade
{
    /// <summary>测试注入点:非空时用它造联网源。</summary>
    public static Func<IUpgradeSource>? SourceFactory;

    /// <summary>单实例最多检查这么多个模组(再多就截断并在结果里说明,避免一次请求几百个接口)。</summary>
    public const int MAX_MODS = 200;

    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, (DateTime At, PyDict Plan)> Cache = new(StringComparer.Ordinal);

    internal static IUpgradeSource Source() => SourceFactory?.Invoke() ?? new NetUpgradeSource();

    /// <summary>加载器名归一:Fabric / fabric / 无 / vanilla → fabric / ""。</summary>
    public static string NormalizeLoader(string? loader)
    {
        string l = Py.Lower(Py.Strip(loader ?? ""));
        return l switch
        {
            "" or "无" or "none" or "vanilla" or "原版" => "",
            "neo" or "neoforged" => "neoforge",
            _ => l,
        };
    }

    /// <summary>给 install_game 用的加载器显示名。</summary>
    public static string LoaderLabel(string loader) => loader switch
    {
        "fabric" => "Fabric",
        "forge" => "Forge",
        "quilt" => "Quilt",
        "neoforge" => "NeoForge",
        _ => "无",
    };

    private static string CacheKey(string inst, string mc, string loader) => inst + "\0" + mc + "\0" + loader;

    /// <summary>最近一次(10 分钟内)对同一组参数出过的计划(Preview 与 apply 复用,不重复联网)。</summary>
    public static PyDict? CachedPlan(string inst, string mc, string loader)
    {
        lock (CacheLock)
        {
            return Cache.TryGetValue(CacheKey(inst, mc, NormalizeLoader(loader)), out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromMinutes(10)
                ? hit.Plan
                : null;
        }
    }

    /// <summary>
    /// 出升级计划(只读 + 联网)。<paramref name="loader"/> 为空 = 沿用实例当前加载器。
    /// 每个模组的 status:upgrade(目标版本有新文件)/ compatible(当前文件就支持目标版本)/ missing(目标版本没有)/ unknown(认不出来源)。
    /// </summary>
    public static PyDict Plan(Instance inst, string targetMc, string? loader = null, IUpgradeSource? source = null)
    {
        targetMc = Py.Strip(targetMc ?? "");
        if (targetMc.Length == 0)
        {
            throw new ValueError("缺少目标 MC 版本(target_version)");
        }
        source ??= Source();
        string fromMc = Mods.DetectMcVersion(inst) ?? "";
        string fromLoader = Mods.DetectLoader(inst) ?? "";
        string toLoader = NormalizeLoader(loader);
        if (toLoader.Length == 0)
        {
            toLoader = fromLoader;
        }
        string modsDir = PPath.Join(inst.path, "mods");
        var entries = Mods.ListInstanceModEntries(inst).Select(e => Py.D(e)).ToList();
        bool truncated = entries.Count > MAX_MODS;
        var rows = new List<PyDict>();
        var installedProjects = new HashSet<string>(StringComparer.Ordinal);
        var identities = new List<(PyDict Entry, PyDict Info, PyDict? Ident)>();
        foreach (PyDict entry in entries.Take(MAX_MODS))
        {
            string file = Py.S(entry.Get("filename"));
            string path = PPath.Join(modsDir, file);
            PyDict info;
            try
            {
                info = Conflict.InspectJar(path);
            }
            catch (Exception)
            {
                info = PyDict.Of("name", new PPath(file).Stem, "id", new PPath(file).Stem);
            }
            PyDict? ident = LocalFs.IsFile(path) ? SafeIdentify(source, path) : null;
            if (ident != null)
            {
                installedProjects.Add(Py.S(ident.Get("project_id")));
            }
            identities.Add((entry, info, ident));
        }

        var depChanges = new List<object>();
        foreach ((PyDict entry, PyDict info, PyDict? ident) in identities)
        {
            string file = Py.S(entry.Get("filename"));
            string name = Py.S(Py.Or(info.Get("name"), new PPath(file).Stem));
            var row = PyDict.Of(
                "filename", file,
                "name", name,
                "mod_id", Py.S(info.Get("id")),
                "enabled", Py.Truthy(entry.Get("enabled")),
                "current", Py.S(Py.Or(ident?.Get("version_number"), info.Get("version"))));
            if (ident == null)
            {
                row["status"] = "unknown";
                row["note"] = "在 Modrinth / CurseForge 上认不出这个文件(可能是自制或改过的 jar),需要手动确认";
                rows.Add(row);
                continue;
            }
            row["source"] = Py.S(ident.Get("source"));
            row["project_id"] = Py.S(ident.Get("project_id"));
            PyDict? target = SafeFind(source, ident, targetMc, toLoader);
            if (target == null)
            {
                row["status"] = "missing";
                row["alternatives"] = SafeAlternatives(source, name, targetMc, toLoader, Py.S(ident.Get("project_id")));
                rows.Add(row);
                continue;
            }
            bool same = Py.S(target.Get("version_id")) == Py.S(ident.Get("version_id"));
            row["status"] = same ? "compatible" : "upgrade";
            row["target_version"] = Py.S(target.Get("version_number"));
            row["target_file"] = Py.S(target.Get("filename"));
            row["target"] = target;
            var oldDeps = Py.ListOr(ident.Get("deps")).Select(x => Py.S(x)).ToHashSet(StringComparer.Ordinal);
            var newDeps = Py.ListOr(target.Get("deps")).Select(x => Py.S(x)).ToHashSet(StringComparer.Ordinal);
            var added = newDeps.Except(oldDeps).ToList();
            var removed = oldDeps.Except(newDeps).ToList();
            if (added.Count > 0 || removed.Count > 0)
            {
                var notInstalled = added.Where(d => !installedProjects.Contains(d)).ToList();
                row["deps_added"] = added.Cast<object>().ToList();
                row["deps_removed"] = removed.Cast<object>().ToList();
                depChanges.Add(PyDict.Of(
                    "mod", name,
                    "added", added.Cast<object>().ToList(),
                    "removed", removed.Cast<object>().ToList(),
                    "need_install", notInstalled.Cast<object>().ToList()));
            }
            rows.Add(row);
        }

        long Count(string status) => rows.LongCount(r => Py.S(r.Get("status")) == status);
        var plan = PyDict.Of(
            "instance", inst.name,
            "from_mc", fromMc,
            "to_mc", targetMc,
            "from_loader", fromLoader,
            "to_loader", toLoader,
            "summary", PyDict.Of(
                "total", (long)rows.Count,
                "upgrade", Count("upgrade"),
                "compatible", Count("compatible"),
                "missing", Count("missing"),
                "unknown", Count("unknown")),
            "mods", rows.Cast<object>().ToList(),
            "missing", rows.Where(r => Py.S(r.Get("status")) == "missing").Select(r => (object)Py.S(r.Get("name"))).ToList(),
            "dependency_changes", depChanges,
            "truncated", truncated,
            "loader_note", toLoader != fromLoader
                ? "加载器要从 " + (fromLoader.Length > 0 ? fromLoader : "原版") + " 换成 " + (toLoader.Length > 0 ? toLoader : "原版") + ",大部分模组需要换成对应版本"
                : "",
            "advice", "升级默认复制成新实例再改(apply_instance_upgrade),原实例与存档不动;缺失的模组会被跳过,先看替代建议再决定。");
        lock (CacheLock)
        {
            Cache[CacheKey(inst.name, targetMc, toLoader)] = (DateTime.UtcNow, plan);
        }
        return plan;
    }

    private static PyDict? SafeIdentify(IUpgradeSource src, string path)
    {
        try
        {
            return src.Identify(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static PyDict? SafeFind(IUpgradeSource src, PyDict ident, string mc, string loader)
    {
        try
        {
            return src.FindVersion(ident, mc, loader);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<object> SafeAlternatives(IUpgradeSource src, string name, string mc, string loader, string selfProject)
    {
        try
        {
            return src.Alternatives(name, mc, loader, 4).Where(a => Py.S(Py.D(a).Get("slug")) != selfProject).Take(3).ToList();
        }
        catch (Exception)
        {
            return new List<object>();
        }
    }

    /// <summary>复制模式下新实例的名字(检查点、预览、执行三处同一个算法:与 create_instance 一样走 UniqueInstanceName)。</summary>
    public static string NewInstanceName(PyDict args, string currentInstance)
    {
        string cached = Py.S(args.Get("_new_name"));
        if (cached.Length > 0)
        {
            return cached;
        }
        string inst = Py.S(Py.Or(args.Get("instance"), currentInstance));
        string wanted = Py.S(args.Get("new_name"));
        if (wanted.Length == 0)
        {
            wanted = (inst.Length > 0 ? inst : "游戏") + "-" + Py.S(args.Get("target_version"));
        }
        string unique = Instances.UniqueInstanceName(wanted);
        args["_new_name"] = unique;
        return unique;
    }

    /// <summary>复制模式要带到新实例的东西(不含 mods / versions / libraries / logs)。</summary>
    public static readonly string[] CARRY_OVER = { "config", "saves", "resourcepacks", "shaderpacks", "options.txt", "optionsof.txt", "optionsshaders.txt", "servers.dat", "servers.json" };

    /// <summary>
    /// 执行升级(已过确认)。<paramref name="installGame"/> 由执行器提供(走 IApi 的 install_game);
    /// <paramref name="progress"/> 每完成一步回调一次(驱动 update_plan 计划卡)。
    /// </summary>
    public static PyDict Apply(Instance inst, PyDict args, Func<string, string, string, object?> installGame, Action<int>? progress = null, IUpgradeSource? source = null)
    {
        string targetMc = Py.Strip(Py.S(args.Get("target_version")));
        string loaderArg = Py.S(args.Get("loader"));
        bool inPlace = Py.Truthy(args.Get("in_place"));
        source ??= Source();
        var skip = Py.ListOr(args.Get("skip")).Select(x => Py.S(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 1. 计划(10 分钟内出过就复用)
        PyDict plan = CachedPlan(inst.name, targetMc, loaderArg.Length > 0 ? loaderArg : (Mods.DetectLoader(inst) ?? ""))
                      ?? Plan(inst, targetMc, loaderArg, source);
        string toLoader = Py.S(plan.Get("to_loader"));
        progress?.Invoke(0);

        // 2. 目标实例
        Instance target;
        var carried = new List<object>();
        if (inPlace)
        {
            target = inst;
        }
        else
        {
            string newName = NewInstanceName(args, inst.name);
            target = Instances.CreateUniqueInstance(newName, "游戏", PyDict.Of("mc_version", targetMc, "upgraded_from", inst.name));
            foreach (string item in CARRY_OVER)
            {
                string src = PPath.Join(inst.path, item);
                string dst = PPath.Join(target.path, item);
                try
                {
                    if (LocalFs.IsDir(src))
                    {
                        LocalFs.CopyTree(src, dst, dirsExistOk: true);
                        carried.Add(item + "/");
                    }
                    else if (LocalFs.IsFile(src))
                    {
                        LocalFs.Copy2(src, dst);
                        carried.Add(item);
                    }
                }
                catch (Exception)
                {
                    // 单项复制失败不中断(例如存档被占用),最后在结果里能看出少了哪项
                }
            }
        }
        progress?.Invoke(1);

        // 3. 装目标版本 + 加载器
        object? install = null;
        string installError = "";
        try
        {
            install = installGame(targetMc, LoaderLabel(toLoader), target.name);
        }
        catch (Exception ex)
        {
            installError = Py.ExcStr(ex);
        }
        progress?.Invoke(2);

        // 4. 模组:升级的下新文件,兼容的原样带过去,缺失 / 认不出的跳过(原地模式下禁用)
        string oldMods = PPath.Join(inst.path, "mods");
        string newMods = PPath.Join(target.path, "mods");
        Utils.EnsureDir(newMods);
        var installed = new List<object>();
        var kept = new List<object>();
        var skipped = new List<object>();
        var failed = new List<object>();
        foreach (object raw in Py.ListOr(plan.Get("mods")))
        {
            PyDict row = Py.D(raw);
            string file = Py.S(row.Get("filename"));
            string status = Py.S(row.Get("status"));
            if (skip.Contains(file))
            {
                skipped.Add(PyDict.Of("filename", file, "reason", "用户要求跳过"));
                continue;
            }
            try
            {
                switch (status)
                {
                    case "upgrade":
                    {
                        PyDict ver = Py.D(row.Get("target"));
                        string newFile = Py.S(Py.Or(ver.Get("filename"), file));
                        string dest = PPath.Join(newMods, newFile);
                        source.Download(ver, dest);
                        if (!LocalFs.IsFile(dest) || LocalFs.Size(dest) <= 0)
                        {
                            throw new RuntimeError("下载后文件不存在");
                        }
                        if (inPlace && !string.Equals(newFile, file, StringComparison.OrdinalIgnoreCase) && LocalFs.IsFile(PPath.Join(oldMods, file)))
                        {
                            File.Delete(PPath.Abs(PPath.Join(oldMods, file)));
                        }
                        installed.Add(PyDict.Of("from", file, "to", newFile, "version", Py.S(ver.Get("version_number"))));
                        break;
                    }
                    case "compatible":
                        if (!inPlace)
                        {
                            LocalFs.Copy2(PPath.Join(oldMods, file), PPath.Join(newMods, file));
                        }
                        kept.Add(file);
                        break;
                    default:
                        if (inPlace && Py.Truthy(row.Get("enabled")))
                        {
                            Mods.SetModEnabled(inst, file, false);
                            skipped.Add(PyDict.Of("filename", file, "reason", (status == "missing" ? "目标版本没有" : "认不出来源") + ",已禁用(可 enable_mod 恢复)"));
                        }
                        else
                        {
                            skipped.Add(PyDict.Of("filename", file, "reason", status == "missing" ? "目标版本没有,未带到新实例" : "认不出来源,未带到新实例"));
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                failed.Add(PyDict.Of("filename", file, "error", Py.ExcStr(ex)));
            }
        }
        progress?.Invoke(3);

        bool ok = installError.Length == 0 && failed.Count == 0;
        return PyDict.Of(
            "ok", ok,
            "mode", inPlace ? "in_place" : "copy",
            "source_instance", inst.name,
            "target_instance", target.name,
            "target_version", targetMc,
            "loader", toLoader,
            "carried_over", carried,
            "install_game", installError.Length > 0 ? PyDict.Of("ok", false, "error", installError) : Py.Or(install, PyDict.Of("ok", true)),
            "mods_upgraded", installed,
            "mods_kept", kept,
            "mods_skipped", skipped,
            "mods_failed", failed,
            "dependency_changes", Py.Or(plan.Get("dependency_changes"), new List<object>()),
            "next_steps", inPlace
                ? "已在原实例上升级;如需回退可用对话里的「撤回」(只覆盖 mods 目录)。建议接着 preflight_check 再启动。"
                : "原实例「" + inst.name + "」没动。新实例「" + target.name + "」装好后先 preflight_check,再按 dependency_changes 补装前置。");
    }
}
