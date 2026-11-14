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
/// L2 编辑体验测试(纯模型层):多选集合、撤销/重做快照栈、剪贴板、键盘意图映射、方向键微调、
/// 对齐辅助线(阈值 4px 磁性吸附)。
/// 为什么能这么测:编辑交互的"判定"全部在 LayoutEditSession / LayoutGuides 这些零 WPF 的纯类型里,
/// WPF 侧的 LayoutEditor 只是把鼠标/键盘事件翻译成对它们的调用;测试宿主是 net8,不能实例化 WPF 控件,
/// 所以**手势本身**(真的拖动鼠标)由 --shot 人工证据覆盖(见交付报告),单测钉住的是手势背后的规则。
/// </summary>
public class LayoutEditorTests
{
	private static PageLayoutV2 Page(params string[] ids)
	{
		PageLayoutV2 page = new PageLayoutV2();
		foreach (string id in ids)
		{
			page.Blocks.Add(new BlockLayout
			{
				Id = id,
				Z = page.Blocks.Count
			});
		}
		return page;
	}

	private static PageLayoutV2 GridPage(int cols, params string[] ids)
	{
		PageLayoutV2 page = Page(ids);
		page.Mode = LayoutSchema.ModeGrid;
		page.Cols = cols;
		return page;
	}

	private static PageLayoutV2 CanvasPage(params string[] ids)
	{
		PageLayoutV2 page = Page(ids);
		page.Mode = LayoutSchema.ModeCanvas;
		foreach (BlockLayout block in page.Blocks)
		{
			block.W = 0.5;
			block.H = 0.5;
		}
		return page;
	}

	private static List<LayoutBlockRef> Refs(params string[] ids)
	{
		List<LayoutBlockRef> list = new List<LayoutBlockRef>();
		foreach (string id in ids)
		{
			list.Add(new LayoutBlockRef(id, true));
		}
		return list;
	}

	private static string[] Ids(PageLayoutV2 page)
	{
		return page.Blocks.Select((BlockLayout b) => b.Id).ToArray();
	}

	// ————— 键盘意图映射(§4.G) —————

	[Fact]
	public void KeyOfMapsContractKeysOnly()
	{
		Assert.Equal(LayoutKey.Undo, LayoutEditSession.KeyOf("Z", true, false));
		Assert.Equal(LayoutKey.Redo, LayoutEditSession.KeyOf("Z", true, true)); // Ctrl+Shift+Z 也是重做
		Assert.Equal(LayoutKey.Redo, LayoutEditSession.KeyOf("Y", true, false));
		Assert.Equal(LayoutKey.Copy, LayoutEditSession.KeyOf("C", true, false));
		Assert.Equal(LayoutKey.Paste, LayoutEditSession.KeyOf("V", true, false));
		Assert.Equal(LayoutKey.Escape, LayoutEditSession.KeyOf("Escape", false, false));
		Assert.Equal(LayoutKey.Delete, LayoutEditSession.KeyOf("Delete", false, false));
		Assert.Equal(LayoutKey.Delete, LayoutEditSession.KeyOf("Back", false, false)); // 笔记本的退格键
		Assert.Equal(LayoutKey.Left, LayoutEditSession.KeyOf("Left", false, false));
		Assert.Equal(LayoutKey.Right, LayoutEditSession.KeyOf("Right", false, false));
		Assert.Equal(LayoutKey.Up, LayoutEditSession.KeyOf("Up", false, false));
		Assert.Equal(LayoutKey.Down, LayoutEditSession.KeyOf("Down", false, false));
		// 不认的键一个都不动作(避免抢页面自己的快捷键)
		Assert.Equal(LayoutKey.None, LayoutEditSession.KeyOf("Z", false, false));
		Assert.Equal(LayoutKey.None, LayoutEditSession.KeyOf("A", true, false));
		Assert.Equal(LayoutKey.None, LayoutEditSession.KeyOf("Left", true, false));
		Assert.Equal(LayoutKey.None, LayoutEditSession.KeyOf("F5", false, false));
		Assert.Equal(LayoutKey.None, LayoutEditSession.KeyOf("", false, false));
		Assert.Equal(LayoutKey.None, LayoutEditSession.KeyOf(null, false, false));
	}

	// ————— 多选 —————

