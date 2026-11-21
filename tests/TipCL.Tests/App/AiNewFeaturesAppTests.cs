// AI-new-features 包的界面测试:启动页「启动前 AI 预检」提示条 / 设置开关 / AI 页「导出诊断报告」入口。
// 预检走 LaunchPreflightHook.Override 注入,不碰真实实例与 Java;不联网。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TipCL.App.Pages.Ai;
using TipCL.App.Pages.Launch;
using TipCL.App.Pages.Settings;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.App;

[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class AiNewFeaturesAppTests
{
    private static PyDict Report(string level) => PyDict.Of(
        "ok", level != "error",
        "instance", "demo",
        "version", "1.20.1",
        "memory_mb", 4096L,
        "java", "自动选择",
        "summary", "发现问题",
        "error_count", level == "error" ? 1L : 0L,
        "warn_count", level == "warn" ? 1L : 0L,
        "items", level == "ok"
            ? new List<object>()
            : new List<object> { PyDict.Of("level", level, "code", "dep_missing", "title", "缺少前置模组", "detail", "sodium 需要 fabric-api", "fix", "装 fabric-api") });

    private static IEnumerable<string> Texts(ILogical root)
        => root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "");

    /// <summary>按钮按无障碍名找(UiClass.Btn 把文案同时写进 AutomationProperties.Name)。</summary>
    private static Button Btn(ILogical root, string text)
        => root.GetLogicalDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == text);

    private static void WithPreflight(Func<string, string, long, PyDict> hook, bool enabled, Action body)
    {
        object? saved = Config.CONFIG.Get(AiPreflight.ENABLED_KEY);
        LaunchPreflightHook.Override = hook;
        Config.CONFIG.Set(AiPreflight.ENABLED_KEY, enabled);
        try
        {
            body();
        }
        finally
        {
            LaunchPreflightHook.Override = null;
            if (saved == null) Config.CONFIG.data.Remove(AiPreflight.ENABLED_KEY);
            else Config.CONFIG.Set(AiPreflight.ENABLED_KEY, saved);
            AiPage.QueuePrompt("");
        }
    }

    [Fact]
    public void Gate_Error_BlocksLaunch_ShowsBar_AndHandsResultToAi()
        => WithPreflight((_, _, _) => Report("error"), true, () => TestApp.RunAsync(async () =>
        {
            var page = new LaunchPage();
            bool go = await page.AiPreflightGateAsync("demo", "1.20.1", 4096);
            Assert.False(go);
            Border bar = page.PreflightBar!;
            Assert.True(bar.IsVisible);
            var texts = Texts(bar).ToList();
            Assert.Contains(texts, t => t.Contains("缺少前置模组"));
            Button fix = Btn(bar, "交给 AI 一键修复");
            Button force = Btn(bar, "仍然启动");
            Assert.True(force.IsVisible);
            fix.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("缺少前置模组", AiPage.PendingPrompt);
            Assert.Contains("preflight_check", AiPage.PendingPrompt);
            Assert.False(bar.IsVisible);
        }));

    [Fact]
    public void Gate_WarnOnly_LaunchesAndShowsBarWithoutForceButton()
        => WithPreflight((_, _, _) => Report("warn"), true, () => TestApp.RunAsync(async () =>
        {
            var page = new LaunchPage();
            Assert.True(await page.AiPreflightGateAsync("demo", "1.20.1", 4096));
            Assert.True(page.PreflightBar!.IsVisible);
            Button force = Btn(page.PreflightBar, "仍然启动");
            Assert.False(force.IsVisible);
        }));

    [Fact]
    public void Gate_Clean_OrDisabled_DoesNotBother()
    {
        WithPreflight((_, _, _) => Report("ok"), true, () => TestApp.RunAsync(async () =>
        {
            var page = new LaunchPage();
            Assert.True(await page.AiPreflightGateAsync("demo", "1.20.1", 4096));
            Assert.True(page.PreflightBar == null || !page.PreflightBar.IsVisible);
        }));
        int called = 0;
        WithPreflight((_, _, _) => { called++; return Report("error"); }, false, () => TestApp.RunAsync(async () =>
        {
            var page = new LaunchPage();
            Assert.True(await page.AiPreflightGateAsync("demo", "1.20.1", 4096));
            Assert.Null(page.PreflightBar);
        }));
        Assert.Equal(0, called); // 开关关掉时根本不跑预检
    }

    [Fact]
    public void Gate_PreflightCrash_NeverBlocksLaunch()
        => WithPreflight((_, _, _) => throw new InvalidOperationException("boom"), true, () => TestApp.RunAsync(async () =>
        {
            var page = new LaunchPage();
            Assert.True(await page.AiPreflightGateAsync("demo", "1.20.1", 4096));
        }));

    [Fact]
    public void Settings_PreflightSwitch_WritesConfig() => TestApp.Run(() =>
    {
        object? saved = Config.CONFIG.Get(AiPreflight.ENABLED_KEY);
        try
        {
            Config.CONFIG.data.Remove(AiPreflight.ENABLED_KEY);
            var page = new SettingsPage();
            var box = (CheckBox?)typeof(SettingsPage).GetField("_aiPreflight", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page);
            Assert.NotNull(box);
            Assert.True(box!.IsChecked); // 默认开
            box.IsChecked = false;
            Assert.False(AiPreflight.IsEnabled());
            box.IsChecked = true;
            Assert.True(AiPreflight.IsEnabled());
        }
        finally
        {
            if (saved == null) Config.CONFIG.data.Remove(AiPreflight.ENABLED_KEY);
            else Config.CONFIG.Set(AiPreflight.ENABLED_KEY, saved);
        }
    });

    [Fact]
    public void AiPage_HasExportEntry_AndFeedsConversationToTheExportTool() => TestApp.Run(() =>
    {
        var page = new AiPage();
        Assert.NotNull(page.Runner.ConversationProvider);
        Assert.NotNull(page.Runner.ConversationProvider!());
        Assert.Contains(Texts(page), t => t.Contains("导出诊断报告"));
    });
}
