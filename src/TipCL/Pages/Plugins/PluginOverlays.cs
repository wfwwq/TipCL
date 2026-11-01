using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using TipCL.Services;

namespace TipCL.Pages.Plugins;

/// <summary>
/// 插件浮层（kind "overlay"）：贴在主窗口上的无边框子窗口（Owner = 主窗口），
/// 跟着主窗口移动 / 缩放 / 最小化。大小、锚点、边距、是否点击穿透来自 spec，运行时可以被 ui.overlayUpdate 改。
/// 透明背景：窗口不用 AllowsTransparency（WebView2 的 HWND 在分层窗口里画不出来），
/// 改为把 DWM 玻璃扩展到整个客户区 + WebView2 DefaultBackgroundColor 透明。Win10/11 上一般是透明的；
/// 个别显卡驱动 / 远程桌面下可能显示成黑底，插件应当允许用户关掉浮层。
/// 点击穿透：WS_EX_LAYERED | WS_EX_TRANSPARENT（整个浮层都不接收鼠标）。
/// Win7/8.1 或没装 WebView2 运行时：不显示浮层（契约 §11）。
/// </summary>
public static class PluginOverlays
{
	private sealed class State
	{
		public UiContribution C = null!;

		public double Width = 240.0;

		public double Height = 320.0;

		public string Anchor = "bottom-right";

		public double MarginX = 24.0;

		public double MarginY = 24.0;

		public bool ClickThrough;

		public bool Visible = true;

		public OverlayWindow? Win;
	}

	private static readonly Dictionary<string, State> _states = new Dictionary<string, State>();

	private static MainWindow? _main;

	public static void Attach(MainWindow main)
	{
		if (_main != null)
		{
			return;
		}
		_main = main;
		PluginUi.Changed += Sync;
		main.LocationChanged += delegate
		{
			RepositionAll();
		};
		main.SizeChanged += delegate
		{
			RepositionAll();
		};
		main.StateChanged += delegate
		{
			RepositionAll();
		};
		main.Closed += delegate
		{
			CloseAll();
		};
		// 启动游戏后「隐藏启动器」只是 Hide 主窗口，被拥有的窗口不会跟着藏：自己处理
		main.IsVisibleChanged += delegate
		{
			if (main.IsVisible)
			{
				RepositionAll();
				return;
			}
			foreach (State st in _states.Values)
			{
				if (st.Win != null && !st.Win.IsClosed)
				{
					st.Win.Hide();
				}
			}
		};
		WebViewHost.AvailabilityChanged += Sync;
	}

	private static void Sync()
	{
		if (_main == null)
		{
			return;
		}
		List<UiContribution> list = PluginUi.OfKind("overlay").ToList();
		HashSet<string> keys = new HashSet<string>(list.Select((UiContribution c) => c.Key));
		foreach (string stale in _states.Keys.Where((string k) => !keys.Contains(k)).ToList())
		{
			_states[stale].Win?.Shutdown();
			_states.Remove(stale);
		}
		if (!WebViewHost.Available)
		{
			return;
		}
		foreach (UiContribution c in list)
		{
			if (!_states.TryGetValue(c.Key, out State st))
			{
				st = new State
				{
					C = c
				};
				ApplySpec(st, c.Spec);
				_states[c.Key] = st;
			}
			else
			{
				bool urlChanged = st.C.Url != c.Url;
				st.C = c;
				if (urlChanged)
				{
					st.Win?.View.SetUrl(c.Url);
				}
			}
			Apply(st);
		}
	}

