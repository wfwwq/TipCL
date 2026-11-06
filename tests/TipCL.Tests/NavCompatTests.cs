using System;
using System.Linq;
using TipCL;
using TipCL.Services;
using Xunit;

// GOAL WP4 验收：旧 NavConfig 配置文件加载不报错（7 组新分组不破坏旧存档）。
// 走 NavConfig.FromJsonString：net8 测试宿主与 net48 的 STJ 程序集版本不同，
// 直接传 JsonElement 会触发 CS1705，故不在测试里引入 STJ 类型。
public class NavCompatTests
{
	[Fact]
	public void OldGroupedConfigWithOldThreeGroupsLoadsAndRenders()
	{
		// 旧版默认分组（账户/游戏/通用）存档
		var cfg = NavConfig.FromJsonString("""
		{
			"ui_nav_style": "grouped",
			"ui_nav_order": ["launch", "download", "instance", "ai", "more", "settings", "tasks"],
			"ui_nav_pinned": ["instance", "settings"],
			"ui_nav_hidden": [],
			"ui_nav_groups": [
				{ "title": "账户", "keys": ["account"] },
				{ "title": "游戏", "keys": ["launch", "instance", "download"] },
				{ "title": "通用", "keys": ["settings", "multiplayer", "ai", "more", "tasks"] }
			]
		}
		""");
		Assert.Equal("grouped", cfg.Style);
		Assert.Equal(3, cfg.Groups!.Count);
		var items = NavModel.NavItemsFromConfig(cfg);
		Assert.NotEmpty(items);
		// 旧存档的分组标题原样保留
		Assert.Contains(items, i => i.Kind == NavEntryKind.Header && i.Label == "账户");
	}

	[Fact]
	public void OldCompactConfigLoads()
	{
		var cfg = NavConfig.FromJsonString("""
		{
			"ui_nav_style": "compact",
			"ui_nav_order": ["launch", "download", "instance", "ai", "more", "settings", "tasks"],
			"ui_nav_pinned": ["instance", "settings", "java"],
			"ui_nav_hidden": ["ai"],
			"ui_section_members": { "download": ["version", "mod"], "more": ["playtime"] }
		}
		""");
		var items = NavModel.NavItemsFromConfig(cfg);
		Assert.NotEmpty(items);
		Assert.DoesNotContain(items, i => i.Kind == NavEntryKind.Item && i.Key == "ai");
		Assert.Contains(items, i => i.Kind == NavEntryKind.Item && i.Key == "java");
	}

	[Fact]
	public void EmptyAndGarbageConfigsFallBackToDefaults()
	{
		Assert.NotNull(NavModel.NavItemsFromConfig(NavConfig.FromJsonString("{}")));
		Assert.NotNull(NavModel.NavItemsFromConfig(NavConfig.FromJsonString("null")));
		// ui_nav_groups 类型错误时按无分组处理 → 用新默认分组，不抛异常
		var items = NavModel.NavItemsFromConfig(NavConfig.FromJsonString("""{ "ui_nav_style": "grouped", "ui_nav_groups": "broken" }"""));
		Assert.NotEmpty(items);
	}

	[Fact]
	public void NewDefaultGroupsCoverAllEntriesAndRender()
	{
		var items = NavModel.NavItemsFromConfig(new NavConfig());
		// 7 个分组头（GOAL WP4：6-7 个顶级分组）
		Assert.Equal(7, items.Count(i => i.Kind == NavEntryKind.Header));
		// 每个 item 要么映射到页面，要么是可折叠分区头（download/more）
		Assert.All(items.Where(i => i.Kind == NavEntryKind.Item),
			i => Assert.True(NavModel.PageForNavKey.ContainsKey(i.Key) || NavModel.IsSection(i.Key), i.Key));
	}
}
