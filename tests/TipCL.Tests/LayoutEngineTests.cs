using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Pages;
using TipCL.Pages.Layout;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 布局引擎(纯模型层)测试:默认布局与冻结的 v1 语义等价、顺序/显隐/span 的排布与命中、
/// 拖拽重排算法(移植自旧 BlockHost)、内容工厂钩子(custom: / plugin: / 不可用)。
/// 测试宿主是 net8:只碰不依赖 WPF 的类型(LayoutEngine / LayoutPageState / LayoutStoreV2),不实例化 BlockHost。
/// </summary>
public class LayoutEngineTests
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
