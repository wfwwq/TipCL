using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using TipCL.Models;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C5b:下载任务 / 账号 / 启动向导 三件东西的测试夹具。
///
/// 纪律与 C1c / C3b / C3c / C4 完全同一套:
/// <list type="bullet">
/// <item>碰 Avalonia 的用例体一律走 <see cref="AvaloniaSession.Run"/>(D-79);</item>
/// <item>全局静态进作用域快照、<c>Dispose</c> 还原(<see cref="C5bScope"/>,D-69);</item>
/// <item>后端用**按方法名路由**的回环假后端 <see cref="C3cFakeApi"/>(不起真后端、不出网、不启外部进程);</item>
/// <item>等待一律"一边泵 Dispatcher 一边看条件"(Stopwatch + 9 秒上限,D-120),不许在会话线程上同步阻塞(D-114);</item>
/// <item>进程级状态(<see cref="TaskStore"/>)由夹具**先清空再交出去**:任务列表是纯内存的瞬态状态,
///   别的包留下的行会让"空态"断言随机红(D-132 的同类病)。</item>
/// </list>
/// </summary>
internal sealed class C5bScope : IDisposable
{
    private readonly BridgeClient? _client;

    private readonly Action<string, string, ToastKind>? _toast;

    private readonly Window? _window;

    private readonly Grid? _host;

    private readonly Control? _blur;

    private readonly bool _smoke;

    private readonly Action<Control, Action>? _answer;

    private readonly Action<string>? _cancel;

    private readonly bool _motionUserEnabled;

    private readonly double _motionScale;

    private readonly ThemeVariant? _variant;

    private readonly string? _layoutDir;

    private readonly Func<Task>? _settingsRefresh;

    private readonly Func<Control, string, Task<bool>>? _clipboard;

    private readonly ImageDecode.BitmapDecoder _decoder;

    private bool _disposed;

    private C5bScope()
    {
        _client = AppServices.Client;
        _toast = AppServices.ToastSink;
        _window = AppServices.Window;
        _host = Dlg.Host;
        _blur = Dlg.BlurTarget;
        _smoke = Dlg.SmokeActive;
        _answer = Dlg.SmokeAutoAnswer;
        _cancel = Dlg.SmokeAutoCancelSystemDialog;
        _motionUserEnabled = Motion.UserEnabled;
        _motionScale = Motion.Scale;
        _variant = Application.Current!.RequestedThemeVariant;
        _layoutDir = PageLayoutStore.DirOverride;
        _settingsRefresh = FirstRunWizard.SettingsRefresh;
        _clipboard = CrashUi.ClipboardWriter;
        _decoder = ImageDecode.Decoder;
        // 交出去之前先把任务列表清干净(见类注释最后一条)
        C5bFiles.EmptyTaskStore();
    }

    internal static C5bScope Capture()
    {
        return new C5bScope();
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
        Motion.Enabled = _motionUserEnabled;
        Motion.Scale = _motionScale;
        Application.Current!.RequestedThemeVariant = _variant;
        PageLayoutStore.DirOverride = _layoutDir;
        FirstRunWizard.SettingsRefresh = _settingsRefresh;
        CrashUi.ClipboardWriter = _clipboard;
        ImageDecode.Decoder = _decoder;
        C5bFiles.EmptyTaskStore();
    }
}

/// <summary>C5b 的公共小工具(只补 C3b/C3c 没给的那几件,泵与查找一律转发既有夹具)。</summary>
internal static class C5bFiles
{
    internal static void Flush(int rounds = 1)
    {
        C3bFiles.Flush(rounds);
    }

    internal static void Pump(Func<bool> until, int timeoutMs = 9000)
    {
        C3bFiles.Pump(until, timeoutMs);
    }

    internal static void PumpTask(Task task, int timeoutMs = 9000)
    {
        C3bFiles.PumpTask(task, timeoutMs);
    }

    internal static void Settle(int rounds = 8)
    {
        C3bFiles.Settle(rounds);
    }

    internal static IEnumerable<Control> Descendants(Control root)
    {
        return C3bFiles.Descendants(root);
    }

