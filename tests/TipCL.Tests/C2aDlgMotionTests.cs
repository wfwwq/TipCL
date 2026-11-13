using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C2a:Dlg 的浮层宿主演习器。把 <see cref="Dlg.Host"/> 指到一个挂在真窗口里的 Grid,
/// 用完还原(全局静态纪律,见 D-69)。
/// </summary>
internal sealed class DlgHarness : IDisposable
{
    private readonly Grid? _previousHost;

    private readonly Control? _previousBlur;

    private readonly UiHost _window;

    private DlgHarness(UiHost window, Grid host, Grid? previousHost, Control? previousBlur)
    {
        _window = window;
        Host = host;
        _previousHost = previousHost;
        _previousBlur = previousBlur;
    }

    internal Grid Host { get; }

    internal static DlgHarness Show()
    {
        Grid host = new Grid();
        UiHost window = UiHost.Show(host);
        Grid? previousHost = Dlg.Host;
        Control? previousBlur = Dlg.BlurTarget;
        Dlg.Host = host;
        Dlg.BlurTarget = null;
        return new DlgHarness(window, host, previousHost, previousBlur);
    }

    internal static IEnumerable<Control> Descendants(Control root)
    {
        return root.GetLogicalDescendants().OfType<Control>();
    }

    internal static Button ButtonByText(Control root, string text)
    {
        return Descendants(root)
            .OfType<Button>()
            .First(candidate => string.Equals(candidate.Content as string, text, StringComparison.Ordinal));
    }

    internal static List<Button> Buttons(Control root)
    {
        return Descendants(root).OfType<Button>().ToList();
    }

    internal static List<TextBlock> Texts(Control root)
    {
        return Descendants(root).OfType<TextBlock>().ToList();
    }

    internal static void Click(Button button)
    {
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }

    internal static void Key(Control target, Key key)
    {
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
    }

    public void Dispose()
    {
        Dlg.Host = _previousHost;
        Dlg.BlurTarget = _previousBlur;
        _window.Dispose();
        UiHost.Flush();
    }
}

/// <summary>
/// C2a:<see cref="Dlg"/> 的构建结果与回调形状。全部经 <c>AvaloniaSession.Run</c>(D-79),
/// 事件用 <c>RaiseEvent</c> 程序化触发(**不模拟输入设备**),宿主与全局静态在 finally 里还原。
///
/// 期望值来源:Windows 版 <c>src\TipCL\Services\Dlg.cs</c>(按钮文案、footer 结构、返回值语义、
/// Escape/点遮罩取消这几条分支)与 C1b 的样式键;文字断言一律与 <c>I18n.L(同一个键)</c> 对拍,
/// 只钉"哪条分支/哪个按钮",不钉词表内容。
/// </summary>
public class C2aDlgTests
{
    [Fact]
    public void Ask_primary_button_resolves_true_and_closes_the_layer()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<bool> task = Dlg.Ask("Title", new TextBlock { Text = "body" });

            Assert.True(Dlg.AnyOpen);
            Assert.Single(env.Host.Children);
            Assert.True(env.Host.IsVisible);
            Assert.Single(DlgHarness.Texts(env.Host), t => t.Text == "Title");

