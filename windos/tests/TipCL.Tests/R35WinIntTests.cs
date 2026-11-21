using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TipCL.Core.Launcher.Layout;
using TipCL.Pages;
using TipCL.Pages.Layout;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// R35 win-int:quick-v3 快照(编辑态 / 设置 → 布局)与 R34 三个包(l0 / l3 / l5)合并后的接线守卫。
/// 界面是 WPF,net8 测试宿主实例化不了控件,所以纯逻辑直接断言、接线按源码文本断言(与 LayoutV3SourceTests 同一做法)。
/// </summary>
public class R35WinIntTests
{
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

	// ————— GOAL §5.4「页面」卡片:入口表 − SingleBlock − settings:layout + library:main = 15 张 —————

	[Fact]
	public void PageCardsAreExactlyFifteenAndIncludeLibrary()
	{
		List<LayoutBlockEntryCatalog.Entry> cards = LayoutBlockEntryCatalog.PageCardEntries();
		Assert.Equal(15, cards.Count);
		Assert.Equal(15, cards.Select((LayoutBlockEntryCatalog.Entry e) => e.PageId).Distinct().Count());
		Assert.DoesNotContain(cards, (LayoutBlockEntryCatalog.Entry e) => e.SingleBlock);
		Assert.DoesNotContain(cards, (LayoutBlockEntryCatalog.Entry e) => e.PageId == "settings:layout");
		Assert.DoesNotContain(cards, (LayoutBlockEntryCatalog.Entry e) => e.PageId == "settings:network" || e.PageId == "settings:about");
		Assert.Contains(cards, (LayoutBlockEntryCatalog.Entry e) => e.PageId == "library:main");
		// 子视图保留 Hint(找不到页面时提示怎么打开),仍然是卡片
		Assert.Contains(cards, (LayoutBlockEntryCatalog.Entry e) => e.PageId == "install" && e.Hint.Length > 0);
		Assert.Contains(cards, (LayoutBlockEntryCatalog.Entry e) => e.PageId == "version-manage:embedded" && e.Hint.Length > 0);
	}

	[Fact]
	public void DefaultSixCardsComeFirstInGoalOrder()
	{
		string[] expected = new string[6] { "launch", "download", "library:main", "multiplayer", "account", "ai" };
		Assert.Equal(expected, LayoutBlockEntryCatalog.PrimaryCardPages);
		List<LayoutBlockEntryCatalog.Entry> cards = LayoutBlockEntryCatalog.PageCardEntries();
		Assert.Equal(expected, cards.Take(6).Select((LayoutBlockEntryCatalog.Entry e) => e.PageId).ToArray());
	}

	[Fact]
	public void LibraryEntryNavigatesToLibraryTab()
	{
		LayoutBlockEntryCatalog.Entry? library = LayoutBlockEntryCatalog.Find("library:main");
		Assert.NotNull(library);
		Assert.Equal("library", library!.NavId);
		Assert.Equal(-1, library.SettingsView);
		Assert.False(library.SingleBlock);
		Assert.Equal("", library.Hint);
		Assert.Equal("实例库", library.Label);
		Assert.Contains("PageCardEntries()", Src("src/TipCL/Pages/SettingsLayoutPage.cs"));
	}

	// ————— GOAL §5.4 方案:AI 备份显示成「AI 改布局前 · 时间」,真实名字不变 —————

	[Fact]
	public void AiBackupProfilesShowFriendlyName()
	{
		string name = TipCL.Core.Launcher.Ai.LayoutTools.BackupPrefix + "2026-11-20 21:57:03";
		string shown = LayoutProfiles.DisplayName(name);
		Assert.DoesNotContain("AI 备份", shown);
		Assert.Contains("11-20 21:57", shown);
		Assert.Equal("我的方案", LayoutProfiles.DisplayName("我的方案"));
		Assert.Equal("", LayoutProfiles.DisplayName(null));
	}

	// ————— D-81 Q1:canvas 推导只保留一份实现(R34 WP-L3 的 LayoutCanvasMigration) —————

