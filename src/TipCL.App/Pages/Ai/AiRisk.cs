namespace TipCL.Pages.Ai;

/// <summary>
/// C8a:AI 工具的风险等级(= Windows <c>Pages\Ai\AiRisk.cs</c>,9 行,**字节级照抄**)。
/// 顺序与取值一字不改:<c>Read / Network / Write / Delete</c>(顺序被别处的比较与显示复用)。
/// </summary>
internal enum AiRisk
{
	Read,
	Network,
	Write,
	Delete,
}
