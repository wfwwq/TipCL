// 布局修复(第三批,Linux 侧):「重置本页」真正回到出厂布局,且自建 / 插件块的数据不丢。
// 对照 Windows 提交 6c520ca(LayoutPageState.Reset 清几何 + LayoutEditor.ResetPage 回出厂)。
// 纯模型用例(LayoutEngine.FactoryPage / ClearGeometry / LayoutPageState.Reset),不建界面。
using System.Collections.Generic;
using System.Linq;
using TipCL.App.Pages.Layout;
using TipCL.Core.Launcher.Layout;
using Xunit;

public class LayoutResetFactoryTests
{
	private static List<LayoutBlockRef> Refs(params (string Id, bool Visible)[] ids)
	{
		return ids.Select(((string Id, bool Visible) r) => new LayoutBlockRef(r.Id, r.Visible)).ToList();
	}

	private static BlockLayout Block(string id, double x, double y, double w, double h)
	{
		return new BlockLayout { Id = id, X = x, Y = y, W = w, H = h };
	}

	// 用户在网格页上拖过、拉过像素高、改过外观,还加了一个自建块和一个插件块
	private static PageLayoutV2 Edited()
	{
		PageLayoutV2 page = new PageLayoutV2 { Mode = LayoutSchema.ModeGrid, Cols = 12, Gap = 4.0 };
		BlockLayout a = Block("a", 6, 3, 6, 2);
		a.Style = LayoutSchema.StyleCard;
		LayoutCell.WriteHeightPx(a, 240.0);
		page.Blocks.Add(a);
		page.Blocks.Add(Block("b", 0, 0, 3, 1));
		BlockLayout custom = Block("custom:note-1", 3, 5, 5, 2);
		custom.Settings["text"] = "便签内容";
		custom.Style = LayoutSchema.StyleCard;
		custom.Title = true;
		custom.Hidden = true;
		LayoutCell.WriteHeightPx(custom, 300.0);
		page.Blocks.Add(custom);
		BlockLayout plugin = Block("plugin:demo:clock", 9, 9, 3, 1);
		plugin.Settings["tz"] = "UTC";
		page.Blocks.Add(plugin);
		return page;
	}

	[Fact]
	public void FactoryPageWithoutDefaultIsWhitelistStackAndKeepsCustomData()
	{
		PageLayoutV2 factory = LayoutEngine.FactoryPage(Edited(), null, Refs(("a", true), ("b", true), ("c", false)));

		// 页级参数 = 白名单默认(单列 stack + 默认间距 / 边距)
		PageLayoutV2 fresh = new PageLayoutV2();
		Assert.Equal(LayoutSchema.ModeStack, factory.Mode);
		Assert.Equal(fresh.Cols, factory.Cols);
		Assert.Equal(fresh.Gap, factory.Gap);
		Assert.Equal(fresh.Pad, factory.Pad);
		// 顺序 = 白名单(含默认隐藏的 c)→ 自建 / 插件块接在最后
		Assert.Equal(new[] { "a", "b", "c", "custom:note-1", "plugin:demo:clock" }, factory.Blocks.Select(b => b.Id).ToArray());
		Assert.True(factory.Blocks.First(b => b.Id == "c").Hidden);

		// 白名单块:旧几何 / 外观 / 像素高一律不沿用
		BlockLayout a = factory.Blocks.First(b => b.Id == "a");
		Assert.Equal(0.0, a.X);
		Assert.Equal(0.0, a.Y);
		Assert.Equal(1.0, a.W);
		Assert.Equal(LayoutSchema.StylePlain, a.Style);
		Assert.Equal(0.0, LayoutCell.ReadHeightPx(a));

		// 自建块:只清几何、恢复显示,数据原样保留
		BlockLayout custom = factory.Blocks.First(b => b.Id == "custom:note-1");
		Assert.False(custom.Hidden);
		Assert.Equal(0.0, custom.X);
		Assert.Equal(1.0, custom.W);
		Assert.Equal(0.0, LayoutCell.ReadHeightPx(custom));
		Assert.Equal("便签内容", custom.Settings["text"]);
		Assert.Equal(LayoutSchema.StyleCard, custom.Style);
		Assert.True(custom.Title);
		Assert.Equal("UTC", factory.Blocks.First(b => b.Id == "plugin:demo:clock").Settings["tz"]);
	}

