using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;

namespace TipCL.Services;

/// <summary>
/// C1c:Windows 版 <c>Services\SmoothScroll.cs</c>(WPF)到 Avalonia 11 的移植。
///
/// 公开成员(<c>Offset</c> / <c>OffsetProperty</c> / <c>BindWindowMaxHeight</c>)与 WPF 版一致:
/// - <see cref="Offset"/> 是一个 <c>double</c> 的可设值:设它就把纵向滚动位置挪过去
///   (WPF 版靠 <c>DependencyProperty</c> 的回调调 <c>ScrollToVerticalOffset</c>,Avalonia 靠
///   <see cref="OnPropertyChanged"/> 里把值转成基类的 <c>Offset</c>(<c>Vector</c>))。
///   **注意**:Avalonia 的 <c>ScrollViewer</c> 自己有一个 <c>Vector</c> 类型的 <c>Offset</c>,
///   所以这里的 <c>double Offset</c> 用 <c>new</c> 隐藏了它(WPF 侧的 <c>ScrollViewer</c> 没有
///   <c>Offset</c> 这个名字,不会撞车)。见 PROGRESS.md 的 C1c 差异表。
/// - <see cref="BindWindowMaxHeight"/>:把 <c>MaxHeight</c> 绑到宿主窗口的 <c>ActualHeight</c> × pct
///   (不低于 floor),矮窗下内层滚动区收缩而不是留白。
/// - 滚轮平滑:<see cref="OnPointerWheelChanged"/> 里按 <c>Lucide.MotionEnabled</c> 决定是
///   原样交给基类(系统滚动)还是做 230ms 的三次缓出动画。**WPF 的 <c>Motion.Enabled</c> 门面
///   属 C2**(见 D-70),所以这里用 <c>Lucide</c> 已经暴露的同一个开关,并且与它一样只在
///   "真正跑起来的应用生命周期"下起动画(离屏 / 测试进程不起,免得无限/长动画挂着)。
///
/// 两处框架差异(在 PROGRESS.md 的差异表里也有):
/// 1. WPF 的 <c>CanContentScroll=false</c>(像素滚动)与 <c>PanningMode</c>(触控板纵向平移)
///    在 Avalonia 的 <c>ScrollViewer</c> 上没有对应属性 —— Avalonia 本来就是像素滚动,
///    触控板平移由平台手势层负责,所以这两项没有移植;
/// 2. 滚动上限在 WPF 读 <c>ScrollableHeight</c>,Avalonia 用 <c>Extent.Height - Viewport.Height</c>
///    (<c>ScrollViewer</c> 上没有 <c>ScrollableHeight</c>)。
/// </summary>
/// C2b 更正:上面"按 Lucide.MotionEnabled 决定"的说法**已作废**,滚轮平滑改走 Motion 门面 ——
/// 与 WPF 版一样读 <c>Motion.Enabled</c>(用户开关 && 有人在推进动画时钟),没有时钟时直接落终态。
/// </summary>
public sealed class SmoothScroll : ScrollViewer
{
    /// <summary>纵向偏移(WPF 版的 <c>DependencyProperty</c>;见类注释里与基类 <c>Offset</c> 的关系)。</summary>
    public static new readonly StyledProperty<double> OffsetProperty =
        AvaloniaProperty.Register<SmoothScroll, double>(nameof(Offset));

    private double _target = -1.0;

    public SmoothScroll()
    {
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
    }

    /// <summary>
    /// 纵向偏移。<c>new</c> 是必须的:Avalonia 的 <c>ScrollViewer</c> 自带一个 <c>Vector</c> 类型的
    /// <c>Offset</c>,而 WPF 版的 <c>SmoothScroll.Offset</c> 是 <c>double</c> —— 保持 WPF 的公开类型,
    /// 于是显式隐藏基类成员(类型不同,不能 override)。
    /// </summary>
    public new double Offset
    {
        get => GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    /// <summary>滚动区总高(超出视口的那部分)。WPF 的 <c>ScrollableHeight</c> 在 Avalonia 没有。</summary>
    private double ScrollableHeight => Math.Max(0.0, Extent.Height - Viewport.Height);

    /// <summary>MaxHeight = 窗口高 × pct,且不低于 floor。</summary>
    public void BindWindowMaxHeight(double pct, double floor)
    {
        Window? host = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        BindWindowMaxHeight(host, pct, floor);
    }

    /// <summary>可注入宿主窗口的重载(测试用:离屏/测试进程里 <c>ApplicationLifetime</c> 是 null)。</summary>
    internal void BindWindowMaxHeight(Window? host, double pct, double floor)
    {
        if (host is null)
        {
            // WPF 版的 Source 是 AppServices.Window(宿主的静态引用)。mac 版的外壳是 C2 的事,
            // 这里没有宿主窗口就不绑 —— 不抛异常,也不写死一个高度。
            return;
        }

        // WPF 绑的是 Window.ActualHeight;Avalonia 没有 ActualHeight,只有 Visual.Bounds,
        // 所以路径写成 Bounds.Height(Bounds 是通知属性,窗口尺寸变了会重算)。
        Bind(Layoutable.MaxHeightProperty, new Binding("Bounds.Height")
        {
            Source = host,
            Converter = new Ui.WindowPctConverter(pct),
            ConverterParameter = floor,
        });
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == OffsetProperty)
        {
            SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(base.Offset.X, change.GetNewValue<double>()));
        }
    }

    /// <summary>
    /// 滚轮:按住时按 230ms 三次缓出滚到目标位置(WPF 版同样用 <c>Motion.Enabled</c> 决定要不要平滑)。
    /// <c>_target</c> 在同一次连续滚动里累计,动画结束后复位(与 WPF 版的 <c>Completed</c> 回调等价)。
    /// </summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (!Motion.Enabled || ScrollableHeight <= 0.0)
        {
            base.OnPointerWheelChanged(e);
            return;
        }

        e.Handled = true;

        // WPF 的 MouseWheelEventArgs.Delta 每格是 120;Avalonia 的 PointerWheelEventArgs.Delta
        // 每格是 1.0,所以先换算成同一量纲再乘 WPF 那 0.95 的步进系数。
        double delta = e.Delta.Y * 120.0;
        double from = Offset;
        double to = Clamp.Of(((_target < 0.0) ? from : _target) - delta * 0.95, 0.0, ScrollableHeight);
        _target = to;
        AnimateTo(from, to);
    }

    private async void AnimateTo(double from, double to)
    {
        // C2b:与 OnPointerWheelChanged 的守卫同一个判断(Motion 门面把"用户开关"与
        // "有没有人在推进时钟"两条都算进去了,见 Motion 的类注释与 D-69)。
        // 没有时钟时不做平滑,直接把偏移落到目标位置 —— 也就是关掉平滑时的终态。
        if (!Motion.Enabled)
        {
            SetCurrentValue(OffsetProperty, to);
            _target = -1.0;
            return;
        }

        try
        {
            var animation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(230.0),
                Easing = new CubicEaseOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(OffsetProperty, from) } },
                    new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(OffsetProperty, to) } },
                },
            };

            await animation.RunAsync(this);
        }
        catch (Exception)
        {
            // 动画是装饰性的:拿不到时钟、或者控件已经被收掉,都不该把界面带崩。
        }
        finally
        {
            _target = -1.0;
        }
    }
}
