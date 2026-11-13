using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using TipCL.Models;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C5b(下载任务 / 账号 / 启动向导 + 皮肤拼图):每一条都对着只读的 Windows 源写
/// (windos\tipcl\src\TipCL\Pages\TasksPage.cs / AccountPage.cs / FirstRunWizard.cs / SkinPng.cs),
/// 期望值的来源写在各条的注释里。
///
/// 纪律:碰 Avalonia 的用例体一律 <c>AvaloniaSession.Run(...)</c>(D-79);全局静态由 <see cref="C5bScope"/>
/// 快照还原(D-69);后端用按方法名路由的回环假后端 <see cref="C3cFakeApi"/>;任务列表先清空再断言(D-132)。
/// </summary>
public class C5bTasksAccountTests
{
    /// <summary>按文案或自动化名找按钮(带图标的按钮 <c>Content</c> 不是字符串,<c>Ui.Btn</c> 会把文字写进自动化名)。</summary>
    private static Button Button(Control root, string text)
    {
        Button? hit = C5bFiles.Descendants(root).OfType<Button>().FirstOrDefault(b =>
            string.Equals(b.Content as string, text, StringComparison.Ordinal) ||
            string.Equals(AutomationProperties.GetName(b), text, StringComparison.Ordinal));
        Assert.True(hit != null, "找不到按钮「" + text + "」");
        return hit!;
    }

    private static bool HasButtonNamed(Control root, string text)
    {
        return C5bFiles.Descendants(root).OfType<Button>().Any(b =>
            string.Equals(b.Content as string, text, StringComparison.Ordinal) ||
            string.Equals(AutomationProperties.GetName(b), text, StringComparison.Ordinal));
    }

    private static TasksPage NewTasksPage(C5bLayerHost harness)
    {
        C5bFiles.LayoutDir();
        TasksPage page = new TasksPage();
        C5bFiles.Mount(harness, page);
        C5bFiles.PumpTask(page.EnsureLoadedAsync());
        return page;
    }

    private static AccountPage NewAccountPage(C5bLayerHost harness)
    {
        C5bFiles.LayoutDir();
        AccountPage page = new AccountPage();
        C5bFiles.Mount(harness, page);
        C5bFiles.PumpTask(page.EnsureLoadedAsync());
        return page;
    }

    // ================================================================ TasksPage

