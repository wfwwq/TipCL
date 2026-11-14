// M4-H P3.5:命令行入口(把 Windows 版「创建桌面快捷方式」生成的参数真的消费掉)。
//
// 背景:Windows 参考仓库 Core/Launcher/Shortcut.cs:79 生成的目标参数是
//   -i <实例> launch <版本> [--account X|--username Y]
// 但整个 Windows 工程没有任何地方解析它(App.xaml.cs 的 OnStartup 只认 --smoke/--shot/--nav-dump/...),
// 即双击那个快捷方式只会打开启动器 —— 这是参考实现的缺陷(见 docs/PARITY.md §6)。
// Linux 侧按 Shortcut.LaunchArgs 的输出形状实现本入口,走与 UI 完全相同的启动链路(LaunchCore.RunAsync),
// 启动后按语义退出(游戏进程结束后带着它的退出码收尾),不留静默挂着的 GUI 进程。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TipCL.App.Common;
using TipCL.App.Common.Launch;
using TipCL.Core;
using TipCL.Core.Launcher;

namespace TipCL.App.Session;

/// <summary>要执行的 CLI 动作。</summary>
internal enum CliKind
{
    /// <summary>不是启动命令:交给 GUI 正常启动。</summary>
    None,

    /// <summary>-i &lt;实例&gt; launch &lt;版本&gt;</summary>
    Launch,
}

/// <summary>解析后的命令行。</summary>
internal sealed class CliCommand
{
    public CliKind Kind { get; init; } = CliKind.None;

    public string Instance { get; init; } = "";

    public string Version { get; init; } = "";

    public string Username { get; init; } = "";

    public string Account { get; init; } = "";

    /// <summary>预检失败时仍启动(用启动页「强制启动」的同一开关,--force 需要显式给)。</summary>
    public bool Force { get; init; }

    /// <summary>不真正启动,只把启动参数打印出来(排障与测试用;不出现在快捷方式里)。</summary>
    public bool PrintOnly { get; init; }

    /// <summary>解析出错时的人话原因(非空即表示命令行写错了)。</summary>
    public string Error { get; init; } = "";
}

/// <summary>退出码约定(冒烟/测试按这些数字断言)。</summary>
internal static class CliExit
{
    public const int Ok = 0;

    /// <summary>游戏崩溃 / 启动流程失败。</summary>
    public const int LaunchFailed = 3;

    /// <summary>命令行写错(缺参数、实例不存在...)。</summary>
    public const int Usage = 4;
}

