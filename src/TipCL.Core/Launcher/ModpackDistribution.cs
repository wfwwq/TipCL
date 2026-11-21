// i18n:ignore-file —— 本文件的中文都是给模型看的工具结果(分类理由 reason / advice),界面上的预览在 Ai/Modpack/ModpackPreview.cs(走 I18nCore.Tr)。
// AI2-modpack:整合包分发许可检查(check_modpack_distribution;export_modpack 导出前也必跑这一份)。
//
// 逐个检查实例 mods/ 下的 jar,分成三类:
//   · linkable  可链接 —— 目标格式能直接写下载引用,包里不带 jar:
//       mrpack:Modrinth 按 sha1 找得到这个文件,且下载地址在 Modrinth 规范允许的域名内(cdn.modrinth.com / github.com /
//               raw.githubusercontent.com / gitlab.com);
//       curseforge:CurseForge 指纹匹配得到(manifest.json 写 projectID / fileID)。
//   · bundle    需打包 —— 只能把 jar 本体打进 overrides/mods:
//       作者在 CurseForge 上允许第三方分发(allowModDistribution=true)的 → 可以直接打包(needs_consent=false);
//       两边都找不到来源的本地 jar、许可查不到 / 联网失败的、只在另一平台上有的 → 可能侵权,默认不打包,
//       需要用户明确同意(export_modpack 的 allow_bundled_jars=true)才打进去(needs_consent=true)。
//   · forbidden 禁止分发 —— mrpack 下只在 CurseForge 上有、且作者关闭了第三方分发(allowModDistribution=false):
//       既不能链接(CurseForge 地址不在 mrpack 允许域名内,作者也不允许第三方下载),也不能打包 → 导出拒绝,提示替换或排除。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace TipCL.Core.Launcher;

/// <summary>整合包分发许可检查(三类清单)。</summary>
public static class ModpackDistribution
{
    /// <summary>Modrinth 整合包规范(modrinth.index.json)允许的下载域名。</summary>
    public static readonly string[] MRPACK_ALLOWED_HOSTS = { "cdn.modrinth.com", "github.com", "raw.githubusercontent.com", "gitlab.com" };

    /// <summary>一次检查最多看这么多个 jar(再多就截断并在结果里说明)。</summary>
    public const int MAX_MODS = 400;

    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, (DateTime At, PyDict Result)> Cache = new(StringComparer.Ordinal);

    /// <summary>格式归一:curseforge / cf / curse / zip → curseforge;其余 → mrpack。</summary>
    public static string NormalizeFormat(string? format)
    {
        string f = Py.Lower(Py.Strip(format ?? ""));
        return f is "curseforge" or "cf" or "curse" or "zip" or "manifest" ? "curseforge" : "mrpack";
    }

