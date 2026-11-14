using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TipCL.Services;

namespace TipCL.Pages.Plugins;

/// <summary>
/// C7b:Windows 版 <c>Pages\Plugins\PluginUi.cs</c>(13921 字节 / 494 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// 插件界面贡献的本地快照(<c>plugins_ui</c>)与已安装插件列表(<c>plugins_list</c>)。
///
/// 收到 <c>plugins.ui_changed</c> / <c>plugins.changed</c> 时刷新,再发 <see cref="Changed"/> /
/// <see cref="PluginsChanged"/>,侧栏、主页卡片、浮层各自重建。**快照、并发合并、比较逻辑一字未改。**
///
/// Windows → mac 的差异(逐条,理由写在行内;其中第 1 条是 <b>GOAL.md 第 34 行规定的设计差异,不是"缺件"</b>):
/// <list type="number">
/// <item><b>插件网页界面用系统浏览器打开(任务书规定:mac 不嵌 WebView)</b> —— 见 <see cref="NativePluginUi.Content"/>
///   与 <see cref="PluginWebContent"/>:Windows 最后一步是 <c>new PluginWebView(url, …)</c>(WebView2);
///   mac 换成**一张如实的内容卡 + 「在浏览器中打开」按钮**,点击走 B1b 已落的系统浏览器口径
///   <c>Crash.ShellOpen</c>。<c>UiContribution.Url</c> 是**已带 pageToken 的视图地址**,系统浏览器打开它是
///   **完整可用的功能**,与 Windows 的差别只是"看在哪里"(记 DECISIONS 为设计差异);</item>
/// <item><c>WebViewHost.DevPlugins</c>(Windows 第 113-116 行)在 mac **如实不建**:那道注册表是给 WebView2 宿主读的,
///   mac 没有 WebView 消费方,建一个空集合只会让人以为"有东西在等它"。</item>
/// <item><c>WebpFallback</c> + WPF 位图(第 394-405 行)**不是 WebView 问题**:它只是 WebP 解码兜底,
///   换成本仓既有的 <see cref="ImageDecode"/>(Core 的 Skia 编解码器)—— 与 <c>PluginIcon</c> 同一处理;</item>
/// <item><c>FrameworkElement</c> → <c>Control</c>;<c>RenderOptions.SetBitmapScalingMode(HighQuality)</c> →
///   <c>SetBitmapInterpolationMode(HighQuality)</c>(Wallpaper.cs 的既有口径);<c>SetResourceReference</c> → <c>Dyn(...)</c>;</item>
/// <item><c>Smoke.Note</c> → <c>Smoke.SmokeState.Note</c>(mac 同名门面);<c>AppServices.Ready</c> 两平台同名。</item>
/// </list>
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

	/// <summary>界面贡献变了(在 UI 线程上触发)。</summary>
	public static event Action? Changed;

	/// <summary>已安装插件列表变了(在 UI 线程上触发)。</summary>
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

	/// <summary>刷新界面贡献(合并并发请求)。</summary>
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
		// 【Windows 源】这里原有一段把 dev 插件 id 塞进 WebViewHost.DevPlugins(给 WebView2 宿主读)。
		// 【mac 差异】mac 不嵌 WebView(任务书规定用系统浏览器打开),没有消费方 ⇒ **如实不建**这个注册表。
		if (listChanged)
		{
			try
			{
				PluginsChanged?.Invoke();
			}
			catch (Exception ex)
			{
				Smoke.SmokeState.Note("plugins", ex.ToString());
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
				Smoke.SmokeState.Note("plugins", ex2.ToString());
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
/// .NET 插件的原生界面。内核在 <c>TipCL.Plugins.PluginUiRegistry</c> 里登记(契约 §3.2);
/// 这里用反射去找,界面层不对内核类型做编译期依赖,两边可以各自独立改、各自编译。
/// 主路径:<c>PluginUiRegistry.Find(plugin, kind, id)</c> → <c>NativeUiEntry.Create()</c> / <c>CreateAiView(chatId)</c>;
/// 兼容路径:静态 <c>CreatePage / CreateDashCard / CreateAiView</c>。找不到就当没有原生界面。
///
/// 【mac 差异】返回类型从 <c>FrameworkElement</c> 换成 <c>Control</c>;
/// 并且**两个程序集都找**:Windows 侧注册表在 App 程序集里,而 mac 的 <c>PluginUiRegistry</c> 由 Core 提供
/// (命名空间同样是 <c>TipCL.Plugins</c>,但它在 <c>TipCL.Core</c> 里)—— 所以先找 App 自己的,再找 Core 的。
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
					_type = typeof(App).Assembly.GetType("TipCL.Plugins.PluginUiRegistry", throwOnError: false)
						?? typeof(TipCL.Core.Launcher.Crash).Assembly.GetType("TipCL.Plugins.PluginUiRegistry", throwOnError: false);
				}
				catch
				{
					_type = null;
				}
			}

			return _type;
		}
	}

	private static Control? Error(Exception ex)
	{
		return Ui.Empty("\ue7ba", I18n.L("插件界面出错了"), ex.Message);
	}

	private static Control? Create(string plugin, string kind, string id, string? chatId)
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
				typeof(string),
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
					MethodInfo? createAi = entry.GetType().GetMethod("CreateAiView", new Type[1] { typeof(string) });
					return createAi?.Invoke(entry, new object[1] { chatId ?? "" }) as Control;
				}

				MethodInfo? create = entry.GetType().GetMethod("Create", Type.EmptyTypes);
				return create?.Invoke(entry, null) as Control;
			}

			string name = kind switch
			{
				"page" => "CreatePage",
				"dashCard" => "CreateDashCard",
				_ => "CreateAiView",
			};
			object[] args = (kind == "aiView") ? new object[3] { plugin, id, chatId ?? "" } : new object[2] { plugin, id };
			MethodInfo? legacy = t.GetMethod(name, BindingFlags.Static | BindingFlags.Public, null, args.Select((object a) => typeof(string)).ToArray(), null);
			return legacy?.Invoke(null, args) as Control;
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

	public static Control? CreatePage(string plugin, string id)
	{
		return Create(plugin, "page", id, null);
	}

	public static Control? CreateDashCard(string plugin, string id)
	{
		return Create(plugin, "dashCard", id, null);
	}

	public static Control? CreateAiView(string plugin, string id, string chatId)
	{
		return Create(plugin, "aiView", id, chatId);
	}

	/// <summary>
	/// 按贡献生成内容:原生工厂优先,否则网页界面。
	///
	/// 【mac 差异 · 任务书规定(GOAL.md 第 34 行「插件网页界面:用系统浏览器打开(不嵌 WebView)」)】
	/// Windows 这里是 <c>new PluginWebView(url, …)</c>(WebView2 内嵌);mac 换成
	/// <see cref="PluginWebContent"/>:一张如实的内容卡 + 「在浏览器中打开」按钮,点击走既有的
	/// <c>Crash.ShellOpen</c>。<b>这是设计差异,不是降级</b> —— 带 pageToken 的地址在系统浏览器里是完整可用的。
	/// </summary>
	public static Control Content(UiContribution c, IDictionary<string, object?>? props = null, string? chatId = null)
	{
		if (c.Native)
		{
			Control? el = c.Kind switch
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
			// 网页界面但还没有地址:Node 宿主还没起来(或正在重启)。宿主就绪后 plugins.ui_changed 会带来地址
			return Ui.Empty("\uea86", I18n.L("插件页面还没准备好"), I18n.L("插件运行环境正在启动，稍等一下会自动出现。"));
		}

		return PluginWebContent(c.Url, c.Plugin, props);
	}

	/// <summary>
	/// mac 的"插件网页界面":不嵌浏览器,给一张卡把地址显示出来,点按钮用**系统浏览器**打开。
	/// <paramref name="props"/> 在 Windows 是喂给 WebView 的初始化参数;系统浏览器带不了它们
	/// —— 但地址本身已经带 pageToken(见 <c>UiContribution.Url</c> 的注释),所以打开即用;这里**如实忽略**并在文档里写明。
	/// </summary>
	private static Control PluginWebContent(string url, string plugin, IDictionary<string, object?>? props)
	{
		_ = props;
		TextBlock hint = Ui.Muted(I18n.L("mac 版的插件界面用系统浏览器打开(任务书规定不嵌浏览器)。")).Wrap();
		TextBlock address = Ui.Mono(url).Trim().Wrap();
		Button open = Ui.Btn(I18n.L("在浏览器中打开"), BtnKind.Primary, delegate
		{
			try
			{
				// B1b 已落的系统浏览器口径:Windows 走 explorer、mac 走 open、Linux 走 xdg-open,
				// 而且认 Crash.ShellOpenHook(测试靠它拦截,**绝不真开窗口**)
				TipCL.Core.Launcher.Crash.ShellOpen(url);
			}
			catch (Exception ex)
			{
				AppServices.Toast(I18n.L("打开失败"), ex.Message, ToastKind.Warning);
			}
		}, "\ue71b");
		SPanel body = Ui.V(8.0, Ui.Txt(I18n.L("插件界面"), 13.5, bold: true), hint, address, Ui.H(8.0, open));
		body.MinWidth = 320.0;
		Border card = Ui.Card(body, 14.0);
		// 把"哪个插件的界面"记在卡上(调试与测试看得到,不影响外观)
		card.Tag = plugin;
		return card;
	}
}

