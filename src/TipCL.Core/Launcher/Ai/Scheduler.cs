// AI-engine-mcp:同一轮里多个工具调用的分组与并行执行。
// 来源: Windows 版参考仓库 windos/src/TipCL/Core/Launcher/Ai/Scheduler.cs(IsExclusive / IsParallelizable /
// Group(maxConcurrency=4) / RunGroups)。分组规则逐条照搬,判定**每次都现读 ToolMeta**(别的包新增工具时
// 不用改这里):
//   · 独占(单独成组、前后都切断):ask_user,以及 side_effect 为 delete / launch 的工具;
//   · 可并行:ToolMeta 标了 readonly、side_effect 是 none / read / network 的工具;
//   · 其余(写本机、工具表里没有的名字、外部 MCP 工具 mcp_*)一律单独成组串行。
// Linux 侧收紧的两处(参考版没有,理由写在这里,不假装与参考版一致):
//   1. long_running 的工具不并行 —— 它们会起后台任务卡,参考版的只读工具里恰好没有长任务;
//   2. 会话内状态工具(update_plan / remember / forget / dispatch_subagent)不并行 —— 它们虽标 readonly,
//      改的是界面计划、记忆文件或要跑一整个子会话,同一轮里的先后顺序有语义(先记再忘),并行会打乱。
// 执行:组内用 SemaphoreSlim 限并发(默认 4),**结果按传入顺序回填**;某个调用抛异常只把它自己变成一条
// 工具错误(异常隔离),兄弟调用照常拿到结果;用户点「停止」(OperationCanceledException)则等组内都收尾后
// 原样抛出。参考版 RunGroups 是「等完再抛第一个异常」,Linux 侧改成隔离:ToolRunner 本身就把执行异常
// 收成错误载荷,能漏到这里的只有调度层自己的意外,不该拖垮同组的只读查询。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>工具调用调度:分组(谁能并行)+ 组内限并发执行(照 Windows 版 Scheduler)。</summary>
public static class Scheduler
{
    /// <summary>默认组内并发上限(与参考版 maxConcurrency 默认值一致)。</summary>
    public const int DefaultMaxConcurrency = 4;

    private static readonly string[] EXCLUSIVE_SIDE_EFFECTS = { "delete", "launch" };

    private static readonly string[] PARALLEL_SIDE_EFFECTS = { "none", "read", "network" };

    /// <summary>标了只读、但同一轮里先后顺序有语义的会话内工具(Linux 侧收紧,见文件头)。</summary>
    private static readonly HashSet<string> SESSION_STATE_TOOLS = new(StringComparer.Ordinal)
    {
        "update_plan", "remember", "forget", "dispatch_subagent",
    };

    /// <summary>是否独占:单独成组,且把前后的并行组切断(ask_user、删除类、启动类)。</summary>
    public static bool IsExclusive(ToolCall tc)
    {
        if (tc == null)
        {
            return false;
        }
        if (tc.name == "ask_user")
        {
            return true;
        }
        ToolMetaInfo? meta = ToolMeta.Get(tc.name);
        return meta != null && Array.IndexOf(EXCLUSIVE_SIDE_EFFECTS, meta.side_effect) >= 0;
    }

    /// <summary>是否可与相邻的只读调用并行(每次现读 ToolMeta,新增工具自动生效)。</summary>
    public static bool IsParallelizable(ToolCall tc)
    {
        if (tc == null || IsExclusive(tc))
        {
            return false;
        }
        ToolMetaInfo? meta = ToolMeta.Get(tc.name);
        if (meta == null || !meta.@readonly || meta.long_running)
        {
            return false;
        }
        if (SESSION_STATE_TOOLS.Contains(tc.name))
        {
            return false;
        }
        return Array.IndexOf(PARALLEL_SIDE_EFFECTS, meta.side_effect) >= 0;
    }

    /// <summary>
    /// 把一轮里的工具调用按原顺序切成若干组:相邻的可并行调用合成一组(每组至多 maxConcurrency 个),
    /// 其余各自成组。组的先后顺序 = 原 tool_call 顺序,拼起来与输入逐个相同。
    /// </summary>
    public static List<List<ToolCall>> Group(IEnumerable<ToolCall> toolCalls, int maxConcurrency = DefaultMaxConcurrency)
        => Group(toolCalls, c => c, maxConcurrency);

