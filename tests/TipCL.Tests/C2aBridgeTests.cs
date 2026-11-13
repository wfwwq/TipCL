using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 测试用的回环假后端:**TcpListener 绑 127.0.0.1:0**(不用 <c>HttpListener</c> —— 它在 VSTest 的 testhost
/// 里必抛"句柄无效",见 D-27),按预置顺序把响应写回去,并把收到的请求原文记下来供断言。
/// 用完必须 Dispose(finally),析构时关掉监听并让接受线程退出。
/// </summary>
internal sealed class FakeBridgeServer : IDisposable
{
    private readonly TcpListener _listener;

    private readonly Thread _thread;

    private readonly Queue<byte[]> _responses;

    private readonly ManualResetEventSlim _served = new ManualResetEventSlim(false);

    private volatile bool _stopped;

    internal FakeBridgeServer(params byte[][] responses)
    {
        _responses = new Queue<byte[]>(responses);
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "FakeBridgeServer" };
        _thread.Start();
    }

    internal int Port { get; }

    internal List<string> Requests { get; } = new List<string>();

    public void Dispose()
    {
        _stopped = true;
        try
        {
            _listener.Stop();
        }
        catch (Exception)
        {
        }

        try
        {
            _thread.Join(2000);
        }
        catch (Exception)
        {
        }

        _served.Dispose();
    }

    /// <summary>等到至少收到 <paramref name="count"/> 个请求(超时即失败,不静默放过)。</summary>
    internal void WaitForRequests(int count, int timeoutMs = 10000)
    {
        int waited = 0;
        while (true)
        {
            lock (Requests)
            {
                if (Requests.Count >= count)
                {
                    return;
                }
            }

            if (waited >= timeoutMs)
            {
                throw new TimeoutException("假后端在 " + timeoutMs + " ms 内只收到 " + Requests.Count + " 个请求");
            }

            Thread.Sleep(10);
            waited += 10;
        }
    }

    internal string RequestAt(int index)
    {
        lock (Requests)
        {
            return Requests[index];
        }
    }

    /// <summary>组一个 HTTP/1.1 响应(默认 Connection: close,让每个请求各开一条连接)。</summary>
    internal static byte[] Response(string body, string contentType = "application/json")
    {
        return ResponseBody(Encoding.UTF8.GetBytes(body), contentType);
    }

    internal static byte[] ResponseBody(byte[] payload, string contentType = "application/json")
    {
        string head =
            "HTTP/1.1 200 OK\r\nContent-Type: " + contentType + "\r\nContent-Length: " + payload.Length +
            "\r\nConnection: close\r\n\r\n";
        byte[] headBytes = Encoding.ASCII.GetBytes(head);
        byte[] result = new byte[headBytes.Length + payload.Length];
        Array.Copy(headBytes, result, headBytes.Length);
        Array.Copy(payload, 0, result, headBytes.Length, payload.Length);
        return result;
    }

    internal static byte[] ErrorResponse(int code, string reason)
    {
        string head = "HTTP/1.1 " + code + " " + reason + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
        return Encoding.ASCII.GetBytes(head);
    }

    private void AcceptLoop()
    {
        while (!_stopped)
        {
            TcpClient client;
            try
            {
                client = _listener.AcceptTcpClient();
            }
            catch (Exception)
            {
                return;
            }

            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    string head = ReadHead(stream);
                    string body = DrainBody(stream, head);
                    lock (Requests)
                    {
                        Requests.Add(head + body);
                    }

                    byte[] response;
                    lock (Requests)
                    {
                        response = (_responses.Count > 0) ? _responses.Dequeue() : ErrorResponse(503, "Service Unavailable");
                    }

                    stream.Write(response, 0, response.Length);
                    stream.Flush();
                    _served.Set();
                }
            }
            catch (Exception)
            {
                // 客户端提前断开:这一条连接不算数,继续接受下一条。
            }
        }
    }

    /// <summary>
    /// 按 Content-Length 把请求体读掉并返回它(没有就返回空串)。必须读:
    /// 否则客户端可能还在写 body 就收到 Connection: close 而报错。
    /// </summary>
    private static string DrainBody(NetworkStream stream, string head)
    {
        int length = 0;
        foreach (string line in head.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(line.Substring("Content-Length:".Length).Trim(), out int parsed))
            {
                length = parsed;
            }
        }

        if (length <= 0)
        {
            return "";
        }

        byte[] buffer = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = stream.Read(buffer, offset, length - offset);
            if (read <= 0)
            {
                break;
            }

            offset += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, offset);
    }

    private static string ReadHead(NetworkStream stream)
    {
        MemoryStream buffer = new MemoryStream();
        byte[] one = new byte[1];
        while (buffer.Length < 65536)
        {
            int read = stream.Read(one, 0, 1);
            if (read <= 0)
            {
                break;
            }

            buffer.WriteByte(one[0]);
            string text = Encoding.ASCII.GetString(buffer.ToArray());
            if (text.EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                return text;
            }
        }

        return Encoding.ASCII.GetString(buffer.ToArray());
    }
}

