// AI3-linux-real:在真 Linux(WSL2 Ubuntu 24.04)上跑出来的缺陷的回归。
//   * 显卡:NVIDIA 专有驱动下 lspci / /sys / /proc 给不出显存;WSL2 里 lspci 只有 GPU-PV 占位
//     「Microsoft Corporation Basic Render Driver」,真实独显只有 nvidia-smi 看得到 → 合并 nvidia-smi。
//     占位设备以前还被 ShaderAdvisor 当成独显(按中配推荐)。
//   * 微软登录:Auth.OpenBrowser 只有 Windows 分支,Linux / macOS 上登录页从不自动打开 → 补 xdg-open / open。
// 全是纯函数测试,不起进程;最后一个只在 Linux 上对照 /proc 做只读核对(其他系统直接返回)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

public class LinuxRealTests
{
    // WSL2(本机实测)的 lspci -mm 原样
    private const string WslLspciMm =
        "0006:00:00.0 \"System peripheral\" \"Red Hat, Inc.\" \"Virtio file system\" -r01 -p00 \"Red Hat, Inc.\" \"Virtio file system\"\n"
        + "2a8c:00:00.0 \"3D controller\" \"Microsoft Corporation\" \"Basic Render Driver\" -p00 \"\" \"\"\n"
        + "3b6d:00:00.0 \"3D controller\" \"Microsoft Corporation\" \"Basic Render Driver\" -p00 \"\" \"\"\n"
        + "7788:00:00.0 \"SCSI storage controller\" \"Red Hat, Inc.\" \"Virtio 1.0 console\" -r01 -p00 \"Red Hat, Inc.\" \"Virtio 1.0 console\"\n";

    [Fact]
    public void NvidiaSmi_ParsesNoUnitsAndMiB_AndIgnoresErrorText()
    {
        List<object> gpus = SystemInfo.ParseNvidiaSmi(
            "NVIDIA GeForce RTX 5060 Laptop GPU, 8151, 573.22\n"
            + "NVIDIA GeForce RTX 3060, 12288 MiB, 550.54.14\r\n"
            + "name, memory.total [MiB], driver_version\n");
        Assert.Equal(2, gpus.Count);
        PyDict first = Py.D(gpus[0]);
        Assert.Equal("NVIDIA GeForce RTX 5060 Laptop GPU", Py.S(first.Get("name")));
        Assert.Equal(8151L, Py.Int(first.Get("vram_mb")));
        Assert.Equal("573.22", Py.S(first.Get("driver_version")));
        Assert.Equal("NVIDIA", Py.S(first.Get("vendor")));
        Assert.Equal(12288L, Py.Int(Py.D(gpus[1]).Get("vram_mb")));

        Assert.Empty(SystemInfo.ParseNvidiaSmi(""));
        Assert.Empty(SystemInfo.ParseNvidiaSmi(null!));
        Assert.Empty(SystemInfo.ParseNvidiaSmi(
            "NVIDIA-SMI has failed because it couldn't communicate with the NVIDIA driver. Make sure that the latest NVIDIA driver is installed and running.\n"));
        Assert.Empty(SystemInfo.ParseNvidiaSmi("No devices were found\n"));
        Assert.Empty(SystemInfo.ParseNvidiaSmi("Foo, [N/A], 1.0\n"));
    }

