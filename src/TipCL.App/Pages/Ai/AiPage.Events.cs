using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using TipCL.Core.Launcher.Ai;
using TipCL.Models;
using TipCL.Pages.Ai;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// K10:事件路由与工具行。【Windows 源】<c>Pages\AiPage.cs:1918-1950</c>(<c>Line</c> / <c>FindGroup</c>)、
/// <c>:1952-2129</c>(<c>OnEvent</c>,八个事件名的分发)、<c>:2160-2175</c>(<c>ReportPendingTask</c>)、
/// <c>:2204-2250</c>(<c>DetailOf</c> / <c>StopNote</c> / <c>NotifyStop</c>)、<c>:2252-2348</c>(<c>OnStatus</c>)、
/// <c>:2350-2522</c>(<c>IsMemoryTool</c> / <c>PayloadStr</c> / <c>AddMemoryTag</c> / <c>AddTaskCard</c> / <c>TaskIdOf</c>)。
///
/// <b>这一块里"已经先落地"的三个静态成员不在本文件</b>:<c>PendingTasksOf</c> / <c>PendingTaskReport</c> /
/// <c>UnsentOf</c>(以及它们要的 <c>StopReasonOf</c>)早在 <c>Pages\AiPage.Tasks.cs</c> 里就搬过一遍了
/// (那三个是 <c>AiFixWindow</c> 的硬前置)。本文件只搬**剩下**的那些,所以两边合起来正好覆盖
/// Windows <c>:2131-2202</c> + <c>:2204-2522</c>,没有重名、没有漏项。
///
/// Windows → mac 的差异(逐条):
/// <list type="number">
/// <item><c>FindGroup</c>:WPF 走 <c>LogicalTreeHelper.GetParent</c> 逐级上溯 → Avalonia 用
///   <c>GetVisualAncestors().OfType&lt;StepGroup&gt;()</c>(本仓既有写法,见 <c>StepGroup.AddLine</c> 与
///   <c>AiFixWindow</c> 里同一处);<c>_group.Parent != _messages</c> 的判据不变;</item>
/// <item><c>_tools.TryGetValue(..., out ToolLine value)</c> 的 <c>out</c> 变量在 mac 侧按可空注解写;
///   <c>AiIcons.Flat(..., pill: true)</c> 的"撤销"小钮是已落地件的既有形状;</item>
/// <item><c>Button undo = null</c> → <c>Button? undo = null</c>;<c>row.Children.Remove(undo)</c> 与
///   <c>Add(undo.VCenter())</c> 语义不变;</item>
/// <item><c>PageBase.Win?.Navigate("launch")</c> + <c>GetPage("launch") is LaunchPage → LaunchNowAsync(instance, version)</c>
///   两平台同形(mac 的 <c>LaunchNowAsync(string name, string? version = null)</c>);</item>
/// <item><b>八个事件名的分发结构逐字照搬</b>(按字符串长度分派的那套 switch 也照抄,便于与 Windows 对读);
///   <c>ai.done</c> 的"不是本对话就跳过"三判、<c>_abandoned</c> 的静默口径、<c>unsent</c> 的续发、
///   <c>pending_tasks</c> 的记账,一条不改。</item>
/// </list>
/// </summary>
internal sealed partial class AiPage
{
	/// <summary>按工具名取/建工具行(【Windows 源】:1918-1938)。</summary>
	private ToolLine Line(string name, string text, ToolState state)
	{
		if (_toolLines.TryGetValue(name, out ToolLine? existing) && existing != null)
		{
			existing.SetText(text);
			existing.SetState(state);
			FindGroup(existing)?.Refresh();
			return existing;
		}

		ToolLine line = new ToolLine(text, name);
		line.SetState(state);
		_toolLines[name] = line;
		if (_group == null || _group.Parent != _messages)
		{
			_group = new StepGroup();
			Add(_group);
		}

		_group.AddLine(line);
		ScrollDown();
		return line;
	}

	/// <summary>从一件控件往上找它所属的步骤组(【Windows 源】:1940-1950)。</summary>
	private static StepGroup? FindGroup(Control el)
	{
		return el.GetVisualAncestors().OfType<StepGroup>().FirstOrDefault();
	}

