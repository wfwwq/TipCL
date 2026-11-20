using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TipCL.Pages;
using TipCL.Pages.Layout;
using Xunit;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Tests;

/// <summary>
/// R34 WP-L5(§5.5 / D-81 Q4、Q5):联机 / 游戏时长 / Java / 启动下方内容区的新默认布局,以及单块页不给右键菜单。
/// 页面类本身是 WPF 控件(net8 测试宿主加载不了),所以分三层验:
///   1) 默认布局本身 —— <see cref="PageDefaultLayouts"/> 是纯数据,直接调用,用 <see cref="LayoutGrid.Pack"/> 排出格子核对位置;
///   2) 「文档里没有该页 → 用新默认 / 有该页 → 完全照文档」—— 照 LayoutEditor.LoadFromStore 的取页规则走一遍真实的
///      LayoutStoreV2 读写(临时目录),并按源码断言 LoadFromStore 里的那条规则还在(规则变了这里会红);
///   3) 接线事实 —— 按源码断言 4 个页面确实把默认布局交给了 BlockHost、块 id / 默认显隐与默认布局一致,
///      单块页(settings:network / settings:about)的宿主确实只有一块、BlockHost 只在 ≥2 块时挂「自定义布局...」。
/// 真机上的观感(半宽卡片挤不挤、启动页网格高度)不在这里,见 progress/WP-L5.md 的「待真机」。
/// </summary>
public class PageDefaultsV3Tests
{
	private readonly string _tmp = Path.Combine(Path.GetTempPath(), "tipcl_l5_defaults_" + Guid.NewGuid().ToString("N"));

	private void UseDir()
	{
		LayoutStoreV2.DirOverride = _tmp;
		LayoutStoreV2.V1Bridge = new PagesLayoutV1Bridge();
		PageLayoutStore.DirOverride = _tmp;
		LayoutStoreV2.ResetCache();
		PageLayoutStore.Reload();
	}

	private void Release()
	{
		LayoutStoreV2.DirOverride = null;
		LayoutStoreV2.V1Bridge = null;
		PageLayoutStore.DirOverride = null;
		LayoutStoreV2.ResetCache();
		PageLayoutStore.Reload();
		try
		{
			Directory.Delete(_tmp, true);
		}
		catch (Exception)
		{
		}
	}

	// ————— 源码读取 —————

