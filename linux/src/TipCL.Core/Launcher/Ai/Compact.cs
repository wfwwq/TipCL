// M4-H（AI 服务层）：上下文压缩（照 Windows 版 Launcher/Ai/Compact.cs 搬运）。
//
// 两级压缩，语义与参考版一致:
//   1. 微压缩 Microcompact:把较旧的 tool 结果换成占位串（不调模型、不花额度），够省才动手；
//   2. 整段压缩 CompactConversation:把前段历史交给模型摘要成一条 [历史摘要]，只留最近若干条。
// 触发判定 ShouldAutocompact 用 Tokens 的实时估算（有 provider 真值就按真值往后加），
// 熔断（连续失败 / 压完又满）由 CompactState 记录 —— 照参考版的 Agent 循环口径。
using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>上下文压缩：微压缩 + 整段摘要 + 触发判定。</summary>
public static class Compact
{
    /// <summary>微压缩时替换工具结果的占位串（照参考版，一字不改）。</summary>
    public const string Placeholder = "[工具结果已清理:内容较旧已从上下文移除,需要时请重新调用该工具获取]";

    public const long DefaultContextWindow = 131072;

    public const long MinContextWindow = 8192;

    public const long MaxContextWindow = 2000000;

    /// <summary>整段摘要的系统提示词（照参考版）。</summary>
    public static string SummaryPrompt =>
        "把这段启动器对话压成一份简短摘要。必须保留:用户最终想要什么、已经执行过哪些操作(工具名和结果成败)、提到的游戏版本/实例/模组名、还没做完的事。直接输出摘要正文,不要客套。";

    private static string? Role(object m) => m is PyDict d ? Py.S(d.Get("role")) : null;

    /// <summary>微压缩：清掉较旧的工具结果。</summary>
    public static MicroResult Microcompact(List<object>? messages0, MicroConfig cfg, double idleSeconds)
    {
        var messages = new List<object>(messages0 ?? new List<object>());
        if (!cfg.enabled)
        {
            return new MicroResult { messages = messages, decision_reason = "disabled" };
        }
        var toolIdx = new List<int>();
        for (int i = 0; i < messages.Count; i++)
        {
            if (messages[i] is PyDict d && Py.Eq(d.Get("role"), "tool"))
            {
                toolIdx.Add(i);
            }
        }
        long keep = Math.Max(0, cfg.keep_recent_tool_results);
        if (toolIdx.Count == 0)
        {
            return new MicroResult { messages = messages, decision_reason = "no_candidates" };
        }
        List<int> candidates = keep > 0
            ? toolIdx.Take(Math.Max(0, toolIdx.Count - (int)keep)).ToList()
            : toolIdx;
        if (candidates.Count == 0)
        {
            return new MicroResult { messages = messages, decision_reason = "nothing_to_clear" };
        }
        long before = Tokens.EstimateMessages(messages);
        bool overTokens = cfg.threshold_tokens.HasValue && before >= cfg.threshold_tokens.Value;
        bool idle = idleSeconds > 0 && idleSeconds >= cfg.idle_threshold_minutes * 60;
        if (!overTokens && !idle)
        {
            return new MicroResult { messages = messages, decision_reason = "not_triggered" };
        }
        var after = new List<object>(messages);
        foreach (int i in candidates)
        {
            PyDict copy = ((PyDict)after[i]).Copy();
            copy["content"] = Placeholder;
            after[i] = copy;
        }
        long saved = before - Tokens.EstimateMessages(after);
        if (saved < cfg.min_token_savings)
        {
            return new MicroResult { messages = messages, decision_reason = "below_min_savings", tokens_saved = saved };
        }
        return new MicroResult
        {
            messages = after,
            decision_reason = "applied",
            cleared_count = candidates.Count,
            tokens_saved = saved,
            changed = true,
        };
    }

    /// <summary>从配置里读上下文窗口（config 的 ai_context_window，越界就夹到合法区间）。</summary>
    public static AutoConfig AutoConfigFromSettings(PyDict? settings)
    {
        object? raw = (settings ?? new PyDict()).Get("ai_context_window");
        long window;
        try
        {
            window = Py.Int(raw);
        }
        catch (Exception ex) when (ex is TypeError || ex is ValueError)
        {
            window = DefaultContextWindow;
        }
        if (window <= 0)
        {
            window = DefaultContextWindow;
        }
        window = Math.Max(MinContextWindow, Math.Min(window, MaxContextWindow));
        return new AutoConfig { context_window = window };
    }

    /// <summary>触发整段压缩的 token 阈值（窗口 - 预留输出余量）。</summary>
    public static long AutoThreshold(AutoConfig cfg) => Math.Max(0, cfg.context_window - cfg.buffer_tokens);

