// R32-MAC-TABS:把布局 v2 文档的 `shell.tabs` 接到 Mac 侧栏上(U1 / R32-D26 的裁定)。
//
// 本包落地的三件事,逐件都有用例(D25:每条都能因"移除该修复"而变红 —— 证伪记录见 progress/R32-MAC-TABS.md §4):
//   1) `ShellTabPlan`(新件):跨树映射表(Windows 5 个语义键 → Mac 导航键)+ 侧栏叠加算法
//      + 回落(NavConfig)+ 写回(v2 文档 + v1 投影);
//   2) `MainWindow.SyncSidebar` / `VisibleFlatEntries` 改吃 `ShellTabPlan.NavItems(...)`
//      ⇒ 侧栏与 ⌘1..⌘9 的可见顺序都由 `shell.tabs` 驱动;
//   3) `MainWindow` 订阅 `LayoutStoreV2.LayoutChanged` ⇒ 设置页改页签后侧栏当场重建(Windows 的
//      `MainWindow.OnLayoutChanged → RebuildShellTabs` 是同一职责)。
//
// 口径:
//   * 纯逻辑用例不起 Avalonia;碰控件/窗口的一律经 AvaloniaSession.Run(headless + Skia,不建 OS 窗口);
//   * 落盘一律 C3aHome(把 LayoutStoreV2 / PageLayoutStore 的 DirOverride 指到 TestHome 下的一次性目录),
//     绝不碰用户真实数据目录(红线);
//   * 期望值照 R32-D26 的裁定与 Mac 侧栏现有表(NavModel.GroupedNav / DefaultNavOrder)写,不照抄报告。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using TipCL.Core.Launcher.Layout;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>R32-MAC-TABS:`shell.tabs` → Mac 侧栏(U1 落地)的纯逻辑 + 可视化树用例。</summary>
public class R32MacTabsTests
{
	// ───────────────────────────── 小工具 ─────────────────────────────

	/// <summary>把行表压成 "H:标题" / "S" / "I:键"(与外壳的可视树读法一一对应)。</summary>
	private static List<string> Describe(IEnumerable<NavEntry> entries)
	{
		return entries.Select(entry => entry.Kind switch
		{
			NavEntryKind.Header => "H:" + entry.Label,
			NavEntryKind.Stretch => "S",
			_ => "I:" + entry.Key
		}).ToList();
	}

	/// <summary>侧栏可视树的行表(读真控件,不是读模型)。</summary>
	private static List<string> DescribeSidebar(MainWindow window)
	{
		Grid nav = C2bAssert.ByName<Grid>(window.Sidebar, "NavGrid");
		List<string> rows = new List<string>();
		foreach (Control child in nav.Children)
		{
			if (child is TextBlock header)
			{
				rows.Add("H:" + header.Text);
			}
			else if (child is ToggleButton item)
			{
				rows.Add("I:" + item.Tag);
			}
		}
		return rows;
	}

	/// <summary>侧栏里某个导航键的行(没有就是 null;= "这一行没进可视树")。</summary>
	private static ToggleButton? Row(MainWindow window, string navKey)
	{
		Grid nav = C2bAssert.ByName<Grid>(window.Sidebar, "NavGrid");
		return nav.Children.OfType<ToggleButton>().FirstOrDefault(row => string.Equals(row.Tag as string, navKey, StringComparison.Ordinal));
	}

	/// <summary>只留条目键(去掉分组头/撑条),便于只看"页集与顺序"。</summary>
	private static List<string> ItemKeys(IEnumerable<NavEntry> entries)
	{
		return (from entry in entries where entry.Kind == NavEntryKind.Item select entry.Key).ToList();
	}

	/// <summary>撑条在可视树里的位置 = 唯一那一行的 Star 高度(它在 <c>_navGrid</c> 里没有控件)。</summary>
	private static int StarRowIndex(MainWindow window)
	{
		Grid nav = C2bAssert.ByName<Grid>(window.Sidebar, "NavGrid");
		for (int i = 0; i < nav.RowDefinitions.Count; i++)
		{
			if (nav.RowDefinitions[i].Height.IsStar)
			{
				return i;
			}
		}
		return -1;
	}

