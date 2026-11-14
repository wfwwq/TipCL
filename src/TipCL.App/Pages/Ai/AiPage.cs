// 来源: Windows 版参考仓库 src/TipCL/Pages/AiPage.cs(2740 行,M4-H P5 移植)。
// AI 对话页:消息列表(气泡 / 步骤组 / 计划卡 / 任务卡)+ 底部确认与选择题区 + 输入区 +
// 带工具的会话循环(Core 的 AiAgent)。
// 与 Windows 的差别只在与宿主相关的部分:Avalonia 没有 Dlg.Layer,确认卡与选择题卡改成
// 贴在输入区上方的**非模态停靠区**(见 DECISIONS D-24),行为(等待用户决定后才继续)一致。
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
using Avalonia.VisualTree;
using Avalonia.Threading;
using TipCL.App.Common;
using TipCL.App.Common.Ai;
using TipCL.App.Pages.Layout;
using TipCL.App.Shell;
using TipCL.App.Ui;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using UiClass = TipCL.App.Ui.Ui;
using AppAiRisk = TipCL.Core.Launcher.Ai.AiRisk;

namespace TipCL.App.Pages.Ai;

/// <summary>AI 助手页(对话 + 工具调用)。</summary>
public sealed class AiPage : PageBase
{
    private static readonly string[] Suggestions =
    {
        "帮我看一下这次崩溃是什么原因",
        "Fabric 和 Forge 有什么区别,我该选哪个",
        "这个版本装了什么模组,有冲突吗",
        "内存设多少合适",
    };

    private readonly StackPanel _messages = UiClass.V(0);
    private readonly ScrollViewer _scroll;
    private readonly ChatInput _input;
    private readonly TextBlock _model = UiClass.Small("");
    private readonly TextBlock _status = UiClass.Small("");
    private readonly TextBlock _permLabel = UiClass.Small("");
    private readonly StackPanel _empty;
    private readonly StackPanel _dock = UiClass.V(6);
    private readonly List<object> _history = new();
    private readonly List<Control> _bubbles = new();
    private readonly Dictionary<string, ToolLine> _toolLines = new();
    private readonly Dictionary<string, TaskCard> _taskCards = new();
    private readonly Dictionary<string, ToolLine?> _taskLines = new();
    private readonly ToolRunner _runner;
    private readonly TextBlock _imgChip = UiClass.Small("");
    private readonly SlashMenu _slash = new();

    /// <summary>上下文占用环的宿主(M4-H R2:读 Usage + Tokens 的比例)。</summary>
    private readonly Border _ringHost = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly TextBlock _usageLabel = UiClass.Small("");
    private List<SlashCommand> _slashItems = new();
    private int _slashSel = -1;
    private string _chatId = "";
    private bool _chatLoaded;
    private AiImageInput _images = null!;

    private StepGroup? _group;
    private AiBubble? _pending;
    private PlanCard? _planCard;
    private ConfirmCard? _pendingConfirm;
    private AskCard? _pendingAsk;
    private TaskCompletionSource<(bool ok, bool always)>? _confirmWaiter;
    private TaskCompletionSource<object?>? _askWaiter;
    private CancellationTokenSource? _run;
    private string _think = "";
    private string _text = "";

