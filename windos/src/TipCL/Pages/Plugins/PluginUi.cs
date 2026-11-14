using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TipCL.Services;

namespace TipCL.Pages.Plugins;

/// <summary>
/// 插件界面贡献的本地快照（plugins_ui），以及已安装插件列表（plugins_list）。
/// 收到 plugins.ui_changed / plugins.changed 时刷新，然后发 <see cref="Changed"/>，侧栏、AI 页、主页卡片、浮层各自重建。
/// </summary>
public static class PluginUi
{
	private static List<UiContribution> _all = new List<UiContribution>();

	private static List<PluginInfo> _plugins = new List<PluginInfo>();

	private static Task? _inflight;

	private static bool _again;

	public static IReadOnlyList<UiContribution> All => _all;

	public static IReadOnlyList<PluginInfo> Plugins => _plugins;

	public static bool Loaded { get; private set; }

	/// <summary>界面贡献变了（在 UI 线程上触发）。</summary>
	public static event Action? Changed;

	/// <summary>已安装插件列表变了（在 UI 线程上触发）。</summary>
	public static event Action? PluginsChanged;

	public static IEnumerable<UiContribution> OfKind(string kind)
	{
		return from c in _all
			where c.Kind == kind
			orderby c.Order, c.Label
			select c;
	}

	public static UiContribution? Find(string kind, string plugin, string id)
	{
		return _all.FirstOrDefault((UiContribution c) => c.Kind == kind && c.Plugin == plugin && c.Id == id);
	}

	public static UiContribution? FindPage(string pageId)
	{
		return _all.FirstOrDefault((UiContribution c) => c.Kind == "page" && c.PageId == pageId);
	}

	public static PluginInfo? Plugin(string id)
	{
		return _plugins.FirstOrDefault((PluginInfo p) => p.Id == id);
	}

	public static string PluginName(string id)
	{
		return Plugin(id)?.DisplayName ?? id;
	}

	/// <summary>刷新界面贡献（合并并发请求）。</summary>
	public static Task RefreshAsync()
	{
		if (_inflight != null)
		{
			_again = true;
			return _inflight;
		}
		return _inflight = RunAsync();
	}

	private static async Task RunAsync()
	{
		try
		{
			do
			{
				_again = false;
				await LoadOnceAsync();
			}
			while (_again);
		}
		finally
		{
			_inflight = null;
		}
	}

	private static async Task LoadOnceAsync()
	{
		if (!AppServices.Ready)
		{
			return;
		}
		JsonElement ui = await AppServices.Client.TryCallAsync<JsonElement>("plugins_ui");
		JsonElement list = await AppServices.Client.TryCallAsync<JsonElement>("plugins_list");
		List<UiContribution> next = J.Items(ui).Select(UiContribution.From).Where((UiContribution c) => c.Plugin.Length > 0 && c.Kind.Length > 0).ToList();
		List<PluginInfo> plugins = J.Items(list).Select(PluginInfo.From).Where((PluginInfo p) => p.Id.Length > 0).ToList();
		bool uiChanged = !Same(next, _all);
		bool listChanged = !SamePlugins(plugins, _plugins);
		_all = next;
		_plugins = plugins;
		Loaded = true;
		WebViewHost.DevPlugins.Clear();
		foreach (PluginInfo p in plugins.Where((PluginInfo p) => p.Source == "dev"))
		{
			WebViewHost.DevPlugins.Add(p.Id);
		}
		if (listChanged)
		{
			try
			{
				PluginsChanged?.Invoke();
			}
			catch (Exception ex)
			{
				Smoke.Note("plugins", ex.ToString());
			}
		}
		if (uiChanged)
		{
			try
			{
				Changed?.Invoke();
			}
			catch (Exception ex2)
			{
				Smoke.Note("plugins", ex2.ToString());
			}
		}
	}

