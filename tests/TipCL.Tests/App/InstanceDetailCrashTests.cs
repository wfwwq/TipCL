using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.App.Common;
using TipCL.App.Pages.Library;
using TipCL.App.Pages.Version;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>
/// M4-H R9 真机串行集合。本组用例会读 `default_instance` 这类进程级状态并真的走布局,而 xUnit 默认按
/// 测试类并行 —— 和其它类抢同一个进程、同一个无头 UI 会话会让它们互相踩。这组声明禁用并行
/// (只影响本集合,不改全局配置)。
/// </summary>
[CollectionDefinition("TipCL 真机交互回归", DisableParallelization = true)]
public sealed class RealInteractionCollection
{
}

/// <summary>
/// M4-H R9 真机崩溃(缺陷 A)的正式回归。
///
/// 真机现象(盒子 main,X11):实例库里点版本行的「版本设置」齿轮 → 进程退出,`unhandled(UI)`:
/// <c>System.InvalidOperationException: The control StackPanel already has a visual parent Border
/// while trying to add it as a child of ScrollContentPresenter (Name = PART_ContentPresenter,
/// Host = ScrollViewer)</c>。栈自下往上:TemplatedControl.ApplyTemplate ← MeasureCore ←
/// ContentPresenter.MeasureOverride(_host 这个 ContentControl) ← Grid.MeasureCell(shell) ← …… 与本文件
/// 里那条路径**逐帧一致**。
///
/// 根因:`InstanceDetailPage` 构造函数里遗留的 `UiClass.Card(_rows, 0)`(那个 Border 从未进过可视树)
/// 把 `_rows` 收成了自己的子级,而 `ShowSeg` 又把同一个 `_rows` 塞进新 `ScrollViewer` 的 Content ——
/// 一个控件两个父级。Avalonia 报错的时机很隐蔽:给还没进可视树的 ScrollViewer 赋 Content 不会当场
/// 报错,要等它第一次 Measure(ApplyTemplate → ScrollContentPresenter.AttachToScrollViewer 绑定 Content)
/// 才抛,所以真机上表现成「点了齿轮之后进程突然没了」。
///
/// 为什么以前的复现台抓不到(两件事同时要成立):
///   1) 必须真的跑一次**布局 pass** —— 无头会话不自动走渲染计时器,`await Task.Delay(...)` 不触发
///      Measure,模板永远不 ApplyTemplate,异常一直潜伏。这里每次 `PassAsync` 都
///      `ForceRenderTimerTick` + `RunJobs` + `CaptureRenderedFrame`(冒烟截图用的那条路);
///   2) 必须走到**那条路径** —— 出事的是**实例库**(LibraryPage)版本行齿轮,它当时点下去是
///      `OpenDetailAsync` → 构造「实例详情」页;而「版本管理」页那枚同名齿轮走的是 `VersionSetupDialog`,
///      与崩溃无关。以前那个复现台直接 `Navigate("instance")`,压根没构造 `InstanceDetailPage`。
///      (M4-H R12 缺陷 2 已把这枚齿轮改回版本设置对话框,见本文件末尾两条用例;同一入口
///      `OpenDetailAsync` 仍由标题栏「实例详情」chip 与文件夹右键菜单进入,爆点路径照样覆盖。)
///
/// 本文件用**自己的 Window**(不借 `MainWindow.Navigate`)承载页面,把布局与交互都握在手里,
/// 这样在并行的全量测试里也不会被别的类带跑。
/// </summary>
[Collection("TipCL 真机交互回归")]
public class InstanceDetailCrashTests
{
    private const string Ver = "demo-1.0";

    private readonly IApi _api = InProcessApi.Instance;
    private readonly string _inst = "r9-" + Guid.NewGuid().ToString("N")[..8];

