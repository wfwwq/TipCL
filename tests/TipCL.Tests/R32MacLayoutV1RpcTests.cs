using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;
using TipCL.Pages;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// <b>R32-MAC-SYS01 / SYS-01 / AP-01:7 个布局 v1 RPC 必须真的作用在 v2 真源上(Mac 树)。</b>
///
/// <para>移植源头:Windows <c>windos\tests\TipCL.Tests\LayoutV1RpcTests.cs</c>(冻结件 <c>0a304a7</c>,
/// 340 行 / 12 个 <c>[Fact]</c>)。语义逐条搬,断言一条没放宽,只换 mac 的三处环境差异:</para>
/// <list type="number">
/// <item>仓库根标记 <c>TipCL.sln</c> → <c>TipCL.Mac.sln</c>;被测路径仍是 <c>src/TipCL.Core/BackendApi.cs</c>
///   (mac 树同路径,源文件里的 7 个方法名与签名一字未改)。</item>
/// <item>临时目录 <c>Path.GetTempPath()</c> → <see cref="TestHome.Root"/> 之下(mac 红线:测试临时文件
///   也必须落在仓库内;与 <c>R32McLayoutV2CompatTests</c> 同一条规则)。</item>
/// <item>v1 注入缝 <c>PagesLayoutV1Bridge</c> → 测试侧镜像 <see cref="R32McV1Bridge"/>(语义逐条照抄;
///   生产装配在 <c>App</c> 启动时,本包不动)。</item>
/// </list>
///
/// <para><b>病</b>:这 7 个 RPC 过去只读写 <c>config.json</c> 的 <c>ui_layout</c> / <c>ui_layouts</c> /
/// <c>ui_layout_profile</c> 三个键,而 v2 落地后那三个键在界面侧 0 个消费者(旧画布 <c>DashHost</c> 不进
/// 可视树,见 <c>Pages/LaunchPage.cs:154/2056</c> 与 <c>C3cPageStructureTests</c> 的断言)⇒ 调用方拿到
/// <c>saved:true</c>,屏幕上什么都不变。"静默无效"比报错更坏。</para>
///
/// <para>这里钉五件事:</para>
/// <list type="number">
/// <item><b>真能读能改</b> —— 调完之后 <see cref="LayoutStoreV2.Load"/> 里能看到改动,而且
///   <b>磁盘上的 <c>ui_layout_v2.json</c></b>(kind / version / pages / blocks)就是那一份(v2 真源);</item>
/// <item><b>不再静默</b> —— 方案名不存在就抛 <see cref="ValueError"/>,而且<b>一个字节都不动</b>;</item>
/// <item><b>不塞垃圾</b> —— 入参里目标页没有的区块 id 一律不新建,并在返回值里报出来;</item>
/// <item><b>v1 死车道真的拆掉了</b> —— 源码门禁(<c>UiLayout.SaveCurrent</c> 等 7 个调用一个都不许留)
///   <b>加上</b>行为门禁:调完这 7 个 RPC 之后 <c>config.json</c> 的三个 v1 键<b>逐字节没动</b>。</item>
/// </list>
///
/// <para>落盘一律走 <c>DirOverride</c> 临时目录 + 隔离 <c>TestHome</c>,绝不碰用户真实数据目录。</para>
/// </summary>
public class R32MacLayoutV1RpcTests : IDisposable
{
	private static readonly string[] V1LaneKeys = { "ui_layout", "ui_layouts", "ui_layout_profile" };

	private readonly string _tmp = Path.Combine(TestHome.Root, "r32-mac-layout-v1rpc-" + Guid.NewGuid().ToString("N"));

	private readonly BackendApi _api;

	private readonly bool _savedWarmup;

	// 用例可能(在行为门禁里)**故意**把 v1 死车道的三个键种成标记值,跑完必须原样还回去
	// (Config.CONFIG 是进程级单例)。这里先抓一份深拷贝。
	private readonly object? _origUiLayout;
	private readonly object? _origUiLayouts;
	private readonly object? _origUiLayoutProfile;

