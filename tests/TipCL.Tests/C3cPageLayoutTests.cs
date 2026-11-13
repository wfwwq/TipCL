using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C3c(三):三个浮层、区块/仪表盘集成、动效与泄漏。
///
/// - 浮层走页面的公开入口 <c>OpenPopupForDevShot</c>(Windows 的 <c>--pop</c> 挂点)驱动;
/// - 区块布局走 <c>BlockHost</c> 自己的状态机(隐藏 / 重排 / 重置,同 C3a 的纪律),
///   落盘目录由 <see cref="C3aHome"/> 注入到 <c>TestHome.Root</c> 之下(绝不碰用户数据目录);
/// - 全程序列 <c>Motion.Enabled = false</c>(测试进程没有动画时钟,见 D-98),
///   用例断言"东西立刻到位、不留计时器 / 订阅"。
/// </summary>
public class C3cPageLayoutTests
{
    private const string VersionsJson = """["1.20.1","fabric-loader-0.20.1-1.20.1"]""";

    private static C3cFakeApi Api()
    {
        return new C3cFakeApi()
            .Route("get_instances", """
                [{"name":"main","path":"C:\\mc\\main","versions":2,"mc":"1.20.1","pack":"","pack_version":"","mc_version":""},
                 {"name":"second","path":"C:\\mc\\second","versions":0,"mc":"","pack":"","pack_version":"","mc_version":""}]
                """)
            .Route("get_account_rows", """[{"name":"Steve","active":true}]""")
            .Route("get_settings", """{"default_memory_mb":2048}""")
            .Route("get_installed_versions", VersionsJson)
            .Route("java_combo_options", """[{"label":"自动选择","value":"自动选择"}]""")
            .Route("java_combo_label_for", "\"自动选择\"")
            .Route("get_installed_mod_entries", "[]")
            .Route("get_setting", "\"news\"")
            .Route("cached_news", "[]")
            .Route("fetch_news", "[]")
            .Route("get_total_playtime", "0")
            .Route("preflight_launch", """{"ok":true,"items":[]}""")
            .Route("launch_game", "\"task-1\"")
            .Route("cancel_task", "null")
            .Route("open_instance_folder", "\"C:\\\\mc\\\\main\"");
    }

    private static void Load(LaunchPage page)
    {
        Task task = page.EnsureLoadedAsync();
        C3cFiles.PumpTask(task);
        C3cFiles.Settle(12);
    }

    // ---------------------------------------------------------------- 实例切换浮层

    /// <summary>【Windows 源】<c>LaunchPage.Popups.cs:86-206</c>:切换浮层列出所有实例,当前实例行带「当前」标记,
    /// 别的行带「管理」按钮;当前实例下面展开版本子行。</summary>
    [Fact]
    public void Switch_popup_lists_instances_and_the_current_instances_versions()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = new LaunchPage();
            Load(page);

            page.OpenPopupForDevShot("instances");
            Assert.True(page.SwitchPopupOpenNow);
            StackPanel list = Assert.IsType<StackPanel>(page.SwitchListNow);

