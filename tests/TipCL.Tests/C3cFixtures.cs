using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C3c(启动页)期望值的**权威副本**,与 C1a/C1b/C1c/C2*/C3a/C3b 同一套做法:
/// 期望值来自 Windows 源与本包任务书,在测试里各留一份,源码改了而期望没跟着改会立刻红。
/// 每条都注明来源:【Windows 源】/【任务书】/【本机实测】。
/// </summary>
internal static class C3cExpected
{
    /// <summary>【Windows 源】<c>MainWindow.NavDefs[0]</c> / <c>NavModel.PageForNavKey["launch"]</c>:页面 id。</summary>
    internal const string PageId = "launch";

    /// <summary>【Windows 源】<c>LaunchPage.cs:230</c>:<c>Title =&gt; I18n.L("启动")</c>。</summary>
    internal const string Title = "启动";

    /// <summary>【Windows 源】<c>LaunchPage.cs:476-483</c>:区块 id 的顺序(BlockHost 的 specs)。</summary>
    internal static readonly string[] BlockIds = { "news", "tasks", "playtime", "notes", "log" };

    /// <summary>【Windows 源】同上的区块标题(经 <see cref="I18n.L"/>)。</summary>
    internal static readonly string[] BlockTitles = { "新闻", "下载任务", "游戏时长", "便签", "日志" };

    /// <summary>【Windows 源】同上:只有 news 默认可见。</summary>
    internal static readonly bool[] BlockDefaultVisible = { true, false, false, false, false };

    /// <summary>【Windows 源】<c>LaunchPage.cs:485-489</c>:内容区块 MaxHeight 绑页面 ActualHeight × 0.62。</summary>
    internal const double BlocksMaxHeightPct = 0.62;

    /// <summary>【Windows 源】<c>LaunchPage.cs:483</c>:<c>contentMargin = new Thickness(56, 0, 24, 18)</c>。</summary>
    internal const double BlocksMarginLeft = 56.0;

    internal const double BlocksMarginRight = 24.0;

    internal const double BlocksMarginBottom = 18.0;

    /// <summary>【Windows 源】<c>LaunchPage.cs:442-445</c>:英雄区 MaxWidth 680 / 边距 (56,0,24,0)。</summary>
    internal const double HeroMaxWidth = 680.0;

    internal const double HeroMarginLeft = 56.0;

    internal const double HeroMarginRight = 24.0;

    /// <summary>【Windows 源】<c>LaunchPage.cs:399-403</c>:主按钮字号 16。</summary>
    internal const double LaunchButtonFontSize = 16.0;

    /// <summary>【Windows 源】<c>LaunchPage.cs:406-414</c>:主按钮外壳 48 高 / 圆角 12 / 内边距 30。</summary>
    internal const double LaunchShellHeight = 48.0;

    internal const double LaunchShellCorner = 12.0;

    internal const double LaunchShellPadding = 30.0;

    /// <summary>【Windows 源】<c>LaunchPage.cs:144-150</c>:「停止」外壳 48 高 / 圆角 12 / 内边距 26。</summary>
    internal const double StopShellHeight = 48.0;

    internal const double StopShellCorner = 12.0;

    internal const double StopShellPadding = 26.0;

    /// <summary>【Windows 源】<c>LaunchPage.cs:365-368</c>:进度条 4 高 / 320 宽 / 不透明度 0.92。</summary>
    internal const double ProgressHeight = 4.0;

    internal const double ProgressWidth = 320.0;

    internal const double ProgressOpacity = 0.92;

    /// <summary>【Windows 源】<c>LaunchPage.cs:125-131</c>:大标题 34 号 ExtraBold / MaxWidth 620。</summary>
    internal const double HeroTitleFontSize = 34.0;

    internal const double HeroTitleMaxWidth = 620.0;

    /// <summary>【Windows 源】<c>LaunchPage.cs:117-123</c>:kicker 12.5 号 / MaxWidth 560。</summary>
    internal const double KickerFontSize = 12.5;

    internal const double KickerMaxWidth = 560.0;

    /// <summary>【Windows 源】<c>LaunchPage.cs:136-142</c>:新闻跑马灯 12.5 号 / MaxWidth 520。</summary>
    internal const double NewsTickerFontSize = 12.5;

