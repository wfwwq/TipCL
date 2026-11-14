using System;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.Core;

/// <summary>
/// Core 复用层（Launcher 目录）数据类/纯逻辑单测（M1-A5）。
/// 用例承接 M1-A4 的 src/TipCL.Core/Tests/CoreReuseSmokeTests.cs（内联断言 → xunit 化）。
/// </summary>
public class LauncherLogicTests
{
    [Fact]
    public void Config_Defaults_HaveMinecraftInstancesDir()
    {
        var cfg = (PyDict)Py.DeepCopy(Config.DEFAULT_CONFIG);
        Assert.True(Py.Truthy(cfg));
        Assert.Equal(".minecraft", Py.Str(cfg.Req("instances_dir")));
    }

    [Fact]
    public void Instances_SanitizeInstanceName_CleansIllegalChars()
    {
        // ResolveName 是查重归一（依赖实例目录状态），清洗行为在 SanitizeInstanceName
        Assert.Equal("a-b", Instances.SanitizeInstanceName("a/b"));
        Assert.NotEqual("CON", Instances.SanitizeInstanceName("CON"), StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Survival 1.20", Instances.SanitizeInstanceName("Survival 1.20"));
        Assert.Equal("游戏", Instances.SanitizeInstanceName(""));
    }

    [Fact]
    public void Installer_McTuple_ParsesVersions()
    {
        long[] v = Installer._mc_tuple("1.20.1");
        Assert.True(v.Length >= 3);
        Assert.Equal(1L, v[0]);
        Assert.Equal(20L, v[1]);
        Assert.Equal(1L, v[2]);
    }

    [Fact]
    public void Java_PlatformKey_NotEmpty()
    {
        Assert.False(string.IsNullOrEmpty(Java.PLATFORM_KEY));
    }
}