	private static bool Same(List<UiContribution> a, List<UiContribution> b)
	{
		if (a.Count != b.Count)
		{
			return false;
		}
		for (int i = 0; i < a.Count; i++)
		{
			if (a[i].Key != b[i].Key || a[i].Url != b[i].Url || a[i].Label != b[i].Label || a[i].Icon != b[i].Icon || a[i].Order != b[i].Order || Raw(a[i].Spec) != Raw(b[i].Spec))
			{
				return false;
			}
		}
		return true;
	}

	private static bool SamePlugins(List<PluginInfo> a, List<PluginInfo> b)
	{
		if (a.Count != b.Count)
		{
			return false;
		}
		for (int i = 0; i < a.Count; i++)
		{
			if (a[i].Id != b[i].Id || a[i].Version != b[i].Version || a[i].Enabled != b[i].Enabled || a[i].State != b[i].State || a[i].Name != b[i].Name)
			{
				return false;
			}
		}
		return true;
	}

	private static string Raw(JsonElement e)
	{
		return (e.ValueKind == JsonValueKind.Undefined) ? "" : e.GetRawText();
	}
}

/// <summary>
/// .NET 插件的原生 WPF 界面。内核在 <c>TipCL.Plugins.PluginUiRegistry</c> 里登记（契约 §3.2）；
/// 这里用反射去找，界面层不对内核类型做编译期依赖，两边可以各自独立改、各自编译。
/// 主路径：<c>PluginUiRegistry.Find(plugin, kind, id)</c> → <c>NativeUiEntry.Create()</c> / <c>CreateAiView(chatId)</c>；
/// 兼容路径：静态 <c>CreatePage / CreateDashCard / CreateAiView</c>。找不到就当没有原生界面。
/// </summary>
public static class NativePluginUi
{
	private static Type? _type;

	private static bool _looked;

	private static Type? RegistryType
	{
		get
		{
			if (!_looked)
			{
				_looked = true;
				try
				{
					_type = typeof(App).Assembly.GetType("TipCL.Plugins.PluginUiRegistry", throwOnError: false);
				}
				catch
				{
					_type = null;
				}
			}
			return _type;
		}
	}

	private static FrameworkElement? Error(Exception ex)
	{
		return Ui.Empty("\ue7ba", I18n.L("插件界面出错了"), ex.Message);
	}

	private static FrameworkElement? Create(string plugin, string kind, string id, string? chatId)
	{
		Type? t = RegistryType;
		if (t == null)
		{
			return null;
		}
		try
		{
			MethodInfo? find = t.GetMethod("Find", BindingFlags.Static | BindingFlags.Public, null, new Type[3]
			{
				typeof(string),
				typeof(string),
				typeof(string)
			}, null);
			if (find != null)
			{
				object? entry = find.Invoke(null, new object[3] { plugin, kind, id });
				if (entry == null)
				{
					return null;
				}
				if (kind == "aiView")
				{
					MethodInfo? m = entry.GetType().GetMethod("CreateAiView", new Type[1] { typeof(string) });
					return m?.Invoke(entry, new object[1] { chatId ?? "" }) as FrameworkElement;
				}
				MethodInfo? m2 = entry.GetType().GetMethod("Create", Type.EmptyTypes);
				return m2?.Invoke(entry, null) as FrameworkElement;
			}
			string name = kind switch
			{
				"page" => "CreatePage",
				"dashCard" => "CreateDashCard",
				_ => "CreateAiView",
			};
			object[] args = (kind == "aiView") ? new object[3] { plugin, id, chatId ?? "" } : new object[2] { plugin, id };
			MethodInfo? legacy = t.GetMethod(name, BindingFlags.Static | BindingFlags.Public, null, args.Select((object a) => typeof(string)).ToArray(), null);
			return legacy?.Invoke(null, args) as FrameworkElement;
		}
		catch (TargetInvocationException ex)
		{
			return Error(ex.InnerException ?? ex);
		}
		catch (Exception ex2)
		{
			return Error(ex2);
		}
	}

	public static FrameworkElement? CreatePage(string plugin, string id)
	{
		return Create(plugin, "page", id, null);
	}

	public static FrameworkElement? CreateDashCard(string plugin, string id)
	{
		return Create(plugin, "dashCard", id, null);
	}

