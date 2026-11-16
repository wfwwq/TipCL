// 来源: Windows 版参考仓库 windos/src/TipCL/Pages/Layout/Widgets/PlaytimeWidget.cs(分支 r32-r31-merge @ 0a304a7)。
// 移植说明:
//  1 机械改写:namespace / 删 `using TipCL.Services;`(I18n→I18nCore,Playtime→TipCL.Core.Launcher.Playtime,
//    Py/PyDict→TipCL.Core);`I18n.L` → `I18nCore.Tr`;`UIElement` → `Control`;`SPanel` → `StackPanel`;
//    `Ui.*` → `U.*`。
//  2 机械改写(事件名):WPF 的 `Loaded` / `Unloaded` → Avalonia 的 `AttachedToVisualTree` / `DetachedFromVisualTree`
//    (本仓既有写法见 SettingsPage.cs:376-377、BlockHost.cs:138)。语义不变:首次挂载用建卡时读的那份,
//    之后每次回到界面重读;离开界面退订静态事件,避免静态事件拖住已丢弃的卡片。
using System;
using Avalonia.Controls;
using Avalonia.Layout;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;

namespace TipCL.App.Pages.Layout;

// 游戏时长区块(§4.F 的 playtime):读启动器自己的 playtime.json(只读,不写),
// 不给 instance 就显示所有实例的累计时长,给了就显示那个实例的。
// 卡片在界面上常驻,不能只在建卡时读一次:重新显示(挂回可视树)时重读,
// 一局游戏结束时由宿主调 NotifyChanged 重读(见报告「遗留与风险」:Linux 侧目前没有调用点)。
internal static class PlaytimeWidget
{
    private static event Action? Changed;

    /// <summary>
    /// 游戏时长变了:界面上的时长块各自重读。只在 UI 线程调(宿主收到 game_exited 时;
    /// 后端先停计时写盘再发这个事件,所以此时读到的已经含刚结束的一局)。
    /// </summary>
    public static void NotifyChanged()
    {
        Changed?.Invoke();
    }

    /// <summary>渲染:大号时长 + 说明。</summary>
    public static Control Build(WidgetDef widget)
    {
        string instance = WidgetCommon.Str(widget.Settings, "instance", "").Trim();
        string title = WidgetUI.TitleOf(widget);
        ContentControl host = new ContentControl
        {
            Focusable = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        Action refresh = delegate
        {
            host.Content = Render(title, instance);
        };
        refresh();
        bool shown = false;
        host.AttachedToVisualTree += delegate
        {
            // 首次挂载用建卡时读的那份;之后每次回到界面(切页回来、编辑态重挂)都重读
            if (shown)
            {
                refresh();
            }
            shown = true;
            Changed -= refresh;
            Changed += refresh;
        };
        // 离开界面就退订:静态事件不能拖住已经丢弃的卡片
        host.DetachedFromVisualTree += delegate
        {
            Changed -= refresh;
        };
        return host;
    }

    private static Control Render(string title, string instance)
    {
        double seconds = -1.0;
        try
        {
            if (instance.Length == 0)
            {
                seconds = Py.Float(Playtime.GetTotalPlaytime());
            }
            else if (instance.Length <= 128)
            {
                PyDict data = Playtime.GetPlaytime(instance);
                seconds = Py.Float(data.Get("total") ?? 0L);
            }
        }
        catch (Exception)
        {
            // 读不到时长数据(文件损坏 / 首次运行)不能让整页渲染失败
            seconds = -1.0;
        }
        string caption = (instance.Length == 0) ? I18nCore.Tr("全部实例累计") : instance;
        if (seconds < 0.0)
        {
            return WidgetUI.Placeholder(title, I18nCore.Tr("读不到游戏时长数据。"));
        }
        TextBlock big = (seconds > 0.0) ? U.Txt(Playtime.FormatDuration(seconds), 22.0, bold: true) : U.Muted(I18nCore.Tr("还没有游戏时长记录"));
        StackPanel panel = U.V(4.0, U.Small(title), big, U.Small(caption));
        return U.Card(panel, 10.0);
    }
}