	/// <summary>后端事件的总入口(【Windows 源】:1952-2129)。</summary>
	public override void OnEvent(BridgeEvent ev)
	{
		string text = ev.Event;
		if (text == null)
		{
			return;
		}

		switch (text.Length)
		{
		case 8:
			switch (text[0])
			{
			case 'a':
				if (text == "ai.delta" && RunIsDisplayed(ev))
				{
					_stream.Append(ev.Text);
					Bubble bubble = OpenRound();
					_round.Append(ev.Text);
					bubble.SetText(_round.ToString(), live: true);
					ScrollDown();
				}

				break;
			case 'p':
			{
				if (text == "progress" && _taskCards.TryGetValue(ev.TaskId, out TaskCard? card) && card != null)
				{
					card.SetProgress(ev.Current, ev.Total, ev.Message);
				}

				break;
			}
			case 'f':
				if (text == "finished")
				{
					if (_taskCards.TryGetValue(ev.TaskId, out TaskCard? done) && done != null)
					{
						done.Finish(ev.Success, ev.Message);
					}

					ReportPendingTask(ev);
				}

				break;
			}

			break;
		case 7:
			switch (text[3])
			{
			case 'd':
			{
				if (!(text == "ai.done"))
				{
					break;
				}

				string doneChatId = ChatIdOf(ev);
				if (_busy && _runChatId.Length > 0 && doneChatId.Length > 0 && doneChatId != _runChatId)
				{
					break;
				}

				bool quiet = _abandoned;
				bool mine = RunIsDisplayed(ev);
				_status.Text = "";
				if (!quiet && mine)
				{
					NotifyStop(ev);
				}

				if (mine)
				{
					ShowUsage(ev);
				}

				string stopNote = StopNote(ev);
				if (mine && (_round.Length > 0 || stopNote.Length > 0))
				{
					string body = _round.ToString();
					if (stopNote.Length > 0 && !body.Contains(stopNote))
					{
						body = ((body.Length > 0) ? (body + "\n\n" + stopNote) : stopNote);
					}

					OpenRound().SetText(body);
				}

				if (mine)
				{
					foreach ((string pendingId, string pendingName) in PendingTasksOf(ev))
					{
						_aiPendingTasks[pendingId] = (pendingName, ChatIdOf(ev));
					}
				}

				ResetRunState();
				string unsent = UnsentOf(ev);
				PageBase.Run(async delegate
				{
					AiStoreDto? store = await PageBase.Api.TryCallAsync("ai_list_chats", null, _store);
					_store = store ?? _store;
					RenderChats();
					if (!mine)
					{
						RenderMessages();
					}

					if (mine && !quiet && unsent.Length > 0)
					{
						await SendTextAsync(unsent, echo: false);
					}
				});
				break;
			}
			case 'f':
			{
				if (!(text == "ai.fail"))
				{
					break;
				}

				string failChatId = ChatIdOf(ev);
				if (_busy && _runChatId.Length > 0 && failChatId.Length > 0 && failChatId != _runChatId)
				{
					break;
				}

				bool mine = RunIsDisplayed(ev);
				bool abandoned = _abandoned;
				_status.Text = (ev.Stopped ? "" : ev.Text);
				if (mine && _roundBubble != null && _round.Length > 0)
				{
					_roundBubble.SetText(_round.ToString());
				}

				if (ev.Stopped)
				{
					if (mine && _roundBubble != null && _round.Length == 0)
					{
						_roundBubble.SetText(I18n.L("已停止"));
					}

					if (!abandoned)
					{
						PageBase.Toast(I18n.L("已停止"), I18n.L("可以继续说下一句"));
					}
				}
				else if (mine)
				{
					if (_roundBubble != null && _round.Length == 0)
					{
						_roundBubble.SetText(ev.Text);
					}
					else
					{
						Add(new Bubble("error", ev.Text, RetryFrom));
					}
				}
				else
				{
					PageBase.Toast(I18n.L("助手出错"), ev.Text, ToastKind.Error);
				}

				string kept = _status.Text;
				ResetRunState();
				_status.Text = kept;
				string unsent = UnsentOf(ev);
				if (mine && !abandoned && unsent.Length > 0)
				{
					PageBase.Run(() => SendTextAsync(unsent, echo: false));
				}

				break;
			}
			}

			break;
		case 9:
			if (text == "ai.status" && RunIsDisplayed(ev))
			{
				OnStatus(ev);
			}

			break;
		case 10:
			if (text == "ai.confirm" && RunIsDisplayed(ev))
			{
				ShowConfirm(ev);
			}

			break;
		case 6:
			if (text == "ai.ask" && RunIsDisplayed(ev))
			{
				ShowAsk(ev);
			}

			break;
		}
	}