	/// <summary>把一份文档的 <c>shell.tabs</c> 落盘(C3aHome 的隔离目录里)。</summary>
	private static void SeedTabs(params string[] tabs)
	{
		LayoutV2 doc = LayoutStoreV2.Load();
		doc.Layout.Shell.Tabs = tabs.ToList();
		Assert.True(LayoutStoreV2.Save(doc), "v2 文档必须写得进隔离目录");
	}

	/// <summary>出厂默认的表(改动前的 <c>NavModel.NavItemsFromConfig(new NavConfig())</c> 逐项值)。</summary>
	private static List<string> FactoryRows()
	{
		return Describe(NavModel.NavItemsFromConfig(new NavConfig()));
	}

	// ═══════════════════════ 1. 映射表(D26:5 个语义键全映射,多出 2 页保持原生)═══════════════════════

	/// <summary>
	/// 映射表逐项:launch→launch、library→instance、download→download、plugins→more、settings→settings。
	/// 这 5 个键 + 原生 2 键(ai / tasks)**恰好铺满** <see cref="NavModel.DefaultNavOrder"/> 的 7 个键 ——
	/// 这就是 D26 说的"5 个语义键全映射,多出 2 页"的算术闭环。
	/// </summary>
	[Fact]
	public void Mapping_covers_five_semantic_keys_and_leaves_exactly_two_mac_pages_native()
	{
		Assert.Equal(5, ShellTabPlan.Mapping.Length);
		Assert.Equal(("launch", "launch"), ShellTabPlan.Mapping[0]);
		Assert.Equal(("library", "instance"), ShellTabPlan.Mapping[1]);
		Assert.Equal(("download", "download"), ShellTabPlan.Mapping[2]);
		Assert.Equal(("plugins", "more"), ShellTabPlan.Mapping[3]);
		Assert.Equal(("settings", "settings"), ShellTabPlan.Mapping[4]);

		// Windows 侧五个键就是 LayoutSchema.DefaultTabs(白名单不许漂移)
		Assert.Equal(LayoutSchema.DefaultTabs, ShellTabPlan.Mapping.Select(pair => pair.TabKey));

		// Mac 多出的两页
		Assert.Equal(new[] { "ai", "tasks" }, ShellTabPlan.NativeNavKeys);

		// 5 + 2 = NavConfig 的 7 键,且两两不相交
		List<string> all = ShellTabPlan.MappedNavKeys.Concat(ShellTabPlan.NativeNavKeys).ToList();
		Assert.Equal(NavModel.DefaultNavOrder.Length, all.Count);
		Assert.Equal(NavModel.DefaultNavOrder.OrderBy(k => k, StringComparer.Ordinal), all.OrderBy(k => k, StringComparer.Ordinal));
		Assert.Empty(ShellTabPlan.MappedNavKeys.Intersect(ShellTabPlan.NativeNavKeys, StringComparer.Ordinal));

		// 双向查表:非映射键一律 null(Mac 多出的两页不写进 shell.tabs)
		Assert.Equal("instance", ShellTabPlan.NavKeyOf("library"));
		Assert.Equal("library", ShellTabPlan.TabKeyOf("instance"));
		Assert.Null(ShellTabPlan.TabKeyOf("ai"));
		Assert.Null(ShellTabPlan.TabKeyOf("tasks"));
		Assert.Null(ShellTabPlan.NavKeyOf("nope"));
		Assert.Null(ShellTabPlan.NavKeyOf(null));
		Assert.False(ShellTabPlan.IsMappedNavKey("ai"));
		Assert.True(ShellTabPlan.IsMappedNavKey("more"));
	}

	// ═══════════════════════ 2. 回落:缺 shell.tabs ⇒ NavConfig 原生(默认不许变样)═══════════════════════

