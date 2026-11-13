using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TipCL;
using TipCL.Services;
using TipCL.Themes;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C2c1(Toast 卡片 / 对话框卡片)期望值的**权威副本**:与 C1a/C1c/C2b 同一套做法 ——
/// 期望值来自 Windows 源(<c>MainWindow.xaml.cs</c> 的 <c>Toast</c> 方法、
/// <c>Services\Dlg.cs</c> 的 <c>Shell</c>/<c>Ask</c>)、任务书与 GOAL,在测试里各留一份,
/// 源码改了而期望没跟着改会立刻红。每条都注明来源:【Windows 源】/【任务书】/【本机实测】。
/// </summary>
internal static class C2c1Expected
{
    // ---------------------------------------------------------------- Toast 卡片(来源:Windows MainWindow.xaml.cs)

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1189</c> —— <c>while (ToastHost.Children.Count >= 4)</c>。</summary>
    internal const int MaxCards = 4;

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1203</c> —— 左侧色条 <c>Width = 3.0</c>。</summary>
    internal const double AccentWidth = 3.0;

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1208</c> —— <c>sPanel.MaxWidth = 300.0</c>。</summary>
    internal const double BodyPanelMaxWidth = 300.0;

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1207</c> —— <c>body.Length &gt; 220</c> 截断补 "..."。</summary>
    internal const int BodyMaxLength = 220;

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1211</c> —— 卡片 <c>CornerRadius = 8</c>。</summary>
    internal const double CardCornerRadius = 8.0;

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1216</c> —— <c>Ui.H(10.0, ...)</c>。</summary>
    internal const double RowSpacing = 10.0;

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1207</c> —— <c>Ui.V(2.0, ...)</c>。</summary>
    internal const double TextSpacing = 2.0;

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1212</c> —— <c>Padding = (12,10,14,11)</c>。</summary>
    internal static readonly Thickness CardPaddingValue = new Thickness(12.0, 10.0, 14.0, 11.0);

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1214</c> —— <c>Margin = (0,0,0,8)</c>(这就是堆叠间距)。</summary>
    internal static readonly Thickness CardMarginValue = new Thickness(0.0, 0.0, 0.0, 8.0);

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1218-1219</c> —— 玻璃底 + 玻璃描边。</summary>
    internal const string CardBackground = "B.GlassStrong";

    internal const string CardBorderBrush = "B.GlassBorder";

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1194-1200</c> 的 kind → (图标码位, 颜色令牌) 表。</summary>
    internal static readonly (ToastKind Kind, string Glyph, string Tone)[] KindStyles =
    {
        (ToastKind.Success, "\uec61", "B.Ok"),
        (ToastKind.Warning, "\ue7ba", "B.Warn"),
        (ToastKind.Error, "\uea39", "B.Danger"),
        (ToastKind.Info, "\ue946", "B.Info"),
    };

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1225</c> —— 错误 7 秒,其余 4 秒。</summary>
    internal static TimeSpan AutoCloseDelay(ToastKind kind)
    {
        return TimeSpan.FromSeconds((kind == ToastKind.Error) ? 7 : 4);
    }

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1207</c> —— 正文的显示文本(超长截断补 "...")。</summary>
    internal static string ShownBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "";
        }

        return (body.Length > BodyMaxLength) ? (body.Substring(0, BodyMaxLength) + "...") : body;
    }

    /// <summary>【Windows 源】<c>MainWindow.xaml</c>:88 —— ToastHost 是右上角的 StackPanel,边距 (0,12,16,0)。</summary>
    internal static readonly Thickness HostMarginValue = new Thickness(0.0, 12.0, 16.0, 0.0);

    // ---------------------------------------------------------------- 对话框卡片(来源:Windows Services\Dlg.cs)

    /// <summary>【Windows 源】<c>Dlg.cs:220</c> —— <c>Ask</c> 的默认宽度 520。</summary>
    internal const double ConfirmWidth = 520.0;

    /// <summary>【Windows 源】<c>Dlg.cs:207</c> —— <c>MinWidth = Math.Min(width, 380.0)</c>。</summary>
    internal const double MinWidthFloor = 380.0;

    /// <summary>【Windows 源】<c>Dlg.cs:203-205</c> —— 圆角 10、内边距 (22,18,22,18)、描边 1。</summary>
    internal const double DialogCornerRadius = 10.0;

    internal static readonly Thickness DialogPaddingValue = new Thickness(22.0, 18.0, 22.0, 18.0);

    /// <summary>【Windows 源】<c>Dlg.cs:192/199</c> —— 标题/正文/footer 之间 14px。</summary>
    internal const double SectionGap = 14.0;

    /// <summary>【Windows 源】<c>Dlg.cs:209-210</c> —— 玻璃底 + 玻璃描边。</summary>
    internal const string DialogBackground = "B.GlassStrong";

    internal const string DialogBorderBrush = "B.GlassBorder";

    /// <summary>【Windows 源】<c>Dlg.cs:225</c> —— 按钮行是 <c>Ui.H(8.0, 取消, 确定)</c>(取消在前、右对齐)。</summary>
    internal const double FooterSpacing = 8.0;
}

/// <summary>
/// 假的自动关闭推进器:把 <see cref="ToastLayer"/> 的计时器换成"记账 + 手动点火",
/// 于是"4 秒 / 7 秒到点关掉"可以确定性地验,不用 <c>Thread.Sleep</c>、也不留真实计时器。
/// </summary>
internal sealed class FakeToastTimer
{
    private readonly List<Ticket> _tickets = new List<Ticket>();

    /// <summary>所有发出去过的计时(含已被停掉的)。</summary>
    internal List<Ticket> All => _tickets;

