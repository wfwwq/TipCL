// AI2-content:数据包 / 资源包的版本 → pack_format 对照表,以及 1.21 起数据包目录「复数 → 单数」的改名。
// i18n:ignore-file —— 本文件的中文只出现在注释与回给模型的报错里(工具结果是数据,不是界面文案)。
//
// 来源(写表时逐行核对过):Minecraft Wiki「Pack format」页(https://minecraft.wiki/w/Pack_format,
// 「Data pack format history」与「Resource pack format history」两张表的 Releases 列),2026-11-21 取用。
//   · 1.21 那一行写明「Renamed legacy folders like loot_tables and tags/items to loot_table and tags/item and functions to function」;
//   · 自 25w31a(正式版 1.21.9)起 pack.mcmeta 改用 min_format / max_format,不再写 pack_format(格式号带小版本,如 94.1)。
// 规矩:**表里没有的版本一律拒绝**(含不存在的小版本号,如 1.16.6、1.21.12),不按「最近的一档」去猜 ——
// 猜错了游戏只会提示「不兼容」或干脆读不出来,用户还以为是工具写坏了。新版本发布后按同一来源往表里加行即可。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TipCL.Core.Launcher.Ai;

/// <summary>版本 → pack_format 对照,以及按版本决定的目录名 / JSON 字段写法。</summary>
public static class ContentFormats
{
    /// <summary>一档格式:主格式号、小版本号(1.21.9 起才有)、覆盖的正式版列表。</summary>
    public sealed record FormatRow(int Major, int Minor, string[] Releases);

    /// <summary>数据包格式(Wiki「Data pack format history」Releases 列;只收正式版,快照不收)。</summary>
    public static readonly FormatRow[] DataPack =
    {
        new(4, 0, new[] { "1.13", "1.13.1", "1.13.2", "1.14", "1.14.1", "1.14.2", "1.14.3", "1.14.4" }),
        new(5, 0, new[] { "1.15", "1.15.1", "1.15.2", "1.16", "1.16.1" }),
        new(6, 0, new[] { "1.16.2", "1.16.3", "1.16.4", "1.16.5" }),
        new(7, 0, new[] { "1.17", "1.17.1" }),
        new(8, 0, new[] { "1.18", "1.18.1" }),
        new(9, 0, new[] { "1.18.2" }),
        new(10, 0, new[] { "1.19", "1.19.1", "1.19.2", "1.19.3" }),
        new(12, 0, new[] { "1.19.4" }),
        new(15, 0, new[] { "1.20", "1.20.1" }),
        new(18, 0, new[] { "1.20.2" }),
        new(26, 0, new[] { "1.20.3", "1.20.4" }),
        new(41, 0, new[] { "1.20.5", "1.20.6" }),
        new(48, 0, new[] { "1.21", "1.21.1" }),
        new(57, 0, new[] { "1.21.2", "1.21.3" }),
        new(61, 0, new[] { "1.21.4" }),
        new(71, 0, new[] { "1.21.5" }),
        new(80, 0, new[] { "1.21.6" }),
        new(81, 0, new[] { "1.21.7", "1.21.8" }),
        new(88, 0, new[] { "1.21.9", "1.21.10" }),
        new(94, 1, new[] { "1.21.11" }),
        new(101, 1, new[] { "26.1", "26.1.1", "26.1.2" }),
        new(107, 1, new[] { "26.2" }),
        new(121, 0, new[] { "26.3" }),
    };

    /// <summary>资源包格式(Wiki「Resource pack format history」Releases 列;1.13 之前的旧结构本工具不生成,故不收)。</summary>
    public static readonly FormatRow[] ResourcePack =
    {
        new(4, 0, new[] { "1.13", "1.13.1", "1.13.2", "1.14", "1.14.1", "1.14.2", "1.14.3", "1.14.4" }),
        new(5, 0, new[] { "1.15", "1.15.1", "1.15.2", "1.16", "1.16.1" }),
        new(6, 0, new[] { "1.16.2", "1.16.3", "1.16.4", "1.16.5" }),
        new(7, 0, new[] { "1.17", "1.17.1" }),
        new(8, 0, new[] { "1.18", "1.18.1", "1.18.2" }),
        new(9, 0, new[] { "1.19", "1.19.1", "1.19.2" }),
        new(12, 0, new[] { "1.19.3" }),
        new(13, 0, new[] { "1.19.4" }),
        new(15, 0, new[] { "1.20", "1.20.1" }),
        new(18, 0, new[] { "1.20.2" }),
        new(22, 0, new[] { "1.20.3", "1.20.4" }),
        new(32, 0, new[] { "1.20.5", "1.20.6" }),
        new(34, 0, new[] { "1.21", "1.21.1" }),
        new(42, 0, new[] { "1.21.2", "1.21.3" }),
        new(46, 0, new[] { "1.21.4" }),
        new(55, 0, new[] { "1.21.5" }),
        new(63, 0, new[] { "1.21.6" }),
        new(64, 0, new[] { "1.21.7", "1.21.8" }),
        new(69, 0, new[] { "1.21.9", "1.21.10" }),
        new(75, 0, new[] { "1.21.11" }),
        new(84, 0, new[] { "26.1", "26.1.1", "26.1.2" }),
        new(88, 0, new[] { "26.2" }),
        new(97, 1, new[] { "26.3" }),
    };

    /// <summary>把用户 / 实例给的版本号规整成正式版写法(去空白、去掉多余的 ".0" 尾巴);不像版本号返回空串。</summary>
    public static string Normalize(string? mc)
    {
        string v = (mc ?? "").Trim();
        if (!Regex.IsMatch(v, @"^\d+\.\d+(\.\d+)?$"))
        {
            return "";
        }
        string[] parts = v.Split('.');
        if (parts.Length == 3 && parts[2] == "0")
        {
            v = parts[0] + "." + parts[1];
        }
        return v;
    }