	public static FrameworkElement? CreateAiView(string plugin, string id, string chatId)
	{
		return Create(plugin, "aiView", id, chatId);
	}

	/// <summary>按贡献生成内容：原生工厂优先，否则 WebView。</summary>
	public static FrameworkElement Content(UiContribution c, IDictionary<string, object?>? props = null, string? chatId = null)
	{
		if (c.Native)
		{
			FrameworkElement? el = c.Kind switch
			{
				"page" => CreatePage(c.Plugin, c.Id),
				"dashCard" => CreateDashCard(c.Plugin, c.Id),
				"aiView" => CreateAiView(c.Plugin, c.Id, chatId ?? ""),
				_ => null,
			};
			if (el != null)
			{
				return el;
			}
			if (c.Url.Length == 0)
			{
				return Ui.Empty("\uea86", I18n.L("插件没有提供这个界面"), I18n.L("可能需要重启 TipCL 才能生效"));
			}
		}
		if (c.Url.Length == 0)
		{
			// 网页视图但还没有地址：Node 宿主还没起来（或正在重启）。宿主就绪后 plugins.ui_changed 会带来地址
			return Ui.Empty("\uea86", I18n.L("插件页面还没准备好"), I18n.L("插件运行环境正在启动，稍等一下会自动出现。"));
		}
		return new PluginWebView(c.Url, c.Plugin, props);
	}
}

/// <summary>插件图标：Fluent 字形、data URL、http 图片都认。</summary>
public static class PluginIcon
{
	public const string DefaultGlyph = "\uea86";

