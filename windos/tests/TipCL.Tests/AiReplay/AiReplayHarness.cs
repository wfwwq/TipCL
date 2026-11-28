using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using TipCL.Core.Plugins;
using Xunit;

namespace TipCL.Tests.AiReplay;

/// <summary>
/// AI5-T 假模型回放台:一个用例一台。
/// 挂上 <see cref="FakeModel" /> 之后 <c>Client.ChatStream</c> / <c>Client.ChatOnce</c> 都不再发真实 HTTP,
/// 但假流仍旧走 AssembleStream 的 SSE 解析(注入点在 Client.TestTransport)。
/// 同时提供:临时数据根(TestHome)、真 BackendApi、confirmFn / askFn 桩、插件事件快照、Trace / Store 事件读取。
/// </summary>
public sealed class AiReplayHarness : IDisposable
{
	/// <summary>
	/// 注入点是 internal,测试工程是另一个程序集。Windows 树**刻意没有** InternalsVisibleTo
	/// (见 PluginManager.cs / Pages/Layout/Widgets/WidgetCommon.cs 里那两条注释:公开给单测是刻意的),
	/// 所以这里用反射挂载,避免为了测试改生产程序集的可见性。
	/// </summary>
	private static readonly FieldInfo _transportField = typeof(Client).GetField("TestTransport", BindingFlags.Static | BindingFlags.NonPublic);

	public const string FakeModelName = "fake-model-1";

	public const string FakeFallbackModel = "fake-fallback";

	private readonly TestHome _home;

	private BackendApi _backend;

	private readonly object _statusLock = new object();

	public FakeModel Model { get; } = new FakeModel();

	public string ChatId { get; }

	public PyDict Settings { get; }

	/// <summary>onDelta 收到的全部片段(按顺序拼起来 = 用户看到的流式正文)。</summary>
	public StringBuilder Streamed { get; } = new StringBuilder();

	/// <summary>onStatus 收到的 (kind, payload),按发生顺序。</summary>
	public List<(string Kind, PyDict Payload)> Statuses { get; } = new List<(string, PyDict)>();

	/// <summary>confirmFn 收到的每次询问(工具名、确认文案、原因)。</summary>
	public List<(string Tool, string Label, string Reason)> ConfirmAsked { get; } = new List<(string, string, string)>();

	/// <summary>askFn 收到的每次提问。</summary>
	public List<List<object>> AskAsked { get; } = new List<List<object>>();

	/// <summary>0 = 拒绝(默认,防止用例意外真的写盘),返回 AiRule = 记住规则,true = 这一次允许。</summary>
	public Func<string, PyDict, string, string, object> Confirm { get; set; }

	/// <summary>askFn 桩:默认回第一个选项的 id。</summary>
	public Func<List<object>, string, object> Ask { get; set; }

	/// <summary>onStatus 的旁路钩子(例如「工具跑完就把 cancelled 置真」用来测中途取消)。</summary>
	public Action<string, PyDict> OnStatusHook { get; set; }

	public AiReplayHarness()
	{
		if (_transportField == null)
		{
			throw new InvalidOperationException("Client.TestTransport 不存在:注入点被改名或删掉了(AI5-T)");
		}
		_home = new TestHome();
		ChatId = "replay" + Guid.NewGuid().ToString("N").Substring(0, 8);
		Settings = PyDict.Of("ai_mode", "custom", "ai_base_url", "https://fake-model.invalid/v1", "ai_api_key", "", "ai_model", FakeModelName, "ai_session_id", ChatId, "ai_permission_mode", "default", "ai_confirm_writes", true, "ai_max_rounds", 20L);
		Confirm = DefaultConfirm;
		Ask = DefaultAsk;
		_transportField.SetValue(null, (Func<PyDict, PyDict, bool, AiResponse>)Model.Serve);
	}

	/// <summary>测试用的临时数据根(TIPCL_HOME)。</summary>
	public string Root => _home.Dir;

	/// <summary>真 BackendApi:所有工具执行都落在这个临时根里,不碰真实用户目录。</summary>
	public BackendApi Backend => _backend ?? (_backend = new BackendApi(new EventBus()));

	/// <summary>默认实例的目录。</summary>
	public string InstancePath(string name = "default")
	{
		return new Instance(name).path;
	}

	/// <summary>默认实例的 config 目录下的某个文件(断言「有没有落盘」用)。</summary>
	public string InstanceConfigFile(string rel, string name = "default")
	{
		return Path.Combine(InstancePath(name), "config", rel);
	}

	public PyDict CloneSettings()
	{
		return Settings.Copy();
	}

	/// <summary>跑一个 AI 回合(和界面走同一条入口 Agent.RunAgent)。</summary>
	public AgentResult Send(string userText, List<object> history = null, Func<bool> cancelled = null, List<object> images = null, PyDict settings = null)
	{
		return Agent.RunAgent(Backend, settings ?? Settings, history ?? new List<object>(), userText, delegate(string piece)
		{
			Streamed.Append(piece);
		}, delegate(string kind, PyDict payload)
		{
			lock (_statusLock)
			{
				Statuses.Add((kind, payload ?? new PyDict()));
			}
			try
			{
				OnStatusHook?.Invoke(kind, payload ?? new PyDict());
			}
			catch (Exception)
			{
			}
		}, delegate(string name, PyDict args, string label, string reason)
		{
			return Confirm(name, args, label, reason);
		}, delegate(List<object> questions, string title)
		{
			AskAsked.Add(questions);
			return Ask(questions, title);
		}, cancelled, null, null, images);
	}

