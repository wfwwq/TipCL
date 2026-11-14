// M4-H（AI 服务层）：MCP 相关异常（照 Windows 版 Launcher/Ai/McpError.cs）。
namespace TipCL.Core.Launcher.Ai;

/// <summary>MCP server 连接/调用失败。</summary>
public class McpError : PyException
{
    public McpError(string m) : base("McpError", m)
    {
    }
}
