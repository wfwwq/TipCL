// AI2-stability 包的 Core 回归:
//   1 EffectiveJvm:内存 / GC「实际生效值」只算一处,LaunchFlow / Preflight / preflight_check 用的是同一份;
//   2 DnsSrv + ServerPing:SRV 报文编解码(含名字压缩)、resolv.conf 解析、本机 UDP 假 DNS → 跟随 SRV 连到回环上的假服务器,
//     查不到 / DNS 不回话时回落到 A 记录 + 原端口;
//   3 apply_instance_upgrade 原地升级:撤回时删掉本次新建的版本目录,已存在的不删;
//   4 联机等待时长:可配置常量 + 每次等待的实际耗时有记录(假门面,不联网);
//   5 并行只读工具背后的 Core:假 HTTP(当前异步流的假响应)下并发搜索 / 并发拉版本清单。
// 全部不联网:HTTP 走 TestNetworkIsolation 的假响应,DNS 走本机 UDP 假服务器,MC 状态查询走本机 TCP 假服务器。
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using TipCL.Tests.App;
using Xunit;

namespace TipCL.Tests.Core;

public class EffectiveJvmTests : IDisposable
{
    private readonly NfTempInstance _t = new("ai2jvm");

    public void Dispose() => _t.Dispose();

    private string Version(string id, long? memory = null, string? gc = null)
    {
        _t.Version(id);
        var vs = new PyDict();
        if (memory != null)
        {
            vs["memory_mb"] = memory.Value;
        }
        if (gc != null)
        {
            vs["gc"] = gc;
        }
        VersionSettings.Save(_t.Inst, id, vs);
        return id;
    }

    [Fact]
    public void Resolve_Order_Version_Instance_Caller_GlobalOrDefault()
    {
        // 什么都没设:落到全局或兜底(全局值由别的用例共享,这里只认来源与正数)
        PyDict none = EffectiveJvm.Resolve(_t.Inst);
        Assert.Contains(Py.S(none.Get("memory_source")), new[] { EffectiveJvm.SourceGlobal, EffectiveJvm.SourceDefault });
        Assert.True(Py.Int(none.Get("memory_mb")) > 0);

        // 调用方 > 全局
        PyDict caller = EffectiveJvm.Resolve(_t.Inst, null, 2500L);
        Assert.Equal(2500L, Py.Int(caller.Get("memory_mb")));
        Assert.Equal(EffectiveJvm.SourceCaller, Py.S(caller.Get("memory_source")));
        // 非法的调用方值不算数,落到下一层
        Assert.NotEqual(EffectiveJvm.SourceCaller, Py.S(EffectiveJvm.Resolve(_t.Inst, null, "abc").Get("memory_source")));
        Assert.NotEqual(EffectiveJvm.SourceCaller, Py.S(EffectiveJvm.Resolve(_t.Inst, null, -5L).Get("memory_source")));

        // 实例 > 调用方
        JvmTuning.ApplyToInstance(_t.Inst, 7000, "g1");
        PyDict inst = EffectiveJvm.Resolve(_t.Inst, null, 2500L);
        Assert.Equal(7000L, Py.Int(inst.Get("memory_mb")));
        Assert.Equal(EffectiveJvm.SourceInstance, Py.S(inst.Get("memory_source")));
        Assert.Equal("g1", Py.S(inst.Get("gc_preset")));
        Assert.Equal(EffectiveJvm.SourceInstance, Py.S(inst.Get("gc_source")));

        // 版本 > 实例
        string ver = Version("ai2-1.20.1", 3000, "ZGC ");
        PyDict v = EffectiveJvm.Resolve(_t.Inst, ver, 2500L);
        Assert.Equal(3000L, Py.Int(v.Get("memory_mb")));
        Assert.Equal(EffectiveJvm.SourceVersion, Py.S(v.Get("memory_source")));
        Assert.Equal("zgc", Py.S(v.Get("gc_preset")));

        // JvmTuning.Effective(给 AI 看的)与不带调用方的 Resolve 完全一致
        Assert.Equal(PyJson.Dumps(EffectiveJvm.Resolve(_t.Inst, ver)), PyJson.Dumps(JvmTuning.Effective(_t.Inst, ver)));
    }

