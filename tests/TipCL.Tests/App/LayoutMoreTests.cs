// R32-LINMORE:本包新增(Linux 线「布局视图」补齐)的净增量用例。
// 被钉住的四块都是本包从 Windows 冻结件搬过来 / 为打通「加块→选部件→上屏」新写的纯逻辑:
//   1 ShellTabsTests              —— shell.tabs 的顺序 / 显隐 / 文案 / 高亮映射(逐项对齐 Windows ShellTabs);
//   2 LayoutBlockEntryCatalogTests —— 设置页「页面区块」入口表的完整性(唯一 pageId、可导航、视图/分段下标合法);
//   3 LayoutWidgetOpsTests         —— 自建区块的纯数据操作(加定义 / 接块 / 幂等 / 删定义与全部引用);
//   4 LayoutProfilesTests          —— 整机方案(增 / 应用 / 重命名 / 复制 / 删除 / 导入 v1 页面布局);
//   5 LayoutJsonEditorTests        —— 布局 JSON 的校验 / 应用(硬错误不落盘、越界只警告)。
// 夹具:LayoutStoreV2.DirOverride 指到独立临时目录,收尾还原并删目录 —— 不碰真实数据根。
// 与其它碰 LayoutStoreV2 静态状态的用例同属「TipCL 真机交互回归」集合(DisableParallelization=true)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.App.Pages.Download;
using TipCL.App.Pages.Feedback;
using TipCL.App.Pages.Layout;
using TipCL.App.Pages.Settings;
using TipCL.App.Shell;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;
using TipCL.Core.Layout;
using Xunit;

namespace TipCL.Tests.App;

// ————————————————————————— 夹具 —————————————————————————

/// <summary>把布局存储重定向到独立临时目录(与 WidgetTests / LayoutEditorTests 同法)。</summary>
internal sealed class LayoutStoreDir : IDisposable
{
    public string Dir { get; }

    public LayoutStoreDir(string tag)
    {
        Dir = Path.Combine(Path.GetTempPath(), "tipcl_linmore_" + tag + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);
        LayoutStoreV2.DirOverride = Dir;
        LayoutStoreV2.V1Bridge = null;
        LayoutStoreV2.ResetCache();
        PageLayoutStore.DirOverride = Dir;
    }

    public void Dispose()
    {
        LayoutStoreV2.DirOverride = null;
        LayoutStoreV2.V1Bridge = null;
        LayoutStoreV2.ResetCache();
        PageLayoutStore.DirOverride = null;
        try
        {
            Directory.Delete(Dir, recursive: true);
        }
        catch
        {
            // 收尾尽力而为
        }
    }
}

/// <summary>
/// 共享的无头「推进到条件成立」小工具:固定拍数轮询在某些测试顺序下不够 —— 全量套件里
/// 共享无头会话的渲染路径可能比孤立跑时慢一两拍,于是「点完按钮就断言正文已上屏」会随机红。
/// 这里在**上限**内一直推进(强制走完 Measure/Arrange + 跑队列),上限到了才放弃,断言仍由调用方给出。
/// </summary>
internal static class HeadlessPump
{
    public static void WaitFor(Window win, Func<bool> ready, int maxTicks = 120)
    {
        for (int i = 0; i < maxTicks && !ready(); i++)
        {
            using Avalonia.Media.Imaging.Bitmap? frame = win.CaptureRenderedFrame();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        }
    }

    /// <summary>
    /// 把部件工厂状态机**复位**成生产态(注册表里有 WidgetRegistry 那一个 provider)。
    /// 为什么需要:<c>LayoutEngineTests</c> / <c>LayoutEditorTests</c> / <c>WidgetTests</c> 会直接调
    /// <c>LayoutContentRegistry.Clear()</c> 清掉全部内容工厂,但 <c>WidgetRegistry._instance</c> 并不随之归零 ——
    /// 此后 <c>WidgetRegistry.EnsureRegistered()</c> 直接早退(false),工厂就再也没回来,
    /// <c>custom:&lt;id&gt;</c> 块一律渲染成「区块不可用」占位。于是本用例的「便签正文在树上」断言
    /// 会随用例顺序随机红(<c>WidgetRegistry.Unregister()</c> 先把 _instance 归零才救得回来)。
    /// 这是测试间的全局状态污染,不是被验代码的缺陷(生产路径 App 启动时只注册一次、没人 Clear)。
    /// </summary>
    public static void ResetWidgetFactories()
    {
        WidgetRegistry.Unregister();
        WidgetRegistry.EnsureRegistered();
    }

    /// <summary>复位工厂 + 让窗口里每个宿主重读一次存储(块内容按活着的工厂重建)。</summary>
    public static void RestoreWidgetFactories(Window win)
    {
        ResetWidgetFactories();
        foreach (BlockHost host in win.GetVisualDescendants().OfType<BlockHost>())
        {
            host.Editor.ReloadFromStore();
        }
        Dispatcher.UIThread.RunJobs();
    }
}

// ————————————————————————— 1 ShellTabs —————————————————————————
[Collection("TipCL 真机交互回归")]
public class ShellTabsTests
{
    [Fact]
    public void DefaultShell_IsTheFiveContractTabsInOrder()
    {
        Assert.Equal(new[] { "launch", "library", "download", "plugins", "settings" }, ShellTabs.OrderedKeys(new ShellLayout()));
        Assert.Equal(ShellTabs.BuiltinKeys, LayoutSchema.DefaultTabs);
    }

