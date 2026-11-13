using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;

namespace TipCL.Themes;

/// <summary>
/// C1b:WPF 样式键 -> Avalonia 类名的唯一映射表。
///
/// 为什么需要它:Windows 版是 WPF,每个控件显式写
/// <c>Style="{StaticResource Btn.Primary}"</c>;Avalonia 没有"按键取样式"这套机制,
/// 样式靠**选择器**命中控件。所以 C1b 把每个 WPF 键固定翻译成一个**类名**(小写、点换连字符),
/// 页面/C1c 的门面只往 <c>Classes</c> 里加这些类名,<c>Controls.axaml</c> 里用同名的类选择器接住。
///
/// 三张表 + 一张清单:
/// <list type="bullet">
/// <item><see cref="Table"/>:具名样式键(如 <c>Btn.Primary</c> -> <c>btn-primary</c>),41 个里去掉模板键 BtnTpl,
/// 共 40 个;</item>
/// <item><see cref="Templates"/>:WPF 里是 ControlTemplate 的键(BtnTpl -> Controls.axaml 的 ControlTheme BtnBase);</item>
/// <item><see cref="ImplicitTypes"/>:WPF 的隐式样式(<c>x:Key="{x:Type X}"</c>)-> Avalonia 的类型选择器(不需要类名);</item>
/// <item><see cref="ResourceKeys"/>:不是样式而是资源令牌的键(F.* / CR.* / D.* / E.*)。</item>
/// </list>
///
/// 每一条都在 <c>Controls.axaml</c> 里有对应物,并且 axaml 里每条选择器上方都写着
/// <c>&lt;!-- WPF键 -&gt; .类名 --&gt;</c> 形式的注释,测试按这四张表逐项核对(见
/// tests\TipCL.Tests\Ported\ControlsPortTests.cs)。
/// </summary>
internal static class StyleKeys
{
    /// <summary>具名样式键 -> Avalonia 类名(40 个)。键即 Windows 版 Controls.xaml 的 x:Key。</summary>
    internal static readonly IReadOnlyDictionary<string, string> Table = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["T.H1"] = "t-h1",
        ["T.PageTitle"] = "t-page-title",
        ["T.GroupTitle"] = "t-group-title",
        ["T.H2"] = "t-h2",
        ["T.H3"] = "t-h3",
        ["T.Muted"] = "t-muted",
        ["T.Small"] = "t-small",
        ["T.Mono"] = "t-mono",
        ["Card"] = "card",
        ["Seg.Rail"] = "seg-rail",
        ["ScrollThumb"] = "scroll-thumb",
        ["Btn.Base"] = "btn-base",
        ["Btn.Primary"] = "btn-primary",
        ["Btn.Soft"] = "btn-soft",
        ["Btn.Normal"] = "btn-normal",
        ["Btn.Ghost"] = "btn-ghost",
        ["Btn.Danger"] = "btn-danger",
        ["Btn.Icon"] = "btn-icon",
        ["Btn.Chip"] = "btn-chip",
        ["Btn.Caption"] = "btn-caption",
        ["Btn.CaptionClose"] = "btn-caption-close",
        ["Btn.TitleIcon"] = "btn-title-icon",
        ["Btn.TitleChip"] = "btn-title-chip",
        ["Ico.Line"] = "ico-line",
        ["Ico.Caption"] = "ico-caption",
        ["TabItemBtn"] = "tab-item-btn",
        ["Tab.Top"] = "tab-top",
        ["Seg.Item"] = "seg-item",
        ["Input"] = "input",
        ["Input.Multi"] = "input-multi",
        ["Input.Log"] = "input-log",
        ["Pw"] = "pw",
        ["ComboItem"] = "combo-item",
        ["Combo"] = "combo",
        ["Chk"] = "chk",
        ["Switch"] = "switch",
        ["Sld"] = "sld",
        ["Prog"] = "prog",
        ["ListItemRow"] = "list-item-row",
        ["PlainList"] = "plain-list",
    };

    /// <summary>
    /// WPF 的 ControlTemplate 键(不是 Style,没有类名):BtnTpl -> Controls.axaml 里
    /// 那个给全部按钮变体共用的 ControlTheme(Btn.Base 的模板)。
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["BtnTpl"] = "BtnBase",
    };

    /// <summary>
    /// WPF 的隐式样式键(<c>x:Key="{x:Type X}"</c>)-> Avalonia 的类型选择器。
    /// 值是"选择器里写的类型名",不是类名;<see cref="Map"/> 对它们返回空串。
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> ImplicitTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["TextBlock"] = "TextBlock",
        ["ScrollBar"] = "ScrollBar",
        ["CheckBox"] = "CheckBox",
        ["RadioButton"] = "RadioButton",
        ["ToggleButton"] = "ToggleButton",
        ["ListBox"] = "ListBox",
        ["ListBoxItem"] = "ListBoxItem",
        ["Label"] = "Label",
        ["Expander"] = "Expander",
        ["TabItem"] = "TabItem",
        ["ToolTip"] = "ToolTip",
        ["ContextMenu"] = "ContextMenu",
        ["MenuItem"] = "MenuItem",
    };

    /// <summary>
    /// Windows 版有、但 Avalonia 11 **没有对应控件**的隐式样式键。目前只有 GroupBox:
    /// Avalonia 11.3.23 里不存在 GroupBox 类型(反射确认),而 Windows 版全仓库也没有任何 GroupBox
    /// 使用点(grep 确认),所以这一条不移植,只在这里留档,别在 axaml 里造一个假控件。
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> Unsupported = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["GroupBox"] = "Avalonia 11 没有 GroupBox 控件;Windows 版也没有使用点",
    };

    /// <summary>资源令牌键(F.Icon / CR.Card / D.Fast / E.Out ...),顺序照 Windows 版的书写顺序。</summary>
    internal static readonly IReadOnlyList<string> ResourceKeys = new[]
    {
        "F.Icon",
        "F.Ui",
        "F.Mono",
        "CR.Card",
        "CR.Ctl",
        "CR.Pill",
        "D.Fast",
        "D.Med",
        "E.Out",
    };

    /// <summary>
    /// WPF 键 -> 类名。具名样式键命中直接返回;令牌、模板键、隐式样式键返回空串
    /// (那些没有类名,见对应表)。两边都不认识也返回空串 —— 调用点不该因为键名写错就崩,
    /// 与 Palette 的兜底同一套做法(见 D-68)。
    /// </summary>
    internal static string Map(string? wpfKey)
    {
        return wpfKey is not null && Table.TryGetValue(wpfKey, out string? token) ? token : string.Empty;
    }

    /// <summary>
    /// 把若干 WPF 键拼成一串 Avalonia 类名(去重、保序、空格分隔),可直接赋给 <c>Control.Classes</c>:
    /// <c>control.Classes.AddRange(StyleKeys.Split(className));</c>。认不出的键被忽略。
    /// </summary>
    internal static string Classes(IEnumerable<string>? wpfKeys)
    {
        if (wpfKeys is null)
        {
            return string.Empty;
        }

        var tokens = new List<string>();
        foreach (string key in wpfKeys)
        {
            string token = Map(key);
            if (token.Length > 0 && !tokens.Contains(token, StringComparer.Ordinal))
            {
                tokens.Add(token);
            }
        }

        return string.Join(" ", tokens);
    }

    /// <summary>把若干 WPF 键对应的类名加到控件上(C1c 的门面用它,免得到处写字符串)。</summary>
    internal static void Apply(Control control, params string[] wpfKeys)
    {
        if (control is null)
        {
            return;
        }

        foreach (string key in wpfKeys)
        {
            string token = Map(key);
            if (token.Length > 0 && !control.Classes.Contains(token))
            {
                control.Classes.Add(token);
            }
        }
    }

    /// <summary>把 <see cref="Classes"/> 的结果拆成可逐个 Add 的类名。</summary>
    internal static string[] Split(string? classNames)
    {
        return string.IsNullOrEmpty(classNames)
            ? Array.Empty<string>()
            : classNames.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray();
    }
}
