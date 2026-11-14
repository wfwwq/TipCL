using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TipCL.Models;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C3b(启动页需要的两个小对话框:<c>CrashUi</c> / <c>VersionSetupDialog</c>)期望值的**权威副本**,
/// 与 C1a/C1c/C2b/C2c1/C2c2/C3a 同一套做法:期望值来自 Windows 源与任务书,在测试里各留一份,
/// 源码改了而期望没跟着改会立刻红。每条都注明来源:【Windows 源】/【任务书】/【本机实测】。
/// </summary>
internal static class C3bExpected
{
    /// <summary>【Windows 源】<c>CrashUi.cs:144-148</c>:卡片标题。</summary>
    internal const string CrashTitle = "启动出问题了";

    /// <summary>【Windows 源】<c>CrashUi.cs:144-148</c>:两个选项,**顺序即返回值**(0 = 关闭,1 = 重新启动)。</summary>
    internal static readonly string[] CrashOptions = { "关闭", "重新启动" };

    /// <summary>【Windows 源】<c>CrashUi.cs:148</c>:<c>Dlg.Choose(..., 760.0)</c>。</summary>
    internal const double CrashDialogWidth = 760.0;

    /// <summary>【Windows 源】<c>CrashUi.cs:22</c>:<c>sPanel.MinWidth = 520.0</c>。</summary>
    internal const double CrashPanelMinWidth = 520.0;

    /// <summary>【Windows 源】<c>CrashUi.cs:45</c>:日志框 <c>Height = 190.0</c> + <c>Input.Log</c> 样式。</summary>
    internal const double CrashLogHeight = 190.0;

    internal const string CrashLogStyle = "Input.Log";

    /// <summary>【Windows 源】<c>CrashUi.cs:89-143</c>:5 个固定工具按钮,**顺序照源码**。</summary>
    internal static readonly string[] CrashChips =
    {
        "打开日志文件", "导出报告", "上传给开发者", "附说明反馈", "复制全文",
    };

    /// <summary>【Windows 源】<c>CrashUi.cs:80</c>:「交给 AI 修复」只在 <c>gameError</c> 为 true 时出现。</summary>
    internal const string CrashAiFix = "交给 AI 修复";

    /// <summary>【Windows 源】<c>CrashUi.cs:19</c>:标题都空时的兜底文案。</summary>
    internal const string CrashFallbackTitle = "游戏异常退出";

    /// <summary>【Windows 源】<c>CrashUi.cs:33</c>:<c>I18n.L("退出码 {0}{1}", code, hint)</c>。</summary>
    internal const string CrashExitFormat = "退出码 {0}{1}";

    /// <summary>【Windows 源】<c>CrashUi.cs:122</c>:「附说明反馈」弹的那个小对话框的标题 / 确定按钮。</summary>
    internal const string FeedbackTitle = "反馈这次崩溃";

    internal const string FeedbackOk = "发送";

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:20</c>:版本隔离的 4 个取值(下标与文案一一对应)。</summary>
    internal static readonly string[] IsoValues = { "", "none", "saves", "all" };

    internal static readonly string[] IsoLabels = { "跟随全局", "关闭(共用实例目录)", "隔离存档", "隔离全部" };

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:31</c>:垃圾回收器的 5 个取值 / 文案。</summary>
    internal static readonly string[] GcValues = { "auto", "g1", "zgc", "serial", "none" };

    internal static readonly string[] GcLabels = { "自动", "G1（通用）", "ZGC（大内存）", "Serial（老电脑）", "不指定" };

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:49-54</c>:窗口模式的 3 个文案 ↔ 取值。</summary>
    internal static readonly string[] WinModes = { "窗口", "全屏", "最大化" };

    internal static readonly string[] WinModeValues = { "window", "fullscreen", "maximized" };

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:61</c>:标题是「版本设置 · 」+ 版本 id(注意"·"两边各一个空格)。</summary>
    internal const string VersionTitlePrefix = "版本设置 · ";

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:61</c>:确定按钮文案。</summary>
    internal const string VersionOk = "保存";

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:60</c>:<c>MinWidth = 560.0</c>。</summary>
    internal const double VersionScrollMinWidth = 560.0;

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:59</c>:<c>BindWindowMaxHeight(0.55, 260.0)</c>。</summary>
    internal const double VersionMaxHeightPct = 0.55;

