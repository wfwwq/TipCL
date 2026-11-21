// AI2-moddev-mcp:外部 MCP 写操作确认的等待超时。 // i18n:ignore-file(超时结果是回给外部 agent 的工具结果)
//
// 背景(progress/AI-engine-mcp.md §5 第 1 条):外部写操作在主窗口上弹模态确认框,主窗口不在前台时用户看不到,
// 外部客户端会一直挂着。现在:
//   · 宿主(App 的 McpServerHost)弹框前把主窗口还原 + 激活置前;
//   · 这里给确认回调套一层超时(默认 120 秒,McpServer.ConfirmTimeout 可调):超时按「拒绝」处理,
//     工具结果是 permission_denied + reason=confirm_timeout + 说明;同时取消 CurrentToken,宿主据此把还开着的确认框关掉。
// 放 Core 是为了可测:单测用一个永不回答的假确认回调即可验证超时路径,不需要界面。
using System;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>给 MCP 执行器的确认回调加等待超时。</summary>
public static class McpConfirmGate
{
    private static readonly AsyncLocal<CancellationToken> _token = new();

    /// <summary>
    /// 当前这次确认的「放弃等待」信号:确认回调(宿主的弹框)在**同步开头**读它,超时后会被取消,
    /// 宿主据此关掉还开着的确认框。不在 MCP 确认里时是 CancellationToken.None。
    /// </summary>
    public static CancellationToken CurrentToken => _token.Value;

    /// <summary>一次 tools/call 的超时状态。</summary>
    public sealed class Handle
    {
        public TimeSpan Timeout { get; init; }

        public bool TimedOut { get; internal set; }
    }

    /// <summary>
    /// 把 <paramref name="runner"/> 的 Confirm 换成带超时的版本。没有确认回调或超时 &lt;= 0 时原样不动
    /// (没有回调本来就会被如实拒绝)。
    /// </summary>
    public static Handle Wrap(ToolRunner runner, TimeSpan timeout)
    {
        var handle = new Handle { Timeout = timeout };
        Func<ToolCall, string, string, PyDict?, Task<(bool ok, bool always)>>? inner = runner.Confirm;
        if (inner == null || timeout <= TimeSpan.Zero)
        {
            return handle;
        }
        runner.Confirm = async (call, label, reason, preview) =>
        {
            var cts = new CancellationTokenSource();
            Task<(bool ok, bool always)> answer;
            _token.Value = cts.Token;
            try
            {
                answer = inner(call, label, reason, preview);
            }
            finally
            {
                _token.Value = default;
            }
            Task first = await Task.WhenAny(answer, Task.Delay(timeout)).ConfigureAwait(false);
            if (first != answer)
            {
                handle.TimedOut = true;
                cts.Cancel();
                // 宿主的弹框之后再回答也不用管了;把它可能的异常吃掉,免得变成未观察异常
                _ = answer.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                Trace.Record("mcp_confirm_timeout", null, "", call.name, null, null, null, "seconds", (long)timeout.TotalSeconds);
                return (false, false);
            }
            return await answer.ConfigureAwait(false);
        };
        return handle;
    }

    /// <summary>超时的工具结果(permission_denied + 原因)。</summary>
    public static string TimeoutPayload(string tool, TimeSpan timeout)
        => PyJson.Dumps(PyDict.Of(
            "ok", false,
            "error", "permission_denied",
            "reason", "confirm_timeout",
            "message", "等了 " + (long)timeout.TotalSeconds + " 秒用户都没有在 TipCL 窗口里确认,按拒绝处理。请提醒用户切到 TipCL 窗口后再试",
            "detail", "",
            "tool", tool));
}