    [Fact]
    public void Wsl_LspciPlaceholders_AreReplacedByNvidiaSmi()
    {
        List<object> lspci = SystemInfo.ParseLspci(WslLspciMm);
        Assert.Equal(2, lspci.Count); // 两个 GPU-PV 占位(本机是 RTX 5060 + Radeon 610M)
        Assert.All(lspci, g => Assert.True(SystemInfo.IsWslPlaceholderGpu(Py.S(Py.D(g).Get("name")))));
        Assert.Equal("Microsoft", Py.S(Py.D(lspci[0]).Get("vendor")));

        // 没有 nvidia-smi:原样保留(行为与以前一致)
        List<object> plain = SystemInfo.MergeLinuxGpus(lspci, new List<object>(), new List<object>());
        Assert.Equal(2, plain.Count);

        List<object> smi = SystemInfo.ParseNvidiaSmi("NVIDIA GeForce RTX 5060 Laptop GPU, 8151, 573.22\n");
        List<object> merged = SystemInfo.MergeLinuxGpus(lspci, new List<object>(), new List<object>(), smi);
        Assert.Equal(2, merged.Count);
        PyDict top = Py.D(merged[0]);
        Assert.Equal("NVIDIA GeForce RTX 5060 Laptop GPU", Py.S(top.Get("name")));
        Assert.Equal(8151L, Py.Int(top.Get("vram_mb")));
        Assert.True(SystemInfo.IsWslPlaceholderGpu(Py.S(Py.D(merged[1]).Get("name"))));
        // 输入没被改
        Assert.True(SystemInfo.IsWslPlaceholderGpu(Py.S(Py.D(lspci[0]).Get("name"))));
    }

    [Fact]
    public void NativeNvidia_GetsVramAndDriverVersionFromNvidiaSmi()
    {
        List<object> lspci = SystemInfo.ParseLspci(
            "00:02.0 \"VGA compatible controller\" \"Intel Corporation\" \"Alder Lake-P GT2 [Iris Xe Graphics]\" -r0c \"Lenovo\" \"Device 3b1a\"\n"
            + "01:00.0 \"3D controller\" \"NVIDIA Corporation\" \"AD107M [GeForce RTX 4060 Max-Q / Mobile]\" -ra1 \"Lenovo\" \"Device 3b1a\"\n");
        var drm = new List<object>
        {
            PyDict.Of("name", "Intel GPU (0x46a6)", "vendor", "Intel", "driver", "i915", "vram_mb", 0L),
            PyDict.Of("name", "NVIDIA GPU (0x28a0)", "vendor", "NVIDIA", "driver", "nvidia", "vram_mb", 0L),
        };
        List<object> smi = SystemInfo.ParseNvidiaSmi("NVIDIA GeForce RTX 4060 Laptop GPU, 8188, 550.120\n");
        List<object> merged = SystemInfo.MergeLinuxGpus(lspci, drm, new List<object>(), smi);
        Assert.Equal(2, merged.Count);
        PyDict nv = Py.D(merged.Single(g => Py.S(Py.D(g).Get("vendor")) == "NVIDIA"));
        Assert.Equal("NVIDIA GeForce RTX 4060 Laptop GPU", Py.S(nv.Get("name")));
        Assert.Equal(8188L, Py.Int(nv.Get("vram_mb")));
        Assert.Equal("nvidia", Py.S(nv.Get("driver")));
        Assert.Equal("550.120", Py.S(nv.Get("driver_version")));
        PyDict intel = Py.D(merged.Single(g => Py.S(Py.D(g).Get("vendor")) == "Intel"));
        Assert.Equal("i915", Py.S(intel.Get("driver")));

        // ShaderAdvisor 按显存挑独显
        Assert.False(ShaderAdvisor.IsIntegrated(Py.S(nv.Get("name"))));
    }

    [Fact]
    public void NoLspci_NoDrm_NvidiaSmiAlone_IsStillReported()
    {
        List<object> smi = SystemInfo.ParseNvidiaSmi("NVIDIA GeForce GTX 1660, 6144, 535.183.01\n");
        List<object> merged = SystemInfo.MergeLinuxGpus(new List<object>(), new List<object>(), new List<object>(), smi);
        Assert.Equal("NVIDIA GeForce GTX 1660", Py.S(Py.D(merged.Single()).Get("name")));
        Assert.Equal(6144L, Py.Int(Py.D(merged.Single()).Get("vram_mb")));
    }

