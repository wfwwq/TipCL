// R32-LINSPLIT:分栏比例(SplitCatalog / SplitRatioStore / PageLayoutHost)的净增量用例。
//
// 对象:src/TipCL.App/Controls/PageLayoutHost.cs(本包新建,按 Windows 冻结件 @0a304a7
//       windos/src/TipCL/Pages/Layout/PageLayoutHost.cs 三段搬入:SplitKind/SplitSite/SplitCatalog
//       + SplitRatioStore + PageLayoutHost)。
//
// 口径(照 R32-BRIEF §3.5(a) 与 D25):
//   * 每条用例钉住源文件里的一个**关键行为**,不是"没抛异常就算过":
//     clamp / roundtrip / 落盘键名 / 复位删键 / 清空壳的 Extra 守卫 / 整页复位 / 不可写形态拒写 /
//     CommitDrag 像素→比例 / PageColumns 的 mode=grid 门槛 / 13 条站点表现状值;
//   * 「分隔条命中」与「两栏宽随比例变化」用**真 Measure/Arrange 读几何**(仓库先例:
//     tests/TipCL.Tests/Layout/LayoutGridCanvasTests.cs —— 自定义面板的测量/排布不需要窗口就能验);
//   * 碰 Avalonia 的用例一律经 TestApp.Run 落到无头 UI 线程(控件有线程亲和);
//   * 夹具 LayoutStoreDir / HeadlessPump 复用 LayoutMoreTests.cs 里那两个(同程序集同命名空间),
//     不重复造第二套 —— 它们把 LayoutStoreV2.DirOverride 指到独立临时目录,不碰真实数据根。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using TipCL.App.Controls;
using TipCL.Core.Launcher.Layout;
using Xunit;

namespace TipCL.Tests.App;

// ————————————————————————— 1 站点表(纯数据,零界面依赖) —————————————————————————

/// <summary>
/// <see cref="SplitCatalog"/> 的"默认 = 现状"可执行事实表。
/// 断言的每个数字都来自 Windows 冻结件 <c>windos/src/TipCL/Pages/Layout/PageLayoutHost.cs:105-253</c>
/// 的 <c>SplitSite</c> 字面量(Linux 侧 4 处接线站点 = #2 #6 #9 #10)。
/// </summary>
public class SplitCatalogTests
{
	[Fact]
	public void Catalog_IsWindowsThirteenRowTable()
	{
		Assert.Equal(13, SplitCatalog.All.Length);
		// 序号必须是 1..13 逐行,不重不漏(视图表按 Index 显示)
		Assert.Equal(Enumerable.Range(1, 13), SplitCatalog.All.Select(s => s.Index));
		// 13 处里真正是左右两栏的 6 条(#1 library / #2 settings / #6 version-manage / #7 ai /
		// #8 feedback / #9 account):Windows TwoColumnCount() 同值
		Assert.Equal(6, SplitCatalog.TwoColumnCount());
		Assert.Equal(new[] { "library", "settings", "version-manage", "ai", "feedback", "account" },
			SplitCatalog.All.Where(s => s.IsTwoColumn).Select(s => s.PageId));
	}

	[Fact]
	public void Catalog_NonTwoColumnRows_KeepWindowsKinds()
	{
		// 形态判定逐条对齐 Windows:非两栏的 7 条形态
		Assert.Equal(SplitKind.Rows, Site("instance:detail", "seg").Kind);
		Assert.Equal(SplitKind.HeightClamp, Site("launch", "hero").Kind);
		Assert.Equal(SplitKind.ListRow, Site("download", "row").Kind);
		Assert.Equal(SplitKind.GridCols, Site("plugins", "cols").Kind);
		Assert.Equal(SplitKind.Rows, Site("settings:about", "about").Kind);
		Assert.Equal(SplitKind.SingleCol, Site("catalog", "list").Kind);
		Assert.Equal(SplitKind.Rows, Site("install", "frame").Kind);
		Assert.False(Site("plugins", "cols").IsTwoColumn);
	}

