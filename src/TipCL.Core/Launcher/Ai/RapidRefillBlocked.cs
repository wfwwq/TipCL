// M4-H（AI 服务层）：压缩熔断异常（照 Windows 版 Launcher/Ai/RapidRefillBlocked.cs）。
namespace TipCL.Core.Launcher.Ai;

/// <summary>压缩后很快又满、连续多次：熔断本轮，避免把额度全花在摘要上。</summary>
public class RapidRefillBlocked : PyException
{
    public RapidRefillBlocked(string m) : base("RapidRefillBlocked", m)
    {
    }
}