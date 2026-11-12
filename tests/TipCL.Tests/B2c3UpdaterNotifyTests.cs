using System;
using System.IO;
using System.Text.RegularExpressions;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2c3 第 1 项:Updater 在非 Windows 上「只提示、不自替换」。
///
/// 源码事实(读过 Updater.cs):<c>Check</c> 拉更新清单返回一个 PyDict;
/// <c>Download</c> 把更新包下到 &lt;ROOT&gt;/cache 并校验 sha256;
/// <c>ApplyStaged</c> 起 powershell.exe 覆盖正在运行的 exe(Windows 专属)。GOAL 的裁决是
/// mac 上只提示新版本 + 打开下载页,不做自替换。
///
/// 红线:测试**绝不调** <c>Updater.Check</c> / <c>Download</c> / <c>ApplyStaged</c>
/// (前两个会联网、后一个会起 powershell),所以这里只测新抽出来的可注入纯函数,
/// 生产调用点用「读源码」的结构钉子钉住。
/// Windows 钉子:<c>SelfUpdateSupported("windows")</c> 为真 → 两个方法里的守卫是空操作,
/// 也就是说 Windows 行为与改动前逐字相同(唯一被授权的增量是 Check 多一个键)。
/// </summary>
public class B2c3UpdaterNotifyTests
{
    /// <summary>非 Windows 上抛出的那句文案(源码里用 \uXXXX 转义写的,测试里同样转义,避免抄错全角标点)。</summary>
    private const string NotifyOnlyMessage =
        "\u5f53\u524d\u5e73\u53f0\u4e0d\u652f\u6301\u539f\u5730\u81ea\u66f4\u65b0\uFF0C\u8bf7\u5230\u4e0b\u8f7d\u9875\u4e0b\u8f7d\u65b0\u7248\u672c\u5b89\u88c5";

    // ------------------------------------------------------------------ SelfUpdateSupported

    /// <summary>Windows 钉子:只有 "windows" 为真 —— 这正是改动前「下载 + 原地替换」唯一可用的平台。</summary>
    [Fact]
    public void Windows_supports_self_update()
    {
        Assert.True(Updater.SelfUpdateSupported("windows"));
    }

