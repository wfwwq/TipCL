// R32-MACMORE:布局 v2「更多入口」包的单元测试 —— mac(Avalonia)树落地件。
//
// 覆盖本包落地/补齐的六件(mac 树在 26097e5 之前没有):
//   1) Pages/ShellTabs.cs        —— 顶栏页签 / 标题栏的纯逻辑层(Windows `Pages/Layout/ShellTabs.cs`,288 行);
//   2) Pages/SplitCatalog.cs     —— 13 处分栏现状表(Windows `Pages/Layout/PageLayoutHost.cs:28-295`);
//   3) Pages/LayoutBlockEntryCatalog.cs —— 设置页「页面区块」入口表(Windows `SettingsLayoutPage.cs:810`);
//   4) Pages/LayoutProfiles.cs   —— 整机布局方案(Windows `SettingsLayoutPage.cs:967`);
//   5) Pages/LayoutJsonEditor.cs —— 布局 JSON 校验 / 应用(Windows `SettingsLayoutPage.cs:1309`);
//   6) Pages/LayoutWidgetAdd.cs  —— **[R32-MACMORE 新增]** 加块(自建区块)纯逻辑;Windows 冻结件里那一节
//      只有入口、按钮是 `add.IsEnabled = false`(L6 未落地),本包把「加块」路径打通。
//
// 口径:断言全部照 Windows 冻结件 `wt-r31-merge @0a304a7` 的源码事实写,不照抄上一包报告;
// 涉及落盘的用例一律用 `C3aHome`(把 PageLayoutStore / LayoutStoreV2 的 DirOverride 指到 TestHome 下的一次性目录),
// 绝不碰用户真实数据目录。
//
// 为什么这些纯逻辑在 mac 侧第一次被钉住:Windows 的测试宿主是 net8,WPF 控件碰不得,
// `SettingsLayoutPage.cs` 的界面部分与 `SplitCatalog` 都在 WPF 文件里 ⇒ Windows 侧只有 `LayoutSplitTests`
// 的 16 例覆盖比例存取,方案 / JSON 编辑器 / 入口表 / 加块都没有等价用例。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Headless;
using TipCL.Core.Launcher.Layout;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 本包六件落地件的单元测试。<see cref="ShellTabs"/> / <see cref="SplitCatalog"/> /
/// <see cref="LayoutBlockEntryCatalog"/> / <see cref="LayoutProfiles"/> / <see cref="LayoutJsonEditor"/> /
/// <see cref="LayoutWidgetAdd"/>。
/// </summary>
public class R32MacMoreLayoutTests
{
	// ─────────────────────────────────────────────────────────── ShellTabs

	/// <summary>空文档 → 内置五键、出厂顺序(Windows `ShellTabs.OrderedKeys` 的第一条不变量)。</summary>
	[Fact]
	public void ShellTabs_empty_shell_falls_back_to_the_five_builtin_tabs()
	{
		using var home = new C3aHome();
		Assert.Equal(new List<string> { "launch", "library", "download", "plugins", "settings" }, ShellTabs.OrderedKeys(null));
		Assert.Equal(new List<string> { "launch", "library", "download", "plugins", "settings" }, ShellTabs.OrderedKeys(ShellTabs.CurrentShell()));
		Assert.Equal(5, ShellTabs.BuiltinKeys.Length);
	}

	/// <summary>用户顺序保留、未知 key 丢弃、重复只认第一次;`settings` 缺失时补到末尾。</summary>
	[Fact]
	public void ShellTabs_keeps_user_order_drops_unknown_and_duplicates_and_always_appends_settings()
	{
		using var home = new C3aHome();
		ShellLayout shell = new ShellLayout
		{
			Tabs = new List<string> { "download", "nope", "launch", "download", "library" }
		};
		Assert.Equal(new List<string> { "download", "launch", "library", "settings" }, ShellTabs.OrderedKeys(shell));
	}

	/// <summary>「设置」页签永远不可隐藏(否则没有入口改回来),其余四键可隐藏。</summary>
	[Fact]
	public void ShellTabs_settings_is_the_only_tab_that_cannot_be_hidden()
	{
		Assert.False(ShellTabs.CanHide("settings"));
		foreach (string key in ShellTabs.BuiltinKeys)
		{
			if (key != "settings")
			{
				Assert.True(ShellTabs.CanHide(key), key + " 应当可隐藏");
			}
		}
	}