    [Fact]
    public void LaunchFlowPrepare_UsesTheSameResolution()
    {
        JvmTuning.ApplyToInstance(_t.Inst, 6144, "g1_tuned");
        string ver = Version("ai2-1.20.4");
        PyDict prep = LaunchFlow.Prepare(_t.Inst, ver, new List<object>(), 2048L);
        PyDict eff = EffectiveJvm.Resolve(_t.Inst, ver, 2048L);
        Assert.Equal(Py.Int(eff.Get("memory_mb")), Py.Int(prep.Get("memory_mb")));
        Assert.Equal(6144L, Py.Int(prep.Get("memory_mb"))); // 实例设置盖过启动页传的 2048
        Assert.Contains("-XX:+UseG1GC", Py.S(prep.Get("jvm_args")));
    }

    [Fact]
    public void Preflight_ComparesEffectiveMemory_NotTheCallersValue()
    {
        JvmTuning.ApplyToInstance(_t.Inst, 4500, "auto");
        var mine = new AsyncLocal<bool> { Value = true };
        Func<long?>? saved = Preflight.AvailPhysMbHook;
        // 只对本用例的异步流回答「可用 5000 MB」;别的用例拿到 null = 照常探测
        Preflight.AvailPhysMbHook = () => mine.Value ? 5000L : saved?.Invoke();
        try
        {
            PyDict r = Preflight.CheckLaunch(_t.Inst, "", 1024);
            PyDict? high = Py.ListOr(r.Get("items")).Select(Py.D).FirstOrDefault(i => Py.S(i.Get("code")) == "memory_high");
            Assert.NotNull(high);
            Assert.Contains("4500", Py.S(high!.Get("detail")));   // 比的是实例生效的 4500,不是调用方的 1024
            Assert.DoesNotContain("1024 MB", Py.S(high.Get("detail")));
        }
        finally
        {
            Preflight.AvailPhysMbHook = saved;
        }
    }

    [Fact]
    public void PreflightCheck_ReportsEffectiveMemoryAndItsSource()
    {
        JvmTuning.ApplyToInstance(_t.Inst, 7000, "auto");
        PyDict r = AiPreflight.Check(_t.Inst, "", 1024, null, PyDict.Of("ram_mb", 8000L));
        Assert.Equal(7000L, Py.Int(r.Get("memory_mb")));
        Assert.Equal(EffectiveJvm.SourceInstance, Py.S(r.Get("memory_source")));
        // 7000 > 8000 × 0.8:以前拿调用方的 1024 去比,这条提醒出不来
        Assert.Contains(Py.ListOr(r.Get("items")).Select(Py.D), i => Py.S(i.Get("code")) == "memory_near_physical");
    }
}

// ───────────────────────────── SRV ─────────────────────────────

