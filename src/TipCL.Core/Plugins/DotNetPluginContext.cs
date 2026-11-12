using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using TipCL.Plugins;

namespace TipCL.Core.Plugins;

/// <summary>加载 .NET 插件（DESIGN §3.2）：Assembly.LoadFrom → 实例化 entryType → Apply(ctx)。卸载要重启才真正生效。</summary>
internal static class DotNetPluginLoader
{
	public static object Load(PluginManager mgr, PluginInstance p)
	{
		string asmPath = Path.GetFullPath(Path.Combine(p.Dir, p.Manifest.Assembly));
		if (!asmPath.StartsWith(Path.GetFullPath(p.Dir), StringComparison.OrdinalIgnoreCase) || !File.Exists(asmPath))
		{
			throw new ValueError("找不到程序集：" + p.Manifest.Assembly);
		}
		Assembly asm = Assembly.LoadFrom(asmPath);
		Type t = asm.GetType(p.Manifest.EntryType, throwOnError: true);
		if (!typeof(ITipCLPlugin).IsAssignableFrom(t))
		{
			throw new ValueError(p.Manifest.EntryType + " 没有实现 TipCL.Plugins.ITipCLPlugin");
		}
		ITipCLPlugin plugin = (ITipCLPlugin)Activator.CreateInstance(t);
		DotNetPluginContext ctx = new DotNetPluginContext(mgr, p);
		EnsureEventSink(mgr);
		try
		{
			plugin.Apply(ctx);
		}
		catch (Exception)
		{
			ctx.RunEffectsReverse();
			throw;
		}
		return ctx;
	}

	public static void Unload(PluginManager mgr, PluginInstance p)
	{
		if (p.DotNetContext is DotNetPluginContext ctx)
		{
			ctx.RunEffectsReverse();
		}
		PluginUiRegistry.UnregisterPlugin(p.Id);
	}

	private static void EnsureEventSink(PluginManager mgr)
	{
		if (mgr.DotNetEventSink != null)
		{
			return;
		}
		PluginUiRegistry.NativeChangedHook = mgr.EmitUiChanged;
		mgr.DotNetEventSink = delegate(string plugin, string topic, object data)
		{
			if (mgr.Get(plugin)?.DotNetContext is DotNetPluginContext c)
			{
				c.Dispatch(topic, data);
			}
		};
	}
}

/// <summary>.NET 插件拿到的上下文。大部分能力直接转到 <see cref="PluginHostApi.Invoke"/>（同一套权限检查）。</summary>
internal sealed class DotNetPluginContext : IPluginContext, IPluginLogger, IPluginLlm, IPluginCredentials, IPluginSessions, IPluginConfig, IPluginUi
{
	private readonly PluginManager _mgr;

	private readonly PluginInstance _p;

	private readonly Stack<(Action undo, string label)> _effects = new Stack<(Action, string)>();

	private readonly List<(string pattern, Action<string, object> handler)> _handlers = new List<(string, Action<string, object>)>();

	private readonly List<Action<string, object>> _configHandlers = new List<Action<string, object>>();

	public DotNetPluginContext(PluginManager mgr, PluginInstance p)
	{
		_mgr = mgr;
		_p = p;
		Paths = new PluginPaths
		{
			Plugin = p.Dir,
			Data = mgr.Dirs.DataDir(p.Id),
			Cache = mgr.Dirs.CacheDir(p.Id),
			Logs = mgr.Dirs.LogsRoot
		};
		Directory.CreateDirectory(Paths.Data);
	}

	public string Id => _p.Id;

	public PluginPaths Paths { get; }

	public IPluginLogger Logger => this;

	public IPluginLlm Llm => this;

	public IPluginCredentials Credentials => this;

	public IPluginSessions Sessions => this;

	public IPluginConfig Config => this;

	public IPluginUi Ui => this;

	private object Call(string method, PyDict prm)
	{
		prm = prm ?? new PyDict();
		prm["plugin"] = _p.Id;
		return _mgr.Api.Invoke(_p.Id, method, prm);
	}

	// ── 副作用 ──

	public void Effect(Func<Action> setup, string label = null)
	{
		Action undo = setup?.Invoke();
		if (undo != null)
		{
			lock (_effects)
			{
				_effects.Push((undo, label));
			}
		}
	}

	private IDisposable Track(Action undo, string label)
	{
		Disposer d = new Disposer(undo);
		lock (_effects)
		{
			_effects.Push((d.Dispose, label));
		}
		return d;
	}

