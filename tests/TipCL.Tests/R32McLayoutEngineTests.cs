// 来源: Linux 移植件 wt-linux-layout @8c96b03 的 tests/TipCL.Tests/Layout/LayoutEngineTests.cs
//       (本文件与它逐字节相同,只做 7 处机械替换:文件头 / namespace / using / v1 注入缝实现名 /
//        3 处临时目录 / 类名前缀;逐条见 progress/R32-MC.md 的「Mac vs Linux 同源对照」)。
//       Windows 侧原始件 = 参考仓库 tests/TipCL.Tests/ 的 4 个文件:
//         A/B/C = {LayoutEngineTests, LayoutRowClampTests, LayoutFixTests}.cs(分支 layout-shell-hooks @ fa1b3d7)
//         D     = AuditFixTests.cs(分支 fix/r31-ui-audit @ 33b5fff;只取覆盖并入的 3 处增量的用例)
//       全部用例都是零 WPF / 零 Avalonia 的纯模型(引擎 / 网格几何 / 外壳判定 / Core v2 模型),net8 测试宿主可直接跑。
// 移植差异(逐条见 progress/R32-MC.md 修订 1 的差异表):
//   1) 命名空间:Linux 的 TipCL.App.Pages.Layout → mac 的 TipCL.Pages;v1 模型 PageLayoutDoc / BlockState 在 mac
//      树同样位于 TipCL.Pages(src\TipCL.App\Pages\PageBlock.cs)⇒ `using TipCL.Core.Layout;` 不搬;类名按 mac
//      约定加包号前缀(R32Mc + Linux 的四个类名),方法名与断言一字未改。
//   2) D 组只搬 3 条(WidgetSignature / ApplyPage(variant) / LayoutVariants.SourceOf);钳位相关的 4 条与
//      L1 的 LayoutRowClampTests 同源且互相冲突,按裁定取 L1(裁定 R32-D2 的落地证据就在本文件的
//      R32McLayoutRowClampTests.AbsurdRowsAreCappedByDefensiveValueNotByColumns);PageLayoutIO.MergeImport 三条
//      属 Pages 层文件(mac 树也没有),不搬。
//   3) LayoutFixTests 里 v1 注入缝的 Windows 实现 PagesLayoutV1Bridge 在 mac 树不存在(生产接线留给后续
//      视图/接线包),改用同测试程序集的 R32McV1Bridge(R32McLayoutV2Fixtures.cs 里的等价实现,语义逐条照抄;
//      Linux 侧对等物叫 LinuxV1Bridge)。
//   4) 3 处临时目录:Path.GetTempPath() → TestHome.Root 之下(mac 红线:测试临时文件必须落在仓库内)。
//   5) R32McLayoutEngineTests 里的 PageLayoutDoc 兼容锚(Default/Normalize/FromJson)在 mac 树就是 TipCL.Pages 的
//      逐字同源实现,断言一字未改。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TipCL.Core.Launcher.Layout;
using TipCL.Pages;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 布局引擎(纯模型层)测试:默认布局与冻结的 v1 语义等价、顺序/显隐/span 的排布与命中、
/// 拖拽重排算法(移植自旧 BlockHost)、内容工厂钩子(custom: / plugin: / 不可用)。
/// 测试宿主是 net8:只碰不依赖 WPF 的类型(LayoutEngine / LayoutPageState / LayoutStoreV2),不实例化 BlockHost。
/// </summary>
public class R32McLayoutEngineTests
{
	private static List<LayoutBlockRef> Refs(params string[] ids)
	{
		List<LayoutBlockRef> list = new List<LayoutBlockRef>();
		foreach (string id in ids)
		{
			list.Add(new LayoutBlockRef(id, true));
		}
		return list;
	}

	private static PageLayoutV2 Page(params string[] ids)
	{
		PageLayoutV2 page = new PageLayoutV2();
		foreach (string id in ids)
		{
			page.Blocks.Add(new BlockLayout
			{
				Id = id
			});
		}
		return page;
	}

	private static string[] Ids(IEnumerable<BlockLayout> blocks)
	{
		return blocks.Select((BlockLayout b) => b.Id).ToArray();
	}

	private static string[] Ids(IEnumerable<BlockState> blocks)
	{
		return blocks.Select((BlockState b) => b.Id).ToArray();
	}

	private static bool[] Hiddens(IEnumerable<BlockLayout> blocks)
	{
		return blocks.Select((BlockLayout b) => b.Hidden).ToArray();
	}

	private static bool[] Hiddens(IEnumerable<BlockState> blocks)
	{
		return blocks.Select((BlockState b) => b.Hidden).ToArray();
	}

	// ————— 默认布局 = 冻结的 v1 语义(硬约束:首屏不能变) —————

	[Fact]
	public void DefaultLayoutEqualsFrozenV1DefaultSemantics()
	{
		List<LayoutBlockRef> refs = new List<LayoutBlockRef>
		{
			new LayoutBlockRef("hero", true),
			new LayoutBlockRef("quick", true),
			new LayoutBlockRef("news", true),
			new LayoutBlockRef("banner", false)
		};
		LayoutPageState state = LayoutEngine.LoadState("pages:launch", null, refs);

		// 与冻结的 v1 实现逐项对照(PageLayoutDoc.Default 是兼容锚,不能被绕过)
		PageLayoutDoc v1 = PageLayoutDoc.Default(
			refs.Select((LayoutBlockRef r) => r.Id),
			refs.Where((LayoutBlockRef r) => r.DefaultVisible).Select((LayoutBlockRef r) => r.Id));
		Assert.Equal(Ids(v1.Blocks), state.Order.ToArray());
		Assert.Equal(Hiddens(v1.Blocks), state.Order.Select((string id) => state.Hidden.Contains(id)).ToArray());
		Assert.False(state.ExistedInDoc);
	}

	[Fact]
	public void DefaultPlanIsSingleColumnWithOneBlockPerRow()
	{
		LayoutPageState state = LayoutEngine.LoadState("p", null, Refs("a", "b", "c"));
		LayoutPlan plan = state.Plan();
		Assert.True(plan.IsSingleColumn);
		Assert.Equal(1, plan.Columns);
		Assert.Equal(new[] { "a", "b", "c" }, plan.VisibleIds.ToArray());
		Assert.Equal(3, plan.Rows.Count);
		Assert.All(plan.Rows, (LayoutRow row) => Assert.Single(row.Slots));
		Assert.Equal(new[] { 0, 1, 2 }, plan.Slots.Select((LayoutSlot s) => s.VisualIndex).ToArray());
		Assert.Equal(LayoutSchema.GapDefault, plan.Gap);
		Assert.Equal(new[] { 18.0, 14.0, 18.0, 16.0 }, plan.Pad);
	}

	[Fact]
	public void StoredOrderHiddenAndSpanAreRespected()
	{
		PageLayoutV2 page = Page("hero", "news", "quick");
		page.Blocks[0].Span = 1;
		page.Blocks[1].Hidden = true;
		page.Blocks[2].Span = 2;
		LayoutPageState state = LayoutEngine.LoadState("p", page, Refs("hero", "news", "quick"));
		Assert.True(state.ExistedInDoc);
		Assert.Equal(new[] { "hero", "news", "quick" }, state.Order.ToArray());
		Assert.Contains("news", state.Hidden);
		Assert.Equal(2, state.Spans["quick"]);
		// 完整顺序含隐藏项;可视顺序只有两块
		LayoutPlan plan = state.Plan();
		Assert.Equal(new[] { "hero", "news", "quick" }, plan.Slots.Select((LayoutSlot s) => s.Id).ToArray());
		Assert.Equal(new[] { "hero", "quick" }, plan.VisibleIds.ToArray());
		Assert.Equal(-1, plan.Slot("news")!.VisualIndex);
	}

	// ————— 未知块过滤 / 缺失块补尾(Normalize 语义) —————

