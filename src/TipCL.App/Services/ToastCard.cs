using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace TipCL.Services;

/// <summary>
/// 自动关闭的延时回调(注入缝)。返回 <c>null</c> 表示"这一份没有真的起计时器"(见
/// <see cref="ToastLayer.DefaultTimer"/>);返回句柄时,<c>Dispose</c> = 停掉这次计时。
/// 测试注入一个假的推进器就能确定性地驱动自动关闭,不必 <c>Thread.Sleep</c>。
/// </summary>
internal delegate IDisposable? ToastTimer(TimeSpan delay, Action onFire);

/// <summary>
/// Toast 卡片。对应 Windows <c>MainWindow.Toast</c>(<c>MainWindow.xaml.cs:1179-1255</c>)里建的那块
/// <c>Border</c>:左侧 3px 色条 + 图标字形 + 标题/正文两行,圆角 8、玻璃底、玻璃描边、
/// 22/0.14/4 的阴影,整张卡片可点(点一下关掉),悬停暂停自动关闭、移开继续。
///
/// 与 Windows 逐项一致的部分:色条宽度 3、圆角 2;图标用 <see cref="Ico"/> 的四个码位
/// (<c>Success=\uec61</c> / <c>Warning=\ue7ba</c> / <c>Error=\uea39</c> / <c>Info=\ue946</c>);
/// 颜色令牌 <c>B.Ok</c> / <c>B.Warn</c> / <c>B.Danger</c> / <c>B.Info</c>;卡片圆角 8、
/// 内边距 (12,10,14,11)、描边 1、下边距 8(这就是堆叠间距);正文面板最宽 300;
/// 正文超过 220 字截断补 <c>...</c>;自动关闭 4 秒、错误 7 秒。
///
/// 框架差异(与 C2a 的 <c>Dlg</c>/<c>Motion</c> 同一套):<c>SetResourceReference</c> →
/// <see cref="UiExt.Dyn{T}"/>;WPF 的 <c>Cursors.Hand</c> → <c>new Cursor(StandardCursorType.Hand)</c>;
/// <c>MouseLeftButtonUp</c> → <c>PointerReleased</c>(只认左键)。
/// </summary>
internal sealed class ToastCard : Border
{
    /// <summary>正文面板的最大宽度(Windows 的 <c>sPanel.MaxWidth = 300.0</c>)。</summary>
    internal const double BodyPanelMaxWidth = 300.0;

    /// <summary>正文截断长度(Windows 的 <c>body.Length > 220</c>)。</summary>
    internal const int BodyMaxLength = 220;

    /// <summary>左侧色条宽度(Windows 的 <c>Width = 3.0</c>)。</summary>
    internal const double AccentWidth = 3.0;

    /// <summary>卡片之间的竖直间隔(Windows 的 <c>Margin = (0,0,0,8)</c>)。</summary>
    internal const double StackSpacing = 8.0;

    /// <summary>卡片圆角(Windows 的 8)。</summary>
    internal const double CardCornerRadius = 8.0;

    /// <summary>卡片内边距(Windows 的 12,10,14,11)。</summary>
    internal static readonly Thickness CardPadding = new Thickness(12.0, 10.0, 14.0, 11.0);

    /// <summary>卡片内容行间距(Windows 的 <c>Ui.H(10.0, ...)</c>)。</summary>
    internal const double RowSpacing = 10.0;

    /// <summary>标题与正文的行间距(Windows 的 <c>Ui.V(2.0, ...)</c>)。</summary>
    internal const double TextSpacing = 2.0;

    internal ToastCard(string title, string body, ToastKind kind)
    {
        Title = title ?? "";
        Body = body ?? "";
        Kind = kind;
        (string glyph, string tone) = ToneOf(kind);
        Glyph = glyph;
        Tone = tone;

        AccentBar = new Border
        {
            Width = AccentWidth,
            CornerRadius = new CornerRadius(2.0),
        };
        AccentBar.Dyn(Border.BackgroundProperty, tone);

        Icon = Ui.Glyph(glyph, 15.0, tone);

        TitleBlock = Ui.Txt(Title, 13.0, bold: true).Wrap();
        BodyBlock = BuildBody(Body);
        BodyPanel = Ui.V(TextSpacing, TitleBlock, BodyBlock);
        BodyPanel.MaxWidth = BodyPanelMaxWidth;

        CornerRadius = new CornerRadius(CardCornerRadius);
        Padding = CardPadding;
        BorderThickness = new Thickness(1.0);
        Margin = new Thickness(0.0, 0.0, 0.0, StackSpacing);
        Cursor = new Cursor(StandardCursorType.Hand);
        Child = Ui.H(RowSpacing, AccentBar, Icon, BodyPanel);
        this.Dyn(Border.BackgroundProperty, "B.GlassStrong");
        this.Dyn(Border.BorderBrushProperty, "B.GlassBorder");
        Motion.Shadow(this, 22.0, 0.14, 4.0);
    }

