using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2c3 第 2 项:macOS 桌面快捷方式(Shortcut)。
///
/// 源码事实(读过 Shortcut.cs):
/// <list type="bullet">
/// <item><c>_create_macos</c> 写 &lt;Desktop&gt;/&lt;name&gt;.command,然后调 <c>Chmod755</c> ——
/// 而 <c>Chmod755</c> 在改动前是**空方法**,所以生成的 .command 从来没有可执行位;</item>
/// <item>脚本正文用的是 <c>_quote</c>(只在「有空格」时才加双引号,完全不转义)拼出来的,
/// 实例名 / 版本号 / 用户名都是用户可控文本,可带单引号、$、反引号 → 双击即以用户权限跑任意命令。</item>
/// </list>
///
/// 本包的做法:正文交给新抽出的纯函数 <c>MacCommandScript</c>(每个词走 PosixShell 的单引号规则),
/// <c>Chmod755</c> 改调 JavaRuntimeFiles.SetExecutable。
/// 红线:测试**绝不调** <c>CreateLaunchShortcut</c> / <c>RemoveLaunchShortcut</c> / <c>DesktopDir</c>
/// (会写真实桌面),只测纯函数 + 读源码核对生产调用点;落盘的文件都在 <see cref="TestHome.Root"/> 之下。
/// mac 上双击 .command 的真实行为**本机无法验证**(未验证项)。
/// </summary>
public class B2c3MacCommandScriptTests
{
    private const string Workdir = "/Users/me/Apps/TipCL.app/Contents/MacOS";
    private const string Target = "/Users/me/Apps/TipCL.app/Contents/MacOS/arm64/TipCL";
    private const string Hostile = "a'; rm -rf ~; echo '";

    // ------------------------------------------------------------------ 正文逐字

    /// <summary>普通取值下的完整正文(三行 + 结尾换行;每个词都被单引号包住)。</summary>
    [Fact]
    public void Plain_values_produce_the_expected_script()
    {
        string text = Shortcut.MacCommandScript(
            "TipCL 启动 1.20.1 / default", Workdir, Target, new[] { "-i", "我的实例", "launch", "1.20.1" });

        string expected =
            "#!/bin/sh\n" +
            "# TipCL 启动 1.20.1 / default\n" +
            "cd '/Users/me/Apps/TipCL.app/Contents/MacOS'\n" +
            "exec '/Users/me/Apps/TipCL.app/Contents/MacOS/arm64/TipCL' '-i' '我的实例' 'launch' '1.20.1'\n";

        Assert.Equal(expected, text);
    }

    /// <summary>
    /// 敌意实例名(任务书点名的那串)逐字对照:两个单引号各被换成 '\'' 四字符转义,
    /// 整串仍然只是一个词。
    /// </summary>
    [Fact]
    public void A_hostile_instance_name_is_escaped_by_the_posix_rule()
    {
        string text = Shortcut.MacCommandScript("desc", Workdir, Target, new[] { "-i", Hostile, "launch" });

        string expected =
            "#!/bin/sh\n" +
            "# desc\n" +
            "cd '/Users/me/Apps/TipCL.app/Contents/MacOS'\n" +
            "exec '/Users/me/Apps/TipCL.app/Contents/MacOS/arm64/TipCL' '-i' 'a'\\''; rm -rf ~; echo '\\''' 'launch'\n";

        Assert.Equal(expected, text);
        Assert.Contains("'a'\\''; rm -rf ~; echo '\\'''", text, StringComparison.Ordinal);
    }

    /// <summary>把一个词按 sh 的读法反解回来,逐个比对原参数(编码与解码是两段代码,不会互相掩盖)。</summary>
    [Fact]
    public void Every_word_round_trips_through_the_single_quoted_tokens()
    {
        string[] args =
        {
            "-i",
            "我的 实例",
            Hostile,
            "$HOME",
            "`id`",
            "say \"hi\"",
            "a\\b",
            "$(touch /tmp/pwn)",
            "",
            "1.20.1"
        };

        string text = Shortcut.MacCommandScript("desc", Workdir, Target, args);
        string execLine = text.Split('\n')[3];
        Assert.StartsWith("exec ", execLine, StringComparison.Ordinal);

        List<string> words = UnquoteWords(execLine.Substring("exec ".Length));

        Assert.Equal(args.Length + 1, words.Count);
        Assert.Equal(Target, words[0]);
        for (int i = 0; i < args.Length; i++)
        {
            Assert.Equal(args[i], words[i + 1]);
        }
    }