/// <summary>CLI 解析与执行。</summary>
internal static class CommandLine
{
    /// <summary>
    /// 解析参数。**形状必须与 Core/Launcher/Shortcut.LaunchArgs 严格一致**:
    /// `-i &lt;实例&gt; launch &lt;版本&gt; [--account X|--username Y]`。
    /// 不是启动命令时返回 Kind=None(外壳照常开 GUI),不报错。
    /// </summary>
    public static CliCommand Parse(IReadOnlyList<string> args)
    {
        string instance = "", version = "", username = "", account = "";
        bool launch = false, force = false, printOnly = false;

        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-i":
                case "--instance":
                    if (i + 1 >= args.Count) return Bad("缺少 <实例>");
                    instance = args[++i];
                    break;
                case "launch":
                case "run":
                    launch = true;
                    break;
                case "--username":
                    if (i + 1 >= args.Count) return Bad("缺少 --username 的值");
                    username = args[++i];
                    break;
                case "--account":
                    if (i + 1 >= args.Count) return Bad("缺少 --account 的值");
                    account = args[++i];
                    break;
                case "--force":
                    force = true;
                    break;
                case "--print-command":
                case "--print":
                    printOnly = true;
                    break;
                default:
                    // 版本 ID 是第一个不是别的开关的裸参数
                    if (!a.StartsWith("-") && version.Length == 0 && launch) version = a;
                    break;
            }
        }

        if (!launch) return new CliCommand { Kind = CliKind.None };
        if (instance.Length == 0) return Bad("缺少实例:请用 -i <实例> launch <版本>");
        if (version.Length == 0) return Bad("缺少版本:请用 -i <实例> launch <版本>");
        return new CliCommand
        {
            Kind = CliKind.Launch,
            Instance = instance,
            Version = version,
            Username = username,
            Account = account,
            Force = force,
            PrintOnly = printOnly,
        };

        static CliCommand Bad(string message) => new() { Kind = CliKind.Launch, Error = message };
    }

    /// <summary>执行解析结果,返回进程退出码。</summary>
    public static int Run(CliCommand cmd, IApi? api = null, TextWriter? output = null)
    {
        output ??= Console.Out;
        if (cmd.Error.Length > 0)
        {
            output.WriteLine(cmd.Error);
            return CliExit.Usage;
        }

        var realApi = api ?? InProcessApi.Instance;
        try
        {
            return RunAsync(cmd, realApi, output).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            output.WriteLine(ex is PyException ? ex.Message : ex.ToString());
            AppLog.Error("CLI 启动失败", ex);
            return CliExit.LaunchFailed;
        }
    }

    private static async Task<int> RunAsync(CliCommand cmd, IApi api, TextWriter output)
    {
        // 与控制台一致的中文输出(真机 nohup/重定向时同样是 UTF-8)
        try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }

        // 实例存在性前置校验(**只查不改**):实例名打错或实例被删时,必须以「用法错误」退出。
        // 不校验的话 EnsureInstance 会把未知名字吞成默认实例(单根模式)或直接新建实例目录,
        // 桌面快捷方式就会在**另一个游戏目录**里静默启动(存档/模组全不对)。
        Instance inst = InstancesApi.FindInstance(cmd.Instance);
        if (inst == null)
        {
            output.WriteLine("没有这个实例:" + cmd.Instance);
            output.WriteLine("请检查实例名,或用「实例库」页确认可用实例。");
            return CliExit.Usage;
        }

        var args = new PyDict
        {
            ["instance"] = cmd.Instance,
            ["version"] = cmd.Version,
        };
        if (cmd.Username.Length > 0) args["username"] = cmd.Username;
        if (cmd.Force) args["force"] = true;

        if (cmd.Account.Length > 0)
        {
            // Windows 侧快捷方式可以带 --account <名>(「离线模式」= 用当前账号)。
            // 启动链要求账号已存在,这里先按「当前账号」语义解析成真实账号名再传。
            var manager = new AccountManager();
            if (cmd.Account == "离线模式" || cmd.Account == "offline")
            {
                PyDict active = manager.GetActive();
                if (active == null) throw new LaunchError(I18nCore.Tr("还没有账号,请先在启动器里添加一个账号"));
                args["account"] = active["name"];
            }
            else if (manager.GetAccount(cmd.Account) != null)
            {
                args["account"] = cmd.Account;
            }
            else
            {
                throw new LaunchError(Py.Fmt(I18nCore.Tr("账号不存在: {0}"), cmd.Account));
            }
        }

        if (cmd.PrintOnly)
        {
            output.WriteLine("实例: " + inst.name);
            output.WriteLine("版本: " + cmd.Version);
            output.WriteLine("启动参数: " + string.Join(" ", Shortcut.LaunchArgs(cmd.Instance, cmd.Version,
                cmd.Username.Length > 0 ? cmd.Username : null, args.Get("account"))));
            return CliExit.Ok;
        }

        // CLI 也把日志/崩溃这两类事件打到 stdout:无人看 GUI,终端就是唯一的现场
        void OnEvent(ApiEvent ev)
        {
            if (ev.Event == "log" && !string.IsNullOrEmpty(ev.Text)) output.WriteLine(ev.Text);
            else if (ev.Event == "game_started") output.WriteLine("游戏进程已启动: " + ev.Message + " " + ev.Text + " (pid " + ev.Current + ")");
            else if (ev.Event == "game_exited") output.WriteLine("游戏进程已退出: " + ev.Message + " " + ev.Text);
            else if (ev.Event == "crash") output.WriteLine("崩溃: " + ev.Message);
        }

        api.BridgeEvent += OnEvent;
        try
        {
            var progress = new TaskProgress("cli-launch", api);
            await LaunchCore.RunAsync(progress, System.Threading.CancellationToken.None, api, args)
                .ConfigureAwait(false);
            output.WriteLine("启动流程结束,游戏已退出");
            return CliExit.Ok;
        }
        catch (GameCrashError crash)
        {
            // 崩溃时仍按「启动流程跑完了」收尾:崩溃本身不是命令行错误
            output.WriteLine(crash.Message);
            TryWriteCrash(output, crash.Report);
            AppLog.Error("CLI 启动的游戏崩溃", crash);
            return CliExit.LaunchFailed;
        }
        catch (Exception ex)
        {
            output.WriteLine(ex is PyException ? ex.Message : ex.ToString());
            AppLog.Error("CLI 启动失败", ex);
            return CliExit.LaunchFailed;
        }
        finally
        {
            api.BridgeEvent -= OnEvent;
        }
    }

    private static void TryWriteCrash(TextWriter output, PyDict? report)
    {
        if (report == null || report.Count == 0)
        {
            return;
        }
        string summary = Py.S(Py.Or(report.Get("summary"), report.Get("headline")));
        if (summary.Length > 0) output.WriteLine("崩溃摘要: " + summary);
    }
}