	/// <summary>显隐写进 v2 文档的 shell.tabs;把 settings 设成不可见也无效(仍留在顺序里)。</summary>
	[Fact]
	public void ShellTabs_set_tab_visible_persists_and_cannot_drop_settings()
	{
		using var home = new C3aHome();
		ShellTabs.SetTabVisible("plugins", visible: false);
		home.ForgetCache();
		ShellLayout reloaded = ShellTabs.CurrentShell();
		Assert.False(ShellTabs.IsVisible(reloaded, "plugins"));
		Assert.True(ShellTabs.IsVisible(reloaded, "settings"));
		ShellTabs.SetTabVisible("settings", visible: false);
		home.ForgetCache();
		Assert.True(ShellTabs.IsVisible(ShellTabs.CurrentShell(), "settings"));
	}

	/// <summary>
	/// SetTabs 只按传入顺序落盘(Windows `ShellTabs.SetTabs` 原文:合法键 + 去重,不做排序);
	/// 但 `OrderedKeys` 的读侧不变量仍然成立 —— settings 只要在表里就留在原位置,缺了才补到末尾。
	/// </summary>
	[Fact]
	public void ShellTabs_set_tabs_keeps_the_given_order_and_ordered_keys_backfills_settings()
	{
		using var home = new C3aHome();
		ShellTabs.SetTabs(new string[] { "settings", "launch", "plugins" });
		home.ForgetCache();
		Assert.Equal(new List<string> { "settings", "launch", "plugins" }, ShellTabs.OrderedKeys(ShellTabs.CurrentShell()));
		// 写入侧过滤未知键与重复
		ShellTabs.SetTabs(new string[] { "launch", "nope", "launch", "download" });
		home.ForgetCache();
		Assert.Equal(new List<string> { "launch", "download", "settings" }, ShellTabs.OrderedKeys(ShellTabs.CurrentShell()));
	}

	/// <summary>标题栏四个开关各自独立落盘,AnyTitlebarVisible 之和随之变化。</summary>
	[Fact]
	public void ShellTabs_titlebar_switches_round_trip()
	{
		using var home = new C3aHome();
		ShellLayout shell = ShellTabs.SetTitlebar(bridge: false, ai: false, theme: false, account: false);
		Assert.False(ShellTabs.AnyTitlebarVisible(shell));
		shell = ShellTabs.SetTitlebar(theme: true);
		Assert.True(ShellTabs.ShowTheme(shell));
		Assert.False(ShellTabs.ShowBridge(shell));
		Assert.True(ShellTabs.AnyTitlebarVisible(shell));
		home.ForgetCache();
		Assert.True(ShellTabs.ShowTheme(ShellTabs.CurrentShell()));
	}

	/// <summary>页签 key ↔ 页面 id / 高亮 key 的映射(Windows `ShellTabs.HighlightKeyOf` 逐条)。</summary>
	[Fact]
	public void ShellTabs_page_and_highlight_mapping_matches_windows()
	{
		Assert.Equal("launch", ShellTabs.PageIdOf("launch"));
		Assert.Null(ShellTabs.PageIdOf("account"));
		Assert.Equal("launch", ShellTabs.HighlightKeyOf("launch"));
		Assert.Equal("download", ShellTabs.HighlightKeyOf("tasks"));
		Assert.Equal("settings", ShellTabs.HighlightKeyOf("account"));
		Assert.Null(ShellTabs.HighlightKeyOf("ai"));
		Assert.Null(ShellTabs.HighlightKeyOf("instance"));
		Assert.Equal("下载", ShellTabs.Label("download"));
	}

	// ─────────────────────────────────────────────────────────── SplitCatalog

	/// <summary>13 处现状表:序号 1..13 不重、PageId/SplitId 非空、每处都有说明。</summary>
	[Fact]
	public void SplitCatalog_has_thirteen_unique_sites_with_notes()
	{
		Assert.Equal(13, SplitCatalog.All.Length);
		Assert.Equal(Enumerable.Range(1, 13).ToList(), SplitCatalog.All.Select(s => s.Index).ToList());
		Assert.Equal(13, SplitCatalog.All.Select(s => s.PageId + "/" + s.SplitId).Distinct().Count());
		foreach (SplitSite site in SplitCatalog.All)
		{
			Assert.False(string.IsNullOrWhiteSpace(site.PageId), site.Index + " 缺 PageId");
			Assert.False(string.IsNullOrWhiteSpace(site.SplitId), site.Index + " 缺 SplitId");
			Assert.False(string.IsNullOrWhiteSpace(site.Note), site.Index + " 缺现状说明");
			Assert.Contains(site.PixelsSide, new[] { "left", "right" });
		}
	}

