using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Pages;
using TipCL.Pages.Layout;
using Xunit;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Tests;

/// <summary>
/// 布局文档 v2 的模型层测试:§3.2 逐字段默认值 / 钳位 / 白名单、未知字段保留、导入校验。
/// 测试宿主是 net8,而 net48 主工程的 System.Text.Json 是 8.0.0.5(测试侧是 8.0.0.0),
/// 所以**绝不把 JsonElement 传进主工程的成员**(会 CS1705):一律走字符串入口(ParseImport / ToJson),
/// 与既有 PageLayoutTests 的做法保持一致。
/// </summary>
public class LayoutV2ModelTests
{
	// ————— 默认值(§3.2 的"默认"列) —————

	[Fact]
	public void EnvelopeAndShellDefaultsMatchContract()
	{
		LayoutV2 doc = LayoutV2.CreateDefault();
		Assert.Equal(2, doc.Version);
		Assert.Equal("tipcl-layout", doc.Kind);
		Assert.Equal("", doc.ActiveProfile);
		Assert.Empty(doc.Layout.Pages);
		Assert.Empty(doc.Layout.Splits);
		Assert.Empty(doc.Layout.Widgets);
		Assert.Empty(doc.Layout.Variants);
		Assert.Empty(doc.Profiles);

		ShellLayout shell = doc.Layout.Shell;
		Assert.Equal("tabs", shell.NavMode);
		Assert.Equal("left", shell.NavSide);
		Assert.Equal(196.0, shell.NavWidth);
		Assert.False(shell.NavIconOnly);
		Assert.Equal("grouped", shell.NavStyle);
		Assert.Equal(new[] { "launch", "download", "instance", "ai", "more", "settings", "tasks" }, shell.NavOrder.ToArray());
		Assert.Empty(shell.NavHidden);
		Assert.Empty(shell.NavGroups);
		Assert.Equal(new[] { "launch", "library", "download", "plugins", "settings" }, shell.Tabs.ToArray());
		Assert.Equal("standard", shell.Density);
		Assert.True(shell.Titlebar.Bridge);
		Assert.True(shell.Titlebar.Ai);
		Assert.True(shell.Titlebar.Theme);
		Assert.True(shell.Titlebar.Account);
	}

	[Fact]
	public void PageAndBlockDefaultsMatchContract()
	{
		PageLayoutV2 page = new PageLayoutV2();
		Assert.Equal(2, page.Version);
		Assert.Equal("stack", page.Mode);
		Assert.Equal(12, page.Cols);
		Assert.Equal(0.0, page.RowH);
		Assert.Equal(10.0, page.Gap);
		Assert.Equal(new[] { 18.0, 14.0, 18.0, 16.0 }, page.Pad);
		Assert.Empty(page.Blocks);

		BlockLayout block = new BlockLayout();
		Assert.Equal("", block.Id);
		Assert.False(block.Hidden);
		Assert.Equal(0.0, block.X);
		Assert.Equal(0.0, block.Y);
		Assert.Equal(1.0, block.W);
		Assert.Equal(1.0, block.H);
		Assert.Equal(1, block.Span);
		Assert.Equal("plain", block.Style);
		Assert.False(block.Title);
		Assert.Null(block.Pad);
		Assert.Equal(0.0, block.MinH);
		Assert.Equal(-1, block.Z); // -1 = 未设置,Normalize 时填顺序索引
		Assert.Empty(block.Settings);
	}

	// ————— shell:白名单 + 钳位 —————

