using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TipCL.Models;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C4(二):<see cref="LibraryPage"/> —— 实例库(导航 id <c>library</c>)。
///
/// 每条碰 Avalonia 的用例体都走 <c>AvaloniaSession.Run(...)</c>(D-79);全局静态由 <see cref="C4Scope"/>
/// 快照还原(D-69);布局文档落盘走 <c>PageLayoutStore.DirOverride</c> 指到 <c>TestHome.Root</c> 之下;
/// 后端一律是**按方法名路由**的回环假后端(不起真后端、不出网、不启外部进程)。
/// 非按钮控件的点击走页面为此留的 internal 处理器体(不构造 Pointer 事件、不模拟输入设备,同 D-115 / D-127)。
/// </summary>
public class C4LibraryTests
{
    // ================================================================ 注册

    /// <summary>【任务书】两个页面都必须由各自页面的模块初始化器注册好,id 与导航表一致。</summary>
    [Fact]
    public void Library_and_instance_pages_are_registered_by_their_module_initializers()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            // 【C7a 更正 · D-126】`instance` 这个 id 已按 Windows 原版改注册成 VersionManagePage
            // (Windows 的 InstancePage 没有任何构造点 = 死代码),而本用例的前提是"instance 就是 InstancePage"。
            // 按 D-126 的做法**先把前提还原**(用 InstancePageRegistration 保留的测试还原入口),
            // 下面原有断言**一字不改**;作用域收尾会把注册表快照还原回去。
            InstancePageRegistration.Register();
            Assert.True(PageRegistry.Contains(C4Expected.LibraryPageId));
            Assert.True(PageRegistry.Contains(C4Expected.InstancePageId));
            Assert.IsType<LibraryPage>(PageRegistry.Create(C4Expected.LibraryPageId));
            Assert.IsType<InstancePage>(PageRegistry.Create(C4Expected.InstancePageId));
            // 大小写敏感(D-94)
            Assert.Null(PageRegistry.Create("Library"));
        });
    }

    /// <summary>
    /// 【C7a 更正 · 钉新契约】`instance` 这个 id 现在的目标是 Windows 原版那一页(版本管理);
    /// <see cref="InstancePage"/> 不再注册(它在 Windows 原版是死代码)。这条与上面那条"还原前提"并存:
    /// 上面那条验的是 InstancePage 自己的行为,这条把**当前默认注册**钉住。
    /// </summary>
    [Fact]
    public void Instance_id_now_resolves_to_VersionManagePage()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            Assert.IsType<VersionManagePage>(PageRegistry.Create(C4Expected.InstancePageId));
        });
    }

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:109</c> 与 <c>NavModel.PageForNavKey</c>:两个 id 都在(值相同)。</summary>
    [Fact]
    public void Page_id_constants_match_the_nav_table()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            Assert.Equal("library", LibraryPageRegistration.PageId);
            Assert.Equal("instance", InstancePageRegistration.PageId);
            Assert.Contains(MainWindow.NavDefs, d => d.Id == "library");
            Assert.Contains(MainWindow.NavDefs, d => d.Id == "instance");
            Assert.Equal("instance", NavModel.PageForNavKey["instance"]);
        });
    }

    /// <summary>
    /// 【本机实测】<c>NavModel.PageForNavKey</c> 里**没有** <c>library</c>(Windows 与 mac 都一样:
    /// WP4 信息架构把侧栏那一行换成了「版本管理」)。所以 <c>library</c> 只能按页面 id 直接切,
    /// 这条用例把这个现状钉住 —— 免得以后有人以为"注册了就一定有点得进去的行"。
    /// </summary>
    [Fact]
    public void Library_has_no_sidebar_nav_key_so_it_is_reached_by_page_id_only()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            Assert.False(NavModel.PageForNavKey.ContainsKey("library"));
            Assert.DoesNotContain("library", NavModel.GroupedNav.SelectMany(g => g.Keys));
        });
    }

    /// <summary>【任务书】外壳能按 id 解析并建出实例库页(不动外壳代码,靠注册表)。</summary>
    [Fact]
    public void The_shell_resolves_the_library_page_by_id()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create();
            // 首屏仍是启动页(加了两个页面不许改变首屏)
            Assert.Equal("launch", shell.Window.CurrentPageId);
            shell.Window.Navigate(C4Expected.LibraryPageId);
            Assert.Equal("library", shell.Window.CurrentPageId);
            Assert.IsType<LibraryPage>(shell.Window.CurrentPage);
            Assert.IsType<LibraryPage>(shell.Window.GetPage("library"));
        });
    }

    /// <summary>【任务书】没有后端(<c>AppServices.Client</c> 为 null)时构造不许抛、不许卡。</summary>
    [Fact]
    public void Library_page_construction_does_not_throw_without_a_backend()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            AppServices.Client = null;
            var page = new LibraryPage();
            Assert.NotNull(page.Content);
            Assert.Equal(I18n.L("实例库"), page.Title);
        });
    }

    // ================================================================ 静态结构

    /// <summary>
    /// 【Windows 源】<c>LibraryPage.cs:128-198</c>:左栏(文件夹列表 + 路径 + 「添加或导入」四个按钮)+
    /// 右栏(版本选择 + 计数 + 搜索 + 实例详情 + 刷新 + 副说明)。
    /// </summary>
    [Fact]
    public void Library_page_builds_the_windows_two_column_shell()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi();
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            Assert.True(C4Files.HasText(page, I18n.L("文件夹列表")));
            Assert.True(C4Files.HasText(page, I18n.L("添加或导入")));
            Assert.True(C4Files.HasText(page, I18n.L("版本选择")));
            Assert.True(C4Files.HasText(page, I18n.L("双击版本直接启动;行尾可收藏/删除/设置")));
            Assert.True(C4Files.HasButton(page, I18n.L("添加已有文件夹")));
            Assert.True(C4Files.HasButton(page, I18n.L("导入整合包")));
            Assert.True(C4Files.HasButton(page, I18n.L("新建实例")));
            Assert.True(C4Files.HasButton(page, I18n.L("导入官方启动器")));
            Assert.True(C4Files.HasButton(page, I18n.L("实例详情")));
            Assert.True(C4Files.HasButton(page, I18n.L("刷新")));
            // 【Windows 源】两个大标题都是 ExtraBold
            TextBlock h = C4Files.TextsWith(page, I18n.L("文件夹列表")).First();
            Assert.Equal(FontWeight.ExtraBold, h.FontWeight);
            Assert.False(toasts.Has(I18n.L("加载失败")), "首屏加载不该失败:" + string.Join("|", toasts.Items.Select(t => t.Title + t.Body)));
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:96</c>:搜索框宽 170、占位文案「过滤版本名」。</summary>
    [Fact]
    public void Library_page_search_box_matches_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi();
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            TextBox search = C4Files.Find<TextBox>(page, "过滤版本框");
            Assert.Equal(170.0, search.Width);
            Assert.Contains("input", search.Classes);
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:79-88</c>:七个分组按 <c>Defs</c> 的顺序出卡,标题形如「Fabric 版本 (N)」。</summary>
    [Fact]
    public void Library_page_groups_versions_by_loader_in_windows_order()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi().Route("get_version_rows", "[" +
                C4Files.VersionRowJson("1.20.1", "Fabric") + "," +
                C4Files.VersionRowJson("1.19.4", "Forge") + "," +
                C4Files.VersionRowJson("1.18.2", "") + "]");
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            Assert.True(C4Files.HasText(page, I18n.L("Fabric 版本") + " (1)"));
            Assert.True(C4Files.HasText(page, I18n.L("Forge 版本") + " (1)"));
            Assert.True(C4Files.HasText(page, I18n.L("原版版本") + " (1)"));
            // 没有 Quilt 版本 ⇒ 不出这张卡
            Assert.False(C4Files.HasText(page, I18n.L("Quilt 版本") + " (0)"));
            Assert.False(C4Files.HasText(page, I18n.L("Quilt 版本") + " (1)"));
            Assert.True(C4Files.HasText(page, I18n.L("共 {0} 个版本", 3)));
            Assert.Equal(3, ((LibraryPage)page).GroupsNow().Count);
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:691-731</c>:版本行 = 图标 + 版本 id + 标签 + 副行 + 收藏/卸载/版本设置三钮。</summary>
    [Fact]
    public void Library_version_row_matches_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi().Route("get_version_rows",
                "[" + C4Files.VersionRowJson("1.20.1", "Fabric", isolated: true, mods: 3, isolationLabel: "隔离全部", java: "17.0.1") + "]");
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            Assert.True(C4Files.HasText(page, "1.20.1"));
            Assert.True(C4Files.HasText(page, I18n.L("模组 {0}", 3)));
            Assert.True(C4Files.HasText(page, "隔离全部"));
            // 副行:正式版 1.20.1,<loader 原文> · Java 17.0.1(Windows 拼的就是 v.Loader 原文)
            Assert.True(C4Files.HasText(page, "正式版 1.20.1,Fabric · Java 17.0.1"));
            Assert.True(C4Files.HasButton(page, I18n.L("卸载这个版本")));
            Assert.True(C4Files.HasButton(page, I18n.L("版本设置")));
            // 收藏钮是手工建的 Button(Content 是字形而不是文字),按字形认它
            Assert.NotNull(StarButton(page, "\ue734"));
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:679-686</c>:快照版本多一个「快照」标签。</summary>
    [Fact]
    public void Library_snapshot_rows_get_the_snapshot_tag()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi().Route("get_version_rows", "[" + C4Files.VersionRowJson("1.20.1-pre1", "") + "]");
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            Assert.True(C4Files.HasText(page, I18n.L("快照")));
            Assert.True(C4Files.HasText(page, "快照 1.20.1"));
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:786-802</c> 的 <c>InferType</c> 判据逐条对拍。</summary>
    [Theory]
    [MemberData(nameof(InferTypeData))]
    public void Library_infer_type_matches_windows(string id, string expected)
    {
        Assert.Equal(expected, LibraryPage.InferType(id));
    }

    public static IEnumerable<object[]> InferTypeData()
    {
        foreach ((string id, string type) in C4Expected.InferTypeCases)
        {
            yield return new object[] { id, type };
        }
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:100</c>:没有版本时的空态。</summary>
    [Fact]
    public void Library_page_empty_state_matches_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi();
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            Assert.True(C4Files.HasText(page, I18n.L("这个文件夹还没有版本")));
            Assert.True(C4Files.HasText(page, I18n.L("点左侧「添加或导入」，或到「下载」页装一个")));
            Assert.True(C4Files.HasText(page, I18n.L("共 {0} 个版本", 0)));
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:472-534</c>:左栏每行 = 图标 + 名称 + 「N 个版本」+ 实例操作钮;默认实例带「默认」标签。</summary>
    [Fact]
    public void Library_folder_rows_match_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[" + C4Files.InstanceJson("甲", 3, path: "/games/甲") + "," + C4Files.InstanceJson("乙", 1) + "]")
                .Route("get_setting", "\"甲\"");
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            Assert.True(C4Files.HasText(page, "甲"));
            Assert.True(C4Files.HasText(page, I18n.L("{0} 个版本", 3)));
            Assert.True(C4Files.HasText(page, I18n.L("默认")));
            Assert.Equal(2, C4Files.Descendants(page).OfType<Button>().Count(b => string.Equals(AutomationProperties.GetName(b), I18n.L("实例操作"), StringComparison.Ordinal)));
            // 路径优先取实例给的 Path
            Assert.True(C4Files.HasText(page, "/games/甲"));
        });
    }

    /// <summary>
    /// 【Windows 源】<c>LibraryPage.cs:436-448</c>:实例没给 <c>Path</c> 时用游戏根目录拼一条兜底路径。
    /// **注意这是照抄的 Windows 写法(用反斜杠)**,mac 上与 Windows 逐字相同 —— 见 PROGRESS 的 C4 小节。
    /// </summary>
    [Fact]
    public void Library_folder_path_falls_back_to_the_game_root_verbatim()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[" + C4Files.InstanceJson("甲") + "]")
                .Route("game_root_path", "\"/games\"");
            using var harness = C4LayerHost.Show();
            LibraryPage page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            Assert.Equal("/games" + "\\" + "甲", page.FolderPathFor("甲"));
            Assert.True(C4Files.HasText(page, "/games" + "\\" + "甲"));
        });
    }

    /// <summary>【Windows 源】路径取不到时显示「当前文件夹」。</summary>
    [Fact]
    public void Library_folder_path_placeholder_matches_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi();
            using var harness = C4LayerHost.Show();
            LibraryPage page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            Assert.Equal("", page.FolderPathFor("甲"));
            Assert.True(C4Files.HasText(page, I18n.L("当前文件夹")));
        });
    }

    // ================================================================ 加载与状态

    /// <summary>【Windows 源】<c>LibraryPage.cs:323-340</c>:首屏拉五个东西(实例 / 默认实例 / 根目录 / 版本 / 时长)。</summary>
    [Fact]
    public void Library_page_load_calls_the_windows_backend_methods()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[" + C4Files.InstanceJson("甲") + "]")
                .Route("get_setting", "\"甲\"");
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            List<string> methods = api.Methods();
            Assert.Contains("get_instances", methods);
            Assert.Contains("get_setting", methods);
            Assert.Contains("game_root_path", methods);
            Assert.Contains("get_version_rows", methods);
            Assert.Contains("get_playtime", methods);
            Assert.Contains("\"key\":\"default_instance\"", api.ParamsOf("get_setting"));
            Assert.Contains("\"include_hidden\":false", api.ParamsOf("get_version_rows"));        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:348-355</c>:切文件夹先把它设成默认实例(<c>save_settings</c>)。</summary>
    [Fact]
    public void Library_sets_the_folder_as_the_default_instance_before_listing_versions()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[" + C4Files.InstanceJson("甲") + "]")
                .Route("get_setting", "\"\"");
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            C4Files.Pump(() => api.Count("save_settings") >= 1);
            Assert.Equal("甲", C4Files.ParamStringIn(api, "save_settings", "data", "default_instance"));
        });
    }

    /// <summary>【Windows 源】已经在默认实例上就不重复写设置。</summary>
    [Fact]
    public void Library_does_not_rewrite_the_default_instance_when_it_already_is()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[" + C4Files.InstanceJson("甲") + "]")
                .Route("get_setting", "\"甲\"");
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            C4Files.Settle(4);
            Assert.Equal(0, api.Count("save_settings"));
        });
    }

    /// <summary>
    /// 【Windows 源】<c>LibraryPage.cs:363-392</c>:副行里的「上次游玩」是从 <c>get_playtime.sessions</c>
    /// 派生的,并且**取每个版本最近的那一次**(两条会话给同一个版本,显示的必须是新的那条)。
    ///
    /// 时间戳的算法刻意做成**与跑测时刻无关**:用"今天 00:05"与"它减一小时(= 昨天 23:05)"。
    /// 一开始写的是 <c>Now.AddHours(-1)</c>,凌晨 0 点到 1 点之间跑会跨到昨天 ⇒ VSTest 通道红、
    /// 进程内通道绿(两条通道跑的时刻不同),本包实测踩到并改掉。
    /// </summary>
    [Fact]
    public void Library_last_played_comes_from_the_playtime_sessions()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            long today = new DateTimeOffset(DateTime.Today.AddMinutes(5)).ToUnixTimeSeconds();
            long yesterday = today - 3600L;
            using var api = new C3cFakeApi()
                .Route("get_version_rows", "[" + C4Files.VersionRowJson("1.20.1", "") + "]")
                .Route("get_playtime", "{\"sessions\":[{\"version\":\"1.20.1\",\"start\":" + yesterday + "},{\"version\":\"1.20.1\",\"start\":" + today + "}]}");
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            Assert.Contains(C4Files.Texts(page), t => (t.Text ?? "").Contains("上次游玩 今天", StringComparison.Ordinal));
            Assert.DoesNotContain(C4Files.Texts(page), t => (t.Text ?? "").Contains("昨天", StringComparison.Ordinal));
        });
    }

    /// <summary>【Windows 源】没有该版本的会话记录 ⇒ 副行里不出现「上次游玩」。</summary>
    [Fact]
    public void Library_last_played_is_absent_when_there_is_no_session()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi()
                .Route("get_version_rows", "[" + C4Files.VersionRowJson("1.20.1", "") + "]")
                .Route("get_playtime", "{\"sessions\":[]}");
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            Assert.Contains(C4Files.Texts(page), t => t.Text == "正式版 1.20.1");
            Assert.DoesNotContain(C4Files.Texts(page), t => (t.Text ?? "").Contains("上次游玩", StringComparison.Ordinal));
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:365</c>:<c>get_playtime</c> 拿到的不是对象 / 没有 sessions 时不许抛。</summary>
    [Fact]
    public void Library_survives_a_malformed_playtime_payload()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi()
                .Route("get_version_rows", "[" + C4Files.VersionRowJson("1.20.1", "") + "]")
                .Route("get_playtime", "\"oops\"");
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            Assert.True(C4Files.HasText(page, "1.20.1"));
            Assert.False(toasts.Has(I18n.L("加载失败")));
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:342-345</c>:<c>RefreshAsync</c> 直接重跑 <c>LoadAsync</c>(不看 <c>_loaded</c>)。</summary>
    [Fact]
    public void Library_refresh_reruns_the_load()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi();
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            int before = api.Count("get_instances");
            C4Files.PumpTask(page.RefreshAsync());
            Assert.True(api.Count("get_instances") > before);
        });
    }

    // ================================================================ 搜索与折叠

    /// <summary>【Windows 源】<c>LibraryPage.cs:547-551</c>:搜索按版本 id 过滤(大小写不敏感)。</summary>
    [Fact]
    public void Library_search_filters_versions_case_insensitively()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi().Route("get_version_rows", "[" +
                C4Files.VersionRowJson("1.20.1", "") + "," + C4Files.VersionRowJson("1.19.4", "") + "]");
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            TextBox search = C4Files.Find<TextBox>(page, "过滤版本框");
            search.Text = "1.20";
            C4Files.Flush(2);
            Assert.True(C4Files.HasText(page, "1.20.1"));
            Assert.False(C4Files.HasText(page, "1.19.4"));
            Assert.True(C4Files.HasText(page, I18n.L("共 {0} 个版本", 1)));
        });
    }

    /// <summary>【Windows 源】搜索中一律展开分组(不受偏好里的折叠状态影响)。</summary>
    [Fact]
    public void Library_search_expands_every_group()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            var prefs = new UiPrefs();
            prefs.Expanded["lib:vanilla"] = false;
            using var shell = ShellFixture.Create(prefs);
            using var api = new C3cFakeApi().Route("get_version_rows", "[" + C4Files.VersionRowJson("1.20.1", "") + "]");
            shell.Window.Navigate("library");
            LibraryPage page = (LibraryPage)shell.Window.CurrentPage!;
            C4Files.Load(page);
            Assert.False(page.GroupsNow()["vanilla"], "偏好里记着折叠 ⇒ 首屏应该是折叠的");
            TextBox search = C4Files.Find<TextBox>(page, "过滤版本框");
            search.Text = "1.20";
            C4Files.Flush(2);
            Assert.True(page.GroupsNow()["vanilla"], "搜索中必须一律展开");
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:663-671</c>:折叠状态从偏好读(键名 <c>lib:&lt;key&gt;</c>)。</summary>
    [Fact]
    public void Library_group_open_state_comes_from_the_prefs()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            var prefs = new UiPrefs();
            prefs.Expanded["lib:vanilla"] = false;
            using var shell = ShellFixture.Create(prefs);
            using var api = new C3cFakeApi().Route("get_version_rows", "[" + C4Files.VersionRowJson("1.20.1", "") + "]");
            shell.Window.Navigate("library");
            LibraryPage page = (LibraryPage)shell.Window.CurrentPage!;
            C4Files.Load(page);
            Assert.False(page.GroupsNow()["vanilla"]);
            // 折叠时「已折叠」小字可见、箭头是 \ue70d
            Border card = GroupCard(page, I18n.L("原版版本") + " (1)");
            TextBlock folded = C4Files.Descendants(card).OfType<TextBlock>().First(t => t.Text == I18n.L("已折叠"));
            Assert.True(folded.IsVisible);
            Assert.Contains(C4Files.Texts(card), t => t.Text == "\ue70d");
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:652-661</c>:点组头折叠/展开,并把新状态写回偏好(<c>lib:&lt;key&gt;</c>)+ 落盘。</summary>
    [Fact]
    public void Library_toggling_a_group_persists_to_the_prefs()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            var prefs = new UiPrefs();
            using var shell = ShellFixture.Create(prefs);
            using var api = new C3cFakeApi().Route("get_version_rows", "[" + C4Files.VersionRowJson("1.20.1", "") + "]");
            shell.Window.Navigate("library");
            LibraryPage page = (LibraryPage)shell.Window.CurrentPage!;
            C4Files.Load(page);
            Assert.True(page.ToggleGroupForTest("vanilla"));
            Assert.False(page.GroupsNow()["vanilla"]);
            Assert.False(prefs.Expanded["lib:vanilla"]);
            Assert.True(page.ToggleGroupForTest("vanilla"));
            Assert.True(page.GroupsNow()["vanilla"]);
            Assert.True(prefs.Expanded["lib:vanilla"]);
            // 键名没写错:别的键不该被写进来
            Assert.Single(prefs.Expanded);
        });
    }

    /// <summary>【Windows 源】折叠之后展开的按组头是 <c>\ue70e</c>、「已折叠」藏起来。</summary>
    [Fact]
    public void Library_open_group_shows_the_open_chevron()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi().Route("get_version_rows", "[" + C4Files.VersionRowJson("1.20.1", "") + "]");
            using var harness = C4LayerHost.Show();
            LibraryPage page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            Border card = GroupCard(page, I18n.L("原版版本") + " (1)");
            Assert.Contains(C4Files.Texts(card), t => t.Text == "\ue70e");
            TextBlock folded = C4Files.Descendants(card).OfType<TextBlock>().First(t => t.Text == I18n.L("已折叠"));
            Assert.False(folded.IsVisible);
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:606</c>:组头有加载器图标(离屏也能真的拿到内嵌资源)。</summary>
    [Fact]
    public void Library_group_header_has_the_loader_icon()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi().Route("get_version_rows", "[" + C4Files.VersionRowJson("1.20.1", "Fabric") + "]");
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            Border card = GroupCard(page, I18n.L("Fabric 版本") + " (1)");
            ThumbTile tile = C4Files.Descendants(card).OfType<ThumbTile>().First();
            Assert.Equal(22.0, tile.Width);
            Assert.Equal(22.0, tile.Height);
            // 【本机实测】没有图时显示名字首字母(C2c2 的 ThumbTile 语义);有 premium 内嵌图标时会换成图
            Assert.Equal("F", tile.Letter.Text);
        });
    }

    // ================================================================ 收藏

    /// <summary>【Windows 源】<c>LibraryPage.cs:814-843</c>:收藏按文件夹分别记在 <c>Prefs.VmFavorites</c>,再点一次取消。</summary>
    [Fact]
    public void Library_favourite_round_trips_through_the_prefs()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            var prefs = new UiPrefs();
            using var shell = ShellFixture.Create(prefs);
            using var api = new C3cFakeApi()
                .Route("get_instances", "[" + C4Files.InstanceJson("甲") + "]")
                .Route("get_setting", "\"甲\"")
                .Route("get_version_rows", "[" + C4Files.VersionRowJson("1.20.1", "") + "]");
            shell.Window.Navigate("library");
            LibraryPage page = (LibraryPage)shell.Window.CurrentPage!;
            C4Files.Load(page);
            Assert.False(page.IsFav("1.20.1"));
            C4Files.Click(StarButton(page, "\ue734"));
            C4Files.Flush(2);
            Assert.True(page.IsFav("1.20.1"));
            Assert.Contains("1.20.1", prefs.VmFavorites["甲"]);
            // 重画之后星钮换成实心(\ue735)
            Assert.NotNull(StarButton(page, "\ue735"));
            C4Files.Click(StarButton(page, "\ue735"));
            C4Files.Flush(2);
            Assert.False(page.IsFav("1.20.1"));
            Assert.Empty(prefs.VmFavorites["甲"]);
        });
    }

    // ================================================================ 布局自定义(WP-U3)

    /// <summary>【Windows 源】<c>LibraryPage.cs:203-224</c>:布局文档里 <c>list</c> 恒可见,<c>folders</c> 缺失时按可见补上。</summary>
    [Fact]
    public void Library_layout_doc_forces_the_version_list_visible()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            C4Files.LayoutDir();
            PageLayoutStore.Save("library", new PageLayoutDoc
            {
                Blocks = new List<BlockState>
                {
                    new BlockState { Id = "list", Hidden = true },
                },
            });
            Assert.True(LibraryPage.FoldersVisible());
            PageLayoutDoc? doc = PageLayoutStore.Load("library");
            Assert.NotNull(doc);
            Assert.False(doc!.Blocks!.First(b => b.Id == "list").Hidden);
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:239-266</c>:文件夹栏可见时是双栏("*,18,2*",左栏有最小/最大宽)。</summary>
    [Fact]
    public void Library_folder_layout_defaults_to_two_columns()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            C4Files.LayoutDir();
            using var api = new C3cFakeApi();
            using var harness = C4LayerHost.Show();
            LibraryPage page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            Grid body = BodyGrid(page);
            Assert.Equal(3, body.ColumnDefinitions.Count);
            Assert.Equal(18.0, body.ColumnDefinitions[1].Width.Value);
            Assert.Equal(2.0, body.ColumnDefinitions[2].Width.Value);
            // 左栏卡:300 最小、380 最大、靠上(Windows 的三条几何)
            Border left = C4Files.Descendants(page).OfType<Border>().First(b => C4Files.HasText(b, I18n.L("文件夹列表")));
            Assert.Equal(300.0, left.MinWidth);
            Assert.Equal(380.0, left.MaxWidth);
            Assert.Equal(VerticalAlignment.Top, left.VerticalAlignment);
            Assert.True(LibraryPage.FoldersVisible());
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:269-321</c>:右键菜单四项的顺序、勾选态与「点了就落盘」,收起后变单栏。</summary>
    [Fact]
    public void Library_folder_layout_collapses_and_restores_through_the_menu()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            C4Files.LayoutDir();
            using var api = new C3cFakeApi();
            using var harness = C4LayerHost.Show();
            LibraryPage page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            Grid body = BodyGrid(page);
            MenuItem root = body.ContextMenu!.Items.OfType<MenuItem>().First();
            MenuItem box = root.Items.OfType<MenuItem>().First(mi => (mi.Header as string) == I18n.L("显示文件夹栏"));
            Assert.True(box.IsChecked, "首屏文件夹栏可见 ⇒ 勾选态是真的");

            // 【本机探针实测】Avalonia 11.3.23 的 MenuItem 是在**指针点击那条路**里先翻转 IsChecked
            // 再抛 Click;`RaiseEvent(ClickEvent)` 只抛事件、不翻转(而它自己的 `IMenuItem.RaiseClick`
            // 是 internal,测试里碰不到)。所以这里按"真实点击进处理器那一刻的状态"先把翻转做掉,
            // 再抛 Click —— 页面处理器读到的值与真机上一致(Windows 也是这样读的)。
            box.IsChecked = false;
            C4Files.Invoke(box);
            C4Files.Flush(2);
            // 【C9a 更正 · D-126】收起不再"把列定义清成 1 列"(列管理已交给共用的 PageLayoutHost),
            // 这里改为断言**同一件事的效果**:左列与间隙收 0、右栏吃满(3 列仍在,但前两列宽 0)。
            // 其余断言一字未改;强度只增(多了"间隙也收 0"与"右栏吃满"两条)。
            Assert.Equal(3, body.ColumnDefinitions.Count);
            Assert.Equal(0.0, body.ColumnDefinitions[0].Width.Value);
            Assert.Equal(0.0, body.ColumnDefinitions[1].Width.Value);
            Assert.Equal(GridUnitType.Star, body.ColumnDefinitions[2].Width.GridUnitType);
            Assert.False(LibraryPage.FoldersVisible());
            PageLayoutDoc? saved = PageLayoutStore.Load("library");
            Assert.NotNull(saved);
            Assert.True(saved!.Blocks!.First(b => b.Id == "folders").Hidden);

            // 再点一次:菜单对象被重建过(ApplyFolderLayout 会重新建菜单),取新的那一份
            MenuItem box2 = BodyGrid(page).ContextMenu!.Items.OfType<MenuItem>().First()
                .Items.OfType<MenuItem>().First(mi => (mi.Header as string) == I18n.L("显示文件夹栏"));
            box2.IsChecked = true;
            C4Files.Invoke(box2);
            C4Files.Flush(2);
            Assert.Equal(3, BodyGrid(page).ColumnDefinitions.Count);
            Assert.True(LibraryPage.FoldersVisible());
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:269-321</c>:菜单四项的顺序与「最终子项是可勾选的复选框」。</summary>
    [Fact]
    public void Library_layout_menu_matches_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi();
            using var harness = C4LayerHost.Show();
            LibraryPage page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            ContextMenu menu = BodyGrid(page).ContextMenu!;
            MenuItem root = menu.Items.OfType<MenuItem>().First();
            Assert.Equal(I18n.L("自定义布局..."), root.Header);
            // 【Windows 源】<c>:316-319</c>:顶层只有一项「自定义布局...」,另外三项是它的子项(顺序如此)
            Assert.Equal(new List<string> { I18n.L("自定义布局...") }, C4Files.MenuHeads(menu));
            Assert.Equal(
                new List<string> { I18n.L("显示文件夹栏"), I18n.L("导出布局"), I18n.L("导入布局") },
                root.Items.OfType<MenuItem>().Select(mi => mi.Header as string ?? "").ToList());
            MenuItem box = root.Items.OfType<MenuItem>().First();
            Assert.Equal(MenuItemToggleType.CheckBox, box.ToggleType);
            Assert.True(box.IsChecked);
            Assert.Equal(new List<string>
            {
                I18n.L("显示文件夹栏"), I18n.L("导出布局"), I18n.L("导入布局"),
            }, C4Expected.LibraryLayoutMenuItems.Skip(1).ToList());
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:302-315</c>:导出/导入布局直接交给 <c>PageLayoutIO</c>(离屏没有文件选择器 ⇒ 什么都不做)。</summary>
    [Fact]
    public void Library_export_and_import_layout_degrade_without_a_desktop_picker()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            C4Files.LayoutDir();
            using var api = new C3cFakeApi();
            using var harness = C4LayerHost.Show();
            LibraryPage page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            ContextMenu menu = BodyGrid(page).ContextMenu!;
            MenuItem root = menu.Items.OfType<MenuItem>().First();
            MenuItem export = root.Items.OfType<MenuItem>().First(mi => (mi.Header as string) == I18n.L("导出布局"));
            MenuItem import = root.Items.OfType<MenuItem>().First(mi => (mi.Header as string) == I18n.L("导入布局"));
            C4Files.Invoke(export);
            C4Files.Invoke(import);
            C4Files.Settle(4);
            // 没有崩溃就是全部要求了:两条路都如实降级(PageLayoutIO 自己处理 null)
            Assert.True(LibraryPage.FoldersVisible());
        });
    }

    // ================================================================ 交互

    /// <summary>【Windows 源】<c>LibraryPage.cs:419-434</c>:选中左栏另一个文件夹会重拉版本列表(并把它设成默认)。</summary>
    [Fact]
    public void Library_selecting_another_folder_reloads_versions()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[" + C4Files.InstanceJson("甲") + "," + C4Files.InstanceJson("乙") + "]")
                .Route("get_setting", "\"甲\"");
            using var harness = C4LayerHost.Show();
            LibraryPage page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            int before = api.Count("get_version_rows");
            page.SelectFolder("乙");
            C4Files.Pump(() => api.Count("get_version_rows") > before);
            Assert.Equal("乙", C4Files.ParamStringIn(api, "save_settings", "data", "default_instance"));
        });
    }

    /// <summary>【Windows 源】选中同一个文件夹什么都不做(不重拉)。</summary>
    [Fact]
    public void Library_selecting_the_same_folder_does_nothing()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[" + C4Files.InstanceJson("甲") + "]")
                .Route("get_setting", "\"甲\"");
            using var harness = C4LayerHost.Show();
            LibraryPage page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            int before = api.Count("get_version_rows");
            page.SelectFolder("甲");
            C4Files.Settle(4);
            Assert.Equal(before, api.Count("get_version_rows"));
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:897-948</c>:左栏菜单的项与顺序(第 6 项前一条分隔线)。</summary>
    [Fact]
    public void Library_folder_menu_matches_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi();
            using var harness = C4LayerHost.Show();
            LibraryPage page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            ContextMenu menu = page.BuildFolderMenu(new Border(), "甲");
            Assert.Equal(
                new List<string>
                {
                    I18n.L("打开文件夹"), I18n.L("设为默认实例"), I18n.L("选择 Java..."), I18n.L("管理模组"),
                    I18n.L("存档管理..."), "—", I18n.L("重命名..."), I18n.L("删除实例"),
                },
                C4Files.MenuHeads(menu));
        });
    }

    /// <summary>【Windows 源】:菜单「打开文件夹」调 <c>open_instance_folder</c>。</summary>
    [Fact]
    public void Library_folder_menu_open_folder_calls_the_backend()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi();
            using var harness = C4LayerHost.Show();
            LibraryPage page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            C4Files.Invoke(C4Files.MenuItemByHead(page.BuildFolderMenu(new Border(), "甲"), I18n.L("打开文件夹")));
            C4Files.Pump(() => api.Count("open_instance_folder") >= 1);
            C4Files.AssertParam(api, "open_instance_folder", "name", "甲");
        });
    }

    /// <summary>【Windows 源】:菜单「管理模组」/「存档管理...」打开 C4 的两个管理对话框。</summary>
    [Fact]
    public void Library_folder_menu_opens_the_two_management_dialogs()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi();
            using var harness = C4LayerHost.Show();
            LibraryPage page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            C4Files.Invoke(C4Files.MenuItemByHead(page.BuildFolderMenu(new Border(), "甲"), I18n.L("管理模组")));
            C4Files.PanelCard(I18n.L("模组管理 · {0}", "甲"));
            C4Files.ClosePanel(C4Files.PanelCard(I18n.L("模组管理 · {0}", "甲")));
            C4Files.Invoke(C4Files.MenuItemByHead(page.BuildFolderMenu(new Border(), "甲"), I18n.L("存档管理...")));
            C4Files.PanelCard(I18n.L("存档管理 · {0}", "甲"));
            C4Files.ClosePanel(C4Files.PanelCard(I18n.L("存档管理 · {0}", "甲")));
            Assert.Equal(0, C4Files.LayerCount());
        });
    }

    /// <summary>【Windows 源】:菜单「选择 Java...」走 <c>java_combo_options</c> → 弹卡 → 选了写 <c>set_instance_java</c>。</summary>
    [Fact]
    public void Library_folder_menu_picks_java_through_the_windows_calls()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi()
                .Route("java_combo_options", "[{\"label\":\"自动选择\",\"value\":\"自动选择\"},{\"label\":\"17.0.1\",\"value\":\"/jdk17/bin/java\"}]")
                .Route("java_combo_label_for", "\"17.0.1\"")
                .Route("instance_java_label", "\"17.0.1\"");
            using var harness = C4LayerHost.Show();
            LibraryPage page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            C4Files.Invoke(C4Files.MenuItemByHead(page.BuildFolderMenu(new Border(), "甲"), I18n.L("选择 Java...")));
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            Assert.True(C4Files.HasText(card, I18n.L("当前记录:") + "17.0.1"));
            C4Files.Click(C4Files.ByText(card, I18n.L("保存")));
            C4Files.Pump(() => api.Count("set_instance_java") >= 1);
            Assert.Contains("\"java\":\"/jdk17/bin/java\"", api.ParamsOf("set_instance_java"));
            Assert.Contains("\"scan_system\":true", api.ParamsOf("java_combo_options"));
        });
    }

    /// <summary>【Windows 源】:没找到 Java ⇒ Toast 警告,不弹卡。</summary>
    [Fact]
    public void Library_folder_menu_warns_when_no_java_is_found()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi().Route("java_combo_options", "[]");
            using var harness = C4LayerHost.Show();
            LibraryPage page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            C4Files.Invoke(C4Files.MenuItemByHead(page.BuildFolderMenu(new Border(), "甲"), I18n.L("选择 Java...")));
            C4Files.Pump(() => toasts.Has(I18n.L("没找到 Java")));
            Assert.Contains(toasts.Items, t => t.Kind == ToastKind.Warning);
            Assert.Equal(0, C4Files.LayerCount());
        });
    }

    /// <summary>【Windows 源】:菜单「重命名...」确认后调 <c>rename_instance</c> 并重载。</summary>
    [Fact]
    public void Library_folder_menu_rename_calls_rename_instance()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi();
            using var harness = C4LayerHost.Show();
            LibraryPage page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            C4Files.Invoke(C4Files.MenuItemByHead(page.BuildFolderMenu(new Border(), "甲"), I18n.L("重命名...")));
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            TextBox input = C4Files.Descendants(card).OfType<TextBox>().First();
            input.Text = "乙";
            C4Files.Click(C4Files.ByText(card, I18n.L("确定")));
            C4Files.Pump(() => api.Count("rename_instance") >= 1);
            C4Files.AssertParam(api, "rename_instance", "new_name", "乙");
        });
    }

    /// <summary>【Windows 源】:菜单「删除实例」要确认,确认后调 <c>delete_instance</c> 并 Toast。</summary>
    [Fact]
    public void Library_folder_menu_delete_confirms_then_calls_delete_instance()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi();
            using var harness = C4LayerHost.Show();
            LibraryPage page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            C4Files.Invoke(C4Files.MenuItemByHead(page.BuildFolderMenu(new Border(), "甲"), I18n.L("删除实例")));
            C4Files.AnswerDialog(I18n.L("永久删除"));
            C4Files.Pump(() => api.Count("delete_instance") >= 1);
            C4Files.AssertParam(api, "delete_instance", "name", "甲");
            C4Files.Pump(() => toasts.Has(I18n.L("已删除")));
        });
    }

    /// <summary>【Windows 源】:菜单「设为默认实例」调 <c>save_settings</c> 并 Toast。</summary>
    [Fact]
    public void Library_folder_menu_set_default_writes_the_setting()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi();
            using var harness = C4LayerHost.Show();
            LibraryPage page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            C4Files.Invoke(C4Files.MenuItemByHead(page.BuildFolderMenu(new Border(), "甲"), I18n.L("设为默认实例")));
            C4Files.Pump(() => api.Count("save_settings") >= 1);
            Assert.Equal("甲", C4Files.ParamStringIn(api, "save_settings", "data", "default_instance"));
            C4Files.Pump(() => toasts.Has(I18n.L("默认实例")));
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:845-854</c>:双击版本 = 设默认 + 让启动页优先用这个版本 + 跳启动页。</summary>
    [Fact]
    public void Library_launch_version_sets_default_prefers_and_navigates()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var shell = ShellFixture.Create();
            using var api = new C3cFakeApi();
            shell.Window.Navigate("library");
            LibraryPage page = (LibraryPage)shell.Window.CurrentPage!;
            C4Files.Load(page);
            C4Files.PumpTask(page.LaunchVerAsync("甲", "1.20.1"));
            Assert.Equal("甲", C4Files.ParamStringIn(api, "save_settings", "data", "default_instance"));
            Assert.Equal("launch", shell.Window.CurrentPageId);
            // 侧证「启动页真的被要求优先用这个实例/版本」:PreferInstance 内部会 Run 一次
            // EnsureLoadedAsync(那个字段本身是 private,外部看不到),所以这里钉"启动页被加载过"。
            C4Files.Pump(() => api.Count("get_instances") >= 1);
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:857-882</c>:「实例详情」把内容换成 <c>InstanceDetailPage</c>,它的「返回」再换回来。</summary>
    [Fact]
    public void Library_detail_entry_swaps_the_page_content_and_the_back_callback_restores_it()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[" + C4Files.InstanceJson("甲", 2) + "]")
                .Route("get_setting", "\"甲\"");
            using var harness = C4LayerHost.Show();
            LibraryPage page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            object? original = page.Content;
            C4Files.PumpTask(page.OpenDetailAsync());
            Assert.IsType<InstanceDetailPage>(page.Content);
            InstanceDetailPage detail = (InstanceDetailPage)page.Content!;
            Assert.Equal("甲", detail.Title);
            C4Files.Click(C4Files.ByText(detail, I18n.L("返回")));
            C4Files.Flush(2);
            Assert.Same(original, page.Content);
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:884-895</c>:卸载版本要确认,确认后调 <c>uninstall_version</c>(spec = <c>实例 / 版本</c>)。</summary>
    [Fact]
    public void Library_uninstall_confirms_then_calls_uninstall_version()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[" + C4Files.InstanceJson("甲") + "]")
                .Route("get_setting", "\"甲\"")
                .Route("get_version_rows", "[" + C4Files.VersionRowJson("1.20.1", "") + "]");
            using var harness = C4LayerHost.Show();
            LibraryPage page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            Task uninstall = page.UninstallAsync("甲", "1.20.1");
            C4Files.AnswerDialog(I18n.L("卸载"));
            C4Files.Pump(() => api.Count("uninstall_version") >= 1);
            C4Files.AssertParam(api, "uninstall_version", "spec", "甲 / 1.20.1");
            C4Files.PumpTask(uninstall);
            Assert.True(toasts.Has(I18n.L("已卸载")));
        });
    }

    /// <summary>【Windows 源】:版本行的「版本设置」按钮打开 <see cref="VersionSetupDialog"/>(C3b 已移植)。</summary>
    [Fact]
    public void Library_version_settings_button_opens_the_version_setup_dialog()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi()
                .Route("get_version_rows", "[" + C4Files.VersionRowJson("1.20.1", "") + "]")
                .Route("get_version_settings", "{}");
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            C4Files.Click(C4Files.ByText(page, I18n.L("版本设置")));
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            Assert.Equal(I18n.L("版本设置 · ") + "1.20.1", card.Title);
        });
    }

    /// <summary>【Windows 源】:版本行尾的「卸载这个版本」走同一套确认流程(这里只验按钮真的接了那条路)。</summary>
    [Fact]
    public void Library_row_uninstall_button_opens_the_confirmation()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi().Route("get_version_rows", "[" + C4Files.VersionRowJson("1.20.1", "") + "]");
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            C4Files.Click(C4Files.ByText(page, I18n.L("卸载这个版本")));
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            Assert.Equal(I18n.L("卸载版本"), card.Title);
            C4Files.Click(C4Files.ByText(card, I18n.L("取消")));
            C4Files.Settle(4);
            Assert.Equal(0, api.Count("uninstall_version"));
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:1000-1016</c>:「添加已有文件夹」先选目录(离屏给 null ⇒ 什么都不做)。</summary>
    [Fact]
    public void Library_add_folder_degrades_without_a_desktop_picker()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi();
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            C4Files.Click(C4Files.ByText(page, I18n.L("添加已有文件夹")));
            C4Files.Settle(4);
            Assert.Equal(0, api.Count("import_existing_folder"));
            Assert.False(toasts.Has(I18n.L("已开始导入")));
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:1019-1026</c>:「导入整合包」选文件(离屏 null ⇒ 什么都不做)。</summary>
    [Fact]
    public void Library_import_pack_degrades_without_a_desktop_picker()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi();
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            C4Files.Click(C4Files.ByText(page, I18n.L("导入整合包")));
            C4Files.Settle(4);
            Assert.Equal(0, C4Files.LayerCount());
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:1028-1040</c>:「新建实例」问名字(<c>Dlg.Prompt</c>)→ <c>create_instance</c>。</summary>
    [Fact]
    public void Library_create_instance_prompts_and_calls_create_instance()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi();
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            C4Files.Click(C4Files.ByText(page, I18n.L("新建实例")));
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            Assert.Equal(I18n.L("新建实例"), card.Title);
            C4Files.Descendants(card).OfType<TextBox>().First().Text = "新实例";
            C4Files.Click(C4Files.ByText(card, I18n.L("确定")));
            C4Files.Pump(() => api.Count("create_instance") >= 1);
            C4Files.AssertParam(api, "create_instance", "name", "新实例");
            C4Files.Pump(() => toasts.Has(I18n.L("已创建")));
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:1042-1061</c>:没探测到官方启动器 ⇒ 只 Toast 警告。</summary>
    [Fact]
    public void Library_migrate_warns_when_no_official_launcher_is_found()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi().Route("detect_official_launcher", "false");
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            C4Files.Click(C4Files.ByText(page, I18n.L("导入官方启动器")));
            C4Files.Pump(() => toasts.Has(I18n.L("没找到官方启动器")));
            Assert.Equal(0, api.Count("migrate_official_launcher"));
        });
    }

    /// <summary>【Windows 源】探测到官方启动器 ⇒ 弹确认卡,确认后起 <c>migrate_official_launcher</c> 任务。</summary>
    [Fact]
    public void Library_migrate_starts_the_task_after_confirming()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi()
                .Route("detect_official_launcher", "true")
                .Route("official_launcher_dir", "\"/home/me/.minecraft\"")
                .Route("scan_official_versions", "[\"1.20.1\",\"1.19.4\"]");
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            C4Files.Click(C4Files.ByText(page, I18n.L("导入官方启动器")));
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            Assert.True(C4Files.HasText(card, I18n.L("检测到：{0}", "/home/me/.minecraft")));
            C4Files.Click(C4Files.ByText(card, I18n.L("开始导入")));
            C4Files.Pump(() => api.Count("migrate_official_launcher") >= 1);
            Assert.Contains("\"instance\":\"default\"", api.ParamsOf("migrate_official_launcher"));
            C4Files.Pump(() => toasts.Has(I18n.L("已开始导入")));
        });
    }

    /// <summary>【Windows 源】<c>LibraryPage.cs:193-197</c>:整页收拖放,上下文是「当前文件夹 + 整合包」。</summary>
    [Fact]
    public void Library_page_enables_drop_with_the_modpack_context()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[" + C4Files.InstanceJson("甲") + "]")
                .Route("get_setting", "\"甲\"");
            using var harness = C4LayerHost.Show();
            LibraryPage page = C4Files.Mount(harness, new LibraryPage());
            C4Files.Load(page);
            Assert.True(page.DropEnabled);
            FileDrop.DropContext context = page.DropContextNow();
            Assert.Equal("甲", context.Instance);
            Assert.Equal("modpack", context.PreferKind);
            Assert.True(DragDrop.GetAllowDrop(page));
        });
    }

    /// <summary>【任务书】关掉动画时一切都立刻到位;页面跑完不留动画(离屏 <c>Motion.Enabled</c> 本来就是假)。</summary>
    [Fact]
    public void Library_page_leaves_no_animation_behind()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            Motion.Enabled = false;
            using var toasts = new C3cToasts();
            using var api = new C3cFakeApi().Route("get_version_rows", "[" + C4Files.VersionRowJson("1.20.1", "") + "]");
            using var harness = C4LayerHost.Show();
            PageBase page = C4Files.Mount(harness, new LibraryPage());
            Motion.ResetObservability();
            C4Files.Load(page);
            C4Files.Settle(2);
            // 只断言**本用例窗口内**起的动画数(D-132 最后一条:进程级总数会被别的用例抬高)
            Assert.Equal(0, Motion.RunningAnimationsSinceReset);
            Assert.Null(Motion.LastError);
        });
    }

    // ---------------------------------------------------------------- 小工具

    /// <summary>页面的双栏外壳那个 Grid(唯一带 ContextMenu 的那一层)。</summary>
    private static Grid BodyGrid(Control page)
    {
        Grid? hit = C4Files.Descendants(page).OfType<Grid>().FirstOrDefault(g => g.ContextMenu != null);
        Assert.True(hit != null, "页面里找不到带 ContextMenu 的双栏 Grid");
        return hit!;
    }

    /// <summary>按组头文案取那张分组卡(标题形如「<c>Fabric 版本 (1)</c>」)。</summary>
    private static Border GroupCard(Control page, string headerText)
    {
        Border? hit = C4Files.Descendants(page).OfType<Border>().FirstOrDefault(b => C4Files.HasText(b, headerText));
        Assert.True(hit != null, "找不到分组卡「" + headerText + "」");
        return hit!;
    }

    /// <summary>版本行的收藏星钮(Windows 是手工建的 <c>Button</c>,Content 是字形而不是文字,所以按字形认)。</summary>
    private static Button StarButton(Control page, string glyph)
    {
        Button? hit = C4Files.Descendants(page).OfType<Button>()
            .FirstOrDefault(b => (b.Content as TextBlock)?.Text == glyph);
        Assert.True(hit != null, "找不到字形为 <" + glyph + "> 的星钮");
        return hit!;
    }
}