    internal const double VersionMaxHeightFloor = 260.0;

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:38</c>:账号列表头部的「不绑定」项。</summary>
    internal const string NoAccount = "(不绑定)";

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:44</c>:端口输入框的占位文案(也是默认值)。</summary>
    internal const string PortPlaceholder = "25565";

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:58</c>:14 个字段的标签(**顺序照源码**,测试按顺序取控件)。</summary>
    internal static readonly string[] Fields =
    {
        "版本隔离", "独占内存", "JVM 参数", "垃圾回收器", "直连服务器", "端口", "启动前命令", "",
        "退出后命令", "窗口标题", "窗口模式", "绑定账号", "统一通行证", "",
    };

    /// <summary>【Windows 源】<c>VersionSetupDialog.cs:65-93</c>:写回后端的键(**顺序照源码**)。</summary>
    internal static readonly string[] PayloadKeys =
    {
        "isolation", "jvm_args", "gc", "server", "port", "pre_launch", "pre_launch_wait",
        "post_launch", "window_title", "window_mode", "login_account", "nide8_id", "hidden", "memory_mb",
    };
}

/// <summary>
/// C3b 的全局静态快照。本包会碰到的全局量全在这张表里,<see cref="Dispose"/> 里逐个还回去
/// (D-69 的纪律:全局改动必须在 finally/Dispose 里还原)。
///
/// 用例必须在 <c>AvaloniaSession.Run(...)</c> 的用例体里建这个作用域(会话线程上一次只跑一个用例体)。
/// </summary>
internal sealed class C3bScope : IDisposable
{
    private readonly BridgeClient? _client;

    private readonly Action<string, string, ToastKind>? _toast;

    private readonly Grid? _host;

    private readonly Control? _blur;

    private readonly bool _smoke;

    private readonly Action<Control, Action>? _answer;

    private readonly Action<string>? _cancel;

    private readonly bool _motionEnabled;

    private readonly double _motionScale;

    private readonly Func<Control, string, Task<bool>>? _clipboard;

    private readonly Action<CrashReport>? _aiFix;

    private bool _disposed;

    private C3bScope()
    {
        _client = AppServices.Client;
        _toast = AppServices.ToastSink;
        _host = Dlg.Host;
        _blur = Dlg.BlurTarget;
        _smoke = Dlg.SmokeActive;
        _answer = Dlg.SmokeAutoAnswer;
        _cancel = Dlg.SmokeAutoCancelSystemDialog;
        _motionEnabled = Motion.UserEnabled;
        _motionScale = Motion.Scale;
        _clipboard = CrashUi.ClipboardWriter;
        _aiFix = CrashUi.AiFixLauncher;
    }

    internal static C3bScope Capture()
    {
        return new C3bScope();
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
        Dlg.Host = _host;
        Dlg.BlurTarget = _blur;
        Dlg.SmokeActive = _smoke;
        Dlg.SmokeAutoAnswer = _answer;
        Dlg.SmokeAutoCancelSystemDialog = _cancel;
        Motion.Enabled = _motionEnabled;
        Motion.Scale = _motionScale;
        CrashUi.ClipboardWriter = _clipboard;
        CrashUi.AiFixLauncher = _aiFix;
    }
}

/// <summary>
/// 一个**回环假后端** + 指向它的真 <see cref="BridgeClient"/>(复用 C2a 的 <see cref="FakeBridgeServer"/>:
/// <c>TcpListener</c> 绑 127.0.0.1:0,**不是** <c>HttpListener</c>,见 D-27)。
///
/// 红线里禁的是 <c>BackendApi</c> / <c>BridgeServer</c> / <c>PluginManager</c> 与外部进程;
/// 这里既没有起真后端,也没有出网 —— 只有一条 127.0.0.1 的连接,与 C2a / C2c2 / C2d 的既有做法一致。
/// </summary>
internal sealed class C3bBackend : IDisposable
{
    private readonly FakeBridgeServer _server;

    private readonly BridgeClient _client;

    internal C3bBackend(params byte[][] responses)
    {
        _server = new FakeBridgeServer(responses);
        _client = new BridgeClient(new Uri("http://127.0.0.1:" + _server.Port + "/"), C3bFiles.Token);
        AppServices.Client = _client;
    }

