using System;
using System.Collections.Generic;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2b 第 3 项:<c>MacNativeSupport.Analyze</c>(这个版本在 mac 上有没有 arm64 原生库)。
/// 这个函数本包**不接入任何调用点**,只给 Lead 裁决用。
///
/// 夹具来源:线上版本 JSON,查询时间 <b>2026-11-12 19:27:43 +08:00</b>,8 份响应体留在
/// <c>artifacts/b2b/&lt;id&gt;.json</c>(不入库)。这里只抄与 mac 有关的库条目(name / natives / classifiers 键 / rules),
/// 每个版本至少留一条「只有 windows/linux」的条目 + 1.8.9 与 1.16.5 各留一条 disallow osx 的条目,
/// 用来验证 rules 过滤。**条目数不是原文件的条目数**(原文件的逐库清单见 PROGRESS.md 的 B2b 第 0 步证据表)。
/// </summary>
public class B2bMacNativeSupportTests
{
    private static PyDict Allow(string osName) => PyDict.Of("action", "allow", "os", PyDict.Of("name", osName));

    private static PyDict AllowAll() => PyDict.Of("action", "allow");

    private static PyDict Disallow(string osName) => PyDict.Of("action", "disallow", "os", PyDict.Of("name", osName));

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

    private static PyDict OsxNatives(string osxKey)
    {
        return PyDict.Of("linux", "natives-linux", "osx", osxKey, "windows", "natives-windows");
    }

    private static PyDict NonMacNatives()
    {
        return PyDict.Of("linux", "natives-linux", "windows", "natives-windows");
    }

    private static string[] WithOsx(string osxKey)
    {
        return new[] { "natives-linux", osxKey, "natives-windows" };
    }

    private static PyDict Version(params PyDict[] libs)
    {
        List<object> list = new List<object>();
        foreach (PyDict lib in libs)
        {
            list.Add(lib);
        }
        return PyDict.Of("libraries", list);
    }

    private static PyDict ModernMacLib(string groupArtifactVersion, string classifier)
    {
        return Lib(groupArtifactVersion + ":" + classifier, null, null, Allow("osx"));
    }

    private static PyDict VersionWithModernPairs(string version, params string[] groupArtifacts)
    {
        List<object> list = new List<object>();
        foreach (string ga in groupArtifacts)
        {
            list.Add(ModernMacLib(ga + ":" + version, "natives-macos"));
            list.Add(ModernMacLib(ga + ":" + version, "natives-macos-arm64"));
        }
        return PyDict.Of("libraries", list);
    }

    /// <summary>1.7.10(https://piston-meta.mojang.com/v1/packages/ed5d8789ed29872ea2ef1c348302b0c55e3f3468/1.7.10.json)。</summary>
    private static PyDict V1710() => Version(
        Lib("org.lwjgl.lwjgl:lwjgl-platform:2.9.1", OsxNatives("natives-osx"), WithOsx("natives-osx")),
        Lib("net.java.jinput:jinput-platform:2.0.5", OsxNatives("natives-osx"), WithOsx("natives-osx")),
        Lib("tv.twitch:twitch-platform:5.16", OsxNatives("natives-osx"), WithOsx("natives-osx"), AllowAll(), Disallow("linux")),
        Lib("tv.twitch:twitch-external-platform:4.5", PyDict.Of("windows", "natives-windows-${arch}"), null, Allow("windows")));

    /// <summary>1.8.9(.../d546f1707a3f2b7d034eece5ea2e311eda875787/1.8.9.json):含一条 disallow osx。</summary>
    private static PyDict V189() => Version(
        Lib("org.lwjgl.lwjgl:lwjgl-platform:2.9.4-nightly-20150209", OsxNatives("natives-osx"), WithOsx("natives-osx"), AllowAll(), Disallow("osx")),
        Lib("org.lwjgl.lwjgl:lwjgl-platform:2.9.2-nightly-20140822", OsxNatives("natives-osx"), WithOsx("natives-osx"), Allow("osx")),
        Lib("net.java.jinput:jinput-platform:2.0.5", OsxNatives("natives-osx"), WithOsx("natives-osx")),
        Lib("tv.twitch:twitch-platform:6.5", OsxNatives("natives-osx"), WithOsx("natives-osx"), AllowAll(), Disallow("linux")),
        Lib("tv.twitch:twitch-external-platform:4.5", PyDict.Of("windows", "natives-windows-${arch}"), null, Allow("windows")));

