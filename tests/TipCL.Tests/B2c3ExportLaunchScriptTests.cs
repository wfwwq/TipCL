using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2c3 第 3 项:导出启动脚本在 mac 上出 .command(非 Windows 出 POSIX sh 正文)。
///
/// 源码事实(读过 VersionOps.cs 201-232 与 BackendApi.cs 2949-2967):
/// <list type="bullet">
/// <item>改动前 <c>export_launch_script</c> 只有一条路:默认文件名写死 ".bat",
/// 内容走 <c>VersionOps.ExportLaunchBat</c>(@echo off / chcp 65001 / cd /d ... / pause / CRLF);</item>
/// <item>同一个方法第 2958 行把**未合并继承链**的版本 JSON 交给 Java.ResolveLaunchJava
/// (B2e 的 Lead 裁决:Forge 这类 inheritsFrom 的版本会因此挑错架构的 Java);</item>
/// <item>JavaRuntimeFiles.SetExecutable 在非 Windows 上设 0755、Windows 上是空操作。</item>
/// </list>
///
/// 本包:非 Windows 分支走 PosixLaunchScript + 按 os 选扩展名;Windows 分支仍然原样调
/// <c>ExportLaunchBat</c>(函数本身一个字节没改,这里用「同一份 cmd/cwd 走两条路字节全等」钉住)。
/// 用例不联网、不起进程、落盘文件都在 <see cref="TestHome.Root"/> 之下。
/// </summary>
public class B2c3ExportLaunchScriptTests
{
    // ------------------------------------------------------------------ ExportScriptExtension

    /// <summary>扩展名映射(任务书给定):windows → .bat,osx → .command,其余 → .sh。</summary>
    [Theory]
    [InlineData("windows", ".bat")]
    [InlineData("osx", ".command")]
    [InlineData("linux", ".sh")]
    [InlineData("", ".sh")]
    [InlineData(null, ".sh")]
    [InlineData("Windows", ".sh")]
    [InlineData("darwin", ".sh")]
    [InlineData("macos", ".sh")]
    public void ExportScriptExtension_maps_the_platform(string? os, string expected)
    {
        Assert.Equal(expected, VersionOps.ExportScriptExtension(os));
    }

    /// <summary>Windows 钉子上扩展名与改动前写死的字面量逐字相同。</summary>
    [Fact]
    public void The_windows_extension_equals_the_literal_used_before()
    {
        Assert.Equal(".bat", VersionOps.ExportScriptExtension("windows"));
    }

