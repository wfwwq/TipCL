using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.Services;

namespace TipCL.Pages.Plugins;

/// <summary>
/// C7b(批4 块1):Windows 版 <c>Pages\Plugins\PluginsPage.Dev.cs</c>(11969 字节 / 319 行 / WPF / net48)到
/// Avalonia 11 / net8 的移植 —— 开发者页签:本地安装(zip / 文件夹 / 链接开发文件夹)、运行环境状态、
/// 模型供应商、插件日志。
///
/// Windows → mac 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><b>"网页视图(WebView2)"那一行按 GOAL.md 第 34 行改成如实的一行</b>:Windows 在这里读
///   <c>WebViewHost.Available / RuntimeVersion / UnavailableReason / IsWin10OrLater / RuntimeDownloadUrl</c>
///   并给「安装 WebView2 运行时」「重新检测」两个按钮 —— mac **不内嵌浏览器**,没有 WebView2 可装、也没有
///   "重新检测"可言 ⇒ 这一行如实说明"插件页面在系统浏览器里打开",**不给假按钮**(设计差异口径同 D-154/D-158);</item>
/// <item><c>Dlg.Form(…)</c> → <c>Dlg.Form&lt;ProviderForm&gt;(…)</c>(mac 的这个成员要求显式类型参数 —— 与
///   `PluginSettingsCard` 那两处同一个写法);</item>
/// <item><c>UIElement</c> → <c>Control</c>;<c>_logBox.ScrollToEnd()</c> → 本页既有
///   <c>ScrollLogToEnd(...)</c>(模板里的 <c>ScrollViewer</c>,D-128 口径);</item>
/// <item><c>items.Select(...)</c> 的 <c>ItemsControl.Items</c> 两平台同名;<c>PasswordBox.Password</c> 同名;
///   <c>Ui.Pw</c> / <c>Ui.Combo</c> / <c>Ui.Field</c> / <c>Ui.Section</c> / <c>Ui.RowCard</c> 均照用。</item>
/// </list>
/// </summary>
public sealed partial class PluginsPage
{
	private Control _devView = null!;

	private readonly SPanel _runtimeHost = Ui.V(2.0);

	private readonly SPanel _providerHost = Ui.V(8.0);

	private readonly ComboBox _logPick = Ui.Combo(null, null, 240.0, I18n.L("选择插件"));

	private readonly TextBox _logBox = Ui.LogBox();

	private readonly List<string> _logIds = new List<string>();

