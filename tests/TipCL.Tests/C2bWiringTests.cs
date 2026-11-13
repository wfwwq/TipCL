using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C2b:服务接线(C2a 验收清单)+ 离屏(<c>--smoke</c>)挂点 + 两个 Motion 遗留点。
/// 一切 Avalonia 工作都在 <c>AvaloniaSession.Run</c> 里(D-79);全局静态由夹具还原。
/// </summary>
public class C2bWiringTests
{
    // ---------------------------------------------------------------- 接线

    [Fact]
    public void AppServices_Window_points_at_the_shell()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create();
            Assert.Same(shell.Window, AppServices.Window);
            Assert.IsAssignableFrom<MainWindow>(AppServices.Window);
        });
    }

    [Fact]
    public void Dlg_host_and_blur_target_are_wired_to_the_shell()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create();
            Assert.NotNull(Dlg.Host);
            Assert.Same(shell.Window.DialogHostPanel, Dlg.Host);
            Assert.Same(shell.Window.ContentLayerPanel, Dlg.BlurTarget);
            // 对话层宿主一开始是空的(没有对话打开)
            Assert.False(Dlg.AnyOpen);
            Assert.Same(shell.Window.Host, shell.Window.ContentLayerPanel.GetVisualDescendants().OfType<PageHost>().First());
        });
    }

    [Fact]
    public void Toast_host_panel_exists_but_the_C2b_shell_leaves_the_toast_sink_unwired()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create();
            Assert.NotNull(shell.Window.ToastHostPanel);
            Assert.Empty(shell.Window.ToastHostPanel.Children);
            // 【C2b 的决定,见 D-92】Toast 卡片是 C2c 的视觉件,本包故意不接 ToastSink:
            // 为空时 AppServices.Toast 只写调试输出(不抛异常、不改状态)。
            // C2c 把外壳的 Toast 方法挂上来之后,这条断言要跟着改成"非空且发出卡片"。
            Assert.Null(AppServices.ToastSink);
            AppServices.Toast("标题", "正文", ToastKind.Warning);
            Assert.Empty(shell.Window.ToastHostPanel.Children);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Motion_user_switch_follows_the_injected_preference(bool anim)
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create(new UiPrefs { Anim = anim });
            Assert.Equal(anim, Motion.UserEnabled);
            // Motion.Enabled 还要求"有人在推进动画时钟":测试进程里 SetupWithoutStarting 不设
            // ApplicationLifetime(D-69),所以这里恒 false —— 这正是离屏/测试不会挂住动画的原因。
            Assert.False(Motion.Enabled);
        });
    }

    [Fact]
    public void Motion_Enabled_stays_false_without_a_clock_even_when_the_user_switch_is_on()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create(new UiPrefs { Anim = true });
            Assert.True(Motion.UserEnabled);
            // Motion.Enabled 还要求"有人在推进动画时钟"(D-69),而测试进程里没有:
            // Avalonia 在 Application 初始化之后**不允许**再改 ApplicationLifetime
            // (本机实测抛 InvalidOperationException: It's not possible to change ApplicationLifetime
            // after Application was initialized),而 C1b-fix 又只允许 AvaloniaSession 一处起平台,
            // 所以"动画开着"那条分支在测试进程里**结构上不可达** —— 这是本包的一条未验证项,
            // 已写进 PROGRESS.md 的 C2b 小节。
            Assert.False(Motion.Enabled);
            Motion.Enabled = true;
            Assert.True(Motion.UserEnabled);
            Assert.False(Motion.Enabled);
            Motion.Enabled = false;
            Assert.False(Motion.UserEnabled);
        });
    }

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(1.6, 1.6)]
    [InlineData(9.0, 2.2)]
    [InlineData(0.1, 0.4)]
    public void Motion_scale_follows_the_preference_with_the_windows_clamp(double pref, double expected)
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create(new UiPrefs { AnimScale = pref });
            Assert.Equal(expected, Motion.Scale);
        });
    }

    // ---------------------------------------------------------------- 离屏挂点

    [Fact]
    public void Offscreen_mode_wires_all_three_Dlg_hooks_and_restores_them()
    {
        AvaloniaSession.Run(() =>
        {
            bool beforeActive = Dlg.SmokeActive;
            Action<Control, Action>? beforeAnswer = Dlg.SmokeAutoAnswer;
            Action<string>? beforeCancel = Dlg.SmokeAutoCancelSystemDialog;
            using (Smoke.SmokeState.Activate())
            {
                Assert.True(Dlg.SmokeActive);
                Assert.NotNull(Dlg.SmokeAutoAnswer);
                Assert.NotNull(Dlg.SmokeAutoCancelSystemDialog);
                Assert.True(Smoke.SmokeState.Active);
            }
            Assert.Equal(beforeActive, Dlg.SmokeActive);
            Assert.Equal(beforeAnswer, Dlg.SmokeAutoAnswer);
            Assert.Equal(beforeCancel, Dlg.SmokeAutoCancelSystemDialog);
            Assert.False(Smoke.SmokeState.Active);
        });
    }

    [Fact]
    public void Offscreen_confirm_returns_the_scripted_answer_without_any_user()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create(smoke: true);
            Task<bool> answer = Dlg.Confirm("自动应答", "正文");
            Assert.True(PumpUntil(() => answer.IsCompleted), "离屏对话框没有自动应答完");
            // 自动应答优先点「取消」→ Confirm 得到 false(与 Windows Smoke.AutoAnswerDialog 同一策略)
            Assert.False(answer.Result);
            Assert.Contains("自动应答", Smoke.SmokeState.Dialogs);
        });
    }

    [Fact]
    public void Offscreen_alert_returns_when_the_only_button_is_clicked()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create(smoke: true);
            Task alert = Dlg.Alert("提示", "正文");
            Assert.True(PumpUntil(() => alert.IsCompleted), "离屏 Alert 没有自动应答完");
            Assert.Empty(Dlg.Host!.Children);
        });
    }

    [Fact]
    public void Offscreen_system_file_dialog_is_cancelled_without_touching_the_disk()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create(smoke: true);
            Assert.Null(Dlg.PickFile("文本|*.txt", "选一个文件"));
            Assert.Null(Dlg.SaveFile("文本|*.txt", "a.txt", "保存到"));
            Assert.Null(Dlg.PickFolder("选一个文件夹"));
            Assert.Contains(Smoke.SmokeState.Dialogs, d => d.Contains("选一个文件", StringComparison.Ordinal));
            Assert.Contains(Smoke.SmokeState.Dialogs, d => d.Contains("选一个文件夹", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Auto_answer_prefers_a_cancel_button_and_falls_back_to_dismiss()
    {
        AvaloniaSession.Run(() =>
        {
            bool dismissed = false;
            bool cancelClicked = false;
            var card = new StackPanel();
            var cancel = new Button { Content = "取消" };
            cancel.Click += delegate
            {
                cancelClicked = true;
            };
            card.Children.Add(new TextBlock { Text = "探针标题" });
            card.Children.Add(cancel);
            card.Children.Add(new Button { Content = "删除" }); // 破坏性按钮:绝不自动点

            Smoke.SmokeState.AutoAnswerDialog(card, delegate
            {
                dismissed = true;
            });
            Assert.True(PumpUntil(() => cancelClicked || dismissed));
            Assert.True(cancelClicked);
            Assert.False(dismissed);

            // 没有任何安全按钮 → 关掉浮层(dismiss)
            dismissed = false;
            var bare = new StackPanel { Children = { new TextBlock { Text = "无按钮" }, new Button { Content = "删除" } } };
            Smoke.SmokeState.AutoAnswerDialog(bare, delegate
            {
                dismissed = true;
            });
            Assert.True(PumpUntil(() => dismissed));
        });
    }

    [Fact]
    public void Smoke_note_and_operation_bookkeeping_only_count_in_offscreen_mode()
    {
        AvaloniaSession.Run(() =>
        {
            // 非离屏:记账一律空转(Notes 列表是进程级静态,可能还留着别的用例留下的条目,
            // 所以这里对"条数不变"下断言,而不是对"空"下断言)
            Assert.False(Smoke.SmokeState.Active);
            int before = Smoke.SmokeState.Issues.Count;
            Assert.Equal(0L, Smoke.SmokeState.OperationStarted());
            Smoke.SmokeState.Note("位置", "不该被记下来");
            Assert.Equal(before, Smoke.SmokeState.Issues.Count);

            using (Smoke.SmokeState.Activate())
            {
                long op = Smoke.SmokeState.OperationStarted();
                Assert.NotEqual(0L, op);
                Assert.Equal(1, Smoke.SmokeState.PendingOperations);
                Smoke.SmokeState.Note("位置", "这一条要记下来");
                Assert.Contains("位置: 这一条要记下来", Smoke.SmokeState.Issues);
                Smoke.SmokeState.OperationFinished(op);
                Assert.Equal(0, Smoke.SmokeState.PendingOperations);
                Smoke.SmokeState.OperationFinished(0L); // 0 号操作直接忽略,不抛
            }
        });
    }

    [Fact]
    public void PageBase_Run_reports_failures_through_toast_without_throwing()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create(smoke: true);
            bool finished = false;
            PageBase.Run(async delegate
            {
                await Task.Yield();
                finished = true;
            });
            Assert.True(PumpUntil(() => finished));
            Assert.Equal(0, Smoke.SmokeState.PendingOperations);

            // 取消异常被吞掉,其它异常记一笔 + Toast(ToastSink 为空 → 只写调试输出,不抛)
            PageBase.Run(async delegate
            {
                await Task.Yield();
                throw new InvalidOperationException("探针失败");
            });
            Assert.True(PumpUntil(() => Smoke.SmokeState.Issues.Any(i => i.Contains("探针失败", StringComparison.Ordinal))));
            Assert.Equal(0, Smoke.SmokeState.PendingOperations);
        });
    }

    [Fact]
    public void Probe_page_reports_load_failures_as_smoke_notes()
    {
        AvaloniaSession.Run(() =>
        {
            var failing = new FailingPage();
            using var shell = ShellFixture.Create(smoke: true);
            shell.Window.BridgeReady = true;
            var host = new PageHost(id => id == "failing" ? failing : null);
            Assert.True(host.Navigate("failing", instant: true, forward: true, runHooks: true));
            // 加载失败:PageBase 记一笔 Smoke 笔记(位置 = 页面 id)、Toast 一次,不把异常抛给外壳
            Assert.True(PumpUntil(() => Smoke.SmokeState.Issues.Any(i => i.StartsWith("failing:", StringComparison.Ordinal))));
            Assert.Equal(1, failing.Loads);
            // 失败之后页面仍是"未加载"状态:再 EnsureLoadedAsync 会重试(而不是被当成已加载)
            host.GetPage("failing")!.EnsureLoadedAsync();
            Assert.True(PumpUntil(() => failing.Loads == 2));
        });
    }

    // ---------------------------------------------------------------- Motion 遗留点

    [Fact]
    public void Motion_Loop_calls_neither_side_when_the_animation_is_off()
    {
        AvaloniaSession.Run(() =>
        {
            Assert.False(Motion.Enabled);
            int started = 0;
            int stopped = 0;
            var owner = new Border();
            Motion.Loop(owner, delegate
            {
                started++;
            }, delegate
            {
                stopped++;
            });
            // 关掉动画(没有时钟)时 Motion.Loop 既不 start 也不 stop:
            // 控件保持在调用方的初态,这正是"立刻落终态"的实现方式(见 Motion 的类注释)。
            Assert.Equal(0, started);
            Assert.Equal(0, stopped);
        });
    }

    [Fact]
    public void Skeleton_reaches_its_static_state_when_motion_is_off()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create(new UiPrefs { Anim = false });
            Border skeleton = Ui.Skeleton(16.0, 120.0);
            Assert.Equal(16.0, skeleton.Height);
            Assert.Equal(120.0, skeleton.Width);
            var glide = Assert.IsType<Border>(skeleton.Child);
            var transform = Assert.IsType<TranslateTransform>(glide.RenderTransform);
            // 横扫停在起点(-90),没有动画在跑 —— 就是 WPF 关掉动画时那块静态灰条
            Assert.Equal(-90.0, transform.X);

            shell.Window.Content = skeleton;
            UiHost.Flush();
            Assert.Equal(-90.0, transform.X);
        });
    }

    [Fact]
    public void SmoothScroll_wheel_guard_follows_Motion_and_the_scrollable_extent()
    {
        AvaloniaSession.Run(() =>
        {
            using var host = ScrollHost();
            var scroll = (SmoothScroll)host.Window.Content!;
            // 关掉动画(测试进程里就是常态)→ 不做平滑,交给基类的系统滚动(Windows 的 base.OnMouseWheel)
            Assert.False(scroll.ShouldSmoothWheel);
            // 没有可滚动高度时也不做平滑(Windows 的第二个守卫条件)
            var empty = new SmoothScroll { Width = 100.0, Height = 100.0, Content = new Border { Height = 10.0 } };
            using var small = UiHost.Show(empty);
            Assert.False(empty.ShouldSmoothWheel);
            // 值能设进去、拿得出来(与 C1c 的同名断言一致)。
            // 注:"设进去之后基类的 Vector Offset 真的跟着动"在本 harness 里验不了:headless 下
            // ScrollViewer 的 Extent/Viewport 读到的是 0(实测),而基类 Offset 会被滚到范围里
            // (0~extent-viewport)夹住 → 只能读到 0。这条与"系统滚动滚了多少"一并记进未验证项。
            scroll.Offset = 42.0;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(42.0, scroll.Offset);
        });
    }

    [Fact]
    public void SmoothScroll_wheel_event_does_not_throw_and_does_not_smooth_when_motion_is_off()
    {
        AvaloniaSession.Run(() =>
        {
            using var host = ScrollHost();
            var scroll = (SmoothScroll)host.Window.Content!;
            Assert.False(Motion.Enabled);
            host.Window.MouseMove(new Point(4.0, 4.0), Avalonia.Input.RawInputModifiers.None);
            host.Window.MouseWheel(new Point(4.0, 4.0), new Vector(0.0, -3.0), Avalonia.Input.RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            // 平滑那条路没有被走:SmoothScroll 自己的 double Offset(只有平滑分支会写)还是 0。
            // 注:headless harness 里基类 ScrollViewer 的系统滚动**不生效**(本机实测:同样条件下
            // 一个 plain ScrollViewer 的 Offset 在滚轮事件后仍是 0),所以"系统滚动滚了多少"验不了 ——
            // 这条记进 PROGRESS.md 的未验证项。
            Assert.Equal(0.0, scroll.Offset);
        });
    }

    [Fact]
    public void Lucide_MotionEnabled_forwards_to_Motion_in_both_directions()
    {
        AvaloniaSession.Run(() =>
        {
            bool before = Lucide.MotionEnabled;
            try
            {
                Lucide.MotionEnabled = false;
                Assert.False(Motion.UserEnabled);
                Lucide.MotionEnabled = true;
                Assert.True(Motion.UserEnabled);
                // 读的是"用户开关"(不含时钟);写的是 Motion.Enabled(改的就是用户开关本身)
                Assert.Equal(Motion.UserEnabled, Lucide.MotionEnabled);
            }
            finally
            {
                Lucide.MotionEnabled = before;
            }
        });
    }

    [Fact]
    public void Lucide_animations_still_build_their_visuals_with_motion_on_in_a_clockless_process()
    {
        AvaloniaSession.Run(() =>
        {
            // 【C1a 的契约】Lucide 的内部动画自带时钟守卫(见 Lucide.Animate),
            // 所以即使 Motion 用户开关开着、进程里没有时钟,构造/调用也不许抛。
            bool before = Lucide.MotionEnabled;
            try
            {
                Lucide.MotionEnabled = true;
                var text = new TextBlock { Text = "TipCL", Foreground = TipCL.Themes.Palette.Brush("B.Ink") };
                Lucide.Shimmer(text, true, "B.Ink");
                Assert.IsType<LinearGradientBrush>(text.Foreground);
                var chevron = Lucide.Icon(Lucide.ChevronRight, 16.0, "B.Ink");
                Lucide.Rotate(chevron, true);
                Assert.NotNull(chevron.RenderTransform);
            }
            finally
            {
                Lucide.MotionEnabled = before;
            }
        });
    }

    // ---------------------------------------------------------------- 小工具

    /// <summary>把一段高内容塞进 <see cref="SmoothScroll"/>,便于验滚轮的两条分支。</summary>
    private static UiHost ScrollHost()
    {
        var scroll = new SmoothScroll { Width = 300.0, Height = 200.0 };
        scroll.Content = new Border { Height = 1200.0, Background = Brushes.Gray };
        return UiHost.Show(scroll);
    }

    /// <summary>把排队的 Dispatcher 工作跑掉直到条件成立(上限 <paramref name="rounds"/> 轮)。</summary>
    private static bool PumpUntil(Func<bool> condition, int rounds = 200, int sleepMs = 2)
    {
        for (int i = 0; i < rounds; i++)
        {
            if (condition())
            {
                return true;
            }
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(sleepMs);
        }
        return condition();
    }

    /// <summary>
    /// 临时造一个桌面生命周期,让 <c>Motion.HasClock</c> 为真 —— **本机实测走不通**:
    /// Avalonia 在 Application 初始化之后不允许再改 <c>ApplicationLifetime</c>
    /// (抛 <c>InvalidOperationException: It's not possible to change ApplicationLifetime after
    /// Application was initialized</c>)。保留这段代码只作为取证记录,不参与编译。
    /// </summary>
    private static class ClockScopeNote
    {
        internal const string Reason =
            "Avalonia 禁止在 Application 初始化后改 ApplicationLifetime,所以测试进程里 Motion.Enabled 恒为 false;" +
            "动画开着的那条分支只能在真桌面运行时验证(见 PROGRESS.md 的 C2b 未验证项)。";
    }
}

/// <summary>加载必失败的探针页(验 PageBase 的失败路径:记 Smoke 笔记 + Toast,不抛)。
/// <c>LoadAsync</c> 故意做成**异步**失败(先让出再抛):同步抛会让 <c>_inflight</c> 的赋值
/// 落在 <c>finally</c> 置空之后,失败后的重试就永远不生效 —— 那是 Windows 同名代码的固有顺序,
/// 真实页面都是异步加载,这里如实照做。</summary>
internal sealed class FailingPage : PageBase
{
    internal int Loads { get; private set; }

    protected override async Task LoadAsync()
    {
        Loads++;
        await Task.Yield();
        throw new InvalidOperationException("加载注定失败");
    }
}
