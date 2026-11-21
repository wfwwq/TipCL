// AI2-i18n-ux 包的界面测试:
//   1. 切到 en 时,上一轮新入口渲染出来的文字不含 CJK:启动页预检提示条 / 确认卡 / 权限面板 /
//      设置页「启动前 AI 预检」与 MCP 两行 / AI 页「导出诊断报告」/ 实例内存·GC 编辑 / 版本设置的内存说明;
//   2. AI 页把执行器报来的阶段进度落到步骤行上;
//   3. 实例详情 → 设置 里有「实例内存 / GC」一行,编辑表单能分别设置 / 清除并拒绝非法值;
//   4. 存档对话框的还原 = 先自动备份再回滚,游戏运行中拒绝;
//   5. 设置 →「布局」视图开着时,布局文档在别的线程被改(模拟 AI 的 layout_apply)→ 方案列表 / JSON 卡片自动刷新;
//      已打开页面的分栏比例即时按新值排。
// 全部在无头会话里跑(TestApp),不弹窗、不联网;落盘走 TestIsolation 的临时 TIPCL_HOME 与临时布局目录。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.App.Common;
using TipCL.App.Controls;
using TipCL.App.Pages.Ai;
using TipCL.App.Pages.Launch;
using TipCL.App.Pages.Layout;
using TipCL.App.Pages.Library;
using TipCL.App.Pages.Settings;
using TipCL.App.Pages.Version;
using TipCL.App.Shell;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using TipCL.Core.Launcher.Layout;
using TipCL.Core.Layout;
using TipCL.Tests.Core;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>收集一棵控件树上「用户看得到」的文字:文本块、输入框内容与占位、按钮无障碍名、提示、下拉项。</summary>
internal static class I18nUxUiTexts
{
    public static List<string> Of(ILogical root)
    {
        var list = new List<string>();
        void Tip(Control c)
        {
            if (ToolTip.GetTip(c) is string tip)
            {
                list.Add(tip);
            }
        }
        IEnumerable<ILogical> all = new[] { root }.Concat(root.GetLogicalDescendants());
        foreach (ILogical node in all)
        {
            switch (node)
            {
                case TextBlock tb:
                    list.Add(tb.Text ?? "");
                    Tip(tb);
                    break;
                case TextBox box:
                    list.Add(box.Text ?? "");
                    list.Add(box.Watermark ?? "");
                    Tip(box);
                    break;
                case ComboBox combo:
                    list.AddRange(combo.Items.OfType<string>());
                    Tip(combo);
                    break;
                case Button b:
                    list.Add(Avalonia.Automation.AutomationProperties.GetName(b) ?? "");
                    if (b.Content is string s)
                    {
                        list.Add(s);
                    }
                    Tip(b);
                    break;
                case Control c:
                    Tip(c);
                    break;
            }
        }
        return list.Where(x => x.Length > 0).ToList();
    }
}

public class AiI18nUxEnglishUiTests
{
    private static PyDict EnglishReport(string level) => PyDict.Of(
        "ok", level != "error", "instance", "demo", "version", "1.20.1", "memory_mb", 4096L, "java", "auto",
        "summary", "found problems",
        "error_count", level == "error" ? 1L : 0L, "warn_count", level == "warn" ? 1L : 0L,
        "items", new List<object>
        {
            PyDict.Of("level", level, "code", "dep_missing", "title", "Missing required mod", "detail", "sodium needs fabric-api", "fix", "install fabric-api"),
            PyDict.Of("level", "warn", "code", "a", "title", "t2", "detail", "d2", "fix", ""),
            PyDict.Of("level", "warn", "code", "b", "title", "t3", "detail", "d3", "fix", ""),
            PyDict.Of("level", "warn", "code", "c", "title", "t4", "detail", "d4", "fix", ""),
        });

