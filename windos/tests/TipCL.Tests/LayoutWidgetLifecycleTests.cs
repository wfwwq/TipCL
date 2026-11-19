using System;
using System.Collections.Generic;
using TipCL.Pages.Layout;
using Xunit;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Tests;

/// <summary>
/// L6 自建区块生命周期的纯模型回归闸门(零 WPF,net8 测试宿主直接断言):
///   新建定义(5 种安全类型的最小参数) / 危险类型拒绝 / 标题与参数校验
///   删除定义 → 同时清掉所有页面(含窄窗变体)里的 custom:&lt;id&gt; 引用
///   追加到页面末尾 = 复用"粘贴到页面末尾"的规则(可为空、按 id 去重、不污染剪贴板)
/// 不落盘,全部只改内存文档;真实写盘仍由 LayoutStoreV2.Save 的唯一入口负责。
/// </summary>
public class LayoutWidgetLifecycleTests
{
	private static WidgetDef? NewWidget(string type, string title, string param = "", string version = "")
	{
		return WidgetLibrary.Create(type, title, param, version, out _);
	}

	private static PageLayoutV2 PageWith(params string[] ids)
	{
		PageLayoutV2 page = new PageLayoutV2();
		foreach (string id in ids)
		{
			page.Blocks.Add(new BlockLayout { Id = id });
		}
		return page;
	}

	private static LayoutV2 DocWith(params string[] referencedWidgetIds)
	{
		LayoutV2 doc = new LayoutV2();
		doc.Layout.Pages["page"] = PageWith(referencedWidgetIds);
		return doc;
	}

	// ————— 新建 —————

	[Fact]
	public void CreateFillsMinimalSettingsForAllSafeTypes()
	{
		WidgetDef note = NewWidget(LayoutSchema.WidgetNote, "待办", "今天先做这个")!;
		Assert.Equal("今天先做这个", WidgetCommon.Str(note.Settings, "text", ""));

		WidgetDef shortcut = NewWidget(LayoutSchema.WidgetShortcut, "开服", "default", "1.20.1")!;
		Assert.Equal("default", WidgetCommon.Str(shortcut.Settings, "instance", ""));
		Assert.Equal("1.20.1", WidgetCommon.Str(shortcut.Settings, "version", ""));

		WidgetDef clock = NewWidget(LayoutSchema.WidgetClock, "时钟", "")!;
		Assert.Equal("HH:mm", WidgetCommon.Str(clock.Settings, "format", ""));

		WidgetDef divider = NewWidget(LayoutSchema.WidgetDivider, "隔断")!;
		Assert.Empty(divider.Settings);

		WidgetDef playtime = NewWidget(LayoutSchema.WidgetPlaytime, "时长")!;
		Assert.Empty(playtime.Settings);
	}

	[Theory]
	[InlineData(LayoutSchema.WidgetWeb)]
	[InlineData(LayoutSchema.WidgetButton)]
	[InlineData("unknown")]
	[InlineData("")]
	public void CreateRejectsDangerousAndUnknownTypes(string type)
	{
		WidgetDef? widget = WidgetLibrary.Create(type, "标题", "https://example.com", "", out string error);
		Assert.Null(widget);
		Assert.False(string.IsNullOrWhiteSpace(error));
	}

	[Fact]
	public void CreateRejectsEmptyTitleAndMissingRequiredParameters()
	{
		Assert.Null(WidgetLibrary.Create(LayoutSchema.WidgetNote, "  ", "正文", "", out string error1));
		Assert.False(string.IsNullOrWhiteSpace(error1));

		Assert.Null(WidgetLibrary.Create(LayoutSchema.WidgetNote, "便签", "  ", "", out string error2));
		Assert.False(string.IsNullOrWhiteSpace(error2));

		Assert.Null(WidgetLibrary.Create(LayoutSchema.WidgetShortcut, "快捷", "", "", out string error3));
		Assert.False(string.IsNullOrWhiteSpace(error3));

		Assert.NotNull(WidgetLibrary.Create(LayoutSchema.WidgetDivider, "隔断", "", "", out _));
	}

