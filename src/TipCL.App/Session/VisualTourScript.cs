// AI3-visual:--visual-tour 的剧本模型与解析(纯数据,零界面依赖,单测直接断言)。
// i18n:ignore-file —— 开发者巡检工具的诊断文字(写进 tour-result.json),不是用户界面文案。
//
// 剧本是一份 JSON(允许 // 注释与尾逗号):
// {
//   "width": 1000, "height": 640,      // 起始窗口尺寸(可选,缺省 900x550 = 外壳默认)
//   "theme": "light",                   // 起始主题(可选)
//   "fixture": true,                    // 先在 TIPCL_HOME 里造一份固定假数据(见 VisualFixture)
//   "settleMs": 300,                    // 每步之后的默认 settle(可选)
//   "steps": [
//     { "op": "navigate", "page": "launch" },
//     { "op": "click", "text": "编辑组件" },          // 按可见文字找控件,算中心点再 MouseDown/MouseUp
//     { "op": "click", "name": "AiBtn" },             // 按控件 Name
//     { "op": "rightclick", "text": "版本列表" },
//     { "op": "key", "keys": "Ctrl+Z" },
//     { "op": "scroll", "to": { "text": "方案" } },   // 滚到某个控件
//     { "op": "scroll", "screens": 1 },               // 当前最大的可滚动区域滚 N 屏
//     { "op": "resize", "width": 780, "height": 640 },
//     { "op": "theme", "value": "dark" },
//     { "op": "wait", "ms": 500 },
//     { "op": "shot", "name": "launch-edit" }
//   ]
// }
// 选择器字段(click / rightclick / hover / dblclick / expect / scroll.to / type.into 共用):
//   name      控件 Name 精确匹配
//   text      可见文字精确匹配(TextBlock.Text、Button/ContentControl 的字符串内容、MenuItem.Header)
//   contains  可见文字包含
//   tip       ToolTip 文字精确匹配(给只有图标的按钮用)
//   watermark 输入框占位文字精确匹配
//   type      控件类型名(如 "ComboBox"),可与上面任一组合
//   within    只在 Name 为此值的祖先里找(如 "PageHost" = 只在页面内容里,不算标题栏)
//   index     命中多个时取第几个(从 0 起,按「最上层优先 + 文档顺序」)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace TipCL.App.Session;

/// <summary>剧本里的一步。字段按 op 取用,没用到的保持默认。</summary>
internal sealed class TourStep
{
    /// <summary>步骤序号(从 0 起,解析时按出现顺序编号)。</summary>
    public int Index { get; set; }

    /// <summary>动作:navigate / click / rightclick / dblclick / hover / key / type / scroll / resize / theme / wait / shot / expect / close / dump / reset。</summary>
    public string Op { get; set; } = "";

    /// <summary>navigate 的页面 id。</summary>
    public string Page { get; set; } = "";

    /// <summary>click / rightclick / hover / dblclick / expect / type 的目标。</summary>
    public TourSelector? Target { get; set; }

    /// <summary>key 的组合键串,如 "Ctrl+Z"、"Esc"。</summary>
    public string Keys { get; set; } = "";

    /// <summary>type 的文字。</summary>
    public string Text { get; set; } = "";

    /// <summary>scroll:滚 N 屏(可为负、可为小数);0 = 不按屏滚。</summary>
    public double Screens { get; set; }

    /// <summary>scroll:滚到这个控件(BringIntoView)。</summary>
    public TourSelector? ScrollTo { get; set; }

    /// <summary>scroll:滚到顶。</summary>
    public bool ScrollTop { get; set; }

    /// <summary>resize 的宽 / 高(px)。</summary>
    public double Width { get; set; }

    public double Height { get; set; }

    /// <summary>theme:light / dark。</summary>
    public string Theme { get; set; } = "";

    /// <summary>wait 的毫秒数;其它步骤里表示这一步之后额外的 settle 毫秒数(-1 = 用剧本默认)。</summary>
    public int Ms { get; set; } = -1;

    /// <summary>shot / dump 的文件名(不含扩展名)。</summary>
    public string Name { get; set; } = "";

    /// <summary>shot:main = 只截主窗口;composite(默认)= 主窗口 + 打开的浮层 / 对话框合成一张。</summary>
    public string Layer { get; set; } = "composite";

    /// <summary>expect:true = 目标必须不存在(用来断言「浮层已关」之类)。</summary>
    public bool Absent { get; set; }

    /// <summary>可选的人话说明(写进结果,方便对照效果图)。</summary>
    public string Note { get; set; } = "";

    /// <summary>原始 JSON(写进结果里,便于复查)。</summary>
    public string Raw { get; set; } = "";
}

/// <summary>按 Name / 文字 / 提示 / 占位 / 类型找控件的条件。</summary>
internal sealed class TourSelector
{
    public string Name { get; set; } = "";

    public string Text { get; set; } = "";

    public string Contains { get; set; } = "";

    public string Tip { get; set; } = "";

    public string Watermark { get; set; } = "";

