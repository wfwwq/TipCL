// M4-H P5:崩溃界面「交给 AI 修复」的对话体(移植 Windows 版 Pages/AiFixWindow.cs,810 行)。
//
// 参考版是一个独立 Window(WPF),Linux 侧等价物是 `Dlg.OpenPanel` 的非模态面板:同一个 AiAgent +
// ToolRunner,同一个开场上下文(Core 的 AiFixChat.BuildCrashContext),同一套卡片/步骤行组件。
// 与 AiPage 的差别:固定「修复本次崩溃」的会话目标、只带诊断相关工具、不写历史。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TipCL.App.Common;
using TipCL.App.Common.Ai;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Ai;

/// <summary>「交给 AI 修复」面板。</summary>
internal sealed class AiFixView : Border
{
    private readonly PyDict _report;
    private readonly IApi _api;
    private readonly StackPanel _messages = UiClass.V(10);
    private readonly ScrollViewer _scroll;
    private readonly ChatInput _input;
    private readonly StackPanel _dock = UiClass.V(6);
    private readonly TextBlock _status = UiClass.Small("");
    private readonly ToolRunner _runner;
    private readonly List<object> _history = new();
    private readonly List<Control> _blocks = new();
    private readonly Dictionary<string, ToolLine> _toolLines = new();
    private readonly Dictionary<string, TaskCard> _taskCards = new();
    private readonly Dictionary<string, ToolLine?> _taskLines = new();

    private StepGroup? _group;
    private AiBubble? _pending;
    private ConfirmCard? _pendingConfirm;
    private AskCard? _pendingAsk;
    private TaskCompletionSource<(bool ok, bool always)>? _confirmWaiter;
    private TaskCompletionSource<object?>? _askWaiter;
    private CancellationTokenSource? _run;
    private string _think = "";

    public AiFixView(IApi api, PyDict report)
    {
        _api = api;
        _report = report ?? new PyDict();
        _runner = new ToolRunner(new Backend(api))
        {
            Instance = Py.S(Py.Or(_report.Get("instance"), TipCL.Core.Launcher.Config.CONFIG.Get("default_instance", "default"))),
            Confirm = ConfirmAsync,
            AskUser = AskUserAsync,
            PlanChanged = plan => Dispatcher.UIThread.Post(() => ShowPlan(plan)),
            TaskStarted = (taskId, label, tool) => Dispatcher.UIThread.Post(() => AddTaskCard(taskId, label, tool, null)),
            Log = line => Dispatcher.UIThread.Post(() => _status.Text = AiUiText.Trim(line, 48)),
        };

        var head = UiClass.Section("AI 修复", BuildHeadSub(),
            U.Btn("重新开始", BtnKind.Chip, (_, _) => Restart(), "Refresh"));

        _scroll = new ScrollViewer
        {
            Content = _messages,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _input = new ChatInput(Send, Stop);
        _dock.IsVisible = false;

        var body = UiClass.V(8, head, _scroll, UiClass.V(6, _dock, _input, _status));
        body.Height = 520;
        Child = body;
        _scroll.Height = 380;

        Loaded += (_, _) => KickOff();
    }

    private sealed class Backend : ToolRunner.IBackend
    {
        private readonly IApi _api;
        public Backend(IApi api) => _api = api;
        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
            => _api.CallAsync<T>(method, args, ct);
    }

    /// <summary>执行器本身(内部只读,用途同 AiPage.Runner:给确认链路的回归测试驱动)。</summary>
    internal ToolRunner Runner => _runner;

    /// <summary>
    /// 修复会话的系统消息:身份 + 本次约定 + 运行时上下文 + 权限档位那句话。
    /// 那句话的位置照参考版(Agent.cs:363-372;参考版两条路径都经过 Agent._system_messages):
    /// 运行时上下文**之后**、长期记忆之前 —— 这条会话不注入长期记忆,所以它在末尾。
    /// 单独抽出来是为了让「位置」这一条可测(AiFixView 不注入 BackendApi,只能靠这个方法验)。
    /// </summary>
    internal static string FixSystemMessage(PyDict report, string instance, string version)
        => AiFixChat.FixSystemPrompt(report) + "\n\n"
            + AiAgent.RuntimeContext(instance, version) + "\n\n" + ToolRunner.PermissionNote();

    private string BuildHeadSub()
    {
        string instance = Py.S(_report.Get("instance"));
        string version = Py.S(_report.Get("version"));
        string headline = Py.S(Py.Or(_report.Get("headline"), _report.Get("title")));
        string who = instance.Length > 0 ? instance : "默认实例";
        if (version.Length > 0)
        {
            who += " / " + version;
        }
        string tail = headline.Length > 0 ? " · " + AiUiText.Trim(headline, 24) : "";
        return "让助手读日志、查冲突并把这次启动问题修掉(" + who + ")" + tail;
    }

    /// <summary>开场:直接把崩溃上下文发出去(照 Windows 版 AiFixWindow 的 KickOffAsync)。</summary>
    private void KickOff()
    {
        if (_history.Count > 0)
        {
            return;
        }
        _history.Add(PyDict.Of("role", "system", "content",
            FixSystemMessage(_report, _runner.Instance, Py.S(_report.Get("version")))));
        Add(Append("assistant", "我已经看到这次的崩溃报告。先读一下日志和模组,再告诉你原因。"));
        _ = RunAgentAsync(AiFixChat.OpeningMessage(_report, InterfaceLabel()));
    }

    private static string InterfaceLabel()
    {
        try
        {
            PyDict endpoint = AiService.ResolveEndpoint();
            return Py.S(endpoint.Get("mode")) == "custom" ? "自定义端点" : "内置网关";
        }
        catch (Exception)
        {
            return "";
        }
    }

    private void Restart()
    {
        Stop();
        _history.Clear();
        _toolLines.Clear();
        _taskCards.Clear();
        foreach (Control block in _blocks.ToList())
        {
            _messages.Children.Remove(block);
        }
        _blocks.Clear();
        KickOff();
    }

    private void Send()
    {
        if (_run != null)
        {
            return;
        }
        string text = _input.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }
        _input.Text = "";
        Append("user", text);
        _ = RunAgentAsync(PyDict.Of("role", "user", "content", text));
    }

