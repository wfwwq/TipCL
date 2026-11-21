// AI2-moddev-mcp:AI 页标题栏的「MCP 已开启 · 最近外部调用」状态条(照 Windows 版 AiPage 的 _mcpBadge / SyncMcp)。
// 订阅 Core McpServer 的 StateChanged / ToolCalled(已有的事件与 LastTool),挂上视觉树时订阅、摘下时退订,
// 不在静态事件上留页面引用。MCP 没开时整条隐藏。
// 共享的 AiPage.cs 只改了一处:标题栏按钮列表里加 McpStatusChip()。
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TipCL.App.Common;
using TipCL.App.Ui;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Ai;

public sealed partial class AiPage
{
    private Border? _mcpChip;
    private TextBlock? _mcpChipText;

    /// <summary>状态条文字(与 Windows 版同一组词条:「MCP 已开启」/「MCP 已开启 · {0} {1}」);没开返回空。</summary>
    internal static string McpStatusText(bool running, string lastTool, DateTime lastAt)
    {
        if (!running)
        {
            return "";
        }
        return (lastTool ?? "").Length > 0
            ? string.Format(I18nCore.Tr("MCP 已开启 · {0} {1}"), lastTool, lastAt.ToString("HH:mm"))
            : I18nCore.Tr("MCP 已开启");
    }

    /// <summary>状态条悬停说明:地址 + 最近一次外部调用的完整时间。</summary>
    internal static string McpStatusTip(string url, string lastTool, DateTime lastAt)
    {
        string tip = string.Format(I18nCore.Tr("MCP Server 地址：{0}"), url);
        if ((lastTool ?? "").Length > 0)
        {
            tip += "\n" + string.Format(I18nCore.Tr("最近一次外部调用：{0}（{1}）"), lastTool, lastAt.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        return tip;
    }

    /// <summary>状态条本体(Name = McpStatusChip,测试按名字找)。</summary>
    internal Border McpStatusChip()
    {
        var dot = new Border
        {
            Width = 7,
            Height = 7,
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        dot[!Border.BackgroundProperty] = Res.Bind("B.Accent");
        _mcpChipText = UiClass.Small("");
        _mcpChipText.TextTrimming = TextTrimming.CharacterEllipsis;
        _mcpChipText.MaxWidth = 260;
        _mcpChipText.VerticalAlignment = VerticalAlignment.Center;
        var chip = new Border
        {
            Name = "McpStatusChip",
            Padding = new Thickness(8, 3, 9, 3),
            CornerRadius = new CornerRadius(10),
            VerticalAlignment = VerticalAlignment.Center,
            Child = UiClass.H(5, dot, _mcpChipText),
            IsVisible = false,
        };
        chip[!Border.BackgroundProperty] = Res.Bind("B.InfoSoft");
        _mcpChip = chip;
        chip.AttachedToVisualTree += (_, _) =>
        {
            McpServer.StateChanged += OnMcpChanged;
            McpServer.ToolCalled += OnMcpToolCalled;
            SyncMcpChip();
        };
        chip.DetachedFromVisualTree += (_, _) =>
        {
            McpServer.StateChanged -= OnMcpChanged;
            McpServer.ToolCalled -= OnMcpToolCalled;
        };
        SyncMcpChip();
        return chip;
    }

    private void OnMcpChanged() => Dispatcher.UIThread.Post(SyncMcpChip);

    private void OnMcpToolCalled(string tool, DateTime at) => Dispatcher.UIThread.Post(SyncMcpChip);

    /// <summary>按 McpServer 的当前状态刷新状态条(只在 UI 线程)。</summary>
    internal void SyncMcpChip()
    {
        if (_mcpChip == null || _mcpChipText == null)
        {
            return;
        }
        bool running = McpServer.Running;
        _mcpChip.IsVisible = running;
        if (!running)
        {
            return;
        }
        _mcpChipText.Text = McpStatusText(true, McpServer.LastTool, McpServer.LastToolAt);
        ToolTip.SetTip(_mcpChip, McpStatusTip(McpServer.Url, McpServer.LastTool, McpServer.LastToolAt));
    }
}
