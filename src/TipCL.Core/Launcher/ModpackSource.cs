// i18n:ignore-file —— 本文件只有联网查询与给模型看的数据字段,没有界面文案。
// AI2-modpack:整合包制作要用到的联网查询(Modrinth 按 sha1 查文件 / CurseForge 指纹与分发许可 /
// Modrinth 搜索、版本、项目批量查询)收在 IModpackSource 后面:导出(界面与 AI 共用的 ModpackExport)、
// 分发许可检查(ModpackDistribution)、一句话造包(ModpackPlanner)都只认这个接口,测试塞假源,不联网。
// 请求形状与已有代码同一条路:Modrinth version_file 与 ExportPack / ModUpdate 相同,CurseForge 走 Mods.CfPost,
// 版本列表走 Mods.ListVersions(与 install_mod 背后的选版本逻辑同源)。
using System;
using System.Collections.Generic;
using System.Linq;

namespace TipCL.Core.Launcher;

/// <summary>整合包制作所需的联网能力(默认实现见 <see cref="NetModpackSource"/>;测试替换成假源)。</summary>
public interface IModpackSource
{
    /// <summary>按 sha1 查 Modrinth 上的文件所属版本(原样返回 Modrinth 的 version 对象);确实没有返回 null,联网失败抛异常。</summary>
    PyDict? ModrinthVersionByHash(string sha1);

    /// <summary>CurseForge 指纹匹配:fingerprint → {mod_id, file_id, file_name, download_url}。没匹配上的不在字典里;联网失败抛异常。</summary>
    Dictionary<long, PyDict> CurseForgeMatches(IList<long> fingerprints);

    /// <summary>CurseForge 项目信息:mod_id → {name, slug, allow_distribution(true/false/null), url}。联网失败抛异常。</summary>
    Dictionary<string, PyDict> CurseForgeMods(IList<string> modIds);

    /// <summary>Modrinth 搜索(按 MC 版本 + 加载器 + 分类过滤;query 为空时按下载量排)。返回 Modrinth 原样的 hits。</summary>
    List<object> SearchModrinth(string query, string mcVersion, string loader, string? category, int limit);

    /// <summary>该项目在指定 MC + 加载器下最新的版本(Mods.ListVersions 的行形状:id / version_number / files / dependencies);没有返回 null。</summary>
    PyDict? ModrinthLatestVersion(string project, string mcVersion, string loader);

    /// <summary>Modrinth 项目批量查询:project_id → 项目(id / slug / title / categories)。</summary>
    Dictionary<string, PyDict> ModrinthProjects(IList<string> ids);
}

/// <summary>整合包联网源的取用点(测试注入点:非空时用它造源)。</summary>
public static class ModpackSources
{
    /// <summary>测试注入点。</summary>
    public static Func<IModpackSource>? Factory;

    public static IModpackSource Get(DownloadManager? dm = null) => Factory?.Invoke() ?? new NetModpackSource(dm);
}

/// <summary>默认联网源(Modrinth 官方 API + CurseForge API,镜像回退沿用 DownloadManager / Source 的既有逻辑)。</summary>
public sealed class NetModpackSource : IModpackSource
{
    private readonly DownloadManager _dm;

    public NetModpackSource(DownloadManager? dm = null) => _dm = dm ?? new DownloadManager(4L);

    public PyDict? ModrinthVersionByHash(string sha1)
    {
        try
        {
            return _dm.FetchJson(ModUpdate.API + "/version_file/" + sha1 + "?algorithm=sha1", 15L) as PyDict;
        }
        catch (HTTPError e) when (e.StatusCode == 404)
        {
            return null;
        }
    }

    public Dictionary<long, PyDict> CurseForgeMatches(IList<long> fingerprints)
    {
        var result = new Dictionary<long, PyDict>();
        if (fingerprints.Count == 0)
        {
            return result;
        }
        object resp = Mods.CfPost(_dm, "/fingerprints", PyDict.Of("fingerprints", fingerprints.Cast<object>().ToList()), null, 30L);
        foreach (object raw in Py.ListOr(Py.D(Py.D(resp).Get("data")).Get("exactMatches")))
        {
            PyDict m = Py.D(raw);
            PyDict file = Py.D(m.Get("file"));
            long fp = Py.Int(Py.Or(file.Get("fileFingerprint"), 0L));
            if (fp == 0)
            {
                continue;
            }
            result[fp] = PyDict.Of(
                "mod_id", Py.S(Py.Or(file.Get("modId"), m.Get("id"))),
                "file_id", Py.S(file.Get("id")),
                "file_name", Py.S(file.Get("fileName")),
                "download_url", Py.S(file.Get("downloadUrl")));
        }
        return result;
    }

