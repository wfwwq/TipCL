// R32-MED:自定义布局 v2「编辑器层(L2 编辑体验)」单元测试,mac(Avalonia)树落地件。
//   底稿(逐行) = Linux 同层落地件 wt-linux-layout @7267d03 的
//                 tests/TipCL.Tests/Layout/LayoutEditorTests.cs(1174 行;正文实测 [Fact] 38 条 = A 组
//                 R32McLayoutEditorTests 24 条纯模型 + B 组 R32McLayoutEditorUiTests 14 条真控件树/真指针)。
//                 计数口径说明:R32-PLAN 记的「39 条」是把底稿第 1 行注释里的字面量 [Fact] 也 grep 进去
//                 了;底稿正文只有 38 条,本文件逐方法名 1:1 对齐(见 progress/R32-MED.md §3)。
//   行为真源    = Windows 冻结参考(layout-shell-hooks @ fa1b3d7);但 Windows 测试宿主是 net8,
//                 WPF 控件碰不得,所以 B 组那层 Windows 侧本来就没有单测(只能靠 --shot 与手测),
//                 这类覆盖由 Linux 底稿 / 本文件补上。
//
// 与 Linux 底稿的机械差异(断言 / 方法名 / 数字一字未改):
//   T-1 文件头:Linux 底稿的中文说明与 using 段 -> 本头注与 mac 的 using 段。
//   T-2 namespace:底稿无 namespace(全局,Linux 树单程序集约定)=> mac 的 namespace TipCL.Tests;
//       (TipCL.Tests.csproj 的 RootNamespace=TipCL.Tests,与本目录既有测试一致)。
//   T-3 using 增减:去掉底稿的 TipCL.App.Pages.Layout / TipCL.Core.Layout / TipCL.Tests.App;
//       加 TipCL.Pages。v1 模型 PageLayoutDoc / BlockState 在 mac 树位于 TipCL.Pages
//       (src/TipCL.App/Pages/PageBlock.cs,与 Linux 的 TipCL.Core.Layout 同源逐字实现)=> using TipCL.Core.Layout 不搬;
//       TipCL.Core.Launcher.Layout(LayoutV2 / PageLayoutV2 / BlockLayout / LayoutSchema /
//       LayoutStoreV2)保留;Avalonia.* 与 Xunit 原样保留。
//   T-4 无头会话门面:TestApp.Run( -> AvaloniaSession.Run(mac 的无头会话类,同测试程序集
//       AvaloniaSession.cs;Linux 侧对等物 = tests/TipCL.Tests/App/TestApp.cs 的 TestApp)。
//   T-5 v1 注入缝:LinuxV1Bridge -> R32McV1Bridge —— mac 树没有 Linux 的生产接线 PagesLayoutV1Bridge
//       那一层,改用同测试程序集里的等价实现(R32McLayoutV2Fixtures.cs:20 的 internal sealed class
//       R32McV1Bridge;语义逐条照抄 Linux 的 LayoutV1BridgeLinux.cs / LinuxV1Bridge)。
//   T-6 临时目录:Path.GetTempPath() -> TestHome.Root(mac 红线:测试临时文件必须落在仓库内,
//       R32-BRIEF §4;仍各自拼 Guid 子目录,隔离语义与实际路径长度无关)。
//   T-7 类名:加包号前缀 R32Mc,方法名 / 断言 / 数字一字未改。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using TipCL.Core.Launcher.Layout;
using TipCL.Pages;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// A 组:L2 编辑体验测试(纯模型层)。编辑交互的"判定"全部在 LayoutEditSession / LayoutGuides 这些
/// 零 UI 的纯类型里,UI 侧的 LayoutEditor 只是把鼠标/键盘事件翻译成对它们的调用 —— B 组负责那层。
/// </summary>
public class R32McLayoutEditorTests
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

	/// <summary>
	/// [R32-LED 新增] 契约书 §2.10-1「待实测」项:Avalonia 的 <c>Key.ToString()</c> 字符串是否就是
	/// <see cref="LayoutEditSession.KeyOf"/> 认的那套名字。这条在 Windows 侧无从测(那边是 WPF 的 Key),
	/// Linux 侧必须实测 —— 它一旦漂移,编辑态键盘会整体失效(而且是静默失效)。
	/// </summary>
	[Fact]
	public void AvaloniaKeyNamesMatchTheKeyOfContract()
	{
		Assert.Equal("Escape", Key.Escape.ToString());
		Assert.Equal("Delete", Key.Delete.ToString());
		Assert.Equal("Back", Key.Back.ToString());
		Assert.Equal("Left", Key.Left.ToString());
		Assert.Equal("Right", Key.Right.ToString());
		Assert.Equal("Up", Key.Up.ToString());
		Assert.Equal("Down", Key.Down.ToString());
		Assert.Equal("Z", Key.Z.ToString());
		Assert.Equal("Y", Key.Y.ToString());
		Assert.Equal("C", Key.C.ToString());
		Assert.Equal("V", Key.V.ToString());
		// 真的按同一套名字走一遍映射(而不是只比字符串)
		Assert.Equal(LayoutKey.Escape, LayoutEditSession.KeyOf(Key.Escape.ToString(), false, false));
		Assert.Equal(LayoutKey.Delete, LayoutEditSession.KeyOf(Key.Delete.ToString(), false, false));
		Assert.Equal(LayoutKey.Delete, LayoutEditSession.KeyOf(Key.Back.ToString(), false, false));
		Assert.Equal(LayoutKey.Left, LayoutEditSession.KeyOf(Key.Left.ToString(), false, false));
		Assert.Equal(LayoutKey.Right, LayoutEditSession.KeyOf(Key.Right.ToString(), false, false));
		Assert.Equal(LayoutKey.Up, LayoutEditSession.KeyOf(Key.Up.ToString(), false, false));
		Assert.Equal(LayoutKey.Down, LayoutEditSession.KeyOf(Key.Down.ToString(), false, false));
		Assert.Equal(LayoutKey.Undo, LayoutEditSession.KeyOf(Key.Z.ToString(), true, false));
		Assert.Equal(LayoutKey.Redo, LayoutEditSession.KeyOf(Key.Y.ToString(), true, false));
		Assert.Equal(LayoutKey.Copy, LayoutEditSession.KeyOf(Key.C.ToString(), true, false));
		Assert.Equal(LayoutKey.Paste, LayoutEditSession.KeyOf(Key.V.ToString(), true, false));
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

	private readonly string _tmp = Path.Combine(TestHome.Root, "tipcl_l2_editor_tests_" + Guid.NewGuid().ToString("N"));

	private void UseDir()
	{
		LayoutStoreV2.DirOverride = _tmp;
		LayoutStoreV2.V1Bridge = new R32McV1Bridge();   // 与 App 启动同装配:v1 走 PageLayoutStore 语义
		PageLayoutStore.DirOverride = _tmp;
		LayoutStoreV2.ResetCache();
		// [移植差异] Windows 这里是 PageLayoutStore.Reload();Linux 无该 API ⇒ 走注入缝的缓存刷新
		LayoutStoreV2.RefreshV1Cache();
	}

	private void Release()
	{
		LayoutStoreV2.DirOverride = null;
		LayoutStoreV2.V1Bridge = null;
		PageLayoutStore.DirOverride = null;
		LayoutStoreV2.ResetCache();
		LayoutStoreV2.RefreshV1Cache();
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
			LayoutStoreV2.RefreshV1Cache();

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

/// <summary>
/// B 组(R32-LED 新增):真控件树 + 真指针事件。
/// Windows 侧这层完全没有单测(测试宿主是 net8,WPF 控件碰不得),只能靠 --shot 与手测;
/// Linux 侧可以在 Avalonia 无头会话里造一棵真窗口,用 HeadlessWindowExtensions.MouseDown/MouseMove/MouseUp
/// 走真实的命中测试 → PointerPressed/PointerMoved/PointerReleased(含指针捕获),于是**手势本身**可以被钉住。
/// 所有用例都在 AvaloniaSession.Run 的无头 UI 线程上跑(仓库既有约定)。
/// </summary>
public class R32McLayoutEditorUiTests
{
	// ————— 夹具 —————

	/// <summary>每个用例一个隔离数据目录(绝不碰真实用户目录),并重置全局变体宿主。</summary>
	private sealed class Store : IDisposable
	{
		public string Dir { get; } = Path.Combine(TestHome.Root, "tipcl_led_ui_" + Guid.NewGuid().ToString("N"));

		public Store()
		{
			Directory.CreateDirectory(Dir);
			LayoutStoreV2.DirOverride = Dir;
			LayoutStoreV2.V1Bridge = new R32McV1Bridge();
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

		public Host(string pageId, PageLayoutV2? defaultPage, params BlockSpec[] specs)
		{
			Editor = new LayoutEditor(pageId, specs, new Thickness(0.0), defaultPage);
			Window = new Window
			{
				Width = 900.0,
				Height = 600.0,
				Content = Editor.Root
			};
			Window.Show();
			Window.UpdateLayout();
		}

		/// <summary>进编辑态 + 跑一次布局 + **真出一帧**。
		/// 为什么必须出帧:无头会话里指针命中测试走渲染器(Avalonia 的 renderer hit test),
		/// 没渲染过就什么都命不中(实测:不出帧时 InputHitTest 返回 null,真喂 MouseDown 也无人接收)。</summary>
		public void EnterEdit()
		{
			Editor.EnterEdit();
			Window.UpdateLayout();
			Window.CaptureRenderedFrame();
		}

		public void Dispose()
		{
			Window.Close();
		}
	}

	private static BlockSpec Spec(string id, string title)
	{
		return new BlockSpec
		{
			Id = id,
			Title = title,
			Content = new TextBlock
			{
				Text = title
			}
		};
	}

	private static PageLayoutV2 GridPage(int cols, params (string Id, int X, int Y, int W, int H)[] blocks)
	{
		PageLayoutV2 page = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeGrid,
			Cols = cols
		};
		int z = 0;
		foreach ((string id, int x, int y, int w, int h) in blocks)
		{
			page.Blocks.Add(new BlockLayout
			{
				Id = id,
				X = x,
				Y = y,
				W = w,
				H = h,
				Z = z++
			});
		}
		return page;
	}

	private static PageLayoutV2 StackPage(params string[] ids)
	{
		PageLayoutV2 page = new PageLayoutV2
		{
			Mode = LayoutSchema.ModeStack
		};
		foreach (string id in ids)
		{
			page.Blocks.Add(new BlockLayout
			{
				Id = id
			});
		}
		return page;
	}

	private static T? Find<T>(Visual? root, Func<T, bool> match) where T : Visual
	{
		if (root == null)
		{
			return null;
		}
		if (root is T typed && match(typed))
		{
			return typed;
		}
		foreach (Visual child in root.GetVisualChildren())
		{
			T? hit = Find(child, match);
			if (hit != null)
			{
				return hit;
			}
		}
		return null;
	}

	private static Visual? Ancestor<T>(Visual node) where T : Visual
	{
		Visual? current = node.GetVisualParent();
		while (current != null)
		{
			if (current is T typed)
			{
				return typed;
			}
			current = current.GetVisualParent();
		}
		return null;
	}

	private static LayoutCard Card(Host host, string id)
	{
		return Find<LayoutCard>(host.Editor.Root, (LayoutCard c) => c.Spec.Id == id)
			?? throw new InvalidOperationException("找不到卡片 " + id + ";可视树 = " + Dump(host.Editor.Root, 0));
	}

	private static string Dump(Visual node, int depth)
	{
		string line = "\n" + new string(' ', depth * 2) + node.GetType().Name + (node is Control c && c.Tag != null ? "[Tag=" + c.Tag + "]" : "");
		if (depth > 6)
		{
			return line;
		}
		foreach (Visual child in node.GetVisualChildren())
		{
			line += Dump(child, depth + 1);
		}
		return line;
	}

	private static Border Zone(LayoutCard card, string name)
	{
		return Find<Border>(card, (Border b) => (b.Tag as string) == name && b.IsVisible)
			?? throw new InvalidOperationException("找不到拖拽热区 " + name);
	}

	private static Point CenterInWindow(Host host, Visual control)
	{
		return CenterInWindow(host, control, 0.5, 0.5);
	}

	private static Point CenterInWindow(Host host, Visual control, double fractionX, double fractionY)
	{
		Point local = new Point(control.Bounds.Width * fractionX, control.Bounds.Height * fractionY);
		return control.TranslatePoint(local, host.Window) ?? throw new InvalidOperationException("控件不在窗口可视树里");
	}

	private static double ColumnStep(Host host)
	{
		GridCanvasPanel panel = Find<GridCanvasPanel>(host.Editor.Root, (GridCanvasPanel p) => true)
			?? throw new InvalidOperationException("找不到画布 Panel");
		return panel.ColumnWidthValue + panel.Gap;
	}

	/// <summary>真按下一路移动到松开(两段移动,保证越过拖动阈值且最后落在目标点)。
	/// 按钮状态必须由 RawInputModifiers 带进去(否则 e.GetCurrentPoint().Properties.IsLeftButtonPressed 是 false)。</summary>
	private static void DragPointer(Host host, Visual source, Point fromLocal, double dx, double dy)
	{
		host.Window.CaptureRenderedFrame();
		Point start = source.TranslatePoint(fromLocal, host.Window) ?? throw new InvalidOperationException("TranslatePoint 失败");
		host.Window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
		host.Window.MouseMove(new Point(start.X + dx * 0.5, start.Y + dy * 0.5), RawInputModifiers.LeftMouseButton);
		host.Window.MouseMove(new Point(start.X + dx, start.Y + dy), RawInputModifiers.LeftMouseButton);
		host.Window.MouseUp(new Point(start.X + dx, start.Y + dy), MouseButton.Left, RawInputModifiers.None);
		host.Window.UpdateLayout();
	}

	private static Button ButtonWithText(Visual root, string text)
	{
		return Find<Button>(root, (Button b) => Find<TextBlock>(b, (TextBlock t) => t.Text == text) != null)
			?? throw new InvalidOperationException("找不到按钮 " + text);
	}

	// ————— 进入 / 退出编辑态 —————

	[Fact]
	public void EnterEditConvertsStackToTemporaryGridAndExitWithoutTouchingWritesNothing()
	{
		using Store store = new Store();
		AvaloniaSession.Run(() =>
		{
			using Host host = new Host("led:stack", StackPage("a", "b"), Spec("a", "块甲"), Spec("b", "块乙"));
			Assert.False(host.Editor.IsEditing);
			Assert.Equal(LayoutSchema.ModeStack, host.Editor.State.Page.Mode);

			host.EnterEdit();

			Assert.True(host.Editor.IsEditing);
			// stack 页进编辑态被**临时**折算成 12 列网格(整块才能自由拖)
			Assert.Equal(LayoutSchema.ModeGrid, host.Editor.State.Page.Mode);
			Assert.True(host.Editor.IsAutoGrid);
			Assert.False(File.Exists(store.V2File), "进编辑态不该写盘");

			host.Editor.ExitEdit();

			// 一步没动 → 还原成原来的 stack,且磁盘上零痕迹(R31 规格 §5 第 8 条 / D-01)
			Assert.False(host.Editor.IsEditing);
			Assert.Equal(LayoutSchema.ModeStack, host.Editor.State.Page.Mode);
			Assert.False(host.Editor.IsAutoGrid);
			Assert.False(File.Exists(store.V2File), "没动过就退出不该写盘");
		});
	}

	[Fact]
	public void ExitAfterRealChangePersistsGridLayout()
	{
		using Store store = new Store();
		AvaloniaSession.Run(() =>
		{
			using Host host = new Host("led:stack2", StackPage("a", "b"), Spec("a", "块甲"), Spec("b", "块乙"));
			host.EnterEdit();
			host.Editor.SetBlockHidden("b", true); // 一次真实改动(落盘)
			host.Editor.ExitEdit();

			// 动过 → 退出时保留网格(不再还原成 stack),而且 v2 文件里有这一页
			Assert.Equal(LayoutSchema.ModeGrid, host.Editor.State.Page.Mode);
			Assert.True(File.Exists(store.V2File), "动过就必须落盘");
			LayoutStoreV2.ResetCache();
			LayoutV2 doc = LayoutStoreV2.Load();
			Assert.True(doc.Layout.Pages.ContainsKey("led:stack2"));
			Assert.True(doc.Layout.Pages["led:stack2"].Blocks.First((BlockLayout b) => b.Id == "b").Hidden);
		});
	}

	// ————— 拖动:真指针事件 —————

	[Fact]
	public void DraggingCardOntoNeighbourSwapsColumnsAndPersists()
	{
		using Store store = new Store();
		AvaloniaSession.Run(() =>
		{
			PageLayoutV2 page = GridPage(12, ("a", 0, 0, 4, 1), ("b", 4, 0, 4, 1));
			using Host host = new Host("led:drag", page, Spec("a", "块甲"), Spec("b", "块乙"));
			host.EnterEdit();
			double step = ColumnStep(host);
			Assert.True(step > 0.0, "列宽必须已经算出来(说明画布真的排布过)");

			LayoutCard cardA = Card(host, "a");
			// 按在卡片下半身(避开顶部操作条),往右拖 4 列 → 正好落到 b 的格子
			DragPointer(host, cardA, new Point(cardA.Bounds.Width * 0.5, cardA.Bounds.Height * 0.75), step * 4.0, 0.0);

			PageLayoutV2 after = host.Editor.State.Page;
			BlockLayout a = after.Blocks.First((BlockLayout b) => b.Id == "a");
			BlockLayout b2 = after.Blocks.First((BlockLayout x) => x.Id == "b");
			// 被拖块占住落点(4 列位),让位给它的那块左右互换回到原列 —— 让位 + 左右互换(规格2-问题2.2)
			Assert.Equal(4.0, a.X, 6);
			Assert.Equal(0.0, a.Y, 6);
			Assert.Equal(0.0, b2.X, 6);
			Assert.Equal(0.0, b2.Y, 6);
			// 被拖块优先占住落点:排到块表最前面(PromoteToFront)
			Assert.Equal("a", after.Blocks[0].Id);
			// 真的落盘了
			Assert.True(File.Exists(store.V2File));
			LayoutStoreV2.ResetCache();
			Assert.Equal(4.0, LayoutStoreV2.Load().Layout.Pages["led:drag"].Blocks[0].X, 6);
		});
	}

	[Fact]
	public void DraggingFarRightClampsToLastColumn()
	{
		using Store store = new Store();
		AvaloniaSession.Run(() =>
		{
			PageLayoutV2 page = GridPage(12, ("a", 0, 0, 4, 1), ("b", 8, 0, 4, 1));
			using Host host = new Host("led:clamp", page, Spec("a", "块甲"), Spec("b", "块乙"));
			host.EnterEdit();

			LayoutCard cardA = Card(host, "a");
			DragPointer(host, cardA, new Point(cardA.Bounds.Width * 0.5, cardA.Bounds.Height * 0.75), 4000.0, 0.0);

			BlockLayout a = host.Editor.State.Page.Blocks.First((BlockLayout x) => x.Id == "a");
			// 12 列 - 宽 4 列 → 最右只能到第 8 列(不许被推出画布)
			Assert.Equal(8.0, a.X, 6);
		});
	}

	[Fact]
	public void SingleClickOnCardChangesNothingOnDisk()
	{
		using Store store = new Store();
		AvaloniaSession.Run(() =>
		{
			PageLayoutV2 page = GridPage(12, ("a", 0, 0, 4, 1), ("b", 4, 0, 4, 1));
			using Host host = new Host("led:click", page, Spec("a", "块甲"), Spec("b", "块乙"));
			host.EnterEdit();

			LayoutCard cardA = Card(host, "a");
			Point start = CenterInWindow(host, cardA, 0.5, 0.75);
			host.Window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
			// 挪 1px:没越过拖动距离(4px),只算点了一下选中
			host.Window.MouseMove(new Point(start.X + 1.0, start.Y), RawInputModifiers.LeftMouseButton);
			host.Window.MouseUp(new Point(start.X + 1.0, start.Y), MouseButton.Left, RawInputModifiers.None);

			BlockLayout a = host.Editor.State.Page.Blocks.First((BlockLayout x) => x.Id == "a");
			Assert.Equal(0.0, a.X, 6);
			Assert.False(File.Exists(store.V2File), "只点了一下不该落盘");
		});
	}

	[Fact]
	public void EscapeDuringDragRollsBackWithoutPersisting()
	{
		using Store store = new Store();
		AvaloniaSession.Run(() =>
		{
			PageLayoutV2 page = GridPage(12, ("a", 0, 0, 4, 1), ("b", 4, 0, 4, 1));
			using Host host = new Host("led:esc", page, Spec("a", "块甲"), Spec("b", "块乙"));
			host.EnterEdit();
			double step = ColumnStep(host);

			LayoutCard cardA = Card(host, "a");
			Point start = CenterInWindow(host, cardA, 0.5, 0.75);
			host.Window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
			host.Window.MouseMove(new Point(start.X + step * 4.0, start.Y), RawInputModifiers.LeftMouseButton);
			host.Window.UpdateLayout();
			// 拖到一半按 Esc:取消手势 + 回滚(不算撤销)
			host.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
			host.Window.UpdateLayout();

			BlockLayout a = host.Editor.State.Page.Blocks.First((BlockLayout x) => x.Id == "a");
			Assert.Equal(0.0, a.X, 6);
			Assert.True(host.Editor.IsEditing, "Esc 取消手势时不该顺手退出编辑态");
			Assert.False(File.Exists(store.V2File), "取消的手势不该落盘");

			host.Window.MouseUp(new Point(start.X + step * 4.0, start.Y), MouseButton.Left, RawInputModifiers.None);
		});
	}

	// ————— 缩放:右边整条边 / 下边整条边 —————

	[Fact]
	public void DraggingRightEdgeWidensByColumns()
	{
		using Store store = new Store();
		AvaloniaSession.Run(() =>
		{
			PageLayoutV2 page = GridPage(12, ("a", 0, 0, 2, 1), ("b", 6, 0, 4, 1));
			using Host host = new Host("led:edge", page, Spec("a", "块甲"), Spec("b", "块乙"));
			host.EnterEdit();
			double step = ColumnStep(host);

			LayoutCard cardA = Card(host, "a");
			Border east = Zone(cardA, "e");
			DragPointer(host, east, new Point(east.Bounds.Width * 0.5, east.Bounds.Height * 0.5), step * 2.0, 0.0);

			BlockLayout a = host.Editor.State.Page.Blocks.First((BlockLayout x) => x.Id == "a");
			// 宽 2 列 → 拖 2 列 → 4 列(右边整条边可拖,这是 v2 对齐新增的能力)
			Assert.Equal(4.0, a.W, 6);
			Assert.Equal(0.0, a.X, 6);
			Assert.True(File.Exists(store.V2File));
		});
	}

	[Fact]
	public void DraggingBottomEdgeOnAutoRowPageWritesFixedPixelHeightAndKeepsContent()
	{
		using Store store = new Store();
		AvaloniaSession.Run(() =>
		{
			// row_h = 0 → 自动行高:此时"拖高度"必须落成固定像素高 HeightPx(否则怎么拖都不变)
			PageLayoutV2 page = GridPage(12, ("a", 0, 0, 6, 1), ("b", 0, 2, 6, 1));
			using Host host = new Host("led:fixed", page, Spec("a", "块甲"), Spec("b", "块乙"));
			host.EnterEdit();

			LayoutCard cardA = Card(host, "a");
			TextBlock content = (TextBlock)cardA.Spec.Content!;
			Border south = Zone(cardA, "s");
			DragPointer(host, south, new Point(south.Bounds.Width * 0.5, south.Bounds.Height * 0.5), 0.0, 80.0);

			BlockLayout a = host.Editor.State.Page.Blocks.First((BlockLayout x) => x.Id == "a");
			double px = LayoutCell.ReadHeightPx(a);
			Assert.True(px >= 56.0, "固定像素高必须写进 extra.height_px,实际 = " + px);

			// 固定高度 = 内容进块内滚动容器;**内容必须还在**(移植差异 D-01:Windows 那行会把内容体摘掉)
			Assert.NotNull(Find<ScrollViewer>(cardA, (ScrollViewer s) => s.Content is FitBox));
			Assert.NotNull(content.GetVisualRoot());
			Assert.True(File.Exists(store.V2File));
		});
	}

	// ————— 折叠 / 隐藏 / 托盘 —————

	[Fact]
	public void FoldAllCollapsesCardsWithoutWritingLayout()
	{
		using Store store = new Store();
		AvaloniaSession.Run(() =>
		{
			PageLayoutV2 page = GridPage(12, ("a", 0, 0, 4, 1), ("b", 4, 0, 4, 1));
			using Host host = new Host("led:fold", page, Spec("a", "块甲"), Spec("b", "块乙"));
			host.EnterEdit();

			Button foldAll = ButtonWithText(host.Editor.Root, "全部折叠");
			Action raiseClick = () => foldAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
			raiseClick();

			Assert.True(Card(host, "a").IsFolded);
			Assert.True(Card(host, "b").IsFolded);
			// 折叠只是编辑时的临时视图:不写布局
			Assert.False(File.Exists(store.V2File));

			// 按钮文案随状态翻转,再点一次全部展开
			Assert.NotNull(Find<Button>(host.Editor.Root, (Button b) => Find<TextBlock>(b, (TextBlock t) => t.Text == "全部展开") != null));
			RaiseClick(host, "全部展开");
			Assert.False(Card(host, "a").IsFolded);
		});
	}

	private static void RaiseClick(Host host, string label)
	{
		ButtonWithText(host.Editor.Root, label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
	}

	[Fact]
	public void HideCardThenRestoreFromTrayKeepsOriginalSlot()
	{
		using Store store = new Store();
		AvaloniaSession.Run(() =>
		{
			using Host host = new Host("led:hide", StackPage("a", "b", "c"), Spec("a", "块甲"), Spec("b", "块乙"), Spec("c", "块丙"));
			host.EnterEdit();
			Assert.Equal(new[] { "a", "b", "c" }, host.Editor.State.Order.ToArray());

			// 点中间那张卡的「隐藏 ×」
			LayoutCard cardB = Card(host, "b");
			Button hide = Find<Button>(cardB, (Button b) => ToolTip.GetTip(b) as string == "隐藏")
				?? throw new InvalidOperationException("找不到隐藏按钮");
			hide.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
			host.Window.UpdateLayout();
			Assert.Contains("b", host.Editor.State.Hidden);

			// 底部托盘出现「块乙」chip,点它恢复 → 回到原来的槽位(不是排到末尾)
			Button chip = ButtonWithText(host.Editor.Root, "块乙");
			chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
			host.Window.UpdateLayout();

			Assert.DoesNotContain("b", host.Editor.State.Hidden);
			Assert.Equal(new[] { "a", "b", "c" }, host.Editor.State.Order.ToArray());
		});
	}

	// ————— 工具条 / 重置 / 公开口子 —————

	[Fact]
	public void ToolbarHintIsEllipsizedAndCarriesFullTooltip()
	{
		using Store store = new Store();
		AvaloniaSession.Run(() =>
		{
			const string Hint = "按住区块拖动移动 · 拖右/下边角缩放";
			using Host host = new Host("led:bar", GridPage(12, ("a", 0, 0, 4, 1)), Spec("a", "块甲"));
			host.EnterEdit();

			TextBlock? hint = Find<TextBlock>(host.Editor.Root, (TextBlock t) => t.Text == Hint);
			Assert.NotNull(hint);
			// 宽度不够用省略号截断 + 指针悬停显示完整内容(规格2-问题6)
			Assert.Equal(TextTrimming.CharacterEllipsis, hint!.TextTrimming);
			Assert.Equal(Hint, ToolTip.GetTip(hint) as string);
			// 工具条按钮组齐备
			foreach (string label in new[] { "撤销", "重做", "吸附", "全部折叠", "完成编辑", "重置本页", "导出布局", "导入布局" })
			{
				Assert.NotNull(Find<Button>(host.Editor.Root, (Button b) => Find<TextBlock>(b, (TextBlock t) => t.Text != null && t.Text!.StartsWith(label)) != null));
			}
		});
	}

	[Fact]
	public void ResetPageRestoresFactoryLayoutAndPersists()
	{
		using Store store = new Store();
		AvaloniaSession.Run(() =>
		{
			PageLayoutV2 page = GridPage(12, ("a", 0, 0, 4, 1), ("b", 4, 0, 4, 1));
			using Host host = new Host("led:reset", page, Spec("a", "块甲"), Spec("b", "块乙"));
			host.EnterEdit();
			double step = ColumnStep(host);
			LayoutCard cardA = Card(host, "a");
			DragPointer(host, cardA, new Point(cardA.Bounds.Width * 0.5, cardA.Bounds.Height * 0.75), step * 4.0, 0.0);
			Assert.Equal(4.0, host.Editor.State.Page.Blocks.First((BlockLayout x) => x.Id == "a").X, 6);

			host.Editor.ResetPage();

			// 回到出厂布局(构造第 4 参给的 defaultPage)
			BlockLayout a = host.Editor.State.Page.Blocks.First((BlockLayout x) => x.Id == "a");
			Assert.Equal(0.0, a.X, 6);
			Assert.Equal(4.0, a.W, 6);
			LayoutStoreV2.ResetCache();
			Assert.Equal(0.0, LayoutStoreV2.Load().Layout.Pages["led:reset"].Blocks[0].X, 6);
		});
	}

	[Fact]
	public void SetBlockHiddenAndMutatePagePersistWithoutEnteringEditMode()
	{
		using Store store = new Store();
		AvaloniaSession.Run(() =>
		{
			PageLayoutV2 page = GridPage(12, ("a", 0, 0, 4, 1), ("b", 4, 0, 4, 1));
			using Host host = new Host("led:api", page, Spec("a", "块甲"), Spec("b", "块乙"));

			host.Editor.SetBlockHidden("b", true);
			Assert.Contains("b", host.Editor.State.Hidden);

			host.Editor.MutatePage((PageLayoutV2 p) =>
			{
				p.Blocks.First((BlockLayout b) => b.Id == "a").X = 6;
			});
			Assert.Equal(6.0, host.Editor.State.Page.Blocks.First((BlockLayout b) => b.Id == "a").X, 6);

			LayoutStoreV2.ResetCache();
			PageLayoutV2 stored = LayoutStoreV2.Load().Layout.Pages["led:api"];
			Assert.True(stored.Blocks.First((BlockLayout b) => b.Id == "b").Hidden);
			Assert.Equal(6.0, stored.Blocks.First((BlockLayout b) => b.Id == "a").X, 6);
		});
	}

	[Fact]
	public void MissingCustomBlockFallsBackToUnavailablePlaceholder()
	{
		using Store store = new Store();
		AvaloniaSession.Run(() =>
		{
			LayoutContentRegistry.Clear();
			using Host host = new Host("led:custom", StackPage("custom:w-none"), Spec("a", "块甲"));
			// 布局文档里的 custom: 块没有对应 spec、也没有内容工厂 ⇒ 「区块不可用」占位
			Assert.NotNull(Find<TextBlock>(host.Editor.Root, (TextBlock t) => t.Text == "区块不可用"));
		});
	}
}
