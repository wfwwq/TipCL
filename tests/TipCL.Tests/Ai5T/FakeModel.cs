// AI5-T:假模型回放测试框架 —— 假模型(脚本 + 请求记录 + SSE 造帧)。
//
// 位置:回放框架的第一层。它只做三件事:
//   1. 按脚本依次「吐」:正文 delta、tool_calls(参数分两片,走 AssembleStream 的增量拼接)、
//      usage、finish_reason(stop / length / tool_calls)、以及三类错误(上下文溢出、可重试的 429/5xx、
//      致命 401)—— 见 FakeReply 的几个工厂方法;
//   2. 记录每次请求**实际收到**的 model / messages / tools(有序工具名),断言「请求序列」靠它;
//   3. 把回复造成 AiResponse(流式 = SSE 行字节、非流式 = JSON 体),交给 Client 里那个注入点。
//
// 假流**不绕开**生产路径:流式仍然经过 Client.AssembleStream 的 SSE 解析,非流式仍然经过
// Client.ChatOnce 的 JSON 解析(注入点见 src/TipCL.Core/Launcher/Ai/Client.cs 的 Client.TestTransport)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;

namespace TipCL.Tests.Ai5T;

/// <summary>脚本里的一次工具调用(名字 + 参数;参数会分两片吐,专门压 AssembleStream 的累加)。</summary>
internal sealed class FakeToolCall
{
    public string Id = "";
    public string Name = "";
    public string Args = "{}";

    public FakeToolCall(string name, string args = "{}", string id = "")
    {
        Name = name;
        Args = args;
        Id = id;
    }

    public static FakeToolCall Of(string name, PyDict args, string id = "")
        => new(name, PyJson.Dumps(args, null, ensureAscii: false), id);
}

/// <summary>脚本里的一次回复(一次请求的应答)。</summary>
internal sealed class FakeReply
{
    /// <summary>助手正文(流式会切成小片吐)。</summary>
    public string Text = "";

    /// <summary>推理内容(reasoning_content;AssembleStream 会包成 &lt;think&gt;…&lt;/think&gt;)。</summary>
    public string? Reasoning;

    /// <summary>本轮要调用的工具(按顺序)。</summary>
    public List<FakeToolCall> Tools = new();

    /// <summary>finish_reason;留空时按「有没有工具调用」自动取 tool_calls / stop。</summary>
    public string? FinishReason;

    /// <summary>usage.total_tokens;null = 不吐 usage(与不返回用量的上游一致)。</summary>
    public long? TotalTokens;

    /// <summary>HTTP 状态码(&gt;=400 时按 HTTP 错误返回)。</summary>
    public int Status;

    /// <summary>流内 error 事件 / 错误体里的 message(上下文溢出、致命错误等走这里)。</summary>
    public string? ErrorMessage;

    /// <summary>故意只吐工具参数的前半段 → 触发「工具参数不完整,正在换一次非流式」那条分支。</summary>
    public bool BrokenArgs;

    /// <summary>流中途断掉(枚举响应体时抛 AiStreamBroken)。</summary>
    public bool BreakMidStream;

    /// <summary>正文(纯文字回复)。</summary>
    public static FakeReply Say(string text) => new() { Text = text };

    /// <summary>一次工具调用。</summary>
    public static FakeReply Call(string name, PyDict args, string text = "", string id = "")
    {
        var reply = new FakeReply { Text = text };
        reply.Tools.Add(FakeToolCall.Of(name, args, id));
        return reply;
    }

    /// <summary>同一轮多次工具调用(顺序即 tool_calls 顺序)。</summary>
    public static FakeReply Calls(params (string Name, PyDict Args)[] calls)
    {
        var reply = new FakeReply();
        int i = 0;
        foreach ((string name, PyDict args) in calls)
        {
            reply.Tools.Add(FakeToolCall.Of(name, args, "call_" + (i + 1)));
            i++;
        }
        return reply;
    }

    /// <summary>存在多个工具调用时按 <paramref name="index"/> 让其中一个的参数不完整(其余正常)。</summary>
    public FakeReply WithBrokenArgs()
    {
        BrokenArgs = true;
        return this;
    }

    /// <summary>错误路径:HTTP 状态码 + 错误体(429 / 5xx / 401 都走这个)。</summary>
    public static FakeReply Http(int status, string message = "")
        => new() { Status = status, ErrorMessage = message.Length > 0 ? message : "HTTP " + status };