public class DnsSrvTests
{
    /// <summary>手搓一个 SRV 应答;<paramref name="compressTarget"/> 为真时目标名用指针指回问题里的后缀。</summary>
    internal static byte[] SrvResponse(ushort id, string qname, int rcode, params (int Pri, int W, int Port, string Target, bool Compress)[] recs)
    {
        var ms = new MemoryStream();
        void U16(int v) { ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
        void Name(string n)
        {
            foreach (string l in n.Split('.'))
            {
                byte[] b = Encoding.ASCII.GetBytes(l);
                ms.WriteByte((byte)b.Length);
                ms.Write(b);
            }
            ms.WriteByte(0);
        }
        U16(id);
        U16(0x8180 | rcode);
        U16(1);
        U16(recs.Length);
        U16(0);
        U16(0);
        Name(qname);
        U16(33);
        U16(1);
        // 问题里最后两段(例如 example.test)的偏移,给压缩指针用
        string[] labels = qname.Split('.');
        int suffixOffset = 12 + labels.Take(labels.Length - 2).Sum(l => l.Length + 1);
        foreach (var r in recs)
        {
            U16(0xC00C); // 名字 = 指向问题
            U16(33);
            U16(1);
            U16(0);
            U16(300);
            var rd = new MemoryStream();
            void R16(int v) { rd.WriteByte((byte)(v >> 8)); rd.WriteByte((byte)v); }
            R16(r.Pri);
            R16(r.W);
            R16(r.Port);
            if (r.Compress)
            {
                byte[] b = Encoding.ASCII.GetBytes(r.Target);
                rd.WriteByte((byte)b.Length);
                rd.Write(b);
                R16(0xC000 | suffixOffset);
            }
            else
            {
                foreach (string l in r.Target.Split('.'))
                {
                    byte[] b = Encoding.ASCII.GetBytes(l);
                    rd.WriteByte((byte)b.Length);
                    rd.Write(b);
                }
                rd.WriteByte(0);
            }
            byte[] data = rd.ToArray();
            U16(data.Length);
            ms.Write(data);
        }
        return ms.ToArray();
    }

    [Fact]
    public void Query_IsAStandardSrvQuestion()
    {
        byte[] q = DnsSrv.BuildQuery(0x1234, "_minecraft._tcp.play.example.test");
        Assert.Equal(0x12, q[0]);
        Assert.Equal(0x34, q[1]);
        Assert.Equal(0x01, q[2]); // RD
        Assert.Equal(1, (q[4] << 8) | q[5]);
        int off = 12;
        Assert.Equal("_minecraft._tcp.play.example.test", DnsSrv.ReadName(q, ref off));
        Assert.Equal(33, (q[off] << 8) | q[off + 1]);
        Assert.Equal(1, (q[off + 2] << 8) | q[off + 3]);
        Assert.Throws<ArgumentException>(() => DnsSrv.BuildQuery(1, "a..b"));
    }

    [Fact]
    public void Response_ParsesRecords_FollowsCompression_AndPicksByPriorityThenWeight()
    {
        const string q = "_minecraft._tcp.play.example.test";
        byte[] resp = SrvResponse(7, q, 0,
            (20, 100, 25570, "backup.example.test", false),
            (10, 5, 25566, "mc", true),
            (10, 50, 25567, "mc2", true));
        List<SrvRecord> recs = DnsSrv.ParseResponse(resp, 7);
        Assert.Equal(3, recs.Count);
        Assert.Contains(recs, r => r.Target == "mc.example.test" && r.Port == 25566);
        SrvRecord best = DnsSrv.Pick(recs)!;
        Assert.Equal("mc2.example.test", best.Target); // 同优先级 10,权重 50 > 5
        Assert.Equal(25567, best.Port);

        Assert.Empty(DnsSrv.ParseResponse(resp, 8));                        // 事务 id 不符
        Assert.Empty(DnsSrv.ParseResponse(SrvResponse(7, q, 3), 7));        // NXDOMAIN
        Assert.Empty(DnsSrv.ParseResponse(resp.Take(20).ToArray(), 7));     // 截断的坏报文
        byte[] notResponse = (byte[])resp.Clone();
        notResponse[2] &= 0x7F;
        Assert.Empty(DnsSrv.ParseResponse(notResponse, 7));                 // QR=0
    }

    [Fact]
    public void ResolvConf_AndWhichHostsGetQueried()
    {
        List<IPAddress> ns = DnsSrv.ParseResolvConf("# comment\nnameserver 10.0.0.2\nnameserver  ::1 # local\nsearch lan\nnameserver bogus\n");
        Assert.Equal(new[] { IPAddress.Parse("10.0.0.2"), IPAddress.IPv6Loopback }, ns);
        Assert.True(DnsSrv.ShouldQuery("play.example.test"));
        Assert.False(DnsSrv.ShouldQuery("127.0.0.1"));
        Assert.False(DnsSrv.ShouldQuery("[::1]"));
        Assert.False(DnsSrv.ShouldQuery("localhost"));
        Assert.False(DnsSrv.ShouldQuery("myserver"));
        Assert.Equal("_minecraft._tcp.play.example.test", DnsSrv.MinecraftName("play.example.test."));
        Assert.False(ServerPing.HasExplicitPort("play.example.test", null));
        Assert.True(ServerPing.HasExplicitPort("play.example.test:25565", null));
        Assert.True(ServerPing.HasExplicitPort("play.example.test", 25565L));
        Assert.True(ServerPing.HasExplicitPort("[::1]:25565", null));
        Assert.False(ServerPing.HasExplicitPort("[::1]", null));
    }
}

/// <summary>本机 UDP 假 DNS:按收到的事务 id 回预先写好的应答(或者一直不回)。</summary>
internal sealed class FakeDns : IDisposable
{
    private readonly UdpClient _udp = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly CancellationTokenSource _cts = new();
    public readonly ConcurrentQueue<string> Questions = new();

