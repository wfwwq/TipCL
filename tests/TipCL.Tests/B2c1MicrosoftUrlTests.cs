using System;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2c1 第 2 项:Microsoft OpenJDK 的下载地址(纯函数)。
///
/// 期望值来源(**线上证据**,2026-11-12 21:14 由本包发起,共 6 个 <c>HEAD</c> 请求,
/// <c>-MaximumRedirection 0</c>,原始输出见 PROGRESS.md 的 B2c1 取证表):
/// <list type="bullet">
/// <item><c>microsoft-jdk-17-mac-x64.zip</c> / <c>...-mac-x64.tar.gz</c> /
/// <c>...-mac-aarch64.zip</c> / <c>...-mac-aarch64.tar.gz</c> 四个都是
/// <c>302 -&gt; https://www.bing.com?ref=aka&amp;shorturl=...</c>(aka.ms 的兜底页,即短链不存在);</item>
/// <item><c>microsoft-jdk-17-macos-x64.tar.gz</c> → <c>301 -&gt;
/// https://aka.ms/download-jdk/microsoft-jdk-17.0.21.0.2-macos-x64.tar.gz</c>;
/// <c>microsoft-jdk-17-macos-aarch64.tar.gz</c> → <c>301 -&gt;
/// https://aka.ms/download-jdk/microsoft-jdk-17.0.21.0.2-macos-aarch64.tar.gz</c>(真实的版本化包名)。</item>
/// </list>
///
/// Windows / Linux 的期望值是**读过的源码行为**(改动前 <c>Java.cs</c> 1543–1587 行:
/// <c>Pick(OS_NAME, "windows","windows","osx","mac","linux","linux")</c> 拼上写死的 <c>.zip</c>),
/// 逐字钉住不许变。
/// </summary>
public class B2c1MicrosoftUrlTests
{
    /// <summary>Windows 的地址逐字不变(OS 段 windows、后缀 .zip、架构段用 <c>Java.Pick</c> 映射后的取值)。</summary>
    [Theory]
    [InlineData("x64")]
    [InlineData("x86")]
    [InlineData("aarch64")]
    public void Windows_urls_are_byte_for_byte_unchanged(string archToken)
    {
        Assert.Equal(
            "https://aka.ms/download-jdk/microsoft-jdk-17-windows-" + archToken + ".zip",
            JavaRuntimeFiles.MicrosoftJdkUrl("windows", "17", archToken));
    }

    /// <summary>Linux 的地址逐字不变(注意改动前后缀也是写死的 <c>.zip</c>,不许顺手改成 tar.gz)。</summary>
    [Theory]
    [InlineData("x64")]
    [InlineData("aarch64")]
    public void Linux_urls_are_byte_for_byte_unchanged(string archToken)
    {
        Assert.Equal(
            "https://aka.ms/download-jdk/microsoft-jdk-17-linux-" + archToken + ".zip",
            JavaRuntimeFiles.MicrosoftJdkUrl("linux", "17", archToken));
    }

    /// <summary>major 的写法原样拼进去(改动前是字符串拼接一个 long)。</summary>
    [Theory]
    [InlineData("8")]
    [InlineData("21")]
    public void The_major_is_concatenated_verbatim(string major)
    {
        Assert.Equal(
            "https://aka.ms/download-jdk/microsoft-jdk-" + major + "-windows-x64.zip",
            JavaRuntimeFiles.MicrosoftJdkUrl("windows", major, "x64"));
    }

    /// <summary>mac 用的是证据支持的 <c>macos</c> + <c>.tar.gz</c>。</summary>
    [Theory]
    [InlineData("x64")]
    [InlineData("aarch64")]
    public void Mac_urls_use_macos_and_tar_gz(string archToken)
    {
        Assert.Equal(
            "https://aka.ms/download-jdk/microsoft-jdk-17-macos-" + archToken + ".tar.gz",
            JavaRuntimeFiles.MicrosoftJdkUrl("osx", "17", archToken));
    }

