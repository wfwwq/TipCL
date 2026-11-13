using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TipCL;
using TipCL.Pages;
using TipCL.Services;
using TipCL.Themes;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C3d(外壳 <c>FlyToTasks</c> / <c>FlyHost</c>)期望值的**权威副本**:数字全部来自 Windows 源
/// <c>MainWindow.xaml.cs:1336-1425</c> 与 <c>MainWindow.xaml</c>:77、任务书,或本机实测。
/// 每条注明来源:【Windows 源】/【任务书】/【本机实测】—— 源码改了而期望没跟着改会立刻红。
/// </summary>
internal static class C3dExpected
{
    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1379</c> <c>TimeSpan.FromMilliseconds(620.0)</c>。</summary>
    internal const double DurationMs = 620.0;

    /// <summary>【Windows 源】:1360 <c>CornerRadius = new CornerRadius(999.0)</c>。</summary>
    internal const double PillRadius = 999.0;

    /// <summary>【Windows 源】:1361 <c>Padding = new Thickness(10.0, 4.0, 10.0, 5.0)</c>。</summary>
    internal static readonly Thickness ChipPadding = new Thickness(10.0, 4.0, 10.0, 5.0);

    /// <summary>【Windows 源】:1362 字号 <c>11.5</c> + <c>bold: true</c>。</summary>
    internal const double FontSize = 11.5;

    /// <summary>【Windows 源】:1362 文字色键 <c>"B.OnAccentStrong"</c>。</summary>
    internal const string TextColourKey = "B.OnAccentStrong";

    /// <summary>【Windows 源】:1362 <c>string.IsNullOrEmpty(text) ? I18n.L("已加入队列") : text</c>。</summary>
    internal const string DefaultText = "已加入队列";

    /// <summary>【Windows 源】:1364 <c>colorKey ?? "B.AccentStrong"</c>。</summary>
    internal const string DefaultColourKey = "B.AccentStrong";

    /// <summary>【Windows 源】:1365 <c>Motion.Shadow(chip, 16.0, 0.3, 3.0)</c>。</summary>
    internal const double ShadowBlur = 16.0;

    internal const double ShadowOpacity = 0.3;

    internal const double ShadowDepth = 3.0;

    /// <summary>【Windows 源】:1367 <c>chip.Measure(new Size(500.0, 60.0))</c>。</summary>
    internal static readonly Size MeasureBounds = new Size(500.0, 60.0);

    /// <summary>【Windows 源】:1373 <c>new ScaleTransform(0.7, 0.7)</c>。</summary>
    internal const double ScaleFrom = 0.7;

    /// <summary>【Windows 源】:1408 <c>new DoubleAnimation(0.7, 0.42, timeSpan)</c>。</summary>
    internal const double ScaleTo = 0.42;

    /// <summary>【Windows 源】:1392-1400 Y 的两个关键帧;:1372-1378 变换组是"先缩放后平移"。</summary>
    internal const double YMidCue = 0.45;

    internal const double YMidFactor = 0.28;

    internal const double YMidLift = 46.0;

    /// <summary>【Windows 源】:1405-1407 透明度的三个关键帧 15% / 72% / 100%。</summary>
    internal const double OpacityStartCue = 0.15;

    internal const double OpacityHoldCue = 0.72;

    /// <summary>【Windows 源】<c>MainWindow.xaml</c>:77 <c>&lt;Canvas Name="FlyHost" ... IsHitTestVisible="False"/&gt;</c>。</summary>
    internal const string FlyHostName = "FlyHost";

    /// <summary>【Windows 源】:1342 目标是 <c>TabTasks</c>;mac 侧是侧栏里切到 <c>tasks</c> 页的那一行(D-97)。</summary>
    internal const string TasksPageId = "tasks";

    internal const string TasksNavKey = "tasks";

    /// <summary>【Windows 源】:1370-1371 起点 = 源控件中心 − chip 尺寸的一半。</summary>
    internal const double CentreDivisor = 2.0;

