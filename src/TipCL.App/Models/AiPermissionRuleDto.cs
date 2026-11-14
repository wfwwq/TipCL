using System.Text.Json.Serialization;

namespace TipCL.Models;

/// <summary>
/// C8a:AI 权限规则的一条(= Windows <c>Models\AiPermissionRuleDto.cs</c>,23 行,**字节级照抄** —— 含
/// <c>JsonPropertyName</c> 的特性,后端字段名一个不改,默认值都是空串)。
/// </summary>
public sealed class AiPermissionRuleDto
{
	public string Key { get; set; } = "";

	[JsonPropertyName("toolName")]
	public string ToolName { get; set; } = "";

	[JsonPropertyName("ruleContent")]
	public string RuleContent { get; set; } = "";

	public string Behavior { get; set; } = "";

	[JsonPropertyName("behavior_label")]
	public string BehaviorLabel { get; set; } = "";

	public string Instance { get; set; } = "";

	public string Scope { get; set; } = "";
}