/// <summary>
/// C2a:<see cref="BridgeHost"/> 里**不需要 BackendApi** 的那部分。
/// 红线(D-23):本类绝不实例化 BridgeHost、绝不调 StartAsync(那会造 BackendApi 并联网)。
/// 只测纯函数,并显式证明"这些调用没有起监听"。
/// </summary>
public class C2aBridgeHostTests
{
    [Fact]
    public void FindRoot_is_the_isolated_test_home()
    {
        // mac 侧 FindRoot = Utils.ResolveRoot()(GOAL 的数据目录约定),隔离进程里 TIPCL_HOME 指向
        // TestHome.Root,所以两者必须相等 —— 这条同时钉住"不再往 %LOCALAPPDATA% 之类的别处落"。
        Assert.Equal(TestHome.Root, BridgeHost.FindRoot());
    }

    [Theory]
    [InlineData("a/b/", "a/b")]
    [InlineData("a/b", "a/b")]
    [InlineData("a/b//", "a/b")]
    [InlineData("/", "/")]
    [InlineData("", "")]
    public void NormalizeRoot_trims_trailing_separators(string input, string expected)
    {
        Assert.Equal(expected, BridgeHost.NormalizeRoot(input));
    }

    [Fact]
    public void NormalizeRoot_returns_null_for_null()
    {
        Assert.Null(BridgeHost.NormalizeRoot(null!));
    }

    [Fact]
    public void New_token_is_64_hex_characters_and_unique()
    {
        string first = BridgeHost.NewToken();
        string second = BridgeHost.NewToken();
        Assert.Equal(64, first.Length);
        Assert.True(first.All(c => "0123456789ABCDEF".Contains(c, StringComparison.Ordinal)), "令牌必须是十六进制:" + first);
        // BridgeServer 的令牌下限是 32 个字符,64 个十六进制字符满足它。
        Assert.True(first.Length >= 32);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Client_base_url_uses_the_bridge_server_loopback_constant()
    {
        Assert.Equal("127.0.0.1", BridgeServer.LOOPBACK_HOST);
        Assert.Equal("http://127.0.0.1:8080/", BridgeHost.ClientBaseUrl(8080));
        Assert.Equal("http://" + BridgeServer.LOOPBACK_HOST + ":1/", BridgeHost.ClientBaseUrl(1));
    }

    [Fact]
    public void Host_side_helpers_do_not_start_any_listener()
    {
        // 找一个当前空闲的端口,调完纯函数之后再绑一次:绑得上就说明这些调用没有起监听。
        int port;
        using (TcpListener probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
        }

        _ = BridgeHost.FindRoot();
        _ = BridgeHost.NewToken();
        string url = BridgeHost.ClientBaseUrl(port);
        Assert.Equal("http://127.0.0.1:" + port + "/", url);

        using TcpListener again = new TcpListener(IPAddress.Loopback, port);
        again.Start();
        Assert.Equal(port, ((IPEndPoint)again.LocalEndpoint).Port);
        again.Stop();
    }
}

/// <summary>
/// C2a:<see cref="BridgeEvent"/> 的反序列化口径与 <see cref="BridgeClient.JsonOpt"/>。
/// 这一条不需要 socket:BridgeClient 与 Core 的后端共用同一套 snake_case 选项。
/// </summary>
public class C2aBridgeEventTests
{
    [Fact]
    public void Token_header_name_matches_the_bridge_server_constant()
    {
        Assert.Equal("X-TipCL-Bridge-Token", BridgeClient.TokenHeader);
        Assert.Equal(BridgeServer.TOKEN_HEADER, BridgeClient.TokenHeader);
    }