    public FakeDns(Func<ushort, string, byte[]?> answer)
    {
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                UdpReceiveResult r;
                try
                {
                    r = await _udp.ReceiveAsync(_cts.Token);
                }
                catch (Exception)
                {
                    return;
                }
                ushort id = (ushort)((r.Buffer[0] << 8) | r.Buffer[1]);
                int off = 12;
                string name = DnsSrv.ReadName(r.Buffer, ref off);
                Questions.Enqueue(name);
                byte[]? resp = answer(id, name);
                if (resp != null)
                {
                    try { await _udp.SendAsync(resp, resp.Length, r.RemoteEndPoint); } catch (Exception) { }
                }
            }
        });
    }

    public IPEndPoint EndPoint => (IPEndPoint)_udp.Client.LocalEndPoint!;

    public void Dispose()
    {
        _cts.Cancel();
        _udp.Dispose();
    }
}

/// <summary>DnsSrv.ServersHook 是进程级静态:用到它的用例串行(本集合内)。</summary>
[CollectionDefinition("AI2 DNS 注入", DisableParallelization = true)]
public class Ai2DnsCollection
{
}

[Collection("AI2 DNS 注入")]
public class ServerPingSrvTests
{
    private const string StatusJson = "{\"version\":{\"name\":\"Fake 1.21\",\"protocol\":767},\"players\":{\"max\":20,\"online\":3},\"description\":\"srv ok\"}";

    /// <summary>回环上的最小 SLP 假服务器:握手 + 状态请求 → JSON → pong。</summary>
    private static (int Port, Task Done, Func<string> HandshakeHost) StartMc()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        string host = "";
        Task done = Task.Run(async () =>
        {
            try
            {
                using TcpClient c = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(10));
                NetworkStream s = c.GetStream();
                byte[] hs = await ServerPing.ReadPacketAsync(s, CancellationToken.None);
                int o = 0;
                ServerPing.ReadVarInt(hs, ref o);
                ServerPing.ReadVarInt(hs, ref o);
                int len = ServerPing.ReadVarInt(hs, ref o);
                host = Encoding.UTF8.GetString(hs, o, len);
                await ServerPing.ReadPacketAsync(s, CancellationToken.None);
                await s.WriteAsync(ServerPing.Packet(0x00, w => ServerPing.WriteString(w, StatusJson)));
                byte[] ping = await ServerPing.ReadPacketAsync(s, CancellationToken.None);
                await s.WriteAsync(ServerPing.Packet(0x01, w => w.Write(ping, 1, ping.Length - 1)));
                await Task.Delay(100);
            }
            catch (Exception)
            {
            }
            finally
            {
                listener.Stop();
            }
        });
        return (port, done, () => host);
    }

    private static IDisposable UseDns(FakeDns dns)
    {
        Func<List<IPEndPoint>>? saved = DnsSrv.ServersHook;
        DnsSrv.ServersHook = () => new List<IPEndPoint> { dns.EndPoint };
        return new Restore(() => DnsSrv.ServersHook = saved);
    }

    private sealed class Restore : IDisposable
    {
        private readonly Action _a;
        public Restore(Action a) => _a = a;
        public void Dispose() => _a();
    }

    [Fact]
    public async Task Ping_FollowsSrvRecord_ToTheAdvertisedTargetAndPort()
    {
        (int mcPort, Task done, Func<string> hsHost) = StartMc();
        using var dns = new FakeDns((id, name) => DnsSrvTests.SrvResponse(id, name, 0, (0, 5, mcPort, "127.0.0.1", false)));
        using (UseDns(dns))
        {
            // 25565 上什么都没有:只有跟随 SRV 才连得上
            PyDict r = await ServerPing.PingAsync("play.example.test", 25565, 3000, CancellationToken.None, resolveSrv: true);
            await done;
            Assert.True(Py.Truthy(r.Get("ok")), PyJson.Dumps(r));
            Assert.Equal("srv", Py.S(r.Get("resolved_via")));
            Assert.Equal("127.0.0.1", Py.S(r.Get("connect_host")));
            Assert.Equal(mcPort, Py.Int(r.Get("connect_port")));
            Assert.Equal(3, Py.Int(r.Get("online")));
            Assert.Equal("_minecraft._tcp.play.example.test", Py.S(Py.D(r.Get("srv")).Get("name")));
            Assert.Equal("play.example.test", Py.S(r.Get("host")));
            Assert.Equal("play.example.test", hsHost()); // 握手里报的是玩家输入的地址(代理服按它分流)
        }
        Assert.Contains("_minecraft._tcp.play.example.test", dns.Questions);
    }

    [Fact]
    public async Task NoSrvRecord_FallsBackToTheHostAndGivenPort()
    {
        using var dns = new FakeDns((id, name) => DnsSrvTests.SrvResponse(id, name, 3)); // NXDOMAIN
        using (UseDns(dns))
        {
            var t = await ServerPing.ResolveTargetAsync("play.example.test", 25565, true, 3000, CancellationToken.None);
            Assert.Equal(("play.example.test", 25565L, "a"), (t.Host, t.Port, t.Via));
            Assert.Null(t.Srv);
        }
    }

    [Fact]
    public async Task SilentDns_TimesOutQuickly_AndFallsBack()
    {
        using var dns = new FakeDns((_, _) => null); // 收到也不回
        using (UseDns(dns))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var t = await ServerPing.ResolveTargetAsync("play.example.test", 25565, true, 800, CancellationToken.None);
            sw.Stop();
            Assert.Equal("a", t.Via);
            Assert.Equal(25565L, t.Port);
            Assert.True(sw.ElapsedMilliseconds < 2500, "SRV 超时没生效:" + sw.ElapsedMilliseconds + " ms");
        }
    }

    [Fact]
    public async Task ExplicitPortOrIpLiteral_NeverQueriesDns()
    {
        using var dns = new FakeDns((id, name) => DnsSrvTests.SrvResponse(id, name, 0, (0, 0, 1, "127.0.0.1", false)));
        using (UseDns(dns))
        {
            var lit = await ServerPing.ResolveTargetAsync("127.0.0.1", 25565, true, 1000, CancellationToken.None);
            Assert.Equal("literal", lit.Via);
            var off = await ServerPing.ResolveTargetAsync("play.example.test", 25570, false, 1000, CancellationToken.None);
            Assert.Equal(("play.example.test", 25570L, "a"), (off.Host, off.Port, off.Via));
        }
        Assert.Empty(dns.Questions);
    }

    [Fact]
    public async Task PingServerTool_OnlyUsesSrvWhenAddressHasNoPort()
    {
        (int mcPort, Task done, _) = StartMc();
        using var dns = new FakeDns((id, name) => DnsSrvTests.SrvResponse(id, name, 0, (0, 0, mcPort, "127.0.0.1", false)));
        using (UseDns(dns))
        {
            var runner = new ToolRunner(new NfFakeApi()) { PermissionMode = "default" };
            var call = new ToolCall("p1", "ping_server", PyDict.Of("address", "play.example.test", "timeout_ms", 3000L));
            string text = await runner.RunAsync(call);
            await done;
            Assert.Equal(ToolCallStatus.COMPLETED, call.status);
            PyDict r = Py.D(PyJson.Loads(text));
            Assert.Equal("srv", Py.S(r.Get("resolved_via")));
            Assert.True(Py.Truthy(r.Get("ok")), text);
        }
    }
}

