using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TipCL.Core.Launcher.Ai;

namespace TipCL.Core.Plugins;

/// <summary>
/// 插件 LLM 服务（DESIGN §6）。供应商 <c>tipcl</c> = 用户当前 AI 设置；额外供应商来自 plugins.json.providers（OpenAI 兼容）。
/// 每次 stream 独立请求，可以并发，和 AI 回合互不阻塞；按 requestId 中止。
/// </summary>
public sealed class PluginLlm
{
	private readonly PluginManager _mgr;

	private readonly ConcurrentDictionary<string, HttpCancel> _active = new ConcurrentDictionary<string, HttpCancel>();

	public PluginLlm(PluginManager mgr)
	{
		_mgr = mgr;
	}

	public List<object> Providers()
	{
		List<object> r = new List<object> { PyDict.Of("id", "tipcl", "name", "TipCL（当前 AI 设置）") };
		foreach (ProviderRecord p in _mgr.State.Providers())
		{
			r.Add(PyDict.Of("id", p.Id, "name", string.IsNullOrEmpty(p.Name) ? p.Id : p.Name));
		}
		return r;
	}

	/// <summary>给某个供应商拼出 Client 能用的设置。</summary>
	public PyDict SettingsFor(string provider, string model)
	{
		provider = string.IsNullOrEmpty(provider) ? "tipcl" : provider;
		if (provider == "tipcl")
		{
			PyDict s = (_mgr.Backend != null) ? _mgr.Backend._with_stored_secrets(null) : new PyDict();
			if (!string.IsNullOrEmpty(model))
			{
				s["ai_model"] = model;
			}
			return s;
		}
		ProviderRecord p = _mgr.State.Providers().FirstOrDefault((ProviderRecord x) => x.Id == provider) ?? throw new PluginRpcException("not_found", "没有这个模型供应商：" + provider);
		string key = _mgr.HostSecrets.Get(string.IsNullOrEmpty(p.ApiKeyRef) ? ("provider:" + p.Id) : p.ApiKeyRef) ?? "";
		if (key.Length == 0)
		{
			throw new PluginRpcException("not_configured", "模型供应商 " + p.Id + " 还没有填 API Key");
		}
		return PyDict.Of("ai_mode", "custom", "ai_base_url", p.BaseUrl, "ai_api_key", key, "ai_model", string.IsNullOrEmpty(model) ? "" : model);
	}

	public List<object> Models(string provider)
	{
		List<object> ids;
		try
		{
			ids = Client.ListModels(SettingsFor(provider, null));
		}
		catch (PluginRpcException)
		{
			throw;
		}
		catch (Exception ex)
		{
			throw new PluginRpcException("provider_error", "拉取模型列表失败：" + ((ex is PyException) ? Py.ExcStr(ex) : ex.Message));
		}
		return ids.Select((object x) => (object)PyDict.Of("id", Py.Str(x), "name", Py.Str(x))).ToList();
	}

	public void Abort(string plugin, string requestId)
	{
		if (_active.TryGetValue(plugin + "\n" + requestId, out HttpCancel hc))
		{
			hc.Abort();
		}
	}

	public void AbortAll(string plugin)
	{
		foreach (KeyValuePair<string, HttpCancel> kv in _active)
		{
			if (plugin == null || kv.Key.StartsWith(plugin + "\n", StringComparison.Ordinal))
			{
				kv.Value.Abort();
			}
		}
	}

	/// <summary>
	/// 流式调用。onChunk 收到 DESIGN §4 llm.chunk 的 chunk：text-delta / reasoning-delta / tool-call / finish。
	/// 返回 {text, reasoning, finish:{kind}, usage?, toolCalls?}。中止时 finish.kind = aborted（不抛错）。
	/// </summary>
	public PyDict Stream(string plugin, PyDict req, Action<PyDict> onChunk)
	{
		string requestId = Py.S(req.Get("requestId"));
		if (requestId.Length == 0)
		{
			requestId = Guid.NewGuid().ToString("N");
		}
		string key = plugin + "\n" + requestId;
		HttpCancel hc = new HttpCancel();
		if (!_active.TryAdd(key, hc))
		{
			throw new PluginRpcException("invalid_params", "requestId 重复：" + requestId);
		}
		try
		{
			PyDict settings = SettingsFor(Py.S(req.Get("provider")), Py.S(req.Get("model")));
			if (req.Get("maxTokens") != null)
			{
				settings["ai_max_tokens"] = Math.Max(1L, Py.IntOr(req.Get("maxTokens"), 8192L));
			}
			double temperature = (req.Get("temperature") != null) ? Py.Float(req.Get("temperature")) : 0.7;
			PyDict extra = ReasoningExtra(Py.S(req.Get("reasoningEffort")));
			List<object> messages = ToOpenAiMessages(Py.S(req.Get("system")), Py.ListOr(req.Get("messages")));
			List<object> tools = ToOpenAiTools(req.Get("tools"));
			try
			{
				if (extra != null)
				{
					settings["ai_extra_body"] = extra;
				}
				return RunOnce(settings, messages, tools, temperature, hc, onChunk, out _);
			}
			catch (AIClientError ex) when (extra != null && ex.Status == 400 && !hc.Cancelled())
			{
				// 端点不认识思考开关参数：去掉再试一次
				settings.Remove("ai_extra_body");
				return RunOnce(settings, messages, tools, temperature, hc, onChunk, out _);
			}
		}
		catch (AIClientError ex2)
		{
			if (hc.Cancelled())
			{
				return Finish(onChunk, "", "", "aborted", null, null);
			}
			PyDict fin = Finish(onChunk, "", "", "error", null, null);
			fin["error"] = ex2.Message;
			((PyDict)fin["finish"])["error"] = ex2.Message;
			return fin;
		}
		finally
		{
			_active.TryRemove(key, out _);
		}
	}