	private static void ApplySpec(State st, JsonElement s)
	{
		if (s.ValueKind != JsonValueKind.Object)
		{
			return;
		}
		if (J.TryGet(s, "width", out JsonElement w) && w.ValueKind == JsonValueKind.Number)
		{
			st.Width = Clamp.Of(w.GetDouble(), 24.0, 2000.0);
		}
		if (J.TryGet(s, "height", out JsonElement h) && h.ValueKind == JsonValueKind.Number)
		{
			st.Height = Clamp.Of(h.GetDouble(), 24.0, 2000.0);
		}
		string anchor = J.Str(s, "anchor");
		if (anchor.Length > 0)
		{
			st.Anchor = anchor;
		}
		if (J.TryGet(s, "margin", out JsonElement m))
		{
			if (m.ValueKind == JsonValueKind.Number)
			{
				st.MarginX = (st.MarginY = m.GetDouble());
			}
			else if (m.ValueKind == JsonValueKind.Object)
			{
				st.MarginX = J.Num(m, "x", st.MarginX);
				st.MarginY = J.Num(m, "y", st.MarginY);
			}
			else if (m.ValueKind == JsonValueKind.Array && m.GetArrayLength() >= 2)
			{
				st.MarginX = m[0].GetDouble();
				st.MarginY = m[1].GetDouble();
			}
		}
		if (J.TryGet(s, "clickThrough", out JsonElement ct) && (ct.ValueKind == JsonValueKind.True || ct.ValueKind == JsonValueKind.False))
		{
			st.ClickThrough = ct.ValueKind == JsonValueKind.True;
		}
		if (J.TryGet(s, "visible", out JsonElement v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False))
		{
			st.Visible = v.ValueKind == JsonValueKind.True;
		}
	}

	/// <summary>ui.overlayUpdate（plugins.ui_request kind "overlay"）。</summary>
	public static void Update(string plugin, JsonElement payload)
	{
		string id = PluginEvents.FStr(payload, "id");
		State? st = _states.Values.FirstOrDefault((State s) => s.C.Plugin == plugin && (id.Length == 0 || s.C.Id == id));
		if (st == null)
		{
			return;
		}
		ApplySpec(st, payload);
		JsonElement inner = J.Obj(payload, "params");
		if (inner.ValueKind != JsonValueKind.Object)
		{
			inner = J.Obj(payload, "data");
		}
		if (inner.ValueKind == JsonValueKind.Object)
		{
			ApplySpec(st, inner);
		}
		Apply(st);
	}

	private static void Apply(State st)
	{
		if (_main == null)
		{
			return;
		}
		if (!st.Visible || !WebViewHost.Available)
		{
			if (st.Win != null && !st.Win.IsClosed)
			{
				st.Win.Hide();
			}
			return;
		}
		if (st.Win != null && st.Win.IsClosed)
		{
			st.Win.Shutdown();
			st.Win = null;
		}
		if (st.Win == null)
		{
			st.Win = new OverlayWindow(st.C);
			try
			{
				if (_main.IsLoaded)
				{
					st.Win.Owner = _main;
				}
			}
			catch
			{
			}
		}
		st.Win.Width = st.Width;
		st.Win.Height = st.Height;
		st.Win.SetClickThrough(st.ClickThrough);
		Position(st);
		if (_main.WindowState != WindowState.Minimized && _main.IsVisible)
		{
			if (!st.Win.IsVisible)
			{
				st.Win.Show();
			}
		}
	}

	private static void RepositionAll()
	{
		foreach (State st in _states.Values)
		{
			if (st.Win != null && st.Visible)
			{
				if (_main != null && _main.WindowState == WindowState.Minimized)
				{
					continue;
				}
				if (st.Win.IsClosed)
				{
					continue;
				}
				Position(st);
				if (!st.Win.IsVisible && _main != null && _main.IsVisible)
				{
					st.Win.Show();
				}
			}
		}
	}

	private static void Position(State st)
	{
		MainWindow? main = _main;
		OverlayWindow? win = st.Win;
		if (main == null || win == null)
		{
			return;
		}
		PresentationSource? src = PresentationSource.FromVisual(main);
		if (src?.CompositionTarget == null)
		{
			return;
		}
		Point tl;
		try
		{
			tl = src.CompositionTarget.TransformFromDevice.Transform(main.PointToScreen(new Point(0.0, 0.0)));
		}
		catch
		{
			return;
		}
		double w = main.ActualWidth;
		double h = main.ActualHeight;
		string a = st.Anchor.ToLowerInvariant();
		double x = a.Contains("left") ? (tl.X + st.MarginX) : (a.Contains("right") ? (tl.X + w - st.Width - st.MarginX) : (tl.X + (w - st.Width) / 2.0));
		double y = a.Contains("top") ? (tl.Y + st.MarginY) : (a.Contains("bottom") ? (tl.Y + h - st.Height - st.MarginY) : (tl.Y + (h - st.Height) / 2.0));
		win.Left = Math.Round(x);
		win.Top = Math.Round(y);
	}

