// AI2-stability:最小 DNS SRV 查询(ping_server 用 _minecraft._tcp.<host>)。
//
// .NET 没有内置的 SRV 查询(System.Net.Dns 只给 A/AAAA),又不加第三方依赖,所以自己用 UDP 发一个最小查询:
//   * DNS 服务器:Linux / macOS 读 /etc/resolv.conf 的 nameserver;Windows(以及读不到 resolv.conf 时)用
//     NetworkInterface 的 DnsAddresses(只取已连通、非环回网卡;IPv4 在前);
//   * 报文:标准查询(RD=1)、一个问题、QTYPE=SRV(33)/ QCLASS=IN;应答按 RFC 1035 解析,支持名字压缩指针;
//   * 校验:事务 id 必须一致、QR=1、RCODE=0,否则视为没有记录;TC(截断)时也按没有记录处理(SRV 应答远小于 512 字节);
//   * 每台服务器单独计时,总超时到了就停 —— 调用方据此回落到「A 记录 + 原端口」,与原版客户端的行为一致。
// 多条记录时按 RFC 2782 取优先级最小的,同优先级取权重最大的(不做随机加权,保证同一输入结果稳定、可测)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TipCL.Core.Launcher;

/// <summary>一条 SRV 记录。</summary>
public sealed record SrvRecord(int Priority, int Weight, int Port, string Target);

/// <summary>最小 DNS SRV 查询客户端(UDP,无第三方依赖)。</summary>
public static class DnsSrv
{
    public const int DnsPort = 53;

    public const ushort TypeSrv = 33;

    public const ushort ClassIn = 1;

    /// <summary>测试注入点:改用这些 DNS 服务器(本机 UDP 假 DNS);为空时读系统配置。</summary>
    public static Func<List<IPEndPoint>>? ServersHook;

    /// <summary>Minecraft 的 SRV 名:<c>_minecraft._tcp.&lt;host&gt;</c>。</summary>
    public static string MinecraftName(string host) => "_minecraft._tcp." + (host ?? "").Trim().TrimEnd('.');

    /// <summary>这个主机名该不该查 SRV:IP 字面量、localhost、单段名都不查(原版客户端同样只对域名查)。</summary>
    public static bool ShouldQuery(string host)
    {
        string h = (host ?? "").Trim().TrimEnd('.');
        if (h.Length == 0 || IPAddress.TryParse(h.Trim('[', ']'), out _))
        {
            return false;
        }
        if (string.Equals(h, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return h.Contains('.');
    }

    // ───────────────────────────── 系统 DNS 服务器 ─────────────────────────────

    /// <summary>当前要问的 DNS 服务器(去重,IPv4 在前)。</summary>
    public static List<IPEndPoint> SystemServers()
    {
        Func<List<IPEndPoint>>? hook = ServersHook;
        if (hook != null)
        {
            return hook() ?? new List<IPEndPoint>();
        }
        var found = new List<IPAddress>();
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                if (File.Exists("/etc/resolv.conf"))
                {
                    found.AddRange(ParseResolvConf(File.ReadAllText("/etc/resolv.conf")));
                }
            }
            catch (Exception)
            {
                // 读不到就退到网卡配置
            }
        }
        if (found.Count == 0)
        {
            found.AddRange(InterfaceServers());
        }
        return found
            .Distinct()
            .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
            .Select(a => new IPEndPoint(a, DnsPort))
            .ToList();
    }

