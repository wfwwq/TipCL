// 来源: Windows 版参考仓库 src/TipCL/Pages/TaskCard.cs(M4-H P5)。
// AI 触发的下载/安装后台任务卡:标题、进度条、百分比/速度;完成或失败后给出后续按钮(启动/打开文件夹/重试)。
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.App.Common;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Ai;

/// <summary>任务卡(照 Windows 版 TaskCard)。</summary>
internal sealed class TaskCard : Border
{
    private readonly TextBlock _title;
    private readonly TextBlock _status;
    private readonly TextBlock _percent;
    private readonly TextBlock _speed;
    private readonly ProgressBar _bar;
    private readonly StackPanel _buttons = UiClass.H(6);
    private readonly Border _iconHost;

    /// <summary>完成后「启动游戏」;为 null 时不显示。</summary>
    public Action? Launch { get; set; }

    /// <summary>完成后「打开文件夹」;为 null 时不显示。</summary>
    public Action? OpenFolder { get; set; }

    /// <summary>失败后「重试」(失败原因作参数);为 null 时不显示。</summary>
    public Action<string>? Retry { get; set; }

    public string TaskId { get; }

    public string Tool { get; }

    public string Instance { get; }

    public string FinishMessage { get; private set; } = "";

    public bool? Succeeded { get; private set; }

    internal string TitleText => _title.Text ?? "";

    public TaskCard(string taskId, string title, string tool, string instance)
    {
        TaskId = taskId ?? "";
        Tool = tool ?? "";
        Instance = instance ?? "";

        _title = UiClass.Txt(title ?? "", 13, true, "B.Ink");
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _status = UiClass.Txt("后台进行中", 11.5, false, "B.InkMuted");
        _status.TextTrimming = TextTrimming.CharacterEllipsis;
        _percent = UiClass.Txt("", 11.5, true, "B.AccentText");
        _speed = UiClass.Txt("", 11.5, false, "B.InkFaint");
        _bar = new ProgressBar { Height = 4, IsIndeterminate = true, Minimum = 0, Maximum = 100 };
        _buttons.IsVisible = false;
        _buttons.Margin = new Thickness(0, 4, 0, 0);

        var head = UiClass.G(null, "*,Auto,Auto");
        head.Add(_title.VCenter(), 0, 0);
        _speed.Margin = new Thickness(8, 0, 0, 0);
        head.Add(_speed.VCenter(), 0, 1);
        _percent.Margin = new Thickness(8, 0, 0, 0);
        head.Add(_percent.VCenter(), 0, 2);

        _iconHost = new Border { Child = AiIcons.Tile(AiIcons.Icon(AiIcons.Spinner, 14, "B.AccentText"), ToolState.Running) };
        var grid = UiClass.G(null, "Auto,*");
        grid.Add(_iconHost, 0, 0);
        var body = UiClass.V(5, head, _bar, _status, _buttons);
        body.Margin = new Thickness(10, 0, 0, 0);
        grid.Add(body, 0, 1);

        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(12, 10, 12, 10);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        this[!Border.BackgroundProperty] = Res.Bind("B.Paper2");
        this[!Border.BorderBrushProperty] = Res.Bind("B.LineSoft");
        Child = grid;
        SetIcon(ToolState.Running);
    }

    public void SetTitle(string? title)
    {
        _title.Text = title ?? "";
        ToolTip.SetTip(this, title);
    }

    private void SetIcon(ToolState state)
    {
        string brush = state == ToolState.Failed ? "B.Danger" : "B.AccentText";
        var icon = state == ToolState.Running
            ? AiIcons.Icon(AiIcons.Spinner, 14, brush)
            : AiIcons.Icon(state == ToolState.Failed ? AiIcons.X : AiIcons.Check, 14, brush);
        _iconHost.Child = AiIcons.Tile(icon, state);
    }

    public void SetProgress(long current, long total, string? message)
    {
        if (Succeeded.HasValue)
        {
            return;
        }
        _bar.IsIndeterminate = total <= 0;
        if (total > 0)
        {
            double percent = Math.Clamp((double)current * 100.0 / total, 0, 100);
            _bar.Value = percent;
            _percent.Text = $"{percent:0}%";
        }
        TaskMsg.SplitMsg(message, out string status, out string speed);
        if (status.Length > 0)
        {
            _status.Text = status;
            ToolTip.SetTip(_status, status);
        }
        _speed.Text = speed;
    }

    public void Finish(bool success, string? message)
    {
        Succeeded = success;
        FinishMessage = message ?? "";
        _bar.IsIndeterminate = false;
        _speed.Text = "";
        if (success)
        {
            _bar.Value = 100;
            _percent.Text = "100%";
            _bar.IsVisible = false;
            _status.Text = string.IsNullOrWhiteSpace(message) ? "完成" : message;
            _status[!TextBlock.ForegroundProperty] = Res.Bind("B.InkMuted");
        }
        else
        {
            _percent.Text = "失败";
            _percent[!TextBlock.ForegroundProperty] = Res.Bind("B.Danger");
            _status.Text = "原因:" + (string.IsNullOrWhiteSpace(message) ? "未知错误" : message);
            _status.TextWrapping = TextWrapping.Wrap;
            _status[!TextBlock.ForegroundProperty] = Res.Bind("B.Danger");
            this[!Border.BorderBrushProperty] = Res.Bind("B.Danger");
        }
        ToolTip.SetTip(_status, message);

        SetIcon(success ? ToolState.Done : ToolState.Failed);
        _buttons.Children.Clear();
        if (success)
        {
            if (Launch != null)
            {
                _buttons.Children.Add(U.Btn("启动游戏", BtnKind.Primary, (_, _) => Launch(), "Play"));
            }
            if (OpenFolder != null)
            {
                _buttons.Children.Add(U.Btn("打开文件夹", BtnKind.Chip, (_, _) => OpenFolder(), "Folder"));
            }
        }
        else if (Retry != null && FinishMessage != "已取消")
        {
            var button = U.Btn("重试", BtnKind.Chip, (_, _) =>
            {
                // 只让点一次:重试是「让助手重发这一步」,连点会重发多次
                var b = (Button)((Avalonia.Controls.StackPanel)_buttons).Children[0];
                b.IsEnabled = false;
                Retry(FinishMessage);
            }, "Refresh");
            ToolTip.SetTip(button, "让助手重新执行这一步");
            _buttons.Children.Add(button);
        }
        _buttons.IsVisible = _buttons.Children.Count > 0;
    }
}