    /// <summary>【Windows 源】:1393 Y 中点的公式 <c>dy * 0.28 - 46.0</c>。</summary>
    internal static double YMid(double dy)
    {
        return dy * YMidFactor - YMidLift;
    }
}

/// <summary>
/// 「假装有人推进动画时钟」的作用域 —— C3d 新增(见 DECISIONS D-129)。
///
/// 为什么需要:headless 会话里 <c>Application.ApplicationLifetime</c> 永远是 null,所以
/// <c>Motion.HasClock</c> 恒假 ⇒ <c>Motion.Enabled</c> 恒假 ⇒ **"动画开着"那一支在测试里不可达**
/// (D-69/D-98 记过同一件事)。而离屏的动画时钟其实推得动:本机探针实测 620ms 的
/// <c>Animation.RunAsync</c> 在 <c>UiHost.Flush()</c> 的泵里真的跑完并落了终态。
/// 本作用域把 <c>Motion.ClockOverride</c> 置真、把用户开关置真,于是 <c>Motion.Enabled == true</c>;
/// 用完**两个都还原**(先还用户开关、再撤时钟,免得撤时钟的那一刻触发别的收尾)。
/// </summary>
internal sealed class C3dClock : IDisposable
{
    private readonly bool? _clock = Motion.ClockOverride;

    private readonly bool _user = Motion.UserEnabled;

    internal C3dClock()
    {
        Motion.ClockOverride = true;
        Motion.Enabled = true;
    }

    /// <summary>闸门真的开着(拿它当断言的前置条件,不要靠"我记得我设过")。</summary>
    internal bool On => Motion.Enabled;

    public void Dispose()
    {
        Motion.Enabled = _user;
        Motion.ClockOverride = _clock;
    }
}

/// <summary>
/// 一个只会"按页面的统一调用形状"调外壳成员的最小页面 —— 用来钉住**后续 13 处调用点**的写法
/// (<c>Win?.FlyToTasks(anchor, text)</c>)在这份代码里真的通、且没有窗口时不会炸。
/// </summary>
internal sealed class FlyProbePage : PageBase
{
    internal int Calls { get; private set; }

    /// <summary><c>PageBase.Win</c> 这一会儿是不是指向真外壳(给 null 安全那几条用)。</summary>
    internal bool WinVisible => Win is not null;

    /// <summary>【任务书】页面的统一调用形状(= Windows 的 <c>PageBase.Win?.FlyToTasks(anchor, text)</c>)。</summary>
    internal void FlyFromPage(Control anchor, string text = "来自页面")
    {
        Calls++;
        Win?.FlyToTasks(anchor, text);
    }

    /// <summary>
    /// 【Windows 源】<c>InstallPage.cs:489</c> 那一种形状:走 <c>AppServices.Window</c>。
    /// mac 侧的 <c>AppServices.Window</c> 静态类型是 Avalonia 的 <c>Window?</c>(D-88),所以要一次 cast。
    /// </summary>
    internal void FlyFromAppServices(Control anchor, string text = "来自 AppServices")
    {
        (AppServices.Window as MainWindow)?.FlyToTasks(anchor, text);
    }
}

/// <summary>C3d 的公共小工具(泵、找控件、现算几何)。全部经 <c>AvaloniaSession.Run</c> 调用(D-79)。</summary>
internal static class C3dFiles
{
    /// <summary>外壳里唯一的那个飞行 chip(不是 1 个就抛,绝不静默取第一个)。</summary>
    internal static Border OnlyChip(MainWindow window)
    {
        Assert.Single(window.FlyHostPanel.Children);
        return Assert.IsType<Border>(window.FlyHostPanel.Children[0]);
    }

    /// <summary>chip 的字(Windows 把文字放在 <c>Border.Child</c> 的 TextBlock 上)。</summary>
    internal static TextBlock ChipText(Border chip)
    {
        return Assert.IsType<TextBlock>(chip.Child);
    }