// ───────────────────────────── 原地升级的撤回 ─────────────────────────────

public class UpgradeCheckpointTests
{
    private static void VersionJson(Instance inst, string id, string? inheritsFrom = null)
    {
        string dir = Path.Combine(inst.VersionsDir(), id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, id + ".json"),
            "{\"id\":\"" + id + "\"" + (inheritsFrom != null ? ",\"inheritsFrom\":\"" + inheritsFrom + "\"" : "") + "}");
        File.WriteAllBytes(Path.Combine(dir, id + ".jar"), new byte[] { 0x50, 0x4B, 3, 4 });
    }

    private static PyDict Args(Instance inst, bool inPlace = true)
        => PyDict.Of("instance", inst.name, "target_version", "1.21.1", "in_place", inPlace);

    /// <summary>照 AiAgent.SnapshotForTool 的顺序记检查点(名单 + 内容快照)。</summary>
    private static void Snapshot(string chat, string turn, Instance inst, PyDict args)
    {
        UpgradeCheckpoint.SnapshotFor(chat, turn, "apply_instance_upgrade", args, inst.name);
        Assert.True(Py.Truthy(Checkpoint.Snapshot(chat, turn, Tools.AffectedPaths("apply_instance_upgrade", args, inst.name)).Get("ok")));
    }

    [Fact]
    public void InPlaceRewind_DeletesNewlyInstalledVersionDirs_KeepsExistingAndUnrelatedOnes()
    {
        using var t = new NfTempInstance("ai2up");
        VersionJson(t.Inst, "1.20.1");
        t.Write("mods/old-mod-1.20.1.jar", "old");
        string chat = "ai2up-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        const string turn = "t1";
        PyDict args = Args(t.Inst);
        Snapshot(chat, turn, t.Inst, args);

        // 「升级」:装目标版本 + Fabric(继承 1.21.1),换模组;同时别的东西装了个不相干的版本
        VersionJson(t.Inst, "1.21.1");
        VersionJson(t.Inst, "fabric-loader-0.16.5-1.21.1", "1.21.1");
        VersionJson(t.Inst, "user-own-1.19.2", "1.19.2");
        File.Delete(t.PathOf("mods", "old-mod-1.20.1.jar"));
        t.Write("mods/new-mod-1.21.1.jar", "new");

        PyDict rb = Checkpoint.Rollback(chat, turn);
        Assert.True(Py.Truthy(rb.Get("ok")), PyJson.Dumps(rb));
        Assert.False(Directory.Exists(Path.Combine(t.Inst.VersionsDir(), "1.21.1")), "新装的原版目录应被删掉");
        Assert.False(Directory.Exists(Path.Combine(t.Inst.VersionsDir(), "fabric-loader-0.16.5-1.21.1")), "新装的加载器目录应被删掉");
        Assert.True(Directory.Exists(Path.Combine(t.Inst.VersionsDir(), "1.20.1")), "原来就有的版本不能删");
        Assert.True(Directory.Exists(Path.Combine(t.Inst.VersionsDir(), "user-own-1.19.2")), "不属于目标版本的新目录不能误删");
        Assert.True(File.Exists(t.PathOf("mods", "old-mod-1.20.1.jar")));
        Assert.False(File.Exists(t.PathOf("mods", "new-mod-1.21.1.jar")));
    }

    [Fact]
    public void InPlaceRewind_DoesNotDeleteTargetVersionThatAlreadyExisted()
    {
        using var t = new NfTempInstance("ai2up");
        VersionJson(t.Inst, "1.21.1"); // 升级前就装过目标版本
        string chat = "ai2up-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        PyDict args = Args(t.Inst);
        Snapshot(chat, "t1", t.Inst, args);
        VersionJson(t.Inst, "neoforge-21.1.77", "1.21.1");
        Checkpoint.Rollback(chat, "t1");
        Assert.True(Directory.Exists(Path.Combine(t.Inst.VersionsDir(), "1.21.1")));
        Assert.False(Directory.Exists(Path.Combine(t.Inst.VersionsDir(), "neoforge-21.1.77")));
    }

    [Fact]
    public void CopyMode_AndOtherTools_AddNoVersionWatch()
    {
        using var t = new NfTempInstance("ai2up");
        Assert.Empty(UpgradeCheckpoint.Watches("apply_instance_upgrade", Args(t.Inst, inPlace: false), t.Name));
        Assert.Empty(UpgradeCheckpoint.Watches("install_mod", PyDict.Of("instance", t.Name), t.Name));
        var w = UpgradeCheckpoint.Watches("apply_instance_upgrade", Args(t.Inst), t.Name);
        Assert.Single(w);
        Assert.Equal("1.21.1", w[0].MatchMc);
        Assert.Equal(Path.GetFullPath(t.Inst.VersionsDir()), Path.GetFullPath(w[0].Parent));
        Assert.True(Checkpoint.BelongsToMc("x", "1.21.1", "1.21.1"));
        Assert.False(Checkpoint.BelongsToMc(Path.Combine(t.Inst.VersionsDir(), "nope"), "nope", "1.21.1"));
    }
}

