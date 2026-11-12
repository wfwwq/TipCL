using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TipCL.App.Common;
using TipCL.App.Session;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>
/// M4-H P3.5:桌面快捷方式 + CLI 启动入口。
/// Windows 参考版的「创建桌面快捷方式」是坏的 —— 它生成的 `-i &lt;实例&gt; launch &lt;版本&gt;`
/// 在 Windows 工程里没有任何消费者(见 docs/PARITY.md §6),Linux 侧这里把它真的做通:
/// 断言 .desktop 的键值、可执行位、以及 Program.cs 的 CLI 入口真能按这些参数把版本启动起来。
/// </summary>
public class ShortcutTests : IDisposable
{
    private readonly IApi _api = InProcessApi.Instance;
    private readonly string _inst = "sc-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    private readonly string _home;
    private readonly string _desktop;
    private readonly string? _oldHome;
    private readonly string? _oldXdgDesktop;
    private readonly string? _oldPosix;

    public ShortcutTests()
    {
        // 隔离桌面目录:DesktopDir() 走 HOME(以及 XDG_DESKTOP_DIR),不动真实桌面
        _home = Path.Combine(Path.GetTempPath(), "tipcl-shortcut-" + Guid.NewGuid().ToString("N"));
        _desktop = Path.Combine(_home, "Desktop");
        Directory.CreateDirectory(_desktop);
        _oldHome = Environment.GetEnvironmentVariable("HOME");
        _oldXdgDesktop = Environment.GetEnvironmentVariable("XDG_DESKTOP_DIR");
        _oldPosix = Environment.GetEnvironmentVariable("TIPCL_SHORTCUT_POSIX");
        Environment.SetEnvironmentVariable("HOME", _home);
        Environment.SetEnvironmentVariable("XDG_DESKTOP_DIR", null);
        // Windows 开发机上强制走 Linux 分支:.desktop 是 Linux 交付物,只有走那条分支才断言得到
        Environment.SetEnvironmentVariable("TIPCL_SHORTCUT_POSIX", "1");

        TestApp.Run(() =>
        {
            _api.CallAsync<object>("create_instance", new { name = _inst }).GetAwaiter().GetResult();
            string verDir = Path.Combine(Instances.GetInstancePath(_inst), "versions", Ver);
            Directory.CreateDirectory(verDir);
            File.WriteAllText(Path.Combine(verDir, Ver + ".json"), $"{{\"id\":\"{Ver}\",\"type\":\"release\"}}");
        });
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HOME", _oldHome);
        Environment.SetEnvironmentVariable("XDG_DESKTOP_DIR", _oldXdgDesktop);
        Environment.SetEnvironmentVariable("TIPCL_SHORTCUT_POSIX", _oldPosix);
        TestApp.Run(() =>
        {
            try { _api.CallAsync<object>("delete_instance", new { name = _inst }).GetAwaiter().GetResult(); }
            catch (Exception) { }
        });
        try { Directory.Delete(_home, true); } catch (Exception) { }
    }

    private const string Ver = "sc-1.0";

    private static List<string> ReadKeyValues(string path)
        => File.ReadAllLines(path).Where(l => l.Contains('=')).ToList();

    private static string ValueOf(string path, string key)
        => File.ReadAllLines(path).First(l => l.StartsWith(key + "=", StringComparison.Ordinal)).Substring(key.Length + 1);

