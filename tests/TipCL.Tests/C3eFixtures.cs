using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C3e(把 <see cref="Motion"/> 的变换动画修成真桌面上真的会跑)期望值的**权威副本**。
/// 每条注明来源:【Windows 源】<c>Services\Motion.cs</c>(READ-ONLY)、【任务书】、【本机实测】——
/// 源码改了而期望没跟着改会立刻红。
/// </summary>
internal static class C3eExpected
{
    /// <summary>【Windows 源】:218-232 FadeIn:不透明度 220ms、竖直位移 <c>fromY = 10.0</c>、位移时长 <c>ms + 60</c>。</summary>
    internal const double FadeInMs = 220.0;

    internal const double FadeInFromY = 10.0;

    internal const double FadeInSlackMs = 60.0;

    /// <summary>【Windows 源】:299-356 HoverLift:缩放 1.012、抬升 2px、进 170ms / 出 190ms。</summary>
    internal const double HoverScale = 1.012;

    internal const double HoverLiftPx = 2.0;

    internal const double HoverInMs = 170.0;

    internal const double HoverOutMs = 190.0;

    /// <summary>【Windows 源】:391-399 Pulse:峰值 1.06、280ms、三帧 <c>1 → peak → 1</c>。</summary>
    internal const double PulsePeak = 1.06;

    internal const double PulseMs = 280.0;

    /// <summary>【Windows 源】:407-417 Shake:六个关键帧与 330ms。</summary>
    internal static readonly double[] ShakeFrames = new double[6] { -7.0, 6.0, -4.0, 3.0, -1.5, 0.0 };

    internal const double ShakeMs = 330.0;

    /// <summary>【Windows 源】:440-450 Shine:1600ms、起点 <c>-(宽度 + 40)</c>。</summary>
    internal const double ShineMs = 1600.0;

    internal const double ShineLeadIn = 40.0;

    /// <summary>【Windows 源】:454-460 透明度四帧 <c>0 / 0.5 / 0.5 / 0</c>。</summary>
    internal static readonly double[] ShineOpacityFrames = new double[4] { 0.0, 0.5, 0.5, 0.0 };

    /// <summary>【Windows 源】:467-477 Spin:每圈 1 秒、0° → 360°,停止时归零。</summary>
    internal const double SpinSeconds = 1.0;

    internal const double SpinDegrees = 360.0;

    /// <summary>【Windows 源】:581-594 PopIn:从 0.96 到 1.0,时长 <c>ms + 60</c>。</summary>
    internal const double PopInFrom = 0.96;

    /// <summary>【Windows 源】:607-616 PopOut:缩到 0.97。</summary>
    internal const double PopOutTo = 0.97;

    /// <summary>【Windows 源】:629-636 SlideIn:从 +40px 滑到 0。</summary>
    internal const double SlideInFromX = 40.0;

    /// <summary>【Windows 源】:649-653 SlideOut:滑到 +40px。</summary>
    internal const double SlideOutToX = 40.0;

    /// <summary>【Windows 源】:680-684 PageSwap:旧页竖直 ±8px。</summary>
    internal const double PageSwapY = 8.0;
}

/// <summary>
/// C3e 的测试舞台:一个**真窗口**(动画的时钟来自 TopLevel,控件不入树就永远不推)+
/// 一套"推进一帧"的泵。全部经 <c>AvaloniaSession.Run</c> 调用(D-79),本类只在会话线程上被使用。
/// </summary>
internal sealed class C3eStage : IDisposable
{
    private readonly UiHost _host;

    internal C3eStage()
    {
        Panel = new StackPanel { Width = 420.0, Height = 320.0 };
        _host = UiHost.Show(Panel);
    }

    internal StackPanel Panel { get; }

    internal Window Window => _host.Window;

    /// <summary>挂进真窗口并先推一帧(让控件进可视树、拿到时钟)。</summary>
    internal T Add<T>(T control)
        where T : Control
    {
        Panel.Children.Add(control);
        Frame();
        return control;
    }

    /// <summary>
    /// 推一帧。**先标脏**:本机实测(C3e 探针表)headless 的动画时钟只在**真的产出帧**时前进 ——
    /// 同一条变换动画,每轮标脏时 251ms 跑完;一次都不标就永远停在起点、任务永不结束。
    /// 真桌面由 vsync 驱动,不需要这一步;离屏不推就看不到动画(所以 <c>Motion.Start</c> 里也顺手推了一次)。
    /// </summary>
    internal void Frame()
    {
        Panel.InvalidateVisual();
        UiHost.Flush();
        Thread.Sleep(3);
    }

