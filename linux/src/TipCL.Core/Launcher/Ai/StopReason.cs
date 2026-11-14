// M4-H（AI 服务层）：停止原因常量（照 Windows 版 Launcher/Ai/StopReason.cs 搬运）。
//
// 参考版的 Agent 用它标「这一轮为什么结束」；Linux 侧 AiAgent 的 StopReason 属性用同一批值,
// 便于两边日志对齐（会话事件流里的 stop_reason 就是这个）。
namespace TipCL.Core.Launcher.Ai;

/// <summary>一轮对话的结束原因。</summary>
public static class StopReason
{
    public const string COMPLETED = "completed";

    /// <summary>模型不再调工具（正常收尾）。</summary>
    public const string NO_TOOL_CALL = "no_tool_call";

    public const string MAX_ROUNDS = "max_rounds";

    public const string TRUNCATED = "truncated";

    public const string PENDING_TASK = "pending_task";

    public const string STREAM_FAILED = "stream_failed";

    public const string EMPTY_RESPONSE = "empty_response";

    public const string RATE_LIMITED = "rate_limited";

    public const string CANCELLED = "cancelled";

    public const string ERROR = "error";
}
