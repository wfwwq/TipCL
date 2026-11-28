// M4-H R7:AI 请求的限流 / 5xx 重试回归(全部走本机假网关,不联网)。
//
// 参考版(Windows 侧 Agent.cs)本来就有这段:429 / 500 / 502 / 503 / 504 以及分类为 rate_limited 的失败,
// 在这一轮还没吐出任何内容时退避重发,最多 3 次;Linux 侧原先漏了,撞上限流直接把错误抛给用户。
// 这里锁死四件事:①限流一次后成功 ②连续限流按上限失败 ③401 一次都不重试 ④退避期间取消要立刻断。
// 另加两条:503 同样重试、已经吐出正文就绝不重发(否则用户会看到重复内容)。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class AiRetryTests : IDisposable
{
    private readonly ScriptedChatServer _server = new();
    private readonly string _modeBefore;
    private readonly string _urlBefore;
    private readonly string _modelBefore;
    private readonly string _keyBefore;

    public AiRetryTests()
    {
        // 把「设置 → AI 助手」临时指向本机假网关(用完还原,别影响别的用例)
        _modeBefore = Py.S(Config.CONFIG.Get("ai_mode", ""));
        _urlBefore = Py.S(Config.CONFIG.Get("ai_base_url", ""));
        _modelBefore = Py.S(Config.CONFIG.Get("ai_model", ""));
        _keyBefore = Py.S(Config.CONFIG.Get("ai_api_key", ""));
        Config.CONFIG.Set("ai_mode", "custom");
        Config.CONFIG.Set("ai_base_url", _server.BaseUrl);
        Config.CONFIG.Set("ai_model", "retry-test-model");
        Config.CONFIG.Set("ai_api_key", "test-key");
    }

    public void Dispose()
    {
        Config.CONFIG.Set("ai_mode", _modeBefore);
        Config.CONFIG.Set("ai_base_url", _urlBefore);
        Config.CONFIG.Set("ai_model", _modelBefore);
        Config.CONFIG.Set("ai_api_key", _keyBefore);
        _server.Dispose();
    }

    // ————— 用例 —————

    [Fact]
    public async Task RateLimitedOnce_ThenSucceeds_RetriesAndKeepsTheAnswer()
    {
        _server.ScriptStatuses(429, 200);

        (AiAgent agent, List<AiAgentEvent> events) = await RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, _server.Requests);                      // 第一次 429、第二次 200:正好发两次
        Assert.Equal(StopReason.NO_TOOL_CALL, agent.stop_reason);
        Assert.Equal("你好", agent.LastText);
        Assert.Equal("你好", string.Concat(events.Where(e => e.Kind == "delta").Select(e => e.Text)));
        Assert.DoesNotContain(events, e => e.Kind == "error");
        // 重试要看得见:状态行有提示,会话事件流里也留一条(排查 / Rewind 都读它)
        Assert.Contains(events, e => e.Kind == "status" && e.Text.Contains("重试"));
        Assert.Contains(Store.ReadEvents(agent.ChatId), e => Py.S(e.Get("event")) == "ModelRetry");
    }

    [Fact]
    public async Task RateLimitedForever_FailsAfterRetryCap()
    {
        _server.ScriptStatuses(429);

        (AiAgent agent, List<AiAgentEvent> events) = await RunOnceAsync(CancellationToken.None);

        Assert.Equal(4, _server.Requests);                      // 首次 + 上限 3 次重试,不再多打
        Assert.Equal(3, events.Count(e => e.Kind == "status" && e.Text.Contains("重试")));
        // 收尾时 Agent 还会补一条 stream_failed 的事件,这里只看真正带上游错误的那条
        AiAgentEvent error = Assert.Single(events, e => e.Kind == "error" && e.Error.Contains("429"));
        Assert.NotEqual("", error.Error);
        // 最终仍按「限流」失败:AIClientError 的 Status / Category 认得出来
        var err = new AIClientError(error.Error, 429);
        Assert.Equal("rate_limited", err.Category);
        Assert.True(err.Retryable());
        Assert.Equal(StopReason.STREAM_FAILED, agent.stop_reason);
    }

    [Fact]
    public async Task Unauthorized_DoesNotRetryAtAll()
    {
        _server.ScriptStatuses(401);

        (AiAgent _agent, List<AiAgentEvent> events) = await RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, _server.Requests);                      // 认证类错误一次都不重发
        AiAgentEvent error = Assert.Single(events, e => e.Kind == "error" && e.Error.Contains("401"));
        Assert.Contains("401", error.Error);
        Assert.DoesNotContain(events, e => e.Kind == "status" && e.Text.Contains("重试"));
    }

    [Fact]
    public async Task BadGateway503_IsRetriedToo()
    {
        _server.ScriptStatuses(503, 200);

        (AiAgent agent, List<AiAgentEvent> _events) = await RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, _server.Requests);
        Assert.Equal(StopReason.NO_TOOL_CALL, agent.stop_reason);
        Assert.Equal("你好", agent.LastText);
    }

    [Fact]
    public async Task ContentAlreadyStreamed_IsNeverResent()
    {
        // 第一次就吐了正文、随后才报限流:已经给用户看过内容了,重发会出现重复正文
        _server.ScriptSse(
            "data: {\"choices\":[{\"delta\":{\"content\":\"前半段\"}}]}\n" +
            "data: {\"error\":{\"message\":\"rate limit exceeded\"}}\n");

        (AiAgent agent, List<AiAgentEvent> events) = await RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, _server.Requests);
        Assert.Equal("前半段", string.Concat(events.Where(e => e.Kind == "delta").Select(e => e.Text)));
        Assert.Single(events, e => e.Kind == "error" && e.Error.Contains("rate limit"));
        Assert.DoesNotContain(events, e => e.Kind == "status" && e.Text.Contains("重试"));
        Assert.Equal(StopReason.STREAM_FAILED, agent.stop_reason);
    }

    [Fact]
    public async Task CancelDuringBackoff_ThrowsImmediately()
    {
        _server.ScriptStatuses(429, 200);
        using var cts = new CancellationTokenSource();
        using var retryNoticeSeen = new ManualResetEventSlim(false);
        var agent = NewAgent(ev =>
        {
            if (ev.Kind == "status" && ev.Text.Contains("重试"))
            {
                retryNoticeSeen.Set();
            }
        });
        var messages = new List<object> { PyDict.Of("role", "user", "content", "你好") };

        Task run = agent.RunAsync(messages, "default", cts.Token);

        // 等到「正在重试」出现 = 已经进入退避等待(第一次退避最少 0.8 秒),这时才取消
        Assert.True(retryNoticeSeen.Wait(TimeSpan.FromSeconds(20)), "没等到重试通知,界面会像卡住一样");
        var watch = Stopwatch.StartNew();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        watch.Stop();

        Assert.True(watch.ElapsedMilliseconds < 700, $"取消后又等了 {watch.ElapsedMilliseconds} ms(退避本来至少 0.8 秒)");
        Assert.Equal(1, _server.Requests);                      // 取消后不再发第二次
        Assert.Equal(StopReason.CANCELLED, agent.stop_reason);
    }

    // ————— 脚手架 —————

    /// <summary>不执行任何工具的假门面(这些用例只跑「说一句话」这一轮)。</summary>
    private sealed class NoToolBackend : ToolRunner.IBackend
    {
        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
        {
            throw new NotImplementedException("重试用例不应该真的执行工具:" + method);
        }
    }

    private static AiAgent NewAgent(Action<AiAgentEvent> emit)
    {
        return new AiAgent(new ToolRunner(new NoToolBackend()), emit)
        {
            ChatId = "retry-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            ToolsEnabled = false,
            MaxRounds = 1,
        };
    }

    private static async Task<(AiAgent Agent, List<AiAgentEvent> Events)> RunOnceAsync(CancellationToken ct)
    {
        var events = new List<AiAgentEvent>();
        AiAgent agent = NewAgent(ev =>
        {
            lock (events)
            {
                events.Add(ev);
            }
        });
        var messages = new List<object> { PyDict.Of("role", "user", "content", "你好") };
        await agent.RunAsync(messages, "default", ct).ConfigureAwait(false);
        return (agent, events);
    }
}