    /// <summary>
    /// 泛型版分组:<paramref name="callOf"/> 从元素里取出工具调用(会话循环拿的是带原始 JSON 的包装对象)。
    /// </summary>
    public static List<List<T>> Group<T>(IEnumerable<T> items, Func<T, ToolCall> callOf, int maxConcurrency = DefaultMaxConcurrency)
    {
        var groups = new List<List<T>>();
        var current = new List<T>();
        int cap = Math.Max(1, maxConcurrency);
        void Flush()
        {
            if (current.Count > 0)
            {
                groups.Add(current);
                current = new List<T>();
            }
        }
        foreach (T item in items ?? Enumerable.Empty<T>())
        {
            ToolCall call = callOf(item);
            if (IsParallelizable(call))
            {
                if (current.Count >= cap)
                {
                    Flush();
                }
                current.Add(item);
            }
            else
            {
                Flush();
                groups.Add(new List<T> { item });
            }
        }
        Flush();
        return groups;
    }

    /// <summary>
    /// 执行一组:单个直接 await;多个按并发上限并行。返回值与 <paramref name="group"/> 下标一一对应。
    /// 单个调用抛出的非取消异常被收成该调用的工具错误(其它调用不受影响);取消异常等组内都结束后再抛。
    /// </summary>
    public static async Task<string[]> RunGroupAsync(IReadOnlyList<ToolCall> group, Func<ToolCall, Task<string>> execute,
        int maxConcurrency = DefaultMaxConcurrency, CancellationToken ct = default)
    {
        var results = new string[group.Count];
        if (group.Count == 0)
        {
            return results;
        }
        if (group.Count == 1)
        {
            results[0] = await Isolated(group[0], execute).ConfigureAwait(false);
            return results;
        }
        using var sem = new SemaphoreSlim(Math.Min(Math.Max(1, maxConcurrency), group.Count));
        var tasks = new Task<string>[group.Count];
        for (int i = 0; i < group.Count; i++)
        {
            ToolCall call = group[i];
            tasks[i] = Task.Run(async () =>
            {
                await sem.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    return await Isolated(call, execute).ConfigureAwait(false);
                }
                finally
                {
                    sem.Release();
                }
            }, CancellationToken.None);
        }
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 逐个看结果:取消要往上抛,别的已经在 Isolated 里收成错误了
        }
        for (int i = 0; i < tasks.Length; i++)
        {
            Task<string> t = tasks[i];
            if (t.IsCanceled || (t.IsFaulted && t.Exception?.InnerException is OperationCanceledException))
            {
                throw new OperationCanceledException(ct);
            }
            if (t.IsFaulted)
            {
                results[i] = FailurePayload(group[i], t.Exception?.InnerException ?? new InvalidOperationException("调度失败"));
                continue;
            }
            results[i] = t.Result;
        }
        return results;
    }

    /// <summary>
    /// 参考版形状的整轮执行:逐组跑,返回 tool_call id → 结果。id 重复时后者覆盖前者(与参考版字典语义一致);
    /// 会话循环自己按下标回填,不走这个入口。
    /// </summary>
    public static async Task<Dictionary<string, string>> RunGroupsAsync(List<List<ToolCall>> groups, Func<ToolCall, Task<string>> execute,
        int maxConcurrency = DefaultMaxConcurrency, CancellationToken ct = default)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (List<ToolCall> group in groups ?? new List<List<ToolCall>>())
        {
            ct.ThrowIfCancellationRequested();
            string[] results = await RunGroupAsync(group, execute, maxConcurrency, ct).ConfigureAwait(false);
            for (int i = 0; i < group.Count; i++)
            {
                map[group[i].id ?? ""] = results[i];
            }
        }
        return map;
    }

    private static async Task<string> Isolated(ToolCall call, Func<ToolCall, Task<string>> execute)
    {
        try
        {
            return await execute(call).ConfigureAwait(false) ?? "";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return FailurePayload(call, ex);
        }
    }

    private static string FailurePayload(ToolCall call, Exception ex)
    {
        string code = Tools.ClassifyException(ex);
        try
        {
            call.Mark(ToolCallStatus.FAILED, error: Py.ExcStr(ex));
        }
        catch (Exception)
        {
        }
        Trace.Record("scheduler_isolated_error", null, "execute", call.name ?? "", ex);
        return Tools.ErrorPayload(ex, call.name ?? "", code);
    }
}
