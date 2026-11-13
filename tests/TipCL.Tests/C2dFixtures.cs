using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using TipCL.Core;
using TipCL.Services;

namespace TipCL.Tests;

/// <summary>
/// C2d:后端启动用例的公共装置(注入缝 + 观察手段)。
///
/// **注入缝**(<c>BridgeHost</c> 上两个 internal 属性,生产代码永远不写它们):
/// <list type="number">
/// <item><c>BridgeHost.ApiFactory</c> —— 默认是生产行为的 <c>new BackendApi(bus)</c>;
/// 本包换成「先把 <c>BackendApi.DisableAuthWarmup</c> 置 true 再构造」的工厂,
/// 于是整个启动路径是真的(真 BackendApi / 真 BridgeServer / 真回环 socket),
/// 唯独不发那次 1.5 秒后的登录预热请求(D-100);</item>
/// <item><c>BridgeHost.OnCoreStarted</c> —— 默认那一步会调
/// <c>McpServer.StartIfEnabled</c> 与 <c>PluginManager.StartGlobal</c>,而后者启动 5 秒后
/// 会去插件市场拉更新(**联网**)。测试必须换成空实现(D-101)。</item>
/// </list>
/// 两个属性都是**进程级静态**,所以装载/还原必须成对,装在用例体的 <c>try</c> 里、
/// 还原在 <c>finally</c> 里(<see cref="Install"/> / <see cref="Restore"/> 各自保存与还原快照)。
/// </summary>
internal static class C2dFixtures
{
    private static bool _installed;

    private static bool _savedWarmupSwitch;

    private static Func<string, EventBus, BackendApi>? _savedApiFactory;

    private static Action<BackendApi>? _savedOnCoreStarted;

    /// <summary>装上"关预热 + 不跑 MCP/插件收尾"的测试注入缝(幂等;必须配 <see cref="Restore"/>)。</summary>
    internal static void Install()
    {
        if (_installed)
        {
            return;
        }

        _savedWarmupSwitch = BackendApi.DisableAuthWarmup;
        _savedApiFactory = BridgeHost.ApiFactory;
        _savedOnCoreStarted = BridgeHost.OnCoreStarted;
        _installed = true;

        BackendApi.DisableAuthWarmup = true;
        BridgeHost.ApiFactory = FreshCoreFactory();
        BridgeHost.OnCoreStarted = delegate(BackendApi api)
        {
            // 故意什么都不做:生产默认那一步 5 秒后联网(见类注释与 D-101)。
        };
    }

    /// <summary>只把内核工厂换回"关预热"的版本(用在用例中途换掉那个故意抛异常的工厂之后)。</summary>
    internal static void InstallCoreFactoryOnly()
    {
        BackendApi.DisableAuthWarmup = true;
        BridgeHost.ApiFactory = FreshCoreFactory();
    }

    /// <summary>"关预热"的内核工厂(默认那个会真的往 login.live.com 发预热,见 D-100)。</summary>
    internal static Func<string, EventBus, BackendApi> FreshCoreFactory()
    {
        return delegate(string root, EventBus bus)
        {
            BackendApi.DisableAuthWarmup = true;
            return new BackendApi(bus);
        };
    }

    /// <summary>还原注入缝与预热开关(顺序无所谓,值都从快照来)。</summary>
    internal static void Restore()
    {
        if (!_installed)
        {
            return;
        }

        BackendApi.DisableAuthWarmup = _savedWarmupSwitch;
        BridgeHost.ApiFactory = _savedApiFactory!;
        BridgeHost.OnCoreStarted = _savedOnCoreStarted;
        BridgeHost.ResetCore();
        _installed = false;
    }

    /// <summary>
    /// 当前进程所有回环监听端口(升序)。用来做**逐端口**的监听器取证:
    /// 起来之后该端口在表里,停掉之后不在表里 —— 不受其它用例的 socket 影响(不做计数比较)。
    /// </summary>
    internal static List<int> LoopbackListeners()
    {
        return IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Where(endpoint => IPAddress.IsLoopback(endpoint.Address))
            .Select(endpoint => endpoint.Port)
            .Distinct()
            .OrderBy(port => port)
            .ToList();
    }

    /// <summary>能不能连上这个回环端口(用真实 socket 连一次,连不上返回 false)。</summary>
    internal static bool CanConnect(int port)
    {
        try
        {
            using TcpClient client = new TcpClient();
            return client.ConnectAsync(IPAddress.Loopback, port).Wait(TimeSpan.FromSeconds(3)) && client.Connected;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>端口现在能不能被重新绑上(能 = 上一个监听器真的放掉了)。</summary>
    internal static bool CanBind(int port)
    {
        try
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, port);
            probe.Start();
            probe.Stop();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 等到"没有新监听端口"为止(给别的用例的socket 一点时间退出,免得把瞬时状态当泄漏)。
    /// 泄漏的监听器会一直在表里,所以这个等待不会把真问题等没。
    /// </summary>
    internal static bool WaitUntilNoNewLoopbackListener(List<int> before, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (LoopbackListeners().Except(before).ToList().Count == 0)
            {
                return true;
            }

            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            Thread.Sleep(150);
        }
    }

    /// <summary><paramref name="root"/> 之下的全部条目(目录 + 文件,递归,绝对路径)。</summary>
    internal static HashSet<string> EntriesUnder(string root)
    {
        return new HashSet<string>(
            Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>当前进程的线程数。</summary>
    internal static int LiveThreadCount()
    {
        using Process process = Process.GetCurrentProcess();
        return process.Threads.Count;
    }

    /// <summary>取一段时间窗口里的线程数**最小值**(退出中的线程与线程池抖动都能被这层过滤掉)。</summary>
    internal static int SettledThreadCount(TimeSpan window)
    {
        int best = int.MaxValue;
        DateTime deadline = DateTime.UtcNow + window;
        do
        {
            best = Math.Min(best, LiveThreadCount());
            Thread.Sleep(100);
        }
        while (DateTime.UtcNow < deadline);

        return best;
    }
}
