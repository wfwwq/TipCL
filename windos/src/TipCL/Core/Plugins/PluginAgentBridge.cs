using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core.Launcher.Ai;

namespace TipCL.Core.Plugins;

/// <summary>
/// 一次 AI 回合里插件相关的状态（Agent.RunAgent 创建，回合线程上可见）：对话 id、预设、流式片段拆分、已发出的消息。
/// </summary>
public sealed class PluginTurn
{
	public string ChatId;

	public AgentPreset Preset;

	public string TurnId;

	public readonly ChatEventLog.ThinkSplitter Splitter = new ChatEventLog.ThinkSplitter();

	/// <summary>最后一条带工具调用的 assistant/message 的文字（回合结束时避免重复发同一句）。</summary>
	public string LastToolMessageText;

	/// <summary>本回合是否发过不带工具调用的 assistant/message。</summary>
	public bool FinalMessageSent;

	/// <summary>callId → 插件工具输出（图片要在工具结果后面作为视觉消息补给模型）。</summary>
	public readonly ConcurrentDictionary<string, PluginToolOutput> Outputs = new ConcurrentDictionary<string, PluginToolOutput>();

	/// <summary>已经发过 tool/result 事件的 callId。</summary>
	public readonly ConcurrentDictionary<string, bool> ResultSent = new ConcurrentDictionary<string, bool>();

	/// <summary>callId → 所在消息的文字（tool.execute 的 messageText）。</summary>
	public readonly ConcurrentDictionary<string, string> MessageTextOf = new ConcurrentDictionary<string, string>();

	public int VisionSeq;
}

/// <summary>
/// Agent 和插件系统之间的桥（DESIGN §5）。都是静态方法，没有插件时几乎零开销。
/// </summary>
public static class PluginAgentBridge
{
	/// <summary>保留最近几条工具画面（更早的视觉消息去掉图片，防止上下文爆掉）。</summary>
	public const int KeepVisionMessages = 2;

	public const string VisionIdPrefix = "plugin_vision_";

	[ThreadStatic]
	private static PluginTurn _current;

	/// <summary>当前线程正在跑的回合（子代理里为 null）。</summary>
	public static PluginTurn Current
	{
		get
		{
			return _current;
		}
		set
		{
			_current = value;
		}
	}

	public static AgentPreset PresetOf(PyDict settings)
	{
		string id = Py.S((settings ?? new PyDict()).Get("ai_preset"));
		return (id.Length == 0) ? null : AgentPresets.Get(id);
	}

	public static void Emit(PluginTurn t, string type, PyDict data)
	{
		if (t == null || string.IsNullOrEmpty(t.ChatId))
		{
			return;
		}
		ChatEventLog.Append(t.ChatId, type, data);
	}

	/// <summary>新一次模型请求开始：重置 &lt;think&gt; 拆分状态。</summary>
	public static void NewRequest(PluginTurn t)
	{
		if (t != null)
		{
			ChatEventLog.FlushDelta(t.ChatId);
			t.Splitter.Reset();
		}
	}

	/// <summary>模型流式片段 → assistant/delta / assistant/reasoning。</summary>
	public static void Delta(PluginTurn t, string piece)
	{
		if (t == null || string.IsNullOrEmpty(t.ChatId) || string.IsNullOrEmpty(piece))
		{
			return;
		}
		t.Splitter.Feed(piece, delegate(string type, string text)
		{
			ChatEventLog.AppendDelta(t.ChatId, type, text);
		});
	}

	/// <summary>
	/// 发 assistant/message（必须在执行工具之前）。calls：(id, name, args)。返回消息里的可见文字。
	/// </summary>
	public static string AssistantMessage(PluginTurn t, string rawText, IEnumerable<(string id, string name, PyDict args)> calls)
	{
		string visible = ChatEventLog.StripThink(rawText ?? "").Trim();
		if (t == null)
		{
			return visible;
		}
		List<object> content = new List<object>();
		if (visible.Length > 0)
		{
			content.Add(PyDict.Of("type", "text", "text", visible));
		}
		bool hasCalls = false;
		foreach ((string id, string name, PyDict args) in calls ?? Enumerable.Empty<(string, string, PyDict)>())
		{
			hasCalls = true;
			content.Add(PyDict.Of("type", "tool-call", "id", id, "name", name, "args", (args ?? new PyDict()).Copy()));
			t.MessageTextOf[id] = visible;
		}
		if (content.Count == 0)
		{
			return visible;
		}
		Emit(t, "assistant/message", PyDict.Of("message", PyDict.Of("role", "assistant", "content", content)));
		if (hasCalls)
		{
			t.LastToolMessageText = visible;
		}
		else
		{
			t.FinalMessageSent = true;
		}
		return visible;
	}