	[Fact]
	public void UnknownBuiltinIdsAreDroppedAndMissingRefsAppendedHiddenPerDefaultVisible()
	{
		// 文档里有白名单内的 hero / quick(quick 被用户隐藏),还有两个白名单外的内置 id(ghost / news)
		PageLayoutV2 page = Page("hero", "ghost", "quick", "news");
		page.Blocks[2].Hidden = true;
		List<LayoutBlockRef> refs = new List<LayoutBlockRef>
		{
			new LayoutBlockRef("hero", true),
			new LayoutBlockRef("banner", false),
			new LayoutBlockRef("quick", true)
		};
		LayoutPageState state = LayoutEngine.LoadState("p", page, refs);
		// 白名单外的内置 id 丢弃;缺失的 banner 按 DefaultVisible 补到末尾
		Assert.Equal(new[] { "hero", "quick", "banner" }, state.Order.ToArray());
		Assert.DoesNotContain("ghost", state.Order);
		Assert.DoesNotContain("news", state.Order);
		Assert.Contains("quick", state.Hidden); // 文档里的 hidden 保留
		Assert.Contains("banner", state.Hidden); // 缺失项 Hidden = !DefaultVisible(与 PageLayoutDoc.Normalize 一致)
		Assert.DoesNotContain("hero", state.Hidden);
	}

	[Fact]
	public void CustomAndPluginBlocksSurviveWithoutPageSpec()
	{
		PageLayoutV2 page = Page("hero", "custom:w-1", "plugin:hello-plugin:card");
		LayoutPageState state = LayoutEngine.LoadState("p", page, Refs("hero"));
		// 布局文档自己的块(custom: / plugin:)不因页面 spec 里没有就被丢掉(§4.F)
		Assert.Equal(new[] { "hero", "custom:w-1", "plugin:hello-plugin:card" }, state.Order.ToArray());
	}

	// ————— span 跨列(§4.A) —————

	[Fact]
	public void StackColumnsIsOptInOnlyForTwoOrThreeColumns()
	{
		PageLayoutV2 page = new PageLayoutV2();
		Assert.Equal(1, LayoutEngine.StackColumns(page)); // 默认 cols=12 → 单列(与改动前逐像素等价)
		page.Cols = 2;
		Assert.Equal(2, LayoutEngine.StackColumns(page));
		page.Cols = 3;
		Assert.Equal(3, LayoutEngine.StackColumns(page));
		page.Cols = 6;
		Assert.Equal(1, LayoutEngine.StackColumns(page));
		page.Mode = LayoutSchema.ModeGrid;
		page.Cols = 2;
		Assert.Equal(1, LayoutEngine.StackColumns(page)); // grid 模式由 L2 接,现在按单列兜底
	}

	[Fact]
	public void TwoColumnStackPacksSpansIntoRows()
	{
		PageLayoutV2 page = new PageLayoutV2
		{
			Cols = 2
		};
		page.Blocks.Add(new BlockLayout
		{
			Id = "a",
			Span = 1
		});
		page.Blocks.Add(new BlockLayout
		{
			Id = "b",
			Span = 1
		});
		page.Blocks.Add(new BlockLayout
		{
			Id = "c",
			Span = 2
		});
		page.Blocks.Add(new BlockLayout
		{
			Id = "d",
			Span = 1
		});
		LayoutPlan plan = LayoutEngine.Plan("p", page);
		Assert.Equal(2, plan.Columns);
		Assert.Equal(3, plan.Rows.Count);
		Assert.Equal(new[] { "a", "b" }, plan.Rows[0].Slots.Select((LayoutSlot s) => s.Id).ToArray());
		Assert.Equal(0, plan.Rows[0].Slots[0].Column);
		Assert.Equal(1, plan.Rows[0].Slots[1].Column);
		Assert.Equal(new[] { "c" }, plan.Rows[1].Slots.Select((LayoutSlot s) => s.Id).ToArray());
		Assert.Equal(2, plan.Rows[1].Slots[0].Span);
		Assert.Equal(new[] { "d" }, plan.Rows[2].Slots.Select((LayoutSlot s) => s.Id).ToArray());
	}

	[Fact]
	public void SpanIsClampedIntoPageColumns()
	{
		PageLayoutV2 page = new PageLayoutV2
		{
			Cols = 2
		};
		page.Blocks.Add(new BlockLayout
		{
			Id = "a",
			Span = 99
		});
		LayoutPlan plan = LayoutEngine.Plan("p", page);
		Assert.Equal(2, Assert.Single(plan.Slots).Span);
	}

	// ————— 拖拽重排(移植自旧 BlockHost 的相邻中点算法) —————

	[Fact]
	public void DragDownOneSlotSwapsAdjacentBlocks()
	{
		LayoutPageState state = LayoutEngine.LoadState("p", null, Refs("a", "b", "c"));
		Dictionary<string, double> heights = new Dictionary<string, double>
		{
			["a"] = 100.0,
			["b"] = 100.0,
			["c"] = 100.0
		};
		// a 的中点 50,b 的中点 160:指针落到 165 → 与 b 交换(逐格比较)
		state.DragReorder("a", 165.0, heights);
		Assert.Equal(new[] { "b", "a", "c" }, state.Order.ToArray());
	}

	[Fact]
	public void DragStopsAtNeighbourMidpoint()
	{
		LayoutPageState state = LayoutEngine.LoadState("p", null, Refs("a", "b", "c"));
		Dictionary<string, double> heights = new Dictionary<string, double>
		{
			["a"] = 100.0,
			["b"] = 100.0,
			["c"] = 100.0
		};
		// 165 恰好是 b 的中点之下才换位;155 在 b 的中点之上 → 不动
		state.DragReorder("a", 155.0, heights);
		Assert.Equal(new[] { "a", "b", "c" }, state.Order.ToArray());
		state.DragReorder("c", 5.0, heights);
		Assert.Equal(new[] { "c", "a", "b" }, state.Order.ToArray());
	}

	[Fact]
	public void DragKeepsHiddenSlotsInPlace()
	{
		PageLayoutV2 page = Page("a", "b", "c", "d");
		page.Blocks[1].Hidden = true;
		LayoutPageState state = LayoutEngine.LoadState("p", page, Refs("a", "b", "c", "d"));
		Dictionary<string, double> heights = new Dictionary<string, double>
		{
			["a"] = 100.0,
			["c"] = 100.0,
			["d"] = 100.0
		};
		state.DragReorder("a", 500.0, heights); // 一直往下拖到底
		Assert.Equal(new[] { "b", "c", "d", "a" }, state.Order.ToArray()); // 隐藏项 b 保持自己的相对槽位
		Assert.Contains("b", state.Hidden);
	}

	[Fact]
	public void HideAndRestoreKeepOriginalSlot()
	{
		LayoutPageState state = LayoutEngine.LoadState("p", null, Refs("a", "b", "c"));
		state.Hide("a");
		Assert.Equal(new[] { "a", "b", "c" }, state.Order.ToArray()); // 隐藏项仍占位
		Assert.Equal(new[] { "b", "c" }, state.VisibleIds().ToArray());
		state.Restore("a");
		Assert.Equal(new[] { "a", "b", "c" }, state.VisibleIds().ToArray());
		Assert.Equal(0, state.VisibleIndexOf("a"));
	}

	[Fact]
	public void MidpointYMatchesCachedHeightsAndGap()
	{
		List<string> visible = new List<string>
		{
			"a",
			"b"
		};
		Dictionary<string, double> heights = new Dictionary<string, double>
		{
			["a"] = 100.0,
			["b"] = 40.0
		};
		Assert.Equal(50.0, LayoutPageState.MidpointY(visible, heights, 10.0, 0));
		Assert.Equal(130.0, LayoutPageState.MidpointY(visible, heights, 10.0, 1)); // 100 + 10 + 20
		// 没有缓存高度时按 120 兜底(旧实现 H() 的默认值)
		Assert.Equal(60.0, LayoutPageState.MidpointY(visible, null, 10.0, 0));
	}

	// ————— 写回 v2 / v1 投影 —————

	[Fact]
	public void RebuildPageWritesOrderHiddenAndSpan()
	{
		LayoutPageState state = StateWithSpan();
		state.Hide("b");
		PageLayoutV2 page = state.ToPage();
		Assert.Equal(new[] { "a", "b", "c" }, Ids(page.Blocks));
		Assert.Equal(new[] { false, true, false }, Hiddens(page.Blocks));
		Assert.Equal(new[] { 0, 1, 2 }, page.Blocks.Select((BlockLayout b) => b.Z).ToArray());
		Assert.Equal(2, page.Blocks[2].Span);
	}

