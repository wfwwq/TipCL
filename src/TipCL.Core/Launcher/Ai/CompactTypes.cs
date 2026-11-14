// M4-H（AI 服务层）：上下文压缩的配置与小结果类型
// （照 Windows 版 Launcher/Ai 的 AutoConfig / MicroConfig / MicroResult / CompactState / CompactionResult 搬运）。
//
// 放在一个文件里是因为这五个类型只服务于 Compact.cs：拆成五个 8 行的文件反而更难读
// （参考版是反编译产物才这么碎）。类型名与字段名与参考版逐字一致。
namespace TipCL.Core.Launcher.Ai;

/// <summary>自动压缩（整段历史摘要）的开关与阈值。</summary>
public sealed class AutoConfig
{
    public bool enabled = true;

    /// <summary>模型上下文窗口（token）；来自 config 的 ai_context_window，缺省 128k。</summary>
    public long context_window = 131072;

    /// <summary>留给模型输出的余量：占用到「窗口 - 余量」就压。</summary>
    public long buffer_tokens = 13000;

    public long max_consecutive_failures = 3;

    public long max_consecutive_rapid_refills = 3;

    /// <summary>压缩后这么多个工具轮内又触发压缩 = 压缩没起作用，计一次 rapid_refill。</summary>
    public long tool_turn_threshold = 3;

    /// <summary>压缩后保留的最近消息条数（按条数留尾巴）。</summary>
    public long keep_recent_messages = 6;
}

/// <summary>微压缩（清掉旧工具结果）的开关与阈值。</summary>
public sealed class MicroConfig
{
    public bool enabled = true;

    /// <summary>超过这个 token 才清（空 = 只看空闲时长）。</summary>
    public long? threshold_tokens;

    /// <summary>保留最近这么多条工具结果不动。</summary>
    public long keep_recent_tool_results = 5;

    /// <summary>至少省这么多 token 才值得动手。</summary>
    public long min_token_savings = 2000;

    /// <summary>空闲这么久（分钟）也可以清。</summary>
    public long idle_threshold_minutes = 30;
}

/// <summary>微压缩的结果。</summary>
public sealed class MicroResult
{
    public System.Collections.Generic.List<object> messages = new();

    /// <summary>disabled / no_candidates / nothing_to_clear / not_triggered / below_min_savings / applied。</summary>
    public string decision_reason = "";

    public long cleared_count;

    public long tokens_saved;

    public bool changed;
}

/// <summary>压缩状态机（跨轮持有）。</summary>
public sealed class CompactState
{
    /// <summary>连续压缩失败次数（达上限就熔断，不再尝试）。</summary>
    public long consecutive_failures;

    /// <summary>连续「压完很快又满」的次数。</summary>
    public long rapid_refills;

    /// <summary>距上次成功压缩过了多少个工具轮（初值取一个很大的数，表示「从来没压过」）。</summary>
    public long tool_turns_since_compact = 1000000000;
}

/// <summary>一次整段压缩的结果。</summary>
public sealed class CompactionResult
{
    public System.Collections.Generic.List<object> messages = new();

    public long pre_token_count;

    public long post_token_count;

    public long summarized_message_count;

    public long kept_message_count;
}