	/// <summary>左右两栏的几处:只有 TwoColumn 才插分隔条;其余形态一律 IsTwoColumn=false。</summary>
	[Fact]
	public void SplitCatalog_only_two_column_sites_are_draggable()
	{
		foreach (SplitSite site in SplitCatalog.All)
		{
			Assert.Equal(site.Kind == SplitKind.TwoColumn, site.IsTwoColumn);
		}
		// Windows 表格:13 处里 6 处是左右两栏(1/2/6/7/8/9),另有 1 处内嵌(Writable=false)不在 All 里
		Assert.Equal(6, SplitCatalog.TwoColumnCount());
		Assert.Equal(new[] { 1, 2, 6, 7, 8, 9 }, SplitCatalog.All.Where(s => s.IsTwoColumn).Select(s => s.Index).ToArray());
		Assert.False(SplitCatalog.Embedded.Writable);
		Assert.Equal("instance:settings", SplitCatalog.Embedded.PageId);
		Assert.Contains(SplitCatalog.All, s => s.Kind == SplitKind.GridCols);
		Assert.Contains(SplitCatalog.All, s => s.Kind == SplitKind.HeightClamp);
	}

	/// <summary>#1 实例库 / folders:1/3 星号、留白 18、左栏下限 300(Windows 表格逐字)。</summary>
	[Fact]
	public void SplitCatalog_library_site_matches_windows_numbers()
	{
		SplitSite site = SplitCatalog.All.First(s => s.Index == 1);
		Assert.Equal("library", site.PageId);
		Assert.Equal("folders", site.SplitId);
		Assert.Equal(SplitKind.TwoColumn, site.Kind);
		Assert.Equal(1.0 / 3.0, site.DefaultRatio, 6);
		Assert.Equal(0.0, site.DefaultPixels);
		Assert.Equal("left", site.PixelsSide);
		Assert.Equal(18.0, site.Gap);
		Assert.Equal(300.0, site.LeftMin);
		Assert.True(site.Writable);
	}

	/// <summary>#2 设置页 / nav:168px 固定左栏、留白 18(Windows 表格逐字)。</summary>
	[Fact]
	public void SplitCatalog_settings_site_matches_windows_numbers()
	{
		SplitSite site = SplitCatalog.All.First(s => s.Index == 2);
		Assert.Equal("settings", site.PageId);
		Assert.Equal("nav", site.SplitId);
		Assert.Equal(168.0, site.DefaultPixels);
		Assert.Equal("left", site.PixelsSide);
		Assert.Equal(18.0, site.Gap);
	}

	// ─────────────────────────────────────────────── LayoutBlockEntryCatalog

	/// <summary>入口表 14 条(设置页内 7 + 顶栏 5 + 实例库 / 插件),pageId 不重。</summary>
	[Fact]
	public void BlockEntryCatalog_has_fourteen_unique_entries()
	{
		Assert.Equal(14, LayoutBlockEntryCatalog.All.Length);
		Assert.Equal(14, LayoutBlockEntryCatalog.All.Select(e => e.PageId).Distinct().Count());
		Assert.All(LayoutBlockEntryCatalog.All, e => Assert.False(string.IsNullOrWhiteSpace(e.Label)));
		// [layout-fix] 规格 §4-能力3 新增的两条必须在表里(本包补齐的宿主)
		Assert.NotNull(LayoutBlockEntryCatalog.Find("library:main"));
		Assert.NotNull(LayoutBlockEntryCatalog.Find("plugins"));
	}

	/// <summary>单块页 = 下载与网络 / 反馈与关于(Windows 注释逐字:D1 放宽到 specs ≥ 1 后它们也能进编辑态)。</summary>
	[Fact]
	public void BlockEntryCatalog_single_block_pages_match_windows()
	{
		Assert.Equal(new List<string> { "settings:network", "settings:about" }, LayoutBlockEntryCatalog.SingleBlockPageIds());
	}

	/// <summary>Find 对空 / 未知 pageId 返回 null(不抛)。</summary>
	[Fact]
	public void BlockEntryCatalog_find_is_null_safe()
	{
		Assert.Null(LayoutBlockEntryCatalog.Find(null));
		Assert.Null(LayoutBlockEntryCatalog.Find(""));
		Assert.Null(LayoutBlockEntryCatalog.Find("nope"));
		Assert.Equal("settings:general", LayoutBlockEntryCatalog.Find("settings:general")!.PageId);
	}

