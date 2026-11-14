using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using TipCL.Themes;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C1a:两份主题色板(Palette.Light.axaml / Palette.Dark.axaml)的用例。
///
/// 期望值来源:
/// - **Windows 源**:<c>Themes\Palette.Light.xaml</c> / <c>Palette.Dark.xaml</c>,逐项钉在
///   <see cref="C1aFixtures.PinnedLight"/> / <see cref="C1aFixtures.PinnedDark"/> 里;
/// - **Avalonia 实测**:11.3.23 headless + Skia 下 <c>Palette</c> 的解析结果。
/// </summary>
public class C1aPaletteTests
{
    private static string LightPath => C1aFixtures.RepoPath("src", "TipCL.App", "Themes", "Palette.Light.axaml");

    private static string DarkPath => C1aFixtures.RepoPath("src", "TipCL.App", "Themes", "Palette.Dark.axaml");

    private static string Diff(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        var lines = new List<string>();
        lines.Add("count expected=" + expected.Count + " actual=" + actual.Count);
        int n = Math.Max(expected.Count, actual.Count);
        for (int i = 0; i < n; i++)
        {
            string e = i < expected.Count ? expected[i] : "<missing>";
            string a = i < actual.Count ? actual[i] : "<missing>";
            if (!string.Equals(e, a, StringComparison.Ordinal))
            {
                lines.Add("[" + i.ToString(CultureInfo.InvariantCulture) + "] expected " + e + " / actual " + a);
                if (lines.Count > 12)
                {
                    lines.Add("...");
                    break;
                }
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    [Fact]
    public void Both_palette_dictionaries_exist_in_the_repository()
    {
        Assert.True(File.Exists(LightPath), "missing Palette.Light.axaml: " + LightPath);
        Assert.True(File.Exists(DarkPath), "missing Palette.Dark.axaml: " + DarkPath);
    }

    [Fact]
    public void Light_dictionary_parses_as_xml_and_is_a_resource_dictionary()
    {
        XDocument doc = XDocument.Load(LightPath);
        Assert.NotNull(doc.Root);
        Assert.Equal("ResourceDictionary", doc.Root!.Name.LocalName);
        Assert.NotEmpty(doc.Root.Elements());
    }

    [Fact]
    public void Dark_dictionary_parses_as_xml_and_is_a_resource_dictionary()
    {
        XDocument doc = XDocument.Load(DarkPath);
        Assert.NotNull(doc.Root);
        Assert.Equal("ResourceDictionary", doc.Root!.Name.LocalName);
        Assert.NotEmpty(doc.Root.Elements());
    }

    [Fact]
    public void Light_and_dark_declare_exactly_the_same_keys()
    {
        List<string> light = C1aFixtures.PaletteKeys(LightPath);
        List<string> dark = C1aFixtures.PaletteKeys(DarkPath);
        Assert.Equal(light.OrderBy(k => k, StringComparer.Ordinal), dark.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Light_and_dark_declare_the_keys_in_the_same_order()
    {
        Assert.Equal(C1aFixtures.PaletteKeys(LightPath), C1aFixtures.PaletteKeys(DarkPath));
    }

    [Fact]
    public void Palette_key_count_is_61_with_seven_colour_keys()
    {
        List<string> keys = C1aFixtures.PaletteKeys(LightPath);
        Assert.Equal(C1aFixtures.PaletteKeyCount, keys.Count);
        Assert.Equal(7, keys.Count(k => k.StartsWith("C.", StringComparison.Ordinal)));
        Assert.Equal(54, keys.Count(k => k.StartsWith("B.", StringComparison.Ordinal)));
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Light_matches_the_windows_palette_entry_by_entry()
    {
        List<string> actual = C1aFixtures.PaletteEntries(LightPath);
        Assert.True(
            C1aFixtures.PinnedLight.SequenceEqual(actual, StringComparer.Ordinal),
            "Palette.Light.axaml differs from the pinned WPF original:" + Environment.NewLine + Diff(C1aFixtures.PinnedLight, actual));
    }

    [Fact]
    public void Dark_matches_the_windows_palette_entry_by_entry()
    {
        List<string> actual = C1aFixtures.PaletteEntries(DarkPath);
        Assert.True(
            C1aFixtures.PinnedDark.SequenceEqual(actual, StringComparer.Ordinal),
            "Palette.Dark.axaml differs from the pinned WPF original:" + Environment.NewLine + Diff(C1aFixtures.PinnedDark, actual));
    }

    [Fact]
    public void Palette_helper_key_list_matches_the_axaml_key_order()
    {
        Assert.Equal(C1aFixtures.PaletteKeys(LightPath), Palette.Keys.ToList());
        Assert.Equal(C1aFixtures.PinnedKeys(C1aFixtures.PinnedLight), Palette.Keys.ToList());
    }

    [Fact]
    public void Every_light_entry_is_a_colour_a_brush_or_a_gradient()
    {
        foreach (string line in C1aFixtures.PaletteEntries(LightPath))
        {
            (string key, string kind, string[] payload) = C1aFixtures.Split(line);
            switch (kind)
            {
                case "Color":
                    Assert.Single(payload);
                    break;
                case "Brush":
                    Assert.Equal(2, payload.Length);
                    break;
                case "Gradient":
                    // BannerFill / PageWash 是 3 个停靠点,LogoFill 是 2 个(与 WPF 源一致)。
                    Assert.InRange(payload.Length, 2, 3);
                    break;
                default:
                    Assert.Fail("unexpected element for key " + key + ": " + kind);
                    break;
            }
        }
    }

    [Fact]
    public void Every_dark_entry_is_a_colour_a_brush_or_a_gradient()
    {
        foreach (string line in C1aFixtures.PaletteEntries(DarkPath))
        {
            (string key, string kind, string[] payload) = C1aFixtures.Split(line);
            switch (kind)
            {
                case "Color":
                    Assert.Single(payload);
                    break;
                case "Brush":
                    Assert.Equal(2, payload.Length);
                    break;
                case "Gradient":
                    Assert.InRange(payload.Length, 2, 3);
                    break;
                default:
                    Assert.Fail("unexpected element for key " + key + ": " + kind);
                    break;
            }
        }
    }

    [Fact]
    public void Every_colour_value_is_rrggbb_or_aarrggbb_hex()
    {
        var hex = new Regex("^#[0-9A-F]{6}$|^#[0-9A-F]{8}$", RegexOptions.CultureInvariant);
        foreach (string path in new[] { LightPath, DarkPath })
        {
            foreach (string line in C1aFixtures.PaletteEntries(path))
            {
                (string key, string kind, string[] payload) = C1aFixtures.Split(line);
                if (kind == "Gradient")
                {
                    foreach (string stop in payload)
                    {
                        string colour = stop.Split('@')[0];
                        Assert.True(hex.IsMatch(colour), Path.GetFileName(path) + " " + key + " stop colour " + colour);
                    }
                }
                else
                {
                    Assert.True(hex.IsMatch(payload[0]), Path.GetFileName(path) + " " + key + " colour " + payload[0]);
                }
            }
        }
    }

    [Fact]
    public void Every_brush_opacity_is_between_zero_and_one()
    {
        foreach (string path in new[] { LightPath, DarkPath })
        {
            foreach (string line in C1aFixtures.PaletteEntries(path))
            {
                (string key, string kind, string[] payload) = C1aFixtures.Split(line);
                if (kind != "Brush")
                {
                    continue;
                }

                double opacity = double.Parse(payload[1], CultureInfo.InvariantCulture);
                Assert.InRange(opacity, 0.0, 1.0);
                Assert.True(opacity > 0.0, Path.GetFileName(path) + " " + key + " opacity " + opacity);
            }
        }
    }

    [Fact]
    public void Every_gradient_has_two_or_three_stops_in_ascending_offset_order()
    {
        foreach (string path in new[] { LightPath, DarkPath })
        {
            foreach (string line in C1aFixtures.PaletteEntries(path))
            {
                (string key, string kind, string[] payload) = C1aFixtures.Split(line);
                if (kind != "Gradient")
                {
                    continue;
                }

                double[] offsets = payload
                    .Select(stop => double.Parse(stop.Split('@')[1], CultureInfo.InvariantCulture))
                    .ToArray();
                Assert.InRange(offsets.Length, 2, 3);
                Assert.Equal(0.0, offsets[0]);
                Assert.Equal(1.0, offsets[^1]);
                for (int i = 1; i < offsets.Length; i++)
                {
                    Assert.True(offsets[i] > offsets[i - 1], key + " offsets are not ascending");
                }
            }
        }
    }

    [Fact]
    public void Every_gradient_uses_relative_percentage_start_and_end_points()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (string path in new[] { LightPath, DarkPath })
        {
            XDocument doc = XDocument.Load(path);
            foreach (XElement element in doc.Root!.Elements("LinearGradientBrush"))
            {
                Assert.Equal("0%,0%", element.Attribute("StartPoint")?.Value);
                Assert.Equal("100%,100%", element.Attribute("EndPoint")?.Value);
            }
        }
    }

    /// <summary>
    /// **剥注释**(守卫的口径):守卫的意图是"**代码**只查询存在的调色板键",而注释里按文档惯例列举键名
    /// **不是查询** ⇒ 匹配前先剥掉注释。
    ///
    /// 口径(刻意选"够用且不误伤",不追求完备的 C# 词法):**逐行**处理 —— ①先扫描该行,遇到 `"` 就按"字符串"
    /// 走(支持 `\"` 转义)直到闭合,期间不认 `//`;②不在字符串里遇到 `//` 就截到行尾;③`/*` 与 `*/` 用跨行标志
    /// 剥(**行内**出现的 `/*` 也认)。**不处理**逐字字符串(`@"..."` 里的 `""`)与正则字面量里的怪写法 ——
    /// 本项目源码里没有那种用法需要靠注释剥离来救,写清边界以免后来人以为它完备。
    /// </summary>
    internal static string StripComments(string text)
    {
        System.Text.StringBuilder output = new System.Text.StringBuilder(text.Length);
        bool inBlock = false;
        foreach (string rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = rawLine;
            int i = 0;
            bool inString = false;
            while (i < line.Length)
            {
                if (inBlock)
                {
                    int close = line.IndexOf("*/", i, StringComparison.Ordinal);
                    if (close < 0)
                    {
                        i = line.Length;
                        break;
                    }
                    inBlock = false;
                    i = close + 2;
                    continue;
                }
                char c = line[i];
                if (inString)
                {
                    if (c == '\\')
                    {
                        i += 2;
                        continue;
                    }
                    if (c == '"')
                    {
                        inString = false;
                    }
                    i++;
                    continue;
                }
                if (c == '"')
                {
                    inString = true;
                    i++;
                    continue;
                }
                if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
                {
                    line = line.Substring(0, i);
                    break;
                }
                if (c == '/' && i + 1 < line.Length && line[i + 1] == '*')
                {
                    inBlock = true;
                    line = line.Substring(0, i);
                    i = line.Length;
                    break;
                }
                i++;
            }
            output.Append(line).Append('\n');
        }
        return output.ToString();
    }

    /// <summary>
    /// **验证验证者**(①③):注释里的键名**不算查询**;字符串字面量里的 `//` **不能被当成注释**切掉。
    /// </summary>
    [Fact]
    public void Comment_stripping_drops_comments_but_keeps_code_strings()
    {
        // ① 行注释与块注释里的假键名:剥掉后**扫不到**
        string commented = "// 示例键:B.NotARealKey\n/* 另一处示例:B.AlsoFake */\n";
        Assert.DoesNotContain("B.NotARealKey", StripComments(commented));
        Assert.DoesNotContain("B.AlsoFake", StripComments(commented));
        // ② 代码里的键名必须留下(注释只是同一行后半截)
        string mixed = "var key = \"B.Canvas\"; // 说明里提到 B.NotARealKey\n";
        Assert.Contains("\"B.Canvas\"", StripComments(mixed));
        Assert.DoesNotContain("B.NotARealKey", StripComments(mixed));
        // ③ 字符串里的 // 不是注释(url)
        string withUrl = "var u = \"https://example.com/a//b\";\n";
        Assert.Contains("https://example.com/a//b", StripComments(withUrl));
        // ④ 反斜杠转义后的引号不结束字符串(`\"` 之后仍算在字符串里)
        string escaped = "var s = \"a\\\"//b\";\n";
        Assert.Contains("a\\\"//b", StripComments(escaped));
    }

    /// <summary>
    /// **验证验证者**(②):真的有一个"代码里查了、色板里没有"的键 ⇒ 守卫**必须报**
    /// (这条防的是"把守卫改成永远不报")。
    /// </summary>
    [Fact]
    public void A_missing_key_in_code_is_still_reported()
    {
        var pattern = new Regex("\"(?:B|C)\\.[A-Za-z0-9]+\"", RegexOptions.CultureInvariant);
        string code = "var key = \"B.DefinitelyNotInThePalette\";\n" + "// 注释里再放一个:B.AlsoNotThere\n";
        List<string> found = pattern.Matches(StripComments(code)).Select(m => m.Value.Trim('"')).ToList();
        Assert.Contains("B.DefinitelyNotInThePalette", found);
        Assert.DoesNotContain("B.AlsoNotThere", found);
        // 用 DoesNotContain 而不是 Assert.False(...Contains(..., comparer)):语义同一(字符串默认序数比较),
        // 但后者会带出 xUnit2017 警告(守卫修正落地时它贡献了唯一一条新增警告)。
        Assert.DoesNotContain("B.DefinitelyNotInThePalette", Palette.Keys);
    }

    /// <summary>
    /// 移植过来的代码按字符串查色板键(例如 Lucide 的默认 <c>"B.InkMuted"</c>、MainWindow 的
    /// <c>"B.Canvas"</c>)。这里从源码里**扫出所有这类字面量**,逐个要求它在色板里存在
    /// —— 键名写错就是构建得起来、运行时才发现的那种错。
    /// <b>扫之前先剥注释</b>(见 <see cref="StripComments"/>):注释里按文档惯例列的键名不是"查询"。
    /// </summary>
    [Fact]
    public void Ported_code_only_looks_up_palette_keys_that_exist()
    {
        string src = Path.Combine(C1aFixtures.RepoRoot(), "src", "TipCL.App");
        var pattern = new Regex("\"(?:B|C)\\.[A-Za-z0-9]+\"", RegexOptions.CultureInvariant);
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in pattern.Matches(StripComments(File.ReadAllText(file))))
            {
                found.Add(match.Value.Trim('"'));
            }
        }

        // 非空性:至少要扫到本包真正用到的那几个键,否则这条用例什么也没验。
        Assert.Contains("B.Canvas", found);
        Assert.Contains("B.Ink", found);
        Assert.Contains("B.InkMuted", found);
        Assert.Contains("B.AccentDeep", found);

        List<string> missing = found.Where(key => !Palette.Keys.Contains(key, StringComparer.Ordinal)).ToList();
        Assert.True(missing.Count == 0, "keys looked up but not in the palette: " + string.Join(", ", missing));
    }

    [Fact]
    public void Windows_light_palette_source_matches_when_the_baseline_is_available()
    {
        string? source = C1aFixtures.WindowsPathOrNull("src", "TipCL", "Themes", "Palette.Light.xaml");
        if (source is null)
        {
            Console.WriteLine("SKIP-NOTE: windows baseline not available, cross-check not run: " + C1aFixtures.WindowsRoot);
            return;
        }

        List<string> actual = C1aFixtures.PaletteEntries(source);
        Assert.True(
            C1aFixtures.PinnedLight.SequenceEqual(actual, StringComparer.Ordinal),
            "windows Palette.Light.xaml differs from the pinned copy:" + Environment.NewLine +
            Diff(C1aFixtures.PinnedLight, actual));
    }

    [Fact]
    public void Windows_dark_palette_source_matches_when_the_baseline_is_available()
    {
        string? source = C1aFixtures.WindowsPathOrNull("src", "TipCL", "Themes", "Palette.Dark.xaml");
        if (source is null)
        {
            Console.WriteLine("SKIP-NOTE: windows baseline not available, cross-check not run: " + C1aFixtures.WindowsRoot);
            return;
        }

        List<string> actual = C1aFixtures.PaletteEntries(source);
        Assert.True(
            C1aFixtures.PinnedDark.SequenceEqual(actual, StringComparer.Ordinal),
            "windows Palette.Dark.xaml differs from the pinned copy:" + Environment.NewLine +
            Diff(C1aFixtures.PinnedDark, actual));
    }

    // ---- 以下用例要真起 Avalonia(headless + Skia)才看得到色板解析结果 ----
    // C1b-fix:本小节的每条用例体都在 AvaloniaSession 的会话线程上执行 —— 平台(与 Dispatcher.UIThread)
    // 就起在那个线程上,所以资源字典求值与刷子构造的 VerifyAccess 恒过,两个测试通道结论一致。

    [Fact]
    public void Palette_resolves_every_key_in_both_theme_variants()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                foreach (string key in Palette.Keys)
                {
                    Assert.True(Palette.TryGet(key, variant, out object? value), "unresolved key: " + key + " in " + variant);
                    Assert.NotNull(value);
                    Assert.True(value is IBrush || value is Color, "unexpected value type for " + key + ": " + value.GetType().Name);
                }
            }
        });
    }

    [Fact]
    public void Palette_returns_the_colour_resources_as_avalonia_colours()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            Assert.Equal(Color.Parse("#F5F5F5"), Palette.Color("C.Canvas", ThemeVariant.Light));
            Assert.Equal(Color.Parse("#FFFFFF"), Palette.Color("C.Paper", ThemeVariant.Light));
            Assert.Equal(Color.Parse("#2E9B6B"), Palette.Color("C.Accent", ThemeVariant.Light));
            Assert.Equal(Color.Parse("#161616"), Palette.Color("C.Canvas", ThemeVariant.Dark));
            Assert.Equal(Color.Parse("#1E1E1E"), Palette.Color("C.Paper", ThemeVariant.Dark));
            Assert.Equal(Color.Parse("#79C8A3"), Palette.Color("C.AccentText", ThemeVariant.Dark));
        });
    }

    [Fact]
    public void Palette_returns_brushes_with_the_pinned_colour_and_opacity()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            var mask = (ISolidColorBrush)Palette.Brush("B.Mask", ThemeVariant.Light);
            Assert.Equal(Color.Parse("#1A1A1A"), mask.Color);
            Assert.Equal(0.34, mask.Opacity, 10);

            var maskDark = (ISolidColorBrush)Palette.Brush("B.Mask", ThemeVariant.Dark);
            Assert.Equal(Color.Parse("#000000"), maskDark.Color);
            Assert.Equal(0.54, maskDark.Opacity, 10);

            var glass = (ISolidColorBrush)Palette.Brush("B.GlassBorder", ThemeVariant.Light);
            Assert.Equal(Color.Parse("#BFDCCF"), glass.Color);
            Assert.Equal(0.68, glass.Opacity, 10);

            var title = (ISolidColorBrush)Palette.Brush("B.TitleSep", ThemeVariant.Light);
            Assert.Equal(0.1, title.Opacity, 10);
        });
    }

    [Fact]
    public void Palette_keeps_eight_digit_argb_colours_transparent()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            var lightCardBorder = (ISolidColorBrush)Palette.Brush("B.CardBorder", ThemeVariant.Light);
            Assert.Equal((byte)0x00, lightCardBorder.Color.A);
            Assert.Equal((byte)0xFF, lightCardBorder.Color.R);
            Assert.Equal((byte)0xFF, lightCardBorder.Color.G);
            Assert.Equal((byte)0xFF, lightCardBorder.Color.B);

            var darkCardBorder = (ISolidColorBrush)Palette.Brush("B.CardBorder", ThemeVariant.Dark);
            Assert.Equal((byte)0x0F, darkCardBorder.Color.A);
        });
    }

    [Fact]
    public void Palette_returns_gradient_brushes_with_three_stops()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            var banner = Assert.IsType<LinearGradientBrush>(Palette.Brush("B.BannerFill", ThemeVariant.Light));
            Assert.Equal(3, banner.GradientStops.Count);
            Assert.Equal(Color.Parse("#237F59"), banner.GradientStops[0].Color);
            Assert.Equal(0.0, banner.GradientStops[0].Offset, 10);
            Assert.Equal(Color.Parse("#1F7A52"), banner.GradientStops[1].Color);
            Assert.Equal(0.52, banner.GradientStops[1].Offset, 10);
            Assert.Equal(Color.Parse("#185F40"), banner.GradientStops[2].Color);
            Assert.Equal(1.0, banner.GradientStops[2].Offset, 10);

            var wash = Assert.IsType<LinearGradientBrush>(Palette.Brush("B.PageWash", ThemeVariant.Dark));
            Assert.Equal(3, wash.GradientStops.Count);
            Assert.Equal(Color.Parse("#161616"), wash.GradientStops[0].Color);
        });
    }

    [Fact]
    public void Unknown_palette_key_falls_back_to_neutral_grey_instead_of_throwing()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            foreach (string key in new[] { "B.Nope", "C.Nope", "B.", "", "not-a-key" })
            {
                Assert.False(Palette.TryGet(key, ThemeVariant.Light, out _), "unexpectedly resolved: " + key);
                IBrush brush = Palette.Brush(key, ThemeVariant.Light);
                var solid = Assert.IsType<SolidColorBrush>(brush);
                Assert.Equal(Palette.FallbackColor, solid.Color);
                Assert.Equal(Palette.FallbackColor, Palette.Color(key, ThemeVariant.Light));
            }

            Assert.Equal(Color.Parse("#808080"), Palette.FallbackColor);
        });
    }

    [Fact]
    public void Light_and_dark_resolve_different_values_for_the_same_key()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            foreach (string key in new[] { "B.Canvas", "B.Paper", "B.Ink", "B.Accent", "B.BannerFill", "C.Canvas" })
            {
                object? light = null;
                object? dark = null;
                Assert.True(Palette.TryGet(key, ThemeVariant.Light, out light));
                Assert.True(Palette.TryGet(key, ThemeVariant.Dark, out dark));
                Assert.NotEqual(Describe(light), Describe(dark));
            }
        });
    }

    /// <summary>
    /// 把刷子写成可比较的字符串。注意 <c>LinearGradientBrush.ToString()</c> 两个主题下**一模一样**
    /// (都是类型名),所以必须先摊开渐变停靠点,否则这条用例会假绿。
    /// </summary>
    private static string Describe(object? value)
    {
        switch (value)
        {
            case ISolidColorBrush solid:
                return "solid:" + solid.Color + ":" + solid.Opacity.ToString("R", CultureInfo.InvariantCulture);
            case LinearGradientBrush gradient:
                return "linear:" + string.Join(
                    ";",
                    gradient.GradientStops.Select(s => s.Color + "@" + s.Offset.ToString("R", CultureInfo.InvariantCulture)));
            default:
                return value?.ToString() ?? "null";
        }
    }

    [Fact]
    public void Palette_current_variant_follows_the_application_requested_variant()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            Application? app = Application.Current;
            Assert.NotNull(app);
            ThemeVariant? original = app!.RequestedThemeVariant;
            try
            {
                app.RequestedThemeVariant = ThemeVariant.Dark;
                Assert.Equal(ThemeVariant.Dark, Palette.Current);
                Assert.Equal(Color.Parse("#161616"), Palette.Color("C.Canvas"));

                app.RequestedThemeVariant = ThemeVariant.Light;
                Assert.Equal(ThemeVariant.Light, Palette.Current);
                Assert.Equal(Color.Parse("#F5F5F5"), Palette.Color("C.Canvas"));

                // Default(跟随系统)按浅色处理 —— 与 A1 的占位色判定同一套规则。
                app.RequestedThemeVariant = ThemeVariant.Default;
                Assert.Equal(ThemeVariant.Light, Palette.Current);
            }
            finally
            {
                // 全局静态改动必须在 finally 里还原(此处同时也保证会话线程上的 Application 状态不残留)。
                app.RequestedThemeVariant = original;
            }
        });
    }
}
