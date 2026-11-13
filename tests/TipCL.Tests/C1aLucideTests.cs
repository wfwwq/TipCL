using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using TipCL.Services;
using TipCL.Themes;
using Xunit;
using ShapePath = Avalonia.Controls.Shapes.Path;
using ShapeRectangle = Avalonia.Controls.Shapes.Rectangle;

namespace TipCL.Tests;

/// <summary>
/// C1a:Lucide 图标系统(Avalonia 移植版)的用例。
///
/// 期望值来源:
/// - **Windows 源**:<c>Services\Lucide.cs</c> 的 <c>public const string</c> path data 常量、
///   <c>ToolIcon</c> 的工具名分支、<c>Services\LucideAssets.cs</c> 的工具名集合
///   —— 前两项用"逐个字符串比对"交叉复核,第三项用键集合比对;
/// - **本机实测**:Avalonia 11.3.23 headless + Skia 下 <c>Geometry.Parse</c> 的 bounds、
///   <c>Icon</c> 的描边缩放、<c>Asset</c> 解出来的位图尺寸。
///
/// 本文件**不打印任何 base64**(LucideAssets 的断言只报工具名与计数)。
/// </summary>
public class C1aLucideTests
{
    private static readonly Regex DataConstant = new Regex(
        "public const string ([A-Za-z0-9_]+) = \"([^\"]*)\";",
        RegexOptions.CultureInvariant);

    private static readonly Regex JsonKey = new Regex("\\[\"([a-z0-9_]+)\"\\] = Convert\\.FromBase64String", RegexOptions.CultureInvariant);

