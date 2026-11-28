using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;

namespace TipCL.Tests.AiReplay;

/// <summary>假模型脚本里的一次工具调用。</summary>
public sealed class FakeToolCall
{
	public string Name;

	/// <summary>工具参数的 JSON 文本(和真实 SSE 里一样是字符串,不是对象)。</summary>
	public string ArgsJson = "{}";

	public string Id;

	/// <summary>把 arguments 拆成两片吐,用来验证增量拼接(默认拆在第 1 个字符后)。</summary>
	public int? SplitArgsAt;

	public FakeToolCall(string name, string argsJson = "{}", string id = null)
	{
		Name = name;
		ArgsJson = argsJson ?? "{}";
		Id = id;
	}
}

/// <summary>
/// 假模型的一步回复。既能在流式请求上用(吐 SSE),也能在非流式请求上用(吐 OpenAI 非流式 JSON);
/// 只填了 Deltas 就自动拼成 content,只填了 Content 就自动当一段 delta。
/// </summary>
public sealed class ModelReply
{
	public int Status = 200;

	public string FailMessage = "假模型错误";

	public string FailType = "invalid_request_error";

	public string Content;

	public readonly List<string> Deltas = new List<string>();

	public readonly List<FakeToolCall> Calls = new List<FakeToolCall>();

	/// <summary>stop / tool_calls / length;有工具调用时默认 tool_calls。</summary>
	public string Finish;

	public long PromptTokens = 12L;

	public long CompletionTokens = 7L;

	/// <summary>不发 usage 帧。</summary>
	public bool NoUsage;

	/// <summary>不发 data: [DONE](模拟流被上游无声掐断)。</summary>
	public bool NoDone;

	/// <summary>已给的片段吐完后断流(IOException → AiStreamBroken → AssembleStream 的兜底分支)。</summary>
	public bool BreakMidStream;

	/// <summary>流内 error 事件(SSE 里带 error 字段的那一帧)。</summary>
	public string StreamErrorMessage;

	public static ModelReply Text(params string[] parts)
	{
		ModelReply reply = new ModelReply();
		reply.Deltas.AddRange(parts ?? Array.Empty<string>());
		reply.Content = string.Concat(parts ?? Array.Empty<string>());
		return reply;
	}

	/// <summary>非流式回复(兜底 ChatOnce 用),只填 content。</summary>
	public static ModelReply NonStream(string content)
	{
		return new ModelReply
		{
			Content = content
		};
	}

	public static ModelReply Call(string name, string argsJson = "{}", string id = null)
	{
		return ModelReply.CallsOf(new FakeToolCall(name, argsJson, id));
	}

	public static ModelReply CallsOf(params FakeToolCall[] calls)
	{
		ModelReply reply = new ModelReply
		{
			Finish = "tool_calls"
		};
		reply.Calls.AddRange(calls);
		return reply;
	}

	/// <summary>HTTP 层错误(429 / 5xx / 4xx):ChatStream 与 ChatOnce 都在发请求这一步就抛 AIClientError。</summary>
	public static ModelReply HttpError(int status, string message)
	{
		return new ModelReply
		{
			Status = status,
			FailMessage = message
		};
	}

	/// <summary>流内 error 事件(HTTP 200,SSE 里报错)。</summary>
	public static ModelReply StreamError(string message)
	{
		ModelReply reply = new ModelReply
		{
			StreamErrorMessage = message,
			NoUsage = true
		};
		return reply;
	}

	/// <summary>空回复:没有任何 delta / 工具调用。</summary>
	public static ModelReply Empty()
	{
		ModelReply reply = new ModelReply
		{
			NoUsage = true
		};
		return reply;
	}

	/// <summary>finish_reason=length 的截断回复。</summary>
	public static ModelReply Truncated(params string[] parts)
	{
		ModelReply reply = ModelReply.Text(parts);
		reply.Finish = "length";
		return reply;
	}

