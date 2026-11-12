using System;
using System.Collections.Generic;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2b 第 1 项:<c>Utils.CheckRules</c> 的可注入重载。
///
/// 期望值来源:
/// <list type="bullet">
/// <item>规则语义逐项对照改动前的源码 <c>Utils.cs</c> 1303-1363 行(Windows 版逐字复制):
/// os.name 与 OS_NAME 比、os.arch 与 ARCH 比、os.version 用 <c>\A(?:p)</c> 或 <c>\A(?:p)\z</c> 匹配,
/// 「最后一条命中的规则生效」,rules 为空/为 null 时返回 true —— 这些属于「源码行为」;</item>
/// <item>Windows 钉子统一用 <c>Utils.OS_NAME</c> / <c>Utils.ARCH</c> / <c>Utils.OS_VERSION</c>
/// (本机实测 <c>windows</c> / <c>x64</c>,见 <c>B2aPlatformKeyTests</c>)与注入同样取值的结果逐项相等。</item>
/// </list>
/// </summary>
public class B2bCheckRulesTests
{
    private static PyDict Rule(string action, string osName = null, string osArch = null, string osVersion = null)
    {
        PyDict rule = PyDict.Of("action", action);
        PyDict os = new PyDict();
        if (osName != null)
        {
            os["name"] = osName;
        }
        if (osArch != null)
        {
            os["arch"] = osArch;
        }
        if (osVersion != null)
        {
            os["version"] = osVersion;
        }
        if (os.Count > 0)
        {
            rule["os"] = os;
        }
        return rule;
    }

    private static List<object> Rules(params PyDict[] rules)
    {
        List<object> list = new List<object>();
        foreach (PyDict r in rules)
        {
            list.Add(r);
        }
        return list;
    }

    /// <summary>os.name 规则按注入的 os 判(1.13.2 起的 arguments.jvm 就是这个形状)。</summary>
    [Theory]
    [InlineData("osx", "x64", true)]
    [InlineData("osx", "arm64", true)]
    [InlineData("windows", "x64", false)]
    [InlineData("linux", "x64", false)]
    public void Os_name_rule_uses_the_injected_os(string os, string arch, bool expected)
    {
        Assert.Equal(expected, Utils.CheckRules(Rules(Rule("allow", osName: "osx")), null, os, arch, "1.0"));
    }

    /// <summary>os.arch 规则按注入的 arch 判(1.13.2 起 <c>-Xss1M</c> 挂在 os.arch=x86 下)。</summary>
    [Theory]
    [InlineData("x86", true)]
    [InlineData("x64", false)]
    [InlineData("arm64", false)]
    public void Os_arch_rule_uses_the_injected_arch(string arch, bool expected)
    {
        Assert.Equal(expected, Utils.CheckRules(Rules(Rule("allow", osArch: "x86")), null, "osx", arch, "1.0"));
    }

    /// <summary>os.version 是**前缀**匹配(<c>\A(?:p)</c>),不是全等 —— 源码 1334-1335 行两种正则取或。
    /// 注意模式本身含 <c>\.</c>:osVersion <c>"10"</c> 不匹配 <c>^10\.</c>(实测)。</summary>
    [Theory]
    [InlineData("^10\\.", "10.0.19045", true)]
    [InlineData("^10\\.", "11.0.0", false)]
    [InlineData("^10\\.", "10", false)]
    [InlineData("10.0.19045", "10.0.19045.1", true)]
    [InlineData("10.0.19045", "10.0.1904", false)]
    public void Os_version_rule_is_a_prefix_match(string pattern, string osVersion, bool expected)
    {
        Assert.Equal(expected, Utils.CheckRules(Rules(Rule("allow", osVersion: pattern)), null, "windows", "x64", osVersion));
    }

    /// <summary>非法正则走 catch(ArgumentException) -> 该条不命中,不抛。</summary>
    [Fact]
    public void Os_version_rule_with_a_broken_regex_is_not_matched_and_does_not_throw()
    {
        Assert.False(Utils.CheckRules(Rules(Rule("allow", osVersion: "^(unclosed")), null, "windows", "x64", "10.0.19045"));
    }

    /// <summary>最后一条命中的规则生效(源码 1357-1360 行:每次命中就整体覆盖 result)。</summary>
    [Fact]
    public void The_last_matching_rule_wins()
    {
        Assert.False(Utils.CheckRules(Rules(Rule("allow", osName: "osx"), Rule("disallow", osName: "osx")), null, "osx", "arm64", "1.0"));
        Assert.True(Utils.CheckRules(Rules(Rule("disallow", osName: "osx"), Rule("allow", osName: "osx")), null, "osx", "arm64", "1.0"));
    }