    private async Task SetupAsync()
    {
        await _api.CallAsync<object>("create_instance", new { name = _inst });
        string dir = Path.Combine(Instances.GetInstancePath(_inst), "versions", Ver);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, Ver + ".json"), $"{{\"id\":\"{Ver}\",\"type\":\"release\"}}");
        await _api.TryCallAsync<object>("save_settings", null, new { data = new { default_instance = _inst } });
    }

    private async Task TeardownAsync()
    {
        try { await _api.CallAsync<object>("delete_instance", new { name = _inst }); } catch (Exception) { }
    }

    /// <summary>
    /// 崩溃观察窗:捕获 Dispatcher 未处理异常 + 把「真布局 pass」变成可断言的步骤。
    /// `ForceRenderTimerTick` / `CaptureRenderedFrame` 里抛出来的异常会直接落在调用点
    /// (真机上它跑到 Dispatcher 变成 unhandled(UI)),两种都记进 <see cref="Errors"/>。
    /// </summary>
    private sealed class Watch : IDisposable
    {
        private readonly DispatcherUnhandledExceptionEventHandler _hook;

        public List<Exception> Errors { get; } = new();

        public Watch()
        {
            _hook = (_, e) =>
            {
                Errors.Add(e.Exception);
                e.Handled = true;
            };
            Dispatcher.UIThread.UnhandledException += _hook;
        }

        /// <summary>一次真布局:计时器 tick → 跑队列 → 捕获一帧(捕获会强制走完 Measure/Arrange)。</summary>
        public async Task PassAsync(TopLevel win, int times = 1)
        {
            for (int i = 0; i < times; i++)
            {
                try
                {
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                    Dispatcher.UIThread.RunJobs();
                    using var frame = win.CaptureRenderedFrame();
                }
                catch (Exception ex) { Errors.Add(ex); }
                await Task.Delay(40);
            }
        }

        /// <summary>等到条件成立(边等边跑布局)。页面数据是异步灌的,固定等 N 毫秒会抖。</summary>
        public async Task<bool> WaitAsync(TopLevel win, Func<bool> ready, int timeoutMs = 15000)
        {
            var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < end)
            {
                if (ready()) return true;
                await PassAsync(win);
            }
            return ready();
        }

        public void Dispose() => Dispatcher.UIThread.UnhandledException -= _hook;
    }

    private Window NewHost(Control page)
    {
        var win = new Window { Content = page, Width = 900, Height = 550 };
        win.Show();
        Dlg.Owner = win;
        return win;
    }

    /// <summary>按 tooltip 找控件(**逻辑树**,不依赖布局是否跑过)。</summary>
    private static Control? ByTip(ILogical root, string tip)
        => root.GetLogicalDescendants().OfType<Control>()
              .FirstOrDefault(c => ToolTip.GetTip(c) as string == tip);

    private static InstanceDetailPage? Detail(TopLevel win)
        => win.GetLogicalDescendants().OfType<InstanceDetailPage>().FirstOrDefault();

    /// <summary>进版本管理/实例库这类页面:走外壳的内部生命周期入口(OnCreate → 拉数据)。</summary>
    private static T Page<T>(T page) where T : PageBase
    {
        page.ShellAttach(null);
        return page;
    }

    // ───────────────────────── 缺陷 A:实例详情页的首次布局 ─────────────────────────

    /// <summary>崩溃现场本身:实例详情页第一次被布局。</summary>
    [Fact]
    public void InstanceDetail_FirstLayout_DoesNotCrash() => TestApp.RunAsync(async () =>
    {
        await SetupAsync();
        using var watch = new Watch();
        try
        {
            var page = new InstanceDetailPage(null!, new InstanceDetailPage.InstInfo { Name = _inst, Versions = 1 }, () => { }, null);
            var win = NewHost(page);
            var rows = RowHost(page);
            await watch.WaitAsync(win, () => rows.Bounds.Width > 0);

            Assert.Empty(watch.Errors);
            Assert.True(rows.Bounds.Width > 0, "实例详情的行容器没被布局过,这个用例就没验到东西");
        }
        finally { await TeardownAsync(); }
    });

    /// <summary>打开 → 关闭 → 再打开(三次),每次都重新布局:重复进出不能出问题。</summary>
    [Fact]
    public void InstanceDetail_OpenBackOpenAgain_DoesNotCrash() => TestApp.RunAsync(async () =>
    {
        await SetupAsync();
        using var watch = new Watch();
        try
        {
            var placeholder = new Border();
            var page = new InstanceDetailPage(null!, new InstanceDetailPage.InstInfo { Name = _inst, Versions = 1 }, () => { }, null);
            var win = NewHost(page);
            await watch.WaitAsync(win, () => RowHost(page).Bounds.Width > 0);

            for (int round = 0; round < 3; round++)
            {
                ((ContentControl)win).Content = placeholder;   // 关
                await watch.PassAsync(win, 2);
                ((ContentControl)win).Content = page;          // 再打开
                await watch.WaitAsync(win, () => RowHost(page).Bounds.Width > 0);
                await watch.PassAsync(win, 2);
                Assert.Empty(watch.Errors);
            }
        }
        finally { await TeardownAsync(); }
    });

    /// <summary>
    /// 五个分段来回切:以前 `ShowSeg` 每切一次就新建一个 ScrollViewer 去装同一个 `_rows`,
    /// 旧容器的 ScrollContentPresenter 仍然持有它 —— 第二次切换就是同一族崩溃的下一发。
    /// </summary>
    [Fact]
    public void InstanceDetail_SwitchEverySegment_DoesNotCrash() => TestApp.RunAsync(async () =>
    {
        await SetupAsync();
        using var watch = new Watch();
        try
        {
            var page = new InstanceDetailPage(null!, new InstanceDetailPage.InstInfo { Name = _inst, Versions = 1 }, () => { }, null);
            var win = NewHost(page);
            await watch.WaitAsync(win, () => RowHost(page).Bounds.Width > 0);

            var segs = page.GetLogicalDescendants().OfType<ToggleButton>()
                .Where(t => t.Content is string s && new[] { "模组", "存档", "资源包", "光影", "设置" }.Contains(s))
                .ToList();
            Assert.Equal(5, segs.Count);
            foreach (var seg in segs.Concat(Enumerable.Reverse(segs)))
            {
                seg.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await watch.PassAsync(win, 2);
            }
            Assert.Empty(watch.Errors);
        }
        finally { await TeardownAsync(); }
    });

    /// <summary>
    /// 结构断言(不依赖「崩不崩」):详情页里装行的那个 StackPanel 只属于**一个** ScrollViewer,
    /// 且它的可视父级就是那个 ScrollViewer 模板里的 ScrollContentPresenter。
    /// </summary>
    [Fact]
    public void InstanceDetail_RowHost_HasExactlyOneParent() => TestApp.RunAsync(async () =>
    {
        await SetupAsync();
        using var watch = new Watch();
        try
        {
            var page = new InstanceDetailPage(null!, new InstanceDetailPage.InstInfo { Name = _inst, Versions = 1 }, () => { }, null);
            var win = NewHost(page);
            await watch.WaitAsync(win, () => RowHost(page).GetVisualParent() != null);

            var hosts = page.GetVisualDescendants().OfType<ScrollViewer>()
                .Where(s => s.Content is StackPanel).ToList();
            Assert.Single(hosts);
            var rows = RowHost(page);
            Assert.Same(hosts[0], rows.GetVisualParent()!.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault(s => s.Content is StackPanel));
            Assert.IsType<ScrollContentPresenter>(rows.GetVisualParent());
            Assert.Empty(watch.Errors);
        }
        finally { await TeardownAsync(); }
    });

    // ───────────── 缺陷 A:真机上那条路径(实例库 → 实例详情)+ R12 齿轮对位 ─────────────

    /// <summary>
    /// 复刻真机操作走到「实例详情」页并真布局一次。R12 之前这里点的是版本行齿轮(它当时挂
    /// `OpenDetailAsync`);R12 把齿轮改回版本设置对话框后,同一入口改由标题栏的「实例详情」chip
    /// 触发(`LibraryPage.cs:130`,右键菜单 `FolderMenu` 里那条走的是同一个 `OpenDetailAsync`)。
    /// </summary>
    [Fact]
    public void LibraryDetailChip_OpensDetail_FirstLayout_DoesNotCrash() => TestApp.RunAsync(async () =>
    {
        await SetupAsync();
        using var watch = new Watch();
        try
        {
            var lib = Page(new LibraryPage());
            var win = NewHost(lib);
            // chip 在构造函数里就有,但 OpenDetailAsync 要 _folder(等版本行渲染出来才算数据到位)
            Assert.True(await watch.WaitAsync(win, () => ByName(lib, "实例详情") != null && ByTip(lib, "版本设置") != null),
                "实例库的「实例详情」chip / 版本行没就绪");

            ByName(lib, "实例详情")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(await watch.WaitAsync(win, () => Detail(win) != null), "实例详情页没打开");
            await watch.WaitAsync(win, () => Detail(win)!.GetVisualDescendants().OfType<ScrollViewer>().Any(s => s.Content is StackPanel));
            await watch.PassAsync(win, 3);

            Assert.Empty(watch.Errors);
        }
        finally { await TeardownAsync(); }
    });

    /// <summary>
    /// M4-H R12 缺陷 2:版本行齿轮 = **版本设置对话框**(与 Windows 对位,`LibraryPage.cs:713-720`),
    /// 不再把页面切成「实例详情」。关窗后列表会重新加载;tooltip 仍是「版本设置」。
    /// </summary>
    [Fact]
    public void LibraryGear_OpensVersionSetup_NotDetail() => TestApp.RunAsync(async () =>
    {
        await SetupAsync();
        using var watch = new Watch();
        try
        {
            var lib = Page(new LibraryPage());
            var win = NewHost(lib);
            Assert.True(await watch.WaitAsync(win, () => ByTip(lib, "版本设置") != null), "实例库版本行的齿轮没出现");

            ByTip(lib, "版本设置")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(await watch.WaitAsync(win, () => win.OwnedWindows.Count > 0), "版本设置对话框没弹出来");
            Assert.True(Dlg.AnyOpen, "对话框没进入模态状态");
            Assert.Null(Detail(win));   // 齿轮不再打开实例详情页

            // 收尾:关掉对话框(别把模态留给后面的用例),关窗后 LibraryPage 会再 LoadAsync 一次
            foreach (var owned in win.OwnedWindows.ToList()) owned.Close();
            Assert.True(await watch.WaitAsync(win, () => !Dlg.AnyOpen), "对话框没关掉");
            await watch.PassAsync(win, 2);

            Assert.Null(Detail(win));
            Assert.Empty(watch.Errors);
        }
        finally { await TeardownAsync(); }
    });

    /// <summary>按无障碍名找控件(`Ui.Btn` 会把文案写进 AutomationProperties.Name)。</summary>
    private static Control? ByName(ILogical root, string name)
        => root.GetLogicalDescendants().OfType<Control>()
              .FirstOrDefault(c => Avalonia.Automation.AutomationProperties.GetName(c) == name);

    /// <summary>详情页里那个装行的 StackPanel(= `_rows`,唯一拥有者见上一条断言)。</summary>
    private static StackPanel RowHost(InstanceDetailPage page)
        => page.GetLogicalDescendants().OfType<ScrollViewer>()
              .Select(s => s.Content).OfType<StackPanel>().First();
}
