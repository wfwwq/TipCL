using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TipCL.Models;
using TipCL.Pages;
using TipCL.Services;
using TipCL.Themes;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C6a:下载页(<see cref="DownloadPage"/>,导航 id <c>download</c>)+ 目录页(<see cref="CatalogPage"/>)+
/// 安装页(<see cref="InstallPage"/>)三页一起的用例。
///
/// 纪律:每条碰 Avalonia 的用例体都走 <c>AvaloniaSession.Run(...)</c>(D-79);全局静态由 <see cref="C6aScope"/>
/// 快照还原(D-69);对话框一律"先推出来再点"(D-113);等待一律"条件成立 + 有截止时间"(D-120);
/// 后端是按方法名路由的回环假后端(不起真后端、不出网、不启外部进程);
/// 读剪贴板走注入缝、**绝不碰真剪贴板**(C6a 的 D 类差异)。
/// </summary>
public class C6aPagesTests
{
    // ================================================================ 注册与外壳

    /// <summary>【任务书】<c>download</c> 由下载页自己的模块初始化器注册,能按 id 建出真页面。</summary>
    [Fact]
    public void Download_is_registered_by_its_module_initializer()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            Assert.True(PageRegistry.Contains(DownloadPageRegistration.PageId));
            Assert.IsType<DownloadPage>(PageRegistry.Create(DownloadPageRegistration.PageId));
            // 大小写敏感(D-94)
            Assert.Null(PageRegistry.Create("Download"));
        });
    }

    /// <summary>【Windows 源】页面 id 常量与导航表一致(<c>NavModel.PageForNavKey["download"]</c>)。</summary>
    [Fact]
    public void Download_page_id_matches_the_nav_table()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            Assert.Equal("download", DownloadPageRegistration.PageId);
            // 外壳的导航表里就有这一行(下载页是**顶层整页**,不是别的页面里的一个 key)
            Assert.Contains(MainWindow.NavDefs, d => d.Id == DownloadPageRegistration.PageId);
            Assert.Contains("download", NavModel.TopKeys);
            Assert.Contains("download", NavModel.SectionIds);
            // 【本机实测】NavModel.PageForNavKey 里**没有** download —— 那张表只放"某个导航 key 对应哪个页面 id"
            // 的条目,下载页自己就是一段(TopKeys + SectionIds 里都有它)。
            // C4 发现 library 也是同一个形状;这里把现状钉住,免得后来者以为"注册了就一定在 PageForNavKey 里"。
            Assert.False(NavModel.PageForNavKey.ContainsKey("download"));
        });
    }

    /// <summary>【任务书】外壳能按 id 解析并建出下载页;而且**子视图不进注册表**(CatalogPage / InstallPage)。</summary>
    [Fact]
    public void The_shell_resolves_the_download_page_and_the_sub_views_stay_unregistered()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var api = C6aFiles.Api();
            using var shell = ShellFixture.Create();
            shell.Window.Navigate("download");
            Assert.Equal("download", shell.Window.CurrentPageId);
            Assert.IsType<DownloadPage>(shell.Window.CurrentPage);
            // Windows 的导航表里没有这两个 id(它们是被换进页内容 / 分段里的子视图)
            Assert.False(PageRegistry.Contains("catalog"));
            Assert.False(PageRegistry.Contains("install"));
        });
    }

    /// <summary>【任务书】没有后端时构造不许抛、不许卡(离屏现实 D-96:渲染预加载状态)。</summary>
    [Fact]
    public void Construction_does_not_throw_without_a_backend()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            AppServices.Client = null;
            DownloadPage page = new DownloadPage();
            Assert.NotNull(page.Content);
            Assert.Equal(I18n.L("下载"), page.Title);
            // 预加载状态:分段 0 的版本列表还没有数据
            ListBox list = C5aFiles.Find<ListBox>(page, "版本列表");
            Assert.Empty(list.Items);
        });
    }

    /// <summary>没有 client 时首屏加载如实降级:记一笔 + Toast「加载失败」,异常不抛给外壳。</summary>
    [Fact]
    public void Load_without_a_backend_degrades_instead_of_throwing()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var toasts = new C3cToasts();
            AppServices.Client = null;
            DownloadPage page = new DownloadPage();
            C5aFiles.Load(page);
            Assert.True(toasts.Has(I18n.L("加载失败")), "没有客户端时必须如实报「加载失败」:" + string.Join("|", toasts.Items.Select(t => t.Title + t.Body)));
        });
    }

    // ================================================================ 静态结构(原版分段)

    /// <summary>【Windows 源】<c>:21</c> 七个分段(标题与顺序一字不改)+ 首屏停在原版段。</summary>
    [Fact]
    public void Download_page_has_the_seven_segments_in_windows_order()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var api = C6aFiles.Api();
            using var harness = C4LayerHost.Show();
            DownloadPage page = C5aFiles.Mount(harness, new DownloadPage());
            C5aFiles.Load(page);

            List<string> labels = C5aFiles.Descendants(page).OfType<ToggleButton>()
                .Select(b => b.Content as string ?? "")
                .Where(t => t.Length > 0)
                .ToList();
            Assert.Equal(
                new List<string> { I18n.L("原版"), "Mod", I18n.L("整合包"), I18n.L("数据包"), I18n.L("资源包"), I18n.L("光影"), I18n.L("世界") },
                labels);
            ToggleButton first = C5aFiles.Descendants(page).OfType<ToggleButton>().First();
            Assert.True(first.IsChecked);
            Assert.Contains(StyleKeys.Map("Seg.Item"), first.Classes);
        });
    }

    /// <summary>【Windows 源】<c>:117-121</c> 原版段 = <c>BlockHost("download")</c> 的两块(筛选 / 版本列表)。</summary>
    [Fact]
    public void The_vanilla_segment_is_the_two_block_host()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var api = C6aFiles.Api();
            using var harness = C4LayerHost.Show();
            DownloadPage page = C5aFiles.Mount(harness, new DownloadPage());
            C5aFiles.Load(page);

            BlockHost host = C5aFiles.Find<BlockHost>(page, "原版段的区块宿主");
            Assert.Equal(new[] { "filters", "list" }, host.OrderNow.Select(s => s.Id).ToArray());
            Assert.Equal(new[] { "筛选", "版本列表" }.Select(t => I18n.L(t)).ToArray(), host.OrderNow.Select(s => s.Title).ToArray());
            // 筛选行:搜索框占位 + 三个筛选钮
            TextBox search = C5aFiles.Descendants(page).OfType<TextBox>().First();
            Assert.Equal(I18n.L("搜索版本"), search.Tag as string ?? search.Classes.FirstOrDefault() ?? "");
            foreach (string label in new[] { "正式版", "快照", "远古" })
            {
                Assert.True(C5aFiles.HasButton(page, I18n.L(label)), "少了筛选钮「" + label + "」");
            }
        });
    }

    /// <summary>【Windows 源】<c>:207-216</c> 三个筛选钮按 <c>Type</c> 本地过滤(不请求后端)。</summary>
    [Fact]
    public void The_three_type_buttons_filter_the_version_list_locally()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            // fetch 回空 ⇒ 保留本地四行(否则 FetchAsync 会用远程清单盖掉)
            using var api = C6aFiles.Api().Route("fetch_version_list", "[]");
            using var harness = C4LayerHost.Show();
            DownloadPage page = C5aFiles.Mount(harness, new DownloadPage());
            C5aFiles.Load(page);
            ListBox list = C5aFiles.Find<ListBox>(page, "版本列表");

            C5aFiles.Click(C5aFiles.ByText(page, I18n.L("正式版")));
            C5aFiles.Settle(2);
            Assert.Equal(2, list.Items.Count); // 1.20.1 / 1.21.2

            C5aFiles.Click(C5aFiles.ByText(page, I18n.L("快照")));
            C5aFiles.Settle(2);
            Assert.Equal("1.20.1-pre1", Assert.IsType<VersionRow>(Assert.Single(list.Items)).Version);

            C5aFiles.Click(C5aFiles.ByText(page, I18n.L("远古")));
            C5aFiles.Settle(2);
            Assert.Equal("b1.7.3", Assert.IsType<VersionRow>(Assert.Single(list.Items)).Version);
        });
    }

    /// <summary>【Windows 源】<c>:91-94</c> 搜索框按版本号本地过滤(大小写不敏感)。</summary>
    [Fact]
    public void The_search_box_filters_the_version_list_locally()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var api = C6aFiles.Api().Route("fetch_version_list", "[]");
            using var harness = C4LayerHost.Show();
            DownloadPage page = C5aFiles.Mount(harness, new DownloadPage());
            C5aFiles.Load(page);
            ListBox list = C5aFiles.Find<ListBox>(page, "版本列表");
            Assert.Equal(2, list.Items.Count);

            C5aFiles.Descendants(page).OfType<TextBox>().First().Text = "1.21";
            C5aFiles.Settle(2);
            Assert.Equal("1.21.2", Assert.IsType<VersionRow>(Assert.Single(list.Items)).Version);
        });
    }

    /// <summary>【Windows 源】<c>:138-141</c> 右上「任务 N」徽标读 <c>TaskStore.RunningCount</c>。</summary>
    [Fact]
    public void The_task_badge_shows_the_running_count()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var tasks = new C3cTaskStore();
            using var api = C6aFiles.Api();
            using var harness = C4LayerHost.Show();
            DownloadPage page = C5aFiles.Mount(harness, new DownloadPage());
            C5aFiles.Load(page);
            Assert.True(C5aFiles.HasText(page, "0"));

            TaskStore.Handle(new BridgeEvent { Event = "task_added", TaskId = "t-1", Title = "装游戏" });
            C5aFiles.Pump(() => C5aFiles.HasText(page, "1"));
            // 「任务」钮的 Content 是图文组合面板(不是字符串),所以按面板里的文案认它
            Assert.True(
                C5aFiles.Descendants(page).OfType<Button>().Any(b => b.Content is SPanel p && p.Children.OfType<TextBlock>().Any(t => t.Text == I18n.L("任务"))),
                "右上角少了「任务」钮");
            TaskStore.Handle(new BridgeEvent { Event = "finished", TaskId = "t-1", Success = true });
            C5aFiles.Pump(() => C5aFiles.HasText(page, "0"));
        });
    }

    // ================================================================ 六个目录分段

    /// <summary>
    /// 【Windows 源】<c>:143-163</c> 第 1..6 段内嵌 <see cref="CatalogPage"/>,种类与标题一一对应,
    /// 而且**切到才建**(与 Windows 的 <c>_subs[k] ??= new CatalogPage(...)</c> 同意图)。
    /// </summary>
    [Theory]
    [InlineData(1, "Mod", "mod")]
    [InlineData(2, "整合包", "modpack")]
    [InlineData(3, "数据包", "datapack")]
    [InlineData(4, "资源包", "resourcepack")]
    [InlineData(5, "光影包", "shader")]
    [InlineData(6, "世界", "world")]
    public void Catalog_segments_are_built_lazily_and_carry_the_right_kind(int segment, string title, string key)
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var api = C6aFiles.Api();
            using var harness = C4LayerHost.Show();
            DownloadPage page = C5aFiles.Mount(harness, new DownloadPage());
            C5aFiles.Load(page);
            Assert.Empty(C5aFiles.Descendants(page).OfType<CatalogPage>());

            page.ShowSeg(segment);
            C5aFiles.Settle(3);
            CatalogPage catalog = Assert.Single(C5aFiles.Descendants(page).OfType<CatalogPage>());
            Assert.Equal(key == "mod" ? "Mod" : I18n.L(title), catalog.Title);
            Assert.True(C5aFiles.HasButton(page, I18n.L("搜索下载")));
            Assert.True(C5aFiles.HasButton(page, I18n.L("重置条件")));
            Assert.True(C5aFiles.HasButton(page, I18n.L("链接安装")));
            Assert.True(C5aFiles.HasButton(page, I18n.L("导入本地文件")));

            // 再切回原版段:目录页还在缓存里(同一个实例,不重建)
            page.ShowSeg(0);
            C5aFiles.Settle(2);
            page.ShowSeg(segment);
            C5aFiles.Settle(2);
            Assert.Same(catalog, C5aFiles.Descendants(page).OfType<CatalogPage>().Single());
        });
    }

    /// <summary>
    /// 【C6a 机制探针】离屏夹具里 <c>Ui.VirtualList</c>(ListBox + FuncDataTemplate)的**数据在、行控件不在**:
    /// 数据项数 &gt; 0,但可视树里没有 <c>ListBoxItem</c> 容器 ⇒ 视口为 0 的平台限制,**不是产品缺陷**。
    /// 这条把现象钉住,并解释 D1 全页冒烟矩阵里"列表页只有计数 / 空态、看不到行"的观感(真机不受影响)。
    /// </summary>
    [Fact]
    public void Virtual_list_data_is_there_but_offscreen_realizes_no_row_containers()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var api = C6aFiles.Api().Route("search_mods", "[" + C6aFiles.CatalogItemJson("JEI", slug: "jei") + "]");
            using var harness = C4LayerHost.Show();
            DownloadPage page = C5aFiles.Mount(harness, new DownloadPage());
            C5aFiles.Load(page);
            page.ShowSeg(1);
            C5aFiles.Pump(() => api.Count("search_mods") >= 1, 5000);
            ListBox list = C5aFiles.Find<ListBox>(page, "结果列表");
            C5aFiles.Settle(8);

            Assert.Single(list.Items);                                     // 数据在
            // 【实测结论 · 平台行为钉子】离屏(视口为 0)时容器**建了、但行模板内容不落地**:
            // ListBoxItem 在可视树里,可是它下面没有任何 TextBlock;行文案在可视树与逻辑树里都找不到。
            // 将来平台改了行为(离屏也会 materialize 行),这条会红 ⇒ 那时再回来复核 D1 的离屏观感结论。
            Assert.Single(list.GetVisualDescendants().OfType<ListBoxItem>());
            Assert.Empty(list.GetVisualDescendants().OfType<TextBlock>());
            Assert.False(C5aFiles.HasText(page, "JEI"));                    // 逻辑树里也看不到
            Assert.True(C5aFiles.HasButton(page, I18n.L("重置条件")));        // 页面骨架是好的(不是整页没渲染)
        });
    }

    /// <summary>【Windows 源】<c>:559-577</c> 搜索走种类的 <c>SearchMethod</c>,参数是 query / source / extra(game_version + category)。</summary>
    [Theory]
    [InlineData(1, "Mod", "search_mods", "Modrinth", "没有找到相关模组")]
    [InlineData(2, "整合包", "search_modpacks", "Modrinth", "没有找到相关整合包")]
    [InlineData(3, "数据包", "search_datapacks", "all", "没有找到相关数据包")]
    [InlineData(4, "资源包", "search_resourcepacks", "all", "没有找到相关资源包")]
    [InlineData(5, "光影包", "search_shaders", "all", "没有找到相关光影")]
    [InlineData(6, "世界", "search_worlds", "CurseForge", "没有找到相关世界地图")]
    public void Catalog_search_calls_the_kinds_search_method(int segment, string title, string method, string source, string emptyText)
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var api = C6aFiles.Api();
            using var harness = C4LayerHost.Show();
            DownloadPage page = C5aFiles.Mount(harness, new DownloadPage());
            C5aFiles.Load(page);
            page.ShowSeg(segment);
            C5aFiles.Settle(2);

            C5aFiles.Pump(() => api.Count(method) >= 1, 5000);
            Assert.Equal("", C5aFiles.ParamAt(api, method, "query"));
            // 【Windows 源】<c>:127</c>:来源下拉的初值是种类的 DefaultSource(有默认来源的种类就不是「全部」)
            Assert.Equal(source, C5aFiles.ParamAt(api, method, "source"));
            Assert.True(C5aFiles.ParamHas(api, method, "\"game_version\""));
            Assert.True(C5aFiles.ParamHas(api, method, "\"category\""));
            // 空结果 → 该种类自己的空态文案(逐个种类的话术不同)
            C5aFiles.Pump(() => C5aFiles.HasText(page, I18n.L(emptyText)), 5000);
            Assert.True(C5aFiles.HasButton(page, I18n.L("重置条件")));
        });
    }

    /// <summary>【Windows 源】<c>:363-391</c> 筛选下拉真的进了搜索参数(游戏版本 + 类型)。</summary>
    [Fact]
    public void Catalog_search_params_carry_the_game_version_and_category()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var api = C6aFiles.Api();
            using var harness = C4LayerHost.Show();
            DownloadPage page = C5aFiles.Mount(harness, new DownloadPage());
            C5aFiles.Load(page);
            page.ShowSeg(1);
            C5aFiles.Settle(2);
            C5aFiles.Pump(() => api.Count("search_mods") >= 1, 5000);

            // 游戏版本框(可编辑)打上 1.20.1,再选一个类型(第 2 项 = 优化)
            // 先改类型(会触发一次搜索),泵到这一次的 category 到位;再改游戏版本,泵到两样都到位。
            ComboBox type = C5aFiles.Descendants(page).OfType<ComboBox>()
                .First(c => c.Items.OfType<object>().Any(o => (o.ToString() ?? "") == I18n.L("优化")));
            type.SelectedIndex = 1;
            C5aFiles.Pump(() => C5aFiles.ParamAt(api, "search_mods", api.Count("search_mods") - 1, "extra", "category") == "优化", 5000);
            ComboBox version = C5aFiles.Descendants(page).OfType<ComboBox>()
                .First(c => c.Items.OfType<object>().Any(o => (o.ToString() ?? "") == "1.20.1"));
            version.Text = "1.20.1";
            C5aFiles.Pump(() => api.Count("search_mods") >= 2 && C5aFiles.ParamAt(api, "search_mods", api.Count("search_mods") - 1, "extra", "game_version") == "1.20.1", 5000);
            int last = api.Count("search_mods") - 1;
            Assert.Equal("1.20.1", C5aFiles.ParamAt(api, "search_mods", last, "extra", "game_version"));
            Assert.Equal("优化", C5aFiles.ParamAt(api, "search_mods", last, "extra", "category"));
        });
    }

    /// <summary>【Windows 源】<c>:988-1047</c> 已安装模式:mod 走 <c>get_installed_mod_entries</c>,其余走种类的 <c>InstalledMethod</c>。</summary>
    [Theory]
    [InlineData(1, "get_installed_mod_entries")]
    [InlineData(2, "get_installed_modpacks")]
    [InlineData(3, "get_installed_datapacks")]
    [InlineData(4, "get_installed_resourcepacks")]
    [InlineData(5, "get_installed_shaders")]
    public void Catalog_installed_mode_calls_the_kinds_installed_method(int segment, string method)
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var api = C6aFiles.Api()
                .Route("get_instances", "[{\"name\":\"甲\",\"path\":\"/g/甲\",\"versions\":1,\"mc\":\"1.20.1\"}]")
                .Route("get_setting", "\"甲\"");
            using var harness = C4LayerHost.Show();
            DownloadPage page = C5aFiles.Mount(harness, new DownloadPage());
            C5aFiles.Load(page);
            page.ShowSeg(segment);
            C5aFiles.Settle(2);
            CatalogPage catalog = C5aFiles.Descendants(page).OfType<CatalogPage>().Single();

            catalog.SetMode(CatalogPage.Mode.Installed);
            C5aFiles.Pump(() => api.Count(method) >= 1, 5000);
            // 参数里的中文在原文里是 \uXXXX 转义(BridgeClient 的 STJ 编码器),按 JSON 取值才比得上
            Assert.Equal("甲", C5aFiles.ParamAt(api, method, "instance"));
            // 未安装任何东西 ⇒ 空态文案(逐个种类的话术)
            C5aFiles.Pump(() => C5aFiles.HasText(page, I18n.L("切回「搜索下载」装一个")));
        });
    }

    /// <summary>【Windows 源】<c>:539-557</c> 收藏模式读 <c>catalog_favorites</c>,空列表给"还没有收藏"的空态。</summary>
    [Fact]
    public void Catalog_favorites_mode_reads_catalog_favorites()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var api = C6aFiles.Api();
            using var harness = C4LayerHost.Show();
            DownloadPage page = C5aFiles.Mount(harness, new DownloadPage());
            C5aFiles.Load(page);
            page.ShowSeg(1);
            C5aFiles.Settle(2);
            CatalogPage catalog = C5aFiles.Descendants(page).OfType<CatalogPage>().Single();

            catalog.SetMode(CatalogPage.Mode.Favorites);
            C5aFiles.Pump(() => C5aFiles.HasText(page, I18n.L("还没有收藏")), 5000);
            Assert.True(api.Count("catalog_favorites") >= 1);
        });
    }

    /// <summary>【Windows 源】<c>:516-537</c> 点星星 → <c>toggle_favorite</c>(item 带 name/source/slug/id),回包重算收藏集。</summary>
    [Fact]
    public void Catalog_star_toggles_through_toggle_favorite()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var api = C6aFiles.Api()
                .Route("search_mods", "[" + C6aFiles.CatalogItemJson("JEI", slug: "jei") + "]")
                .Route("toggle_favorite", "[" + C6aFiles.CatalogItemJson("JEI", slug: "jei") + "]");
            using var harness = C4LayerHost.Show();
            DownloadPage page = C5aFiles.Mount(harness, new DownloadPage());
            C5aFiles.Load(page);
            page.ShowSeg(1);
            C5aFiles.Settle(2);
            CatalogPage catalog = C5aFiles.Descendants(page).OfType<CatalogPage>().Single();
            C5aFiles.Pump(() => api.Count("search_mods") >= 1, 5000);
            // 数据在(行控件在离屏不会实例化 —— 见机制探针那条用例)
            Assert.Single(C5aFiles.Find<ListBox>(page, "结果列表").Items);
            // 驱动"点那颗星"那一层(处理器体,D-115 / D-127;真机走同一段代码)
            CatalogItem item = new CatalogItem { Name = "JEI", Slug = "jei", Source = "Modrinth" };
            Task toggle = catalog.ToggleFavRowForTest(item);
            C5aFiles.Pump(() => api.Count("toggle_favorite") >= 1, 5000);
            C5aFiles.PumpTask(toggle);
            Assert.Equal("JEI", C5aFiles.ParamAt(api, "toggle_favorite", "item", "name"));
            Assert.Equal("Modrinth", C5aFiles.ParamAt(api, "toggle_favorite", "item", "source"));
            Assert.Equal("jei", C5aFiles.ParamAt(api, "toggle_favorite", "item", "slug"));
        });
    }

    /// <summary>【Windows 源】<c>:696-936</c> 安装一条结果:先 <c>list_catalog_files</c> 选文件,再起种类的 <c>InstallMethod</c>。</summary>
    [Fact]
    public void Catalog_install_picks_a_version_file_then_starts_the_install_task()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var api = C6aFiles.Api()
                .Route("search_mods", "[" + C6aFiles.CatalogItemJson("JEI", slug: "jei") + "]")
                .Route("list_catalog_files", "[" + C6aFiles.CatalogFileJson("JEI", "jei-1.jar") + "]");
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            DownloadPage page = C5aFiles.Mount(harness, new DownloadPage());
            C5aFiles.Load(page);
            page.ShowSeg(1);
            C5aFiles.Pump(() => api.Count("search_mods") >= 1, 5000);
            CatalogPage catalog = C5aFiles.Descendants(page).OfType<CatalogPage>().Single();
            Assert.Single(C5aFiles.Find<ListBox>(page, "结果列表").Items);

            // 驱动"点这一行的安装"那一层(D-115 / D-127),再推卡片(D-113)
            Task install = catalog.InstallRowForTest(new CatalogItem { Name = "JEI", Slug = "jei", Source = "Modrinth" });
            C5aFiles.Pump(() => api.Count("list_catalog_files") >= 1, 5000);
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last(c => c.Title == I18n.L("选择版本文件 · JEI"));
            C5aFiles.Click(C5aFiles.ByText(card, I18n.L("安装所选")));
            C5aFiles.PumpTask(install);

            C5aFiles.Pump(() => api.Count("install_mod") >= 1, 5000);
            Assert.Equal("JEI", C5aFiles.ParamAt(api, "install_mod", "name"));
            Assert.True(C5aFiles.ParamHas(api, "install_mod", "\"filename\":\"jei-1.jar\""));
        });
    }

    /// <summary>【Windows 源】<c>:892-936</c> 整合包的安装多带一个 <c>source</c>(默认 Modrinth)。</summary>
    [Fact]
    public void Catalog_modpack_install_sends_the_source_and_uses_the_modpack_method()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var api = C6aFiles.Api()
                .Route("search_modpacks", "[" + C6aFiles.CatalogItemJson("史诗整合", slug: "epic") + "]")
                .Route("list_catalog_files", "[{\"id\":\"9\",\"name\":\"史诗整合\",\"version_number\":\"2.0\",\"filename\":\"epic.mrpack\",\"game_versions\":[\"1.20.1\"],\"loaders\":[\"fabric\"],\"date\":\"2026-01-01\",\"downloads\":3,\"release_type\":\"release\",\"source\":\"modrinth\"}]");
            using var harness = C4LayerHost.Show();
            DownloadPage page = C5aFiles.Mount(harness, new DownloadPage());
            C5aFiles.Load(page);
            page.ShowSeg(2);
            C5aFiles.Pump(() => api.Count("search_modpacks") >= 1, 5000);
            CatalogPage catalog = C5aFiles.Descendants(page).OfType<CatalogPage>().Single();
            Assert.Single(C5aFiles.Find<ListBox>(page, "结果列表").Items);

            Task install = catalog.InstallRowForTest(new CatalogItem { Name = "史诗整合", Slug = "epic", Source = "Modrinth" });
            C5aFiles.Pump(() => api.Count("list_catalog_files") >= 1, 5000);
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last(c => c.Title == I18n.L("选择版本文件 · 史诗整合"));
            C5aFiles.Click(C5aFiles.ByText(card, I18n.L("安装最新")));
            C5aFiles.PumpTask(install);
            C5aFiles.Pump(() => api.Count("install_modpack") >= 1, 5000);
            Assert.Equal("Modrinth", C5aFiles.ParamAt(api, "install_modpack", "source"));
        });
    }

    /// <summary>
    /// 【Windows 源】<c>:1208-1233</c> 切到目录页时嗅剪贴板里的 Modrinth / CurseForge 链接。
    /// 【D 类差异】走注入缝 <see cref="CatalogPage.ClipboardReader"/> —— 这条用例**没有碰真剪贴板**。
    /// </summary>
    [Fact]
    public void Catalog_clipboard_seam_fills_the_search_box()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var toasts = new C3cToasts();
            using var api = C6aFiles.Api();
            using var harness = C4LayerHost.Show();
            DownloadPage page = C5aFiles.Mount(harness, new DownloadPage());
            C5aFiles.Load(page);
            page.ShowSeg(1);
            C5aFiles.Settle(2);

            CatalogPage.ClipboardReader = () => "https://modrinth.com/mod/jei";
            CatalogPage catalog = C5aFiles.Descendants(page).OfType<CatalogPage>().Single();
            catalog.OnShown();
            C5aFiles.Pump(() => toasts.Has(I18n.L("识别到剪贴板链接")), 5000);
            TextBox search = C5aFiles.Descendants(page).OfType<TextBox>().First(t => (t.Tag as string ?? "").StartsWith(I18n.L("搜索"), StringComparison.Ordinal));
            Assert.Equal("https://modrinth.com/mod/jei", search.Text);

            // 普通文本不认(也要走同一条缝,避免"读了真剪贴板"这种假绿)
            CatalogPage.ClipboardReader = () => "今天天气不错";
            catalog.OnShown();
            C5aFiles.Settle(3);
            Assert.Equal(1, toasts.Items.Count(t => t.Title == I18n.L("识别到剪贴板链接")));
        });
    }

    /// <summary>【Windows 源】<c>:285-289</c> 整页收拖放,上下文是「当前实例 + 种类的落点类型」。</summary>
    [Theory]
    [InlineData(1, "mod")]
    [InlineData(5, "shaderpack")]
    [InlineData(6, "world")]
    public void Catalog_drop_context_matches_the_kind(int segment, string preferredKind)
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var api = C6aFiles.Api();
            using var harness = C4LayerHost.Show();
            DownloadPage page = C5aFiles.Mount(harness, new DownloadPage());
            C5aFiles.Load(page);
            page.ShowSeg(segment);
            C5aFiles.Settle(2);
            CatalogPage catalog = C5aFiles.Descendants(page).OfType<CatalogPage>().Single();
            Assert.True(catalog.DropEnabled);
            FileDrop.DropContext context = catalog.DropContextNow();
            Assert.Equal(preferredKind, context.PreferKind);
            Assert.True(DragDrop.GetAllowDrop(catalog));
        });
    }

    // ================================================================ 安装页(状态机 + 往返)

    /// <summary>【Windows 源】<c>:106-139</c> 五张加载器卡 + <c>:251-279</c> 六个 <c>list_loader_versions</c>(mc_version + loader)。</summary>
    [Fact]
    public void Install_page_lists_the_five_loader_cards_and_asks_for_six_loader_versions()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var api = C6aFiles.Api();
            using var harness = C4LayerHost.Show();
            InstallPage install = C5aFiles.Mount(harness, new InstallPage("1.20.1", I18n.L("正式版"), () => { }));
            C5aFiles.Pump(() => api.Count("list_loader_versions") >= 6, 5000);

            foreach (string name in new[] { "不安装", "Fabric", "Forge", "NeoForge", "Quilt" })
            {
                Assert.True(C5aFiles.HasText(install, name), "少了加载器卡「" + name + "」");
            }
            Assert.True(C5aFiles.HasText(install, I18n.L("安装 Minecraft {0}", "1.20.1")));
            Assert.True(C5aFiles.HasText(install, I18n.L("正式版")));
            // 六个请求是 Task.WhenAll 并发发出的,**到达顺序**不保证与发出顺序一致(D-103 的同类坑),
            // 所以按"集合"断言:六个加载器名一个不少,而且每个都带 mc_version。
            List<string> all = Enumerable.Range(0, 6).Select(i => api.ParamsOf("list_loader_versions", i)).ToList();
            foreach (string loader in new[] { "fabric", "forge", "neoforge", "quilt", "optifine", "liteloader" })
            {
                Assert.Contains(all, p => p.Contains("\"loader\":\"" + loader + "\"", StringComparison.Ordinal));
            }
            Assert.All(all, p => Assert.Contains("\"mc_version\":\"1.20.1\"", p, StringComparison.Ordinal));
            // 卡里的副标题是"推荐"的那个 id(稳定版优先)
            Assert.True(C5aFiles.HasText(install, "0.15.11"));
        });
    }

    /// <summary>【Windows 源】<c>:307-400</c> 选一张卡:名称、摘要、体积估算与版本行可见性都跟着变。</summary>
    [Fact]
    public void Install_page_selecting_a_loader_updates_the_summary()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var api = C6aFiles.Api();
            using var harness = C4LayerHost.Show();
            InstallPage install = C5aFiles.Mount(harness, new InstallPage("1.20.1", I18n.L("正式版"), () => { }));
            C5aFiles.Pump(() => api.Count("list_loader_versions") >= 6, 5000);

            // 首屏 = 纯原版:名称就是版本号,摘要只有版本,估算 30 MB
            Assert.True(C5aFiles.HasText(install, I18n.L("· 约 {0} MB", 30)));

            install.Select(1); // Fabric
            C5aFiles.Settle(2);
            // 名称在**输入框**里(不是 TextBlock),所以按 TextBox.Text 断言
            Assert.Equal("Fabric 1.20.1", C5aFiles.Descendants(install).OfType<TextBox>().First().Text);
            List<string> texts = C5aFiles.Texts(install).Select(t => t.Text ?? "").ToList();
            Assert.True(texts.Contains("1.20.1 + Fabric 0.15.11 + Fabric API"), "摘要不对;现有文案:" + string.Join("|", texts));
            Assert.True(texts.Contains(I18n.L("· 约 {0} MB", 33)), "体积估算不对;现有文案:" + string.Join("|", texts));
            Assert.True(texts.Contains(I18n.L("将新建实例")), "名称提示不对;现有文案:" + string.Join("|", texts));
        });
    }

    /// <summary>【Windows 源】<c>:429-491</c> 开始安装:新名字先 <c>create_instance</c>,再 <c>install_game</c>(带 loader/loader_version/instance/extra)。</summary>
    [Fact]
    public void Install_page_start_creates_the_instance_then_starts_install_game()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var api = C6aFiles.Api();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            InstallPage install = C5aFiles.Mount(harness, new InstallPage("1.20.1", I18n.L("正式版"), () => { }));
            C5aFiles.Pump(() => api.Count("list_loader_versions") >= 6, 5000);
            install.Select(2); // Forge(0=不安装 / 1=Fabric / 2=Forge / 3=NeoForge / 4=Quilt)
            C5aFiles.Settle(2);

            C5aFiles.Click(C5aFiles.ByText(install, I18n.L("开始安装")));
            C5aFiles.Pump(() => api.Count("install_game") >= 1, 5000);
            Assert.Equal(1, api.Count("create_instance"));
            Assert.Equal("Forge 1.20.1", C5aFiles.ParamAt(api, "create_instance", "name"));
            Assert.Equal("1.20.1", C5aFiles.ParamAt(api, "install_game", "version"));
            Assert.Equal("Forge", C5aFiles.ParamAt(api, "install_game", "loader"));
            Assert.Equal("0.15.11", C5aFiles.ParamAt(api, "install_game", "loader_version"));
            Assert.Equal("Forge 1.20.1", C5aFiles.ParamAt(api, "install_game", "instance"));
            Assert.True(C5aFiles.ParamHas(api, "install_game", "\"optifine\""));
            C5aFiles.Pump(() => toasts.Has(I18n.L("已加入队列")));
        });
    }

    /// <summary>【Windows 源】<c>:458-488</c> Fabric + 勾了 API:等 <c>install_game</c> 那个任务成完之后再排 <c>install_mod</c>(Fabric API)。</summary>
    [Fact]
    public void Install_page_fabric_with_api_queues_the_fabric_api_mod_after_the_game_task()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var tasks = new C3cTaskStore();
            using var api = C6aFiles.Api();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            InstallPage install = C5aFiles.Mount(harness, new InstallPage("1.20.1", I18n.L("正式版"), () => { }));
            C5aFiles.Pump(() => api.Count("list_loader_versions") >= 6, 5000);
            install.Select(1); // Fabric(默认勾着 Fabric API)
            C5aFiles.Settle(2);

            C5aFiles.Click(C5aFiles.ByText(install, I18n.L("开始安装")));
            C5aFiles.Pump(() => api.Count("install_game") >= 1, 5000);
            Assert.Equal(0, api.Count("install_mod"));

            // 【时机】"请求到达" ≠ "续体跑完":InstallPage 是在 `await StartTaskAsync` **之后**才把
            // TaskStore.Updated 的处理器挂上的(Windows 同)。所以先等一个可观测的效果 ——
            // 加入队列的 Toast(它在挂完处理器之后才发)—— 再去构造"任务完成"的事件。
            C5aFiles.Pump(() => toasts.Has(I18n.L("已加入队列")), 5000);

            // 任务完成 ⇒ 续体排 Fabric API(先核对任务行真的进了 finished + Success:
            // Windows 的 TaskStore.Handle("finished") 只在行取得到时才写这两个字段并抛 Updated)
            TaskStore.Handle(new BridgeEvent { Event = "task_added", TaskId = "task-1", Title = "装游戏" });
            TaskStore.Handle(new BridgeEvent { Event = "finished", TaskId = "task-1", Success = true });
            TaskRow? row = TaskStore.Get("task-1");
            Assert.True(row != null && row.Finished && row.Success, "任务行没进 finished/Success 状态");
            C5aFiles.Pump(() => api.Count("install_mod") >= 1, 8000);
            Assert.Equal("Fabric API", C5aFiles.ParamAt(api, "install_mod", "name"));
            Assert.Equal("fabric-api", C5aFiles.ParamAt(api, "install_mod", "extra", "slug"));
            Assert.Equal("Modrinth", C5aFiles.ParamAt(api, "install_mod", "extra", "source"));
        });
    }

    /// <summary>【Windows 源】<c>:431-436</c> 名称为空:如实 Toast,一个后端调用都不发。</summary>
    [Fact]
    public void Install_page_refuses_an_empty_instance_name()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var api = C6aFiles.Api();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            InstallPage install = C5aFiles.Mount(harness, new InstallPage("1.20.1", I18n.L("正式版"), () => { }));
            C5aFiles.Pump(() => api.Count("list_loader_versions") >= 6, 5000);

            TextBox name = C5aFiles.Descendants(install).OfType<TextBox>().First();
            name.Text = "   ";
            C5aFiles.Settle(2);
            C5aFiles.Click(C5aFiles.ByText(install, I18n.L("开始安装")));
            C5aFiles.Pump(() => toasts.Has(I18n.L("请填写实例名称")), 5000);
            Assert.Equal(0, api.Count("install_game"));
            Assert.Equal(0, api.Count("create_instance"));
        });
    }

    /// <summary>【Windows 源】<c>:165-171</c> 下载页的「安装」= 把内容换成 <see cref="InstallPage"/>;返回回调把外壳换回来。</summary>
    [Fact]
    public void Download_page_swaps_in_the_install_sub_view_and_back()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C6aScope.Capture();
            using var api = C6aFiles.Api().Route("fetch_version_list", "[]");
            using var harness = C4LayerHost.Show();
            DownloadPage page = C5aFiles.Mount(harness, new DownloadPage());
            C5aFiles.Load(page);
            object? shellContent = page.Content;

            VersionRow row = new VersionRow { Version = "1.20.1", Type = "release" };
            page.OpenInstall(row);
            C5aFiles.Settle(3);
            InstallPage install = Assert.IsType<InstallPage>(page.Content);
            Assert.True(C5aFiles.HasText(install, I18n.L("安装 Minecraft {0}", "1.20.1")));

            // 返回:内容换回原来的外壳
            C5aFiles.Click(C5aFiles.Descendants(install).OfType<Button>().First());
            C5aFiles.Settle(2);
            Assert.Same(shellContent, page.Content);
        });
    }
}
