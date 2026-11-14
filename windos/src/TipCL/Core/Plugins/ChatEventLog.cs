using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TipCL.Core.Plugins;

/// <summary>
/// AI 对话事件流（DESIGN §5）。每个对话一个递增 seq，内存里保留最近 <see cref="Capacity"/> 条。
/// 每追加一条事件就同步调用 <see cref="Sink"/>（插件管理器在这里把 <c>ai.event</c> 直接写给 Node 宿主，并发到 EventBus），
/// 所以事件到达 Node 的顺序和发生顺序一致：assistant/message 一定早于该消息里工具的 tool.execute 请求。
/// <para>assistant/delta 会合并：连续的片段攒在一起，遇到别的事件或攒够 <see cref="DeltaFlushChars"/> 字 / <see cref="DeltaFlushMs"/> 毫秒才发一条，避免把环形缓冲挤满。</para>
/// </summary>
public static class ChatEventLog
{
	public const int Capacity = 2000;

	public const int DeltaFlushChars = 120;

	public const int DeltaFlushMs = 120;

	private sealed class ChatLog
	{
		public long Seq;

		public readonly LinkedList<PyDict> Events = new LinkedList<PyDict>();

		public readonly StringBuilder PendingDelta = new StringBuilder();

		public string PendingType;

		public double PendingSince;

		public bool Running;
	}

	private static readonly object Lock = new object();

	private static readonly Dictionary<string, ChatLog> Logs = new Dictionary<string, ChatLog>(StringComparer.Ordinal);

	/// <summary>(chatId, event)。在持有内部锁时调用，保证顺序；回调里不要再回头调 ChatEventLog。</summary>
	public static Action<string, PyDict> Sink;

