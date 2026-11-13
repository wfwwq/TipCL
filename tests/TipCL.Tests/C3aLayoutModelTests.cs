using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C3a:布局模型层(<c>PageLayoutDoc</c> / <c>PageLayoutStore</c> / <c>DashCard</c> / <c>DashLayout</c>)
/// 里 **Windows 的 <c>Ported\PageLayoutTests</c> / <c>Ported\LayoutDefaultTests</c> 没覆盖到**的那几面:
/// 落盘文件的编码与原子性、缓存失效、导出/导入的边角、JSON 往返、Z 序重排。
///
/// 这些类型是纯模型(不引 Avalonia),所以本文件的用例体**不需要** <c>AvaloniaSession</c>;
/// 落盘一律走 <see cref="C3aHome"/> 注入的目录(红线:不碰用户真实数据目录)。
/// 期望值来源逐条写在用例上方。
/// </summary>
public class C3aLayoutModelTests
{
	/// <summary>
	/// 来源:Windows 源 <c>PageLayoutStore.ExportJson</c> 的信封
	/// <c>{"version":1,"kind":"tipcl-page-layout","pages":{...}}</c> +
	/// <c>WriteFile</c> 的 <c>new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)</c>。
	/// </summary>
	[Fact]
	public void Store_writes_the_documented_envelope_as_utf8_without_bom()
	{
		using var home = new C3aHome();
		PageLayoutStore.Save("home", PageLayoutDoc.Default(new[] { "a", "b" }, new[] { "a" }));

		byte[] bytes = File.ReadAllBytes(Path.Combine(home.Dir, "ui_page_layouts.json"));
		Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "不允许 BOM");

