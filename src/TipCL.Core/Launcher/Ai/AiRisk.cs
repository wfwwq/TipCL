// 来源: Windows 版参考仓库 src/TipCL/Pages/Ai/AiRisk.cs(M4-H P5;Linux 侧搬到 Core/Ai,
// 因为工具执行器(ToolRunner)与页面都要按它做风险分级与确认强度)。
namespace TipCL.Core.Launcher.Ai;

/// <summary>工具的风险等级(照 Windows 版 AiRisk)。</summary>
public enum AiRisk
{
    /// <summary>只读:不弹确认。</summary>
    Read = 0,

    /// <summary>联网:只读但会出网(搜索/下载元数据)。</summary>
    Network = 1,

    /// <summary>写本机:装/改/启用禁用。</summary>
    Write = 2,

    /// <summary>删除:不可恢复,确认用危险样式。</summary>
    Delete = 3,
}
