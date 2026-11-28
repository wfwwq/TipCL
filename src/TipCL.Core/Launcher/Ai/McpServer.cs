// AI-engine-mcp:本机 MCP Server(Streamable HTTP 的 POST 子集,只听 127.0.0.1)。
// 来源: Windows 版参考仓库 windos/src/TipCL/Core/Launcher/Ai/McpServer.cs —— 地址 http://127.0.0.1:25585/mcp、
// DefaultPort = 25585、ProtocolVersion = "2025-06-18"、偏好文件 数据目录/mcp_server.json({"enabled","port"},
// 默认关 / 25585)、隐藏工具表、JSON-RPC 方法(initialize / ping / tools/list / tools/call / notifications/*)、
// HTTP 口径(仅 /mcp;GET 405、DELETE 200、POST 读 Content-Length ≤ 4MB;通知回 202)、安全口径(只接受环回
// 来源;带 Origin 头且不是本机来源一律 403,防网页经浏览器 DNS rebinding / CSRF 偷调)、tools/call 的返回形状
// ({content:[{type:text,text}], isError})全部照搬。参考版**没有 token 鉴权**(环回 + Origin 校验就是它的口径),
// 这里同样不加,免得两边客户端配置不通用。
//
// 与参考版不同的两处(都为了守住「写操作走同一条权限确认链」这条红线,DECISIONS D-28 当初就是卡在这里判了不移植):
//   1. tools/call **不直通**后端,而是造一个 ToolRunner(由宿主 RunnerFactory 提供:带 IApi 门面 + 界面确认回调)
//      走 RunAsync —— 档位、自定义规则(含按参数匹配)、钩子、Preview 改前改后、确认回调一个不少;
//   2. 执行器强制 ForceConfirmWrites:外部 agent 发起的写操作**一律弹确认**(哪怕档位是「全自动」),
//      拒绝类裁决(只看不动 / 不询问 / 规则禁止)照旧直接拒绝。宿主没接确认回调(无界面、测试)时写操作得到
//      permission_denied —— 这是如实的拒绝,不是假成功。参考版是直接执行、只在 instructions 里请对方「先和用户确认」。
// 实现零依赖:System.Net.Sockets.TcpListener + 手写最小 HTTP/1.1(与参考版同一做法;没用 HttpListener 是因为它在
// Windows 上监听非 localhost 前缀需要 urlacl/管理员,而本仓测试在 Windows 低完整性沙箱里跑)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>一个可独立起停的 MCP HTTP 端点实例(测试直接用它起随机端口;产品走静态门面 <see cref="McpServer"/>)。</summary>
public sealed class McpHttpServer : IDisposable
{
    private readonly object _lock = new();
    private TcpListener? _listener;
    private volatile bool _running;

    /// <summary>每次 tools/call 用的执行器(宿主提供:IApi 门面 + 确认回调)。返回 null 表示内核还没就绪。</summary>
    public Func<ToolRunner?>? RunnerFactory { get; set; }

    public int Port { get; private set; }

    public bool Running => _running;

    public string Url => $"http://127.0.0.1:{Port}/mcp";

    public string LastError { get; private set; } = "";

    /// <summary>AI2-moddev-mcp:外部写操作等用户确认的最长时间(默认取 <see cref="McpServer.ConfirmTimeout"/>;测试调小)。</summary>
    public TimeSpan ConfirmTimeout { get; set; } = McpServer.ConfirmTimeout;

    /// <summary>外部客户端调用工具时触发(工具名、本机时间;可能在后台线程)。</summary>
    public event Action<string, DateTime>? ToolCalled;

