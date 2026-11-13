using System;
using System.Collections.Generic;
using System.Linq;
using TipCL;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C2b:导航模型(<c>NavModel</c> / <c>NavConfig</c> / <c>NavDef</c> / <c>NavEntry</c> / <c>NavGroup</c>)
/// 与页面注册表(<c>PageRegistry</c>)。
///
/// 期望值一律来自 <see cref="C2bExpected"/>(那里面标了来源:Windows 源 / 任务书 / 本机实测)。
/// 这些用例**全是纯表与纯函数**,不碰 Avalonia 控件 → 按 C1b 的先例不必走会话;
/// 只有真正 <c>new</c> 页面的那几条(<c>PageRegistry</c> 的用例)才包在
/// <c>AvaloniaSession.Run</c> 里(D-79)。
/// </summary>
public class C2bNavTests
{
    // ---------------------------------------------------------------- 页面表

    [Fact]
    public void NavDefs_ids_are_unique_and_non_empty()
    {
        // 【Windows 源】MainWindow.NavDefs 的 10 个 id
        var ids = MainWindow.NavDefs.Select(d => d.Id).ToList();
        Assert.Equal(10, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(C2bExpected.NavDefs.Select(d => d.Id), ids);
    }

    [Fact]
    public void NavDefs_table_matches_the_windows_source_in_order_title_and_glyph()
    {
        Assert.Equal(MainWindow.NavDefs.Count, C2bExpected.NavDefs.Length);
        for (int i = 0; i < C2bExpected.NavDefs.Length; i++)
        {
            (string id, string titleZh, string glyph) = C2bExpected.NavDefs[i];
            NavDef def = MainWindow.NavDefs[i];
            Assert.Equal(id, def.Id);
            Assert.Equal(I18n.L(titleZh), def.Title);
            Assert.Equal(glyph, def.Glyph);
            // C2b:页面还没移植,工厂一律留空(由 PageRegistry 注册) —— 见 D-92
            Assert.Null(def.Create);
        }
    }

    [Fact]
    public void Every_nav_key_in_PageForNavKey_points_at_a_page_id_of_the_table()
    {
        var defIds = new HashSet<string>(MainWindow.NavDefs.Select(d => d.Id), StringComparer.Ordinal);
        Assert.Equal(C2bExpected.PageForNavKey.Length, NavModel.PageForNavKey.Count);
        foreach ((string key, string pageId) in C2bExpected.PageForNavKey)
        {
            Assert.True(NavModel.PageForNavKey.TryGetValue(key, out string? actual), key);
            Assert.Equal(pageId, actual);
            Assert.Contains(pageId, defIds);
        }
    }

    [Fact]
    public void PageForNavKey_lookup_is_case_sensitive_and_sections_are_not_pages()
    {
        // 【Windows 源】字典比较器是序号比较(Dictionary<string,string> 默认):大小写不同就是查不到
        Assert.False(NavModel.PageForNavKey.ContainsKey("LAUNCH"));
        Assert.False(NavModel.PageForNavKey.ContainsKey("Launch"));
        Assert.True(NavModel.PageForNavKey.ContainsKey("launch"));
        // 分区键(两个折叠分区)在导航表里但**不是页面**:它们在可见顺序里靠分区成员展开
        Assert.DoesNotContain("download", NavModel.PageForNavKey.Keys);
        Assert.DoesNotContain("more", NavModel.PageForNavKey.Keys);
    }

    [Fact]
    public void Model_constants_match_the_windows_tables()
    {
        Assert.Equal(C2bExpected.TopKeys, NavModel.TopKeys);
        Assert.Equal(C2bExpected.SectionIds, NavModel.SectionIds);
        Assert.Equal(C2bExpected.BottomKeys, NavModel.BottomKeys);
        Assert.Equal(C2bExpected.AllSubKeys.OrderBy(k => k, StringComparer.Ordinal), NavModel.AllSubKeys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("compact", NavModel.StyleCompact);
        Assert.Equal("grouped", NavModel.StyleGrouped);
        Assert.Equal("grouped", NavModel.DefaultStyle);
        Assert.Equal(C2bExpected.NavDefaultsVersion, NavModel.NavDefaultsVersion);
    }

    [Fact]
    public void Default_nav_order_pinned_and_hidden_match_the_windows_tables()
    {
        Assert.Equal(C2bExpected.DefaultNavOrder, NavModel.DefaultNavOrder);
        Assert.Equal(C2bExpected.DefaultNavPinned, NavModel.DefaultNavPinned);
        Assert.Empty(NavModel.DefaultNavHidden);
        Assert.Equal(new[] { "download", "more" }, NavModel.SubDefaultMembers.Keys.OrderBy(k => k, StringComparer.Ordinal).OrderBy(k => k == "download" ? 0 : 1));
        Assert.Empty(NavModel.SubDefaultMembers["download"]);
        Assert.Equal(new[] { "instance", "account", "multiplayer", "settings", "plugins" }, NavModel.SubDefaultMembers["more"]);
    }

    [Fact]
    public void GroupedNav_has_the_six_windows_groups_in_order()
    {
        Assert.Equal(C2bExpected.GroupedNav.Length, NavModel.GroupedNav.Length);
        for (int i = 0; i < C2bExpected.GroupedNav.Length; i++)
        {
            Assert.Equal(I18n.L(C2bExpected.GroupedNav[i].Title), NavModel.GroupedNav[i].Title);
            Assert.Equal(C2bExpected.GroupedNav[i].Keys, NavModel.GroupedNav[i].Keys);
        }
    }

    [Fact]
    public void Grouped_labels_and_sub_titles_match_the_windows_tables()
    {
        Assert.Equal(I18n.L("下载"), NavModel.GroupedLabels["download"]);
        Assert.Equal(I18n.L("多人联机"), NavModel.GroupedLabels["multiplayer"]);
        Assert.Equal(2, NavModel.GroupedLabels.Count);
        Assert.Equal(I18n.L("版本管理"), NavModel.SubTitles["instance"]);
        Assert.Equal(I18n.L("账号"), NavModel.SubTitles["account"]);
        Assert.Equal(I18n.L("联机"), NavModel.SubTitles["multiplayer"]);
        Assert.Equal(I18n.L("设置"), NavModel.SubTitles["settings"]);
        Assert.Equal(I18n.L("插件"), NavModel.SubTitles["plugins"]);
        Assert.Equal(I18n.L("启动"), NavModel.NavSpecs["launch"]);
        Assert.Equal(I18n.L("游戏"), NavModel.NavSpecs["download"]);
        Assert.Equal(I18n.L("AI 助手"), NavModel.NavSpecs["ai"]);
        Assert.Equal(I18n.L("更多"), NavModel.NavSpecs["more"]);
        Assert.Equal(I18n.L("下载任务"), NavModel.NavSpecs["tasks"]);
    }

    [Theory]
    [InlineData(null, "grouped")]
    [InlineData("", "grouped")]
    [InlineData("grouped", "grouped")]
    [InlineData("compact", "compact")]
    [InlineData("GROUPED", "grouped")]
    [InlineData("nonsense", "grouped")]
    public void NavStyle_normalises_unknown_styles_to_grouped(string? style, string expected)
    {
        // 【Windows 源】NavModel.NavStyle:只认 StyleLabels 里的两个键,其余(含 null/空)一律 grouped
        Assert.Equal(expected, NavModel.NavStyle(new NavConfig { Style = style }));
        Assert.Equal("grouped", NavModel.NavStyle(null));
    }

    [Theory]
    [InlineData("launch", true)]
    [InlineData("more", true)]
    [InlineData("instance", false)]
    [InlineData("nonsense", false)]
    public void IsTop_matches_the_windows_top_keys(string key, bool expected)
    {
        Assert.Equal(expected, NavModel.IsTop(key));
    }

    [Theory]
    [InlineData("download", true)]
    [InlineData("more", true)]
    [InlineData("plugins", false)]
    public void IsSection_matches_the_two_fold_sections(string key, bool expected)
    {
        Assert.Equal(expected, NavModel.IsSection(key));
    }

    [Theory]
    [InlineData("launch", "启动")]
    [InlineData("tasks", "下载任务")]
    [InlineData("plugins", "插件")]
    [InlineData("account", "账号")]
    [InlineData("unknown-key", "unknown-key")]
    public void NavLabel_prefers_top_specs_then_sub_titles_then_the_raw_key(string key, string expected)
    {
        Assert.Equal(I18n.L(expected), NavModel.NavLabel(key));
    }

    // ---------------------------------------------------------------- 侧栏行表

    [Fact]
    public void Default_config_produces_the_windows_grouped_rows()
    {
        List<string> rows = C2bAssert.DescribeRows(NavModel.NavItemsFromConfig(new NavConfig()));
        Assert.Equal(C2bExpected.DefaultGroupedRows, rows);
        Assert.Equal(6, rows.Count(r => r.StartsWith("H:", StringComparison.Ordinal)));
        Assert.DoesNotContain(rows, r => r == "S");
    }

    [Fact]
    public void Default_config_sequence_and_members_match_the_windows_rules()
    {
        var cfg = new NavConfig();
        Assert.Equal(new[] { "launch", "instance", "download", "multiplayer", "account", "ai", "tasks", "more", "settings" }, NavModel.SidebarSequence(cfg));
        Assert.Equal(new[] { "instance", "multiplayer", "account", "settings" }, NavModel.PinnedFromConfig(cfg));
        Dictionary<string, List<string>> members = NavModel.SectionMembersFromConfig(cfg);
        Assert.Empty(members["download"]);
        Assert.Equal(new[] { "plugins" }, members["more"]);
        Assert.Equal(new[] { "download", "more" }, NavModel.VisibleSections(cfg));
    }

    [Fact]
    public void Default_visible_page_order_is_the_windows_visible_flat_order()
    {
        // 【Windows 源 + 本机实测】把侧栏行表按 VisibleFlatOrder 的算法(分区展开 + 去重)压成页面 id
        var cfg = new NavConfig();
        Dictionary<string, List<string>> members = NavModel.SectionMembersFromConfig(cfg);
        var order = new List<string>();
        foreach (NavEntry entry in NavModel.NavItemsFromConfig(cfg).Where(e => e.Kind == NavEntryKind.Item))
        {
            IEnumerable<string> keys = NavModel.IsSection(entry.Key) ? members[entry.Key] : new[] { entry.Key };
            foreach (string key in keys)
            {
                if (NavModel.PageForNavKey.TryGetValue(key, out string? pageId) && !order.Contains(pageId))
                {
                    order.Add(pageId);
                }
            }
        }
        Assert.Equal(C2bExpected.DefaultVisiblePages, order);
    }

    [Fact]
    public void Hidden_keys_disappear_from_rows_pinned_and_sequence()
    {
        var cfg = new NavConfig { Hidden = new List<string> { "ai", "instance" } };
        List<string> rows = C2bAssert.DescribeRows(NavModel.NavItemsFromConfig(cfg));
        Assert.DoesNotContain(rows, r => r == "I:ai");
        Assert.DoesNotContain(rows, r => r == "I:instance");
        Assert.DoesNotContain("ai", NavModel.SidebarSequence(cfg));
        Assert.DoesNotContain("instance", NavModel.PinnedFromConfig(cfg));
        // 分组头还在(组里还有别的键),AI 组变空 → 整组连标题一起去掉
        Assert.DoesNotContain(rows, r => r == "H:AI 助手");
    }

    [Fact]
    public void Compact_style_rows_place_the_stretch_before_the_bottom_keys()
    {
        var cfg = new NavConfig
        {
            Style = "compact",
            Order = C2bExpected.DefaultNavOrder.ToList(),
            Pinned = C2bExpected.DefaultNavPinned.ToList(),
        };
        List<string> rows = C2bAssert.DescribeRows(NavModel.NavItemsFromConfig(cfg));
        Assert.Equal(new[] { "I:launch", "I:download", "I:instance", "S", "I:ai", "I:more", "I:settings", "I:tasks" }, rows);
    }

    [Fact]
    public void Compact_style_pins_extra_sub_keys_from_the_config()
    {
        var cfg = new NavConfig
        {
            Style = "compact",
            Order = new List<string> { "launch", "download", "instance", "ai", "more", "settings", "tasks" },
            Pinned = new List<string> { "instance", "plugins" },
        };
        Assert.Equal(new[] { "instance", "plugins" }, NavModel.PinnedFromConfig(cfg));
        List<string> rows = C2bAssert.DescribeRows(NavModel.NavItemsFromConfig(cfg));
        // settings 既不是顶级键也没被置顶 → 顺式式里不出现(Windows 的 NavItemsFromConfig 同);
        // plugins 是"被置顶但不在 order 里"的兜底键:插在 more/tasks 之前
        Assert.Equal(new[] { "I:launch", "I:download", "I:instance", "S", "I:ai", "I:plugins", "I:more", "I:tasks" }, rows);
    }

    // ---------------------------------------------------------------- 分组布局

    [Fact]
    public void GroupedLayout_falls_back_to_the_default_groups_when_the_config_has_none_usable()
    {
        var cfg = new NavConfig { Groups = new List<NavGroup> { new NavGroup { Title = "  ", Keys = new List<string> { "launch" } } } };
        List<NavGroup> groups = NavModel.GroupedLayout(cfg);
        Assert.Equal(NavModel.GroupedNav.Select(g => I18n.L(g.Title)), groups.Select(g => g.Title));
        Assert.Equal(NavModel.GroupedNav.Select(g => g.Keys.ToList()), groups.Select(g => g.Keys));
    }

    [Fact]
    public void GroupedLayout_keeps_config_titles_dedupes_and_appends_missing_top_keys()
    {
        var cfg = new NavConfig
        {
            Groups = new List<NavGroup>
            {
                new NavGroup { Title = " 我的组 ", Keys = new List<string> { "account", "account", "nonsense", "launch" } },
                new NavGroup { Title = "第二组", Keys = new List<string> { "launch" } },
            },
        };
        List<NavGroup> groups = NavModel.GroupedLayout(cfg);
        Assert.Equal(2, groups.Count);
        Assert.Equal("我的组", groups[0].Title);
        Assert.Equal(new[] { "account", "launch" }, groups[0].Keys);
        // 第二组的 launch 已在第一组出现 → 去重后为空;缺失的顶级键补进最后一组
        Assert.Equal(new[] { "download", "ai", "more", "tasks" }, groups[1].Keys);
    }

    [Fact]
    public void GroupedLayoutPatch_wraps_a_deep_clone()
    {
        var groups = new List<NavGroup> { new NavGroup { Title = "组", Keys = new List<string> { "launch" } } };
        Dictionary<string, object?> patch = NavModel.GroupedLayoutPatch(groups);
        var cloned = Assert.IsType<List<NavGroup>>(patch["ui_nav_groups"]);
        Assert.Equal("组", cloned[0].Title);
        cloned[0].Keys.Add("ai");
        Assert.Single(groups[0].Keys);
    }

    [Theory]
    [InlineData("instance", "ai", true, true)]
    [InlineData("instance", "ai", false, true)]
    [InlineData("instance", "instance", true, false)]
    [InlineData("instance", "nonsense", true, false)]
    public void MoveWithinGroups_moves_only_when_the_target_exists(string key, string target, bool before, bool expected)
    {
        var groups = new List<NavGroup>
        {
            new NavGroup { Title = "A", Keys = new List<string> { "instance", "launch" } },
            new NavGroup { Title = "B", Keys = new List<string> { "ai" } },
        };
        bool moved = NavModel.MoveWithinGroups(groups, key, target, before);
        Assert.Equal(expected, moved);
        if (expected && target == "ai")
        {
            // 目标唯一:整表拼起来之后,key 应该紧邻 target 的前(before)或后
            List<string> merged = groups.SelectMany(g => g.Keys).ToList();
            Assert.Contains(key, merged);
            int at = merged.IndexOf(key);
            int targetAt = merged.IndexOf("ai");
            Assert.Equal(before ? targetAt - 1 : targetAt + 1, at);
        }
    }

    // ---------------------------------------------------------------- 补丁计算

    [Fact]
    public void UnpinNavConfig_moves_a_pinned_page_back_into_its_section()
    {
        var cfg = new NavConfig();
        (Dictionary<string, object?> Patch, string Section)? result = NavModel.UnpinNavConfig(cfg, "instance");
        Assert.NotNull(result);
        Assert.Equal("more", result!.Value.Section);
        var members = Assert.IsType<Dictionary<string, List<string>>>(result.Value.Patch["ui_section_members"]);
        Assert.Contains("instance", members["more"]);
        var groups = Assert.IsType<List<NavGroup>>(result.Value.Patch["ui_nav_groups"]);
        Assert.DoesNotContain(groups.SelectMany(g => g.Keys), k => k == "instance");
    }

    [Fact]
    public void UnpinNavConfig_returns_null_when_the_key_is_not_pinned()
    {
        Assert.Null(NavModel.UnpinNavConfig(new NavConfig(), "launch"));
        Assert.Null(NavModel.UnpinNavConfig(new NavConfig(), "nonsense"));
    }

    [Fact]
    public void UnpinNavConfig_honours_a_requested_section_and_index()
    {
        (Dictionary<string, object?> Patch, string Section)? result = NavModel.UnpinNavConfig(new NavConfig(), "settings", "download", 0);
        Assert.NotNull(result);
        Assert.Equal("download", result!.Value.Section);
        var members = Assert.IsType<Dictionary<string, List<string>>>(result.Value.Patch["ui_section_members"]);
        Assert.Equal("settings", members["download"][0]);
    }

    [Fact]
    public void PinNavConfig_rejects_the_last_member_of_a_section_with_the_windows_message()
    {
        // plugins 是「更多」分区里唯一剩下的成员
        NavModel.PinResult? result = NavModel.PinNavConfig(new NavConfig(), "plugins");
        Assert.NotNull(result);
        Assert.Null(result!.Patch);
        Assert.False(string.IsNullOrEmpty(result.Error));
        Assert.Contains("分区", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void PinNavConfig_moves_a_section_member_into_the_groups()
    {
        var cfg = new NavConfig { Hidden = new List<string> { "account", "multiplayer", "settings" } };
        NavModel.PinResult? result = NavModel.PinNavConfig(cfg, "plugins");
        Assert.NotNull(result);
        Assert.Null(result!.Error);
        var groups = Assert.IsType<List<NavGroup>>(result.Patch!["ui_nav_groups"]);
        Assert.Contains("plugins", groups.SelectMany(g => g.Keys));
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("launch")]
    [InlineData("instance")]
    public void PinNavConfig_returns_null_for_unknown_or_already_pinned_keys(string key)
    {
        Assert.Null(NavModel.PinNavConfig(new NavConfig(), key));
    }

    [Fact]
    public void ReorderNavConfig_grouped_returns_a_groups_patch_and_compact_returns_order()
    {
        var grouped = new NavConfig { Groups = new List<NavGroup> { new NavGroup { Title = "组", Keys = new List<string> { "launch", "ai" } } } };
        Dictionary<string, object?>? patch = NavModel.ReorderNavConfig(grouped, "ai", "launch", true);
        Assert.NotNull(patch);
        var groups = Assert.IsType<List<NavGroup>>(patch!["ui_nav_groups"]);
        // 缺的顶级键会被 GroupedLayout 补进最后一组(Windows 同):download / more / tasks
        Assert.Equal(new[] { "ai", "launch", "download", "more", "tasks" }, groups[0].Keys);

        var compact = new NavConfig { Style = "compact", Order = new List<string> { "launch", "download", "ai" }, Pinned = new List<string> { "instance" } };
        Dictionary<string, object?>? compactPatch = NavModel.ReorderNavConfig(compact, "ai", "launch", true);
        Assert.NotNull(compactPatch);
        Assert.True(compactPatch!.ContainsKey("ui_nav_order"));
        Assert.Null(NavModel.ReorderNavConfig(compact, "ai", "nonsense", true));
        Assert.Null(NavModel.ReorderNavConfig(compact, "ai", "ai", true));
    }

    [Fact]
    public void WriteSidebarSequence_dedupes_filters_and_appends_missing_top_keys()
    {
        Dictionary<string, object?> patch = NavModel.WriteSidebarSequence(new[] { "launch", "launch", "nonsense", "instance" });
        var order = Assert.IsType<List<string>>(patch["ui_nav_order"]);
        Assert.Equal(new[] { "launch", "instance" }, order.Take(2));
        Assert.Equal(C2bExpected.TopKeys, order.Where(k => C2bExpected.TopKeys.Contains(k)));
        var pinned = Assert.IsType<List<string>>(patch["ui_nav_pinned"]);
        Assert.Equal(new[] { "instance" }, pinned);
    }

    [Fact]
    public void MergeNavOrder_rebuilds_the_sequence_from_top_order_and_pinned()
    {
        var cfg = new NavConfig { Style = "compact", Order = new List<string> { "launch", "download", "instance", "ai" }, Pinned = new List<string> { "instance" } };
        List<string> merged = NavModel.MergeNavOrder(cfg, new[] { "tasks", "more" }, new[] { "instance" });
        Assert.Equal("tasks", merged[0]);
        Assert.Contains("instance", merged);
        Assert.Contains("more", merged);
    }

    [Fact]
    public void DefaultNavPatch_resets_all_nav_keys()
    {
        Dictionary<string, object?> patch = NavModel.DefaultNavPatch();
        Assert.Equal(C2bExpected.DefaultNavOrder.ToList(), Assert.IsType<List<string>>(patch["ui_nav_order"]));
        Assert.Equal(C2bExpected.DefaultNavPinned.ToList(), Assert.IsType<List<string>>(patch["ui_nav_pinned"]));
        Assert.Empty(Assert.IsType<List<string>>(patch["ui_nav_hidden"]));
        Assert.Equal("grouped", patch["ui_nav_style"]);
        Assert.Equal(C2bExpected.NavDefaultsVersion, patch["ui_nav_defaults"]);
        Assert.Null(patch["ui_nav_groups"]);
        Assert.Null(patch["ui_section_members"]);
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("""{ "ui_nav_order": [], "ui_nav_pinned": [], "ui_nav_hidden": [] }""", true)]
    [InlineData("""{ "ui_nav_order": ["launch", "download", "instance", "more", "settings", "tasks"], "ui_nav_pinned": ["instance", "settings"], "ui_nav_hidden": ["ai"] }""", true)]
    [InlineData("""{ "ui_nav_order": ["tasks", "launch"], "ui_nav_pinned": ["plugins"], "ui_nav_hidden": [] }""", false)]
    public void NavUntouched_accepts_the_current_and_legacy_defaults(string json, bool expected)
    {
        Assert.Equal(expected, NavModel.NavUntouched(NavConfig.FromJsonString(json)));
        Assert.True(NavModel.NavUntouched(null));
    }

    [Fact]
    public void ToRpcArgs_turns_groups_into_plain_dicts()
    {
        Dictionary<string, object?> patch = NavModel.GroupedLayoutPatch(new List<NavGroup> { new NavGroup { Title = "组", Keys = new List<string> { "launch", "ai" } } });
        Dictionary<string, object?> args = NavModel.ToRpcArgs(patch);
        var list = Assert.IsType<List<Dictionary<string, object>>>(args["ui_nav_groups"]);
        Assert.Equal("组", list[0]["title"]);
        Assert.Equal(new List<string> { "launch", "ai" }, Assert.IsType<List<string>>(list[0]["keys"]));
    }

    // ---------------------------------------------------------------- NavConfig

    [Fact]
    public void FromJsonString_reads_every_nav_key()
    {
        NavConfig cfg = NavConfig.FromJsonString("""
        {
          "ui_nav_style": "compact",
          "ui_nav_order": ["launch", "download"],
          "ui_nav_pinned": ["instance"],
          "ui_nav_hidden": ["ai"],
          "ui_nav_groups": [ { "title": "组", "keys": ["launch"] }, { "title": "", "keys": ["ai"] } ],
          "ui_section_members": { "download": ["instance"], "more": ["plugins"] },
          "ui_sidebar_width": 240
        }
        """);
        Assert.Equal("compact", cfg.Style);
        Assert.Equal(new List<string> { "launch", "download" }, cfg.Order);
        Assert.Equal(new List<string> { "instance" }, cfg.Pinned);
        Assert.Equal(new List<string> { "ai" }, cfg.Hidden);
        Assert.NotNull(cfg.Groups);
        Assert.Single(cfg.Groups!);
        Assert.Equal("组", cfg.Groups![0].Title);
        Assert.NotNull(cfg.SectionMembers);
        Assert.Equal(new List<string> { "instance" }, cfg.SectionMembers!["download"]);
        Assert.Equal(new List<string> { "plugins" }, cfg.SectionMembers!["more"]);
        Assert.Equal(240, cfg.SidebarWidth);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{ "ui_nav_groups": "broken", "ui_section_members": 7, "ui_nav_order": "launch", "ui_sidebar_width": "abc" }""")]
    public void FromJsonString_tolerates_wrong_types(string json)
    {
        NavConfig cfg = NavConfig.FromJsonString(json);
        Assert.Null(cfg.Groups);
        Assert.Null(cfg.SectionMembers);
        Assert.Empty(cfg.Order);
        Assert.Equal(0, cfg.SidebarWidth);
        Assert.NotNull(NavModel.NavItemsFromConfig(cfg));
    }

    [Fact]
    public void FromJsonString_accepts_a_numeric_string_sidebar_width()
    {
        Assert.Equal(210, NavConfig.FromJsonString("""{ "ui_sidebar_width": "210" }""").SidebarWidth);
        Assert.Equal(0, NavConfig.FromJsonString("""{ "ui_sidebar_width": 12.5 }""").SidebarWidth);
    }

    [Fact]
    public void Copy_and_patch_are_deep()
    {
        var cfg = new NavConfig
        {
            Style = "grouped",
            Order = new List<string> { "launch" },
            Groups = new List<NavGroup> { new NavGroup { Title = "组", Keys = new List<string> { "launch" } } },
            SectionMembers = new Dictionary<string, List<string>> { ["more"] = new List<string> { "plugins" } },
            SidebarWidth = 200,
        };
        NavConfig clone = cfg.Clone();
        clone.Order.Add("ai");
        clone.Groups![0].Keys.Add("ai");
        clone.SectionMembers!["more"].Add("instance");
        Assert.Single(cfg.Order);
        Assert.Single(cfg.Groups![0].Keys);
        Assert.Single(cfg.SectionMembers!["more"]);
        Assert.Equal(200, clone.SidebarWidth);

        NavConfig patched = cfg.With(new Dictionary<string, object?>
        {
            ["ui_nav_style"] = "compact",
            ["ui_nav_order"] = new List<string> { "tasks", "" },
            ["ui_nav_groups"] = new List<NavGroup> { new NavGroup { Title = "别的组", Keys = new List<string> { "ai" } } },
            ["ui_sidebar_width"] = 260,
        });
        Assert.Equal("compact", patched.Style);
        Assert.Equal(new List<string> { "tasks" }, patched.Order);
        Assert.Equal("别的组", patched.Groups![0].Title);
        Assert.Equal(260, patched.SidebarWidth);
        // 未出现在补丁里的键原样保留
        Assert.Equal("grouped", cfg.Style);
        Assert.True(patched.SectionMembers!.ContainsKey("more"));
    }

    [Fact]
    public void StrList_filters_non_strings_empty_and_non_arrays()
    {
        NavConfig cfg = NavConfig.FromJsonString("""{ "ui_nav_order": ["launch", "", 7, null, "ai"] }""");
        Assert.Equal(new List<string> { "launch", "ai" }, cfg.Order);
        Assert.Empty(NavConfig.StrList(null));
    }

    // ---------------------------------------------------------------- 页面注册表

    [Fact]
    public void PageRegistry_registers_creates_and_unregisters()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = new RegistryScope();
            scope.Register("probe", () => new ProbePage());
            Assert.True(PageRegistry.Contains("probe"));
            Assert.Contains("probe", PageRegistry.Ids);
            PageBase? page = PageRegistry.Create("probe");
            Assert.IsType<ProbePage>(page);
            Assert.NotSame(page, PageRegistry.Create("probe")); // 工厂每次建新的,缓存由 PageHost 负责
            Assert.True(PageRegistry.Unregister("probe"));
            Assert.False(PageRegistry.Unregister("probe"));
            Assert.Null(PageRegistry.Create("probe"));
        });
    }

    [Fact]
    public void PageRegistry_lookup_is_case_sensitive_and_never_throws()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = new RegistryScope();
            scope.Register("launch", () => new ProbePage());
            Assert.NotNull(PageRegistry.Create("launch"));
            Assert.Null(PageRegistry.Create("LAUNCH"));
            Assert.Null(PageRegistry.Create("Launch"));
            Assert.False(PageRegistry.Contains("LAUNCH"));
            Assert.Null(PageRegistry.Create(""));
        });
    }

    [Fact]
    public void PageRegistry_snapshot_and_restore_round_trip()
    {
        AvaloniaSession.Run(() =>
        {
            IReadOnlyDictionary<string, Func<PageBase>> snapshot = PageRegistry.Snapshot();
            try
            {
                PageRegistry.Register("zz-c2b", () => new ProbePage());
                Assert.True(PageRegistry.Contains("zz-c2b"));
                PageRegistry.Restore(snapshot);
                Assert.False(PageRegistry.Contains("zz-c2b"));
            }
            finally
            {
                PageRegistry.Restore(snapshot);
            }
        });
    }

    [Fact]
    public void PageRegistry_rejects_empty_ids_and_null_factories()
    {
        Assert.Throws<ArgumentException>(() => PageRegistry.Register("", () => new ProbePage()));
        Assert.Throws<ArgumentException>(() => PageRegistry.Register("x", null!));
    }
}