// ───────────────────────────── 联机等待 ─────────────────────────────

public class MultiplayerTimingTests
{
    [Fact]
    public void Defaults_AreNamedConstants_AndConfigCanOverride()
    {
        Assert.Equal(15000, MultiplayerTiming.DefaultStartWaitMs);
        Assert.Equal(6000, MultiplayerTiming.DefaultRoomWaitMs);
        object? saved = Config.CONFIG.Get(MultiplayerTiming.RoomWaitKey);
        try
        {
            Config.CONFIG.Set(MultiplayerTiming.RoomWaitKey, 2500L);
            Assert.Equal(2500, MultiplayerTiming.RoomWaitMs());
            Config.CONFIG.Set(MultiplayerTiming.RoomWaitKey, 999999L);
            Assert.Equal(MultiplayerTiming.MaxWaitMs, MultiplayerTiming.RoomWaitMs());
            Config.CONFIG.Set(MultiplayerTiming.RoomWaitKey, "oops");
            Assert.Equal(MultiplayerTiming.DefaultRoomWaitMs, MultiplayerTiming.RoomWaitMs());
        }
        finally
        {
            Config.CONFIG.Set(MultiplayerTiming.RoomWaitKey, saved!);
        }
        Assert.Equal(MultiplayerTiming.DefaultStartWaitMs, MultiplayerTiming.StartWaitMs());
    }