            DlgHarness.Click(DlgHarness.ButtonByText(env.Host, I18n.L("\u786e\u5b9a")));
            Assert.True(task.IsCompleted);
            Assert.True(task.Result);
            Assert.False(Dlg.AnyOpen);
            Assert.Empty(env.Host.Children);
            // Motion 在测试进程里是关的(D-83):关掉动画时 PopOut 会**同步**落终态,所以这里马上就是收起状态。
            Assert.False(env.Host.IsVisible);
        });
    }

    [Fact]
    public void Ask_cancel_button_resolves_false()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<bool> task = Dlg.Ask("Title", new TextBlock { Text = "body" });
            DlgHarness.Click(DlgHarness.ButtonByText(env.Host, I18n.L("\u53d6\u6d88")));
            Assert.True(task.IsCompleted);
            Assert.False(task.Result);
            Assert.False(Dlg.AnyOpen);
        });
    }

    [Fact]
    public void Ask_uses_the_supplied_button_labels_and_the_danger_style()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            _ = Dlg.Ask("T", new TextBlock(), "\u5220\u9664", "\u7b97\u4e86", danger: true);
            Button primary = DlgHarness.ButtonByText(env.Host, "\u5220\u9664");
            Button cancel = DlgHarness.ButtonByText(env.Host, "\u7b97\u4e86");
            Assert.Contains(StyleKeysForTest("Btn.Danger"), primary.Classes);
            Assert.DoesNotContain(StyleKeysForTest("Btn.Primary"), primary.Classes);
            Assert.Contains(StyleKeysForTest("Btn.Normal"), cancel.Classes);
        });
    }

    [Fact]
    public void Ask_defaults_to_the_primary_style()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            _ = Dlg.Ask("T", new TextBlock());
            Button primary = DlgHarness.ButtonByText(env.Host, I18n.L("\u786e\u5b9a"));
            Assert.Contains(StyleKeysForTest("Btn.Primary"), primary.Classes);
        });
    }

    [Fact]
    public void Ask_escape_dismisses_as_cancel()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<bool> task = Dlg.Ask("T", new TextBlock());
            DlgHarness.Key(env.Host.Children[0], Key.Escape);
            Assert.True(task.IsCompleted);
            Assert.False(task.Result);
            Assert.False(Dlg.AnyOpen);
        });
    }

    [Fact]
    public void Confirm_wraps_the_body_text_and_defaults_its_labels()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<bool> task = Dlg.Confirm("Title", "hello");
            Assert.Single(DlgHarness.Texts(env.Host), t => t.Text == "hello");
            Assert.NotNull(DlgHarness.ButtonByText(env.Host, I18n.L("\u786e\u5b9a")));
            Assert.NotNull(DlgHarness.ButtonByText(env.Host, I18n.L("\u53d6\u6d88")));
            DlgHarness.Click(DlgHarness.ButtonByText(env.Host, I18n.L("\u786e\u5b9a")));
            Assert.True(task.Result);
        });
    }

    [Fact]
    public void Alert_shows_a_read_only_text_box_and_closes_on_the_only_button()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task task = Dlg.Alert("Title", "the body");
            TextBox box = DlgHarness.Descendants(env.Host).OfType<TextBox>().Single();
            Assert.Equal("the body", box.Text);
            Assert.True(box.IsReadOnly);
            Assert.Equal(380.0, box.MaxHeight);
            Assert.Single(DlgHarness.Buttons(env.Host));

            DlgHarness.Click(DlgHarness.ButtonByText(env.Host, I18n.L("\u77e5\u9053\u4e86")));
            Assert.True(task.IsCompleted);
            Assert.False(Dlg.AnyOpen);
        });
    }

    [Fact]
    public void Prompt_returns_the_trimmed_input()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<string?> task = Dlg.Prompt("Title", "label", "  initial  ", "hint");
            TextBox input = DlgHarness.Descendants(env.Host).OfType<TextBox>().Single();
            Assert.Equal("  initial  ", input.Text);
            input.Text = "  typed  ";
            DlgHarness.Click(DlgHarness.ButtonByText(env.Host, I18n.L("\u786e\u5b9a")));
            Assert.True(task.IsCompleted);
            Assert.Equal("typed", task.Result);
            Assert.False(Dlg.AnyOpen);
        });
    }

    [Fact]
    public void Prompt_enter_key_accepts_and_cancel_returns_null()
    {
        AvaloniaSession.Run(() =>
        {
            using (DlgHarness env = DlgHarness.Show())
            {
                Task<string?> task = Dlg.Prompt("Title", "label", "abc");
                TextBox input = DlgHarness.Descendants(env.Host).OfType<TextBox>().Single();
                DlgHarness.Key(input, Key.Return);
                Assert.True(task.IsCompleted);
                Assert.Equal("abc", task.Result);
            }

            using (DlgHarness env2 = DlgHarness.Show())
            {
                Task<string?> task = Dlg.Prompt("Title", "label", "abc");
                DlgHarness.Click(DlgHarness.ButtonByText(env2.Host, I18n.L("\u53d6\u6d88")));
                Assert.True(task.IsCompleted);
                Assert.Null(task.Result);
            }
        });
    }

    [Fact]
    public void Prompt_escape_returns_null()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<string?> task = Dlg.Prompt("Title", "label");
            DlgHarness.Key(env.Host.Children[0], Key.Escape);
            Assert.True(task.IsCompleted);
            Assert.Null(task.Result);
        });
    }

    [Fact]
    public void Choose_builds_one_button_per_option_and_returns_its_index()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<int> task = Dlg.Choose("Title", new TextBlock(), new[] { "one", "two", "three" });
            Assert.Equal(3, DlgHarness.Buttons(env.Host).Count);
            // Win 源:最后一个选项是 Primary,其余 Normal。
            Assert.Contains(StyleKeysForTest("Btn.Primary"), DlgHarness.ButtonByText(env.Host, "three").Classes);
            Assert.Contains(StyleKeysForTest("Btn.Normal"), DlgHarness.ButtonByText(env.Host, "one").Classes);

            DlgHarness.Click(DlgHarness.ButtonByText(env.Host, "two"));
            Assert.True(task.IsCompleted);
            Assert.Equal(1, task.Result);
            Assert.False(Dlg.AnyOpen);
        });
    }

    [Fact]
    public void Choose_dismissed_by_escape_returns_minus_one()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<int> task = Dlg.Choose("Title", new TextBlock(), new[] { "one" });
            DlgHarness.Key(env.Host.Children[0], Key.Escape);
            Assert.True(task.IsCompleted);
            Assert.Equal(-1, task.Result);
        });
    }

    [Fact]
    public void Choose_with_dismissable_false_ignores_escape()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<int> task = Dlg.Choose("Title", new TextBlock(), new[] { "one" }, 560.0, dismissable: false);
            DlgHarness.Key(env.Host.Children[0], Key.Escape);
            Assert.False(task.IsCompleted);
            Assert.True(Dlg.AnyOpen);
        });
    }

    [Fact]
    public void Form_runs_the_collector_on_save_and_returns_its_value()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<string?> task = Dlg.Form("Title", new TextBlock(), () => "collected", "\u4fdd\u5b58");
            DlgHarness.Click(DlgHarness.ButtonByText(env.Host, "\u4fdd\u5b58"));
            Assert.True(task.IsCompleted);
            Assert.Equal("collected", task.Result);
            Assert.False(Dlg.AnyOpen);
        });
    }

    [Fact]
    public void Form_keeps_the_layer_open_when_the_collector_throws()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Task<string?> task = Dlg.Form<string>("Title", new TextBlock(), () => throw new InvalidOperationException("bad input"));
            DlgHarness.Click(DlgHarness.ButtonByText(env.Host, I18n.L("\u4fdd\u5b58")));
            // 收集抛异常时:不设结果、不关层(只 Toast + Shake);Toast 宿主没接线 -> 只写调试输出。
            Assert.False(task.IsCompleted);
            Assert.True(Dlg.AnyOpen);
            DlgHarness.Click(DlgHarness.ButtonByText(env.Host, I18n.L("\u53d6\u6d88")));
            Assert.True(task.IsCompleted);
            Assert.Null(task.Result);
        });
    }

    [Fact]
    public void Form_cancel_returns_null_without_calling_the_collector()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            bool called = false;
            Task<string?> task = Dlg.Form("Title", new TextBlock(), () =>
            {
                called = true;
                return "x";
            });
            DlgHarness.Click(DlgHarness.ButtonByText(env.Host, I18n.L("\u53d6\u6d88")));
            Assert.True(task.IsCompleted);
            Assert.Null(task.Result);
            Assert.False(called);
        });
    }

    [Fact]
    public void Panel_returns_a_layer_with_a_close_button_that_runs_on_close()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            int closed = 0;
            Dlg.Layer layer = Dlg.Panel("Title", new TextBlock { Text = "body" }, 620.0, () => closed++);
            Assert.True(Dlg.AnyOpen);
            Assert.Same(env.Host.Children[0], layer.Root);
            Assert.Equal(620.0, layer.Card.MaxWidth);
            Assert.Equal(420.0, layer.Card.MinWidth);
            // Win 源:onClose 只在关闭时回调(点遮罩/Escape/关闭按钮),Push 时不动它。
            Assert.Equal(0, closed);

            Button close = DlgHarness.Buttons(layer.Card).Single();
            DlgHarness.Click(close);
            Assert.Equal(1, closed);
            Assert.False(Dlg.AnyOpen);
        });
    }

    [Fact]
    public void Panel_body_scrolls_and_is_narrower_than_the_card_min_width_floor()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Dlg.Layer layer = Dlg.Panel("Title", new TextBlock(), 320.0);
            Assert.Equal(320.0, layer.Card.MinWidth);
            ScrollViewer scroll = DlgHarness.Descendants(layer.Card).OfType<ScrollViewer>().FirstOrDefault()!;
            Assert.NotNull(scroll);
            Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
        });
    }

    [Fact]
    public void Busy_returns_a_disposable_that_closes_the_layer()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            IDisposable busy = Dlg.Busy();
            Assert.True(Dlg.AnyOpen);
            ProgressBar bar = DlgHarness.Descendants(env.Host).OfType<ProgressBar>().Single();
            Assert.True(bar.IsIndeterminate);
            Assert.Equal(220.0, bar.Width);

            busy.Dispose();
            Assert.False(Dlg.AnyOpen);
            // 幂等:再 Dispose 一次不抛、也不改变状态。
            busy.Dispose();
            Assert.False(Dlg.AnyOpen);
        });
    }

    [Fact]
    public void Busy_is_not_dismissable_by_escape()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            using IDisposable busy = Dlg.Busy("working");
            DlgHarness.Key(env.Host.Children[0], Key.Escape);
            Assert.True(Dlg.AnyOpen);
        });
    }

    [Fact]
    public void AnyOpen_is_false_when_no_layer_is_open()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            Assert.False(Dlg.AnyOpen);
        });
    }

    [Fact]
    public void Push_throws_a_clear_error_when_the_host_is_not_wired()
    {
        AvaloniaSession.Run(() =>
        {
            Grid? previous = Dlg.Host;
            Dlg.Host = null;
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                {
                    _ = Dlg.Ask("T", new TextBlock());
                });
            }
            finally
            {
                Dlg.Host = previous;
            }
        });
    }

    [Fact]
    public void Smoke_auto_answer_hook_dismisses_the_layer_and_resolves_the_cancel_path()
    {
        AvaloniaSession.Run(() =>
        {
            using DlgHarness env = DlgHarness.Show();
            bool previousSmoke = Dlg.SmokeActive;
            Action<Control, Action>? previousHook = Dlg.SmokeAutoAnswer;
            try
            {
                Dlg.SmokeActive = true;
                Dlg.SmokeAutoAnswer = (_, close) =>
                {
                    // Windows 版 Smoke.AutoAnswerDialog 会自己把对话点掉;这里模拟它的回调。
                    close();
                };
                Task<bool> task = Dlg.Ask("T", new TextBlock());
                Assert.False(Dlg.AnyOpen);
                Assert.True(task.IsCompleted);
                Assert.False(task.Result);
            }
            finally
            {
                Dlg.SmokeActive = previousSmoke;
                Dlg.SmokeAutoAnswer = previousHook;
            }
        });
    }

    [Fact]
    public void File_pickers_degrade_to_null_without_a_desktop_window()
    {
        AvaloniaSession.Run(() =>
        {
            // 测试进程里没有桌面生命周期 -> Provider() 为 null -> 返回 null,不抛异常、不起进程、不弹窗。
            Assert.Null(Application.Current!.ApplicationLifetime);
            Assert.Null(Dlg.PickFile("PNG (*.png)|*.png"));
            Assert.Null(Dlg.PickFiles("PNG (*.png)|*.png"));
            Assert.Null(Dlg.SaveFile("TXT (*.txt)|*.txt", "a.txt"));
            Assert.Null(Dlg.PickFolder());
        });
    }

    [Fact]
    public void File_pickers_call_the_smoke_cancel_hook_when_smoke_is_active()
    {
        AvaloniaSession.Run(() =>
        {
            bool previousSmoke = Dlg.SmokeActive;
            Action<string>? previousHook = Dlg.SmokeAutoCancelSystemDialog;
            List<string> titles = new List<string>();
            try
            {
                Dlg.SmokeActive = true;
                Dlg.SmokeAutoCancelSystemDialog = titles.Add;
                Assert.Null(Dlg.PickFolder("pick me"));
                Assert.Null(Dlg.SaveFile("TXT (*.txt)|*.txt", "a.txt", "save me"));
                Assert.Equal(new[] { "pick me", "save me" }, titles.ToArray());
            }
            finally
            {
                Dlg.SmokeActive = previousSmoke;
                Dlg.SmokeAutoCancelSystemDialog = previousHook;
            }
        });
    }

    [Fact]
    public void Parse_filter_turns_the_wpf_syntax_into_file_picker_types()
    {
        // Win 源:页面传的都是 "标签|模式" (Windows 的 Filter 语法),这里换成 Avalonia 的 FileTypeFilter。
        FilePickerFileType[] single = Dlg.ParseFilter("PNG (*.png)|*.png");
        Assert.Single(single);
        Assert.Equal("PNG (*.png)", single[0].Name);
        Assert.Equal(new[] { "*.png" }, single[0].Patterns!.ToArray());
    }

    [Fact]
    public void Parse_filter_handles_multiple_patterns_and_groups()
    {
        FilePickerFileType[] multi = Dlg.ParseFilter("Images|*.png;*.jpg");
        Assert.Single(multi);
        Assert.Equal(new[] { "*.png", "*.jpg" }, multi[0].Patterns!.ToArray());

        FilePickerFileType[] groups = Dlg.ParseFilter("Logs (*.log;*.txt)|*.log;*.txt|All|*.*");
        Assert.Equal(2, groups.Length);
        Assert.Equal("Logs (*.log;*.txt)", groups[0].Name);
        Assert.Equal(new[] { "*.log", "*.txt" }, groups[0].Patterns!.ToArray());
        // *.* 在 Avalonia 侧写成 *
        Assert.Equal(new[] { "*" }, groups[1].Patterns!.ToArray());
    }

    [Fact]
    public void Parse_filter_treats_a_lone_pattern_as_one_group()
    {
        FilePickerFileType[] types = Dlg.ParseFilter("*.jar");
        Assert.Single(types);
        Assert.Equal(I18n.L("\u6587\u4ef6"), types[0].Name);
        Assert.Equal(new[] { "*.jar" }, types[0].Patterns!.ToArray());
    }

    [Fact]
    public void Parse_filter_returns_nothing_for_an_empty_filter()
    {
        Assert.Empty(Dlg.ParseFilter(""));
        Assert.Empty(Dlg.ParseFilter(null));
        Assert.Empty(Dlg.ParseFilter("Label|"));
    }

    private static string StyleKeysForTest(string key)
    {
        // C1b 的键 -> 类名映射(用同一张表,免得在测试里抄一遍类名词)。
        return TipCL.Themes.StyleKeys.Map(key);
    }
}

