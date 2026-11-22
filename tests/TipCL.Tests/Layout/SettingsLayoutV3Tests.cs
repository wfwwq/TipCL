// R35 lin-l4:布局 v3 WP-L4「设置 → 布局」(GOAL-layout-v3 §5.4 / §6.3 WP-L4)的 Linux 侧验收单测。
// 钉住的事实:
//   1 页面卡片 = 入口表去掉 SingleBlock 的页和 settings:layout 本身,含实例库 library:main;默认 6 张顺序固定;
//   2 「已自定义」判定与 LayoutStoreV2 里有没有这一页的条目一致;
//   3 方案卡片:AI 备份按前缀「AI 备份 」识别并显示成「AI 改布局前 + 时间」;正在使用的方案不能删;排序;一行信息;
//   4 方案 应用 / 重命名 / 复制 / 删除 继续可用(LayoutMoreTests.LayoutProfilesTests 之外再走一遍完整链);
//   5 界面字符串:SettingsPage.Layout.cs 里所有 I18nCore.Tr("…") 字面量不含 ui_layout_v2 / 桥接 / L4 / L6 / #编号;
//   6 无头:「高级」默认折叠、组件管理行在里面;默认 6 张卡片 +「显示全部 N 个页面」;页签眼睛隐藏立刻落盘、「设置」锁住;
//     标题栏只剩三项勾选(没有「桥接」)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.App.Pages.Layout;
using TipCL.App.Pages.Settings;
using TipCL.App.Shell;
using TipCL.Core.Launcher.Layout;
using TipCL.Tests.App;
using Xunit;

namespace TipCL.Tests.Layout;

[Collection("TipCL 真机交互回归")]
public class SettingsLayoutV3Tests
{
    // ————— 1 页面卡片的数据 —————

    [Fact]
    public void PageCards_ExcludeSingleBlockAndLayoutSelf_AndIncludeLibrary()
    {
        List<LayoutBlockEntryCatalog.Entry> cards = SettingsPage.LayoutPageCardEntries();
        Assert.DoesNotContain(cards, e => e.SingleBlock);
        Assert.DoesNotContain(cards, e => e.PageId == "settings:layout");
        Assert.Contains(cards, e => e.PageId == "library:main");
        // 张数 = 入口表 − 单块页 − 布局页自己(Linux 入口表比 Windows 多资源目录 / 插件市场 / AI / 版本安装 / 版本管理,
        // 所以不是 Windows 的 15 张,见 progress/R35-lin-l4.md)
        int expected = LayoutBlockEntryCatalog.All.Length
            - LayoutBlockEntryCatalog.All.Count(e => e.SingleBlock)
            - 1;
        Assert.Equal(expected, cards.Count);
        Assert.Equal(cards.Count, cards.Select(e => e.PageId).Distinct().Count());
        Assert.Equal(21, cards.Count);
    }

    [Fact]
    public void PageCards_DefaultSixComeFirstInGoalOrder()
    {
        Assert.Equal(new[] { "launch", "download", "library:main", "multiplayer", "account", "ai" }, SettingsPage.LayoutDefaultCardPages);
        List<LayoutBlockEntryCatalog.Entry> cards = SettingsPage.LayoutPageCardEntries();
        Assert.Equal(SettingsPage.LayoutDefaultCardPages, cards.Take(6).Select(e => e.PageId).ToArray());
    }

    [Fact]
    public void SingleBlockPages_AreListedSeparately()
    {
        List<string> single = SettingsPage.LayoutSingleBlockEntries().Select(e => e.PageId).ToList();
        Assert.Contains("settings:network", single);
        Assert.Contains("feedback", single);
        Assert.Equal(LayoutBlockEntryCatalog.SingleBlockPageIds().OrderBy(s => s), single.OrderBy(s => s));
    }

    [Fact]
    public void InstallSubView_KeepsItsHint()
    {
        // 子视图(版本安装)保留找不到页面时的提示:BeginEditPage 找不到宿主时用它
        Assert.NotEqual("", LayoutBlockEntryCatalog.Find("install")!.NeedsContext);
        Assert.Contains(SettingsPage.LayoutPageCardEntries(), e => e.PageId == "install");
    }

    // ————— 2 「已自定义」= 存档里有这一页 —————

    [Fact]
    public void Customized_MatchesStoreEntries()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("v3custom");
        LayoutV2 doc = LayoutStoreV2.Load();
        doc.Layout.Pages["download"] = new PageLayoutV2 { Mode = LayoutSchema.ModeGrid };
        doc.Layout.Pages["download"].Blocks.Add(new BlockLayout { Id = "list" });
        doc.Layout.Pages["library:main"] = new PageLayoutV2 { Mode = LayoutSchema.ModeStack };
        Assert.True(LayoutStoreV2.Save(doc));
        LayoutStoreV2.ResetCache();