	[Fact]
	public void SelectionReplacesOrAppends()
	{
		LayoutEditSession session = new LayoutEditSession();
		session.Select("a", false);
		Assert.True(session.IsSelected("a"));
		Assert.Equal(1, session.SelectedCount);
		session.Select("a", true); // 重复追加同一个 id 不会出现两份
		Assert.Equal(1, session.SelectedCount);
		session.Select("b", false); // 不带修饰键 = 单选替换
		Assert.False(session.IsSelected("a"));
		Assert.True(session.IsSelected("b"));
		session.Select("c", true);
		Assert.Equal(2, session.SelectedCount);
		session.Select("", false); // 空 id = 清空
		Assert.Equal(0, session.SelectedCount);
		Assert.False(session.IsSelected(null));
	}

	[Fact]
	public void ToggleAddsAndRemoves()
	{
		LayoutEditSession session = new LayoutEditSession();
		session.Toggle("a");
		Assert.True(session.IsSelected("a"));
		session.Toggle("a");
		Assert.False(session.IsSelected("a"));
		session.Toggle("a");
		session.Toggle(null); // 空 id 不动选择
		Assert.Equal(1, session.SelectedCount);
		session.ClearSelection();
		Assert.Equal(0, session.SelectedCount);
	}

	[Fact]
	public void SelectManyIsUsedByMarqueeAndRetainDropsGhosts()
	{
		LayoutEditSession session = new LayoutEditSession();
		session.SelectMany(new string[5] { "a", "b", "b", null!, "" });
		Assert.Equal(2, session.SelectedCount); // 去重 + 丢空 id
		// 撤销/删除之后,已经不存在的块必须从选择里剔除(否则会留下幽灵 id)
		session.Retain(new string[2] { "b", "c" });
		Assert.True(session.IsSelected("b"));
		Assert.False(session.IsSelected("a"));
		session.Retain(null);
		Assert.Equal(0, session.SelectedCount);
	}

	[Fact]
	public void OrderedSelectionFollowsPageOrder()
	{
		LayoutEditSession session = new LayoutEditSession();
		session.SelectMany(new string[2] { "c", "a" });
		List<string> ordered = session.OrderedSelection(new string[3] { "a", "b", "c" });
		Assert.Equal(new string[2] { "a", "c" }, ordered.ToArray()); // 按页面顺序,而不是点选顺序
		Assert.Empty(session.OrderedSelection(null));
	}

	// ————— 撤销 / 重做(内存快照) —————

	[Fact]
	public void UndoAndRedoSwapSnapshotsDeeply()
	{
		LayoutEditSession session = new LayoutEditSession();
		PageLayoutV2 before = Page("a", "b");
		session.PushUndo(before);
		Assert.True(session.CanUndo);
		Assert.False(session.CanRedo);
		Assert.Equal(1, session.UndoCount);

		PageLayoutV2 after = Page("b", "a");
		PageLayoutV2? back = session.TakeUndo(after);
		Assert.NotNull(back);
		Assert.Equal(new string[2] { "a", "b" }, Ids(back!));
		Assert.False(session.CanUndo);
		Assert.True(session.CanRedo); // 撤销把"当前状态"压进重做栈

		// 栈空时再撤销 = null(调用方弹"没有可撤销的操作")
		Assert.Null(session.TakeUndo(after));

		PageLayoutV2? forward = session.TakeRedo(back);
		Assert.NotNull(forward);
		Assert.Equal(new string[2] { "b", "a" }, Ids(forward!));
		Assert.False(session.CanRedo);
		Assert.True(session.CanUndo);

		// 快照是深拷贝:改返回出去的对象不会污染栈里的历史
		forward!.Blocks[0].Hidden = true;
		PageLayoutV2? again = session.TakeUndo(forward);
		Assert.False(again!.Blocks[0].Hidden);
		Assert.Equal(new string[2] { "a", "b" }, Ids(again));
	}

	[Fact]
	public void NewChangeClearsRedoStack()
	{
		LayoutEditSession session = new LayoutEditSession();
		session.PushUndo(Page("a"));
		session.TakeUndo(Page("b"));
		Assert.True(session.CanRedo);
		session.PushUndo(Page("c")); // 新改动一来,重做历史作废(R6:不做分支历史)
		Assert.False(session.CanRedo);
	}