	private static void CloseAll()
	{
		foreach (State st in _states.Values)
		{
			st.Win?.Shutdown();
			st.Win = null;
		}
		_states.Clear();
	}
}

/// <summary>浮层窗口本身。</summary>
internal sealed class OverlayWindow : Window
{
	private struct MARGINS
	{
		public int Left;

		public int Right;

		public int Top;

		public int Bottom;
	}

	private const int GWL_EXSTYLE = -20;

	private const int WS_EX_TRANSPARENT = 32;

	private const int WS_EX_TOOLWINDOW = 128;

	private const int WS_EX_LAYERED = 524288;

	private const int WS_EX_NOACTIVATE = 134217728;

	private const int LWA_ALPHA = 2;

	private bool _clickThrough;

	private bool _closing;

	public PluginWebView View { get; }

	public UiContribution C { get; }

	[DllImport("user32.dll", EntryPoint = "GetWindowLong")]
	private static extern int GetWindowLong32(IntPtr hwnd, int index);

	[DllImport("user32.dll", EntryPoint = "SetWindowLong")]
	private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

	[DllImport("user32.dll")]
	private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

	[DllImport("dwmapi.dll")]
	private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

	public OverlayWindow(UiContribution c)
	{
		C = c;
		base.WindowStyle = WindowStyle.None;
		base.ResizeMode = ResizeMode.NoResize;
		base.ShowInTaskbar = false;
		base.ShowActivated = false;
		base.Topmost = false;
		base.Background = Brushes.Transparent;
		base.Title = c.Label;
		WindowChrome.SetWindowChrome(this, new WindowChrome
		{
			CaptionHeight = 0.0,
			ResizeBorderThickness = new Thickness(0.0),
			GlassFrameThickness = new Thickness(-1.0),
			CornerRadius = new CornerRadius(0.0),
			UseAeroCaptionButtons = false
		});
		View = new PluginWebView(c.Url, c.Plugin, new Dictionary<string, object?>
		{
			["overlay"] = c.Id
		}, transparent: true);
		base.Content = View;
		base.SourceInitialized += delegate
		{
			IntPtr hwnd = new WindowInteropHelper(this).Handle;
			try
			{
				HwndSource src = HwndSource.FromHwnd(hwnd);
				if (src?.CompositionTarget != null)
				{
					src.CompositionTarget.BackgroundColor = Colors.Transparent;
				}
				MARGINS margins = new MARGINS
				{
					Left = -1,
					Right = -1,
					Top = -1,
					Bottom = -1
				};
				DwmExtendFrameIntoClientArea(hwnd, ref margins);
			}
			catch
			{
			}
			ApplyExStyle();
		};
		base.Closed += delegate
		{
			_closing = true;
		};
	}

	public void SetClickThrough(bool on)
	{
		if (_clickThrough != on)
		{
			_clickThrough = on;
			ApplyExStyle();
		}
	}

	private void ApplyExStyle()
	{
		IntPtr hwnd = new WindowInteropHelper(this).Handle;
		if (hwnd == IntPtr.Zero)
		{
			return;
		}
		try
		{
			int ex = GetWindowLong32(hwnd, GWL_EXSTYLE);
			ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
			ex = (_clickThrough ? (ex | WS_EX_TRANSPARENT | WS_EX_LAYERED) : (ex & ~WS_EX_TRANSPARENT & ~WS_EX_LAYERED));
			SetWindowLong32(hwnd, GWL_EXSTYLE, ex);
			if (_clickThrough)
			{
				SetLayeredWindowAttributes(hwnd, 0u, byte.MaxValue, LWA_ALPHA);
			}
		}
		catch
		{
		}
	}

	public bool IsClosed => _closing;

	public void Shutdown()
	{
		try
		{
			View.Dispose();
		}
		catch
		{
		}
		if (_closing)
		{
			return;
		}
		_closing = true;
		try
		{
			Close();
		}
		catch
		{
		}
	}
}