	private void BuildDev()
	{
		Button zip = Ui.Btn(I18n.L("从 zip 安装"), BtnKind.Soft, null, "\ue8b5");
		zip.Click += delegate
		{
			string? path = Dlg.PickFile(I18n.L("插件包") + " (*.zip)|*.zip", I18n.L("选择插件包"));
			if (path != null)
			{
				PageBase.Run(() => InstallLocalAsync(path, dev: false, zip), I18n.L("安装失败"));
			}
		};
		Button folder = Ui.Btn(I18n.L("从文件夹安装"), BtnKind.Soft, null, "\ue8b7");
		folder.Click += delegate
		{
			string? path2 = Dlg.PickFolder(I18n.L("选择插件文件夹(里面有 tipcl-plugin.json)"));
			if (path2 != null)
			{
				PageBase.Run(() => InstallLocalAsync(path2, dev: false, folder), I18n.L("安装失败"));
			}
		};
		Button link = Ui.Btn(I18n.L("链接开发文件夹"), BtnKind.Primary, null, "\ue71b");
		link.Click += delegate
		{
			string? path3 = Dlg.PickFolder(I18n.L("选择插件开发文件夹(不复制,改了自动重载)"));
			if (path3 != null)
			{
				PageBase.Run(() => InstallLocalAsync(path3, dev: true, link), I18n.L("链接失败"));
			}
		};
		Border local = Ui.Card(Ui.V(8.0, Ui.Section(I18n.L("本地安装"), I18n.L("zip / 文件夹会复制进 TipCL;开发文件夹只链接,文件一改就自动重载")), Ui.H(8.0, zip, folder, link), Ui.Small(I18n.L("也可以直接把插件 zip 或文件夹拖进这个页面。"))));
		Button runtimeRefresh = Ui.Btn(I18n.L("刷新"), BtnKind.Chip, delegate
		{
			PageBase.Run(LoadRuntimeAsync, I18n.L("读取失败"));
		}, "\ue72c");
		Border runtime = Ui.Card(Ui.V(4.0, Ui.Section(I18n.L("运行环境"), I18n.L("Node 插件宿主和网页视图"), runtimeRefresh), _runtimeHost));
		Button addProvider = Ui.Btn(I18n.L("添加供应商"), BtnKind.Chip, delegate
		{
			PageBase.Run(() => EditProviderAsync(null), I18n.L("保存失败"));
		}, "\ue710");
		Border providers = Ui.Card(Ui.V(8.0, Ui.Section(I18n.L("模型供应商"), I18n.L("插件直接调用模型时可选的 OpenAI 兼容端点;默认的 tipcl 就是你在 AI 设置里配的模型"), addProvider), _providerHost));
		_logBox.Height = 300.0;
		_logPick.SelectionChanged += delegate
		{
			PageBase.Run(LoadLogAsync);
		};
		Button logRefresh = Ui.Btn(I18n.L("刷新"), BtnKind.Chip, delegate
		{
			PageBase.Run(LoadLogAsync);
		}, "\ue72c");
		Button logFolder = Ui.Btn(I18n.L("打开日志文件夹"), BtnKind.Chip, delegate
		{
			string root;
			try
			{
				root = BridgeHost.FindRoot();
			}
			catch
			{
				root = AppContext.BaseDirectory;
			}

			OpenFolder(System.IO.Path.Combine(root, "plugin-logs"));
		}, "\ue8b7");
		Border logs = Ui.Card(Ui.V(8.0, Ui.Section(I18n.L("插件日志"), I18n.L("console.* 和 ctx.logger 的输出")), Ui.H(8.0, _logPick, logRefresh, logFolder), _logBox));
		_devView = PageBase.ScrollBody(local, runtime, providers, logs);
	}

	private async Task LoadDevAsync()
	{
		await LoadRuntimeAsync();
		await LoadProvidersAsync();
		if (_installed.Count == 0)
		{
			await LoadInstalledAsync();
		}

		string keep = (_logPick.SelectedIndex >= 0 && _logPick.SelectedIndex < _logIds.Count) ? _logIds[_logPick.SelectedIndex] : "";
		_logIds.Clear();
		_logIds.AddRange(_installed.Select((PluginInfo p) => p.Id));
		_logIds.Add("host");
		List<string> names = _installed.Select((PluginInfo p) => p.DisplayName).ToList();
		names.Add(I18n.L("Node 宿主"));
		_logPick.Items.Clear();
		foreach (string n in names)
		{
			_logPick.Items.Add(n);
		}

		int idx = _logIds.IndexOf(keep);
		_logPick.SelectedIndex = (idx >= 0) ? idx : 0;
	}

	private async Task LoadLogAsync()
	{
		int i = _logPick.SelectedIndex;
		if (i < 0 || i >= _logIds.Count)
		{
			_logBox.Text = "";
			return;
		}

		_logBox.Text = await ReadLogAsync(_logIds[i]);
		ScrollLogToEnd(_logBox);
	}

	private static string RuntimeLabel(string key)
	{
		return key switch
		{
			"node" => I18n.L("Node"),
			"nodePath" => I18n.L("Node 路径"),
			"node_path" => I18n.L("Node 路径"),
			"nodeVersion" => I18n.L("Node 版本"),
			"node_version" => I18n.L("Node 版本"),
			"host" => I18n.L("宿主状态"),
			"state" => I18n.L("宿主状态"),
			"hostState" => I18n.L("宿主状态"),
			"host_state" => I18n.L("宿主状态"),
			"port" => I18n.L("网页端口"),
			"webPort" => I18n.L("网页端口"),
			"web_port" => I18n.L("网页端口"),
			"pid" => I18n.L("进程"),
			"error" => I18n.L("错误"),
			"restarts" => I18n.L("重启次数"),
			"hostVersion" => I18n.L("宿主版本"),
			"host_version" => I18n.L("宿主版本"),
			"version" => I18n.L("版本"),
			"path" => I18n.L("路径"),
			"source" => I18n.L("来源"),
			"startedAt" => I18n.L("启动时间"),
			"nodeRequired" => I18n.L("需要 Node"),
			"downloadTask" => I18n.L("下载任务"),
			"home" => I18n.L("插件目录"),
			_ => key,
		};
	}

