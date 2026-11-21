// AI2-content:数据包 / 资源包 / KubeJS 脚本的内容校验(纯函数,不碰磁盘;贴图校验只看传进来的字节)。
// i18n:ignore-file —— 本文件的中文只出现在回给模型的校验报错里(工具结果是数据,不是界面文案)。
//
// 口径:只做「游戏一定会拒、或明显写错」的结构检查(必填字段、resource location 写法、函数行首斜杠、PNG 尺寸 …),
// 不追求完整复刻游戏的解析器 —— 模组可以注册自定义配方 / 战利品类型,认不出的类型只查 type 写法并在结果里说明。
// 写不对的东西一律拒写并说清哪一行 / 哪个字段,不悄悄修正(悄悄改了模型就学不会正确写法,下次还错)。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TipCL.Core.Launcher.Ai;

/// <summary>内容校验(数据包 JSON / 函数 / 资源包 JSON / PNG / KubeJS 静态检查)。</summary>
public static class ContentValidate
{
    // ───────────────────────────── 名字 / resource location ─────────────────────────────

    private static readonly Regex NamespaceRx = new(@"^[a-z0-9_.-]{1,64}$", RegexOptions.CultureInvariant);

    private static readonly Regex PathSegRx = new(@"^[a-z0-9_.-]+$", RegexOptions.CultureInvariant);

    private static readonly Regex RlRx = new(@"^(?:[a-z0-9_.-]+:)?[a-z0-9_.\-/]+$", RegexOptions.CultureInvariant);

    /// <summary>包文件夹名 / 脚本文件名:字母数字与 _ - .,不以点开头,最长 64。</summary>
    private static readonly Regex PackNameRx = new(@"^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant);

    /// <summary>Windows 设备名(con / nul / com1 …):在 Windows 上建不出来、或者建出来删不掉。</summary>
    private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "con", "prn", "aux", "nul", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    /// <summary>命名空间:小写字母数字与 _ . -(游戏规则)。</summary>
    public static bool IsNamespace(string? ns) => NamespaceRx.IsMatch(ns ?? "");

    /// <summary>
    /// resource location:`namespace:path` 或省略命名空间的 `path`(游戏按 minecraft: 处理),一律小写,
    /// path 只能有 a-z 0-9 _ . - /;冒号最多一个;不许空段、不许 . / .. 段。
    /// </summary>
    public static bool IsResourceLocation(string? rl)
    {
        string s = rl ?? "";
        if (!RlRx.IsMatch(s))
        {
            return false;
        }
        string path = s.Contains(':') ? s.Substring(s.IndexOf(':') + 1) : s;
        if (path.Length == 0)
        {
            return false;
        }
        return path.Split('/').All(seg => seg.Length > 0 && seg != "." && seg != "..");
    }

    /// <summary>标签引用:#namespace:path。</summary>
    public static bool IsTagRef(string? s) => (s ?? "").StartsWith("#", StringComparison.Ordinal) && IsResourceLocation(s!.Substring(1));

    /// <summary>
    /// 包内文件路径(不带扩展名,如 diamond_from_dirt 或 tools/pick):逐段小写字母数字与 _ . -,
    /// 不许 ..、空段、绝对路径、反斜杠,最多 8 级、128 字。不合法抛 ValueError。
    /// </summary>
    public static string CheckResourcePath(string? raw, string what = "path")
    {
        string p = (raw ?? "").Trim();
        if (p.Length == 0)
        {
            throw new ValueError(what + " 不能为空");
        }
        if (p.Length > 128)
        {
            throw new ValueError(what + " 太长(最多 128 字)");
        }
        if (p.Contains('\\') || p.StartsWith("/", StringComparison.Ordinal) || p.Contains(':') || p.Contains('\0'))
        {
            throw new PermissionError(what + " 只能是包内的相对路径(用 / 分级,不能带盘符、反斜杠或开头的 /): " + p);
        }
        string[] segs = p.Split('/');
        if (segs.Any(s => s == ".." || s == "."))
        {
            throw new PermissionError(what + " 里不能有 . 或 .. : " + p);
        }
        if (segs.Length > 8)
        {
            throw new ValueError(what + " 层级太深(最多 8 级): " + p);
        }
        foreach (string s in segs)
        {
            if (!PathSegRx.IsMatch(s))
            {
                throw new ValueError(what + " 只能用小写字母、数字和 _ . -(游戏要求资源路径全小写): " + p);
            }
            if (s.EndsWith(".", StringComparison.Ordinal))
            {
                throw new ValueError(what + " 的每一级不能以点结尾: " + p);
            }
        }
        return p;
    }

    /// <summary>包文件夹名 / 脚本文件名 / 存档名里不许出现的东西(路径分隔、..、设备名 …);不合法抛异常。</summary>
    public static string CheckFolderName(string? raw, string what)
    {
        string n = (raw ?? "").Trim();
        if (n.Length == 0)
        {
            throw new ValueError(what + " 不能为空");
        }
        if (n.Contains('/') || n.Contains('\\') || n == "." || n == ".." || n.Contains(':') || n.Contains('\0'))
        {
            throw new PermissionError(what + " 只能是一个文件夹名,不能带路径: " + n);
        }
        if (!PackNameRx.IsMatch(n) || n.EndsWith(".", StringComparison.Ordinal))
        {
            throw new ValueError(what + " 只能用字母、数字和 _ - .(不以点开头或结尾,最长 64): " + n);
        }
        if (DeviceNames.Contains(n.Split('.')[0]))
        {
            throw new ValueError(what + " 不能用系统保留名: " + n);
        }
        return n;
    }

    /// <summary>
    /// 存档文件夹名:存档名常带空格、中文,所以比包名宽松 —— 只拒路径分隔、..、冒号、控制字符和设备名;
    /// 是否真的存在由调用方再查。
    /// </summary>
    public static string CheckWorldName(string? raw)
    {
        string n = (raw ?? "").Trim();
        if (n.Length == 0)
        {
            throw new ValueError("world 不能为空(存档文件夹名,可先用 list_saves 查)");
        }
        if (n.Length > 128 || n.Contains('/') || n.Contains('\\') || n == "." || n == ".." || n.Contains(':')
            || n.Any(char.IsControl) || n.EndsWith(".", StringComparison.Ordinal) || n.EndsWith(" ", StringComparison.Ordinal))
        {
            throw new PermissionError("world 只能是 saves 下的一个存档文件夹名,不能带路径: " + n);
        }
        if (DeviceNames.Contains(n.Split('.')[0]))
        {
            throw new ValueError("world 不能用系统保留名: " + n);
        }
        return n;
    }

    // ───────────────────────────── JSON 小工具 ─────────────────────────────

    /// <summary>把参数里的 content 变成 JSON 值:字符串就解析,对象 / 数组原样用。解析失败抛 ValueError(带位置)。</summary>
    public static object ParseJson(object? content, string where)
    {
        if (content is PyDict || content is List<object>)
        {
            return content;
        }
        string text = Py.S(content);
        if (text.Trim().Length == 0)
        {
            throw new ValueError(where + ":content 不能为空");
        }
        try
        {
            return PyJson.Loads(text.Trim()) ?? throw new ValueError(where + ":JSON 不能是 null");
        }
        catch (ValueError)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ValueError(where + ":不是合法 JSON —— " + Py.ExcStr(ex));
        }
    }