	/// <summary>这一回复的 SSE 报文(和真实网关吐的形状一致)。公开给用例做「同一条解析路径」对照。</summary>
	public string BuildStreamBody()
	{
		StringBuilder sb = new StringBuilder();
		string finish = Finish ?? ((Calls.Count > 0) ? "tool_calls" : "stop");
		if (StreamErrorMessage != null)
		{
			sb.Append(Sse("{\"error\":{\"message\":" + J(StreamErrorMessage) + ",\"type\":\"server_error\"}}"));
		}
		List<string> pieces = new List<string>(Deltas);
		if (pieces.Count == 0 && !string.IsNullOrEmpty(Content))
		{
			// 只写了 Content 的回复也能用在流式请求上:当成一段 delta 吐出去
			pieces.Add(Content);
		}
		foreach (string delta in pieces)
		{
			sb.Append(Sse("{\"choices\":[{\"index\":0,\"delta\":{\"content\":" + J(delta) + "},\"finish_reason\":null}]}"));
		}
		for (int i = 0; i < Calls.Count; i++)
		{
			FakeToolCall call = Calls[i];
			string id = (call.Id ?? ("call_" + (i + 1)));
			string args = call.ArgsJson ?? "{}";
			int cut = Math.Max(0, Math.Min(args.Length, call.SplitArgsAt ?? args.Length));
			string head = args.Substring(0, cut);
			string tail = args.Substring(cut);
			sb.Append(Sse("{\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":" + i + ",\"id\":" + J(id) + ",\"type\":\"function\",\"function\":{\"name\":" + J(call.Name) + ",\"arguments\":" + J(head) + "}}]},\"finish_reason\":null}]}"));
			if (tail.Length > 0)
			{
				sb.Append(Sse("{\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":" + i + ",\"function\":{\"arguments\":" + J(tail) + "}}]},\"finish_reason\":null}]}"));
			}
		}
		sb.Append(Sse("{\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":" + J(finish) + "}]}"));
		if (!NoUsage)
		{
			sb.Append(Sse("{\"choices\":[],\"usage\":{\"prompt_tokens\":" + PromptTokens + ",\"completion_tokens\":" + CompletionTokens + "}}"));
		}
		// 断流的流不会再有 data: [DONE] —— 有 [DONE] 的话 AssembleStream 在读到它时就正常收尾,断流分支根本不会触发
		if (!NoDone && !BreakMidStream)
		{
			sb.Append("data: [DONE]\n\n");
		}
		return sb.ToString();
	}

	/// <summary>这一回复的非流式 JSON 报文。</summary>
	public string BuildJsonBody()
	{
		if (Status >= 400)
		{
			return "{\"error\":{\"message\":" + J(FailMessage) + ",\"type\":" + J(FailType) + "}}";
		}
		string content = Content ?? string.Concat(Deltas);
		StringBuilder sb = new StringBuilder();
		sb.Append("{\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":" + J(content));
		if (Calls.Count > 0)
		{
			sb.Append(",\"tool_calls\":[");
			for (int i = 0; i < Calls.Count; i++)
			{
				FakeToolCall call = Calls[i];
				if (i > 0)
				{
					sb.Append(",");
				}
				sb.Append("{\"id\":" + J(call.Id ?? ("call_" + (i + 1))) + ",\"type\":\"function\",\"function\":{\"name\":" + J(call.Name) + ",\"arguments\":" + J(call.ArgsJson ?? "{}") + "}}");
			}
			sb.Append("]");
		}
		string finish = Finish ?? ((Calls.Count > 0) ? "tool_calls" : "stop");
		sb.Append("},\"finish_reason\":" + J(finish) + "}]");
		if (!NoUsage)
		{
			sb.Append(",\"usage\":{\"prompt_tokens\":" + PromptTokens + ",\"completion_tokens\":" + CompletionTokens + "}");
		}
		sb.Append("}");
		return sb.ToString();
	}

	private static string Sse(string json)
	{
		return "data: " + json + "\n\n";
	}

	private static string J(string s)
	{
		return PyJson.Dumps(s ?? "", null, ensureAscii: false);
	}
}

/// <summary>假模型收到的一次请求。字段全部来自 Client 真正交出去的请求体,不是测试自己拼的。</summary>
public sealed class ModelRequest
{
	public PyDict Endpoint;

	public PyDict Body;

	/// <summary>true = ChatStream(流式),false = ChatOnce(非流式兜底 / 摘要)。</summary>
	public bool Stream;

	public int Index;

	public string Model => Py.S(Body.Get("model"));

	public bool HasTools => Body.Get("tools") is List<object>;

	/// <summary>本次请求下发给模型的工具 schema(null = 请求里没有 tools 字段)。</summary>
	public List<object> ToolSchemas => Body.Get("tools") as List<object>;

	public List<string> ToolNames => (ToolSchemas ?? new List<object>()).Select(delegate(object s)
	{
		PyDict pyDict = (PyDict)((PyDict)s)["function"];
		return Py.Str(pyDict["name"]);
	}).ToList();

	public List<object> Msgs => Body.Get("messages") as List<object> ?? new List<object>();

	public List<PyDict> Messages => Msgs.OfType<PyDict>().ToList();

	/// <summary>
	/// 摘要请求:Agent._summarize 的 ChatOnce 是不带 tools 的(tools=null),
	/// 而界面走的一律是带工具表的 ChatStream / 兜底 ChatOnce(SelectToolSchemas 永远不会返回空表)。
	/// </summary>
	public bool IsSummarizer => !HasTools;

	public List<string> Roles => Messages.Select((PyDict m) => Py.S(m.Get("role"))).ToList();

	public string SystemText => string.Join("\n", Messages.Where((PyDict m) => Py.Eq(m.Get("role"), "system")).Select((PyDict m) => Py.S(m.Get("content"))));

	public List<string> UserTexts => Messages.Where((PyDict m) => Py.Eq(m.Get("role"), "user")).Select((PyDict m) => Py.S(m.Get("content"))).ToList();

	public string LastUserText => UserTexts.Count == 0 ? "" : UserTexts[UserTexts.Count - 1];

	/// <summary>本轮喂回模型的工具结果文本(顺序 = 请求里的顺序)。</summary>
	public List<string> ToolResults => Messages.Where((PyDict m) => Py.Eq(m.Get("role"), "tool")).Select((PyDict m) => Py.S(m.Get("content"))).ToList();

