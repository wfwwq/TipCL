using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TipCL;
using Xunit;

namespace TipCL.Tests;

// R34 WP-L0：布局 v3 编辑态令牌守卫。
// GOAL-layout-v3 §6.4 第 1 条：画刷 / 矢量图标 / 样式由 WP-L0 提供，别的包只按键在运行时取（SetResourceReference /
// TryFindResource + 兜底），所以「键在不在、两套色板是否成对、数值是否对得上效果图」只能靠这里在编译期之外守住。
// 测试宿主是 net8（没有 WPF），这里直接把 XAML 当 XML 读。
public class LayoutV3ResourceTests
{
	private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

	private static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

	/// <summary>§6.4 第 1 条点名的画刷（两个 Palette 文件都必须有）。</summary>
	public static readonly string[] ContractBrushes =
	{
		"B.EditOutline", "B.EditOutlineHover", "B.EditOutlineOnDark", "B.EditDropFill", "B.EditColShade",
		"B.EditBarBg", "B.EditDim", "B.WarnSoft", "B.WarnInk"
	};

	/// <summary>本包另外提供、样式里用到的画刷与阴影（同样成对）。</summary>
	public static readonly string[] ExtraTokens =
	{
		"B.EditOutlineHoverOnDark", "B.EditDropFillOnDark", "B.EditColShadeOnDark", "B.EditBarLine", "B.EditItemBg",
		"B.EditSegRail", "B.EditSegOn", "B.EditGlass", "B.EditGlassLine", "B.EditGlassChip", "B.EditGlassChipLine",
		"B.OnEditGlass", "B.OnEditGlassMuted", "Fx.EditBar", "Fx.EditLabel", "Fx.Drawer", "Fx.SegOn"
	};

	/// <summary>只用在永远是深色背景（启动页英雄图）上的令牌：明暗两套取值必须相同。</summary>
	public static readonly string[] ThemeInvariantTokens =
	{
		"B.EditOutlineOnDark", "B.EditOutlineHoverOnDark", "B.EditDropFillOnDark", "B.EditColShadeOnDark", "B.EditDim",
		"B.EditGlass", "B.EditGlassLine", "B.EditGlassChip", "B.EditGlassChipLine", "B.OnEditGlass", "B.OnEditGlassMuted"
	};

	public static readonly string[] ContractGeometries = { "Geo.Eye", "Geo.EyeOff", "Geo.Lock", "Geo.Undo", "Geo.Redo", "Geo.Grip" };

	/// <summary>样式键 → TargetType 里应出现的类型名。前 5 个是 §6.4 点名的，其余是 §6.3 验收里的「深色玻璃版本」和辅助样式。</summary>
	public static TheoryData<string, string> ContractStyles()
	{
		return new TheoryData<string, string>
		{
			{ "Layout.EditBar", "Border" },
			{ "Layout.EditLabel", "Border" },
			{ "Layout.IconAct", "ButtonBase" },
			{ "Layout.Drawer", "Border" },
			{ "Layout.SegItem", "ToggleButton" },
			{ "Layout.EditOutline", "Rectangle" },
			{ "Layout.IconAct.Danger", "ButtonBase" },
			{ "Layout.EditBar.OnDark", "Border" },
			{ "Layout.EditLabel.OnDark", "Border" },
			{ "Layout.IconAct.OnDark", "ButtonBase" },
			{ "Layout.IconAct.Danger.OnDark", "ButtonBase" },
			{ "Layout.EditOutline.OnDark", "Rectangle" },
			{ "Layout.DropBox", "Rectangle" },
			{ "Layout.DropBox.OnDark", "Rectangle" },
			{ "Layout.ColShade", "Border" },
			{ "Layout.ColShade.OnDark", "Border" },
			{ "Layout.SegRail", "Border" },
			{ "Layout.Glyph", "Path" },
			{ "Layout.Grip", "Path" },
			{ "Layout.ResizeGrip", "Thumb" },
			{ "Layout.ResizeGrip.Bottom", "Thumb" },
			{ "Layout.EditTip", "Border" },
			{ "Layout.Count", "Border" },
			{ "Layout.DrawerItem", "Border" },
			{ "Layout.WarnNote", "Border" },
			{ "Layout.LockNote", "Border" }
		};
	}