    /// <summary>描述里的 CR / LF 一律压成空格(注释只占一行)。</summary>
    [Theory]
    [InlineData("a\nb", "# a b")]
    [InlineData("a\rb", "# a b")]
    [InlineData("a\r\nb", "# a b")]
    [InlineData("a\n\nb", "# a  b")]
    [InlineData("TipCL 启动 实例\nlaunch", "# TipCL 启动 实例 launch")]
    public void Newlines_in_the_description_become_spaces(string description, string expectedCommentLine)
    {
        string text = Shortcut.MacCommandScript(description, Workdir, Target, Array.Empty<string>());

        Assert.Equal(
            "#!/bin/sh\n" + expectedCommentLine + "\ncd '" + Workdir + "'\nexec '" + Target + "'\n",
            text);
        Assert.DoesNotContain('\r', text);
    }

    /// <summary>描述里塞换行 + 一条命令,不能顶出注释行(这是本项的注入点之一)。</summary>
    [Fact]
    public void A_hostile_description_cannot_break_out_of_the_comment()
    {
        string text = Shortcut.MacCommandScript("x\necho pwned", Workdir, Target, Array.Empty<string>());

        Assert.Equal(
            "#!/bin/sh\n# x echo pwned\ncd '/Users/me/Apps/TipCL.app/Contents/MacOS'\nexec '/Users/me/Apps/TipCL.app/Contents/MacOS/arm64/TipCL'\n",
            text);
        Assert.DoesNotContain("\necho pwned", text, StringComparison.Ordinal);
    }

    /// <summary>空描述 / null 描述:注释行只有 "# "(不抛)。</summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void An_empty_description_leaves_an_empty_comment(string? description)
    {
        string text = Shortcut.MacCommandScript(description, Workdir, Target, Array.Empty<string>());

        Assert.Equal("# ", text.Split('\n')[1]);
    }

    /// <summary>没有参数时 exec 行只有目标本身,结尾不留多余空格。</summary>
    [Fact]
    public void No_args_means_no_trailing_space()
    {
        string text = Shortcut.MacCommandScript("d", Workdir, Target, new List<string>());

        Assert.Equal("#!/bin/sh\n# d\ncd '" + Workdir + "'\nexec '" + Target + "'\n", text);
        Assert.Equal(Shortcut.MacCommandScript("d", Workdir, Target, null), text);
    }

    /// <summary>路径带空格 / 单引号 / 中文都能安全变成一个字面词。</summary>
    [Fact]
    public void Paths_with_spaces_quotes_and_chinese_are_quoted()
    {
        string target = "/Applications/My App/偷懒启动器/TipCL";
        string workdir = "/Applications/My App";

        string text = Shortcut.MacCommandScript("d", workdir, target, Array.Empty<string>());

        Assert.Contains("cd '/Applications/My App'", text, StringComparison.Ordinal);
        Assert.Contains("exec '/Applications/My App/偷懒启动器/TipCL'", text, StringComparison.Ordinal);
        AssertSingleQuoted(text, target);
        AssertSingleQuoted(text, workdir);
    }

    /// <summary>目标路径本身带单引号时,同样走转义规则。</summary>
    [Fact]
    public void A_target_with_a_single_quote_is_escaped()
    {
        string target = "/tmp/it's/TipCL";

        string text = Shortcut.MacCommandScript("d", Workdir, target, Array.Empty<string>());

        Assert.Contains("exec '/tmp/it'\\''s/TipCL'", text, StringComparison.Ordinal);
    }

    /// <summary>用户可控的值只出现在正确加引号的词里(逐个核对前后各是一个单引号)。</summary>
    [Fact]
    public void User_controlled_values_appear_only_inside_single_quoted_tokens()
    {
        string[] args = { "-i", "$HOME", "`id`", "say \"hi\"", "中文 实例", "a;b|c" };

        string text = Shortcut.MacCommandScript("desc", Workdir, Target, args);

        foreach (string value in args)
        {
            AssertSingleQuoted(text, value);
        }
        AssertSingleQuoted(text, Target);
        AssertSingleQuoted(text, Workdir);
    }

