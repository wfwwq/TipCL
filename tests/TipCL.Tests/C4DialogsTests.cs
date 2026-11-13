using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using TipCL.Models;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C4(一):两个管理对话框(<see cref="ModsDialog"/> / <see cref="SavesDialog"/>)+ 它们依赖的那个小对话框
/// (<see cref="GlobalModsDialog"/>)。
///
/// 每条碰 Avalonia 的用例体都走 <c>AvaloniaSession.Run(...)</c>(D-79 / C1b-fix);全局静态由
/// <see cref="C4Scope"/> 快照还原(D-69);后端用**按方法名路由**的回环假后端
/// <see cref="C3cFakeApi"/>(不起真后端、不出网、不启外部进程)。每条期望的来源见注释。
/// </summary>
public class C4DialogsTests
{
    // ================================================================ ModsDialog

    /// <summary>【Windows 源】<c>ModsDialog.cs:86</c>:标题是「模组管理 · &lt;实例&gt;」,宽度 800。</summary>
    [Fact]
    public void Mods_dialog_pushes_the_windows_titled_panel()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            Task show = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            Assert.Equal(800.0, ((Border)panel).MaxWidth);
            Assert.True(C4Files.HasText(panel, I18n.L("目标")));
            Assert.True(C4Files.HasText(panel, I18n.L("共 {0} 个模组", 0)));
            Assert.Contains(C4Files.Texts(panel), t => t.Classes.Contains("t-h2"));
        });
    }

    /// <summary>
    /// 【Windows 源】<c>ModsDialog.cs:156-177</c>:目标表来自 <c>get_mods_targets</c>(<c>instance</c> 参数),
    /// 传进来的 <c>version</c> 决定选中项;列表来自 <c>get_installed_mod_entries</c>(<c>instance</c> + <c>version</c>)。
    /// </summary>
    [Fact]
    public void Mods_dialog_loads_the_windows_target_table_and_selects_the_given_version()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_mods_targets", "[{\"label\":\"实例共享 mods 目录\",\"value\":\"\"},{\"label\":\"1.20.1\",\"value\":\"1.20.1\"}]")
                .Route("get_installed_mod_entries", "[]");
            Task show = ModsDialog.ShowAsync("默认", "1.20.1");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            ComboBox target = C4Files.Find<ComboBox>(panel, "目标下拉");
            Assert.Equal(2, target.Items.Count);
            Assert.Equal("实例共享 mods 目录", target.Items[0]);
            // version = "1.20.1" 命中第 2 项 ⇒ SelectedIndex 1(Windows 用 Math.Max(0, FindIndex))
            Assert.Equal(1, target.SelectedIndex);
            C4Files.AssertParam(api, "get_mods_targets", "instance", "默认");
            Assert.Contains("\"version\":\"1.20.1\"", api.ParamsOf("get_installed_mod_entries"));
        });
    }

    /// <summary>【Windows 源】<c>ModsDialog.cs:159-166</c>:目标表为空时兜一个「实例共享 mods 目录」。</summary>
    [Fact]
    public void Mods_dialog_falls_back_to_one_shared_target_when_the_backend_returns_none()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("get_mods_targets", "[]");
            Task show = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            ComboBox target = C4Files.Find<ComboBox>(panel, "目标下拉");
            Assert.Single(target.Items);
            Assert.Equal(I18n.L("实例共享 mods 目录"), target.Items[0]);
            Assert.Equal(0, target.SelectedIndex);
            // 兜底项的 Value 是空串 ⇒ 拉列表时 version 也是空串
            Assert.Contains("\"version\":\"\"", api.ParamsOf("get_installed_mod_entries"));
        });
    }

    /// <summary>
    /// 【Windows 源】<c>ModsDialog.cs:111-153</c>:每行 = 开关(初值 = <c>item.Enabled</c>)+ 名称或文件名 + 文件名小字
    /// + 「删除」;名称空白时回落到文件名。
    /// </summary>
    [Fact]
    public void Mods_dialog_rows_show_switch_name_file_and_delete()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_installed_mod_entries", "[{\"filename\":\"sodium.jar\",\"name\":\"Sodium\",\"enabled\":true}," +
                                                     "{\"filename\":\"weird.jar\",\"name\":\"\",\"enabled\":false}]");
            Task show = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            List<CheckBox> boxes = C4Files.Descendants(panel).OfType<CheckBox>().ToList();
            Assert.Equal(2, boxes.Count);
            Assert.True(boxes[0].IsChecked == true);
            Assert.True(boxes[1].IsChecked == false);
            Assert.All(boxes, b => Assert.Contains("switch", b.Classes));
            Assert.True(C4Files.HasText(panel, "Sodium"));
            Assert.True(C4Files.HasText(panel, "sodium.jar"));
            // name 为空 ⇒ 显示文件名
            Assert.True(C4Files.HasText(panel, "weird.jar"));
            Assert.Equal(2, C4Files.Descendants(panel).OfType<Button>().Count(b => string.Equals(b.Content as string, I18n.L("删除"), StringComparison.Ordinal)));
        });
    }

    /// <summary>【Windows 源】<c>ModsDialog.cs:106</c>:计数是「共 N 个模组」。</summary>
    [Fact]
    public void Mods_dialog_count_uses_the_windows_format()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("get_installed_mod_entries", "[{\"filename\":\"a.jar\"},{\"filename\":\"b.jar\"},{\"filename\":\"c.jar\"}]");
            Task show = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            Assert.True(C4Files.HasText(panel, I18n.L("共 {0} 个模组", 3)));
        });
    }

    /// <summary>【Windows 源】<c>ModsDialog.cs:109</c>:空列表给「还没有模组」+ 那句指路的小字。</summary>
    [Fact]
    public void Mods_dialog_empty_list_shows_the_windows_empty_state()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            Task show = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            Assert.True(C4Files.HasText(panel, I18n.L("还没有模组")));
            Assert.True(C4Files.HasText(panel, I18n.L("到 Mod 页搜索安装,或直接导入 jar")));
        });
    }

    /// <summary>【Windows 源】<c>ModsDialog.cs:120-131</c>:开关打开调 <c>enable_mod</c>、关掉调 <c>disable_mod</c>,
    /// 参数是 <c>instance</c> / <c>filename</c> / <c>version</c>(version = 当前目标)。</summary>
    [Fact]
    public void Mods_dialog_switch_calls_enable_and_disable_with_instance_filename_and_version()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_mods_targets", "[{\"label\":\"隔离 1.20.1\",\"value\":\"1.20.1\"}]")
                .Route("get_installed_mod_entries", "[{\"filename\":\"sodium.jar\",\"enabled\":true}]");
            Task show = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            CheckBox box = C4Files.Descendants(panel).OfType<CheckBox>().First();
            box.IsChecked = false;
            C4Files.Pump(() => api.Count("disable_mod") >= 1);
            C4Files.AssertParam(api, "disable_mod", "instance", "默认");
            Assert.Contains("\"filename\":\"sodium.jar\"", api.ParamsOf("disable_mod"));
            Assert.Contains("\"version\":\"1.20.1\"", api.ParamsOf("disable_mod"));
            box.IsChecked = true;
            C4Files.Pump(() => api.Count("enable_mod") >= 1);
            Assert.Contains("\"version\":\"1.20.1\"", api.ParamsOf("enable_mod"));
        });
    }

    /// <summary>
    /// 【Windows 源】<c>ModsDialog.cs:136-151</c>:点「删除」先弹确认卡,确认后才 <c>delete_mod</c> 并重拉列表。
    /// 驱动方式照 D-113:先泵出卡片再点按钮。
    /// </summary>
    [Fact]
    public void Mods_dialog_delete_confirms_then_calls_delete_mod_and_reloads()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_installed_mod_entries", "[{\"filename\":\"sodium.jar\"}]");
            Task show = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            C4Files.Click(C4Files.ByText(panel, I18n.L("删除")));
            C4Files.AnswerDialog(I18n.L("删除"));
            C4Files.Pump(() => api.Count("delete_mod") >= 1);
            Assert.Contains("\"filename\":\"sodium.jar\"", api.ParamsOf("delete_mod"));
            // 删完会再拉一次列表(第一次是首屏共 1 次)
            C4Files.Pump(() => api.Count("get_installed_mod_entries") >= 2);
        });
    }

    /// <summary>【Windows 源】取消确认 ⇒ 一个字节都不写后端。</summary>
    [Fact]
    public void Mods_dialog_delete_cancelled_writes_nothing()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("get_installed_mod_entries", "[{\"filename\":\"sodium.jar\"}]");
            Task show = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            C4Files.Click(C4Files.ByText(panel, I18n.L("删除")));
            C4Files.AnswerDialog(I18n.L("取消"));
            C4Files.Settle(4);
            Assert.Equal(0, api.Count("delete_mod"));
        });
    }

    /// <summary>【Windows 源】<c>ModsDialog.cs:64-74</c>:「打开目录」调 <c>open_mods_folder</c>(带当前版本)并 Toast「已打开」。</summary>
    [Fact]
    public void Mods_dialog_open_folder_calls_open_mods_folder_with_the_current_version()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_mods_targets", "[{\"label\":\"实例共享 mods 目录\",\"value\":\"\"}]")
                .Route("open_mods_folder", "\"/tmp/mods\"");
            Task show = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            C4Files.Click(C4Files.ByText(panel, I18n.L("打开目录")));
            C4Files.Pump(() => api.Count("open_mods_folder") >= 1);
            Assert.Contains("\"version\":\"\"", api.ParamsOf("open_mods_folder"));
            C4Files.Pump(() => toasts.Has(I18n.L("已打开")));
        });
    }

    /// <summary>
    /// 【本机实测】离屏/测试里 <c>Dlg.PickFiles</c> 没有桌面窗口 ⇒ 返回 null(C2a 的既有结论)。
    /// 页面必须**什么都不做**(不假装导入了),而不是抛或写后端。
    /// </summary>
    [Fact]
    public void Mods_dialog_import_jar_degrades_to_nothing_without_a_desktop_picker()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("get_installed_mod_entries", "[]");
            Task show = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            C4Files.Click(C4Files.ByText(panel, I18n.L("导入 jar")));
            C4Files.Settle(4);
            Assert.Equal(0, api.Count("install_mod"));
            Assert.False(toasts.Has(I18n.L("已加入队列")));
        });
    }

    /// <summary>【Windows 源】<c>ModsDialog.cs:29-31</c>:换目标下拉会按新版本重拉列表(<c>sync</c> 标志不挡用户操作)。</summary>
    [Fact]
    public void Mods_dialog_switching_target_reloads_with_the_new_version()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_mods_targets", "[{\"label\":\"实例共享 mods 目录\",\"value\":\"\"},{\"label\":\"隔离版本\",\"value\":\"1.20.1\"}]");
            Task show = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            ComboBox target = C4Files.Find<ComboBox>(panel, "目标下拉");
            target.SelectedIndex = 1;
            C4Files.Pump(() => api.Count("get_installed_mod_entries") >= 2);
            Assert.Contains("\"version\":\"1.20.1\"", api.ParamsOf("get_installed_mod_entries", 1));
        });
    }

    /// <summary>【Windows 源】「刷新」就是再跑一次 Reload(不重拉目标表)。</summary>
    [Fact]
    public void Mods_dialog_refresh_button_reloads_the_list_only()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            Task show = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            C4Files.Click(C4Files.ByText(panel, I18n.L("刷新")));
            C4Files.Pump(() => api.Count("get_installed_mod_entries") >= 2);
            Assert.Equal(1, api.Count("get_mods_targets"));
        });
    }

    /// <summary>
    /// 【Windows 源】<c>ModsDialog.cs:19-20</c>:主列表是 <c>Ui.Scroll</c> + <c>BindWindowMaxHeight(0.5, 220)</c>
    /// + <c>MinWidth = 620</c>。
    ///
    /// 【本机实测】离屏没有 <c>IClassicDesktopStyleApplicationLifetime</c>,所以公开入口那次绑定**直接返回**
    /// (C1c 的既定行为)—— 这里据实断言两件事:①<c>MinWidth</c> 真的落了 620;
    /// ②用可注入宿主的那个重载在**这个页面自己的 ScrollViewer** 上绑一次,得到 max(floor, host×pct)。
    /// </summary>
    [Fact]
    public void Mods_dialog_list_scroll_bounds_follow_the_windows_constants()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            Task show = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            SmoothScroll scroll = C4Files.Descendants(panel).OfType<SmoothScroll>().First();
            Assert.Equal(620.0, scroll.MinWidth);
            Assert.True(double.IsPositiveInfinity(scroll.MaxHeight), "离屏没有宿主窗口 ⇒ 公开入口不绑 MaxHeight");
            // 界面/宿主都在:用同一个重载绑一次,值必须是 max(220, 高×0.5)(钉住 0.5 / 220 这两个常量)
            scroll.BindWindowMaxHeight(harness.Window, 0.5, 220.0);
            C4Files.Flush(2);
            double height = harness.Window.Bounds.Height;
            Assert.True(height > 0.0, "宿主窗口必须有实际高度,否则这条用例没有意义");
            Assert.Equal(Math.Max(220.0, height * 0.5), scroll.MaxHeight, 3);
        });
    }

    /// <summary>
    /// 【Windows 源】<c>ModsDialog.cs:154</c>:每行最后都 <c>Motion.Stagger</c>。
    /// 【任务书】关掉动画时一切都立刻到位(离屏 <c>Motion.Enabled</c> 恒假,见 D-69 / D-129)。
    /// </summary>
    [Fact]
    public void Mods_dialog_lands_immediately_with_motion_disabled()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            Motion.Enabled = false;
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("get_installed_mod_entries", "[{\"filename\":\"a.jar\"},{\"filename\":\"b.jar\"}]");
            Task show = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            // 【Windows 源】<c>:152</c>:行本身是 Ui.RowCard(圆角 8 / 描边 1 / 内边距 10)
            SPanel list = (SPanel)C4Files.Descendants(panel).OfType<SmoothScroll>().First().Content!;
            List<Border> rows = list.Children.OfType<Border>().ToList();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, b =>
            {
                Assert.Equal(10.0, b.Padding.Left);
                Assert.Equal(8.0, b.CornerRadius.TopLeft);
            });
            C4Files.Settle(2);
            Assert.All(rows, b =>
            {
                Assert.Equal(1.0, b.Opacity);
                Assert.Null(b.RenderTransform);
            });
        });
    }

    /// <summary>
    /// 【Windows 源】列表里字段缺失 / null 时不许抛(后端给脏数据也要能开出来)。
    /// 【任务书】"mods/saves 列表要能处理空与畸形输入而不抛"。
    /// </summary>
    [Fact]
    public void Mods_dialog_survives_malformed_rows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("get_mods_targets", "[{}]")
                .Route("get_installed_mod_entries", "[{},{\"filename\":null,\"name\":null,\"enabled\":true}]");
            Task show = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            Assert.True(C4Files.HasText(panel, I18n.L("共 {0} 个模组", 2)));
            // 目标表里那一条也是空对象(Label / Value 都回落成空串),下拉必须仍有 1 项
            Assert.Single(C4Files.Find<ComboBox>(panel, "目标下拉").Items);
        });
    }

    /// <summary>【Windows 源】<c>ModsDialog.cs:75-81</c>:「全局模组」按钮打开 <see cref="GlobalModsDialog"/>。</summary>
    [Fact]
    public void Mods_dialog_global_mods_button_opens_the_global_dialog()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            Task show = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            C4Files.Click(C4Files.ByText(panel, I18n.L("全局模组")));
            Control global = C4Files.PanelCard(I18n.L("全局模组"));
            Assert.NotNull(global);
            // 两个面板同时在(ModsDialog 那张 + 全局模组那张)
            Assert.Equal(2, C4Files.LayerCount());
        });
    }

    // ================================================================ CheckUpdatesAsync

    /// <summary>【Windows 源】<c>ModsDialog.cs:188-192</c>:没有可更新项 ⇒ Toast「都是最新的」+ 正文。</summary>
    [Fact]
    public void Check_updates_toasts_the_windows_all_current_message()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("check_mod_updates", "[]");
            Task task = ModsDialog.CheckUpdatesAsync("默认");
            C4Files.PumpTask(task);
            C4Files.AssertParam(api, "check_mod_updates", "instance", "默认");
            Assert.True(toasts.Has(I18n.L("都是最新的")));
            Assert.Contains(toasts.Items, t => t.Body == I18n.L("没有找到可更新的模组") && t.Kind == ToastKind.Success);
            // Busy 层用完必须收掉
            Assert.Equal(0, C4Files.LayerCount());
        });
    }

    /// <summary>【Windows 源】<c>ModsDialog.cs:197-208</c>:更新卡每行 = 默认勾选的框 + 名称/文件名 + 版本行 + MC 标签。</summary>
    [Fact]
    public void Check_updates_builds_the_windows_pick_card()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("check_mod_updates",
                "[{\"filename\":\"a.jar\",\"name\":\"Alpha\",\"current\":\"1.0\",\"latest\":\"2.0\",\"source\":\"modrinth\",\"size\":2048,\"mc_version\":\"1.20.1\"}," +
                "{\"filename\":\"b.jar\",\"name\":\"\",\"current\":\"1.0\",\"latest\":\"2.0\",\"source\":\"curseforge\",\"size\":1024,\"mc_version\":\"\"}]");
            Task task = ModsDialog.CheckUpdatesAsync("默认");
            C3bFiles.WaitDialog();
            Assert.False(task.IsCompleted, "更新卡还没点按钮,任务不该结束");
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            Assert.Equal(I18n.L("{0} 个模组有更新", 2), card.Title);
            List<CheckBox> boxes = C4Files.Descendants(card).OfType<CheckBox>().ToList();
            Assert.Equal(2, boxes.Count);
            Assert.All(boxes, b => Assert.True(b.IsChecked == true));
            Assert.True(C4Files.HasText(card, "Alpha"));
            Assert.True(C4Files.HasText(card, "1.0 → 2.0  ·  modrinth  ·  " + Fmt.Size(2048)));
            // mc_version 为空 ⇒ 用「—」占位
            Assert.True(C4Files.HasText(card, "—"));
            C4Files.Click(C4Files.ByText(card, I18n.L("更新所选")));
            C4Files.PumpTask(task);
            Assert.Equal(2, api.Count("apply_mod_update"));
        });
    }

    /// <summary>【Windows 源】<c>ModsDialog.cs:212-218</c>:只更新勾上的那些。</summary>
    [Fact]
    public void Check_updates_applies_only_the_checked_rows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("check_mod_updates",
                "[{\"filename\":\"a.jar\"},{\"filename\":\"b.jar\"}]");
            Task task = ModsDialog.CheckUpdatesAsync("默认");
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            List<CheckBox> boxes = C4Files.Descendants(card).OfType<CheckBox>().ToList();
            boxes[1].IsChecked = false;
            C4Files.Click(C4Files.ByText(card, I18n.L("更新所选")));
            C4Files.PumpTask(task);
            Assert.Equal(1, api.Count("apply_mod_update"));
            Assert.Contains("\"filename\":\"a.jar\"", api.ParamsOf("apply_mod_update"));
            Assert.True(toasts.Has(I18n.L("已更新")));
        });
    }

    /// <summary>【Windows 源】全部取消勾选 ⇒ 什么都不做(也不 Toast 成功)。</summary>
    [Fact]
    public void Check_updates_with_nothing_checked_writes_nothing()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("check_mod_updates", "[{\"filename\":\"a.jar\"}]");
            Task task = ModsDialog.CheckUpdatesAsync("默认");
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            C4Files.Descendants(card).OfType<CheckBox>().First().IsChecked = false;
            C4Files.Click(C4Files.ByText(card, I18n.L("更新所选")));
            C4Files.PumpTask(task);
            Assert.Equal(0, api.Count("apply_mod_update"));
            Assert.False(toasts.Has(I18n.L("已更新")));
        });
    }

    /// <summary>【Windows 源】取消更新卡(点「取消」)⇒ 什么都不做。</summary>
    [Fact]
    public void Check_updates_cancelled_writes_nothing()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("check_mod_updates", "[{\"filename\":\"a.jar\"}]");
            Task task = ModsDialog.CheckUpdatesAsync("默认");
            C3bFiles.WaitDialog();
            C4Files.AnswerDialog(I18n.L("取消"));
            C4Files.PumpTask(task);
            Assert.Equal(0, api.Count("apply_mod_update"));
        });
    }

    /// <summary>
    /// 【Windows 源】<c>ModsDialog.cs:219-243</c>:逐个更新,失败收集成「部分模组没能更新」的提示框,
    /// 正文是「成功 X 个,失败 Y 个:\n\n文件名:原因」。
    /// </summary>
    [Fact]
    public void Check_updates_reports_failures_in_the_windows_alert()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("check_mod_updates", "[{\"filename\":\"a.jar\"},{\"filename\":\"b.jar\"}]")
                .RouteError("apply_mod_update", "下载失败");
            Task task = ModsDialog.CheckUpdatesAsync("默认");
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            C4Files.Click(C4Files.ByText(card, I18n.L("更新所选")));
            // 【D-120 纪律】Alert 的那个任务要等卡片关掉才结束,所以**不能** PumpTask(它会死等到会话超时):
            // 这里泵到失败提示卡出现就断言,最后再点关闭把任务收掉。
            Control alertCard = null!;
            C4Files.Pump(() =>
            {
                alertCard = C4Files.Descendants(Dlg.Host!).OfType<DialogCard>()
                    .LastOrDefault(c => c.Title == I18n.L("部分模组没能更新"))!;
                return alertCard != null;
            });
            DialogCard alert = (DialogCard)alertCard;
            Assert.Equal(I18n.L("部分模组没能更新"), alert.Title);
            // Alert 的正文是一个只读 TextBox(C2a 的既有形状),所以按 TextBox.Text 找
            Assert.Contains(C4Files.Descendants(alert).OfType<TextBox>(),
                t => (t.Text ?? "").Contains("a.jar", StringComparison.Ordinal));
            Assert.False(toasts.Has(I18n.L("已更新")));
            // 关掉提示卡,任务才收尾(不留挂着的 await)。Alert 的唯一按钮是「知道了」(C2a 的既有文案)。
            C4Files.Click(C4Files.ByText(alert, I18n.L("知道了")));
            C4Files.PumpTask(task);
        });
    }

    // ================================================================ GlobalModsDialog

    /// <summary>【Windows 源】<c>GlobalModsDialog.cs:18-26</c>:两个按钮 + 380/520 的滚动区 + 620 宽卡片。</summary>
    [Fact]
    public void Global_mods_dialog_builds_the_windows_shell()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            Task task = GlobalModsDialog.ShowAsync();
            C4Files.PumpTask(task);
            Control panel = C4Files.PanelCard(I18n.L("全局模组"));
            Assert.Equal(620.0, ((Border)panel).MaxWidth);
            Assert.True(C4Files.HasButton(panel, I18n.L("打开目录")));
            Assert.True(C4Files.HasButton(panel, I18n.L("刷新")));
            SmoothScroll scroll = C4Files.Descendants(panel).OfType<SmoothScroll>().First();
            Assert.Equal(520.0, scroll.MinWidth);
            Assert.Equal(380.0, scroll.MaxHeight);
        });
    }

    /// <summary>【Windows 源】<c>GlobalModsDialog.cs:31-35</c>:空目录的空态文案。</summary>
    [Fact]
    public void Global_mods_dialog_shows_the_windows_empty_state()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            Task task = GlobalModsDialog.ShowAsync();
            C4Files.PumpTask(task);
            Control panel = C4Files.PanelCard(I18n.L("全局模组"));
            Assert.True(C4Files.HasText(panel, I18n.L("全局模组目录是空的")));
            Assert.True(C4Files.HasText(panel, I18n.L("放进去的模组会自动注入每个实例")));
        });
    }

    /// <summary>【Windows 源】<c>GlobalModsDialog.cs:36-60</c>:一行 = 开关 + 文件名 + 大小;开关写 <c>set_global_mod_enabled</c>。</summary>
    [Fact]
    public void Global_mods_dialog_rows_toggle_through_set_global_mod_enabled()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("list_global_mods", "[{\"filename\":\"g.jar\",\"enabled\":true,\"size\":\"12 KB\"}]");
            Task task = GlobalModsDialog.ShowAsync();
            C4Files.PumpTask(task);
            Control panel = C4Files.PanelCard(I18n.L("全局模组"));
            Assert.True(C4Files.HasText(panel, "g.jar"));
            Assert.True(C4Files.HasText(panel, "12 KB"));
            CheckBox box = C4Files.Descendants(panel).OfType<CheckBox>().First();
            Assert.Contains("switch", box.Classes);
            Assert.True(box.IsChecked == true);
            box.IsChecked = false;
            C4Files.Pump(() => api.Count("set_global_mod_enabled") >= 1);
            Assert.Contains("\"enabled\":false", api.ParamsOf("set_global_mod_enabled"));
            box.IsChecked = true;
            C4Files.Pump(() => api.Count("set_global_mod_enabled") >= 2);
            Assert.Contains("\"enabled\":true", api.ParamsOf("set_global_mod_enabled", 1));
        });
    }

    // ================================================================ SavesDialog

    /// <summary>【Windows 源】<c>SavesDialog.cs:43</c>:标题是「存档管理 · &lt;实例&gt; / &lt;版本&gt;」,宽度 780。</summary>
    [Fact]
    public void Saves_dialog_pushes_the_windows_titled_panel()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            Task show = SavesDialog.ShowAsync("默认", "1.20.1");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认") + " / 1.20.1");
            Assert.Equal(780.0, ((Border)panel).MaxWidth);
            Assert.True(C4Files.HasText(panel, I18n.L("截图 / 日志")));
        });
    }

    /// <summary>【Windows 源】版本为空串时标题**不**拼 " / " 那一段。</summary>
    [Fact]
    public void Saves_dialog_title_omits_the_version_suffix_when_empty()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            Task show = SavesDialog.ShowAsync("默认", "");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认"));
            Assert.Contains(C4Files.Texts(panel), t => t.Text == I18n.L("存档管理 · {0}", "默认"));
        });
    }

    /// <summary>
    /// 【Windows 源】<c>SavesDialog.cs:55-98</c>:每行 = 名称 + 「大小 · 上次游玩 · 模式」+ 备份/导出/打开/装数据包/删除
    /// (五个动作,顺序如此)。
    /// </summary>
    [Fact]
    public void Saves_dialog_rows_show_the_windows_sub_line_and_five_actions()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("list_saves",
                "[{\"name\":\"World\",\"size\":\"2 MB\",\"last_played\":\"今天\",\"mode\":\"生存\"}]");
            Task show = SavesDialog.ShowAsync("默认", "1.20.1");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认") + " / 1.20.1");
            Assert.True(C4Files.HasText(panel, "World"));
            Assert.True(C4Files.HasText(panel, "2 MB · 今天 · 生存"));
            Assert.True(C4Files.HasButton(panel, I18n.L("备份")));
            Assert.True(C4Files.HasButton(panel, I18n.L("导出")));
            Assert.True(C4Files.HasButton(panel, I18n.L("打开")));
            Assert.True(C4Files.HasButton(panel, I18n.L("装数据包")));
            Assert.True(C4Files.HasButton(panel, I18n.L("删除")));
            Assert.Contains("\"version\":\"1.20.1\"", api.ParamsOf("list_saves"));
        });
    }

    /// <summary>【Windows 源】<c>SavesDialog.cs:53</c>:没有存档时的空态文案。</summary>
    [Fact]
    public void Saves_dialog_empty_state_matches_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            Task show = SavesDialog.ShowAsync("默认", "");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认"));
            Assert.True(C4Files.HasText(panel, I18n.L("这个版本还没有存档")));
            Assert.True(C4Files.HasText(panel, I18n.L("进游戏创建世界后会出现在这里")));
        });
    }

    /// <summary>【Windows 源】：备份栏的标题是「备份(N)」,且**只列前 20 条**(<c>Take(20)</c>)。</summary>
    [Fact]
    public void Saves_dialog_lists_backups_but_caps_at_twenty()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            string many = "[" + string.Join(",", Enumerable.Range(1, 25).Select(i =>
                "{\"name\":\"bk" + i + "\",\"size\":\"1 MB\",\"date\":\"2026-11-0" + (i % 9 + 1) + "\"}")) + "]";
            using var api = new C3cFakeApi().Route("list_save_backups", many);
            Task show = SavesDialog.ShowAsync("默认", "");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认"));
            Assert.True(C4Files.HasText(panel, I18n.L("备份({0})", 25)));
            Assert.True(C4Files.HasText(panel, "bk20"));
            Assert.False(C4Files.HasText(panel, "bk21"));
        });
    }

    /// <summary>【Windows 源】<c>SavesDialog.cs:157</c>:媒体栏**只列前 60 条**(<c>Take(60)</c>),空目录给「这个目录还是空的」。</summary>
    [Fact]
    public void Saves_dialog_media_bar_caps_at_sixty_and_has_its_own_empty_line()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            string many = "[" + string.Join(",", Enumerable.Range(1, 70).Select(i =>
                "{\"name\":\"shot" + i + ".png\",\"path\":\"/tmp/s" + i + ".png\",\"bytes\":" + i * 1024 + "}")) + "]";
            using var api = new C3cFakeApi().Route("list_media", many);
            Task show = SavesDialog.ShowAsync("默认", "");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认"));
            Assert.True(C4Files.HasText(panel, I18n.L("{0} 个文件", 70)));
            Assert.True(C4Files.HasText(panel, "shot60.png"));
            Assert.False(C4Files.HasText(panel, "shot61.png"));
        });
    }

    /// <summary>【Windows 源】<c>SavesDialog.cs:154</c>:媒体栏没有文件时给一句灰字。</summary>
    [Fact]
    public void Saves_dialog_media_empty_line_matches_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            Task show = SavesDialog.ShowAsync("默认", "");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认"));
            Assert.True(C4Files.HasText(panel, I18n.L("这个目录还是空的")));
            Assert.True(C4Files.HasText(panel, I18n.L("{0} 个文件", 0)));
        });
    }

    /// <summary>【Windows 源】<c>SavesDialog.cs:144</c>:三个下拉文案映射到三个后端 kind 键。</summary>
    [Theory]
    [InlineData("截图", "screenshots")]
    [InlineData("崩溃报告", "crash-reports")]
    [InlineData("日志", "logs")]
    public void Saves_dialog_media_kind_maps_like_windows(string label, string kind)
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            Task show = SavesDialog.ShowAsync("默认", "1.20.1");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认") + " / 1.20.1");
            ComboBox combo = C4Files.Find<ComboBox>(panel, "媒体类型下拉");
            int at = combo.Items.Cast<object>().ToList().FindIndex(o => string.Equals(o as string, I18n.L(label), StringComparison.Ordinal));
            Assert.True(at >= 0, "下拉里没有「" + label + "」这一项");
            // 【本机实测】Ui.Combo 默认会选中第 0 项,所以「切到截图」这一支不会触发 SelectionChanged
            // (别的两支会)。为了三条数据都能真的走两次切换,先切到**一定不等于当前项**的一项再切目标项。
            int count = combo.Items.Count;
            int away = (((at + 1) % count) == combo.SelectedIndex) ? ((at + 2) % count) : ((at + 1) % count);
            Assert.NotEqual(at, away);
            combo.SelectedIndex = away;
            C4Files.Pump(() => api.Count("list_media") >= 2);
            combo.SelectedIndex = at;
            C4Files.Pump(() => api.Count("list_media") >= 3);
            C4Files.AssertParam(api, "list_media", "kind", kind, api.Count("list_media") - 1);
        });
    }

    /// <summary>【Windows 源】<c>SavesDialog.cs:59-65</c>:「备份」起 <c>backup_save</c> 任务(带 name/version)并 Toast。</summary>
    [Fact]
    public void Saves_dialog_backup_starts_the_task_and_toasts()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("list_saves", "[{\"name\":\"World\"}]").Route("backup_save", "\"task-1\"");
            Task show = SavesDialog.ShowAsync("默认", "1.20.1");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认") + " / 1.20.1");
            C4Files.Click(C4Files.ByText(panel, I18n.L("备份")));
            C4Files.Pump(() => api.Count("backup_save") >= 1);
            Assert.Contains("\"name\":\"World\"", api.ParamsOf("backup_save"));
            Assert.Contains("\"version\":\"1.20.1\"", api.ParamsOf("backup_save"));
            C4Files.Pump(() => toasts.Has(I18n.L("已开始备份")));
        });
    }

    /// <summary>【Windows 源】<c>SavesDialog.cs:83-93</c>:删除存档要确认,确认后 <c>delete_save</c> 并重拉列表。</summary>
    [Fact]
    public void Saves_dialog_delete_confirms_then_calls_delete_save()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("list_saves", "[{\"name\":\"World\"}]");
            Task show = SavesDialog.ShowAsync("默认", "1.20.1");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认") + " / 1.20.1");
            C4Files.Click(C4Files.ByText(panel, I18n.L("删除")));
            C4Files.AnswerDialog(I18n.L("永久删除"));
            C4Files.Pump(() => api.Count("delete_save") >= 1);
            Assert.Contains("\"name\":\"World\"", api.ParamsOf("delete_save"));
            C4Files.Pump(() => api.Count("list_saves") >= 2);
        });
    }

    /// <summary>【Windows 源】<c>SavesDialog.cs:107-123</c>:还原备份带 <c>overwrite: true</c>,确认按钮是「还原」。</summary>
    [Fact]
    public void Saves_dialog_restore_backup_sends_overwrite_true()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("list_save_backups", "[{\"name\":\"bk1\",\"size\":\"1 MB\",\"date\":\"2026-11-01\"}]");
            Task show = SavesDialog.ShowAsync("默认", "1.20.1");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认") + " / 1.20.1");
            C4Files.Click(C4Files.ByText(panel, I18n.L("还原")));
            C4Files.AnswerDialog(I18n.L("还原"));
            C4Files.Pump(() => api.Count("restore_save_backup") >= 1);
            string args = api.ParamsOf("restore_save_backup");
            Assert.Contains("\"backup_name\":\"bk1\"", args);
            Assert.Contains("\"overwrite\":true", args);
            Assert.Contains("\"version\":\"1.20.1\"", args);
            C4Files.Pump(() => toasts.Has(I18n.L("已还原")));
        });
    }

    /// <summary>【Windows 源】<c>SavesDialog.cs:124-135</c>:备份的「删除」**不**再确认。</summary>
    [Fact]
    public void Saves_dialog_delete_backup_writes_without_a_confirm()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("list_save_backups", "[{\"name\":\"bk1\"}]");
            Task show = SavesDialog.ShowAsync("默认", "");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认"));
            C4Files.Click(C4Files.ByText(panel, I18n.L("删除")));
            C4Files.Pump(() => api.Count("delete_save_backup") >= 1);
            Assert.Contains("\"backup_name\":\"bk1\"", api.ParamsOf("delete_save_backup"));
        });
    }

    /// <summary>【Windows 源】<c>SavesDialog.cs:77-79</c>:存档行的「打开」调 <c>open_save</c>(带 name)。</summary>
    [Fact]
    public void Saves_dialog_open_save_calls_open_save()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("list_saves", "[{\"name\":\"World\"}]");
            Task show = SavesDialog.ShowAsync("默认", "");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认"));
            C4Files.Click(C4Files.ByText(panel, I18n.L("打开")));
            C4Files.Pump(() => api.Count("open_save") >= 1);
            Assert.Contains("\"name\":\"World\"", api.ParamsOf("open_save"));
        });
    }

    /// <summary>【Windows 源】<c>SavesDialog.cs:162-165</c>:媒体行的「打开」调 <c>open_media</c>(只带 path)。</summary>
    [Fact]
    public void Saves_dialog_open_media_calls_open_media_with_the_path()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("list_media", "[{\"name\":\"shot.png\",\"path\":\"/tmp/shot.png\",\"bytes\":100}]");
            Task show = SavesDialog.ShowAsync("默认", "");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认"));
            C4Files.Click(C4Files.ByText(panel, I18n.L("打开")));
            C4Files.Pump(() => api.Count("open_media") >= 1);
            Assert.Contains("\"path\":\"/tmp/shot.png\"", api.ParamsOf("open_media"));
            Assert.True(C4Files.HasText(panel, Fmt.Size(100)));
        });
    }

    /// <summary>【本机实测】离屏没有桌面窗口 ⇒ <c>Dlg.SaveFile</c> 返回 null ⇒ 导出这条路什么都不做(不抛、不写盘)。</summary>
    [Fact]
    public void Saves_dialog_export_degrades_to_nothing_without_a_desktop_picker()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("list_saves", "[{\"name\":\"World\"}]");
            Task show = SavesDialog.ShowAsync("默认", "");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认"));
            C4Files.Click(C4Files.ByText(panel, I18n.L("导出")));
            C4Files.Settle(4);
            Assert.Equal(0, api.Count("export_save"));
            Assert.False(toasts.Has(I18n.L("已导出")));
        });
    }

    /// <summary>【Windows 源】<c>SavesDialog.cs:190-201</c>:目标表 1 项直接用、0 项给空串、多项才弹下拉。</summary>
    [Fact]
    public void Pick_save_target_short_circuits_for_zero_or_one_row()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_saves_targets", p => p.ToString().Contains("none", StringComparison.Ordinal) ? "[]" : "[{\"label\":\"唯一\",\"value\":\"only\"}]");
            Task<string?> single = SavesDialog.PickSaveTargetAsync("唯一实例", "装到哪");
            C4Files.PumpTask(single);
            Assert.Equal("only", single.Result);
            Task<string?> none = SavesDialog.PickSaveTargetAsync("none", "装到哪");
            C4Files.PumpTask(none);
            Assert.Equal("", none.Result);
            // 0/1 项都不该弹卡片
            Assert.Equal(0, C4Files.LayerCount());
        });
    }

    /// <summary>【Windows 源】<c>SavesDialog.cs:195-201</c>:多项时弹下拉,取消返回 null、选中返回该项的 Value。</summary>
    [Fact]
    public void Pick_save_target_asks_and_returns_the_picked_value_or_null_on_cancel()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("get_saves_targets",
                "[{\"label\":\"A\",\"value\":\"a\"},{\"label\":\"B\",\"value\":\"b\"}]");
            Task<string?> cancelled = SavesDialog.PickSaveTargetAsync("默认", "装到哪");
            C3bFiles.WaitDialog();
            C4Files.AnswerDialog(I18n.L("取消"));
            C4Files.PumpTask(cancelled);
            Assert.Null(cancelled.Result);

            Task<string?> picked = SavesDialog.PickSaveTargetAsync("默认", "装到哪");
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            ComboBox combo = C4Files.Find<ComboBox>(card, "存档目标下拉");
            combo.SelectedIndex = 1;
            C4Files.Click(C4Files.ByText(card, I18n.L("继续")));
            C4Files.PumpTask(picked);
            Assert.Equal("b", picked.Result);
        });
    }

    /// <summary>【Windows 源】<c>SavesDialog.cs:207-212</c>:一个数据包都没装过时只弹一句提示,不弹选择框。</summary>
    [Fact]
    public void Install_datapack_alerts_when_nothing_is_installed()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("list_saves", "[{\"name\":\"World\"}]");
            Task show = SavesDialog.ShowAsync("默认", "");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认"));
            C4Files.Click(C4Files.ByText(panel, I18n.L("装数据包")));
            C3bFiles.WaitDialog();
            DialogCard alert = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            Assert.Equal(I18n.L("没有数据包"), alert.Title);
            Assert.Equal(0, api.Count("install_datapack_into_save"));
        });
    }

    /// <summary>【Windows 源】<c>SavesDialog.cs:213-224</c>:选了数据包 ⇒ <c>install_datapack_into_save</c>(filename = 下拉值)+ Toast。</summary>
    [Fact]
    public void Install_datapack_installs_the_picked_pack()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("list_saves", "[{\"name\":\"World\"}]")
                .Route("get_installed_datapacks", "[\"pack1.zip\",\"pack2.zip\"]")
                .Route("install_datapack_into_save", "\"pack1.zip\"");
            Task show = SavesDialog.ShowAsync("默认", "1.20.1");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认") + " / 1.20.1");
            C4Files.Click(C4Files.ByText(panel, I18n.L("装数据包")));
            C3bFiles.WaitDialog();
            DialogCard ask = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            C4Files.Click(C4Files.ByText(ask, I18n.L("安装")));
            C4Files.Pump(() => api.Count("install_datapack_into_save") >= 1);
            string args = api.ParamsOf("install_datapack_into_save");
            Assert.Contains("\"filename\":\"pack1.zip\"", args);
            Assert.Contains("\"save_name\":\"World\"", args);
            Assert.Contains("\"version\":\"1.20.1\"", args);
            C4Files.Pump(() => toasts.Has(I18n.L("已安装")));
        });
    }

    /// <summary>【任务书】列表里出现畸形行(缺 name / null)时不许抛。</summary>
    [Fact]
    public void Saves_dialog_survives_malformed_rows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("list_saves", "[{},{\"name\":null}]")
                .Route("list_save_backups", "[{},{}]")
                .Route("list_media", "[{}]");
            Task show = SavesDialog.ShowAsync("默认", "");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认"));
            Assert.Equal(2, C4Files.Descendants(panel).OfType<Button>().Count(b => string.Equals(b.Content as string, I18n.L("备份"), StringComparison.Ordinal)));
            Assert.True(C4Files.HasText(panel, I18n.L("备份({0})", 2)));
        });
    }

    /// <summary>【Windows 源】<c>SavesDialog.cs:19-26</c>:三段滚动区的高度口径各不相同(0.4/180、150、170)。</summary>
    [Fact]
    public void Saves_dialog_scroll_bounds_follow_the_windows_constants()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            Task show = SavesDialog.ShowAsync("默认", "");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认"));
            List<SmoothScroll> scrolls = C4Files.Descendants(panel).OfType<SmoothScroll>().ToList();            Assert.Equal(3, scrolls.Count);
            Assert.Equal(620.0, scrolls[0].MinWidth);
            // 后两栏是裸 MaxHeight(Windows 也是直接写 MaxHeight = 150 / 170)
            Assert.Equal(150.0, scrolls[1].MaxHeight);
            Assert.Equal(170.0, scrolls[2].MaxHeight);
            // 第一栏是 BindWindowMaxHeight(0.4, 180):离屏没有宿主 ⇒ 不绑(见 ModsDialog 的同名用例)
            Assert.True(double.IsPositiveInfinity(scrolls[0].MaxHeight));
            scrolls[0].BindWindowMaxHeight(harness.Window, 0.4, 180.0);
            C4Files.Flush(2);
            double height = harness.Window.Bounds.Height;
            Assert.Equal(Math.Max(180.0, height * 0.4), scrolls[0].MaxHeight, 3);
        });
    }

    /// <summary>【Windows 源】<c>SavesDialog.cs:99</c>:存档列表也走 <c>Motion.Stagger</c>;关掉动画时立刻到位。</summary>
    [Fact]
    public void Saves_dialog_lands_immediately_with_motion_disabled()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            Motion.Enabled = false;
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("list_saves", "[{\"name\":\"World\"}]");
            Task show = SavesDialog.ShowAsync("默认", "");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认"));
            // 【Windows 源】<c>:97</c>:存档列表那一段是 Ui.Scroll(存 RowCard);关掉动画时行要立刻到位
            SPanel list = (SPanel)C4Files.Descendants(panel).OfType<SmoothScroll>().First().Content!;
            Border card = list.Children.OfType<Border>().First();
            Assert.Equal(1.0, card.Opacity);
            Assert.Null(card.RenderTransform);
            // 抽屉里没有残留的动画
            Motion.ResetObservability();
            Assert.Equal(0, Motion.RunningAnimationsSinceReset);
        });
    }

    /// <summary>三个对话框都用 <c>Dlg.Panel</c>(可关闭的浮层);点「关闭」后层要收干净(不留订阅/图层)。</summary>
    [Fact]
    public void Closing_a_panel_leaves_no_layer_behind()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            Task show = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            Assert.Equal(1, C4Files.LayerCount());
            C4Files.ClosePanel(panel);
            Assert.Equal(0, C4Files.LayerCount());
        });
    }

    /// <summary>【任务书】每个对话框的期望值都要有落点:这条把三张卡片的核心控件一次钉住(自查用)。</summary>
    [Fact]
    public void All_three_dialogs_build_their_expected_top_level_controls()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            Task mods = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(mods);
            Control modsPanel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            Assert.Single(C4Files.Descendants(modsPanel).OfType<ComboBox>());
            Assert.True(C4Files.HasButton(modsPanel, I18n.L("检查更新")));
            C4Files.ClosePanel(modsPanel);

            Task saves = SavesDialog.ShowAsync("默认", "");
            C4Files.PumpTask(saves);
            Control savesPanel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认"));
            Assert.Single(C4Files.Descendants(savesPanel).OfType<ComboBox>());
            Assert.Empty(C4Files.Descendants(savesPanel).OfType<CheckBox>());
            C4Files.ClosePanel(savesPanel);

            Task global = GlobalModsDialog.ShowAsync();
            C4Files.PumpTask(global);
            Control globalPanel = C4Files.PanelCard(I18n.L("全局模组"));
            Assert.Empty(C4Files.Descendants(globalPanel).OfType<ComboBox>());
            C4Files.ClosePanel(globalPanel);
            Assert.Equal(0, C4Files.LayerCount());
        });
    }

    /// <summary>【Windows 源】<c>ModsDialog.cs:32-56</c>:操作条上的五个按钮顺序与文案。</summary>
    [Fact]
    public void Mods_dialog_toolbar_matches_windows_order()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            Task show = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            List<string> wanted = new List<string>
            {
                I18n.L("导入 jar"), I18n.L("检查更新"), I18n.L("打开目录"), I18n.L("全局模组"), I18n.L("刷新"),
            };
            // 【本机实测】带图标的按钮 Content 是图文组合面板(不是字符串),Ui.Btn 会把文字写进
            // AutomationProperties.Name,所以按自动化名认(与 C3bFiles.ByText 同一口径)。
            List<string> heads = C4Files.Descendants(panel).OfType<Button>()
                .Select(b => AutomationProperties.GetName(b))
                .Where(s => s != null && wanted.Contains(s!))
                .Select(s => s!)
                .ToList();
            Assert.Equal(wanted, heads);
        });
    }

    /// <summary>【Windows 源】<c>SavesDialog.cs:38-41</c>:媒体栏头部「截图 / 日志」+ 下拉 + 计数。</summary>
    [Fact]
    public void Saves_dialog_media_header_matches_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            Task show = SavesDialog.ShowAsync("默认", "");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认"));
            ComboBox kind = C4Files.Find<ComboBox>(panel, "媒体类型下拉");
            Assert.Equal(3, kind.Items.Count);
            Assert.Equal(I18n.L("截图"), kind.Items[0]);
            Assert.Equal(I18n.L("崩溃报告"), kind.Items[1]);
            Assert.Equal(I18n.L("日志"), kind.Items[2]);
            Assert.Equal(130.0, kind.Width);
            Assert.True(C4Files.HasText(panel, I18n.L("截图 / 日志")));
            // 三段之间的两条分隔线【Windows 源 <c>:42</c> 的两个 Ui.Sep()】:高 1、无子控件
            Assert.Equal(2, C4Files.Descendants(panel).OfType<Border>().Count(b => b.Height == 1.0 && b.Child == null));
        });
    }

    // ================================================================ C4(d) 补的:未知项 / 后端失联 / 取消路径 / 只画不回写

    /// <summary>
    /// 【Windows 源】<c>ModsDialog.cs:168</c>:<c>version</c> 不在目标表里时 <c>FindIndex</c> 给 -1,
    /// <c>Math.Max(0, …)</c> 把它夹到第 0 项(不抛、不空选)。
    /// </summary>
    [Fact]
    public void Mods_dialog_unknown_version_is_clamped_to_the_first_target()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("get_mods_targets",
                "[{\"label\":\"实例共享 mods 目录\",\"value\":\"\"},{\"label\":\"1.20.1\",\"value\":\"1.20.1\"}]");
            Task show = ModsDialog.ShowAsync("默认", "9.9.9");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            ComboBox target = C4Files.Find<ComboBox>(panel, "目标下拉");
            Assert.Equal(0, target.SelectedIndex);
            // 第 0 项的 Value 是空串 ⇒ 列表按空串版本拉;那个不存在的 9.9.9 一个字节都没传到后端
            Assert.Contains("\"version\":\"\"", api.ParamsOf("get_installed_mod_entries"));
            Assert.DoesNotContain("9.9.9", api.ParamsOf("get_installed_mod_entries"));
        });
    }

    /// <summary>
    /// 【Windows 源】<c>ModsDialog.cs:101-109</c>:列表那次调用失败时 <c>TryCallAsync</c> 回落到空列表
    /// (<c>BridgeClient.cs:129-140</c> 静默吞掉异常)⇒ 落进「共 0 个模组」的空态;不抛、不弹错、不 Toast。
    /// </summary>
    [Fact]
    public void Mods_dialog_list_backend_error_degrades_to_the_empty_state()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().RouteError("get_installed_mod_entries", "后端没了");
            Task show = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            Assert.True(C4Files.HasText(panel, I18n.L("共 {0} 个模组", 0)));
            Assert.True(C4Files.HasText(panel, I18n.L("还没有模组")));
            C4Files.Settle(4);
            Assert.Empty(toasts.Items);
        });
    }

    /// <summary>
    /// 【Windows 源】<c>ModsDialog.cs:142-150</c> + <c>PageBase.cs:120-139</c>:<c>delete_mod</c> 抛了 ⇒
    /// <c>PageBase.Run</c> 收下异常并 Toast 失败标题「删除失败」(Error),且**不**走到后面的 <c>Reload()</c>
    /// (列表不重拉,行还在)。
    /// </summary>
    [Fact]
    public void Mods_dialog_delete_failure_toasts_and_does_not_reload()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_installed_mod_entries", "[{\"filename\":\"sodium.jar\"}]")
                .RouteError("delete_mod", "磁盘满了");
            Task show = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            C4Files.Click(C4Files.ByText(panel, I18n.L("删除")));
            C4Files.AnswerDialog(I18n.L("删除"));
            C4Files.Pump(() => toasts.Has(I18n.L("删除失败")));
            Assert.Contains(toasts.Items, t => t.Kind == ToastKind.Error && string.Equals(t.Title, I18n.L("删除失败"), StringComparison.Ordinal));
            C4Files.Settle(4);
            // 只有首屏那一次;失败的删除不该再拉一遍列表
            Assert.Equal(1, api.Count("get_installed_mod_entries"));
        });
    }

    /// <summary>
    /// 【本机实测 + Windows 源 <c>ModsDialog.cs:111-153</c>】:首屏只是"把行画出来",不该有任何回写。
    ///
    /// 这条专门盯 Avalonia 与 WPF 的那个差异:WPF 的 <c>Checked</c> / <c>Unchecked</c> 只在**用户**改动时触发,
    /// Avalonia 的 <c>IsCheckedChanged</c> 程序化赋值也会触发;移植把"初值先设、订阅在后"当成了不触发的前提
    /// (<c>ModsDialog.cs:157-161</c> 的行内注释),这条用例就是那个前提的回归闸。
    /// </summary>
    [Fact]
    public void Mods_dialog_drawing_rows_writes_nothing_back()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("get_installed_mod_entries",
                "[{\"filename\":\"a.jar\",\"enabled\":true},{\"filename\":\"b.jar\",\"enabled\":false}]");
            Task show = ModsDialog.ShowAsync("默认");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("模组管理 · {0}", "默认"));
            Assert.Equal(2, C4Files.Descendants(panel).OfType<CheckBox>().Count());
            C4Files.Settle(4);
            Assert.Equal(0, api.Count("enable_mod"));
            Assert.Equal(0, api.Count("disable_mod"));
        });
    }

    /// <summary>
    /// 【Windows 源】<c>ModsDialog.cs:208</c>:更新卡除了标题,还有正文那句小字与确认按钮「更新所选」;
    /// 点「取消」⇒ 什么都不写(返回值 false 传回调用者)。
    /// </summary>
    [Fact]
    public void Check_updates_ask_card_carries_the_windows_title_body_and_button()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("check_mod_updates", "[{\"filename\":\"a.jar\"}]");
            Task task = ModsDialog.CheckUpdatesAsync("默认");
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            Assert.Equal(I18n.L("{0} 个模组有更新", 1), card.Title);
            Assert.True(C4Files.HasText(card, I18n.L("勾上要更新的,旧文件会在新包校验通过后才删除。")));
            Assert.True(C4Files.HasButton(card, I18n.L("更新所选")));
            C4Files.AnswerDialog(I18n.L("取消"));
            C4Files.PumpTask(task);
            Assert.Equal(0, api.Count("apply_mod_update"));
        });
    }

    /// <summary>
    /// 【Windows 源】<c>GlobalModsDialog.cs:18-21</c>:「打开目录」调 <c>open_global_mods</c>(无参数),
    /// 而且与 ModsDialog 的「打开目录」不同 —— 这一支**不** Toast(所以不能照抄那句「已打开」)。
    /// </summary>
    [Fact]
    public void Global_mods_dialog_open_folder_calls_open_global_mods_without_a_toast()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi();
            Task task = GlobalModsDialog.ShowAsync();
            C4Files.PumpTask(task);
            Control panel = C4Files.PanelCard(I18n.L("全局模组"));
            C4Files.Click(C4Files.ByText(panel, I18n.L("打开目录")));
            C4Files.Pump(() => api.Count("open_global_mods") >= 1);
            Assert.Equal(1, api.Count("open_global_mods"));
            C4Files.Settle(4);
            Assert.Empty(toasts.Items);
        });
    }

    /// <summary>【Windows 源】<c>GlobalModsDialog.cs:22-25 / :28-61</c>:「刷新」重拉一次列表,空态被真实的行替掉。</summary>
    [Fact]
    public void Global_mods_dialog_refresh_reloads_the_list()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            int calls = 0;
            using var api = new C3cFakeApi().Route("list_global_mods", _ =>
            {
                int n = System.Threading.Interlocked.Increment(ref calls);
                return (n == 1)
                    ? "[]"
                    : "[{\"filename\":\"g1.jar\",\"enabled\":true,\"size\":\"1 KB\"},{\"filename\":\"g2.jar\",\"enabled\":false,\"size\":\"2 KB\"}]";
            });
            Task task = GlobalModsDialog.ShowAsync();
            C4Files.PumpTask(task);
            Control panel = C4Files.PanelCard(I18n.L("全局模组"));
            Assert.True(C4Files.HasText(panel, I18n.L("全局模组目录是空的")));
            C4Files.Click(C4Files.ByText(panel, I18n.L("刷新")));
            C4Files.Pump(() => api.Count("list_global_mods") >= 2);
            Assert.True(C4Files.HasText(panel, "g1.jar"));
            Assert.True(C4Files.HasText(panel, "2 KB"));
            Assert.Equal(2, C4Files.Descendants(panel).OfType<CheckBox>().Count());
            Assert.False(C4Files.HasText(panel, I18n.L("全局模组目录是空的")));
        });
    }

    /// <summary>【任务书】全局模组列表里的畸形行(空对象 / null 字段)不许抛,行照画、开关照给。</summary>
    [Fact]
    public void Global_mods_dialog_survives_malformed_rows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("list_global_mods", "[{},{\"filename\":null,\"size\":null,\"enabled\":true}]");
            Task task = GlobalModsDialog.ShowAsync();
            C4Files.PumpTask(task);
            Control panel = C4Files.PanelCard(I18n.L("全局模组"));
            Assert.Equal(2, C4Files.Descendants(panel).OfType<CheckBox>().Count());
            Assert.All(C4Files.Descendants(panel).OfType<CheckBox>(), b => Assert.Contains("switch", b.Classes));
            Assert.False(C4Files.HasText(panel, I18n.L("全局模组目录是空的")));
        });
    }

    /// <summary>【本机实测 + Windows 源 <c>GlobalModsDialog.cs:39-55</c>】:画行不回写(与 ModsDialog 的同名用例同一个理由)。</summary>
    [Fact]
    public void Global_mods_dialog_drawing_rows_writes_nothing_back()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("list_global_mods",
                "[{\"filename\":\"g1.jar\",\"enabled\":true},{\"filename\":\"g2.jar\",\"enabled\":false}]");
            Task task = GlobalModsDialog.ShowAsync();
            C4Files.PumpTask(task);
            C4Files.Settle(4);
            Assert.Equal(0, api.Count("set_global_mod_enabled"));
        });
    }

    /// <summary>【Windows 源】<c>SavesDialog.cs:83-93</c>:删存档的确认卡点「取消」⇒ 不写后端、也不重拉列表。</summary>
    [Fact]
    public void Saves_dialog_delete_cancelled_writes_nothing()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("list_saves", "[{\"name\":\"World\"}]");
            Task show = SavesDialog.ShowAsync("默认", "1.20.1");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认") + " / 1.20.1");
            C4Files.Click(C4Files.ByText(panel, I18n.L("删除")));
            C4Files.AnswerDialog(I18n.L("取消"));
            C4Files.Settle(4);
            Assert.Equal(0, api.Count("delete_save"));
            Assert.Equal(1, api.Count("list_saves"));
        });
    }

    /// <summary>【Windows 源】<c>SavesDialog.cs:107-123</c>:还原备份的确认卡点「取消」⇒ 不写后端、不 Toast「已还原」。</summary>
    [Fact]
    public void Saves_dialog_restore_cancelled_writes_nothing()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("list_save_backups", "[{\"name\":\"bk1\"}]");
            Task show = SavesDialog.ShowAsync("默认", "");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认"));
            C4Files.Click(C4Files.ByText(panel, I18n.L("还原")));
            C4Files.AnswerDialog(I18n.L("取消"));
            C4Files.Settle(4);
            Assert.Equal(0, api.Count("restore_save_backup"));
            Assert.False(toasts.Has(I18n.L("已还原")));
        });
    }

    /// <summary>【Windows 源】<c>SavesDialog.cs:213-224</c>:数据包选择卡点「取消」⇒ 一个字节都不装。</summary>
    [Fact]
    public void Install_datapack_cancelled_writes_nothing()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("list_saves", "[{\"name\":\"World\"}]")
                .Route("get_installed_datapacks", "[\"pack1.zip\"]");
            Task show = SavesDialog.ShowAsync("默认", "");
            C4Files.PumpTask(show);
            Control panel = C4Files.PanelCard(I18n.L("存档管理 · {0}", "默认"));
            C4Files.Click(C4Files.ByText(panel, I18n.L("装数据包")));
            C4Files.AnswerDialog(I18n.L("取消"));
            C4Files.Settle(4);
            Assert.Equal(0, api.Count("install_datapack_into_save"));
            Assert.False(toasts.Has(I18n.L("已安装")));
        });
    }

    /// <summary>
    /// 【Windows 源】<c>SavesDialog.cs:188-194</c>:目标表只有 1 行、而那行是**空对象**时返回空串
    /// (不弹卡片、不抛)—— 「未知/畸形项不抛」在 <c>PickSaveTargetAsync</c> 这一支的落点。
    /// </summary>
    [Fact]
    public void Pick_save_target_tolerates_a_single_blank_row()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C4Scope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            using var api = new C3cFakeApi().Route("get_saves_targets", "[{}]");
            Task<string?> only = SavesDialog.PickSaveTargetAsync("默认", "装到哪");
            C4Files.PumpTask(only);
            Assert.Equal("", only.Result);
            Assert.Equal(0, C4Files.LayerCount());
        });
    }
}