	[Fact]
	public void CanvasCompatDelegatesToMigration()
	{
		PageLayoutV2 page = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeCanvas,
			Cols = 12,
			Blocks = new List<BlockLayout>
			{
				new BlockLayout { Id = "b", X = 0.5, Y = 0.0, W = 0.5, H = 0.3 },
				new BlockLayout { Id = "a", X = 0.0, Y = 0.0, W = 0.5, H = 0.3 },
				new BlockLayout { Id = "c", X = 0.0, Y = 0.5, W = 1.0, H = 0.3 },
				new BlockLayout { Id = "h", X = 0.0, Y = 0.9, W = 0.4, H = 0.1, Hidden = true }
			}
		};
		PageLayoutV2? viaCompat = LayoutCanvasCompat.AsGrid(page);
		PageLayoutV2? viaMigration = LayoutCanvasMigration.ToGridPage(page);
		Assert.NotNull(viaCompat);
		Assert.NotNull(viaMigration);
		Assert.Equal(LayoutSchema.ModeGrid, viaCompat!.Mode);
		Assert.Equal(viaMigration!.Blocks.Select((BlockLayout b) => b.Id + ":" + b.X + "," + b.Y + "," + b.Span + "," + b.Hidden), viaCompat.Blocks.Select((BlockLayout b) => b.Id + ":" + b.X + "," + b.Y + "," + b.Span + "," + b.Hidden));
		// 原页面一个字段都不动
		Assert.Equal(LayoutSchema.ModeCanvas, page.Mode);
		Assert.Equal(0.5, page.Blocks[0].X);
		// 非 canvas 页原样返回同一个对象
		PageLayoutV2 grid = new PageLayoutV2 { Mode = LayoutSchema.ModeGrid };
		Assert.Same(grid, LayoutCanvasCompat.AsGrid(grid));
		// 编辑器装载走的也是这一份
		string editor = Src("src/TipCL/Pages/Layout/LayoutEditor.cs");
		Assert.Contains("LayoutCanvasMigration.ToGridPage(canvasPage", editor);
	}

	// ————— D-81 Q6:网页 / 按钮全局开关只有一个真源 —————

	[Fact]
	public void DangerWidgetSwitchHasSingleSource()
	{
		// 运行时读写要碰 MainWindow(WPF),net8 测试宿主加载不了 → 按源码断言:别名直接转发到唯一真源
		string prefs = Src("src/TipCL/Pages/Layout/LayoutEditPrefs.cs");
		Assert.Contains("public static bool AllowDangerWidgets", prefs);
		Assert.Contains("return LayoutSettingsPrefs.DangerWidgetsEnabled;", prefs);
		Assert.Contains("LayoutSettingsPrefs.DangerWidgetsEnabled = value;", prefs);
		Assert.Equal("layout.v3.danger_widgets", LayoutSettingsPrefs.DangerWidgetsKey);
		// 默认关:存储缺键时的内存兜底是 false
		Assert.Contains("private static bool _fallback;", Src("src/TipCL/Pages/Layout/LayoutSettingsPrefs.cs"));
		// 抽屉按这个开关过滤网页 / 按钮
		Assert.Contains("if (LayoutSettingsPrefs.DangerWidgetsEnabled)", Src("src/TipCL/Pages/Layout/LayoutEditorV3.cs"));
	}

	// ————— GOAL §5.2:自动靠拢固定为开,界面不再提供修改 —————

	[Fact]
	public void EditorNoLongerReadsOrWritesAutoPackPreference()
	{
		string editor = Src("src/TipCL/Pages/Layout/LayoutEditor.cs");
		Assert.Contains("private static bool AutoPackOn => true;", editor);
		Assert.DoesNotContain("CompactCells(pinned, false, LayoutEditPrefs.AutoPack)", editor);
		Assert.DoesNotContain("CompactCells(pinned, swapX, LayoutEditPrefs.AutoPack)", editor);
	}

	// ————— 跨包接线(GOAL §6.4 / 各包报告的合并事项) —————

	[Fact]
	public void CrossPackageWiringIsInPlace()
	{
		// L5 ← 快照:右键入口门槛用 BlockHostRules,菜单体按编辑态重建
		string host = Src("src/TipCL/Pages/BlockHost.cs");
		Assert.Contains("if (BlockHostRules.OffersLayoutMenu(_specCount))", host);
		Assert.Contains("RebuildContextMenu(contextMenu);", host);
		Assert.Contains("internal bool OffersLayoutMenu", host);
		Assert.Contains("public bool CanEdit", host);
		// L5 合并事项 2:壳级兜底右键跳过单块宿主
		string hooks = Src("src/TipCL/Pages/Layout/LayoutShellHooks.cs");
		Assert.Contains("h.OffersLayoutMenu || h.IsEditing", hooks);
		Assert.Contains("return BlockHostRules.OffersLayoutMenu(count);", hooks);
		// L3 合并事项 1:卡片按画布实际模式出手柄
		string editor = Src("src/TipCL/Pages/Layout/LayoutEditor.cs");
		Assert.Contains("card.SetBoxMode(_box.Mode);", editor);
		// L0 合并事项 1 / 2:落点框浅底跟主题色;截图钩子接正式入口
		Assert.Contains("d[\"B.EditDropFill\"] = new SolidColorBrush(color5);", Src("src/TipCL/Pages/SettingsPage.Themes.cs"));
		Assert.Contains("\"B.EditDropFill\"", Src("src/TipCL/Pages/SettingsPage.cs"));
		string shot = Src("src/TipCL/DevShot.cs");
		Assert.Contains("SelectHook ??=", shot);
		Assert.Contains("h.Editor.DevSelect(id)", shot);
		Assert.Contains("h.Editor.OpenAddDrawer();", shot);
		// L5 越界需求:启动页编辑态压暗英雄区 + 「主区域固定」
		string v3 = Src("src/TipCL/Pages/Layout/LayoutEditorV3.cs");
		Assert.Contains("public event Action<bool>? EditingChanged;", v3);
		// R35 启动页 v3:编辑态压暗 / 「主区域固定」改由组件区 ZoneSetMode 驱动(R35LaunchV3Tests 守卫)
		// 设置页 ↔ 编辑态
		string settings = Src("src/TipCL/Pages/SettingsLayoutPage.cs");
		Assert.Contains("LayoutShellHooks.BeginEditPage(pageId);", settings);
		Assert.Contains("LayoutShellHooks.CanEditPage(pageId);", settings);
	}

	// ————— GOAL §5.1:网格右边缘 + 下边缘把手、「N / 列数」读数、L0 令牌 —————

	[Fact]
	public void CardChromeUsesV3TokensAndBothEdgeGrips()
	{
		string v3 = Src("src/TipCL/Pages/Layout/LayoutEditorV3.cs");
		Assert.Contains("_host.BeginBoxResize(this, \"e\", e);", v3);
		Assert.Contains("_host.BeginBoxResize(this, \"s\", e);", v3);
		Assert.Contains("I18n.L(\"拖动改高度 · 双击恢复自动高度\")", v3);
		Assert.Contains("I18n.L(\"{0} / {1} 列\", span, cols)", v3);
		Assert.Contains("\"B.EditOutline\"", v3);
		Assert.Contains("\"B.EditOutlineOnDark\"", v3);
		Assert.Contains("_v3Drawer.Width = 316.0;", v3);
		Assert.Contains("I18n.L(\"· 组件\")", v3);
		// 深色表面只按 pageId == "launch" 判断(GOAL §6.4 第 2 条)
		Assert.Contains("string.Equals(_pageId, \"launch\", StringComparison.Ordinal)", v3);
		// 拖动时的淡色列底纹 + 虚线落点框
		string panel = Src("src/TipCL/Pages/Layout/GridCanvasPanel.cs");
		Assert.Contains("\"B.EditColShade\"", panel);
		Assert.Contains("StrokeDashArray = new DoubleCollection { 4.0, 3.0 }", panel);
	}
}