	/// <summary>
	/// 出厂五键 = "未设置"。<c>LayoutStoreV2.Load</c> 在 v2 缺失 / v1 迁移 / 首次运行时都把 Tabs 填成
	/// <see cref="LayoutSchema.DefaultTabs"/>,所以"文档里没写"与"写了出厂值"在语义上必须等价
	/// (否则默认外观就会变样 —— 这是本包最硬的一条不变量)。
	/// </summary>
	[Fact]
	public void Factory_tab_list_is_treated_as_unset()
	{
		Assert.False(ShellTabPlan.HasCustomTabs(null));
		Assert.False(ShellTabPlan.HasCustomTabs(new ShellLayout { Tabs = new List<string>() }));
		Assert.False(ShellTabPlan.HasCustomTabs(new ShellLayout { Tabs = LayoutSchema.DefaultTabs.ToList() }));

		Assert.True(ShellTabPlan.HasCustomTabs(new ShellLayout { Tabs = new List<string> { "settings", "launch", "library", "download", "plugins" } }));
		Assert.True(ShellTabPlan.HasCustomTabs(new ShellLayout { Tabs = new List<string> { "launch", "library", "download", "plugins" } }));
	}

	/// <summary>
	/// <c>shell.tabs</c> 缺失(null / 空 / 出厂五键)时,行表必须**逐项等于** <c>NavModel.NavItemsFromConfig</c>
	/// —— 也就是与 <c>26097e5</c>(本包改动前)的渲染路径完全一致。分组式与精简式两条分支都钉。
	/// </summary>
	[Fact]
	public void Missing_shell_tabs_falls_back_to_navconfig_rows_verbatim()
	{
		using var home = new C3aHome();

		NavConfig grouped = new NavConfig();
		NavConfig compact = new NavConfig
		{
			Style = NavModel.StyleCompact,
			Order = NavModel.DefaultNavOrder.ToList(),
			Pinned = NavModel.DefaultNavPinned.ToList()
		};
		List<ShellLayout?> unset = new List<ShellLayout?>
		{
			null,
			new ShellLayout(),
			new ShellLayout { Tabs = new List<string>() },
			new ShellLayout { Tabs = LayoutSchema.DefaultTabs.ToList() }
		};

		foreach (NavConfig cfg in new[] { grouped, compact })
		{
			foreach (ShellLayout? shell in unset)
			{
				Assert.Equal(
					Describe(NavModel.NavItemsFromConfig(cfg)),
					Describe(ShellTabPlan.NavItems(cfg, shell)));
			}
		}

		// 出厂行表本身也钉一份(默认外观的"金标";分组式 6 组 9 条目)
		Assert.Equal(
			new List<string>
			{
				"H:" + I18n.L("启动"), "I:launch",
				"H:" + I18n.L("库"), "I:instance", "I:download", "I:multiplayer",
				"H:" + I18n.L("社区"), "I:account",
				"H:" + I18n.L("AI 助手"), "I:ai",
				"H:" + I18n.L("工具"), "I:tasks", "I:more",
				"H:" + I18n.L("设置"), "I:settings"
			},
			FactoryRows());
	}

	/// <summary>
	/// v1 兼容:只有 <c>ui_page_layouts.json</c> 的老数据目录(v2 尚未落盘)⇒ <c>LayoutStoreV2</c> 走 v1 迁移,
	/// <c>shell.tabs</c> 取出厂值 ⇒ 视同未设置 ⇒ 侧栏仍是 NavConfig 原生行为。
	/// </summary>
	[Fact]
	public void V1_only_home_has_no_shell_tabs_so_the_sidebar_stays_native()
	{
		using var home = new C3aHome();
		File.WriteAllText(Path.Combine(home.Dir, "ui_page_layouts.json"),
			"{\"version\":1,\"kind\":\"tipcl-page-layout\",\"pages\":{\"launch\":{\"version\":1,\"blocks\":[{\"id\":\"recent\",\"hidden\":true}]}}}");
		LayoutStoreV2.ResetCache();

		// 真的走了 v1 回落(否则下面两条断言只是默认值的同义反复)
		LayoutV2 doc = LayoutStoreV2.Load();
		Assert.True(doc.Layout.Pages.ContainsKey("launch"), "v1 的 launch 页必须被迁移进 v2 文档");
		Assert.Equal(LayoutSchema.DefaultTabs.ToList(), doc.Layout.Shell.Tabs);

		Assert.False(ShellTabPlan.HasCustomTabs(doc.Layout.Shell));
		Assert.Equal(FactoryRows(), Describe(ShellTabPlan.NavItems(new NavConfig())));
	}