	/// <summary>设置页内的入口必须带 SettingsView(≥0),顶栏页的入口必须是 -1(文档顺序与语义一致)。</summary>
	[Fact]
	public void BlockEntryCatalog_settings_views_are_only_on_settings_entries()
	{
		foreach (LayoutBlockEntryCatalog.Entry entry in LayoutBlockEntryCatalog.All)
		{
			if (entry.NavId == "settings")
			{
				Assert.True(entry.SettingsView >= 0, entry.PageId + " 在设置页里却没给视图下标");
			}
			else
			{
				Assert.Equal(-1, entry.SettingsView);
			}
		}
	}

	// ─────────────────────────────────────────────────────────── LayoutProfiles

	/// <summary>新建方案:以当前布局为基线,ActiveName 立刻指向它,重启后仍在(落盘)。</summary>
	[Fact]
	public void Profiles_create_from_current_round_trips()
	{
		using var home = new C3aHome();
		LayoutV2 doc = LayoutStoreV2.Load();
		doc.Layout.Shell.Density = LayoutSchema.DensityLoose;
		LayoutStoreV2.Save(doc);

		Assert.Equal("", LayoutProfiles.Create("我的布局", fromCurrent: true));
		Assert.Equal("我的布局", LayoutProfiles.ActiveName());
		Assert.Equal(new List<string> { "我的布局" }, LayoutProfiles.Names());
		Assert.Equal(LayoutSchema.DensityLoose, LayoutProfiles.Snapshot("我的布局")!.Shell.Density);

		home.ForgetCache();
		Assert.Equal(new List<string> { "我的布局" }, LayoutProfiles.Names());
	}

	/// <summary>方案名校验:空 / 超长(>40)/ 重名都拒绝;与自身同名视为合法(重命名用)。</summary>
	[Fact]
	public void Profiles_validate_name_rejects_empty_too_long_and_duplicates()
	{
		using var home = new C3aHome();
		Assert.NotEqual("", LayoutProfiles.ValidateName(null, null));
		Assert.NotEqual("", LayoutProfiles.ValidateName("   ", null));
		Assert.NotEqual("", LayoutProfiles.ValidateName(new string('x', LayoutProfiles.ProfileNameMax + 1), null));
		Assert.Equal("", LayoutProfiles.ValidateName(new string('x', LayoutProfiles.ProfileNameMax), null));
		Assert.Equal("", LayoutProfiles.Create("甲", fromCurrent: false));
		Assert.NotEqual("", LayoutProfiles.ValidateName("甲", null));
		Assert.Equal("", LayoutProfiles.ValidateName("甲", "甲"));
	}

	/// <summary>应用方案 = 原子替换整个 layout 并把旧布局留底成「上一次布局」(kept 非空)。</summary>
	[Fact]
	public void Profiles_apply_replaces_layout_atomically_and_keeps_previous()
	{
		using var home = new C3aHome();
		LayoutV2 doc = LayoutStoreV2.Load();
		doc.Layout.Shell.Density = LayoutSchema.DensityCompact;
		LayoutStoreV2.Save(doc);
		Assert.Equal("", LayoutProfiles.Create("紧凑", fromCurrent: true));

		// 手改一次(直接编辑态),再应用「紧凑」:当前布局应当被留底
		doc = LayoutStoreV2.Load();
		doc.Layout.Shell.Density = LayoutSchema.DensityLoose;
		LayoutStoreV2.Save(doc);

		string error = LayoutProfiles.Apply("紧凑", out string kept);
		Assert.Equal("", error);
		Assert.Equal(LayoutProfiles.PreviousName, kept);
		Assert.Equal(LayoutSchema.DensityCompact, LayoutStoreV2.Load().Layout.Shell.Density);
		Assert.Equal(LayoutSchema.DensityLoose, LayoutProfiles.Snapshot(kept)!.Shell.Density);
		Assert.Equal("紧凑", LayoutProfiles.ActiveName());
	}

	/// <summary>应用不存在的方案 = 中文错误,布局一字不动。</summary>
	[Fact]
	public void Profiles_apply_missing_name_returns_error_and_touches_nothing()
	{
		using var home = new C3aHome();
		LayoutV2 doc = LayoutStoreV2.Load();
		doc.Layout.Shell.Density = LayoutSchema.DensityLoose;
		LayoutStoreV2.Save(doc);
		string error = LayoutProfiles.Apply("不存在", out string kept);
		Assert.NotEqual("", error);
		Assert.Equal("", kept);
		Assert.Equal(LayoutSchema.DensityLoose, LayoutStoreV2.Load().Layout.Shell.Density);
	}