    private async Task RunAgentAsync(PyDict userMessage)
    {
        _history.Add(userMessage);
        var open = Append("assistant", "");
        _pending = open;
        _run = new CancellationTokenSource();
        _input.Send.IsVisible = false;
        _input.Stop.IsVisible = true;
        try
        {
            var agent = new AiAgent(_runner, ev => Dispatcher.UIThread.Post(() => OnAgentEvent(ev)));
            await Task.Run(() => agent.RunAsync(_history, _runner.Instance, _run!.Token));
        }
        catch (OperationCanceledException)
        {
            FinishRun("已停止");
        }
        catch (Exception ex)
        {
            string message = ex is PyException ? ex.Message : ex.ToString();
            Dispatcher.UIThread.Post(() =>
            {
                Append("assistant", "修复会话失败:" + message);
                FinishRun("");
            });
        }
    }

    private void OnAgentEvent(AiAgentEvent ev)
    {
        switch (ev.Kind)
        {
            case "delta":
            case "think":
                if (_pending == null)
                {
                    _pending = Append("assistant", "");
                }
                if (ev.Kind == "think")
                {
                    _think += ev.Text;
                    _pending.SetThinking(_think);
                }
                else
                {
                    _pending.AppendAnswer(ev.Text);
                    ScrollToEnd();
                }
                break;
            case "status":
                _status.Text = ev.Text;
                break;
            case "tool":
                if (ev.Call != null && Tools.IsAskTool(ev.Call.name))
                {
                    CloseRound();
                    break;
                }
                CloseRound();
                Line(ev.Call!.name, "准备:" + ev.Text, ToolState.Prepare);
                _status.Text = "正在执行操作…";
                break;
            case "tool_done":
                if (ev.Call != null && Tools.IsAskTool(ev.Call.name))
                {
                    break;
                }
                CloseRound();
                if (ev.Call != null)
                {
                    Line(ev.Call.name, "完成:" + ev.Text, ToolState.Done);
                }
                _status.Text = "操作完成";
                break;
            case "error":
                if (ev.Text != "已停止")
                {
                    Append("assistant", "修复会话失败:" + ev.Error);
                    ToastHost.Show("AI 修复失败", ev.Error, ToastKind.Warning);
                }
                FinishRun(ev.Text == "已停止" ? "已停止" : "");
                break;
            case "done":
                FinishRun("");
                break;
        }
    }

    private void FinishRun(string note)
    {
        _run?.Dispose();
        _run = null;
        _input.Send.IsVisible = true;
        _input.Stop.IsVisible = false;
        _input.Hint.Text = note;
        _status.Text = "";
        if (_pending != null && _pending.Markdown().Trim().Length == 0)
        {
            _messages.Children.Remove(_pending);
            _blocks.Remove(_pending);
        }
        _pending = null;
        foreach (StepGroup group in _messages.Children.OfType<StepGroup>())
        {
            group.Complete();
        }
        _group = null;
        ClearDock();
    }

    private void Stop()
    {
        _run?.Cancel();
        _pendingConfirm?.Reject();
        _askWaiter?.TrySetResult(null);
    }

    private AiBubble Append(string role, string text)
    {
        var bubble = new AiBubble(role, text);
        _blocks.Add(bubble);
        _messages.Children.Add(bubble);
        ScrollToEnd();
        return bubble;
    }

    private void Add(Control control)
    {
        _blocks.Add(control);
        _messages.Children.Add(control);
    }

    private void ScrollToEnd() => Dispatcher.UIThread.Post(() => _scroll.Offset = new Vector(0, double.MaxValue), DispatcherPriority.Background);