    /// <summary>【Windows 源】起点 = 源控件中心 − chip 尺寸的一半(:1370-1371 的算法,这里现算一遍)。</summary>
    internal static Point ExpectedStart(MainWindow window, Control source, Border chip)
    {
        Point? centre = source.TranslatePoint(
            new Point(source.Bounds.Width / C3dExpected.CentreDivisor, source.Bounds.Height / C3dExpected.CentreDivisor), window);
        Assert.NotNull(centre);
        return new Point(
            centre!.Value.X - chip.DesiredSize.Width / C3dExpected.CentreDivisor,
            centre.Value.Y - chip.DesiredSize.Height / C3dExpected.CentreDivisor);
    }

    /// <summary>【Windows 源】终点 = 任务行中心(:1352)。</summary>
    internal static Point TargetCentre(MainWindow window, Control target)
    {
        Point? centre = target.TranslatePoint(
            new Point(target.Bounds.Width / C3dExpected.CentreDivisor, target.Bounds.Height / C3dExpected.CentreDivisor), window);
        Assert.NotNull(centre);
        return centre!.Value;
    }

    /// <summary>把 chip 的变换组拆成 (缩放, 平移);不是"先缩放后平移"的组就抛。</summary>
    internal static (ScaleTransform Scale, TranslateTransform Move) Split(Border chip)
    {
        TransformGroup group = Assert.IsType<TransformGroup>(chip.RenderTransform);
        Assert.Equal(2, group.Children.Count);
        ScaleTransform scale = Assert.IsType<ScaleTransform>(group.Children[0]);
        TranslateTransform move = Assert.IsType<TranslateTransform>(group.Children[1]);
        return (scale, move);
    }

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1364</c> 的背景色键(与色板逐色对拍,不硬编码色值)。</summary>
    internal static void AssertBackground(Border chip, string key)
    {
        Assert.NotNull(chip.Background);
        Assert.Equal(C1cExpected.ColourOf(Palette.Brush(key)), C1cExpected.ColourOf(chip.Background));
    }

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1405-1407</c> 的文字色键。</summary>
    internal static void AssertTextColour(TextBlock text, string key)
    {
        Assert.NotNull(text.Foreground);
        Assert.Equal(C1cExpected.ColourOf(Palette.Brush(key)), C1cExpected.ColourOf(text.Foreground));
    }

    /// <summary>启动页的假后端(路由来自 C3cPageStateTests 的同一批"本机实测"路由;按方法名路由,不按请求序号)。</summary>
    internal static C3cFakeApi LaunchApi()
    {
        return new C3cFakeApi()
            .Route("get_instances", """[{"name":"main","path":"C:\\mc\\main","versions":2,"mc":"1.20.1","pack":"","pack_version":"","mc_version":"1.20.1","java":"","java_label":""}]""")
            .Route("get_account_rows", """[{"name":"Steve","active":true}]""")
            .Route("get_settings", """{"default_memory_mb":2048,"default_resolution":[1280,720]}""")
            .Route("get_installed_versions", """["1.20.1"]""")
            .Route("java_combo_options", """[{"label":"Java 17","value":"Java 17"}]""")
            .Route("java_combo_label_for", "\"Java 17\"")
            .Route("get_installed_mod_entries", "[]")
            .Route("get_setting", parameters => parameters.TryGetProperty("key", out JsonElement k) && k.GetString() == "homepage_mode" ? "\"news\"" : "\"\"")
            .Route("cached_news", "[]")
            .Route("fetch_news", "[]")
            .Route("get_total_playtime", "0")
            .Route("repair_version", "\"task-repair\"");
    }

    /// <summary>把启动页按它自己的入口加载一次,并把 fire-and-forget 的尾巴泵完。</summary>
    internal static void Load(LaunchPage page)
    {
        Task task = page.EnsureLoadedAsync();
        C3bFiles.PumpTask(task);
        C3cFiles.Settle(12);
    }
}