    /// <summary>不命中的规则不改结果:只有 windows 规则时,osx 下返回 false(而不是「没命中就算 true」)。</summary>
    [Fact]
    public void A_rule_that_does_not_match_leaves_the_result_false()
    {
        Assert.False(Utils.CheckRules(Rules(Rule("disallow", osName: "osx"), Rule("allow", osName: "windows")), null, "osx", "arm64", "1.0"));
        Assert.True(Utils.CheckRules(Rules(Rule("disallow", osName: "osx"), Rule("allow", osName: "windows")), null, "windows", "x64", "10.0"));
    }

    /// <summary>没有 rules / rules 为 null 一律 true(源码 1305-1308 行)。</summary>
    [Fact]
    public void Missing_rules_always_pass()
    {
        Assert.True(Utils.CheckRules(null, null, "osx", "arm64", "1.0"));
        Assert.True(Utils.CheckRules(new List<object>(), null, "osx", "arm64", "1.0"));
        Assert.True(Utils.CheckRules(new PyDict(), null, "osx", "arm64", "1.0"));
    }

    /// <summary>features 规则按传入的 features 判(缺省是空表 -> 一律不命中)。</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Features_rule_uses_the_injected_features(bool featureValue, bool expected)
    {
        PyDict rule = Rule("allow", osName: "osx");
        rule["features"] = PyDict.Of("is_demo_user", true);
        Assert.Equal(expected, Utils.CheckRules(Rules(rule), PyDict.Of("is_demo_user", featureValue), "osx", "arm64", "1.0"));
        Assert.False(Utils.CheckRules(Rules(rule), null, "osx", "arm64", "1.0"));
    }

    /// <summary>os.name 不命中时 features 规则整体跳过(源码 1347 行的 <c>&amp;&amp; flag</c>)。</summary>
    [Fact]
    public void Features_are_ignored_when_the_os_rule_already_failed()
    {
        PyDict rule = Rule("allow", osName: "osx");
        rule["features"] = PyDict.Of("is_demo_user", true);
        Assert.False(Utils.CheckRules(Rules(rule), PyDict.Of("is_demo_user", true), "windows", "x64", "10.0"));
    }

    /// <summary>Windows 钉子:注入本机三个静态字段取到的值与 public 方法逐项相等。</summary>
    [Theory]
    [InlineData("allow")]
    [InlineData("disallow")]
    public void Injected_osx_windows_rule_matches_public_method_on_this_machine(string action)
    {
        List<object> rules = Rules(Rule(action, osName: "osx"), Rule(action, osName: "windows"), Rule("allow", osArch: "x64"));
        Assert.Equal(
            Utils.CheckRules(rules),
            Utils.CheckRules(rules, null, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION));
    }

    /// <summary>Windows 钉子:全套夹具(含 features 与 os.version)逐条与 public 方法相等。</summary>
    [Fact]
    public void Every_fixture_matches_the_public_method_on_this_machine()
    {
        List<List<object>> fixtures = new List<List<object>>
        {
            Rules(Rule("allow", osName: "windows")),
            Rules(Rule("allow", osName: "windows", osVersion: "^10\\.")),
            Rules(Rule("allow", osArch: "x64")),
            Rules(Rule("allow", osArch: "x86")),
            Rules(Rule("disallow", osName: "osx")),
            Rules(Rule("allow", osName: "windows"), Rule("disallow", osName: "windows")),
            Rules(Rule("allow")),
        };
        foreach (List<object> rules in fixtures)
        {
            Assert.Equal(
                Utils.CheckRules(rules),
                Utils.CheckRules(rules, null, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION));
        }
    }

    /// <summary>本机就是 Windows x64,所以「注入 windows + x64」必须与「不注入」一致(含 os.version 前缀规则)。</summary>
    [Fact]
    public void This_machine_is_windows_x64_so_the_windows_path_is_pinned()
    {
        Assert.Equal("windows", Utils.OS_NAME);
        Assert.Equal("x64", Utils.ARCH);
        List<object> windowsOnly = Rules(Rule("allow", osName: "windows", osVersion: "^10\\."));
        Assert.True(Utils.CheckRules(windowsOnly, null, "windows", "x64", Utils.OS_VERSION));
        Assert.True(Utils.CheckRules(windowsOnly));
        Assert.False(Utils.CheckRules(windowsOnly, null, "osx", "x64", Utils.OS_VERSION));
    }
}