	private static LayoutPageState StateWithSpan()
	{
		PageLayoutV2 page = Page("a", "b", "c");
		page.Blocks[2].Span = 2;
		return LayoutEngine.LoadState("p", page, Refs("a", "b", "c"));
	}

	[Fact]
	public void ResetRestoresDefaultOrderButKeepsCustomBlocks()
	{
		PageLayoutV2 page = Page("a", "b", "custom:w-9");
		LayoutPageState state = LayoutEngine.LoadState("p", page, Refs("a", "b"));
		state.Hide("a");
		state.Reset(Refs("a", "b"));
		Assert.Equal(new[] { "a", "b", "custom:w-9" }, state.Order.ToArray()); // 顺序回默认,自建块不丢
		Assert.Empty(state.Hidden);
	}

	// ————— 内容工厂钩子(§4.F:custom: / plugin: + 「不可用」) —————

	private sealed class FakeProvider : ILayoutContentProvider
	{
		private readonly string _prefix;

		public FakeProvider(string prefix)
		{
			_prefix = prefix;
		}

		public bool CanBuild(string blockId)
		{
			return blockId != null && blockId.StartsWith(_prefix, StringComparison.Ordinal);
		}

		public object? Build(string blockId, string pageId)
		{
			return "content:" + blockId + "@" + pageId;
		}
	}

	[Fact]
	public void RegistryClassifiesAndResolvesByNamespace()
	{
		Assert.Equal(LayoutContentKind.Builtin, LayoutEngine.Classify("hero"));
		Assert.Equal(LayoutContentKind.Custom, LayoutEngine.Classify("custom:w-1"));
		Assert.Equal(LayoutContentKind.Plugin, LayoutEngine.Classify("plugin:hello:farm"));
		Assert.Equal("w-1", LayoutEngine.WidgetIdOf("custom:w-1"));
		Assert.Null(LayoutEngine.WidgetIdOf("hero"));
		Assert.True(LayoutEngine.TryParsePluginId("plugin:hello:farm", out string plugin, out string inner));
		Assert.Equal("hello", plugin);
		Assert.Equal("farm", inner);
		Assert.False(LayoutEngine.TryParsePluginId("plugin:hello", out string badPlugin, out string badInner));
		Assert.Equal("", badPlugin);
		Assert.Equal("", badInner);
		Assert.False(LayoutEngine.TryParsePluginId("hero", out string noPlugin, out string noInner));
		Assert.Equal("", noPlugin);
		Assert.Equal("", noInner);

		LayoutContentRegistry.Clear();
		try
		{
			// 没有工厂 = 不可用(数据保留,只显示占位)
			Assert.True(LayoutEngine.IsUnavailable("custom:w-1"));
			Assert.True(LayoutEngine.IsUnavailable("plugin:hello:farm"));
			Assert.False(LayoutEngine.IsUnavailable("hero")); // 内置块永远是页面自己给内容

			FakeProvider provider = new FakeProvider("custom:");
			LayoutContentRegistry.Register(provider);
			Assert.Equal(1, LayoutContentRegistry.Count);
			Assert.False(LayoutEngine.IsUnavailable("custom:w-1"));
			Assert.True(LayoutEngine.IsUnavailable("plugin:hello:farm")); // 仍没有插件工厂
			Assert.Equal("content:custom:w-1@pages:launch", LayoutContentRegistry.Build("custom:w-1", "pages:launch"));

			LayoutContentRegistry.Unregister(provider);
			Assert.Equal(0, LayoutContentRegistry.Count);
			Assert.Null(LayoutContentRegistry.Build("custom:w-1", "pages:launch"));
		}
		finally
		{
			LayoutContentRegistry.Clear();
		}
	}

	[Fact]
	public void BrokenProviderDoesNotBreakRendering()
	{
		LayoutContentRegistry.Clear();
		try
		{
			LayoutContentRegistry.Register(new ThrowingProvider());
			Assert.False(LayoutContentRegistry.HasProvider("custom:w-1")); // 工厂抛异常 = 当作造不出来
			Assert.Null(LayoutContentRegistry.Build("custom:w-1", "p"));
		}
		finally
		{
			LayoutContentRegistry.Clear();
		}
	}

	private sealed class ThrowingProvider : ILayoutContentProvider
	{
		public bool CanBuild(string blockId)
		{
			throw new InvalidOperationException("boom");
		}

		public object? Build(string blockId, string pageId)
		{
			throw new InvalidOperationException("boom");
		}
	}

	// ————— 导入 v1 语义文件的兼容路径 —————

	[Fact]
	public void MergeV1TakesOrderAndHiddenButKeepsV2OnlyFields()
	{
		PageLayoutV2 existing = Page("a", "b");
		existing.Blocks[0].Style = "card";
		existing.Blocks[0].Settings["k"] = "v";
		existing.Blocks[1].Span = 2;
		PageLayoutDoc v1 = PageLayoutDoc.Default(new[] { "b", "a", "c" }, new[] { "b" });
		PageLayoutV2 merged = LayoutEngine.MergeV1(existing, v1);
		Assert.Equal(new[] { "b", "a", "c" }, Ids(merged.Blocks));
		Assert.Equal(new[] { false, true, true }, Hiddens(merged.Blocks));
		Assert.Equal("card", merged.Blocks[1].Style); // v2 专属字段按 id 保留
		Assert.Equal("v", merged.Blocks[1].Settings["k"]);
		Assert.Equal(2, merged.Blocks[0].Span);
		Assert.Equal(new[] { 0, 1, 2 }, merged.Blocks.Select((BlockLayout b) => b.Z).ToArray());
	}
}

/// <summary>
/// [A-01] / [B-01] 回归闸门:网格的**行号 Y 与行跨度 H 与列数 cols 无关**。
///
/// 缺陷(1.2.0 既有,不是本轮移植引入):
///   `LayoutSchema.cs` 的 `SanitizeBlock` 非 canvas 分支把 `Y` 钳在 `0..cols-1`、`H` 钳在 `1..cols`,
///   把"行"的上限绑在了"列数"上;而引擎侧的契约是"行数自适应增长"(`LayoutCell.Y`、
///   `LayoutGrid.Pack` 并不钳 Y)。每次 Save/Load/Export/Import 都会跑一遍 `Sanitize`
///   ⇒ 2/3/4/6 列网格里行数多于列数的页面、或 12 列里第 13 行以后的块,重新加载后被压到最后一行并
///   互相重叠;行跨度超过列数行的块被压矮。
///   `LayoutEngine.cs` 的 `Pack` 与 `ResizeCell` 的 `s` 手柄有同一个 `1..cols` 上限,而 `n` 手柄不钳
///   上限 ⇒ 编辑态"从上边拉能超过上限、存盘后又被压回去"。
///
/// 修法:`Y` 只钳下限 `≥0`、`H` 只钳下限 `≥1`,上限统一用**与列数无关**的防御值
/// `LayoutSchema.RowMax`(4096,挡手改 JSON 的天文数字);`Pack` / `ResizeCell` 的 `n` 与 `s`
/// 两个手柄同口径。
///
/// 这些用例全部只用零 WPF 的纯模型类型,能在 net8 测试宿主里跑。
/// </summary>
public class R32McLayoutRowClampTests
{
	/// <summary>造一份最小的合法 v2 文档:一页 + 给定的 blocks 数组正文。</summary>
	private static LayoutV2 Doc(int cols, string blocks)
	{
		return LayoutStoreV2.ParseImport("{\"version\":2,\"kind\":\"tipcl-layout\",\"layout\":{\"pages\":{" +
			"\"p\":{\"version\":2,\"mode\":\"grid\",\"cols\":" + cols + ",\"blocks\":[" + blocks + "]}}}}");
	}

	private static PageLayoutV2 PageOf(LayoutV2 doc)
	{
		return doc.Layout.Pages["p"];
	}

	// ————— A-01:Sanitize 不再按列数钳行号 / 行跨度 —————

