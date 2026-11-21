// AI-engine-mcp:Linux 版本机 MCP Server(照 Windows 版 McpServer.cs)的回归。
//   · 协议面:initialize / tools/list / tools/call / ping / 通知 / 未知方法 / 解析错误 / 批量;
//   · 集成:随机空闲端口起服务 → HTTP POST tools/list → 调一个只读工具 → 外部写操作不弹确认就被拒
//     / 确认后才执行 → 安全口径(Origin、GET、路径)→ 关闭后连不上;
//   · 偏好:mcp_server.json 的 enabled / port(默认关 / 25585,与参考版同名同默认值)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

public class McpServerTests
{
    private sealed class FakeApi : ToolRunner.IBackend
    {
        public readonly List<string> Methods = new();

        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
        {
            lock (Methods)
            {
                Methods.Add(method);
            }
            object result = method == "get_instances"
                ? new List<object?> { PyDict.Of("name", "default"), PyDict.Of("name", "survival") }
                : PyDict.Of("ok", true);
            return Task.FromResult(result is T t ? t : default);
        }
    }

    private static string Rpc(int id, string method, string paramsJson = "{}")
        => "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"" + method + "\",\"params\":" + paramsJson + "}";

    private static JsonElement Parse(string? json)
    {
        Assert.NotNull(json);
        using JsonDocument doc = JsonDocument.Parse(json!);
        return doc.RootElement.Clone();
    }

    private static string ToolText(JsonElement response)
        => response.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString() ?? "";

    private static bool IsError(JsonElement response)
        => response.GetProperty("result").GetProperty("isError").GetBoolean();

    // ───────────────────────── 协议面(不起端口) ─────────────────────────