    [Fact]
    public async Task HostWaits_RecordActualElapsedTime_WithoutRealNetwork()
    {
        var api = new ScriptedApi();
        int snaps = 0;
        api.Handlers["terracotta_snapshot"] = _ =>
        {
            int n = Interlocked.Increment(ref snaps);
            // 第 1 次:装了没跑(触发等内核);第 3 次起在跑;开房后第 5 次起有房间号
            return PyDict.Of("supported", true, "installed", true, "running", n >= 3, "label", "x", "room", n >= 5 ? "ROOM-42" : "");
        };
        api.Handlers["terracotta_prepare"] = _ => PyDict.Of("task_id", "tp1");
        int savedPoll = ToolRunner.MultiplayerPollMs;
        ToolRunner.MultiplayerPollMs = 20;
        try
        {
            var runner = new ToolRunner(api) { PermissionMode = "default", Instance = "default" };
            runner.Confirm = (_, _, _, _) => Task.FromResult((true, false));
            int before = MultiplayerTiming.Recent().Count;
            var call = new ToolCall("m1", "multiplayer", PyDict.Of("action", "host"));
            string text = await runner.RunAsync(call);
            Assert.Contains("ROOM-42", text);
            List<MultiplayerTiming.Wait> mine = MultiplayerTiming.Recent().Where(w => w.At >= DateTime.Now.AddMinutes(-1)).ToList();
            MultiplayerTiming.Wait start = mine.Last(w => w.What == MultiplayerTiming.WaitStart);
            MultiplayerTiming.Wait room = mine.Last(w => w.What == MultiplayerTiming.WaitRoom);
            Assert.True(start.Satisfied && room.Satisfied);
            Assert.Equal(MultiplayerTiming.DefaultStartWaitMs, start.LimitMs);
            Assert.Equal(MultiplayerTiming.DefaultRoomWaitMs, room.LimitMs);
            Assert.True(start.Polls >= 1, "要轮询到第 3 次快照才算起来");
            Assert.True(start.ElapsedMs >= 15 && start.ElapsedMs < MultiplayerTiming.DefaultStartWaitMs, "实际耗时 " + start.ElapsedMs);
            Assert.True(room.ElapsedMs < MultiplayerTiming.DefaultRoomWaitMs);
        }
        finally
        {
            ToolRunner.MultiplayerPollMs = savedPoll;
        }
    }
}

// ───────────────────────────── 并行只读工具的线程安全 ─────────────────────────────

