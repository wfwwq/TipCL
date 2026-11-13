using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C2d:进程内后端的**真实启动 / 收摊 / 状态机 / 隔离**用例。
///
/// 这些用例跑的是真东西:真 <c>BackendApi</c>(只关掉 1.5 秒后的登录预热,D-100)、
/// 真 <c>BridgeServer</c>(<c>TcpListener</c> 绑 <c>127.0.0.1:0</c>)、真回环 socket、
/// 真 <c>RpcDispatcher</c>。**唯一被换掉的是"内核起来之后那一步"**(<c>OnCoreStarted</c>):
/// 生产默认那一步会启动插件系统,而它 5 秒后会去插件市场拉更新(联网),测试不能碰(D-101)。
///
/// 用例体一律 <c>async Task</c> 并 <c>await</c>(不用 <c>.GetAwaiter().GetResult()</c>:
/// xUnit1031 会因此报警告,而本项目要求"NU1900 之外的警告只有 SYSLIB0014 一处")。
/// 每个用例都在 <c>finally</c> 里 <see cref="C2dFixtures.Restore"/> 并 Dispose 启动器
/// (任务书:每个 start 必须有配对的 stop,端口用 0 让系统挑)。
///
/// 期望值来源逐条标注:"任务书" / "Win 源"(Windows 版对应实现)/ "实测"(本机跑出来的观测值)。
/// </summary>
[Collection(C2dFixtures.Collection)]
public class C2dBackendLifecycleTests
{
    // ───────────────────────── 状态机 ─────────────────────────

    /// <summary>没启动过:阶段是 NotStarted,端口 0,宿主 null(实测)。</summary>
    [Fact]
    public void Before_start_the_bootstrap_is_not_started()
    {
        C2dFixtures.Install();
        try
        {
            using BackendBootstrap boot = new BackendBootstrap();

            Assert.Equal(BridgePhase.NotStarted, boot.Phase);
            Assert.False(boot.IsReady);
            Assert.Null(boot.Host);
            Assert.Equal(0, boot.Port);
            Assert.Equal("", boot.Root);
            Assert.Null(boot.Error);
        }
        finally
        {
            C2dFixtures.Restore();
        }
    }

    /// <summary>
    /// 状态迁移在这一个用例里整条看完:Starting(启动途中可观察)→ Ready → Stopped。
    /// 做法是让内核工厂先卡在一个门闩上,趁它卡着读阶段(实测)。
    /// </summary>
    [Fact]
    public async Task Start_goes_through_starting_and_ends_ready_then_stopped()
    {
        C2dFixtures.Install();
        using ManualResetEventSlim gate = new ManualResetEventSlim(false);
        using ManualResetEventSlim inFactory = new ManualResetEventSlim(false);
        BridgeHost.ApiFactory = delegate(string root, EventBus bus)
        {
            inFactory.Set();
            gate.Wait(TimeSpan.FromSeconds(20));
            BackendApi.DisableAuthWarmup = true;
            return new BackendApi(bus);
        };

        try
        {
            using BackendBootstrap boot = new BackendBootstrap();
            Task<BridgeHost?> start = boot.StartAsync();

            Assert.True(inFactory.Wait(TimeSpan.FromSeconds(20)), "注入的工厂没有被调用");
            Assert.Equal(BridgePhase.Starting, boot.Phase);
            Assert.False(boot.IsReady);
            Assert.Null(boot.Host);
            Assert.Equal(0, boot.Port);

            gate.Set();
            BridgeHost? host = await start;

            Assert.NotNull(host);
            Assert.Equal(BridgePhase.Ready, boot.Phase);
            Assert.True(boot.IsReady);
            Assert.Same(host, boot.Host);
            Assert.Equal(host!.Port, boot.Port);

            boot.Stop();

            Assert.Equal(BridgePhase.Stopped, boot.Phase);
            Assert.False(boot.IsReady);
            Assert.Null(boot.Host);
            Assert.Equal(0, boot.Port);
        }
        finally
        {
            gate.Set();
            C2dFixtures.Restore();
        }
    }

