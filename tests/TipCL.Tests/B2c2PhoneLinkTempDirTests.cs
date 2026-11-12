using System;
using System.IO;
using System.Runtime.CompilerServices;
using TipCL.Core.Launcher.Sync;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2c2 第 4 项:PhoneLink 的临时 zip 目录改成可注入。
///
/// 本文件自带一个 <b>ModuleInitializer</b>:把 <c>PhoneLink.TempDirProvider</c> 指到
/// <c>TestHome.Root/phone-sync</c>,这样 <c>PhoneLink.TempZip()</c> 不再往真实的 %TEMP% 写东西
/// (A6 把 <c>PhoneLinkTests</c> 排除的第二个原因就是这个,见 PROGRESS.md 的 A6 Lead 验收)。
///
/// <b>委托必须是惰性的</b>:<c>TestHome.Root</c> 由 TestHomeIsolation.cs 的 ModuleInitializer 赋值,
/// 而模块初始化器之间的执行顺序**没有保证**,所以这里不能在 Init 里就把路径算成字符串存下来
/// (没有改 <c>TestHome.Init()</c>)。
///
/// 用例不联网、不起进程;动的目录都在 <see cref="TestHome.Root"/> 之下。
/// </summary>
internal static class B2c2PhoneLinkTempDirModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        PhoneLink.TempDirProvider = delegate { return Path.Combine(TestHome.Root, "phone-sync"); };
    }
}

public class B2c2PhoneLinkTempDirTests
{
    /// <summary>
    /// 默认落点仍是原来那一句:系统临时目录 + tipcl-phone-sync(只比字符串,**不调用它去建目录**)。
    /// </summary>
    [Fact]
    public void The_default_provider_is_the_original_system_temp_subdirectory()
    {
        Assert.Equal(
            Path.Combine(Path.GetTempPath(), "tipcl-phone-sync"),
            PhoneLink.DefaultTempDir());
    }

    /// <summary>测试进程里的提供者已经被本文件的 ModuleInitializer 换成隔离目录(mac 上默认值 = 原来那句)。</summary>
    [Fact]
    public void The_test_process_provider_points_under_the_isolated_root()
    {
        Assert.NotNull(PhoneLink.TempDirProvider);

        string dir = PhoneLink.TempDirProvider();

        Assert.Equal(Path.Combine(TestHome.Root, "phone-sync"), dir);
        Assert.StartsWith(TestHome.Root, dir, StringComparison.Ordinal);
        Assert.NotEqual(PhoneLink.DefaultTempDir(), dir);
    }

    /// <summary>TempZip 只返回路径并建出**父目录**;文件本身由调用方写(这里不写)。</summary>
    [Fact]
    public void Temp_zip_lands_under_the_isolated_root_and_creates_only_the_directory()
    {
        string zip = PhoneLink.TempZip();

        Assert.StartsWith(TestHome.Root, zip, StringComparison.Ordinal);
        Assert.EndsWith(".zip", zip, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(TestHome.Root, "phone-sync"), Path.GetDirectoryName(zip));
        Assert.True(Directory.Exists(Path.GetDirectoryName(zip)), "TempZip 必须建出落点目录: " + Path.GetDirectoryName(zip));
        Assert.False(File.Exists(zip), "TempZip 不该建出空 zip 文件");
    }

    /// <summary>两次调用名字不同(每次一个 Guid),落在同一个目录。</summary>
    [Fact]
    public void Two_temp_zips_get_distinct_names_in_the_same_directory()
    {
        string a = PhoneLink.TempZip();
        string b = PhoneLink.TempZip();

        Assert.NotEqual(a, b);
        Assert.Equal(Path.GetDirectoryName(a), Path.GetDirectoryName(b));
    }

    /// <summary>提供者可以在用例里临时换掉,并在 finally 里恢复原值(D-红线的静态恢复要求)。</summary>
    [Fact]
    public void The_provider_can_be_swapped_and_is_restored_in_finally()
    {
        Func<string> original = PhoneLink.TempDirProvider;
        string swapped = Path.Combine(TestHome.Root, "b2c2-swap-" + Guid.NewGuid().ToString("N"));
        try
        {
            PhoneLink.TempDirProvider = delegate { return swapped; };

            string zip = PhoneLink.TempZip();

            Assert.StartsWith(swapped, zip, StringComparison.Ordinal);
            Assert.Equal(swapped, Path.GetDirectoryName(zip));
            Assert.True(Directory.Exists(swapped));
        }
        finally
        {
            PhoneLink.TempDirProvider = original;
        }

        Assert.Same(original, PhoneLink.TempDirProvider);
        Assert.Equal(Path.Combine(TestHome.Root, "phone-sync"), PhoneLink.TempDirProvider());
    }

    /// <summary>落点不在真实 %TEMP% 的默认子目录下(测试窗口内不该在那里出现新目录)。</summary>
    [Fact]
    public void The_isolated_landing_zone_is_not_the_original_temp_directory()
    {
        string zip = PhoneLink.TempZip();
        string defaultDir = Path.Combine(Path.GetTempPath(), "tipcl-phone-sync");

        Assert.NotEqual(defaultDir, Path.GetDirectoryName(zip));
        Assert.False(Path.GetDirectoryName(zip).StartsWith(defaultDir, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 结构钉子(读源码,不执行生产入口):默认值就是原来那一句、TempZip 走提供者,
    /// 所以生产行为与改动前一致(Windows 上没有任何行为变化)。
    /// </summary>
    [Fact]
    public void Temp_zip_uses_the_provider_and_the_default_is_the_old_path()
    {
        string source = File.ReadAllText(RepoFile("src", "TipCL.Core", "Launcher", "Sync", "PhoneLink.cs"));

        Assert.Contains("internal static Func<string> TempDirProvider = DefaultTempDir;", source);
        Assert.Contains("string dir = (TempDirProvider ?? DefaultTempDir)();", source);
        Assert.Contains("return Path.Combine(Path.GetTempPath(), \"tipcl-phone-sync\");", source);
    }

    /// <summary>从输出目录往上找到仓库根,再拼出源文件路径(只在仓库内跑测试)。</summary>
    private static string RepoFile(params string[] relative)
    {
        DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string[] parts = new string[relative.Length + 1];
            parts[0] = dir.FullName;
            Array.Copy(relative, 0, parts, 1, relative.Length);
            string candidate = Path.Combine(parts);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("没找到仓库里的源文件: " + string.Join("/", relative));
    }
}