    /// <summary>错误路径:流里的 error 事件(上下文溢出、致命错误等)。</summary>
    public static FakeReply Fail(string message) => new() { ErrorMessage = message };

    public FakeReply Usage(long tokens)
    {
        TotalTokens = tokens;
        return this;
    }

    public FakeReply Finish(string reason)
    {
        FinishReason = reason;
        return this;
    }

    /// <summary>流断在半途(读响应体时抛 AiStreamBroken)。</summary>
    public FakeReply Broken()
    {
        BreakMidStream = true;
        return this;
    }

    /// <summary>把同一份回复复制成多份(「连续 N 轮都这样」的脚本更好写)。</summary>
    public FakeReply Clone()
        => new()
        {
            Text = Text,
            Reasoning = Reasoning,
            Tools = Tools.Select(t => new FakeToolCall(t.Name, t.Args, t.Id)).ToList(),
            FinishReason = FinishReason,
            TotalTokens = TotalTokens,
            Status = Status,
            ErrorMessage = ErrorMessage,
            BrokenArgs = BrokenArgs,
            BreakMidStream = BreakMidStream,
        };

    /// <summary>复制成 N 份。</summary>
    public static List<FakeReply> Repeat(int times, FakeReply reply)
        => Enumerable.Range(0, times).Select(_ => reply.Clone()).ToList();
}

/// <summary>假模型每次请求收到的内容(请求序列断言的对象)。</summary>
internal sealed class FakeRequest
{
    /// <summary>第几次请求(从 1 起,流式与非流式一起算)。</summary>
    public int Index;

    public string Url = "";

    /// <summary>true = 流式(ChatStream),false = 非流式(ChatOnce)。</summary>
    public bool Stream;

    public string Model = "";

    /// <summary>本次请求实际发出去的 messages(深拷贝,不会被后续轮次的追加改掉)。</summary>
    public List<object> Messages = new();

    /// <summary>本次请求实际下发的工具名(顺序即 schema 顺序)。</summary>
    public List<string> ToolNames = new();

    public PyDict Body = new();

    /// <summary>系统消息正文(第一条 role=system)。</summary>
    public string SystemText
        => Messages.Where(m => m is PyDict d && Py.Eq(d.Get("role"), "system"))
                   .Select(m => Py.S(((PyDict)m).Get("content")))
                   .FirstOrDefault() ?? "";

    /// <summary>第一条用户消息正文。</summary>
    public string FirstUserText
        => Messages.Where(m => m is PyDict d && Py.Eq(d.Get("role"), "user"))
                   .Select(m => Py.S(((PyDict)m).Get("content")))
                   .FirstOrDefault() ?? "";

    /// <summary>最后一条用户消息正文。</summary>
    public string LastUserText
        => Messages.Where(m => m is PyDict d && Py.Eq(d.Get("role"), "user"))
                   .Select(m => Py.S(((PyDict)m).Get("content")))
                   .LastOrDefault() ?? "";

    /// <summary>消息里的 tool 结果文本(按顺序)。</summary>
    public List<string> ToolResults
        => Messages.Where(m => m is PyDict d && Py.Eq(d.Get("role"), "tool"))
                   .Select(m => Py.S(((PyDict)m).Get("content")))
                   .ToList();

    /// <summary>带某个写工具名字的 schema 在本请求里下发了没有。</summary>
    public bool HasTool(string name) => ToolNames.Contains(name);
}

/// <summary>
/// 假模型:按脚本依次应答,并把每次请求记进 <see cref="Requests"/>。
/// 安装方式见 <see cref="Install"/>;脚本用完还来请求 = 直接抛,免得「少写了一段脚本」被当成通过了。
/// </summary>
internal sealed class FakeModel : IDisposable
{
    private readonly List<FakeReply> _script = new();
    private readonly object _gate = new();
    private int _cursor;

    /// <summary>每次请求(含流式与非流式)的完整快照。</summary>
    public readonly List<FakeRequest> Requests = new();

    /// <summary>请求记录之后的钩子(用来在「请求刚发出、响应还没消费」的时刻动手,例如中途取消)。</summary>
    public Action<FakeRequest>? OnRequest;