		// 说明:这里**不**断言"没有 CR 字节"。实测 .NET 8 的 Utf8JsonWriter 在打开
		// WriteIndented 时输出的换行是 CRLF(与 Windows 侧同一个 API、同一份序列化代码,
		// 所以那不是移植差异);红线里的 CR=0 针对的是**受版本控制的源文件**,不是运行时数据文件。
		string json = Encoding.UTF8.GetString(bytes);
		Assert.Contains("\"version\": 1", json, StringComparison.Ordinal);
		Assert.Contains("\"kind\": \"tipcl-page-layout\"", json, StringComparison.Ordinal);
		Assert.Contains("\"home\"", json, StringComparison.Ordinal);
		using JsonDocument parsed = JsonDocument.Parse(json);
		Assert.Equal("tipcl-page-layout", parsed.RootElement.GetProperty("kind").GetString());
	}

	/// <summary>
	/// 来源:Windows 源 <c>WriteFile</c> —— 先写 <c>path + ".tmp"</c> 再改名。
	/// 连存两次:文件内容是最新那份、目录里不留 <c>.tmp</c>。
	/// mac 差异:改名那一步是 <c>File.Move(overwrite: true)</c>(差异表 / D-116)。
	/// </summary>
	[Fact]
	public void Store_overwrites_atomically_and_never_leaves_a_tmp_file()
	{
		using var home = new C3aHome();
		PageLayoutStore.Save("home", PageLayoutDoc.Default(new[] { "a" }, new[] { "a" }));

		PageLayoutDoc second = PageLayoutDoc.Default(new[] { "a", "b" }, new[] { "b" });
		PageLayoutStore.Save("home", second);

		PageLayoutStore.ResetCache();
		PageLayoutDoc loaded = Assert.IsType<PageLayoutDoc>(PageLayoutStore.Load("home"));
		Assert.Equal(2, loaded.Blocks.Count);
		Assert.True(loaded.Blocks[0].Hidden);
		Assert.False(loaded.Blocks[1].Hidden);

		Assert.False(File.Exists(Path.Combine(home.Dir, "ui_page_layouts.json.tmp")), "不该留下 .tmp");
	}

	/// <summary>
	/// 来源:Windows 源 <c>Cache()</c> 的 <c>_cacheDir != dir</c> 判据 + mac 新增的
	/// <c>ResetCache</c>(本包注入缝,只为让测试能模拟"重启后重新读盘")。
	/// </summary>
	[Fact]
	public void Store_reads_back_from_disk_after_the_cache_is_dropped()
	{
		using var home = new C3aHome();
		PageLayoutStore.Save("home", PageLayoutDoc.Default(new[] { "a" }, new[] { "a" }));
		Assert.NotNull(PageLayoutStore.Load("home"));

		PageLayoutStore.ResetCache();

		Assert.NotNull(PageLayoutStore.Load("home"));
		Assert.Single(PageLayoutStore.LoadAll());
	}

	/// <summary>来源:Windows 源 <c>Save</c> 的 <c>Cache()[pageId] = doc; WriteFile(...)</c> —— 多页共存。</summary>
	[Fact]
	public void Store_keeps_every_page_side_by_side()
	{
		using var home = new C3aHome();
		PageLayoutStore.Save("launch", PageLayoutDoc.Default(new[] { "news" }, new[] { "news" }));
		PageLayoutStore.Save("settings", PageLayoutDoc.Default(new[] { "general" }, new[] { "general" }));

		PageLayoutStore.ResetCache();

		IReadOnlyDictionary<string, PageLayoutDoc> all = PageLayoutStore.LoadAll();
		Assert.Equal(2, all.Count);
		Assert.Equal("news", all["launch"].Blocks[0].Id);
		Assert.Equal("general", all["settings"].Blocks[0].Id);
	}

	/// <summary>来源:Windows 源 <c>DirPath</c> 里那句 <c>Directory.CreateDirectory(dir)</c>(目录按需创建)。</summary>
	[Fact]
	public void Store_creates_the_injected_directory_on_demand()
	{
		using var home = new C3aHome();
		string nested = Path.Combine(home.Dir, "deep", "deeper");
		string previous = PageLayoutStore.DirOverride!;
		try
		{
			PageLayoutStore.DirOverride = nested;
			PageLayoutStore.ResetCache();
			Assert.Equal(nested, PageLayoutStore.DirPath);
			Assert.True(Directory.Exists(nested), "DirPath 应顺手把目录建出来");
			PageLayoutStore.Save("p", PageLayoutDoc.Default(new[] { "a" }, new[] { "a" }));
			Assert.True(File.Exists(PageLayoutStore.FilePath));
		}
		finally
		{
			PageLayoutStore.DirOverride = previous;
			PageLayoutStore.ResetCache();
		}
	}

	/// <summary>
	/// 来源:Windows 源 <c>ExportJson</c> 的两条守卫 —— 空 pageId 与 null 文档都不进信封。
	/// </summary>
	[Fact]
	public void ExportJson_skips_empty_page_ids_and_null_documents()
	{
		List<KeyValuePair<string, PageLayoutDoc>> pages = new List<KeyValuePair<string, PageLayoutDoc>>
		{
			new KeyValuePair<string, PageLayoutDoc>("good", PageLayoutDoc.Default(new[] { "a" }, new[] { "a" })),
			new KeyValuePair<string, PageLayoutDoc>("", PageLayoutDoc.Default(new[] { "b" }, new[] { "b" })),
			new KeyValuePair<string, PageLayoutDoc>("nulled", null!)
		};

		string json = PageLayoutStore.ExportJson(pages);

		Assert.Contains("\"good\"", json, StringComparison.Ordinal);
		Assert.DoesNotContain("\"nulled\"", json, StringComparison.Ordinal);
		Assert.Single(PageLayoutStore.ParseImport(json));
	}

	/// <summary>
	/// 来源:Windows 源 <c>ParseImport</c> —— 解析失败 / kind 不对 / version 不对都抛
	/// <c>InvalidOperationException</c>(消息 I18n「布局文件无效」),null 与空串同样抛。
	/// </summary>
	[Fact]
	public void ParseImport_rejects_null_empty_and_garbage()
	{
		Assert.Throws<InvalidOperationException>(() => PageLayoutStore.ParseImport(null!));
		Assert.Throws<InvalidOperationException>(() => PageLayoutStore.ParseImport(""));
		Assert.Throws<InvalidOperationException>(() => PageLayoutStore.ParseImport("[]"));
		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
			() => PageLayoutStore.ParseImport("{\"version\":1,\"kind\":\"other\",\"pages\":{}}"));
		Assert.Equal(I18n.L("布局文件无效"), ex.Message);
	}

	/// <summary>
	/// 来源:Windows 源 <c>PageLayoutDoc.ToJson</c> 的
	/// <c>Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping</c>(中文不转义)。
	/// </summary>
	[Fact]
	public void PageLayoutDoc_ToJson_keeps_chinese_unescaped()
	{
		PageLayoutDoc doc = PageLayoutDoc.Default(new[] { "新闻", "任务" }, new[] { "新闻" });
		string json = doc.ToJson();

		Assert.Contains("新闻", json, StringComparison.Ordinal);
		Assert.Contains("任务", json, StringComparison.Ordinal);
		Assert.DoesNotContain("\\u", json, StringComparison.Ordinal);
		Assert.Contains("\"hidden\": true", json, StringComparison.Ordinal);
	}

	/// <summary>
	/// 来源:Windows 源 <c>DashLayout.ToJson</c> / <c>FromJson</c> —— 往返后:Grid 保住、
	/// 卡顺序按 Z 重排、坐标四舍五入到 5 位仍能读回、settings 只在是对象时保留。
	/// </summary>
	[Fact]
	public void DashLayout_json_round_trip_keeps_grid_cards_and_settings()
	{
		DashLayout layout = new DashLayout { Grid = 16 };
		layout.Items.Add(new DashCard { Id = "b", Type = "news", X = 0.125, Y = 0.25, W = 0.5, H = 0.75, Z = 3 });
		layout.Items.Add(new DashCard { Id = "a", Type = "config", X = 0.0, Y = 0.0, W = 0.42, H = 1.0, Z = 1 });

		string json = layout.ToJson();
		using JsonDocument doc = JsonDocument.Parse(json);
		DashLayout back = DashLayout.FromJson(doc.RootElement);

		Assert.Equal(16, back.Grid);
		Assert.Equal(2, back.Items.Count);
		// ToDict/FromJson 都只调 Normalize(= 按原 Z 把 Z 重排成 0..n-1),**不重排 Items 数组**;
		// 真正按 Z 排序的是 VisibleItems。所以数组顺序仍是写入时的 [b, a],Z 变成 b=1 / a=0。
		Assert.Equal("b", back.Items[0].Id);
		Assert.Equal(1, back.Items[0].Z);
		Assert.Equal("a", back.Items[1].Id);
		Assert.Equal(0, back.Items[1].Z);
		Assert.Equal(0.125, back.Items[0].X, 6);
		Assert.Equal(0.25, back.Items[0].Y, 6);
		Assert.Equal(0.5, back.Items[0].W, 6);
		Assert.Equal(0.75, back.Items[0].H, 6);

		List<string> visible = new List<string>();
		foreach (DashCard card in back.VisibleItems())
		{
			visible.Add(card.Id);
		}
		Assert.Equal(new[] { "a", "b" }, visible);
	}

	/// <summary>
	/// 来源:Windows 源 <c>DashLayout.Normalize</c> —— 缺 id 的用 <c>type-序号</c> 补、
	/// 重名的加 <c>-2 / -3</c> 后缀、最后按 Z 重排成 0..n-1。
	/// </summary>
	[Fact]
	public void DashLayout_Normalize_fills_missing_ids_dedups_and_reindexes_z()
	{
		DashLayout layout = new DashLayout();
		layout.Items.Add(new DashCard { Type = "news", Z = 5 });
		layout.Items.Add(new DashCard { Type = "news", Z = 2 });
		layout.Items.Add(new DashCard { Id = "news-0", Type = "news", Z = 9 });

		layout.Normalize();

		Assert.Equal("news-0", layout.Items[0].Id);
		Assert.Equal("news-1", layout.Items[1].Id);
		Assert.Equal("news-0-2", layout.Items[2].Id);
		// Z 按原 Z 重排成 0..n-1:原 Z 是 5 / 2 / 9,对应 Items[0] / Items[1] / Items[2]
		Assert.Equal(1, layout.Items[0].Z);
		Assert.Equal(0, layout.Items[1].Z);
		Assert.Equal(2, layout.Items[2].Z);
	}

	/// <summary>
	/// 来源:Windows 源 <c>DashLayout.Parse</c> —— 根不是对象 / items 不是数组 / 全都不是已知类型 →
	/// null;已知类型保留并按 Z 排序。
	/// </summary>
	[Fact]
	public void DashLayout_Parse_returns_null_unless_at_least_one_known_type_survives()
	{
		using JsonDocument notObject = JsonDocument.Parse("5");
		Assert.Null(DashLayout.Parse(notObject.RootElement, new[] { "news" }));

		using JsonDocument noItems = JsonDocument.Parse("{\"grid\":8}");
		Assert.Null(DashLayout.Parse(noItems.RootElement, new[] { "news" }));

		using JsonDocument onlyUnknown = JsonDocument.Parse(
			"{\"grid\":8,\"items\":[{\"type\":\"nope\",\"x\":0,\"y\":0,\"w\":0.3,\"h\":0.3,\"z\":0}]}");
		Assert.Null(DashLayout.Parse(onlyUnknown.RootElement, new[] { "news" }));

		using JsonDocument mixed = JsonDocument.Parse(
			"{\"grid\":8,\"items\":["
			+ "{\"type\":\"nope\",\"z\":0},"
			+ "{\"type\":\"news\",\"x\":0.1,\"y\":0.2,\"w\":0.3,\"h\":0.4,\"z\":2}]}");
		DashLayout parsed = Assert.IsType<DashLayout>(DashLayout.Parse(mixed.RootElement, new[] { "news" }));
		Assert.Single(parsed.Items);
		Assert.Equal("news", parsed.Items[0].Type);
		Assert.Equal(0.1, parsed.Items[0].X, 6);
	}

	/// <summary>
	/// 来源:Windows 源 <c>DashCard.FromJson</c> —— x/y 钳到 [0,1]、w/h 钳到 [0.04,1]、
	/// 缺 type 补 <c>notes</c>、hidden 只认布尔 true、数字字符串也认。
	/// </summary>
	[Fact]
	public void DashCard_FromJson_clamps_ranges_and_defaults_the_type()
	{
		using JsonDocument doc = JsonDocument.Parse(
			"{\"x\":2.5,\"y\":-1,\"w\":0.001,\"h\":5,\"z\":\"3\",\"hidden\":\"yes\"}");
		DashCard card = DashCard.FromJson(doc.RootElement);

		Assert.Equal("", card.Id);
		Assert.Equal("notes", card.Type);
		Assert.Equal(1.0, card.X, 6);
		Assert.Equal(0.0, card.Y, 6);
		Assert.Equal(0.04, card.W, 6);
		Assert.Equal(1.0, card.H, 6);
		Assert.Equal(3, card.Z);
		Assert.False(card.Hidden);

		using JsonDocument hidden = JsonDocument.Parse("{\"hidden\":true}");
		Assert.True(DashCard.FromJson(hidden.RootElement).Hidden);
	}

	/// <summary>来源:Windows 源 <c>DashLayout.VisibleItems</c>(未隐藏的按 Z 升序)与 <c>NextZ</c>。</summary>
	[Fact]
	public void DashLayout_visible_items_are_ordered_by_z_and_next_z_follows_the_maximum()
	{
		DashLayout layout = new DashLayout();
		Assert.Equal(0, layout.NextZ());

		layout.Items.Add(new DashCard { Id = "a", Type = "a", Z = 7 });
		layout.Items.Add(new DashCard { Id = "b", Type = "b", Z = 2 });
		layout.Items.Add(new DashCard { Id = "c", Type = "c", Z = 5, Hidden = true });

		Assert.Equal(8, layout.NextZ());
		List<string> visible = new List<string>();
		foreach (DashCard card in layout.VisibleItems())
		{
			visible.Add(card.Id);
		}
		Assert.Equal(new[] { "b", "a" }, visible);
	}

	/// <summary>
	/// 来源:Windows 源 <c>DashLayout.Default()</c> 的注释 —— 必须与 Core 的
	/// <c>UiLayout.DefaultDoc()</c> 完全一致(前端口径同源的第三个钉子;
	/// Windows 侧由 <c>LayoutDefaultTests</c> 检查,mac 侧由本包把它恢复编译后同样检查)。
	/// </summary>
	[Fact]
	public void DashLayout_Default_matches_the_core_ui_layout_default()
	{
		DashLayout ui = DashLayout.Default();
		// 用 var:LayoutDoc / LayoutItem 是 Core 里的类型,这里只想比字段值,不引它们的类型名
		var core = TipCL.Core.Launcher.UiLayout.DefaultDoc();

		Assert.Equal(core.items.Count, ui.Items.Count);
		for (int i = 0; i < core.items.Count; i++)
		{
			Assert.Equal(core.items[i].type, ui.Items[i].Type);
			Assert.Equal(core.items[i].id, ui.Items[i].Id);
			Assert.Equal((double)core.items[i].x, ui.Items[i].X, 6);
			Assert.Equal((double)core.items[i].y, ui.Items[i].Y, 6);
			Assert.Equal((double)core.items[i].w, ui.Items[i].W, 6);
			Assert.Equal((double)core.items[i].h, ui.Items[i].H, 6);
		}
		Assert.Equal(8, ui.Grid);
	}
}