    [Fact]
    public void EmptyOrUnknownTabs_FallBackToDefaults()
    {
        Assert.Equal(ShellTabs.BuiltinKeys, ShellTabs.OrderedKeys(new ShellLayout { Tabs = new List<string>() }));
        Assert.Equal(ShellTabs.BuiltinKeys, ShellTabs.OrderedKeys(new ShellLayout { Tabs = new List<string> { "nope", "xxx" } }));
    }

    [Fact]
    public void OrderedKeys_KeepsUserOrder_DropsDuplicates_AndAppendsSettings()
    {
        ShellLayout shell = new ShellLayout { Tabs = new List<string> { "plugins", "plugins", "launch", "bad", "download" } };
        List<string> keys = ShellTabs.OrderedKeys(shell);
        Assert.Equal(new[] { "plugins", "launch", "download", "settings" }, keys);
    }

    [Fact]
    public void SettingsTab_CannotBeHidden()
    {
        Assert.False(ShellTabs.CanHide(ShellTabs.SettingsKey));
        Assert.True(ShellTabs.CanHide(ShellTabs.LibraryKey));
        // 文档里漏了 settings 也必须补回来
        ShellLayout shell = new ShellLayout { Tabs = new List<string> { "launch" } };
        Assert.Contains(ShellTabs.SettingsKey, ShellTabs.OrderedKeys(shell));
    }

    [Fact]
    public void HighlightMapping_MatchesWindows()
    {
        Assert.Equal("launch", ShellTabs.HighlightKeyOf("launch"));
        Assert.Equal("library", ShellTabs.HighlightKeyOf("library"));
        Assert.Equal("download", ShellTabs.HighlightKeyOf("download"));
        Assert.Equal("download", ShellTabs.HighlightKeyOf("tasks"));
        Assert.Equal("plugins", ShellTabs.HighlightKeyOf("plugins"));
        Assert.Equal("settings", ShellTabs.HighlightKeyOf("settings"));
        Assert.Equal("settings", ShellTabs.HighlightKeyOf("account"));
        Assert.Null(ShellTabs.HighlightKeyOf("multiplayer"));
        Assert.Null(ShellTabs.HighlightKeyOf(""));
    }

    [Fact]
    public void Labels_AreStableKeys()
    {
        Assert.Equal("启动", ShellTabs.Label("launch"));
        Assert.Equal("实例库", ShellTabs.Label("library"));
        Assert.Equal("下载", ShellTabs.Label("download"));
        Assert.Equal("插件", ShellTabs.Label("plugins"));
        Assert.Equal("设置", ShellTabs.Label("settings"));
        Assert.Equal("plugin:x", ShellTabs.Label("plugin:x"));
    }

    [Fact]
    public void StartPageId_IsFirstVisibleBuiltinTab()
    {
        Assert.Equal("launch", ShellTabs.StartPageId(new ShellLayout()));
        ShellLayout hiddenLaunch = new ShellLayout { Tabs = new List<string> { "library", "download", "plugins", "settings" } };
        Assert.Equal("library", ShellTabs.StartPageId(hiddenLaunch));
    }

    [Fact]
    public void SetTabVisible_PersistsHideAndRestore()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("tabs");
        ShellLayout afterHide = ShellTabs.SetTabVisible("library", false);
        Assert.DoesNotContain("library", afterHide.Tabs);
        Assert.False(ShellTabs.IsVisible(afterHide, "library"));

        // 落盘后再读一次(不是同一份内存对象)
        ShellLayout reloaded = LayoutStoreV2.Load().Layout.Shell;
        Assert.DoesNotContain("library", reloaded.Tabs);

        ShellLayout afterShow = ShellTabs.SetTabVisible("library", true);
        Assert.Contains("library", afterShow.Tabs);
    }

    [Fact]
    public void SetTabs_Reorders_AndTitlebarTogglesPersist()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("tabs2");
        ShellLayout reordered = ShellTabs.SetTabs(new[] { "settings", "plugins", "download", "library", "launch" });
        Assert.Equal(new[] { "settings", "plugins", "download", "library", "launch" }, reordered.Tabs);

        ShellLayout bar = ShellTabs.SetTitlebar(ai: false, theme: false, account: false);
        Assert.False(ShellTabs.ShowAi(bar));
        Assert.False(ShellTabs.ShowTheme(bar));
        Assert.False(ShellTabs.ShowAccount(bar));
        Assert.True(ShellTabs.ShowBridge(bar)); // 未传 = 不动,默认 true

        ShellLayout persisted = LayoutStoreV2.Load().Layout.Shell;
        Assert.False(persisted.Titlebar.Ai);
        Assert.Equal(new[] { "settings", "plugins", "download", "library", "launch" }, persisted.Tabs);
    }
}

// ————————————————————————— 2 页面区块入口表 —————————————————————————