	/// <summary>后台任务跑完,替用户把"回报"这句话发出去(【Windows 源】:2160-2175)。</summary>
	private void ReportPendingTask(BridgeEvent ev)
	{
		if (!_aiPendingTasks.TryGetValue(ev.TaskId, out (string Name, string ChatId) task))
		{
			return;
		}

		_aiPendingTasks.Remove(ev.TaskId);
		if (task.ChatId.Length <= 0 || !(task.ChatId != _store.ActiveId))
		{
			if (!_busy)
			{
				PageBase.Toast(I18n.L("后台任务完成"), I18n.L("助手回来汇报结果"));
			}

			PageBase.Run(() => SendTextAsync(PendingTaskReport(task.Name, ev), echo: false));
		}
	}

	private static string DetailOf(BridgeEvent ev)
	{
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty("detail", out JsonElement value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}

		return value.GetString() ?? "";
	}

	/// <summary>停因那一段括号说明(【Windows 源】:2213-2234)。</summary>
	private static string StopNote(BridgeEvent ev)
	{
		string text = StopReasonOf(ev) switch
		{
			"no_tool_call" => I18n.L("它没有真的开始执行:模型只回了文字,没有调用任何工具。"),
			"max_rounds" => I18n.L("步骤太多,先停在这里。你可以让我继续。"),
			"pending_task" => I18n.L("下载/安装还在后台跑,可以在「下载任务」里看进度。"),
			"stream_failed" => I18n.L("接口这轮没有返回内容,已停止。"),
			"empty_response" => I18n.L("接口返回了空回复。"),
			_ => "",
		};
		if (text.Length == 0)
		{
			return "";
		}

		string detail = DetailOf(ev);
		if (detail.Length > 0 && !text.Contains(detail))
		{
			text = text + I18n.L("(") + detail + I18n.L(")");
		}

		return I18n.L("(提示:") + text + I18n.L(")");
	}

	private void NotifyStop(BridgeEvent ev)
	{
		string reason = StopReasonOf(ev);
		if (!(reason == "no_tool_call"))
		{
			if (reason == "pending_task")
			{
				PageBase.Toast(I18n.L("任务还在后台跑"), I18n.L("可以在「下载任务」里查进度。"));
			}
		}
		else
		{
			PageBase.Toast(I18n.L("它没有真的开始执行"), I18n.L("模型只回了文字,没有调用任何工具。"), ToastKind.Warning);
		}
	}