    /// <summary>【Windows 源】<c>LaunchPage.cs:391</c>:小标签行外边距 (0,10,0,24)。</summary>
    internal const double ChipRowMarginTop = 10.0;

    internal const double ChipRowMarginBottom = 24.0;

    /// <summary>【Windows 源】<c>LaunchPage.cs:41,187</c>:内存滑杆 1024–32768 默认 4096(步进 256);竖线 1×20。</summary>
    internal const double MemMin = 1024.0;

    internal const double MemMax = 32768.0;

    internal const double MemDefault = 4096.0;

    internal const double MemTick = 256.0;

    internal const string MemLabelDefault = "4096 MB";

    /// <summary>【Windows 源】<c>LaunchPage.cs:51</c>:状态文本初值「就绪」。</summary>
    internal const string ReadyText = "就绪";

    /// <summary>【Windows 源】<c>LaunchPage.cs:1159-1189</c>(SyncHero):没有实例时的标题。</summary>
    internal const string HeroNoInstance = "选择实例";

    /// <summary>【Windows 源】<c>LaunchPage.cs:1147-1148</c>(SyncBanner):没有版本时的标题 / 副标题。</summary>
    internal const string BannerNoVersion = "未选择版本";

    internal const string BannerNoVersionSub = "先到「下载」安装一个版本";

    /// <summary>【Windows 源】<c>LaunchPage.cs:1148</c>:有版本时的副标题模板。</summary>
    internal const string BannerWithVersionSub = "实例 {0} · 点「启动游戏」进入世界";

    /// <summary>【Windows 源】<c>LaunchPage.cs:950</c>:空版本列表时的状态文案。</summary>
    internal const string NoVersionsStatus = "这个实例还没有版本，先去「下载」装一个";

    /// <summary>【Windows 源】<c>LaunchPage.cs:1316-1317</c>:没选版本就点启动时的 Toast。</summary>
    internal const string NoVersionToastTitle = "没有版本";

    internal const string NoVersionToastBody = "请先到「下载」安装";

    /// <summary>【Windows 源】<c>LaunchPage.cs:1380</c>:启动过程中把状态改成「准备启动...」。</summary>
    internal const string PreparingText = "准备启动...";

    /// <summary>【Windows 源】<c>LaunchPage.cs:1814</c>:<c>game_exited</c> 的两句状态(带 / 不带退出码)。</summary>
    internal const string GameExited = "游戏已退出";

    internal const string GameExitedFormat = "游戏已退出（退出码 {0}）";

    /// <summary>【Windows 源】<c>LaunchPage.cs:1820</c>:<c>game_started</c> 的状态。</summary>
    internal const string GameStartedText = "游戏进程已启动,正在加载世界...";

    /// <summary>【Windows 源】<c>LaunchPage.cs:1845</c>:任务完成时的两句状态。</summary>
    internal const string FinishedText = "已完成";

    internal const string FailedText = "✘ 失败";

    /// <summary>【Windows 源】<c>LaunchPage.cs:1834</c>:进度消息解析不出状态时的兜底。</summary>
    internal const string ProcessingText = "处理中...";

    /// <summary>【Windows 源】<c>LaunchPage.cs:1273,1245,1204,1205</c>:任务卡 / 新闻的几句占位文案。</summary>
    internal const string NoTasksText = "暂无下载任务";

    internal const string NoNewsText = "暂无新闻";

    internal const string BlankHomeText = "主页已设为空白";

    /// <summary>【Windows 源】<c>LaunchPage.cs:79</c>:便签的占位文案。</summary>
    internal const string NotesPlaceholder = "随手记点什么...";

    /// <summary>【Windows 源】<c>LaunchPage.cs:35</c>:直连服务器输入框的占位。</summary>
    internal const string ServerPlaceholder = "直连服务器 ip:port（可空）";

    /// <summary>【Windows 源】<c>LaunchPage.Popups.cs:67,1601</c>(+ <c>LaunchPage.cs</c>):三处浮层 / 登录框的标题。</summary>
    internal const string ConfigTitle = "启动配置";

    internal const string ConfigLoading = "配置加载中";

    internal const string SearchPlaceholder = "搜索实例...";

