using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TipCL.Models;

/// <summary>
/// K2 附属件:AI 页的**线缆 DTO**(= Windows <c>Models\AiStoreDto.cs</c> / <c>AiChatDto.cs</c> /
/// <c>AiMsgDto.cs</c> / <c>AiPlanDto.cs</c> / <c>AiPlanItemDto.cs</c>,五份共 6 个小类,逐字段照搬)。
///
/// <b>为什么在 <c>Pages\Ai\</c> 目录里却声明 <see cref="TipCL.Models"/> 命名空间</b>:
/// Windows 侧这五个类在 <c>TipCL.Models</c> 命名空间,<c>AiPage</c> 的调用点(如 <c>_store.Chats</c>、
/// <c>activeChat.Plan.Items</c>)全部按那个口径写。命名空间与目录名无关(C# 里由声明决定),所以
/// **把文件放进本包允许写入的 <c>Pages\Ai\</c>、命名空间照抄 Windows**,调用点就能一条不改地照搬。
/// 本仓 <c>Models\</c> 目录里已有同族的 <see cref="OpResult"/>(C8a 落的),这五个如果将来搬家,
/// 直接移到 <c>src\TipCL.App\Models\</c> 即可(命名空间不用改)。
///
/// 字段名与 JSON 名不一致的三处(<c>active_id</c> / <c>pending_card</c> / <c>turn_id</c>)照 Windows 挂
/// <see cref="JsonPropertyNameAttribute"/>;其余字段名与后端键同名,靠 <c>PropertyNameCaseInsensitive</c>
/// 之外的默认匹配(Avalonia 侧的 <c>BridgeClient</c> 用同一套 <c>JsonSerializerOptions</c>,与 Windows 同)。
/// </summary>
public sealed class AiStoreDto
{
	[JsonPropertyName("active_id")]
	public string ActiveId { get; set; } = "";

	public List<AiChatDto> Chats { get; set; } = new List<AiChatDto>();

	public bool Busy { get; set; }

	[JsonPropertyName("pending_card")]
	public JsonElement? PendingCard { get; set; }
}

/// <summary>一个对话(= Windows <c>Models\AiChatDto.cs</c>)。</summary>
public sealed class AiChatDto
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";

	public List<AiMsgDto> Messages { get; set; } = new List<AiMsgDto>();

	public AiPlanDto? Plan { get; set; }

	/// <summary>对话用的 AI 预设(插件预设形如 <c>plugin:&lt;插件&gt;/&lt;预设&gt;</c>),普通对话为空。</summary>
	public string Preset { get; set; } = "";

	/// <summary>这个对话默认打开的 aiView 标签。</summary>
	public string View { get; set; } = "";
}

/// <summary>一条消息(= Windows <c>Models\AiMsgDto.cs</c>)。</summary>
public sealed class AiMsgDto
{
	public string Role { get; set; } = "";

	public string Content { get; set; } = "";

	public string Note { get; set; } = "";

	[JsonPropertyName("id")]
	public string Id { get; set; } = "";
}

/// <summary>一轮计划(= Windows <c>Models\AiPlanDto.cs</c>)。</summary>
public sealed class AiPlanDto
{
	[JsonPropertyName("turn_id")]
	public string TurnId { get; set; } = "";

	public List<AiPlanItemDto> Items { get; set; } = new List<AiPlanItemDto>();
}

/// <summary>计划里的一条(= Windows <c>Models\AiPlanItemDto.cs</c>)。</summary>
public sealed class AiPlanItemDto
{
	public string Title { get; set; } = "";

	public string Status { get; set; } = "pending";
}
