using System;
using System.Collections.Generic;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2b 第 2 项:natives 选择的可注入重载 + mac 现状钉子。
///
/// 夹具来源:线上版本 JSON,查询时间 <b>2026-11-12 19:27:43 +08:00</b>,
/// 地址 <c>https://piston-meta.mojang.com/v1/packages/&lt;sha1&gt;/&lt;id&gt;.json</c>(每个夹具注明 id);
/// 每个库条目的 <c>name</c> / <c>natives</c> / <c>downloads.classifiers</c> 键 / <c>rules</c> 都照抄原文,
/// 只把 classifier 的 url/sha1 省掉(classifier 的**键**才是选择逻辑的输入)。响应体留在 <c>artifacts/b2b/</c>(不入库)。
///
/// 期望值来源:源码 <c>Installer.cs</c> 的 <c>NativeArchToken</c> / <c>SubstNativeKey</c> /
/// <c>SelectNativeClassifier</c>(含局部函数 rank 的 0 / 1 / 2 三档)。
/// 「osx + arm64 选不到」是本包**特意钉住的现状**,不是期望的正确行为。
/// </summary>
public class B2bNativeClassifierTests
{
    private static PyDict Lib(string name, PyDict natives, string[] classifierKeys, params PyDict[] rules)
    {
        PyDict lib = PyDict.Of("name", name);
        if (natives != null && natives.Count > 0)
        {
            lib["natives"] = natives;
        }
        if (classifierKeys != null && classifierKeys.Length > 0)
        {
            PyDict classifiers = new PyDict();
            foreach (string key in classifierKeys)
            {
                classifiers[key] = PyDict.Of("path", "org/example/" + key + ".jar");
            }
            lib["downloads"] = PyDict.Of("classifiers", classifiers);
        }
        if (rules != null && rules.Length > 0)
        {
            List<object> list = new List<object>();
            foreach (PyDict r in rules)
            {
                list.Add(r);
            }
            lib["rules"] = list;
        }
        return lib;
    }

    private static PyDict Natives(params object[] kv)
    {
        return PyDict.Of(kv);
    }

    private static PyDict Allow(string osName) => PyDict.Of("action", "allow", "os", PyDict.Of("name", osName));

    private static PyDict AllowAll() => PyDict.Of("action", "allow");

    private static PyDict Disallow(string osName) => PyDict.Of("action", "disallow", "os", PyDict.Of("name", osName));

    /// <summary>1.7.10 <c>LIB#1</c>:org.lwjgl.lwjgl:lwjgl-platform:2.9.1(classifiersMac = natives-osx)。</summary>
    private static PyDict Lwjgl2Platform() => Lib(
        "org.lwjgl.lwjgl:lwjgl-platform:2.9.1",
        Natives("linux", "natives-linux", "osx", "natives-osx", "windows", "natives-windows"),
        new[] { "natives-linux", "natives-osx", "natives-windows" });

    /// <summary>1.7.10 <c>LIB#3</c>:tv.twitch:twitch-platform:5.16(windows 那个键带 <c>${arch}</c>)。</summary>
    private static PyDict TwitchPlatform() => Lib(
        "tv.twitch:twitch-platform:5.16",
        Natives("linux", "natives-linux", "osx", "natives-osx", "windows", "natives-windows-${arch}"),
        new[] { "natives-linux", "natives-osx", "natives-windows-32", "natives-windows-64" },
        AllowAll(),
        Disallow("linux"));

    /// <summary>1.8.9 <c>LIB#1</c>/<c>LIB#2</c>:同一个库两个 nightly,一个 disallow osx、一个 allow osx。</summary>
    private static PyDict Lwjgl2PlatformDisallowOsx() => Lib(
        "org.lwjgl.lwjgl:lwjgl-platform:2.9.4-nightly-20150209",
        Natives("linux", "natives-linux", "osx", "natives-osx", "windows", "natives-windows"),
        new[] { "natives-linux", "natives-osx", "natives-windows" },
        AllowAll(),
        Disallow("osx"));

    private static PyDict Lwjgl2PlatformAllowOsx() => Lib(
        "org.lwjgl.lwjgl:lwjgl-platform:2.9.2-nightly-20140822",
        Natives("linux", "natives-linux", "osx", "natives-osx", "windows", "natives-windows"),
        new[] { "natives-linux", "natives-osx", "natives-windows" },
        Allow("osx"));

    /// <summary>1.12.2 <c>LIB#5</c>:ca.weblite:java-objc-bridge:1.0.0(只有 mac 一个平台)。</summary>
    private static PyDict JavaObjcBridge() => Lib(
        "ca.weblite:java-objc-bridge:1.0.0",
        Natives("osx", "natives-osx"),
        new[] { "natives-osx" },
        Allow("osx"));