        LayoutV2 reloaded = LayoutStoreV2.Load();
        foreach (LayoutBlockEntryCatalog.Entry entry in SettingsPage.LayoutPageCardEntries())
        {
            Assert.Equal(reloaded.Layout.Pages.ContainsKey(entry.PageId), SettingsPage.IsPageCustomized(reloaded, entry.PageId));
        }
        Assert.True(SettingsPage.IsPageCustomized(reloaded, "download"));
        Assert.True(SettingsPage.IsPageCustomized(reloaded, "library:main"));
        Assert.False(SettingsPage.IsPageCustomized(reloaded, "launch"));
        Assert.False(SettingsPage.IsPageCustomized(null, "launch"));
        Assert.False(SettingsPage.IsPageCustomized(reloaded, null));
    }

    // ————— 3 方案卡片 —————

    [Fact]
    public void AiBackup_IsRecognizedByPrefix_AndShownAsBeforeAiChange()
    {
        Assert.Equal("AI 备份 ", LayoutProfiles.AiBackupPrefix);
        string name = LayoutProfiles.AiBackupPrefix + "2026-11-20 15:20:00";
        Assert.True(LayoutProfiles.IsAiBackup(name, null));
        Assert.Equal("AI 改布局前 11-20 15:20", LayoutProfiles.DisplayName(name));
        // 认不出时间就原样带上后缀
        Assert.Equal("AI 改布局前 xyz", LayoutProfiles.DisplayName(LayoutProfiles.AiBackupPrefix + "xyz"));
        Assert.Equal("我的布局", LayoutProfiles.DisplayName("我的布局"));
        Assert.False(LayoutProfiles.IsAiBackup("我的布局", null));
        Assert.Equal("AI 改布局之前自动存的一份", LayoutProfiles.CardInfo(name, new LayoutBody()));
    }

    [Fact]
    public void CardInfo_CountsCustomizedPages_AndDensity()
    {
        LayoutBody body = new LayoutBody();
        Assert.Equal("各页面都是默认排列 · 标准间距", LayoutProfiles.CardInfo("A", body));
        body.Pages["download"] = new PageLayoutV2();
        body.Pages["launch"] = new PageLayoutV2();
        body.Shell.Density = LayoutSchema.DensityCompact;
        Assert.Equal("2 个页面自定义过 · 紧凑间距", LayoutProfiles.CardInfo("A", body));
        Assert.Equal("切换方案或恢复默认之前自动留的底", LayoutProfiles.CardInfo(LayoutProfiles.PreviousName, body));
    }

    [Fact]
    public void ActiveProfile_CannotBeDeleted()
    {
        Assert.False(LayoutProfiles.CanDelete("A", "A"));
        Assert.True(LayoutProfiles.CanDelete("B", "A"));
        Assert.True(LayoutProfiles.CanDelete("B", ""));
        Assert.True(LayoutProfiles.CanDelete("B", null));
        Assert.False(LayoutProfiles.CanDelete("", "A"));
    }

    [Fact]
    public void CardsOrder_NormalThenPreviousThenAiNewestFirst()
    {
        string ai1 = LayoutProfiles.AiBackupPrefix + "2026-11-19 10:00:00";
        string ai2 = LayoutProfiles.AiBackupPrefix + "2026-11-20 09:00:00";
        List<string> order = LayoutProfiles.OrderForCards(new[] { ai1, "B", LayoutProfiles.PreviousName, ai2, "A" });
        Assert.Equal(new[] { "B", "A", LayoutProfiles.PreviousName, ai2, ai1 }, order);
    }

    // ————— 4 方案 CRUD 整链 + 删除守卫 —————

    [Fact]
    public void Profiles_ApplyRenameCopyDelete_StillWork_AndGuardBlocksActive()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("v3prof");
        Assert.Equal("", LayoutProfiles.Create("A", fromCurrent: true));
        Assert.Equal("", LayoutProfiles.Create("B", fromCurrent: true));
        Assert.Equal("B", LayoutProfiles.ActiveName());
        Assert.False(LayoutProfiles.CanDelete("B", LayoutProfiles.ActiveName()));

        Assert.Equal("", LayoutProfiles.Apply("A"));
        Assert.Equal("A", LayoutProfiles.ActiveName());
        Assert.True(LayoutProfiles.CanDelete("B", LayoutProfiles.ActiveName()));

        Assert.Equal("", LayoutProfiles.Rename("A", "A2"));
        Assert.Equal("A2", LayoutProfiles.ActiveName());
        Assert.Equal("", LayoutProfiles.Duplicate("A2", "A3"));
        Assert.Contains("A3", LayoutProfiles.Names());
        Assert.Equal("", LayoutProfiles.Delete("B"));
        Assert.DoesNotContain("B", LayoutProfiles.Names());
        Assert.Contains("A2", LayoutProfiles.Names());
    }

    // ————— 5 界面字符串里没有内部说法 —————

    private static readonly Regex LiteralOfTr = new("I18nCore\\.Tr\\(\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

    private static string? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "TipCL.App", "Pages", "Settings", "SettingsPage.Layout.cs")))
            {
                return dir.FullName;
            }
        }
        return null;
    }

    [Fact]
    public void LayoutPageStrings_HaveNoInternalWords()
    {
        string? root = RepoRoot();
        if (root is null)
        {
            return; // 没有源码树(只拿到二进制)时安静返回,与 LayoutV3I18nTests 同口径
        }
        string text = File.ReadAllText(Path.Combine(root, "src", "TipCL.App", "Pages", "Settings", "SettingsPage.Layout.cs"));
        List<string> literals = LiteralOfTr.Matches(text).Select(m => m.Groups[1].Value).ToList();
        Assert.True(literals.Count > 100, "只扫到 " + literals.Count + " 条文案,源码路径可能变了");
        Regex banned = new Regex("ui_layout_v2|桥接|\\bL4\\b|\\bL6\\b|WP-L|#\\s*\\d", RegexOptions.Compiled);
        List<string> bad = literals.Where(s => banned.IsMatch(s)).ToList();
        Assert.True(bad.Count == 0, "界面文案里出现了内部说法:\n" + string.Join("\n", bad));
    }

    // ————— 6 无头:设置 → 布局 页面本身 —————

    private static void Pump(Window win)
    {
        for (int i = 0; i < 5; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
            using Avalonia.Media.Imaging.Bitmap? frame = win.CaptureRenderedFrame();
        }
    }

    private static BlockHost OpenLayout(MainWindow win)
    {
        win.Show();
        Pump(win);
        win.Navigate("settings");
        Pump(win);
        SettingsPage page = Assert.IsType<SettingsPage>(win.PageHostControl!.Content);
        page.OpenLayoutView();
        Pump(win);
        HeadlessPump.WaitFor(win, () => win.GetVisualDescendants().OfType<BlockHost>().Any(h => h.PageId == "settings:layout"));
        return win.GetVisualDescendants().OfType<BlockHost>().First(h => h.PageId == "settings:layout");
    }

    private static List<T> Named<T>(Visual root, Func<string, bool> match) where T : Control
        => root.GetVisualDescendants().OfType<T>().Where(c => match(AutomationProperties.GetName(c) ?? "")).ToList();

    [Fact]
    public void Headless_DefaultSixCards_AdvancedCollapsed_WidgetManageInside()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("v3view");
        TestApp.Run(() =>
        {
            var win = new MainWindow();
            try
            {
                BlockHost layout = OpenLayout(win);
                // 默认 6 张卡片(每张一个「编辑」)
                List<Button> edits = Named<Button>(layout, n => n.StartsWith("编辑页面布局:", StringComparison.Ordinal));
                Assert.Equal(6, edits.Count);
                int total = SettingsPage.LayoutPageCardEntries().Count;
                Assert.Contains(layout.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "显示全部 " + total + " 个页面");

                // 「高级」标题在,内容默认折叠(组件管理 / 分栏 / 布局文件 都不可见)
                Assert.Contains(layout.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "分栏、网页与按钮组件、布局文件");
                TextBlock manage = layout.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "组件管理");
                Assert.False(manage.IsEffectivelyVisible);
                Assert.Contains(layout.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "已创建 0 个组件");

                // 标题栏三项勾选,没有「桥接」
                List<CheckBox> bars = Named<CheckBox>(layout, n => n.StartsWith("标题栏显示:", StringComparison.Ordinal));
                Assert.Equal(new[] { "标题栏显示:AI 按钮", "标题栏显示:主题切换", "标题栏显示:账号" }, bars.Select(b => AutomationProperties.GetName(b)).ToArray());
                Assert.DoesNotContain(layout.GetVisualDescendants().OfType<TextBlock>(), t => (t.Text ?? "").Contains("桥接", StringComparison.Ordinal));
            }
            finally
            {
                win.Close();
            }
        });
    }

    [Fact]
    public void Headless_EyeHidesTabAtOnce_SettingsIsLocked()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("v3tabs");
        TestApp.Run(() =>
        {
            var win = new MainWindow();
            try
            {
                BlockHost layout = OpenLayout(win);
                Button settingsEye = Named<Button>(layout, n => n == "隐藏页签:设置").Single();
                Assert.False(settingsEye.IsEnabled);

                Button downloadEye = Named<Button>(layout, n => n == "隐藏页签:下载").Single();
                downloadEye.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(win);
                LayoutStoreV2.ResetCache();
                List<string> keys = ShellTabs.OrderedKeys(LayoutStoreV2.Load().Layout.Shell);
                Assert.DoesNotContain("download", keys);
                Assert.Contains("settings", keys);

                // 视图重建后「下载」变成「显示页签」
                BlockHost again = win.GetVisualDescendants().OfType<BlockHost>().First(h => h.PageId == "settings:layout");
                Assert.Single(Named<Button>(again, n => n == "显示页签:下载"));
            }
            finally
            {
                win.Close();
            }
        });
    }
}