	[Fact]
	public void Catalog_FourWiredLinuxSites_MatchWindowsDeclarations()
	{
		// #2 settings/nav:168px + 18 留白,左栏下限 168
		SplitSite settings = Site("settings", "nav");
		Assert.True(settings.IsTwoColumn);
		Assert.Equal(168.0, settings.DefaultPixels);
		Assert.Equal("left", settings.PixelsSide);
		Assert.Equal(18.0, settings.Gap);
		Assert.Equal(168.0, settings.LeftMin);
		Assert.True(settings.Writable);

		// #6 version-manage/nav:272px + 18 留白,左栏下限 272
		SplitSite vm = Site("version-manage", "nav");
		Assert.Equal(272.0, vm.DefaultPixels);
		Assert.Equal(18.0, vm.Gap);
		Assert.Equal(272.0, vm.LeftMin);

		// #9 account/hero:240px + 14 留白(现状那 14px 右外边距改成分隔条),左栏下限 240
		SplitSite account = Site("account", "hero");
		Assert.Equal(240.0, account.DefaultPixels);
		Assert.Equal(14.0, account.Gap);
		Assert.Equal(240.0, account.LeftMin);

		// #1 library/folders 是星号 1:2 = 1/3(本树该页随 LIN-PAGES 改成 v2 grid,未接线)
		Assert.Equal(1.0 / 3.0, Site("library", "folders").DefaultRatio, 12);
	}

	[Fact]
	public void Catalog_UnknownKeysReturnNull_AndEmbeddedIsReadOnly()
	{
		Assert.Null(SplitCatalog.Find("nope", "nav"));
		Assert.Null(SplitCatalog.Find("settings", "nope"));
		Assert.Null(SplitCatalog.Find(null, null));
		Assert.Null(SplitCatalog.Find("settings", ""));

		// 内嵌态(实例详情内嵌版本管理):与顶级同一套实现,但不可写
		SplitSite embedded = Site("instance:settings", "vm");
		Assert.Equal(272.0, embedded.DefaultPixels);
		Assert.Equal(18.0, embedded.Gap);
		Assert.False(embedded.Writable);
		Assert.Equal(0, embedded.Index);
	}

	[Fact]
	public void Catalog_EveryTwoColumnSite_HasGapAndNote()
	{
		// 两栏站点必须给出"分隔条占哪段留白"的现状说明(Gap>0 且 Note 非空)——
		// 这是"默认 = 现状(首屏逐像素不变)"能成立的前提
		foreach (SplitSite site in SplitCatalog.All.Where(s => s.IsTwoColumn))
		{
			Assert.True(site.Gap > 0.0, site.PageId + "/" + site.SplitId + " 缺 Gap");
			Assert.False(string.IsNullOrWhiteSpace(site.Note), site.PageId + "/" + site.SplitId + " 缺 Note");
			Assert.True(site.DefaultPixels > 0.0 || site.DefaultRatio > 0.0, site.PageId + "/" + site.SplitId + " 缺现状宽度");
		}
	}

	private static SplitSite Site(string pageId, string splitId)
	{
		SplitSite? site = SplitCatalog.Find(pageId, splitId);
		Assert.NotNull(site);
		return site!;
	}
}

// ————————————————————————— 2 比例存储(唯一入口 = LayoutStoreV2) —————————————————————————