	private static string RepoRoot()
	{
		DirectoryInfo? d = new DirectoryInfo(AppContext.BaseDirectory);
		while (d != null && !File.Exists(Path.Combine(d.FullName, "TipCL.sln")))
		{
			d = d.Parent;
		}
		Assert.NotNull(d);
		return d!.FullName;
	}

	private static XElement Load(string file)
	{
		return XDocument.Load(Path.Combine(RepoRoot(), "src", "TipCL", "Themes", file)).Root!;
	}

	private static Dictionary<string, XElement> Keyed(string file)
	{
		return Load(file).Elements()
			.Where(e => e.Attribute(X + "Key") != null)
			.ToDictionary(e => (string)e.Attribute(X + "Key")!, e => e);
	}

	private static XElement StyleOf(string key)
	{
		Assert.True(Keyed("Controls.xaml").TryGetValue(key, out XElement? s), "Controls.xaml 缺样式 " + key);
		Assert.Equal("Style", s!.Name.LocalName);
		return s;
	}

	// 样式自身（不含 BasedOn）上某个属性的 Setter 值
	private static string? SetterValue(XElement style, string property)
	{
		return style.Elements(P + "Setter")
			.Where(s => (string?)s.Attribute("Property") == property || ((string?)s.Attribute("Property"))?.EndsWith("." + property) == true)
			.Select(s => (string?)s.Attribute("Value"))
			.FirstOrDefault();
	}

	// 顺着 BasedOn 找最近定义了该属性的 Setter（模拟 WPF 的样式继承）
	private static string? EffectiveSetter(string key, string property)
	{
		Dictionary<string, XElement> all = Keyed("Controls.xaml");
		string? cur = key;
		while (cur != null && all.TryGetValue(cur, out XElement? s))
		{
			string? v = SetterValue(s, property);
			if (v != null)
			{
				return v;
			}
			Match m = Regex.Match((string?)s.Attribute("BasedOn") ?? "", @"\{StaticResource\s+([^\s}]+)\}");
			cur = m.Success ? m.Groups[1].Value : null;
		}
		return null;
	}

	[Fact]
	public void PalettesHaveIdenticalKeySetsAndKinds()
	{
		Dictionary<string, XElement> light = Keyed("Palette.Light.xaml");
		Dictionary<string, XElement> dark = Keyed("Palette.Dark.xaml");
		Assert.Empty(light.Keys.Except(dark.Keys));
		Assert.Empty(dark.Keys.Except(light.Keys));
		foreach (string k in light.Keys)
		{
			Assert.True(light[k].Name == dark[k].Name, k + " 在两套色板里类型不同：" + light[k].Name.LocalName + " / " + dark[k].Name.LocalName);
		}
	}

	[Fact]
	public void ContractBrushesExistInBothPalettes()
	{
		foreach (string file in new[] { "Palette.Light.xaml", "Palette.Dark.xaml" })
		{
			Dictionary<string, XElement> keys = Keyed(file);
			foreach (string k in ContractBrushes)
			{
				Assert.True(keys.TryGetValue(k, out XElement? e), file + " 缺 " + k);
				Assert.Equal("SolidColorBrush", e!.Name.LocalName);
				Assert.Matches("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", (string?)e.Attribute("Color") ?? "");
			}
		}
	}

	[Fact]
	public void ExtraTokensExistInBothPalettes()
	{
		foreach (string file in new[] { "Palette.Light.xaml", "Palette.Dark.xaml" })
		{
			Dictionary<string, XElement> keys = Keyed(file);
			foreach (string k in ExtraTokens)
			{
				Assert.True(keys.TryGetValue(k, out XElement? e), file + " 缺 " + k);
				Assert.Equal(k.StartsWith("Fx.") ? "DropShadowEffect" : "SolidColorBrush", e!.Name.LocalName);
			}
		}
	}

