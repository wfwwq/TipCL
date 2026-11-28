// M4-H P5:AI 会话(Agent)本体 —— 「带工具的一轮对话」在 Linux 侧的落地。
//
// Windows 版把这件事放在 Python 侧(BackendApi 的 ai.chat 流 + Launcher/Ai/Agent.cs 的工具循环,
// 事件是 ai.delta / ai.done / ai.fail / ai.status / tool / tool_run / tool_done),
// Linux 侧没有 Python 桥,于是把这个循环做实现在进程内:**同一批事件名、同一批工具名**,
// 只是换成 C# 的委托回调而不是事件总线。
//
// 一轮的流程照 Windows 版 BackendApi._ai_chat:
//   带工具发起流式请求 → 增量文本(含 <think> 推理段)→ 有 tool_calls 就逐个执行
//   → 把 assistant(tool_calls) 与 tool(结果) 追加进对话 → 再发起下一轮,直到没有工具调用或轮数用尽。
//
// M4-H(AI 服务层)在这一层接上的服务件(名字与参考版一致):
//   · Checkpoint:写操作之前按 Tools.AffectedPaths 做文件快照(配合 Rewind 回退);
//   · Store.LogEvent:TurnStarted / TurnCompleted / TurnFailed 事件流(Rewind 靠它数轮次);
//   · Compact:每轮之前判断要不要压历史(整段摘要 + 微压缩),带熔断;
//   · Tokens / Usage:provider 有 usage 就校准,没有就本地估算,页面据此画占用环;
//   · Mcp:每轮开始按 config 的 ai_mcp_servers 连外部 server,把它们的工具并进工具表;
//   · Artifacts:超长工具结果落盘 + read_artifact 回读;
//   · dispatch_subagent:派一个**只读**子代理跑调研,把结论带回来。
//
// M4-H R7:补上 Windows 版有、Linux 侧原先漏掉的**限流/5xx 重试**(参考版 Agent.cs 的 retry 分支):
// 429 / 500 / 502 / 503 / 504 与分类为 rate_limited 的失败,在这一轮还没产出任何内容时最多重发 3 次
// (退避 min(4, 2^(n-1)) 秒 + 抖动,等待可被「停止」打断);已经吐出正文就绝不重发,免得用户看到重复内容。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;

namespace TipCL.Core.Launcher.Ai;

/// <summary>Agent 发出的事件(页面按 Kind 渲染;名字与 Windows 版的事件名一一对应)。</summary>
public sealed class AiAgentEvent
{
    /// <summary>delta / think / status / tool / tool_done / error / done / usage / compact。</summary>
    public string Kind = "";

    /// <summary>工具名(tool / tool_done)。</summary>
    public string Name = "";

    /// <summary>增量文本(delta)/ 状态文案(status)/ 工具标签(tool)。</summary>
    public string Text = "";

    /// <summary>工具调用(tool / tool_done)。</summary>
    public ToolCall? Call;

    /// <summary>错误文案(error)。</summary>
    public string Error = "";

    /// <summary>完成原因(done):这是**模型的** finish_reason(通常是 "stop"),不是 StopReason。</summary>
    /// <remarks>AI5-A1 新增的 <see cref="StopReason"/> 才是这一轮为什么结束。既有字段含义不动。</remarks>
    public string FinishReason = "";

    /// <summary>AI5-A1:终态事件(error / done)里的结束原因(StopReason 的一组常量)。其它事件为空。</summary>
    public string StopReason = "";

    /// <summary>计划(update_plan)。</summary>
    public List<(string Title, string Status)>? Plan;

    /// <summary>本条事件对应的用量快照(usage;对话页的上下文占用环直接读它)。</summary>
    public PyDict? Usage;

    /// <summary>AI5-S:这条事件来自子代理(tool / tool_done / status / compact)。界面可据此显示「子任务进行中」一行。</summary>
    public bool Subagent;

    /// <summary>本轮序号(从 1 起)。</summary>
    public int Round;
}

/// <summary>带工具的 AI 会话。构造后调 <see cref="RunAsync"/>;取消走传进来的 CancellationToken。</summary>
public sealed class AiAgent
{
    /// <summary>
    /// 最多几轮工具调用(防止模型来回刷;照 Windows 版 Agent 的轮数上限口径)。
    /// AI5-A7:默认与 Windows 对齐(20,见 Defaults.MAX_TOOL_ROUNDS_MAIN);**不显式赋值**时按 config 的
    /// ai_max_rounds 取值(每轮开始时读一次,改设置立刻生效)。默认值从 12 变成 20 是本包有意的行为变化,
    /// 已写进 progress/AI5-A.md。
    /// </summary>
    public int MaxRounds
    {
        get => _maxRounds ?? (int)Math.Max(1L, Py.IntOr(Config.CONFIG.Get("ai_max_rounds"), Defaults.MAX_TOOL_ROUNDS_MAIN));
        set => _maxRounds = Math.Max(1, value);
    }

    private int? _maxRounds;

    /// <summary>组内并行上限(照参考版 Scheduler 的 maxConcurrency=4;设成 1 就退回完全串行)。</summary>
    public int MaxToolConcurrency { get; set; } = Scheduler.DefaultMaxConcurrency;

    /// <summary>是否把工具表带给模型(false 时退化成纯对话)。</summary>
    public bool ToolsEnabled { get; set; } = true;

    /// <summary>对话 id(用量统计、检查点、会话事件都按它分组;照参考版的 session_id)。</summary>
    public string ChatId { get; set; } = "active";

    /// <summary>这一轮是不是子代理跑的(子代理只读、不再派子代理、不重复连 MCP)。</summary>
    public bool IsSubagent { get; set; }

    /// <summary>本轮 id(检查点与事件流按它对轮;每轮开始时生成)。</summary>
    public string TurnId { get; private set; } = "";

    /// <summary>AI5-S:本轮实际用掉的轮数(给 dispatch_subagent 的返回值用)。</summary>
    public int RoundsUsed { get; private set; }

    /// <summary>AI5-S:本轮调用过的工具名(去重、按首次出现顺序;给 dispatch_subagent 的返回值用)。</summary>
    public List<string> ToolsUsed { get; } = new();

    /// <summary>AI5-S:本轮是不是被长度/轮数上限截断(给 dispatch_subagent 的返回值用)。</summary>
    public bool Truncated { get; private set; }

    /// <summary>AI5-A8:本轮实际生效的模型名(空 = 用设置里的主模型;粘性降级后指向备用模型)。</summary>
    public string Model { get; private set; } = "";

    /// <summary>
    /// AI5-A8:粘性降级状态。第一次撞上限流/5xx 时切到 ai_fallback_model,之后本轮所有请求都走它
    /// (照 Windows 版 Agent 的 fallbackActive 口径:切换是粘性的,不来回跳)。
    /// </summary>
    private bool _fallbackActive;

    private string _fallbackModel = "";

    /// <summary>AI5-A6:finish_reason=length 时最多续写几次(照参考版 Agent.cs 的「num &lt; 2」)。</summary>
    private const int MaxContinuations = 2;

    /// <summary>AI5-A6:续写时追加的提醒(照参考版 Agent.cs:998 的文案)。</summary>
    private const string ContinuationPrompt = "请从断开处继续,不要重复。";

    /// <summary>AI5-A6:已经续写了几次。</summary>
    private int _continuations;

    /// <summary>AI5-A6:续写拼起来的正文(续写成功后首尾相接,不重复拼接)。</summary>
    private readonly List<string> _continuationParts = new();

    /// <summary>AI5-S:派子代理的硬上限(用户设置再大也夹在这个范围内,防着一轮子会话把额度跑光)。</summary>
    public const int SubagentHardMaxRounds = Defaults.MAX_TOOL_ROUNDS_MAIN;

    /// <summary>AI5-A2:用户可见文案走词表(中文界面下与改动前逐字相同,英文界面下跟着切换)。</summary>
    private static string T(string zh) => I18nCore.Tr(zh);

    /// <summary>AI5-S:记一次工具使用(去重、按首次出现顺序)。</summary>
    private void NoteToolUsed(string name)
    {
        if (name.Length > 0 && !ToolsUsed.Contains(name))
        {
            ToolsUsed.Add(name);
        }
    }

    /// <summary>结束原因(照参考版 StopReason:completed / no_tool_call / max_rounds / ...)。</summary>
    public string stop_reason { get; private set; } = "";

    /// <summary>本轮最后一段助手文本(子代理把结论带回去时读它)。</summary>
    public string LastText { get; private set; } = "";

    /// <summary>token 状态(provider 基准 + 本地估算)。</summary>
    public TokenState TokenState { get; } = new();

