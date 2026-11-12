using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2e 第 3 项:安装计划 <see cref="JavaArch.PlanInstall"/> 的逐格断言,以及第 5 项的兜底大版本。
///
/// 期望值来源:**改动前的源码行为**(<c>Java.cs</c> 1342–1369 行的 <c>EnsureJavaForVersion</c>:
/// <c>component</c> 非空且不是 <c>jre-legacy</c> 才走 Mojang;平台键 = 静态 <c>PLATFORM_KEY</c>
/// = <c>PlatformKeyFor(Utils.OS_NAME, Utils.ARCH)</c>;Adoptium 的 arch = <c>_IsLegacy32bit ? "x86" : null</c>)
/// 加上**任务书第 3 项的裁决**(跨架构时 Mojang 用 <c>PlatformKeyFor(os,"x64")</c> = <c>mac-os</c>,
/// Adoptium 传 <c>"x64"</c>)。
/// 纯函数,不读盘、不联网、不起进程、不读 <c>Config.CONFIG</c>。
/// </summary>
public class B2eJavaInstallPlanTests
{
    /// <summary>
    /// 三种版本形态 × 六种平台组合,逐格断言平台键 / Adoptium 的 arch / 是否先试 Mojang。
    /// 后四种(Apple 同架构 / Intel Mac / Windows x64 / Windows x64 legacy32)是**改动前的结果**;
    /// Apple 跨架构那一行是本包改的那一格。
    /// </summary>
    [Theory]
    // 1.16.5 形态:jre-legacy + major 8 —— 不管哪条路都不走 Mojang
    [InlineData("jre-legacy", 8L, "osx", "arm64", "x64", false, false, "mac-os", "x64")]
    [InlineData("jre-legacy", 8L, "osx", "arm64", "arm64", false, false, "mac-os-arm64", null)]
    [InlineData("jre-legacy", 8L, "osx", "x64", "x64", false, false, "mac-os", null)]
    [InlineData("jre-legacy", 8L, "windows", "x64", "x64", false, false, "windows-x64", null)]
    [InlineData("jre-legacy", 8L, "windows", "x64", "x64", true, false, "windows-x64", "x86")]
    [InlineData("jre-legacy", 8L, "linux", "x64", "x64", false, false, "linux", null)]
    // 1.18.2 形态:java-runtime-beta + major 17 —— Apple 芯片上必须退到 mac-os(mac-os-arm64 没有 beta)
    [InlineData("java-runtime-beta", 17L, "osx", "arm64", "x64", false, true, "mac-os", "x64")]
    [InlineData("java-runtime-beta", 17L, "osx", "arm64", "arm64", false, true, "mac-os-arm64", null)]
    [InlineData("java-runtime-beta", 17L, "osx", "x64", "x64", false, true, "mac-os", null)]
    [InlineData("java-runtime-beta", 17L, "windows", "x64", "x64", false, true, "windows-x64", null)]
    [InlineData("java-runtime-beta", 17L, "windows", "x64", "x64", true, true, "windows-x64", "x86")]
    [InlineData("java-runtime-beta", 17L, "linux", "x64", "x64", false, true, "linux", null)]
    // 1.19.4 形态:java-runtime-gamma + major 17 —— arm64 目标,照常原生跑
    [InlineData("java-runtime-gamma", 17L, "osx", "arm64", "x64", false, true, "mac-os", "x64")]
    [InlineData("java-runtime-gamma", 17L, "osx", "arm64", "arm64", false, true, "mac-os-arm64", null)]
    [InlineData("java-runtime-gamma", 17L, "osx", "x64", "x64", false, true, "mac-os", null)]
    [InlineData("java-runtime-gamma", 17L, "windows", "x64", "x64", false, true, "windows-x64", null)]
    [InlineData("java-runtime-gamma", 17L, "windows", "x64", "x64", true, true, "windows-x64", "x86")]
    [InlineData("java-runtime-gamma", 17L, "linux", "x64", "x64", false, true, "linux", null)]
    public void Install_plan_grid(string component, long major, string os, string processArch, string targetArch, bool legacy32, bool tryMojang, string platformKey, string? adoptiumArch)
    {
        JavaArch.InstallPlan plan = JavaArch.PlanInstall(component, major, os, processArch, targetArch, legacy32);
        Assert.Equal(tryMojang, plan.TryMojang);
        Assert.Equal(platformKey, plan.MojangPlatformKey);
        Assert.Equal(adoptiumArch, plan.AdoptiumArch);
        // 上下文(不参与判断)也如实带出来。
        Assert.Equal(targetArch, plan.TargetArch);
        Assert.Equal(major, plan.Major);
    }