	/// <summary>`ai.status` 的十二种 kind(【Windows 源】:2252-2348)。</summary>
	private void OnStatus(BridgeEvent ev)
	{
		string name = (string.IsNullOrEmpty(ev.Name) ? ev.Kind : ev.Name);
		string label = (string.IsNullOrWhiteSpace(ev.Label) ? ev.Name : ev.Label);
		switch (ev.Kind)
		{
		case "plan":
			ShowPlan(ev);
			break;
		case "compact":
			Add(Divider(I18n.L("更早的对话已压缩成摘要")));
			break;
		case "checkpoint_warn":
		{
			string message = ((ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.String) ? (m.GetString() ?? "") : "");
			PageBase.Toast(I18n.L("无法一键撤回"), (message.Length > 0) ? message : I18n.L("检查点不可用,本轮改动无法自动撤回"), ToastKind.Warning);
			break;
		}
		case "model_fallback":
		{
			string model = ((ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("model", out JsonElement mv) && mv.ValueKind == JsonValueKind.String) ? (mv.GetString() ?? "") : "");
			PageBase.Toast(I18n.L("已切换备用模型"), model, ToastKind.Warning);
			Add(Note((model.Length > 0) ? I18n.L("主模型暂时不可用,本轮已切换到 {0}", model) : I18n.L("主模型暂时不可用,本轮已切换到备用模型")));
			break;
		}
		case "thinking":
			_status.Text = I18n.L("思考中...");
			OpenRound();
			break;
		case "think":
		{
			bool afterTools = ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("after_tools", out JsonElement v) && v.ValueKind == JsonValueKind.True;
			string thinking = (afterTools ? I18n.L("搜完了,正在整理...") : I18n.L("正在想..."));
			_status.Text = (afterTools ? I18n.L("搜完了,正在整理...") : I18n.L("思考中..."));
			OpenRound(thinking);
			break;
		}
		case "tool":
			if (IsMemoryTool(ev.Name))
			{
				CloseRound();
			}
			else if (ev.Name == "ask_user")
			{
				if (_roundBubble == null)
				{
					OpenRound(I18n.L("请在下面选一下"));
				}
			}
			else
			{
				CloseRound();
				Line(name, I18n.L("准备:") + label, ToolState.Prepare);
				_status.Text = I18n.L("正在执行操作...");
			}

			break;
		case "tool_run":
		case "tool_start":
			CloseRound();
			if (IsMemoryTool(ev.Name))
			{
				break;
			}

			Line(name, I18n.L("执行中:") + label, ToolState.Running);
			_status.Text = I18n.L("正在执行操作...");
			break;
		case "tool_done":
		{
			CloseRound();
			if (IsMemoryTool(ev.Name) && AddMemoryTag(ev))
			{
				break;
			}

			Line(name, I18n.L("完成:") + label, ToolState.Done);
			string taskId = TaskIdOf(ev);
			if (taskId.Length > 0 && !_taskCards.ContainsKey(taskId))
			{
				AddTaskCard(taskId, ev, label);
			}

			_status.Text = I18n.L("操作完成");
			break;
		}
		case "tool_skip":
			CloseRound();
			Line(name, I18n.L("已跳过:") + label, ToolState.Skipped);
			break;
		default:
			if (!string.IsNullOrWhiteSpace(ev.Label))
			{
				_status.Text = ev.Label;
			}

			break;
		}
	}

	private static bool IsMemoryTool(string? name)
	{
		return name == "remember" || name == "forget";
	}

	private static string PayloadStr(BridgeEvent ev, string prop)
	{
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty(prop, out JsonElement value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}

		return value.GetString() ?? "";
	}

	/// <summary>remember / forget 的结果显示成小标签;「已记住」带「撤销」。认不出结果时返回 false,按普通步骤显示。
	/// (【Windows 源】:2364-2427)</summary>
	private bool AddMemoryTag(BridgeEvent ev)
	{
		string result = PayloadStr(ev, "result");
		string text;
		bool undoable = false;
		string? remembered = null;
		if (ev.Name == "remember")
		{
			remembered = FileMemory.RememberedOf(result);
			if (remembered != null)
			{
				text = I18n.L("已记住:") + remembered;
				undoable = true;
			}
			else if (result.StartsWith("已经记过了", StringComparison.Ordinal))
			{
				int at = result.IndexOfAny(new char[2] { ':', ':' });
				text = I18n.L("已记住:") + ((at >= 0) ? result.Substring(at + 1).Trim() : result) + I18n.L("(之前就记过)");
			}
			else
			{
				return false;
			}
		}
		else
		{
			string? forgotten = AiChatOps.ForgottenOf(result);
			if (forgotten == null)
			{
				return false;
			}

			text = I18n.L("已忘记:") + forgotten;
		}

		TextBlock label = Ui.Txt(text, 12.0, bold: false, "B.AccentText");
		label.TextTrimming = TextTrimming.CharacterEllipsis;
		label.MaxWidth = 560.0;
		ToolTip.SetTip(label, text);
		SPanel row = Ui.H(6.0, AiIcons.Icon((ev.Name == "remember") ? "M19 21l-7-4-7 4V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z" : "M3 6h18 M8 6V4h8v2 M19 6l-1 14H6L5 6", 12.0, "B.AccentText").VCenter(), label.VCenter());
		Border tag = new Border
		{
			CornerRadius = new CornerRadius(999.0),
			Padding = new Thickness(10.0, 3.0, 6.0, 3.0),
			HorizontalAlignment = HorizontalAlignment.Left,
			Child = row
		};
		tag.Dyn(Border.BackgroundProperty, "B.AccentSoft");
		if (undoable)
		{
			Button? undo = null;
			undo = AiIcons.Flat("M3 7v6h6 M21 17a9 9 0 0 0-15-6.7L3 13", I18n.L("撤销"), I18n.L("删掉刚记下的这条记忆"), delegate
			{
				bool ok = FileMemory.Undo(remembered ?? "");
				row.Children.Remove(undo);
				label.Text = (ok ? I18n.L("已撤销:") : I18n.L("没找到这条记忆:")) + remembered;
				label.Dyn(TextBlock.ForegroundProperty, "B.InkMuted");
			}, "B.InkMuted", 11.0, pill: true);
			row.Children.Add(undo.VCenter());
		}

		Add(tag);
		_group = null;
		_status.Text = I18n.L("操作完成");
		return true;
	}

