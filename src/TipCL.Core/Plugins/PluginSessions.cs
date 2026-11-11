using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TipCL.Core.Launcher.Ai;

namespace TipCL.Core.Plugins;

/// <summary>
/// 插件驱动 AI 对话（DESIGN §4 ai.chats.*，§5 并发规则）。TipCL 同一时间只跑一个 AI 回合：
/// <list type="bullet">
/// <item>send auto：没在跑 → 开新回合；正在跑同一对话 → steer；正在跑别的对话 → {ok:false, error:"busy"}。</item>
/// <item>send steer：同 auto（没在跑时直接开回合）。</item>
/// <item>send queue：没在跑 → 开新回合；正在跑同一对话 → 排队，本回合结束后自动作为下一回合发出；跑别的对话 → busy。</item>
/// </list>
/// </summary>
public sealed class PluginSessions
{
	private readonly PluginManager _mgr;

	private readonly object _qLock = new object();

	private readonly Dictionary<string, List<(string text, List<object> images, string source)>> _queue = new Dictionary<string, List<(string, List<object>, string)>>(StringComparer.Ordinal);

	public PluginSessions(PluginManager mgr)
	{
		_mgr = mgr;
	}

	private BackendApi B => _mgr.Backend ?? throw new PluginRpcException("unavailable", "启动器内核未就绪");

	/// <summary>当前在跑的对话 id（没在跑返回 null）。</summary>
	public string RunningChat()
	{
		BackendApi b = _mgr.Backend;
		if (b == null || !b._ai_busy)
		{
			return null;
		}
		return b._ai_running_chat;
	}

	/// <summary>预设 id 解析：插件自己的短 id → plugin:&lt;插件&gt;/&lt;id&gt;；完整 id 原样；找不到抛错。空返回 ""。</summary>
	public static string ResolvePreset(string plugin, string preset)
	{
		if (string.IsNullOrEmpty(preset))
		{
			return "";
		}
		AgentPreset p = AgentPresets.Get(AgentPreset.MakeFullId(plugin, preset)) ?? AgentPresets.Get(preset);
		if (p == null)
		{
			throw new PluginRpcException("not_found", "没有这个预设：" + preset);
		}
		return p.FullId;
	}

	/// <summary>界面新建对话用：完整 id 或唯一的短 id → 完整 id；找不到时原样保留（插件可能稍后才加载）。</summary>
	public static string ResolvePresetLenient(string preset)
	{
		if (string.IsNullOrEmpty(preset))
		{
			return "";
		}
		return AgentPresets.Get(preset)?.FullId ?? preset;
	}

	public PyDict Create(string plugin, string preset, string title, string view, string prompt, bool open, List<object> images = null)
	{
		string full = ResolvePreset(plugin, preset);
		AgentPreset p = AgentPresets.Get(full);
		if (string.IsNullOrEmpty(view) && p != null)
		{
			view = p.View;
		}
		string chatId = CreateChat(full, view ?? "", title, open);
		_mgr.Bus.Emit("ai.chat.created", PyDict.Of("chatId", chatId, "preset", full, "view", view ?? "", "plugin", plugin ?? ""));
		PyDict result = PyDict.Of("chatId", chatId);
		if (!string.IsNullOrEmpty(prompt))
		{
			result["send"] = Send(plugin, chatId, prompt, "auto", images);
		}
		if (open)
		{
			Open(plugin, chatId, view);
		}
		return result;
	}

	/// <summary>在 ai_chats.json 里新建一条带 preset / view 的对话。open=false 时不切换当前对话。</summary>
	public static string CreateChat(string preset, string view, string title, bool makeActive)
	{
		PyDict data = Store.Load();
		object prevActive = data.Get("active_id");
		PyDict chat = Store.NewChat(data);
		chat["preset"] = preset ?? "";
		chat["view"] = view ?? "";
		if (!string.IsNullOrEmpty(title))
		{
			chat["title"] = Py.Slice(title, null, 40);
		}
		if (!makeActive && prevActive != null && Store.GetChat(data, prevActive) != null)
		{
			data["active_id"] = prevActive;
		}
		Store.Save(data);
		return Py.Str(chat["id"]);
	}

	public List<object> List()
	{
		PyDict data = Store.Load();
		string running = RunningChat();
		List<object> r = new List<object>();
		foreach (PyDict c in Py.ListOr(data.Get("chats")).OfType<PyDict>())
		{
			string id = Py.S(c.Get("id"));
			r.Add(PyDict.Of("chatId", id, "title", Py.S(c.Get("title")), "preset", Py.S(c.Get("preset")), "view", Py.S(c.Get("view")), "updatedAt", Py.IntOr(c.Get("updated"), 0L) * 1000, "running", id == running, "active", Py.Eq(data.Get("active_id"), id)));
		}
		return r;
	}

	public PyDict Events(string chatId, long from)
	{
		var (seq, events) = ChatEventLog.Snapshot(chatId, from);
		return PyDict.Of("seq", seq, "events", events);
	}

	public PyDict StateOf(string chatId)
	{
		return PyDict.Of("running", RunningChat() == chatId && chatId != null, "seq", ChatEventLog.Seq(chatId));
	}

	private static bool ChatExists(string chatId)
	{
		return Store.GetChat(Store.Load(), chatId) != null;
	}