    /// <summary>
    /// 非跨架构的一切组合上,平台键必须**逐字等于** <c>PlatformKeyFor(os, 进程架构)</c>
    /// —— 那正是静态 <c>PLATFORM_KEY</c> 的定义(`Java.cs:24` 的 `_PlatformKey()`),也就是改动前的值。
    /// </summary>
    [Theory]
    [InlineData("osx", "arm64", "arm64")]
    [InlineData("osx", "x64", "x64")]
    [InlineData("windows", "x64", "x64")]
    [InlineData("windows", "arm64", "arm64")]
    [InlineData("windows", "x86", "x86")]
    [InlineData("linux", "x64", "x64")]
    [InlineData("linux", "arm64", "arm64")]
    public void Non_cross_platform_key_equals_platform_key_for(string os, string processArch, string targetArch)
    {
        JavaArch.InstallPlan plan = JavaArch.PlanInstall("java-runtime-gamma", 17L, os, processArch, targetArch, false);
        Assert.Equal(Java.PlatformKeyFor(os, processArch), plan.MojangPlatformKey);
    }

    /// <summary>
    /// 非跨架构上 Adoptium 的 arch 就是改动前那一句:`legacy32 ? "x86" : null`
    /// (<c>Java.cs</c> 改动前的 `string arch = (_IsLegacy32bit(version_json) ? "x86" : null);`)。
    /// 注意 <paramref name="legacy32"/> 这个入参在 mac / linux 上**恒为 false** —— 它由
    /// <c>_IsLegacy32bit</c> 算出来,那个函数要求 <c>windows</c> + 进程 <c>x64</c>(`Java.cs:1333`);
    /// 所以「osx / linux + true」不是可达状态,本用例只钉「这个布尔怎么映射到 arch」。
    /// </summary>
    [Theory]
    [InlineData("windows", "x64", "x64", false, null)]
    [InlineData("windows", "x64", "x64", true, "x86")]
    [InlineData("windows", "arm64", "arm64", false, null)]
    [InlineData("osx", "arm64", "arm64", false, null)]
    [InlineData("osx", "x64", "x64", false, null)]
    [InlineData("linux", "x64", "x64", false, null)]
    public void Non_cross_adoptium_arch_is_the_old_value(string os, string processArch, string targetArch, bool legacy32, string? expected)
    {
        Assert.Equal(expected, JavaArch.PlanInstall("java-runtime-gamma", 17L, os, processArch, targetArch, legacy32).AdoptiumArch);
    }

    /// <summary>跨架构上 Adoptium 的 arch 恒为 <c>x64</c>(不管 legacy32 是不是被置上,虽然 mac 上它恒为 false)。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cross_arch_adoptium_arch_is_x64(bool legacy32)
    {
        Assert.Equal("x64", JavaArch.PlanInstall("java-runtime-gamma", 17L, "osx", "arm64", "x64", legacy32).AdoptiumArch);
    }

    /// <summary>空 component(null / "")与 <c>jre-legacy</c> 都不走 Mojang(改动前的 <c>Py.Truthy(obj)</c> 语义)。</summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("jre-legacy", false)]
    [InlineData("java-runtime-gamma", true)]
    [InlineData("java-runtime-beta", true)]
    [InlineData("java-runtime-alpha", true)]
    public void Only_non_legacy_components_try_mojang(string? component, bool expected)
    {
        Assert.Equal(expected, JavaArch.PlanInstall(component, 17L, "windows", "x64", "x64", false).TryMojang);
}

