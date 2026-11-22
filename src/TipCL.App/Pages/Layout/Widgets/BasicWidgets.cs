// 来源: Windows 版参考仓库 windos/src/TipCL/Pages/Layout/Widgets/BasicWidgets.cs(分支 r32-r31-merge @ 0a304a7)。
// 移植说明:
//  1 机械改写:namespace TipCL.Pages.Layout → TipCL.App.Pages.Layout;删掉 Linux 不存在的 `using TipCL.Services;`
//    (I18n 住 TipCL.Core.Launcher.I18nCore);`I18n.L(x)` → `I18nCore.Tr(x)`;`UIElement` → `Control`;
//    `SPanel` → `StackPanel`;`Ui.*` → `U.*`(同命名空间门面,裸 `Ui.` 会被 TipCL.App.Ui 命名空间遮蔽),
//    只有分隔线用的 `Ui.Sep()` 走别名 `UiF`(U 门面没转发 Sep);`TextWrapping` 走 Avalonia.Media。
//  2 机械改写(事件名):WPF 的 `Loaded` / `Unloaded` → Avalonia 的 `AttachedToVisualTree` / `DetachedFromVisualTree`
//    (本仓既有写法见 SettingsPage.cs:376-377、BlockHost.cs:138)。
//  3 机械改写(计时器):Windows 的 `new DispatcherTimer(DispatcherPriority.Background)` 在 Avalonia 里没有
//    对应构造(全仓无此用法),改为 `new DispatcherTimer { Interval = ... }`;丢掉的 Background 优先级只影响
//    刷新时机,不影响每秒走表的行为。
using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;
using UiF = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Layout;

// 三类简单自建区块:便签 / 时钟 / 分隔线(§4.F 的首期白名单 note / clock / divider)。
internal static class BasicWidgets
{
    /// <summary>便签:显示 settings.text,空则给一句引导。</summary>
    public static Control Note(WidgetDef widget)
    {
        string text = WidgetCommon.Str(widget.Settings, "text", "").Trim();
        if (text.Length == 0)
        {
            text = I18nCore.Tr("在布局 JSON 里给这个区块填 settings.text 即可写便签。");
        }
        if (text.Length > 4000)
        {
            text = text.Substring(0, 4000) + "...";
        }
        // AI4-other 令牌:D-84 §3 / launch_v3 规则 7「字号只有五档」。便签正文原来是 12.5,
        // 按 Tok.Font 的归并表(≤12.5 → 12)本来就是「说明 / 次要字」档 → 直接写令牌,不再留一个会被归并的字面量。
        TextBlock body = U.Txt(text, TipCL.App.Ui.Tok.FsCaption);
        body.TextWrapping = TextWrapping.Wrap;
        body.LineHeight = 19.0;
        StackPanel panel = U.V(6.0, U.Small(WidgetUI.TitleOf(widget)), body);
        return U.Card(panel, 10.0);
    }

    /// <summary>时钟:按 settings.format(.NET 时间格式,默认 HH:mm)每秒刷新;离开可视树就停表。</summary>
    public static Control Clock(WidgetDef widget)
    {
        string format = WidgetCommon.Str(widget.Settings, "format", "HH:mm").Trim();
        if (format.Length == 0 || format.Length > 32)
        {
            format = "HH:mm";
        }
        TextBlock big = U.Txt(SafeTime(format), 26.0, bold: true);
        TextBlock caption = U.Small(WidgetUI.TitleOf(widget));
        StackPanel panel = U.V(2.0, big, caption);
        DispatcherTimer timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.0)
        };
        timer.Tick += delegate
        {
            string next = SafeTime(format);
            if (!string.Equals(big.Text, next, StringComparison.Ordinal))
            {
                big.Text = next;
            }
        };
        // 只在可视时走表:摘下来就停,避免计时器把内容一直挂在内存里
        panel.AttachedToVisualTree += delegate
        {
            timer.Start();
        };
        panel.DetachedFromVisualTree += delegate
        {
            timer.Stop();
        };
        return U.Card(panel, 10.0);
    }

    /// <summary>分隔线:复用 Ui.Sep() 的 B.LineSoft 1px 上分隔线。</summary>
    public static Control Divider(WidgetDef widget)
    {
        return UiF.Sep();
    }

    // 时间格式非法时退回 HH:mm(绝不因为用户格式串抛异常)
    private static string SafeTime(string format)
    {
        try
        {
            return DateTime.Now.ToString(format);
        }
        catch (FormatException)
        {
            return DateTime.Now.ToString("HH:mm");
        }
        catch (Exception)
        {
            return "";
        }
    }
}
