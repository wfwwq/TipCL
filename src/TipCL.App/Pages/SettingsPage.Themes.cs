using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TipCL.Models;
using TipCL.Services;
using TipCL.Themes;

namespace TipCL.Pages;

/// <summary>
/// 主题包管理 + 视觉应用 + 颜色规范化(Windows 的 <c>SettingsPage.Themes.cs</c>,390 行;WP10 从主文件机械提取,
/// partial class,行为不变 —— 见 Windows 的 D-24)。mac 侧同样切出来,行号与结构尽量一一对应。
///
/// <b>【B 类差异 · Lead 裁决】</b>Windows 的 <c>ApplyVisuals</c> 直接往 WPF 的
/// <c>App.PaletteDictionary</c>(运行时可写 ResourceDictionary)里写 24 个强调色键 + 3 个背景键,
/// 并把内置色板副本当"恢复默认"的来源(<c>new ResourceDictionary { Source = App.PaletteUri(isDark) }</c>)。
/// mac 侧:
/// <list type="bullet">
/// <item>可写落点 = <see cref="PaletteOverride"/>(<c>Themes\PaletteOverride.cs</c>,本包新增的**主题层**能力),
///   本文件一个字节都不碰资源字典 —— 设置页不该私藏一个"谁都不知道存在的全局"(Lead 裁决,与 C3d 的
///   「FlyToTasks 一次修在外壳」同一条道理);</item>
/// <item>"内置值"从 <see cref="PaletteOverride.BuiltinColor"/> 直读 axaml(**绕开**覆盖层)—— 就是 Windows 那份
///   <c>new ResourceDictionary { Source = ... }</c> 的等价物;不绕开的话"把主题色恢复成默认绿"会读到被覆盖过的值;</item>
/// <item>覆盖层是**不按主题分**的(一层普通键值),所以主题变体一变必须重算一遍
///   (<c>OnThemeVariantChanged</c> / <see cref="SettingsPage.ApplyDark"/> 里都调了 <see cref="ApplyVisuals()"/>);
///   Windows 因为每个变体有一份自己的字典,天生不用重算 —— 这是 mac 侧唯一需要补的一步;</item>
/// <item><c>App.IsDark</c> → <see cref="PaletteOverride.IsDarkNow"/>;<c>App.RaiseThemeVisualsChanged()</c> →
///   <c>PageBase.Win?.ApplyThemeVisuals()</c>(mac 的外壳成员,按当前变体重刷标题栏/侧栏/底纹取色)。</item>
/// </list>
/// </summary>
public sealed partial class SettingsPage
{
    private async Task ReloadThemesAsync()
    {
        List<ThemeRow> list = (await PageBase.Api.TryCallAsync("list_themes", null, new List<ThemeRow>())) ?? new List<ThemeRow>();
        string keep = _themePack.Str();
        _themePack.Fill(list.Select((ThemeRow t) => t.Name), keep);
        _themeInfo.Text = (list.Count == 0)
            ? I18n.L("还没有主题包。调好配色后点「保存为主题包」。")
            : (I18n.L("共 {0} 个:", list.Count) + string.Join(I18n.L("、"), list.Take(6).Select((ThemeRow t) => I18n.L("{0}({1} {2})", t.Name, t.UiDark ? I18n.L("深色") : I18n.L("浅色"), t.ThemeColor))));
    }

    private async Task SaveThemeAsync()
    {
        string? name = await Dlg.Prompt(I18n.L("保存主题包"), I18n.L("主题包名称"), I18n.L("我的主题"));
        if (!string.IsNullOrWhiteSpace(name))
        {
            await PageBase.Api.CallAsync<object>("save_settings", new
            {
                data = new Dictionary<string, object>
                {
                    ["theme_color"] = _color,
                    ["ui_dark"] = PaletteOverride.IsDarkNow,
                    ["ui_bg_preset"] = KeyOf(_bg),
                },
            });
            Extras.ThemeColor = _color;
            Extras.Background = KeyOf(_bg);
            SaveExtras();
            await PageBase.Api.CallAsync<object>("save_theme", new
            {
                name = name.Trim(),
            });
            PageBase.Toast(I18n.L("已保存"), I18n.L("主题包「{0}」已保存", name.Trim()), ToastKind.Success);
            await ReloadThemesAsync();
        }
    }