            // 2 个实例行 + 当前实例(1 条版本 + 1 条 fabric 版本)= 4
            Assert.Equal(4, list.Children.Count);
            Assert.Equal(I18n.L(C3cExpected.SearchPlaceholder), page.SwitchSearchNow!.Tag as string);
            Assert.True(C3cFiles.HasButton(list, I18n.L(C3cExpected.ManageText)), "非当前实例行要有「管理」按钮");
            Assert.True(C3cFiles.HasText(list, I18n.L(C3cExpected.CurrentMarkText)), "当前实例行要有「当前」标记");
        });
    }

    /// <summary>【Windows 源】<c>Popups.cs:118-206</c>:搜索框一改就重排行;没有匹配时显示「没有匹配的实例」。</summary>
    [Fact]
    public void Switch_popup_search_filters_the_rows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = new LaunchPage();
            Load(page);
            page.OpenPopupForDevShot("instances");

            page.SwitchSearchNow!.Text = "second";
            C3cFiles.Settle();
            StackPanel list = Assert.IsType<StackPanel>(page.SwitchListNow);
            Assert.Single(list.Children); // 只剩「second」那一行(它不是当前实例,没有版本子行)
            Assert.True(C3cFiles.HasButton(list, I18n.L(C3cExpected.ManageText)));

            page.SwitchSearchNow!.Text = "no-such-instance";
            C3cFiles.Settle();
            Assert.Single(list.Children);
            Assert.Equal(I18n.L(C3cExpected.NoMatchText), Assert.IsType<TextBlock>(list.Children[0]).Text);
        });
    }

    /// <summary>【Windows 源】<c>Popups.cs:234-244</c>:点版本子行 = 关浮层 + 选中该版本。</summary>
    [Fact]
    public void Switch_popup_version_row_selects_the_version_and_closes()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = new LaunchPage();
            Load(page);
            page.OpenPopupForDevShot("instances");

            page.PickVersionForTest("fabric-loader-0.20.1-1.20.1");
            Assert.False(page.SwitchPopupOpenNow);
            Assert.Equal("fabric-loader-0.20.1-1.20.1", page.CombosNow[1].Str());
            Assert.Equal("Fabric 0.20.1", page.ChipsNow[0]);
        });
    }

    /// <summary>【Windows 源】<c>Popups.cs:175-185</c>:点实例行 = 关浮层 + 切实例(并触发一次版本重载)。</summary>
    [Fact]
    public void Switch_popup_instance_row_switches_the_instance()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = new LaunchPage();
            Load(page);
            page.OpenPopupForDevShot("instances");

            int before = api.Count("get_installed_versions");
            page.PickInstanceForTest("second");
            // 切实例 → 版本重载 → SyncBanner → SyncHero 是一串异步续体,泵到英雄区标题也跟上为止
            C3cFiles.Pump(() => page.HeroTitleNow == "second");
            Assert.False(page.SwitchPopupOpenNow);
            Assert.True(api.Count("get_installed_versions") > before);
            Assert.Equal("second", page.CombosNow[0].Str());
            Assert.Equal("second", page.HeroTitleNow);
        });
    }

    /// <summary>【Windows 源】<c>Popups.cs:192-196</c>:当前实例没有版本时,浮层里给一句「先去下载装一个」。</summary>
    [Fact]
    public void Switch_popup_hints_when_the_current_instance_has_no_versions()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            api.Route("get_installed_versions", "[]");
            var page = new LaunchPage();
            Load(page);
            page.OpenPopupForDevShot("instances");
            StackPanel list = Assert.IsType<StackPanel>(page.SwitchListNow);
            Assert.Contains(C3cFiles.Texts(list), t => t.Text == I18n.L(C3cExpected.NoVersionsStatus));
        });
    }

    // ---------------------------------------------------------------- 公告 / 配置浮层

    /// <summary>【Windows 源】<c>Popups.cs:292-300</c>:公告浮层挂在新闻卡上方,内容是可滚动的新闻列表。</summary>
    [Fact]
    public void News_popup_wraps_the_news_host()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = new LaunchPage();
            Load(page);
            page.OpenPopupForDevShot("news");
            Assert.True(page.NewsPopupOpenNow);
            Border shell = Assert.IsType<Border>(page.NewsShellNow);
            Assert.Equal(420.0, shell.Width, 3);
            SmoothScroll scroll = Assert.IsType<SmoothScroll>(shell.Child);
            Assert.Same(page.NewsHostNow, scroll.Content);
        });
    }

    /// <summary>【Windows 源】<c>Popups.cs:24-40</c>:三个弹层外壳都是圆角 12 / 描边 1 / 内边距 12;
    /// 没有宿主窗口时 MaxHeight 就是 <c>PopShell</c> 给的 440 / 360(绑定取不到窗口 → 不写值)。</summary>
    [Fact]
    public void Popup_shells_share_the_windows_geometry()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = new LaunchPage();
            Load(page);
            page.OpenPopupForDevShot("config");
            page.OpenPopupForDevShot("news");

            Border config = Assert.IsType<Border>(page.ConfigShellNow);
            Border news = Assert.IsType<Border>(page.NewsShellNow);
            foreach (Border shell in new[] { config, news })
            {
                Assert.Equal(12.0, shell.CornerRadius.TopLeft, 3);
                Assert.Equal(1.0, shell.BorderThickness.Left, 3);
                Assert.Equal(12.0, shell.Padding.Left, 3);
            }

            Assert.Equal(360.0, config.Width, 3);
            Assert.Equal(440.0, config.MaxHeight, 3);
            Assert.Equal(420.0, news.Width, 3);
            Assert.Equal(360.0, news.MaxHeight, 3);
        });
    }

    /// <summary>【Windows 源】<c>Popups.cs:42-59</c>:弹层外壳的 MaxHeight 绑的是**宿主窗口**高度的 86%
    /// (WPF 绑 ActualHeight;Avalonia 用 Bounds.Height,同 C1c 的 SmoothScroll)。</summary>
    [Fact]
    public void Popup_max_height_follows_the_host_window()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var registry = new RegistryScope();
            using var shell = ShellFixture.Create();
            using var api = Api();
            var page = Assert.IsType<LaunchPage>(shell.Window.GetPage(C3cExpected.PageId));
            Load(page);
            page.OpenPopupForDevShot("config");
            C3cFiles.Settle();

            Border config = Assert.IsType<Border>(page.ConfigShellNow);
            double host = shell.Window.Bounds.Height;
            Assert.True(host > 0.0, "外壳窗口应当有真实高度");
            Assert.True(
                Math.Abs(config.MaxHeight - host * 0.86) < 0.5,
                "弹层 MaxHeight 应当跟着宿主窗口: MaxHeight=" + config.MaxHeight + " 窗口高=" + host);
            // 注:浮层外壳的底色是 DynamicResource,离屏下浮层内容不挂进窗口的资源树,解析出来是 null,
            // 所以本机**不验**浮层配色(已记入 PROGRESS 的"未验证项")。
        });
    }

    /// <summary>【Windows 源】<c>Popups.cs:303-336</c>:dev 截图挂点 —— 摘出浮层内容并关掉浮层;名字不认识返回 null。</summary>
    [Fact]
    public void Dev_shot_hooks_detach_the_popup_shell()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = new LaunchPage();
            Load(page);
            page.OpenPopupForDevShot("config");
            Assert.True(page.ConfigPopupOpenNow);

            Control? shell = page.DetachPopupForShot("config");
            Assert.IsType<Border>(shell);
            Assert.False(page.ConfigPopupOpenNow, "摘出来之后浮层要关掉");
            Assert.Null(page.DetachPopupForShot("nonsense"));
            Assert.Null(page.DetachPopupForShot("news")); // 没开过 → Child 为 null → 返回 null
        });
    }

    /// <summary>【Windows 源】<c>Popups.cs:303-317</c>:<c>OpenPopupForDevShot</c> 认 config / instances / news,其它名字什么都不做。</summary>
    [Fact]
    public void Open_popup_for_dev_shot_ignores_unknown_names()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = new LaunchPage();
            Load(page);
            page.OpenPopupForDevShot("nonsense");
            Assert.False(page.ConfigPopupOpenNow);
            Assert.False(page.SwitchPopupOpenNow);
            Assert.False(page.NewsPopupOpenNow);
        });
    }

    // ---------------------------------------------------------------- 「更多操作」菜单

    /// <summary>【Windows 源】<c>LaunchPage.cs:1526-1556</c>:菜单 7 项、**顺序照源码**。</summary>
    [Fact]
    public void More_menu_has_the_seven_windows_items_in_order()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = new LaunchPage();
            using var ui = UiHost.Show(page);
            Load(page);
            page.ShowMoreMenu(page.LaunchButtonNow);
            C3cFiles.Settle();

            ContextMenu menu = Assert.IsType<ContextMenu>(page.MoreMenuNow);
            Assert.Equal(
                C3cExpected.MoreMenuItems.Select(I18n.L).ToArray(),
                menu.Items.OfType<MenuItem>().Select(i => i.Header as string).ToArray());
            Assert.Same(page.LaunchButtonNow, menu.PlacementTarget);
        });
    }

    /// <summary>【Windows 源】同上的「打开实例文件夹」那一项:点了走 <c>open_instance_folder</c>。</summary>
    [Fact]
    public void More_menu_open_instance_folder_calls_the_backend()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = new LaunchPage();
            using var ui = UiHost.Show(page);
            Load(page);
            page.ShowMoreMenu(page.LaunchButtonNow);
            C3cFiles.Settle();

            MenuItem item = Assert.IsType<ContextMenu>(page.MoreMenuNow).Items.OfType<MenuItem>()
                .Single(i => (i.Header as string) == I18n.L("打开实例文件夹"));
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            C3cFiles.PumpCall(api, "open_instance_folder");
            Assert.Equal("main", JsonDocument.Parse(api.ParamsOf("open_instance_folder")).RootElement.GetProperty("name").GetString());
        });
    }

    /// <summary>【Windows 源】同上的「刷新新闻」那一项:点了重新拉新闻(走 get_setting)。</summary>
    [Fact]
    public void More_menu_refresh_news_reloads_the_news()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = new LaunchPage();
            using var ui = UiHost.Show(page);
            Load(page);
            int before = api.Count("get_setting");
            page.ShowMoreMenu(page.LaunchButtonNow);
            C3cFiles.Settle();

            MenuItem item = Assert.IsType<ContextMenu>(page.MoreMenuNow).Items.OfType<MenuItem>()
                .Single(i => (i.Header as string) == I18n.L("刷新新闻"));
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            C3cFiles.Pump(() => api.Count("get_setting") > before);
            Assert.True(api.Count("get_setting") > before);
        });
    }

    // ---------------------------------------------------------------- 区块 / 仪表盘集成

    /// <summary>【Windows 源】<c>LaunchPage.cs:476-483</c>:页面把 <c>pageId = "launch"</c> 交给 BlockHost,
    /// 布局因此落在 <c>&lt;dir&gt;/launch.json</c>(注入目录里,不碰用户数据目录)。</summary>
    [Fact]
    public void Block_layout_persists_under_the_launch_page_id()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var home = new C3aHome();
            using var api = Api();
            var page = new LaunchPage();
            BlockHost host = Assert.IsType<BlockHost>(page.BlocksNow);

            // 编辑态是隐藏 / 重排的入口条件(BlockHost.HideBlock 等只在 _edit 下生效,Windows 亦然)
            host.EnterEdit();
            host.HideBlock(host.CardOf("news")!.Spec);
            host.ExitEdit();
            C3cFiles.Settle();

            string[] files = System.IO.Directory.GetFiles(home.Dir, "*.json", System.IO.SearchOption.AllDirectories);
            Assert.Single(files);
            // PageLayoutStore 是所有页面共用一份文件(键是页面 id),不是"一页一个文件"
            Assert.Equal("ui_page_layouts.json", System.IO.Path.GetFileName(files[0]), StringComparer.OrdinalIgnoreCase);
            string json = System.IO.File.ReadAllText(files[0]);
            Assert.Contains("\"launch\"", json, StringComparison.Ordinal);
            Assert.Contains("\"news\"", json, StringComparison.Ordinal);
        });
    }

    /// <summary>【Windows 源】<c>BlockHost</c> 的显隐落盘:隐藏 news + 显示 tasks,换一个页面实例重读,布局还在。</summary>
    [Fact]
    public void Hidden_and_shown_blocks_survive_a_reload()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var home = new C3aHome();
            using var api = Api();
            var first = new LaunchPage();
            BlockHost host = Assert.IsType<BlockHost>(first.BlocksNow);
            host.EnterEdit();
            host.HideBlock(host.CardOf("news")!.Spec);
            host.Restore(host.CardOf("tasks")!.Spec);
            host.ExitEdit();
            C3cFiles.Settle();
            Assert.Equal(new[] { "tasks" }, C3aTree.RenderedIds(host).ToArray());

            home.ForgetCache();
            var second = new LaunchPage();
            BlockHost reloaded = Assert.IsType<BlockHost>(second.BlocksNow);
            Assert.Equal(new[] { "tasks" }, C3aTree.RenderedIds(reloaded).ToArray());
            Assert.Contains("news", reloaded.HiddenNow);
        });
    }

    /// <summary>【Windows 源】<c>BlockHost</c> 的重排落盘:把 tasks 拖到 news 前面,重载后顺序保持。</summary>
    [Fact]
    public void Block_order_survives_a_reload()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var home = new C3aHome();
            using var api = Api();
            var page = new LaunchPage();
            BlockHost host = Assert.IsType<BlockHost>(page.BlocksNow);
            host.EnterEdit();
            host.Restore(host.CardOf("tasks")!.Spec);

            // 用 C3a 的状态机拖:抓住 tasks 的卡,拖到 news 中点之上,松手
            BlockHost.BlockCard card = host.CardOf("tasks")!;
            host.BeginDragCore(card, card, null);
            host.DragTo(0.0);
            host.EndDrag(card);
            host.ExitEdit();
            C3cFiles.Settle();
            Assert.Equal(new[] { "tasks", "news" }, C3aTree.RenderedIds(host).ToArray());

            home.ForgetCache();
            var reloaded = new LaunchPage();
            Assert.Equal(new[] { "tasks", "news" }, C3aTree.RenderedIds(Assert.IsType<BlockHost>(reloaded.BlocksNow)).ToArray());
        });
    }

    /// <summary>【Windows 源】<c>BlockHost.ResetPage</c>:「重置本页」回到页面给的默认文档(只有 news 可见)。</summary>
    [Fact]
    public void Reset_page_restores_the_default_layout()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var home = new C3aHome();
            using var api = Api();
            var page = new LaunchPage();
            BlockHost host = Assert.IsType<BlockHost>(page.BlocksNow);
            host.EnterEdit();
            host.HideBlock(host.CardOf("news")!.Spec);
            host.Restore(host.CardOf("log")!.Spec);
            host.ExitEdit();
            C3cFiles.Settle();

            host.ResetPage();
            C3cFiles.Settle();
            Assert.Equal(new[] { "news" }, C3aTree.RenderedIds(host).ToArray());
            Assert.Equal(
                new[] { "tasks", "playtime", "notes", "log" },
                C3cExpected.BlockIds.Where(id => host.HiddenNow.Contains(id)).ToArray());
            // 重置是"保存默认文档"的路径,所以盘上那份也回到默认(不再有隐藏项)
            Assert.False(string.IsNullOrEmpty(home.Dir));
        });
    }

    /// <summary>【Windows 源】旧卡片画布:字段保留、不挂树、**页面不订阅它的变更**(照抄 Windows 的现状)。</summary>
    [Fact]
    public void Dashboard_host_still_works_but_the_page_does_not_persist_it()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var home = new C3aHome();
            using var api = Api();
            var page = new LaunchPage();
            int changed = 0;
            page.DashNow.Changed += delegate
            {
                changed++;
            };
            page.DashNow.AddCard("clock");
            Assert.True(page.DashNow.Has("clock"));
            Assert.Equal(1, changed);
            Assert.Null(page.DashNow.Parent);

            // 没有任何布局文件被写出来(Windows 的启动页也不订阅 DashHost.Changed)
            Assert.Empty(System.IO.Directory.GetFiles(home.Dir, "*.json", System.IO.SearchOption.AllDirectories));
        });
    }

    // ---------------------------------------------------------------- 动效 / 泄漏

    /// <summary>【Windows 源 + 本机实测】关掉动画时一切都立刻到位:没有位移、没有动画挂件、
    /// 状态机照改(与 C3a 的"交换即落位"同一口径)。</summary>
    [Fact]
    public void With_motion_disabled_everything_lands_immediately()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            Motion.Enabled = false;
            using var api = Api();
            var page = new LaunchPage();
            using var ui = UiHost.Show(page);
            Load(page);

            Border shell = Assert.IsType<Border>(page.LaunchShellNow);
            AssertNoMotion(shell);

            C3cFiles.Click(page.LaunchButtonNow);
            C3cFiles.PumpCall(api, "launch_game");
            // 立刻生效:主按钮外壳收起、停止外壳显示、进度条可见(不用等任何动画)
            Assert.True(page.StopShownNow);
            Assert.False(page.LaunchShownNow);
            Assert.True(page.ProgressShownNow);
            AssertNoMotion(shell);
        });
    }

    /// <summary>「没有动效挂着」的判据:不透明度 1、变换是恒等(或压根没有)。
    /// <c>Motion.HoverLift</c> 给 Border 挂了一个静止态 = 恒等的变换,所以不能直接要求 null。</summary>
    private static void AssertNoMotion(Control control)
    {
        Assert.Equal(1.0, control.Opacity, 3);
        if (control.RenderTransform is { } transform)
        {
            Assert.Equal(Matrix.Identity, transform.Value);
        }
    }

    /// <summary>【本机实测】构造完没有任何计时器在跑;内存滑杆的防抖计时器只在改动时起。</summary>
    [Fact]
    public void No_timer_is_left_running_by_construction()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = new LaunchPage();
            using var ui = UiHost.Show(page);
            Load(page);
            C3cFiles.Settle();
            Assert.False(page.MemorySaveTimerRunningNow);
        });
    }

    /// <summary>【Windows 源】在外壳里切走再切回:页面的状态变更订阅跟着可视树挂上 / 摘掉(不留订阅)。</summary>
    [Fact]
    public void Switching_pages_in_the_shell_attaches_and_detaches_the_subscriptions()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var registry = new RegistryScope();
            registry.Register("settings", () => new ProbePage());
            using var shell = ShellFixture.Create();
            var page = Assert.IsType<LaunchPage>(shell.Window.GetPage(C3cExpected.PageId));
            C3cFiles.Settle();
            Assert.True(page.SyncHooksAttached);

            shell.Window.Navigate("settings", instant: true);
            C3cFiles.Settle();
            Assert.False(page.SyncHooksAttached, "切走之后不许留着订阅");
            Assert.Equal("settings", shell.Window.CurrentPageId);

            shell.Window.Navigate(C3cExpected.PageId, instant: true);
            C3cFiles.Settle();
            Assert.True(page.SyncHooksAttached, "切回来要重新挂上");
            Assert.Same(page, shell.Window.CurrentPage);
        });
    }

    /// <summary>【本机实测】那条 MaxHeight 绑定可以显式释放(测试收尾用),释放后快照如实变 false。</summary>
    [Fact]
    public void Blocks_height_binding_can_be_released()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            Assert.True(page.BlocksHeightBindingAlive);
            page.ReleaseBlocksHeightBinding();
            Assert.False(page.BlocksHeightBindingAlive);
        });
    }

    /// <summary>【Windows 源】<c>OnHidden</c> 在 Windows 的默认构建里**不重写**(只在 <c>#if IMMERSIVE</c> 里有):
    /// mac 侧同样不重写,所以调它什么都不做、也不抛。</summary>
    [Fact]
    public void OnHidden_does_nothing_in_the_default_build()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = new LaunchPage();
            Load(page);
            bool onImageBefore = page.OnImageNow;
            string statusBefore = page.StatusTextNow;
            page.OnHidden();
            Assert.Equal(onImageBefore, page.OnImageNow);
            Assert.Equal(statusBefore, page.StatusTextNow);
        });
    }

    /// <summary>【Windows 源】<c>OnShown</c> 会把主题相关的柔光重取一次(<c>ApplyHeroTone</c> + <c>_dash</c> 刷新主题视觉)。</summary>
    [Fact]
    public void OnShown_refreshes_the_theme_visuals_without_throwing()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = Api();
            var page = new LaunchPage();
            using var ui = UiHost.Show(page);
            Load(page);

            Border shell = Assert.IsType<Border>(page.LaunchShellNow);
            DropShadowEffect glow = Assert.IsType<DropShadowEffect>(shell.Effect);
            Color before = glow.Color;
            page.OnShown();
            C3cFiles.Settle();
            Assert.Equal(before, glow.Color);
            Assert.NotNull(page.Content);
        });
    }
}
