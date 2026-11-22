using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TipCL.Pages;
using TipCL.Pages.Layout;
using Xunit;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Tests;

/// <summary>
/// R34 布局 v3 · 设置 → 布局(WP-L4)的回归闸门:
///   界面上没有内部说法     → LayoutPageLiteralsCarryNoInternalWording(扫描 SettingsLayoutPage.cs 全部 I18n.L 字面量)
///   入口表 18 / 卡片 15      → CatalogHasEighteenHostsAndFifteenCards / FeaturedCardsComeFirst
///   「已自定义」= 文档有条目 → CustomizedFollowsStoreEntries / ResetRemovesOnlyThatPage
///   摘要 / 缩略图            → SummaryDescribesModeWidgetsAndHidden / Thumb*
///   全部恢复默认会先留底     → ResetAllPagesKeepsPreviousAndLeavesTheRest
///   分栏一行                 → SplitSummaryListsOnlyStoredTwoColumnSplits
///   方案卡片 / AI 备份       → ProfileCardsMarkActiveAndAiBackups / AiBackupNamesAreRecognisedByPrefix / AiBackupPrefixMatchesCore
///   网页 / 按钮全局开关      → DangerWidgetsSwitchDefaultsOff
///   视图结构(源码事实)     → LayoutViewKeepsLegacyBlockIdsAndCollapsesAdvanced / TitlebarRowDropsBridgeButKeepsTheData
/// 落盘一律走 DirOverride 临时目录(同时给 PageLayoutStore 注入同一目录),绝不碰用户真实数据目录;只用纯逻辑类型。
/// </summary>
public class SettingsLayoutV3Tests
{
	private readonly string _tmp = Path.Combine(Path.GetTempPath(), "tipcl_layout_v3_tests_" + Guid.NewGuid().ToString("N"));

	private void UseDir()
	{
		Directory.CreateDirectory(_tmp);
		LayoutStoreV2.DirOverride = _tmp;
		LayoutStoreV2.V1Bridge = new PagesLayoutV1Bridge();
		PageLayoutStore.DirOverride = _tmp;
		LayoutStoreV2.ResetCache();
	}

	private void Release()
	{
		LayoutStoreV2.DirOverride = null;
		LayoutStoreV2.V1Bridge = null;
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

	// ————— 1) 界面上不出现内部说法 —————

	private static readonly Regex[] Forbidden = new Regex[]
	{
		new Regex("ui_layout_v2", RegexOptions.IgnoreCase),
		new Regex("ui_page_layouts", RegexOptions.IgnoreCase),
		new Regex("桥接"),
		new Regex("(?<![A-Za-z])L[0-9](?![0-9])"),
		new Regex("WP-", RegexOptions.IgnoreCase),
		new Regex("#[0-9]"),
		new Regex("标题栏[:：]\\s*\\{0\\}"),
		new Regex("custom:|plugin:"),
		new Regex("w_note")
	};

	private static void AssertClean(string? text, string where)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		foreach (Regex rule in Forbidden)
		{
			Assert.False(rule.IsMatch(text), where + " 含内部说法(" + rule + "):" + text);
		}
	}

	private static List<string> I18nLiterals(string source)
	{
		return Regex.Matches(source, "I18n\\.L\\(\\s*\"((?:[^\"\\\\]|\\\\.)*)\"").Cast<Match>().Select((Match m) => Regex.Unescape(m.Groups[1].Value)).ToList();
	}