/// <summary>
/// C2a:<see cref="Motion"/> 的门面行为。测试进程里没有桌面生命周期(D-83),
/// 所以这里钉住的正是"关掉动画时的样子"= **立刻落终态**,以及"取不到时钟就不起动画"这条纪律。
/// 真生命周期下的播放效果本机没法验(记在 PROGRESS 的未验证项里)。
/// </summary>
public class C2aMotionTests
{
    [Fact]
    public void Motion_is_disabled_when_nothing_pumps_the_animation_clock()
    {
        AvaloniaSession.Run(() =>
        {
            Assert.Null(Application.Current!.ApplicationLifetime);
            Assert.False(Motion.HasClock);
            Assert.True(Motion.UserEnabled);
            Assert.False(Motion.Enabled);
            Assert.Equal(1.0, Motion.Scale);
        });
    }

    [Fact]
    public void User_switch_round_trips_and_raises_the_event_only_on_change()
    {
        AvaloniaSession.Run(() =>
        {
            bool original = Motion.UserEnabled;
            int fired = 0;
            Action onChanged = () => fired++;
            Motion.EnabledChanged += onChanged;
            try
            {
                Motion.Enabled = false;
                Assert.False(Motion.UserEnabled);
                Assert.Equal(1, fired);
                Motion.Enabled = false;
                Assert.Equal(1, fired);
                Motion.Enabled = true;
                Assert.True(Motion.UserEnabled);
                Assert.Equal(2, fired);
            }
            finally
            {
                Motion.EnabledChanged -= onChanged;
                Motion.Enabled = original;
            }
        });
    }