/// <summary>插件图标:Fluent 字形、data URL、http 图片都认。</summary>
public static class PluginIcon
{
	public const string DefaultGlyph = "\uea86";

	public static Control Make(string? icon, double size, string fg = "B.InkSoft")
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
			VerticalAlignment = VerticalAlignment.Center,
		};
		RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.HighQuality);
		if (icon.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
		{
			Bitmap? bmp = DecodeDataUrl(icon, (int)size);
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
				Height = size,
			};
			TextBlock placeholder = Ui.Glyph(DefaultGlyph, size * 0.8, fg);
			placeholder.HorizontalAlignment = HorizontalAlignment.Center;
			g.Children.Add(placeholder);
			g.Children.Add(img);
			Load(img, placeholder, icon, (int)size);
			return g;
		}

		return Ui.Glyph(DefaultGlyph, size * 0.8, fg);
	}

	private static async void Load(Image img, Control placeholder, string url, int px)
	{
		try
		{
			Bitmap? bmp = Thumbs.Cached(url, px) ?? (await Thumbs.LoadAsync(url, px));
			if (bmp != null)
			{
				img.Source = bmp;
				placeholder.IsVisible = false;
			}
		}
		catch
		{
			// 【Windows 源】:空 catch —— 图标加载失败就当没有,不打扰用户
		}
	}

	/// <summary>
	/// 【Windows 源】<c>:379-411</c>:从 data URL 解出位图(SVG 直接放弃、小于 16px 的目标宽夹到 16)。
	/// 【mac 差异】解码换成既有 <see cref="ImageDecode"/>(Core 的 Skia 编解码器,WebP 也归它管)——
	/// Windows 那份的 <c>WebpFallback</c> + WPF <c>BitmapImage</c> 在 mac 没有对应物,而"解不出来就 null"的语义一致。
	/// </summary>
	public static Bitmap? DecodeDataUrl(string dataUrl, int px)
	{
		try
		{
			int comma = dataUrl.IndexOf(',');
			if (comma < 0 || dataUrl.IndexOf(";base64", 0, comma, StringComparison.OrdinalIgnoreCase) < 0)
			{
				return null;
			}

			if (dataUrl.StartsWith("data:image/svg", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}

			byte[] bytes = Convert.FromBase64String(dataUrl.Substring(comma + 1));
			return ImageDecode.Decode(bytes, Math.Max(16, px * 2));
		}
		catch
		{
			return null;
		}
	}
}

/// <summary>权限的中文名和说明(契约 §2)。</summary>
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

	/// <summary>Node 插件自动带 system(契约 §2)。</summary>
	public static List<string> Effective(IEnumerable<string> perms, string runtime)
	{
		List<string> list = perms.Distinct<string>().ToList();
		if ((runtime ?? "node") == "node" && !list.Contains("system"))
		{
			list.Add("system");
		}

		return list;
	}

	/// <summary>权限清单(红色标出 system)。</summary>
	public static Control List(IEnumerable<string> perms, IEnumerable<string>? highlightNew = null)
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
				desc.Dyn(TextBlock.ForegroundProperty, "B.Danger");
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