/// <summary>
/// <see cref="SplitRatioStore"/> 的读写语义:clamp / 落盘键 / 复位删键 / 清空壳的 Extra 守卫 /
/// 整页复位 / 不可写形态拒写 / CommitDrag 换算 / PageColumns 的 mode=grid 门槛。
/// 夹具把布局存储重定向到独立临时目录(与 WidgetTests / LayoutEditorTests / LayoutMoreTests 同法)。
/// </summary>
[Collection("TipCL 真机交互回归")]
public class SplitRatioStoreTests
{
	[Fact]
	public void ClampRatio_IsFifteenToEightyFive_AndNaNFallsToDefault()
	{
		Assert.Equal(0.15, SplitRatioStore.ClampRatio(0.05));
		Assert.Equal(0.15, SplitRatioStore.ClampRatio(-3.0));
		Assert.Equal(0.85, SplitRatioStore.ClampRatio(0.95));
		Assert.Equal(0.85, SplitRatioStore.ClampRatio(7.0));
		Assert.Equal(0.42, SplitRatioStore.ClampRatio(0.42));
		// NaN / Infinity 落契约默认 0.5(与 LayoutSchema.Clamp 同口径)
		Assert.Equal(0.5, SplitRatioStore.ClampRatio(double.NaN));
		Assert.Equal(0.5, SplitRatioStore.ClampRatio(double.PositiveInfinity));
		Assert.Equal(0.15, SplitRatioStore.MinRatio);
		Assert.Equal(0.85, SplitRatioStore.MaxRatio);
		Assert.Equal(0.5, SplitRatioStore.DefaultRatio);
	}

	[Fact]
	public void WriteThenRead_RoundTrips_AndLandsInV2SplitsSection()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("split-roundtrip");
		Assert.Null(SplitRatioStore.Read("version-manage", "nav"));
		Assert.False(SplitRatioStore.IsStored("version-manage", "nav"));

		Assert.True(SplitRatioStore.Write("version-manage", "nav", 0.42));
		Assert.True(SplitRatioStore.IsStored("version-manage", "nav"));
		Assert.Equal(0.42, SplitRatioStore.Read("version-manage", "nav")!.Value, 12);