/// <summary>
/// 本机假网关:TcpListener 手写 HTTP(免 Windows 的 HttpListener URL ACL),按脚本逐个请求应答。
/// 非 200 回 OpenAI 风格的错误体;200 回一段最小 SSE(一格正文 + finish_reason + [DONE]),写完关连接。
/// AI5-A/S:再加两样 —— 1 记录每个请求的「流式还是非流式 / model / 下发的工具名」,
/// 兜底与降级用例靠它断言「兜底请求用的是本轮流式那份工具表」「降级后 model 换了」;
/// 2 允许按请求逐条脚本化应答(SSE 正文或非流式 JSON),A4 的「溢出一次后成功」需要它。
/// </summary>
internal sealed class ScriptedChatServer : IDisposable
{
    private const string DefaultSse =
        "data: {\"choices\":[{\"delta\":{\"content\":\"你好\"}}]}\n" +
        "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n" +
        "data: [DONE]\n";

    // 上游限流时真实网关就是这么回的(状态码 429 + 这段文案)
    private const string RateLimitedBody =
        "{\"error\":{\"message\":\"当前分组上游负载已饱和,请稍后重试(rate limit exceeded)\",\"type\":\"rate_limit_error\"}}";

    private const string UnauthorizedBody =
        "{\"error\":{\"message\":\"令牌无效(unauthorized)\",\"type\":\"invalid_request_error\"}}";

