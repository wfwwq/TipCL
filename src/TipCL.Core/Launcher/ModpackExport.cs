// AI2-modpack:整合包导出引擎(界面「导出为整合包」与 AI 工具 export_modpack 共用这一份,不另写导出逻辑)。
//
// · ExportPack.ExportMrpack(界面入口,Api.cs 的 export_modpack 任务)= Export(inst, dest, ModpackExportOptions.Legacy())
//   行为与原实现一致:逐个按 sha1 查 Modrinth,找到就写 files,找不到就把 jar 打进 overrides;覆盖用户在保存对话框里选的文件。
// · AI 工具 export_modpack = Export(..., 带分发许可检查的选项):先跑 ModpackDistribution.Check,遇到「禁止分发」拒绝导出,
//   来源不明的 jar 默认不打包(allow_bundled_jars=true 才打),不覆盖已有文件。
// · BuildPlan 是「将要写入什么」的唯一来源:确认卡预览(文件清单 + 大小估计)与真正写包用的是同一份清单。
//
// 格式依据:
//   mrpack —— Modrinth 整合包规范(modrinth.index.json:formatVersion=1 / game=minecraft / versionId / name / summary /
//             files[{path, hashes{sha1, sha512}, downloads[], fileSize}] / dependencies{minecraft, forge|neoforge|fabric-loader|quilt-loader})
//             + overrides/ 目录;
//   curseforge —— CurseForge 导出的 manifest.json(minecraft{version, modLoaders[{id, primary}]} / manifestType=minecraftModpack /
//             manifestVersion=1 / name / version / author / files[{projectID, fileID, required}] / overrides="overrides")+ modlist.html。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace TipCL.Core.Launcher;

/// <summary>导出选项。</summary>
public sealed class ModpackExportOptions
{
    /// <summary>mrpack / curseforge。</summary>
    public string Format = "mrpack";

    /// <summary>包名 / 版本 / 简介 / 作者;空 = 用实例元数据或默认值。</summary>
    public string Name = "";
    public string Version = "";
    public string Summary = "";
    public string Author = "";

    /// <summary>图标(png 路径,可空)。规范里没有图标字段,写成包根目录的 icon.png。</summary>
    public string IconPath = "";

    /// <summary>要打进 overrides 的目录(config / resourcepacks / shaderpacks / datapacks 的子集)。</summary>
    public List<string> Overrides = new(ExportPack.OVERRIDE_DIRS);

    /// <summary>先跑分发许可检查(AI 工具恒为 true;curseforge 格式也恒为 true)。</summary>
    public bool CheckDistribution = true;

    /// <summary>来源不明 / 许可未知的 jar 是否打进包(需要用户明确同意)。</summary>
    public bool AllowBundledJars;

    /// <summary>不放进包里的模组文件名(用来排除禁止分发 / 不想带的模组)。</summary>
    public HashSet<string> Exclude = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>目标文件已存在时是否覆盖(界面保存对话框已问过用户 = true;AI 工具 = false)。</summary>
    public bool Overwrite;

    /// <summary>联网源(空 = ModpackSources.Get)。</summary>
    public IModpackSource? Source;

    /// <summary>界面入口的原行为:不做许可检查,找不到 Modrinth 来源的 jar 一律打进 overrides,覆盖已选文件。</summary>
    public static ModpackExportOptions Legacy() => new() { CheckDistribution = false, AllowBundledJars = true, Overwrite = true };
}

/// <summary>整合包导出(mrpack / CurseForge)。</summary>
public static class ModpackExport
{
    private static string Tr(string key) => I18nCore.Tr(key);

    /// <summary>格式 → 扩展名。</summary>
    public static string Extension(string format) => ModpackDistribution.NormalizeFormat(format) == "curseforge" ? ".zip" : ".mrpack";

    // ───────────────────────────── 实例信息 ─────────────────────────────