    /// <summary>标题原文(传进来的那一份,不截断)。</summary>
    internal string Title { get; }

    /// <summary>正文原文(传进来的那一份,不截断)。</summary>
    internal string Body { get; }

    internal ToastKind Kind { get; }

    /// <summary>图标字形码位(Windows 的 <c>item</c>)。</summary>
    internal string Glyph { get; }

    /// <summary>颜色令牌键(Windows 的 <c>item2</c>)。</summary>
    internal string Tone { get; }

    /// <summary>左侧色条(测试按它的背景色断言取色)。</summary>
    internal Border AccentBar { get; }

    /// <summary>图标字形。</summary>
    internal TextBlock Icon { get; }

    internal TextBlock TitleBlock { get; }

    /// <summary>正文;正文为空/空白时是 <c>null</c>(Windows 用 <c>null</c> 表示"不加这一行")。</summary>
    internal TextBlock? BodyBlock { get; }

    /// <summary>标题 + 正文那一列(Windows 的 <c>sPanel</c>)。</summary>
    internal SPanel BodyPanel { get; }

    /// <summary>正文截断后的实际文本(空正文是空串)。</summary>
    internal string BodyShown => BodyBlock?.Text ?? "";

    /// <summary>卡片的三个孩子:色条、图标、文字列(Windows 的 <c>Ui.H(10.0, border, glyph, sPanel)</c>)。</summary>
    internal IReadOnlyList<Control> Row => new List<Control>(((SPanel)Child!).Children);

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1194-1200</c> 的 kind → (图标码位, 颜色令牌) 表。</summary>
    internal static (string Glyph, string Tone) ToneOf(ToastKind kind)
    {
        return kind switch
        {
            ToastKind.Success => (Ico.Success, "B.Ok"),
            ToastKind.Warning => (Ico.Warning, "B.Warn"),
            ToastKind.Error => (Ico.Error, "B.Danger"),
            _ => (Ico.Info, "B.Info"),
        };
    }

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1225</c> 的 <c>Error ? 7 : 4</c> 秒。</summary>
    internal static TimeSpan AutoCloseDelay(ToastKind kind)
    {
        return TimeSpan.FromSeconds((kind == ToastKind.Error) ? 7 : 4);
    }

    /// <summary>
    /// 【Windows 源】<c>MainWindow.xaml.cs:1207</c>:正文空白就不加这一行;超过 220 字留前 220 字 + <c>...</c>。
    /// 截断按 UTF-16 码元数,与 Windows 的 <c>Substring(0, 220)</c> 同口径。
    /// </summary>
    internal static TextBlock? BuildBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        return Ui.Muted((body.Length > BodyMaxLength) ? (body.Substring(0, BodyMaxLength) + "...") : body);
    }
}

/// <summary>
/// Toast 宿主。对应 Windows <c>MainWindow.Toast</c> 里"管 <c>ToastHost</c> 那一摊"的部分:
/// 最多同时挂 <see cref="MaxCards"/> 张(多了先顶掉最老的)、每张进 <c>Motion.SlideIn</c>、
/// 到点或点一下 <c>Motion.SlideOut</c> 后从宿主里摘掉、悬停暂停自动关闭。
///
/// Windows 把这段等同于内联在窗口方法里;mac 侧提成独立类型(与 C2a 的 <c>Dlg</c> 提 <c>Push</c>、
/// C2b 提 <c>PageHost</c> 同一套做法),这样"堆叠顺序 / 间距 / 自动关闭 / 计时器不泄漏"
/// 都能离开窗口单独钉住(见 D-106)。
///
/// **计时器的两条纪律**:
/// 1. 每张卡片的计时句柄都记在 <see cref="_timers"/> 里,暂停/关闭/收回/收摊时**一律 Dispose**,
///    <see cref="LiveTimerCount"/> 就是"还挂着的计时器数"(收摊后必须是 0);
/// 2. 顶掉最老的一张时 Windows 只从 <c>ToastHost.Children</c> 里 <c>RemoveAt(0)</c>,那张卡片的
///    <c>DispatcherTimer</c> 会一直转到它自己到点(最长 7 秒)才被 <c>Tick</c> 里的
///    <c>timer.Stop()</c> 收掉 —— mac 侧顺手在顶掉时就把计时停掉(等价于"到点后什么也不做",
///    但不留残留计时器,见 D-105)。
/// </summary>
internal sealed class ToastLayer : IDisposable
{
    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1189</c> 的 4 张上限。</summary>
    internal const int MaxCards = 4;

    private readonly StackPanel _host;

    private readonly ToastTimer _schedule;

    private readonly List<ToastCard> _cards = new List<ToastCard>();

    private readonly Dictionary<ToastCard, IDisposable> _timers = new Dictionary<ToastCard, IDisposable>();