[Collection("TipCL 真机交互回归")]
public class LayoutBlockEntryCatalogTests
{
    [Fact]
    public void PageIdsAreUnique_AndEveryEntryHasNavAndLabel()
    {
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (LayoutBlockEntryCatalog.Entry entry in LayoutBlockEntryCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.PageId));
            Assert.True(seen.Add(entry.PageId), "重复的 pageId:" + entry.PageId);
            Assert.False(string.IsNullOrWhiteSpace(entry.NavId));
            Assert.False(string.IsNullOrWhiteSpace(entry.Label));
        }
    }

    [Fact]
    public void SettingsEntries_HaveValidViewIndex_AndOthersUseMinusOne()
    {
        foreach (LayoutBlockEntryCatalog.Entry entry in LayoutBlockEntryCatalog.All)
        {
            if (entry.NavId == "settings")
            {
                Assert.True(entry.SettingsView >= 0 && entry.SettingsView < 8, entry.Label + " 的设置视图下标越界");
            }
            else
            {
                Assert.Equal(-1, entry.SettingsView);
            }
        }
    }

    [Fact]
    public void DownloadEntries_HaveSegment0To6_PluginMarketHasTab()
    {
        foreach (LayoutBlockEntryCatalog.Entry entry in LayoutBlockEntryCatalog.All)
        {
            if (entry.NavId == "download")
            {
                // 0 = 原版分段(宿主是 _vanillaView),1..6 = 六个资源目录分段(R32-LINMORE 起含 0)
                Assert.True(entry.Segment >= 0 && entry.Segment <= 6, entry.Label + " 的分段下标越界");
            }
            else
            {
                Assert.Equal(-1, entry.Segment);
            }
        }
        Assert.Equal(0, LayoutBlockEntryCatalog.Find("download")!.Segment);
        Assert.Equal("market", LayoutBlockEntryCatalog.Find("plugins:market")!.Tab);
        Assert.Equal("", LayoutBlockEntryCatalog.Find("plugins")!.Tab);
    }

    [Fact]
    public void CoversTheThreeGapPages_AndLaunch()
    {
        Assert.NotNull(LayoutBlockEntryCatalog.Find("library:main"));
        Assert.NotNull(LayoutBlockEntryCatalog.Find("catalog:mod"));
        Assert.NotNull(LayoutBlockEntryCatalog.Find("plugins"));
        Assert.NotNull(LayoutBlockEntryCatalog.Find("launch"));
        Assert.Null(LayoutBlockEntryCatalog.Find("nope"));
    }

    /// <summary>[R32-LINMORE] 本包补齐的 5 个宿主必须都进了入口表(download / java / servers / playtime / feedback);
    /// 设置内层页的下标必须与 SettingsPage 的导航一致。</summary>
    [Fact]
    public void CoversTheFiveHostsAddedByThisPackage()
    {
        Assert.Equal(0, LayoutBlockEntryCatalog.Find("download")!.Segment);
        Assert.Equal("download", LayoutBlockEntryCatalog.Find("download")!.NavId);
        Assert.Equal(2, LayoutBlockEntryCatalog.Find("java")!.SettingsView);
        Assert.Equal(4, LayoutBlockEntryCatalog.Find("servers")!.SettingsView);
        Assert.Equal(5, LayoutBlockEntryCatalog.Find("playtime")!.SettingsView);
        Assert.Equal(6, LayoutBlockEntryCatalog.Find("feedback")!.SettingsView);
        foreach (string pageId in new[] { "java", "servers", "playtime", "feedback" })
        {
            LayoutBlockEntryCatalog.Entry entry = LayoutBlockEntryCatalog.Find(pageId)!;
            Assert.Equal("settings", entry.NavId);
            Assert.True(entry.SettingsView >= 0);
        }
        // 唯一一处与 Windows 入口条数不等:Linux 没有独立的 settings:about 宿主(§2.1-10 方案 B)
        Assert.Null(LayoutBlockEntryCatalog.Find("settings:about"));
    }

    [Fact]
    public void SingleBlockIds_MatchEntries()
    {
        HashSet<string> set = LayoutBlockEntryCatalog.SingleBlockPageIds();
        Assert.Contains("settings:network", set);
        Assert.DoesNotContain("settings:general", set);
    }
}

// ————————————————————————— 3 自建区块数据操作 —————————————————————————

[Collection("TipCL 真机交互回归")]
public class LayoutWidgetOpsTests
{
    [Fact]
    public void SafeTypes_AreTheFiveFirstPhaseTypes()
    {
        Assert.Equal(5, WidgetRegistry.SafeTypes.Length);
        Assert.DoesNotContain(LayoutSchema.WidgetWeb, WidgetRegistry.SafeTypes);
        Assert.DoesNotContain(LayoutSchema.WidgetButton, WidgetRegistry.SafeTypes);
        foreach (string type in WidgetRegistry.SafeTypes)
        {
            Assert.True(LayoutSchema.IsKnownWidgetType(type), type);
        }
    }

    [Fact]
    public void AddWidget_AppendsDefinition_WithTypeTitleAndUniqueId()
    {
        LayoutV2 doc = LayoutV2.CreateDefault();
        int before = doc.Layout.Widgets.Count;
        string id = LayoutWidgetOps.AddWidget(doc, LayoutSchema.WidgetNote);
        Assert.False(string.IsNullOrEmpty(id));
        Assert.Equal(before + 1, doc.Layout.Widgets.Count);
        WidgetDef def = doc.Layout.Widgets.Last();
        Assert.Equal(id, def.Id);
        Assert.Equal(LayoutSchema.WidgetNote, def.Type);
        Assert.Equal(WidgetUI.TypeLabel(LayoutSchema.WidgetNote), def.Title);
        Assert.Equal(LayoutEngine.CustomPrefix + id, LayoutWidgetOps.BlockIdOf(id));
    }