	private static string Src(string rel)
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TipCL.sln")))
		{
			dir = dir.Parent;
		}
		Assert.NotNull(dir);
		return File.ReadAllText(Path.Combine(dir!.FullName, rel));
	}

	private sealed class SpecLine
	{
		public string Id = "";

		public string Title = "";

		public bool DefaultVisible = true;
	}

	/// <summary>
	/// 抓 <c>new BlockHost("pageId", new List&lt;BlockSpec&gt; { ... }</c> 里逐行写的 BlockSpec(这几页都是一行一个),
	/// 以及列表右花括号之后到分号为止的尾巴(contentMargin / defaultPage 实参就在这里)。
	/// </summary>
	private static List<SpecLine> HostSpecs(string src, string pageId, out string tail)
	{
		int at = src.IndexOf("new BlockHost(\"" + pageId + "\"", StringComparison.Ordinal);
		Assert.True(at >= 0, "找不到宿主:" + pageId);
		Match end = new Regex("\\r?\\n\\s*\\}(?=[,)])").Match(src, at);
		Assert.True(end.Success, "宿主的块列表没有收尾:" + pageId);
		string body = src.Substring(at, end.Index - at);
		int semi = src.IndexOf(';', end.Index);
		Assert.True(semi > end.Index);
		tail = src.Substring(end.Index, semi - end.Index);
		List<SpecLine> specs = new List<SpecLine>();
		foreach (Match m in Regex.Matches(body, "new BlockSpec \\{ Id = \"([^\"]+)\", Title = I18n\\.L\\(\"([^\"]+)\"\\)([^\\r\\n]*)"))
		{
			specs.Add(new SpecLine
			{
				Id = m.Groups[1].Value,
				Title = m.Groups[2].Value,
				DefaultVisible = !m.Groups[3].Value.Contains("DefaultVisible = false")
			});
		}
		// 不允许漏抓:列表里 BlockSpec 的个数必须和抓到的行数相同
		Assert.Equal(Regex.Matches(body, "new BlockSpec\\b").Count, specs.Count);
		return specs;
	}

	public static IEnumerable<object[]> DefaultPages()
	{
		yield return new object[] { "multiplayer", "src/TipCL/Pages/MultiplayerPage.cs", "Multiplayer" };
		yield return new object[] { "playtime", "src/TipCL/Pages/PlaytimePage.cs", "Playtime" };
		yield return new object[] { "java", "src/TipCL/Pages/JavaPage.cs", "Java" };
		yield return new object[] { "launch", "src/TipCL/Pages/LaunchPage.cs", "LaunchContent" };
	}

	private static PageLayoutV2 Factory(string name)
	{
		return name switch
		{
			"Multiplayer" => PageDefaultLayouts.Multiplayer(),
			"Playtime" => PageDefaultLayouts.Playtime(),
			"Java" => PageDefaultLayouts.Java(),
			"LaunchContent" => PageDefaultLayouts.LaunchContent(),
			_ => throw new ArgumentException(name)
		};
	}

	private static List<LayoutBlockRef> RefsOf(IEnumerable<SpecLine> specs)
	{
		return specs.Select((SpecLine s) => new LayoutBlockRef(s.Id, s.DefaultVisible)).ToList();
	}

	// 与 LayoutEditor.LoadFromStore 同一条取页规则(宽度未知 = 主 body;文档里没有这一页才用页面默认布局)。
	// 规则本身由 EditorStillPicksDefaultOnlyWhenDocHasNoPage 按源码钉住。
	private static LayoutPageState HostLoad(LayoutV2 doc, string pageId, PageLayoutV2? defaultPage, IReadOnlyList<LayoutBlockRef> refs)
	{
		PageLayoutV2? page = LayoutVariants.PageOf(doc.Layout, pageId, 0.0, LayoutVariants.NarrowWidthThreshold);
		if (page == null && defaultPage != null)
		{
			page = defaultPage.Clone();
		}
		return LayoutEngine.LoadState(pageId, page, refs);
	}

	private static string Cells(PageLayoutV2 page)
	{
		return string.Join(" ", LayoutGrid.Pack(page).Select((LayoutCell c) => $"{c.Id}@{c.X},{c.Y},{c.W}"));
	}

	// ————— 1) 默认布局本身 —————

	[Theory]
	[MemberData(nameof(DefaultPages))]
	public void DefaultCoversExactlyThePageBlocksAndIsWiredIntoTheHost(string pageId, string file, string factory)
	{
		List<SpecLine> specs = HostSpecs(Src(file), pageId, out string tail);
		PageLayoutV2 page = Factory(factory);

		Assert.Equal(LayoutSchema.ModeGrid, page.Mode);
		Assert.Equal(PageDefaultLayouts.Cols, page.Cols);
		Assert.Equal(12, page.Cols); // 各页现有列制(12)不变,效果图的 6 列只是示意
		// 块集合 = 页面白名单,不多不少不重复
		string[] ids = page.Blocks.Select((BlockLayout b) => b.Id).ToArray();
		Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
		Assert.Equal(specs.Select((SpecLine s) => s.Id).OrderBy((string s) => s, StringComparer.Ordinal), ids.OrderBy((string s) => s, StringComparer.Ordinal));
		// 默认隐藏 = BlockSpec.DefaultVisible == false(两处说法一致,重置 / 补齐缺块时才不会打架)
		foreach (SpecLine spec in specs)
		{
			Assert.Equal(!spec.DefaultVisible, page.Blocks.Single((BlockLayout b) => b.Id == spec.Id).Hidden);
		}
		// 每块的列跨度合法
		Assert.All(page.Blocks, (BlockLayout b) => Assert.InRange(b.Span, 1, page.Cols));
		// 页面确实把这份默认交给了宿主
		Assert.Contains("PageDefaultLayouts." + factory + "()", tail);
		// 按 id 取到的是同一份
		PageLayoutV2? byId = PageDefaultLayouts.ForPage(pageId);
		Assert.NotNull(byId);
		Assert.Equal(ids, byId!.Blocks.Select((BlockLayout b) => b.Id).ToArray());
	}

	[Fact]
	public void MultiplayerDefaultIsHeaderThenStateRoomHalvesThenActionsThenPlayersAndNodes()
	{
		Assert.Equal("header@0,0,12 state@0,1,6 room@6,1,6 actions@0,2,12 players@0,3,8 nodes@8,3,4", Cells(PageDefaultLayouts.Multiplayer()));
	}

	[Fact]
	public void PlaytimeDefaultHidesOverviewAndPutsTotalAndRangeSideBySide()
	{
		PageLayoutV2 page = PageDefaultLayouts.Playtime();
		Assert.Equal("total@0,0,6 range@6,0,6 list@0,1,12", Cells(page));
		Assert.True(page.Blocks.Single((BlockLayout b) => b.Id == "header").Hidden);
	}

	[Fact]
	public void JavaDefaultHidesIntroAndPutsInstallAndDefaultsSideBySide()
	{
		PageLayoutV2 page = PageDefaultLayouts.Java();
		Assert.Equal("install@0,0,6 defaults@6,0,6 list@0,1,12", Cells(page));
		Assert.True(page.Blocks.Single((BlockLayout b) => b.Id == "intro").Hidden);
	}

	[Fact]
	public void LaunchContentDefaultShowsOnlyNewsFullWidth()
	{
		PageLayoutV2 page = PageDefaultLayouts.LaunchContent();
		Assert.Equal("news@0,0,12", Cells(page));
		Assert.Equal(new[] { "tasks", "playtime", "notes", "log" }, page.Blocks.Where((BlockLayout b) => b.Hidden).Select((BlockLayout b) => b.Id).ToArray());
	}

	// 默认隐藏的块显示出来之后:接在已有块后面,两两并排,日志占整行(不会挤进第 0 列、不会压住新闻)
	[Fact]
	public void LaunchHiddenBlocksPairUpWhenShownAndLogTakesTheWholeRow()
	{
		List<LayoutBlockRef> refs = RefsOf(HostSpecs(Src("src/TipCL/Pages/LaunchPage.cs"), "launch", out string _));
		LayoutPageState state = LayoutEngine.LoadState("launch", PageDefaultLayouts.LaunchContent(), refs);
		Assert.Equal(new[] { "news" }, state.VisibleIds().ToArray());

		state.Restore("tasks");
		state.Restore("playtime");
		Assert.Equal("news@0,0,12 tasks@0,1,6 playtime@6,1,6", Cells(state.ToPage()));

		state.Restore("notes");
		state.Restore("log");
		Assert.Equal("news@0,0,12 tasks@0,1,6 playtime@6,1,6 notes@0,2,6 log@0,3,12", Cells(state.ToPage()));

		// 只显示日志:直接接在新闻下面占整行
		LayoutPageState only = LayoutEngine.LoadState("launch", PageDefaultLayouts.LaunchContent(), refs);
		only.Restore("log");
		Assert.Equal("news@0,0,12 log@0,1,12", Cells(only.ToPage()));
	}

	[Fact]
	public void HiddenOverviewAndIntroComeBackAtTheEndFullWidth()
	{
		LayoutPageState playtime = LayoutEngine.LoadState("playtime", PageDefaultLayouts.Playtime(), RefsOf(HostSpecs(Src("src/TipCL/Pages/PlaytimePage.cs"), "playtime", out string _)));
		playtime.Restore("header");
		Assert.Equal("total@0,0,6 range@6,0,6 list@0,1,12 header@0,2,12", Cells(playtime.ToPage()));

		LayoutPageState java = LayoutEngine.LoadState("java", PageDefaultLayouts.Java(), RefsOf(HostSpecs(Src("src/TipCL/Pages/JavaPage.cs"), "java", out string _)));
		java.Restore("intro");
		Assert.Equal("install@0,0,6 defaults@6,0,6 list@0,1,12 intro@0,2,12", Cells(java.ToPage()));
	}

	[Fact]
	public void EveryCallReturnsAFreshCopy()
	{
		PageLayoutV2 a = PageDefaultLayouts.Multiplayer();
		a.Blocks[0].Hidden = true;
		a.Blocks.RemoveAt(1);
		a.Cols = 6;
		PageLayoutV2 b = PageDefaultLayouts.Multiplayer();
		Assert.NotSame(a, b);
		Assert.Equal(12, b.Cols);
		Assert.Equal(6, b.Blocks.Count);
		Assert.False(b.Blocks[0].Hidden);
	}

	[Theory]
	[InlineData("download")]
	[InlineData("library:main")]
	[InlineData("settings:network")]
	[InlineData("settings:about")]
	[InlineData("servers")]
	[InlineData("account")]
	[InlineData("")]
	[InlineData(null)]
	public void OtherPagesHaveNoV3Default(string? pageId)
	{
		Assert.Null(PageDefaultLayouts.ForPage(pageId));
	}

	// ————— 2) 文档里没有 / 有该页 —————

	[Fact]
	public void EditorStillPicksDefaultOnlyWhenDocHasNoPage()
	{
		// HostLoad 复刻的是这两行;LayoutEditor 里这条规则变了,这里的「两种情况」就不再代表真实行为,必须红
		string editor = Src("src/TipCL/Pages/Layout/LayoutEditor.cs");
		int at = editor.IndexOf("private void LoadFromStore()", StringComparison.Ordinal);
		Assert.True(at > 0);
		string body = editor.Substring(at, Math.Min(2400, editor.Length - at));
		Assert.Contains("LayoutVariants.PageOf(doc.Layout, _pageId", body);
		Assert.Contains("if (page == null && _defaultPage != null)", body);
		Assert.Contains("page = _defaultPage.Clone();", body);
		Assert.Contains("LayoutEngine.LoadState(_pageId, page, _refs)", body);
		// BlockHost 把 defaultPage 原样交给编辑器
		Assert.Contains("new LayoutEditor(pageId, specs, contentMargin, defaultPage, native)", Src("src/TipCL/Pages/BlockHost.cs"));
	}

	[Theory]
	[MemberData(nameof(DefaultPages))]
	public void WithoutPageEntryTheHostRendersTheNewDefault(string pageId, string file, string factory)
	{
		UseDir();
		try
		{
			// 文档里有别的页、唯独没有这一页(真实用户最常见的形态:自定义过别的页)
			LayoutV2 seed = LayoutStoreV2.ParseImport("""
			{
			  "version": 2,
			  "kind": "tipcl-layout",
			  "layout": { "pages": { "tasks": { "version": 2, "blocks": [{ "id": "list" }, { "id": "header" }] } } }
			}
			""");
			LayoutStoreV2.Save(seed);
			List<LayoutBlockRef> refs = RefsOf(HostSpecs(Src(file), pageId, out string _));
			LayoutV2 doc = LayoutStoreV2.Load();
			Assert.False(doc.Layout.Pages.ContainsKey(pageId));

			LayoutPageState state = HostLoad(doc, pageId, Factory(factory), refs);
			PageLayoutV2 expected = Factory(factory);
			Assert.Equal(LayoutSchema.ModeGrid, state.Page.Mode);
			Assert.Equal(12, state.Page.Cols);
			Assert.Equal(expected.Blocks.Select((BlockLayout b) => b.Id).ToArray(), state.Order.ToArray());
			Assert.Equal(expected.Blocks.Where((BlockLayout b) => b.Hidden).Select((BlockLayout b) => b.Id).OrderBy((string s) => s, StringComparer.Ordinal), state.Hidden.OrderBy((string s) => s, StringComparer.Ordinal));
			Assert.Equal(Cells(expected), Cells(state.ToPage()));
			// 不是白名单默认(纵向流)
			Assert.NotEqual(LayoutEngine.LoadState(pageId, null, refs).Page.Mode, state.Page.Mode);
		}
		finally
		{
			Release();
		}
	}

	[Theory]
	[MemberData(nameof(DefaultPages))]
	public void WithPageEntryTheHostFollowsTheDocumentExactly(string pageId, string file, string factory)
	{
		UseDir();
		try
		{
			List<SpecLine> specs = HostSpecs(Src(file), pageId, out string _);
			List<LayoutBlockRef> refs = RefsOf(specs);
			// 用户存过的样子:纵向列表、顺序倒过来、第一块(默认可见)藏起来、默认隐藏的块全部显示
			List<string> reversed = specs.Select((SpecLine s) => s.Id).Reverse().ToList();
			string hiddenId = specs.First((SpecLine s) => s.DefaultVisible).Id;
			string blocks = string.Join(",", reversed.Select((string id) => "{ \"id\": \"" + id + "\"" + ((id == hiddenId) ? ", \"hidden\": true" : "") + " }"));
			LayoutStoreV2.Save(LayoutStoreV2.ParseImport("{ \"version\": 2, \"kind\": \"tipcl-layout\", \"layout\": { \"pages\": { \"" + pageId + "\": { \"version\": 2, \"mode\": \"stack\", \"blocks\": [" + blocks + "] } } } }"));

			LayoutV2 doc = LayoutStoreV2.Load();
			Assert.True(doc.Layout.Pages.ContainsKey(pageId));
			LayoutPageState state = HostLoad(doc, pageId, Factory(factory), refs);
			Assert.Equal(LayoutSchema.ModeStack, state.Page.Mode);
			Assert.Equal(reversed, state.Order);
			Assert.Equal(new[] { hiddenId }, state.Hidden.ToArray());
			LayoutPlan plan = state.Plan();
			Assert.True(plan.IsSingleColumn, pageId);
			Assert.Equal(reversed.Count - 1, plan.Rows.Count);

			// 存过的网格也照文档:6 列、每块整行,默认布局的 12 列 / 半宽一个都不出现
			string gridBlocks = string.Join(",", specs.Select((SpecLine s, int i) => "{ \"id\": \"" + s.Id + "\", \"x\": 0, \"y\": " + i + ", \"w\": 6, \"h\": 1, \"span\": 6 }"));
			LayoutStoreV2.Save(LayoutStoreV2.ParseImport("{ \"version\": 2, \"kind\": \"tipcl-layout\", \"layout\": { \"pages\": { \"" + pageId + "\": { \"version\": 2, \"mode\": \"grid\", \"cols\": 6, \"blocks\": [" + gridBlocks + "] } } } }"));
			LayoutPageState grid = HostLoad(LayoutStoreV2.Load(), pageId, Factory(factory), refs);
			Assert.Equal(6, grid.Page.Cols);
			Assert.Empty(grid.Hidden); // 文档里没写 hidden = 全部显示,哪怕默认布局里它们是隐藏的
			Assert.Equal(string.Join(" ", specs.Select((SpecLine s, int i) => s.Id + "@0," + i + ",6")), Cells(grid.ToPage()));
		}
		finally
		{
			Release();
		}
	}

	[Theory]
	[MemberData(nameof(DefaultPages))]
	public void DefaultSurvivesAStoreRoundTrip(string pageId, string file, string factory)
	{
		UseDir();
		try
		{
			List<LayoutBlockRef> refs = RefsOf(HostSpecs(Src(file), pageId, out string _));
			LayoutV2 doc = LayoutStoreV2.Load();
			LayoutPageState state = HostLoad(doc, pageId, Factory(factory), refs);
			// 编辑态什么都没动就点完成 = 这一份写进文档
			LayoutEngine.ApplyPage(doc, pageId, state.ToPage());
			Assert.True(LayoutStoreV2.Save(doc));
			LayoutStoreV2.ResetCache();
			LayoutV2 back = LayoutStoreV2.Load();
			PageLayoutV2 stored = back.Layout.Pages[pageId];
			PageLayoutV2 expected = Factory(factory);
			Assert.Equal(LayoutSchema.ModeGrid, stored.Mode);
			Assert.Equal(12, stored.Cols);
			Assert.Equal(expected.Blocks.Select((BlockLayout b) => (b.Id, b.Hidden, b.Span)).ToArray(), stored.Blocks.Select((BlockLayout b) => (b.Id, b.Hidden, b.Span)).ToArray());
			Assert.Equal(Cells(expected), Cells(stored));
			// 读回来再装载 = 照文档,和第一次装载看到的一样
			Assert.Equal(Cells(state.ToPage()), Cells(HostLoad(back, pageId, Factory(factory), refs).ToPage()));
		}
		finally
		{
			Release();
		}
	}

	// ————— 3) 单块页没有「自定义布局...」(D-81 Q5) —————

	[Theory]
	[InlineData(0, false)]
	[InlineData(1, false)]
	[InlineData(2, true)]
	[InlineData(5, true)]
	public void LayoutMenuNeedsAtLeastTwoBlocks(int count, bool offered)
	{
		Assert.Equal(offered, BlockHostRules.OffersLayoutMenu(count));
		Assert.Equal(2, BlockHostRules.MinBlocksForLayoutMenu);
	}

	[Theory]
	[InlineData("settings:network")]
	[InlineData("settings:about")]
	public void SingleBlockSettingsPagesGetNoLayoutMenu(string pageId)
	{
		List<SpecLine> specs = HostSpecs(Src("src/TipCL/Pages/SettingsPage.cs"), pageId, out string _);
		Assert.Single(specs);
		Assert.False(BlockHostRules.OffersLayoutMenu(specs.Count));
		// 设置 → 布局的入口表里它们也标着 SingleBlock(L4 据此不列卡片)
		Assert.Contains(pageId, LayoutBlockEntryCatalog.SingleBlockPageIds());
	}

	[Fact]
	public void BlockHostHangsTheMenuOnlyBehindTheTwoBlockRule()
	{
		string src = Src("src/TipCL/Pages/BlockHost.cs");
		int ctor = src.IndexOf("public BlockHost(string pageId, IReadOnlyList<BlockSpec> specs, Thickness? contentMargin, PageLayoutV2? defaultPage, BlockNativeLayout? native)", StringComparison.Ordinal);
		Assert.True(ctor > 0);
		int gate = src.IndexOf("if (BlockHostRules.OffersLayoutMenu(_specCount))", ctor, StringComparison.Ordinal);
		int menu = src.IndexOf("I18n.L(\"自定义布局...\")", ctor, StringComparison.Ordinal);
		int other = src.IndexOf("else if (_specCount > 0)", ctor, StringComparison.Ordinal);
		Assert.True(gate > ctor, "BlockHost 构造里没有 ≥2 块的门槛");
		Assert.True(menu > gate && menu < other, "「自定义布局...」必须挂在门槛分支里");
		// 单块分支:占位菜单 + 禁用,让全局右键让路且自己不弹
		string single = src.Substring(other, Math.Min(900, src.Length - other));
		Assert.Contains("base.ContextMenu = new ContextMenu();", single);
		Assert.Contains("ContextMenuService.SetIsEnabled(this, false);", single);
		// 单块分支里不建任何菜单项
		string singleBody = single.Substring(0, single.IndexOf("ContextMenuService.SetIsEnabled", StringComparison.Ordinal));
		Assert.DoesNotContain("new MenuItem", singleBody);
		Assert.DoesNotContain("I18n.L(", singleBody);
		// 旧的 ≥1 放宽写法不能回来
		Assert.DoesNotContain("specs.Count >= 1", src);
	}

	[Theory]
	[MemberData(nameof(DefaultPages))]
	public void PagesWithV3DefaultsStillOfferTheMenu(string pageId, string file, string factory)
	{
		Assert.NotNull(factory);
		Assert.True(BlockHostRules.OffersLayoutMenu(HostSpecs(Src(file), pageId, out string _).Count), pageId);
	}

	// ————— LayoutBlockHostTests.HostTable 是手抄的白名单,改过的这几页对一遍源码,防止表和代码分叉 —————

	[Theory]
	[InlineData("launch", "src/TipCL/Pages/LaunchPage.cs")]
	[InlineData("multiplayer", "src/TipCL/Pages/MultiplayerPage.cs")]
	[InlineData("playtime", "src/TipCL/Pages/PlaytimePage.cs")]
	[InlineData("java", "src/TipCL/Pages/JavaPage.cs")]
	[InlineData("servers", "src/TipCL/Pages/ServersPage.cs")]
	[InlineData("account", "src/TipCL/Pages/AccountPage.cs")]
	[InlineData("settings:network", "src/TipCL/Pages/SettingsPage.cs")]
	[InlineData("settings:about", "src/TipCL/Pages/SettingsPage.cs")]
	public void HostTableRowMatchesSource(string pageId, string file)
	{
		object[] row = LayoutBlockHostTests.HostTable().Single((object[] r) => (string)r[0] == pageId);
		List<SpecLine> specs = HostSpecs(Src(file), pageId, out string _);
		Assert.Equal((string)row[1], string.Join(",", specs.Select((SpecLine s) => s.Id)));
		Assert.Equal((string)row[2], string.Join(",", specs.Where((SpecLine s) => s.DefaultVisible).Select((SpecLine s) => s.Id)));
	}
}