	[Theory]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(4)]
	[InlineData(6)]
	[InlineData(12)]
	public void SanitizeKeepsRowsAndRowSpansBeyondColumnCount(int cols)
	{
		int y = cols + 3;      // 行号 > 列数 ⇒ 行数比列数多
		int h = cols + 1;      // 行跨度 > 列数
		LayoutV2 doc = Doc(cols,
			"{\"id\":\"deep\",\"x\":0,\"y\":" + y + ",\"w\":1,\"h\":" + h + "}," +
			"{\"id\":\"wide\",\"x\":99,\"y\":0,\"w\":99,\"h\":1}");

		PageLayoutV2 page = PageOf(doc);
		Assert.Equal(cols, page.Cols);
		BlockLayout deep = page.Blocks.First((BlockLayout b) => b.Id == "deep");
		// 行号 / 行跨度原样保留(1.2.0 会压成 cols-1 / cols)
		Assert.Equal((double)y, deep.Y);
		Assert.Equal((double)h, deep.H);
		// 列方向照旧按列数夹(这次修改不该放宽列)
		BlockLayout wide = page.Blocks.First((BlockLayout b) => b.Id == "wide");
		Assert.Equal((double)(cols - 1), wide.X);
		Assert.Equal((double)cols, wide.W);
		// 原样再进出一次(幂等):已经合法的行号不会被下一次 Sanitize 改小
		LayoutV2 again = LayoutStoreV2.ParseImport(LayoutStoreV2.ExportJson(doc));
		Assert.Equal((double)y, PageOf(again).Blocks.First((BlockLayout b) => b.Id == "deep").Y);
	}

	[Theory]
	[InlineData(2)]
	[InlineData(12)]
	public void AbsurdRowsAreCappedByDefensiveValueNotByColumns(int cols)
	{
		// 防御上限与列数无关:同样的天文数字,2 列与 12 列的结果一样(都是 RowMax)
		// [裁定 R32-D2 · 归档] 施工中途 Core 一度按 Tier-1 取 L2(Y 上限 = RowMax - 1),本断言随之
		// 改成 RowMax - 1;lead 的终局裁定把 Core 的**行方向上/下限统一回 L1 口径**
		// (Y 上限 = RowMax、H ∈ 1..RowMax —— 理由是 L1 已把 Pack / 'n' 手柄 / 's' 手柄 / Sanitize 四处
		//  统一成同一口径,L2 只统一了两处),并要求**判定口径以冻结件 wt-r31-merge @0a304a7 为准**。
		// 故本断言恢复 L1 原文的 RowMax,与本包引擎侧"取 L1 的 'n' 手柄钳位"的裁定同源,两处一致;
		// mac 的 Core 模型层已按该冻结件逐字节重取(见 progress/R32-MC.md 修订 1)。
		LayoutV2 doc = Doc(cols, "{\"id\":\"huge\",\"y\":100000000,\"w\":1,\"h\":100000000}");
		BlockLayout huge = Assert.Single(PageOf(doc).Blocks);
		Assert.Equal((double)LayoutSchema.RowMax, huge.Y);
		Assert.Equal((double)LayoutSchema.RowMax, huge.H);
		// 负值仍然钳到下限(0 行 / 1 行跨度)
		LayoutV2 neg = Doc(cols, "{\"id\":\"neg\",\"y\":-5,\"w\":1,\"h\":-5}");
		BlockLayout block = Assert.Single(PageOf(neg).Blocks);
		Assert.Equal(0.0, block.Y);
		Assert.Equal(1.0, block.H);
	}

	// ————— A-01:Save → Load(磁盘真源)与 Export → Import 都原样保留 —————

	[Fact]
	public void SaveLoadAndExportImportRoundTripKeepRowsBeyondColumnCount()
	{
		string dir = Path.Combine(TestHome.Root, "r32mc-rowclamp-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		LayoutStoreV2.DirOverride = dir;
		LayoutStoreV2.ResetCache();
		try
		{
			// 12 列 + 第 20 行 / 15 行跨度(需求里点名的这组值)
			foreach (int cols in new int[5] { 2, 3, 4, 6, 12 })
			{
				int y = ((cols == 12) ? 20 : (cols + 6));
				int h = ((cols == 12) ? 15 : (cols + 2));
				LayoutV2 doc = Doc(cols, "{\"id\":\"deep\",\"y\":" + y + ",\"w\":1,\"h\":" + h + "}");

				// 1 Save(内部先 Sanitize)→ 清缓存 → Load(重新读盘)
				LayoutStoreV2.Save(doc);
				LayoutStoreV2.ResetCache();
				LayoutV2 loaded = LayoutStoreV2.Load();
				Assert.Equal("v2", LayoutStoreV2.LastLoadSource);
				BlockLayout fromDisk = Assert.Single(PageOf(loaded).Blocks);
				Assert.Equal((double)y, fromDisk.Y);
				Assert.Equal((double)h, fromDisk.H);
				Assert.Equal(cols, PageOf(loaded).Cols);

				// 2 磁盘文件的文本本身就是这份值(直接按文件内容再解析一次,不经过内存缓存)
				string text = File.ReadAllText(LayoutStoreV2.FilePath);
				BlockLayout fromText = Assert.Single(PageOf(LayoutStoreV2.ParseImport(text)).Blocks);
				Assert.Equal((double)y, fromText.Y);
				Assert.Equal((double)h, fromText.H);

				// 3 Export → Import(整个文档的字符串往返)
				BlockLayout fromExport = Assert.Single(
					PageOf(LayoutStoreV2.ParseImport(LayoutStoreV2.ExportJson(loaded))).Blocks);
				Assert.Equal((double)y, fromExport.Y);
				Assert.Equal((double)h, fromExport.H);
			}
		}
		finally
		{
			LayoutStoreV2.DirOverride = null;
			LayoutStoreV2.ResetCache();
			try
			{
				Directory.Delete(dir, true);
			}
			catch
			{
			}
		}
	}

	[Fact]
	public void EditorMoveToDeepRowSurvivesSaveAndReload()
	{
		// 需求 4 的实链路:进编辑拖动(引擎写回块坐标)→ 保存 → 重新载入,行号不能被压回去
		string dir = Path.Combine(TestHome.Root, "r32mc-rowclamp-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		LayoutStoreV2.DirOverride = dir;
		LayoutStoreV2.ResetCache();
		try
		{
			PageLayoutV2 page = new PageLayoutV2
			{
				Mode = LayoutSchema.ModeGrid,
				Cols = 4,
				Gap = 10.0
			};
			page.Blocks.Add(new BlockLayout { Id = "a", X = 0.0, Y = 0.0, W = 4.0, H = 1.0, Span = 4 });
			page.Blocks.Add(new BlockLayout { Id = "b", X = 0.0, Y = 1.0, W = 4.0, H = 1.0, Span = 4 });
			LayoutV2 doc = LayoutV2.CreateDefault();
			doc.Layout.Pages["p"] = page;

			// 拖动:第二块往下 7 行(4 列 + 第 8 行 ⇒ 行数远多于列数)
			List<LayoutCell> cells = LayoutGrid.Pack(page);
			Assert.True(LayoutGrid.MoveCell(cells[1], 0, 7, 4, 0));
			LayoutGrid.Apply(page, cells);
			Assert.Equal(8.0, page.Blocks.First((BlockLayout b) => b.Id == "b").Y);

			LayoutStoreV2.Save(doc);
			LayoutStoreV2.ResetCache();
			LayoutV2 loaded = LayoutStoreV2.Load();
			PageLayoutV2 reloaded = loaded.Layout.Pages["p"];
			Assert.Equal(8.0, reloaded.Blocks.First((BlockLayout b) => b.Id == "b").Y);
			Assert.Equal(0.0, reloaded.Blocks.First((BlockLayout b) => b.Id == "a").Y);
			// 再 Pack 一次(等价于重新打开页面排版):行号仍然是 8,没有被折叠到第 3 行
			Assert.Equal(8, LayoutGrid.Pack(reloaded).First((LayoutCell c) => c.Id == "b").Y);
		}
		finally
		{
			LayoutStoreV2.DirOverride = null;
			LayoutStoreV2.ResetCache();
			try
			{
				Directory.Delete(dir, true);
			}
			catch
			{
			}
		}
	}

	// ————— B-01:Pack 与竖直线柄同口径 —————

	[Fact]
	public void PackKeepsRowSpanBeyondColumnCount()
	{
		PageLayoutV2 page = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeGrid,
			Cols = 2,
			Gap = 10.0
		};
		page.Blocks.Add(new BlockLayout { Id = "tall", X = 0.0, Y = 0.0, W = 2.0, H = 5.0, Span = 2 });
		LayoutCell cell = Assert.Single(LayoutGrid.Pack(page));
		Assert.Equal(5, cell.H);            // 2 列的页面里 5 行跨度必须留住(1.2.0 会压成 2)
		Assert.Equal(0, cell.Y);
	}

	private static LayoutCell At(LayoutCell cell, int x, int y, int w, int h)
	{
		cell.X = x;
		cell.Y = y;
		cell.W = w;
		cell.H = h;
		return cell;
	}

	[Fact]
	public void VerticalHandlesShareTheSameRowCap()
	{
		// 's'(下边):只钳下限 1 + 防御上限 RowMax,上边界钉住不动
		LayoutCell south = At(new LayoutCell { Id = "s" }, 0, 0, 1, 1);
		Assert.True(LayoutGrid.ResizeCell(south, "s", 0, 100000, 2));
		Assert.Equal(LayoutSchema.RowMax, south.H);
		Assert.Equal(0, south.Y);

		// 'n'(上边):同一口径(下边界钉住不动)⇒ 也撑不过 RowMax;过去这里不钳上限、's' 钳在 cols
		LayoutCell north = At(new LayoutCell { Id = "n" }, 0, LayoutSchema.RowMax, 1, 2);
		Assert.True(LayoutGrid.ResizeCell(north, "n", 0, -100000, 2));
		Assert.Equal(LayoutSchema.RowMax, north.H);
		Assert.Equal(2, north.Y);           // 下边界 = RowMax + 2,高度夹到 RowMax 后上边界落在第 2 行
		Assert.Equal(south.H, north.H);     // 两个竖直手柄的上限完全一致

		// 常规缩放仍然照旧(小数值不受上限影响),且行跨度不再受列数限制
		LayoutCell small = At(new LayoutCell { Id = "x" }, 1, 1, 2, 2);
		Assert.True(LayoutGrid.ResizeCell(small, "s", 0, 1, 2));
		Assert.Equal(3, small.H);           // cols=2 但行跨度可以到 3
		Assert.Equal(1, small.Y);

		// [任务0 对齐] 另一条重复实现(外部 agent 的 AuditFixTests)里有一条我方原先没覆盖的断言:
		// **非饱和的小数值**下两个竖直手柄同样能越过列数,而且 'n' 手柄保持下边界钉住不动。
		// 我方原来只测了"天文数字 ⇒ 夹到 RowMax",这条踩的是真实的拖动量级,属真空白,按同一口径并进来。
		LayoutCell south2 = At(new LayoutCell { Id = "s2" }, 0, 0, 1, 2);
		Assert.True(LayoutGrid.ResizeCell(south2, "s", 0, 3, 2));
		Assert.Equal(5, south2.H);          // 2 列的页面里行跨度 2 → 5(1.2.0 会夹到 2)
		Assert.Equal(0, south2.Y);          // 's' 上边界不动
		LayoutCell north2 = At(new LayoutCell { Id = "n2" }, 0, 6, 1, 2);
		Assert.True(LayoutGrid.ResizeCell(north2, "n", 0, -3, 2));
		Assert.Equal(5, north2.H);
		Assert.Equal(3, north2.Y);          // 'n' 下边界钉住(6+2=8)⇒ 上边界 = 8-5 = 3
	}
}

