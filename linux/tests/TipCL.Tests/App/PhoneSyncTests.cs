// M4-H R3 测试:手机同步面板。
//
// 真机同步要一台连着手机的环境(接线/同网),这里覆盖不需要手机的那部分:
// 面板在「没连接」时的诚实提示、裁决文案与时间格式、以及 Core 的本地目标枚举(不碰网络)。
// 对比/传输链的语义由 Core 的 PhoneLink/SaveSync 承担(逐项 TXT 远侧不在此列)。
using System;
using System.Collections.Generic;
using System.Linq;
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

    [Fact]
    public void SyncPanelEntry_RefusesWhenNotConnected() => TestApp.Run(() =>
    {
        // 没连接手机时点「同步面板」只给提示,不开一个空面板
        PhoneLink.Disconnect();
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
