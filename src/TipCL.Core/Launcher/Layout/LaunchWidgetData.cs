// AI4-launch:启动页组件区的数据源(纯数据层,不碰界面):
//   - 本周游戏时长:读启动器自己的 playtime.json(Playtime.GetAllPlaytime),按本地日期把每局拆到周一~周日;
//   - 服务器状态:读当前实例的服务器列表(servers.dat + servers.json,Servers.ListServers)第一台可见的,
//     用 ServerPing 测在线人数与延迟。测试 / 巡检里把 ServerStatusHub.Source 换成假源,不联网。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core.Launcher.Ai;

namespace TipCL.Core.Launcher.Layout;

/// <summary>本周游戏时长(周一开头)。</summary>
public sealed class WeekPlaytime
{
    /// <summary>周一~周日每天的小时数。</summary>
    public double[] Hours { get; } = new double[7];

    /// <summary>今天是第几天(0 = 周一)。今天之后的柱子画成虚线(还没到)。</summary>
    public int Today { get; set; }

    public double TotalHours => Hours.Sum();

    public double LastWeekHours { get; set; }

    /// <summary>本周玩得最多的实例(没有记录时为空)。</summary>
    public string TopInstance { get; set; } = "";

    /// <summary>playtime.json 里是否有任何一局记录(区分「还没玩过」与「本周没玩」)。</summary>
    public bool AnyRecord { get; set; }
}