    internal const string OpenLibraryText = "打开实例库";

    internal const string NoMatchText = "没有匹配的实例";

    internal const string CurrentMarkText = "当前";

    internal const string ManageText = "管理";

    internal const string MultiplayerText = "联机";

    internal const string ConfigPillTip = "启动配置";

    /// <summary>【Windows 源】<c>LaunchPage.cs:68,74</c>:更多操作按钮的提示 / <c>Ui.Empty</c> 的图标码位。</summary>
    internal const string MoreActionsTip = "更多操作";

    internal const string ConfigGlyph = "\ue713";

    /// <summary>【Windows 源】<c>LaunchPage.cs:1529-1534</c>:「更多操作」菜单的 7 项(**顺序照源码**)。</summary>
    internal static readonly string[] MoreMenuItems =
    {
        "版本设置", "刷新新闻", "查看启动命令", "导出启动脚本 (.bat)", "创建桌面快捷方式", "修复当前版本", "打开实例文件夹",
    };

    /// <summary>【Windows 源】<c>LaunchPage.cs:1506-1512</c>:折叠开关的两句文案。</summary>
    internal const string MoreSettingsText = "更多设置";

    internal const string CollapseText = "收起";

    /// <summary>【Windows 源】<c>LaunchPage.cs:1491-1498</c>:配置浮层里的两个登录按钮。</summary>
    internal const string MicrosoftLoginText = "微软登录";

    internal const string AuthlibLoginText = "皮肤站";

    /// <summary>【Windows 源】<c>LaunchPage.cs:1591</c>:微软登录面板的标题。</summary>
    internal const string MicrosoftDialogTitle = "微软账号登录";

    /// <summary>【Windows 源】<c>LaunchPage.cs:1614</c>:皮肤站登录对话框的标题 / 确定按钮。</summary>
    internal const string AuthlibDialogTitle = "皮肤站登录";

    internal const string AuthlibDialogOk = "登录";

    /// <summary>【Windows 源】<c>LaunchPage.cs:1572-1582</c>:复制代码按钮与它的两句 Toast。</summary>
    internal const string CopyCodeText = "复制代码";

    internal const string CopiedTitle = "已复制";

    internal const string CopiedBody = "代码已放进剪贴板";

    /// <summary>【Windows 源】<c>LaunchPage.cs:1583-1589</c>:打开浏览器按钮。</summary>
    internal const string OpenBrowserText = "打开浏览器";

    /// <summary>【Windows 源】<c>LaunchPage.cs:1428-1432</c>:多开确认框的标题 / 两个选项。</summary>
    internal const string MultiRunningTitle = "游戏已经在运行";

    internal static readonly string[] MultiRunningOptions = { "取消", "打开多开并启动" };

    /// <summary>【Windows 源】<c>LaunchPage.cs:1728</c>:修复确认框的标题。</summary>
    internal const string RepairTitle = "修复版本";

    /// <summary>【Windows 源】<c>LaunchPage.cs:1737</c>:修复时「飞向任务页」的那句话。</summary>
    internal const string RepairFlyingText = "修复中";

    /// <summary>【Windows 源】<c>CrashUi.cs</c> 被本页调用时的标题(<c>LaunchPage.cs:1871</c>)。</summary>
    internal const string CrashTitle = "启动出问题了";

    /// <summary>【Windows 源】<c>LaunchPage.cs:1856</c>:任务失败且没弹过崩溃卡时的报告标题。</summary>
    internal const string LaunchFailedTitle = "启动失败";

    /// <summary>【Windows 源】<c>LaunchPage.cs:813</c>:内嵌 hero.jpg 的 pack URI(→ mac 的 avares)。</summary>
    internal const string HeroUri = "avares://TipCL/PremiumAssets/hero.jpg";

    /// <summary>【Windows 源】<c>LaunchPage.cs:1084-1108</c>(LoaderChip):6 组前缀 → 标签,不匹配回落全名。</summary>
    internal static readonly (string Version, string Chip)[] LoaderChips =
    {
        ("fabric-loader-0.20.1-26.3", "Fabric 0.20.1"),
        ("neoforge-21.1.72", "NeoForge 21.1.72"),
        ("optifine-HD_U_I7", "OptiFine HD_U_I7"),
        ("liteloader-1.12.2", "LiteLoader 1.12.2"),
        ("quilt-loader-0.23.1-1.20.1", "Quilt 0.23.1"),
        ("forge-1.20.1-47.2.0", "Forge 1.20.1"),
        ("1.20.1", "1.20.1"),
    };

