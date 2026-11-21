// R32-MUT-FIX:补上 R32-MUT-LIN 在 @1823c3c 上量出来的四个**测试覆盖空洞**。
//
// 上游证据(progress/R32-MUT-LIN.md §2 表 + §4 覆盖空洞分析):
//   M11 `App.axaml.cs:34` 的 `WidgetRegistry.EnsureRegistered()` 整行注释掉 ⇒ 全量 670 条**全绿**;
//   M12 `LayoutEditor.cs:912` 的 `LayoutContentRegistry.Build(...)` 改为 null ⇒ 全量**全绿**
//       (套件里只有反向断言「没工厂时显示『区块不可用』」,缺「注册了工厂 → 内容真渲染」正向路径);
//   M13 `LayoutEditor.cs:1676` `MinBlockPx 56 → 20` ⇒ 全绿(既有用例只断言 `px >= 56`);
//   M14 `BasicWidgets.cs:37` 便签字号 `12.5 → 99` ⇒ 全绿(部件"外观"零断言);
//   M15 `LayoutEditor.cs:1681` `MinCols 2 → 1` ⇒ 全绿(现有用例的块宽 2/4/6 列,永远碰不到下限)。
//
// 本文件的每条用例都按「**能被对应变异杀红**」设计:
//   * M13 / M15 断言**精确下界**(56.0 / 2 列),不是 `>0` 或 `>=56`;
//   * M14 断言便签正文 TextBlock 的 **FontSize == 12.5**(从真实可视树里按正文取,不是"存在某元素");
//   * M12 断言**注册表内容真的进了编辑器的可视树**(唯一 marker 文本存在 + 「区块不可用」占位不存在);
//   * M11 两条:一条**真跑生产初始化方法** `App.OnFrameworkInitializationCompleted()` 后断言
//     `WidgetRegistry.Registered == True`(这是 MUT-LIN 记录的「真实 lifetime 路径」在**进程内**的
//     等价物:变异就是把这个方法体里那唯一一行删掉);另一条照既有 `I18nLanguageWiringTests` 的 idiom
//     写源码守卫(未注释的 `EnsureRegistered()` 且必须在 lifetime 判断之前),两条互为冗余。
//
// 稳定性代价(如实写在这里,也写进报告):
//   M11 的运行时用例走的是无头会话里**已经装好的那个 App 实例**,直接调用它自己的生产方法:
//     - 它要求 `Application.Current` 就是 `TipCL.App.App`(测试会话由 TestApp.BuildAvaloniaApp 保证);
//     - 它要求会话没装 classic desktop lifetime(装了就会真造 MainWindow)—— 用例里前置断言卡住这一条;
//     - 它会跑 `I18nCore.InitLanguage()` 与 `EnsureRegistered()`,故 finally 里恢复
//       `I18nCore.CurrentLang` 与注册表状态;xUnit 在本程序集已全局 `DisableTestParallelization`,
//       不存在并发污染。
//   代价是「比其他纯模型用例多一层对 Avalonia 会话形状的依赖」;收益是它**真的执行生产代码**,
//   而不是像源码守卫那样只读文本(注释掉、改成 `if (false)`、挪进 lifetime 分支,运行时用例照样红)。
//
// 夹具隔离:每个用例自带独立临时数据根(LayoutStoreV2.DirOverride),不碰真实用户目录;
// 碰 Avalonia 的用例体一律包在 TestApp.Run(...) 里(线程亲和 + 事件派发),与 R32-LED 的既有 idiom 一致。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using TipCL.App.Pages.Layout;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;
using TipCL.Core.Layout;
using TipCL.Tests.App;
using Xunit;

namespace TipCL.Tests.MutCoverage;