	/// <summary>重命名保序、跟着改 active_profile;复制一份内容相同;删除后 active 回到直接编辑态。</summary>
	[Fact]
	public void Profiles_rename_duplicate_delete_behave_like_windows()
	{
		using var home = new C3aHome();
		Assert.Equal("", LayoutProfiles.Create("甲", fromCurrent: true));
		Assert.Equal("", LayoutProfiles.Create("乙", fromCurrent: false));
		Assert.Equal(new List<string> { "甲", "乙" }, LayoutProfiles.Names());

		// 注意:Windows 的 Create 会把 ActiveProfile 记成新方案名 ⇒ 第二次 Create 之后激活的是「乙」;
		// 重命名非激活方案不该改 active_profile(Windows `LayoutProfiles.Rename` 只在 == oldName 时跟改)。
		Assert.Equal("", LayoutProfiles.Rename("甲", "丙"));
		Assert.Equal(new List<string> { "丙", "乙" }, LayoutProfiles.Names());
		Assert.Equal("乙", LayoutProfiles.ActiveName());

		Assert.Equal("", LayoutProfiles.Duplicate("丙", "丁"));
		Assert.Equal(new List<string> { "丙", "乙", "丁" }, LayoutProfiles.Names());

		Assert.Equal("", LayoutProfiles.Delete("丙"));
		Assert.Equal("乙", LayoutProfiles.ActiveName());
		Assert.Equal(new List<string> { "乙", "丁" }, LayoutProfiles.Names());
		Assert.NotEqual("", LayoutProfiles.Delete("丙"));
		// 删掉当前激活方案 ⇒ active_profile 回到直接编辑态
		Assert.Equal("", LayoutProfiles.Delete("乙"));
		Assert.Equal("", LayoutProfiles.ActiveName());
		Assert.Equal(new List<string> { "丁" }, LayoutProfiles.Names());
	}

	/// <summary>把 v1 页面布局并进 v2 文档的 pages:按 pageId 覆盖,其余页不动,返回并入页数。</summary>
	[Fact]
	public void Profiles_merge_v1_pages_overwrites_only_listed_pages()
	{
		using var home = new C3aHome();
		LayoutV2 doc = LayoutStoreV2.Load();
		doc.Layout.Pages["launch"] = new PageLayoutV2
		{
			Blocks = new List<BlockLayout> { new BlockLayout { Id = "news" } }
		};
		Dictionary<string, PageLayoutDoc> pages = new Dictionary<string, PageLayoutDoc>
		{
			["tasks"] = new PageLayoutDoc
			{
				Blocks = new List<BlockState>
				{
					new BlockState { Id = "queue" },
					new BlockState { Id = "history", Hidden = true }
				}
			}
		};
		int merged = LayoutProfiles.MergeV1Pages(doc, pages);
		Assert.Equal(1, merged);
		Assert.Equal(new List<string> { "news" }, doc.Layout.Pages["launch"].Blocks.Select(b => b.Id).ToList());
		Assert.Equal(new List<string> { "queue", "history" }, doc.Layout.Pages["tasks"].Blocks.Select(b => b.Id).ToList());
		Assert.True(doc.Layout.Pages["tasks"].Blocks[1].Hidden);
	}

	// ─────────────────────────────────────────────────────────── LayoutJsonEditor

	/// <summary>Text(当前文档)就是可再次解析的整机方案:kind/version 门禁字段齐全。</summary>
	[Fact]
	public void JsonEditor_text_is_a_parseable_envelope()
	{
		using var home = new C3aHome();
		string text = LayoutJsonEditor.Text(LayoutStoreV2.Load());
		using JsonDocument parsed = JsonDocument.Parse(text);
		Assert.Equal(LayoutSchema.Kind, parsed.RootElement.GetProperty("kind").GetString());
		Assert.Equal(LayoutSchema.CurrentVersion, parsed.RootElement.GetProperty("version").GetInt32());
	}

	/// <summary>空文本 / 非法 JSON / 非对象 / kind 错 / version 错 = 硬错误(不落盘)。</summary>
	[Fact]
	public void JsonEditor_inspect_rejects_hard_errors()
	{
		Assert.Contains(LayoutJsonEditor.Inspect("", out _), i => i.Error);
		Assert.Contains(LayoutJsonEditor.Inspect("   ", out _), i => i.Error);
		Assert.Contains(LayoutJsonEditor.Inspect("{ oops", out _), i => i.Error);
		Assert.Contains(LayoutJsonEditor.Inspect("[]", out _), i => i.Error);
		Assert.Contains(LayoutJsonEditor.Inspect("{\"kind\":\"x\",\"version\":2,\"layout\":{}}", out _), i => i.Error);
		Assert.Contains(LayoutJsonEditor.Inspect("{\"kind\":\"" + LayoutSchema.Kind + "\",\"layout\":{}}", out _), i => i.Error);
		Assert.Contains(LayoutJsonEditor.Inspect("{\"kind\":\"" + LayoutSchema.Kind + "\",\"version\":99,\"layout\":{}}", out _), i => i.Error);
	}

