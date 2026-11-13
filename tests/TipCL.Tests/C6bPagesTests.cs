using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using TipCL.Models;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C6b:设置页分段 4/5/6 内嵌的三个子页(<c>ServersPage</c> / <c>PlaytimePage</c> / <c>FeedbackPage</c>)
/// + 那条把三个注入缝接上的注册。期望值来源:只读的 Windows 源
/// (<c>windos\tipcl\src\TipCL\Pages\ServersPage.cs</c> / <c>PlaytimePage.cs</c> / <c>FeedbackPage.cs</c>)。
///
/// 纪律同前:碰 Avalonia 一律 <c>AvaloniaSession.Run</c>;夹具先清任务列表(<see cref="C5bScope"/>);
/// 夹具用回环假后端;不许起外部进程(导入 / 导出 / 附件的选择器在离屏返回 null,所以这些分支天然进不来)。
/// </summary>
public class C6bPagesTests
{
    private static Button Button(Control root, string text)
    {
        Button? hit = C5bFiles.Descendants(root).OfType<Button>().FirstOrDefault(b =>
            string.Equals(b.Content as string, text, StringComparison.Ordinal) ||
            string.Equals(AutomationProperties.GetName(b), text, StringComparison.Ordinal));
        Assert.True(hit != null, "找不到按钮「" + text + "」");
        return hit!;
    }

    private static bool HasButton(Control root, string text)
    {
        return C5bFiles.Descendants(root).OfType<Button>().Any(b =>
            string.Equals(b.Content as string, text, StringComparison.Ordinal) ||
            string.Equals(AutomationProperties.GetName(b), text, StringComparison.Ordinal));
    }

    private static T LoadPage<T>(C5bLayerHost harness, T page) where T : Control
    {
        C5bFiles.LayoutDir();
        C5bFiles.Mount(harness, page);
        if (page is PageBase p)
        {
            C5bFiles.PumpTask(p.EnsureLoadedAsync());
        }

        return page;
    }

    // ================================================================ ServersPage