    /// <summary>泵到条件成立为止(Stopwatch 计时、上限 9 秒 —— 明显小于会话的 30 秒,理由见 D-120)。</summary>
    internal void Pump(Func<bool> until, int timeoutMs = 9000)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!until())
        {
            if (watch.ElapsedMilliseconds >= timeoutMs)
            {
                throw new TimeoutException("C3e 用例等待 " + timeoutMs + " ms 仍未满足条件");
            }

            Frame();
        }
    }

    /// <summary>空推若干帧(给"没动画也该结束"的用例用)。</summary>
    internal void Frames(int count = 8)
    {
        for (int i = 0; i < count; i++)
        {
            Frame();
        }
    }

    public void Dispose()
    {
        _host.Dispose();
    }
}

/// <summary>
/// 盯住一个 double 属性在这段时间里写过的值(最小 / 最大 / 写了几次)。
///
/// **为什么不用"每帧采样"**:动画写值是有事件的,而一帧可能跨过很长的真实时间
/// (机器忙的时候一帧能吞掉整段动画),采样式断言会因此偶发漏掉中间态 —— 本包的进程内执行器
/// 就跑出过一次这样的抖动,所以中间态一律改成事件记账。
/// </summary>
internal sealed class C3eWatcher : IDisposable
{
    private readonly Animatable _target;

    private readonly AvaloniaProperty<double> _property;

    private double _min = double.PositiveInfinity;

    private double _max = double.NegativeInfinity;

    private C3eWatcher(Animatable target, AvaloniaProperty<double> property)
    {
        _target = target;
        _property = property;
        target.PropertyChanged += OnChanged;
    }

    /// <summary>最小写值(一次都没写过是 +∞)。</summary>
    internal double Min => _min;

    /// <summary>最大写值(一次都没写过是 −∞)。</summary>
    internal double Max => _max;

    /// <summary>写了几次(0 = 动画一个字节都没写 ⇒ 静默不动)。</summary>
    internal int Writes { get; private set; }

    internal static C3eWatcher Watch(Animatable target, AvaloniaProperty<double> property)
    {
        return new C3eWatcher(target, property);
    }

    private void OnChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != _property)
        {
            return;
        }

        double value = (double)_target.GetValue(_property)!;
        _min = Math.Min(_min, value);
        _max = Math.Max(_max, value);
        Writes++;
    }

    public void Dispose()
    {
        _target.PropertyChanged -= OnChanged;
    }
}

/// <summary>C3e 的公共小工具:可观测性纪律、取变换实例、终值断言。</summary>
internal static class C3eFiles
{
    /// <summary>
    /// 用例开头统一来一次:清掉 <see cref="Motion.LastError"/> 与计数,并把"还挂着几条"的基线钉住。
    ///
    /// **为什么不能直接断言总数是 0**:<c>Motion.RunningAnimationsSinceReset</c> 是进程级的,别的用例
    /// (C3d 的飞行 chip 在窗口关掉之后还有 4 条收不了尾)会把它一直抬着 —— 本机两条通道的跑法顺序不同,
    /// 直接断言总数会让 VSTest 绿、进程内执行器红(本包实测踩到)。所以只算**本用例窗口里起的**那些。
    /// </summary>
    internal static int Begin()
    {
        Motion.ResetObservability();
        Assert.Null(Motion.LastError);
        Assert.Equal(0, Motion.RunningAnimationsSinceReset);
        // Shine 的 Sweep 守卫会看 Dlg.BlurTarget?.Effect;本类不开对话框,
        // 把它清成 null 让那条路确定性地可验(没有对话框时它本来就该是 null)。
        Dlg.BlurTarget = null;
        return Motion.StartedAnimations;
    }

    /// <summary>用例收尾:真的起过动画 + 一条都没报错 + 本用例起的都收摊了(三条都说完再报错)。</summary>
    internal static void Ended(string what)
    {
        List<string> problems = new List<string>();
        if (Motion.StartedAnimations == 0)
        {
            problems.Add("一条动画都没真的交给 Avalonia(StartedAnimations=0)");
        }

        if (Motion.LastError is not null)
        {
            problems.Add("动画报错:" + Motion.LastError);
        }

        if (Motion.RunningAnimationsSinceReset != 0)
        {
            problems.Add("本用例起的动画还有 " + Motion.RunningAnimationsSinceReset + " 条没结束");
        }

        Assert.True(problems.Count == 0, what + " → " + string.Join(";", problems));
    }

    /// <summary>控件的平移变换(必须是 EnsureTranslate 建出来的那一个)。</summary>
    internal static TranslateTransform Translate(Control el)
    {
        TransformGroup group = Assert.IsType<TransformGroup>(el.RenderTransform);
        return group.Children.OfType<TranslateTransform>().Single();
    }

    /// <summary>控件的缩放变换。</summary>
    internal static ScaleTransform Scale(Control el)
    {
        TransformGroup group = Assert.IsType<TransformGroup>(el.RenderTransform);
        return group.Children.OfType<ScaleTransform>().Single();
    }

    /// <summary>终值断言(浮点给一点余量:插值关键帧落到终点的误差远小于 1e-6)。</summary>
    internal static void Reached(double expected, double actual, string what)
    {
        Assert.True(
            Math.Abs(expected - actual) < 1e-6,
            what + ":期望 " + expected + ",实际 " + actual);
    }

