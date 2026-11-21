// AI-new-features:Minecraft Server List Ping(1.7+ 的状态查询协议)。
//
// 只读联网:握手(next_state=1)→ 状态请求 → 读 JSON → ping/pong 测延迟。全程带超时,
// 连不上 / 超时 / 对端不是 MC 服务器时返回 ok=false + 中文原因,不抛到工具层(让模型能直接转述)。
// 不做 SRV 记录解析(.NET 没有内置 DNS SRV 查询,不加新依赖),域名按 A/AAAA 直连;遗留在 progress 文档里。
// AI2-stability:补上 SRV —— 地址没写端口且是域名时先查 _minecraft._tcp.<host>(DnsSrv,自己发 UDP 查询,无新依赖),
// 查不到 / 超时就回落到 A 记录 + 原端口(25565)。结果里多给 resolved_via / connect_host / connect_port / srv。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TipCL.Core.Launcher.Ai;

/// <summary>Minecraft Server List Ping 客户端(纯 TCP,无第三方依赖)。</summary>
public static class ServerPing
{
    /// <summary>握手里报的协议号:-1 是社区约定的「只查状态」写法,新老服务端都会回 status。</summary>
    public const int PROTOCOL_PROBE = -1;

    /// <summary>SRV 查询最多占用多久(毫秒;实际取 min(此值, 总超时/2))。</summary>
    public const int SrvTimeoutMs = 1500;

    /// <summary>
    /// 查询一台服务器。返回字段:ok / host / port / latency_ms / online / max / version / protocol / motd /
    /// players_sample / error / message,以及 resolved_via(srv / a / literal)/ connect_host / connect_port / srv。
    /// <paramref name="timeoutMs"/> 是连接与每次读写的超时;<paramref name="resolveSrv"/> = true(地址里没写端口)时先查 SRV。
    /// </summary>
    public static async Task<PyDict> PingAsync(string host, long port = 25565, int timeoutMs = 5000, CancellationToken ct = default, bool resolveSrv = false)
    {
        string h = Py.Strip(host ?? "");
        long p = port < 1 || port > 65535 ? 25565 : port;
        (string connectHost, long connectPort, string via, PyDict? srv) = await ResolveTargetAsync(h, p, resolveSrv, timeoutMs, ct).ConfigureAwait(false);
        PyDict result = await PingCoreAsync(h, p, connectHost, connectPort, timeoutMs, ct).ConfigureAwait(false);
        result["resolved_via"] = via;
        result["connect_host"] = connectHost;
        result["connect_port"] = connectPort;
        result["srv"] = srv;
        return result;
    }

