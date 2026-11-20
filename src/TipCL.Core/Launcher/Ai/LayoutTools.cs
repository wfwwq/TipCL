// AI-acct-layout-mp:AI 排版工具(layout_get / layout_apply)的 Linux 版。
// 来源: Windows 版参考仓库 windos/src/TipCL/Core/Launcher/Ai/LayoutTools.cs(工具名、参数名、返回文案逐字对齐)。
//
// 与 Windows 版的差异(都是「更严」,不是「更松」):
//   1. D-30 门禁在 layout_apply 里**同样执行**:web 只允许 https(禁 file:// / javascript: / data: / http),
//      button 只允许内部动作白名单 page / launch / folder:data / rpc:<只读 RPC>。判定与
//      App 侧 Pages/Layout/Widgets/WidgetCommon 逐字等价(Core 不能引用 App,这里复刻一份;
//      两份结果一致由 AiAcctLayoutMpTests 的对照用例锁死)。不合法 → 整次拒绝,不落盘。
//   2. AI 新写入 / 改过目标的 web / button 一律 enabled=false(与 4277a02「导入危险件默认关闭」同一口径):
//      D-30 要求开启必须由用户在设置里显式勾选并二次确认,AI 不能替用户打开。
//      用户自己早已开启、且这次没改网址/动作的那块保持原样。
//   3. mode 取值不合法直接报错(Windows 版交给 Sanitize 静默改回 stack,模型会以为改成功了)。
//   4. 方案已满(LayoutSchema.MaxProfiles)时拒绝,而不是让 Sanitize 把刚做的「AI 备份」悄悄丢掉。
//   5. 「先算再写」拆成 Plan / Apply 两步:确认卡上的「改前改后」预览与真正执行走同一份 Plan,不会两套语义。
using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core.Launcher.Layout;

namespace TipCL.Core.Launcher.Ai;

/// <summary>AI 布局工具:读当前布局、改布局(改之前自动备份成「AI 备份 &lt;时间&gt;」方案)。</summary>
public static class LayoutTools
{
    /// <summary>只读工具名。</summary>
    public const string GetName = "layout_get";

    /// <summary>写工具名(ro=false,走确认通道)。</summary>
    public const string ApplyName = "layout_apply";

    /// <summary>自动备份用的方案名前缀(与 Windows 版一致)。</summary>
    public const string BackupPrefix = "AI 备份 ";

    /// <summary>最多保留几份 AI 备份(避免 profiles 无限膨胀)。</summary>
    public const int MaxBackups = 5;

    private const int MaxBlocksPerPage = 512;

    /// <summary>button 允许触发的只读 RPC(与 WidgetCommon.RpcAllowList 同值)。</summary>
    public static readonly string[] RpcAllowList = { "get_launcher_state", "list_instances", "plugins_list" };

    /// <summary>button 允许的动作前缀(与 WidgetCommon.ActionKinds 同值)。</summary>
    public static readonly string[] ActionKinds = { "page", "launch", "folder", "rpc" };