    /// <summary>【Windows 源】<c>LaunchPage.cs:1181-1182</c>:Java 标签的两种取法。</summary>
    internal static readonly (string Label, string Chip)[] JavaChips =
    {
        ("Java 17 (C:\\jdk-17)", "Java 17"),
        ("jdk1.8.0_301", "Java 8"),
        ("自动选择", "Java 自动"),
    };

    /// <summary>【Windows 源】<c>LaunchPage.cs:1453-1472</c>(ExtraArgs):直连参数的切法。</summary>
    internal static readonly (string Server, string[]? Args)[] ExtraArgsCases =
    {
        ("", null),
        ("mc.example.com:25565", new[] { "--server", "mc.example.com", "--port", "25565" }),
        ("mc.example.com", new[] { "--server", "mc.example.com", "--port", "25565" }),
    };
}

/// <summary>
/// C3c 的全局静态快照。本包会碰到的全局量全在这张表里,<see cref="Dispose"/> 里逐个还回去
/// (D-69 的纪律:全局改动必须在 finally/Dispose 里还原)。
/// 用例必须在 <c>AvaloniaSession.Run(...)</c> 的用例体里建这个作用域(会话线程上一次只跑一个用例体)。
/// </summary>
internal sealed class C3cScope : IDisposable
{
    private readonly BridgeClient? _client;

    private readonly Action<string, string, ToastKind>? _toast;

    private readonly Window? _window;

    private readonly Grid? _host;

    private readonly Control? _blur;

    private readonly bool _smoke;

    private readonly Action<Control, Action>? _answer;

    private readonly Action<string>? _cancel;

    private readonly bool _motionEnabled;

    private readonly double _motionScale;

    private readonly ThemeVariant? _variant;

    private readonly IReadOnlyDictionary<string, Func<PageBase>> _registry;

    private readonly Action<Control, string>? _flyToTasks;

    private bool _disposed;

    private C3cScope()
    {
        _client = AppServices.Client;
        _toast = AppServices.ToastSink;
        _window = AppServices.Window;
        _host = Dlg.Host;
        _blur = Dlg.BlurTarget;
        _smoke = Dlg.SmokeActive;
        _answer = Dlg.SmokeAutoAnswer;
        _cancel = Dlg.SmokeAutoCancelSystemDialog;
        _motionEnabled = Motion.UserEnabled;
        _motionScale = Motion.Scale;
        _variant = Application.Current!.RequestedThemeVariant;
        _registry = PageRegistry.Snapshot();
        _flyToTasks = LaunchPage.FlyToTasksSeam;
    }

    internal static C3cScope Capture()
    {
        return new C3cScope();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AppServices.Client = _client;
        AppServices.ToastSink = _toast;
        AppServices.Window = _window;
        Dlg.Host = _host;
        Dlg.BlurTarget = _blur;
        Dlg.SmokeActive = _smoke;
        Dlg.SmokeAutoAnswer = _answer;
        Dlg.SmokeAutoCancelSystemDialog = _cancel;
        Motion.Enabled = _motionEnabled;
        Motion.Scale = _motionScale;
        Application.Current!.RequestedThemeVariant = _variant;
        PageRegistry.Restore(_registry);
        LaunchPage.FlyToTasksSeam = _flyToTasks;
    }
}

/// <summary>把 Toast 截下来(与 C3a / C3b 的同一套做法;Windows 是 <c>AppServices.Window?.Toast</c>)。</summary>
internal sealed class C3cToasts : IDisposable
{
    private readonly Action<string, string, ToastKind>? _previous;

    internal C3cToasts()
    {
        _previous = AppServices.ToastSink;
        AppServices.ToastSink = delegate(string title, string body, ToastKind kind)
        {
            Items.Add((Title: title, Body: body, Kind: kind));
        };
    }

    internal List<(string Title, string Body, ToastKind Kind)> Items { get; } = new List<(string, string, ToastKind)>();

