// AI2-moddev-mcp:ToolRunner 的分部 —— 模组开发测试环境 5 个工具的分发,以及「非测试实例要额外确认」的闸门。 // i18n:ignore-file(工具结果)
// 共享的 ToolRunner.cs 只改了两处:ExecuteAsync 的 switch 加一组 case 转到这里;RunAsync 的权限判断外面套一层 ModDevGate
// (并让 ModDevForceAsk 能绕过「本对话都允许」)。
using System;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

public sealed partial class ToolRunner
{
    /// <summary>
    /// 这次调用是否必须弹确认(不管档位、不管「本对话都允许」):install_local_mod 的目标不是测试实例时。
    /// 判不出(实例不存在 / 参数坏)时返回 false,交给执行体去如实报错。
    /// </summary>
    internal bool ModDevForceAsk(ToolCall call)
    {
        if (call.name != "install_local_mod")
        {
            return false;
        }
        try
        {
            PyDict args = call.args ?? new PyDict();
            Instance inst = ModDevOps.ResolveInstance(Py.S(Py.Or(args.Get("instance"), Instance)));
            return !ModDevOps.IsDev(inst);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>非测试实例:allow 改成 ask,ask 的理由换成风险说明;deny 保持 deny。</summary>
    private PermissionVerdict ModDevGate(ToolCall call, PermissionVerdict verdict)
    {
        if (verdict.decision == "deny" || !ModDevForceAsk(call))
        {
            return verdict;
        }
        string reason = ModDevText.NonDevReason();
        return new PermissionVerdict("ask", verdict.decision == "ask" && verdict.reason.Length > 0 ? reason + ";" + verdict.reason : reason);
    }

    private async Task<object?> ExecuteModDevAsync(ToolCall call, PyDict args, CancellationToken ct)
    {
        switch (call.name)
        {
            case "create_test_instance":
            {
                // 先读参数(探针值在这里就会抛,保证任何改盘动作之前)
                _ = Py.S(args.Get("mc_version"));
                return await Task.Run(() => ModDevOps.CreateTestInstance(args), ct).ConfigureAwait(false);
            }
            case "install_local_mod":
            {
                _ = Py.S(args.Get("jar_path"));
                string inst = InstName(args);
                return await Task.Run(() => ModDevOps.InstallLocalMod(args, inst), ct).ConfigureAwait(false);
            }
            case "revert_local_mod":
            {
                _ = Py.S(args.Get("change_id"));
                return await Task.Run(() => ModDevOps.RevertLocalMod(args), ct).ConfigureAwait(false);
            }
            case "run_test_launch":
                return await RunTestLaunchAsync(args, ct).ConfigureAwait(false);
            case "get_task_status":
            {
                string id = Py.S(args.Get("task_id"));
                int lines = (int)Py.Int(Py.Or(args.Get("log_lines"), 40L));
                double wait = Py.Float(Py.Or(args.Get("wait_seconds"), 0L));
                return await ModDevTasks.StatusAsync(id, lines, wait, ct).ConfigureAwait(false);
            }
            default:
                return ToolErr(call.name, "unknown_tool", "没有这个工具: " + call.name, "");
        }
    }

    private async Task<object?> RunTestLaunchAsync(PyDict args, CancellationToken ct)
    {
        string version = Py.S(args.Get("version"));
        string pattern = Py.S(args.Get("ready_pattern"));
        int timeout = (int)Math.Clamp(Py.Int(Py.Or(args.Get("timeout_seconds"), (long)TestLaunch.DefaultTimeoutSeconds)),
            TestLaunch.MinTimeoutSeconds, TestLaunch.MaxTimeoutSeconds);
        bool wait = Py.Truthy(args.Get("wait"));
        if (pattern.Length > 0)
        {
            try
            {
                _ = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException ex)
            {
                throw new ValueError("ready_pattern 不是合法的正则: " + ex.Message);
            }
        }
        Instance inst = ModDevOps.ResolveInstance(InstName(args));
        string ver = TestLaunch.PickVersion(inst, version);
        if (ModDevTasks.RunningOf("run_test_launch") is { } busy)
        {
            return PyDict.Of("ok", false, "error", "busy", "task_id", busy.Id,
                "message", "已经有一个测试启动在跑(" + busy.Title + "),等它结束再来;可用 get_task_status 查它");
        }
        ITestLaunchDriver driver = ModDevHost.LaunchDriverFactory?.Invoke(_api) ?? new BackendLaunchDriver(_api);
        ModDevTask task = ModDevTasks.Start("run_test_launch", "测试启动 " + inst.name + " / " + ver,
            (t, tok) => TestLaunch.RunAsync(inst, ver, timeout, pattern, driver, t, tok));
        if (!wait)
        {
            return PyDict.Of(
                "ok", true,
                "task_id", task.Id,
                "instance", inst.name,
                "version", ver,
                "timeout_s", (long)timeout,
                "message", "测试启动已开始。用 get_task_status(task_id=" + task.Id + ", wait_seconds=30) 查进度;结束后 result 里有 outcome / 耗时 / 日志片段 / 崩溃摘要");
        }
        try
        {
            await ModDevTasks.WaitAsync(task, TimeSpan.FromSeconds(timeout + 60), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 用户在界面上点了「停止」:测试启动也停掉(监视循环的 finally 会关游戏)
            ModDevTasks.Cancel(task.Id);
            throw;
        }
        if (task.Result is { } result)
        {
            var copy = new PyDict(result) { ["task_id"] = task.Id, ["state"] = task.State };
            return copy;
        }
        return task.ToDict(60);
    }
}
