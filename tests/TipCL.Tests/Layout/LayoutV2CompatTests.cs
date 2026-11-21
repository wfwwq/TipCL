// 来源: Windows 版参考仓库 tests/TipCL.Tests/LayoutV2CompatTests.cs(分支 layout-shell-hooks @ fa1b3d7,R32-LC 随布局 v2 模型层移植)。
// L0 兼容性回归闸门:逐条对应 docs/layout-v2-design.md §3.3 的"可测断言"。
//   行 1 只有 v1 → 迁移逐项保留         → OnlyV1MigratesWithExactBlocksAndOrder / MigrationWritesV2OnlyAndLeavesV1ByteIdentical
//   行 2 v1 与 v2 都在 → v2 优先         → V1AndV2BothPresentPrefersV2
//   行 3 改 v2 → 写通 v1,冻结解析器全等  → SaveWritesProjectionThatFrozenV1ParserReadsBackIdentically / SavedV1FileHasOnlyFrozenFields
//   行 4 v2 损坏 → 回落 v1 子集(再不行回默认) → CorruptV2FallsBackToV1SubsetWithoutThrowing / BothFilesCorruptFallsBackToDefault
//   行 5 旧版重写 v1 后 v2 新字段不丢     → OldProgramRewritingV1DoesNotLoseV2Fields
//   行 6 分享/导入校验                   → ImportRejectsWrongKindOrVersionWithChineseMessage
//   事件(D5)与写盘失败不丢内存           → LayoutChangedFiresOnSave / SaveSwallowsDiskFailureAndKeepsLatestInMemory
//
// 移植差异(Linux 侧没有 Pages 层,注入缝的等价物见本文件末尾的 LinuxV1Bridge):
//   1) Windows 用 PagesLayoutV1Bridge + PageLayoutStore(带缓存)+ LayoutV1Projection.ProjectV1;
//      Linux 树没有 Pages,改用 TipCL.Core.Layout 的 PageLayoutStore + 本文件实现的
//      LinuxV1Bridge(在 LayoutV1Bridge.cs 的公共契约上做纯搬运,不引入新语义);
//   2) 断言的 v1 解析器从 Windows 的 PageLayoutDoc 换成 LayoutV1Json(冻结 v1 文件的 Core 自带解析器,
//      语义逐字对齐:同一份 v1 文件、同样的 kind/version 门禁与 {id,hidden} 逐项保留);
//   3) 无法直接搬的 1 条用例(只有 v1 时的迁移)在用例内额外钉一次"文件→LayoutV1Json 读回"的逐项全等,
//      用 Core 的解析器补上 Windows 里由 PageLayoutStore 承担的那半段证据。
// 所有落盘测试都走 DirOverride 临时目录,绝不碰用户真实数据目录。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;
using TipCL.Core.Layout;
using Xunit;