    /// <summary>还活着(没被 Dispose)的计时数 —— 与 <see cref="ToastLayer.LiveTimerCount"/> 应当时刻相等。</summary>
    internal int LiveCount => _tickets.Count(t => !t.Stopped);

    /// <summary>一次计时的记账:请求的延时、点火回调、有没有被停掉。</summary>
    internal sealed class Ticket : IDisposable
    {
        internal Ticket(TimeSpan delay, Action onFire)
        {
            Delay = delay;
            OnFire = onFire;
        }

        internal TimeSpan Delay { get; }

        internal Action OnFire { get; }

        internal bool Stopped { get; private set; }

        public void Dispose()
        {
            Stopped = true;
        }
    }

    /// <summary>注入给 <see cref="ToastLayer"/> 的那个委托。</summary>
    internal IDisposable Schedule(TimeSpan delay, Action onFire)
    {
        Ticket ticket = new Ticket(delay, onFire);
        _tickets.Add(ticket);
        return ticket;
    }

    /// <summary>点火第 <paramref name="index"/> 个还活着的计时(模拟"到点了")。</summary>
    internal Ticket Fire(int index = 0)
    {
        Ticket ticket = _tickets.Where(t => !t.Stopped).ElementAt(index);
        ticket.OnFire();
        return ticket;
    }

    /// <summary>最近一次发出去的那张票。</summary>
    internal Ticket Last => _tickets[_tickets.Count - 1];
}

/// <summary>
/// Toast 的测试台:一个挂进真窗口的右上角 StackPanel + 一个用 <see cref="FakeToastTimer"/>
/// 驱动的 <see cref="ToastLayer"/>。用完把窗口关掉、全局静态还原(D-69 的纪律)。
/// </summary>
internal sealed class ToastHarness : IDisposable
{
    private readonly UiHost _window;

    private readonly bool _motionWasEnabled;

    private ToastHarness(UiHost window, StackPanel host, ToastLayer layer, bool motionWasEnabled)
    {
        _window = window;
        Host = host;
        Layer = layer;
        _motionWasEnabled = motionWasEnabled;
    }

    internal StackPanel Host { get; }

    internal FakeToastTimer Timer { get; } = new FakeToastTimer();

    internal ToastLayer Layer { get; private set; }

    internal static ToastHarness Show()
    {
        bool motionWasEnabled = Motion.UserEnabled;
        Motion.Enabled = false; // 测试进程里本来就没有时钟(Motion.Enabled 恒 false),这里只是把用户开关也钉住
        StackPanel host = new StackPanel
        {
            Name = "ToastHost",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = C2c1Expected.HostMarginValue,
        };
        UiHost window = UiHost.Show(host);
        ToastHarness harness = new ToastHarness(window, host, null!, motionWasEnabled);
        harness.Layer = new ToastLayer(host, harness.Timer.Schedule);
        return harness;
    }

    /// <summary>宿主里的卡片(按加入顺序)。</summary>
    internal List<ToastCard> Cards => Host.Children.OfType<ToastCard>().ToList();

    public void Dispose()
    {
        Layer.Dispose();
        Motion.Enabled = _motionWasEnabled;
        _window.Dispose();
        UiHost.Flush();
    }
}

/// <summary>程序化触发指针/键盘事件的助手(headless 里不模拟真实输入设备,只走事件路由)。</summary>
internal static class C2c1Input
{
    /// <summary>造一个指针抬起事件(与 C1cUiInputTests 同一套写法)。</summary>
    internal static PointerReleasedEventArgs Release(MouseButton button, Visual source)
    {
        return new PointerReleasedEventArgs(
            source,
            new Pointer(1, PointerType.Mouse, true),
            source,
            new Point(0.0, 0.0),
            0UL,
            new PointerPointProperties(),
            KeyModifiers.None,
            button);
    }

    /// <summary>程序化抬起一下左键(卡片的"点一下关掉")。</summary>
    internal static void Click(Control card)
    {
        card.RaiseEvent(Release(MouseButton.Left, card));
    }

    /// <summary>
    /// 程序化"在遮罩上按下左键"(Dlg 的 <c>onDismiss</c>:点遮罩 = 取消)。
    /// <see cref="PointerPointProperties"/> 要显式说"左键是按下的",否则 <c>IsLeftButtonPressed</c> 是 false
    /// (真实设备那一层在 headless 里不许模拟,只走到事件路由这一层)。
    /// </summary>
    internal static void PressMask(Control mask)
    {
        mask.RaiseEvent(new PointerPressedEventArgs(
            mask,
            new Pointer(1, PointerType.Mouse, true),
            mask,
            new Point(0.0, 0.0),
            0UL,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None,
            1));
    }

    /// <summary>程序化触发"指针进入"(悬停:暂停自动关闭)。</summary>
    internal static void Enter(Control card)
    {
        card.RaiseEvent(new PointerEventArgs(
            InputElement.PointerEnteredEvent,
            card,
            new Pointer(1, PointerType.Mouse, true),
            card,
            new Point(0.0, 0.0),
            0UL,
            new PointerPointProperties(),
            KeyModifiers.None));
    }

    /// <summary>程序化触发"指针离开"(移开:继续自动关闭)。</summary>
    internal static void Leave(Control card)
    {
        card.RaiseEvent(new PointerEventArgs(
            InputElement.PointerExitedEvent,
            card,
            new Pointer(1, PointerType.Mouse, true),
            card,
            new Point(0.0, 0.0),
            0UL,
            new PointerPointProperties(),
            KeyModifiers.None));
    }

    /// <summary>程序化按一下按钮。</summary>
    internal static void Press(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    /// <summary>程序化按一下 Esc(对话框的"关掉"分支)。</summary>
    internal static void Escape(Control target)
    {
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
    }
}