	private object DefaultConfirm(string tool, PyDict args, string label, string reason)
	{
		ConfirmAsked.Add((tool, label, reason));
		return false;
	}

	private static object DefaultAsk(List<object> questions, string title)
	{
		return FirstOptionId(questions);
	}

	/// <summary>askFn 缺省作答:第一题的第一个选项 id。</summary>
	public static string FirstOptionId(List<object> questions)
	{
		if (questions == null || questions.Count == 0)
		{
			return "";
		}
		PyDict first = questions[0] as PyDict;
		List<object> options = Py.ListOr(Py.Or(first?.Get("options"), new List<object>()));
		if (options.Count == 0)
		{
			return "";
		}
		object o = options[0];
		if (o is PyDict pyDict)
		{
			return Py.Str(Py.Or(pyDict.Get("id"), pyDict.Get("label")));
		}
		return Py.Str(o);
	}

	// —— 插件事件(ChatEventLog,DESIGN §5 的事件序列) ——

	public List<PyDict> PluginEvents()
	{
		return ChatEventLog.Snapshot(ChatId, 0L).events.OfType<PyDict>().ToList();
	}

	public List<string> PluginTypes()
	{
		return PluginEvents().Select((PyDict e) => Py.S(e.Get("type"))).ToList();
	}

	public List<PyDict> PluginEventsOf(string type)
	{
		return PluginEvents().Where((PyDict e) => Py.S(e.Get("type")) == type).ToList();
	}

	public int PluginEventCount(string type)
	{
		return PluginEventsOf(type).Count;
	}

	public PyDict PluginEvent(string type, int nth = 0)
	{
		List<PyDict> list = PluginEventsOf(type);
		if (nth >= list.Count)
		{
			throw new InvalidOperationException("插件事件里没有第 " + (nth + 1) + " 条 " + type + "(现有:" + string.Join(", ", PluginTypes()) + ")");
		}
		return list[nth];
	}

	/// <summary>事件里的 data 字段。</summary>
	public PyDict EventData(PyDict ev)
	{
		return ev?.Get("data") as PyDict ?? new PyDict();
	}

	public long EventSeq(PyDict ev)
	{
		return Py.Int(ev.Get("seq"));
	}

	/// <summary>终态事件:turn/end 恰好一条,并且回合已经不在运行(UI 解锁)。</summary>
	public PyDict AssertSingleTurnEnd(string expectReason = null)
	{
		Assert.Equal(1, PluginEventCount("turn/start"));
		Assert.Equal(1, PluginEventCount("turn/end"));
		PyDict end = PluginEvent("turn/end");
		if (expectReason != null)
		{
			Assert.Equal(expectReason, Py.S(EventData(end).Get("reason")));
		}
		Assert.False(ChatEventLog.Running(ChatId));
		return end;
	}

	// —— Trace / Store(落盘的运行时记录) ——

	public List<PyDict> ReadJsonl(string path)
	{
		List<PyDict> list = new List<PyDict>();
		if (!File.Exists(path))
		{
			return list;
		}
		foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
		{
			if (line.Trim().Length != 0 && PyJson.Loads(line) is PyDict pyDict)
			{
				list.Add(pyDict);
			}
		}
		return list;
	}

	public List<PyDict> TraceEvents()
	{
		return ReadJsonl(Path.Combine(Root, "cache", "ai_trace", DateTime.Now.ToString("yyyyMMdd") + ".jsonl"));
	}

	public List<PyDict> TraceEventsOf(string type)
	{
		return TraceEvents().Where((PyDict e) => Py.S(e.Get("event")) == type).ToList();
	}

	public List<PyDict> StoreEvents(string chatId = null)
	{
		return ReadJsonl(Path.Combine(Root, "cache", "ai_sessions", (chatId ?? ChatId) + ".jsonl"));
	}

	public List<string> StoreEventNames(string chatId = null)
	{
		return StoreEvents(chatId).Select((PyDict e) => Py.S(e.Get("event"))).ToList();
	}

	public List<PyDict> StoreEventsOf(string ev, string chatId = null)
	{
		return StoreEvents(chatId).Where((PyDict e) => Py.S(e.Get("event")) == ev).ToList();
	}

	public List<PyDict> StatusesOf(string kind)
	{
		lock (_statusLock)
		{
			return Statuses.Where(s => s.Kind == kind).Select(s => s.Payload).ToList();
		}
	}

	/// <summary>把 <c>&lt;home&gt;/ai_permissions.json</summary> 读成规则表。</summary>
	public PyDict PermissionStore()
	{
		return Permission.LoadRuleStore();
	}

	public void Dispose()
	{
		try
		{
			_transportField.SetValue(null, null);
		}
		catch (Exception)
		{
		}
		ChatEventLog.Forget(ChatId);
		ChatEventLog.Forget(ChatId + "-sub");
		_home.Dispose();
	}
}