    /// <summary>现在该不该整段压缩。</summary>
    public static (bool should, string reason) ShouldAutocompact(List<object>? messages, AutoConfig cfg, CompactState cstate, TokenState? st)
    {
        if (!cfg.enabled)
        {
            return (false, "disabled");
        }
        if (cstate.consecutive_failures >= cfg.max_consecutive_failures)
        {
            return (false, "circuit_breaker");
        }
        if ((messages ?? new List<object>()).Count(m => m is PyDict d && !Py.Eq(d.Get("role"), "system")) < cfg.keep_recent_messages + 2)
        {
            return (false, "not_enough_messages");
        }
        long used = st != null ? Tokens.CurrentInputTokens(messages, st) : 0;
        if (used == 0)
        {
            used = Tokens.EstimateMessages(messages);
        }
        if (used < AutoThreshold(cfg))
        {
            return (false, "below_threshold");
        }
        return (true, "above_threshold");
    }

    /// <summary>压完很快又触发 = 压缩没起作用；连续多次就抛异常熔断本轮。</summary>
    public static void CheckRapidRefill(CompactState cstate, AutoConfig cfg)
    {
        if (cstate.tool_turns_since_compact < cfg.tool_turn_threshold)
        {
            cstate.rapid_refills++;
            if (cstate.rapid_refills >= cfg.max_consecutive_rapid_refills)
            {
                throw new RapidRefillBlocked(
                    "上下文压缩后 " + cstate.tool_turns_since_compact + " 个工具轮又触发压缩,连续 " + cstate.rapid_refills + " 次,已熔断");
            }
        }
    }

    /// <summary>压缩成功：清失败计数与工具轮计数。</summary>
    public static void NoteCompactSuccess(CompactState c)
    {
        c.consecutive_failures = 0;
        c.tool_turns_since_compact = 0;
    }

    /// <summary>压缩失败：累计失败数，返回是否已达熔断阈值。</summary>
    public static bool NoteCompactFailure(CompactState c, AutoConfig cfg)
    {
        c.consecutive_failures++;
        return c.consecutive_failures >= cfg.max_consecutive_failures;
    }

    /// <summary>切点不能落在 tool 消息上（否则工具结果与它的调用被拆开，接口会拒），往前让位。</summary>
    private static int AlignKeepBoundary(List<object> body, int cut)
    {
        cut = Math.Max(0, Math.Min(cut, body.Count));
        while (cut > 0 && cut < body.Count && Role(body[cut]) == "tool")
        {
            cut--;
        }
        return cut;
    }

    /// <summary>把前段历史摘要成一条 [历史摘要] 消息；摘要为空时抛 ValueError（宁可不换，也不丢历史）。</summary>
    public static CompactionResult CompactConversation(List<object>? messages0, AutoConfig cfg, Func<List<object>, string> summarize)
    {
        var messages = new List<object>(messages0 ?? new List<object>());
        long before = Tokens.EstimateMessages(messages);
        int head = 0;
        while (head < messages.Count && Role(messages[head]) == "system")
        {
            head++;
        }
        List<object> body = messages.GetRange(head, messages.Count - head);
        int keep = (int)Math.Max(2, cfg.keep_recent_messages);
        if (body.Count <= keep)
        {
            return new CompactionResult
            {
                messages = messages,
                pre_token_count = before,
                post_token_count = before,
                kept_message_count = body.Count,
            };
        }
        int cut = AlignKeepBoundary(body, body.Count - keep);
        if (cut <= 0)
        {
            return new CompactionResult
            {
                messages = messages,
                pre_token_count = before,
                post_token_count = before,
                kept_message_count = body.Count,
            };
        }
        List<object> old = body.GetRange(0, cut);
        List<object> kept = body.GetRange(cut, body.Count - cut);
        string summary = summarize(old);
        if (Py.Strip(summary ?? "").Length == 0)
        {
            throw new ValueError("摘要为空,拒绝替换历史");
        }
        // 摘要标记要带上被摘要到的最后一条消息 id（参考版的 compact_<id>）：
        // 下一轮靠它认出来「这段已经压过了」，不会每轮从头再压一遍
        string tailId = "";
        for (int i = 0; i < old.Count; i++)
        {
            object? id = old[i] is PyDict d ? d.Get("id") : null;
            tailId = Py.Str(Py.Truthy(id) ? id : "idx_" + (head + i));
        }
        var marker = PyDict.Of(
            "role", "user",
            "content", "[历史摘要]\n" + Py.Strip(summary) + "\n(以上是更早对话的摘要,继续当前任务。)",
            "id", "compact_" + tailId,
            "keep", (long)kept.Count);

        var result = messages.GetRange(0, head);
        result.Add(marker);
        result.AddRange(kept);
        return new CompactionResult
        {
            messages = result,
            pre_token_count = before,
            post_token_count = Tokens.EstimateMessages(result),
            summarized_message_count = old.Count,
            kept_message_count = kept.Count,
        };
    }
}
