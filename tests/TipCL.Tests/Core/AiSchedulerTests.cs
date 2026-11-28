// AI-engine-mcp:只读工具并行调度(Scheduler)的回归 —— 分组、顺序、并发上限、异常隔离、取消,
// 以及会话循环里「事件 / 对话回填按原 tool_call 顺序」的端到端用例(本机假网关,不联网)。
using System;
using System.Collections.Generic;
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

public class AiSchedulerTests
{
    private static ToolCall C(string id, string name) => new(id, name, new PyDict());

    private static string Shape(IEnumerable<List<ToolCall>> groups)
        => string.Join("|", groups.Select(g => string.Join(",", g.Select(c => c.id))));

    // ───────────────────────── 分组 ─────────────────────────

    [Fact]
    public void Group_MergesAdjacentReadOnly_AndIsolatesWritesDeletesLaunchAsk()
    {
        var calls = new List<ToolCall>
        {
            C("1", "list_mods"), C("2", "search_mods"), C("3", "get_java_list"),
            C("4", "install_mod"),
            C("5", "list_instances"), C("6", "read_mod_config"),
            C("7", "delete_mod"),
            C("8", "launch_game"),
            C("9", "ask_user"),
            C("10", "scan_mod_conflicts"),
        };
        List<List<ToolCall>> groups = Scheduler.Group(calls);
        Assert.Equal("1,2,3|4|5,6|7|8|9|10", Shape(groups));
        // 拼回去与原顺序逐个相同(结果回填靠它)
        Assert.Equal(calls.Select(c => c.id), groups.SelectMany(g => g).Select(c => c.id));
    }

    [Fact]
    public void Group_CapsEachParallelGroupAtMaxConcurrency()
    {
        var calls = Enumerable.Range(1, 6).Select(i => C(i.ToString(), "list_mods")).ToList();
        Assert.Equal("1,2,3,4|5,6", Shape(Scheduler.Group(calls)));
        Assert.Equal("1,2|3,4|5,6", Shape(Scheduler.Group(calls, 2)));
        // 上限 ≤ 0 按 1 处理(退化成完全串行),不死循环
        Assert.Equal("1|2|3|4|5|6", Shape(Scheduler.Group(calls, 0)));
    }

    [Fact]
    public void Group_UnknownAndExternalMcpTools_AreSerial()
    {
        var calls = new List<ToolCall> { C("a", "list_mods"), C("b", "mcp_files_read"), C("c", "no_such_tool"), C("d", "list_mods") };
        Assert.Equal("a|b|c|d", Shape(Scheduler.Group(calls)));
        Assert.False(Scheduler.IsParallelizable(C("x", "mcp_files_read")));
        Assert.False(Scheduler.IsExclusive(C("x", "mcp_files_read")));
    }

    [Fact]
    public void Classification_IsDerivedFromToolMeta_ForEveryTool()
    {
        // 判定只看 ToolMeta(别的包新增工具时不用改 Scheduler):逐个工具按公式核对
        var sessionState = new HashSet<string> { "update_plan", "remember", "forget", "dispatch_subagent" };
        foreach (ToolMetaInfo meta in ToolMeta.ALL)
        {
            ToolCall call = C("x", meta.name);
            bool exclusive = meta.name == "ask_user" || meta.side_effect is "delete" or "launch";
            bool parallel = !exclusive && meta.@readonly && !meta.long_running
                && meta.side_effect is "none" or "read" or "network" && !sessionState.Contains(meta.name);
            Assert.True(exclusive == Scheduler.IsExclusive(call), meta.name + " 的独占判定不对");
            Assert.True(parallel == Scheduler.IsParallelizable(call), meta.name + " 的并行判定不对");
        }
        // 写类工具一个都不能并行
        Assert.DoesNotContain(ToolMeta.ALL, m => !m.@readonly && Scheduler.IsParallelizable(C("x", m.name)));
    }

    [Fact]
    public void SessionStateTools_StaySerial_EvenThoughReadOnly()
    {
        var calls = new List<ToolCall> { C("a", "remember"), C("b", "forget"), C("c", "update_plan") };
        Assert.Equal("a|b|c", Shape(Scheduler.Group(calls)));
    }

    // ───────────────────────── 执行 ─────────────────────────