    private ToolLine Line(string name, string text, ToolState state)
    {
        if (_toolLines.TryGetValue(name, out ToolLine? existing))
        {
            existing.SetText(text);
            existing.SetState(state);
            _group?.Refresh();
            return existing;
        }
        var line = new ToolLine(text, name);
        line.SetState(state);
        _toolLines[name] = line;
        if (_group == null || !_messages.Children.Contains(_group))
        {
            _group = new StepGroup();
            _messages.Children.Add(_group);
            _blocks.Add(_group);
        }
        _group.AddLine(line);
        ScrollToEnd();
        return line;
    }

    private void CloseRound()
    {
        _group = null;
        _pending = null;
    }

    private void ShowPlan(List<(string Title, string Status)> items)
    {
        PlanCard? old = _messages.Children.OfType<PlanCard>().LastOrDefault();
        if (old != null)
        {
            _messages.Children.Remove(old);
            _blocks.Remove(old);
        }
        var card = new PlanCard(items);
        _messages.Children.Add(card);
        _blocks.Add(card);
        ScrollToEnd();
    }

    private void AddTaskCard(string taskId, string label, string tool, ToolLine? line)
    {
        if (taskId.Length == 0 || _taskCards.ContainsKey(taskId))
        {
            return;
        }
        var card = new TaskCard(taskId, label, tool, _runner.Instance)
        {
            OpenFolder = () => _ = _api.CallAsync<string>("open_instance_folder", new { name = _runner.Instance }),
            Retry = _ => _input.Text = "刚才那一步失败了,再试一次",
        };
        _taskCards[taskId] = card;
        _taskLines[taskId] = line;
        _messages.Children.Add(card);
        _blocks.Add(card);
        ScrollToEnd();
    }

    /// <summary>任务事件(由宿主把外壳事件流转进来)。</summary>
    public void OnEvent(ApiEvent ev)
    {
        if (ev.TaskId.Length == 0 || !_taskCards.TryGetValue(ev.TaskId, out TaskCard? card))
        {
            return;
        }
        if (ev.Event == "progress")
        {
            card.SetProgress(ev.Current, ev.Total, ev.Message);
            if (_taskLines.TryGetValue(ev.TaskId, out ToolLine? line) && line != null)
            {
                line.SetProgress(ev.Current, ev.Total, ev.Message);
            }
        }
        else if (ev.Event == "finished")
        {
            card.Finish(ev.Success, ev.Message);
            if (_taskLines.TryGetValue(ev.TaskId, out ToolLine? line) && line != null)
            {
                line.Finish(ev.Success, ev.Message);
            }
        }
    }

    /// <summary>
    /// 权限确认(与 AiPage 同一套语义,参数含义见 <c>AiPage.ConfirmAsync</c>):
    /// <paramref name="label"/> 是「做什么」、<paramref name="reason"/> 是权限裁决给出的「为什么需要你确认」,
    /// <paramref name="preview"/> 是 Core 算好的「改前改后」预览。三者各就各位。
    /// </summary>
    private async Task<(bool ok, bool always)> ConfirmAsync(ToolCall call, string label, string reason, PyDict? preview)
    {
        var waiter = new TaskCompletionSource<(bool, bool)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _confirmWaiter = waiter;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _dock.Children.Clear();
            ConfirmCard? card = null;
            card = new ConfirmCard(call.name, label, reason, Preview.Flatten(preview), ConfirmCard.AllowAlways(call.name),
                (ok, always, _) =>
                {
                    _pendingConfirm = null;
                    var done = card;
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (done != null)
                        {
                            _dock.Children.Remove(done);
                        }
                        _dock.IsVisible = _dock.Children.Count > 0;
                    });
                    waiter.TrySetResult((ok, always));
                });
            _pendingConfirm = card;
            _dock.Children.Add(card);
            _dock.IsVisible = true;
        });
        return await waiter.Task.ConfigureAwait(false);
    }

    private async Task<object?> AskUserAsync(PyDict normalized)
    {
        List<object> questions = Py.ListOr(normalized.Get("questions"));
        string title = Py.S(normalized.Get("title"));
        var waiter = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _askWaiter = waiter;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _dock.Children.Clear();
            AskCard? card = null;
            card = new AskCard(title, questions, answer =>
            {
                _pendingAsk = null;
                var done = card;
                Dispatcher.UIThread.Post(() =>
                {
                    if (done != null)
                    {
                        _dock.Children.Remove(done);
                    }
                    _dock.IsVisible = _dock.Children.Count > 0;
                });
                waiter.TrySetResult(answer);
            });
            _pendingAsk = card;
            _dock.Children.Add(card);
            _dock.IsVisible = true;
        });
        return await waiter.Task.ConfigureAwait(false);
    }

    private void ClearDock()
    {
        _pendingConfirm?.Reject();
        _pendingConfirm = null;
        _pendingAsk = null;
        _confirmWaiter?.TrySetResult((false, false));
        _askWaiter?.TrySetResult(null);
        _confirmWaiter = null;
        _askWaiter = null;
        _dock.Children.Clear();
        _dock.IsVisible = false;
    }
}