    public string Type { get; set; } = "";

    /// <summary>只在 Name 为此值的祖先控件里找(给「没有文字也没有提示的图标钮」用,如 within=PageHost + type=Button)。</summary>
    public string Within { get; set; } = "";

    public int Index { get; set; }

    /// <summary>至少给了一个匹配条件(只有 index 不算)。</summary>
    public bool IsEmpty => Name.Length == 0 && Text.Length == 0 && Contains.Length == 0
        && Tip.Length == 0 && Watermark.Length == 0 && Type.Length == 0;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Name.Length > 0) parts.Add("name=" + Name);
        if (Text.Length > 0) parts.Add("text=" + Text);
        if (Contains.Length > 0) parts.Add("contains=" + Contains);
        if (Tip.Length > 0) parts.Add("tip=" + Tip);
        if (Watermark.Length > 0) parts.Add("watermark=" + Watermark);
        if (Type.Length > 0) parts.Add("type=" + Type);
        if (Within.Length > 0) parts.Add("within=" + Within);
        if (Index != 0) parts.Add("index=" + Index.ToString(CultureInfo.InvariantCulture));
        return string.Join(" ", parts);
    }
}

/// <summary>解析后的剧本。</summary>
internal sealed class TourScript
{
    public double Width { get; set; } = 900;

    public double Height { get; set; } = 550;

    public string Theme { get; set; } = "light";

    public bool Fixture { get; set; }

    public int SettleMs { get; set; } = 300;

    public List<TourStep> Steps { get; } = new();

    /// <summary>解析时发现的问题(未知 op、缺字段……)。不为空也照样返回剧本:坏步骤在运行时记 FAIL。</summary>
    public List<string> Problems { get; } = new();
}

