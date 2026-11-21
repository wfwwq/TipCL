// i18n:ignore-file —— 本文件的中文都是给模型看的:主题词表(用户原话匹配用)与工具结果里的 reason / note / next。
// 计划卡上的步骤标题在 ModpackPreview.PlanSteps(走 I18nCore.Tr)。
//
// AI2-modpack:一句话造包的候选清单(plan_modpack,只读 + 联网)。
//   1. 主题描述 → Modrinth 分类(technology / optimization / magic …,词表见 THEME_WORDS),外加模型给的英文 keywords;
//   2. 每个分类 / 关键词各搜一次 Modrinth(search_mods 背后同一个 /search 接口,按 MC 版本 + 加载器 + 分类过滤,
//      空查询时按下载量排),去掉 library 类(它们作为前置随装),按规模配额挑;
//   3. 每个候选取该 MC + 加载器下最新版本(Mods.ListVersions,与 install_mod 选版本同源):
//      required 依赖 = 前置清单(install_mod 会随装),incompatible 依赖 = 冲突;
//   4. 冲突提示两种:Modrinth 版本元数据声明的 incompatible(确定),以及已知同类模组的功能重复(推断,结果里 inferred=true)。
// 只出清单,不建实例、不装任何东西;后续步骤由模型按计划卡逐步走确认链。
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TipCL.Core.Launcher.Ai;

/// <summary>一句话造包:出候选模组清单。</summary>
public static class ModpackPlanner
{
    /// <summary>主题词 → Modrinth 分类(分类名是 Modrinth 官方的 mod 分类;词表只做用户用词到分类的映射,不预设任何模组)。</summary>
    public static readonly (string Category, string[] Words)[] THEME_WORDS =
    {
        ("technology", new[] { "科技", "机械", "工业", "自动化", "电力", "technology", "tech", "automation", "industrial" }),
        ("optimization", new[] { "优化", "性能", "帧数", "流畅", "提帧", "optimization", "optimize", "performance", "fps" }),
        ("magic", new[] { "魔法", "法术", "魔导", "magic" }),
        ("adventure", new[] { "冒险", "探索", "地牢", "adventure", "exploration", "dungeon" }),
        ("storage", new[] { "存储", "仓储", "收纳", "背包", "storage", "inventory" }),
        ("decoration", new[] { "装饰", "建筑", "家具", "decoration", "building", "furniture" }),
        ("utility", new[] { "实用", "便利", "辅助", "生活质量", "工具", "utility", "qol", "quality of life" }),
        ("worldgen", new[] { "世界生成", "地形", "生物群系", "群系", "worldgen", "terrain", "biome" }),
        ("food", new[] { "食物", "美食", "烹饪", "农业", "种田", "food", "cooking", "farming" }),
        ("mobs", new[] { "生物", "怪物", "动物", "mobs", "creatures", "animals" }),
        ("equipment", new[] { "装备", "武器", "盔甲", "equipment", "weapons", "armor" }),
        ("transportation", new[] { "交通", "运输", "载具", "transportation", "vehicles" }),
        ("game-mechanics", new[] { "游戏机制", "玩法", "机制", "mechanics" }),
        ("economy", new[] { "经济", "交易", "economy", "trading" }),
        ("management", new[] { "管理", "management" }),
        ("social", new[] { "社交", "聊天", "social" }),
    };

    /// <summary>「轻量」类用词:没给规模时默认 small。</summary>
    public static readonly string[] LIGHT_WORDS = { "轻量", "精简", "小型", "轻度", "lightweight", "minimal" };

    /// <summary>
    /// 已知同类模组(按 Modrinth slug):同一组里装两个通常是功能重复。这是**推断**(模组元数据不一定声明),
    /// 结果里标 inferred=true,交给用户选留哪个。
    /// </summary>
    public static readonly (string Niche, string[] Slugs)[] NICHES =
    {
        ("小地图", new[] { "xaeros-minimap", "journeymap", "voxelmap-updated" }),
        ("配方查看", new[] { "jei", "rei", "emi" }),
        ("渲染优化核心", new[] { "sodium", "embeddium", "rubidium" }),
        ("光影加载器", new[] { "iris", "oculus" }),
        ("光照引擎优化", new[] { "starlight", "phosphor" }),
        ("通用逻辑优化", new[] { "lithium", "canary", "radium" }),
    };

    /// <summary>规模 → 目标候选数(不含随装的前置)。</summary>
    public static int SizeTarget(string? size, string? theme = null)
    {
        string s = Py.Lower(Py.Strip(size ?? ""));
        if (s.Length == 0)
        {
            string t = Py.Lower(theme ?? "");
            s = LIGHT_WORDS.Any(w => t.Contains(w, StringComparison.OrdinalIgnoreCase)) ? "small" : "medium";
        }
        return s switch
        {
            "small" or "小" or "s" => 8,
            "large" or "大" or "l" => 40,
            _ => 20,
        };
    }