	[Fact]
	public void ShellUnknownValuesFallBackAndNumbersAreClamped()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{
		  "version": 2,
		  "kind": "tipcl-layout",
		  "layout": {
		    "shell": {
		      "nav_mode": "weird",
		      "nav_side": "top",
		      "nav_width": 20,
		      "nav_style": "fancy",
		      "density": "huge",
		      "nav_icon_only": true,
		      "titlebar": { "bridge": false, "ai": "yes" },
		      "tabs": ["settings", "bogus", "settings", "launch"],
		      "nav_order": ["settings", "bogus", "tasks"],
		      "nav_hidden": ["nope", "ai"]
		    }
		  }
		}
		""");
		ShellLayout shell = doc.Layout.Shell;
		Assert.Equal("tabs", shell.NavMode);
		Assert.Equal("left", shell.NavSide);
		Assert.Equal(140.0, shell.NavWidth); // 20 → 夹到 140
		Assert.Equal("grouped", shell.NavStyle);
		Assert.Equal("standard", shell.Density);
		Assert.True(shell.NavIconOnly);
		Assert.False(shell.Titlebar.Bridge);
		Assert.True(shell.Titlebar.Ai); // 非布尔 → 保持默认 true
		Assert.Equal(new[] { "settings", "launch" }, shell.Tabs.ToArray()); // 未知丢弃、重复只留一次、顺序保持
		Assert.Equal(new[] { "settings", "tasks" }, shell.NavOrder.ToArray());
		Assert.Equal(new[] { "ai" }, shell.NavHidden.ToArray());

		LayoutV2 big = LayoutStoreV2.ParseImport("""
		{ "version": 2, "kind": "tipcl-layout", "layout": { "shell": { "nav_width": 9999 } } }
		""");
		Assert.Equal(320.0, big.Layout.Shell.NavWidth);
	}

	[Fact]
	public void EmptyTabsFallBackToDefaultFive()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{ "version": 2, "kind": "tipcl-layout", "layout": { "shell": { "tabs": ["bogus"] } } }
		""");
		Assert.Equal(new[] { "launch", "library", "download", "plugins", "settings" }, doc.Layout.Shell.Tabs.ToArray());
	}

	[Fact]
	public void NavGroupsDropUnknownKeysAndEmptyGroups()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{
		  "version": 2,
		  "kind": "tipcl-layout",
		  "layout": {
		    "shell": {
		      "nav_groups": [
		        { "title": "我的常用", "keys": ["launch", "bogus", "launch", "tasks"] },
		        { "title": "空的", "keys": ["bogus"] }
		      ]
		    }
		  }
		}
		""");
		NavGroupLayout group = Assert.Single(doc.Layout.Shell.NavGroups);
		Assert.Equal("我的常用", group.Title);
		Assert.Equal(new[] { "launch", "tasks" }, group.Keys.ToArray());
	}

	// ————— 单页:模式 / 列数 / 行高 / 间距 / 边距 —————

	[Fact]
	public void PageClampsModeColsRowHeightGapAndPad()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{
		  "version": 2,
		  "kind": "tipcl-layout",
		  "layout": {
		    "pages": {
		      "pages:launch": {
		        "version": 2,
		        "mode": "freeform",
		        "cols": 5,
		        "row_h": -3,
		        "gap": 99,
		        "pad": [100, -5, 7],
		        "blocks": []
		      }
		    }
		  }
		}
		""");
		PageLayoutV2 page = doc.Layout.Pages["pages:launch"];
		Assert.Equal("stack", page.Mode); // 未知模式 → stack
		Assert.Equal(4, page.Cols); // 5 → 最近档 4(等距取小档)
		Assert.Equal(0.0, page.RowH); // -3 → 0(0 = 自适应)
		Assert.Equal(32.0, page.Gap); // 99 → 32
		Assert.Equal(new[] { 64.0, 0.0, 7.0, 16.0 }, page.Pad); // 逐项夹 0..64,缺的第 4 项用默认 16
	}

	[Fact]
	public void ColsSnapToNearestAllowedChoice()
	{
		Assert.Equal(2, LayoutSchema.SnapCols(1));
		Assert.Equal(2, LayoutSchema.SnapCols(2));
		Assert.Equal(4, LayoutSchema.SnapCols(5));
		Assert.Equal(6, LayoutSchema.SnapCols(6));
		Assert.Equal(12, LayoutSchema.SnapCols(99));
		Assert.Equal(3, LayoutSchema.SnapCols(3));
	}

	[Fact]
	public void PageVersionMismatchDropsOnlyThatPage()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{
		  "version": 2,
		  "kind": "tipcl-layout",
		  "layout": {
		    "pages": {
		      "old": { "version": 1, "blocks": [{ "id": "a" }] },
		      "good": { "version": 2, "blocks": [{ "id": "a" }] }
		    }
		  }
		}
		""");
		Assert.False(doc.Layout.Pages.ContainsKey("old")); // 单页版本门禁不过 → 该页回默认
		Assert.True(doc.Layout.Pages.ContainsKey("good"));
	}

	// ————— 单块:grid / canvas 两套档位 —————

	[Fact]
	public void GridBlocksClampToPageColumnsAndStyleWhitelist()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{
		  "version": 2,
		  "kind": "tipcl-layout",
		  "layout": {
		    "pages": {
		      "p": {
		        "version": 2,
		        "mode": "grid",
		        "cols": 6,
		        "blocks": [
		          { "id": "hero", "x": 9, "y": 9, "w": 99, "h": 0, "span": 99, "style": "fancy", "title": "yes", "min_h": -2, "z": -5 }
		        ]
		      }
		    }
		  }
		}
		""");
		BlockLayout block = Assert.Single(doc.Layout.Pages["p"].Blocks);
		Assert.Equal(5.0, block.X); // 0..cols-1
		Assert.Equal(5.0, block.Y);
		Assert.Equal(6.0, block.W); // 1..cols
		Assert.Equal(1.0, block.H); // 0 → 1
		Assert.Equal(6, block.Span); // 1..cols
		Assert.Equal("plain", block.Style); // 未知外观 → plain(默认渲染与现状等价)
		Assert.False(block.Title); // 非布尔 → false
		Assert.Equal(0.0, block.MinH);
		Assert.Equal(0, block.Z); // 负数 → 顺序索引
	}

	[Fact]
	public void CanvasBlocksUseNormalizedClamps()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{
		  "version": 2,
		  "kind": "tipcl-layout",
		  "layout": {
		    "pages": {
		      "p": {
		        "version": 2,
		        "mode": "canvas",
		        "row_h": 80,
		        "blocks": [{ "id": "hero", "x": 2, "y": -1, "w": 0.001, "h": 3 }]
		      }
		    }
		  }
		}
		""");
		BlockLayout block = Assert.Single(doc.Layout.Pages["p"].Blocks);
		Assert.Equal(1.0, block.X);
		Assert.Equal(0.0, block.Y);
		Assert.Equal(0.04, block.W);
		Assert.Equal(1.0, block.H);
		Assert.Equal(80.0, doc.Layout.Pages["p"].RowH);
	}

	[Fact]
	public void BlocksDeduplicateIdsAndFillZWithIndexOrder()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{
		  "version": 2,
		  "kind": "tipcl-layout",
		  "layout": {
		    "pages": {
		      "p": {
		        "version": 2,
		        "blocks": [
		          { "id": "a" },
		          { "id": "a" },
		          { "id": "" },
		          { "id": "b" }
		        ]
		      }
		    }
		  }
		}
		""");
		Assert.Equal(new[] { "a", "b" }, doc.Layout.Pages["p"].Blocks.Select((BlockLayout b) => b.Id).ToArray());
		Assert.Equal(new[] { 0, 1 }, doc.Layout.Pages["p"].Blocks.Select((BlockLayout b) => b.Z).ToArray());
	}

	[Fact]
	public void HiddenOnlyAcceptsJsonTrueLikeV1()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{
		  "version": 2,
		  "kind": "tipcl-layout",
		  "layout": {
		    "pages": {
		      "p": {
		        "version": 2,
		        "blocks": [
		          { "id": "hero", "hidden": "yes" },
		          { "id": "news", "hidden": true },
		          { "id": "quick", "hidden": 1 },
		          { "id": "config", "hidden": false }
		        ]
		      }
		    }
		  }
		}
		""");
		Assert.Equal(new[] { false, true, false, false }, doc.Layout.Pages["p"].Blocks.Select((BlockLayout b) => b.Hidden).ToArray());
	}

	[Fact]
	public void BlockPadIsOptionalAndClampedWhenPresent()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{
		  "version": 2,
		  "kind": "tipcl-layout",
		  "layout": {
		    "pages": {
		      "p": {
		        "version": 2,
		        "blocks": [
		          { "id": "hero", "pad": [-1, 200, 8, 9] },
		          { "id": "news" }
		        ]
		      }
		    }
		  }
		}
		""");
		Assert.Equal(new[] { 0.0, 64.0, 8.0, 9.0 }, doc.Layout.Pages["p"].Blocks[0].Pad);
		Assert.Null(doc.Layout.Pages["p"].Blocks[1].Pad);
	}

	[Fact]
	public void UnknownBlockFieldsSurviveInSettingsAndExtra()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{
		  "version": 2,
		  "kind": "tipcl-layout",
		  "layout": {
		    "pages": {
		      "p": {
		        "version": 2,
		        "blocks": [{ "id": "hero", "settings": { "instance": "1.20.1-Fabric" } }]
		      }
		    }
		  }
		}
		""");
		BlockLayout block = Assert.Single(doc.Layout.Pages["p"].Blocks);
		Assert.True(block.Settings.ContainsKey("instance"));
		Assert.Equal("1.20.1-Fabric", block.Settings["instance"]);
	}

	// ————— 分栏比例 —————

	[Fact]
	public void SplitRatiosAreClampedAndNonNumbersGoToExtra()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{
		  "version": 2,
		  "kind": "tipcl-layout",
		  "layout": {
		    "splits": {
		      "pages:library": { "folders": 0.02, "list": 0.9, "weird": "x" }
		    }
		  }
		}
		""");
		SplitState split = doc.Layout.Splits["pages:library"];
		Assert.Equal(0.15, split.Get("folders", 0.0));
		Assert.Equal(0.85, split.Get("list", 0.0));
		Assert.Equal(2, split.Ratios.Count); // 非数字不生效,留在 Extra 里
		Assert.True(split.Extra.ContainsKey("weird"));
	}

	// ————— 自建区块(§4.F / Q5) —————

	[Fact]
	public void WidgetsGetAutomaticIdsAndFlagDangerousTypes()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{
		  "version": 2,
		  "kind": "tipcl-layout",
		  "layout": {
		    "widgets": [
		      { "type": "note", "title": "便签" },
		      { "id": "custom-1", "type": "web", "title": "网页" },
		      { "id": "custom-2", "type": "button" },
		      { "id": "custom-3", "type": "unknown-thing" }
		    ]
		  }
		}
		""");
		Assert.Equal(4, doc.Layout.Widgets.Count);
		Assert.StartsWith("w-", doc.Layout.Widgets[0].Id); // 缺 id → 自动 w-<时间戳>
		Assert.Equal("便签", doc.Layout.Widgets[0].Title);
		Assert.True(LayoutSchema.IsKnownWidgetType("note"));
		Assert.True(LayoutSchema.IsKnownWidgetType("web"));
		Assert.True(LayoutSchema.IsDangerousWidgetType("web")); // Q5:默认关闭
		Assert.True(LayoutSchema.IsDangerousWidgetType("button")); // Q5:默认关闭
		Assert.False(LayoutSchema.IsDangerousWidgetType("note"));
		Assert.False(LayoutSchema.IsKnownWidgetType("unknown-thing"));
		Assert.Equal("unknown-thing", doc.Layout.Widgets[3].Type); // 未知类型保留(不删用户数据)
	}

	// ————— 未知字段保留但不生效 —————

	[Fact]
	public void UnknownFieldsAreKeptAndReemitted()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{
		  "version": 2,
		  "kind": "tipcl-layout",
		  "future_top": 123,
		  "layout": {
		    "future_layout": { "a": 1 },
		    "shell": { "future_shell": "s" },
		    "pages": {
		      "pages:launch": {
		        "version": 2,
		        "future_page": "x",
		        "future_page_obj": { "deep": [1, 2, {"k": "v"}] },
		        "blocks": [{ "id": "hero", "future_block": [1, 2] }]
		      }
		    },
		    "widgets": [{ "id": "custom-1", "type": "note", "future_widget": true }],
		    "meta": { "source": "分享来源" }
		  }
		}
		""");
		Assert.True(doc.Extra.ContainsKey("future_top"));
		Assert.True(doc.Layout.Extra.ContainsKey("future_layout"));
		Assert.True(doc.Layout.Shell.Extra.ContainsKey("future_shell"));
		Assert.True(doc.Layout.Pages["pages:launch"].Extra.ContainsKey("future_page"));
		Assert.True(doc.Layout.Pages["pages:launch"].Blocks[0].Extra.ContainsKey("future_block"));
		Assert.True(doc.Layout.Widgets[0].Extra.ContainsKey("future_widget"));
		Assert.Equal("分享来源", doc.Layout.Meta["source"]);

		string json = LayoutStoreV2.ExportJson(doc);
		foreach (string key in new[] { "future_top", "future_layout", "future_shell", "future_page", "future_page_obj", "future_block", "future_widget" })
		{
			Assert.Contains("\"" + key + "\"", json);
		}
		Assert.Contains("分享来源", json);
		// 未知字段写回后仍能读回来(不知道的字段一路保真)
		LayoutV2 again = LayoutStoreV2.ParseImport(json);
		Assert.True(again.Layout.Pages["pages:launch"].Extra.ContainsKey("future_page_obj"));
	}

	[Fact]
	public void UnknownFieldsNeverOverrideKnownFields()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{ "version": 2, "kind": "tipcl-layout", "layout": { "pages": { "p": { "version": 2, "blocks": [] } } } }
		""");
		doc.Extra["version"] = 99;
		doc.Layout.Shell.Extra["density"] = "loose";
		string json = LayoutStoreV2.ExportJson(doc);
		LayoutV2 again = LayoutStoreV2.ParseImport(json);
		Assert.Equal(2, again.Version);
		Assert.Equal("standard", again.Layout.Shell.Density);
	}

	// ————— 响应式变体(§0.1 Q6) —————

	[Fact]
	public void VariantsAreSanitizedOneLevelDeep()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{
		  "version": 2,
		  "kind": "tipcl-layout",
		  "layout": {
		    "variants": {
		      "narrow": {
		        "pages": { "p": { "version": 2, "mode": "grid", "blocks": [{ "id": "hero" }] } },
		        "variants": { "nested": { "pages": {} } }
		      }
		    }
		  }
		}
		""");
		Assert.True(doc.Layout.Variants.ContainsKey("narrow"));
		PageLayoutV2 narrow = doc.Layout.Variants["narrow"].Pages["p"];
		Assert.Equal("grid", narrow.Mode);
		Assert.Equal("hero", narrow.Blocks[0].Id);
		Assert.Empty(doc.Layout.Variants["narrow"].Variants); // 嵌套 variants 不再递归展开
	}

	// ————— 方案(profiles) —————

	[Fact]
	public void ProfilesRoundTripAndDanglingActiveProfileIsCleared()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{
		  "version": 2,
		  "kind": "tipcl-layout",
		  "active_profile": "不存在的方案",
		  "layout": { "pages": { "p": { "version": 2, "blocks": [{ "id": "hero" }] } } },
		  "profiles": { "我的布局": { "pages": { "p": { "version": 2, "blocks": [{ "id": "hero", "hidden": true }] } } } }
		}
		""");
		Assert.Equal("", doc.ActiveProfile); // 悬空方案名 → 直接编辑态
		Assert.True(doc.Profiles.ContainsKey("我的布局"));
		Assert.True(doc.GetProfile("我的布局")!.Pages["p"].Blocks[0].Hidden);
		Assert.Equal("我的布局", Assert.Single(doc.ListProfiles()).Name);
	}

	// ————— 往返 —————

	[Fact]
	public void ExportImportRoundTripKeepsContractFields()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{
		  "version": 2,
		  "kind": "tipcl-layout",
		  "active_profile": "",
		  "layout": {
		    "shell": { "tabs": ["library", "launch"], "nav_width": 240, "density": "loose" },
		    "pages": {
		      "pages:launch": {
		        "version": 2,
		        "mode": "grid",
		        "cols": 6,
		        "gap": 12,
		        "pad": [10, 11, 12, 13],
		        "blocks": [
		          { "id": "hero", "x": 0, "w": 3, "span": 2, "style": "card", "title": true, "min_h": 120, "z": 1 },
		          { "id": "news", "hidden": true, "style": "divider" }
		        ]
		      }
		    },
		    "splits": { "pages:library": { "folders": 0.3 } },
		    "widgets": [{ "id": "custom-1", "type": "clock", "title": "时钟", "settings": { "format": "HH:mm" } }],
		    "meta": { "theme": "dark" }
		  }
		}
		""");
		LayoutV2 again = LayoutStoreV2.ParseImport(LayoutStoreV2.ExportJson(doc));
		Assert.Equal(new[] { "library", "launch" }, again.Layout.Shell.Tabs.ToArray());
		Assert.Equal(240.0, again.Layout.Shell.NavWidth);
		Assert.Equal("loose", again.Layout.Shell.Density);
		PageLayoutV2 page = again.Layout.Pages["pages:launch"];
		Assert.Equal("grid", page.Mode);
		Assert.Equal(6, page.Cols);
		Assert.Equal(12.0, page.Gap);
		Assert.Equal(new[] { 10.0, 11.0, 12.0, 13.0 }, page.Pad);
		Assert.Equal(new[] { "hero", "news" }, page.Blocks.Select((BlockLayout b) => b.Id).ToArray());
		Assert.True(page.Blocks[1].Hidden);
		Assert.Equal(3.0, page.Blocks[0].W);
		Assert.Equal(2, page.Blocks[0].Span);
		Assert.Equal("card", page.Blocks[0].Style);
		Assert.True(page.Blocks[0].Title);
		Assert.Equal(120.0, page.Blocks[0].MinH);
		Assert.Equal(1, page.Blocks[0].Z);
		Assert.Equal(0.3, again.Layout.Splits["pages:library"].Get("folders", 0.0));
		Assert.Equal("HH:mm", again.Layout.Widgets[0].Settings["format"]);
		Assert.Equal("dark", again.Layout.Meta["theme"]);
	}

	// ————— 导入校验(kind / version / 非 JSON) —————

	[Fact]
	public void ImportRejectsBadEnvelopeWithChineseMessage()
	{
		string[] bad = new[]
		{
			"not json at all",
			"",
			"{\"version\":2,\"kind\":\"something-else\",\"layout\":{}}",
			"{\"version\":2,\"kind\":\"tipcl-page-layout\",\"pages\":{}}",
			"{\"version\":1,\"kind\":\"tipcl-layout\",\"layout\":{}}",
			"{\"version\":3,\"kind\":\"tipcl-layout\",\"layout\":{}}",
			"{\"kind\":\"tipcl-layout\",\"layout\":{}}",
			"{\"version\":2,\"layout\":{}}",
			"[1,2,3]",
			"\"x\""
		};
		foreach (string json in bad)
		{
			InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => LayoutStoreV2.ParseImport(json));
			Assert.Contains("布局文件无效", ex.Message);
		}
	}

	[Fact]
	public void ParseImportAcceptsTruncatedLookingButCompleteDocumentWithNoPages()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("{\"version\":2,\"kind\":\"tipcl-layout\"}");
		Assert.Equal(2, doc.Version);
		Assert.Equal("tipcl-layout", doc.Kind);
		Assert.Empty(doc.Layout.Pages);
		Assert.Equal(new[] { "launch", "library", "download", "plugins", "settings" }, doc.Layout.Shell.Tabs.ToArray());
	}

	[Fact]
	public void CloneIsDeepAndSanitizeIsIdempotent()
	{
		LayoutV2 doc = LayoutStoreV2.ParseImport("""
		{
		  "version": 2,
		  "kind": "tipcl-layout",
		  "layout": {
		    "pages": { "p": { "version": 2, "blocks": [{ "id": "hero", "settings": { "k": "v" } }] } },
		    "widgets": [{ "id": "custom-1", "type": "note" }]
		  }
		}
		""");
		LayoutV2 copy = doc.Clone();
		copy.Layout.Pages["p"].Blocks[0].Settings["k"] = "changed";
		copy.Layout.Pages["p"].Mode = "canvas";
		Assert.Equal("v", doc.Layout.Pages["p"].Blocks[0].Settings["k"]);
		Assert.Equal("stack", doc.Layout.Pages["p"].Mode);
		string once = LayoutStoreV2.ExportJson(doc);
		string twice = LayoutStoreV2.ExportJson(LayoutStoreV2.ParseImport(once));
		Assert.Equal(once, twice); // 钳位幂等:洗两遍结果一致
	}
}