    /// <summary>行尾只有 LF、结尾恰好一个换行 —— 不用 CR,也不吞掉最后的换行。</summary>
    [Fact]
    public void Only_lf_and_exactly_one_trailing_newline()
    {
        string[] inputs = { "d", "a\nb", "" };

        foreach (string description in inputs)
        {
            string text = Shortcut.MacCommandScript(description, Workdir, Target, new[] { "x" });
            Assert.DoesNotContain('\r', text);
            Assert.StartsWith("#!/bin/sh\n", text, StringComparison.Ordinal);
            Assert.EndsWith("\n", text, StringComparison.Ordinal);
            Assert.False(text.EndsWith("\n\n", StringComparison.Ordinal));
            Assert.DoesNotContain("'#!/bin/sh'", text, StringComparison.Ordinal);
        }
    }

    /// <summary>第一行就是 shebang,而且没有被引号包起来(否则 sh 不认)。</summary>
    [Fact]
    public void The_first_line_is_a_bare_shebang()
    {
        string text = Shortcut.MacCommandScript("d", Workdir, Target, Array.Empty<string>());

        Assert.Equal("#!/bin/sh", text.Split('\n')[0]);
    }

    // ------------------------------------------------------------------ 可执行位

    /// <summary>
    /// Chmod755 从空方法改成调 JavaRuntimeFiles.SetExecutable。
    /// 这里落一个文件在 TestHome.Root 之下,核对 Windows 上它是空操作(内容一个字节不动),
    /// 非 Windows 上是 0755(本机是 Windows,那一支跑不到,由 B2c2 的同类用例覆盖)。
    /// </summary>
    [Fact]
    public void SetExecutable_called_from_Chmod755_is_a_noop_on_windows()
    {
        string dir = Path.Combine(TestHome.Root, "b2c3-shortcut-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "x.command");
        byte[] payload = new UTF8Encoding(false).GetBytes("#!/bin/sh\nexit 0\n");
        File.WriteAllBytes(path, payload);

        JavaRuntimeFiles.SetExecutable(path);

        Assert.Equal(payload, File.ReadAllBytes(path));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(JavaRuntimeFiles.ModeForEntry(executable: true), File.GetUnixFileMode(path));
        }
    }

    // ------------------------------------------------------------------ 源码结构钉子

    /// <summary>Chmod755 不再是空方法(读源码核对;它现在只有一句)。</summary>
    [Fact]
    public void Chmod755_is_no_longer_empty()
    {
        string body = MethodBody("Shortcut.cs", "private static void Chmod755(", "private static string _create_linux(");

        Assert.Contains("JavaRuntimeFiles.SetExecutable(path);", body, StringComparison.Ordinal);
        Assert.DoesNotContain("{\n\t}", body, StringComparison.Ordinal);
    }

