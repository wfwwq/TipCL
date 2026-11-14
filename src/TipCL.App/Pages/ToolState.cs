namespace TipCL.Pages;

/// <summary>
/// C8a:一个 AI 工具调用的展示状态(= Windows <c>Pages\ToolState.cs</c>,10 行,**字节级照抄**):
/// <c>Prepare / Running / Done / Skipped / Failed</c> —— 顺序一字不改(渲染层按它判分支)。
/// 注意命名空间是 <c>TipCL.Pages</c>(不是 <c>TipCL.Pages.Ai</c>),与 Windows 相同;
/// <c>TipCL.Pages.Ai</c> 下的类型能直接看到它(向外层找)。
/// </summary>
internal enum ToolState
{
	Prepare,
	Running,
	Done,
	Skipped,
	Failed,
}
