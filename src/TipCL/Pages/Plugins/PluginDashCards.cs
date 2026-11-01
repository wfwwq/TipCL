using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TipCL.Services;

namespace TipCL.Pages.Plugins;

/// <summary>
/// 首页插件卡片（kind "dashCard"）：卡片类型 <c>plugin:&lt;plugin&gt;:&lt;id&gt;</c>，出现在「编辑布局 → 添加卡片」里。
/// DashHost 会按类型缓存卡片内容，所以这里给每个类型一个固定的外壳，插件重载 / 禁用时只换外壳里的内容。
/// 编辑布局时（DashHost 把卡片内容设为不可点）暂时藏起 WebView，否则它会盖住拖拽手柄。
/// </summary>
public static class PluginDashCards
{
	public const string Prefix = "plugin:";

	private static readonly Dictionary<string, CardShell> _shells = new Dictionary<string, CardShell>();

	private static bool _hooked;

	public static bool IsPluginType(string type)
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

	/// <summary>可以添加的插件卡片（类型, 标题, 默认宽高 px）。</summary>
	public static IEnumerable<(string Type, string Title, int W, int H)> Available()
	{
		foreach (UiContribution c in PluginUi.OfKind("dashCard"))
		{
			// px；不超过 12 的值当作“格”（.NET 原生卡片默认 2×2 格），1 格约 160×110px
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

	public static UIElement? Create(string type)
	{
		if (!IsPluginType(type))
		{
			return null;
		}
		Hook();
		if (!_shells.TryGetValue(type, out CardShell shell))
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
		WebViewHost.AvailabilityChanged += delegate
		{
			foreach (CardShell s2 in _shells.Values)
			{
				s2.Reset();
			}
		};
	}

	private sealed class CardShell : ContentControl
	{
		private readonly string _type;

		private UiContribution? _c;

		private ContentControl? _owner;

		public CardShell(string type)
		{
			_type = type;
			base.Focusable = false;
			base.HorizontalContentAlignment = HorizontalAlignment.Stretch;
			base.VerticalContentAlignment = VerticalAlignment.Stretch;
			base.Loaded += delegate
			{
				HookOwner();
			};
		}

		/// <summary>DashHost 的 CardView 在编辑模式下把内容容器设成不可点：跟着它藏 / 显 WebView。</summary>
		private void HookOwner()
		{
			DependencyObject d = VisualTreeHelper.GetParent(this);
			while (d != null && !(d is ContentControl))
			{
				d = VisualTreeHelper.GetParent(d);
			}
			ContentControl? owner = d as ContentControl;
			if (owner == _owner)
			{
				return;
			}
			if (_owner != null)
			{
				_owner.IsHitTestVisibleChanged -= OnOwnerHit;
			}
			_owner = owner;
			if (_owner != null)
			{
				_owner.IsHitTestVisibleChanged += OnOwnerHit;
			}
			ApplyEdit();
		}

		private void OnOwnerHit(object sender, DependencyPropertyChangedEventArgs e)
		{
			ApplyEdit();
		}

		private void ApplyEdit()
		{
			if (base.Content is PluginWebView web)
			{
				web.Suppressed = _owner != null && !_owner.IsHitTestVisible;
			}
		}

		public void Sync()
		{
			UiContribution? c = Find(_type);
			if (c == null)
			{
				if (_c != null || base.Content == null)
				{
					(base.Content as IDisposable)?.Dispose();
					_c = null;
					base.Content = Ui.V(4.0, Ui.Muted(I18n.L("插件卡片不可用")), Ui.Small(I18n.L("提供它的插件被禁用或卸载了；可以在编辑布局里移除这张卡片。")).Wrap());
				}
				return;
			}
			if (_c != null && base.Content is PluginWebView w && _c.Native == c.Native)
			{
				_c = c;
				w.SetUrl(c.Url);
				return;
			}
			(base.Content as IDisposable)?.Dispose();
			_c = c;
			if (!c.Native && !WebViewHost.Available)
			{
				Button open = Ui.Btn(I18n.L("在浏览器中打开"), BtnKind.Soft, delegate
				{
					Ui.OpenUrl(WebViewHost.WithParams(c.Url));
				}, "\ue8a7");
				base.Content = Ui.V(6.0, Ui.Small(I18n.L("当前系统不能在首页内嵌插件卡片")).Wrap(), open.Left());
				return;
			}
			base.Content = NativePluginUi.Content(c, new Dictionary<string, object?> { ["card"] = c.Id });
			ApplyEdit();
		}

		public void Reset()
		{
			(base.Content as IDisposable)?.Dispose();
			base.Content = null;
			_c = null;
			Sync();
		}
	}
}
