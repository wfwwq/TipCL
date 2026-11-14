using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using UiFacade = TipCL.App.Ui.Ui;

namespace TipCL.App.Common;

/// <summary>
/// 主题色动态派生（移植自 Windows 版 src/TipCL/Pages/SettingsPage.Themes.cs 的
/// ApplyAccent/ApplyVisuals，纯 Color 数学；WPF ResourceDictionary 写法换成
/// Avalonia Application.Resources 直接覆盖 B.* 键）。
/// 由用户 hex 派生 24 个主题键 + 4 套背景预设（default/eye/warm/mist）。
/// </summary>
public static class ThemeService
{
    /// <summary>accent 派生的 24 键（照 Windows 版 SettingsPage.AccentKeys）。</summary>
    public static readonly string[] AccentKeys =
    {
        "C.Accent", "C.AccentStrong", "C.AccentText", "C.AccentDeep", "C.AccentLite",
        "B.Accent", "B.AccentStrong", "B.OnAccentStrong", "B.AccentText", "B.AccentTint",
        "B.AccentDeep", "B.AccentLite", "B.AccentSoft", "B.AccentSoft2", "B.OnAccent",
        "B.Glass", "B.GlassStrong", "B.GlassBorder", "B.Ok", "B.OnOk",
        "B.BannerFill", "B.OnBanner", "B.LogoFill", "B.OnLogo",
    };

    private static readonly string[] BackgroundKeys = { "C.Canvas", "B.Canvas", "B.PageWash" };

    public const string DefaultAccent = "#2E9B6B";

    /// <summary>最近应用的视觉（明暗切换后重放）。</summary>
    public static string CurrentColor { get; private set; } = DefaultAccent;

    public static string CurrentBackground { get; private set; } = "default";

    /// <summary>视觉被重放/应用后触发（外壳可刷新预览）。</summary>
    public static event Action? VisualsChanged;

    /// <summary>
    /// 应用主题色 + 背景预设。dark 不传时按 UiFacade.IsDark。
    /// default（跟随主题）之外的颜色走派生；背景预设覆盖 C.Canvas/B.Canvas/B.PageWash。
    /// </summary>
    public static void Apply(string? colorHex, string? background = "default", bool? dark = null)
    {
        string color = NormalizeColor(colorHex) ?? DefaultAccent;
        string bg = NormalizeBackground(background);
        CurrentColor = color;
        CurrentBackground = bg;
        var app = Application.Current;
        if (app == null) return;
        try
        {
            bool isDark = dark ?? UiFacade.IsDark;
            // 取当前 Palette 的原值，default 档直接回原值
            bool isDefaultColor = string.Equals(color, DefaultAccent, StringComparison.OrdinalIgnoreCase);
            Color paper = ResColor(app, "C.Paper") ?? (isDark ? Color.FromRgb(30, 30, 30) : Colors.White);
            if (isDefaultColor)
            {
                foreach (string key in AccentKeys)
                {
                    var v = Res(app, key);
                    if (v != null) app.Resources[key] = v;
                }
            }
            else
            {
                ApplyAccent(app.Resources, Parse(color), paper, isDark);
            }

            switch (bg)
            {
                case "eye":
                    SetBackground(app, isDark ? "#121A15" : "#EBF4EC",
                        isDark ? new[] { "#141D17", "#16211A", "#121A15" } : new[] { "#EFF7F0", "#EAF4EB", "#EDF6EE" });
                    break;
                case "warm":
                    SetBackground(app, isDark ? "#1B1813" : "#F7F3EA",
                        isDark ? new[] { "#1D1A15", "#201C16", "#1B1813" } : new[] { "#F9F5ED", "#F6F2E8", "#F8F4EC" });
                    break;
                case "mist":
                    SetBackground(app, isDark ? "#14181D" : "#EDF1F6",
                        isDark ? new[] { "#161B21", "#181E25", "#14181D" } : new[] { "#F0F4F9", "#EBF0F6", "#EEF2F8" });
                    break;
                default:
                    foreach (string key in BackgroundKeys)
                    {
                        var v = Res(app, key);
                        if (v != null) app.Resources[key] = v;
                    }
                    break;
            }
        }
        catch (Exception)
        {
            // 派生失败不崩 UI，保持当前主题（照 Windows 版吞异常语义）
        }
        VisualsChanged?.Invoke();
    }

    /// <summary>明暗切换后由外壳重放（依赖申请：外壳 SetDark 后调用此方法）。</summary>
    public static void Replay() => Apply(CurrentColor, CurrentBackground);

