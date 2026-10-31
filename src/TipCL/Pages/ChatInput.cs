using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TipCL.Services;

namespace TipCL.Pages;

internal sealed class ChatInput : TextBox
{
	private const double MinH = 48.0;

	private const double MaxH = 140.0;

	private string _composition = "";

	public event Action<string>? Submitted;

	/// <summary>在发送逻辑之前看按键（斜杠命令候选列表用）；返回 true 表示已处理。</summary>
	public Func<Key, bool>? KeyIntercept { get; set; }

	public ChatInput()
	{
		base.Style = Ui.S("Input.Multi");
		base.Tag = I18n.L("问我要下什么、哪报错、模组怎么配…  Enter 发送，Shift+Enter 换行");
		base.AcceptsReturn = true;
		base.TextWrapping = TextWrapping.Wrap;
		base.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
		base.Height = 48.0;
		base.TextChanged += delegate
		{
			Grow();
		};
		TextCompositionManager.AddTextInputStartHandler(this, OnComposition);
		TextCompositionManager.AddTextInputUpdateHandler(this, OnComposition);
		TextCompositionManager.AddTextInputHandler(this, OnCompositionEnd);
		base.LostFocus += delegate
		{
			_composition = "";
		};
	}

	private void OnComposition(object sender, TextCompositionEventArgs e)
	{
		_composition = e.TextComposition?.CompositionText ?? "";
	}

	private void OnCompositionEnd(object sender, TextCompositionEventArgs e)
	{
		_composition = "";
	}

	private void Grow()
	{
		double value = (double)((base.LineCount <= 0) ? 1 : base.LineCount) * base.FontSize * 1.6 + 22.0;
		base.Height = Clamp.Of(value, 48.0, 140.0);
	}

	protected override void OnPreviewKeyDown(KeyEventArgs e)
	{
		bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
		if (_composition.Length == 0 && e.Key != Key.ImeProcessed && e.ImeProcessedKey == Key.None && !shift && KeyIntercept != null && KeyIntercept(e.Key))
		{
			e.Handled = true;
			return;
		}
		if (ImeGuard.ShouldSend(e.Key, e.ImeProcessedKey, shift, _composition))
		{
			string text = base.Text?.Trim() ?? "";
			if (text.Length > 0)
			{
				this.Submitted?.Invoke(text);
			}
			e.Handled = true;
		}
		else
		{
			base.OnPreviewKeyDown(e);
		}
	}
}