	private static PyDict Finish(Action<PyDict> onChunk, string text, string reasoning, string kind, PyDict usage, List<object> toolCalls)
	{
		PyDict reason = PyDict.Of("kind", kind);
		PyDict chunk = PyDict.Of("type", "finish", "reason", reason);
		if (usage != null)
		{
			chunk["usage"] = usage;
		}
		try
		{
			onChunk?.Invoke(chunk);
		}
		catch (Exception)
		{
		}
		PyDict r = PyDict.Of("text", text, "reasoning", reasoning, "finish", reason);
		if (usage != null)
		{
			r["usage"] = usage;
		}
		if (toolCalls != null && toolCalls.Count > 0)
		{
			r["toolCalls"] = toolCalls;
		}
		return r;
	}

	private static PyDict RunOnce(PyDict settings, List<object> messages, List<object> tools, double temperature, HttpCancel hc, Action<PyDict> onChunk, out bool any)
	{
		any = false;
		StringBuilder text = new StringBuilder();
		StringBuilder reasoning = new StringBuilder();
		PyDict usage = null;
		List<object> calls = new List<object>();
		string kind = "stop";
		ChatEventLog.ThinkSplitter splitter = new ChatEventLog.ThinkSplitter();
		bool gotAny = false;
		foreach (PyDict ev in Client.ChatStream(settings, messages, tools, temperature, hc))
		{
			if (hc.Cancelled())
			{
				kind = "aborted";
				break;
			}
			string type = Py.S(ev.Get("type"));
			if (type == "delta")
			{
				gotAny = true;
				splitter.Feed(Py.S(ev.Get("text")), delegate(string t, string s)
				{
					if (t == "assistant/reasoning")
					{
						reasoning.Append(s);
						Emit(PyDict.Of("type", "reasoning-delta", "text", s));
					}
					else
					{
						text.Append(s);
						Emit(PyDict.Of("type", "text-delta", "text", s));
					}
				});
			}
			else if (type == "tool_calls")
			{
				gotAny = true;
				foreach (PyDict tc in Py.ListOr(ev.Get("tool_calls")).OfType<PyDict>())
				{
					PyDict fn = (tc.Get("function") as PyDict) ?? new PyDict();
					object args = PyJson.LoadsOr(Py.S(fn.Get("arguments")), Py.S(fn.Get("arguments")));
					PyDict call = PyDict.Of("type", "tool-call", "id", Py.S(tc.Get("id")), "name", Py.S(fn.Get("name")), "args", args);
					calls.Add(call);
					Emit(call);
				}
				kind = "tool-calls";
				break;
			}
			else if (type == "usage")
			{
				usage = ev.Get("usage") as PyDict;
			}
			else if (type == "done")
			{
				kind = Py.Eq(ev.Get("finish_reason"), "length") ? "length" : "stop";
				break;
			}
			else if (type == "error")
			{
				if (hc.Cancelled())
				{
					kind = "aborted";
					break;
				}
				throw new AIClientError(Py.S(ev.Get("message")));
			}
		}
		any = gotAny;
		if (hc.Cancelled())
		{
			kind = "aborted";
		}
		return Finish(onChunk, text.ToString(), reasoning.ToString(), kind, usage, calls);
		void Emit(PyDict c)
		{
			try
			{
				onChunk?.Invoke(c);
			}
			catch (Exception)
			{
			}
		}
	}

	/// <summary>reasoningEffort → 额外请求字段。off：关思考（DeepSeek/豆包式 thinking.type=disabled）；low/medium/high：reasoning_effort。</summary>
	public static PyDict ReasoningExtra(string effort)
	{
		switch ((effort ?? "").Trim().ToLowerInvariant())
		{
		case "":
		case "default":
		case "auto":
			return null;
		case "off":
		case "none":
		case "disabled":
			return PyDict.Of("thinking", PyDict.Of("type", "disabled"));
		case "minimal":
		case "low":
		case "medium":
		case "high":
			return PyDict.Of("reasoning_effort", effort.Trim().ToLowerInvariant());
		default:
			return null;
		}
	}

