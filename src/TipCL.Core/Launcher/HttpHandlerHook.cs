// AI2-stability:HTTP 处理器注入点。
//
// 进程里两条出网通道建 HttpClient 时都过这里:Net(下载 / 搜索 / 版本清单共用的 HttpClient)与 AI 的 AiSession。
// 生产代码不设 Wrap,行为与以前完全一样;测试程序集在模块初始化器里装一层「测试闸门」:
//   * AI 通道只放行环回地址(本机假网关),别的地址当场失败 —— ClickSweep 点到 AI 页的建议问题时不再真的发请求,
//     失败事件也就不会拖到别的用例里、在无头会话重置时才回到界面线程(DlgCloseTests 偶发 ArgumentNullException 的来源);
//   * Net 通道:当前异步流登记了假响应的请求交给假响应(并发压力测试用),其余照常放行。
// 必须在第一次建 HttpClient 之前装好(Net 会缓存客户端;之后再装要先 Net.ResetClients)。
using System;
using System.Net.Http;

namespace TipCL.Core.Launcher;

/// <summary>出网处理器的包装钩子(测试注入用)。</summary>
public static class HttpHandlerHook
{
    /// <summary>通道名:Net 的共享 HttpClient。</summary>
    public const string ChannelNet = "net";

    /// <summary>通道名:AI 会话(AiSession)。</summary>
    public const string ChannelAi = "ai";

    /// <summary>(真处理器, 通道名) → 实际使用的处理器。为空 = 不包装。</summary>
    public static Func<HttpMessageHandler, string, HttpMessageHandler>? Wrap;

    /// <summary>按当前钩子包装处理器;钩子抛异常或返回 null 时退回真处理器(注入点不能把生产路径弄坏)。</summary>
    public static HttpMessageHandler Apply(HttpMessageHandler inner, string channel)
    {
        Func<HttpMessageHandler, string, HttpMessageHandler>? wrap = Wrap;
        if (wrap == null)
        {
            return inner;
        }
        try
        {
            return wrap(inner, channel) ?? inner;
        }
        catch (Exception)
        {
            return inner;
        }
    }
}
