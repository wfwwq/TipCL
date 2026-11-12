using System;
using System.Collections.Generic;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2e 第 2 项:<see cref="JavaArch.Acceptable(string, string, string, string)"/> 的完整真值表
/// (os × 进程架构 × 目标架构 × 探测结果 = 3 × 2 × 2 × 3 = 36 格,逐格断言),
/// 以及「候选列表过滤 / 排序」的纯函数与非 Apple 芯片的行为钉子。
///
/// 期望值来源:**任务书第 2 项的裁决**(纯函数语义)与改动前的源码行为 ——
/// 非 Apple 芯片上一切恒为 true / 原样返回,见 PROGRESS.md 的 B2e 设计第 2 项。
/// 全部是纯函数调用,不读盘(**注入的是「读取 release 的委托」**,不是「架构探测委托」:
/// 探测本身也是被测代码)、不起进程、不联网。
/// </summary>
public class B2eJavaAcceptableTests
{
    /// <summary>与启动器落盘规则一致的目录名(末段是 <c>adoptium-&lt;major&gt;-&lt;arch&gt;</c>)。</summary>
    private static string Exe(string arch)
    {
        return Exe(arch, "j");
    }

    /// <summary><paramref name="id"/> 用来在同一批候选里区分两个同架构的 Java(它不影响目录名推断)。</summary>
    private static string Exe(string arch, string id)
    {
        return "/Users/a/.tipcl/java/" + id + "/adoptium-17-" + arch + "/jre.bundle/Contents/Home/bin/java";
    }

    /// <summary>与启动器命名无关的路径 → 探测结果是 null(「不知道」)。</summary>
    private const string UnknownExe = "/Library/Java/JavaVirtualMachines/temurin-17.jdk/Contents/Home/bin/java";

    private const string UnknownExe2 = "/usr/local/opt/openjdk/bin/java";

