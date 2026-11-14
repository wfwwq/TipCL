using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

// 主题包管理 + 视觉应用 + 颜色规范化（WP10 拆分）
// 从 SettingsPage.cs 机械提取（partial class，行为不变；见 DECISIONS D-24）。
public sealed partial class SettingsPage
{
	private async Task ReloadThemesAsync()
	{
		List<ThemeRow> list = (await PageBase.Api.TryCallAsync("list_themes", null, new List<ThemeRow>())) ?? new List<ThemeRow>();
		string keep = _themePack.Str();
		_themePack.Fill(list.Select((ThemeRow t) => t.Name), keep);
		_themeInfo.Text = ((list.Count == 0) ? I18n.L("还没有主题包。调好配色后点「保存为主题包」。") : (I18n.L("共 {0} 个：", list.Count) + string.Join(I18n.L("、"), from t in list.Take(6)
			select I18n.L("{0}（{1} {2}）", t.Name, t.UiDark ? I18n.L("深色") : I18n.L("浅色"), t.ThemeColor))));
	}

	private async Task SaveThemeAsync()
	{
		string name = await Dlg.Prompt(I18n.L("保存主题包"), I18n.L("主题包名称"), I18n.L("我的主题"));
		if (!string.IsNullOrWhiteSpace(name))
		{
			await PageBase.Api.CallAsync<object>("save_settings", new
			{
				data = new Dictionary<string, object>
				{
					["theme_color"] = _color,
					["ui_dark"] = App.IsDark,
					["ui_bg_preset"] = KeyOf(_bg)
				}
			});
			Extras.ThemeColor = _color;
			Extras.Background = KeyOf(_bg);
			SaveExtras();
			await PageBase.Api.CallAsync<object>("save_theme", new
			{
				name = name.Trim()
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
		await PageBase.Api.CallAsync<object>("load_theme", new { name });
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
			flag = await Dlg.Confirm(I18n.L("删除主题包"), I18n.L("删除「{0}」？", name), I18n.L("删除"), I18n.L("取消"), danger: true);
		}
		if (flag)
		{
			await PageBase.Api.CallAsync<object>("delete_theme", new { name });
			PageBase.Toast(I18n.L("已删除"), name, ToastKind.Success);
			await ReloadThemesAsync();
		}
	}

	private async Task ImportThemeAsync()
	{
		string path = Dlg.PickFile(I18n.L("主题包 (*.json)|*.json|全部文件|*.*"), I18n.L("导入主题包"));
		if (path != null)
		{
			string text = await PageBase.Api.CallAsync<string>("import_theme", new { path });
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
		string dest = Dlg.SaveFile(I18n.L("主题包 (*.json)|*.json"), text + ".json", I18n.L("导出主题包"));
		if (dest != null)
		{
			string text2 = await PageBase.Api.CallAsync<string>("export_theme", new
			{
				name = text,
				dest = dest
			});
			PageBase.Toast(I18n.L("已导出"), text2 ?? dest, ToastKind.Success);
		}
	}

	internal static void ApplyVisuals()
	{
		ApplyVisuals(_visualColor, _visualBackground);
	}

	private static void ApplyVisuals(string color, string bg)
	{
		color = NormalizeColor(color) ?? "#2E9B6B";
		bg = NormalizeBackground(bg);
		_visualColor = color;
		_visualBackground = bg;
		try
		{
			ResourceDictionary paletteDictionary = App.PaletteDictionary;
			if (paletteDictionary == null)
			{
				return;
			}
			bool isDark = App.IsDark;
			ResourceDictionary resourceDictionary = new ResourceDictionary
			{
				Source = App.PaletteUri(isDark)
			};
			Color paper = ((resourceDictionary["C.Paper"] is Color color2) ? color2 : (isDark ? Color.FromRgb(30, 30, 30) : Colors.White));
			if (string.Equals(color, "#2E9B6B", StringComparison.OrdinalIgnoreCase))
			{
				string[] accentKeys = AccentKeys;
				foreach (string key in accentKeys)
				{
					if (resourceDictionary.Contains(key))
					{
						paletteDictionary[key] = resourceDictionary[key];
					}
				}
			}
			else
			{
				ApplyAccent(paletteDictionary, Opaque(Parse(color)), paper, isDark);
			}
			(string, string[]) tuple;
			switch (bg)
			{
			case "default":
			{
				string[] accentKeys = BackgroundKeys;
				foreach (string key2 in accentKeys)
				{
					if (resourceDictionary.Contains(key2))
					{
						paletteDictionary[key2] = resourceDictionary[key2];
					}
				}
				goto end_IL_0025;
			}
			case "eye":
				tuple = (isDark ? ("#121A15", new string[3] { "#141D17", "#16211A", "#121A15" }) : ("#EBF4EC", new string[3] { "#EFF7F0", "#EAF4EB", "#EDF6EE" }));
				break;
			case "warm":
				tuple = (isDark ? ("#1B1813", new string[3] { "#1D1A15", "#201C16", "#1B1813" }) : ("#F7F3EA", new string[3] { "#F9F5ED", "#F6F2E8", "#F8F4EC" }));
				break;
			default:
				tuple = (isDark ? ("#14181D", new string[3] { "#161B21", "#181E25", "#14181D" }) : ("#EDF1F6", new string[3] { "#F0F4F9", "#EBF0F6", "#EEF2F8" }));
				break;
			}
			(string, string[]) tuple2 = tuple;
			string item = tuple2.Item1;
			string[] item2 = tuple2.Item2;
			Color color3 = Parse(item);
			paletteDictionary["C.Canvas"] = color3;
			paletteDictionary["B.Canvas"] = new SolidColorBrush(color3);
			LinearGradientBrush linearGradientBrush = new LinearGradientBrush
			{
				StartPoint = new Point(0.0, 0.0),
				EndPoint = new Point(0.9, 1.0)
			};
			linearGradientBrush.GradientStops.Add(new GradientStop(Parse(item2[0]), 0.0));
			linearGradientBrush.GradientStops.Add(new GradientStop(Parse(item2[1]), 0.55));
			linearGradientBrush.GradientStops.Add(new GradientStop(Parse(item2[2]), 1.0));
			paletteDictionary["B.PageWash"] = linearGradientBrush;
			end_IL_0025:;
		}
		catch (Exception)
		{
		}
		App.RaiseThemeVisualsChanged();
	}

	private static void ApplyAccent(ResourceDictionary d, Color accent, Color paper, bool dark)
	{
		Color color = EnsureWhiteSurface(accent, 4.8);
		Color color2 = EnsureTextContrast(accent, paper, dark, 4.8);
		Color color3 = EnsureTextContrast(accent, paper, dark, 1.6);
		Color color4 = Mix(color3, Colors.White, dark ? 0.28 : 0.3);
		Color color5 = Mix(paper, color3, dark ? 0.16 : 0.105);
		Color color6 = Mix(paper, color3, dark ? 0.24 : 0.165);
		d["C.Accent"] = color3;
		d["C.AccentStrong"] = color;
		d["C.AccentText"] = color2;
		d["C.AccentDeep"] = color2;
		d["C.AccentLite"] = color4;
		d["B.Accent"] = new SolidColorBrush(color3);
		d["B.AccentStrong"] = new SolidColorBrush(color);
		d["B.OnAccentStrong"] = Brushes.White;
		d["B.AccentText"] = new SolidColorBrush(color2);
		d["B.AccentTint"] = new SolidColorBrush(Mix(paper, color3, dark ? 0.1 : 0.07));
		d["B.AccentDeep"] = new SolidColorBrush(color2);
		d["B.AccentLite"] = new SolidColorBrush(color4);
		d["B.AccentSoft"] = new SolidColorBrush(color5);
		d["B.AccentSoft2"] = new SolidColorBrush(color6);
		d["B.OnAccent"] = Brushes.White;
		Color color7 = Mix(paper, color3, dark ? 0.1 : 0.07);
		Color color8 = Mix(paper, color3, dark ? 0.07 : 0.045);
		Color color9 = Mix(paper, color3, dark ? 0.26 : 0.2);
		d["B.Glass"] = new SolidColorBrush(color7)
		{
			Opacity = (dark ? 0.72 : 0.76)
		};
		d["B.GlassStrong"] = new SolidColorBrush(color8)
		{
			Opacity = (dark ? 0.86 : 0.88)
		};
		d["B.GlassBorder"] = new SolidColorBrush(color9)
		{
			Opacity = (dark ? 0.62 : 0.68)
		};
		Color color10 = (dark ? Color.FromRgb(63, 176, 124) : Color.FromRgb(46, 155, 107));
		d["B.Ok"] = new SolidColorBrush(color10);
		d["B.OnOk"] = new SolidColorBrush(ContrastForeground(color10));
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush
		{
			StartPoint = new Point(0.0, 0.0),
			EndPoint = new Point(1.0, 1.0)
		};
		linearGradientBrush.GradientStops.Add(new GradientStop(Mix(color, Colors.White, 0.06), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(color, 0.52));
		linearGradientBrush.GradientStops.Add(new GradientStop(Mix(color, Colors.Black, 0.18), 1.0));
		d["B.BannerFill"] = linearGradientBrush;
		d["B.OnBanner"] = Brushes.White;
		LinearGradientBrush linearGradientBrush2 = new LinearGradientBrush
		{
			StartPoint = new Point(0.0, 0.0),
			EndPoint = new Point(1.0, 1.0)
		};
		linearGradientBrush2.GradientStops.Add(new GradientStop(Mix(color, Colors.White, 0.08), 0.0));
		linearGradientBrush2.GradientStops.Add(new GradientStop(Mix(color, Colors.Black, 0.16), 1.0));
		d["B.LogoFill"] = linearGradientBrush2;
		d["B.OnLogo"] = Brushes.White;
	}

	private static Color Parse(string hex)
	{
		return (Color)(ColorConverter.ConvertFromString(hex) ?? ((object)Colors.SeaGreen));
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
		Color color2 = (dark ? Colors.White : Colors.Black);
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