	[Fact]
	public void LayoutPageLiteralsCarryNoInternalWording()
	{
		string src = Src("src/TipCL/Pages/SettingsLayoutPage.cs");
		List<string> literals = I18nLiterals(src);
		// 粗量级护栏:正则失灵(一条都没抓到)时不能静默通过
		Assert.True(literals.Count > 150, "只抓到 " + literals.Count + " 个 I18n.L 字面量");
		foreach (string text in literals)
		{
			AssertClean(text, "SettingsLayoutPage.cs");
		}
		// 入口表的文案与提示同样会显示给用户
		foreach (LayoutBlockEntryCatalog.Entry entry in LayoutBlockEntryCatalog.All)
		{
			AssertClean(entry.Label, entry.PageId + ".Label");
			AssertClean(entry.Hint, entry.PageId + ".Hint");
		}
		// 新建的布局编辑窗口也一并扫描
		foreach (string text in I18nLiterals(Src("src/TipCL/Pages/Layout/LayoutJsonWindow.cs")))
		{
			AssertClean(text, "LayoutJsonWindow.cs");
		}
		// 页面顶部的说明换成任务书给的那一句
		Assert.Contains("调整导航、各页面的排列，或者保存成方案。改动即时生效，随时可以恢复默认。", literals);
	}

	[Fact]
	public void ForbiddenRulesActuallyCatchTheOldWording()
	{
		// 反向自证:R33 的那些说法确实会被上面的规则抓到(防止规则写错后「永远通过」)
		string[] old = new string[] { "这里的改动即时写进 ui_layout_v2.json", "标题栏:{0}", "桥接", "网页 / 按钮默认关闭,需显式开启(L6)", "#10  plugins / cols", "w_note1" };
		foreach (string text in old)
		{
			Assert.Contains(Forbidden, (Regex rule) => rule.IsMatch(text));
		}
		// 正常文案不误伤
		foreach (string text in new string[] { "隐藏 AI 按钮后仍可按 Ctrl+K 打开", "布局 (*.json)|*.json|全部文件|*.*", "时间格式(留空默认 HH:mm)" })
		{
			Assert.DoesNotContain(Forbidden, (Regex rule) => rule.IsMatch(text));
		}
	}

	// ————— 2) 入口表 18 项、页面卡片 15 张 —————

	[Fact]
	public void CatalogHasEighteenHostsAndFifteenCards()
	{
		Assert.Equal(18, LayoutBlockEntryCatalog.All.Length);
		LayoutBlockEntryCatalog.Entry? library = LayoutBlockEntryCatalog.Find("library:main");
		Assert.NotNull(library);
		Assert.Equal("library", library!.NavId);
		Assert.Equal(-1, library.SettingsView);
		Assert.False(library.SingleBlock);
		Assert.Equal("", library.Hint);

		List<LayoutBlockEntryCatalog.Entry> cards = LayoutPageCards.Cards();
		Assert.Equal(15, cards.Count);
		Assert.Equal(15, cards.Select((LayoutBlockEntryCatalog.Entry e) => e.PageId).Distinct().Count());
		Assert.DoesNotContain(cards, (LayoutBlockEntryCatalog.Entry e) => e.SingleBlock);
		Assert.DoesNotContain(cards, (LayoutBlockEntryCatalog.Entry e) => e.PageId == "settings:layout");
		Assert.DoesNotContain(cards, (LayoutBlockEntryCatalog.Entry e) => e.PageId == "settings:network" || e.PageId == "settings:about");
		Assert.Contains(cards, (LayoutBlockEntryCatalog.Entry e) => e.PageId == "library:main");
		// 卡片 = 入口表减去 2 个单块页与「布局」页自己,一张不多一张不少
		List<string> expected = LayoutBlockEntryCatalog.All.Where((LayoutBlockEntryCatalog.Entry e) => !e.SingleBlock && e.PageId != "settings:layout").Select((LayoutBlockEntryCatalog.Entry e) => e.PageId).OrderBy((string s) => s, StringComparer.Ordinal).ToList();
		Assert.Equal(expected, cards.Select((LayoutBlockEntryCatalog.Entry e) => e.PageId).OrderBy((string s) => s, StringComparer.Ordinal).ToList());
		// 卡片下方那行说明点名两个单块页
		string note = LayoutPageCards.SingleBlockNote();
		Assert.Contains(I18n.L("下载与网络"), note);
		Assert.Contains(I18n.L("反馈与关于"), note);
	}

