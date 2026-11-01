using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace TipCL.Services;

/// <summary>
/// 插件网页视图的公共部分：共享 CoreWebView2Environment、运行时检测、主题桥接、对话层遮挡处理。
/// 用户数据目录：%TIPCL_HOME%/runtime/webview2/profile（所有插件页面共用一个浏览器进程和同一个源）。
/// </summary>
public static partial class WebViewHost
{
	/// <summary>Evergreen 运行时引导安装器（Win10+）。</summary>
	public const string RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

	/// <summary>Chromium 参数：允许无手势自动播放（大肥鱼要能直接开口说话）。</summary>
	public const string BrowserArguments = "--autoplay-policy=no-user-gesture-required";

	private static bool? _available;

	private static string _reason = "";

	private static string _version = "";

	private static Task<CoreWebView2Environment>? _env;

	private static bool _hooked;

	private static readonly List<WeakReference<PluginWebView>> _live = new List<WeakReference<PluginWebView>>();

	public static event Action? AvailabilityChanged;

	public static string RuntimeDir
	{
		get
		{
			string root;
			try
			{
				root = BridgeHost.FindRoot();
			}
			catch
			{
				root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TipCL");
			}
			return Path.Combine(root, "runtime", "webview2");
		}
	}

	public static string UserDataFolder => Path.Combine(RuntimeDir, "profile");

	/// <summary>Win10 及以上（app.manifest 声明了 Win10 兼容，OSVersion 是真实版本）。</summary>
	public static bool IsWin10OrLater => Environment.OSVersion.Version.Major >= 10;

	public static string RuntimeVersion => _version;

	/// <summary>不可用时的原因（给用户看）。</summary>
	public static string UnavailableReason => _reason;

	/// <summary>检测一次 WebView2 运行时（结果缓存；Recheck 可以重测）。</summary>
	public static bool Available => _available ?? Probe();

	public static bool Probe()
	{
		try
		{
			EnsureLoader();
			string v = CoreWebView2Environment.GetAvailableBrowserVersionString(null);
			_version = v ?? "";
			_available = !string.IsNullOrEmpty(v);
			_reason = (_available == true) ? "" : I18n.L("没有检测到 WebView2 运行时");
		}
		catch (WebView2RuntimeNotFoundException)
		{
			_available = false;
			_reason = IsWin10OrLater ? I18n.L("没有安装 Microsoft Edge WebView2 运行时") : I18n.L("当前系统（Win7 / 8.1）不支持 WebView2，插件页面只能在浏览器里打开");
		}
		catch (Exception ex)
		{
			_available = false;
			_reason = I18n.L("WebView2 加载失败：") + ex.Message + ((_loaderError != null) ? (" · " + _loaderError) : "");
		}
		return _available == true;
	}

	/// <summary>用户装完运行时后点「重新检测」。</summary>
	public static bool Recheck()
	{
		bool before = _available == true;
		_env = null;
		bool now = Probe();
		if (now != before)
		{
			try
			{
				AvailabilityChanged?.Invoke();
			}
			catch
			{
			}
		}
		return now;
	}

	/// <summary>共享环境：同一用户数据目录、同一组浏览器参数。</summary>
	public static Task<CoreWebView2Environment> EnvironmentAsync()
	{
		if (_env != null)
		{
			return _env;
		}
		EnsureLoader();
		Directory.CreateDirectory(UserDataFolder);
		CoreWebView2EnvironmentOptions options = new CoreWebView2EnvironmentOptions(BrowserArguments)
		{
			Language = (I18n.Current == "en") ? "en-US" : "zh-CN"
		};
		Task<CoreWebView2Environment> t = CoreWebView2Environment.CreateAsync(null, UserDataFolder, options);
		_env = t;
		t.ContinueWith(delegate(Task<CoreWebView2Environment> r)
		{
			if (r.IsFaulted && _env == r)
			{
				_env = null;
			}
		}, TaskScheduler.Default);
		return t;
	}

	/// <summary>插件开发调试：开发者链接的插件或 TIPCL_WPF_DEBUG=1 时开 DevTools。</summary>
	public static bool DevToolsFor(string? plugin)
	{
		if (Environment.GetEnvironmentVariable("TIPCL_WPF_DEBUG") == "1")
		{
			return true;
		}
		return plugin != null && DevPlugins.Contains(plugin);
	}

	/// <summary>由插件页刷新列表时填：source == "dev" 的插件。</summary>
	public static readonly HashSet<string> DevPlugins = new HashSet<string>(StringComparer.Ordinal);

	// ---- 主题桥接（契约 §7）-------------------------------------------------

	private static string Hex(string key, string fallback)
	{
		try
		{
			if (Application.Current?.TryFindResource(key) is SolidColorBrush b)
			{
				Color c = b.Color;
				return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
			}
		}
		catch
		{
		}
		return fallback;
	}

	public static Dictionary<string, object> ThemePayload()
	{
		bool dark = App.IsDark;
		return new Dictionary<string, object>
		{
			["mode"] = dark ? "dark" : "light",
			["colors"] = new Dictionary<string, object>
			{
				["bg"] = Hex("B.Paper", dark ? "#1E1E1E" : "#FFFFFF"),
				["fg"] = Hex("B.Ink", dark ? "#EDEDED" : "#262626"),
				["card"] = Hex("B.Paper2", dark ? "#252525" : "#FAFAFA"),
				["border"] = Hex("B.Line", dark ? "#3A3A3A" : "#E6E6E6"),
				["accent"] = Hex("B.Accent", "#2E9B6B"),
				["muted"] = Hex("B.InkMuted", "#888888"),
				["danger"] = Hex("B.Danger", "#D64545")
			},
			["font"] = "\"Microsoft YaHei UI\", \"Segoe UI\", system-ui, sans-serif"
		};
	}