	/// <summary>工具干出的"后台任务"补一张卡(【Windows 源】:2429-2487)。</summary>
	private void AddTaskCard(string taskId, BridgeEvent ev, string label)
	{
		string tool = ev.Name ?? "";
		string instance = PayloadStr(ev, "instance");
		TaskCard card = new TaskCard(taskId, label, tool, instance);
		if (instance.Length > 0 && tool.StartsWith("install_", StringComparison.Ordinal))
		{
			if (tool == "install_game")
			{
				card.Launch = delegate
				{
					string version = AiChatOps.InstalledVersionOf(card.FinishMessage);
					PageBase.Run(async delegate
					{
						PageBase.Win?.Navigate("launch");
						if (PageBase.Win?.GetPage("launch") is LaunchPage launchPage)
						{
							await launchPage.LaunchNowAsync(instance, (version.Length > 0) ? version : null);
						}
					}, I18n.L("启动失败"));
				};
			}

			card.OpenFolder = delegate
			{
				PageBase.Run(async delegate
				{
					if (tool == "install_mod")
					{
						await PageBase.Api.CallAsync("open_mods_folder", new { instance });
					}
					else
					{
						await PageBase.Api.CallAsync("open_instance_folder", new
						{
							name = instance
						});
					}
				}, I18n.L("打开文件夹失败"));
			};
		}

		card.Retry = delegate(string reason)
		{
			PageBase.Run(() => SendTextAsync(I18n.L("刚才的后台任务「{0}」失败了({1}),请重试", card.TitleText, reason)));
		};
		_taskCards[taskId] = card;
		Add(card);
		_group = null;
		PageBase.Run(async delegate
		{
			string title = (await PageBase.Api.TryCallAsync("task_title", new
			{
				task_id = taskId
			}, "")) ?? "";
			if (!string.IsNullOrWhiteSpace(title) && title != taskId)
			{
				card.SetTitle(title);
			}
		});
	}

	/// <summary>后台任务 id 三处来源依次找(【Windows 源】:2489-2522)。</summary>
	private static string TaskIdOf(BridgeEvent ev)
	{
		if (!string.IsNullOrEmpty(ev.TaskId))
		{
			return ev.TaskId;
		}

		if (ev.Payload.ValueKind != JsonValueKind.Object)
		{
			return "";
		}

		if (ev.Payload.TryGetProperty("task_id", out JsonElement value) && value.ValueKind == JsonValueKind.String)
		{
			return value.GetString() ?? "";
		}

		if (!ev.Payload.TryGetProperty("result", out JsonElement value2) || value2.ValueKind != JsonValueKind.String)
		{
			return "";
		}

		string text = value2.GetString() ?? "";
		if (!text.StartsWith("{", StringComparison.Ordinal))
		{
			return "";
		}

		try
		{
			using JsonDocument doc = JsonDocument.Parse(text);
			return ((doc.RootElement.TryGetProperty("task_id", out JsonElement value3) && value3.ValueKind == JsonValueKind.String) ? (value3.GetString() ?? "") : "");
		}
		catch
		{
			return "";
		}
	}
}