    /// <summary>至少等到 <paramref name="count"/> 个请求到达(超时即失败,不静默放过)。</summary>
    internal void Need(int count)
    {
        _server.WaitForRequests(count);
    }

    internal string RequestAt(int index)
    {
        return _server.RequestAt(index);
    }

    internal int RequestsCount
    {
        get
        {
            lock (_server.Requests)
            {
                return _server.Requests.Count;
            }
        }
    }

    /// <summary>等到"再收到第 <paramref name="count"/> 个请求"为止 —— 用 <c>Assert.Throws&lt;TimeoutException&gt;</c> 包住它,就是在钉"**没有**多打那次请求"。</summary>
    internal void MustNotReach(int count, int timeoutMs = 250)
    {
        _server.WaitForRequests(count, timeoutMs);
    }

    /// <summary>组一个成功的 JSON-RPC 响应体:<c>{"result": &lt;json&gt;}</c>。</summary>
    internal static byte[] Rpc(string resultJson)
    {
        return FakeBridgeServer.Response("{\"result\":" + resultJson + "}");
    }

    /// <summary>组一个失败的 JSON-RPC 响应体(<c>BridgeClient</c> 会抛 <c>BridgeCallException</c>)。</summary>
    internal static byte[] RpcError(string message)
    {
        return FakeBridgeServer.Response("{\"error\":{\"message\":\"" + message + "\"}}");
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
    }
}

/// <summary>C3b 的公共小工具:泵、找控件、点按钮、取 JSON 字段。全部**不模拟真实输入设备**(用 <c>RaiseEvent</c>)。</summary>
internal static class C3bFiles
{
    /// <summary><c>BridgeClient</c> 的令牌必须 64 字符(C2a 的校验)。</summary>
    internal const string Token = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    /// <summary>把 Dispatcher 排队的活儿跑 <paramref name="rounds"/> 轮(headless 下要手动推渲染计时器)。</summary>
    internal static void Flush(int rounds = 1)
    {
        for (int i = 0; i < rounds; i++)
        {
            UiHost.Flush();
        }
    }