	[Fact]
	public void CreateForGivesDocumentUniqueIdWithoutTouchingPages()
	{
		LayoutV2 doc = DocWith();
		WidgetDef widget = WidgetLibrary.CreateFor(doc, LayoutSchema.WidgetNote, "便签", "正文", "", out string error)!;
		Assert.Empty(error);
		Assert.False(string.IsNullOrEmpty(widget.Id));
		doc.Layout.Widgets.Add(widget);

		// 新定义默认不加到任何页面
		Assert.Empty(doc.Layout.Pages["page"].Blocks);

		WidgetDef second = WidgetLibrary.CreateFor(doc, LayoutSchema.WidgetClock, "时钟", "", "", out _)!;
		Assert.NotEqual(widget.Id, second.Id);
	}

	// ————— 改标题 / 改参数 —————

	[Fact]
	public void RenameAndUpdateSettingsKeepUnknownKeysAndRejectDangerousTypes()
	{
		WidgetDef note = NewWidget(LayoutSchema.WidgetNote, "旧标题", "旧正文")!;
		note.Settings["custom"] = "keep";
		Assert.True(WidgetLibrary.TryRename(note, "新标题", out _));
		Assert.Equal("新标题", note.Title);
		Assert.True(WidgetLibrary.TryUpdateSettings(note, "新正文", "", out _));
		Assert.Equal("新正文", WidgetCommon.Str(note.Settings, "text", ""));
		Assert.Equal("keep", WidgetCommon.Str(note.Settings, "custom", ""));

		WidgetDef divider = NewWidget(LayoutSchema.WidgetDivider, "隔断")!;
		divider.Settings["extra"] = "1";
		Assert.True(WidgetLibrary.TryUpdateSettings(divider, "随便", "", out _));
		Assert.Equal("1", WidgetCommon.Str(divider.Settings, "extra", ""));

		WidgetDef dangerous = new WidgetDef { Id = "w-web", Type = LayoutSchema.WidgetWeb, Title = "网页" };
		dangerous.Settings["enabled"] = true;
		Assert.False(WidgetLibrary.TryUpdateSettings(dangerous, "https://example.com", "", out string error));
		Assert.False(string.IsNullOrWhiteSpace(error));
		Assert.True(WidgetLibrary.TryRename(dangerous, "改个名", out _)); // 标题仍可改
	}

	// ————— 引用统计 / 删除 —————

	[Fact]
	public void CountReferencesCountsPagesIncludingVariants()
	{
		LayoutV2 doc = new LayoutV2();
		doc.Layout.Widgets.Add(new WidgetDef { Id = "w1", Type = LayoutSchema.WidgetNote, Title = "便签" });
		doc.Layout.Pages["a"] = PageWith("hero", "custom:w1");
		doc.Layout.Pages["b"] = PageWith("custom:w1", "custom:w2");
		doc.Layout.Pages["c"] = PageWith("hero");
		doc.Layout.Variants["narrow"] = new LayoutBody
		{
			Pages = new Dictionary<string, PageLayoutV2>(StringComparer.Ordinal)
			{
				["a"] = PageWith("custom:w1")
			}
		};
		Assert.Equal(3, WidgetLibrary.CountReferences(doc, "w1"));
		Assert.Equal(1, WidgetLibrary.CountReferences(doc, "w2"));
		Assert.Equal(0, WidgetLibrary.CountReferences(doc, "missing"));
	}

	[Fact]
	public void DeleteRemovesDefinitionAndAllPageReferences()
	{
		LayoutV2 doc = new LayoutV2();
		doc.Layout.Widgets.Add(new WidgetDef { Id = "w1", Type = LayoutSchema.WidgetNote, Title = "便签" });
		doc.Layout.Widgets.Add(new WidgetDef { Id = "w2", Type = LayoutSchema.WidgetClock, Title = "时钟" });
		doc.Layout.Pages["a"] = PageWith("hero", "custom:w1");
		doc.Layout.Pages["b"] = PageWith("custom:w1");
		doc.Layout.Variants["narrow"] = new LayoutBody
		{
			Pages = new Dictionary<string, PageLayoutV2>(StringComparer.Ordinal)
			{
				["a"] = PageWith("custom:w1")
			}
		};

		int removed = WidgetLibrary.Delete(doc, "w1");
		Assert.Equal(3, removed);
		Assert.Single(doc.Layout.Widgets);
		Assert.Equal("w2", doc.Layout.Widgets[0].Id);
		Assert.DoesNotContain(doc.Layout.Pages["a"].Blocks, (BlockLayout b) => b.Id == "custom:w1");
		Assert.Contains(doc.Layout.Pages["a"].Blocks, (BlockLayout b) => b.Id == "hero");
		Assert.Empty(doc.Layout.Pages["b"].Blocks);
		Assert.Empty(doc.Layout.Variants["narrow"].Pages["a"].Blocks);
		Assert.Equal(0, WidgetLibrary.CountReferences(doc, "w1"));
	}