	[Fact]
	public void FactoryPageWithHostDefaultRestoresDefaultGeometryAndAppendsCustomFullWidth()
	{
		PageLayoutV2 host = new PageLayoutV2 { Mode = LayoutSchema.ModeGrid, Cols = 12, Gap = 18.0 };
		host.Blocks.Add(Block("a", 0, 0, 4, 1));
		host.Blocks.Add(Block("b", 4, 0, 8, 1));

		PageLayoutV2 factory = LayoutEngine.FactoryPage(Edited(), host, Refs(("a", true), ("b", true)));

		Assert.Equal(LayoutSchema.ModeGrid, factory.Mode);
		Assert.Equal(18.0, factory.Gap);
		Assert.Equal(4.0, factory.Blocks.First(b => b.Id == "a").W);
		Assert.Equal(0.0, LayoutCell.ReadHeightPx(factory.Blocks.First(b => b.Id == "a")));
		Assert.Equal(4.0, factory.Blocks.First(b => b.Id == "b").X);

		// 网格出厂页里自建块占满一整行,自动流把它落到内置块下面 —— 不与出厂块重叠
		List<LayoutCell> cells = LayoutGrid.Pack(factory);
		LayoutCell note = cells.First(c => c.Id == "custom:note-1");
		Assert.Equal(12, note.W);
		Assert.True(note.Y >= 1, "自建块应该排在出厂块下方,实际 y = " + note.Y);
		Assert.Equal(0, cells.First(c => c.Id == "a").Y);
		Assert.Equal(4, cells.First(c => c.Id == "b").X);
		// 宿主默认布局对象本身不被改动(每次重置都从同一份出厂页出发)
		Assert.Equal(2, host.Blocks.Count);
	}

	[Fact]
	public void LoadStateOfFactoryPageKeepsImplicitBlocks()
	{
		List<LayoutBlockRef> refs = Refs(("a", true), ("b", true));
		PageLayoutV2 factory = LayoutEngine.FactoryPage(Edited(), null, refs);
		LayoutPageState state = LayoutEngine.LoadState("p", factory, refs, true);

		Assert.Equal(new[] { "a", "b", "custom:note-1", "plugin:demo:clock" }, state.Order.ToArray());
		Assert.Empty(state.Hidden);
		Assert.Equal("便签内容", state.ToPage().Blocks.First(b => b.Id == "custom:note-1").Settings["text"]);
	}

	[Fact]
	public void StateResetClearsGeometryButKeepsCustomBlockData()
	{
		// 与 Windows LayoutEngineTests.ResetClearsGeometryButKeepsCustomBlockData 同一条断言口径
		List<LayoutBlockRef> refs = Refs(("a", true), ("b", true));
		LayoutPageState state = LayoutEngine.LoadState("p", Edited(), refs);

		state.Reset(refs);

		BlockLayout a = state.Page.Blocks.First(b => b.Id == "a");
		Assert.Equal(0.0, a.X);
		Assert.Equal(1.0, a.W);
		Assert.Equal(0.0, LayoutCell.ReadHeightPx(a));
		BlockLayout custom = state.Page.Blocks.First(b => b.Id == "custom:note-1");
		Assert.Equal(0.0, custom.X);
		Assert.Equal(0.0, custom.Y);
		Assert.Equal(1.0, custom.W);
		Assert.Equal(1.0, custom.H);
		Assert.Equal(0.0, LayoutCell.ReadHeightPx(custom));
		Assert.False(custom.Hidden);
		Assert.Equal("便签内容", custom.Settings["text"]);
		Assert.Equal(LayoutSchema.StyleCard, custom.Style);
	}
}
