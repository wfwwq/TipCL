using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using TipCL;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C3d(一):外壳 <c>FlyToTasks</c> 的守卫、chip 的构造与几何、620ms 时间线。
///
/// 纪律:Avalonia 用例体全走 <c>AvaloniaSession.Run</c>(D-79);全局静态由 <see cref="C3cScope"/> 快照还原
/// (D-69);"动画开着"那一支靠 <see cref="C3dClock"/> 打开(<c>Motion.ClockOverride</c>,D-129);
/// 等待一律"一边泵 Dispatcher 一边看条件"(<see cref="C3cFiles.Pump"/> / <c>C3bFiles.Pump</c>,D-120)。
/// 每条断言注明来源:【Windows 源】/【任务书】/【本机实测】。
/// </summary>
public class C3dFlyTests
{
    // ---------------------------------------------------------------- 守卫

    /// <summary>
    /// 【任务书 + 本机实测】离屏 / 测试进程里没有动画时钟(D-69/D-98),<c>Motion.Enabled</c> 恒假,
    /// 所以 <c>FlyToTasks</c> **一行都不画** —— <c>--smoke</c> 的帧因此逐位不变。
    /// </summary>
    [Fact]
    public void Nothing_flies_while_motion_is_off()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();
            Assert.Null(Motion.ClockOverride);
            Assert.False(Motion.Enabled, "测试进程里没有动画时钟,Motion.Enabled 必须是假");
            Control source = shell.Window.Sidebar;
            Assert.True(source.IsVisible, "源控件必须可见,否则这条用例验不到守卫");
            Assert.NotNull(shell.Window.TasksNavRow());

            shell.Window.FlyToTasks(source, "飞行");

