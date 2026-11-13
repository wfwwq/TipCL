using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TipCL;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C3d(二):可达性与回归 —— **后续 13 处页面调用点**要用的那条路真的通、null 安全、
/// 以及 C3c 记的那条"用户可见降级"确实没了。
///
/// 统一调用形状(本包标准化的就是这一条,**逐字等于 Windows 源**):
/// <c>Win?.FlyToTasks(anchor, text)</c> —— <c>PageBase.Win</c> 早就是 <c>internal static MainWindow?</c>(C2b),
/// 所以 12 处 <c>PageBase.Win?.FlyToTasks(...)</c> 一字不改就能编;
/// 只有 <c>InstallPage.cs:489</c> 那一种形状(<c>AppServices.Window?.FlyToTasks(...)</c>)在 mac 侧要多一次 cast,
/// 因为 <c>AppServices.Window</c> 的静态类型是 Avalonia 的 <c>Window?</c>(D-88)。
///
/// 纪律与来源标注同 <see cref="C3dFlyTests"/>。
/// </summary>
public class C3dReachTests
{
    // ---------------------------------------------------------------- 可达性

    /// <summary>【Windows 源】<c>PageBase.Win</c> 就是外壳(<c>AppServices.Window as MainWindow</c>)。</summary>
    [Fact]
    public void Page_win_points_at_the_shell()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();

