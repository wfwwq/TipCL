using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using TipCL.App.Pages.Layout;
using TipCL.App.Session;
using TipCL.Tests.App;
using Xunit;

namespace TipCL.Tests.Layout;

/// <summary>
/// R35 lin-l0(布局 v3 · WP-L0)令牌测试:色板成对、契约键齐全、兜底值与色板逐字一致、
/// ControlTheme 能真套到对应控件上、图标尺寸 / 线宽口径、截图开关的纯函数解析。
/// 对应 Windows r34/l0 的 LayoutV3ResourceTests。axaml 以源码文本读取(同 ThemeKeysTests),运行期部分走 TestApp 无头会话。
/// </summary>
public class LayoutV3TokensTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "docs/dev/GOAL.md")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? "";
    }

    private static string ThemeFile(string file) => System.IO.Path.Combine(FindRepoRoot(), "src", "TipCL.App", "Themes", file);

    private static XDocument Load(string file) => XDocument.Load(ThemeFile(file));

    private static Dictionary<string, XElement> Keyed(string file)
    {
        var map = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var e in Load(file).Root!.Elements())
        {
            var key = (string?)e.Attribute(X + "Key");
            if (key != null) map[key] = e;
        }
        return map;
    }

    private static Color ParseColor(string s) => Color.Parse(s.Trim());

    // ───────────────────────────── 色板 ─────────────────────────────

    [Fact]
    public void ContractBrushKeys_AreTheNineFromTheBrief()
    {
        Assert.Equal(new[]
        {
            "B.EditOutline", "B.EditOutlineHover", "B.EditOutlineOnDark", "B.EditDropFill", "B.EditColShade",
            "B.EditBarBg", "B.EditDim", "B.WarnSoft", "B.WarnInk",
        }, LayoutVisualTokens.BrushKeys.Contract);
        Assert.All(LayoutVisualTokens.BrushKeys.Contract, k => Assert.Contains(k, LayoutVisualTokens.BrushKeys.All));
    }

    [Theory]
    [InlineData("Palette.Light.axaml")]
    [InlineData("Palette.Dark.axaml")]
    public void Palette_HasEveryV3BrushAndShadow(string file)
    {
        var keyed = Keyed(file);
        foreach (var key in LayoutVisualTokens.BrushKeys.All)
        {
            Assert.True(keyed.TryGetValue(key, out var e), $"{file} 缺少 {key}");
            Assert.Equal("SolidColorBrush", e!.Name.LocalName);
        }
        foreach (var key in LayoutVisualTokens.ShadowKeys.All)
        {
            Assert.True(keyed.TryGetValue(key, out var e), $"{file} 缺少 {key}");
            Assert.Equal("BoxShadows", e!.Name.LocalName);
            BoxShadows.Parse(e.Value.Trim()); // 能解析
        }
    }

    [Theory]
    [InlineData("Palette.Light.axaml", false)]
    [InlineData("Palette.Dark.axaml", true)]
    public void FallbackColors_MatchPaletteExactly(string file, bool dark)
    {
        var keyed = Keyed(file);
        var table = dark ? LayoutVisualTokens.FallbackDark : LayoutVisualTokens.FallbackLight;
        Assert.Equal(LayoutVisualTokens.BrushKeys.All.Length, table.Count);
        foreach (var key in LayoutVisualTokens.BrushKeys.All)
        {
            var e = keyed[key];
            Assert.Null(e.Attribute("Opacity")); // 新键一律把透明度写进 #AARRGGBB,兜底才能逐字对齐
            Assert.Equal(ParseColor((string)e.Attribute("Color")!), table[key]);
        }
    }

    [Theory]
    [InlineData("Palette.Light.axaml", false)]
    [InlineData("Palette.Dark.axaml", true)]
    public void FallbackShadows_MatchPaletteExactly(string file, bool dark)
    {
        var keyed = Keyed(file);
        var table = dark ? LayoutVisualTokens.FallbackShadowDark : LayoutVisualTokens.FallbackShadowLight;
        foreach (var key in LayoutVisualTokens.ShadowKeys.All)
        {
            Assert.Equal(BoxShadows.Parse(keyed[key].Value.Trim()), BoxShadows.Parse(table[key]));
        }
    }

    [Fact]
    public void GlassAndOnDarkKeys_AreIdenticalInLightAndDark()
    {
        // 只用在永远是深色的启动页英雄图上:明暗两套必须同值
        var same = new[]
        {
            "B.EditOutlineOnDark", "B.EditOutlineHoverOnDark", "B.EditDropFillOnDark", "B.EditColShadeOnDark", "B.EditDim",
            "B.EditGlass", "B.EditGlassLine", "B.EditGlassChip", "B.EditGlassChipLine", "B.OnEditGlass", "B.OnEditGlassMuted",
        };
        foreach (var key in same)
        {
            Assert.Equal(LayoutVisualTokens.FallbackLight[key], LayoutVisualTokens.FallbackDark[key]);
        }
    }

    [Fact]
    public void MockupValues_AreKept()
    {
        // layout3.html:--page-line2 = rgba(0,0,0,.16);hover = rgba(0,0,0,.34);--page-warn = #b45309;--page-warn-bg = #fdf3e2
        Assert.Equal(Color.FromArgb(0x29, 0, 0, 0), LayoutVisualTokens.FallbackLight["B.EditOutline"]);
        Assert.Equal(Color.FromArgb(0x57, 0, 0, 0), LayoutVisualTokens.FallbackLight["B.EditOutlineHover"]);
        Assert.Equal(Color.Parse("#B45309"), LayoutVisualTokens.FallbackLight["B.WarnInk"]);
        Assert.Equal(Color.Parse("#FDF3E2"), LayoutVisualTokens.FallbackLight["B.WarnSoft"]);
        Assert.Equal(316.0, LayoutVisualTokens.DrawerWidth);
        Assert.Equal(46.0, LayoutVisualTokens.EditBarHeight);
        Assert.Equal(24.0, LayoutVisualTokens.EditLabelHeight);
        Assert.Equal(26.0, LayoutVisualTokens.IconActWidth);
        Assert.Equal(4.0, LayoutVisualTokens.OutlineInset);
        Assert.Equal(1.5, LayoutVisualTokens.OutlineThickness);
    }

    // ───────────────────────────── Controls.axaml ─────────────────────────────

    [Fact]
    public void Controls_HasEveryGeometryWithFallbackIdentical() => TestApp.Run(() =>
    {
        var keyed = Keyed("Controls.axaml");
        Assert.Equal(new[] { "Geo.Eye", "Geo.EyeOff", "Geo.Lock", "Geo.Undo", "Geo.Redo", "Geo.Grip" }, LayoutVisualTokens.GeoKeys.Contract);
        foreach (var key in LayoutVisualTokens.GeoKeys.All)
        {
            Assert.True(keyed.TryGetValue(key, out var e), $"Controls.axaml 缺少 {key}");
            Assert.Equal("StreamGeometry", e!.Name.LocalName);
            Assert.Equal(LayoutVisualTokens.FallbackGeometry[key], e.Value.Trim());
            var g = Geometry.Parse(e.Value.Trim());
            // 16×16 视框内
            Assert.InRange(g.Bounds.Left, 0.0, 16.0);
            Assert.InRange(g.Bounds.Right, 0.0, 16.0);
            Assert.InRange(g.Bounds.Top, 0.0, 16.0);
            Assert.InRange(g.Bounds.Bottom, 0.0, 16.0);
        }
    });

    [Fact]
    public void Controls_HasEveryThemeAndOnDarkVariant()
    {
        var keyed = Keyed("Controls.axaml");
        Assert.Equal(new[] { "Layout.EditBar", "Layout.EditLabel", "Layout.IconAct", "Layout.Drawer", "Layout.SegItem" }, LayoutVisualTokens.ThemeKeys.Contract);
        foreach (var key in LayoutVisualTokens.ThemeKeys.All)
        {
            Assert.True(keyed.TryGetValue(key, out var e), $"Controls.axaml 缺少 {key}");
            Assert.Equal("ControlTheme", e!.Name.LocalName);
        }
        foreach (var key in LayoutVisualTokens.ThemeKeys.WithOnDark)
        {
            Assert.True(keyed.ContainsKey(LayoutVisualTokens.ThemeKeys.OnDark(key)), $"Controls.axaml 缺少深色玻璃版 {key}.OnDark");
        }
    }

    [Fact]
    public void V3Section_UsesOnlyRadius8And12_AndOnlyDynamicPaletteColors()
    {
        var text = File.ReadAllText(ThemeFile("Controls.axaml"));
        int start = text.IndexOf("R35 lin-l0", StringComparison.Ordinal);
        Assert.True(start > 0, "找不到 R35 lin-l0 段落标记");
        var section = text.Substring(start);
        foreach (Match m in Regex.Matches(section, "Property=\"(CornerRadius|RadiusX|RadiusY)\" Value=\"([^\"]+)\""))
        {
            Assert.Contains(m.Groups[2].Value, new[] { "8", "12" });
        }
        // 模板里的 CornerRadius 属性:8 / 12,唯一例外是 2px 灰条的 1(半径 = 线宽一半)
        foreach (Match m in Regex.Matches(section, " CornerRadius=\"([^\"]+)\""))
        {
            Assert.Contains(m.Groups[1].Value, new[] { "1", "8", "12" });
        }
        // 新段落里引用的每个 DynamicResource 键都必须在两个色板里(或是本文件自己的键)
        var light = Keyed("Palette.Light.axaml");
        var dark = Keyed("Palette.Dark.axaml");
        foreach (Match m in Regex.Matches(section, "\\{DynamicResource ([^}]+)\\}"))
        {
            var key = m.Groups[1].Value.Trim();
            Assert.True(light.ContainsKey(key) && dark.ContainsKey(key), $"DynamicResource {key} 不在两个色板里");
        }
    }

    // ───────────────────────────── 运行期:资源可取、主题能套 ─────────────────────────────

    private static void Mount()
    {
        TipCL.App.Ui.Ui.EnsureMounted();
        TipCL.App.Ui.Ui.ApplyTheme(TipCL.App.Ui.Ui.IsDark); // 只补挂当前色板,不切明暗(别的用例并行时不受影响)
    }

    [Fact]
    public void Runtime_EveryKeyResolvesToTheRightType() => TestApp.Run(() =>
    {
        Mount();
        foreach (var key in LayoutVisualTokens.BrushKeys.All)
        {
            Assert.IsAssignableFrom<ISolidColorBrush>(LayoutVisualTokens.Resource(key));
        }
        foreach (var key in LayoutVisualTokens.ShadowKeys.All)
        {
            Assert.IsType<BoxShadows>(LayoutVisualTokens.Resource(key));
            Assert.NotEqual(0, LayoutVisualTokens.ShadowOr(key).Count);
        }
        foreach (var key in LayoutVisualTokens.GeoKeys.All)
        {
            Assert.IsAssignableFrom<Geometry>(LayoutVisualTokens.Resource(key));
            var a = LayoutVisualTokens.GeometryOf(key);
            var b = LayoutVisualTokens.GeometryOf(key);
            Assert.NotNull(a);
            Assert.NotSame(a, b); // 每次给新实例,调用方随便改 Transform
        }
        foreach (var key in LayoutVisualTokens.ThemeKeys.All.Concat(LayoutVisualTokens.ThemeKeys.WithOnDark.Select(LayoutVisualTokens.ThemeKeys.OnDark)))
        {
            Assert.NotNull(LayoutVisualTokens.Theme(key));
        }
    });

    [Fact]
    public void Runtime_EveryThemeAppliesToItsTargetType() => TestApp.Run(() =>
    {
        Mount();
        var keys = LayoutVisualTokens.ThemeKeys.All.Concat(LayoutVisualTokens.ThemeKeys.WithOnDark.Select(LayoutVisualTokens.ThemeKeys.OnDark));
        var panel = new StackPanel();
        var window = new Window { Content = panel, Width = 600, Height = 400 };
        var made = new List<(string Key, StyledElement El)>();
        foreach (var key in keys)
        {
            var theme = LayoutVisualTokens.Theme(key)!;
            var el = (Control)Activator.CreateInstance(theme.TargetType!)!;
            Assert.True(LayoutVisualTokens.ApplyTheme(el, key), key);
            Assert.Same(theme, el.Theme);
            panel.Children.Add(el);
            made.Add((key, el));
        }
        // SegItem 也能套到 RadioButton(RadioButton 继承 ToggleButton)
        var radio = new RadioButton();
        Assert.True(LayoutVisualTokens.ApplyTheme(radio, LayoutVisualTokens.ThemeKeys.SegItem));
        panel.Children.Add(radio);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Border bar = (Border)made.First(m => m.Key == "Layout.EditBar").El;
        Assert.Equal(46.0, bar.Height);
        Assert.Equal(new CornerRadius(12), bar.CornerRadius);
        Assert.NotEqual(0, bar.BoxShadow.Count);
        Border drawer = (Border)made.First(m => m.Key == "Layout.Drawer").El;
        Assert.Equal(316.0, drawer.Width);
        Border label = (Border)made.First(m => m.Key == "Layout.EditLabel").El;
        Assert.Equal(24.0, label.Height);
        Button act = (Button)made.First(m => m.Key == "Layout.IconAct").El;
        Assert.Equal(26.0, act.Width);
        Assert.Equal(24.0, act.Height);
        ToggleButton seg = (ToggleButton)made.First(m => m.Key == "Layout.SegItem").El;
        Assert.Equal(24.0, seg.Height);
        window.Close();
    });

    [Fact]
    public void Runtime_EditOutlineClassesSwitchDashedToSolid() => TestApp.Run(() =>
    {
        Mount();
        var rect = new Rectangle();
        Assert.True(LayoutVisualTokens.ApplyTheme(rect, LayoutVisualTokens.ThemeKeys.EditOutline));
        var window = new Window { Content = new Border { Width = 200, Height = 100, Child = rect } };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(1.5, rect.StrokeThickness);
        Assert.NotNull(rect.StrokeDashArray);
        Assert.Equal(new Thickness(-4), rect.Margin);
        Assert.False(rect.IsHitTestVisible);

        rect.Classes.Add(LayoutVisualTokens.OutlineClasses.Selected);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(2.0, rect.StrokeThickness);
        Assert.Null(rect.StrokeDashArray);
        var accent = LayoutVisualTokens.AccentColor();
        Assert.Equal(accent, (rect.Stroke as ISolidColorBrush)?.Color);

        rect.Classes.Remove(LayoutVisualTokens.OutlineClasses.Selected);
        rect.Classes.Add(LayoutVisualTokens.OutlineClasses.Hover);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(1.5, rect.StrokeThickness);
        Assert.Equal(LayoutVisualTokens.ColorOr("B.EditOutlineHover"), (rect.Stroke as ISolidColorBrush)?.Color);
        window.Close();
    });

    [Fact]
    public void Runtime_ApplyTheme_NeverThrows() => TestApp.Run(() =>
    {
        Mount();
        Assert.False(LayoutVisualTokens.ApplyTheme(new Border(), "Layout.NoSuchTheme"));
        // 类型不匹配(EditBar 是 Border 主题)→ false,不抛
        Assert.False(LayoutVisualTokens.ApplyTheme(new TextBlock(), LayoutVisualTokens.ThemeKeys.EditBar));
        // OnDark 找不到就退回普通版
        var tip = new Border();
        Assert.True(LayoutVisualTokens.ApplyTheme(tip, LayoutVisualTokens.ThemeKeys.EditTip, onDark: true));
        Assert.Same(LayoutVisualTokens.Theme(LayoutVisualTokens.ThemeKeys.EditTip), tip.Theme);
        // OnDark 有就用 OnDark
        var bar = new Border();
        Assert.True(LayoutVisualTokens.ApplyTheme(bar, LayoutVisualTokens.ThemeKeys.EditBar, onDark: true));
        Assert.Same(LayoutVisualTokens.Theme("Layout.EditBar.OnDark"), bar.Theme);
        Assert.Null(LayoutVisualTokens.Resource("B.NoSuchKey"));
        Assert.Equal(Colors.Red, LayoutVisualTokens.ColorOr("B.NoSuchKey", Colors.Red));
        Assert.Null(LayoutVisualTokens.GeometryOf("Geo.NoSuchKey"));
    });

    [Theory]
    [InlineData("Geo.Undo", 15.0, 1.5)]
    [InlineData("Geo.EyeOff", 14.0, 1.4)]
    [InlineData("Geo.Lock", 13.0, 1.3)]
    [InlineData("Geo.Grip", 12.0, 1.65)]
    [InlineData("Geo.Eye", 16.0, 1.6)]
    public void Glyph_ScalesGeometryAndStrokeLikeTheMockup(string key, double size, double stroke) => TestApp.Run(() =>
    {
        Mount();
        var path = LayoutVisualTokens.Glyph(key, size);
        Assert.NotNull(path.Data);
        Assert.Equal(size, path.Width);
        Assert.Equal(size, path.Height);
        Assert.Equal(stroke, path.StrokeThickness, 3);
        Assert.Equal(PenLineCap.Round, path.StrokeLineCap);
        Assert.Equal(Stretch.None, path.Stretch);
        if (Math.Abs(size - 16.0) > 0.001)
        {
            var st = Assert.IsType<ScaleTransform>(path.Data!.Transform);
            Assert.Equal(size / 16.0, st.ScaleX, 6);
        }
        Assert.NotNull(path.Stroke);
    });

    [Fact]
    public void Glyph_FollowsOwnerForeground() => TestApp.Run(() =>
    {
        Mount();
        var btn = new Button { Foreground = Brushes.Orange };
        var path = LayoutVisualTokens.Glyph(LayoutVisualTokens.GeoKeys.EyeOff, 14, btn);
        btn.Content = path;
        Assert.Same(Brushes.Orange, path.Stroke);
        btn.Foreground = Brushes.Blue;
        Assert.Same(Brushes.Blue, path.Stroke);
    });

    // ───────────────────────────── 截图开关(纯函数) ─────────────────────────────

    private static Func<string, string?> Env(params (string K, string V)[] kv)
    {
        var d = kv.ToDictionary(p => p.K, p => p.V);
        return k => d.TryGetValue(k, out var v) ? v : null;
    }

    [Fact]
    public void Shot_NoSwitches_KeepsSmokeBehaviour()
    {
        var o = SmokeShotOptions.Parse(new[] { "TipCL.App", "--smoke" }, Env(("SMOKE_PAGE", "download")));
        Assert.False(o.Any);
        Assert.False(o.NeedsEdit);
        Assert.Equal("download", o.Page);
        Assert.Null(o.SubPage);
        Assert.Null(o.Output);
        Assert.Empty(o.Errors);

        var none = SmokeShotOptions.Parse(null, null);
        Assert.False(none.Any);
        Assert.Null(none.Page);
    }

    [Fact]
    public void Shot_WindowsStyleArgs()
    {
        var o = SmokeShotOptions.Parse(new[] { "--smoke", "--shot", "download", "--edit", "--select", "list", "--pop", "layout-drawer", "--dark", "--out", "a.png" }, null);
        Assert.Equal("download", o.Page);
        Assert.True(o.Edit);
        Assert.Null(o.EditPage);
        Assert.Equal("list", o.Select);
        Assert.True(o.PopDrawer);
        Assert.True(o.Dark);
        Assert.Equal("a.png", o.Output);
        Assert.True(o.Any);
        Assert.Empty(o.Errors);
    }

    [Fact]
    public void Shot_SelectOrPopImpliesEdit()
    {
        Assert.True(SmokeShotOptions.Parse(new[] { "--select", "list" }, null).NeedsEdit);
        Assert.True(SmokeShotOptions.Parse(new[] { "--pop", "layout-drawer" }, null).NeedsEdit);
        Assert.False(SmokeShotOptions.Parse(new[] { "--expand", "advanced" }, null).NeedsEdit);
    }

    [Fact]
    public void Shot_ExpandAdvanced_DefaultsToSettingsLayout()
    {
        var o = SmokeShotOptions.Parse(new[] { "--expand", "advanced" }, null);
        Assert.True(o.ExpandAdvanced);
        Assert.Equal("settings:layout", o.Page);
        Assert.Equal("settings", o.NavPage);
        Assert.Equal("layout", o.SubPage);

        var kept = SmokeShotOptions.Parse(new[] { "--shot", "settings:layout", "--expand", "advanced" }, null);
        Assert.Equal("settings:layout", kept.Page);
    }

    [Fact]
    public void Shot_EnvVariables_AndArgsWin()
    {
        var o = SmokeShotOptions.Parse(Array.Empty<string>(), Env(("SMOKE_EDIT", "settings:general"), ("SMOKE_SELECT", "w1"), ("SMOKE_POP", "layout-drawer"), ("SMOKE_EXPAND", "advanced"), ("SMOKE_DARK", "1")));
        Assert.True(o.Edit);
        Assert.Equal("settings:general", o.EditPage);
        Assert.Equal("w1", o.Select);
        Assert.True(o.PopDrawer);
        Assert.True(o.ExpandAdvanced);
        Assert.True(o.Dark);

        var plain = SmokeShotOptions.Parse(Array.Empty<string>(), Env(("SMOKE_EDIT", "1")));
        Assert.True(plain.Edit);
        Assert.Null(plain.EditPage);
        Assert.False(SmokeShotOptions.Parse(Array.Empty<string>(), Env(("SMOKE_EDIT", "0"))).Edit);

        var win = SmokeShotOptions.Parse(new[] { "--shot", "java", "--select", "detected" }, Env(("SMOKE_PAGE", "download"), ("SMOKE_SELECT", "list")));
        Assert.Equal("java", win.Page);
        Assert.Equal("detected", win.Select);

        var editPage = SmokeShotOptions.Parse(new[] { "--edit", "multiplayer", "--dark" }, null);
        Assert.Equal("multiplayer", editPage.EditPage);
        Assert.True(editPage.Dark);
    }

    [Fact]
    public void Shot_BadValuesAreReportedNotThrown()
    {
        var o = SmokeShotOptions.Parse(new[] { "--pop", "menu", "--expand", "all", "--select" }, null);
        Assert.False(o.PopDrawer);
        Assert.False(o.ExpandAdvanced);
        Assert.Null(o.Select);
        Assert.Equal(3, o.Errors.Count);
        Assert.False(o.Any);
    }

    private sealed class FakeEditor
    {
        public string? Selected;
        public bool DrawerOpen;

        internal bool SelectBlock(string id)
        {
            Selected = id;
            return true;
        }

        internal void ShowDrawer(bool open) => DrawerOpen = open;
    }

    private sealed class EmptyEditor
    {
    }

    [Fact]
    public void Shot_ReflectionFallbacks()
    {
        var fake = new FakeEditor();
        Assert.True(SmokeRunner.ReflectSelect(fake, "list"));
        Assert.Equal("list", fake.Selected);
        Assert.True(SmokeRunner.ReflectOpenDrawer(fake));
        Assert.True(fake.DrawerOpen);

        Assert.False(SmokeRunner.ReflectSelect(new EmptyEditor(), "x"));
        Assert.False(SmokeRunner.ReflectOpenDrawer(new EmptyEditor()));
    }
}