	public void RunEffectsReverse()
	{
		while (true)
		{
			(Action undo, string label) e;
			lock (_effects)
			{
				if (_effects.Count == 0)
				{
					break;
				}
				e = _effects.Pop();
			}
			try
			{
				e.undo();
			}
			catch (Exception ex)
			{
				_mgr.Log(_p.Id, "warn", "撤销 " + (e.label ?? "副作用") + " 出错：" + ex.Message);
			}
		}
		lock (_handlers)
		{
			_handlers.Clear();
		}
	}

	// ── 启动器 / 事件 ──

	public object Launcher(string method, IDictionary<string, object> kwargs = null)
	{
		return Call("launcher.call", PyDict.Of("method", method, "kwargs", (kwargs == null) ? new PyDict() : (Py.Box(kwargs) as PyDict ?? new PyDict())));
	}

	public IDisposable OnEvent(string topic, Action<string, object> handler)
	{
		_mgr.Subscribe(_p.Id, new[] { topic });
		(string, Action<string, object>) entry = (topic, handler);
		lock (_handlers)
		{
			_handlers.Add(entry);
		}
		return Track(delegate
		{
			lock (_handlers)
			{
				_handlers.Remove(entry);
				if (!_handlers.Any(((string pattern, Action<string, object> handler) h) => h.pattern == topic))
				{
					_mgr.Unsubscribe(_p.Id, new[] { topic });
				}
			}
		}, "event " + topic);
	}

	public void Dispatch(string topic, object data)
	{
		List<(string pattern, Action<string, object> handler)> hs;
		lock (_handlers)
		{
			hs = _handlers.ToList();
		}
		foreach ((string pattern, Action<string, object> handler) in hs)
		{
			if (AgentPresets.GlobMatch(pattern, topic))
			{
				try
				{
					handler(topic, data);
				}
				catch (Exception ex)
				{
					_mgr.Log(_p.Id, "error", "事件处理出错（" + topic + "）：" + ex.Message);
				}
			}
		}
		if (topic == "config.changed" && data is PyDict d)
		{
			List<Action<string, object>> ch;
			lock (_configHandlers)
			{
				ch = _configHandlers.ToList();
			}
			foreach (Action<string, object> h in ch)
			{
				try
				{
					h(Py.S(d.Get("key")), d.Get("value"));
				}
				catch (Exception)
				{
				}
			}
		}
	}

	// ── 工具 / 预设 / 钩子 ──

	public IDisposable RegisterTool(PluginToolSpec spec, Func<PluginToolCall, CancellationToken, PluginToolResult> execute)
	{
		_mgr.RequirePermission(_p.Id, PluginPermissions.AiTools);
		PluginToolEntry e = new PluginToolEntry
		{
			Plugin = _p.Id,
			Name = spec.Name,
			Description = spec.Description ?? "",
			Parameters = PluginTools.NormalizeParameters(Py.Box(spec.Parameters)),
			TimeoutMs = spec.TimeoutMs,
			Label = spec.Label ?? "",
			PresetOnly = spec.PresetOnly,
			Executor = delegate(PyDict prm, CancellationToken ct)
			{
				PluginToolResult r = execute(new PluginToolCall
				{
					CallId = Py.S(prm.Get("callId")),
					ChatId = Py.S(prm.Get("chatId")),
					Name = Py.S(prm.Get("name")),
					Args = (prm.Get("args") as PyDict) ?? new PyDict(),
					MessageText = Py.S(prm.Get("messageText"))
				}, ct);
				return ToResultDict(r);
			}
		};
		PluginTools.Register(e);
		return Track(delegate
		{
			PluginTools.Unregister(_p.Id, spec.Name);
		}, "tool " + spec.Name);
	}

	private PyDict ToResultDict(PluginToolResult r)
	{
		List<object> content = new List<object>();
		foreach (PluginContentPart part in r?.Content ?? new List<PluginContentPart>())
		{
			if (part.Type == "image")
			{
				string id = part.AttachmentId;
				if (string.IsNullOrEmpty(id) && part.Data != null)
				{
					id = Py.S(PluginAttachments.Save(_p.Id, part.Data, part.MediaType, null)["id"]);
				}
				content.Add(PyDict.Of("type", "image", "attachment", PyDict.Of("id", id), "mediaType", part.MediaType ?? ""));
			}
			else
			{
				content.Add(PyDict.Of("type", "text", "text", part.Text ?? ""));
			}
		}
		return PyDict.Of("content", content, "isError", r != null && r.IsError);
	}