    /// <summary>在 127.0.0.1:<paramref name="port"/> 上开始监听;返回是否已在运行(失败原因见 <see cref="LastError"/>)。</summary>
    public bool Start(int port)
    {
        lock (_lock)
        {
            if (_running)
            {
                return true;
            }
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start(32);
                _listener = listener;
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                _running = true;
                LastError = "";
                new Thread(() => AcceptLoop(listener)) { IsBackground = true, Name = "mcp-accept" }.Start();
            }
            catch (Exception ex)
            {
                _running = false;
                _listener = null;
                Port = port;
                LastError = ex.Message;
            }
            return _running;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _running = false;
            try
            {
                _listener?.Stop();
            }
            catch (Exception)
            {
            }
            _listener = null;
        }
    }

    public void Dispose() => Stop();

    private void AcceptLoop(TcpListener listener)
    {
        while (_running)
        {
            TcpClient client;
            try
            {
                client = listener.AcceptTcpClient();
            }
            catch (Exception)
            {
                if (!_running || !ReferenceEquals(listener, _listener))
                {
                    break;
                }
                continue;
            }
            ThreadPool.QueueUserWorkItem(_ => Handle(client));
        }
    }

    private void Handle(TcpClient client)
    {
        using (client)
        {
            try
            {
                if (client.Client.RemoteEndPoint is not IPEndPoint ep || !IPAddress.IsLoopback(ep.Address))
                {
                    return;
                }
                client.ReceiveTimeout = 30000;
                NetworkStream s = client.GetStream();
                string? requestLine = ReadLine(s);
                if (string.IsNullOrEmpty(requestLine))
                {
                    return;
                }
                string[] parts = requestLine.Split(' ');
                string method = parts.Length > 0 ? parts[0] : "";
                string path = parts.Length > 1 ? parts[1] : "";
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string? line;
                while (!string.IsNullOrEmpty(line = ReadLine(s)))
                {
                    int i = line.IndexOf(':');
                    if (i > 0)
                    {
                        headers[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                    }
                }
                // 先把正文读完再做各种拒绝:收到的数据没读就关连接,Windows 会回 RST,客户端只看到「连接被重置」
                // 而不是 403 / 404(参考版在这里是先拒后读,外站 Origin 的 403 实际传不到对方)
                int len = headers.TryGetValue("Content-Length", out string? cl) && int.TryParse(cl, out int n) ? n : 0;
                byte[] body = Array.Empty<byte>();
                int read = 0;
                if (len > 0 && len <= 4194304)
                {
                    body = new byte[len];
                    while (read < len)
                    {
                        int r = s.Read(body, read, len - read);
                        if (r <= 0)
                        {
                            break;
                        }
                        read += r;
                    }
                }
                // 防止网页通过浏览器偷偷调用本机接口(DNS rebinding / CSRF)。
                // SEC-MCP-1:Origin "null"(sandbox iframe / file:// / data: 页)不再视为本机;Host 也必须是环回名。
                if (headers.TryGetValue("Origin", out string? origin) && !McpServer.IsLocalOrigin(origin))
                {
                    Write(s, 403, "text/plain", "forbidden origin");
                    return;
                }
                if (headers.TryGetValue("Host", out string? host) && !McpServer.IsLocalHost(host))
                {
                    Write(s, 403, "text/plain", "forbidden host");
                    return;
                }
                int q = path.IndexOf('?');
                if (q >= 0)
                {
                    path = path.Substring(0, q);
                }
                if (path != "/mcp" && path != "/mcp/")
                {
                    Write(s, 404, "text/plain", "TipCL MCP endpoint: /mcp");
                    return;
                }
                if (method == "GET")
                {
                    Write(s, 405, "text/plain", "SSE stream not supported; use POST");
                    return;
                }
                if (method == "DELETE")
                {
                    Write(s, 200, "text/plain", "");
                    return;
                }
                if (method != "POST")
                {
                    Write(s, 405, "text/plain", "method not allowed");
                    return;
                }
                // SEC-MCP-1:只收 application/json。text/plain 等「简单请求」浏览器不发 CORS 预检,
                // 网页能直接打进来;强制 JSON 后浏览器必须先预检,而本服务不回 CORS 头,跨站调用就被拦下。
                if (!McpServer.IsJsonContentType(headers.TryGetValue("Content-Type", out string? ct) ? ct : null))
                {
                    Write(s, 415, "text/plain", "Content-Type must be application/json");
                    return;
                }
                if (len <= 0 || len > 4194304)
                {
                    Write(s, 400, "text/plain", "bad content-length");
                    return;
                }
                string? reply = Dispatch(Encoding.UTF8.GetString(body, 0, read));
                if (reply == null)
                {
                    Write(s, 202, "text/plain", "");
                }
                else
                {
                    Write(s, 200, "application/json", reply);
                }
            }
            catch (Exception ex)
            {
                Trace.Record("mcp_server_request_failed", null, "", "", ex);
            }
        }
    }

    /// <summary>处理一条 JSON-RPC 消息(或批量),返回响应 JSON;纯通知返回 null(HTTP 202)。</summary>
    public string? Dispatch(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (Exception)
        {
            return Error(null, -32700, "Parse error");
        }
        using (doc)
        {
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var outs = new List<string>();
                foreach (JsonElement m in doc.RootElement.EnumerateArray())
                {
                    string? r = One(m);
                    if (r != null)
                    {
                        outs.Add(r);
                    }
                }
                return outs.Count == 0 ? null : "[" + string.Join(",", outs) + "]";
            }
            return One(doc.RootElement);
        }
    }

    private string? One(JsonElement m)
    {
        if (m.ValueKind != JsonValueKind.Object)
        {
            return Error(null, -32600, "Invalid Request");
        }
        string? id = m.TryGetProperty("id", out JsonElement idEl) ? idEl.GetRawText() : null;
        string? method = m.TryGetProperty("method", out JsonElement me) && me.ValueKind == JsonValueKind.String ? me.GetString() : null;
        if (method == null)
        {
            return null; // 客户端发来的响应,忽略
        }
        bool isNotification = id == null;
        JsonElement prm = m.TryGetProperty("params", out JsonElement pe) ? pe : default;
        try
        {
            switch (method)
            {
                case "initialize":
                {
                    string ver = prm.ValueKind == JsonValueKind.Object && prm.TryGetProperty("protocolVersion", out JsonElement pv) && pv.ValueKind == JsonValueKind.String
                        ? pv.GetString() ?? McpServer.ProtocolVersion
                        : McpServer.ProtocolVersion;
                    return Result(id, "{\"protocolVersion\":" + Str(ver)
                        + ",\"capabilities\":{\"tools\":{\"listChanged\":false}},\"serverInfo\":{\"name\":\"tipcl\",\"title\":\"TipCL Minecraft 启动器\",\"version\":"
                        + Str(Defaults.CLIENT_HEADER.Replace("TipCL/", "")) + "},\"instructions\":" + Str(McpServer.Instructions) + "}");
                }
                case "ping":
                    return isNotification ? null : Result(id, "{}");
                case "tools/list":
                    return Result(id, "{\"tools\":" + McpServer.ToolListJson() + "}");
                case "tools/call":
                    return Result(id, CallTool(prm));
                default:
                    if (method.StartsWith("notifications/", StringComparison.Ordinal) || isNotification)
                    {
                        return null;
                    }
                    return Error(id, -32601, "Method not found: " + method);
            }
        }
        catch (Exception ex)
        {
            return isNotification ? null : Error(id, -32603, ex.Message);
        }
    }

    private string CallTool(JsonElement prm)
    {
        if (prm.ValueKind != JsonValueKind.Object || !prm.TryGetProperty("name", out JsonElement ne) || ne.ValueKind != JsonValueKind.String)
        {
            return ToolText("缺少工具名 name", true);
        }
        string name = ne.GetString() ?? "";
        if (McpServer.HiddenTools.Contains(name))
        {
            return ToolText("该工具只能在启动器界面里用: " + name, true);
        }
        if (!McpServer.IsExposed(name))
        {
            return ToolText("没有这个工具: " + name, true);
        }
        ToolRunner? runner = null;
        try
        {
            runner = RunnerFactory?.Invoke();
        }
        catch (Exception ex)
        {
            Trace.Record("mcp_server_runner_failed", null, "", name, ex);
        }
        if (runner == null)
        {
            return ToolText("启动器内核还没就绪，请稍后再试", true);
        }
        // 外部发起的写操作一律要坐在电脑前的用户点确认(见文件头);由服务端强制,不信任宿主工厂
        runner.ForceConfirmWrites = true;
        // AI2-moddev-mcp:确认等待超时(默认 120 秒),超时按拒绝处理并如实说明原因(见 McpConfirmGate)
        McpConfirmGate.Handle confirmGate = McpConfirmGate.Wrap(runner, ConfirmTimeout);
        NoteToolCall(name);
        string rawArgs = prm.TryGetProperty("arguments", out JsonElement ae) && ae.ValueKind == JsonValueKind.Object ? ae.GetRawText() : "{}";
        (PyDict? args, string parseError) = Tools.ParseArgs(rawArgs, name);
        if (args == null)
        {
            return ToolText(Tools.ErrorPayload(new ValueError(parseError), name, "bad_arguments"), true);
        }
        var call = new ToolCall("mcp-" + Guid.NewGuid().ToString("N").Substring(0, 12), name, args);
        string output = runner.RunAsync(call).ConfigureAwait(false).GetAwaiter().GetResult();
        if (confirmGate.TimedOut)
        {
            output = McpConfirmGate.TimeoutPayload(name, confirmGate.Timeout);
        }
        bool isError = call.status != ToolCallStatus.COMPLETED || McpServer.LooksLikeError(output);
        return ToolText(output, isError);
    }

    private void NoteToolCall(string name)
    {
        DateTime at = DateTime.Now;
        McpServer.NoteToolCall(name, at);
        try
        {
            ToolCalled?.Invoke(name, at);
        }
        catch (Exception)
        {
        }
    }

    private static string ToolText(string text, bool isError) =>
        "{\"content\":[{\"type\":\"text\",\"text\":" + Str(text ?? "") + "}],\"isError\":" + (isError ? "true" : "false") + "}";

    private static string? Result(string? id, string result) => id == null ? null : "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":" + result + "}";

    private static string Error(string? id, int code, string msg) => "{\"jsonrpc\":\"2.0\",\"id\":" + (id ?? "null") + ",\"error\":{\"code\":" + code + ",\"message\":" + Str(msg) + "}}";

    internal static string Str(string? s) => JsonSerializer.Serialize(s ?? "");

    private static string? ReadLine(Stream s)
    {
        var sb = new StringBuilder();
        int b;
        while ((b = s.ReadByte()) >= 0)
        {
            if (b == '\n')
            {
                break;
            }
            if (b != '\r')
            {
                sb.Append((char)b);
            }
            if (sb.Length > 65536)
            {
                return null;
            }
        }
        return b < 0 && sb.Length == 0 ? null : sb.ToString();
    }

    private static void Write(Stream s, int code, string type, string body)
    {
        byte[] data = Encoding.UTF8.GetBytes(body ?? "");
        string reason = code switch
        {
            200 => "OK",
            202 => "Accepted",
            400 => "Bad Request",
            403 => "Forbidden",
            404 => "Not Found",
            405 => "Method Not Allowed",
            _ => "OK",
        };
        string head = $"HTTP/1.1 {code} {reason}\r\nContent-Type: {type}; charset=utf-8\r\nContent-Length: {data.Length}\r\nConnection: close\r\n\r\n";
        byte[] h = Encoding.ASCII.GetBytes(head);
        s.Write(h, 0, h.Length);
        s.Write(data, 0, data.Length);
        s.Flush();
    }
}