	/// <summary>回合结束时补发最后一句（不带工具调用）。</summary>
	public static void FinalMessage(PluginTurn t, string text)
	{
		if (t == null || t.FinalMessageSent)
		{
			return;
		}
		string visible = ChatEventLog.StripThink(text ?? "").Trim();
		if (visible.Length == 0 || visible == (t.LastToolMessageText ?? ""))
		{
			return;
		}
		AssistantMessage(t, visible, null);
	}

	/// <summary>工具列表：按预设过滤内置工具，再加插件工具。</summary>
	public static List<object> Tools(PluginTurn t, List<object> builtinSelected)
	{
		// 子代理（t == null）不带插件工具
		if (t == null)
		{
			return builtinSelected;
		}
		List<PluginToolEntry> plugin = PluginTools.All();
		AgentPreset preset = t.Preset;
		if (plugin.Count == 0 && preset == null)
		{
			return builtinSelected;
		}
		return AgentPresets.FilterTools(preset, builtinSelected, plugin.Select((PluginToolEntry e) => (e.Schema(), e.PresetOnly)));
	}

	public static long MaxRounds(PluginTurn t, long settingsValue)
	{
		return AgentPresets.EffectiveMaxRounds(t?.Preset, settingsValue);
	}

	public static bool IsPluginTool(string name)
	{
		return PluginTools.IsPluginTool(name);
	}

	/// <summary>ai.turn.before：返回 (新文字, 追加的上下文)。</summary>
	public static (string text, string context) BeforeTurn(string chatId, string text)
	{
		if (!PluginHooks.Any("ai.turn.before"))
		{
			return (text, null);
		}
		PyDict r = PluginHooks.Invoke("ai.turn.before", PyDict.Of("chatId", chatId ?? "", "text", text ?? ""));
		string t2 = (r.Get("text") is string s) ? s : text;
		string ctx = Py.S(r.Get("context"));
		return (t2, (ctx.Length > 0) ? ctx : null);
	}

	/// <summary>ai.tool.before：可以改参数（直接改 call.args），返回拦截原因（null = 放行）。</summary>
	public static string BeforeTool(string chatId, ToolCall call)
	{
		if (!PluginHooks.Any("ai.tool.before"))
		{
			return null;
		}
		PyDict r = PluginHooks.Invoke("ai.tool.before", PyDict.Of("chatId", chatId ?? "", "name", call.name, "args", (call.args ?? new PyDict()).Copy()), (PyDict d) => Py.Truthy(d.Get("block")));
		if (Py.Truthy(r.Get("block")))
		{
			object b = r.Get("block");
			return (b is string bs) ? bs : "插件拦截了这次调用";
		}
		if (r.Get("args") is PyDict na)
		{
			call.args = na;
		}
		return null;
	}

	/// <summary>ai.tool.after：可以改 result（字符串；插件返回非字符串时序列化成 JSON）。</summary>
	public static string AfterTool(string chatId, string name, PyDict args, string result)
	{
		if (!PluginHooks.Any("ai.tool.after"))
		{
			return result;
		}
		PyDict r = PluginHooks.Invoke("ai.tool.after", PyDict.Of("chatId", chatId ?? "", "name", name, "args", (args ?? new PyDict()).Copy(), "result", result ?? ""));
		object v = r.Get("result");
		if (v == null)
		{
			return result;
		}
		return (v is string s) ? s : PyJson.Dumps(v, null, ensureAscii: false);
	}

	/// <summary>执行插件工具（阻塞；用户停止时抛 ToolCancelled）。</summary>
	public static PluginToolOutput ExecuteTool(PluginTurn t, ToolCall call, Func<bool> cancelled)
	{
		PluginToolEntry e = PluginTools.Get(call.name) ?? throw new ValueError("插件工具已卸载：" + call.name);
		string msgText = "";
		t?.MessageTextOf.TryGetValue(call.id, out msgText);
		PluginToolOutput o = PluginTools.Execute(e, t?.ChatId, call.id, call.args, msgText ?? "", cancelled);
		if (t != null)
		{
			t.Outputs[call.id] = o;
		}
		return o;
	}

	public static string ToolLabel(string name)
	{
		PluginToolEntry e = PluginTools.Get(name);
		return (e == null || string.IsNullOrEmpty(e.Label)) ? name : e.Label;
	}

	public static void ToolStart(PluginTurn t, string callId, string name, string label)
	{
		Emit(t, "tool/start", PyDict.Of("callId", callId, "name", name, "label", label ?? name));
	}