	[Fact]
	public void UndoStackHonoursCapacityByDroppingOldest()
	{
		LayoutEditSession session = new LayoutEditSession
		{
			Capacity = 2
		};
		session.PushUndo(Page("a"));
		session.PushUndo(Page("b"));
		session.PushUndo(Page("c"));
		Assert.Equal(2, session.UndoCount);
		// 撤销是"从最近一次往前退":最新压进去的是 c(压栈时点的状态),最老的 a 已因超容量被丢掉
		Assert.Equal("c", Ids(session.TakeUndo(null)!)[0]);
		Assert.Equal("b", Ids(session.TakeUndo(null)!)[0]);
		Assert.False(session.CanUndo);
		Assert.Null(session.TakeUndo(null)); // a 真的没了(容量 2 只留最近两份)
	}

	[Fact]
	public void DiscardUndoRollsBackWithoutCountingAsUndo()
	{
		// 拖到一半按 Esc:回滚到起手状态,但不算一次撤销,也不污染重做栈
		LayoutEditSession session = new LayoutEditSession();
		session.PushUndo(Page("a"));
		Assert.True(session.CanUndo);
		PageLayoutV2? snapshot = session.DiscardUndo();
		Assert.NotNull(snapshot);
		Assert.Equal("a", Ids(snapshot!)[0]);
		Assert.False(session.CanUndo);
		Assert.False(session.CanRedo);
		Assert.Equal(0, session.UndoCount);
		Assert.Equal(0, session.RedoCount);
		Assert.Null(session.DiscardUndo());
	}

	[Fact]
	public void ClearHistoryEmptiesBothStacks()
	{
		LayoutEditSession session = new LayoutEditSession();
		session.PushUndo(Page("a"));
		session.TakeUndo(Page("b"));
		session.ClearHistory();
		Assert.False(session.CanUndo);
		Assert.False(session.CanRedo);
	}

	// ————— 复制 / 粘贴 —————

	[Fact]
	public void CopySelectionOrWholePage()
	{
		LayoutEditSession session = new LayoutEditSession();
		PageLayoutV2 page = Page("a", "custom:w-1", "b");
		// 没有选中 → 视为整页(编辑器里 Ctrl+C 的语义)
		Assert.Equal(3, session.CopyFrom(page, false));
		Assert.Equal(3, session.ClipboardCount);
		// 有选中 → 只复制选中的
		session.Select("b", false);
		Assert.Equal(1, session.CopyFrom(page, false));
		Assert.Equal(1, session.ClipboardCount);
		// wholePage = true → 整页
		Assert.Equal(3, session.CopyFrom(page, true));
		Assert.Equal(3, session.ClipboardCount);
		// 源页为空:不清空已有剪贴板
		Assert.Equal(0, session.CopyFrom(null, true));
		Assert.Equal(3, session.ClipboardCount);
		Assert.False(new LayoutEditSession().HasClipboard);
	}

	[Fact]
	public void PasteDeduplicatesAndFiltersUnknownBuiltins()
	{
		LayoutEditSession session = new LayoutEditSession();
		PageLayoutV2 source = Page("a", "b", "ghost");
		session.CopyFrom(source, true);
		PageLayoutV2 target = Page("a");
		// a 已在目标页(重名跳过)、ghost 不在目标页白名单(内置块过滤)、只有 b 真的粘进来
		Assert.Equal(1, session.PasteInto(target, Refs("a", "b")));
		Assert.Equal(new string[2] { "a", "b" }, Ids(target));
		Assert.Equal(1, target.Blocks[1].Z); // 新块接在末尾
		// 再粘一次:按 id 去重 → 不再新增(不造重名块)
		Assert.Equal(0, session.PasteInto(target, Refs("a", "b")));
		Assert.Equal(2, target.Blocks.Count);
		// 空剪贴板 / null 页 → 0
		Assert.Equal(0, new LayoutEditSession().PasteInto(target, Refs("a")));
		Assert.Equal(0, session.PasteInto(null, Refs("a", "b", "ghost")));
	}