/// <summary>
/// 本机 MCP Server 的进程级门面(名字与参考版 McpServer 一致:DefaultPort / ProtocolVersion / Enabled /
/// StartIfEnabled / SetEnabled / Start / Stop / Url / Running / LastError / LastTool / StateChanged / ToolCalled)。
/// 把 AI 助手已有的工具原样开放给 Claude / Cursor 等外部 agent。地址:http://127.0.0.1:25585/mcp
/// </summary>
public static class McpServer
{
    public const int DefaultPort = 25585;

    public const string ProtocolVersion = "2025-06-18";

    /// <summary>AI2-moddev-mcp:外部写操作等用户确认的最长时间,超时按拒绝(permission_denied + confirm_timeout)。</summary>
    public static TimeSpan ConfirmTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>initialize 返回给外部 agent 的说明(与参考版同一句话)。</summary>
    public const string Instructions = "TipCL 启动器的本机工具：查实例、装版本/模组/整合包/光影、启动游戏、诊断崩溃。写操作会直接改玩家的游戏目录，执行前请先和用户确认。";

    /// <summary>依赖启动器界面的工具不对外开放(与参考版 _hidden 同一张表)。</summary>
    public static readonly HashSet<string> HiddenTools = new(StringComparer.Ordinal)
    {
        "ask_user", "dispatch_subagent", "update_plan", "remember", "forget",
    };

