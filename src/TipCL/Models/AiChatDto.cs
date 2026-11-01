using System.Collections.Generic;

namespace TipCL.Models;

public sealed class AiChatDto
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";

	public List<AiMsgDto> Messages { get; set; } = new List<AiMsgDto>();

	public AiPlanDto? Plan { get; set; }

	/// <summary>对话用的 AI 预设（插件预设形如 plugin:&lt;插件&gt;/&lt;预设&gt;），普通对话为空。</summary>
	public string Preset { get; set; } = "";

	/// <summary>这个对话默认打开的 aiView 标签。</summary>
	public string View { get; set; } = "";
}
