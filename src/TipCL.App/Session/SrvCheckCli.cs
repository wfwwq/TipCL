// AI3-stability:`--srv-check` —— SRV 解析的真网络手动核验(不进单元测试的那一半)。
//
// ping_server 的 SRV 查询(Core/Launcher/DnsSrv)在单元测试里只对过本机假 DNS。这个开关对公开域名做真实 DNS 查询,
// 用来人工确认「报文拼得对、应答解析得对、系统 DNS 服务器找得到」:
//   dotnet TipCL.App.dll --srv-check                      默认问 4 个已知有 _minecraft._tcp 记录的服务器域名
//                                                          + 一个通用 SRV(_xmpp-server._tcp.jabber.org)
//   dotnet TipCL.App.dll --srv-check example.net          只问 _minecraft._tcp.example.net
//   dotnet TipCL.App.dll --srv-check _sip._tcp.example.net  名字里已带 ._tcp. / ._udp. 就原样问
//   --dns 8.8.8.8 [--dns 1.1.1.1:53]                      只问这些解析器(默认:系统 DNS + 8.8.8.8 + 1.1.1.1)
//   --timeout 3000                                         每次查询的超时(毫秒)
// 每个(解析器, 名字)单独查一次并打印结果;没带 --dns 时再按 ping_server 的真实路径(DnsSrv.ResolveAsync,系统 DNS)解析一遍。
// 退出码:0 = 至少有一次查询拿到了 SRV 记录;3 = 一条都没拿到(多半是没网 / UDP 53 被拦,如实报告即可);4 = 参数错。
// 输出是英文的协议诊断行(与 --ai-selftest 一样只给开发者 / 维护者看)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using TipCL.Core.Launcher;

namespace TipCL.App.Session;

/// <summary>`--srv-check` 的实现。</summary>
public static class SrvCheckCli
{
    public const string Flag = "--srv-check";

    public const int NoAnswer = 3;

    public const int BadArgs = 4;

    /// <summary>默认核验的域名(2026-11-21 经 dns.google 确认都有 _minecraft._tcp 记录)。</summary>
    public static readonly string[] DefaultMinecraftHosts = { "hypixel.net", "mineplex.com", "gommehd.net", "2b2t.org" };

    /// <summary>通用 SRV(不依赖 Minecraft 服务器):XMPP 联邦记录。</summary>
    public const string DefaultGenericName = "_xmpp-server._tcp.jabber.org";

    /// <summary>默认公共解析器。</summary>
    public static readonly string[] DefaultPublicResolvers = { "8.8.8.8", "1.1.1.1" };