    [Fact]
    public void Initialize_EchoesClientVersion_OrFallsBackToTheReferenceVersion()
    {
        var server = new McpHttpServer();
        JsonElement mine = Parse(server.Dispatch(Rpc(1, "initialize", "{\"protocolVersion\":\"2025-03-26\"}")));
        Assert.Equal("2025-03-26", mine.GetProperty("result").GetProperty("protocolVersion").GetString());
        JsonElement dflt = Parse(server.Dispatch(Rpc(2, "initialize")));
        JsonElement result = dflt.GetProperty("result");
        Assert.Equal("2025-06-18", result.GetProperty("protocolVersion").GetString());
        Assert.Equal(McpServer.ProtocolVersion, result.GetProperty("protocolVersion").GetString());
        Assert.Equal("tipcl", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.False(result.GetProperty("capabilities").GetProperty("tools").GetProperty("listChanged").GetBoolean());
        Assert.Contains("确认", result.GetProperty("instructions").GetString());
        Assert.Equal(25585, McpServer.DefaultPort);
    }

    [Fact]
    public void ToolsList_ReadsTheLiveSchemaTable_AndHidesLauncherOnlyTools()
    {
        JsonElement resp = Parse(new McpHttpServer().Dispatch(Rpc(3, "tools/list")));
        var tools = resp.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
        var names = tools.Select(t => t.GetProperty("name").GetString() ?? "").ToList();
        // 动态:schema 表里每个对外工具都在,顺序一致;隐藏表里的一个都不在
        var expected = Tools.Names().Where(n => !McpServer.HiddenTools.Contains(n)).ToList();
        Assert.Equal(expected, names);
        foreach (string hidden in new[] { "ask_user", "dispatch_subagent", "update_plan", "remember", "forget" })
        {
            Assert.DoesNotContain(hidden, names);
        }
        foreach (JsonElement t in tools)
        {
            string name = t.GetProperty("name").GetString() ?? "";
            ToolMetaInfo meta = ToolMeta.Get(name)!;
            Assert.Equal("object", t.GetProperty("inputSchema").GetProperty("type").GetString());
            Assert.Equal(meta.@readonly, t.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
            Assert.Equal(meta.side_effect == "delete", t.GetProperty("annotations").GetProperty("destructiveHint").GetBoolean());
            Assert.False(string.IsNullOrEmpty(t.GetProperty("description").GetString()));
        }
    }

    [Fact]
    public void JsonRpcEdges_NotificationsPingUnknownParseErrorBatch()
    {
        var server = new McpHttpServer();
        Assert.Null(server.Dispatch("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"));
        Assert.Equal("{}", Parse(server.Dispatch(Rpc(4, "ping"))).GetProperty("result").GetRawText());
        Assert.Equal(-32601, Parse(server.Dispatch(Rpc(5, "resources/list"))).GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(-32700, Parse(server.Dispatch("{not json")).GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(-32600, Parse(server.Dispatch("[1]")).EnumerateArray().First().GetProperty("error").GetProperty("code").GetInt32());
        JsonElement batch = Parse(server.Dispatch("[" + Rpc(6, "ping") + ",{\"jsonrpc\":\"2.0\",\"method\":\"notifications/x\"}]"));
        Assert.Equal(1, batch.GetArrayLength());
        Assert.Equal(6, batch[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public void ToolsCall_RefusesHiddenUnknownAndNotReady()
    {
        var server = new McpHttpServer();     // 没有执行器工厂 = 内核没就绪
        JsonElement hidden = Parse(server.Dispatch(Rpc(7, "tools/call", "{\"name\":\"ask_user\",\"arguments\":{}}")));
        Assert.True(IsError(hidden));
        Assert.Contains("只能在启动器界面", ToolText(hidden));
        JsonElement unknown = Parse(server.Dispatch(Rpc(8, "tools/call", "{\"name\":\"no_such_tool\"}")));
        Assert.True(IsError(unknown));
        JsonElement notReady = Parse(server.Dispatch(Rpc(9, "tools/call", "{\"name\":\"list_instances\"}")));
        Assert.True(IsError(notReady));
        Assert.Contains("还没就绪", ToolText(notReady));
        JsonElement noName = Parse(server.Dispatch(Rpc(10, "tools/call", "{}")));
        Assert.Contains("缺少工具名", ToolText(noName));
    }

    // ───────────────────────── 集成:真起端口 ─────────────────────────

    [Fact]
    public async Task Http_Start_ListTools_CallReadOnlyTool_GateWrites_ThenStop()
    {
        var api = new FakeApi();
        var confirms = new List<string>();
        bool approve = false;
        using var server = new McpHttpServer
        {
            RunnerFactory = () => new ToolRunner(api)
            {
                PermissionMode = "yolo",     // 档位是全自动也一样:外部写操作必须确认
                Instance = "default",
                Confirm = (call, _, reason, _) =>
                {
                    lock (confirms)
                    {
                        confirms.Add(call.name + "|" + reason);
                    }
                    return Task.FromResult((approve, true));
                },
            },
        };
        Assert.True(server.Start(0), server.LastError);     // 0 = 系统分配随机空闲端口,不和别的代理/用例撞
        int port = server.Port;
        Assert.True(port > 0);
        Assert.Equal($"http://127.0.0.1:{port}/mcp", server.Url);
        var called = new List<string>();
        server.ToolCalled += (name, _) => { lock (called) { called.Add(name); } };

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        async Task<JsonElement> Post(string body)
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using HttpResponseMessage resp = await http.PostAsync(server.Url, content);
            Assert.Equal(200, (int)resp.StatusCode);
            return Parse(await resp.Content.ReadAsStringAsync());
        }

        JsonElement init = await Post(Rpc(1, "initialize", "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"xunit\",\"version\":\"1\"}}"));
        Assert.Equal("2025-06-18", init.GetProperty("result").GetProperty("protocolVersion").GetString());

        // 通知回 202、无正文
        using (var note = new StringContent("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", Encoding.UTF8, "application/json"))
        using (HttpResponseMessage accepted = await http.PostAsync(server.Url, note))
        {
            Assert.Equal(202, (int)accepted.StatusCode);
        }

        JsonElement list = await Post(Rpc(2, "tools/list"));
        var names = list.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains("list_instances", names);
        Assert.Contains("install_mod", names);
        Assert.DoesNotContain("ask_user", names);

        // 只读工具:直接执行,不弹确认
        JsonElement read = await Post(Rpc(3, "tools/call", "{\"name\":\"list_instances\",\"arguments\":{}}"));
        Assert.False(IsError(read));
        Assert.Contains("survival", ToolText(read));
        Assert.Contains("get_instances", api.Methods);
        Assert.Empty(confirms);

        // 写工具:弹确认,用户拒绝 ⇒ isError + permission_denied,门面没被调用
        JsonElement refused = await Post(Rpc(4, "tools/call", "{\"name\":\"enable_mod\",\"arguments\":{\"filename\":\"a.jar\"}}"));
        Assert.True(IsError(refused));
        Assert.Contains("permission_denied", ToolText(refused));
        Assert.DoesNotContain("enable_mod", api.Methods);
        Assert.Equal("enable_mod|" + ToolRunner.ExternalWriteReason, Assert.Single(confirms));

        // 用户同意 ⇒ 才执行;「以后都允许」不生效(下一次照样问)
        approve = true;
        JsonElement ok1 = await Post(Rpc(5, "tools/call", "{\"name\":\"enable_mod\",\"arguments\":{\"filename\":\"a.jar\"}}"));
        JsonElement ok2 = await Post(Rpc(6, "tools/call", "{\"name\":\"enable_mod\",\"arguments\":{\"filename\":\"a.jar\"}}"));
        Assert.False(IsError(ok1));
        Assert.False(IsError(ok2));
        Assert.Equal(2, api.Methods.Count(m => m == "enable_mod"));
        Assert.Equal(3, confirms.Count);
        Assert.Equal(new[] { "list_instances", "enable_mod", "enable_mod", "enable_mod" }, called);

        // 安全口径:外站 Origin 403;GET 405;别的路径 404
        using (var evil = new HttpRequestMessage(HttpMethod.Post, server.Url) { Content = new StringContent(Rpc(7, "tools/list"), Encoding.UTF8, "application/json") })
        {
            evil.Headers.Add("Origin", "https://evil.example");
            using HttpResponseMessage forbidden = await http.SendAsync(evil);
            Assert.Equal(403, (int)forbidden.StatusCode);
        }
        using (var local = new HttpRequestMessage(HttpMethod.Post, server.Url) { Content = new StringContent(Rpc(8, "ping"), Encoding.UTF8, "application/json") })
        {
            local.Headers.Add("Origin", "http://localhost:3000");
            using HttpResponseMessage allowed = await http.SendAsync(local);
            Assert.Equal(200, (int)allowed.StatusCode);
        }
        using (HttpResponseMessage get = await http.GetAsync(server.Url))
        {
            Assert.Equal(405, (int)get.StatusCode);
        }
        using (HttpResponseMessage other = await http.GetAsync($"http://127.0.0.1:{port}/other"))
        {
            Assert.Equal(404, (int)other.StatusCode);
        }

        // 关闭后端口不再接受连接
        server.Stop();
        Assert.False(server.Running);
        using var probe = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(() => probe.ConnectAsync("127.0.0.1", port));
    }

    [Fact]
    public async Task Http_WriteWithoutConfirmCallback_IsDeniedHonestly()
    {
        var api = new FakeApi();
        using var server = new McpHttpServer { RunnerFactory = () => new ToolRunner(api) { PermissionMode = "default" } };
        Assert.True(server.Start(0), server.LastError);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var content = new StringContent(Rpc(1, "tools/call", "{\"name\":\"delete_instance\",\"arguments\":{\"name\":\"default\"}}"), Encoding.UTF8, "application/json");
        using HttpResponseMessage resp = await http.PostAsync(server.Url, content);
        JsonElement body = Parse(await resp.Content.ReadAsStringAsync());
        Assert.True(IsError(body));
        Assert.Contains("permission_denied", ToolText(body));
        Assert.Empty(api.Methods);
    }

    // ───────────────────────── 偏好与门面 ─────────────────────────

    [Fact]
    public void Prefs_FileShapeAndDefaults_MatchTheReference_AndFacadeStartsStops()
    {
        string path = McpServer.PrefPath;
        Assert.Equal(Path.Combine(Utils.ROOT, "mcp_server.json"), path);
        string? saved = File.Exists(path) ? File.ReadAllText(path) : null;
        Func<ToolRunner?>? savedFactory = McpServer.RunnerFactory;
        try
        {
            if (File.Exists(path)) File.Delete(path);
            Assert.Equal((false, 25585), McpServer.LoadPrefs());
            Assert.False(McpServer.Enabled);

            File.WriteAllText(path, "{\"enabled\": false, \"port\": 70000}");   // 越界端口 → 回默认
            Assert.Equal((false, 25585), McpServer.LoadPrefs());

            int port = FreePort();
            Assert.True(McpServer.SetPort(port));
            Assert.False(McpServer.SetPort(0));
            Assert.Equal((false, port), McpServer.LoadPrefs());

            Assert.True(McpServer.SetEnabled(true), McpServer.LastError);
            Assert.True(McpServer.Running);
            Assert.Equal($"http://127.0.0.1:{port}/mcp", McpServer.Url);
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
            {
                Assert.True(doc.RootElement.GetProperty("enabled").GetBoolean());
                Assert.Equal(port, doc.RootElement.GetProperty("port").GetInt32());
            }

            Assert.False(McpServer.SetEnabled(false));
            Assert.False(McpServer.Running);
            Assert.Equal((false, port), McpServer.LoadPrefs());
        }
        finally
        {
            McpServer.Stop();
            McpServer.RunnerFactory = savedFactory;
            if (saved != null) File.WriteAllText(path, saved);
            else if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LooksLikeError_AndLocalOrigin_FollowTheReference()
    {
        Assert.True(McpServer.LooksLikeError("{\"ok\":false,\"error\":\"x\"}"));
        Assert.True(McpServer.LooksLikeError("{\"error\":\"x\"}"));
        Assert.False(McpServer.LooksLikeError("{\"ok\":true}"));
        Assert.False(McpServer.LooksLikeError("[1,2]"));
        Assert.False(McpServer.LooksLikeError("not json"));
        Assert.True(McpServer.IsLocalOrigin(null));
        Assert.True(McpServer.IsLocalOrigin("null"));
        Assert.True(McpServer.IsLocalOrigin("http://127.0.0.1:8080"));
        Assert.True(McpServer.IsLocalOrigin("http://localhost"));
        Assert.False(McpServer.IsLocalOrigin("https://evil.example"));
        Assert.False(McpServer.IsLocalOrigin("garbage"));
    }

    private static int FreePort()
    {
        var l = new TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        int port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