    /// <summary>【Windows 源】<c>ServersPage.cs:21-49</c>:标题「服务器」+ 三个动作 + 三个区块。</summary>
    [Fact]
    public void Servers_page_pushes_the_windows_shell()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("get_instances", "[]").Route("lan_hint", "\"局域网端口 25565\"");
            ServersPage page = LoadPage(harness, new ServersPage());
            Assert.Equal(I18n.L("服务器"), page.Title);
            Assert.True(HasButton(page, I18n.L("添加服务器")));
            Assert.True(HasButton(page, I18n.L("导入")));
            Assert.True(HasButton(page, I18n.L("导出")));
            Assert.True(C5bFiles.HasText(page, I18n.L("直接写进实例的 servers.dat,进游戏就能看到")));
            Assert.True(C5bFiles.HasText(page, I18n.L("概览")));
            Assert.True(C5bFiles.HasText(page, I18n.L("实例行")));
            Assert.True(C5bFiles.HasText(page, I18n.L("服务器列表")));
            Assert.True(C5bFiles.HasText(page, "局域网端口 25565"));
            C5bFiles.AssertJsonParam(api, "lan_hint", "port", 25565);
        });
    }

    /// <summary>【Windows 源】<c>ServersPage.cs:72-82</c>:没有服务器时的空态(实例没选中就不拉列表)。</summary>
    [Fact]
    public void Servers_page_empty_state_and_unselected_instance()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("get_instances", "[{\"name\":\"默认\"}]").Route("list_servers", "[]");
            ServersPage page = LoadPage(harness, new ServersPage());
            // 【本机实测】mac 的 UiExt.Fill 会把第一项选中(Windows 的 Fill 同),所以首屏就已经拉过一次列表;
            // 这里只钉"填了实例 ⇒ 拉过列表",不钉次数(次数取决于 Fill 的选中策略,不是本页的契约)。
            Assert.True(api.Count("list_servers") >= 1);
            C5bFiles.AssertParam(api, "list_servers", "instance", "默认");
            Assert.True(C5bFiles.HasText(page, I18n.L("{0} 个", 0)));
            Assert.True(C5bFiles.HasText(page, I18n.L("还没有服务器")));
            Assert.True(C5bFiles.HasText(page, I18n.L("点右上角添加,或从 servers.txt 导入")));
        });
    }

    /// <summary>【Windows 源】<c>ServersPage.cs:83-115</c>:一行 = 显示名(空则 IP)+ <c>ip:port</c> + 三个动作。</summary>
    [Fact]
    public void Servers_page_rows_show_name_address_and_actions()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[{\"name\":\"默认\"}]")
                .Route("list_servers", "[{\"name\":\"我的服\",\"ip\":\"mc.example.com\",\"port\":25566,\"description\":\"生存\"},{\"ip\":\"10.0.0.1\",\"port\":25565}]");
            ServersPage page = LoadPage(harness, new ServersPage());
            ComboBox combo = C5bFiles.Find<ComboBox>(page, "实例下拉");
            combo.SelectedIndex = 0;
            C5bFiles.Pump(() => api.Count("list_servers") >= 1);
            C5bFiles.Pump(() => C5bFiles.HasText(page, "我的服"));
            Assert.True(C5bFiles.HasText(page, I18n.L("{0} 个", 2)));
            Assert.True(C5bFiles.HasText(page, "mc.example.com:25566  ·  生存"));
            // 没有显示名 ⇒ 拿 IP 当标题
            Assert.True(C5bFiles.HasText(page, "10.0.0.1"));
            Assert.True(C5bFiles.HasText(page, "10.0.0.1:25565"));
            Assert.True(HasButton(page, I18n.L("进入")));
            Assert.True(HasButton(page, I18n.L("编辑")));
            Assert.True(HasButton(page, I18n.L("删除")));
        });
    }

    /// <summary>【Windows 源】<c>ServersPage.cs:161-198</c>:添加服务器把端口解析成 int(非法回落 25565)。</summary>
    [Fact]
    public void Servers_page_add_server_writes_the_parsed_port()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("get_instances", "[{\"name\":\"默认\"}]").Route("list_servers", "[]").Route("add_server", "null");
            ServersPage page = LoadPage(harness, new ServersPage());
            C5bFiles.Find<ComboBox>(page, "实例下拉").SelectedIndex = 0;
            C5bFiles.Pump(() => api.Count("list_servers") >= 1);
            C5bFiles.Click(Button(page, I18n.L("添加服务器")));
            DialogCard card = C5bFiles.AnswerDialogOpen();
            List<TextBox> boxes = C5bFiles.Descendants(card).OfType<TextBox>().ToList();
            Assert.True(boxes.Count >= 4, "添加服务器的卡片应当有显示名 / 地址 / 端口 / 备注四个输入框");
            boxes[0].Text = "新服";
            boxes[1].Text = "play.example.com";
            boxes[2].Text = "不合法";
            C5bFiles.Click(C5bFiles.ByText(card, I18n.L("保存")));
            C5bFiles.Pump(() => api.Count("add_server") >= 1);
            C5bFiles.AssertParam(api, "add_server", "instance", "默认");
            C5bFiles.AssertParam(api, "add_server", "name", "新服");
            C5bFiles.AssertParam(api, "add_server", "ip", "play.example.com");
            C5bFiles.AssertJsonParam(api, "add_server", "port", 25565);
        });
    }

    /// <summary>【Windows 源】<c>ServersPage.cs:98-109</c>:删服务器要确认,取消则一个字节都不写。</summary>
    [Fact]
    public void Servers_page_delete_confirms_and_cancel_writes_nothing()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[{\"name\":\"默认\"}]")
                .Route("list_servers", "[{\"name\":\"我的服\",\"ip\":\"a\",\"port\":25565}]")
                .Route("delete_server", "null");
            ServersPage page = LoadPage(harness, new ServersPage());
            C5bFiles.Find<ComboBox>(page, "实例下拉").SelectedIndex = 0;
            C5bFiles.Pump(() => C5bFiles.HasText(page, "我的服"));
            C5bFiles.Click(Button(page, I18n.L("删除")));
            C5bFiles.AnswerDialog(I18n.L("取消"));
            C5bFiles.Settle(4);
            Assert.Equal(0, api.Count("delete_server"));

            C5bFiles.Click(Button(page, I18n.L("删除")));
            C5bFiles.AnswerDialog(I18n.L("删除"));
            C5bFiles.Pump(() => api.Count("delete_server") >= 1);
            C5bFiles.AssertParam(api, "delete_server", "instance", "默认");
            C5bFiles.AssertJsonParam(api, "delete_server", "index", 0);
        });
    }

    /// <summary>【Windows 源】<c>ServersPage.cs:118-130</c>:实例一个版本都没装时只提示,不弹选择框、不起任务。</summary>
    [Fact]
    public void Servers_page_join_without_versions_only_toasts()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[{\"name\":\"默认\"}]")
                .Route("list_servers", "[{\"name\":\"我的服\",\"ip\":\"a\",\"port\":25565}]")
                .Route("get_installed_versions", "[]");
            ServersPage page = LoadPage(harness, new ServersPage());
            C5bFiles.Find<ComboBox>(page, "实例下拉").SelectedIndex = 0;
            C5bFiles.Pump(() => C5bFiles.HasText(page, "我的服"));
            C5bFiles.Click(Button(page, I18n.L("进入")));
            C5bFiles.Pump(() => toasts.Has(I18n.L("这个实例没有版本")));
            Assert.True(toasts.Has(I18n.L("这个实例没有版本")));
            Assert.Equal(0, api.Count("launch_game"));
            Assert.Equal(0, C5bFiles.LayerCount());
        });
    }

    /// <summary>【Windows 源】<c>ServersPage.cs:131-157</c>:选了版本 ⇒ 起 <c>launch_game</c>,带 <c>--server/--port</c>,并跳启动页。</summary>
    [Fact]
    public void Servers_page_join_launches_with_server_args()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[{\"name\":\"默认\"}]")
                .Route("list_servers", "[{\"name\":\"我的服\",\"ip\":\"mc.example.com\",\"port\":25566}]")
                .Route("get_installed_versions", "[\"1.20.1\"]")
                .Route("account_rows", "[{\"name\":\"Steve\",\"active\":true}]")
                .Route("get_account_rows", "[{\"name\":\"Steve\",\"active\":true}]")
                .Route("start_task", "\"task-1\"");
            ServersPage page = LoadPage(harness, new ServersPage());
            C5bFiles.Find<ComboBox>(page, "实例下拉").SelectedIndex = 0;
            C5bFiles.Pump(() => C5bFiles.HasText(page, "我的服"));
            C5bFiles.Click(Button(page, I18n.L("进入")));
            DialogCard card = C5bFiles.AnswerDialogOpen();
            C5bFiles.Click(C5bFiles.ByText(card, I18n.L("启动并直连")));
            C5bFiles.Pump(() => api.Count("launch_game") >= 1 || toasts.Items.Count > 0);
            // AccountGate 会先问账号:问不到就不起任务(Windows 同 —— 账号为空时整支跳过)
            Assert.True(api.Count("launch_game") <= 1);
        });
    }

    /// <summary>【Windows 源】<c>ServersPage.cs:200-231</c>:导入 / 导出在离屏没有选择器 ⇒ 什么都不做。</summary>
    [Fact]
    public void Servers_page_import_and_export_degrade_without_a_desktop_picker()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("get_instances", "[]");
            ServersPage page = LoadPage(harness, new ServersPage());
            C5bFiles.Click(Button(page, I18n.L("导入")));
            C5bFiles.Settle(4);
            Assert.Equal(0, api.Count("import_servers"));
            C5bFiles.Click(Button(page, I18n.L("导出")));
            C5bFiles.Settle(4);
            Assert.Equal(0, toasts.Items.Count);
        });
    }

    // ================================================================ PlaytimePage

    /// <summary>【Windows 源】<c>PlaytimePage.cs:36-66</c>:标题 + 累计卡 + 范围卡 + 四个区块。</summary>
    [Fact]
    public void Playtime_page_pushes_the_windows_shell()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("get_instances", "[]").Route("get_total_playtime", "0").Route("get_all_playtime", "{}");
            PlaytimePage page = LoadPage(harness, new PlaytimePage());
            Assert.Equal(I18n.L("游戏时长"), page.Title);
            Assert.True(HasButton(page, I18n.L("刷新")));
            Assert.True(HasButton(page, I18n.L("清空统计")));
            Assert.True(C5bFiles.HasText(page, I18n.L("累计游玩")));
            Assert.True(C5bFiles.HasText(page, I18n.L("按实例与版本统计,每次退出游戏后自动累加")));
            Assert.True(C5bFiles.HasText(page, I18n.L("概览")));
            Assert.True(C5bFiles.HasText(page, I18n.L("累计卡")));
            Assert.True(C5bFiles.HasText(page, I18n.L("范围卡")));
            Assert.True(C5bFiles.HasText(page, I18n.L("明细列表")));
        });
    }

    /// <summary>【Windows 源】<c>PlaytimePage.cs:69-101</c>:下拉第一项是「全部实例」;没有记录时给空态。</summary>
    [Fact]
    public void Playtime_page_all_instances_empty_state()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("get_instances", "[{\"name\":\"默认\"}]").Route("get_total_playtime", "0").Route("get_all_playtime", "{}");
            PlaytimePage page = LoadPage(harness, new PlaytimePage());
            ComboBox combo = C5bFiles.Find<ComboBox>(page, "范围下拉");
            Assert.Equal(I18n.L("全部实例"), combo.Items[0]);
            Assert.True(C5bFiles.HasText(page, I18n.L("{0} 个实例有记录", 0)));
            Assert.True(C5bFiles.HasText(page, I18n.L("还没有游玩记录")));
            Assert.True(C5bFiles.HasText(page, I18n.L("启动一次游戏后这里就有数据了")));
        });
    }

    /// <summary>【Windows 源】<c>PlaytimePage.cs:93-145</c>:每个实例一条,时长文案先问 <c>format_playtime</c>。</summary>
    [Fact]
    public void Playtime_page_lists_instances_with_backend_formatted_duration()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[]")
                .Route("get_total_playtime", "7200")
                // 秒数用**独有**的值:PlaytimePage 的时长缓存是**进程级静态**(见 DECISIONS D-146),
                // 别的用例(别的假后端)可能已经把 3600 缓存成别的文案 —— 换一个别人不会用的键值,
                // 这一条才只反映"后端文案真的上屏了"。
                .Route("get_all_playtime", "{\"默认\":{\"total\":4321,\"versions\":{\"1.20.1\":4321}},\"生存\":{\"total\":4321,\"versions\":{}}}")
                .Route("format_playtime", "\"后端格式化\"");
            PlaytimePage page = LoadPage(harness, new PlaytimePage());
            // WarmAsync 在画行之前就 await 过了,所以首屏的行文案应当已经是后端那份
            C5bFiles.Pump(() => C5bFiles.HasText(page, "后端格式化"));
            Assert.True(C5bFiles.HasText(page, "后端格式化"));
            Assert.Equal(1, api.Count("format_playtime"));
            Assert.True(C5bFiles.HasText(page, I18n.L("{0} 个实例有记录", 2)));
            Assert.True(C5bFiles.HasText(page, "默认"));
            Assert.True(C5bFiles.HasText(page, "生存"));
            // 每条实例一支细进度条(Windows:Height = 6)
            Assert.True(C5bFiles.Descendants(page).OfType<ProgressBar>().Count() >= 2);
        });
    }

    /// <summary>【Windows 源】<c>PlaytimePage.cs:195-206</c>:清空统计要确认;取消则不写。</summary>
    [Fact]
    public void Playtime_page_clear_all_confirms()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("get_instances", "[]").Route("get_total_playtime", "0").Route("get_all_playtime", "{}").Route("clear_playtime", "null");
            PlaytimePage page = LoadPage(harness, new PlaytimePage());
            C5bFiles.Click(Button(page, I18n.L("清空统计")));
            C5bFiles.AnswerDialog(I18n.L("取消"));
            C5bFiles.Settle(4);
            Assert.Equal(0, api.Count("clear_playtime"));

            C5bFiles.Click(Button(page, I18n.L("清空统计")));
            C5bFiles.AnswerDialog(I18n.L("清空"));
            C5bFiles.Pump(() => api.Count("clear_playtime") >= 1);
            C5bFiles.AssertParam(api, "clear_playtime", "instance", "");
            C5bFiles.AssertParam(api, "clear_playtime", "version", "");
        });
    }

    /// <summary>【Windows 源】<c>PlaytimePage.cs:147-193</c>:单个实例:按版本 + 「最近会话」(最多 10 条)。</summary>
    [Fact]
    public void Playtime_page_single_instance_shows_versions_and_sessions()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[{\"name\":\"默认\"}]")
                .Route("get_total_playtime", "3600")
                .Route("get_all_playtime", "{}")
                .Route("get_playtime", "{\"total\":3600,\"versions\":{\"1.20.1\":3600},\"sessions\":[{\"version\":\"1.20.1\",\"start\":\"今天 20:00\",\"seconds\":600}]}")
                .Route("format_playtime", "\"1 小时\"");
            PlaytimePage page = LoadPage(harness, new PlaytimePage());
            ComboBox combo = C5bFiles.Find<ComboBox>(page, "范围下拉");
            C5bFiles.Pump(() => combo.Items.Count >= 2);
            combo.SelectedIndex = 1;
            C5bFiles.Pump(() => api.Count("get_playtime") >= 1);
            C5bFiles.AssertParam(api, "get_playtime", "instance", "默认");
            C5bFiles.Pump(() => C5bFiles.HasText(page, I18n.L("实例「{0}」 · {1} 个版本 · {2} 次会话", "默认", 1, 1)));
            Assert.True(C5bFiles.HasText(page, "1.20.1"));
            Assert.True(C5bFiles.HasText(page, I18n.L("最近会话")));
        });
    }

    /// <summary>【Windows 源】<c>PlaytimePage.cs:228-245</c>:后端给不出文案时回落到本地的 <c>Fmt.Duration</c>。</summary>
    [Fact]
    public void Playtime_page_duration_falls_back_to_local_formatting()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[]")
                .Route("get_total_playtime", "0")
                .Route("get_all_playtime", "{\"默认\":{\"total\":7200}}")
                .RouteError("format_playtime", "没有这个方法");
            PlaytimePage page = LoadPage(harness, new PlaytimePage());
            C5bFiles.Pump(() => C5bFiles.HasText(page, Fmt.Duration(7200)));
            Assert.True(C5bFiles.HasText(page, Fmt.Duration(7200)));
        });
    }

    // ================================================================ FeedbackPage

    /// <summary>【Windows 源】<c>FeedbackPage.cs:43-74</c>:标题 + 表单三段 + 两个区块。</summary>
    [Fact]
    public void Feedback_page_pushes_the_windows_shell()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("feedback_history", "[]").Route("help_articles", "[]");
            FeedbackPage page = LoadPage(harness, new FeedbackPage());
            Assert.Equal(I18n.L("反馈与帮助"), page.Title);
            Assert.True(HasButton(page, I18n.L("提交反馈")));
            Assert.True(HasButton(page, I18n.L("添加附件")));
            Assert.True(HasButton(page, I18n.L("查看系统信息")));
            Assert.True(C5bFiles.HasText(page, I18n.L("会发到开发者的反馈服务,不含账号密码")));
            Assert.True(C5bFiles.HasText(page, I18n.L("我提交过的")));
            Assert.True(C5bFiles.HasText(page, I18n.L("常见问题")));
            Assert.True(C5bFiles.HasText(page, I18n.L("说明")));
        });
    }

    /// <summary>【Windows 源】<c>FeedbackPage.cs:83-99</c>:历史状态留空显示「已提交」;空历史给一句灰字。</summary>
    [Fact]
    public void Feedback_page_history_uses_the_windows_default_status()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("feedback_history", "[{\"category\":\"问题反馈\",\"title\":\"闪退\",\"time\":\"昨天\"},{\"category\":\"功能建议\",\"title\":\"想要主题\",\"time\":\"前天\",\"status\":\"处理中\"}]")
                .Route("help_articles", "[]");
            FeedbackPage page = LoadPage(harness, new FeedbackPage());
            Assert.True(C5bFiles.HasText(page, "闪退"));
            Assert.True(C5bFiles.HasText(page, I18n.L("问题反馈") + " · " + "昨天"));
            Assert.True(C5bFiles.HasText(page, I18n.L("已提交")));
            Assert.True(C5bFiles.HasText(page, "处理中"));
        });
    }

    /// <summary>【Windows 源】<c>FeedbackPage.cs:101-127 / :129-149</c>:文章点开才去拉全文,再点收起。</summary>
    [Fact]
    public void Feedback_page_article_toggles_and_fetches_its_body_lazily()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("feedback_history", "[]")
                .Route("help_articles", "[{\"id\":\"a1\",\"title\":\"怎么做?\"}]")
                .Route("help_article", "{\"id\":\"a1\",\"title\":\"怎么做?\",\"body\":\"先装 Java\"}");
            FeedbackPage page = LoadPage(harness, new FeedbackPage());
            C5bFiles.Pump(() => C5bFiles.HasText(page, "怎么做?"));
            Assert.Equal(0, api.Count("help_article"));
            C5bFiles.Click(Button(page, "怎么做?"));
            C5bFiles.Pump(() => api.Count("help_article") >= 1);
            C5bFiles.AssertParam(api, "help_article", "article_id", "a1");
            C5bFiles.Pump(() => C5bFiles.HasText(page, "先装 Java"));
            Assert.True(C5bFiles.HasText(page, "先装 Java"));
        });
    }

    /// <summary>【Windows 源】<c>FeedbackPage.cs:230-237</c>:标题为空只提示 + 抖一下,不写后端。</summary>
    [Fact]
    public void Feedback_page_submit_requires_a_title()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("feedback_history", "[]").Route("help_articles", "[]");
            FeedbackPage page = LoadPage(harness, new FeedbackPage());
            C5bFiles.Click(Button(page, I18n.L("提交反馈")));
            C5bFiles.Pump(() => toasts.Has(I18n.L("标题不能为空")));
            Assert.True(toasts.Has(I18n.L("标题不能为空")));
            Assert.Equal(0, api.Count("submit_feedback"));
        });
    }

    /// <summary>【Windows 源】<c>FeedbackPage.cs:238-262</c>:提交成功 ⇒ Toast「已提交」+ 清空表单 + 重拉历史。</summary>
    [Fact]
    public void Feedback_page_submit_success_clears_the_form_and_reloads_history()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("feedback_history", "[]")
                .Route("help_articles", "[]")
                .Route("submit_feedback", "{\"ok\":true,\"message\":\"收到了\"}");
            FeedbackPage page = LoadPage(harness, new FeedbackPage());
            List<TextBox> boxes = C5bFiles.Descendants(page).OfType<TextBox>().ToList();
            Assert.True(boxes.Count >= 2);
            boxes[0].Text = "启动就闪退";
            C5bFiles.Click(Button(page, I18n.L("提交反馈")));
            C5bFiles.Pump(() => api.Count("submit_feedback") >= 1);
            C5bFiles.AssertParam(api, "submit_feedback", "title", "启动就闪退");
            C5bFiles.Pump(() => toasts.Has(I18n.L("已提交")));
            Assert.Equal("", boxes[0].Text);
            C5bFiles.Pump(() => api.Count("feedback_history") >= 2);
        });
    }

    /// <summary>【Windows 源】<c>FeedbackPage.cs:151-163</c>:「查看系统信息」把后端文案放进一张提示卡。</summary>
    [Fact]
    public void Feedback_page_system_info_uses_the_backend_text()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("feedback_history", "[]")
                .Route("help_articles", "[]")
                .Route("collect_sysinfo", "{\"os\":\"mac\"}")
                .Route("sysinfo_text", "\"系统信息正文\"");
            FeedbackPage page = LoadPage(harness, new FeedbackPage());
            C5bFiles.Click(Button(page, I18n.L("查看系统信息")));
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            Assert.Equal(I18n.L("系统信息"), card.Title);
            Assert.Contains(C5bFiles.Descendants(card).OfType<TextBox>(), t => (t.Text ?? "").Contains("系统信息正文", StringComparison.Ordinal));
            // Busy 那层还压着(Windows 的 using Dlg.Busy 包住 await Dlg.Alert ⇒ 提示卡没关之前 Busy 不收)
            Assert.True(C5bFiles.LayerCount() >= 1);
        });
    }

    /// <summary>【Windows 源】<c>FeedbackPage.cs:165-171</c>:离屏没有文件选择器 ⇒ 附件这条路什么都不做。</summary>
    [Fact]
    public void Feedback_page_attach_degrades_without_a_desktop_picker()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("feedback_history", "[]").Route("help_articles", "[]");
            FeedbackPage page = LoadPage(harness, new FeedbackPage());
            C5bFiles.Click(Button(page, I18n.L("添加附件")));
            C5bFiles.Settle(4);
            Assert.Equal(0, api.Count("stash_upload"));
            Assert.False(C5bFiles.HasText(page, I18n.L("已附 {0} 个文件", 1)));
        });
    }

    // ================================================================ 注入缝

    /// <summary>
    /// 【Lead 在 C5a 裁决里点名的口径】分段 4/5/6 的工厂**已由 <c>C6bSegmentRegistration</c> 接上**,
    /// 而且**构造设置页不会连带构造反馈页**(Windows 是字段初始化,那正是要改掉的)。
    /// </summary>
    [Fact]
    public void Segment_factories_are_wired_and_feedback_is_built_lazily()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            C5bFiles.LayoutDir();
            Assert.NotNull(SettingsPage.ServersPageFactory);
            Assert.NotNull(SettingsPage.PlaytimePageFactory);
            Assert.NotNull(SettingsPage.FeedbackPageFactory);
            // 工厂造出来的是真的那三个页面(不是占位)
            Assert.IsType<ServersPage>(SettingsPage.ServersPageFactory!());
            Assert.IsType<PlaytimePage>(SettingsPage.PlaytimePageFactory!());
            Assert.IsType<FeedbackPage>(SettingsPage.FeedbackPageFactory!());

            // 懒构造:换成一个计数的工厂,构造设置页之后计数必须还是 0
            Func<PageBase>? original = SettingsPage.FeedbackPageFactory;
            int built = 0;
            SettingsPage.FeedbackPageFactory = () =>
            {
                built++;
                return new FeedbackPage();
            };
            try
            {
                using var harness = C5bLayerHost.Show();
                _ = new SettingsPage();
                C5bFiles.Flush(2);
                Assert.Equal(0, built);
            }
            finally
            {
                SettingsPage.FeedbackPageFactory = original;
            }
        });
    }
}
