using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using TipCL.Services;

namespace TipCL.Pages.Plugins;

/// <summary>
/// C7b:Windows 版 <c>Pages\Plugins\PluginEvents.cs</c>(8217 字节 / 272 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// 插件相关的 EventBus 事件(契约 §10):<c>plugins.ui_changed</c> / <c>plugins.changed</c> /
/// <c>plugins.confirm</c> / <c>plugins.ui_request</c> / <c>plugins.error</c>。外壳在 UI 线程上把每个桥事件转进来。
///
/// Windows → mac 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>AppServices.Window</c> 在 mac 的静态类型是 <c>Window</c>(不是 <c>MainWindow?</c>,见 C2a 记录)
///   ⇒ 按既有写法 <c>AppServices.Window as MainWindow</c>;</item>
/// <item><c>MainWindow.NavDefs.Any(d =&gt; d.Id == page)</c> **照搬**:mac 的 <c>MainWindow.NavDefs</c>
///   是同一张只读页表(<c>public static readonly List&lt;NavDef&gt;</c>),所以"这是不是内置页 id"不需要新造判断;</item>
/// <item><b>两处注入缝(本批新加,均在未接时给可观测的如实行为,不假装成功)</b>:
///   <list type="bullet">
///   <item><see cref="ChatOpener"/> —— Windows 这里是 <c>win.GetPage("ai") is AiPage ai → await ai.OpenChatAsync(...)</c>,
///     而 <c>AiPage</c> 属 AI 批(mac 侧还不存在)。接了缝就调它;**没接就 Navigate("ai") + 一条用户可见的如实 Toast**
///     (照 <c>CrashUi.AiFixLauncher</c> 那条注入缝的兜底口径:**绝不静默、也不假装开过聊天窗**;
///     那条缝在正常启动路径上已经由 <c>Pages\AiFixLauncher.cs</c> 接好,这里只是照它的兜底写法),AI 批落地后接线即消除;</item>
///   <item><b>插件浮层(kind "overlay")是"由 GOAL.md 第 34 行导致的设计差异",不是缺件</b>:
///     Windows 这里是 <c>PluginOverlays.Update(plugin, p)</c> —— 造一个 **Win32 分层窗口**
///     (点击穿透 = <c>WS_EX_TRANSPARENT|WS_EX_LAYERED</c>,透明 = DWM 玻璃扩到客户区),内容由 **WebView2** 渲染;
///     这三条支柱在 mac 侧都没有对应物,而 GOAL-34 又规定不嵌 WebView ⇒ **不造假浮层、也不搬 <c>PluginOverlays.cs</c>**:
///     mac 只给**一次**(每次会话一次)用户可见的如实提示 + 「在浏览器中打开」(见 <see cref="NotifyOverlayUnsupportedAsync"/>),
///     之后只记一笔 <c>SmokeState.Note</c>。详见 DECISIONS 那条设计差异的登记。</item>
///   </list>
/// </item>
/// <item><c>ScrollViewer</c> / <c>ScrollBarVisibility</c> / <c>Ui.*</c> / <c>Dlg.*</c> 两平台同名同参,调用形状一字未改。</item>
/// </list>
/// </summary>
public static class PluginEvents
{
	/// <summary>
	/// 【注入缝 · 等 AI 批落地后接】打开一段 AI 对话:<c>(chatId, view, plugin) → Task</c>。
	/// 未接时 <see cref="OpenChatAsync"/> 会 Navigate("ai") 并弹一条如实 Toast(不静默、不假装)。
	/// </summary>
	internal static Func<string, string, string, Task>? ChatOpener { get; set; }

	/// <summary>
	/// 【设计差异 · 由 GOAL.md 第 34 行导致】**插件浮层(mac 不支持)的提示只弹一次**(同一次会话里重复请求不再反复弹)。
	/// 为什么要这个标志:浮层请求是**插件主动发来的界面请求**(用户点了插件里的东西),
	/// 首次**必须让用户看见**否则等于"点了没反应",但每次都弹会把用户刷屏 ⇒ 一次;之后只记 <c>SmokeState.Note</c>。
	/// </summary>
	private static bool _overlayNoticeShown;

