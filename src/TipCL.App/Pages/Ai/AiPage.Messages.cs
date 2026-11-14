using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TipCL.Models;
using TipCL.Pages.Ai;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// K4:消息区渲染分区(【Windows 源】<c>Pages\AiPage.cs:1354-1665</c>)——
/// <c>RenderMessages</c>(历史消息逐条铺成气泡 / 压缩摘要分隔线 / 计划卡)、
/// <c>SyncTurnActions</c>(重试 / 撤回两个动作只挂在最后一条上)、<c>EmptyState</c>(四张快捷卡)、
/// 两个静态小件 <c>Divider</c> / <c>Note</c>,以及 <c>AddWhoIfNeeded</c> / <c>ScrollDown</c> /
/// <c>Add</c>(增量上屏)与 <c>LastUserText</c>。
///
/// Windows → mac 的差异(逐条):
/// <list type="number">
/// <item><c>FrameworkElement</c> / <c>UIElement</c> → <c>Control</c>;<c>Visibility.Collapsed</c> → 不涉及;</item>
/// <item><c>el is FrameworkElement { Parent: Panel parent }</c> → <c>el.Parent is Panel</c>
///   (Avalonia 的 <c>StyledElement.Parent</c> 就是逻辑父级,与 WPF 的 <c>Parent</c> 同一含义);</item>
/// <item><c>el is Bubble { IsMine: not false }</c> → <c>el is Bubble b &amp;&amp; b.IsMine</c>
///   (同一个判据,Avalonia 侧 <c>IsMine</c> 是 <c>_mine</c> 的只读投影);</item>
/// <item><c>Dispatcher.BeginInvoke((Action)…)</c> → <c>Dispatcher.UIThread.Post(…)</c>(C2c 既有口径);
///   <c>_scroll.ScrollToEnd()</c> 原样可用(<c>SmoothScroll</c> 是 Avalonia 的 <c>ScrollViewer</c> 子类);</item>
/// <item><c>UniformGrid</c> 两平台同名(Avalonia 在 <c>Avalonia.Controls.Primitives</c>);
///   <c>MouseEnter/MouseLeave</c> → <c>PointerEntered/PointerExited</c>;
///   <c>SetResourceReference(Control.BorderBrushProperty, …)</c> → <c>Dyn(TemplatedControl.BorderBrushProperty, …)</c>;</item>
/// <item><c>AiIcons.Icon(...).Mid()</c> / <c>.TopLeft()</c> 是本仓既有的两个对齐扩展(AiIcons 里);</item>
/// <item><b>压缩分隔线</b>:Windows 里"老对话每轮都存一条摘要标记"的兼容口径(<c>LastOrDefault</c> 只画最后一条)
///   逐字照搬,没改;</item>
/// <item><c>_titleText.Text</c> 与 <c>_retry/_rewind</c> 的可用性同样照搬。</item>
/// </list>
/// </summary>
internal sealed partial class AiPage
{
	/// <summary>把当前对话的整段历史重画一遍(【Windows 源】:1354-1443)。</summary>
	private void RenderMessages()
	{
		_messages.Children.Clear();
		_toolLines.Clear();
		_taskCards.Clear();
		_roundBubble = null;
		_round.Clear();
		_stream.Clear();
		_group = null;
		_needWho = true;
		_lastUser = null;
		_emptyView = null;
		_planCard = null;
		AiChatDto? activeChat = ActiveChat;
		_titleText.Text = ((activeChat == null || string.IsNullOrWhiteSpace(activeChat.Title)) ? I18n.L("新对话") : activeChat.Title);
		if (activeChat == null || activeChat.Messages.Count == 0)
		{
			_emptyView = EmptyState();
			_messages.Children.Add(_emptyView);
			_retry.IsEnabled = false;
			_rewind.IsEnabled = false;
			return;
		}

		Bubble? lastAssistant = null;
		// 只画最后一条摘要分隔线:旧版本每轮都存一条摘要标记,老对话里会一句一条
		AiMsgDto? lastCompact = activeChat.Messages.LastOrDefault((AiMsgDto m) => m.Role == "user" && (m.Id ?? "").StartsWith("compact_", StringComparison.Ordinal));
		foreach (AiMsgDto message in activeChat.Messages)
		{
			if (message.Role == "user" && (message.Id ?? "").StartsWith("compact_", StringComparison.Ordinal))
			{
				if (message == lastCompact)
				{
					_messages.Children.Add(Divider(I18n.L("更早的对话已压缩成摘要")));
				}

				continue;
			}

			bool known;
			switch (message.Role)
			{
			case "user":
			case "assistant":
			case "error":
				known = true;
				break;
			default:
				known = false;
				break;
			}

			if (known)
			{
				string text = message.Content;
				if (!string.IsNullOrWhiteSpace(message.Note) && !text.Contains(message.Note, StringComparison.Ordinal))
				{
					text = (text + "\n\n" + message.Note).Trim();
				}

				Bubble bubble = new Bubble(message.Role, text, RetryFrom);
				bubble.OrigText = message.Content;
				bubble.IsSteer = message.Id.StartsWith("steer_", StringComparison.Ordinal);
				if (message.Role == "user")
				{
					_needWho = true;
					_lastUser = bubble;
					lastAssistant = null;
				}
				else
				{
					AddWhoIfNeeded(animate: false);
					lastAssistant = bubble;
				}

				_messages.Children.Add(bubble);
			}
		}

		AiPlanDto? plan = activeChat.Plan;
		if (plan != null)
		{
			List<AiPlanItemDto> items = plan.Items;
			if (items != null && items.Count > 0)
			{
				_planCard = new PlanCard((from i in activeChat.Plan.Items
					where !string.IsNullOrWhiteSpace(i.Title)
					select (Title: i.Title, Status: i.Status)).ToList());
				_messages.Children.Add(_planCard);
			}
		}

		Motion.Stagger(_messages, 18.0, 200.0, 8.0);
		_retry.IsEnabled = !_busy && LastUserText().Length > 0;
		_rewind.IsEnabled = _retry.IsEnabled;
		SyncTurnActions(lastAssistant);
		ScrollDown();
	}