    [Fact]
    public void AddWidget_RejectsDangerousOrUnknownTypes()
    {
        LayoutV2 doc = LayoutV2.CreateDefault();
        Assert.Throws<ArgumentException>(() => LayoutWidgetOps.AddWidget(doc, LayoutSchema.WidgetWeb));
        Assert.Throws<ArgumentException>(() => LayoutWidgetOps.AddWidget(doc, LayoutSchema.WidgetButton));
        Assert.Throws<ArgumentException>(() => LayoutWidgetOps.AddWidget(doc, "nope"));
    }

    [Fact]
    public void AddWidget_ClockGetsDefaultFormat()
    {
        LayoutV2 doc = LayoutV2.CreateDefault();
        string id = LayoutWidgetOps.AddWidget(doc, LayoutSchema.WidgetClock);
        WidgetDef def = doc.Layout.Widgets.First(w => w.Id == id);
        Assert.Equal("HH:mm", def.Settings["format"]);
    }

    [Fact]
    public void AppendBlock_IsIdempotent_AndKeepsExistingBlocks()
    {
        PageLayoutV2 page = new PageLayoutV2();
        Assert.True(LayoutWidgetOps.AppendBlock(page, "folders"));
        string blockId = LayoutWidgetOps.BlockIdOf("w1");
        Assert.True(LayoutWidgetOps.AppendBlock(page, blockId));
        Assert.False(LayoutWidgetOps.AppendBlock(page, blockId));
        Assert.Equal(new[] { "folders", blockId }, page.Blocks.Select(b => b.Id).ToArray());
        Assert.Equal(1, page.Blocks[1].Z);
        Assert.Equal(1, page.Blocks[1].Span);
    }

    [Fact]
    public void RemoveWidget_DropsDefinitionAndEveryPageReference()
    {
        LayoutV2 doc = LayoutV2.CreateDefault();
        string id = LayoutWidgetOps.AddWidget(doc, LayoutSchema.WidgetNote);
        string blockId = LayoutWidgetOps.BlockIdOf(id);
        PageLayoutV2 page = new PageLayoutV2();
        LayoutWidgetOps.AppendBlock(page, "header");
        LayoutWidgetOps.AppendBlock(page, blockId);
        doc.Layout.Pages["library:main"] = page;

        int removed = LayoutWidgetOps.RemoveWidget(doc, id);
        Assert.Equal(1, removed);
        Assert.DoesNotContain(doc.Layout.Widgets, w => w.Id == id);
        Assert.DoesNotContain(doc.Layout.Pages["library:main"].Blocks, b => b.Id == blockId);
        Assert.Contains(doc.Layout.Pages["library:main"].Blocks, b => b.Id == "header");
    }

    [Fact]
    public void RemoveWidget_AlsoDropsProfileAndVariantReferences()
    {
        LayoutV2 doc = LayoutV2.CreateDefault();
        string id = LayoutWidgetOps.AddWidget(doc, LayoutSchema.WidgetNote);
        string blockId = LayoutWidgetOps.BlockIdOf(id);

        PageLayoutV2 page = new PageLayoutV2();
        LayoutWidgetOps.AppendBlock(page, blockId);
        doc.Layout.Pages["library:main"] = page;

        LayoutBody variant = new LayoutBody();
        PageLayoutV2 variantPage = new PageLayoutV2();
        LayoutWidgetOps.AppendBlock(variantPage, blockId);
        LayoutWidgetOps.AppendBlock(variantPage, "header");
        variant.Pages["library:main"] = variantPage;
        variant.Widgets.Add(new WidgetDef { Id = id, Type = LayoutSchema.WidgetNote });
        doc.Layout.Variants["narrow"] = variant;

        LayoutBody profile = new LayoutBody();
        PageLayoutV2 profilePage = new PageLayoutV2();
        LayoutWidgetOps.AppendBlock(profilePage, blockId);
        profile.Pages["plugins"] = profilePage;
        profile.Widgets.Add(new WidgetDef { Id = id, Type = LayoutSchema.WidgetNote });
        doc.Profiles["mine"] = profile;

        int removed = LayoutWidgetOps.RemoveWidget(doc, id);
        Assert.Equal(3, removed);
        Assert.DoesNotContain(doc.Layout.Widgets, w => w.Id == id);
        Assert.Empty(doc.Layout.Variants["narrow"].Widgets);
        Assert.DoesNotContain(doc.Layout.Variants["narrow"].Pages["library:main"].Blocks, b => b.Id == blockId);
        Assert.Contains(doc.Layout.Variants["narrow"].Pages["library:main"].Blocks, b => b.Id == "header");
        Assert.Empty(doc.Profiles["mine"].Widgets);
        Assert.Empty(doc.Profiles["mine"].Pages["plugins"].Blocks);
    }