    /// <summary>把 <c>public const string</c> 常量名逐个给出去(为了不把长字符串打进用例显示名,只给名字)。</summary>
    public static IEnumerable<object[]> PathDataConstantNames()
    {
        foreach (FieldInfo field in typeof(Lucide).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.IsLiteral && field.FieldType == typeof(string) && field.GetRawConstantValue() is string)
            {
                yield return new object[] { field.Name };
            }
        }
    }

    public static IEnumerable<object[]> KnownToolIcons()
    {
        yield return new object[] { "search_modpacks", "M21 21l-4.34-4.34 M3 11a8 8 0 1 0 16 0a8 8 0 1 0-16 0" };
        yield return new object[] { "search_mods", "M21 21l-4.34-4.34 M3 11a8 8 0 1 0 16 0a8 8 0 1 0-16 0" };
        yield return new object[] { "get_crash_report", "M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z M14 2v5a1 1 0 0 0 1 1h5 M10 9H8 M16 13H8 M16 17H8" };
        yield return new object[] { "write_mod_config", "M14.364 13.634a2 2 0 0 0-.506.854l-.837 2.87a.5.5 0 0 0 .62.62l2.87-.837a2 2 0 0 0 .854-.506l4.013-4.009a1 1 0 0 0-3.004-3.004z M14.487 7.858A1 1 0 0 1 14 7V2 M20 19.645V20a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l2.516 2.516 M8 18h1" };
    }

    private static string ConstantValue(string name)
    {
        FieldInfo? field = typeof(Lucide).GetField(name, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
        return (string)field!.GetRawConstantValue()!;
    }

    [Theory]
    [MemberData(nameof(PathDataConstantNames))]
    public void Every_public_path_data_constant_parses_with_avalonia_and_has_non_zero_bounds(string name)
    {
        C1aFixtures.EnsureAvalonia();
        // C1b-fix:Geometry.Parse 要平台;会话线程上平台与 Dispatcher.UIThread 是同一个线程。
        AvaloniaSession.Run(() =>
        {
            string data = ConstantValue(name);
            Assert.False(string.IsNullOrWhiteSpace(data), name + " is empty");
            Geometry geometry = Geometry.Parse(data);
            Assert.True(geometry.Bounds.Width > 0.0, name + " has zero width: " + geometry.Bounds);
            Assert.True(geometry.Bounds.Height > 0.0, name + " has zero height: " + geometry.Bounds);
        });
    }

    [Fact]
    public void There_are_fifteen_public_path_data_constants()
    {
        int count = typeof(Lucide).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Count(f => f.IsLiteral && f.FieldType == typeof(string));
        Assert.Equal(15, count);
    }

    [Fact]
    public void Path_data_constants_are_identical_to_the_windows_source()
    {
        string? source = C1aFixtures.WindowsPathOrNull("src", "TipCL", "Services", "Lucide.cs");
        if (source is null)
        {
            Console.WriteLine("SKIP-NOTE: windows baseline not available, cross-check not run: " + C1aFixtures.WindowsRoot);
            return;
        }

        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in DataConstant.Matches(File.ReadAllText(source)))
        {
            expected[match.Groups[1].Value] = match.Groups[2].Value;
        }

        // 非空性:Windows 源里必须真读到常量(否则这条用例会假绿)。
        Assert.True(expected.Count >= 12, "parsed only " + expected.Count + " constants from the windows source");

        var differing = new List<string>();
        foreach (string name in PathDataConstantNames().Select(row => (string)row[0]))
        {
            if (!expected.TryGetValue(name, out string? value))
            {
                differing.Add(name + " (missing in windows source)");
                continue;
            }

            if (!string.Equals(value, ConstantValue(name), StringComparison.Ordinal))
            {
                differing.Add(name + " (length " + value.Length + " vs " + ConstantValue(name).Length + ")");
            }
        }

        Assert.True(differing.Count == 0, "path data differs from the windows source: " + string.Join(", ", differing));
    }

    [Fact]
    public void Icon_keeps_the_24_unit_geometry_and_scales_stroke_and_bounds()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            Geometry reference = Geometry.Parse(Lucide.ChevronRight);
            double expectedWidth = reference.Bounds.Width / 24.0 * 16.0;

            ShapePath icon = Lucide.Icon(Lucide.ChevronRight, 16.0, "B.Ink");
            Assert.Equal(16.0, icon.Width);
            Assert.Equal(16.0, icon.Height);
            Assert.Equal(16.0 / 24.0 * 2.0, icon.StrokeThickness, 10);
            Assert.Equal(expectedWidth, icon.Data!.Bounds.Width, 6);
            Assert.Equal(Stretch.None, icon.Stretch);
            Assert.Equal(PenLineCap.Round, icon.StrokeLineCap);
            Assert.Equal(PenLineJoin.Round, icon.StrokeJoin);
            Assert.False(icon.IsHitTestVisible);

            ShapePath full = Lucide.Icon(Lucide.ChevronRight);
            Assert.Equal(16.0, full.Width);
            Assert.Equal(16.0 / 24.0 * 2.0, full.StrokeThickness, 10);
        });
    }

    [Fact]
    public void Icon_at_design_size_does_not_scale_the_geometry()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            ShapePath icon = Lucide.Icon(Lucide.Trash2, 24.0, "B.Ink");
            Geometry reference = Geometry.Parse(Lucide.Trash2);
            Assert.Equal(reference.Bounds, icon.Data!.Bounds);
            Assert.Equal(2.0, icon.StrokeThickness, 10);
        });
    }

    [Fact]
    public void Icon_uses_the_palette_brush_key_and_falls_back_for_unknown_keys()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            var ink = Assert.IsType<SolidColorBrush>(Lucide.Icon(Lucide.Eye, 16.0, "B.Ink").Stroke);
            Assert.Equal(Palette.Color("B.Ink", ThemeVariant.Light), ink.Color);

            var fallback = Assert.IsType<SolidColorBrush>(Lucide.Icon(Lucide.Eye, 16.0, "B.Nope").Stroke);
            Assert.Equal(Palette.FallbackColor, fallback.Color);
        });
    }

    [Fact]
    public void Icon_centres_the_rotation_origin_for_the_spinner()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            ShapePath icon = Lucide.Icon(Lucide.ChevronRight, 16.0, "B.Ink");
            Assert.Equal(0.5, icon.RenderTransformOrigin.Point.X, 10);
            Assert.Equal(0.5, icon.RenderTransformOrigin.Point.Y, 10);
            Assert.Equal(RelativeUnit.Relative, icon.RenderTransformOrigin.Unit);
        });
    }

    [Fact]
    public void Spinner_is_the_loader_circle_with_a_rotate_transform()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            ShapePath spinner = Lucide.Spinner(16.0);
            Geometry reference = Geometry.Parse(Lucide.LoaderCircle);
            double scale = 16.0 / 24.0;
            Assert.Equal(reference.Bounds.Width * scale, spinner.Data!.Bounds.Width, 4);
            Assert.Equal(reference.Bounds.Height * scale, spinner.Data!.Bounds.Height, 4);
            var rotate = Assert.IsType<RotateTransform>(spinner.RenderTransform);
            Assert.Equal(0.0, rotate.Angle, 10);
            var stroke = Assert.IsType<SolidColorBrush>(spinner.Stroke);
            Assert.Equal(Palette.Color("B.AccentDeep", ThemeVariant.Light), stroke.Color);
        });
    }

    [Fact]
    public void Spinner_defaults_to_the_accent_deep_key()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            var stroke = Assert.IsType<SolidColorBrush>(Lucide.Spinner().Stroke);
            Assert.Equal(Palette.Color("B.AccentDeep", ThemeVariant.Light), stroke.Color);
        });
    }

    [Fact]
    public void Rotate_snaps_to_90_degrees_when_motion_is_off_and_restores_the_flag()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            bool original = Lucide.MotionEnabled;
            try
            {
                Lucide.MotionEnabled = false;
                ShapePath chevron = Lucide.Icon(Lucide.ChevronRight, 16.0, "B.Ink");
                Lucide.Rotate(chevron, true);
                var rotate = Assert.IsType<RotateTransform>(chevron.RenderTransform);
                Assert.Equal(90.0, rotate.Angle, 10);

                Lucide.Rotate(chevron, false);
                Assert.Equal(0.0, rotate.Angle, 10);
            }
            finally
            {
                Lucide.MotionEnabled = original;
            }

            Assert.Equal(original, Lucide.MotionEnabled);
        });
    }

    [Fact]
    public void Rotate_creates_the_rotate_transform_when_the_path_has_none()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            bool original = Lucide.MotionEnabled;
            try
            {
                Lucide.MotionEnabled = false;
                ShapePath chevron = Lucide.Icon(Lucide.ChevronRight, 16.0, "B.Ink");
                Assert.Null(chevron.RenderTransform);
                Lucide.Rotate(chevron, true);
                Assert.IsType<RotateTransform>(chevron.RenderTransform);
            }
            finally
            {
                Lucide.MotionEnabled = original;
            }
        });
    }

    [Fact]
    public void Rotate_ignores_a_null_chevron()
    {
        AvaloniaSession.Run(() => Lucide.Rotate(null!, true));
    }

    [Fact]
    public void Rotate_starts_an_animation_when_motion_is_on_without_throwing()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            bool original = Lucide.MotionEnabled;
            try
            {
                Lucide.MotionEnabled = true;
                ShapePath chevron = Lucide.Icon(Lucide.ChevronRight, 16.0, "B.Ink");
                Lucide.Rotate(chevron, true);
                Assert.IsType<RotateTransform>(chevron.RenderTransform);
            }
            finally
            {
                Lucide.MotionEnabled = original;
            }
        });
    }

    [Fact]
    public void Shimmer_off_restores_the_plain_palette_brush()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            var text = new TextBlock { Text = "TipCL" };
            Lucide.Shimmer(text, false, "B.Ink");
            var brush = Assert.IsType<SolidColorBrush>(text.Foreground);
            Assert.Equal(Palette.Color("B.Ink", ThemeVariant.Light), brush.Color);
        });
    }

    [Fact]
    public void Shimmer_on_builds_a_five_stop_gradient_and_off_restores_it()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            bool original = Lucide.MotionEnabled;
            var text = new TextBlock { Text = "TipCL", Foreground = Palette.Brush("B.Ink") };
            try
            {
                Lucide.MotionEnabled = true;
                Lucide.Shimmer(text, true, "B.Ink");
                var gradient = Assert.IsType<LinearGradientBrush>(text.Foreground);
                Assert.Equal(5, gradient.GradientStops.Count);
                Assert.Equal(Palette.Color("B.Ink", ThemeVariant.Light), gradient.GradientStops[0].Color);
                Assert.Equal(Palette.Color("B.Ink", ThemeVariant.Light), gradient.GradientStops[4].Color);
                Assert.NotEqual(gradient.GradientStops[0].Color, gradient.GradientStops[2].Color);

                Lucide.Shimmer(text, false, "B.Ink");
                var restored = Assert.IsType<SolidColorBrush>(text.Foreground);
                Assert.Equal(Palette.Color("B.Ink", ThemeVariant.Light), restored.Color);
            }
            finally
            {
                Lucide.MotionEnabled = original;
            }
        });
    }

    [Fact]
    public void Shimmer_with_motion_off_never_builds_a_gradient()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            bool original = Lucide.MotionEnabled;
            var text = new TextBlock { Text = "TipCL", Foreground = Palette.Brush("B.Ink") };
            try
            {
                Lucide.MotionEnabled = false;
                Lucide.Shimmer(text, true, "B.Ink");
                Assert.IsType<SolidColorBrush>(text.Foreground);
            }
            finally
            {
                Lucide.MotionEnabled = original;
            }
        });
    }

    [Theory]
    [MemberData(nameof(KnownToolIcons))]
    public void ToolIcon_maps_the_known_tool_names(string tool, string expected)
    {
        Assert.Equal(expected, Lucide.ToolIcon(tool));
    }

    [Fact]
    public void ToolIcon_returns_the_mcp_icon_for_mcp_prefixed_names()
    {
        const string Mcp = "M12 22v-5 M9 8V2 M15 8V2 M18 8v5a6 6 0 0 1-12 0V8z";
        Assert.Equal(Mcp, Lucide.ToolIcon("mcp__filesystem_read"));
        Assert.Equal(Mcp, Lucide.ToolIcon("mcp.everything"));
        Assert.Equal(Mcp, Lucide.ToolIcon("mcp_playwright"));
    }

    [Fact]
    public void ToolIcon_returns_null_for_unknown_tools()
    {
        Assert.Null(Lucide.ToolIcon("no_such_tool"));
        Assert.Null(Lucide.ToolIcon("mcp"));
        Assert.Null(Lucide.ToolIcon("mcpx"));
        Assert.Null(Lucide.ToolIcon(""));
        Assert.Null(Lucide.ToolIcon(null));
    }

    [Fact]
    public void IsMcp_matches_the_windows_predicate()
    {
        Assert.True(Lucide.IsMcp("mcp__a"));
        Assert.True(Lucide.IsMcp("mcp.a"));
        Assert.True(Lucide.IsMcp("mcp_a"));
        Assert.False(Lucide.IsMcp("mcp"));
        Assert.False(Lucide.IsMcp("MCP_a"));
        Assert.False(Lucide.IsMcp(null));
    }

    [Fact]
    public void LucideAssets_is_not_empty_and_has_no_empty_values()
    {
        Assert.NotEmpty(LucideAssets.Icons);
        foreach (KeyValuePair<string, byte[]> entry in LucideAssets.Icons)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Key), "empty tool name");
            Assert.True(entry.Value.Length > 0, "empty asset for " + entry.Key);
        }
    }

    [Fact]
    public void Every_embedded_asset_is_a_png_with_non_zero_size()
    {
        int count = 0;
        foreach (KeyValuePair<string, byte[]> entry in LucideAssets.Icons)
        {
            byte[] bytes = entry.Value;
            Assert.True(
                bytes.Length > 33 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47,
                "not a PNG: " + entry.Key);
            (int width, int height) = C1aFixtures.PngSize(bytes);
            Assert.True(width > 0, "zero width PNG: " + entry.Key);
            Assert.True(height > 0, "zero height PNG: " + entry.Key);
            count++;
        }

        Assert.True(count >= 20, "expected at least 20 embedded assets, found " + count);
    }

    [Fact]
    public void Embedded_asset_keys_match_the_windows_source()
    {
        string? source = C1aFixtures.WindowsPathOrNull("src", "TipCL", "Services", "LucideAssets.cs");
        if (source is null)
        {
            Console.WriteLine("SKIP-NOTE: windows baseline not available, cross-check not run: " + C1aFixtures.WindowsRoot);
            return;
        }

        // 只从 Windows 源里**抠工具名**,绝不把 base64 带出来(断言失败也只报名字)。
        var expected = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match match in JsonKey.Matches(File.ReadAllText(source)))
        {
            expected.Add(match.Groups[1].Value);
        }

        Assert.True(expected.Count >= 20, "parsed only " + expected.Count + " asset keys from the windows source");
        var actual = new SortedSet<string>(LucideAssets.Icons.Keys, StringComparer.Ordinal);
        Assert.Equal(expected.ToList(), actual.ToList());
    }

    [Fact]
    public void Asset_returns_a_shape_filled_with_the_palette_brush_and_masked_by_the_png()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            Control asset = Lucide.Asset("ask_user", 16.0, "B.AccentDeep");
            var rectangle = Assert.IsType<ShapeRectangle>(asset);
            Assert.Equal(16.0, rectangle.Width);
            Assert.Equal(16.0, rectangle.Height);
            var fill = Assert.IsType<SolidColorBrush>(rectangle.Fill);
            Assert.Equal(Palette.Color("B.AccentDeep", ThemeVariant.Light), fill.Color);

            var mask = Assert.IsType<ImageBrush>(rectangle.OpacityMask);
            var bitmap = Assert.IsType<Bitmap>(mask.Source);
            Assert.Equal(128, bitmap.PixelSize.Width);
            Assert.Equal(128, bitmap.PixelSize.Height);
            Assert.Equal(Stretch.Uniform, mask.Stretch);
            Assert.False(rectangle.IsHitTestVisible);
        });
    }

    [Fact]
    public void Asset_falls_back_to_a_spinner_for_an_unknown_tool()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            Control asset = Lucide.Asset("no_such_tool");
            Assert.IsType<ShapePath>(asset);
        });
    }

    [Fact]
    public void Asset_returns_the_same_cached_bitmap_for_repeated_calls()
    {
        C1aFixtures.EnsureAvalonia();
        AvaloniaSession.Run(() =>
        {
            var first = Assert.IsType<ImageBrush>(Assert.IsType<ShapeRectangle>(Lucide.Asset("ask_user")).OpacityMask);
            var second = Assert.IsType<ImageBrush>(Assert.IsType<ShapeRectangle>(Lucide.Asset("ask_user")).OpacityMask);
            Assert.Same(first.Source, second.Source);
        });
    }

    [Fact]
    public void Ico_table_covers_the_pinned_sample_codepoints()
    {
        string icoPath = C1aFixtures.RepoPath("src", "TipCL.App", "Services", "Ico.cs");
        var pattern = new Regex("\\\\u([Ee][0-9A-Fa-f]{3}|[Ff][0-8][0-9A-Fa-f]{2})", RegexOptions.CultureInvariant);
        var found = new SortedSet<int>();
        foreach (Match match in pattern.Matches(File.ReadAllText(icoPath)))
        {
            found.Add(Convert.ToInt32(match.Groups[1].Value, 16));
        }

        // Ico.cs 一共有 67 个字形常量,这里只抽查其中 22 个(全量大表见 Services\Ico.cs)。
        Assert.True(found.Count >= 60, "parsed only " + found.Count + " codepoints from Ico.cs");
        List<int> missing = C1aFixtures.SampledIconCodepoints.Select(cp => (int)cp).Where(cp => !found.Contains(cp)).ToList();
        Assert.True(missing.Count == 0, "sampled codepoints not found in Ico.cs: " + string.Join(", ", missing.Select(cp => "U+" + cp.ToString("X4"))));
    }

    [Fact]
    public void Mac_Lucide_source_does_not_import_the_unported_pages_or_wpf_namespaces()
    {
        string path = C1aFixtures.RepoPath("src", "TipCL.App", "Services", "Lucide.cs");
        string text = File.ReadAllText(path);
        Assert.DoesNotContain("using TipCL.Pages", text, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows", text, StringComparison.Ordinal);
        Assert.Contains("using TipCL.Themes;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LucideAssets_source_is_byte_identical_to_the_windows_source()
    {
        string path = C1aFixtures.RepoPath("src", "TipCL.App", "Services", "LucideAssets.cs");
        using FileStream stream = File.OpenRead(path);
        string hash = Convert.ToHexString(SHA256.HashData(stream));
        Assert.Equal("1B93FADFF6D610AB03A4D363986CFC12D6C4EDB214142031D34F11593366BB52", hash);

        string? source = C1aFixtures.WindowsPathOrNull("src", "TipCL", "Services", "LucideAssets.cs");
        if (source is null)
        {
            Console.WriteLine("SKIP-NOTE: windows baseline not available, cross-check not run: " + C1aFixtures.WindowsRoot);
            return;
        }

        using FileStream original = File.OpenRead(source);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(original)), hash);
    }
}