	[Fact]
	public void FeaturedCardsComeFirst()
	{
		List<string> all = LayoutPageCards.Cards().Select((LayoutBlockEntryCatalog.Entry e) => e.PageId).ToList();
		Assert.Equal(new string[6] { "launch", "download", "library:main", "multiplayer", "account", "ai" }, all.Take(6).ToArray());
		Assert.Equal(6, LayoutPageCards.Shown(false).Count);
		Assert.Equal(all, LayoutPageCards.Shown(true).Select((LayoutBlockEntryCatalog.Entry e) => e.PageId).ToList());
	}

	// ————— 3) 「已自定义」= LayoutStoreV2 里有这一页的条目 —————

	[Fact]
	public void CustomizedFollowsStoreEntries()
	{
		UseDir();
		try
		{
			LayoutV2 fresh = LayoutStoreV2.Load();
			foreach (LayoutBlockEntryCatalog.Entry entry in LayoutPageCards.Cards())
			{
				Assert.False(LayoutPageCards.IsCustomized(fresh, entry.PageId), entry.PageId);
			}
			Assert.Equal(0, LayoutPageCards.CustomizedCount(fresh));

			// 主 body 里有 download、窄窗变体里有 ai
			LayoutV2 doc = LayoutStoreV2.Load();
			doc.Layout.Pages["download"] = new PageLayoutV2();
			doc.Layout.Pages["download"].Blocks.Add(new BlockLayout { Id = "list" });
			LayoutBody narrow = new LayoutBody();
			narrow.Pages["ai"] = new PageLayoutV2();
			doc.Layout.Variants["narrow"] = narrow;
			Assert.True(LayoutStoreV2.Save(doc));

			LayoutStoreV2.ResetCache();
			LayoutV2 after = LayoutStoreV2.Load();
			foreach (LayoutBlockEntryCatalog.Entry entry in LayoutBlockEntryCatalog.All)
			{
				bool inDoc = after.Layout.Pages.ContainsKey(entry.PageId) || after.Layout.Variants.Values.Any((LayoutBody b) => b != null && b.Pages.ContainsKey(entry.PageId));
				Assert.Equal(inDoc, LayoutPageCards.IsCustomized(after, entry.PageId));
			}
			Assert.True(LayoutPageCards.IsCustomized(after, "download"));
			Assert.True(LayoutPageCards.IsCustomized(after, "ai"));
			Assert.False(LayoutPageCards.IsCustomized(after, "launch"));
			Assert.Equal(2, LayoutPageCards.CustomizedCount(after));
			Assert.NotNull(LayoutPageCards.PageOf(after, "download"));
			Assert.Null(LayoutPageCards.PageOf(after, "launch"));
			Assert.False(LayoutPageCards.IsCustomized(after, null));
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void ResetRemovesOnlyThatPage()
	{
		UseDir();
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			doc.Layout.Pages["download"] = new PageLayoutV2();
			doc.Layout.Pages["account"] = new PageLayoutV2();
			LayoutBody narrow = new LayoutBody();
			narrow.Pages["download"] = new PageLayoutV2();
			doc.Layout.Variants["narrow"] = narrow;
			doc.Layout.Widgets.Add(new WidgetDef { Id = "w1", Type = LayoutSchema.WidgetDivider, Title = "分隔线" });
			Assert.True(LayoutStoreV2.Save(doc));

			LayoutV2 work = LayoutStoreV2.Load();
			// 主 body + 变体各删一份
			Assert.Equal(2, LayoutPageCards.Reset(work, "download"));
			Assert.Equal(0, LayoutPageCards.Reset(work, "download"));
			Assert.Equal(0, LayoutPageCards.Reset(work, null));
			Assert.True(LayoutStoreV2.Save(work));
			LayoutStoreV2.ResetCache();
			LayoutV2 after = LayoutStoreV2.Load();
			Assert.False(LayoutPageCards.IsCustomized(after, "download"));
			Assert.True(LayoutPageCards.IsCustomized(after, "account"));
			// 组件定义不受影响
			Assert.Single(after.Layout.Widgets);
		}
		finally
		{
			Release();
		}
	}

	// ————— 4) 摘要 —————

	[Fact]
	public void SummaryDescribesModeWidgetsAndHidden()
	{
		PageLayoutV2 grid = new PageLayoutV2 { Mode = LayoutSchema.ModeGrid, Cols = 12 };
		grid.Blocks.Add(new BlockLayout { Id = "filters", Hidden = true });
		grid.Blocks.Add(new BlockLayout { Id = "list" });
		grid.Blocks.Add(new BlockLayout { Id = "custom:w1" });
		grid.Blocks.Add(new BlockLayout { Id = "custom:w2", Hidden = true });
		Assert.Equal(string.Join(" · ", I18n.L("网格"), I18n.L("{0} 个组件", 1), I18n.L("隐藏 {0} 项", 1)), LayoutPageCards.Summary(grid));

		PageLayoutV2 stack = new PageLayoutV2();
		stack.Blocks.Add(new BlockLayout { Id = "b" });
		stack.Blocks.Add(new BlockLayout { Id = "a" });
		Assert.Equal(I18n.L("调整了顺序"), LayoutPageCards.Summary(stack));
		Assert.Equal(I18n.L("和出厂时一样"), LayoutPageCards.Summary(null));

		// 自由画布在界面上已并入网格:摘要也写「网格」
		PageLayoutV2 canvas = new PageLayoutV2 { Mode = LayoutSchema.ModeCanvas };
		canvas.Blocks.Add(new BlockLayout { Id = "a" });
		Assert.Equal(I18n.L("网格"), LayoutPageCards.Summary(canvas));
	}

	// ————— 5) 缩略图 —————

	[Fact]
	public void ThumbFallsBackToPlaceholderWithoutLayout()
	{
		LayoutThumb thumb = LayoutPageCards.ThumbOf("download", null);
		Assert.True(thumb.Placeholder);
		Assert.Equal(3, thumb.Cells.Count);
		Assert.All(thumb.Cells, (LayoutThumbCell c) => Assert.False(c.Widget));
	}

	[Fact]
	public void ThumbFollowsGridSlots()
	{
		PageLayoutV2 page = new PageLayoutV2 { Mode = LayoutSchema.ModeGrid, Cols = 12 };
		page.Blocks.Add(new BlockLayout { Id = "folders", X = 0, Y = 0, W = 4, H = 1 });
		page.Blocks.Add(new BlockLayout { Id = "versions", X = 4, Y = 0, W = 8, H = 1 });
		page.Blocks.Add(new BlockLayout { Id = "custom:w1", X = 0, Y = 1, W = 12, H = 1 });
		page.Blocks.Add(new BlockLayout { Id = "status", Hidden = true });
		LayoutThumb thumb = LayoutPageCards.ThumbOf("library:main", page);
		Assert.False(thumb.Placeholder);
		Assert.Equal(12, thumb.Cols);
		Assert.Equal(3, thumb.Cells.Count);
		LayoutThumbCell folders = thumb.Cells.Single((LayoutThumbCell c) => c.Id == "folders");
		LayoutThumbCell versions = thumb.Cells.Single((LayoutThumbCell c) => c.Id == "versions");
		LayoutThumbCell widget = thumb.Cells.Single((LayoutThumbCell c) => c.Id == "custom:w1");
		Assert.Equal((0, 0, 4), (folders.Col, folders.Row, folders.Span));
		Assert.Equal((4, 0, 8), (versions.Col, versions.Row, versions.Span));
		Assert.Equal((0, 1, 12), (widget.Col, widget.Row, widget.Span));
		Assert.True(widget.Widget);
		Assert.False(folders.Widget);
		Assert.DoesNotContain(thumb.Cells, (LayoutThumbCell c) => c.Id == "status");
	}

	[Fact]
	public void ThumbFollowsStackRows()
	{
		PageLayoutV2 page = new PageLayoutV2();
		page.Blocks.Add(new BlockLayout { Id = "a" });
		page.Blocks.Add(new BlockLayout { Id = "b", Hidden = true });
		page.Blocks.Add(new BlockLayout { Id = "plugin:hello:card" });
		LayoutThumb thumb = LayoutPageCards.ThumbOf("download", page);
		Assert.Equal(1, thumb.Cols);
		Assert.Equal(new string[2] { "a", "plugin:hello:card" }, thumb.Cells.Select((LayoutThumbCell c) => c.Id).ToArray());
		Assert.Equal(new int[2] { 0, 1 }, thumb.Cells.Select((LayoutThumbCell c) => c.Row).ToArray());
		Assert.True(thumb.Cells[1].Widget);
	}

	[Fact]
	public void ThumbFlowsCanvasByYThenX()
	{
		PageLayoutV2 page = new PageLayoutV2 { Mode = LayoutSchema.ModeCanvas, Cols = 12 };
		page.Blocks.Add(new BlockLayout { Id = "right", X = 0.5, Y = 0.0, W = 0.5, H = 0.3 });
		page.Blocks.Add(new BlockLayout { Id = "left", X = 0.0, Y = 0.0, W = 0.5, H = 0.3 });
		page.Blocks.Add(new BlockLayout { Id = "wide", X = 0.0, Y = 0.6, W = 1.0, H = 0.3 });
		page.Blocks.Add(new BlockLayout { Id = "gone", X = 0.0, Y = 0.1, W = 0.2, H = 0.2, Hidden = true });
		LayoutThumb thumb = LayoutPageCards.ThumbOf("download", page);
		Assert.Equal(12, thumb.Cols);
		Assert.Equal(new string[3] { "left", "right", "wide" }, thumb.Cells.Select((LayoutThumbCell c) => c.Id).ToArray());
		Assert.Equal(new int[3] { 0, 6, 0 }, thumb.Cells.Select((LayoutThumbCell c) => c.Col).ToArray());
		Assert.Equal(new int[3] { 0, 0, 1 }, thumb.Cells.Select((LayoutThumbCell c) => c.Row).ToArray());
		Assert.Equal(new int[3] { 6, 6, 12 }, thumb.Cells.Select((LayoutThumbCell c) => c.Span).ToArray());
	}

	// ————— 6) 全部恢复默认 —————

	[Fact]
	public void ResetAllPagesKeepsPreviousAndLeavesTheRest()
	{
		UseDir();
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			doc.Layout.Pages["download"] = new PageLayoutV2();
			doc.Layout.Pages["ai"] = new PageLayoutV2();
			SplitState split = new SplitState();
			split.Ratios["folders"] = 0.4;
			doc.Layout.Splits["library"] = split;
			doc.Layout.Widgets.Add(new WidgetDef { Id = "w1", Type = LayoutSchema.WidgetDivider, Title = "分隔线" });
			doc.Layout.Shell.Tabs = new List<string> { "download", "launch", "settings" };
			Assert.True(LayoutStoreV2.Save(doc));

			Assert.Equal("", LayoutProfiles.ResetAllPages(out string kept, out int removed));
			Assert.Equal(2, removed);
			Assert.Equal(LayoutProfiles.PreviousName, kept);

			LayoutStoreV2.ResetCache();
			LayoutV2 after = LayoutStoreV2.Load();
			Assert.Empty(after.Layout.Pages);
			Assert.Equal("", after.ActiveProfile);
			// 留底的那份还能切回
			Assert.Equal(2, after.Profiles[LayoutProfiles.PreviousName].Pages.Count);
			// 页签 / 分栏 / 组件定义不动
			Assert.Equal(new string[3] { "download", "launch", "settings" }, after.Layout.Shell.Tabs.ToArray());
			Assert.Equal(0.4, after.Layout.Splits["library"].Ratios["folders"], 6);
			Assert.Single(after.Layout.Widgets);

			// 已经全是默认:再点一次什么也不做
			Assert.Equal("", LayoutProfiles.ResetAllPages(out string kept2, out int removed2));
			Assert.Equal("", kept2);
			Assert.Equal(0, removed2);
		}
		finally
		{
			Release();
		}
	}