    /// <summary>【Windows 源】<c>TasksPage.cs:50-65</c>:标题「下载任务」+ 概览行 + 两个区块。</summary>
    [Fact]
    public void Tasks_page_pushes_the_windows_shell()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi();
            TasksPage page = NewTasksPage(harness);
            Assert.Equal(I18n.L("下载任务"), page.Title);
            Assert.True(C5bFiles.HasText(page, I18n.L("下载任务")));
            Assert.True(C5bFiles.HasText(page, I18n.L("安装、下载、启动都在这里排队")));
            Assert.True(HasButtonNamed(page, I18n.L("清除已完成")));
            // 两个区块的标题(布局自定义的落点)
            Assert.True(C5bFiles.HasText(page, I18n.L("概览")));
            Assert.True(C5bFiles.HasText(page, I18n.L("任务列表")));
        });
    }

    /// <summary>【Windows 源】<c>TasksPage.cs:101-105 / :103</c>:没有任务时的空态文案。</summary>
    [Fact]
    public void Tasks_page_empty_state_matches_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi();
            TasksPage page = NewTasksPage(harness);
            Assert.True(C5bFiles.HasText(page, I18n.L("还没有任务")));
            Assert.True(C5bFiles.HasText(page, I18n.L("到「下载」页装个版本或模组试试")));
            // 空的时候概览那句是空串(Windows:list.Count == 0 ⇒ Text = "")
            Assert.False(C5bFiles.Texts(page).Any(t => (t.Text ?? "").StartsWith(I18n.L("进行中"), StringComparison.Ordinal)));
        });
    }

    /// <summary>【Windows 源】<c>TasksPage.cs:106-112 / :136-158</c>:一行 = 标题 + 状态副行 + 进度条 + 三个动作。</summary>
    [Fact]
    public void Tasks_page_row_shows_title_status_progress_and_actions()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi();
            C5bFiles.AddTask("t1", "安装 1.20.1", progress: 40.0);
            TasksPage page = NewTasksPage(harness);
            page.OnShown();
            Assert.True(C5bFiles.HasText(page, "安装 1.20.1"));
            Assert.True(C5bFiles.HasText(page, "下载中 1 MB/s"));
            Assert.True(HasButtonNamed(page, I18n.L("取消")));
            Assert.True(HasButtonNamed(page, I18n.L("日志")));
            ProgressBar bar = C5bFiles.Find<ProgressBar>(page, "进度条");
            Assert.False(bar.IsIndeterminate);
            // 没跑完 ⇒ 「崩溃分析」是藏着的(Windows:button2.Visibility = Collapsed)
            Assert.False(Button(page, I18n.L("崩溃分析")).IsVisible);
        });
    }

    /// <summary>【Windows 源】<c>TasksPage.cs:100 / :261</c>:概览是「进行中 N · 共 M」。</summary>
    [Fact]
    public void Tasks_page_summary_counts_running_and_total()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi();
            C5bFiles.AddTask("a", "跑着的");
            C5bFiles.AddTask("b", "完事的", finished: true);
            TasksPage page = NewTasksPage(harness);
            page.OnShown();
            Assert.True(C5bFiles.HasText(page, I18n.L("进行中 {0} · 共 {1}", 1, 2)));
        });
    }

    /// <summary>【Windows 源】<c>TasksPage.cs:242-250</c>:跑完的行「取消」变「清除」,失败的才给「崩溃分析」。</summary>
    [Fact]
    public void Tasks_page_finished_row_swaps_cancel_for_clear_and_reveals_crash_on_failure()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi();
            C5bFiles.AddTask("bad", "失败的", finished: true, success: false);
            TasksPage page = NewTasksPage(harness);
            page.OnShown();
            Assert.True(HasButtonNamed(page, I18n.L("清除")));
            Assert.False(HasButtonNamed(page, I18n.L("取消")));
            Assert.True(Button(page, I18n.L("崩溃分析")).IsVisible);
            ProgressBar bar = C5bFiles.Find<ProgressBar>(page, "进度条");
            // 【Windows 源】跑完且失败 ⇒ 进度条染成 B.Danger;这里只钉「不是 indeterminate 就是真值」
            Assert.False(bar.IsIndeterminate);
        });
    }

    /// <summary>【Windows 源】<c>TasksPage.cs:54-58</c>:「清除已完成」只动 <c>TaskStore</c>,不碰后端。</summary>
    [Fact]
    public void Tasks_page_clear_finished_drops_finished_rows_only()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi();
            C5bFiles.AddTask("done", "完事的", finished: true);
            C5bFiles.AddTask("run", "跑着的");
            TasksPage page = NewTasksPage(harness);
            page.OnShown();
            C5bFiles.Click(Button(page, I18n.L("清除已完成")));
            C5bFiles.Settle(4);
            Assert.Null(TaskStore.Get("done"));
            Assert.NotNull(TaskStore.Get("run"));
            Assert.False(C5bFiles.HasText(page, "完事的"));
            Assert.Equal(0, api.Count("cancel_task"));
        });
    }

    /// <summary>【Windows 源】<c>TasksPage.cs:173-191</c>:没跑完的行点「取消」下发 <c>cancel_task</c> 并把自己禁用。</summary>
    [Fact]
    public void Tasks_page_cancel_running_row_calls_cancel_task()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi();
            C5bFiles.AddTask("t1", "跑着的");
            TasksPage page = NewTasksPage(harness);
            page.OnShown();
            Button cancel = Button(page, I18n.L("取消"));
            C5bFiles.Click(cancel);
            C5bFiles.Pump(() => api.Count("cancel_task") >= 1);
            C5bFiles.AssertParam(api, "cancel_task", "task_id", "t1");
            Assert.False(cancel.IsEnabled);
        });
    }

    /// <summary>【Windows 源】<c>TasksPage.cs:177-181</c>:跑完的行点「清除」只是从列表里摘掉,不调后端。</summary>
    [Fact]
    public void Tasks_page_clear_finished_row_removes_it_without_a_backend_call()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi();
            C5bFiles.AddTask("done", "完事的", finished: true);
            TasksPage page = NewTasksPage(harness);
            page.OnShown();
            C5bFiles.Click(Button(page, I18n.L("清除")));
            C5bFiles.Settle(4);
            Assert.Null(TaskStore.Get("done"));
            Assert.True(C5bFiles.HasText(page, I18n.L("还没有任务")));
            Assert.Equal(0, api.Count("cancel_task"));
        });
    }

    /// <summary>【Windows 源】<c>TasksPage.cs:214-228</c>:「日志」把这张卡展开成只读日志框,再点收起。</summary>
    [Fact]
    public void Tasks_page_log_toggle_expands_and_collapses_the_log_box()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi();
            TaskRow row = C5bFiles.AddTask("t1", "跑着的");
            row.Append("第一行日志");
            TasksPage page = NewTasksPage(harness);
            page.OnShown();
            TextBox log = C5bFiles.Find<TextBox>(page, "日志框");
            Assert.False(log.IsVisible);
            C5bFiles.Click(Button(page, I18n.L("日志")));
            C5bFiles.Settle(2);
            Assert.True(log.IsVisible);
            Assert.Contains("第一行日志", log.Text ?? "", StringComparison.Ordinal);
            C5bFiles.Click(Button(page, I18n.L("日志")));
            C5bFiles.Settle(2);
            Assert.False(log.IsVisible);
        });
    }

    /// <summary>【Windows 源】<c>TasksPage.cs:196-211</c>:没有崩溃报告时只 Toast 一句,不弹窗。</summary>
    [Fact]
    public void Tasks_page_crash_button_toasts_when_there_is_no_report()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("get_crash", "{}");
            C5bFiles.AddTask("bad", "失败的", finished: true, success: false);
            TasksPage page = NewTasksPage(harness);
            page.OnShown();
            C5bFiles.Click(Button(page, I18n.L("崩溃分析")));
            C5bFiles.Pump(() => toasts.Has(I18n.L("没有崩溃分析")));
            Assert.True(toasts.Has(I18n.L("没有崩溃分析")));
            C5bFiles.AssertParam(api, "get_crash", "task_id", "bad");
            Assert.Equal(0, C5bFiles.LayerCount());
        });
    }

    /// <summary>【Windows 源】<c>TasksPage.cs:117-134</c>:占位标题的任务会去问 <c>task_title</c> 并把标题补上。</summary>
    [Fact]
    public void Tasks_page_resolves_placeholder_titles_from_the_backend()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("task_title", "\"真的标题\"");
            TaskRow row = C5bFiles.AddTask("t1", "");
            Assert.Equal(TaskStore.PlaceholderTitle, row.Title);
            TasksPage page = NewTasksPage(harness);
            page.OnShown();
            C5bFiles.Pump(() => api.Count("task_title") >= 1);
            C5bFiles.Pump(() => C5bFiles.HasText(page, "真的标题"));
            Assert.True(C5bFiles.HasText(page, "真的标题"));
            Assert.Equal("真的标题", row.Title);
        });
    }

    // ================================================================ AccountPage

    /// <summary>【Windows 源】<c>AccountPage.cs:46-80</c>:标题「账号」+ 五个入口 + 头像卡 + 两个区块。</summary>
    [Fact]
    public void Account_page_pushes_the_windows_shell()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi();
            AccountPage page = NewAccountPage(harness);
            Assert.Equal(I18n.L("账号"), page.Title);
            Assert.True(HasButtonNamed(page, I18n.L("添加离线账号")));
            Assert.True(HasButtonNamed(page, I18n.L("微软登录")));
            Assert.True(HasButtonNamed(page, I18n.L("皮肤站登录")));
            Assert.True(HasButtonNamed(page, I18n.L("统一通行证")));
            Assert.True(C5bFiles.HasText(page, I18n.L("启动游戏时使用标记为「当前」的账号")));
            Assert.True(C5bFiles.HasText(page, I18n.L("账号操作")));
            Assert.True(C5bFiles.HasText(page, I18n.L("账号卡")));
        });
    }

    /// <summary>【Windows 源】<c>AccountPage.cs:94-99 / :115-117</c>:没有账号时的空态与头像卡文案。</summary>
    [Fact]
    public void Account_page_empty_state_matches_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi();
            AccountPage page = NewAccountPage(harness);
            Assert.True(C5bFiles.HasText(page, I18n.L("共 {0} 个", 0)));
            Assert.True(C5bFiles.HasText(page, I18n.L("还没有账号")));
            Assert.True(C5bFiles.HasText(page, I18n.L("添加离线账号,或用微软 / 皮肤站 / 统一通行证登录")));
            Assert.True(C5bFiles.HasText(page, I18n.L("未登录")));
            Assert.True(C5bFiles.HasText(page, I18n.L("先添加一个账号")));
        });
    }

    /// <summary>【Windows 源】<c>AccountPage.cs:101-106 / :154-164 / :171-172</c>:类型标签 + 「当前」标签 + UUID 副行。</summary>
    [Fact]
    public void Account_page_rows_show_type_labels_and_the_current_tag()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("get_account_rows",
                "[{\"name\":\"Steve\",\"type\":\"microsoft\",\"uuid\":\"abc-123\",\"active\":true}," +
                "{\"name\":\"Alex\",\"type\":\"offline\",\"active\":false}]");
            AccountPage page = NewAccountPage(harness);
            Assert.True(C5bFiles.HasText(page, I18n.L("共 {0} 个", 2)));
            Assert.True(C5bFiles.HasText(page, "Steve"));
            Assert.True(C5bFiles.HasText(page, "Alex"));
            Assert.True(C5bFiles.HasText(page, I18n.L("微软")));
            Assert.True(C5bFiles.HasText(page, I18n.L("离线")));
            Assert.True(C5bFiles.HasText(page, I18n.L("当前")));
            Assert.True(C5bFiles.HasText(page, "UUID abc-123"));
            // 没有 uuid ⇒ 「<类型>账号」(Windows:text2 = text + I18n.L("账号"))
            Assert.True(C5bFiles.HasText(page, I18n.L("离线") + I18n.L("账号")));
            // 已经「当前」的那一行不再给「设为当前」;离线那一行给
            Assert.True(HasButtonNamed(page, I18n.L("设为当前")));
            Assert.True(HasButtonNamed(page, I18n.L("皮肤")));
        });
    }

    /// <summary>【Windows 源】<c>AccountPage.cs:206-211</c>:「设为当前」写 <c>set_active_account</c> + Toast + 重拉。</summary>
    [Fact]
    public void Account_page_set_active_writes_and_reloads()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("get_account_rows", "[{\"name\":\"Alex\",\"type\":\"offline\",\"active\":false}]");
            AccountPage page = NewAccountPage(harness);
            C5bFiles.Click(Button(page, I18n.L("设为当前")));
            C5bFiles.Pump(() => api.Count("set_active_account") >= 1);
            C5bFiles.AssertParam(api, "set_active_account", "name", "Alex");
            C5bFiles.Pump(() => toasts.Has(I18n.L("已切换")));
            C5bFiles.Pump(() => api.Count("get_account_rows") >= 2);
            Assert.Equal(2, api.Count("get_account_rows"));
        });
    }

    /// <summary>【Windows 源】<c>AccountPage.cs:213-224</c>:删账号先确认,取消则一个字节都不写。</summary>
    [Fact]
    public void Account_page_delete_confirms_then_removes_and_a_cancel_writes_nothing()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("get_account_rows", "[{\"name\":\"Alex\",\"type\":\"offline\",\"active\":false}]");
            AccountPage page = NewAccountPage(harness);
            C5bFiles.Click(Button(page, I18n.L("删除")));
            C5bFiles.AnswerDialog(I18n.L("取消"));
            C5bFiles.Settle(4);
            Assert.Equal(0, api.Count("remove_account"));

            C5bFiles.Click(Button(page, I18n.L("删除")));
            C5bFiles.AnswerDialog(I18n.L("删除"));
            C5bFiles.Pump(() => api.Count("remove_account") >= 1);
            C5bFiles.AssertParam(api, "remove_account", "name", "Alex");
            C5bFiles.Pump(() => toasts.Has(I18n.L("已删除")));
        });
    }

    /// <summary>【Windows 源】<c>AccountPage.cs:236-242</c>:离线账号名称为空只提示,不写后端。</summary>
    [Fact]
    public void Account_page_add_offline_needs_a_name()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("get_account_rows", "[]");
            AccountPage page = NewAccountPage(harness);
            C5bFiles.Click(Button(page, I18n.L("添加离线账号")));
            DialogCard card = WaitAsk();
            C5bFiles.Click(C5bFiles.ByText(card, I18n.L("添加")));
            C5bFiles.Pump(() => toasts.Has(I18n.L("缺少名字")));
            Assert.True(toasts.Has(I18n.L("缺少名字")));
            Assert.Equal(0, api.Count("add_offline_account"));
        });
    }

    /// <summary>【Windows 源】<c>AccountPage.cs:243-253</c>:三种皮肤文案映射成三个后端键(default / steve / alex)。</summary>
    [Theory]
    [InlineData("默认", "default")]
    [InlineData("Alex", "alex")]
    public void Account_page_add_offline_maps_the_skin_choice(string label, string expected)
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("get_account_rows", "[]");
            AccountPage page = NewAccountPage(harness);
            C5bFiles.Click(Button(page, I18n.L("添加离线账号")));
            DialogCard card = WaitAsk();
            C5bFiles.Find<TextBox>(card, "离线名字输入框").Text = "Steve";
            ComboBox skin = C5bFiles.Find<ComboBox>(card, "皮肤下拉");
            int at = skin.Items.Cast<object>().ToList().FindIndex(o => string.Equals(o as string, (label == "默认") ? I18n.L("默认") : label, StringComparison.Ordinal));
            Assert.True(at >= 0);
            skin.SelectedIndex = at;
            C5bFiles.Click(C5bFiles.ByText(card, I18n.L("添加")));
            C5bFiles.Pump(() => api.Count("add_offline_account") >= 1);
            C5bFiles.AssertParam(api, "add_offline_account", "username", "Steve");
            C5bFiles.AssertParam(api, "add_offline_account", "skin", expected);
        });
    }

    /// <summary>【Windows 源】<c>AccountPage.cs:399-452</c>:微软登录起 <c>start_microsoft_login</c>,浮层不可关,
    /// 收到 <c>login_code</c> 填码、<c>finished</c> 成功则关掉浮层 + Toast + 重拉。</summary>
    [Fact]
    public void Account_page_microsoft_login_starts_the_task_and_reacts_to_events()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("get_account_rows", "[]").Route("start_microsoft_login", "\"task-9\"");
            AccountPage page = NewAccountPage(harness);
            C5bFiles.Click(Button(page, I18n.L("微软登录")));
            C5bFiles.Pump(() => api.Count("start_microsoft_login") >= 1);
            Control panel = C5bFiles.PanelCard(I18n.L("微软账号登录"));
            Assert.Equal(1, C5bFiles.LayerCount());
            // 【Windows 源】:login_code 事件把码填进去、把地址拼进提示
            page.OnEvent(new BridgeEvent { Event = "login_code", Code = "ABCD-EFGH", Uri = "https://microsoft.com/link" });
            C5bFiles.Settle(2);
            Assert.True(C5bFiles.HasText(panel, "ABCD-EFGH"));
            Assert.True(C5bFiles.Texts(panel).Any(t => (t.Text ?? "").Contains("https://microsoft.com/link", StringComparison.Ordinal)));
            // 【Windows 源】:finished 成功 ⇒ 关浮层 + Toast「登录成功」+ 重拉
            page.OnEvent(new BridgeEvent { Event = "finished", TaskId = "task-9", Success = true });
            C5bFiles.Pump(() => toasts.Has(I18n.L("登录成功")));
            Assert.True(toasts.Has(I18n.L("登录成功")));
            C5bFiles.Pump(() => C5bFiles.LayerCount() == 0);
            C5bFiles.Pump(() => api.Count("get_account_rows") >= 2);
        });
    }

    /// <summary>【Windows 源】<c>AccountPage.cs:470-484</c>:皮肤站登录缺地址 / 用户名只提示;补齐才起任务。</summary>
    [Fact]
    public void Account_page_authlib_login_requires_api_and_username()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("get_account_rows", "[]").Route("authlib_presets", "[]").Route("start_authlib_login", "\"task-1\"");
            AccountPage page = NewAccountPage(harness);

            // 第一趟:什么都不填就点「登录」⇒ 只提示,不起任务(Ask 关掉后页面直接返回)
            C5bFiles.Click(Button(page, I18n.L("皮肤站登录")));
            DialogCard empty = WaitAsk();
            C5bFiles.Click(C5bFiles.ByText(empty, I18n.L("登录")));
            C5bFiles.Pump(() => toasts.Has(I18n.L("填写不完整")));
            Assert.Equal(0, api.Count("start_authlib_login"));

            // 第二趟:重新点开,填好 API 地址与用户名(密码框是 Ui.Pw 的 TextBox)
            C5bFiles.Click(Button(page, I18n.L("皮肤站登录")));
            DialogCard filled = WaitAsk();
            List<TextBox> boxes = C5bFiles.Descendants(filled).OfType<TextBox>().ToList();
            Assert.True(boxes.Count >= 3, "皮肤站浮层应当有 API 地址 / 用户名 / 密码三个输入框");
            boxes[0].Text = "https://littleskin.cn/api/yggdrasil";
            boxes[1].Text = "someone@example.com";
            C5bFiles.Click(C5bFiles.ByText(filled, I18n.L("登录")));
            C5bFiles.Pump(() => api.Count("start_authlib_login") >= 1);
            C5bFiles.AssertParam(api, "start_authlib_login", "api", "https://littleskin.cn/api/yggdrasil");
            C5bFiles.AssertParam(api, "start_authlib_login", "username", "someone@example.com");
            C5bFiles.Pump(() => toasts.Has(I18n.L("正在登录")));
        });
    }

    /// <summary>【Windows 源】<c>AccountPage.cs:493-510</c>:统一通行证缺服务器 ID 只提示 + 抖一下输入框。</summary>
    [Fact]
    public void Account_page_nide8_login_requires_a_server_id()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("get_account_rows", "[]").Route("start_nide8_login", "\"task-2\"");
            AccountPage page = NewAccountPage(harness);
            C5bFiles.Click(Button(page, I18n.L("统一通行证")));
            DialogCard card = WaitAsk();
            C5bFiles.Click(C5bFiles.ByText(card, I18n.L("登录")));
            C5bFiles.Pump(() => toasts.Has(I18n.L("缺少服务器 ID")));
            Assert.True(toasts.Has(I18n.L("缺少服务器 ID")));
            Assert.Equal(0, api.Count("start_nide8_login"));
        });
    }

    /// <summary>【Windows 源】<c>AccountPage.cs:344-351</c>:皮肤浮层点「清除,用游戏默认」⇒ 只带 name 写 <c>set_account_skin</c>。</summary>
    [Fact]
    public void Account_page_skin_dialog_clear_writes_only_the_name()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_account_rows", "[{\"name\":\"Alex\",\"type\":\"offline\",\"active\":false,\"skin_file\":\"alex.png\"}]")
                .Route("get_account_skin", "{\"name\":\"Alex\",\"skin_file\":\"alex.png\",\"skin_model\":\"classic\"}");
            AccountPage page = NewAccountPage(harness);
            C5bFiles.Click(Button(page, I18n.L("皮肤")));
            DialogCard card = WaitAsk();
            C5bFiles.Click(Button(card, I18n.L("清除,用游戏默认")));
            C5bFiles.Click(C5bFiles.ByText(card, I18n.L("保存")));
            C5bFiles.Pump(() => api.Count("set_account_skin") >= 1);
            string args = api.ParamsOf("set_account_skin");
            Assert.Contains("\"name\":\"Alex\"", args, StringComparison.Ordinal);
            Assert.DoesNotContain("\"path\"", args, StringComparison.Ordinal);
            C5bFiles.Pump(() => toasts.Has(I18n.L("皮肤已更新")));
        });
    }

    // ================================================================ FirstRunWizard

    /// <summary>【Windows 源】<c>FirstRunWizard.cs:58-135</c>:标题「欢迎使用 TipCL」+ 560 宽 + 四步 + 三个按钮。</summary>
    [Fact]
    public void First_run_wizard_pushes_the_windows_panel()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("get_settings", "{\"root\":\"/tmp/game\"}");
            Task<bool> running = RunWizard(api);
            Control panel = C5bFiles.PanelCard(I18n.L("欢迎使用 TipCL"));
            Assert.Equal(560.0, ((Border)panel).MaxWidth);
            Assert.True(C5bFiles.HasText(panel, I18n.L("三步就能开始玩")));
            Assert.True(C5bFiles.HasText(panel, I18n.L("第 {0} / {1} 步", 1, 4)));
            Assert.True(HasButtonNamed(panel, I18n.L("上一步")));
            Assert.True(HasButtonNamed(panel, I18n.L("下一步")));
            Assert.True(HasButtonNamed(panel, I18n.L("跳过向导")));
            // 第一步的「上一步」禁用(Windows:back.IsEnabled = index > 0)
            Assert.False(Button(panel, I18n.L("上一步")).IsEnabled);
            Assert.Equal(1, C5bFiles.LayerCount());
            C5bFiles.Click(Button(panel, I18n.L("跳过向导")));
            C5bFiles.PumpTask(running);
        });
    }

    /// <summary>【Windows 源】<c>FirstRunWizard.cs:208-220</c>:四步之间前后翻,最后一步按钮变「开始使用」。</summary>
    [Fact]
    public void First_run_wizard_walks_the_four_steps()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("get_settings", "{}");
            Task<bool> running = RunWizard(api);
            Control panel = C5bFiles.PanelCard(I18n.L("欢迎使用 TipCL"));
            C5bFiles.Click(Button(panel, I18n.L("下一步")));
            C5bFiles.Settle(2);
            Assert.True(C5bFiles.HasText(panel, I18n.L("第 {0} / {1} 步", 2, 4)));
            C5bFiles.Click(Button(panel, I18n.L("下一步")));
            C5bFiles.Settle(2);
            Assert.True(C5bFiles.HasText(panel, I18n.L("第 {0} / {1} 步", 3, 4)));
            C5bFiles.Click(Button(panel, I18n.L("下一步")));
            C5bFiles.Settle(2);
            Assert.True(C5bFiles.HasText(panel, I18n.L("第 {0} / {1} 步", 4, 4)));
            // 【Windows 源】最后一步的「下一步」变成「开始使用」
            Assert.True(HasButtonNamed(panel, I18n.L("开始使用")));
            Assert.True(Button(panel, I18n.L("上一步")).IsEnabled);
            // 回退一步:步骤号退回去,「上一步」在第一步才禁用
            C5bFiles.Click(Button(panel, I18n.L("上一步")));
            C5bFiles.Settle(2);
            Assert.True(C5bFiles.HasText(panel, I18n.L("第 {0} / {1} 步", 3, 4)));
            C5bFiles.Click(Button(panel, I18n.L("跳过向导")));
            C5bFiles.PumpTask(running);
        });
    }

    /// <summary>【Windows 源】<c>FirstRunWizard.cs:158-168</c>:跳过向导只把 <c>first_run</c> 关掉,不写另外三项、不碰目录。</summary>
    [Fact]
    public void First_run_wizard_skip_writes_only_first_run_false()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("get_settings", "{\"root\":\"/tmp/game\"}");
            Task<bool> running = RunWizard(api);
            Control panel = C5bFiles.PanelCard(I18n.L("欢迎使用 TipCL"));
            C5bFiles.Click(Button(panel, I18n.L("跳过向导")));
            C5bFiles.PumpTask(running);
            Assert.Equal(1, api.Count("save_settings"));
            Assert.Contains("\"first_run\":false", api.ParamsOf("save_settings"), StringComparison.Ordinal);
            Assert.Equal(0, api.Count("set_game_dir"));
            Assert.False(toasts.Has(I18n.L("向导已完成")));
        });
    }

    /// <summary>【Windows 源】<c>FirstRunWizard.cs:169-199</c>:走完写目录 + 四项设置 + Toast;
    /// 「刷新设置页」那条走注入缝(接上了才调)。</summary>
    [Fact]
    public void First_run_wizard_finish_writes_settings_and_calls_the_injected_refresh()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            int refreshed = 0;
            FirstRunWizard.SettingsRefresh = () =>
            {
                refreshed++;
                return Task.CompletedTask;
            };
            using var api = new C3cFakeApi().Route("get_settings", "{\"root\":\"/tmp/old\"}").Route("set_game_dir", "\"/tmp/new\"");
            Task<bool> running = RunWizard(api);
            Control panel = C5bFiles.PanelCard(I18n.L("欢迎使用 TipCL"));
            // 第二步:把游戏目录改成 /tmp/new
            C5bFiles.Click(Button(panel, I18n.L("下一步")));
            C5bFiles.Settle(2);
            List<TextBox> boxes = C5bFiles.Descendants(panel).OfType<TextBox>().ToList();
            Assert.True(boxes.Count >= 1, "向导第二步应当有游戏目录输入框");
            boxes[0].Text = "/tmp/new";
            // 走到最后一步并收尾
            C5bFiles.Click(Button(panel, I18n.L("下一步")));
            C5bFiles.Settle(2);
            C5bFiles.Click(Button(panel, I18n.L("下一步")));
            C5bFiles.Settle(2);
            C5bFiles.Click(Button(panel, I18n.L("开始使用")));
            C5bFiles.PumpTask(running);
            C5bFiles.AssertParam(api, "set_game_dir", "path", "/tmp/new");
            string settings = api.ParamsOf("save_settings");
            Assert.Contains("\"download_source\"", settings, StringComparison.Ordinal);
            Assert.Contains("\"default_memory_mb\"", settings, StringComparison.Ordinal);
            Assert.Contains("\"default_isolation\"", settings, StringComparison.Ordinal);
            Assert.Contains("\"first_run\":false", settings, StringComparison.Ordinal);
            Assert.Equal(1, refreshed);
            Assert.True(toasts.Has(I18n.L("向导已完成")));
            Assert.Equal(0, C5bFiles.LayerCount());
        });
    }

    /// <summary>【Windows 源】<c>FirstRunWizard.cs:196-199</c> 的注入缝:没接线时**不许抛**,也不许假装刷新过。</summary>
    [Fact]
    public void First_run_wizard_works_without_the_settings_refresh_seam()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            FirstRunWizard.SettingsRefresh = null;
            using var api = new C3cFakeApi().Route("get_settings", "{}");
            Task<bool> running = RunWizard(api);
            Control panel = C5bFiles.PanelCard(I18n.L("欢迎使用 TipCL"));
            for (int i = 0; i < 3; i++)
            {
                C5bFiles.Click(Button(panel, I18n.L("下一步")));
                C5bFiles.Settle(2);
            }

            C5bFiles.Click(Button(panel, I18n.L("开始使用")));
            C5bFiles.PumpTask(running);
            Assert.True(toasts.Has(I18n.L("向导已完成")));
            Assert.Equal(0, api.Count("set_game_dir"));
        });
    }

    /// <summary>【Windows 源】<c>FirstRunWizard.cs:46-56</c>:<c>first_run</c> 为真才弹;为假一个浮层都不推。</summary>
    [Fact]
    public void Maybe_show_respects_the_first_run_setting()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("get_setting", "false");
            Task maybe = FirstRunWizard.MaybeShowAsync();
            C5bFiles.PumpTask(maybe);
            Assert.Equal(0, C5bFiles.LayerCount());
            C5bFiles.AssertParam(api, "get_setting", "key", "first_run");
        });
    }

    // ================================================================ SkinPng

    /// <summary>【Windows 源】<c>SkinPng.cs:11-26</c>:data URL 只取逗号后面那截再解 base64;坏 base64 返回 null。</summary>
    [Fact]
    public void Skin_png_decode_strips_the_data_url_prefix_and_rejects_garbage()
    {
        byte[] payload = { 1, 2, 3, 4, 5 };
        string base64 = Convert.ToBase64String(payload);
        Assert.Equal(payload, SkinPng.Decode("data:image/png;base64," + base64));
        Assert.Equal(payload, SkinPng.Decode(base64));
        Assert.Equal(payload, SkinPng.Decode("data:image/png;base64,  " + base64));
        Assert.Null(SkinPng.Decode(null));
        Assert.Null(SkinPng.Decode(""));
        Assert.Null(SkinPng.Decode("   "));
        Assert.Null(SkinPng.Decode("data:image/png;base64,这不是 base64"));
    }

    /// <summary>【Windows 源】<c>SkinPng.cs:116-133</c>:小于 64 像素的图认成"皮肤"直接 null。</summary>
    [Fact]
    public void Skin_png_front_view_rejects_images_narrower_than_64()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            // 注入解码口:交出一张 32x32(小于 64 ⇒ 认不出来);作用域 Dispose 会把它还原
            WriteableBitmap small = Solid(32, 32);
            ImageDecode.Decoder = (data, width) => small;
            Assert.Null(SkinPng.FrontView(new byte[] { 9 }, false));
        });
    }

    /// <summary>
    /// 【Windows 源】<c>SkinPng.cs:28-114</c>:64x64 的皮肤拼出来是一张 **128x256** 的正面像
    /// (Windows 的 <c>RenderTargetBitmap(128, 256, …)</c>);64x32 的旧格式也要能出图。
    /// </summary>
    [Fact]
    public void Skin_png_front_view_composes_a_128_by_256_image()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            foreach (bool slim in new bool[] { false, true })
            {
                foreach (PixelSize size in new PixelSize[] { new PixelSize(64, 64), new PixelSize(64, 32) })
                {
                    WriteableBitmap source = Solid(size.Width, size.Height);
                    ImageDecode.Decoder = (data, width) => source;
                    Bitmap? front = SkinPng.FrontView(new byte[] { 9 }, slim);
                    Assert.True(front != null, "slim=" + slim + " / " + size.Width + "x" + size.Height + " 应当拼得出正面像");
                    Assert.Equal(new PixelSize(128, 256), front!.PixelSize);
                }
            }
        });
    }

    /// <summary>【Windows 源】<c>SkinPng.cs:129-132</c>:解不出来(坏字节)不许抛,一律 null。</summary>
    [Fact]
    public void Skin_png_front_view_returns_null_for_undecodable_bytes()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            Assert.Null(SkinPng.FrontView(new byte[] { 1, 2, 3 }, false));
            Assert.Null(SkinPng.FrontView(Array.Empty<byte>(), true));
        });
    }

    // ================================================================ 小工具

    /// <summary>起一次向导并返回它那个还没收尾的任务(调用方负责点按钮 / PumpTask)。</summary>
    private static Task<bool> RunWizard(C3cFakeApi api)
    {
        Task running = FirstRunWizard.ShowAsync();
        C3bFiles.WaitDialog();
        return Wrap(running);

        static async Task<bool> Wrap(Task inner)
        {
            await inner;
            return true;
        }
    }

    /// <summary>等到 <c>Dlg.Ask</c> 那张卡出现并返回它(向导那句 Ask 与别处不一样:它自己就是 <c>DialogCard</c>)。</summary>
    private static DialogCard WaitAsk()
    {
        C3bFiles.WaitDialog();
        return Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
    }

    /// <summary>一张纯色位图(测试用,不落盘)。</summary>
    private static WriteableBitmap Solid(int width, int height)
    {
        WriteableBitmap bitmap = new WriteableBitmap(new PixelSize(width, height), new Avalonia.Vector(96.0, 96.0), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
        using (var fb = bitmap.Lock())
        {
            int stride = fb.RowBytes;
            byte[] row = new byte[stride];
            for (int y = 0; y < height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(row, 0, IntPtr.Add(fb.Address, y * stride), stride);
            }
        }

        return bitmap;
    }
}