	// ═══════════════════════ 3. 文档存在 ⇒ 顺序/显隐按文档 ═══════════════════════

	/// <summary>
	/// 文档 = ["settings","launch","download","library"](plugins 被隐掉、顺序被打乱):
	/// 映射出的 Mac 顺序 = settings / launch / download / instance,<c>more</c> 不在计划里。
	/// </summary>
	[Fact]
	public void Document_order_and_visibility_drive_the_five_mapped_rows()
	{
		using var home = new C3aHome();
		ShellLayout shell = new ShellLayout
		{
			Tabs = new List<string> { "settings", "launch", "download", "library" }
		};
		Assert.True(ShellTabPlan.HasCustomTabs(shell));
		Assert.Equal(new List<string> { "settings", "launch", "download", "instance" }, ShellTabPlan.MappedOrder(shell).Take(4).ToList());
		Assert.Equal(new List<string> { "more" }, ShellTabPlan.HiddenMapped(shell));

		// 侧栏:5 个映射行按文档顺序落位;more 被抽走
		List<string> keys = ShellTabPlan.SidebarKeys(new NavConfig(), shell);
		Assert.Equal(new[] { "settings", "launch", "download", "instance" }, keys.Where(ShellTabPlan.IsMappedNavKey));
		Assert.DoesNotContain("more", keys);
	}

	/// <summary>
	/// 文档把某个映射键隐掉 ⇒ 那一行不进侧栏;而 Mac 多出的 <c>ai</c> / <c>tasks</c> 与
	/// <c>multiplayer</c> / <c>account</c> **一律原样还在**(D26:多项页保持原生,不许为对齐键数删页面)。
	/// </summary>
	[Fact]
	public void Overlay_keeps_the_two_mac_only_pages_and_the_native_sub_rows()
	{
		using var home = new C3aHome();
		ShellLayout shell = new ShellLayout { Tabs = new List<string> { "launch", "download" } };

		List<string> keys = ShellTabPlan.SidebarKeys(new NavConfig(), shell);
		Assert.Contains("ai", keys);
		Assert.Contains("tasks", keys);
		Assert.Contains("multiplayer", keys);
		Assert.Contains("account", keys);
		// launch / download 在;library(instance) / plugins(more) 被隐掉
		Assert.Contains("launch", keys);
		Assert.Contains("download", keys);
		Assert.DoesNotContain("instance", keys);
		Assert.DoesNotContain("more", keys);

		// ai / tasks 的相对位置没被搬动:它们仍是基准表里的相对次序
		List<string> native = keys.Where(k => !ShellTabPlan.IsMappedNavKey(k)).ToList();
		List<string> baseNative = ItemKeys(NavModel.NavItemsFromConfig(new NavConfig()))
			.Where(k => !ShellTabPlan.IsMappedNavKey(k)).ToList();
		Assert.Equal(baseNative, native);
	}

	/// <summary>
	/// 文档与 NavConfig 打架时的优先级:<c>shell.tabs</c> 是 5 个映射键的**显隐真源**。
	/// ① NavConfig 隐藏了 more(<c>ui_nav_hidden</c>),文档点名要 plugins ⇒ more 回到侧栏;
	/// ② NavConfig 没隐藏任何键,文档隐掉 plugins ⇒ more 消失。
	/// </summary>
	[Fact]
	public void Document_visibility_beats_navconfig_for_mapped_keys()
	{
		using var home = new C3aHome();
		NavConfig hideMore = new NavConfig { Hidden = new List<string> { "more" } };
		ShellLayout showPlugins = new ShellLayout
		{
			Tabs = new List<string> { "settings", "plugins", "library", "download", "launch" }
		};
		Assert.Contains("more", ShellTabPlan.SidebarKeys(hideMore, showPlugins));

		ShellLayout hidePlugins = new ShellLayout
		{
			Tabs = new List<string> { "settings", "launch", "library", "download" }
		};
		Assert.DoesNotContain("more", ShellTabPlan.SidebarKeys(new NavConfig(), hidePlugins));

		// NavConfig 对**非映射键**仍然说了算(文档管不到 ai/tasks)
		NavConfig hideAi = new NavConfig { Hidden = new List<string> { "ai" } };
		Assert.DoesNotContain("ai", ShellTabPlan.SidebarKeys(hideAi, showPlugins));
		Assert.Contains("more", ShellTabPlan.SidebarKeys(hideAi, showPlugins));
	}