    /// <summary>mac 分支:正文走 MacCommandScript,写完补可执行位,编码仍是 UTF-8 无 BOM。</summary>
    [Fact]
    public void The_mac_branch_uses_the_new_script_and_chmods_it()
    {
        string body = MethodBody("Shortcut.cs", "private static string _create_macos(", "public static string CreateLaunchShortcut(");

        Assert.Contains("MacCommandScript(description, workdir, target, rawArgs)", body, StringComparison.Ordinal);
        Assert.Contains("Chmod755(PPath.Abs(text));", body, StringComparison.Ordinal);
        Assert.Contains("new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("_quote(", body, StringComparison.Ordinal);
        Assert.Contains("\".command\"", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// CreateLaunchShortcut:Windows / Linux 拿到的仍是同一份预拼接 arguments(值与改动前逐字相同),
    /// 原始参数表另外传给 macOS 分支。
    /// </summary>
    [Fact]
    public void CreateLaunchShortcut_keeps_the_prejoined_arguments_and_passes_raw_args_to_mac()
    {
        string body = MethodBody("Shortcut.cs", "public static string CreateLaunchShortcut(", "public static bool RemoveLaunchShortcut(");

        Assert.Contains("List<string> rawArgs = list.Skip(1).Concat(LaunchArgs(instance, version, username, account)).ToList();", body, StringComparison.Ordinal);
        Assert.Contains("string arguments = string.Join(\" \", rawArgs.Select(_quote));", body, StringComparison.Ordinal);
        Assert.Contains("return _create_macos(name2, target, arguments, pROJECT_ROOT, icon, description, rawArgs);", body, StringComparison.Ordinal);
        Assert.Contains("return _create_windows(name2, target, arguments, pROJECT_ROOT, icon, description);", body, StringComparison.Ordinal);
        Assert.Contains("return _create_linux(name2, target, arguments, pROJECT_ROOT, icon, description);", body, StringComparison.Ordinal);
    }

    /// <summary>Windows 与 Linux 两个分支一个字没动(Windows 是 .lnk + PowerShell,Linux 仍用预拼接 arguments)。</summary>
    [Fact]
    public void The_windows_and_linux_branches_are_untouched()
    {
        string source = RepoSource("src", "TipCL.Core", "Launcher", "Shortcut.cs");

        Assert.Contains("_ps_literal(arguments)", source, StringComparison.Ordinal);
        Assert.Contains("$sc.WorkingDirectory = ", source, StringComparison.Ordinal);
        Assert.Contains("$sc.Save()", source, StringComparison.Ordinal);
        Assert.Contains("\"Exec=\" + Py.Strip(_quote(target) + \" \" + arguments),", source, StringComparison.Ordinal);
        Assert.Contains("\"Terminal=false\",", source, StringComparison.Ordinal);
        Assert.Contains("if (!arg.Contains(\" \") && arg.Length != 0)", source, StringComparison.Ordinal);
        Assert.Contains("return \"\\\"\" + arg + \"\\\"\";", source, StringComparison.Ordinal);
        Assert.Contains("\"icon.ico\"", source, StringComparison.Ordinal);
    }

    /// <summary>Windows 钉子:.lnk 的 arguments 就是预拼接那串(取值与改动前一致)。</summary>
    [Fact]
    public void Windows_still_gets_the_prejoined_arguments_string()
    {
        string source = RepoSource("src", "TipCL.Core", "Launcher", "Shortcut.cs");

        // _quote 的输出形态没变:不含空格且非空 → 原样;否则两端加双引号。
        Assert.Equal("-i", Quote("-i"));
        Assert.Equal("\"a b\"", Quote("a b"));
        Assert.Equal("\"\"", Quote(""));
        Assert.Contains("string arguments = string.Join(\" \", rawArgs.Select(_quote));", source, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ 小工具

    /// <summary>把一个词按 POSIX 单引号规则反解成原文(只认本包生成的那种形态)。</summary>
    private static List<string> UnquoteWords(string line)
    {
        List<string> words = new List<string>();
        StringBuilder current = null;
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == ' ' && current == null)
            {
                i++;
                continue;
            }
            if (c != '\'')
            {
                throw new InvalidOperationException("出现了没有被单引号包住的字符: " + c + " @" + i);
            }
            current = new StringBuilder();
            i++;
            while (i < line.Length)
            {
                if (line[i] != '\'')
                {
                    current.Append(line[i]);
                    i++;
                    continue;
                }
                if (i + 3 < line.Length && line[i + 1] == '\\' && line[i + 2] == '\'' && line[i + 3] == '\'')
                {
                    // '\'' 四个字符 = 收尾 + 转义单引号 + 重开
                    current.Append('\'');
                    i += 4;
                    continue;
                }
                i++;
                break;
            }
            words.Add(current.ToString());
            current = null;
        }
        return words;
    }

    /// <summary>value 必须整段落在单引号里(调用者负责保证 value 本身不含单引号)。</summary>
    private static void AssertSingleQuoted(string text, string value)
    {
        Assert.DoesNotContain("'", value, StringComparison.Ordinal);
        int index = text.IndexOf(value, StringComparison.Ordinal);
        Assert.True(index > 0, "值没有出现在脚本里: " + value);
        Assert.Equal('\'', text[index - 1]);
        Assert.Equal('\'', text[index + value.Length]);
    }

    /// <summary>反射调用 <c>Shortcut._quote</c>(private static),核对它没被本包改动。</summary>
    private static string Quote(string arg)
    {
        System.Reflection.MethodInfo method = typeof(Shortcut).GetMethod(
            "_quote", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        return (string)method.Invoke(null, new object[] { arg });
    }

    private static string MethodBody(string fileName, string fromMarker, string toMarker)
    {
        string source = RepoSource("src", "TipCL.Core", "Launcher", fileName);
        int start = source.IndexOf(fromMarker, StringComparison.Ordinal);
        int end = source.IndexOf(toMarker, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "没找到源码里的方法边界: " + fromMarker);
        return source.Substring(start, end - start);
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
