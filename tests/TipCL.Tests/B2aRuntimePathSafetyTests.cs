using System;
using System.IO;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2a:mac 运行时落盘的权限位与路径安全检查(<c>Launcher\JavaRuntimeFiles.cs</c> 的纯函数)。
///
/// 期望值来源:
/// <list type="bullet">
/// <item>权限位 0755 / 0644 = 任务书规定的取值(可执行条目 0755,其余 0644);</item>
/// <item>链接目标的形态来自线上清单实测(2026-11-12 19:01,见 PROGRESS.md 的 B2a 证据表):
/// <c>mac-os-arm64</c> 的 <c>java-runtime-gamma</c> 里 link 条目的 target 全是相对路径、且都在安装目录内
/// (例:<c>jre.bundle/Contents/Home/legal/java.compiler/LICENSE -> ../java.base/LICENSE</c>);</item>
/// <item>安全判据 = 本包新增的行为(解析后的绝对路径必须仍在安装根目录之内)。</item>
/// </list>
///
/// 用例都只在这些纯函数上做,不写盘、不起进程、不建真实符号链接。
/// </summary>
public class B2aRuntimePathSafetyTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(TestHome.Root, "b2a-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Executable_entries_get_0755()
    {
        UnixFileMode mode = JavaRuntimeFiles.ModeForEntry(executable: true);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute, mode);
        Assert.Equal(0x1ED, (int)mode); // 0755
    }

    [Fact]
    public void Non_executable_entries_get_0644()
    {
        UnixFileMode mode = JavaRuntimeFiles.ModeForEntry(executable: false);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite
            | UnixFileMode.GroupRead | UnixFileMode.OtherRead, mode);
        Assert.Equal(0x1A4, (int)mode); // 0644
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ModeForEntry_keeps_no_group_write_and_no_other_write(bool executable)
    {
        UnixFileMode mode = JavaRuntimeFiles.ModeForEntry(executable);

        Assert.Equal(0, (int)(mode & UnixFileMode.GroupWrite));
        Assert.Equal(0, (int)(mode & UnixFileMode.OtherWrite));
        Assert.NotEqual(0, (int)(mode & UnixFileMode.UserRead));
        Assert.Equal(executable, (mode & UnixFileMode.UserExecute) != 0);
    }

    [Fact]
    public void Inside_root_covers_the_root_itself_and_children()
    {
        string root = TempDir();

        Assert.True(JavaRuntimeFiles.IsInsideRoot(root, root, isWindows: false));
        Assert.True(JavaRuntimeFiles.IsInsideRoot(root, Path.Combine(root, "a", "b.txt"), isWindows: false));
        Assert.True(JavaRuntimeFiles.IsInsideRoot(root, Path.Combine(root, "a", "..", "b.txt"), isWindows: false));
    }

    [Fact]
    public void Outside_root_and_the_sibling_prefix_trap_are_rejected()
    {
        string root = TempDir();
        string sibling = root + "-sibling";

        Assert.False(JavaRuntimeFiles.IsInsideRoot(root, Path.Combine(root, "..", "x.txt"), isWindows: false));
        Assert.False(JavaRuntimeFiles.IsInsideRoot(root, sibling, isWindows: false));
        Assert.False(JavaRuntimeFiles.IsInsideRoot(root, Path.Combine(sibling, "x.txt"), isWindows: false));
    }

    [Fact]
    public void Empty_inputs_are_rejected_by_the_root_check()
    {
        string root = TempDir();

        Assert.False(JavaRuntimeFiles.IsInsideRoot("", "x", isWindows: false));
        Assert.False(JavaRuntimeFiles.IsInsideRoot(root, "", isWindows: false));
    }

    [Fact]
    public void Windows_semantics_compare_case_insensitively()
    {
        string root = TempDir();
        string upper = root.ToUpperInvariant();
        // TestHome.Root 里一定带小写 Guid,所以 root 与 upper 的拼写本来就不相等;
        // 这里不用「平台是不是 Windows」做条件,而是拿「拼写是否相同」当期望,任何平台上都成立。
        bool sameSpelling = string.Equals(root, upper, StringComparison.Ordinal);

        Assert.False(sameSpelling);
        Assert.False(JavaRuntimeFiles.IsInsideRoot(root, Path.Combine(upper, "x.txt"), isWindows: false));
        Assert.True(JavaRuntimeFiles.IsInsideRoot(root, Path.Combine(upper, "x.txt"), isWindows: true));
    }

    [Fact]
    public void Entry_paths_stay_inside_the_install_root()
    {
        string root = TempDir();

        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, "jre.bundle", "Contents", "Home", "bin", "java")),
            JavaRuntimeFiles.ResolveEntryPath(root, "jre.bundle/Contents/Home/bin/java", isWindows: false));
        // 中间带 .. 但仍在根内:规范化之后允许(Mojang 清单里的 link 就是这种相对形态)。
        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, "legal", "java.base", "LICENSE")),
            JavaRuntimeFiles.ResolveEntryPath(root, "legal/java.compiler/../java.base/LICENSE", isWindows: false));
    }

    [Theory]
    [InlineData("../escaped.txt")]
    [InlineData("a/../../escaped.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    public void Escaping_entry_paths_are_rejected(string entryName)
    {
        string root = TempDir();

        Assert.Null(JavaRuntimeFiles.ResolveEntryPath(root, entryName, isWindows: false));
    }

    /// <summary>线上清单里的真实形态:<c>legal/java.compiler/LICENSE -> ../java.base/LICENSE</c>(2026-11-12 实测)。</summary>
    [Fact]
    public void A_relative_link_target_inside_the_root_is_kept_relative()
    {
        string root = TempDir();
        string link = "jre.bundle/Contents/Home/legal/java.compiler/LICENSE";

        string target = JavaRuntimeFiles.ResolveLinkTarget(root, link, "../java.base/LICENSE", isWindows: false);

        Assert.Equal("../java.base/LICENSE", target);
    }

    /// <summary>指向上级但仍在根内(两级 ..)也允许 —— 与上面同源,只是层级更深。</summary>
    [Fact]
    public void A_deeper_relative_target_that_stays_inside_is_allowed()
    {
        string root = TempDir();

        Assert.Equal("../../lib/x.dylib",
            JavaRuntimeFiles.ResolveLinkTarget(root, "a/b/c/link", "../../lib/x.dylib", isWindows: false));
    }

    /// <summary>逃出安装根目录的链接目标必须被拒(防止清单里的 ../../ 指到安装目录之外)。</summary>
    [Theory]
    [InlineData("../../../outside")]
    [InlineData("../../../../../../etc/passwd")]
    [InlineData("")]
    public void Link_targets_that_escape_the_root_are_rejected(string linkTarget)
    {
        string root = TempDir();

        Assert.Null(JavaRuntimeFiles.ResolveLinkTarget(root, "a/b/link", linkTarget, isWindows: false));
    }

    [Fact]
    public void An_absolute_link_target_inside_the_root_is_returned_normalized()
    {
        string root = TempDir();
        string absoluteInside = Path.Combine(root, "jre.bundle", "Contents", "Home", "bin", "java");

        Assert.Equal(Path.GetFullPath(absoluteInside),
            JavaRuntimeFiles.ResolveLinkTarget(root, "link", absoluteInside, isWindows: false));
    }

    [Fact]
    public void An_absolute_link_target_outside_the_root_is_rejected()
    {
        string root = TempDir();
        string outside = Path.Combine(root + "-outside", "x.txt");

        Assert.Null(JavaRuntimeFiles.ResolveLinkTarget(root, "link", outside, isWindows: false));
        Assert.Null(JavaRuntimeFiles.ResolveLinkTarget(root, "../escaped/link", "target", isWindows: false));
    }

    /// <summary>Windows 分支保持改动前的行为:link 条目一律跳过(返回 null / false)。</summary>
    [Fact]
    public void Windows_keeps_skipping_link_entries()
    {
        string root = TempDir();

        Assert.Null(JavaRuntimeFiles.ResolveLinkTarget(root, "a/link", "../b/target", isWindows: true));
        Assert.False(JavaRuntimeFiles.TryCreateRuntimeLink(root, "a/link", "../b/target", isWindows: true));
    }

    /// <summary>非 Windows 上,合法的 link 条目会被受理(假日志出口,不真建链接:本机是 Windows)。</summary>
    [Fact]
    public void A_safe_link_entry_is_accepted_on_non_windows()
    {
        string root = TempDir();
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        string warnings = "";

        bool ok = JavaRuntimeFiles.TryCreateRuntimeLink(
            root, "a/b/link", "../target", isWindows: false, delegate(string m) { warnings += m; });

        Assert.True(ok);
        Assert.Equal("", warnings);
    }

    [Fact]
    public void An_unsafe_link_entry_is_skipped_with_a_log_line()
    {
        string root = TempDir();
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        string warnings = "";

        bool ok = JavaRuntimeFiles.TryCreateRuntimeLink(
            root, "a/b/link", "../../../../outside", isWindows: false, delegate(string m) { warnings += m; });

        Assert.False(ok);
        Assert.Contains("跳过", warnings, StringComparison.Ordinal);
    }
}