	/// <summary>
	/// <c>settings</c> 永远在(Windows <c>ShellTabs.OrderedKeys</c> 的合同):文档把它漏掉,
	/// <see cref="ShellTabs.OrderedKeys"/> 补到末尾 ⇒ 侧栏的设置行不会消失。
	/// </summary>
	[Fact]
	public void Settings_row_survives_a_document_that_forgets_it()
	{
		using var home = new C3aHome();
		ShellLayout shell = new ShellLayout { Tabs = new List<string> { "launch", "library", "download", "plugins" } };
		List<string> keys = ShellTabPlan.SidebarKeys(new NavConfig(), shell);
		Assert.Contains("settings", keys);
		Assert.Equal("settings", keys.Last(k => ShellTabPlan.IsMappedNavKey(k)));
	}

	/// <summary>
	/// 精简式特有的撑条(<see cref="NavEntry.Stretch"/>)在叠加后按 <c>NavModel</c> 的规则重算:
	/// ① 只要底部键仍在后面,撑条就仍只有一条、并且压到第一个底部键之前;
	/// ② 重排把底部键(settings)顶到第一行时,<c>NavModel</c> 的 <c>stretchAt &lt;= 0 → -1</c> 同一条线
	///    ⇒ 不插撑条(否则会插到第 0 行前面,把整列内容顶下去)。分组式不产生撑条。
	/// </summary>
	[Fact]
	public void Overlay_keeps_one_stretch_before_the_first_bottom_key()
	{
		using var home = new C3aHome();
		NavConfig compact = new NavConfig
		{
			Style = NavModel.StyleCompact,
			Order = NavModel.DefaultNavOrder.ToList(),
			Pinned = NavModel.DefaultNavPinned.ToList()
		};

		// ① 文档只隐掉 plugins,前三个映射键顺序不变 ⇒ 撑条仍在 ai 之前
		ShellLayout keepOrder = new ShellLayout { Tabs = new List<string> { "launch", "download", "library", "settings" } };
		List<string> rows = Describe(ShellTabPlan.NavItems(compact, keepOrder));
		Assert.Equal(1, rows.Count(row => row == "S"));
		int stretch = rows.IndexOf("S");
		Assert.StartsWith("I:", rows[stretch + 1], StringComparison.Ordinal);
		Assert.Contains(rows[stretch + 1].Substring(2), NavModel.BottomKeys);
		Assert.Equal(new[] { "I:launch", "I:download", "I:instance" }, rows.Take(stretch));

		// ② 文档把 settings 拍到第一行 ⇒ 第一个底部键就在第 0 行,撑条无处可插
		ShellLayout settingsFirst = new ShellLayout { Tabs = new List<string> { "settings", "launch", "library", "download", "plugins" } };
		List<string> reordered = Describe(ShellTabPlan.NavItems(compact, settingsFirst));
		Assert.Equal("I:settings", reordered[0]);
		Assert.DoesNotContain("S", reordered);

		// 分组式没有撑条
		Assert.DoesNotContain("S", Describe(ShellTabPlan.NavItems(new NavConfig(), settingsFirst)));
	}

	// ═══════════════════════ 4. 写回(v2 文档 + v1 投影)═══════════════════════

