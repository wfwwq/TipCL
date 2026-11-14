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
/// L1 宿主侧接线测试(BlockHost 内部逻辑的纯模型部分)。
/// BlockHost 是 WPF 控件(测试宿主是 net8,不能实例化),所以这里测它**委托出去**的那一层:
///   LayoutEditor 的装载/提交路径 = LayoutStoreV2.Load → LayoutPageState → LayoutEngine.ApplyPage → LayoutStoreV2.Save
///   + PageLayoutStore.Reload();
/// 逐条对应 L1 硬性要求:
///   1) 某 pageId 没有 v2 条目 → 默认顺序等价 PageLayoutDoc.Default(12 个真实宿主逐个对照);
///   2) 提交一次 = 写 v2 + 由 v2 写通 v1,冻结 v1 解析器回读全等;
///   3) 写完后 PageLayoutStore.Reload() 让老 v1 路径立刻看到新值(L0 报告的坑 3);
///   4) 提交只影响本页,别的页面的 v2 专属字段一个不丢。
/// 视觉层面的"首屏逐像素等价"由 --shot 前后对照证据覆盖(见交付报告),单测不替代它。
/// </summary>
public class LayoutBlockHostTests
{
	private readonly string _tmp = Path.Combine(Path.GetTempPath(), "tipcl_l1host_tests_" + Guid.NewGuid().ToString("N"));