    [Fact]
    public void DesktopShortcut_CreatesDesktopEntry_WithLaunchArgs() => TestApp.RunAsync(async () =>
    {
        string path = await _api.CallAsync<string>("create_desktop_shortcut", new { instance = _inst, version = Ver }) ?? "";

        Assert.EndsWith(".desktop", path);
        Assert.Equal(Path.Combine(_desktop, Ver + " - " + _inst + ".desktop"), path);
        Assert.True(File.Exists(path));

        var lines = File.ReadAllLines(path);
        Assert.Equal("[Desktop Entry]", lines[0]);
        Assert.Contains(lines, l => l == "Type=Application");
        Assert.Contains(lines, l => l == "Name=" + Ver + " - " + _inst);
        Assert.Contains(lines, l => l.StartsWith("Comment=TipCL 启动 ", StringComparison.Ordinal) && l.Contains(_inst) && l.Contains(Ver));
        Assert.Contains(lines, l => l == "Terminal=false");
        Assert.Contains(lines, l => l == "Categories=Game;");

        // Exec 行:目标 + `-i <实例> launch <版本>`,与 Shortcut.LaunchArgs 严格一致
        string exec = ValueOf(path, "Exec");
        var parts = SplitExec(exec);
        Assert.True(parts.Count >= 5, "Exec 行参数太少: " + exec);
        Assert.Equal("-i", parts[^4]);
        Assert.Equal(_inst, parts[^3]);
        Assert.Equal("launch", parts[^2]);
        Assert.Equal(Ver, parts[^1]);

        // Path / Icon 必须是有效键,且 Icon 指向真实存在的图标文件
        string workdir = ValueOf(path, "Path");
        Assert.False(string.IsNullOrEmpty(workdir));
        Assert.DoesNotContain("\\", workdir);
        string icon = ValueOf(path, "Icon");
        Assert.True(File.Exists(icon), "Icon 指向的文件不存在: " + icon);
        Assert.True(ShortcutIcon.IsValidPng(icon));

        // 可执行位(参考版 _create_linux 的 Chmod755 是空方法,Linux 侧必须真的有)
        if (!OperatingSystem.IsWindows())
        {
            UnixFileMode mode = File.GetUnixFileMode(path);
            Assert.True(mode.HasFlag(UnixFileMode.UserExecute), "缺少可执行位: " + mode);
        }

        // 文件里不能有裸 Windows 路径(参考版直接把反斜杠路径写进 Path=)
        Assert.True(!lines.Any(l => l.StartsWith("Exec=", StringComparison.Ordinal) && l.Contains("\\")),
            "Exec 行含反斜杠: " + string.Join(" | ", lines));

        Assert.True(Shortcut.Exists(_inst, Ver));
    });

    [Fact]
    public void DesktopShortcut_Delete_RemovesFile() => TestApp.RunAsync(async () =>
    {
        string path = await _api.CallAsync<string>("create_desktop_shortcut", new { instance = _inst, version = Ver }) ?? "";
        Assert.True(File.Exists(path));

        string name = Shortcut.ShortcutName(_inst, Ver);
        var r = await _api.CallAsync<PyDict>("delete_desktop_shortcut", new { name });
        Assert.NotNull(r);
        Assert.True(Py.Truthy(r!.Get("ok")));
        Assert.False(File.Exists(path));
        Assert.False(Shortcut.Exists(_inst, Ver));

        // 再删一次:不抛错,如实回 ok=false
        var again = await _api.CallAsync<PyDict>("delete_desktop_shortcut", new { name });
        Assert.False(Py.Truthy(again!.Get("ok")));
    });

    [Fact]
    public void DesktopShortcut_UsesXdgDesktopDir() => TestApp.RunAsync(async () =>
    {
        string custom = Path.Combine(_home, "桌面");
        Directory.CreateDirectory(custom);
        Environment.SetEnvironmentVariable("XDG_DESKTOP_DIR", custom);
        try
        {
            string path = await _api.CallAsync<string>("create_desktop_shortcut", new { instance = _inst, version = Ver }) ?? "";
            Assert.Equal(custom, Path.GetDirectoryName(path));
            Assert.True(File.Exists(path));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DESKTOP_DIR", null);
        }
    });

    [Fact]
    public void DesktopShortcut_MissingVersion_Throws() => TestApp.RunAsync(async () =>
    {
        await Assert.ThrowsAsync<ShortcutError>(() => _api.CallAsync<string>("create_desktop_shortcut", new { instance = _inst }));
    });

