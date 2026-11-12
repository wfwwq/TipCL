using System;
using System.IO;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2c1 第 4 项:<c>Preflight.FreeBytes</c> 的平台分支。
///
/// 期望值来源:
/// <list type="bullet">
/// <item>「Windows 走 kernel32、非 Windows 走别的东西」= 本包新增的分支(任务书第 4 项);
/// Windows 那支的实现是**读过的源码行为**(<c>GetDiskFreeSpaceEx</c> 返回 false 时返回 null,
/// 异常被吞成 null),逐字保留;</item>
/// <item><c>FreeBytesHook</c> 优先于两个平台委托 = **读过的源码行为**(改动前 hook 非 null 就直接 return);</item>
/// <item>真实 <c>DriveInfo</c> 委托对 <see cref="TestHome.Root"/> 取值大于 0 = 本机(Windows)实测,
/// 断言的是「委托本身能取到值」,**不代表 mac 上挂载点的行为已验证**。</item>
/// </list>
///
/// 全部用例都不起进程、不联网,只碰一个内部重载与两个委托。
/// </summary>
public class B2c1PreflightFreeBytesTests
{
    /// <summary>isWindows 决定走哪个委托,另一个**一次都不调用**。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_platform_flag_selects_the_matching_delegate(bool isWindows)
    {
        int windowsCalls = 0;
        int unixCalls = 0;
        Func<string, long?> windows = delegate(string p) { windowsCalls++; return 111L; };
        Func<string, long?> unix = delegate(string p) { unixCalls++; return 222L; };

        long? got = Preflight.FreeBytes(TestHome.Root, isWindows, windows, unix);

        Assert.Equal(isWindows ? 111L : 222L, got);
        Assert.Equal(isWindows ? 1 : 0, windowsCalls);
        Assert.Equal(isWindows ? 0 : 1, unixCalls);
    }

    /// <summary>委托拿到的是规范化后的绝对路径(<c>Utils.AbsPath</c>),调用点传什么形态都行。</summary>
    [Fact]
    public void The_delegate_receives_an_absolute_path()
    {
        string seen = null;
        Func<string, long?> unix = delegate(string p) { seen = p; return 1L; };

        long? got = Preflight.FreeBytes(TestHome.Root, false, delegate(string p) { return 0L; }, unix);

        Assert.Equal(1L, got);
        Assert.Equal(Path.GetFullPath(TestHome.Root), seen);
        Assert.True(Path.IsPathRooted(seen));
    }

    /// <summary>worker 抛异常一律吞成 null(与改动前一致:异常返回 null,不往上抛)。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_throwing_delegate_yields_null(bool isWindows)
    {
        Func<string, long?> boom = delegate(string p) { throw new IOException("boom"); };

        Assert.Null(Preflight.FreeBytes(TestHome.Root, isWindows, boom, boom));
    }

    /// <summary>
    /// <c>FreeBytesHook</c> 的优先级不变:hook 非 null 时两个委托都不碰。
    /// 改全局静态后必须在 finally 里恢复原值。
    /// </summary>
    [Fact]
    public void The_hook_still_wins_and_is_restored()
    {
        Func<string, long?> saved = Preflight.FreeBytesHook;
        int windowsCalls = 0;
        int unixCalls = 0;
        try
        {
            Preflight.FreeBytesHook = delegate(string p) { return 333L; };

            long? got = Preflight.FreeBytes(
                TestHome.Root, false,
                delegate(string p) { windowsCalls++; return 1L; },
                delegate(string p) { unixCalls++; return 2L; });

            Assert.Equal(333L, got);
            Assert.Equal(0, windowsCalls);
            Assert.Equal(0, unixCalls);
        }
        finally
        {
            Preflight.FreeBytesHook = saved;
        }

        Assert.Same(saved, Preflight.FreeBytesHook);
    }

    /// <summary>hook 为 null 时委托必须被真的用上(上面那条的对照)。</summary>
    [Fact]
    public void Without_a_hook_the_delegate_is_used()
    {
        Func<string, long?> saved = Preflight.FreeBytesHook;
        try
        {
            Preflight.FreeBytesHook = null;

            long? got = Preflight.FreeBytes(
                TestHome.Root, false,
                delegate(string p) { return 1L; },
                delegate(string p) { return 444L; });

            Assert.Equal(444L, got);
        }
        finally
        {
            Preflight.FreeBytesHook = saved;
        }
    }

    /// <summary>
    /// 真实 <c>DriveInfo</c> 委托(非 Windows 分支的生产实现)在 <see cref="TestHome.Root"/> 上
    /// 必须取到大于 0 的值。本机是 Windows,<c>DriveInfo</c> 接受绝对目录路径并归到它所在的卷。
    /// </summary>
    [Fact]
    public void The_real_drive_info_delegate_reads_a_positive_value_for_the_test_home()
    {
        long? got = Preflight.FreeBytes(TestHome.Root, false, Preflight.FreeBytesDriveInfo, Preflight.FreeBytesDriveInfo);

        Assert.NotNull(got);
        Assert.True(got.Value > 0L, "DriveInfo 取到的剩余空间应该大于 0,实际 " + got);
    }

    /// <summary>
    /// Windows 分支的生产实现(kernel32)仍然能取到值,而且与 <c>DriveInfo</c> 对同一个卷的读数同量级
    /// —— 这条是「Windows 行为未变」的钉子(改动前唯一实现就是它)。
    /// </summary>
    [Fact]
    public void The_real_windows_api_delegate_still_reads_a_positive_value()
    {
        long? api = Preflight.FreeBytes(TestHome.Root, true, Preflight.FreeBytesWindowsApi, Preflight.FreeBytesWindowsApi);
        long? drive = Preflight.FreeBytes(TestHome.Root, false, Preflight.FreeBytesDriveInfo, Preflight.FreeBytesDriveInfo);

        if (!OperatingSystem.IsWindows())
        {
            // 非 Windows 上 kernel32 不存在,FreeBytes 会把 DllNotFoundException 吞成 null。
            // 本机是 Windows,这条 mac 行为**未验证**,所以这里不做任何断言。
            return;
        }

        Assert.NotNull(api);
        Assert.True(api.Value > 0L, "kernel32 取到的剩余空间应该大于 0,实际 " + api);
        Assert.NotNull(drive);
        // 同一个卷上的两次读数:允许之间的自然波动,但必须同量级(差值小于 20%)。
        long diff = Math.Abs(api.Value - drive.Value);
        Assert.True(diff * 5L < Math.Max(api.Value, drive.Value), "两次读数相差过大: " + api + " vs " + drive);
    }
}
