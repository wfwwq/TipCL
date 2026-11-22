// R35 lin-l3 · GOAL-layout-v3 §5.3 / §6.3「WP-L3」/ D-81 Q1:canvas 模式收敛的迁移单测。
// 对照 Windows 参考:r34/l3 @ d195462 的 windos/tests/TipCL.Tests/LayoutCanvasMigrationTests.cs(语义同口径)。
// 用 3 份 canvas 文档(含隐藏块、custom: 自建组件、plugin: 插件块)验证:
//   1) 推导后块集合不变(id / hidden / 未知字段一个不少);
//   2) 顺序先 y 后 x;
//   3) span 合法:= clamp(round(w × cols), min(cols, max(2, MinCols)), cols),且 w == span、x + w ≤ cols;
//   4) 读入再原样写出逐字节相同(推导只作用在副本上;编辑器只看不改时磁盘文件逐字节不变);
//   5) 编辑器里提交一次之后才变成 grid,id / hidden / widgets 都还在。
// Linux 树没有 AI 的 layout_apply 工具(grep 过 src/,无此名),所以 Windows 那组 RPC 用例在本树 N/A。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using TipCL.App.Pages.Layout;
using TipCL.Core.Launcher.Layout;
using TipCL.Core.Layout;
using TipCL.Tests.App;
using Xunit;

namespace TipCL.Tests.Layout;

/// <summary>canvas 页「读取时按网格推导、提交一次才写成 grid」的迁移单测(纯模型 + 编辑器无头宿主)。</summary>
// [R35 合并后补] 本类夹具 Store..ctor 会 set LayoutStoreV2.DirOverride(进程级全局状态),
// 必须挂在串行集合上 —— GlobalStateGuardTests 会扫出来(IL 守卫,漏了当场红)。
[Collection(GlobalStateCollection.Name)]
public class LayoutCanvasMigrationTests
{
	// ————— 夹具:3 份 canvas 文档 —————

	private const string PageA = "lcm:launch";
	private const string PageB = "lcm:plugins";
	private const string PageC = "lcm:dense";

	private static BlockLayout B(string id, double x, double y, double w, double h, bool hidden = false)
	{
		return new BlockLayout
		{
			Id = id,
			X = x,
			Y = y,
			W = w,
			H = h,
			Hidden = hidden
		};
	}

