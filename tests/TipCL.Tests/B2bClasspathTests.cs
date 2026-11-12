using System;
using System.Collections.Generic;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2b 第 2 项(后半):没有 <c>natives</c> 字段、靠库名 classifier + rules 区分平台的新式库,
/// 到底进不进 classpath。判断逻辑原本内联在 <c>McLauncher.BuildLaunchCommand</c> 里
/// (改动前的 541-573 行),本次抽成可注入 os / arch / osVersion 的
/// <c>McLauncher._classpath_path</c>,行为逐字保留。
///
/// 夹具来源:线上版本 JSON,查询时间 <b>2026-11-12 19:27:43 +08:00</b>(<c>1.19.4</c> 与 <c>1.16.5</c>,地址见
/// <c>B2bExpandArgsTests</c> 的注释);库名 / <c>downloads.artifact.path</c> / <c>rules</c> 照抄原文,
/// sha1 与 size 省掉(它们不参与这个判断)。
/// </summary>
public class B2bClasspathTests
{
    private const string LibsDir = "C:\\tipcl\\instances\\i\\libraries";

    private static PyDict Lib(string name, string artifactPath, params PyDict[] rules)
    {
        PyDict lib = PyDict.Of("name", name);
        if (artifactPath != null)
        {
            lib["downloads"] = PyDict.Of("artifact", PyDict.Of("path", artifactPath));
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

    private static PyDict Allow(string osName) => PyDict.Of("action", "allow", "os", PyDict.Of("name", osName));

    private static PyDict AllowAll() => PyDict.Of("action", "allow");

    private static PyDict Disallow(string osName) => PyDict.Of("action", "disallow", "os", PyDict.Of("name", osName));

    private static PyDict ModernMacos() => Lib(
        "org.lwjgl:lwjgl-glfw:3.3.1:natives-macos",
        "org/lwjgl/lwjgl-glfw/3.3.1/lwjgl-glfw-3.3.1-natives-macos.jar",
        Allow("osx"));

    private static PyDict ModernMacosArm64() => Lib(
        "org.lwjgl:lwjgl-glfw:3.3.1:natives-macos-arm64",
        "org/lwjgl/lwjgl-glfw/3.3.1/lwjgl-glfw-3.3.1-natives-macos-arm64.jar",
        Allow("osx"));

    private static PyDict ModernMainJar() => Lib(
        "org.lwjgl:lwjgl-glfw:3.3.1",
        "org/lwjgl/lwjgl-glfw/3.3.1/lwjgl-glfw-3.3.1.jar",
        AllowAll());

    private static PyDict Lwjgl322NotOsx() => Lib(
        "org.lwjgl:lwjgl:3.2.2",
        "org/lwjgl/lwjgl/3.2.2/lwjgl-3.2.2.jar",
        AllowAll(),
        Disallow("osx"));

    /// <summary>1.7.10 的 org.lwjgl.lwjgl:lwjgl-platform:2.9.1:只有 classifiers,没有 artifact。</summary>
    private static PyDict LegacyNativesOnly()
    {
        PyDict lib = PyDict.Of(
            "name", "org.lwjgl.lwjgl:lwjgl-platform:2.9.1",
            "natives", PyDict.Of("linux", "natives-linux", "osx", "natives-osx", "windows", "natives-windows"));
        lib["downloads"] = PyDict.Of("classifiers", PyDict.Of("natives-osx", PyDict.Of("path", "org/lwjgl/lwjgl/lwjgl-platform/2.9.1/lwjgl-platform-2.9.1-natives-osx.jar")));
        return lib;
    }

    /// <summary>合成夹具(线上 JSON 里没有):没有 artifact.path 但是普通库,走 maven 路径兜底。</summary>
    private static PyDict ArtifactWithoutPath() => PyDict.Of("name", "ca.weblite:java-objc-bridge:1.0.0", "downloads", PyDict.Of("artifact", PyDict.Of("url", "https://example.invalid/a.jar")));

    /// <summary>合成夹具:clientreq=false 的库不参与。</summary>
    private static PyDict ClientReqFalse() => PyDict.Of("name", "com.example:server-only:1.0", "clientreq", false, "downloads", PyDict.Of("artifact", PyDict.Of("path", "com/example/server-only/1.0/server-only-1.0.jar")));

    /// <summary>1.19.4 的 <c>natives-macos</c> 条目:osx(不管 x64 还是 arm64)都进 classpath,Windows 不进。</summary>
    [Theory]
    [InlineData("osx", "x64", true)]
    [InlineData("osx", "arm64", true)]
    [InlineData("windows", "x64", false)]
    [InlineData("linux", "x64", false)]
    public void Modern_macos_jar_is_on_the_classpath_only_on_macos(string os, string arch, bool expected)
    {
        string path = McLauncher._classpath_path(ModernMacos(), LibsDir, os, arch, "10.0.19045");
        Assert.Equal(expected, path != null);
        if (expected)
        {
            Assert.Equal("C:/tipcl/instances/i/libraries/org/lwjgl/lwjgl-glfw/3.3.1/lwjgl-glfw-3.3.1-natives-macos.jar", path.Replace('\\', '/'));
        }
    }

    /// <summary>arm64 分类器的那条也一样:x64 与 arm64 两个 jar 在 mac 上**都**进 classpath(钉住现状)。</summary>
    [Theory]
    [InlineData("osx", "x64", true)]
    [InlineData("osx", "arm64", true)]
    [InlineData("windows", "x64", false)]
    public void Modern_macos_arm64_jar_is_on_the_classpath_only_on_macos(string os, string arch, bool expected)
    {
        string path = McLauncher._classpath_path(ModernMacosArm64(), LibsDir, os, arch, "10.0.19045");
        Assert.Equal(expected, path != null);
        if (expected)
        {
            Assert.EndsWith("lwjgl-glfw-3.3.1-natives-macos-arm64.jar", path.Replace('\\', '/'));
        }
    }

    /// <summary>1.19.4 的主 jar(没有 classifier)在任何平台都进 classpath。</summary>
    [Theory]
    [InlineData("osx", "arm64")]
    [InlineData("windows", "x64")]
    public void The_plain_main_jar_always_joins_the_classpath(string os, string arch)
    {
        Assert.EndsWith("lwjgl-glfw-3.3.1.jar", McLauncher._classpath_path(ModernMainJar(), LibsDir, os, arch, "10.0.19045").Replace('\\', '/'));
    }

    /// <summary>1.16.5 的 LWJGL 3.2.2 条目 disallow osx:osx 下不进 classpath,Windows 下照旧进。</summary>
    [Theory]
    [InlineData("osx", "x64", false)]
    [InlineData("osx", "arm64", false)]
    [InlineData("windows", "x64", true)]
    public void Disallow_osx_entry_is_skipped_on_macos(string os, string arch, bool expected)
    {
        string path = McLauncher._classpath_path(Lwjgl322NotOsx(), LibsDir, os, arch, "10.0.19045");
        Assert.Equal(expected, path != null);
    }

    /// <summary>老式 natives 库(没有 artifact)不进 classpath —— 源码 561-564 行的 continue。</summary>
    [Theory]
    [InlineData("osx")]
    [InlineData("windows")]
    [InlineData("linux")]
    public void Natives_only_libraries_never_join_the_classpath(string os)
    {
        Assert.Null(McLauncher._classpath_path(LegacyNativesOnly(), LibsDir, os, "x64", "10.0.19045"));
    }

    /// <summary>没有 artifact.path 的普通库走 maven 路径兜底(源码 565 行 + <c>Utils.MavenArtifactPath</c>)。</summary>
    [Fact]
    public void Missing_artifact_path_falls_back_to_the_maven_path()
    {
        Assert.Equal(
            "C:/tipcl/instances/i/libraries/ca/weblite/java-objc-bridge/1.0.0/java-objc-bridge-1.0.0.jar",
            McLauncher._classpath_path(ArtifactWithoutPath(), LibsDir, "osx", "arm64", "14.5").Replace('\\', '/'));
    }

    /// <summary>clientreq=false 的库在 mac 上也不进 classpath(源码 543-547 行)。</summary>
    [Fact]
    public void Clientreq_false_libraries_never_join_the_classpath()
    {
        Assert.Null(McLauncher._classpath_path(ClientReqFalse(), LibsDir, "osx", "arm64", "14.5"));
    }

    /// <summary>没有 name 的库直接跳过(源码 548-552 行)。</summary>
    [Fact]
    public void A_library_without_a_name_is_skipped()
    {
        Assert.Null(McLauncher._classpath_path(PyDict.Of("downloads", PyDict.Of("artifact", PyDict.Of("path", "a.jar"))), LibsDir, "osx", "x64", "14.5"));
    }

    /// <summary>Windows 钉子:注入本机三个静态字段(Windows x64)与注入字面量 windows + x64 结果一致,
    /// 且本机 (windows, x64) 下的期望值与 public 路径(源码行为)一致。</summary>
    [Fact]
    public void Windows_pin_matches_the_expected_values_on_this_machine()
    {
        Assert.Equal("windows", Utils.OS_NAME);
        Assert.Equal("x64", Utils.ARCH);
        List<PyDict> fixtures = new List<PyDict> { ModernMacos(), ModernMacosArm64(), ModernMainJar(), Lwjgl322NotOsx(), LegacyNativesOnly(), ArtifactWithoutPath(), ClientReqFalse() };
        foreach (PyDict lib in fixtures)
        {
            Assert.Equal(
                McLauncher._classpath_path(lib, LibsDir, "windows", "x64", Utils.OS_VERSION),
                McLauncher._classpath_path(lib, LibsDir, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION));
        }
        // 本机(Windows)下:mac 专属的 classifier 条目一律不进 classpath,Windows 专属的照旧进。
        Assert.Null(McLauncher._classpath_path(ModernMacos(), LibsDir, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION));
        Assert.Null(McLauncher._classpath_path(ModernMacosArm64(), LibsDir, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION));
        Assert.EndsWith("lwjgl-3.2.2.jar", McLauncher._classpath_path(Lwjgl322NotOsx(), LibsDir, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION).Replace('\\', '/'));
    }

    /// <summary>第 4 项:PPath 的非 Windows 形态用正斜杠、不带盘符 —— 这是 mac 上
    /// <c>${natives_directory}</c> 与 classpath 条目的形态。</summary>
    [Fact]
    public void Mac_style_paths_use_forward_slashes_without_a_drive_letter()
    {
        Assert.Equal(
            "/Users/x/.tipcl/instances/i/libraries/org/lwjgl/lwjgl-glfw/3.3.1/lwjgl-glfw-3.3.1-natives-macos.jar",
            new PPath(windows: false, "/Users/x/.tipcl/instances/i/libraries", "org/lwjgl/lwjgl-glfw/3.3.1/lwjgl-glfw-3.3.1-natives-macos.jar").ToString());
        Assert.Equal(
            "/Users/x/.tipcl/instances/i/versions/v/v-natives",
            new PPath(windows: false, "/Users/x/.tipcl/instances/i/versions", "v", "v-natives").ToString());
        // 本机(Windows)默认形态是反斜杠 —— 两种形态由 PPath.Windows 决定,不是写死的。
        Assert.Equal(
            "\\Users\\x\\.tipcl\\instances\\i\\libraries\\a.jar",
            new PPath(windows: true, "/Users/x/.tipcl/instances/i/libraries", "a.jar").ToString());
    }
}
