using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace TipCL.Tests.Theme;

/// <summary>
/// 主题键存在性测试（M1-A5）：解析 M1-A2 的 Palette.Light.axaml / Palette.Dark.axaml 源文件，
/// 断言明暗两套键集合一致、关键键在。判定逻辑仿照 Windows 版 tools/check-palette-pair.ps1。
/// axaml 以源码文本方式读取，无编译期依赖；A2 未落地时整体 Skip（收尾时若 A2 已就位则移除 Skip）。
/// </summary>
public class ThemeKeysTests
{
    private static readonly Regex KeyRegex = new("x:Key=\"([^\"]+)\"", RegexOptions.Compiled);

    /// <summary>关键键清单（源自 Windows 版 v2 调色板核心键，明暗两套都必须有）。</summary>
    private static readonly string[] RequiredKeys =
    {
        "B.Accent", "B.AccentText", "B.OnAccent", "B.Card", "B.CardBorder",
        "B.Canvas", "B.Paper", "B.Ink", "B.InkMuted", "B.Line",
        "B.TitleBar", "B.Side", "B.Danger", "B.Ok", "B.Warn",
        "C.Accent", "C.Paper",
    };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GOAL.md")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? "";
    }

    private static string PalettePath(string file)
    {
        return Path.Combine(FindRepoRoot(), "src", "TipCL.App", "Themes", file);
    }

    private static SortedSet<string> LoadKeys(string file)
    {
        return new SortedSet<string>(KeyRegex.Matches(File.ReadAllText(PalettePath(file)))
            .Select(m => m.Groups[1].Value));
    }

    [Fact]
    public void LightAndDark_Palettes_Exist()
    {
        Assert.True(File.Exists(PalettePath("Palette.Light.axaml")), "缺少 Palette.Light.axaml");
        Assert.True(File.Exists(PalettePath("Palette.Dark.axaml")), "缺少 Palette.Dark.axaml");
    }

    [Fact]
    public void LightAndDark_KeySets_AreIdentical()
    {
        var light = LoadKeys("Palette.Light.axaml");
        var dark = LoadKeys("Palette.Dark.axaml");
        var onlyLight = light.Except(dark).ToList();
        var onlyDark = dark.Except(light).ToList();
        Assert.True(onlyLight.Count == 0, $"仅在 Light 中的键: {string.Join(", ", onlyLight)}");
        Assert.True(onlyDark.Count == 0, $"仅在 Dark 中的键: {string.Join(", ", onlyDark)}");
    }

    [Fact]
    public void Palettes_HaveMinimumKeyCount()
    {
        // Windows 版 v2 调色板每套 61 键；A2 全量对齐前先要求不少于 40。
        Assert.True(LoadKeys("Palette.Light.axaml").Count >= 40, "Light 调色板键数不足 40");
        Assert.True(LoadKeys("Palette.Dark.axaml").Count >= 40, "Dark 调色板键数不足 40");
    }

    [Fact]
    public void RequiredKeys_PresentInBothPalettes()
    {
        var light = LoadKeys("Palette.Light.axaml");
        var dark = LoadKeys("Palette.Dark.axaml");
        foreach (var key in RequiredKeys)
        {
            Assert.True(light.Contains(key), $"Light 缺少关键键 {key}");
            Assert.True(dark.Contains(key), $"Dark 缺少关键键 {key}");
        }
    }
}