[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class R32MutCoverageTests
{
	// ————————————————————————— 夹具 —————————————————————————

	/// <summary>每个用例一个隔离数据目录(绝不碰真实用户目录),并重置全局变体宿主。</summary>
	private sealed class Store : IDisposable
	{
		public string Dir { get; } = Path.Combine(Path.GetTempPath(), "tipcl_mutfix_" + Guid.NewGuid().ToString("N"));

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

		/// <summary>进编辑态 + 跑一次布局 + **真出一帧**(无头会话里命中测试要靠渲染器,见 R32-LED 夹具注释)。</summary>
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

	private static double ColumnStep(Host host)
	{
		GridCanvasPanel panel = Find<GridCanvasPanel>(host.Editor.Root, (GridCanvasPanel p) => true)
			?? throw new InvalidOperationException("找不到画布 Panel");
		return panel.ColumnWidthValue + panel.Gap;
	}

	/// <summary>真按下一路移动到松开(两段移动,保证越过拖动阈值且最后落在目标点)。</summary>
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

	/// <summary>从测试输出目录往上找源码树里的文件。找不到返回 null(与 I18nLanguageWiringTests 同一写法)。</summary>
	private static string? FindRepoFile(string relative)
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		for (int i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
		{
			string candidate = Path.Combine(dir.FullName, relative);
			if (File.Exists(candidate)) return candidate;
		}
		return null;
	}

	// ————————————————————————— 探针内容工厂(M12 专用) —————————————————————————

	/// <summary>只认领一个块 id 的最小工厂:返回一段带唯一 marker 的文本控件。</summary>
	private sealed class ProbeProvider : ILayoutContentProvider
	{
		private readonly string _blockId;

		private readonly string _marker;

		public ProbeProvider(string blockId, string marker)
		{
			_blockId = blockId;
			_marker = marker;
		}

		public bool CanBuild(string blockId)
		{
			return string.Equals(blockId, _blockId, StringComparison.Ordinal);
		}

		public object? Build(string blockId, string pageId)
		{
			return new TextBlock
			{
				Text = _marker
			};
		}
	}

	// ═════════════════ M11:装配守卫(App 初始化必须真的把注册表插上电) ═════════════════

	/// <summary>
	/// **强版本**:在进程内真跑生产初始化方法,断言 `WidgetRegistry.Registered == True`。
	///
	/// 为什么这能杀 M11:MUT-LIN 的变异就是把 `App.axaml.cs:34` 那一行
	/// `TipCL.App.Pages.Layout.WidgetRegistry.EnsureRegistered();` 整行注释掉 —— 只要本用例调用
	/// 这个方法体,注册表就再也不会被插电,断言必然红。它比源码守卫强的地方:注释掉、改成
	/// `if (false)`、挪进 `if (ApplicationLifetime is ...)` 分支(生产上仍会跑,但无头路径不跑)、
	/// 甚至改成调用别的重载,运行时都能分辨;源码守卫只认"有未注释的那一行"。
	///
	/// 为什么可以在这里调:无头测试会话里的 `Application.Current` 就是这个 `TipCL.App.App` 实例,
	/// 而会话没装 classic desktop lifetime ⇒ 方法体只走「公共段」(InitLanguage + EnsureRegistered),
	/// 不建窗口、不注入 V1Bridge、不弹首启向导。
	/// </summary>
	[Fact]
	public void AppFrameworkInitialization_RegistersWidgetRegistry_OnTheProductionStartupMethod()
	{
		TestApp.Run(() =>
		{
			Application? current = Application.Current;
			Assert.True(current is TipCL.App.App,
				"测试会话里的 Application 不是 TipCL.App.App,实际 = "
				+ (current == null ? "(null)" : current.GetType().FullName));

			// 前置条件:无头会话不该装 classic desktop lifetime —— 装了就会真造 MainWindow,
			// 这条用例就不能直接调生产初始化方法(它会在 if 分支里 new MainWindow())。
			Assert.False(current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime,
				"无头测试会话装了 classic desktop lifetime(实际 = "
				+ (current.ApplicationLifetime == null ? "(null)" : current.ApplicationLifetime.GetType().FullName)
				+ "),本用例不能直接调 OnFrameworkInitializationCompleted()");

			bool wasRegistered = WidgetRegistry.Registered;
			string prevLang = I18nCore.CurrentLang;
			// 先把注册表摘成"没插电"状态:否则 EnsureRegistered() 幂等返回,断言没有分辨力。
			WidgetRegistry.Unregister();
			try
			{
				Assert.False(WidgetRegistry.Registered, "前置条件:WidgetRegistry 应先被摘掉");

				((TipCL.App.App)current).OnFrameworkInitializationCompleted();

				Assert.True(WidgetRegistry.Registered,
					"App.OnFrameworkInitializationCompleted() 跑完后 WidgetRegistry 仍未插上电 —— "
					+ "src/TipCL.App/App.axaml.cs 里 WidgetRegistry.EnsureRegistered() 那一行"
					+ "(R32-LINT 提交 1823c3c 的接线)被删掉/注释掉/挪进 lifetime 分支了。"
					+ "后果:自建块 custom:<id> 与插件块 plugin:... 在真实窗口里永远显示「区块不可用」。");
				Assert.True(LayoutContentRegistry.Count >= 1,
					"注册表里一个内容工厂都没有(Count = " + LayoutContentRegistry.Count + ")");
			}
			finally
			{
				if (!wasRegistered)
				{
					WidgetRegistry.Unregister();
				}
				else if (!WidgetRegistry.Registered)
				{
					WidgetRegistry.EnsureRegistered();
				}
				I18nCore.CurrentLang = prevLang;
			}
		});
	}

	/// <summary>
	/// **源码守卫**(与 `I18nLanguageWiringTests.AppStartupPath_CallsInitLanguage_OutsideTheLifetimeBranch`
	/// 同一个 idiom):只看真正的代码行,断言 `WidgetRegistry.EnsureRegistered()` 存在且未注释、且在
	/// lifetime 判断之前。它不依赖 Avalonia 会话形状、失败信息直接指向源码行,是上面那条运行时用例的廉价冗余
	/// (但**不能**替代它:源码里有未注释的一行、初始化方法却没被调用,只有运行时用例能分辨)。
	/// </summary>
	[Fact]
	public void AppStartupSource_CallsEnsureRegistered_OutsideTheLifetimeBranch()
	{
		string? path = FindRepoFile(Path.Combine("src", "TipCL.App", "App.axaml.cs"));
		Assert.True(path != null,
			"找不到 src/TipCL.App/App.axaml.cs(本用例需要源码树位于测试输出目录的上级)");

		string[] lines = File.ReadAllLines(path!);

		// 只看真正的代码行:被注释掉的 EnsureRegistered() 不算接线。
		int callAt = Array.FindIndex(lines, l =>
		{
			string t = l.Trim();
			return !t.StartsWith("//", StringComparison.Ordinal)
				&& !t.StartsWith("*", StringComparison.Ordinal)
				&& t.Contains("WidgetRegistry.EnsureRegistered()", StringComparison.Ordinal);
		});
		int lifetimeAt = Array.FindIndex(lines, l =>
			!l.Trim().StartsWith("//", StringComparison.Ordinal)
			&& l.Contains("is IClassicDesktopStyleApplicationLifetime", StringComparison.Ordinal));

		Assert.True(callAt >= 0,
			"App.axaml.cs 里没有 WidgetRegistry.EnsureRegistered() 的**未注释**调用 —— "
			+ "R32-LINT(提交 1823c3c)的部件接线被删掉了,自建块/插件块会重新回到「工厂就绪但没插电」。");
		Assert.True(lifetimeAt >= 0, "App.axaml.cs 里找不到 lifetime 判断,本用例需要跟着改");
		Assert.True(callAt < lifetimeAt,
			"EnsureRegistered() 必须在 lifetime 判断**之前**(对齐 Windows 真源 windos/src/TipCL/App.xaml.cs:94),"
			+ "现在它落在判断之后(行号 " + (callAt + 1) + " > " + (lifetimeAt + 1) + ")。");
	}

	// ═════════════════ M12:宿主真的咨询注册表(正向路径) ═════════════════

	/// <summary>
	/// 注册一个内容工厂 → `LayoutEditor` 必须**真的渲染出该内容**,而不是「区块不可用」占位。
	///
	/// MUT-LIN 的 M12 变异把 `LayoutEditor.cs:912` 的 `LayoutContentRegistry.Build(...)` 改成 null:
	/// 此时 marker 文本消失、占位文案出现 ⇒ 本用例两条断言同时红。
	/// 断言的是**实际文本 + 占位文案不存在**,不是"没崩"。
	/// </summary>
	[Fact]
	public void RegisteredContentFactory_IsRenderedInsideTheEditor_ForACustomBlock()
	{
		using Store store = new Store();
		TestApp.Run(() =>
		{
			const string BlockId = "custom:mfc-probe";
			const string Marker = "MUTFIX-PROBE-CONTENT-7a3f";
			// WidgetRegistry 若已在册,它会认领所有非内置块并返回它自己的占位 —— 先摘掉,
			// 让本用例的工厂是唯一认领者;finally 里按原状态恢复。
			bool wasRegistered = WidgetRegistry.Registered;
			if (wasRegistered)
			{
				WidgetRegistry.Unregister();
			}
			ProbeProvider probe = new ProbeProvider(BlockId, Marker);
			LayoutContentRegistry.Register(probe);
			try
			{
				// 不给 BlockSpec(即块没有内置内容)⇒ ResolveContent 必须走 LayoutContentRegistry。
				using Host host = new Host("mfc:custom", GridPage(12, (BlockId, 0, 0, 6, 1)));
				host.Window.CaptureRenderedFrame();

				TextBlock? rendered = Find<TextBlock>(host.Editor.Root, (TextBlock t) => t.Text == Marker);
				Assert.True(rendered != null,
					"注册表里的内容没有被渲染进编辑器:找不到文本 " + Marker
					+ ";可视树文本 = " + Texts(host.Editor.Root));

				// 反向:不能是「区块不可用」占位(那正是 M12 变异后的样子)。
				string unavailable = I18nCore.Tr("区块不可用");
				Assert.Null(Find<TextBlock>(host.Editor.Root, (TextBlock t) => t.Text == unavailable));
				Assert.False(File.Exists(store.V2File), "只渲染不该写盘");
			}
			finally
			{
				LayoutContentRegistry.Unregister(probe);
				if (wasRegistered)
				{
					WidgetRegistry.EnsureRegistered();
				}
			}
		});
	}

	/// <summary>把可视树里的文本全收集出来(失败信息用;不做断言)。</summary>
	private static string Texts(Visual? root)
	{
		List<string> texts = new List<string>();
		Collect(root, texts);
		return "[" + string.Join(" | ", texts) + "]";
	}

	private static void Collect(Visual? node, List<string> texts)
	{
		if (node == null)
		{
			return;
		}
		if (node is TextBlock tb && !string.IsNullOrEmpty(tb.Text))
		{
			texts.Add(tb.Text!);
		}
		foreach (Visual child in node.GetVisualChildren())
		{
			Collect(child, texts);
		}
	}

	// ═════════════════ M13:缩放的最小像素高 = 56(精确) ═════════════════

	/// <summary>
	/// 把块的「下边」一路往上拖到极小,固定像素高必须**精确停在 56.0**。
	///
	/// 既有用例 `LayoutEditorUiTests.DraggingBottomEdgeOnAutoRowPageWritesFixedPixelHeightAndKeepsContent`
	/// 只断言 `px >= 56`,对 `MinBlockPx 56 → 20` 没有分辨力(MUT-LIN M13 存活的原因)。
	/// 这里改拖"往上"、且位移足以越过下限 ⇒ 结果只可能等于 `MinBlockPx`,断言精确值。
	/// 拖拽距离按卡片实际高度算(`-(H + 20)`):起点位移后 `startH + dy == -20 < 56`,
	/// 无论卡片多高都会走到 clamp 分支,不依赖行高猜测。
	/// </summary>
	[Fact]
	public void ResizingBlockHeightBelowMinimum_ClampsToExactly56Px()
	{
		using Store store = new Store();
		TestApp.Run(() =>
		{
			// 块放在第 2 行:下面那条边的中点离窗口顶部还有余量,往上拖 200px 仍在窗口内。
			PageLayoutV2 page = GridPage(12, ("a", 0, 1, 6, 1), ("b", 0, 3, 6, 1));
			using Host host = new Host("mfc:minh", page, Spec("a", "块甲"), Spec("b", "块乙"));
			host.EnterEdit();

			LayoutCard cardA = Card(host, "a");
			Border south = Zone(cardA, "s");
			double dy = -(cardA.Bounds.Height + 20.0);
			DragPointer(host, south, new Point(south.Bounds.Width * 0.5, south.Bounds.Height * 0.5), 0.0, dy);

			BlockLayout a = host.Editor.State.Page.Blocks.First((BlockLayout x) => x.Id == "a");
			double px = LayoutCell.ReadHeightPx(a);
			Assert.True(px > 0.0, "拖缩后没写进 extra.height_px(实际 = " + px + ")");
			Assert.Equal(56.0, px, 6);
			Assert.True(File.Exists(store.V2File));
		});
	}

	// ═════════════════ M15:最窄列数 = 2(精确) ═════════════════

	/// <summary>
	/// 把块的「右边」往左拖掉一列,宽度必须**精确停在 2 列**(12 列网格的 v2 下限)。
	///
	/// 既有用例的块宽都是 2/4/6 列、且只往宽里拖,永远碰不到 `MinCols` 分支(MUT-LIN M15 存活的原因)。
	/// `MinCols: (cols >= 6) ? 2 : 1` 被改成 `return 1` 后,这里会得到 1 列 ⇒ 断言精确值 2.0 变红。
	/// </summary>
	[Fact]
	public void ShrinkingBlockWidthBelowMinimum_ClampsToExactlyTwoColumns()
	{
		using Store store = new Store();
		TestApp.Run(() =>
		{
			PageLayoutV2 page = GridPage(12, ("a", 0, 0, 2, 1), ("b", 6, 0, 4, 1));
			using Host host = new Host("mfc:mincols", page, Spec("a", "块甲"), Spec("b", "块乙"));
			host.EnterEdit();
			double step = ColumnStep(host);

			LayoutCard cardA = Card(host, "a");
			Border east = Zone(cardA, "e");
			// 左边不动、右边往左拖 1 列:2 列 → 1 列,必须被 MinCols 顶回 2 列。
			DragPointer(host, east, new Point(east.Bounds.Width * 0.5, east.Bounds.Height * 0.5), -1.0 * step, 0.0);

			BlockLayout a = host.Editor.State.Page.Blocks.First((BlockLayout x) => x.Id == "a");
			Assert.Equal(0.0, a.X, 6);
			Assert.Equal(2.0, a.W, 6);
			Assert.True(File.Exists(store.V2File));
		});
	}

	/// <summary>
	/// `MinCols` 的**另一个站点**(`LayoutEditor.cs:497-499`,进编辑态时的压缩对齐):旧存档里被拉到
	/// 1 列的块,进编辑态必须先被放宽到 2 列,否则文字竖排。这里不拖任何东西 —— 只进编辑态。
	///
	/// 为什么单独一条:`MinCols()` 在生产代码里有两处调用(手势缩放 1609 / 编辑态压紧 499),
	/// 上面那条只打手势站点的靶;若将来有人只把 499 那一处改成"不限",手势用例不会红。本条补那一格。
	/// </summary>
	[Fact]
	public void EnteringEditMode_WidensLegacySingleColumnBlockToTwoColumns()
	{
		using Store store = new Store();
		TestApp.Run(() =>
		{
			// 旧存档里的现场:块 a 只有 1 列宽(文字竖排),块 b 正常 4 列。
			PageLayoutV2 page = GridPage(12, ("a", 0, 0, 1, 1), ("b", 5, 0, 4, 1));
			using Host host = new Host("mfc:mincols-enter", page, Spec("a", "块甲"), Spec("b", "块乙"));
			host.EnterEdit();

			BlockLayout a = host.Editor.State.Page.Blocks.First((BlockLayout x) => x.Id == "a");
			Assert.Equal(2.0, a.W, 6);
			// 编辑态只是视图:不乱写盘(这条同时守住「进编辑不许落盘」的既有契约)
			Assert.False(File.Exists(store.V2File));
		});
	}

	// ═════════════════ M14:便签正文的默认字号 = 12.5(精确) ═════════════════

	/// <summary>
	/// 便签部件的正文 TextBlock 必须有 `FontSize == 12.5`。
	///
	/// 走的是生产分派 `WidgetUI.Build`(type=note → `BasicWidgets.Note`),并把结果挂进真窗口,
	/// 再从可视树里按**正文文本**取 TextBlock —— 断言的正是 MUT-LIN M14 改的那个常数
	/// (`BasicWidgets.cs:37` 的 `U.Txt(text, 12.5)`),改成 99 会直接变红。
	/// 既有 `WidgetTests` 只断言文本/结构,对"部件长什么样"零覆盖,这条补的就是那一格。
	/// </summary>
	[Fact]
	public void NoteWidgetBody_KeepsItsDesignedFontSizeOf12Point5()
	{
		TestApp.Run(() =>
		{
			const string Body = "MUTFIX-NOTE-BODY-58c1";
			WidgetDef widget = new WidgetDef
			{
				Id = "mfc-note",
				Type = LayoutSchema.WidgetNote,
				Title = "MUTFIX 便签",
				Settings = new Dictionary<string, object?>
				{
					["text"] = Body
				}
			};

			Control content = WidgetUI.Build(widget, "mfc:note");
			Window window = new Window
			{
				Width = 640.0,
				Height = 400.0,
				Content = content
			};
			window.Show();
			window.UpdateLayout();
			try
			{
				TextBlock? body = Find<TextBlock>(window, (TextBlock t) => t.Text == Body);
				Assert.True(body != null, "便签正文没有出现在可视树里;树 = " + Dump(window, 0));
				Assert.Equal(12.5, body!.FontSize, 6);
			}
			finally
			{
				window.Close();
			}
		});
	}
}