/// <summary>
/// 自定义布局修复(两份规格)的纯模型回归闸门。全部只用**零 WPF** 的类型
/// (LayoutPageState / LayoutEngine / LayoutGrid / LayoutWheelRouting / LayoutHostPick),
/// 因为测试宿主是 net8:一旦碰到 LayoutEditor / BlockHost / LayoutShellHooks(WPF)就会因为
/// 加载不到 net48 的 PresentationFramework 而炸 —— 那几处由 --shot 与真机手测覆盖。
///
/// 覆盖:
///   §3-问题7 第 2 点  → RebuildPageKeepsCoordinatesSizeAndAppearance(独立复现的那条 bug)
///   §3-问题7 重置     → ResetClearsBlocksSoFactoryLayoutCanBeReloaded
///   §3-问题7 stack 折算 → StackToGridFoldsRowsProportionally
///   §3-问题7 让位     → PromoteToFrontMovesTouchedBlocksFirst
///   §4-能力2 defaultPage → DefaultPageIsUsedWhenDocHasNoEntryAndPersistsOnFirstWrite
///   规格2-问题2.1     → CompactFloatsBlocksUpWithoutLeavingHoles / CompactSwapsColumnsWhenThatSitsHigher /
///                        FirstFreeInColumnHRespectsBlockHeight
///   规格2-问题2.2     → RowAtUsesRowHeightSnapshot
///   §3-问题3          → WheelRoutingPrefersInnermostThenOuter / WheelRoutingFallsBackToPageWidest
///   §4-能力1          → HostPickPrefersInnermostThenSmallestThenLargest
/// </summary>
public class R32McLayoutFixTests
{
	private static List<LayoutBlockRef> Refs(params string[] ids)
	{
		List<LayoutBlockRef> list = new List<LayoutBlockRef>();
		foreach (string id in ids)
		{
			list.Add(new LayoutBlockRef(id, true));
		}
		return list;
	}

	private static BlockLayout Block(string id, double x, double y, double w, double h)
	{
		return new BlockLayout
		{
			Id = id,
			X = x,
			Y = y,
			W = w,
			H = h,
			Span = (int)w
		};
	}

