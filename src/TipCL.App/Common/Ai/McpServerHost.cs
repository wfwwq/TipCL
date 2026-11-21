// AI-engine-mcp:本机 MCP Server 的界面侧接线(执行器工厂 + 外部写操作确认框 + 启停)。
// Core 的 McpServer 只认 ToolRunner;这里给它:IApi 门面(与 AI 对话页同一个 InProcessApi)、
// 默认实例、以及确认回调 —— 外部 agent 发起的写操作在主窗口上弹 Dlg.Ask,用户点了才执行
// (参考版 McpServer 直接执行;Linux 侧的口径见 Core/Launcher/Ai/McpServer.cs 文件头)。
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
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

    /// <summary>外部写操作确认框:写清「谁要做什么、为什么要你点」;不给「以后都允许」。</summary>
    private static async Task<(bool ok, bool always)> ConfirmAsync(ToolCall call, string label, string reason, PyDict? preview)
    {
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
            return await Dlg.Ask(I18nCore.Tr("外部 MCP 请求执行操作"), body, I18nCore.Tr("允许这一次"), I18nCore.Tr("拒绝"), danger: high, width: 520);
        });
        return (ok, false);
    }
}