    private bool _disposed;

    internal ToastLayer(StackPanel host, ToastTimer? schedule = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _schedule = schedule ?? DefaultTimer;
    }

    /// <summary>卡片宿主(外壳里那个右上角的 StackPanel)。</summary>
    internal StackPanel Host => _host;

    /// <summary>按加入顺序排列的活卡片。</summary>
    internal IReadOnlyList<ToastCard> Cards => _cards;

    /// <summary>还挂着的自动关闭计时器数(泄漏断言用)。</summary>
    internal int LiveTimerCount => _timers.Count;

    /// <summary>弹一张卡片(必须在 UI 线程上调用;<see cref="MainWindow.Toast"/> 负责转线程)。</summary>
    internal ToastCard Show(string title, string body = "", ToastKind kind = ToastKind.Info)
    {
        ToastCard card = new ToastCard(title, body, kind);
        // 【Windows 源】1189-1192:满了先顶掉最老的那张(Windows 只摘孩子,这里连计时一起收)。
        while (_host.Children.Count >= MaxCards)
        {
            EvictOldest();
        }

        _host.Children.Add(card);
        _cards.Add(card);
        Arm(card);
        card.PointerEntered += delegate
        {
            Pause(card);
        };
        card.PointerExited += delegate
        {
            Arm(card);
        };
        card.PointerReleased += delegate(object? _, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
            {
                Close(card);
            }
        };
        Motion.SlideIn(card);
        return card;
    }

    /// <summary>关掉指定的那一张(**恰好一张**);返回它当时是不是活着的。</summary>
    internal bool Close(ToastCard card)
    {
        int index = _cards.IndexOf(card);
        if (index < 0)
        {
            return false;
        }

        _cards.RemoveAt(index);
        Pause(card);
        if (_host.Children.Contains(card))
        {
            // 关掉动画时 SlideOut 会**同步**回调(见 Motion 的类注释),所以测试里当场就摘掉了。
            Motion.SlideOut(card, delegate
            {
                _host.Children.Remove(card);
            });
        }

        return true;
    }

    /// <summary>全关掉(切主题、账号切换之类的外壳动作可以用;不计入公开面)。</summary>
    internal void CloseAll()
    {
        foreach (ToastCard card in _cards.ToArray())
        {
            Close(card);
        }
    }

    /// <summary>收摊:停掉所有计时器并清空宿主(窗口关闭时调,保证不留任何计时器)。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopAllTimers();
        _cards.Clear();
        _host.Children.Clear();
    }

    private void Arm(ToastCard card)
    {
        Pause(card);
        IDisposable? handle = _schedule(ToastCard.AutoCloseDelay(card.Kind), delegate
        {
            Close(card);
        });
        if (handle is not null)
        {
            _timers[card] = handle;
        }
    }

    private void Pause(ToastCard card)
    {
        if (_timers.TryGetValue(card, out IDisposable? handle))
        {
            _timers.Remove(card);
            handle.Dispose();
        }
    }

    private void EvictOldest()
    {
        if (_cards.Count == 0)
        {
            // 兜底:活卡片表空了而宿主还有孩子(都在退场动画里),直接清掉,免得死循环。
            _host.Children.Clear();
            return;
        }

        ToastCard oldest = _cards[0];
        _cards.RemoveAt(0);
        Pause(oldest);
        _host.Children.Remove(oldest);
    }

    private void StopAllTimers()
    {
        foreach (IDisposable handle in new List<IDisposable>(_timers.Values))
        {
            handle.Dispose();
        }

        _timers.Clear();
    }

    /// <summary>
    /// 默认计时器。真桌面生命周期下就是 Windows 的那个 <c>DispatcherTimer</c>(到点先 Stop 再关卡片);
    /// 离屏(<c>--smoke</c>)与测试进程里**没有人在推进 Dispatcher 的时钟**(与 <c>Motion.HasClock</c>
    /// 同一个条件),在那里起计时器永远不会被触发,只会留一个残留句柄 —— 所以直接返回 <c>null</c>:
    /// 自动关闭在那两个环境里不生效(Windows 那边同样收不到 Tick,行为等价),但**不留残留计时器**。
    /// </summary>
    private static IDisposable? DefaultTimer(TimeSpan delay, Action onFire)
    {
        if (!Motion.HasClock)
        {
            return null;
        }

        DispatcherTimer timer = new DispatcherTimer { Interval = delay };
        timer.Tick += delegate
        {
            timer.Stop();
            onFire();
        };
        timer.Start();
        return new DispatcherTimerHandle(timer);
    }

    private sealed class DispatcherTimerHandle : IDisposable
    {
        private readonly DispatcherTimer _timer;

        internal DispatcherTimerHandle(DispatcherTimer timer)
        {
            _timer = timer;
        }

        public void Dispose()
        {
            _timer.Stop();
        }
    }
}