/// <summary>剧本解析。</summary>
internal static class TourScriptParser
{
    /// <summary>支持的动作。</summary>
    public static readonly string[] Ops =
    {
        "navigate", "click", "rightclick", "dblclick", "hover", "key", "type", "scroll", "resize",
        "theme", "wait", "shot", "expect", "close", "dump", "reset",
    };

    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>解析剧本文本;JSON 本身坏掉时抛 <see cref="FormatException"/>(人话原因)。</summary>
    public static TourScript Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, Options);
        }
        catch (JsonException ex)
        {
            throw new FormatException("剧本不是合法 JSON: " + ex.Message, ex);
        }
        using (doc)
        {
            var script = new TourScript();
            JsonElement root = doc.RootElement;
            JsonElement stepsEl;
            if (root.ValueKind == JsonValueKind.Array)
            {
                stepsEl = root;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                script.Width = Num(root, "width", script.Width);
                script.Height = Num(root, "height", script.Height);
                script.Theme = Str(root, "theme", script.Theme);
                script.Fixture = Bool(root, "fixture", false);
                script.SettleMs = (int)Num(root, "settleMs", script.SettleMs);
                if (!root.TryGetProperty("steps", out stepsEl) || stepsEl.ValueKind != JsonValueKind.Array)
                {
                    throw new FormatException("剧本缺少 steps 数组");
                }
            }
            else
            {
                throw new FormatException("剧本顶层必须是对象或数组");
            }

            int i = 0;
            foreach (JsonElement el in stepsEl.EnumerateArray())
            {
                TourStep step = ParseStep(el, i, script.Problems);
                script.Steps.Add(step);
                i++;
            }
            return script;
        }
    }

    private static TourStep ParseStep(JsonElement el, int index, List<string> problems)
    {
        var step = new TourStep { Index = index, Raw = el.GetRawText() };
        if (el.ValueKind != JsonValueKind.Object)
        {
            step.Op = "";
            problems.Add($"第 {index} 步不是对象");
            return step;
        }
        step.Op = Str(el, "op", "").Trim().ToLowerInvariant();
        step.Note = Str(el, "note", "");
        step.Ms = (int)Num(el, "ms", -1);
        if (Array.IndexOf(Ops, step.Op) < 0)
        {
            problems.Add($"第 {index} 步:不认识的动作「{step.Op}」");
            return step;
        }
        switch (step.Op)
        {
            case "navigate":
                step.Page = Str(el, "page", "");
                if (step.Page.Length == 0) problems.Add($"第 {index} 步 navigate 缺 page");
                break;
            case "click":
            case "rightclick":
            case "dblclick":
            case "hover":
            case "expect":
                step.Target = Selector(el);
                step.Absent = Bool(el, "absent", false);
                if (step.Target.IsEmpty) problems.Add($"第 {index} 步 {step.Op} 没给目标(name/text/contains/tip/watermark/type)");
                break;
            case "key":
                step.Keys = Str(el, "keys", "");
                if (step.Keys.Length == 0) problems.Add($"第 {index} 步 key 缺 keys");
                break;
            case "type":
                step.Text = Str(el, "text", "");
                if (el.TryGetProperty("into", out JsonElement into) && into.ValueKind == JsonValueKind.Object)
                {
                    step.Target = Selector(into);
                }
                break;
            case "scroll":
                step.Screens = Num(el, "screens", 0);
                step.ScrollTop = Bool(el, "top", false);
                if (el.TryGetProperty("to", out JsonElement to) && to.ValueKind == JsonValueKind.Object)
                {
                    step.ScrollTo = Selector(to);
                }
                if (el.TryGetProperty("in", out JsonElement inEl) && inEl.ValueKind == JsonValueKind.Object)
                {
                    step.Target = Selector(inEl);
                }
                if (step.ScrollTo == null && step.Screens == 0 && !step.ScrollTop)
                    problems.Add($"第 {index} 步 scroll 既没给 to 也没给 screens / top");
                break;
            case "resize":
                step.Width = Num(el, "width", 0);
                step.Height = Num(el, "height", 0);
                if (step.Width <= 0 || step.Height <= 0) problems.Add($"第 {index} 步 resize 宽高必须为正");
                break;
            case "theme":
                step.Theme = Str(el, "value", "").ToLowerInvariant();
                if (step.Theme != "light" && step.Theme != "dark") problems.Add($"第 {index} 步 theme 只认 light / dark");
                break;
            case "wait":
                if (step.Ms < 0) step.Ms = 300;
                break;
            case "shot":
            case "dump":
                step.Name = Str(el, "name", "");
                step.Layer = Str(el, "layer", "composite").ToLowerInvariant();
                if (step.Name.Length == 0) problems.Add($"第 {index} 步 {step.Op} 缺 name");
                else if (step.Name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
                    problems.Add($"第 {index} 步 {step.Op} 的 name 含路径字符");
                break;
        }
        return step;
    }

    /// <summary>从对象里取选择器字段。</summary>
    internal static TourSelector Selector(JsonElement el) => new()
    {
        Name = Str(el, "name", ""),
        Text = Str(el, "text", ""),
        Contains = Str(el, "contains", ""),
        Tip = Str(el, "tip", ""),
        Watermark = Str(el, "watermark", ""),
        Type = Str(el, "type", ""),
        Within = Str(el, "within", ""),
        Index = (int)Num(el, "index", 0),
    };

    private static string Str(JsonElement el, string key, string dflt)
    {
        if (el.TryGetProperty(key, out JsonElement v))
        {
            if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? dflt;
            if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
        }
        return dflt;
    }

    private static double Num(JsonElement el, string key, double dflt)
    {
        if (el.TryGetProperty(key, out JsonElement v))
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d)) return d;
            if (v.ValueKind == JsonValueKind.String
                && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double s)) return s;
        }
        return dflt;
    }

    private static bool Bool(JsonElement el, string key, bool dflt)
    {
        if (el.TryGetProperty(key, out JsonElement v))
        {
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
        }
        return dflt;
    }

    /// <summary>
    /// 组合键串 → (主键名, Ctrl, Shift, Alt, Meta)。"Ctrl+Shift+Z"、"Esc"、"ctrl+y" 都认;
    /// 主键名归一成 Avalonia <c>Key</c> 枚举的写法(Esc→Escape、Del→Delete、数字→D0..D9、Enter→Return 的别名保留 Enter)。
    /// 无法识别时主键名为空串。
    /// </summary>
    public static (string Key, bool Ctrl, bool Shift, bool Alt, bool Meta) ParseKeys(string keys)
    {
        bool ctrl = false, shift = false, alt = false, meta = false;
        string main = "";
        foreach (string raw in (keys ?? "").Split('+'))
        {
            string part = raw.Trim();
            if (part.Length == 0) continue;
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    ctrl = true;
                    continue;
                case "shift":
                    shift = true;
                    continue;
                case "alt":
                    alt = true;
                    continue;
                case "meta":
                case "win":
                case "cmd":
                    meta = true;
                    continue;
            }
            main = NormalizeKey(part);
        }
        return (main, ctrl, shift, alt, meta);
    }

    private static string NormalizeKey(string part)
    {
        string p = part.Trim();
        switch (p.ToLowerInvariant())
        {
            case "esc":
            case "escape":
                return "Escape";
            case "del":
            case "delete":
                return "Delete";
            case "enter":
            case "return":
                return "Enter";
            case "space":
                return "Space";
            case "tab":
                return "Tab";
            case "up":
                return "Up";
            case "down":
                return "Down";
            case "left":
                return "Left";
            case "right":
                return "Right";
            case "pgdn":
            case "pagedown":
                return "PageDown";
            case "pgup":
            case "pageup":
                return "PageUp";
            case "home":
                return "Home";
            case "end":
                return "End";
            case "backspace":
            case "back":
                return "Back";
        }
        if (p.Length == 1 && char.IsLetter(p[0])) return p.ToUpperInvariant();
        if (p.Length == 1 && char.IsDigit(p[0])) return "D" + p;
        if (p.Length >= 2 && (p[0] == 'F' || p[0] == 'f') && int.TryParse(p.Substring(1), out int f) && f >= 1 && f <= 24)
            return "F" + f.ToString(CultureInfo.InvariantCulture);
        return p;
    }
}
