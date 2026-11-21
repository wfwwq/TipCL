// AI2-stability:测试进程的出网闸门(配合 Core 的 HttpHandlerHook)。
//
// 为什么要它:ClickSweepTests 会点到 AI 页「还没有对话」下面的建议问题 —— 测试环境里内置网关是「已配置」的,
// 于是真的向外网发了一次对话请求。请求失败的事件被 Post 回界面线程,常常晚到下一个用例(DlgCloseTests)里,
// 恰好在无头会话 Dispatcher.ResetForUnitTests 冲刷旧队列时执行;那一刻 Application.Current 为空,
// AiBubble 的 Res.Bind 抛 ArgumentNullException,算在了 DlgCloseTests 头上(基线复现率见 progress/AI2-stability.md)。
//
// 规则:
//   * AI 通道(AiSession):只放行环回地址(各测试自己起的本机假网关),其余当场回 HTTP 400 ——
//     不联网、不重试,而且失败是**立刻**的,事件在发起它的那个用例里就落地;
//   * Net 通道:当前异步流用 Fake(...) 登记了假响应的,交给假响应(并发压力测试用,AsyncLocal 不串到别的用例);
//     其余照常放行(不改变既有用例的行为)。
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core.Launcher;

namespace TipCL.Tests;

internal static class TestNetworkIsolation
{
    private static readonly AsyncLocal<Func<HttpRequestMessage, HttpResponseMessage?>?> Responder = new();

    private static int _blockedAi;

    /// <summary>被闸门挡下的 AI 请求数(测试用)。</summary>
    public static int BlockedAiRequests => Volatile.Read(ref _blockedAi);

    /// <summary>由 <see cref="TestIsolation.Init"/> 调(必须早于任何 HttpClient 被建出来)。</summary>
    internal static void Install()
    {
        HttpHandlerHook.Wrap = (inner, channel) => new Gate(inner, channel);
        // 顺带:走 Core 统一入口的「打开文件夹 / 登录页」在测试进程里不真的拉起资源管理器 / 浏览器
        // (用户要求跑测试不弹任何窗口;各用例仍可自己换上记录用的钩子,用完还原回这里的空操作)。
        Crash.ShellOpenHook ??= _ => Interlocked.Increment(ref _suppressedShellOpens);
        Auth.OpenBrowserHook ??= _ => Interlocked.Increment(ref _suppressedShellOpens);
    }

    private static int _suppressedShellOpens;

    /// <summary>被拦下的「打开文件夹 / 浏览器」次数(排查用)。</summary>
    public static int SuppressedShellOpens => Volatile.Read(ref _suppressedShellOpens);

    /// <summary>在当前异步流里登记假响应;responder 返回 null 表示「这个请求不管,放行」。Dispose 撤销。</summary>
    public static IDisposable Fake(Func<HttpRequestMessage, HttpResponseMessage?> responder)
    {
        Func<HttpRequestMessage, HttpResponseMessage?>? previous = Responder.Value;
        Responder.Value = responder;
        return new Restore(previous);
    }

    private sealed class Restore : IDisposable
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage?>? _previous;

        public Restore(Func<HttpRequestMessage, HttpResponseMessage?>? previous) => _previous = previous;

        public void Dispose() => Responder.Value = _previous;
    }

    internal static bool IsLoopback(Uri? uri)
    {
        if (uri == null)
        {
            return false;
        }
        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return IPAddress.TryParse(uri.Host.Trim('[', ']'), out IPAddress? ip) && IPAddress.IsLoopback(ip);
    }

    private sealed class Gate : DelegatingHandler
    {
        private readonly string _channel;

        public Gate(HttpMessageHandler inner, string channel) : base(inner)
        {
            _channel = channel;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Func<HttpRequestMessage, HttpResponseMessage?>? fake = Responder.Value;
            if (fake != null)
            {
                HttpResponseMessage? resp = fake(request);
                if (resp != null)
                {
                    resp.RequestMessage ??= request;
                    return Task.FromResult(resp);
                }
            }
            if (_channel == HttpHandlerHook.ChannelAi && !IsLoopback(request.RequestUri))
            {
                // 回 400 而不是抛异常:连接类错误会被 AiAgent 当成「网络异常」退避重试 3 次(约 7 秒),
                // 失败事件又会拖到别的用例里;4xx 不重试,事件当场落地。
                Interlocked.Increment(ref _blockedAi);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    RequestMessage = request,
                    Content = new StringContent("{\"error\":{\"message\":\"test isolation: outbound AI request blocked (" + request.RequestUri?.Host + ")\"}}",
                        System.Text.Encoding.UTF8, "application/json"),
                });
            }
            return base.SendAsync(request, cancellationToken);
        }
    }
}
