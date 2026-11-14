using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.Pages.Ai;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// C8a:一条对话气泡(= Windows <c>Pages\Bubble.cs</c>,454 行)。
///
/// 三种角色:<c>user</c>(右侧强调底色卡 + 编辑/重发/撤回)、<c>assistant</c>(Markdown 正文 + 「思考过程」折叠块 +
/// 复制/顶/踩/重试)、<c>error</c>(危险色卡 + 复制/重试)。含"编辑后重发"(输入框 ↔ 正文互换)与
/// <c>&lt;think&gt;</c> 分段的流式渲染(<c>SplitThink</c> / <c>ThinkOpen</c>,逐字照抄)。
///
/// 与 Windows 的差异(逐条):
/// <list type="number">
/// <item><c>Visibility</c> → <c>IsVisible</c>;<c>MouseEnter/Leave</c> → <c>PointerEntered/Exited</c>;</item>
/// <item><b>跨块换行</b>:WPF 的 <c>KeyEventArgs.ImeProcessedKey</c> 在 Avalonia 没有对等成员 ⇒ 改用
///   <c>e.Key != Key.ImeProcessed</c> 判"这一下不是输入法组合键"(语义最接近;<b>极窄边界</b>:WPF 里 IME
///   组合中的回车会被跳过,这里同样跳过);<c>Keyboard.Modifiers</c> → <c>e.KeyModifiers</c>;</item>
/// <item><b>写剪贴板</b>:WPF 静态 <c>Clipboard.SetText</c> → <c>TopLevel.GetTopLevel(this)?.Clipboard.SetTextAsync</c>
///   (走 <c>AiIcons.CopyText</c> 这条新加的小口径,失败只吞掉这一次复制,与 Windows 的 try/catch 同义);</item>
/// <item><c>Style</c>/<c>SetResourceReference</c> → <c>Dyn</c>;颜色键、圆角、内边距、字号、<c>MaxWidth</c>
///   (用户卡 600、整体 760)与 Windows **逐值相同**;</item>
/// <item>正文渲染走 <see cref="MdView"/>(C8a 自己重写的块级渲染器);公开面与成员名不变,
///   <c>AiFace</c> 那层调用点因此不需要改。</item>
/// </list>
/// </summary>
internal sealed class Bubble : Border
{
	private readonly MdView? _md;

	private readonly TextBlock? _plainBody;

	private readonly SPanel _thinking;

	private readonly TextBlock _thinkingLabel;

	private readonly ThinkFold? _think;

	private readonly bool _mine;

	private readonly bool _error;

	private readonly SPanel _actions;

	private readonly Button? _rewindBtn;

	private readonly Button? _retryBtn;

	private readonly Button? _editBtn;

	private readonly Border? _userBody;

	private readonly SPanel? _editor;

	private readonly TextBox? _editBox;

	/// <summary>发给后端的原文(不含"附图"等显示用后缀),编辑后重发时用来在对话里定位。</summary>
	internal string OrigText { get; set; } = "";

	/// <summary>回复中插队发的消息,撤回逻辑不把它当一轮。</summary>
	internal bool IsSteer { get; set; }

	internal bool Editing => _editor != null && _editor.IsVisible;

	private string _plain = "";

	private string _answer = "";

	private bool _live;

	internal ThinkFold? Think => _think;

	internal string Answer => _answer;

	internal string BodyText
	{
		get
		{
			if (_md == null)
			{
				return _plainBody?.Text ?? "";
			}
			return _md.Markdown;
		}
	}

	internal bool BodyShown => ((Control?)_md ?? _plainBody)?.IsVisible == true;

	internal bool IsMine => _mine;

	internal Action? Rewind
	{
		set
		{
			if (_rewindBtn != null)
			{
				_rewindBtn.Tag = value;
				_rewindBtn.IsVisible = value != null;
			}
		}
	}

	/// <summary>设置后显示「编辑」按钮;确认编辑时用新内容回调。</summary>
	internal Action<Bubble, string>? Edit
	{
		set
		{
			if (_editBtn != null)
			{
				_editBtn.Tag = value;
				_editBtn.IsVisible = value != null;
				if (value == null)
				{
					CancelEdit();
				}
			}
		}
	}

	internal Action? Retry
	{
		set
		{
			if (_retryBtn != null)
			{
				_retryBtn.Tag = value;
				_retryBtn.IsVisible = value != null;
			}
		}
	}

