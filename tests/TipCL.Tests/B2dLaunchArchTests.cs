using System.Collections.Generic;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2d 第 1 项:<see cref="LaunchArch"/>(本次启动的目标架构)。全部是纯函数调用,不联网、不起进程、
/// 不碰磁盘。
/// 夹具 = B2b 取证的 8 个线上版本形状(见 <see cref="B2dFixtures"/> 的注释),期望值来自
/// 「B2b 第 0 步的线上证据 + MacNativeSupport 的判定口径」(PROGRESS.md 的 B2b 第 0/3/6 项):
/// 1.18.2 及更老的 mac 原生库只有 x86_64 → Apple 芯片上退 x64;1.19.4 起全都有 arm64 → 不动。
/// 本机是 Windows,osx 的真实行为**未验证**,这里钉的是纯函数语义。
/// </summary>
public class B2dLaunchArchTests
{
    /// <summary>osx + arm64:6 个老版本 → x64;1.19.4 / 26.3 → arm64(原生跑)。</summary>
    [Theory]
    [InlineData("1.7.10", "x64")]
    [InlineData("1.8.9", "x64")]
    [InlineData("1.12.2", "x64")]
    [InlineData("1.13.2", "x64")]
    [InlineData("1.16.5", "x64")]
    [InlineData("1.18.2", "x64")]
    [InlineData("1.19.4", "arm64")]
    [InlineData("26.3", "arm64")]
    public void Apple_silicon_legacy_versions_get_x64(string id, string expected)
    {
        Assert.Equal(expected, LaunchArch.For(B2dFixtures.ById(id), "osx", "arm64", "14.5"));
    }

    /// <summary>Intel Mac(osx + 进程 x64):一律返回进程架构 x64。8 个版本逐格断言。</summary>
    [Theory]
    [InlineData("1.7.10")]
    [InlineData("1.8.9")]
    [InlineData("1.12.2")]
    [InlineData("1.13.2")]
    [InlineData("1.16.5")]
    [InlineData("1.18.2")]
    [InlineData("1.19.4")]
    [InlineData("26.3")]
    public void Intel_mac_keeps_the_process_architecture(string id)
    {
        Assert.Equal("x64", LaunchArch.For(B2dFixtures.ById(id), "osx", "x64", "14.5"));
    }

    /// <summary>Windows + x64:一律返回 x64(本机平台,行为与改动前一致)。</summary>
    [Theory]
    [InlineData("1.7.10")]
    [InlineData("1.8.9")]
    [InlineData("1.12.2")]
    [InlineData("1.13.2")]
    [InlineData("1.16.5")]
    [InlineData("1.18.2")]
    [InlineData("1.19.4")]
    [InlineData("26.3")]
    public void Windows_x64_keeps_the_process_architecture(string id)
    {
        Assert.Equal("x64", LaunchArch.For(B2dFixtures.ById(id), "windows", "x64", "10.0.19045"));
    }

    /// <summary>Windows + arm64(Windows on ARM):进程架构原值,不许因为「arm64 进程 + 有 mac natives」而改。</summary>
    [Theory]
    [InlineData("1.7.10")]
    [InlineData("1.8.9")]
    [InlineData("1.12.2")]
    [InlineData("1.13.2")]
    [InlineData("1.16.5")]
    [InlineData("1.18.2")]
    [InlineData("1.19.4")]
    [InlineData("26.3")]
    public void Windows_arm64_keeps_the_process_architecture(string id)
    {
        Assert.Equal("arm64", LaunchArch.For(B2dFixtures.ById(id), "windows", "arm64", "10.0.26100"));
    }

    /// <summary>Linux + arm64:进程架构原值(判定里只有 osx 分支)。</summary>
    [Theory]
    [InlineData("1.7.10")]
    [InlineData("1.8.9")]
    [InlineData("1.12.2")]
    [InlineData("1.13.2")]
    [InlineData("1.16.5")]
    [InlineData("1.18.2")]
    [InlineData("1.19.4")]
    [InlineData("26.3")]
    public void Linux_arm64_keeps_the_process_architecture(string id)
    {
        Assert.Equal("arm64", LaunchArch.For(B2dFixtures.ById(id), "linux", "arm64", "6.8.0"));
    }

    /// <summary>null 版本 JSON:不改进程架构(不抛异常)。</summary>
    [Theory]
    [InlineData("osx", "arm64", "arm64")]
    [InlineData("osx", "x64", "x64")]
    [InlineData("windows", "x64", "x64")]
    [InlineData("linux", "arm64", "arm64")]
    public void Null_version_json_keeps_the_process_architecture(string os, string processArch, string expected)
    {
        Assert.Equal(expected, LaunchArch.For(null, os, processArch, "14.5"));
    }