    /// <summary>1.12.2(.../832d95b9f40699d4961394dcf6cf549e65f15dc5/1.12.2.json)。</summary>
    private static PyDict V1122() => Version(
        Lib("org.lwjgl.lwjgl:lwjgl-platform:2.9.4-nightly-20150209", OsxNatives("natives-osx"), WithOsx("natives-osx"), AllowAll(), Disallow("osx")),
        Lib("org.lwjgl.lwjgl:lwjgl-platform:2.9.2-nightly-20140822", OsxNatives("natives-osx"), WithOsx("natives-osx"), Allow("osx")),
        Lib("net.java.jinput:jinput-platform:2.0.5", OsxNatives("natives-osx"), WithOsx("natives-osx")),
        Lib("com.mojang:text2speech:1.10.3", NonMacNatives(), new[] { "natives-linux", "natives-windows" }),
        Lib("ca.weblite:java-objc-bridge:1.0.0", PyDict.Of("osx", "natives-osx"), new[] { "natives-osx" }, Allow("osx")));

    /// <summary>1.13.2(.../fa3ddc22146c46bfeb0e9d322c6f83b937e25005/1.13.2.json):LWJGL 3.1.6 用的是 natives-macos。</summary>
    private static PyDict V1132() => Version(
        Lib("org.lwjgl:lwjgl:3.1.6", OsxNatives("natives-macos"), WithOsx("natives-macos")),
        Lib("org.lwjgl:lwjgl-jemalloc:3.1.6", OsxNatives("natives-macos"), WithOsx("natives-macos")),
        Lib("org.lwjgl:lwjgl-openal:3.1.6", OsxNatives("natives-macos"), WithOsx("natives-macos")),
        Lib("org.lwjgl:lwjgl-opengl:3.1.6", OsxNatives("natives-macos"), WithOsx("natives-macos")),
        Lib("org.lwjgl:lwjgl-glfw:3.1.6", OsxNatives("natives-macos"), WithOsx("natives-macos")),
        Lib("org.lwjgl:lwjgl-stb:3.1.6", OsxNatives("natives-macos"), WithOsx("natives-macos")),
        Lib("com.mojang:text2speech:1.10.3", NonMacNatives(), new[] { "natives-linux", "natives-windows" }),
        Lib("ca.weblite:java-objc-bridge:1.0.0", PyDict.Of("osx", "natives-osx"), new[] { "natives-osx" }, Allow("osx")));

    /// <summary>1.16.5(.../fba9f7833e858a1257d810d21a3a9e3c967f9077/1.16.5.json):3.2.1 给 osx,3.2.2 给 linux/windows 且 disallow osx。</summary>
    private static PyDict V1165() => Version(
        Lib("org.lwjgl:lwjgl:3.2.1", PyDict.Of("osx", "natives-macos"), new[] { "natives-macos" }, Allow("osx")),
        Lib("org.lwjgl:lwjgl:3.2.2", NonMacNatives(), new[] { "natives-linux", "natives-windows" }, AllowAll(), Disallow("osx")),
        Lib("org.lwjgl:lwjgl-jemalloc:3.2.1", PyDict.Of("osx", "natives-macos"), new[] { "natives-macos" }, Allow("osx")),
        Lib("org.lwjgl:lwjgl-glfw:3.2.1", PyDict.Of("osx", "natives-macos"), new[] { "natives-macos" }, Allow("osx")),
        Lib("com.mojang:text2speech:1.11.3", NonMacNatives(), new[] { "natives-linux", "natives-windows" }),
        Lib("ca.weblite:java-objc-bridge:1.0.0", PyDict.Of("osx", "natives-osx"), new[] { "natives-osx" }, Allow("osx")));

