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

    /// <summary>完成原因(done)。</summary>
    public string FinishReason = "";

    /// <summary>计划(update_plan)。</summary>
    public List<(string Title, string Status)>? Plan;

    /// <summary>本条事件对应的用量快照(usage;对话页的上下文占用环直接读它)。</summary>
    public PyDict? Usage;

    /// <summary>本轮序号(从 1 起)。</summary>
    public int Round;
}

/// <summary>带工具的 AI 会话。构造后调 <see cref="RunAsync"/>;取消走传进来的 CancellationToken。</summary>
public sealed class AiAgent
{
    /// <summary>最多几轮工具调用(防止模型来回刷;照 Windows 版 Agent 的轮数上限口径)。</summary>
    public int MaxRounds { get; set; } = 12;

    /// <summary>是否把工具表带给模型(false 时退化成纯对话)。</summary>
    public bool ToolsEnabled { get; set; } = true;

    /// <summary>对话 id(用量统计、检查点、会话事件都按它分组;照参考版的 session_id)。</summary>
    public string ChatId { get; set; } = "active";

    /// <summary>这一轮是不是子代理跑的(子代理只读、不再派子代理、不重复连 MCP)。</summary>
    public bool IsSubagent { get; set; }

    /// <summary>本轮 id(检查点与事件流按它对轮;每轮开始时生成)。</summary>
    public string TurnId { get; private set; } = "";

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

    /// <summary>系统消息(身份 + 语言规矩 + 运行时上下文 + 用户让 AI 记住的事)。</summary>
    public static PyDict SystemMessage(string instance, string version = "")
    {
        string content = AiData.SystemPrompt() + "\n\n" + RuntimeContext(instance, version);
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

        TurnId = "t" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 4);
        stop_reason = "";
        LastText = "";
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

