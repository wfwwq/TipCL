// AI3-content:数据包 / 资源包里引用的物品、方块 ID 的命名空间检查 —— 只给 warnings,不拒绝。
// i18n:ignore-file —— 本文件的中文只出现在回给模型的工具结果(warnings)里;确认卡上给用户看的那一行在 ContentPreview(走 Tr)。
//
// 口径:引用的 ID 命名空间既不是 minecraft、也不是这个实例 mods 目录里已装模组的 mod id 时提醒一句
// (多半是拼错了,或者用的是没装的模组的物品;游戏加载时会跳过 / 报错这条内容)。
//   · 已装模组的 mod id 复用 ModJarInfo.Read(install_local_mod 用的同一套 jar 元数据解析:fabric.mod.json / quilt.mod.json /
//     META-INF/mods.toml / neoforge.mods.toml);.disabled 的 jar 不算(不会被加载)。按 mods 目录的「文件名 + 大小 + 修改时间」签名缓存。
//   · 物品 / 方块 / 配方类型:只认 minecraft + 已装 mod id;
//   · 标签引用(#ns:path):另外认加载器的公共标签命名空间(c / forge / neoforge / fabric / quilt)和本包自己用到的命名空间(包里可以自己定义标签);
//   · 资源包的模型 / 贴图引用:另外认本包自己的命名空间(包里自己放的模型、贴图)。
// 不查 path 部分(物品是否真存在要游戏说了算 —— 用 validate_content 看日志)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TipCL.Core.Launcher.Ai;

/// <summary>内容里引用的 ID 的命名空间检查(只警告)。</summary>
public static class ContentIds
{
    /// <summary>一处引用:ID、种类(item / block / tag / recipe_type / model / texture)、出处(哪个文件)。</summary>
    public sealed record Ref(string Id, string Kind, string Where);

    /// <summary>加载器约定的公共标签命名空间(没有同名模组,但标签确实存在)。</summary>
    public static readonly string[] LoaderTagNamespaces = { "c", "forge", "neoforge", "fabric", "quilt" };

    /// <summary>一次最多列出这么多个可疑命名空间。</summary>
    public const int MAX_WARNINGS = 20;

    private static readonly object Gate = new();

    private static readonly Dictionary<string, (string Sig, HashSet<string> Ids)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>测试 / 自检用:清掉 mod id 缓存。</summary>
    public static void ClearCache()
    {
        lock (Gate)
        {
            Cache.Clear();
        }
    }