		// 落盘键名与 Windows 一字不差:v2 文档的 "splits": { "<pageId>": { "<splitId>": ratio } }
		string json = File.ReadAllText(Path.Combine(dir.Dir, "ui_layout_v2.json"));
		Assert.Contains("\"splits\"", json, StringComparison.Ordinal);
		Assert.Contains("\"version-manage\"", json, StringComparison.Ordinal);
		Assert.Contains("\"nav\"", json, StringComparison.Ordinal);
		// 重读(清进程内缓存 → 真读盘)仍是同一值
		LayoutStoreV2.ResetCache();
		Assert.Equal(0.42, SplitRatioStore.Read("version-manage", "nav")!.Value, 12);
	}

	[Fact]
	public void Write_ClampsOutOfRangeValueOnDisk()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("split-clamp");
		Assert.True(SplitRatioStore.Write("account", "hero", 0.99));
		Assert.Equal(0.85, SplitRatioStore.Read("account", "hero")!.Value, 12);
		LayoutStoreV2.ResetCache();
		// 磁盘上也不能是 0.99(schema 归一化 + 本类写前夹取,两道)
		string json = File.ReadAllText(Path.Combine(dir.Dir, "ui_layout_v2.json"));
		Assert.DoesNotContain("0.99", json, StringComparison.Ordinal);
	}

	[Fact]
	public void Clear_RemovesKey_AndDropsEmptyShell()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("split-clear");
		Assert.False(SplitRatioStore.Clear("account", "hero"));   // 没存过 → false,不抛
		Assert.True(SplitRatioStore.Write("account", "hero", 0.3));
		Assert.True(SplitRatioStore.Write("account", "hero2", 0.6));
		Assert.True(SplitRatioStore.Clear("account", "hero"));
		Assert.Null(SplitRatioStore.Read("account", "hero"));
		Assert.Equal(0.6, SplitRatioStore.Read("account", "hero2")!.Value, 12);   // 别处不动
		Assert.True(SplitRatioStore.Clear("account", "hero2"));
		// 该页一个比例都不剩**且没有 Extra 残留** ⇒ pageId 这一层也去掉(文档里不留空壳)
		Assert.False(SplitsHasPage(dir.Dir, "account"));
	}

	[Fact]
	public void Clear_KeepsShellWhenExtraPresent()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("split-extra");
		// 造一份带未知字段的文档:宽容读取承诺"未知字段保留",复位时不能把它顺手删掉
		Directory.CreateDirectory(dir.Dir);
		File.WriteAllText(Path.Combine(dir.Dir, "ui_layout_v2.json"),
			"{\"kind\":\"tipcl-layout\",\"version\":2,\"saved_at\":0,\"app\":\"t\","
			+ "\"layout\":{\"splits\":{\"account\":{\"hero\":0.4,\"x_future\":\"keep-me\"}}}}");
		LayoutStoreV2.ResetCache();
		Assert.Equal(0.4, SplitRatioStore.Read("account", "hero")!.Value, 12);

		Assert.True(SplitRatioStore.Clear("account", "hero"));
		Assert.Null(SplitRatioStore.Read("account", "hero"));
		// 「清空壳的 Extra 守卫」:Extra 还在 ⇒ 壳必须留着,未知字段逐字保留
		string json = File.ReadAllText(Path.Combine(dir.Dir, "ui_layout_v2.json"));
		Assert.Contains("x_future", json, StringComparison.Ordinal);
		Assert.Contains("keep-me", json, StringComparison.Ordinal);
		Assert.True(SplitsHasPage(dir.Dir, "account"));
	}

	/// <summary>v2 文档的 <c>layout.splits</c> 里是否还有该页这一层(JSON 解析,不靠子串猜)。</summary>
	private static bool SplitsHasPage(string dir, string pageId)
	{
		using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "ui_layout_v2.json")));
		if (!doc.RootElement.TryGetProperty("layout", out JsonElement layout)
			|| !layout.TryGetProperty("splits", out JsonElement splits))
		{
			return false;
		}
		return splits.TryGetProperty(pageId, out _);
	}

	[Fact]
	public void ClearPage_RemovesEveryRatioOfThatPage_ReturnsCount_LeavesOthersAlone()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("split-clearpage");
		Assert.Equal(0, SplitRatioStore.ClearPage("account"));           // 没有这一页 → 0
		Assert.Equal(0, SplitRatioStore.ClearPage(null));
		Assert.True(SplitRatioStore.Write("account", "hero", 0.3));
		Assert.True(SplitRatioStore.Write("account", "second", 0.7));
		Assert.True(SplitRatioStore.Write("settings", "nav", 0.25));

		Assert.Equal(2, SplitRatioStore.ClearPage("account"));            // 整页复位:删掉 2 条
		Assert.Null(SplitRatioStore.Read("account", "hero"));
		Assert.Null(SplitRatioStore.Read("account", "second"));
		Assert.Equal(0, SplitRatioStore.ClearPage("account"));            // 幂等
		Assert.Equal(0.25, SplitRatioStore.Read("settings", "nav")!.Value, 12);   // 别的页不动
	}

	[Fact]
	public void Write_RefusesNonWritableSite()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("split-embedded");
		// SplitCatalog.Embedded:内嵌态 Writable=false ⇒ 一律不写(Windows :374)
		Assert.False(SplitRatioStore.Write("instance:settings", "vm", 0.4));
		Assert.Null(SplitRatioStore.Read("instance:settings", "vm"));
		// 未登记的 pageId/splitId 没有"不可写"声明 ⇒ 允许写(Windows 同判定:site == null 不拦)
		Assert.True(SplitRatioStore.Write("unlisted", "x", 0.4));
	}

	[Fact]
	public void CommitDrag_WritesPixelRatio_AndRejectsDegenerateWidths()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("split-commit");
		// 可用宽 882(= 900 - 18 分隔条),左栏拖到 400px
		double ratio = SplitRatioStore.CommitDrag("version-manage", "nav", 400.0, 882.0);
		Assert.False(double.IsNaN(ratio));
		Assert.Equal(400.0 / 882.0, ratio, 12);
		Assert.Equal(ratio, SplitRatioStore.Read("version-manage", "nav")!.Value, 12);

		// 退化宽度一律不写(返回 NaN)
		Assert.True(double.IsNaN(SplitRatioStore.CommitDrag("account", "hero", 0.0, 882.0)));
		Assert.True(double.IsNaN(SplitRatioStore.CommitDrag("account", "hero", 400.0, 0.0)));
		Assert.True(double.IsNaN(SplitRatioStore.CommitDrag("account", "hero", double.NaN, 882.0)));
		// 不可写形态也不写
		Assert.True(double.IsNaN(SplitRatioStore.CommitDrag("instance:settings", "vm", 400.0, 882.0)));
	}

	[Fact]
	public void ClampRatioForWidths_HonoursSitePixelBounds()
	{
		SplitSite vm = SplitCatalog.Find("version-manage", "nav")!;
		// 左栏下限 272px,可用宽 882 ⇒ 比例下限 272/882(比合同的 0.15 更紧)
		Assert.Equal(272.0 / 882.0, SplitRatioStore.ClampRatioForWidths(0.02, 882.0, vm), 12);
		Assert.Equal(0.85, SplitRatioStore.ClampRatioForWidths(0.99, 882.0, vm), 12);   // 上限仍受合同 0.85
		// 窄窗下边界自相矛盾(272 > 882)时退回合同夹取,不抛
		Assert.Equal(0.2, SplitRatioStore.ClampRatioForWidths(0.2, 100.0, vm), 12);
		// 没有站点声明 ⇒ 只走合同夹取(0.02 被合同下限 0.15 抬上来)
		Assert.Equal(0.15, SplitRatioStore.ClampRatioForWidths(0.02, 882.0, null), 12);
		Assert.Equal(0.2, SplitRatioStore.ClampRatioForWidths(0.2, 882.0, null), 12);
	}

	[Fact]
	public void PageColumns_OnlyHonoursDocWhenModeIsGrid()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("split-cols");
		// 文档里没有这一页 / 没有声明 grid ⇒ 回落 fallback(现状 3 列)
		Assert.Equal(3, SplitRatioStore.PageColumns("plugins", 3, 1, 6));
		LayoutV2 doc = LayoutStoreV2.Load();
		doc.Layout.Pages["plugins"] = new PageLayoutV2 { Mode = "stack", Cols = 4 };
		Assert.True(LayoutStoreV2.Save(doc));
		Assert.Equal(3, SplitRatioStore.PageColumns("plugins", 3, 1, 6));      // stack ⇒ 不生效

		doc = LayoutStoreV2.Load();
		doc.Layout.Pages["plugins"].Mode = LayoutSchema.ModeGrid;
		doc.Layout.Pages["plugins"].Cols = 4;
		Assert.True(LayoutStoreV2.Save(doc));
		Assert.Equal(4, SplitRatioStore.PageColumns("plugins", 3, 1, 6));      // grid ⇒ 生效

		doc = LayoutStoreV2.Load();
		doc.Layout.Pages["plugins"].Cols = 99;
		Assert.True(LayoutStoreV2.Save(doc));
		LayoutStoreV2.ResetCache();
		// schema 的 SnapCols({2,3,4,6,12})会先把 99 吸附成 12,再被 [1,6] 夹到 6
		Assert.Equal(6, SplitRatioStore.PageColumns("plugins", 3, 1, 6));
	}

	[Fact]
	public void Resolve_ThreeLevelFallback()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("split-resolve");
		// 有存储 → 存储值; 无存储 + 有现状比例 → 现状; 都没有 → 0.5
		Assert.True(SplitRatioStore.Write("account", "hero", 0.42));
		Assert.Equal(0.42, SplitRatioStore.Resolve("account", "hero"), 12);
		Assert.Equal(1.0 / 3.0, SplitRatioStore.Resolve("library", "folders"), 12);
		Assert.Equal(0.5, SplitRatioStore.Resolve("unlisted", "x"), 12);
	}
}