    public Dictionary<string, PyDict> CurseForgeMods(IList<string> modIds)
    {
        var result = new Dictionary<string, PyDict>(StringComparer.Ordinal);
        var ids = modIds.Where(x => long.TryParse(x, out _)).Select(x => (object)long.Parse(x)).Distinct().ToList();
        if (ids.Count == 0)
        {
            return result;
        }
        object resp = Mods.CfPost(_dm, "/mods", PyDict.Of("modIds", ids), null, 30L);
        foreach (object raw in Py.ListOr(Py.D(resp).Get("data")))
        {
            PyDict d = Py.D(raw);
            string id = Py.S(d.Get("id"));
            if (id.Length == 0)
            {
                continue;
            }
            object? allow = d.Get("allowModDistribution");
            result[id] = PyDict.Of(
                "name", Py.S(d.Get("name")),
                "slug", Py.S(d.Get("slug")),
                "allow_distribution", allow is bool b ? (object)b : null,
                "url", Py.S(Py.D(d.Get("links")).Get("websiteUrl")));
        }
        return result;
    }

    /// <summary>Modrinth 的 facets:每个内层数组是 OR,外层是 AND(MC 版本、加载器、分类各占一组)。</summary>
    public static string Facets(string mcVersion, string loader, string? category)
    {
        var groups = new List<object> { new List<object> { "project_type:mod" } };
        if (mcVersion.Length > 0)
        {
            groups.Add(new List<object> { "versions:" + mcVersion });
        }
        if (loader.Length > 0)
        {
            // Quilt 能吃 Fabric 模组:目标是 quilt 时两种都算(与 InstanceUpgrade 同口径)
            groups.Add(loader == "quilt"
                ? new List<object> { "categories:quilt", "categories:fabric" }
                : new List<object> { "categories:" + loader });
        }
        if (!string.IsNullOrEmpty(category))
        {
            groups.Add(new List<object> { "categories:" + category });
        }
        return PyJson.Dumps(groups);
    }

    public List<object> SearchModrinth(string query, string mcVersion, string loader, string? category, int limit)
    {
        string q = Py.Strip(query ?? "");
        PyDict ps = PyDict.Of("query", q, "facets", Facets(mcVersion, loader, category), "limit", (long)Math.Clamp(limit, 1, 100),
            "index", q.Length > 0 ? "relevance" : "downloads");
        Exception? last = null;
        foreach (string api in Source.ModrinthApiBases())
        {
            try
            {
                object o = _dm.FetchJson(Mods.WithParams(api + "/search", ps), Mods.API_TIMEOUT, expand: false);
                return Py.ListOr(Py.D(o).Get("hits"));
            }
            catch (Exception e)
            {
                last = e;
            }
        }
        throw new ModError("Modrinth 搜索失败: " + (last == null ? "None" : Py.ExcStr(last)));
    }

    public PyDict? ModrinthLatestVersion(string project, string mcVersion, string loader)
    {
        List<object>? loaders = loader.Length == 0 ? null
            : loader == "quilt" ? new List<object> { "quilt", "fabric" } : new List<object> { loader };
        List<object> versions = Mods.ListVersions(_dm, project, mcVersion.Length > 0 ? mcVersion : null, loaders);
        if (versions.Count == 0)
        {
            return null;
        }
        // 优先正式版;没有正式版才用 beta / alpha
        object? pick = versions.FirstOrDefault(v => Py.S(Py.D(v).Get("version_type")) == "release") ?? versions[0];
        return Py.D(pick);
    }

    public Dictionary<string, PyDict> ModrinthProjects(IList<string> ids)
    {
        var result = new Dictionary<string, PyDict>(StringComparer.Ordinal);
        var wanted = ids.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal).ToList();
        for (int i = 0; i < wanted.Count; i += 80)
        {
            var chunk = wanted.Skip(i).Take(80).Cast<object>().ToList();
            object o = _dm.FetchJson(Mods.WithParams(ModUpdate.API + "/projects", PyDict.Of("ids", PyJson.Dumps(chunk))), 20L);
            foreach (object raw in Py.ListOr(o))
            {
                PyDict p = Py.D(raw);
                string id = Py.S(p.Get("id"));
                if (id.Length > 0)
                {
                    result[id] = p;
                }
            }
        }
        return result;
    }
}
