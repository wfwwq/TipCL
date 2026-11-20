using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TipCL.Pages;
using TipCL.Pages.Layout;
using Xunit;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Tests;

/// <summary>
/// R34 模式收敛(GOAL-layout-v3 §5.3 / §6.3 WP-L3,D-81 Q1):canvas 退出界面后,旧的 canvas 页**读取时**按网格推导。
/// 用 3 份 canvas 文档(含隐藏块 / 自建组件 / 插件块)钉住:
/// <list type="number">
/// <item>渲染出的块集合不变(一个都不丢,隐藏、custom:、plugin:、白名单外的块都在);</item>
/// <item>顺序按「先 y 后 x」;</item>
/// <item>span = clamp(round(w × cols), max(2, MinCols), cols),在合法范围内;</item>
/// <item>读进来再原样写出去逐字节相同 —— 推导(渲染)路径不改文档、不碰文件;</item>
/// <item>在编辑器里提交一次之后变成 grid,id / hidden / widgets / 未知字段 / 方案都在。</item>
/// </list>
/// 推导函数是纯模型(<see cref="LayoutCanvasMigration"/>,Core),画布 Panel 的提交 = <see cref="LayoutGrid.CommitCanvasAsGrid"/>;
/// WPF 那一层(GridCanvasPanel.SetLayout / ApplyTo)只是把这两个函数接上,真机截图见 WP-L3 报告的「待真机」。
/// 落盘一律走 <c>DirOverride</c> 临时目录,不碰用户真实数据目录。
/// </summary>
public class LayoutCanvasMigrationTests : IDisposable
{
	private readonly string _tmp = Path.Combine(Path.GetTempPath(), "tipcl_layout_canvas_migration_" + Guid.NewGuid().ToString("N"));

	public LayoutCanvasMigrationTests()
	{
		UseDir();
	}

	/// <summary>进程级静态,每个用例开头都重设一次(与 LayoutV2RpcTests 同一套隔离)。</summary>
	private void UseDir()
	{
		Directory.CreateDirectory(_tmp);
		LayoutStoreV2.DirOverride = _tmp;
		LayoutStoreV2.V1Bridge = new PagesLayoutV1Bridge();
		PageLayoutStore.DirOverride = _tmp;
		LayoutStoreV2.ResetCache();
		PageLayoutStore.Reload();
	}

