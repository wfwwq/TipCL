using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TipCL.Core.Launcher;
using TipCL.Core.Plugins;

namespace TipCL.Core;

/// <summary>
/// 插件系统的 BackendApi RPC（DESIGN §10）。WPF 界面经 BridgeClient 调用；方法名就是 RPC 名，不能重载。
/// </summary>
public partial class BackendApi
{
	/// <summary>当前（或最近一次）AI 回合所在的对话 id；配合 _ai_busy 判断“正在跑哪个对话”。</summary>
	internal volatile string _ai_running_chat;

	private PluginManager _pm()
	{
		return PluginManager.Instance ?? PluginManager.StartGlobal(this);
	}

	/// <summary>已安装插件列表（DESIGN §10 plugins_list）。</summary>
	public object plugins_list()
	{
		PluginManager m = _pm();
		return m.Snapshot().OrderBy((PluginInstance p) => p.Manifest?.Name ?? p.Id, StringComparer.CurrentCulture).Select((PluginInstance p) => (object)m.InfoRow(p)).ToList();
	}

	/// <summary>合并后的市场列表：{plugins:[...每项带 installed/installedVersion/updatable/compatible], errors, indexes, allowUnverified}。</summary>
	public object plugins_market([Def(false)] object refresh = null)
	{
		PluginManager m = _pm();
		PyDict r = m.Market.Rows(Py.Truthy(refresh));
		if (Py.Truthy(refresh))
		{
			ThreadPool.QueueUserWorkItem(delegate
			{
				try
				{
					m.Market.CheckUpdates(refresh: false);
				}
				catch (Exception)
				{
				}
			});
		}
		return r;
	}

	/// <summary>从市场安装/更新（后台任务）。需要权限确认时发 plugins.confirm。返回 {task_id}。</summary>
	public object plugins_install(object id)
	{
		string pid = Py.S(id);
		PluginManager m = _pm();
		string tid = StartTask(I18nCore.Tr("安装插件") + " " + pid, (TaskContext ctx) => m.Market.InstallFromMarket(ctx, pid));
		return PyDict.Of("task_id", tid);
	}

	/// <summary>本地安装：zip 文件或文件夹（复制）；dev=true 时链接文件夹（不复制、热重载）。返回 {task_id}。</summary>
	public object plugins_install_local(object path, [Def(false)] object dev = null)
	{
		string p = Py.S(path);
		bool isDev = Py.Truthy(dev);
		PluginManager m = _pm();
		string tid = StartTask((isDev ? I18nCore.Tr("链接开发插件") : I18nCore.Tr("安装本地插件")) + " " + System.IO.Path.GetFileName(p.TrimEnd('/', '\\')), (TaskContext ctx) => m.Market.InstallPackage(ctx, p, new PluginMarket.InstallOptions
		{
			Source = (isDev ? "dev" : "local"),
			Verified = false
		}));
		return PyDict.Of("task_id", tid);
	}

	/// <summary>回复 plugins.confirm（权限确认）。</summary>
	public object plugins_confirm(object request_id, object ok)
	{
		return PyDict.Of("ok", _pm().Reply(Py.S(request_id), Py.Truthy(ok)));
	}

	/// <summary>卸载。delete_data=true 时同时删除 plugin-data/&lt;id&gt; 和凭据。返回 {ok, restartRequired}。</summary>
	public object plugins_uninstall(object id, [Def(false)] object delete_data = null)
	{
		return _pm().Market.Uninstall(Py.S(id), Py.Truthy(delete_data));
	}

	/// <summary>启用/停用。启用时有未授予的权限会先发 plugins.confirm（返回 pending:true）。</summary>
	public object plugins_set_enabled(object id, object enabled)
	{
		return _pm().SetEnabled(Py.S(id), Py.Truthy(enabled));
	}

	/// <summary>重新加载（后台进行，状态变化看 plugins.changed）。</summary>
	public object plugins_reload(object id)
	{
		PluginManager m = _pm();
		string pid = Py.S(id);
		m.Require(pid);
		ThreadPool.QueueUserWorkItem(delegate
		{
			try
			{
				m.Reload(pid);
			}
			catch (Exception ex)
			{
				m.Log(pid, "error", "重载失败：" + ex.Message);
			}
		});
		return PyDict.Of("ok", true);
	}

