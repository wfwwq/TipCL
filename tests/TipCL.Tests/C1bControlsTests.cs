using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TipCL.Themes;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C1b:Windows 版 <c>Themes\Controls.xaml</c> -> mac 版 <c>Themes\Controls.axaml</c> 的移植核对
/// (XML 文本层,不需要 Avalonia 平台)。
///
/// 期望值来源分三种,每条用例的注释里都写明是哪一种:
/// - **Windows 源**:<c>D:\项目\tipcl\windos\tipcl\src\TipCL\Themes\Controls.xaml</c>(1055 行、64 个 x:Key),
///   本包开工时逐个抄到 <see cref="PinnedWindowsKeys"/> 里钉死,所以用例**不依赖**那条路径;
///   路径可用时另有一条交叉复核用例(不可用只打印一行原因,不判失败);
/// - **任务书**:<c>GOAL.md</c> 第 3 节的 mac 字体列表、以及 C1b 工作包的类名映射规则;
/// - **本包产物**:<c>src\TipCL.App\Themes\Controls.axaml</c> 与 <c>StyleKeys.cs</c> 的实际字节。
/// </summary>
public class C1bControlsTests
{
    /// <summary>mac 侧控件样式表。</summary>
    internal static string ControlsPath => C1aFixtures.RepoPath("src", "TipCL.App", "Themes", "Controls.axaml");

    /// <summary>mac 侧控件样式表的原始文本。</summary>
    internal static string ControlsText => File.ReadAllText(ControlsPath);

    /// <summary>
    /// 去掉 XML 注释后的正文 —— 判断"WPF 残留语法"时必须只看标记,不然差异说明里的
    /// 那些注释(它们故意提到了 FocusVisualStyle 之类)会把自己判红。
    /// </summary>
    internal static string ControlsMarkup => Regex.Replace(ControlsText, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    /// <summary>
    /// Windows 版 Controls.xaml 的 64 个 x:Key,按源文件书写顺序(期望值的权威副本)。
    /// 资源令牌 9 个 + 具名样式 40 个 + ControlTemplate 1 个 + 隐式类型样式 14 个。
    /// </summary>
    internal static readonly string[] PinnedWindowsKeys =
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
        "TextBlock",
        "T.H1",
        "T.PageTitle",
        "T.GroupTitle",
        "T.H2",
        "T.H3",
        "T.Muted",
        "T.Small",
        "T.Mono",
        "Card",
        "Seg.Rail",
        "ScrollThumb",
        "ScrollBar",
        "BtnTpl",
        "Btn.Base",
        "Btn.Primary",
        "Btn.Soft",
        "Btn.Normal",
        "Btn.Ghost",
        "Btn.Danger",
        "Btn.Icon",
        "Btn.Chip",
        "CheckBox",
        "RadioButton",
        "ToggleButton",
        "ListBox",
        "ListBoxItem",
        "Label",
        "Expander",
        "GroupBox",
        "TabItem",
        "Btn.Caption",
        "Btn.CaptionClose",
        "Btn.TitleIcon",
        "Btn.TitleChip",
        "Ico.Line",
        "Ico.Caption",
        "TabItemBtn",
        "Tab.Top",
        "Seg.Item",
        "Input",
        "Input.Multi",
        "Input.Log",
        "Pw",
        "ComboItem",
        "Combo",
        "Chk",
        "Switch",
        "Sld",
        "Prog",
        "ListItemRow",
        "PlainList",
        "ToolTip",
        "ContextMenu",
        "MenuItem",
    };

    [Fact]
    public void Controls_axaml_exists_in_the_repository()
    {
        Assert.True(File.Exists(ControlsPath), "missing Controls.axaml: " + ControlsPath);
    }

    [Fact]
    public void Controls_axaml_parses_as_xml_and_is_a_styles_root()
    {
        XDocument doc = XDocument.Load(ControlsPath);
        Assert.NotNull(doc.Root);
        Assert.Equal("Styles", doc.Root!.Name.LocalName);
        Assert.NotEmpty(doc.Root.Elements());
    }