    /// <summary>
    /// 泵到 <paramref name="until"/> 为真为止,超时就**抛异常**(不静默放过)。
    /// 每轮都 <c>UiHost.Flush()</c> —— 续体是投回 Dispatcher 的(实测会话线程的
    /// <c>SynchronizationContext</c> 就是 <c>AvaloniaSynchronizationContext</c>),靠它才跑得起来。
    ///
    /// 计时用 <see cref="Stopwatch"/> 而不是"轮数 × 2ms":<c>UiHost.Flush</c> 里要推一次渲染计时器,
    /// 一轮的真实耗时远大于 2ms,按轮数记会把 9 秒的预算拖成几十秒 —— 那样先超时的是
    /// <c>AvaloniaSession</c> 的 30 秒,25 秒死等的会话线程会把后面每条用例都拖成超时(本包实测踩到,
    /// 见 PROGRESS 的 C3b 小节与 D-120)。所以这里的上限必须**明显小于** 30 秒,
    /// 保证失败时冒出来的是本方法自己的异常。
    /// </summary>
    internal static void Pump(Func<bool> until, int timeoutMs = 9000)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!until())
        {
            if (watch.ElapsedMilliseconds >= timeoutMs)
            {
                throw new TimeoutException("C3b 用例等待 " + timeoutMs + " ms 仍未满足条件");
            }

            UiHost.Flush();
            Thread.Sleep(2);
        }
    }

    /// <summary>
    /// 推几轮 Dispatcher 让"点了按钮之后的同步 / 微异步效果"落地,但**不等**那个还没结束的任务。
    /// 用途:对话框还开着、而用例要断言的是"点了之后的效果"(复制、取消文件选择器......)时
    /// —— 这时候去 <c>PumpTask(对话框任务)</c> 必然死等到会话超时(本包实测踩到,见 D-120)。
    /// </summary>
    internal static void Settle(int rounds = 8)
    {
        for (int i = 0; i < rounds; i++)
        {
            UiHost.Flush();
            Thread.Sleep(2);
        }
    }

    /// <summary>泵到对话框出现并返回它(那张卡片)。用途:<c>ShowAsync</c> 要先跑一次 RPC 才推卡片。</summary>
    internal static DialogCard WaitDialog(int timeoutMs = 9000)
    {
        DialogCard? card = null;
        Pump(() =>
        {
            card = Dlg.Host?.GetLogicalDescendants().OfType<DialogCard>().LastOrDefault();
            return card != null;
        }, timeoutMs);
        return card!;
    }

    /// <summary>泵到 <paramref name="task"/> 结束(不阻塞等续体:一边泵一边看它完没完)。</summary>
    internal static void PumpTask(Task task, int timeoutMs = 9000)
    {
        Pump(() => task.IsCompleted, timeoutMs);
    }

    /// <summary>
    /// 按文字找一个按钮。<c>Ui.Btn</c> 给了图标时 <c>Content</c> 是图文组合面板(不是字符串),
    /// 但同一条路径会给按钮设 <c>AutomationProperties.Name</c>(<c>Ui.cs:316-318</c>),
    /// 所以这里两种都认。
    /// </summary>
    internal static Button ByText(Control root, string text)
    {
        return Descendants(root).OfType<Button>().First(b =>
            string.Equals(AutomationProperties.GetName(b), text, StringComparison.Ordinal) ||
            string.Equals(b.Content as string, text, StringComparison.Ordinal));
    }

    internal static bool HasButton(Control root, string text)
    {
        return Descendants(root).OfType<Button>().Any(b =>
            string.Equals(AutomationProperties.GetName(b), text, StringComparison.Ordinal) ||
            string.Equals(b.Content as string, text, StringComparison.Ordinal));
    }

    /// <summary>按钮上的文字(先取自动化名,退回去找里面的 TextBlock)。</summary>
    internal static string TextOf(Button button)
    {
        string? name = AutomationProperties.GetName(button);
        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }

        if (button.Content is string text)
        {
            return text;
        }

        TextBlock? block = button.GetLogicalDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => !string.IsNullOrEmpty(t.Text));
        return block?.Text ?? "";
    }

    /// <summary>程序化点一个按钮(不模拟输入设备)。</summary>
    internal static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    internal static IEnumerable<Control> Descendants(Control root)
    {
        return root.GetLogicalDescendants().OfType<Control>();
    }

    internal static List<TextBlock> Texts(Control root)
    {
        return Descendants(root).OfType<TextBlock>().ToList();
    }

    /// <summary>对话框里的输入框,按可视顺序(ComboBox 模板里的那个不算)。</summary>
    internal static List<TextBox> Inputs(Control root)
    {
        return Descendants(root).OfType<TextBox>()
            .Where(box => box.FindLogicalAncestorOfType<ComboBox>() == null)
            .ToList();
    }

    internal static List<ComboBox> Combos(Control root)
    {
        return Descendants(root).OfType<ComboBox>().ToList();
    }

    internal static List<CheckBox> Checks(Control root)
    {
        return Descendants(root).OfType<CheckBox>().ToList();
    }

    /// <summary>找一个 TextBlock 的文案(找不到就返回 null,断言里自己写清楚)。</summary>
    internal static string? FindText(Control root, string text)
    {
        return Texts(root).FirstOrDefault(t => string.Equals(t.Text, text, StringComparison.Ordinal))?.Text;
    }

    /// <summary>
    /// 从假后端收到的 JSON-RPC 请求原文里取 <c>params</c>(测试要钉"写回去的到底是什么")。
    /// 取不到就抛 —— 不静默返回空对象。
    /// </summary>
    internal static JsonElement Params(string request)
    {
        int index = request.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(index > 0, "假后端收到的请求里没有空行分隔的正文:\n" + request);
        string body = request.Substring(index + 4);
        using JsonDocument doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("params", out JsonElement parameters), "请求里没有 params:\n" + body);
        return parameters.Clone();
    }

    /// <summary>取请求的方法名(断言"打的是哪个后端方法")。</summary>
    internal static string Method(string request)
    {
        int index = request.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(index > 0, "假后端收到的请求里没有空行分隔的正文:\n" + request);
        using JsonDocument doc = JsonDocument.Parse(request.Substring(index + 4));
        return doc.RootElement.GetProperty("method").GetString() ?? "";
    }
}
