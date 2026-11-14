using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using TipCL.Services;
using TipCL.Themes;

namespace TipCL.Pages;

/// <summary>
/// K5 附属件:AI 页的复合输入框(= Windows <c>Pages\ChatInput.cs</c>,81 行)。
/// 多行 + 自增高 + <c>Submitted</c>(Enter 发送 / Shift+Enter 换行)+ <c>KeyIntercept</c>
/// (发送逻辑之前先看一眼按键 —— 斜杠候选列表用它)。
///
/// 跟 Windows 逐条对齐的部分:<c>MinH=48</c> / <c>MaxH=140</c> 两个常量、<c>AcceptsReturn</c>、
/// 软换行、纵向滚动条按需、<c>Tag</c> 的占位文案、<c>Submitted</c> 只喂**去空白后非空**的文本、
/// <c>KeyIntercept</c> 返回 true 时吃掉这一下且**不发送**。
///
/// Windows → mac 的差异(逐条):
/// <list type="number">
/// <item><b>样式</b>:<c>Style = Ui.S("Input.Multi")</c> → <c>StyleKeys.Apply(this, "Input.Multi")</c>
///   (Avalonia 的样式靠类名命中,见 <see cref="Ui"/> 的差异表);</item>
/// <item><b>纵向滚动条</b>:WPF <c>TextBox.VerticalScrollBarVisibility</c> 在 Avalonia 是挂载属性
///   <c>ScrollViewer.SetVerticalScrollBarVisibility</c>;</item>
/// <item><b>自增高</b>:WPF 读 <c>TextBox.LineCount</c>(**含软换行后的行数**)算高度;Avalonia 11 的
///   <c>TextBox</c> 没有 <c>LineCount</c> ⇒ 这里按**显式换行数**(<c>'\n'</c> 的个数 + 1)算同一个公式
///   <c>行数 × FontSize × 1.6 + 22</c>,再夹到 48..140。**如实登记**:一行超长的软换行不会撑高
///   (超长时靠框内滚动,不丢字);</item>
/// <item><b>IME</b>:WPF 的 <c>TextCompositionManager</c> 三个事件与 <c>KeyEventArgs.ImeProcessedKey</c>
///   在 Avalonia 都没有对等物(<c>Bubble</c> 的跨块换行也是这么处理的)⇒ 只留
///   <see cref="ShouldSend"/> 里的 <c>Key.ImeProcessed</c> 那一判(最接近 "**这一下是输入法组合键,不是用户敲的回车**");
///   <c>LostFocus</c> 里清组合串那半句因此也**不需要了**(mac 侧根本没有可清的组合串缓存)。</item>
/// </list>
/// </summary>
internal sealed class ChatInput : TextBox
{
	private const double MinH = 48.0;

	private const double MaxH = 140.0;

	/// <summary>发送(参数是去掉首尾空白后的正文;空串**不会**触发)。Windows 同名同义。</summary>
	internal event Action<string>? Submitted;

	/// <summary>在发送逻辑之前看按键(斜杠命令候选列表用);返回 true 表示已处理。Windows 同名同义
	/// (mac 侧用 Avalonia 的 <see cref="Key"/>,斜杠那份用的是 Up/Down/Enter/Tab/Escape,五个键名一一对应)。</summary>
	internal Func<Key, bool>? KeyIntercept { get; set; }

	public ChatInput()
	{
		StyleKeys.Apply(this, "Input.Multi");
		Tag = I18n.L("问我要下什么、哪报错、模组怎么配...  Enter 发送,Shift+Enter 换行");
		AcceptsReturn = true;
		TextWrapping = TextWrapping.Wrap;
		ScrollViewer.SetVerticalScrollBarVisibility(this, ScrollBarVisibility.Auto);
		Height = MinH;
		TextChanged += delegate
		{
			Grow();
		};
	}

	/// <summary>
	/// 「这一下回车该不该发送」(= Windows <c>Services\ImeGuard.ShouldSend</c> 去掉 IME 组合串那两参后的口径):
	/// Shift 按下 ⇒ 换行;输入法吃掉这一下(<c>Key.ImeProcessed</c>)⇒ 不上屏不发送;不是回车 ⇒ 不管。
	/// </summary>
	internal static bool ShouldSend(Key key, bool shift)
	{
		if (shift)
		{
			return false;
		}

		if (key == Key.ImeProcessed)
		{
			return false;
		}

		return key == Key.Enter;
	}

	/// <summary>行数 → 高度(48..140)。Windows <c>Grow</c> 的同一个公式,行数来源见类注释第 3 条。</summary>
	private void Grow()
	{
		int lines = 1;
		string text = Text ?? "";
		foreach (char c in text)
		{
			if (c == '\n')
			{
				lines++;
			}
		}

		double value = lines * FontSize * 1.6 + 22.0;
		Height = Clamp.Of(value, MinH, MaxH);
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
		if (!shift && e.Key != Key.ImeProcessed && KeyIntercept != null && KeyIntercept(e.Key))
		{
			e.Handled = true;
			return;
		}

		if (ShouldSend(e.Key, shift))
		{
			string text = (Text ?? "").Trim();
			if (text.Length > 0)
			{
				Submitted?.Invoke(text);
			}

			e.Handled = true;
			return;
		}

		base.OnKeyDown(e);
	}
}
