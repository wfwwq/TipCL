// M4-H（AI 服务层）测试:检查点回滚 / 上下文压缩 / 令牌与用量 / 会话存档 / 产物回读 / MCP 配置。
//
// 全部是纯逻辑或临时目录内的读写,不联网、不碰真实用户数据(TestIsolation 已把 TIPCL_HOME 指到临时目录)。
// 会落盘的四件(Checkpoint / Artifacts / Trace / Store 的事件流)在 AiServiceDirs 里各自指到临时子目录，
// 用完复原，避免测试之间互相干扰。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

/// <summary>给会落盘的服务件指一套临时目录，Dispose 时复原。</summary>
internal sealed class AiServiceDirs : IDisposable
{
    private readonly string _root;
    private readonly string? _checkpoints = Checkpoint.CheckpointsDir;
    private readonly string? _artifacts = Artifacts.ArtifactsDir;
    private readonly string? _trace = Trace.TraceDir;
    private readonly string? _sessions = Store.SessionsDir;
    private readonly string? _storeFile = Store.StoreFileOverride;

    public AiServiceDirs()
    {
        _root = Path.Combine(Path.GetTempPath(), "tipcl-ai-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Checkpoint.CheckpointsDir = Path.Combine(_root, "checkpoints");
        Artifacts.ArtifactsDir = Path.Combine(_root, "results");
        Trace.TraceDir = Path.Combine(_root, "trace");
        Store.SessionsDir = Path.Combine(_root, "sessions");
        Store.StoreFileOverride = Path.Combine(_root, "ai_chats.json");
        Directory.CreateDirectory(Checkpoint.CheckpointsDir);
        Directory.CreateDirectory(Artifacts.ArtifactsDir);
        Directory.CreateDirectory(Trace.TraceDir);
        Directory.CreateDirectory(Store.SessionsDir);
    }

    public string Root => _root;

    public void Dispose()
    {
        Checkpoint.CheckpointsDir = _checkpoints;
        Artifacts.ArtifactsDir = _artifacts;
        Trace.TraceDir = _trace;
        Store.SessionsDir = _sessions;
        Store.StoreFileOverride = _storeFile;
        try { Directory.Delete(_root, true); } catch (Exception) { }
    }
}

public class TokensTests
{
    [Fact]
    public void EstimateTextCountsCjkAsOneAndAsciiAsQuarter()
    {
        Assert.Equal(0, Tokens.EstimateText(""));
        // "你好" = 2 个 CJK ≈ 2 token + 1
        Assert.Equal(3, Tokens.EstimateText("你好"));
        // "abcd" = 4 * 0.25 = 1 + 1
        Assert.Equal(2, Tokens.EstimateText("abcd"));
        Assert.True(Tokens.EstimateText("中文中文中文") > Tokens.EstimateText("abcdefgh"));
    }

    [Fact]
    public void EstimateMessagesAddsOverheadAndToolCalls()
    {
        var plain = new List<object> { PyDict.Of("role", "user", "content", "abcd") };
        Assert.Equal(2 + 4, Tokens.EstimateMessages(plain));

        var withTools = new List<object>
        {
            PyDict.Of(
                "role", "assistant",
                "content", "",
                "tool_calls", new List<object>
                {
                    PyDict.Of("type", "function", "function", PyDict.Of("name", "list_mods", "arguments", "{}")),
                }),
        };
        Assert.True(Tokens.EstimateMessages(withTools) > 4);
        Assert.Equal(0, Tokens.EstimateMessages(null));
    }

    [Fact]
    public void UpdateFromUsageCalibratesAndCurrentInputAddsNewMessagesOnly()
    {
        var st = new TokenState();
        var messages = new List<object> { PyDict.Of("role", "user", "content", "hello world") };
        Assert.Equal("estimate", st.source);

        Tokens.UpdateFromUsage(st, PyDict.Of("prompt_tokens", 1000L, "completion_tokens", 50L,
            "prompt_tokens_details", PyDict.Of("cached_tokens", 800L)), messages.Count);
        Assert.Equal("provider_usage", st.source);
        Assert.Equal(1000, st.base_tokens);
        Assert.Equal(800, st.cache_read);
        Assert.Equal(50, st.last_output_tokens);
        Assert.Equal(1000, Tokens.CurrentInputTokens(messages, st));

        messages.Add(PyDict.Of("role", "assistant", "content", "hi"));
        Assert.True(Tokens.CurrentInputTokens(messages, st) > 1000);
    }

    [Fact]
    public void UpdateFromUsageAcceptsAnthropicKeys()
    {
        var st = new TokenState();
        Tokens.UpdateFromUsage(st, PyDict.Of("input_tokens", 321L, "output_tokens", 9L), 0);
        Assert.Equal(321, st.base_tokens);
        Assert.Equal("provider_usage", st.source);
    }
}

public class CompactTests
{
    private static List<object> Conversation(int userTurns, int toolsPerTurn = 0, int padChars = 0)
    {
        string pad = padChars > 0 ? new string('中', padChars) : "";
        var msgs = new List<object> { PyDict.Of("role", "system", "content", "sys") };
        for (int i = 0; i < userTurns; i++)
        {
            msgs.Add(PyDict.Of("role", "user", "content", "用户第 " + i + " 句" + pad, "id", "u" + i));
            msgs.Add(PyDict.Of("role", "assistant", "content", "回答 " + i + pad));
            for (int t = 0; t < toolsPerTurn; t++)
            {
                msgs.Add(PyDict.Of("role", "tool", "content", "工具结果 " + i + "-" + t + pad));
            }
        }
        return msgs;
    }

