using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace TipCL.Core.Plugins;

/// <summary>
/// 宿主 API（DESIGN §4）：Node 宿主发来的请求在这里分发，按插件 grantedPermissions 检查权限。
/// .NET 插件的上下文也走这里（<see cref="Invoke"/>），两边共享同一套实现。
/// </summary>
public sealed class PluginHostApi
{
	private readonly PluginManager _mgr;

	private RpcDispatcher _dispatcher;

	/// <summary>launcher.call 不允许调用的 BackendApi 方法（插件不能替用户点确认、改别的插件）。</summary>
	private static readonly HashSet<string> LauncherDeny = new HashSet<string>(StringComparer.Ordinal) { "ai_confirm", "ai_answer", "shutdown", "start_task" };

	public PluginHostApi(PluginManager mgr)
	{
		_mgr = mgr;
	}

	/// <summary>Node 宿主 → TipCL 请求。</summary>
	public object HandleRequest(NodeHostProcess host, string method, PyDict prm)
	{
		if (method == "host.hello")
		{
			return _mgr.OnHostHello(host, prm);
		}
		string plugin = Py.S(prm.Get("plugin"));
		if (method == "log" && plugin.Length == 0)
		{
			_mgr.LogHost(Py.S(prm.Get("text")));
			return null;
		}
		return Invoke(plugin, method, prm, delegate(string m, PyDict p)
		{
			host.Peer.Notify(m, p);
		}, host);
	}

	/// <summary>Node 宿主发来的通知（没有 id）：当作不需要结果的请求处理。</summary>
	public void HandleNotification(NodeHostProcess host, string method, PyDict prm)
	{
		ThreadPool.QueueUserWorkItem(delegate
		{
			try
			{
				HandleRequest(host, method, prm);
			}
			catch (Exception)
			{
			}
		});
	}

	private PluginInstance RequirePlugin(string plugin)
	{
		PluginInstance p = _mgr.Get(plugin);
		if (p == null || p.Manifest == null)
		{
			throw new PluginRpcException("unknown_plugin", "未知插件：" + plugin);
		}
		return p;
	}

	private void Need(string plugin, string permission)
	{
		_mgr.RequirePermission(plugin, permission);
	}