    /// <summary>1.13.2 <c>LIB#1</c>:org.lwjgl:lwjgl:3.1.6(natives.osx = natives-macos)。</summary>
    private static PyDict Lwjgl316() => Lib(
        "org.lwjgl:lwjgl:3.1.6",
        Natives("linux", "natives-linux", "osx", "natives-macos", "windows", "natives-windows"),
        new[] { "natives-linux", "natives-macos", "natives-windows" });

    /// <summary>1.16.5 <c>LIB#1</c>:org.lwjgl:lwjgl:3.2.1(只给 osx 的 natives,带 allow osx 规则)。</summary>
    private static PyDict Lwjgl321OsxOnly() => Lib(
        "org.lwjgl:lwjgl:3.2.1",
        Natives("osx", "natives-macos"),
        new[] { "natives-macos" },
        Allow("osx"));

    /// <summary>1.16.5 <c>LIB#2</c>:org.lwjgl:lwjgl:3.2.2(给 linux/windows 且 disallow osx)。</summary>
    private static PyDict Lwjgl322NotOsx() => Lib(
        "org.lwjgl:lwjgl:3.2.2",
        Natives("linux", "natives-linux", "windows", "natives-windows"),
        new[] { "natives-linux", "natives-windows" },
        AllowAll(),
        Disallow("osx"));

    /// <summary>1.16.5 <c>LIB#15</c>:com.mojang:text2speech:1.11.3(没有 mac 的 natives)。</summary>
    private static PyDict Text2Speech() => Lib(
        "com.mojang:text2speech:1.11.3",
        Natives("linux", "natives-linux", "windows", "natives-windows"),
        new[] { "natives-linux", "natives-windows" });

    /// <summary>1.19.4 <c>LIB#1</c>:org.lwjgl:lwjgl-glfw:3.3.1:natives-macos(没有 natives 字段,只有 artifact)。</summary>
    private static PyDict ModernNativesMacos() => Lib(
        "org.lwjgl:lwjgl-glfw:3.3.1:natives-macos", null, null, Allow("osx"));

    /// <summary>1.19.4 <c>LIB#2</c>:org.lwjgl:lwjgl-glfw:3.3.1:natives-macos-arm64。</summary>
    private static PyDict ModernNativesMacosArm64() => Lib(
        "org.lwjgl:lwjgl-glfw:3.3.1:natives-macos-arm64", null, null, Allow("osx"));

    private static IEnumerable<PyDict> AllFixtures()
    {
        yield return Lwjgl2Platform();
        yield return TwitchPlatform();
        yield return Lwjgl2PlatformDisallowOsx();
        yield return Lwjgl2PlatformAllowOsx();
        yield return JavaObjcBridge();
        yield return Lwjgl316();
        yield return Lwjgl321OsxOnly();
        yield return Lwjgl322NotOsx();
        yield return Text2Speech();
        yield return ModernNativesMacos();
        yield return ModernNativesMacosArm64();
    }

    /// <summary>NativeArchToken:x86 -> 32,其余一律 64(源码 206-213 行)。</summary>
    [Theory]
    [InlineData("x86", "32")]
    [InlineData("x64", "64")]
    [InlineData("arm64", "64")]
    [InlineData("ia64", "64")]
    [InlineData("", "64")]
    public void Native_arch_token(string arch, string expected)
    {
        Assert.Equal(expected, Installer.NativeArchToken(arch));
    }

    /// <summary>Windows 钉子:public NativeArchToken() 与注入本机 ARCH 的结果相等(本机 x64 -> "64")。</summary>
    [Fact]
    public void Native_arch_token_windows_pin()
    {
        Assert.Equal(Installer.NativeArchToken(Utils.ARCH), Installer.NativeArchToken());
        Assert.Equal("64", Installer.NativeArchToken());
    }

    /// <summary>SubstNativeKey 只替换 ${arch}(源码 232-239 行)。</summary>
    [Theory]
    [InlineData("natives-windows-${arch}", "x86", "natives-windows-32")]
    [InlineData("natives-windows-${arch}", "x64", "natives-windows-64")]
    [InlineData("natives-windows-${arch}", "arm64", "natives-windows-64")]
    [InlineData("natives-osx", "arm64", "natives-osx")]
    [InlineData(null, "x64", null)]
    [InlineData("", "x64", "")]
    public void Subst_native_key(string? key, string arch, string? expected)
    {
        Assert.Equal(expected, Installer.SubstNativeKey(key, arch));
    }