    /// <summary>一个带背景色的 Border(和页面上的卡片同类,能真的被渲染)。</summary>
    internal static Border Card(double width = 120.0, double height = 40.0)
    {
        return new Border { Width = width, Height = height, Background = new SolidColorBrush(Color.FromRgb(200, 60, 60)) };
    }

    /// <summary>
    /// 只用一次的双帧动画(可观测性那几条用):故意选一个**没有动画器**的属性,
    /// 让 Avalonia 同步抛 <c>InvalidOperationException: No animator registered ...</c>。
    /// </summary>
    internal static Animation BrokenAnimation()
    {
        return new Animation
        {
            Duration = TimeSpan.FromMilliseconds(50.0),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(TextBlock.TextProperty, "a") } },
                new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(TextBlock.TextProperty, "b") } },
            },
        };
    }

    /// <summary>一条正常的两帧不透明度动画(可观测性用例里当"好动画"用)。</summary>
    internal static Animation GoodAnimation(double ms = 60.0)
    {
        return new Animation
        {
            Duration = TimeSpan.FromMilliseconds(ms),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(Visual.OpacityProperty, 0.0) } },
                new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
            },
        };
    }

    /// <summary>【本机实测】目标类型必须**不是** Transform —— 那正是本包修掉的那条错路(D-132)。</summary>
    internal static void TargetMustNotBeATransform(string what)
    {
        string? target = Motion.LastTargetType;
        Assert.False(string.IsNullOrEmpty(target), what + ":没有记到目标类型");
        Assert.DoesNotContain("Transform", target!, StringComparison.Ordinal);
    }

    /// <summary>
    /// 按名字跑一次受影响的动画(回归钉子用,**只负责起**):每个分支都在真窗口里建自己的控件、
    /// 挂好该挂的变换、调那一个 <c>Motion</c> 成员。终态与收尾由调用方泵。
    /// </summary>
    internal static void RunAffected(string which, C3eStage stage)
    {
        switch (which)
        {
            case "FadeIn":
                Motion.FadeIn(stage.Add(Card()), C3eExpected.FadeInMs, C3eExpected.FadeInFromY);
                break;
            case "FadeInDelay":
                Motion.FadeIn(stage.Add(Card()), C3eExpected.FadeInMs, C3eExpected.FadeInFromY, 120.0);
                break;
            case "HoverLift":
                {
                    Border hover = stage.Add(Card());
                    Motion.HoverLift(hover, C3eExpected.HoverScale, C3eExpected.HoverLiftPx);
                    C2c1Input.Enter(hover);
                    break;
                }
            case "Pulse":
                Motion.Pulse(stage.Add(Card()), C3eExpected.PulsePeak);
                break;
            case "Shake":
                Motion.Shake(stage.Add(Card()));
                break;
            case "Shine":
                {
                    Border layer = Card(60.0, 20.0);
                    stage.Add(layer);
                    Motion.Shine(layer, new TranslateTransform(-2.0, 0.0), 800.0);
                    break;
                }
            case "Spin":
                {
                    Border owner = stage.Add(Card(24.0, 24.0));
                    RotateTransform rot = new RotateTransform();
                    owner.RenderTransform = rot;
                    Motion.Spin(owner, rot, 0.08);
                    stage.Pump(() => Motion.RunningAnimationsSinceReset >= 1);
                    // Spin 是无限的,不起停就永远"收不了摊";停法与生产一致(Loop 的 stop 分支)。
                    Motion.Enabled = false;
                    break;
                }
            case "PopIn":
                Motion.PopIn(stage.Add(Card()));
                break;
            case "PopOut":
                Motion.PopOut(stage.Add(Card()), delegate { });
                break;
            case "SlideIn":
                Motion.SlideIn(stage.Add(Card()), C3eExpected.SlideInFromX);
                break;
            case "SlideOut":
                Motion.SlideOut(stage.Add(Card()), delegate { }, C3eExpected.SlideOutToX);
                break;
            case "PageSwap":
                {
                    ContentControl host = stage.Add(new ContentControl());
                    host.Content = Card();
                    stage.Frame();
                    Motion.PageSwap(host, Card(), true);
                    break;
                }
            case "Stagger":
                {
                    StackPanel panel = stage.Add(new StackPanel());
                    panel.Children.Add(Card());
                    panel.Children.Add(Card());
                    stage.Frame();
                    Motion.Stagger(panel, 20.0, 120.0, 8.0);
                    break;
                }
            case "StaggerItems":
                {
                    List<Control> items = new List<Control>();
                    items.Add(stage.Add(Card()));
                    items.Add(stage.Add(Card()));
                    Motion.StaggerItems(items, 20.0, 120.0, 8.0);
                    break;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(which), which, "未收录的动画名");
        }
    }
}