    [Fact]
    public void MicrocompactClearsOldToolResultsOnly()
    {
        var messages = Conversation(4, toolsPerTurn: 2, padChars: 200);
        var cfg = new MicroConfig { threshold_tokens = null, keep_recent_tool_results = 2, min_token_savings = 1, idle_threshold_minutes = 0 };
        MicroResult result = Compact.Microcompact(messages, cfg, idleSeconds: 0);
        // 没有触发条件(无阈值、没空闲)就不动
        Assert.Equal("not_triggered", result.decision_reason);
        Assert.False(result.changed);

        cfg.threshold_tokens = 1;
        result = Compact.Microcompact(messages, cfg, idleSeconds: 0);
        Assert.Equal("applied", result.decision_reason);
        Assert.True(result.changed);
        Assert.Equal(6, result.cleared_count);
        Assert.True(result.tokens_saved > 0);
        Assert.Equal(6, result.messages.Count(m => m is PyDict d && Py.S(d.Get("content")) == Compact.Placeholder));
        // 最近 2 条工具结果要留着
        PyDict last = (PyDict)result.messages[result.messages.Count - 1];
        Assert.StartsWith("工具结果 3-1", Py.S(last.Get("content")));
        // 被清掉的只是工具结果，用户/助手消息原样保留
        Assert.Equal(4, result.messages.Count(m => m is PyDict d && Py.S(d.Get("role")) == "user"));

        Assert.Equal("disabled", Compact.Microcompact(messages, new MicroConfig { enabled = false }, 0).decision_reason);
        Assert.Equal("no_candidates", Compact.Microcompact(Conversation(2), new MicroConfig(), 0).decision_reason);
    }

    [Fact]
    public void MicrocompactKeepsEverythingWhenSavingsTooSmall()
    {
        var messages = Conversation(3, toolsPerTurn: 1);
        var cfg = new MicroConfig { threshold_tokens = 1, keep_recent_tool_results = 0, min_token_savings = 10_000_000 };
        MicroResult result = Compact.Microcompact(messages, cfg, 0);
        Assert.Equal("below_min_savings", result.decision_reason);
        Assert.False(result.changed);
        Assert.Equal(messages.Count, result.messages.Count);
    }

    [Fact]
    public void AutoConfigClampsContextWindow()
    {
        Assert.Equal(Compact.DefaultContextWindow, Compact.AutoConfigFromSettings(null).context_window);
        Assert.Equal(Compact.MinContextWindow, Compact.AutoConfigFromSettings(PyDict.Of("ai_context_window", 100L)).context_window);
        Assert.Equal(Compact.MaxContextWindow, Compact.AutoConfigFromSettings(PyDict.Of("ai_context_window", 99_000_000L)).context_window);
        Assert.Equal(200_000, Compact.AutoConfigFromSettings(PyDict.Of("ai_context_window", "200000")).context_window);
        Assert.Equal(Compact.DefaultContextWindow, Compact.AutoConfigFromSettings(PyDict.Of("ai_context_window", "abc")).context_window);
        // 阈值 = 窗口 - 预留输出余量
        Assert.Equal(200_000 - 13_000, Compact.AutoThreshold(Compact.AutoConfigFromSettings(PyDict.Of("ai_context_window", 200_000L))));
    }

    [Fact]
    public void ShouldAutocompactReportsReasonPerCondition()
    {
        // 阈值 = 窗口 - 预留 = 12 token，随便几句话就超过
        var cfg = new AutoConfig { context_window = 8192, buffer_tokens = 8180, keep_recent_messages = 4 };
        var state = new CompactState();
        List<object> msgs = Conversation(2);

        Assert.Equal("not_enough_messages", Compact.ShouldAutocompact(msgs, cfg, state, null).reason);
        Assert.Equal("disabled", Compact.ShouldAutocompact(msgs, new AutoConfig { enabled = false }, state, null).reason);

        List<object> many = Conversation(20);
        Assert.Equal("above_threshold", Compact.ShouldAutocompact(many, cfg, state, null).reason);

        // 阈值高（窗口 8192 - 预留 1000）时短对话不该触发
        var roomy = new AutoConfig { context_window = 8192, buffer_tokens = 1000, keep_recent_messages = 4 };
        Assert.Equal("below_threshold", Compact.ShouldAutocompact(many, roomy, state, null).reason);

        state.consecutive_failures = cfg.max_consecutive_failures;
        Assert.Equal("circuit_breaker", Compact.ShouldAutocompact(many, cfg, state, null).reason);
    }