	public static void Handle(BridgeEvent ev)
	{
		switch (ev.Event)
		{
		case "plugins.ui_changed":
		case "plugins.changed":
			PageBase.Run(PluginUi.RefreshAsync);
			break;
		case "plugins.confirm":
			PageBase.Run(() => ConfirmAsync(ev.Payload));
			break;
		case "plugins.ui_request":
			PageBase.Run(() => UiRequestAsync(ev.Payload), I18n.L("插件请求处理失败"));
			break;
		case "plugins.error":
		{
			string plugin = J.Str(ev.Payload, "plugin");
			string message = J.Str(ev.Payload, "message");
			AppServices.Toast((plugin.Length > 0) ? I18n.L("插件「{0}」出错", PluginUi.PluginName(plugin)) : I18n.L("插件出错"), message, ToastKind.Error);
			break;
		}
		}
	}

	/// <summary>请求里的字段:先看顶层,再看 params / data(内核两种包法都认)。</summary>
	internal static JsonElement Field(JsonElement p, string key)
	{
		if (J.TryGet(p, key, out JsonElement v))
		{
			return v;
		}

		string[] wraps = new string[2] { "params", "data" };
		foreach (string w in wraps)
		{
			JsonElement inner = J.Obj(p, w);
			if (inner.ValueKind == JsonValueKind.Object && J.TryGet(inner, key, out JsonElement v2))
			{
				return v2;
			}
		}

		return default(JsonElement);
	}

	internal static string FStr(JsonElement p, string key, string def = "")
	{
		JsonElement v = Field(p, key);
		return v.ValueKind switch
		{
			JsonValueKind.String => v.GetString() ?? def,
			JsonValueKind.Number => v.GetRawText(),
			_ => def,
		};
	}

	private static async Task ConfirmAsync(JsonElement p)
	{
		string requestId = J.Str(p, "requestId");
		bool ok = await PluginConfirm.ShowAsync(p);
		await AppServices.Client.TryCallAsync<object>("plugins_confirm", new
		{
			request_id = requestId,
			ok
		});
	}

	private static async Task UiRequestAsync(JsonElement p)
	{
		string requestId = J.Str(p, "requestId");
		string plugin = J.Str(p, "plugin");
		string kind = J.Str(p, "kind");
		string who = PluginUi.PluginName(plugin);
		object? result = true;
		switch (kind)
		{
		case "toast":
		{
			// 顶层 kind 是请求类型 "toast",提示级别放在 level(或包在 params/data 里的 kind)
			string level = FStr(p, "level", FStr(p, "toastKind"));
			if (level.Length == 0)
			{
				JsonElement inner = J.Obj(p, "params");
				if (inner.ValueKind != JsonValueKind.Object)
				{
					inner = J.Obj(p, "data");
				}

				level = J.Str(inner, "kind");
			}

			ToastKind tk = level switch
			{
				"success" => ToastKind.Success,
				"warn" => ToastKind.Warning,
				"warning" => ToastKind.Warning,
				"error" => ToastKind.Error,
				_ => ToastKind.Info,
			};
			AppServices.Toast(who, FStr(p, "text", FStr(p, "message")), tk);
			break;
		}
		case "confirm":
		{
			string title = FStr(p, "title", who);
			result = await Dlg.Confirm(title, FStr(p, "text"), NullIfEmpty(FStr(p, "ok")), NullIfEmpty(FStr(p, "cancel")));
			break;
		}
		case "prompt":
		{
			string title2 = FStr(p, "title", who);
			result = await Dlg.Prompt(title2, FStr(p, "text"), FStr(p, "default"), FStr(p, "placeholder"));
			break;
		}
		case "openPage":
			OpenPage(plugin, FStr(p, "page"));
			break;
		case "openChat":
		{
			string chatId = FStr(p, "chatId");
			string view = FStr(p, "view");
			await OpenChatAsync(chatId, view, plugin);
			break;
		}
		case "openExternal":
		{
			string url = FStr(p, "url");
			if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			{
				Ui.OpenUrl(url);
			}
			else
			{
				result = false;
			}

			break;
		}
		case "overlay":
			// 【Windows 源】:PluginOverlays.Update(plugin, p) —— 造一个 Win32 分层窗口,内容由 WebView2 渲染。
			// 【mac 设计差异(GOAL.md 第 34 行)】:那三条支柱(Win32 分层窗口的点击穿透、DWM 玻璃透明、
			// WebView2 内容宿主)在 mac 侧全都没有对应物 ⇒ **不造假浮层**,改成一次如实的用户可见提示。
			await NotifyOverlayUnsupportedAsync(plugin, p);
			break;
		default:
			result = null;
			break;
		}

		if (requestId.Length > 0)
		{
			// 字典传参:prompt 取消时 result 为 null,也要发出去
			await AppServices.Client.TryCallAsync<object>("plugins_ui_reply", new Dictionary<string, object?>
			{
				["request_id"] = requestId,
				["result"] = result,
			});
		}
	}

