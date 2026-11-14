using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TipCL.Pages;
using TipCL.Pages.Layout;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// L7a 外壳回归闸门:顶栏页签的计划(顺序 / 显隐 / 高亮映射)与标题栏元素开关。
///   默认等价  → DefaultTabsAreTheFiveBuiltinsInCurrentOrder / HighlightMappingIsUnchanged
///   数据驱动  → OrderedKeysFollowsShellTabsOrderAndVisibility / SetTabs* / SetTabVisible*
///   标题栏    → SetTitlebarOnlyTouchesGivenFlags
///   文案走 i18n → LabelsComeFromPairedLocaleTables
///   文案搬出 XAML → TabLabelsLiveInShellTabsOnly
/// 只测纯逻辑与落盘契约:可视元素的重建在 MainWindow.RebuildShellTabs 里,net8 宿主跑不了 WPF,
/// 那部分靠 --shot 人工对照 + --smoke。落盘一律走 DirOverride 临时目录,绝不碰用户真实数据目录。
/// </summary>
public class LayoutShellTests
{
	private static readonly string[] Builtins = new string[5] { "launch", "library", "download", "plugins", "settings" };

	/// <summary>五个页签的中文原文(= 词表 key;只出现在测试里,src 侧只允许出现在 ShellTabs.Label)。</summary>
	private static readonly Dictionary<string, string> Zh = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["launch"] = "启动", 
		["library"] = "实例库", 
		["download"] = "下载", 
		["plugins"] = "插件", 
		["settings"] = "设置", 
	};

	private readonly string _tmp = Path.Combine(Path.GetTempPath(), "tipcl_shell_tests_" + Guid.NewGuid().ToString("N"));

	// 两个存储必须指到同一目录:否则 v2 的写通会落到用户真实数据目录
	private void UseDir()
	{
		LayoutStoreV2.DirOverride = _tmp;
		PageLayoutStore.DirOverride = _tmp;
		LayoutStoreV2.ResetCache();
	}

	private void Release()
	{
		LayoutStoreV2.DirOverride = null;
		PageLayoutStore.DirOverride = null;
		LayoutStoreV2.ResetCache();
		try
		{
			Directory.Delete(_tmp, true);
		}
		catch (Exception)
		{
		}
	}

	// ————— 默认 = 现状五项,顺序一致 —————

	[Fact]
	public void DefaultTabsAreTheFiveBuiltinsInCurrentOrder()
	{
		Assert.Equal(Builtins, ShellTabs.BuiltinKeys);
		// 契约默认值只有一个源头,ShellTabs 与 LayoutSchema 不许漂移
		Assert.Equal(LayoutSchema.DefaultTabs, ShellTabs.BuiltinKeys);

		Assert.Equal(Builtins, ShellTabs.OrderedKeys(new ShellLayout()).ToArray());
		// 空 / null 一律回落默认:界面上永远不会出现"零页签"
		Assert.Equal(Builtins, ShellTabs.OrderedKeys(new ShellLayout { Tabs = new List<string>() }).ToArray());
		Assert.Equal(Builtins, ShellTabs.OrderedKeys(null).ToArray());
		Assert.Equal(new string[0], ShellTabs.HiddenKeys(new ShellLayout()).ToArray());
	}

	// ————— 顺序 + 显隐 + 白名单 + 去重 —————

	[Fact]
	public void OrderedKeysFollowsShellTabsOrderAndVisibility()
	{
		ShellLayout shell = new ShellLayout { Tabs = new List<string> { "plugins", "launch", "settings" } };
		Assert.Equal(new[] { "plugins", "launch", "settings" }, ShellTabs.OrderedKeys(shell).ToArray());
		Assert.True(ShellTabs.IsVisible(shell, "plugins"));
		Assert.False(ShellTabs.IsVisible(shell, "library"));
		Assert.False(ShellTabs.IsVisible(shell, "download"));
		Assert.False(ShellTabs.IsVisible(shell, ""));
		Assert.Equal(new[] { "library", "download" }, ShellTabs.HiddenKeys(shell).ToArray());

		// 未知 key 丢弃、重复去重;HiddenKeys 按默认顺序而不是输入顺序
		shell.Tabs = new List<string> { "launch", "nope", "launch", "settings", "tasks" };
		Assert.Equal(new[] { "launch", "settings" }, ShellTabs.OrderedKeys(shell).ToArray());
		Assert.Equal(new[] { "library", "download", "plugins" }, ShellTabs.HiddenKeys(shell).ToArray());
	}

	// ————— 高亮映射:改动前 SyncTabs 五行的逐项等价 —————

	[Theory]
	[InlineData("launch", "launch")]
	[InlineData("library", "library")]
	[InlineData("download", "download")]
	[InlineData("tasks", "download")]
	[InlineData("plugins", "plugins")]
	[InlineData("settings", "settings")]
	[InlineData("account", "settings")]
	[InlineData("ai", null)]
	[InlineData("instance", null)]
	[InlineData("multiplayer", null)]
	[InlineData("design-sheet", null)]
	[InlineData("plugin:hello:home", null)]
	[InlineData("", null)]
	public void HighlightMappingIsUnchanged(string pageId, string? expected)
	{
		Assert.Equal(expected, ShellTabs.HighlightKeyOf(pageId));
	}

	// ————— 页签的去向:与改动前五处 Click 的 Navigate 目标一致 —————

	[Fact]
	public void BuiltinTabsNavigateToTheirOwnPageIds()
	{
		foreach (string key in Builtins)
		{
			Assert.Equal(key, ShellTabs.PageIdOf(key));
		}
		Assert.Null(ShellTabs.PageIdOf("tasks"));
		Assert.Null(ShellTabs.PageIdOf("nope"));
	}

	// ————— 文案:走 I18n.L,且 en / zh_CN 词条成对 —————

	[Theory]
	[InlineData("launch")]
	[InlineData("library")]
	[InlineData("download")]
	[InlineData("plugins")]
	[InlineData("settings")]
	public void LabelsComeFromPairedLocaleTables(string key)
	{
		string zhKey = Zh[key];
		string zhValue = LocaleValue("zh_CN.json", zhKey);
		string enValue = LocaleValue("en.json", zhKey);
		Assert.False(string.IsNullOrWhiteSpace(zhValue));
		Assert.False(string.IsNullOrWhiteSpace(enValue));
		// zh_CN 的既有约定是 key 自映射,这里钉住它,顺便挡住"改 key 忘了改这里"
		Assert.Equal(zhKey, zhValue);

		// 页签文案只能是词表里的取值之一:既证明走了 I18n.L,也挡住"翻译键写错"
		string label = ShellTabs.Label(key);
		Assert.Contains(label, new string[2] { zhValue, enValue });
	}

	[Fact]
	public void UnknownLabelFallsBackToItsKey()
	{
		Assert.Equal("custom-thing", ShellTabs.Label("custom-thing"));
	}

	// ————— 文案已从 XAML 搬走:骨架仍在,中文字面量只允许在 ShellTabs —————

	[Fact]
	public void TabLabelsLiveInShellTabsOnly()
	{
		string xaml = Src("src/TipCL/MainWindow.xaml");
		int panel = xaml.IndexOf("TopTabPanel", StringComparison.Ordinal);
		Assert.True(panel > 0, "MainWindow.xaml 里找不到 TopTabPanel");
		int end = xaml.IndexOf("</StackPanel>", panel, StringComparison.Ordinal);
		Assert.True(end > panel);
		string block = xaml.Substring(panel, end - panel);

		// 五个骨架必须在(重建时要按 key 找元素)
		foreach (string name in new string[5] { "TabLaunch", "TabLibrary", "TabTasks", "TabPlugins", "TabSettings" })
		{
			Assert.Contains("Name=\"" + name + "\"", block);
		}
		// 页签文案不许再写死在 XAML 里
		foreach (string zh in Zh.Values)
		{
			Assert.DoesNotContain("Content=\"" + zh + "\"", block);
		}

		// 中文字面量只允许出现在 ShellTabs.Label 里,且必须被 I18n.L 包裹
		string shellSrc = Src("src/TipCL/Pages/Layout/ShellTabs.cs");
		foreach (string zh in Zh.Values)
		{
			Assert.Contains("I18n.L(\"" + zh + "\")", shellSrc);
		}
	}

	// ————— 落盘:唯一入口是 LayoutStoreV2,v1 投影不许带上 v2 字段 —————

	[Fact]
	public void SetTabsWritesThroughLayoutStoreV2AndKeepsV1Frozen()
	{
		UseDir();
		try
		{
			ShellLayout shell = ShellTabs.SetTabs(new[] { "download", "launch" });
			Assert.Equal(new[] { "download", "launch" }, shell.Tabs.ToArray());

			string v2Path = Path.Combine(_tmp, "ui_layout_v2.json");
			Assert.True(File.Exists(v2Path));
			string v2 = File.ReadAllText(v2Path);
			Assert.Contains("\"tabs\"", v2);
			Assert.Contains("\"download\"", v2);

			// v2 是唯一真源:清缓存重新装载后还是这套顺序
			LayoutStoreV2.ResetCache();
			Assert.Equal(new[] { "download", "launch" }, ShellTabs.CurrentShell().Tabs.ToArray());
			Assert.Equal("v2", LayoutStoreV2.LastLoadSource);

			// v1 投影语义冻结:只有 version / kind / pages,一个 v2 字段都不许出现
			string v1Path = Path.Combine(_tmp, "ui_page_layouts.json");
			Assert.True(File.Exists(v1Path));
			string v1 = File.ReadAllText(v1Path);
			foreach (string forbidden in new string[5] { "shell", "tabs", "titlebar", "nav_mode", "density" })
			{
				Assert.DoesNotContain("\"" + forbidden + "\"", v1);
			}
			using JsonDocument json = JsonDocument.Parse(v1);
			Assert.Equal(1, json.RootElement.GetProperty("version").GetInt32());
			Assert.Equal("tipcl-page-layout", json.RootElement.GetProperty("kind").GetString());
			Assert.Equal(new[] { "version", "kind", "pages" }, json.RootElement.EnumerateObject().Select((JsonProperty p) => p.Name).ToArray());
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void SetTabsDropsUnknownKeysAndEmptyFallsBackToDefault()
	{
		UseDir();
		try
		{
			// 未知 key 丢弃、重复去重
			Assert.Equal(new[] { "launch", "plugins" }, ShellTabs.SetTabs(new[] { "launch", "nope", "plugins", "launch" }).Tabs.ToArray());
			// 空 / 全非法 = 回落默认(Sanitize 的职责,ShellTabs 不另外造一套规则)
			Assert.Equal(Builtins, ShellTabs.SetTabs(Array.Empty<string>()).Tabs.ToArray());
			Assert.Equal(Builtins, ShellTabs.SetTabs(new[] { "nope" }).Tabs.ToArray());
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void SetTabVisibleTogglesOneTabAndKeepsOrder()
	{
		UseDir();
		try
		{
			ShellLayout shell = ShellTabs.SetTabVisible("plugins", false);
			Assert.Equal(new[] { "launch", "library", "download", "settings" }, shell.Tabs.ToArray());

			shell = ShellTabs.SetTabVisible("plugins", true);
			Assert.Equal(new[] { "launch", "library", "download", "settings", "plugins" }, shell.Tabs.ToArray());
			// 再显示一次不产生重复项
			shell = ShellTabs.SetTabVisible("plugins", true);
			Assert.Equal(5, shell.Tabs.Count);

			// 全部隐藏 → 回落默认:界面上永远有入口
			foreach (string key in Builtins)
			{
				shell = ShellTabs.SetTabVisible(key, false);
			}
			Assert.Equal(Builtins, shell.Tabs.ToArray());
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void SetTitlebarOnlyTouchesGivenFlags()
	{
		UseDir();
		try
		{
			ShellLayout fresh = new ShellLayout();
			Assert.True(ShellTabs.ShowBridge(fresh));
			Assert.True(ShellTabs.ShowAi(fresh));
			Assert.True(ShellTabs.ShowTheme(fresh));
			Assert.True(ShellTabs.ShowAccount(fresh));
			Assert.True(ShellTabs.AnyTitlebarVisible(fresh));
			// 读不到 shell 时按"全开"处理(View 不能因为读布局失败而把标题栏吞掉)
			Assert.True(ShellTabs.AnyTitlebarVisible(null));

			ShellLayout shell = ShellTabs.SetTitlebar(ai: false, account: false);
			Assert.True(ShellTabs.ShowBridge(shell));
			Assert.False(ShellTabs.ShowAi(shell));
			Assert.True(ShellTabs.ShowTheme(shell));
			Assert.False(ShellTabs.ShowAccount(shell));
			Assert.True(ShellTabs.AnyTitlebarVisible(shell));

			// 读回:新会话装载后仍是这套开关
			LayoutStoreV2.ResetCache();
			ShellLayout again = ShellTabs.CurrentShell();
			Assert.True(again.Titlebar.Bridge);
			Assert.False(again.Titlebar.Ai);
			Assert.True(again.Titlebar.Theme);
			Assert.False(again.Titlebar.Account);

			// null = 该项不动
			shell = ShellTabs.SetTitlebar(bridge: true);
			Assert.True(ShellTabs.ShowBridge(shell));
			Assert.False(ShellTabs.ShowAi(shell));

			// 四项全关 → 分隔线也该收起(宿主据此决定)
			shell = ShellTabs.SetTitlebar(bridge: false, ai: false, theme: false, account: false);
			Assert.False(ShellTabs.AnyTitlebarVisible(shell));
		}
		finally
		{
			Release();
		}
	}

	// ————— 小工具 —————

	/// <summary>源码树根(向上找 TipCL.sln),与 StaticInitOrderTests 的做法一致。</summary>
	private static string Root()
	{
		DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TipCL.sln")))
		{
			dir = dir.Parent;
		}
		Assert.NotNull(dir);
		return dir!.FullName;
	}

	private static string Src(string rel)
	{
		return File.ReadAllText(Path.Combine(Root(), rel.Replace('/', Path.DirectorySeparatorChar)));
	}

	/// <summary>读词表里某个 key 的取值(直接读源码树的 json,不依赖 I18n 的静态表:测试宿主的 TIPCL_HOME 是临时目录)。</summary>
	private static string LocaleValue(string file, string key)
	{
		using JsonDocument json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "src", "TipCL", "Resources", "locales", file)));
		return (json.RootElement.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String) ? (value.GetString() ?? "") : "";
	}
}