    /// <summary>假「读取 release 的委托」:只认 <c>&lt;home&gt;/release</c> → 文本,别的路径一律 null。</summary>
    private static Func<string, string> Releases(params string[] pairs)
    {
        Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i + 1 < pairs.Length; i += 2)
        {
            map[pairs[i] + "/release"] = pairs[i + 1];
        }
        return delegate(string path)
        {
            return map.TryGetValue(path, out string text) ? text : null;
        };
    }

    /// <summary>什么都不认、只计数的读取委托:用来断言「这里根本不该探测」。</summary>
    private static Func<string, string> Counting(out int[] calls)
    {
        int[] counter = new int[1];
        calls = counter;
        return delegate(string path)
        {
            counter[0]++;
            return null;
        };
    }

    private static PyDict Java(string exe, long major)
    {
        return PyDict.Of("name", exe, "exe", exe, "major", major, "dir", "/java", "meta", new PyDict());
    }

    // ------------------------------------------------------------------ 真值表

    /// <summary>Windows / Linux 上:不管进程架构、目标架构、探测结果,恒为可用(改动前后一致)。</summary>
    [Theory]
    [InlineData("windows", "x64", "x64", "x64")]
    [InlineData("windows", "x64", "x64", "arm64")]
    [InlineData("windows", "x64", "x64", null)]
    [InlineData("windows", "x64", "arm64", "x64")]
    [InlineData("windows", "x64", "arm64", "arm64")]
    [InlineData("windows", "x64", "arm64", null)]
    [InlineData("windows", "arm64", "x64", "x64")]
    [InlineData("windows", "arm64", "x64", "arm64")]
    [InlineData("windows", "arm64", "x64", null)]
    [InlineData("windows", "arm64", "arm64", "x64")]
    [InlineData("windows", "arm64", "arm64", "arm64")]
    [InlineData("windows", "arm64", "arm64", null)]
    [InlineData("linux", "x64", "x64", "x64")]
    [InlineData("linux", "x64", "x64", "arm64")]
    [InlineData("linux", "x64", "x64", null)]
    [InlineData("linux", "x64", "arm64", "x64")]
    [InlineData("linux", "x64", "arm64", "arm64")]
    [InlineData("linux", "x64", "arm64", null)]
    [InlineData("linux", "arm64", "x64", "x64")]
    [InlineData("linux", "arm64", "x64", "arm64")]
    [InlineData("linux", "arm64", "x64", null)]
    [InlineData("linux", "arm64", "arm64", "x64")]
    [InlineData("linux", "arm64", "arm64", "arm64")]
    [InlineData("linux", "arm64", "arm64", null)]
    public void Non_apple_platforms_are_always_acceptable(string os, string processArch, string targetArch, string? detected)
    {
        Assert.True(JavaArch.Acceptable(os, processArch, targetArch, detected));
    }

    /// <summary>Intel Mac(osx + 进程 x64):目标架构永远等于进程架构,恒为可用。</summary>
    [Theory]
    [InlineData("x64", "x64", "x64")]
    [InlineData("x64", "x64", "arm64")]
    [InlineData("x64", "x64", null)]
    [InlineData("x64", "arm64", "x64")]
    [InlineData("x64", "arm64", "arm64")]
    [InlineData("x64", "arm64", null)]
    public void Intel_mac_is_always_acceptable(string processArch, string targetArch, string? detected)
    {
        Assert.True(JavaArch.Acceptable("osx", processArch, targetArch, detected));
    }

    /// <summary>Apple 芯片 + 目标架构等于进程架构(arm64):恒为可用(保持现状,x64 的 Java 也能跑,只是慢)。</summary>
    [Theory]
    [InlineData("x64")]
    [InlineData("arm64")]
    [InlineData("x86")]
    [InlineData(null)]
    public void Apple_native_target_is_always_acceptable(string? detected)
    {
        Assert.True(JavaArch.Acceptable("osx", "arm64", "arm64", detected));
    }

    /// <summary>
    /// Apple 芯片 + 跨架构(目标 x64):**只有探测为 x64 才可用**;
    /// 探测不出来(null)按不可用处理(宁可自动下载一个,也不拿 arm64 的 Java 去加载 x86_64 原生库)。
    /// </summary>
    [Theory]
    [InlineData("x64", true)]
    [InlineData("arm64", false)]
    [InlineData("x86", false)]
    [InlineData(null, false)]
    public void Apple_cross_arch_requires_detected_x64(string? detected, bool expected)
    {
        Assert.Equal(expected, JavaArch.Acceptable("osx", "arm64", "x64", detected));
    }

    /// <summary>「是不是 Apple 芯片」与「是不是跨架构」两个判定的边界。</summary>
    [Theory]
    [InlineData("osx", "arm64", true)]
    [InlineData("osx", "x64", false)]
    [InlineData("osx", "x86", false)]
    [InlineData("windows", "arm64", false)]
    [InlineData("linux", "arm64", false)]
    [InlineData(null, "arm64", false)]
    public void Is_apple_silicon(string? os, string processArch, bool expected)
    {
        Assert.Equal(expected, JavaArch.IsAppleSilicon(os, processArch));
    }

    [Theory]
    [InlineData("osx", "arm64", "x64", true)]
    [InlineData("osx", "arm64", "arm64", false)]
    [InlineData("osx", "arm64", "x86", true)]
    [InlineData("osx", "arm64", null, true)]
    [InlineData("osx", "x64", "x64", false)]
    [InlineData("windows", "arm64", "x64", false)]
    [InlineData("linux", "arm64", "x64", false)]
    public void Is_cross_arch(string os, string processArch, string? targetArch, bool expected)
    {
        Assert.Equal(expected, JavaArch.IsCrossArch(os, processArch, targetArch));
    }

    // ------------------------------------------------------------------ 可注入读取委托的入口

    /// <summary>非 Apple 芯片上 <see cref="JavaArch.AcceptableForExecutable"/> 直接 true,**读取委托一次都不调**。</summary>
    [Theory]
    [InlineData("windows", "x64", "x64")]
    [InlineData("windows", "arm64", "x64")]
    [InlineData("linux", "arm64", "x64")]
    [InlineData("osx", "x64", "x64")]
    [InlineData("osx", "x64", "arm64")]
    public void Acceptable_for_executable_does_not_probe_on_non_apple(string os, string processArch, string targetArch)
    {
        Assert.True(JavaArch.AcceptableForExecutable(os, processArch, targetArch, Exe("arm64"), Counting(out int[] calls)));
        Assert.Equal(0, calls[0]);
    }

    /// <summary>Apple 芯片上才真的探测:每个 Java 读一次 release,并按探测结果判定。</summary>
    [Fact]
    public void Acceptable_for_executable_probes_on_apple()
    {
        // x64 的 Java + 目标 x64 → 可用(读了一次 release,读不到 → 按目录名推断)
        Assert.True(JavaArch.AcceptableForExecutable("osx", "arm64", "x64", Exe("x64"), Counting(out int[] callsA)));
        Assert.Equal(1, callsA[0]);
        // arm64 的 Java + 目标 x64 → 不可用
        Assert.False(JavaArch.AcceptableForExecutable("osx", "arm64", "x64", Exe("arm64"), Counting(out int[] callsB)));
        Assert.Equal(1, callsB[0]);
        // 探测不出来 → 也不可用
        Assert.False(JavaArch.AcceptableForExecutable("osx", "arm64", "x64", UnknownExe, Counting(out int[] callsC)));
        Assert.Equal(1, callsC[0]);
        // 目标 arm64(同架构)→ 一律可用
        Assert.True(JavaArch.AcceptableForExecutable("osx", "arm64", "arm64", Exe("arm64"), Counting(out int[] callsD)));
        Assert.Equal(1, callsD[0]);
    }

    /// <summary>能读到 release 时以 release 为准(目录名在 home 里,但 release 说 aarch64)。</summary>
    [Fact]
    public void Acceptable_for_executable_prefers_release()
    {
        string home = "/Users/a/.tipcl/java/j/adoptium-17-arm64/jre.bundle/Contents/Home";
        string exe = home + "/bin/java";
        Assert.False(JavaArch.AcceptableForExecutable("osx", "arm64", "x64", exe, Releases(home, "OS_ARCH=\"aarch64\"\n")));
        Assert.True(JavaArch.AcceptableForExecutable("osx", "arm64", "x64", exe, Releases(home, "OS_ARCH=\"x86_64\"\n")));
    }

    /// <summary>目标架构为 null 时按「不等于进程架构」处理 → 跨架构 → 要求 x64(保守)。</summary>
    [Fact]
    public void Null_target_arch_is_treated_as_cross()
    {
        Assert.True(JavaArch.AcceptableForExecutable("osx", "arm64", null, Exe("x64"), Counting(out int[] callsA)));
        Assert.Equal(1, callsA[0]);
        Assert.False(JavaArch.AcceptableForExecutable("osx", "arm64", null, Exe("arm64"), Counting(out int[] callsB)));
        Assert.Equal(1, callsB[0]);
    }

    // ------------------------------------------------------------------ 列表过滤 / 排序

    /// <summary>非 Apple 芯片:原样返回**同一个列表实例**,顺序、内容都不变,而且一次都不探测。</summary>
    [Theory]
    [InlineData("windows", "x64", "x64")]
    [InlineData("windows", "arm64", "x64")]
    [InlineData("linux", "arm64", "x64")]
    [InlineData("osx", "x64", "x64")]
    [InlineData("osx", "x64", "arm64")]
    public void Filter_returns_the_same_list_on_non_apple(string os, string processArch, string targetArch)
    {
        List<object> input = new List<object> { Java(Exe("x64"), 8L), Java(Exe("arm64"), 17L), Java(UnknownExe, 21L) };
        List<object> output = JavaArch.FilterForTarget(input, os, processArch, targetArch, Counting(out int[] calls));
        Assert.Same(input, output);
        Assert.Equal(3, output.Count);
        Assert.Same(input[0], output[0]);
        Assert.Same(input[1], output[1]);
        Assert.Same(input[2], output[2]);
        Assert.Equal(0, calls[0]);
    }

    /// <summary>Apple 芯片 + 跨架构:只留探测为 x64 的,探测不出来的(含系统 Java)一律去掉。</summary>
    [Fact]
    public void Cross_arch_keeps_only_x64()
    {
        List<object> input = new List<object>
        {
            Java(Exe("arm64"), 17L),
            Java(Exe("x64"), 17L),
            Java(UnknownExe, 17L),
            Java(Exe("x86"), 17L)
        };
        List<object> output = JavaArch.FilterForTarget(input, "osx", "arm64", "x64", Counting(out int[] calls));
        Assert.Single(output);
        Assert.Same(input[1], output[0]);
        // 每个候选探一次(4 个候选 → 4 次),不多不少。
        Assert.Equal(4, calls[0]);
    }

    /// <summary>Apple 芯片 + 跨架构且一个都没有:返回空列表(调用方接着走自动下载)。</summary>
    [Fact]
    public void Cross_arch_with_no_x64_gives_empty()
    {
        List<object> input = new List<object> { Java(Exe("arm64"), 17L), Java(UnknownExe, 17L) };
        List<object> output = JavaArch.FilterForTarget(input, "osx", "arm64", "x64", Counting(out int[] calls));
        Assert.Empty(output);
        // 原列表没有被改。
        Assert.Equal(2, input.Count);
    }

    /// <summary>
    /// Apple 芯片 + 同架构:探测为 arm64 或 null 的排在探测为 x64 的前面,且是**稳定排序**
    /// (同类之间的相对顺序不变)。
    /// </summary>
    [Fact]
    public void Same_arch_sorts_native_and_unknown_first_stably()
    {
        List<object> input = new List<object>
        {
            Java(Exe("x64", "a"), 17L),      // x64 → 后面
            Java(Exe("arm64", "b"), 17L),    // arm64 → 前面
            Java(UnknownExe, 17L),           // 未知 → 前面
            Java(Exe("x64", "c"), 17L),      // x64 → 后面
            Java(Exe("arm64", "d"), 17L),    // arm64 → 前面
            Java(UnknownExe2, 17L)           // 未知 → 前面
        };
        List<object> output = JavaArch.FilterForTarget(input, "osx", "arm64", "arm64", Counting(out int[] calls));
        Assert.Equal(6, output.Count);
        Assert.Same(input[1], output[0]);
        Assert.Same(input[2], output[1]);
        Assert.Same(input[4], output[2]);
        Assert.Same(input[5], output[3]);
        Assert.Same(input[0], output[4]);
        Assert.Same(input[3], output[5]);
        Assert.Equal(6, calls[0]);
    }

    /// <summary>同架构排序不会丢掉任何候选(只是换了顺序)。</summary>
    [Fact]
    public void Same_arch_sort_keeps_every_candidate()
    {
        List<object> input = new List<object> { Java(Exe("x64"), 17L), Java(Exe("arm64"), 17L) };
        List<object> output = JavaArch.FilterForTarget(input, "osx", "arm64", "arm64", Counting(out int[] calls));
        Assert.Equal(2, output.Count);
        Assert.Same(input[1], output[0]);
        Assert.Same(input[0], output[1]);
    }

    /// <summary>列表项不是 PyDict 或没有 exe 时当「不知道」:跨架构被去掉,同架构排前面,不抛异常。</summary>
    [Fact]
    public void Non_dict_items_are_treated_as_unknown()
    {
        List<object> input = new List<object> { "not-a-dict", PyDict.Of("name", "no-exe"), Java(Exe("x64"), 17L) };
        List<object> cross = JavaArch.FilterForTarget(input, "osx", "arm64", "x64", Counting(out int[] callsA));
        Assert.Single(cross);
        Assert.Same(input[2], cross[0]);

        List<object> native = JavaArch.FilterForTarget(input, "osx", "arm64", "arm64", Counting(out int[] callsB));
        Assert.Equal(3, native.Count);
        Assert.Same(input[2], native[2]);
    }

    /// <summary>排序键:探测为 x64 记 1,arm64 / null / 其它记 0。</summary>
    [Theory]
    [InlineData("x64", 1)]
    [InlineData("arm64", 0)]
    [InlineData("x86", 0)]
    [InlineData(null, 0)]
    public void Rank_puts_native_and_unknown_first(string? detected, int expected)
    {
        Assert.Equal(expected, JavaArch.Rank(detected));
    }

    /// <summary>空列表在三种平台组合下都不炸。</summary>
    [Fact]
    public void Empty_list_is_fine()
    {
        List<object> empty = new List<object>();
        Assert.Empty(JavaArch.FilterForTarget(empty, "osx", "arm64", "x64", Counting(out int[] callsA)));
        Assert.Empty(JavaArch.FilterForTarget(empty, "osx", "arm64", "arm64", Counting(out int[] callsB)));
        Assert.Empty(JavaArch.FilterForTarget(empty, "windows", "x64", "x64", Counting(out int[] callsC)));
    }
}