	public R32MacLayoutV1RpcTests()
	{
		_savedWarmup = BackendApi.DisableAuthWarmup;
		BackendApi.DisableAuthWarmup = true;
		_origUiLayout = Py.DeepCopy(Config.CONFIG.Get("ui_layout"));
		_origUiLayouts = Py.DeepCopy(Config.CONFIG.Get("ui_layouts"));
		_origUiLayoutProfile = Py.DeepCopy(Config.CONFIG.Get("ui_layout_profile"));
		_api = new BackendApi(new EventBus());
		UseDir();
	}

	private void UseDir()
	{
		Directory.CreateDirectory(_tmp);
		LayoutStoreV2.DirOverride = _tmp;
		LayoutStoreV2.V1Bridge = new R32McV1Bridge();
		PageLayoutStore.DirOverride = _tmp;
		LayoutStoreV2.ResetCache();
		PageLayoutStore.ResetCache();
	}

	public void Dispose()
	{
		LayoutStoreV2.DirOverride = null;
		LayoutStoreV2.V1Bridge = null;
		PageLayoutStore.DirOverride = null;
		LayoutStoreV2.ResetCache();
		PageLayoutStore.ResetCache();
		Config.CONFIG.Set("ui_layout", _origUiLayout);
		Config.CONFIG.Set("ui_layouts", _origUiLayouts);
		Config.CONFIG.Set("ui_layout_profile", _origUiLayoutProfile);
		BackendApi.DisableAuthWarmup = _savedWarmup;
		try
		{
			if (Directory.Exists(_tmp))
			{
				Directory.Delete(_tmp, recursive: true);
			}
		}
		catch (Exception)
		{
		}
	}

	private static string RepoFile(string rel)
	{
		DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TipCL.Mac.sln")))
		{
			dir = dir.Parent;
		}
		Assert.NotNull(dir);
		return File.ReadAllText(Path.Combine(dir!.FullName, rel.Replace('/', Path.DirectorySeparatorChar)));
	}

