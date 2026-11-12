using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2c1 第 3 项:<c>Java.JavaForInstaller("forge-legacy")</c> 在 Apple 芯片上要装的 Adoptium 包该传什么 arch。
///
/// 期望值来源(**线上证据**,见 PROGRESS.md 的 B2a 第 1.3 节与 Lead 补查的 Azul 表):
/// Adoptium 对 mac <c>aarch64</c> **没有** Java 8 的包(请求会失败),而有 <c>macos-x64</c> 的 8;
/// 所以 Apple 芯片上必须显式要 <c>x64</c> 那个包(目录 <c>adoptium-8-x64</c>)。
/// 「其余平台返回 null」= **读过的源码行为**(改动前是 <c>InstallAdoptium(dm, 8L, on_progress)</c>,
/// 不带 arch → <c>InstallAdoptium</c> 内部 <c>arch = arch ?? Utils.ARCH</c>,即用进程架构)。
/// </summary>
public class B2c1Legacy8Tests
{
    /// <summary>Apple 芯片(唯一为真的组合)返回 <c>"x64"</c>;其余一律 null(= 用进程架构,原行为)。
    /// (InlineData 里用 <c>&lt;null&gt;</c> 代表 null 期望值 —— xunit 分析器不许 null 进 string 参数。)</summary>
    [Theory]
    [InlineData("osx", "arm64", "x64")]
    [InlineData("osx", "x64", "<null>")]
    [InlineData("windows", "x64", "<null>")]
    [InlineData("windows", "arm64", "<null>")]
    [InlineData("linux", "x64", "<null>")]
    public void Only_apple_silicon_asks_for_the_x64_package(string os, string processArch, string expected)
    {
        string want = (expected == "<null>") ? null : expected;

        Assert.Equal(want, JavaArch.Legacy8AdoptiumArch(os, processArch));
    }

    /// <summary>「返回非 null」与 <see cref="JavaArch.IsAppleSilicon"/> 必须完全重合(逐格对照)。</summary>
    [Theory]
    [InlineData("osx", "arm64")]
    [InlineData("osx", "x64")]
    [InlineData("windows", "arm64")]
    [InlineData("linux", "arm64")]
    public void The_non_null_cases_line_up_with_apple_silicon(string os, string processArch)
    {
        Assert.Equal(
            JavaArch.IsAppleSilicon(os, processArch),
            JavaArch.Legacy8AdoptiumArch(os, processArch) != null);
    }

    /// <summary>
    /// 传进去的这个 <c>"x64"</c> 落到 Adoptium 地址里必须仍是 <c>x64</c>
    /// (架构映射逐字不变,不让 mac 的 x64 变成别的写法)。
    /// </summary>
    [Fact]
    public void The_value_lands_in_the_adoptium_url_as_x64()
    {
        string arch = JavaArch.Legacy8AdoptiumArch("osx", "arm64");

        Assert.Equal("x64", arch);
        Assert.Equal("x64", Java.AdoptiumArch(arch));
        Assert.Equal("mac", Java.AdoptiumOs("osx"));
    }

    /// <summary>
    /// 非 Apple 芯片传 null = 改动前逐字相同:null 在 <c>InstallAdoptium</c> 里被
    /// <c>arch ?? Utils.ARCH</c> 换成进程架构,所以映射结果等于直接传进程架构。
    /// </summary>
    [Fact]
    public void Null_means_the_process_arch_which_is_the_old_behaviour()
    {
        foreach (string processArch in new string[] { "x64", "x86", "arm64" })
        {
            Assert.Null(JavaArch.Legacy8AdoptiumArch("windows", processArch));
            string effective = JavaArch.Legacy8AdoptiumArch("windows", processArch) ?? processArch;
            Assert.Equal(Java.AdoptiumArch(processArch), Java.AdoptiumArch(effective));
        }
    }

    /// <summary>目录名钉子:Apple 芯片上这条路径装出来的目录就是 <c>adoptium-8-x64</c>
    /// (<c>InstallAdoptium</c> 用 <c>"adoptium-" + major + "-" + arch</c>),与 1.17 之前版本
    /// 在 Apple 芯片上启动用的那个 x64 Java 8 是同一个目录。</summary>
    [Fact]
    public void The_installed_directory_name_is_adoptium_8_x64()
    {
        string arch = JavaArch.Legacy8AdoptiumArch("osx", "arm64");

        Assert.Equal("adoptium-8-x64", "adoptium-" + 8L + "-" + arch);
    }
}