	/// <summary>tool/result（每个 callId 只发一次）。插件工具带原始 content（含图片引用），内置工具是文字。</summary>
	public static void ToolResult(PluginTurn t, string callId, string name, string text, bool isError)
	{
		if (t == null || !t.ResultSent.TryAdd(callId, true))
		{
			return;
		}
		List<object> content;
		if (t.Outputs.TryGetValue(callId, out PluginToolOutput o) && o.Content.Count > 0)
		{
			content = o.Content.Select((object x) => (x is PyDict d) ? d.Copy() : x).ToList();
			isError = isError || o.IsError;
		}
		else
		{
			string s = text ?? "";
			if (s.Length > 4000)
			{
				s = s.Substring(0, 4000) + "…";
			}
			content = Py.L(PyDict.Of("type", "text", "text", s));
		}
		Emit(t, "tool/result", PyDict.Of("callId", callId, "name", name, "content", content, "isError", isError));
	}

	/// <summary>
	/// 工具结果里的图片：追加一条紧跟工具结果的 user 视觉消息（DESIGN §5）。模型不支持看图时只留文字说明。
	/// 更早的视觉消息去掉图片，只保留最近 <see cref="KeepVisionMessages"/> 条。
	/// </summary>
	public static void AppendVisionMessages(PluginTurn t, List<object> messages, IEnumerable<(string callId, string name)> calls, Func<bool> supportsImage)
	{
		if (t == null)
		{
			return;
		}
		List<string> paths = new List<string>();
		List<string> names = new List<string>();
		foreach ((string callId, string name) in calls)
		{
			if (t.Outputs.TryGetValue(callId, out PluginToolOutput o) && o.Images.Count > 0)
			{
				paths.AddRange(o.Images);
				names.Add(name);
			}
		}
		if (paths.Count == 0)
		{
			return;
		}
		string label = "[工具 " + string.Join("、", names.Distinct()) + " 返回的画面]";
		bool vision;
		try
		{
			vision = supportsImage();
		}
		catch (Exception)
		{
			vision = false;
		}
		// 旧的视觉消息去图
		List<PyDict> old = messages.OfType<PyDict>().Where((PyDict m) => Py.S(m.Get("id")).StartsWith(VisionIdPrefix, StringComparison.Ordinal) && m.Get("images") != null).ToList();
		for (int i = 0; i < old.Count - (KeepVisionMessages - 1); i++)
		{
			old[i].Remove("images");
			old[i]["content"] = Py.S(old[i].Get("content")) + "（较早的画面已省略）";
		}
		t.VisionSeq++;
		PyDict msg = PyDict.Of("role", "user", "content", vision ? label : (label + "\n(画面已省略：当前模型不支持看图)"), "id", VisionIdPrefix + t.TurnId + "_" + t.VisionSeq);
		if (vision)
		{
			msg["images"] = paths.Take(Images.MAX_PER_TURN).Cast<object>().ToList();
		}
		messages.Add(msg);
	}

	/// <summary>
	/// 系统提示词（按预设替换/追加；includeRuntimeContext:false 时不带启动器状态和长期记忆）。
	/// 返回 null 表示用 Agent 原来的逻辑（没有预设）。
	/// </summary>
	public static List<object> SystemMessages(AgentPreset preset, Func<string> defaultPrompt, Func<string> runtimeContext, Func<string> permissionNote, Func<string> memory, string extraContext)
	{
		if (preset == null)
		{
			return null;
		}
		string prompt = preset.ReplaceSystemPrompt ? preset.SystemPrompt : (defaultPrompt() + ((preset.SystemPrompt.Length > 0) ? ("\n\n" + preset.SystemPrompt) : ""));
		List<object> list = new List<object> { PyDict.Of("role", "system", "content", prompt ?? "") };
		if (preset.IncludeRuntimeContext)
		{
			list.Add(PyDict.Of("role", "system", "content", "当前启动器状态：\n" + runtimeContext()));
			string note = permissionNote();
			if (!string.IsNullOrEmpty(note))
			{
				list.Add(PyDict.Of("role", "system", "content", note));
			}
			string mem = memory();
			if (!string.IsNullOrEmpty(mem))
			{
				list.Add(PyDict.Of("role", "system", "content", "长期记忆：\n" + mem));
			}
		}
		else if (preset.Builtin)
		{
			string note2 = permissionNote();
			if (!string.IsNullOrEmpty(note2))
			{
				list.Add(PyDict.Of("role", "system", "content", note2));
			}
		}
		if (!string.IsNullOrEmpty(extraContext))
		{
			list.Add(PyDict.Of("role", "system", "content", extraContext));
		}
		return list;
	}
}
