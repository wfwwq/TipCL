using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// C8b(第 1 步):**这是将来 <c>AiPage</c> 的一个分区** —— 先落地,是因为 <c>AiFixWindow</c> 调它。
///
/// 【Windows 源】<c>Pages\AiPage.cs:2153-2156</c>(注意:Windows 侧它在 <c>Pages\</c> 下,**不在** <c>Ai\</c> 子目录),
/// 全文只有 3 行,依赖只有 <see cref="BridgeEvent"/>(Core 已移植)与 <c>I18n.L</c>,
/// **不碰任何 <c>AiPage</c> 实例状态** —— 所以这一片是安全的,不会把整页拉进来。
///
/// 将来 `AiPage` 主文件落地时,写成同一个类的**另一个分区**即可(两边自然合起来,调用点不用改)。
/// **只搬这一个方法**:它旁边那几个静态(如 <c>UnsentOf</c>)等需要时再说,别顺手搬。
/// </summary>
internal sealed partial class AiPage
{
	/// <summary>后台任务(下载/安装)结束时,给 AI 回一段"任务回报"文本(Windows 同名同文)。</summary>
	internal static string PendingTaskReport(string name, BridgeEvent finished)
	{
		return "[后台任务回报] " + name + " " + (finished.Success ? I18n.L("成功") : I18n.L("失败")) + ":" + finished.Message;
	}

	/// <summary>
	/// 【Windows 源】<c>Pages\AiPage.cs:2131-2153</c>:从"这一轮因为后台任务而停下"的事件里取出待办任务表。
	/// 逐字搬;**依赖只有 <see cref="BridgeEvent"/> + <c>I18n</c> + 下面那个 <c>StopReasonOf</c>** ——
	/// 与 <see cref="PendingTaskReport"/> 一样**不碰任何 <c>AiPage</c> 实例状态**(所以能先落地)。
	/// </summary>
	internal static List<(string TaskId, string Name)> PendingTasksOf(BridgeEvent ev)
	{
		List<(string TaskId, string Name)> list = new List<(string, string)>();
		if (StopReasonOf(ev) != "pending_task" || !ev.Payload.TryGetProperty("pending_tasks", out JsonElement tasks) || tasks.ValueKind != JsonValueKind.Array)
		{
			return list;
		}

		foreach (JsonElement item in tasks.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			string taskId = (item.TryGetProperty("task_id", out JsonElement id) && id.ValueKind == JsonValueKind.String) ? (id.GetString() ?? "") : "";
			if (taskId.Length == 0)
			{
				continue;
			}

			string name = (item.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String) ? (n.GetString() ?? "") : "";
			list.Add((taskId, (name.Length > 0) ? name : I18n.L("任务")));
		}

		return list;
	}

	/// <summary>
	/// 【Windows 源】<c>Pages\AiPage.cs:2177-2193</c>:**没送出去的话**(`unsent[]`)拼成一段文本,
	/// 让前端把它们接着发给助手。依赖只有 <see cref="BridgeEvent"/> 与字符串处理,**无实例状态**。
	/// </summary>
	internal static string UnsentOf(BridgeEvent ev)
	{
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty("unsent", out JsonElement unsent) || unsent.ValueKind != JsonValueKind.Array)
		{
			return "";
		}

		List<string> list = new List<string>();
		foreach (JsonElement item in unsent.EnumerateArray())
		{
			string text = (item.ValueKind == JsonValueKind.String) ? (item.GetString() ?? "").Trim() : "";
			if (text.Length > 0)
			{
				list.Add(text);
			}
		}

		return string.Join("\n\n", list);
	}

	/// <summary>
	/// 停因(【Windows 源】<c>Pages\AiPage.cs:2195-2202</c>)—— `PendingTasksOf` 要用它。
	/// **Windows 里 <c>AiPage</c> 自己也有一份私有同名方法**(与本文件里的这份并存,只是属于同一个类),
	/// 所以这里照搬同名同实现;`AiFixWindow` 那份是**另一个类**的私有方法,互不影响。
	/// </summary>
	private static string StopReasonOf(BridgeEvent ev)
	{
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty("stop_reason", out JsonElement v) || v.ValueKind != JsonValueKind.String)
		{
			return "";
		}

		return v.GetString() ?? "";
	}
}