    private static readonly Regex MetaLoaderRx = new(@"^(fabric-loader|quilt-loader|neoforge|forge|fabric|quilt)-(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// 认出实例的 MC 版本、加载器与加载器版本。先看已装的版本 id(fabric-loader-0.15.11-1.20.1 / 1.20.1-forge-47.2.0 /
    /// neoforge-21.1.77 …,即实际会启动的那一份),再看实例元数据(装整合包时记下的 modpack.loader),最后 MC 版本回落 Mods.DetectMcVersion。
    /// loader 为 fabric / quilt / forge / neoforge / ""。
    /// </summary>
    public static (string Mc, string Loader, string LoaderVersion) DetectLoader(Instance inst)
    {
        string mc = "";
        string loader = "";
        string lv = "";
        foreach (object raw in inst.InstalledIds())
        {
            string id = Py.S(raw);
            Match m;
            if ((m = Regex.Match(id, @"^fabric-loader-([^-]+)-(.+)$", RegexOptions.IgnoreCase)).Success)
            {
                (loader, lv, mc) = ("fabric", m.Groups[1].Value, m.Groups[2].Value);
            }
            else if ((m = Regex.Match(id, @"^quilt-loader-([^-]+)-(.+)$", RegexOptions.IgnoreCase)).Success)
            {
                (loader, lv, mc) = ("quilt", m.Groups[1].Value, m.Groups[2].Value);
            }
            else if ((m = Regex.Match(id, @"neoforge-?(\d[\w.+\-]*)$", RegexOptions.IgnoreCase)).Success)
            {
                (loader, lv) = ("neoforge", m.Groups[1].Value);
            }
            else if ((m = Regex.Match(id, @"^(\d[\w.]*)-forge-?(.+)$", RegexOptions.IgnoreCase)).Success)
            {
                string ver = m.Groups[2].Value;
                if (ver.StartsWith(m.Groups[1].Value + "-", StringComparison.Ordinal))
                {
                    ver = ver.Substring(m.Groups[1].Value.Length + 1);
                }
                (loader, lv, mc) = ("forge", ver, m.Groups[1].Value);
            }
            else if ((m = Regex.Match(id, @"(fabric|quilt)[- ](?:loader[- ])?(\d+\.\d+[\w.+\-]*)", RegexOptions.IgnoreCase)).Success)
            {
                (loader, lv) = (Py.Lower(m.Groups[1].Value), m.Groups[2].Value);
            }
            else
            {
                continue;
            }
            if (mc.Length == 0)
            {
                try
                {
                    mc = Py.S(Py.D(inst.VersionJson(id)).Get("inheritsFrom"));
                }
                catch (Exception)
                {
                    mc = "";
                }
            }
            break;
        }
        PyDict meta = Py.DictOr(Py.Or(inst.Meta(), new PyDict()));
        PyDict pack = (meta["modpack"] as PyDict) ?? new PyDict();
        if (loader.Length == 0)
        {
            Match mm = MetaLoaderRx.Match(Py.S(pack.Get("loader")));
            if (mm.Success)
            {
                loader = Py.Lower(mm.Groups[1].Value).Replace("-loader", "");
                lv = mm.Groups[2].Value;
            }
        }
        if (mc.Length == 0)
        {
            mc = Py.S(Mods.DetectMcVersion(inst));
        }
        return (mc, loader, lv);
    }

    /// <summary>mrpack dependencies 里加载器的键名(Modrinth 规范)。</summary>
    public static string MrpackLoaderKey(string loader) => loader switch
    {
        "fabric" => "fabric-loader",
        "quilt" => "quilt-loader",
        "forge" => "forge",
        "neoforge" => "neoforge",
        _ => "",
    };

    /// <summary>包名与版本:参数优先,其次实例元数据(装整合包时记下的 modpack.name / version),最后实例名 / 1.0.0。</summary>
    public static (string Name, string Version) NameVersion(Instance inst, string? name, string? version)
    {
        PyDict meta = Py.DictOr(Py.Or(inst.Meta(), new PyDict()));
        PyDict pack = (meta["modpack"] as PyDict) ?? new PyDict();
        string n = Py.Strip(name ?? "");
        string v = Py.Strip(version ?? "");
        return (n.Length > 0 ? n : Py.S(Py.Or(pack.Get("name"), inst.name)),
                v.Length > 0 ? v : Py.S(Py.Or(pack.Get("version"), "1.0.0")));
    }

    /// <summary>默认文件名:包名-版本.mrpack / .zip(非法字符去掉)。</summary>
    public static string DefaultFileName(string name, string version, string format)
    {
        string stem = Instances.SanitizeInstanceName(name, "modpack");
        string ver = Regex.Replace(version ?? "", @"[^\w.\-+]", "");
        return stem + (ver.Length > 0 ? "-" + ver : "") + Extension(format);
    }

    /// <summary>目录下不重名的路径(重名自动加 -2、-3,绝不覆盖)。</summary>
    public static string UniquePath(string dir, string fileName)
    {
        string path = PPath.Join(dir, fileName);
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        for (int i = 2; File.Exists(PPath.Abs(path)); i++)
        {
            path = PPath.Join(dir, stem + "-" + i + ext);
        }
        return path;
    }

    // ───────────────────────────── 清单 ─────────────────────────────

    private static PyDict Linked(PyDict entry, string format)
    {
        if (format == "curseforge")
        {
            return PyDict.Of(
                "path", Py.S(entry.Get("path")),
                "name", Py.S(entry.Get("name")),
                "size", Py.Int(Py.Or(entry.Get("size"), 0L)),
                "project_id", Py.S(Py.Or(entry.Get("project_id"), entry.Get("curseforge_project"))),
                "file_id", Py.S(Py.Or(entry.Get("file_id"), entry.Get("curseforge_file"))));
        }
        return PyDict.Of(
            "path", Py.S(entry.Get("path")),
            "name", Py.S(entry.Get("name")),
            "size", Py.Int(Py.Or(entry.Get("size"), 0L)),
            "sha1", Py.S(entry.Get("sha1")),
            "sha512", Py.S(entry.Get("sha512")),
            "url", Py.S(entry.Get("url")));
    }

    /// <summary>
    /// 算出这次导出要写什么(不写盘)。返回:format / name / version / summary / author / mc_version / loader / loader_version /
    /// linked(写进索引、安装时下载的模组)/ files(包里真正写入的文件:zip_path / src / size / kind)/ bundled_jars / skipped_jars /
    /// forbidden / excluded / total_bytes(包内文件压缩前合计)/ linked_bytes / warnings / distribution(检查结果,可空)。
    /// </summary>
    public static PyDict BuildPlan(Instance inst, ModpackExportOptions opt, DownloadManager? dm = null, Action<string, long, long>? onNote = null)
    {
        string format = ModpackDistribution.NormalizeFormat(opt.Format);
        bool check = opt.CheckDistribution || format == "curseforge";
        (string mc, string loader, string lv) = DetectLoader(inst);
        (string name, string version) = NameVersion(inst, opt.Name, opt.Version);
        string summary = Py.Strip(opt.Summary).Length > 0 ? Py.Strip(opt.Summary) : Tr("由 TipCL 从实例 {name} 导出").Replace("{name}", inst.name);
        string author = Py.Strip(opt.Author);

        var linked = new List<object>();
        var bundled = new List<object>();
        var skipped = new List<object>();
        var forbidden = new List<object>();
        var excluded = new List<object>();
        var warnings = new List<object>();
        PyDict? dist = null;
        IModpackSource source = opt.Source ?? ModpackSources.Get(dm);

        if (check)
        {
            dist = ModpackDistribution.Check(inst, format, source,
                (file, i, n) => onNote?.Invoke(Tr("解析模组 {name}").Replace("{name}", file), i, n), useCache: true);
            foreach (string key in new[] { "linkable", "bundle", "forbidden" })
            {
                foreach (object raw in Py.ListOr(dist.Get(key)))
                {
                    PyDict e = Py.D(raw);
                    string file = Py.S(e.Get("filename"));
                    if (opt.Exclude.Contains(file))
                    {
                        excluded.Add(file);
                        continue;
                    }
                    switch (key)
                    {
                        case "linkable":
                            linked.Add(Linked(e, format));
                            break;
                        case "forbidden":
                            forbidden.Add(e);
                            break;
                        default:
                            if (Py.Truthy(e.Get("needs_consent")) && !opt.AllowBundledJars)
                            {
                                skipped.Add(e);
                            }
                            else
                            {
                                bundled.Add(e);
                            }
                            break;
                    }
                }
            }
        }
        else
        {
            // 原 ExportMrpack 的口径:Modrinth 按 sha1 找得到就链接,找不到就打进 overrides
            List<string> jars = ModpackDistribution.ModJars(inst);
            for (int i = 0; i < jars.Count; i++)
            {
                string jar = jars[i];
                string file = new PPath(jar).Name;
                onNote?.Invoke(Tr("解析模组 {name}").Replace("{name}", file), i, jars.Count);
                if (opt.Exclude.Contains(file))
                {
                    excluded.Add(file);
                    continue;
                }
                string sha1 = Utils.Sha1File(jar);
                PyDict? hit = null;
                try
                {
                    PyDict? v = source.ModrinthVersionByHash(sha1);
                    PyDict? f = v == null ? null : Py.ListOr(v.Get("files")).Select(x => Py.D(x))
                        .FirstOrDefault(x => string.Equals(Py.S(Py.D(x.Get("hashes")).Get("sha1")), sha1, StringComparison.OrdinalIgnoreCase))
                        ?? Py.ListOr(v?.Get("files")).Select(x => Py.D(x)).FirstOrDefault(x => Py.Truthy(x.Get("primary")));
                    if (f != null && Py.S(f.Get("url")).Length > 0)
                    {
                        hit = f;
                    }
                }
                catch (Exception)
                {
                    hit = null;
                }
                var e = PyDict.Of("filename", file, "path", "mods/" + file, "name", new PPath(jar).Stem, "size", LocalFs.Size(jar), "sha1", sha1);
                if (hit != null)
                {
                    e["sha512"] = ModpackDistribution.Sha512File(jar);
                    e["url"] = Py.S(hit.Get("url"));
                    linked.Add(Linked(e, "mrpack"));
                }
                else
                {
                    bundled.Add(e);
                }
            }
        }

        // 包里真正写入的文件:overrides 目录 + 打包的 jar + 图标
        var files = new List<object>();
        string modsDir = PPath.Join(inst.path, "mods");
        foreach (object raw in bundled)
        {
            PyDict e = Py.D(raw);
            string file = Py.S(e.Get("filename"));
            files.Add(PyDict.Of("zip_path", "overrides/mods/" + file, "src", PPath.Join(modsDir, file), "size", Py.Int(Py.Or(e.Get("size"), 0L)), "kind", "jar"));
        }
        var dirs = opt.Overrides.Select(d => Py.Lower(Py.Strip(d))).Where(d => ExportPack.OVERRIDE_DIRS.Contains(d)).Distinct().ToList();
        foreach (string d in ExportPack.OVERRIDE_DIRS.Where(dirs.Contains))
        {
            string root = PPath.Join(inst.path, d);
            if (!LocalFs.IsDir(root))
            {
                continue;
            }
            foreach (string f in LocalFs.RglobFiles(root).OrderBy(x => x, StringComparer.Ordinal))
            {
                string rel = PPath.Join(d, new PPath(f).RelativeTo(root).ToString()).Replace("\\", "/");
                files.Add(PyDict.Of("zip_path", "overrides/" + rel, "src", f, "size", LocalFs.Size(f), "kind", d));
            }
        }
        string icon = Py.Strip(opt.IconPath);
        if (icon.Length > 0)
        {
            if (LocalFs.IsFile(icon) && icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(PyDict.Of("zip_path", "icon.png", "src", icon, "size", LocalFs.Size(icon), "kind", "icon"));
            }
            else
            {
                warnings.Add(Tr("图标不存在或不是 png,已忽略:{path}").Replace("{path}", icon));
                icon = "";
            }
        }
        if (mc.Length == 0)
        {
            warnings.Add(Tr("没认出实例的 Minecraft 版本,包里不会写游戏版本"));
        }
        if (loader.Length > 0 && lv.Length == 0)
        {
            warnings.Add(Tr("没认出加载器版本,包里不会写加载器(装包后要手动装 {loader})").Replace("{loader}", loader));
        }

        long total = files.Sum(f => Py.Int(Py.D(f).Get("size")));
        long linkedBytes = linked.Sum(f => Py.Int(Py.D(f).Get("size")));
        return PyDict.Of(
            "format", format,
            "name", name,
            "version", version,
            "summary", summary,
            "author", author,
            "mc_version", mc,
            "loader", loader,
            "loader_version", lv,
            "overrides", dirs.Cast<object>().ToList(),
            "linked", linked,
            "files", files,
            "bundled_jars", bundled,
            "skipped_jars", skipped,
            "forbidden", forbidden,
            "excluded", excluded,
            "total_bytes", total,
            "linked_bytes", linkedBytes,
            "icon", icon,
            "warnings", warnings,
            "distribution", dist);
    }

    // ───────────────────────────── 索引文件 ─────────────────────────────

    /// <summary>modrinth.index.json 的内容(字段与 Modrinth 规范一致)。</summary>
    public static PyDict MrpackIndex(PyDict plan)
    {
        var files = new List<object>();
        foreach (object raw in Py.ListOr(plan.Get("linked")))
        {
            PyDict l = Py.D(raw);
            files.Add(PyDict.Of(
                "path", Py.S(l.Get("path")),
                "hashes", PyDict.Of("sha1", Py.S(l.Get("sha1")), "sha512", Py.S(l.Get("sha512"))),
                "downloads", new List<object> { Py.S(l.Get("url")) },
                "fileSize", Py.Int(l.Get("size"))));
        }
        var deps = new PyDict();
        string mc = Py.S(plan.Get("mc_version"));
        if (mc.Length > 0)
        {
            deps["minecraft"] = mc;
        }
        string key = MrpackLoaderKey(Py.S(plan.Get("loader")));
        string lv = Py.S(plan.Get("loader_version"));
        if (key.Length > 0 && lv.Length > 0)
        {
            deps[key] = lv;
        }
        var index = PyDict.Of(
            "formatVersion", 1L,
            "game", "minecraft",
            "versionId", Py.S(plan.Get("version")),
            "name", Py.S(plan.Get("name")));
        string summary = Py.S(plan.Get("summary"));
        if (summary.Length > 0)
        {
            index["summary"] = summary;
        }
        index["files"] = files;
        index["dependencies"] = deps;
        return index;
    }

    /// <summary>CurseForge manifest.json 的内容。</summary>
    public static PyDict CurseManifest(PyDict plan)
    {
        var loaders = new List<object>();
        string loader = Py.S(plan.Get("loader"));
        string lv = Py.S(plan.Get("loader_version"));
        if (loader.Length > 0 && lv.Length > 0)
        {
            loaders.Add(PyDict.Of("id", loader + "-" + lv, "primary", true));
        }
        var files = new List<object>();
        foreach (object raw in Py.ListOr(plan.Get("linked")))
        {
            PyDict l = Py.D(raw);
            files.Add(PyDict.Of("projectID", Py.Int(l.Get("project_id")), "fileID", Py.Int(l.Get("file_id")), "required", true));
        }
        return PyDict.Of(
            "minecraft", PyDict.Of("version", Py.S(plan.Get("mc_version")), "modLoaders", loaders),
            "manifestType", "minecraftModpack",
            "manifestVersion", 1L,
            "name", Py.S(plan.Get("name")),
            "version", Py.S(plan.Get("version")),
            "author", Py.S(plan.Get("author")),
            "files", files,
            "overrides", "overrides");
    }

    private static string ModList(PyDict plan)
    {
        var sb = new StringBuilder("<ul>\n");
        foreach (object raw in Py.ListOr(plan.Get("linked")))
        {
            PyDict l = Py.D(raw);
            sb.Append("<li><a href=\"https://www.curseforge.com/projects/").Append(WebUtility.HtmlEncode(Py.S(l.Get("project_id"))))
              .Append("\">").Append(WebUtility.HtmlEncode(Py.S(l.Get("name")))).Append("</a></li>\n");
        }
        return sb.Append("</ul>\n").ToString();
    }

    // ───────────────────────────── 写包 ─────────────────────────────

    /// <summary>禁止分发的模组还在清单里时的拒绝信息(空 = 可以导出)。</summary>
    public static string ForbiddenMessage(PyDict plan)
    {
        var names = Py.ListOr(plan.Get("forbidden")).Select(x => Py.S(Py.D(x).Get("filename"))).ToList();
        if (names.Count == 0)
        {
            return "";
        }
        return Tr("有 {n} 个模组禁止第三方分发,不能导出:{list}。请先替换或禁用它们,或排除后再导出")
            .Replace("{n}", names.Count.ToString()).Replace("{list}", string.Join(", ", names));
    }

    /// <summary>
    /// 导出整合包。返回 {ok, path, format, name, version, mc_version, loader, loader_version, files_linked, files_written,
    /// bundled_jars, skipped_jars, excluded, bytes, warnings}。遇到禁止分发的模组抛 ModpackError,不写任何文件。
    /// </summary>
    public static PyDict Export(Instance inst, string dest, ModpackExportOptions opt, DownloadManager? dm = null, Action<string, long, long>? onNote = null)
    {
        dest = PPath.Norm(dest);
        PyDict plan = BuildPlan(inst, opt, dm, onNote);
        string refuse = ForbiddenMessage(plan);
        if (refuse.Length > 0)
        {
            throw new ModpackError(refuse);
        }
        string abs = PPath.Abs(dest);
        Utils.EnsureDir(new PPath(dest).Parent.ToString());
        if (File.Exists(abs) && !opt.Overwrite)
        {
            throw new FileExistsError(Tr("目标文件已存在,不覆盖:{path}").Replace("{path}", dest));
        }
        string format = Py.S(plan.Get("format"));
        string tmp = abs + ".part";
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        List<object> files = Py.ListOr(plan.Get("files"));
        try
        {
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                void Text(string entryName, string content)
                {
                    using Stream s = zip.CreateEntry(entryName, CompressionLevel.Optimal).Open();
                    byte[] bytes = utf8.GetBytes(content);
                    s.Write(bytes, 0, bytes.Length);
                }
                if (format == "curseforge")
                {
                    Text("manifest.json", PyJson.Dumps(CurseManifest(plan), 2, ensureAscii: false));
                    Text("modlist.html", ModList(plan));
                }
                else
                {
                    Text("modrinth.index.json", PyJson.Dumps(MrpackIndex(plan), 2, ensureAscii: false));
                }
                for (int i = 0; i < files.Count; i++)
                {
                    PyDict f = Py.D(files[i]);
                    string src = Py.S(f.Get("src"));
                    ZipArchiveEntry entry = zip.CreateEntry(Py.S(f.Get("zip_path")), CompressionLevel.Optimal);
                    DateTime t = File.GetLastWriteTime(PPath.Abs(src));
                    entry.LastWriteTime = t.Year < 1980 ? new DateTime(1980, 1, 1) : t;
                    using Stream to = entry.Open();
                    using FileStream from = File.OpenRead(PPath.Abs(src));
                    from.CopyTo(to);
                }
            }
            if (File.Exists(abs))
            {
                File.Delete(abs);
            }
            File.Move(tmp, abs);
        }
        catch (Exception)
        {
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch (Exception)
            {
            }
            throw;
        }
        onNote?.Invoke(Tr("导出完成"), 1L, 1L);
        return PyDict.Of(
            "ok", true,
            "path", dest,
            "format", format,
            "name", plan.Get("name"),
            "version", plan.Get("version"),
            "mc_version", plan.Get("mc_version"),
            "loader", plan.Get("loader"),
            "loader_version", plan.Get("loader_version"),
            "files_linked", (long)Py.ListOr(plan.Get("linked")).Count,
            "files_written", (long)files.Count,
            "bundled_jars", Py.ListOr(plan.Get("bundled_jars")).Select(x => (object)Py.S(Py.D(x).Get("filename"))).ToList(),
            "skipped_jars", Py.ListOr(plan.Get("skipped_jars")).Select(x => (object)Py.S(Py.D(x).Get("filename"))).ToList(),
            "excluded", plan.Get("excluded"),
            "bytes", new FileInfo(abs).Length,
            "warnings", plan.Get("warnings"));
    }
}