            Assert.Empty(shell.Window.FlyHostPanel.Children);
        });
    }

    /// <summary>【Windows 源】:1338 <c>if (!Motion.Enabled ...) return;</c> —— 用户自己关掉动画时什么都不画。</summary>
    [Fact]
    public void Nothing_flies_when_the_user_turned_animations_off()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();
            using var clock = new C3dClock();
            Assert.True(clock.On, "时钟打开之后 Motion.Enabled 必须为真,否则这条用例验的不是用户开关");

            Motion.Enabled = false;
            shell.Window.FlyToTasks(shell.Window.Sidebar, "飞行");

            Assert.Empty(shell.Window.FlyHostPanel.Children);
        });
    }

    /// <summary>【Windows 源】:1343 <c>!source.IsVisible → return</c>。</summary>
    [Fact]
    public void Invisible_source_means_no_chip()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();
            using var clock = new C3dClock();
            Control source = shell.Window.Sidebar;
            source.IsVisible = false;

            shell.Window.FlyToTasks(source, "飞行");

            Assert.Empty(shell.Window.FlyHostPanel.Children);
        });
    }

    /// <summary>【Windows 源】:1342-1346 目标(<c>TabTasks</c>)没了就不播 —— mac 侧是"任务行不在侧栏里"。</summary>
    [Fact]
    public void Missing_tasks_row_means_no_chip()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();
            using var clock = new C3dClock();
            using var doc = System.Text.Json.JsonDocument.Parse("""{"ui_nav_hidden":["tasks"]}""");
            shell.Window.ApplyNavSettings(doc.RootElement);
            C3cFiles.Settle();

            Assert.Null(shell.Window.TasksNavRow());
            shell.Window.FlyToTasks(shell.Window.Sidebar, "飞行");

            Assert.Empty(shell.Window.FlyHostPanel.Children);
        });
    }

    /// <summary>【Windows 源】:1343 <c>!target.IsVisible → return</c>。</summary>
    [Fact]
    public void Invisible_tasks_row_means_no_chip()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();
            using var clock = new C3dClock();
            ToggleButton row = Assert.IsType<ToggleButton>(shell.Window.TasksNavRow());
            row.IsVisible = false;

            shell.Window.FlyToTasks(shell.Window.Sidebar, "飞行");

            Assert.Empty(shell.Window.FlyHostPanel.Children);
        });
    }

    /// <summary>
    /// 【Windows 源】:1338 <c>!IsLoaded → return</c> —— Avalonia 11 的 <c>Control.IsLoaded</c> 与 WPF 同义
    /// (本机探针实测:没 Show 过 = 假、Show 之后 = 真、Close 之后 = 假)。
    /// 注意**不能**用 <c>IsAttachedToVisualTree()</c> 当判据:窗口是它自己那棵树的根,没 Show 过它也是真。
    /// </summary>
    [Fact]
    public void Shell_that_was_never_shown_means_no_chip()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var window = new MainWindow(new UiPrefs());
            using var clock = new C3dClock();
            Assert.False(window.IsLoaded, "没 Show 过的窗口不该是 loaded");
            Assert.True(window.IsAttachedToVisualTree(), "窗口本身是可视树的根,所以 attached 是真 —— 这就是不能用它当判据的原因");

            window.FlyToTasks(window.Sidebar, "飞行");

            Assert.Empty(window.FlyHostPanel.Children);
        });
    }

    /// <summary>【Windows 源】:1338 —— 关掉的窗口(<c>IsLoaded</c> 回假)同样不画。</summary>
    [Fact]
    public void Closed_shell_means_no_chip()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var window = new MainWindow(new UiPrefs());
            window.Show();
            UiHost.Flush();
            Assert.True(window.IsLoaded);
            window.Close();
            UiHost.Flush();
            using var clock = new C3dClock();
            Assert.False(window.IsLoaded);

            window.FlyToTasks(window.Sidebar, "飞行");

            Assert.Empty(window.FlyHostPanel.Children);
        });
    }

    /// <summary>【Windows 源】:1349-1357 取不到坐标(<c>TransformToAncestor</c> 抛)就返回 ——
    /// Avalonia 这边是 <c>TranslatePoint</c> 返回 null(源控件不在同一棵可视树里)。</summary>
    [Fact]
    public void Source_outside_the_tree_means_no_chip()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();
            using var clock = new C3dClock();
            var loose = new Border { Width = 40.0, Height = 20.0 };
            Assert.True(loose.IsVisible);

            shell.Window.FlyToTasks(loose, "飞行");

            Assert.Empty(shell.Window.FlyHostPanel.Children);
        });
    }

    /// <summary>【本机实测】mac 侧多加的一条防御:Windows 会直接 NRE,这里安静返回。</summary>
    [Fact]
    public void Null_source_is_ignored()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();
            using var clock = new C3dClock();

            shell.Window.FlyToTasks(null!, "飞行");

            Assert.Empty(shell.Window.FlyHostPanel.Children);
        });
    }

    // ---------------------------------------------------------------- chip 的构造

    /// <summary>【Windows 源】:1358-1366 chip 建在 FlyHost 里;不给文案时用「已加入队列」。</summary>
    [Theory]
    [InlineData("", "已加入队列")]
    [InlineData("修复中", "修复中")]
    [InlineData("导出中", "导出中")]
    public void Chip_carries_the_windows_wording(string text, string expected)
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();
            using var clock = new C3dClock();

            shell.Window.FlyToTasks(shell.Window.Sidebar, text);

            Border chip = C3dFiles.OnlyChip(shell.Window);
            string want = (text.Length == 0) ? I18n.L(C3dExpected.DefaultText) : expected;
            Assert.Equal(want, C3dFiles.ChipText(chip).Text);
            PumpAway(shell.Window);
        });
    }

    /// <summary>【Windows 源】:1364 <c>chip.SetResourceReference(Border.BackgroundProperty, colorKey ?? "B.AccentStrong")</c>。</summary>
    [Theory]
    [InlineData(null, "B.AccentStrong")]
    [InlineData("B.Ink", "B.Ink")]
    public void Chip_background_uses_the_colour_key(string? colorKey, string expectedKey)
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();
            using var clock = new C3dClock();

            shell.Window.FlyToTasks(shell.Window.Sidebar, "飞行", colorKey);

            Border chip = C3dFiles.OnlyChip(shell.Window);
            C3dFiles.AssertBackground(chip, expectedKey);
            PumpAway(shell.Window);
        });
    }

    /// <summary>【Windows 源】:1362 文字是 11.5 号半粗 + <c>"B.OnAccentStrong"</c>。</summary>
    [Fact]
    public void Chip_text_is_11_5_semibold_on_accent_strong()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();
            using var clock = new C3dClock();

            shell.Window.FlyToTasks(shell.Window.Sidebar, "飞行");

            TextBlock text = C3dFiles.ChipText(C3dFiles.OnlyChip(shell.Window));
            Assert.Equal(C3dExpected.FontSize, text.FontSize);
            Assert.Equal(FontWeight.SemiBold, text.FontWeight);
            C3dFiles.AssertTextColour(text, C3dExpected.TextColourKey);
            PumpAway(shell.Window);
        });
    }

    /// <summary>【Windows 源】:1360-1361 <c>CornerRadius 999</c> + <c>Padding (10,4,10,5)</c>。</summary>
    [Fact]
    public void Chip_is_a_999_pill_with_the_windows_padding()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();
            using var clock = new C3dClock();

            shell.Window.FlyToTasks(shell.Window.Sidebar, "飞行");

            Border chip = C3dFiles.OnlyChip(shell.Window);
            Assert.Equal(new CornerRadius(C3dExpected.PillRadius), chip.CornerRadius);
            Assert.Equal(C3dExpected.ChipPadding, chip.Padding);
            PumpAway(shell.Window);
        });
    }

    /// <summary>【Windows 源】:1365 <c>Motion.Shadow(chip, 16.0, 0.3, 3.0)</c>(Avalonia 侧是 <c>BoxShadow</c>,见 C2a 差异表)。</summary>
    [Fact]
    public void Chip_has_the_windows_shadow()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();
            using var clock = new C3dClock();

            shell.Window.FlyToTasks(shell.Window.Sidebar, "飞行");

            Border chip = C3dFiles.OnlyChip(shell.Window);
            Assert.Equal(1, chip.BoxShadow.Count);
            Assert.Equal(0.0, chip.BoxShadow[0].OffsetX);
            Assert.Equal(C3dExpected.ShadowDepth, chip.BoxShadow[0].OffsetY);
            Assert.Equal(C3dExpected.ShadowBlur, chip.BoxShadow[0].Blur);
            Assert.Equal((byte)Math.Round(C3dExpected.ShadowOpacity * 255.0), chip.BoxShadow[0].Color.A);
            Assert.Equal(Color.FromArgb((byte)Math.Round(C3dExpected.ShadowOpacity * 255.0), 20, 20, 20), chip.BoxShadow[0].Color);
            PumpAway(shell.Window);
        });
    }

    /// <summary>【Windows 源】:1367-1371 量 500×60、按 <c>DesiredSize</c> 把 chip 居中到源控件中心。</summary>
    [Fact]
    public void Chip_starts_centred_on_the_source()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();
            using var clock = new C3dClock();

            shell.Window.FlyToTasks(shell.Window.Sidebar, "飞行");

            Border chip = C3dFiles.OnlyChip(shell.Window);
            Point expected = C3dFiles.ExpectedStart(shell.Window, shell.Window.Sidebar, chip);
            Assert.Equal(expected.X, Canvas.GetLeft(chip), 3);
            Assert.Equal(expected.Y, Canvas.GetTop(chip), 3);
            // 尺寸来自真渲染:text 11.5 + padding 10/4/10/5 ⇒ 至少 14(高)与 20(宽)的两条下界。
            Assert.True(chip.DesiredSize.Height >= 14.0, "chip 高度 = " + chip.DesiredSize.Height);
            Assert.True(chip.DesiredSize.Width > 20.0, "chip 宽度 = " + chip.DesiredSize.Width);
            // 【Windows 源】:1367 量的是 500×60,所以真实尺寸必须落在这个盒子里。
            Assert.True(chip.DesiredSize.Width <= C3dExpected.MeasureBounds.Width, "chip 宽度超出量盒");
            Assert.True(chip.DesiredSize.Height <= C3dExpected.MeasureBounds.Height, "chip 高度超出量盒");
            PumpAway(shell.Window);
        });
    }

    /// <summary>【Windows 源】:1372-1378 变换组 = 先 <c>Scale(0.7,0.7)</c> 后 <c>Translate</c>,原点 (0.5,0.5)。</summary>
    [Fact]
    public void Chip_transform_group_is_scale_then_translate()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();
            using var clock = new C3dClock();

            shell.Window.FlyToTasks(shell.Window.Sidebar, "飞行");

            Border chip = C3dFiles.OnlyChip(shell.Window);
            (ScaleTransform scale, TranslateTransform move) = C3dFiles.Split(chip);
            Assert.Equal(C3dExpected.ScaleFrom, scale.ScaleX, 3);
            Assert.Equal(C3dExpected.ScaleFrom, scale.ScaleY, 3);
            Assert.Equal(0.0, move.X, 3);
            Assert.Equal(0.0, move.Y, 3);
            Assert.Equal(0.5, chip.RenderTransformOrigin.Point.X, 3);
            Assert.Equal(0.5, chip.RenderTransformOrigin.Point.Y, 3);
            Assert.Equal(RelativeUnit.Relative, chip.RenderTransformOrigin.Unit);
            PumpAway(shell.Window);
        });
    }

    // ---------------------------------------------------------------- 620ms 时间线

    /// <summary>【Windows 源】:1379-1424 的四条曲线常量,逐项对拍。</summary>
    [Fact]
    public void Timeline_constants_match_the_windows_source()
    {
        AvaloniaSession.Run(() =>
        {
            Assert.Equal(C3dExpected.DurationMs, FlyMotion.DurationMs);
            Assert.Equal(C3dExpected.ScaleFrom, FlyMotion.ScaleFrom);
            Assert.Equal(C3dExpected.ScaleTo, FlyMotion.ScaleTo);
            Assert.Equal(C3dExpected.YMidCue, FlyMotion.YMidCue);
            Assert.Equal(C3dExpected.YMidFactor, FlyMotion.YMidFactor);
            Assert.Equal(C3dExpected.YMidLift, FlyMotion.YMidLift);
            Assert.Equal(C3dExpected.OpacityStartCue, FlyMotion.OpacityStartCue);
            Assert.Equal(C3dExpected.OpacityHoldCue, FlyMotion.OpacityHoldCue);
            // 45% 那个关键帧必须正好落在采样点上(否则采样会把它抹掉,见 D-129)。
            double stepsToMid = C3dExpected.YMidCue / FlyMotion.YSampleStep;
            Assert.Equal(Math.Round(stepsToMid), stepsToMid, 9);
            double stepsToEnd = 1.0 / FlyMotion.YSampleStep;
            Assert.Equal(Math.Round(stepsToEnd), stepsToEnd, 9);
        });
    }

    /// <summary>
    /// 【Windows 源 + WPF 的 CubicEase 定义】四条曲线逐点与闭式一致:
    /// EaseIn = t³、EaseOut = 1-(1-t)³、EaseInOut = t&lt;0.5 ? 4t³ : 1-(-2t+2)³/2。
    /// (Avalonia 的 <c>CubicEaseIn/Out/InOut</c> 与 WPF 同为这三条公式 —— 本机探针实测
    /// EaseIn(0.25)=0.015625、EaseOut(0.25)=0.578125、EaseInOut(0.25)=0.0625。)
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.15)]
    [InlineData(0.25)]
    [InlineData(0.45)]
    [InlineData(0.5)]
    [InlineData(0.72)]
    [InlineData(0.9)]
    [InlineData(1.0)]
    public void Curves_match_the_wpf_cubic_easing(double t)
    {
        AvaloniaSession.Run(() =>
        {
            const double dx = 400.0;
            const double dy = -200.0;
            double mid = dy * C3dExpected.YMidFactor - C3dExpected.YMidLift;
            double expectedY = (t <= C3dExpected.YMidCue)
                ? mid * WpfOut(t / C3dExpected.YMidCue)
                : mid + (dy - mid) * WpfIn((t - C3dExpected.YMidCue) / (1.0 - C3dExpected.YMidCue));

            Assert.Equal(dx * WpfInOut(t), FlyMotion.XAt(dx, t), 6);
            Assert.Equal(expectedY, FlyMotion.YAt(dy, t), 6);
            Assert.Equal(C3dExpected.ScaleFrom + (C3dExpected.ScaleTo - C3dExpected.ScaleFrom) * WpfInOut(t), FlyMotion.ScaleAt(t), 6);
            Assert.Equal(t <= C3dExpected.OpacityHoldCue ? 1.0 : 1.0 - (t - C3dExpected.OpacityHoldCue) / (1.0 - C3dExpected.OpacityHoldCue), FlyMotion.OpacityAt(t), 6);
        });
    }

    /// <summary>
    /// 【Windows 源】:1392-1400 Y 的关键帧:<c>dy*0.28-46</c> 这个 45% 点**比线性的 28% 朝终点多走了 46px**
    /// (那个 46.0 就是这一跳),之后继续朝 dy 走、但换成缓入。dy&lt;0 时"朝终点"= 越来越负,所以这里
    /// 断言的是**单调朝 dy** 与**45% 处的领先量**,不是"先上后下"。
    /// </summary>
    [Fact]
    public void Y_curve_overshoots_the_linear_path_by_the_windows_46px()
    {
        AvaloniaSession.Run(() =>
        {
            const double dy = -200.0;
            double mid = C3dExpected.YMid(dy);
            Assert.Equal(-102.0, mid, 6);          // -200 * 0.28 - 46
            Assert.Equal(0.0, FlyMotion.YAt(dy, 0.0), 6);
            Assert.Equal(mid, FlyMotion.YAt(dy, C3dExpected.YMidCue), 9);
            Assert.Equal(dy, FlyMotion.YAt(dy, 1.0), 6);
            // 45% 处比"线性的 28%"多走的正好是 46px(方向朝终点)。
            Assert.Equal(C3dExpected.YMidLift, (dy * C3dExpected.YMidFactor) - mid, 6);
            // 三段单调性:0 → 45% 快速前冲;45% → 100% 继续朝 dy(缓入),始终没越过终点。
            Assert.True(FlyMotion.YAt(dy, 0.3) < FlyMotion.YAt(dy, 0.0));
            Assert.True(FlyMotion.YAt(dy, 0.3) > FlyMotion.YAt(dy, C3dExpected.YMidCue));
            Assert.True(FlyMotion.YAt(dy, 0.8) < FlyMotion.YAt(dy, C3dExpected.YMidCue));
            Assert.True(FlyMotion.YAt(dy, 0.8) > dy);
        });
    }

    /// <summary>【Windows 源】:1401-1407 透明度:15% 之前是 1、72% 之后线性淡到 0。</summary>
    [Fact]
    public void Opacity_holds_then_fades_over_the_last_quarter()
    {
        AvaloniaSession.Run(() =>
        {
            Assert.Equal(1.0, FlyMotion.OpacityAt(0.0), 6);
            Assert.Equal(1.0, FlyMotion.OpacityAt(C3dExpected.OpacityStartCue), 6);
            Assert.Equal(1.0, FlyMotion.OpacityAt(C3dExpected.OpacityHoldCue), 6);
            Assert.Equal(0.5, FlyMotion.OpacityAt((C3dExpected.OpacityHoldCue + 1.0) / 2.0), 6);
            Assert.Equal(0.0, FlyMotion.OpacityAt(1.0), 6);
        });
    }

    // ---------------------------------------------------------------- 飞完就把 chip 摘掉

    /// <summary>
    /// 【Windows 源】:1412-1419 <c>Completed</c> 里把 chip 从 <c>FlyHost</c> 摘掉 —— **这是本包最要紧的一条**:
    /// 动画跑完之后宿主必须重新是空的(不泄漏)。
    /// </summary>
    [Fact]
    public void Fly_host_is_empty_again_after_the_flight()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();
            using var clock = new C3dClock();

            shell.Window.FlyToTasks(shell.Window.Sidebar, "飞行");
            Assert.Single(shell.Window.FlyHostPanel.Children);

            C3cFiles.Pump(() => shell.Window.FlyHostPanel.Children.Count == 0);
            Assert.Empty(shell.Window.FlyHostPanel.Children);
        });
    }

    /// <summary>【Windows 源】每次调用都是一个独立的 chip(Windows 也是直接 <c>FlyHost.Children.Add</c>),两条都飞走。</summary>
    [Fact]
    public void Two_flights_in_a_row_both_land_and_leave_nothing()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();
            using var clock = new C3dClock();
            Control source = shell.Window.Sidebar;

            shell.Window.FlyToTasks(source, "一");
            shell.Window.FlyToTasks(source, "二");
            Assert.Equal(2, shell.Window.FlyHostPanel.Children.Count);

            C3cFiles.Pump(() => shell.Window.FlyHostPanel.Children.Count == 0);
            Assert.Empty(shell.Window.FlyHostPanel.Children);
        });
    }

    /// <summary>【Windows 源】:1415-1418 落点就是任务行中心,终态缩放 0.42、透明度 0。</summary>
    [Fact]
    public void Chip_lands_on_the_tasks_row_and_fades_out()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();
            using var clock = new C3dClock();
            Control source = shell.Window.Sidebar;

            shell.Window.FlyToTasks(source, "飞行");
            Border chip = C3dFiles.OnlyChip(shell.Window);
            (ScaleTransform scale, TranslateTransform move) = C3dFiles.Split(chip);
            Point from = source.TranslatePoint(new Point(source.Bounds.Width / 2.0, source.Bounds.Height / 2.0), shell.Window)!.Value;
            Point to = C3dFiles.TargetCentre(shell.Window, shell.Window.TasksNavRow()!);

            C3cFiles.Pump(() => shell.Window.FlyHostPanel.Children.Count == 0);

            Assert.Equal(to.X - from.X, move.X, 3);
            Assert.Equal(to.Y - from.Y, move.Y, 3);
            Assert.Equal(C3dExpected.ScaleTo, scale.ScaleX, 3);
            Assert.Equal(C3dExpected.ScaleTo, scale.ScaleY, 3);
            Assert.True(chip.Opacity < 0.01, "透明度应当淡到 0,实际 " + chip.Opacity);
        });
    }

    /// <summary>【Windows 源】:1417 落地时给任务行 <c>Motion.Pulse</c> 一下(证据:任务行上出现了缩放变换)。</summary>
    [Fact]
    public void Tasks_row_is_pulsed_when_the_flight_ends()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            using var shell = ShellFixture.Create();
            using var clock = new C3dClock();
            ToggleButton row = Assert.IsType<ToggleButton>(shell.Window.TasksNavRow());

            shell.Window.FlyToTasks(shell.Window.Sidebar, "飞行");
            C3cFiles.Pump(() => shell.Window.FlyHostPanel.Children.Count == 0);

            TransformGroup group = Assert.IsType<TransformGroup>(row.RenderTransform);
            Assert.Contains(group.Children, child => child is ScaleTransform);
            Assert.Equal(0.5, row.RenderTransformOrigin.Point.X, 3);
        });
    }

    /// <summary>把还在飞的 chip 泵走(结构类用例收尾时用,免得动画跨用例留着)。</summary>
    private static void PumpAway(MainWindow window)
    {
        C3cFiles.Pump(() => window.FlyHostPanel.Children.Count == 0);
    }

    // WPF CubicEase 的三条闭式(测试侧的"权威副本",不从被测实现里借)。
    private static double WpfIn(double t)
    {
        return t * t * t;
    }

    private static double WpfOut(double t)
    {
        return 1.0 - Math.Pow(1.0 - t, 3.0);
    }

    private static double WpfInOut(double t)
    {
        return (t < 0.5) ? (4.0 * t * t * t) : (1.0 - Math.Pow(-2.0 * t + 2.0, 3.0) / 2.0);
    }
}