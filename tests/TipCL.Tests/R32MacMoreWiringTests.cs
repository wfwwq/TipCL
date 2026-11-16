// R32-MACMORE:布局 v2「更多入口」包的接线测试 —— 设置页布局视图 / 三页宿主 / 加块路径。
//
// 与 R32MacMoreLayoutTests.cs 的分工:
//   那边验**纯逻辑**(ShellTabs / SplitCatalog / 入口表 / 方案 / JSON 编辑器 / 加块数据层);
//   这边验**接线**(视图装配、导航入口、三页真成 BlockHost 宿主、编辑态四标记、加块从按钮到落盘到上屏)。
//
// 纪律:
//   * 每条碰 Avalonia 的用例体都经 AvaloniaSession.Run(...);落盘一律 C3aHome(或 C4Files.LayoutDir)注入目录;
//   * 外壳一律 ShellFixture.Create(smoke: true) 造并按 Dispose 还原全局静态(与 C2b/C7b 同一套);
//   * 断言只读可视化树 / 文档 / Toast,不做像素断言(像素证据走 headless 可视化树与 `--smoke` 截图,
//     见 progress/R32-MACMORE.md 的「GUI 使用与焦点纪律」)。
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TipCL.Controls;
using TipCL.Core.Launcher.Layout;
using TipCL.Models;
using TipCL.Pages;
using TipCL.Pages.Ai;
using TipCL.Pages.Plugins;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>设置页「布局」视图 + 三页区间宿主 + 加块路径的接线用例。</summary>
public class R32MacMoreWiringTests
{
	// ───────────────────────────── 小工具 ─────────────────────────────

	private static IEnumerable<Control> Descendants(Control root)
	{
		return root.GetVisualDescendants().OfType<Control>();
	}

	private static string TextOf(Control control)
	{
		if (control is TextBlock text)
		{
			return text.Text ?? "";
		}
		if (control is ContentControl content)
		{
			if (content.Content is TextBlock inner)
			{
				return inner.Text ?? "";
			}
			return content.Content?.ToString() ?? "";
		}
		if (control is TextBox box)
		{
			return box.Text ?? "";
		}
		return "";
	}

	private static Button ButtonWith(Control root, string label)
	{
		return Descendants(root).OfType<Button>().First(b => TextOf(b) == label);
	}

	/// <summary>页面里所有区块宿主(可视树)。</summary>
	private static List<BlockHost> Hosts(Control root)
	{
		return Descendants(root).OfType<BlockHost>().ToList();
	}

	/// <summary>设置页:取第 i 段的左导航钮(内部 <c>_nav</c> 的公开投影)。</summary>
	private static List<ToggleButton> NavButtons(SettingsPage page)
	{
		return Descendants(page).OfType<ToggleButton>()
			.Where(b => (b.Content as TextBlock)?.Text is { Length: > 0 })
			.ToList();
	}

	// ─────────────────────── 设置页「布局」视图(第 8 段) ───────────────────────

	/// <summary>
	/// 布局视图是第 8 段(Windows `LayoutView = 7`):`OpenLayoutView()` 后左导航第 8 个钮选中,
	/// 且七个 小节 a-g 的标题全部出现在可视树里。
	/// </summary>
	[Fact]
	public void Settings_open_layout_view_selects_the_eighth_segment_with_all_seven_sections()
	{
		AvaloniaSession.Run(() =>
		{
			using var scope = new C3aHome();
			using var api = new C3cFakeApi();
			using var harness = C4LayerHost.Show();
			using var toasts = new C3cToasts();
			SettingsPage page = C4Files.Mount(harness, new SettingsPage());

			// 切之前:布局视图的标题不在树里(判别性:不是"一直都在")
			Assert.DoesNotContain(Descendants(page).OfType<TextBlock>(), t => t.Text == I18n.L("外观与密度"));

			page.OpenLayoutView();
			C3bFiles.Flush(4);

			List<ToggleButton> nav = NavButtons(page);
			Assert.Equal(8, nav.Count);
			Assert.Equal(I18n.L("布局"), (nav[7].Content as TextBlock)!.Text);
			Assert.True(nav[7].IsChecked, "第 8 段「布局」应当被选中");
			Assert.False(nav[0].IsChecked, "「通用」应当被取消选中");

			List<string> texts = Descendants(page).OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
			foreach (string section in new[]
			{
				"外观与密度", "页签与标题栏", "页面区块", "分栏比例", "自建区块", "布局方案"
			})
			{
				Assert.Contains(I18n.L(section), texts);
			}
			// g 小节(JSON 编辑器)是卡片标题,不是 LayoutSection
			Assert.Contains(I18n.L("布局 JSON"), texts);
			// 七个小节的说明文案也必须在(视图装配完整的第二判据)
			Assert.Contains(texts, t => t.Contains("ui_layout_v2.json"));
		});
	}