public static class LaunchWidgetData
{
    /// <summary>
    /// 按本地时间统计本周(周一 00:00 起)每天的游戏时长;跨午夜的一局按实际时间拆到两天。
    /// allInstances = Playtime.GetAllPlaytime() 的返回值({实例名: {total, versions, sessions:[{start, duration}]}})。
    /// 数据坏了的局直接跳过,不抛。
    /// </summary>
    public static WeekPlaytime Week(object? allInstances, DateTime nowLocal, TimeZoneInfo? tz = null)
    {
        tz ??= TimeZoneInfo.Local;
        var w = new WeekPlaytime();
        int dow = ((int)nowLocal.DayOfWeek + 6) % 7;
        w.Today = dow;
        DateTime monday = nowLocal.Date.AddDays(-dow);
        DateTime lastMonday = monday.AddDays(-7);
        var perInstance = new Dictionary<string, double>(StringComparer.Ordinal);
        if (allInstances is not PyDict map)
            return w;
        foreach (var kv in map)
        {
            if (kv.Value is not PyDict inst)
                continue;
            List<object> sessions;
            try { sessions = Py.ListOr(inst.Get("sessions")); }
            catch (Exception) { continue; }
            foreach (var s in sessions)
            {
                if (s is not PyDict sd)
                    continue;
                if (!TryNum(sd.Get("start"), out double start) || !TryNum(sd.Get("duration"), out double dur))
                    continue;
                if (dur <= 0 || dur > 7 * 86400 || start < 0 || start > 32503680000)
                    continue;
                w.AnyRecord = true;
                DateTime from = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UnixEpoch.AddSeconds(start), tz);
                DateTime to = from.AddSeconds(dur);
                w.LastWeekHours += Overlap(from, to, lastMonday, monday) / 3600.0;
                // 本周逐日
                for (int d = 0; d < 7; d++)
                {
                    double sec = Overlap(from, to, monday.AddDays(d), monday.AddDays(d + 1));
                    if (sec <= 0)
                        continue;
                    w.Hours[d] += sec / 3600.0;
                    perInstance[kv.Key] = perInstance.TryGetValue(kv.Key, out var t) ? t + sec : sec;
                }
            }
        }
        if (perInstance.Count > 0)
            w.TopInstance = perInstance.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).First().Key;
        return w;
    }

    private static bool TryNum(object? o, out double v)
    {
        switch (o)
        {
            case long l: v = l; return true;
            case int i: v = i; return true;
            case double d when !double.IsNaN(d) && !double.IsInfinity(d): v = d; return true;
            default: v = 0; return false;
        }
    }

    private static double Overlap(DateTime a0, DateTime a1, DateTime b0, DateTime b1)
    {
        DateTime s = a0 > b0 ? a0 : b0;
        DateTime e = a1 < b1 ? a1 : b1;
        return e > s ? (e - s).TotalSeconds : 0;
    }

    /// <summary>小时数的显示:一位小数,去掉多余的 .0(6.2 / 6 / 0.4)。</summary>
    public static string Hours1(double h)
    {
        double r = Math.Round(h, 1, MidpointRounding.AwayFromZero);
        return r.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static readonly (string Prefix, string Label)[] LoaderPrefixes =
    {
        ("fabric-loader-", "Fabric"),
        ("quilt-loader-", "Quilt"),
        ("neoforge-", "NeoForge"),
        ("optifine-", "OptiFine"),
        ("liteloader-", "LiteLoader"),
        ("forge-", "Forge"),
    };

    private static readonly System.Text.RegularExpressions.Regex McHead = new(@"^(\d+\.\d+(?:\.\d+)?)", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex VanillaLike = new(@"^(\d+\.\d+.*|\d\dw\d\d[a-z]|[ab]\d[\d.]*.*|rd-\d+|inf-\d+|c\d[\d.]*.*)$", System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// 启动页大标题用的版本友好名(launch_v3:「Fabric 1.20.1」):加载器名 + 游戏版本;原版 =「Minecraft 1.20.1」;
    /// 认不出的自定义名原样。inheritsFrom = 版本 json 的 inheritsFrom(有就以它为游戏版本)。
    /// </summary>
    public static string FriendlyVersion(string? id, string? inheritsFrom = null)
    {
        id = (id ?? "").Trim();
        if (id.Length == 0)
            return "";
        string low = id.ToLowerInvariant();
        string mc = (inheritsFrom ?? "").Trim();
        string loader = "";
        foreach (var (prefix, label) in LoaderPrefixes)
        {
            if (!low.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            loader = label;
            string rest = id.Substring(prefix.Length);
            if (mc.Length == 0 && (label == "Fabric" || label == "Quilt"))
            {
                int dash = rest.LastIndexOf('-');
                if (dash > 0)
                    mc = rest.Substring(dash + 1);
            }
            else if (mc.Length == 0 && label == "NeoForge")
            {
                // NeoForge 21.1.x ↔ Minecraft 1.21.1;20.4.x ↔ 1.20.4;21.0.x ↔ 1.21
                var parts = rest.Split('.', '-');
                if (parts.Length >= 2 && int.TryParse(parts[0], out int major) && int.TryParse(parts[1], out int minor))
                    mc = minor == 0 ? "1." + major : "1." + major + "." + minor;
            }
            break;
        }
        if (loader.Length == 0)
        {
            if (low.Contains("neoforge")) loader = "NeoForge";
            else if (low.Contains("forge")) loader = "Forge";
            else if (low.Contains("optifine")) loader = "OptiFine";
            else if (low.Contains("fabric")) loader = "Fabric";
            else if (low.Contains("quilt")) loader = "Quilt";
            if (loader.Length > 0 && mc.Length == 0)
            {
                var m = McHead.Match(id);
                if (m.Success)
                    mc = m.Groups[1].Value;
            }
        }
        if (loader.Length > 0)
            return mc.Length > 0 ? loader + " " + mc : id;
        if (mc.Length > 0 && !string.Equals(mc, id, StringComparison.Ordinal))
            return id; // 自定义名的原版副本:用户起的名字就是友好名
        return VanillaLike.IsMatch(id) ? "Minecraft " + id : id;
    }
}

/// <summary>服务器状态组件要显示的一份快照。</summary>
public sealed class ServerStatus
{
    /// <summary>实例里一台服务器都没有。</summary>
    public bool NoServer { get; set; }

    public string Name { get; set; } = "";

    public string Host { get; set; } = "";

    public long Port { get; set; } = 25565;

    /// <summary>ping 通了。</summary>
    public bool Ok { get; set; }

    public long Online { get; set; }

    public long Max { get; set; }

    public long LatencyMs { get; set; }

    public List<string> Players { get; set; } = new();

    public string Error { get; set; } = "";

    /// <summary>「host:port」(端口 25565 时只写 host)。</summary>
    public string Address => Port == 25565 ? Host : Host + ":" + Port;
}

/// <summary>服务器状态的数据源(真源读 servers.dat + ping;测试 / 巡检用假源)。</summary>
public interface IServerStatusSource
{
    Task<ServerStatus> FetchAsync(string instance, CancellationToken ct);
}

/// <summary>真源:当前实例服务器列表里第一台未隐藏的 → ServerPing。</summary>
public sealed class RealServerStatusSource : IServerStatusSource
{
    public async Task<ServerStatus> FetchAsync(string instance, CancellationToken ct)
    {
        var st = new ServerStatus();
        PyDict? first = null;
        try
        {
            first = await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(instance))
                    return null;
                var rows = Servers.ListServers(new Instance(instance));
                return rows.OfType<PyDict>().FirstOrDefault(r => !Py.Truthy(r.Get("hidden", false)) && Py.S(r.Get("ip", "")).Length > 0);
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            st.NoServer = true;
            st.Error = ex.Message;
            return st;
        }
        if (first == null)
        {
            st.NoServer = true;
            return st;
        }
        st.Name = Py.S(first.Get("name", ""));
        st.Host = Py.S(first.Get("ip", ""));
        try { st.Port = Py.Int(first.Get("port", 25565L)); }
        catch (Exception) { st.Port = 25565; }
        if (st.Name.Length == 0)
            st.Name = st.Host;
        try
        {
            PyDict r = await ServerPing.PingAsync(st.Host, st.Port, 3000, ct, resolveSrv: st.Port == 25565).ConfigureAwait(false);
            st.Ok = Py.Truthy(r.Get("ok", false));
            if (st.Ok)
            {
                st.Online = Py.Int(r.Get("online", 0L));
                st.Max = Py.Int(r.Get("max", 0L));
                st.LatencyMs = Py.Int(r.Get("latency_ms", 0L));
                foreach (var p in Py.ListOr(r.Get("players_sample", null)))
                    if (p is string s && s.Length > 0)
                        st.Players.Add(s);
            }
            else
            {
                st.Error = Py.S(r.Get("message", r.Get("error", "")));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            st.Ok = false;
            st.Error = ex.Message;
        }
        return st;
    }
}

/// <summary>服务器状态数据源的挂点:默认真源;测试 / 巡检替换成假源。</summary>
public static class ServerStatusHub
{
    public static IServerStatusSource Source { get; set; } = new RealServerStatusSource();
}

/// <summary>固定返回一份快照的假源(测试、无窗口巡检用)。</summary>
public sealed class FixedServerStatusSource : IServerStatusSource
{
    private readonly ServerStatus _status;

    public FixedServerStatusSource(ServerStatus status) => _status = status;

    public int Calls { get; private set; }

    public Task<ServerStatus> FetchAsync(string instance, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(_status);
    }
}
