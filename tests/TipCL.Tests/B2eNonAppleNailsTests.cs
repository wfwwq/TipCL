using System.IO;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2e:「非 Apple 芯片行为未变」的钉子 + 生产入口(<see cref="JavaArch.Detect(string)"/>)的真实文件读取。
///
/// 文件操作只发生在 <see cref="TestHome.Root"/> 之下(红线:测试的临时文件必须落在仓库内,
/// 不写 %TEMP%);不联网、不起进程、不调 <c>Java.GetJavaMajor</c> / <c>ResolveLaunchJava</c> /
/// 任何 <c>Install*</c> 入口(它们会起 <c>java -version</c>、扫盘、读 <c>Config.CONFIG</c> 或联网)。
///
/// 期望值来源:改动前的源码行为(<c>JavaUsableFor</c> 只比较大版本,不看架构;非 Apple 芯片上
/// 本包新增的判断一律恒为 true)+ 任务书第 2 项的裁决。
/// </summary>
public class B2eNonAppleNailsTests
{
    private static string Probe(string leaf)
    {
        string root = TestHome.Root + "/b2e-arch-probe/" + leaf;
        Directory.CreateDirectory(root);
        return root;
    }

    private static string Release(string homeLeaf, string releaseText)
    {
        string home = Probe(homeLeaf);
        File.WriteAllText(home + "/release", releaseText);
        return home + "/bin/java";
    }

    // ------------------------------------------------------------------ 生产入口读真实文件

    /// <summary><c>&lt;JAVA_HOME&gt;/release</c> 真实存在时读它(两条都是 mac 的包形态)。</summary>
    [Theory]
    [InlineData("adoptium-17-arm64/jre.bundle/Contents/Home", "OS_ARCH=\"aarch64\"\n", "arm64")]
    [InlineData("adoptium-17-x64/jre.bundle/Contents/Home", "OS_ARCH=\"x86_64\"\n", "x64")]
    [InlineData("zulu-8-x64/jre.bundle/Contents/Home", "IMPLEMENTOR=\"Azul\"\nOS_ARCH=\"x86_64\"\n", "x64")]
    [InlineData("java-runtime-gamma-mac-os-arm64/jre.bundle/Contents/Home", "OS_ARCH=\"arm64\"\n", "arm64")]
    public void Production_entry_reads_the_real_release_file(string homeLeaf, string releaseText, string expected)
    {
        Assert.Equal(expected, JavaArch.Detect(Release(homeLeaf, releaseText)));
    }

    /// <summary>release 不存在时按真实目录名推断(目录名形态来自启动器的落盘规则)。</summary>
    [Theory]
    [InlineData("adoptium-8-x64/jre.bundle/Contents/Home", "x64")]
    [InlineData("java-runtime-beta-mac-os/jre.bundle/Contents/Home", "x64")]
    [InlineData("java-runtime-gamma-mac-os-arm64/jre.bundle/Contents/Home", "arm64")]
    public void Production_entry_falls_back_to_the_real_directory_name(string homeLeaf, string expected)
    {
        Assert.Equal(expected, JavaArch.Detect(Probe(homeLeaf) + "/bin/java"));
    }

    /// <summary>都不认识 → null(不猜),并且不抛异常。</summary>
    [Fact]
    public void Production_entry_returns_null_when_nothing_matches()
    {
        Assert.Null(JavaArch.Detect(Probe("not-a-launcher-dir/Contents/Home") + "/bin/java"));
    }

    /// <summary>
    /// 只读 <c>&lt;JAVA_HOME&gt;/release</c>:**不检查可执行文件本身是否存在**
    /// (源码行为 —— <c>JavaArch.Detect</c> 里只读那一个文件)。调用方(<c>JavaUsableFor</c>)才查文件。
    /// </summary>
    [Fact]
    public void Production_entry_only_reads_release()
    {
        string home = Probe("only-release/bundle/Contents/Home");
        File.WriteAllText(home + "/release", "OS_ARCH=\"aarch64\"\n");
        Assert.False(File.Exists(home + "/bin/java"));
        Assert.Equal("arm64", JavaArch.Detect(home + "/bin/java"));
    }