	private static PageLayoutV2 CanvasOf(int cols, params BlockLayout[] blocks)
	{
		PageLayoutV2 page = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeCanvas,
			Cols = cols
		};
		for (int i = 0; i < blocks.Length; i++)
		{
			blocks[i].Z = i;
			page.Blocks.Add(blocks[i]);
		}
		return page;
	}

	/// <summary>
	/// 文档 1:左右并排两块 + 一块隐藏的内置块 + 一个整行便签组件;块表顺序故意与画布位置不一致。
	/// 文档 2:插件块在右上、两块内置在左侧上下叠、一个隐藏的时钟组件、一个窄到 0.05 的便签(span 必须被下限顶回 2)。
	/// 文档 3:6 列页、重叠块、块带未知字段、row_h 非 0(canvas 下不生效)。
	/// </summary>
	private static LayoutV2 Fixture()
	{
		LayoutV2 doc = LayoutV2.CreateDefault();
		doc.Layout.Widgets.Add(new WidgetDef { Id = "w_note1", Type = LayoutSchema.WidgetNote, Title = "联机便签" });
		doc.Layout.Widgets.Add(new WidgetDef { Id = "w_clock1", Type = LayoutSchema.WidgetClock, Title = "" });
		doc.Layout.Widgets.Add(new WidgetDef { Id = "w_note2", Type = LayoutSchema.WidgetNote, Title = "窄便签" });

		doc.Layout.Pages[PageA] = CanvasOf(12,
			B("custom:w_note1", 0.0, 0.55, 1.0, 0.3),
			B("b", 0.5, 0.0, 0.5, 0.4),
			B("hiddenA", 0.0, 0.45, 0.3, 0.1, hidden: true),
			B("a", 0.0, 0.0, 0.5, 0.4));

		doc.Layout.Pages[PageB] = CanvasOf(12,
			B("a", 0.0, 0.1, 0.6, 0.2),
			B("plugin:demo:panel", 0.6, 0.1, 0.4, 0.4),
			B("custom:w_clock1", 0.2, 0.7, 0.25, 0.2, hidden: true),
			B("b", 0.0, 0.35, 0.6, 0.3),
			B("custom:w_note2", 0.0, 0.8, 0.05, 0.1));

		PageLayoutV2 dense = CanvasOf(6,
			B("c", 0.5, 0.5, 0.5, 0.5),
			B("a", 0.0, 0.0, 0.7, 0.6),
			B("b", 0.3, 0.2, 0.5, 0.3),
			B("plugin:demo:log", 0.0, 0.5, 0.33, 0.5, hidden: true));
		dense.RowH = 80.0;
		dense.Blocks[1].Extra["future_field"] = "keep-me";
		doc.Layout.Pages[PageC] = dense;
		return LayoutSchema.Sanitize(doc);
	}

	private static IEnumerable<string> PageIds()
	{
		return new string[3] { PageA, PageB, PageC };
	}

	// 源页按「先 y 后 x、同坐标保持原块表顺序」排出来的 id
	private static List<string> SourceOrder(PageLayoutV2 page, bool visibleOnly)
	{
		return page.Blocks
			.Select((BlockLayout b, int i) => (b, i))
			.Where(t => !visibleOnly || !t.b.Hidden)
			.OrderBy(t => t.b.Y).ThenBy(t => t.b.X).ThenBy(t => t.i)
			.Select(t => t.b.Id)
			.ToList();
	}

	// 推导出的网格按 (行, 列) 的阅读顺序
	private static List<string> GridReadingOrder(PageLayoutV2 grid)
	{
		return grid.Blocks.Where(b => !b.Hidden).OrderBy(b => b.Y).ThenBy(b => b.X).Select(b => b.Id).ToList();
	}

	// ————— 1. 块集合不变 —————

	[Fact]
	public void DerivedGridKeepsEveryBlockIdHiddenFlagAndUnknownField()
	{
		LayoutV2 doc = Fixture();
		foreach (string pageId in PageIds())
		{
			PageLayoutV2 source = doc.Layout.Pages[pageId];
			PageLayoutV2? grid = LayoutEngine.CanvasGridView(source);
			Assert.NotNull(grid);
			Assert.Equal(LayoutSchema.ModeGrid, grid!.Mode);
			Assert.Equal(source.Blocks.Count, grid.Blocks.Count);
			Assert.Equal(source.Blocks.Select(b => b.Id).OrderBy(s => s, StringComparer.Ordinal),
				grid.Blocks.Select(b => b.Id).OrderBy(s => s, StringComparer.Ordinal));
			foreach (BlockLayout block in source.Blocks)
			{
				BlockLayout derived = grid.Blocks.Single(b => b.Id == block.Id);
				Assert.Equal(block.Hidden, derived.Hidden);
				Assert.Equal(block.Style, derived.Style);
				Assert.Equal(block.Extra.Count, derived.Extra.Count);
			}
		}
		// 自建组件 / 插件块 / 隐藏块都在(这三类是最容易被「白名单」顺手丢掉的)
		PageLayoutV2 b2 = LayoutEngine.CanvasGridView(doc.Layout.Pages[PageB])!;
		Assert.Contains(b2.Blocks, b => b.Id == "plugin:demo:panel" && !b.Hidden);
		Assert.Contains(b2.Blocks, b => b.Id == "custom:w_clock1" && b.Hidden);
		Assert.Contains(b2.Blocks, b => b.Id == "custom:w_note2" && !b.Hidden);
		PageLayoutV2 c2 = LayoutEngine.CanvasGridView(doc.Layout.Pages[PageC])!;
		Assert.Equal("keep-me", c2.Blocks.Single(b => b.Id == "a").Extra["future_field"]);
		Assert.Contains(c2.Blocks, b => b.Id == "plugin:demo:log" && b.Hidden);
	}

	// ————— 2. 顺序先 y 后 x —————

	[Fact]
	public void DerivedGridOrderIsYThenX()
	{
		LayoutV2 doc = Fixture();
		foreach (string pageId in PageIds())
		{
			PageLayoutV2 source = doc.Layout.Pages[pageId];
			PageLayoutV2 grid = LayoutEngine.CanvasGridView(source)!;
			// 块表顺序(含隐藏块)= 先 y 后 x;z = 新序号
			Assert.Equal(SourceOrder(source, visibleOnly: false), grid.Blocks.Select(b => b.Id).ToList());
			Assert.Equal(Enumerable.Range(0, grid.Blocks.Count).ToList(), grid.Blocks.Select(b => b.Z).ToList());
			// 网格的阅读顺序(行优先、行内从左到右)= 先 y 后 x
			Assert.Equal(SourceOrder(source, visibleOnly: true), GridReadingOrder(grid));
		}
		// 具体落点:文档 1 的 a / b 并排在第 0 行,便签整行在第 1 行;隐藏块排在可见块之后
		PageLayoutV2 a = LayoutEngine.CanvasGridView(doc.Layout.Pages[PageA])!;
		BlockLayout ba = a.Blocks.Single(b => b.Id == "a");
		BlockLayout bb = a.Blocks.Single(b => b.Id == "b");
		BlockLayout note = a.Blocks.Single(b => b.Id == "custom:w_note1");
		BlockLayout hid = a.Blocks.Single(b => b.Id == "hiddenA");
		Assert.Equal((0.0, 0.0, 6.0), (ba.X, ba.Y, ba.W));
		Assert.Equal((6.0, 0.0, 6.0), (bb.X, bb.Y, bb.W));
		Assert.Equal((0.0, 1.0, 12.0), (note.X, note.Y, note.W));
		Assert.True(hid.Y > note.Y, "隐藏块应排在可见块之后的行里");
		// 文档 2:插件块与 a 同一行(上沿相同),b 在下一行(上沿在 a 的下沿之下)
		PageLayoutV2 b2 = LayoutEngine.CanvasGridView(doc.Layout.Pages[PageB])!;
		Assert.Equal(0.0, b2.Blocks.Single(b => b.Id == "a").Y);
		Assert.Equal(0.0, b2.Blocks.Single(b => b.Id == "plugin:demo:panel").Y);
		Assert.Equal(7.0, b2.Blocks.Single(b => b.Id == "plugin:demo:panel").X);
		Assert.Equal(1.0, b2.Blocks.Single(b => b.Id == "b").Y);
	}

	[Fact]
	public void CompactionWouldBreakYThenXSoDerivationDoesNotCompact()
	{
		// 右上一块、左边稍低一块:若「每块一行再压紧」(基线 quick-v3 的做法),左边那块会浮到第 0 行第 0 列,
		// 阅读顺序变成 [left, right] —— 违反「先 y 后 x」。R35 的行流放置保持 [right, left]。
		PageLayoutV2 page = CanvasOf(12, B("left", 0.0, 0.2, 0.5, 0.5), B("right", 0.5, 0.0, 0.5, 0.5));
		PageLayoutV2 grid = LayoutEngine.CanvasGridView(page)!;
		Assert.Equal(new List<string> { "right", "left" }, GridReadingOrder(grid));
	}

	// ————— 3. span 合法 —————

	[Fact]
	public void DerivedSpansFollowTheFormulaAndStayInRange()
	{
		LayoutV2 doc = Fixture();
		foreach (string pageId in PageIds())
		{
			PageLayoutV2 source = doc.Layout.Pages[pageId];
			PageLayoutV2 grid = LayoutEngine.CanvasGridView(source)!;
			int cols = LayoutSchema.SnapCols(source.Cols);
			Assert.Equal(cols, grid.Cols);
			Assert.Equal(0.0, grid.RowH); // canvas 下 row_h 不生效,推导后是自适应行
			foreach (BlockLayout block in grid.Blocks)
			{
				BlockLayout before = source.Blocks.Single(b => b.Id == block.Id);
				int expect = Math.Max(Math.Min(cols, 2), Math.Min(cols, (int)Math.Round(before.W * cols, MidpointRounding.AwayFromZero)));
				Assert.Equal(expect, block.Span);
				Assert.InRange(block.Span, Math.Min(cols, LayoutCanvasMigration.MinColsFloor), cols);
				Assert.Equal((double)block.Span, block.W);
				Assert.Equal(1.0, block.H);
				Assert.True(block.X >= 0.0 && block.X + block.W <= cols, block.Id + " 越出右边:" + block.X + "+" + block.W);
				Assert.Equal(Math.Round(block.X), block.X);
				Assert.Equal(Math.Round(block.Y), block.Y);
			}
		}
		// 0.05 宽的便签:round(0.6) = 1 列 → 被下限顶回 2 列
		Assert.Equal(2, LayoutEngine.CanvasGridView(doc.Layout.Pages[PageB])!.Blocks.Single(b => b.Id == "custom:w_note2").Span);
	}

	[Fact]
	public void SpanOfClampsRoundsAwayFromZeroAndHonoursMinCols()
	{
		Assert.Equal(6, LayoutCanvasMigration.SpanOf(0.5, 12, 0));
		Assert.Equal(12, LayoutCanvasMigration.SpanOf(1.0, 12, 0));
		Assert.Equal(12, LayoutCanvasMigration.SpanOf(1.7, 12, 0));    // 越界宽度 → cols
		Assert.Equal(2, LayoutCanvasMigration.SpanOf(0.04, 12, 0));    // 下限 2
		Assert.Equal(2, LayoutCanvasMigration.SpanOf(0.0, 12, 0));
		Assert.Equal(5, LayoutCanvasMigration.SpanOf(0.375, 12, 0));   // 4.5 → 5(远离零)
		Assert.Equal(5, LayoutCanvasMigration.SpanOf(0.3, 12, 5));     // MinCols 5 顶上来
		Assert.Equal(12, LayoutCanvasMigration.SpanOf(0.3, 12, 40));   // MinCols 超过列数 → cols
		Assert.Equal(12, LayoutCanvasMigration.SpanOf(double.NaN, 12, 0));
		Assert.Equal(1, LayoutCanvasMigration.SpanOf(0.5, 1, 0));      // 1 列页:下限也只能是 1
		Assert.Equal(3, LayoutCanvasMigration.SpanOf(0.5, 6, 0));
		// 每块最小列数经 minColsOf 传进来
		PageLayoutV2 page = CanvasOf(12, B("wide", 0.0, 0.0, 0.1, 0.2), B("free", 0.0, 0.5, 0.1, 0.2));
		PageLayoutV2 grid = LayoutEngine.CanvasGridView(page, id => id == "wide" ? 8 : 0)!;
		Assert.Equal(8, grid.Blocks.Single(b => b.Id == "wide").Span);
		Assert.Equal(2, grid.Blocks.Single(b => b.Id == "free").Span);
	}

	[Fact]
	public void NonCanvasPagesAreLeftAlone()
	{
		PageLayoutV2 grid = new PageLayoutV2 { Mode = LayoutSchema.ModeGrid, Cols = 12 };
		grid.Blocks.Add(B("a", 3.0, 2.0, 4.0, 1.0));
		string before = Json(grid);
		Assert.False(LayoutEngine.CanvasToGrid(grid));
		Assert.False(LayoutCanvasMigration.ConvertInPlace(grid));
		Assert.Equal(before, Json(grid));
		Assert.Equal(before, Json(LayoutEngine.CanvasGridView(grid)!));
		Assert.Null(LayoutEngine.CanvasGridView(null));
		Assert.False(LayoutCanvasMigration.IsCanvas(null));
	}

	private static string Json(PageLayoutV2 page)
	{
		return System.Text.Json.JsonSerializer.Serialize(page.ToDict());
	}

	// ————— 4. 读入再原样写出逐字节相同 —————

	[Fact]
	public void ReadDeriveAndWriteBackIsByteIdentical()
	{
		string text0 = LayoutStoreV2.ExportJson(Fixture());
		LayoutV2? loaded = LayoutV2.FromJsonText(text0);
		Assert.NotNull(loaded);
		foreach (string pageId in PageIds())
		{
			PageLayoutV2 page = loaded!.Layout.Pages[pageId];
			Assert.Equal(LayoutSchema.ModeCanvas, page.Mode);
			// 渲染侧的三条推导入口都只作用在副本上
			LayoutEngine.CanvasGridView(page);
			LayoutCanvasMigration.ToGridPage(page);
			TestApp.Run(delegate
			{
				GridCanvasPanel panel = new GridCanvasPanel();
				panel.SetLayout(page.Mode, page.Cols, page.RowH, page.Gap, page);
				Assert.Equal(LayoutSchema.ModeGrid, panel.Mode);
			});
		}
		string text1 = LayoutStoreV2.ExportJson(loaded);
		Assert.Equal(Encoding.UTF8.GetBytes(text0), Encoding.UTF8.GetBytes(text1));
	}

	[Fact]
	public void EditorThatOnlyLooksLeavesTheFileByteIdentical()
	{
		using Store store = new Store();
		LayoutStoreV2.Save(Fixture());
		byte[] bytes0 = File.ReadAllBytes(store.V2File);
		LayoutStoreV2.ResetCache();
		TestApp.Run(delegate
		{
			foreach (string pageId in PageIds())
			{
				using Host host = new Host(pageId, SpecsOf(pageId));
				// 渲染按网格(内存里),磁盘不动
				Assert.Equal(LayoutSchema.ModeGrid, host.Editor.State.Page.Mode);
				host.EnterEdit();
				host.Editor.ExitEdit();
			}
		});
		LayoutStoreV2.ResetCache();
		Assert.Equal(bytes0, File.ReadAllBytes(store.V2File));
		foreach (string pageId in PageIds())
		{
			Assert.Equal(LayoutSchema.ModeCanvas, LayoutStoreV2.Load().Layout.Pages[pageId].Mode);
		}
	}

	// ————— 5. 编辑器提交一次之后才写成 grid —————

	[Fact]
	public void EditorCommitWritesGridAndKeepsIdsHiddenAndWidgets()
	{
		using Store store = new Store();
		LayoutV2 fixture = Fixture();
		LayoutStoreV2.Save(fixture);
		LayoutStoreV2.ResetCache();
		LayoutV2 before = LayoutStoreV2.Load();
		TestApp.Run(delegate
		{
			foreach (string pageId in PageIds())
			{
				using Host host = new Host(pageId, SpecsOf(pageId));
				host.EnterEdit();
				// 一次真实提交:把一个可见内置块隐藏再放回(净效果不改 hidden),然后「完成」
				host.Editor.SetBlockHidden("a", true);
				host.Editor.SetBlockHidden("a", false);
				host.Editor.ExitEdit();
			}
		});
		LayoutStoreV2.ResetCache();
		LayoutV2 after = LayoutStoreV2.Load();
		foreach (string pageId in PageIds())
		{
			PageLayoutV2 src = before.Layout.Pages[pageId];
			PageLayoutV2 got = after.Layout.Pages[pageId];
			Assert.Equal(LayoutSchema.ModeGrid, got.Mode);
			Assert.Equal(src.Blocks.Select(b => b.Id).OrderBy(s => s, StringComparer.Ordinal),
				got.Blocks.Select(b => b.Id).OrderBy(s => s, StringComparer.Ordinal));
			foreach (BlockLayout block in src.Blocks)
			{
				Assert.Equal(block.Hidden, got.Blocks.Single(b => b.Id == block.Id).Hidden);
			}
			foreach (BlockLayout block in got.Blocks)
			{
				Assert.InRange(block.W, 1.0, (double)got.Cols);
			}
		}
		// widgets[] 定义一个不少(id / type / title)
		Assert.Equal(before.Layout.Widgets.Select(w => (w.Id, w.Type, w.Title)), after.Layout.Widgets.Select(w => (w.Id, w.Type, w.Title)));
		Assert.Equal("keep-me", after.Layout.Pages[PageC].Blocks.Single(b => b.Id == "a").Extra["future_field"]);
	}

	[Fact]
	public void PanelDerivesGridFromCanvasAndApplyToCommitsGrid()
	{
		TestApp.Run(delegate
		{
			PageLayoutV2 page = Fixture().Layout.Pages[PageB];
			string before = Json(page);
			GridCanvasPanel panel = new GridCanvasPanel();
			panel.SetLayout(LayoutSchema.ModeCanvas, page.Cols, page.RowH, page.Gap, page);
			Assert.True(panel.DerivedFromCanvas);
			Assert.False(panel.IsCanvas);
			Assert.Equal(LayoutSchema.ModeGrid, panel.Mode);
			Assert.Equal(SourceOrder(page, visibleOnly: true),
				panel.Cells.OrderBy(c => c.Y).ThenBy(c => c.X).Select(c => c.Id).ToList());
			Assert.Equal(before, Json(page)); // 只读推导:入参不动

			panel.ApplyTo(page); // 提交 ⇒ 整页写成 grid
			Assert.Equal(LayoutSchema.ModeGrid, page.Mode);
			Assert.Equal(5, page.Blocks.Count);
			Assert.True(page.Blocks.Single(b => b.Id == "custom:w_clock1").Hidden);
			Assert.Contains(page.Blocks, b => b.Id == "plugin:demo:panel");
		});
	}

	// ————— 夹具:隔离数据目录 + 编辑器无头宿主(照 LayoutEditorUiTests 的写法,私有类不能跨文件复用) —————

	private sealed class Store : IDisposable
	{
		public string Dir { get; } = Path.Combine(Path.GetTempPath(), "tipcl_lcm_" + Guid.NewGuid().ToString("N"));

		public Store()
		{
			Directory.CreateDirectory(Dir);
			LayoutStoreV2.DirOverride = Dir;
			LayoutStoreV2.V1Bridge = new LinuxV1Bridge();
			PageLayoutStore.DirOverride = Dir;
			LayoutStoreV2.ResetCache();
			LayoutStoreV2.RefreshV1Cache();
			LayoutVariantHost.Reset();
		}

		public string V2File
		{
			get { return Path.Combine(Dir, LayoutStoreV2.FileName); }
		}

		public void Dispose()
		{
			LayoutStoreV2.DirOverride = null;
			LayoutStoreV2.V1Bridge = null;
			PageLayoutStore.DirOverride = null;
			LayoutStoreV2.ResetCache();
			LayoutVariantHost.Reset();
			try
			{
				Directory.Delete(Dir, true);
			}
			catch (Exception)
			{
			}
		}
	}

	private sealed class Host : IDisposable
	{
		public Window Window { get; }

		public LayoutEditor Editor { get; }

		public Host(string pageId, params BlockSpec[] specs)
		{
			Editor = new LayoutEditor(pageId, specs, new Thickness(0.0), null);
			Window = new Window
			{
				Width = 900.0,
				Height = 600.0,
				Content = Editor.Root
			};
			Window.Show();
			Window.UpdateLayout();
		}

		public void EnterEdit()
		{
			Editor.EnterEdit();
			Window.UpdateLayout();
		}

		public void Dispose()
		{
			Window.Close();
		}
	}

	private static BlockSpec Spec(string id)
	{
		return new BlockSpec
		{
			Id = id,
			Title = id,
			Content = new TextBlock { Text = id }
		};
	}

	// 每页的内置块白名单(只给文档里真有的内置块,免得编辑器把白名单里多出来的块补进页面)
	private static BlockSpec[] SpecsOf(string pageId)
	{
		if (pageId == PageA)
		{
			return new BlockSpec[3] { Spec("a"), Spec("b"), Spec("hiddenA") };
		}
		return (pageId == PageC) ? new BlockSpec[3] { Spec("a"), Spec("b"), Spec("c") } : new BlockSpec[2] { Spec("a"), Spec("b") };
	}
}
