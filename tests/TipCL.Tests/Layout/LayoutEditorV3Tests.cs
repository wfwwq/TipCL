// R35 lin-l12:布局 v3 编辑态(GOAL-layout-v3 §5.1 / §5.2 / §6.3「WP-L12」)的新增用例。
//   A 组 LayoutEditorV3RuleTests —— 纯规则:区块显示标题(自建块取 widget title / 无 title 取类型名 / 插件块取插件标题 /
//                                   任何分支都不返回块 id)、「添加」抽屉的类型清单与「已添加」判定、网页 / 按钮全局开关钩子。
//   B 组 LayoutEditorV3UiTests   —— Avalonia 无头会话:编辑条上没有「吸附」「自动靠拢」「全部折叠」;显示文本里没有块 id;
//                                   撤销 / 重做按钮随历史栈置灰;Esc 先关抽屉再退出;抽屉里「显示」后闪光晕;
//                                   网格把手(右边缘 + 下边缘)只在选中时出现;拖动时淡色列底纹;拖右边缘受 MinCols 限制。
// 约束(主 agent 2026-11-20 补充):本文件的用例**不点**任何会打开浏览器 / 文件管理器 / 外部程序的按钮
// (不开 ⋯ 菜单、不点导出 / 导入、不点「去开启」),只调编辑器自身的内部入口或纯函数。
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
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.App.Pages.Layout;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;
using TipCL.Core.Layout;
using TipCL.Tests.App;
using Xunit;

/// <summary>A 组:纯规则(不需要 UI 线程)。</summary>
public class LayoutEditorV3RuleTests
{
	private static Func<string, WidgetDef?> Widgets(params WidgetDef[] defs)
	{
		return delegate(string blockId)
		{
			string? id = LayoutEngine.WidgetIdOf(blockId);
			return defs.FirstOrDefault((WidgetDef d) => d.Id == id);
		};
	}

	[Fact]
	public void CustomBlockTitleIsWidgetTitle()
	{
		WidgetDef note = new WidgetDef { Id = "w_note1", Type = LayoutSchema.WidgetNote, Title = "联机便签" };
		string title = LayoutBlockTitles.Resolve("custom:w_note1", null, Widgets(note), null);
		Assert.Equal("联机便签", title);
	}

	[Fact]
	public void CustomBlockWithoutTitleUsesTypeName()
	{
		WidgetDef clock = new WidgetDef { Id = "w_clock1", Type = LayoutSchema.WidgetClock, Title = "" };
		Assert.Equal(WidgetUI.TypeLabel(LayoutSchema.WidgetClock), LayoutBlockTitles.Resolve("custom:w_clock1", null, Widgets(clock), null));
		WidgetDef note = new WidgetDef { Id = "w_note2", Type = LayoutSchema.WidgetNote, Title = "   " };
		Assert.Equal(WidgetUI.TypeLabel(LayoutSchema.WidgetNote), LayoutBlockTitles.Resolve("custom:w_note2", null, Widgets(note), null));
	}

	[Fact]
	public void PluginBlockUsesPluginTitle()
	{
		string title = LayoutBlockTitles.Resolve("plugin:demo:stats", null, null, (string plugin, string inner) => (plugin == "demo" && inner == "stats") ? "服务器状态" : null);
		Assert.Equal("服务器状态", title);
	}

	[Fact]
	public void SpecTitleWinsUnlessItIsTheId()
	{
		Assert.Equal("筛选", LayoutBlockTitles.Resolve("filter", "筛选", null, null));
		// 页面 spec 的标题恰好就是 id(老代码的兜底写法)⇒ 不算标题
		Assert.NotEqual("filter", LayoutBlockTitles.Resolve("filter", "filter", null, null));
	}