    // ------------------------------------------------------------------ 端到端串联(B2d 夹具 → 目标架构 → 安装计划)

    /// <summary>
    /// <see cref="B2dFixtures"/> 的版本夹具 → <c>LaunchArch.For(json, "osx", "arm64", ...)</c>
    /// → <see cref="JavaArch.PlanInstall"/>:
    /// 1.16.5 退 x64 且是 jre-legacy → Adoptium 的 <c>x64</c> 包(Adoptium 对 mac x64 有 Java 8,线上证据);
    /// 1.18.2 退 x64 → Mojang 平台键 <c>mac-os</c>(它有 java-runtime-beta);
    /// 1.19.4 目标 arm64 → Mojang 平台键 <c>mac-os-arm64</c>(它有 java-runtime-gamma)。
    /// 目标架构与平台键都来自线上取证(PROGRESS.md 的 B2a 第 1.1/1.3 节 + B2c/B2d 的裁决)。
    /// </summary>
    [Theory]
    [InlineData("1.16.5", "jre-legacy", 8L, "x64", false, "mac-os", "x64")]
    [InlineData("1.18.2", "java-runtime-beta", 17L, "x64", true, "mac-os", "x64")]
    [InlineData("1.19.4", "java-runtime-gamma", 17L, "arm64", true, "mac-os-arm64", null)]
    public void Fixture_to_target_arch_to_install_plan(string id, string component, long major, string expectedTarget, bool tryMojang, string platformKey, string? adoptiumArch)
    {
        PyDict json = B2dFixtures.ById(id);
        string target = LaunchArch.For(json, "osx", "arm64", "14.5");
        Assert.Equal(expectedTarget, target);
        JavaArch.InstallPlan plan = JavaArch.PlanInstall(component, major, "osx", "arm64", target, false);
        Assert.Equal(expectedTarget, plan.TargetArch);
        Assert.Equal(tryMojang, plan.TryMojang);
        Assert.Equal(platformKey, plan.MojangPlatformKey);
        Assert.Equal(adoptiumArch, plan.AdoptiumArch);
    }

    /// <summary>
    /// 同一份夹具在 Windows / Intel Mac 上不跨架构:目标架构 = 进程架构,平台键就是原来的值
    /// (「非 Apple 芯片行为未变」的钉子)。
    /// </summary>
    [Theory]
    [InlineData("windows", "x64", "windows-x64", "x64")]
    [InlineData("osx", "x64", "mac-os", "x64")]
    public void Same_fixture_on_non_apple_platforms_keeps_the_old_plan(string os, string processArch, string platformKey, string expectedTarget)
    {
        PyDict json = B2dFixtures.ById("1.16.5");
        string target = LaunchArch.For(json, os, processArch, "10.0.19045");
        Assert.Equal(expectedTarget, target);
        JavaArch.InstallPlan plan = JavaArch.PlanInstall("jre-legacy", 8L, os, processArch, target, false);
        Assert.Equal(platformKey, plan.MojangPlatformKey);
        Assert.Null(plan.AdoptiumArch);
    }

    // ------------------------------------------------------------------ 第 5 项:兜底大版本

    /// <summary>
    /// <c>Installer._ensure_any_java()</c> 的兜底大版本:Apple 芯片上 17
    /// (Adoptium 对 mac aarch64 **没有** Java 8 的包,线上证据见 PROGRESS.md 的 B2a 第 1.3 节),
    /// 其它平台一律 8(改动前的原值)。
    /// </summary>
    [Theory]
    [InlineData("osx", "arm64", 17L)]
    [InlineData("osx", "x64", 8L)]
    [InlineData("windows", "x64", 8L)]
    [InlineData("windows", "arm64", 8L)]
    [InlineData("windows", "x86", 8L)]
    [InlineData("linux", "x64", 8L)]
    [InlineData("linux", "arm64", 8L)]
    public void Fallback_major(string os, string processArch, long expected)
    {
        Assert.Equal(expected, JavaArch.FallbackMajor(os, processArch));
    }
}