	/// <summary>【Windows 源】<c>:118-143</c>:把 <c>&lt;think&gt;</c> 段落抽出来(返回 各段 + 去掉思考后的正文)。</summary>
	internal static (List<string> Parts, string Answer) SplitThink(string? text)
	{
		List<string> parts = new List<string>();
		StringBuilder answer = new StringBuilder();
		string rest = text ?? "";
		while (true)
		{
			int start = rest.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
			if (start < 0)
			{
				break;
			}
			answer.Append(rest, 0, start);
			int end = rest.IndexOf("</think>", start + 7, StringComparison.OrdinalIgnoreCase);
			if (end < 0)
			{
				parts.Add(rest.Substring(start + 7));
				rest = "";
				break;
			}
			parts.Add(rest.Substring(start + 7, end - start - 7));
			rest = rest.Substring(end + 8);
		}
		answer.Append(rest);
		return (Parts: parts, Answer: answer.ToString().Trim());
	}

	/// <summary>【Windows 源】<c>:145-159</c>:一段文本里 <c>&lt;think&gt;</c> 是否多于 <c>&lt;/think&gt;</c>(= 还在思考)。</summary>
	private static bool ThinkOpen(string text)
	{
		return Count(text, "<think>") > Count(text, "</think>");
		static int Count(string s, string tag)
		{
			int n = 0;
			int at = 0;
			while ((at = s.IndexOf(tag, at, StringComparison.OrdinalIgnoreCase)) >= 0)
			{
				n++;
				at += tag.Length;
			}
			return n;
		}
	}