    internal bool Has(string title)
    {
        return Items.Any(t => string.Equals(t.Title, title, StringComparison.Ordinal));
    }

    public void Dispose()
    {
        AppServices.ToastSink = _previous;
    }
}

/// <summary>
/// **按方法名路由**的回环假后端(<c>TcpListener</c> 绑 <c>127.0.0.1:0</c>,**不是** <c>HttpListener</c>,见 D-27)。
///
/// 为什么不直接用 C2a 的 <see cref="FakeBridgeServer"/>:它按"第几个请求"给预置响应,
/// 而启动页在 <c>FillCardData</c> 里既 <c>await</c> 一串调用、又 fire-and-forget 地起
/// <c>ReloadJavaAsync(scan:true)</c> / <c>LoadNewsAsync</c> / <c>LoadPlaytimeAsync</c> —— 到达顺序天生不确定,
/// 按序号发响应必然时绿时红(D-103 记过的同类病)。按方法名路由就没有这个问题。
///
/// 红线:不禁 <c>TcpListener</c>(C2a/C2d/C3b 的既有做法),但**不**起真 <c>BackendApi</c> / <c>BridgeServer</c>、
/// 不出网、不启外部进程;只连 <c>127.0.0.1</c>。
/// </summary>
internal sealed class C3cFakeApi : IDisposable
{
    private readonly TcpListener _listener;

    private readonly Thread _thread;

    private readonly BridgeClient _client;

    private readonly Dictionary<string, Func<JsonElement, string>> _routes = new Dictionary<string, Func<JsonElement, string>>(StringComparer.Ordinal);

    private readonly List<(string Method, string Params)> _calls = new List<(string, string)>();

    private volatile bool _stopped;

    internal C3cFakeApi()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _thread = new Thread(AcceptLoop)
        {
            IsBackground = true,
            Name = "C3cFakeApi",
        };
        _thread.Start();
        _client = new BridgeClient(new Uri("http://127.0.0.1:" + Port + "/"), C3cFiles.Token);
        AppServices.Client = _client;
    }

    internal int Port { get; }

    /// <summary>某个方法返回 <c>{"result": &lt;json&gt;}</c>。</summary>
    internal C3cFakeApi Route(string method, string resultJson)
    {
        _routes[method] = _ => "{\"result\":" + resultJson + "}";
        return this;
    }

    /// <summary>某个方法按请求参数现算结果(给"同一个方法不同参数要给不同答复"的用例)。</summary>
    internal C3cFakeApi Route(string method, Func<JsonElement, string> resultOf)
    {
        _routes[method] = p => "{\"result\":" + resultOf(p) + "}";
        return this;
    }

    /// <summary>某个方法回一个错误(<c>BridgeClient</c> 会抛 <c>BridgeCallException</c>)。</summary>
    internal C3cFakeApi RouteError(string method, string message)
    {
        _routes[method] = _ => "{\"error\":{\"message\":\"" + message + "\"}}";
        return this;
    }

    internal int Count(string method)
    {
        lock (_calls)
        {
            return _calls.Count(c => string.Equals(c.Method, method, StringComparison.Ordinal));
        }
    }

    /// <summary>某个方法收到的第 <paramref name="index"/> 次(0 起)参数原文;没有就抛(不静默返回空)。</summary>
    internal string ParamsOf(string method, int index = 0)
    {
        lock (_calls)
        {
            List<string> list = _calls.Where(c => string.Equals(c.Method, method, StringComparison.Ordinal)).Select(c => c.Params).ToList();
            Assert.True(list.Count > index, "假后端没有收到第 " + index + " 次 " + method + "(一共 " + list.Count + " 次）");
            return list[index];
        }
    }

    /// <summary>收到过的全部方法名(按到达顺序;排查用)。</summary>
    internal List<string> Methods()
    {
        lock (_calls)
        {
            return _calls.Select(c => c.Method).ToList();
        }
    }

    public void Dispose()
    {
        _stopped = true;
        try
        {
            _listener.Stop();
        }
        catch (Exception)
        {
        }

        try
        {
            _thread.Join(2000);
        }
        catch (Exception)
        {
        }

        _client.Dispose();
    }

    private void AcceptLoop()
    {
        while (!_stopped)
        {
            TcpClient client;
            try
            {
                client = _listener.AcceptTcpClient();
            }
            catch (Exception)
            {
                return;
            }

            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    string head = C3cWire.ReadHead(stream);
                    string body = C3cWire.DrainBody(stream, head);
                    string method = "";
                    string parameters = "{}";
                    try
                    {
                        using JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                        if (doc.RootElement.TryGetProperty("method", out JsonElement m))
                        {
                            method = m.GetString() ?? "";
                        }
                        if (doc.RootElement.TryGetProperty("params", out JsonElement p))
                        {
                            parameters = p.GetRawText();
                        }
                    }
                    catch (JsonException)
                    {
                    }

                    lock (_calls)
                    {
                        _calls.Add((method, parameters));
                    }

                    string reply;
                    Func<JsonElement, string>? route;
                    lock (_calls)
                    {
                        _routes.TryGetValue(method, out route);
                    }

                    if (route is null)
                    {
                        // 没路由的方法一律回 null(而不是报错):TryCallAsync 会回落到它自己的 fallback,
                        // CallAsync<List<...>> 会拿到 null 由调用点 ?? 兜底 —— 与"后端没这个方法"最接近。
                        reply = "{\"result\":null}";
                    }
                    else
                    {
                        try
                        {
                            using JsonDocument parametersDoc = JsonDocument.Parse(parameters);
                            reply = route(parametersDoc.RootElement.Clone());
                        }
                        catch (Exception ex)
                        {
                            // 路由自己写坏了(JSON 拼错之类):回一个错误体,**不**让连接悬着 ——
                            // 悬着会表现成"页面卡住",排查时会指向错误的方向。
                            reply = "{\"error\":{\"message\":" + JsonSerializer.Serialize("假后端路由出错: " + ex.Message) + "}}";
                        }
                    }

                    byte[] payload = Encoding.UTF8.GetBytes(reply);
                    string header =
                        "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + payload.Length +
                        "\r\nConnection: close\r\n\r\n";
                    byte[] headerBytes = Encoding.ASCII.GetBytes(header);
                    stream.Write(headerBytes, 0, headerBytes.Length);
                    stream.Write(payload, 0, payload.Length);
                    stream.Flush();
                }
            }
            catch (Exception)
            {
                // 客户端提前断开:这一条连接不算数,继续接受下一条。
            }
        }
    }
}

