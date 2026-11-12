using System.Collections.Generic;
using System.IO;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2d 的「非 Apple 芯片行为未变」钉子(任务书测试第 3 条)。
/// 两层意思:
/// <list type="number">
/// <item>本机(Windows + 进程 x64):<see cref="LaunchArch.For(PyDict)"/> 恒等于 <c>Utils.ARCH</c>,
/// 所以调用点从「传 Utils.ARCH」换成「传 LaunchArch 算出来的目标架构」时,传进去的是同一个值
/// (对 arch 型的纯函数直接逐项比较;对 (os, 进程架构, osVersion) 型的纯函数则比较
/// 「注入本机三字段」与「注入字面量 windows + x64」)。</item>
/// <item>Intel Mac(osx + 进程 x64):目标架构也是 x64,等于进程架构 → 与 B2b 那批可注入重载的结果逐项相等。</item>
/// </list>
/// 全是纯函数调用(只有一处在 <see cref="TestHome.Dir"/> 下造空文件),不联网、不起进程。
/// </summary>
public class B2dNonAppleNailsTests
{
    private static string LibsDir()
    {
        return Path.Combine(TestHome.Root, "b2d-nails-libs");
    }

    private static List<string> Keys(List<KeyValuePair<string, PyDict>> list)
    {
        return list.Select((KeyValuePair<string, PyDict> p) => p.Key).ToList();
    }

    /// <summary>本机恒等钉子:目标架构就是进程架构(所以 natives / 规则 / classpath 三处拿到的值与改动前一致)。</summary>
    [Theory]
    [InlineData("1.7.10")]
    [InlineData("1.8.9")]
    [InlineData("1.12.2")]
    [InlineData("1.13.2")]
    [InlineData("1.16.5")]
    [InlineData("1.18.2")]
    [InlineData("1.19.4")]
    [InlineData("26.3")]
    public void Target_architecture_equals_the_process_architecture_on_windows(string id)
    {
        Assert.Equal("windows", Utils.OS_NAME);
        Assert.Equal("x64", Utils.ARCH);
        PyDict json = B2dFixtures.ById(id);
        Assert.Equal(Utils.ARCH, LaunchArch.For(json));
        Assert.Equal(Utils.ARCH, LaunchArch.For(json, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION));
        Assert.Equal(Utils.ARCH, LaunchArch.For(json, "windows", "x64", Utils.OS_VERSION));
    }

    /// <summary>Intel Mac 钉子:目标架构等于进程架构 x64(B2b 那批重载的结果因此逐项不变)。</summary>
    [Theory]
    [InlineData("1.7.10")]
    [InlineData("1.8.9")]
    [InlineData("1.12.2")]
    [InlineData("1.13.2")]
    [InlineData("1.16.5")]
    [InlineData("1.18.2")]
    [InlineData("1.19.4")]
    [InlineData("26.3")]
    public void Target_architecture_equals_the_process_architecture_on_intel_mac(string id)
    {
        PyDict json = B2dFixtures.ById(id);
        Assert.Equal("x64", LaunchArch.For(json, "osx", "x64", Utils.OS_VERSION));
        // 分类器:按目标架构选 == 按进程架构选(B2b 的 osx + x64 结果)。
        foreach (PyDict lib in Installer.Libs(json))
        {
            Assert.Equal(
                Installer.SelectNativeClassifier(lib, "osx", "x64"),
                Installer.SelectNativeClassifier(lib, "osx", LaunchArch.For(json, "osx", "x64", Utils.OS_VERSION)));
        }
    }