	[Fact]
	public void PasteKeepsCustomAndPluginBlocksWithoutWhitelist()
	{
		// custom: / plugin: 是布局文档自己的块(§4.F),不受目标页白名单限制
		LayoutEditSession session = new LayoutEditSession();
		PageLayoutV2 source = Page("custom:w-1", "plugin:hello:farm");
		session.CopyFrom(source, true);
		PageLayoutV2 target = Page("a");
		Assert.Equal(2, session.PasteInto(target, Refs("a")));
		Assert.Equal(new string[3] { "a", "custom:w-1", "plugin:hello:farm" }, Ids(target));
	}

	// ————— 方向键微调 —————

	[Fact]
	public void NudgeGridMovesOneCellAndClampsAtEdges()
	{
		PageLayoutV2 page = GridPage(2, "a", "b");
		Assert.Equal(1, LayoutEditSession.Nudge(page, new string[1] { "a" }, 1, 0, false, 600.0, 400.0, 8.0));
		Assert.Equal(1.0, page.Blocks[0].X, 6);
		// 已经贴右边(列数 2 - 跨度 1 = 最右到第 1 列)再往右 = 不动
		Assert.Equal(0, LayoutEditSession.Nudge(page, new string[1] { "a" }, 1, 0, false, 600.0, 400.0, 8.0));
		Assert.Equal(1.0, page.Blocks[0].X, 6);
		// 上下:向下 +1 行,再向上回到 0
		Assert.Equal(1, LayoutEditSession.Nudge(page, new string[1] { "a" }, 0, 1, false, 600.0, 400.0, 8.0));
		Assert.Equal(1.0, page.Blocks[0].Y, 6);
		Assert.Equal(1, LayoutEditSession.Nudge(page, new string[1] { "a" }, 0, -1, false, 600.0, 400.0, 8.0));
		Assert.Equal(0.0, page.Blocks[0].Y, 6);
		// 没选中的块一个都不动
		Assert.Equal(0.0, page.Blocks[1].X, 6);
		Assert.Equal(0.0, page.Blocks[1].Y, 6);
		// 空选择 / null 页 → 0
		Assert.Equal(0, LayoutEditSession.Nudge(page, new List<string>(), 1, 0, false, 600.0, 400.0, 8.0));
		Assert.Equal(0, LayoutEditSession.Nudge(null, new string[1] { "a" }, 1, 0, false, 600.0, 400.0, 8.0));
	}

	[Fact]
	public void NudgeGridSkipsHiddenBlocks()
	{
		PageLayoutV2 page = GridPage(2, "a");
		page.Blocks[0].Hidden = true;
		Assert.Equal(0, LayoutEditSession.Nudge(page, new string[1] { "a" }, 1, 0, false, 600.0, 400.0, 8.0));
	}

	[Fact]
	public void NudgeShiftTranslatesTenPixelsIntoGridUnits()
	{
		// 设计写"Shift = 10px";grid 模式下位置按格量化,所以把 10px 折算成格数、不足一格按一格。
		// 常规宽度下格子远大于 10px → Shift 与普通方向键同为 1 格;只有极窄画布才会一次走多格。
		PageLayoutV2 wide = GridPage(2, "a");
		Assert.Equal(1, LayoutEditSession.Nudge(wide, new string[1] { "a" }, 1, 0, true, 600.0, 400.0, 8.0));
		Assert.Equal(1.0, wide.Blocks[0].X, 6);

		PageLayoutV2 narrow = GridPage(12, "a");
		// 40px 宽 12 列 ≈ 每格 3.33px → 10px ≈ 3 格
		Assert.Equal(1, LayoutEditSession.Nudge(narrow, new string[1] { "a" }, 1, 0, true, 40.0, 100.0, 8.0));
		Assert.Equal(3.0, narrow.Blocks[0].X, 6);
	}