	/// <summary>未知顶层字段 = 警告(保留但不生效);合法文档 0 错误。</summary>
	[Fact]
	public void JsonEditor_inspect_warns_on_unknown_top_keys_but_does_not_fail()
	{
		using var home = new C3aHome();
		string text = LayoutJsonEditor.Text(LayoutStoreV2.Load());
		string withUnknown = text.TrimEnd().TrimEnd('}') + ",\"future_field\":1}";
		List<LayoutJsonIssue> issues = LayoutJsonEditor.Inspect(withUnknown, out LayoutV2? doc);
		Assert.NotNull(doc);
		Assert.False(issues.Any(i => i.Error), "未知顶层字段不该是硬错误:" + string.Join("|", issues.Select(i => i.Path + ":" + i.Message)));
		Assert.Contains(issues, i => i.Path == "future_field");
	}

	/// <summary>越界数值 / 未知 mode / 未知 style / 重复块 id = 警告(按契约钳位或忽略),不是硬错误。</summary>
	[Fact]
	public void JsonEditor_inspect_warns_on_out_of_contract_values()
	{
		string body = "{\"shell\":{\"density\":\"weird\"},\"pages\":{\"launch\":{" +
			"\"mode\":\"diagonal\",\"cols\":7,\"gap\":999," +
			"\"blocks\":[{\"id\":\"news\",\"style\":\"glow\"},{\"id\":\"news\"}]}}}";
		string text = "{\"kind\":\"" + LayoutSchema.Kind + "\",\"version\":" + LayoutSchema.CurrentVersion + ",\"layout\":" + body + "}";
		List<LayoutJsonIssue> issues = LayoutJsonEditor.Inspect(text, out LayoutV2? doc);
		Assert.NotNull(doc);
		Assert.False(issues.Any(i => i.Error), "这些都是警告:" + string.Join("|", issues.Select(i => i.Path + ":" + i.Message)));
		Assert.Contains(issues, i => i.Path == "layout.shell.density");
		Assert.Contains(issues, i => i.Path == "layout.pages[\"launch\"].mode");
		Assert.Contains(issues, i => i.Path == "layout.pages[\"launch\"].cols");
		Assert.Contains(issues, i => i.Path == "layout.pages[\"launch\"].gap");
		Assert.Contains(issues, i => i.Path == "layout.pages[\"launch\"].blocks[0].style");
		Assert.Contains(issues, i => i.Path == "layout.pages[\"launch\"].blocks[1].id");
	}

	/// <summary>Apply(合法文本)= 真落盘;Apply(硬错误)= 不落盘(旧文档逐字节还在)。</summary>
	[Fact]
	public void JsonEditor_apply_persists_only_when_valid()
	{
		using var home = new C3aHome();
		string good = "{\"kind\":\"" + LayoutSchema.Kind + "\",\"version\":" + LayoutSchema.CurrentVersion +
			",\"layout\":{\"shell\":{\"density\":\"" + LayoutSchema.DensityLoose + "\"}}}";
		List<LayoutJsonIssue> issues = LayoutJsonEditor.Apply(good, out LayoutV2? doc);
		Assert.False(issues.Any(i => i.Error));
		Assert.NotNull(doc);
		home.ForgetCache();
		Assert.Equal(LayoutSchema.DensityLoose, LayoutStoreV2.Load().Layout.Shell.Density);

		// 硬错误:kind 不对 ⇒ 不落盘,磁盘上的还是上一份
		byte[] before = File.ReadAllBytes(LayoutStoreV2.FilePath);
		List<LayoutJsonIssue> bad = LayoutJsonEditor.Apply("{\"kind\":\"nope\",\"version\":2,\"layout\":{}}", out _);
		Assert.True(bad.Any(i => i.Error));
		Assert.Equal(before, File.ReadAllBytes(LayoutStoreV2.FilePath));
	}

	// ─────────────────────────────────────────────── LayoutWidgetAdd(加块)