    /// <summary>Windows 钉子:public SubstNativeKey(key) 与注入本机 ARCH 相等(本机 x64 -> 64)。</summary>
    [Fact]
    public void Subst_native_key_windows_pin()
    {
        Assert.Equal(Installer.SubstNativeKey("natives-windows-${arch}", Utils.ARCH), Installer.SubstNativeKey("natives-windows-${arch}"));
        Assert.Equal("natives-windows-64", Installer.SubstNativeKey("natives-windows-${arch}"));
    }

    /// <summary>1.7.10 的 lwjgl-platform:osx + x64 选 natives-osx;osx + arm64 现状选不到(钉住)。</summary>
    [Theory]
    [InlineData("osx", "x64", "natives-osx")]
    [InlineData("osx", "arm64", null)]
    [InlineData("osx", "x86", "natives-osx")]
    [InlineData("windows", "x64", "natives-windows")]
    [InlineData("windows", "x86", "natives-windows")]
    [InlineData("linux", "x64", "natives-linux")]
    public void Lwjgl_2_9_platform_classifier(string os, string arch, string? expected)
    {
        Assert.Equal(expected, Installer.SelectNativeClassifier(Lwjgl2Platform(), os, arch));
    }

    /// <summary>windows 的 natives 键带 <c>${arch}</c>:x86 / x64 下换成 32 / 64 后缀,arm64 下换成 64
    /// 但 64 不在 arm64 的后缀表(arm64 / aarch64)里,于是**选不到**(实测,钉住现状)。</summary>
    [Theory]
    [InlineData("x86", "natives-windows-32")]
    [InlineData("x64", "natives-windows-64")]
    [InlineData("arm64", null)]
    public void Twitch_platform_windows_arch_token(string arch, string? expected)
    {
        Assert.Equal(expected, Installer.SelectNativeClassifier(TwitchPlatform(), "windows", arch));
        Assert.Equal("natives-osx", Installer.SelectNativeClassifier(TwitchPlatform(), "osx", "x64"));
    }

    /// <summary>1.8.9 / 1.12.2 的老式 natives-osx 库:osx + x64 能选到,osx + arm64 现状选不到。</summary>
    [Theory]
    [InlineData("osx", "x64", "natives-osx")]
    [InlineData("osx", "arm64", null)]
    public void Legacy_natives_osx_libraries(string os, string arch, string? expected)
    {
        Assert.Equal(expected, Installer.SelectNativeClassifier(Lwjgl2PlatformAllowOsx(), os, arch));
        Assert.Equal(expected, Installer.SelectNativeClassifier(JavaObjcBridge(), os, arch));
    }

    /// <summary>选择器不看 rules(过滤由调用方做):disallow osx 的那条仍会被选成 natives-osx,
    /// 但 <c>CheckRules(..., "osx")</c> 为 false,所以调用方会跳过它。</summary>
    [Fact]
    public void Classifier_selection_ignores_rules_but_check_rules_does_not()
    {
        Assert.Equal("natives-osx", Installer.SelectNativeClassifier(Lwjgl2PlatformDisallowOsx(), "osx", "x64"));
        Assert.False(Utils.CheckRules(Lwjgl2PlatformDisallowOsx().Get("rules"), null, "osx", "x64", Utils.OS_VERSION));
        Assert.True(Utils.CheckRules(Lwjgl2PlatformAllowOsx().Get("rules"), null, "osx", "x64", Utils.OS_VERSION));
    }

    /// <summary>1.13.2 的 LWJGL 3.1.6(natives.osx = natives-macos):osx + x64 选 natives-macos,osx + arm64 现状选不到。</summary>
    [Theory]
    [InlineData("osx", "x64", "natives-macos")]
    [InlineData("osx", "arm64", null)]
    [InlineData("windows", "x64", "natives-windows")]
    [InlineData("linux", "x64", "natives-linux")]
    public void Lwjgl_3_1_6_classifier(string os, string arch, string? expected)
    {
        Assert.Equal(expected, Installer.SelectNativeClassifier(Lwjgl316(), os, arch));
    }

    /// <summary>1.16.5 的 LWJGL 3.2.1 条目只给 osx:Windows 上选不到,osx + x64 选 natives-macos。</summary>
    [Theory]
    [InlineData("osx", "x64", "natives-macos")]
    [InlineData("osx", "arm64", null)]
    [InlineData("windows", "x64", null)]
    [InlineData("linux", "x64", null)]
    public void Lwjgl_3_2_1_osx_only_entry(string os, string arch, string? expected)
    {
        Assert.Equal(expected, Installer.SelectNativeClassifier(Lwjgl321OsxOnly(), os, arch));
    }