/// <summary>假后端的 HTTP 拆包工具(照 C2a 的 <c>FakeBridgeServer</c> 那两段,不改动它)。</summary>
internal static class C3cWire
{
    internal static string ReadHead(NetworkStream stream)
    {
        List<byte> bytes = new List<byte>();
        while (bytes.Count < 65536)
        {
            int b = stream.ReadByte();
            if (b < 0)
            {
                break;
            }

            bytes.Add((byte)b);
            int n = bytes.Count;
            // 请求头以空行结束(最后 4 个字节是 CR LF CR LF)。
            if (n >= 4 && bytes[n - 4] == '\r' && bytes[n - 3] == '\n' && bytes[n - 2] == '\r' && bytes[n - 1] == '\n')
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    internal static string DrainBody(NetworkStream stream, string head)
    {
        int length = 0;
        foreach (string line in head.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(line.Substring("Content-Length:".Length).Trim(), out int parsed))
            {
                length = parsed;
            }
        }

        if (length <= 0)
        {
            return "";
        }

        byte[] buffer = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = stream.Read(buffer, offset, length - offset);
            if (read <= 0)
            {
                break;
            }

            offset += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, offset);
    }
}

/// <summary>
/// 任务队列(<see cref="TaskStore"/>)的全局作用域:可以把队列**清空**成确定状态,Dispose 时把进作用域之前的那些行
/// 按 Id/Title/状态**逐个重建回去**(D-69 的纪律:全局静态必须还原)。
///
/// 为什么需要它:<c>TaskStore</c> 是进程级的,别的包(C2a/C2b)的用例会在里面留行,
/// 于是"任务卡显示『暂无下载任务』"这种断言会随着谁先跑而时绿时红 —— 那正是 A6/D-103 记过的病。
/// </summary>
internal sealed class C3cTaskStore : IDisposable
{
    private readonly List<TaskRow> _saved;

