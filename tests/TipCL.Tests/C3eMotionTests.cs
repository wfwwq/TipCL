using System;
using System.Collections.Generic;
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
/// C3e:<see cref="Motion"/> 里**以变换属性为目标的动画**必须在真桌面上真的跑。
///
/// 为什么要有这一包:这些动画以前把 <c>Transform</c> 对象当 <c>Animation.RunAsync</c> 的目标,
/// Avalonia 的 <c>TransformAnimator</c> 会同步抛 <c>InvalidCastException</c>,又被 <c>Motion.Start</c> 吞掉 ⇒
/// 离屏(<c>Motion.Enabled</c> 恒假)走不到那一步,于是**测试全绿、真桌面上静默不动**(D-130 / D-132)。
/// 本文件的每一条用例都在 <see cref="C3dClock"/>(D-129)里把"动画开着"那一支打开,
/// 并且**断言终值**(不只是"没抛异常")—— 那是本包唯一有意义的验收方式。
///
/// 驱动纪律(全部经 <c>AvaloniaSession.Run</c>,D-79):
/// - 控件必须挂进真窗口(动画的时钟来自 TopLevel);离屏的动画时钟只在**真的产出帧**时前进,
///   所以 <see cref="C3eStage.Frame"/> 每轮先 <c>InvalidateVisual</c>(本机实测,见 PROGRESS 的 C3e 探针表);
/// - 等待一律"一边泵一边看条件"(Stopwatch + 9 秒上限,D-120),不模拟真实输入(<c>C2c1Input</c> 走 RaiseEvent)。
/// </summary>
public class C3eMotionTests
{
    // ------------------------------------------------------------------
    // 组 A:每个受影响的动画 —— 真的跑、不抛、LastError 为 null、**终值到位**
    // ------------------------------------------------------------------

    /// <summary>【Windows 源】:218-232 FadeIn:不透明度 0→1(220ms)+ 竖直位移 fromY→0(280ms,CubicEaseOut)。</summary>
    [Fact]
    public void FadeIn_fades_in_and_slides_up_to_the_end_state()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            Border el = stage.Add(C3eFiles.Card());

            Motion.FadeIn(el, C3eExpected.FadeInMs, C3eExpected.FadeInFromY);

            TranslateTransform move = C3eFiles.Translate(el);
            Assert.Equal(0.0, el.Opacity);
            Assert.Equal(C3eExpected.FadeInFromY, move.Y);

            stage.Pump(() => el.Opacity >= 1.0 && Math.Abs(move.Y) < 1e-6);