    [Fact]
    public void Json_options_are_case_insensitive_snake_case_and_allow_numbers_from_strings()
    {
        Assert.True(BridgeClient.JsonOpt.PropertyNameCaseInsensitive);
        Assert.Equal(JsonNamingPolicy.SnakeCaseLower, BridgeClient.JsonOpt.PropertyNamingPolicy);
        Assert.Equal(System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull, BridgeClient.JsonOpt.DefaultIgnoreCondition);
    }

    [Fact]
    public void Event_payload_deserialises_from_the_snake_case_wire_format()
    {
        const string json =
            "{\"event\":\"progress\",\"task_id\":\"t1\",\"title\":\"T\",\"message\":\"m\",\"text\":\"x\",\"current\":\"3\",\"total\":12,\"count\":1,\"success\":true,\"stopped\":false,\"code\":\"c\",\"uri\":\"u\",\"kind\":\"k\",\"label\":\"l\",\"name\":\"n\",\"payload\":{\"package\":\"p\"},\"crash\":{\"title\":\"boom\"}}";
        BridgeEvent? ev = JsonSerializer.Deserialize<BridgeEvent>(json, BridgeClient.JsonOpt);
        Assert.NotNull(ev);
        Assert.Equal("progress", ev!.Event);
        Assert.Equal("t1", ev.TaskId);
        Assert.Equal("T", ev.Title);
        Assert.Equal("x", ev.Text);
        Assert.Equal(3, ev.Current);
        Assert.Equal(12, ev.Total);
        Assert.Equal(1, ev.Count);
        Assert.True(ev.Success);
        Assert.False(ev.Stopped);
        Assert.Equal("c", ev.Code);
        Assert.Equal("u", ev.Uri);
        Assert.Equal("k", ev.Kind);
        Assert.Equal("l", ev.Label);
        Assert.Equal("n", ev.Name);
        Assert.Equal(JsonValueKind.Object, ev.Payload.ValueKind);
        Assert.NotNull(ev.Crash);
        Assert.Equal("boom", ev.Crash!.Title);
    }

    [Fact]
    public void Event_defaults_are_empty_strings_and_zero()
    {
        BridgeEvent ev = new BridgeEvent();
        Assert.Equal("", ev.Event);
        Assert.Equal("", ev.TaskId);
        Assert.Equal("", ev.Message);
        Assert.Equal("", ev.Code);
        Assert.Equal(0, ev.Current);
        Assert.Equal(0, ev.Total);
        Assert.False(ev.Success);
        Assert.False(ev.Stopped);
        Assert.Null(ev.Crash);
    }