    /// <summary>1.16.5 的 text2speech 没有 mac 的 natives:osx 下选不到,Windows 上照旧。</summary>
    [Theory]
    [InlineData("osx", "x64", null)]
    [InlineData("osx", "arm64", null)]
    [InlineData("windows", "x64", "natives-windows")]
    public void Text2speech_has_no_mac_natives(string os, string arch, string? expected)
    {
        Assert.Equal(expected, Installer.SelectNativeClassifier(Text2Speech(), os, arch));
    }

    /// <summary>1.19.4 起的新式库(名字第 4 段才是 classifier、没有 <c>natives</c> 字段):
    /// 选择器在**任何**平台都返回 null —— 它靠库名 + rules 进 classpath,不走 natives 解压(钉住现状)。</summary>
    [Theory]
    [InlineData("osx", "x64")]
    [InlineData("osx", "arm64")]
    [InlineData("windows", "x64")]
    [InlineData("linux", "x64")]
    public void Modern_classifier_libraries_never_select_a_legacy_classifier(string os, string arch)
    {
        Assert.Null(Installer.SelectNativeClassifier(ModernNativesMacos(), os, arch));
        Assert.Null(Installer.SelectNativeClassifier(ModernNativesMacosArm64(), os, arch));
    }

    /// <summary>1.18.2 的 LWJGL 3.2.1 条目与 1.16.5 同形(org.lwjgl:lwjgl-stb:3.2.1)。</summary>
    [Theory]
    [InlineData("osx", "x64", "natives-macos")]
    [InlineData("osx", "arm64", null)]
    [InlineData("windows", "x64", null)]
    public void Lwjgl_3_2_1_stb_entry_from_1_18_2(string os, string arch, string? expected)
    {
        PyDict lib = Lib("org.lwjgl:lwjgl-stb:3.2.1", Natives("osx", "natives-macos"), new[] { "natives-macos" }, Allow("osx"));
        Assert.Equal(expected, Installer.SelectNativeClassifier(lib, os, arch));
    }

    /// <summary>【现状,不是期望的正确行为】Apple 芯片上老式 natives 分类器一律选不到 —— 三个版本各验一次。</summary>
    [Fact(DisplayName = "现状(待 Lead 裁决):osx + arm64 下老式 natives-osx / natives-macos 分类器全部选不到")]
    public void Current_state_osx_arm64_selects_no_legacy_classifier()
    {
        Assert.Null(Installer.SelectNativeClassifier(Lwjgl2Platform(), "osx", "arm64"));
        Assert.Null(Installer.SelectNativeClassifier(Lwjgl2PlatformAllowOsx(), "osx", "arm64"));
        Assert.Null(Installer.SelectNativeClassifier(JavaObjcBridge(), "osx", "arm64"));
        Assert.Null(Installer.SelectNativeClassifier(Lwjgl316(), "osx", "arm64"));
        Assert.Null(Installer.SelectNativeClassifier(Lwjgl321OsxOnly(), "osx", "arm64"));
        // 同一批夹具在 osx + x64 下都能选到 —— 差别只在 arch。
        Assert.Equal("natives-osx", Installer.SelectNativeClassifier(Lwjgl2Platform(), "osx", "x64"));
        Assert.Equal("natives-macos", Installer.SelectNativeClassifier(Lwjgl316(), "osx", "x64"));
        Assert.Equal("natives-macos", Installer.SelectNativeClassifier(Lwjgl321OsxOnly(), "osx", "x64"));
    }

    /// <summary>Windows 钉子:每个夹具上 public 方法与「注入本机 OS_NAME / ARCH」完全一致(本机 windows x64)。</summary>
    [Fact]
    public void Every_fixture_matches_the_public_method_on_this_machine()
    {
        Assert.Equal("windows", Utils.OS_NAME);
        Assert.Equal("x64", Utils.ARCH);
        foreach (PyDict lib in AllFixtures())
        {
            Assert.Equal(
                Installer.SelectNativeClassifier(lib),
                Installer.SelectNativeClassifier(lib, Utils.OS_NAME, Utils.ARCH));
        }
        Assert.Equal("natives-windows", Installer.SelectNativeClassifier(Lwjgl2Platform()));
        // 本机是 Windows,所以 1.13.2 的 LWJGL 3.1.6 会选到 windows 那一份(源码行为)。
        Assert.Equal("natives-windows", Installer.SelectNativeClassifier(Lwjgl316()));
    }
}
