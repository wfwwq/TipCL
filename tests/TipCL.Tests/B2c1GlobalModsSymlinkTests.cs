using System;
using System.IO;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2c1 第 5 项:<c>GlobalMods.IsSymlink</c> 的平台分支。
///
/// 期望值来源:
/// <list type="bullet">
/// <item>Windows 分支的三种形态(普通文件 / 不存在的路径 / 目录)都返回 false =
/// **读过的源码行为**(改动前唯一实现就是「先看 <c>FileInfo.Exists</c> / <c>Directory.Exists</c>,
/// 再看 <see cref="FileAttributes.ReparsePoint"/>,读属性抛异常就返回 false);
/// 这条分支本包**一个字都没改**,只是搬进 <c>isWindows</c> 为 true 的那一支;</item>
/// <item>非 Windows 分支「链接目标不为 null 才算链接、委托抛异常返回 false」= 任务书第 5 项规定的语义。
/// 真实符号链接在本机建不出来(需要特权,任务书也不许),所以这里一律用假委托。</item>
/// </list>
///
/// 用例都不建真实符号链接、不联网、不起进程。
/// </summary>
public class B2c1GlobalModsSymlinkTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(TestHome.Root, "b2c1-symlink-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Windows 分支逐字未变:普通文件、目录、不存在的路径都不是「符号链接」
    /// (<c>ReparsePoint</c> 位都没设)。<c>readLinkTarget</c> 传一个会抛异常的委托,
    /// 用来证明 Windows 分支**根本不碰**它。
    /// </summary>
    [Theory]
    [InlineData("file")]
    [InlineData("dir")]
    [InlineData("missing")]
    public void The_windows_branch_is_unchanged_and_never_reads_the_link_target(string kind)
    {
        string dir = TempDir();
        string path;
        if (kind == "file")
        {
            path = Path.Combine(dir, "plain.txt");
            File.WriteAllText(path, "x");
        }
        else if (kind == "dir")
        {
            path = Path.Combine(dir, "sub");
            Directory.CreateDirectory(path);
        }
        else
        {
            path = Path.Combine(dir, "nope");
        }

        bool got = GlobalMods.IsSymlink(
            path, isWindows: true,
            delegate(string p) { throw new InvalidOperationException("Windows 分支不该读 LinkTarget"); });

        Assert.False(got);
    }

    /// <summary>非 Windows 分支:委托说有目标 → 是链接(悬空链接也走这条路,目标字符串照样非 null)。</summary>
    [Fact]
    public void A_non_windows_link_is_recognized_from_a_non_null_target()
    {
        bool got = GlobalMods.IsSymlink(
            "/tmp/whatever", isWindows: false,
            delegate(string p) { return "../target"; });

        Assert.True(got);
    }

    /// <summary>非 Windows 分支:委托返回 null → 不是链接。</summary>
    [Fact]
    public void A_non_windows_path_without_a_target_is_not_a_link()
    {
        bool got = GlobalMods.IsSymlink(
            "/tmp/whatever", isWindows: false,
            delegate(string p) { return null; });

        Assert.False(got);
    }

    /// <summary>非 Windows 分支:读取委托抛异常 → false(与 Windows 分支的异常语义一致,不往上抛)。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_throwing_reader_means_not_a_link(bool ioException)
    {
        Func<string, string> reader;
        if (ioException)
        {
            reader = delegate(string p) { throw new IOException("boom"); };
        }
        else
        {
            reader = delegate(string p) { throw new UnauthorizedAccessException("boom"); };
        }

        Assert.False(GlobalMods.IsSymlink("/tmp/whatever", isWindows: false, reader));
    }

    /// <summary>委托收到的是调用方传的原样路径(不做规范化、不做平台转换)。</summary>
    [Fact]
    public void The_reader_receives_the_path_verbatim()
    {
        string seen = null;

        GlobalMods.IsSymlink("/tmp/a/b", isWindows: false, delegate(string p) { seen = p; return null; });

        Assert.Equal("/tmp/a/b", seen);
    }

    /// <summary>
    /// 生产读取委托(<see cref="FileSystemInfo.LinkTarget"/>)在 <see cref="TestHome.Root"/> 下的
    /// 真实小文件 / 真实目录上必须给 false —— 它们都不是链接。
    /// </summary>
    [Theory]
    [InlineData("file")]
    [InlineData("dir")]
    [InlineData("missing")]
    public void The_production_reader_says_no_for_real_plain_entries(string kind)
    {
        string dir = TempDir();
        string path;
        if (kind == "file")
        {
            path = Path.Combine(dir, "plain.txt");
            File.WriteAllText(path, "x");
        }
        else if (kind == "dir")
        {
            path = Path.Combine(dir, "sub");
            Directory.CreateDirectory(path);
        }
        else
        {
            path = Path.Combine(dir, "nope");
        }

        Assert.False(GlobalMods.IsSymlink(path, isWindows: false, null));
    }
}