	[Fact]
	public void OnDarkTokensAreThemeInvariant()
	{
		Dictionary<string, XElement> light = Keyed("Palette.Light.xaml");
		Dictionary<string, XElement> dark = Keyed("Palette.Dark.xaml");
		foreach (string k in ThemeInvariantTokens)
		{
			Assert.Equal(((string?)light[k].Attribute("Color"))?.ToUpperInvariant(), ((string?)dark[k].Attribute("Color"))?.ToUpperInvariant());
		}
	}

	[Fact]
	public void LightValuesMatchMockup()
	{
		// layout3.html：--page-line2 = rgba(0,0,0,.16)，悬停 rgba(0,0,0,.34)，列底纹 rgba(0,0,0,.035)，琥珀 #b45309 / #fdf3e2
		Dictionary<string, XElement> light = Keyed("Palette.Light.xaml");
		string C(string k) => ((string)light[k].Attribute("Color")!).ToUpperInvariant();
		Assert.Equal("#29000000", C("B.EditOutline"));
		Assert.Equal("#57000000", C("B.EditOutlineHover"));
		Assert.Equal("#09000000", C("B.EditColShade"));
		Assert.Equal("#FFFFFF", C("B.EditBarBg"));
		Assert.Equal("#B45309", C("B.WarnInk"));
		Assert.Equal("#FDF3E2", C("B.WarnSoft"));
		// pages3.html 深色启动页：虚线 rgba(255,255,255,.35)，悬停 .7
		Assert.Equal("#59FFFFFF", C("B.EditOutlineOnDark"));
		Assert.Equal("#B3FFFFFF", C("B.EditOutlineHoverOnDark"));
	}

	[Fact]
	public void ContractGeometriesAreStrokePathsIn16Box()
	{
		Dictionary<string, XElement> keys = Keyed("Controls.xaml");
		foreach (string k in ContractGeometries)
		{
			Assert.True(keys.TryGetValue(k, out XElement? g), "Controls.xaml 缺 " + k);
			Assert.Equal("Geometry", g!.Name.LocalName);
			string data = g.Value.Trim();
			Assert.StartsWith("M", data);
			// 只用绝对坐标的 M / L / C / A / Z（WPF 路径迷你语言里最稳的子集）
			Assert.Matches(@"^[MLCAZ0-9.,\s]+$", data);
			foreach (Match n in Regex.Matches(data, @"\d+(\.\d+)?"))
			{
				double v = double.Parse(n.Value, CultureInfo.InvariantCulture);
				Assert.InRange(v, 0.0, 16.0);
			}
		}
	}

	[Theory]
	[MemberData(nameof(ContractStyles))]
	public void ContractStylesExistWithTargetType(string key, string targetType)
	{
		XElement s = StyleOf(key);
		Assert.Contains("x:Type " + targetType + "}", (string?)s.Attribute("TargetType") ?? "");
	}

	[Fact]
	public void StyleNumbersMatchMockup()
	{
		Assert.Equal("46", EffectiveSetter("Layout.EditBar", "Height"));
		Assert.Equal("12", EffectiveSetter("Layout.EditBar", "CornerRadius"));
		Assert.Equal("24", EffectiveSetter("Layout.EditLabel", "Height"));
		Assert.Equal("8", EffectiveSetter("Layout.EditLabel", "CornerRadius"));
		Assert.Equal("26", EffectiveSetter("Layout.IconAct", "Width"));
		Assert.Equal("24", EffectiveSetter("Layout.IconAct", "Height"));
		Assert.Equal("26", EffectiveSetter("Layout.IconAct.Danger.OnDark", "Width"));
		Assert.Equal("316", EffectiveSetter("Layout.Drawer", "Width"));
		Assert.Equal("24", EffectiveSetter("Layout.SegItem", "Height"));
		Assert.Equal("1.5", EffectiveSetter("Layout.EditOutline", "StrokeThickness"));
		Assert.Equal("-4", EffectiveSetter("Layout.EditOutline", "Margin"));
		Assert.Equal("-4", EffectiveSetter("Layout.EditOutline.OnDark", "Margin"));
		Assert.NotNull(EffectiveSetter("Layout.EditOutline", "StrokeDashArray"));
	}