	// ————— 7) 分栏宽度一行 —————

	[Fact]
	public void SplitSummaryListsOnlyStoredTwoColumnSplits()
	{
		LayoutV2 doc = LayoutV2.CreateDefault();
		Assert.Empty(LayoutSplitSummary.AdjustedNames(doc));
		SplitState settings = new SplitState();
		settings.Ratios["nav"] = 0.3;
		doc.Layout.Splits["settings"] = settings;
		SplitState library = new SplitState();
		library.Ratios["folders"] = 0.4;
		doc.Layout.Splits["library"] = library;
		// 不是左右分栏的那几处(网格列数 / 纵向分行)即使有值也不计
		SplitState plugins = new SplitState();
		plugins.Ratios["cols"] = 0.5;
		doc.Layout.Splits["plugins"] = plugins;
		// 按 SplitCatalog 的顺序:实例库在前,设置导航在后
		Assert.Equal(new string[2] { I18n.L("实例库文件夹栏"), I18n.L("设置左侧导航") }, LayoutSplitSummary.AdjustedNames(doc).ToArray());
		Assert.Empty(LayoutSplitSummary.AdjustedNames(null));
		// 每一处左右分栏都有自己的名字,不回落成 pageId / splitId 这类写法
		foreach (SplitSite site in SplitCatalog.All.Where((SplitSite s) => s.IsTwoColumn))
		{
			string name = LayoutSplitSummary.NameOf(site);
			Assert.False(string.IsNullOrWhiteSpace(name));
			Assert.DoesNotContain("/", name);
			Assert.DoesNotContain(site.SplitId, name);
			Assert.NotEqual(I18n.L("其它页面"), name);
		}
	}