	private static PageLayoutV2 GridPage(int cols, params BlockLayout[] blocks)
	{
		PageLayoutV2 page = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeGrid,
			Cols = cols,
			Gap = 10.0
		};
		foreach (BlockLayout block in blocks)
		{
			page.Blocks.Add(block);
		}
		return page;
	}

	// ————— §3-问题7 第 2 点:原有 bug(拖完的坐标与外观在下一次重建时被清空) —————

	[Fact]
	public void RebuildPageKeepsCoordinatesSizeAndAppearance()
	{
		// 这一条对应外部独立复现的 bug:旧 RebuildPage 每块只写回 Id/Hidden/Span/Z,
		// 于是 x/y/w/h 与 style/title/pad/min_h/settings 到不了渲染器(两次"只改坐标"的截图 sha256 相同)。
		PageLayoutV2 page = GridPage(12, Block("a", 3, 2, 4, 2), Block("b", 0, 0, 12, 1));
		page.Blocks[0].Style = LayoutSchema.StyleCard;
		page.Blocks[0].Title = true;
		page.Blocks[0].Pad = new double[4] { 1.0, 2.0, 3.0, 4.0 };
		page.Blocks[0].MinH = 77.0;
		page.Blocks[0].Settings["k"] = "v";

		LayoutPageState state = LayoutEngine.LoadState("p", page, Refs("a", "b"));
		// 任何一次"重建"(Plan / ToPage 都会走它)都不能把用户拖出来的东西清掉
		state.Plan();
		state.ToPage();

		BlockLayout a = state.Page.Blocks.First((BlockLayout x) => x.Id == "a");
		Assert.Equal(3.0, a.X);
		Assert.Equal(2.0, a.Y);
		Assert.Equal(4.0, a.W);
		Assert.Equal(2.0, a.H);
		Assert.Equal(LayoutSchema.StyleCard, a.Style);
		Assert.True(a.Title);
		Assert.Equal(new double[4] { 1.0, 2.0, 3.0, 4.0 }, a.Pad!);
		Assert.Equal(77.0, a.MinH);
		Assert.Equal("v", a.Settings["k"]);

		// 顺序 / 显隐 / span 仍然按状态表刷新(这是 RebuildPage 的原有职责)
		state.Hide("b");
		Assert.True(state.Page.Blocks.First((BlockLayout x) => x.Id == "b").Hidden);
		Assert.Equal(3.0, state.Page.Blocks.First((BlockLayout x) => x.Id == "a").X);
	}

	// ————— §3-问题7:重置 = 清空(出厂布局由宿主用 defaultPage 铺回来) —————

	[Fact]
	public void ResetClearsBlocksSoFactoryLayoutCanBeReloaded()
	{
		PageLayoutV2 page = GridPage(12, Block("a", 5, 5, 6, 3), Block("b", 0, 0, 6, 1));
		page.Blocks[0].Style = LayoutSchema.StyleDivider;
		page.Blocks[0].Settings["x"] = 1;
		LayoutPageState state = LayoutEngine.LoadState("p", page, Refs("a", "b"));

		state.Reset(Refs("a", "b"));

		BlockLayout a = state.Page.Blocks.First((BlockLayout x) => x.Id == "a");
		Assert.Equal(0.0, a.X);
		Assert.Equal(0.0, a.Y);
		Assert.Equal(1.0, a.W);
		Assert.Equal(LayoutSchema.StylePlain, a.Style);
		Assert.Empty(a.Settings);
		// 白名单顺序仍然在(块本身不删数据,只是回到默认几何)
		Assert.Equal(new[] { "a", "b" }, state.Order.ToArray());

		// 编辑器那边的「重置本页」= LoadState(pageId, defaultPage) —— 出厂模式也一起回来
		PageLayoutV2 factory = GridPage(12, Block("a", 0, 0, 4, 1), Block("b", 4, 0, 8, 1));
		LayoutPageState reset = LayoutEngine.LoadState("p", factory, Refs("a", "b"), false);
		Assert.Equal(LayoutSchema.ModeGrid, reset.Page.Mode);
		Assert.Equal(4.0, reset.Page.Blocks.First((BlockLayout x) => x.Id == "a").W);
		Assert.Equal(4.0, reset.Page.Blocks.First((BlockLayout x) => x.Id == "b").X);
		// 没有出厂布局的页:回白名单默认 = 单列 stack
		LayoutPageState plain = LayoutEngine.LoadState("p", null, Refs("a", "b"), false);
		Assert.Equal(LayoutSchema.ModeStack, plain.Page.Mode);
	}

	// ————— §3-问题7:stack → 12 列网格的逐行折算 —————

	[Fact]
	public void StackToGridFoldsRowsProportionally()
	{
		// 两栏 stack 页:第一行两块各占 1 列(→ 6/6),第二行整行一块(→ 12)
		PageLayoutV2 page = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeStack,
			Cols = 2,
			Gap = 10.0
		};
		page.Blocks.Add(new BlockLayout { Id = "a", Span = 1 });
		page.Blocks.Add(new BlockLayout { Id = "b", Span = 1 });
		page.Blocks.Add(new BlockLayout { Id = "c", Span = 2 });

		Assert.True(LayoutEngine.StackToGrid(page, "p"));
		Assert.Equal(LayoutSchema.ModeGrid, page.Mode);
		Assert.Equal(12, page.Cols);
		Assert.Equal(0.0, page.RowH);

		BlockLayout a = page.Blocks[0];
		BlockLayout b = page.Blocks[1];
		BlockLayout c = page.Blocks[2];
		Assert.Equal(new double[] { 0, 0, 6, 1 }, new double[] { a.X, a.Y, a.W, a.H });
		Assert.Equal(new double[] { 6, 0, 6, 1 }, new double[] { b.X, b.Y, b.W, b.H });
		Assert.Equal(new double[] { 0, 1, 12, 1 }, new double[] { c.X, c.Y, c.W, c.H });
		Assert.Equal(6, a.Span);
		// 单列页:每块整行
		PageLayoutV2 single = new PageLayoutV2 { Mode = LayoutSchema.ModeStack, Cols = 12 };
		single.Blocks.Add(new BlockLayout { Id = "a", Span = 1 });
		single.Blocks.Add(new BlockLayout { Id = "b", Span = 1 });
		Assert.True(LayoutEngine.StackToGrid(single, "p"));
		Assert.Equal(12.0, single.Blocks[0].W);
		// 单列页每块独占一行 → 第一块第 0 行、第二块第 1 行,都铺满整行、左边归零
		Assert.Equal(0.0, single.Blocks[0].Y);
		Assert.Equal(1.0, single.Blocks[1].Y);
		Assert.Equal(0.0, single.Blocks[1].X);
		Assert.Equal(12.0, single.Blocks[1].W);
		// 已经是 grid 的页不折算
		Assert.False(LayoutEngine.StackToGrid(GridPage(12, Block("a", 0, 0, 4, 1)), "p"));
	}

	// ————— §3-问题7:被拖块优先占住落点 —————

	[Fact]
	public void PromoteToFrontMovesTouchedBlocksFirst()
	{
		LayoutPageState state = LayoutEngine.LoadState("p", GridPage(12, Block("a", 0, 0, 4, 1), Block("b", 4, 0, 4, 1), Block("c", 8, 0, 4, 1)), Refs("a", "b", "c"));
		state.PromoteToFront(new[] { "c" });
		Assert.Equal(new[] { "c", "a", "b" }, state.Order.ToArray());
		// Z 跟着表序走(Pack 的抢占顺序就是这个顺序)
		Assert.Equal(0, state.Page.Blocks[0].Z);
		Assert.Equal(1, state.Page.Blocks[1].Z);
		// 不认识的 id / 空表不动
		state.PromoteToFront(new[] { "nope" });
		Assert.Equal(new[] { "c", "a", "b" }, state.Order.ToArray());
		state.PromoteToFront(null);
		Assert.Equal(new[] { "c", "a", "b" }, state.Order.ToArray());
	}

	// ————— 规格2-问题2.1:压紧(重力上浮) —————

	[Fact]
	public void CompactFloatsBlocksUpWithoutLeavingHoles()
	{
		// b 被旧存档留在了很靠下的行(a 在第一行)→ 压紧后 b 浮上来,不留空洞
		List<LayoutCell> cells = new List<LayoutCell>
		{
			new LayoutCell { Id = "a", X = 0, Y = 0, W = 6, H = 1 },
			new LayoutCell { Id = "b", X = 6, Y = 4, W = 6, H = 1 }
		};
		LayoutGrid.Compact(cells, 12, null, null);
		Assert.Equal(0, cells[0].Y);
		Assert.Equal(0, cells[1].Y);
		Assert.Equal(1, LayoutGrid.RowCount(cells));
	}

	[Fact]
	public void CompactKeepsPinnedBlockAndFloatsTheRestAroundIt()
	{
		// 被拖的 b 钉在第二行;其余块仍然上浮到各自能放下的最高处
		List<LayoutCell> cells = new List<LayoutCell>
		{
			new LayoutCell { Id = "a", X = 0, Y = 3, W = 6, H = 1 },
			new LayoutCell { Id = "b", X = 0, Y = 1, W = 12, H = 1 }
		};
		LayoutGrid.Compact(cells, 12, new HashSet<string>(new[] { "b" }), null);
		Assert.Equal(1, cells[1].Y);          // 钉住的不动
		Assert.Equal(0, cells[0].Y);          // 第一行还空着,a 浮上去
	}

	[Fact]
	public void CompactSwapsColumnsWhenThatSitsHigher()
	{
		// 实例库场景:文件夹栏(0..4)被拖到 8..12,版本选择(4..12)与它冲突。
		// 备选列 = 被拖块原来的列(0):版本选择换到左边能坐到第 0 行 → 左右互换成功。
		List<LayoutCell> cells = new List<LayoutCell>
		{
			new LayoutCell { Id = "folders", X = 8, Y = 0, W = 4, H = 1 },
			new LayoutCell { Id = "versions", X = 4, Y = 0, W = 8, H = 1 }
		};
		Dictionary<string, int> swap = new Dictionary<string, int>(StringComparer.Ordinal)
		{
			["versions"] = 0
		};
		LayoutGrid.Compact(cells, 12, new HashSet<string>(new[] { "folders" }), swap);
		LayoutCell folders = cells[0];
		LayoutCell versions = cells[1];
		Assert.Equal(8, folders.X);
		Assert.Equal(0, folders.Y);
		Assert.Equal(0, versions.X);       // 换到了被拖块原来的列
		Assert.Equal(0, versions.Y);       // 而且没有被挤到下一行
	}

	[Fact]
	public void FirstFreeInColumnHRespectsBlockHeight()
	{
		// [layout-fix] V-3:这条用例必须真的钉住"按块实际高度(H 行)判空"——
		// 上一轮的形状(高块 y 的 W=12 铺满整行)两个实现算出的 Y 一样,所以把 FirstFreeInColumnH 的
		// 判空改成恒按 1 行(忽略参数 h)时它**仍然是绿的**(变异验证 M3 就是这么漏过去的)。
		// 现在用"落点完全由 H 决定"的形状:第 1 行被占、第 0 行空着,高块要 2 行 ⇒ 只能落到第 2 行。
		// 恒按 1 行判空的实现会把它放到第 0 行(压住下面那块)⇒ 必红。
		List<LayoutCell> blockedBelow = new List<LayoutCell>
		{
			new LayoutCell { Id = "pin", X = 0, Y = 1, W = 6, H = 1 },
			new LayoutCell { Id = "tall", X = 0, Y = 1, W = 6, H = 2 }
		};
		LayoutGrid.Compact(blockedBelow, 12, new HashSet<string>(new[] { "pin" }), null);
		Assert.Equal(1, blockedBelow[0].Y);   // 钉住的不动(注意它上面第 0 行是空的)
		Assert.Equal(2, blockedBelow[1].Y);   // 高 2 ⇒ 第 0 行装不下(会压到第 1 行的 pin)⇒ 落到第 2 行
		// 同一形状、高度换成 1:第 0 行就放得下 ⇒ 落点确实由高度决定(对照)
		List<LayoutCell> flatInSameColumn = new List<LayoutCell>
		{
			new LayoutCell { Id = "pin", X = 0, Y = 1, W = 6, H = 1 },
			new LayoutCell { Id = "flat", X = 0, Y = 1, W = 6, H = 1 }
		};
		LayoutGrid.Compact(flatInSameColumn, 12, new HashSet<string>(new[] { "pin" }), null);
		Assert.Equal(0, flatInSameColumn[1].Y);

		// 原始形状(高块铺满整行)作为回归保留
		List<LayoutCell> tall = new List<LayoutCell>
		{
			new LayoutCell { Id = "x", X = 0, Y = 0, W = 6, H = 1 },
			new LayoutCell { Id = "y", X = 0, Y = 1, W = 12, H = 2 }
		};
		LayoutGrid.Compact(tall, 12, null, null);
		// x 先落(0,0);y 要 2 行,在列 0 放不下第 1 行(被 x 占),于是落到第 1 行
		Assert.Equal(0, tall[0].Y);
		Assert.Equal(1, tall[1].Y);
		// 一行块的场景:第二块可以浮到第一行
		List<LayoutCell> flat = new List<LayoutCell>
		{
			new LayoutCell { Id = "x", X = 0, Y = 0, W = 6, H = 1 },
			new LayoutCell { Id = "y", X = 6, Y = 5, W = 6, H = 1 }
		};
		LayoutGrid.Compact(flat, 12, null, null);
		Assert.Equal(0, flat[1].Y);
	}

	// ————— 规格2-问题2.2:行定位用行高快照,不用卡片自身高度 —————

	[Fact]
	public void RowAtUsesRowHeightSnapshot()
	{
		double[] rows = new double[] { 100.0, 100.0, 100.0 };
		// 行带 = [0,100) / [110,210) / [220,320)(间距 10,容差半个间距)
		Assert.Equal(0, LayoutGrid.RowAt(rows, 0.0, 10.0));
		Assert.Equal(0, LayoutGrid.RowAt(rows, 104.0, 10.0));
		Assert.Equal(1, LayoutGrid.RowAt(rows, 106.0, 10.0));
		Assert.Equal(1, LayoutGrid.RowAt(rows, 200.0, 10.0));
		Assert.Equal(2, LayoutGrid.RowAt(rows, 230.0, 10.0));
		// 越过最后一行 → 下一行(允许往画布下方拖出新行)
		Assert.Equal(3, LayoutGrid.RowAt(rows, 400.0, 10.0));
		// 负值归 0;空表恒 0
		Assert.Equal(0, LayoutGrid.RowAt(rows, -50.0, 10.0));
		Assert.Equal(0, LayoutGrid.RowAt(new double[0], 500.0, 10.0));
	}

	// ————— §4-能力2:defaultPage 兜底 + 首次落盘 —————

	[Fact]
	public void DefaultPageIsUsedWhenDocHasNoEntryAndPersistsOnFirstWrite()
	{
		string dir = Path.Combine(TestHome.Root, "r32mc-layoutfix-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		LayoutStoreV2.DirOverride = dir;
		// [移植差异] Windows 用 Pages 层 PagesLayoutV1Bridge;mac 树还没有那个生产文件,
		// 改用同测试程序集的 R32McV1Bridge(R32McLayoutV2Fixtures.cs 里的等价实现,语义逐条照抄)。
		LayoutStoreV2.V1Bridge = new R32McV1Bridge();
		PageLayoutStore.DirOverride = dir;
		LayoutStoreV2.ResetCache();
		try
		{
			PageLayoutV2 factory = GridPage(12, Block("folders", 0, 0, 4, 1), Block("versions", 4, 0, 8, 1));
			List<LayoutBlockRef> refs = Refs("folders", "versions");
			// 文档里还没有这一页(PageOf 返回 null)→ 用 defaultPage 兜底,并且**不要**被当成"文档里本来就有"
			LayoutV2 doc = LayoutStoreV2.Load();
			PageLayoutV2? stored = LayoutVariants.PageOf(doc.Layout, "library:main", 0.0, LayoutVariantHost.Threshold);
			Assert.Null(stored);
			LayoutPageState state = LayoutEngine.LoadState("library:main", factory, refs, stored != null);
			Assert.False(state.ExistedInDoc);
			Assert.Equal(LayoutSchema.ModeGrid, state.Page.Mode);
			Assert.Equal(4.0, state.Page.Blocks.First((BlockLayout b) => b.Id == "versions").X);
			Assert.False(File.Exists(Path.Combine(dir, LayoutStoreV2.FileName)));

			// 第一次真实改动落盘之后,文档里就有这一页了(后续读到的就是用户自己那份)
			LayoutEngine.ApplyPage(doc, "library:main", state.ToPage());
			LayoutStoreV2.Save(doc);
			LayoutStoreV2.ResetCache();
			LayoutV2 again = LayoutStoreV2.Load();
			PageLayoutV2? persisted = LayoutVariants.PageOf(again.Layout, "library:main", 0.0, LayoutVariantHost.Threshold);
			Assert.NotNull(persisted);
			Assert.Equal(8.0, persisted!.Blocks.First((BlockLayout b) => b.Id == "versions").W);
			Assert.Equal(4.0, persisted.Blocks.First((BlockLayout b) => b.Id == "folders").W);
			Assert.Equal(LayoutSchema.ModeGrid, persisted.Mode);
		}
		finally
		{
			LayoutStoreV2.DirOverride = null;
			LayoutStoreV2.V1Bridge = null;
			PageLayoutStore.DirOverride = null;
			LayoutStoreV2.ResetCache();
			try
			{
				Directory.Delete(dir, true);
			}
			catch
			{
			}
		}
	}

	// ————— §3-问题3:滚轮路由(纯判定) —————

	[Fact]
	public void WheelRoutingPrefersInnermostThenOuter()
	{
		// 最内层能滚 → 交给原生(0)
		Assert.Equal(0, LayoutWheelRouting.Route(new[] { true, true }, true));
		// 最内层滚不动、外层能滚 → 转交外层(1)
		Assert.Equal(1, LayoutWheelRouting.Route(new[] { false, true }, true));
		// 整条链都到头 → 不接管(-1)
		Assert.Equal(LayoutWheelRouting.NotScrollable, LayoutWheelRouting.Route(new[] { false, false }, true));
		// 完全不在滚动容器里 → 滚页面最大可滚区域(-2)
		Assert.Equal(LayoutWheelRouting.OutsideAnyScroll, LayoutWheelRouting.Route(new List<bool>(), false));
		// 方向语义:向下滚到底 / 向上滚到顶都算"滚不动"
		Assert.True(LayoutWheelRouting.CanScroll(0.0, 100.0, -1.0));
		Assert.False(LayoutWheelRouting.CanScroll(100.0, 100.0, -1.0));
		Assert.False(LayoutWheelRouting.CanScroll(0.0, 100.0, 1.0));
		Assert.True(LayoutWheelRouting.CanScroll(50.0, 100.0, 1.0));
		// 没有可滚高度 = 不能滚(虚拟列表在外层容器里拿到无限高就是这一种)
		Assert.False(LayoutWheelRouting.CanScroll(0.0, 0.0, -1.0));
	}

	[Fact]
	public void WheelRoutingFallsBackToPageWidest()
	{
		// 两个候选区(都不在鼠标下):选面积最大且当前方向还能滚的那个
		List<double> areas = new List<double> { 100.0, 900.0 };
		List<bool> canScroll = new List<bool> { true, true };
		Assert.Equal(1, LayoutWheelRouting.Widest(areas, canScroll));
		// 最大的那个滚不动了 → 退而求其次
		List<bool> onlySmall = new List<bool> { true, false };
		Assert.Equal(0, LayoutWheelRouting.Widest(areas, onlySmall));
		// 都滚不动 / 空表 → -1
		Assert.Equal(-1, LayoutWheelRouting.Widest(areas, new List<bool> { false, false }));
		Assert.Equal(-1, LayoutWheelRouting.Widest(null, null));
		// 偏移夹取
		Assert.Equal(0.0, LayoutWheelRouting.OffsetAfter(0.0, 100.0, 120.0));
		Assert.Equal(100.0, LayoutWheelRouting.OffsetAfter(90.0, 100.0, -120.0));
	}

	// ————— §4-能力1:右键目标宿主的选取顺序(纯判定) —————

	[Fact]
	public void HostPickPrefersInnermostThenSmallestThenLargest()
	{
		List<LayoutRect> rects = new List<LayoutRect>
		{
			new LayoutRect(0.0, 0.0, 900.0, 600.0),    // 0 = 外层大宿主
			new LayoutRect(400.0, 100.0, 300.0, 200.0) // 1 = 内层小宿主
		};
		// ① 鼠标在某个宿主里(事件来源链上最内层)→ 直接选它
		Assert.Equal(1, LayoutHostPick.Resolve(1, rects, 10.0, 10.0));
		// ② 事件来源不在任何宿主里(启动页空白穿透)→ 选"包含鼠标位置的最小宿主"
		Assert.Equal(1, LayoutHostPick.Resolve(LayoutHostPick.None, rects, 450.0, 150.0));
		// ③ 鼠标在页面里但不在任何宿主里 → 选最大的宿主
		Assert.Equal(0, LayoutHostPick.Resolve(LayoutHostPick.None, rects, 10.0, 10.0));
		Assert.Equal(LayoutHostPick.None, LayoutHostPick.Resolve(LayoutHostPick.None, new List<LayoutRect>(), 10.0, 10.0));
		// 没有有效矩形的宿主不参与(算不出几何时给的是空矩形)
		Assert.Equal(1, LayoutHostPick.Largest(new List<LayoutRect> { default(LayoutRect), rects[1] }));
		Assert.Equal(0, LayoutHostPick.SmallestContaining(rects, 10.0, 10.0));
	}
}