	public IDisposable RegisterPreset(AgentPresetSpec spec)
	{
		PyDict d = PyDict.Of("id", spec.Id, "name", spec.Name, "description", spec.Description, "order", spec.Order, "systemPrompt", spec.SystemPrompt, "replaceSystemPrompt", spec.ReplaceSystemPrompt, "includeRuntimeContext", spec.IncludeRuntimeContext, "tools", PyDict.Of("include", spec.ToolInclude.Cast<object>().ToList(), "exclude", spec.ToolExclude.Cast<object>().ToList(), "builtin", spec.Builtin), "view", spec.View ?? "");
		if (spec.MaxRounds >= 0)
		{
			d["maxRounds"] = spec.MaxRounds;
		}
		Call("presets.register", PyDict.Of("preset", d));
		return Track(delegate
		{
			AgentPresets.Unregister(_p.Id, spec.Id);
		}, "preset " + spec.Id);
	}

	public IDisposable RegisterHook(string hook, Func<object, object> handler)
	{
		_mgr.RequirePermission(_p.Id, PluginHostApi.HookPermission(hook));
		PluginHooks.Register(_p.Id, hook, 0L, (PyDict data, CancellationToken ct) => handler(data));
		return Track(delegate
		{
			PluginHooks.Unregister(_p.Id, hook);
		}, "hook " + hook);
	}

	public string SaveImage(byte[] data, string mediaType, string name = null)
	{
		_mgr.RequirePermission(_p.Id, PluginPermissions.AiTools);
		return Py.S(PluginAttachments.Save(_p.Id, data, mediaType, name)["id"]);
	}

	public void Provide(string name, object service)
	{
		_mgr.Provide(name, service);
		Track(delegate
		{
			_mgr.Provide(name, null);
		}, "service " + name);
	}

	public object Get(string name)
	{
		return _mgr.GetService(name);
	}

	// ── 日志 ──

	public void Debug(string text)
	{
		_mgr.Log(_p.Id, "debug", text);
	}

	public void Info(string text)
	{
		_mgr.Log(_p.Id, "info", text);
	}

	public void Warn(string text)
	{
		_mgr.Log(_p.Id, "warn", text);
	}

	public void Error(string text)
	{
		_mgr.Log(_p.Id, "error", text);
	}

	// ── LLM ──

	public List<object> Providers()
	{
		return (List<object>)Call("llm.providers", null);
	}

	public List<object> Models(string provider)
	{
		return (List<object>)Call("llm.models", PyDict.Of("provider", provider));
	}

	public PyDict Stream(object request, Action<PyDict> onChunk, CancellationToken token = default(CancellationToken))
	{
		_mgr.RequirePermission(_p.Id, PluginPermissions.AiLlm);
		PyDict req = (Py.Box(request) as PyDict)?.Copy() ?? new PyDict();
		string rid = Py.S(req.Get("requestId"));
		if (rid.Length == 0)
		{
			rid = Guid.NewGuid().ToString("N");
			req["requestId"] = rid;
		}
		using (token.Register(delegate
		{
			_mgr.Llm.Abort(_p.Id, rid);
		}))
		{
			return _mgr.Llm.Stream(_p.Id, req, onChunk);
		}
	}

	// ── 凭据 ──

	public string Resolve(string name)
	{
		return (Call("credentials.resolve", PyDict.Of("name", name)) as PyDict)?.Get("value") as string;
	}

	public void Set(string name, string value)
	{
		Call("credentials.set", PyDict.Of("name", name, "value", value));
	}

	public PyDict Status()
	{
		return (PyDict)Call("credentials.status", null);
	}

	// ── 会话 ──

	public string Create(string preset = null, string title = null, string view = null, string prompt = null, bool open = true)
	{
		return Py.S(((PyDict)Call("ai.chats.create", PyDict.Of("preset", preset ?? "", "title", title ?? "", "view", view ?? "", "prompt", prompt ?? "", "open", open)))["chatId"]);
	}

	public List<object> List()
	{
		return (List<object>)Call("ai.chats.list", null);
	}

	public PyDict Events(string chatId, long from = 0L)
	{
		return (PyDict)Call("ai.chats.events", PyDict.Of("chatId", chatId, "from", from));
	}

