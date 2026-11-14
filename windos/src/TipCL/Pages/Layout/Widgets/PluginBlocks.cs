using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using TipCL.Pages.Plugins;
using TipCL.Services;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Pages.Layout;

// 插件区块(§4.F 的 kind = "block",块 id = plugin:<plugin>:<blockId>)。
// 渲染方式与首页插件卡片(dashCard)一致:原生工厂优先,否则内嵌 WebView(见 NativePluginUi.Content)。
// 语义照抄 PluginDashCards.cs:160-172:插件被禁用 / 卸载后**块保留**、只显示「不可用」,不删用户数据;
// 编辑态临时藏起 WebView(照抄 PluginDashCards.cs:122-158 的做法)。
internal static class PluginBlocks
{
	// 每个块 id 一个常驻外壳:插件重载 / 禁用只换外壳里的内容,编辑态拖拽期间不重建
	private static readonly Dictionary<string, Shell> _shells = new Dictionary<string, Shell>(StringComparer.Ordinal);

	private static bool _hooked;

	/// <summary>造插件区块内容;id 不合法返回 null(交给上层显示通用占位)。</summary>
	public static UIElement? Build(string blockId, string pageId)
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

	/// <summary>插件界面贡献或 WebView 可用性变化时,让所有外壳重新同步。</summary>
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
		WebViewHost.AvailabilityChanged += delegate
		{
			foreach (Shell shell2 in _shells.Values)
			{
				shell2.Reset();
			}
		};
	}

	private sealed class Shell : EditAwareShell
	{
		private readonly string _plugin;

		private readonly string _id;

		private UiContribution? _current;

		public Shell(string plugin, string id)
			: base(disposeOnDetach: true)
		{
			_plugin = plugin;
			_id = id;
			base.Loaded += delegate
			{
				// 被摘下来销毁过(切页/重排):挂回可视树时重建
				if (base.Content == null)
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
				if (_current != null || base.Content == null)
				{
					(base.Content as IDisposable)?.Dispose();
					_current = null;
					base.Content = Unavailable();
				}
				return;
			}
			if (_current != null && base.Content is PluginWebView existing && _current.Native == contribution.Native)
			{
				_current = contribution;
				existing.SetUrl(contribution.Url);
				return;
			}
			(base.Content as IDisposable)?.Dispose();
			_current = contribution;
			Dictionary<string, object?> props = new Dictionary<string, object?>
			{
				["block"] = contribution.Id
			};
			base.Content = NativePluginUi.Content(contribution, props);
			OnEditChanged();
		}

		public void Reset()
		{
			(base.Content as IDisposable)?.Dispose();
			base.Content = null;
			_current = null;
			Sync();
		}

		// WebView2(HwndHost)会盖住拖拽手柄与操作条,编辑态必须临时藏起来
		protected override void OnEditChanged()
		{
			if (base.Content is PluginWebView web)
			{
				web.Suppressed = Editing;
			}
		}

		// 被摘下来(切页 / 编辑态重排):销毁内容(WebView2 必须显式 Dispose),挂回来时重建
		protected override void DisposeContent()
		{
			_current = null;
			object? content = base.Content;
			base.Content = null;
			try
			{
				(content as IDisposable)?.Dispose();
			}
			catch (Exception)
			{
			}
		}

		private static UIElement Unavailable()
		{
			return Ui.V(4.0, Ui.Muted(I18n.L("插件区块不可用")), Ui.Small(I18n.L("提供它的插件被禁用或卸载了;可以在编辑布局里移除这个区块。")).Wrap());
		}
	}
}
