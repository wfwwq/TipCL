using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2e 第 4 项:Rosetta 提示文案与「追加与否」的纯函数。
///
/// 期望值来源:任务书第 4 项的裁决(文案里必须含 <c>softwareupdate --install-rosetta</c>;
/// 跨架构才追加、不检测 Rosetta、不阻断启动;不许给启动命令加 <c>arch -x86_64</c> 之类的前缀)
/// 与源码行为(<see cref="JavaArch.WithRosettaNote"/>)。纯函数,不读盘、不联网、不起进程。
///
/// 中文文案一律用 <c>\uXXXX</c> 转义写(D-16 / D-24:全角 / 半角标点肉眼分不出来),
/// 转义由一次性脚本从源码行上取出来(见 PROGRESS.md 的 B2e 第 6 节)。
/// </summary>
public class B2eRosettaNoteTests
{
    /// <summary>模拟 <c>McLauncher._coerce_java_exe</c> 的原文案(转义抄自它的真实那一行)。</summary>
    private const string Message = "\u65E0\u6CD5\u542F\u52A8\u6B64\u7248\u672C\uFF08\u9700\u8981 Java 17+\uFF09\u3002";

    /// <summary>文案里必须有那条命令(ASCII,可以直接写)。</summary>
    [Fact]
    public void Note_contains_the_softwareupdate_command()
    {
        Assert.Contains("softwareupdate --install-rosetta", JavaArch.RosettaNote);
    }

    /// <summary>文案说的是「没有 Apple 芯片原生库 / 经 Rosetta 运行」(转义断言,避免抄错标点)。</summary>
    [Fact]
    public void Note_says_what_is_going_on()
    {
        Assert.Contains("\u8BE5\u7248\u672C\u6CA1\u6709 Apple \u82AF\u7247\u539F\u751F\u5E93", JavaArch.RosettaNote);
        Assert.Contains("\u7ECF Rosetta \u8FD0\u884C", JavaArch.RosettaNote);
        Assert.Contains("Intel(x64)", JavaArch.RosettaNote);
    }

    /// <summary>启动命令 / JVM 参数不受影响:文案里不出现 <c>arch -x86_64</c> 这类包装。</summary>
    [Fact]
    public void Note_does_not_wrap_the_command()
    {
        Assert.DoesNotContain("arch -x86_64", JavaArch.RosettaNote);
    }

    /// <summary>跨架构:原文案一字不改地在前面,后面接一个空格再追加提示。</summary>
    [Fact]
    public void Cross_arch_appends_the_note()
    {
        string result = JavaArch.WithRosettaNote(Message, "osx", "arm64", "x64");
        Assert.Equal(Message + " " + JavaArch.RosettaNote, result);
        Assert.StartsWith(Message, result);
        Assert.EndsWith(JavaArch.RosettaNote, result);
        Assert.Contains("softwareupdate --install-rosetta", result);
    }

    /// <summary>非跨架构(Windows / Linux / Intel Mac / Apple 芯片但是 arm64 目标):**原样返回同一句**,不追加。</summary>
    [Theory]
    [InlineData("windows", "x64", "x64")]
    [InlineData("windows", "arm64", "x64")]
    [InlineData("linux", "x64", "x64")]
    [InlineData("linux", "arm64", "x64")]
    [InlineData("osx", "x64", "x64")]
    [InlineData("osx", "arm64", "arm64")]
    public void Non_cross_arch_returns_the_message_unchanged(string os, string processArch, string targetArch)
    {
        string result = JavaArch.WithRosettaNote(Message, os, processArch, targetArch);
        Assert.Equal(Message, result);
        Assert.DoesNotContain("softwareupdate", result);
    }

    /// <summary>目标架构为 null(理论上不会出现)按跨架构处理 → 追加(保守)。</summary>
    [Fact]
    public void Null_target_arch_appends_on_apple()
    {
        Assert.Equal(Message + " " + JavaArch.RosettaNote, JavaArch.WithRosettaNote(Message, "osx", "arm64", null));
    }

    /// <summary>空文案也能拼接,不炸。</summary>
    [Fact]
    public void Empty_message_still_appends()
    {
        Assert.Equal(" " + JavaArch.RosettaNote, JavaArch.WithRosettaNote("", "osx", "arm64", "x64"));
    }
}