	/// <summary>发消息（规则见类说明）。返回 {ok, mode:"start"|"steer"|"queue", error?}。</summary>
	public PyDict Send(string plugin, string chatId, string text, string mode, List<object> images = null, string source = "plugin")
	{
		text = (text ?? "").Trim();
		if (text.Length == 0)
		{
			return PyDict.Of("ok", false, "mode", mode ?? "auto", "error", "empty");
		}
		if (string.IsNullOrEmpty(chatId) || !ChatExists(chatId))
		{
			return PyDict.Of("ok", false, "mode", mode ?? "auto", "error", "not_found");
		}
		mode = string.IsNullOrEmpty(mode) ? "auto" : mode;
		if (mode != "auto" && mode != "queue" && mode != "steer")
		{
			throw new PluginRpcException("invalid_params", "mode 只能是 auto / queue / steer");
		}
		BackendApi b = B;
		lock (_qLock)
		{
			string running = RunningChat();
			if (running == null)
			{
				return Start(chatId, text, images, source);
			}
			if (running != chatId)
			{
				return PyDict.Of("ok", false, "mode", mode, "error", "busy");
			}
			if (mode == "queue")
			{
				if (!_queue.TryGetValue(chatId, out List<(string, List<object>, string)> q))
				{
					q = new List<(string, List<object>, string)>();
					_queue[chatId] = q;
				}
				q.Add((text, images, source));
				return PyDict.Of("ok", true, "mode", "queue", "queued", (long)q.Count);
			}
			PyDict r = b.ai_steer(text) as PyDict;
			if (r != null && Py.Truthy(r.Get("ok")))
			{
				return PyDict.Of("ok", true, "mode", "steer");
			}
			// 回合刚好结束了：直接开新回合
			if (RunningChat() == null)
			{
				return Start(chatId, text, images, source);
			}
			return PyDict.Of("ok", false, "mode", "steer", "error", Py.S(r?.Get("message")));
		}
	}

	private PyDict Start(string chatId, string text, List<object> images, string source)
	{
		PyDict r = B.ai_send(text, chatId, null, null, ToImagePaths(images), source) as PyDict;
		if (r != null && Py.Truthy(r.Get("ok")))
		{
			return PyDict.Of("ok", true, "mode", "start");
		}
		return PyDict.Of("ok", false, "mode", "start", "error", "busy", "message", Py.S(r?.Get("message")));
	}

	/// <summary>图片：文件路径原样；{data, mediaType} 存成附件；{attachment:{id}} / "att_xxx" 解析成路径。</summary>
	private static List<object> ToImagePaths(List<object> images)
	{
		if (images == null || images.Count == 0)
		{
			return null;
		}
		List<object> r = new List<object>();
		foreach (object im in images)
		{
			if (im is string s)
			{
				string p = PluginAttachments.Resolve(s) ?? s;
				r.Add(p);
			}
			else if (im is PyDict d)
			{
				string path = null;
				if (d.Get("attachment") is PyDict att)
				{
					path = PluginAttachments.Resolve(Py.S(att.Get("id")));
				}
				else if (d.Get("id") is string aid)
				{
					path = PluginAttachments.Resolve(aid);
				}
				else if (d.Get("data") is string b64)
				{
					int comma = b64.IndexOf(',');
					byte[] bytes = Convert.FromBase64String((b64.StartsWith("data:", StringComparison.Ordinal) && comma > 0) ? b64.Substring(comma + 1) : b64);
					PyDict saved = PluginAttachments.Save("tipcl-chat", bytes, Py.S(d.Get("mediaType")), Py.S(d.Get("name")));
					path = PluginAttachments.Resolve(Py.S(saved["id"]));
				}
				if (path != null)
				{
					r.Add(path);
				}
			}
		}
		return (r.Count > 0) ? r : null;
	}

	public PyDict Cancel(string chatId)
	{
		lock (_qLock)
		{
			_queue.Remove(chatId ?? "");
		}
		if (RunningChat() == chatId && chatId != null)
		{
			B.ai_stop();
			return PyDict.Of("ok", true);
		}
		return PyDict.Of("ok", false, "error", "not_running");
	}

	public void Open(string plugin, string chatId, string view)
	{
		if (!string.IsNullOrEmpty(chatId))
		{
			_mgr.UiRequest(plugin, "openChat", PyDict.Of("chatId", chatId, "view", view ?? ""), wait: false);
		}
	}

	/// <summary>回合结束（turn/end 事件）后调用：有排队的消息就作为下一回合发出。</summary>
	public void OnTurnEnded(string chatId)
	{
		(string text, List<object> images, string source) next;
		lock (_qLock)
		{
			if (!_queue.TryGetValue(chatId, out List<(string, List<object>, string)> q) || q.Count == 0)
			{
				return;
			}
			next = q[0];
			q.RemoveAt(0);
			if (q.Count == 0)
			{
				_queue.Remove(chatId);
			}
		}
		ThreadPool.QueueUserWorkItem(delegate
		{
			// 等上一回合把“忙”标记释放（turn/end 比释放早一点点）
			for (int i = 0; i < 300; i++)
			{
				if (RunningChat() == null)
				{
					break;
				}
				Thread.Sleep(100);
			}
			try
			{
				PyDict r = Send(null, chatId, next.text, "queue", next.images, next.source);
				if (!Py.Truthy(r.Get("ok")))
				{
					_mgr.LogHost("排队消息没能发出（" + Py.S(r.Get("error")) + "）：" + chatId);
				}
			}
			catch (Exception ex)
			{
				_mgr.LogHost("排队消息发送失败：" + ex.Message);
			}
		});
	}
}