	// ————— 8) 方案卡片 —————

	[Fact]
	public void ProfileCardsMarkActiveAndAiBackups()
	{
		UseDir();
		try
		{
			Assert.Equal("", LayoutProfiles.Create("我的布局", fromCurrent: true));
			LayoutV2 doc = LayoutStoreV2.Load();
			LayoutBody backup = new LayoutBody();
			backup.Pages["download"] = new PageLayoutV2();
			doc.Profiles["AI 备份 2026-11-20 15:20:00"] = backup;
			Assert.True(LayoutStoreV2.Save(doc));

			List<LayoutProfileCard> cards = LayoutProfiles.Cards(LayoutStoreV2.Load());
			Assert.Equal(2, cards.Count);
			LayoutProfileCard mine = cards.Single((LayoutProfileCard c) => c.Name == "我的布局");
			LayoutProfileCard ai = cards.Single((LayoutProfileCard c) => c.IsAiBackup);
			Assert.True(mine.IsActive);
			Assert.False(mine.IsAiBackup);
			Assert.Equal("我的布局", mine.DisplayName);
			Assert.False(ai.IsActive);
			Assert.Equal("AI 备份 2026-11-20 15:20:00", ai.Name);
			Assert.Equal(I18n.L("AI 改布局前 {0}", "11-20 15:20"), ai.DisplayName);
			Assert.DoesNotContain("AI 备份", ai.DisplayName);
			Assert.False(string.IsNullOrWhiteSpace(mine.Info));
			Assert.False(string.IsNullOrWhiteSpace(ai.Info));
			Assert.Empty(LayoutProfiles.Cards(null));
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void AiBackupNamesAreRecognisedByPrefix()
	{
		Assert.True(LayoutProfiles.IsAiBackup("AI 备份 2026-11-20 15:20:00"));
		Assert.True(LayoutProfiles.IsAiBackup("AI 备份 随便什么"));
		Assert.False(LayoutProfiles.IsAiBackup("AI 备份"));
		Assert.False(LayoutProfiles.IsAiBackup("我的 AI 备份 1"));
		Assert.False(LayoutProfiles.IsAiBackup(null));
		// 时间解析不了就原样带上后缀
		Assert.Equal(I18n.L("AI 改布局前 {0}", "随便什么"), LayoutProfiles.DisplayName("AI 备份 随便什么"));
		Assert.Equal("普通方案", LayoutProfiles.DisplayName("普通方案"));
	}

	[Fact]
	public void AiBackupPrefixMatchesCore()
	{
		// 识别规则与 AI 那条路写备份时用的前缀必须是同一个字符串(两边各写一份,这里钉住不漂移)
		Assert.Equal(TipCL.Core.Launcher.Ai.LayoutTools.BackupPrefix, LayoutProfiles.AiBackupPrefix);
	}

	// ————— 9) 网页 / 按钮组件的全局开关(Q6) —————

	[Fact]
	public void DangerWidgetsSwitchDefaultsOff()
	{
		// 全局开关存在 UiPrefs 里,缺省关;自动靠拢仍是缺省开
		Assert.False(new UiPrefs().LayoutDangerWidgets);
		Assert.True(new UiPrefs().LayoutAutoPack);
		// LayoutEditPrefs 要碰主窗口(WPF),net8 宿主载不了 PresentationFramework:这里按源码钉住
		// 「写法照 AutoPack」:读写的是 UiPrefs.LayoutDangerWidgets,并且落盘(SaveUiPrefs)
		string prefs = Src("src/TipCL/Pages/Layout/LayoutEditPrefs.cs");
		Assert.Contains("public static bool AllowDangerWidgets", prefs);
		Assert.Contains("win.Prefs.LayoutDangerWidgets : _dangerFallback", prefs);
		Assert.Contains("win.Prefs.LayoutDangerWidgets = value;", prefs);
		Assert.Contains("private static bool _dangerFallback;", prefs);
		// 设置页「高级」里的开关读写的就是它
		string page = Src("src/TipCL/Pages/SettingsLayoutPage.cs");
		Assert.Contains("LayoutEditPrefs.AllowDangerWidgets = true;", page);
		Assert.Contains("LayoutEditPrefs.AllowDangerWidgets = false;", page);
	}

	// ————— 10) 视图结构(源码事实) —————

	[Fact]
	public void LayoutViewKeepsLegacyBlockIdsAndCollapsesAdvanced()
	{
		string src = Src("src/TipCL/Pages/SettingsLayoutPage.cs");
		// 区块 id 沿用 R33 的七个:用户已存过的本页布局照旧对得上
		foreach (string id in new string[7] { "appearance", "shell", "blocks", "splits", "widgets", "profiles", "json" })
		{
			Assert.Contains("new BlockSpec { Id = \"" + id + "\"", src);
		}
		// 「高级」默认折叠:字段没有初值(= false)
		Assert.Contains("private bool _layoutAdvancedOpen;", src);
		// JSON 框不再常驻页面:页面上只剩「在编辑器中打开…」,编辑器窗口里仍走 LayoutJsonEditor 的校验 / 应用
		Assert.DoesNotContain("LayoutJsonCard", src);
		Assert.DoesNotContain("Ui.LogBox()", src);
		Assert.Contains("LayoutJsonWindow.ShowFor(", src);
		string window = Src("src/TipCL/Pages/Layout/LayoutJsonWindow.cs");
		Assert.Contains("LayoutJsonEditor.Apply(", window);
		Assert.Contains("LayoutJsonEditor.Inspect(", window);
		// 13 行分栏表删掉了:不再逐处渲染 SplitCatalog
		Assert.DoesNotContain("SplitNowText", src);
		Assert.DoesNotContain("SplitKindLabel", src);
	}

	[Fact]
	public void TitlebarRowDropsBridgeButKeepsTheData()
	{
		List<string> literals = I18nLiterals(Src("src/TipCL/Pages/SettingsLayoutPage.cs"));
		Assert.Contains("AI 按钮", literals);
		Assert.Contains("主题切换", literals);
		Assert.Contains("账号", literals);
		Assert.Contains("隐藏 AI 按钮后仍可按 Ctrl+K 打开", literals);
		Assert.DoesNotContain(literals, (string t) => t.Contains("桥接"));
		// 数据与写入口都保留:老文档里存的桥接开关照旧生效
		MethodInfo? set = typeof(ShellTabs).GetMethod("SetTitlebar");
		Assert.NotNull(set);
		Assert.Contains(set!.GetParameters(), (ParameterInfo p) => p.Name == "bridge");
		Assert.True(ShellTabs.ShowBridge(new ShellLayout()));
	}

	// ————— 小工具 —————

	private static string Root()
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
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
}