	/// <summary>
	/// DSH / AI SDK 风格消息 → OpenAI chat 消息。content 可以是字符串或片段数组：
	/// text、image（data base64 + mediaType，或 url）、tool-call（assistant）、tool-result（role:tool）。
	/// </summary>
	public static List<object> ToOpenAiMessages(string system, List<object> msgs)
	{
		List<object> r = new List<object>();
		if (!string.IsNullOrEmpty(system))
		{
			r.Add(PyDict.Of("role", "system", "content", system));
		}
		foreach (PyDict m in msgs.OfType<PyDict>())
		{
			string role = Py.S(m.Get("role"));
			if (role.Length == 0)
			{
				role = "user";
			}
			object content = m.Get("content");
			if (!(content is List<object> parts))
			{
				PyDict simple = PyDict.Of("role", role, "content", (content is string cs) ? cs : ((content == null) ? "" : PyJson.Dumps(content, null, ensureAscii: false)));
				if (role == "tool")
				{
					simple["tool_call_id"] = Py.S(Py.Or(m.Get("toolCallId"), m.Get("tool_call_id")));
				}
				if (m.Get("tool_calls") is List<object> rawCalls)
				{
					simple["tool_calls"] = rawCalls;
				}
				r.Add(simple);
				continue;
			}
			List<object> oaParts = new List<object>();
			List<object> toolCalls = new List<object>();
			StringBuilder plain = new StringBuilder();
			bool hasImage = false;
			foreach (object p0 in parts)
			{
				if (p0 is string ps)
				{
					oaParts.Add(PyDict.Of("type", "text", "text", ps));
					plain.Append(ps);
					continue;
				}
				if (!(p0 is PyDict p))
				{
					continue;
				}
				string t = Py.S(p.Get("type"));
				if (t == "text")
				{
					oaParts.Add(PyDict.Of("type", "text", "text", Py.S(p.Get("text"))));
					plain.Append(Py.S(p.Get("text")));
				}
				else if (t == "image" || t == "image_url")
				{
					string url = Py.S(p.Get("url"));
					if (p.Get("image_url") is PyDict iu)
					{
						url = Py.S(iu.Get("url"));
					}
					if (url.Length == 0 && p.Get("data") is string b64)
					{
						url = b64.StartsWith("data:", StringComparison.Ordinal) ? b64 : ("data:" + Py.S(Py.Or(p.Get("mediaType"), "image/png")) + ";base64," + b64);
					}
					if (url.Length == 0 && p.Get("attachment") is PyDict att)
					{
						string path = PluginAttachments.Resolve(Py.S(att.Get("id")));
						if (path != null)
						{
							url = Images.EncodeFile(path);
						}
					}
					if (url.Length > 0)
					{
						oaParts.Add(Images.MakeBlock(url));
						hasImage = true;
					}
				}
				else if (t == "tool-call")
				{
					object args = p.Get("args") ?? p.Get("input") ?? new PyDict();
					toolCalls.Add(PyDict.Of("id", Py.S(Py.Or(p.Get("id"), p.Get("toolCallId"))), "type", "function", "function", PyDict.Of("name", Py.S(Py.Or(p.Get("name"), p.Get("toolName"))), "arguments", (args is string a) ? a : PyJson.Dumps(args, null, ensureAscii: false))));
				}
				else if (t == "tool-result")
				{
					object output = p.Get("output") ?? p.Get("result") ?? p.Get("content");
					r.Add(PyDict.Of("role", "tool", "tool_call_id", Py.S(Py.Or(p.Get("toolCallId"), p.Get("id"))), "content", (output is string os) ? os : PyJson.Dumps(output, null, ensureAscii: false)));
				}
			}
			if (oaParts.Count == 0 && toolCalls.Count == 0)
			{
				continue;
			}
			PyDict om = PyDict.Of("role", role, "content", hasImage ? ((object)oaParts) : plain.ToString());
			if (toolCalls.Count > 0)
			{
				om["tool_calls"] = toolCalls;
				if (plain.Length == 0)
				{
					om["content"] = null;
				}
			}
			r.Add(om);
		}
		return r;
	}

	/// <summary>tools：OpenAI 格式数组原样；DSH 风格 {name, description, parameters} 数组或 {name: {...}} 对象转 OpenAI 格式。</summary>
	public static List<object> ToOpenAiTools(object tools)
	{
		if (tools == null)
		{
			return null;
		}
		List<object> r = new List<object>();
		IEnumerable<PyDict> items;
		if (tools is PyDict map)
		{
			items = map.Select(delegate(KeyValuePair<string, object> kv)
			{
				PyDict d = (kv.Value as PyDict)?.Copy() ?? new PyDict();
				d.SetDefault("name", kv.Key);
				return d;
			});
		}
		else
		{
			items = Py.ListOr(tools).OfType<PyDict>();
		}
		foreach (PyDict t in items)
		{
			if (Py.Eq(t.Get("type"), "function") && t.Get("function") is PyDict)
			{
				r.Add(t);
				continue;
			}
			r.Add(PyDict.Of("type", "function", "function", PyDict.Of("name", Py.S(t.Get("name")), "description", Py.S(t.Get("description")), "parameters", PluginTools.NormalizeParameters(t.Get("parameters") ?? t.Get("inputSchema")))));
		}
		return (r.Count > 0) ? r : null;
	}
}