	/// <summary>
	/// 自动设置页数据：{schema: manifest.config, values: {key: value}（secret 项为空串）, secrets: {key: {configured}},
	/// credentials: [...plugins_credentials], settingsUrl: 自定义设置视图地址或 ""}。
	/// </summary>
	public object plugins_get_config(object id)
	{
		PluginManager m = _pm();
		PluginInstance p = m.Require(Py.S(id));
		PyDict values = m.EffectiveConfig(p);
		PyDict secrets = new PyDict();
		foreach (KeyValuePair<string, object> kv in p.Manifest?.Config ?? new PyDict())
		{
			if (p.Manifest.IsSecretConfig(kv.Key))
			{
				secrets[kv.Key] = PyDict.Of("configured", Py.S(values.Get(kv.Key)).Length > 0);
				values[kv.Key] = "";
			}
		}
		UiContribution custom = m.FindUi(p.Id, "settings", "settings");
		return PyDict.Of("schema", p.Manifest?.Config ?? new PyDict(), "values", values, "secrets", secrets, "credentials", m.CredentialRows(p), "settingsUrl", (custom != null) ? m.ViewUrl(p.Id, Py.S(custom.Spec.Get("view"))) : "");
	}

	/// <summary>改一项配置（按 manifest 校验类型/范围），插件立即收到 config.changed。</summary>
	public object plugins_set_config(object id, object key, object value)
	{
		_pm().SetConfig(Py.S(id), Py.S(key), value);
		return PyDict.Of("ok", true);
	}

	/// <summary>凭据状态：[{name, label, description, configured, declared}]（不含值）。</summary>
	public object plugins_credentials(object id)
	{
		PluginManager m = _pm();
		return m.CredentialRows(m.Require(Py.S(id)));
	}

	/// <summary>设置凭据（value 为空/null 删除）。</summary>
	public object plugins_set_credential(object id, object name, object value = null)
	{
		_pm().SetCredential(Py.S(id), Py.S(name), (value == null) ? null : Py.Str(value));
		return PyDict.Of("ok", true);
	}

	/// <summary>
	/// 全部插件 UI 贡献：[{plugin, kind, id, key, label, icon, order, url, spec, native}]。
	/// url 已带 pageToken（aiView 需要界面自己追加 &amp;props={"chatId":...}，URL 编码）；native=true 的去 TipCL.Plugins.PluginUiRegistry 拿控件。
	/// </summary>
	public object plugins_ui()
	{
		return _pm().UiRows();
	}

	/// <summary>回复 plugins.ui_request（confirm → true/false，prompt → 字符串或 null）。</summary>
	public object plugins_ui_reply(object request_id, object result = null)
	{
		return PyDict.Of("ok", _pm().Reply(Py.S(request_id), result));
	}

	/// <summary>
	/// 界面通知插件：页面打开/关闭、浮层被点击等（转成 Node 的 ui.event 通知）。
	/// </summary>
	public object plugins_ui_event(object plugin, object kind, object id, object @event, object data = null)
	{
		PluginManager m = _pm();
		PluginInstance p = m.Require(Py.S(plugin));
		PyDict prm = PyDict.Of("plugin", p.Id, "kind", Py.S(kind), "id", Py.S(id), "event", Py.S(@event), "data", data);
		NodeHostProcess h = m.Host;
		if (p.IsNode && h != null && h.Running)
		{
			h.Peer.Notify("ui.event", prm);
		}
		m.Publish("ui.event", prm, p.Id);
		return PyDict.Of("ok", true);
	}

	/// <summary>点击插件注册的 action（where: instance / launch / tray / ai.input）。context 是界面给的上下文（如实例名、对话 id）。</summary>
	public object plugins_action(object plugin, object id, object context = null)
	{
		PluginManager m = _pm();
		PluginInstance p = m.Require(Py.S(plugin));
		PyDict prm = PyDict.Of("plugin", p.Id, "id", Py.S(id), "context", context ?? new PyDict());
		NodeHostProcess h = m.Host;
		if (p.IsNode && h != null && h.Running)
		{
			h.Peer.Notify("action.invoke", prm);
		}
		m.Publish("action.invoke", prm, p.Id);
		return PyDict.Of("ok", true);
	}