    [Fact]
    public void ShortcutIcon_Render_IsValidPng()
    {
        byte[] png = ShortcutIcon.Render();
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png.Take(8).ToArray());
        // IHDR:宽高各 4 字节大端
        int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        Assert.Equal(ShortcutIcon.Size, w);
        Assert.Equal(ShortcutIcon.Size, h);
        Assert.Equal("IHDR", Encoding.ASCII.GetString(png, 12, 4));
    }

    // ───────────────────────── CLI 入口 ─────────────────────────

    [Fact]
    public void Cli_Parses_ShortcutShape()
    {
        // 与 Shortcut.LaunchArgs 的输出形状一一对应
        var cmd = CommandLine.Parse(Shortcut.LaunchArgs(_inst, Ver));
        Assert.Equal(CliKind.Launch, cmd.Kind);
        Assert.Equal("", cmd.Error);
        Assert.Equal(_inst, cmd.Instance);
        Assert.Equal(Ver, cmd.Version);
        Assert.Equal("", cmd.Username);
        Assert.Equal("", cmd.Account);
    }

    [Fact]
    public void Cli_Parses_AccountAndUsername()
    {
        var withAccount = CommandLine.Parse(new List<string> { "-i", _inst, "launch", Ver, "--account", "Steve" });
        Assert.Equal("Steve", withAccount.Account);
        Assert.Equal("", withAccount.Username);

        var withUser = CommandLine.Parse(new List<string> { "-i", _inst, "launch", Ver, "--username", "Alex" });
        Assert.Equal("Alex", withUser.Username);
        Assert.Equal("", withUser.Account);

        // 长开关形态与 Shortcut.LaunchArgs 的输出必须完全等价
        var fromShortcut = CommandLine.Parse(Shortcut.LaunchArgs(_inst, Ver, null, "Steve"));
        Assert.Equal(withAccount.Account, fromShortcut.Account);
        Assert.Equal(withAccount.Version, fromShortcut.Version);
    }

    [Fact]
    public void Cli_NotALaunchCommand_LeavesGuiAlone()
    {
        Assert.Equal(CliKind.None, CommandLine.Parse(new List<string>()).Kind);
        Assert.Equal(CliKind.None, CommandLine.Parse(new List<string> { "--smoke" }).Kind);
        Assert.Equal(CliKind.None, CommandLine.Parse(new List<string> { "--nav-dump" }).Kind);
    }

    [Fact]
    public void Cli_BadUsage_ReportsError()
    {
        var missingVersion = CommandLine.Parse(new List<string> { "-i", _inst, "launch" });
        Assert.Equal(CliKind.Launch, missingVersion.Kind);
        Assert.Contains("版本", missingVersion.Error);

        var missingInstance = CommandLine.Parse(new List<string> { "launch", Ver });
        Assert.Contains("实例", missingInstance.Error);

        // 有解析错误时 Run 只报错不启动,退出码是「用法错误」
        var output = new StringWriter();
        Assert.Equal(CliExit.Usage, CommandLine.Run(missingVersion, _api, output));
        Assert.Contains("版本", output.ToString());
    }

    [Fact]
    public void Cli_PrintOnly_ShowsShortcutArgs() => TestApp.Run(() =>
    {
        var cmd = CommandLine.Parse(new List<string> { "-i", _inst, "launch", Ver, "--print-command" });
        var output = new StringWriter();
        int code = CommandLine.Run(cmd, _api, output);
        Assert.Equal(CliExit.Ok, code);
        string text = output.ToString();
        Assert.Contains("-i", text);
        Assert.Contains("launch", text);
        Assert.Contains(Ver, text);
    });

    /// <summary>
    /// P0(主 agent 复核 A 节):`-i &lt;不存在的实例&gt;` 必须以 **用法错误(4)** 退出并打印明确原因。
    /// 修前它不报错:EnsureInstance 在单根模式下把未知名字吞成根实例,在多实例模式下直接新建实例目录,
    /// 于是快捷方式会在错的游戏目录里静默启动(那时看到的 exit=3 来自「没有离线账号」,与本条无关)。
    /// </summary>
    [Fact]
    public void Cli_UnknownInstance_ExitsWithUsage() =>
        TestApp.Run(() =>
        {
            string bogus = "no-such-instance-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var cmd = CommandLine.Parse(new List<string> { "-i", bogus, "launch", Ver });
            var output = new StringWriter();
            int code = CommandLine.Run(cmd, _api, output);

            Assert.Equal(CliExit.Usage, code);
            string text = output.ToString();
            Assert.Contains("没有这个实例:" + bogus, text);
            // 校验只查不改:不能顺手把那个不存在的实例建出来
            Assert.False(Directory.Exists(Instances.GetInstancePath(bogus)), "CLI 不该凭空创建实例目录");
        });

    /// <summary>`-i` 缺值同样归到「用法错误(4)」,不落到别的码上。</summary>
    [Fact]
    public void Cli_MissingInstanceValue_ExitsWithUsage()
    {
        var cmd = CommandLine.Parse(new List<string> { "-i" });
        Assert.Equal(CliKind.Launch, cmd.Kind);
        Assert.Contains("实例", cmd.Error);

        var output = new StringWriter();
        Assert.Equal(CliExit.Usage, CommandLine.Run(cmd, _api, output));
        Assert.Contains("实例", output.ToString());
    }

    /// <summary>存在性校验在 --print-command 之前:排障打印的也得是「真能跑」的那条命令。</summary>
    [Fact]
    public void Cli_UnknownInstance_PrintOnly_AlsoFails() => TestApp.Run(() =>
    {
        var cmd = CommandLine.Parse(new List<string> { "-i", "no-such-instance", "launch", Ver, "--print-command" });
        var output = new StringWriter();
        Assert.Equal(CliExit.Usage, CommandLine.Run(cmd, _api, output));
        Assert.Contains("没有这个实例", output.ToString());
    });

    /// <summary>
    /// 端到端:照着 .desktop 里那条命令行跑 CLI,真的把版本启动起来(用假 java 记录收到的参数)。
    /// 不联网、不下载:假 java 脚本把 -version 问话与真正的 JVM 参数分别记下来。
    /// </summary>
    [Fact]
    public void Cli_Launch_RunsGameWithShortcutArgs() => TestApp.RunAsync(async () =>
    {
        if (OperatingSystem.IsWindows())
            return; // 假 java 是 shell 脚本,Windows 上没法跑(该路径本身就是 Linux 交付目标)

        string bin = Path.Combine(_home, "fake-java");
        Directory.CreateDirectory(bin);
        string log = Path.Combine(_home, "java-args.txt");
        string java = Path.Combine(bin, "java");
        File.WriteAllText(java,
            "#!/bin/sh\n" +
            "if [ \"$1\" = \"-version\" ]; then\n" +
            "  echo 'openjdk version \"17.0.9\" 2023-10-17' >&2\n" +
            "  echo 'OpenJDK Runtime Environment (build 17.0.9+9)' >&2\n" +
            "  exit 0\n" +
            "fi\n" +
            "echo \"GAME-ARGS $@\" >> \"" + log + "\"\n" +
            "exit 0\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(java, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        // 版本 json 声明 java 17(与假 java 的 -version 输出一致)
        string verDir = Path.Combine(Instances.GetInstancePath(_inst), "versions", Ver);
        Directory.CreateDirectory(Path.Combine(verDir, "natives"));
        File.WriteAllText(Path.Combine(verDir, Ver + ".json"),
            $"{{\"id\":\"{Ver}\",\"type\":\"release\",\"javaVersion\":{{\"majorVersion\":17}}," +
            "\"mainClass\":\"net.minecraft.client.main.Main\",\"libraries\":[],\"arguments\":{\"game\":[],\"jvm\":[]}}");

        // 版本设置里钉死 Java 路径,跳过「自动选择」的探测
        await _api.CallAsync<PyDict>("save_version_settings", new { instance = _inst, version = Ver, data = new { java, memory_mb = 1024 } });

        var cmd = CommandLine.Parse(new List<string> { "-i", _inst, "launch", Ver });
        var output = new StringWriter();
        int code = await Task.Run(() => CommandLine.Run(cmd, _api, output));

        Assert.Equal(CliExit.Ok, code);
        Assert.True(File.Exists(log), "假 java 没被启动;CLI 输出:\n" + output);
        string args = File.ReadAllText(log);
        Assert.Contains("GAME-ARGS", args);
        Assert.Contains("--version", args);
        // CLI 走的是与 UI 相同的启动链:日志里有实例/版本/账号/Java
        string text = output.ToString();
        Assert.Contains(_inst, text);
        Assert.Contains("Java", text);
    });

    private static List<string> SplitExec(string exec)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        foreach (char c in exec)
        {
            if (c == '"') { quoted = !quoted; continue; }
            if (c == ' ' && !quoted)
            {
                if (sb.Length > 0) { result.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) result.Add(sb.ToString());
        return result;
    }
}