    /// <summary>这个下载地址能不能写进 mrpack 的 downloads(只认 https + 规范里的域名)。</summary>
    public static bool AllowedMrpackUrl(string? url)
    {
        if (!Uri.TryCreate(url ?? "", UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }
        string host = uri.Host.ToLowerInvariant();
        return MRPACK_ALLOWED_HOSTS.Contains(host);
    }

    /// <summary>实例 mods/ 下启用的 jar(按文件名排序;.disabled 的不算,导出时也不带)。</summary>
    public static List<string> ModJars(Instance inst)
    {
        string dir = PPath.Join(inst.path, "mods");
        if (!LocalFs.IsDir(dir))
        {
            return new List<string>();
        }
        return LocalFs.Children(dir)
            .Where(p => LocalFs.IsFile(p) && Py.Lower(new PPath(p).Name).EndsWith(".jar", StringComparison.Ordinal))
            .OrderBy(p => new PPath(p).Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>mods 目录的指纹(文件名 + 大小 + 修改时间):变了就不复用缓存。</summary>
    public static string Signature(Instance inst, string format)
    {
        var parts = new List<string> { inst.name, NormalizeFormat(format) };
        foreach (string jar in ModJars(inst))
        {
            DateTime t = File.GetLastWriteTimeUtc(PPath.Abs(jar));
            parts.Add(new PPath(jar).Name + ":" + LocalFs.Size(jar) + ":" + t.Ticks);
        }
        return string.Join("|", parts);
    }

    /// <summary>10 分钟内、mods 目录没变过的上一次检查结果(预览与导出复用,不重复联网)。</summary>
    public static PyDict? Cached(Instance inst, string format)
    {
        string sig = Signature(inst, format);
        lock (CacheLock)
        {
            return Cache.TryGetValue(sig, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromMinutes(10) ? hit.Result : null;
        }
    }

    /// <summary>清掉缓存(测试用)。</summary>
    public static void ClearCache()
    {
        lock (CacheLock)
        {
            Cache.Clear();
        }
    }

    public static string Sha512File(string path)
    {
        using FileStream fs = File.OpenRead(PPath.Abs(path));
        using SHA512 sha = SHA512.Create();
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    private static string ModName(string jar)
    {
        try
        {
            PyDict info = Ai.Conflict.InspectJar(jar);
            string name = Py.S(info.Get("name"));
            return name.Length > 0 ? name : new PPath(jar).Stem;
        }
        catch (Exception)
        {
            return new PPath(jar).Stem;
        }
    }

    /// <summary>在 Modrinth version 对象里找 sha1 对得上的那个文件(对不上就取 primary)。</summary>
    private static PyDict? MatchFile(PyDict version, string sha1)
    {
        List<object> files = Py.ListOr(version.Get("files"));
        object? exact = files.FirstOrDefault(f => string.Equals(Py.S(Py.D(Py.D(f).Get("hashes")).Get("sha1")), sha1, StringComparison.OrdinalIgnoreCase));
        object? pick = exact ?? files.FirstOrDefault(f => Py.Truthy(Py.D(f).Get("primary"))) ?? files.FirstOrDefault();
        return pick == null ? null : Py.D(pick);
    }

    /// <summary>
    /// 跑一次检查。<paramref name="useCache"/>=true 时先看 10 分钟内的缓存(导出 / 预览用);
    /// 结果总会写回缓存。每个模组的条目:filename / path / name / size / sha1 / sha512 / source / category / reason /
    /// needs_consent,以及能链接时的 url(mrpack)或 project_id + file_id(curseforge)。
    /// </summary>
    public static PyDict Check(Instance inst, string format, IModpackSource? source = null, Action<string, long, long>? onNote = null, bool useCache = true)
    {
        format = NormalizeFormat(format);
        if (useCache && Cached(inst, format) is { } hit)
        {
            return hit;
        }
        string sig = Signature(inst, format);
        source ??= ModpackSources.Get();
        List<string> jars = ModJars(inst);
        bool truncated = jars.Count > MAX_MODS;
        jars = jars.Take(MAX_MODS).ToList();
        var errors = new List<object>();

        // 1. 本地:哈希 / 指纹 / 名字
        var rows = new List<PyDict>();
        for (int i = 0; i < jars.Count; i++)
        {
            string jar = jars[i];
            string file = new PPath(jar).Name;
            onNote?.Invoke(file, i, jars.Count);
            long fp = 0;
            try
            {
                fp = ModUpdate.CfFingerprint(jar);
            }
            catch (Exception)
            {
                fp = 0;
            }
            rows.Add(PyDict.Of(
                "filename", file,
                "path", "mods/" + file,
                "abs", jar,
                "name", ModName(jar),
                "size", LocalFs.Size(jar),
                "sha1", Utils.Sha1File(jar),
                "sha512", Sha512File(jar),
                "fingerprint", fp));
        }

        // 2. Modrinth:逐个按 sha1 查(与原 ExportMrpack 同一个接口)
        var mrHits = new Dictionary<string, PyDict>(StringComparer.Ordinal);
        var mrFailed = new HashSet<string>(StringComparer.Ordinal);
        foreach (PyDict row in rows)
        {
            string file = Py.S(row.Get("filename"));
            try
            {
                PyDict? v = source.ModrinthVersionByHash(Py.S(row.Get("sha1")));
                if (v != null && MatchFile(v, Py.S(row.Get("sha1"))) is { } f && Py.S(f.Get("url")).Length > 0)
                {
                    mrHits[file] = PyDict.Of(
                        "project_id", Py.S(v.Get("project_id")),
                        "version_id", Py.S(v.Get("id")),
                        "url", Py.S(f.Get("url")),
                        "sha512", Py.S(Py.D(f.Get("hashes")).Get("sha512")));
                }
            }
            catch (Exception e)
            {
                mrFailed.Add(file);
                errors.Add(PyDict.Of("filename", file, "source", "modrinth", "error", Py.ExcStr(e)));
            }
        }

        // 3. CurseForge:一次指纹批量匹配 + 一次项目批量查询(拿 allowModDistribution)
        var cfHits = new Dictionary<string, PyDict>(StringComparer.Ordinal);
        bool cfFailed = false;
        try
        {
            var fps = rows.Select(r => Py.Int(r.Get("fingerprint"))).Where(x => x != 0).Distinct().ToList();
            Dictionary<long, PyDict> matches = source.CurseForgeMatches(fps);
            var modIds = matches.Values.Select(m => Py.S(m.Get("mod_id"))).Where(x => x.Length > 0).Distinct().ToList();
            Dictionary<string, PyDict> mods = modIds.Count > 0 ? source.CurseForgeMods(modIds) : new Dictionary<string, PyDict>();
            foreach (PyDict row in rows)
            {
                if (matches.TryGetValue(Py.Int(row.Get("fingerprint")), out PyDict? m))
                {
                    var hitRow = new PyDict(m);
                    if (mods.TryGetValue(Py.S(m.Get("mod_id")), out PyDict? info))
                    {
                        hitRow["mod_name"] = Py.S(info.Get("name"));
                        hitRow["allow_distribution"] = info.Get("allow_distribution");
                        hitRow["url_page"] = Py.S(info.Get("url"));
                    }
                    else
                    {
                        hitRow["allow_distribution"] = null;
                    }
                    cfHits[Py.S(row.Get("filename"))] = hitRow;
                }
            }
        }
        catch (Exception e)
        {
            cfFailed = true;
            errors.Add(PyDict.Of("filename", "*", "source", "curseforge", "error", Py.ExcStr(e)));
        }

        // 4. 分类
        var linkable = new List<object>();
        var bundle = new List<object>();
        var forbidden = new List<object>();
        foreach (PyDict row in rows)
        {
            string file = Py.S(row.Get("filename"));
            mrHits.TryGetValue(file, out PyDict? mr);
            cfHits.TryGetValue(file, out PyDict? cf);
            var entry = new PyDict(row);
            entry.Remove("abs");
            entry.Remove("fingerprint");
            if (mr != null)
            {
                entry["modrinth_project"] = Py.S(mr.Get("project_id"));
            }
            if (cf != null)
            {
                entry["curseforge_project"] = Py.S(cf.Get("mod_id"));
                entry["curseforge_file"] = Py.S(cf.Get("file_id"));
                entry["allow_distribution"] = cf.Get("allow_distribution");
            }
            object? allow = cf?.Get("allow_distribution");
            if (format == "mrpack")
            {
                if (mr != null && AllowedMrpackUrl(Py.S(mr.Get("url"))))
                {
                    entry["category"] = "linkable";
                    entry["source"] = "modrinth";
                    entry["url"] = Py.S(mr.Get("url"));
                    entry["needs_consent"] = false;
                    entry["reason"] = "Modrinth 上按 sha1 找到了这个文件,写进 modrinth.index.json 的 files(安装时从 Modrinth 下载,包里不带 jar)";
                    linkable.Add(entry);
                }
                else if (cf != null && allow is false)
                {
                    entry["category"] = "forbidden";
                    entry["source"] = "curseforge";
                    entry["needs_consent"] = false;
                    entry["reason"] = "只在 CurseForge 上找到,且作者关闭了第三方分发(allowModDistribution=false):mrpack 不能链接 CurseForge 下载地址,也不能把 jar 打进包里。请换成 Modrinth 上有的同类模组、禁用它,或用 exclude 排除";
                    forbidden.Add(entry);
                }
                else if (cf != null && allow is true)
                {
                    entry["category"] = "bundle";
                    entry["source"] = "curseforge";
                    entry["needs_consent"] = false;
                    entry["reason"] = "只在 CurseForge 上找到,作者允许第三方分发(allowModDistribution=true);mrpack 不能链接 CurseForge 地址,所以把 jar 打进 overrides/mods";
                    bundle.Add(entry);
                }
                else
                {
                    entry["category"] = "bundle";
                    entry["source"] = cf != null ? "curseforge" : (mr != null ? "modrinth" : "local");
                    entry["needs_consent"] = true;
                    entry["reason"] = cf != null
                        ? "CurseForge 上找到了文件,但查不到作者是否允许第三方分发:只能打进 overrides,可能侵犯作者权利,默认不打包,需用户明确同意"
                        : mr != null
                            ? "Modrinth 上的下载地址不在 mrpack 允许的域名内:只能打进 overrides,默认不打包,需用户明确同意"
                            : (mrFailed.Contains(file) || cfFailed)
                                ? "联网核对失败,无法确认来源与许可:按来源不明处理,默认不打包(可稍后重试检查)"
                                : "Modrinth / CurseForge 都找不到这个文件(可能是自制、改过或从别处下载的 jar):只能打进 overrides,可能侵犯作者权利,默认不打包,需用户明确同意";
                    bundle.Add(entry);
                }
            }
            else
            {
                if (cf != null)
                {
                    entry["category"] = "linkable";
                    entry["source"] = "curseforge";
                    entry["project_id"] = Py.S(cf.Get("mod_id"));
                    entry["file_id"] = Py.S(cf.Get("file_id"));
                    entry["needs_consent"] = false;
                    entry["reason"] = allow is false
                        ? "CurseForge 指纹匹配到了,写进 manifest.json 的 files;注意作者关闭了第三方分发,只有 CurseForge 官方客户端能自动下载,第三方启动器装包时可能要手动下载"
                        : "CurseForge 指纹匹配到了,写进 manifest.json 的 files(projectID / fileID,安装时从 CurseForge 下载,包里不带 jar)";
                    linkable.Add(entry);
                }
                else
                {
                    entry["category"] = "bundle";
                    entry["source"] = mr != null ? "modrinth" : "local";
                    entry["needs_consent"] = true;
                    entry["reason"] = mr != null
                        ? "只在 Modrinth 上有,CurseForge 清单无法引用:只能把 jar 打进 overrides,请确认该模组许可允许再分发,默认不打包,需用户明确同意"
                        : (mrFailed.Contains(file) || cfFailed)
                            ? "联网核对失败,无法确认来源与许可:按来源不明处理,默认不打包(可稍后重试检查)"
                            : "Modrinth / CurseForge 都找不到这个文件(可能是自制、改过或从别处下载的 jar):只能打进 overrides,可能侵犯作者权利,默认不打包,需用户明确同意";
                    bundle.Add(entry);
                }
            }
        }

        long consent = bundle.LongCount(e => Py.Truthy(Py.D(e).Get("needs_consent")));
        var result = PyDict.Of(
            "ok", true,
            "instance", inst.name,
            "format", format,
            "total", (long)rows.Count,
            "truncated", truncated,
            "summary", PyDict.Of(
                "linkable", (long)linkable.Count,
                "bundle", (long)bundle.Count,
                "bundle_needs_consent", consent,
                "forbidden", (long)forbidden.Count),
            "linkable", linkable,
            "bundle", bundle,
            "forbidden", forbidden,
            "lookup_errors", errors,
            "advice", forbidden.Count > 0
                ? "有禁止分发的模组:导出会被拒绝。先把它们换成 Modrinth 上有的同类模组、禁用,或在 export_modpack 的 exclude 里排除。"
                : consent > 0
                    ? "有来源不明 / 许可未知的 jar:默认不打包(包里会缺这些模组)。只有用户明确同意时才传 allow_bundled_jars=true。"
                    : "全部模组都能合规导出。");
        lock (CacheLock)
        {
            Cache[sig] = (DateTime.UtcNow, result);
        }
        return result;
    }
}