/// <summary>
/// R31 布局审计(progress/R31-LAYOUT-AUDIT.md)里覆盖**本包并入的 3 处增量**的回归用例。
/// 来源:Windows 版参考仓库 tests/TipCL.Tests/AuditFixTests.cs(分支 fix/r31-ui-audit @ 33b5fff)。
/// 逐条裁定(见 progress/R32-MC.md 的差异表):
///   ✅ 搬入:NarrowVariantIsTheSourceOnlyWhenItHasThePage / ApplyPageWritesBackToTheVariantItWasReadFrom
///           (覆盖 LayoutVariants.SourceOf 与 LayoutEngine.ApplyPage(variant 重载))、
///           WidgetSignatureChangesOnlyWhenThatDefinitionChanges(覆盖 LayoutEngine.WidgetSignature)。
///   ⛔ 不搬:SanitizeKeepsGridRowsBeyondColumnCount / PackKeepsRowSpanTallerThanColumnCount /
///           SouthHandleGrowsPastColumnCountLikeNorthHandle
///           —— 与 L1 的 LayoutRowClampTests 同源(mac 侧由 R32McLayoutRowClampTests 承担覆盖)。
///   ✅ 搬入(修订 1 追加):SanitizeStillCapsAbsurdGridRows —— 裁定 R32-D2 的钉子,放在
///           R32McLayoutV2ModelTests(模型层,与 Core 的 Sanitize 直接对应;原 L2 版取 RowMax-1,
///           按冻结件改回 RowMax)。
///   ⛔ 不搬:Import* 三条走 PageLayoutIO.MergeImport(本包地盘外的 Pages 层文件,mac 树尚无)。
/// </summary>
public class R32McLayoutAuditFixTests
{	// ————— D-03:窄窗下显示的是变体,改动写回变体,不盖掉宽版 —————