    private static readonly object _lock = new();

    private static McpHttpServer? _server;

    /// <summary>每次 tools/call 用的执行器工厂(App 启动时注入:IApi 门面 + 界面确认回调)。</summary>
    public static Func<ToolRunner?>? RunnerFactory { get; set; }

    public static int Port { get; private set; } = DefaultPort;

    public static bool Running => _server?.Running == true;

    public static string Url => $"http://127.0.0.1:{Port}/mcp";

    public static string LastError { get; private set; } = "";

    /// <summary>外部客户端最近一次调用的工具名和时间(本机时间);没调用过为空。</summary>
    public static string LastTool { get; private set; } = "";

    public static DateTime LastToolAt { get; private set; }

    /// <summary>开关变化时触发(可能在后台线程)。</summary>
    public static event Action? StateChanged;

    /// <summary>外部客户端调用工具时触发:工具名、时间(可能在后台线程)。</summary>
    public static event Action<string, DateTime>? ToolCalled;

    /// <summary>偏好文件:数据目录/mcp_server.json(与参考版同名同形)。</summary>
    public static string PrefPath => Path.Combine(Utils.ROOT, "mcp_server.json");

    /// <summary>读偏好:开关(默认关)+ 端口(默认 25585);读的同时把端口记到 <see cref="Port"/>(与参考版同一副作用)。</summary>
    public static bool Enabled
    {
        get
        {
            (bool on, int port) = LoadPrefs();
            Port = port;
            return on;
        }
    }