    [Fact]
    public async Task RunGroup_ReturnsResultsInInputOrder_AndActuallyRunsInParallel()
    {
        var group = new List<ToolCall> { C("a", "list_mods"), C("b", "list_mods"), C("c", "list_mods"), C("d", "list_mods") };
        int running = 0;
        int peak = 0;
        string[] results = await Scheduler.RunGroupAsync(group, async call =>
        {
            int now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            // 先进来的睡得最久:如果是串行,结果顺序也对,但峰值并发会是 1
            await Task.Delay(call.id switch { "a" => 300, "b" => 200, "c" => 100, _ => 10 });
            Interlocked.Decrement(ref running);
            return "r-" + call.id;
        });
        Assert.Equal(new[] { "r-a", "r-b", "r-c", "r-d" }, results);
        Assert.True(peak >= 2, "组内没有并发执行(峰值 " + peak + ")");
        Assert.True(peak <= Scheduler.DefaultMaxConcurrency);
    }

    [Fact]
    public async Task RunGroup_RespectsConcurrencyCap()
    {
        var group = Enumerable.Range(0, 8).Select(i => C("c" + i, "list_mods")).ToList();
        int running = 0;
        int peak = 0;
        string[] results = await Scheduler.RunGroupAsync(group, async call =>
        {
            InterlockedMax(ref peak, Interlocked.Increment(ref running));
            await Task.Delay(40);
            Interlocked.Decrement(ref running);
            return call.id;
        }, maxConcurrency: 2);
        Assert.Equal(group.Select(c => c.id), results);
        Assert.True(peak <= 2, "并发超过上限:" + peak);
    }

    [Fact]
    public async Task RunGroup_IsolatesAFailingCall_SiblingsStillSucceed()
    {
        var group = new List<ToolCall> { C("ok1", "list_mods"), C("bad", "list_mods"), C("ok2", "get_java_list") };
        string[] results = await Scheduler.RunGroupAsync(group, async call =>
        {
            await Task.Delay(5);
            if (call.id == "bad")
            {
                throw new InvalidOperationException("调度层意外");
            }
            return "fine-" + call.id;
        });
        Assert.Equal("fine-ok1", results[0]);
        Assert.Equal("fine-ok2", results[2]);
        PyDict err = Py.D(PyJson.Loads(results[1]));
        Assert.False(Py.Truthy(err.Get("ok")));
        Assert.Contains("调度层意外", PyJson.Dumps(err, null, ensureAscii: false));
        Assert.Equal(ToolCallStatus.FAILED, group[1].status);
        // 单个调用的组同样隔离(不走并行分支)
        string[] single = await Scheduler.RunGroupAsync(new[] { C("s", "install_mod") }, _ => throw new ValueError("坏参数"));
        Assert.Contains("坏参数", PyJson.Dumps(PyJson.Loads(single[0]), null, ensureAscii: false));
    }

    [Fact]
    public async Task RunGroup_PropagatesCancellation_AfterGroupSettles()
    {
        var group = new List<ToolCall> { C("a", "list_mods"), C("b", "list_mods") };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Scheduler.RunGroupAsync(group, async call =>
        {
            await Task.Delay(5);
            if (call.id == "b")
            {
                throw new OperationCanceledException();
            }
            return call.id;
        }));
    }

    [Fact]
    public async Task RunGroupsAsync_MapsResultsById_LikeTheReference()
    {
        var calls = new List<ToolCall> { C("x", "list_mods"), C("y", "install_mod"), C("z", "list_mods") };
        Dictionary<string, string> map = await Scheduler.RunGroupsAsync(Scheduler.Group(calls), c => Task.FromResult(c.name + ":" + c.id));
        Assert.Equal(3, map.Count);
        Assert.Equal("install_mod:y", map["y"]);
        Assert.Equal("list_mods:z", map["z"]);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while (value > (seen = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, seen) == seen)
            {
                return;
            }
        }
    }
}

