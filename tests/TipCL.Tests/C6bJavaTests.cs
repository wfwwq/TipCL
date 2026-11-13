using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using TipCL.Models;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C6b:`JavaPage`(设置页分段 2 内嵌的子页)+ 那条"打开所在目录"的平台口径。
/// 期望值来源:只读的 Windows 源 <c>windos\tipcl\src\TipCL\Pages\JavaPage.cs</c>。
///
/// 纪律:碰 Avalonia 一律 <c>AvaloniaSession.Run</c>;夹具用回环假后端;
/// <b>绝不真起进程</b> —— "打开所在目录"那条走注入缝 / <c>Crash.ShellOpenHook</c> 拦截(见 D-147)。
/// </summary>
public class C6bJavaTests
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

    private static JavaPage LoadJava(C5bLayerHost harness)
    {
        C5bFiles.LayoutDir();
        JavaPage page = new JavaPage();
        C5bFiles.Mount(harness, page);
        C5bFiles.PumpTask(page.EnsureLoadedAsync());
        return page;
    }

    /// <summary>【Windows 源】<c>JavaPage.cs:47-114</c>:标题 + 四个区块 + 五个大版本卡 + 两个动作。</summary>
    [Fact]
    public void Java_page_pushes_the_windows_shell()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi().Route("get_instances", "[]").Route("get_java_list", "[]").Route("java_vendor_list", "[]");
            JavaPage page = LoadJava(harness);
            Assert.Equal("Java", page.Title);
            Assert.True(C5bFiles.HasText(page, I18n.L("游戏按版本挑 Java:26.1+ 要 25,1.20.5+ 要 21,1.18+ 要 17,老版本用 8")));
            Assert.True(HasButton(page, I18n.L("扫描系统 Java")));
            Assert.True(HasButton(page, I18n.L("用这个路径")));
            Assert.True(C5bFiles.HasText(page, I18n.L("说明")));
            Assert.True(C5bFiles.HasText(page, I18n.L("安装方式")));
            Assert.True(C5bFiles.HasText(page, I18n.L("实例默认")));
            Assert.True(C5bFiles.HasText(page, I18n.L("已检测列表")));
            // 五个大版本卡(Java 8 / 11 / 17 / 21 / 25)
            foreach (string major in new[] { "8", "11", "17", "21", "25" })
            {
                Assert.True(C5bFiles.HasText(page, "Java " + major), "缺少 Java " + major + " 那张卡");
            }

            Assert.True(C5bFiles.HasText(page, I18n.L("下载新运行时")));
            Assert.True(C5bFiles.HasText(page, I18n.L("发行版")));
        });
    }

    /// <summary>【Windows 源】<c>JavaPage.cs:254-289</c>:已检测列表的空态;发行版为空时兜 adoptium。</summary>
    [Fact]
    public void Java_page_empty_list_state()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[]")
                .Route("get_java_list", "[]")
                .Route("java_vendor_list", "[]")
                .Route("java_vendor_label", "\"Adoptium\"");
            JavaPage page = LoadJava(harness);
            Assert.True(C5bFiles.HasText(page, I18n.L("共 {0} 个", 0)));
            Assert.True(C5bFiles.HasText(page, I18n.L("没有找到 Java")));
            Assert.True(C5bFiles.HasText(page, I18n.L("点上面「下载」装一个,或扫描系统已有安装")));
            // 发行版表为空 ⇒ 兜一个 adoptium,并选中它(Windows 同)
            ComboBox vendor = C5bFiles.Descendants(page).OfType<ComboBox>().First(c => c.Items.Count == 1 && string.Equals(c.Items[0] as string, "Adoptium", StringComparison.Ordinal));
            Assert.Equal(1, vendor.Items.Count);
            Assert.Equal("Adoptium", vendor.Items[0]);
            Assert.Equal(0, vendor.SelectedIndex);
        });
    }

    /// <summary>【Windows 源】<c>JavaPage.cs:284-289</c>:一行 = 大版本 + 名称与路径 + 三个动作。</summary>
    [Fact]
    public void Java_page_rows_show_major_name_path_and_actions()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[{\"name\":\"默认\"}]")
                .Route("get_java_list", "[{\"name\":\"Temurin 21\",\"major\":\"21\",\"path\":\"/Library/Java/21/bin/java\"}]")
                .Route("java_vendor_list", "[]")
                .Route("java_combo_options", "[]");
            JavaPage page = LoadJava(harness);
            Assert.True(C5bFiles.HasText(page, I18n.L("共 {0} 个", 1)));
            Assert.True(C5bFiles.HasText(page, "Temurin 21"));
            Assert.True(C5bFiles.HasText(page, "/Library/Java/21/bin/java"));
            Assert.True(C5bFiles.HasText(page, "21"));
            Assert.True(HasButton(page, I18n.L("设为默认")));
            Assert.True(HasButton(page, I18n.L("给本实例")));
            // 骨架条用完必须收掉(Windows:先加两条 Skeleton,拿到数据后清空重画)
            Assert.Equal(0, C5bFiles.Descendants(page).OfType<Button>().Count(b => string.Equals(AutomationProperties.GetName(b), I18n.L("打开所在目录"), StringComparison.Ordinal)) - 1);
        });
    }

    /// <summary>【Windows 源】<c>JavaPage.cs:302-336</c>:「设为默认」写 <c>save_settings.default_java</c>;「给本实例」写 <c>set_instance_java</c>。</summary>
    [Fact]
    public void Java_page_row_actions_write_the_backend()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[{\"name\":\"默认\"}]")
                .Route("get_java_list", "[{\"name\":\"Temurin 21\",\"major\":\"21\",\"path\":\"/jdk21/bin/java\"}]")
                .Route("java_vendor_list", "[]")
                .Route("java_combo_options", "[]")
                .Route("save_settings", "null")
                .Route("set_instance_java", "null");
            JavaPage page = LoadJava(harness);
            C5bFiles.Click(Button(page, I18n.L("设为默认")));
            C5bFiles.Pump(() => api.Count("save_settings") >= 1);
            Assert.Contains("\"default_java\":\"/jdk21/bin/java\"", api.ParamsOf("save_settings"), StringComparison.Ordinal);
            C5bFiles.Click(Button(page, I18n.L("给本实例")));
            C5bFiles.Pump(() => api.Count("set_instance_java") >= 1);
            C5bFiles.AssertParam(api, "set_instance_java", "name", "默认");
            C5bFiles.AssertParam(api, "set_instance_java", "java", "/jdk21/bin/java");
        });
    }

    /// <summary>【Windows 源】<c>JavaPage.cs:375-395</c>:adoptium 走 <c>download_java</c>,别的发行版走 <c>install_java</c>。</summary>
    [Fact]
    public void Java_page_download_dispatch_depends_on_the_vendor()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[]")
                .Route("get_java_list", "[]")
                .Route("java_vendor_list", "[\"zulu\"]")
                .Route("java_vendor_label", "\"Zulu\"")
                .Route("download_java", "null")
                .Route("install_java", "null");
            JavaPage page = LoadJava(harness);
            // 选中 Zulu(非 adoptium)⇒ 走 install_java
            C5bFiles.Click(C5bFiles.Descendants(page).OfType<Button>().First(b => string.Equals(AutomationProperties.GetName(b), I18n.L("下载"), StringComparison.Ordinal)));
            C5bFiles.Pump(() => api.Count("install_java") >= 1 || api.Count("download_java") >= 1);
            Assert.Equal(1, api.Count("install_java"));
            Assert.Equal(0, api.Count("download_java"));
            Assert.Contains("\"vendor\":\"zulu\"", api.ParamsOf("install_java"), StringComparison.Ordinal);
            Assert.Contains("\"major\":8", api.ParamsOf("install_java"), StringComparison.Ordinal);
            C5bFiles.Pump(() => toasts.Has(I18n.L("已加入队列")));
        });
    }

    /// <summary>
    /// 【C6b 裁决的口径 + D-147】「打开所在目录」:后端 <c>open_media</c> 先走(与 Windows 同序);
    /// 后端不成才走注入缝 —— **测试里绝不真起进程**(这里同时把缝与 <c>Crash.ShellOpenHook</c> 都拦上)。
    /// </summary>
    [Fact]
    public void Java_page_open_folder_prefers_the_backend_then_the_reveal_seam()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[]")
                .Route("get_java_list", "[{\"name\":\"Temurin 21\",\"major\":\"21\",\"path\":\"/jdk21/bin/java\"}]")
                .Route("java_vendor_list", "[]")
                .Route("open_media", "true");
            JavaPage page = LoadJava(harness);
            List<string> revealed = new List<string>();
            List<string> shellOpened = new List<string>();
            Action<string>? originalSeam = JavaPage.FolderRevealer;
            Action<string>? originalHook = TipCL.Core.Launcher.Crash.ShellOpenHook;
            JavaPage.FolderRevealer = p => revealed.Add(p);
            TipCL.Core.Launcher.Crash.ShellOpenHook = p => shellOpened.Add(p);
            try
            {
                C5bFiles.Click(Button(page, I18n.L("打开所在目录")));
                C5bFiles.Pump(() => api.Count("open_media") >= 1);
                C5bFiles.Settle(4);
                // 后端吃下了 ⇒ 既不揭示也不开壳(与 Windows 同:open_media 成功就 return)
                Assert.Equal(0, revealed.Count);
                Assert.Equal(0, shellOpened.Count);
                // 【Windows 源】:目标 = `File.Exists(path) ? 它的目录 : path` —— 测试里这个路径不存在,
                // 所以原样交给后端(与 Windows 逐字相同的判据)
                C5bFiles.AssertParam(api, "open_media", "path", "/jdk21/bin/java");
            }
            finally
            {
                JavaPage.FolderRevealer = originalSeam;
                TipCL.Core.Launcher.Crash.ShellOpenHook = originalHook;
            }
        });
    }

    /// <summary>【D-147】后端不成、又没接缝 ⇒ 退回 <c>Crash.ShellOpen</c> 只打开目录(如实降级、不选中),且不起真进程。</summary>
    [Fact]
    public void Java_page_open_folder_degrades_to_shell_open_without_the_seam()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            using var harness = C5bLayerHost.Show();
            using var api = new C3cFakeApi()
                .Route("get_instances", "[]")
                .Route("get_java_list", "[{\"name\":\"Temurin 21\",\"major\":\"21\",\"path\":\"/jdk21/bin/java\"}]")
                .Route("java_vendor_list", "[]")
                .RouteError("open_media", "打不开");
            JavaPage page = LoadJava(harness);
            List<string> shellOpened = new List<string>();
            Action<string>? originalSeam = JavaPage.FolderRevealer;
            Action<string>? originalHook = TipCL.Core.Launcher.Crash.ShellOpenHook;
            JavaPage.FolderRevealer = null;
            TipCL.Core.Launcher.Crash.ShellOpenHook = p => shellOpened.Add(p);
            try
            {
                C5bFiles.Click(Button(page, I18n.L("打开所在目录")));
                C5bFiles.Pump(() => shellOpened.Count >= 1);
                // 路径不存在(测试环境)⇒ `File.Exists` 为假 ⇒ 原样交给壳(Windows 的判据逐字相同)
                Assert.Equal(new[] { "/jdk21/bin/java" }, shellOpened);
            }
            finally
            {
                JavaPage.FolderRevealer = originalSeam;
                TipCL.Core.Launcher.Crash.ShellOpenHook = originalHook;
            }
        });
    }

    /// <summary>【C6b 注册】分段 2 的工厂已经接上,造出来的是真的 <see cref="JavaPage"/>。</summary>
    [Fact]
    public void Java_segment_factory_is_wired()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5bScope.Capture();
            using var toasts = new C3cToasts();
            Func<PageBase>? original = SettingsPage.JavaPageFactory;
            try
            {
                C6bSegmentRegistration.Register();
                Assert.NotNull(SettingsPage.JavaPageFactory);
                Assert.IsType<JavaPage>(SettingsPage.JavaPageFactory!());
            }
            finally
            {
                SettingsPage.JavaPageFactory = original;
            }
        });
    }
}