	/// <summary>嵌套对象(node / host)按「标签:值」逐行显示,空值略过。</summary>
	private static string FlattenRuntime(JsonElement obj)
	{
		List<string> lines = new List<string>();
		foreach (JsonProperty p in obj.EnumerateObject())
		{
			string v = p.Value.ValueKind switch
			{
				JsonValueKind.String => p.Value.GetString() ?? "",
				JsonValueKind.True => I18n.L("是"),
				JsonValueKind.False => I18n.L("否"),
				JsonValueKind.Null => "",
				JsonValueKind.Number => (p.Value.GetRawText() == "0") ? "" : p.Value.GetRawText(),
				_ => p.Value.GetRawText(),
			};
			if (v.Length > 0)
			{
				lines.Add(RuntimeLabel(p.Name) + ":" + v);
			}
		}

		return (lines.Count == 0) ? "—" : string.Join("\n", lines);
	}

	private async Task LoadRuntimeAsync()
	{
		_runtimeHost.Children.Clear();
		JsonElement r = await PageBase.Api.TryCallAsync<JsonElement>("plugins_runtime_status");
		if (r.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty prop in r.EnumerateObject())
			{
				string v = (prop.Value.ValueKind == JsonValueKind.Object) ? FlattenRuntime(prop.Value) : (prop.Value.ValueKind switch
				{
					JsonValueKind.String => prop.Value.GetString() ?? "",
					JsonValueKind.True => I18n.L("是"),
					JsonValueKind.False => I18n.L("否"),
					JsonValueKind.Null => "—",
					_ => prop.Value.GetRawText(),
				});
				TextBlock val = Ui.Txt(v, 12.5, bold: false, (prop.Name == "error" && v.Length > 0) ? "B.Danger" : "B.InkSoft").Wrap();
				_runtimeHost.Children.Add(Ui.Field(RuntimeLabel(prop.Name), val, null, 140.0));
			}
		}
		else
		{
			_runtimeHost.Children.Add(Ui.Field(I18n.L("Node 宿主"), Ui.Muted(I18n.L("读不到状态(后端还不支持插件,或者没连上)")), null, 140.0));
		}

