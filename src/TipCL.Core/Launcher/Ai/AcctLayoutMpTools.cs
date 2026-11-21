// AI-acct-layout-mp:账号 / 皮肤 / 联机 / 排版 这一包工具的**表数据**(schema、标签、关键词、预览、检查点路径)。
// 来源: Windows 版参考仓库 windos/src/TipCL/Core/Launcher/Ai/ExtraTools.cs 里的
//   list_accounts / add_offline_account / switch_account / set_skin / multiplayer 五条 + LayoutTools 两条。
// 工具名、参数名、schema 描述与 Windows 版逐字对齐(只在 set_skin 描述末尾补了一句「只对离线账号有效」,
// 因为 Linux 的 set_account_skin 对正版/外置账号会直接报错,Windows 版的 schema 没写这个前提)。
// 执行体在 ToolRunner.AcctLayoutMp.cs(partial),布局逻辑在 LayoutTools.cs;
// 共享文件(Tools / ToolMeta / ToolRunner / Preview / AiData)里只各留一行转发,方便合并。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TipCL.Core.Launcher.Layout;

namespace TipCL.Core.Launcher.Ai;

/// <summary>账号 / 皮肤 / 联机 / 排版 工具的静态表与辅助判定。</summary>
public static class AcctLayoutMpTools
{
    /// <summary>本包负责的全部工具名(声明顺序 = schema 顺序)。</summary>
    public static readonly string[] Names =
    {
        "list_accounts", "add_offline_account", "switch_account", "set_skin", "multiplayer",
        LayoutTools.GetName, LayoutTools.ApplyName,
    };

