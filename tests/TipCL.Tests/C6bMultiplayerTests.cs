using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TipCL.Models;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C6b:`MultiplayerPage`(导航 id <c>multiplayer</c>,「陶瓦联机」那一页)。
/// 期望值来源:只读的 Windows 源 <c>windos\tipcl\src\TipCL\Pages\MultiplayerPage.cs</c>(625 行)。
///
/// 纪律同前:碰 Avalonia 一律 <c>AvaloniaSession.Run</c>;夹具用回环假后端;剪贴板走**注入缝**
/// (不碰真剪贴板);轮询计时器靠 <c>OnHidden</c> 停掉,不留后台。
/// </summary>
public class C6bMultiplayerTests
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

    private static MultiplayerPage LoadPage(C5bLayerHost harness, string snapshot)
    {
        C5bFiles.LayoutDir();
        MultiplayerPage page = new MultiplayerPage();
        C5bFiles.Mount(harness, page);
        C5bFiles.PumpTask(page.EnsureLoadedAsync());
        return page;
    }

    private static C3cFakeApi Api(string snapshot)
    {
        return new C3cFakeApi()
            .Route("lan_hint", "\"局域网提示\"")
            .Route("local_ips", "[]")
            .Route("terracotta_snapshot", snapshot)
            .Route("terracotta_idle", "null")
            .Route("terracotta_host", "null")
            .Route("terracotta_join", "null")
            .Route("terracotta_prepare", "\"task-prep\"");
    }

    /// <summary>【Windows 源】<c>:71-115</c>:标题「联机」+ 六个区块 + 三个防火墙动作 + 项目主页。</summary>
    [Fact]
    public void Multiplayer_page_pushes_the_windows_shell()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = Api("{\"state\":\"idle\",\"installed\":true}");
            MultiplayerPage page = LoadPage(harness, "{}");
            Assert.Equal(I18n.L("联机"), page.Title);
            Assert.True(C5bFiles.HasText(page, I18n.L("陶瓦联机")));
            Assert.True(C5bFiles.HasText(page, I18n.L("输入邀请码即可加入。陶瓦是 EasyTier P2P 打洞,不是 FRP 隧道;") + I18n.L("会与 HMCL 一样带上自定义会合节点。PCL 房间号不互通,局域网请用下面的地址。")));
            Assert.True(HasButton(page, I18n.L("允许访问")));
            Assert.True(HasButton(page, I18n.L("打开设置")));
            Assert.True(HasButton(page, I18n.L("关闭内核")));
            Assert.True(HasButton(page, I18n.L("Terracotta 项目主页")));
            foreach (string block in new[] { "说明", "状态卡", "房间卡", "操作区", "玩家列表", "节点说明" })
            {
                Assert.True(C5bFiles.HasText(page, I18n.L(block)), "缺少区块标题「" + block + "」");
            }
        });
    }

    /// <summary>【Windows 源】<c>:184-214 / :322-326</c>:<c>missing</c> ⇒ 明细三句 + 动作区给「下载」。</summary>
    [Fact]
    public void Multiplayer_page_missing_state_offers_the_download_action()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = Api("{\"supported\":true,\"installed\":false,\"running\":false,\"state\":\"missing\"}");
            MultiplayerPage page = LoadPage(harness, "{}");
            // 诊断:`EnsureLoadedAsync` 会把载入期的异常吞掉并 Toast,所以先把 Toast 摊开看
            Assert.True(toasts.Items.Count == 0, "载入期抛了:" + string.Join(" | ", toasts.Items.Select(t => t.Title + " / " + t.Body)));
            Assert.Contains(C5bFiles.Texts(page), t => (t.Text ?? "").Contains(I18n.L("内核未安装"), StringComparison.Ordinal));
            Assert.Contains(C5bFiles.Texts(page), t => (t.Text ?? "").Contains(I18n.L("内核未运行"), StringComparison.Ordinal));
            Assert.True(C5bFiles.HasText(page, I18n.L("下载陶瓦联机内核")));
            Assert.True(HasButton(page, I18n.L("下载")));
            // 玩家标题与房间卡默认藏着(Windows:Visibility.Collapsed)。
            // 注意:藏起来的 TextBlock **仍在树里**(Avalonia 的 IsVisible 只是控件自己的标志),
            // 所以断言要看那个控件自己的 IsVisible,不能"找不到就算藏着"。
            TextBlock playersTitle = C5bFiles.Texts(page).First(t => string.Equals(t.Text, I18n.L("房间成员"), StringComparison.Ordinal));
            Assert.False(playersTitle.IsVisible);
            TextBlock roomLabel = C5bFiles.Texts(page).First(t => string.Equals(t.Text, I18n.L("邀请码"), StringComparison.Ordinal));
            Assert.False(roomLabel.GetVisualAncestors().OfType<Border>().First().IsVisible);
        });
    }

    /// <summary>【Windows 源】<c>:327-332</c>:<c>waiting</c> ⇒ 「我想当房主」+「我想当房客」两张卡。</summary>
    [Fact]
    public void Multiplayer_page_waiting_state_offers_host_and_join()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = Api("{\"supported\":true,\"installed\":true,\"running\":true,\"state\":\"waiting\"}");
            MultiplayerPage page = LoadPage(harness, "{}");
            Assert.True(C5bFiles.HasText(page, I18n.L("我想当房主")));
            Assert.True(C5bFiles.HasText(page, I18n.L("我想当房客")));
            Assert.True(HasButton(page, I18n.L("创建")));
            Assert.True(HasButton(page, I18n.L("加入")));
        });
    }

    /// <summary>【Windows 源】<c>:241-258 / :335-344</c>:<c>host-ok</c> + 房间号 ⇒ 房间卡显示邀请码,并自动复制一次。</summary>
    [Fact]
    public void Multiplayer_page_host_ok_shows_the_room_and_auto_copies_it()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = Api("{\"supported\":true,\"installed\":true,\"running\":true,\"state\":\"host-ok\",\"room\":\"U/1234-5678-9012-3456\"}");
            List<string> copied = new List<string>();
            Func<Control, string, Task<bool>>? original = CrashUi.ClipboardWriter;
            CrashUi.ClipboardWriter = (owner, text) =>
            {
                copied.Add(text);
                return Task.FromResult(true);
            };
            try
            {
                MultiplayerPage page = LoadPage(harness, "{}");
                Assert.True(C5bFiles.HasText(page, I18n.L("邀请码")));
                Assert.True(C5bFiles.HasText(page, "U/1234-5678-9012-3456"));
                Assert.True(HasButton(page, I18n.L("复制邀请码")));
                // 刚进 host-ok ⇒ 自动复制一次(Windows:Copy(s.Room, "已把邀请码复制到剪贴板"))
                C5bFiles.Pump(() => copied.Count >= 1);
                Assert.Equal("U/1234-5678-9012-3456", copied[0]);
                C5bFiles.Pump(() => toasts.Has(I18n.L("已把邀请码复制到剪贴板")));
                Assert.True(C5bFiles.HasText(page, I18n.L("退出")));
            }
            finally
            {
                CrashUi.ClipboardWriter = original;
            }
        });
    }

    /// <summary>【Windows 源】<c>:293-302</c>:玩家列表(房主/成员标签 + ThumbTile),签名没变不重画。</summary>
    [Fact]
    public void Multiplayer_page_renders_players_with_the_host_tag()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = Api("{\"supported\":true,\"state\":\"guest-ok\",\"profiles\":[{\"name\":\"Steve\",\"kind\":\"HOST\",\"vendor\":\"1.20.1\"},{\"name\":\"Alex\",\"kind\":\"GUEST\"}]}");
            MultiplayerPage page = LoadPage(harness, "{}");
            C5bFiles.Pump(() => C5bFiles.HasText(page, "Steve"));
            Assert.True(C5bFiles.HasText(page, I18n.L("房间成员")));
            Assert.True(C5bFiles.HasText(page, "Steve"));
            Assert.True(C5bFiles.HasText(page, "Alex"));
            Assert.True(C5bFiles.HasText(page, "1.20.1"));
            Assert.True(C5bFiles.HasText(page, I18n.L("房主")));
            // guest-ok 的动作区:进入世界 + 退出(Windows 同)
            Assert.True(HasButton(page, I18n.L("进入")));
            Assert.True(HasButton(page, I18n.L("退出")));
        });
    }

    /// <summary>【Windows 源】<c>:503-514</c>:「加入」问邀请码;填了就写 <c>terracotta_join</c>,空着不写。</summary>
    [Fact]
    public void Multiplayer_page_join_writes_the_room_from_the_prompt()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = Api("{\"supported\":true,\"installed\":true,\"running\":true,\"state\":\"waiting\"}");
            MultiplayerPage page = LoadPage(harness, "{}");
            C5bFiles.Click(Button(page, I18n.L("加入")));
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            List<TextBox> boxes = C5bFiles.Descendants(card).OfType<TextBox>().ToList();
            Assert.True(boxes.Count >= 1, "邀请码输入框应当在卡片里");
            boxes[0].Text = "U/AAAA-BBBB";
            C5bFiles.Click(C5bFiles.ByText(card, I18n.L("确定")));
            C5bFiles.Pump(() => api.Count("terracotta_join") >= 1);
            C5bFiles.AssertParam(api, "terracotta_join", "room", "U/AAAA-BBBB");
        });
    }

    /// <summary>【Windows 源】<c>:484-501</c> + <c>:171-180</c>:没在游戏里开房要先确认(<c>terracotta_host</c> 在确认之后);</summary>
    [Fact]
    public void Multiplayer_page_host_confirms_when_the_game_is_not_running()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = Api("{\"supported\":true,\"installed\":true,\"running\":true,\"state\":\"waiting\",\"game_running\":false}")
                .Route("terracotta_player", "\"Steve\"");
            MultiplayerPage page = LoadPage(harness, "{}");
            C5bFiles.Click(Button(page, I18n.L("创建")));
            C5bFiles.AnswerDialog(I18n.L("游戏已启动"));
            C5bFiles.Pump(() => api.Count("terracotta_host") >= 1);
            Assert.Equal(1, api.Count("terracotta_host"));
        });
    }

    /// <summary>【Windows 源】<c>:171-180 / :469-482</c>:支持 / 已装 / 没在跑 ⇒ 显示时自动准备一次(起 <c>terracotta_prepare</c>)。</summary>
    [Fact]
    public void Multiplayer_page_auto_prepares_once_when_shown()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = Api("{\"supported\":true,\"installed\":true,\"running\":false,\"state\":\"idle\"}");
            MultiplayerPage page = LoadPage(harness, "{}");
            page.OnShown();
            C5bFiles.Pump(() => api.Count("terracotta_prepare") >= 1);
            Assert.Equal(1, api.Count("terracotta_prepare"));
            // 再显示一次不重入(Windows:_autoStarted 挡住)
            page.OnShown();
            C5bFiles.Settle(4);
            Assert.Equal(1, api.Count("terracotta_prepare"));
            page.OnHidden();
            C5bFiles.Pump(() => toasts.Has(I18n.L("正在准备联机内核")));
        });
    }
}