	/// <summary>可加的五类 = WidgetRegistry.SafeTypes;web / button(危险类型)不在其中。</summary>
	[Fact]
	public void WidgetAdd_exposes_exactly_the_five_safe_types()
	{
		Assert.Equal(5, LayoutWidgetAdd.AddableTypes.Length);
		Assert.Equal(WidgetRegistry.SafeTypes.ToList(), LayoutWidgetAdd.AddableTypes.ToList());
		Assert.DoesNotContain(LayoutSchema.WidgetWeb, LayoutWidgetAdd.AddableTypes);
		Assert.DoesNotContain(LayoutSchema.WidgetButton, LayoutWidgetAdd.AddableTypes);
		foreach (string type in LayoutWidgetAdd.AddableTypes)
		{
			Assert.True(LayoutWidgetAdd.CanAdd(type), type + " 应当可加");
			Assert.False(string.IsNullOrWhiteSpace(LayoutWidgetAdd.TypeLabel(type)));
		}
		Assert.Contains("便签", LayoutWidgetAdd.TypeListText());
		Assert.Contains("游戏时长", LayoutWidgetAdd.TypeListText());
	}

	/// <summary>危险类型 / 未知类型 / 空类型一律拒绝,且拒绝时**不动文档**。</summary>
	[Fact]
	public void WidgetAdd_rejects_dangerous_unknown_and_empty_types()
	{
		using var home = new C3aHome();
		LayoutV2 doc = LayoutStoreV2.Load();
		int before = doc.Layout.Widgets.Count;
		Assert.NotEqual("", LayoutWidgetAdd.AddWidget(doc, LayoutSchema.WidgetWeb, out WidgetDef? w1));
		Assert.Null(w1);
		Assert.NotEqual("", LayoutWidgetAdd.AddWidget(doc, LayoutSchema.WidgetButton, out _));
		Assert.NotEqual("", LayoutWidgetAdd.AddWidget(doc, "teleporter", out _));
		Assert.NotEqual("", LayoutWidgetAdd.AddWidget(doc, null, out _));
		Assert.NotEqual("", LayoutWidgetAdd.AddWidget(null, LayoutSchema.WidgetNote, out _));
		Assert.Equal(before, doc.Layout.Widgets.Count);
		Assert.False(LayoutWidgetAdd.CanAdd(LayoutSchema.WidgetWeb));
		Assert.False(LayoutWidgetAdd.CanAdd(""));
	}

	/// <summary>连续加两次:两条 id 不同(同一毫秒也不撞),标题按序号与类型生成。</summary>
	[Fact]
	public void WidgetAdd_generates_unique_ids_and_readable_titles()
	{
		using var home = new C3aHome();
		LayoutV2 doc = LayoutStoreV2.Load();
		Assert.Equal("", LayoutWidgetAdd.AddWidget(doc, LayoutSchema.WidgetNote, out WidgetDef? first));
		Assert.Equal("", LayoutWidgetAdd.AddWidget(doc, LayoutSchema.WidgetClock, out WidgetDef? second));
		Assert.NotNull(first);
		Assert.NotNull(second);
		Assert.NotEqual(first!.Id, second!.Id);
		Assert.Equal(2, LayoutWidgetAdd.Count(doc));
		Assert.Contains(LayoutWidgetAdd.TypeLabel(LayoutSchema.WidgetNote), first.Title);
		Assert.Equal("HH:mm", second.Settings["format"]);
		Assert.Equal("custom:" + first.Id, LayoutWidgetAdd.BlockIdOf(first.Id));
	}