    /// <summary>空 libraries:没有 mac 原生库 → 不改进程架构。</summary>
    [Theory]
    [InlineData("osx", "arm64", "arm64")]
    [InlineData("osx", "x64", "x64")]
    public void Version_without_libraries_keeps_the_process_architecture(string os, string processArch, string expected)
    {
        Assert.Equal(expected, LaunchArch.For(B2dFixtures.Version(), os, processArch, "14.5"));
        Assert.Equal(expected, LaunchArch.For(new PyDict(), os, processArch, "14.5"));
    }

    /// <summary>人造的 Mixed(一部分库有 arm64、一部分只有 x86_64)→ x64,依据见 LaunchArch 的类注释。</summary>
    [Fact]
    public void Mixed_goes_to_x64()
    {
        PyDict json = B2dFixtures.MixedIsolated();
        Assert.Equal(MacNativeKind.Mixed, MacNativeSupport.Analyze(json, "osx", "arm64", "14.5").Kind);
        Assert.Equal("x64", LaunchArch.For(json, "osx", "arm64", "14.5"));
    }

    /// <summary>人造的 NoMacNatives(只有 windows / linux 的 natives)→ 不改进程架构(不是「退 x64」)。</summary>
    [Fact]
    public void No_mac_natives_keeps_the_process_architecture()
    {
        PyDict json = B2dFixtures.NoMacNativesBuilder();
        Assert.Equal(MacNativeKind.NoMacNatives, MacNativeSupport.Analyze(json, "osx", "arm64", "14.5").Kind);
        Assert.Equal("arm64", LaunchArch.For(json, "osx", "arm64", "14.5"));
    }

    /// <summary>Arm64Native(1.19.4 / 26.3)→ 原样 arm64(与上面那组 Theory 同一个事实,这里显式钉 Kind)。</summary>
    [Theory]
    [InlineData("1.19.4")]
    [InlineData("26.3")]
    public void Arm64_native_versions_stay_on_arm64(string id)
    {
        PyDict json = B2dFixtures.ById(id);
        Assert.Equal(MacNativeKind.Arm64Native, MacNativeSupport.Analyze(json, "osx", "arm64", "14.5").Kind);
        Assert.Equal("arm64", LaunchArch.For(json, "osx", "arm64", "14.5"));
    }

    /// <summary>目标架构是版本的确定函数:同一个版本连算两次结果相同,且与 osVersion 无关
    /// (vanilla 这 8 份 JSON 的 libraries / arguments 里没有任何 os.version 规则,见 B2b 第 0 步)。</summary>
    [Fact]
    public void Target_architecture_is_a_deterministic_function_of_the_version()
    {
        foreach (string id in B2dFixtures.LiveIds)
        {
            PyDict first = B2dFixtures.ById(id);
            PyDict second = B2dFixtures.ById(id);
            Assert.Equal(LaunchArch.For(first, "osx", "arm64", "14.5"), LaunchArch.For(second, "osx", "arm64", "14.5"));
            Assert.Equal(
                LaunchArch.For(first, "osx", "arm64", "14.5"),
                LaunchArch.For(first, "osx", "arm64", "11.0"));
            Assert.Equal(
                LaunchArch.For(first, "osx", "arm64", "26.0"),
                LaunchArch.For(first, "osx", "arm64", "14.5"));
        }
    }

    /// <summary>Apple 芯片上退 x64 的版本集合恰好是 1.18.2 及更老的 6 个(逐格复核,顺序无关)。</summary>
    [Fact]
    public void Only_the_six_legacy_versions_are_redirected()
    {
        List<string> list = new List<string>();
        foreach (string id in B2dFixtures.LiveIds)
        {
            if (LaunchArch.For(B2dFixtures.ById(id), "osx", "arm64", "14.5") == "x64")
            {
                list.Add(id);
            }
        }
        Assert.Equal(new List<string>(B2dFixtures.X64OnlyIds), list);
    }

    /// <summary>Windows 钉子:公开入口(只传版本 JSON)在本机恒等于进程架构,因为本机是 windows + x64。</summary>
    [Fact]
    public void Public_entry_equals_the_process_architecture_on_this_machine()
    {
        Assert.Equal("windows", Utils.OS_NAME);
        Assert.Equal("x64", Utils.ARCH);
        foreach (string id in B2dFixtures.LiveIds)
        {
            PyDict json = B2dFixtures.ById(id);
            Assert.Equal(Utils.ARCH, LaunchArch.For(json));
            Assert.Equal(LaunchArch.For(json), LaunchArch.For(json, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION));
        }
        Assert.Equal("x64", LaunchArch.For(null));
        Assert.Equal("x64", LaunchArch.For(B2dFixtures.Version()));
    }

    /// <summary>注入本机三字段 == 注入字面量 windows + x64(公开入口与内部重载的一致性钉子)。</summary>
    [Theory]
    [InlineData("1.7.10")]
    [InlineData("1.16.5")]
    [InlineData("1.19.4")]
    public void Injecting_this_machine_equals_injecting_the_literals(string id)
    {
        PyDict json = B2dFixtures.ById(id);
        Assert.Equal(
            LaunchArch.For(json, "windows", "x64", Utils.OS_VERSION),
            LaunchArch.For(json, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION));
    }
}