            Assert.Same(shell.Window, AppServices.Window);
            Assert.NotNull(PageBase.Win);
            Assert.Same(shell.Window, PageBase.Win);
        });
    }

    /// <summary>【任务书】页面的统一调用形状真的到得了外壳成员(时钟开着 ⇒ 宿主里真的多一个 chip)。</summary>
    [Fact]
    public void Page_call_shape_reaches_the_shell_member()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();
            using var clock = new C3dClock();
            var page = new FlyProbePage();

            page.FlyFromPage(shell.Window.Sidebar, "来自页面");

            Assert.Equal(1, page.Calls);
            Border chip = C3dFiles.OnlyChip(shell.Window);
            Assert.Equal("来自页面", C3dFiles.ChipText(chip).Text);
        });
    }

    /// <summary>【任务书】没有窗口时 <c>Win?</c> 的 null 安全:调了什么都不发生、也不抛。</summary>
    [Fact]
    public void Page_call_shape_is_null_safe_without_a_window()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            AppServices.Window = null;
            var page = new FlyProbePage();

            Assert.False(page.WinVisible);
            page.FlyFromPage(new Border { Width = 10.0, Height = 10.0 });
            page.FlyFromAppServices(new Border { Width = 10.0, Height = 10.0 });

            Assert.Equal(1, page.Calls);
        });
    }

    /// <summary>【本机实测】窗口不是外壳时 <c>Win</c> 是 null(不误 cast、不炸)。</summary>
    [Fact]
    public void Page_win_is_null_when_the_window_is_not_the_shell()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var ui = UiHost.Show(new Grid());
            var page = new FlyProbePage();

            Assert.False(page.WinVisible);
            page.FlyFromPage(new Border { Width = 10.0, Height = 10.0 });

            Assert.Equal(1, page.Calls);
        });
    }

    /// <summary>【Windows 源】<c>InstallPage.cs:489</c> 那一种形状:cast 一次之后同样到得了外壳成员。</summary>
    [Fact]
    public void AppServices_window_shape_reaches_the_member_after_one_cast()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();
            using var clock = new C3dClock();
            var page = new FlyProbePage();

            page.FlyFromAppServices(shell.Window.Sidebar);

            Border chip = C3dFiles.OnlyChip(shell.Window);
            Assert.Equal("来自 AppServices", C3dFiles.ChipText(chip).Text);
        });
    }

    // ---------------------------------------------------------------- 接缝接线 / 回归

    /// <summary>
    /// 【C3d】外壳构造函数把 C3c 留的 <c>LaunchPage.FlyToTasksSeam</c> 接到真成员上 ——
    /// 接缝的签名没动,但它的行为不再是"没接就不播"。
    /// </summary>
    [Fact]
    public void Shell_wires_the_launch_page_seam_to_the_real_member()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();
            using var clock = new C3dClock();

            Assert.NotNull(LaunchPage.FlyToTasksSeam);
            LaunchPage.FlyToTasksSeam!(shell.Window.Sidebar, "修复中");

            Border chip = C3dFiles.OnlyChip(shell.Window);
            Assert.Equal(I18n.L("修复中"), C3dFiles.ChipText(chip).Text);
        });
    }

    /// <summary>【C3d 泄漏纪律】接缝是静态的,关窗必须解绑(不能一直拎着一个死窗口)。</summary>
    [Fact]
    public void Shell_unbinds_the_seam_when_the_window_closes()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var window = new MainWindow(new UiPrefs());
            Assert.NotNull(LaunchPage.FlyToTasksSeam);
            window.Show();
            UiHost.Flush();

            window.Close();
            UiHost.Flush();

            Assert.Null(LaunchPage.FlyToTasksSeam);
        });
    }

    /// <summary>
    /// 【回归】LaunchPage 的「修复当前版本」走到 <c>FlyToTasksSeam</c>,而且**文案与 anchor 都对**
    /// (这正是 C3c 那条降级所欠的那一步;接缝在这里换成探针,免得依赖"飞行是否真的播"这条时序)。
    /// </summary>
    [Fact]
    public void Repair_path_invokes_the_fly_seam()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = C3dFiles.LaunchApi();
            var page = new LaunchPage();
            using var dlg = DlgHarness.Show();
            using var ui = UiHost.Show(page);
            C3dFiles.Load(page);
            List<(Control Source, string Text)> calls = new List<(Control, string)>();
            LaunchPage.FlyToTasksSeam = delegate(Control source, string text)
            {
                calls.Add((source, text));
            };

            Repair(page);

            C3cFiles.Pump(() => calls.Count == 1);
            Assert.Equal(I18n.L("修复中"), calls[0].Text);
            Assert.Same(page.LaunchButtonNow, calls[0].Source);
            Assert.Equal(1, api.Count("repair_version"));
        });
    }

    /// <summary>
    /// 【回归 · 端到端】启动页长在**真外壳**里时,「修复当前版本」之后的飞行真的落在外壳的 <c>FlyHost</c> 上,
    /// 而且飞完自动摘掉 —— 这就是 C3c 文档里那条"用户可见降级"消失的证据。
    /// </summary>
    [Fact]
    public void Repair_path_flies_through_the_real_shell_member()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var api = C3dFiles.LaunchApi();
            var page = new LaunchPage();
            using var registry = new RegistryScope().Register("launch", () => page);
            using var shell = ShellFixture.Create();
            Assert.Same(page, shell.Window.CurrentPage);
            using var clock = new C3dClock();
            C3dFiles.Load(page);
            C3cFiles.Settle(12);

            Repair(page);

            C3cFiles.Pump(() => shell.Window.FlyHostPanel.Children.Count == 1);
            Border chip = C3dFiles.OnlyChip(shell.Window);
            Assert.Equal(I18n.L("修复中"), C3dFiles.ChipText(chip).Text);
            C3dFiles.AssertBackground(chip, C3dExpected.DefaultColourKey);
            // 飞完自己摘掉(不泄漏)。
            C3cFiles.Pump(() => shell.Window.FlyHostPanel.Children.Count == 0);
            Assert.Equal(1, api.Count("repair_version"));
        });
    }

    /// <summary>
    /// 驱动「更多操作」菜单里的「修复当前版本」:菜单项走 <c>RaiseEvent</c>(不模拟真实输入设备,D-115),
    /// 弹出的确认卡走 <c>C2c2Files.Dialog()</c> + 点「确定」("先把卡片推出来再点",D-113)。
    /// </summary>
    private static void Repair(LaunchPage page)
    {
        page.ShowMoreMenu(page.LaunchButtonNow);
        C3cFiles.Settle();
        MenuItem item = Assert.IsType<ContextMenu>(page.MoreMenuNow).Items.OfType<MenuItem>()
            .Single(i => (i.Header as string) == I18n.L("修复当前版本"));
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        DialogCard card = C2c2Files.Dialog();
        Assert.Equal(I18n.L("修复版本"), card.Title);
        C2c2Files.Click(card, I18n.L("确定"));
    }
}
