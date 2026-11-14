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
using TipCL.Models;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C4(三):<see cref="InstancePage"/>(导航 id <c>instance</c>)与 <see cref="InstanceDetailPage"/>
/// (**不是导航页**,由实例库「实例详情」换进页内容里)。
///
/// 纪律与 C4LibraryTests 一致:每条碰 Avalonia 的用例体走 <c>AvaloniaSession.Run(...)</c>(D-79);
/// 全局静态由 <see cref="C4Scope"/> 快照还原;**非按钮**的交互(行选中 / 右键菜单 / 键盘)走页面
/// 为此留的 internal 处理器体,不构造 Pointer 事件、不模拟输入设备(D-115 / D-127);
/// 对话框照 D-113「先泵出卡片再点按钮」。
/// </summary>
public class C4InstanceTests
{
    // ================================================================ InstancePage 注册与构造

    /// <summary>【Windows 源】<c>InstancePage.cs:65</c>:标题是「实例」(导航行标题「版本管理」是另一件事)。</summary>
    [Fact]
    public void Instance_page_title_matches_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            AppServices.Client = null;
            var page = new InstancePage();
            Assert.Equal(I18n.L("实例"), page.Title);
            Assert.NotNull(page.Content);
        });
    }

    /// <summary>
    /// 【Windows 源】<c>InstanceDetailPage.cs:14</c> 的注释 + Windows 的导航表:<c>InstanceDetailPage</c>
    /// **没有**自己的导航 id,它是页内切入的。这条把"没人给它注册"钉住(免得以后有人误加一行注册)。
    /// </summary>
    [Fact]
    public void Instance_detail_page_is_not_a_registered_navigation_page()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            Assert.DoesNotContain("instance-detail", PageRegistry.Ids);
            Assert.DoesNotContain("detail", PageRegistry.Ids);
            Assert.Null(PageRegistry.Create("instance-detail"));
        });
    }

    /// <summary>【Windows 源】<c>InstancePage.cs:70-98</c>:Section 头 + 四个动作钮(顺序照 Windows 的 Ui.H)。</summary>
    [Fact]
    public void Instance_page_builds_the_windows_section_head()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            Assert.True(C4Files.HasText(page, I18n.L("每个实例是一份独立的 .minecraft,互不干扰")));
            Assert.True(C4Files.HasButton(page, I18n.L("新建实例")));
            Assert.True(C4Files.HasButton(page, I18n.L("导入官方启动器")));
            Assert.True(C4Files.HasButton(page, I18n.L("打开游戏目录")));
            Assert.True(C4Files.HasButton(page, I18n.L("刷新")));
            // 新建实例是 Primary(Windows 的 BtnKind.Primary)
            Button create = C4Files.Descendants(page).OfType<Button>()
                .First(b => AutomationProperties.GetName(b) == I18n.L("新建实例"));
            Assert.Contains("btn-primary", create.Classes);
            Assert.False(toasts.Has(I18n.L("加载失败")));
        });
    }

    /// <summary>【Windows 源】<c>InstancePage.cs:127-140</c>:首屏拉 <c>get_instances</c> + <c>get_setting</c>。</summary>
    [Fact]
    public void Instance_page_load_calls_the_windows_backend_methods()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("get_instances", "[" + C4Files.InstanceJson("甲") + "]").Route("get_setting", "\"甲\"");
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            Assert.Contains("get_instances", api.Methods());
            Assert.Contains("get_setting", api.Methods());
            Assert.Equal("default_instance", C4Files.ParamString(api, "get_setting", "key"));
        });
    }

    /// <summary>【Windows 源】<c>InstancePage.cs:219-308</c>:卡片 = 图标 + 名称 + 副行 + 标签行 + 四个动作;默认实例带「默认」标签。</summary>
    [Fact]
    public void Instance_card_matches_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[" + C4Files.InstanceJson("甲", 3, pack: "Fabulously", mc: "1.20.1", javaLabel: "17.0.1") + "]")
                .Route("get_setting", "\"甲\"");
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            Assert.True(C4Files.HasText(page, "甲"));
            // 副行:有整合包就显示「整合包 + 打包版本」(pack_version 为空 ⇒ Trim 掉尾部空格后的结果)
            Assert.True(C4Files.HasText(page, "Fabulously"));
            Assert.True(C4Files.HasText(page, I18n.L("默认")));
            Assert.True(C4Files.HasText(page, I18n.L("{0} 个版本", 3)));
            Assert.True(C4Files.HasText(page, "17.0.1"));
            Assert.True(C4Files.HasText(page, "MC 1.20.1"));
            Assert.True(C4Files.HasButton(page, I18n.L("启动")));
            Assert.True(C4Files.HasButton(page, I18n.L("管理这个实例的模组")));
            Assert.True(C4Files.HasButton(page, I18n.L("存档管理")));
            Assert.True(C4Files.HasButton(page, I18n.L("更多")));
        });
    }

    /// <summary>【Windows 源】<c>InstancePage.cs:200-201</c>:没有整合包时副行退回 MC 版本。</summary>
    [Fact]
    public void Instance_card_sub_line_falls_back_to_the_mc_version()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("get_instances", "[" + C4Files.InstanceJson("甲", 1, mc: "1.19.4") + "]");
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            Assert.True(C4Files.HasText(page, "1.19.4"));
            Assert.False(C4Files.HasText(page, I18n.L("默认")));
        });
    }

    /// <summary>【Windows 源】<c>InstancePage.cs:150-153</c>:计数两种口径(有过滤 / 无过滤)+ 空态标题切换。</summary>
    [Fact]
    public void Instance_page_filter_changes_the_count_and_the_empty_title()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[" + C4Files.InstanceJson("生存服") + "," + C4Files.InstanceJson("创造服") + "]");
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            Assert.True(C4Files.HasText(page, I18n.L("共 {0} 个", 2)));
            TextBox search = C4Files.Find<TextBox>(page, "过滤实例框");
            search.Text = "创造";
            // 【Windows 源】220ms 防抖:刚打完字**不该**立刻重画(离屏里 DispatcherTimer 的推进不可靠,
            // 所以这里只钉"没有立刻变"这一半,再用一次刷新把 Render 那条路走一遍验过滤结果)
            C4Files.Settle(4);
            Assert.True(C4Files.HasText(page, I18n.L("共 {0} 个", 2)), "防抖没过点之前计数不该变");
            C4Files.PumpTask(page.RefreshAsync());
            Assert.True(C4Files.HasText(page, I18n.L("{0} / {1} 个", 1, 2)));
            Assert.True(C4Files.HasText(page, "创造服"));
            Assert.False(C4Files.HasText(page, "生存服"));
            // 空态组件是收起的(还有匹配项)。注意:有实例时空态**标题**已被 Render 改成「没有匹配的实例」
            // (Windows 同: _emptyTitle.Text = _rows.Count > 0 ? 没有匹配的实例 : 还没有实例),按这个认它。
            SPanel empty = EmptyPanel(page, I18n.L("没有匹配的实例"));
            Assert.False(empty.IsVisible);

            search.Text = "不存在";
            C4Files.PumpTask(page.RefreshAsync());
            Assert.True(C4Files.HasText(page, I18n.L("没有匹配的实例")));
            Assert.True(C4Files.HasText(page, I18n.L("{0} / {1} 个", 0, 2)));
            Assert.False(C4Files.HasText(page, "创造服"));
            Assert.True(empty.IsVisible);
        });
    }

    /// <summary>【Windows 源】:一个实例都没有时的空态文案(与过滤没命中时不同)。</summary>
    [Fact]
    public void Instance_page_empty_state_matches_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            Assert.True(C4Files.HasText(page, I18n.L("还没有实例")));
            Assert.True(C4Files.HasText(page, I18n.L("点右上角「新建实例」建一个")));
            Assert.True(C4Files.HasText(page, I18n.L("共 {0} 个", 0)));
        });
    }

    /// <summary>【Windows 源】<c>InstancePage.cs:335-343</c>:上下移动选中;没有选中时向下选第一个、向上选最后一个。</summary>
    [Fact]
    public void Instance_page_keyboard_navigation_moves_the_selection()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[" + C4Files.InstanceJson("甲") + "," + C4Files.InstanceJson("乙") + "]");
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            Assert.True(page.HandleNavKey(Key.Down));
            Assert.Equal("甲", page.SelectedNow());
            Assert.True(page.HandleNavKey(Key.Down));
            Assert.Equal("乙", page.SelectedNow());
            // 已经在最后一个 ⇒ 夹住不动
            Assert.True(page.HandleNavKey(Key.Down));
            Assert.Equal("乙", page.SelectedNow());
            Assert.True(page.HandleNavKey(Key.Up));
            Assert.Equal("甲", page.SelectedNow());
        });
    }

    /// <summary>【Windows 源】<c>InstancePage.cs:100-124</c>:焦点在输入框里时键盘导航**全部**不响应。</summary>
    [Fact]
    public void Instance_page_ignores_keyboard_navigation_while_the_search_box_has_focus()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("get_instances", "[" + C4Files.InstanceJson("甲") + "," + C4Files.InstanceJson("乙") + "]");
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            TextBox search = C4Files.Find<TextBox>(page, "过滤实例框");
            search.Focus();
            C4Files.Flush(2);
            Assert.True(search.IsFocused, "离屏里 Focus() 必须真的生效,否则这条用例没有意义");
            Assert.False(page.HandleNavKey(Key.Down));
            Assert.Null(page.SelectedNow());
        });
    }

    /// <summary>【Windows 源】<c>InstancePage.cs:114-121</c>:回车启动选中的那个(要经过 <c>save_settings</c>)。</summary>
    [Fact]
    public void Instance_page_enter_launches_the_selected_instance()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("get_instances", "[" + C4Files.InstanceJson("甲") + "]");
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            // 没有选中时回车什么都不做(Windows 的 _selected != null 判据)
            Assert.False(page.HandleNavKey(Key.Enter));
            page.Select("甲");
            Assert.True(page.HandleNavKey(Key.Enter));
            C4Files.Pump(() => api.Count("save_settings") >= 1);
            Assert.Equal("甲", C4Files.ParamStringIn(api, "save_settings", "data", "default_instance"));
        });
    }

    /// <summary>【Windows 源】<c>InstancePage.cs:393-470</c>:「更多」菜单的项与顺序(第 6 项前一条分隔线)。</summary>
    [Fact]
    public void Instance_row_menu_matches_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            ContextMenu menu = page.BuildRowMenu(new Border(), "甲");
            Assert.Equal(new List<string>
            {
                I18n.L("打开文件夹"), I18n.L("设为默认实例"), I18n.L("选择 Java..."), I18n.L("挑着更新模组..."),
                I18n.L("后台检查模组更新"), I18n.L("导出为整合包"), "—", I18n.L("重命名..."), I18n.L("删除实例"),
            }, C4Files.MenuHeads(menu));
        });
    }

    /// <summary>【Windows 源】:菜单「打开文件夹」调 <c>open_instance_folder</c>。</summary>
    [Fact]
    public void Instance_row_menu_open_folder_calls_the_backend()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            C4Files.Invoke(C4Files.MenuItemByHead(page.BuildRowMenu(new Border(), "甲"), I18n.L("打开文件夹")));
            C4Files.Pump(() => api.Count("open_instance_folder") >= 1);
            C4Files.AssertParam(api, "open_instance_folder", "name", "甲");
        });
    }

    /// <summary>【Windows 源】<c>InstancePage.cs:412-416</c>:「后台检查模组更新」起 <c>start_mod_updates</c> 并飞向任务页。</summary>
    [Fact]
    public void Instance_row_menu_background_mod_check_starts_the_task()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            C4Files.Invoke(C4Files.MenuItemByHead(page.BuildRowMenu(new Border(), "甲"), I18n.L("后台检查模组更新")));
            C4Files.Pump(() => api.Count("start_mod_updates") >= 1);
            C4Files.AssertParam(api, "start_mod_updates", "instance", "甲");
            // 离屏 Motion.Enabled 恒假 ⇒ 飞行 chip 不会出现(第一道守卫就返回,D-131 / C3d 的既有结论)
            Assert.Equal(0, C4Files.LayerCount());
        });
    }

    /// <summary>【Windows 源】<c>InstancePage.cs:417-429</c>:「导出为整合包」选路径(离屏 null ⇒ 不起任务)。</summary>
    [Fact]
    public void Instance_row_menu_export_degrades_without_a_desktop_picker()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            C4Files.Invoke(C4Files.MenuItemByHead(page.BuildRowMenu(new Border(), "甲"), I18n.L("导出为整合包")));
            C4Files.Settle(4);
            Assert.Equal(0, api.Count("export_modpack"));
        });
    }

    /// <summary>【Windows 源】:菜单「设为默认实例」写 <c>save_settings</c> 并 Toast。</summary>
    [Fact]
    public void Instance_row_menu_set_default_writes_the_setting()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            C4Files.Invoke(C4Files.MenuItemByHead(page.BuildRowMenu(new Border(), "甲"), I18n.L("设为默认实例")));
            C4Files.Pump(() => api.Count("save_settings") >= 1);
            Assert.Equal("甲", C4Files.ParamStringIn(api, "save_settings", "data", "default_instance"));
            C4Files.Pump(() => toasts.Has(I18n.L("默认实例")));
        });
    }

    /// <summary>【Windows 源】<c>InstancePage.cs:444-455</c>:「删除实例」确认后调 <c>delete_instance</c>。</summary>
    [Fact]
    public void Instance_row_menu_delete_confirms_then_calls_delete_instance()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            C4Files.Invoke(C4Files.MenuItemByHead(page.BuildRowMenu(new Border(), "甲"), I18n.L("删除实例")));
            C4Files.AnswerDialog(I18n.L("永久删除"));
            C4Files.Pump(() => api.Count("delete_instance") >= 1);
            C4Files.AssertParam(api, "delete_instance", "name", "甲");
            C4Files.Pump(() => toasts.Has(I18n.L("已删除")));
        });
    }

    /// <summary>【Windows 源】:菜单「管理这个实例的模组」/「存档管理」两钮打开 C4 的两个对话框。</summary>
    [Fact]
    public void Instance_card_buttons_open_the_two_management_dialogs()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("get_instances", "[" + C4Files.InstanceJson("甲") + "]");
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            C4Files.Click(C4Files.ByText(page, I18n.L("管理这个实例的模组")));
            C4Files.ClosePanel(C4Files.PanelCard(I18n.L("模组管理 · {0}", "甲")));
            C4Files.Click(C4Files.ByText(page, I18n.L("存档管理")));
            C4Files.ClosePanel(C4Files.PanelCard(I18n.L("存档管理 · {0}", "甲")));
            Assert.Equal(0, C4Files.LayerCount());
        });
    }

    /// <summary>
    /// 【C7a 更正 · 钉新契约】`instance` 这个 id 现在的目标是 Windows 原版那一页(版本管理);
    /// <see cref="InstancePage"/> 不再注册(Windows 原版里它是死代码)。与上面那条"还原前提"并存。
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

    /// <summary>【Windows 源】<c>InstancePage.cs:345-355</c>:卡片上的「启动」把该实例设为默认、让启动页优先用它、再跳启动页。</summary>
    [Fact]
    public void Instance_card_launch_button_navigates_after_setting_the_default()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var shell = ShellFixture.Create();
            using var api = new C3cFakeApi().Route("get_instances", "[" + C4Files.InstanceJson("甲") + "]");
            // 【C7a 更正 · D-126】本用例全程把 `instance` 当 InstancePage 用;而 `instance` 已按 Windows 原版
            // 改注册成 VersionManagePage(InstancePage 在原版是死代码)。先把前提还原(测试专用的还原入口),
            // 原断言一字不改;作用域收尾还原注册表快照。
            InstancePageRegistration.Register();
            shell.Window.Navigate("instance");
            InstancePage page = (InstancePage)shell.Window.CurrentPage!;
            C4Files.Load(page);
            C4Files.Click(C4Files.ByText(page, I18n.L("启动")));
            C4Files.Pump(() => api.Count("save_settings") >= 1);
            Assert.Equal("甲", C4Files.ParamStringIn(api, "save_settings", "data", "default_instance"));
            C4Files.Pump(() => shell.Window.CurrentPageId == "launch");
            Assert.Equal("launch", shell.Window.CurrentPageId);
        });
    }

    /// <summary>【Windows 源】<c>InstancePage.cs:507-527</c>:「打开游戏目录」先问根目录再 <c>open_media</c>,成功 Toast 里带名字。</summary>
    [Fact]
    public void Instance_page_open_game_dir_uses_open_media()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("game_root_path", "\"/games\"")
                .Route("game_root_name", "\"我的世界\"")
                .Route("open_media", "true");
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            C4Files.Click(C4Files.ByText(page, I18n.L("打开游戏目录")));
            C4Files.Pump(() => api.Count("open_media") >= 1);
            C4Files.AssertParam(api, "open_media", "path", "/games");
            C4Files.Pump(() => toasts.Has(I18n.L("已打开")));
            Assert.Contains(toasts.Items, t => t.Body == "我的世界 · /games");
        });
    }

    /// <summary>【Windows 源】根目录拿不到 ⇒ Toast 警告「后端没给出目录」。</summary>
    [Fact]
    public void Instance_page_open_game_dir_warns_when_the_backend_has_no_root()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            C4Files.Click(C4Files.ByText(page, I18n.L("打开游戏目录")));
            C4Files.Pump(() => toasts.Has(I18n.L("打不开游戏目录")));
            Assert.Contains(toasts.Items, t => t.Body == I18n.L("后端没给出目录") && t.Kind == ToastKind.Warning);
        });
    }

    /// <summary>【Windows 源】<c>InstancePage.cs:529-538</c>:「新建实例」问名字 → <c>create_instance</c> → Toast。</summary>
    [Fact]
    public void Instance_page_create_prompts_and_calls_create_instance()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            C4Files.Click(C4Files.ByText(page, I18n.L("新建实例")));
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            C4Files.Descendants(card).OfType<TextBox>().First().Text = "新实例";
            C4Files.Click(C4Files.ByText(card, I18n.L("确定")));
            C4Files.Pump(() => api.Count("create_instance") >= 1);
            C4Files.AssertParam(api, "create_instance", "name", "新实例");
            C4Files.Pump(() => toasts.Has(I18n.L("已创建")));
        });
    }

    /// <summary>【Windows 源】<c>InstancePage.cs:540-559</c>:没探测到官方启动器 ⇒ 只 Toast 警告。</summary>
    [Fact]
    public void Instance_page_migrate_warns_when_no_official_launcher()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("detect_official_launcher", "false");
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            C4Files.Click(C4Files.ByText(page, I18n.L("导入官方启动器")));
            C4Files.Pump(() => toasts.Has(I18n.L("没找到官方启动器")));
            Assert.Equal(0, api.Count("migrate_official_launcher"));
        });
    }

    /// <summary>【Windows 源】<c>InstancePage.cs:160-181</c>:卡片按实例名复用(刷新不会重建控件),所以选中/悬停状态不丢。</summary>
    [Fact]
    public void Instance_card_controls_are_reused_across_renders()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("get_instances", "[" + C4Files.InstanceJson("甲") + "]");
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            Border? first = FirstCard(page);
            Assert.NotNull(first);
            C4Files.PumpTask(page.RefreshAsync());
            Assert.Same(first, FirstCard(page));
        });
    }

    /// <summary>【Windows 源】<c>InstancePage.cs:142-145</c>:切页离开停掉防抖表(离屏里直接调 OnHidden 验).</summary>
    [Fact]
    public void Instance_page_hides_stops_the_debounce_timer()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("get_instances", "[" + C4Files.InstanceJson("甲") + "]");
            InstancePage page = C4Files.Mount(harness, new InstancePage());
            C4Files.Load(page);
            TextBox search = C4Files.Find<TextBox>(page, "过滤实例框");
            search.Text = "甲";
            page.OnHidden();
            int before = api.Count("get_instances");
            C4Files.Settle(40);
            // 停表之后不该再因为那次输入重画/重拉
            Assert.Equal(before, api.Count("get_instances"));
            Assert.True(C4Files.HasText(page, I18n.L("共 {0} 个", 1)));
        });
    }

    /// <summary>【Windows 源】<c>InstancePage.cs:357-366</c>:<c>SetDefaultAsync</c> 的载荷形状(<c>data.default_instance</c>)。</summary>
    [Fact]
    public void Set_default_async_sends_the_windows_payload()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            C4Files.PumpTask(InstancePage.SetDefaultAsync("甲"));
            Assert.Equal("甲", C4Files.ParamStringIn(api, "save_settings", "data", "default_instance"));
        });
    }

    // ================================================================ InstanceDetailPage

    /// <summary>【Windows 源】<c>InstanceDetailPage.cs:46-136</c>:头部(返回 + 标题 + 副行 + 启动)+ 五个分段 + 版本下拉 + 更多管理。</summary>
    [Fact]
    public void Instance_detail_builds_the_windows_head_and_rail()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            var detail = C4Files.Mount(harness, new InstanceDetailPage(new InstanceInfo { Name = "甲", Mc = "1.20.1", Pack = "Fabulously" }, delegate { }));
            C4Files.Flush(2);
            Assert.Equal("甲", detail.Title);
            Assert.True(C4Files.HasText(detail, "甲"));
            Assert.True(C4Files.HasText(detail, "Minecraft 1.20.1 · Fabulously"));
            Assert.True(C4Files.HasButton(detail, I18n.L("返回")));
            Assert.True(C4Files.HasButton(detail, I18n.L("启动")));
            Assert.True(C4Files.HasButton(detail, I18n.L("更多管理")));
            // 五个分段按钮(顺序与 Windows 的 Segs 一致),类名是 seg-item;外壳那一条是 seg-rail
            List<ToggleButton> segs = C4Files.Descendants(detail).OfType<ToggleButton>().ToList();
            Assert.Equal(C4Expected.DetailSegs.Length, segs.Count);
            Assert.Equal(C4Expected.DetailSegs, segs.Select(s => s.Content as string ?? "").ToList());
            Assert.All(segs, s => Assert.Contains("seg-item", s.Classes));
            Assert.Contains(C4Files.Descendants(detail).OfType<Border>(), b => b.Classes.Contains("seg-rail"));
            // 标题用的是 Windows 的 T.PageTitle 命名样式
            Assert.Contains(C4Files.TextsWith(detail, "甲"), t => t.Classes.Contains("t-page-title"));
        });
    }

    /// <summary>【Windows 源】<c>InstanceDetailPage.cs:139-160</c>:<c>get_version_rows</c> 填版本下拉(「id · 隔离标签」)。</summary>
    [Fact]
    public void Instance_detail_fills_the_version_combo()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("get_version_rows", "[" +
                C4Files.VersionRowJson("1.20.1", isolated: true, isolationLabel: "隔离全部") + "," +
                C4Files.VersionRowJson("1.19.4", isolated: false) + "]");
            var detail = C4Files.Mount(harness, new InstanceDetailPage(new InstanceInfo { Name = "甲" }, delegate { }));
            // 【D1 修法 · A 组】等下拉**真的填好**(续体产出),不是"请求到达";
            // 顺带删掉那处 `C4Files.Flush(2);`(固定刷一次的残留)。断言一字不改。
            ComboBox combo = C4Files.Find<ComboBox>(detail, "版本下拉");
            C4Files.Pump(() => combo.Items.Count == 2, 9000);
            Assert.Equal(2, combo.Items.Count);
            Assert.Equal("1.20.1 · 隔离全部", combo.Items[0]);
            Assert.Equal("1.19.4", combo.Items[1]);
            Assert.Equal(200.0, combo.MinWidth);
            Assert.True(combo.IsVisible);
            Assert.Contains("\"include_hidden\":false", api.ParamsOf("get_version_rows"));
        });
    }

    /// <summary>【Windows 源】:一个版本都没有时下拉藏起来,并照样应用分段规则。</summary>
    [Fact]
    public void Instance_detail_hides_the_version_combo_when_there_are_no_versions()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            var detail = C4Files.Mount(harness, new InstanceDetailPage(new InstanceInfo { Name = "甲" }, delegate { }));
            C4Files.Pump(() => api.Count("get_version_rows") >= 1);
            C4Files.Flush(2);
            Assert.False(C4Files.Find<ComboBox>(detail, "版本下拉").IsVisible);
            // cur == null ⇒ shared=false、isolatedNote=false ⇒ 前四个分段可见
            List<ToggleButton> segs = C4Files.Descendants(detail).OfType<ToggleButton>().ToList();
            Assert.All(segs.Take(4), s => Assert.True(s.IsVisible));
        });
    }

    /// <summary>
    /// 【Windows 源】<c>InstanceDetailPage.cs:163-177</c>:选中版本 <c>isolated == false</c>(共享/大锅饭)时
    /// 前四个分段藏起来、显示灰字说明、并强制切到「设置」。
    /// </summary>
    [Fact]
    public void Instance_detail_shared_mode_hides_the_asset_segments()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("get_version_rows", "[" + C4Files.VersionRowJson("1.20.1", isolated: false) + "]");
            var detail = C4Files.Mount(harness, new InstanceDetailPage(new InstanceInfo { Name = "甲" }, delegate { }));
            C4Files.Pump(() => api.Count("get_version_rows") >= 1);
            C4Files.Flush(2);
            List<ToggleButton> segs = C4Files.Descendants(detail).OfType<ToggleButton>().ToList();
            Assert.All(segs.Take(4), s => Assert.False(s.IsVisible));
            Assert.True(segs[4].IsVisible);
            Assert.True(segs[4].IsChecked);
            Assert.True(C4Files.HasText(detail, I18n.L("该版本为共享(大锅饭)模式,模组/存档使用实例共享目录;开启独立隔离请见版本管理页")));
            // 两条说明一直都在树上,靠 IsVisible 区分(Windows 也是改 Visibility)
            Assert.True(HasVisibleText(detail, I18n.L("该版本为共享(大锅饭)模式,模组/存档使用实例共享目录;开启独立隔离请见版本管理页")));
            Assert.False(HasVisibleText(detail, I18n.L("该版本已开启隔离:模组/日志/资源包位于 versions\\<版本> 目录下,与实例根目录分开")));
        });
    }

    /// <summary>【Windows 源】:开了隔离就显示那条隔离说明(而不是共享说明)。</summary>
    [Fact]
    public void Instance_detail_isolated_mode_shows_the_isolation_note()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("get_version_rows", "[" + C4Files.VersionRowJson("1.20.1", isolated: true) + "]");
            var detail = C4Files.Mount(harness, new InstanceDetailPage(new InstanceInfo { Name = "甲" }, delegate { }));
            // 【D1 修法 · A 组】等那段说明**可见地画出来**(续体产出);删掉固定刷一次的残留 Flush;断言一字不改
            C4Files.Pump(() => HasVisibleText(detail, I18n.L("该版本已开启隔离:模组/日志/资源包位于 versions\\<版本> 目录下,与实例根目录分开")), 9000);
            Assert.True(C4Files.HasText(detail, I18n.L("该版本已开启隔离:模组/日志/资源包位于 versions\\<版本> 目录下,与实例根目录分开")));
            Assert.True(HasVisibleText(detail, I18n.L("该版本已开启隔离:模组/日志/资源包位于 versions\\<版本> 目录下,与实例根目录分开")));
            Assert.False(HasVisibleText(detail, I18n.L("该版本为共享(大锅饭)模式,模组/存档使用实例共享目录;开启独立隔离请见版本管理页")));
            Assert.All(C4Files.Descendants(detail).OfType<ToggleButton>().Take(4), s => Assert.True(s.IsVisible));
        });
    }

    /// <summary>
    /// 【mac 侧降级,见 D-134】「设置」分段要内嵌的 <c>VersionManagePage</c> 属别的工作包,mac 侧不存在:
    /// 挂点没接时显示一句**如实的**占位说明,而不是空白、也不是假装能用。
    /// </summary>
    [Fact]
    public void Instance_detail_settings_segment_reports_the_missing_page_honestly()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            InstanceDetailPage.SettingsPageFactory = null;
            var detail = C4Files.Mount(harness, new InstanceDetailPage(new InstanceInfo { Name = "甲" }, delegate { }));
            C4Files.Flush(2);
            detail.ShowSeg(4);
            C4Files.Flush(2);
            Assert.True(C4Files.HasText(detail, I18n.L("版本管理页还没移植")));
            Assert.True(C4Files.HasText(detail, I18n.L("这一栏内嵌的是「版本管理」页,它属于后续工作包;已注入 SettingsPageFactory 时会正常显示")));
        });
    }

    /// <summary>【Windows 源】<c>InstanceDetailPage.cs:189-195</c>:把工厂接上就真的嵌进去并 <c>EnsureLoadedAsync</c> + <c>OnShown</c>。</summary>
    [Fact]
    public void Instance_detail_settings_segment_embeds_the_injected_page()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            SettingsProbePage? probe = null;
            InstanceDetailPage.SettingsPageFactory = delegate(string inst)
            {
                probe = new SettingsProbePage(inst);
                return probe;
            };
            var detail = C4Files.Mount(harness, new InstanceDetailPage(new InstanceInfo { Name = "甲" }, delegate { }));
            C4Files.Flush(2);
            detail.ShowSeg(4);
            C4Files.Pump(() => probe != null && probe.LoadRan);
            Assert.Equal("甲", probe!.Instance);
            Assert.True(probe.ShownRan, "OnShown 也要被调到(Windows 同)");
            ContentControl host = SegmentHost(detail);
            Assert.Same(probe, host.Content);
            // 再切一次不会重新建页(Windows 的 _settings ??=)
            detail.ShowSeg(0);
            detail.ShowSeg(4);
            C4Files.Flush(2);
            Assert.Same(probe, host.Content);
        });
    }

    /// <summary>【Windows 源】<c>InstanceDetailPage.cs:186</c>:「更多管理」只在模组/存档两个分段出现,并按分段选对话框。</summary>
    [Fact]
    public void Instance_detail_more_button_switches_between_the_two_dialogs()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            var detail = C4Files.Mount(harness, new InstanceDetailPage(new InstanceInfo { Name = "甲" }, delegate { }));
            C4Files.Flush(2);
            detail.ShowSeg(0);
            C4Files.Flush(2);
            C4Files.Click(C4Files.ByText(detail, I18n.L("更多管理")));
            C4Files.ClosePanel(C4Files.PanelCard(I18n.L("模组管理 · {0}", "甲")));

            detail.ShowSeg(1);
            C4Files.Flush(2);
            C4Files.Click(C4Files.ByText(detail, I18n.L("更多管理")));
            C4Files.ClosePanel(C4Files.PanelCard(I18n.L("存档管理 · {0}", "甲")));

            // 设置分段上不再显示「更多管理」
            detail.ShowSeg(4);
            C4Files.Flush(2);
            Assert.False(C4Files.ByText(detail, I18n.L("更多管理")).IsVisible);
            Assert.Equal(0, C4Files.LayerCount());
        });
    }

    /// <summary>【Windows 源】<c>InstanceDetailPage.cs:206-231</c>:模组分段按「实例共享目录优先」拉列表,开关写 enable/disable。</summary>
    [Fact]
    public void Instance_detail_mods_segment_uses_the_shared_target_and_toggles()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_mods_targets", "[{\"label\":\"隔离 1.20.1\",\"value\":\"1.20.1\"},{\"label\":\"共享\",\"value\":\"\"}]")
                .Route("get_installed_mod_entries", "[{\"filename\":\"sodium.jar\",\"name\":\"Sodium\",\"enabled\":true}]");
            var detail = C4Files.Mount(harness, new InstanceDetailPage(new InstanceInfo { Name = "甲" }, delegate { }));
            C4Files.Flush(2);
            detail.ShowSeg(0);
            // 【D1 修法 · A 组】等那一行**画出来**(续体产出);删掉固定刷一次的残留 Flush。断言一字不改
            C4Files.Pump(() => C4Files.HasText(detail, "Sodium"), 9000);
            Assert.True(C4Files.HasText(detail, "Sodium"));
            // 有 Value 为空的项 ⇒ 用共享目录(version = "")
            C4Files.AssertParam(api, "get_installed_mod_entries", "version", "");
            CheckBox sw = C4Files.Descendants(detail).OfType<CheckBox>().First();
            sw.IsChecked = false;
            C4Files.Pump(() => api.Count("disable_mod") >= 1);
            C4Files.AssertParam(api, "disable_mod", "version", "");
        });
    }

    /// <summary>【Windows 源】<c>InstanceDetailPage.cs:232-241</c>:存档分段的「打开」调 <c>open_save</c>。</summary>
    [Fact]
    public void Instance_detail_saves_segment_calls_open_save()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("list_saves", "[{\"name\":\"World\",\"size\":\"2 MB\",\"last_played\":\"今天\"}]");
            var detail = C4Files.Mount(harness, new InstanceDetailPage(new InstanceInfo { Name = "甲" }, delegate { }));
            C4Files.Flush(2);
            detail.ShowSeg(1);
            // 【D1 修法 · 形状 A / D-152】原来等的是"请求到达"(`Count("list_saves") >= 1`)再 `Flush(2)` 就断言界面文案;
            // 但那一行的**控件**是在续体里(拿到回包之后)才加进 `_rows` 的 ⇒ VSTest 通道下偶发 46ms 假红
            // (`HasText("World")` 拿到 false)。改成**等那个界面效果本身**(条件 + 9000 截止),断言一字不改。
            C4Files.Pump(() => C4Files.HasText(detail, "World"), 9000);
            Assert.True(C4Files.HasText(detail, "World"));
            Assert.True(C4Files.HasText(detail, "2 MB · 今天"));
            C4Files.Click(C4Files.ByText(detail, I18n.L("打开")));
            C4Files.Pump(() => api.Count("open_save") >= 1);
            C4Files.AssertParam(api, "open_save", "name", "World");
        });
    }

    /// <summary>【Windows 源】<c>InstanceDetailPage.cs:243-261</c>:资源包 / 光影分段共用 <c>delete_&lt;kind&gt;</c>,删前要确认。</summary>
    [Fact]
    public void Instance_detail_resourcepack_and_shader_segments_delete_through_one_method_name()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_installed_resourcepacks", "[\"pack.zip\"]")
                .Route("get_installed_shaders", "[\"shader.zip\"]");
            var detail = C4Files.Mount(harness, new InstanceDetailPage(new InstanceInfo { Name = "甲" }, delegate { }));
            C4Files.Flush(2);

            detail.ShowSeg(2);
            // 【D1 修法 · A 组】等那一行**画出来**;删掉残留 Flush。断言一字不改
            C4Files.Pump(() => C4Files.HasText(detail, "pack.zip"), 9000);
            Assert.True(C4Files.HasText(detail, "pack.zip"));
            C4Files.Click(C4Files.ByText(detail, I18n.L("删除")));
            C4Files.AnswerDialog(I18n.L("删除"));
            C4Files.Pump(() => api.Count("delete_resourcepack") >= 1);
            C4Files.AssertParam(api, "delete_resourcepack", "filename", "pack.zip");

            detail.ShowSeg(3);
            // 【D1 修法 · A 组】等那一行**画出来**;删掉残留 Flush。断言一字不改
            C4Files.Pump(() => C4Files.HasText(detail, "shader.zip"), 9000);
            Assert.True(C4Files.HasText(detail, "shader.zip"));
            C4Files.Click(C4Files.ByText(detail, I18n.L("删除")));
            C4Files.AnswerDialog(I18n.L("删除"));
            C4Files.Pump(() => api.Count("delete_shader") >= 1);
            C4Files.AssertParam(api, "delete_shader", "filename", "shader.zip");
        });
    }

    /// <summary>【Windows 源】:分段空态「这里还是空的」+ 指路小字。</summary>
    [Fact]
    public void Instance_detail_segment_empty_state_matches_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            var detail = C4Files.Mount(harness, new InstanceDetailPage(new InstanceInfo { Name = "甲" }, delegate { }));
            C4Files.Flush(2);
            detail.ShowSeg(1);
            C4Files.Pump(() => api.Count("list_saves") >= 1);
            C4Files.Flush(2);
            Assert.True(C4Files.HasText(detail, I18n.L("这里还是空的")));
            Assert.True(C4Files.HasText(detail, I18n.L("到「下载」页搜索安装")));
        });
    }

    /// <summary>【Windows 源】<c>InstanceDetailPage.cs:49-52</c>:「返回」回调由调用方给(实例库用它把原内容换回来)。</summary>
    [Fact]
    public void Instance_detail_back_button_invokes_the_callback()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            int calls = 0;
            var detail = C4Files.Mount(harness, new InstanceDetailPage(new InstanceInfo { Name = "甲" }, delegate { calls++; }));
            C4Files.Flush(2);
            C4Files.Click(C4Files.ByText(detail, I18n.L("返回")));
            C4Files.Flush(2);
            Assert.Equal(1, calls);
        });
    }

    /// <summary>【Windows 源】<c>InstanceDetailPage.cs:60-66</c>:「启动」让启动页按这个实例启动(<c>LaunchNowAsync</c>)。</summary>
    [Fact]
    public void Instance_detail_launch_button_asks_the_launch_page_to_start()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var shell = ShellFixture.Create();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("get_instances", "[" + C4Files.InstanceJson("甲") + "]");
            // 详情页不在外壳的页面宿主里也能拿到外壳(Win 是 AppServices.Window 的 cast),
            // 所以这里单独挂一份,点「启动」走的正是 Windows 那条 Win.GetPage("launch") 的路。
            var detail = C4Files.Mount(harness, new InstanceDetailPage(new InstanceInfo { Name = "甲" }, delegate { }));
            C4Files.Flush(2);
            Assert.NotNull(shell.Window.GetPage("launch"));
            C4Files.Click(C4Files.ByText(detail, I18n.L("启动")));
            // LaunchNowAsync 会先把启动页加载起来(EnsureLoadedAsync → FillCardData),那是可观测的
            C4Files.Pump(() => api.Count("get_instances") >= 1);
            Assert.True(api.Count("get_instances") >= 1, "启动页应当被要求加载(否则「启动」没接上)");
        });
    }

    /// <summary>【Windows 源】:分段的资源行共用同一套行样式(最小高 46、内边距 18/4、B.RowLine 描边)。</summary>
    [Fact]
    public void Instance_detail_rows_use_the_windows_row_geometry()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("list_saves", "[{\"name\":\"World\"}]");
            var detail = C4Files.Mount(harness, new InstanceDetailPage(new InstanceInfo { Name = "甲" }, delegate { }));
            C4Files.Flush(2);
            detail.ShowSeg(1);
            C4Files.Pump(() => api.Count("list_saves") >= 1);
            C4Files.Flush(2);
            Border row = C4Files.Descendants(detail).OfType<Border>()
                .First(b => b.Child is Grid g && g.Children.OfType<SPanel>().Any());
            Assert.Equal(46.0, row.MinHeight);
            Assert.Equal(18.0, row.Padding.Left);
            Assert.Equal(4.0, row.Padding.Top);
        });
    }

    // ---------------------------------------------------------------- 小工具

    /// <summary>卡片列表里第一张卡(实例卡是 <c>_list</c> 的 Border 子项)。</summary>
    private static Border? FirstCard(Control page)
    {
        StackPanel? list = C4Files.Descendants(page).OfType<StackPanel>()
            .FirstOrDefault(sp => sp.Children.OfType<Border>().Any(b => b.BorderThickness.Left == 1.0));
        return list?.Children.OfType<Border>().FirstOrDefault();
    }

    /// <summary>空态组件的那个 SPanel(按它里面的标题文案反查最近的一个 SPanel 祖先)。</summary>
    private static SPanel EmptyPanel(Control page, string title)
    {
        TextBlock hit = C4Files.TextsWith(page, title).First();
        return hit.GetLogicalAncestors().OfType<SPanel>().First();
    }

    /// <summary>树上有这条文案、而且它是**可见**的(Windows 那边改的是 Visibility)。</summary>
    private static bool HasVisibleText(Control root, string text)
    {
        return C4Files.TextsWith(root, text).Any(t => t.IsVisible);
    }

    /// <summary>
    /// 「设置」分段的内容宿主(<c>InstanceDetailPage._host</c>)。
    /// **不能**用 <c>FirstOrDefault&lt;ContentControl&gt;()</c>:<c>Button</c> 本身就是 <c>ContentControl</c>,
    /// 页面里第一个 ContentControl 其实是「返回」那个图标钮(本包实测踩到)。所以这里排掉按钮。
    /// </summary>
    private static ContentControl SegmentHost(Control page)
    {
        ContentControl? hit = C4Files.Descendants(page).OfType<ContentControl>().FirstOrDefault(c => c is not Button);
        Assert.True(hit != null, "页面里找不到分段内容宿主");
        return hit!;
    }
}
