// 来源: Windows 版参考仓库 windos/src/TipCL/Pages/Layout/Widgets/DangerousWidgets.cs(分支 r32-r31-merge @ 0a304a7)。
// 移植说明:
//  1 机械改写:namespace / 删 `using TipCL.Services;`(I18n→I18nCore,PyJson→TipCL.Core,
//    Utils→TipCL.Core.Launcher.Utils);`I18n.L(x)` → `I18nCore.Tr(x)`;`UIElement` → `Control`;
//    `SPanel` → `StackPanel`;`Ui.*` → `U.*`;`Ico.Play` → "Play"、`Ico.Play2` → "Open";
//    `AppServices.Toast` → `ToastHost.Show`;`AppServices.Window?.Navigate` → `LayoutFx.Navigate`。
//  2 机械改写(启动入口):`BridgeClient.StartTaskAsync("launch_game", args)` →
//    `InProcessApi.Instance.CallAsync<object?>("launch_game", args)`(与 LaunchPage.cs:992 同一入口)。
//  3 机械改写(只读 RPC):Windows 的 `client.CallAsync(name)` 返回 `JsonElement`(走桥的 JSON 字符串),
//    Linux 进程内 API 返回对象,故改用 `PyJson.DumpsU(result)` 序列化成同一份可读文本再截断 200 字符。
//  4 机械偏差(已记录):Windows 版先取 `BridgeClient`,取不到就 toast「启动器还没准备好」;
//    Linux 的进程内 API 恒非空,故这两个分支在 Linux 没有对应物,整体删除。
//  5 降级(WebView2 专属,按本仓既有先例处理):
//    Windows 的 web 区块在 `WebViewHost.Available` 为真时挂 `PluginWebView`(WebView2/HwndHost)内嵌网页
//    (`WebShell : EditAwareShell`,编辑态用 Suppressed 临时藏起)。Linux 结构化地没有 WebView2 ——
//    按 lead 的裁定「按既有 Linux 先例降级」:恒走 Windows 版**本来就有**的 `!WebViewHost.Available` 分支
//    (「当前系统不能内嵌网页」+「在浏览器中打开」),删掉 WebShell 整类,**不发明**任何新的 WebView2 依赖。
//    Linux 先例:PluginUi.cs:6(插件界面只做占位)、PluginHostPage.cs:95-97(「Linux 版不引入 CEF / WebKitGTK
//    这类网页宿主,所以这里不内嵌网页」)、PluginHostPage.cs:74-77(给「在浏览器打开」)、
//    PluginHostPage.cs:104-114(OpenUrl:交给系统浏览器,失败 toast)、PluginModels.cs:283+
//    (UiDispatch.Placeholder:插件贡献的界面在 Linux 上如实说明 + 可选的「在浏览器打开」)。
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.App.Common;
using TipCL.App.Ui;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;

namespace TipCL.App.Pages.Layout;

// 危险类型区块(§0.1 Q5):web(网页)与 button(按钮),**默认关闭**。
// 只有布局文档里显式写 settings.enabled = true 才渲染内容;否则显示「已禁用的自建区块」占位(数据保留)。
// 两条安全线:
//   web    —— 只允许 https(禁 file:// / javascript: / data:);Linux 没有内嵌网页宿主,渲染成
//             「当前系统不能内嵌网页 + 在浏览器中打开」的卡片(降级,见文件头 5);
//   button —— 只允许内部动作白名单:切页 / 启动指定实例 / 打开启动器数据目录 / 触发只读 RPC,其它一律拒绝并给中文原因。
internal static class DangerousWidgets
{
    /// <summary>按类型分派(两种都先过 Q5 开关)。</summary>
    public static Control Build(WidgetDef widget)
    {
        if (!WidgetCommon.IsEnabled(widget))
        {
            return WidgetUI.DisabledDangerous(widget);
        }
        if (widget.Type == LayoutSchema.WidgetWeb)
        {
            return Web(widget);
        }
        return Button(widget);
    }

    // ————— web —————

    private static Control Web(WidgetDef widget)
    {
        string raw = WidgetCommon.Str(widget.Settings, "url", "");
        if (!WidgetCommon.TryAcceptWebUrl(raw, out string url, out string reason))
        {
            return WidgetUI.Placeholder(I18nCore.Tr("网页地址不合法"), reason);
        }
        // Linux 没有 WebView2 宿主:恒走 Windows 版本来就有兜底分支(降级点,见文件头 5)
        Button open = U.Btn(I18nCore.Tr("在浏览器中打开"), BtnKind.Soft, delegate
        {
            OpenUrl(url);
        }, "Open");
        return U.Card(U.V(6.0, U.Small(I18nCore.Tr("当前系统不能内嵌网页")).Wrap(), U.H(8.0, open, U.Spring())), 10.0);
    }

    // ————— button —————

    private static Control Button(WidgetDef widget)
    {
        string action = WidgetCommon.Str(widget.Settings, "action", "");
        if (!WidgetCommon.TryParseAction(action, out string kind, out string arg1, out string arg2, out string reason))
        {
            return WidgetUI.Placeholder(I18nCore.Tr("按钮动作不合法"), reason);
        }
        Button button = U.Btn(WidgetUI.TitleOf(widget), BtnKind.Normal, delegate
        {
            Run(kind, arg1, arg2);
        }, "Play");
        StackPanel panel = U.V(6.0, U.Small(WidgetUI.TypeLabel(LayoutSchema.WidgetButton)), U.H(8.0, button, U.Spring()), U.Small(kind + ":" + arg1));
        return U.Card(panel, 10.0);
    }

    // 白名单动作执行:全部走启动器自己的入口,不开任何外部命令
    private static void Run(string kind, string arg1, string arg2)
    {
        switch (kind)
        {
        case "page":
            LayoutFx.Navigate(arg1);
            break;
        case "folder":
            // 只允许启动器数据目录(WidgetCommon 已把别的值挡在外面)
            OpenUrl(Utils.ROOT);
            break;
        case "launch":
            _ = LaunchAsync(arg1, arg2);
            break;
        case "rpc":
            _ = CallRpcAsync(arg1);
            break;
        }
    }

    private static async Task LaunchAsync(string instance, string version)
    {
        try
        {
            Dictionary<string, object?> args = new Dictionary<string, object?>
            {
                ["instance"] = instance
            };
            if (version.Length > 0)
            {
                args["version"] = version;
            }
            await InProcessApi.Instance.CallAsync<object?>("launch_game", args, CancellationToken.None);
            LayoutFx.Navigate("launch");
        }
        catch (Exception ex)
        {
            ToastHost.Show(I18nCore.Tr("启动失败"), ex.Message, ToastKind.Error);
        }
    }

    // 只读 RPC:结果截断成一行 Toast(按钮不该弹大段内容)
    private static async Task CallRpcAsync(string name)
    {
        try
        {
            object? result = await InProcessApi.Instance.CallAsync<object?>(name, null, CancellationToken.None);
            string text = (result == null) ? "" : PyJson.DumpsU(result);
            if (text.Length > 200)
            {
                text = text.Substring(0, 200) + "...";
            }
            ToastHost.Show(name, text, ToastKind.Info);
        }
        catch (Exception ex)
        {
            ToastHost.Show(I18nCore.Tr("这个动作没能执行"), ex.Message, ToastKind.Error);
        }
    }

    // 交给系统浏览器打开(逐字照抄本仓既有实现 PluginHostPage.cs:104-114)
    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ToastHost.Show(I18nCore.Tr("打不开链接"), ex.Message, ToastKind.Warning);
        }
    }
}