	/// <summary>stack 页追加在页尾、Span 1、Z 递增,且幂等(同 id 不插第二条)。</summary>
	[Fact]
	public void WidgetAdd_appends_to_the_end_of_a_stack_page_once()
	{
		PageLayoutV2 page = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeStack,
			Blocks = new List<BlockLayout>
			{
				new BlockLayout { Id = "appearance" },
				new BlockLayout { Id = "wallpaper", Z = 5 }
			}
		};
		Assert.Equal("", LayoutWidgetAdd.AppendBlock(page, "w-1"));
		Assert.Equal(new List<string> { "appearance", "wallpaper", "custom:w-1" }, page.Blocks.Select(b => b.Id).ToList());
		Assert.Equal(1, page.Blocks[2].Span);
		Assert.Equal(2, page.Blocks[2].Z);
		Assert.Equal("", LayoutWidgetAdd.AppendBlock(page, "w-1"));
		Assert.Equal(3, page.Blocks.Count);
		Assert.True(LayoutWidgetAdd.HasBlock(page, "custom:w-1"));
	}

	/// <summary>grid 页放到所有块下面一条新行、横跨整行(不挤用户已经摆好的位置)。</summary>
	[Fact]
	public void WidgetAdd_places_a_new_block_below_a_grid_page()
	{
		PageLayoutV2 page = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeGrid,
			Cols = 12,
			Blocks = new List<BlockLayout>
			{
				new BlockLayout { Id = "folders", X = 0, Y = 0, W = 4, H = 1 },
				new BlockLayout { Id = "versions", X = 4, Y = 0, W = 8, H = 2 }
			}
		};
		Assert.Equal("", LayoutWidgetAdd.AppendBlock(page, "w-2"));
		BlockLayout added = page.Blocks[2];
		Assert.Equal(0.0, added.X);
		Assert.Equal(2.0, added.Y);
		Assert.Equal(12.0, added.W);
		Assert.Equal(1.0, added.H);
	}

	/// <summary>
	/// 端到端(纯数据层):加块 → 写进页 → Save → 丢缓存重读,定义与引用都还在,
	/// 且 <see cref="LayoutEngine.LoadState"/> 认它是 implicit ref(不会被当成"未知内置块"丢掉)。
	/// </summary>
	[Fact]
	public void WidgetAdd_end_to_end_survives_save_and_reload()
	{
		using var home = new C3aHome();
		LayoutV2 doc = LayoutStoreV2.Load();
		Assert.Equal("", LayoutWidgetAdd.AddWidget(doc, LayoutSchema.WidgetNote, out WidgetDef? def));
		PageLayoutV2 page = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeStack,
			Blocks = new List<BlockLayout> { new BlockLayout { Id = "appearance" } }
		};
		Assert.Equal("", LayoutWidgetAdd.AppendBlock(page, def!.Id));
		doc.Layout.Pages["settings:general"] = page;
		Assert.True(LayoutStoreV2.Save(doc));

		home.ForgetCache();
		LayoutV2 reloaded = LayoutStoreV2.Load();
		WidgetDef? back = reloaded.Layout.Widgets.FirstOrDefault(w => w.Id == def.Id);
		Assert.NotNull(back);
		Assert.Equal(LayoutSchema.WidgetNote, back!.Type);
		List<string> ids = reloaded.Layout.Pages["settings:general"].Blocks.Select(b => b.Id).ToList();
		Assert.Contains("custom:" + def.Id, ids);

		// 渲染侧的真源判定:编辑器装载这一页时必须保留这条 custom: 引用
		List<LayoutBlockRef> refs = new List<LayoutBlockRef> { new LayoutBlockRef { Id = "appearance" } };
		LayoutPageState state = LayoutEngine.LoadState("settings:general", reloaded.Layout.Pages["settings:general"], refs);
		Assert.Contains("custom:" + def.Id, state.Order);
		Assert.True(LayoutEngine.IsImplicitRef("custom:" + def.Id));
		Assert.Equal(LayoutContentKind.Custom, LayoutEngine.Classify("custom:" + def.Id));
	}

	/// <summary>落盘文件里就是 ui_layout_v2.json,且 widgets/pages 两处都能在原文里读到(证据可复查)。</summary>
	[Fact]
	public void WidgetAdd_writes_the_expected_json_file()
	{
		using var home = new C3aHome();
		LayoutV2 doc = LayoutStoreV2.Load();
		Assert.Equal("", LayoutWidgetAdd.AddWidget(doc, LayoutSchema.WidgetClock, out WidgetDef? def));
		doc.Layout.Pages["launch"] = new PageLayoutV2
		{
			Blocks = new List<BlockLayout> { new BlockLayout { Id = "custom:" + def!.Id } }
		};
		Assert.True(LayoutStoreV2.Save(doc));
		Assert.Equal("ui_layout_v2.json", Path.GetFileName(LayoutStoreV2.FilePath));
		string raw = File.ReadAllText(LayoutStoreV2.FilePath);
		using JsonDocument parsed = JsonDocument.Parse(raw);
		JsonElement widgets = parsed.RootElement.GetProperty("layout").GetProperty("widgets");
		Assert.Contains(widgets.EnumerateArray(), w => w.GetProperty("id").GetString() == def.Id);
		JsonElement blocks = parsed.RootElement.GetProperty("layout").GetProperty("pages").GetProperty("launch").GetProperty("blocks");
		Assert.Contains(blocks.EnumerateArray(), b => b.GetProperty("id").GetString() == "custom:" + def.Id);
	}
}