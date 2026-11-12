using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TipCL.Core.Launcher.Sync;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2c2 第 3 项:adb 路径。
///
/// <c>PhoneClient.FindAdb()</c> 原来把 <c>adb.exe</c> 写死在 6 类候选里(配置项 → 程序目录 → 数据根 →
/// ANDROID_HOME / ANDROID_SDK_ROOT → %LOCALAPPDATA%\Android\Sdk → PATH 逐目录 adb.exe 与 adb)。
/// 现在候选列表抽成纯函数 <c>AdbCandidates</c>(os、各环境来源、PATH 分隔符全部注入),
/// Windows 的候选与顺序**逐项不变**;非 Windows 换成不带 .exe 的候选并补上 mac 的两条默认位置。
///
/// 期望值来源:Windows 的 6 类候选 = **读过的源码行为**(改动前那个函数体);
/// mac 的 ~/Library/Android/sdk 与两个 Homebrew 位置 = **任务书裁决给出的默认位置,未经真机验证**。
///
/// 用例不联网、不起进程、不读真实环境变量、不调用无参的 <c>FindAdb()</c>(红线:它会读 Config 与真实环境)。
/// 用到的真实文件都在 <see cref="TestHome.Root"/> 之下。
/// </summary>
public class B2c2AdbPathTests
{
    private const string Configured = "C:\\custom\\adb";
    private const string BaseDir = "C:\\app";
    private const string DataRoot = "C:\\data\\TipCL";
    private const string AndroidHome = "C:\\android-home";
    private const string AndroidSdkRoot = "C:\\android-sdk-root";
    private const string HomeDir = "C:\\Users\\tester";
    private const string LocalAppData = "C:\\Users\\tester\\AppData\\Local";

    /// <summary>PATH 里混了空项与前后带空格的项:原实现会跳过空项、对非空项 Trim。</summary>
    private static readonly string[] PathSegments = new string[] { "C:\\tools", "", "  C:\\android-sdk\\platform-tools  " };
    private static readonly string[] CleanPathDirs = new string[] { "C:\\tools", "C:\\android-sdk\\platform-tools" };

    private static string PathValue()
    {
        return string.Join(new string(Path.PathSeparator, 1), PathSegments);
    }

    private static Func<string, string> Env()
    {
        return delegate(string name)
        {
            if (name == "ANDROID_HOME")
            {
                return AndroidHome;
            }
            if (name == "ANDROID_SDK_ROOT")
            {
                return AndroidSdkRoot;
            }
            return null;
        };
    }

    private static List<string> Candidates(string os, string configured = Configured, string dataRoot = DataRoot, Func<string, string> getEnv = null)
    {
        return PhoneClient.AdbCandidates(
            os,
            configured,
            BaseDir,
            dataRoot,
            getEnv ?? Env(),
            HomeDir,
            LocalAppData,
            PathValue(),
            Path.PathSeparator);
    }

    private static List<string> ExpectedWindows()
    {
        List<string> e = new List<string>();
        e.Add(Configured);
        e.Add(Path.Combine(BaseDir, "adb.exe"));
        e.Add(Path.Combine(DataRoot, "adb.exe"));
        e.Add(Path.Combine(AndroidHome, "platform-tools", "adb.exe"));
        e.Add(Path.Combine(AndroidSdkRoot, "platform-tools", "adb.exe"));
        e.Add(Path.Combine(LocalAppData, "Android", "Sdk", "platform-tools", "adb.exe"));
        foreach (string dir in CleanPathDirs)
        {
            e.Add(Path.Combine(dir, "adb.exe"));
            e.Add(Path.Combine(dir, "adb"));
        }
        return e;
    }

    private static List<string> ExpectedMac()
    {
        List<string> e = new List<string>();
        e.Add(Configured);
        e.Add(Path.Combine(BaseDir, "adb"));
        e.Add(Path.Combine(DataRoot, "adb"));
        e.Add(Path.Combine(AndroidHome, "platform-tools", "adb"));
        e.Add(Path.Combine(AndroidSdkRoot, "platform-tools", "adb"));
        e.Add(Path.Combine(HomeDir, "Library", "Android", "sdk", "platform-tools", "adb"));
        e.Add("/opt/homebrew/bin/adb");
        e.Add("/usr/local/bin/adb");
        foreach (string dir in CleanPathDirs)
        {
            e.Add(Path.Combine(dir, "adb"));
        }
        return e;
    }