	public override string ToString()
	{
		return "#" + Index + (Stream ? " 流式" : " 非流式") + " model=" + Model + " 工具=" + ((ToolSchemas == null) ? "(无)" : ToolNames.Count.ToString()) + " 消息=" + Messages.Count;
	}
}

/// <summary>
/// 按脚本依次作答的假模型:挂到 Client.TestTransport 之后,ChatStream / ChatOnce 都不再发真实 HTTP。
/// 每次请求都会记进 <see cref="Requests" />(含 messages、tools、model),用来断言请求序列。
/// </summary>
public sealed class FakeModel
{
	private readonly List<Func<ModelRequest, ModelReply>> _steps = new List<Func<ModelRequest, ModelReply>>();

	public readonly List<ModelRequest> Requests = new List<ModelRequest>();

	/// <summary>脚本消费到的位置(= 已消费步数)。</summary>
	public int Cursor;

	/// <summary>摘要请求(Agent._summarize 的 ChatOnce,不带 tools)自动作答,不占脚本步。</summary>
	public bool AutoSummarize = true;

	public string SummaryText = "【摘要】更早的对话";

	/// <summary>脚本用尽后自己接招(默认 null → 直接抛异常,脚本写错当场暴露)。</summary>
	public Func<ModelRequest, ModelReply> OnUnscripted;

	/// <summary>追加一步:收到下一次请求时按这个回复作答。</summary>
	public FakeModel Then(ModelReply reply)
	{
		_steps.Add(delegate
		{
			return reply;
		});
		return this;
	}

	/// <summary>追加一步(带前置检查:不满足就报错,避免脚本错位)。</summary>
	public FakeModel Then(Func<ModelRequest, ModelReply> reply, Func<ModelRequest, bool> when = null, string note = null)
	{
		_steps.Add(delegate(ModelRequest req)
		{
			if (when != null && !when(req))
			{
				throw new InvalidOperationException("假模型脚本第 " + _steps.Count + " 步的前置条件没满足" + ((note == null) ? "" : ("(" + note + ")")) + ":" + req);
			}
			return reply(req);
		});
		return this;
	}

	/// <summary>重复 N 次同一回复(比如连续 3 次 429)。</summary>
	public FakeModel Repeat(int times, ModelReply reply)
	{
		for (int i = 0; i < times; i++)
		{
			Then(reply);
		}
		return this;
	}

	public int StepCount => _steps.Count;

	/// <summary>假传输本体(签名与 Client.TestTransport 一致)。</summary>
	public AiResponse Serve(PyDict endpoint, PyDict body, bool stream)
	{
		ModelRequest req = new ModelRequest
		{
			Endpoint = endpoint,
			Body = body,
			Stream = stream,
			Index = Requests.Count + 1
		};
		ModelReply reply;
		// 并行工具组会在别的线程上再打模型(例如同一回合派两个子代理),记账要加锁
		lock (_gate)
		{
			Requests.Add(req);
			if (req.IsSummarizer && AutoSummarize)
			{
				reply = ModelReply.NonStream(SummaryText);
			}
			else if (Cursor < _steps.Count)
			{
				reply = _steps[Cursor++](req);
			}
			else if (OnUnscripted != null)
			{
				reply = OnUnscripted(req);
			}
			else
			{
				throw new InvalidOperationException("假模型脚本只有 " + _steps.Count + " 步,第 " + req.Index + " 次请求没人接:" + req);
			}
		}
		return Build(reply, stream);
	}

	private readonly object _gate = new object();

	private static AiResponse Build(ModelReply reply, bool stream)
	{
		if (reply.Status >= 400)
		{
			return new AiResponse
			{
				StatusCode = reply.Status,
				Body = new MemoryStream(Encoding.UTF8.GetBytes(reply.BuildJsonBody())),
				ReadTimeoutMs = 10000
			};
		}
		string text = (stream ? reply.BuildStreamBody() : reply.BuildJsonBody());
		byte[] bytes = Encoding.UTF8.GetBytes(text);
		Stream body = (reply.BreakMidStream ? new BreakAfterStream(bytes) : new MemoryStream(bytes));
		return new AiResponse
		{
			StatusCode = 200,
			Body = body,
			ReadTimeoutMs = 10000
		};
	}

	/// <summary>吐完脚本给的字节后抛 IOException:模拟流被上游掐断(AiResponse.IterLines → AiStreamBroken)。</summary>
	private sealed class BreakAfterStream : Stream
	{
		private readonly MemoryStream _inner;

		public BreakAfterStream(byte[] data)
		{
			_inner = new MemoryStream(data);
		}

		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get
			{
				throw new NotSupportedException();
			}
			set
			{
				throw new NotSupportedException();
			}
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			int n = _inner.Read(buffer, offset, count);
			if (n > 0)
			{
				return n;
			}
			throw new IOException("假模型脚本:流被掐断");
		}

		public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			try
			{
				return Task.FromResult(Read(buffer, offset, count));
			}
			catch (Exception ex)
			{
				return Task.FromException<int>(ex);
			}
		}

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}
	}
}