    private bool _disposed;

    internal C3cTaskStore(bool emptyNow = true)
    {
        _saved = TaskStore.Rows.ToList();
        if (emptyNow)
        {
            Empty();
        }
    }

    /// <summary>把当前所有任务行删掉(先标成已完成,<c>ClearFinished</c> 才肯删)。</summary>
    internal static void Empty()
    {
        foreach (TaskRow row in TaskStore.Rows.ToList())
        {
            row.Finished = true;
        }

        TaskStore.ClearFinished();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Empty();
        foreach (TaskRow row in _saved)
        {
            TaskStore.Handle(new BridgeEvent { Event = "task_added", TaskId = row.Id, Title = row.Title });
            TaskRow? now = TaskStore.Get(row.Id);
            if (now is null)
            {
                continue;
            }

            now.Status = row.Status;
            now.Speed = row.Speed;
            now.Progress = row.Progress;
            now.Indeterminate = row.Indeterminate;
            now.Finished = row.Finished;
            now.Success = row.Success;
        }
    }
}

/// <summary>C3c 的公共小工具:泵、找控件、点按钮、按方法名断言假后端。全部**不模拟真实输入设备**。</summary>
internal static class C3cFiles
{
    /// <summary><c>BridgeClient</c> 的令牌必须 64 字符(C2a 的校验)。</summary>
    internal const string Token = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    /// <summary>推几轮 Dispatcher(用法与 C3b 的同名工具一致)。</summary>
    internal static void Settle(int rounds = 8)
    {
        C3bFiles.Settle(rounds);
    }

    /// <summary>泵到条件成立(Stopwatch 计时、9 秒上限,理由见 D-120)。</summary>
    internal static void Pump(Func<bool> until, int timeoutMs = 9000)
    {
        C3bFiles.Pump(until, timeoutMs);
    }

    /// <summary>泵到某个后端方法至少被调到 <paramref name="count"/> 次。</summary>
    internal static void PumpCall(C3cFakeApi api, string method, int count = 1, int timeoutMs = 9000)
    {
        Pump(() => api.Count(method) >= count, timeoutMs);
    }

    /// <summary>泵到这个任务结束(不停下来干等:一边泵一边看它完没完)。</summary>
    internal static void PumpTask(Task task, int timeoutMs = 9000)
    {
        C3bFiles.PumpTask(task, timeoutMs);
    }

    /// <summary>页面里所有后代控件(走逻辑树,与 C3b 同一套)。</summary>
    internal static IEnumerable<Control> Descendants(Control root)
    {
        return C3bFiles.Descendants(root);
    }

    internal static Button ByText(Control root, string text)
    {
        return C3bFiles.ByText(root, text);
    }

    internal static bool HasButton(Control root, string text)
    {
        return C3bFiles.HasButton(root, text);
    }

    internal static void Click(Button button)
    {
        C3bFiles.Click(button);
    }

    internal static List<TextBlock> Texts(Control root)
    {
        return C3bFiles.Texts(root);
    }

    /// <summary>按类型找一个后代(找不到就抛,不静默返回 null)。</summary>
    internal static T Find<T>(Control root, string what) where T : Control
    {
        T? hit = Descendants(root).OfType<T>().FirstOrDefault();
        Assert.True(hit != null, "页面里找不到 " + what + "(" + typeof(T).Name + ")");
        return hit!;
    }

    internal static List<TextBlock> TextsWith(Control root, string text)
    {
        return Texts(root).Where(t => string.Equals(t.Text, text, StringComparison.Ordinal)).ToList();
    }

    internal static bool HasText(Control root, string text)
    {
        return TextsWith(root, text).Count > 0;
    }

    /// <summary>找一个 Border(按它孩子里的文案认);给浮层/玻璃块用。</summary>
    internal static Border BorderWithText(Control root, string text)
    {
        Border? hit = Descendants(root).OfType<Border>()
            .FirstOrDefault(b => b.Child is TextBlock t && string.Equals(t.Text, text, StringComparison.Ordinal));
        Assert.True(hit != null, "找不到文案为「" + text + "」的 Border");
        return hit!;
    }
}