    // ---------- Windows:逐项不变 ----------

    /// <summary>Windows 钉子:6 类候选与顺序(含每个 PATH 目录先 adb.exe、后 adb)与改动前逐项相同。</summary>
    [Fact]
    public void Windows_candidates_match_the_old_order_exactly()
    {
        Assert.Equal(ExpectedWindows(), Candidates("windows"));
    }

    /// <summary>配置项为空 → 只是少第一个候选,其余原样。</summary>
    [Fact]
    public void Windows_without_a_configured_path_drops_only_that_candidate()
    {
        Assert.Equal(ExpectedWindows().Skip(1).ToList(), Candidates("windows", ""));
    }

    /// <summary>配置项是 null(没有这个键)→ 同上。</summary>
    [Fact]
    public void Windows_with_a_null_configured_path_drops_only_that_candidate()
    {
        Assert.Equal(ExpectedWindows().Skip(1).ToList(), Candidates("windows", null));
    }

    /// <summary>数据根取不到(改动前 <c>Utils.ResolveRoot()</c> 抛异常被 catch)→ 只是少那个候选。</summary>
    [Fact]
    public void Windows_without_a_data_root_drops_only_that_candidate()
    {
        List<string> expected = ExpectedWindows();
        expected.RemoveAt(2);

        Assert.Equal(expected, Candidates("windows", Configured, dataRoot: null));
        Assert.Equal(expected, Candidates("windows", Configured, dataRoot: ""));
    }

    /// <summary>ANDROID_HOME / ANDROID_SDK_ROOT 没设 → 只是少那两个候选。</summary>
    [Fact]
    public void Windows_without_android_env_values_drops_only_those_candidates()
    {
        List<string> expected = ExpectedWindows();
        expected.RemoveAt(4);
        expected.RemoveAt(3);

        Assert.Equal(expected, Candidates("windows", Configured, DataRoot, delegate { return null; }));
        Assert.Equal(expected, Candidates("windows", Configured, DataRoot, delegate { return ""; }));
    }

    /// <summary>只有一个环境变量设了 → 只多一条候选(另一个没设,不产生候选)。</summary>
    [Theory]
    [InlineData("ANDROID_HOME")]
    [InlineData("ANDROID_SDK_ROOT")]
    public void Windows_honours_either_android_env_variable(string name)
    {
        List<string> got = Candidates("windows", Configured, DataRoot, delegate(string n) { return n == name ? "C:\\only" : null; });

        List<string> expected = new List<string>();
        expected.Add(Configured);
        expected.Add(Path.Combine(BaseDir, "adb.exe"));
        expected.Add(Path.Combine(DataRoot, "adb.exe"));
        expected.Add(Path.Combine("C:\\only", "platform-tools", "adb.exe"));
        expected.Add(Path.Combine(LocalAppData, "Android", "Sdk", "platform-tools", "adb.exe"));
        foreach (string dir in CleanPathDirs)
        {
            expected.Add(Path.Combine(dir, "adb.exe"));
            expected.Add(Path.Combine(dir, "adb"));
        }
        Assert.Equal(expected, got);
    }

    /// <summary>
    /// %LOCALAPPDATA% 那条候选**无条件**加上(改动前就是无条件 <c>c.Add(...)</c>);
    /// 传空串时拼出来的仍是同一个相对路径,与原实现的 Path.Combine 语义一致。
    /// </summary>
    [Fact]
    public void Windows_always_keeps_the_local_appdata_candidate()
    {
        List<string> got = PhoneClient.AdbCandidates(
            "windows", null, BaseDir, null, null, HomeDir, "", "", Path.PathSeparator);

        Assert.Contains(Path.Combine("", "Android", "Sdk", "platform-tools", "adb.exe"), got);
    }

    /// <summary>PATH 的空项被跳过、前后空格被 Trim(改动前就是 <c>dir.Trim().Length > 0</c>)。</summary>
    [Fact]
    public void Windows_path_entries_are_trimmed_and_empty_ones_skipped()
    {
        List<string> got = Candidates("windows");

        Assert.DoesNotContain("adb.exe", got);
        Assert.Contains(Path.Combine("C:\\tools", "adb.exe"), got);
        Assert.Contains(Path.Combine("C:\\android-sdk\\platform-tools", "adb"), got);
        Assert.DoesNotContain(got, delegate(string p) { return p.StartsWith("  ", StringComparison.Ordinal); });
    }