        string failReason = "";
        try
        {
            for (int round = 1; round <= MaxRounds; round++)
            {
                ct.ThrowIfCancellationRequested();
                if (MaybeCompact(messages, round))
                {
                    failReason = "上下文反复逼近窗口上限,已中断本轮以保护额度";
                    break;
                }
                var state = new RoundState { Round = round };
                await StreamOnceAsync(messages, state, mcpSchemas, ct).ConfigureAwait(false);
                if (state.Failed)
                {
                    failReason = "stream_failed";
                    break;
                }

                // 把 assistant 这一轮的话(可能为空)+ 工具调用追加进对话
                var assistant = PyDict.Of("role", "assistant", "content", state.Text.ToString());
                if (state.Calls.Count > 0)
                {
                    assistant["tool_calls"] = state.Calls.Select(c => c.Raw).ToList();
                }
                messages.Add(assistant);
                LastText = state.Text.ToString();

                if (state.Calls.Count == 0)
                {
                    stop_reason = StopReason.NO_TOOL_CALL;
                    Store.LogEvent(ChatId, "TurnCompleted", PyDict.Of("turn_id", TurnId, "rounds", (long)round, "stop_reason", stop_reason));
                    _emit(new AiAgentEvent { Kind = "done", FinishReason = state.FinishReason, Round = round });
                    return;
                }

                foreach (ToolInvocation invocation in state.Calls)
                {
                    ct.ThrowIfCancellationRequested();
                    _emit(new AiAgentEvent { Kind = "tool", Name = invocation.Call.name, Text = Tools.Label(invocation.Call.name, invocation.Call.args), Call = invocation.Call, Round = round });
                    Store.LogEvent(ChatId, "ToolStarted", PyDict.Of("turn_id", TurnId, "round", (long)round, "tool", invocation.Call.name, "tool_call_id", invocation.Call.id));
                    SnapshotForTool(invocation.Call);
                    string result = await _runner.RunAsync(invocation.Call, ct).ConfigureAwait(false);
                    Store.LogEvent(ChatId, "ToolCompleted", PyDict.Of(
                        "turn_id", TurnId,
                        "round", (long)round,
                        "tool", invocation.Call.name,
                        "tool_call_id", invocation.Call.id,
                        "result_len", (long)(result ?? "").Length,
                        "ok", invocation.Call.status == ToolCallStatus.COMPLETED));
                    _emit(new AiAgentEvent
                    {
                        Kind = "tool_done",
                        Name = invocation.Call.name,
                        Text = Tools.Label(invocation.Call.name, invocation.Call.args),
                        Call = invocation.Call,
                        Round = round,
                    });
                    messages.Add(PyDict.Of(
                        "role", "tool",
                        "tool_call_id", invocation.Call.id,
                        "name", invocation.Call.name,
                        "content", result));
                }
                CompactState.tool_turns_since_compact++;

                _emit(new AiAgentEvent { Kind = "status", Text = "正在整理...", Round = round });
            }

            if (failReason.Length > 0)
            {
                stop_reason = failReason == "stream_failed" ? StopReason.STREAM_FAILED : StopReason.ERROR;
                Store.LogEvent(ChatId, "TurnFailed", PyDict.Of("turn_id", TurnId, "rounds", (long)MaxRounds, "reason", stop_reason));
                _emit(new AiAgentEvent { Kind = "error", Error = failReason, Text = failReason, Round = MaxRounds });
                return;
            }

            // 轮数用尽:让模型收个尾(不带工具,避免又调一轮)
            _emit(new AiAgentEvent { Kind = "status", Text = "步骤太多,正在收尾...", Round = MaxRounds });
            messages.Add(PyDict.Of("role", "system", "content", "工具轮数已达上限。请直接用已有信息回答用户,不要再调用工具。"));
            var tail = new RoundState { Round = MaxRounds + 1, NoTools = true };
            await StreamOnceAsync(messages, tail, mcpSchemas, ct).ConfigureAwait(false);
            stop_reason = tail.Failed ? StopReason.STREAM_FAILED : StopReason.MAX_ROUNDS;
            Store.LogEvent(ChatId, tail.Failed ? "TurnFailed" : "TurnCompleted",
                PyDict.Of("turn_id", TurnId, "rounds", (long)MaxRounds, "reason", stop_reason));
            if (!tail.Failed)
            {
                messages.Add(PyDict.Of("role", "assistant", "content", tail.Text.ToString()));
                LastText = tail.Text.ToString();
                _emit(new AiAgentEvent { Kind = "done", FinishReason = tail.FinishReason, Round = tail.Round });
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
            if (!IsSubagent)
            {
                Mcp.CloseAll(mcpClients);
                _runner.McpClients = new List<McpClient>();
                _runner.Subagent = null;
            }
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

    /// <summary>写操作之前做检查点快照(认不出受影响路径的工具不做,见 Tools.AffectedPaths)。</summary>
    private void SnapshotForTool(ToolCall call)
    {
        try
        {
            if (!Tools.IsWriteTool(call.name))
            {
                return;
            }
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

    /// <summary>摘要输入:把要压的消息铺成「role: content」，必要时截断。</summary>
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
        var sub = new AiAgent(subRunner, _ => { })
        {
            MaxRounds = 4,
            IsSubagent = true,
            ChatId = ChatId + "-sub",
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
            return PyJson.Dumps(PyDict.Of("ok", false, "error", "子代理未得出结论(停止原因: " + sub.stop_reason + ")"), null, ensureAscii: false);
        }
        return PyJson.Dumps(PyDict.Of("ok", true, "answer", answer, "stop_reason", sub.stop_reason), null, ensureAscii: false);
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
        public string FinishReason = "";
        public readonly StringBuilder Text = new();
        public readonly List<ToolInvocation> Calls = new();
    }

    private async Task StreamOnceAsync(List<object> messages, RoundState state, List<object> mcpSchemas, CancellationToken ct)
    {
        var cancel = new HttpCancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.Token.Register(() => cancel.Abort());

        List<object>? tools = state.NoTools || !ToolsEnabled
            ? null
            : Tools.SelectToolSchemas(messages).Concat(mcpSchemas).ToList();

        string error = "";
        string finish = "";
        var pendingTools = new List<object>();

        await Task.Run(() =>
        {
            try
            {
                foreach (PyDict ev in Client.ChatStream(Config.CONFIG.data, messages, tools, 0.3, cancel))
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
                                pendingTools.Add(item!);
                            }
                            break;
                        case "usage":
                        {
                            // provider 给了真值就校准 TokenState，并把这轮的用量记进 Usage(页面画占用环)
                            PyDict usage = ev.Get("usage") is PyDict u ? u : new PyDict();
                            Tokens.UpdateFromUsage(TokenState, usage, messages.Count);
                            Usage.Add(ChatId, usage, Tokens.EstimateMessages(messages), 0);
                            _emit(new AiAgentEvent { Kind = "usage", Round = state.Round, Usage = Usage.Get(ChatId) });
                            break;
                        }
                        case "error":
                            error = Py.S(ev.Get("message"));
                            if (error.Length == 0)
                            {
                                error = "AI 接口出错";
                            }
                            break;
                        case "done":
                            finish = Py.S(ev.Get("finish_reason"));
                            break;
                    }
                    if (error.Length > 0)
                    {
                        break;
                    }
                }
            }
            catch (AIClientError ex)
            {
                if (!linked.IsCancellationRequested) error = ex.Message;
            }
            catch (Exception ex)
            {
                if (!linked.IsCancellationRequested) error = "AI 对话异常:" + Py.ExcStr(ex);
            }
        }, CancellationToken.None).ConfigureAwait(false);

        if (linked.IsCancellationRequested)
        {
            state.Failed = true;
            _emit(new AiAgentEvent { Kind = "error", Error = "", Text = "已停止", Round = state.Round });
            return;
        }
        if (error.Length > 0)
        {
            state.Failed = true;
            // 报错也要记一次用量(这一轮确实花掉了请求)
            Usage.Add(ChatId, null, Tokens.EstimateMessages(messages), 0);
            _emit(new AiAgentEvent { Kind = "error", Error = error, Round = state.Round });
            return;
        }

        state.FinishReason = finish;
        foreach (object? raw in pendingTools)
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
}
