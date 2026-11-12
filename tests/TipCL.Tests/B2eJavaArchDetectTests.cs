using System;
using System.Collections.Generic;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2e 第 1 项:<see cref="JavaArch.Detect(string, Func{string, string})"/> 的架构探测。
/// 全部用**假的读取委托**,不读磁盘、不起进程、不联网,也从不调用 <c>Java.GetJavaMajor</c> 之类
/// 会起 <c>java -version</c> 的入口。
///
/// 期望值来自两种东西,注释里逐条标明:
///   · 源码行为 —— 本包自己写的解析规则(<c>JavaArch._ParseOsArch</c> / <c>_InferFromDirectoryNames</c> /
///     <c>_JavaHomeOf</c>,任务书第 1 项);
///   · 线上取证 —— mac 上 java 的相对路径是 <c>jre.bundle/Contents/Home/bin/java</c>
///     (PROGRESS.md 的 B2a 第 1.2 节;B2e 还核对了该清单里确有
///     <c>jre.bundle/Contents/Home/release</c> 这个条目,见 B2e 第 1 项)。
/// <c>release</c> 里 <c>OS_ARCH</c> 的**真实取值**本机没有 mac 的 JDK 可看 → 未验证。
/// </summary>
public class B2eJavaArchDetectTests
{
    private const string AdoptiumArmHome = "/Users/a/.tipcl/java/adoptium-17-arm64/jre.bundle/Contents/Home";
    private const string AdoptiumArmJava = AdoptiumArmHome + "/bin/java";
    private const string ZuluX64Java = "/Users/a/.tipcl/java/zulu-8-x64/jre.bundle/Contents/Home/bin/java";
    private const string GammaArmJava = "/Users/a/.tipcl/java/java-runtime-gamma-mac-os-arm64/jre.bundle/Contents/Home/bin/java";
    private const string GammaX64Java = "/Users/a/.tipcl/java/java-runtime-gamma-mac-os/jre.bundle/Contents/Home/bin/java";