    private const string SchemasJson = """
    [
      {
        "type": "function",
        "function": {
          "name": "layout_get",
          "description": "查看当前界面布局:每个页面的区块顺序与显隐、页面布局模式、自建区块(note/shortcut/clock/divider/playtime)、分栏比例、顶栏页签、命名方案。改布局之前先看它,不要凭记忆改。",
          "parameters": { "type": "object", "properties": {} }
        }
      },
      {
        "type": "function",
        "function": {
          "name": "layout_apply",
          "description": "修改界面布局。改之前会自动把当前布局备份成一个命名方案(profiles 里的「AI 备份 …」),用户可以随时切回去。可组合使用:page + blocks 改某一页的区块顺序与显隐;profile 应用一份已保存的方案;json 提交整份布局文档;widgets 新增或覆盖自建区块;shell_tabs 改顶栏页签。改完要告诉用户改了什么、备份叫什么名字。",
          "parameters": {
            "type": "object",
            "properties": {
              "page": { "type": "string", "description": "要改的页面 id,如 launch / tasks / settings:general" },
              "blocks": {
                "type": "array",
                "description": "该页区块的新顺序;每项 {id, hidden}。没列出的内置区块会按默认可见性补到末尾",
                "items": {
                  "type": "object",
                  "properties": {
                    "id": { "type": "string" },
                    "hidden": { "type": "boolean" }
                  }
                }
              },
              "mode": { "type": "string", "description": "该页布局模式:stack / grid / canvas" },
              "cols": { "type": "integer", "description": "该页列数:{2,3,4,6,12}" },
              "gap": { "type": "number", "description": "该页块间距 0..32,默认 10" },
              "profile": { "type": "string", "description": "应用一份已保存的命名方案(layout_get 里有方案名)" },
              "json": { "type": "object", "description": "整份 v2 布局文档(kind=tipcl-layout,version=2)" },
              "widgets": {
                "type": "array",
                "description": "新增或覆盖自建区块定义:每项 {id, type, title, settings};type 首选 note/shortcut/clock/divider/playtime。web(settings.url 只允许 https)与 button(settings.action 只允许 page:<页面> / launch:<实例> / folder:data / rpc:<只读接口>)属危险类型,写入后一律默认关闭,要用户自己到 设置 → 布局 → 自建区块 开启",
                "items": { "type": "object" }
              },
              "shell_tabs": {
                "type": "array",
                "description": "顶栏页签的顺序与显隐,取值只能是 launch/library/download/plugins/settings",
                "items": { "type": "string" }
              }
            }
          }
        }
      }
    ]
    """;

    /// <summary>两个工具的 schema(由 AcctLayoutMpTools.Schemas() 合并进工具表)。</summary>
    public static List<object> Schemas() => Py.ListOr(PyJson.Loads(SchemasJson));

    /// <summary>是否本模块负责的工具名。</summary>
    public static bool Handles(string? name) => name == GetName || name == ApplyName;

    /// <summary>分发。</summary>
    public static string Run(string name, PyDict? args) => name == GetName ? Get() : Apply(args);

    /// <summary>布局摘要(AI 看的紧凑格式,不含整份方案快照)。</summary>
    public static string Get() => PyJson.Dumps(Summary(LayoutStoreV2.Load()), 2, ensureAscii: false);

    /// <summary>摘要字典(与 Windows 版 Get 同形:version / active_profile / profiles / shell / pages / widgets / splits)。</summary>
    public static PyDict Summary(LayoutV2 doc)
    {
        var summary = new PyDict
        {
            ["version"] = (long)LayoutSchema.CurrentVersion,
            ["active_profile"] = doc.ActiveProfile ?? "",
            ["profiles"] = doc.Profiles.Keys.Cast<object>().ToList(),
            ["shell"] = PyDict.Of(
                "tabs", doc.Layout.Shell.Tabs.Cast<object>().ToList(),
                "density", doc.Layout.Shell.Density),
        };
        var pages = new PyDict();
        foreach (KeyValuePair<string, PageLayoutV2> kv in doc.Layout.Pages.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var blocks = new List<object>();
            foreach (BlockLayout block in kv.Value.Blocks)
            {
                blocks.Add(block.Hidden ? block.Id + "(hidden)" : block.Id);
            }
            pages[kv.Key] = PyDict.Of("mode", kv.Value.Mode, "cols", (long)kv.Value.Cols, "gap", kv.Value.Gap, "blocks", blocks);
        }
        summary["pages"] = pages;
        if (doc.Layout.Widgets.Count > 0)
        {
            var widgets = new List<object>();
            foreach (WidgetDef widget in doc.Layout.Widgets)
            {
                var item = PyDict.Of("id", widget.Id, "type", widget.Type, "title", widget.Title, "block_id", "custom:" + widget.Id);
                if (LayoutSchema.IsDangerousWidgetType(widget.Type))
                {
                    item["enabled"] = WidgetEnabled(widget);
                }
                widgets.Add(item);
            }
            summary["widgets"] = widgets;
        }
        if (doc.Layout.Splits.Count > 0)
        {
            var splits = new PyDict();
            foreach (KeyValuePair<string, SplitState> kv in doc.Layout.Splits.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var ratios = new PyDict();
                foreach (KeyValuePair<string, double> ratio in kv.Value.Ratios)
                {
                    ratios[ratio.Key] = ratio.Value;
                }
                splits[kv.Key] = ratios;
            }
            summary["splits"] = splits;
        }
        return summary;
    }