	[Fact]
	public void NudgeCanvasUsesSnapStepAndShiftUsesTenPixels()
	{
		PageLayoutV2 page = CanvasPage("a");
		// 吸附档 8px + 轴 100px → 一格 = 0.08
		Assert.Equal(1, LayoutEditSession.Nudge(page, new string[1] { "a" }, 1, 0, false, 100.0, 100.0, 8.0));
		Assert.Equal(0.08, page.Blocks[0].X, 6);
		// Shift = 10px → 0.10
		Assert.Equal(1, LayoutEditSession.Nudge(page, new string[1] { "a" }, 1, 0, true, 100.0, 100.0, 8.0));
		Assert.Equal(0.18, page.Blocks[0].X, 6);
		// 吸附档 0(不吸附)时仍有微调:按 1px 走
		PageLayoutV2 free = CanvasPage("a");
		Assert.Equal(1, LayoutEditSession.Nudge(free, new string[1] { "a" }, 0, 1, false, 100.0, 100.0, 0.0));
		Assert.Equal(0.01, free.Blocks[0].Y, 6);
		// 一直往右推到边界:夹在 1 - NW = 0.5(块不许被推出画布)
		for (int i = 0; i < 6; i++)
		{
			LayoutEditSession.Nudge(page, new string[1] { "a" }, 1, 0, false, 100.0, 100.0, 8.0);
		}
		Assert.Equal(0.5, page.Blocks[0].X, 6);
		// 已经贴边再按 → 0(调用方不会误判成"动过了")
		Assert.Equal(0, LayoutEditSession.Nudge(page, new string[1] { "a" }, 1, 0, false, 100.0, 100.0, 8.0));
		Assert.Equal(0.5, page.Blocks[0].X, 6);
	}

	[Fact]
	public void NudgeStackModeDoesNothing()
	{
		// stack(缺省)没有自由坐标:方向键必须完全不动作(否则会改到不该改的字段)
		PageLayoutV2 page = Page("a", "b");
		Assert.Equal(0, LayoutEditSession.Nudge(page, new string[1] { "a" }, 1, 1, false, 600.0, 400.0, 8.0));
		Assert.Equal(0.0, page.Blocks[0].X, 6);
		Assert.Equal(0.0, page.Blocks[0].Y, 6);
	}

	// ————— 对齐辅助线(阈值 4px 磁性吸附) —————

	[Fact]
	public void GuidesSnapToNearestEdgeWithinThreshold()
	{
		LayoutRect moving = new LayoutRect(103.0, 50.0, 100.0, 40.0);
		LayoutRect other = new LayoutRect(100.0, 0.0, 200.0, 200.0);
		LayoutGuideResult result = LayoutGuides.Align(moving, new List<LayoutRect> { other }, LayoutGuides.Threshold);
		Assert.True(result.HasSnap);
		Assert.Equal(-3.0, result.Dx, 6); // 左边界差 3px → 磁性吸附回 100
		Assert.Equal(0.0, result.Dy, 6); // 纵向最近的对齐差 10px > 阈值
		LayoutGuideLine line = Assert.Single(result.Lines);
		Assert.True(line.Vertical);
		Assert.Equal(100.0, line.Position, 6);
	}

	[Fact]
	public void GuidesSnapBothAxesAtOnce()
	{
		LayoutRect moving = new LayoutRect(103.0, 97.0, 100.0, 100.0);
		LayoutRect other = new LayoutRect(100.0, 100.0, 200.0, 200.0);
		LayoutGuideResult result = LayoutGuides.Align(moving, new List<LayoutRect> { other }, LayoutGuides.Threshold);
		Assert.Equal(-3.0, result.Dx, 6);
		Assert.Equal(3.0, result.Dy, 6);
		Assert.Equal(2, result.Lines.Count);
		Assert.Contains(result.Lines, (LayoutGuideLine l) => l.Vertical);
		Assert.Contains(result.Lines, (LayoutGuideLine l) => !l.Vertical);
	}

	[Fact]
	public void GuidesMatchCentersToo()
	{
		// 中线对齐:两块中心 X 相差 2px → 吸到中线
		LayoutRect moving = new LayoutRect(52.0, 400.0, 100.0, 20.0);
		LayoutRect other = new LayoutRect(0.0, 0.0, 200.0, 100.0);
		LayoutGuideResult result = LayoutGuides.Align(moving, new List<LayoutRect> { other }, LayoutGuides.Threshold);
		Assert.Equal(-2.0, result.Dx, 6);
		Assert.Equal(100.0, Assert.Single(result.Lines).Position, 6);
	}