	private void UseDir()
	{
		LayoutStoreV2.DirOverride = _tmp;
		LayoutStoreV2.V1Bridge = new PagesLayoutV1Bridge();   // 与 App.OnStartup 同装配:v1 走 PageLayoutStore 语义
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

	private string V1Path()
	{
		return Path.Combine(_tmp, "ui_page_layouts.json");
	}

	private string V2Path()
	{
		return Path.Combine(_tmp, "ui_layout_v2.json");
	}

	// ————— 1) 12 个真实宿主的默认布局 = 冻结 v1 语义 —————

	// 表格 = 逐个从源码里抄下来的页面白名单(src/TipCL/Pages/*.cs 的 new BlockHost(pageId, ...) 站点)
	public static IEnumerable<object[]> HostTable()
	{
		yield return new object[] { "launch", "news,tasks,playtime,notes,log", "news" };
		yield return new object[] { "tasks", "header,list", "header,list" };
		yield return new object[] { "download", "filters,list", "filters,list" };
		yield return new object[] { "multiplayer", "header,state,room,actions,players,nodes", "header,state,room,actions,players,nodes" };
		yield return new object[] { "servers", "header,instance,list", "header,instance,list" };
		yield return new object[] { "java", "intro,install,defaults,list", "intro,install,defaults,list" };
		yield return new object[] { "playtime", "header,total,range,list", "header,total,range,list" };
		yield return new object[] { "account", "header,list", "header,list" };
		yield return new object[] { "feedback", "header,content", "header,content" };
		yield return new object[] { "settings:general", "appearance,wallpaper,themePack,launcher,game,storage,ai,memory,phoneSync", "appearance,wallpaper,themePack,launcher,game,storage,ai,memory,phoneSync" };
		yield return new object[] { "settings:network", "network", "network" };
		yield return new object[] { "settings:about", "about", "about" };
	}

	[Theory]
	[MemberData(nameof(HostTable))]
	public void DefaultLayoutOfEveryRealHostEqualsFrozenV1Semantics(string pageId, string idsCsv, string visibleCsv)
	{
		string[] ids = idsCsv.Split(',');
		List<LayoutBlockRef> refs = new List<LayoutBlockRef>();
		HashSet<string> visible = new HashSet<string>(visibleCsv.Split(','), StringComparer.Ordinal);
		foreach (string id in ids)
		{
			refs.Add(new LayoutBlockRef(id, visible.Contains(id)));
		}
		// 没有 v2 条目 → 用白名单生成默认布局
		LayoutPageState state = LayoutEngine.LoadState(pageId, null, refs);
		PageLayoutDoc frozen = PageLayoutDoc.Default(ids, visible);
		Assert.Equal(frozen.Blocks.Select((BlockState b) => b.Id).ToArray(), state.Order.ToArray());
		Assert.Equal(frozen.Blocks.Select((BlockState b) => b.Hidden).ToArray(), state.Order.Select((string id) => state.Hidden.Contains(id)).ToArray());
		// 默认 = 单列纵向流(每行一个块),这是"首屏与改动前同一条渲染路径"的机器可判条件
		LayoutPlan plan = state.Plan();
		Assert.True(plan.IsSingleColumn, pageId);
		Assert.Equal(state.VisibleIds().Count, plan.Rows.Count);
		Assert.All(plan.Rows, (LayoutRow row) => Assert.Single(row.Slots));
	}

	// ————— 2) 提交 = 写 v2 + 写通 v1(冻结解析器回读全等) —————

	[Fact]
	public void PersistWritesV2ThenWritesThroughV1()
	{
		UseDir();
		try
		{
			List<LayoutBlockRef> refs = new List<LayoutBlockRef>
			{
				new LayoutBlockRef("header", true),
				new LayoutBlockRef("list", true)
			};
			// 1) 旧版本留下的 v1 先摆上(升级路径)
			PageLayoutDoc legacy = PageLayoutDoc.Default(new[] { "list", "header" }, new[] { "list" });
			PageLayoutStore.Save("tasks", legacy);
			Assert.False(File.Exists(V2Path()));

			// 2) 宿主装载(只有 v1 → 迁移)
			LayoutV2 doc = LayoutStoreV2.Load();
			PageLayoutV2? stored = (doc.Layout.Pages.TryGetValue("tasks", out PageLayoutV2? found) ? found : null);
			LayoutPageState state = LayoutEngine.LoadState("tasks", stored, refs);
			Assert.Equal(new[] { "list", "header" }, state.Order.ToArray()); // v1 的顺序被保留
			Assert.Contains("header", state.Hidden);

			// 3) 用户改了布局(恢复 header、调顺序)并提交
			state.Restore("header");
			state.DragReorder("header", 0.0, new Dictionary<string, double>
			{
				["header"] = 100.0,
				["list"] = 100.0
			});
			LayoutEngine.ApplyPage(doc, "tasks", state.ToPage());
			LayoutStoreV2.Save(doc);
			PageLayoutStore.Reload();

			Assert.True(File.Exists(V2Path()));
			LayoutV2 onDisk = LayoutStoreV2.Load();
			PageLayoutV2 page = onDisk.Layout.Pages["tasks"];
			Assert.Equal(new[] { "header", "list" }, page.Blocks.Select((BlockLayout b) => b.Id).ToArray());
			Assert.All(page.Blocks, (BlockLayout b) => Assert.False(b.Hidden));

			// 4) 冻结的 v1 解析器读磁盘回同一份 {id,hidden}
			PageLayoutDoc? v1 = PageLayoutStore.Load("tasks");
			Assert.NotNull(v1);
			Assert.Equal(new[] { "header", "list" }, v1!.Blocks.Select((BlockState b) => b.Id).ToArray());
			Assert.All(v1.Blocks, (BlockState b) => Assert.False(b.Hidden));
		}
		finally
		{
			Release();
		}
	}

	// ————— 3) Reload() 让老 v1 路径立刻看到写通结果(L0 报告的坑 3) —————

	[Fact]
	public void ReloadRefreshesV1CacheAfterWriteThrough()
	{
		UseDir();
		try
		{
			PageLayoutDoc before = PageLayoutDoc.Default(new[] { "header", "list" }, new[] { "header" });
			PageLayoutStore.Save("tasks", before);
			Assert.True(PageLayoutStore.Load("tasks")!.Blocks[1].Hidden); // 旧值进缓存

			// 走 v2 真源改写(等价宿主一次提交)
			LayoutV2 doc = LayoutStoreV2.Load();
			PageLayoutV2 page = doc.Layout.Pages["tasks"];
			page.Blocks[1].Hidden = false;
			LayoutStoreV2.Save(doc);

			// 缓存没过期 → 老路径看到的是旧快照(这是 PageLayoutStore 的既有行为,本包只加 Reload 不改它)
			Assert.True(PageLayoutStore.Load("tasks")!.Blocks[1].Hidden);
			// Reload 之后立刻看到新值
			PageLayoutStore.Reload();
			Assert.False(PageLayoutStore.Load("tasks")!.Blocks[1].Hidden);
		}
		finally
		{
			Release();
		}
	}

	// ————— 4) 提交只动本页,别的页面的 v2 专属字段不丢 —————

	[Fact]
	public void PersistKeepsOtherPagesV2OnlyFields()
	{
		UseDir();
		try
		{
			LayoutV2 seed = LayoutStoreV2.ParseImport("""
			{
			  "version": 2,
			  "kind": "tipcl-layout",
			  "layout": {
			    "pages": {
			      "tasks": { "version": 2, "mode": "stack", "gap": 12, "blocks": [{ "id": "header" }, { "id": "list" }] },
			      "settings:general": {
			        "version": 2,
			        "mode": "grid",
			        "cols": 6,
			        "blocks": [{ "id": "appearance", "style": "card", "title": true, "hidden": true }]
			      }
			    },
			    "splits": { "settings:general": { "nav": 0.25 } },
			    "widgets": [{ "id": "w-1", "type": "note", "title": "便签" }]
			  },
			  "profiles": { "我的布局": { "pages": {} } }
			}
			""");
			LayoutStoreV2.Save(seed);

			// 宿主只改 tasks 页
			List<LayoutBlockRef> refs = new List<LayoutBlockRef>
			{
				new LayoutBlockRef("header", true),
				new LayoutBlockRef("list", true)
			};
			LayoutV2 doc = LayoutStoreV2.Load();
			LayoutPageState state = LayoutEngine.LoadState("tasks", doc.Layout.Pages["tasks"], refs);
			state.Hide("header");
			LayoutEngine.ApplyPage(doc, "tasks", state.ToPage());
			LayoutStoreV2.Save(doc);
			PageLayoutStore.Reload();

			LayoutV2 after = LayoutStoreV2.Load();
			Assert.True(after.Layout.Pages["tasks"].Blocks[0].Hidden);
			// 别的页面一个字段没动
			PageLayoutV2 other = after.Layout.Pages["settings:general"];
			Assert.Equal("grid", other.Mode);
			Assert.Equal(6, other.Cols);
			Assert.True(other.Blocks[0].Hidden);
			Assert.Equal("card", other.Blocks[0].Style);
			Assert.True(other.Blocks[0].Title);
			Assert.Equal(0.25, after.Layout.Splits["settings:general"].Get("nav", 0.0));
			Assert.Equal("note", Assert.Single(after.Layout.Widgets).Type);
			Assert.True(after.Profiles.ContainsKey("我的布局"));
			Assert.Equal(12.0, after.Layout.Pages["tasks"].Gap); // 本页页级参数也保留
		}
		finally
		{
			Release();
		}
	}

	// ————— 自建块 / 插件块在宿主装载里的存活(v2 是它们的唯一来源) —————

	[Fact]
	public void CustomBlocksInDocSurviveHostLoadAndPersist()
	{
		UseDir();
		try
		{
			LayoutV2 seed = LayoutStoreV2.ParseImport("""
			{
			  "version": 2,
			  "kind": "tipcl-layout",
			  "layout": {
			    "widgets": [{ "id": "w-1", "type": "note", "title": "便签" }],
			    "pages": {
			      "tasks": { "version": 2, "blocks": [{ "id": "header" }, { "id": "list" }, { "id": "custom:w-1", "span": 1 }] }
			    }
			  }
			}
			""");
			LayoutStoreV2.Save(seed);

			List<LayoutBlockRef> refs = new List<LayoutBlockRef>
			{
				new LayoutBlockRef("header", true),
				new LayoutBlockRef("list", true)
			};
			LayoutV2 doc = LayoutStoreV2.Load();
			LayoutPageState state = LayoutEngine.LoadState("tasks", doc.Layout.Pages["tasks"], refs);
			Assert.Equal(new[] { "header", "list", "custom:w-1" }, state.Order.ToArray());
			Assert.False(LayoutEngine.IsUnavailable("z-custom"));
			Assert.True(LayoutEngine.IsUnavailable("custom:w-1")); // 还没注册内容工厂 → 显示「不可用」占位
			// 隐藏再提交:自建块仍然留在顺序里(数据不丢)
			state.Hide("custom:w-1");
			LayoutEngine.ApplyPage(doc, "tasks", state.ToPage());
			LayoutStoreV2.Save(doc);
			LayoutV2 after = LayoutStoreV2.Load();
			Assert.Equal(new[] { "header", "list", "custom:w-1" }, after.Layout.Pages["tasks"].Blocks.Select((BlockLayout b) => b.Id).ToArray());
			Assert.True(after.Layout.Pages["tasks"].Blocks[2].Hidden);
			Assert.Equal("note", Assert.Single(after.Layout.Widgets).Type);
		}
		finally
		{
			Release();
		}
	}

	// ————— 重置本页 = 回默认(等价 PageLayoutDoc.Default),且不动别的页 —————

	[Fact]
	public void ResetPageGoesBackToWhitelistDefault()
	{
		UseDir();
		try
		{
			LayoutV2 seed = LayoutStoreV2.ParseImport("""
			{
			  "version": 2,
			  "kind": "tipcl-layout",
			  "layout": { "pages": { "tasks": { "version": 2, "blocks": [{ "id": "list" }, { "id": "header", "hidden": true }] } } }
			}
			""");
			LayoutStoreV2.Save(seed);
			List<LayoutBlockRef> refs = new List<LayoutBlockRef>
			{
				new LayoutBlockRef("header", true),
				new LayoutBlockRef("list", true)
			};
			LayoutV2 doc = LayoutStoreV2.Load();
			LayoutPageState state = LayoutEngine.LoadState("tasks", doc.Layout.Pages["tasks"], refs);
			state.Reset(refs);
			PageLayoutV2 reset = state.ToPage();
			PageLayoutDoc frozen = PageLayoutDoc.Default(new[] { "header", "list" }, new[] { "header", "list" });
			Assert.Equal(frozen.Blocks.Select((BlockState b) => b.Id).ToArray(), reset.Blocks.Select((BlockLayout b) => b.Id).ToArray());
			Assert.Equal(frozen.Blocks.Select((BlockState b) => b.Hidden).ToArray(), reset.Blocks.Select((BlockLayout b) => b.Hidden).ToArray());
		}
		finally
		{
			Release();
		}
	}

	// ————— 损坏的 v2 不能让宿主崩:回落 v1 → 默认 —————

	[Fact]
	public void HostLoadFallsBackWhenV2IsCorrupt()
	{
		UseDir();
		try
		{
			PageLayoutStore.Save("tasks", PageLayoutDoc.Default(new[] { "list", "header" }, new[] { "header" }));
			Directory.CreateDirectory(_tmp);
			File.WriteAllText(V2Path(), "{ truncated");

			List<LayoutBlockRef> refs = new List<LayoutBlockRef>
			{
				new LayoutBlockRef("header", true),
				new LayoutBlockRef("list", true)
			};
			LayoutV2 doc = LayoutStoreV2.Load();
			Assert.Equal("v1-fallback", LayoutStoreV2.LastLoadSource);
			Assert.True(doc.Layout.Pages.TryGetValue("tasks", out PageLayoutV2? stored));
			LayoutPageState state = LayoutEngine.LoadState("tasks", stored, refs);
			Assert.Equal(new[] { "list", "header" }, state.Order.ToArray()); // v1 顺序生效,而不是白名单默认顺序
		}
		finally
		{
			Release();
		}
	}
}