    private async Task LoadThemeAsync()
    {
        string name = _themePack.Str();
        if (string.IsNullOrEmpty(name))
        {
            PageBase.Toast(I18n.L("没有主题包"), I18n.L("先保存一个再来加载"), ToastKind.Warning);
            return;
        }
        await PageBase.Api.CallAsync<object>("load_theme", new
        {
            name,
        });
        await LoadAsync();
        await Wallpaper.ReloadAsync();
        SyncWallpaperInfo();
        PageBase.Toast(I18n.L("已加载"), I18n.L("主题包「{0}」已应用", name), ToastKind.Success);
    }

    private async Task DeleteThemeAsync()
    {
        string name = _themePack.Str();
        bool flag = !string.IsNullOrEmpty(name);
        if (flag)
        {
            flag = await Dlg.Confirm(I18n.L("删除主题包"), I18n.L("删除「{0}」?", name), I18n.L("删除"), I18n.L("取消"), danger: true);
        }
        if (flag)
        {
            await PageBase.Api.CallAsync<object>("delete_theme", new
            {
                name,
            });
            PageBase.Toast(I18n.L("已删除"), name, ToastKind.Success);
            await ReloadThemesAsync();
        }
    }

    private async Task ImportThemeAsync()
    {
        string? path = Dlg.PickFile(I18n.L("主题包 (*.json)|*.json|全部文件|*.*"), I18n.L("导入主题包"));
        if (path != null)
        {
            string text = await PageBase.Api.CallAsync<string>("import_theme", new
            {
                path,
            });
            PageBase.Toast(I18n.L("已导入"), string.IsNullOrEmpty(text) ? path : I18n.L("主题包「{0}」", text), ToastKind.Success);
            await ReloadThemesAsync();
        }
    }

    private async Task ExportThemeAsync()
    {
        string text = _themePack.Str();
        if (string.IsNullOrEmpty(text))
        {
            PageBase.Toast(I18n.L("没有主题包"), I18n.L("先保存一个再来导出"), ToastKind.Warning);
            return;
        }
        string? dest = Dlg.SaveFile(I18n.L("主题包 (*.json)|*.json"), text + ".json", I18n.L("导出主题包"));
        if (dest != null)
        {
            string text2 = await PageBase.Api.CallAsync<string>("export_theme", new
            {
                name = text,
                dest,
            });
            PageBase.Toast(I18n.L("已导出"), text2 ?? dest, ToastKind.Success);
        }
    }

    /// <summary>按当前保存的视觉设定重刷一遍(外壳主题变了、设置加载完了都调它)。</summary>
    internal static void ApplyVisuals()
    {
        ApplyVisuals(_visualColor, _visualBackground);
    }

    /// <summary>
    /// 【Windows 源】<c>SettingsPage.Themes.cs:125-205</c>:把主题色与背景预设落到全局色板上。
    /// 主题色是默认绿时**逐键恢复内置值**(等价于撤掉那些覆盖),否则整组重算(对比度补偿那一套见 <see cref="ApplyAccent"/>)。
    /// </summary>
    private static void ApplyVisuals(string color, string bg)
    {
        color = NormalizeColor(color) ?? DefaultColor;
        bg = NormalizeBackground(bg);
        _visualColor = color;
        _visualBackground = bg;
        try
        {
            bool isDark = PaletteOverride.IsDarkNow;
            Color paper = PaletteOverride.BuiltinColor("C.Paper");
            if (string.Equals(color, DefaultColor, StringComparison.OrdinalIgnoreCase))
            {
                foreach (string key in AccentKeys)
                {
                    // 撤掉这一条的覆盖 ⇒ 读到的就是内置色板里的值(Windows 是把内置字典里的同名项拷回来)
                    PaletteOverride.Remove(key);
                }
            }
            else
            {
                ApplyAccent(Opaque(Parse(color)), paper, isDark);
            }

            (string, string[]) tuple;
            switch (bg)
            {
                case "default":
                    foreach (string key2 in BackgroundKeys)
                    {
                        PaletteOverride.Remove(key2);
                    }
                    break;
                case "eye":
                    tuple = isDark
                        ? ("#121A15", new string[3] { "#141D17", "#16211A", "#121A15" })
                        : ("#EBF4EC", new string[3] { "#EFF7F0", "#EAF4EB", "#EDF6EE" });
                    ApplyBackground(tuple);
                    break;
                case "warm":
                    tuple = isDark
                        ? ("#1B1813", new string[3] { "#1D1A15", "#201C16", "#1B1813" })
                        : ("#F7F3EA", new string[3] { "#F9F5ED", "#F6F2E8", "#F8F4EC" });
                    ApplyBackground(tuple);
                    break;
                default:
                    tuple = isDark
                        ? ("#14181D", new string[3] { "#161B21", "#181E25", "#14181D" })
                        : ("#EDF1F6", new string[3] { "#F0F4F9", "#EBF0F6", "#EEF2F8" });
                    ApplyBackground(tuple);
                    break;
            }
        }
        catch (Exception)
        {
        }
        // Windows 是 App.RaiseThemeVisualsChanged();mac 的外壳成员同名同义(按变体重刷各处取色)。
        PageBase.Win?.ApplyThemeVisuals();
    }