    /// <summary>一次「先算不写」的结果:改前文档、改后文档(已过 Sanitize,尚未加备份)、改动说明、错误原因。</summary>
    public sealed class LayoutPlan
    {
        public LayoutV2 Current = new LayoutV2();
        public LayoutV2 Next = new LayoutV2();
        public List<string> Changes = new List<string>();
        public List<string> Notes = new List<string>();
        public string? Error;
    }

    /// <summary>
    /// 只计算、不落盘:读当前布局 → 在副本上改 → D-30 门禁 → Sanitize。
    /// 预览(确认卡)与执行共用这一份,保证「看到的就是要做的」。
    /// </summary>
    public static LayoutPlan Plan(PyDict? args)
    {
        PyDict input = args ?? new PyDict();
        var plan = new LayoutPlan { Current = LayoutStoreV2.Load() };
        plan.Next = plan.Current.Clone();
        plan.Error = ApplyOps(plan.Next, input, plan.Changes);
        if (plan.Error != null)
        {
            return plan;
        }
        if (plan.Changes.Count == 0)
        {
            plan.Error = "没有识别到要修改的内容:请给 page+blocks(改某一页)、profile(应用方案)、json(整份文档)、widgets 或 shell_tabs 中的一个。";
            return plan;
        }
        plan.Error = GuardDangerousWidgets(plan.Current, plan.Next, plan.Notes);
        if (plan.Error != null)
        {
            return plan;
        }
        LayoutSchema.Sanitize(plan.Next);
        return plan;
    }

    /// <summary>
    /// 应用布局改动:Plan → 把**改之前**的布局备份成方案 → Save(v2 + 写通 v1,触发 LayoutChanged)。
    /// 任何一步失败都返回中文原因,且不落盘(不会留下半成品)。
    /// </summary>
    public static string Apply(PyDict? args)
    {
        LayoutPlan plan = Plan(args);
        if (plan.Error != null)
        {
            return plan.Error;
        }
        LayoutV2 next = plan.Next;
        PruneBackups(next, MaxBackups - 1);
        if (next.Profiles.Count >= LayoutSchema.MaxProfiles)
        {
            return "布局方案已经有 " + next.Profiles.Count + " 个(上限 " + LayoutSchema.MaxProfiles + "),放不下改动前的备份;请先在 设置 → 布局 → 布局方案 里删掉几个再试。";
        }
        string backup = BackupName(next);
        next.Profiles[backup] = plan.Current.Layout.Clone();
        bool saved = LayoutStoreV2.Save(next);
        // 写通 v1 后清掉老路径的缓存,让还走 v1 的读取立刻看到新值(与 Windows 版同一时点)
        LayoutStoreV2.RefreshV1Cache();
        string text = "已更新布局:" + string.Join(";", plan.Changes) + "。改动前的布局已备份为方案「" + backup + "」(可在设置 → 布局里切回)。";
        if (plan.Notes.Count > 0)
        {
            text += string.Join("", plan.Notes);
        }
        if (!saved)
        {
            text += "(注意:布局文件没能写进磁盘,本次改动只在这次运行里有效,重启后会丢。)";
        }
        return text;
    }

    /// <summary>备份方案名:「AI 备份 yyyy-MM-dd HH:mm:ss」,同一秒里重名就追加序号。</summary>
    private static string BackupName(LayoutV2 doc)
    {
        string name = BackupPrefix + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string candidate = name;
        for (int i = 2; doc.Profiles.ContainsKey(candidate); i++)
        {
            candidate = name + " #" + i;
        }
        return candidate;
    }

    // ————— 具体改动(每项都往 changes 里记一句中文说明) —————

