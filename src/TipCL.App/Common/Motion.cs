using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace TipCL.App.Common;

/// <summary>
/// 动画门面（对齐 Windows 版 Services/Motion.cs 的常用面，Avalonia Transitions 实现）：
/// 淡入/淡出、入场位移（FadeIn/Stagger）、进度条平滑（Progress）。设置页「界面动画」开关控 Enabled。
/// </summary>
public static class Motion
{
    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// 延后执行一段动画收尾代码。**这里必须吞掉异常** —— 动画定时器可能在 Avalonia 平台 / 服务
    /// 定位器已经拆掉之后才跑到(收尾竞态),这时写 `TranslateTransform.Y` 会抛
    /// `Unable to locate 'Avalonia.Animation.IGlobalClock'`。它属于**未被观察的任务异常**,
    /// .NET Core 会由终结器线程重新抛出并**直接干掉整个进程**:2026-11-13 在 Linux 盒子上跑
    /// `dotnet test` 时就是这么把测试宿主打崩的(跑完 148 条后 `Test host process crashed`,
    /// 栈顶 `Motion.FadeIn` → `TranslateTransform.set_Y`)。
    /// 真机运行期不会走到这里(那时平台还活着);走到这里就说明已经在收尾,记一条 WARN 即可,
    /// 不该让它升级成进程崩溃 —— 与 AppLog 对「未观察异常」的处置口径一致(见 D-62)。
    /// </summary>
    private static void Later(TimeSpan delay, Action body)
    {
        DispatcherTimer.RunOnce(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                AppLog.Warn("动画收尾回调在平台拆掉之后触发,已忽略: " + ex.GetType().Name + ": " + ex.Message);
            }
        }, delay);
    }

    /// <summary>淡入 + 自下方 fromY 位移（ms 毫秒）。</summary>
    public static void FadeIn(Control el, double ms = 220, double fromY = 10, double delayMs = 0)
    {
        if (!Enabled || el == null) return;
        el.Opacity = 0;
        el.Transitions ??= new Transitions();
        el.Transitions.Add(new DoubleTransition
        {
            Property = Visual.OpacityProperty,
            Duration = TimeSpan.FromMilliseconds(ms),
        });
        if (fromY != 0)
        {
            var translate = new TranslateTransform { Y = fromY };
            translate.Transitions = new Transitions
            {
                new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(ms) },
            };
            el.RenderTransform = translate;
            Later(TimeSpan.FromMilliseconds(delayMs), () =>
            {
                el.Opacity = 1;
                translate.Y = 0;
            });
        }
        else
        {
            Later(TimeSpan.FromMilliseconds(delayMs), () => el.Opacity = 1);
        }
    }

    /// <summary>淡出，结束后回调（默认移出可视树由调用方处理）。</summary>
    public static void FadeOut(Control el, double ms = 140, Action? done = null)
    {
        if (!Enabled || el == null)
        {
            done?.Invoke();
            return;
        }
        el.Transitions ??= new Avalonia.Animation.Transitions();
        el.Transitions.Add(new DoubleTransition
        {
            Property = Visual.OpacityProperty,
            Duration = TimeSpan.FromMilliseconds(ms),
        });
        el.Opacity = 0;
        Later(TimeSpan.FromMilliseconds(ms), () => done?.Invoke());
    }

    /// <summary>容器子项依次入场位移淡入（照 Windows 版 Motion.Stagger 语义）。</summary>
    public static void Stagger(Panel? host, double step = 26, double ms = 210, double fromY = 12)
    {
        if (host == null || !Enabled) return;
        double delay = 0;
        foreach (var child in host.Children.OfType<Control>())
        {
            FadeIn(child, ms, fromY, delay);
            delay += step;
        }
    }

    /// <summary>进度条平滑到目标值（Avalonia ProgressBar.Value 过渡）。</summary>
    public static void Progress(ProgressBar bar, double value)
    {
        if (bar == null) return;
        if (!Enabled)
        {
            bar.Value = value;
            return;
        }
        bar.Transitions ??= new Avalonia.Animation.Transitions();
        if (!bar.Transitions.Any(t => t is DoubleTransition d && d.Property == RangeBase.ValueProperty))
            bar.Transitions.Add(new DoubleTransition
            {
                Property = RangeBase.ValueProperty,
                Duration = TimeSpan.FromMilliseconds(220),
            });
        bar.Value = value;
    }

    /// <summary>悬停上浮（Scale 1.02，配 Transitions；挂在 Border/Button 上）。</summary>
    public static void HoverLift(Control el, double scale = 1.02)
    {
        if (!Enabled || el == null) return;
        var scaleT = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
        el.RenderTransform = scaleT;
        el.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        scaleT.Transitions = new Avalonia.Animation.Transitions
        {
            new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(140) },
            new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(140) },
        };
        el.PointerEntered += (_, _) => { scaleT.ScaleX = scale; scaleT.ScaleY = scale; };
        el.PointerExited += (_, _) => { scaleT.ScaleX = 1; scaleT.ScaleY = 1; };
    }
}