	public Bubble(string role, string text, Action<string> resend)
	{
		Bubble bubble = this;
		_plain = text ?? "";
		_answer = _plain;
		OrigText = _plain;
		_mine = role == "user";
		_error = role == "error";
		_thinkingLabel = Ui.Txt("", 12.5, bold: false, "B.InkMuted");
		_thinking = Ui.H(7.0,
			AiIcons.Icon("M12 18V5 M15 13a4.17 4.17 0 0 1-3-4 4.17 4.17 0 0 1-3 4 M17.598 6.5A3 3 0 1 0 12 5a3 3 0 1 0-5.598 1.5 M17.997 5.125a4 4 0 0 1 2.526 5.77 M18 18a4 4 0 0 0 2-7.464 M19.967 17.483A4 4 0 1 1 12 18a4 4 0 1 1-7.967-.517 M6 18a4 4 0 0 1-2-7.464 M6.003 5.125a4 4 0 0 0-2.526 5.77", 13.0).VCenter(),
			_thinkingLabel.VCenter());
		_thinking.IsVisible = false;
		_actions = Ui.H(2.0);
		if (_mine)
		{
			_plainBody = new TextBlock
			{
				Text = _plain,
				TextWrapping = TextWrapping.Wrap,
				FontSize = 13.5,
				LineHeight = 22.0,
			};
			_plainBody.Dyn(TextBlock.ForegroundProperty, "B.Ink");
			Border body = new Border
			{
				CornerRadius = new CornerRadius(10.0, 10.0, 4.0, 10.0),
				Padding = new Thickness(13.0, 8.0, 13.0, 9.0),
				Child = _plainBody,
				HorizontalAlignment = HorizontalAlignment.Right,
			};
			body.Dyn(Border.BackgroundProperty, "B.AccentSoft");
			body.ContextMenu = CopyMenu();
			_userBody = body;
			_editBox = Ui.Multi("", "", 72.0);
			_editBox.AcceptsReturn = true;
			_editBox.MinWidth = 320.0;
			_editBox.KeyDown += delegate(object? _, KeyEventArgs e)
			{
				if (e.Key == Key.Escape)
				{
					bubble.CancelEdit();
					e.Handled = true;
				}
				else if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Shift) == 0 && e.Key != Key.ImeProcessed)
				{
					bubble.ConfirmEdit();
					e.Handled = true;
				}
			};
			Button ok = Ui.Btn(I18n.L("发送"), BtnKind.Primary, delegate
			{
				bubble.ConfirmEdit();
			});
			Button cancel = Ui.Btn(I18n.L("取消"), BtnKind.Ghost, delegate
			{
				bubble.CancelEdit();
			});
			SPanel editButtons = Ui.H(6.0, Ui.Small(I18n.L("会撤回到这句话之前再重新发送")).VCenter(), cancel, ok);
			editButtons.HorizontalAlignment = HorizontalAlignment.Right;
			_editor = Ui.V(6.0, _editBox, editButtons);
			_editor.IsVisible = false;
			_actions.Children.Add(AiIcons.Flat(AiIcons.Retry, I18n.L("重新发送"), I18n.L("把这句话再发一次"), delegate
			{
				resend(bubble._plain);
			}, "B.InkMuted", 12.0, pill: true));
			_rewindBtn = AiIcons.Flat(AiIcons.Undo, I18n.L("撤回这一轮"), I18n.L("回退到这句话之前,并尽量还原这一轮改过的文件"), delegate
			{
				(bubble._rewindBtn?.Tag as Action)?.Invoke();
			}, "B.Warn", 12.0, pill: true);
			_rewindBtn.IsVisible = false;
			_actions.Children.Add(_rewindBtn);
			_editBtn = AiIcons.Flat(AiIcons.Pen, I18n.L("编辑"), I18n.L("改这句话,撤回到它之前再重新发送"), delegate
			{
				bubble.BeginEdit();
			}, "B.InkMuted", 12.0, pill: true);
			_editBtn.IsVisible = false;
			_actions.Children.Insert(0, _editBtn);
			_actions.HorizontalAlignment = HorizontalAlignment.Right;
			_actions.Opacity = 0.0;
			_actions.Margin = new Thickness(0.0, 4.0, 0.0, 0.0);
			base.HorizontalAlignment = HorizontalAlignment.Right;
			base.MaxWidth = 600.0;
			base.Background = Brushes.Transparent;
			base.Child = Ui.V(0.0, body, _editor, _actions);
			base.PointerEntered += delegate
			{
				bubble._actions.Opacity = 1.0;
			};
			base.PointerExited += delegate
			{
				bubble._actions.Opacity = 0.0;
			};
			return;
		}
		_think = new ThinkFold
		{
			IsVisible = false,
		};
		_md = new MdView();
		if (_error)
		{
			Grid head = Ui.G(null, "Auto,*");
			head.Add(AiIcons.Icon("M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0 M15 9l-6 6 M9 9l6 6", 14.0, "B.Danger").VCenter(), 0);
			head.Add(Ui.Txt(I18n.L("出错了"), 12.5, bold: true, "B.Danger").M(7.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
			_actions.Children.Add(AiIcons.Flat(AiIcons.Copy, null, I18n.L("复制"), Copy, "B.InkMuted", 13.0));
			_retryBtn = AiIcons.Flat(AiIcons.Retry, I18n.L("重试"), I18n.L("再试一次"), delegate
			{
				(bubble._retryBtn?.Tag as Action)?.Invoke();
			}, "B.InkMuted", 12.0);
			_retryBtn.IsVisible = false;
			_actions.Children.Add(_retryBtn);
			base.CornerRadius = new CornerRadius(10.0);
			base.Padding = new Thickness(12.0, 9.0, 12.0, 8.0);
			base.BorderThickness = new Thickness(1.0);
			this.Dyn(Border.BackgroundProperty, "B.DangerSoft");
			this.Dyn(Border.BorderBrushProperty, "B.Danger");
			base.Child = Ui.V(4.0, head, _md, _actions);
		}
		else
		{
			_actions.Children.Add(AiIcons.Flat(AiIcons.Copy, null, I18n.L("复制这条"), Copy, "B.InkFaint"));
			_actions.Children.Add(Vote(AiIcons.ThumbUp, I18n.L("有帮助")));
			_actions.Children.Add(Vote(AiIcons.ThumbDown, I18n.L("没帮助")));
			_retryBtn = AiIcons.Flat(AiIcons.Retry, null, I18n.L("重新生成这条回复"), delegate
			{
				(bubble._retryBtn?.Tag as Action)?.Invoke();
			}, "B.InkFaint");
			_retryBtn.IsVisible = false;
			_actions.Children.Add(_retryBtn);
			_actions.Margin = new Thickness(-4.0, 2.0, 0.0, 0.0);
			base.Background = Brushes.Transparent;
			base.Padding = new Thickness(0.0);
			base.Child = Ui.V(6.0, _thinking, _think, _md, _actions);
		}
		base.HorizontalAlignment = HorizontalAlignment.Stretch;
		base.MaxWidth = 760.0;
		Render(live: false);
	}

	/// <summary>【Windows 源】<c>:299-313</c>:进编辑态(输入框显示原文,正文与动作行收起)。</summary>
	internal void BeginEdit()
	{
		if (_editor == null || _editBox == null || _userBody == null)
		{
			return;
		}
		_editBox.Text = OrigText;
		_userBody.IsVisible = false;
		_actions.IsVisible = false;
		_editor.IsVisible = true;
		base.MaxWidth = 760.0;
		base.HorizontalAlignment = HorizontalAlignment.Stretch;
		_editBox.Focus();
		_editBox.CaretIndex = (_editBox.Text ?? "").Length;
	}

	/// <summary>【Windows 源】<c>:315-326</c>:退出编辑态,恢复正文。</summary>
	internal void CancelEdit()
	{
		if (_editor == null || _userBody == null || !_editor.IsVisible)
		{
			return;
		}
		_editor.IsVisible = false;
		_userBody.IsVisible = true;
		_actions.IsVisible = true;
		base.MaxWidth = 600.0;
		base.HorizontalAlignment = HorizontalAlignment.Right;
	}

	private void ConfirmEdit()
	{
		string text = _editBox?.Text?.Trim() ?? "";
		Action<Bubble, string>? action = _editBtn?.Tag as Action<Bubble, string>;
		if (text.Length == 0 || action == null)
		{
			return;
		}
		CancelEdit();
		action(this, text);
	}

	/// <summary>顶/踩:点过之后整行动作变实(与 Windows 同:只给一次反馈 Toast)。</summary>
	private Button Vote(string icon, string tip)
	{
		return AiIcons.Flat(icon, null, tip, delegate
		{
			foreach (Button item in _actions.Children.OfType<Button>())
			{
				item.Opacity = 1.0;
			}
			AppServices.Toast(I18n.L("谢谢反馈"), "", ToastKind.Success);
		}, "B.InkFaint");
	}

	/// <summary>用户气泡右键的「复制」(Avalonia 里 <c>ContextMenu</c> 是 <c>Control</c> 的属性,不需要手工 Open)。</summary>
	private ContextMenu CopyMenu()
	{
		ContextMenu menu = new ContextMenu();
		MenuItem item = new MenuItem
		{
			Header = I18n.L("复制"),
		};
		item.Click += delegate
		{
			Copy();
		};
		menu.Items.Add(item);
		return menu;
	}

	/// <summary>【Windows 源】<c>:367-381</c>:换正文(流式时 live=true;思考行在此时收起)。</summary>
	public void SetText(string text, bool live = false)
	{
		_plain = text ?? "";
		_live = live;
		if (_thinking.IsVisible)
		{
			Lucide.Shimmer(_thinkingLabel, on: false, "B.InkMuted");
			_thinking.IsVisible = false;
			if (_md != null)
			{
				_md.IsVisible = true;
			}
		}
		Render(live);
	}

	/// <summary>【Windows 源】<c>:383-422</c>:按角色/思考段渲染(live 时正文可能是空的,动作行收起)。</summary>
	private void Render(bool live)
	{
		if (_mine || _think == null || _md == null)
		{
			_answer = _plain;
			if (_plainBody != null)
			{
				_plainBody.Text = _plain;
			}
			else
			{
				_md?.SetMarkdown(_plain);
			}
			return;
		}
		(List<string> Parts, string Answer) split = SplitThink(_plain);
		List<string> parts = split.Parts;
		string text = (_answer = split.Answer);
		if (parts.Count > 0)
		{
			bool thinking = ThinkOpen(_plain);
			_think.SetThink(string.Join("\n\n", parts.Select(p => p.Trim()).Where(p => p.Length > 0)), live, live && thinking);
			_think.IsVisible = true;
			_md.IsVisible = !(live && text.Length == 0);
			_md.SetMarkdown((!live && text.Length == 0) ? "..." : text, live);
		}
		else
		{
			_think.IsVisible = false;
			if (!_thinking.IsVisible)
			{
				_md.IsVisible = true;
			}
			_md.SetMarkdown(text, live);
		}
		_actions.IsVisible = !(live || text.Length == 0 || text == "...");
	}

	/// <summary>【Windows 源】<c>:424-441</c>:进"思考中"(正文与折叠块都收起,标题行闪烁)。</summary>
	public void SetThinking(string text)
	{
		_plain = "";
		_answer = "";
		_md?.SetMarkdown("");
		_thinkingLabel.Text = text ?? "";
		_thinking.IsVisible = true;
		if (_md != null)
		{
			_md.IsVisible = false;
		}
		if (_think != null)
		{
			_think.IsVisible = false;
		}
		_actions.IsVisible = false;
		Lucide.Shimmer(_thinkingLabel, on: true, "B.InkMuted");
	}

	/// <summary>【Windows 源】<c>:443-453</c>:复制正文(有正文就复制正文,否则复制原文;失败只吞掉这一次)。</summary>
	private void Copy()
	{
		AiIcons.CopyText(this, string.IsNullOrEmpty(_answer) ? _plain : _answer);
	}
}