    public AiPage() : base("ai", "AI 助手")
    {
        _runner = new ToolRunner(new ApiBackend())
        {
            Instance = Py.S(TipCL.Core.Launcher.Config.CONFIG.Get("default_instance", "default")),
            Confirm = ConfirmAsync,
            AskUser = AskUserAsync,
            PlanChanged = plan => Dispatcher.UIThread.Post(() => ShowPlan(plan)),
            TaskStarted = (taskId, label, tool) => Dispatcher.UIThread.Post(() => AddTaskCard(taskId, label, tool, null)),
            Log = line => Dispatcher.UIThread.Post(() => _status.Text = AiUiText.Trim(line, 48)),
        };

        var permBtn = U.Btn("权限", BtnKind.Chip, (_, _) => OpenPermissionPanel(), "Shield");
        var memoryBtn = U.Btn("记忆", BtnKind.Chip, (_, _) => OpenMemoryPanel(), "Star");
        var clear = U.Btn("清空", BtnKind.Chip, (_, _) => Clear(), "Broom");
        var settings = U.Btn("设置", BtnKind.Chip, (_, _) =>
        {
            if (this.GetVisualRoot() is MainWindow w) w.Navigate("settings");
        }, "Gear");
        _usageLabel.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(_usageLabel, "上下文占用:按本会话每轮平均输入估算");
        _ringHost.Child = AiIcons.Ring(0);
        var head = UiClass.Section("AI 助手", "问启动、模组、崩溃都行;输入 / 看命令",
            UiClass.H(8, _model.VCenter(), _permLabel.VCenter(), _ringHost, _usageLabel.VCenter(), memoryBtn, permBtn, settings, clear));
        _scroll = new ScrollViewer
        {
            Content = _messages,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _empty = BuildEmpty();
        _messages.Children.Add(_empty);

        _input = new ChatInput(Send, Stop);
        _input.Paste = OnInputKey;
        _input.TextChanged = RefreshSlash;
        _input.PreKey = OnInputPreKey;
        _slash.Picked = cmd => RunSlash(cmd);
        _images = new AiImageInput();
        _images.Changed += SyncImageChip;
        _images.HookDrop(_input);
        var imgBtn = UiClass.IconBtn("Image", "附一张图(也可以直接粘贴或拖进来)", (_, _) => _ = _images.PickAsync(this.GetVisualRoot() as Window), 14);
        _imgChip.IsVisible = false;
        _imgChip.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        _imgChip.PointerReleased += (_, _) => _images.Clear();
        var imgRow = UiClass.H(8, imgBtn, _imgChip.VCenter());
        imgRow.HorizontalAlignment = HorizontalAlignment.Left;

        _dock.IsVisible = false;
        var bottom = UiClass.V(6, _dock, _slash, imgRow, _input);

        var root = UiClass.G("Auto,*,Auto");
        root.Add(head, 0, 0);
        root.Add(_scroll, 1, 0);
        root.Add(bottom, 2, 0);
        root.Margin = new Thickness(22, 18, 22, 16);
        Content = root;

        RefreshModelLabel();
        RefreshPermissionLabel();
        RefreshInputState();
        RefreshUsage(null, _history);
    }

    /// <summary>把 IApi 收成 ToolRunner 要的最小门面(只用到 CallAsync)。</summary>
    private sealed class ApiBackend : ToolRunner.IBackend
    {
        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
            => InProcessApi.Instance.CallAsync<T>(method, args, ct);
    }

    /// <summary>
    /// 执行器本身(内部只读)。给回归测试用:确认链路的断链只有驱动**页面自己的**确认回调
    /// 才能查出来(预览算得对不对、卡片收没收到,是两个独立的事实)。
    /// </summary>
    internal ToolRunner Runner => _runner;

    // ───────────────────────────── 生命周期 ─────────────────────────────

    protected override void OnActivate()
    {
        RefreshModelLabel();
        RefreshPermissionLabel();
        // 回到 AI 页时把存档里的对话读回来(换页/重启后对话还在),并把占用环刷新一下
        LoadChatHistory();
        RefreshUsage(Usage.Get(_chatId.Length > 0 ? _chatId : "active"), _history);
    }

    /// <summary>端点探测:解析失败 = 还没配置好,页面显示引导而不是点了没反应。</summary>
    private static PyDict? TryEndpoint()
    {
        try { return AiService.ResolveEndpoint(); }
        catch (Exception) { return null; }
    }

    private void RefreshModelLabel()
    {
        var endpoint = TryEndpoint();
        if (endpoint == null)
        {
            _model.Text = "未配置";
            return;
        }
        string label = Py.S(endpoint.Get("mode")) == "custom" ? "自定义端点" : "内置网关";
        string model = Py.S(endpoint.Get("model"));
        _model.Text = model.Length == 0 ? label : label + " · " + model;
    }

    private void RefreshPermissionLabel()
    {
        string mode = Py.S(TipCL.Core.Launcher.Config.CONFIG.Get("ai_permission_mode", "default"));
        int index = Array.IndexOf(AiPermissionPanel.Modes, mode);
        _permLabel.Text = "权限:" + AiPermissionPanel.ModeLabels[index >= 0 ? index : 0];
    }

    private static bool Configured() => TryEndpoint() != null;

    private void RefreshInputState()
    {
        bool configured = Configured();
        _input.Send.IsEnabled = configured || _history.Count > 0;
        _input.Hint.Text = configured ? "" : "还没配置 AI 接入方式:到「设置 → 通用」填好地址与密钥后即可对话";
    }

    private StackPanel BuildEmpty()
    {
        var col = UiClass.V(10);
        col.Children.Add(UiClass.Empty("Lightning", "还没有对话", "下面几句最常问,点一下直接发"));
        var wrap = new WrapPanel();
        foreach (string s in Suggestions)
        {
            string text = s;
            var chip = U.Btn(text, BtnKind.Chip);
            chip.Margin = new Thickness(0, 0, 8, 8);
            chip.Click += (_, _) =>
            {
                _input.Text = text;
                Send();
            };
            wrap.Children.Add(chip);
        }
        col.Children.Add(wrap);
        return col;
    }

    private void OpenPermissionPanel()
    {
        var panel = new AiPermissionPanel(InProcessApi.Instance, _runner.Instance);
        var handle = Dlg.OpenPanel("权限管理", panel, 860);
        handle.Closed.ContinueWith(_ => Dispatcher.UIThread.Post(RefreshPermissionLabel), TaskScheduler.Default);
        _ = panel.RefreshAsync();
    }

    /// <summary>会话记忆面板(与设置页「AI 助手 → 记忆」卡同一份文件)。</summary>
    private void OpenMemoryPanel() => Dlg.OpenPanel("助手记忆", new AiMemoryPanel(), 760);

    // ───────────────────────────── 斜杠命令 ─────────────────────────────

    /// <summary>输入变化时刷新候选(只在以 / 开头且还没输空格时出现)。</summary>
    private void RefreshSlash()
    {
        _slashItems = AiChatOps.MatchSlash(_input.Text);
        _slashSel = _slashItems.Count > 0 ? 0 : -1;
        if (_slashItems.Count == 0)
        {
            _slash.Hide();
            return;
        }
        _slash.Show(_slashItems, _slashSel);
    }

    /// <summary>候选条打开时:上下键移动、Tab 选中、Esc 收起。</summary>
    private bool OnInputPreKey(Avalonia.Input.KeyEventArgs e)
    {
        if (_slashItems.Count == 0)
        {
            return false;
        }
        switch (e.Key)
        {
            case Avalonia.Input.Key.Down:
                _slashSel = AiChatOps.MoveSelection(_slashSel, 1, _slashItems.Count);
                _slash.Show(_slashItems, _slashSel);
                return true;
            case Avalonia.Input.Key.Up:
                _slashSel = AiChatOps.MoveSelection(_slashSel, -1, _slashItems.Count);
                _slash.Show(_slashItems, _slashSel);
                return true;
            case Avalonia.Input.Key.Tab:
                if (_slashSel >= 0)
                {
                    RunSlash(_slashItems[_slashSel]);
                }
                return true;
            case Avalonia.Input.Key.Escape:
                _slashItems = new List<SlashCommand>();
                _slashSel = -1;
                _slash.Hide();
                return true;
            default:
                return false;
        }
    }

    /// <summary>执行一条斜杠命令:Local 的在本机做,Prompt 的当作一句用户输入发出去。</summary>
    private void RunSlash(SlashCommand cmd)
    {
        _slashItems = new List<SlashCommand>();
        _slashSel = -1;
        _slash.Hide();
        if (cmd.Local.Length > 0)
        {
            _input.Text = "";
            switch (cmd.Local)
            {
                case "new_chat":
                    NewChat();
                    return;
                case "clear":
                    Clear();
                    return;
                case "rewind":
                    _ = RewindAsync();
                    return;
                default:
                    ToastHost.Show("这条命令还没接上", cmd.Local, ToastKind.Warning);
                    return;
            }
        }
        _input.Text = cmd.Local.Length > 0 ? "" : cmd.Prompt;
        Send();
    }

    // ───────────────────────────── 会话存档(Store) ─────────────────────────────

    /// <summary>取当前对话 id(第一次用到时才读存档,页面打开本身不落盘)。</summary>
    private string EnsureChatId()
    {
        if (_chatId.Length > 0)
        {
            return _chatId;
        }
        PyDict data = Store.Load();
        string active = Py.S(data.Get("active_id"));
        if (active.Length == 0)
        {
            active = Py.S(Store.NewChat(data)["id"]);
        }
        _chatId = active;
        return _chatId;
    }

    /// <summary>第一次发送前把存档里的对话读回界面(换页/重启后对话还在)。</summary>
    private void LoadChatHistory()
    {
        if (_chatLoaded)
        {
            return;
        }
        _chatLoaded = true;
        string chatId = EnsureChatId();
        PyDict? chat = Store.GetChat(Store.Load(), chatId);
        if (chat == null)
        {
            return;
        }
        List<object> messages = Store.ApiMessages(Py.ListOr(chat.Get("messages")));
        if (messages.Count == 0)
        {
            return;
        }
        _history.Clear();
        _history.Add(AiAgent.SystemMessage(_runner.Instance));
        _history.AddRange(messages);
        RenderHistory();
    }

    /// <summary>把当前对话写回存档(标题按第一句用户消息自动起)。</summary>
    private void SaveChatHistory()
    {
        if (_history.Count == 0)
        {
            return;
        }
        try
        {
            PyDict data = Store.Load();
            Store.UpsertMessages(data, EnsureChatId(), new List<object>(_history));
        }
        catch (Exception ex)
        {
            ToastHost.Show("对话没存下来", Py.ExcStr(ex), ToastKind.Warning);
        }
    }

    /// <summary>用存档里的消息重画一遍气泡(清空/回退后调)。</summary>
    private void RenderHistory()
    {
        foreach (Control b in _bubbles.ToList())
        {
            _messages.Children.Remove(b);
        }
        _bubbles.Clear();
        _toolLines.Clear();
        _taskCards.Clear();
        _taskLines.Clear();
        _planCard = null;
        _group = null;
        _pending = null;
        foreach (object raw in _history)
        {
            if (raw is not PyDict m || !Py.Eq(m.Get("role"), "user") && !Py.Eq(m.Get("role"), "assistant"))
            {
                continue;
            }
            // 带图的消息在存档里是 multimodal 数组:重画时只取文字部分(图片已经发过一次,不重复贴)
            string text = Py.S(Images.StripImages(m.Get("content")));
            if (text.Trim().Length == 0)
            {
                continue;
            }
            Append(Py.S(m.Get("role")), text);
        }
        _empty.IsVisible = _bubbles.Count == 0;
        RefreshInputState();
    }

    /// <summary>新对话:把当前对话留在存档里,再开一个空对话。</summary>
    private void NewChat()
    {
        Stop();
        SaveChatHistory();
        PyDict data = Store.Load();
        PyDict chat = Store.NewChat(data);
        _chatId = Py.S(chat["id"]);
        _chatLoaded = true;
        _history.Clear();
        RenderHistory();
        _messages.Children.Add(_empty);
        _empty.IsVisible = true;
        Usage.Reset(_chatId);
        RefreshUsage(Usage.Get(_chatId), _history);
        ToastHost.Show("已开新对话", "上一段对话还在存档里", ToastKind.Success);
    }

    /// <summary>「撤回」:退回到最后一句用户消息之前,并把这一轮改过的文件按检查点还原。</summary>
    private async Task RewindAsync()
    {
        if (_run != null)
        {
            ToastHost.Show("正在生成中", "先停止再撤回", ToastKind.Warning);
            return;
        }
        SaveChatHistory();
        PyDict result;
        try
        {
            result = Rewind.RewindLastRound(EnsureChatId());
        }
        catch (Exception ex)
        {
            ToastHost.Show("撤回失败", Py.ExcStr(ex), ToastKind.Warning);
            return;
        }
        if (!Py.Truthy(result.Get("ok")) && !Py.Truthy(result.Get("truncated")))
        {
            ToastHost.Show("没有可撤回的一轮", "这个对话还没有你发的消息", ToastKind.Warning);
            return;
        }
        if (!Py.Truthy(result.Get("truncated")))
        {
            ToastHost.Show("没有可撤回的一轮", Py.S(result.Get("reason")), ToastKind.Warning);
            return;
        }
        _chatLoaded = true;
        PyDict? chat = Store.GetChat(Store.Load(), EnsureChatId());
        _history.Clear();
        _history.Add(AiAgent.SystemMessage(_runner.Instance));
        if (chat != null)
        {
            _history.AddRange(Store.ApiMessages(Py.ListOr(chat.Get("messages"))));
        }
        RenderHistory();
        long files = Py.Int(Py.Or(result.Get("restored_files"), 0L));
        string body = files > 0
            ? "已还原 " + files + " 个改过的文件"
            : (Py.Truthy(result.Get("rollbackable")) ? "这一轮没有改过文件" : "这一轮没有磁盘改动");
        ToastHost.Show("已撤回上一轮", body, ToastKind.Success);
        await Task.CompletedTask;
    }

    // ───────────────────────────── 上下文占用环 ─────────────────────────────

    /// <summary>按本会话的用量与上下文窗口画占用环(照 Windows 版 AiPage.ShowUsage)。</summary>
    private void RefreshUsage(PyDict? usage, List<object> messages)
    {
        long window = Compact.AutoConfigFromSettings(TipCL.Core.Launcher.Config.CONFIG.data).context_window;
        long input = 0;
        long output = 0;
        long requests = 0;
        if (usage != null)
        {
            input = Py.Int(usage.Get("input")) + Py.Int(usage.Get("input_estimated"));
            output = Py.Int(usage.Get("output"));
            requests = Py.Int(usage.Get("requests"));
        }
        if (input == 0 && output == 0)
        {
            // 还没跑过请求:用本地估算给个初值，用户能看到环随上下文生长
            input = Tokens.EstimateMessages(messages);
            requests = input > 0 ? 1 : 0;
        }
        double ratio = AiUiText.ContextRatio(input, requests, window);
        _ringHost.Child = AiIcons.Ring(ratio);
        _usageLabel.Text = $"{ratio * 100.0:0}% · {AiUiText.Tokens(input)}";
        string source = usage != null && Py.S(usage.Get("source")) == "provider_usage" ? "" : "(估算)";
        ToolTip.SetTip(_usageLabel,
            $"本会话:输入 {AiUiText.Tokens(input)} / 输出 {AiUiText.Tokens(output)} tokens{source}\n" +
            $"上下文窗口 {AiUiText.Tokens(window)},圆环为每轮平均占用的估算");
        ToolTip.SetTip(_ringHost, ToolTip.GetTip(_usageLabel) as string);
    }

    /// <summary>输入框按键:Ctrl+V 先看图片(有图就吞掉这次粘贴)。</summary>
    private void OnInputKey(Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.V || !e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
        {
            return;
        }
        var top = TopLevel.GetTopLevel(this);
        if (top == null)
        {
            return;
        }
        _ = HandlePasteAsync(top, e);
    }

    private async Task HandlePasteAsync(TopLevel top, Avalonia.Input.KeyEventArgs e)
    {
        if (await _images.HandlePasteAsync(top))
        {
            e.Handled = true;
        }
    }

    private void SyncImageChip()
    {
        _imgChip.Text = _images.ChipText;
        _imgChip.IsVisible = _images.Count > 0;
        ToolTip.SetTip(_imgChip, _images.Count > 0 ? string.Join("\n", _images.Paths) + "\n点击清空" : null);
    }

    // ───────────────────────────── 发送 / 会话循环 ─────────────────────────────

    private void Send()
    {
        if (_run != null)
        {
            return;
        }
        string text = _input.Text.Trim();
        if (text.Length == 0)
        {
            ToastHost.Show("先写点内容", "", ToastKind.Warning);
            return;
        }
        // 斜杠命令:整句正好是一条命令就按命令走;只写了半条(还开着候选)就按选中的那条走
        SlashCommand? exact = AiChatOps.ExactSlash(text);
        if (exact != null)
        {
            RunSlash(exact);
            return;
        }
        if (text.StartsWith("/", StringComparison.Ordinal) && _slashItems.Count > 0 && _slashSel >= 0)
        {
            RunSlash(_slashItems[_slashSel]);
            return;
        }
        if (!Configured())
        {
            ToastHost.Show("还没配置 AI", "到「设置 → 通用」填好接入方式与密钥", ToastKind.Warning);
            return;
        }
        LoadChatHistory();
        _input.Text = "";
        _slash.Hide();
        _slashItems = new List<SlashCommand>();
        _empty.IsVisible = false;
        Append("user", text);

        // 附件:带图时 content 走 OpenAI 的 multimodal 数组(Core 的 Images.BuildUserContent)
        object content = text;
        if (_images.Count > 0)
        {
            List<object> imageBlocks = _images.Paths.Select(Images.EncodeFile).Where(x => x.Length > 0).Cast<object>().ToList();
            if (imageBlocks.Count > 0)
            {
                if (!AiImageInput.VisionSupported(out string reason))
                {
                    ToastHost.Show("当前模型可能看不了图", "图片会被省略。" + reason, ToastKind.Warning);
                }
                else
                {
                    content = Images.BuildUserContent(text, imageBlocks);
                }
            }
            _images.Clear();
        }
        _history.Add(PyDict.Of("role", "user", "content", content));
        if (_history.Count == 1)
        {
            _history.Insert(0, AiAgent.SystemMessage(_runner.Instance));
        }

        _run = new CancellationTokenSource();
        _input.Send.IsVisible = false;
        _input.Stop.IsVisible = true;
        _input.Hint.Text = "正在生成...";
        _ = RunAgentAsync();
    }

    private async Task RunAgentAsync()
    {
        var agent = new AiAgent(_runner, ev => Dispatcher.UIThread.Post(() => OnAgentEvent(ev)))
        {
            ChatId = EnsureChatId(),
        };
        try
        {
            await Task.Run(() => agent.RunAsync(_history, _runner.Instance, _run!.Token));
        }
        catch (OperationCanceledException)
        {
            Dispatcher.UIThread.Post(() => FinishRun("已停止"));
        }
        catch (Exception ex)
        {
            string message = ex is PyException ? ex.Message : ex.ToString();
            Dispatcher.UIThread.Post(() =>
            {
                Append("assistant", "对话失败:" + message);
                ToastHost.Show("AI 对话失败", message, ToastKind.Warning);
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
                AppendStream(ev.Text, ev.Kind == "think");
                break;
            case "status":
                _status.Text = ev.Text;
                break;
            case "compact":
                // 历史被压成摘要:提示一下,并刷新占用环(压完会明显降下来)
                _status.Text = ev.Text;
                ToastHost.Show("上下文太长", "已把较早的对话压成摘要,继续问就行", ToastKind.Success);
                break;
            case "usage":
                RefreshUsage(ev.Usage, _history);
                break;
            case "tool":
                // ask_user 不进步骤组:它是给用户看的卡片
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
                    var line = Line(ev.Call.name, "完成:" + ev.Text, ToolState.Done);
                    line.SetResult(Summarize(ev.Call));
                }
                _status.Text = "操作完成";
                break;
            case "error":
                if (ev.Text == "已停止")
                {
                    FinishRun("已停止");
                }
                else
                {
                    Append("assistant", "对话失败:" + ev.Error);
                    ToastHost.Show("AI 对话失败", ev.Error, ToastKind.Warning);
                    FinishRun("");
                }
                break;
            case "done":
                FinishRun("");
                break;
        }
    }

    /// <summary>工具结果的短摘要(给步骤行右侧那格)。</summary>
    private static string Summarize(ToolCall call)
    {
        if (call.status == ToolCallStatus.DENIED)
        {
            // 被拒的步骤行要说明「为什么被拒」:reason 由 Core 的裁决带出(照 Windows 的
            // SetToolStatus(..., "denied", "[权限] 已拒绝:" + reason)),不再只显示结果 JSON。
            return string.IsNullOrEmpty(call.error) ? "已拒绝" : call.error;
        }
        if (call.status == ToolCallStatus.FAILED)
        {
            return "失败";
        }
        string text = call.result ?? "";
        if (text.Length == 0)
        {
            return "";
        }
        if (text.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                PyDict dict = Py.D(PyJson.Loads(text));
                string message = Py.S(dict.Get("message"));
                if (message.Length > 0)
                {
                    return AiUiText.Trim(message, 40);
                }
                object? list = dict.Get("items");
                if (list is List<object> items)
                {
                    return items.Count + " 条结果";
                }
            }
            catch (Exception)
            {
                // 结果不是 JSON 就原样截断
            }
        }
        return AiUiText.Trim(text.Replace("\n", " "), 40);
    }

    private void AppendStream(string delta, bool thinking)
    {
        if (_pending == null)
        {
            _pending = Append("assistant", "");
            _text = "";
        }
        if (thinking)
        {
            _think += delta;
            _pending.SetThinking(_think);
            return;
        }
        _text += delta;
        _pending.AppendAnswer(delta);
        ScrollToEnd();
    }

    private void FinishRun(string note)
    {
        _run?.Dispose();
        _run = null;
        _input.Send.IsVisible = true;
        _input.Stop.IsVisible = false;
        _input.Hint.Text = note;
        _status.Text = "";
        if (_pending != null)
        {
            string answer = _pending.Markdown();
            if (answer.Trim().Length > 0)
            {
                _history.Add(PyDict.Of("role", "assistant", "content", answer));
            }
            else
            {
                _messages.Children.Remove(_pending);
                _bubbles.Remove(_pending);
            }
            _pending = null;
        }
        foreach (StepGroup group in _messages.Children.OfType<StepGroup>())
        {
            group.Complete();
        }
        _group = null;
        ClearDock();
        SaveChatHistory();
        RefreshUsage(Usage.Get(EnsureChatId()), _history);
        RefreshInputState();
    }

    private void Stop()
    {
        _run?.Cancel();
        _pendingConfirm?.Reject();
        _askWaiter?.TrySetResult(null);
        FinishRun("已停止");
    }

    private AiBubble Append(string role, string text)
    {
        var bubble = new AiBubble(role, text);
        _bubbles.Add(bubble);
        _messages.Children.Add(bubble);
        ScrollToEnd();
        return bubble;
    }

    private void ScrollToEnd() => Dispatcher.UIThread.Post(() => _scroll.Offset = new Vector(0, double.MaxValue), DispatcherPriority.Background);

    // ───────────────────────────── 步骤组 / 卡片 ─────────────────────────────

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
            _bubbles.Add(_group);
        }
        _group.AddLine(line);
        ScrollToEnd();
        return line;
    }

    /// <summary>一段「正文」结束:下一个工具要开新的步骤组。</summary>
    private void CloseRound()
    {
        _group = null;
        _pending = null;
    }

    private void ShowPlan(List<(string Title, string Status)> items)
    {
        if (_planCard != null)
        {
            _messages.Children.Remove(_planCard);
            _bubbles.Remove(_planCard);
        }
        _planCard = new PlanCard(items);
        _messages.Children.Add(_planCard);
        _bubbles.Add(_planCard);
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
            Launch = () =>
            {
                if (this.GetVisualRoot() is MainWindow w) w.Navigate("launch");
            },
            OpenFolder = () => _ = Api.CallAsync<string>("open_instance_folder", new { name = _runner.Instance }),
            Retry = _ => _input.Text = "刚才那一步失败了,再试一次",
        };
        _taskCards[taskId] = card;
        _taskLines[taskId] = line;
        _messages.Children.Add(card);
        _bubbles.Add(card);
        ScrollToEnd();
    }

    // ───────────────────────────── 停靠区:确认 / 选择题 ─────────────────────────────

    /// <summary>
    /// 权限确认。三个信息槽与确认卡的三个位置一一对应:<paramref name="label"/> 是这一步「做什么」
    /// (Core 的 Tools.Label,空则卡片回退到工具名),<paramref name="reason"/> 是权限裁决给出的
    /// 「为什么需要你确认」;二者不许混着传 —— 卡片上它们是两行(reason 为空时那一行不渲染)。
    /// <paramref name="preview"/> 是 Core 在真的要写之前算好的「改前改后」预览(没有可预览的东西时为 null),
    /// 摊平成行后交给确认卡显示。
    /// </summary>
    private async Task<(bool ok, bool always)> ConfirmAsync(ToolCall call, string label, string reason, PyDict? preview)
    {
        var waiter = new TaskCompletionSource<(bool, bool)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _confirmWaiter = waiter;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _dock.Children.Clear();
            ConfirmCard? card = null;
            card = new ConfirmCard(call.name, label, reason, Preview.Flatten(preview),
                ConfirmCard.AllowAlways(call.name),
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

    // ───────────────────────────── 事件流(任务卡进度)─────────────────────────────

    /// <summary>任务进度事件转给对应的任务卡与步骤行(外壳经 PageBase.OnEvent 派发)。</summary>
    protected override void OnEvent(ApiEvent ev)
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

    protected override void OnDeactivate() => ClearDock();

    private void Clear()
    {
        Stop();
        _history.Clear();
        _toolLines.Clear();
        _taskCards.Clear();
        _planCard = null;
        foreach (var b in _bubbles.ToList())
        {
            _messages.Children.Remove(b);
        }
        _bubbles.Clear();
        _messages.Children.Add(_empty);
        _empty.IsVisible = true;
        // 当前对话的消息一并清掉(存档里其它对话不受影响)
        if (_chatLoaded)
        {
            try
            {
                PyDict data = Store.Load();
                Store.UpsertMessages(data, EnsureChatId(), new List<object>());
            }
            catch (Exception)
            {
                // 存档清不掉不影响继续用
            }
        }
        Usage.Reset(_chatId);
        RefreshUsage(Usage.Get(_chatId), _history);
        RefreshInputState();
    }
}