	/// <summary>重试 / 撤回 / 编辑三个动作只挂在"该挂的那一条"上(【Windows 源】:1445-1487)。</summary>
	private void SyncTurnActions(Bubble? lastAssistant = null)
	{
		foreach (Bubble item in _messages.Children.OfType<Bubble>())
		{
			if (item.IsMine)
			{
				item.Rewind = null;
				item.Edit = null;
			}
			else
			{
				item.Retry = null;
			}
		}

		if (_busy)
		{
			return;
		}

		if (_lastUser != null)
		{
			_lastUser.Rewind = delegate
			{
				PageBase.Run(RewindAsync);
			};
		}

		foreach (Bubble item2 in _messages.Children.OfType<Bubble>())
		{
			if (item2.IsMine && !item2.IsSteer)
			{
				item2.Edit = EditFrom;
			}
		}

		Bubble? bubble = lastAssistant ?? _messages.Children.OfType<Bubble>().LastOrDefault((Bubble b) => !b.IsMine);
		int at = ((bubble == null) ? (-1) : _messages.Children.IndexOf(bubble));
		int userAt = ((_lastUser == null) ? (-1) : _messages.Children.IndexOf(_lastUser));
		if (bubble != null && at > userAt && LastUserText().Length > 0)
		{
			bubble.Retry = delegate
			{
				PageBase.Run(RetryAsync);
			};
		}
	}