    /// <summary>上一次 <see cref="Install"/> 之前的传输(还原用)。</summary>
    private Func<Client.TestRequest, AiResponse>? _saved;

    /// <summary>追加一段脚本(每次请求消费一项)。</summary>
    public FakeModel Script(params FakeReply[] replies)
    {
        lock (_gate)
        {
            _script.AddRange(replies);
        }
        return this;
    }

    /// <summary>追加一段脚本(集合形式)。</summary>
    public FakeModel Script(IEnumerable<FakeReply> replies)
    {
        lock (_gate)
        {
            _script.AddRange(replies);
        }
        return this;
    }

    /// <summary>脚本还剩几项没消费。</summary>
    public int Remaining
    {
        get { lock (_gate) return _script.Count - _cursor; }
    }

    /// <summary>只统计流式请求。</summary>
    public List<FakeRequest> StreamRequests => Requests.Where(r => r.Stream).ToList();

    /// <summary>只统计非流式请求(ChatOnce:摘要 / 兜底 / 子代理结论都走这条)。</summary>
    public List<FakeRequest> OnceRequests => Requests.Where(r => !r.Stream).ToList();

    /// <summary>装上注入点(生产默认为 null;用完必须 <see cref="Dispose"/> 还原)。</summary>
    public FakeModel Install()
    {
        _saved = Client.TestTransport;
        Client.TestTransport = Respond;
        return this;
    }

    /// <summary>注入点本体:记请求、取脚本、造 AiResponse。</summary>
    internal AiResponse Respond(Client.TestRequest req)
    {
        FakeRequest snapshot = Snapshot(req);
        FakeReply reply;
        lock (_gate)
        {
            snapshot.Index = Requests.Count + 1;
            Requests.Add(snapshot);
            if (_cursor >= _script.Count)
            {
                throw new AIClientError("假模型脚本用尽:第 " + snapshot.Index + " 次请求(" +
                    (snapshot.Stream ? "流式" : "非流式") + ")没有对应脚本");
            }
            reply = _script[_cursor];
            _cursor++;
        }
        OnRequest?.Invoke(snapshot);
        return req.Stream ? StreamResponse(reply) : OnceResponse(reply);
    }

    private static FakeRequest Snapshot(Client.TestRequest req)
    {
        PyDict body = req.Body ?? new PyDict();
        var names = new List<string>();
        foreach (object raw in Py.ListOr(body.Get("tools")))
        {
            PyDict function = Py.D(Py.D(raw).Get("function"));
            string name = Py.S(function.Get("name"));
            if (name.Length > 0)
            {
                names.Add(name);
            }
        }
        List<object> messages;
        try
        {
            messages = Py.ListOr(PyJson.Loads(PyJson.Dumps(Py.ListOr(body.Get("messages")), null, ensureAscii: false)));
        }
        catch (Exception)
        {
            messages = new List<object>();
        }
        return new FakeRequest
        {
            Index = 0, // 由 Respond 按 Requests 下标回填
            Url = req.Url,
            Stream = req.Stream,
            Model = Py.S(body.Get("model")),
            Messages = messages,
            ToolNames = names,
            Body = body,
        };
    }