    [Fact]
    public void FadeIn_lands_on_the_final_state_without_a_clock()
    {
        AvaloniaSession.Run(() =>
        {
            Border el = new Border { Opacity = 0.0 };
            Motion.FadeIn(el);
            Assert.Equal(1.0, el.Opacity);
            // 关掉动画时连 RenderTransform 都不该建(免得留下半截平移)。
            Assert.Null(el.RenderTransform);
        });
    }

    [Fact]
    public void FadeIn_with_a_delay_still_lands_immediately_when_disabled()
    {
        AvaloniaSession.Run(() =>
        {
            Border el = new Border { Opacity = 0.0 };
            Motion.FadeIn(el, 220.0, 10.0, 500.0);
            Assert.Equal(1.0, el.Opacity);
            Assert.Null(el.RenderTransform);
        });
    }

    [Fact]
    public void Fade_FadeOut_and_PopIn_PopOut_apply_the_final_state_and_run_callbacks()
    {
        AvaloniaSession.Run(() =>
        {
            Border el = new Border { Opacity = 1.0 };
            Motion.Fade(el, 0.25, 100.0);
            Assert.Equal(0.25, el.Opacity);

            bool faded = false;
            Motion.FadeOut(el, 100.0, () => faded = true);
            Assert.True(faded);
            Assert.Equal(0.0, el.Opacity);

            Motion.PopIn(el);
            Assert.Equal(1.0, el.Opacity);

            bool popped = false;
            Motion.PopOut(el, () => popped = true);
            Assert.True(popped);
        });
    }