    private static string? ApplyOps(LayoutV2 next, PyDict args, List<string> changes)
    {
        object? jsonValue = args.Get("json");
        if (Py.Truthy(jsonValue))
        {
            string text = jsonValue is string s ? s : PyJson.Dumps(jsonValue, 2, ensureAscii: false);
            LayoutV2 parsed;
            try
            {
                parsed = LayoutStoreV2.ParseImport(text);
            }
            catch (InvalidOperationException ex)
            {
                return "这份布局 JSON 用不了:" + ex.Message;
            }
            next.Layout = parsed.Layout;
            next.Profiles = parsed.Profiles;
            next.ActiveProfile = parsed.ActiveProfile;
            changes.Add("用整份 JSON 替换了布局");
        }
        string profile = Py.S(Py.Or(args.Get("profile"), "")).Trim();
        if (profile.Length > 0)
        {
            if (!next.Profiles.TryGetValue(profile, out LayoutBody? body) || body == null)
            {
                return "找不到名为「" + profile + "」的布局方案;先用 layout_get 看现有方案名。";
            }
            next.Layout = body.Clone();
            next.ActiveProfile = profile;
            changes.Add("应用方案「" + profile + "」");
        }
        string page = Py.S(Py.Or(args.Get("page"), "")).Trim();
        List<object> blocks = ListOrEmpty(args, "blocks");
        bool hasPageParams = args.Get("mode") != null || args.Get("cols") != null || args.Get("gap") != null;
        if (page.Length > 0 && (blocks.Count > 0 || hasPageParams))
        {
            PageLayoutV2 target = next.Layout.Pages.TryGetValue(page, out PageLayoutV2? found) && found != null
                ? found.Clone()
                : new PageLayoutV2();
            if (blocks.Count > 0)
            {
                if (blocks.Count > MaxBlocksPerPage)
                {
                    return "单个页面的区块不能超过 " + MaxBlocksPerPage + " 个。";
                }
                var ordered = new List<LayoutV1Block>();
                foreach (object item in blocks)
                {
                    if (item is not PyDict entry)
                    {
                        continue;
                    }
                    string id = Py.S(Py.Or(entry.Get("id"), "")).Trim();
                    if (id.Length == 0)
                    {
                        continue;
                    }
                    ordered.Add(new LayoutV1Block { Id = id, Hidden = Py.Truthy(entry.Get("hidden")) });
                }
                if (ordered.Count == 0)
                {
                    return "blocks 里没有有效的区块 id。";
                }
                // 顺序 / 显隐以 AI 给的为准;同一块的其它 v2 字段(span / style / settings)按 id 保留(Core 唯一实现)
                target = LayoutStoreV2.MergeV1Order(target, ordered);
            }
            if (args.Get("mode") != null)
            {
                string mode = Py.S(args.Get("mode")).Trim();
                if (!LayoutSchema.IsKnownMode(mode))
                {
                    return "mode 只能是 stack / grid / canvas,不认识「" + mode + "」。";
                }
                target.Mode = mode;
            }
            if (args.Get("cols") != null)
            {
                long cols;
                try
                {
                    cols = Py.Int(args.Get("cols"));
                }
                catch (Exception)
                {
                    return "cols 要写数字(2 / 3 / 4 / 6 / 12)。";
                }
                target.Cols = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, cols));
            }
            if (args.Get("gap") != null)
            {
                try
                {
                    target.Gap = Py.Float(args.Get("gap"));
                }
                catch (Exception)
                {
                    return "gap 要写数字(0..32)。";
                }
            }
            next.Layout.Pages[page] = target;
            changes.Add("页面 " + page + " 的区块顺序/显隐" + (hasPageParams ? "与页面参数" : ""));
        }
        List<object> widgets = ListOrEmpty(args, "widgets");
        if (widgets.Count > 0)
        {
            var touched = new List<string>();
            foreach (object item in widgets)
            {
                if (item is not PyDict entry)
                {
                    continue;
                }
                string id = Py.S(Py.Or(entry.Get("id"), "")).Trim();
                string type = Py.S(Py.Or(entry.Get("type"), "")).Trim();
                if (id.Length == 0)
                {
                    // 缺 id 时按时间戳生成(与 LayoutSchema.NewWidgetId 同规则;同毫秒重名由 Normalize 追加序号)
                    id = LayoutSchema.NewWidgetId();
                }
                if (type.Length == 0)
                {
                    return "自建区块 " + id + " 没写 type。";
                }
                if (!LayoutSchema.IsKnownWidgetType(type))
                {
                    return "不认识的区块类型「" + type + "」;首期只支持 note / shortcut / clock / divider / playtime(web / button 需显式开启)。";
                }
                WidgetDef? existing = next.Layout.Widgets.FirstOrDefault(w => w != null && string.Equals(w.Id, id, StringComparison.Ordinal));
                WidgetDef def = existing ?? new WidgetDef { Id = id };
                def.Id = id;
                def.Type = type;
                def.Title = Py.S(Py.Or(entry.Get("title"), "")).Trim();
                def.Settings = Plain(entry.Get("settings")) as Dictionary<string, object?> ?? LayoutJson.Map();
                if (existing == null)
                {
                    next.Layout.Widgets.Add(def);
                }
                touched.Add(type + ":" + id);
            }
            if (touched.Count > 0)
            {
                changes.Add("自建区块 " + string.Join("/", touched));
            }
        }
        List<object> tabs = ListOrEmpty(args, "shell_tabs");
        if (tabs.Count > 0)
        {
            var names = new List<string>();
            foreach (object item in tabs)
            {
                string name = Py.S(item).Trim();
                if (name.Length == 0)
                {
                    continue;
                }
                if (!LayoutSchema.IsKnownTab(name))
                {
                    return "顶栏页签只支持 launch / library / download / plugins / settings,不认识「" + name + "」。";
                }
                if (!names.Contains(name))
                {
                    names.Add(name);
                }
            }
            if (names.Count == 0)
            {
                return "shell_tabs 里没有有效的页签名。";
            }
            next.Layout.Shell.Tabs = names;
            changes.Add("顶栏页签 → " + string.Join(" / ", names));
        }
        return null;
    }

    // ————— D-30 门禁 —————

    /// <summary>
    /// 对改后文档里的每个 web / button 定义(当前布局、各方案、各响应式变体都算)执行 D-30:
    /// 「新写入或改了网址/动作」的 → 先校验(不合法整次拒绝),再强制 enabled=false;
    /// 用户早已开启且这次目标没变的 → 原样保留。返回 null = 通过。
    /// </summary>
    public static string? GuardDangerousWidgets(LayoutV2 current, LayoutV2 next, List<string>? notes = null)
    {
        Dictionary<string, WidgetDef> known = new Dictionary<string, WidgetDef>(StringComparer.Ordinal);
        foreach (WidgetDef w in AllWidgets(current))
        {
            if (LayoutSchema.IsDangerousWidgetType(w.Type) && !known.ContainsKey(w.Id))
            {
                known[w.Id] = w;
            }
        }
        var disarmed = new List<string>();
        foreach (WidgetDef w in AllWidgets(next))
        {
            if (!LayoutSchema.IsDangerousWidgetType(w.Type))
            {
                continue;
            }
            known.TryGetValue(w.Id, out WidgetDef? before);
            bool sameTarget = before != null && string.Equals(before.Type, w.Type, StringComparison.Ordinal)
                && string.Equals(Target(before), Target(w), StringComparison.Ordinal);
            if (!sameTarget)
            {
                string? reason = DangerousWidgetError(w);
                if (reason != null)
                {
                    return "自建区块 " + w.Id + "(" + w.Type + ")不能用:" + reason;
                }
            }
            bool keepEnabled = sameTarget && WidgetEnabled(before!);
            if (!keepEnabled && WidgetEnabled(w))
            {
                w.Settings ??= LayoutJson.Map();
                w.Settings["enabled"] = false;
                if (!disarmed.Contains(w.Id))
                {
                    disarmed.Add(w.Id);
                }
            }
        }
        if (disarmed.Count > 0 && notes != null)
        {
            notes.Add("网页/按钮区块 " + string.Join("、", disarmed) + " 按安全规定先保持关闭,要用的话请用户自己到 设置 → 布局 → 自建区块 里开启。");
        }
        return null;
    }

    /// <summary>单个 web / button 定义的 D-30 判定:合法返回 null,否则返回中文原因(与 WidgetCommon 同文案)。</summary>
    public static string? DangerousWidgetError(WidgetDef widget)
    {
        if (widget == null || !LayoutSchema.IsDangerousWidgetType(widget.Type))
        {
            return null;
        }
        if (string.Equals(widget.Type, LayoutSchema.WidgetWeb, StringComparison.Ordinal))
        {
            return TryAcceptWebUrl(Str(widget.Settings, "url"), out _, out string reason) ? null : reason;
        }
        return TryParseAction(Str(widget.Settings, "action"), out _, out _, out _, out string why) ? null : why;
    }

    /// <summary>web 地址准入(与 WidgetCommon.TryAcceptWebUrl 逐字等价):只允许 https,必须有主机名。</summary>
    public static bool TryAcceptWebUrl(string? url, out string normalized, out string reason)
    {
        normalized = "";
        reason = "";
        string text = (url ?? "").Trim();
        if (text.Length == 0)
        {
            reason = I18nCore.Tr("没有填写网页地址");
            return false;
        }
        if (text.Length > 2048)
        {
            reason = I18nCore.Tr("网页地址太长");
            return false;
        }
        if (text.IndexOf("://", StringComparison.Ordinal) < 0)
        {
            reason = I18nCore.Tr("网页地址必须以 https:// 开头");
            return false;
        }
        if (text.StartsWith("file://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || text.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            reason = I18nCore.Tr("只允许 https 网页,禁止本地文件与脚本地址");
            return false;
        }
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || uri == null)
        {
            reason = I18nCore.Tr("网页地址格式不对");
            return false;
        }
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            reason = I18nCore.Tr("只允许 https 网页(http 与其它协议一律拒绝)");
            return false;
        }
        if (string.IsNullOrEmpty(uri.Host))
        {
            reason = I18nCore.Tr("网页地址缺少主机名");
            return false;
        }
        normalized = uri.AbsoluteUri;
        return true;
    }

    /// <summary>button 内部动作白名单解析(与 WidgetCommon.TryParseAction 逐字等价)。</summary>
    public static bool TryParseAction(string? action, out string kind, out string arg1, out string arg2, out string reason)
    {
        kind = "";
        arg1 = "";
        arg2 = "";
        reason = "";
        string text = (action ?? "").Trim();
        if (text.Length == 0)
        {
            reason = I18nCore.Tr("没有填写按钮动作");
            return false;
        }
        int sep = text.IndexOf(':');
        if (sep <= 0)
        {
            reason = I18nCore.Tr("按钮动作要写成 类型:参数(例如 page:launch)");
            return false;
        }
        string head = text.Substring(0, sep);
        string rest = text.Substring(sep + 1).Trim();
        if (Array.IndexOf(ActionKinds, head) < 0)
        {
            reason = Py.Fmt(I18nCore.Tr("不支持的按钮动作:{0}(只允许 page / launch / folder / rpc)"), head);
            return false;
        }
        if (rest.Length == 0)
        {
            reason = I18nCore.Tr("按钮动作缺少参数");
            return false;
        }
        switch (head)
        {
            case "page":
                if (!IsKnownPageTarget(rest))
                {
                    reason = I18nCore.Tr("按钮不能打开这个页面");
                    return false;
                }
                kind = head;
                arg1 = rest;
                return true;
            case "folder":
                if (!string.Equals(rest, "data", StringComparison.OrdinalIgnoreCase))
                {
                    reason = I18nCore.Tr("只允许打开启动器数据目录(folder:data)");
                    return false;
                }
                kind = head;
                arg1 = rest;
                return true;
            case "rpc":
                if (Array.IndexOf(RpcAllowList, rest) < 0)
                {
                    reason = Py.Fmt(I18nCore.Tr("这个 RPC 不在白名单里:{0}"), rest);
                    return false;
                }
                kind = head;
                arg1 = rest;
                return true;
            default:
            {
                int bar = rest.IndexOf('|');
                kind = head;
                if (bar > 0)
                {
                    arg1 = rest.Substring(0, bar).Trim();
                    arg2 = rest.Substring(bar + 1).Trim();
                }
                else
                {
                    arg1 = rest;
                }
                if (arg1.Length == 0)
                {
                    reason = I18nCore.Tr("按钮动作缺少实例名");
                    return false;
                }
                return true;
            }
        }
    }

    // 与 WidgetCommon.IsKnownPageTarget / MainWindow.GetPage 能打开的页面一致
    private static bool IsKnownPageTarget(string id)
    {
        switch (id)
        {
            case "launch":
            case "library":
            case "instance":
            case "download":
            case "plugins":
            case "settings":
            case "multiplayer":
            case "ai":
            case "account":
            case "tasks":
                return true;
            default:
                return id.StartsWith("plugin:", StringComparison.Ordinal) && id.Length > "plugin:".Length;
        }
    }

    private static string Target(WidgetDef w)
        => string.Equals(w.Type, LayoutSchema.WidgetWeb, StringComparison.Ordinal) ? Str(w.Settings, "url") : Str(w.Settings, "action");

    private static string Str(Dictionary<string, object?>? settings, string key)
        => settings != null && settings.TryGetValue(key, out object? value) && value is string text ? text : "";

    private static IEnumerable<WidgetDef> AllWidgets(LayoutV2 doc)
    {
        foreach (LayoutBody body in Bodies(doc))
        {
            if (body.Widgets == null)
            {
                continue;
            }
            foreach (WidgetDef w in body.Widgets)
            {
                if (w != null && !string.IsNullOrEmpty(w.Id))
                {
                    yield return w;
                }
            }
        }
    }

    private static IEnumerable<LayoutBody> Bodies(LayoutV2 doc)
    {
        var roots = new List<LayoutBody>();
        if (doc.Layout != null)
        {
            roots.Add(doc.Layout);
        }
        if (doc.Profiles != null)
        {
            roots.AddRange(doc.Profiles.Values.Where(b => b != null));
        }
        foreach (LayoutBody body in roots)
        {
            yield return body;
            if (body.Variants == null)
            {
                continue;
            }
            foreach (LayoutBody variant in body.Variants.Values)
            {
                if (variant != null)
                {
                    yield return variant;
                }
            }
        }
    }

    // AI 备份只留最近 keep 份(按名字里的时间倒序),避免 profiles 被撑爆
    private static void PruneBackups(LayoutV2 doc, int keep)
    {
        List<string> names = doc.Profiles.Keys
            .Where(key => key.StartsWith(BackupPrefix, StringComparison.Ordinal))
            .OrderByDescending(key => key, StringComparer.Ordinal)
            .ToList();
        for (int i = Math.Max(0, keep); i < names.Count; i++)
        {
            doc.Profiles.Remove(names[i]);
        }
    }

    private static bool WidgetEnabled(WidgetDef widget)
        => widget.Settings != null && widget.Settings.TryGetValue("enabled", out object? value) && value is bool flag && flag;

    // 可选数组参数:缺失 / 空 / 类型不对都当"没给"
    private static List<object> ListOrEmpty(PyDict args, string key)
    {
        object? value = args.Get(key);
        if (value == null || value is string)
        {
            return new List<object>();
        }
        if (value is List<object> list)
        {
            return list;
        }
        return Py.Truthy(value) ? Py.ListOr(value) : new List<object>();
    }

    // PyDict / List<object> → 契约用的朴素值(字符串 / long / double / bool / 字典 / 列表)
    private static object? Plain(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case PyDict dict:
            {
                Dictionary<string, object?> map = LayoutJson.Map();
                foreach (string key in dict.Keys.Select(k => Py.S(k)))
                {
                    map[key] = Plain(dict.Get(key));
                }
                return map;
            }
            case string text:
                return text;
            case bool flag:
                return flag;
            case int i:
                return (long)i;
            case long or double:
                return value;
            case System.Collections.IEnumerable list:
            {
                var items = new List<object?>();
                foreach (object? item in list)
                {
                    items.Add(Plain(item));
                }
                return items;
            }
            default:
                return value;
        }
    }
}
