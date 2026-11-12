using System;
using System.IO;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B1b:全局模组软链接(<c>Launcher\GlobalMods.cs</c> 的 <c>_link_file</c>;A3 清点表 D2)。
///
/// 原实现无条件调 kernel32 的 <c>CreateSymbolicLinkW</c>:mac 上抛 <c>DllNotFoundException</c> 被 catch 吞掉,
/// 静默退化成 <c>File.Copy</c>。现在非 Windows 走 <c>File.CreateSymbolicLink</c>,Windows 分支不变。
///
/// 测试**不真的建符号链接**(Windows 上要特权、结果不稳定):用注入的假委托验证「非 Windows 分支」,
/// 目标已存在 / 委托失败 / 委托返回 false 三种情况都只走现有兜底逻辑。Windows 分支故意不测
/// (公开重载在本机就走 Windows 分支,那会真的创建链接)。
/// </summary>
public class B1bGlobalModsLinkTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(TestHome.Root, "b1b-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string MakeSource(string dir, string content = "mod-bytes")
    {
        string src = Path.Combine(dir, "src-" + Guid.NewGuid().ToString("N") + ".jar");
        File.WriteAllText(src, content);
        return src;
    }

    [Fact]
    public void Non_windows_calls_the_link_creator_with_link_then_target()
    {
        string dir = TempDir();
        string src = MakeSource(dir);
        string dest = Path.Combine(dir, "link.jar");
        (string Link, string Target) seen = (null, null);
        bool called = false;

        bool ok = GlobalMods._link_file(dest, src, isWindows: false, delegate(string link, string target)
        {
            called = true;
            seen = (link, target);
            return true;
        });

        Assert.True(ok);
        Assert.True(called);
        Assert.Equal(Path.GetFullPath(dest), seen.Link);
        Assert.Equal(Path.GetFullPath(src), seen.Target);
        Assert.False(File.Exists(dest));      // 假委托什么都不做
    }

    [Fact]
    public void A_link_failure_falls_back_to_copying_the_file()
    {
        string dir = TempDir();
        string src = MakeSource(dir, "fallback-bytes");
        string dest = Path.Combine(dir, "copy.jar");

        bool ok = GlobalMods._link_file(dest, src, isWindows: false, delegate
        {
            throw new IOException("link-unsupported");
        });

        Assert.True(ok);
        Assert.True(File.Exists(dest));
        Assert.Equal("fallback-bytes", File.ReadAllText(dest));
    }

    [Fact]
    public void A_link_creator_returning_false_falls_back_to_copying_the_file()
    {
        string dir = TempDir();
        string src = MakeSource(dir, "flag-false-bytes");
        string dest = Path.Combine(dir, "copy2.jar");

        bool ok = GlobalMods._link_file(dest, src, isWindows: false, delegate
        {
            return false;
        });

        Assert.True(ok);
        Assert.True(File.Exists(dest));
        Assert.Equal("flag-false-bytes", File.ReadAllText(dest));
    }

    [Fact]
    public void The_copy_fallback_keeps_the_source_timestamp()
    {
        string dir = TempDir();
        string src = MakeSource(dir, "ts");
        DateTime stamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(src, stamp);
        string dest = Path.Combine(dir, "copy3.jar");

        Assert.True(GlobalMods._link_file(dest, src, isWindows: false, delegate
        {
            throw new IOException("link-unsupported");
        }));

        double delta = Math.Abs((File.GetLastWriteTimeUtc(dest) - File.GetLastWriteTimeUtc(src)).TotalSeconds);
        Assert.True(delta < 1.0, "时间戳应该一起带过来,实际差 " + delta + " 秒");
    }

    [Fact]
    public void An_existing_destination_returns_false_without_calling_the_link_creator()
    {
        string dir = TempDir();
        string src = MakeSource(dir, "new");
        string dest = Path.Combine(dir, "exists.jar");
        File.WriteAllText(dest, "old");
        bool called = false;

        bool ok = GlobalMods._link_file(dest, src, isWindows: false, delegate
        {
            called = true;
            return true;
        });

        Assert.False(ok);
        Assert.False(called);
        Assert.Equal("old", File.ReadAllText(dest));
    }

    [Fact]
    public void A_missing_source_fails_without_leaving_the_destination()
    {
        string dir = TempDir();
        string dest = Path.Combine(dir, "nosrc.jar");

        bool ok = GlobalMods._link_file(dest, Path.Combine(dir, "does-not-exist.jar"), isWindows: false, delegate
        {
            return false;
        });

        Assert.False(ok);
        Assert.False(File.Exists(dest));
    }
}