    /// <summary>
    /// PATH 分隔符是注入的:用 '|' 传进去就按 '|' 拆(证明没有写死 Path.PathSeparator)。
    /// 这里不用 ':' —— 盘符本身带冒号,Windows 上没法当分隔符做这个断言。
    /// </summary>
    [Fact]
    public void The_path_separator_is_injected()
    {
        List<string> got = PhoneClient.AdbCandidates(
            "windows", null, BaseDir, null, null, HomeDir, LocalAppData, "C:\\one|C:\\two", '|');

        Assert.Contains(Path.Combine("C:\\one", "adb.exe"), got);
        Assert.Contains(Path.Combine("C:\\two", "adb.exe"), got);
        Assert.Contains(Path.Combine("C:\\two", "adb"), got);
    }

    // ---------- 非 Windows ----------

    /// <summary>
    /// mac 钉子:全用无后缀的 <c>adb</c>,并补上 ~/Library/Android/sdk 与两个 Homebrew 位置
    /// (从访达启动的应用拿不到 shell 的 PATH,所以显式列出 —— 任务书裁决,未经真机验证)。
    /// </summary>
    [Fact]
    public void Mac_candidates_use_the_bare_adb_name_and_add_the_two_homebrew_paths()
    {
        Assert.Equal(ExpectedMac(), Candidates("osx"));
    }

    /// <summary>主目录读不到(理论上不会)→ 只少 Library 那一条,两个 Homebrew 绝对路径仍在。</summary>
    [Fact]
    public void Mac_skips_only_the_library_candidate_when_the_home_directory_is_unknown()
    {
        List<string> expected = ExpectedMac();
        expected.RemoveAt(5);

        List<string> got = PhoneClient.AdbCandidates(
            "osx", Configured, BaseDir, DataRoot, Env(), "", LocalAppData, PathValue(), Path.PathSeparator);

        Assert.Equal(expected, got);
        Assert.Contains("/opt/homebrew/bin/adb", got);
        Assert.Contains("/usr/local/bin/adb", got);
    }

    /// <summary>linux 本包不新增功能:不补 mac 那三条,其余与非 Windows 一致。</summary>
    [Fact]
    public void Linux_has_no_mac_only_candidates()
    {
        List<string> got = Candidates("linux");

        Assert.Equal(Candidates("osx").Take(5).Concat(CleanPathDirs.Select(delegate(string d) { return Path.Combine(d, "adb"); })).ToList(), got);
        Assert.DoesNotContain("/opt/homebrew/bin/adb", got);
        Assert.DoesNotContain("/usr/local/bin/adb", got);
        Assert.DoesNotContain(got, delegate(string p) { return p.Contains("Library"); });
    }

    /// <summary>非 Windows 一条 .exe 都不探测,也不看 LocalApplicationData。</summary>
    [Theory]
    [InlineData("osx")]
    [InlineData("linux")]
    public void Non_windows_never_offers_an_exe_or_a_local_appdata_candidate(string os)
    {
        List<string> got = Candidates(os);

        Assert.DoesNotContain(got, delegate(string p) { return p.EndsWith("adb.exe", StringComparison.Ordinal); });
        Assert.DoesNotContain(got, delegate(string p) { return p.Contains("AppData"); });
        Assert.DoesNotContain(got, delegate(string p) { return p.Contains("Android\\Sdk"); });
    }

    // ---------- FindAdb(注入存在性判断) ----------

    /// <summary>配置项存在时排在最前(改动前它就是第一个候选)。</summary>
    [Fact]
    public void The_configured_path_wins_when_every_candidate_exists()
    {
        string got = PhoneClient.FindAdb(
            "windows", Configured, BaseDir, DataRoot, Env(), HomeDir, LocalAppData, PathValue(), Path.PathSeparator,
            delegate { return true; });

        Assert.Equal(Configured, got);
    }

    /// <summary>只有某个候选存在时返回它 —— 分别验 Windows 与 mac 的第三、第四个候选。</summary>
    [Fact]
    public void Find_adb_returns_the_first_existing_candidate()
    {
        string onlyRoot = Path.Combine(DataRoot, "adb.exe");
        Assert.Equal(
            onlyRoot,
            PhoneClient.FindAdb("windows", null, BaseDir, DataRoot, null, HomeDir, LocalAppData, "", Path.PathSeparator, delegate(string p) { return p == onlyRoot; }));

        string onlySdk = Path.Combine(AndroidSdkRoot, "platform-tools", "adb");
        Assert.Equal(
            onlySdk,
            PhoneClient.FindAdb("osx", null, BaseDir, DataRoot, Env(), HomeDir, LocalAppData, "", Path.PathSeparator, delegate(string p) { return p == onlySdk; }));

        string onlyHomebrew = "/usr/local/bin/adb";
        Assert.Equal(
            onlyHomebrew,
            PhoneClient.FindAdb("osx", null, BaseDir, null, null, null, null, "", Path.PathSeparator, delegate(string p) { return p == onlyHomebrew; }));
    }

