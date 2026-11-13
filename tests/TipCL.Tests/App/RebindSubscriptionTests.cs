// M4-H R21h 收尾:把 PARITY §9.5 里「悬停重新 Res.Bind,旧订阅到底有没有被释放」这条**从未验证**
// 改成有可执行断言。
//
// 背景:src/TipCL.App/Pages/Ai/MdView.cs 的链接悬停是每次进出都**重挂一条观察绑定**(不是直接赋画刷):
//   inner.PointerEntered += (_, _) => inner[!TextBlock.ForegroundProperty] = Res.Bind("B.Accent");
//   inner.PointerExited  += (_, _) => inner[!TextBlock.ForegroundProperty] = Res.Bind("B.AccentText");
// 其中 Res.Bind(key) = Application.Current.GetResourceObservable(key).ToBinding()(Common/Res.cs:13)
// —— 每挂一次就多订阅一次资源变更。鼠标在链接上来回滑动会挂很多次,所以「旧的有没有被释放」值得钉死。
//
// 这里两条用例分工不同,如实区分:
//   1 【机制:证明「重挂 = 释放旧订阅」】框架源码级结论 + 可执行断言。
//     Avalonia 11.3.2 的 `AvaloniaObject.Bind(AvaloniaProperty, IBinding, anchor)`(AvaloniaObject.cs:659)
//     → `ValueStore.AddBinding(property, expression)`(src/Avalonia.Base/PropertyStore/ValueStore.cs:48);
//     该分支在 `priority == BindingPriority.LocalValue` 时**先**调
//     `DisposeExistingLocalValueBinding(property)`(同文件:1238-1245)**再**装新绑定;
//     而 `!property = binding` 这条索引器写法与 `Res.Bind` 走的都是 `priority == LocalValue`。
//     用例用一个「能数活跃订阅」的 observable 走同一个 ValueStore 入口
//     (`Bind<T>(StyledProperty<T>, IObservable<T>)`,默认优先级就是 LocalValue):第二次绑定之后,
//     第一个 observable 的活跃订阅数必须归零。这条是决定性的。
//   2 【真实路径:用户看得见的那件事】用 MdView 里那条真链接控件,按悬停的写法来回重挂 200 次,
//     再切深浅色 —— 字色必须等于**当前**主题对应键的颜色,不能把某一档的画刷留下来。
//     ⚠ 如实说明:这条**不能**单独证明订阅被释放 —— 就算真泄漏,最后订阅的那条仍会最后推值、
//     颜色看着也对;释放由第 1 条证明。两条合起来才是完整结论。
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using TipCL.App.Common;
using TipCL.App.Pages.Ai;
using TipCL.App.Shell;
using Xunit;

namespace TipCL.Tests.App;

public class RebindSubscriptionTests
{
    /// <summary>能数「当前有几个活跃订阅」的 observable(Dispose 时减一)。</summary>
    private sealed class Counting<T> : IObservable<T>
    {
        private readonly T _value;

        public Counting(T value) => _value = value;

        /// <summary>当前活跃订阅数。</summary>
        public int Live { get; private set; }

        /// <summary>被订阅过的总次数(用来确认用例真的走过重挂,而不是一次都没挂上)。</summary>
        public int TotalSubscribed { get; private set; }

        public IDisposable Subscribe(IObserver<T> observer)
        {
            Live++;
            TotalSubscribed++;
            observer.OnNext(_value);
            return new Sub(this);
        }

        private sealed class Sub : IDisposable
        {
            private readonly Counting<T> _owner;
            private bool _done;

            public Sub(Counting<T> owner) => _owner = owner;

            public void Dispose()
            {
                if (_done) return;
                _done = true;
                _owner.Live--;
            }
        }
    }

    /// <summary>把无头渲染队列推干净(主题切换 / 资源通知后用)。</summary>
    private static void Pump()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Application 级主题色的当前值(资源没挂上直接失败,别让断言拿 null 蒙混)。</summary>
    private static Color Palette(string key)
    {
        Assert.True(Application.Current!.TryGetResource(key, null, out var value), $"Application 级缺少主题键 {key}");
        return Assert.IsType<SolidColorBrush>(value).Color;
    }

    private static Color? ForegroundOf(TextBlock tb) => (tb.Foreground as SolidColorBrush)?.Color;

    // ───────────────── 1) 机制:重挂会释放旧订阅(决定性) ─────────────────

