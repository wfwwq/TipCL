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
/// L5(设计 §4.C 整机方案 + §4.G JSON 编辑器)的回归闸门:
///   方案增删改查      → CreateFromCurrentSnapshotsIndependently / RenameKeepsOrderAndFollowsActive / DuplicateAndDelete
///   名字校验          → CreateRejectsBadNames
///   应用 = 原子替换   → ApplyReplacesWholeLayoutAtomically / ApplySnapshotResetsActiveProfile / ApplyMissingProfileReportsChinese
///   导入 v1 页面布局  → MergeV1PagesOverwritesOnlyThosePages
///   导出兼容 v1       → ExportedV1TextStillParsesWithFrozenV1Parser
///   JSON 编辑器校验   → InspectAcceptsOwnExport / InspectReportsKindAndVersion / InspectReportsFieldPaths / InspectRejectsBrokenText
///   JSON 应用才落盘   → ApplyWritesOnlyWhenThereIsNoError
/// 全部用 DirOverride 临时目录(同时给 PageLayoutStore 注入同一目录),绝不碰用户真实数据目录;
/// 本文件只用纯逻辑类型(LayoutProfiles / LayoutJsonEditor / LayoutStoreV2),不碰 WPF 类型。
/// </summary>
public class LayoutProfileTests
{
	private readonly string _tmp = Path.Combine(Path.GetTempPath(), "tipcl_profile_tests_" + Guid.NewGuid().ToString("N"));

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

	// 造一份"当前布局":一页 + 一处分栏比例(方案快照要能带过去)
	private static void SeedCurrentLayout(double ratio)
	{
		LayoutV2 doc = LayoutStoreV2.Load();
		PageLayoutV2 page = new PageLayoutV2();
		page.Blocks.Add(new BlockLayout { Id = "filters", Hidden = false });
		page.Blocks.Add(new BlockLayout { Id = "list", Hidden = true });
		doc.Layout.Pages["download"] = page;
		SplitState split = new SplitState();
		split.Ratios["folders"] = ratio;
		doc.Layout.Splits["library"] = split;
		LayoutStoreV2.Save(doc);
	}

	private static string V2Text()
	{
		return File.ReadAllText(LayoutStoreV2.FilePath);
	}

	// ————— 方案:新建(以当前为基线) —————

