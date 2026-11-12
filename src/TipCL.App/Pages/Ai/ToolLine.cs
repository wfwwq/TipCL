// 来源: Windows 版参考仓库 src/TipCL/Pages/ToolLine.cs + Pages/ToolState.cs(M4-H P5)。
// 一行工具调用:图标块 + 中文标签 + 参数(等宽)+ 结果 + 耗时/进度条。
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.App.Common;
using TipCL.Core.Launcher.Ai;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Ai;

/// <summary>一步工具调用的行(照 Windows 版 ToolLine)。</summary>
internal sealed class ToolLine : Border
{
    private readonly TextBlock _label;
    private readonly TextBlock _arg;
    private readonly TextBlock _result;
    private readonly Border _mcp;
    private readonly ProgressBar _bar;
    private readonly Border _iconHost;
    private readonly string _tool;
    private ToolState _state = (ToolState)(-1);

    /// <summary>标签文字(StepGroup 的标题用它显示「正在…」)。</summary>
    internal string LabelText => _label.Text ?? "";

    internal string ArgText => _arg.Text ?? "";

    internal ToolState State => _state;

    public ToolLine(string text, string? tool = null)
    {
        _tool = tool ?? "";
        _label = UiClass.Txt("", 12.5, false, "B.Ink");
        _label.TextTrimming = TextTrimming.CharacterEllipsis;
        _arg = UiClass.Txt("", 11.5, false, "B.InkMuted");
        _arg.FontFamily = MdView.Mono;
        _arg.TextTrimming = TextTrimming.CharacterEllipsis;
        _arg.Margin = new Thickness(8, 0, 8, 0);
        _result = UiClass.Txt("", 11.5, false, "B.InkFaint");

        _mcp = UiClass.Tag("MCP · 外部", "B.Info", "B.InfoSoft");
        _mcp.Margin = new Thickness(8, 0, 0, 0);
        _mcp.IsVisible = AiUiText.IsMcp(tool);

        _bar = new ProgressBar { Height = 3, IsIndeterminate = true, IsVisible = false };
        _bar.Margin = new Thickness(35, 2, 0, 0);

        var grid = UiClass.G(null, "Auto,Auto,Auto,*,Auto");
        _iconHost = new Border { Child = AiIcons.Tile(AiIcons.Icon(AiIcons.Spinner, 14), ToolState.Prepare) };
        grid.Add(_iconHost, 0, 0);
        grid.Add(UiExt.Mk(Vert(_label), 9, 0, 0, 0), 0, 1);
        grid.Add(_mcp.VCenter(), 0, 2);
        grid.Add(_arg.VCenter(), 0, 3);
        grid.Add(_result.VCenter(), 0, 4);
        Padding = new Thickness(6, 4, 6, 4);
        CornerRadius = new CornerRadius(6);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Background = Brushes.Transparent;
        Child = UiClass.V(0, grid, _bar);

        SetText(text);
        SetState(ToolState.Prepare);
    }

    private static Control Vert(Control c)
    {
        c.VerticalAlignment = VerticalAlignment.Center;
        return c;
    }

    /// <summary>更新文字(「执行中: 装模组 钠」这类)。</summary>
    public void SetText(string text)
    {
        (string label, string arg) = AiUiText.SplitLabel(text);
        _label.Text = label;
        _arg.Text = arg;
        ToolTip.SetTip(this, AiUiText.StripState(text));
    }

    public void SetResult(string? text) => _result.Text = text ?? "";

    public void SetState(ToolState state)
    {
        if (_state == state)
        {
            return;
        }
        _state = state;
        SyncIcon();
    }

    private void SyncIcon()
    {
        bool busy = _state is ToolState.Prepare or ToolState.Running;
        string brushKey = _state switch
        {
            ToolState.Failed => "B.Danger",
            ToolState.Skipped => "B.InkMuted",
            _ => "B.AccentText",
        };
        _label.Opacity = busy ? 0.75 : 1;
        var icon = AiIcons.Icon(busy ? AiIcons.Spinner : AiIcons.ToolIcon(_tool), 14, brushKey);
        _iconHost.Child = AiIcons.Tile(icon, _state);
        if (_state == ToolState.Skipped && (_result.Text ?? "").Length == 0)
        {
            _result.Text = "已跳过";
        }
    }

    /// <summary>后台任务开始:进度条转起来。</summary>
    public void BindTask()
    {
        _bar.IsVisible = true;
        _bar.IsIndeterminate = true;
        _result.Text = "后台进行中";
    }

    /// <summary>任务进度回调。</summary>
    public void SetProgress(long current, long total, string? message)
    {
        _bar.IsVisible = true;
        _bar.IsIndeterminate = total <= 0;
        if (total > 0)
        {
            double percent = Math.Clamp((double)current * 100.0 / total, 0, 100);
            _bar.Value = percent;
            _result.Text = $"{percent:0}%";
        }
        TaskMsg.SplitMsg(message, out string status, out _);
        if (status.Length > 0)
        {
            _arg.Text = status;
        }
    }

    /// <summary>任务结束。</summary>
    public void Finish(bool success, string? message)
    {
        _bar.IsIndeterminate = false;
        if (success)
        {
            _bar.Value = 100;
            _bar.IsVisible = false;
        }
        _result.Text = success ? "完成" : "失败";
        if (!string.IsNullOrWhiteSpace(message))
        {
            _arg.Text = message;
            ToolTip.SetTip(this, message);
        }
        SetState(success ? ToolState.Done : ToolState.Failed);
        _result[!TextBlock.ForegroundProperty] = Res.Bind(success ? "B.InkFaint" : "B.Danger");
    }
}
