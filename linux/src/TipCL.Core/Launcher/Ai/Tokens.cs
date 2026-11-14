// M4-H（AI 服务层）：token 估算（照 Windows 版 Launcher/Ai/Tokens.cs 搬运）。
//
// 参考版的 Agent 在 Python 侧，靠这套估算决定「要不要压缩上下文」「现在用了多少窗口」。
// 估法照抄参考版（CJK 一字 ≈ 1 token，其余 ≈ 0.25 token，每条消息再加 4 的开销），
// 只做量级判断，不当作计费依据；有 provider 回传的 usage 时以真值为基准（见 UpdateFromUsage）。
using System.Collections.Generic;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>上下文占用与用量的 token 估算。</summary>
public static class Tokens
{
    /// <summary>每条消息的结构开销（照参考版 _PER_MSG_OVERHEAD）。</summary>
    private const int PerMsgOverhead = 4;

    /// <summary>一段文本的 token 估算（CJK 与全角标点按 1 个，其余按 0.25 个）。</summary>
    public static long EstimateText(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return 0;
        }
        double acc = 0;
        foreach (int cp in PyDifflib.CodePoints(s))
        {
            bool cjk = (cp >= 19968 && cp <= 40959) || (cp >= 12288 && cp <= 12351);
            acc += cjk ? 1.0 : 0.25;
        }
        return (long)acc + 1;
    }

    /// <summary>整个消息数组的 token 估算（含 tool_calls 的 name/arguments）。</summary>
    public static long EstimateMessages(IEnumerable<object>? messages)
    {
        long total = 0;
        if (messages == null)
        {
            return 0;
        }
        foreach (object message in messages)
        {
            if (message is not PyDict m)
            {
                continue;
            }
            total += PerMsgOverhead;
            total += EstimateText(Py.S(m.Get("content")));
            foreach (object raw in Py.ListOr(m.Get("tool_calls")))
            {
                PyDict function = Py.DictOr(Tools.DGet(raw, "function"));
                total += EstimateText(Py.S(function.Get("arguments")));
                total += EstimateText(Py.S(function.Get("name")));
            }
        }
        return total;
    }

    /// <summary>取 provider 的 usage 字段（OpenAI 与 Anthropic 两种键名都认）。</summary>
    internal static long UsageInt(PyDict? usage, string a, string b)
        => Py.Int(Py.Or(usage?.Get(a), usage?.Get(b), 0L));

    /// <summary>用 provider 回传的 usage 校准 token 状态（下一轮从这里往后估算，避免重复算历史）。</summary>
    public static void UpdateFromUsage(TokenState st, PyDict? usage, long nMessages)
    {
        usage ??= new PyDict();
        long input = UsageInt(usage, "prompt_tokens", "input_tokens");
        long output = UsageInt(usage, "completion_tokens", "output_tokens");
        long cacheRead = Py.Int(Py.Or(Tools.DGet(Py.Or(usage.Get("prompt_tokens_details"), new PyDict()), "cached_tokens"), 0L));
        if (input > 0)
        {
            st.base_tokens = input;
            st.base_msg_index = nMessages;
            st.cache_read = cacheRead;
            st.source = "provider_usage";
        }
        st.last_output_tokens = output;
    }

    /// <summary>当前上下文占用：有 provider 基准时只估算基准之后新增的消息，否则整体估算。</summary>
    public static long CurrentInputTokens(List<object>? messages, TokenState st)
    {
        messages ??= new List<object>();
        if (st.source == "provider_usage" && st.base_msg_index <= messages.Count)
        {
            int start = (int)st.base_msg_index;
            return st.base_tokens + EstimateMessages(messages.GetRange(start, messages.Count - start));
        }
        return EstimateMessages(messages);
    }
}
