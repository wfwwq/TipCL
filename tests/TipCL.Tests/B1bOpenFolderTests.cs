using System;
using System.IO;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B1b:AI 工具「打开文件夹」不再写死 <c>explorer.exe</c>(<c>Launcher\Ai\ExtraTools.cs</c> 的 OpenFolder)。
///
/// 只测 <c>isWindows: false</c> 的路径:<c>Crash.ShellOpen</c> 自己有 mac(<c>open</c>)/ Linux(<c>xdg-open</c>)分支,
/// 测试先用 <see cref="Crash.ShellOpenHook"/> 把它拦下来(<c>finally</c> 里恢复原值),
/// **绝不真的起进程 / 打开资源管理器窗口**。Windows 分支保持原来那一行(<c>explorer.exe</c>),
/// 所以这里一条走 Windows 分支的用例都不写 —— 那会真的弹出资源管理器。
///
/// 文案里的全角标点用 <c>\uXXXX</c> 写死(D-16 / D-24:全角 ':' 与半角 ':' 在源码里长得一样)。
/// </summary>
public class B1bOpenFolderTests
{
    // 打不开:(U+6253 U+4E0D U+5F00 + U+FF1A)
    private const string FailPrefix = "\u6253\u4E0D\u5F00\uFF1A";

    // 已打开 + 半角空格
    private const string OkPrefix = "\u5DF2\u6253\u5F00 ";

    private static string TempDir()
    {
        string dir = Path.Combine(TestHome.Root, "b1b-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Non_windows_hands_the_directory_to_ShellOpen()
    {
        string dir = TempDir();
        string seen = null;
        Action<string> previous = Crash.ShellOpenHook;
        try
        {
            Crash.ShellOpenHook = delegate(string path)
            {
                seen = path;
            };

            string result = ExtraTools.OpenFolderNow(dir, isWindows: false);

            Assert.Equal(dir, seen);
            Assert.Equal(OkPrefix + dir, result);
        }
        finally
        {
            Crash.ShellOpenHook = previous;
        }
    }

    [Fact]
    public void Non_windows_reports_failure_when_the_hook_throws()
    {
        string dir = TempDir();
        Action<string> previous = Crash.ShellOpenHook;
        try
        {
            Crash.ShellOpenHook = delegate
            {
                throw new InvalidOperationException("hook-boom");
            };

            string result = ExtraTools.OpenFolderNow(dir, isWindows: false);

            Assert.Equal(FailPrefix + "hook-boom", result);
        }
        finally
        {
            Crash.ShellOpenHook = previous;
        }
    }

    [Fact]
    public void Non_windows_reports_success_without_starting_anything()
    {
        string dir = TempDir();
        Action<string> previous = Crash.ShellOpenHook;
        try
        {
            Crash.ShellOpenHook = delegate
            {
            };

            Assert.Equal(OkPrefix + dir, ExtraTools.OpenFolderNow(dir, isWindows: false));
            // 复用同一个目录:结果只取决于传入的目录,不残留状态
            Assert.Equal(OkPrefix + dir, ExtraTools.OpenFolderNow(dir, isWindows: false));
        }
        finally
        {
            Crash.ShellOpenHook = previous;
        }
    }

    [Fact]
    public void The_hook_is_restored_after_the_test()
    {
        // 这条同时证明上一个用例的 finally 生效(执行顺序与 xunit 一致:声明顺序)
        Assert.Null(Crash.ShellOpenHook);
    }
}