    /// <summary>arch 型纯函数(arch 直接是入参)的钉子:传 <c>Utils.ARCH</c> 与传 LaunchArch 算出来的值
    /// 得到同一结果 —— 本机(Windows)与 Intel Mac 都成立。</summary>
    [Theory]
    [InlineData("windows", "x64")]
    [InlineData("osx", "x64")]
    public void Arch_taking_helpers_get_the_same_value_as_before(string os, string processArch)
    {
        PyDict json = B2dFixtures.V1165WithJars();
        string target = LaunchArch.For(json, os, processArch, Utils.OS_VERSION);
        Assert.Equal(processArch, target);
        foreach (PyDict lib in Installer.Libs(json))
        {
            PyDict downloads = Py.DictOr(Py.Or(lib.Get("downloads"), new PyDict()));
            object name = lib.Get("name");
            PyDict before = Installer._native_classifier_entry(lib, downloads, os, Utils.ARCH, out string classifierBefore);
            PyDict after = Installer._native_classifier_entry(lib, downloads, os, target, out string classifierAfter);
            Assert.Equal(classifierBefore, classifierAfter);
            Assert.Equal(before == null, after == null);
            if (before != null)
            {
                Assert.Equal(Py.Str(before.Get("path")), Py.Str(after.Get("path")));
            }
            PyDict entryBefore = Preflight._natives_entry(lib, downloads, name, os, Utils.ARCH, out string relBefore);
            PyDict entryAfter = Preflight._natives_entry(lib, downloads, name, os, target, out string relAfter);
            Assert.Equal(relBefore, relAfter);
            Assert.Equal(entryBefore == null, entryAfter == null);
        }
    }

    /// <summary>(os, 进程架构, osVersion) 型纯函数的钉子:注入本机三字段 == 注入字面量 windows + x64,
    /// 也就是「与改动前在本机的返回逐项相等」。</summary>
    [Theory]
    [InlineData("1.7.10")]
    [InlineData("1.16.5")]
    [InlineData("1.19.4")]
    public void Process_arch_helpers_are_unchanged_on_this_machine(string id)
    {
        PyDict json = B2dFixtures.ById(id);
        string libs = LibsDir();
        Assert.Equal(
            Cleaner._lib_relpaths(json, libs, "windows", "x64", Utils.OS_VERSION),
            Cleaner._lib_relpaths(json, libs, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION));
        Assert.Equal(
            McLauncher._needs_natives(json, "windows", "x64", Utils.OS_VERSION),
            McLauncher._needs_natives(json, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION));
        Assert.Equal(
            Preflight._natives_required(json, "windows", "x64", Utils.OS_VERSION),
            Preflight._natives_required(json, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION));
        Assert.Equal(
            OfficialMigrate._library_relpaths(B2dFixtures.ChainMixed(), "windows", "x64", Utils.OS_VERSION),
            OfficialMigrate._library_relpaths(B2dFixtures.ChainMixed(), Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION));
    }

    /// <summary>_natives_to_extract 的钉子:本机三字段与字面量 windows + x64 都要拿到同一份清单 ——
    /// 先在 <see cref="TestHome.Dir"/> 下造出两条 windows natives jar 的路径文件。</summary>
    [Fact]
    public void Extract_plan_is_unchanged_on_this_machine()
    {
        using TestHome testHome = new TestHome();
        string libs = Path.Combine(testHome.Dir, "libraries");
        string rel = Path.Combine("org", "example", "natives-windows.jar");
        Directory.CreateDirectory(Path.Combine(libs, "org", "example"));
        File.WriteAllText(Path.Combine(libs, rel), "");

        PyDict json = B2dFixtures.V1165WithJars();
        List<string> windows = Keys(Installer._natives_to_extract(json, libs, "windows", "x64", Utils.OS_VERSION));
        Assert.Equal(windows, Keys(Installer._natives_to_extract(json, libs, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION)));
        Assert.Equal(2, windows.Count);
        // 1.16.5 形态里 mac 专有的两条在 windows 上不会被选中。
        Assert.DoesNotContain(windows, (string p) => p.Contains("natives-macos", System.StringComparison.Ordinal));
        Assert.DoesNotContain(windows, (string p) => p.Contains("natives-osx", System.StringComparison.Ordinal));
    }

    /// <summary>与 Windows 源同一份 Cast:1.16.5 形态在 windows + x64 下选到的分类器与 Process 架构
    /// 下的公开方法(读 Utils.ARCH)逐项一致。</summary>
    [Fact]
    public void Public_entries_match_the_injected_native_architecture()
    {
        PyDict json = B2dFixtures.V1165WithJars();
        foreach (PyDict lib in Installer.Libs(json))
        {
            Assert.Equal(
                Installer.SelectNativeClassifier(lib),
                Installer.SelectNativeClassifier(lib, Utils.OS_NAME, LaunchArch.For(json)));
            Assert.Equal(
                Installer.NativesJarRelpath(lib, "natives-macos"),
                Installer.NativesJarRelpath(lib, "natives-macos", LaunchArch.For(json)));
        }
    }
}
