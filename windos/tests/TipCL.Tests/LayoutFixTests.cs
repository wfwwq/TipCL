using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TipCL.Pages;
using TipCL.Pages.Layout;
using Xunit;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Tests;

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
public class LayoutFixTests
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
		// 高度 2 的块:第一行虽然"第一格"空着,但它要 2 行 → 必须整体让开
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
		string dir = Path.Combine(Path.GetTempPath(), "tipcl_layoutfix_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		LayoutStoreV2.DirOverride = dir;
		LayoutStoreV2.V1Bridge = new PagesLayoutV1Bridge();
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