    private static bool IsNumber(object? o) => o is long || o is int || o is double || o is float;

    private static bool IsInt(object? o) => o is long || o is int;

    /// <summary>文本组件(进度标题 / 描述):字符串、对象或数组都算。</summary>
    private static bool IsTextComponent(object? o) => o is string || o is PyDict || o is List<object>;

    private sealed class Errs
    {
        public readonly List<string> List = new();

        public void Add(string field, string msg) => List.Add(field + ":" + msg);

        public void Rl(PyDict obj, string key, string field, bool required)
        {
            object? v = obj.Get(key);
            if (v == null)
            {
                if (required)
                {
                    Add(field, "缺少必填字段");
                }
                return;
            }
            if (v is not string s || !IsResourceLocation(s))
            {
                Add(field, "应为小写的 resource location(namespace:path),实际是 " + Short(v));
            }
        }
    }

    private static string Short(object? v)
    {
        string s = v is string str ? "\"" + str + "\"" : PyJson.Dumps(v, ensureAscii: false);
        return s.Length > 60 ? s.Substring(0, 60) + "…" : s;
    }

    private static string NormType(string type) => type.Contains(':') ? type : "minecraft:" + type;

    private static void Throw(Errs e, string where)
    {
        if (e.List.Count > 0)
        {
            throw new ValueError(where + " 校验没通过:\n- " + string.Join("\n- ", e.List.Take(20))
                + (e.List.Count > 20 ? "\n- …还有 " + (e.List.Count - 20) + " 处" : ""));
        }
    }

    // ───────────────────────────── 数据包:配方 ─────────────────────────────