	[Theory]
	[InlineData("custom:w_note1")]
	[InlineData("custom:w-gone")]
	[InlineData("plugin:demo:stats")]
	[InlineData("plugin:x:y")]
	[InlineData("filter")]
	[InlineData("download:list")]
	public void DisplayTextNeverContainsTheBlockId(string blockId)
	{
		// 各种「拿不到标题」的情况:定义缺失、插件不给标题 / 给的就是 id、插件回调抛异常
		WidgetDef selfNamed = new WidgetDef { Id = "w_note1", Type = LayoutSchema.WidgetNote, Title = "custom:w_note1" };
		List<string> titles = new List<string>
		{
			LayoutBlockTitles.Resolve(blockId, null, null, null),
			LayoutBlockTitles.Resolve(blockId, blockId, null, null),
			LayoutBlockTitles.Resolve(blockId, null, Widgets(selfNamed), (string p, string i) => blockId),
			LayoutBlockTitles.Resolve(blockId, null, (string _) => throw new InvalidOperationException(), (string p, string i) => throw new InvalidOperationException()),
			LayoutBlockTitles.Resolve(blockId, null, null, (string p, string i) => i)
		};
		foreach (string title in titles)
		{
			Assert.False(string.IsNullOrWhiteSpace(title));
			Assert.False(LayoutBlockTitles.ContainsId(title, blockId), "显示文本「" + title + "」里出现了块 id " + blockId);
			Assert.DoesNotContain("custom:", title);
			Assert.DoesNotContain("plugin:", title);
		}
	}

	[Fact]
	public void LabelSuffixOnlyForCustomWidgets()
	{
		Assert.Equal(I18nCore.Tr("· 组件"), LayoutBlockTitles.LabelSuffix("custom:w_note1"));
		Assert.Equal("", LayoutBlockTitles.LabelSuffix("filter"));
		Assert.Equal("", LayoutBlockTitles.LabelSuffix("plugin:demo:stats"));
	}

	[Fact]
	public void DrawerTypesHideWebAndButtonUntilTheGlobalSwitchIsOn()
	{
		List<string> off = LayoutAddDrawer.Types(false);
		Assert.Equal(WidgetRegistry.SafeTypes, off.ToArray());
		Assert.DoesNotContain(LayoutSchema.WidgetWeb, off);
		Assert.DoesNotContain(LayoutSchema.WidgetButton, off);
		List<string> on = LayoutAddDrawer.Types(true);
		Assert.Equal(LayoutSchema.WidgetWeb, on[on.Count - 2]);
		Assert.Equal(LayoutSchema.WidgetButton, on[on.Count - 1]);
	}

	[Fact]
	public void DangerHookFailuresCountAsOff()
	{
		Func<bool> saved = LayoutAddDrawer.DangerWidgetsAllowed;
		try
		{
			LayoutAddDrawer.DangerWidgetsAllowed = () => throw new InvalidOperationException();
			Assert.False(LayoutAddDrawer.IsDangerAllowed());
			LayoutAddDrawer.DangerWidgetsAllowed = null!;
			Assert.False(LayoutAddDrawer.IsDangerAllowed());
			LayoutAddDrawer.DangerWidgetsAllowed = () => true;
			Assert.True(LayoutAddDrawer.IsDangerAllowed());
			Assert.Contains(LayoutSchema.WidgetWeb, LayoutAddDrawer.Types());
		}
		finally
		{
			LayoutAddDrawer.DangerWidgetsAllowed = saved;
		}
	}

	[Fact]
	public void SingletonTypesShowAddedOnceOnPage()
	{
		string?[] onPage = { LayoutSchema.WidgetClock, LayoutSchema.WidgetNote, LayoutSchema.WidgetWeb };
		Assert.True(LayoutAddDrawer.IsAdded(LayoutSchema.WidgetClock, onPage));
		Assert.False(LayoutAddDrawer.IsAdded(LayoutSchema.WidgetShortcut, onPage));
		// 效果图 WT 表里 multi:true 的类型(便签 / 分隔线 / 网页 / 按钮)可以重复加
		Assert.False(LayoutAddDrawer.IsAdded(LayoutSchema.WidgetNote, onPage));
		Assert.False(LayoutAddDrawer.IsAdded(LayoutSchema.WidgetWeb, onPage));
		Assert.True(LayoutAddDrawer.AllowsMultiple(LayoutSchema.WidgetDivider));
		Assert.False(LayoutAddDrawer.AllowsMultiple(LayoutSchema.WidgetPlaytime));
		Assert.Equal(316.0, LayoutAddDrawer.DrawerWidth);
		foreach (string type in LayoutAddDrawer.Types(true))
		{
			Assert.False(string.IsNullOrEmpty(LayoutAddDrawer.DescriptionOf(type)), type + " 缺一句说明");
		}
	}

