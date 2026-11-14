using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using TipCL.Services;

namespace TipCL.Pages.Plugins;

/// <summary>
/// C7b:Windows 版 <c>Pages\Plugins\PluginDashCards.cs</c>(5124 字节 / 202 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// 首页插件卡片(kind <c>dashCard</c>):卡片类型 <c>plugin:&lt;plugin&gt;:&lt;id&gt;</c>,
/// 出现在「编辑布局 → 添加卡片」里;宿主按类型缓存卡片内容,插件重载 / 禁用时只换外壳里的内容。
///
/// Windows → mac 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><b>mac 永远走 Windows 那条"没有 WebView"的分支</b> —— 这不是新设计:Windows 自己就有
///   <c>if (!c.Native &amp;&amp; !WebViewHost.Available) { 「在浏览器中打开」+ 提示 }</c>(第 181-189 行)。
///   mac 按 GOAL.md 第 34 行不嵌 WebView(见 D-154),所以**那条分支就是 mac 的常态**;
///   它的内容由 <see cref="NativePluginUi.Content"/> 统一给出(网页贡献 → 如实内容卡 + 「在浏览器中打开」),
///   这里不再需要 <c>PluginWebView</c> / <c>WebViewHost.Available</c> / <c>WebViewHost.WithParams</c>;</item>
/// <item><c>WebViewHost.AvailabilityChanged</c>(第 93-99 行)mac 没有对应物 ⇒ **如实不挂**:
///   没有内嵌浏览器,就不存在"可用性变化后要重建卡片"这件事(原位留注释);</item>
/// <item><c>HookOwner</c> / <c>ApplyEdit</c>(第 122-158 行)那一整套是为"编辑布局时把 WebView 藏起来,
///   免得它盖住拖拽手柄"服务的 —— mac 卡片里没有 WebView ⇒ **整套如实不搬**(留注释说明它的原用途);</item>
/// <item><c>VisualTreeHelper.GetParent</c> → Avalonia 的 <c>GetVisualParent()</c>(只在需要时才用,本文件已不用);
///   <c>DependencyObject</c> / <c>DependencyPropertyChangedEventArgs</c> 亦无对应需要;</item>
/// <item><c>(Content as IDisposable)?.Dispose()</c> → mac 的内容是 <c>Control</c>(不是可释放的 WebView)⇒ 直接换掉内容,不调 Dispose;</item>
/// <item><c>Loaded</c> 事件在 Avalonia 同名可用(本文件已不需要);<c>ContentControl.Focusable</c> / 两个 ContentAlignment 逐字保留。</item>
/// </list>
/// </summary>
public static class PluginDashCards
{
	public const string Prefix = "plugin:";

	private static readonly Dictionary<string, CardShell> _shells = new Dictionary<string, CardShell>();

	private static bool _hooked;

	public static bool IsPluginType(string? type)
	{
		return type != null && type.StartsWith(Prefix, StringComparison.Ordinal);
	}

	public static string TypeOf(UiContribution c)
	{
		return Prefix + c.Plugin + ":" + c.Id;
	}

	public static UiContribution? Find(string type)
	{
		return PluginUi.OfKind("dashCard").FirstOrDefault((UiContribution c) => TypeOf(c) == type);
	}

	/// <summary>可以添加的插件卡片(类型, 标题, 默认宽高 px)。</summary>
	public static IEnumerable<(string Type, string Title, int W, int H)> Available()
	{
		foreach (UiContribution c in PluginUi.OfKind("dashCard"))
		{
			// px;不超过 12 的值当作"格"(.NET 原生卡片默认 2×2 格),1 格约 160×110px
			double dw = J.Num(c.Spec, "defaultW", 320.0);
			double dh = J.Num(c.Spec, "defaultH", 220.0);
			int w = (int)((dw <= 12.0) ? (dw * 160.0) : dw);
			int h = (int)((dh <= 12.0) ? (dh * 110.0) : dh);
			yield return (TypeOf(c), c.Label, Math.Max(160, w), Math.Max(120, h));
		}
	}

	public static string? Title(string type)
	{
		if (!IsPluginType(type))
		{
			return null;
		}

		UiContribution? c = Find(type);
		return (c != null) ? c.Label : I18n.L("插件卡片");
	}

	public static Control? Create(string type)
	{
		if (!IsPluginType(type))
		{
			return null;
		}

		Hook();
		if (!_shells.TryGetValue(type, out CardShell? shell))
		{
			shell = new CardShell(type);
			_shells[type] = shell;
		}

		shell.Sync();
		return shell;
	}

	private static void Hook()
	{
		if (_hooked)
		{
			return;
		}

		_hooked = true;
		PluginUi.Changed += delegate
		{
			foreach (CardShell s in _shells.Values)
			{
				s.Sync();
			}
		};
		// 【Windows 源】这里还挂了一个 WebViewHost.AvailabilityChanged → 全部 Reset。
		// 【mac 差异】没有内嵌浏览器(GOAL.md 34)⇒ 没有"可用性变化"这件事,如实不挂。
	}

	private sealed class CardShell : ContentControl
	{
		private readonly string _type;

		private UiContribution? _c;

		public CardShell(string type)
		{
			_type = type;
			Focusable = false;
			HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
			VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
		}

		public void Sync()
		{
			UiContribution? c = Find(_type);
			if (c == null)
			{
				if (_c != null || Content == null)
				{
					_c = null;
					Content = Ui.V(4.0, Ui.Muted(I18n.L("插件卡片不可用")), Ui.Small(I18n.L("提供它的插件被禁用或卸载了；可以在编辑布局里移除这张卡片。")).Wrap());
				}

				return;
			}

			// 【Windows 源】这里还会区分"原生 / WebView 且可用 / WebView 不可用(给「在浏览器中打开」)"三种;
			// 【mac 差异】不嵌 WebView ⇒ 交给 NativePluginUi.Content 统一给结果(原生工厂优先,否则如实内容卡 + 浏览器按钮)。
			// 贡献没变就重画一次也无妨(它只是一张卡,不是 WebView,没有"重建会闪"的问题)。
			_c = c;
			Content = NativePluginUi.Content(c, new Dictionary<string, object?> { ["card"] = c.Id });
		}

		public void Reset()
		{
			Content = null;
			_c = null;
			Sync();
		}
	}
}