    /// <summary>压缩状态(连续失败 / 快速回填计数)。</summary>
    public CompactState CompactState { get; } = new();

    private readonly ToolRunner _runner;
    private readonly Action<AiAgentEvent> _emit;

    public AiAgent(ToolRunner runner, Action<AiAgentEvent> emit)
    {
        _runner = runner;
        _emit = emit;
    }

    /// <summary>运行时上下文(附在系统提示词后面,照 Windows 版 Tools.RuntimeContext 的作用)。</summary>
    public static string RuntimeContext(string instance, string version = "")
    {
        string def = Py.S(Config.CONFIG.Get("default_instance", "default"));
        return "# 当前状态\n" +
               "- 默认实例: " + (def.Length > 0 ? def : "(无)") + "\n" +
               "- 本次会话实例: " + (instance.Length > 0 ? instance : def) + "\n" +
               "- 本次会话版本: " + (version.Length > 0 ? version : "(未指定)") + "\n" +
               "- 数据目录: " + Utils.ROOT + "\n" +
               "- 时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
    }

    /// <summary>系统消息(身份 + 语言规矩 + 运行时上下文 + 权限档位 + 用户让 AI 记住的事)。</summary>
    public static PyDict SystemMessage(string instance, string version = "")
    {
        string content = AiData.SystemPrompt() + "\n\n" + RuntimeContext(instance, version);
        // 当前权限档位写成一句话告知模型:位置照参考版(Agent.cs:363-372,三条 system 消息的顺序是
        // 身份 / 当前状态 / 权限设置 / 长期记忆)—— Linux 侧合并成一条,所以它是「运行时上下文之后、
        // 长期记忆之前」的独立段落。这句话只是**告知**,裁决仍由 ToolRunner.Decide 说了算。
        string note = ToolRunner.PermissionNote();
        if (note.Length > 0)
        {
            content += "\n\n" + note;
        }
        string memory = AiMemory.Context();
        if (memory.Length > 0)
        {
            content += "\n\n" + memory;
        }
        return PyDict.Of("role", "system", "content", content);
    }

    /// <summary>
    /// 跑一轮完整对话(含内部工具循环)。<paramref name="messages"/> 会被就地追加
    /// assistant / tool 消息,调用方把这同一个列表留着就能继续下一句。
    /// </summary>
    public async Task RunAsync(List<object> messages, string instance, CancellationToken ct = default)
    {
        _runner.Instance = instance;
        if (messages == null)
        {
            throw new ValueError("messages 不能为空");
        }

        // AI5-O:整轮跑在「本会话」的轨迹作用域里,期间记的轨迹行都带上 session,
        // 按会话汇总(诊断导出)时才能把这一会话的轨迹与其它会话分开。窗口/子代理各自有自己的会话 id。
        using IDisposable traceScope = Trace.SessionScope(ChatId);
        double startedAt = Py.Monotonic();
        long toolCalls = 0;
        long toolFailures = 0;

        TurnId = "t" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 4);
        stop_reason = "";
        LastText = "";
        // AI5-A6/S:续写与统计每次 Run 都从零开始(同一个 AiAgent 可能被复用跑多轮)
        _continuationParts.Clear();
        _continuations = 0;
        ToolsUsed.Clear();
        RoundsUsed = 0;
        Truncated = false;
        Model = "";
        _reactiveMessages = messages;   // AI5-A4:反应式压缩要就地替换这份对话
        Checkpoint.BeginChat(ChatId);
        Store.LogEvent(ChatId, "TurnStarted", PyDict.Of("turn_id", TurnId, "user_len", (long)LastUserLength(messages)));

        // MCP:每轮开始按配置连一次外部 server,结束就关掉(照参考版一轮一连接的口径)
        var mcpClients = new List<McpClient>();
        List<object> mcpSchemas = new List<object>();
        if (!IsSubagent)
        {
            try
            {
                mcpClients = Mcp.ConnectServers(Config.CONFIG.data);
                mcpSchemas = Mcp.McpToolSchemas(mcpClients);
            }
            catch (Exception ex)
            {
                Trace.Record("mcp_setup_failed", null, "", "", ex);
            }
            _runner.McpClients = mcpClients;
            _runner.Subagent = RunSubagentAsync;
        }