    /// <summary>工具名 → 中文标签(与 Windows ExtraTools.Labels 同文)。</summary>
    public static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        { "list_accounts", "查看账号" },
        { "add_offline_account", "添加离线账号" },
        { "switch_account", "切换账号" },
        { "set_skin", "设置皮肤" },
        { "multiplayer", "联机" },
        { "layout_get", "查看界面布局" },
        { "layout_apply", "调整界面布局" },
    };

    /// <summary>带参数时的标签模板(参数缺失就回落成上面的通用标签)。</summary>
    private static readonly Dictionary<string, string> Formats = new(StringComparer.Ordinal)
    {
        { "add_offline_account", "添加离线账号 {username}" },
        { "switch_account", "切换账号 {name}" },
        { "set_skin", "给 {name} 换皮肤" },
        { "multiplayer", "联机:{action}" },
    };

    /// <summary>按用户用词追加的工具(与 Windows ExtraTools.Keywords 的三行同值)。</summary>
    public static readonly (string[] keys, string[] extra)[] Keywords =
    {
        (new[] { "联机", "开房", "房间", "朋友一起", "一起玩", "局域网", "陶瓦", "lan", "multiplayer" }, new[] { "multiplayer" }),
        (new[] { "皮肤", "账号", "账户", "披风", "离线", "skin", "account" }, new[] { "list_accounts", "add_offline_account", "switch_account", "set_skin" }),
        (new[] { "布局", "排版", "自定义布局", "页面顺序", "区块顺序", "界面太乱", "换布局", "layout", "ui_layout" }, new[] { "layout_get", "layout_apply" }),
    };

    private const string SchemasJson = "[" +
        "{\"type\": \"function\", \"function\": {\"name\": \"list_accounts\", \"description\": \"列出启动器里的游戏账号（正版 / 离线 / 外置），以及当前用哪个、皮肤模型\", \"parameters\": {\"type\": \"object\", \"properties\": {}}}}, " +
        "{\"type\": \"function\", \"function\": {\"name\": \"add_offline_account\", \"description\": \"添加离线账号\", \"parameters\": {\"type\": \"object\", \"properties\": {\"username\": {\"type\": \"string\", \"description\": \"3-16 位英文、数字或下划线\"}}, \"required\": [\"username\"]}}}, " +
        "{\"type\": \"function\", \"function\": {\"name\": \"switch_account\", \"description\": \"切换当前使用的账号\", \"parameters\": {\"type\": \"object\", \"properties\": {\"name\": {\"type\": \"string\"}}, \"required\": [\"name\"]}}}, " +
        "{\"type\": \"function\", \"function\": {\"name\": \"set_skin\", \"description\": \"给账号换皮肤：传本地 png 皮肤文件路径（可以是用户刚附的图片），model 为 classic（粗手臂）或 slim（细手臂）。只对离线账号有效\", \"parameters\": {\"type\": \"object\", \"properties\": {\"name\": {\"type\": \"string\", \"description\": \"账号名\"}, \"path\": {\"type\": \"string\", \"description\": \"皮肤 png 路径\"}, \"model\": {\"type\": \"string\", \"description\": \"classic / slim\"}}, \"required\": [\"name\", \"path\"]}}}, " +
        "{\"type\": \"function\", \"function\": {\"name\": \"multiplayer\", \"description\": \"陶瓦联机（不用公网 IP 和朋友一起玩）。action：status 查看状态；host 开房（先在游戏里打开单人世界并对局域网开放）；join 用房间号加入；stop 退出联机。\", \"parameters\": {\"type\": \"object\", \"properties\": {\"action\": {\"type\": \"string\", \"description\": \"status / host / join / stop\"}, \"room\": {\"type\": \"string\", \"description\": \"join 时填朋友给的房间号\"}}, \"required\": [\"action\"]}}}" +
        "]";

    private static List<object>? _schemas;

    /// <summary>本包全部 schema(账号/皮肤/联机 5 条 + 布局 2 条)。</summary>
    public static List<object> Schemas()
    {
        List<object>? cached = _schemas;
        if (cached == null)
        {
            cached = Py.ListOr(PyJson.Loads(SchemasJson));
            cached.AddRange(LayoutTools.Schemas());
            _schemas = cached;
        }
        // 每次给一份浅拷贝:调用方(Tools.Schemas)会把它并进自己的列表
        return new List<object>(cached);
    }

    /// <summary>是否本包负责的工具。</summary>
    public static bool Handles(string? name) => name != null && Array.IndexOf(Names, name) >= 0;

    /// <summary>本包工具的中文标签;不是本包的工具返回 null(由 Tools.Label 继续走自己的表)。</summary>
    public static string? Label(string? name, PyDict? args)
    {
        if (name == null || !Labels.TryGetValue(name, out string? label))
        {
            return null;
        }
        if (args != null && Formats.TryGetValue(name, out string? fmt))
        {
            string filled = fmt;
            foreach (Match m in Regex.Matches(fmt, "\\{([a-z_]+)\\}"))
            {
                string value = Py.S(args.Get(m.Groups[1].Value)).Trim();
                if (value.Length == 0)
                {
                    return label;
                }
                filled = filled.Replace(m.Value, value);
            }
            return filled;
        }
        return label;
    }

    // ————— 账号:只读出口的字段白名单 —————

    /// <summary>list_accounts 允许返回的字段(与 Windows 版 list_accounts 的投影同集合)。token / 密码 / uuid / 头像地址一律不出。</summary>
    public static readonly string[] AccountFields = { "name", "type", "active", "skin_model", "skin_file" };

    /// <summary>
    /// 把 get_account_rows 的行投影成 AI 能看的安全字段:只留 <see cref="AccountFields"/>,
    /// skin_file 只给文件名(不暴露数据目录路径)。不认识的结构直接丢掉。
    /// </summary>
    public static List<object> SafeAccountRows(IEnumerable<object?>? rows)
    {
        var result = new List<object>();
        if (rows == null)
        {
            return result;
        }
        foreach (object? raw in rows)
        {
            if (raw is not PyDict row)
            {
                continue;
            }
            string name = Py.S(row.Get("name"));
            if (name.Length == 0)
            {
                continue;
            }
            string skinFile = Py.S(row.Get("skin_file"));
            if (skinFile.Length > 0)
            {
                skinFile = Path.GetFileName(skinFile.Replace('\\', '/'));
            }
            result.Add(PyDict.Of(
                "name", name,
                "type", Py.S(Py.Or(row.Get("type"), "offline")),
                "active", Py.Truthy(row.Get("active")),
                "skin_model", Py.S(Py.Or(row.Get("skin_model"), "classic")),
                "skin_file", skinFile));
        }
        return result;
    }

    /// <summary>离线账号用户名规则(与 Windows 版 add_offline_account 同一正则)。</summary>
    public static bool ValidOfflineName(string? name) => Regex.IsMatch(name ?? "", "^[A-Za-z0-9_]{3,16}$");

    /// <summary>set_skin 的皮肤路径(兼容别名 skin_file / file;展开 ~)。</summary>
    public static string SkinPathArg(PyDict args)
    {
        string path = Py.S(Py.Or(Py.Or(args.Get("path"), args.Get("skin_file")), args.Get("file"))).Trim().Trim('"');
        if (path.StartsWith("~", StringComparison.Ordinal))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = home + path.Substring(1);
        }
        return path;
    }

    /// <summary>set_skin 的手臂模型(兼容别名 skin_model;只认 slim,其余一律 classic)。</summary>
    public static string SkinModelArg(PyDict args)
        => Py.Lower(Py.S(Py.Or(args.Get("model"), args.Get("skin_model"))).Trim()) == "slim" ? "slim" : "classic";

    /// <summary>
    /// 对话里附了图片时,把**本地 png 路径**作为一行附注交给模型(模型看得到图,但不知道文件在哪,
    /// 没有这行 set_skin 就拿不到 path)。没有 png 附件返回空串;只给 png,别的格式当不了皮肤。
    /// </summary>
    public static string AttachmentNote(IEnumerable<string>? paths)
    {
        List<string> pngs = (paths ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p) && p.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (pngs.Count == 0)
        {
            return "";
        }
        return "\n\n[附图本地文件(要换皮肤时可把路径原样传给 set_skin 的 path):" + string.Join(" ; ", pngs) + "]";
    }

    // ————— 检查点:会落盘的路径 —————

    /// <summary>
    /// 写操作前要快照的路径。只有 layout_apply 给出(ui_layout_v2.json 与写通的 ui_page_layouts.json);
    /// 账号类**故意不做文件快照**:accounts.json 里是封存过的令牌/钥匙串引用,回退成旧文件会把之后新登录的账号
    /// 一起冲掉、或留下指向已删钥匙串的悬空引用(Windows 版同样不给账号做检查点)。联机不落盘。
    /// </summary>
    public static List<string> AffectedPaths(string name, PyDict? args)
    {
        if (name == LayoutTools.ApplyName)
        {
            try
            {
                return new List<string> { LayoutStoreV2.FilePath, LayoutStoreV2.V1FilePath };
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }
        return new List<string>();
    }

    // ————— 确认卡上的「将要发生什么」 —————

    private static PyDict Row(string kind, string head, List<string> lines, bool tooLarge = false)
        => PyDict.Of("kind", kind, "head", head, "lines", lines.Cast<object>().ToList(), "too_large", tooLarge);

    /// <summary>本包写工具的预览(形状与 Preview.ChangePreview 一致:kind / head / lines / too_large)。</summary>
    public static PyDict? ChangePreview(string name, PyDict? args)
    {
        args ??= new PyDict();
        var lines = new List<string>();
        switch (name)
        {
            case "add_offline_account":
            {
                string user = Py.S(args.Get("username")).Trim();
                lines.Add(I18nCore.Tr("账号：{name}（离线）").Replace("{name}", user));
                if (FindAccount(user) != null)
                {
                    lines.Add(I18nCore.Tr("已有同名账号，不会重复添加"));
                }
                else
                {
                    lines.Add(I18nCore.Tr("添加后会切换为当前账号"));
                }
                return Row("info", I18nCore.Tr("将要添加的账号："), lines);
            }
            case "switch_account":
            {
                string current = "";
                try
                {
                    current = Py.S(new AccountManager().active);
                }
                catch (Exception)
                {
                    current = "";
                }
                lines.Add(I18nCore.Tr("当前账号：{name}").Replace("{name}", current.Length > 0 ? current : "-"));
                lines.Add(I18nCore.Tr("切换到：{name}").Replace("{name}", Py.S(args.Get("name")).Trim()));
                return Row("info", I18nCore.Tr("将要切换账号："), lines);
            }
            case "set_skin":
            {
                lines.Add(I18nCore.Tr("当前账号：{name}").Replace("{name}", Py.S(args.Get("name")).Trim()));
                lines.Add(I18nCore.Tr("皮肤文件：{path}").Replace("{path}", SkinPathArg(args)));
                lines.Add(I18nCore.Tr("手臂模型：{model}").Replace("{model}", SkinModelArg(args)));
                return Row("info", I18nCore.Tr("将要更换皮肤："), lines);
            }
            case "multiplayer":
            {
                string action = Py.Lower(Py.S(args.Get("action")).Trim());
                switch (action)
                {
                    case "host":
                        lines.Add(I18nCore.Tr("开房：启动陶瓦联机内核（后台进程）并连接公共节点"));
                        lines.Add(I18nCore.Tr("联机内核还没装时会先下载"));
                        break;
                    case "join":
                        lines.Add(I18nCore.Tr("加入房间：{room}").Replace("{room}", Py.S(args.Get("room")).Trim()));
                        lines.Add(I18nCore.Tr("联机内核还没装时会先下载"));
                        break;
                    case "stop":
                        lines.Add(I18nCore.Tr("退出联机（内核保持运行）"));
                        break;
                    default:
                        lines.Add(action.Length > 0 ? action : "status");
                        break;
                }
                return Row("info", I18nCore.Tr("联机操作："), lines);
            }
            case "layout_apply":
            {
                LayoutTools.LayoutPlan plan = LayoutTools.Plan(args);
                if (plan.Error != null)
                {
                    lines.Add(plan.Error);
                    return Row("info", I18nCore.Tr("这次改动用不了："), lines);
                }
                lines.AddRange(plan.Changes);
                lines.AddRange(plan.Notes);
                lines.Add(I18nCore.Tr("改动前会自动备份为方案「AI 备份 <时间>」"));
                List<string> diff = Preview.UnifiedDiff(
                    Py.SplitLines(PyJson.Dumps(LayoutTools.Summary(plan.Current), 2, ensureAscii: false)),
                    Py.SplitLines(PyJson.Dumps(LayoutTools.Summary(plan.Next), 2, ensureAscii: false)),
                    "a/ui_layout_v2.json", "b/ui_layout_v2.json");
                bool tooLarge = diff.Count > Preview.MAX_PREVIEW_LINES;
                lines.AddRange(diff.Take(Preview.MAX_PREVIEW_LINES));
                return Row("diff", I18nCore.Tr("将要调整的布局："), lines, tooLarge);
            }
            default:
                return null;
        }
    }

    private static PyDict? FindAccount(string name)
    {
        if (name.Length == 0)
        {
            return null;
        }
        try
        {
            return new AccountManager().GetAccount(name);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
