using System;
using System.IO;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2a:「给定安装根目录 → java 可执行文件路径」的定位(<c>JavaRuntimeFiles.FindJavaUnderRoot</c>)。
///
/// 目录树形态来源:
/// <list type="bullet">
/// <item><b>Mojang mac</b>:<c>jre.bundle/Contents/Home/bin/java</c> —— 线上清单实测
/// (<c>mac-os-arm64</c> 的 <c>java-runtime-gamma</c>,2026-11-12 19:01,清单里唯一以 <c>/bin/java</c> 结尾的键;
/// <c>mac-os</c> 的 <c>jre-legacy</c> 也是这个相对路径);</item>
/// <item><b>Adoptium / Zulu mac</b>:<c>xxx.jre/Contents/Home/bin/java</c> —— 线上返回的包名形态
/// (<c>OpenJDK17U-jre_aarch64_mac_hotspot_17.0.21_9.tar.gz</c> / <c>…-macosx_aarch64.zip</c>)解压后的约定布局;</item>
/// <item><b>Windows</b>:<c>bin\java.exe</c> —— 改动前 Windows 版的实际形态(<c>Utils.FindExecutable</c> 的候选表)。</item>
/// </list>
///
/// 树里全是**空文件**(定位只看「文件存在」),目录都建在 <c>TestHome.Root</c> 之下;不起进程、不联网。
/// </summary>
public class B2aJavaLocatorTests
{
    /// <summary>按相对路径(正斜杠)造一棵树;不传文件就是空目录。</summary>
    private static string Tree(string label, params string[] relativeFiles)
    {
        string root = Path.Combine(TestHome.Root, "b2a-tree-" + label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (string relative in relativeFiles)
        {
            string full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, Array.Empty<byte>());
        }
        return root;
    }

    private static void AssertFound(string expectedRelative, string actual, string root)
    {
        Assert.NotNull(actual);
        string expected = Path.Combine(root, expectedRelative.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(actual));
    }

    [Fact]
    public void Mojang_mac_layout_is_found_on_the_mac_branch()
    {
        string root = Tree("mojang", "jre.bundle/Contents/Home/bin/java");

        AssertFound("jre.bundle/Contents/Home/bin/java",
            JavaRuntimeFiles.FindJavaUnderRoot(root, isWindows: false, isMac: true), root);
    }

    [Fact]
    public void Adoptium_mac_layout_is_found_on_the_mac_branch()
    {
        string root = Tree("adoptium", "jdk-17.0.13+11-jre/Contents/Home/bin/java");

        AssertFound("jdk-17.0.13+11-jre/Contents/Home/bin/java",
            JavaRuntimeFiles.FindJavaUnderRoot(root, isWindows: false, isMac: true), root);
    }

    [Fact]
    public void Zulu_mac_layout_is_found_on_the_mac_branch()
    {
        string root = Tree("zulu", "zulu17.70.113-ca-jre17.0.21.0.3-macosx_aarch64/zulu-17.jre/Contents/Home/bin/java");

        AssertFound("zulu17.70.113-ca-jre17.0.21.0.3-macosx_aarch64/zulu-17.jre/Contents/Home/bin/java",
            JavaRuntimeFiles.FindJavaUnderRoot(root, isWindows: false, isMac: true), root);
    }

    /// <summary>厂商 tar 解到根之后 <c>Contents/Home/bin/java</c> 直接挂在根下(本包新加的快速通道)。</summary>
    [Fact]
    public void A_home_dir_extracted_directly_into_the_root_is_found()
    {
        string root = Tree("home-only", "Contents/Home/bin/java");

        AssertFound("Contents/Home/bin/java",
            JavaRuntimeFiles.FindJavaUnderRoot(root, isWindows: false, isMac: true), root);
    }

    [Fact]
    public void Windows_layout_is_found_on_the_windows_branch()
    {
        string root = Tree("win", "bin/java.exe");

        AssertFound("bin/java.exe", JavaRuntimeFiles.FindJavaUnderRoot(root, isWindows: true, isMac: false), root);
    }

    /// <summary>共用的遍历本身与平台无关:同一个 Windows 形态在 mac 分支上也能找到(名称表里就有 java.exe)。</summary>
    [Fact]
    public void The_shared_walk_still_finds_the_windows_shape_on_the_mac_branch()
    {
        string root = Tree("win-on-mac", "bin/java.exe");

        AssertFound("bin/java.exe", JavaRuntimeFiles.FindJavaUnderRoot(root, isWindows: false, isMac: true), root);
    }

    [Theory]
    [InlineData("mojang", "jre.bundle/Contents/Home/bin/java")]
    [InlineData("adoptium", "jdk-17.0.13+11-jre/Contents/Home/bin/java")]
    [InlineData("zulu", "zulu-17.jre/Contents/Home/bin/java")]
    [InlineData("win", "bin/java.exe")]
    public void Windows_and_linux_branches_are_exactly_the_previous_Utils_FindExecutable(string label, string relative)
    {
        string root = Tree(label, relative);

        // 红线:Windows / Linux 的定位行为与改动前一字不差 —— 两个分支都只是转调 Utils.FindExecutable。
        Assert.Equal(Utils.FindExecutable(root), JavaRuntimeFiles.FindJavaUnderRoot(root, isWindows: true, isMac: false));
        Assert.Equal(Utils.FindExecutable(root), JavaRuntimeFiles.FindJavaUnderRoot(root, isWindows: false, isMac: false));
    }

    [Fact]
    public void A_tree_without_java_returns_null()
    {
        string root = Tree("nojava", "jre.bundle/Contents/Home/lib/x.dylib", "bin/unrelated.txt");

        Assert.Null(JavaRuntimeFiles.FindJavaUnderRoot(root, isWindows: false, isMac: true));
        Assert.Null(JavaRuntimeFiles.FindJavaUnderRoot(root, isWindows: true, isMac: false));
    }

    [Fact]
    public void A_missing_directory_returns_null_instead_of_throwing()
    {
        string missing = Path.Combine(TestHome.Root, "b2a-tree-missing-" + Guid.NewGuid().ToString("N"));

        Assert.Null(JavaRuntimeFiles.FindJavaUnderRoot(missing, isWindows: false, isMac: true));
        Assert.Null(JavaRuntimeFiles.FindJavaUnderRoot(missing, isWindows: true, isMac: false));
    }

    /// <summary>空根目录:mac 分支直接返回 null(不去遍历当前工作目录)。</summary>
    [Fact]
    public void An_empty_root_returns_null_on_the_mac_branch()
    {
        Assert.Null(JavaRuntimeFiles.FindJavaUnderRoot("", isWindows: false, isMac: true));
    }

    /// <summary>改动前的查找顺序优先 <c>bin</c>,这条把 Mojang mac 形态下返回的就是那个真实文件钉住。</summary>
    [Fact]
    public void The_returned_path_is_a_file_that_exists()
    {
        string root = Tree("exists", "jre.bundle/Contents/Home/bin/java");
        string java = JavaRuntimeFiles.FindJavaUnderRoot(root, isWindows: false, isMac: true);

        Assert.True(File.Exists(java), "定位结果必须是真实存在的文件: " + java);
    }
}