    [Fact]
    public void SlideIn_and_SlideOut_land_immediately_when_disabled()
    {
        AvaloniaSession.Run(() =>
        {
            Border el = new Border { Opacity = 0.0 };
            Motion.SlideIn(el);
            Assert.Equal(1.0, el.Opacity);

            bool slid = false;
            Motion.SlideOut(el, () => slid = true);
            Assert.True(slid);
        });
    }

    [Fact]
    public void Progress_clamps_and_writes_the_value_directly()
    {
        AvaloniaSession.Run(() =>
        {
            ProgressBar bar = new ProgressBar();
            Motion.Progress(bar, 150.0);
            Assert.Equal(100.0, bar.Value);
            Motion.Progress(bar, -5.0);
            Assert.Equal(0.0, bar.Value);
            Motion.Progress(bar, 30.0);
            Assert.Equal(30.0, bar.Value);
        });
    }

    [Fact]
    public void CountUp_writes_the_target_text_and_calls_done()
    {
        AvaloniaSession.Run(() =>
        {
            TextBlock text = new TextBlock();
            bool done = false;
            Motion.CountUp(text, 0.0, 42.0, "0", 620.0, () => done = true);
            Assert.Equal("42", text.Text);
            Assert.True(done);
        });
    }

    [Fact]
    public void Animate_width_and_height_apply_the_target_size()
    {
        AvaloniaSession.Run(() =>
        {
            Border el = new Border();
            Motion.AnimateWidth(el, 120.0);
            Assert.Equal(120.0, el.Width);
            Motion.AnimateHeight(el, 64.0);
            Assert.Equal(64.0, el.Height);

            bool done = false;
            Motion.AnimateWidth(el, 200.0, 220.0, () => done = true);
            Assert.Equal(200.0, el.Width);
            Assert.True(done);
        });
    }