public class ParallelReadToolsStressTests
{
    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Query(HttpRequestMessage req)
    {
        string q = req.RequestUri?.Query ?? "";
        foreach (string part in q.TrimStart('?').Split('&'))
        {
            if (part.StartsWith("query=", StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(part.Substring(6).Replace('+', ' '));
            }
        }
        return "";
    }

    /// <summary>假 HTTP:Modrinth 搜索回一条以查询词命名的结果;版本清单回两条;其余 404。带一点延迟,让并发真的叠在一起。</summary>
    private static HttpResponseMessage? Responder(HttpRequestMessage req, ConcurrentBag<string> seen)
    {
        string url = req.RequestUri?.ToString() ?? "";
        seen.Add(url);
        Thread.Sleep(15);
        if (url.Contains("/search", StringComparison.Ordinal) && !url.Contains("curseforge", StringComparison.OrdinalIgnoreCase) && !url.Contains("/mods/search", StringComparison.Ordinal))
        {
            string q = Query(req);
            return Json("{\"hits\":[{\"slug\":\"fake-" + q + "\",\"title\":\"Fake " + q + "\",\"author\":\"t\",\"downloads\":7,\"description\":\"d\",\"categories\":[]}],\"total_hits\":1}");
        }
        if (url.Contains("version_manifest", StringComparison.Ordinal))
        {
            return Json("{\"latest\":{\"release\":\"1.21.1\"},\"versions\":[{\"id\":\"1.21.1\",\"type\":\"release\"},{\"id\":\"1.20.1\",\"type\":\"release\"}]}");
        }
        if (url.Contains("/mods/search", StringComparison.Ordinal))
        {
            return Json("{\"data\":[]}");
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
    }

    [Fact]
    public async Task CatalogSearches_RunConcurrently_WithoutCorruptingSharedState()
    {
        var seen = new ConcurrentBag<string>();
        using (TestNetworkIsolation.Fake(r => Responder(r, seen)))
        {
            CatalogFiles.ResetAliasCache(); // 让并发的第一批调用一起去建别名表
            string[] kinds = { "mod", "shader", "resourcepack", "modpack" };
            Task<(string Kind, string Q, List<object> Rows)>[] tasks = Enumerable.Range(0, 24).Select(i => Task.Run(() =>
            {
                string kind = kinds[i % kinds.Length];
                string q = "q" + i;
                return (kind, q, CatalogFiles.SearchProjects(new DownloadManager(2L), kind, q, "全部", new PyDict()));
            })).ToArray();
            var results = await Task.WhenAll(tasks);
            foreach (var (kind, q, rows) in results)
            {
                Assert.Contains(rows.Select(Py.D), r => Py.S(r.Get("name")) == "Fake " + q);
            }
        }
        Assert.NotEmpty(seen);
    }

    [Fact]
    public async Task VersionManifest_ConcurrentCallers_FetchOnlyOnce()
    {
        var seen = new ConcurrentBag<string>();
        string cache = Path.Combine(Utils.ROOT, "cache");
        Func<bool>? savedProbe = Source.ProbeHook;
        try
        {
            using (TestNetworkIsolation.Fake(r => Responder(r, seen)))
            {
                foreach (string f in new[] { "version_manifest.json", "version_manifest.meta" })
                {
                    try { File.Delete(Path.Combine(cache, f)); } catch (Exception) { }
                }
                int before = Manifest.ManifestFetchCount;
                var gate = new ManualResetEventSlim(false);
                Task<PyDict>[] tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
                {
                    gate.Wait();
                    return Manifest.ListRemoteVersions(new DownloadManager(), force: false);
                })).ToArray();
                gate.Set();
                PyDict[] all = await Task.WhenAll(tasks);
                Assert.All(all, v => Assert.True(v.ContainsKey("1.21.1")));
                Assert.Equal(1, Manifest.ManifestFetchCount - before); // 单飞:8 个并发调用只拉一次
            }
        }
        finally
        {
            Source.ProbeHook = savedProbe;
        }
    }

    /// <summary>真实场景:同一轮里多个只读联网工具由 Scheduler 并发执行,底下走真实 InProcessApi + Core(HTTP 是假的)。</summary>
    [Fact]
    public async Task ReadOnlyToolGroup_RunsInParallel_ThroughRealApi()
    {
        var seen = new ConcurrentBag<string>();
        using (TestNetworkIsolation.Fake(r => Responder(r, seen)))
        {
            var runner = new ToolRunner(new ApiBackend()) { PermissionMode = "default", Instance = "default" };
            var calls = new List<ToolCall>();
            for (int i = 0; i < 12; i++)
            {
                calls.Add((i % 3) switch
                {
                    0 => new ToolCall("s" + i, "search_mods", PyDict.Of("query", "m" + i)),
                    1 => new ToolCall("s" + i, "search_content", PyDict.Of("query", "c" + i, "kind", i % 2 == 0 ? "shader" : "resourcepack")),
                    _ => new ToolCall("s" + i, "search_modpacks", PyDict.Of("query", "p" + i)),
                });
            }
            Assert.All(calls, c => Assert.True(ToolMeta.Get(c.name)!.@readonly));
            string[] results = await Scheduler.RunGroupAsync(calls, c => runner.RunAsync(c), 4, CancellationToken.None);
            for (int i = 0; i < calls.Count; i++)
            {
                Assert.Equal(ToolCallStatus.COMPLETED, calls[i].status);
                string q = Py.S(calls[i].args.Get("query"));
                Assert.Contains("Fake " + q, results[i]);
            }
        }
    }

    private sealed class ApiBackend : ToolRunner.IBackend
    {
        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
            => TipCL.App.Common.InProcessApi.Instance.CallAsync<T>(method, args, ct);
    }
}