	/// <summary>
	/// 「页面区块」小节 = 入口表 14 条 × 一个「自定义布局...」按钮;点第一条(通用)能进入编辑态。
	/// 这条同时证明 Windows `SettingsLayoutPage.LayoutBlockRows/OpenBlockEditor` 的接线在 mac 上真的通。
	/// <para>
	/// **必须用真外壳**:<c>OpenBlockEditor</c> 的第一步是 <c>PageBase.Win</c>(没有外壳就直接 return,
	/// 这正是本用例第一版用 <see cref="C4LayerHost"/> 时"点了什么都不发生"的原因)。
	/// </para>
	/// </summary>
	[Fact]
	public void Settings_layout_view_block_entries_can_enter_edit_on_the_target_page()
	{
		AvaloniaSession.Run(() =>
		{
			using var scope = new C3aHome();
			using var api = new C3cFakeApi();
			// 顺序要紧:外壳构造时会把 AppServices.ToastSink 挂成自己的 Toast,所以捕获器必须**后**建
			using var shell = ShellFixture.Create(smoke: false);
			using var toasts = new C3cToasts();
			shell.Window.Navigate("settings");
			C3bFiles.Flush(4);
			SettingsPage page = Assert.IsType<SettingsPage>(shell.Window.GetPage("settings"));
			page.OpenLayoutView();
			C3bFiles.Flush(4);

			List<Button> entries = Descendants(page).OfType<Button>()
				.Where(b => TextOf(b) == I18n.L("自定义布局...")).ToList();
			Assert.Equal(LayoutBlockEntryCatalog.All.Length, entries.Count);

			Assert.True(PageBase.Win != null, "诊断:PageBase.Win 为 null(AppServices.Window=" + (AppServices.Window?.GetType().Name ?? "null") + ")");
			// 第一条 = settings:general(通用):点它 → 切回第 0 段并进编辑态
			entries[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
			C3bFiles.Flush(8);

			string diag = "pageHosts=[" + string.Join(",", Descendants(page).OfType<BlockHost>().Select(h => h.PageId + (h.Editor.IsEditing ? "(edit)" : ""))) +
				"] windowHosts=[" + string.Join(",", Descendants(shell.Window).OfType<BlockHost>().Select(h => h.PageId)) +
				"] pageAttached=" + (page.GetVisualRoot() != null) + " sameAsDisplayed=" + ReferenceEquals(shell.Window.GetPage("settings"), page);
			Assert.True(Descendants(page).OfType<BlockHost>().Any(h => h.PageId == "settings:general" && h.Editor.IsEditing), "诊断:" + diag);
			Assert.Contains(toasts.Items, t => t.Title == I18n.L("已进入自定义布局"));
			// 收尾:退出编辑态,避免残留状态影响后续用例(外壳 Dispose 只还原全局静态)
			Descendants(page).OfType<BlockHost>().First(h => h.PageId == "settings:general").Editor.ExitEdit();
			C3bFiles.Flush(2);
		});
	}

	/// <summary>
	/// 加块面板:入口按钮「添加区块」+ 两个下拉(目标页 / 部件)+ 「部件清单」那一行把五类都列出来。
	/// 这条是任务书 §3-4「枚举部件清单」的静态判据;端到端点击见下一条。
	/// </summary>
	[Fact]
	public void Settings_layout_view_has_the_add_block_panel_with_the_widget_list()
	{
		AvaloniaSession.Run(() =>
		{
			using var scope = new C3aHome();
			using var api = new C3cFakeApi();
			using var harness = C4LayerHost.Show();
			using var toasts = new C3cToasts();
			SettingsPage page = C4Files.Mount(harness, new SettingsPage());
			page.OpenLayoutView();
			C3bFiles.Flush(4);

			Button add = ButtonWith(page, I18n.L("添加区块"));
			Assert.True(add.IsEnabled, "「添加区块」必须可用(Windows 冻结件里它是 disabled 的空壳)");

			List<string> combos = Descendants(page).OfType<ComboBox>()
				.SelectMany(c => c.Items.OfType<object>().Select(i => i?.ToString() ?? ""))
				.ToList();
			// 目标页下拉里必须有「通用」与「实例库」
			Assert.Contains(I18n.L("通用"), combos);
			Assert.Contains(I18n.L("实例库"), combos);
			// 部件下拉里必须有五类安全类型(枚举部件清单)
			foreach (string type in LayoutWidgetAdd.AddableTypes)
			{
				Assert.Contains(LayoutWidgetAdd.TypeLabel(type), combos);
			}
			// 「部件清单」那一行的可见文案(单独一行枚举,截图/UIA 都能读到)
			Assert.Contains(Descendants(page).OfType<TextBlock>(),
				t => (t.Text ?? "") == LayoutWidgetAdd.TypeListText());
		});
	}

	/// <summary>
	/// **加块端到端(界面层)**:在布局视图里点「添加区块」→ 目标页 通用 / 类型 便签
	/// → 文档里出现 WidgetDef + `custom:` 块 → 切回「通用」段后新块**真的渲染出来**(标题可查)。
	/// </summary>
	[Fact]
	public void Settings_add_block_click_creates_widget_and_renders_it_on_the_target_page()
	{
		AvaloniaSession.Run(() =>
		{
			using var scope = new C3aHome();
			using var api = new C3cFakeApi();
			using var shell = ShellFixture.Create(smoke: false);
			using var toasts = new C3cToasts();
			WidgetRegistry.EnsureRegistered();
			try
			{
				shell.Window.Navigate("settings");
				C3bFiles.Flush(4);
				SettingsPage page = Assert.IsType<SettingsPage>(shell.Window.GetPage("settings"));
				page.OpenLayoutView();
				C3bFiles.Flush(4);

				Button add = ButtonWith(page, I18n.L("添加区块"));
				Assert.True(PageBase.Win != null, "诊断:PageBase.Win 为 null(AppServices.Window=" + (AppServices.Window?.GetType().Name ?? "null") + ")");
				add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
				// 点击 → Select(0) + Post(Loaded) → AttachWidget(找宿主 + 一次 Save)。多推几轮。
				C3bFiles.Flush(8);

				LayoutV2 doc = LayoutStoreV2.Load();
				WidgetDef def = Assert.Single(doc.Layout.Widgets);
				Assert.Equal(LayoutSchema.WidgetNote, def.Type);
				Assert.True(doc.Layout.Pages.ContainsKey("settings:general"), "目标页必须写进文档");
				PageLayoutV2 pageDoc = doc.Layout.Pages["settings:general"];
				Assert.Contains("custom:" + def.Id, pageDoc.Blocks.Select(b => b.Id));
				// 内置块一个都没丢(9 个)
				Assert.Equal(9, pageDoc.Blocks.Count(b => !b.Id.StartsWith("custom:", StringComparison.Ordinal)));

				// 成功 Toast 与"上屏":切回第 0 段后,新块的标题必须出现在可视树里
				Assert.Contains(toasts.Items, t => t.Title == I18n.L("已添加区块"));
				Assert.True(NavButtons(page)[0].IsChecked, "加块后应当自动切到目标页「通用」");
				// 上屏:等 LayoutChanged → BlockHost.ReloadFromStore → Render 把新块画出来(全量跑时
				// 渲染队列里可能还排着别的用例的活儿,所以这里泵到出现为止,不用固定轮数)
				try
				{
					C3bFiles.Pump(() => Descendants(page).OfType<TextBlock>().Any(t => (t.Text ?? "") == def.Title), 8000);
				}
				catch (TimeoutException)
				{
					BlockHost diagHost = Descendants(page).OfType<BlockHost>().First(h => h.PageId == "settings:general");
					LayoutV2 fresh = LayoutStoreV2.Load();
					PageLayoutV2? freshPage = fresh.Layout.Pages.TryGetValue("settings:general", out PageLayoutV2? fp) ? fp : null;
					string diag = "stateOrder=[" + string.Join(",", diagHost.Editor.State.Order) + "]"
						+ " freshDocBlocks=[" + string.Join(",", freshPage?.Blocks.Select(b => b.Id) ?? new List<string>()) + "]"
						+ " variantWidth=" + LayoutVariantHost.Width.ToString("0.#")
						+ " variantSrc=" + LayoutVariants.SourceOf(fresh.Layout, "settings:general", LayoutVariantHost.Width, LayoutVariantHost.Threshold)
						+ " widgets=" + fresh.Layout.Widgets.Count
						+ " hostCount=" + Descendants(page).OfType<BlockHost>().Count()
						+ " sameAsDisplayed=" + ReferenceEquals(page, shell.Window.GetPage("settings"))
						+ " titles=[" + string.Join("|", Descendants(page).OfType<TextBlock>().Select(t => t.Text ?? "").Where(t => t.Contains("便签") || t.Contains("custom"))) + "]";
					throw new TimeoutException("加块上屏超时:" + diag);
				}
				Assert.Contains(Descendants(page).OfType<TextBlock>(), t => (t.Text ?? "") == def.Title);
				// 不只是"有个标题":内容工厂(WidgetUI.Note)必须真造出便签卡片 —— 便签正文的默认引导语
				// 是 LayoutWidgetAdd 给 note 的 settings 为空时的固定文案(WidgetCommon/ BasicWidgets.Note)。
				Assert.Contains(Descendants(page).OfType<TextBlock>(),
					t => (t.Text ?? "").Contains("settings.text", StringComparison.Ordinal));
				// 便签卡片是 Ui.Card(Border) 包着 标题 + 正文:标题的 Border 祖先存在(证明确实渲染成卡片)
				TextBlock title = Descendants(page).OfType<TextBlock>().First(t => (t.Text ?? "") == def.Title);
				Assert.Contains(title.GetVisualAncestors(), a => a is Border);
			}
			finally
			{
				WidgetRegistry.Unregister();
			}
		});
	}

	/// <summary>
	/// `LayoutShellHooks.OpenLayoutSettings` 真打开布局设置(Windows `LayoutShellHooks.cs:228-235`):
	/// 从任意页调用 → 导航到设置页 + 选中第 8 段「布局」。
	/// </summary>
	[Fact]
	public void OpenLayoutSettings_navigates_to_settings_and_selects_the_layout_view()
	{
		AvaloniaSession.Run(() =>
		{
			using var scope = new C3aHome();
			using var api = new C3cFakeApi();
			using var shell = ShellFixture.Create(smoke: false);
			shell.Window.Navigate("launch");
			C3bFiles.Flush(4);

			LayoutShellHooks.OpenLayoutSettings(shell.Window);
			C3bFiles.Flush(6);

			SettingsPage page = Assert.IsType<SettingsPage>(shell.Window.GetPage("settings"));
			List<ToggleButton> nav = NavButtons(page);
			Assert.Equal(8, nav.Count);
			Assert.True(nav[7].IsChecked, "OpenLayoutSettings 必须真的切到「布局」内层视图");
			Assert.Contains(Descendants(page).OfType<TextBlock>(), t => t.Text == I18n.L("外观与密度"));
		});
	}

	/// <summary>
	/// R32-D26 的界面判据:「页签与标题栏」小节按 **Mac 页集(7)** 列行 —— 其中 5 个语义键
	/// 各有显隐/上移/下移(写 `shell.tabs`),多出的 2 页(ai / tasks)是只读的原生行。
	/// 分子:五个「页签顺序与显隐(&lt;key&gt;)」说明 + 两行「多出的页:布局 v2 不接管(保持原生行为)」。
	/// </summary>
	[Fact]
	public void Settings_shell_section_lists_the_seven_mac_pages_and_marks_the_two_native_ones()
	{
		AvaloniaSession.Run(() =>
		{
			using var scope = new C3aHome();
			using var api = new C3cFakeApi();
			using var harness = C4LayerHost.Show();
			using var toasts = new C3cToasts();
			SettingsPage page = C4Files.Mount(harness, new SettingsPage());
			page.OpenLayoutView();
			C3bFiles.Flush(4);

			List<string> texts = Descendants(page).OfType<TextBlock>().Select(t => t.Text ?? "").ToList();

			// 5 个语义键:每一行都写清落到 shell.tabs 的哪个键(说明列用**持久化键**,
			// 所以「版本管理」那一行写的是 (library)、「更多」那一行写的是 (plugins))
			foreach (string tabKey in new[] { "launch", "library", "download", "plugins", "settings" })
			{
				Assert.Contains(I18n.L("页签顺序与显隐({0})", tabKey), texts);
			}
			// Mac 页名(取自侧栏同一套词条)
			foreach (string label in new[] { "启动", "游戏", "版本管理", "更多", "设置", "AI 助手", "下载任务" })
			{
				Assert.Contains(I18n.L(label), texts);
			}
			// 多出的 2 页:只读原生行(两行同一句说明)
			Assert.Equal(2, texts.Count(t => t == I18n.L("多出的页:布局 v2 不接管(保持原生行为)")));
			// 原生行不给开关:这两页在 shell.tabs 里没有键,IsMacNativeOnly 为真(逻辑层钉死)
			Assert.True(ShellTabs.IsMacNativeOnly("ai") && ShellTabs.IsMacNativeOnly("tasks"));
			Assert.False(ShellTabs.IsMacNativeOnly("instance"));
			// 持久化不因界面扩到 7 行而变:仍是 5 个 Windows 语义键
			Assert.Equal(5, ShellTabs.BuiltinKeys.Length);
		});
	}

	// ─────────────────────── ai 分栏间隙(唯一真偏离 8px) ───────────────────────

	/// <summary>
	/// R32-D26 / ROUND2-SPEC §3.2:`ai/history` 是 4 处留白里**唯一真数值偏离** —— Mac 早先用了默认
	/// `gap = 18` 却保留 Windows 的 `-10` 补偿(净 +8px)。现在按 Windows 站点声明 `gap 10 + −10`
	/// 修正:宿主 <see cref="PageLayoutHost.GapWidth"/> 必须是 10,且与 <see cref="SplitCatalog"/> 的
	/// `ai/history` 站点声明一致(去掉其中任一处,这条会红)。
	/// </summary>
	[Fact]
	public void AiPage_history_split_uses_the_windows_gap_of_ten()
	{
		AvaloniaSession.Run(() =>
		{
			using var scope = new C3aHome();
			using var api = new C3cFakeApi();
			// 只构造、不挂树:分栏宿主在构造期就建好了,断言它的间隙不需要渲染,也就不会往
			// 全套件的渲染队列 / 事件里塞东西(全套件串行跑时这点很重要)。
			AiPage page = new AiPage();
			PageLayoutHost host = page.HistorySplit;
			// Windows `PageLayoutHost.cs:170-181` 的 ai/history 站点:DefaultPixels=220 / Gap=10
			SplitSite? site = SplitCatalog.Find("ai", "history");
			Assert.NotNull(site);
			Assert.Equal(10.0, site!.Gap);
			Assert.Equal(site.Gap, host.GapWidth);
			// 反证:默认值(18)会让这条红 —— 也就是"把 gap 改成默认、漏了站点声明"必被抓住
			Assert.NotEqual(PageLayoutHost.DefaultGap, host.GapWidth);
			Assert.Equal("left", site.PixelsSide);
		});
	}

	// ─────────────────────── 三页区间宿主(实例库 / 资源目录 / 插件) ───────────────────────

	/// <summary>实例库 = `BlockHost("library:main")`,两区块 folders / versions(Windows `:245`)。</summary>
	[Fact]
	public void Library_page_is_a_library_main_block_host()
	{
		AvaloniaSession.Run(() =>
		{
			using var scope = new C3aHome();
			using var api = new C3cFakeApi();
			using var harness = C4LayerHost.Show();
			using var toasts = new C3cToasts();
			LibraryPage page = C4Files.Mount(harness, new LibraryPage());
			List<BlockHost> hosts = Hosts(page);
			BlockHost host = Assert.Single(hosts);
			Assert.Equal("library:main", host.PageId);
			Assert.Equal(new List<string> { "folders", "versions" }, host.OrderNow.Select(b => b.Id).ToList());
			// 出厂默认页是 12 栅格 4/8 列(grid 模式)
			Assert.True(host.Editor.CanEdit);
			Assert.True(host.EnterEdit());
			Assert.True(host.Editor.IsEditing);
		});
	}

	/// <summary>
	/// 资源目录页是按 kind 分段的宿主:pageId = `catalog:&lt;类型&gt;`(Windows `CatalogPage.cs:138/315`),
	/// 三区块 header / filters / results。
	/// </summary>
	[Fact]
	public void Catalog_page_is_a_host_named_after_its_kind()
	{
		AvaloniaSession.Run(() =>
		{
			using var scope = new C3aHome();
			using var api = new C3cFakeApi();
			using var harness = C4LayerHost.Show();
			using var toasts = new C3cToasts();
			CatalogPage page = C4Files.Mount(harness, new CatalogPage(CatalogKind.Mod));
			Assert.Equal("catalog:mod", page.LayoutPageId);
			BlockHost host = Assert.Single(Hosts(page));
			Assert.Equal("catalog:mod", host.PageId);
			Assert.Equal(new List<string> { "header", "filters", "results" }, host.OrderNow.Select(b => b.Id).ToList());
			Assert.True(host.EnterEdit());
			Assert.True(host.Editor.IsEditing);
		});
	}

	/// <summary>插件页 = `BlockHost("plugins")`,两区块 tabs / body(Windows `PluginsPage.cs:120`)。</summary>
	[Fact]
	public void Plugins_page_is_a_plugins_block_host()
	{
		AvaloniaSession.Run(() =>
		{
			using var scope = new C3aHome();
			using var api = new C3cFakeApi();
			using var harness = C4LayerHost.Show();
			using var toasts = new C3cToasts();
			PluginsPage page = C4Files.Mount(harness, new PluginsPage());
			BlockHost host = Hosts(page).First(h => h.PageId == "plugins");
			Assert.Equal(new List<string> { "tabs", "body" }, host.OrderNow.Select(b => b.Id).ToList());
			Assert.True(host.EnterEdit());
			Assert.True(host.Editor.IsEditing);
		});
	}

	/// <summary>
	/// 三页进了编辑态后,编辑工具条的四个标记(完成编辑 / 重置本页 / 导出布局 / 导入布局)都在 ——
	/// 这就是真窗口 UIA 用例里那四个可访问节点的来源。
	/// </summary>
	[Fact]
	public void Edit_mode_toolbar_of_the_three_pages_carries_the_four_markers()
	{
		AvaloniaSession.Run(() =>
		{
			using var scope = new C3aHome();
			using var api = new C3cFakeApi();
			using var harness = C4LayerHost.Show();
			using var toasts = new C3cToasts();
			List<Control> pages = new List<Control>
			{
				C4Files.Mount(harness, new LibraryPage()),
				C4Files.Mount(harness, new CatalogPage(CatalogKind.Mod)),
				C4Files.Mount(harness, new PluginsPage())
			};
			foreach (Control page in pages)
			{
				BlockHost host = Hosts(page)[0];
				Assert.True(host.EnterEdit(), host.PageId + " 应当能进编辑态");
				C3bFiles.Flush(2);
				List<string> buttons = Descendants(host).OfType<Button>().Select(TextOf).ToList();
				foreach (string marker in new[] { "完成编辑", "重置本页", "导出布局", "导入布局" })
				{
					Assert.Contains(I18n.L(marker), buttons);
				}
				host.Editor.ExitEdit();
				C3bFiles.Flush(2);
			}
		});
	}

	/// <summary>
	/// 三页 + 设置页三宿主的 pageId 与入口表口径一致:入口表里每一条都能在本页范围内找到宿主
	/// (静态对照表,不依赖窗口导航;`catalog:*` 与 `plugins:market` 与 Windows 同口径不在表内)。
	/// </summary>
	[Fact]
	public void Block_entry_catalog_page_ids_are_all_real_host_ids()
	{
		HashSet<string> live = new HashSet<string>(StringComparer.Ordinal)
		{
			"settings:general", "settings:network", "settings:about",
			"java", "servers", "playtime", "feedback",
			"launch", "download", "account", "tasks", "multiplayer",
			"library:main", "plugins",
			// 与 Windows 入口表同口径不进表的两个宿主
			"catalog:mod", "plugins:market"
		};
		foreach (LayoutBlockEntryCatalog.Entry entry in LayoutBlockEntryCatalog.All)
		{
			Assert.Contains(entry.PageId, live);
		}
	}
}