    /// <summary>mods 目录里已装(启用)模组的 mod id(小写)。读不了的 jar 跳过。</summary>
    public static HashSet<string> InstalledModIds(string modsDir)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(modsDir) || !Directory.Exists(modsDir))
        {
            return ids;
        }
        List<FileInfo> jars;
        try
        {
            jars = new DirectoryInfo(modsDir).EnumerateFiles("*.jar")
                .Where(f => f.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return ids;
        }
        string sig = string.Join("|", jars.Select(f => f.Name + ":" + f.Length + ":" + f.LastWriteTimeUtc.Ticks));
        lock (Gate)
        {
            if (Cache.TryGetValue(modsDir, out var hit) && hit.Sig == sig)
            {
                return new HashSet<string>(hit.Ids, StringComparer.Ordinal);
            }
        }
        foreach (FileInfo jar in jars)
        {
            ModJarInfo info = ModJarInfo.Read(jar.FullName);
            if (info.Id.Length > 0)
            {
                ids.Add(info.Id.ToLowerInvariant());
            }
        }
        lock (Gate)
        {
            Cache[modsDir] = (sig, new HashSet<string>(ids, StringComparer.Ordinal));
        }
        return ids;
    }

    /// <summary>resource location 的命名空间(省略时是 minecraft;# 标签前缀去掉)。</summary>
    public static string NamespaceOf(string id)
    {
        string s = (id ?? "").Trim().TrimStart('#');
        int i = s.IndexOf(':');
        return i > 0 ? s.Substring(0, i) : "minecraft";
    }

    private static void Add(List<Ref> refs, object? v, string kind, string where)
    {
        if (v is string s && s.Trim().Length > 0)
        {
            string t = s.Trim();
            refs.Add(t.StartsWith("#", StringComparison.Ordinal) ? new Ref(t, "tag", where) : new Ref(t, kind, where));
        }
    }

    // ───────────────────────────── 数据包 ─────────────────────────────

    /// <summary>配方原料:字符串(1.21.2 起)/ {item} / {tag} / 数组 / 模组加载器的嵌套写法(items / ingredient)。</summary>
    private static void Ingredient(List<Ref> refs, object? v, string where)
    {
        switch (v)
        {
            case string:
                Add(refs, v, "item", where);
                break;
            case List<object> list:
                foreach (object x in list)
                {
                    Ingredient(refs, x, where);
                }
                break;
            case PyDict d:
                Add(refs, d.Get("item"), "item", where);
                if (d.Get("tag") is string tag)
                {
                    refs.Add(new Ref("#" + tag.Trim().TrimStart('#'), "tag", where));
                }
                foreach (string k in new[] { "items", "ingredient", "ingredients", "children" })
                {
                    if (d.Get(k) != null)
                    {
                        Ingredient(refs, d.Get(k), where);
                    }
                }
                break;
        }
    }

    /// <summary>配方产物:字符串 / {id} / {item} / 数组。</summary>
    private static void Stack(List<Ref> refs, object? v, string where)
    {
        switch (v)
        {
            case string:
                Add(refs, v, "item", where);
                break;
            case List<object> list:
                foreach (object x in list)
                {
                    Stack(refs, x, where);
                }
                break;
            case PyDict d:
                Add(refs, d.Get("id") ?? d.Get("item"), "item", where);
                break;
        }
    }

    private static readonly string[] IngredientKeys = { "ingredient", "ingredients", "base", "template", "addition", "input", "inputs" };

    private static readonly string[] ResultKeys = { "result", "results", "output", "outputs" };

    /// <summary>配方引用的 ID:配方类型、原料、产物(原版与常见模组写法)。</summary>
    public static void FromRecipe(object json, string where, List<Ref> refs)
    {
        if (json is not PyDict d)
        {
            return;
        }
        if (d.Get("type") is string type && type.Contains(':'))
        {
            refs.Add(new Ref(type.Trim(), "recipe_type", where));
        }
        foreach (string k in IngredientKeys)
        {
            if (d.Get(k) != null)
            {
                Ingredient(refs, d.Get(k), where);
            }
        }
        if (d.Get("key") is PyDict key)
        {
            foreach (KeyValuePair<string, object?> kv in key)
            {
                Ingredient(refs, kv.Value, where);
            }
        }
        foreach (string k in ResultKeys)
        {
            if (d.Get(k) != null)
            {
                Stack(refs, d.Get(k), where);
            }
        }
    }

    private static void LootEntries(List<Ref> refs, object? entries, string where)
    {
        foreach (object raw in Py.ListOr(entries))
        {
            if (raw is not PyDict e)
            {
                continue;
            }
            string t = Py.S(e.Get("type"));
            string type = t.Contains(':') ? t : "minecraft:" + t;
            if (type == "minecraft:item")
            {
                Add(refs, e.Get("name"), "item", where);
            }
            else if (type == "minecraft:tag" && e.Get("name") is string tag)
            {
                refs.Add(new Ref("#" + tag.Trim().TrimStart('#'), "tag", where));
            }
            if (e.Get("children") != null)
            {
                LootEntries(refs, e.Get("children"), where);
            }
        }
    }

    /// <summary>战利品表里 item / tag 条目的 name(含 alternatives / group / sequence 的 children)。</summary>
    public static void FromLootTable(object json, string where, List<Ref> refs)
    {
        if (json is PyDict d)
        {
            foreach (object pool in Py.ListOr(d.Get("pools")))
            {
                if (pool is PyDict p)
                {
                    LootEntries(refs, p.Get("entries"), where);
                }
            }
        }
    }

    /// <summary>进度的图标物品。</summary>
    public static void FromAdvancement(object json, string where, List<Ref> refs)
    {
        if (json is PyDict d && d.Get("display") is PyDict display && display.Get("icon") is PyDict icon)
        {
            Add(refs, icon.Get("id") ?? icon.Get("item"), "item", where);
        }
    }

    /// <summary>物品 / 方块标签的 values(其他类型的标签不在「物品、方块 ID」范围内,不查)。</summary>
    public static void FromTag(object json, string tagType, string where, List<Ref> refs)
    {
        if (tagType is not ("item" or "block") || json is not PyDict d)
        {
            return;
        }
        foreach (object v in Py.ListOr(d.Get("values")))
        {
            Add(refs, v is PyDict o ? o.Get("id") : v, tagType, where);
        }
    }

    // ───────────────────────────── 资源包 ─────────────────────────────

    /// <summary>模型的 parent 与贴图引用(#变量不算)。</summary>
    public static void FromModel(object json, string where, List<Ref> refs)
    {
        if (json is not PyDict d)
        {
            return;
        }
        if (d.Get("parent") is string parent && !parent.StartsWith("builtin/", StringComparison.Ordinal))
        {
            refs.Add(new Ref(parent.Trim(), "model", where));
        }
        if (d.Get("textures") is PyDict tex)
        {
            foreach (KeyValuePair<string, object?> kv in tex)
            {
                if (kv.Value is string s && !s.StartsWith("#", StringComparison.Ordinal))
                {
                    refs.Add(new Ref(s.Trim(), "texture", where));
                }
            }
        }
    }

    private static void ModelRefs(List<Ref> refs, object? v, string where)
    {
        switch (v)
        {
            case List<object> list:
                foreach (object x in list)
                {
                    ModelRefs(refs, x, where);
                }
                break;
            case PyDict m when m.Get("model") is string model:
                refs.Add(new Ref(model.Trim(), "model", where));
                break;
        }
    }

    /// <summary>方块状态:文件本身对应方块 ns:path,里面引用的模型。</summary>
    public static void FromBlockstate(object json, string blockId, string where, List<Ref> refs)
    {
        refs.Add(new Ref(blockId, "block", where));
        if (json is not PyDict d)
        {
            return;
        }
        if (d.Get("variants") is PyDict variants)
        {
            foreach (KeyValuePair<string, object?> kv in variants)
            {
                ModelRefs(refs, kv.Value, where);
            }
        }
        foreach (object part in Py.ListOr(d.Get("multipart")))
        {
            if (part is PyDict p)
            {
                ModelRefs(refs, p.Get("apply"), where);
            }
        }
    }

    // ───────────────────────────── 判定 ─────────────────────────────

    private static string KindText(string kind) => kind switch
    {
        "item" => "物品",
        "block" => "方块",
        "tag" => "标签",
        "recipe_type" => "配方类型",
        "model" => "模型",
        "texture" => "贴图",
        _ => kind,
    };

    /// <summary>某种引用在这个命名空间下算不算「有来头」。</summary>
    public static bool Known(string kind, string ns, ISet<string> modIds, ISet<string> own)
    {
        if (ns == "minecraft" || modIds.Contains(ns))
        {
            return true;
        }
        return kind switch
        {
            "tag" => own.Contains(ns) || LoaderTagNamespaces.Contains(ns),
            "model" or "texture" => own.Contains(ns),
            _ => false,
        };
    }

    /// <summary>
    /// 按命名空间汇总可疑引用,返回给模型看的 warnings(空 = 没发现)。<paramref name="own"/> 是本包自己的命名空间
    /// (这次要写的文件 + 包里已有的 data/ 或 assets/ 子目录)。
    /// </summary>
    public static List<string> Warnings(IEnumerable<Ref> refs, ISet<string> modIds, ISet<string> own)
    {
        var groups = new Dictionary<string, List<Ref>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (Ref r in refs)
        {
            string ns = NamespaceOf(r.Id);
            if (!ContentValidate.IsNamespace(ns) || Known(r.Kind, ns, modIds, own))
            {
                continue; // 写法不对的 ID 已经由结构校验拦下;这里只管「写法对、但命名空间没有来头」
            }
            if (!groups.TryGetValue(ns, out List<Ref>? list))
            {
                groups[ns] = list = new List<Ref>();
                order.Add(ns);
            }
            if (!list.Any(x => x.Id == r.Id && x.Where == r.Where))
            {
                list.Add(r);
            }
        }
        var result = new List<string>();
        foreach (string ns in order.Take(MAX_WARNINGS))
        {
            List<Ref> list = groups[ns];
            string sample = string.Join("、", list.Take(5).Select(r => r.Where + " 的" + KindText(r.Kind) + " " + r.Id))
                + (list.Count > 5 ? " 等 " + list.Count + " 处" : "");
            result.Add("命名空间 " + ns + " 既不是 minecraft,也不是这个实例已装模组的 mod id(" + sample
                + ")。多半是拼错了或对应模组没装,游戏会跳过 / 报错这条内容;确认无误(如模组版本自带别名)可以忽略。");
        }
        if (order.Count > MAX_WARNINGS)
        {
            result.Add("另有 " + (order.Count - MAX_WARNINGS) + " 个可疑命名空间未列出");
        }
        return result;
    }

    /// <summary>可疑的命名空间(给确认卡列名字用)。</summary>
    public static List<string> SuspiciousNamespaces(IEnumerable<Ref> refs, ISet<string> modIds, ISet<string> own)
        => refs.Select(r => (r.Kind, Ns: NamespaceOf(r.Id)))
            .Where(x => ContentValidate.IsNamespace(x.Ns) && !Known(x.Kind, x.Ns, modIds, own))
            .Select(x => x.Ns).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>包里已有的命名空间(data/ 或 assets/ 下的子目录名)。</summary>
    public static HashSet<string> ExistingNamespaces(string packRoot, string sub)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            string dir = Path.Combine(packRoot, sub);
            if (Directory.Exists(dir))
            {
                foreach (string d in Directory.EnumerateDirectories(dir))
                {
                    set.Add(Path.GetFileName(d));
                }
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
        }
        return set;
    }
}