/// <summary>
/// 会话循环端到端:假网关第一轮回 4 个只读 + 1 个写 + 1 个参数坏掉的调用,第二轮回一句话。
/// 断言:只读那几个真的并发跑了;tool / tool_done 事件与对话里的 tool 消息都按原 tool_call 顺序、一个不丢。
/// </summary>
[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class AiAgentParallelToolsTests : IDisposable
{
    private readonly SequencedSseServer _server;
    private readonly Dictionary<string, object?> _before = new();
    private static readonly string[] Keys = { "ai_mode", "ai_base_url", "ai_model", "ai_api_key", "ai_permission_mode", "ai_permission_rules" };

    public AiAgentParallelToolsTests()
    {
        foreach (string k in Keys)
        {
            _before[k] = Config.CONFIG.Get(k);
        }
        string round1 =
            Sse(ToolCallDelta(0, "a", "list_mods", "{\\\"instance\\\":\\\"default\\\"}")) +
            Sse(ToolCallDelta(1, "b", "get_java_list", "{}")) +
            Sse(ToolCallDelta(2, "c", "search_versions", "{\\\"query\\\":\\\"1.20\\\"}")) +
            Sse(ToolCallDelta(3, "d", "list_instances", "{}")) +
            Sse(ToolCallDelta(4, "e", "install_mod", "{\\\"name\\\":\\\"sodium\\\"}")) +
            Sse(ToolCallDelta(5, "f", "list_mods", "[1,2]")) +
            Sse("{\"choices\":[{\"delta\":{},\"finish_reason\":\"tool_calls\"}]}") +
            "data: [DONE]\n";
        string round2 =
            Sse("{\"choices\":[{\"delta\":{\"content\":\"查完了\"}}]}") +
            Sse("{\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}") +
            "data: [DONE]\n";
        _server = new SequencedSseServer(round1, round2);
        Config.CONFIG.Set("ai_mode", "custom");
        Config.CONFIG.Set("ai_base_url", _server.BaseUrl);
        Config.CONFIG.Set("ai_model", "parallel-test-model");
        Config.CONFIG.Set("ai_api_key", "test-key");
        Config.CONFIG.Set("ai_permission_mode", "default");
        Config.CONFIG.Set("ai_permission_rules", new List<object>());
    }

    public void Dispose()
    {
        foreach (string k in Keys)
        {
            object? v = _before[k];
            Config.CONFIG.Set(k, v ?? (k == "ai_permission_rules" ? new List<object>() : ""));
        }
        _server.Dispose();
    }

    private static string Sse(string json) => "data: " + json + "\n";

    private static string ToolCallDelta(int index, string id, string name, string argsEscaped)
        => "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":" + index + ",\"id\":\"" + id + "\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":\"" + argsEscaped + "\"}}]}}]}";

    /// <summary>慢吞吞的只读门面:记录并发峰值与调用到的方法;写工具一律不该走到这里(没接确认回调)。</summary>
    private sealed class SlowBackend : ToolRunner.IBackend
    {
        public int Running;
        public int Peak;
        public readonly List<string> Methods = new();

        public async Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
        {
            lock (Methods)
            {
                Methods.Add(method);
            }
            int now = Interlocked.Increment(ref Running);
            int seen;
            while (now > (seen = Volatile.Read(ref Peak)) && Interlocked.CompareExchange(ref Peak, now, seen) != seen)
            {
            }
            // 越靠前的调用睡得越久:串行时顺序也对,所以顺序断言之外还要看并发峰值
            int delay = method switch
            {
                "get_installed_mod_entries" => 400,
                "get_java_list" => 250,
                "get_version_list" => 120,
                _ => 20,
            };
            await Task.Delay(delay, ct).ConfigureAwait(false);
            Interlocked.Decrement(ref Running);
            object list = new List<object?> { method };
            return (T?)list;
        }
    }

    [Fact]
    public async Task ReadOnlyCallsRunInParallel_EventsAndToolMessagesKeepOriginalOrder()
    {
        var backend = new SlowBackend();
        var events = new List<AiAgentEvent>();
        var agent = new AiAgent(new ToolRunner(backend) { PermissionMode = "default" }, ev =>
        {
            lock (events)
            {
                events.Add(ev);
            }
        })
        {
            ChatId = "parallel-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            MaxRounds = 3,
        };
        var messages = new List<object> { PyDict.Of("role", "user", "content", "帮我看看模组和 Java,再装个钠") };

        await agent.RunAsync(messages, "default");

        // AI5-A6:这一轮**执行过工具**再收尾,stop_reason 现在是 completed(不再是 no_tool_call)。
        // 判定口径:一句工具都没调过才叫 no_tool_call;调过就是正常完成的收尾(与参考版把
        // no_tool_call 归一成 completed 一致)。纯文字回复仍然是 no_tool_call,见 AiAgentLoopTests。
        Assert.Equal(StopReason.COMPLETED, agent.stop_reason);
        Assert.Equal("查完了", agent.LastText);
        Assert.True(backend.Peak >= 2, "只读调用没有并发(峰值 " + backend.Peak + ")");
        // 写工具没接确认回调 ⇒ 被拒,门面上不该出现 install_mod;参数坏掉的那个也不该执行
        Assert.DoesNotContain("install_mod", backend.Methods);
        Assert.Equal(1, backend.Methods.Count(m => m == "get_installed_mod_entries"));

        string[] ids = { "a", "b", "c", "d", "e", "f" };
        List<string> toolEvents = events.Where(e => e.Kind == "tool").Select(e => e.Call!.id).ToList();
        List<string> doneEvents = events.Where(e => e.Kind == "tool_done").Select(e => e.Call!.id).ToList();
        Assert.Equal(ids, toolEvents);
        Assert.Equal(ids, doneEvents);
        // 每个调用的 tool 事件都在它自己的 tool_done 之前;并行组(a-d)先全部「准备」再依次「完成」
        List<string> stream = events.Where(e => e.Kind is "tool" or "tool_done").Select(e => e.Kind + ":" + e.Call!.id).ToList();
        Assert.Equal(new[]
        {
            "tool:a", "tool:b", "tool:c", "tool:d", "tool_done:a", "tool_done:b", "tool_done:c", "tool_done:d",
            "tool:e", "tool_done:e", "tool:f", "tool_done:f",
        }, stream);

        List<PyDict> toolMessages = messages.OfType<PyDict>().Where(m => Py.S(m.Get("role")) == "tool").ToList();
        Assert.Equal(ids, toolMessages.Select(m => Py.S(m.Get("tool_call_id"))));
        Assert.Contains("get_installed_mod_entries", Py.S(toolMessages[0].Get("content")));
        Assert.Contains("get_java_list", Py.S(toolMessages[1].Get("content")));
        Assert.Contains("permission_denied", Py.S(toolMessages[4].Get("content")));
        Assert.Contains("bad_arguments", Py.S(toolMessages[5].Get("content")));
        Assert.Equal(2, _server.Requests);
    }

    [Fact]
    public async Task MaxToolConcurrencyOne_FallsBackToSerial()
    {
        var backend = new SlowBackend();
        var agent = new AiAgent(new ToolRunner(backend) { PermissionMode = "default" }, _ => { })
        {
            ChatId = "serial-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            MaxRounds = 3,
            MaxToolConcurrency = 1,
        };
        var messages = new List<object> { PyDict.Of("role", "user", "content", "看看模组") };
        await agent.RunAsync(messages, "default");
        Assert.Equal(1, backend.Peak);
        Assert.Equal(new[] { "a", "b", "c", "d", "e", "f" },
            messages.OfType<PyDict>().Where(m => Py.S(m.Get("role")) == "tool").Select(m => Py.S(m.Get("tool_call_id"))));
    }
}

/// <summary>本机假网关:第 N 个请求回第 N 段 SSE(用完重复最后一段)。TcpListener 手写 HTTP,端口由系统分配。</summary>
internal sealed class SequencedSseServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly string[] _bodies;
    private int _requests;

    public string BaseUrl { get; }

    public int Requests => Volatile.Read(ref _requests);

    public SequencedSseServer(params string[] bodies)
    {
        _bodies = bodies;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        _ = Task.Run(AcceptLoopAsync);
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
                var chunk = new byte[8192];
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
                int index = Interlocked.Increment(ref _requests) - 1;
                string body = _bodies[Math.Min(index, _bodies.Length - 1)];
                byte[] payload = Encoding.UTF8.GetBytes(body);
                string head = "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\n" +
                              $"Content-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(head), _cts.Token).ConfigureAwait(false);
                await stream.WriteAsync(payload, _cts.Token).ConfigureAwait(false);
                await stream.FlushAsync(_cts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
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