    [Fact]
    public void MergeImport_DisarmsNewDangerousWidgets_AndKeepsExistingSwitch()
    {
        LayoutV2 current = LayoutV2.CreateDefault();
        WidgetDef kept = new WidgetDef { Id = "keep-web", Type = LayoutSchema.WidgetWeb, Title = "old" };
        kept.Settings["enabled"] = true;
        current.Layout.Widgets.Add(kept);

        LayoutV2 incoming = LayoutV2.CreateDefault();
        PageLayoutV2 importedPage = new PageLayoutV2();
        importedPage.Blocks.Add(new BlockLayout { Id = "custom:keep-web" });
        importedPage.Blocks.Add(new BlockLayout { Id = "custom:new-btn" });
        importedPage.Blocks.Add(new BlockLayout { Id = "custom:new-note" });
        incoming.Layout.Pages["library:main"] = importedPage;

        WidgetDef same = new WidgetDef { Id = "keep-web", Type = LayoutSchema.WidgetWeb };
        same.Settings["enabled"] = false;
        incoming.Layout.Widgets.Add(same);
        WidgetDef button = new WidgetDef { Id = "new-btn", Type = LayoutSchema.WidgetButton, Title = "go" };
        button.Settings["enabled"] = true;
        button.Settings["action"] = "page:launch";
        incoming.Layout.Widgets.Add(button);
        incoming.Layout.Widgets.Add(new WidgetDef { Id = "new-note", Type = LayoutSchema.WidgetNote, Title = "n" });

        int count = PageLayoutIO.MergeImport(current, incoming.ToJson());
        Assert.Equal(1, count);
        Assert.Equal(true, Assert.Single(current.Layout.Widgets, w => w.Id == "keep-web").Settings["enabled"]);
        Assert.Equal(false, Assert.Single(current.Layout.Widgets, w => w.Id == "new-btn").Settings["enabled"]);
        Assert.Contains(current.Layout.Widgets, w => w.Id == "new-note" && w.Type == LayoutSchema.WidgetNote);
    }

    [Fact]
    public void AddThenSaveThenLoad_KeepsWidgetAndBlock()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("widgets");
        LayoutV2 doc = LayoutStoreV2.Load();
        string id = LayoutWidgetOps.AddWidget(doc, LayoutSchema.WidgetNote);
        PageLayoutV2 page = new PageLayoutV2
        {
            Mode = LayoutSchema.ModeStack,
            Gap = LayoutSchema.GapDefault,
            Pad = LayoutSchema.PadDefaultCopy()
        };
        LayoutWidgetOps.AppendBlock(page, "header");
        LayoutWidgetOps.AppendBlock(page, LayoutWidgetOps.BlockIdOf(id));
        doc.Layout.Pages["library:main"] = page;
        Assert.True(LayoutStoreV2.Save(doc));

        LayoutV2 reloaded = LayoutStoreV2.Load();
        Assert.Contains(reloaded.Layout.Widgets, w => w.Id == id && w.Type == LayoutSchema.WidgetNote);
        Assert.Contains(reloaded.Layout.Pages["library:main"].Blocks, b => b.Id == LayoutWidgetOps.BlockIdOf(id));
        Assert.True(File.Exists(Path.Combine(dir.Dir, LayoutStoreV2.FileName)));
    }
}

// ————————————————————————— 4 整机布局方案 —————————————————————————

[Collection("TipCL 真机交互回归")]
public class LayoutProfilesTests
{
    [Fact]
    public void Create_FromCurrent_SetsActiveProfile_AndIsListed()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("profile1");
        Assert.Equal("", LayoutProfiles.Create("我的布局", fromCurrent: true));
        Assert.Contains("我的布局", LayoutProfiles.Names());
        Assert.Equal("我的布局", LayoutProfiles.ActiveName());
    }

    [Fact]
    public void Create_RejectsDuplicateAndEmptyNames()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("profile2");
        Assert.Equal("", LayoutProfiles.Create("A", fromCurrent: true));
        Assert.NotEqual("", LayoutProfiles.Create("A", fromCurrent: true));
        Assert.NotEqual("", LayoutProfiles.Create("   ", fromCurrent: true));
        Assert.NotEqual("", LayoutProfiles.Create(new string('x', LayoutProfiles.ProfileNameMax + 1), fromCurrent: true));
    }

    [Fact]
    public void Apply_ReplacesLayoutAtomically_AndKeepsPrevious()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("profile3");
        // 基线：把当前布局存为方案 A
        Assert.Equal("", LayoutProfiles.Create("A", fromCurrent: true));
        // 改当前布局（加一个块），再存为方案 B
        LayoutV2 doc = LayoutStoreV2.Load();
        doc.Layout.Pages["library:main"] = new PageLayoutV2 { Mode = LayoutSchema.ModeGrid };
        doc.Layout.Pages["library:main"].Blocks.Add(new BlockLayout { Id = "folders" });
        LayoutStoreV2.Save(doc);
        Assert.Equal("", LayoutProfiles.Create("B", fromCurrent: true));

        // 再手改一次当前布局(与激活方案 B 不再一致)⇒ 应用 A 之前必须把这份留底
        LayoutV2 drifted = LayoutStoreV2.Load();
        drifted.Layout.Pages["plugins"] = new PageLayoutV2 { Mode = LayoutSchema.ModeStack };
        drifted.Layout.Pages["plugins"].Blocks.Add(new BlockLayout { Id = "tabs" });
        LayoutStoreV2.Save(drifted);

        // 应用 A：layout 被整块替换,原布局被留底为「上一次布局」
        string error = LayoutProfiles.Apply("A", out string kept);
        Assert.Equal("", error);
        Assert.Equal(LayoutProfiles.PreviousName, kept);
        Assert.Contains(LayoutProfiles.PreviousName, LayoutProfiles.Names());
        Assert.False(LayoutStoreV2.Load().Layout.Pages.ContainsKey("library:main"));
        Assert.Equal("A", LayoutProfiles.ActiveName());
    }

    [Fact]
    public void Rename_Duplicate_Delete_Work()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("profile4");
        Assert.Equal("", LayoutProfiles.Create("A", fromCurrent: true));
        Assert.Equal("", LayoutProfiles.Rename("A", "B"));
        Assert.Contains("B", LayoutProfiles.Names());
        Assert.DoesNotContain("A", LayoutProfiles.Names());

        Assert.Equal("", LayoutProfiles.Duplicate("B", "C"));
        Assert.Contains("C", LayoutProfiles.Names());

        Assert.Equal("", LayoutProfiles.Delete("B"));
        Assert.DoesNotContain("B", LayoutProfiles.Names());
        Assert.Contains("C", LayoutProfiles.Names());
    }

    [Fact]
    public void MergeV1Pages_ImportsOrderAndHiddenFlags()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("profile5");
        LayoutV2 doc = LayoutStoreV2.Load();
        PageLayoutDoc v1 = new PageLayoutDoc();
        v1.Blocks.Add(new BlockState { Id = "folders", Hidden = false });
        v1.Blocks.Add(new BlockState { Id = "versions", Hidden = true });
        int merged = LayoutProfiles.MergeV1Pages(doc, new Dictionary<string, PageLayoutDoc>(StringComparer.Ordinal)
        {
            ["library:main"] = v1
        });
        Assert.Equal(1, merged);
        PageLayoutV2 page = doc.Layout.Pages["library:main"];
        Assert.Equal(new[] { "folders", "versions" }, page.Blocks.Select(b => b.Id).ToArray());
        Assert.False(page.Blocks[0].Hidden);
        Assert.True(page.Blocks[1].Hidden);
    }
}