    private static void SetBackground(Application app, string canvasHex, string[] wash)
    {
        Color canvas = Parse(canvasHex);
        app.Resources["C.Canvas"] = canvas;
        app.Resources["B.Canvas"] = new SolidColorBrush(canvas);
        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.9, 1, RelativeUnit.Relative),
        };
        gradient.GradientStops.Add(new GradientStop(Parse(wash[0]), 0.0));
        gradient.GradientStops.Add(new GradientStop(Parse(wash[1]), 0.55));
        gradient.GradientStops.Add(new GradientStop(Parse(wash[2]), 1.0));
        app.Resources["B.PageWash"] = gradient;
    }

    /// <summary>由 accent 派生 24 键（照 Windows 版 SettingsPage.Themes.ApplyAccent 逐行移植）。</summary>
    private static void ApplyAccent(IDictionary<object, object?> d, Color accent, Color paper, bool dark)
    {
        Color strong = EnsureWhiteSurface(accent, 4.8);
        Color textHi = EnsureTextContrast(accent, paper, dark, 4.8);
        Color baseAccent = EnsureTextContrast(accent, paper, dark, 1.6);
        Color lite = Mix(baseAccent, Colors.White, dark ? 0.28 : 0.3);
        Color soft = Mix(paper, baseAccent, dark ? 0.16 : 0.105);
        Color soft2 = Mix(paper, baseAccent, dark ? 0.24 : 0.165);

        d["C.Accent"] = baseAccent;
        d["C.AccentStrong"] = strong;
        d["C.AccentText"] = textHi;
        d["C.AccentDeep"] = textHi;
        d["C.AccentLite"] = lite;
        d["B.Accent"] = new SolidColorBrush(baseAccent);
        d["B.AccentStrong"] = new SolidColorBrush(strong);
        d["B.OnAccentStrong"] = new SolidColorBrush(Colors.White);
        d["B.AccentText"] = new SolidColorBrush(textHi);
        d["B.AccentTint"] = new SolidColorBrush(Mix(paper, baseAccent, dark ? 0.1 : 0.07));
        d["B.AccentDeep"] = new SolidColorBrush(textHi);
        d["B.AccentLite"] = new SolidColorBrush(lite);
        d["B.AccentSoft"] = new SolidColorBrush(soft);
        d["B.AccentSoft2"] = new SolidColorBrush(soft2);
        d["B.OnAccent"] = new SolidColorBrush(Colors.White);

        Color glass = Mix(paper, baseAccent, dark ? 0.1 : 0.07);
        Color glassStrong = Mix(paper, baseAccent, dark ? 0.07 : 0.045);
        Color glassBorder = Mix(paper, baseAccent, dark ? 0.26 : 0.2);
        d["B.Glass"] = new SolidColorBrush(glass) { Opacity = dark ? 0.72 : 0.76 };
        d["B.GlassStrong"] = new SolidColorBrush(glassStrong) { Opacity = dark ? 0.86 : 0.88 };
        d["B.GlassBorder"] = new SolidColorBrush(glassBorder) { Opacity = dark ? 0.62 : 0.68 };

        Color ok = dark ? Color.FromRgb(63, 176, 124) : Color.FromRgb(46, 155, 107);
        d["B.Ok"] = new SolidColorBrush(ok);
        d["B.OnOk"] = new SolidColorBrush(ContrastForeground(ok));

        var banner = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        };
        banner.GradientStops.Add(new GradientStop(Mix(strong, Colors.White, 0.06), 0.0));
        banner.GradientStops.Add(new GradientStop(strong, 0.52));
        banner.GradientStops.Add(new GradientStop(Mix(strong, Colors.Black, 0.18), 1.0));
        d["B.BannerFill"] = banner;
        d["B.OnBanner"] = new SolidColorBrush(Colors.White);

        var logo = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        };
        logo.GradientStops.Add(new GradientStop(Mix(strong, Colors.White, 0.08), 0.0));
        logo.GradientStops.Add(new GradientStop(Mix(strong, Colors.Black, 0.16), 1.0));
        d["B.LogoFill"] = logo;
        d["B.OnLogo"] = new SolidColorBrush(Colors.White);
    }

    // ───────────────────────────── 色彩数学（照搬 Windows 版） ─────────────────────────────

    public static Color Parse(string hex)
    {
        try
        {
            if (Color.Parse(hex) is Color c) return c;
        }
        catch (Exception)
        {
        }
        return Colors.SeaGreen;
    }

    public static string? NormalizeColor(string? hex)
    {
        string text = hex?.Trim() ?? "";
        if (text.Length == 9 && text[0] == '#' && text.Substring(1, 2).Equals("FF", StringComparison.OrdinalIgnoreCase))
            text = "#" + text.Substring(3);
        if (text.Length != 7 || text[0] != '#') return null;
        for (int i = 1; i < 7; i++)
            if (!Uri.IsHexDigit(text[i]))
                return null;
        return text.ToUpperInvariant();
    }

    public static string NormalizeBackground(string? bg) => bg switch
    {
        "eye" => "eye",
        "warm" => "warm",
        "mist" => "mist",
        _ => "default",
    };

    private static Color EnsureWhiteSurface(Color color, double minRatio)
    {
        if (ContrastRatio(color, Colors.White) >= minRatio) return color;
        for (int i = 1; i <= 20; i++)
        {
            Color c = Mix(color, Colors.Black, i * 0.035);
            if (ContrastRatio(c, Colors.White) >= minRatio) return c;
        }
        return Mix(color, Colors.Black, 0.7);
    }

    private static Color EnsureTextContrast(Color color, Color paper, bool dark, double minRatio)
    {
        if (ContrastRatio(color, paper) >= minRatio) return color;
        Color toward = dark ? Colors.White : Colors.Black;
        for (int i = 1; i <= 20; i++)
        {
            Color c = Mix(color, toward, i * 0.035);
            if (ContrastRatio(c, paper) >= minRatio) return c;
        }
        return toward;
    }

    private static Color ContrastForeground(Color bg)
    {
        double w = ContrastRatio(bg, Colors.White);
        double b = ContrastRatio(bg, Colors.Black);
        return w >= b ? Colors.White : Colors.Black;
    }

    public static double ContrastRatio(Color a, Color b)
    {
        double l1 = RelativeLuminance(a);
        double l2 = RelativeLuminance(b);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    private static double RelativeLuminance(Color c)
    {
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        static double Ch(byte v)
        {
            double n = v / 255.0;
            return n <= 0.04045 ? n / 12.92 : Math.Pow((n + 0.055) / 1.055, 2.4);
        }
    }

    public static Color Mix(Color a, Color b, double t)
        => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));

    private static Color? ResColor(Application app, string key)
        => app.TryGetResource(key, null, out var v) && v is Color c ? c : null;

    private static object? Res(Application app, string key)
        => app.TryGetResource(key, null, out var v) ? v : null;
}
