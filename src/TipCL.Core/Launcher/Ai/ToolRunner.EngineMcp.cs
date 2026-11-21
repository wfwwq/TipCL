// AI-engine-mcp:ToolRunner 的两处扩展(分文件放,少和别的包在 ToolRunner.cs 上撞车)。
//   1. 工具前后钩子接线(Hooks.RunBeforeEx / RunAfter):钩子错误不让调用崩,经 HookErrors 回调 + 轨迹回传;
//   2. ForceConfirmWrites:本机 MCP Server 用的「外部发起的写操作一律弹确认」闸门(见 McpServer)。
using System;
using System.Collections.Generic;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

public sealed partial class ToolRunner
{
    /// <summary>
    /// 为 true 时,写操作即使档位/规则判了 allow 也改成「需要确认」(拒绝照旧是拒绝),
    /// 且确认卡上的「以后都允许」不生效。本机 MCP Server 的执行器打开它:外部 agent 发起的写操作
    /// 必须由坐在电脑前的用户点一下才执行(参考版 McpServer 直接执行,只在 instructions 里请外部 agent
    /// 「先和用户确认」—— Linux 侧不把安全交给对方自觉,见 DECISIONS 待补条目 / progress/AI-engine-mcp.md)。
    /// </summary>
    public bool ForceConfirmWrites { get; set; }

    /// <summary>外部写操作闸门给确认卡的理由(显示在确认卡上;AI2-i18n-ux:走词表)。</summary>
    public static string ExternalWriteReason => I18nCore.Tr("外部 MCP 客户端发起的写操作需要你确认");

    /// <summary>
    /// 钩子报错回传(工具调用、阶段 before/after、错误列表「钩子名: 异常」)。
    /// 钩子出错不影响这次调用本身;宿主可以把它显示在步骤行或日志里。
    /// </summary>
    public Action<ToolCall, string, List<string>>? HookErrors { get; set; }

    private PermissionVerdict ExternalWriteGate(PermissionVerdict verdict)
    {
        if (ForceConfirmWrites && verdict.decision == "allow")
        {
            return new PermissionVerdict("ask", ExternalWriteReason);
        }
        return verdict;
    }

    /// <summary>跑执行前钩子:改写 call.args;返回非 null 表示被某个钩子拦下(值为理由)。</summary>
    private string? RunBeforeHooks(ToolCall call)
    {
        (PyDict args, List<string> errors, string? blocked) = Hooks.RunBeforeEx(call.name, call.args);
        call.args = args;
        ReportHookErrors(call, "before", errors);
        return blocked;
    }

    /// <summary>跑执行后钩子:返回(可能被改写的)结果文本。</summary>
    private string RunAfterHooks(ToolCall call, string text)
    {
        (string result, List<string> errors) = Hooks.RunAfter(call.name, text);
        ReportHookErrors(call, "after", errors);
        return result;
    }

    private void ReportHookErrors(ToolCall call, string phase, List<string> errors)
    {
        if (errors.Count == 0)
        {
            return;
        }
        Trace.Record("hook_errors", null, phase, call.name, null, null, null, "count", (long)errors.Count);
        try
        {
            HookErrors?.Invoke(call, phase, errors);
        }
        catch (Exception)
        {
            // 宿主回调自己出错也不能拖垮工具调用
        }
    }
}