    /// <summary>mac 的扩展名与桌面快捷方式的选择一致(Shortcut 的 mac 分支也写 .command)。</summary>
    [Fact]
    public void The_mac_extension_matches_the_shortcut_choice()
    {
        Assert.Equal(".command", VersionOps.ExportScriptExtension("osx"));
        Assert.Contains("\".command\"", RepoSource("src", "TipCL.Core", "Launcher", "Shortcut.cs"), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ PosixLaunchScript

    /// <summary>真实形态的命令行:JVM 参数 / classpath(冒号分隔)/ 中文路径都逐字对照。</summary>
    [Fact]
    public void Posix_script_quotes_every_word_and_keeps_the_classpath()
    {
        string cwd = "/Users/me/.tipcl/instances/我的 实例/.minecraft";
        List<object> cmd = new List<object>
        {
            "/Users/me/.tipcl/java/adoptium-17-x64/Contents/Home/bin/java",
            "-XstartOnFirstThread",
            "-Djava.library.path=/Users/me/.tipcl/instances/我的 实例/natives",
            "-cp",
            "/a/b.jar:/c/中文.jar",
            "net.minecraft.client.main.Main",
            "--username",
            "张三"
        };

        string expected =
            "#!/bin/sh\n" +
            "cd '/Users/me/.tipcl/instances/我的 实例/.minecraft'\n" +
            "'/Users/me/.tipcl/java/adoptium-17-x64/Contents/Home/bin/java' '-XstartOnFirstThread' " +
            "'-Djava.library.path=/Users/me/.tipcl/instances/我的 实例/natives' '-cp' '/a/b.jar:/c/中文.jar' " +
            "'net.minecraft.client.main.Main' '--username' '张三'\n";

        Assert.Equal(expected, VersionOps.PosixLaunchScript(cmd, cwd));
        Assert.Contains("'/a/b.jar:/c/中文.jar'", VersionOps.PosixLaunchScript(cmd, cwd), StringComparison.Ordinal);
    }

    /// <summary>敌意参数逐字转义(与 Shortcut 用同一个 PosixShell 规则)。</summary>
    [Fact]
    public void Posix_script_escapes_hostile_values()
    {
        List<object> cmd = new List<object> { "/bin/sh", "-c", "a'; rm -rf ~; echo '" };

        string text = VersionOps.PosixLaunchScript(cmd, "/tmp/it's/目录");

        Assert.Equal(
            "#!/bin/sh\n" +
            "cd '/tmp/it'\\''s/目录'\n" +
            "'/bin/sh' '-c' 'a'\\''; rm -rf ~; echo '\\'''\n",
            text);
    }

    /// <summary>$、反引号、双引号、分号在单引号里都是字面量,原样保留。</summary>
    [Fact]
    public void Dollar_backticks_and_quotes_stay_literal()
    {
        List<object> cmd = new List<object> { "$HOME/x", "`id`", "say \"hi\"", "a;b", "a\\b" };

        string text = VersionOps.PosixLaunchScript(cmd, "/tmp");

        Assert.Contains("'$HOME/x' '`id`' 'say \"hi\"' 'a;b' 'a\\b'", text, StringComparison.Ordinal);
    }

    /// <summary>没有 cmd 的形态契约:没有 pause / chcp / exec,只有 LF,结尾一个换行。</summary>
    [Fact]
    public void Posix_script_has_no_cmd_isms_and_only_lf()
    {
        List<object> cmd = new List<object> { "/bin/echo", "hi" };

        string text = VersionOps.PosixLaunchScript(cmd, "/tmp/x");

        Assert.StartsWith("#!/bin/sh\n", text, StringComparison.Ordinal);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
        Assert.DoesNotContain("pause", text, StringComparison.Ordinal);
        Assert.DoesNotContain("chcp", text, StringComparison.Ordinal);
        Assert.DoesNotContain("@echo", text, StringComparison.Ordinal);
        Assert.DoesNotContain("exec ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("-XstartOnFirstThread", text, StringComparison.Ordinal);
        Assert.DoesNotContain("arch", text, StringComparison.Ordinal);
    }

    /// <summary>空命令表 / null 命令表:仍然是一份合法脚本(第三行为空行),不抛。</summary>
    [Fact]
    public void An_empty_or_null_command_list_still_produces_a_script()
    {
        string expected = "#!/bin/sh\ncd '/tmp/x'\n\n";

        Assert.Equal(expected, VersionOps.PosixLaunchScript(new List<object>(), "/tmp/x"));
        Assert.Equal(expected, VersionOps.PosixLaunchScript(null, "/tmp/x"));
    }

    /// <summary>
    /// cwd 为 null 时 `Py.Str` 给的是 "None"(CPython 同款语义),不是空串 ——
    /// 这一点与 Windows 分支的 `"cd /d \"" + Py.Str(cwd) + "\""` 完全一致(本包不另立一套),
    /// 所以这里断言的是「与 Windows 同源」而不是「空单引号」。
    /// </summary>
    [Fact]
    public void A_null_cwd_follows_Py_Str_exactly_like_the_windows_branch()
    {
        string text = VersionOps.PosixLaunchScript(new List<object> { "/bin/echo" }, null);

        Assert.Equal("None", Py.Str(null));
        Assert.Equal("#!/bin/sh\ncd 'None'\n'/bin/echo'\n", text);
        Assert.Contains("Py.Str(cwd)", RepoSource("src", "TipCL.Core", "Launcher", "VersionOps.cs"), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ ExportLaunchScript

    /// <summary>osx:写出 UTF-8 无 BOM、只有 LF、开头是 shebang,并把父目录建出来。</summary>
    [Fact]
    public void ExportLaunchScript_for_osx_writes_utf8_without_bom()
    {
        string dir = TempDir();
        string dest = PPath.Join(dir, "sub", "launch-myinst-1.20.1.command");
        List<object> cmd = new List<object> { "/usr/bin/java", "-cp", "a b.jar", "Main" };
        const string cwd = "/Users/me/.minecraft/我的 实例";

        string got = VersionOps.ExportLaunchScript(dest, cmd, cwd, "osx");
        string path = PPath.Abs(dest);

        Assert.True(File.Exists(path), "脚本没有落盘: " + path);
        Assert.Equal(new PPath(dest).ToString(), got);

        byte[] bytes = File.ReadAllBytes(path);
        byte[] expected = new UTF8Encoding(false).GetBytes(VersionOps.PosixLaunchScript(cmd, cwd));

        Assert.Equal(expected, bytes);
        Assert.Equal((byte)'#', bytes[0]);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal(-1, Array.IndexOf(bytes, (byte)13));
        Assert.StartsWith("#!/bin/sh\n", new UTF8Encoding(false).GetString(bytes), StringComparison.Ordinal);
    }

    /// <summary>linux(以及任何非 windows 取值)走同一份 POSIX 正文,只是扩展名约定不同。</summary>
    [Fact]
    public void ExportLaunchScript_for_linux_writes_the_same_posix_body()
    {
        string dir = TempDir();
        string a = PPath.Join(dir, "a.command");
        string b = PPath.Join(dir, "b.sh");
        List<object> cmd = new List<object> { "/bin/echo", "hi" };

        VersionOps.ExportLaunchScript(a, cmd, "/tmp", "osx");
        VersionOps.ExportLaunchScript(b, cmd, "/tmp", "plan9");

        Assert.Equal(File.ReadAllBytes(PPath.Abs(a)), File.ReadAllBytes(PPath.Abs(b)));
        Assert.Equal(new PPath(b).ToString(), VersionOps.ExportLaunchScript(b, cmd, "/tmp", "linux"));
    }

    /// <summary>
    /// Windows 钉子:ExportLaunchScript(...,"windows") 与直接调 ExportLaunchBat 的字节完全一样,
    /// 因为前者就是原样转调后者(Windows 行为一字不变)。
    /// </summary>
    [Fact]
    public void ExportLaunchScript_for_windows_is_byte_identical_to_ExportLaunchBat()
    {
        string dir = TempDir();
        string viaSwitch = PPath.Join(dir, "switch", "launch.bat");
        string direct = PPath.Join(dir, "direct", "launch.bat");
        List<object> cmd = new List<object> { @"C:\Program Files\Java\bin\java.exe", "-cp", @"C:\游戏\a b.jar", "Main" };
        const string cwd = @"C:\游戏\我的 实例\.minecraft";

        string gotSwitch = VersionOps.ExportLaunchScript(viaSwitch, cmd, cwd, "windows");
        string gotDirect = VersionOps.ExportLaunchBat(direct, cmd, cwd);

        Assert.Equal(File.ReadAllBytes(PPath.Abs(direct)), File.ReadAllBytes(PPath.Abs(viaSwitch)));
        Assert.Equal(new PPath(viaSwitch).ToString(), gotSwitch);
        Assert.Equal(new PPath(direct).ToString(), gotDirect);
        // 两边的内容都必须是原来那份 cmd 脚本(改动只在「谁来写」,不在「写什么」)
        string text = File.ReadAllText(PPath.Abs(viaSwitch));
        Assert.StartsWith("@echo off", text, StringComparison.Ordinal);
        Assert.Contains("chcp 65001 >nul", text, StringComparison.Ordinal);
        Assert.Contains("pause", text, StringComparison.Ordinal);
        Assert.Contains("\r\n", text, StringComparison.Ordinal);
    }

    /// <summary>Windows 分支不会顺手给 .bat 设可执行位(路径不同、也不该碰权限位)。</summary>
    [Fact]
    public void The_windows_branch_only_returns_the_bat_path()
    {
        string dir = TempDir();
        string dest = PPath.Join(dir, "x.bat");

        string got = VersionOps.ExportLaunchScript(dest, new List<object> { "cmd" }, @"C:\x", "windows");

        Assert.Equal(new PPath(dest).ToString(), got);
        Assert.True(File.Exists(PPath.Abs(dest)));
        if (!OperatingSystem.IsWindows())
        {
            // 本机是 Windows,这一支只是把「不碰权限位」写成断言留给真机。
            Assert.NotEqual(JavaRuntimeFiles.ModeForEntry(executable: true), File.GetUnixFileMode(PPath.Abs(dest)));
        }
    }

    // ------------------------------------------------------------------ ExportVersionJson

    /// <summary>Windows 钉子:原样返回同一个对象,loader 一次都不调(行为与改动前一致)。</summary>
    [Fact]
    public void ExportVersionJson_on_windows_returns_the_same_instance_and_never_loads()
    {
        PyDict child = ChildJson();
        bool called = false;

        PyDict got = VersionOps.ExportVersionJson(child, pid =>
        {
            called = true;
            return ParentJson();
        }, isWindows: true);

        Assert.Same(child, got);
        Assert.False(called, "Windows 上不许调 load_parent");
        Assert.Equal("1.20.1", Py.Str(got.Get("inheritsFrom")));
        Assert.Null(got.Get("assets"));
    }

    /// <summary>非 Windows:合并继承链,父版本的 libraries 与 assets 都进来了,子版本自己覆盖的键仍是子的。</summary>
    [Fact]
    public void ExportVersionJson_off_windows_merges_the_parent()
    {
        PyDict child = ChildJson();
        PyDict parent = ParentJson();
        List<object> parentLibs = (List<object>)parent.Get("libraries");
        string seen = "";

        PyDict got = VersionOps.ExportVersionJson(child, pid =>
        {
            seen = pid;
            return parent;
        }, isWindows: false);

        Assert.Equal("1.20.1", seen);
        Assert.Equal("1.20", Py.Str(got.Get("assets")));
        Assert.Equal("child.Main", Py.Str(got.Get("mainClass")));
        // Manifest.Merge 的语义是「父做底、子覆盖」:子版本自己的 id 是赢的那个。
        Assert.Equal("my-instance-1.20.1", Py.Str(got.Get("id")));
        Assert.False(got.ContainsKey("inheritsFrom"));

        // MergeLibraries:父的库在前、子的库在后(identity 相同的会被子覆盖,这里两个名字不同)
        List<object> libs = Assert.IsType<List<object>>(got.Get("libraries"));
        Assert.Equal(2, libs.Count);
        Assert.Same(parentLibs[0], libs[0]);
        Assert.Equal("com.example:parent-lib:1.0", Py.Str(((PyDict)libs[0]).Get("name")));
        Assert.Equal("com.example:child-lib:1.0", Py.Str(((PyDict)libs[1]).Get("name")));

        // 入参本身没被改(ResolveInherits 先做了一份拷贝)
        Assert.Equal("1.20.1", Py.Str(child.Get("inheritsFrom")));
        Assert.Equal(4, child.Count);
        Assert.True(child.ContainsKey("libraries"));
    }

    /// <summary>非 Windows 但没有 inheritsFrom:loader 不调,返回内容等价的拷贝。</summary>
    [Fact]
    public void ExportVersionJson_without_inheritance_only_copies()
    {
        PyDict child = PyDict.Of("id", "1.20.1", "mainClass", "child.Main");
        bool called = false;

        PyDict got = VersionOps.ExportVersionJson(child, pid =>
        {
            called = true;
            return ParentJson();
        }, isWindows: false);

        Assert.False(called);
        Assert.Equal("child.Main", Py.Str(got.Get("mainClass")));
        Assert.Equal(2, got.Count);
        Assert.Same(child.Get("mainClass"), got.Get("mainClass"));
    }

    /// <summary>父版本加载抛异常(例如磁盘上没有父版本 JSON):退回 childJson,导出不失败。</summary>
    [Fact]
    public void A_throwing_loader_falls_back_to_the_child()
    {
        PyDict child = ChildJson();

        PyDict got = VersionOps.ExportVersionJson(child, pid => throw new DownloadError("父版本坏了"), isWindows: false);

        Assert.Same(child, got);
    }

    /// <summary>loader 返回 null(父版本没装):ResolveInherits 抛 DownloadError,同样退回 childJson。</summary>
    [Fact]
    public void A_missing_parent_falls_back_to_the_child()
    {
        PyDict child = ChildJson();

        PyDict got = VersionOps.ExportVersionJson(child, pid => null, isWindows: false);

        Assert.Same(child, got);
        Assert.Equal("1.20.1", Py.Str(got.Get("inheritsFrom")));
    }

    /// <summary>继承链成环:也退回 childJson,不让导出卡死或抛出。</summary>
    [Fact]
    public void An_inheritance_cycle_falls_back_to_the_child()
    {
        PyDict child = PyDict.Of("inheritsFrom", "loop", "mainClass", "child.Main");

        PyDict got = VersionOps.ExportVersionJson(child, pid => PyDict.Of("inheritsFrom", "loop"), isWindows: false);

        Assert.Same(child, got);
    }

    /// <summary>
    /// childJson 为 null 时:Windows 分支原样返回 null;非 Windows 走 Manifest.ResolveInherits,
    /// 它对 null 的既有语义是「按空 dict 处理」(返回一个空的 PyDict),本包不改它。
    /// 生产调用点永远不传 null(实参表达式里有 ?? new PyDict())。
    /// </summary>
    [Fact]
    public void A_null_child_is_null_on_windows_and_an_empty_dict_off_windows()
    {
        Assert.Null(VersionOps.ExportVersionJson(null, pid => ParentJson(), isWindows: true));

        PyDict got = VersionOps.ExportVersionJson(null, pid => ParentJson(), isWindows: false);

        Assert.NotNull(got);
        Assert.Equal(0, got.Count);
    }

    // ------------------------------------------------------------------ 生产调用点(读源码)

    /// <summary>
    /// BackendApi.export_launch_script 的两处替换:第 2958 行走 ExportVersionJson,
    /// 第 2963 行走 ExportLaunchScript + ExportScriptExtension(默认文件名不再写死 .bat)。
    /// Windows 上的等价性:ExportVersionJson(..., isWindows: true) 原样返回同一个对象,
    /// ExportScriptExtension("windows") 就是 ".bat" 且 ExportLaunchScript 转调 ExportLaunchBat。
    /// </summary>
    [Fact]
    public void BackendApi_export_launch_script_uses_the_new_seams()
    {
        string source = RepoSource("src", "TipCL.Core", "BackendApi.cs");

        Assert.Contains(
            "VersionOps.ExportVersionJson((Py.Or(instance2.VersionJson(text), new PyDict()) as PyDict) ?? new PyDict(), (string pid) => instance2.VersionJson(pid) as PyDict, Utils.IS_WINDOWS)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("VersionOps.ExportScriptExtension(Utils.OS_NAME)", source, StringComparison.Ordinal);
        Assert.Contains("VersionOps.ExportLaunchScript(PPath.Norm(", source, StringComparison.Ordinal);
        Assert.Contains("launchCommand.game_dir, Utils.OS_NAME);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"launch-\" + instance2.name + \"-\" + text + \".bat\"", source, StringComparison.Ordinal);
    }

    /// <summary>ExportLaunchBat 本身仍然只做 cmd 脚本(没有被本包改成 POSIX 分支)。</summary>
    [Fact]
    public void ExportLaunchBat_is_untouched()
    {
        string source = RepoSource("src", "TipCL.Core", "Launcher", "VersionOps.cs");
        int start = source.IndexOf("public static string ExportLaunchBat(", StringComparison.Ordinal);
        int end = source.IndexOf("internal static string ExportScriptExtension(", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);

        string body = source.Substring(start, end - start);

        Assert.Contains("\"@echo off\",", body, StringComparison.Ordinal);
        Assert.Contains("\"chcp 65001 >nul\",", body, StringComparison.Ordinal);
        Assert.Contains("\"pause\"", body, StringComparison.Ordinal);
        Assert.Contains("string.Join(\"\\r\\n\", list) + \"\\r\\n\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("PosixShell", body, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ 小工具

    private static PyDict ChildJson()
    {
        return PyDict.Of(
            "id", "my-instance-1.20.1",
            "inheritsFrom", "1.20.1",
            "mainClass", "child.Main",
            "libraries", new List<object> { PyDict.Of("name", "com.example:child-lib:1.0") });
    }

    private static PyDict ParentJson()
    {
        return PyDict.Of(
            "id", "1.20.1",
            "assets", "1.20",
            "mainClass", "parent.Main",
            "libraries", new List<object> { PyDict.Of("name", "com.example:parent-lib:1.0") });
    }

    private static string TempDir()
    {
        string dir = Path.Combine(TestHome.Root, "b2c3-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
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