	/// <summary>
	/// 侧栏侧写回口:<see cref="ShellTabPlan.WriteOrder"/> 只把 5 个映射键翻成 <c>shell.tabs</c> 的键,
	/// 落盘仍走 <c>LayoutStoreV2.Save</c> —— 先原子写 v2,再把 v1 投影(<c>ui_page_layouts.json</c>)
	/// 单向写通(与 Windows 同口径)。断言:① v2 里确实写了 tabs;② 重新读盘拿到同一集合;
	/// ③ v1 投影文件仍在且原有页块没被这次写弄丢。
	/// </summary>
	[Fact]
	public void Write_order_persists_shell_tabs_to_the_v2_document_and_rewrites_the_v1_projection()
	{
		using var home = new C3aHome();
		// v1 里先放一页,证明"写回页签"不会把 v1 投影写坏(它由 v2 单向重算)
		File.WriteAllText(Path.Combine(home.Dir, "ui_page_layouts.json"),
			"{\"version\":1,\"kind\":\"tipcl-page-layout\",\"pages\":{\"launch\":{\"version\":1,\"blocks\":[{\"id\":\"recent\",\"hidden\":false}]}}}");
		LayoutStoreV2.ResetCache();
		Assert.True(LayoutStoreV2.Load().Layout.Pages.ContainsKey("launch"));

		// 侧栏侧顺序(含 Mac 独有的 ai/tasks —— 它们不该被写进 shell.tabs)
		ShellLayout shell = ShellTabPlan.WriteOrder(new[] { "settings", "ai", "launch", "tasks", "instance", "download", "more" });
		Assert.Equal(new List<string> { "settings", "launch", "library", "download", "plugins" }, shell.Tabs);

		// ① v2 文件里确实有 shell.tabs(读原文,不只看内存)
		string v2 = File.ReadAllText(Path.Combine(home.Dir, "ui_layout_v2.json"));
		Assert.Contains("\"tabs\"", v2);
		Assert.Contains("\"library\"", v2);

		// ② 换掉内存缓存(模拟重启)后仍是同一集合与同一顺序
		LayoutStoreV2.ResetCache();
		Assert.Equal(new List<string> { "settings", "launch", "library", "download", "plugins" }, LayoutStoreV2.Load().Layout.Shell.Tabs);

		// ③ v1 投影:文件在、页还在、块还在(单向写通没有把它写空)
		string v1 = File.ReadAllText(Path.Combine(home.Dir, "ui_page_layouts.json"));
		Assert.Contains("tipcl-page-layout", v1);
		Assert.Contains("launch", v1);
	}

	/// <summary>
	/// 写回的非对称边界:Mac 多出的两页(<c>ai</c> / <c>tasks</c>)与未知键**不碰文档**
	/// —— 它们的显隐归 <c>ui_nav_*</c>(NavConfig)管,这是 D26 的超集规则。
	/// </summary>
	[Fact]
	public void Set_visible_on_a_native_page_never_touches_the_document()
	{
		using var home = new C3aHome();
		SeedTabs("settings", "launch", "library", "download");
		string before = File.ReadAllText(Path.Combine(home.Dir, "ui_layout_v2.json"));

		ShellLayout after = ShellTabPlan.SetVisible("ai", visible: false);
		Assert.Equal(new List<string> { "settings", "launch", "library", "download" }, after.Tabs);
		Assert.Equal(before, File.ReadAllText(Path.Combine(home.Dir, "ui_layout_v2.json")));

		// 映射键照常写盘
		ShellLayout written = ShellTabPlan.SetVisible("more", visible: true);
		Assert.Contains("plugins", written.Tabs);
		Assert.NotEqual(before, File.ReadAllText(Path.Combine(home.Dir, "ui_layout_v2.json")));

		// settings 不可隐藏(Windows ShellTabs 的合同)
		Assert.Contains("settings", ShellTabPlan.SetVisible("settings", visible: false).Tabs);
	}

