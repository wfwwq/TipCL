using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TipCL.Services;

/// <summary>
/// 一个插件网页视图：WebView2 可用时内嵌，不可用（Win7/8.1、没装运行时）时显示兜底面板，
/// 可以「在浏览器中打开」同一个地址。只允许停留在插件源上，其它链接交给系统浏览器。
/// 视图第一次显示时才创建浏览器；从界面上摘下来不会销毁（HwndHost 会被挂到停车窗口），
/// 所以切页面再回来状态还在。调用 <see cref="Dispose"/> 才真正关掉。
/// </summary>
public sealed class PluginWebView : Grid, IDisposable
{
	private string _url;

	private readonly string _plugin;

	private readonly bool _transparent;

	private readonly IDictionary<string, object?>? _props;

	private WebView2? _web;

	private bool _started;

	private bool _disposed;

	private bool _covered;

	private string _origin = "";

	private readonly Border _veil = new Border
	{
		Visibility = Visibility.Collapsed
	};

	/// <summary>页面里点了「浮层」等需要回报的事件时用（目前只用于日志/调试）。</summary>
	public event Action<string>? MessageReceived;

	public string Url => _url;

	public string Plugin => _plugin;

	public bool IsEmbedded => _web != null;

	public PluginWebView(string url, string plugin, IDictionary<string, object?>? props = null, bool transparent = false)
	{
		_url = url ?? "";
		_plugin = plugin ?? "";
		_props = props;
		_transparent = transparent;
		base.Focusable = false;
		base.ClipToBounds = true;
		if (transparent)
		{
			base.Background = Brushes.Transparent;
		}
		base.Loaded += delegate
		{
			if (!_started && !_disposed)
			{
				_started = true;
				PageStart();
			}
		};
		WebViewHost.Register(this);
	}

	private async void PageStart()
	{
		try
		{
			await StartAsync();
		}
		catch (Exception ex)
		{
			ShowFallback(I18n.L("页面没能加载：") + ex.Message);
		}
	}

	private string FullUrl()
	{
		return WebViewHost.WithParams(_url, _props);
	}

	private async Task StartAsync()
	{
		base.Children.Clear();
		if (string.IsNullOrEmpty(_url))
		{
			ShowFallback(I18n.L("插件没有提供页面地址"));
			return;
		}
		if (!WebViewHost.Available)
		{
			ShowFallback(WebViewHost.UnavailableReason);
			return;
		}
		ProgressBar bar = Ui.Prog();
		bar.IsIndeterminate = true;
		bar.Width = 160.0;
		bar.HorizontalAlignment = HorizontalAlignment.Center;
		bar.VerticalAlignment = VerticalAlignment.Center;
		if (!_transparent)
		{
			base.Children.Add(bar);
		}
		CoreWebView2Environment env = await WebViewHost.EnvironmentAsync();
		if (_disposed)
		{
			return;
		}
		WebView2 web = new WebView2();
		try
		{
			web.DefaultBackgroundColor = _transparent ? System.Drawing.Color.Transparent : PaperColor();
		}
		catch
		{
		}
		_web = web;
		base.Children.Add(web);
		await web.EnsureCoreWebView2Async(env);
		if (_disposed)
		{
			return;
		}
		base.Children.Remove(bar);
		CoreWebView2 core = web.CoreWebView2;
		bool dev = WebViewHost.DevToolsFor(_plugin);
		core.Settings.AreDevToolsEnabled = dev;
		core.Settings.AreDefaultContextMenusEnabled = dev;
		core.Settings.IsStatusBarEnabled = false;
		core.Settings.IsZoomControlEnabled = false;
		try
		{
			core.Settings.AreBrowserAcceleratorKeysEnabled = dev;
			core.Settings.IsGeneralAutofillEnabled = false;
			core.Settings.IsPasswordAutosaveEnabled = false;
		}
		catch
		{
		}
		_origin = WebViewHost.OriginOf(_url);
		core.NavigationStarting += OnNavigationStarting;
		core.NewWindowRequested += OnNewWindow;
		core.ProcessFailed += OnProcessFailed;
		core.WebMessageReceived += delegate(object? _, CoreWebView2WebMessageReceivedEventArgs e)
		{
			try
			{
				MessageReceived?.Invoke(e.TryGetWebMessageAsString() ?? "");
			}
			catch
			{
				try
				{
					MessageReceived?.Invoke(e.WebMessageAsJson ?? "");
				}
				catch
				{
				}
			}
		};
		core.Navigate(FullUrl());
		ApplyCover();
	}

	private static System.Drawing.Color PaperColor()
	{
		if (Application.Current?.TryFindResource("B.Paper") is SolidColorBrush b)
		{
			return System.Drawing.Color.FromArgb(255, b.Color.R, b.Color.G, b.Color.B);
		}
		return System.Drawing.Color.White;
	}

