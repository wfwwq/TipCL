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
// AI3-content:候选来源加上 CurseForge(复用 Mods.SearchCurseforge / Mods.CfFiles):同一组里两边的结果按加权下载量
// (Mods.WeightedDownloads,与搜索页同一套权重)合并,同一模组(slug / 标题归一后相同)只留一条 —— Modrinth 有对应版本就用 Modrinth
// (mrpack 能直接链接),没有才用 CurseForge 的文件;连不上 CurseForge 时照常只用 Modrinth,结果里的 curseforge / notes 写明。
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
        /// <summary>modrinth / curseforge(两边都有且 Modrinth 有对应版本时用 Modrinth)。</summary>
        public string Source = "modrinth";
        /// <summary>同一模组在另一个平台上的条目(合并去重时挂上;Modrinth 查不到版本时改用它)。</summary>
        public PyDict? Twin;
        public bool SwitchedFromModrinth;
        public string Group = "";
        public bool ByCategory;
        public int Rank;
        public PyDict? Version;
        public PyDict? CfFile;
        public string Error = "";
        public bool Cf => Source == "curseforge";
        public string CfId => Py.S(Hit.Get("id"));
        public string Id => Cf ? "cf:" + CfId : Py.S(Hit.Get("project_id"));
        public string Slug => Py.S(Hit.Get("slug"));
        public string Title => Py.S(Py.Or(Hit.Get("title"), Hit.Get("slug")));
        public long Downloads => Py.Int(Py.Or(Hit.Get("downloads"), 0L));
        public bool Matched => Cf ? CfFile != null : Version != null;
    }

    /// <summary>Modrinth 分类 → CurseForge 分类名里的关键词(小写子串匹配);CurseForge 没有对应分类的不列(那一组只搜 Modrinth)。</summary>
    public static readonly Dictionary<string, string> CF_CATEGORY = new(StringComparer.Ordinal)
    {
        ["technology"] = "technology",
        ["optimization"] = "performance",
        ["magic"] = "magic",
        ["adventure"] = "adventure",
        ["storage"] = "storage",
        ["utility"] = "utility",
        ["worldgen"] = "world gen",
        ["food"] = "food",
        ["mobs"] = "mobs",
        ["equipment"] = "armor",
        ["transportation"] = "transport",
    };

    public static string? CfCategory(string modrinthCategory) => CF_CATEGORY.TryGetValue(modrinthCategory ?? "", out string? c) ? c : null;

    /// <summary>去重用的归一名:只留小写字母数字("Just Enough Items" 与 "just-enough-items" 算同一个)。</summary>
    public static string NormKey(string? s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static bool IsCfLibrary(PyDict hit) => Py.ListOr(hit.Get("cf_categories")).Any(c => Py.Lower(Py.S(c)).Contains("library"));

    /// <summary>
    /// 两个各自排好序的结果按加权下载量归并(各自内部顺序不变,Modrinth 单独有结果时与以前完全一致)。
    /// Rank = 在各自平台结果里的名次。
    /// </summary>
    private static IEnumerable<(PyDict Hit, string Source, int Rank)> MergeByDownloads(List<object> mr, List<object> cf)
    {
        static double W(PyDict h, string src) => Mods.WeightedDownloads(PyDict.Of("source", src, "downloads", Py.Or(h.Get("downloads"), 0L)));
        int i = 0;
        int j = 0;
        while (i < mr.Count || j < cf.Count)
        {
            if (j >= cf.Count || (i < mr.Count && W(Py.D(mr[i]), "modrinth") >= W(Py.D(cf[j]), "curseforge")))
            {
                PyDict hit = Py.D(mr[i]);
                i++;
                yield return (hit, "modrinth", i);
            }
            else
            {
                PyDict hit = Py.D(cf[j]);
                j++;
                yield return (hit, "curseforge", j);
            }
        }
    }

    private static PyDict? CfFileOf(IModpackSource source, string id, string mc, string ld, out string error)
    {
        error = "";
        try
        {
            return source.CurseForgeLatestFile(id, mc, ld);
        }
        catch (NotSupportedException)
        {
            error = "当前数据源不支持 CurseForge";
        }
        catch (Exception e)
        {
            error = "查 CurseForge 文件失败: " + Py.ExcStr(e);
        }
        return null;
    }

    private static bool IsLibrary(PyDict hit)
        => Py.ListOr(hit.Get("categories")).Concat(Py.ListOr(hit.Get("display_categories"))).Any(c => Py.S(c) == "library");

    private static List<string> DepIds(PyDict? version, string type)
        => Py.ListOr(version?.Get("dependencies")).Select(d => Py.D(d))
            .Where(d => Py.S(d.Get("dependency_type")) == type && Py.S(d.Get("project_id")).Length > 0)
            .Select(d => Py.S(d.Get("project_id"))).Distinct().ToList();

    /// <summary>一个候选的前置 / 不兼容(Modrinth 取版本元数据;CurseForge 取文件的 dependencies,id 带 cf: 前缀)。</summary>
    private static List<string> Deps(Pick p, string type)
        => p.Cf
            ? Py.ListOr(p.CfFile?.Get("dependencies")).Select(d => Py.D(d))
                .Where(d => Py.S(d.Get("type")) == type && Py.S(d.Get("mod_id")).Length > 0)
                .Select(d => "cf:" + Py.S(d.Get("mod_id"))).Distinct().ToList()
            : DepIds(p.Version, type);

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

        // 1. 每组各搜一次 Modrinth + CurseForge,两边按加权下载量合并、同一模组只留一条,按配额挑
        int quota = (int)Math.Ceiling(target / (double)groups.Count);
        var picks = new List<Pick>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var mrKeys = new Dictionary<string, Pick>(StringComparer.Ordinal);
        var cfKeys = new Dictionary<string, Pick>(StringComparer.Ordinal);
        var searchErrors = new List<object>();
        string cfStatus = "ok";
        string cfError = "";
        bool cfSearched = false;
        IEnumerable<string> KeysOf(PyDict hit) => new[] { NormKey(Py.S(hit.Get("slug"))), NormKey(Py.S(hit.Get("title"))) }.Where(k => k.Length >= 3);
        void Remember(Pick p, PyDict hit, bool cf)
        {
            foreach (string k in KeysOf(hit))
            {
                (cf ? cfKeys : mrKeys).TryAdd(k, p);
            }
        }
        // 另一个平台上已经挑中、还没有配对的同名模组
        Pick? TwinOf(PyDict hit, bool cf)
        {
            foreach (string k in KeysOf(hit))
            {
                if ((cf ? mrKeys : cfKeys).TryGetValue(k, out Pick? p) && p.Twin == null && p.Cf != cf)
                {
                    return p;
                }
            }
            return null;
        }
        foreach ((string key, bool byCategory) in groups)
        {
            int limit = Math.Min(100, quota * 2 + 10);
            List<object> hits = new();
            try
            {
                hits = byCategory
                    ? source.SearchModrinth("", mc, ld, key, limit)
                    : source.SearchModrinth(key, mc, ld, null, limit);
            }
            catch (Exception e)
            {
                searchErrors.Add(PyDict.Of("group", key, "error", Py.ExcStr(e)));
            }
            List<object> cfHits = new();
            string? cfCat = byCategory ? CfCategory(key) : null;
            if (cfStatus == "ok" && (!byCategory || cfCat != null))
            {
                try
                {
                    cfHits = source.SearchCurseForge(byCategory ? "" : key, mc, ld, cfCat, Math.Min(50, limit));
                    cfSearched = true;
                }
                catch (NotSupportedException e)
                {
                    cfStatus = "unsupported";
                    cfError = Py.ExcStr(e);
                }
                catch (Exception e)
                {
                    // 连不上 CurseForge:后面的组不再试(每次都要等超时),照常只用 Modrinth,结果里注明
                    cfStatus = "unavailable";
                    cfError = Py.ExcStr(e);
                    searchErrors.Add(PyDict.Of("group", key, "source", "curseforge", "error", cfError));
                }
            }
            int taken = 0;
            foreach ((PyDict hit, string src, int rank) in MergeByDownloads(hits, cfHits))
            {
                if (src == "modrinth")
                {
                    string id = Py.S(Py.Or(hit.Get("project_id"), hit.Get("slug")));
                    if (id.Length == 0 || IsLibrary(hit) || !seen.Add(id))
                    {
                        continue;
                    }
                    if (Py.S(hit.Get("project_id")).Length == 0)
                    {
                        hit["project_id"] = id;
                    }
                    Pick? twin = TwinOf(hit, cf: false);
                    if (twin != null)
                    {
                        // 同一模组 CurseForge 那条先到(下载量更高):改成 Modrinth 为主(mrpack 能直接链接),CurseForge 挂在 Twin 上
                        twin.Twin = twin.Hit;
                        twin.Hit = hit;
                        twin.Source = "modrinth";
                        twin.Rank = rank;
                        Remember(twin, hit, cf: false);
                        continue;
                    }
                    var p = new Pick { Hit = hit, Group = key, ByCategory = byCategory, Rank = rank };
                    picks.Add(p);
                    Remember(p, hit, cf: false);
                }
                else
                {
                    string cfid = Py.S(hit.Get("id"));
                    if (cfid.Length == 0 || IsCfLibrary(hit) || !seen.Add("cf:" + cfid))
                    {
                        continue;
                    }
                    Pick? twin = TwinOf(hit, cf: true);
                    if (twin != null)
                    {
                        twin.Twin = hit; // 已有 Modrinth 那条:只记下 CurseForge 上也有,不重复列
                        Remember(twin, hit, cf: true);
                        continue;
                    }
                    var p = new Pick { Hit = hit, Source = "curseforge", Group = key, ByCategory = byCategory, Rank = rank };
                    picks.Add(p);
                    Remember(p, hit, cf: true);
                }
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
                    ? "Modrinth / CurseForge 搜索失败,稍后再试"
                    : "在 " + mc + " + " + ld + " 下没搜到符合主题的模组" + (fallback ? "(主题里没认出分类,请用 keywords 给英文搜索词再试)" : ""),
                "search_errors", searchErrors);
        }

        // 2. 每个候选取该 MC + 加载器下最新版本(拿前置与不兼容声明):Modrinth 优先;
        //    Modrinth 没有对应版本、而 CurseForge 上的同一模组有对应文件时,改用 CurseForge
        Parallel.ForEach(picks, new ParallelOptions { MaxDegreeOfParallelism = 6 }, p =>
        {
            if (p.Cf)
            {
                p.CfFile = CfFileOf(source, p.CfId, mc, ld, out string err);
                if (p.CfFile == null)
                {
                    p.Error = err.Length > 0 ? err : "CurseForge 上没有 " + mc + " + " + ld + " 的文件";
                }
                return;
            }
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
            if (p.Version != null || p.Twin == null)
            {
                return;
            }
            PyDict? file = CfFileOf(source, Py.S(p.Twin.Get("id")), mc, ld, out string cfErr);
            if (file != null)
            {
                PyDict mrHit = p.Hit;
                p.Hit = p.Twin;
                p.Twin = mrHit;
                p.Source = "curseforge";
                p.CfFile = file;
                p.SwitchedFromModrinth = true;
            }
            else if (cfErr.Length > 0)
            {
                p.Error += ";CurseForge:" + cfErr;
            }
        });
        var dropped = picks.Where(p => !p.Matched).Select(p => (object)PyDict.Of("title", p.Title, "slug", p.Slug, "source", p.Source, "reason", p.Error)).ToList();
        picks = picks.Where(p => p.Matched).ToList();

        // 3. 前置与冲突对象的名字(Modrinth / CurseForge 各一次批量查询)
        var pickIds = new HashSet<string>(picks.Select(p => p.Id), StringComparer.Ordinal);
        var otherIds = picks.SelectMany(p => Deps(p, "required").Concat(Deps(p, "incompatible")))
            .Where(id => !pickIds.Contains(id)).Distinct().ToList();
        var mrOther = otherIds.Where(id => !id.StartsWith("cf:", StringComparison.Ordinal)).ToList();
        var cfOther = otherIds.Where(id => id.StartsWith("cf:", StringComparison.Ordinal)).Select(id => id.Substring(3)).ToList();
        Dictionary<string, PyDict> projects;
        try
        {
            projects = mrOther.Count > 0 ? source.ModrinthProjects(mrOther) : new Dictionary<string, PyDict>();
        }
        catch (Exception)
        {
            projects = new Dictionary<string, PyDict>();
        }
        Dictionary<string, PyDict> cfProjects;
        try
        {
            cfProjects = cfOther.Count > 0 ? source.CurseForgeMods(cfOther) : new Dictionary<string, PyDict>();
        }
        catch (Exception)
        {
            cfProjects = new Dictionary<string, PyDict>();
        }
        string TitleOf(string id)
        {
            Pick? p = picks.FirstOrDefault(x => x.Id == id);
            if (p != null)
            {
                return p.Title;
            }
            if (id.StartsWith("cf:", StringComparison.Ordinal))
            {
                return cfProjects.TryGetValue(id.Substring(3), out PyDict? cp) ? Py.S(Py.Or(cp.Get("name"), cp.Get("slug"), id)) : id;
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
            if (id.StartsWith("cf:", StringComparison.Ordinal))
            {
                return cfProjects.TryGetValue(id.Substring(3), out PyDict? cp) ? Py.S(cp.Get("slug")) : "";
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
            foreach (string d in Deps(p, "required"))
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
            foreach (string bad in Deps(p, "incompatible"))
            {
                if (pickIds.Contains(bad))
                {
                    AddPair(p, bad, "incompatible", false, p.Title + (p.Cf ? " 的 CurseForge 文件信息声明与 " : " 的 Modrinth 版本信息声明与 ") + TitleOf(bad) + " 不兼容,只能留一个");
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
            var deps = new List<object>();
            foreach (string d in Deps(p, "required"))
            {
                bool cfDep = d.StartsWith("cf:", StringComparison.Ordinal);
                PyDict dep = PyDict.Of("project_id", cfDep ? d.Substring(3) : d, "slug", SlugOf(d), "title", TitleOf(d), "in_list", pickIds.Contains(d));
                if (cfDep)
                {
                    dep["source"] = "curseforge";
                    dep["id"] = d.Substring(3);
                }
                deps.Add(dep);
                if (!pickIds.Contains(d))
                {
                    if (!extraDeps.TryGetValue(d, out List<string>? by))
                    {
                        extraDeps[d] = by = new List<string>();
                    }
                    by.Add(p.Title);
                }
            }
            long downloads = p.Downloads;
            string desc = Py.S(Py.Or(p.Hit.Get("description"), p.Hit.Get("summary")));
            desc = desc.Length > 160 ? desc.Substring(0, 160) : desc;
            if (!p.Cf)
            {
                PyDict v = p.Version!;
                PyDict file = Py.D(Py.ListOr(v.Get("files")).FirstOrDefault(f => Py.Truthy(Py.D(f).Get("primary"))) ?? Py.ListOr(v.Get("files")).FirstOrDefault());
                string reason = p.ByCategory
                    ? "主题对应 Modrinth 分类「" + p.Group + "」,在 " + mc + " + " + ld + " 下按下载量排第 " + p.Rank + "(下载 " + downloads + " 次)"
                    : "按关键词「" + p.Group + "」在 " + mc + " + " + ld + " 下搜索,相关度排第 " + p.Rank + "(下载 " + downloads + " 次)";
                PyDict row = PyDict.Of(
                    "slug", p.Slug,
                    "project_id", p.Id,
                    "source", "modrinth",
                    "title", p.Title,
                    "description", desc,
                    "downloads", downloads,
                    "category", p.ByCategory ? p.Group : "",
                    "keyword", p.ByCategory ? "" : p.Group,
                    "reason", reason,
                    "version_number", Py.S(v.Get("version_number")),
                    "version_id", Py.S(v.Get("id")),
                    "filename", Py.S(file.Get("filename")),
                    "size", Py.Int(Py.Or(file.Get("size"), 0L)),
                    "dependencies", deps,
                    "conflicts", perPick[p.Id]);
                if (p.Twin != null)
                {
                    // 同一模组 CurseForge 上也有:合并成这一条(列一次),留下 CurseForge 的 id 和下载量给模型参考
                    row["also_on_curseforge"] = PyDict.Of("id", Py.S(p.Twin.Get("id")), "slug", Py.S(p.Twin.Get("slug")),
                        "downloads", Py.Int(Py.Or(p.Twin.Get("downloads"), 0L)));
                }
                candidates.Add(row);
                continue;
            }
            PyDict cf = p.CfFile!;
            string how = p.ByCategory
                ? "主题对应分类「" + p.Group + "」(CurseForge 分类关键词「" + CfCategory(p.Group) + "」),在 " + mc + " 下按热门度排第 " + p.Rank
                : "按关键词「" + p.Group + "」在 " + mc + " 下搜索,排第 " + p.Rank;
            string cfReason = "CurseForge:" + how + "(下载 " + downloads + " 次);有 " + mc + " + " + ld + " 的文件"
                + (p.SwitchedFromModrinth ? ";Modrinth 上也有这个模组,但没有 " + mc + " + " + ld + " 的版本,所以用 CurseForge 的" : "");
            candidates.Add(PyDict.Of(
                "slug", p.Slug,
                "project_id", p.CfId,
                "id", p.CfId,
                "source", "curseforge",
                "title", p.Title,
                "description", desc,
                "downloads", downloads,
                "category", p.ByCategory ? p.Group : "",
                "keyword", p.ByCategory ? "" : p.Group,
                "reason", cfReason,
                "version_number", Py.S(cf.Get("display_name")),
                "version_id", Py.S(cf.Get("file_id")),
                "file_id", Py.S(cf.Get("file_id")),
                "filename", Py.S(cf.Get("filename")),
                "size", Py.Int(Py.Or(cf.Get("size"), 0L)),
                "dependencies", deps,
                "conflicts", perPick[p.Id],
                "note", "CurseForge 来源:install_mods_batch 里写 {source: \"curseforge\", id: \"" + p.CfId + "\"};"
                    + "导出 mrpack 时不能直接链接(要用户同意打包,作者禁止分发时会被拒),导出 curseforge 格式没问题"));
        }
        var extraList = extraDeps.Select(kv =>
        {
            bool cfDep = kv.Key.StartsWith("cf:", StringComparison.Ordinal);
            PyDict e = PyDict.Of(
                "project_id", cfDep ? kv.Key.Substring(3) : kv.Key, "slug", SlugOf(kv.Key), "title", TitleOf(kv.Key),
                "required_by", kv.Value.Distinct().Cast<object>().ToList());
            if (cfDep)
            {
                e["source"] = "curseforge";
            }
            return (object)e;
        }).ToList();
        if (cfStatus == "ok" && !cfSearched)
        {
            cfStatus = "not_searched";
        }
        var notes = new List<object>();
        switch (cfStatus)
        {
            case "unavailable":
                notes.Add("这次连不上 CurseForge(" + cfError + "),候选只来自 Modrinth;要 CurseForge 上的模组可以稍后再试一次 plan_modpack 或用 search_mods 单独搜。");
                break;
            case "unsupported":
                notes.Add("当前数据源不支持 CurseForge,候选只来自 Modrinth。");
                break;
            case "not_searched":
                notes.Add("主题对应的分类在 CurseForge 上没有同名分类,这次只搜了 Modrinth;给英文 keywords 可以让 CurseForge 也一起搜。");
                break;
        }
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
            "sources", picks.Select(p => p.Source).Distinct().Cast<object>().ToList(),
            "curseforge", PyDict.Of("status", cfStatus, "error", cfError),
            "notes", notes,
            "extra_dependencies", extraList,
            "conflicts", pairs,
            "dropped", dropped,
            "search_errors", searchErrors,
            "next", "先把清单(理由、前置、冲突、来源)讲给用户,用 ask_user(allow_multiple=true)让用户勾选确认;有冲突的先问留哪个。用户确认前不要 create_instance、不要装任何东西。"
                    + "确认后:create_instance → install_game(" + mc + " + " + ld + ")→ install_mods_batch(只装确认的;前置会随装;CurseForge 来源的写 {source: \"curseforge\", id})→ scan_mod_conflicts + preflight_check → (可选)launch_game 试启动并 get_latest_log → export_modpack。");
    }
}