    /// <summary>配方原料(按版本:1.21.2 起是字符串 / 字符串数组,之前是 {item}/{tag} 对象或其数组;带 type 的自定义原料放过)。</summary>
    private static void Ingredient(Errs e, object? v, string field, string mc)
    {
        bool strings = ContentFormats.StringIngredients(mc);
        if (v == null)
        {
            e.Add(field, "缺少必填字段");
            return;
        }
        if (v is List<object> list)
        {
            if (list.Count == 0)
            {
                e.Add(field, "原料数组不能为空");
            }
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is List<object>)
                {
                    e.Add(field + "[" + i + "]", "原料数组里不能再套数组");
                    continue;
                }
                Ingredient(e, list[i], field + "[" + i + "]", mc);
            }
            return;
        }
        if (v is string s)
        {
            if (!strings)
            {
                e.Add(field, "MC " + mc + " 的原料要写成 {\"item\": \"minecraft:stone\"} 或 {\"tag\": \"minecraft:logs\"}(1.21.2 起才是字符串写法)");
                return;
            }
            if (!(IsResourceLocation(s) || IsTagRef(s)))
            {
                e.Add(field, "应为物品 ID(如 minecraft:stone)或 #标签(如 #minecraft:logs),实际是 " + Short(s));
            }
            return;
        }
        if (v is PyDict d)
        {
            if (d.Get("type") is string customType)
            {
                // 模组加载器的自定义原料(如 neoforge:components / forge:nbt):只查 type 写法
                if (!IsResourceLocation(customType))
                {
                    e.Add(field + ".type", "自定义原料的 type 应为小写 resource location");
                }
                return;
            }
            if (strings)
            {
                e.Add(field, "MC " + mc + " 的原料要写成字符串:\"minecraft:stone\" 或 \"#minecraft:logs\"(不再是 {\"item\": …} 对象)");
                return;
            }
            bool hasItem = d.ContainsKey("item");
            bool hasTag = d.ContainsKey("tag");
            if (hasItem == hasTag)
            {
                e.Add(field, "原料对象要有且只有 item 或 tag 其中一个");
                return;
            }
            e.Rl(d, hasItem ? "item" : "tag", field + "." + (hasItem ? "item" : "tag"), true);
            return;
        }
        e.Add(field, "原料写法不对:" + Short(v));
    }

    /// <summary>配方产物:1.20.5 起 {"id", "count"?, "components"?};之前合成类是 {"item", "count"?},烧炼 / 切石是物品 ID 字符串。</summary>
    private static void Result(Errs e, object? v, string mc, bool legacyString)
    {
        if (v == null)
        {
            e.Add("result", "缺少必填字段");
            return;
        }
        if (ContentFormats.ItemStackUsesId(mc))
        {
            if (v is not PyDict d)
            {
                e.Add("result", "MC " + mc + " 的产物要写成 {\"id\": \"minecraft:xxx\", \"count\": 1}");
                return;
            }
            if (d.ContainsKey("item") && !d.ContainsKey("id"))
            {
                e.Add("result", "MC " + mc + " 起产物字段叫 id,不是 item");
                return;
            }
            e.Rl(d, "id", "result.id", true);
            Count(e, d.Get("count"), "result.count");
            return;
        }
        if (legacyString)
        {
            if (v is not string s || !IsResourceLocation(s))
            {
                e.Add("result", "MC " + mc + " 的这类配方产物写成物品 ID 字符串(如 \"minecraft:iron_ingot\"),实际是 " + Short(v));
            }
            return;
        }
        if (v is not PyDict r)
        {
            e.Add("result", "MC " + mc + " 的产物要写成 {\"item\": \"minecraft:xxx\", \"count\": 1}");
            return;
        }
        if (r.ContainsKey("id") && !r.ContainsKey("item"))
        {
            e.Add("result", "MC " + mc + " 的产物字段叫 item(1.20.5 起才改成 id)");
            return;
        }
        e.Rl(r, "item", "result.item", true);
        Count(e, r.Get("count"), "result.count");
    }

    private static void Count(Errs e, object? count, string field)
    {
        if (count != null && (!IsInt(count) || Py.Int(count) < 1 || Py.Int(count) > 99))
        {
            e.Add(field, "应为 1~99 的整数");
        }
    }

    /// <summary>
    /// 配方 JSON 结构校验。返回给模型看的备注(如「这个类型只做了基础检查」);不合格抛 ValueError。
    /// </summary>
    public static string Recipe(object json, string mc, string where = "recipe")
    {
        var e = new Errs();
        if (json is not PyDict d)
        {
            throw new ValueError(where + ":配方应为 JSON 对象");
        }
        e.Rl(d, "type", "type", true);
        Throw(e, where);
        string type = NormType(Py.S(d.Get("type")));
        string note = "";
        switch (type)
        {
            case "minecraft:crafting_shaped":
                ShapedPattern(e, d, mc);
                Result(e, d.Get("result"), mc, legacyString: false);
                break;
            case "minecraft:crafting_shapeless":
                if (d.Get("ingredients") is not List<object> { Count: > 0 } ings)
                {
                    e.Add("ingredients", "无序合成要有非空的 ingredients 数组");
                }
                else
                {
                    if (ings.Count > 9)
                    {
                        e.Add("ingredients", "最多 9 个原料");
                    }
                    for (int i = 0; i < ings.Count; i++)
                    {
                        Ingredient(e, ings[i], "ingredients[" + i + "]", mc);
                    }
                }
                Result(e, d.Get("result"), mc, legacyString: false);
                break;
            case "minecraft:smelting":
            case "minecraft:blasting":
            case "minecraft:smoking":
            case "minecraft:campfire_cooking":
                Ingredient(e, d.Get("ingredient"), "ingredient", mc);
                Result(e, d.Get("result"), mc, legacyString: true);
                if (d.Get("experience") is { } xp && !IsNumber(xp))
                {
                    e.Add("experience", "应为数字");
                }
                if (d.Get("cookingtime") is { } ct)
                {
                    if (!IsInt(ct) || Py.Int(ct) <= 0)
                    {
                        e.Add("cookingtime", "应为正整数(tick)");
                    }
                }
                else if (ContentFormats.CookingTimeRequired(mc))
                {
                    e.Add("cookingtime", "MC " + mc + " 起烧炼类配方必须写 cookingtime");
                }
                break;
            case "minecraft:stonecutting":
                Ingredient(e, d.Get("ingredient"), "ingredient", mc);
                Result(e, d.Get("result"), mc, legacyString: true);
                if (!ContentFormats.ItemStackUsesId(mc))
                {
                    Count(e, d.Get("count"), "count");
                }
                break;
            case "minecraft:smithing_transform":
                Ingredient(e, d.Get("base"), "base", mc);
                foreach (string k in new[] { "template", "addition" })
                {
                    if (d.ContainsKey(k))
                    {
                        Ingredient(e, d.Get(k), k, mc);
                    }
                }
                Result(e, d.Get("result"), mc, legacyString: false);
                break;
            default:
                note = "配方类型 " + type + " 不在内置校验范围(可能是模组或特殊配方),只检查了 type 写法";
                break;
        }
        foreach (string k in new[] { "group", "category" })
        {
            if (d.Get(k) is { } g && g is not string)
            {
                e.Add(k, "应为字符串");
            }
        }
        Throw(e, where);
        return note;
    }

    private static void ShapedPattern(Errs e, PyDict d, string mc)
    {
        if (d.Get("pattern") is not List<object> { Count: > 0 } rows)
        {
            e.Add("pattern", "有序合成要有非空的 pattern 数组(1~3 行字符串)");
            return;
        }
        if (rows.Count > 3)
        {
            e.Add("pattern", "最多 3 行");
        }
        var used = new HashSet<char>();
        int width = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] is not string row || row.Length == 0 || row.Length > 3)
            {
                e.Add("pattern[" + i + "]", "每行是 1~3 个字符的字符串");
                continue;
            }
            if (width >= 0 && row.Length != width)
            {
                e.Add("pattern[" + i + "]", "每行长度必须相同(空位用空格占位)");
            }
            width = row.Length;
            foreach (char c in row.Where(c => c != ' '))
            {
                used.Add(c);
            }
        }
        if (d.Get("key") is not PyDict key)
        {
            e.Add("key", "有序合成要有 key 对象(符号 → 原料)");
            return;
        }
        foreach (KeyValuePair<string, object?> kv in key)
        {
            if (kv.Key.Length != 1 || kv.Key == " ")
            {
                e.Add("key." + kv.Key, "key 的每个键是单个非空格字符");
                continue;
            }
            if (!used.Contains(kv.Key[0]))
            {
                e.Add("key." + kv.Key, "pattern 里没用到这个符号(游戏会拒绝整份配方)");
            }
            Ingredient(e, kv.Value, "key." + kv.Key, mc);
        }
        foreach (char c in used)
        {
            if (!key.ContainsKey(c.ToString()))
            {
                e.Add("pattern", "符号 '" + c + "' 在 key 里没有定义");
            }
        }
    }

    // ───────────────────────────── 数据包:战利品表 / 进度 / 标签 / 函数 ─────────────────────────────

    /// <summary>战利品表:根是对象;pools 每项要有 rolls 与 entries,条目要有 type,item / tag 条目要有 name。</summary>
    public static string LootTable(object json, string mc, string where = "loot_table")
    {
        var e = new Errs();
        if (json is not PyDict d)
        {
            throw new ValueError(where + ":战利品表应为 JSON 对象");
        }
        e.Rl(d, "type", "type", false);
        object? poolsRaw = d.Get("pools");
        if (poolsRaw != null && poolsRaw is not List<object>)
        {
            e.Add("pools", "应为数组");
        }
        List<object> pools = Py.ListOr(poolsRaw);
        for (int i = 0; i < pools.Count; i++)
        {
            string f = "pools[" + i + "]";
            if (pools[i] is not PyDict pool)
            {
                e.Add(f, "应为对象");
                continue;
            }
            object? rolls = pool.Get("rolls");
            if (rolls == null)
            {
                e.Add(f + ".rolls", "缺少必填字段");
            }
            else if (!IsNumber(rolls) && rolls is not PyDict)
            {
                e.Add(f + ".rolls", "应为数字或数值提供器对象");
            }
            if (pool.Get("entries") is not List<object> entries)
            {
                e.Add(f + ".entries", "缺少 entries 数组");
                continue;
            }
            for (int j = 0; j < entries.Count; j++)
            {
                string ef = f + ".entries[" + j + "]";
                if (entries[j] is not PyDict entry)
                {
                    e.Add(ef, "应为对象");
                    continue;
                }
                e.Rl(entry, "type", ef + ".type", true);
                string et = NormType(Py.S(entry.Get("type")));
                if (et is "minecraft:item" or "minecraft:tag")
                {
                    e.Rl(entry, "name", ef + ".name", true);
                }
            }
        }
        Throw(e, where);
        return "";
    }

    /// <summary>进度:criteria 必填且非空(每条要有 trigger);parent 可选;display 有的话 icon / title / description 必填。</summary>
    public static string Advancement(object json, string mc, string where = "advancement")
    {
        var e = new Errs();
        if (json is not PyDict d)
        {
            throw new ValueError(where + ":进度应为 JSON 对象");
        }
        e.Rl(d, "parent", "parent", false);
        if (d.Get("criteria") is not PyDict { Count: > 0 } criteria)
        {
            e.Add("criteria", "缺少非空的 criteria 对象(至少一个触发条件)");
        }
        else
        {
            foreach (KeyValuePair<string, object?> kv in criteria)
            {
                if (kv.Value is not PyDict c)
                {
                    e.Add("criteria." + kv.Key, "应为对象");
                    continue;
                }
                e.Rl(c, "trigger", "criteria." + kv.Key + ".trigger", true);
            }
            if (d.Get("requirements") is { } req)
            {
                if (req is not List<object> groups || groups.Any(g => g is not List<object>))
                {
                    e.Add("requirements", "应为「字符串数组」的数组");
                }
                else
                {
                    foreach (string name in groups.SelectMany(g => (List<object>)g!).Select(Py.S))
                    {
                        if (!criteria.ContainsKey(name))
                        {
                            e.Add("requirements", "引用了不存在的条件 " + name);
                        }
                    }
                }
            }
        }
        if (d.Get("display") is { } displayRaw)
        {
            if (displayRaw is not PyDict display)
            {
                e.Add("display", "应为对象");
            }
            else
            {
                string itemKey = ContentFormats.ItemStackUsesId(mc) ? "id" : "item";
                if (display.Get("icon") is not PyDict icon)
                {
                    e.Add("display.icon", "缺少 icon 对象(如 {\"" + itemKey + "\": \"minecraft:diamond\"})");
                }
                else
                {
                    e.Rl(icon, itemKey, "display.icon." + itemKey, true);
                }
                foreach (string k in new[] { "title", "description" })
                {
                    if (!IsTextComponent(display.Get(k)))
                    {
                        e.Add("display." + k, "缺少文本(字符串或文本组件)");
                    }
                }
                if (display.Get("frame") is { } frame && !(frame is string fs && fs is "task" or "goal" or "challenge"))
                {
                    e.Add("display.frame", "只能是 task / goal / challenge");
                }
            }
        }
        Throw(e, where);
        return "";
    }

    /// <summary>标签:values 必填数组;每项是 ID / #标签 字符串,或 {"id": …, "required": bool};replace 可选布尔。</summary>
    public static string Tag(object json, string mc, string where = "tag")
    {
        var e = new Errs();
        if (json is not PyDict d)
        {
            throw new ValueError(where + ":标签应为 JSON 对象");
        }
        if (d.Get("replace") is { } rep && rep is not bool)
        {
            e.Add("replace", "应为 true / false");
        }
        if (d.Get("values") is not List<object> values)
        {
            e.Add("values", "缺少 values 数组");
            Throw(e, where);
            return "";
        }
        for (int i = 0; i < values.Count; i++)
        {
            string f = "values[" + i + "]";
            object? v = values[i];
            string id = v is PyDict o ? Py.S(o.Get("id")) : v as string ?? "";
            if (v is PyDict obj && obj.Get("required") is { } r && r is not bool)
            {
                e.Add(f + ".required", "应为 true / false");
            }
            if (!(IsResourceLocation(id) || IsTagRef(id)))
            {
                e.Add(f, "应为 ID(如 minecraft:stone)或 #标签,实际是 " + Short(v));
            }
        }
        Throw(e, where);
        return "";
    }

    private static readonly Regex CommandHeadRx = new(@"^[a-z][a-z0-9_:.\-]*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// 函数文件(.mcfunction)基本语法:每行一条命令、不以 / 开头、命令名小写;# 开头是注释;
    /// $ 开头的宏行只有 1.20.2 起才认。不合格抛 ValueError(列出行号)。返回规整后的文本(统一 LF、末尾一个换行)。
    /// </summary>
    public static string Function(object? content, string mc, string where = "function")
    {
        if (content is PyDict || content is List<object>)
        {
            throw new ValueError(where + ":函数文件的 content 是命令文本(每行一条),不是 JSON");
        }
        string text = Py.S(content).Replace("\r\n", "\n").Replace("\r", "\n");
        if (text.Trim().Length == 0)
        {
            throw new ValueError(where + ":函数文件不能为空");
        }
        if (text.Contains('\0'))
        {
            throw new ValueError(where + ":内容里有空字节");
        }
        var errs = new List<string>();
        string[] lines = text.Split('\n');
        bool macros = ContentFormats.FunctionMacros(mc);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }
            int no = i + 1;
            if (line.StartsWith("/", StringComparison.Ordinal))
            {
                errs.Add("第 " + no + " 行以 / 开头:函数文件里的命令不要带斜杠 —— " + Clip(line));
                continue;
            }
            if (line.StartsWith("$", StringComparison.Ordinal))
            {
                if (!macros)
                {
                    errs.Add("第 " + no + " 行是宏行($ 开头),MC " + mc + " 不支持(1.20.2 起才有)");
                }
                continue;
            }
            string head = line.Split(' ', '\t')[0];
            if (!CommandHeadRx.IsMatch(head))
            {
                errs.Add("第 " + no + " 行命令名应为小写字母开头的单词,实际是 \"" + Clip(head) + "\"");
            }
        }
        if (errs.Count > 0)
        {
            throw new ValueError(where + " 校验没通过:\n- " + string.Join("\n- ", errs.Take(20))
                + (errs.Count > 20 ? "\n- …还有 " + (errs.Count - 20) + " 处" : ""));
        }
        return text.TrimEnd('\n') + "\n";
    }

    private static string Clip(string s) => s.Length > 80 ? s.Substring(0, 80) + "…" : s;

    // ───────────────────────────── 资源包:语言 / 模型 / 方块状态 ─────────────────────────────

    private static readonly Regex LangCodeRx = new(@"^[a-z]{2,3}(_[a-z]{2,4})?$", RegexOptions.CultureInvariant);

    /// <summary>语言代码(zh_cn、en_us、lzh …),全小写。</summary>
    public static bool IsLangCode(string? code) => LangCodeRx.IsMatch(code ?? "");

    /// <summary>语言文件:扁平对象,键非空字符串,值都是字符串。</summary>
    public static string Lang(object json, string where = "lang")
    {
        var e = new Errs();
        if (json is not PyDict d)
        {
            throw new ValueError(where + ":语言文件应为 {\"翻译键\": \"文本\"} 形式的 JSON 对象");
        }
        foreach (KeyValuePair<string, object?> kv in d)
        {
            if (kv.Key.Trim().Length == 0)
            {
                e.Add("(空键)", "翻译键不能为空");
            }
            if (kv.Value is not string)
            {
                e.Add(kv.Key, "值应为字符串,实际是 " + Short(kv.Value));
            }
        }
        Throw(e, where);
        return "";
    }

    private static readonly string[] Faces = { "down", "up", "north", "south", "west", "east" };

    private static bool IsTextureRef(string? s) => (s ?? "").StartsWith("#", StringComparison.Ordinal) ? (s!.Length > 1) : IsResourceLocation(s);

    /// <summary>方块 / 物品模型:至少有 parent / textures / elements 之一;贴图引用是 #变量或 resource location;元素坐标在 -16~32。</summary>
    public static string Model(object json, string where = "model")
    {
        var e = new Errs();
        if (json is not PyDict d)
        {
            throw new ValueError(where + ":模型应为 JSON 对象");
        }
        if (!d.ContainsKey("parent") && !d.ContainsKey("textures") && !d.ContainsKey("elements"))
        {
            e.Add("(根)", "至少要有 parent、textures、elements 其中之一");
        }
        e.Rl(d, "parent", "parent", false);
        if (d.Get("textures") is { } texRaw)
        {
            if (texRaw is not PyDict tex)
            {
                e.Add("textures", "应为对象(变量名 → 贴图)");
            }
            else
            {
                foreach (KeyValuePair<string, object?> kv in tex)
                {
                    if (kv.Value is not string s || !IsTextureRef(s))
                    {
                        e.Add("textures." + kv.Key, "应为贴图 ID(如 minecraft:block/stone)或 #变量,实际是 " + Short(kv.Value));
                    }
                }
            }
        }
        if (d.Get("elements") is { } elRaw)
        {
            if (elRaw is not List<object> els)
            {
                e.Add("elements", "应为数组");
            }
            else
            {
                for (int i = 0; i < els.Count; i++)
                {
                    string f = "elements[" + i + "]";
                    if (els[i] is not PyDict el)
                    {
                        e.Add(f, "应为对象");
                        continue;
                    }
                    foreach (string k in new[] { "from", "to" })
                    {
                        if (el.Get(k) is not List<object> { Count: 3 } xyz || xyz.Any(n => !IsNumber(n) || Convert.ToDouble(n) < -16 || Convert.ToDouble(n) > 32))
                        {
                            e.Add(f + "." + k, "应为 3 个 -16~32 之间的数字");
                        }
                    }
                    if (el.Get("faces") is { } facesRaw)
                    {
                        if (facesRaw is not PyDict faces)
                        {
                            e.Add(f + ".faces", "应为对象");
                            continue;
                        }
                        foreach (KeyValuePair<string, object?> kv in faces)
                        {
                            if (!Faces.Contains(kv.Key))
                            {
                                e.Add(f + ".faces." + kv.Key, "面只能是 " + string.Join("/", Faces));
                            }
                            else if (kv.Value is not PyDict face || face.Get("texture") is not string t || !IsTextureRef(t))
                            {
                                e.Add(f + ".faces." + kv.Key + ".texture", "缺少贴图(#变量或贴图 ID)");
                            }
                        }
                    }
                }
            }
        }
        Throw(e, where);
        return "";
    }

    private static void ModelRef(Errs e, object? v, string field)
    {
        if (v is List<object> list)
        {
            if (list.Count == 0)
            {
                e.Add(field, "不能是空数组");
            }
            for (int i = 0; i < list.Count; i++)
            {
                ModelRef(e, list[i], field + "[" + i + "]");
            }
            return;
        }
        if (v is not PyDict m)
        {
            e.Add(field, "应为 {\"model\": \"namespace:block/xxx\"} 或它的数组");
            return;
        }
        e.Rl(m, "model", field + ".model", true);
        foreach (string k in new[] { "x", "y" })
        {
            if (m.Get(k) is { } r && (!IsInt(r) || Py.Int(r) % 90 != 0))
            {
                e.Add(field + "." + k, "旋转只能是 0/90/180/270");
            }
        }
    }

    /// <summary>方块状态:variants(对象)与 multipart(数组)二选一,每个变体指向一个模型。</summary>
    public static string Blockstate(object json, string where = "blockstate")
    {
        var e = new Errs();
        if (json is not PyDict d)
        {
            throw new ValueError(where + ":方块状态应为 JSON 对象");
        }
        bool hasVariants = d.ContainsKey("variants");
        bool hasMultipart = d.ContainsKey("multipart");
        if (hasVariants == hasMultipart)
        {
            e.Add("(根)", "variants 与 multipart 要有且只有一个");
        }
        else if (hasVariants)
        {
            if (d.Get("variants") is not PyDict { Count: > 0 } variants)
            {
                e.Add("variants", "应为非空对象(如 {\"\": {\"model\": \"ns:block/x\"}})");
            }
            else
            {
                foreach (KeyValuePair<string, object?> kv in variants)
                {
                    ModelRef(e, kv.Value, "variants[\"" + kv.Key + "\"]");
                }
            }
        }
        else if (d.Get("multipart") is not List<object> { Count: > 0 } parts)
        {
            e.Add("multipart", "应为非空数组");
        }
        else
        {
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] is not PyDict part)
                {
                    e.Add("multipart[" + i + "]", "应为对象");
                    continue;
                }
                ModelRef(e, part.Get("apply"), "multipart[" + i + "].apply");
                if (part.Get("when") is { } when && when is not PyDict)
                {
                    e.Add("multipart[" + i + "].when", "应为对象");
                }
            }
        }
        Throw(e, where);
        return "";
    }

    // ───────────────────────────── PNG 贴图 ─────────────────────────────

    /// <summary>贴图最大 4 MB。</summary>
    public const int MAX_PNG_BYTES = 4 * 1024 * 1024;

    /// <summary>贴图边长上限(再大的材质包也用不到)。</summary>
    public const int MAX_PNG_SIDE = 8192;

    private static readonly byte[] PngSig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static bool PowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

    /// <summary>
    /// 校验 PNG:文件头签名 + 第一个块是 13 字节的 IHDR;宽高都要是 2 的幂或 16 的倍数(动画贴图是竖长条,
    /// 高是宽的整数倍,一样满足)。返回 (宽, 高);不合格抛 ValueError。
    /// </summary>
    public static (int width, int height) Png(byte[] data, string where = "texture")
    {
        if (data == null || data.Length < 33)
        {
            throw new ValueError(where + ":文件太小,不是 PNG");
        }
        if (data.Length > MAX_PNG_BYTES)
        {
            throw new ValueError(where + ":贴图太大(上限 4 MB)");
        }
        if (!data.Take(8).SequenceEqual(PngSig))
        {
            throw new ValueError(where + ":不是 PNG 文件(文件头签名不对;扩展名改成 .png 没用)");
        }
        int len = (data[8] << 24) | (data[9] << 16) | (data[10] << 8) | data[11];
        string type = Encoding.ASCII.GetString(data, 12, 4);
        if (len != 13 || type != "IHDR")
        {
            throw new ValueError(where + ":PNG 结构不对(第一个块应为 IHDR)");
        }
        int w = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
        int h = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
        if (w <= 0 || h <= 0 || w > MAX_PNG_SIDE || h > MAX_PNG_SIDE)
        {
            throw new ValueError(where + ":PNG 尺寸异常(" + w + "×" + h + ")");
        }
        foreach ((string side, int n) in new[] { ("宽", w), ("高", h) })
        {
            if (!PowerOfTwo(n) && n % 16 != 0)
            {
                throw new ValueError(where + ":贴图" + side + " " + n + " 像素既不是 2 的幂也不是 16 的倍数(" + w + "×" + h + "),游戏里会错位或被拒");
            }
        }
        return (w, h);
    }

    // ───────────────────────────── KubeJS 静态检查 ─────────────────────────────

    /// <summary>
    /// KubeJS 脚本里禁止出现的用法(规则名 → 正则)。脚本跑在游戏进程里,有完整的 Java 权限;
    /// 这里拦的是「能碰文件系统 / 网络 / 起进程 / 反射绕过」的那几类入口,以及能把这些拼出来的动态求值。
    /// 检查对原文和「把 \uXXXX 转义还原后」的文本各跑一遍(防 \u0065val 这种写法);注释和字符串里出现也算 ——
    /// 宁可误拦一句注释,也不去做容易被绕过的语法分析。
    /// </summary>
    public static readonly (string Rule, string Pattern)[] KubeJsDenied =
    {
        ("require(...) 加载模块", @"\brequire\s*\("),
        ("eval 动态求值", @"\beval\b"),
        ("Function 构造器动态求值", @"\bFunction\s*\("),
        ("importPackage / importClass / JavaImporter", @"\b(?:importPackage|importClass|JavaImporter)\b"),
        ("JavaAdapter", @"\bJavaAdapter\b"),
        ("Packages.* 直接访问 Java 包", @"\bPackages\s*[.\[]"),
        ("java.lang.Runtime(执行外部命令)", @"java\.lang\.Runtime\b"),
        ("ProcessBuilder / Process(起进程)", @"\bProcess(?:Builder|Handle)?\b"),
        ("java.lang.reflect / invoke(反射)", @"java\.lang\.(?:reflect|invoke)\b"),
        ("ClassLoader(加载任意类)", @"ClassLoader\b"),
        ("Class.forName(反射加载类)", @"\bforName\s*\("),
        ("setAccessible(绕过访问控制)", @"\bsetAccessible\s*\("),
        ("java.lang.System(退出进程 / 读环境变量)", @"java\.lang\.System\b"),
        ("java.lang.Thread(后台线程)", @"java\.lang\.Thread\b"),
        ("java.net(网络)", @"java\.net\b"),
        ("java.nio.file / channels(文件系统)", @"java\.nio\.(?:file|channels)\b"),
        ("java.io(文件读写)", @"java\.io\b"),
        ("javax.script(脚本引擎)", @"javax\.script\b"),
        ("sun.* / jdk.internal / com.sun.*(JDK 内部类)", @"\b(?:sun\.|jdk\.internal|com\.sun\.)"),
        ("Java.loadClass 参数不是字符串常量(没法确认加载的是什么)", @"Java\s*\.\s*loadClass\s*\(\s*(?![""'][^""'+]*[""']\s*\))"),
    };

    private static readonly Regex UnicodeEscapeRx = new(@"\\u([0-9a-fA-F]{4})|\\u\{([0-9a-fA-F]{1,6})\}", RegexOptions.CultureInvariant);

    /// <summary>把 \uXXXX / \u{X} 转义还原(只为检查,不改写入内容)。</summary>
    public static string UnescapeUnicode(string text)
        => UnicodeEscapeRx.Replace(text ?? "", m =>
        {
            string hex = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            int code = Convert.ToInt32(hex, 16);
            return code <= 0x10FFFF ? char.ConvertFromUtf32(code >= 0xD800 && code <= 0xDFFF ? 0xFFFD : code) : m.Value;
        });

    /// <summary>KubeJS 静态检查:返回命中的规则(「第 N 行:规则 —— 片段」);空 = 没发现禁用用法。</summary>
    public static List<string> KubeJsViolations(string content)
    {
        var hits = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string raw = (content ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
        foreach (string text in new[] { raw, UnescapeUnicode(raw) })
        {
            string[] lines = text.Split('\n');
            foreach ((string rule, string pattern) in KubeJsDenied)
            {
                var rx = new Regex(pattern, RegexOptions.CultureInvariant);
                for (int i = 0; i < lines.Length; i++)
                {
                    Match m = rx.Match(lines[i]);
                    if (m.Success && seen.Add(rule + "@" + i))
                    {
                        hits.Add("第 " + (i + 1) + " 行:" + rule + " —— " + Clip(lines[i].Trim()));
                    }
                }
                // 跨行的 loadClass(参数换行写)按整段再查一遍,行号取匹配起点
                if (rule.StartsWith("Java.loadClass", StringComparison.Ordinal))
                {
                    foreach (Match m in rx.Matches(text))
                    {
                        int line = text.Substring(0, m.Index).Count(c => c == '\n');
                        if (seen.Add(rule + "@" + line))
                        {
                            hits.Add("第 " + (line + 1) + " 行:" + rule + " —— " + Clip(lines[line].Trim()));
                        }
                    }
                }
            }
        }
        return hits;
    }
}