	[Fact]
	public void SelectedOutlineIsSolidAccent()
	{
		XElement s = StyleOf("Layout.EditOutline");
		XElement sel = s.Descendants(P + "Trigger").Single(t => (string?)t.Attribute("Value") == "selected");
		Dictionary<string, string?> set = sel.Elements(P + "Setter").ToDictionary(e => (string)e.Attribute("Property")!, e => (string?)e.Attribute("Value"));
		Assert.Equal("{DynamicResource B.Accent}", set["Stroke"]);
		Assert.Equal("2", set["StrokeThickness"]);
		Assert.Equal("{x:Null}", set["StrokeDashArray"]);
	}

	[Fact]
	public void DangerHoverIsCaptionRed()
	{
		foreach (string key in new[] { "Layout.IconAct.Danger", "Layout.IconAct.Danger.OnDark" })
		{
			XElement hover = StyleOf(key).Descendants(P + "Trigger").Single(t => ((string?)t.Attribute("Property"))?.EndsWith("IsMouseOver") == true);
			Assert.Contains(hover.Elements(P + "Setter"), e => (string?)e.Attribute("Property") == "Background" && (string?)e.Attribute("Value") == "{DynamicResource B.CaptionHot}");
		}
		foreach (string file in new[] { "Palette.Light.xaml", "Palette.Dark.xaml" })
		{
			Assert.Equal("#C42B1C", ((string)Keyed(file)["B.CaptionHot"].Attribute("Color")!).ToUpperInvariant());
		}
	}

	[Fact]
	public void LayoutStylesUseOnlyRadii8And12()
	{
		Regex radiusProp = new Regex(@"(^|\.)(CornerRadius|RadiusX|RadiusY)$");
		foreach (XElement s in Keyed("Controls.xaml").Where(kv => kv.Key.StartsWith("Layout.")).Select(kv => kv.Value))
		{
			IEnumerable<string> values = s.Descendants(P + "Setter")
				.Where(e => radiusProp.IsMatch((string?)e.Attribute("Property") ?? ""))
				.Select(e => (string)e.Attribute("Value")!)
				.Concat(s.Descendants().SelectMany(e => e.Attributes()).Where(a => radiusProp.IsMatch(a.Name.LocalName)).Select(a => a.Value));
			foreach (string v in values)
			{
				foreach (string part in v.Split(','))
				{
					Assert.True(part.Trim() == "8" || part.Trim() == "12", (string)s.Attribute(X + "Key")! + " 用了圆角 " + v);
				}
			}
		}
	}

	[Fact]
	public void LayoutStyleResourceRefsResolve()
	{
		HashSet<string> known = new HashSet<string>(Keyed("Controls.xaml").Keys);
		HashSet<string> light = new HashSet<string>(Keyed("Palette.Light.xaml").Keys);
		HashSet<string> dark = new HashSet<string>(Keyed("Palette.Dark.xaml").Keys);
		Regex refRx = new Regex(@"\{(Dynamic|Static)Resource\s+([^\s}]+)\}");
		foreach (KeyValuePair<string, XElement> kv in Keyed("Controls.xaml").Where(kv => kv.Key.StartsWith("Layout.") || kv.Key.StartsWith("Geo.")))
		{
			foreach (Match m in refRx.Matches(kv.Value.ToString()))
			{
				string key = m.Groups[2].Value;
				if (key.StartsWith("{"))
				{
					continue;
				}
				Assert.True(known.Contains(key) || (light.Contains(key) && dark.Contains(key)), kv.Key + " 引用了不存在（或没有成对）的键 " + key);
			}
		}
	}
}

// DevShot 布局 v3 开关的参数解析（不运行 TipCL.exe，只测纯函数）
public class DevShotArgsTests
{
	[Fact]
	public void EditOnDownload()
	{
		DevShotArgs a = DevShotArgs.Parse(new[] { "--shot", "download", "--edit", "--out", "x.png" });
		Assert.Equal("download", a.ShotId);
		Assert.Equal("download", a.NavId);
		Assert.True(a.Edit);
		Assert.Null(a.Select);
		Assert.False(a.LayoutDrawer);
		Assert.Equal(-1, a.SettingsView);
		Assert.Equal(2, a.MatchScore("download"));
		Assert.Equal(0, a.MatchScore("launch"));
	}

