using System;
using System.IO;
using System.Runtime.CompilerServices;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 测试进程的数据目录隔离(红线:绝不许碰用户真实的 %LOCALAPPDATA%\TipCL)。
///
/// 静态部分(<see cref="Root"/> + <see cref="Init"/>)是 A3 起就有的进程级隔离:
/// [ModuleInitializer] 在本程序集任何代码执行之前运行 —— 包括进程内执行器 Program.Main
/// (与测试同一个程序集,所以同样会触发),也包括 VSTest 的 testhost。
/// 它把 TIPCL_HOME 指到 bin 目录下的 test-home\{每次运行一个新 Guid},
/// 并同步调用 Utils.SetRoot(同一路径),这样 Utils.ROOT 与后续一切走 ROOT 的代码
/// (Config / PPath / log 等)都落在临时目录里。
///
/// 实例部分(<see cref="Dir"/> + <see cref="Dispose"/>)是 A6 为搬来的 Windows 测试补的:
/// 与 Windows `tests\TipCL.Tests\TestHome.cs` 同名同义(无参构造 / Dir / Dispose),
/// 唯一区别是临时目录建在 <see cref="Root"/> 之下,而**不是** %TEMP%
/// —— 红线要求测试的临时文件也必须落在仓库内。
/// </summary>
public sealed class TestHome : IDisposable
{
    public static string Root { get; private set; } = "";

    private readonly string _old;

    /// <summary>本实例的临时数据目录(构造时创建,Dispose 时删除)。</summary>
    public string Dir { get; }

    [ModuleInitializer]
    internal static void Init()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "test-home", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("TIPCL_HOME", root);
        Utils.SetRoot(root);
        Root = root;
    }

    public TestHome()
    {
        _old = Environment.GetEnvironmentVariable("TIPCL_HOME");
        Dir = Path.Combine(Root, "t-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);
        Environment.SetEnvironmentVariable("TIPCL_HOME", Dir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TIPCL_HOME", _old);
        try
        {
            Directory.Delete(Dir, recursive: true);
        }
        catch (Exception)
        {
        }
    }
}

public class TestHomeIsolationTests
{
    [Fact]
    public void Isolated_home_is_created_under_the_test_bin_directory()
    {
        Assert.False(string.IsNullOrEmpty(TestHome.Root));
        Assert.StartsWith(AppContext.BaseDirectory, TestHome.Root, StringComparison.Ordinal);
        Assert.True(Directory.Exists(TestHome.Root), "隔离目录必须真实存在: " + TestHome.Root);
    }

    /// <summary>进程内执行器 Program.Main 与本程序集同一个 module,所以它也会先触发隔离。</summary>
    [Fact]
    public void Utils_ROOT_is_the_isolated_home_and_is_not_the_real_user_dir()
    {
        string realUserDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TipCL");

        Assert.StartsWith(AppContext.BaseDirectory, Utils.ROOT, StringComparison.Ordinal);
        Assert.NotEqual(realUserDir, Utils.ROOT);
        Assert.Equal(TestHome.Root, Utils.ROOT);
    }

    [Fact]
    public void TIPCL_HOME_points_at_the_isolated_home()
    {
        Assert.Equal(TestHome.Root, Environment.GetEnvironmentVariable("TIPCL_HOME"));
    }

    /// <summary>Utils.ResolveRoot(TIPCL_HOME 有值)= 该路径(源码 Utils.cs 309-322,经 PPath 规范化)。</summary>
    [Fact]
    public void ResolveRoot_honours_TIPCL_HOME()
    {
        string original = Environment.GetEnvironmentVariable("TIPCL_HOME");
        string probe = Path.Combine(AppContext.BaseDirectory, "test-home", "resolve-probe");
        try
        {
            Environment.SetEnvironmentVariable("TIPCL_HOME", probe);
            Assert.Equal(new PPath(probe).ToString(), Utils.ResolveRoot());
            Assert.Contains("resolve-probe", Utils.ResolveRoot(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIPCL_HOME", original);
        }
    }

    /// <summary>TIPCL_HOME 为空时:Windows → %LOCALAPPDATA%\TipCL;其它平台 → ~/.tipcl(只算字符串,不落盘)。</summary>
    [Fact]
    public void ResolveRoot_without_TIPCL_HOME_falls_back_to_the_platform_default()
    {
        string original = Environment.GetEnvironmentVariable("TIPCL_HOME");
        try
        {
            Environment.SetEnvironmentVariable("TIPCL_HOME", "");
            string expected = Utils.IS_WINDOWS
                ? new PPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TipCL").ToString()
                : new PPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tipcl").ToString();
            Assert.Equal(expected, Utils.ResolveRoot());
            Assert.False(Utils.ResolveRoot().StartsWith(AppContext.BaseDirectory, StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIPCL_HOME", original);
        }
    }

    /// <summary>ResolveRoot 的路径拼装经 PPath:结尾不会多一个分隔符。</summary>
    [Fact]
    public void SetRoot_and_resolve_return_ppath_normalized_spelling()
    {
        string original = Environment.GetEnvironmentVariable("TIPCL_HOME");
        string probe = Path.Combine(AppContext.BaseDirectory, "test-home", "norm-probe");
        try
        {
            Environment.SetEnvironmentVariable("TIPCL_HOME", probe + Path.DirectorySeparatorChar);
            Assert.Equal(new PPath(probe).ToString(), Utils.ResolveRoot());
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIPCL_HOME", original);
        }
    }

    /// <summary>平台键取自 RuntimeInformation,三种取值之一,IS_WINDOWS / IS_MAC 与之一致(Utils.cs 241-247, 347-357)。</summary>
    [Fact]
    public void Platform_keys_are_consistent()
    {
        Assert.Contains(Utils.OS_NAME, new[] { "windows", "osx", "linux" });
        Assert.Equal(Utils.OS_NAME == "windows", Utils.IS_WINDOWS);
        Assert.Equal(Utils.OS_NAME == "osx", Utils.IS_MAC);
        Assert.Contains(Utils.ARCH, new[] { "x64", "x86", "arm64" });
    }

    /// <summary>A6:`new TestHome()` 期间 TIPCL_HOME 指向实例自己的 Dir,且 Dir 落在进程级隔离目录之下。</summary>
    [Fact]
    public void New_instance_switches_TIPCL_HOME_to_a_dir_under_the_isolated_root()
    {
        using var home = new TestHome();

        Assert.Equal(home.Dir, Environment.GetEnvironmentVariable("TIPCL_HOME"));
        Assert.StartsWith(TestHome.Root, home.Dir, StringComparison.Ordinal);
        Assert.NotEqual(TestHome.Root, home.Dir);
        Assert.True(Directory.Exists(home.Dir), "实例目录必须真实存在: " + home.Dir);
    }

    /// <summary>A6:`Dispose` 把 TIPCL_HOME 恢复到构造时的值(即进程级隔离目录)并删除 Dir。</summary>
    [Fact]
    public void Dispose_restores_TIPCL_HOME_and_removes_the_dir()
    {
        string dir;
        using (var home = new TestHome())
        {
            dir = home.Dir;
            Assert.True(Directory.Exists(dir));
        }

        Assert.Equal(TestHome.Root, Environment.GetEnvironmentVariable("TIPCL_HOME"));
        Assert.False(Directory.Exists(dir), "Dispose 之后实例目录必须已删除: " + dir);
    }
}