	[Fact]
	public void CreateFromCurrentSnapshotsIndependently()
	{
		UseDir();
		try
		{
			SeedCurrentLayout(0.4);
			Assert.Equal("", LayoutProfiles.Create("我的布局", fromCurrent: true));
			Assert.Equal("我的布局", LayoutProfiles.ActiveName());
			Assert.Equal(new string[1] { "我的布局" }, LayoutProfiles.Names().ToArray());

			LayoutBody? snapshot = LayoutProfiles.Snapshot("我的布局");
			Assert.NotNull(snapshot);
			Assert.Equal(2, snapshot!.Pages["download"].Blocks.Count);
			Assert.True(snapshot.Pages["download"].Blocks[1].Hidden);
			Assert.Equal(0.4, snapshot.Splits["library"].Ratios["folders"], 6);

			// 快照是深拷贝:之后改当前布局不会污染方案
			LayoutV2 doc = LayoutStoreV2.Load();
			doc.Layout.Pages.Remove("download");
			doc.Layout.Splits.Clear();
			LayoutStoreV2.Save(doc);
			Assert.Equal(2, LayoutProfiles.Snapshot("我的布局")!.Pages["download"].Blocks.Count);
			Assert.Equal(0.4, LayoutProfiles.Snapshot("我的布局")!.Splits["library"].Ratios["folders"], 6);
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void CreateRejectsBadNames()
	{
		UseDir();
		try
		{
			Assert.NotEqual("", LayoutProfiles.Create("", fromCurrent: true));
			Assert.NotEqual("", LayoutProfiles.Create("   ", fromCurrent: true));
			Assert.NotEqual("", LayoutProfiles.Create(new string('长', LayoutProfiles.ProfileNameMax + 1), fromCurrent: true));
			Assert.Equal("", LayoutProfiles.Create("A", fromCurrent: true));
			// 重名(含首尾空格归一后重名)
			Assert.NotEqual("", LayoutProfiles.Create("A", fromCurrent: true));
			Assert.NotEqual("", LayoutProfiles.Create(" A ", fromCurrent: true));
			Assert.Equal(1, LayoutProfiles.Count());
			// 名字两端的空格会被去掉
			Assert.Equal("", LayoutProfiles.Create("  规划版  ", fromCurrent: true));
			Assert.Contains("规划版", LayoutProfiles.Names());
		}
		finally
		{
			Release();
		}
	}

	// ————— 应用 = 原子替换 layout —————

	[Fact]
	public void ApplyReplacesWholeLayoutAtomically()
	{
		UseDir();
		try
		{
			SeedCurrentLayout(0.4);
			Assert.Equal("", LayoutProfiles.Create("基线", fromCurrent: true));

			// 之后把当前布局改成另一套(多一页、去掉分栏)
			LayoutV2 doc = LayoutStoreV2.Load();
			doc.Layout.Pages["launch"] = new PageLayoutV2();
			doc.Layout.Splits.Clear();
			LayoutStoreV2.Save(doc);
			Assert.Equal(2, LayoutStoreV2.Load().Layout.Pages.Count);

			// 应用方案:整块 layout 换回快照(多出来的那页也一起消失 = 原子替换,不是合并)
			Assert.Equal("", LayoutProfiles.Apply("基线"));
			LayoutV2 restored = LayoutStoreV2.Load();
			Assert.Single(restored.Layout.Pages);
			Assert.True(restored.Layout.Pages.ContainsKey("download"));
			Assert.Equal(0.4, restored.Layout.Splits["library"].Ratios["folders"], 6);
			Assert.Equal("基线", restored.ActiveProfile);

			// v1 由 v2 单向写通:应用后 v1 里能看到 download 的 {id,hidden},但没有任何新字段(D1)
			string v1 = File.ReadAllText(LayoutStoreV2.V1FilePath);
			Assert.Contains("download", v1);
			Assert.DoesNotContain("splits", v1);
			Assert.DoesNotContain("profiles", v1);
			// PageLayoutStore 按目录进程内只缓存一次(L0 记录的既有行为)→ 用 Reload() 清缓存再读
			PageLayoutStore.Reload();
			IReadOnlyDictionary<string, PageLayoutDoc> v1Pages = PageLayoutStore.LoadAll();
			Assert.Equal(new string[2] { "filters", "list" }, v1Pages["download"].Blocks.Select((BlockState b) => b.Id).ToArray());
			Assert.True(v1Pages["download"].Blocks[1].Hidden);
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void ApplySnapshotResetsActiveProfile()
	{
		UseDir();
		try
		{
			SeedCurrentLayout(0.3);
			Assert.Equal("", LayoutProfiles.Create("A", fromCurrent: true));
			// 导入整机方案 = 直接替换当前布局 → 回到直接编辑态
			LayoutBody body = new LayoutBody();
			body.Pages["plugins"] = new PageLayoutV2();
			Assert.Equal("", LayoutProfiles.ApplySnapshot(body));
			LayoutV2 doc = LayoutStoreV2.Load();
			Assert.Equal("", doc.ActiveProfile);
			Assert.Single(doc.Layout.Pages);
			Assert.True(doc.Layout.Pages.ContainsKey("plugins"));
			// 方案本身没被删
			Assert.Equal(1, LayoutProfiles.Count());
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void ApplyMissingProfileReportsChinese()
	{
		UseDir();
		try
		{
			string error = LayoutProfiles.Apply("不存在的方案");
			Assert.NotEqual("", error);
			Assert.Contains("不存在", error);
			Assert.NotEqual("", LayoutProfiles.Rename("不存在的方案", "新名"));
			Assert.NotEqual("", LayoutProfiles.Delete("不存在的方案"));
			Assert.NotEqual("", LayoutProfiles.Duplicate("不存在的方案", "新名"));
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void RenameKeepsOrderAndFollowsActive()
	{
		UseDir();
		try
		{
			Assert.Equal("", LayoutProfiles.Create("A", fromCurrent: true));
			Assert.Equal("", LayoutProfiles.Create("B", fromCurrent: true));
			Assert.Equal("B", LayoutProfiles.ActiveName());
			Assert.Equal("", LayoutProfiles.Rename("A", "A2"));
			Assert.Equal(new string[2] { "A2", "B" }, LayoutProfiles.Names().ToArray());
			// 重命名当前激活的方案:active_profile 跟着改
			Assert.Equal("", LayoutProfiles.Rename("B", "B2"));
			Assert.Equal("B2", LayoutProfiles.ActiveName());
			Assert.Equal(new string[2] { "A2", "B2" }, LayoutProfiles.Names().ToArray());
			// 重名 / 空名 / 超长都要拒绝
			Assert.NotEqual("", LayoutProfiles.Rename("A2", "B2"));
			Assert.NotEqual("", LayoutProfiles.Rename("A2", " "));
			Assert.NotEqual("", LayoutProfiles.Rename("A2", new string('长', LayoutProfiles.ProfileNameMax + 1)));
			Assert.Equal(new string[2] { "A2", "B2" }, LayoutProfiles.Names().ToArray());
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void DuplicateAndDelete()
	{
		UseDir();
		try
		{
			SeedCurrentLayout(0.5);
			Assert.Equal("", LayoutProfiles.Create("A", fromCurrent: true));
			Assert.Equal("", LayoutProfiles.Duplicate("A", "A 副本"));
			Assert.Equal(2, LayoutProfiles.Count());
			Assert.Equal(0.5, LayoutProfiles.Snapshot("A 副本")!.Splits["library"].Ratios["folders"], 6);
			// 复制出来的是独立快照
			Assert.Equal("", LayoutProfiles.Duplicate("A 副本", "A2"));
			Assert.Equal(3, LayoutProfiles.Count());

			// 删掉当前激活的方案 → active_profile 回空(直接编辑态)
			Assert.Equal("A", LayoutProfiles.ActiveName());
			Assert.Equal("", LayoutProfiles.Delete("A"));
			Assert.Equal("", LayoutProfiles.ActiveName());
			Assert.Equal(new string[2] { "A 副本", "A2" }, LayoutProfiles.Names().ToArray());
		}
		finally
		{
			Release();
		}
	}

	// ————— 导入兼容 v1 的页面布局 —————

	[Fact]
	public void MergeV1PagesOverwritesOnlyThosePages()
	{
		UseDir();
		try
		{
			// 当前文档:download(3 块) + plugins(1 块)
			LayoutV2 doc = LayoutStoreV2.Load();
			PageLayoutV2 download = new PageLayoutV2();
			download.Blocks.Add(new BlockLayout { Id = "a" });
			download.Blocks.Add(new BlockLayout { Id = "b" });
			download.Blocks.Add(new BlockLayout { Id = "c" });
			doc.Layout.Pages["download"] = download;
			doc.Layout.Pages["plugins"] = new PageLayoutV2();
			LayoutStoreV2.Save(doc);

			// 外来 v1:只含 download(顺序 c,b,a + c 隐藏)
			PageLayoutDoc v1 = PageLayoutDoc.Default(new string[3] { "c", "b", "a" }, new string[3] { "c", "b", "a" });
			v1.Blocks[0].Hidden = true;
			Dictionary<string, PageLayoutDoc> pages = new Dictionary<string, PageLayoutDoc>();
			pages["download"] = v1;

			LayoutV2 target = LayoutStoreV2.Load();
			Assert.Equal(1, LayoutProfiles.MergeV1Pages(target, pages));
			LayoutStoreV2.Save(target);

			LayoutV2 after = LayoutStoreV2.Load();
			Assert.Equal(new string[3] { "c", "b", "a" }, after.Layout.Pages["download"].Blocks.Select((BlockLayout b) => b.Id).ToArray());
			Assert.True(after.Layout.Pages["download"].Blocks[0].Hidden);
			// 没给到的页保持不动(plugins 那页本来是空页)
			Assert.Empty(after.Layout.Pages["plugins"].Blocks);
			Assert.Equal(2, after.Layout.Pages.Count);
			Assert.Equal(0, LayoutProfiles.MergeV1Pages(null, pages));
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void ExportedV1TextStillParsesWithFrozenV1Parser()
	{
		UseDir();
		try
		{
			SeedCurrentLayout(0.4);
			// 「导出兼容 v1 的页面布局」= ProjectV1 + PageLayoutStore.ExportJson
			string text = PageLayoutStore.ExportJson(LayoutStoreV2.ProjectV1(LayoutStoreV2.Load()));
			using JsonDocument parsed = JsonDocument.Parse(text);
			Assert.Equal(1, parsed.RootElement.GetProperty("version").GetInt32());
			Assert.Equal(PageLayoutStore.Kind, parsed.RootElement.GetProperty("kind").GetString());
			// 冻结的 v1 解析器读回来:{id,hidden} 与顺序全等
			IReadOnlyDictionary<string, PageLayoutDoc> pages = PageLayoutStore.ParseImport(text);
			Assert.Equal(new string[2] { "filters", "list" }, pages["download"].Blocks.Select((BlockState b) => b.Id).ToArray());
			Assert.True(pages["download"].Blocks[1].Hidden);
			Assert.DoesNotContain("splits", text);
		}
		finally
		{
			Release();
		}
	}

	// ————— JSON 编辑器 —————

	[Fact]
	public void InspectAcceptsOwnExport()
	{
		UseDir();
		try
		{
			SeedCurrentLayout(0.4);
			string text = LayoutStoreV2.ExportJson(LayoutStoreV2.Load());
			List<LayoutJsonIssue> issues = LayoutJsonEditor.Inspect(text, out LayoutV2? doc);
			Assert.Empty(issues);
			Assert.NotNull(doc);
			Assert.Equal(0.4, doc!.Layout.Splits["library"].Ratios["folders"], 6);
			Assert.Equal(text, LayoutJsonEditor.Text(doc));
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void InspectReportsKindAndVersion()
	{
		UseDir();
		try
		{
			List<LayoutJsonIssue> wrongKind = LayoutJsonEditor.Inspect("{\"version\":2,\"kind\":\"tipcl-page-layout\"}", out LayoutV2? _);
			Assert.Contains(wrongKind, (LayoutJsonIssue i) => i.Error && i.Path == "kind" && i.Message.Contains("tipcl-layout"));

			List<LayoutJsonIssue> wrongVersion = LayoutJsonEditor.Inspect("{\"version\":1,\"kind\":\"tipcl-layout\"}", out LayoutV2? _);
			Assert.Contains(wrongVersion, (LayoutJsonIssue i) => i.Error && i.Path == "version");

			List<LayoutJsonIssue> missing = LayoutJsonEditor.Inspect("{\"layout\":{}}", out LayoutV2? _);
			Assert.Contains(missing, (LayoutJsonIssue i) => i.Error && i.Path == "kind");
			Assert.Contains(missing, (LayoutJsonIssue i) => i.Error && i.Path == "version");

			// kind/version 不对就不该继续往下报字段问题(门禁先行)
			Assert.Single(wrongKind);
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void InspectRejectsBrokenText()
	{
		UseDir();
		try
		{
			Assert.Contains(LayoutJsonEditor.Inspect("", out LayoutV2? _), (LayoutJsonIssue i) => i.Error && i.Message.Contains("空"));
			Assert.Contains(LayoutJsonEditor.Inspect("   ", out LayoutV2? _), (LayoutJsonIssue i) => i.Error);
			Assert.Contains(LayoutJsonEditor.Inspect("{不是 JSON", out LayoutV2? _), (LayoutJsonIssue i) => i.Error && i.Message.Contains("JSON"));
			Assert.Contains(LayoutJsonEditor.Inspect("[1,2,3]", out LayoutV2? _), (LayoutJsonIssue i) => i.Error && i.Message.Contains("对象"));
			Assert.Contains(LayoutJsonEditor.Inspect(new string('x', LayoutJsonEditor.MaxTextLength + 1), out LayoutV2? _), (LayoutJsonIssue i) => i.Error);
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void InspectReportsFieldPaths()
	{
		UseDir();
		try
		{
			string json = "{\"version\":2,\"kind\":\"tipcl-layout\",\"unknown_top\":1,\"layout\":{"
				+ "\"shell\":{\"density\":\"weird\",\"nav_width\":999,\"tabs\":[\"launch\",\"nope\"]},"
				+ "\"pages\":{\"launch\":{\"version\":1,\"mode\":\"flying\",\"cols\":5,\"gap\":99,"
				+ "\"blocks\":[{\"id\":\"a\",\"style\":\"shiny\",\"hidden\":\"yes\"},{\"id\":\"a\"}]}},"
				+ "\"splits\":{\"library\":{\"folders\":0.99,\"list\":\"x\"}},"
				+ "\"widgets\":[{\"id\":\"w1\",\"type\":\"quantum\"}]}}";
			List<LayoutJsonIssue> issues = LayoutJsonEditor.Inspect(json, out LayoutV2? doc);
			Assert.NotNull(doc);
			Assert.DoesNotContain(issues, (LayoutJsonIssue i) => i.Error);
			string[] paths = issues.Select((LayoutJsonIssue i) => i.Path).ToArray();
			Assert.Contains("unknown_top", paths);
			Assert.Contains("layout.shell.density", paths);
			Assert.Contains("layout.shell.nav_width", paths);
			Assert.Contains("layout.shell.tabs", paths);
			Assert.Contains("layout.pages[\"launch\"].version", paths);
			Assert.Contains("layout.pages[\"launch\"].mode", paths);
			Assert.Contains("layout.pages[\"launch\"].cols", paths);
			Assert.Contains("layout.pages[\"launch\"].gap", paths);
			Assert.Contains("layout.pages[\"launch\"].blocks[0].style", paths);
			Assert.Contains("layout.pages[\"launch\"].blocks[0].hidden", paths);
			Assert.Contains("layout.pages[\"launch\"].blocks[1].id", paths);
			Assert.Contains("layout.splits[\"library\"][\"folders\"]", paths);
			Assert.Contains("layout.splits[\"library\"][\"list\"]", paths);
			Assert.Contains("layout.widgets[0].type", paths);
			// 越界比例被夹取 + 单页 version 门禁不过 → 该页按默认处理(不进文档)
			Assert.DoesNotContain("launch", doc!.Layout.Pages.Keys);
			Assert.Equal(0.85, doc.Layout.Splits["library"].Ratios["folders"], 6);
			Assert.Equal(LayoutSchema.DensityStandard, doc.Layout.Shell.Density);
		}
		finally
		{
			Release();
		}
	}

	// ————— L5b:入口表必须覆盖单块页 —————

	[Fact]
	public void BlockEntryCatalogCoversSingleBlockPages()
	{
		// D1 把 LayoutEditor.CanEdit 放宽到 specs ≥ 1 之后,单块页也要有入口:
		// 设置页「布局」→「页面区块」小节必须能进 settings:network / settings:about 的编辑态
		Assert.Equal(12, LayoutBlockEntryCatalog.All.Length);
		Assert.Equal(12, LayoutBlockEntryCatalog.All.Select((LayoutBlockEntryCatalog.Entry e) => e.PageId).Distinct().Count());
		Assert.Equal(new string[2] { "settings:network", "settings:about" }, LayoutBlockEntryCatalog.SingleBlockPageIds().ToArray());

		LayoutBlockEntryCatalog.Entry? network = LayoutBlockEntryCatalog.Find("settings:network");
		Assert.NotNull(network);
		Assert.True(network!.SingleBlock);
		Assert.Equal("settings", network.NavId);
		Assert.Equal(3, network.SettingsView);

		LayoutBlockEntryCatalog.Entry? about = LayoutBlockEntryCatalog.Find("settings:about");
		Assert.NotNull(about);
		Assert.True(about!.SingleBlock);
		Assert.Equal("settings", about.NavId);
		Assert.Equal(6, about.SettingsView);

		// 每一行都要能真的找到页面:导航 id 必须是已知 key;文案与 pageId 都不能空
		foreach (LayoutBlockEntryCatalog.Entry entry in LayoutBlockEntryCatalog.All)
		{
			Assert.True(LayoutSchema.IsKnownNavKey(entry.NavId), entry.PageId + " 的导航 id 不合法:" + entry.NavId);
			Assert.False(string.IsNullOrWhiteSpace(entry.Label));
			Assert.False(string.IsNullOrWhiteSpace(entry.PageId));
			Assert.True(entry.SettingsView >= -1);
			// 只有在设置页里的那几项才带视图下标
			Assert.Equal(entry.NavId == "settings", entry.SettingsView >= 0);
		}
		Assert.Null(LayoutBlockEntryCatalog.Find("settings:nope"));
		Assert.Null(LayoutBlockEntryCatalog.Find(null));
	}

	[Fact]
	public void ApplyWritesOnlyWhenThereIsNoError()
	{
		UseDir();
		try
		{
			SeedCurrentLayout(0.4);
			string before = V2Text();

			// 有硬错误:一个字节都不该落盘
			List<LayoutJsonIssue> failed = LayoutJsonEditor.Apply("{\"version\":2,\"kind\":\"nope\"}", out LayoutV2? _);
			Assert.Contains(failed, (LayoutJsonIssue i) => i.Error);
			Assert.Equal(before, V2Text());

			// 只有警告(比例越界 / 未知字段):照常应用,并按契约钳位
			string json = "{\"version\":2,\"kind\":\"tipcl-layout\",\"hello\":1,\"layout\":{\"splits\":{\"library\":{\"folders\":0.99}}}}";
			List<LayoutJsonIssue> warned = LayoutJsonEditor.Apply(json, out LayoutV2? applied);
			Assert.DoesNotContain(warned, (LayoutJsonIssue i) => i.Error);
			Assert.NotEmpty(warned);
			Assert.Equal(0.85, LayoutStoreV2.Load().Layout.Splits["library"].Ratios["folders"], 6);
			Assert.NotNull(applied);
			// 应用后 v1 仍然是冻结格式
			string v1 = File.ReadAllText(LayoutStoreV2.V1FilePath);
			Assert.DoesNotContain("splits", v1);
			Assert.DoesNotContain("hello", v1);
		}
		finally
		{
			Release();
		}
	}
}