    /// <summary>一个都不存在 → null(调用方抛 InvalidOperationException)。</summary>
    [Fact]
    public void Find_adb_returns_null_when_nothing_exists()
    {
        Assert.Null(PhoneClient.FindAdb("windows", Configured, BaseDir, DataRoot, Env(), HomeDir, LocalAppData, PathValue(), Path.PathSeparator, delegate { return false; }));
        Assert.Null(PhoneClient.FindAdb("osx", Configured, BaseDir, DataRoot, Env(), HomeDir, LocalAppData, PathValue(), Path.PathSeparator, delegate { return false; }));
    }

    /// <summary>存在性判断收到的是**候选列表按顺序**的每一个路径(注入的委托决定顺序语义)。</summary>
    [Fact]
    public void Find_adb_hands_the_candidates_to_the_exists_delegate_in_order()
    {
        List<string> seen = new List<string>();

        PhoneClient.FindAdb(
            "osx", Configured, BaseDir, DataRoot, Env(), HomeDir, LocalAppData, PathValue(), Path.PathSeparator,
            delegate(string p) { seen.Add(p); return false; });

        Assert.Equal(Candidates("osx"), seen);
    }

    /// <summary>环境变量名按 ANDROID_HOME → ANDROID_SDK_ROOT 的顺序问(改动前的数组顺序)。</summary>
    [Fact]
    public void Find_adb_reads_the_android_environment_variables_in_order()
    {
        List<string> asked = new List<string>();

        PhoneClient.AdbCandidates(
            "windows", null, BaseDir, DataRoot,
            delegate(string name) { asked.Add(name); return null; },
            HomeDir, LocalAppData, "", Path.PathSeparator);

        Assert.Equal(new string[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" }, asked);
    }

    /// <summary>不注入存在性判断时用生产的 <c>File.Exists</c>:TestHome.Root 下真放一个 adb 就能找到。</summary>
    [Theory]
    [InlineData("windows", "adb.exe")]
    [InlineData("linux", "adb")]
    public void Find_adb_with_the_real_file_check_finds_a_real_adb_under_the_test_home(string os, string fileName)
    {
        string dir = Path.Combine(TestHome.Root, "b2c2-adb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string adb = Path.Combine(dir, fileName);
        File.WriteAllBytes(adb, new byte[] { 0 });

        string got = PhoneClient.FindAdb(os, null, dir, null, null, null, LocalAppData, "", Path.PathSeparator, null);

        Assert.Equal(adb, got);
        Assert.True(File.Exists(got));
    }

    /// <summary>
    /// Windows 分支忠实照搬了原实现的无条件 <c>Path.Combine(LocalApplicationData, ...)</c>:
    /// 传 null 进来同样抛 ArgumentNullException(生产中 <c>GetFolderPath</c> 永远不会给 null;
    /// 非 Windows 分支根本不碰这个值,所以不抛)。
    /// </summary>
    [Fact]
    public void Windows_with_a_null_local_appdata_throws_like_the_original_did()
    {
        Assert.Throws<ArgumentNullException>(delegate
        {
            PhoneClient.AdbCandidates("windows", null, BaseDir, null, null, HomeDir, null, "", Path.PathSeparator);
        });

        List<string> linux = PhoneClient.AdbCandidates("linux", null, BaseDir, null, null, HomeDir, null, "", Path.PathSeparator);
        Assert.Contains(Path.Combine(BaseDir, "adb"), linux);
    }

    // ---------- 报错文案 ----------

    /// <summary>改动前的 Windows 原文(逐字,转义抄自源码;生产调用点传的就是这一句,见下一条用例)。</summary>
    private const string OriginalWindowsAdbMessage =
        "\u6CA1\u627E\u5230 adb.exe\u3002\u628A Android platform-tools \u91CC\u7684 adb.exe\uFF08\u8FDE\u540C\u4E24\u4E2A AdbWin*.dll\uFF09\u653E\u5230 TipCL \u540C\u76EE\u5F55\u5373\u53EF";

    /// <summary>非 Windows 换掉的新文案(源码里就是这一句,纯函数按 os 选)。</summary>
    private const string NonWindowsAdbMessage =
        "\u6CA1\u627E\u5230 adb\u3002\u53EF\u5728\u8BBE\u7F6E\u91CC\u586B\u5199 adb \u7684\u5B8C\u6574\u8DEF\u5F84\uFF0C\u6216\u5B89\u88C5 Android platform-tools \u540E\u91CD\u8BD5";

    /// <summary>Windows 上原样返回调用点传进来的那句话(一个字符都没改)。</summary>
    [Fact]
    public void Windows_missing_message_is_the_original_text_verbatim()
    {
        string got = PhoneClient.AdbMissingMessage("windows", OriginalWindowsAdbMessage);

        Assert.Equal(OriginalWindowsAdbMessage, got);
        Assert.Contains("adb.exe", got);
        Assert.Contains("AdbWin*.dll", got);
        Assert.NotEqual(NonWindowsAdbMessage, got);
    }

    /// <summary>非 Windows 的文案明确不提 adb.exe 与 AdbWin*.dll,并给出「填路径 / 装 platform-tools」两条出路。</summary>
    [Theory]
    [InlineData("osx")]
    [InlineData("linux")]
    [InlineData("")]
    [InlineData("Windows")]
    public void Non_windows_missing_message_drops_the_exe_and_dll_wording(string os)
    {
        string got = PhoneClient.AdbMissingMessage(os, OriginalWindowsAdbMessage);

        Assert.Equal(NonWindowsAdbMessage, got);
        Assert.DoesNotContain("adb.exe", got);
        Assert.DoesNotContain("AdbWin", got);
        Assert.DoesNotContain(".dll", got);
        Assert.Contains("adb", got);
        Assert.Contains("platform-tools", got);
        Assert.Contains("\u8BBE\u7F6E", got);
    }

    // ---------- 结构钉子:生产调用点确实接上了新接缝(读源码,不执行生产入口) ----------

    /// <summary>
    /// 生产调用点(ConnectUsb)那句 Windows 文案必须**逐字**等于改动前的原文,
    /// 同时又经过了可注入 os 的 <c>AdbMissingMessage</c>。直接读源码文本核对(结构性检查)。
    /// </summary>
    [Fact]
    public void The_call_site_passes_the_original_windows_text_through_the_os_switch()
    {
        string source = File.ReadAllText(RepoFile("src", "TipCL.Core", "Launcher", "Sync", "PhoneClient.cs"));
        Match match = Regex.Match(source, "AdbMissingMessage\\(Utils\\.OS_NAME, \"(?<text>[^\"]*)\"\\)");

        Assert.True(match.Success, "没在 PhoneClient.cs 里找到 ConnectUsb 的报错调用点");
        Assert.Equal(OriginalWindowsAdbMessage, match.Groups["text"].Value);
    }

    /// <summary>生产入口 <c>FindAdb()</c> 只做「取真实值 + 转调可注入重载」,候选逻辑不在它里面。</summary>
    [Fact]
    public void The_production_entry_point_delegates_to_the_injectable_overload()
    {
        string source = File.ReadAllText(RepoFile("src", "TipCL.Core", "Launcher", "Sync", "PhoneClient.cs"));
        int start = source.IndexOf("public static string FindAdb()", StringComparison.Ordinal);
        Assert.True(start >= 0, "没找到 public static string FindAdb()");

        string body = source.Substring(start, 900);
        Assert.Contains("Utils.OS_NAME", body);
        Assert.Contains("Config.CONFIG.Get(\"adb_path\"", body);
        Assert.Contains("Utils.ResolveRoot()", body);
        Assert.Contains("AppDomain.CurrentDomain.BaseDirectory", body);
        Assert.Contains("Path.PathSeparator", body);
        Assert.Contains("File.Exists(path)", body);
        Assert.DoesNotContain("adb.exe", body);
    }

    /// <summary>从输出目录往上找到仓库根,再拼出源文件路径(只在仓库内跑测试)。</summary>
    private static string RepoFile(params string[] relative)
    {
        DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(new List<string> { dir.FullName }.Concat(relative).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("没找到仓库里的源文件: " + string.Join("/", relative));
    }
}