        // AI5-O:一轮收尾时补一条会话事件(TurnSummary),它是诊断导出里「轮数 / 工具调用数 / 失败数 / 耗时」
        // 的权威来源(字段口径照 Windows 版 Agent.cs 的 TurnSummary)。纯记账,不改任何对话行为。
        void LogTurnSummary(string stopReason, long rounds)
        {
            try
            {
                Store.LogEvent(ChatId, "TurnSummary", PyDict.Of(
                    "turn_id", TurnId,
                    "stop_reason", stopReason ?? "",
                    "rounds", rounds,
                    "tools", toolCalls,
                    "tool_failures", toolFailures,
                    "elapsed", Py.Round(Py.Monotonic() - startedAt, 2)));
            }
            catch (Exception)
            {
            }
        }
        // AI5-A1:整轮只从下面那个出口发终态事件(done / error),每条结束路径都恰好一条。
        // 从前有两处不对:轮数用尽后的收尾轮失败时只写 Store.LogEvent、一个事件都不发(页面永远停在「生成中」);
        // 失败路径又多发一条重复的报错(AI5-A2)。页面只认 error/done 才解锁,漏一条就卡住。
        bool done = false;
        string terminalError = "";
        string terminalFinish = "";
        int lastRound = 0;
        // AI5-A7:轮数上限在读的时候取一次(不显式赋 MaxRounds 时就是 config 的 ai_max_rounds)
        int maxRounds = Math.Max(1, MaxRounds);
        try
        {
            // AI5-A8:本轮开始时把设置里的备用模型读一次(粘性降级用;空 = 没配,不做降级)
            _fallbackActive = false;
            _fallbackModel = Py.Strip(Py.S(Config.CONFIG.Get("ai_fallback_model")));
            Model = "";

            // AI5-A6:「续写」不占轮次(照参考版:续写时轮号不变,最多 2 次),所以轮号手动维护
            int roundNo = 0;
            while (roundNo < maxRounds && !done)
            {
                roundNo++;
                lastRound = roundNo;
                ct.ThrowIfCancellationRequested();
                if (MaybeCompact(messages, roundNo))
                {
                    terminalError = T("上下文反复逼近窗口上限,已中断本轮以保护额度");
                    break;
                }
                // AI5-A3:整段压缩之后再做一遍微压缩(清掉较旧的工具结果,不调模型、不花额度)。
                // 参考版每轮都走这一步(Microcompact 原先只在自检里被调用过,主循环根本没接)。
                Microcompact(messages, roundNo);

                var state = new RoundState { Round = roundNo };
                await StreamOnceAsync(messages, state, mcpSchemas, ct).ConfigureAwait(false);
                // AI5-A5:流式这条路整体没走通(工具参数不全 / 超时 / 重试用尽)→ 用**本轮流式请求实际下发的
                // 那份工具表**再发一次非流式请求兜底,把这一轮救回来;救不回来才按失败语义收尾。
                if (state.Failed && await FallbackOnceAsync(messages, state, mcpSchemas, ct).ConfigureAwait(false))
                {
                    state.Failed = false;
                }
                if (state.Failed)
                {
                    if (state.Cancelled)
                    {
                        // 用户点了「停止」:终态文案是「已停止」(页面按它解锁,不回显成「对话失败」)
                        stop_reason = StopReason.CANCELLED;
                        terminalError = "已停止";
                    }
                    else
                    {
                        // 流式/兜底两条路都没拿到内容 —— 这就是参考版的 stream_failed
                        stop_reason = StopReason.STREAM_FAILED;
                        if (terminalError.Length == 0)
                        {
                            terminalError = state.Error.Length > 0 ? state.Error : T("接口没有返回内容,请稍后重试");
                        }
                    }
                    break;
                }

                // 把 assistant 这一轮的话(可能为空)+ 工具调用追加进对话。
                string full = _continuationParts.Count > 0
                    ? state.Text.ToString().StartsWith(string.Concat(_continuationParts), StringComparison.Ordinal)
                        ? string.Concat(_continuationParts) + state.Text.ToString().Substring(string.Concat(_continuationParts).Length)
                        : string.Concat(_continuationParts) + "\n\n" + state.Text.ToString()
                    : state.Text.ToString();
                var assistant = PyDict.Of("role", "assistant", "content", full);
                if (state.Calls.Count > 0)
                {
                    assistant["tool_calls"] = state.Calls.Select(c => c.Raw).ToList();
                }
                messages.Add(assistant);
                LastText = full;   // 续写过的正文也要整段带回去(子代理的结论走这里)

                if (state.Calls.Count == 0)
                {
                    // AI5-A6:finish_reason=length = 被输出上限截断。先续写(最多 2 次);续写次数用尽、
                    // 或者这一轮干脆没吐出任何增量,就直接按 truncated 收尾(参考版 Agent.cs:991-1006)。
                    if (Py.Eq(state.FinishReason, "length"))
                    {
                        if (_continuations < MaxContinuations && state.Text.Length > 0)
                        {
                            _continuations++;
                            _continuationParts.Add(state.Text.ToString());
                            messages.Add(PyDict.Of("role", "user", "content", ContinuationPrompt));
                            Store.LogEvent(ChatId, "LengthContinuation", PyDict.Of(
                                "turn_id", TurnId, "round", (long)roundNo, "attempt", (long)_continuations));
                            _emit(new AiAgentEvent
                            {
                                Kind = "status",
                                Text = T("回复被长度限制截断了,正在接着写(第 {n}/{total} 次)")
                                    .Replace("{n}", _continuations.ToString())
                                    .Replace("{total}", MaxContinuations.ToString()),
                                Round = roundNo,
                            });
                            roundNo--;                       // 续写不占轮次
                            continue;
                        }
                        Truncated = true;
                        stop_reason = StopReason.TRUNCATED;
                        terminalFinish = state.FinishReason;
                        done = true;
                        break;
                    }
                    // AI5-A6:上游回了空内容(既没正文、也没工具调用)以前被当成 no_tool_call 静默收尾,
                    // 现在给明确的 empty_response,页面提示「模型没返回内容」而不是什么都不显示
                    if (Py.Strip(string.Concat(_continuationParts) + state.Text).Length == 0)
                    {
                        stop_reason = StopReason.EMPTY_RESPONSE;
                        terminalError = T("模型没有返回任何内容,请再试一次");
                        break;
                    }
                    // AI5-A6:让 COMPLETED 真正被赋值 —— 这一轮**执行过工具**再说完整的话,就是完成的正常收尾;
                    // 一句工具都没调过就只回了文字,才是「模型只回了文字」(no_tool_call)。
                    // 这与参考版把 no_tool_call 归一成 completed 的口径一致,同时保住了 Linux 侧
                    // 「纯文字回复 = no_tool_call」的既有语义(场景 1 / 场景 2 的期望正是这两条)。
                    stop_reason = (ToolsUsed.Count > 0 || _continuationParts.Count > 0)
                        ? StopReason.COMPLETED
                        : StopReason.NO_TOOL_CALL;
                    done = true;
                    terminalFinish = state.FinishReason;
                    break;
                }

                // AI-engine-mcp:同一轮的工具调用按 Scheduler 分组 —— 相邻的只读调用并行,写/删/启动/ask_user 单独串行。
                // 事件与对话回填都在本线程按原 tool_call 顺序做:组内先依次发 tool,全部跑完再依次发 tool_done
                // 并追加 tool 消息,所以界面与模型看到的顺序和串行时一样(只是更快)。
                foreach (List<ToolInvocation> group in Scheduler.Group(state.Calls, i => i.Call, MaxToolConcurrency))
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (ToolInvocation invocation in group)
                    {
                        _emit(new AiAgentEvent { Kind = "tool", Name = invocation.Call.name, Text = Tools.Label(invocation.Call.name, invocation.Call.args), Call = invocation.Call, Round = roundNo });
                        Store.LogEvent(ChatId, "ToolStarted", PyDict.Of("turn_id", TurnId, "round", (long)roundNo, "tool", invocation.Call.name, "tool_call_id", invocation.Call.id));
                    }
                    string[] results = await Scheduler.RunGroupAsync(group.Select(i => i.Call).ToList(), call => ExecuteOneAsync(call, ct), MaxToolConcurrency, ct).ConfigureAwait(false);
                    for (int k = 0; k < group.Count; k++)
                    {
                        ToolInvocation invocation = group[k];
                        string result = results[k];
                        // AI5-O:就地累计这一轮的工具调用数/失败数(TurnSummary 用;失败 = 没到 COMPLETED)
                        toolCalls++;
                        if (invocation.Call.status != ToolCallStatus.COMPLETED)
                        {
                            toolFailures++;
                        }
                        Store.LogEvent(ChatId, "ToolCompleted", PyDict.Of(
                            "turn_id", TurnId,
                            "round", (long)roundNo,
                            "tool", invocation.Call.name,
                            "tool_call_id", invocation.Call.id,
                            "result_len", (long)(result ?? "").Length,
                            "ok", invocation.Call.status == ToolCallStatus.COMPLETED));
                        NoteToolUsed(invocation.Call.name);
                        _emit(new AiAgentEvent
                        {
                            Kind = "tool_done",
                            Name = invocation.Call.name,
                            Text = Tools.Label(invocation.Call.name, invocation.Call.args),
                            Call = invocation.Call,
                            Round = roundNo,
                        });
                        messages.Add(PyDict.Of(
                            "role", "tool",
                            "tool_call_id", invocation.Call.id,
                            "name", invocation.Call.name,
                            "content", result));
                    }
                }
                CompactState.tool_turns_since_compact++;

                _emit(new AiAgentEvent { Kind = "status", Text = "正在整理...", Round = roundNo });
            }

