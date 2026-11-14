using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.Models;
using TipCL.Pages.Ai;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// K6:一轮对话的生命周期分区 —— 起(发)/ 停 / 重试 / 撤回 / 编辑后重发 / 忙态。
/// 【Windows 源】<c>Pages\AiPage.cs:1667-1906</c>(<c>SendAsync</c> … <c>SetBusy</c>)与
/// <c>:1253-1352</c>(<c>AbandonRunAsync</c> / <c>BackendBusy</c> / <c>OpenRound</c> / <c>CloseRound</c> /
/// <c>ResetRunState</c> / <c>RunIsDisplayed</c> / <c>ChatIdOf</c>)。
///
/// Windows → mac 的差异(逐条):
/// <list type="number">
/// <item><c>TaskCompletionSource&lt;object&gt;</c> → <c>TaskCompletionSource&lt;object?&gt;</c>
///   (本仓启用可空注解,<c>TrySetResult(null)</c> 的那个 null 要有类型);</item>
/// <item><c>_input.Clear()</c> 两个平台同名同义;<c>Bubble("user", text, RetryFrom)</c> 与
///   <c>IsSteer</c> / <c>OrigText</c> 都是 C8a/C8b 已落地件的既有成员;</item>
/// <item><c>PageBase.Toast</c> / <c>PageBase.Run</c> / <c>PageBase.Api.TryCallAsync</c> 同名同参,
///   <c>OpResult</c> 走 mac 已有的 <c>TipCL.Models.OpResult</c>;</item>
/// <item><b>没有任何行为改造</b>:插队(<c>ai_steer</c>)成功/失败两条路、3 秒兜底等待、
///   发失败时把占位气泡摘掉换一条 error 气泡、<c>RewindAsync</c> 四种状态文案、<c>SetBusy</c> 收尾时
///   把所有 <c>StepGroup</c> 收口并清空 <c>_dock</c> —— 全部照搬。</item>
/// </list>
/// </summary>
internal sealed partial class AiPage
{
	/// <summary>切对话时把"正在跑的那一轮"放掉(【Windows 源】:1253-1277)。</summary>
	private async Task AbandonRunAsync()
	{
		if (_busy)
		{
			_abandoned = true;
			TaskCompletionSource<object?> ended = (_runEnded = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously));
			if (!BackendBusy(await PageBase.Api.TryCallAsync<JsonElement>("ai_stop")))
			{
				_roundBubble = null;
				_round.Clear();
				_stream.Clear();
				_runChatId = "";
				_status.Text = "";
				SetBusy(on: false);
				_runEnded?.TrySetResult(null);
				_runEnded = null;
			}
			else
			{
				await Task.WhenAny(ended.Task, Task.Delay(3000));
				ResetRunState();
				PageBase.Toast(I18n.L("已停止上一个对话的回合"), I18n.L("切换对话时正在运行的那一轮已中断"));
			}
		}
	}

	private static bool BackendBusy(JsonElement r)
	{
		if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("busy", out JsonElement value))
		{
			return value.ValueKind == JsonValueKind.True;
		}

		return false;
	}

	private Bubble OpenRound(string? thinking = null)
	{
		Bubble? roundBubble = _roundBubble;
		if (roundBubble != null)
		{
			if (thinking != null)
			{
				roundBubble.SetThinking(thinking);
			}

			return roundBubble;
		}

		_round.Clear();
		_group?.Refresh();
		_group = null;
		Bubble bubble = new Bubble("assistant", "...", RetryFrom);
		bubble.SetThinking(thinking ?? I18n.L("正在想..."));
		Add(bubble);
		return _roundBubble = bubble;
	}

	private void CloseRound()
	{
		if (_roundBubble != null)
		{
			_roundBubble.SetText(_round.ToString());
			_roundBubble = null;
			_round.Clear();
		}
	}

	private void ResetRunState()
	{
		_roundBubble = null;
		_round.Clear();
		_stream.Clear();
		_runChatId = "";
		_abandoned = false;
		_status.Text = "";
		SetBusy(on: false);
		_runEnded?.TrySetResult(null);
		_runEnded = null;
	}

	/// <summary>这一轮的事件该不该显示在当前对话上(【Windows 源】:1331-1343)。</summary>
	private bool RunIsDisplayed(BridgeEvent ev)
	{
		string text = ChatIdOf(ev);
		if (text.Length == 0)
		{
			text = _runChatId;
		}

		if (text.Length != 0)
		{
			return text == _store.ActiveId;
		}

		return true;
	}

	private static string ChatIdOf(BridgeEvent ev)
	{
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty("chat_id", out JsonElement value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}

		return value.GetString() ?? "";
	}

	/// <summary>发送输入框里的那一句(【Windows 源】:1667-1670)。</summary>
	private Task SendAsync()
	{
		return SendTextAsync(_input.Text);
	}

	private void RetryFrom(string text)
	{
		PageBase.Run(() => SendTextAsync(text));
	}

	private Task RetryAsync()
	{
		string text = LastUserText();
		if (text.Length != 0)
		{
			return SendTextAsync(text);
		}

		return Task.CompletedTask;
	}

	private void EditFrom(Bubble bubble, string newText)
	{
		PageBase.Run(() => EditResendAsync(bubble, newText), I18n.L("编辑重发失败"));
	}

	/// <summary>编辑后重发:用撤回检查点逻辑一轮轮回退到这句话之前,再用新内容重跑(【Windows 源】:1692-1741)。</summary>
	private async Task EditResendAsync(Bubble bubble, string newText)
	{
		if (_busy)
		{
			PageBase.Toast(I18n.L("回合还在跑"), I18n.L("先停止再编辑"), ToastKind.Warning);
			return;
		}

		string target = (bubble.OrigText ?? "").Trim();
		List<Bubble> mine = _messages.Children.OfType<Bubble>().Where((Bubble b) => b.IsMine).ToList();
		int at = mine.IndexOf(bubble);
		int occurrence = ((at < 0) ? 0 : mine.Skip(at + 1).Count((Bubble b) => (b.OrigText ?? "").Trim() == target));
		_store = (await PageBase.Api.TryCallAsync("ai_list_chats", null, _store)) ?? _store;
		List<(string Role, string Id, string Content)> msgs = (ActiveChat?.Messages ?? new List<AiMsgDto>()).Select((AiMsgDto m) => (m.Role, m.Id, m.Content)).ToList();
		int rounds = AiChatOps.RoundsToRewind(msgs, target, occurrence);
		if (rounds <= 0)
		{
			RenderMessages();
			PageBase.Toast(I18n.L("没法从这句重发"), I18n.L("这句话可能已被压缩进摘要,或是插队消息"), ToastKind.Warning);
			return;
		}

		int restored = 0;
		for (int i = 0; i < rounds; i++)
		{
			JsonElement r = await PageBase.Api.TryCallAsync<JsonElement>("ai_rewind", new
			{
				chat_id = (_store.ActiveId ?? "")
			});
			bool ok = r.ValueKind == JsonValueKind.Object && r.TryGetProperty("ok", out JsonElement okEl) && okEl.ValueKind == JsonValueKind.True;
			if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("restored_files", out JsonElement rf) && rf.TryGetInt32(out int n))
			{
				restored += n;
			}

			if (!ok)
			{
				_store = (await PageBase.Api.TryCallAsync("ai_list_chats", null, _store)) ?? _store;
				RenderMessages();
				string msg = ((r.ValueKind == JsonValueKind.Object && r.TryGetProperty("message", out JsonElement me) && me.ValueKind == JsonValueKind.String) ? (me.GetString() ?? "") : "");
				_status.Text = ((msg.Length > 0) ? msg : I18n.L("回退没有全部成功,已停下,请检查后再发"));
				return;
			}
		}

		_store = (await PageBase.Api.TryCallAsync("ai_list_chats", null, _store)) ?? _store;
		RenderMessages();
		await SendTextAsync(newText);
		if (restored > 0)
		{
			PageBase.Toast(I18n.L("已回退并重发"), I18n.L("{0} 个文件的改动已还原", restored), ToastKind.Success);
		}
	}

	/// <summary>真正的一次发送(【Windows 源】:1743-1825):忙则插队,否则起一轮。</summary>
	private async Task SendTextAsync(string raw, bool echo = true)
	{
		string text = (raw ?? "").Trim();
		if (text.Length == 0 && echo && _pendingImages.Count > 0)
		{
			text = I18n.L("看看这张图");
		}

		if (text.Length == 0)
		{
			return;
		}

		if (_busy && _pendingConfirm != null && !_pendingConfirm.Answered)
		{
			_pendingConfirm.Reject();
		}

		if (_busy)
		{
			JsonElement steer = await PageBase.Api.TryCallAsync<JsonElement>("ai_steer", new { text });
			if (steer.ValueKind == JsonValueKind.Object && steer.TryGetProperty("ok", out JsonElement value) && value.ValueKind == JsonValueKind.True)
			{
				if (echo)
				{
					_input.Clear();
					Add(new Bubble("user", text, RetryFrom)
					{
						IsSteer = true
					});
					PageBase.Toast(I18n.L("已插队"), I18n.L("这句话会立刻交给正在运行的助手"));
				}

				return;
			}

			TaskCompletionSource<object?> ended = _runEnded ?? (_runEnded = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously));
			await Task.WhenAny(ended.Task, Task.Delay(3000));
			if (_busy)
			{
				PageBase.Toast(I18n.L("上一条还在处理"), I18n.L("等它说完,或点「停止」再发"), ToastKind.Warning);
				return;
			}
		}

		if (echo)
		{
			_input.Clear();
		}

		_runChatId = _store.ActiveId;
		SetBusy(on: true);
		_status.Text = I18n.L("思考中...");
		if (echo)
		{
			Add(new Bubble("user", (_pendingImages.Count > 0) ? (text + "\n" + I18n.L("[附图 {0} 张]", _pendingImages.Count)) : text, RetryFrom)
			{
				OrigText = text
			});
		}

		_stream.Clear();
		_roundBubble = OpenRound();
		Dictionary<string, object> launch = new Dictionary<string, object>
		{
			["instance"] = PageBase.Win?.Prefs.CatalogInstance ?? "",
			["username"] = PageBase.Win?.Prefs.LastUser ?? ""
		};
		List<string> images = (echo ? _pendingImages.ToList() : new List<string>());
		_pendingImages.Clear();
		SyncImageChip();
		OpResult? result = await PageBase.Api.TryCallAsync<OpResult>("ai_send", new
		{
			text = text,
			chat_id = _store.ActiveId,
			launch = launch,
			images = images
		});
		if (result == null || !result.Ok)
		{
			string? message = result?.Message;
			string shown = ((message != null && message.Length > 0) ? message : I18n.L("后端没有响应,请稍后再试"));
			if (_roundBubble != null)
			{
				_messages.Children.Remove(_roundBubble);
			}

			Add(new Bubble("error", shown, RetryFrom));
			ResetRunState();
			_status.Text = shown;
		}
	}

	private async Task StopAsync()
	{
		JsonElement r = await PageBase.Api.TryCallAsync<JsonElement>("ai_stop");
		_status.Text = I18n.L("已停止");
		if (!BackendBusy(r))
		{
			ResetRunState();
			_status.Text = I18n.L("已停止");
		}
	}

	/// <summary>撤回上一轮(【Windows 源】:1838-1880):先回退对话,再按回包说清磁盘改动还原到什么程度。</summary>
	private async Task RewindAsync()
	{
		JsonElement r = await PageBase.Api.TryCallAsync<JsonElement>("ai_rewind", new
		{
			chat_id = (_store.ActiveId ?? "")
		});
		_store = (await PageBase.Api.TryCallAsync("ai_list_chats", null, _store)) ?? _store;
		RenderMessages();
		bool ok = r.ValueKind == JsonValueKind.Object && r.TryGetProperty("ok", out JsonElement value) && value.GetBoolean();
		bool diskChanged = r.ValueKind == JsonValueKind.Object && r.TryGetProperty("disk_changed", out JsonElement value2) && value2.GetBoolean();
		if (!ok && diskChanged)
		{
			_status.Text = I18n.L("对话已回退,但磁盘改动没能全部还原,请手动检查相关文件");
			return;
		}

		if (!ok)
		{
			string message = ((r.ValueKind == JsonValueKind.Object && r.TryGetProperty("message", out JsonElement value3) && value3.ValueKind == JsonValueKind.String) ? (value3.GetString() ?? "") : "");
			_status.Text = ((message.Length > 0) ? message : I18n.L("撤回失败"));
			return;
		}

		int restored = 0;
		if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("restored_files", out JsonElement value4) && value4.TryGetInt32(out int value5))
		{
			restored = value5;
		}

		if (diskChanged)
		{
			_status.Text = string.Format(I18n.L("已撤回:{0} 个文件的改动已还原"), restored);
		}
		else if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("rollbackable", out JsonElement value6) && value6.ValueKind == JsonValueKind.False)
		{
			_status.Text = I18n.L("已撤回:只回退了对话,这一轮的磁盘改动无法自动还原");
		}
		else
		{
			_status.Text = I18n.L("已撤回:只回退了对话,这一轮没有可还原的磁盘改动");
		}
	}

	/// <summary>忙态开关(【Windows 源】:1882-1906):发/停按钮互换、重试撤回可用性、收尾时把所有步骤组收口。</summary>
	private void SetBusy(bool on)
	{
		bool changed = _busy != on;
		_busy = on;
		_send.IsEnabled = !on;
		_stop.IsEnabled = on;
		_sendStop.Content = (on ? _stop : _send);
		_rewind.IsEnabled = !on && LastUserText().Length > 0;
		_retry.IsEnabled = !on && LastUserText().Length > 0;
		if (!on)
		{
			foreach (StepGroup item in _messages.Children.OfType<StepGroup>())
			{
				item.Complete();
			}

			_group = null;
			ClearDock();
		}

		SyncPlaceholder();
		SyncTurnActions();
		if (changed)
		{
			RenderChats();
		}
	}
}