    [Fact]
    public void Bridge_call_exception_keeps_the_method_and_message()
    {
        BridgeCallException ex = new BridgeCallException("do_thing", "boom");
        Assert.Equal("do_thing", ex.Method);
        Assert.Equal("boom", ex.Message);
        Assert.IsAssignableFrom<Exception>(ex);
    }
}

/// <summary>
/// C2a:<see cref="BridgeClient"/> 的请求/响应帧、错误映射、事件流与令牌头。
/// 全程对着 <see cref="FakeBridgeServer"/>(127.0.0.1 上的 TcpListener)跑,绝不碰真后端(D-23)。
/// </summary>
public class C2aBridgeClientTests
{
    private const string Token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData("http://example.com:9/")]
    [InlineData("http://192.168.1.5:9/")]
    [InlineData("https://127.0.0.1:9/")]
    [InlineData("http://127.0.0.1:0/")]
    public void Constructor_rejects_anything_that_is_not_a_loopback_http_url(string url)
    {
        Assert.Throws<ArgumentException>(() => new BridgeClient(new Uri(url), Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short")]
    [InlineData("0123456789abcdef0123456789abcde")]
    public void Constructor_rejects_a_token_shorter_than_32_characters(string token)
    {
        Assert.Throws<ArgumentException>(() => new BridgeClient(new Uri("http://127.0.0.1:9/"), token));
    }

    [Fact]
    public void Constructor_accepts_a_loopback_url_with_a_long_enough_token()
    {
        using BridgeClient client = new BridgeClient(new Uri("http://127.0.0.1:9/"), Token);
        Assert.Equal("http://127.0.0.1:9/", client.BaseUri.ToString());
        Assert.False(client.StreamConnected);
        Assert.Equal("", client.LastStreamError);
    }

    [Fact]
    public async Task Call_sends_the_jsonrpc_envelope_with_the_token_header()
    {
        using FakeBridgeServer server = new FakeBridgeServer(
            FakeBridgeServer.Response("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"ok\":true}}"));
        using BridgeClient client = new BridgeClient(new Uri("http://127.0.0.1:" + server.Port + "/"), Token);

        JsonElement result = await client.CallAsync("get_settings", new { key = "value" });
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.True(result.GetProperty("ok").GetBoolean());

        server.WaitForRequests(1);
        string request = server.RequestAt(0);
        Assert.StartsWith("POST /rpc HTTP/1.1\r\n", request, StringComparison.Ordinal);
        Assert.Contains("X-TipCL-Bridge-Token: " + Token, request, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"method\":\"get_settings\"", request, StringComparison.Ordinal);
        Assert.Contains("\"jsonrpc\":\"2.0\"", request, StringComparison.Ordinal);
        Assert.Contains("\"params\":{\"key\":\"value\"}", request, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Call_maps_an_object_error_to_BridgeCallException()
    {
        using FakeBridgeServer server = new FakeBridgeServer(
            FakeBridgeServer.Response("{\"error\":{\"message\":\"boom\"}}"));
        using BridgeClient client = new BridgeClient(new Uri("http://127.0.0.1:" + server.Port + "/"), Token);

        BridgeCallException ex = await Assert.ThrowsAsync<BridgeCallException>(() => client.CallAsync("explode"));
        Assert.Equal("explode", ex.Method);
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task Call_maps_a_string_error_to_BridgeCallException()
    {
        using FakeBridgeServer server = new FakeBridgeServer(
            FakeBridgeServer.Response("{\"error\":\"plain failure\"}"));
        using BridgeClient client = new BridgeClient(new Uri("http://127.0.0.1:" + server.Port + "/"), Token);

        BridgeCallException ex = await Assert.ThrowsAsync<BridgeCallException>(() => client.CallAsync("explode2"));
        Assert.Equal("plain failure", ex.Message);
    }

    [Fact]
    public async Task Call_without_a_result_yields_an_undefined_element()
    {
        using FakeBridgeServer server = new FakeBridgeServer(FakeBridgeServer.Response("{}"));
        using BridgeClient client = new BridgeClient(new Uri("http://127.0.0.1:" + server.Port + "/"), Token);

        JsonElement result = await client.CallAsync("nothing");
        Assert.Equal(JsonValueKind.Undefined, result.ValueKind);
    }

    [Fact]
    public async Task Call_with_an_empty_body_is_treated_as_an_empty_object()
    {
        using FakeBridgeServer server = new FakeBridgeServer(FakeBridgeServer.Response(""));
        using BridgeClient client = new BridgeClient(new Uri("http://127.0.0.1:" + server.Port + "/"), Token);

        JsonElement result = await client.CallAsync("empty");
        Assert.Equal(JsonValueKind.Undefined, result.ValueKind);
    }

    [Fact]
    public async Task Call_generic_returns_a_string_result_directly()
    {
        using FakeBridgeServer server = new FakeBridgeServer(
            FakeBridgeServer.Response("{\"result\":\"hello\"}"));
        using BridgeClient client = new BridgeClient(new Uri("http://127.0.0.1:" + server.Port + "/"), Token);

        Assert.Equal("hello", await client.CallAsync<string>("greet"));
    }

    [Fact]
    public async Task Call_generic_deserialises_snake_case_and_numbers_from_strings()
    {
        using FakeBridgeServer server = new FakeBridgeServer(
            FakeBridgeServer.Response("{\"result\":{\"task_id\":\"t9\",\"current\":\"7\",\"success\":true}}"));
        using BridgeClient client = new BridgeClient(new Uri("http://127.0.0.1:" + server.Port + "/"), Token);

        // NumberHandling = AllowReadingFromString:JSON 里写成字符串的数字也要能落到 int 属性上。
        BridgeEvent? ev = await client.CallAsync<BridgeEvent>("list");
        Assert.NotNull(ev);
        Assert.Equal("t9", ev!.TaskId);
        Assert.Equal(7, ev.Current);
        Assert.True(ev.Success);
    }

    [Fact]
    public async Task Try_call_swallows_the_error_and_returns_the_fallback()
    {
        using FakeBridgeServer server = new FakeBridgeServer(
            FakeBridgeServer.Response("{\"error\":{\"message\":\"boom\"}}"));
        using BridgeClient client = new BridgeClient(new Uri("http://127.0.0.1:" + server.Port + "/"), Token);

        Assert.Equal("fallback", await client.TryCallAsync("explode", null, "fallback"));
    }

    [Fact]
    public async Task Start_task_returns_the_raw_string_of_a_non_string_result()
    {
        using FakeBridgeServer server = new FakeBridgeServer(
            FakeBridgeServer.Response("{\"result\":{\"id\":\"t1\"}}"));
        using BridgeClient client = new BridgeClient(new Uri("http://127.0.0.1:" + server.Port + "/"), Token);

        // Win 源:不是字符串的 result 直接 ToString()(所以这里拿到的是 JSON 原文)。
        string text = await client.StartTaskAsync("start_install");
        Assert.Contains("\"id\"", text, StringComparison.Ordinal);
        Assert.Contains("t1", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Is_healthy_reports_the_status_code()
    {
        using FakeBridgeServer server = new FakeBridgeServer(
            FakeBridgeServer.Response("{\"ok\":true}"),
            FakeBridgeServer.ErrorResponse(503, "Service Unavailable"));
        using BridgeClient client = new BridgeClient(new Uri("http://127.0.0.1:" + server.Port + "/"), Token);

        Assert.True(await client.IsHealthyAsync());
        Assert.False(await client.IsHealthyAsync());
        server.WaitForRequests(2);
        Assert.StartsWith("GET /health HTTP/1.1", server.RequestAt(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Event_stream_frames_sse_blocks_into_bridge_events()
    {
        byte[] sse = FakeBridgeServer.ResponseBody(
            Encoding.UTF8.GetBytes("event: progress\ndata: {\"task_id\":\"t5\",\"current\":2,\"total\":4}\n\n"),
            "text/event-stream");
        using FakeBridgeServer server = new FakeBridgeServer(sse);
        using BridgeClient client = new BridgeClient(new Uri("http://127.0.0.1:" + server.Port + "/"), Token);

        List<bool> states = new List<bool>();
        BridgeEvent? received = null;
        using ManualResetEventSlim got = new ManualResetEventSlim(false);
        client.StreamStateChanged += (_, connected) =>
        {
            lock (states)
            {
                states.Add(connected);
            }
        };
        client.EventReceived += (_, ev) =>
        {
            received = ev;
            got.Set();
        };

        client.ConnectEvents();
        Assert.True(got.Wait(10000), "10 秒内没有收到 SSE 事件");

        Assert.NotNull(received);
        Assert.Equal("progress", received!.Event);
        Assert.Equal("t5", received.TaskId);
        Assert.Equal(2, received.Current);
        Assert.Equal(4, received.Total);
        Assert.Equal(JsonValueKind.Object, received.Payload.ValueKind);

        server.WaitForRequests(1);
        Assert.StartsWith("GET /events HTTP/1.1", server.RequestAt(0), StringComparison.Ordinal);
        Assert.Contains("X-TipCL-Bridge-Token: " + Token, server.RequestAt(0), StringComparison.OrdinalIgnoreCase);

        lock (states)
        {
            Assert.NotEmpty(states);
            Assert.True(states[0], "事件流建立时必须先报 connected=true");
        }
    }

    [Fact]
    public void Dispose_is_idempotent_and_stops_the_event_stream()
    {
        byte[] sse = FakeBridgeServer.ResponseBody(Encoding.UTF8.GetBytes(": keepalive\n\n"), "text/event-stream");
        FakeBridgeServer server = new FakeBridgeServer(sse);
        try
        {
            BridgeClient client = new BridgeClient(new Uri("http://127.0.0.1:" + server.Port + "/"), Token);
            client.ConnectEvents();
            client.Dispose();
            client.Dispose();
        }
        finally
        {
            server.Dispose();
        }
    }

    [Fact]
    public async Task Requests_go_to_the_configured_loopback_port_only()
    {
        using FakeBridgeServer server = new FakeBridgeServer(FakeBridgeServer.Response("{\"result\":1}"));
        Uri baseUri = new Uri(BridgeHost.ClientBaseUrl(server.Port));
        using BridgeClient client = new BridgeClient(baseUri, Token);
        Assert.Equal("127.0.0.1", client.BaseUri.Host);
        Assert.Equal(server.Port, client.BaseUri.Port);
        await client.CallAsync("ping");
        server.WaitForRequests(1);
        Assert.StartsWith("POST /rpc", server.RequestAt(0), StringComparison.Ordinal);
    }
}