// ————————————————————————— 3 分栏宿主(真几何) —————————————————————————

/// <summary>
/// <see cref="PageLayoutHost"/> 的真排布几何:分隔条落在留白列、两栏宽随比例变化、复位回现状、
/// 折叠一侧时左栏与分隔条一起收 0、以及"默认 = 现状"的 x 坐标不变性(设置页的 -18 补偿)。
/// 全部用 <c>Measure/Arrange</c> 直接排(仓库先例 LayoutGridCanvasTests):不建 OS 窗口、不开真窗口。
/// </summary>
[Collection("TipCL 真机交互回归")]
public class PageLayoutHostGeometryTests
{
	private const double PageWidth = 900.0;

	/// <summary>把宿主按固定页宽排一遍(数据源是各页现状:窗口内容区宽 900)。</summary>
	private static void LayoutIt(Control root, double width = PageWidth, double height = 550.0)
	{
		root.InvalidateMeasure();
		root.InvalidateArrange();
		root.Measure(new Size(width, height));
		root.Arrange(new Rect(0.0, 0.0, width, height));
	}

	private static PageLayoutHost VersionHost()
	{
		var host = new PageLayoutHost("version-manage", "nav",
			new GridLength(272.0), new GridLength(1.0, GridUnitType.Star));
		host.SetLeft(new Border { Height = 100.0 });
		host.SetRight(new Border { Height = 100.0 });
		return host;
	}