    [Fact]
    public void Shadow_uses_box_shadow_on_borders_and_an_effect_elsewhere()
    {
        AvaloniaSession.Run(() =>
        {
            Border border = new Border();
            Motion.Shadow(border, 12.0, 0.5, 3.0);
            Assert.Equal(1, border.BoxShadow.Count);
            Assert.Equal(3.0, border.BoxShadow[0].OffsetY);
            Assert.Equal(12.0, border.BoxShadow[0].Blur);
            Assert.Equal((byte)128, border.BoxShadow[0].Color.A);

            Button button = new Button();
            Motion.Shadow(button);
            DropShadowEffect effect = Assert.IsType<DropShadowEffect>(button.Effect);
            Assert.Equal(18.0, effect.BlurRadius);
            Assert.Equal(2.0, effect.OffsetY);
            Assert.Equal(0.08, effect.Opacity);
        });
    }

    [Fact]
    public void Pulse_and_Shake_only_touch_the_scale_and_translate_transforms()
    {
        AvaloniaSession.Run(() =>
        {
            Border el = new Border();
            Motion.Pulse(el, 1.06);
            TransformGroup group = Assert.IsType<TransformGroup>(el.RenderTransform);
            ScaleTransform scale = Assert.IsType<ScaleTransform>(group.Children.OfType<ScaleTransform>().Single());
            Assert.Equal(1.0, scale.ScaleX);
            Assert.Equal(0.5, el.RenderTransformOrigin.Point.X);
            Assert.Equal(0.5, el.RenderTransformOrigin.Point.Y);

            Motion.Shake(el);
            TranslateTransform translate = group.Children.OfType<TranslateTransform>().Single();
            Assert.Equal(0.0, translate.X);
            Assert.Equal(0.0, translate.Y);
        });
    }

