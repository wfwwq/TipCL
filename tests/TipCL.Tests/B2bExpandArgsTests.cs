using System;
using System.Collections.Generic;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2b 第 1 项:<c>McLauncher._expand_args</c> 的可注入重载(给定 arguments.jvm 列表 + os / arch -> 参数列表)。
///
/// 夹具来源:线上版本 JSON,查询时间 <b>2026-11-12 19:27:43 +08:00</b>,地址见每个夹具的注释;
/// 响应体留在 <c>artifacts/b2b/*.json</c>(不入库)。原文里的条目按顺序抄成 PyDict / 字符串,数值一字未改。
/// 期望值来源:源码 <c>McLauncher.cs</c> 63-94 行(原私有方法)与 <c>Utils.CheckRules</c>(规则匹配)的行为。
/// </summary>
public class B2bExpandArgsTests
{
    private static PyDict Ent(params object[] rules)
    {
        List<object> list = new List<object>();
        foreach (object r in rules)
        {
            list.Add(r);
        }
        return PyDict.Of("rules", list);
    }

    private static PyDict AllowOs(string name)
    {
        return PyDict.Of("action", "allow", "os", PyDict.Of("name", name));
    }

    private static PyDict AllowOsVersion(string name, string version)
    {
        return PyDict.Of("action", "allow", "os", PyDict.Of("name", name, "version", version));
    }

    private static PyDict AllowArch(string arch)
    {
        return PyDict.Of("action", "allow", "os", PyDict.Of("arch", arch));
    }

    private static PyDict Valued(PyDict entry, params string[] values)
    {
        List<object> list = new List<object>();
        foreach (string v in values)
        {
            list.Add(v);
        }
        entry["value"] = list;
        return entry;
    }

    /// <summary>
    /// <c>1.19.4</c> 的 <c>arguments.jvm</c>(https://piston-meta.mojang.com/v1/packages/509d363de59da2f2350fee270f11ace8a9412948/1.19.4.json,34407 字节)。
    /// </summary>
    private static List<object> Jvm1194()
    {
        return new List<object>
        {
            Valued(Ent(AllowOs("osx")), "-XstartOnFirstThread"),
            Valued(Ent(AllowOs("windows")), "-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump"),
            Valued(Ent(AllowOsVersion("windows", "^10\\.")), "-Dos.name=Windows 10", "-Dos.version=10.0"),
            Valued(Ent(AllowArch("x86")), "-Xss1M"),
            "-Djava.library.path=${natives_directory}",
            "-Dminecraft.launcher.brand=${launcher_name}",
            "-Dminecraft.launcher.version=${launcher_version}",
            "-cp",
            "${classpath}",
        };
    }

    /// <summary>
    /// <c>26.3</c>(latest.release)的 <c>arguments.jvm</c>(https://piston-meta.mojang.com/v1/packages/9f67e10ed013deeaad20ac3c800652765d493408/26.3.json,44992 字节)。
    /// 注意它少了 windows 的 os.version 规则,多了 4 个带 <c>${natives_directory}/</c> 子目录的 -D 参数。
    /// </summary>
    private static List<object> Jvm263()
    {
        return new List<object>
        {
            Valued(Ent(AllowOs("osx")), "-XstartOnFirstThread"),
            Valued(Ent(AllowOs("windows")), "-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump"),
            Valued(Ent(AllowArch("x86")), "-Xss1M"),
            "-XX:StackShadowPages=32",
            "--enable-native-access=ALL-UNNAMED",
            "--add-exports",
            "java.base/jdk.internal.misc=ALL-UNNAMED",
            "-Djava.library.path=${natives_directory}/java",
            "-Djna.tmpdir=${natives_directory}/jna",
            "-Dorg.lwjgl.system.SharedLibraryExtractPath=${natives_directory}/lwjgl",
            "-Dio.netty.native.workdir=${natives_directory}/netty",
            "-Dminecraft.launcher.brand=${launcher_name}",
            "-Dminecraft.launcher.version=${launcher_version}",
            "-cp",
            "${classpath}",
        };
    }

    private static PyDict Placeholders(string nativesDir = "/Users/x/.tipcl/instances/i/versions/v/v-natives")
    {
        return PyDict.Of("natives_directory", nativesDir, "classpath", "/libs/a.jar", "launcher_name", "TipCL", "launcher_version", "1.0.10");
    }

    private static List<string> Expand(List<object> jvm, string os, string arch, string osVersion = "10.0.19045")
    {
        return McLauncher._expand_args(jvm, Placeholders(), PyDict.Of("is_demo_user", false, "has_custom_resolution", false), os, arch, osVersion);
    }