    /// <summary>解析参数:要问的 SRV 名、解析器(空 = 默认)、超时;出错返回 error。</summary>
    public static (List<string> Names, List<IPEndPoint> Resolvers, int TimeoutMs, string Error) Parse(string[] args)
    {
        var names = new List<string>();
        var resolvers = new List<IPEndPoint>();
        int timeout = 3000;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a == Flag)
            {
                continue;
            }
            if (a == "--dns" || a == "--timeout")
            {
                if (i + 1 >= args.Length)
                {
                    return (names, resolvers, timeout, "missing value after " + a);
                }
                string v = args[++i];
                if (a == "--timeout")
                {
                    if (!int.TryParse(v, out timeout) || timeout <= 0 || timeout > 60000)
                    {
                        return (names, resolvers, 3000, "bad --timeout: " + v);
                    }
                    continue;
                }
                if (!TryEndPoint(v, out IPEndPoint? ep))
                {
                    return (names, resolvers, timeout, "bad --dns: " + v);
                }
                resolvers.Add(ep!);
                continue;
            }
            if (a.StartsWith("-", StringComparison.Ordinal))
            {
                return (names, resolvers, timeout, "unknown option: " + a);
            }
            names.Add(SrvName(a));
        }
        if (names.Count == 0)
        {
            names.AddRange(DefaultMinecraftHosts.Select(DnsSrv.MinecraftName));
            names.Add(DefaultGenericName);
        }
        return (names, resolvers, timeout, "");
    }

    /// <summary>位置参数 → SRV 名:已经是 _service._proto.name 就原样用,否则当 Minecraft 服务器域名。</summary>
    public static string SrvName(string arg)
    {
        string s = (arg ?? "").Trim().TrimEnd('.');
        return s.Contains("._tcp.", StringComparison.OrdinalIgnoreCase) || s.Contains("._udp.", StringComparison.OrdinalIgnoreCase)
            ? s
            : DnsSrv.MinecraftName(s);
    }

    private static bool TryEndPoint(string text, out IPEndPoint? ep)
    {
        ep = null;
        if (IPAddress.TryParse(text, out IPAddress? ip))
        {
            ep = new IPEndPoint(ip, DnsSrv.DnsPort);
            return true;
        }
        if (IPEndPoint.TryParse(text, out IPEndPoint? parsed))
        {
            ep = parsed.Port == 0 ? new IPEndPoint(parsed.Address, DnsSrv.DnsPort) : parsed;
            return true;
        }
        return false;
    }

    /// <summary>跑核验并把结果写出去,返回进程退出码。</summary>
    public static int Run(string[] args, TextWriter? output = null)
    {
        output ??= Console.Out;
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (Exception)
        {
        }
        (List<string> names, List<IPEndPoint> resolvers, int timeout, string error) = Parse(args);
        if (error.Length > 0)
        {
            output.WriteLine("srv-check: " + error);
            output.WriteLine("usage: --srv-check [--dns <ip[:port]>]... [--timeout <ms>] [domain | _service._proto.name]...");
            return BadArgs;
        }
        bool explicitResolvers = resolvers.Count > 0;
        var labelled = new List<(string Label, IPEndPoint Ep)>();
        if (explicitResolvers)
        {
            labelled.AddRange(resolvers.Select(r => ("dns " + r, r)));
        }
        else
        {
            List<IPEndPoint> system;
            try
            {
                system = DnsSrv.SystemServers();
            }
            catch (Exception ex)
            {
                system = new List<IPEndPoint>();
                output.WriteLine("srv-check: reading system dns servers failed: " + ex.GetType().Name + ": " + ex.Message);
            }
            labelled.AddRange(system.Select(r => ("system " + r, r)));
            labelled.AddRange(DefaultPublicResolvers.Select(s => new IPEndPoint(IPAddress.Parse(s), DnsSrv.DnsPort)).Select(r => ("public " + r, r)));
        }
        output.WriteLine("srv-check: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " timeout=" + timeout + "ms resolvers=" +
                         (labelled.Count == 0 ? "(none)" : string.Join(", ", labelled.Select(l => l.Label))));
        int queries = 0;
        int answered = 0;
        int withRecords = 0;
        foreach (string name in names)
        {
            foreach ((string label, IPEndPoint ep) in labelled)
            {
                queries++;
                (List<SrvRecord> recs, string status, int ms) = DnsSrv.QueryServerAsync(ep, name, timeout, CancellationToken.None).GetAwaiter().GetResult();
                if (status != "timeout" && !status.StartsWith("error", StringComparison.Ordinal))
                {
                    answered++;
                }
                if (recs.Count > 0)
                {
                    withRecords++;
                }
                string detail = recs.Count == 0 ? "" : " " + string.Join(" | ", recs.Select(r => r.Priority + " " + r.Weight + " " + r.Port + " " + r.Target));
                output.WriteLine("[" + label + "] " + name + " -> " + status + detail + " (" + ms + " ms)");
            }
            if (!explicitResolvers)
            {
                // ping_server 实际走的路径:系统 DNS 依次问,取 RFC 2782 挑出的那条
                SrvRecord? pick = DnsSrv.ResolveAsync(name, timeout, CancellationToken.None).GetAwaiter().GetResult();
                output.WriteLine("[resolve] " + name + " -> " + (pick == null ? "(no record, ping_server would fall back to A + 25565)" : pick.Target + ":" + pick.Port));
            }
        }
        output.WriteLine("srv-check: queries=" + queries + " answered=" + answered + " with_records=" + withRecords);
        return withRecords > 0 ? 0 : NoAnswer;
    }
}