    /// <summary>mac 的地址里不许再出现改动前的 <c>-mac-</c>,也不许以 <c>.zip</c> 结尾。</summary>
    [Fact]
    public void Mac_urls_no_longer_look_like_the_old_zip_form()
    {
        string url = JavaRuntimeFiles.MicrosoftJdkUrl("osx", "17", "x64");

        Assert.DoesNotContain("-mac-", url, StringComparison.Ordinal);
        Assert.False(url.EndsWith(".zip", StringComparison.Ordinal), url);
    }

    /// <summary>
    /// 证据关系钉子:我们生成的这个短链,与线上实测 <c>301</c> 的 <c>Location</c> 必须是同一族
    /// (同一个 <c>microsoft-jdk-17-macos-x64</c> 前缀、同一个 <c>-macos-x64.tar.gz</c> 结尾)。
    /// </summary>
    [Fact]
    public void The_mac_url_matches_the_recorded_redirect_location()
    {
        const string recorded = "https://aka.ms/download-jdk/microsoft-jdk-17.0.21.0.2-macos-x64.tar.gz";

        string url = JavaRuntimeFiles.MicrosoftJdkUrl("osx", "17", "x64");

        Assert.StartsWith("https://aka.ms/download-jdk/microsoft-jdk-17-macos-x64", url, StringComparison.Ordinal);
        Assert.EndsWith("-macos-x64.tar.gz", recorded, StringComparison.Ordinal);
        Assert.Contains("microsoft-jdk-17-macos-x64.tar.gz", url, StringComparison.Ordinal);
    }

    /// <summary>OS 段的映射:Windows / Linux 逐字,osx → macos。</summary>
    [Theory]
    [InlineData("windows", "windows")]
    [InlineData("linux", "linux")]
    [InlineData("osx", "macos")]
    public void The_os_segment_mapping_is_pinned(string os, string expected)
    {
        Assert.Equal(expected, JavaRuntimeFiles.MicrosoftJdkOsFor(os));
    }

    /// <summary>未知 OS 与改动前的 <c>Java.Pick</c> 一样抛 <see cref="KeyError"/>(不悄悄兜底成某个平台)。</summary>
    [Theory]
    [InlineData("darwin")]
    [InlineData("")]
    public void An_unknown_os_still_throws_a_key_error(string os)
    {
        Assert.Throws<KeyError>(delegate { JavaRuntimeFiles.MicrosoftJdkOsFor(os); });
    }

    /// <summary>后缀映射:只有 mac 是 <c>.tar.gz</c>,Windows / Linux 仍是 <c>.zip</c>。</summary>
    [Theory]
    [InlineData("windows", ".zip")]
    [InlineData("linux", ".zip")]
    [InlineData("osx", ".tar.gz")]
    public void The_archive_suffix_mapping_is_pinned(string os, string expected)
    {
        Assert.Equal(expected, JavaRuntimeFiles.MicrosoftJdkArchiveSuffixFor(os));
    }

    /// <summary>
    /// mac 上地址后缀与落盘文件后缀必须一致:落盘走 <see cref="JavaRuntimeFiles.ArchiveSuffixFor"/>
    /// (mac 是 <c>.tar.gz</c>),解压又是按文件名后缀选解压器的。
    /// 改动前这里是「地址 .zip、文件名 .tar.gz」的死结(B2a 只记录了它,本包修掉)。
    /// </summary>
    [Fact]
    public void On_mac_the_url_suffix_and_the_on_disk_suffix_agree()
    {
        Assert.Equal(
            JavaRuntimeFiles.ArchiveSuffixFor("osx"),
            JavaRuntimeFiles.MicrosoftJdkArchiveSuffixFor("osx"));
        Assert.Equal(".tar.gz", JavaRuntimeFiles.ArchiveSuffixFor("osx"));
    }
}
