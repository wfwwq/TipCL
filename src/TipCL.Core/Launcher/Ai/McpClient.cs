// M4-H（AI 服务层）：MCP 客户端（照 Windows 版 Launcher/Ai/McpClient.cs 搬运）。
//
// 自己实现，不引第三方 MCP 包:按 MCP 的 stdio 传输拉一个子进程，stdin/stdout 上跑 JSON-RPC 2.0,
// 一条请求一行 JSON（与参考版一致，Windows 上行尾 \r\n、Linux 上 \n）。
// 握手照协议走 initialize → notifications/initialized，工具面走 tools/list / tools/call。
//
// 超时与失败语义照参考版:连接 10s、调用 60s；超时或子进程退出都抛 McpError（调用方按「工具失败」处理，
// 不会让整轮对话崩掉）；关掉时先关 stdin 再等 2s，还不退才 Kill。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>一个 MCP server 的连接（stdio + JSON-RPC）。</summary>
public sealed class McpClient
{
    public const string ProtocolVersion = "2024-11-05";

    public const double ConnectTimeout = 10.0;

    public const double CallTimeout = 60.0;

    public readonly string name;

    public readonly string command;

    public readonly List<string> args;

    public readonly PyDict? env;

    private Process? _proc;

    private long _nextId = 1;

    private readonly object _lock = new();

    public McpClient(string name, string command, List<object>? args = null, PyDict? env = null)
    {
        this.name = name;
        this.command = command;
        this.args = (args ?? new List<object>()).Select(Py.Str).ToList();
        this.env = env;
    }

    private void Send(PyDict payload)
    {
        string line = PyJson.Dumps(payload, null, ensureAscii: false) + (Utils.IS_WINDOWS ? "\r\n" : "\n");
        _proc!.StandardInput.Write(line);
        _proc.StandardInput.Flush();
    }

    /// <summary>等一条响应（跳过通知；超时就杀掉子进程并抛错）。</summary>
    private PyDict Recv(long wantId, double timeout)
    {
        double deadline = Py.Monotonic() + timeout;
        bool timedOut = false;
        using var answered = new ManualResetEventSlim(false);
        Process proc = _proc!;
        var watchdog = new Thread(() =>
        {
            if (answered.Wait(TimeSpan.FromSeconds(Math.Max(0.1, deadline - Py.Monotonic()))))
            {
                return;
            }
            timedOut = true;
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill();
                }
            }
            catch (Exception)
            {
            }
        })
        {
            IsBackground = true,
        };
        watchdog.Start();
        try
        {
            while (true)
            {
                string? line;
                try
                {
                    line = _proc!.StandardOutput.ReadLine();
                }
                catch (Exception)
                {
                    line = null;
                }
                if (line == null)
                {
                    if (timedOut)
                    {
                        throw new McpError("MCP server " + name + " 响应超时");
                    }
                    string code;
                    try
                    {
                        code = _proc!.WaitForExit(200) ? _proc.ExitCode.ToString() : "None";
                    }
                    catch (Exception)
                    {
                        code = "None";
                    }
                    throw new McpError("MCP server " + name + " 已退出(exit=" + code + ")");
                }
                line = Py.Strip(line);
                if (line.Length != 0)
                {
                    object parsed;
                    try
                    {
                        parsed = PyJson.Loads(line);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    if (parsed is PyDict msg && Py.Eq(msg.Get("id"), wantId))
                    {
                        return msg;
                    }
                    if (Py.Monotonic() > deadline)
                    {
                        break;
                    }
                }
            }
            throw new McpError("MCP server " + name + " 响应超时");
        }
        finally
        {
            answered.Set();
        }
    }

    private PyDict Request(string method, PyDict? prms, double timeout)
    {
        PyDict reply;
        lock (_lock)
        {
            if (_proc == null)
            {
                throw new McpError("MCP server " + name + " 未连接");
            }
            long id = _nextId++;
            Send(PyDict.Of("jsonrpc", "2.0", "id", id, "method", method, "params", prms ?? new PyDict()));
            reply = Recv(id, timeout);
        }
        if (reply.Get("error") is PyDict err)
        {
            throw new McpError("MCP " + method + " 失败: " + Py.Str(err.Get("message")));
        }
        return (Py.Or(reply.Get("result"), new PyDict()) as PyDict) ?? new PyDict();
    }

    /// <summary>拉起子进程并握手。</summary>
    public void Connect()
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = Utils.List2CmdLine(args),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = PPath.Abs(Utils.ROOT),
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        if (env != null)
        {
            foreach (KeyValuePair<string, object> kv in env)
            {
                psi.EnvironmentVariables[kv.Key] = Py.Str(kv.Value);
            }
        }
        try
        {
            _proc = Process.Start(psi);
        }
        catch (Exception)
        {
            throw new FileNotFoundError("[Errno 2] No such file or directory: " + Py.ReprStr(command));
        }
        try
        {
            // server 的 stderr 直接丢掉（有些 server 往 stderr 打日志，读满管道会把它堵死）
            _proc.StandardError.BaseStream.CopyToAsync(Stream.Null);
        }
        catch (Exception)
        {
        }
        Request("initialize",
            PyDict.Of("protocolVersion", ProtocolVersion, "capabilities", new PyDict(), "clientInfo", PyDict.Of("name", "TipCL", "version", "1.0")),
            ConnectTimeout);
        lock (_lock)
        {
            Send(PyDict.Of("jsonrpc", "2.0", "method", "notifications/initialized"));
        }
    }

    /// <summary>关连接（先关 stdin，给它自己退出的机会）。</summary>
    public void Close()
    {
        try
        {
            if (_proc != null)
            {
                try
                {
                    _proc.StandardInput.Close();
                }
                catch (Exception)
                {
                }
                try
                {
                    if (!_proc.HasExited && !_proc.WaitForExit(2000))
                    {
                        _proc.Kill();
                    }
                }
                catch (Exception)
                {
                }
            }
        }
        catch (Exception)
        {
        }
        _proc = null;
    }

    /// <summary>列出这个 server 提供的工具（原始 schema 行）。</summary>
    public List<object> ListTools()
        => (Request("tools/list", new PyDict(), ConnectTimeout).Get("tools") as List<object>) ?? new List<object>();

    /// <summary>调一个工具；返回文本结果（多个 text 块按行拼）。</summary>
    public string CallTool(string toolName, PyDict? arguments)
    {
        PyDict result = Request("tools/call", PyDict.Of("name", toolName, "arguments", arguments ?? new PyDict()), CallTimeout);
        if (Py.Truthy(result.Get("isError")))
        {
            throw new McpError(Py.Slice(Py.Str(Py.Or(result.Get("content"), "tool error")), null, 300));
        }
        var texts = new List<string>();
        foreach (object item in Py.ListOr(Py.Or(result.Get("content"), new List<object>())))
        {
            if (item is PyDict block && Py.Eq(block.Get("type"), "text"))
            {
                texts.Add(Py.S(block.Get("text")));
            }
        }
        if (texts.Count == 0)
        {
            // 没有 text 块（图片/资源）就把原始 JSON 回给模型，别丢结果
            return PyJson.Dumps(result, null, ensureAscii: false);
        }
        return string.Join("\n", texts);
    }
}