	[Fact]
	public void DefaultGeometry_MatchLegacyColumns_AndSplitterSitsInGapColumn()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("host-default");
		TestApp.Run(() =>
		{
			PageLayoutHost host = VersionHost();
			LayoutIt(host);

			// 无存储比例 ⇒ 逐像素沿用现状列定义 272,18,*
			Assert.False(host.HasStoredRatio);
			Assert.Equal(272.0, host.ColumnDefinitions[0].Width.Value);
			Assert.Equal(18.0, host.ColumnDefinitions[1].Width.Value);
			Assert.True(host.ColumnDefinitions[2].Width.IsStar);
			Assert.Equal(272.0, host.ColumnDefinitions[0].ActualWidth, 6);
			Assert.Equal(18.0, host.ColumnDefinitions[1].ActualWidth, 6);
			Assert.Equal(610.0, host.ColumnDefinitions[2].ActualWidth, 6);

			// 分隔条:第 1 列(留白列)、可命中、静止时不显细线、拖动光标
			Assert.Equal(1, host.SplitterColumn);
			Assert.True(host.SplitterVisible);
			Assert.Equal(272.0, host.SplitterBounds.X, 6);
			Assert.Equal(18.0, host.SplitterBounds.Width, 6);
			Assert.False(host.HoverLineVisible);
			GridSplitter splitter = host.Children.OfType<GridSplitter>().Single();
			// 光标 = 左右拖动(Windows Cursors.SizeWE);Avalonia 侧是 StandardCursorType.SizeWestEast
			Assert.Equal("SizeWestEast", splitter.Cursor!.ToString());
			// 分隔条本身透明(命中靠 Transparent 画刷,不靠可见装饰 ⇒ 静止零像素变化)
			Assert.Same(Avalonia.Media.Brushes.Transparent, splitter.Background);
		});
	}

	[Fact]
	public void StoredRatio_ChangesBothColumns_AndResetReturnsToLegacy()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("host-ratio");
		TestApp.Run(() =>
		{
			// 预置一条存储比例 0.5:可用宽 = 900 - 18 = 882 ⇒ 两栏各 441
			Assert.True(SplitRatioStore.Write("version-manage", "nav", 0.5));
			PageLayoutHost host = VersionHost();
			LayoutIt(host);

			Assert.True(host.HasStoredRatio);
			Assert.True(host.ColumnDefinitions[0].Width.IsStar);
			Assert.Equal(0.5, host.ColumnDefinitions[0].Width.Value);
			Assert.Equal(0.5, host.ColumnDefinitions[2].Width.Value);
			Assert.Equal(441.0, host.ColumnDefinitions[0].ActualWidth, 6);
			Assert.Equal(441.0, host.ColumnDefinitions[2].ActualWidth, 6);
			// 分隔条跟着左栏右边界走
			Assert.Equal(441.0, host.SplitterBounds.X, 6);
			Assert.Equal(0.5, host.CurrentRatio, 12);

			// 双击分隔条那条路 = ResetToDefault:删键 + 回现状列定义
			host.ResetToDefaultForTest();
			LayoutIt(host);
			Assert.False(host.HasStoredRatio);
			Assert.Null(SplitRatioStore.Read("version-manage", "nav"));
			Assert.Equal(272.0, host.ColumnDefinitions[0].ActualWidth, 6);
			Assert.Equal(610.0, host.ColumnDefinitions[2].ActualWidth, 6);
		});
	}

	[Fact]
	public void SplitterDragCompleted_PersistsRatio_AndColumnsFollow()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("host-drag");
		TestApp.Run(() =>
		{
			PageLayoutHost host = VersionHost();
			LayoutIt(host);
			GridSplitter splitter = host.Children.OfType<GridSplitter>().Single();

			// 真事件链:GridSplitter.DragStarted → 拖动改变列宽 → GridSplitter.DragCompleted。
			// 中间那步用直接写列定义代替鼠标位移(Avalonia 无头下不模拟输入设备),
			// 但"松手 → CommitDrag → 写 v2"这段是**产品代码里同一段**(OnDragCompleted)。
			splitter.RaiseEvent(new VectorEventArgs { RoutedEvent = GridSplitter.DragStartedEvent });
			Assert.True(host.HoverLineVisible);          // 拖动中细线可见
			host.ColumnDefinitions[0].Width = new GridLength(400.0);
			LayoutIt(host);
			splitter.RaiseEvent(new VectorEventArgs { RoutedEvent = GridSplitter.DragCompletedEvent });

			// 1) 落了盘:比例 = 400 / (900-18)
			Assert.True(SplitRatioStore.IsStored("version-manage", "nav"));
			Assert.Equal(400.0 / 882.0, SplitRatioStore.Read("version-manage", "nav")!.Value, 12);
			// 2) 当场切到星号比例,且左栏像素宽仍是 400(比例与拖动结果自洽)
			Assert.True(host.HasStoredRatio);
			LayoutIt(host);
			Assert.Equal(400.0, host.ColumnDefinitions[0].ActualWidth, 6);
			Assert.Equal(482.0, host.ColumnDefinitions[2].ActualWidth, 6);
			Assert.False(host.HoverLineVisible);         // 松手后细线收回(静止零像素变化)
		});
	}

	[Fact]
	public void ZeroDisplacementClick_DoesNotPersist()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("host-click");
		TestApp.Run(() =>
		{
			// 单击分隔条也会走一次 0 位移的拖拽:不该把页面随手切成比例模式(Windows :1017)
			PageLayoutHost host = VersionHost();
			LayoutIt(host);
			GridSplitter splitter = host.Children.OfType<GridSplitter>().Single();
			splitter.RaiseEvent(new VectorEventArgs { RoutedEvent = GridSplitter.DragStartedEvent });
			splitter.RaiseEvent(new VectorEventArgs { RoutedEvent = GridSplitter.DragCompletedEvent });

			Assert.False(host.HasStoredRatio);
			Assert.Null(SplitRatioStore.Read("version-manage", "nav"));
		});
	}

	[Fact]
	public void CollapsingLeft_TakesSplitterAndGapToZero_AndNeverWrites()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("host-collapse");
		TestApp.Run(() =>
		{
			PageLayoutHost host = VersionHost();
			LayoutIt(host);
			Assert.True(host.SplitterVisible);
			Assert.Equal(18.0, host.GapColumnWidth);

			host.SetLeftVisible(false);
			LayoutIt(host);
			Assert.False(host.LeftVisible);
			Assert.False(host.SplitterVisible);          // 拖动入口不存在
			Assert.Equal(0.0, host.GapColumnWidth);      // 留白列收 0
			Assert.Equal(0.0, host.ColumnDefinitions[0].ActualWidth, 6);
			Assert.Equal(900.0, host.ColumnDefinitions[2].ActualWidth, 6);   // 右栏独占整宽
			Assert.False(host.HasStoredRatio);           // 折叠形态不写比例
			Assert.Null(SplitRatioStore.Read("version-manage", "nav"));

			host.SetLeftVisible(true);
			LayoutIt(host);
			Assert.True(host.SplitterVisible);
			Assert.Equal(272.0, host.ColumnDefinitions[0].ActualWidth, 6);
		});
	}

	[Fact]
	public void CollapsedHost_RefusesReset()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("host-collapse-reset");
		TestApp.Run(() =>
		{
			Assert.True(SplitRatioStore.Write("version-manage", "nav", 0.5));
			PageLayoutHost host = VersionHost();
			LayoutIt(host);
			host.SetLeftVisible(false);
			// 折叠态下复位是空操作(Windows :814:LeftVisible/_rightVisible 为假直接返回)
			host.ResetToDefaultForTest();
			Assert.Equal(0.5, SplitRatioStore.Read("version-manage", "nav")!.Value, 12);
		});
	}

	[Fact]
	public void SettingsHost_DefaultGeometry_KeepsViewContentAt186()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("host-settings");
		TestApp.Run(() =>
		{
			// 还原 SettingsPage 的接线:左导航 168px + 18px 留白 + 视图(视图左移 18px 抵消)
			var nav = new Border { Width = 156.0, Margin = new Thickness(12.0, 16.0, 0.0, 12.0) };
			var view = new Border { };
			view.Margin = new Thickness(-18.0, 0.0, 0.0, 0.0);
			var host = new PageLayoutHost("settings", "nav", new GridLength(168.0), new GridLength(1.0, GridUnitType.Star));
			host.SetLeft(nav);
			host.SetRight(view);
			LayoutIt(host);

			// 导航列 168;留白列 18;视图列的**内容盒**从 x=168 起(186 - 18)= 与改动前同一 x
			Assert.Equal(168.0, host.ColumnDefinitions[0].ActualWidth, 6);
			Assert.Equal(18.0, host.ColumnDefinitions[1].ActualWidth, 6);
			Assert.Equal(168.0, view.Bounds.X, 6);
			Assert.Equal(732.0, view.Bounds.Width, 6);           // 714 + 18(负外边距把这一列吃回来)
			Assert.Equal(168.0 + 18.0, view.Bounds.X + 18.0, 6); // 视图内容(其内部 18px pad)仍从 186 起
			Assert.Equal(168.0, host.SplitterBounds.X, 6);       // 分隔条压在 168..186 这段留白上
			Assert.Equal(18.0, host.SplitterBounds.Width, 6);
		});
	}

	[Fact]
	public void AccountHost_AutoLeftColumn_UsesCardWidthPlusSiteGap()
	{
		using LayoutStoreDir dir = new LayoutStoreDir("host-account");
		TestApp.Run(() =>
		{
			// AccountPage 的现状:头像卡 Width=240,原来靠自己的 14px 右外边距留白;
			// 接线后该 14px 由分隔条承担 ⇒ 列表起点仍是 x=254,左栏仍是 240
			var card = new Border { Width = 240.0, VerticalAlignment = VerticalAlignment.Top };
			var list = new Border { };
			var host = new PageLayoutHost("account", "hero", GridLength.Auto, new GridLength(1.0, GridUnitType.Star));
			host.SetLeft(card);
			host.SetRight(list);
			LayoutIt(host);

			Assert.Equal(240.0, host.ColumnDefinitions[0].ActualWidth, 6);
			Assert.Equal(14.0, host.ColumnDefinitions[1].ActualWidth, 6);     // #9 Gap = 14
			Assert.Equal(240.0, host.SplitterBounds.X, 6);                    // 分隔条占住 240..254
			Assert.Equal(14.0, host.SplitterBounds.Width, 6);
			Assert.Equal(646.0, host.ColumnDefinitions[2].ActualWidth, 6);   // 900 - 240 - 14
			Assert.Equal(254.0, list.Bounds.X, 6);
		});
	}
}
