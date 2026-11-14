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
/// L4 分栏比例持久化的回归闸门(设计 §2.3 的 13 处清单 + §4.E 的实现约定):
///   清单与默认值      → CatalogHasThirteenSites / TwoColumnSitesAreTheOnesWithGaps / DefaultPixelsMatchTodaysLayout
///   默认比例 = 现状   → DefaultRatioOfLibraryMatchesStarWeights / ResolveFallsBackToTheCurrentValue
///   唯一写入口(v2)   → WriteStoresRatioIntoV2SplitsOnly / SaveNeverWritesSplitsIntoTheFrozenV1File
///   钳位 0.15..0.85   → WriteClampsRatioIntoContractRange / ClampForWidthsHonoursTodaysBounds / CommitDragWritesTheRatioUnderTodaysBounds
///   复位              → ClearRemovesOnlyThatSplit / ClearPageRemovesWholePage
///   内嵌态不写比例    → EmbeddedVersionManageSiteIsReadOnly
///   容错              → CorruptV2FileIsTreatedAsNothingStored / UnknownSiteFallsBackWithoutThrowing
///   网格列数(第 10 处) → PageColumnsOnlyHonoursGridModePages
///   几何换算          → RatioFromPixelsRoundTrips
/// 所有落盘用例都走 DirOverride 临时目录(同时给 PageLayoutStore 注入同一目录),绝不碰用户真实数据目录。
/// 本文件刻意只用纯数据/纯逻辑类型(SplitCatalog / SplitSite / SplitRatioStore):宿主是 net8,
/// 一旦碰到 PageLayoutHost(WPF)就会因为加载不到 net48 的 PresentationFramework 而炸。
/// </summary>
public class LayoutSplitTests
{
	private readonly string _tmp = Path.Combine(Path.GetTempPath(), "tipcl_split_tests_" + Guid.NewGuid().ToString("N"));

	private string V2Path()
	{
		return Path.Combine(_tmp, LayoutStoreV2.FileName);
	}

	private string V1Path()
	{
		return Path.Combine(_tmp, LayoutStoreV2.V1FileName);
	}