	public void Dispose()
	{
		LayoutStoreV2.DirOverride = null;
		LayoutStoreV2.V1Bridge = null;
		PageLayoutStore.DirOverride = null;
		LayoutStoreV2.ResetCache();
		PageLayoutStore.Reload();
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

	// ————— 三份种子文档 —————

	/// <summary>含隐藏块:下载页,12 列;块表顺序故意打乱,另带页级 / 块级未知字段。</summary>
	private const string SeedHidden = """
	{
	  "kind": "tipcl-layout",
	  "version": 2,
	  "layout": {
	    "pages": {
	      "download": {
	        "version": 2,
	        "mode": "canvas",
	        "cols": 12,
	        "x_future_page": { "keep": true },
	        "blocks": [
	          { "id": "list", "x": 0, "y": 0.4, "w": 1, "h": 0.6, "style": "card" },
	          { "id": "filters", "x": 0.5, "y": 0, "w": 0.5, "h": 0.3, "min_h": 140 },
	          { "id": "search", "x": 0, "y": 0, "w": 0.5, "h": 0.3, "x_future_block": "keep" },
	          { "id": "tips", "hidden": true, "x": 0.2, "y": 0.2, "w": 0.3, "h": 0.2 }
	        ]
	      }
	    }
	  }
	}
	""";

	/// <summary>含自建组件:启动页,两个 custom: 块 + widgets[] 定义(标题带中文和引号),顶层未知字段。</summary>
	private const string SeedWidget = """
	{
	  "kind": "tipcl-layout",
	  "version": 2,
	  "future_top": { "a": 1 },
	  "layout": {
	    "pages": {
	      "launch": {
	        "version": 2,
	        "mode": "canvas",
	        "cols": 12,
	        "gap": 14,
	        "blocks": [
	          { "id": "custom:w-clock", "x": 0.1, "y": 0.6, "w": 0.04, "h": 0.2 },
	          { "id": "news", "x": 0, "y": 0, "w": 0.66, "h": 0.5 },
	          { "id": "tasks", "hidden": true, "x": 0, "y": 0.8, "w": 0.5, "h": 0.2 },
	          { "id": "custom:w-note1", "x": 0.66, "y": 0, "w": 0.34, "h": 0.5, "settings": { "pinned": true } }
	        ]
	      }
	    },
	    "widgets": [
	      { "id": "w-note1", "type": "note", "title": "我的便签", "settings": { "text": "带 \"引号\" 的内容" } },
	      { "id": "w-clock", "type": "clock", "title": "", "settings": {} }
	    ]
	  }
	}
	""";

	/// <summary>含插件块:插件页,6 列;一个隐藏的插件块、一个带外观的插件块、一个白名单外的内置块,外加一份 canvas 方案。</summary>
	private const string SeedPlugin = """
	{
	  "kind": "tipcl-layout",
	  "version": 2,
	  "layout": {
	    "pages": {
	      "plugins": {
	        "version": 2,
	        "mode": "canvas",
	        "cols": 6,
	        "blocks": [
	          { "id": "list", "x": 0, "y": 0.1, "w": 0.5, "h": 0.9 },
	          { "id": "ai:header", "x": 0, "y": 0.95, "w": 1, "h": 0.05 },
	          { "id": "plugin:hello:farm", "x": 0.5, "y": 0.1, "w": 0.5, "h": 0.4, "style": "card", "title": true, "height_px": 220, "settings": { "k": "v" } },
	          { "id": "plugin:other:stats", "hidden": true, "x": 0, "y": 0, "w": 0.3, "h": 0.2 }
	        ]
	      }
	    }
	  },
	  "profiles": {
	    "工作": {
	      "pages": {
	        "plugins": {
	          "version": 2,
	          "mode": "canvas",
	          "blocks": [ { "id": "list", "x": 0.2, "y": 0, "w": 0.6, "h": 0.5 } ]
	        }
	      }
	    }
	  }
	}
	""";

	private sealed class Seed
	{
		public string Json = "";

		public string PageId = "";

		public LayoutBlockRef[] Refs = new LayoutBlockRef[0];

		public (string Id, int X, int Y, int Span, bool Hidden)[] Expected = new (string, int, int, int, bool)[0];
	}

	private static Seed SeedOf(string name)
	{
		switch (name)
		{
		case "hidden":
			return new Seed
			{
				Json = SeedHidden,
				PageId = "download",
				Refs = new LayoutBlockRef[4]
				{
					new LayoutBlockRef("search", defaultVisible: true),
					new LayoutBlockRef("filters", defaultVisible: true),
					new LayoutBlockRef("list", defaultVisible: true),
					new LayoutBlockRef("tips", defaultVisible: true)
				},
				// 12 列:0.5 → 6 列并排;隐藏的 0.3 → round(3.6) = 4;满宽 1.0 放不下 → 换行
				Expected = new (string, int, int, int, bool)[4]
				{
					("search", 0, 0, 6, false),
					("filters", 6, 0, 6, false),
					("tips", 0, 0, 4, true),
					("list", 0, 1, 12, false)
				}
			};
		case "widget":
			return new Seed
			{
				Json = SeedWidget,
				PageId = "launch",
				Refs = new LayoutBlockRef[2]
				{
					new LayoutBlockRef("news", defaultVisible: true),
					new LayoutBlockRef("tasks", defaultVisible: false)
				},
				// 0.66 → round(7.92) = 8;0.34 → 4;0.04 → round(0.48) = 0 → 下限 2 列;
				// 时钟的上沿 0.6 已经在第一行最矮块的下沿 0.5 之下 → 换行
				Expected = new (string, int, int, int, bool)[4]
				{
					("news", 0, 0, 8, false),
					("custom:w-note1", 8, 0, 4, false),
					("custom:w-clock", 0, 1, 2, false),
					("tasks", 0, 0, 6, true)
				}
			};
		case "plugin":
			return new Seed
			{
				Json = SeedPlugin,
				PageId = "plugins",
				Refs = new LayoutBlockRef[1]
				{
					new LayoutBlockRef("list", defaultVisible: true)
				},
				// 6 列:隐藏的 0.3 → round(1.8) = 2;两个 0.5 → 3 + 3;白名单外的 ai:header 满宽 → 换行(不丢)
				Expected = new (string, int, int, int, bool)[4]
				{
					("plugin:other:stats", 0, 0, 2, true),
					("list", 0, 0, 3, false),
					("plugin:hello:farm", 3, 0, 3, false),
					("ai:header", 0, 1, 6, false)
				}
			};
		default:
			throw new ArgumentException(name);
		}
	}

	private static LayoutV2 Parse(string json)
	{
		LayoutV2? doc = LayoutV2.FromJsonText(json);
		Assert.NotNull(doc);
		return doc!;
	}

	private static string Canonical(string json)
	{
		return Parse(json).ToJson();
	}

	private static string Json(object? value)
	{
		return JsonSerializer.Serialize(value);
	}

	private static string[] ExpectedOrder(PageLayoutV2 page)
	{
		return page.Blocks.Select((BlockLayout b, int i) => (b, i)).OrderBy(t => t.b.Y).ThenBy(t => t.b.X).ThenBy(t => t.i)
			.Select(t => t.b.Id)
			.ToArray();
	}

	private static BlockLayout BlockOf(PageLayoutV2 page, string id)
	{
		BlockLayout? block = page.Blocks.FirstOrDefault((BlockLayout b) => b.Id == id);
		Assert.NotNull(block);
		return block!;
	}

	// ————— 1. 读进来再原样写出去:逐字节相同(推导不改文档) —————

	[Theory]
	[InlineData("hidden")]
	[InlineData("widget")]
	[InlineData("plugin")]
	public void ReadThenWriteIsByteIdenticalAndStaysCanvas(string name)
	{
		string canonical = Canonical(SeedOf(name).Json);
		LayoutV2 again = Parse(canonical);
		Assert.Equal(Encoding.UTF8.GetBytes(canonical), Encoding.UTF8.GetBytes(again.ToJson()));
		// 契约层不改写:canvas 仍是 canvas(R34 只在读取时推导)
		Assert.Equal(LayoutSchema.ModeCanvas, again.Layout.Pages[SeedOf(name).PageId].Mode);
	}

	[Theory]
	[InlineData("hidden")]
	[InlineData("widget")]
	[InlineData("plugin")]
	public void RenderingDerivationNeverTouchesTheDocumentOrTheFile(string name)
	{
		UseDir();
		Seed seed = SeedOf(name);
		string canonical = Canonical(seed.Json);
		string path = Path.Combine(_tmp, LayoutStoreV2.FileName);
		File.WriteAllText(path, canonical, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		byte[] before = File.ReadAllBytes(path);
		LayoutStoreV2.ResetCache();
		LayoutV2 doc = LayoutStoreV2.Load();
		Assert.Equal(canonical, doc.ToJson());
		// 渲染路径会做的所有事:推导网格 → 打包槽位 → 装页面状态 → 排布计划(主 body 与方案里的页都走一遍)
		List<PageLayoutV2> pages = new List<PageLayoutV2>(doc.Layout.Pages.Values);
		foreach (LayoutBody body in doc.Profiles.Values)
		{
			pages.AddRange(body.Pages.Values);
		}
		foreach (PageLayoutV2 page in pages)
		{
			PageLayoutV2? derived = LayoutCanvasMigration.ToGridPage(page);
			Assert.NotNull(derived);
			LayoutGrid.Pack(derived);
			LayoutEngine.LoadState(seed.PageId, page, seed.Refs).Plan();
		}
		Assert.Equal(Encoding.UTF8.GetBytes(canonical), Encoding.UTF8.GetBytes(doc.ToJson()));
		Assert.Equal(before, File.ReadAllBytes(path));
		LayoutStoreV2.ResetCache();
		Assert.Equal(canonical, LayoutStoreV2.Load().ToJson());
	}

	// ————— 2. 推导:块集合不变 / 先 y 后 x / span 合法 / 外观与数据原样 —————

	[Theory]
	[InlineData("hidden")]
	[InlineData("widget")]
	[InlineData("plugin")]
	public void DerivedGridKeepsEveryBlockOrdersByYThenXAndClampsSpans(string name)
	{
		Seed seed = SeedOf(name);
		LayoutV2 doc = Parse(seed.Json);
		string docBefore = doc.ToJson();
		PageLayoutV2 page = doc.Layout.Pages[seed.PageId];
		int cols = LayoutSchema.SnapCols(page.Cols);

		PageLayoutV2? derived = LayoutCanvasMigration.ToGridPage(page);

		Assert.NotNull(derived);
		PageLayoutV2 grid = derived!;
		Assert.Equal(docBefore, doc.ToJson()); // 入参一个字节都不改
		Assert.Equal(LayoutSchema.ModeCanvas, page.Mode);
		Assert.Equal(LayoutSchema.ModeGrid, grid.Mode);
		Assert.Equal(cols, grid.Cols);
		// 块集合不变(数量与 id 都一样)
		Assert.Equal(page.Blocks.Count, grid.Blocks.Count);
		Assert.Equal(page.Blocks.Select((BlockLayout b) => b.Id).OrderBy((string id) => id, StringComparer.Ordinal), grid.Blocks.Select((BlockLayout b) => b.Id).OrderBy((string id) => id, StringComparer.Ordinal));
		// 顺序:先 y 后 x(同位置保持原块表顺序)
		string[] order = ExpectedOrder(page);
		Assert.Equal(order, grid.Blocks.Select((BlockLayout b) => b.Id).ToArray());
		for (int i = 0; i < grid.Blocks.Count; i++)
		{
			Assert.Equal(i, grid.Blocks[i].Z);
		}
		foreach (BlockLayout block in grid.Blocks)
		{
			BlockLayout source = BlockOf(page, block.Id);
			// span 在合法范围内,且就是 clamp(round(w × cols), 2, cols)
			Assert.InRange(block.Span, Math.Min(cols, LayoutCanvasMigration.MinColsFloor), cols);
			Assert.Equal(LayoutCanvasMigration.SpanOf(source.W, cols, 0), block.Span);
			// 显隐、外观、数据、未知字段原样
			Assert.Equal(source.Hidden, block.Hidden);
			Assert.Equal(source.Style, block.Style);
			Assert.Equal(source.Title, block.Title);
			Assert.Equal(source.MinH, block.MinH);
			Assert.Equal(source.HeightPx, block.HeightPx);
			Assert.Equal(Json(source.Settings), Json(block.Settings));
			Assert.Equal(Json(source.Extra), Json(block.Extra));
			if (block.Hidden)
			{
				// 隐藏块写成契约默认矩形:重新显示时网格按 span 自动流
				Assert.Equal(0.0, block.X);
				Assert.Equal(0.0, block.Y);
				Assert.Equal(1.0, block.W);
				Assert.Equal(1.0, block.H);
			}
			else
			{
				Assert.Equal(block.Span, (int)block.W);
				Assert.Equal(1.0, block.H);
				Assert.True(block.X + block.W <= cols, block.Id + " 越过了右边界");
			}
		}
		Assert.Equal(Json(page.Extra), Json(grid.Extra));
		Assert.Equal(page.Gap, grid.Gap);
		Assert.Equal(Json(page.Pad), Json(grid.Pad));
		// 推导结果本身就是合法的 grid 页:再过一遍 Sanitize 什么都不变
		Assert.Equal(Json(new LayoutV2 { Layout = new LayoutBody { Pages = { ["p"] = grid.Clone() } } }.ToJson()), Json(LayoutSchema.Sanitize(new LayoutV2 { Layout = new LayoutBody { Pages = { ["p"] = grid.Clone() } } }).ToJson()));
		// 网格打包:显示的就是推导出来的位置(不再自动流),顺序与「先 y 后 x」一致,不重叠
		List<LayoutCell> cells = LayoutGrid.Pack(grid);
		string[] visible = order.Where((string id) => !BlockOf(page, id).Hidden).ToArray();
		Assert.Equal(visible, cells.Select((LayoutCell c) => c.Id).ToArray());
		Assert.Equal(visible, cells.OrderBy((LayoutCell c) => c.Y).ThenBy((LayoutCell c) => c.X).Select((LayoutCell c) => c.Id).ToArray());
		foreach (LayoutCell cell in cells)
		{
			BlockLayout block = BlockOf(grid, cell.Id);
			Assert.Equal((int)block.X, cell.X);
			Assert.Equal((int)block.Y, cell.Y);
			Assert.Equal(block.Span, cell.W);
		}
		for (int a = 0; a < cells.Count; a++)
		{
			for (int b = a + 1; b < cells.Count; b++)
			{
				bool overlap = cells[a].X < cells[b].X + cells[b].W && cells[b].X < cells[a].X + cells[a].W && cells[a].Y < cells[b].Y + cells[b].H && cells[b].Y < cells[a].Y + cells[a].H;
				Assert.False(overlap, cells[a].Id + " 与 " + cells[b].Id + " 重叠");
			}
		}
	}

	[Theory]
	[InlineData("hidden")]
	[InlineData("widget")]
	[InlineData("plugin")]
	public void DerivedPositionsArePinned(string name)
	{
		Seed seed = SeedOf(name);
		PageLayoutV2 grid = LayoutCanvasMigration.ToGridPage(Parse(seed.Json).Layout.Pages[seed.PageId])!;
		Assert.Equal(seed.Expected.Select(e => e.Id).ToArray(), grid.Blocks.Select((BlockLayout b) => b.Id).ToArray());
		foreach ((string Id, int X, int Y, int Span, bool Hidden) e in seed.Expected)
		{
			BlockLayout block = BlockOf(grid, e.Id);
			Assert.Equal(e.Hidden, block.Hidden);
			Assert.Equal(e.Span, block.Span);
			Assert.Equal((double)e.X, block.X);
			Assert.Equal((double)e.Y, block.Y);
		}
	}

	// ————— 3. 编辑器里提交一次 → grid,id / hidden / widgets 都在 —————

	[Theory]
	[InlineData("hidden")]
	[InlineData("widget")]
	[InlineData("plugin")]
	public void CommittingOnceInTheEditorWritesGridAndKeepsIdsHiddenAndWidgets(string name)
	{
		UseDir();
		Seed seed = SeedOf(name);
		LayoutV2 seedDoc = Parse(seed.Json);
		Assert.True(LayoutStoreV2.Save(seedDoc.Clone()));
		LayoutStoreV2.ResetCache();
		LayoutV2 doc = LayoutStoreV2.Load();
		PageLayoutV2 original = doc.Layout.Pages[seed.PageId].Clone();
		Assert.Equal(LayoutSchema.ModeCanvas, original.Mode); // 存盘本身不改写 canvas

		// 编辑器的同一条链(LayoutEditor.RenderBox → GridCanvasPanel.SetLayout / CommitBox → ApplyTo 的纯模型等价):
		// 画布槽位来自推导出的网格;提交时整页(含白名单外的块)写成 grid,再用槽位覆盖可见块
		LayoutPageState state = LayoutEngine.LoadState(seed.PageId, doc.Layout.Pages[seed.PageId], seed.Refs);
		List<LayoutCell> cells = LayoutGrid.Pack(LayoutCanvasMigration.ToGridPage(state.Page));
		PageLayoutV2 page = state.ToPage();
		LayoutGrid.CommitCanvasAsGrid(page, cells);
		LayoutEngine.ApplyPage(doc, seed.PageId, page);
		Assert.True(LayoutStoreV2.Save(doc));

		LayoutStoreV2.ResetCache();
		LayoutV2 back = LayoutStoreV2.Load();
		PageLayoutV2 saved = back.Layout.Pages[seed.PageId];
		Assert.Equal(LayoutSchema.ModeGrid, saved.Mode);
		// id 一个不少、一个不多;显隐逐块相同
		Assert.Equal(original.Blocks.Select((BlockLayout b) => b.Id).OrderBy((string id) => id, StringComparer.Ordinal), saved.Blocks.Select((BlockLayout b) => b.Id).OrderBy((string id) => id, StringComparer.Ordinal));
		foreach (BlockLayout block in original.Blocks)
		{
			BlockLayout now = BlockOf(saved, block.Id);
			Assert.Equal(block.Hidden, now.Hidden);
			Assert.Equal(block.Style, now.Style);
			Assert.Equal(block.Title, now.Title);
			Assert.Equal(Json(block.Settings), Json(now.Settings));
			Assert.Equal(Json(block.Extra), Json(now.Extra));
		}
		// widgets、页级 / 顶层未知字段、方案原样
		Assert.Equal(seedDoc.Layout.Widgets.Count, back.Layout.Widgets.Count);
		for (int i = 0; i < seedDoc.Layout.Widgets.Count; i++)
		{
			WidgetDef expected = seedDoc.Layout.Widgets[i];
			WidgetDef actual = back.Layout.Widgets[i];
			Assert.Equal(expected.Id, actual.Id);
			Assert.Equal(expected.Type, actual.Type);
			Assert.Equal(expected.Title, actual.Title);
			Assert.Equal(Json(expected.Settings), Json(actual.Settings));
		}
		Assert.Equal(Json(original.Extra), Json(saved.Extra));
		Assert.Equal(Json(seedDoc.Extra), Json(back.Extra));
		Assert.Equal(seedDoc.Profiles.Keys.OrderBy((string k) => k, StringComparer.Ordinal), back.Profiles.Keys.OrderBy((string k) => k, StringComparer.Ordinal));
		foreach (KeyValuePair<string, LayoutBody> kv in seedDoc.Profiles)
		{
			// 方案只是存档,不是「编辑器里提交」的对象:里面的 canvas 页保持原样
			Assert.Equal(Json(kv.Value.ToDict()), Json(back.Profiles[kv.Key].ToDict()));
		}
		// 提交后看到的就是提交前看到的:网格位置与推导时一致
		List<LayoutCell> after = LayoutGrid.Pack(saved);
		foreach (LayoutCell cell in cells)
		{
			LayoutCell? now = after.FirstOrDefault((LayoutCell c) => c.Id == cell.Id);
			Assert.NotNull(now);
			Assert.Equal(cell.X, now!.X);
			Assert.Equal(cell.Y, now.Y);
			Assert.Equal(cell.W, now.W);
		}
		// 已经是 grid 了:再「提交」一次就是普通的网格写回,不再推导
		Assert.False(LayoutCanvasMigration.ConvertInPlace(saved));
	}

	[Fact]
	public void CommitAfterADragKeepsTheUsersNewGeometry()
	{
		Seed seed = SeedOf("hidden");
		PageLayoutV2 page = Parse(seed.Json).Layout.Pages[seed.PageId];
		List<LayoutCell> cells = LayoutGrid.Pack(LayoutCanvasMigration.ToGridPage(page));
		// 用户把「filters」的右边缘往左拖 2 列(6 → 4)
		LayoutCell filters = cells.First((LayoutCell c) => c.Id == "filters");
		Assert.True(LayoutGrid.ResizeCell(filters, "e", -2, 0, 12));

		LayoutGrid.CommitCanvasAsGrid(page, cells);

		Assert.Equal(LayoutSchema.ModeGrid, page.Mode);
		BlockLayout block = BlockOf(page, "filters");
		Assert.Equal(4.0, block.W);
		Assert.Equal(4, block.Span);
		Assert.Equal(6.0, block.X);
		// 没被拖的块用推导值,隐藏块保留推导出的 span
		Assert.Equal(12, BlockOf(page, "list").Span);
		Assert.Equal(4, BlockOf(page, "tips").Span);
		Assert.True(BlockOf(page, "tips").Hidden);
	}

	// ————— 4. 纯函数的边界 —————

	[Fact]
	public void SpanOfRoundsAndClamps()
	{
		Assert.Equal(6, LayoutCanvasMigration.SpanOf(0.5, 12, 0));
		Assert.Equal(12, LayoutCanvasMigration.SpanOf(1.0, 12, 0));
		Assert.Equal(5, LayoutCanvasMigration.SpanOf(0.45, 12, 0));   // 5.4 → 5
		Assert.Equal(5, LayoutCanvasMigration.SpanOf(0.375, 12, 0));  // 4.5 → 5(远离零)
		Assert.Equal(2, LayoutCanvasMigration.SpanOf(0.04, 12, 0));   // 0.48 → 0 → 下限 2
		Assert.Equal(6, LayoutCanvasMigration.SpanOf(0.3, 12, 6));    // 3.6 → 4 → MinCols 6
		Assert.Equal(12, LayoutCanvasMigration.SpanOf(0.2, 12, 20));  // MinCols 超过列数 → 列数
		Assert.Equal(2, LayoutCanvasMigration.SpanOf(0.5, 3, 0));     // 1.5 → 2
		Assert.Equal(12, LayoutCanvasMigration.SpanOf(double.NaN, 12, 0));
		Assert.Equal(12, LayoutCanvasMigration.SpanOf(5.0, 12, 0));
		Assert.Equal(2, LayoutCanvasMigration.SpanOf(-1.0, 12, 0));
	}

	[Fact]
	public void MinColsOfWidensNarrowBlocksAndWrapsTheRow()
	{
		Seed seed = SeedOf("hidden");
		PageLayoutV2 page = Parse(seed.Json).Layout.Pages[seed.PageId];
		PageLayoutV2 grid = LayoutCanvasMigration.ToGridPage(page, (string id) => (id == "filters") ? 8 : 0)!;
		Assert.Equal(8, BlockOf(grid, "filters").Span);
		// 6 + 8 > 12:filters 换到下一行,list 再往下
		Assert.Equal(0.0, BlockOf(grid, "search").Y);
		Assert.Equal(0.0, BlockOf(grid, "filters").X);
		Assert.Equal(1.0, BlockOf(grid, "filters").Y);
		Assert.Equal(2.0, BlockOf(grid, "list").Y);
	}

	[Fact]
	public void NonCanvasPagesAreLeftAlone()
	{
		Assert.Null(LayoutCanvasMigration.ToGridPage(null));
		Assert.False(LayoutCanvasMigration.ConvertInPlace(null));
		Assert.False(LayoutCanvasMigration.IsCanvas(null));
		PageLayoutV2 grid = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeGrid,
			Cols = 4
		};
		grid.Blocks.Add(new BlockLayout { Id = "b", X = 0.0, Y = 0.0, W = 1.0, H = 1.0 });
		grid.Blocks.Add(new BlockLayout { Id = "a", X = 2.0, Y = 0.0, W = 2.0, H = 1.0, Span = 2 });
		string before = Json(new LayoutBody { Pages = { ["p"] = grid } }.ToDict());
		Assert.False(LayoutCanvasMigration.ConvertInPlace(grid));
		PageLayoutV2? copy = LayoutCanvasMigration.ToGridPage(grid);
		Assert.NotNull(copy);
		Assert.NotSame(grid, copy);
		Assert.Equal(before, Json(new LayoutBody { Pages = { ["p"] = grid } }.ToDict()));
		Assert.Equal(before, Json(new LayoutBody { Pages = { ["p"] = copy! } }.ToDict()));
		PageLayoutV2 stack = new PageLayoutV2();
		Assert.False(LayoutCanvasMigration.IsCanvas(stack));
		Assert.False(LayoutCanvasMigration.ConvertInPlace(stack));
	}

	[Fact]
	public void EmptyCanvasPageBecomesAnEmptyGrid()
	{
		PageLayoutV2 page = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeCanvas,
			Cols = 5
		};
		Assert.True(LayoutCanvasMigration.ConvertInPlace(page));
		Assert.Equal(LayoutSchema.ModeGrid, page.Mode);
		Assert.Equal(LayoutSchema.SnapCols(5), page.Cols);
		Assert.Empty(page.Blocks);
	}

	[Fact]
	public void AppendedWidgetOnACanvasPageLandsLastInTheDerivedGrid()
	{
		Seed seed = SeedOf("hidden");
		PageLayoutV2 page = Parse(seed.Json).Layout.Pages[seed.PageId];
		BlockLayout appended = new BlockLayout { Id = "custom:w-new" };
		page.Blocks.Add(appended);

		LayoutEngine.PlaceAppendedBlock(page, appended);

		// 仍是小卡片(0.4 × 0.25,与旧语义一致),但推导出来排在页尾,而不是挤到页首
		Assert.Equal(0.4, appended.W);
		Assert.Equal(0.25, appended.H);
		PageLayoutV2 grid = LayoutCanvasMigration.ToGridPage(page)!;
		Assert.Equal("custom:w-new", grid.Blocks.Last((BlockLayout b) => !b.Hidden).Id);
		List<LayoutCell> cells = LayoutGrid.Pack(grid);
		Assert.Equal("custom:w-new", cells.OrderBy((LayoutCell c) => c.Y).ThenBy((LayoutCell c) => c.X).Last().Id);
	}
}
