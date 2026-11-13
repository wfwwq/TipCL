using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TipCL;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C2b:窗口外壳(标题栏 / 侧栏 / 页面宿主 / 系统菜单 / 快捷键)。
/// 一切 Avalonia 工作都在 <c>AvaloniaSession.Run</c> 里(D-79/C1b-fix 的纪律),
/// 全局静态由 <see cref="ShellFixture"/> 在 Dispose 里还原。
/// </summary>
public class C2bShellTests
{
    // ---------------------------------------------------------------- 窗口

    [Fact]
    public void Window_size_and_min_size_follow_the_brief()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create();
            Assert.Equal(C2bExpected.WindowWidth, shell.Window.Width);
            Assert.Equal(C2bExpected.WindowHeight, shell.Window.Height);
            Assert.Equal(C2bExpected.WindowMinWidth, shell.Window.MinWidth);
            Assert.Equal(C2bExpected.WindowMinHeight, shell.Window.MinHeight);
            Assert.Equal(WindowStartupLocation.CenterScreen, shell.Window.WindowStartupLocation);
            Assert.Equal(I18n.L("TipCL 启动器"), shell.Window.Title);
        });
    }

    [Fact]
    public void Title_bar_is_46_px_and_its_content_leaves_78_px_for_the_traffic_lights()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create();
            Assert.Equal(46.0, MainWindow.TitleBarHeight);
            Assert.Equal(78.0, MainWindow.TitleBarLeftInset);
            Assert.Equal(C2bExpected.TitleBarHeight, shell.Window.TopBar.Height);

            var row = Assert.IsType<Grid>(shell.Window.TopBar.Child);
            SPanel left = row.Children.OfType<SPanel>().First();
            Assert.Equal(C2bExpected.TitleBarLeftInset, left.Margin.Left);
            // 红绿灯那块(x < 78)在标题栏里不放任何内容
            Assert.All(row.Children.OfType<Control>(), c => Assert.True(c.Margin.Left >= 78.0 || c.HorizontalAlignment == HorizontalAlignment.Right));
        });
    }

    [Fact]
    public void Traffic_light_extension_is_enabled_with_system_chrome()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create();
            // 【任务书/GOAL】用系统红绿灯:内容铺进标题栏,标题栏高度提示 46px,chrome 让系统画
            Assert.True(shell.Window.ExtendClientAreaToDecorationsHint);
            Assert.Equal(C2bExpected.TitleBarHeight, shell.Window.ExtendClientAreaTitleBarHeightHint);
            Assert.Equal(Avalonia.Platform.ExtendClientAreaChromeHints.PreferSystemChrome, shell.Window.ExtendClientAreaChromeHints);
        });
    }

    [Fact]
    public void No_custom_minimize_maximize_or_close_buttons_are_drawn()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create();
            // 【任务书】不画 Windows 式 caption 按钮:标题栏里一个 Button 都没有,
            // 整个窗口也没有 btn-caption / btn-caption-close 类
            Assert.Empty(shell.Window.TopBar.GetVisualDescendants().OfType<Button>());
            List<Button> buttons = shell.Window.GetVisualDescendants().OfType<Button>().ToList();
            Assert.All(buttons, b => Assert.DoesNotContain("btn-caption", b.Classes));
            Assert.All(buttons, b => Assert.DoesNotContain("btn-caption-close", b.Classes));
        });
    }

    [Theory]
    [InlineData(250.0, 250.0)]
    [InlineData(196.0, 196.0)]
    [InlineData(100.0, 140.0)]
    [InlineData(400.0, 320.0)]
    [InlineData(0.0, 196.0)]
    public void Sidebar_width_follows_UiPrefs_SideWidth_with_the_windows_clamp(double pref, double expected)
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create(new UiPrefs { SideWidth = pref });
            Assert.Equal(expected, shell.Window.SidebarWidth);
            // 侧栏宽度的真正落点:内容区两列 Grid 的第一列
            Grid body = C2bAssert.ByName<Grid>(shell.Window, "BodyGrid");
            Assert.Equal(expected, body.ColumnDefinitions[0].Width.Value);
            // 布局跑过一轮之后侧栏就是那么宽(±0.5px 的布局舍入)
            Assert.True(Math.Abs(shell.Window.Sidebar.Bounds.Width - expected) < 0.5, "侧栏实测宽度 = " + shell.Window.Sidebar.Bounds.Width);
        });
    }

    // ---------------------------------------------------------------- 侧栏

    [Fact]
    public void Sidebar_rows_come_from_the_nav_model_with_headers_items_and_the_stretch_row()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create();
            List<string> rows = DescribeSidebar(shell.Window);
            Assert.Equal(new[] { "H:启动", "I:launch", "H:库", "I:instance", "I:download", "I:multiplayer", "H:社区", "I:account", "H:AI 助手", "I:ai", "H:工具", "I:tasks", "I:more", "H:设置", "I:settings" }, rows);
            Assert.Equal(6, rows.Count(r => r.StartsWith("H:", StringComparison.Ordinal)));
        });
    }

    [Fact]
    public void Sidebar_item_rows_are_tab_top_toggles_tagged_with_the_nav_key()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create();
            List<ToggleButton> items = shell.Window.Sidebar.GetVisualDescendants().OfType<ToggleButton>().ToList();
            Assert.Equal(9, items.Count);
            Assert.All(items, t => Assert.Contains("tab-top", t.Classes));
            Assert.Equal(new[] { "launch", "instance", "download", "multiplayer", "account", "ai", "tasks", "more", "settings" }, items.Select(t => (string)t.Tag!));
            // 只有能去页面的行才挂 Click(可点性由 Clicking_a_sidebar_item_navigates... 那条用例验);
            // 光标没法用来判:tab-top 的 ControlTheme 自己就把光标设成 Hand。
            Assert.All(items, t => Assert.True(t.Cursor is null || t.Cursor.ToString() == "Hand"));
        });
    }

    [Fact]
    public void Sidebar_selection_follows_the_current_page()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = new RegistryScope();
            scope.Register("launch", () => new ProbePage());
            scope.Register("settings", () => new ProbePage());
            using var shell = ShellFixture.Create();
            // 构造时 Navigate("launch", instant) —— 与 Windows 的首屏一致
            Assert.Equal("launch", shell.Window.CurrentPageId);
            AssertChecked(shell.Window, "launch", true);
            AssertChecked(shell.Window, "settings", false);

            shell.Window.Navigate("settings");
            Assert.Equal("settings", shell.Window.CurrentPageId);
            AssertChecked(shell.Window, "settings", true);
            AssertChecked(shell.Window, "launch", false);
        });
    }

    [Fact]
    public void Clicking_a_sidebar_item_navigates_and_clicking_a_section_row_does_nothing()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = new RegistryScope();
            scope.Register("launch", () => new ProbePage());
            scope.Register("instance", () => new ProbePage());
            using var shell = ShellFixture.Create();
            Assert.Equal("launch", shell.Window.CurrentPageId);

            ToggleButton instance = SidebarRow(shell.Window, "instance");
            instance.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("instance", shell.Window.CurrentPageId);

            // 分区行(download / more)没有页面 id 可去:点它不改当前页(Windows 的 PageOf 返回 null 时同)
            ToggleButton download = SidebarRow(shell.Window, "download");
            download.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("instance", shell.Window.CurrentPageId);
        });
    }

    [Fact]
    public void ApplyNavSettings_rebuilds_the_sidebar_and_the_menu()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create();
            Assert.Contains("H:启动", DescribeSidebar(shell.Window));

            // 换成分组式的对偶(精简式):行表里分组头消失、出现撑条。
            // 注意 ApplyNavSettings 收的是后端 settings 的 JSON(键名 ui_nav_*),不是 C# 对象的序列化结果。
            using var doc = System.Text.Json.JsonDocument.Parse("""
            {
              "ui_nav_style": "compact",
              "ui_nav_order": ["launch", "download", "instance", "ai", "more", "settings", "tasks"],
              "ui_nav_pinned": ["instance", "settings"]
            }
            """);
            shell.Window.ApplyNavSettings(doc.RootElement);

            Assert.Equal("compact", shell.Window.NavConfig.Style);
            List<string> rows = DescribeSidebar(shell.Window);
            Assert.DoesNotContain("H:启动", rows);
            Assert.Equal(new[] { "I:launch", "I:download", "I:instance", "I:ai", "I:more", "I:settings", "I:tasks" }, rows);
            // 菜单也跟着重建:可见顺序换成精简式的顺序(分区键 download/more 展开成它们的分区成员)
            Assert.Equal(
                new[] { "launch", "instance", "ai", "account", "multiplayer", "plugins", "settings", "tasks" },
                shell.Window.VisibleFlatOrder());
        });
    }

    // ---------------------------------------------------------------- 页面宿主

    /// <summary>
    /// C3c 更正:C2b 时这一条叫 <c>Page_host_starts_empty_because_no_page_is_registered_yet</c>,
    /// 断的是"还没有页面注册所以宿主是空的"。启动页注册进来之后那个前提**不再成立**
    /// (外壳构造函数第一件事就是 <c>Navigate("launch", instant)</c>,与 Windows 首屏一致),
    /// 所以这里把小节拆成两条:
    /// <list type="number">
    /// <item>注册之后:宿主直接落在启动页上(断言**更多**:当前页类型、宿主内容同一个实例);</item>
    /// <item><see cref="Page_host_starts_empty_when_the_launch_page_is_unregistered"/>:
    ///   用 <c>RegistryScope</c> + <c>Unregister</c> 把启动页摘掉,原来的三条断言**一字不改**地留着。</item>
    /// </list>
    /// 没有删测试、没有放宽断言、没有加 Skip。
    /// </summary>
    [Fact]
    public void Page_host_opens_the_registered_launch_page()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create();
            Assert.Equal("launch", shell.Window.CurrentPageId);
            Assert.IsType<LaunchPage>(shell.Window.CurrentPage);
            Assert.Same(shell.Window.CurrentPage, shell.Window.Host.Content);
        });
    }

    /// <summary>【Windows 源】<c>MainWindow</c> 的 <c>Navigate(未知 id)</c>:静默不切页;
    /// 启动页没注册时首屏就是"空宿主"(C2b 的原始断言,一条不改)。</summary>
    [Fact]
    public void Page_host_starts_empty_when_the_launch_page_is_unregistered()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = new RegistryScope();
            Assert.True(PageRegistry.Unregister("launch"), "前置条件:启动页此刻应当是注册着的");
            using var shell = ShellFixture.Create();
            Assert.Equal("", shell.Window.CurrentPageId);
            Assert.Null(shell.Window.CurrentPage);
            Assert.Null(shell.Window.Host.Content);
        });
    }

    [Fact]
    public void Navigate_to_an_unknown_id_does_nothing_and_does_not_throw()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = new RegistryScope();
            // C3c:启动页已注册,而这一条要断的是"未知 id 不切页 / 大小写不同建不出来",
            // 所以先把 launch 摘掉,插回 C2b 时的初始状态(其余断言一字未改)。
            Assert.True(PageRegistry.Unregister("launch"), "前置条件:启动页此刻应当是注册着的");
            using var shell = ShellFixture.Create();
            // Windows 的 Navigate:GetPage 返回 null 就直接返回(不切页、不改状态、不抛)
            shell.Window.Navigate("nonsense");
            Assert.Equal("", shell.Window.CurrentPageId);
            Assert.Null(shell.Window.GetPage("nonsense"));
            shell.Window.Navigate("LAUNCH");
            Assert.Equal("", shell.Window.CurrentPageId);
            // 大小写不同也建不出来(序号比较,与 Windows 的字典一致)
            scope.Register("launch", () => new ProbePage());
            Assert.Null(shell.Window.GetPage("Launch"));
            Assert.NotNull(shell.Window.GetPage("launch"));
        });
    }

    [Fact]
    public void PageHost_swaps_the_child_and_runs_each_lifecycle_hook_once_per_switch()
    {
        AvaloniaSession.Run(() =>
        {
            var a = new ProbePage();
            var b = new ProbePage();
            var host = new PageHost(id => id switch { "a" => a, "b" => b, _ => null });

            Assert.True(host.Navigate("a", instant: true, forward: true, runHooks: true));
            PumpDispatcher();
            Assert.Same(a, host.Content);
            Assert.Equal("a", host.CurrentId);
            Assert.Equal(new[] { "Load", "Show" }, a.Log);
            Assert.Equal(1, a.Loads);

            Assert.True(host.Navigate("b", instant: true, forward: true, runHooks: true));
            PumpDispatcher();
            Assert.Same(b, host.Content);
            Assert.Equal(1, a.Hidden);
            Assert.Equal(1, b.Shown);

            Assert.True(host.Navigate("a", instant: true, forward: false, runHooks: true));
            PumpDispatcher();
            // 回到 a:实例是原来那个(每页状态留在缓存里),LoadAsync 不重跑,OnShown 再调一次
            Assert.Same(a, host.Content);
            Assert.Equal(1, a.Loads);
            Assert.Equal(2, a.Shown);
            Assert.Equal(1, b.Hidden);
            Assert.Equal(2, host.PageCount);
        });
    }

    [Fact]
    public void PageHost_ignores_unknown_ids_and_same_id_switches()
    {
        AvaloniaSession.Run(() =>
        {
            var a = new ProbePage();
            var host = new PageHost(id => id == "a" ? a : null);
            Assert.False(host.Navigate("nonsense", instant: true, forward: true, runHooks: true));
            Assert.Null(host.Content);
            Assert.Equal("", host.CurrentId);
            Assert.True(host.Navigate("a", instant: true, forward: true, runHooks: true));
            // 已经是当前页:不重复调 OnHidden/OnShown
            Assert.False(host.Navigate("a", instant: true, forward: true, runHooks: true));
            PumpDispatcher();
            Assert.Equal(1, a.Shown);
            Assert.Equal(0, a.Hidden);
        });
    }

    [Fact]
    public void PageHost_skips_the_load_hooks_when_the_backend_is_not_ready()
    {
        AvaloniaSession.Run(() =>
        {
            var a = new ProbePage();
            var host = new PageHost(id => id == "a" ? a : null);
            // Windows 的 _bridgeReady 门:后端没连上就只换内容、不加载
            Assert.True(host.Navigate("a", instant: true, forward: true, runHooks: false));
            Assert.Same(a, host.Content);
            PumpDispatcher();
            Assert.Equal(0, a.Loads);
            Assert.Equal(0, a.Shown);
        });
    }

    [Fact]
    public void MainWindow_gates_the_load_hooks_on_BridgeReady()
    {
        AvaloniaSession.Run(() =>
        {
            var probe = new ProbePage();
            using var scope = new RegistryScope();
            scope.Register("launch", () => new ProbePage());
            scope.Register("ai", () => probe);
            using var shell = ShellFixture.Create();

            Assert.False(shell.Window.BridgeReady);
            shell.Window.Navigate("ai");
            PumpDispatcher();
            Assert.Equal(0, probe.Loads);
            Assert.Equal(0, probe.Shown);

            shell.Window.BridgeReady = true;
            shell.Window.Navigate("launch");
            shell.Window.Navigate("ai");
            PumpDispatcher();
            Assert.Equal(1, probe.Loads);
            Assert.Equal(1, probe.Shown);
        });
    }

    [Fact]
    public void RefreshCommand_refreshes_only_a_loaded_page()
    {
        AvaloniaSession.Run(() =>
        {
            var probe = new ProbePage();
            using var scope = new RegistryScope();
            scope.Register("launch", () => probe);
            scope.Register("ai", () => new ProbePage());
            using var shell = ShellFixture.Create();
            // 构造时 Navigate("launch") 已经切到 launch(但那时后端未就绪,没加载),
            // 所以要绕一圈回来才会走"EnsureLoadedAsync + OnShown"
            shell.Window.BridgeReady = true;
            shell.Window.Navigate("ai");
            shell.Window.Navigate("launch");
            PumpDispatcher();
            Assert.Equal(1, probe.Loads);

            shell.Window.RefreshCommand.Execute(null);
            PumpDispatcher();
            // Windows 的 RefreshAsync:没加载过就空转,加载过才重跑 LoadAsync
            Assert.Equal(1, probe.Refreshes);
            Assert.Equal(1, probe.Shown);
        });
    }

    [Fact]
    public void NavOrderIndex_uses_the_visible_order_and_falls_back_to_99()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create();
            Assert.Equal(C2bExpected.DefaultVisiblePages.ToList(), shell.Window.VisibleFlatOrder());
            Assert.Equal(0, shell.Window.NavOrderIndex("launch"));
            Assert.Equal(7, shell.Window.NavOrderIndex("settings"));
            Assert.Equal(99, shell.Window.NavOrderIndex("library"));
            Assert.Equal(99, shell.Window.NavOrderIndex("nonsense"));
            // library / download 是页面但不在导航的可见顺序里(Windows 同:它们靠顶栏页签/分区进入)
            Assert.DoesNotContain("library", shell.Window.VisibleFlatOrder());
        });
    }

    // ---------------------------------------------------------------- 系统菜单与快捷键

    [Fact]
    public void Menu_has_the_two_top_level_submenus_from_the_shell()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create();
            NativeMenu menu = Assert.IsType<NativeMenu>(shell.Window.Menu);
            Assert.Equal(2, menu.Items.Count);
            Assert.Equal(I18n.L("页面"), Item(menu, 0).Header);
            Assert.Equal(I18n.L("操作"), Item(menu, 1).Header);
            Assert.NotNull(Item(menu, 0).Menu);
            Assert.NotNull(Item(menu, 1).Menu);
            // 菜单挂在窗口上(NativeDock.SetMenu),macOS 会把它当系统菜单栏
            Assert.Same(menu, NativeDock.GetMenu(shell.Window));
        });
    }

    [Fact]
    public void Page_menu_items_follow_the_visible_order_and_map_to_cmd_1_to_9()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create();
            NativeMenu pageItems = Sub(shell.Window.Menu!, 0);
            List<string> visible = shell.Window.VisibleFlatOrder();
            Assert.Equal(visible.Count, pageItems.Items.Count);
            for (int i = 0; i < pageItems.Items.Count; i++)
            {
                NativeMenuItem item = Item(pageItems, i);
                Assert.Equal(visible[i], Assert.IsType<string>(item.CommandParameter));
                // 【GOAL】⌘1...⌘9:第 i 个菜单项挂 ⌘(i+1)
                KeyGesture gesture = Assert.IsType<KeyGesture>(item.Gesture);
                Assert.Equal((Key)((int)Key.D1 + i), gesture.Key);
                Assert.Equal(KeyModifiers.Meta, gesture.KeyModifiers);
                Assert.Equal(NativeMenuItemToggleType.Radio, item.ToggleType);
            }
            // 第 9 项(⌘9)没有第 9 页可去:默认可见顺序只有 8 项
            Assert.Equal(8, pageItems.Items.Count);
            Assert.DoesNotContain(pageItems.Items.Cast<NativeMenuItem>(), i => i.Gesture!.Key == Key.D9);
        });
    }

    [Fact]
    public void Page_menu_labels_use_the_same_labels_as_the_sidebar()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create();
            NativeMenu pageItems = Sub(shell.Window.Menu!, 0);
            List<string> labels = pageItems.Items.Cast<NativeMenuItem>().Select(i => i.Header!).ToList();
            Assert.Equal(new[] { "启动", "版本管理", "多人联机", "账号", "AI 助手", "下载任务", "插件", "设置" }.Select(I18n.L), labels);
        });
    }

    [Fact]
    public void Actions_menu_carries_the_windows_shell_actions_and_their_gestures()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create();
            NativeMenu actions = Sub(shell.Window.Menu!, 1);
            Assert.Equal(4, actions.Items.Count);
            Assert.Equal(I18n.L("AI 助手"), Item(actions, 0).Header);
            Assert.Equal(Key.K, Assert.IsType<KeyGesture>(Item(actions, 0).Gesture).Key);
            Assert.Equal(KeyModifiers.Meta, Assert.IsType<KeyGesture>(Item(actions, 0).Gesture).KeyModifiers);
            Assert.Equal(I18n.L("设置"), Item(actions, 1).Header);
            Assert.Equal(Key.OemComma, Assert.IsType<KeyGesture>(Item(actions, 1).Gesture).Key);
            Assert.Equal(KeyModifiers.Meta, Assert.IsType<KeyGesture>(Item(actions, 1).Gesture).KeyModifiers);
            Assert.Equal(I18n.L("刷新"), Item(actions, 2).Header);
            Assert.Equal(Key.R, Assert.IsType<KeyGesture>(Item(actions, 2).Gesture).Key);
            Assert.Equal(KeyModifiers.Meta, Assert.IsType<KeyGesture>(Item(actions, 2).Gesture).KeyModifiers);
            Assert.Equal(I18n.L("切换深色 / 浅色"), Item(actions, 3).Header);
            Assert.Null(Item(actions, 3).Gesture);
        });
    }

    [Fact]
    public void Invoking_a_menu_command_navigates_to_the_expected_page()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = new RegistryScope();
            foreach (string id in C2bExpected.DefaultVisiblePages)
            {
                string captured = id;
                scope.Register(captured, () => new ProbePage());
            }
            using var shell = ShellFixture.Create();
            Assert.Equal("launch", shell.Window.CurrentPageId);

            NativeMenu pageItems = Sub(shell.Window.Menu!, 0);
            // 程序化触发第 4 项(⌘4 → account):不模拟按键,直接跑菜单项挂的 ICommand
            Item(pageItems, 3).Command!.Execute(Item(pageItems, 3).CommandParameter);
            Assert.Equal("account", shell.Window.CurrentPageId);

            NativeMenu actions = Sub(shell.Window.Menu!, 1);
            Item(actions, 1).Command!.Execute(Item(actions, 1).CommandParameter);
            Assert.Equal("settings", shell.Window.CurrentPageId);

            Item(actions, 0).Command!.Execute(null);
            Assert.Equal("ai", shell.Window.CurrentPageId);
        });
    }

    [Fact]
    public void NavigateIndex_walks_the_visible_order_and_ignores_out_of_range()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = new RegistryScope();
            foreach (string id in C2bExpected.DefaultVisiblePages)
            {
                string captured = id;
                scope.Register(captured, () => new ProbePage());
            }
            using var shell = ShellFixture.Create();

            Assert.True(shell.Window.NavigateIndex(0));
            Assert.Equal("launch", shell.Window.CurrentPageId);
            Assert.True(shell.Window.NavigateIndex(7));
            Assert.Equal("settings", shell.Window.CurrentPageId);
            Assert.False(shell.Window.NavigateIndex(8));
            Assert.False(shell.Window.NavigateIndex(-1));
            Assert.Equal("settings", shell.Window.CurrentPageId);
        });
    }

    [Fact]
    public void Theme_toggle_flips_the_variant_and_the_persisted_preference()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create(new UiPrefs { Dark = false });
            Assert.Equal(ThemeVariant.Light, shell.Window.RequestedThemeVariant);
            shell.Window.ToggleThemeCommand.Execute(null);
            Assert.Equal(ThemeVariant.Dark, shell.Window.RequestedThemeVariant);
            Assert.True(shell.Window.Prefs.Dark);
            shell.Window.ToggleThemeCommand.Execute(null);
            Assert.Equal(ThemeVariant.Light, shell.Window.RequestedThemeVariant);
            Assert.False(shell.Window.Prefs.Dark);
        });
    }

    [Fact]
    public void Shell_shortcut_keys_map_to_the_goals_accelerators()
    {
        // 【GOAL §3】⌘1...9 切页、⌘K AI、⌘, 设置、⌘R 刷新
        Assert.Equal(new KeyGesture(Key.D1, KeyModifiers.Meta), ShellShortcuts.PageGesture(0));
        Assert.Equal(new KeyGesture(Key.D9, KeyModifiers.Meta), ShellShortcuts.PageGesture(8));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShellShortcuts.PageGesture(9));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShellShortcuts.PageGesture(-1));
        Assert.Equal(new KeyGesture(Key.K, KeyModifiers.Meta), ShellShortcuts.AiGesture);
        Assert.Equal(new KeyGesture(Key.OemComma, KeyModifiers.Meta), ShellShortcuts.SettingsGesture);
        Assert.Equal(new KeyGesture(Key.R, KeyModifiers.Meta), ShellShortcuts.RefreshGesture);
        Assert.Equal(0, ShellShortcuts.PageIndex(Key.D1));
        Assert.Equal(8, ShellShortcuts.PageIndex(Key.D9));
        Assert.Equal(-1, ShellShortcuts.PageIndex(Key.D0));
        Assert.Equal(-1, ShellShortcuts.PageIndex(Key.A));
    }

    [Fact]
    public void Shortcut_guard_accepts_cmd_or_ctrl_and_does_not_steal_ai_while_typing()
    {
        AvaloniaSession.Run(() =>
        {
            // Windows 是 Keyboard.Modifiers == Control;mac 上主用 ⌘(Meta),同时接受 Ctrl(便于本机调试)
            Assert.True(ShellShortcuts.IsAccelerator(KeyModifiers.Meta));
            Assert.True(ShellShortcuts.IsAccelerator(KeyModifiers.Control));
            Assert.False(ShellShortcuts.IsAccelerator(KeyModifiers.Shift));
            Assert.False(ShellShortcuts.IsAccelerator(KeyModifiers.None));
            Assert.False(ShellShortcuts.IsAccelerator(KeyModifiers.Meta | KeyModifiers.Shift));

            // Windows:对话框开着、或焦点在文本框里时 Ctrl+K 不抢
            Assert.True(ShellShortcuts.CanStealAiShortcut(null, false));
            Assert.True(ShellShortcuts.CanStealAiShortcut(new Border(), false));
            Assert.False(ShellShortcuts.CanStealAiShortcut(new TextBox(), false));
            Assert.False(ShellShortcuts.CanStealAiShortcut(null, true));
            Assert.False(ShellShortcuts.CanStealAiShortcut(new Border(), true));
        });
    }

    // ---------------------------------------------------------------- 小工具

    private static void PumpDispatcher()
    {
        for (int i = 0; i < 20; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>把侧栏压成 "H:标题" / "I:键" 的行表(撑条行没有可视控件,不出现在这里)。</summary>
    private static List<string> DescribeSidebar(MainWindow window)
    {
        List<string> rows = new List<string>();
        Grid nav = C2bAssert.ByName<Grid>(window.Sidebar, "NavGrid");
        foreach (Control child in nav.Children)
        {
            if (child is TextBlock header)
            {
                rows.Add("H:" + header.Text);
            }
            else if (child is ToggleButton item)
            {
                rows.Add("I:" + item.Tag);
            }
        }
        return rows;
    }

    private static ToggleButton SidebarRow(MainWindow window, string navKey)
    {
        Grid nav = C2bAssert.ByName<Grid>(window.Sidebar, "NavGrid");
        return nav.Children.OfType<ToggleButton>().First(t => (string)t.Tag! == navKey);
    }

    /// <summary>菜单项取出来当 <see cref="NativeMenuItem"/> 用(<c>NativeMenu.Items</c> 的元素静态类型是基类)。</summary>
    private static NativeMenuItem Item(NativeMenu menu, int index)
    {
        return Assert.IsType<NativeMenuItem>(menu.Items[index]);
    }

    /// <summary>子菜单(顶层项的第二层)。</summary>
    private static NativeMenu Sub(NativeMenu menu, int index)
    {
        return Assert.IsType<NativeMenu>(Item(menu, index).Menu);
    }

    private static void AssertChecked(MainWindow window, string navKey, bool expected)
    {
        Assert.Equal(expected, SidebarRow(window, navKey).IsChecked);
    }
}
