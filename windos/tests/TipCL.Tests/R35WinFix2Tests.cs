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
/// R35 win-fix2:用户真机录屏里的布局 v3 问题(2026-11-21)。界面是 WPF,net8 测试宿主实例化不了控件,
/// 所以「纯规则」直接断言,「接线」按源码文本断言(同 LayoutV3SourceTests / R35WinIntTests 的做法)。
/// </summary>
public class R35WinFix2Tests
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

	private static string Between(string src, string startMarker, string endMarker)
	{
		int a = src.IndexOf(startMarker, StringComparison.Ordinal);
		Assert.True(a >= 0, "找不到:" + startMarker);
		int b = src.IndexOf(endMarker, a + startMarker.Length, StringComparison.Ordinal);
		Assert.True(b > a, "找不到:" + endMarker);
		return src.Substring(a, b - a);
	}

	// ————— 1. 拖动方向 —————

	[Fact]
	public void PointerUpMovesFloatingCardUp()
	{
		// 起手 y = 300,指针上移到 250 → 卡片位移 -50(往上);下移到 340 → +40(往下)
		Assert.Equal(-50.0, StackDragPlan.FloatOffset(0.0, 300.0, 250.0));
		Assert.Equal(40.0, StackDragPlan.FloatOffset(0.0, 300.0, 340.0));
		// 换位补偿的基准位移照常叠加
		Assert.Equal(-50.0 + 110.0, StackDragPlan.FloatOffset(110.0, 300.0, 250.0));
		Assert.Equal(12.0, StackDragPlan.FloatOffset(12.0, double.NaN, 5.0));
	}

	[Fact]
	public void PointerUpGivesSmallerDropIndex()
	{
		// 三块各高 100、间距 10:拖最下面那块(下标 2),指针一路往上 → 落点下标只减不增,最后到 0
		List<string> visible = new List<string> { "filters", "list", "extra" };
		Dictionary<string, double> heights = new Dictionary<string, double> { ["filters"] = 100.0, ["list"] = 100.0, ["extra"] = 100.0 };
		List<double> pointer = new List<double> { 260.0, 200.0, 150.0, 90.0, 40.0, 5.0 };
		List<int> series = StackDragPlan.IndexSeries(visible, heights, 10.0, pointer, 2);
		for (int i = 1; i < series.Count; i++)
		{
			Assert.True(series[i] <= series[i - 1], "指针上移时落点下标变大了:" + string.Join(",", series));
		}
		Assert.Equal(0, series[series.Count - 1]);
		Assert.True(StackDragPlan.TargetIndex(visible, heights, 10.0, 40.0, 2) < 2);
	}

	[Fact]
	public void StackDragMoveUpdatesLivePointerNotAnchor()
	{
		string src = Src("src/TipCL/Pages/Layout/LayoutEditor.cs");
		string move = Between(src, "private void OnDragMove(", "// ————————————— stack 浮起跟手");
		Assert.Contains("_stackPointerYLive = y;", move);
		Assert.DoesNotContain("_stackPointerY = y;", move);
		// 位移只由 StackDragPlan.FloatOffset(基准, 锚点, 实时) 决定
		string float_ = Between(src, "private void MoveStackFloat()", "private void ApplyStackReorder(");
		Assert.Contains("StackDragPlan.FloatOffset(_stackFloatBase, _stackPointerY, _stackPointerYLive)", float_);
	}

	// ————— 2. 编辑态按住块的任意位置都能拖 —————

	[Fact]
	public void EditModeCardHasTransparentHitShieldBelowHandles()
	{
		string le = Src("src/TipCL/Pages/Layout/LayoutEditor.cs");
		string ctor = Between(le, "public LayoutCard(LayoutEditor host, BlockSpec spec, UIElement? content)", "public void ReleaseContent()");
		int shield = ctor.IndexOf("grid.Add(_editShield", StringComparison.Ordinal);
		int handles = ctor.IndexOf("grid.Add(_handleLayer", StringComparison.Ordinal);
		int chrome = ctor.IndexOf("BuildV3Chrome(grid);", StringComparison.Ordinal);
		int body = ctor.IndexOf("grid.Add(_bodyWrap, 2);", StringComparison.Ordinal);
		Assert.True(body >= 0 && shield > body, "命中层要盖在内容体上面");
		Assert.True(handles > shield && chrome > shield, "手柄层 / 眼睛 / 垃圾桶 / 边缘把手要在命中层之上");
		string setEdit = Between(le, "public void SetEdit(bool edit)", "UpdateHandles();");
		Assert.Contains("_bodyWrap.IsHitTestVisible = !edit;", setEdit);
		Assert.Contains("_editShield.Visibility = (edit ? Visibility.Visible : Visibility.Collapsed);", setEdit);
		// 透明底才参与命中测试(null 底不参与)
		string v3 = Src("src/TipCL/Pages/Layout/LayoutEditorV3.cs");
		string decl = Between(v3, "internal readonly Border _editShield", "};");
		Assert.Contains("Background = Brushes.Transparent", decl);
	}

	// ————— 3. 和效果图对齐 —————

	private static BlockLayout Legacy(string id, bool hidden = false)
	{
		return new BlockLayout { Id = id, Hidden = hidden, Span = 1 };
	}

	private static PageLayoutV2 UserLaunchDoc()
	{
		// 用户真实存档的形状:mode = stack、每块 span = 1、全部可见,外加一个自建「游戏时长」组件
		return new PageLayoutV2
		{
			Mode = LayoutSchema.ModeStack,
			Blocks = new List<BlockLayout> { Legacy("news"), Legacy("tasks"), Legacy("playtime"), Legacy("notes"), Legacy("log"), Legacy("custom:w-1") }
		};
	}

	private static BlockLayout Find(PageLayoutV2 page, string id)
	{
		BlockLayout? b = page.Blocks.FirstOrDefault((BlockLayout x) => x.Id == id);
		Assert.NotNull(b);
		return b!;
	}

	[Fact]
	public void LaunchLegacyStackRendersAsPairedGrid()
	{
		PageLayoutV2 doc = UserLaunchDoc();
		PageLayoutV2? grid = LayoutV3Compat.LaunchLegacyStackAsGrid("launch", doc, PageDefaultLayouts.LaunchContent());
		Assert.NotNull(grid);
		Assert.Equal(LayoutSchema.ModeGrid, grid!.Mode);
		Assert.Equal(12, grid.Cols);
		// 新闻整行;下载任务 + 游戏时长并排;便签 + 自建组件并排;日志整行
		Assert.Equal((0.0, 0.0, 12), (Find(grid, "news").X, Find(grid, "news").Y, Find(grid, "news").Span));
		Assert.Equal((0.0, 1.0, 6), (Find(grid, "tasks").X, Find(grid, "tasks").Y, Find(grid, "tasks").Span));
		Assert.Equal((6.0, 1.0, 6), (Find(grid, "playtime").X, Find(grid, "playtime").Y, Find(grid, "playtime").Span));
		Assert.Equal((0.0, 2.0, 6), (Find(grid, "notes").X, Find(grid, "notes").Y, Find(grid, "notes").Span));
		Assert.Equal((6.0, 2.0, 6), (Find(grid, "custom:w-1").X, Find(grid, "custom:w-1").Y, Find(grid, "custom:w-1").Span));
		Assert.Equal((0.0, 3.0, 12), (Find(grid, "log").X, Find(grid, "log").Y, Find(grid, "log").Span));
		// 用户已经显示的块继续显示;存档本身不动(读时呈现)
		Assert.All(grid.Blocks, (BlockLayout b) => Assert.False(b.Hidden));
		Assert.Equal(LayoutSchema.ModeStack, doc.Mode);
		Assert.All(doc.Blocks, (BlockLayout b) => Assert.Equal(1, b.Span));
	}

	[Fact]
	public void LaunchLegacyStackKeepsHiddenBlocksHidden()
	{
		PageLayoutV2 doc = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeStack,
			Blocks = new List<BlockLayout> { Legacy("news"), Legacy("tasks", hidden: true), Legacy("log") }
		};
		PageLayoutV2 grid = LayoutV3Compat.LaunchLegacyStackAsGrid("launch", doc, PageDefaultLayouts.LaunchContent())!;
		Assert.True(Find(grid, "tasks").Hidden);
		Assert.Equal(6, Find(grid, "tasks").Span);
		Assert.Equal((0.0, 1.0, 12), (Find(grid, "log").X, Find(grid, "log").Y, Find(grid, "log").Span));
	}

	[Fact]
	public void LegacyStackConversionIsLaunchOnlyAndSkipsV3Docs()
	{
		PageLayoutV2 defaults = PageDefaultLayouts.LaunchContent();
		PageLayoutV2 doc = UserLaunchDoc();
		Assert.Same(doc, LayoutV3Compat.LaunchLegacyStackAsGrid("download", doc, defaults));
		Assert.Same(doc, LayoutV3Compat.LaunchLegacyStackAsGrid("launch", doc, null));
		// v3 编辑器里切到「列表」保存过的页带着网格留下的 span:按用户的选择保持列表
		doc.Blocks[0].Span = 12;
		Assert.False(LayoutV3Compat.IsLegacyStack(doc));
		Assert.Same(doc, LayoutV3Compat.LaunchLegacyStackAsGrid("launch", doc, defaults));
		Assert.True(LayoutV3Compat.IsLegacyStack(UserLaunchDoc()));
	}

	[Fact]
	public void LaunchDefaultsStillHideSmallCards()
	{
		// 默认值:新闻整行可见,其余四块默认隐藏(效果图 pages3.html hid:1)
		PageLayoutV2 def = PageDefaultLayouts.LaunchContent();
		Assert.False(Find(def, "news").Hidden);
		foreach (string id in new string[4] { "tasks", "playtime", "notes", "log" })
		{
			Assert.True(Find(def, id).Hidden, id);
		}
		// R35 启动页 v3:启动页不再有 BlockHost,这几块映射成右侧组件区的组件(R35LaunchV3Tests);源码断言随旧界面删除
	}

	[Fact]
	public void RestoredBlockWithoutSpanTakesDefaultSpan()
	{
		// 实例库:旧版本从「添加」显示「实例状态」时存成契约默认矩形 + span 1,渲染成窄条挤在左栏旁
		PageLayoutV2 page = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeGrid,
			Cols = 12,
			Blocks = new List<BlockLayout>
			{
				new BlockLayout { Id = "folders", X = 0, Y = 0, W = 4, H = 1, Span = 4 },
				new BlockLayout { Id = "versions", X = 4, Y = 0, W = 8, H = 2, Span = 8 },
				new BlockLayout { Id = "status", Span = 1 }
			}
		};
		PageLayoutV2 defaults = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeGrid,
			Cols = 12,
			Blocks = new List<BlockLayout>
			{
				new BlockLayout { Id = "folders", X = 0, Y = 0, W = 4, H = 1, Span = 4 },
				new BlockLayout { Id = "versions", X = 4, Y = 0, W = 8, H = 1, Span = 8 },
				new BlockLayout { Id = "status", Hidden = true, Span = 4 }
			}
		};
		PageLayoutV2 fixedPage = LayoutV3Compat.FillMissingSpans(page, defaults)!;
		Assert.Equal(4, Find(fixedPage, "status").Span);
		Assert.Equal(8, Find(fixedPage, "versions").Span);
		Assert.Equal(1, Find(page, "status").Span); // 读时呈现,入参不动
		// 默认里没登记的块、stack 页:原样
		Assert.Same(page, LayoutV3Compat.FillMissingSpans(page, new PageLayoutV2 { Mode = LayoutSchema.ModeGrid }));
		// 实例库的默认布局登记了「实例状态」的列宽
		string lib = Src("src/TipCL/Pages/LibraryPage.cs");
		string def = Between(lib, "private static PageLayoutV2 DefaultLibraryPage()", "private bool FoldersVisible()");
		Assert.Contains("Id = \"status\"", def);
		Assert.Contains("Span = 4", def);
	}

	[Fact]
	public void DownloadEditBarSitsAtContentTop()
	{
		string dl = Src("src/TipCL/Pages/DownloadPage.cs");
		Assert.Contains("_vanillaHost.Editor.EditingChanged +=", dl);
		Assert.Contains("_vanillaTop.Visibility = (editing ? Visibility.Collapsed : Visibility.Visible);", dl);
		// 宿主边距 0 的页,编辑条左右也留 22px(效果图 .bodyA padding)
		Assert.Equal(22.0, LayoutV3Compat.EditBarSideMargin(0.0));
		Assert.Equal(18.0, LayoutV3Compat.EditBarSideMargin(18.0));
		Assert.Equal(56.0, LayoutV3Compat.EditBarSideMargin(56.0));
	}

	[Fact]
	public void AddDrawerListsEachWidgetTypeOnce()
	{
		string v3 = Src("src/TipCL/Pages/Layout/LayoutEditorV3.cs");
		string drawer = Between(v3, "private void RefreshV3Drawer()", "private static string WidgetDescription(");
		Assert.DoesNotContain("I18n.L(\"你建过的", drawer);
		Assert.Contains("AddCustomBlock(reuse)", drawer);
		// 上半部分始终是「已隐藏的内容」,空时虚线空状态
		Assert.Contains("SectionLabel(I18n.L(\"已隐藏的内容\")), _v3DrawerHidden, SectionLabel(I18n.L(\"组件\"))", v3);
		Assert.Contains("没有隐藏的内容", drawer);
	}

	[Fact]
	public void RightClickIsTheOnlyEntryAndMenusAreV3()
	{
		string hooks = Src("src/TipCL/Pages/Layout/LayoutShellHooks.cs");
		Assert.DoesNotContain("I18n.L(\"布局设置...\")", hooks);
		Assert.DoesNotContain("I18n.L(\"重置本页布局\")", hooks);
		// 页面内容里不放「自定义布局...」按钮(插件页 / 设置 → Java 等)
		foreach (string rel in new string[3] { "src/TipCL/Pages/Plugins/PluginsPage.cs", "src/TipCL/Pages/JavaPage.cs", "src/TipCL/Pages/SettingsPage.cs" })
		{
			string src = Src(rel);
			Assert.DoesNotContain("Btn(I18n.L(\"自定义布局", src);
		}
	}

	[Fact]
	public void SelectionIsClearedAfterADrop()
	{
		string le = Src("src/TipCL/Pages/Layout/LayoutEditor.cs");
		string endDrag = Between(le, "private void EndDrag(UIElement src", "private void CancelDrag(");
		Assert.Contains("ClearSelectionAfterDrop();", endDrag);
		string endBox = Between(le, "private void EndBoxGesture(", "private void CompactState(");
		Assert.Contains("CommitBox();\n\t\t\tClearSelectionAfterDrop();", endBox.Replace("\r\n", "\n"));
	}
}