    // ------------------------------------------------------------------ JavaUsableFor 里那句判断的等价形式

    /// <summary>
    /// 非 Apple 芯片:<c>JavaUsableFor</c> 在架构这关恒为 true,而且**不看盘** ——
    /// 哪怕磁盘上的 <c>release</c> 明说 aarch64、版本 JSON 是「Apple 芯片上会退 x64」的 1.16.5 形态。
    /// 这里调的就是 <c>JavaUsableFor</c> 里那句(<c>AcceptableForExecutable</c>,目标架构用同一份
    /// 版本 JSON 按同一组平台参数算出来);`JavaUsableFor` 本身不能直接测 —— 它会调
    /// <c>Java.GetJavaMajor</c>,那会起 <c>java -version</c> 进程(红线禁止)。
    /// </summary>
    [Theory]
    [InlineData("windows", "x64")]
    [InlineData("windows", "arm64")]
    [InlineData("linux", "x64")]
    [InlineData("linux", "arm64")]
    [InlineData("osx", "x64")]
    public void Non_apple_versions_always_acceptable(string os, string processArch)
    {
        string java = Release("non-apple-" + os + "-" + processArch + "/jre.bundle/Contents/Home", "OS_ARCH=\"aarch64\"\n");
        PyDict json = B2dFixtures.V1165();
        Assert.True(JavaArch.AcceptableForExecutable(os, processArch, LaunchArch.For(json, os, processArch, "14.5"), java));
    }

    /// <summary>
    /// Apple 芯片 + 跨架构(1.16.5 形态 → 目标 x64):arm64 的 Java 不可用,探测不出来也不可用,
    /// 只有 x64 的可用(任务书第 2 项)。
    /// </summary>
    [Fact]
    public void Apple_cross_arch_version_checks_the_real_files()
    {
        string arm = Release("apple-arm/jre.bundle/Contents/Home", "OS_ARCH=\"aarch64\"\n");
        string x64 = Release("apple-x64/jre.bundle/Contents/Home", "OS_ARCH=\"x86_64\"\n");
        string nowhere = Probe("apple-nowhere/Contents/Home") + "/bin/java";
        PyDict json = B2dFixtures.V1165();
        string target = LaunchArch.For(json, "osx", "arm64", "14.5");

        Assert.Equal("x64", target);
        Assert.False(JavaArch.AcceptableForExecutable("osx", "arm64", target, arm));
        Assert.True(JavaArch.AcceptableForExecutable("osx", "arm64", target, x64));
        Assert.False(JavaArch.AcceptableForExecutable("osx", "arm64", target, nowhere));
    }

    /// <summary>Apple 芯片 + 同架构(1.19.4 形态 → 目标 arm64):三个都可用(保持现状)。</summary>
    [Fact]
    public void Apple_native_arch_version_accepts_everything()
    {
        string arm = Release("apple-native-arm/jre.bundle/Contents/Home", "OS_ARCH=\"aarch64\"\n");
        string x64 = Release("apple-native-x64/jre.bundle/Contents/Home", "OS_ARCH=\"x86_64\"\n");
        string nowhere = Probe("apple-native-nowhere/Contents/Home") + "/bin/java";
        PyDict json = B2dFixtures.V1194();
        string target = LaunchArch.For(json, "osx", "arm64", "14.5");

        Assert.Equal("arm64", target);
        Assert.True(JavaArch.AcceptableForExecutable("osx", "arm64", target, arm));
        Assert.True(JavaArch.AcceptableForExecutable("osx", "arm64", target, x64));
        Assert.True(JavaArch.AcceptableForExecutable("osx", "arm64", target, nowhere));
    }
}