    /// <summary>流式应答:SSE 行字节。</summary>
    private AiResponse StreamResponse(FakeReply reply)
    {
        if (reply.Status >= 400)
        {
            return Raw(reply.Status, PyJson.Dumps(PyDict.Of("error", PyDict.Of("message", reply.ErrorMessage ?? "HTTP " + reply.Status)), null, ensureAscii: false));
        }
        var lines = new List<string>();
        if (reply.ErrorMessage != null)
        {
            // 上游把错误放在流里(HTTP 200 + error 事件):上下文溢出 / 致命错误都这样来
            lines.Add(PyJson.Dumps(PyDict.Of("error", PyDict.Of("message", reply.ErrorMessage)), null, ensureAscii: false));
            return Sse(lines);
        }
        if (reply.Reasoning is { Length: > 0 })
        {
            lines.Add(Delta(PyDict.Of("delta", PyDict.Of("reasoning_content", reply.Reasoning))));
        }
        foreach (string chunk in Chunks(reply.Text, 3))
        {
            lines.Add(Delta(PyDict.Of("delta", PyDict.Of("content", chunk))));
        }
        int index = 0;
        foreach (FakeToolCall call in reply.Tools)
        {
            string id = call.Id.Length > 0 ? call.Id : "call_" + (index + 1);
            string args = reply.BrokenArgs ? Cut(call.Args) : call.Args;
            string head = Cut(args);
            string tail = args.Substring(head.Length);
            var first = PyDict.Of(
                "index", (long)index,
                "id", id,
                "function", PyDict.Of("name", call.Name, "arguments", head));
            lines.Add(Delta(PyDict.Of("delta", PyDict.Of("tool_calls", new List<object> { first }))));
            if (tail.Length > 0)
            {
                var second = PyDict.Of(
                    "index", (long)index,
                    "function", PyDict.Of("arguments", tail));
                lines.Add(Delta(PyDict.Of("delta", PyDict.Of("tool_calls", new List<object> { second }))));
            }
            index++;
        }
        string reason = reply.FinishReason ?? (reply.Tools.Count > 0 ? "tool_calls" : "stop");
        lines.Add(Delta(PyDict.Of("delta", new PyDict(), "finish_reason", reason)));
        if (reply.TotalTokens.HasValue)
        {
            // 真实上游把 usage 放在最后一个 choices 为空的块里(stream_options.include_usage)
            lines.Add(PyJson.Dumps(PyDict.Of(
                "choices", new List<object>(),
                "usage", PyDict.Of("prompt_tokens", reply.TotalTokens.Value / 2,
                                   "completion_tokens", reply.TotalTokens.Value - reply.TotalTokens.Value / 2,
                                   "total_tokens", reply.TotalTokens.Value)), null, ensureAscii: false));
        }
        lines.Add("[DONE]");
        return Sse(lines);
    }

    /// <summary>非流式应答(ChatOnce):OpenAI 兼容的 JSON 体。</summary>
    private AiResponse OnceResponse(FakeReply reply)
    {
        if (reply.Status >= 400)
        {
            return Raw(reply.Status, PyJson.Dumps(PyDict.Of("error", PyDict.Of("message", reply.ErrorMessage ?? "HTTP " + reply.Status)), null, ensureAscii: false));
        }
        var message = PyDict.Of("role", "assistant", "content", reply.Text);
        if (reply.Tools.Count > 0)
        {
            var calls = new List<object>();
            int i = 0;
            foreach (FakeToolCall call in reply.Tools)
            {
                calls.Add(PyDict.Of(
                    "id", call.Id.Length > 0 ? call.Id : "call_" + (i + 1),
                    "type", "function",
                    "function", PyDict.Of("name", call.Name, "arguments", call.Args)));
                i++;
            }
            message["tool_calls"] = calls;
        }
        var choice = PyDict.Of(
            "index", 0L,
            "message", message,
            "finish_reason", reply.FinishReason ?? (reply.Tools.Count > 0 ? "tool_calls" : "stop"));
        var body = PyDict.Of("choices", new List<object> { choice });
        if (reply.TotalTokens.HasValue)
        {
            body["usage"] = PyDict.Of("total_tokens", reply.TotalTokens.Value);
        }
        return Raw(200, PyJson.Dumps(body, null, ensureAscii: false));
    }

    private static string Delta(PyDict choice)
        => PyJson.Dumps(PyDict.Of("choices", new List<object> { choice }), null, ensureAscii: false);

    private static AiResponse Sse(IEnumerable<string> lines)
    {
        var sb = new StringBuilder();
        foreach (string line in lines)
        {
            sb.Append("data: ").Append(line).Append("\n\n");
        }
        return Raw(200, sb.ToString());
    }

    private static AiResponse Raw(int status, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return new AiResponse { StatusCode = status, Body = new MemoryStream(bytes, writable: false) };
    }

    /// <summary>把文本切成小片(压 AssembleStream 的增量拼接,而不是一次性到齐)。</summary>
    private static IEnumerable<string> Chunks(string text, int size)
    {
        for (int i = 0; i < text.Length; i += size)
        {
            yield return text.Substring(i, Math.Min(size, text.Length - i));
        }
    }

    /// <summary>取前半段(按字符),用于制造「工具参数不完整」。</summary>
    private static string Cut(string text) => text.Length <= 1 ? text : text.Substring(0, text.Length / 2);

    public void Dispose()
    {
        Client.TestTransport = _saved;
        _saved = null;
    }
}
