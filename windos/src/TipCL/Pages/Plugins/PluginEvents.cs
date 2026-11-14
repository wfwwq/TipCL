using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TipCL.Services;

namespace TipCL.Pages.Plugins;

/// <summary>
/// 插件相关的 EventBus 事件（契约 §10）：plugins.ui_changed / plugins.changed / plugins.confirm / plugins.ui_request / plugins.error。
/// MainWindow 在 UI 线程上把每个桥事件转进来。
/// </summary>
public static class PluginEvents
{
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
			string msg = J.Str(ev.Payload, "message");
			AppServices.Toast((plugin.Length > 0) ? I18n.L("插件「{0}」出错", PluginUi.PluginName(plugin)) : I18n.L("插件出错"), msg, ToastKind.Error);
			break;
		}
		}
	}

	/// <summary>请求里的字段：先看顶层，再看 params / data（内核两种包法都认）。</summary>
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
			ok = ok
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
			// 顶层 kind 是请求类型 "toast"，提示级别放在 level（或包在 params/data 里的 kind）
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
			PluginOverlays.Update(plugin, p);
			break;
		default:
			result = null;
			break;
		}
		if (requestId.Length > 0)
		{
			// 字典传参：prompt 取消时 result 为 null，也要发出去
			await AppServices.Client.TryCallAsync<object>("plugins_ui_reply", new Dictionary<string, object?>
			{
				["request_id"] = requestId,
				["result"] = result
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

	/// <summary>插件页 id（同插件）、完整的 plugin:&lt;id&gt;:&lt;page&gt;，或内置页 id（launch、ai、settings…）。
	/// 同插件自己注册的页面 id 优先于内置页（dsh-game 注册了 id "library" 的页面，nav.open("library") 必须进插件页而不是实例库）。</summary>
	public static void OpenPage(string plugin, string page)
	{
		MainWindow? win = AppServices.Window;
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

	public static async Task OpenChatAsync(string chatId, string view, string plugin = "")
	{
		MainWindow? win = AppServices.Window;
		if (win == null)
		{
			return;
		}
		win.Navigate("ai");
		if (win.GetPage("ai") is AiPage ai)
		{
			await ai.EnsureLoadedAsync();
			await ai.OpenChatAsync(chatId, view, plugin);
		}
	}
}

/// <summary>安装/更新时的权限确认（plugins.confirm）。</summary>
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
			body.Children.Add(Ui.Small(I18n.L("这个插件没有经过市场审核，来源和代码都没人检查过。")).Wrap());
		}
		if (perms.Contains("system"))
		{
			Border warn = Ui.Pane(Ui.H(8.0, Ui.Glyph("\ue7ba", 15.0, "B.Danger").VTop(), Ui.Txt((runtime == "dotnet") ? I18n.L("这是一个 .NET 插件，运行在启动器进程里，拥有完整的电脑权限（文件、网络、运行程序）。只安装你信任的来源。") : I18n.L("这是一个 Node 插件，拥有完整的电脑权限（文件、网络、运行程序），做不到沙箱。只安装你信任的来源。"), 12.5, bold: false, "B.Danger").Wrap()), "B.DangerSoft", 10.0, 10.0);
			body.Children.Add(warn);
		}
		body.Children.Add(Ui.Muted((action == "update" && added.Count > 0) ? I18n.L("新版本需要新的权限：") : I18n.L("它需要这些权限：")));
		ScrollViewer sv = new ScrollViewer
		{
			Content = PluginPerms.List(perms, added),
			MaxHeight = 300.0,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto
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