	/// <summary>
	/// 分发一个宿主 API 调用。notify 用于 llm.stream 过程中发 llm.chunk。host 为 null 表示 .NET 插件调用（不支持需要 Node 回调的注册）。
	/// </summary>
	public object Invoke(string plugin, string method, PyDict prm, Action<string, PyDict> notify = null, NodeHostProcess host = null)
	{
		PluginInstance p = RequirePlugin(plugin);
		switch (method)
		{
		case "log":
			_mgr.Log(plugin, Py.S(Py.Or(prm.Get("level"), "info")), Py.S(prm.Get("text")));
			return null;
		case "launcher.call":
			Need(plugin, PluginPermissions.Launcher);
			return LauncherCall(Py.S(prm.Get("method")), prm.Get("args") as List<object>, prm.Get("kwargs") as PyDict);
		case "events.subscribe":
			_mgr.Subscribe(plugin, Topics(prm));
			return null;
		case "events.unsubscribe":
			_mgr.Unsubscribe(plugin, Topics(prm));
			return null;
		case "tools.register":
		{
			Need(plugin, PluginPermissions.AiTools);
			PyDict tool = (prm.Get("tool") as PyDict) ?? throw new PluginRpcException("invalid_params", "缺少 tool");
			PluginToolEntry e = ToolEntry(plugin, tool);
			if (host == null)
			{
				throw new PluginRpcException("unsupported", ".NET 插件请用 RegisterTool");
			}
			e.Executor = NodeToolExecutor(host);
			PluginTools.Register(e);
			return null;
		}
		case "tools.unregister":
			Need(plugin, PluginPermissions.AiTools);
			PluginTools.Unregister(plugin, Py.S(prm.Get("name")));
			return null;
		case "presets.register":
		{
			Need(plugin, PluginPermissions.AiPresets);
			PyDict preset = (prm.Get("preset") as PyDict) ?? throw new PluginRpcException("invalid_params", "缺少 preset");
			AgentPreset ap = AgentPreset.FromDict(plugin, preset, p.Dir);
			AgentPresets.Register(ap);
			_mgr.EmitChanged();
			return PyDict.Of("id", ap.FullId);
		}
		case "presets.unregister":
			Need(plugin, PluginPermissions.AiPresets);
			AgentPresets.Unregister(plugin, Py.S(prm.Get("id")));
			_mgr.EmitChanged();
			return null;
		case "presets.list":
			return AgentPresets.All().Select((AgentPreset x) => (object)x.ToInfo()).ToList();
		case "ai.chats.create":
			Need(plugin, PluginPermissions.AiSessions);
			if (!prm.ContainsKey("open") || Py.Truthy(prm.Get("open")))
			{
				// 打开对话需要 ui 权限；没有就只建不开
				if (!_mgr.HasPermission(plugin, PluginPermissions.Ui))
				{
					prm["open"] = false;
				}
			}
			return _mgr.Sessions.Create(plugin, Py.S(prm.Get("preset")), Py.S(prm.Get("title")), Py.S(prm.Get("view")), Py.S(prm.Get("prompt")), !prm.ContainsKey("open") || Py.Truthy(prm.Get("open")), prm.Get("images") as List<object>);
		case "ai.chats.list":
			Need(plugin, PluginPermissions.AiSessions);
			return _mgr.Sessions.List();
		case "ai.chats.events":
			Need(plugin, PluginPermissions.AiSessions);
			return _mgr.Sessions.Events(Py.S(prm.Get("chatId")), Py.IntOr(prm.Get("from"), 0L));
		case "ai.chats.send":
			Need(plugin, PluginPermissions.AiSessions);
			return _mgr.Sessions.Send(plugin, Py.S(prm.Get("chatId")), Py.S(prm.Get("text")), Py.S(prm.Get("mode")), prm.Get("images") as List<object>);
		case "ai.chats.cancel":
			Need(plugin, PluginPermissions.AiSessions);
			return _mgr.Sessions.Cancel(Py.S(prm.Get("chatId")));
		case "ai.chats.open":
			Need(plugin, PluginPermissions.AiSessions);
			Need(plugin, PluginPermissions.Ui);
			_mgr.Sessions.Open(plugin, Py.S(prm.Get("chatId")), Py.S(prm.Get("view")));
			return null;
		case "ai.chats.state":
			Need(plugin, PluginPermissions.AiSessions);
			return _mgr.Sessions.StateOf(Py.S(prm.Get("chatId")));
		case "llm.providers":
			Need(plugin, PluginPermissions.AiLlm);
			return _mgr.Llm.Providers();
		case "llm.models":
			Need(plugin, PluginPermissions.AiLlm);
			return _mgr.Llm.Models(Py.S(prm.Get("provider")));
		case "llm.stream":
		{
			Need(plugin, PluginPermissions.AiLlm);
			string rid = Py.S(prm.Get("requestId"));
			return _mgr.Llm.Stream(plugin, prm, delegate(PyDict chunk)
			{
				notify?.Invoke("llm.chunk", PyDict.Of("requestId", rid, "plugin", plugin, "chunk", chunk));
			});
		}
		case "llm.abort":
			Need(plugin, PluginPermissions.AiLlm);
			_mgr.Llm.Abort(plugin, Py.S(prm.Get("requestId")));
			return null;
		case "credentials.resolve":
		{
			Need(plugin, PluginPermissions.Credentials);
			string v = p.Secrets.Get(Py.S(prm.Get("name")));
			return (v == null || Py.S(prm.Get("name")).StartsWith("config:", StringComparison.Ordinal)) ? null : PyDict.Of("value", v);
		}
		case "credentials.set":
			Need(plugin, PluginPermissions.Credentials);
			_mgr.SetCredential(plugin, Py.S(prm.Get("name")), prm.Get("value") as string);
			return null;
		case "credentials.status":
		{
			Need(plugin, PluginPermissions.Credentials);
			PyDict st = new PyDict();
			foreach (PyDict row in _mgr.CredentialRows(p).OfType<PyDict>())
			{
				st[Py.S(row["name"])] = PyDict.Of("configured", row["configured"]);
			}
			return st;
		}
		case "attachments.save":
		{
			Need(plugin, PluginPermissions.AiTools);
			List<object> r = new List<object>();
			foreach (PyDict im in Py.ListOr(prm.Get("images")).OfType<PyDict>())
			{
				string b64 = Py.S(im.Get("data"));
				int comma = b64.IndexOf(',');
				if (b64.StartsWith("data:", StringComparison.Ordinal) && comma > 0)
				{
					b64 = b64.Substring(comma + 1);
				}
				byte[] bytes;
				try
				{
					bytes = Convert.FromBase64String(b64);
				}
				catch (FormatException)
				{
					throw new PluginRpcException("invalid_params", "图片 data 不是 base64");
				}
				r.Add(PluginAttachments.Save(plugin, bytes, Py.S(im.Get("mediaType")), Py.S(im.Get("name"))));
			}
			return r;
		}
		case "config.get":
			return _mgr.EffectiveConfig(p);
		case "config.set":
			_mgr.SetConfig(plugin, Py.S(prm.Get("key")), prm.Get("value"));
			return null;
		case "ui.register":
		{
			string kind = Py.S(prm.Get("kind"));
			if (!new[] { "page", "aiView", "dashCard", "overlay", "settings", "action" }.Contains(kind))
			{
				throw new PluginRpcException("invalid_params", "未知 UI 类型：" + kind);
			}
			Need(plugin, (kind == "overlay") ? PluginPermissions.UiOverlay : PluginPermissions.Ui);
			_mgr.RegisterUi(plugin, kind, (prm.Get("spec") as PyDict) ?? new PyDict());
			return null;
		}
		case "ui.unregister":
			Need(plugin, PluginPermissions.Ui);
			_mgr.UnregisterUi(plugin, Py.S(prm.Get("kind")), Py.S(prm.Get("id")));
			return null;
		case "ui.toast":
			Need(plugin, PluginPermissions.Ui);
			_mgr.UiRequest(plugin, "toast", PyDict.Of("text", Py.S(prm.Get("text")), "toastKind", Py.S(Py.Or(prm.Get("kind"), "info"))), wait: false);
			return null;
		case "ui.confirm":
			Need(plugin, PluginPermissions.Ui);
			return Py.Truthy(_mgr.UiRequest(plugin, "confirm", PyDict.Of("title", Py.S(prm.Get("title")), "text", Py.S(prm.Get("text")), "ok", Py.S(prm.Get("ok")), "cancel", Py.S(prm.Get("cancel"))), wait: true, false));
		case "ui.prompt":
		{
			Need(plugin, PluginPermissions.Ui);
			object v2 = _mgr.UiRequest(plugin, "prompt", PyDict.Of("title", Py.S(prm.Get("title")), "text", Py.S(prm.Get("text")), "placeholder", Py.S(prm.Get("placeholder")), "default", Py.S(prm.Get("default"))), wait: true);
			return (v2 == null) ? null : Py.Str(v2);
		}
		case "ui.openPage":
		{
			Need(plugin, PluginPermissions.Ui);
			string page = Py.S(prm.Get("page"));
			string target = (_mgr.FindUi(plugin, "page", page) != null) ? ("plugin:" + plugin + "/" + page) : page;
			_mgr.UiRequest(plugin, "openPage", PyDict.Of("page", page, "target", target), wait: false);
			return null;
		}
		case "ui.overlayUpdate":
			Need(plugin, PluginPermissions.UiOverlay);
			_mgr.UpdateOverlay(plugin, Py.S(prm.Get("id")), prm);
			return null;
		case "ui.openExternal":
		{
			Need(plugin, PluginPermissions.Ui);
			string url = Py.S(prm.Get("url"));
			if (!Uri.TryCreate(url, UriKind.Absolute, out Uri u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
			{
				throw new PluginRpcException("invalid_params", "只允许打开 http/https 地址");
			}
			_mgr.UiRequest(plugin, "openExternal", PyDict.Of("url", u.AbsoluteUri), wait: false);
			return null;
		}
		case "hooks.register":
		{
			string hook = Py.S(prm.Get("hook"));
			Need(plugin, HookPermission(hook));
			if (host == null)
			{
				throw new PluginRpcException("unsupported", ".NET 插件请用 RegisterHook");
			}
			PluginHooks.Register(plugin, hook, Py.IntOr(prm.Get("priority"), 0L), NodeHookHandler(host, plugin, hook));
			return null;
		}
		case "hooks.unregister":
			PluginHooks.Unregister(plugin, Py.S(prm.Get("hook")));
			return null;
		case "plugin.reload":
			ThreadPool.QueueUserWorkItem(delegate
			{
				try
				{
					_mgr.Reload(plugin);
				}
				catch (Exception ex)
				{
					_mgr.Log(plugin, "error", "重载失败：" + ex.Message);
				}
			});
			return null;
		case "plugin.info":
			return _mgr.InfoRow(p);
		default:
			throw new PluginRpcException("method_not_found", "未知方法：" + method);
		}
	}

	public static string HookPermission(string hook)
	{
		if (!PluginHooks.IsKnown(hook))
		{
			throw new PluginRpcException("invalid_params", "未知钩子：" + hook);
		}
		if (hook.StartsWith("ai.tool.", StringComparison.Ordinal))
		{
			return PluginPermissions.AiTools;
		}
		if (hook == "ai.turn.before")
		{
			return PluginPermissions.AiSessions;
		}
		return PluginPermissions.Launcher;
	}

	private static List<string> Topics(PyDict prm)
	{
		object t = prm.Get("topics") ?? prm.Get("topic");
		if (t is string s)
		{
			return new List<string> { s };
		}
		return Py.ListOr(t).Select(Py.S).Where((string x) => x.Length > 0).ToList();
	}

	public static PluginToolEntry ToolEntry(string plugin, PyDict tool)
	{
		string name = Py.S(tool.Get("name"));
		return new PluginToolEntry
		{
			Plugin = plugin,
			Name = name,
			Description = Py.S(tool.Get("description")),
			Parameters = PluginTools.NormalizeParameters(tool.Get("parameters") ?? tool.Get("inputSchema")),
			TimeoutMs = tool.ContainsKey("timeoutMs") ? Math.Max(0L, Py.IntOr(tool.Get("timeoutMs"), PluginTools.DefaultTimeoutMs)) : PluginTools.DefaultTimeoutMs,
			Label = Py.S(tool.Get("label")),
			PresetOnly = Py.Truthy(tool.Get("presetOnly"))
		};
	}

	/// <summary>Node 工具：发 tool.execute 请求；取消时发 tool.cancel 通知。</summary>
	private static Func<PyDict, CancellationToken, object> NodeToolExecutor(NodeHostProcess host)
	{
		return delegate(PyDict prm, CancellationToken ct)
		{
			string callId = Py.S(prm.Get("callId"));
			using (ct.Register(delegate
			{
				try
				{
					host.Peer.Notify("tool.cancel", PyDict.Of("callId", callId, "plugin", prm.Get("plugin")));
				}
				catch (Exception)
				{
				}
			}))
			{
				return host.Peer.Request("tool.execute", prm, null, ct);
			}
		};
	}

	private static Func<PyDict, CancellationToken, object> NodeHookHandler(NodeHostProcess host, string plugin, string hook)
	{
		return (PyDict data, CancellationToken ct) => host.Peer.Request("hook.invoke", PyDict.Of("plugin", plugin, "hook", hook, "data", data), PluginHooks.Timeout, ct);
	}

	/// <summary>
	/// launcher.call：调用 BackendApi 的 RPC 方法。args（位置参数）和 kwargs 可以同时给：位置参数按形参顺序变成关键字参数再合并。
	/// 不允许调用 plugins_* 等管理方法。
	/// </summary>
	public object LauncherCall(string method, List<object> args, PyDict kwargs)
	{
		if (string.IsNullOrEmpty(method))
		{
			throw new PluginRpcException("invalid_params", "缺少 method");
		}
		if (method.StartsWith("plugins_", StringComparison.Ordinal) || LauncherDeny.Contains(method))
		{
			throw new PluginRpcException("permission_denied", "插件不能调用 " + method);
		}
		BackendApi b = _mgr.Backend ?? throw new PluginRpcException("unavailable", "启动器内核未就绪");
		if (_dispatcher == null)
		{
			_dispatcher = new RpcDispatcher(b);
		}
		if (!_dispatcher.Has(method))
		{
			throw new PluginRpcException("method_not_found", "BackendApi 没有方法：" + method);
		}
		object prm;
		if (args != null && args.Count > 0 && kwargs != null && kwargs.Count > 0)
		{
			prm = MergeArgs(method, args, kwargs);
		}
		else if (args != null && args.Count > 0)
		{
			prm = args;
		}
		else
		{
			prm = kwargs ?? new PyDict();
		}
		return _dispatcher.Call(method, prm);
	}

	/// <summary>位置参数按形参名转成关键字参数（跳过 [VarArgs]/[VarKw]），和 kwargs 合并（kwargs 优先）。</summary>
	public static PyDict MergeArgs(string method, List<object> args, PyDict kwargs)
	{
		MethodInfo mi = typeof(BackendApi).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public).FirstOrDefault((MethodInfo m) => m.Name == method);
		if (mi == null)
		{
			throw new PluginRpcException("method_not_found", "BackendApi 没有方法：" + method);
		}
		List<string> names = mi.GetParameters().Where((ParameterInfo x) => x.GetCustomAttribute<VarKwAttribute>() == null && x.GetCustomAttribute<VarArgsAttribute>() == null).Select((ParameterInfo x) => x.Name).ToList();
		if (args.Count > names.Count)
		{
			throw new PluginRpcException("invalid_params", method + " 位置参数太多");
		}
		PyDict r = new PyDict();
		for (int i = 0; i < args.Count; i++)
		{
			r[names[i]] = args[i];
		}
		foreach (KeyValuePair<string, object> kv in kwargs)
		{
			r[kv.Key] = kv.Value;
		}
		return r;
	}
}