	[Fact]
	public void DeleteReportsMissingDefinition()
	{
		LayoutV2 doc = DocWith("custom:missing");
		Assert.Equal(-1, WidgetLibrary.Delete(doc, "missing"));
		// 没有定义就不动页面(别误删悬空引用)
		Assert.Single(doc.Layout.Pages["page"].Blocks);
	}

	// ————— B:追加到本页末尾(复用粘贴规则) —————

	[Fact]
	public void AppendImplicitAddsCustomBlockAtEndAndDedupes()
	{
		LayoutEditSession session = new LayoutEditSession();
		PageLayoutV2 page = PageWith("hero", "list");
		List<LayoutBlockRef> refs = new List<LayoutBlockRef>
		{
			new LayoutBlockRef("hero", true),
			new LayoutBlockRef("list", true)
		};

		Assert.Equal(1, session.AppendImplicit(page, "custom:w1", refs));
		Assert.Equal(3, page.Blocks.Count);
		Assert.Equal("custom:w1", page.Blocks[2].Id);
		Assert.Equal(2, page.Blocks[2].Z);

		// 同页重复追加不产生重复块
		Assert.Equal(0, session.AppendImplicit(page, "custom:w1", refs));
		Assert.Equal(3, page.Blocks.Count);
	}

	[Fact]
	public void AppendImplicitRejectsBuiltinButAllowsPluginNamespace()
	{
		LayoutEditSession session = new LayoutEditSession();
		PageLayoutV2 page = PageWith("hero");
		List<LayoutBlockRef> refs = new List<LayoutBlockRef> { new LayoutBlockRef("hero", true) };

		Assert.Equal(0, session.AppendImplicit(page, "not-a-page-block", refs));
		Assert.Single(page.Blocks);

		Assert.Equal(1, session.AppendImplicit(page, "plugin:demo:card", refs));
		Assert.Equal("plugin:demo:card", page.Blocks[1].Id);
	}

	[Fact]
	public void AppendImplicitDoesNotClobberClipboard()
	{
		LayoutEditSession session = new LayoutEditSession();
		PageLayoutV2 source = PageWith("hero");
		Assert.Equal(1, session.CopyFrom(source, true));
		Assert.Equal(1, session.ClipboardCount);

		PageLayoutV2 target = PageWith();
		Assert.Equal(1, session.AppendImplicit(target, "custom:w1", null));
		// 剪贴板仍是复制的那一块:粘到另一页能粘出 hero
		Assert.Equal(1, session.ClipboardCount);
		PageLayoutV2 other = PageWith();
		Assert.Equal(1, session.PasteInto(other, new List<LayoutBlockRef> { new LayoutBlockRef("hero", true) }));
		Assert.Equal("hero", other.Blocks[0].Id);
	}

	[Fact]
	public void PlaceAppendedBlockStartsNewFullWidthRowInGridAndSmallCardOnCanvas()
	{
		PageLayoutV2 grid = PageWith("hero", "list");
		grid.Mode = LayoutSchema.ModeGrid;
		grid.Cols = 12;
		grid.Blocks[0].Y = 0.0;
		grid.Blocks[0].H = 1.0;
		grid.Blocks[1].Y = 2.0;
		grid.Blocks[1].H = 1.0;
		BlockLayout appended = new BlockLayout { Id = "custom:w1", Y = 0.0, W = 1.0 };
		grid.Blocks.Add(appended);
		LayoutEngine.PlaceAppendedBlock(grid, appended);
		Assert.Equal(0.0, appended.X);
		Assert.Equal(3.0, appended.Y);
		Assert.Equal(12.0, appended.W);
		Assert.Equal(12, appended.Span);

		PageLayoutV2 canvas = PageWith();
		canvas.Mode = LayoutSchema.ModeCanvas;
		BlockLayout small = new BlockLayout { Id = "custom:w2" };
		canvas.Blocks.Add(small);
		LayoutEngine.PlaceAppendedBlock(canvas, small);
		Assert.Equal(0.4, small.W);
		Assert.Equal(0.25, small.H);
	}
}