	public static FrameworkElement Make(string? icon, double size, string fg = "B.InkSoft")
	{
		icon = (icon ?? "").Trim();
		if (icon.Length == 0)
		{
			return Ui.Glyph(DefaultGlyph, size * 0.8, fg);
		}
		if (icon.Length <= 2)
		{
			return Ui.Glyph(icon, size * 0.8, fg);
		}
		Image img = new Image
		{
			Width = size,
			Height = size,
			Stretch = Stretch.Uniform,
			VerticalAlignment = VerticalAlignment.Center
		};
		RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
		if (icon.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
		{
			BitmapSource? bmp = DecodeDataUrl(icon, (int)size);
			if (bmp == null)
			{
				return Ui.Glyph(DefaultGlyph, size * 0.8, fg);
			}
			img.Source = bmp;
			return img;
		}
		if (icon.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || icon.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			Grid g = new Grid
			{
				Width = size,
				Height = size
			};
			TextBlock ph = Ui.Glyph(DefaultGlyph, size * 0.8, fg);
			ph.HorizontalAlignment = HorizontalAlignment.Center;
			g.Children.Add(ph);
			g.Children.Add(img);
			Load(img, ph, icon, (int)size);
			return g;
		}
		return Ui.Glyph(DefaultGlyph, size * 0.8, fg);
	}

	private static async void Load(Image img, UIElement placeholder, string url, int px)
	{
		try
		{
			BitmapSource? bmp = Thumbs.Cached(url, px) ?? (await Thumbs.LoadAsync(url, px));
			if (bmp != null)
			{
				img.Source = bmp;
				placeholder.Visibility = Visibility.Collapsed;
			}
		}
		catch
		{
		}
	}

	public static BitmapSource? DecodeDataUrl(string dataUrl, int px)
	{
		try
		{
			int comma = dataUrl.IndexOf(',');
			if (comma < 0 || dataUrl.IndexOf(";base64", 0, comma, StringComparison.OrdinalIgnoreCase) < 0)
			{
				return null;
			}
			byte[] bytes = Convert.FromBase64String(dataUrl.Substring(comma + 1));
			if (dataUrl.StartsWith("data:image/svg", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			// Win7 没有 WebP 解码器：失败时转 PNG 重试（见 WebpFallback）
			return WebpFallback.Decode(bytes, delegate(byte[] b)
			{
				BitmapImage bi = new BitmapImage();
				bi.BeginInit();
				bi.CacheOption = BitmapCacheOption.OnLoad;
				bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
				bi.DecodePixelWidth = Math.Max(16, px * 2);
				bi.StreamSource = new MemoryStream(b);
				bi.EndInit();
				bi.Freeze();
				return (BitmapSource)bi;
			});
		}
		catch
		{
			return null;
		}
	}
}

/// <summary>权限的中文名和说明（契约 §2）。</summary>
public static class PluginPerms
{
	public static string Label(string perm)
	{
		return perm switch
		{
			"launcher" => I18n.L("启动器"),
			"ai.tools" => I18n.L("AI 工具"),
			"ai.presets" => I18n.L("AI 预设"),
			"ai.sessions" => I18n.L("AI 对话"),
			"ai.llm" => I18n.L("调用模型"),
			"credentials" => I18n.L("凭据"),
			"ui" => I18n.L("界面"),
			"ui.overlay" => I18n.L("主窗口浮层"),
			"system" => I18n.L("完整电脑权限"),
			_ => perm,
		};
	}

	public static string Describe(string perm)
	{
		return perm switch
		{
			"launcher" => I18n.L("调用启动器的全部功能（下载、改实例、启动游戏等），订阅启动器事件，在启动前修改参数"),
			"ai.tools" => I18n.L("给 AI 助手注册工具"),
			"ai.presets" => I18n.L("注册 AI 预设（人设、可用工具、步数）"),
			"ai.sessions" => I18n.L("新建、读取、驱动 AI 对话"),
			"ai.llm" => I18n.L("直接调用模型，会消耗你的 AI 额度"),
			"credentials" => I18n.L("读写它自己的凭据（加密保存）"),
			"ui" => I18n.L("侧栏页面、AI 标签、首页卡片、设置页、提示框"),
			"ui.overlay" => I18n.L("在主窗口上显示浮层"),
			"system" => I18n.L("可以读写你的文件、访问网络、运行程序。Node 插件做不到沙箱，只安装你信任的插件"),
			_ => I18n.L("未知权限"),
		};
	}

	public static bool Dangerous(string perm)
	{
		return perm == "system";
	}

	/// <summary>Node 插件自动带 system（契约 §2）。</summary>
	public static List<string> Effective(IEnumerable<string> perms, string runtime)
	{
		List<string> list = perms.Distinct<string>().ToList();
		if ((runtime ?? "node") == "node" && !list.Contains("system"))
		{
			list.Add("system");
		}
		return list;
	}

	/// <summary>权限清单（红色标出 system）。</summary>
	public static FrameworkElement List(IEnumerable<string> perms, IEnumerable<string>? highlightNew = null)
	{
		HashSet<string> fresh = new HashSet<string>(highlightNew ?? Array.Empty<string>());
		SPanel panel = Ui.V(6.0);
		foreach (string p in perms)
		{
			bool danger = Dangerous(p);
			TextBlock name = Ui.Txt(Label(p), 13.0, bold: true, danger ? "B.Danger" : "B.Ink");
			TextBlock code = Ui.Txt(p, 11.0, bold: false, "B.InkFaint");
			SPanel head = Ui.H(6.0, name, code.VCenter(), fresh.Contains(p) ? Ui.Tag(I18n.L("新增"), "B.Warn", "B.WarnSoft") : null);
			TextBlock desc = Ui.Small(Describe(p)).Wrap();
			if (danger)
			{
				desc.SetResourceReference(TextBlock.ForegroundProperty, "B.Danger");
			}
			Grid row = Ui.G(null, "Auto,*");
			row.Add(Ui.Glyph(danger ? "\ue7ba" : "\uea18", 13.0, danger ? "B.Danger" : "B.InkMuted").VTop().M(0.0, 2.0, 8.0), 0);
			row.Add(Ui.V(1.0, head, desc), 0, 1);
			panel.Children.Add(row);
		}
		if (panel.Children.Count == 0)
		{
			panel.Children.Add(Ui.Muted(I18n.L("不需要额外权限")));
		}
		return panel;
	}
}