    /// <summary>规模名归一(small / medium / large)。</summary>
    public static string SizeName(int target) => target <= 8 ? "small" : target >= 40 ? "large" : "medium";

    /// <summary>主题描述 → 分类(按在原话里出现的先后排序,去重)。</summary>
    public static List<string> ThemeCategories(string? theme)
    {
        string t = Py.Lower(theme ?? "");
        var hits = new List<(int At, string Category)>();
        foreach ((string category, string[] words) in THEME_WORDS)
        {
            int at = int.MaxValue;
            foreach (string w in words)
            {
                int i = t.IndexOf(w, StringComparison.OrdinalIgnoreCase);
                if (i >= 0 && i < at)
                {
                    at = i;
                }
            }
            if (at != int.MaxValue)
            {
                hits.Add((at, category));
            }
        }
        return hits.OrderBy(h => h.At).Select(h => h.Category).Distinct().ToList();
    }

    private static readonly HashSet<string> Loaders = new(StringComparer.Ordinal) { "fabric", "forge", "neoforge", "quilt" };

    private sealed class Pick
    {
        public PyDict Hit = new();
        public string Group = "";
        public bool ByCategory;
        public int Rank;
        public PyDict? Version;
        public string Error = "";
        public string Id => Py.S(Hit.Get("project_id"));
        public string Slug => Py.S(Hit.Get("slug"));
        public string Title => Py.S(Py.Or(Hit.Get("title"), Hit.Get("slug")));
    }

    private static bool IsLibrary(PyDict hit)
        => Py.ListOr(hit.Get("categories")).Concat(Py.ListOr(hit.Get("display_categories"))).Any(c => Py.S(c) == "library");

    private static List<string> DepIds(PyDict? version, string type)
        => Py.ListOr(version?.Get("dependencies")).Select(d => Py.D(d))
            .Where(d => Py.S(d.Get("dependency_type")) == type && Py.S(d.Get("project_id")).Length > 0)
            .Select(d => Py.S(d.Get("project_id"))).Distinct().ToList();