    /// <summary>背景预设的三个色落盘(画布色 + 渐变洗色,与 Windows 的三段式一致)。</summary>
    private static void ApplyBackground((string Canvas, string[] Wash) tuple)
    {
        Color canvas = Parse(tuple.Canvas);
        PaletteOverride.SetColor("C.Canvas", canvas);
        PaletteOverride.SetBrush("B.Canvas", new SolidColorBrush(canvas));
        LinearGradientBrush wash = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.0, 0.0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.9, 1.0, RelativeUnit.Relative),
        };
        wash.GradientStops.Add(new GradientStop(Parse(tuple.Wash[0]), 0.0));
        wash.GradientStops.Add(new GradientStop(Parse(tuple.Wash[1]), 0.55));
        wash.GradientStops.Add(new GradientStop(Parse(tuple.Wash[2]), 1.0));
        PaletteOverride.SetBrush("B.PageWash", wash);
    }

    /// <summary>【Windows 源】<c>:207-267</c>:由主题色推导整组强调色(含对比度补偿与三处渐变)。</summary>
    private static void ApplyAccent(Color accent, Color paper, bool dark)
    {
        Color color = EnsureWhiteSurface(accent, 4.8);
        Color color2 = EnsureTextContrast(accent, paper, dark, 4.8);
        Color color3 = EnsureTextContrast(accent, paper, dark, 1.6);
        Color color4 = Mix(color3, Colors.White, dark ? 0.28 : 0.3);
        Color color5 = Mix(paper, color3, dark ? 0.16 : 0.105);
        Color color6 = Mix(paper, color3, dark ? 0.24 : 0.165);
        PaletteOverride.SetColor("C.Accent", color3);
        PaletteOverride.SetColor("C.AccentStrong", color);
        PaletteOverride.SetColor("C.AccentText", color2);
        PaletteOverride.SetColor("C.AccentDeep", color2);
        PaletteOverride.SetColor("C.AccentLite", color4);
        PaletteOverride.SetBrush("B.Accent", new SolidColorBrush(color3));
        PaletteOverride.SetBrush("B.AccentStrong", new SolidColorBrush(color));
        PaletteOverride.SetBrush("B.OnAccentStrong", Brushes.White);
        PaletteOverride.SetBrush("B.AccentText", new SolidColorBrush(color2));
        PaletteOverride.SetBrush("B.AccentTint", new SolidColorBrush(Mix(paper, color3, dark ? 0.1 : 0.07)));
        PaletteOverride.SetBrush("B.AccentDeep", new SolidColorBrush(color2));
        PaletteOverride.SetBrush("B.AccentLite", new SolidColorBrush(color4));
        PaletteOverride.SetBrush("B.AccentSoft", new SolidColorBrush(color5));
        PaletteOverride.SetBrush("B.AccentSoft2", new SolidColorBrush(color6));
        PaletteOverride.SetBrush("B.OnAccent", Brushes.White);
        Color color7 = Mix(paper, color3, dark ? 0.1 : 0.07);
        Color color8 = Mix(paper, color3, dark ? 0.07 : 0.045);
        Color color9 = Mix(paper, color3, dark ? 0.26 : 0.2);
        PaletteOverride.SetBrush("B.Glass", new SolidColorBrush(color7)
        {
            Opacity = dark ? 0.72 : 0.76,
        });
        PaletteOverride.SetBrush("B.GlassStrong", new SolidColorBrush(color8)
        {
            Opacity = dark ? 0.86 : 0.88,
        });
        PaletteOverride.SetBrush("B.GlassBorder", new SolidColorBrush(color9)
        {
            Opacity = dark ? 0.62 : 0.68,
        });
        Color color10 = dark ? Color.FromRgb(63, 176, 124) : Color.FromRgb(46, 155, 107);
        PaletteOverride.SetBrush("B.Ok", new SolidColorBrush(color10));
        PaletteOverride.SetBrush("B.OnOk", new SolidColorBrush(ContrastForeground(color10)));
        LinearGradientBrush banner = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.0, 0.0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1.0, 1.0, RelativeUnit.Relative),
        };
        banner.GradientStops.Add(new GradientStop(Mix(color, Colors.White, 0.06), 0.0));
        banner.GradientStops.Add(new GradientStop(color, 0.52));
        banner.GradientStops.Add(new GradientStop(Mix(color, Colors.Black, 0.18), 1.0));
        PaletteOverride.SetBrush("B.BannerFill", banner);
        PaletteOverride.SetBrush("B.OnBanner", Brushes.White);
        LinearGradientBrush logo = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.0, 0.0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1.0, 1.0, RelativeUnit.Relative),
        };
        logo.GradientStops.Add(new GradientStop(Mix(color, Colors.White, 0.08), 0.0));
        logo.GradientStops.Add(new GradientStop(Mix(color, Colors.Black, 0.16), 1.0));
        PaletteOverride.SetBrush("B.LogoFill", logo);
        PaletteOverride.SetBrush("B.OnLogo", Brushes.White);
    }

    /// <summary>十六进制 → 颜色(【Windows 源】<c>:269-272</c> 的 <c>ColorConverter.ConvertFromString</c>);解析不了回落 SeaGreen。</summary>
    private static Color Parse(string hex)
    {
        string text = NormalizeColor(hex) ?? "";
        if (text.Length == 0)
        {
            return Colors.SeaGreen;
        }
        try
        {
            return Color.Parse(text);
        }
        catch (Exception)
        {
            return Colors.SeaGreen;
        }
    }

    private static Color Opaque(Color c)
    {
        return Color.FromRgb(c.R, c.G, c.B);
    }

    private static string NormalizeBackground(string? bg)
    {
        return bg switch
        {
            "eye" => "eye",
            "warm" => "warm",
            "mist" => "mist",
            _ => "default",
        };
    }

    private static Color EnsureWhiteSurface(Color color, double minRatio)
    {
        if (ContrastRatio(color, Colors.White) >= minRatio)
        {
            return color;
        }
        for (int i = 1; i <= 20; i++)
        {
            Color color2 = Mix(color, Colors.Black, (double)i * 0.035);
            if (ContrastRatio(color2, Colors.White) >= minRatio)
            {
                return color2;
            }
        }
        return Mix(color, Colors.Black, 0.7);
    }

    private static Color EnsureTextContrast(Color color, Color paper, bool dark, double minRatio)
    {
        if (ContrastRatio(color, paper) >= minRatio)
        {
            return color;
        }
        Color color2 = dark ? Colors.White : Colors.Black;
        for (int i = 1; i <= 20; i++)
        {
            Color color3 = Mix(color, color2, (double)i * 0.035);
            if (ContrastRatio(color3, paper) >= minRatio)
            {
                return color3;
            }
        }
        return color2;
    }

    private static Color ContrastForeground(Color bg)
    {
        Color black = Colors.Black;
        Color white = Colors.White;
        if (!(ContrastRatio(bg, white) >= ContrastRatio(bg, black)))
        {
            return black;
        }
        return white;
    }

    private static double ContrastRatio(Color a, Color b)
    {
        double val = RelativeLuminance(a);
        double val2 = RelativeLuminance(b);
        double num = Math.Max(val, val2);
        double num2 = Math.Min(val, val2);
        return (num + 0.05) / (num2 + 0.05);
    }

    private static double RelativeLuminance(Color c)
    {
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        static double Ch(byte v)
        {
            double num = (double)(int)v / 255.0;
            if (!(num <= 0.04045))
            {
                return Math.Pow((num + 0.055) / 1.055, 2.4);
            }
            return num / 12.92;
        }
    }

    /// <summary>【Windows 源】<c>:359-378</c>:规范化十六进制色(<c>#AARRGGBB</c> 里 alpha 为 FF 时压成 <c>#RRGGBB</c>)。</summary>
    internal static string? NormalizeColor(string? hex)
    {
        string text = hex?.Trim() ?? "";
        if (text.Length == 9 && text[0] == '#' && text.Substring(1, 2).Equals("FF", StringComparison.OrdinalIgnoreCase))
        {
            text = "#" + text.Substring(3);
        }
        if (text.Length != 7 || text[0] != '#')
        {
            return null;
        }
        for (int i = 1; i < 7; i++)
        {
            if (!Uri.IsHexDigit(text[i]))
            {
                return null;
            }
        }
        return text.ToUpperInvariant();
    }

    private static bool IsValidColor(string? hex)
    {
        return NormalizeColor(hex) != null;
    }

    private static Color Mix(Color a, Color b, double t)
    {
        return Color.FromRgb((byte)((double)(int)a.R + (double)(b.R - a.R) * t), (byte)((double)(int)a.G + (double)(b.G - a.G) * t), (byte)((double)(int)a.B + (double)(b.B - a.B) * t));
    }
}