	/// <summary>整体复位回出厂五键 ⇒ 文档回到"未设置"态 ⇒ 侧栏回到 NavConfig 原生行表。</summary>
	[Fact]
	public void Reset_to_factory_gives_the_native_sidebar_back()
	{
		using var home = new C3aHome();
		SeedTabs("settings", "launch");
		Assert.NotEqual(FactoryRows(), Describe(ShellTabPlan.NavItems(new NavConfig())));

		ShellTabPlan.ResetToFactory();
		Assert.Equal(LayoutSchema.DefaultTabs.ToList(), ShellTabPlan.CurrentShell().Tabs);
		Assert.False(ShellTabPlan.HasCustomTabs(ShellTabPlan.CurrentShell()));
		Assert.Equal(FactoryRows(), Describe(ShellTabPlan.NavItems(new NavConfig())));
	}

	// ═══════════════════════ 5. 可视化树(headless + Skia,不建 OS 窗口)═══════════════════════

	/// <summary>
	/// 真外壳(headless + Skia)的侧栏行表必须按文档渲染:设置/启动/下载/版本管理按文档顺序落位,
	/// 插件对应的「更多」行整行不在可视树里;Mac 独有的 ai / tasks 仍在。
	/// </summary>
	[Fact]
	public void Sidebar_visual_tree_follows_the_document_order_and_hides_the_dropped_tab()
	{
		AvaloniaSession.Run(() =>
		{
			using var home = new C3aHome();
			SeedTabs("settings", "launch", "download", "library");

			using var shell = ShellFixture.Create();
			UiHost.Flush();

			List<string> rows = DescribeSidebar(shell.Window);
			Assert.Equal(
				new[] { "settings", "launch", "download", "instance" },
				rows.Where(row => row.StartsWith("I:", StringComparison.Ordinal))
					.Select(row => row.Substring(2))
					.Where(ShellTabPlan.IsMappedNavKey));
			Assert.DoesNotContain("I:more", rows);
			Assert.Contains("I:ai", rows);
			Assert.Contains("I:tasks", rows);
			Assert.Null(Row(shell.Window, "more"));

			// 行是真的能点的行(不是只剩模型):instance 行仍绑着页面
			Assert.NotNull(Row(shell.Window, "instance"));
			Assert.NotNull(Row(shell.Window, "settings"));
		});
	}

	/// <summary>
	/// 默认(无 <c>shell.tabs</c>)时侧栏与改动前逐项相同 —— 这条是本包的"零回归"门禁:
	/// 可视树行表 == <c>NavModel.NavItemsFromConfig</c>(未改动的旧实现)的行表。
	/// 两条样式分支都钉:分组式(出厂默认)与精简式(<c>ui_nav_style=compact</c>)。
	/// 精简式那一支是关键 —— 出厂五键的顺序(launch/library/download/plugins/settings)映射成
	/// <c>launch/instance/download/more/settings</c>,与精简式的 <c>launch/download/instance/…</c> **不同**;
	/// 所以只要把"出厂值视同未设置"的回落判据去掉(哪怕只在默认文档上照样叠加),这一支就会红。
	/// </summary>
	[Fact]
	public void Sidebar_visual_tree_is_unchanged_when_the_document_has_no_shell_tabs()
	{
		AvaloniaSession.Run(() =>
		{
			using var home = new C3aHome();
			Assert.False(File.Exists(Path.Combine(home.Dir, "ui_layout_v2.json")), "本用例的前提:数据目录里没有 v2 文档");

			using var shell = ShellFixture.Create();
			UiHost.Flush();

			// ① 出厂分组式
			Assert.Equal(FactoryRows(), DescribeSidebar(shell.Window));

			// ② 精简式(后端 settings 推下来的一份 ui_nav_*)
			using System.Text.Json.JsonDocument compact = System.Text.Json.JsonDocument.Parse(
				"{\"ui_nav_style\":\"compact\"," +
				"\"ui_nav_order\":[\"launch\",\"download\",\"instance\",\"ai\",\"more\",\"settings\",\"tasks\"]," +
				"\"ui_nav_pinned\":[\"instance\",\"settings\"]}");
			shell.Window.ApplyNavSettings(compact.RootElement);
			C3cFiles.Settle();

			NavConfig expected = shell.Window.NavConfig;
			Assert.Equal(NavModel.StyleCompact, NavModel.NavStyle(expected));
			// 撑条在可视树里**没有控件**(它只是那一行的 Star 高度),所以比行表时把它滤掉,
			// 再用行定义另外钉它的位置。
			Assert.Equal(
				Describe(NavModel.NavItemsFromConfig(expected)).Where(row => row != "S").ToList(),
				DescribeSidebar(shell.Window));
			Assert.Equal(new[] { "I:launch", "I:download", "I:instance" },
				DescribeSidebar(shell.Window).Take(3));
			Assert.Equal(3, StarRowIndex(shell.Window));
		});
	}

