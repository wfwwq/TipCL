// M4-H R3 测试:手机同步面板。
//
// 真机同步要一台连着手机的环境(接线/同网),这里覆盖不需要手机的那部分:
// 面板在「没连接」时的诚实提示、裁决文案与时间格式、以及 Core 的本地目标枚举(不碰网络)。
// 对比/传输链的语义由 Core 的 PhoneLink/SaveSync 承担(逐项 TXT 远侧不在此列)。
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using TipCL.App.Common;
using TipCL.App.Pages.Settings;
using TipCL.Core.Launcher.Sync;
using Xunit;

namespace TipCL.Tests.App;

public class PhoneSyncPanelTests
{
    [Fact]
    public void Panel_ConstructsAndTellsUserToConnectFirst() => TestApp.Run(() =>
    {
        // 没连接手机时面板不该假装能对比,而是明确让用户先去设置页连接
        PhoneLink.Disconnect();
        var panel = new PhoneSyncPanel();
        Assert.NotNull(panel.Child);
        Assert.Contains("还没有连接手机", panel.StatusText);
    });

    [Fact]
    public void DecisionLabelsCoverEveryDecisionCase()
    {
        SyncDecision[] all = Enum.GetValues<SyncDecision>();
        var labels = all.Select(PhoneSyncPanel.DecisionLabel).ToList();
        Assert.Equal(all.Length, labels.Distinct().Count());
        Assert.All(labels, l => Assert.False(string.IsNullOrWhiteSpace(l)));
        // 关键文案与 Windows 版一致
        Assert.Equal("一致", PhoneSyncPanel.DecisionLabel(SyncDecision.Same));
        Assert.Equal("电脑较新", PhoneSyncPanel.DecisionLabel(SyncDecision.Push));
        Assert.Equal("手机较新", PhoneSyncPanel.DecisionLabel(SyncDecision.Pull));
        Assert.Equal("两边都改过", PhoneSyncPanel.DecisionLabel(SyncDecision.Conflict));
        Assert.Equal("只在电脑", PhoneSyncPanel.DecisionLabel(SyncDecision.OnlyLocal));
        Assert.Equal("只在手机", PhoneSyncPanel.DecisionLabel(SyncDecision.OnlyRemote));
    }

    [Fact]
    public void TimeTextShowsDashForUnknown()
    {
        Assert.Equal("-", PhoneSyncPanel.TimeText(0));
        Assert.Equal("-", PhoneSyncPanel.TimeText(-5));
        DateTime local = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000L).LocalDateTime;
        Assert.Equal(local.ToString("yyyy-MM-dd HH:mm"), PhoneSyncPanel.TimeText(1_700_000_000_000L));
    }

    [Fact]
    public void TargetsEnumerateLocalInstancesWithoutNetwork()
    {
        // Targets() 只读本机实例(不联网):至少要能返回一个列表且每项都有存档目录字段
        List<SaveSync.Target> targets = SaveSync.Targets();
        Assert.NotNull(targets);
        Assert.All(targets, t => Assert.NotNull(t.SavesDir));
    }

    /// <summary>
    /// R30-WP3(R3):把**上一个用例泄漏**过来的对话框模态状态重置回干净值。
    ///
    /// <c>Dlg.AnyOpen</c>(Common/Dlg.cs:24)是**进程级静态量**,只在对话框自己的 <c>Closed</c>
    /// 处理器 / <c>await ShowDialog</c> 之后那两处被清回 false(见 Dlg.cs:84-86、123-125、153-155、
    /// 184-185、246-248)。于是有两条泄漏路径:
    ///   1) 某个用例开了窗却没关 —— true 一直留着;
    ///   2) <c>ShowDialog</c> 自己抛异常(典型:owner 不可见,D-40 那条
    ///      「Cannot show window with non-visible owner」)—— 赋值在 84/123/153/246 行、
    ///      清理在 86/125/155/248 行,**中间没有 try/finally**,抛出去就再也没人清。
    /// 无论哪条,接下来任何断言 <c>Assert.False(Dlg.AnyOpen)</c> 的用例都会被动变红。R30-WP0 §2.4
    /// 记的就是这条:3 轮里红 2 轮,断言完全相同(Expected: False, Actual: True)。
    ///
    /// 这里做的是**重置**,不是放宽断言:下面的 <c>Assert.False</c> 一条不少、也没加 skip。
    /// 先关掉还开着的对话框窗口(正常路径下关窗会触发 Closed 处理器),关不掉的那种(路径 2:
    /// 窗口从没真正显示过,Close 不会触发 Closed)再把静态标志直接复位。
    /// 产品侧的根治办法是给 Dlg 的那四处加上 try/finally —— 不在本包写范围(Dlg.cs 归 WP-1/WP-2 之外
    /// 的负责人),已写进 R30-WP3 报告的建议条目。
    /// </summary>
    private static void ResetLeakedDlgState()
    {
        for (int i = 0; i < 20 && Dlg.AnyOpen; i++)
        {
            foreach (var w in LeakedDialogWindows())
            {
                try { w.Close(); }
                catch (Exception) { }
            }
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
        }
        if (Dlg.AnyOpen)
        {
            // 路径 2 兜底:把 <AnyOpen>k__BackingField 置 false。
            // 用反射只是因为 AnyOpen 的 setter 是 private(Dlg.cs:24),测试拿不到公开复位口。
            var f = typeof(Dlg).GetField("<AnyOpen>k__BackingField",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            f?.SetValue(null, false);
        }
    }

    /// <summary>当前还开着的对话框窗口:宿主窗口的 OwnedWindows + 桌面生命周期的窗口(去掉主窗口)。</summary>
    private static List<Window> LeakedDialogWindows()
    {
        var open = new List<Window>();
        if (Dlg.Owner != null)
            open.AddRange(Dlg.Owner.OwnedWindows);
        if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var w in desktop.Windows)
                if (!ReferenceEquals(w, desktop.MainWindow))
                    open.Add(w);
        }
        return open.Distinct().ToList();
    }

    [Fact]
    public void SyncPanelEntry_RefusesWhenNotConnected() => TestApp.Run(() =>
    {
        // 没连接手机时点「同步面板」只给提示,不开一个空面板
        PhoneLink.Disconnect();
        // R3:断言前先把上个用例可能留下的静态模态状态重置掉,让它不依赖全局泄漏
        ResetLeakedDlgState();
        Assert.False(Dlg.AnyOpen);
        SettingsPage.OpenPhoneSyncPanel();
        Assert.False(Dlg.AnyOpen);
    });

    [Fact]
    public void SyncDecisionEnumMatchesReferenceCases()
    {
        // 六个裁决与 Windows 版一致(两边共用同一套判定语义)
        string[] keys = Enum.GetNames<SyncDecision>();
        Assert.Contains("Same", keys);
        Assert.Contains("Push", keys);
        Assert.Contains("Pull", keys);
        Assert.Contains("Conflict", keys);
        Assert.Contains("OnlyLocal", keys);
        Assert.Contains("OnlyRemote", keys);
        Assert.Equal(6, keys.Length);
    }
}
