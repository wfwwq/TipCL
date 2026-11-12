using System;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B1b:CA1416 的守卫改写(<c>Launcher\Sysinfo.cs</c> 的 <c>_winreg_value</c>)。
///
/// 原来的守卫是 <c>if (!Utils.IS_WINDOWS) return dflt;</c>,分析器不认自定义标志 → 3 条 CA1416;
/// 现在换成 <c>OperatingSystem.IsWindows()</c>(与 <c>Utils.IS_WINDOWS</c> 同为
/// <c>RuntimeInformation.IsOSPlatform(OSPlatform.Windows)</c>,语义不变)。
///
/// 只读注册表(HKLM 下一个随机不存在的子键),不写任何东西。
/// </summary>
public class B1bSysinfoTests
{
    private static string MissingSubKey()
    {
        return "SOFTWARE\\TipCL-B1b-Missing-" + Guid.NewGuid().ToString("N");
    }

    [Fact]
    public void A_missing_registry_key_returns_the_given_default()
    {
        Assert.Equal("dflt", Sysinfo._winreg_value(MissingSubKey(), "nope", "dflt"));
    }

    [Fact]
    public void A_missing_registry_key_returns_the_empty_default()
    {
        Assert.Equal("", Sysinfo._winreg_value(MissingSubKey(), "nope"));
    }

    [Fact]
    public void A_missing_value_under_an_existing_key_returns_the_default()
    {
        // 键存在、值不存在 —— 走的是 GetValue 返回 null 的那条分支
        string name = "TipCL-B1b-Nonexistent-" + Guid.NewGuid().ToString("N");

        Assert.Equal("dflt", Sysinfo._winreg_value("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", name, "dflt"));
    }
}