	[Fact]
	public void GuidesDoNothingBeyondThresholdOrWithoutOthers()
	{
		LayoutRect other = new LayoutRect(100.0, 100.0, 200.0, 200.0);
		// 110×50 的块与 other 的四条边/中线最近也差 10px(纵向)与 10px(横向)→ 一条都不吸
		LayoutRect far = new LayoutRect(110.0, 50.0, 50.0, 40.0);
		LayoutGuideResult none = LayoutGuides.Align(far, new List<LayoutRect> { other }, LayoutGuides.Threshold);
		Assert.False(none.HasSnap);
		Assert.Empty(none.Lines);
		Assert.Equal(0.0, none.Dx, 6);
		Assert.Equal(0.0, none.Dy, 6);
		// 没有其它块 / null 列表 → 什么都不吸
		Assert.False(LayoutGuides.Align(far, new List<LayoutRect>(), LayoutGuides.Threshold).HasSnap);
		Assert.False(LayoutGuides.Align(far, null, LayoutGuides.Threshold).HasSnap);
		// threshold ≤ 0 → 回落到契约阈值 4px(而不是"完全不吸附")
		Assert.True(LayoutGuides.Align(new LayoutRect(103.0, 50.0, 50.0, 50.0), new List<LayoutRect> { other }, 0.0).HasSnap);
	}

	// ————— 与持久化的接缝:画布槽位经 v2 落盘,v1 投影仍然冻结 —————

	private readonly string _tmp = Path.Combine(Path.GetTempPath(), "tipcl_l2_editor_tests_" + Guid.NewGuid().ToString("N"));

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

	[Fact]
	public void GridSlotChangesPersistThroughV2AndV1ProjectionStaysFrozen()
	{
		UseDir();
		try
		{
			LayoutV2 seed = LayoutStoreV2.ParseImport("""
			{
			  "version": 2,
			  "kind": "tipcl-layout",
			  "layout": {
			    "shell": { "density": "compact" },
			    "pages": {
			      "tasks": { "version": 2, "mode": "grid", "cols": 2, "blocks": [{ "id": "header" }, { "id": "list" }] }
			    }
			  }
			}
			""");
			LayoutStoreV2.Save(seed);

			// 等价一次画布拖拽结束时的写回:槽位 → 页面块 → v2 → 写通 v1
			LayoutV2 doc = LayoutStoreV2.Load();
			PageLayoutV2 page = doc.Layout.Pages["tasks"];
			Assert.Equal(LayoutSchema.ModeGrid, page.Mode);
			List<LayoutCell> cells = LayoutGrid.Pack(page);
			Assert.Equal(2, cells.Count);
			Assert.Equal(new string[2] { "header", "list" }, cells.Select((LayoutCell c) => c.Id).ToArray());
			Assert.Equal(0, cells[0].X);
			Assert.Equal(1, cells[1].X);
			cells[1].X = 0;
			cells[0].X = 1;
			LayoutGrid.Apply(page, cells);
			LayoutStoreV2.Save(doc);
			PageLayoutStore.Reload();

			// v2 是真源:网格坐标与全局密度都在
			LayoutV2 after = LayoutStoreV2.Load();
			Assert.Equal(1.0, after.Layout.Pages["tasks"].Blocks[0].X, 6);
			Assert.Equal(0.0, after.Layout.Pages["tasks"].Blocks[1].X, 6);
			Assert.Equal(0.0, after.Layout.Pages["tasks"].Blocks[0].Y, 6);
			Assert.Equal(0.0, after.Layout.Pages["tasks"].Blocks[1].Y, 6);
			Assert.Equal(LayoutSchema.DensityCompact, after.Layout.Shell.Density);

			// 冻结的 v1 解析器读回:顺序/显隐语义没变,且文件里**一个字节的新字段都没有**
			PageLayoutDoc? v1 = PageLayoutStore.Load("tasks");
			Assert.NotNull(v1);
			Assert.Equal(new string[2] { "header", "list" }, v1!.Blocks.Select((BlockState b) => b.Id).ToArray());
			string raw = File.ReadAllText(Path.Combine(_tmp, "ui_page_layouts.json"));
			Assert.DoesNotContain("\"x\"", raw);
			Assert.DoesNotContain("\"y\"", raw);
			Assert.DoesNotContain("\"mode\"", raw);
			Assert.DoesNotContain("\"density\"", raw);
			Assert.Contains("\"version\": 1", raw);
			Assert.Contains("\"kind\": \"tipcl-page-layout\"", raw);
		}
		finally
		{
			Release();
		}
	}
}
