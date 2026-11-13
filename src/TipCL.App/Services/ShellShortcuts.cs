using System;
using Avalonia.Controls;
using Avalonia.Input;

namespace TipCL.Services;

/// <summary>
/// 快捷键映射(纯函数,方便单测)。来源:Windows 版 <c>MainWindow.xaml.cs</c> 的
/// <c>OnShortcut</c> + GOAL §3 的窗口行(「快捷键 Ctrl→⌘:⌘1...9 切页、⌘K AI、⌘, 设置、⌘R 刷新」)。
///
/// Ctrl → ⌘ 的对应关系:macOS 上 <c>KeyModifiers.Meta</c> 就是 ⌘;另外**同时接受 Control**,
/// 这样同一套快捷键在本机(Windows)手工调也能用,行为与 Windows 原实现一致。
/// Windows 的 F5 刷新在 mac 侧换成 ⌘R(GOAL 指定)。
/// </summary>
internal static class ShellShortcuts
{
	/// <summary>⌘1...⌘9 最多切 9 页(Windows 的 <c>Key.D1..D9</c> 分支一对一)。</summary>
	internal const int PageShortcutCount = 9;

	/// <summary>第 <paramref name="index"/> 个页面快捷键(0 基):⌘1 = index 0。</summary>
	internal static KeyGesture PageGesture(int index)
	{
		if (index < 0 || index >= PageShortcutCount)
		{
			throw new ArgumentOutOfRangeException(nameof(index), index, "页面快捷键只有 ⌘1...⌘9");
		}
		return new KeyGesture((Key)((int)Key.D1 + index), KeyModifiers.Meta);
	}

	internal static KeyGesture AiGesture => new KeyGesture(Key.K, KeyModifiers.Meta);

	internal static KeyGesture SettingsGesture => new KeyGesture(Key.OemComma, KeyModifiers.Meta);

	internal static KeyGesture RefreshGesture => new KeyGesture(Key.R, KeyModifiers.Meta);

	/// <summary>这条按键是不是"加速键"(⌘ 或 Ctrl;Windows 是 <c>Keyboard.Modifiers == Control</c>)。</summary>
	internal static bool IsAccelerator(KeyModifiers modifiers)
	{
		return modifiers == KeyModifiers.Meta || modifiers == KeyModifiers.Control;
	}

	/// <summary>⌘1...⌘9 → 0...8;其它键返回 -1(Windows 的 key switch 表达式)。</summary>
	internal static int PageIndex(Key key)
	{
		int offset = (int)key - (int)Key.D1;
		return (offset >= 0 && offset < PageShortcutCount) ? offset : -1;
	}

	/// <summary>
	/// ⌘K 抢不抢:Windows 的原规则是「对话框开着、或者焦点在文本框/密码框里就不抢」。
	/// mac 侧等价条件:<paramref name="dialogOpen"/> = <c>Dlg.AnyOpen</c>,
	/// 焦点是 <see cref="TextBox"/>(Avalonia 的密码输入也是 TextBox)时不抢。
	/// </summary>
	internal static bool CanStealAiShortcut(Control? focused, bool dialogOpen)
	{
		if (dialogOpen)
		{
			return false;
		}
		return focused is not TextBox;
	}
}
