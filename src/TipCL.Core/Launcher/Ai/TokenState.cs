// M4-H（AI 服务层）：token 状态（照 Windows 版 Launcher/Ai/TokenState.cs 搬运）。
namespace TipCL.Core.Launcher.Ai;

/// <summary>一次会话里 token 用量的滚动状态：provider 给的基准 + 基准之后本地估算的新增。</summary>
public sealed class TokenState
{
    /// <summary>provider 回传的输入 token（基准）。</summary>
    public long base_tokens;

    /// <summary>基准对应的消息条数（估算从此下标之后开始累加）。</summary>
    public long base_msg_index;

    /// <summary>命中缓存的输入 token。</summary>
    public long cache_read;

    /// <summary>写入缓存的 token。</summary>
    public long cache_write;

    /// <summary>上一次的输出 token。</summary>
    public long last_output_tokens;

    /// <summary>estimate（本地估算）/ provider_usage（真值）。</summary>
    public string source = "estimate";
}