    [Fact]
    public void Stagger_StaggerItems_HoverLift_Shine_and_Spin_do_nothing_when_disabled()
    {
        AvaloniaSession.Run(() =>
        {
            SPanel panel = Ui.V(4.0, new TextBlock { Text = "a" }, new TextBlock { Text = "b" });
            Motion.Stagger(panel);
            Assert.All(panel.Children, child => Assert.Equal(1.0, child.Opacity));

            List<Control> items = new List<Control> { new TextBlock(), new TextBlock() };
            Motion.StaggerItems(items);
            Assert.All(items, child => Assert.Equal(1.0, child.Opacity));

            Border hover = new Border();
            Motion.HoverLift(hover, 1.012, 2.0, 22.0);
            Assert.True(hover.BoxShadow.Equals(default(BoxShadows)));

            Border shine = new Border();
            TranslateTransform slide = new TranslateTransform(-2.0, 0.0);
            Motion.Shine(shine, slide, 800.0);
            Assert.Same(slide, shine.RenderTransform);
            Assert.Equal(0.0, shine.Opacity);

            RotateTransform rot = new RotateTransform();
            Border spinner = new Border();
            Motion.Spin(spinner, rot, 1.0);
            Assert.Equal(0.0, rot.Angle);
        });
    }

    [Fact]
    public void Loop_never_starts_or_stops_without_a_clock()
    {
        AvaloniaSession.Run(() =>
        {
            Border owner = new Border();
            int started = 0;
            int stopped = 0;
            Motion.Loop(owner, () => started++, () => stopped++);
            Assert.Equal(0, started);
            Assert.Equal(0, stopped);

            // 挂进真窗口也一样:没有桌面生命周期就没有时钟(D-83)。
            using UiHost window = UiHost.Show(owner);
            Assert.Equal(0, started);
            Assert.Equal(0, stopped);
        });
    }

    [Fact]
    public void PageSwap_replaces_the_content_and_resets_the_state()
    {
        AvaloniaSession.Run(() =>
        {
            ContentControl host = new ContentControl();
            Border first = new Border { Opacity = 0.4 };
            Border second = new Border { Opacity = 0.4 };
            host.Content = first;
            Motion.PageSwap(host, second);
            Assert.Same(second, host.Content);
            Assert.Equal(1.0, second.Opacity);
            // 关掉动画时旧内容也要被复位(Opacity 回 1、平移归零),不留半截状态。
            Assert.Equal(1.0, first.Opacity);
        });
    }
}