    /// <summary>1.18.2(.../334b33fcba3c9be4b7514624c965256535bd7eba/1.18.2.json):与 1.16.5 同形。</summary>
    private static PyDict V1182() => Version(
        Lib("org.lwjgl:lwjgl:3.2.1", PyDict.Of("osx", "natives-macos"), new[] { "natives-macos" }, Allow("osx")),
        Lib("org.lwjgl:lwjgl:3.2.2", NonMacNatives(), new[] { "natives-linux", "natives-windows" }, AllowAll(), Disallow("osx")),
        Lib("org.lwjgl:lwjgl-stb:3.2.1", PyDict.Of("osx", "natives-macos"), new[] { "natives-macos" }, Allow("osx")),
        Lib("ca.weblite:java-objc-bridge:1.0.0", PyDict.Of("osx", "natives-osx"), new[] { "natives-osx" }, Allow("osx")));

    /// <summary>1.19.4(.../509d363de59da2f2350fee270f11ace8a9412948/1.19.4.json):7 个原生库,每个都有 macos 与 macos-arm64 两条。</summary>
    private static PyDict V1194() => VersionWithModernPairs(
        "3.3.1",
        "org.lwjgl:lwjgl-glfw",
        "org.lwjgl:lwjgl-jemalloc",
        "org.lwjgl:lwjgl-openal",
        "org.lwjgl:lwjgl-opengl",
        "org.lwjgl:lwjgl-stb",
        "org.lwjgl:lwjgl-tinyfd",
        "org.lwjgl:lwjgl");

    /// <summary>26.3(latest.release,.../9f67e10ed013deeaad20ac3c800652765d493408/26.3.json):12 个原生库。</summary>
    private static PyDict V263() => VersionWithModernPairs(
        "3.4.3",
        "com.mojang:jtracy",
        "org.lwjgl:lwjgl-freetype",
        "org.lwjgl:lwjgl-jemalloc",
        "org.lwjgl:lwjgl-openal",
        "org.lwjgl:lwjgl-opengl",
        "org.lwjgl:lwjgl-sdl",
        "org.lwjgl:lwjgl-shaderc",
        "org.lwjgl:lwjgl-spvc",
        "org.lwjgl:lwjgl-stb",
        "org.lwjgl:lwjgl-vma",
        "org.lwjgl:lwjgl-vulkan",
        "org.lwjgl:lwjgl");

    /// <summary>8 个取证版本的结论。<c>X64Only</c> 的依据是整份 JSON 里没有任何 arm64 字样。</summary>
    [Theory]
    [InlineData("1.7.10", MacNativeKind.X64Only)]
    [InlineData("1.8.9", MacNativeKind.X64Only)]
    [InlineData("1.12.2", MacNativeKind.X64Only)]
    [InlineData("1.13.2", MacNativeKind.X64Only)]
    [InlineData("1.16.5", MacNativeKind.X64Only)]
    [InlineData("1.18.2", MacNativeKind.X64Only)]
    [InlineData("1.19.4", MacNativeKind.Arm64Native)]
    [InlineData("26.3", MacNativeKind.Arm64Native)]
    public void Live_versions_are_classified(string id, MacNativeKind expected)
    {
        PyDict json = id switch
        {
            "1.7.10" => V1710(),
            "1.8.9" => V189(),
            "1.12.2" => V1122(),
            "1.13.2" => V1132(),
            "1.16.5" => V1165(),
            "1.18.2" => V1182(),
            "1.19.4" => V1194(),
            _ => V263(),
        };
        MacNativeSupportReport report = MacNativeSupport.Analyze(json);
        Assert.Equal(expected, report.Kind);
    }

    /// <summary>1.19.4:7 个原生库全部有 arm64 变体,缺 arm64 的清单为空。</summary>
    [Fact]
    public void Version_1194_has_arm64_for_every_mac_native_library()
    {
        MacNativeSupportReport report = MacNativeSupport.Analyze(V1194());
        Assert.Equal(MacNativeKind.Arm64Native, report.Kind);
        Assert.Equal(7, report.MacNativeLibraries.Count);
        Assert.Equal(7, report.LibrariesWithArm64.Count);
        Assert.Empty(report.LibrariesWithoutArm64);
        Assert.Contains("org.lwjgl:lwjgl-glfw", report.LibrariesWithArm64);
    }