    /// <summary>
    /// 同一属性上第二次绑定之后,第一个 observable 的活跃订阅必须归零 —— 走的是与 `Res.Bind`
    /// 完全相同的 ValueStore 入口(默认优先级 LocalValue)。
    /// </summary>
    [Fact]
    public void Rebind_ReleasesThePreviousSubscription() => TestApp.Run(() =>
    {
        var tb = new TextBlock();
        var first = new Counting<double>(0.25);
        var second = new Counting<double>(0.75);

        tb.Bind(Visual.OpacityProperty, first);
        Assert.Equal(1, first.Live);                  // 先确认真的订阅上了
        Assert.Equal(0.25, tb.Opacity);

        tb.Bind(Visual.OpacityProperty, second);

        Assert.Equal(0, first.Live);                  // ← 旧订阅被框架释放(本用例的意思所在)
        Assert.Equal(1, second.Live);
        Assert.Equal(1, first.TotalSubscribed);       // 只订阅过一次(不是重复挂同一份)
        Assert.Equal(0.75, tb.Opacity);               // 生效值是最新那条
    });

    /// <summary>
    /// 悬停那样来回重挂 200 次:活跃订阅数必须**恒为 1**(等于最后一次那条),
    /// 其余 199 条全部已释放 —— 泄漏的话这里会看到 199。
    /// </summary>
    [Fact]
    public void Rebind_ManyTimes_LeavesExactlyOneLiveSubscription() => TestApp.Run(() =>
    {
        var tb = new TextBlock();
        var hover = new Counting<double>(0.1);        // 对应 PointerEntered(B.Accent)
        var normal = new Counting<double>(0.9);       // 对应 PointerExited(B.AccentText)

        for (int i = 0; i < 100; i++)
        {
            tb.Bind(Visual.OpacityProperty, hover);
            tb.Bind(Visual.OpacityProperty, normal);
        }

        Assert.Equal(100, hover.TotalSubscribed);     // 真的挂了 100 次(用例本身有效)
        Assert.Equal(100, normal.TotalSubscribed);
        Assert.Equal(0, hover.Live);
        Assert.Equal(1, normal.Live);
        Assert.Equal(0.9, tb.Opacity);

        // 反向再来一轮:最后一次挂的是 hover,那么活着的必须是 hover
        tb.Bind(Visual.OpacityProperty, hover);
        Assert.Equal(0, normal.Live);
        Assert.Equal(1, hover.Live);
        Assert.Equal(0.1, tb.Opacity);
    });

    // ───────────────── 2) 真实路径:MdView 链接来回悬停后仍跟着主题 ─────────────────

    /// <summary>MdView 里那条真链接的内层控件(InlineUIContainer 里的 TextBlock,MdView.cs:323-347)。</summary>
    private static TextBlock LinkBlock()
    {
        var view = new MdView();
        view.SetMarkdown("见 [站点](https://example.com) 一档。");
        var host = (StackPanel)((ScrollViewer)view.Content!).Content!;
        TextBlock paragraph = host.Children.OfType<TextBlock>().First();
        InlineUIContainer container = paragraph.Inlines!.OfType<InlineUIContainer>().First();
        return (TextBlock)container.Child!;
    }

    /// <summary>
    /// 按悬停的写法重挂 200 次(两个键来回切),然后切一次深浅色:字色必须等于当前主题里对应键的颜色。
    /// 两个键在每一档里颜色不同,所以「留下旧画刷」会被看出来。
    /// </summary>
    [Fact]
    public void LinkHoverRebind_KeepsFollowingTheTheme() => TestApp.Run(() =>
    {
        var window = new MainWindow();                 // 挂 Application 级 Palette,否则 Res.Bind 解析为 null
        TextBlock link = LinkBlock();
        bool before = window.IsDark;
        try
        {
            foreach (bool dark in new[] { false, true })
            {
                window.SetDark(dark);
                Pump();

                Color normal = Palette("B.AccentText");
                Color hover = Palette("B.Accent");
                Assert.NotEqual(normal, hover);         // 两个键必须画得出区别,否则这条用例没有区分度

                // 单次:挂哪个键就是哪个颜色(先证明 Res.Bind 这条路径本身是活的)
                link[!TextBlock.ForegroundProperty] = Res.Bind("B.Accent");
                Pump();
                Assert.Equal(hover, ForegroundOf(link));
                link[!TextBlock.ForegroundProperty] = Res.Bind("B.AccentText");
                Pump();
                Assert.Equal(normal, ForegroundOf(link));

                // 来回 100 轮(模拟鼠标反复进出)之后停在「常态色»
                for (int i = 0; i < 100; i++)
                {
                    link[!TextBlock.ForegroundProperty] = Res.Bind("B.Accent");
                    link[!TextBlock.ForegroundProperty] = Res.Bind("B.AccentText");
                }
                Pump();
                Assert.Equal(normal, ForegroundOf(link));

                // 这一档里再切主题:颜色必须跟着走(不是把这一档的画刷留下来)
                window.SetDark(!dark);
                Pump();
                Color flipped = Palette("B.AccentText");
                Assert.NotEqual(normal, flipped);       // 两档颜色确实不同
                Assert.Equal(flipped, ForegroundOf(link));
                window.SetDark(dark);
                Pump();
                Assert.Equal(Palette("B.AccentText"), ForegroundOf(link));
            }
        }
        finally
        {
            window.SetDark(before);                     // 别把主题留给别的用例
        }
    });
}