    internal static List<TextBlock> Texts(Control root)
    {
        return C3bFiles.Texts(root);
    }

    internal static bool HasText(Control root, string text)
    {
        return Texts(root).Any(t => string.Equals(t.Text, text, StringComparison.Ordinal));
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

    internal static T Find<T>(Control root, string what) where T : Control
    {
        T? hit = Descendants(root).OfType<T>().FirstOrDefault();
        Assert.True(hit != null, "页面里找不到 " + what + "(" + typeof(T).Name + ")");
        return hit!;
    }

    /// <summary>现在压在 <c>Dlg.Host</c> 上的浮层数(每层一个 wrap-Grid)。</summary>
    internal static int LayerCount()
    {
        return Dlg.Host?.Children.OfType<Grid>().Count() ?? 0;
    }

    /// <summary>某一层里的那张卡片(<c>wrap → holder → card</c>,与 <c>Dlg.Push</c> 的结构一一对应)。</summary>
    private static Control? CardAt(int index)
    {
        Grid? wrap = Dlg.Host?.Children.OfType<Grid>().ElementAtOrDefault(index);
        Grid? holder = wrap?.Children.OfType<Grid>().LastOrDefault();
        return holder?.Children.OfType<Control>().FirstOrDefault();
    }

    /// <summary>
    /// 泵到由 <c>Dlg.Panel</c> 推出的那张卡片出现并返回它(它**不是** <c>DialogCard</c>,
    /// 所以不能用 <c>C3bFiles.WaitDialog</c>)。按标题文字认,免得把 <c>Dlg.Busy</c> / <c>Dlg.Ask</c> 的卡片认成它。
    /// </summary>
    internal static Control PanelCard(string title)
    {
        Control? found = null;
        Pump(() =>
        {
            found = Enumerable.Range(0, LayerCount())
                .Select(CardAt)
                .LastOrDefault(c => c != null && HasText(c, title));
            return found != null;
        });
        return found!;
    }

    /// <summary>点对话框(Confirm / Ask)上某个文案的按钮,点完**不等**那个任务回来。</summary>
    internal static Control AnswerDialog(string buttonText)
    {
        C3bFiles.WaitDialog();
        Control card = CardAt(Math.Max(0, LayerCount() - 1))!;
        Click(ByText(card, buttonText));
        Settle(4);
        return card;
    }

    /// <summary>只把对话框等出来、**先不点任何按钮**(给"先填表再确认"的用例用)。</summary>
    internal static DialogCard AnswerDialogOpen()
    {
        return C3bFiles.WaitDialog();
    }

    /// <summary>把 <c>PageLayoutStore</c> 指到临时目录(落盘必须落在 <see cref="TestHome.Root"/> 之下)。</summary>
    internal static string LayoutDir()
    {
        string dir = System.IO.Path.Combine(TestHome.Root, "c5b-layout-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        PageLayoutStore.DirOverride = dir;
        PageLayoutStore.ResetCache();
        return dir;
    }

    /// <summary>
    /// 把任务列表清空:未完成的先按 <c>finished</c> 事件收掉,再摘掉所有已完成的行。
    /// 为什么不能直接断言总数是 0:任务列表是**进程级**的,别的包留下的行会让空态断言随机红(D-132 同类病)。
    /// </summary>
    internal static void EmptyTaskStore()
    {
        foreach (TaskRow row in TaskStore.Rows.ToList())
        {
            if (!row.Finished)
            {
                TaskStore.Handle(new BridgeEvent
                {
                    Event = "finished",
                    TaskId = row.Id,
                    Success = true,
                });
            }
        }

        TaskStore.ClearFinished();
    }

    /// <summary>造一条任务:<c>task_added</c> → 可选的一次 <c>progress</c>(与外壳收事件的路径完全相同)。</summary>
    internal static TaskRow AddTask(string id, string title, bool finished = false, bool success = true, double progress = 0.0, bool indeterminate = false)
    {
        TaskStore.Handle(new BridgeEvent
        {
            Event = "task_added",
            TaskId = id,
            Title = title,
        });
        TaskRow row = TaskStore.Get(id)!;
        if (indeterminate || progress > 0.0)
        {
            TaskStore.Handle(new BridgeEvent
            {
                Event = "progress",
                TaskId = id,
                Message = "下载中 1 MB/s",
                Total = (indeterminate ? 0 : 100),
                Current = (int)progress,
            });
        }

        if (finished)
        {
            TaskStore.Handle(new BridgeEvent
            {
                Event = "finished",
                TaskId = id,
                Success = success,
                Message = (success ? "" : "失败了"),
            });
        }

        return TaskStore.Get(id)!;
    }

    /// <summary>断言某个参数值(解码后);失败消息里带上方法名、键名与**实际值**。</summary>
    internal static void AssertParam(C3cFakeApi api, string method, string key, string expected, int index = 0)
    {
        using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(api.ParamsOf(method, index));
        string actual = "";
        if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && doc.RootElement.TryGetProperty(key, out System.Text.Json.JsonElement el))
        {
            actual = (el.ValueKind == System.Text.Json.JsonValueKind.String) ? (el.GetString() ?? "") : el.GetRawText();
        }

        Assert.True(string.Equals(actual, expected, StringComparison.Ordinal),
            method + "(" + index + ") 的 " + key + " 期望「" + expected + "」,实际「" + actual + "」");
    }

    /// <summary>第 <paramref name="index"/> 次调用的参数原文里有没有这段(中文会被转义,所以要找 <c>\uXXXX</c> 的那种用 <see cref="AssertParam"/>)。</summary>
    internal static void AssertJsonParam(C3cFakeApi api, string method, string key, object value, int index = 0)
    {
        string expected = "\"" + key + "\":" + ((value is string s) ? ("\"" + s + "\"") : (value is bool b ? (b ? "true" : "false") : value.ToString()));
        Assert.Contains(expected, api.ParamsOf(method, index));
    }

    /// <summary>页面挂进浮层宿主那个窗口(页面与 <c>Dlg</c> 的浮层于是同一棵树)。</summary>
    internal static T Mount<T>(C5bLayerHost harness, T page) where T : Control
    {
        harness.Host.Children.Add(page);
        Flush(2);
        return page;
    }

    /// <summary>页面/卡片里某个按钮的自动化名或文案(带图标的按钮 <c>Content</c> 是图文面板,按自动化名认)。</summary>
    internal static List<string> ButtonNames(Control root)
    {
        return Descendants(root).OfType<Button>()
            .Select(b => Avalonia.Automation.AutomationProperties.GetName(b) ?? "")
            .Where(s => s.Length > 0)
            .ToList();
    }
}

/// <summary>
/// C5b 的浮层宿主:把 <c>Dlg.Host</c> 指到一个挂在真窗口里的 Grid,用完还原(全局静态纪律)。
/// 与 C2a 的 <c>DlgHarness</c> / C4 的 <c>C4LayerHost</c> 是同一套做法。
/// </summary>
internal sealed class C5bLayerHost : IDisposable
{
    private readonly Grid? _previousHost;

    private readonly Control? _previousBlur;

    private readonly UiHost _window;

    private C5bLayerHost(UiHost window, Grid host, Grid? previousHost, Control? previousBlur)
    {
        _window = window;
        Host = host;
        _previousHost = previousHost;
        _previousBlur = previousBlur;
    }

    internal Grid Host { get; }

    /// <summary>承载它的真窗口(取页面里某个控件的渲染尺寸用)。</summary>
    internal Window Window => _window.Window;

    internal static C5bLayerHost Show()
    {
        Grid host = new Grid();
        UiHost window = UiHost.Show(host);
        Grid? previousHost = Dlg.Host;
        Control? previousBlur = Dlg.BlurTarget;
        Dlg.Host = host;
        Dlg.BlurTarget = null;
        return new C5bLayerHost(window, host, previousHost, previousBlur);
    }

    public void Dispose()
    {
        Dlg.Host = _previousHost;
        Dlg.BlurTarget = _previousBlur;
        _window.Dispose();
        UiHost.Flush();
    }
}
