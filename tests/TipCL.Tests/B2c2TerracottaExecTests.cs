using System;
using System.IO;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2c2 第 2 项:陶瓦联机内核(terracotta)在 mac / linux 上的可执行权限。
///
/// 现状(读过的源码,不是猜的):<c>Terracotta.Install</c> 走 <c>_extract_named</c>,它用
/// <c>File.WriteAllBytes</c> 把内核文件写到 <c>&lt;ROOT&gt;/terracotta/0.4.2/&lt;exe&gt;</c> ——
/// **没有任何一步设过可执行位**(全文件既没有 <c>File.SetUnixFileMode</c>,也没有 chmod / UAC 之类),
/// 非 Windows 上权限位就是 umask 默认值,<c>Process.Start</c> 会因「没有执行权限」失败。
/// 所以本包在 Install 落盘之后、第一次执行之前补一次 <c>JavaRuntimeFiles.SetExecutable</c>。
///
/// 需要补哪几个文件的判断抽成了纯函数 <c>Terracotta.FilesNeedingExecutableBit</c>(可注入 os):
/// Windows 返回空表(改动前一个文件都不碰,行为逐字不变);非 Windows 只给内核可执行文件本身。
/// 用例不联网、不起进程、不下载、不改防火墙;落盘的文件都在 <see cref="TestHome.Root"/> 之下。
/// mac 上到底能不能跑起来**未经真机验证**。
/// </summary>
public class B2c2TerracottaExecTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(TestHome.Root, "b2c2-terracotta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Windows 钉子:一个文件都不需要补权限位(空的返回值让 Install 里的循环体一次都不执行,
    /// 这正是改动前「什么都不做」的行为)。
    /// </summary>
    [Theory]
    [InlineData("terracotta-0.4.2-windows-x86_64.exe")]
    [InlineData("terracotta-0.4.2-windows-arm64.exe")]
    [InlineData("VCRUNTIME140.DLL")]
    [InlineData("")]
    public void Windows_needs_no_executable_bit(string exeName)
    {
        Assert.Empty(Terracotta.FilesNeedingExecutableBit("windows", exeName));
    }

    /// <summary>名字为 null(理论上不会发生)时也是空表,不抛。</summary>
    [Fact]
    public void Windows_needs_no_executable_bit_for_a_null_name()
    {
        Assert.Empty(Terracotta.FilesNeedingExecutableBit("windows", null));
        Assert.Empty(Terracotta.FilesNeedingExecutableBit("osx", null));
    }

    /// <summary>
    /// 非 Windows 只给内核可执行文件本身(文件名的依据是源码 Terracotta.cs 里两张包表:
    /// macos-x86_64 / macos-arm64 各是一个**无扩展名**的可执行文件,linux 两张表同型)。
    /// </summary>
    [Theory]
    [InlineData("osx", "terracotta-0.4.2-macos-arm64")]
    [InlineData("osx", "terracotta-0.4.2-macos-x86_64")]
    [InlineData("linux", "terracotta-0.4.2-linux-arm64")]
    [InlineData("linux", "terracotta-0.4.2-linux-x86_64")]
    public void Unix_needs_the_bit_on_the_kernel_binary_only(string os, string exeName)
    {
        string[] got = Terracotta.FilesNeedingExecutableBit(os, exeName);

        Assert.Single(got);
        Assert.Equal(exeName, got[0]);
        Assert.False(got[0].Contains('\\'));
        Assert.False(got[0].Contains('/'));
    }

    /// <summary>除 "windows" 之外的任何 os 都按非 Windows 处理(与 Utils.OS_NAME 只有三种取值的现状一致)。</summary>
    [Theory]
    [InlineData("freebsd")]
    [InlineData("")]
    [InlineData("Windows")]
    public void Only_the_exact_windows_key_is_special(string os)
    {
        Assert.Single(Terracotta.FilesNeedingExecutableBit(os, "terracotta-0.4.2-macos-arm64"));
    }

    /// <summary>
    /// <c>SetExecutable</c> 在 Windows 上是**空操作**(源码第一句就是 OperatingSystem.IsWindows 守卫):
    /// 文件内容与长度一个字节都不动、不抛异常。非 Windows 上把权限位设成 0755。
    /// </summary>
    [Fact]
    public void Set_executable_is_a_no_op_on_windows_and_0755_on_unix()
    {
        string dir = TempDir();
        string path = Path.Combine(dir, "terracotta-0.4.2-macos-arm64");
        byte[] payload = new byte[] { 1, 2, 3, 4, 5 };
        File.WriteAllBytes(path, payload);

        JavaRuntimeFiles.SetExecutable(path);

        Assert.True(File.Exists(path));
        Assert.Equal(payload, File.ReadAllBytes(path));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(JavaRuntimeFiles.ModeForEntry(executable: true), File.GetUnixFileMode(path));
        }
    }

    /// <summary>null / 空 / 不存在的路径都不抛(非 Windows 上底层异常被 catch 掉并只记一条警告)。</summary>
    [Fact]
    public void Set_executable_survives_null_empty_and_missing_paths()
    {
        string missing = Path.Combine(TestHome.Root, "b2c2-missing-" + Guid.NewGuid().ToString("N"));

        JavaRuntimeFiles.SetExecutable(null);
        JavaRuntimeFiles.SetExecutable("");
        JavaRuntimeFiles.SetExecutable(missing);

        Assert.False(File.Exists(missing));
        Assert.False(Directory.Exists(missing));
    }

    /// <summary>同一个文件调用两次不报错,第二次之后内容与权限位仍然符合预期(幂等)。</summary>
    [Fact]
    public void Set_executable_is_idempotent()
    {
        string dir = TempDir();
        string path = Path.Combine(dir, "terracotta-0.4.2-linux-x86_64");
        File.WriteAllBytes(path, new byte[] { 9 });

        JavaRuntimeFiles.SetExecutable(path);
        JavaRuntimeFiles.SetExecutable(path);

        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(path));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(JavaRuntimeFiles.ModeForEntry(executable: true), File.GetUnixFileMode(path));
        }
    }

    /// <summary>内核可执行文件名里不含目录分隔符与扩展名 —— Install 是 <c>PPath.Join(安装目录, 该名字)</c> 用的。</summary>
    [Fact]
    public void The_kernel_binary_name_is_a_bare_file_name()
    {
        string[] got = Terracotta.FilesNeedingExecutableBit("osx", "terracotta-0.4.2-macos-arm64");

        Assert.Equal("terracotta-0.4.2-macos-arm64", Path.GetFileName(got[0]));
        Assert.Equal(got[0], Path.GetFileName(got[0]));
    }

    /// <summary>
    /// 结构钉子(读源码核对,不执行 —— <c>Install</c> 会下载、联网,红线禁止在测试里调):
    /// 落盘之后、第一次执行之前,Install 用 <c>Utils.OS_NAME</c> 与 <c>pkg.exe</c> 调新接缝补权限位;
    /// Windows 下 <c>FilesNeedingExecutableBit</c> 返回空表,循环体一次都不执行。
    /// </summary>
    [Fact]
    public void Install_applies_the_bit_through_the_new_seam()
    {
        string source = File.ReadAllText(RepoFile("src", "TipCL.Core", "Launcher", "Terracotta.cs"));

        Assert.Contains("foreach (string needsExec in FilesNeedingExecutableBit(Utils.OS_NAME, pkg.exe))", source);
        Assert.Contains("JavaRuntimeFiles.SetExecutable(PPath.Join(text3, needsExec));", source);
        Assert.Contains("private static void _extract_named(", source);
        Assert.Contains("File.WriteAllBytes(Utils.AbsPath(path), item.Data ?? new byte[0]);", source);
    }

    /// <summary>从输出目录往上找到仓库根,再拼出源文件路径(只在仓库内跑测试)。</summary>
    private static string RepoFile(params string[] relative)
    {
        DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string[] parts = new string[relative.Length + 1];
            parts[0] = dir.FullName;
            Array.Copy(relative, 0, parts, 1, relative.Length);
            string candidate = Path.Combine(parts);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("没找到仓库里的源文件: " + string.Join("/", relative));
    }
}