// ————————————————————————— 5 布局 JSON 编辑器 —————————————————————————

[Collection("TipCL 真机交互回归")]
public class LayoutJsonEditorTests
{
    [Fact]
    public void Text_RoundtripsCurrentDocument()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("json1");
        string text = LayoutJsonEditor.Text(LayoutStoreV2.Load());
        Assert.False(string.IsNullOrWhiteSpace(text));
        List<LayoutJsonIssue> issues = LayoutJsonEditor.Inspect(text, out LayoutV2? doc);
        Assert.DoesNotContain(issues, i => i.Error);
        Assert.NotNull(doc);
        Assert.Equal(2, doc!.Version);
    }

    [Fact]
    public void Inspect_RejectsEmptyAndBrokenJson()
    {
        Assert.Contains(LayoutJsonEditor.Inspect("", out _), i => i.Error);
        Assert.Contains(LayoutJsonEditor.Inspect("{ not json", out _), i => i.Error);
        Assert.Contains(LayoutJsonEditor.Inspect("[1,2,3]", out _), i => i.Error);
    }

    [Fact]
    public void Apply_WritesValidDocumentToStore()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("json2");
        string text = "{\"version\":2,\"kind\":\"tipcl-layout\",\"layout\":{\"shell\":{\"density\":\"loose\"},\"pages\":{}}}";
        List<LayoutJsonIssue> issues = LayoutJsonEditor.Apply(text, out LayoutV2? doc);
        Assert.DoesNotContain(issues, i => i.Error);
        Assert.Equal("loose", LayoutStoreV2.Load().Layout.Shell.Density);
    }

    [Fact]
    public void Apply_BrokenJsonDoesNotWrite()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("json3");
        string before = LayoutJsonEditor.Text(LayoutStoreV2.Load());
        List<LayoutJsonIssue> issues = LayoutJsonEditor.Apply("{ broken", out _);
        Assert.Contains(issues, i => i.Error);
        string after = LayoutJsonEditor.Text(LayoutStoreV2.Load());
        Assert.Equal(before, after);
    }

    [Fact]
    public void OutOfRangeSplitRatio_IsWarningNotError()
    {
        string text = "{\"version\":2,\"kind\":\"tipcl-layout\",\"layout\":{\"splits\":{\"library:main\":{\"folders\":0.99}}}}";
        List<LayoutJsonIssue> issues = LayoutJsonEditor.Inspect(text, out _);
        Assert.DoesNotContain(issues, i => i.Error);
        Assert.Contains(issues, i => !i.Error && i.Path.Contains("splits"));
    }
}
// ————————————————————————— 7 内层子视图 / 下载页的宿主接线 —————————————————————————

