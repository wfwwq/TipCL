// AI2-content:ToolRunner 的分部 —— 数据包 / 资源包 / KubeJS 脚本 / 内容加载检查四个工具的分发入口。
// 主 switch(ToolRunner.cs)里只加了一组 case 指到 RunContent,实现都在 Content/ContentTools.cs。
// 写工具走 RunAsync 的同一条确认链:ToolMeta 风险分级 → Decide → Confirm(带 ContentPreview 预览)
// → AiAgent 在执行前按 ContentTools.AffectedPaths 做检查点。write_kubejs_script 是 high,确认卡与执行器都不给「本对话都允许」。
namespace TipCL.Core.Launcher.Ai;

public sealed partial class ToolRunner
{
    /// <summary>create_datapack / create_resourcepack / write_kubejs_script / validate_content 的执行(实现见 ContentTools)。</summary>
    private object RunContent(string name, PyDict args) => ContentTools.Run(name, args, InstName(args));
}