	/// <summary>在真源里造一个启动页(布局 v1 的画布在 v2 里对应的就是这一页)。</summary>
	private static void SeedLaunchPage(params string[] blockIds)
	{
		LayoutV2 doc = LayoutStoreV2.Load();
		PageLayoutV2 page = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeStack,
			Cols = LayoutSchema.ColsDefault,
			Gap = LayoutSchema.GapDefault
		};
		foreach (string id in blockIds)
		{
			page.Blocks.Add(new BlockLayout { Id = id });
		}
		doc.Layout.Pages["launch"] = page;
		LayoutStoreV2.Save(doc);
	}

	/// <summary>v1 画布文档形状:<c>{items:[{type,x,y,w,h,id,z,hidden}]}</c>(与 UiLayout/LayoutDoc 的冻结契约同形)。</summary>
	private static PyDict V1Doc(params string[] types)
	{
		List<object> items = new List<object>();
		long z = 0L;
		foreach (string type in types)
		{
			items.Add(PyDict.Of(
				"id", type + "-main",
				"type", type,
				"x", 0.0,
				"y", 0.0,
				"w", 1.0,
				"h", 1.0,
				"z", z,
				"hidden", false));
			z++;
		}
		return PyDict.Of("version", 1L, "items", items);
	}

	private static string[] LaunchOrder()
	{
		LayoutV2 doc = LayoutStoreV2.Load();
		Assert.True(doc.Layout.Pages.TryGetValue("launch", out PageLayoutV2? page), "真源里应该有 launch 页");
		return page!.Blocks.Select((BlockLayout b) => b.Id).ToArray();
	}

	/// <summary>磁盘上的 v2 真源(不是内存缓存):kind / version / 某页的 block id 顺序。</summary>
	private (string Kind, int Version, string[] Blocks) V2File(string pageId)
	{
		string path = Path.Combine(_tmp, LayoutStoreV2.FileName);
		Assert.True(File.Exists(path), "v2 真源文件必须真的写出来了: " + path);
		using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
		JsonElement root = doc.RootElement;
		Assert.Equal(JsonValueKind.Object, root.ValueKind);
		string kind = root.TryGetProperty("kind", out JsonElement k) ? (k.GetString() ?? "") : "";
		int version = root.TryGetProperty("version", out JsonElement v) ? v.GetInt32() : -1;
		string[] blocks = Array.Empty<string>();
		if (root.TryGetProperty("layout", out JsonElement layout)
			&& layout.TryGetProperty("pages", out JsonElement pages)
			&& pages.TryGetProperty(pageId, out JsonElement page)
			&& page.TryGetProperty("blocks", out JsonElement list)
			&& list.ValueKind == JsonValueKind.Array)
		{
			blocks = list.EnumerateArray()
				.Select((JsonElement b) => b.TryGetProperty("id", out JsonElement id) ? (id.GetString() ?? "") : "")
				.ToArray();
		}
		return (kind, version, blocks);
	}

	private static string[] PageBlocks(PyDict snapshot, string pageId)
	{
		Dictionary<string, object?> pages = (Dictionary<string, object?>)snapshot["pages"]!;
		Dictionary<string, object?> page = (Dictionary<string, object?>)pages[pageId]!;
		return ((List<object?>)page["blocks"]!).Cast<string>().ToArray();
	}

	// ————— 1. 真能读能改(v2 真源,不是那个没人看的 config 键) —————

	[Fact]
	public void SaveLayoutWritesTheV2TruthAndGetLayoutSeesIt()
	{
		SeedLaunchPage("news", "tasks", "playtime");

		// 入参顺序反过来:playtime / news(tasks 没列 → 按 MergeV1Order 的约定落到末尾)
		PyDict result = _api.save_layout(V1Doc("playtime", "news"));

		Assert.True((bool)result["saved"]!, "v2 真源应该真的写成功了");
		Assert.Equal(LayoutV1Rpc.ApiVersion, result["api"]);
		Assert.Contains("v2 真源", (string)result["message"]!, StringComparison.Ordinal);
		// 真源(不是返回值)里就是新顺序 —— 这才是"不再静默无效"的判据
		Assert.Equal(new[] { "playtime", "news", "tasks" }, LaunchOrder());
		// 只读口立刻看到同一份真源
		PyDict snapshot = _api.get_layout();
		Assert.Equal(LayoutV1Rpc.ApiVersion, snapshot["api"]);
		Assert.Equal(new[] { "playtime", "news", "tasks" }, PageBlocks(snapshot, "launch"));
	}

	/// <summary>
	/// mac 追加(Windows 那 12 例里没有的):**磁盘上的** <c>ui_layout_v2.json</c> 就是写读往返的落点。
	/// 判据是文件里的 kind / version / layout.pages.&lt;page&gt;.blocks[].id 顺序,不是"内存里有个对象"。
	/// 这条能因"把 save_layout 改回 v1(写 config.json)"而变红:那时 v2 文件里没有这一页。
	/// </summary>
	[Fact]
	public void SaveLayoutRoundTripLandsInTheV2DocumentOnDisk()
	{
		SeedLaunchPage("news", "tasks", "playtime");

		PyDict result = _api.save_layout(V1Doc("playtime", "news"));
		Assert.True((bool)result["saved"]!);

		(string kind, int version, string[] blocks) = V2File("launch");
		Assert.Equal(LayoutSchema.Kind, kind);
		Assert.Equal(2, version);
		Assert.Equal(new[] { "playtime", "news", "tasks" }, blocks);

		// 同一次改动也必须体现在 v1 投影文件里(单向写通,v2 是唯一真源)
		string v1Path = Path.Combine(_tmp, LayoutStoreV2.V1FileName);
		Assert.True(File.Exists(v1Path), "v2 保存成功后应当写通 v1 投影: " + v1Path);
		Assert.Contains("playtime", File.ReadAllText(v1Path), StringComparison.Ordinal);
	}

	[Fact]
	public void GetLayoutKeepsTheV1MinSizesButReadsEverythingElseFromV2()
	{
		SeedLaunchPage("news", "tasks");
		LayoutV2 doc = LayoutStoreV2.Load();
		doc.Profiles["甲"] = doc.Layout.Clone();
		doc.ActiveProfile = "甲";
		LayoutStoreV2.Save(doc);

		PyDict snapshot = _api.get_layout();

		Assert.True(snapshot.ContainsKey("min_sizes"), "v1 的最小尺寸表不该被删掉");
		PyDict minSizes = (PyDict)snapshot["min_sizes"]!;
		Assert.Equal(UiLayout.CARD_MIN_SIZE.Count, minSizes.Count);
		Assert.True(minSizes.ContainsKey("news"), "min_sizes 必须是 v1 冻结表原样");
		Assert.Equal("甲", snapshot["profile"]);
		Assert.Contains("甲", ((IEnumerable<object>)snapshot["profiles"]!).Cast<string>());
		// doc 现在是 v2 的 LAYOUT 对象(pages 在),不再是 v1 的 items
		Dictionary<string, object?> plain = (Dictionary<string, object?>)snapshot["doc"]!;
		Assert.True(plain.ContainsKey("pages"), "doc 应该是 v2 的 LAYOUT 对象");
		Assert.False(plain.ContainsKey("items"), "doc 不该还是 v1 的 items 形状");
		Assert.Contains("ui_layout", (string)snapshot["legacy"]!, StringComparison.Ordinal);
		Assert.Contains("已不再有任何效果", (string)snapshot["legacy"]!, StringComparison.Ordinal);
	}

	// ————— 2. 不再静默:方案不存在 = 抛错 + 一个字节都不动 —————

	[Fact]
	public void ActivatingAMissingProfileThrowsInsteadOfSilentlyClearingTheLayout()
	{
		SeedLaunchPage("news", "tasks");
		string[] before = LaunchOrder();

		ValueError error = Assert.Throws<ValueError>(() => _api.activate_layout_profile("没有这个方案"));

		Assert.Contains("没有这个方案", error.Message, StringComparison.Ordinal);
		// v1 过去会把布局静默清成默认;现在必须原样
		Assert.Equal(before, LaunchOrder());
		Assert.Equal("", LayoutStoreV2.Load().ActiveProfile);
	}

	[Fact]
	public void ActivateAppliesTheProfileSnapshotAndDeleteForgetsIt()
	{
		SeedLaunchPage("news", "tasks", "playtime");
		PyDict saved = _api.save_layout_profile("我的布局");
		Assert.Equal("我的布局", saved["profile"]);
		// 改掉当前布局,再切回方案 → 应该回到快照那一份
		PyDict moved = _api.save_layout(V1Doc("tasks", "news"));
		Assert.True((bool)moved["saved"]!);
		Assert.Equal(new[] { "tasks", "news", "playtime" }, LaunchOrder());

		_api.activate_layout_profile("我的布局");

		Assert.Equal(new[] { "news", "tasks", "playtime" }, LaunchOrder());
		Assert.Equal("我的布局", LayoutStoreV2.Load().ActiveProfile);
		// 删掉生效方案:方案没了,布局留原样,回直接编辑态
		_api.delete_layout_profile("我的布局");
		LayoutV2 after = LayoutStoreV2.Load();
		Assert.False(after.Profiles.ContainsKey("我的布局"));
		Assert.Equal("", after.ActiveProfile);
		Assert.Equal(new[] { "news", "tasks", "playtime" }, LaunchOrder());
		// 方案表在磁盘上的那一份也没了(不是只在内存里删)
		Assert.DoesNotContain("我的布局", File.ReadAllText(Path.Combine(_tmp, LayoutStoreV2.FileName)), StringComparison.Ordinal);
	}

	[Fact]
	public void DeletingAMissingProfileStillRaisesTheSameChineseError()
	{
		ValueError error = Assert.Throws<ValueError>(() => _api.delete_layout_profile("不存在"));
		Assert.Contains("不存在", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ResetClearsTheLayoutButKeepsTheNamedProfiles()
	{
		SeedLaunchPage("news", "tasks");
		_api.save_layout_profile("我的布局");

		_api.reset_layout();

		LayoutV2 doc = LayoutStoreV2.Load();
		Assert.Empty(doc.Layout.Pages);            // 各页回到自带默认
		Assert.Equal("", doc.ActiveProfile);       // 直接编辑态
		Assert.True(doc.Profiles.ContainsKey("我的布局"), "「恢复默认」不该顺手删掉用户的命名方案");
		// 磁盘同一份:pages 清空、方案还在
		(string kind, int version, string[] blocks) = V2File("launch");
		Assert.Equal(LayoutSchema.Kind, kind);
		Assert.Equal(2, version);
		Assert.Empty(blocks);
	}

	// ————— 3. 不塞垃圾:目标页没有的区块 id 不新建,而且报出来 —————

	[Fact]
	public void SaveLayoutIgnoresIdsThePageDoesNotHaveInsteadOfCreatingJunkBlocks()
	{
		SeedLaunchPage("news", "tasks");

		// v1 的默认画布就是 config / quick / news 三张卡,而 v2 的启动页没有 config / quick 这两种块
		PyDict result = _api.save_layout(V1Doc("config", "quick", "news"));

		string message = (string)result["message"]!;
		Assert.Contains("config", message, StringComparison.Ordinal);
		Assert.Contains("quick", message, StringComparison.Ordinal);
		Assert.Equal(new[] { "news", "tasks" }, LaunchOrder());   // 没有凭空多出 config / quick
	}

	[Fact]
	public void SaveLayoutOnAPageThatDoesNotExistYetRefusesAndExplains()
	{
		// 全新数据目录:真源里还没有 launch 页 ⇒ 没有任何"已有区块"可改
		PyDict result = _api.save_layout(V1Doc("news"));

		Assert.False((bool)result["saved"]!);
		Assert.Contains("不存在", (string)result["message"]!, StringComparison.Ordinal);
		Assert.Empty(LayoutStoreV2.Load().Layout.Pages);
	}

	// ————— 4. import:两种入参两种语义(D-04 / M-01 定下的同一套规则) —————

	[Fact]
	public void ImportV2DocumentReplacesTheWholeLayoutAndKeepsExistingProfiles()
	{
		SeedLaunchPage("news", "tasks");
		_api.save_layout_profile("插件作者给的方案");   // 存一份,证明导入不会把它冲掉

		LayoutV2 incoming = LayoutV2.CreateDefault();
		incoming.Layout.Pages["tasks"] = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeGrid,
			Cols = 3,
			Blocks = new List<BlockLayout> { new BlockLayout { Id = "list" } }
		};
		incoming.Profiles["新方案"] = incoming.Layout.Clone();
		incoming.ActiveProfile = "新方案";

		// 入参就是文件文本(整机 v2 方案),与调用方从磁盘读一份文件过来的情形一致
		_api.import_layout(incoming.ToJson());

		LayoutV2 doc = LayoutStoreV2.Load();
		Assert.False(doc.Layout.Pages.ContainsKey("launch"), "整套 LAYOUT 应该被替换");
		Assert.True(doc.Layout.Pages.ContainsKey("tasks"));
		Assert.Equal(3, doc.Layout.Pages["tasks"].Cols);
		Assert.True(doc.Profiles.ContainsKey("插件作者给的方案"), "方案表按名字合并,原有方案要留着");
		Assert.Equal("新方案", doc.ActiveProfile);
		// 磁盘上的整份文档也换掉了
		(string kind, int version, _) = V2File("tasks");
		Assert.Equal(LayoutSchema.Kind, kind);
		Assert.Equal(2, version);
	}

	[Fact]
	public void ImportV1DocumentOnlyMergesOrderAndHidden()
	{
		SeedLaunchPage("news", "tasks", "playtime");

		_api.import_layout(V1Doc("playtime", "news"));

		// 只并顺序/显隐:页还在、其它页/方案表都不受影响
		Assert.Equal(new[] { "playtime", "news", "tasks" }, LaunchOrder());
		Assert.Equal(LayoutSchema.ModeStack, LayoutStoreV2.Load().Layout.Pages["launch"].Mode);
	}

	[Fact]
	public void ImportOfSomethingElseSaysTheSameChineseError()
	{
		ValueError error = Assert.Throws<ValueError>(() => _api.import_layout(PyDict.Of("hello", "world")));
		Assert.Contains("不是有效的布局文件", error.Message, StringComparison.Ordinal);
	}

	// ————— 5. v1 那三个"没人看"的键真的不再被这 7 个方法写了 —————

	/// <summary>源码门禁(Windows 那 12 例里的同名用例,断言一字未改;只换仓库根标记)。</summary>
	[Fact]
	public void TheSevenRpcsNoLongerWriteTheDeadV1DashboardKeys()
	{
		string src = RepoFile("src/TipCL.Core/BackendApi.cs");
		int at = src.IndexOf("public PyDict get_layout()", StringComparison.Ordinal);
		Assert.True(at > 0, "找不到 get_layout 定义");
		int tail = src.IndexOf("internal Instance _instance(", at, StringComparison.Ordinal);
		Assert.True(tail > at, "找不到这 7 个方法的结尾(_instance)");
		string block = src.Substring(at, tail - at);

		Assert.DoesNotContain("UiLayout.SaveCurrent", block, StringComparison.Ordinal);
		Assert.DoesNotContain("UiLayout.SaveProfile", block, StringComparison.Ordinal);
		Assert.DoesNotContain("UiLayout.ActivateProfile", block, StringComparison.Ordinal);
		Assert.DoesNotContain("UiLayout.ResetToDefault", block, StringComparison.Ordinal);
		Assert.DoesNotContain("UiLayout.SaveActiveDoc", block, StringComparison.Ordinal);
		Assert.DoesNotContain("UiLayout.LoadActiveDoc", block, StringComparison.Ordinal);
		Assert.DoesNotContain("UiLayout.LayoutSnapshot", block, StringComparison.Ordinal);
		// 7 个方法一个都不许删(不许用"删掉"消灭问题)
		foreach (string name in new[] { "get_layout", "save_layout", "save_layout_profile", "activate_layout_profile", "delete_layout_profile", "reset_layout", "import_layout" })
		{
			Assert.Contains("public PyDict " + name + "(", block, StringComparison.Ordinal);
		}
		// 也钉住"7 个方法都真的转调了 v2 适配层"
		Assert.Contains("LayoutV1Rpc.", block, StringComparison.Ordinal);
		// min_sizes 是 v1 冻结表,必须留着
		Assert.Contains("UiLayout.CARD_MIN_SIZE", block, StringComparison.Ordinal);
	}

	/// <summary>
	/// mac 追加的行为门禁(比源码门禁更硬:不靠"删掉某行"就不能改回 v1 —— 只要真的写进
	/// <c>config.json</c> 的三个键,值就会变):
	/// 把 v1 死车道种成**标记值**,依次调完 7 个 RPC,再比对三个键的 JSON,必须逐字节相同。
	/// 任何一个 RPC 被改回 <c>UiLayout.*</c> 实现(写 config.json)这条就红。
	/// </summary>
	[Fact]
	public void TheSevenRpcsLeaveTheV1ConfigLaneByteIdentical()
	{
		// 先把 v2 真源造好,让 7 个调用都能真的干活(否则"没动"可能只是"没走到写"的分支)
		SeedLaunchPage("news", "tasks", "playtime");

		// v1 死车道种标记值:三个键都取非默认值,任何 v1 写入都会改变它们
		PyDict legacyProfiles = PyDict.Of();
		legacyProfiles["旧方案"] = UiLayout.DefaultDoc().ToDict();
		Config.CONFIG.Set("ui_layout", UiLayout.DefaultDoc().ToDict());
		Config.CONFIG.Set("ui_layouts", legacyProfiles);
		Config.CONFIG.Set("ui_layout_profile", "旧方案");
		string before = V1LaneJson();

		// 7 个 RPC 各调一次(顺序按文件里的定义顺序)
		_api.get_layout();
		_api.save_layout(V1Doc("playtime", "news"));
		_api.save_layout_profile("新方案");
		_api.activate_layout_profile("新方案");
		_api.get_layout();
		_api.delete_layout_profile("新方案");
		_api.import_layout(V1Doc("news", "tasks"));
		_api.reset_layout();

		Assert.Equal(before, V1LaneJson());
	}

	private static string V1LaneJson()
	{
		PyDict lane = PyDict.Of();
		foreach (string key in V1LaneKeys)
		{
			lane[key] = Config.CONFIG.Get(key);
		}
		return PyJson.Dumps(lane, 0, ensureAscii: false, sortKeys: true);
	}

	// ————— 6. 同一会话里"RPC 真的改了屏幕"(不只是回 saved:true) —————

	/// <summary>
	/// mac 追加的端到端一条:把真的 <see cref="BlockHost"/> 挂进**真 Avalonia 窗口**(headless + Skia,
	/// 与 <c>--smoke</c> 同一平台配置),然后**只**经 v1 RPC 改布局:
	///   save_layout 改顺序 → 同一窗口里两块的**窗口坐标**当场互换(不是只回 saved:true);
	///   reset_layout 清布局 → 回到页面自己声明的顺序,坐标再换回来。
	///
	/// <para>判据是三路对齐:渲染顺序(<see cref="C3aTree.RenderedIds"/>)、窗口坐标
	/// (<c>TranslatePoint</c> 到 Window 的 Y)、v2 真源(<see cref="LaunchOrder"/>)。</para>
	///
	/// <para>为什么不用截图字节做判据:本仓库 headless 宿主上 <c>Window.CaptureRenderedFrame()</c> 与
	/// <c>RenderTargetBitmap.Render</c> 拿到的都是**空白帧**(实测:改前/改后两张 PNG 同 sha256),
	/// 用它们断言"变了"会红、断言"没变"会假绿 —— 两边都不成立,所以像素证据改由**真窗口**那一轮
	/// (postMessage 右键 → 自定义布局 → 编辑态 + PrintWindow 截图)给出,这里只用坐标。</para>
	///
	/// <para>这条能因"把 save_layout / reset_layout 改回 v1(写 config.json)"而变红 ——
	/// 那时窗口里两块的顺序与坐标一动不动。</para>
	/// </summary>
	[Fact]
	public void TheRpcChangesWhatTheRealWindowRendersInTheSameSession()
	{
		AvaloniaSession.Run(() =>
		{
			SeedLaunchPage("news", "tasks");
			BlockHost host = C3aBuild.Host("launch", C3aBuild.Spec("news"), C3aBuild.Spec("tasks"));
			using UiHost ui = UiHost.Show(host);
			Assert.Equal(new[] { "news", "tasks" }, C3aTree.RenderedIds(host).ToArray());
			(double newsY, double tasksY) = Tops(host, ui.Window);
			Assert.True(newsY < tasksY, "改前:news 在 tasks 上面");

			// 1) save_layout(顺序反过来) → 同一窗口里两块的顺序与坐标当场换
			PyDict saved = _api.save_layout(V1Doc("tasks", "news"));
			Assert.True((bool)saved["saved"]!);
			UiHost.Flush();
			Assert.Equal(new[] { "tasks", "news" }, C3aTree.RenderedIds(host).ToArray());
			Assert.Equal(new[] { "tasks", "news" }, LaunchOrder());
			(double newsY2, double tasksY2) = Tops(host, ui.Window);
			Assert.True(tasksY2 < newsY2, "save_layout 之后 tasks 必须移到 news 上面(屏幕上真的换了位)");
			Assert.True(newsY2 > newsY, "news 的窗口 Y 必须下降");

			// 2) reset_layout → 布局清空,页面回到自己声明的顺序(屏幕再变一次)
			//    注意:reset_layout / activate / import / delete / save_layout_profile 的返回值是 get_layout()
			//    快照(与 Windows 一字不差),没有 "saved" 键 —— 别在这里断言它。
			PyDict reset = _api.reset_layout();
			Assert.Equal(0, ((Dictionary<string, object?>)reset["pages"]!).Count);
			UiHost.Flush();
			Assert.Equal(new[] { "news", "tasks" }, C3aTree.RenderedIds(host).ToArray());
			Assert.Empty(LayoutStoreV2.Load().Layout.Pages);
			(double newsY3, double tasksY3) = Tops(host, ui.Window);
			Assert.True(newsY3 < tasksY3, "reset_layout 之后必须回到 news 在上");
			Assert.Equal(newsY, newsY3, 1);
			Assert.Equal(tasksY, tasksY3, 1);
		});
	}

	/// <summary>两块卡在**窗口坐标**里的顶边 Y(顺序变了才能反映"屏幕真的变了")。</summary>
	private static (double News, double Tasks) Tops(BlockHost host, Window window)
	{
		double news = double.NaN;
		double tasks = double.NaN;
		foreach (LayoutCard card in C3aTree.RenderedCards(host))
		{
			double y = ((Visual)card).TranslatePoint(new Avalonia.Point(0.0, 0.0), window)?.Y ?? double.NaN;
			if (card.Spec.Id == "news")
			{
				news = y;
			}
			else if (card.Spec.Id == "tasks")
			{
				tasks = y;
			}
		}
		Assert.False(double.IsNaN(news) || double.IsNaN(tasks), "两张卡都必须真的在可视树里");
		return (news, tasks);
	}
}