    /// <summary>osx 规则下的 <c>-XstartOnFirstThread</c> 只在 osx 出现;windows 专属参数反之。</summary>
    [Theory]
    [InlineData("osx", "x64", true, false, false)]
    [InlineData("osx", "arm64", true, false, false)]
    [InlineData("windows", "x64", false, true, false)]
    [InlineData("windows", "x86", false, true, true)]
    [InlineData("linux", "x64", false, false, false)]
    public void Osx_rule_params_and_windows_rule_params_are_mutually_exclusive(string os, string arch, bool expectFirstThread, bool expectHeapDump, bool expectXss1M)
    {
        List<string> list = Expand(Jvm1194(), os, arch);
        Assert.Equal(expectFirstThread, list.Contains("-XstartOnFirstThread"));
        Assert.Equal(
            expectHeapDump,
            list.Contains("-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump"));
        Assert.Equal(expectXss1M, list.Contains("-Xss1M"));
    }

    /// <summary>osx 展开时 <c>-XstartOnFirstThread</c> 必须在最前面(JSON 里它就是第一条),顺序不被打乱。</summary>
    [Fact]
    public void Expand_for_osx_keeps_the_json_order()
    {
        List<string> list = Expand(Jvm1194(), "osx", "arm64");
        Assert.Equal(new List<string>
        {
            "-XstartOnFirstThread",
            "-Djava.library.path=/Users/x/.tipcl/instances/i/versions/v/v-natives",
            "-Dminecraft.launcher.brand=TipCL",
            "-Dminecraft.launcher.version=1.0.10",
            "-cp",
            "/libs/a.jar",
        }, list);
    }

    /// <summary>windows 展开时两个 -Dos.* 参数要一起带上(它们是同一条规则下的两个 value)。</summary>
    [Fact]
    public void Expand_for_windows_keeps_both_os_properties_together()
    {
        List<string> list = Expand(Jvm1194(), "windows", "x64", "10.0.19045");
        Assert.Contains("-Dos.name=Windows 10", list);
        Assert.Contains("-Dos.version=10.0", list);
        Assert.DoesNotContain("-XstartOnFirstThread", list);
    }

    /// <summary>os.version 规则:Windows 11 上不再注入 -Dos.name=Windows 10(源码行为)。</summary>
    [Fact]
    public void Windows_11_does_not_get_the_windows_10_properties()
    {
        List<string> list = Expand(Jvm1194(), "windows", "x64", "11.0.22631");
        Assert.DoesNotContain("-Dos.name=Windows 10", list);
        Assert.Contains("-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump", list);
    }

    /// <summary>26.3:osx + arm64 带上 -XstartOnFirstThread,但**不**带 os.arch=x86 的 -Xss1M。</summary>
    [Fact]
    public void Version_26_3_osx_arm64_gets_start_on_first_thread_only()
    {
        List<string> list = Expand(Jvm263(), "osx", "arm64");
        Assert.Contains("-XstartOnFirstThread", list);
        Assert.DoesNotContain("-Xss1M", list);
        Assert.DoesNotContain("-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump", list);
        Assert.Contains("-XX:StackShadowPages=32", list);
        Assert.Contains("--enable-native-access=ALL-UNNAMED", list);
        Assert.Contains("-Djava.library.path=/Users/x/.tipcl/instances/i/versions/v/v-natives/java", list);
        Assert.Contains("-Djna.tmpdir=/Users/x/.tipcl/instances/i/versions/v/v-natives/jna", list);
        Assert.Contains("-Dorg.lwjgl.system.SharedLibraryExtractPath=/Users/x/.tipcl/instances/i/versions/v/v-natives/lwjgl", list);
        Assert.Contains("-Dio.netty.native.workdir=/Users/x/.tipcl/instances/i/versions/v/v-natives/netty", list);
    }

    /// <summary>26.3 的 <c>--add-exports</c> 与它的值是一对,不能被当成孤儿参数丢掉。</summary>
    [Fact]
    public void Version_26_3_keeps_the_add_exports_pair()
    {
        List<string> list = Expand(Jvm263(), "osx", "x64");
        int i = list.IndexOf("--add-exports");
        Assert.True(i >= 0, "--add-exports 应该被保留");
        Assert.Equal("java.base/jdk.internal.misc=ALL-UNNAMED", list[i + 1]);
    }

    /// <summary>26.3 第三条规则是 os.arch=x86,与游戏版本无关的架构参数只在 x86 下出现。</summary>
    [Fact]
    public void Version_26_3_arch_rule_only_fires_for_x86()
    {
        Assert.Contains("-Xss1M", Expand(Jvm263(), "osx", "x86"));
        Assert.DoesNotContain("-Xss1M", Expand(Jvm263(), "osx", "x64"));
        Assert.DoesNotContain("-Xss1M", Expand(Jvm263(), "osx", "arm64"));
    }

