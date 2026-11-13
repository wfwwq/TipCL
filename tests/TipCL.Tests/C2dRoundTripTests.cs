using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core.Launcher;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C2d:**一次真实的 RPC 往返**(<c>BridgeClient</c> → 回环 HTTP → <c>RpcDispatcher</c> → 真
/// <c>BackendApi</c> 方法 → 结果回到调用方),外加一次真实的事件流(SSE)投递。
///
/// 选的方法与"为什么安全"(任务书要求逐条说明):
/// <list type="bullet">
/// <item><c>get_language</c> → 只读 <c>I18nCore.CurrentLanguage()</c>(内存里的语言码),无 IO;</item>
/// <item><c>translate</c> → 读 Core 内嵌的 <c>Resources/locales/*.json</c>(程序集内嵌资源),不落盘、不联网;</item>
/// <item><c>available_languages</c> → 同一张内嵌词表,顺手证明"结构化(对象)返回值"也能过桥;</item>
/// <item><c>get_instances</c> → 只扫隔离目录下的实例目录,返回的 path 必须落在 TestHome.Root 下;</item>
/// <item><c>create_instance</c> → 在隔离目录里补齐标准目录,顺带触发一次 <c>ui_changed</c> 事件(SSE 用);</item>
/// <item>不存在的 <c>no_such_method_c2d</c> → 证明分发器真的在解析方法名(返回 error,不是静默成功)。</item>
/// </list>
/// 一个都不碰:下载、启动游戏、java_list 这类会扫盘/联网的方法一律不调。
/// 用例体一律 <c>async Task</c> + <c>await</c>(避免 xUnit1031 警告)。
/// </summary>
[Collection(C2dFixtures.Collection)]
public class C2dRoundTripTests
{
    /// <summary>基础往返:字符串返回值。<c>get_language</c> 返回当前语言码(实测 `zh_CN`)。</summary>
    [Fact]
    public async Task RoundTrip_get_language_returns_the_real_language_code()
    {
        C2dFixtures.Install();
        try
        {
            using BackendBootstrap boot = new BackendBootstrap();
            BridgeHost? host = await boot.StartAsync();
            Assert.NotNull(host);

            JsonElement result = await host!.Client.CallAsync("get_language");
            string language = result.GetString() ?? "";

            Assert.NotEqual("", language);
            Assert.Equal(I18nCore.CurrentLanguage(), language);
            Assert.Contains(language, new[] { "zh_CN", "en" });

            boot.Stop();
        }
        finally
        {
            C2dFixtures.Restore();
        }
    }

    /// <summary>
    /// 带参数 + 读内嵌词表的往返:<c>translate</c>。中文词表把键原样返回,英文词表里
    /// <c>启动</c> = <c>Launch</c>(两个期望值都是本机实测的,不是猜的)。
    /// </summary>
    [Fact]
    public async Task RoundTrip_translate_reads_the_embedded_locale_table()
    {
        C2dFixtures.Install();
        try
        {
            using BackendBootstrap boot = new BackendBootstrap();
            BridgeHost? host = await boot.StartAsync();
            Assert.NotNull(host);

            JsonElement zh = await host!.Client.CallAsync("translate", new Dictionary<string, object>
            {
                ["key"] = "启动"
            });
            JsonElement en = await host.Client.CallAsync("translate", new Dictionary<string, object>
            {
                ["key"] = "启动",
                ["lang"] = "en"
            });

            Assert.Equal("启动", zh.GetString());
            Assert.Equal("Launch", en.GetString());
            Assert.Equal(I18nCore.Tr("启动"), zh.GetString());

            boot.Stop();
        }
        finally
        {
            C2dFixtures.Restore();
        }
    }

    /// <summary>结构化返回值(对象)也能过桥:<c>available_languages</c> 返回 {语言码: 名称}。</summary>
    [Fact]
    public async Task RoundTrip_available_languages_returns_an_object()
    {
        C2dFixtures.Install();
        try
        {
            using BackendBootstrap boot = new BackendBootstrap();
            BridgeHost? host = await boot.StartAsync();
            Assert.NotNull(host);

            JsonElement result = await host!.Client.CallAsync("available_languages");

            Assert.Equal(JsonValueKind.Object, result.ValueKind);
            Assert.True(result.TryGetProperty("zh_CN", out JsonElement zh));
            Assert.Equal("简体中文", zh.GetString());
            Assert.True(result.TryGetProperty("en", out JsonElement en));
            Assert.Equal("English", en.GetString());

            boot.Stop();
        }
        finally
        {
            C2dFixtures.Restore();
        }
    }

    /// <summary>
    /// 返回真实业务数据 + 数据落点:<c>get_instances</c>。它的 <c>path</c> 必须在隔离目录之下
    /// (实测:单根模式下就是 &lt;TestHome.Root&gt;\.minecraft)。
    /// </summary>
    [Fact]
    public async Task RoundTrip_get_instances_returns_paths_under_the_isolated_home()
    {
        C2dFixtures.Install();
        try
        {
            using BackendBootstrap boot = new BackendBootstrap();
            BridgeHost? host = await boot.StartAsync();
            Assert.NotNull(host);

            JsonElement result = await host!.Client.CallAsync("get_instances");

            Assert.Equal(JsonValueKind.Array, result.ValueKind);
            Assert.True(result.GetArrayLength() >= 1, "至少要有单根实例那一行");
            string path = result[0].GetProperty("path").GetString() ?? "";
            Assert.StartsWith(TestHome.Root, path, StringComparison.Ordinal);
            Assert.NotEqual("", result[0].GetProperty("name").GetString());

            boot.Stop();
        }
        finally
        {
            C2dFixtures.Restore();
        }
    }

    /// <summary>分发器真的在解析方法名:不存在的名字必须报错(不是静默成功)。</summary>
    [Fact]
    public async Task RoundTrip_unknown_method_is_an_error()
    {
        C2dFixtures.Install();
        try
        {
            using BackendBootstrap boot = new BackendBootstrap();
            BridgeHost? host = await boot.StartAsync();
            Assert.NotNull(host);

            BridgeCallException ex = await Assert.ThrowsAsync<BridgeCallException>(
                () => host!.Client.CallAsync("no_such_method_c2d"));

            Assert.Equal("no_such_method_c2d", ex.Method);
            Assert.Contains("unknown method", ex.Message, StringComparison.OrdinalIgnoreCase);

            boot.Stop();
        }
        finally
        {
            C2dFixtures.Restore();
        }
    }

    /// <summary>
    /// 真事件流:接上 <c>/events</c>(SSE)之后调 <c>create_instance</c>,后端会
    /// <c>Emit("ui_changed")</c>,客户端必须真的收到(实测)。顺带取证落点在隔离目录里。
    /// </summary>
    [Fact]
    public async Task RoundTrip_events_reach_the_client_over_sse()
    {
        C2dFixtures.Install();
        try
        {
            using BackendBootstrap boot = new BackendBootstrap();
            BridgeHost? host = await boot.StartAsync();
            Assert.NotNull(host);

            using ManualResetEventSlim arrived = new ManualResetEventSlim(false);
            string first = "";
            string seen = "";
            host!.Client.EventReceived += delegate(object? sender, BridgeEvent ev)
            {
                // 连上事件流时 BridgeServer 会先发一条握手事件(event: hello,见 BridgeServer.cs),
                // 所以这里**只认 ui_changed** —— 否则握手会把"收到事件"这件事提前点亮(实测:
                // 并行通道里正是这样拿到 hello 而误判的)。
                if (first.Length == 0)
                {
                    first = ev.Event;
                }

                if (ev.Event == "ui_changed")
                {
                    seen = ev.Event;
                    arrived.Set();
                }
            };
            host.Client.ConnectEvents();
            Assert.True(WaitFor(() => host.Client.StreamConnected, TimeSpan.FromSeconds(10)), "事件流没连上: " + host.Client.LastStreamError);

            string instance = "c2d-rt-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            await host.Client.CallAsync("create_instance", new Dictionary<string, object>
            {
                ["name"] = instance
            });

            Assert.True(arrived.Wait(TimeSpan.FromSeconds(20)), "20 秒内没收到 ui_changed 事件(第一条事件是 " + first + ")");
            Assert.Equal("ui_changed", seen);

            // 顺带取证:这次调用的落点也在隔离目录里。**不要**断言某一种具体形状:隔离根里有没有
            // .instance.json(单根模式)取决于别的用例有没有先建过,而 Instances.ResolveName 在
            // 单根模式下会把未知名字映射成根实例(Instance.cs 的 is_root 分支),两种模式下的落点不同:
            //   - 单根模式:补齐根实例的标准目录(<root>\.minecraft\mods ...);
            //   - 多实例模式:新建 <root>\.minecraft\<名字>\...
            // 两种都必须在隔离目录之下 —— 所以这里按"哪种形状出现"取落点再断言前缀(实测两种都见过)。
            string rootInstance = Path.Combine(TestHome.Root, ".minecraft");
            bool rootMode = Directory.Exists(Path.Combine(rootInstance, "mods"));
            bool namedMode = Directory.Exists(Path.Combine(rootInstance, instance));
            Assert.True(rootMode || namedMode, "create_instance 的落点两种形状都不是");
            string landed = namedMode ? Path.Combine(rootInstance, instance) : rootInstance;
            Assert.StartsWith(TestHome.Root, landed, StringComparison.Ordinal);

            boot.Stop();
        }
        finally
        {
            C2dFixtures.Restore();
        }
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
