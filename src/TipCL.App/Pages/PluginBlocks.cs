using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using TipCL.Core.Launcher.Layout;
using TipCL.Pages.Plugins;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// R32-MWG:Windows 版 <c>Pages\Layout\Widgets\PluginBlocks.cs</c>(WPF / net48)到 mac(Avalonia 11 / net8.0)的移植。
/// 插件区块(§4.F 的 kind = "block",块 id = plugin:&lt;plugin&gt;:&lt;blockId&gt;)。
/// 渲染方式与首页插件卡片(dashCard)一致:原生工厂优先,否则如实内容卡 —— 见 NativePluginUi.Content。
/// 语义照抄 PluginDashCards:插件被禁用 / 卸载后**块保留**、只显示「不可用」,不删用户数据。
///
/// Windows → mac 差异(逐条,含降级 D3):
/// <list type="number">
/// <item><b>namespace</b> <c>TipCL.Pages.Layout</c> → <c>TipCL.Pages</c>(R32-D7 扁平);
///   UIElement → Control,宿主类 <c>Shell : ContentControl</c>(不再是 EditAwareShell);</item>
/// <item><b>【D3 / GOAL.md 34】没有 WebView ⇒ 不做"编辑态藏 WebView"那一套</b>:
///   Windows 的 Shell 继承 EditAwareShell,编辑态通过 <c>web.Suppressed = Editing</c> 藏 HwndHost;
///   mac 没有 PluginWebView / 没有 IsHitTestVisibleChanged 事件 ⇒ 该机制整套不搬
///   (先例:Pages/Plugins/PluginDashCards.cs 的 CardShell 就是普通 ContentControl);</item>
/// <item><b><c>WebViewHost.AvailabilityChanged</c> 订阅不搬</b>:没有内嵌浏览器就没有"可用性变化后要 Reset"这件事
///   (与 PluginDashCards.Hook 的处理一致,原位留注释);</item>
/// <item><b><c>(Content as IDisposable)?.Dispose()</c> 不搬</b>:mac 的内容是 Control,不是可释放的 WebView,
///   直接换内容即可;因此也没有 Windows 的 <c>disposeOnDetach</c> / <c>DisposeContent</c>;</item>
/// <item><b><c>PluginUi.Find("block", plugin, id)</c> / <c>NativePluginUi.Content(c, props)</c> 同名同签名</b>
///   (mac 的 UiContribution 在 TipCL.Pages.Plugins);props 键 <c>"block"</c> 逐字保留。</item>
/// </list>
/// </summary>
internal static class PluginBlocks
{
	// 每个块 id 一个常驻外壳:插件重载 / 禁用只换外壳里的内容
	private static readonly Dictionary<string, Shell> _shells = new Dictionary<string, Shell>(StringComparer.Ordinal);

	private static bool _hooked;

	/// <summary>造插件区块内容;id 不合法返回 null(交给上层显示通用占位)。</summary>
	public static Control? Build(string blockId, string pageId)
	{
		if (!LayoutEngine.TryParsePluginId(blockId, out string plugin, out string id))
		{
			return null;
		}
		Hook();
		if (!_shells.TryGetValue(blockId, out Shell? shell))
		{
			shell = new Shell(plugin, id);
			_shells[blockId] = shell;
		}
		shell.Sync();
		return shell;
	}

	/// <summary>插件界面贡献变化时,让所有外壳重新同步。</summary>
	private static void Hook()
	{
		if (_hooked)
		{
			return;
		}
		_hooked = true;
		PluginUi.Changed += delegate
		{
			foreach (Shell shell in _shells.Values)
			{
				shell.Sync();
			}
		};
		// 【Windows 源】这里还挂了一个 WebViewHost.AvailabilityChanged → 全部 Reset。
		// 【mac 差异】没有内嵌浏览器(GOAL.md 34)⇒ 没有"可用性变化"这件事,如实不挂。
	}

	private sealed class Shell : ContentControl
	{
		private readonly string _plugin;

		private readonly string _id;

		private UiContribution? _current;

		public Shell(string plugin, string id)
		{
			_plugin = plugin;
			_id = id;
			Focusable = false;
			HorizontalContentAlignment = HorizontalAlignment.Stretch;
			VerticalContentAlignment = VerticalAlignment.Stretch;
			base.Loaded += delegate
			{
				// 被摘下来销毁过(切页/重排):挂回可视树时重建
				if (Content == null)
				{
					Sync();
				}
			};
		}

		public void Sync()
		{
			UiContribution? contribution = PluginUi.Find("block", _plugin, _id);
			if (contribution == null)
			{
				// 插件被禁用 / 卸载:块保留,只换内容
				if (_current != null || Content == null)
				{
					_current = null;
					Content = Unavailable();
				}
				return;
			}
			_current = contribution;
			Dictionary<string, object?> props = new Dictionary<string, object?>
			{
				["block"] = contribution.Id
			};
			Content = NativePluginUi.Content(contribution, props);
		}

		public void Reset()
		{
			Content = null;
			_current = null;
			Sync();
		}

		private static Control Unavailable()
		{
			return Ui.V(4.0, Ui.Muted(I18n.L("插件区块不可用")), Ui.Small(I18n.L("提供它的插件被禁用或卸载了;可以在编辑布局里移除这个区块。")).Wrap());
		}
	}
}