    /// <summary>
    /// 算出真正要连的地址:开了 SRV 且是域名 → 查 _minecraft._tcp.&lt;host&gt;,查到就用记录里的目标与端口(via=srv);
    /// 查不到 / 超时 / 没有 DNS 服务器 → 原主机 + 原端口(via=a,由系统解析 A/AAAA);IP 字面量 via=literal。
    /// </summary>
    internal static async Task<(string Host, long Port, string Via, PyDict? Srv)> ResolveTargetAsync(string h, long p, bool resolveSrv, int timeoutMs, CancellationToken ct)
    {
        string connectHost = h;
        long connectPort = p;
        string via = DnsSrv.ShouldQuery(h) ? "a" : "literal";
        PyDict? srv = null;
        if (resolveSrv && h.Length > 0 && DnsSrv.ShouldQuery(h))
        {
            string name = DnsSrv.MinecraftName(h);
            SrvRecord? rec = null;
            try
            {
                rec = await DnsSrv.ResolveAsync(name, Math.Min(SrvTimeoutMs, Math.Max(200, timeoutMs / 2)), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                rec = null;
            }
            if (rec != null)
            {
                connectHost = rec.Target;
                connectPort = rec.Port;
                via = "srv";
                srv = PyDict.Of("name", name, "target", rec.Target, "port", (long)rec.Port, "priority", (long)rec.Priority, "weight", (long)rec.Weight);
            }
        }
        return (connectHost, connectPort, via, srv);
    }

    /// <summary>地址里有没有显式写端口(参数 port 非空,或 host:port / [v6]:port)。没写才查 SRV(与原版客户端一致)。</summary>
    public static bool HasExplicitPort(string? address, object? port)
    {
        if (port != null && !(port is string ps && Py.Strip(ps).Length == 0) && Py.Truthy(port))
        {
            return true;
        }
        string a = Py.Strip(address ?? "");
        if (a.StartsWith("[", StringComparison.Ordinal))
        {
            return a.Contains("]:", StringComparison.Ordinal);
        }
        return a.Count(c => c == ':') == 1;
    }

    private static async Task<PyDict> PingCoreAsync(string host, long port, string connectHost, long connectPort, int timeoutMs, CancellationToken ct)
    {
        host = Py.Strip(host ?? "");
        if (host.Length == 0)
        {
            return Fail(host, port, "bad_arguments", "服务器地址不能为空");
        }
        if (port < 1 || port > 65535)
        {
            port = 25565;
        }
        timeoutMs = Math.Clamp(timeoutMs, 200, 30000);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient { NoDelay = true, ReceiveTimeout = timeoutMs, SendTimeout = timeoutMs };
            await client.ConnectAsync(connectHost, (int)connectPort, cts.Token).ConfigureAwait(false);
            using NetworkStream stream = client.GetStream();

            // 握手 + 状态请求
            byte[] handshake = Packet(0x00, w =>
            {
                WriteVarInt(w, PROTOCOL_PROBE);
                WriteString(w, host);
                w.WriteByte((byte)(port >> 8));
                w.WriteByte((byte)(port & 0xFF));
                WriteVarInt(w, 1);
            });
            await stream.WriteAsync(handshake, cts.Token).ConfigureAwait(false);
            await stream.WriteAsync(Packet(0x00, _ => { }), cts.Token).ConfigureAwait(false);
            long statusStart = sw.ElapsedMilliseconds;

            byte[] body = await ReadPacketAsync(stream, cts.Token).ConfigureAwait(false);
            long statusMs = sw.ElapsedMilliseconds - statusStart;
            int offset = 0;
            int id = ReadVarInt(body, ref offset);
            if (id != 0x00)
            {
                return Fail(host, port, "bad_response", "对端回的不是 Minecraft 状态包(包号 " + id + ")");
            }
            int len = ReadVarInt(body, ref offset);
            if (len < 0 || offset + len > body.Length)
            {
                return Fail(host, port, "bad_response", "状态包长度不对");
            }
            string json = Encoding.UTF8.GetString(body, offset, len);

            // ping / pong 测往返;失败就退回用状态包的往返时间
            long latency = statusMs;
            try
            {
                long payload = DateTime.UtcNow.Ticks;
                byte[] ping = Packet(0x01, w =>
                {
                    for (int i = 7; i >= 0; i--)
                    {
                        w.WriteByte((byte)(payload >> (i * 8)));
                    }
                });
                long pingStart = sw.ElapsedMilliseconds;
                await stream.WriteAsync(ping, cts.Token).ConfigureAwait(false);
                byte[] pong = await ReadPacketAsync(stream, cts.Token).ConfigureAwait(false);
                int po = 0;
                if (pong.Length >= 1 && ReadVarInt(pong, ref po) == 0x01)
                {
                    latency = sw.ElapsedMilliseconds - pingStart;
                }
            }
            catch (Exception)
            {
                // 有的服务端回完状态就断开:延迟按状态往返算
            }
            return Parse(host, port, json, latency);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail(host, port, "timeout", "连接超时(" + timeoutMs + " ms 内没有回应)");
        }
        catch (SocketException ex)
        {
            return Fail(host, port, "unreachable", "连不上服务器:" + ex.SocketErrorCode);
        }
        catch (IOException ex)
        {
            return Fail(host, port, "io_error", "连接被对端断开:" + ex.Message);
        }
    }