	/// <summary>
	/// 接线:`LayoutStoreV2.Save`(设置页「页签与标题栏」/ RPC / 导入都会走到它)之后侧栏**当场**重建
	/// —— 这条钉的是 <c>MainWindow</c> 对 <c>LayoutStoreV2.LayoutChanged</c> 的订阅;
	/// 没有订阅时侧栏要等下一次 <c>ApplyNavSettings</c> 才变(本用例会红)。
	/// </summary>
	[Fact]
	public void Settings_write_of_shell_tabs_rebuilds_the_sidebar_in_the_same_session()
	{
		AvaloniaSession.Run(() =>
		{
			using var home = new C3aHome();
			using var shell = ShellFixture.Create();
			UiHost.Flush();

			Assert.NotNull(Row(shell.Window, "more"));

			// Windows 设置页「页签与标题栏」里点掉「插件」= 同一次调用
			ShellTabs.SetTabVisible("plugins", visible: false);
			C3cFiles.Settle();

			Assert.Null(Row(shell.Window, "more"));
			Assert.DoesNotContain("I:more", DescribeSidebar(shell.Window));
			// 其余 4 行原样还在(只少了一行)
			Assert.NotNull(Row(shell.Window, "launch"));
			Assert.NotNull(Row(shell.Window, "instance"));
			Assert.NotNull(Row(shell.Window, "download"));
			Assert.NotNull(Row(shell.Window, "settings"));

			// 再点回来:行回到侧栏。注意 Windows `ShellTabs.SetTabVisible` 的口径是**追加到末尾**
			// (`OrderedKeys` 已含 settings,于是 plugins 落在 settings 之后)—— 侧栏照文档顺序渲染,
			// 所以「更多」出现在「设置」之后;这是既有 ShellTabs 合同(逐字移植件)的既有行为,不在这里改。
			ShellTabs.SetTabVisible("plugins", visible: true);
			C3cFiles.Settle();
			Assert.NotNull(Row(shell.Window, "more"));
			List<string> mappedRows = DescribeSidebar(shell.Window)
				.Where(row => row.StartsWith("I:", StringComparison.Ordinal))
				.Select(row => row.Substring(2))
				.Where(ShellTabPlan.IsMappedNavKey)
				.ToList();
			Assert.Equal(ShellTabPlan.MappedOrder(ShellTabPlan.CurrentShell()), mappedRows);
			Assert.Equal(5, mappedRows.Count);

			// 复位回出厂五键 ⇒ 侧栏回到 NavConfig 原生行表(设置仍在该在的位置)
			ShellTabPlan.ResetToFactory();
			C3cFiles.Settle();
			Assert.Equal(FactoryRows(), DescribeSidebar(shell.Window));
		});
	}

	/// <summary>
	/// 菜单「页面」与 ⌘1..⌘9 的可见顺序跟侧栏一致(<c>VisibleFlatEntries</c> 也改吃计划表)。
	/// 文档把 settings 拍到最前 ⇒ 可见顺序的第一项是 settings 行对应的页面。
	/// </summary>
	[Fact]
	public void Visible_flat_order_follows_the_sidebar_plan()
	{
		AvaloniaSession.Run(() =>
		{
			using var home = new C3aHome();
			SeedTabs("settings", "launch", "library", "download", "plugins");

			using var shell = ShellFixture.Create();
			UiHost.Flush();

			List<string> order = shell.Window.VisibleFlatOrder();
			Assert.NotEmpty(order);
			Assert.Equal("settings", order[0]);
			Assert.True(order.IndexOf("launch") > 0);
		});
	}
}