	/// <summary>空对话时那一屏:徽标 + 两行文案 + 四张快捷卡(【Windows 源】:1489-1565)。</summary>
	private Control EmptyState()
	{
		Border border = new Border
		{
			Width = 52.0,
			Height = 52.0,
			CornerRadius = new CornerRadius(10.0),
			Child = AiIcons.Icon("M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8z", 26.0, "B.OnAccentStrong").Mid(),
			HorizontalAlignment = HorizontalAlignment.Center
		};
		border.Dyn(Border.BackgroundProperty, "B.AccentStrong");
		TextBlock textBlock = Ui.Txt(I18n.L("今天想折腾点什么？"), 20.0, bold: true, "B.Ink");
		textBlock.HorizontalAlignment = HorizontalAlignment.Center;
		textBlock.Margin = new Thickness(0.0, 14.0, 0.0, 0.0);
		TextBlock textBlock2 = Ui.Txt(I18n.L("下游戏、装模组、读崩溃日志，改动前都会先问你"), 13.0, bold: false, "B.InkMuted");
		textBlock2.HorizontalAlignment = HorizontalAlignment.Center;
		textBlock2.Margin = new Thickness(0.0, 6.0, 0.0, 18.0);
		UniformGrid uniformGrid = new UniformGrid
		{
			Columns = 2,
			HorizontalAlignment = HorizontalAlignment.Center,
			Width = 620.0
		};
		(string Title, string Sub, string Icon, string Prompt)[] quickStarts = QuickStarts;
		for (int i = 0; i < quickStarts.Length; i++)
		{
			(string Title, string Sub, string Icon, string Prompt) tuple = quickStarts[i];
			string item = tuple.Title;
			string item2 = tuple.Sub;
			string item3 = tuple.Icon;
			string prompt = tuple.Prompt;
			Border border2 = new Border
			{
				Width = 30.0,
				Height = 30.0,
				CornerRadius = new CornerRadius(8.0),
				Child = AiIcons.Icon(item3, 16.0, "B.AccentText").Mid()
			};
			border2.Dyn(Border.BackgroundProperty, "B.AccentSoft");
			Grid grid = Ui.G(null, "Auto,*");
			grid.Add(border2.TopLeft(), 0);
			grid.Add(Ui.V(2.0, Ui.Txt(item, 13.0, bold: true, "B.Ink"), Ui.Txt(item2, 12.0, bold: false, "B.InkMuted")).M(11.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
			Button card = new Button
			{
				Template = AiIcons.FlatButtonTemplate(),
				Content = grid,
				Padding = new Thickness(14.0, 12.0, 14.0, 12.0),
				Margin = new Thickness(5.0),
				BorderThickness = new Thickness(1.0),
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Cursor = new Cursor(StandardCursorType.Hand),
				Focusable = false
			};
			grid.HorizontalAlignment = HorizontalAlignment.Left;
			card.Dyn(TemplatedControl.BorderBrushProperty, "B.Line");
			card.Dyn(TemplatedControl.BackgroundProperty, "B.Paper");
			card.PointerEntered += delegate
			{
				card.Dyn(TemplatedControl.BackgroundProperty, "B.Hover");
			};
			card.PointerExited += delegate
			{
				card.Dyn(TemplatedControl.BackgroundProperty, "B.Paper");
			};
			card.Click += delegate
			{
				PageBase.Run(() => SendTextAsync(prompt));
			};
			uniformGrid.Children.Add(card);
		}

		SPanel sPanel = Ui.V(0.0, border, textBlock, textBlock2, uniformGrid);
		sPanel.HorizontalAlignment = HorizontalAlignment.Center;
		sPanel.VerticalAlignment = VerticalAlignment.Center;
		sPanel.Margin = new Thickness(0.0, 90.0, 0.0, 40.0);
		return sPanel;
	}

	/// <summary>「更早的对话已压缩成摘要」那道分隔线(【Windows 源】:1567-1587)。</summary>
	private static Control Divider(string text)
	{
		Grid grid = Ui.G(null, "*,Auto,*");
		Border border = new Border
		{
			Height = 1.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		Border border2 = new Border
		{
			Height = 1.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		border.Dyn(Border.BackgroundProperty, "B.Line");
		border2.Dyn(Border.BackgroundProperty, "B.Line");
		grid.Add(border, 0);
		grid.Add(Ui.H(6.0, AiIcons.Icon("M3 3h18a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z M4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8 M10 12h4", 12.0, "B.InkFaint").VCenter(), Ui.Txt(text, 11.5, bold: false, "B.InkFaint").VCenter()).M(10.0, 0.0, 10.0), 0, 1);
		grid.Add(border2, 0, 2);
		grid.Margin = new Thickness(0.0, 2.0, 0.0, 2.0);
		return grid;
	}

	/// <summary>一条警示条(【Windows 源】:1589-1597;备用模型降级时插进消息流)。</summary>
	private static Control Note(string text, string icon = "M10.3 3.9L1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z M12 9v4 M12 17h.01")
	{
		Border border = new Border();
		border.CornerRadius = new CornerRadius(8.0);
		border.Padding = new Thickness(10.0, 6.0, 10.0, 6.0);
		border.Child = Ui.H(8.0, AiIcons.Icon(icon, 13.0, "B.Warn").VCenter(), Ui.Txt(text, 12.0, bold: false, "B.Warn").Wrap().VCenter());
		border.Dyn(Border.BackgroundProperty, "B.WarnSoft");
		return border;
	}

	/// <summary>助手第一次开口时补一个「TipCL 助手」身份行(【Windows 源】:1599-1621)。</summary>
	private void AddWhoIfNeeded(bool animate = true)
	{
		if (_needWho)
		{
			_needWho = false;
			Border border = new Border
			{
				Width = 22.0,
				Height = 22.0,
				CornerRadius = new CornerRadius(6.0),
				Child = AiIcons.Icon("M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8z", 12.0, "B.OnAccentStrong").Mid()
			};
			border.Dyn(Border.BackgroundProperty, "B.AccentStrong");
			SPanel sPanel = Ui.H(7.0, border.VCenter(), Ui.Txt(I18n.L("TipCL 助手"), 12.0, bold: false, "B.InkMuted").VCenter());
			sPanel.Margin = new Thickness(0.0, 6.0, 0.0, -4.0);
			sPanel.Tag = "who";
			_messages.Children.Add(sPanel);
			if (animate)
			{
				Motion.FadeIn(sPanel, 200.0, 8.0);
			}
		}
	}

	/// <summary>滚到底(【Windows 源】:1623-1629;排队到 UI 线程之后,等布局量完再滚)。</summary>
	private void ScrollDown()
	{
		Dispatcher.UIThread.Post(delegate
		{
			_scroll.ScrollToEnd();
		});
	}

	/// <summary>往消息流里加一件(【Windows 源】:1631-1660):空态先撤、已在别处先摘、用户气泡重置身份行。</summary>
	private void Add(Control el)
	{
		if (_emptyView != null)
		{
			_messages.Children.Remove(_emptyView);
			_emptyView = null;
		}

		if (el.Parent is Panel parent)
		{
			if (parent == _messages)
			{
				ScrollDown();
				return;
			}

			parent.Children.Remove(el);
		}

		if (el is Bubble bubble && bubble.IsMine)
		{
			_needWho = true;
			_group = null;
			_lastUser = bubble;
		}
		else if (!(el is ConfirmCard))
		{
			AddWhoIfNeeded();
		}

		_messages.Children.Add(el);
		Motion.FadeIn(el, 200.0, 8.0);
		ScrollDown();
	}

	/// <summary>最后一条"真人说的话"(【Windows 源】:1662-1665;不含插队的 <c>steer_</c>)。</summary>
	private string LastUserText()
	{
		return ActiveChat?.Messages.LastOrDefault((AiMsgDto m) => m.Role == "user" && !string.IsNullOrWhiteSpace(m.Content) && !m.Id.StartsWith("steer_", StringComparison.Ordinal))?.Content ?? "";
	}
}