    /// <summary>版本号转成可比较的整数序列(Normalize 过的版本)。</summary>
    public static int[] Parse(string mc)
        => Normalize(mc).Split('.', StringSplitOptions.RemoveEmptyEntries).Select(p => int.TryParse(p, out int n) ? n : 0).ToArray();

    /// <summary>a 与 b 比大小(按段比,缺的段当 0)。</summary>
    public static int Compare(string a, string b)
    {
        int[] x = Parse(a);
        int[] y = Parse(b);
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            int xi = i < x.Length ? x[i] : 0;
            int yi = i < y.Length ? y[i] : 0;
            if (xi != yi)
            {
                return xi.CompareTo(yi);
            }
        }
        return 0;
    }

    /// <summary>mc 是否不低于 floor。</summary>
    public static bool AtLeast(string mc, string floor) => Compare(mc, floor) >= 0;

    private static FormatRow? Find(FormatRow[] table, string mc)
    {
        string v = Normalize(mc);
        return v.Length == 0 ? null : table.FirstOrDefault(r => r.Releases.Contains(v, StringComparer.Ordinal));
    }

    /// <summary>数据包格式;表里没有返回 null(调用方据此拒绝,不猜)。</summary>
    public static FormatRow? DataPackFormat(string mc) => Find(DataPack, mc);

    /// <summary>资源包格式;表里没有返回 null。</summary>
    public static FormatRow? ResourcePackFormat(string mc) => Find(ResourcePack, mc);

    /// <summary>1.21.9 起 pack.mcmeta 用 min_format / max_format(Wiki:「Since 25w31a … min_format and max_format」)。</summary>
    public static bool UsesMinMaxFormat(string mc) => AtLeast(mc, "1.21.9");

    /// <summary>1.21 起数据包的目录名改单数(recipes → recipe、tags/items → tags/item …)。</summary>
    public static bool SingularDirs(string mc) => AtLeast(mc, "1.21");

    /// <summary>1.20.5 起配方产物 / 进度图标的物品字段由 item 改为 id(物品堆叠组件化)。</summary>
    public static bool ItemStackUsesId(string mc) => AtLeast(mc, "1.20.5");

    /// <summary>1.21.2 起配方原料写成字符串(物品 ID 或 #标签)或字符串数组,不再是 {"item": …} / {"tag": …}。</summary>
    public static bool StringIngredients(string mc) => AtLeast(mc, "1.21.2");

    /// <summary>1.20.2 起函数文件支持宏行($ 开头)。</summary>
    public static bool FunctionMacros(string mc) => AtLeast(mc, "1.20.2");

    /// <summary>26.3 起烧炼类配方必须写 cookingtime(Wiki 26.3 行:「Changed cooking recipes to require cookingtime」)。</summary>
    public static bool CookingTimeRequired(string mc) => AtLeast(mc, "26.3");

    /// <summary>数据包里本工具支持的文件类型(对外统一用单数写法)。</summary>
    public static readonly string[] DataTypes = { "recipe", "loot_table", "advancement", "function", "tag" };

    /// <summary>标签支持的注册表类别(单数写法)。</summary>
    public static readonly string[] TagTypes = { "block", "item", "entity_type", "fluid", "function", "game_event" };

    private static readonly Dictionary<string, string> Plural = new(StringComparer.Ordinal)
    {
        ["recipe"] = "recipes",
        ["loot_table"] = "loot_tables",
        ["advancement"] = "advancements",
        ["function"] = "functions",
        ["block"] = "blocks",
        ["item"] = "items",
        ["entity_type"] = "entity_types",
        ["fluid"] = "fluids",
        ["game_event"] = "game_events",
    };

    /// <summary>把复数写法(recipes、items、tags/items 里的 items)规整成单数;认不出原样返回。</summary>
    public static string Singular(string kind)
    {
        string k = (kind ?? "").Trim().ToLowerInvariant();
        foreach (KeyValuePair<string, string> kv in Plural)
        {
            if (kv.Value == k)
            {
                return kv.Key;
            }
        }
        return k;
    }

    /// <summary>某类文件在该版本下的目录名(1.21 前复数,1.21 起单数)。</summary>
    public static string DataDir(string type, string mc)
    {
        string t = Singular(type);
        return SingularDirs(mc) || !Plural.TryGetValue(t, out string? plural) ? t : plural;
    }

    /// <summary>标签目录:tags/&lt;类别&gt;(类别同样按版本单复数)。</summary>
    public static string TagDir(string tagType, string mc) => "tags/" + DataDir(tagType, mc);

    /// <summary>pack.mcmeta 的内容(按版本选 pack_format 或 min_format / max_format)。</summary>
    public static PyDict PackMeta(FormatRow row, string mc, string description)
    {
        PyDict pack = UsesMinMaxFormat(mc)
            ? PyDict.Of("description", description, "min_format", (long)row.Major, "max_format", (long)row.Major)
            : PyDict.Of("description", description, "pack_format", (long)row.Major);
        return PyDict.Of("pack", pack);
    }

    /// <summary>给人看的格式号(带小版本时写成 94.1)。</summary>
    public static string FormatText(FormatRow row) => row.Minor > 0 ? row.Major + "." + row.Minor : row.Major.ToString();

    /// <summary>表里收录的全部版本(报错时给模型看)。</summary>
    public static string KnownRange(FormatRow[] table) => table.First().Releases.First() + " ~ " + table.Last().Releases.Last();
}