    /// <summary>非 Windows(含大小写不同、拼写相近的值)一律不支持。</summary>
    [Theory]
    [InlineData("osx")]
    [InlineData("linux")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Windows")]
    [InlineData("WINDOWS")]
    [InlineData("win")]
    [InlineData("macos")]
    [InlineData("darwin")]
    public void Only_the_exact_windows_key_supports_self_update(string? os)
    {
        Assert.False(Updater.SelfUpdateSupported(os));
    }

    // ------------------------------------------------------------------ EnsureSelfUpdateSupported

    /// <summary>Windows 钉子上守卫不抛:两个方法在 Windows 上的第一条语句是空操作。</summary>
    [Fact]
    public void The_guard_does_not_throw_on_windows()
    {
        Updater.EnsureSelfUpdateSupported("windows");
    }

    /// <summary>非 Windows 上抛 RuntimeError,文案逐字等于源码里那一句。</summary>
    [Theory]
    [InlineData("osx")]
    [InlineData("linux")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Windows")]
    public void The_guard_throws_RuntimeError_off_windows(string? os)
    {
        RuntimeError error = Assert.Throws<RuntimeError>(() => Updater.EnsureSelfUpdateSupported(os));

        Assert.Equal(NotifyOnlyMessage, error.Message);
    }

    /// <summary>文案本身:提到下载页、是中文、不需要含任何命令或 URL。</summary>
    [Fact]
    public void The_message_points_at_the_download_page_and_names_no_url()
    {
        RuntimeError error = Assert.Throws<RuntimeError>(() => Updater.EnsureSelfUpdateSupported("osx"));

        Assert.Contains("\u4e0b\u8f7d\u9875", error.Message, StringComparison.Ordinal);
        Assert.Contains("\u4e0d\u652f\u6301", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("http", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("powershell", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 本机(Windows)上守卫必须是空操作,否则说明 Windows 路径被改动了。
    /// 这条用例在两条通道里都按 Utils.OS_NAME 取实走的分支,不留死分支。
    /// </summary>
    [Fact]
    public void The_guard_matches_the_platform_this_test_runs_on()
    {
        if (Utils.OS_NAME == "windows")
        {
            Assert.True(Updater.SelfUpdateSupported(Utils.OS_NAME));
            Updater.EnsureSelfUpdateSupported(Utils.OS_NAME);
        }
        else
        {
            Assert.False(Updater.SelfUpdateSupported(Utils.OS_NAME));
            Assert.Throws<RuntimeError>(() => Updater.EnsureSelfUpdateSupported(Utils.OS_NAME));
        }
    }

    // ------------------------------------------------------------------ 源码结构钉子

    /// <summary>
    /// Download 与 ApplyStaged 的第一条语句都是守卫(各一处)。
    /// 依据:任务书要求「两个方法在方法体最顶上就调用守卫」;这里用源码文本核对。
    /// </summary>
    [Fact]
    public void Download_and_ApplyStaged_are_guarded_first()
    {
        string source = RepoSource("src", "TipCL.Core", "Launcher", "Updater.cs");

        Assert.Equal(2, CountOccurrences(source, "EnsureSelfUpdateSupported(Utils.OS_NAME);"));
        Assert.Matches(
            new Regex(@"public static void ApplyStaged\(string package, bool restart\)\s*\{\s*EnsureSelfUpdateSupported\(Utils\.OS_NAME\);"),
            source);
        Assert.Matches(
            new Regex(@"public static string Download\(PyDict info, DownloadManager dm = null\)\s*\{\s*EnsureSelfUpdateSupported\(Utils\.OS_NAME\);"),
            source);
    }

    /// <summary>
    /// BuildApplyScript 一个字节没动:它仍然只有那段 PowerShell 文本,里面没有平台守卫
    /// (Ported\UpdaterApplyTests.cs 用它做逐字断言,必须继续成立)。
    /// </summary>
    [Fact]
    public void BuildApplyScript_is_untouched()
    {
        string source = RepoSource("src", "TipCL.Core", "Launcher", "Updater.cs");
        int start = source.IndexOf("public static string BuildApplyScript(", StringComparison.Ordinal);
        int end = source.IndexOf("public static void ApplyStaged(", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "没找到 BuildApplyScript / ApplyStaged 的方法边界");

        string body = source.Substring(start, end - start);

        Assert.Contains("$ErrorActionPreference='SilentlyContinue';", body, StringComparison.Ordinal);
        Assert.Contains("Wait-Process -Id ", body, StringComparison.Ordinal);
        Assert.Contains("Start-Process -FilePath ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureSelfUpdateSupported", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SelfUpdateSupported", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Check 的两个 return 各多一个键 "can_self_update",取值就是 SelfUpdateSupported(Utils.OS_NAME)。
    /// 这是本包唯一被授权的 Windows 可见改动(一个增量键,Windows 上为 true)。
    /// </summary>
    [Fact]
    public void Check_reports_can_self_update_in_both_returns()
    {
        string source = RepoSource("src", "TipCL.Core", "Launcher", "Updater.cs");

        Assert.Equal(2, CountOccurrences(source, "\"can_self_update\", SelfUpdateSupported(Utils.OS_NAME)"));
        Assert.Equal(2, CountOccurrences(source, "return PyDict.Of("));
    }

    /// <summary>
    /// Check 返回的键清单(读源码逐个人工核对,PROGRESS.md 里也抄了一份):
    /// 失败分支 8 个键 + can_self_update = 9;成功分支多一个 sha256 = 10。
    /// 键名全部是 ASCII 小写加下划线,别的平台差异没有偷偷塞进这个字典。
    /// </summary>
    [Fact]
    public void Check_key_names_are_stable()
    {
        string source = RepoSource("src", "TipCL.Core", "Launcher", "Updater.cs");
        int start = source.IndexOf("public static PyDict Check(", StringComparison.Ordinal);
        int end = source.IndexOf("public static string BuildApplyScript(", StringComparison.Ordinal);
        string body = source.Substring(start, end - start);

        foreach (string key in new[] { "ok", "current", "latest", "has_update", "can_self_update", "message", "notes", "url" })
        {
            Assert.Contains("\"" + key + "\"", body, StringComparison.Ordinal);
        }

        Assert.Contains("\"sha256\"", body, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(body, "\"can_self_update\""));
        Assert.DoesNotContain("\"can_self_update\"", RepoSource("src", "TipCL.Core", "Launcher", "Shortcut.cs"), StringComparison.Ordinal);
    }

    /// <summary>确定一边:只在 Updater.cs 里出现这一个平台分支入口,没有第二套判定。</summary>
    [Fact]
    public void There_is_exactly_one_platform_predicate()
    {
        string source = RepoSource("src", "TipCL.Core", "Launcher", "Updater.cs");

        Assert.Equal(1, CountOccurrences(source, "internal static bool SelfUpdateSupported(string os)"));
        Assert.Equal(1, CountOccurrences(source, "internal static void EnsureSelfUpdateSupported(string os)"));
        Assert.Equal(1, CountOccurrences(source, "return os == \"windows\";"));
    }

    // ------------------------------------------------------------------ 小工具

    private static int CountOccurrences(string text, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    /// <summary>从输出目录往上找到仓库根,读出源文件正文(只在仓库内跑测试;路径与内容都不出仓库)。</summary>
    private static string RepoSource(params string[] relative)
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
                return File.ReadAllText(candidate);
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("没找到仓库里的源文件: " + string.Join("/", relative));
    }
}
