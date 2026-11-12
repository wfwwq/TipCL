using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Avalonia.Media;
using Avalonia.Platform;
using TipCL.Themes;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C1a:内置图标字体 TipCLIcons.ttf 的用例。
///
/// 期望值来源:
/// - **Windows 源**:<c>Resources\fonts\TipCLIcons.ttf</c>(14232 字节)与
///   <c>LICENSE-FluentSystemIcons.txt</c>(1078 字节)—— 本包用 <c>Copy-Item -LiteralPath</c>
///   字节级复制,这里用 SHA256 钉住;
/// - **本机实测**:ttf 的 name 表家族名是 <c>TipCL Icons</c>(与 Windows 版 <c>Services\Ui.cs</c>
///   里 <c>./Resources/fonts/#TipCL Icons</c> 一致),Avalonia 11.3.23 headless + Skia 下
///   <c>avares://…/TipCLIcons.ttf#TipCL Icons</c> 能拿到字形。
/// </summary>
public class C1aIconFontTests
{
    /// <summary>Windows 基准里那份 ttf 的 SHA256(本包复制前实测)。</summary>
    private const string TtfSha256 = "220875655B20A2E9C8344F437F48227CDA1503D4B9729E6AA117221144679748";

    /// <summary>Windows 基准里那份 LICENSE 的 SHA256(本包复制前实测)。</summary>
    private const string LicenceSha256 = "69BC45DC42B9ACB96A69823ADBC6AE538374E3C0BDE169B855B32C48EAAEF52F";

    private static string FontPath => C1aFixtures.RepoPath("src", "TipCL.App", "Resources", "fonts", "TipCLIcons.ttf");

    private static string LicencePath => C1aFixtures.RepoPath("src", "TipCL.App", "Resources", "fonts", "LICENSE-FluentSystemIcons.txt");

    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static IEnumerable<object[]> SampledCodepoints()
    {
        foreach (ushort codepoint in C1aFixtures.SampledIconCodepoints)
        {
            yield return new object[] { codepoint };
        }
    }

    [Fact]
    public void Icon_font_exists_at_the_expected_path_with_a_non_zero_size()
    {
        var info = new FileInfo(FontPath);
        Assert.True(info.Exists, "missing icon font: " + FontPath);
        Assert.True(info.Length > 0, "icon font is empty: " + FontPath);
    }

    [Fact]
    public void Icon_font_is_byte_identical_to_the_windows_source()
    {
        Assert.Equal(TtfSha256, Sha256(FontPath));

        string? source = C1aFixtures.WindowsPathOrNull("src", "TipCL", "Resources", "fonts", "TipCLIcons.ttf");
        if (source is null)
        {
            Console.WriteLine("SKIP-NOTE: windows baseline not available, cross-check not run: " + C1aFixtures.WindowsRoot);
            return;
        }

        Assert.Equal(Sha256(source), Sha256(FontPath));
    }

    [Fact]
    public void Licence_file_is_present_next_to_the_font_and_byte_identical_to_the_windows_source()
    {
        Assert.True(File.Exists(LicencePath), "missing licence file: " + LicencePath);
        Assert.Equal(LicenceSha256, Sha256(LicencePath));

        string? source = C1aFixtures.WindowsPathOrNull("src", "TipCL", "Resources", "fonts", "LICENSE-FluentSystemIcons.txt");
        if (source is null)
        {
            Console.WriteLine("SKIP-NOTE: windows baseline not available, cross-check not run: " + C1aFixtures.WindowsRoot);
            return;
        }

        Assert.Equal(Sha256(source), Sha256(LicencePath));
    }

    [Fact]
    public void Icon_font_name_table_says_TipCL_Icons()
    {
        // nameID 1 = Family,4 = Full name,6 = PostScript name;三项都要与 Windows 版一致。
        Assert.Equal("TipCL Icons", C1aFixtures.FontNameString(FontPath, 1));
        Assert.StartsWith("TipCL Icons", C1aFixtures.FontNameString(FontPath, 4) ?? "", StringComparison.Ordinal);
        Assert.Equal("TipCLIcons-Regular", C1aFixtures.FontNameString(FontPath, 6));
        Assert.Equal("TipCL Icons", Palette.IconFontFamilyName);
    }

    [Fact]
    public void Icon_font_cmap_is_not_empty_and_covers_the_private_use_range()
    {
        HashSet<int> codepoints = C1aFixtures.FontCodepoints(FontPath);
        Assert.True(codepoints.Count > 0, "cmap is empty");
        Assert.Contains(0xE768, codepoints);
        Assert.Contains(0xE7FC, codepoints);
        Assert.Contains(0xF5B0, codepoints);
        Assert.All(codepoints, cp => Assert.InRange(cp, 0x20, 0x10FFFF));
    }

    /// <summary>
    /// <c>Services\Ico.cs</c> 里的每个图标码位都必须在这份字体里(10/22 不满足就说明字体换错了)。
    /// </summary>
    [Theory]
    [MemberData(nameof(SampledCodepoints))]
    public void Icon_font_covers_the_codepoints_used_by_the_icon_table(ushort codepoint)
    {
        HashSet<int> codepoints = C1aFixtures.FontCodepoints(FontPath);
        Assert.True(
            codepoints.Contains(codepoint),
            "U+" + codepoint.ToString("X4") + " missing from the embedded icon font");
    }

    [Fact]
    public void Icon_font_is_embedded_under_the_avares_uri_the_app_declares()
    {
        C1aFixtures.EnsureAvalonia();
        using Stream embedded = AssetLoader.Open(new Uri("avares://TipCL/Resources/fonts/TipCLIcons.ttf"));
        Assert.NotNull(embedded);
        using var buffer = new MemoryStream();
        embedded.CopyTo(buffer);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(buffer.ToArray())),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(FontPath))));
    }

    [Fact]
    public void Icon_font_resolves_through_the_avalonia_font_manager()
    {
        C1aFixtures.EnsureAvalonia();
        var typeface = new Typeface(Palette.IconFont);
        Assert.True(
            FontManager.Current.TryGetGlyphTypeface(typeface, out IGlyphTypeface? glyph),
            "avares font family did not resolve: " + Palette.IconFontUri);
        Assert.NotNull(glyph);
        Assert.Equal("TipCL Icons", glyph!.FamilyName);

        foreach (ushort codepoint in C1aFixtures.SampledIconCodepoints)
        {
            Assert.True(
                glyph.TryGetGlyph(codepoint, out _),
                "no glyph for U+" + codepoint.ToString("X4") + " through the Avalonia font manager");
        }
    }

    [Fact]
    public void Icon_font_uri_is_an_avares_uri_with_the_family_after_the_hash()
    {
        Assert.Equal("avares://TipCL/Resources/fonts/TipCLIcons.ttf#TipCL Icons", Palette.IconFontUri);
        Assert.Equal("TipCL Icons", Palette.IconFont.Name);
        Assert.StartsWith("avares://TipCL/", Palette.IconFontUri, StringComparison.Ordinal);
    }

    [Fact]
    public void Icon_font_file_is_the_only_one_the_app_embeds_from_that_folder()
    {
        string folder = Path.GetDirectoryName(FontPath)!;
        string[] files = Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;
        Assert.Equal(new[] { "LICENSE-FluentSystemIcons.txt", "TipCLIcons.ttf" }, files);
    }
}