	private static readonly JsonSerializerOptions JsonOpt = new JsonSerializerOptions
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	public static string ThemeJson()
	{
		return JsonSerializer.Serialize(ThemePayload(), JsonOpt);
	}

	public static string ToJson(object o)
	{
		return JsonSerializer.Serialize(o, JsonOpt);
	}

	/// <summary>
	/// 给插件视图地址补上主题和 props：<c>theme=&lt;json&gt;</c>；<paramref name="props"/> 合并进已有的 <c>props</c> JSON。
	/// </summary>
	public static string WithParams(string url, IDictionary<string, object?>? props = null)
	{
		if (string.IsNullOrEmpty(url))
		{
			return url;
		}
		string frag = "";
		int hash = url.IndexOf('#');
		if (hash >= 0)
		{
			frag = url.Substring(hash);
			url = url.Substring(0, hash);
		}
		int q = url.IndexOf('?');
		string path = (q < 0) ? url : url.Substring(0, q);
		List<KeyValuePair<string, string>> pairs = new List<KeyValuePair<string, string>>();
		if (q >= 0)
		{
			foreach (string part in url.Substring(q + 1).Split(new char[1] { '&' }, StringSplitOptions.RemoveEmptyEntries))
			{
				int eq = part.IndexOf('=');
				string k = Uri.UnescapeDataString((eq < 0) ? part : part.Substring(0, eq));
				string v = (eq < 0) ? "" : Uri.UnescapeDataString(part.Substring(eq + 1).Replace('+', ' '));
				pairs.Add(new KeyValuePair<string, string>(k, v));
			}
		}
		if (props != null && props.Count > 0)
		{
			Dictionary<string, object?> merged = new Dictionary<string, object>(StringComparer.Ordinal);
			int idx = pairs.FindIndex((KeyValuePair<string, string> p) => p.Key == "props");
			if (idx >= 0)
			{
				try
				{
					using JsonDocument doc = JsonDocument.Parse(pairs[idx].Value);
					if (doc.RootElement.ValueKind == JsonValueKind.Object)
					{
						foreach (JsonProperty p2 in doc.RootElement.EnumerateObject())
						{
							merged[p2.Name] = p2.Value.Clone();
						}
					}
				}
				catch
				{
				}
				pairs.RemoveAt(idx);
			}
			foreach (KeyValuePair<string, object?> kv in props)
			{
				merged[kv.Key] = kv.Value;
			}
			pairs.Add(new KeyValuePair<string, string>("props", JsonSerializer.Serialize(merged, JsonOpt)));
		}
		pairs.RemoveAll((KeyValuePair<string, string> p) => p.Key == "theme");
		pairs.Add(new KeyValuePair<string, string>("theme", ThemeJson()));
		StringBuilder sb = new StringBuilder(path);
		for (int i = 0; i < pairs.Count; i++)
		{
			sb.Append((i == 0) ? '?' : '&');
			sb.Append(Uri.EscapeDataString(pairs[i].Key));
			sb.Append('=');
			sb.Append(Uri.EscapeDataString(pairs[i].Value));
		}
		sb.Append(frag);
		return sb.ToString();
	}

	/// <summary>插件视图所在的源（scheme://host:port），导航到别处一律改用系统浏览器打开。</summary>
	public static string OriginOf(string url)
	{
		try
		{
			Uri u = new Uri(url);
			return u.GetLeftPart(UriPartial.Authority);
		}
		catch
		{
			return "";
		}
	}

	// ---- 存活视图登记：主题推送、对话层遮挡 -----------------------------------

	internal static void Register(PluginWebView v)
	{
		Hook();
		lock (_live)
		{
			_live.RemoveAll((WeakReference<PluginWebView> w) => !w.TryGetTarget(out PluginWebView _));
			_live.Add(new WeakReference<PluginWebView>(v));
		}
	}

	internal static void Unregister(PluginWebView v)
	{
		lock (_live)
		{
			_live.RemoveAll((WeakReference<PluginWebView> w) => !w.TryGetTarget(out PluginWebView t) || t == v);
		}
	}

	public static IReadOnlyList<PluginWebView> LiveViews()
	{
		lock (_live)
		{
			List<PluginWebView> list = new List<PluginWebView>();
			foreach (WeakReference<PluginWebView> w in _live)
			{
				if (w.TryGetTarget(out PluginWebView t))
				{
					list.Add(t);
				}
			}
			return list;
		}
	}

	private static void Hook()
	{
		if (_hooked)
		{
			return;
		}
		_hooked = true;
		App.ThemeVisualsChanged += PushThemeAll;
		// WebView2 是独立 HWND（airspace）：WPF 的对话层画不到它上面。对话打开时先把视图藏起来，关掉再显示。
		if (Dlg.Host != null)
		{
			Dlg.Host.IsVisibleChanged += delegate
			{
				SyncDialogCover();
			};
		}
	}

	/// <summary>对话层开着时隐藏所有不在对话里的网页视图。</summary>
	public static void SyncDialogCover()
	{
		bool cover = Dlg.Host != null && Dlg.Host.IsVisible;
		foreach (PluginWebView v in LiveViews())
		{
			bool inside = Dlg.Host != null && v.IsDescendantOf(Dlg.Host);
			v.SetCovered(cover && !inside);
		}
	}

	public static void PushThemeAll()
	{
		foreach (PluginWebView v in LiveViews())
		{
			v.PushTheme();
		}
	}
}