	private static string? NullIfEmpty(string s)
	{
		if (s.Length != 0)
		{
			return s;
		}

		return null;
	}

	/// <summary>
	/// mac 对插件浮层(kind <c>overlay</c>)的如实处置 —— **由 GOAL.md 第 34 行导致的设计差异,不是"未实现的降级"**
	/// (见 DECISIONS):Windows 那边是造一个 Win32 分层窗口(点击穿透 = <c>WS_EX_TRANSPARENT|WS_EX_LAYERED</c>,
	/// 透明 = DWM 玻璃扩到客户区),内容由 WebView2 渲染;这三条在 mac 侧都没有对应物,
	/// 而 GOAL-34 又规定不嵌 WebView ⇒ **不造窗口、也不假装浮层已显示**。
	///
	/// 行为:首次(每次会话一次)给一张**用户可见**的如实提示 + 「在浏览器中打开」(走既有的
	/// <c>Crash.ShellOpen</c>;测试靠 <c>Crash.ShellOpenHook</c> 拦截,绝不真开窗口);
	/// 之后同一次会话里只记一笔 <c>SmokeState.Note</c>(重复弹会把用户刷屏)。请求本身仍照常回
	/// <c>plugins_ui_reply</c>(见 <see cref="UiRequestAsync"/> 的公共尾部)。
	///
	/// 特意**没有**搬的部分(它们都依附于"那个窗口",没有窗口就没有它们要算的东西):
	/// spec 解析(<c>width/height/anchor/margin/clickThrough/visible</c> + <c>Clamp.Of</c>)、
	/// 贡献增删同步、跟随主窗口移动/缩放/最小化的重排。
	/// **回溯线索**:若将来 mac 有了可嵌入的视图宿主(或原生插件 UI 想要真浮层),
	/// 参照 Windows <c>Pages\Plugins\PluginOverlays.cs</c>(468 行,含 <c>OverlayWindow</c>)取回这套逻辑。
	/// </summary>
	private static async Task NotifyOverlayUnsupportedAsync(string plugin, JsonElement p)
	{
		Smoke.SmokeState.Note("plugins", "插件浮层请求(mac 不支持;按 GOAL.md 34 用浏览器打开):" + plugin);
		if (_overlayNoticeShown)
		{
			return;
		}

		_overlayNoticeShown = true;
		string id = FStr(p, "id");
		string url = PluginUi.OfKind("overlay").FirstOrDefault((UiContribution c) => c.Plugin == plugin && (id.Length == 0 || c.Id == id))?.Url ?? "";
		string text = I18n.L("mac 版的插件浮层没法就地显示:这里不内嵌浏览器(任务书规定用系统浏览器打开)。");
		if (url.Length == 0)
		{
			await Dlg.Alert(I18n.L("插件浮层"), text);
			return;
		}

		SPanel body = Ui.V(6.0, Ui.Muted(text).Wrap(), Ui.Mono(url).Trim().Wrap());
		if (await Dlg.Ask(I18n.L("插件浮层"), body, I18n.L("在浏览器中打开"), I18n.L("知道了")))
		{
			try
			{
				TipCL.Core.Launcher.Crash.ShellOpen(url);
			}
			catch (Exception ex)
			{
				AppServices.Toast(I18n.L("打开失败"), ex.Message, ToastKind.Warning);
			}
		}
	}

	/// <summary>插件页 id(同插件)、完整的 plugin:&lt;plugin&gt;:&lt;page&gt;,或内置页 id(launch、ai、settings…)。
	/// 同插件自己注册的页面 id 优先于内置页(dsh-game 注册了 id "library" 的页面,nav.open("library") 必须进插件页而不是实例库)。</summary>
	public static void OpenPage(string plugin, string page)
	{
		MainWindow? win = AppServices.Window as MainWindow;
		if (win == null || string.IsNullOrEmpty(page))
		{
			return;
		}

		UiContribution? own = PluginUi.Find("page", plugin, page);
		if (own != null)
		{
			win.Navigate(own.PageId);
			return;
		}

		// 【Windows 源】照搬:内置页 id 直接进(mac 的 NavDefs 是同一张只读页表,不需要新造判断)
		if (page == "plugins" || MainWindow.NavDefs.Any((NavDef d) => d.Id == page))
		{
			win.Navigate(page);
			return;
		}

		UiContribution? c = PluginUi.FindPage(page) ?? PluginUi.OfKind("page").FirstOrDefault((UiContribution x) => x.Id == page);
		if (c != null)
		{
			win.Navigate(c.PageId);
		}
	}

