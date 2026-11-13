using System;
using System.Diagnostics;
using System.Threading;
using TipCL;
using TipCL.Core;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C2d:外壳接线 —— 后端起真的起来了才把 <see cref="MainWindow.BridgeReady"/> 总闸打开,
/// 失败就保持关着并把原因留下(D-96 的缺口,本包补上)。
///
/// 这些用例体都碰 Avalonia(要造 <c>MainWindow</c> / 页面),所以一律走
/// <see cref="AvaloniaSession.Run(Action)"/>(C1b-fix 的纪律);用例体必须**同步**
/// —— 后端那一段用 <see cref="MainWindow.ConnectBlocking"/> 同步等待(它内部不碰 Avalonia,
/// 只写普通字段,所以从会话线程上阻塞等是安全的)。
///
/// 期望值来源:"Win 源" = Windows 版 <c>MainWindow.ConnectAsync</c>(`_bridgeReady = true` 在
/// `BridgeHost.StartAsync` 成功之后、`catch` 里把状态打回"未连接"且不改 `_bridgeReady`);
/// "任务书" = C2d 任务书的第 2 条;"实测" = 本机跑出来的观测值。
/// </summary>
[Collection(C2dFixtures.Collection)]
public class C2dShellWiringTests
{
    /// <summary>
    /// 连接成功:开闸 + 服务定位器接上 + 返回 true;收摊之后闸门关回去、端口放掉。
    /// </summary>
    [Fact]
    public void Connect_opens_the_gate_and_registers_the_services()
    {
        AvaloniaSession.Run(() =>
        {
            C2dFixtures.Install();
            try
            {
                using ShellFixture shell = ShellFixture.Create();
                MainWindow window = shell.Window;

                Assert.False(window.BridgeReady);
                Assert.Equal("", window.BridgeError);

                using BackendBootstrap boot = new BackendBootstrap();
                Stopwatch watch = Stopwatch.StartNew();
                bool ok = window.ConnectBlocking(boot);
                watch.Stop();

                Assert.True(ok, "连接失败: " + window.BridgeError);
                Assert.True(window.BridgeReady);
                Assert.Equal("", window.BridgeError);
                Assert.Equal(BridgePhase.Ready, boot.Phase);
                Assert.True(boot.Port > 0);
                Assert.NotNull(AppServices.Host);
                Assert.Same(boot.Host, AppServices.Host);
                Assert.Same(boot.Host!.Client, AppServices.Client);
                Assert.True(AppServices.Ready);
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(25), "同步连接花了 " + watch.Elapsed);

                int port = boot.Port;
                window.StopBackend();

                Assert.False(window.BridgeReady);
                Assert.True(C2dFixtures.CanBind(port), "收后端之后端口没放掉: " + port);
            }
            finally
            {
                C2dFixtures.Restore();
            }
        });
    }

    /// <summary>
    /// 失败路径:后端起不来时闸门保持关闭,原因写进 <see cref="MainWindow.BridgeError"/>
    /// (任务书:不许静默变成 ready),而且过后还能再连一次成功并**清掉**上一次的原因(实测)。
    /// </summary>
    [Fact]
    public void Failed_connect_keeps_the_gate_closed_and_records_the_reason()
    {
        AvaloniaSession.Run(() =>
        {
            C2dFixtures.Install();
            BridgeHost.ApiFactory = delegate(string root, EventBus bus)
            {
                throw new InvalidOperationException("C2d 连不上");
            };

            try
            {
                using ShellFixture shell = ShellFixture.Create();
                MainWindow window = shell.Window;

                using (BackendBootstrap broken = new BackendBootstrap())
                {
                    Assert.False(window.ConnectBlocking(broken));
                    Assert.False(window.BridgeReady);
                    Assert.NotEqual("", window.BridgeError);
                    Assert.Contains("C2d", window.BridgeError, StringComparison.Ordinal);
                    Assert.Equal(BridgePhase.Failed, broken.Phase);
                }

                // 换回正常工厂:同一扇窗口再连一次必须能成功,并且把旧原因清掉。
                C2dFixtures.InstallCoreFactoryOnly();
                using BackendBootstrap boot = new BackendBootstrap();
                Assert.True(window.ConnectBlocking(boot));
                Assert.True(window.BridgeReady);
                Assert.Equal("", window.BridgeError);

                window.StopBackend();
                Assert.False(window.BridgeReady);
            }
            finally
            {
                C2dFixtures.Restore();
            }
        });
    }

    /// <summary>
    /// 总闸真的在管页面加载(两阶段用例):
    /// 阶段 1 —— 闸门关着时切页**不跑** <c>LoadAsync</c>/<c>OnShown</c>;连上之后闸门打开;
    /// 阶段 2 —— 连上时投到 Dispatcher 的"补载当前页"由会话泵跑掉(那一页因此被加载 + <c>OnShown</c>),
    /// 之后门开着切页也立刻加载。
    /// 第二阶段的断言必须另起一次 <c>AvaloniaSession.Run</c>:用例体占着会话线程时泵不会跑
    /// (Dispatcher 上投的工作要等当前用例体退出,见 AvaloniaSession 的设计)。
    /// </summary>
    [Fact]
    public void Page_loading_follows_the_bridge_ready_gate()
    {
        ShellFixture? shell = null;
        RegistryScope? registry = null;
        BackendBootstrap? boot = null;
        ProbePage? before = null;
        ProbePage? after = null;

        try
        {
            AvaloniaSession.Run(() =>
            {
                C2dFixtures.Install();
                registry = new RegistryScope()
                    .Register("c2d-probe-a", () => new ProbePage())
                    .Register("c2d-probe-b", () => new ProbePage());
                shell = ShellFixture.Create();
                MainWindow window = shell.Window;
                Assert.False(window.BridgeReady);

                window.Navigate("c2d-probe-a");
                before = window.CurrentPage as ProbePage;
                Assert.NotNull(before);
                Assert.True(before!.Loads == 0, "闸门关着时不许加载页面;probe-a Log=" + string.Join(",", before.Log));
                Assert.True(before.Shown == 0, "闸门关着时不许跑 OnShown;probe-a Log=" + string.Join(",", before.Log));

                boot = new BackendBootstrap();
                Assert.True(window.ConnectBlocking(boot));
                Assert.True(window.BridgeReady);
            });

            // 给会话泵一点时间去跑 Dispatcher 上那个"补载当前页"的工作(它每 2ms 跑一次 RunJobs)。
            Thread.Sleep(300);

            AvaloniaSession.Run(() =>
            {
                MainWindow window = shell!.Window;
                Assert.Equal(BridgePhase.Ready, boot!.Phase);

                Assert.NotNull(before);
                Assert.True(before!.Loads == 1, "连上之后当前页必须被补载;probe-a Log=" + string.Join(",", before.Log));
                Assert.True(before.Shown == 1, "补载之后必须跑 OnShown;probe-a Log=" + string.Join(",", before.Log));

                window.Navigate("c2d-probe-b");
                after = window.CurrentPage as ProbePage;
                Assert.NotNull(after);
                Assert.True(after!.Loads == 1, "闸门开了之后切页必须立刻加载;probe-b Log=" + string.Join(",", after.Log));
                Assert.True(after.Shown == 1, "闸门开了之后切页必须跑 OnShown;probe-b Log=" + string.Join(",", after.Log));
            });
        }
        finally
        {
            AvaloniaSession.Run(() =>
            {
                try
                {
                    shell?.Window.StopBackend();
                }
                catch (Exception)
                {
                }

                boot?.Dispose();
                shell?.Dispose();
                registry?.Dispose();
                C2dFixtures.Restore();
            });
        }
    }

    /// <summary>
    /// 生产入口 <see cref="MainWindow.StartBackend"/> 是 fire-and-forget(App 的桌面生命周期里调用它),
    /// 所以这里轮询等它自己开闸(实测:门是在后台开的,没有阻塞调用方)。
    /// </summary>
    [Fact]
    public void Start_backend_opens_the_gate_in_the_background()
    {
        AvaloniaSession.Run(() =>
        {
            C2dFixtures.Install();
            try
            {
                using ShellFixture shell = ShellFixture.Create();
                MainWindow window = shell.Window;

                Stopwatch watch = Stopwatch.StartNew();
                window.StartBackend();

                // 生产是"投出去就不管",但同步调用不该被后端启动拖住。
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), "StartBackend 阻塞了 " + watch.Elapsed);

                bool ready = WaitFor(() => window.BridgeReady, TimeSpan.FromSeconds(20));
                Assert.True(ready, "StartBackend 之后 BridgeReady 一直是 false: " + window.BridgeError);
                Assert.Equal("", window.BridgeError);
                Assert.NotNull(AppServices.Host);
                Assert.True(AppServices.Host!.Port > 0);
                int port = AppServices.Host.Port;

                window.StopBackend();

                Assert.False(window.BridgeReady);
                Assert.True(C2dFixtures.CanBind(port));
            }
            finally
            {
                C2dFixtures.Restore();
            }
        });
    }

    /// <summary>收后端可以重复调,闸门保持关闭(实测;幂等)。</summary>
    [Fact]
    public void Stop_backend_is_idempotent()
    {
        AvaloniaSession.Run(() =>
        {
            C2dFixtures.Install();
            try
            {
                using ShellFixture shell = ShellFixture.Create();
                MainWindow window = shell.Window;

                using BackendBootstrap boot = new BackendBootstrap();
                Assert.True(window.ConnectBlocking(boot));
                int port = boot.Port;

                window.StopBackend();
                window.StopBackend();
                window.StopBackend();

                Assert.False(window.BridgeReady);
                Assert.True(C2dFixtures.CanBind(port));
            }
            finally
            {
                C2dFixtures.Restore();
            }
        });
    }

    private static bool WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        do
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(50);
        }
        while (DateTime.UtcNow < deadline);

        return condition();
    }
}