// 判据(D25:已接线必须有能被移除而变红的测试):把 SettingsSubView.UseBlockHost(...) 或
// DownloadPage 里的 new BlockHost("download", ...) 撤掉,Content 立刻退回 ScrollViewer / StackPanel,
// 下面每一条都会红。块 id 与顺序按 Windows 冻结件逐字钉死,防止「接上了但 id 与 Windows 认不上」。
[Collection("TipCL 真机交互回归")]
public class SubViewBlockHostWiringTests
{
    /// <summary>只读反射:LayoutEditor 没有公开 specs(块描述只在内部用),测试按字段名取一次。</summary>
    private static List<string> SpecIds(BlockHost host)
    {
        object editor = typeof(BlockHost).GetProperty("Editor", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;
        var specs = (System.Collections.IEnumerable)editor.GetType()
            .GetField("_specs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        return specs.Cast<BlockSpec>().Select(s => s.Id).ToList();
    }

    private static BlockHost AssertHost(UserControl view, string pageId, string[] blockIds)
    {
        var host = Assert.IsType<BlockHost>(view.Content);
        Assert.Equal(pageId, host.PageId);
        Assert.True(host.Editor.CanEdit);
        Assert.Equal(blockIds, SpecIds(host));
        return host;
    }

    // 说明:四条子视图断言合并在**同一个** TestApp.Run 会话体里。分开写时每个测试各占一次会话派发,
    // 实测会把共享无头会话推到「后续渲染路径拿不到平台」的状态(全量套件里第 6 节的便签正文断言
    // 变成随机红);合并后派发次数与改动前一致,套件稳定(连续两轮 710/710 全绿,见 R32-LINMORE.md)。
    [Fact]
    public void WiredPages_ExposeBlockHostsWithWindowsPageIdsAndBlocks() => TestApp.Run(() =>
    {
        AssertHost(new JavaView(), "java", new[] { "intro", "install", "defaults", "list" });
        AssertHost(new ServersView(), "servers", new[] { "header", "instance", "list" });
        AssertHost(new PlaytimeView(), "playtime", new[] { "header", "total", "range", "list" });
        // 方案 B(§2.1-10):「关于」卡留在 feedback 的 content 块里,没有独立 settings:about 宿主
        AssertHost(new FeedbackView(), "feedback", new[] { "header", "content" });

        // 下载页原版分段:视图挂在页壳 Grid 的 ContentControl 里(不是直接当 Content)
        var page = new DownloadPage();
        page.SelectSegment(0);
        var shell = Assert.IsType<Grid>(page.Content);
        ContentControl? content = shell.Children.OfType<ContentControl>().FirstOrDefault();
        Assert.NotNull(content);
        Assert.Equal("download", Assert.IsType<BlockHost>(content!.Content).PageId);
    });
}

// ————————————————————————— 8 新宿主上的「加块 → 选部件 → 上屏」无头端到端 —————————————————————————

// 与第 6 节同一个无头夹具(Avalonia Headless + Skia,真可视化树、不建 OS 窗口),
// 只是目标页换成本包新接的 Java(设置内层 #2):证明设置页「布局 → 自建区块」能把部件加到
// 任意一个已接线的页面上,并且当场渲染出来。
[Collection("TipCL 真机交互回归")]
public class AddBlockToNewlyWiredPageHeadlessTests
{
    private static void Pump(Window? win = null)
    {
        for (int i = 0; i < 5; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
            if (win != null)
            {
                using Avalonia.Media.Imaging.Bitmap? frame = win.CaptureRenderedFrame();
            }
        }
    }

    /// <summary>先强制走完 Measure/Arrange,再跑队列 —— 目标子页是切过去才建的,不先布局会找不到宿主。</summary>
    private static void LayoutThenJobs(Window win)
    {
        for (int i = 0; i < 3; i++)
        {
            using Avalonia.Media.Imaging.Bitmap? frame = win.CaptureRenderedFrame();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Fact]
    public void AddNoteToJavaPage_RendersOnThatPage()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("addjava");
        // 先复位工厂状态机(别的用例会 Clear 掉内容工厂而留下 _instance,见 HeadlessPump.ResetWidgetFactories)
        HeadlessPump.ResetWidgetFactories();
        string guide = I18nCore.Tr("在布局 JSON 里给这个区块填 settings.text 即可写便签。");

        TestApp.Run(() =>
        {
            var win = new MainWindow();
            win.Show();
            Pump(win);
            win.Navigate("settings");
            Pump();
            var page = Assert.IsType<SettingsPage>(win.PageHostControl!.Content);
            page.OpenLayoutView();
            Pump(win);

            // 1) 目标页选 Java(入口表下标 = LayoutBlockEntryCatalog.All 里的位置)
            int index = Array.FindIndex(LayoutBlockEntryCatalog.All, e => e.PageId == "java");
            Assert.True(index >= 0);
            ComboBox? combo = win.GetVisualDescendants().OfType<ComboBox>()
                .FirstOrDefault(c => (Avalonia.Automation.AutomationProperties.GetName(c) ?? "") == "自建区块加到页面");
            Assert.NotNull(combo);
            combo!.SelectedIndex = index;
            Pump();

            // 2) 选部件:便签
            Button? add = win.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => (Avalonia.Automation.AutomationProperties.GetName(b) ?? "")
                    .StartsWith("添加部件:便签(note)", StringComparison.Ordinal));
            Assert.NotNull(add);
            add!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            LayoutThenJobs(win);

            // 3) 数据层:块引用落在 java 这一页上(不是落回默认页)
            LayoutV2 doc = LayoutStoreV2.Load();
            WidgetDef widget = Assert.Single(doc.Layout.Widgets);
            string blockId = LayoutEngine.CustomPrefix + widget.Id;
            Assert.Contains(doc.Layout.Pages["java"].Blocks, b => b.Id == blockId);

            // 4) 可视树:Java 页宿主在树里,且便签正文真的被构造出来(按文本断言,不是"没崩")
            HeadlessPump.WaitFor(win, () => win.GetVisualDescendants().OfType<BlockHost>().Any(h => h.PageId == "java"));
            BlockHost? host = win.GetVisualDescendants().OfType<BlockHost>().FirstOrDefault(h => h.PageId == "java");
            Assert.NotNull(host);
            HeadlessPump.RestoreWidgetFactories(win);
            HeadlessPump.WaitFor(win, () => host!.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == guide));
            Assert.Contains(host!.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == guide);
            // 收尾:关窗(Closed 会摘掉 LayoutShellHooks 的静态窗口引用;泄漏的窗口会让后续窗口类用例
            // 逐步丢掉无头平台服务 —— 实测丢了之后报 Unable to locate IFontManagerImpl)
            win.Close();
        });
    }
}