    /// <summary>没有 value 的字典条目不会崩,也不会产出参数(源码 82-90 行)。</summary>
    [Fact]
    public void A_dict_entry_without_value_adds_nothing()
    {
        List<object> jvm = new List<object> { Ent(AllowOs("osx")) };
        Assert.Empty(Expand(jvm, "osx", "x64"));
    }

    /// <summary>无法解析的占位符整条丢弃(源码 70-74 / 85-89 行的 HasPlaceholder 判定)。</summary>
    [Fact]
    public void Unresolved_placeholders_drop_the_whole_argument()
    {
        List<object> jvm = new List<object> { "-Dfoo=${nothing_here}" };
        Assert.Empty(Expand(jvm, "osx", "x64"));
    }

    /// <summary>孤儿 JVM 值参数被丢掉:<c>-cp</c> 后面没有值时整条消失。</summary>
    [Fact]
    public void An_orphan_jvm_value_flag_is_dropped()
    {
        Assert.Empty(Expand(new List<object> { "-cp" }, "osx", "x64"));
        Assert.Equal(new List<string> { "-Dfoo=1" }, Expand(new List<object> { "-Dfoo=1" }, "osx", "x64"));
    }

    /// <summary>值参数后面跟了另一个开关(<c>-</c> 开头)时也按孤儿处理。</summary>
    [Fact]
    public void A_jvm_value_flag_followed_by_another_switch_is_dropped()
    {
        Assert.Equal(new List<string> { "-Dfoo=1" }, Expand(new List<object> { "-cp", "-Dfoo=1" }, "osx", "x64"));
    }

    /// <summary>1.7.10 / 1.8.9 / 1.12.2 只有 <c>minecraftArguments</c>,JSON 里**没有** <c>arguments</c> 键:
    /// 走这条路的版本不该被自行加上 <c>-XstartOnFirstThread</c>(本包红线)。
    /// 夹具:这三个版本的 arguments.jvm 不存在,调用方传的是空列表(McLauncher.cs 590 行 `Get("jvm", new List&lt;object&gt;())`)。</summary>
    [Theory]
    [InlineData("1.7.10")]
    [InlineData("1.8.9")]
    [InlineData("1.12.2")]
    public void Legacy_versions_have_no_jvm_argument_list_at_all(string version)
    {
        Assert.NotEmpty(version);
        Assert.Empty(Expand(new List<object>(), "osx", "arm64", "14.5"));
    }

    /// <summary>Windows 钉子(硬编码期望值):注入 <c>windows + x64 + 10.0.19045</c> 得到的列表,
    /// 期望值来自源码 63-94 行 + <c>CheckRules</c> 的规则语义。</summary>
    [Fact]
    public void Windows_injected_expansion_matches_the_expected_list()
    {
        Assert.Equal(new List<string>
        {
            "-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump",
            "-Dos.name=Windows 10",
            "-Dos.version=10.0",
            "-Djava.library.path=/Users/x/.tipcl/instances/i/versions/v/v-natives",
            "-Dminecraft.launcher.brand=TipCL",
            "-Dminecraft.launcher.version=1.0.10",
            "-cp",
            "/libs/a.jar",
        }, McLauncher._expand_args(Jvm1194(), Placeholders(), PyDict.Of("is_demo_user", false, "has_custom_resolution", false), "windows", "x64", "10.0.19045"));
    }

    /// <summary>Windows 钉子(本机等价):注入本机三个静态字段与注入字面量 <c>windows + x64 + 本机 OS_VERSION</c> 结果一致,
    /// 且与「按源码规则(^10\. 前缀)手算的期望值」一致。</summary>
    [Fact]
    public void Windows_pin_matches_the_expected_windows_list_on_this_machine()
    {
        Assert.Equal("windows", Utils.OS_NAME);
        Assert.Equal("x64", Utils.ARCH);
        List<string> injected = McLauncher._expand_args(
            Jvm1194(), Placeholders(), PyDict.Of("is_demo_user", false, "has_custom_resolution", false),
            Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION);
        Assert.Equal(
            McLauncher._expand_args(Jvm1194(), Placeholders(), PyDict.Of("is_demo_user", false, "has_custom_resolution", false), "windows", "x64", Utils.OS_VERSION),
            injected);
        List<string> expected = new List<string>
        {
            "-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump",
        };
        if (Utils.OS_VERSION.StartsWith("10.", StringComparison.Ordinal))
        {
            expected.Add("-Dos.name=Windows 10");
            expected.Add("-Dos.version=10.0");
        }
        expected.Add("-Djava.library.path=/Users/x/.tipcl/instances/i/versions/v/v-natives");
        expected.Add("-Dminecraft.launcher.brand=TipCL");
        expected.Add("-Dminecraft.launcher.version=1.0.10");
        expected.Add("-cp");
        expected.Add("/libs/a.jar");
        Assert.Equal(expected, injected);
        Assert.DoesNotContain("-XstartOnFirstThread", injected);
    }
}