	/// <summary>测试用的时钟（毫秒）。</summary>
	public static Func<double> NowMs = () => (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	private static ChatLog Get(string chatId)
	{
		if (!Logs.TryGetValue(chatId, out ChatLog l))
		{
			l = new ChatLog();
			Logs[chatId] = l;
		}
		return l;
	}

	/// <summary>追加一条事件，返回它的 seq。chatId 为空时忽略（返回 0）。</summary>
	public static long Append(string chatId, string type, PyDict data)
	{
		if (string.IsNullOrEmpty(chatId))
		{
			return 0L;
		}
		lock (Lock)
		{
			ChatLog l = Get(chatId);
			FlushDeltaLocked(chatId, l);
			if (type == "turn/start")
			{
				l.Running = true;
			}
			else if (type == "turn/end")
			{
				l.Running = false;
			}
			return AddLocked(chatId, l, type, data);
		}
	}

	/// <summary>流式片段：assistant/delta 或 assistant/reasoning，自动合并。</summary>
	public static void AppendDelta(string chatId, string type, string text)
	{
		if (string.IsNullOrEmpty(chatId) || string.IsNullOrEmpty(text))
		{
			return;
		}
		lock (Lock)
		{
			ChatLog l = Get(chatId);
			if (l.PendingType != null && l.PendingType != type)
			{
				FlushDeltaLocked(chatId, l);
			}
			if (l.PendingType == null)
			{
				l.PendingType = type;
				l.PendingSince = NowMs();
			}
			l.PendingDelta.Append(text);
			if (l.PendingDelta.Length >= DeltaFlushChars || NowMs() - l.PendingSince >= DeltaFlushMs)
			{
				FlushDeltaLocked(chatId, l);
			}
		}
	}

	/// <summary>把攒着的片段发出去（回合结束、工具开始前调用）。</summary>
	public static void FlushDelta(string chatId)
	{
		if (string.IsNullOrEmpty(chatId))
		{
			return;
		}
		lock (Lock)
		{
			if (Logs.TryGetValue(chatId, out ChatLog l))
			{
				FlushDeltaLocked(chatId, l);
			}
		}
	}

	private static void FlushDeltaLocked(string chatId, ChatLog l)
	{
		if (l.PendingType == null)
		{
			return;
		}
		string type = l.PendingType;
		string text = l.PendingDelta.ToString();
		l.PendingType = null;
		l.PendingDelta.Clear();
		if (text.Length > 0)
		{
			AddLocked(chatId, l, type, PyDict.Of("text", text));
		}
	}

	private static long AddLocked(string chatId, ChatLog l, string type, PyDict data)
	{
		l.Seq++;
		PyDict ev = PyDict.Of("seq", l.Seq, "type", type, "data", data ?? new PyDict(), "at", (long)NowMs());
		l.Events.AddLast(ev);
		while (l.Events.Count > Capacity)
		{
			l.Events.RemoveFirst();
		}
		try
		{
			Sink?.Invoke(chatId, ev);
		}
		catch (Exception)
		{
		}
		return l.Seq;
	}

	/// <summary>seq 大于 <paramref name="from"/> 的事件（from 不含；from&lt;=0 时返回全部保留的）。</summary>
	public static (long seq, List<object> events) Snapshot(string chatId, long from = 0L)
	{
		lock (Lock)
		{
			if (string.IsNullOrEmpty(chatId) || !Logs.TryGetValue(chatId, out ChatLog l))
			{
				return (0L, new List<object>());
			}
			List<object> r = l.Events.Where((PyDict e) => Py.Int(e["seq"]) > from).Select((PyDict e) => (object)e.Copy()).ToList();
			return (l.Seq, r);
		}
	}

	public static long Seq(string chatId)
	{
		lock (Lock)
		{
			return (chatId != null && Logs.TryGetValue(chatId, out ChatLog l)) ? l.Seq : 0L;
		}
	}

	public static bool Running(string chatId)
	{
		lock (Lock)
		{
			return chatId != null && Logs.TryGetValue(chatId, out ChatLog l) && l.Running;
		}
	}

	/// <summary>对话被删除时丢掉事件。</summary>
	public static void Forget(string chatId)
	{
		lock (Lock)
		{
			if (chatId != null)
			{
				Logs.Remove(chatId);
			}
		}
	}

	/// <summary>测试用。</summary>
	public static void Reset()
	{
		lock (Lock)
		{
			Logs.Clear();
		}
	}

	/// <summary>
	/// 把模型流里的 &lt;think&gt;…&lt;/think&gt; 拆开：思考进 assistant/reasoning，正文进 assistant/delta。
	/// 每个回合一个实例。
	/// </summary>
	public sealed class ThinkSplitter
	{
		private bool _inThink;

		private readonly StringBuilder _visible = new StringBuilder();

		public string VisibleText => _visible.ToString();

		public void Reset()
		{
			_inThink = false;
			_visible.Clear();
		}

		/// <summary>喂一个片段，回调 (type, text)。</summary>
		public void Feed(string piece, Action<string, string> emit)
		{
			string s = piece ?? "";
			while (s.Length > 0)
			{
				if (_inThink)
				{
					int end = s.IndexOf("</think>", StringComparison.Ordinal);
					if (end < 0)
					{
						emit("assistant/reasoning", s);
						return;
					}
					if (end > 0)
					{
						emit("assistant/reasoning", s.Substring(0, end));
					}
					_inThink = false;
					s = s.Substring(end + 8);
				}
				else
				{
					int start = s.IndexOf("<think>", StringComparison.Ordinal);
					if (start < 0)
					{
						_visible.Append(s);
						emit("assistant/delta", s);
						return;
					}
					if (start > 0)
					{
						_visible.Append(s.Substring(0, start));
						emit("assistant/delta", s.Substring(0, start));
					}
					_inThink = true;
					s = s.Substring(start + 7);
				}
			}
		}
	}

	/// <summary>去掉文本里的 &lt;think&gt;…&lt;/think&gt; 段（未闭合的也去掉到结尾）。</summary>
	public static string StripThink(string text)
	{
		if (string.IsNullOrEmpty(text) || text.IndexOf("<think>", StringComparison.Ordinal) < 0)
		{
			return text ?? "";
		}
		StringBuilder sb = new StringBuilder();
		new ThinkSplitter().Feed(text, delegate(string t, string s)
		{
			if (t == "assistant/delta")
			{
				sb.Append(s);
			}
		});
		return sb.ToString().Trim();
	}
}