// ————————————————————————— 6 设置页「布局 → 自建区块」无头端到端 —————————————————————————

// 来源/动机:lead 的新红线(2026-11-16 18:5x)禁止再启动非无头 GUI,而「加块 → 选部件 → 上屏」
// 这条链又必须证明。Avalonia Headless + Skia 能建**真可视化树**(不建 OS 窗口),所以这条链改成
// 无头会话里点真 Button + 断言可视树里真的出现部件内容 + 清缓存新建窗口证明"重开会话仍在"。
// 注意:这是**无头等价路径**,不是物理鼠标点击;真实窗口 UIA 版见 R32-LINMORE.md 的「GUI 使用与焦点纪律」。
[Collection("TipCL 真机交互回归")]
public class SettingsLayoutAddBlockHeadlessTests
{
    private static void Pump(Window? win = null)
    {
        for (int i = 0; i < 5; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
            // CaptureRenderedFrame 会强制走完 Measure/Arrange(无头会话里 RunJobs 不触发这两步)
            if (win != null)
            {
                using Avalonia.Media.Imaging.Bitmap? frame = win.CaptureRenderedFrame();
            }
        }
    }

    private static SettingsPage OpenLayoutView(MainWindow win)
    {
        win.Show();   // Avalonia headless:真可视化树,但**不建 OS 窗口**
        Pump(win);
        win.Navigate("settings");
        Pump();
        SettingsPage page = Assert.IsType<SettingsPage>(win.PageHostControl!.Content);
        page.OpenLayoutView();
        Pump();
        return page;
    }

    [Fact]
    public void LayoutView_ListsAllFiveSafeWidgetTypes()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("addlist");
        HeadlessPump.ResetWidgetFactories();
        TestApp.Run(() =>
        {
            var win = new MainWindow();
            OpenLayoutView(win);
            List<string> names = win.GetVisualDescendants().OfType<Button>()
                .Select(b => Avalonia.Automation.AutomationProperties.GetName(b) ?? "")
                .Where(n => n.StartsWith("添加部件:", StringComparison.Ordinal))
                .ToList();
            Assert.Equal(5, names.Count);
            foreach (string type in WidgetRegistry.SafeTypes)
            {
                Assert.Contains(names, n => n.Contains("(" + type + ")", StringComparison.Ordinal));
            }
            Assert.Contains(names, n => n.Contains("便签(note)", StringComparison.Ordinal));
            win.Close();
        });
    }

    [Fact]
    public void ClickingAddNote_PersistsWidgetAndBlock_AndRendersIt_AlsoAfterReopen()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("addnote");
        // 先复位工厂状态机(同上):便签正文只有在宿主**首次**渲染时工厂还在才会出现,缓存成占位后不会自愈
        HeadlessPump.ResetWidgetFactories();
        string guide = I18nCore.Tr("在布局 JSON 里给这个区块填 settings.text 即可写便签。");

        TestApp.Run(() =>
        {
            var win = new MainWindow();
            OpenLayoutView(win);
            Button? add = win.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => (Avalonia.Automation.AutomationProperties.GetName(b) ?? "").StartsWith("添加部件:便签(note)", StringComparison.Ordinal));
            Assert.NotNull(add);
            add!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(win);

            // 1) 数据层:定义 + 页面块引用都落进文档
            LayoutV2 doc = LayoutStoreV2.Load();
            WidgetDef widget = Assert.Single(doc.Layout.Widgets);
            Assert.Equal(LayoutSchema.WidgetNote, widget.Type);
            string blockId = LayoutEngine.CustomPrefix + widget.Id;
            Assert.Contains(doc.Layout.Pages["settings:general"].Blocks, b => b.Id == blockId);

            // 2) 可视树:便签正文真的被构造出来(按文本节点断言,不是"没崩")
            HeadlessPump.RestoreWidgetFactories(win);
            HeadlessPump.WaitFor(win, () => win.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == guide));
            Assert.Contains(win.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == guide);

            // 3) 重开会话等价:清进程内缓存 + 新窗口 + 新设置页,仍能看到同一段正文
            LayoutStoreV2.ResetCache();
            WidgetRegistry.EnsureRegistered();
            var win2 = new MainWindow();
            win2.Show();
            Pump(win2);
            win2.Navigate("settings");
            Pump(win2);
            HeadlessPump.RestoreWidgetFactories(win2);
            HeadlessPump.WaitFor(win2, () => win2.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == guide));
            Assert.Contains(win2.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == guide);
            // 收尾:关窗(理由同 AddBlockToNewlyWiredPageHeadlessTests)
            win2.Close();
            win.Close();
        });
    }
}
