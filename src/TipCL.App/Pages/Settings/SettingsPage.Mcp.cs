// AI-engine-mcp:设置 → AI 助手 → MCP Server 一行(开关 + 端口 + 状态 + 复制地址)。
// 对位 Windows 版 SettingsPage.cs:1070-1100 的 MCP Server 开关(状态文案「未开启 / 启动失败：… / 地址」照搬);
// 参考版只有开关、端口要手改 mcp_server.json,Linux 侧把端口也放到这一行(同一个文件、同一个键,默认 25585)。
using System;
using Avalonia.Controls;
using TipCL.App.Common;
using TipCL.App.Ui;
using UiClass = TipCL.App.Ui.Ui;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;

namespace TipCL.App.Pages.Settings;

public sealed partial class SettingsPage
{
    /// <summary>MCP Server 一行:开关落 mcp_server.json 的 enabled,端口落 port(与参考版同名同默认值)。</summary>
    private Control BuildMcpRow()
    {
        (bool enabled, int port) = McpServer.LoadPrefs();
        var status = UiClass.Small("");
        var portBox = UiClass.Input(McpServer.DefaultPort.ToString(), port.ToString(), 90);
        bool syncing = false;
        CheckBox sw = null!;
        void Render()
        {
            void Apply()
            {
                syncing = true;
                sw.IsChecked = McpServer.Running;
                syncing = false;
                status.Text = McpServer.Running
                    ? McpServer.Url
                    : (string.IsNullOrEmpty(McpServer.LastError) ? I18nCore.Tr("未开启") : I18nCore.Tr("启动失败：") + McpServer.LastError);
            }
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) Apply();
            else Avalonia.Threading.Dispatcher.UIThread.Post(Apply);
        }
        sw = UiClass.Switch(enabled && McpServer.Running, (_, _) =>
        {
            if (syncing) return;
            bool on = sw.IsChecked == true;
            bool running = McpServer.SetEnabled(on);
            if (on && !running)
                Toast(I18nCore.Tr("MCP Server 启动失败"), McpServer.LastError, ToastKind.Warning);
            Render();
        });
        var applyPort = UiClass.Btn(I18nCore.Tr("应用端口"), BtnKind.Chip, (_, _) =>
        {
            if (!int.TryParse((portBox.Text ?? "").Trim(), out int p) || !McpServer.SetPort(p))
            {
                Toast(I18nCore.Tr("端口无效"), I18nCore.Tr("端口要在 1–65535 之间"), ToastKind.Warning);
                return;
            }
            Render();
        });
        var copy = UiClass.Btn(I18nCore.Tr("复制地址"), BtnKind.Chip, async (_, _) =>
        {
            var clip = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clip != null)
                await clip.SetTextAsync(McpServer.Url);
            Toast(I18nCore.Tr("已复制"), McpServer.Url, ToastKind.Success);
        });
        Action onState = Render;
        AttachedToVisualTree += (_, _) => { McpServer.StateChanged -= onState; McpServer.StateChanged += onState; Render(); };
        DetachedFromVisualTree += (_, _) => McpServer.StateChanged -= onState;
        Render();
        return UiClass.V(4, UiClass.H(8, sw, portBox, applyPort, copy), status.Wrap());
    }
}