    /// <summary>偏好文件里的(开关, 端口);文件缺失/损坏 → (false, 25585)。</summary>
    public static (bool enabled, int port) LoadPrefs()
    {
        try
        {
            if (!File.Exists(PrefPath))
            {
                return (false, DefaultPort);
            }
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(PrefPath));
            int port = DefaultPort;
            if (doc.RootElement.TryGetProperty("port", out JsonElement p) && p.TryGetInt32(out int v) && v > 0 && v < 65536)
            {
                port = v;
            }
            bool on = doc.RootElement.TryGetProperty("enabled", out JsonElement e) && e.ValueKind == JsonValueKind.True;
            return (on, port);
        }
        catch (Exception)
        {
            return (false, DefaultPort);
        }
    }

    private static void SavePrefs(bool on, int port)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefPath)!);
            File.WriteAllText(PrefPath, $"{{\"enabled\": {(on ? "true" : "false")}, \"port\": {port}}}");
        }
        catch (Exception ex)
        {
            Trace.Record("mcp_server_pref_write_failed", null, "", "", ex);
        }
    }

    /// <summary>启动时调用:上次开着就自动开。</summary>
    public static void StartIfEnabled(Func<ToolRunner?>? runnerFactory = null)
    {
        if (runnerFactory != null)
        {
            RunnerFactory = runnerFactory;
        }
        if (Enabled)
        {
            Start();
        }
    }

    /// <summary>设置页开关调用;返回是否已在运行。</summary>
    public static bool SetEnabled(bool on)
    {
        _ = Enabled; // 先把文件里的端口读回来,别把用户改过的端口写丢
        SavePrefs(on, Port);
        if (on)
        {
            Start();
        }
        else
        {
            Stop();
        }
        return Running;
    }

    /// <summary>设置页改端口:写回偏好;正在运行就换到新端口重开。返回是否接受了这个端口。</summary>
    public static bool SetPort(int port)
    {
        if (port <= 0 || port >= 65536)
        {
            return false;
        }
        bool on = Enabled;
        bool wasRunning = Running;
        Port = port;
        SavePrefs(on, port);
        if (wasRunning)
        {
            Stop();
            Start();
        }
        else
        {
            RaiseState();
        }
        return true;
    }

    public static void Start()
    {
        lock (_lock)
        {
            if (_server?.Running == true)
            {
                return;
            }
            var server = new McpHttpServer { RunnerFactory = () => RunnerFactory?.Invoke() };
            if (server.Start(Port))
            {
                _server = server;
                LastError = "";
            }
            else
            {
                _server = null;
                LastError = server.LastError;
            }
        }
        RaiseState();
    }

    public static void Stop()
    {
        lock (_lock)
        {
            _server?.Stop();
            _server = null;
        }
        RaiseState();
    }

    /// <summary>直接处理一条 JSON-RPC(不经 HTTP;测试与自检用)。</summary>
    public static string? Dispatch(string json) => new McpHttpServer { RunnerFactory = () => RunnerFactory?.Invoke() }.Dispatch(json);

    /// <summary>记下最近一次外部调用并通知界面。</summary>
    public static void NoteToolCall(string name, DateTime? at = null)
    {
        LastTool = name ?? "";
        LastToolAt = at ?? DateTime.Now;
        try
        {
            ToolCalled?.Invoke(LastTool, LastToolAt);
        }
        catch (Exception)
        {
        }
    }

    private static void RaiseState()
    {
        try
        {
            StateChanged?.Invoke();
        }
        catch (Exception)
        {
        }
    }

    /// <summary>这个工具名是否对外开放:在 schema 里、有元信息、不在隐藏表里。</summary>
    public static bool IsExposed(string name)
        => !HiddenTools.Contains(name) && ToolMeta.Get(name) != null && Tools.Names().Contains(name);

    /// <summary>tools/list 的 tools 数组:**每次现读** Tools.Schemas(别的包新增的工具自动出现)。</summary>
    public static string ToolListJson()
    {
        var items = new List<string>();
        foreach (object schema in Tools.Schemas)
        {
            string name = Tools.SchemaName(schema);
            if (name.Length == 0 || HiddenTools.Contains(name) || ToolMeta.Get(name) is not { } meta)
            {
                continue;
            }
            PyDict fn = Py.D(Py.D(schema).Get("function"));
            string desc = Py.S(fn.Get("description"));
            object parameters = fn.Get("parameters") ?? PyDict.Of("type", "object", "properties", new PyDict());
            bool destructive = meta.side_effect == "delete" || name.StartsWith("delete_", StringComparison.Ordinal);
            items.Add("{\"name\":" + McpHttpServer.Str(name) + ",\"description\":" + McpHttpServer.Str(desc)
                + ",\"inputSchema\":" + PyJson.DumpsU(parameters)
                + ",\"annotations\":{\"readOnlyHint\":" + (meta.@readonly ? "true" : "false")
                + ",\"destructiveHint\":" + (destructive ? "true" : "false") + "}}");
        }
        return "[" + string.Join(",", items) + "]";
    }

    /// <summary>工具结果像不像错误(顶层 error 键,或 ok=false;与参考版 LooksLikeError 同一判据)。</summary>
    public static bool LooksLikeError(string output)
    {
        try
        {
            using JsonDocument d = JsonDocument.Parse(output);
            return d.RootElement.ValueKind == JsonValueKind.Object
                && (d.RootElement.TryGetProperty("error", out _)
                    || (d.RootElement.TryGetProperty("ok", out JsonElement ok) && ok.ValueKind == JsonValueKind.False));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Origin 是否本机。没带 Origin(命令行 / 桌面 MCP 客户端)放行;
    /// "null"(sandbox iframe、file://、data: 页面发出的请求)一律拒绝(SEC-MCP-1)。
    /// </summary>
    public static bool IsLocalOrigin(string? origin)
    {
        if (string.IsNullOrEmpty(origin))
        {
            return true;
        }
        if (string.Equals(origin.Trim(), "null", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        try
        {
            string h = new Uri(origin).Host;
            return h is "127.0.0.1" or "localhost" or "[::1]" or "::1";
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Host 头是否环回名(127.0.0.1 / localhost / [::1],可带端口);空视为本机(HTTP/1.0 客户端)。</summary>
    public static bool IsLocalHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }
        string h = host.Trim();
        if (h.StartsWith("[", StringComparison.Ordinal))
        {
            int end = h.IndexOf(']');
            h = end > 0 ? h.Substring(1, end - 1) : h;
        }
        else
        {
            int colon = h.LastIndexOf(':');
            if (colon > 0 && h.IndexOf(':') == colon)
            {
                h = h.Substring(0, colon);
            }
        }
        return string.Equals(h, "127.0.0.1", StringComparison.Ordinal)
            || string.Equals(h, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(h, "::1", StringComparison.Ordinal);
    }

    /// <summary>Content-Type 是否 application/json(允许带 charset 等参数)。</summary>
    public static bool IsJsonContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }
        string media = contentType.Split(';')[0].Trim();
        return string.Equals(media, "application/json", StringComparison.OrdinalIgnoreCase);
    }
}