    [Fact]
    public void CompactConversationKeepsSystemAndRecentMessages()
    {
        var cfg = new AutoConfig { keep_recent_messages = 4, context_window = 8192 };
        List<object> messages = Conversation(10);
        string seen = "";
        CompactionResult result = Compact.CompactConversation(messages, cfg, slice =>
        {
            seen = string.Join("|", slice.Select(m => Py.S(((PyDict)m).Get("content"))));
            return "  摘要内容  ";
        });

        Assert.Contains("用户第 0 句", seen);
        Assert.True(result.post_token_count < result.pre_token_count);
        Assert.Equal("system", Py.S(((PyDict)result.messages[0]).Get("role")));
        PyDict marker = (PyDict)result.messages[1];
        Assert.Equal("user", Py.S(marker.Get("role")));
        Assert.Contains("[历史摘要]", Py.S(marker.Get("content")));
        Assert.Contains("摘要内容", Py.S(marker.Get("content")));
        Assert.StartsWith("compact_", Py.S(marker.Get("id")));
        Assert.Equal(4, result.kept_message_count);
    }

    [Fact]
    public void CompactConversationRefusesEmptySummaryAndShortConversations()
    {
        var cfg = new AutoConfig { keep_recent_messages = 4 };
        Assert.Throws<ValueError>(() => Compact.CompactConversation(Conversation(10), cfg, _ => "   "));
        CompactionResult tiny = Compact.CompactConversation(Conversation(1), cfg, _ => "x");
        Assert.Equal(0, tiny.summarized_message_count);
        Assert.Equal(3, tiny.messages.Count);
    }

    [Fact]
    public void CompactBoundaryNeverSplitsToolResultsFromTheirCalls()
    {
        var cfg = new AutoConfig { keep_recent_messages = 2 };
        List<object> messages = Conversation(6, toolsPerTurn: 2);
        CompactionResult result = Compact.CompactConversation(messages, cfg, _ => "摘要");
        // 摘要之后的第一条不能是 tool(否则接口会拒)
        Assert.Equal("user", Py.S(((PyDict)result.messages[1]).Get("role")));
        Assert.NotEqual("tool", Py.S(((PyDict)result.messages[2]).Get("role")));
    }

    [Fact]
    public void RapidRefillThrowsAfterTooManyQuickCompactions()
    {
        var cfg = new AutoConfig { tool_turn_threshold = 3, max_consecutive_rapid_refills = 2 };
        var state = new CompactState { tool_turns_since_compact = 1 };
        Compact.CheckRapidRefill(state, cfg);
        Assert.Equal(1, state.rapid_refills);
        Assert.Throws<RapidRefillBlocked>(() => Compact.CheckRapidRefill(state, cfg));

        // 压缩成功会清掉「连续失败」与「距上次压缩的工具轮数」（rapid_refills 计数照参考版不清，它记的是历史）
        Compact.NoteCompactSuccess(state);
        Assert.Equal(0, state.consecutive_failures);
        Assert.Equal(0, state.tool_turns_since_compact);
        Assert.False(Compact.NoteCompactFailure(state, cfg));
        Assert.False(Compact.NoteCompactFailure(state, cfg));
        Assert.True(Compact.NoteCompactFailure(state, cfg));
    }
}

public class UsageTests
{
    [Fact]
    public void RealUsageIsRecordedAndMarkedAsProviderSource()
    {
        string chat = "usage-real-" + Guid.NewGuid().ToString("N");
        Usage.Add(chat, PyDict.Of("prompt_tokens", 120L, "completion_tokens", 30L), 999, 0);
        Usage.Add(chat, PyDict.Of("prompt_tokens", 80L, "completion_tokens", 20L), 0, 0);
        PyDict row = Usage.Get(chat);
        Assert.Equal(200, Py.Int(row.Get("input")));
        Assert.Equal(50, Py.Int(row.Get("output")));
        Assert.Equal(2, Py.Int(row.Get("requests")));
        Assert.Equal(2, Py.Int(row.Get("real_requests")));
        Assert.Equal(0, Py.Int(row.Get("input_estimated")));
        Assert.Equal("provider_usage", Py.S(row.Get("source")));
        Usage.Reset(chat);
        Assert.Equal("estimate", Py.S(Usage.Get(chat).Get("source")));
    }

    [Fact]
    public void MissingUsageFallsBackToEstimate()
    {
        string chat = "usage-est-" + Guid.NewGuid().ToString("N");
        Usage.Add(chat, null, 1500, 40);
        PyDict row = Usage.Get(chat);
        Assert.Equal(1500, Py.Int(row.Get("input_estimated")));
        Assert.Equal(40, Py.Int(row.Get("output")));
        Assert.Equal(0, Py.Int(row.Get("real_requests")));
        Assert.Equal("estimate", Py.S(row.Get("source")));
        Usage.Reset(chat);
    }

    [Fact]
    public void EmptyChatReturnsZeroRow()
    {
        PyDict row = Usage.Get("never-used-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(0, Py.Int(row.Get("requests")));
        Assert.Equal("estimate", Py.S(row.Get("source")));
    }
}