    /// <summary>解析 resolv.conf 的 <c>nameserver</c> 行(忽略注释、非法地址、带 zone 的 IPv6 也认)。</summary>
    public static List<IPAddress> ParseResolvConf(string text)
    {
        var list = new List<IPAddress>();
        foreach (string raw in (text ?? "").Split('\n'))
        {
            string line = raw.Trim();
            int hash = line.IndexOfAny(new[] { '#', ';' });
            if (hash >= 0)
            {
                line = line.Substring(0, hash).Trim();
            }
            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0] == "nameserver" && IPAddress.TryParse(parts[1], out IPAddress? ip))
            {
                list.Add(ip);
            }
        }
        return list;
    }

    private static IEnumerable<IPAddress> InterfaceServers()
    {
        var list = new List<IPAddress>();
        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }
                foreach (IPAddress ip in nic.GetIPProperties().DnsAddresses)
                {
                    // fec0::/10 是 Windows 早年的站点本地占位 DNS,基本都不通
                    if (ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.IsIPv6SiteLocal)
                    {
                        continue;
                    }
                    list.Add(ip);
                }
            }
        }
        catch (Exception)
        {
            // 某些受限环境不让枚举网卡:当作没有 DNS 服务器
        }
        return list;
    }

    // ───────────────────────────── 报文 ─────────────────────────────

    /// <summary>拼一个标准查询报文(RD=1,一个问题)。名字非法(空段 / 段超 63 字节)抛 <see cref="ArgumentException"/>。</summary>
    public static byte[] BuildQuery(ushort id, string name, ushort qtype = TypeSrv)
    {
        using var ms = new MemoryStream();
        WriteU16(ms, id);
        WriteU16(ms, 0x0100); // 标准查询 + 期望递归
        WriteU16(ms, 1);      // QDCOUNT
        WriteU16(ms, 0);
        WriteU16(ms, 0);
        WriteU16(ms, 0);
        foreach (string label in (name ?? "").Trim().TrimEnd('.').Split('.'))
        {
            byte[] bytes = Encoding.ASCII.GetBytes(label);
            if (bytes.Length == 0 || bytes.Length > 63)
            {
                throw new ArgumentException("bad dns label in " + name);
            }
            ms.WriteByte((byte)bytes.Length);
            ms.Write(bytes, 0, bytes.Length);
        }
        ms.WriteByte(0);
        WriteU16(ms, qtype);
        WriteU16(ms, ClassIn);
        return ms.ToArray();
    }

    /// <summary>
    /// 解析应答里的 SRV 记录。id 不符 / 不是应答 / RCODE≠0 / 截断 / 报文损坏一律返回空表(调用方回落)。
    /// </summary>
    public static List<SrvRecord> ParseResponse(byte[] data, ushort expectedId)
    {
        var list = new List<SrvRecord>();
        try
        {
            if (data == null || data.Length < 12)
            {
                return list;
            }
            if (ReadU16(data, 0) != expectedId)
            {
                return list;
            }
            int flags = ReadU16(data, 2);
            bool isResponse = (flags & 0x8000) != 0;
            bool truncated = (flags & 0x0200) != 0;
            int rcode = flags & 0x000F;
            if (!isResponse || truncated || rcode != 0)
            {
                return list;
            }
            int qd = ReadU16(data, 4);
            int an = ReadU16(data, 6);
            int off = 12;
            for (int i = 0; i < qd; i++)
            {
                ReadName(data, ref off);
                off += 4;
            }
            for (int i = 0; i < an; i++)
            {
                ReadName(data, ref off);
                int type = ReadU16(data, off);
                int klass = ReadU16(data, off + 2);
                int rdlen = ReadU16(data, off + 8);
                off += 10;
                if (off + rdlen > data.Length)
                {
                    break;
                }
                if (type == TypeSrv && klass == ClassIn && rdlen >= 7)
                {
                    int p = off;
                    int priority = ReadU16(data, p);
                    int weight = ReadU16(data, p + 2);
                    int port = ReadU16(data, p + 4);
                    p += 6;
                    string target = ReadName(data, ref p);
                    if (port > 0 && target.Length > 0 && target != ".")
                    {
                        list.Add(new SrvRecord(priority, weight, port, target.TrimEnd('.')));
                    }
                }
                off += rdlen;
            }
        }
        catch (Exception)
        {
            // 报文坏了:按没有记录处理
        }
        return list;
    }

    /// <summary>RFC 2782 的挑选:优先级最小,其次权重最大。</summary>
    public static SrvRecord? Pick(IEnumerable<SrvRecord> records)
        => records.OrderBy(r => r.Priority).ThenByDescending(r => r.Weight).FirstOrDefault();

    // ───────────────────────────── 查询 ─────────────────────────────

    /// <summary>
    /// 查 SRV:依次问每台 DNS 服务器,谁先给出有效记录就用谁;全部失败 / 超时返回 null。
    /// <paramref name="timeoutMs"/> 是总时长上限(每台服务器最多分到剩余时间)。
    /// </summary>
    public static async Task<SrvRecord?> ResolveAsync(string name, int timeoutMs, CancellationToken ct = default)
    {
        List<IPEndPoint> servers;
        try
        {
            servers = SystemServers();
        }
        catch (Exception)
        {
            return null;
        }
        if (servers.Count == 0)
        {
            return null;
        }
        byte[] query;
        ushort id = (ushort)Random.Shared.Next(1, 0xFFFF);
        try
        {
            query = BuildQuery(id, name);
        }
        catch (ArgumentException)
        {
            return null;
        }
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(50, timeoutMs));
        foreach (IPEndPoint server in servers)
        {
            int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
            if (left <= 0)
            {
                break;
            }
            ct.ThrowIfCancellationRequested();
            List<SrvRecord> got = await AskAsync(server, query, id, left, ct).ConfigureAwait(false);
            if (got.Count > 0)
            {
                return Pick(got);
            }
        }
        return null;
    }

    /// <summary>
    /// AI3-stability:只问一台服务器,并把结果分清楚(真网络核验 `--srv-check` 用;ping_server 仍走 <see cref="ResolveAsync"/>)。
    /// Status:ok / no_record(应答里没有 SRV)/ rcode=N(3 = NXDOMAIN)/ truncated / timeout / error:…。
    /// </summary>
    public static async Task<(List<SrvRecord> Records, string Status, int ElapsedMs)> QueryServerAsync(IPEndPoint server, string name, int timeoutMs, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ushort id = (ushort)Random.Shared.Next(1, 0xFFFF);
        byte[] query;
        try
        {
            query = BuildQuery(id, name);
        }
        catch (ArgumentException ex)
        {
            return (new List<SrvRecord>(), "error: " + ex.Message, 0);
        }
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Math.Max(50, timeoutMs));
        try
        {
            using var udp = new UdpClient(server.AddressFamily);
            await udp.SendAsync(query, query.Length, server).ConfigureAwait(false);
            while (true)
            {
                UdpReceiveResult res = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                if (!res.RemoteEndPoint.Address.Equals(server.Address) || res.Buffer.Length < 4 || ReadU16(res.Buffer, 0) != id)
                {
                    continue;
                }
                List<SrvRecord> recs = ParseResponse(res.Buffer, id);
                int flags = ReadU16(res.Buffer, 2);
                string status = recs.Count > 0 ? "ok"
                    : (flags & 0x000F) != 0 ? "rcode=" + (flags & 0x000F)
                    : (flags & 0x0200) != 0 ? "truncated"
                    : "no_record";
                return (recs, status, (int)sw.ElapsedMilliseconds);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (new List<SrvRecord>(), "timeout", (int)sw.ElapsedMilliseconds);
        }
        catch (SocketException ex)
        {
            return (new List<SrvRecord>(), "error: " + ex.SocketErrorCode, (int)sw.ElapsedMilliseconds);
        }
    }

    private static async Task<List<SrvRecord>> AskAsync(IPEndPoint server, byte[] query, ushort id, int timeoutMs, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try
        {
            using var udp = new UdpClient(server.AddressFamily);
            await udp.SendAsync(query, query.Length, server).ConfigureAwait(false);
            while (true)
            {
                UdpReceiveResult res = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                // 只认问过的那台服务器回来的、事务 id 对得上的包(防串包 / 伪造)
                if (!res.RemoteEndPoint.Address.Equals(server.Address) || res.Buffer.Length < 2 || ReadU16(res.Buffer, 0) != id)
                {
                    continue;
                }
                return ParseResponse(res.Buffer, id);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new List<SrvRecord>();
        }
        catch (SocketException)
        {
            return new List<SrvRecord>();
        }
        catch (ObjectDisposedException)
        {
            return new List<SrvRecord>();
        }
    }

    // ───────────────────────────── 编解码小工具 ─────────────────────────────

    private static void WriteU16(Stream s, int v)
    {
        s.WriteByte((byte)((v >> 8) & 0xFF));
        s.WriteByte((byte)(v & 0xFF));
    }

    private static int ReadU16(byte[] d, int off)
    {
        if (off + 1 >= d.Length)
        {
            throw new IndexOutOfRangeException();
        }
        return (d[off] << 8) | d[off + 1];
    }

    /// <summary>读一个(可能压缩的)域名;<paramref name="off"/> 前进到名字之后。最多跟 16 次指针防环。</summary>
    internal static string ReadName(byte[] d, ref int off)
    {
        var labels = new List<string>();
        int pos = off;
        bool jumped = false;
        int hops = 0;
        while (true)
        {
            if (pos >= d.Length)
            {
                throw new IndexOutOfRangeException();
            }
            int len = d[pos];
            if (len == 0)
            {
                pos++;
                break;
            }
            if ((len & 0xC0) == 0xC0)
            {
                if (pos + 1 >= d.Length || ++hops > 16)
                {
                    throw new IndexOutOfRangeException();
                }
                int ptr = ((len & 0x3F) << 8) | d[pos + 1];
                if (!jumped)
                {
                    off = pos + 2;
                    jumped = true;
                }
                pos = ptr;
                continue;
            }
            if (pos + 1 + len > d.Length)
            {
                throw new IndexOutOfRangeException();
            }
            labels.Add(Encoding.ASCII.GetString(d, pos + 1, len));
            pos += 1 + len;
        }
        if (!jumped)
        {
            off = pos;
        }
        return string.Join(".", labels);
    }
}