	[Fact]
	public void AutoPackToggleIsGoneFromTheEditor()
	{
		// §5.2:「自动靠拢」界面删除,编辑器里不再有切换它的入口
		Assert.Null(typeof(LayoutEditor).GetMethod("ToggleAutoPack", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public));
	}
}

/// <summary>B 组:无头 UI(TestApp.Run 的 UI 线程)。</summary>
public class LayoutEditorV3UiTests
{
	private sealed class Store : IDisposable
	{
		public string Dir { get; } = Path.Combine(Path.GetTempPath(), "tipcl_led_v3_" + Guid.NewGuid().ToString("N"));

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

	private static BlockSpec Spec(string id, string title, int minCols = 0)
	{
		return new BlockSpec
		{
			Id = id,
			Title = title,
			MinCols = minCols,
			Content = new TextBlock { Text = title }
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
			page.Blocks.Add(new BlockLayout { Id = id, X = x, Y = y, W = w, H = h, Z = z++ });
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

	private static List<T> All<T>(Visual root) where T : Visual
	{
		return root.GetVisualDescendants().OfType<T>().ToList();
	}

	private static LayoutCard Card(Host host, string id)
	{
		return Find<LayoutCard>(host.Editor.Root, (LayoutCard c) => c.Spec.Id == id) ?? throw new InvalidOperationException("找不到卡片 " + id);
	}

	private static double ColumnStep(Host host)
	{
		GridCanvasPanel panel = Find<GridCanvasPanel>(host.Editor.Root, (GridCanvasPanel p) => true) ?? throw new InvalidOperationException("找不到画布 Panel");
		return panel.ColumnWidthValue + panel.Gap;
	}

	private static void ClickCard(Host host, LayoutCard card)
	{
		host.Window.CaptureRenderedFrame();
		Point start = card.TranslatePoint(new Point(card.Bounds.Width * 0.5, card.Bounds.Height * 0.75), host.Window) ?? throw new InvalidOperationException();
		host.Window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
		host.Window.MouseUp(start, MouseButton.Left, RawInputModifiers.None);
		host.Window.UpdateLayout();
	}

	private static void SaveWidget(WidgetDef def, string pageId, PageLayoutV2 page)
	{
		LayoutV2 doc = LayoutStoreV2.Load();
		doc.Layout.Widgets.Add(def);
		LayoutEngine.ApplyPage(doc, pageId, page);
		LayoutStoreV2.Save(doc);
		LayoutStoreV2.ResetCache();
	}

	[Fact]
	public void EditBarHasNoSnapAutoPackOrFoldAllAndNoBlockIds()
	{
		using Store store = new Store();
		TestApp.Run(() =>
		{
			using Host host = new Host("led3:bar", GridPage(12, ("a", 0, 0, 4, 1), ("b", 4, 0, 4, 1)), Spec("a", "块甲"), Spec("b", "块乙"));
			host.EnterEdit();
			List<string> texts = host.Editor.EditBarTexts();
			Assert.Contains("完成", texts);
			foreach (string text in texts)
			{
				Assert.False(text.StartsWith("吸附", StringComparison.Ordinal), "编辑条上还有「" + text + "」");
				Assert.DoesNotContain("自动靠拢", text);
				Assert.DoesNotContain("全部折叠", text);
				Assert.DoesNotContain("全部展开", text);
				Assert.NotEqual("a", text);
				Assert.NotEqual("b", text);
				Assert.DoesNotContain("led3:", text);
			}
			// 整个编辑器的可见文字里也没有「自动靠拢」
			Assert.DoesNotContain(All<TextBlock>(host.Editor.Root), (TextBlock t) => (t.Text ?? "").Contains("自动靠拢", StringComparison.Ordinal));
		});
	}

	[Fact]
	public void CustomBlockLabelShowsWidgetTitleWithComponentSuffixAndNeverTheId()
	{
		using Store store = new Store();
		TestApp.Run(() =>
		{
			const string PageId = "led3:custom";
			PageLayoutV2 page = GridPage(12, ("a", 0, 0, 4, 1), ("custom:w_note1", 4, 0, 4, 1), ("custom:w_clock9", 8, 0, 4, 1));
			LayoutV2 doc = LayoutStoreV2.Load();
			doc.Layout.Widgets.Add(new WidgetDef { Id = "w_clock9", Type = LayoutSchema.WidgetClock, Title = "" });
			LayoutStoreV2.Save(doc);
			LayoutStoreV2.ResetCache();
			SaveWidget(new WidgetDef { Id = "w_note1", Type = LayoutSchema.WidgetNote, Title = "联机便签" }, PageId, page);
			using Host host = new Host(PageId, null, Spec("a", "块甲"));
			host.EnterEdit();

			LayoutCard note = Card(host, "custom:w_note1");
			Assert.Equal("联机便签", note.Spec.Title);
			Assert.NotNull(Find<TextBlock>(note, (TextBlock t) => t.Text == I18nCore.Tr("· 组件")));
			Assert.Equal(WidgetUI.TypeLabel(LayoutSchema.WidgetClock), Card(host, "custom:w_clock9").Spec.Title);
			// 内置块没有「· 组件」后缀
			Assert.Null(Find<TextBlock>(Card(host, "a"), (TextBlock t) => t.Text == I18nCore.Tr("· 组件")));
			// 任何显示文本里都不出现块 id
			foreach (TextBlock text in All<TextBlock>(host.Editor.Root))
			{
				string value = text.Text ?? "";
				Assert.DoesNotContain("w_note1", value);
				Assert.DoesNotContain("w_clock9", value);
				Assert.DoesNotContain("custom:", value);
			}
		});
	}

	[Fact]
	public void UndoRedoButtonsFollowTheHistoryStack()
	{
		using Store store = new Store();
		TestApp.Run(() =>
		{
			using Host host = new Host("led3:undo", GridPage(12, ("a", 0, 0, 4, 1), ("b", 4, 0, 4, 1)), Spec("a", "块甲"), Spec("b", "块乙"));
			host.EnterEdit();
			Button undo = Find<Button>(host.Editor.Root, (Button b) => ToolTip.GetTip(b) as string == I18nCore.Tr("撤销 (Ctrl+Z)")) ?? throw new InvalidOperationException("找不到撤销按钮");
			Button redo = Find<Button>(host.Editor.Root, (Button b) => ToolTip.GetTip(b) as string == I18nCore.Tr("重做 (Ctrl+Y)")) ?? throw new InvalidOperationException("找不到重做按钮");
			Assert.False(undo.IsEnabled);
			Assert.False(redo.IsEnabled);

			host.Editor.HideBlock(Card(host, "b").Spec);
			host.Window.UpdateLayout();
			Assert.True(undo.IsEnabled);
			Assert.False(redo.IsEnabled);

			undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
			host.Window.UpdateLayout();
			Assert.DoesNotContain("b", host.Editor.State.Hidden);
			Assert.False(undo.IsEnabled);
			Assert.True(redo.IsEnabled);
		});
	}

	[Fact]
	public void EscapeClosesTheDrawerFirstThenLeavesEditMode()
	{
		using Store store = new Store();
		TestApp.Run(() =>
		{
			using Host host = new Host("led3:esc", GridPage(12, ("a", 0, 0, 4, 1), ("b", 4, 0, 4, 1)), Spec("a", "块甲"), Spec("b", "块乙"));
			host.EnterEdit();
			host.Editor.ShowDrawer(true);
			host.Window.UpdateLayout();
			Assert.True(host.Editor.DrawerOpen);
			host.Editor.Root.Focus();

			host.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
			host.Window.UpdateLayout();
			Assert.False(host.Editor.DrawerOpen);
			Assert.True(host.Editor.IsEditing, "抽屉开着时 Esc 只关抽屉");

			host.Editor.Root.Focus();
			host.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
			host.Window.UpdateLayout();
			Assert.False(host.Editor.IsEditing);
		});
	}

	[Fact]
	public void DrawerShowsDashedEmptyStateAndRestoringFlashesTheBlock()
	{
		using Store store = new Store();
		TestApp.Run(() =>
		{
			using Host host = new Host("led3:drawer", GridPage(12, ("a", 0, 0, 4, 1), ("b", 4, 0, 4, 1)), Spec("a", "块甲"), Spec("b", "块乙"));
			host.EnterEdit();
			host.Editor.ShowDrawer(true);
			host.Window.UpdateLayout();
			// 没有隐藏项:虚线空状态框
			TextBlock empty = Find<TextBlock>(host.Editor.Root, (TextBlock t) => t.Text == I18nCore.Tr("没有隐藏的内容")) ?? throw new InvalidOperationException("没有空状态");
			Assert.NotNull(Find<Avalonia.Controls.Shapes.Rectangle>(empty.GetVisualParent(), (Avalonia.Controls.Shapes.Rectangle r) => r.StrokeDashArray != null && r.StrokeDashArray.Count > 0));
			// 抽屉宽 316,右侧停靠
			Border drawer = Find<Border>(host.Editor.Root, (Border b) => Math.Abs(b.Width - 316.0) < 0.01) ?? throw new InvalidOperationException("找不到抽屉");
			Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, drawer.HorizontalAlignment);

			host.Editor.HideBlock(Card(host, "b").Spec);
			host.Window.UpdateLayout();
			Button row = Find<Button>(host.Editor.Root, (Button b) => (Avalonia.Automation.AutomationProperties.GetName(b) ?? "") == I18nCore.Tr("显示") + " 块乙") ?? throw new InvalidOperationException("抽屉里找不到「显示 块乙」");
			row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
			host.Window.UpdateLayout();
			Assert.DoesNotContain("b", host.Editor.State.Hidden);
			Assert.Equal("b", host.Editor.LastFlashedBlock);
			Dispatcher.UIThread.RunJobs();
			Assert.True(Card(host, "b").IsFlashing, "找回的块要闪一下主题色光晕");
		});
	}

	[Fact]
	public void GridHandlesRightAndBottomAppearOnlyWhenSelected()
	{
		using Store store = new Store();
		TestApp.Run(() =>
		{
			using Host host = new Host("led3:handles", GridPage(12, ("a", 0, 0, 4, 1), ("b", 4, 0, 4, 1)), Spec("a", "块甲"), Spec("b", "块乙"));
			host.EnterEdit();
			LayoutCard card = Card(host, "b");
			Func<Border, bool> right = (Border x) => Math.Abs(x.Width - 10.0) < 0.01 && Math.Abs(x.Height - 36.0) < 0.01;
			Func<Border, bool> bottom = (Border x) => Math.Abs(x.Width - 36.0) < 0.01 && Math.Abs(x.Height - 10.0) < 0.01;
			Assert.False(Find<Border>(card, right)!.IsVisible);
			Assert.False(Find<Border>(card, bottom)!.IsVisible);

			ClickCard(host, card);
			Assert.True(Find<Border>(card, right)!.IsVisible, "选中后右边缘把手要出现");
			Assert.True(Find<Border>(card, bottom)!.IsVisible, "选中后下边缘把手要出现(Q3)");
		});
	}

	[Fact]
	public void DraggingShowsColumnShadeAndHidesItOnRelease()
	{
		using Store store = new Store();
		TestApp.Run(() =>
		{
			using Host host = new Host("led3:shade", GridPage(12, ("a", 0, 0, 4, 1), ("b", 4, 0, 4, 1)), Spec("a", "块甲"), Spec("b", "块乙"));
			host.EnterEdit();
			double step = ColumnStep(host);
			LayoutCard card = Card(host, "a");
			host.Window.CaptureRenderedFrame();
			Point start = card.TranslatePoint(new Point(card.Bounds.Width * 0.5, card.Bounds.Height * 0.75), host.Window) ?? throw new InvalidOperationException();
			Assert.False(host.Editor.ColumnShadeVisible);
			host.Window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
			host.Window.MouseMove(new Point(start.X + step, start.Y), RawInputModifiers.LeftMouseButton);
			host.Window.MouseMove(new Point(start.X + step * 2.0, start.Y), RawInputModifiers.LeftMouseButton);
			host.Window.UpdateLayout();
			Assert.True(host.Editor.ColumnShadeVisible, "拖动时要显示淡色列底纹");
			host.Window.MouseUp(new Point(start.X + step * 2.0, start.Y), MouseButton.Left, RawInputModifiers.None);
			host.Window.UpdateLayout();
			Assert.False(host.Editor.ColumnShadeVisible);
		});
	}

	[Fact]
	public void DraggingRightEdgeRespectsTheBlocksMinCols()
	{
		using Store store = new Store();
		TestApp.Run(() =>
		{
			using Host host = new Host("led3:mincols", GridPage(12, ("a", 0, 0, 8, 1), ("b", 0, 1, 4, 1)), Spec("a", "块甲", minCols: 6), Spec("b", "块乙"));
			host.EnterEdit();
			double step = ColumnStep(host);
			LayoutCard card = Card(host, "a");
			Border east = Find<Border>(card, (Border b) => (b.Tag as string) == "e" && b.IsVisible) ?? throw new InvalidOperationException("找不到右边缘热区");
			host.Window.CaptureRenderedFrame();
			Point start = east.TranslatePoint(new Point(east.Bounds.Width * 0.5, east.Bounds.Height * 0.5), host.Window) ?? throw new InvalidOperationException();
			host.Window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
			host.Window.MouseMove(new Point(start.X - step * 3.0, start.Y), RawInputModifiers.LeftMouseButton);
			host.Window.MouseMove(new Point(start.X - step * 6.0, start.Y), RawInputModifiers.LeftMouseButton);
			host.Window.MouseUp(new Point(start.X - step * 6.0, start.Y), MouseButton.Left, RawInputModifiers.None);
			host.Window.UpdateLayout();
			BlockLayout a = host.Editor.State.Page.Blocks.First((BlockLayout x) => x.Id == "a");
			// 8 列往左拖 6 列 → 被块自己的 MinCols = 6 挡住(按整列,不是 2)
			Assert.Equal(6.0, a.W, 6);
		});
	}

	[Fact]
	public void LaunchPageUsesWhiteTranslucentOutlines()
	{
		using Store store = new Store();
		TestApp.Run(() =>
		{
			Color dark = ((ISolidColorBrush)LayoutCard.OutlineBrush(dark: true, hover: false)).Color;
			Assert.Equal(255, dark.R);
			Assert.Equal(255, dark.G);
			Assert.True(dark.A > 0 && dark.A < 255, "深色表面的虚线是白色半透明");
			Color hover = ((ISolidColorBrush)LayoutCard.OutlineBrush(dark: true, hover: true)).Color;
			Assert.True(hover.A > dark.A, "悬停加深");
			using Host launch = new Host("launch", GridPage(12, ("a", 0, 0, 6, 1), ("b", 6, 0, 6, 1)), Spec("a", "块甲"), Spec("b", "块乙"));
			Assert.True(launch.Editor.IsDarkSurface);
			using Host other = new Host("led3:light", GridPage(12, ("a", 0, 0, 6, 1), ("b", 6, 0, 6, 1)), Spec("a", "块甲"), Spec("b", "块乙"));
			Assert.False(other.Editor.IsDarkSurface);
		});
	}
}