	[Fact]
	public void SelectAndDrawerImplyEdit()
	{
		DevShotArgs s = DevShotArgs.Parse(new[] { "--shot", "download", "--select", "list" });
		Assert.True(s.Edit);
		Assert.Equal("list", s.Select);
		DevShotArgs d = DevShotArgs.Parse(new[] { "--shot", "download", "--pop", "layout-drawer" });
		Assert.True(d.Edit);
		Assert.True(d.LayoutDrawer);
	}

	[Fact]
	public void LaunchPopupsAreNotLayoutDrawer()
	{
		DevShotArgs a = DevShotArgs.Parse(new[] { "--shot", "launch", "--pop", "config" });
		Assert.Equal("config", a.Pop);
		Assert.False(a.LayoutDrawer);
		Assert.False(a.Edit);
	}

	[Fact]
	public void SettingsLayoutExpandAdvanced()
	{
		DevShotArgs a = DevShotArgs.Parse(new[] { "--shot", "settings:layout", "--expand", "advanced", "--size", "1000x3100" });
		Assert.Equal("settings", a.NavId);
		Assert.Equal(7, a.SettingsView);
		Assert.Equal(DevShotArgs.ExpandAdvanced, a.Expand);
		Assert.Null(a.HostId);
		// 布局子页没有区块宿主：任何宿主都不算匹配（不会误把别的宿主拉进编辑态）
		Assert.Equal(0, a.MatchScore("settings:general"));
	}

	[Theory]
	[InlineData("java", 2, "java")]
	[InlineData("servers", 4, "servers")]
	[InlineData("playtime", 5, "playtime")]
	[InlineData("feedback", 6, "feedback")]
	[InlineData("settings:general", 0, "settings:general")]
	[InlineData("settings:network", 3, "settings:network")]
	[InlineData("settings:about", 6, "settings:about")]
	[InlineData("settings:account", 1, "account")]
	public void SettingsSubPagesMapToView(string shot, int view, string host)
	{
		DevShotArgs a = DevShotArgs.Parse(new[] { "--shot", shot, "--edit" });
		Assert.Equal("settings", a.NavId);
		Assert.Equal(view, a.SettingsView);
		Assert.Equal(host, a.HostId);
		Assert.Equal(2, a.MatchScore(host));
		Assert.Equal(0, a.MatchScore("download"));
	}

	[Fact]
	public void TopLevelPagesStayTopLevel()
	{
		foreach (string id in new[] { "account", "multiplayer", "launch", "download", "settings" })
		{
			DevShotArgs a = DevShotArgs.Parse(new[] { "--shot", id });
			Assert.Equal(id, a.NavId);
			Assert.Equal(-1, a.SettingsView);
		}
	}

	[Fact]
	public void LibraryMatchesHostPrefix()
	{
		DevShotArgs a = DevShotArgs.Parse(new[] { "--shot", "library", "--edit" });
		Assert.Equal("library", a.NavId);
		Assert.Equal(1, a.MatchScore("library:main"));
		Assert.Equal(0, a.MatchScore("librarything"));
		DevShotArgs b = DevShotArgs.Parse(new[] { "--shot", "library:main" });
		Assert.Equal("library", b.NavId);
		Assert.Equal(2, b.MatchScore("library:main"));
	}

	[Fact]
	public void DesignSheetAlone()
	{
		Assert.Equal("design-sheet", DevShotArgs.Parse(new[] { "--design-sheet" }).ShotId);
		Assert.Equal("design-sheet", DevShotArgs.Parse(new[] { "--design-sheet", "--out", "a.png" }).ShotId);
		Assert.Equal("design-sheet", DevShotArgs.Parse(new[] { "--shot", "design-sheet" }).ShotId);
	}

	[Fact]
	public void MissingValuesAreNull()
	{
		DevShotArgs a = DevShotArgs.Parse(new[] { "--shot", "download", "--select", "--edit" });
		Assert.Null(a.Select);
		Assert.True(a.Edit);
		Assert.Equal("launch", DevShotArgs.Parse(System.Array.Empty<string>()).ShotId);
		Assert.Equal("launch", DevShotArgs.Parse(null).NavId);
	}
}
