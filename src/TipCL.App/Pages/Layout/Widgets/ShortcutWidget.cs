// 来源: Windows 版参考仓库 windos/src/TipCL/Pages/Layout/Widgets/ShortcutWidget.cs(分支 r32-r31-merge @ 0a304a7)。
// 移植说明:
//  1 机械改写:namespace / 删 `using TipCL.Services;`(I18n→I18nCore);`I18n.L` → `I18nCore.Tr`;
//    `UIElement` → `Control`;`SPanel` → `StackPanel`;`Ui.*` → `U.*`;`Ico.Play` → 图标名 "Play"
//    (U.Btn 的 glyph 参数收 IconLib 名);`AppServices.Toast` → `ToastHost.Show`;
//    `AppServices.Window?.Navigate` → `LayoutFx.Navigate`(同命名空间,无窗口时安全空转)。
//  2 机械改写(启动入口):Windows 的 `BridgeClient.StartTaskAsync("launch_game", args)` → Linux 的
//    `InProcessApi.Instance.CallAsync<object?>("launch_game", args)`(与 LaunchPage.cs:992 同一入口)。
//  3 机械偏差(已记录):Windows 版先取 `BridgeClient`,取不到就 toast「启动器还没准备好」;
//    Linux 的进程内 API(`InProcessApi.Instance`)恒非空,故该分支在 Linux 没有对应物,整体删除。
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.App.Common;
using TipCL.App.Ui;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;

namespace TipCL.App.Pages.Layout;

// 快捷启动区块(§4.F 的 shortcut):一键启动指定实例 / 版本。
// 只调用启动器自己的 launch_game 任务(与 LaunchPage.cs:992 同一入口),不碰任意命令。
internal static class ShortcutWidget
{
    /// <summary>渲染:标题 + 一个启动按钮。</summary>
    public static Control Build(WidgetDef widget)
    {
        string instance = WidgetCommon.Str(widget.Settings, "instance", "").Trim();
        string version = WidgetCommon.Str(widget.Settings, "version", "").Trim();
        string label = WidgetUI.TitleOf(widget);
        if (instance.Length == 0)
        {
            return WidgetUI.Placeholder(I18nCore.Tr("快捷启动没配置实例"), I18nCore.Tr("在布局 JSON 里给这个区块填 settings.instance(可选 settings.version)。"));
        }
        if (instance.Length > 128 || version.Length > 128)
        {
            return WidgetUI.Placeholder(I18nCore.Tr("快捷启动配置太长"), I18nCore.Tr("实例名 / 版本号都不应超过 128 个字符。"));
        }
        Button button = null!;
        button = U.Btn(label, BtnKind.Primary, delegate
        {
            if (!button.IsEnabled)
            {
                return;
            }
            button.IsEnabled = false;
            _ = LaunchAsync(button, instance, version);
        }, "Play");
        TextBlock hint = U.Small((version.Length > 0) ? instance + " · " + version : instance);
        StackPanel panel = U.V(8.0, U.Small(WidgetUI.TypeLabel(LayoutSchema.WidgetShortcut)), U.H(8.0, button, U.Spring()), hint.Wrap());
        return U.Card(panel, 10.0);
    }

    // 启动走既有任务入口:参数只给实例与版本,其余(账号 / 内存 / Java)由后端按当前配置补默认
    private static async Task LaunchAsync(Button button, string instance, string version)
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
        finally
        {
            button.IsEnabled = true;
        }
    }
}
