using System;
using System.Collections.Generic;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2a:Mojang 运行时平台键(<c>Java.PlatformKeyFor</c>)。
///
/// 期望值来源:
/// <list type="bullet">
/// <item><c>windows</c> / <c>linux</c> / 默认分支逐项对照改动前的源码 <c>Java.cs</c> 67-96 行(Windows 版逐字复制,
/// 本包只改 <c>osx</c> 分支),所以这些期望值属于「源码行为」;</item>
/// <item>mac 的两个键 <c>mac-os</c> / <c>mac-os-arm64</c> 来自线上清单实测
/// (<c>https://piston-meta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json</c>,
/// 查询时间 2026-11-12 18:58 与 19:01,HTTP 200 / 13385 字节),
/// 顶层键与各自非空组件数:mac-os 7、mac-os-arm64 4、windows-x64 8、windows-x86 6、windows-arm64 4、
/// linux 7、linux-i386 1、gamecore 0;清单里**没有** osx / osx-arm64 / linux-arm64。
/// 整份 all.json 留在 <c>artifacts/b2a/mojang-all.json</c>(不入库)。</item>
/// </list>
/// </summary>
public class B2aPlatformKeyTests
{
    /// <summary>线上清单的顶层键(手抄自上面那份 all.json,注释里是查询时间与地址)。</summary>
    private static readonly HashSet<string> LiveManifestPlatformKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "gamecore",
        "linux",
        "linux-i386",
        "mac-os",
        "mac-os-arm64",
        "windows-arm64",
        "windows-x64",
        "windows-x86",
    };

    /// <summary>源窗口/Windows 与 Linux 的期望值逐项来自改动前的 Java.cs 67-96 行。</summary>
    [Theory]
    [InlineData("windows", "x64", "windows-x64")]
    [InlineData("windows", "arm64", "windows-arm64")]
    [InlineData("windows", "x86", "windows-x86")]
    [InlineData("windows", "ia64", "windows-x86")]
    [InlineData("linux", "x64", "linux")]
    [InlineData("linux", "arm64", "linux-arm64")]
    [InlineData("linux", "x86", "linux-i386")]
    [InlineData("linux", "riscv64", "linux")]
    [InlineData("freebsd", "x64", "linux")]
    [InlineData("", "arm64", "linux")]
    [InlineData("osx", "x64", "mac-os")]
    [InlineData("osx", "arm64", "mac-os-arm64")]
    [InlineData("osx", "x86", "mac-os")]
    public void PlatformKeyFor_matches_the_expected_table(string os, string arch, string expected)
    {
        Assert.Equal(expected, Java.PlatformKeyFor(os, arch));
    }

    /// <summary>两个 mac 键必须出现在线上清单的顶层键集合里,旧键两个都不能在(否则 mac 会走「清单中没有平台」)。</summary>
    [Fact]
    public void Mac_keys_come_from_the_live_manifest_and_the_old_keys_are_absent()
    {
        Assert.Contains(Java.PlatformKeyFor("osx", "x64"), LiveManifestPlatformKeys);
        Assert.Contains(Java.PlatformKeyFor("osx", "arm64"), LiveManifestPlatformKeys);
        Assert.Equal("mac-os", Java.PlatformKeyFor("osx", "x64"));
        Assert.DoesNotContain("osx", LiveManifestPlatformKeys);
        Assert.DoesNotContain("osx-arm64", LiveManifestPlatformKeys);
        Assert.DoesNotContain("osx", Java.PlatformKeyFor("osx", "arm64"), StringComparison.Ordinal);
    }

    /// <summary>本机算出来的键也要在线上清单里(本机是 Windows → windows-x64)。</summary>
    [Fact]
    public void This_machines_key_exists_in_the_live_manifest()
    {
        Assert.Equal("windows", Utils.OS_NAME);
        Assert.Equal("x64", Utils.ARCH);
        Assert.Contains(Java.PLATFORM_KEY, LiveManifestPlatformKeys);
    }

    /// <summary>PLATFORM_KEY 仍是 public static readonly,且等于纯函数对当前机器取值的结果。</summary>
    [Fact]
    public void PlatformKey_field_equals_the_pure_function_on_this_machine()
    {
        Assert.Equal(Java.PlatformKeyFor(Utils.OS_NAME, Utils.ARCH), Java.PLATFORM_KEY);
        Assert.Equal(Java.PlatformKeyFor(Utils.OsName(), Utils.ArchName()), Java.PLATFORM_KEY);
    }

    /// <summary>同一个键也用于 Mojang 运行时的安装目录名(<c>&lt;component&gt;-&lt;PLATFORM_KEY&gt;</c>)。</summary>
    [Fact]
    public void Mac_install_directory_names_follow_the_platform_key()
    {
        Assert.Equal("java-runtime-gamma-mac-os-arm64", "java-runtime-gamma-" + Java.PlatformKeyFor("osx", "arm64"));
        Assert.Equal("jre-legacy-mac-os", "jre-legacy-" + Java.PlatformKeyFor("osx", "x64"));
    }
}