    /// <summary>一次请求的可观测快照(AI5-A/S 的断言靠它)。</summary>
    internal sealed class RequestRecord
    {
        /// <summary>请求体是不是带 "stream":true 的那条路(兜底走非流式)。</summary>
        public bool Streaming;

        public string Model = "";

        /// <summary>本次请求下发的工具名(按请求体顺序;空 = 没带工具)。</summary>
        public List<string> Tools = new();
    }

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly List<(int Status, string? Body)> _scripted = new();
    private readonly List<RequestRecord> _records = new();
    private int _index;
    private int _requests;

    public string BaseUrl { get; }

    /// <summary>已经收到的请求数(每次重试都会加一)。</summary>
    public int Requests => Volatile.Read(ref _requests);

    /// <summary>已经收到的请求快照(与 <see cref="Requests"/> 同序)。</summary>
    public List<RequestRecord> Records
    {
        get { lock (_gate) return new List<RequestRecord>(_records); }
    }

    public ScriptedChatServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);   // 端口 0 = 让系统给个空闲端口,避免和别的用例撞
        _listener.Start();
        BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        _ = Task.Run(AcceptLoopAsync);
    }

    /// <summary>按顺序回这些状态码;用完一直重复最后一条(用来测「连续限流超过上限」)。</summary>
    public void ScriptStatuses(params int[] statuses)
    {
        lock (_gate)
        {
            _scripted.Clear();
            foreach (int s in statuses)
            {
                _scripted.Add((s, null));
            }
            _index = 0;
        }
    }

    /// <summary>每个请求都回这段 SSE 正文(200)。</summary>
    public void ScriptSse(string sse)
    {
        lock (_gate)
        {
            _scripted.Clear();
            _scripted.Add((200, sse));
            _index = 0;
        }
    }

    /// <summary>按顺序脚本化应答;用完一直重复最后一条。<paramref name="body"/> 为 SSE 正文(非流式请求会转成 JSON)。</summary>
    public void Script(params (int Status, string? Body)[] responses)
    {
        lock (_gate)
        {
            _scripted.Clear();
            _scripted.AddRange(responses);
            _index = 0;
        }
    }

    private (int Status, string Body) Next(bool streaming)
    {
        lock (_gate)
        {
            if (_scripted.Count == 0)
            {
                return (200, DefaultSse);
            }
            (int status, string? body) = _scripted[Math.Min(_index, _scripted.Count - 1)];
            _index++;
            if (status == 200)
            {
                return (200, body ?? DefaultSse);
            }
            // 脚本给了非 200 的正文就用它(溢出类错误需要自己的文案),否则用限流/认证两套现成的
            if (!string.IsNullOrEmpty(body))
            {
                return (status, body!);
            }
            return (status, status == 401 || status == 403 ? UnauthorizedBody : RateLimitedBody);
        }
    }

    private void Record(string body)
    {
        var record = new RequestRecord
        {
            Streaming = body.Contains("\"stream\":true") || body.Contains("\"stream\": true"),
            Model = JsonField(body, "model"),
        };
        foreach (string name in ToolNames(body))
        {
            record.Tools.Add(name);
        }
        lock (_gate)
        {
            _records.Add(record);
        }
    }

    /// <summary>从请求体里粗取出 model(测试专用,不做完整 JSON 解析)。</summary>
    private static string JsonField(string body, string key)
    {
        string needle = "\"" + key + "\":";
        int at = body.IndexOf(needle, StringComparison.Ordinal);
        if (at < 0)
        {
            needle = "\"" + key + "\": ";
            at = body.IndexOf(needle, StringComparison.Ordinal);
        }
        if (at < 0)
        {
            return "";
        }
        int i = at + needle.Length;
        while (i < body.Length && body[i] == ' ')
        {
            i++;
        }
        if (i >= body.Length || body[i] != '"')
        {
            return "";
        }
        int end = body.IndexOf('"', i + 1);
        return end < 0 ? "" : body.Substring(i + 1, end - i - 1);
    }

    /// <summary>把请求体里 tools 数组的每个 function.name 抠出来(只按出现顺序收集 "name":"...")。</summary>
    private static List<string> ToolNames(string body)
    {
        var names = new List<string>();
        int toolsAt = body.IndexOf("\"tools\":", StringComparison.Ordinal);
        if (toolsAt < 0)
        {
            return names;
        }
        string slice = body.Substring(toolsAt);
        int depth = 0;
        int end = slice.Length;
        for (int i = 0; i < slice.Length; i++)
        {
            if (slice[i] == '[')
            {
                depth++;
            }
            else if (slice[i] == ']')
            {
                depth--;
                if (depth == 0)
                {
                    end = i + 1;
                    break;
                }
            }
        }
        slice = slice.Substring(0, end);
        int at = 0;
        while (true)
        {
            int fn = slice.IndexOf("\"function\":", at, StringComparison.Ordinal);
            if (fn < 0)
            {
                break;
            }
            string name = JsonField(slice.Substring(fn), "name");
            if (name.Length > 0)
            {
                names.Add(name);
            }
            at = fn + 11;
        }
        return names;
    }

    /// <summary>把 SSE 正文折成非流式 JSON 应答(兜底走 ChatOnce 时用)。</summary>
    private static string ToChatOnceJson(string sse)
    {
        var content = new System.Text.StringBuilder();
        string finish = "stop";
        var toolCalls = new List<object>();
        foreach (string raw in sse.Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }
            string payload = line.Substring(5).Trim();
            if (payload.Length == 0 || payload == "[DONE]")
            {
                continue;
            }
            PyDict ev;
            try
            {
                ev = Py.D(PyJson.Loads(payload));
            }
            catch (Exception)
            {
                continue;
            }
            List<object> choices = Py.ListOr(ev.Get("choices"));
            if (choices.Count == 0)
            {
                continue;
            }
            PyDict choice = Py.D(choices[0]);
            PyDict delta = Py.D(choice.Get("delta"));
            content.Append(Py.S(delta.Get("content")));
            foreach (object tc in Py.ListOr(delta.Get("tool_calls")))
            {
                toolCalls.Add(tc);
            }
            string reason = Py.S(choice.Get("finish_reason"));
            if (reason.Length > 0)
            {
                finish = reason;
            }
        }
        return PyJson.Dumps(PyDict.Of(
            "choices", new List<object>
            {
                PyDict.Of(
                    "message", PyDict.Of(
                        "content", content.ToString(),
                        "tool_calls", toolCalls),
                    "finish_reason", finish),
            },
            "usage", PyDict.Of("prompt_tokens", 10L, "completion_tokens", 5L)), null, ensureAscii: false);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        try
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                var buffer = new List<byte>();
                var chunk = new byte[4096];
                int headerEnd = -1;
                while (headerEnd < 0)
                {
                    int n = await stream.ReadAsync(chunk, _cts.Token).ConfigureAwait(false);
                    if (n <= 0)
                    {
                        return;
                    }
                    buffer.AddRange(chunk[..n]);
                    headerEnd = FindHeaderEnd(buffer);
                }

                int contentLength = ContentLength(buffer, headerEnd);
                while (buffer.Count < headerEnd + contentLength)
                {
                    int n = await stream.ReadAsync(chunk, _cts.Token).ConfigureAwait(false);
                    if (n <= 0)
                    {
                        break;
                    }
                    buffer.AddRange(chunk[..n]);
                }

                Interlocked.Increment(ref _requests);
                string requestBody = Encoding.UTF8.GetString(buffer.ToArray(), headerEnd + 1,
                    Math.Max(0, Math.Min(contentLength, buffer.Count - headerEnd - 1)));
                Record(requestBody);
                bool streaming = requestBody.Contains("\"stream\":true") || requestBody.Contains("\"stream\": true");
                (int status, string body) = Next(streaming);
                // 非流式这条(兜底 ChatOnce)期待 JSON,SSE 脚本要折成等价 JSON,否则解析必失败
                if (status == 200 && !streaming && body.StartsWith("data:", StringComparison.Ordinal))
                {
                    body = ToChatOnceJson(body);
                }

                byte[] payload = Encoding.UTF8.GetBytes(body);
                string contentType = status != 200
                    ? "application/json"
                    : streaming ? "text/event-stream" : "application/json";
                string head =
                    $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Error")}\r\n" +
                    "Content-Type: " + contentType + "\r\n" +
                    $"Content-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(head), _cts.Token).ConfigureAwait(false);
                await stream.WriteAsync(payload, _cts.Token).ConfigureAwait(false);
                await stream.FlushAsync(_cts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // 取消 / 客户端主动断开都算正常收尾
        }
    }

    private static int ContentLength(List<byte> buffer, int headerEnd)
    {
        string head = Encoding.UTF8.GetString(buffer.ToArray(), 0, headerEnd);
        foreach (string line in head.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(line.Substring("Content-Length:".Length).Trim(), out int len))
            {
                return len;
            }
        }
        return 0;
    }

    private static int FindHeaderEnd(List<byte> bytes)
    {
        for (int i = 3; i < bytes.Count; i++)
        {
            if (bytes[i - 3] == 13 && bytes[i - 2] == 10 && bytes[i - 1] == 13 && bytes[i] == 10)
            {
                return i + 1;
            }
        }
        return -1;
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch (Exception) { }
        try { _listener.Stop(); } catch (Exception) { }
        _cts.Dispose();
    }
}