    /// <summary>启动成功:阶段 Ready、宿主就绪、端口 &gt; 0、数据根 = 隔离目录(实测)。</summary>
    [Fact]
    public async Task Start_reports_ready_with_a_real_port_and_the_isolated_root()
    {
        C2dFixtures.Install();
        try
        {
            using BackendBootstrap boot = new BackendBootstrap();
            BridgeHost? host = await boot.StartAsync();

            Assert.NotNull(host);
            Assert.Equal(BridgePhase.Ready, boot.Phase);
            Assert.Null(boot.Error);
            Assert.True(boot.Port > 0, "端口必须是真端口(0 让系统挑),实际 " + boot.Port);
            Assert.Equal(TestHome.Root, boot.Root);
            Assert.Equal(TestHome.Root, host!.Root);
            Assert.Equal(BridgeHost.ClientBaseUrl(boot.Port), host.Client.BaseUri.ToString());
            Assert.Equal(I18n.L("C 桥"), host.Backend);

            boot.Stop();
        }
        finally
        {
            C2dFixtures.Restore();
        }
    }

    /// <summary>已经在用的时候再启动一次:抛 InvalidOperationException(调用错误,不是静默复用)。</summary>
    [Fact]
    public async Task Second_start_while_ready_is_rejected()
    {
        C2dFixtures.Install();
        try
        {
            using BackendBootstrap boot = new BackendBootstrap();
            BridgeHost? host = await boot.StartAsync();
            Assert.NotNull(host);

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => boot.StartAsync());

            Assert.Contains(I18n.L("后端已经启动了"), ex.Message, StringComparison.Ordinal);
            Assert.Equal(BridgePhase.Ready, boot.Phase);
            Assert.Same(host, boot.Host);

            boot.Stop();
        }
        finally
        {
            C2dFixtures.Restore();
        }
    }

    /// <summary>
    /// 失败路径:注入的内核工厂抛异常 → 启动返回 null、阶段 Failed、原因是**原异常**、
    /// 宿主为 null、端口 0(任务书:失败不许静默变成 ready)。
    /// </summary>
    [Fact]
    public async Task Factory_that_throws_leaves_the_bootstrap_failed()
    {
        C2dFixtures.Install();
        BridgeHost.ApiFactory = delegate(string root, EventBus bus)
        {
            throw new InvalidOperationException("C2d 注入的工厂故意失败");
        };

        try
        {
            using BackendBootstrap boot = new BackendBootstrap();
            BridgeHost? host = await boot.StartAsync();

            Assert.Null(host);
            Assert.Equal(BridgePhase.Failed, boot.Phase);
            Assert.False(boot.IsReady);
            Assert.Null(boot.Host);
            Assert.Equal(0, boot.Port);
            Assert.NotNull(boot.Error);
            Assert.IsType<InvalidOperationException>(boot.Error);
            Assert.Contains("C2d", boot.Error!.Message, StringComparison.Ordinal);
        }
        finally
        {
            C2dFixtures.Restore();
        }
    }

    /// <summary>
    /// 失败之后不许留下任何监听:失败的那一刻没有任何新监听端口出现,
    /// 而且紧接着的第二次启动照样能起来(实测)。
    /// </summary>
    [Fact]
    public async Task Failed_start_leaves_no_listener_and_a_later_start_works()
    {
        C2dFixtures.Install();
        BridgeHost.ApiFactory = delegate(string root, EventBus bus)
        {
            throw new InvalidOperationException("C2d 注入的工厂故意失败");
        };

        try
        {
            List<int> before = C2dFixtures.LoopbackListeners();
            using (BackendBootstrap broken = new BackendBootstrap())
            {
                Assert.Null(await broken.StartAsync());
                Assert.Equal(BridgePhase.Failed, broken.Phase);
            }

            Assert.True(
                C2dFixtures.WaitUntilNoNewLoopbackListener(before, TimeSpan.FromSeconds(5)),
                "失败路径留下了监听端口: " + string.Join(",", C2dFixtures.LoopbackListeners().Except(before)));

            // 换回正常工厂,证明失败没有把进程级状态弄脏(内核单例还有效)。
            C2dFixtures.InstallCoreFactoryOnly();
            using BackendBootstrap boot = new BackendBootstrap();
            BridgeHost? host = await boot.StartAsync();

            Assert.NotNull(host);
            Assert.Equal(BridgePhase.Ready, boot.Phase);
            Assert.Contains(host!.Port, C2dFixtures.LoopbackListeners());

            boot.Stop();
            Assert.True(C2dFixtures.CanBind(host.Port));
        }
        finally
        {
            C2dFixtures.Restore();
        }
    }

    /// <summary>
    /// 启动途中被 Stop:启动任务返回 null(不假成功)、阶段停在 Stopped、宿主为 null,
    /// 而且**没有留下新监听**(任务书:取消在飞的启动 + 不留监听)。
    /// </summary>
    [Fact]
    public async Task Stop_during_start_cancels_and_leaves_no_listener()
    {
        C2dFixtures.Install();
        using ManualResetEventSlim gate = new ManualResetEventSlim(false);
        using ManualResetEventSlim inFactory = new ManualResetEventSlim(false);
        BridgeHost.ApiFactory = delegate(string root, EventBus bus)
        {
            inFactory.Set();
            gate.Wait(TimeSpan.FromSeconds(20));
            BackendApi.DisableAuthWarmup = true;
            return new BackendApi(bus);
        };

        try
        {
            List<int> before = C2dFixtures.LoopbackListeners();
            using BackendBootstrap boot = new BackendBootstrap();
            Task<BridgeHost?> start = boot.StartAsync();

            Assert.True(inFactory.Wait(TimeSpan.FromSeconds(20)), "注入的工厂没有被调用");
            Assert.Equal(BridgePhase.Starting, boot.Phase);

            boot.Stop();
            Assert.Equal(BridgePhase.Stopped, boot.Phase);

            gate.Set();
            BridgeHost? host = await start;

            Assert.Null(host);
            Assert.Null(boot.Host);
            Assert.False(boot.IsReady);
            Assert.True(
                C2dFixtures.WaitUntilNoNewLoopbackListener(before, TimeSpan.FromSeconds(10)),
                "取消启动后留下了监听端口: " + string.Join(",", C2dFixtures.LoopbackListeners().Except(before)));
        }
        finally
        {
            gate.Set();
            C2dFixtures.Restore();
        }
    }

    // ───────────────────────── 监听与收摊 ─────────────────────────

    /// <summary>端口真的绑上了:回环上能连、而且出现在操作系统的监听表里(实测)。</summary>
    [Fact]
    public async Task Started_port_is_bound_and_reachable()
    {
        C2dFixtures.Install();
        try
        {
            using BackendBootstrap boot = new BackendBootstrap();
            BridgeHost? host = await boot.StartAsync();
            Assert.NotNull(host);

            Assert.True(C2dFixtures.CanConnect(host!.Port), "回环端口连不上: " + host.Port);
            Assert.Contains(host.Port, C2dFixtures.LoopbackListeners());

            boot.Stop();
        }
        finally
        {
            C2dFixtures.Restore();
        }
    }

    /// <summary>/health 真的答 200(这也是 <c>BridgeHost</c> 起来之后的开闸条件)。</summary>
    [Fact]
    public async Task Health_endpoint_answers_200()
    {
        C2dFixtures.Install();
        try
        {
            using BackendBootstrap boot = new BackendBootstrap();
            BridgeHost? host = await boot.StartAsync();
            Assert.NotNull(host);

            Assert.True(await host!.Client.IsHealthyAsync());

            boot.Stop();
        }
        finally
        {
            C2dFixtures.Restore();
        }
    }

    /// <summary>Stop 幂等:连调两次不抛,阶段停在 Stopped(实测)。</summary>
    [Fact]
    public async Task Stop_is_idempotent()
    {
        C2dFixtures.Install();
        try
        {
            using BackendBootstrap boot = new BackendBootstrap();
            BridgeHost? host = await boot.StartAsync();
            Assert.NotNull(host);
            int port = host!.Port;

            boot.Stop();
            boot.Stop();
            boot.Stop();

            Assert.Equal(BridgePhase.Stopped, boot.Phase);
            Assert.Null(boot.Host);
            Assert.True(C2dFixtures.CanBind(port), "端口没放掉: " + port);
        }
        finally
        {
            C2dFixtures.Restore();
        }
    }

    /// <summary>
    /// 收摊取证(逐个端口,不做计数比较):旧端口从系统监听表里消失、能被重新绑上、
    /// 而且再连它会被拒(实测)。这三条一起才算"监听器真的关了"。
    /// </summary>
    [Fact]
    public async Task Stop_releases_the_port_and_refuses_new_connections()
    {
        C2dFixtures.Install();
        try
        {
            using BackendBootstrap boot = new BackendBootstrap();
            BridgeHost? host = await boot.StartAsync();
            Assert.NotNull(host);
            int port = host!.Port;

            boot.Stop();

            Assert.DoesNotContain(port, C2dFixtures.LoopbackListeners());
            Assert.True(C2dFixtures.CanBind(port), "端口不能被重新绑: " + port);
            Assert.False(C2dFixtures.CanConnect(port), "停掉之后还能连上: " + port);
        }
        finally
        {
            C2dFixtures.Restore();
        }
    }

    /// <summary>停掉之后还能再起来(阶段从 Stopped 回到 Ready;实测)。</summary>
    [Fact]
    public async Task Restart_after_stop_works()
    {
        C2dFixtures.Install();
        try
        {
            using BackendBootstrap boot = new BackendBootstrap();
            BridgeHost? first = await boot.StartAsync();
            Assert.NotNull(first);
            int firstPort = first!.Port;
            boot.Stop();

            BridgeHost? second = await boot.StartAsync();

            Assert.NotNull(second);
            Assert.Equal(BridgePhase.Ready, boot.Phase);
            Assert.True(second!.Port > 0);
            Assert.NotSame(first, second);
            Assert.True(C2dFixtures.CanConnect(second.Port));

            boot.Stop();
            Assert.True(C2dFixtures.CanBind(firstPort));
            Assert.True(C2dFixtures.CanBind(second.Port));
        }
        finally
        {
            C2dFixtures.Restore();
        }
    }

    /// <summary>Dispose = 收后端 + 之后再用就抛 ObjectDisposedException(实测)。</summary>
    [Fact]
    public async Task Dispose_stops_the_backend_and_closes_the_bootstrap()
    {
        C2dFixtures.Install();
        try
        {
            BackendBootstrap boot = new BackendBootstrap();
            BridgeHost? host = await boot.StartAsync();
            Assert.NotNull(host);
            int port = host!.Port;

            boot.Dispose();
            boot.Dispose();

            Assert.Equal(BridgePhase.Stopped, boot.Phase);
            Assert.Null(boot.Host);
            Assert.True(C2dFixtures.CanBind(port));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => boot.StartAsync());
        }
        finally
        {
            C2dFixtures.Restore();
        }
    }

    /// <summary>
    /// 收摊时"取消在飞的任务":事件流(SSE)循环连着的时候 Stop 也必须立刻返回并把端口放掉
    /// (Win 源:`BridgeHost.Dispose` 先 <c>Client.Dispose()</c>(取消 SSE)再 <c>server.Stop()</c>)。
    /// </summary>
    [Fact]
    public async Task Stop_while_the_event_stream_is_open_is_prompt()
    {
        C2dFixtures.Install();
        try
        {
            using BackendBootstrap boot = new BackendBootstrap();
            BridgeHost? host = await boot.StartAsync();
            Assert.NotNull(host);
            int port = host!.Port;

            host.Client.ConnectEvents();
            Assert.True(WaitForStream(host, TimeSpan.FromSeconds(5)), "事件流没连上");

            Stopwatch watch = Stopwatch.StartNew();
            boot.Stop();
            watch.Stop();

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "Stop 花了 " + watch.Elapsed + "(疑似被 SSE 拖住)");
            // 事件流那条读循环是在别的线程上收摊的(SetStreamState(false) 在它的 finally 里),
            // 所以这里给一个有上限的等待 —— 并行通道下"Stop 刚返回就读状态"会看到中间态(实测)。
            Assert.True(
                WaitForStreamClosed(host, TimeSpan.FromSeconds(10)),
                "停止之后事件流状态没有落回未连接;LastStreamError=" + host.Client.LastStreamError);
            Assert.True(C2dFixtures.CanBind(port));
        }
        finally
        {
            C2dFixtures.Restore();
        }
    }

    /// <summary>
    /// 反复起停 10 轮:每个用过的端口最后都要放掉、能被重新绑、系统监听表里不再出现新端口,
    /// 而且进程线程数不许一轮一堆地涨(每轮都有一个 bridge-accept 线程,泄漏的话会 +10;
    /// 这里给 6 的余量,把线程池的正常波动排除在外)。实测值写在断言消息里。
    /// </summary>
    [Fact]
    public async Task Ten_start_stop_cycles_leave_no_listener_or_thread_pileup()
    {
        C2dFixtures.Install();
        try
        {
            List<int> before = C2dFixtures.LoopbackListeners();
            int threadsBefore = C2dFixtures.SettledThreadCount(TimeSpan.FromSeconds(1));
            List<int> used = new List<int>();

            for (int i = 0; i < 10; i++)
            {
                using BackendBootstrap boot = new BackendBootstrap();
                BridgeHost? host = await boot.StartAsync();
                Assert.NotNull(host);
                Assert.Equal(BridgePhase.Ready, boot.Phase);
                used.Add(host!.Port);
                boot.Stop();
                Assert.Equal(BridgePhase.Stopped, boot.Phase);
            }

            foreach (int port in used)
            {
                Assert.DoesNotContain(port, C2dFixtures.LoopbackListeners());
                Assert.True(C2dFixtures.CanBind(port), "第 " + used.IndexOf(port) + " 轮的端口没放掉: " + port);
            }

            Assert.True(
                C2dFixtures.WaitUntilNoNewLoopbackListener(before, TimeSpan.FromSeconds(5)),
                "10 轮之后还有新监听端口: " + string.Join(",", C2dFixtures.LoopbackListeners().Except(before)));

            int threadsAfter = C2dFixtures.SettledThreadCount(TimeSpan.FromSeconds(1));
            Assert.True(
                threadsAfter <= threadsBefore + 6,
                "线程数涨了 " + (threadsAfter - threadsBefore) + " 条(前 " + threadsBefore + " / 后 " + threadsAfter + "),疑似有线程没退出");
        }
        finally
        {
            C2dFixtures.Restore();
        }
    }

    /// <summary>
    /// 停掉之后 RPC 必须**失败**,不许假装成功(诚实性):拿一个新建的回环客户端打已停的端口,
    /// 必然抛(连接被拒)。期望类型不写死(平台差异),只要求它抛。
    /// </summary>
    [Fact]
    public async Task Rpc_after_stop_fails_instead_of_pretending()
    {
        C2dFixtures.Install();
        try
        {
            using BackendBootstrap boot = new BackendBootstrap();
            BridgeHost? host = await boot.StartAsync();
            Assert.NotNull(host);
            int port = host!.Port;
            boot.Stop();

            using BridgeClient client = new BridgeClient(new Uri(BridgeHost.ClientBaseUrl(port)), new string('a', 64));
            await Assert.ThrowsAnyAsync<Exception>(() => client.CallAsync("get_language"));
            Assert.False(C2dFixtures.CanConnect(port));
        }
        finally
        {
            C2dFixtures.Restore();
        }
    }

    // ───────────────────────── 数据落点 ─────────────────────────

    /// <summary>
    /// 数据全部落在隔离目录里:让后端**真的写一个文件**(<c>stash_upload</c> 把 base64 解出来写到
    /// <c>Utils.ROOT/uploads</c> 下,名字带 Guid),然后断言落点在 <see cref="TestHome.Root"/> 之下、
    /// 内容正确、并且启动前后新增的条目全都在隔离目录里;用户真实的
    /// <c>%LOCALAPPDATA%\TipCL</c> 与本次启动无关。
    /// </summary>
    [Fact]
    public async Task Backend_writes_only_under_the_isolated_test_home()
    {
        C2dFixtures.Install();
        try
        {
            string name = "c2d-containment-" + Guid.NewGuid().ToString("N") + ".bin";
            HashSet<string> before = C2dFixtures.EntriesUnder(TestHome.Root);

            using BackendBootstrap boot = new BackendBootstrap();
            BridgeHost? host = await boot.StartAsync();
            Assert.NotNull(host);

            string data = Convert.ToBase64String(Encoding.UTF8.GetBytes("C2d"));
            JsonElement result = await host!.Client.CallAsync("stash_upload", new Dictionary<string, object>
            {
                ["name"] = name,
                ["data"] = data
            });
            string written = result.GetString() ?? "";

            List<string> created = C2dFixtures.EntriesUnder(TestHome.Root).Except(before).ToList();
            boot.Stop();

            Assert.StartsWith(TestHome.Root, written, StringComparison.Ordinal);
            Assert.EndsWith(name, written, StringComparison.Ordinal);
            Assert.True(File.Exists(written), "后端写出来的文件不存在: " + written);
            Assert.Equal("C2d", File.ReadAllText(written));
            Assert.Contains(created, path => string.Equals(path, written, StringComparison.OrdinalIgnoreCase));
            Assert.All(created, path => Assert.StartsWith(TestHome.Root, path, StringComparison.Ordinal));
            Assert.StartsWith(Path.Combine(TestHome.Root, "uploads"), written, StringComparison.Ordinal);

            string realUserDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TipCL");
            Assert.StartsWith(TestHome.Root, host.Root, StringComparison.Ordinal);
            Assert.StartsWith(TestHome.Root, Utils.ROOT, StringComparison.Ordinal);
            Assert.False(host.Root.StartsWith(realUserDir, StringComparison.Ordinal));
            Assert.False(Utils.ROOT.StartsWith(realUserDir, StringComparison.Ordinal));
        }
        finally
        {
            C2dFixtures.Restore();
        }
    }

    /// <summary>启动不会动"当前目录"这类进程级设置:隔离根与 <c>BridgeHost.FindRoot()</c> 始终一致(实测)。</summary>
    [Fact]
    public async Task Start_keeps_find_root_and_utils_root_in_agreement()
    {
        C2dFixtures.Install();
        try
        {
            Assert.Equal(TestHome.Root, BridgeHost.FindRoot());
            Assert.Equal(TestHome.Root, Utils.ROOT);

            using BackendBootstrap boot = new BackendBootstrap();
            Assert.NotNull(await boot.StartAsync());

            Assert.Equal(TestHome.Root, BridgeHost.FindRoot());
            Assert.Equal(TestHome.Root, Utils.ROOT);
            Assert.Equal(TestHome.Root, boot.Root);

            boot.Stop();
            Assert.Equal(TestHome.Root, Utils.ROOT);
        }
        finally
        {
            C2dFixtures.Restore();
        }
    }

    private static bool WaitForStream(BridgeHost host, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (host.Client.StreamConnected)
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return host.Client.StreamConnected;
    }

    private static bool WaitForStreamClosed(BridgeHost host, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!host.Client.StreamConnected)
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return !host.Client.StreamConnected;
    }
}