            C3eFiles.Reached(1.0, el.Opacity, "FadeIn 的不透明度");
            C3eFiles.Reached(0.0, move.Y, "FadeIn 的竖直位移");
            C3eFiles.TargetMustNotBeATransform("FadeIn");
            C3eFiles.Ended("FadeIn");
        });
    }

    /// <summary>【本机实测】FadeIn 只往已有的 TransformGroup 里补一个 TranslateTransform,不换掉那个组。</summary>
    [Fact]
    public void FadeIn_only_adds_a_translate_to_the_existing_transform_group()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            ScaleTransform scale = new ScaleTransform();
            TransformGroup group = new TransformGroup { Children = { scale } };
            Border el = C3eFiles.Card();
            el.RenderTransform = group;
            stage.Add(el);

            Motion.FadeIn(el, C3eExpected.FadeInMs, C3eExpected.FadeInFromY);
            stage.Pump(() => el.Opacity >= 1.0 && C3eFiles.Translate(el).Y == 0.0);

            Assert.Same(group, el.RenderTransform);
            Assert.Equal(2, group.Children.Count);
            Assert.Same(scale, group.Children[0]);
            C3eFiles.Ended("FadeIn(已有缩放组)");
        });
    }

    /// <summary>
    /// 【Windows 源】:229-235 的 <c>BeginTime</c> ⇒ mac 用 <c>DispatcherTimer</c> 推迟。
    /// 【本机实测】离屏的 <c>DispatcherTimer</c> 在"每轮 sleep 3~5ms"的泵里**会** tick(80ms 的定时器 60 帧后 tick 了 9 次),
    /// 所以延时那条路也能验:延时期间没有任何动画在跑,延时之后照样落终态。
    /// (D-129 记的"一次都不 Tick"是另一种泵法 —— 泵里不 sleep 时真实时间没走够,见 PROGRESS C3e 的更正。)
    /// </summary>
    [Fact]
    public void FadeIn_with_a_delay_starts_nothing_until_the_delay_elapsed_then_lands()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            Border el = C3eFiles.Card();
            el.Opacity = 0.4;
            stage.Add(el);

            Motion.FadeIn(el, C3eExpected.FadeInMs, C3eExpected.FadeInFromY, 300.0);

            Assert.Equal(0.4, el.Opacity);
            Assert.Null(el.RenderTransform);
            Assert.Equal(0, Motion.RunningAnimationsSinceReset);
            Assert.Equal(0, Motion.StartedAnimations);

            stage.Pump(() => el.Opacity >= 1.0 && el.RenderTransform is not null && C3eFiles.Translate(el).Y == 0.0);
            C3eFiles.Reached(1.0, el.Opacity, "延时 FadeIn 的不透明度");
            C3eFiles.Reached(0.0, C3eFiles.Translate(el).Y, "延时 FadeIn 的竖直位移");
            C3eFiles.Ended("延时 FadeIn");
        });
    }

    /// <summary>【Windows 源】:299-356 HoverLift 的进入分支:缩放 1.012 + 抬升 2px(170ms,CubicEaseOut)。</summary>
    [Fact]
    public void HoverLift_scales_and_lifts_on_pointer_enter()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            Border hover = stage.Add(C3eFiles.Card());
            Motion.HoverLift(hover, C3eExpected.HoverScale, C3eExpected.HoverLiftPx);

            C2c1Input.Enter(hover);
            stage.Pump(() =>
            {
                ScaleTransform sc = C3eFiles.Scale(hover);
                return Math.Abs(sc.ScaleX - C3eExpected.HoverScale) < 1e-9
                    && Math.Abs(sc.ScaleY - C3eExpected.HoverScale) < 1e-9
                    && Math.Abs(C3eFiles.Translate(hover).Y + C3eExpected.HoverLiftPx) < 1e-9;
            });

            C3eFiles.Reached(C3eExpected.HoverScale, C3eFiles.Scale(hover).ScaleX, "悬停的 ScaleX");
            C3eFiles.Reached(C3eExpected.HoverScale, C3eFiles.Scale(hover).ScaleY, "悬停的 ScaleY");
            C3eFiles.Reached(-C3eExpected.HoverLiftPx, C3eFiles.Translate(hover).Y, "悬停的抬升");
            C3eFiles.TargetMustNotBeATransform("HoverLift");
            C3eFiles.Ended("HoverLift 进入");
        });
    }

    /// <summary>【Windows 源】:347-366 HoverLift 的离开分支:缩放回 1.0、位移回 0(190ms)。</summary>
    [Fact]
    public void HoverLift_returns_to_rest_on_pointer_exit()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            Border hover = stage.Add(C3eFiles.Card());
            Motion.HoverLift(hover, C3eExpected.HoverScale, C3eExpected.HoverLiftPx);

            C2c1Input.Enter(hover);
            stage.Pump(() => Math.Abs(C3eFiles.Translate(hover).Y + C3eExpected.HoverLiftPx) < 1e-9);
            C2c1Input.Leave(hover);
            stage.Pump(() =>
            {
                ScaleTransform sc = C3eFiles.Scale(hover);
                return Math.Abs(sc.ScaleX - 1.0) < 1e-9 && Math.Abs(C3eFiles.Translate(hover).Y) < 1e-9;
            });

            C3eFiles.Reached(1.0, C3eFiles.Scale(hover).ScaleX, "离开后的 ScaleX");
            C3eFiles.Reached(1.0, C3eFiles.Scale(hover).ScaleY, "离开后的 ScaleY");
            C3eFiles.Reached(0.0, C3eFiles.Translate(hover).Y, "离开后的位移");
            C3eFiles.Ended("HoverLift 离开");
        });
    }

    /// <summary>【Windows 源】:321-346 悬停时给 Border 挂上 <c>BoxShadow</c>(blur 22 / offsetY 3 / alpha 41)。</summary>
    [Fact]
    public void HoverLift_adds_the_box_shadow_on_enter()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            Border hover = stage.Add(C3eFiles.Card());
            Motion.HoverLift(hover, C3eExpected.HoverScale, C3eExpected.HoverLiftPx, 22.0);

            C2c1Input.Enter(hover);

            Assert.Equal(1, hover.BoxShadow.Count);
            Assert.Equal(22.0, hover.BoxShadow[0].Blur);
            Assert.Equal(3.0, hover.BoxShadow[0].OffsetY);
            Assert.Equal((byte)41, hover.BoxShadow[0].Color.A);
            stage.Pump(() => Math.Abs(C3eFiles.Translate(hover).Y + C3eExpected.HoverLiftPx) < 1e-9);
            C3eFiles.Ended("HoverLift 阴影");
        });
    }

    /// <summary>【Windows 源】:391-399 Pulse:三条关键帧 <c>1 → 1.06 → 1</c>(280ms),先见到峰值、最后回到 1。</summary>
    [Fact]
    public void Pulse_reaches_its_peak_then_returns_to_one()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            Border el = stage.Add(C3eFiles.Card());

            Motion.Pulse(el, C3eExpected.PulsePeak);

            ScaleTransform scale = C3eFiles.Scale(el);
            double peak = 1.0;
            stage.Pump(() =>
            {
                peak = Math.Max(peak, scale.ScaleX);
                return Math.Abs(scale.ScaleX - 1.0) < 1e-9 && Motion.RunningAnimationsSinceReset == 0;
            });

            Assert.True(peak > 1.0, "Pulse 应当真的鼓起来过,实际峰值 " + peak);
            Assert.True(peak <= C3eExpected.PulsePeak + 1e-9, "峰值不该超过 1.06,实际 " + peak);
            C3eFiles.Reached(1.0, scale.ScaleX, "Pulse 结束时的 ScaleX");
            C3eFiles.Reached(1.0, scale.ScaleY, "Pulse 结束时的 ScaleY");
            C3eFiles.TargetMustNotBeATransform("Pulse");
            C3eFiles.Ended("Pulse");
        });
    }

    /// <summary>【Windows 源】:407-417 Shake:六个关键帧 −7 / 6 / −4 / 3 / −1.5 / 0(330ms),最后回到 0。</summary>
    [Fact]
    public void Shake_wobbles_then_returns_to_zero()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            Border el = stage.Add(C3eFiles.Card());

            Motion.Shake(el);

            TranslateTransform move = C3eFiles.Translate(el);
            bool wobbled = false;
            stage.Pump(() =>
            {
                if (Math.Abs(move.X) > 0.5)
                {
                    wobbled = true;
                }

                return Math.Abs(move.X) < 1e-9 && Motion.RunningAnimationsSinceReset == 0;
            });

            Assert.True(wobbled, "Shake 应当真的抖过(途中 X 离开过 0.5 以上)");
            C3eFiles.Reached(0.0, move.X, "Shake 结束时的 X");
            C3eFiles.TargetMustNotBeATransform("Shake");
            C3eFiles.Ended("Shake");
        });
    }

    /// <summary>
    /// 【Windows 源】:440-460 Shine:横扫 −(宽+40) → 父宽+40(1600ms,CubicEaseInOut),
    /// 同时透明度四帧 <c>0 / 0.5 / 0.5 / 0</c>。
    /// 【本机实测】离屏窗口的 <c>IsActive</c> 是 true,所以 <c>Sweep</c> 的守卫过得去。
    /// </summary>
    [Fact]
    public void Shine_sweeps_the_layer_across_and_fades_it_out()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            Control? previousBlur = Dlg.BlurTarget;
            Dlg.BlurTarget = null;
            try
            {
                Border layer = C3eFiles.Card(60.0, 20.0);
                stage.Add(layer);
                TranslateTransform tx = new TranslateTransform(-2.0, 0.0);
                double expectedEnd = stage.Panel.Bounds.Width + C3eExpected.ShineLeadIn;

                Motion.Shine(layer, tx, 800.0);
                Assert.Same(tx, layer.RenderTransform);

                stage.Pump(() => Math.Abs(tx.X - expectedEnd) < 1e-6 && Motion.RunningAnimationsSinceReset == 0);

                C3eFiles.Reached(expectedEnd, tx.X, "Shine 横扫的终点");
                C3eFiles.Reached(0.0, tx.Y, "Shine 不该动竖直位移");
                C3eFiles.Reached(0.0, layer.Opacity, "Shine 结束时的透明度");
                C3eFiles.TargetMustNotBeATransform("Shine");
                C3eFiles.Ended("Shine");
            }
            finally
            {
                Dlg.BlurTarget = previousBlur;
            }
        });
    }

    /// <summary>
    /// 【Windows 源】:467-477 Spin:每圈 1 秒 0°→360°,无限循环;停掉时归零。
    /// 【C3e】<c>IterationCount.Infinite</c> + <c>RunAsync</c> 在 Avalonia 11 上必抛
    /// <c>"Looping animations must not use the Run method."</c>(本机探针),所以改成"一圈接一圈"。
    /// 这里验三件事:角度真的在走、圈数真的在续、停掉之后归零且没有残留。
    /// </summary>
    [Fact]
    public void Spin_keeps_turning_across_turns_and_stops_at_zero()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            int before = C3eFiles.Begin();
            Border owner = stage.Add(C3eFiles.Card(24.0, 24.0));
            RotateTransform rot = new RotateTransform();
            owner.RenderTransform = rot;

            Motion.Spin(owner, rot, 0.08);

            Assert.True(Motion.RunningAnimationsSinceReset >= 1, "Spin 起来之后应当有一条动画在跑");
            stage.Pump(() => rot.Angle > 1.0);
            // 至少转完两圈:证明"一圈跑完接着下一圈"这条重启路径真的接上了
            stage.Pump(() => Motion.StartedAnimations - before >= 3);
            Assert.True(rot.Angle >= 0.0 && rot.Angle <= 360.0 + 1e-6, "角度应当在 0..360 之间,实际 " + rot.Angle);

            // 停掉:Windows 那边是 BeginAnimation(AngleProperty, null);mac 走 Loop 的 stop 分支
            Motion.Enabled = false;
            stage.Pump(() => Motion.RunningAnimationsSinceReset == 0);

            C3eFiles.Reached(0.0, rot.Angle, "Spin 停掉后的角度");
            C3eFiles.TargetMustNotBeATransform("Spin");
            C3eFiles.Ended("Spin");
        });
    }

    /// <summary>【Windows 源】:581-594 PopIn:缩放 0.96→1.0 + 不透明度 0→1。</summary>
    [Fact]
    public void PopIn_grows_and_fades_in()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            Border el = stage.Add(C3eFiles.Card());

            Motion.PopIn(el);

            ScaleTransform scale = C3eFiles.Scale(el);
            Assert.Equal(C3eExpected.PopInFrom, scale.ScaleX);
            Assert.Equal(0.0, el.Opacity);
            stage.Pump(() =>
                Math.Abs(scale.ScaleX - 1.0) < 1e-9 && Math.Abs(scale.ScaleY - 1.0) < 1e-9 && el.Opacity >= 1.0);

            C3eFiles.Reached(1.0, scale.ScaleX, "PopIn 的 ScaleX");
            C3eFiles.Reached(1.0, scale.ScaleY, "PopIn 的 ScaleY");
            C3eFiles.Reached(1.0, el.Opacity, "PopIn 的不透明度");
            C3eFiles.TargetMustNotBeATransform("PopIn");
            C3eFiles.Ended("PopIn");
        });
    }

    /// <summary>【Windows 源】:607-616 PopOut:缩放缩到 0.97 + 不透明度到 0,然后调 <c>done</c>。</summary>
    [Fact]
    public void PopOut_shrinks_fades_out_and_runs_done()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            Border el = stage.Add(C3eFiles.Card());
            bool done = false;

            Motion.PopOut(el, () => done = true);

            ScaleTransform scale = C3eFiles.Scale(el);
            stage.Pump(() => done && Math.Abs(scale.ScaleX - C3eExpected.PopOutTo) < 1e-9 && el.Opacity <= 1e-9);

            C3eFiles.Reached(C3eExpected.PopOutTo, scale.ScaleX, "PopOut 的 ScaleX");
            C3eFiles.Reached(C3eExpected.PopOutTo, scale.ScaleY, "PopOut 的 ScaleY");
            C3eFiles.Reached(0.0, el.Opacity, "PopOut 的不透明度");
            C3eFiles.TargetMustNotBeATransform("PopOut");
            C3eFiles.Ended("PopOut");
        });
    }

    /// <summary>【Windows 源】:629-636 SlideIn:从 +40px 滑进来 + 不透明度 0→1。</summary>
    [Fact]
    public void SlideIn_slides_in_from_the_right()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            Border el = stage.Add(C3eFiles.Card());

            Motion.SlideIn(el, C3eExpected.SlideInFromX);

            TranslateTransform move = C3eFiles.Translate(el);
            Assert.Equal(C3eExpected.SlideInFromX, move.X);
            Assert.Equal(0.0, el.Opacity);
            stage.Pump(() => Math.Abs(move.X) < 1e-9 && el.Opacity >= 1.0);

            C3eFiles.Reached(0.0, move.X, "SlideIn 的 X");
            C3eFiles.Reached(0.0, move.Y, "SlideIn 不该动竖直位移");
            C3eFiles.Reached(1.0, el.Opacity, "SlideIn 的不透明度");
            C3eFiles.TargetMustNotBeATransform("SlideIn");
            C3eFiles.Ended("SlideIn");
        });
    }

    /// <summary>【Windows 源】:649-653 SlideOut:滑到 +40px + 淡到 0,然后调 <c>done</c>。</summary>
    [Fact]
    public void SlideOut_slides_out_to_the_right_and_runs_done()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            Border el = stage.Add(C3eFiles.Card());
            bool done = false;

            Motion.SlideOut(el, () => done = true, C3eExpected.SlideOutToX);

            TranslateTransform move = C3eFiles.Translate(el);
            stage.Pump(() => done && Math.Abs(move.X - C3eExpected.SlideOutToX) < 1e-9 && el.Opacity <= 1e-9);

            C3eFiles.Reached(C3eExpected.SlideOutToX, move.X, "SlideOut 的 X");
            C3eFiles.Reached(0.0, el.Opacity, "SlideOut 的不透明度");
            C3eFiles.TargetMustNotBeATransform("SlideOut");
            C3eFiles.Ended("SlideOut");
        });
    }

    /// <summary>
    /// 【Windows 源】:680-690 PageSwap:旧页滑出(竖直 −8px)+ 淡出,收尾里 <c>Reset</c> 旧页、
    /// 换内容、再给新页 <c>FadeIn(250, +14)</c>。
    /// 这里分两段验:先见到旧页真的位移过(证明动画在跑),再验换页与复位。
    /// </summary>
    [Fact]
    public void PageSwap_moves_the_old_page_out_then_swaps_and_fades_the_new_one_in()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            ContentControl host = stage.Add(new ContentControl());
            Border first = C3eFiles.Card();
            host.Content = first;
            stage.Frame();
            Border second = C3eFiles.Card();

            Motion.PageSwap(host, second, true);

            TranslateTransform move = C3eFiles.Translate(first);
            bool movedOut = false;
            stage.Pump(() =>
            {
                if (move.Y < -1.0)
                {
                    movedOut = true;
                }

                return ReferenceEquals(host.Content, second);
            });

            Assert.True(movedOut, "旧页应当先向上滑出去(Y 到过 −1 以下),实际 " + move.Y);
            C3eFiles.Reached(1.0, first.Opacity, "旧页被 Reset 回了不透明度 1");
            C3eFiles.Reached(0.0, move.Y, "旧页被 Reset 回了位移 0");

            stage.Pump(() => second.Opacity >= 1.0 && C3eFiles.Translate(second).Y == 0.0);
            C3eFiles.Reached(1.0, second.Opacity, "新页的不透明度");
            C3eFiles.Reached(0.0, C3eFiles.Translate(second).Y, "新页的位移");
            C3eFiles.TargetMustNotBeATransform("PageSwap");
            C3eFiles.Ended("PageSwap");
        });
    }

    /// <summary>【Windows 源】:680 <c>forward ? -8 : 8</c>:后退方向的旧页向下滑出。</summary>
    [Fact]
    public void PageSwap_backward_uses_the_positive_offset()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            ContentControl host = stage.Add(new ContentControl());
            Border first = C3eFiles.Card();
            host.Content = first;
            stage.Frame();
            Border second = C3eFiles.Card();

            Motion.PageSwap(host, second, false);

            TranslateTransform move = C3eFiles.Translate(first);
            bool movedDown = false;
            stage.Pump(() =>
            {
                if (move.Y > 1.0)
                {
                    movedDown = true;
                }

                return ReferenceEquals(host.Content, second);
            });

            Assert.True(movedDown, "后退时旧页应当向下滑出去(Y 到过 +1 以上),实际 " + move.Y);
            // 换页之后新页还要 FadeIn(不透明度 250ms + 位移 310ms):等它整条收摊再断言
            stage.Pump(() => second.Opacity >= 1.0
                && C3eFiles.Translate(second).Y == 0.0
                && Motion.RunningAnimationsSinceReset == 0);
            C3eFiles.Reached(1.0, second.Opacity, "后退时新页的不透明度");
            C3eFiles.Reached(0.0, C3eFiles.Translate(second).Y, "后退时新页的位移");
            C3eFiles.Ended("PageSwap 后退");
        });
    }

    /// <summary>【Windows 源】:263-280 附近的 Stagger:逐个 FadeIn(步长 26ms,上限 320ms)—— 间接走 FadeIn 的位移。</summary>
    [Fact]
    public void Stagger_fades_every_child_in_including_the_delayed_ones()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            StackPanel host = stage.Add(new StackPanel());
            List<Border> children = new List<Border>();
            for (int i = 0; i < 4; i++)
            {
                Border child = C3eFiles.Card();
                children.Add(child);
                host.Children.Add(child);
            }

            stage.Frame();
            Motion.Stagger(host, 20.0, 120.0, 8.0);

            stage.Pump(() => children.All(c =>
                c.RenderTransform is not null &&
                Math.Abs(c.Opacity - 1.0) < 1e-9 &&
                C3eFiles.Translate(c).Y == 0.0));

            Assert.All(children, c => C3eFiles.Reached(0.0, C3eFiles.Translate(c).Y, "Stagger 子项的位移"));
            Assert.All(children, c => C3eFiles.Reached(1.0, c.Opacity, "Stagger 子项的不透明度"));
            C3eFiles.Ended("Stagger");
        });
    }

    /// <summary>【Windows 源】<c>StaggerItems</c>:与 Stagger 同一套 FadeIn,只是进参是 IEnumerable。</summary>
    [Fact]
    public void StaggerItems_fades_every_item_in()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            List<Border> items = new List<Border>();
            for (int i = 0; i < 3; i++)
            {
                Border item = C3eFiles.Card();
                items.Add(item);
                stage.Add(item);
            }

            Motion.StaggerItems(items, 20.0, 120.0, 8.0);

            stage.Pump(() => items.All(c =>
                c.RenderTransform is not null &&
                Math.Abs(c.Opacity - 1.0) < 1e-9 &&
                C3eFiles.Translate(c).Y == 0.0));

            Assert.All(items, c => C3eFiles.Reached(0.0, C3eFiles.Translate(c).Y, "StaggerItems 子项的位移"));
            C3eFiles.Ended("StaggerItems");
        });
    }

    /// <summary>【Windows 源】:486-497 Progress:动画走 <c>RangeBase.ValueProperty</c>(目标本来就是控件)。基线不回归。</summary>
    [Fact]
    public void Progress_animates_the_value_to_the_target()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            ProgressBar bar = stage.Add(new ProgressBar { Minimum = 0.0, Maximum = 100.0, Value = 0.0 });

            Motion.Progress(bar, 80.0);
            stage.Pump(() => Math.Abs(bar.Value - 80.0) < 1e-9);

            C3eFiles.Reached(80.0, bar.Value, "Progress 的 Value");
            Assert.Equal("RangeBase.Value", Motion.LastPropertyName);
            C3eFiles.Ended("Progress");
        });
    }

    /// <summary>【Windows 源】:540-560 AnimateWidth / AnimateHeight:落到目标尺寸并回调 <c>done</c>。</summary>
    [Fact]
    public void AnimateWidth_and_Height_reach_the_target_and_run_done()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            Border el = stage.Add(C3eFiles.Card(50.0, 50.0));
            bool widthDone = false;
            bool heightDone = false;

            Motion.AnimateWidth(el, 180.0, 80.0, () => widthDone = true);
            Motion.AnimateHeight(el, 90.0, 80.0, () => heightDone = true);

            stage.Pump(() => widthDone && heightDone);
            C3eFiles.Reached(180.0, el.Width, "AnimateWidth 的宽度");
            C3eFiles.Reached(90.0, el.Height, "AnimateHeight 的高度");
            C3eFiles.Ended("AnimateWidth/Height");
        });
    }

    /// <summary>【Windows 源】:498-530 CountUp:16ms 的 DispatcherTimer 逐帧写文字(离屏按真实时间会 tick)。</summary>
    [Fact]
    public void CountUp_ticks_to_the_target_text()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            TextBlock text = stage.Add(new TextBlock { Text = "" });
            bool done = false;

            Motion.CountUp(text, 0.0, 42.0, "0", 200.0, () => done = true);
            stage.Pump(() => done && text.Text == "42");

            Assert.Equal("42", text.Text);
            C3eFiles.Reached(0.0, Motion.RunningAnimationsSinceReset, "CountUp 不该留动画(它走定时器)");
        });
    }

    // ------------------------------------------------------------------
    // 组 B:关掉动画 —— 什么都不建、什么都不起,终态与 Windows 关掉动画时一致
    // ------------------------------------------------------------------

    /// <summary>【Windows 源】:216-224 关掉动画时 FadeIn 直接落 opacity=1 且**不建** RenderTransform。</summary>
    [Fact]
    public void FadeIn_disabled_lands_immediately_and_starts_nothing()
    {
        AvaloniaSession.Run(() =>
        {
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            Border el = stage.Add(new Border { Opacity = 0.0 });

            Motion.FadeIn(el);

            C3eFiles.Reached(1.0, el.Opacity, "关掉动画时的不透明度");
            Assert.Null(el.RenderTransform);
            Assert.Equal(0, Motion.StartedAnimations);
            Assert.Equal(0, Motion.RunningAnimationsSinceReset);
            Assert.Null(Motion.LastError);
        });
    }

    /// <summary>【Windows 源】:581-636 PopIn / SlideIn 关掉动画时只落 opacity=1。</summary>
    [Fact]
    public void PopIn_and_SlideIn_disabled_land_on_opacity_one_and_start_nothing()
    {
        AvaloniaSession.Run(() =>
        {
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            Border pop = stage.Add(new Border { Opacity = 0.0 });
            Border slide = stage.Add(new Border { Opacity = 0.0 });

            Motion.PopIn(pop);
            Motion.SlideIn(slide);

            C3eFiles.Reached(1.0, pop.Opacity, "PopIn 关掉动画时的不透明度");
            C3eFiles.Reached(1.0, slide.Opacity, "SlideIn 关掉动画时的不透明度");
            Assert.Equal(0, Motion.StartedAnimations);
            Assert.Null(Motion.LastError);
        });
    }

    /// <summary>【Windows 源】:388-419 Pulse / Shake 关掉动画时把终态(1.0 / 0.0)写进变换。</summary>
    [Fact]
    public void Pulse_and_Shake_disabled_write_their_end_values_without_animating()
    {
        AvaloniaSession.Run(() =>
        {
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            Border el = stage.Add(new Border());

            Motion.Pulse(el, C3eExpected.PulsePeak);
            ScaleTransform scale = C3eFiles.Scale(el);
            C3eFiles.Reached(1.0, scale.ScaleX, "关掉动画时 Pulse 的 ScaleX");

            Motion.Shake(el);
            C3eFiles.Reached(0.0, C3eFiles.Translate(el).X, "关掉动画时 Shake 的 X");
            Assert.Equal(0, Motion.StartedAnimations);
            Assert.Null(Motion.LastError);
        });
    }

    /// <summary>【Windows 源】:302-306 HoverLift 的进入分支自己判 <c>Enabled</c>:关掉时连阴影都不挂。</summary>
    [Fact]
    public void HoverLift_disabled_starts_nothing_on_hover()
    {
        AvaloniaSession.Run(() =>
        {
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            Border hover = stage.Add(new Border());
            Motion.HoverLift(hover);

            C2c1Input.Enter(hover);
            stage.Frames(4);

            Assert.True(hover.BoxShadow.Equals(default(BoxShadows)));
            Assert.Equal(0, Motion.StartedAnimations);
            Assert.Null(Motion.LastError);
        });
    }

    /// <summary>【Windows 源】:421-434 / 467-477 Shine 与 Spin 都靠 <c>Loop</c>,关掉动画时 start 不会被调。</summary>
    [Fact]
    public void Shine_and_Spin_disabled_start_nothing()
    {
        AvaloniaSession.Run(() =>
        {
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            Border layer = stage.Add(C3eFiles.Card(60.0, 20.0));
            TranslateTransform tx = new TranslateTransform(-2.0, 0.0);
            Motion.Shine(layer, tx, 800.0);
            Assert.Same(tx, layer.RenderTransform);
            C3eFiles.Reached(0.0, layer.Opacity, "关掉动画时 Shine 的透明度");

            Border owner = stage.Add(C3eFiles.Card(24.0, 24.0));
            RotateTransform rot = new RotateTransform();
            owner.RenderTransform = rot;
            Motion.Spin(owner, rot, 1.0);
            stage.Frames(4);

            C3eFiles.Reached(0.0, rot.Angle, "关掉动画时 Spin 的角度");
            Assert.Equal(0, Motion.StartedAnimations);
            Assert.Null(Motion.LastError);
        });
    }

    /// <summary>【Windows 源】:674-690 PageSwap 关掉动画时直接换内容并复位旧页。</summary>
    [Fact]
    public void PageSwap_disabled_swaps_and_resets_without_animating()
    {
        AvaloniaSession.Run(() =>
        {
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            ContentControl host = stage.Add(new ContentControl());
            Border first = new Border { Opacity = 0.4 };
            Border second = new Border { Opacity = 0.4 };
            host.Content = first;
            stage.Frame();

            Motion.PageSwap(host, second, true);

            Assert.Same(second, host.Content);
            C3eFiles.Reached(1.0, second.Opacity, "新页关掉动画时的不透明度");
            C3eFiles.Reached(1.0, first.Opacity, "旧页被复位的不透明度");
            Assert.Equal(0, Motion.StartedAnimations);
            Assert.Null(Motion.LastError);
        });
    }

    /// <summary>
    /// 【任务书】"假装有时钟"(<see cref="Motion.ClockOverride"/>)但**用户开关是关的**时不许起动画:
    /// <c>Enabled</c> = 用户开关 **且** 有时钟,两个条件缺一不可。
    /// </summary>
    [Fact]
    public void User_switch_off_keeps_the_clock_override_from_starting_animations()
    {
        AvaloniaSession.Run(() =>
        {
            bool? previousClock = Motion.ClockOverride;
            bool previousUser = Motion.UserEnabled;
            try
            {
                Motion.ClockOverride = true;
                Motion.Enabled = false;
                using C3eStage stage = new C3eStage();
                C3eFiles.Begin();
                Border el = stage.Add(new Border { Opacity = 0.0 });

                Motion.FadeIn(el);

                Assert.False(Motion.Enabled);
                C3eFiles.Reached(1.0, el.Opacity, "用户关掉动画时的不透明度");
                Assert.Null(el.RenderTransform);
                Assert.Equal(0, Motion.StartedAnimations);
                Assert.Null(Motion.LastError);
            }
            finally
            {
                Motion.Enabled = previousUser;
                Motion.ClockOverride = previousClock;
            }
        });
    }

    // ------------------------------------------------------------------
    // 组 C:可观测性 —— 失败必须被记下来,而不是被吞掉
    // ------------------------------------------------------------------

    /// <summary>【任务书】故意失败的动画:进 <see cref="Motion.LastError"/>,界面不抛。</summary>
    [Fact]
    public void A_failing_animation_is_recorded_and_does_not_throw()
    {
        AvaloniaSession.Run(() =>
        {
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            TextBlock text = stage.Add(new TextBlock { Text = "原样" });

            // 不抛:装饰性动画失败不许把界面带崩
            Motion.RunForTest(C3eFiles.BrokenAnimation(), text);
            stage.Frames(4);

            Assert.NotNull(Motion.LastError);
            Assert.Contains("animator", Motion.LastError!.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("原样", text.Text);
            Assert.Equal(0, Motion.RunningAnimationsSinceReset);
        });
    }

    /// <summary>【任务书】<see cref="Motion.ResetObservability"/> 清掉记录(用例之间互不污染)。</summary>
    [Fact]
    public void ResetObservability_clears_the_recorded_error()
    {
        AvaloniaSession.Run(() =>
        {
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            TextBlock text = stage.Add(new TextBlock());

            Motion.RunForTest(C3eFiles.BrokenAnimation(), text);
            Assert.NotNull(Motion.LastError);

            Motion.ResetObservability();
            Assert.Null(Motion.LastError);
            Assert.Equal(0, Motion.StartedAnimations);
        });
    }

    /// <summary>【任务书】正常跑完的动画一条错误都不留,而且被记了一条"起过"。基线。</summary>
    [Fact]
    public void A_successful_animation_leaves_no_error_and_counts_one_start()
    {
        AvaloniaSession.Run(() =>
        {
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            Border el = stage.Add(new Border { Opacity = 0.0 });
            bool done = false;

            Motion.RunForTest(C3eFiles.GoodAnimation(), el, () => done = true);
            stage.Pump(() => done);

            Assert.Equal(1, Motion.StartedAnimations);
            Assert.Equal(0, Motion.RunningAnimationsSinceReset);
            Assert.Null(Motion.LastError);
            C3eFiles.Reached(1.0, el.Opacity, "那条不透明度动画的终值");
        });
    }

    /// <summary>
    /// 【C3e 新增的自检】<c>Spin</c> 拿到一个**没挂到控件上**的 RotateTransform 时,
    /// Avalonia 的动画器会静默什么都不做 —— 现在这件事会作为原因记进 <see cref="Motion.LastError"/>。
    /// </summary>
    [Fact]
    public void Spin_with_an_unattached_rotate_transform_records_the_reason()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            Border owner = stage.Add(C3eFiles.Card(24.0, 24.0));
            RotateTransform notAttached = new RotateTransform();

            Motion.Spin(owner, notAttached, 0.08);
            stage.Frames(4);

            Assert.NotNull(Motion.LastError);
            Assert.Contains("RotateTransform", Motion.LastError!.Message, StringComparison.Ordinal);
            C3eFiles.Reached(0.0, notAttached.Angle, "没挂上去的变换不该被转");

            Motion.Enabled = false;
            stage.Frames(4);
        });
    }

    /// <summary>【任务书】一次失败之后,后面的正常动画照样跑(失败只影响那一条)。</summary>
    [Fact]
    public void The_app_keeps_working_after_a_failed_animation()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            int before = C3eFiles.Begin();
            TextBlock text = stage.Add(new TextBlock());
            Motion.RunForTest(C3eFiles.BrokenAnimation(), text);
            Assert.NotNull(Motion.LastError);

            Border el = stage.Add(C3eFiles.Card());
            Motion.FadeIn(el, C3eExpected.FadeInMs, C3eExpected.FadeInFromY);
            stage.Pump(() => el.Opacity >= 1.0 && C3eFiles.Translate(el).Y == 0.0);

            C3eFiles.Reached(1.0, el.Opacity, "失败之后 FadeIn 仍然到位");
            Assert.True(Motion.StartedAnimations - before >= 2, "失败之后仍然起过动画");
            Assert.Equal(0, Motion.RunningAnimationsSinceReset);
        });
    }

    /// <summary>【任务书】变换动画交给 Avalonia 的目标必须是控件,属性名仍是变换自己的属性。</summary>
    [Fact]
    public void A_transform_animation_reports_a_control_target_and_the_transform_property()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            Border el = stage.Add(C3eFiles.Card());

            Motion.FadeIn(el, C3eExpected.FadeInMs, C3eExpected.FadeInFromY);
            stage.Pump(() => C3eFiles.Translate(el).Y == 0.0 && el.Opacity >= 1.0);

            Assert.Equal("TranslateTransform.Y", Motion.LastPropertyName);
            Assert.Contains("Border", Motion.LastTargetType!, StringComparison.Ordinal);
            C3eFiles.TargetMustNotBeATransform("FadeIn");
            C3eFiles.Ended("变换动画的目标");
        });
    }

    // ------------------------------------------------------------------
    // 组 D:回归钉子 —— 逐方法确认"目标不是 Transform",而且跑完不留残留
    // ------------------------------------------------------------------

    /// <summary>
    /// 【任务书】逐个方法钉住:C3e 修掉的那一类错路(把 Transform 当目标)如果被谁改回来,
    /// 这里会红(目标类型里出现 "Transform"),或者干脆编译不过(<c>Motion.Start</c> 只收 <c>Visual</c>)。
    /// </summary>
    [Theory]
    [InlineData("FadeIn")]
    [InlineData("FadeInDelay")]
    [InlineData("HoverLift")]
    [InlineData("Pulse")]
    [InlineData("Shake")]
    [InlineData("Shine")]
    [InlineData("Spin")]
    [InlineData("PopIn")]
    [InlineData("PopOut")]
    [InlineData("SlideIn")]
    [InlineData("SlideOut")]
    [InlineData("PageSwap")]
    [InlineData("Stagger")]
    [InlineData("StaggerItems")]
    public void Every_affected_animation_targets_a_control_and_leaves_nothing_running(string which)
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            int before = C3eFiles.Begin();

            C3eFiles.RunAffected(which, stage);

            stage.Pump(() => Motion.StartedAnimations > before && Motion.RunningAnimationsSinceReset == 0);
            C3eFiles.TargetMustNotBeATransform(which);
            Assert.True(Motion.LastError is null, which + " 报错:" + Motion.LastError);
            C3eFiles.Ended(which);
        });
    }

    // ------------------------------------------------------------------
    // 组 E:泄露 —— 动画结束后不许留任何一条在跑
    // ------------------------------------------------------------------

    /// <summary>【任务书】一批动画都跑完之后,<see cref="Motion.RunningAnimationsSinceReset"/> 必须回到 0。</summary>
    [Fact]
    public void A_batch_of_animations_leaves_no_animation_running()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            using C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            List<Border> cards = new List<Border>();
            for (int i = 0; i < 5; i++)
            {
                Border card = C3eFiles.Card();
                cards.Add(card);
                stage.Add(card);
                Motion.FadeIn(card, C3eExpected.FadeInMs, C3eExpected.FadeInFromY);
            }

            stage.Pump(() => cards.All(c => c.Opacity >= 1.0));
            stage.Frames(6);

            Assert.Equal(0, Motion.RunningAnimationsSinceReset);
            Assert.Null(Motion.LastError);
            Assert.All(cards, c => C3eFiles.Reached(0.0, C3eFiles.Translate(c).Y, "批量 FadeIn 的位移"));
        });
    }

    /// <summary>【Windows 源】:171-190 Spin 的 owner 不在树上时应停掉:关窗之后不许再有一条无限动画在跑。</summary>
    [Fact]
    public void Spin_stops_when_the_window_closes()
    {
        AvaloniaSession.Run(() =>
        {
            using C3dClock clock = new C3dClock();
            C3eStage stage = new C3eStage();
            C3eFiles.Begin();
            try
            {
                Border owner = stage.Add(C3eFiles.Card(24.0, 24.0));
                RotateTransform rot = new RotateTransform();
                owner.RenderTransform = rot;
                Motion.Spin(owner, rot, 0.08);
                stage.Pump(() => rot.Angle > 1.0);
                Assert.True(Motion.RunningAnimationsSinceReset >= 1);
            }
            finally
            {
                stage.Dispose();
            }

            // 关窗 ⇒ Loaded/Unloaded 把 Loop 的 stop 分支推到 Dispatcher 上;这里只推泵、不再有窗口
            for (int i = 0; i < 40 && Motion.RunningAnimationsSinceReset > 0; i++)
            {
                UiHost.Flush();
                Thread.Sleep(3);
            }

            Assert.Equal(0, Motion.RunningAnimationsSinceReset);
            Assert.Null(Motion.LastError);
        });
    }
}