// AI-engine-mcp:本机 MCP Server 的界面侧接线(执行器工厂 + 外部写操作确认框 + 启停)。
// Core 的 McpServer 只认 ToolRunner;这里给它:IApi 门面(与 AI 对话页同一个 InProcessApi)、
// 默认实例、以及确认回调 —— 外部 agent 发起的写操作在主窗口上弹 Dlg.Ask,用户点了才执行
// (参考版 McpServer 直接执行;Linux 侧的口径见 Core/Launcher/Ai/McpServer.cs 文件头)。
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;

namespace TipCL.App.Common.Ai;

/// <summary>本机 MCP Server 的宿主接线。App 启动时调用 <see cref="Init"/>,退出时 <see cref="Shutdown"/>。</summary>
public static class McpServerHost
{
    private static bool _inited;

    /// <summary>注入执行器工厂;上次开着就自动开(照参考版 BridgeHost 里的 StartIfEnabled)。</summary>
    public static void Init()
    {
        if (_inited)
        {
            return;
        }
        _inited = true;
        // AI2-moddev-mcp:get_task_status / run_test_launch 要能读启动器自己的任务表(install_* / launch_game 起的任务)
        ModDevHost.HostTaskProbe = TaskProbe;
        try
        {
            McpServer.StartIfEnabled(CreateRunner);
        }
        catch (Exception ex)
        {
            Trace.Record("mcp_server_init_failed", null, "", "", ex);
        }
    }

    public static void Shutdown()
    {
        try
        {
            McpServer.Stop();
        }
        catch (Exception)
        {
        }
    }

    /// <summary>每次外部 tools/call 用一个新执行器(互不共享「以后都允许」之类的会话状态)。</summary>
    public static ToolRunner CreateRunner() => new(new ApiBackend())
    {
        Instance = Py.S(Config.CONFIG.Get("default_instance", "default")),
        Confirm = ConfirmAsync,
        ForceConfirmWrites = true,
    };

    private sealed class ApiBackend : ToolRunner.IBackend
    {
        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
            => InProcessApi.Instance.CallAsync<T>(method, args, ct);
    }

    /// <summary>按 task_id 读 TaskStore 的一行(给 Core 的 ModDevHost.HostTaskProbe);没有就 null。</summary>
    internal static PyDict? TaskProbe(string id)
    {
        TaskRow? row = TaskStore.Get(id ?? "");
        if (row == null)
        {
            return null;
        }
        return PyDict.Of(
            "title", row.Title,
            "status", row.Status,
            "finished", row.Finished,
            "success", row.Success,
            "progress", row.Indeterminate && !row.Finished ? -1L : (long)Math.Round(row.Progress),
            "log", row.LogText);
    }

    /// <summary>
    /// AI2-moddev-mcp:弹外部确认框之前把主窗口拉到前台(最小化就还原、激活、短暂置顶一下绕过系统的焦点保护),
    /// 否则用户在别的窗口里写代码时根本看不到确认框。只在 UI 线程调用。
    /// </summary>
    internal static Window? BringMainWindowToFront()
    {
        Window? w = Dlg.Owner
            ?? (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null);
        if (w == null)
        {
            return null;
        }
        try
        {
            if (!w.IsVisible)
            {
                w.Show();
            }
            if (w.WindowState == WindowState.Minimized)
            {
                w.WindowState = WindowState.Normal;
            }
            w.Activate();
            bool top = w.Topmost;
            w.Topmost = true;
            w.Topmost = top;
        }
        catch (Exception ex)
        {
            Trace.Record("mcp_bring_to_front_failed", null, "", "", ex);
        }
        return w;
    }

    /// <summary>外部写操作确认框:写清「谁要做什么、为什么要你点」;不给「以后都允许」。</summary>
    private static async Task<(bool ok, bool always)> ConfirmAsync(ToolCall call, string label, string reason, PyDict? preview)
    {
        // AI2-moddev-mcp:等待超时由 Core 的 McpConfirmGate 计时;超时后这个信号被取消,把还开着的框关掉(要在同步开头读)
        CancellationToken giveUp = McpConfirmGate.CurrentToken;
        bool high = ToolMeta.Get(call.name) is { } meta && (meta.risk == "high" || meta.side_effect is "delete" or "launch");
        string args = Py.Slice(PyJson.Dumps(call.args ?? new PyDict(), null, ensureAscii: false), null, 600);
        bool ok = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var body = new StackPanel { Spacing = 6 };
            body.Children.Add(new TextBlock
            {
                Text = I18nCore.Tr("外部 MCP 客户端(如 Claude / Cursor)请求在本机执行："),
                TextWrapping = TextWrapping.Wrap,
            });
            body.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
            if (reason.Length > 0)
            {
                body.Children.Add(new TextBlock { Text = reason, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
            }
            body.Children.Add(new TextBlock { Text = call.name + " " + args, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Opacity = 0.7 });
            Window? owner = BringMainWindowToFront();
            var before = owner?.OwnedWindows.ToList() ?? new System.Collections.Generic.List<Window>();
            using CancellationTokenRegistration closeOnTimeout = giveUp.Register(() => Dispatcher.UIThread.Post(() =>
            {
                if (owner == null)
                {
                    return;
                }
                foreach (Window opened in owner.OwnedWindows.Except(before).ToList())
                {
                    try
                    {
                        opened.Close();
                    }
                    catch (Exception)
                    {
                    }
                }
            }));
            return await Dlg.Ask(I18nCore.Tr("外部 MCP 请求执行操作"), body, I18nCore.Tr("允许这一次"), I18nCore.Tr("拒绝"), danger: high, width: 520, owner: owner);
        });
        return (ok, false);
    }
}