	private bool SameOrigin(string uri)
	{
		if (string.IsNullOrEmpty(uri) || uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		string o = WebViewHost.OriginOf(uri);
		return o.Length > 0 && string.Equals(o, _origin, StringComparison.OrdinalIgnoreCase);
	}

	private static void OpenExternal(string uri)
	{
		if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
		{
			Ui.OpenUrl(uri);
		}
	}

	private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
	{
		if (!SameOrigin(e.Uri))
		{
			e.Cancel = true;
			OpenExternal(e.Uri);
		}
	}

	private void OnNewWindow(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
	{
		e.Handled = true;
		string uri = e.Uri ?? "";
		if (!SameOrigin(uri))
		{
			OpenExternal(uri);
		}
		else if (uri.Length > 0)
		{
			// 同源新窗口：仍然交给系统浏览器（插件页面应当用 tipcl.nav.open 跳转）
			OpenExternal(uri);
		}
	}

	private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
	{
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			if (_disposed)
			{
				return;
			}
			Button reload = Ui.Btn(I18n.L("重新加载"), BtnKind.Primary, delegate
			{
				Reload();
			}, "\ue72c");
			SPanel panel = Ui.Empty("\ue7ba", I18n.L("插件页面崩溃了"), e.ProcessFailedKind.ToString());
			panel.Children.Add(reload.Center());
			Border cover = new Border
			{
				Child = panel
			};
			cover.SetResourceReference(Border.BackgroundProperty, "B.Paper");
			DisposeWeb();
			base.Children.Clear();
			base.Children.Add(cover);
		});
	}

	/// <summary>换地址（插件重载后 pageToken 会变）。</summary>
	public void SetUrl(string url)
	{
		if (url == _url)
		{
			return;
		}
		_url = url ?? "";
		_origin = WebViewHost.OriginOf(_url);
		if (_web?.CoreWebView2 != null)
		{
			_web.CoreWebView2.Navigate(FullUrl());
		}
		else if (_started)
		{
			Reload();
		}
	}

	public void Reload()
	{
		DisposeWeb();
		_started = true;
		PageStart();
	}

	/// <summary>主题切换时通知页面（契约 §7：window 上的 <c>tipcl:theme</c> 事件）。</summary>
	public void PushTheme()
	{
		try
		{
			if (_web?.CoreWebView2 == null)
			{
				return;
			}
			try
			{
				_web.DefaultBackgroundColor = _transparent ? System.Drawing.Color.Transparent : PaperColor();
			}
			catch
			{
			}
			string json = WebViewHost.ThemeJson();
			_ = _web.CoreWebView2.ExecuteScriptAsync("try{window.dispatchEvent(new CustomEvent('tipcl:theme',{detail:" + json + "}));}catch(e){}");
			_web.CoreWebView2.PostWebMessageAsJson("{\"type\":\"theme.changed\",\"theme\":" + json + "}");
		}
		catch
		{
		}
	}

	/// <summary>往页面发一条 JSON 消息（window.chrome.webview 的 message 事件）。</summary>
	public void Post(string json)
	{
		try
		{
			_web?.CoreWebView2?.PostWebMessageAsJson(json);
		}
		catch
		{
		}
	}

	internal void SetCovered(bool covered)
	{
		_covered = covered;
		ApplyCover();
	}

	private bool _suppressed;

	/// <summary>
	/// 临时藏起浏览器（独立 HWND 会盖住 WPF 内容）：比如首页「编辑布局」时要露出卡片的拖拽手柄。
	/// 和对话层遮挡分开记，两者任一为真都隐藏。
	/// </summary>
	public bool Suppressed
	{
		get
		{
			return _suppressed;
		}
		set
		{
			_suppressed = value;
			ApplyCover();
		}
	}

	private void ApplyCover()
	{
		if (_web != null)
		{
			_web.Visibility = (_covered || _suppressed) ? Visibility.Hidden : Visibility.Visible;
		}
	}

	private void ShowFallback(string reason)
	{
		DisposeWeb();
		base.Children.Clear();
		if (_transparent)
		{
			return;
		}
		string full = FullUrl();
		Button open = Ui.Btn(I18n.L("在浏览器中打开"), BtnKind.Primary, delegate
		{
			Ui.OpenUrl(full);
		}, "\ue8a7");
		open.IsEnabled = !string.IsNullOrEmpty(_url);
		Button recheck = Ui.Btn(I18n.L("重新检测"), BtnKind.Normal, delegate
		{
			if (WebViewHost.Recheck())
			{
				Reload();
			}
			else
			{
				AppServices.Toast(I18n.L("仍然不可用"), WebViewHost.UnavailableReason, ToastKind.Warning);
			}
		}, "\ue72c");
		SPanel buttons = Ui.H(8.0, open, recheck);
		if (WebViewHost.IsWin10OrLater && !WebViewHost.Available)
		{
			buttons.Children.Add(Ui.Btn(I18n.L("安装 WebView2 运行时"), BtnKind.Soft, delegate
			{
				Ui.OpenUrl(WebViewHost.RuntimeDownloadUrl);
			}, "\ue896"));
		}
		buttons.HorizontalAlignment = HorizontalAlignment.Center;
		SPanel panel = Ui.Empty("\ue946", I18n.L("这里没法内嵌插件页面"), reason + "\n" + I18n.L("功能不受影响：可以在系统浏览器里打开同一个页面。"));
		panel.Children.Add(buttons);
		base.Children.Add(Ui.Scroll(panel));
	}

	private void DisposeWeb()
	{
		WebView2 web = _web;
		_web = null;
		if (web == null)
		{
			return;
		}
		try
		{
			base.Children.Remove(web);
		}
		catch
		{
		}
		try
		{
			web.Dispose();
		}
		catch
		{
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		DisposeWeb();
		WebViewHost.Unregister(this);
	}
}