	[Fact]
	public void NarrowVariantIsTheSourceOnlyWhenItHasThePage()
	{
		LayoutBody body = new LayoutBody();
		body.Pages["p"] = new PageLayoutV2();
		body.Pages["q"] = new PageLayoutV2();
		body.Variants[LayoutVariants.Narrow] = new LayoutBody();
		body.Variants[LayoutVariants.Narrow].Pages["p"] = new PageLayoutV2();

		Assert.Equal(LayoutVariants.Narrow, LayoutVariants.SourceOf(body, "p", 600, 760));
		Assert.Equal("", LayoutVariants.SourceOf(body, "p", 1200, 760));
		Assert.Equal("", LayoutVariants.SourceOf(body, "q", 600, 760));
		Assert.Equal("", LayoutVariants.SourceOf(body, "p", 0, 760));
	}

	[Fact]
	public void ApplyPageWritesBackToTheVariantItWasReadFrom()
	{
		LayoutV2 doc = new LayoutV2();
		PageLayoutV2 wide = new PageLayoutV2 { Blocks = new List<BlockLayout> { new BlockLayout { Id = "a", W = 8, H = 1 } } };
		doc.Layout.Pages["p"] = wide;
		PageLayoutV2 narrow = new PageLayoutV2 { Blocks = new List<BlockLayout> { new BlockLayout { Id = "a", W = 12, H = 2 } } };

		LayoutEngine.ApplyPage(doc, "p", narrow, LayoutVariants.Narrow);
		Assert.Same(wide, doc.Layout.Pages["p"]);
		Assert.Same(narrow, doc.Layout.Variants[LayoutVariants.Narrow].Pages["p"]);

		PageLayoutV2 edited = narrow.Clone();
		LayoutEngine.ApplyPage(doc, "p", edited, "");
		Assert.Same(edited, doc.Layout.Pages["p"]);
		Assert.Same(narrow, doc.Layout.Variants[LayoutVariants.Narrow].Pages["p"]);
	}

	// ————— F-01:自建块定义变了才重建那张卡 —————

	[Fact]
	public void WidgetSignatureChangesOnlyWhenThatDefinitionChanges()
	{
		LayoutV2 doc = new LayoutV2();
		WidgetDef note = new WidgetDef { Id = "w-note", Type = "note", Title = "便签" };
		note.Settings["text"] = "第一版";
		doc.Layout.Widgets.Add(note);
		doc.Layout.Widgets.Add(new WidgetDef { Id = "w-clock", Type = "clock", Title = "时钟" });

		string? before = LayoutEngine.WidgetSignature(doc.Clone().Layout, "custom:w-note");
		Assert.False(string.IsNullOrEmpty(before));
		Assert.Equal(before, LayoutEngine.WidgetSignature(doc.Clone().Layout, "custom:w-note"));

		doc.Layout.Widgets[1].Title = "别的时钟";
		Assert.Equal(before, LayoutEngine.WidgetSignature(doc.Clone().Layout, "custom:w-note"));

		note.Settings["text"] = "第二版";
		Assert.NotEqual(before, LayoutEngine.WidgetSignature(doc.Clone().Layout, "custom:w-note"));

		Assert.Equal("", LayoutEngine.WidgetSignature(doc.Layout, "custom:w-gone"));
		Assert.Equal("", LayoutEngine.WidgetSignature(null, "custom:w-note"));
		Assert.Null(LayoutEngine.WidgetSignature(doc.Layout, "hero"));
	}
}
