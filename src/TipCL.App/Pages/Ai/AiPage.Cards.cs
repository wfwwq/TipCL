using System.Collections.Generic;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.Models;
using TipCL.Pages.Ai;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// K9:三张"等你一句话"的卡片 —— 计划卡 / 确认卡 / 提问卡,以及承载它们的那条 dock。
/// 【Windows 源】<c>Pages\AiPage.cs:1908-1916</c>(<c>ClearDock</c>)、<c>:2563-2593</c>(<c>ShowPlan</c>)、
/// <c>:2595-2662</c>(<c>ShowConfirm</c>)、<c>:2664-2688</c>(<c>ShowAsk</c>)。
///
/// Windows → mac 的差异(逐条):
/// <list type="number">
/// <item><c>SetResourceReference</c> → <c>Dyn</c>;<c>Visibility</c> 不涉及;
///   <c>Motion.FadeIn(card, 200.0, 8.0)</c> 两平台同名同参;</item>
/// <item><c>_messages.Children.Insert(idx, sPanel)</c>:Avalonia 的 <c>Controls</c> 同样是可插入列表,
///   插入点(紧跟在当前步骤组之后)逐字照搬;</item>
/// <item><c>ConfirmCard.AllowAlways(ev)</c> / <c>ConfirmCard.Resolved</c> / <c>ConfirmedCard.Reject()</c> /
///   <c>AskCard(title, questions, submit)</c> / <c>PlanCard(items)</c> 全部是已落地件的既有成员
///   (PlanCard 的 items 是 <c>(string Title, string Status)</c> 列表,与 Windows 同形);</item>
/// <item><c>Path.GetDirectoryName</c> 之类一概不涉及;<c>AskCard</c> 的 <c>ApkBlock</c> 展开在它自己的文件里;</item>
/// <item><b>行为逐条照搬</b>:关掉这一轮、拿不到 <c>questions</c> 时**自动回一个空答案**、计划卡换新时先摘旧的、
///   答完在消息流里补一行"已允许/已拒绝",以及 <c>ClearDock</c> 里那句"先摸一下 Answered 再清"的语义
///   (mac 侧同样只是取一次属性,不改状态)。</item>
/// </list>
/// </summary>
internal sealed partial class AiPage
{
	/// <summary>把 dock 清空(【Windows 源】:1908-1916)。</summary>
	private void ClearDock()
	{
		if (_pendingConfirm != null)
		{
			_ = _pendingConfirm.Answered;
		}

		_pendingConfirm = null;
		_dock.Children.Clear();
	}

	/// <summary>计划卡(【Windows 源】:2563-2593)。</summary>
	private void ShowPlan(BridgeEvent ev)
	{
		List<(string Title, string Status)> items = new List<(string, string)>();
		if (ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("items", out JsonElement value) && value.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in value.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.Object)
				{
					string title = (item.TryGetProperty("title", out JsonElement t) ? (t.GetString() ?? "") : "");
					string status = (item.TryGetProperty("status", out JsonElement s) ? (s.GetString() ?? "pending") : "pending");
					if (!string.IsNullOrWhiteSpace(title))
					{
						items.Add((title, status));
					}
				}
			}
		}

		if (items.Count != 0)
		{
			if (_planCard != null)
			{
				_messages.Children.Remove(_planCard);
				_planCard = null;
			}

			_planCard = new PlanCard(items);
			Add(_planCard);
		}
	}

	/// <summary>等你确认那张卡(【Windows 源】:2595-2662)。</summary>
	private void ShowConfirm(BridgeEvent ev)
	{
		CloseRound();
		string label = (string.IsNullOrWhiteSpace(ev.Label) ? ev.Name : ev.Label);
		string reason = ((ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("reason", out JsonElement value) && value.ValueKind == JsonValueKind.String) ? (value.GetString() ?? "") : "");
		List<string> preview = new List<string>();
		if (ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("preview", out JsonElement value2) && value2.ValueKind == JsonValueKind.Object && value2.TryGetProperty("lines", out JsonElement value3) && value3.ValueKind == JsonValueKind.Array)
		{
			if (value2.TryGetProperty("head", out JsonElement value4) && value4.ValueKind == JsonValueKind.String)
			{
				string head = value4.GetString() ?? "";
				if (head.Length > 0)
				{
					preview.Add(head.StartsWith("@@", System.StringComparison.Ordinal) ? head : ("@@ " + head));
				}
			}

			foreach (JsonElement item in value3.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.String)
				{
					string line = item.GetString() ?? "";
					if (line.Length > 0)
					{
						preview.Add(line);
					}
				}
			}
		}

		ClearDock();
		ConfirmCard card = new ConfirmCard(ev.Name ?? "", label, reason, preview, ConfirmCard.AllowAlways(ev), delegate(bool ok, bool always, string scope)
		{
			PageBase.Run(async delegate
			{
				await PageBase.Api.TryCallAsync<object>("ai_confirm", new { ok, always, scope });
			});
		});
		card.Resolved += delegate(bool ok)
		{
			if (_pendingConfirm == card)
			{
				_pendingConfirm = null;
				_dock.Children.Remove(card);
			}

			TextBlock textBlock = Ui.Txt((ok ? I18n.L("已允许：") : I18n.L("已拒绝：")) + label, 12.0, bold: false, ok ? "B.InkMuted" : "B.Danger");
			textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
			SPanel sPanel = Ui.H(6.0, AiIcons.Icon(ok ? "M20 6L9 17l-5-5" : "M18 6L6 18 M6 6l12 12", 12.0, ok ? "B.Accent" : "B.Danger").VCenter(), textBlock.VCenter());
			sPanel.Margin = new Thickness(6.0, 0.0, 0.0, 0.0);
			if (_group != null && _group.Parent == _messages)
			{
				_messages.Children.Insert(_messages.Children.IndexOf(_group) + 1, sPanel);
			}
			else
			{
				_messages.Children.Add(sPanel);
			}

			_status.Text = (ok ? I18n.L("正在执行操作…") : I18n.L("已拒绝"));
			SyncPlaceholder();
			ScrollDown();
		};
		_pendingConfirm = card;
		_dock.Children.Add(card);
		Motion.FadeIn(card, 200.0, 8.0);
		_status.Text = I18n.L("等你确认");
		SyncPlaceholder();
		ScrollDown();
	}

	/// <summary>等你回答那张卡(【Windows 源】:2664-2688)。</summary>
	private void ShowAsk(BridgeEvent ev)
	{
		CloseRound();
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty("questions", out JsonElement value) || value.ValueKind != JsonValueKind.Array)
		{
			PageBase.Run(async delegate
			{
				await PageBase.Api.TryCallAsync<object>("ai_answer", new
				{
					result = (object?)null
				});
			});
			return;
		}

		string title = (ev.Payload.TryGetProperty("title", out JsonElement t) ? (t.GetString() ?? "") : "");
		AskCard el = new AskCard(title, value, delegate(object? result)
		{
			PageBase.Run(async delegate
			{
				await PageBase.Api.TryCallAsync<object>("ai_answer", new { result });
			});
		});
		Add(el);
		_status.Text = I18n.L("等你回答");
	}
}