	// 两个存储必须指到同一目录:否则 v2 的写通会落到用户真实数据目录
	private void UseDir()
	{
		Directory.CreateDirectory(_tmp);
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

	// ————— §2.3 清单:13 处逐条有交代 —————

	[Fact]
	public void CatalogHasThirteenSitesWithIndexesAndUniqueKeys()
	{
		Assert.Equal(13, SplitCatalog.All.Length);
		int[] indexes = SplitCatalog.All.Select((SplitSite s) => s.Index).OrderBy((int i) => i).ToArray();
		Assert.Equal(Enumerable.Range(1, 13).ToArray(), indexes);
		// pageId + splitId 唯一(内嵌形态不在这 13 条里)
		Assert.Equal(13, SplitCatalog.All.Select((SplitSite s) => s.PageId + "/" + s.SplitId).Distinct().Count());
		// 每一处都有形态与说明:报告里的"完成度表"与代码同一个口径
		foreach (SplitSite site in SplitCatalog.All)
		{
			Assert.False(string.IsNullOrWhiteSpace(site.Kind));
			Assert.False(string.IsNullOrWhiteSpace(site.Note));
			Assert.False(string.IsNullOrWhiteSpace(site.PageId));
			Assert.False(string.IsNullOrWhiteSpace(site.SplitId));
		}
	}

	[Fact]
	public void TwoColumnSitesAreTheOnesWithGaps()
	{
		// 设计 §2.3 的 13 处里,只有 6 处真能插左右分隔条(留着现状留白);其余是纵向行/列表行/网格列数
		Assert.Equal(6, SplitCatalog.TwoColumnCount());
		foreach (SplitSite site in SplitCatalog.All)
		{
			Assert.Equal(site.IsTwoColumn, site.Gap > 0.0);
			// 非两栏的形态必须写明"为什么这处不插分隔条"
			if (!site.IsTwoColumn)
			{
				Assert.True(site.Note.Length > 10, site.PageId + " 应写明不接入的原因");
			}
		}
		// 插了分隔条的 6 处 = L4 实际接线的 6 个 PageLayoutHost 站点
		string[] wired = SplitCatalog.All.Where((SplitSite s) => s.IsTwoColumn).Select((SplitSite s) => s.PageId + "/" + s.SplitId).OrderBy((string x) => x, StringComparer.Ordinal).ToArray();
		Assert.Equal(new string[6] { "account/hero", "ai/history", "feedback/side", "library/folders", "settings/nav", "version-manage/nav" }, wired);
	}

	[Fact]
	public void DefaultPixelsMatchTodaysLayout()
	{
		// §2.3 表里逐行的现状宽度/留白:改这些数就等于改了"默认 = 现状",必须同时改页面与报告
		SplitSite library = SplitCatalog.Find("library", "folders")!;
		Assert.Equal(18.0, library.Gap);
		Assert.Equal(300.0, library.LeftMin);
		Assert.Equal(0.0, library.DefaultPixels);

		SplitSite settings = SplitCatalog.Find("settings", "nav")!;
		Assert.Equal(168.0, settings.DefaultPixels);
		Assert.Equal(18.0, settings.Gap);

		SplitSite versionManage = SplitCatalog.Find("version-manage", "nav")!;
		Assert.Equal(272.0, versionManage.DefaultPixels);
		Assert.Equal(18.0, versionManage.Gap);

		SplitSite ai = SplitCatalog.Find("ai", "history")!;
		Assert.Equal(220.0, ai.DefaultPixels);
		Assert.Equal(10.0, ai.Gap);

		SplitSite feedback = SplitCatalog.Find("feedback", "side")!;
		Assert.Equal(360.0, feedback.DefaultPixels);
		Assert.Equal("right", feedback.PixelsSide);
		Assert.Equal(360.0, feedback.RightMin);
		Assert.Equal(14.0, feedback.Gap);

		SplitSite account = SplitCatalog.Find("account", "hero")!;
		Assert.Equal(240.0, account.DefaultPixels);
		Assert.Equal(14.0, account.Gap);

		// 明确"不接入"的 7 处:形态不是左右两栏(行/列表行/网格列数/限高/单列)
		Assert.Equal(SplitKind.Rows, SplitCatalog.Find("instance:detail", "seg")!.Kind);
		Assert.Equal(SplitKind.HeightClamp, SplitCatalog.Find("launch", "hero")!.Kind);
		Assert.Equal(SplitKind.ListRow, SplitCatalog.Find("download", "row")!.Kind);
		Assert.Equal(SplitKind.GridCols, SplitCatalog.Find("plugins", "cols")!.Kind);
		Assert.Equal(SplitKind.Rows, SplitCatalog.Find("settings:about", "about")!.Kind);
		Assert.Equal(SplitKind.SingleCol, SplitCatalog.Find("catalog", "list")!.Kind);
		Assert.Equal(SplitKind.Rows, SplitCatalog.Find("install", "frame")!.Kind);
	}

	[Fact]
	public void DefaultRatioOfLibraryMatchesStarWeights()
	{
		// 现状 "*,18,2*" → 左栏占星号 1/3;这就是 library 的"默认比例 = 现状值"
		Assert.Equal(1.0 / 3.0, SplitCatalog.Find("library", "folders")!.DefaultRatio, 6);
		// 按像素定义的那几处没有单一比例(0),默认值由运行时"现状像素 ÷ 可用宽"现算
		Assert.Equal(0.0, SplitCatalog.Find("settings", "nav")!.DefaultRatio);
		Assert.Equal(0.0, SplitCatalog.Find("ai", "history")!.DefaultRatio);
		Assert.Equal(0.0, SplitCatalog.Find("account", "hero")!.DefaultRatio);
	}

	// ————— 唯一写入口:只进 v2 的 splits,不碰 v1 —————

	[Fact]
	public void WriteStoresRatioIntoV2SplitsOnly()
	{
		UseDir();
		try
		{
			Assert.True(SplitRatioStore.Write("library", "folders", 0.42));
			Assert.True(File.Exists(V2Path()));

			using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(V2Path()));
			JsonElement splits = doc.RootElement.GetProperty("layout").GetProperty("splits");
			Assert.Equal(0.42, splits.GetProperty("library").GetProperty("folders").GetDouble(), 6);
			// 信封仍然是 v2 契约
			Assert.Equal(2, doc.RootElement.GetProperty("version").GetInt32());
			Assert.Equal("tipcl-layout", doc.RootElement.GetProperty("kind").GetString());

			// 读回一致,且 v1 文件里绝不出现新数据(D1:v1 只承载 id/hidden)
			Assert.Equal(0.42, SplitRatioStore.Read("library", "folders")!.Value, 6);
			Assert.True(File.Exists(V1Path()));
			string v1 = File.ReadAllText(V1Path());
			Assert.DoesNotContain("splits", v1);
			Assert.DoesNotContain("ratio", v1);
			Assert.DoesNotContain("folders", v1);
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void WriteKeepsOtherSplitsAndPagesIntact()
	{
		UseDir();
		try
		{
			Assert.True(SplitRatioStore.Write("library", "folders", 0.30));
			Assert.True(SplitRatioStore.Write("settings", "nav", 0.25));
			Assert.True(SplitRatioStore.Write("settings", "side", 0.70));
			// 逐处复位只动一处,同页其它比例与别页比例都不受影响
			Assert.True(SplitRatioStore.Clear("settings", "nav"));
			Assert.Null(SplitRatioStore.Read("settings", "nav"));
			Assert.Equal(0.70, SplitRatioStore.Read("settings", "side")!.Value, 6);
			Assert.Equal(0.30, SplitRatioStore.Read("library", "folders")!.Value, 6);
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void WriteClampsRatioIntoContractRange()
	{
		UseDir();
		try
		{
			// 契约 §3.2:splits 的比例夹 0.15..0.85(Sanitize 统一处理,写之前先夹一次保证界面与落盘一致)
			Assert.True(SplitRatioStore.Write("library", "folders", 0.02));
			Assert.Equal(LayoutSchema.RatioMin, SplitRatioStore.Read("library", "folders")!.Value, 6);
			Assert.True(SplitRatioStore.Write("library", "folders", 9.0));
			Assert.Equal(LayoutSchema.RatioMax, SplitRatioStore.Read("library", "folders")!.Value, 6);
			// 非法数字按默认值处理,不写 NaN 进文件
			Assert.Equal(SplitRatioStore.DefaultRatio, SplitRatioStore.ClampRatio(double.NaN), 6);
			Assert.Equal(SplitRatioStore.DefaultRatio, SplitRatioStore.ClampRatio(double.PositiveInfinity), 6);
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void ClearPageRemovesWholePage()
	{
		UseDir();
		try
		{
			Assert.True(SplitRatioStore.Write("library", "folders", 0.30));
			Assert.True(SplitRatioStore.Write("library", "list", 0.40));
			Assert.True(SplitRatioStore.Write("ai", "history", 0.50));
			Assert.Equal(2, SplitRatioStore.ClearPage("library"));
			Assert.Null(SplitRatioStore.Read("library", "folders"));
			Assert.Null(SplitRatioStore.Read("library", "list"));
			Assert.Equal(0.50, SplitRatioStore.Read("ai", "history")!.Value, 6);
			// 没有比例的页再复位一次:什么也不做,不报错
			Assert.Equal(0, SplitRatioStore.ClearPage("library"));
			Assert.Equal(0, SplitRatioStore.ClearPage(""));
			// 复位不存在的分栏:返回 false,不抛
			Assert.False(SplitRatioStore.Clear("settings", "nav"));
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void EmbeddedVersionManageSiteIsReadOnly()
	{
		UseDir();
		try
		{
			// §4.E:实例详情第 5 段内嵌的版本管理必须"隐藏分隔条且不写比例"
			SplitSite embedded = SplitCatalog.Embedded;
			Assert.Equal("instance:settings", embedded.PageId);
			Assert.False(embedded.Writable);
			Assert.False(SplitRatioStore.Write(embedded.PageId, embedded.SplitId, 0.5));
			Assert.Null(SplitRatioStore.Read(embedded.PageId, embedded.SplitId));
			// 顶级版本管理页照常可写(两者不是同一份比例)
			Assert.True(SplitCatalog.Find("version-manage", "nav")!.Writable);
			Assert.True(SplitRatioStore.Write("version-manage", "nav", 0.35));
			Assert.Equal(0.35, SplitRatioStore.Read("version-manage", "nav")!.Value, 6);
		}
		finally
		{
			Release();
		}
	}

	// ————— 容错:读不到就当"没存过",界面回落到现状 —————

	[Fact]
	public void CorruptV2FileIsTreatedAsNothingStored()
	{
		UseDir();
		try
		{
			Assert.True(SplitRatioStore.Write("library", "folders", 0.30));
			File.WriteAllText(V2Path(), "{\"version\":2,\"kind\":\"tipcl-layout\",\"layout\":{\"splits\":{\"lib");
			LayoutStoreV2.ResetCache();
			// 截断的 v2:不抛,当作"没存过比例"(页面按现状列定义渲染)
			Assert.Null(SplitRatioStore.Read("library", "folders"));
			Assert.False(SplitRatioStore.IsStored("library", "folders"));
			// 此刻写一次会重建一份完整的 v2(不静默清空用户其它页面的布局)
			Assert.True(SplitRatioStore.Write("library", "folders", 0.33));
			Assert.Equal(0.33, SplitRatioStore.Read("library", "folders")!.Value, 6);
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void UnknownSiteAndEmptyKeysFallBackWithoutThrowing()
	{
		UseDir();
		try
		{
			Assert.Null(SplitCatalog.Find("no-such-page", "no-such-split"));
			Assert.Null(SplitCatalog.Find("", ""));
			Assert.Null(SplitCatalog.Find(null, null));
			Assert.Null(SplitRatioStore.Read("no-such-page", "no-such-split"));
			Assert.False(SplitRatioStore.IsStored("no-such-page", "no-such-split"));
			// 未登记的分栏照样能存(未来新增页不会因为没登记就不能用)
			Assert.True(SplitRatioStore.Write("future-page", "left", 0.4));
			Assert.Equal(0.4, SplitRatioStore.Read("future-page", "left")!.Value, 6);
			// 空 key 直接拒绝,不写脏数据
			Assert.False(SplitRatioStore.Write("", "left", 0.4));
			Assert.False(SplitRatioStore.Write("future-page", "", 0.4));
			Assert.Null(SplitRatioStore.Read("", ""));
		}
		finally
		{
			Release();
		}
	}

	// ————— 默认值解析 / 几何换算(默认比例 = 现状的可测形式) —————

	[Fact]
	public void ResolveFallsBackToTheCurrentValue()
	{
		UseDir();
		try
		{
			// 没存过:library 的默认 = 现状星号 1:2 → 1/3;没有现状比例的登记项用契约默认 0.5
			Assert.Equal(1.0 / 3.0, SplitRatioStore.Resolve("library", "folders"), 6);
			Assert.Equal(SplitRatioStore.DefaultRatio, SplitRatioStore.Resolve("settings", "nav"), 6);
			Assert.Equal(SplitRatioStore.DefaultRatio, SplitRatioStore.Resolve("no-such-page", "x"), 6);
			// 存过之后以存储值为准
			Assert.True(SplitRatioStore.Write("library", "folders", 0.6));
			Assert.Equal(0.6, SplitRatioStore.Resolve("library", "folders"), 6);
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void RatioFromPixelsRoundTrips()
	{
		// 现状像素宽 ÷ 可用宽(可用宽 = 外壳宽 - 分隔条)= 比例;拖动结束后就是这么算的
		Assert.Equal(1.0 / 3.0, SplitRatioStore.RatioFromPixels(300.0, 900.0), 6);
		Assert.Equal(300.0, SplitRatioStore.PixelsFromRatio(1.0 / 3.0, 900.0), 6);
		// 越界一律夹取(可用宽异常时回落到默认比例,不产生 NaN)
		Assert.Equal(SplitRatioStore.DefaultRatio, SplitRatioStore.RatioFromPixels(500.0, 0.0), 6);
		Assert.Equal(0.0, SplitRatioStore.PixelsFromRatio(0.5, 0.0), 6);
		Assert.Equal(LayoutSchema.RatioMin, SplitRatioStore.RatioFromPixels(1.0, 900.0), 6);
	}

	[Fact]
	public void CommitDragWritesTheRatioUnderTodaysBounds()
	{
		UseDir();
		try
		{
			// 拖到左栏 300px(可用宽 900)→ 比例 1/3 落进 v2,并作为返回值交给界面
			Assert.Equal(1.0 / 3.0, SplitRatioStore.CommitDrag("library", "folders", 300.0, 900.0), 6);
			Assert.Equal(1.0 / 3.0, SplitRatioStore.Read("library", "folders")!.Value, 6);

			// 拖过头(左栏 100px < 现状卡片 MinWidth 300)→ 按现状边界夹回 1/3
			Assert.Equal(1.0 / 3.0, SplitRatioStore.CommitDrag("library", "folders", 100.0, 900.0), 6);
			Assert.Equal(1.0 / 3.0, SplitRatioStore.Read("library", "folders")!.Value, 6);

			// 宽度取不到(页面还没布局完 / 外壳收起来了)→ 不写,返回 NaN,界面保持现状
			Assert.True(double.IsNaN(SplitRatioStore.CommitDrag("library", "folders", 0.0, 900.0)));
			Assert.True(double.IsNaN(SplitRatioStore.CommitDrag("library", "folders", 300.0, 0.0)));
			Assert.Equal(1.0 / 3.0, SplitRatioStore.Read("library", "folders")!.Value, 6);

			// 内嵌态(不可写)→ 不写
			Assert.True(double.IsNaN(SplitRatioStore.CommitDrag("instance:settings", "vm", 300.0, 900.0)));
			Assert.Null(SplitRatioStore.Read("instance:settings", "vm"));
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void ClampForWidthsHonoursTodaysBounds()
	{
		SplitSite library = SplitCatalog.Find("library", "folders")!;
		// 可用宽 900:左栏现状下限 300 → 比例不能小于 1/3(否则左卡会溢出压到右栏)
		Assert.Equal(1.0 / 3.0, SplitRatioStore.ClampRatioForWidths(0.1, 900.0, library), 6);
		Assert.Equal(0.5, SplitRatioStore.ClampRatioForWidths(0.5, 900.0, library), 6);

		SplitSite feedback = SplitCatalog.Find("feedback", "side")!;
		// 反馈页右栏现状 360:可用宽 900 时左栏比例最大 (900-360)/900
		Assert.Equal((900.0 - 360.0) / 900.0, SplitRatioStore.ClampRatioForWidths(0.9, 900.0, feedback), 6);

		// 窄窗:可用宽 250 撑不下左栏下限 300 → 边界自相矛盾,退回合同夹取(与现状"越界溢出"行为一致)
		Assert.Equal(LayoutSchema.RatioMax, SplitRatioStore.ClampRatioForWidths(0.9, 250.0, library), 6);
		// 没登记的分栏 / 拿不到实际宽度:只夹合同 0.15..0.85
		Assert.Equal(0.6, SplitRatioStore.ClampRatioForWidths(0.6, 900.0, null), 6);
		Assert.Equal(0.6, SplitRatioStore.ClampRatioForWidths(0.6, 0.0, library), 6);
	}

	// ————— §2.3 第 10 处:插件页已安装网格列数 —————

	[Fact]
	public void PageColumnsOnlyHonoursGridModePages()
	{
		UseDir();
		try
		{
			// 没有 pages["plugins"] → 现状 3 列
			Assert.Equal(3, SplitRatioStore.PageColumns("plugins", 3, 1, 6));

			// 页面没声明 grid 模式(默认 stack)→ 仍然用现状列数,避免 L2 写其它字段时误改插件页
			LayoutV2 doc = LayoutStoreV2.Load();
			doc.Layout.Pages["plugins"] = new PageLayoutV2
			{
				Mode = LayoutSchema.ModeStack,
				Cols = 12
			};
			LayoutStoreV2.Save(doc);
			Assert.Equal(3, SplitRatioStore.PageColumns("plugins", 3, 1, 6));

			// 声明 grid 模式后才生效,并夹到 [min, max]
			doc = LayoutStoreV2.Load();
			doc.Layout.Pages["plugins"]!.Mode = LayoutSchema.ModeGrid;
			doc.Layout.Pages["plugins"]!.Cols = 6;
			LayoutStoreV2.Save(doc);
			Assert.Equal(6, SplitRatioStore.PageColumns("plugins", 3, 1, 6));

			doc = LayoutStoreV2.Load();
			doc.Layout.Pages["plugins"]!.Cols = 12;
			LayoutStoreV2.Save(doc);
			Assert.Equal(6, SplitRatioStore.PageColumns("plugins", 3, 1, 6));
		}
		finally
		{
			Release();
		}
	}
}
