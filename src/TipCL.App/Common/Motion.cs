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
            DispatcherTimer.RunOnce(() =>
            {
                el.Opacity = 1;
                translate.Y = 0;
            }, TimeSpan.FromMilliseconds(delayMs));
        }
        else
        {
            DispatcherTimer.RunOnce(() => el.Opacity = 1, TimeSpan.FromMilliseconds(delayMs));
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
        DispatcherTimer.RunOnce(() => done?.Invoke(), TimeSpan.FromMilliseconds(ms));
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