    /// <summary>26.3:12 个原生库全部有 arm64 变体。</summary>
    [Fact]
    public void Version_263_has_arm64_for_every_mac_native_library()
    {
        MacNativeSupportReport report = MacNativeSupport.Analyze(V263());
        Assert.Equal(MacNativeKind.Arm64Native, report.Kind);
        Assert.Equal(12, report.MacNativeLibraries.Count);
        Assert.Empty(report.LibrariesWithoutArm64);
        Assert.Contains("com.mojang:jtracy", report.LibrariesWithArm64);
    }

    /// <summary>1.16.5:每个 mac 原生库都只有 x86_64,清单里要如实列出来。</summary>
    [Fact]
    public void Version_1165_lists_the_libraries_that_lack_arm64()
    {
        MacNativeSupportReport report = MacNativeSupport.Analyze(V1165());
        Assert.Equal(MacNativeKind.X64Only, report.Kind);
        Assert.Empty(report.LibrariesWithArm64);
        Assert.Contains("org.lwjgl:lwjgl", report.LibrariesWithoutArm64);
        Assert.Contains("org.lwjgl:lwjgl-glfw", report.LibrariesWithoutArm64);
        Assert.Contains("ca.weblite:java-objc-bridge", report.LibrariesWithoutArm64);
        // 只有 windows/linux 的 natives 不算 mac 原生库。
        Assert.DoesNotContain("com.mojang:text2speech", report.LibrariesWithoutArm64);
    }

    /// <summary>1.8.9:disallow osx 的那条 nightly 被 rules 过滤掉,只剩 allow osx 的那条。</summary>
    [Fact]
    public void Version_189_drops_the_entry_that_disallows_osx()
    {
        MacNativeSupportReport report = MacNativeSupport.Analyze(V189());
        Assert.Equal(MacNativeKind.X64Only, report.Kind);
        Assert.Contains("org.lwjgl.lwjgl:lwjgl-platform", report.LibrariesWithoutArm64);
        Assert.DoesNotContain("tv.twitch:twitch-external-platform", report.LibrariesWithoutArm64);
        Assert.Equal(3, report.MacNativeLibraries.Count);
    }

    /// <summary>合成夹具:Mixed —— 一部分原生库有 arm64,一部分只有 x86_64(报告里要带清单)。</summary>
    [Fact]
    public void A_partially_ported_version_is_mixed_and_lists_the_gap()
    {
        PyDict json = Version(
            ModernMacLib("org.lwjgl:lwjgl-glfw:3.3.1", "natives-macos"),
            ModernMacLib("org.lwjgl:lwjgl-glfw:3.3.1", "natives-macos-arm64"),
            Lib("ca.weblite:java-objc-bridge:1.0.0", PyDict.Of("osx", "natives-osx"), new[] { "natives-osx" }, Allow("osx")));
        MacNativeSupportReport report = MacNativeSupport.Analyze(json);
        Assert.Equal(MacNativeKind.Mixed, report.Kind);
        Assert.Equal(new[] { "ca.weblite:java-objc-bridge" }, report.LibrariesWithoutArm64);
        Assert.Equal(new[] { "org.lwjgl:lwjgl-glfw" }, report.LibrariesWithArm64);
    }

    /// <summary>合成夹具:只有 windows / linux 的 natives -> NoMacNatives。</summary>
    [Fact]
    public void A_version_without_any_mac_natives_is_reported_as_no_mac_natives()
    {
        MacNativeSupportReport report = MacNativeSupport.Analyze(Version(
            Lib("com.mojang:text2speech:1.10.3", NonMacNatives(), new[] { "natives-linux", "natives-windows" }),
            Lib("org.lwjgl:lwjgl-glfw:3.3.1:natives-windows", null, null, Allow("windows"))));
        Assert.Equal(MacNativeKind.NoMacNatives, report.Kind);
        Assert.Empty(report.MacNativeLibraries);
    }