	public PyDict Send(string chatId, string text, string mode = "auto")
	{
		return (PyDict)Call("ai.chats.send", PyDict.Of("chatId", chatId, "text", text, "mode", mode));
	}

	public void Cancel(string chatId)
	{
		Call("ai.chats.cancel", PyDict.Of("chatId", chatId));
	}

	public void Open(string chatId, string view = null)
	{
		Call("ai.chats.open", PyDict.Of("chatId", chatId, "view", view ?? ""));
	}

	public PyDict State(string chatId)
	{
		return (PyDict)Call("ai.chats.state", PyDict.Of("chatId", chatId));
	}

	// ── 配置 ──

	PyDict IPluginConfig.Get()
	{
		return (PyDict)Call("config.get", null);
	}

	object IPluginConfig.Get(string key)
	{
		return ((PyDict)Call("config.get", null)).Get(key);
	}

	void IPluginConfig.Set(string key, object value)
	{
		Call("config.set", PyDict.Of("key", key, "value", value));
	}

	public IDisposable OnChange(Action<string, object> handler)
	{
		lock (_configHandlers)
		{
			_configHandlers.Add(handler);
		}
		return Track(delegate
		{
			lock (_configHandlers)
			{
				_configHandlers.Remove(handler);
			}
		}, "config.onChange");
	}

	// ── 界面 ──

	public IDisposable RegisterPage(PluginPageSpec spec, Func<object> factory)
	{
		_mgr.RequirePermission(_p.Id, PluginPermissions.Ui);
		string icon = spec.Icon ?? "";
		if (icon.Length > 2 && PluginManifest.IsSafeRelPath(icon))
		{
			icon = Path.Combine(_p.Dir, icon);
		}
		IDisposable d = PluginUiRegistry.Register(new NativeUiEntry
		{
			Plugin = _p.Id,
			Kind = "page",
			Id = spec.Id,
			Label = spec.Label,
			Icon = icon,
			Order = spec.Order,
			Group = spec.Group ?? "more",
			Factory = factory
		});
		return Track(d.Dispose, "page " + spec.Id);
	}

	public IDisposable RegisterDashCard(PluginDashCardSpec spec, Func<object> factory)
	{
		_mgr.RequirePermission(_p.Id, PluginPermissions.Ui);
		IDisposable d = PluginUiRegistry.Register(new NativeUiEntry
		{
			Plugin = _p.Id,
			Kind = "dashCard",
			Id = spec.Id,
			Label = spec.Label,
			DefaultW = spec.DefaultW,
			DefaultH = spec.DefaultH,
			Factory = factory
		});
		return Track(d.Dispose, "dashCard " + spec.Id);
	}

	public IDisposable RegisterAiView(PluginAiViewSpec spec, Func<string, object> factory)
	{
		_mgr.RequirePermission(_p.Id, PluginPermissions.Ui);
		List<string> presets = (spec.Presets ?? new List<string>()).Select((string x) => x.StartsWith("plugin:", StringComparison.Ordinal) ? x : AgentPreset.MakeFullId(_p.Id, x)).ToList();
		IDisposable d = PluginUiRegistry.Register(new NativeUiEntry
		{
			Plugin = _p.Id,
			Kind = "aiView",
			Id = spec.Id,
			Label = spec.Label,
			Order = spec.Order,
			Presets = presets,
			AiViewFactory = factory
		});
		return Track(d.Dispose, "aiView " + spec.Id);
	}

	public void Toast(string text, string kind = "info")
	{
		Call("ui.toast", PyDict.Of("text", text, "kind", kind));
	}

	public bool Confirm(string title, string text, string ok = null, string cancel = null)
	{
		return Py.Truthy(Call("ui.confirm", PyDict.Of("title", title, "text", text, "ok", ok ?? "", "cancel", cancel ?? "")));
	}

	public string Prompt(string title, string text, string placeholder = null, string @default = null)
	{
		return Call("ui.prompt", PyDict.Of("title", title, "text", text, "placeholder", placeholder ?? "", "default", @default ?? "")) as string;
	}

	public void OpenPage(string page)
	{
		Call("ui.openPage", PyDict.Of("page", page));
	}

	public void OpenExternal(string url)
	{
		Call("ui.openExternal", PyDict.Of("url", url));
	}

	private sealed class Disposer : IDisposable
	{
		private Action _a;

		public Disposer(Action a)
		{
			_a = a;
		}

		public void Dispose()
		{
			Interlocked.Exchange(ref _a, null)?.Invoke();
		}
	}
}