    [Fact]
    public void ShaderAdvisor_TreatsWslPlaceholderAsIntegrated()
    {
        // lspci -mm 拼出来的名字是「厂商 + 设备」,以前「microsoft basic」匹配不到,被当成独显按中配推荐
        Assert.True(ShaderAdvisor.IsIntegrated("Microsoft Corporation Basic Render Driver"));
        Assert.Equal("low", ShaderAdvisor.HardwareTier(
            PyDict.Of("gpu", "Microsoft Corporation Basic Render Driver", "vram_mb", 0L, "ram_mb", 16384L), out _));
    }

    [Fact]
    public void BrowserStartInfo_UsesXdgOpenOnLinux_OpenOnMac_ShellExecuteOnWindows()
    {
        const string url = "https://www.microsoft.com/link?otc=AB CD&x=\"1\"";
        var linux = Auth.BrowserStartInfo(url, windows: false, mac: false);
        Assert.NotNull(linux);
        Assert.Equal("xdg-open", linux.FileName);
        Assert.False(linux.UseShellExecute);
        Assert.Equal(new[] { url }, linux.ArgumentList.ToArray());
        Assert.Equal("", linux.Arguments);

        var mac = Auth.BrowserStartInfo(url, windows: false, mac: true);
        Assert.Equal("open", mac.FileName);
        Assert.Equal(new[] { url }, mac.ArgumentList.ToArray());

        var win = Auth.BrowserStartInfo("https://www.microsoft.com/link", windows: true, mac: false);
        Assert.True(win.UseShellExecute);
        Assert.Equal("https://www.microsoft.com/link", win.FileName);

        // 只认 http(s):别的协议 / 本地路径 / 空串一律不打开
        Assert.Null(Auth.BrowserStartInfo("file:///etc/passwd", false, false));
        Assert.Null(Auth.BrowserStartInfo("/tmp/x", false, false));
        Assert.Null(Auth.BrowserStartInfo("javascript:alert(1)", true, false));
        Assert.Null(Auth.BrowserStartInfo("  ", false, false));
        Assert.Null(Auth.BrowserStartInfo(null!, false, false));
    }

    [Fact]
    public void OpenBrowser_InTestProcess_OnlyReachesTheHook()
    {
        // 测试隔离装了空钩子;这里换成记录钩子,确认不论平台都只交给钩子、不起进程
        Action<string> before = Auth.OpenBrowserHook;
        var seen = new List<string>();
        Auth.OpenBrowserHook = seen.Add;
        try
        {
            Auth.OpenBrowser("https://www.microsoft.com/link");
        }
        finally
        {
            Auth.OpenBrowserHook = before;
        }
        Assert.Equal(new[] { "https://www.microsoft.com/link" }, seen);
    }

    [Fact]
    public void OnLinux_SnapshotMatchesProcCpuinfoAndMeminfo()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/proc/cpuinfo") || !File.Exists("/proc/meminfo"))
        {
            return; // 只在 Linux 上有意义;Windows / macOS 由各自的探测测试覆盖
        }
        PyDict cpu = SystemInfo.Cpu();
        string[] cpuinfo = File.ReadAllLines("/proc/cpuinfo");
        Assert.Equal((long)cpuinfo.Count(l => l.StartsWith("processor", StringComparison.Ordinal)), Py.Int(cpu.Get("cores_logical")));
        string? model = cpuinfo.FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal));
        if (model != null)
        {
            Assert.Equal(string.Join(" ", model.Split(':', 2)[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)), Py.S(cpu.Get("name")));
        }
        string total = File.ReadAllLines("/proc/meminfo").First(l => l.StartsWith("MemTotal:", StringComparison.Ordinal));
        long totalKb = long.Parse(new string(total.Where(char.IsDigit).ToArray()));
        PyDict mem = SystemInfo.Memory();
        Assert.Equal(totalKb / 1024, Py.Int(mem.Get("total_mb")));
        Assert.Equal("/proc/meminfo", Py.S(mem.Get("source")));
    }
}
