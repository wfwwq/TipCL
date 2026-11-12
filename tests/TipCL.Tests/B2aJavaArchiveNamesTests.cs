using System;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2a:Adoptium / Zulu 在 mac 上的「OS 名 / 架构名 / 压缩包类型 / 后缀」四件事必须自洽。
///
/// 期望值来源:
/// <list type="bullet">
/// <item>OS 名与架构名 <c>windows</c> / <c>linux</c> 两项逐字对照改动前的 <c>Java.cs</c>
/// (<c>_AdoptiumOs</c> / <c>_AdoptiumArch</c> / <c>_ZuluOs</c> / <c>_ZuluArch</c>),本包只把它们改成可注入的纯函数,
/// 取值一字未改 —— 属「源码行为」;</item>
/// <item>mac 的取值 <c>mac</c> / <c>macos</c> / <c>aarch64</c> 由线上查询实测(2026-11-12 19:0x,见 PROGRESS.md 的 B2a 证据表):
/// Adoptium <c>os=mac&amp;architecture=aarch64</c> 与 <c>architecture=x64</c> 都能取到包(文件名 <c>…_mac_hotspot_….tar.gz</c>);
/// Azul <c>os=macos&amp;arch=aarch64</c> 能取到 <c>…-macosx_aarch64.zip</c> —— 两个厂商的 os / arch 名都被线上确认。</item>
/// </list>
///
/// 唯一被本包改掉的是 Zulu 的 <c>archive_type</c>:改动前写死 <c>zip</c>,与落盘的 <c>.tar.gz</c> 后缀及
/// 「按后缀选解压器」互相矛盾(mac 上会把 zip 的字节交给 GZipStream)。Windows / Linux 逐字保持 <c>zip</c>。
/// </summary>
public class B2aJavaArchiveNamesTests
{
    [Theory]
    [InlineData("windows", "windows")]
    [InlineData("osx", "mac")]
    [InlineData("linux", "linux")]
    public void Adoptium_os_names(string os, string expected)
    {
        Assert.Equal(expected, Java.AdoptiumOs(os));
    }

    [Theory]
    [InlineData("windows", "windows")]
    [InlineData("osx", "macos")]
    [InlineData("linux", "linux")]
    public void Zulu_os_names(string os, string expected)
    {
        Assert.Equal(expected, Java.ZuluOs(os));
    }

    [Theory]
    [InlineData("x64", "x64")]
    [InlineData("x86", "x86")]
    [InlineData("arm64", "aarch64")]
    public void Adoptium_arch_names(string arch, string expected)
    {
        Assert.Equal(expected, Java.AdoptiumArch(arch));
    }

    [Theory]
    [InlineData("x64", "x64")]
    [InlineData("x86", "x86")]
    [InlineData("arm64", "aarch64")]
    public void Zulu_arch_names(string arch, string expected)
    {
        Assert.Equal(expected, Java.ZuluArch(arch));
    }

    /// <summary>表里没有的键沿用原有的 <c>Pick</c> 行为:抛 <see cref="KeyError"/>(没有静默兜底)。</summary>
    [Fact]
    public void Unknown_names_still_throw_KeyError()
    {
        Assert.Throws<KeyError>(() => Java.AdoptiumOs("freebsd"));
        Assert.Throws<KeyError>(() => Java.ZuluOs("freebsd"));
        Assert.Throws<KeyError>(() => Java.AdoptiumArch("riscv64"));
        Assert.Throws<KeyError>(() => Java.ZuluArch("riscv64"));
    }

    /// <summary>后缀:Windows 是 .zip,其余(.tar.gz)与改动前的 <c>ArchiveSuffix()</c> 逐字一致。</summary>
    [Theory]
    [InlineData("windows", ".zip")]
    [InlineData("osx", ".tar.gz")]
    [InlineData("linux", ".tar.gz")]
    [InlineData("freebsd", ".tar.gz")]
    public void Archive_suffixes_match_the_previous_behaviour(string os, string expected)
    {
        Assert.Equal(expected, JavaRuntimeFiles.ArchiveSuffixFor(os));
    }

    /// <summary>Windows 的 Zulu 请求参数一字未改(仍是 zip)。</summary>
    [Fact]
    public void Zulu_archive_type_on_windows_is_unchanged()
    {
        Assert.Equal("zip", JavaRuntimeFiles.ZuluArchiveTypeFor("windows"));
    }

    /// <summary>mac 的 Zulu 请求参数与落盘后缀对齐(改动前是 zip → 现在 tar.gz)。</summary>
    [Fact]
    public void Zulu_archive_type_on_mac_matches_the_suffix()
    {
        string type = JavaRuntimeFiles.ZuluArchiveTypeFor("osx");

        Assert.Equal("tar.gz", type);
        Assert.Equal(JavaRuntimeFiles.ArchiveSuffixFor("osx"), "." + type);
    }

    /// <summary>
    /// Linux 仍是改动前的 zip —— 也就是说 Linux 上「请求 zip / 按 .tar.gz 落盘」这对矛盾**原样保留**:
    /// 红线要求本包只改 mac 分支,Linux 的 URL 参数一个都不许动。这条用例把现状钉住(要修必须由 Lead 裁决)。
    /// </summary>
    [Fact]
    public void Zulu_archive_type_on_linux_keeps_the_previous_inconsistent_value()
    {
        Assert.Equal("zip", JavaRuntimeFiles.ZuluArchiveTypeFor("linux"));
        Assert.Equal(".tar.gz", JavaRuntimeFiles.ArchiveSuffixFor("linux"));
    }

    /// <summary>两个厂商的 os / arch 名在 mac 上都不为空且不含 windows 字样(免得把 mac 请求发到 windows 命名空间)。</summary>
    [Fact]
    public void Mac_names_are_distinct_and_platform_specific()
    {
        Assert.NotEqual(Java.AdoptiumOs("osx"), Java.AdoptiumOs("windows"));
        Assert.NotEqual(Java.ZuluOs("osx"), Java.ZuluOs("windows"));
        Assert.Equal("aarch64", Java.AdoptiumArch("arm64"));
        Assert.Equal(Java.AdoptiumArch("arm64"), Java.ZuluArch("arm64"));
    }

    /// <summary>Adoptium 的主版本归一化(与改动前一致):8/11/17/21 各自的落点。</summary>
    [Theory]
    [InlineData(1L, 8L)]
    [InlineData(8L, 8L)]
    [InlineData(11L, 11L)]
    [InlineData(17L, 17L)]
    [InlineData(21L, 21L)]
    [InlineData(25L, 21L)]
    public void Adoptium_major_normalisation(long major, long expected)
    {
        Assert.Equal(expected, Java.AdoptiumMajor(major));
    }
}