    /// <summary>把状态 JSON 解析成工具结果(纯函数,单独测)。</summary>
    public static PyDict Parse(string host, long port, string json, long latencyMs)
    {
        object parsed;
        try
        {
            parsed = PyJson.Loads(json);
        }
        catch (Exception)
        {
            return Fail(host, port, "bad_response", "状态 JSON 解析失败");
        }
        PyDict root = Py.D(parsed);
        PyDict version = Py.D(root.Get("version"));
        PyDict players = Py.D(root.Get("players"));
        var sample = new List<object>();
        foreach (object? raw in Py.ListOr(players.Get("sample")))
        {
            string name = Py.S(Py.D(raw).Get("name"));
            if (name.Length > 0 && sample.Count < 12)
            {
                sample.Add(StripFormatting(name));
            }
        }
        return PyDict.Of(
            "ok", true,
            "host", host,
            "port", port,
            "latency_ms", latencyMs,
            "online", Py.Int(Py.Or(players.Get("online"), 0L)),
            "max", Py.Int(Py.Or(players.Get("max"), 0L)),
            "version", StripFormatting(Py.S(version.Get("name"))),
            "protocol", Py.Int(Py.Or(version.Get("protocol"), 0L)),
            "motd", StripFormatting(FlattenChat(root.Get("description"))).Trim(),
            "players_sample", sample,
            "has_icon", Py.S(root.Get("favicon")).Length > 0);
    }

    /// <summary>MOTD 可能是字符串,也可能是 {text, extra:[...]} 的聊天组件:摊平成纯文本。</summary>
    public static string FlattenChat(object? component)
    {
        if (component == null)
        {
            return "";
        }
        if (component is string s)
        {
            return s;
        }
        var sb = new StringBuilder();
        if (component is PyDict d)
        {
            sb.Append(Py.S(d.Get("text")));
            foreach (object? child in Py.ListOr(d.Get("extra")))
            {
                sb.Append(FlattenChat(child));
            }
        }
        else if (component is List<object> list)
        {
            foreach (object? child in list)
            {
                sb.Append(FlattenChat(child));
            }
        }
        return sb.ToString();
    }

    /// <summary>去掉 § 颜色/格式码。</summary>
    public static string StripFormatting(string text)
        => Regex.Replace(text ?? "", "§.", "");

    private static PyDict Fail(string host, long port, string code, string message)
        => PyDict.Of("ok", false, "host", host, "port", port, "error", code, "message", message);

    // ───────────────────────────── 协议编解码 ─────────────────────────────

    internal static byte[] Packet(int id, Action<MemoryStream> body)
    {
        using var payload = new MemoryStream();
        WriteVarInt(payload, id);
        body(payload);
        using var framed = new MemoryStream();
        WriteVarInt(framed, (int)payload.Length);
        payload.Position = 0;
        payload.CopyTo(framed);
        return framed.ToArray();
    }

    internal static void WriteVarInt(Stream s, int value)
    {
        uint v = unchecked((uint)value);
        do
        {
            byte b = (byte)(v & 0x7F);
            v >>= 7;
            if (v != 0)
            {
                b |= 0x80;
            }
            s.WriteByte(b);
        }
        while (v != 0);
    }

    internal static void WriteString(Stream s, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        WriteVarInt(s, bytes.Length);
        s.Write(bytes, 0, bytes.Length);
    }

    internal static int ReadVarInt(byte[] data, ref int offset)
    {
        int result = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            if (offset >= data.Length)
            {
                throw new IOException("VarInt 被截断");
            }
            byte b = data[offset++];
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }
        }
        throw new IOException("VarInt 过长");
    }

    internal static async Task<int> ReadVarIntAsync(Stream s, CancellationToken ct)
    {
        int result = 0;
        var one = new byte[1];
        for (int shift = 0; shift < 35; shift += 7)
        {
            int n = await s.ReadAsync(one.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (n <= 0)
            {
                throw new IOException("连接已关闭");
            }
            result |= (one[0] & 0x7F) << shift;
            if ((one[0] & 0x80) == 0)
            {
                return result;
            }
        }
        throw new IOException("VarInt 过长");
    }

    /// <summary>读一个完整包体(不含长度前缀)。状态包上限 2 MB(带图标的服务器 MOTD 也远小于此)。</summary>
    internal static async Task<byte[]> ReadPacketAsync(Stream s, CancellationToken ct)
    {
        int length = await ReadVarIntAsync(s, ct).ConfigureAwait(false);
        if (length <= 0 || length > 2 * 1024 * 1024)
        {
            throw new IOException("包长度异常:" + length);
        }
        var buf = new byte[length];
        int read = 0;
        while (read < length)
        {
            int n = await s.ReadAsync(buf.AsMemory(read, length - read), ct).ConfigureAwait(false);
            if (n <= 0)
            {
                throw new IOException("连接已关闭");
            }
            read += n;
        }
        return buf;
    }
}