/// <summary>
/// L0 兼容性回归闸门(§3.3 的"可测断言"):v1 ↔ v2 迁移 / 写通 / 回落 / 导入校验。
/// </summary>
[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class LayoutV2CompatTests
{
	private readonly string _tmp = Path.Combine(Path.GetTempPath(), "tipcl_layoutv2_tests_" + Guid.NewGuid().ToString("N"));

	private string V1Path(string dir)
	{
		return Path.Combine(dir, "ui_page_layouts.json");
	}

	private string V2Path(string dir)
	{
		return Path.Combine(dir, "ui_layout_v2.json");
	}

	// 两个存储必须指到同一目录:否则 v2 的写通会落到用户真实数据目录
	private void UseDir(string dir)
	{
		LayoutStoreV2.DirOverride = dir;
		LayoutStoreV2.V1Bridge = new LinuxV1Bridge();   // 与 App 启动同装配:v1 走 PageLayoutStore 语义
		PageLayoutStore.DirOverride = dir;
		LayoutStoreV2.ResetCache();
	}

	private void Release()
	{
		LayoutStoreV2.DirOverride = null;
		LayoutStoreV2.V1Bridge = null;
		PageLayoutStore.DirOverride = null;
		LayoutStoreV2.ResetCache();
		try
		{
			Directory.Delete(_tmp, true);
		}
		catch (Exception)
		{
		}
	}

	// ————— §3.3 行 1:只有 v1,没有 v2(升级首跑) —————

	[Fact]
	public void OnlyV1MigratesWithExactBlocksAndOrder()
	{
		UseDir(_tmp);
		try
		{
			PageLayoutDoc launch = PageLayoutDoc.Default(new[] { "hero", "quick", "news" }, new[] { "hero", "news" });
			launch.Blocks[1].Hidden = true;
			PageLayoutStore.Save("pages:launch", launch);

			PageLayoutDoc network = PageLayoutDoc.Default(new[] { "proxy", "dns" }, new[] { "dns" });
			PageLayoutStore.Save("settings:network", network);

			Assert.False(File.Exists(V2Path(_tmp))); // 升级首跑:还没有 v2

			LayoutV2 doc = LayoutStoreV2.Load();
			Assert.Equal("v1-fallback", LayoutStoreV2.LastLoadSource);
			Assert.Equal(2, doc.Layout.Pages.Count);

			// 每个 v1 page 的 blocks 顺序与 {id,hidden} 逐项相等
			AssertV1Equals(launch, doc.Layout.Pages["pages:launch"]);
			AssertV1Equals(network, doc.Layout.Pages["settings:network"]);

			// 磁盘上那份 v1 文件,交给 Core 自带的冻结解析器再读一遍:块顺序与显隐同样逐项全等
			// (LayoutStoreV2 走桥读到的是 PageLayoutStore 的缓存,这里补验"文件本身"的形态)
			IReadOnlyDictionary<string, IReadOnlyList<LayoutV1Block>>? fromFile = LayoutV1Json.ReadPages(V1Path(_tmp));
			Assert.NotNull(fromFile);
			AssertV1Equals(launch, fromFile!["pages:launch"]);
			AssertV1Equals(network, fromFile!["settings:network"]);

			// 新增字段一律取默认(mode=stack、cols=12、gap=10、style=plain、title=false)
			PageLayoutV2 page = doc.Layout.Pages["pages:launch"];
			Assert.Equal(2, page.Version);
			Assert.Equal("stack", page.Mode);
			Assert.Equal(12, page.Cols);
			Assert.Equal(0.0, page.RowH);
			Assert.Equal(10.0, page.Gap);
			Assert.Equal(new[] { 18.0, 14.0, 18.0, 16.0 }, page.Pad);
			foreach (BlockLayout block in page.Blocks)
			{
				Assert.Equal("plain", block.Style);
				Assert.False(block.Title);
				Assert.Equal(0.0, block.MinH);
			}
			Assert.False(File.Exists(V2Path(_tmp))); // Load 只读不写
		}
		finally
		{
			Release();
		}
	}

	// ————— §3.3 行 1 + D3:迁移落盘只写 v2,v1 一个字节都不动 —————

	[Fact]
	public void MigrationWritesV2OnlyAndLeavesV1ByteIdentical()
	{
		UseDir(_tmp);
		try
		{
			PageLayoutDoc legacy = PageLayoutDoc.Default(new[] { "b", "a" }, new[] { "a" });
			legacy.Blocks[0].Hidden = true;
			PageLayoutStore.Save("pages:launch", legacy);
			string v1Before = File.ReadAllText(V1Path(_tmp));

			LayoutV2 migrated = LayoutStoreV2.LoadMigrating();
			Assert.Equal("v1-migrated", LayoutStoreV2.LastLoadSource);
			Assert.True(File.Exists(V2Path(_tmp)));
			Assert.Equal(v1Before, File.ReadAllText(V1Path(_tmp))); // v1 原样保留(安全网)

			// 迁移结果落盘后,v2 与 v1 的投影仍然全等
			LayoutStoreV2.ResetCache();
			LayoutV2 after = LayoutStoreV2.Load();
			Assert.Equal("v2", LayoutStoreV2.LastLoadSource);
			AssertV1Equals(legacy, after.Layout.Pages["pages:launch"]);
		}
		finally
		{
			Release();
		}
	}

	// ————— §3.3 行 3:任何一次 v2 保存后,冻结的 v1 解析器读回与 v2 投影全等 —————

	[Fact]
	public void SaveWritesProjectionThatFrozenV1ParserReadsBackIdentically()
	{
		UseDir(_tmp);
		try
		{
			LayoutV2 doc = LayoutStoreV2.ParseImport("""
			{
			  "version": 2,
			  "kind": "tipcl-layout",
			  "layout": {
			    "pages": {
			      "pages:launch": {
			        "version": 2,
			        "mode": "grid",
			        "cols": 6,
			        "blocks": [
			          { "id": "news", "style": "card", "title": true, "x": 3, "w": 3 },
			          { "id": "hero", "hidden": true, "span": 2 },
			          { "id": "quick", "z": 2 }
			        ]
			      },
			      "settings:about": { "version": 2, "blocks": [{ "id": "version" }] }
			    }
			  }
			}
			""");
			LayoutStoreV2.Save(doc);

			Assert.True(File.Exists(V2Path(_tmp)));
			Assert.True(File.Exists(V1Path(_tmp)));

			// 冻结的 v1 解析器(Linux 侧 = LayoutV1Json.ReadPages)读回磁盘上那份 v1 文件
			IReadOnlyDictionary<string, IReadOnlyList<LayoutV1Block>>? loaded = LayoutV1Json.ReadPages(V1Path(_tmp));
			Assert.NotNull(loaded);
			Assert.Equal(new[] { "news", "hero", "quick" }, loaded!["pages:launch"].Select((LayoutV1Block b) => b.Id).ToArray());
			Assert.Equal(new[] { false, true, false }, loaded["pages:launch"].Select((LayoutV1Block b) => b.Hidden).ToArray());
			Assert.Equal(new[] { "version" }, loaded["settings:about"].Select((LayoutV1Block b) => b.Id).ToArray());

			// 与 v2 投影逐页全等
			Dictionary<string, IReadOnlyList<LayoutV1Block>> projection = LayoutStoreV2.ProjectV1(LayoutStoreV2.Load());
			Assert.Equal(projection.Count, loaded.Count);
			foreach (KeyValuePair<string, IReadOnlyList<LayoutV1Block>> kv in projection)
			{
				Assert.True(loaded.ContainsKey(kv.Key), kv.Key);
				Assert.Equal(
					kv.Value.Select((LayoutV1Block b) => b.Id + "|" + b.Hidden).ToArray(),
					loaded[kv.Key].Select((LayoutV1Block b) => b.Id + "|" + b.Hidden).ToArray());
			}
		}
		finally
		{
			Release();
		}
	}

	// ————— D1:v1 文件语义冻结(只有 version/kind/pages[id].{version,blocks[{id,hidden}]}) —————

	[Fact]
	public void SavedV1FileHasOnlyFrozenFields()
	{
		UseDir(_tmp);
		try
		{
			LayoutV2 doc = LayoutStoreV2.ParseImport("""
			{
			  "version": 2,
			  "kind": "tipcl-layout",
			  "saved_at": "2026-11-14T09:10:00+08:00",
			  "app": "1.1.0",
			  "active_profile": "我的布局",
			  "layout": {
			    "shell": { "density": "loose", "tabs": ["launch"] },
			    "pages": {
			      "pages:launch": {
			        "version": 2,
			        "mode": "canvas",
			        "cols": 4,
			        "row_h": 80,
			        "gap": 12,
			        "pad": [1, 2, 3, 4],
			        "blocks": [
			          { "id": "hero", "x": 0.2, "y": 0.3, "w": 0.4, "h": 0.4, "span": 2, "style": "card", "title": true, "pad": [5, 5, 5, 5], "min_h": 90, "z": 0, "settings": { "k": "v" } },
			          { "id": "news", "hidden": true }
			        ]
			      }
			    },
			    "splits": { "pages:launch": { "main": 0.4 } },
			    "widgets": [{ "id": "custom-1", "type": "note", "title": "便签" }],
			    "variants": { "narrow": { "pages": {} } },
			    "meta": { "theme": "dark" }
			  },
			  "profiles": { "我的布局": { "pages": {} } }
			}
			""");
			LayoutStoreV2.Save(doc);

			string text = File.ReadAllText(V1Path(_tmp));
			// 先证明"这些字段确实存在于同一份文档的 v2 侧"(否则下面的"不许出现"是空断言)
			string v2Text = File.ReadAllText(V2Path(_tmp));
			foreach (string must in new[] { "mode", "style", "title", "span", "min_h", "settings", "row_h", "splits", "widgets", "variants", "profiles", "active_profile", "saved_at" })
			{
				Assert.Contains("\"" + must + "\"", v2Text);
			}
			// v1 侧:v2 专属字段名一个都不许出现
			foreach (string forbidden in new[]
			{
				"mode", "style", "title", "span", "min_h", "settings", "cols", "row_h", "gap", "pad",
				"x", "y", "w", "h", "splits", "widgets", "variants", "meta", "profiles", "shell",
				"saved_at", "app", "active_profile"
			})
			{
				Assert.DoesNotContain("\"" + forbidden + "\"", text);
			}

			using JsonDocument json = JsonDocument.Parse(text);
			JsonElement root = json.RootElement;
			Assert.Equal(new[] { "version", "kind", "pages" }, root.EnumerateObject().Select((JsonProperty p) => p.Name).ToArray());
			Assert.Equal(1, root.GetProperty("version").GetInt32());
			Assert.Equal("tipcl-page-layout", root.GetProperty("kind").GetString());
			JsonElement pages = root.GetProperty("pages");
			Assert.Single(pages.EnumerateObject());
			foreach (JsonProperty page in pages.EnumerateObject())
			{
				Assert.Equal(new[] { "version", "blocks" }, page.Value.EnumerateObject().Select((JsonProperty p) => p.Name).ToArray());
				Assert.Equal(1, page.Value.GetProperty("version").GetInt32());
				foreach (JsonElement block in page.Value.GetProperty("blocks").EnumerateArray())
				{
					Assert.Equal(new[] { "id", "hidden" }, block.EnumerateObject().Select((JsonProperty p) => p.Name).ToArray());
				}
			}
		}
		finally
		{
			Release();
		}
	}

	// ————— §3.3 行 5:旧版本程序重写 v1 之后,v2 里的新字段仍在 —————

	[Fact]
	public void OldProgramRewritingV1DoesNotLoseV2Fields()
	{
		UseDir(_tmp);
		try
		{
			LayoutV2 doc = LayoutStoreV2.ParseImport("""
			{
			  "version": 2,
			  "kind": "tipcl-layout",
			  "layout": {
			    "pages": {
			      "pages:launch": {
			        "version": 2,
			        "mode": "grid",
			        "cols": 6,
			        "row_h": 90,
			        "gap": 14,
			        "blocks": [
			          { "id": "hero", "style": "card", "title": true, "x": 0, "w": 6, "settings": { "k": "v" } }
			        ]
			      }
			    },
			    "splits": { "pages:library": { "folders": 0.3 } },
			    "widgets": [{ "id": "custom-1", "type": "clock", "title": "时钟" }],
			    "variants": { "narrow": { "pages": {} } }
			  },
			  "profiles": { "我的布局": { "pages": {} } }
			}
			""");
			LayoutStoreV2.Save(doc);

			// 模拟"旧版本程序"重写 v1(它只会写 v1 语义:顺序 + 显隐),并把顺序整个改掉、还多出一个页面
			PageLayoutDoc legacy = PageLayoutDoc.Default(new[] { "hero", "added" }, new[] { "added" });
			legacy.Blocks[0].Hidden = true;
			PageLayoutStore.Save("pages:launch", legacy);
			PageLayoutStore.Save("legacy-only", PageLayoutDoc.Default(new[] { "only" }, new[] { "only" }));

			// 新版重新从磁盘读:仍然以 v2 为准,新字段一个不丢
			LayoutStoreV2.ResetCache();
			LayoutV2 after = LayoutStoreV2.Load();
			Assert.Equal("v2", LayoutStoreV2.LastLoadSource);
			PageLayoutV2 page = after.Layout.Pages["pages:launch"];
			Assert.Equal("grid", page.Mode);
			Assert.Equal(6, page.Cols);
			Assert.Equal(90.0, page.RowH);
			Assert.Equal(14.0, page.Gap);
			Assert.Equal("card", page.Blocks[0].Style);
			Assert.True(page.Blocks[0].Title);
			Assert.Equal(6.0, page.Blocks[0].W);
			Assert.Equal("v", page.Blocks[0].Settings["k"]);
			Assert.Equal(0.3, after.Layout.Splits["pages:library"].Get("folders", 0.0));
			Assert.Equal("clock", Assert.Single(after.Layout.Widgets).Type);
			Assert.True(after.Layout.Variants.ContainsKey("narrow"));
			Assert.True(after.Profiles.ContainsKey("我的布局"));
			Assert.False(after.Layout.Pages["pages:launch"].Blocks[0].Hidden); // v2 的显隐不被 v1 覆盖

			// 读 v2 不改写盘:旧版写回的那份 v1 内容原样在
			Assert.Contains("legacy-only", File.ReadAllText(V1Path(_tmp)));
		}
		finally
		{
			Release();
		}
	}

	// ————— §3.3 行 2:v1 与 v2 都在 → v2 优先 —————

	[Fact]
	public void V1AndV2BothPresentPrefersV2()
	{
		UseDir(_tmp);
		try
		{
			LayoutV2 doc = LayoutStoreV2.ParseImport("""
			{ "version": 2, "kind": "tipcl-layout", "layout": { "pages": { "p": { "version": 2, "mode": "grid", "blocks": [{ "id": "a" }, { "id": "b" }] } } } }
			""");
			LayoutStoreV2.Save(doc);

			// 直接改 v1 文件(模拟外部工具动了旧格式)
			File.WriteAllText(V1Path(_tmp), PageLayoutStore.ExportJson(new[]
			{
				new KeyValuePair<string, PageLayoutDoc>("p", PageLayoutDoc.Default(new[] { "b", "a" }, new[] { "a" }))
			}));

			LayoutStoreV2.ResetCache();
			LayoutV2 back = LayoutStoreV2.Load();
			Assert.Equal("v2", LayoutStoreV2.LastLoadSource);
			Assert.Equal("grid", back.Layout.Pages["p"].Mode); // v2 优先,不因 v1 变更新鲜而回退
			Assert.Equal(new[] { "a", "b" }, back.Layout.Pages["p"].Blocks.Select((BlockLayout b) => b.Id).ToArray());

			// 需要把 v1 拉回与 v2 一致时,显式写通一次
			LayoutStoreV2.WriteThroughV1();
			IReadOnlyDictionary<string, IReadOnlyList<LayoutV1Block>>? v1 = LayoutV1Json.ReadPages(V1Path(_tmp));
			Assert.Equal(new[] { "a", "b" }, v1!["p"].Select((LayoutV1Block b) => b.Id).ToArray());
		}
		finally
		{
			Release();
		}
	}

	// ————— §3.3 行 4:v2 损坏 → 回落 v1 子集,不抛、不清空用户数据 —————

	[Fact]
	public void CorruptV2FallsBackToV1SubsetWithoutThrowing()
	{
		UseDir(_tmp);
		try
		{
			PageLayoutDoc legacy = PageLayoutDoc.Default(new[] { "hero", "news" }, new[] { "news" });
			PageLayoutStore.Save("pages:launch", legacy);

			LayoutStoreV2.Save(LayoutStoreV2.ParseImport("""
			{ "version": 2, "kind": "tipcl-layout", "layout": { "pages": { "p": { "version": 2, "blocks": [{ "id": "a" }] } } } }
			"""));
			string good = File.ReadAllText(V2Path(_tmp));

			string[] damaged = new[]
			{
				good.Substring(0, Math.Max(1, good.Length / 2)), // 截断
				"not json at all {{{",
				"",
				"{\"version\":2,\"kind\":\"something-else\",\"layout\":{}}", // kind 不对
				"{\"version\":1,\"kind\":\"tipcl-layout\",\"layout\":{}}", // version 不对
				"{\"version\":3,\"kind\":\"tipcl-layout\",\"layout\":{}}",
				"[1,2,3]",
				"{\"version\":2,\"kind\":\"tipcl-layout\",\"layout\":{\"pages\":" // 半截对象
			};
			foreach (string text in damaged)
			{
				File.WriteAllText(V2Path(_tmp), text); // 外部损坏模拟:直接写坏文件
				LayoutStoreV2.ResetCache();
				LayoutV2 doc = LayoutStoreV2.Load(); // 绝不能抛
				Assert.Equal("v1-fallback", LayoutStoreV2.LastLoadSource);
				AssertV1Equals(legacy, doc.Layout.Pages["pages:launch"]);
				Assert.Equal(text, File.ReadAllText(V2Path(_tmp))); // 读路径不写盘,损坏文件不被"顺手清掉"
				Assert.Equal(legacy.Blocks.Count, doc.Layout.Pages["pages:launch"].Blocks.Count);
			}
		}
		finally
		{
			Release();
		}
	}

	// ————— §3.3 行 4 的后半:v1 也读不到 → 回默认文档,不抛 —————

	[Fact]
	public void BothFilesCorruptFallsBackToDefault()
	{
		string dir = Path.Combine(_tmp, "both-bad");
		UseDir(dir);
		try
		{
			Directory.CreateDirectory(dir);
			File.WriteAllText(V2Path(dir), "{ half");
			File.WriteAllText(V1Path(dir), "{ not a layout");

			LayoutV2 doc = LayoutStoreV2.Load();
			Assert.Equal("default", LayoutStoreV2.LastLoadSource);
			Assert.Empty(doc.Layout.Pages);
			Assert.Equal(2, doc.Version);
			Assert.Equal(new[] { "launch", "library", "download", "plugins", "settings" }, doc.Layout.Shell.Tabs.ToArray());
		}
		finally
		{
			Release();
		}
	}

	// ————— §3.3 行 6:导入校验 —————

	[Fact]
	public void ImportRejectsWrongKindOrVersionWithChineseMessage()
	{
		using (JsonDocument parsed = JsonDocument.Parse(LayoutStoreV2.ExportJson(LayoutV2.CreateDefault())))
		{
			Assert.Equal("tipcl-layout", parsed.RootElement.GetProperty("kind").GetString());
			Assert.Equal(2, parsed.RootElement.GetProperty("version").GetInt32());
		}
		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => LayoutStoreV2.ParseImport(
			"{\"version\":1,\"kind\":\"tipcl-page-layout\",\"pages\":{}}"));
		Assert.Contains("布局文件无效", ex.Message);
		Assert.Throws<InvalidOperationException>(() => LayoutStoreV2.ParseImport("{\"version\":2,\"kind\":\"tipcl-layout-v3\"}"));
		Assert.Throws<InvalidOperationException>(() => LayoutStoreV2.ParseImport("完全不是 JSON"));
	}

	// ————— 事件(D5)与"写盘失败也不能丢内存" —————

	[Fact]
	public void LayoutChangedFiresOnSave()
	{
		UseDir(_tmp);
		try
		{
			int fired = 0;
			Action handler = delegate
			{
				fired++;
			};
			LayoutStoreV2.LayoutChanged += handler;
			try
			{
				// L2 冻结的 Tier-1 契约:Save 返回"v2 文件是否写成功",正常路径必须为 true
				bool written = LayoutStoreV2.Save(LayoutStoreV2.ParseImport("""
				{ "version": 2, "kind": "tipcl-layout", "layout": { "pages": { "p": { "version": 2, "blocks": [{ "id": "a" }] } } } }
				"""));
				Assert.True(written);
			}
			finally
			{
				LayoutStoreV2.LayoutChanged -= handler;
			}
			Assert.Equal(1, fired);
		}
		finally
		{
			Release();
		}
	}

	[Fact]
	public void SaveSwallowsDiskFailureAndKeepsLatestInMemory()
	{
		string blocker = Path.Combine(_tmp, "blocker");
		Directory.CreateDirectory(_tmp);
		File.WriteAllText(blocker, "x"); // 用文件占位,让目录创建/写入必然失败
		UseDir(Path.Combine(blocker, "sub"));
		try
		{
			LayoutV2 doc = LayoutStoreV2.ParseImport("""
			{ "version": 2, "kind": "tipcl-layout", "layout": { "pages": { "p": { "version": 2, "mode": "grid", "blocks": [{ "id": "a" }] } } } }
			""");
			LayoutStoreV2.Save(doc); // 不抛
			// L2 冻结的 Tier-1 契约:写盘失败返回 false(调用方据此提示"重启后会丢失"),内存里仍保留最新值
			Assert.False(LayoutStoreV2.Save(doc));
			LayoutV2 back = LayoutStoreV2.Load();
			Assert.Equal("grid", back.Layout.Pages["p"].Mode); // 内存里保留最新值
			Assert.False(File.Exists(Path.Combine(blocker, "sub", "ui_layout_v2.json")));
		}
		finally
		{
			Release();
		}
	}

	// ————— 断言助手 —————

	// 冻结的 v1 文档 ↔ v2 页布局:{id,hidden} 逐项 + 顺序全等
	private static void AssertV1Equals(PageLayoutDoc v1, PageLayoutV2 page)
	{
		Assert.Equal(v1.Blocks.Count, page.Blocks.Count);
		for (int i = 0; i < v1.Blocks.Count; i++)
		{
			Assert.Equal(v1.Blocks[i].Id, page.Blocks[i].Id);
			Assert.Equal(v1.Blocks[i].Hidden, page.Blocks[i].Hidden);
		}
	}

	// Core 的朴素 v1 结构 ↔ v2 页布局:LINUX 侧替代 LayoutV1Projection 的断言路径
	private static void AssertV1Equals(PageLayoutDoc v1, IReadOnlyList<LayoutV1Block> blocks)
	{
		Assert.Equal(v1.Blocks.Count, blocks.Count);
		for (int i = 0; i < v1.Blocks.Count; i++)
		{
			Assert.Equal(v1.Blocks[i].Id, blocks[i].Id);
			Assert.Equal(v1.Blocks[i].Hidden, blocks[i].Hidden);
		}
	}
}