            if (!done && terminalError.Length == 0)
            {
                // 轮数用尽:让模型收个尾(不带工具,避免又调一轮)
                _emit(new AiAgentEvent { Kind = "status", Text = "步骤太多,正在收尾...", Round = maxRounds });
                messages.Add(PyDict.Of("role", "system", "content", "工具轮数已达上限。请直接用已有信息回答用户,不要再调用工具。"));
                var tail = new RoundState { Round = maxRounds + 1, NoTools = true };
                await StreamOnceAsync(messages, tail, mcpSchemas, ct).ConfigureAwait(false);
                if (tail.Failed && await FallbackOnceAsync(messages, tail, mcpSchemas, ct).ConfigureAwait(false))
                {
                    tail.Failed = false;
                }
                lastRound = tail.Round;
                RoundsUsed = maxRounds;
                Truncated = true;
                if (tail.Failed)
                {
                    stop_reason = StopReason.STREAM_FAILED;
                    terminalError = tail.Error.Length > 0 ? tail.Error : T("接口没有返回内容,请稍后重试");
                }
                else
                {
                    messages.Add(PyDict.Of("role", "assistant", "content", tail.Text.ToString()));
                    LastText = tail.Text.ToString();
                    done = true;
                    stop_reason = StopReason.MAX_ROUNDS;
                    terminalFinish = tail.FinishReason;
                }
            }
        }
        catch (OperationCanceledException)
        {
            stop_reason = StopReason.CANCELLED;
            Store.LogEvent(ChatId, "TurnFailed", PyDict.Of("turn_id", TurnId, "reason", StopReason.CANCELLED));
            throw;
        }
        finally
        {
            // AI5-S:粘性降级不跨轮泄漏(子代理共用同一个 runner,更要清干净)
            _fallbackActive = false;
            if (!IsSubagent)
            {
                Mcp.CloseAll(mcpClients);
                _runner.McpClients = new List<McpClient>();
                _runner.Subagent = null;
            }
        }

        // AI5-A1:唯一出口 —— 到这里必然发出恰好一条终态事件
        RoundsUsed = Math.Max(RoundsUsed, Math.Min(lastRound, maxRounds));
        string reason = done
            ? stop_reason
            : (stop_reason.Length > 0 ? stop_reason : StopReason.ERROR);
        stop_reason = reason;
        Store.LogEvent(ChatId, done ? "TurnCompleted" : "TurnFailed",
            PyDict.Of("turn_id", TurnId, "rounds", (long)RoundsUsed, "stop_reason", reason));
        // AI5-O(合并方接线):TurnSummary 记账。linux-loop 的 A1 把终态收敛到唯一出口后,
        // 原先散在各 return 点的 LogTurnSummary 调用全部随旧路径消失;这里在唯一出口补回**一次**,
        // 保持 AI5-O「一轮恰好一条 TurnSummary」的口径(诊断导出的轮数/工具数/失败数/耗时靠它)。
        LogTurnSummary(reason, RoundsUsed);
        if (done)
        {
            _emit(new AiAgentEvent { Kind = "done", FinishReason = terminalFinish, StopReason = reason, Round = lastRound });
        }
        else
        {
            // AI5-A2:失败只留这一条(上游原文优先),不再有第二条内部代号 stream_failed 的重复报错
            string message = terminalError.Length > 0 ? terminalError : T("AI 接口出错");
            _emit(new AiAgentEvent { Kind = "error", Error = message, Text = message, StopReason = reason, Round = lastRound });
        }
    }

    private static int LastUserLength(List<object> messages)
    {
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i] is PyDict m && Py.Eq(m.Get("role"), "user"))
            {
                return Py.S(m.Get("content")).Length;
            }
        }
        return 0;
    }

    /// <summary>
    /// 执行一次工具调用(调度器的执行体;只读组里会被并发调用)。
    /// 参数解析失败的调用不执行,直接回 bad_arguments 让模型重发(照参考版 Agent._execute 的 parseErrors 分支;
    /// 此前 Linux 会拿空参数照跑一遍)。写操作永远单独成组,快照不会与别的调用并发。
    /// </summary>
    private async Task<string> ExecuteOneAsync(ToolCall call, CancellationToken ct)
    {
        if (call.status == ToolCallStatus.FAILED && call.error.Length > 0 && call.started_at == 0)
        {
            Trace.Record("tool_bad_args", null, "", call.name, null, null, null, "detail", Py.Slice(call.error, null, 200));
            return Tools.ErrorPayload(new ValueError(call.error), call.name, "bad_arguments");
        }
        // 06补(「拒绝即无副作用」):判成 deny 的写工具连检查点快照都不该做。
        // 以前是先 SnapshotForTool 再判权限,于是 plan 档下被拒的写操作照样在检查点里留下一条快照
        // (实测证据见 progress/AI5-A.md 的 06补 一节)。这里只做**保守前置**:完全按 ToolRunner 的裁决口径
        // 先问一句会不会被拒(免确认放行与强制确认两种例外都不动,交给 RunAsync 原样处理)。
        if (Tools.IsWriteTool(call.name) && !_runner.ForceConfirmWrites && !_runner.IsAllowedAlways(call.name)
            && _runner.Decide(call.name, call.args).decision == "deny")
        {
            Trace.Record("tool_denied_no_snapshot", null, "", call.name, null, null, null,
                "reason", _runner.Decide(call.name, call.args).reason);
            return await _runner.RunAsync(call, ct).ConfigureAwait(false);   // 走它原本的拒绝路径(回 permission_denied)
        }
        SnapshotForTool(call);
        string? text = null;
        try
        {
            text = await _runner.RunAsync(call, ct).ConfigureAwait(false);
            return text;
        }
        finally
        {
            CaptureAfterTool(call, text); // AI3-stability:原地升级执行完补记「新建了哪些版本目录」(差集),见 UpgradeCheckpoint
        }
    }

    /// <summary>AI3-stability:写工具执行完(含失败 / 取消)后补记检查点里的「新建子目录」差集;失败只记追踪,不影响结果。</summary>
    private void CaptureAfterTool(ToolCall call, string? resultText)
    {
        try
        {
            if (Tools.IsWriteTool(call.name))
            {
                UpgradeCheckpoint.CaptureAfter(ChatId, TurnId, call.name, call.args, _runner.Instance, resultText);
            }
        }
        catch (Exception ex)
        {
            Trace.Record("checkpoint_capture_error", null, "", call.name, ex);
        }
    }

    /// <summary>写操作之前做检查点快照(认不出受影响路径的工具不做,见 Tools.AffectedPaths)。</summary>
    private void SnapshotForTool(ToolCall call)
    {
        try
        {
            if (!Tools.IsWriteTool(call.name))
            {
                return;
            }
            // AI2-stability:原地升级新装的版本目录也纳入撤回(只记名单不存内容,见 UpgradeCheckpoint)
            UpgradeCheckpoint.SnapshotFor(ChatId, TurnId, call.name, call.args, _runner.Instance);
            // AI3-content:数据包 / 资源包 / 脚本新建的目录也记一份名单(必须在内容快照之前:撤回时先删文件、再删变空的新目录)
            ContentTools.SnapshotNewDirs(ChatId, TurnId, call.name, call.args, _runner.Instance);
            List<string> paths = Tools.AffectedPaths(call.name, call.args, _runner.Instance);
            if (paths.Count == 0)
            {
                return;
            }
            PyDict snap = Checkpoint.Snapshot(ChatId, TurnId, paths);
            if (!Py.Truthy(snap.Get("ok")))
            {
                // 快照失败不阻断操作,但要让用户知道「这一步退不回去了」
                Trace.Record("checkpoint_failed", null, "", call.name, null, null, null, "reason", Py.S(snap.Get("reason")));
                _emit(new AiAgentEvent { Kind = "status", Text = "这一步的改动无法回退:" + Py.S(snap.Get("reason")) });
            }
        }
        catch (Exception ex)
        {
            Trace.Record("checkpoint_error", null, "", call.name, ex);
        }
    }

    /// <summary>
    /// 上下文接近窗口上限时压缩历史。返回 true 表示「压不动了，本轮该中止」(照参考版的熔断语义)。
    /// </summary>
    private bool MaybeCompact(List<object> messages, int round)
    {
        try
        {
            AutoConfig cfg = Compact.AutoConfigFromSettings(Config.CONFIG.data);
            (bool should, string _) = Compact.ShouldAutocompact(messages, cfg, CompactState, TokenState);
            if (!should)
            {
                return false;
            }
            Compact.CheckRapidRefill(CompactState, cfg);
            _emit(new AiAgentEvent { Kind = "compact", Text = "正在压缩历史...", Round = round });
            CompactionResult result = Compact.CompactConversation(messages, cfg, Summarize);
            messages.Clear();
            messages.AddRange(result.messages);
            Compact.NoteCompactSuccess(CompactState);
            Store.LogEvent(ChatId, "CompactBoundary", PyDict.Of(
                "turn_id", TurnId,
                "round", (long)round,
                "trigger", "auto",
                "pre", result.pre_token_count,
                "post", result.post_token_count,
                "summarized", result.summarized_message_count));
            return false;
        }
        catch (RapidRefillBlocked)
        {
            return true;
        }
        catch (Exception ex)
        {
            Trace.Record("compact_failed", round, "", "", ex);
            return Compact.NoteCompactFailure(CompactState, Compact.AutoConfigFromSettings(Config.CONFIG.data));
        }
    }

    /// <summary>
    /// AI5-A4:反应式压缩(照参考版 _force_compact,Agent.cs:1532-1559)。
    /// 与 <see cref="MaybeCompact"/> 的区别:这里不做「该不该压」的判断(上游已经用溢出错误告诉我们该压了),
    /// 直接压一次;成功换掉对话并重置 token 状态,返回 true 让调用方重发这一轮。
    /// </summary>
    private bool ReactiveCompact(int round)
    {
        try
        {
            AutoConfig cfg = Compact.AutoConfigFromSettings(Config.CONFIG.data);
            _emit(new AiAgentEvent { Kind = "compact", Text = T("上下文超出模型窗口,正在压缩历史..."), Round = round });
            CompactionResult result = Compact.CompactConversation(_reactiveMessages!, cfg, Summarize);
            _reactiveMessages!.Clear();
            _reactiveMessages.AddRange(result.messages);
            Store.LogEvent(ChatId, "CompactBoundary", PyDict.Of(
                "turn_id", TurnId,
                "round", (long)round,
                "trigger", "reactive",
                "pre", result.pre_token_count,
                "post", result.post_token_count,
                "summarized", result.summarized_message_count));
            Compact.NoteCompactSuccess(CompactState);
            Trace.Record("reactive_compact", round, "", "", null, null, null,
                "pre", result.pre_token_count, "post", result.post_token_count);
            return true;
        }
        catch (Exception ex)
        {
            Compact.NoteCompactFailure(CompactState, Compact.AutoConfigFromSettings(Config.CONFIG.data));
            Trace.Record("reactive_compact_failure", round, "", "", ex);
            return false;
        }
    }

    /// <summary>AI5-A4:反应式压缩要就地替换调用方的 messages 列表,所以压之前把引用挂在这里。</summary>
    private List<object>? _reactiveMessages;

    /// <summary>
    /// AI5-A3:微压缩配置的测试接缝。生产路径恒为 null(按设置推导,见 <see cref="MicroConfigFromSettings"/>)。
    /// 存在的理由:微压缩与整段压缩的阈值同源于 ai_context_window,要在用例里只压低微压缩阈值,
    /// 否则得造十几万 token 的历史才能触发一次,用例又慢又脆。
    /// </summary>
    internal MicroConfig? MicroConfigOverride { get; set; }

    /// <summary>把一段历史交给模型摘要(照参考版 _summarize:一次非流式请求)。</summary>
    private string Summarize(List<object> slice)
    {
        var msgs = new List<object>
        {
            PyDict.Of("role", "system", "content", Compact.SummaryPrompt),
            PyDict.Of("role", "user", "content", SummaryInput(slice)),
        };
        PyDict reply = Client.ChatOnce(Config.CONFIG.data, msgs, null, 0.3, new HttpCancel());
        return Py.S(reply.Get("content"));
    }

    // ——————— AI5-A3 ~ A5,A8:压缩 / 兜底 / 降级的接线(以下都在 AiAgent.cs 内部,不改别的文件) ———————

    /// <summary>
    /// AI5-A3:微压缩接线。整段压缩(MaybeCompact)之后每轮再跑一遍 <see cref="Compact.Microcompact"/>:
    /// 把较旧的 tool 结果换成占位串,保留最近若干条。不调模型、不花额度,所以放主循环里每轮都跑。
    /// 照参考版 Agent.cs:747-756 的口径:阈值取「整段压缩阈值 - 16000」(留出富余,别等真的要压了才动手);
    /// idle 传 0 —— Microcompact 只在 idleSeconds &gt; 0 时才按空闲时长触发,而它自己的注释说明 0 表示「不知道空闲多久」,
    /// 靠 threshold_tokens 这条路径触发就够,不会因此把最近的工具结果清掉。
    /// </summary>
    private void Microcompact(List<object> messages, int round)
    {
        try
        {
            MicroConfig cfg = MicroConfigOverride ?? MicroConfigFromSettings(Config.CONFIG.data);
            if (!cfg.threshold_tokens.HasValue)
            {
                return;
            }
            MicroResult result = Compact.Microcompact(messages, cfg, idleSeconds: 0);
            if (!result.changed)
            {
                return;   // disabled / not_triggered / below_min_savings / nothing_to_clear:都不记事件
            }
            messages.Clear();
            messages.AddRange(result.messages);
            _emit(new AiAgentEvent
            {
                Kind = "status",
                Text = T("已清理较早的工具结果({cleared} 条)").Replace("{cleared}", result.cleared_count.ToString()),
                Round = round,
            });
        }
        catch (Exception ex)
        {
            // 微压缩只是省额度,失败不该打断对话
            Trace.Record("microcompact_failed", round, "", "", ex);
        }
    }

    /// <summary>微压缩配置:阈值照参考版(整段压缩阈值减 16000,最少 4000),其余用默认(保留最近 5 条工具结果)。</summary>
    private static MicroConfig MicroConfigFromSettings(PyDict? settings)
    {
        AutoConfig auto = Compact.AutoConfigFromSettings(settings);
        return new MicroConfig
        {
            threshold_tokens = Math.Max(4000L, Compact.AutoThreshold(auto) - 16000),
        };
    }

    /// <summary>AI5-A5:算出这一轮真正要下发的工具表。子代理只读过滤也在这里(兜底复用同一份)。</summary>
    private List<object>? RoundTools(List<object> messages, RoundState state, List<object> mcpSchemas, bool forceAll = false)
    {
        if (state.NoTools || !ToolsEnabled)
        {
            return null;
        }
        List<object> schemas = Tools.SelectToolSchemas(messages, null, forceAll).Concat(mcpSchemas).ToList();
        // AI5-S/C2:子代理在全量重发时也必须过滤写工具(参考版同样如此:plan 档与子代理不给写工具)
        return IsSubagent ? FilterSubagentTools(schemas) : schemas;
    }

    /// <summary>
    /// AI5-S:子代理只读 —— 把写工具从下发的工具表里摘掉。
    /// 以前只靠子执行器的 plan 档在执行时拒绝,工具表本身还是全的:模型会去调写工具,白烧一轮上下文。
    /// 兜底请求也走这一层,所以「子代理的兜底请求里也没有写工具」是同一份表带来的。
    /// </summary>
    private static List<object> FilterSubagentTools(List<object> schemas)
    {
        return schemas.Where(s =>
        {
            string name = Tools.SchemaName(s);
            if (name.Length == 0)
            {
                return false;
            }
            if (name == "dispatch_subagent")
            {
                return false;   // 子代理不许再派子代理
            }
            if (Mcp.IsMcpTool(name))
            {
                return false;   // MCP 工具一律当写工具(与 ToolRunner 的 MCP 裁决同口径)
            }
            ToolMetaInfo? meta = ToolMeta.Get(name);
            return meta != null && meta.@readonly;
        }).ToList();
    }

    /// <summary>AI5-A5:把一次响应里的 tool_calls 转成本轮的调用(参数解析失败的不丢,回 bad_arguments 让模型重发)。</summary>
    private static void AddPendingTools(RoundState state, List<object> pending)
    {
        foreach (object? raw in pending)
        {
            PyDict item = Py.D(raw);
            PyDict function = Py.D(item.Get("function"));
            string name = Py.S(function.Get("name"));
            if (name.Length == 0)
            {
                continue;
            }
            (PyDict? args, string parseError) = Tools.ParseArgs(function.Get("arguments"), name);
            if (args == null)
            {
                // 参数解析不了的调用不丢弃:回一条工具错误给模型,让它重发
                var broken = new ToolCall(Py.S(Py.Or(item.Get("id"), "call_bad")), name, new PyDict());
                state.Calls.Add(new ToolInvocation
                {
                    Raw = new PyDict(item),
                    Call = broken,
                });
                broken.Mark(ToolCallStatus.FAILED, error: parseError);
                continue;
            }
            var call = new ToolCall(Py.S(Py.Or(item.Get("id"), "call_" + state.Calls.Count)), name, args);
            state.Calls.Add(new ToolInvocation { Raw = new PyDict(item), Call = call });
        }
    }

    /// <summary>
    /// AI5-A5:流式这条路是不是「结构性没走通」。命中才做非流式兜底,而不是拿它去吞掉限流/认证这类错误 ——
    /// 那些错误重试或降级后该失败还是要失败,否则会把额度不足掩盖成「重试成功」。
    /// 这三条文案来自 Client.AssembleStream(SSE 装配层自己给出的「换一次非流式」信号):
    /// 参数截断(ArgsComplete=false)、超时、流中断。
    /// </summary>
    private static bool StreamIncomplete(string message)
    {
        return message.Contains("工具参数不完整")
            || message.Contains("接口超时")
            || message.Contains("连接中断");
    }

    /// <summary>
    /// AI5-A5:流式失败后的非流式兜底(照参考版 Agent.cs:882-914 的 ChatOnce 分支)。
    /// 关键要求:兜底请求用**本轮流式请求实际下发的那份工具表**(<see cref="RoundState.Tools"/>,含预设筛选、
    /// on-the-wire 的子代理只读过滤),绝不用全量表;因此也不会出现「兜底时子代理看到写工具」。
    /// 只处理结构性失败(见 <see cref="StreamIncomplete"/>);已经吐过正文的不重发(免得正文重复)。
    /// </summary>
    private async Task<bool> FallbackOnceAsync(List<object> messages, RoundState state, List<object> mcpSchemas, CancellationToken ct)
    {
        if (state.Tools == null && !state.NoTools && ToolsEnabled)
        {
            // 正常情况下 StreamOnceAsync 已经填好;这条只是为了「没填也不能用全量表」兜住
            state.Tools = RoundTools(messages, state, mcpSchemas);
        }
        string upstream = state.Error;
        if (!StreamIncomplete(upstream))
        {
            return false;
        }
        // StreamOnceAsync 只把「最终失败」留在这里,所以这条路最多走一次(它自己内部也有上限)
        if (!TryFallbackModel(null, upstream, state.Round))
        {
            _emit(new AiAgentEvent { Kind = "status", Text = T("流式接口没走通,换成非流式再要一次"), Round = state.Round });
        }
        Store.LogEvent(ChatId, "ChatOnceFallback", PyDict.Of(
            "turn_id", TurnId, "round", (long)state.Round, "error", Py.Slice(upstream, null, 160)));
        Trace.Record("chat_once_fallback", state.Round, "", "", null, null, null, "error", Py.Slice(upstream, null, 120));
        try
        {
            // 兜底请求的工具表 == 本轮流式请求实际下发的工具表(不能换成 Tools.Schemas 全量)
            PyDict reply = await Task.Run(
                () => Client.ChatOnce(RequestSettings(), messages, state.Tools, 0.3, new HttpCancel()),
                ct).ConfigureAwait(false);
            ApplyChatOnce(state, reply);
            state.Failed = false;
            state.Error = "";
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Trace.Record("chat_once_fallback_failed", state.Round, "", "", ex);
            string detail = ex is AIClientError ce ? ce.Message : Py.ExcStr(ex);
            state.Error = StreamIncomplete(detail) ? upstream : detail;
            state.Failed = true;
            return false;
        }
    }

    /// <summary>把非流式响应装进这一轮的状态(正文可能已经流出去一段:只补差量,不重复拼接)。</summary>
    private void ApplyChatOnce(RoundState state, PyDict reply)
    {
        if (reply == null)
        {
            return;
        }
        if (reply.Get("usage") is PyDict usage && usage.Count > 0)
        {
            Tokens.UpdateFromUsage(TokenState, usage, 1);
            Usage.Add(ChatId, usage, Tokens.EstimateMessages(new List<object>()), 0);
            _emit(new AiAgentEvent { Kind = "usage", Round = state.Round, Usage = Usage.Get(ChatId) });
        }
        string text = Py.S(reply.Get("content"));
        if (text.Length > 0)
        {
            if (state.Text.Length == 0)
            {
                state.Text.Append(text);
                _emit(new AiAgentEvent { Kind = "delta", Text = text, Round = state.Round });
            }
            else
            {
                string had = state.Text.ToString();
                if (text.StartsWith(had, StringComparison.Ordinal))
                {
                    string extra = text.Substring(had.Length);
                    if (extra.Length > 0)
                    {
                        state.Text.Append(extra);
                        _emit(new AiAgentEvent { Kind = "delta", Text = extra, Round = state.Round });
                    }
                }
                else
                {
                    // 兜底返回的和已经吐出去的对不上:整段替换,只把差量补出去(界面上的半截收不回来)
                    state.Text.Clear();
                    state.Text.Append(text);
                    _emit(new AiAgentEvent { Kind = "delta", Text = "\n\n" + text, Round = state.Round });
                }
            }
        }
        if (state.Calls.Count == 0)
        {
            AddPendingTools(state, Py.ListOr(reply.Get("tool_calls")));
        }
        string finish = Py.S(reply.Get("finish_reason"));
        if (finish.Length > 0)
        {
            state.FinishReason = finish;
        }
    }

    /// <summary>
    /// AI5-A8:粘性降级 —— 撞上限流/5xx 且设置里配了备用模型时,把本轮剩余请求都切到备用模型
    /// (照参考版 Agent.cs:1568-1585 的 _maybe_fallback)。粘性:只切一次,切完不再来回跳。
    /// </summary>
    private bool TryFallbackModel(AIClientError? err, string message, int round)
    {
        if (!ShouldTryFallback(err, message))
        {
            return false;
        }
        _fallbackActive = true;
        _emit(new AiAgentEvent
        {
            Kind = "status",
            Text = T("主模型暂时不可用,正在改用备用模型 {model}").Replace("{model}", _fallbackModel),
            Round = round,
        });
        Trace.Record("model_fallback", round, "", "", err, null, null, "model", _fallbackModel);
        Store.LogEvent(ChatId, "ModelFallback", PyDict.Of(
            "turn_id", TurnId, "round", (long)round, "model", _fallbackModel,
            "error", Py.Slice(message, null, 160)));
        return true;
    }

    /// <summary>该不该换备用模型(还没切过 + 配上备用模型 + 这次失败是限流/5xx 之类)。</summary>
    private bool ShouldTryFallback(AIClientError? err, string message)
    {
        if (_fallbackModel.Length == 0 || _fallbackActive)
        {
            return false;
        }
        if (err != null && (err.Status is 429 or 500 or 502 or 503 or 504 || err.Category == "rate_limited"))
        {
            return true;
        }
        // 没有分类信息时(比如流里 error 事件、ChatOnce 抛的非 5xx)按文案兜一下
        string text = message ?? "";
        return text.Contains("429") || text.Contains("500") || text.Contains("502") || text.Contains("503")
            || text.Contains("504") || text.Contains("rate limit") || text.Contains("限流")
            || text.Contains("额度") || text.Contains("上游负载");
    }

    /// <summary>摘要输入:把要压的消息铺成「role: content」，必要时截断。</summary>
    /// <summary>
    /// AI5-A8:这一轮真正要下发给接口的设置。粘性降级生效后把 ai_model 换成备用模型,
    /// 并把当前生效的模型记进 <see cref="Model"/>(状态行、日志、用例都读它)。
    /// 只改这份内存配置,不动用户在设置里填的值。
    /// </summary>
    private PyDict RequestSettings()
    {
        PyDict settings = Config.CONFIG.data;
        if (!_fallbackActive || _fallbackModel.Length == 0)
        {
            Model = "";
            return settings;
        }
        settings["ai_model"] = _fallbackModel;
        Model = _fallbackModel;
        return settings;
    }

    private static string SummaryInput(List<object> slice)
    {
        var sb = new StringBuilder();
        foreach (object raw in slice)
        {
            if (raw is not PyDict m)
            {
                continue;
            }
            string role = Py.S(m.Get("role"));
            string content = Py.S(m.Get("content"));
            if (Py.Truthy(m.Get("tool_calls")))
            {
                var names = Py.ListOr(m.Get("tool_calls"))
                    .Select(c => Py.S(Py.DictOr(Tools.DGet(c, "function")).Get("name")))
                    .Where(n => n.Length > 0);
                content += " [调用工具: " + string.Join(",", names) + "]";
            }
            sb.Append(role).Append(": ").Append(content).Append('\n');
        }
        string text = sb.ToString();
        return text.Length > 24000 ? text.Substring(text.Length - 24000) : text;
    }

    /// <summary>
    /// 派一个只读子代理跑调研任务(照参考版的 RunSubagent):
    /// 不给写权限(子执行器用 plan 档)、不许再派子代理、最多 4 轮，结论带回主对话。
    /// </summary>
    private async Task<string> RunSubagentAsync(PyDict args, CancellationToken ct)
    {
        string task = Py.Strip(Py.S(args.Get("task")));
        string context = Py.Strip(Py.S(args.Get("context")));
        string userText = "[子任务] " + task + (context.Length > 0 ? "\n相关背景:" + context : "");
        var subRunner = new ToolRunner(_runner.Api)
        {
            // plan 档 = 只读:子代理只能查，不能改玩家的游戏目录
            PermissionMode = "plan",
            Instance = _runner.Instance,
            McpClients = _runner.McpClients,
        };
        // AI5-S:轮数 = 设置 ai_subagent_max_rounds(默认 4)与 dispatch_subagent 的 max_rounds 参数取夹在硬上限内的值
        int rounds = SubagentRounds(args);
        var sub = new AiAgent(subRunner, ev => ForwardSubagentEvent(ev))
        {
            MaxRounds = rounds,
            IsSubagent = true,
            // AI5-S:每次派发唯一。原来是固定的 <父id>-sub,同一轮派两个子代理会共用一个会话域
            // (压缩状态、检查点、事件日志全串在一起)。
            ChatId = ChatId + "-sub-" + Guid.NewGuid().ToString("N").Substring(0, 6),
        };
        var messages = new List<object>
        {
            SystemMessage(_runner.Instance),
            PyDict.Of("role", "user", "content", userText),
        };
        try
        {
            await sub.RunAsync(messages, _runner.Instance, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Trace.Record("subagent_failed", null, "", "", ex, null, null, "task", Py.Slice(task, null, 100));
            return PyJson.Dumps(PyDict.Of("ok", false, "error", "子代理执行失败: " + Py.ExcStr(ex)), null, ensureAscii: false);
        }
        string answer = sub.LastText;
        if (sub.stop_reason != StopReason.COMPLETED && sub.stop_reason != StopReason.NO_TOOL_CALL && Py.Strip(answer).Length == 0)
        {
            return PyJson.Dumps(PyDict.Of(
                "ok", false,
                "error", "子代理未得出结论(停止原因: " + sub.stop_reason + ")",
                "stop_reason", sub.stop_reason,
                "rounds", (long)sub.RoundsUsed,
                "tools_used", new List<object>(sub.ToolsUsed),
                "truncated", sub.Truncated), null, ensureAscii: false);
        }
        // AI5-S:原有字段不动,新增 rounds / tools_used / truncated
        return PyJson.Dumps(PyDict.Of(
            "ok", true,
            "answer", answer,
            "stop_reason", sub.stop_reason,
            "rounds", (long)sub.RoundsUsed,
            "tools_used", new List<object>(sub.ToolsUsed),
            "truncated", sub.Truncated), null, ensureAscii: false);
    }

    /// <summary>AI5-S:子代理轮数默认值(与改造前一致)。</summary>
    private const int SubagentDefaultRounds = Defaults.MAX_TOOL_ROUNDS_SUBAGENT;

    /// <summary>
    /// AI5-S:子代理轮数 —— 设置项 ai_subagent_max_rounds(默认 4)为基线,dispatch_subagent 的
    /// max_rounds 参数可覆盖,但一律夹在 [1, SubagentHardMaxRounds] 内(硬上限挡着,参数再大也跑不飞)。
    /// </summary>
    internal static int SubagentRounds(PyDict args)
    {
        int configured = (int)Py.IntOr(Config.CONFIG.Get("ai_subagent_max_rounds"), SubagentDefaultRounds);
        int rounds = Math.Clamp(configured, 1, SubagentHardMaxRounds);
        object? raw = args.Get("max_rounds");
        if (raw != null && Py.Truthy(raw))
        {
            rounds = Math.Clamp((int)Py.IntOr(raw, rounds), 1, SubagentHardMaxRounds);
        }
        return rounds;
    }

    /// <summary>AI5-S:把子代理的状态/工具事件转给父回合(带 subagent 字段,界面据它显示「子任务进行中」)。</summary>
    private void ForwardSubagentEvent(AiAgentEvent ev)
    {
        // 只转状态与工具进度:子代理的正文增量丢掉(父回合只用它的最终结论,照参考版口径)
        if (ev.Kind != "status" && ev.Kind != "tool" && ev.Kind != "tool_done" && ev.Kind != "compact")
        {
            return;
        }
        _emit(new AiAgentEvent
        {
            Kind = ev.Kind,
            Name = ev.Name,
            Text = ev.Text,
            Call = ev.Call,
            Round = ev.Round,
            Subagent = true,
        });
    }

    /// <summary>一轮工具调用的原始形状(要原样回填进 assistant.tool_calls)。</summary>
    private sealed class ToolInvocation
    {
        public PyDict Raw = new();
        public ToolCall Call = null!;
    }

    private sealed class RoundState
    {
        public int Round;
        public bool NoTools;
        public bool Failed;

        /// <summary>AI5-A2:用户点了「停止」——终态是「已停止」,不是接口报错。</summary>
        public bool Cancelled;

        /// <summary>AI5-A2/A5:这一轮最终失败时的报文(只由唯一出口发一条 error 事件)。</summary>
        public string Error = "";

        /// <summary>AI5-A5:这一轮流式请求**实际下发**的工具表(授权过筛 + MCP 合并 + 子代理只读过滤);
        /// 空表示这轮不带工具。非流式兜底必须原样用这一份,不能改用全量表。</summary>
        public List<object>? Tools;

        public string FinishReason = "";
        public readonly StringBuilder Text = new();
        public readonly List<ToolInvocation> Calls = new();
    }

    /// <summary>一轮流式请求失败后最多原地重发几次(照参考版 Agent.cs 的 num4 &lt; 3)。</summary>
    private const int MaxStreamRetries = 3;

    /// <summary>一次流式请求的结果(重试的最小单元:增量直接吐给 state,这里只带回错误与本轮的 tool_calls)。</summary>
    private sealed class StreamAttempt
    {
        public string Error = "";
        public AIClientError? ClientError;
        public string Finish = "";
        public readonly List<object> PendingTools = new();
    }

    /// <summary>
    /// 重试前的退避秒数:min(4, 2^(n-1)) 再乘 0.8~1.2 的抖动
    /// (照参考版 _sleep_backoff:第 1 次约 1 秒、第 2 次约 2 秒、第 3 次约 4 秒,有上限、不会无限等)。
    /// </summary>
    private static double BackoffSeconds(int attempt)
    {
        return Math.Min(4.0, Math.Pow(2.0, attempt - 1)) * (0.8 + 0.4 * Random.Shared.NextDouble());
    }

    /// <summary>
    /// 这次失败能不能原地重发(照参考版 Agent.cs 的 retry 分支):
    ///   · 401/403 认证类(Fatal)一次都不重试;
    ///   · 只有 Retryable 的错误才重试:429 / 500 / 502 / 503 / 504,以及分类为 rate_limited 的;
    ///     provider_timeout / provider_network_error / provider_stream_error 也算可重试(与参考版 Retryable 口径一致);
    ///   · **这一轮已经吐出正文/思考内容或拿到 tool_calls 就绝不重发** —— 重发会让用户看到重复内容;
    ///   · 上下文超长、参数错、未知错误都不是 Retryable,照旧直接失败。
    /// </summary>
    private bool CanRetryStream(AIClientError? err, RoundState state, StreamAttempt attempt)
    {
        if (err == null || err.Fatal() || !err.Retryable())
        {
            return false;
        }
        return state.Text.Length == 0 && state.Calls.Count == 0 && attempt.PendingTools.Count == 0;
    }

    /// <summary>重试说明(给状态行/日志看,别让界面静默卡住)。</summary>
    private static string RetryNotice(AIClientError err, int attempt, double waitSeconds)
    {
        string why = err.Status > 0
            ? "HTTP " + err.Status
            : err.Category switch
            {
                "rate_limited" => "限流",
                "provider_timeout" => "超时",
                "provider_network_error" => "网络异常",
                "provider_stream_error" => "连接中断",
                _ => "出错",
            };
        return "接口" + why + ",正在重试(第 " + attempt + "/" + MaxStreamRetries + " 次,约 " +
               waitSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " 秒后)";
    }

    /// <summary>
    /// 发一轮流式请求;限流(429)与 5xx 这类「重发一次就好」的失败会退避重试(参考版有、Linux 侧原先漏了这一层)。
    /// 重试有次数上限,退避期间随时能被 CancellationToken 打断(用户点「停止」立刻断,不等完)。
    /// </summary>
    private async Task StreamOnceAsync(List<object> messages, RoundState state, List<object> mcpSchemas, CancellationToken ct)
    {
        var cancel = new HttpCancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.Token.Register(() => cancel.Abort());

        // AI5-A5:这一轮实际下发的工具表算一次就固定下来 —— 流式请求与随后的非流式兜底必须用同一份
        // (子代理只读过滤也在里面),否则兜底时上下文会突然变大、子代理还能看到写工具。
        List<object>? tools = RoundTools(messages, state, mcpSchemas);
        state.Tools = tools;

        int retries = 0;
        // AI5-A4:这一轮还剩几次「反应式压缩后重发」的额度(每轮最多 1 次,照参考版的 flag6)
        int reactiveBudget = 1;
        // AI5-C2:tool_schema_miss 全量重发每轮最多 1 次(防死循环)
        bool schemaMissRetried = false;
        while (true)
        {
            // 这一轮尝试开始时的已生成文本长度:流断在半途、稍后整轮重试时,必须回到这个位置再重写,
            // 否则「前半段 + 重试后的完整回答」会一起进 state.Text —— 既污染落盘消息,也污染下一轮上下文
            // (界面上已吐出的增量收不回来,那部分属于 UI 侧,见报告跨域表)。
            int textBase = state.Text.Length;
            StreamAttempt attempt = await StreamAttemptAsync(messages, state, tools, cancel, linked).ConfigureAwait(false);

            if (linked.IsCancellationRequested)
            {
                // AI5-A2:取消也走唯一出口 —— 这里只留状态,事件由 RunAsync 收尾时发一条「已停止」
                state.Failed = true;
                state.Error = "";
                state.Cancelled = true;
                return;
            }
            if (attempt.Error.Length > 0)
            {
                // AI5-A8:撞上限流/5xx 且配了备用模型 → **先**换模型再试(不能排在重试上限之后:
                // 那 3 次重试会把同一个限流错误重发 3 遍,兜底模型永远轮不到)。
                // 粘性:切一次之后本轮都用它,不会来回跳。
                bool fallbackNow = ShouldTryFallback(attempt.ClientError, attempt.Error);
                if (fallbackNow && TryFallbackModel(attempt.ClientError, attempt.Error, state.Round))
                {
                    retries = 0;
                    continue;
                }
                if (!fallbackNow && retries < MaxStreamRetries && CanRetryStream(attempt.ClientError, state, attempt))
                {
                    retries++;
                    double wait = BackoffSeconds(retries);
                    // 让界面/日志看得见「限流了,正在重试第 N 次」,而不是静默卡住
                    _emit(new AiAgentEvent
                    {
                        Kind = "status",
                        Text = RetryNotice(attempt.ClientError!, retries, wait),
                        Round = state.Round,
                    });
                    Store.LogEvent(ChatId, "ModelRetry", PyDict.Of(
                        "turn_id", TurnId,
                        "round", (long)state.Round,
                        "attempt", (long)retries,
                        "category", attempt.ClientError!.Category,
                        "error", Py.Slice(attempt.Error, null, 160)));
                    Trace.Record("retry", state.Round, "", "", attempt.ClientError, null, null,
                        "attempt", (long)retries, "wait", Math.Round(wait, 1));
                    // 退避 + 尊重取消:取消时 Task.Delay 直接抛 OperationCanceledException,不会睡满
                    await Task.Delay(TimeSpan.FromSeconds(wait), ct).ConfigureAwait(false);
                    // 重试前把本轮已经吐进 state.Text 的半截文本丢掉,避免重复
                    state.Text.Length = textBase;
                    continue;
                }
                // AI5-A4:上下文溢出 = 反应式压缩。强制压一次(不调模型的判断,直接压),压成功就重发这一轮;
                // 每轮最多 1 次(压完还溢出就照失败报错,不再无限压 —— 见 reactiveBudget)。
                if (Client.IsContextOverflow(attempt.Error))
                {
                    if (reactiveBudget > 0 && ReactiveCompact(state.Round))
                    {
                        reactiveBudget--;
                        state.Text.Length = textBase;   // 溢出的那一轮白吐的增量不要留在上下文里
                        retries = 0;
                        continue;
                    }
                    if (reactiveBudget == 0)
                    {
                        // 已经压过一次还是溢出:把话说清楚,别让用户看到一句看不懂的上游报文
                        state.Error = T("上下文太长,压缩一次后仍然超出模型窗口,请开新对话或减少附件");
                    }
                }
                // AI5-A2:这里不再自己发 error 事件 —— 从前这里发一条(上游原文)、RunAsync 收尾又发一条
                // (内部代号 stream_failed),页面渲染成两个气泡 + 两次 toast。现在只把报文带回去,
                // 由唯一出口发**恰好一条**用户能看懂的 error。
                state.Failed = true;
                state.Error = state.Error.Length > 0 ? state.Error : attempt.Error;
                // 报错也要记一次用量(这一轮确实花掉了请求)
                Usage.Add(ChatId, null, Tokens.EstimateMessages(messages), 0);
                return;
            }

            state.FinishReason = attempt.Finish;
            AddPendingTools(state, attempt.PendingTools);

            // AI5-C2(第 1 条):模型调用了**本机根本不认识**的工具 → 用全量表重发一次(每轮最多 1 次)。
            // 判据与参考版 Agent.cs:959-977 逐条对齐:那里 declared 取的是 Tools.TOOL_SCHEMAS **全量注册表**
            // (不是本轮按需选出来的那份),所以只有「注册表里没有这个名字」才触发(比如插件/MCP 侧新出现的工具)。
            // 这与 linux-memperm 的 ToolRunner.IsUnknownTool 是同一类问题、两个层次:它按执行结果判 unknown_tool
            // (那需要它的 ToolCall.code 修复),这里按「名字在不在注册表」在**发起执行之前**判,不依赖该修复。
            // 不执行那些调用,先把工具表放宽再问一次,否则这一轮只会拿到一串「没有这个工具」。
            if (!schemaMissRetried && HasUndeclaredTools(state, mcpSchemas))
            {
                schemaMissRetried = true;
                List<object> upgraded = RoundTools(messages, state, mcpSchemas, forceAll: true);
                state.Tools = upgraded;
                tools = upgraded;
                state.Calls.Clear();
                state.Text.Clear();
                string missing = string.Join(",", attempt.PendingTools
                    .Select(t => Py.S(Py.DictOr(Tools.DGet(t, "function")).Get("name")))
                    .Where(n => n.Length > 0 && !AllKnownToolNames(mcpSchemas).Contains(n)));
                Trace.Record("tool_schema_miss", state.Round, "", "", null, null, null, "tools", missing);
                Store.LogEvent(ChatId, "ToolSchemaMiss", PyDict.Of(
                    "turn_id", TurnId, "round", (long)state.Round, "tools", missing));
                continue;   // 用放宽后的工具表重发这一轮
            }
            return;
        }
    }

    /// <summary>本机认识的全部工具名(注册表 + 本轮连上的 MCP 工具)。</summary>
    private static HashSet<string> AllKnownToolNames(List<object> mcpSchemas)
    {
        var known = new HashSet<string>(Tools.Schemas.Select(Tools.SchemaName), StringComparer.Ordinal);
        foreach (object schema in mcpSchemas ?? new List<object>())
        {
            string name = Tools.SchemaName(schema);
            if (name.Length > 0)
            {
                known.Add(name);
            }
        }
        return known;
    }

    /// <summary>AI5-C2:这轮返回的 tool_call 里有没有「本机根本不认识的工具」。</summary>
    private static bool HasUndeclaredTools(RoundState state, List<object> mcpSchemas)
    {
        HashSet<string> known = AllKnownToolNames(mcpSchemas);
        return state.Calls.Any(c => c.Call.name.Length > 0 && !known.Contains(c.Call.name));
    }

    /// <summary>
    /// 发一次流式请求:增量文本 / 用量事件照旧直接吐给 state,
    /// 只把「这轮的错误 + tool_calls + 结束原因」带回去,交 <see cref="StreamOnceAsync"/> 决定要不要重试。
    /// </summary>
    private async Task<StreamAttempt> StreamAttemptAsync(List<object> messages, RoundState state, List<object>? tools, HttpCancel cancel, CancellationTokenSource linked)
    {
        var attempt = new StreamAttempt();
        // AI5-A8:粘性降级生效后,本轮剩余请求都走备用模型(照参考版把模型换成 fallbackModel 的口径)
        PyDict settings = RequestSettings();

        await Task.Run(() =>
        {
            try
            {
                foreach (PyDict ev in Client.ChatStream(settings, messages, tools, 0.3, cancel))
                {
                    if (linked.IsCancellationRequested)
                    {
                        break;
                    }
                    string type = Py.S(ev.Get("type"));
                    switch (type)
                    {
                        case "delta":
                        {
                            string text = Py.S(ev.Get("text"));
                            if (text.Length > 0)
                            {
                                state.Text.Append(text);
                                _emit(new AiAgentEvent
                                {
                                    Kind = text.StartsWith("<think>", StringComparison.Ordinal) || text.StartsWith("</think>", StringComparison.Ordinal) ? "think" : "delta",
                                    Text = text,
                                    Round = state.Round,
                                });
                            }
                            break;
                        }
                        case "tool_calls":
                            foreach (object? item in Py.ListOr(ev.Get("tool_calls")))
                            {
                                attempt.PendingTools.Add(item!);
                            }
                            break;
                        case "usage":
                        {
                            // provider 给了真值就校准 TokenState，并把这轮的用量记进 Usage(页面画占用环)
                            PyDict usage = ev.Get("usage") is PyDict u ? u : new PyDict();
                            Tokens.UpdateFromUsage(TokenState, usage, messages.Count);
                            Usage.Add(ChatId, usage, Tokens.EstimateMessages(messages), 0);
                            // AI5-O:provider 的真值顺带落一条会话事件(字段口径照 Windows 版 Agent.cs 的 Usage 事件)。
                            // 内存里的 Usage 表只活到本进程结束,重启后诊断导出就读不到 token 了,落盘才留得住。
                            long promptTokens = Tokens.UsageInt(usage, "prompt_tokens", "input_tokens");
                            long completionTokens = Tokens.UsageInt(usage, "completion_tokens", "output_tokens");
                            if (promptTokens > 0 || completionTokens > 0)
                            {
                                PyDict usageDetails = Py.D(Py.Or(usage.Get("prompt_tokens_details"), new PyDict()));
                                Store.LogEvent(ChatId, "Usage", PyDict.Of(
                                    "turn_id", TurnId,
                                    "round", (long)state.Round,
                                    "prompt_tokens", promptTokens,
                                    "completion_tokens", completionTokens,
                                    "cached_tokens", Py.Int(Py.Or(usageDetails.Get("cached_tokens"), 0L)),
                                    "source", "provider"));
                            }
                            _emit(new AiAgentEvent { Kind = "usage", Round = state.Round, Usage = Usage.Get(ChatId) });
                            break;
                        }
                        case "error":
                            attempt.Error = Py.S(ev.Get("message"));
                            if (attempt.Error.Length == 0)
                            {
                                attempt.Error = "AI 接口出错";
                            }
                            // 参考版把流里的 error 事件包成 AIClientError 抛出来;这里同样包一层,
                            // 让「额度不足 / rate limit」这类没有 HTTP 状态码的错误也能按分类决定要不要重试。
                            attempt.ClientError = new AIClientError(attempt.Error);
                            break;
                        case "done":
                            attempt.Finish = Py.S(ev.Get("finish_reason"));
                            break;
                    }
                    if (attempt.Error.Length > 0)
                    {
                        break;
                    }
                }
            }
            catch (AIClientError ex)
            {
                if (!linked.IsCancellationRequested)
                {
                    attempt.Error = ex.Message;
                    // 保留带状态码/分类的原异常:429 与 5xx 靠它走重试分支
                    attempt.ClientError = ex;
                }
            }
            catch (Exception ex)
            {
                if (!linked.IsCancellationRequested) attempt.Error = "AI 对话异常:" + Py.ExcStr(ex);
            }
        }, CancellationToken.None).ConfigureAwait(false);

        return attempt;
    }
}
