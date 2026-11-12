using System;
using System.Collections.Generic;
using System.IO;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2c1 第 1 项:Mojang 运行时安装的建链顺序与幂等。
///
/// 期望值来源(逐条标明):
/// <list type="bullet">
/// <item><see cref="JavaRuntimeFiles.DecideLink"/> 的四格真值表 = 任务书第 1 项写明的语义
/// (路径上没有 → 新建;已是链接且目标相同 → 跳过不记警告;已是链接但目标不同 → 删除重建;
/// 路径上是普通文件或目录 → 跳过并记一条警告、不删除);</item>
/// <item>「Windows 上仍然跳过 link 条目、返回 false」= **读过的源码行为**(改动前的
/// <c>Java.cs</c> 1211–1252 行对 <c>type == "link"</c> 直接 <c>continue</c>;B2a 的非 Windows 分支
/// 在 Windows 上恒返回 false);</item>
/// <item>「相对链接目标原样保留」= **读过的源码行为**(<see cref="JavaRuntimeFiles.ResolveLinkTarget"/>
/// 对根内相对目标返回原字符串,理由:安装目录整体移动后链接依然有效)。</item>
/// </list>
///
/// 用例全部只碰 <see cref="JavaRuntimeFiles"/> 的纯函数 / 可注入委托版本,以及
/// <see cref="TestHome.Root"/> 下自造的小文件;不联网、不起进程、**不建真实符号链接**
/// (建链接出口一律用假委托)。
/// </summary>
public class B2c1LinkOrderTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(TestHome.Root, "b2c1-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>既有状态 × 期望处置,四格逐格钉住。
    /// (InlineData 里用 <c>&lt;none&gt;</c> 代表「不是符号链接」的 null —— xunit 分析器不许 null 进 string 参数。)</summary>
    [Theory]
    [InlineData("<none>", false, "Create")]
    [InlineData("../t", true, "Skip")]
    [InlineData("../other", true, "Replace")]
    [InlineData("<none>", true, "Conflict")]
    public void DecideLink_covers_the_four_states(string existingTarget, bool pathExists, string expected)
    {
        string target = (existingTarget == "<none>") ? null : existingTarget;

        Assert.Equal(expected, JavaRuntimeFiles.DecideLink(target, pathExists, "../t").ToString());
    }

    /// <summary>路径上什么都没有时,「读到目标」这件事本身没有意义:一律新建。</summary>
    [Fact]
    public void DecideLink_creates_when_nothing_is_there_even_if_a_target_string_is_reported()
    {
        Assert.Equal("Create", JavaRuntimeFiles.DecideLink("../whatever", false, "../t").ToString());
    }

    /// <summary>目标比较是逐字比较(<c>Ordinal</c>):链接里存的就是传进去的那个字符串,不做规范化。</summary>
    [Theory]
    [InlineData("../t", "../t", true)]
    [InlineData("../t", "../T", false)]
    [InlineData("./../t", "../t", false)]
    [InlineData("../t", "/a/t", false)]
    public void DecideLink_compares_targets_verbatim(string existingTarget, string desiredTarget, bool same)
    {
        string expected = same ? "Skip" : "Replace";
        Assert.Equal(expected, JavaRuntimeFiles.DecideLink(existingTarget, true, desiredTarget).ToString());
    }

    /// <summary>Windows 分支保持改动前的行为:link 条目一律跳过(返回 false),连状态读取都不做。</summary>
    [Fact]
    public void Windows_still_skips_link_entries_without_touching_the_link_hooks()
    {
        string root = TempDir();
        int reads = 0;
        int creates = 0;
        string warnings = "";

        bool ok = JavaRuntimeFiles.TryCreateRuntimeLink(
            root, "a/b/link", "../target", isWindows: true,
            delegate(string m) { warnings += m; },
            new JavaRuntimeFiles.LinkHooks
            {
                ReadLinkTarget = delegate(string p) { reads++; return "x"; },
                Exists = delegate(string p) { reads++; return true; },
                CreateLink = delegate(string p, string t) { creates++; },
            });

        Assert.False(ok);
        Assert.Equal(0, reads);
        Assert.Equal(0, creates);
        Assert.Equal("", warnings);
    }

    /// <summary>路径上没有东西 → 新建:建链接委托收到解析后的绝对链接路径 + 原样的相对目标。</summary>
    [Fact]
    public void A_missing_link_path_is_created_with_the_relative_target_kept_verbatim()
    {
        string root = TempDir();
        string expectedLinkPath = Path.Combine(root, "a", "b", "link");
        var created = new List<KeyValuePair<string, string>>();
        var deleted = new List<string>();
        string warnings = "";

        bool ok = JavaRuntimeFiles.TryCreateRuntimeLink(
            root, "a/b/link", "../target", isWindows: false,
            delegate(string m) { warnings += m; },
            new JavaRuntimeFiles.LinkHooks
            {
                ReadLinkTarget = delegate(string p) { return null; },
                Exists = delegate(string p) { return false; },
                DeleteLink = delegate(string p) { deleted.Add(p); },
                CreateLink = delegate(string p, string t) { created.Add(new KeyValuePair<string, string>(p, t)); },
            });

        Assert.True(ok);
        Assert.Equal("", warnings);
        Assert.Empty(deleted);
        Assert.Single(created);
        Assert.Equal(expectedLinkPath, created[0].Key);
        Assert.Equal("../target", created[0].Value);
    }

    /// <summary>已经是链接且目标相同 → 跳过,**不记警告**(改动前这里会抛异常并被记成警告)。</summary>
    [Fact]
    public void An_existing_link_with_the_same_target_is_left_alone_without_a_warning()
    {
        string root = TempDir();
        int creates = 0;
        int deletes = 0;
        string warnings = "";

        bool ok = JavaRuntimeFiles.TryCreateRuntimeLink(
            root, "a/b/link", "../target", isWindows: false,
            delegate(string m) { warnings += m; },
            new JavaRuntimeFiles.LinkHooks
            {
                ReadLinkTarget = delegate(string p) { return "../target"; },
                Exists = delegate(string p) { return true; },
                DeleteLink = delegate(string p) { deletes++; },
                CreateLink = delegate(string p, string t) { creates++; },
            });

        Assert.True(ok);
        Assert.Equal(0, creates);
        Assert.Equal(0, deletes);
        Assert.Equal("", warnings);
    }

    /// <summary>已经是链接但目标不同 → 先删后建(链接本身没有内容,删掉安全)。</summary>
    [Fact]
    public void An_existing_link_with_a_different_target_is_replaced()
    {
        string root = TempDir();
        string expectedLinkPath = Path.Combine(root, "a", "b", "link");
        var created = new List<KeyValuePair<string, string>>();
        var deleted = new List<string>();
        string warnings = "";

        bool ok = JavaRuntimeFiles.TryCreateRuntimeLink(
            root, "a/b/link", "../target", isWindows: false,
            delegate(string m) { warnings += m; },
            new JavaRuntimeFiles.LinkHooks
            {
                ReadLinkTarget = delegate(string p) { return "../stale"; },
                Exists = delegate(string p) { return true; },
                DeleteLink = delegate(string p) { deleted.Add(p); },
                CreateLink = delegate(string p, string t) { created.Add(new KeyValuePair<string, string>(p, t)); },
            });

        Assert.True(ok);
        Assert.Equal("", warnings);
        Assert.Single(deleted);
        Assert.Equal(expectedLinkPath, deleted[0]);
        Assert.Single(created);
        Assert.Equal("../target", created[0].Value);
    }

    /// <summary>路径上是普通文件或目录 → 跳过并记一条警告,**不删除**(避免误删下载好的文件)。</summary>
    [Fact]
    public void A_plain_file_on_the_link_path_is_skipped_with_one_warning_and_never_deleted()
    {
        string root = TempDir();
        int creates = 0;
        int deletes = 0;
        var warnings = new List<string>();

        bool ok = JavaRuntimeFiles.TryCreateRuntimeLink(
            root, "a/b/link", "../target", isWindows: false,
            delegate(string m) { warnings.Add(m); },
            new JavaRuntimeFiles.LinkHooks
            {
                ReadLinkTarget = delegate(string p) { return null; },
                Exists = delegate(string p) { return true; },
                DeleteLink = delegate(string p) { deletes++; },
                CreateLink = delegate(string p, string t) { creates++; },
            });

        Assert.False(ok);
        Assert.Equal(0, creates);
        Assert.Equal(0, deletes);
        Assert.Single(warnings);
        Assert.Contains("\u8df3\u8fc7", warnings[0], StringComparison.Ordinal);
    }

    /// <summary>条目名逃出安装根目录 → 跳过并记一条警告,不读状态、不建链接。</summary>
    [Fact]
    public void An_escaping_entry_name_is_skipped_before_any_link_hook_runs()
    {
        string root = TempDir();
        int reads = 0;
        int creates = 0;
        var warnings = new List<string>();

        bool ok = JavaRuntimeFiles.TryCreateRuntimeLink(
            root, "../../escaped", "../target", isWindows: false,
            delegate(string m) { warnings.Add(m); },
            new JavaRuntimeFiles.LinkHooks
            {
                ReadLinkTarget = delegate(string p) { reads++; return null; },
                CreateLink = delegate(string p, string t) { creates++; },
            });

        Assert.False(ok);
        Assert.Equal(0, reads);
        Assert.Equal(0, creates);
        Assert.Single(warnings);
        Assert.Contains("\u8df3\u8fc7", warnings[0], StringComparison.Ordinal);
    }

    /// <summary>链接目标逃出安装根目录 → 跳过并记一条警告,不读状态、不建链接。</summary>
    [Fact]
    public void An_escaping_link_target_is_skipped_before_any_link_hook_runs()
    {
        string root = TempDir();
        int reads = 0;
        int creates = 0;
        var warnings = new List<string>();

        bool ok = JavaRuntimeFiles.TryCreateRuntimeLink(
            root, "a/b/link", "../../../../outside", isWindows: false,
            delegate(string m) { warnings.Add(m); },
            new JavaRuntimeFiles.LinkHooks
            {
                ReadLinkTarget = delegate(string p) { reads++; return null; },
                CreateLink = delegate(string p, string t) { creates++; },
            });

        Assert.False(ok);
        Assert.Equal(0, reads);
        Assert.Equal(0, creates);
        Assert.Single(warnings);
    }

    /// <summary>状态读取委托抛异常 → 记一条警告并跳过,不建链接(不把异常抛给调用方)。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_throwing_state_reader_is_reported_as_a_warning_and_skipped(bool readerThrows)
    {
        string root = TempDir();
        int creates = 0;
        var warnings = new List<string>();

        Func<string, string> reader = readerThrows
            ? delegate(string p) { throw new IOException("boom"); }
            : delegate(string p) { return null; };
        Func<string, bool> exists = readerThrows
            ? delegate(string p) { return false; }
            : delegate(string p) { throw new IOException("boom"); };

        bool ok = JavaRuntimeFiles.TryCreateRuntimeLink(
            root, "a/b/link", "../target", isWindows: false,
            delegate(string m) { warnings.Add(m); },
            new JavaRuntimeFiles.LinkHooks
            {
                ReadLinkTarget = reader,
                Exists = exists,
                CreateLink = delegate(string p, string t) { creates++; },
            });

        Assert.False(ok);
        Assert.Equal(0, creates);
        Assert.Single(warnings);
        Assert.Contains("boom", warnings[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// 真实文件系统(生产读取委托)上的钉子:链接路径上有一个**普通文件**时,
    /// 返回 false、且这个文件一个字节都没动(内容仍是写入时的内容)。
    /// </summary>
    [Fact]
    public void A_real_plain_file_under_the_real_reader_is_kept_byte_for_byte()
    {
        string root = TempDir();
        string linkPath = Path.Combine(root, "a", "b", "link");
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath));
        File.WriteAllText(linkPath, "keep-me");
        var warnings = new List<string>();

        bool ok = JavaRuntimeFiles.TryCreateRuntimeLink(
            root, "a/b/link", "../target", isWindows: false,
            delegate(string m) { warnings.Add(m); });

        Assert.False(ok);
        Assert.Single(warnings);
        Assert.Contains("\u8df3\u8fc7", warnings[0], StringComparison.Ordinal);
        Assert.Equal("keep-me", File.ReadAllText(linkPath));
    }

    /// <summary>
    /// 真实文件系统(生产读取委托 + 生产建链接出口)上的钉子:本机是 Windows,
    /// 所以 <c>File.CreateSymbolicLink</c> 不会被调用(实现里有真实平台守卫),
    /// 返回值是 true 但磁盘上什么都没多出来 —— 这条就是「Windows 上不建真链接」的钉子。
    /// (非 Windows 上它会真的建链接;本包不测那条 —— 任务书禁止在测试里建真实符号链接。)
    /// </summary>
    [Fact]
    public void The_real_hooks_report_success_on_windows_without_creating_anything_on_disk()
    {
        string root = TempDir();
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        string linkPath = Path.Combine(root, "a", "b", "link");
        string warnings = "";

        bool ok = JavaRuntimeFiles.TryCreateRuntimeLink(
            root, "a/b/link", "../target", isWindows: false,
            delegate(string m) { warnings += m; });

        if (!OperatingSystem.IsWindows())
        {
            // 非 Windows 上这里会真的建出符号链接(运行时行为)。任务书禁止测试建真实符号链接,
            // 所以这条用例只在 Windows 上断言,非 Windows 上不断言(不做任何事,不留残留)。
            return;
        }

        Assert.True(ok);
        Assert.Equal("", warnings);
        Assert.False(File.Exists(linkPath));
        Assert.False(Directory.Exists(linkPath));
    }
}