    /// <summary>
    /// 出候选清单。返回 {ok, mc_version, loader, theme, size, target_count, categories, keywords, candidates[], count,
    /// extra_dependencies[], conflicts[], dropped[], next}。每个候选:slug / project_id / title / description / downloads /
    /// category / reason / version_number / version_id / filename / size / dependencies[] / conflicts[]。
    /// </summary>
    public static PyDict Plan(string mcVersion, string loader, string theme, string? size, IList<string>? keywords, IModpackSource source)
    {
        string mc = Py.Strip(mcVersion ?? "");
        if (mc.Length == 0)
        {
            throw new ValueError("缺少 MC 版本(mc_version),如 1.20.1");
        }
        string ld = InstanceUpgrade.NormalizeLoader(loader);
        if (!Loaders.Contains(ld))
        {
            throw new ValueError("loader 只能是 fabric / forge / neoforge / quilt(原版装不了模组)");
        }
        int target = SizeTarget(size, theme);
        List<string> categories = ThemeCategories(theme);
        List<string> extra = (keywords ?? new List<string>()).Select(k => Py.Strip(k)).Where(k => k.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var groups = categories.Select(c => (Key: c, ByCategory: true)).Concat(extra.Select(k => (Key: k, ByCategory: false))).ToList();
        bool fallback = false;
        if (groups.Count == 0)
        {
            if (Py.Strip(theme ?? "").Length == 0)
            {
                throw new ValueError("缺少主题描述(theme)");
            }
            // 主题里一个分类都没认出:拿原话当搜索词试一次,并在结果里提醒模型补英文 keywords
            groups.Add((Py.Strip(theme!), false));
            fallback = true;
        }

        // 1. 每组各搜一次,按配额挑
        int quota = (int)Math.Ceiling(target / (double)groups.Count);
        var picks = new List<Pick>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var searchErrors = new List<object>();
        foreach ((string key, bool byCategory) in groups)
        {
            List<object> hits;
            try
            {
                hits = byCategory
                    ? source.SearchModrinth("", mc, ld, key, Math.Min(100, quota * 2 + 10))
                    : source.SearchModrinth(key, mc, ld, null, Math.Min(100, quota * 2 + 10));
            }
            catch (Exception e)
            {
                searchErrors.Add(PyDict.Of("group", key, "error", Py.ExcStr(e)));
                continue;
            }
            int taken = 0;
            int rank = 0;
            foreach (object raw in hits)
            {
                PyDict hit = Py.D(raw);
                rank++;
                string id = Py.S(Py.Or(hit.Get("project_id"), hit.Get("slug")));
                if (id.Length == 0 || IsLibrary(hit) || !seen.Add(id))
                {
                    continue;
                }
                if (Py.S(hit.Get("project_id")).Length == 0)
                {
                    hit["project_id"] = id;
                }
                picks.Add(new Pick { Hit = hit, Group = key, ByCategory = byCategory, Rank = rank });
                if (++taken >= quota || picks.Count >= target)
                {
                    break;
                }
            }
            if (picks.Count >= target)
            {
                break;
            }
        }
        if (picks.Count == 0)
        {
            return PyDict.Of(
                "ok", false,
                "error", searchErrors.Count > 0 ? "search_failed" : "no_candidates",
                "message", searchErrors.Count > 0
                    ? "Modrinth 搜索失败,稍后再试"
                    : "在 " + mc + " + " + ld + " 下没搜到符合主题的模组" + (fallback ? "(主题里没认出分类,请用 keywords 给英文搜索词再试)" : ""),
                "search_errors", searchErrors);
        }

        // 2. 每个候选取该 MC + 加载器下最新版本(拿前置与不兼容声明)
        Parallel.ForEach(picks, new ParallelOptions { MaxDegreeOfParallelism = 6 }, p =>
        {
            try
            {
                p.Version = source.ModrinthLatestVersion(Py.S(Py.Or(p.Hit.Get("slug"), p.Id)), mc, ld);
                if (p.Version == null)
                {
                    p.Error = "Modrinth 上没有 " + mc + " + " + ld + " 的版本";
                }
            }
            catch (Exception e)
            {
                p.Error = "查版本失败: " + Py.ExcStr(e);
            }
        });
        var dropped = picks.Where(p => p.Version == null).Select(p => (object)PyDict.Of("title", p.Title, "slug", p.Slug, "reason", p.Error)).ToList();
        picks = picks.Where(p => p.Version != null).ToList();

        // 3. 前置与冲突对象的名字(一次批量查询)
        var pickIds = new HashSet<string>(picks.Select(p => p.Id), StringComparer.Ordinal);
        var otherIds = picks.SelectMany(p => DepIds(p.Version, "required").Concat(DepIds(p.Version, "incompatible")))
            .Where(id => !pickIds.Contains(id)).Distinct().ToList();
        Dictionary<string, PyDict> projects;
        try
        {
            projects = otherIds.Count > 0 ? source.ModrinthProjects(otherIds) : new Dictionary<string, PyDict>();
        }
        catch (Exception)
        {
            projects = new Dictionary<string, PyDict>();
        }
        string TitleOf(string id)
        {
            Pick? p = picks.FirstOrDefault(x => x.Id == id);
            if (p != null)
            {
                return p.Title;
            }
            return projects.TryGetValue(id, out PyDict? proj) ? Py.S(Py.Or(proj.Get("title"), proj.Get("slug"), id)) : id;
        }
        string SlugOf(string id)
        {
            Pick? p = picks.FirstOrDefault(x => x.Id == id);
            if (p != null)
            {
                return p.Slug;
            }
            return projects.TryGetValue(id, out PyDict? proj) ? Py.S(proj.Get("slug")) : "";
        }

        // 4. 冲突:元数据声明的不兼容(确定)+ 已知同类功能重复(推断)
        var pairs = new List<object>();
        var perPick = picks.ToDictionary(p => p.Id, _ => new List<object>(), StringComparer.Ordinal);
        var pairSeen = new HashSet<string>(StringComparer.Ordinal);
        void AddPair(Pick a, string otherId, string kind, bool inferred, string note)
        {
            string k = string.CompareOrdinal(a.Id, otherId) < 0 ? a.Id + "|" + otherId : otherId + "|" + a.Id;
            if (!pairSeen.Add(kind + ":" + k))
            {
                return;
            }
            pairs.Add(PyDict.Of("a", a.Title, "b", TitleOf(otherId), "kind", kind, "inferred", inferred, "note", note));
            perPick[a.Id].Add(PyDict.Of("with", TitleOf(otherId), "kind", kind, "inferred", inferred, "note", note));
            if (perPick.TryGetValue(otherId, out List<object>? other))
            {
                other.Add(PyDict.Of("with", a.Title, "kind", kind, "inferred", inferred, "note", note));
            }
        }
        var depOwners = new Dictionary<string, List<Pick>>(StringComparer.Ordinal);
        foreach (Pick p in picks)
        {
            foreach (string d in DepIds(p.Version, "required"))
            {
                if (!depOwners.TryGetValue(d, out List<Pick>? owners))
                {
                    depOwners[d] = owners = new List<Pick>();
                }
                owners.Add(p);
            }
        }
        foreach (Pick p in picks)
        {
            foreach (string bad in DepIds(p.Version, "incompatible"))
            {
                if (pickIds.Contains(bad))
                {
                    AddPair(p, bad, "incompatible", false, p.Title + " 的 Modrinth 版本信息声明与 " + TitleOf(bad) + " 不兼容,只能留一个");
                }
                else if (depOwners.TryGetValue(bad, out List<Pick>? owners))
                {
                    foreach (Pick o in owners.Where(o => o.Id != p.Id))
                    {
                        AddPair(p, o.Id, "incompatible", false, p.Title + " 声明与 " + TitleOf(bad) + " 不兼容,而它是 " + o.Title + " 的前置");
                    }
                }
            }
        }
        foreach ((string niche, string[] slugs) in NICHES)
        {
            var same = picks.Where(p => slugs.Contains(p.Slug, StringComparer.OrdinalIgnoreCase)).ToList();
            for (int i = 0; i < same.Count; i++)
            {
                for (int j = i + 1; j < same.Count; j++)
                {
                    AddPair(same[i], same[j].Id, "overlap", true,
                        same[i].Title + " 和 " + same[j].Title + " 都是「" + niche + "」类模组,功能重复(按已知同类模组推断,元数据没有声明),通常只留一个");
                }
            }
        }

        // 5. 组装
        var candidates = new List<object>();
        var extraDeps = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (Pick p in picks)
        {
            PyDict v = p.Version!;
            PyDict file = Py.D(Py.ListOr(v.Get("files")).FirstOrDefault(f => Py.Truthy(Py.D(f).Get("primary"))) ?? Py.ListOr(v.Get("files")).FirstOrDefault());
            var deps = new List<object>();
            foreach (string d in DepIds(v, "required"))
            {
                deps.Add(PyDict.Of("project_id", d, "slug", SlugOf(d), "title", TitleOf(d), "in_list", pickIds.Contains(d)));
                if (!pickIds.Contains(d))
                {
                    if (!extraDeps.TryGetValue(d, out List<string>? by))
                    {
                        extraDeps[d] = by = new List<string>();
                    }
                    by.Add(p.Title);
                }
            }
            long downloads = Py.Int(Py.Or(p.Hit.Get("downloads"), 0L));
            string reason = p.ByCategory
                ? "主题对应 Modrinth 分类「" + p.Group + "」,在 " + mc + " + " + ld + " 下按下载量排第 " + p.Rank + "(下载 " + downloads + " 次)"
                : "按关键词「" + p.Group + "」在 " + mc + " + " + ld + " 下搜索,相关度排第 " + p.Rank + "(下载 " + downloads + " 次)";
            string desc = Py.S(p.Hit.Get("description"));
            candidates.Add(PyDict.Of(
                "slug", p.Slug,
                "project_id", p.Id,
                "source", "modrinth",
                "title", p.Title,
                "description", desc.Length > 160 ? desc.Substring(0, 160) : desc,
                "downloads", downloads,
                "category", p.ByCategory ? p.Group : "",
                "keyword", p.ByCategory ? "" : p.Group,
                "reason", reason,
                "version_number", Py.S(v.Get("version_number")),
                "version_id", Py.S(v.Get("id")),
                "filename", Py.S(file.Get("filename")),
                "size", Py.Int(Py.Or(file.Get("size"), 0L)),
                "dependencies", deps,
                "conflicts", perPick[p.Id]));
        }
        var extraList = extraDeps.Select(kv => (object)PyDict.Of(
            "project_id", kv.Key, "slug", SlugOf(kv.Key), "title", TitleOf(kv.Key),
            "required_by", kv.Value.Distinct().Cast<object>().ToList())).ToList();
        return PyDict.Of(
            "ok", true,
            "mc_version", mc,
            "loader", ld,
            "theme", theme ?? "",
            "size", SizeName(target),
            "target_count", (long)target,
            "categories", categories.Cast<object>().ToList(),
            "keywords", extra.Cast<object>().ToList(),
            "theme_unrecognized", fallback,
            "count", (long)candidates.Count,
            "candidates", candidates,
            "extra_dependencies", extraList,
            "conflicts", pairs,
            "dropped", dropped,
            "search_errors", searchErrors,
            "next", "先把清单(理由、前置、冲突)讲给用户,用 ask_user(allow_multiple=true)让用户勾选确认;有冲突的先问留哪个。用户确认前不要 create_instance、不要装任何东西。"
                    + "确认后:create_instance → install_game(" + mc + " + " + ld + ")→ install_mods_batch(只装确认的;前置会随装)→ scan_mod_conflicts + preflight_check → (可选)launch_game 试启动并 get_latest_log → export_modpack。");
    }
}
