using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using TipCL.Themes;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C1b:WPF 样式键 -> Avalonia 类名的映射表(<c>Themes\StyleKeys.cs</c>)。
///
/// 期望值来源:
/// - **任务书**:类名规则 —— 小写、点换连字符、互不重复;映射表要覆盖每个非隐式 WPF 键;
/// - **Windows 源**:表里出现的键必须真的在 Windows 版 <c>Controls.xaml</c> 里(键集合钉在
///   <see cref="C1bControlsTests.PinnedWindowsKeys"/>);
/// - 具体条目值(如 <c>Btn.Primary -> btn-primary</c>)是本包对任务书规则的逐条应用结果。
/// </summary>
public class C1bStyleKeysTests
{
    /// <summary>Windows 版的具名样式键共 41 个,减去 ControlTemplate(BtnTpl)后 40 个有类名。</summary>
    [Fact]
    public void Map_table_has_forty_entries()
    {
        Assert.Equal(40, StyleKeys.Table.Count);
        Assert.Equal(41, StyleKeys.Table.Count + StyleKeys.Templates.Count);
    }

    [Fact]
    public void Every_class_token_is_lowercase_and_uses_hyphens()
    {
        foreach (KeyValuePair<string, string> pair in StyleKeys.Table)
        {
            Assert.Matches("^[a-z][a-z0-9-]*$", pair.Value);
            Assert.DoesNotContain(".", pair.Value, StringComparison.Ordinal);
            Assert.DoesNotContain(" ", pair.Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_two_windows_keys_share_a_class_token()
    {
        Assert.Equal(StyleKeys.Table.Count, StyleKeys.Table.Values.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void No_two_windows_keys_share_a_class_token_across_tables()
    {
        // 隐式类型样式与资源令牌不参与类名,所以这里只要求 Table 与 Templates 的值互不相同。
        Assert.Empty(StyleKeys.Table.Values.Intersect(StyleKeys.Templates.Values, StringComparer.Ordinal));
    }

    /// <summary>点换连字符的小写规则逐条核对(期望值来自任务书里的映射示例)。</summary>
    [Theory]
    [InlineData("Btn.Primary", "btn-primary")]
    [InlineData("T.PageTitle", "t-page-title")]
    [InlineData("Input.Log", "input-log")]
    [InlineData("PlainList", "plain-list")]
    [InlineData("Seg.Rail", "seg-rail")]
    [InlineData("ListItemRow", "list-item-row")]
    [InlineData("Btn.CaptionClose", "btn-caption-close")]
    [InlineData("Tab.Top", "tab-top")]
    public void Documented_examples_map_as_the_brief_says(string wpfKey, string token)
    {
        Assert.Equal(token, StyleKeys.Map(wpfKey));
    }

    /// <summary>
    /// 映射规则本身:点换连字符 + 驼峰处插连字符 + 全小写,用不熟悉的键也能推出来
    /// (期望值来自任务书规则,示例见上一条用例)。
    /// </summary>
    [Fact]
    public void Map_matches_the_lowercase_hyphen_rule_for_every_entry()
    {
        foreach (KeyValuePair<string, string> pair in StyleKeys.Table)
        {
            string derived = System.Text.RegularExpressions.Regex
                .Replace(pair.Key, "(?<=[a-z0-9])(?=[A-Z])", "-")
                .Replace('.', '-')
                .ToLowerInvariant();
            Assert.Equal(derived, pair.Value);
        }
    }

    [Fact]
    public void Map_returns_empty_for_tokens_templates_and_implicit_types()
    {
        foreach (string token in StyleKeys.ResourceKeys)
        {
            Assert.Equal(string.Empty, StyleKeys.Map(token));
        }

        foreach (string template in StyleKeys.Templates.Keys)
        {
            Assert.Equal(string.Empty, StyleKeys.Map(template));
        }

        foreach (string implicitType in StyleKeys.ImplicitTypes.Keys)
        {
            Assert.Equal(string.Empty, StyleKeys.Map(implicitType));
        }

        Assert.Equal(string.Empty, StyleKeys.Map(null));
        Assert.Equal(string.Empty, StyleKeys.Map(string.Empty));
        Assert.Equal(string.Empty, StyleKeys.Map("No.Such.Key"));
    }

    /// <summary>Classes():去重、保序、忽略认不出的键(键名写错不该崩,与 D-68 的兜底同一套)。</summary>
    [Fact]
    public void Classes_joins_tokens_and_ignores_unknown_keys()
    {
        Assert.Equal("btn-base btn-primary", StyleKeys.Classes(new[] { "Btn.Base", "Btn.Primary" }));
        Assert.Equal("btn-primary", StyleKeys.Classes(new[] { "Btn.Primary", "Btn.Primary" }));
        Assert.Equal(string.Empty, StyleKeys.Classes(Array.Empty<string>()));
        Assert.Equal(string.Empty, StyleKeys.Classes(null));
        Assert.Equal("card", StyleKeys.Classes(new[] { "Nope", "Card", "F.Ui", "GroupBox" }));
    }

    [Fact]
    public void Split_returns_the_tokens_of_a_class_string()
    {
        Assert.Equal(new[] { "btn-base", "btn-primary" }, StyleKeys.Split("btn-base btn-primary"));
        Assert.Empty(StyleKeys.Split(""));
        Assert.Empty(StyleKeys.Split(null));
    }

    /// <summary>Apply():直接把类名加到控件上,重复调用不会重复添加。</summary>
    [Fact]
    public void Apply_adds_the_class_names_to_a_control_once()
    {
        // C1b-fix:构造 Button 要走 Avalonia 会话线程(控件构造会查 Dispatcher,见 AvaloniaSession 的注释)。
        AvaloniaSession.Run(() =>
        {
            var button = new Button();
            StyleKeys.Apply(button, "Btn.Primary", "Btn.Chip");
            StyleKeys.Apply(button, "Btn.Primary");
            Assert.Equal(new[] { "btn-primary", "btn-chip" }, button.Classes.ToArray());
        });
    }

    [Fact]
    public void Apply_tolerates_unknown_keys_and_null_control()
    {
        // C1b-fix:同上,这条也要会话线程。
        AvaloniaSession.Run(() =>
        {
            var button = new Button();
            StyleKeys.Apply(button, "No.Such.Key", "F.Ui");
            Assert.Empty(button.Classes);
            StyleKeys.Apply(null!, "Btn.Primary");
        });
    }

    /// <summary>表里的每个键都必须真的出现在 Windows 源里(钉死的 64 个键之内)。</summary>
    [Fact]
    public void Every_table_key_exists_in_the_pinned_windows_key_list()
    {
        foreach (string key in StyleKeys.Table.Keys
                     .Concat(StyleKeys.Templates.Keys)
                     .Concat(StyleKeys.ImplicitTypes.Keys)
                     .Concat(StyleKeys.ResourceKeys)
                     .Concat(StyleKeys.Unsupported.Keys))
        {
            Assert.Contains(key, C1bControlsTests.PinnedWindowsKeys, StringComparer.Ordinal);
        }
    }

    /// <summary>四张表加上 Unsupported 正好铺满 64 个 Windows 键,一个不漏、一个不多。</summary>
    [Fact]
    public void The_four_tables_partition_the_windows_key_set()
    {
        var all = StyleKeys.Table.Keys
            .Concat(StyleKeys.Templates.Keys)
            .Concat(StyleKeys.ImplicitTypes.Keys)
            .Concat(StyleKeys.ResourceKeys)
            .Concat(StyleKeys.Unsupported.Keys)
            .ToList();

        Assert.Equal(64, all.Count);
        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            C1bControlsTests.PinnedWindowsKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            all.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    /// <summary>资源令牌清单顺序与 Windows 源一致(9 个)。</summary>
    [Fact]
    public void Resource_key_list_keeps_the_windows_order()
    {
        Assert.Equal(
            new[] { "F.Icon", "F.Ui", "F.Mono", "CR.Card", "CR.Ctl", "CR.Pill", "D.Fast", "D.Med", "E.Out" },
            StyleKeys.ResourceKeys.ToArray());
    }
}