    /// <summary>空输入不抛:没有 libraries 键 / null 都是 NoMacNatives。</summary>
    [Fact]
    public void Empty_input_is_no_mac_natives()
    {
        Assert.Equal(MacNativeKind.NoMacNatives, MacNativeSupport.Analyze(new PyDict()).Kind);
        Assert.Equal(MacNativeKind.NoMacNatives, MacNativeSupport.Analyze(null).Kind);
    }

    /// <summary>rules 里的 os.arch 也按注入值过滤(vanilla 的 libraries 目前没有这种规则,这里用合成夹具证明参数是通的)。</summary>
    [Theory]
    [InlineData("x86", MacNativeKind.X64Only)]
    [InlineData("x64", MacNativeKind.NoMacNatives)]
    [InlineData("arm64", MacNativeKind.NoMacNatives)]
    public void Os_arch_rules_are_honoured(string arch, MacNativeKind expected)
    {
        PyDict json = Version(Lib(
            "com.example:only-x86:1.0",
            PyDict.Of("osx", "natives-osx"),
            new[] { "natives-osx" },
            PyDict.Of("action", "allow", "os", PyDict.Of("name", "osx", "arch", "x86"))));
        Assert.Equal(expected, MacNativeSupport.Analyze(json, "osx", arch, "14.5").Kind);
    }

    /// <summary>函数按注入的 os 工作(内部重载等价于「问这个平台的原生库架构覆盖」):
    /// 1.16.5 在 windows 下算出来的是 windows 自己的原生库(text2speech),mac 那两个专有库不算。</summary>
    [Fact]
    public void The_os_parameter_selects_the_platform_aliases()
    {
        MacNativeSupportReport mac = MacNativeSupport.Analyze(V1165(), "osx", "arm64", Utils.OS_VERSION);
        MacNativeSupportReport win = MacNativeSupport.Analyze(V1165(), "windows", "x64", Utils.OS_VERSION);
        Assert.Contains("com.mojang:text2speech", win.LibrariesWithoutArm64);
        Assert.DoesNotContain("com.mojang:text2speech", mac.MacNativeLibraries);
        Assert.Contains("ca.weblite:java-objc-bridge", mac.LibrariesWithoutArm64);
        Assert.DoesNotContain("ca.weblite:java-objc-bridge", win.MacNativeLibraries);
    }

    /// <summary>Windows 钉子:默认入口(Analyze(json))与显式注入 osx + arm64 完全一致 ——
    /// 说明默认参数没有从静态字段里偷偷取 os / arch(只取 OS_VERSION 给 os.version 规则用)。</summary>
    [Fact]
    public void Windows_pin_default_entry_equals_explicit_osx_arm64()
    {
        foreach (PyDict json in new[] { V1710(), V1165(), V1194(), V263() })
        {
            MacNativeSupportReport a = MacNativeSupport.Analyze(json);
            MacNativeSupportReport b = MacNativeSupport.Analyze(json, "osx", "arm64", Utils.OS_VERSION);
            Assert.Equal(b.Kind, a.Kind);
            Assert.Equal(b.MacNativeLibraries, a.MacNativeLibraries);
            Assert.Equal(b.LibrariesWithArm64, a.LibrariesWithArm64);
            Assert.Equal(b.LibrariesWithoutArm64, a.LibrariesWithoutArm64);
        }
    }

    /// <summary>报告里的库名归组到 group:artifact,同一个原生库的 x64 / arm64 两条算一组。</summary>
    [Fact]
    public void Library_names_are_grouped_by_group_and_artifact()
    {
        MacNativeSupportReport report = MacNativeSupport.Analyze(V1194());
        Assert.DoesNotContain(report.MacNativeLibraries, (string n) => n.EndsWith(":natives-macos", StringComparison.Ordinal));
        Assert.DoesNotContain(report.MacNativeLibraries, (string n) => n.EndsWith(":natives-macos-arm64", StringComparison.Ordinal));
        Assert.Contains("org.lwjgl:lwjgl", report.MacNativeLibraries);
    }
}