	/// <summary>
	/// 打开一段 AI 对话:接了 <see cref="ChatOpener"/> 就走它;**没接**(AI 批还没落地)时 Navigate("ai") +
	/// 一条**用户可见的如实 Toast** —— 绝不静默吞掉、也不假装打开过(AI 批接线后这条提示自然消失)。
	/// </summary>
	public static async Task OpenChatAsync(string chatId, string view, string plugin = "")
	{
		MainWindow? win = AppServices.Window as MainWindow;
		if (win == null)
		{
			return;
		}

		win.Navigate("ai");
		Func<string, string, string, Task>? opener = ChatOpener;
		if (opener != null)
		{
			await opener(chatId, view, plugin);
			return;
		}

		AppServices.Toast(I18n.L("AI 助手还没移植"), I18n.L("这台机器上的 AI 助手页还没落地,暂时打不开这段对话;等它落地后这里会自动接上。"), ToastKind.Warning);
	}
}

/// <summary>安装/更新时的权限确认(plugins.confirm)。</summary>
public static class PluginConfirm
{
	public static Task<bool> ShowAsync(JsonElement p)
	{
		string plugin = J.Str(p, "plugin");
		string name = J.Str(p, "name", PluginUi.PluginName(plugin));
		string version = J.Str(p, "version");
		string action = J.Str(p, "action", "install");
		string runtime = J.Str(p, "runtime", "node");
		bool verified = J.Bool(p, "verified");
		List<string> perms = PluginPerms.Effective(J.StrList(p, "permissions"), runtime);
		List<string> added = J.StrList(p, "newPermissions");
		if (added.Count == 0)
		{
			added = J.StrList(p, "added");
		}

		string title = action switch
		{
			"update" => I18n.L("更新插件"),
			"enable" => I18n.L("启用插件"),
			_ => I18n.L("安装插件"),
		};
		SPanel head = Ui.H(8.0, Ui.Txt(name, 15.0, bold: true).VCenter(), (version.Length > 0) ? Ui.Tag("v" + version) : null, verified ? Ui.Tag(I18n.L("已审核"), "B.Ok", "B.AccentSoft") : Ui.Tag(I18n.L("未审核"), "B.Danger", "B.DangerSoft"));
		SPanel body = Ui.V(12.0, head);
		if (!verified)
		{
			body.Children.Add(Ui.Small(I18n.L("这个插件没有经过市场审核,来源和代码都没人检查过。")).Wrap());
		}

		if (perms.Contains("system"))
		{
			Border warn = Ui.Pane(Ui.H(8.0, Ui.Glyph("\ue7ba", 15.0, "B.Danger").VTop(), Ui.Txt((runtime == "dotnet") ? I18n.L("这是一个 .NET 插件,运行在启动器进程里,拥有完整的电脑权限(文件、网络、运行程序)。只安装你信任的来源。") : I18n.L("这是一个 Node 插件,拥有完整的电脑权限(文件、网络、运行程序),做不到沙箱。只安装你信任的来源。"), 12.5, bold: false, "B.Danger").Wrap()), "B.DangerSoft", 10.0, 10.0);
			body.Children.Add(warn);
		}

		body.Children.Add(Ui.Muted((action == "update" && added.Count > 0) ? I18n.L("新版本需要新的权限:") : I18n.L("它需要这些权限:")));
		ScrollViewer sv = new ScrollViewer
		{
			Content = PluginPerms.List(perms, added),
			MaxHeight = 300.0,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
		};
		body.Children.Add(sv);
		body.MinWidth = 420.0;
		string ok = action switch
		{
			"update" => I18n.L("允许并更新"),
			"enable" => I18n.L("允许并启用"),
			_ => I18n.L("允许并安装"),
		};
		return Dlg.Ask(title, body, ok, I18n.L("取消"), perms.Contains("system") && !verified, 560.0);
	}
}
