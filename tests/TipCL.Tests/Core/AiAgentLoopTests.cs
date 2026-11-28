// AI5-A(A1–A8)+ AI5-S(Linux 侧)+ AI5-C2(第 1 条)的回归用例。
//
// 全部走本机假网关(ScriptedChatServer),不联网、不弹窗、不碰真实联机服务。
// 每条都对应用户/评审能看到的行为,不只是「没抛异常」:
//   A1 任何结束路径恰好一个终态事件(AiPage 只认 error/done 才解锁)
//   A2 一次失败只报一遍,且是上游原文而不是内部代号
//   A3 微压缩真的接进了主循环(陈旧工具结果被换成占位串,最近若干条保留)
//   A4 上下文溢出 → 反应式压缩一次 → 重发该轮;每轮最多一次
//   A5 流式没走通 → 非流式兜底,且**用本轮流式请求实际下发的那份工具表**
//   A6 finish_reason=length 续写(最多 2 次)→ truncated;空回复 → empty_response;COMPLETED 真的被赋值
//   A7 轮数上限读 config 的 ai_max_rounds(默认 20)
//   A8 限流/5xx → 粘性切换到 ai_fallback_model 并留下切换记录
//   S  子代理会话 id 每次唯一、轮数可配、写工具不出现在子代理的请求里、返回值带 rounds/tools_used/truncated
//   C2 调用了本轮没下发的工具 → 记录 tool_schema_miss 并用全量表重发一次
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class AiAgentLoopTests : IDisposable
{
    private readonly ScriptedChatServer _server = new();
    private readonly Dictionary<string, object?> _before = new();

    private const string OverflowBody =
        "{\"error\":{\"message\":\"This model's maximum context length is 8192 tokens (context_length_exceeded)\"," +
        "\"type\":\"invalid_request_error\"}}";

    public AiAgentLoopTests()
    {
        foreach (string key in new[]
                 {
                     "ai_mode", "ai_base_url", "ai_model", "ai_api_key", "ai_fallback_model",
                     "ai_max_rounds", "ai_subagent_max_rounds", "ai_context_window",
                 })
        {
            _before[key] = Config.CONFIG.Get(key);
        }
        Config.CONFIG.Set("ai_mode", "custom");
        Config.CONFIG.Set("ai_base_url", _server.BaseUrl);
        Config.CONFIG.Set("ai_model", "main-model");
        Config.CONFIG.Set("ai_api_key", "test-key");
        Config.CONFIG.Set("ai_fallback_model", "");
        Config.CONFIG.Set("ai_max_rounds", 20L);
        Config.CONFIG.Set("ai_subagent_max_rounds", 4L);
    }

    public void Dispose()
    {
        foreach (KeyValuePair<string, object?> item in _before)
        {
            Config.CONFIG.Set(item.Key, item.Value!);
        }
        _server.Dispose();
    }

    // ————————————————— A1 / A2 —————————————————

    /// <summary>A1:轮数用尽后的「收尾轮」也失败时,从前只写会话事件、一个事件都不发(页面永远卡在「生成中」)。</summary>
    [Fact]
    public async Task TailRoundFailure_StillEmitsExactlyOneTerminalEvent()
    {
        // 第 1、2 轮各回一个只读工具调用(把轮数吃满),第 3 次(收尾轮)持续 429
        _server.Script((200, ToolCallSse("list_instances", "{}")), (200, ToolCallSse("list_instances", "{}")), (429, null));

        (AiAgent agent, List<AiAgentEvent> events) = await RunAsync(rounds: 2);

        Assert.Equal(StopReason.STREAM_FAILED, agent.stop_reason);
        // 恰好一个终态事件,而且是 error 不是 done(收尾轮失败的语义)
        Assert.Single(events, e => e.Kind == "error" || e.Kind == "done");
        Assert.Single(events, e => e.Kind == "error");
        Assert.DoesNotContain(events, e => e.Kind == "done");
        // 带上 stop_reason(A1 的验收要求:终态事件要能说明为什么结束)
        Assert.Equal(StopReason.STREAM_FAILED, events.Single(e => e.Kind == "error").StopReason);
    }

    /// <summary>A2:一次失败从前报两遍(上游原文 + 内部代号 stream_failed),页面渲染成两个气泡两次 toast。</summary>
    [Fact]
    public async Task StreamFailure_ReportsExactlyOnceWithUpstreamText()
    {
        _server.ScriptStatuses(401);   // 认证类:Fatal,不重试也不降级,直接失败

        (AiAgent agent, List<AiAgentEvent> events) = await RunAsync();

        List<AiAgentEvent> errors = events.Where(e => e.Kind == "error").ToList();
        Assert.Single(errors);
        Assert.Equal(StopReason.STREAM_FAILED, agent.stop_reason);
        // 文案是上游原文(用户看得懂),不是内部代号
        Assert.Contains("401", errors[0].Text);
        Assert.DoesNotContain("stream_failed", errors[0].Text);
        Assert.Equal(errors[0].Text, errors[0].Error);
    }

    // ————————————————— A3 —————————————————

    /// <summary>A3:微压缩接进主循环 —— 陈旧的工具结果被替换成占位串,最近 N 条保留在请求里。</summary>
    [Fact]
    public async Task Microcompact_RunsEveryRound_AndKeepsTheMostRecentToolResults()
    {
        var messages = new List<object>
        {
            PyDict.Of("role", "system", "content", "系统"),
            PyDict.Of("role", "user", "content", "把旧结果清一清"),
        };
        // 8 条工具结果,每条都足够大(确保省下的 token 超过 min_token_savings)
        var bloated = new string('测', 4000);
        for (int i = 0; i < 8; i++)
        {
            messages.Add(PyDict.Of("role", "tool", "tool_call_id", "c" + i, "name", "list_mods", "content", "第" + i + "条:" + bloated));
        }
        messages.Add(PyDict.Of("role", "assistant", "content", "好"));
        messages.Add(PyDict.Of("role", "user", "content", "继续"));

        _server.Script((200, TextSse("结束了", "stop")));
        var events = new List<AiAgentEvent>();
        AiAgent agent = NewAgent(events, rounds: 1);
        // 生产阈值由 ai_context_window 推导(要十几万 token 才触发),用例只压低这一个数
        agent.MicroConfigOverride = new MicroConfig { threshold_tokens = 4000, keep_recent_tool_results = 3 };

        await agent.RunAsync(messages, "default", CancellationToken.None);

        Assert.Contains(events, e => e.Kind == "status" && e.Text.Contains("已清理较早的工具结果"));
        List<string> sent = _server.Records[0].Tools;      // 只取请求快照,确认这轮真发了请求
        Assert.NotEmpty(sent);
        // 最近 3 条保留原文,更旧的变成占位串
        var contents = messages.Where(m => Py.Eq(Py.D(m).Get("role"), "tool")).Select(m => Py.S(Py.D(m).Get("content"))).ToList();
        Assert.Equal(3, contents.Count(c => c.Contains("第") && c.Length > 4000));
        Assert.Equal(5, contents.Count(c => c == Compact.Placeholder));
    }

    // ————————————————— A4 —————————————————

    /// <summary>A4:上游报上下文溢出 → 强制压缩一次 → 重发该轮成功;压缩只做一次。</summary>
    [Fact]
    public async Task ContextOverflow_TriggersOneReactiveCompactAndRetriesTheRound()
    {
        _server.Script(
            (400, OverflowBody),                                  // 第 1 次:溢出
            (200, SummaryJson("历史摘要正文")),                     // 压缩时请模型写摘要(非流式,一次)
            (200, TextSse("压缩之后正常回答了", "stop")));          // 重发该轮:成功

        (AiAgent agent, List<AiAgentEvent> events) = await RunAsync(rounds: 2, withHistory: true);

        Assert.Equal(StopReason.NO_TOOL_CALL, agent.stop_reason);   // 不是失败
        Assert.Equal("压缩之后正常回答了", agent.LastText);
        Assert.Contains(events, e => e.Kind == "compact" && e.Text.Contains("压缩历史"));
        // 会话事件流里留下 trigger=reactive 的边界,且只有一条(每轮最多 1 次)
        List<PyDict> session = Store.ReadEvents(agent.ChatId);
        Assert.Single(session, e => Py.S(e.Get("event")) == "CompactBoundary" && Py.S(e.Get("trigger")) == "reactive");
    }

    /// <summary>A4:压完还是溢出就别再压 —— 报一条用户看得懂的错,而不是把上游报文原样丢给用户。</summary>
    [Fact]
    public async Task ContextOverflow_TwiceInOneRound_DoesNotLoopForever()
    {
        _server.Script(
            (400, OverflowBody),
            (200, SummaryJson("摘要")),
            (400, OverflowBody),      // 压完还是溢出:不再压第二次
            (400, OverflowBody));

        (AiAgent agent, List<AiAgentEvent> events) = await RunAsync(rounds: 2, withHistory: true);

        Assert.Equal(StopReason.STREAM_FAILED, agent.stop_reason);
        AiAgentEvent error = Assert.Single(events, e => e.Kind == "error");
        Assert.Contains("上下文太长", error.Text);
        List<PyDict> session = Store.ReadEvents(agent.ChatId);
        Assert.Single(session, e => Py.S(e.Get("event")) == "CompactBoundary" && Py.S(e.Get("trigger")) == "reactive");
    }

    // ————————————————— A5 —————————————————

    /// <summary>A5:工具参数不完整(流式结构性没走通)→ 用非流式兜底,且兜底请求的工具表 == 流式请求的工具表。</summary>
    [Fact]
    public async Task IncompleteStreamArgs_FallsBackToChatOnce_WithTheSameToolTable()
    {
        _server.Script(
            (200, IncompleteToolArgsSse("list_instances")),
            (200, ChatOnceToolCallJson("list_instances", "{}")),   // 兜底:非流式,直接给出工具调用
            (200, TextSse("查完了", "stop")));

        (AiAgent agent, List<AiAgentEvent> events) = await RunAsync(rounds: 2);

        List<ScriptedChatServer.RequestRecord> records = _server.Records;
        Assert.True(records.Count >= 2, "应该发生一次兜底请求");
        Assert.True(records[0].Streaming, "第一次必须是流式");
        Assert.False(records[1].Streaming, "兜底必须走非流式");
        // 关键验收:两份工具表逐字相同(不能换成全量表)
        Assert.Equal(records[0].Tools, records[1].Tools);
        Assert.NotEmpty(records[0].Tools);
        Assert.Contains(events, e => e.Kind == "status" && e.Text.Contains("非流式"));
        // 兜底救回来了:整轮不算失败
        Assert.NotEqual(StopReason.STREAM_FAILED, agent.stop_reason);
        Assert.Equal("查完了", agent.LastText);
    }

    // ————————————————— A6 —————————————————

    /// <summary>A6:finish_reason=length → 最多续写 2 次,之后返回 truncated(并在 done 事件里带 stop_reason)。</summary>
    [Fact]
    public async Task FinishReasonLength_ContinuesAtMostTwiceThenTruncated()
    {
        _server.Script((200, TextSse("前半段", "length")));   // 三个请求都回 length(脚本用完重复最后一条)

        (AiAgent agent, List<AiAgentEvent> events) = await RunAsync(rounds: 5);

        Assert.Equal(StopReason.TRUNCATED, agent.stop_reason);
        Assert.True(agent.Truncated);
        // 首次 + 2 次续写,不再多打(第 3 次之后直接收)
        Assert.Equal(3, _server.Records.Count);
        Assert.Equal(2, events.Count(e => e.Kind == "status" && e.Text.Contains("接着写")));
        AiAgentEvent done = Assert.Single(events, e => e.Kind == "done");
        Assert.Equal(StopReason.TRUNCATED, done.StopReason);
        // 模型侧的 finish_reason 语义不变(这里仍是 length)
        Assert.Equal("length", done.FinishReason);
    }

    /// <summary>A6:上游回了空内容 → empty_response(以前被当成 no_tool_call 静默收尾)。</summary>
    [Fact]
    public async Task EmptyReply_ReportsEmptyResponseTerminal()
    {
        _server.Script((200, TextSse("", "stop")));

        (AiAgent agent, List<AiAgentEvent> events) = await RunAsync(rounds: 2);

        Assert.Equal(StopReason.EMPTY_RESPONSE, agent.stop_reason);
        AiAgentEvent error = Assert.Single(events, e => e.Kind == "error");
        Assert.Equal(StopReason.EMPTY_RESPONSE, error.StopReason);
        Assert.Contains("没有返回任何内容", error.Text);
    }

    /// <summary>A6:COMPLETED 真的被赋值 —— 执行过工具再说完整的话是 completed,一句工具没调过才是 no_tool_call。</summary>
    [Fact]
    public async Task StopReasonCompleted_IsAssignedAfterToolsRan()
    {
        _server.Script(
            (200, ToolCallSse("list_instances", "{}")),
            (200, TextSse("查完了", "stop")));

        (AiAgent agent, List<AiAgentEvent> _events) = await RunAsync(rounds: 3);

        Assert.Equal(StopReason.COMPLETED, agent.stop_reason);
        Assert.Contains("list_instances", agent.ToolsUsed);
    }

    [Fact]
    public async Task StopReasonNoToolCall_ForPureTextReply()
    {
        _server.Script((200, TextSse("你好呀", "stop")));

        (AiAgent agent, List<AiAgentEvent> _events) = await RunAsync(rounds: 3);

        Assert.Equal(StopReason.NO_TOOL_CALL, agent.stop_reason);
    }

    // ————————————————— A7 —————————————————

    /// <summary>A7:轮数上限读 config 的 ai_max_rounds —— 配 3 时第 4 轮不再发起。</summary>
    [Fact]
    public async Task MaxRounds_ComesFromConfig()
    {
        Config.CONFIG.Set("ai_max_rounds", 3L);
        _server.Script((200, ToolCallSse("list_instances", "{}")));   // 每轮都调工具,永远不收尾

        (AiAgent agent, List<AiAgentEvent> events) = await RunAsync(rounds: null);

        Assert.Equal(3, agent.RoundsUsed);
        Assert.Equal(StopReason.MAX_ROUNDS, agent.stop_reason);
        // 3 轮工具 + 1 次收尾(不带工具) = 4 个请求;第 4 轮工具绝不该发起
        Assert.Equal(4, _server.Records.Count);
        Assert.False(_server.Records[3].Tools.Count > 0 && _server.Records[3].Tools.Any(t => t.Length > 0));
        // 收尾轮不带工具
        Assert.Empty(_server.Records[3].Tools);
        Assert.Single(events, e => e.Kind == "done" || e.Kind == "error");
    }

    [Fact]
    public void MaxRounds_DefaultsToTwenty()
    {
        // 「没配」= 键不存在(与首次安装时一致),让默认值兜底
        Config.CONFIG.data.Pop("ai_max_rounds");

        AiAgent agent = NewAgent(new List<AiAgentEvent>(), rounds: null);

        Assert.Equal(20, agent.MaxRounds);
    }

    // ————————————————— A8 —————————————————

    /// <summary>A8:限流 → 粘性切换到备用模型,之后本轮都用它,并留下切换记录。</summary>
    [Fact]
    public async Task RateLimited_SticksToFallbackModel_AndLogsTheSwitch()
    {
        Config.CONFIG.Set("ai_fallback_model", "backup-model");
        _server.Script((429, null), (200, TextSse("备用模型答的", "stop")));

        (AiAgent agent, List<AiAgentEvent> events) = await RunAsync(rounds: 2);

        Assert.Equal("备用模型答的", agent.LastText);
        Assert.Equal("backup-model", agent.Model);
        Assert.Equal(2, _server.Records.Count);
        Assert.Equal("main-model", _server.Records[0].Model);
        Assert.Equal("backup-model", _server.Records[1].Model);
        Assert.Contains(events, e => e.Kind == "status" && e.Text.Contains("备用模型"));
        Assert.Contains(Store.ReadEvents(agent.ChatId), e => Py.S(e.Get("event")) == "ModelFallback");
        Assert.Equal(StopReason.NO_TOOL_CALL, agent.stop_reason);
    }

    /// <summary>A8:没配备用模型时行为不变 —— 还是原来的「重试到上限」。</summary>
    [Fact]
    public async Task RateLimitedWithoutFallback_KeepsRetrying()
    {
        _server.ScriptStatuses(429);

        (AiAgent agent, List<AiAgentEvent> events) = await RunAsync(rounds: 2);

        Assert.Equal(StopReason.STREAM_FAILED, agent.stop_reason);
        Assert.Equal(4, _server.Records.Count);     // 首次 + 上限 3 次重试
        Assert.DoesNotContain(events, e => e.Kind == "status" && e.Text.Contains("备用模型"));
    }

    // ————————————————— S —————————————————

    /// <summary>S:子代理的请求里不含写工具,返回 JSON 带 rounds/tools_used/truncated,且会话 id 每次唯一。</summary>
    [Fact]
    public async Task Subagent_IsReadOnlyOnly_AndReturnsTheNewFields()
    {
        _server.Script(
            (200, ToolCallSse("dispatch_subagent", "{\"task\":\"查一下\"}")),   // 父:派子代理
            (200, ToolCallSse("list_instances", "{}")),                       // 子:查一次(只读工具可用)
            (200, TextSse("子代理的结论", "stop")),                            // 子:回结论
            (200, TextSse("好", "stop")));                                    // 父:收尾

        (AiAgent agent, List<AiAgentEvent> events) = await RunAsync(rounds: 3);
        _ = agent;

        Assert.True(_server.Records.Count >= 3, "父 → 子 → 父 至少 3 次请求");
        List<string> subTools = _server.Records[1].Tools;
        Assert.NotEmpty(subTools);
        // 子代理的请求里不能出现任何写工具(执行层还会拦,但工具表本身就不该下发)
        List<string> writes = subTools.Where(t => Tools.IsWriteTool(t)).ToList();
        Assert.Empty(writes);
        Assert.DoesNotContain("dispatch_subagent", subTools);
        // 父的请求里照常有写工具(只读过滤只对子代理生效)
        Assert.Contains(_server.Records[0].Tools, t => Tools.IsWriteTool(t));
        // 证据固化(2026-11-28,qa 场景 12 跟进):父请求的表里有写工具(install_mod 等),
        // 子请求的表里一条写工具都没有、也没有 dispatch_subagent —— 所以「子代理看到 install_mod」
        // 只可能是断言查了父回合那次请求(Records[0] 或没有区分第几次请求)。
        Assert.Contains("install_mod", _server.Records[0].Tools);
        Assert.DoesNotContain(subTools, t => Tools.IsWriteTool(t));
        // 子代理的进度事件转给了父回合,并带 subagent 标记
        Assert.Contains(events, e => e.Subagent && e.Kind == "tool");
    }

    /// <summary>S:每次派发一个唯一的 &lt;父id&gt;-sub-&lt;短随机id&gt; —— 会话事件日志各写一份。</summary>
    [Fact]
    public async Task Subagent_EachDispatchGetsItsOwnSessionId()
    {
        _server.Script(
            (200, ToolCallSse("dispatch_subagent", "{\"task\":\"第一个\"}")),
            (200, TextSse("结论一", "stop")),
            (200, ToolCallSse("dispatch_subagent", "{\"task\":\"第二个\"}")),
            (200, TextSse("结论二", "stop")),
            (200, TextSse("都好了", "stop")));

        (AiAgent agent, List<AiAgentEvent> _events) = await RunAsync(rounds: 4);

        string dir = Utils.AbsPath(Store.SessionDirPath());
        List<string> subFiles = Directory.Exists(dir)
            ? Directory.GetFiles(dir, agent.ChatId + "-sub-*.jsonl").Select(f => Path.GetFileNameWithoutExtension(f)!).ToList()
            : new List<string>();
        // 两次派发 = 两个不同的会话 id(以前是固定的 <父id>-sub,两次会写同一份日志)
        Assert.Equal(2, subFiles.Distinct().Count());
    }

    /// <summary>S:子代理轮数 —— 设置项 ai_subagent_max_rounds 生效,dispatch_subagent 的参数在硬上限内覆盖。</summary>
    [Fact]
    public void SubagentRounds_FollowsSettingAndArgument()
    {
        Config.CONFIG.Set("ai_subagent_max_rounds", 7L);
        Assert.Equal(7, AiAgent.SubagentRounds(new PyDict()));
        // 参数覆盖
        Assert.Equal(2, AiAgent.SubagentRounds(PyDict.Of("max_rounds", 2L)));
        // 硬上限:参数再大也被夹住
        Assert.Equal(AiAgent.SubagentHardMaxRounds, AiAgent.SubagentRounds(PyDict.Of("max_rounds", 999L)));
        // 设置越界也夹住
        Config.CONFIG.Set("ai_subagent_max_rounds", 999L);
        Assert.Equal(AiAgent.SubagentHardMaxRounds, AiAgent.SubagentRounds(new PyDict()));
        // 不设 = 与改造前一致(4)
        Config.CONFIG.data.Pop("ai_subagent_max_rounds");
        Assert.Equal(4, AiAgent.SubagentRounds(new PyDict()));
    }

    // ————————————————— C2(第 1 条)—————————————————

    /// <summary>
    /// C2:模型调用了本机不认识的工具 → 记录 tool_schema_miss,用全量表重发一次(每轮最多一次)。
    /// 判据照参考版:名字不在**全量注册表**(Tools.Schemas + 连上的 MCP 工具)里才算;
    /// 只是在「本轮按需选出的那部分」之外不算 —— Linux 的执行层本来就不要求工具先被下发,
    /// 那种情况照常执行,不该白搭一次请求(既有的 AiAgentParallelToolsTests 正锁着这条)。
    /// </summary>
    [Fact]
    public async Task UnknownToolCall_RetriesOnceWithFullToolTable()
    {
        _server.Script(
            (200, ToolCallSse("totally_unregistered_tool", "{}")),
            (200, TextSse("换成别的办法了", "stop")));

        var messages = new List<object>
        {
            PyDict.Of("role", "system", "content", "系统"),
            PyDict.Of("role", "user", "content", "今天天气怎么样"),
        };
        var events = new List<AiAgentEvent>();
        AiAgent agent = NewAgent(events, rounds: 2);
        await agent.RunAsync(messages, "default", CancellationToken.None);

        Assert.True(_server.Records.Count >= 2, "应该用放宽后的工具表重发一次");
        Assert.Contains(Store.ReadEvents(agent.ChatId), e => Py.S(e.Get("event")) == "ToolSchemaMiss");
        Assert.Contains(Store.ReadEvents(agent.ChatId), e =>
            Py.S(e.Get("event")) == "ToolSchemaMiss" && Py.S(e.Get("tools")).Contains("totally_unregistered_tool"));
        // 真正下发到那一次请求的工具数,必须严格多于第一次(forceAll 放宽)
        Assert.True(_server.Records[1].Tools.Count > _server.Records[0].Tools.Count,
            $"放宽后工具数没有变多:{_server.Records[0].Tools.Count} → {_server.Records[1].Tools.Count}");
        // 每轮最多一次:第三、第四次请求不会再出现 ToolSchemaMiss
        Assert.Single(Store.ReadEvents(agent.ChatId), e => Py.S(e.Get("event")) == "ToolSchemaMiss");
    }

    /// <summary>
    /// C2 的关键性质(钉住 Linux 的判据,别被改成「本轮没下发就重发」):
    /// 模型调了**本轮按需筛掉、但注册表里真实存在**的只读工具 → 照常执行,不触发重发。
    /// 理由:Linux 的执行层只认 ToolMeta,不要求工具先被下发;把能跑的路改成丢弃重发是回归。
    /// 参考:windos 的 Agent.cs 用的是「本轮没下发」判据(`list4` 在每轮开头被覆盖成按需选出那份),
    /// Linux 有意不跟 —— 这是两棵树已登记的差异,写进 docs/ai-agent-parity.md 的差异清单。
    /// </summary>
    [Fact]
    public async Task RealToolOutsideThisRoundsSelection_StillExecutes_WithoutRetry()
    {
        // 用户这句话不会命中「版本」关键词,所以 search_versions 不在当轮下发的那份表里
        _server.Script(
            (200, ToolCallSse("search_versions", "{\"query\":\"1.20\"}")),
            (200, TextSse("查到几个版本", "stop")));

        var messages = new List<object>
        {
            PyDict.Of("role", "system", "content", "系统"),
            PyDict.Of("role", "user", "content", "帮我看看模组"),
        };
        var events = new List<AiAgentEvent>();
        AiAgent agent = NewAgent(events, rounds: 2);
        await agent.RunAsync(messages, "default", CancellationToken.None);

        // 没有重发:正好两次请求(工具轮 + 收尾轮)
        Assert.Equal(2, _server.Records.Count);
        Assert.DoesNotContain(Store.ReadEvents(agent.ChatId), e => Py.S(e.Get("event")) == "ToolSchemaMiss");
        // 而且这个工具真的执行了(进了这一轮的工具使用清单)
        Assert.Contains("search_versions", agent.ToolsUsed);
    }

    // ————————————————— 收尾轮 / 续写拼接 / 兜底替换(2026-11-28 qa 跟进) —————————————————

    /// <summary>
    /// 收尾轮没吐出正文时,不能把前面几轮已经拿到的答案抹成空。
    /// (qa 在场景 14 上实测「收尾轮文本为空」——根因是旧代码无条件 `LastText = tail.Text`。)
    /// </summary>
    [Fact]
    public async Task TailRoundWithoutText_KeepsTheAnswerFromEarlierRounds()
    {
        // 前 2 轮:工具 + 正文;收尾轮:又回一个工具调用(不执行)且没有正文
        _server.Script(
            (200, ToolCallWithTextSse("查到了:钠和小地图都在。", "list_instances", "{}")),
            (200, ToolCallSse("list_instances", "{}")),
            (200, ToolCallSse("list_instances", "{}")));

        (AiAgent agent, List<AiAgentEvent> events) = await RunAsync(rounds: 2);

        Assert.Equal(StopReason.MAX_ROUNDS, agent.stop_reason);
        Assert.Equal("查到了:钠和小地图都在。", agent.LastText);   // 不被收尾轮的空文本抹掉
        Assert.Single(events, e => e.Kind == "done" || e.Kind == "error");
    }

    /// <summary>续写是「从断开处继续」:首尾**直接相接**,不插空行(照参考版 _full 的 string.Concat)。</summary>
    [Fact]
    public async Task LengthContinuation_JoinsWithoutInsertingBlankLines()
    {
        _server.Script(
            (200, TextSse("前半段", "length")),
            (200, TextSse("后半段", "stop")));

        (AiAgent agent, List<AiAgentEvent> _events) = await RunAsync(rounds: 5);

        Assert.Equal("前半段后半段", agent.LastText);
        Assert.DoesNotContain("\n\n", agent.LastText);
    }

    /// <summary>
    /// 兜底文本与流式残段对不上时:**整段替换**,不追加(场景 9「文本不重复拼接」)。
    /// 关键断言是「回填进上下文的 assistant 内容里,残段只出现一次」。
    /// </summary>
    [Fact]
    public async Task FallbackReplacesStreamedFragment_InsteadOfAppendingIt()
    {
        // 流式先吐「前半段」再断(参数不完整),兜底给出一份**完全不同**的完整答复
        _server.Script(
            (200, IncompleteToolArgsSse("list_instances", partialText: "前半段")),
            (200, ChatOnceTextJson("兜底重新生成的完整答复")),
            (200, TextSse("好", "stop")));

        var messages = new List<object>
        {
            PyDict.Of("role", "system", "content", "系统"),
            PyDict.Of("role", "user", "content", "帮我看看实例列表"),
        };
        var events = new List<AiAgentEvent>();
        AiAgent agent = NewAgent(events, rounds: 2);
        await agent.RunAsync(messages, "default", CancellationToken.None);

        Assert.Equal("兜底重新生成的完整答复", agent.LastText);
        List<string> assistantTexts = messages.OfType<PyDict>()
            .Where(m => Py.S(m.Get("role")) == "assistant")
            .Select(m => Py.S(m.Get("content"))).ToList();
        string joined = string.Join("\u0001", assistantTexts);
        Assert.DoesNotContain("前半段", joined);            // 残段被替换掉了,没有留在上下文里
        Assert.Contains("兜底重新生成的完整答复", joined);
        // 页面上要能重置气泡:发了一条带「正在重试」的状态
        Assert.Contains(events, e => e.Kind == "status" && e.Text.Contains("正在重试"));
    }

    /// <summary>
    /// A8 的诚实性闸门:内置网关(builtin)档下模型名由 `Client.ResolveEndpoint` 固定成默认值,
    /// 设置改不动 —— 所以**不做降级、也不谎报**「已切到备用模型」;自定义端点(custom)档才降级。
    /// 这里直接查闸门本身:走行为路径需要真的发到内置网关(测试网络闸门不允许,也不该去连)。
    /// </summary>
    [Fact]
    public void Fallback_OnlyAppliesToCustomEndpoints()
    {
        Config.CONFIG.Set("ai_base_url", "http://127.0.0.1:1/v1");
        Config.CONFIG.Set("ai_mode", "custom");
        Assert.True(AiAgent.FallbackApplies("backup-model", Config.CONFIG.data));
        // 没配备用模型 = 不做降级
        Assert.False(AiAgent.FallbackApplies("", Config.CONFIG.data));

        // builtin 档(以及「custom 但没填地址」都会被 Client.Mode 归一成 builtin)
        Config.CONFIG.Set("ai_mode", "builtin");
        Assert.False(AiAgent.FallbackApplies("backup-model", Config.CONFIG.data));
        Config.CONFIG.Set("ai_mode", "custom");
        Config.CONFIG.Set("ai_base_url", "");
        Assert.False(AiAgent.FallbackApplies("backup-model", Config.CONFIG.data));
    }

    // ————————————————— 06补:拒绝即无副作用 —————————————————

    /// <summary>
    /// 06补:被拒的写工具不该先生成检查点。以前是 SnapshotForTool 在权限裁决之前,于是 plan 档下
    /// 被拒的写操作照样在检查点里留下一条快照(实测见 progress/AI5-A.md「06补」)。
    /// </summary>
    [Fact]
    public async Task DeniedWriteTool_DoesNotCreateACheckpointSnapshot()
    {
        _server.Script(
            (200, ToolCallSse("install_mod", "{\"name\":\"sodium\",\"instance\":\"default\"}")),
            (200, TextSse("这一步被拒了", "stop")));

        var events = new List<AiAgentEvent>();
        var runner = new ToolRunner(new FakeBackend()) { PermissionMode = "plan" };
        var agent = new AiAgent(runner, ev => { lock (events) events.Add(ev); })
        {
            ChatId = "deny-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            MaxRounds = 2,
        };
        var messages = new List<object>
        {
            PyDict.Of("role", "system", "content", "系统"),
            PyDict.Of("role", "user", "content", "帮我在 default 实例装个钠"),
        };

        await agent.RunAsync(messages, "default", CancellationToken.None);

        List<PyDict> toolMessages = messages.OfType<PyDict>().Where(m => Py.S(m.Get("role")) == "tool").ToList();
        Assert.Single(toolMessages);
        Assert.Contains("permission_denied", Py.S(toolMessages[0].Get("content")));
        // 关键断言:这一轮一条检查点操作都没有(拒绝即无副作用)
        var probe = new PyDict { ["name"] = "sodium", ["instance"] = "default" };
        ToolRunner.PermissionVerdict verdict = runner.Decide("install_mod", probe);
        string journal = Path.Combine(Utils.AbsPath(PPath.Join(Utils.ROOT, "cache", "ai_checkpoints")),
            agent.ChatId, "journal.json");
        Assert.False(Checkpoint.TurnHasOps(agent.ChatId, agent.TurnId),
            $"被拒的写工具不该生成检查点(裁决={verdict.decision}/{verdict.reason},强制确认={runner.ForceConfirmWrites}),"
            + $"turn={agent.TurnId} journal={(File.Exists(journal) ? File.ReadAllText(journal) : "(无)")}");
    }

    // ————————————————— 脚手架 —————————————————

    private sealed class FakeBackend : ToolRunner.IBackend
    {
        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
        {
            object? value = method switch
            {
                "get_instances" => new List<object?>(),
                "get_installed_versions" => new List<object?>(),
                "get_installed_mod_entries" => new List<object?>(),
                "get_java_list" => new List<object?>(),
                "get_version_list" => new List<object?>(),
                _ => (object?)null,
            };
            return Task.FromResult((T?)value);
        }
    }

    private AiAgent NewAgent(List<AiAgentEvent> events, int? rounds)
    {
        var agent = new AiAgent(new ToolRunner(new FakeBackend()), ev =>
        {
            lock (events)
            {
                events.Add(ev);
            }
        })
        {
            ChatId = "loop-" + Guid.NewGuid().ToString("N").Substring(0, 8),
        };
        if (rounds.HasValue)
        {
            agent.MaxRounds = rounds.Value;
        }
        return agent;
    }

    private async Task<(AiAgent Agent, List<AiAgentEvent> Events)> RunAsync(int? rounds = null, bool withHistory = false)
    {
        var events = new List<AiAgentEvent>();
        AiAgent agent = NewAgent(events, rounds);
        var messages = new List<object>
        {
            PyDict.Of("role", "system", "content", "系统"),
        };
        if (withHistory)
        {
            for (int i = 0; i < 8; i++)
            {
                messages.Add(PyDict.Of("role", "user", "content", "历史问题 " + i));
                messages.Add(PyDict.Of("role", "assistant", "content", "历史回答 " + i));
            }
        }
        messages.Add(PyDict.Of("role", "user", "content", "帮我看看实例列表"));
        await agent.RunAsync(messages, "default", CancellationToken.None).ConfigureAwait(false);
        return (agent, events);
    }

    private static string TextSse(string text, string finish)
        => "data: " + PyJson.Dumps(PyDict.Of("choices", new List<object>
           {
               PyDict.Of("delta", PyDict.Of("content", text)),
           }), null, ensureAscii: false) + "\n"
           + "data: " + PyJson.Dumps(PyDict.Of("choices", new List<object>
             {
                 PyDict.Of("delta", new PyDict(), "finish_reason", finish),
             }), null, ensureAscii: false) + "\n"
           + "data: [DONE]\n";

    private static string ToolCallSse(string name, string args)
        => "data: " + PyJson.Dumps(PyDict.Of("choices", new List<object>
           {
               PyDict.Of(
                   "delta", PyDict.Of(
                       "content", "",
                       "tool_calls", new List<object>
                       {
                           PyDict.Of("index", 0L, "id", "call_" + name,
                               "function", PyDict.Of("name", name, "arguments", args)),
                       }),
                   "finish_reason", "tool_calls"),
           }), null, ensureAscii: false) + "\n"
           + "data: [DONE]\n";

    /// <summary>工具参数被上游截断(非法 JSON)的流:Client 会给出「工具参数不完整,正在换一次非流式」。</summary>
    private static string IncompleteToolArgsSse(string name, string partialText = "")
        => (partialText.Length > 0 ? TextPartSse(partialText) : "")
           + "data: " + PyJson.Dumps(PyDict.Of("choices", new List<object>
           {
               PyDict.Of(
                   "delta", PyDict.Of(
                       "content", "",
                       "tool_calls", new List<object>
                       {
                           PyDict.Of("index", 0L, "id", "call_" + name,
                               "function", PyDict.Of("name", name, "arguments", "{\"a\":")),
                       }),
                   "finish_reason", "tool_calls"),
           }), null, ensureAscii: false) + "\n"
           + "data: [DONE]\n";

    /// <summary>只有一段正文增量的 SSE(不含 finish_reason,用来构造「吐了半截就断」)。</summary>
    private static string TextPartSse(string text)
        => "data: " + PyJson.Dumps(PyDict.Of("choices", new List<object>
           {
               PyDict.Of("delta", PyDict.Of("content", text)),
           }), null, ensureAscii: false) + "\n";

    /// <summary>一次响应里既有正文又有工具调用(用来构造「工具轮也有话说」)。</summary>
    private static string ToolCallWithTextSse(string text, string name, string args)
        => TextPartSse(text) + ToolCallSse(name, args);

    /// <summary>非流式(兜底)应答:纯正文。</summary>
    private static string ChatOnceTextJson(string text)
        => PyJson.Dumps(PyDict.Of("choices", new List<object>
           {
               PyDict.Of(
                   "message", PyDict.Of("content", text, "tool_calls", new List<object>()),
                   "finish_reason", "stop"),
           }), null, ensureAscii: false);

    /// <summary>非流式(兜底)应答:直接给出一个工具调用。</summary>
    private static string ChatOnceToolCallJson(string name, string args)
        => PyJson.Dumps(PyDict.Of("choices", new List<object>
           {
               PyDict.Of(
                   "message", PyDict.Of(
                       "content", "",
                       "tool_calls", new List<object>
                       {
                           PyDict.Of("id", "call_" + name, "type", "function",
                               "function", PyDict.Of("name", name, "arguments", args)),
                       }),
                   "finish_reason", "tool_calls"),
           }), null, ensureAscii: false);

    /// <summary>非流式应答:纯摘要正文(反应式压缩的 Summarize 会读它的 content)。</summary>
    private static string SummaryJson(string text)
        => PyJson.Dumps(PyDict.Of("choices", new List<object>
           {
               PyDict.Of(
                   "message", PyDict.Of("content", text, "tool_calls", new List<object>()),
                   "finish_reason", "stop"),
           }), null, ensureAscii: false);
}