		// 【Windows 源】:这里读 WebViewHost.Available/RuntimeVersion/UnavailableReason/IsWin10OrLater,
		// 并给「安装 WebView2 运行时」「重新检测」两个按钮。
		// 【mac 设计差异(GOAL.md 第 34 行,口径同 D-154/D-158)】:mac **不内嵌浏览器** —— 没有 WebView2 可装、
		// 也没有"重新检测"可言;插件页面本来就**在系统浏览器里打开**(完整可用的功能,只是"看在哪里"不同)。
		// 所以这一行**如实说明**,**不给假按钮**。
		_runtimeHost.Children.Add(Ui.Field(I18n.L("网页视图"), Ui.Txt(I18n.L("mac 版不内嵌浏览器(任务书规定):插件的网页界面在系统浏览器里打开"), 12.5, bold: false, "B.InkSoft").Wrap(), null, 140.0));
	}

	private async Task LoadProvidersAsync()
	{
		_providerHost.Children.Clear();
		JsonElement r = await PageBase.Api.TryCallAsync<JsonElement>("plugins_providers");
		List<JsonElement> items = J.Items(r).ToList();
		if (items.Count == 0)
		{
			_providerHost.Children.Add(Ui.Muted(I18n.L("只有默认的 tipcl(跟随 AI 设置)。")));
			return;
		}

		foreach (JsonElement p in items)
		{
			string id = J.Str(p, "id");
			string name = J.Str(p, "name", id);
			string baseUrl = J.Str(p, "baseUrl");
			bool builtin = id == "tipcl" || J.Bool(p, "builtin");
			bool hasKey = J.Bool(p, "configured", J.Bool(p, "hasKey", J.Str(p, "apiKeyRef").Length > 0));
			Grid g = Ui.G(null, "*,Auto");
			g.Add(Ui.V(2.0, Ui.H(6.0, Ui.Txt(name, 13.0, bold: true), Ui.Tag(id), builtin ? Ui.Tag(I18n.L("默认")) : (hasKey ? Ui.Tag(I18n.L("已配置 Key"), "B.Ok", "B.AccentSoft") : Ui.Tag(I18n.L("没有 Key"), "B.Warn", "B.WarnSoft"))), Ui.Small(builtin ? I18n.L("使用 AI 设置里的接入方式和模型") : baseUrl).Trim()), 0);
			if (!builtin)
			{
				JsonElement copy = p.Clone();
				g.Add(Ui.H(4.0, Ui.IconBtn("\ue70f", I18n.L("编辑"), delegate
				{
					PageBase.Run(() => EditProviderAsync(copy), I18n.L("保存失败"));
				}), Ui.IconBtn("\ue74d", I18n.L("删除"), delegate
				{
					PageBase.Run(async delegate
					{
						if (await Dlg.Confirm(I18n.L("删除供应商「{0}」?", name), I18n.L("用到它的插件会改用默认的 tipcl。"), I18n.L("删除"), null, danger: true))
						{
							await PageBase.Api.CallAsync("plugins_remove_provider", new { id });
							await LoadProvidersAsync();
						}
					}, I18n.L("删除失败"));
				})).VCenter(), 0, 1);
			}

			_providerHost.Children.Add(Ui.RowCard(g, null, 10.0));
		}
	}

	private sealed class ProviderForm
	{
		public string Id = "";

		public string Name = "";

		public string BaseUrl = "";

		public string ApiKey = "";
	}

	private async Task EditProviderAsync(JsonElement? existing)
	{
		bool edit = existing.HasValue;
		JsonElement e = existing.GetValueOrDefault();
		TextBox id = Ui.Input("openrouter", edit ? J.Str(e, "id") : "", 300.0);
		id.IsEnabled = !edit;
		TextBox name = Ui.Input("OpenRouter", edit ? J.Str(e, "name") : "", 300.0);
		TextBox baseUrl = Ui.Input("https://openrouter.ai/api/v1", edit ? J.Str(e, "baseUrl") : "", 300.0);
		// 【mac 差异】Ui.Pw 返回的是 TextBox(不是 PasswordBox —— 那是我按 WPF 记忆写错的类型名),
		// 取值走 .Text(与本仓其它用 Ui.Pw 的页面一致)
		TextBox key = Ui.Pw(300.0);
		key.Tag = edit ? I18n.L("留空不修改") : "sk-...";
		SPanel body = Ui.V(2.0, Ui.Field("ID", id, I18n.L("小写字母、数字、- 和 _"), 120.0), Ui.Field(I18n.L("名称"), name, null, 120.0), Ui.Field(I18n.L("地址"), baseUrl, I18n.L("OpenAI 兼容的 base_url"), 120.0), Ui.Field("API Key", key, I18n.L("加密保存在本机"), 120.0));
		// 【mac 差异】Dlg.Form 要显式类型参数(与 PluginSettingsCard 两处同一写法)
		ProviderForm? form = await Dlg.Form<ProviderForm>(edit ? I18n.L("编辑模型供应商") : I18n.L("添加模型供应商"), body, delegate
		{
			string i = id.Text.Trim().ToLowerInvariant();
			if (i.Length == 0 || i == "tipcl" || !i.All((char c) => char.IsLetterOrDigit(c) || c == '-' || c == '_'))
			{
				throw new ArgumentException(I18n.L("ID 只能用小写字母、数字、- 和 _,且不能是 tipcl"));
			}

			string u = baseUrl.Text.Trim();
			if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			{
				throw new ArgumentException(I18n.L("地址要以 http:// 或 https:// 开头"));
			}

			return new ProviderForm
			{
				Id = i,
				Name = ((name.Text.Trim().Length > 0) ? name.Text.Trim() : i),
				BaseUrl = u,
				ApiKey = key.Text,
			};
		});
		if (form != null)
		{
			Dictionary<string, object> p = new Dictionary<string, object>
			{
				["id"] = form.Id,
				["name"] = form.Name,
				["baseUrl"] = form.BaseUrl,
			};
			if (form.ApiKey.Length > 0)
			{
				p["apiKey"] = form.ApiKey;
			}

			await PageBase.Api.CallAsync("plugins_set_provider", new { p });
			PageBase.Toast(I18n.L("已保存"), form.Name, ToastKind.Success);
			await LoadProvidersAsync();
		}
	}
}