	/// <summary>全部 agent 预设（新建对话选预设用）：[{id(完整), plugin, name, description, order, view}]。</summary>
	public object plugins_presets()
	{
		_pm();
		return AgentPresets.All().Select((AgentPreset p) => (object)p.ToInfo()).ToList();
	}

	/// <summary>额外模型供应商：[{id, name, baseUrl, configured}]（不含 apiKey）。</summary>
	public object plugins_providers()
	{
		PluginManager m = _pm();
		return m.State.Providers().Select((ProviderRecord p) => (object)PyDict.Of("id", p.Id, "name", p.Name, "baseUrl", p.BaseUrl, "configured", !string.IsNullOrEmpty(m.HostSecrets.Get(string.IsNullOrEmpty(p.ApiKeyRef) ? ("provider:" + p.Id) : p.ApiKeyRef)))).ToList();
	}

	/// <summary>新增/修改供应商 {id, name, baseUrl, apiKey?}。apiKey 为空时保留原来的；apiKey 存 DPAPI，引用名 provider:&lt;id&gt;。</summary>
	public object plugins_set_provider(object p)
	{
		PyDict d = (p as PyDict) ?? throw new TypeError("参数必须是对象");
		string id = Py.S(d.Get("id")).Trim();
		if (!PluginManifest.IsValidId(id) || id == "tipcl")
		{
			throw new ValueError(I18nCore.Tr("供应商 id 只能用小写字母、数字和 -_.（不能叫 tipcl）"));
		}
		string baseUrl = Py.S(d.Get("baseUrl")).Trim();
		if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			throw new ValueError(I18nCore.Tr("地址必须以 http:// 或 https:// 开头"));
		}
		PluginManager m = _pm();
		string keyRef = "provider:" + id;
		m.State.UpsertProvider(new ProviderRecord
		{
			Id = id,
			Name = Py.S(Py.Or(d.Get("name"), id)),
			BaseUrl = baseUrl,
			ApiKeyRef = keyRef
		});
		string apiKey = Py.S(d.Get("apiKey")).Trim();
		if (apiKey.Length > 0)
		{
			m.HostSecrets.Set(keyRef, apiKey);
		}
		return plugins_providers();
	}

	public object plugins_remove_provider(object id)
	{
		PluginManager m = _pm();
		string pid = Py.S(id);
		m.State.RemoveProvider(pid);
		m.HostSecrets.Set("provider:" + pid, null);
		return plugins_providers();
	}

	/// <summary>{node:{path, version, source}|null, nodeRequired, host:{state, error, pid, port, version, startedAt}, downloadTask, home}</summary>
	public object plugins_runtime_status()
	{
		return _pm().RuntimeStatus();
	}

	/// <summary>重启 Node 宿主（清掉崩溃计数）。</summary>
	public object plugins_restart_host()
	{
		PluginManager m = _pm();
		ThreadPool.QueueUserWorkItem(delegate
		{
			try
			{
				m.RestartHost();
			}
			catch (Exception ex)
			{
				m.LogHost("重启宿主失败：" + ex.Message);
			}
		});
		return PyDict.Of("ok", true);
	}

	/// <summary>手动检查更新（后台）。结果体现在 plugins_list 的 updateAvailable 上。</summary>
	public object plugins_check_updates()
	{
		PluginManager m = _pm();
		ThreadPool.QueueUserWorkItem(delegate
		{
			try
			{
				m.Market.CheckUpdates(refresh: true);
			}
			catch (Exception)
			{
			}
		});
		return PyDict.Of("ok", true);
	}

	/// <summary>读插件日志尾部（id 为 "host" 时读宿主日志）。</summary>
	public object plugins_log(object id, [Def(200L)] object lines = null)
	{
		string pid = Py.S(id);
		PluginManager m = _pm();
		// DESIGN §10：id="host" 读 Node 宿主日志；"_host"/空串是旧写法，一并兼容
		if (PluginManager.IsHostLogId(pid))
		{
			pid = "_host";
		}
		else
		{
			m.Require(pid);
		}
		return m.LogTail(pid, (int)Math.Max(1L, Math.Min(5000L, Py.IntOr(lines, 200L))));
	}
}