    [Fact]
    public void LaunchPreflightBar_InEnglish_HasNoCjk()
    {
        object? saved = Config.CONFIG.Get(AiPreflight.ENABLED_KEY);
        LaunchPreflightHook.Override = (_, _, _) => EnglishReport("error");
        Config.CONFIG.Set(AiPreflight.ENABLED_KEY, true);
        try
        {
            // 注意:无头会话每次派发都会重新初始化 App(里面按配置重设语言),所以切语言必须在测试体里面做
            TestApp.RunAsync(() => I18nUxCjk.InLangAsync("en", async () =>
            {
                var page = new LaunchPage();
                Assert.False(await page.AiPreflightGateAsync("demo", "1.20.1", 4096));
                List<string> texts = I18nUxUiTexts.Of(page.PreflightBar!);
                Assert.Contains(texts, t => t.Contains("Let AI fix it"));
                Assert.Contains(texts, t => t.Contains("1 more"));
                I18nUxCjk.AssertNone(texts, "启动页预检提示条");
            }));
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
    public void ConfirmCard_InEnglish_HasNoCjk_AndChineseIsUnchanged()
    {
        var args = PyDict.Of("filename", "sodium.jar", "instance", "demo");
        var preview = Enumerable.Range(0, 45).Select(i => "- line " + i).ToList();
        TestApp.Run(() => I18nUxCjk.InLang("en", () =>
        {
            var card = new ConfirmCard("delete_mod", Tools.Label("delete_mod", args), ToolRunner.ExternalWriteReason, preview, false, (_, _, _) => { });
            List<string> texts = I18nUxUiTexts.Of(card);
            Assert.Contains(texts, t => t.Contains("5 more lines"));
            I18nUxCjk.AssertNone(texts, "确认卡");
        }));
        TestApp.Run(() => I18nUxCjk.InLang("zh_CN", () =>
        {
            var card = new ConfirmCard("delete_mod", Tools.Label("delete_mod", args), "", preview, false, (_, _, _) => { });
            List<string> texts = I18nUxUiTexts.Of(card);
            Assert.Contains("助手要执行一个操作,需要你确认", texts);
            Assert.Contains(texts, t => t == "...还有 5 行");
        }));
    }

    [Fact]
    public void PermissionPanel_InEnglish_HasNoCjk()
    {
        object? savedRules = Config.CONFIG.Get("ai_permission_rules");
        try
        {
            Config.CONFIG.Set("ai_permission_rules", new List<object> { PyDict.Of("tool", "delete_mod", "behavior", "deny", "content", "", "instance", "") });
            TestApp.RunAsync(() => I18nUxCjk.InLangAsync("en", async () =>
            {
                var panel = new AiPermissionPanel(InProcessApi.Instance, "demo");
                await panel.RefreshAsync();
                List<string> texts = I18nUxUiTexts.Of(panel);
                Assert.Contains(texts, t => t.Contains("1 custom rules"));
                I18nUxCjk.AssertNone(texts, "权限面板");
                Assert.False(I18nUxCjk.Has(AiPermissionPanel.ContentHint));
            }));
        }
        finally
        {
            if (savedRules == null) Config.CONFIG.data.Remove("ai_permission_rules");
            else Config.CONFIG.Set("ai_permission_rules", savedRules);
        }
    }

    [Fact]
    public void SettingsPreflightAndMcpRows_InEnglish_HaveNoCjk()
    {
        TestApp.Run(() => I18nUxCjk.InLang("en", () =>
        {
            var page = new SettingsPage();
            MethodInfo pre = typeof(SettingsPage).GetMethod("AiPreflightField", BindingFlags.NonPublic | BindingFlags.Instance)!;
            MethodInfo mcp = typeof(SettingsPage).GetMethod("BuildMcpRow", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var texts = I18nUxUiTexts.Of((Control)pre.Invoke(page, null)!);
            texts.AddRange(I18nUxUiTexts.Of((Control)mcp.Invoke(page, null)!));
            Assert.Contains(texts, t => t.Contains("pre-launch", StringComparison.OrdinalIgnoreCase));
            I18nUxCjk.AssertNone(texts, "设置页预检 / MCP 两行");
        }));
    }

    [Fact]
    public void AiPageExportEntry_InEnglish_HasNoCjk() => TestApp.Run(() => I18nUxCjk.InLang("en", () =>
    {
        var page = new AiPage();
        string name = I18nCore.Tr("导出诊断报告");
        Assert.False(I18nUxCjk.Has(name));
        Button btn = page.GetLogicalDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == name);
        I18nUxCjk.AssertNone(I18nUxUiTexts.Of(btn), "AI 页导出诊断报告");
    }));
}

public class AiI18nUxProgressUiTests
{
    [Fact]
    public void AiPage_ShowsUpgradeStageOnTheRunningStepLine() => TestApp.Run(() =>
    {
        var page = new AiPage();
        var call = new ToolCall("u1", "apply_instance_upgrade", PyDict.Of("instance", "demo", "target_version", "1.21.1"));
        // 没有步骤行时只更新状态条
        Assert.Null(page.ShowToolProgress("apply_instance_upgrade", "x"));
        MethodInfo onEvent = typeof(AiPage).GetMethod("OnAgentEvent", BindingFlags.NonPublic | BindingFlags.Instance)!;
        onEvent.Invoke(page, new object[] { new AiAgentEvent { Kind = "tool", Name = call.name, Text = Tools.Label(call.name, call.args), Call = call } });
        string stage = ToolRunner.UpgradeStageText(2, false);
        ToolLine? line = page.ShowToolProgress("apply_instance_upgrade", stage);
        Assert.NotNull(line);
        Assert.Equal(ToolState.Running, line!.State);
        Assert.Contains(line.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == stage);
        Assert.Contains(page.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == stage); // 状态条
        // 执行器回调 → AI 页的接线(初始化里那一行)
        Assert.NotNull(page.Runner.ToolProgress);
    });
}

public class AiI18nUxInstanceJvmUiTests
{
    [Fact]
    public void Editor_SetsClearsAndRejects() => TestApp.Run(() =>
    {
        using var t = new NfTempInstance("jvmui");
        InstanceJvmEditor.Form form = InstanceJvmEditor.BuildForm(t.Name);
        Assert.Equal("", form.Memory.Text);
        Assert.Equal(0, form.Gc.SelectedIndex);

        form.Memory.Text = "3072";
        form.Gc.SelectedIndex = Array.IndexOf(InstanceJvmEditor.GcValues, "zgc");
        Assert.Null(InstanceJvmEditor.Apply(t.Name, form));
        Assert.Equal(((long?)3072, "zgc"), InstanceJvmSettings.Read(t.Inst));
        Assert.Contains("3072", InstanceJvmEditor.Summary(t.Name));

        // 重新打开:表单带出现值
        form = InstanceJvmEditor.BuildForm(t.Name);
        Assert.Equal("3072", form.Memory.Text);
        Assert.Equal("zgc", InstanceJvmEditor.GcValues[form.Gc.SelectedIndex]);

        form.Memory.Text = "abc";
        Assert.NotNull(InstanceJvmEditor.Apply(t.Name, form));
        form.Memory.Text = "100";
        Assert.NotNull(InstanceJvmEditor.Apply(t.Name, form));
        Assert.Equal(((long?)3072, "zgc"), InstanceJvmSettings.Read(t.Inst)); // 非法值不落盘

        form.Memory.Text = "";
        form.Gc.SelectedIndex = 0;
        Assert.Null(InstanceJvmEditor.Apply(t.Name, form));
        Assert.Equal(((long?)null, (string?)null), InstanceJvmSettings.Read(t.Inst));
    });

    [Fact]
    public void Editor_InEnglish_HasNoCjk() => TestApp.Run(() =>
    {
        using var t = new NfTempInstance("jvmui");
        InstanceJvmSettings.Save(t.Inst, 4096, "g1_tuned");
        I18nUxCjk.InLang("en", () =>
        {
            var texts = new List<string> { InstanceJvmEditor.Title, InstanceJvmEditor.OrderHint, InstanceJvmEditor.Summary(t.Name) };
            texts.AddRange(InstanceJvmEditor.GcLabels());
            texts.AddRange(I18nUxUiTexts.Of(InstanceJvmEditor.BuildForm(t.Name).Body));
            texts.Add(VersionSetupDialog.MemoryHint);
            texts.Add(VersionSetupDialog.MemoryPlaceholder);
            I18nUxCjk.AssertNone(texts, "实例内存 / GC 编辑与版本设置说明");
        });
    });

    [Fact]
    public void VersionDialog_TextFollowsTheNewReadOrder_AndGcCanFollowInstance() => TestApp.Run(() =>
    {
        I18nUxCjk.InLang("zh_CN", () =>
        {
            string hint = VersionSetupDialog.MemoryHint;
            int iInst = hint.IndexOf("实例设置", StringComparison.Ordinal);
            int iLaunch = hint.IndexOf("启动页内存", StringComparison.Ordinal);
            int iGlobal = hint.IndexOf("全局默认", StringComparison.Ordinal);
            int iDefault = hint.IndexOf("4096", StringComparison.Ordinal);
            Assert.True(iInst >= 0 && iInst < iLaunch && iLaunch < iGlobal && iGlobal < iDefault, hint);
            Assert.DoesNotContain("留空则用全局设置", VersionSetupDialog.MemoryPlaceholder);
        });
        (string[] labels, string[] values) = VersionSetupDialog.GcChoices;
        Assert.Equal(labels.Length, values.Length);
        Assert.Equal("", values[0]); // 「跟随实例 / 全局」= 不在版本层写 gc
        Assert.Contains("auto", values);
    });

    [Fact]
    public void InstanceDetail_SettingsSegment_HasTheJvmRow() => TestApp.RunAsync(async () =>
    {
        using var t = new NfTempInstance("jvmui");
        InstanceJvmSettings.Save(t.Inst, 5120, "g1");
        var page = new InstanceDetailPage(null!, new InstanceDetailPage.InstInfo { Name = t.Name, Versions = 0 }, () => { }, null);
        typeof(InstanceDetailPage).GetMethod("ShowSeg", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, new object[] { 4 });
        var rows = (StackPanel)typeof(InstanceDetailPage).GetField("_rows", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
        for (int i = 0; i < 100 && !rows.GetLogicalDescendants().OfType<TextBlock>().Any(x => x.Text == InstanceJvmEditor.Title); i++)
        {
            await Task.Delay(30);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Contains(rows.GetLogicalDescendants().OfType<TextBlock>(), x => x.Text == InstanceJvmEditor.Title);
        Assert.Contains(rows.GetLogicalDescendants().OfType<TextBlock>(), x => (x.Text ?? "").Contains("5120"));
        Assert.Contains(rows.GetLogicalDescendants().OfType<Button>(), b => b.Name == "InstanceJvmEditButton");
    });
}

public class AiI18nUxSavesRestoreTests
{
    private static string MakeWorldWithBackup(NfTempInstance t)
    {
        t.Write("saves/World1/level.dat", "v1");
        PyDict backup = Saves.BackupSave(t.Inst, "World1");
        string name = Py.S(backup.Get("name"));
        Assert.True(name.Length > 0);
        t.Write("saves/World1/level.dat", "v2");
        return name;
    }

    [Fact]
    public void Restore_BacksUpCurrentWorldFirst() => TestApp.RunAsync(async () =>
    {
        using var t = new NfTempInstance("saves");
        string backup = MakeWorldWithBackup(t);
        SavesRestore.GameRunningOverride = () => false;
        try
        {
            PyDict r = await SavesRestore.RestoreAsync(t.Name, "", backup);
            Assert.Equal("v1", File.ReadAllText(t.PathOf("saves", "World1", "level.dat")));
            string safetyPath = Py.S(r.Get("safety_backup_path"));
            Assert.True(File.Exists(safetyPath), PyJson.Dumps(r));
            using (ZipArchive zip = ZipFile.OpenRead(safetyPath))
            {
                ZipArchiveEntry entry = zip.Entries.First(e => e.FullName.Replace('\\', '/').EndsWith("level.dat", StringComparison.Ordinal));
                using var reader = new StreamReader(entry.Open());
                Assert.Equal("v2", reader.ReadToEnd()); // 回滚前的那份被留下来了
            }
            Assert.Contains(Py.S(r.Get("safety_backup")), SavesRestore.DoneText(r, backup));
        }
        finally
        {
            SavesRestore.GameRunningOverride = null;
        }
    });

    [Fact]
    public void Restore_IsRefusedWhileTheGameRuns() => TestApp.RunAsync(async () =>
    {
        using var t = new NfTempInstance("saves");
        string backup = MakeWorldWithBackup(t);
        SavesRestore.GameRunningOverride = () => true;
        try
        {
            Assert.True(SavesRestore.GameRunning());
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => SavesRestore.RestoreAsync(t.Name, "", backup));
            Assert.Equal(SavesRestore.RunningHint, ex.Message);
            Assert.Equal("v2", File.ReadAllText(t.PathOf("saves", "World1", "level.dat"))); // 没碰盘
            I18nUxCjk.InLang("en", () => Assert.False(I18nUxCjk.Has(SavesRestore.RunningHint)));
        }
        finally
        {
            SavesRestore.GameRunningOverride = null;
        }
    });
}

[Collection("TipCL 真机交互回归")]
public class AiI18nUxLayoutLiveTests
{
    private static void Pump(Window win, int n = 6)
    {
        for (int i = 0; i < n; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
            using Avalonia.Media.Imaging.Bitmap? frame = win.CaptureRenderedFrame();
        }
    }

    private static SettingsPage OpenLayoutView(MainWindow win)
    {
        win.Show();
        Pump(win);
        win.Navigate("settings");
        Pump(win);
        SettingsPage page = Assert.IsType<SettingsPage>(win.PageHostControl!.Content);
        page.OpenLayoutView();
        Pump(win);
        return page;
    }

    private static TextBox JsonBox(Window win)
        => win.GetVisualDescendants().OfType<TextBox>().First(b => (b.Text ?? "").TrimStart().StartsWith("{", StringComparison.Ordinal));

    [Fact]
    public void LayoutView_RefreshesWhenTheLayoutIsChangedFromAnotherThread()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("i18nux-live");
        TestApp.Run(() =>
        {
            var win = new MainWindow();
            try
            {
                SettingsPage page = OpenLayoutView(win);
                int before = page.LayoutLiveRefreshCount;
                Assert.DoesNotContain(win.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "AI-made-profile");

                // 模拟 AI 的 layout_apply:在线程池上改布局文档(新建一个方案并改密度)
                Task.Run(() =>
                {
                    Assert.Equal("", LayoutProfiles.Create("AI-made-profile", fromCurrent: true));
                    LayoutV2 doc = LayoutStoreV2.Load();
                    doc.Layout.Shell.Density = "loose";
                    LayoutStoreV2.Save(doc);
                }).Wait();
                HeadlessPump.WaitFor(win, () => page.LayoutLiveRefreshCount > before);

                Assert.True(page.LayoutLiveRefreshCount > before, "布局视图没有自动刷新");
                Assert.Contains(win.GetVisualDescendants().OfType<TextBlock>(), x => (x.Text ?? "").Contains("AI-made-profile"));
                Assert.Contains("loose", JsonBox(win).Text ?? "");

                // 本视图自己没改东西时,同一份文档再触发一次不重建
                int count = page.LayoutLiveRefreshCount;
                Assert.False(page.ApplyLayoutLive());
                Assert.Equal(count, page.LayoutLiveRefreshCount);
            }
            finally
            {
                win.Close();
            }
        });
    }

    [Fact]
    public void LayoutView_KeepsAnUnappliedJsonDraft()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("i18nux-draft");
        TestApp.Run(() =>
        {
            var win = new MainWindow();
            try
            {
                SettingsPage page = OpenLayoutView(win);
                TextBox box = JsonBox(win);
                box.Text = "{ \"draft\": true }";
                Pump(win);
                int before = page.LayoutLiveRefreshCount;
                Task.Run(() =>
                {
                    LayoutV2 doc = LayoutStoreV2.Load();
                    doc.Layout.Shell.Density = "compact";
                    LayoutStoreV2.Save(doc);
                }).Wait();
                HeadlessPump.WaitFor(win, () => page.LayoutLiveRefreshCount > before);
                Assert.True(page.LayoutLiveRefreshCount > before);
                Assert.Contains(win.GetVisualDescendants().OfType<TextBox>(), b => b.Text == "{ \"draft\": true }");
            }
            finally
            {
                win.Close();
            }
        });
    }

    [Fact]
    public void OpenPage_SplitRatio_TakesEffectImmediately()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("i18nux-split");
        TestApp.Run(() =>
        {
            var win = new MainWindow();
            try
            {
                win.Show();
                Pump(win);
                win.Navigate("settings");
                Pump(win);
                PageLayoutHost host = win.GetVisualDescendants().OfType<PageLayoutHost>().First(h => h.Site?.PageId == "settings");
                Assert.False(host.HasStoredRatio);
                int reloads = host.LiveRatioReloads;

                Task.Run(() => Assert.True(SplitRatioStore.Write("settings", host.Site!.SplitId, 0.3))).Wait();
                HeadlessPump.WaitFor(win, () => host.HasStoredRatio);
                Assert.True(host.HasStoredRatio, "已打开页面的分栏没按新比例排");
                Assert.True(host.LiveRatioReloads > reloads);

                Task.Run(() => Assert.True(SplitRatioStore.Clear("settings", host.Site!.SplitId))).Wait();
                HeadlessPump.WaitFor(win, () => !host.HasStoredRatio);
                Assert.False(host.HasStoredRatio);

                // 同一个值再来一次:不重排
                int now = host.LiveRatioReloads;
                Assert.False(host.ReloadStoredRatio());
                Assert.Equal(now, host.LiveRatioReloads);
            }
            finally
            {
                win.Close();
            }
        });
    }
}