    [Fact]
    public void Controls_axaml_carries_a_resource_dictionary_with_the_tokens()
    {
        XDocument doc = XDocument.Load(ControlsPath);
        Assert.NotNull(doc.Root);
        XElement? resources = doc.Root!.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith("Resources", StringComparison.Ordinal));
        Assert.NotNull(resources);
        XElement? dict = resources!.Elements().FirstOrDefault(e => e.Name.LocalName == "ResourceDictionary");
        Assert.NotNull(dict);
        var keys = dict!.Elements().Select(e => e.Attribute(C1bControlsTests.XNameSpace + "Key")?.Value).Where(k => k is not null).ToList();
        foreach (string token in StyleKeys.ResourceKeys)
        {
            Assert.Contains(token, keys, StringComparer.Ordinal);
        }
    }

    internal static readonly XNamespace XNameSpace = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// 交叉复核:Windows 源文件在的时候,把它的 x:Key 集合与钉死的 64 个键对拍
    /// (隐式样式在源文件里写成 <c>{x:Type X}</c>,这里统一归一化成类型名)。
    /// 路径不可用就打印原因后返回,不判失败 —— 用例不许依赖那条路径。
    /// </summary>
    [Fact]
    public void Pinned_key_list_matches_the_windows_source_when_it_is_reachable()
    {
        string? path = C1aFixtures.WindowsPathOrNull("src", "TipCL", "Themes", "Controls.xaml");
        if (path is null)
        {
            Console.Out.WriteLine("Windows 源不可用,跳过交叉复核: " + C1aFixtures.WindowsRoot);
            return;
        }

        var actual = new List<string>();
        foreach (XElement element in XDocument.Load(path).Root!.Elements())
        {
            string? key = element.Attribute(XNameSpace + "Key")?.Value;
            if (key is null)
            {
                continue;
            }

            Match implicitType = Regex.Match(key, @"^\{x:Type\s+([A-Za-z0-9_.]+)\}$");
            actual.Add(implicitType.Success ? implicitType.Groups[1].Value : key);
        }

        Assert.Equal(PinnedWindowsKeys.ToArray(), actual.ToArray());
    }

    /// <summary>资源令牌键:mac 侧必须有一模一样的 x:Key(期望值来自 Windows 源)。</summary>
    [Theory]
    [MemberData(nameof(ResourceKeyRows))]
    public void Resource_token_keys_exist_in_the_mac_dictionary(string key)
    {
        Assert.Contains("x:Key=\"" + key + "\"", ControlsText, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> ResourceKeyRows()
    {
        return StyleKeys.ResourceKeys.Select(k => new object[] { k });
    }

    /// <summary>
    /// 具名样式键:每个 WPF 键都要在 StyleKeys 的映射表里,并且 mac 样式表里要有
    /// <c>&lt;!-- 键 -&gt; .类名 --&gt;</c> 注释与一条命中该类的选择器。
    /// 类名规则(任务书):小写、点换连字符。
    /// </summary>
    [Theory]
    [MemberData(nameof(ClassMapRows))]
    public void Keyed_style_keys_are_mapped_and_documented_in_the_axaml(string wpfKey, string token)
    {
        Assert.False(string.IsNullOrEmpty(token));
        Assert.Equal(token.ToLowerInvariant(), token);
        Assert.DoesNotContain(".", token, StringComparison.Ordinal);
        Assert.DoesNotContain(" ", token, StringComparison.Ordinal);
        Assert.Contains("<!-- " + wpfKey + " -> ." + token + " -->", ControlsText, StringComparison.Ordinal);

        var selector = new Regex("Selector=\"[^\"]*\\." + Regex.Escape(token) + "([\\s\",:/]|$)", RegexOptions.None);
        Assert.True(selector.IsMatch(ControlsMarkup), "样式表里找不到命中 ." + token + " 的选择器(键 " + wpfKey + ")");
    }

    public static IEnumerable<object[]> ClassMapRows()
    {
        return StyleKeys.Table.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                              .Select(pair => new object[] { pair.Key, pair.Value });
    }

    /// <summary>隐式类型样式:WPF 的 <c>x:Key="{x:Type X}"</c> 在 mac 侧是类型选择器。</summary>
    [Theory]
    [MemberData(nameof(ImplicitTypeRows))]
    public void Implicit_type_keys_have_a_type_selector(string wpfKey, string typeName)
    {
        Assert.Contains("Selector=\"" + typeName + "\"", ControlsMarkup, StringComparison.Ordinal);
        Assert.Equal(typeName, StyleKeys.ImplicitTypes[wpfKey]);
    }

    public static IEnumerable<object[]> ImplicitTypeRows()
    {
        return StyleKeys.ImplicitTypes.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                                      .Select(pair => new object[] { pair.Key, pair.Value });
    }

    /// <summary>
    /// GroupBox 是唯一没有对应物的键:Avalonia 11 没有 GroupBox 控件(本机反射确认),
    /// Windows 版全仓库也没有使用点(grep 确认),所以只留档不移植(见 StyleKeys.Unsupported)。
    /// </summary>
    [Fact]
    public void GroupBox_is_recorded_as_unsupported_with_a_reason()
    {
        Assert.True(StyleKeys.Unsupported.ContainsKey("GroupBox"));
        Assert.False(string.IsNullOrEmpty(StyleKeys.Unsupported["GroupBox"]));
        Assert.False(StyleKeys.ImplicitTypes.ContainsKey("GroupBox"));
        Assert.DoesNotContain("GroupBox", ControlsMarkup, StringComparison.Ordinal);
    }

    /// <summary>
    /// 完整性:钉死的 64 个 Windows 键逐个在 mac 侧有落点 —— 资源令牌要有 x:Key、
    /// 具名样式要在映射表里(连带注释与选择器)、隐式样式要有类型选择器、BtnTpl 要有 ControlTheme。
    /// </summary>
    [Theory]
    [MemberData(nameof(AllPinnedKeyRows))]
    public void Every_windows_key_has_a_counterpart(string wpfKey)
    {
        if (StyleKeys.ResourceKeys.Contains(wpfKey, StringComparer.Ordinal))
        {
            Assert.Contains("x:Key=\"" + wpfKey + "\"", ControlsText, StringComparison.Ordinal);
            return;
        }

        if (StyleKeys.ImplicitTypes.TryGetValue(wpfKey, out string? typeName))
        {
            Assert.Contains("Selector=\"" + typeName + "\"", ControlsMarkup, StringComparison.Ordinal);
            return;
        }

        if (StyleKeys.Templates.TryGetValue(wpfKey, out string? themeKey))
        {
            Assert.Contains("x:Key=\"" + themeKey + "\"", ControlsMarkup, StringComparison.Ordinal);
            return;
        }

        if (StyleKeys.Unsupported.ContainsKey(wpfKey))
        {
            return;
        }

        string token = StyleKeys.Map(wpfKey);
        Assert.False(string.IsNullOrEmpty(token), "映射表里没有键 " + wpfKey);
        Assert.Contains("<!-- " + wpfKey + " -> ." + token + " -->", ControlsText, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> AllPinnedKeyRows()
    {
        return PinnedWindowsKeys.Select(k => new object[] { k });
    }

    [Fact]
    public void Pinned_key_list_has_no_duplicates_and_covers_every_table_entry()
    {
        Assert.Equal(PinnedWindowsKeys.Length, PinnedWindowsKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(64, PinnedWindowsKeys.Length);

        foreach (string key in StyleKeys.Table.Keys)
        {
            Assert.Contains(key, PinnedWindowsKeys, StringComparer.Ordinal);
        }

        foreach (string key in StyleKeys.ImplicitTypes.Keys)
        {
            Assert.Contains(key, PinnedWindowsKeys, StringComparer.Ordinal);
        }

        foreach (string key in StyleKeys.Templates.Keys)
        {
            Assert.Contains(key, PinnedWindowsKeys, StringComparer.Ordinal);
        }

        foreach (string key in StyleKeys.Unsupported.Keys)
        {
            Assert.Contains(key, PinnedWindowsKeys, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// 色板引用防呆:样式表里出现的每一个 <c>{DynamicResource B.*}</c> / <c>{StaticResource C.*}</c>
    /// 都必须在浅色与深色色板里都存在(期望值来自 mac 的两份色板文件本身,穷尽扫描 axaml 文本)。
    /// </summary>
    [Theory]
    [MemberData(nameof(PaletteReferenceRows))]
    public void Palette_references_exist_in_both_palette_dictionaries(string brushKey)
    {
        foreach (string file in new[] { "Palette.Light.axaml", "Palette.Dark.axaml" })
        {
            string path = C1aFixtures.RepoPath("src", "TipCL.App", "Themes", file);
            Assert.Contains(brushKey, C1aFixtures.PaletteKeys(path), StringComparer.Ordinal);
        }
    }

    public static IEnumerable<object[]> PaletteReferenceRows()
    {
        var keys = new List<string>();
        foreach (Match match in Regex.Matches(ControlsText, @"\{(?:Dynamic|Static)Resource\s+([A-Za-z][A-Za-z0-9_.]*)\}"))
        {
            string key = match.Groups[1].Value;
            if ((key.StartsWith("B.", StringComparison.Ordinal) || key.StartsWith("C.", StringComparison.Ordinal)) &&
                !keys.Contains(key, StringComparer.Ordinal))
            {
                keys.Add(key);
            }
        }

        Assert.NotEmpty(keys);
        return keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => new object[] { k });
    }

    /// <summary>
    /// 字体令牌:必须用 GOAL.md 第 3 节的 mac 字体列表(它覆盖 Windows 的写法);
    /// 图标字体必须指向 C1a 的 avares 字体族,并把 Windows 的两个兜底字体族折在同一行里
    /// (任务书要求;逗号列表能否解析由运行时用例证明)。
    /// </summary>
    [Fact]
    public void Font_tokens_follow_the_mac_goal_lists()
    {
        Assert.Contains("<FontFamily x:Key=\"F.Ui\">PingFang SC, Hiragino Sans GB, Microsoft YaHei UI, sans-serif</FontFamily>", ControlsText, StringComparison.Ordinal);
        Assert.Contains("<FontFamily x:Key=\"F.Mono\">Menlo, Consolas, monospace</FontFamily>", ControlsText, StringComparison.Ordinal);
        Assert.Contains("avares://TipCL/Resources/fonts/TipCLIcons.ttf#TipCL Icons", ControlsText, StringComparison.Ordinal);
        Assert.Contains("Segoe Fluent Icons", ControlsText, StringComparison.Ordinal);
        Assert.Contains("Segoe MDL2 Assets", ControlsText, StringComparison.Ordinal);

        // Windows 的字体列表不许留在 mac 侧(任务书:mac 字体列表覆盖 Windows 的)
        Assert.DoesNotContain("Microsoft YaHei UI, Microsoft YaHei, Segoe UI", ControlsText, StringComparison.Ordinal);
        Assert.DoesNotContain("Cascadia Mono", ControlsText, StringComparison.Ordinal);
    }

    /// <summary>圆角令牌:12 / 8 / 999(期望值来自 Windows 源)。</summary>
    [Theory]
    [InlineData("CR.Card", 12)]
    [InlineData("CR.Ctl", 8)]
    [InlineData("CR.Pill", 999)]
    public void Corner_radius_tokens_keep_windows_values(string key, int expected)
    {
        XElement element = ElementWithKey(key);
        Assert.Equal("CornerRadius", element.Name.LocalName);
        Assert.Equal(expected, int.Parse(element.Value.Trim(), CultureInfo.InvariantCulture));
    }

    /// <summary>时长令牌:130ms / 220ms(期望值来自 Windows 源的 Duration "0:0:0.13" / "0:0:0.22")。</summary>
    [Theory]
    [InlineData("D.Fast", "0:0:0.13")]
    [InlineData("D.Med", "0:0:0.22")]
    public void Duration_tokens_keep_windows_values(string key, string expected)
    {
        XElement element = ElementWithKey(key);
        Assert.Equal("TimeSpan", element.Name.LocalName);
        Assert.Equal(TimeSpan.Parse(expected, CultureInfo.InvariantCulture), TimeSpan.Parse(element.Value.Trim(), CultureInfo.InvariantCulture));
    }

    /// <summary>E.Out:WPF 是 CubicEase + EasingMode=EaseOut,Avalonia 等价物是 CubicEaseOut。</summary>
    [Fact]
    public void Ease_out_token_is_a_cubic_ease_out()
    {
        XElement element = ElementWithKey("E.Out");
        Assert.Equal("CubicEaseOut", element.Name.LocalName);
    }

    private static XElement ElementWithKey(string key)
    {
        XDocument doc = XDocument.Load(ControlsPath);
        XElement? element = doc.Root!
            .Descendants()
            .FirstOrDefault(e => e.Attribute(XNameSpace + "Key")?.Value == key);
        Assert.NotNull(element);
        return element!;
    }

    /// <summary>
    /// 不许留 WPF 语法(机械移植不彻底的信号)。只看标记、不看注释。
    /// 注意 <c>xmlns:x</c> 本身必须留在 XAML 命名空间里,所以禁的是 WPF 的表现层命名空间。
    /// </summary>
    [Theory]
    [InlineData("http://schemas.microsoft.com/winfx/2006/xaml/presentation")]
    [InlineData("{x:Type")]
    [InlineData("ControlTemplate.Triggers")]
    [InlineData("OverridesDefaultStyle")]
    [InlineData("SnapsToDevicePixels")]
    [InlineData("FocusVisualStyle")]
    [InlineData("TextOptions.")]
    [InlineData("TextElement.")]
    [InlineData("Storyboard")]
    [InlineData("BeginStoryboard")]
    [InlineData("DropShadowEffect")]
    [InlineData("x:Name=")]
    [InlineData("BasedOn=\"{StaticResource {x:Type")]
    public void Controls_axaml_has_no_wpf_leftovers(string forbidden)
    {
        Assert.DoesNotContain(forbidden, ControlsMarkup, StringComparison.Ordinal);
    }

    /// <summary>模板键 BtnTpl 落在 ControlTheme 上(StyleKeys.Templates 的唯一一条)。</summary>
    [Fact]
    public void Button_template_is_a_control_theme_shared_by_all_variants()
    {
        Assert.Equal("BtnBase", StyleKeys.Templates["BtnTpl"]);
        Assert.Contains("<ControlTheme x:Key=\"BtnBase\" TargetType=\"Button\">", ControlsMarkup, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"BtnCaptionClose\" TargetType=\"Button\" BasedOn=\"{StaticResource BtnCaption}\"", ControlsMarkup, StringComparison.Ordinal);
    }
}