    /// <summary>只认 「路径 -&gt; 文本」 表里的项,别的一律返回 null(不碰磁盘)。</summary>
    private static Func<string, string> Reader(params string[] pairs)
    {
        Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i + 1 < pairs.Length; i += 2)
        {
            map[pairs[i]] = pairs[i + 1];
        }
        return delegate(string path)
        {
            return map.TryGetValue(path, out string text) ? text : null;
        };
    }

    private static Func<string, string> ReaderThatOnlyServes(string path, string text)
    {
        return delegate(string p)
        {
            return (p == path) ? text : null;
        };
    }

    /// <summary>把读到的请求路径记下来,便于断言 JAVA_HOME 的推导结果。</summary>
    private static Func<string, string> RecordingReader(List<string> seen, string text)
    {
        return delegate(string path)
        {
            seen.Add(path);
            return text;
        };
    }

    // ------------------------------------------------------------------ release 里的 OS_ARCH

    /// <summary>
    /// <c>release</c> 里的 <c>OS_ARCH</c> 是权威来源。覆盖:双引号 / 单引号 / 无引号、键值两侧空白、
    /// 多行(JAVA_VERSION / OS_NAME / IMPLEMENTOR 混在一起)、CRLF、值大小写。
    /// 期望值 = 任务书第 1 项给的映射表(源码行为)。
    /// </summary>
    [Theory]
    [InlineData("OS_ARCH=\"aarch64\"\n", "arm64")]
    [InlineData("OS_ARCH=\"arm64\"\n", "arm64")]
    [InlineData("OS_ARCH=\"x86_64\"\n", "x64")]
    [InlineData("OS_ARCH=\"amd64\"\n", "x64")]
    [InlineData("OS_ARCH=\"x64\"\n", "x64")]
    [InlineData("OS_ARCH='arm64'\n", "arm64")]
    [InlineData("OS_ARCH='x86_64'\n", "x64")]
    [InlineData("OS_ARCH=i386\n", "x86")]
    [InlineData("OS_ARCH=\"i486\"\n", "x86")]
    [InlineData("OS_ARCH=\"i586\"\n", "x86")]
    [InlineData("OS_ARCH=\"i686\"\n", "x86")]
    [InlineData("OS_ARCH=\"x86\"\n", "x86")]
    [InlineData("   OS_ARCH   =   \"aarch64\"   \n", "arm64")]
    [InlineData("OS_ARCH = x86_64\n", "x64")]
    [InlineData("IMPLEMENTOR=\"Eclipse Adoptium\"\nOS_NAME=\"Darwin\"\nOS_ARCH=\"aarch64\"\nJAVA_VERSION=\"17.0.21\"\n", "arm64")]
    [InlineData("JAVA_VERSION=\"17.0.21\"\r\nOS_NAME=\"Darwin\"\r\nOS_ARCH=\"x86_64\"\r\n", "x64")]
    [InlineData("OS_ARCH=\"AARCH64\"\n", "arm64")]
    [InlineData("OS_ARCH=\"X86_64\"\n", "x64")]
    public void Release_os_arch_is_authoritative(string release, string expected)
    {
        string arch = JavaArch.Detect(AdoptiumArmJava, Reader(AdoptiumArmHome + "/release", release));
        Assert.Equal(expected, arch);
    }

    /// <summary>OS_ARCH 的值不在映射表里(未知平台)→ 不猜,继续走目录名推断(源码行为)。</summary>
    [Theory]
    [InlineData("OS_ARCH=\"sparc\"\n")]
    [InlineData("OS_ARCH=\"riscv64\"\n")]
    [InlineData("OS_ARCH=\"\"\n")]
    [InlineData("OS_ARCH=\n")]
    [InlineData("OS_ARCH=\"\"\"\n")]
    [InlineData("JAVA_VERSION=\"17.0.21\"\nOS_NAME=\"Darwin\"\n")]
    [InlineData("OSARCH=\"aarch64\"\n")]
    [InlineData("os_arch=\"aarch64\"\n")]
    [InlineData("# OS_ARCH=\"aarch64\"\n")]
    [InlineData("")]
    public void Unknown_or_missing_release_value_falls_back_to_directory_name(string release)
    {
        // 目录名 adoptium-17-arm64 说是 arm64,所以回落到目录名时结果仍是 arm64。
        string arch = JavaArch.Detect(AdoptiumArmJava, Reader(AdoptiumArmHome + "/release", release));
        Assert.Equal("arm64", arch);
    }

    /// <summary>release 与目录名冲突时**以 release 为准**(任务书裁决)。</summary>
    [Fact]
    public void Release_wins_over_directory_name()
    {
        // 目录名 adoptium-8-x64 说 x64,release 说 aarch64 → arm64。
        string java = "/Users/a/.tipcl/java/adoptium-8-x64/jre.bundle/Contents/Home/bin/java";
        string home = "/Users/a/.tipcl/java/adoptium-8-x64/jre.bundle/Contents/Home";
        Assert.Equal("arm64", JavaArch.Detect(java, Reader(home + "/release", "OS_ARCH=\"aarch64\"\n")));
        // 反向:目录名说 arm64,release 说 x86_64 → x64。
        string home2 = "/Users/a/.tipcl/java/adoptium-17-arm64/jre.bundle/Contents/Home";
        Assert.Equal("x64", JavaArch.Detect(AdoptiumArmJava, Reader(home2 + "/release", "OS_ARCH=\"x86_64\"\n")));
        Assert.Equal("x86", JavaArch.Detect(AdoptiumArmJava, Reader(home2 + "/release", "OS_ARCH=\"i686\"\n")));
    }

    /// <summary>读不到 release(文件不存在 / 读取委托返回 null)时回落到目录名。</summary>
    [Fact]
    public void Unreadable_release_falls_back_to_directory_name()
    {
        // 读取委托只认另一个路径 → 等于 release 文件不存在。
        Assert.Equal("arm64", JavaArch.Detect(AdoptiumArmJava, ReaderThatOnlyServes("/nope/release", "OS_ARCH=\"x86_64\"\n")));
        Assert.Equal("x64", JavaArch.Detect(ZuluX64Java, Reader("/nope/release", "OS_ARCH=\"aarch64\"\n")));
        Assert.Equal("arm64", JavaArch.Detect(GammaArmJava, ReaderThatOnlyServes("/nope/release", null)));
    }

    /// <summary>读取委托抛异常时也要吞掉并回落(源码行为:Detect 里 try/catch)。</summary>
    [Fact]
    public void Reader_that_throws_falls_back_to_directory_name()
    {
        Func<string, string> boom = delegate(string path)
        {
            throw new InvalidOperationException("boom");
        };
        Assert.Equal("arm64", JavaArch.Detect(AdoptiumArmJava, boom));
        Assert.Null(JavaArch.Detect("/usr/local/opt/openjdk/bin/java", boom));
    }

    /// <summary>release 的路径 = 「可执行文件路径里 bin 的上一级」+ <c>/release</c>(源码行为)。</summary>
    [Fact]
    public void Java_home_is_the_directory_above_bin()
    {
        List<string> seen = new List<string>();
        JavaArch.Detect(AdoptiumArmJava, RecordingReader(seen, "OS_ARCH=\"aarch64\"\n"));
        Assert.Equal(new[] { AdoptiumArmHome + "/release" }, seen);

        // 没有 bins 的老式 JDK 包:.../Contents/Home/bin/java,同样是 Contents/Home/release。
        List<string> seen2 = new List<string>();
        JavaArch.Detect("/Library/Java/JavaVirtualMachines/temurin-17.jdk/Contents/Home/bin/java", RecordingReader(seen2, "OS_ARCH=\"x86_64\"\n"));
        Assert.Equal(new[] { "/Library/Java/JavaVirtualMachines/temurin-17.jdk/Contents/Home/release" }, seen2);

        // 末尾多余的分隔符不影响推导(源码行为:先 TrimEnd)。
        List<string> seen3 = new List<string>();
        JavaArch.Detect(AdoptiumArmJava + "/", RecordingReader(seen3, "OS_ARCH=\"aarch64\"\n"));
        Assert.Equal(new[] { AdoptiumArmHome + "/release" }, seen3);
    }

    // ------------------------------------------------------------------ 目录名推断

    /// <summary>
    /// 只认启动器自己装的目录名:三段式 <c>&lt;厂商&gt;-&lt;major&gt;-&lt;arch&gt;</c> 与
    /// <c>…-mac-os[-arm64]</c>(源码行为 / 任务书第 1 项)。命中的是**最靠前**的那一段。
    /// </summary>
    [Theory]
    [InlineData("/Users/a/.tipcl/java/adoptium-8-x64/jre.bundle/Contents/Home/bin/java", "x64")]
    [InlineData("/Users/a/.tipcl/java/adoptium-17-arm64/jre.bundle/Contents/Home/bin/java", "arm64")]
    [InlineData("/Users/a/.tipcl/java/zulu-17-arm64/jre.bundle/Contents/Home/bin/java", "arm64")]
    [InlineData("/Users/a/.tipcl/java/zulu-8-x64/jre.bundle/Contents/Home/bin/java", "x64")]
    [InlineData("/Users/a/.tipcl/java/microsoft-17-x64/jre.bundle/Contents/Home/bin/java", "x64")]
    [InlineData("/Users/a/.tipcl/java/microsoft-17-x86/jre.bundle/Contents/Home/bin/java", "x86")]
    [InlineData("/Users/a/.tipcl/java/java-runtime-beta-mac-os/jre.bundle/Contents/Home/bin/java", "x64")]
    [InlineData("/Users/a/.tipcl/java/jre-legacy-mac-os/jre.bundle/Contents/Home/bin/java", "x64")]
    [InlineData("/Users/a/.tipcl/java/java-runtime-gamma-mac-os-arm64/jre.bundle/Contents/Home/bin/java", "arm64")]
    [InlineData("/Users/a/.tipcl/java/java-runtime-delta-mac-os-arm64/jre.bundle/Contents/Home/bin/java", "arm64")]
    [InlineData("adoptium-17-arm64/jre.bundle/Contents/Home/bin/java", "arm64")]
    [InlineData("C:\\tipcl\\java\\zulu-17-x64\\jre.bundle\\Contents\\Home\\bin\\java.exe", "x64")]
    public void Directory_name_inference(string javaExe, string expected)
    {
        // 读取委托一个都认不出来 → 只能靠目录名。
        Assert.Equal(expected, JavaArch.Detect(javaExe, ReaderThatOnlyServes("/nope/release", "x")));
    }

    /// <summary>与启动器命名无关的目录名不猜:返回 null(「不知道」比猜错安全)。</summary>
    [Theory]
    [InlineData("/usr/bin/java")]
    [InlineData("/usr/local/opt/openjdk/bin/java")]
    [InlineData("/opt/homebrew/opt/openjdk/bin/java")]
    [InlineData("/Library/Java/JavaVirtualMachines/temurin-17.jdk/Contents/Home/bin/java")]
    [InlineData("/Library/Java/JavaVirtualMachines/jdk-17.jdk/Contents/Home/bin/java")]
    [InlineData("/Users/a/.tipcl/java/my-custom-java/bin/java")]
    [InlineData("/Users/a/.tipcl/java/adoptium-17x64/bin/java")]
    [InlineData("/Users/a/.tipcl/java/adoptium-17-x64-extra/bin/java")]
    [InlineData("/Users/a/.tipcl/java/java-runtime-gamma-mac-os-arm64x/bin/java")]
    [InlineData("java")]
    [InlineData("/bin/java")]
    [InlineData("")]
    [InlineData(null)]
    public void Unrelated_paths_give_null(string? javaExe)
    {
        Assert.Null(JavaArch.Detect(javaExe, ReaderThatOnlyServes("/nope/release", "x")));
    }

    /// <summary>两者都认不出来 → null(跨架构时这条会被当成「不可用」,宁可自动下载)。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("JAVA_VERSION=\"17.0.21\"\n")]
    [InlineData("OS_ARCH=\"sparc\"\n")]
    public void Both_unknown_gives_null(string? release)
    {
        Assert.Null(JavaArch.Detect("/Library/Java/JavaVirtualMachines/temurin-17.jdk/Contents/Home/bin/java", ReaderThatOnlyServes("/Library/Java/JavaVirtualMachines/temurin-17.jdk/Contents/Home/release", release)));
    }

    /// <summary>生产入口 <see cref="JavaArch.Detect(string)"/>:文件不存在 → null,不抛异常、不起进程。</summary>
    [Fact]
    public void Production_entry_returns_null_for_missing_file()
    {
        Assert.Null(JavaArch.Detect(TestHome.Root + "/definitely-not-here/bin/java"));
        Assert.Null(JavaArch.Detect(null));
    }

    // ------------------------------------------------------------------ 取值映射表

    /// <summary>
    /// <see cref="JavaArch.MapOsArchValue"/> 的完整映射(源码行为 / 任务书第 1 项):
    /// aarch64|arm64 → arm64;x86_64|amd64|x64 → x64;i386|i486|i586|i686|x86 → x86;其余 → null。
    /// </summary>
    [Theory]
    [InlineData("aarch64", "arm64")]
    [InlineData("arm64", "arm64")]
    [InlineData("ARM64", "arm64")]
    [InlineData("aarch64 ", "arm64")]
    [InlineData(" aarch64", "arm64")]
    [InlineData("x86_64", "x64")]
    [InlineData("amd64", "x64")]
    [InlineData("x64", "x64")]
    [InlineData("x86_64\r", "x64")]
    [InlineData("i386", "x86")]
    [InlineData("i486", "x86")]
    [InlineData("i586", "x86")]
    [InlineData("i686", "x86")]
    [InlineData("x86", "x86")]
    [InlineData("sparc", null)]
    [InlineData("riscv64", null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void Map_os_arch_value(string? value, string? expected)
    {
        Assert.Equal(expected, JavaArch.MapOsArchValue(value));
    }
}
