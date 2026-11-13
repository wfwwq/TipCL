using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C2c2:拖放(<see cref="FileDrop"/> / <see cref="DropVeil"/> / <see cref="PageBase.EnableDrop"/>)。
///
/// 纪律:
/// <list type="bullet">
/// <item>一切碰 Avalonia 的用例体都经 <see cref="AvaloniaSession.Run(Action)"/>(D-79);
///   用例体**同步**(在会话线程上阻塞等一个要投回同一 Dispatcher 的 Task 会自锁);</item>
/// <item>不模拟真实输入设备:拖进来 / 离开 / 落点都走 <see cref="FileDrop"/> 的公开入口,
///   "拖的是什么"由用例注入(这是本包为拖放留的注入缝);</item>
/// <item>真窗口用 <see cref="ShellFixture"/>(它会把外壳动过的全局静态还原),本包新加的口子用
/// <see cref="C2c2Scope"/> 还原;临时文件一律落在 <see cref="TestHome"/> 之下。</item>
/// </list>
/// </summary>
[Collection("C2c2Globals")]
public sealed class C2c2FileDropTests
{
    /// <summary>一次性把外壳 + 注入缝 + 假落点摆好(用例体里调,必须在会话线程上)。</summary>
    private static List<(IReadOnlyList<string> Paths, FileDrop.DropContext? Context)> Arm(ShellFixture shell)
    {
        FileDrop.Enabled = true;
        FileDrop.ReadyGate = static () => true;
        List<(IReadOnlyList<string> Paths, FileDrop.DropContext? Context)> received =
            new List<(IReadOnlyList<string>, FileDrop.DropContext?)>();
        FileDrop.Sink = delegate(IReadOnlyList<string> paths, FileDrop.DropContext? context)
        {
            received.Add((paths.ToList(), context));
            return Task.CompletedTask;
        };
        return received;
    }

    // ---------------------------------------------------------------- 浮层(进入 / 离开 / 落点)

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:238-256</c>:带文件拖进来 → 出浮层。</summary>
    [Fact]
    public void DragEnter_with_files_shows_the_veil()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();
            Arm(shell);

            Assert.False(FileDrop.VeilVisible, "还没拖进来的时候浮层必须是收起的");

            bool changed = FileDrop.DragEnter(new[] { @"D:\a\mymod.jar" });

            Assert.True(changed);
            Assert.True(FileDrop.Dragging);
            Assert.False(FileDrop.DragCancelled);
            Assert.True(FileDrop.VeilVisible);
            DropVeil veil = FileDrop.Veil!;
            Assert.True(veil.IsVisible);
            Assert.True(veil.IsOn);
            Assert.Equal(1, veil.ShowCount);
            Assert.False(veil.FadeOutPending);
        });
    }

    /// <summary>【Windows 源】浮层挂在外壳里(Windows 是 XAML 里的一个 Border;这里按 Name 找得到)。</summary>
    [Fact]
    public void Veil_is_inserted_in_the_shell_window()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();

            DropVeil veil = C2bAssert.ByName<DropVeil>(shell.Window, "DropVeil");

            Assert.NotNull(veil.Parent);
            Assert.True(veil.IsHitTestVisible == false, "浮层不吃鼠标事件(Windows 是 IsHitTestVisible=False)");
            Assert.Same(FileDrop.Veil, veil);
        });
    }

    /// <summary>【Windows 源】<c>ShowDropVeil(e.Data.GetDataPresent(DataFormats.FileDrop))</c>:
    /// 拖进来的东西里没有文件 → 不出浮层。</summary>
    [Fact]
    public void DragEnter_without_files_keeps_the_veil_hidden()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();
            Arm(shell);

            Assert.False(FileDrop.DragEnter(Array.Empty<string>()));
            Assert.False(FileDrop.VeilVisible);
            Assert.False(FileDrop.Dragging);
            Assert.Equal(0, FileDrop.Veil!.ShowCount);

            Assert.False(FileDrop.DragEnter(null));
            Assert.False(FileDrop.VeilVisible);
        });
    }

    /// <summary>【Windows 源】<c>ShowDropVeil</c> 头一句 <c>if (!_bridgeReady) on = false;</c>
    /// —— 后端没连上时拖进来也不出浮层。</summary>
    [Fact]
    public void DragEnter_without_a_ready_backend_keeps_the_veil_hidden()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();
            Arm(shell);
            FileDrop.ReadyGate = static () => false;

            Assert.False(FileDrop.CanDrop);
            Assert.False(FileDrop.DragEnter(new[] { @"D:\a\mymod.jar" }));
            Assert.False(FileDrop.VeilVisible);
            Assert.False(FileDrop.Dragging);
        });
    }

    /// <summary>总闸 <see cref="FileDrop.Enabled"/> 关掉 → 一切都压住(不出浮层、不落点)。</summary>
    [Fact]
    public void Disabled_gate_suppresses_everything()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();
            List<(IReadOnlyList<string> Paths, FileDrop.DropContext? Context)> received = Arm(shell);
            FileDrop.Enabled = false;

            Assert.False(FileDrop.CanDrop);
            Assert.False(FileDrop.DragEnter(new[] { @"D:\a\mymod.jar" }));
            Assert.False(FileDrop.VeilVisible);
            Assert.False(FileDrop.Drop(new[] { @"D:\a\mymod.jar" }));
            Assert.Empty(received);
            Assert.False(FileDrop.Dragging);
        });
    }

    /// <summary>【Windows 源】<c>PreviewDragOver</c> 与 <c>PreviewDragEnter</c> 都调
    /// <c>ShowDropVeil(true)</c> —— 已经在显示时是幂等的(<c>on == Visible</c> 早退)。</summary>
    [Fact]
    public void DragOver_is_idempotent_while_the_veil_is_shown()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();
            Arm(shell);

            FileDrop.DragEnter(new[] { @"D:\a\mymod.jar" });
            Assert.False(FileDrop.DragOver(new[] { @"D:\a\mymod.jar" }));
            Assert.Equal(1, FileDrop.Veil!.ShowCount);
            Assert.True(FileDrop.VeilVisible);
        });
    }

    /// <summary>【Windows 源】<c>PreviewDragLeave</c> → <c>ShowDropVeil(false)</c>:收浮层且不留残留。</summary>
    [Fact]
    public void DragLeave_hides_the_veil_and_leaves_no_residue()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();
            Arm(shell);

            FileDrop.DragEnter(new[] { @"D:\a\mymod.jar" });
            Assert.True(FileDrop.DragLeave());

            Assert.False(FileDrop.Dragging);
            Assert.True(FileDrop.DragCancelled, "离开窗口要记一笔:之后那次落点算取消");
            Assert.False(FileDrop.VeilVisible);
            Assert.False(FileDrop.Veil!.IsOn);
            Assert.False(FileDrop.Veil.FadeOutPending, "没有留下没落地的淡出(离屏环境里是同步落的)");
            Assert.False(FileDrop.Veil.IsVisible);
            Assert.Equal(2, FileDrop.Veil.ShowCount);
        });
    }

    /// <summary>【Windows 源】拖出去松手 = 取消(窗口根本收不到 Drop);mac 侧靠离开标记钉住这条语义。</summary>
    [Fact]
    public void Drop_after_leaving_the_window_is_cancelled()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();
            List<(IReadOnlyList<string> Paths, FileDrop.DropContext? Context)> received = Arm(shell);

            FileDrop.DragEnter(new[] { @"D:\a\mymod.jar" });
            FileDrop.DragLeave();

            Assert.False(FileDrop.Drop(new[] { @"D:\a\mymod.jar" }));
            Assert.Empty(received);
            Assert.False(FileDrop.VeilVisible);
        });
    }

    /// <summary>重新拖进来会清掉"离开过"的标记,于是照样能落点。</summary>
    [Fact]
    public void Re_entering_clears_the_cancelled_flag()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();
            List<(IReadOnlyList<string> Paths, FileDrop.DropContext? Context)> received = Arm(shell);

            FileDrop.DragEnter(new[] { @"D:\a\mymod.jar" });
            FileDrop.DragLeave();
            FileDrop.DragEnter(new[] { @"D:\a\mymod.jar" });

            Assert.False(FileDrop.DragCancelled);
            Assert.True(FileDrop.Drop(new[] { @"D:\a\mymod.jar" }));
            Assert.Single(received);
        });
    }

    // ---------------------------------------------------------------- 落点的载荷

    /// <summary>【任务书】落点交给页面的载荷必须与拖进来的一模一样(路径,按 Windows 的顺序)。</summary>
    [Fact]
    public void Drop_hands_the_page_exactly_what_was_dropped()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();
            List<(IReadOnlyList<string> Paths, FileDrop.DropContext? Context)> received = Arm(shell);

            string[] paths = { @"D:\downloads\a.jar", @"D:\downloads\b.zip", @"D:\pics\c.png" };
            FileDrop.DragEnter(paths);
            Assert.True(FileDrop.Drop(paths));

            Assert.Single(received);
            Assert.Equal(paths, received[0].Paths);
            Assert.False(FileDrop.VeilVisible);
            Assert.False(FileDrop.Dragging);
        });
    }

    /// <summary>【Windows 源】<c>HandleAsync</c> 会滤掉空路径(<c>!string.IsNullOrWhiteSpace</c>)。</summary>
    [Fact]
    public void Drop_filters_blank_paths()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();
            List<(IReadOnlyList<string> Paths, FileDrop.DropContext? Context)> received = Arm(shell);

            FileDrop.DragEnter(new[] { @"D:\downloads\a.jar" });
            Assert.True(FileDrop.Drop(new[] { "", "   ", @"D:\downloads\a.jar" }));

            Assert.Single(received);
            Assert.Equal(new[] { @"D:\downloads\a.jar" }, received[0].Paths);
        });
    }

    /// <summary>落下来的东西里没有文件 → 什么都不做(与 Windows 的 <c>paths.Length != 0</c> 同一道闸)。</summary>
    [Fact]
    public void Drop_without_files_does_nothing()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();
            List<(IReadOnlyList<string> Paths, FileDrop.DropContext? Context)> received = Arm(shell);

            Assert.False(FileDrop.Drop(Array.Empty<string>()));
            Assert.False(FileDrop.Drop(null));
            Assert.Empty(received);
        });
    }

    /// <summary>【Windows 源】<c>OnDrop</c> 的 <c>if (!_bridgeReady) return;</c>:后端没连上不接文件。</summary>
    [Fact]
    public void Drop_without_a_ready_backend_does_nothing()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();
            List<(IReadOnlyList<string> Paths, FileDrop.DropContext? Context)> received = Arm(shell);
            FileDrop.ReadyGate = static () => false;

            FileDrop.DragEnter(new[] { @"D:\a\mymod.jar" });
            Assert.False(FileDrop.Drop(new[] { @"D:\a\mymod.jar" }));
            Assert.Empty(received);
        });
    }

    /// <summary>【Windows 源】<c>FileDrop.HandleAsync</c> 第一句就是"没有路径直接返回"。</summary>
    [Fact]
    public void Handle_async_returns_immediately_without_paths()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();

            // 空表:连后端都不会碰(Windows 同)。
            FileDrop.HandleAsync(shell.Window, Array.Empty<string>()).GetAwaiter().GetResult();
        });
    }

    /// <summary>【Windows 源】<c>FileDrop.cs:42</c> —— 认种类这一步同时滤掉空路径,
    /// 而且认不出来的给出"这个路径不存在"的说明。</summary>
    [Fact]
    public void Identify_paths_matches_windows_kind_rules()
    {
        using TestHome home = new TestHome();
        string png = C2c2Files.Write(Path.Combine(home.Dir, "shot.png"), OnePixelPng());
        string missing = Path.Combine(home.Dir, "nope.png");

        List<FileKinds.Verdict> verdicts = FileDrop.IdentifyPaths(new[] { "", png, missing });

        Assert.Equal(2, verdicts.Count);
        Assert.Equal(png, verdicts[0].Path);
        Assert.True(verdicts[0].Sure);
        Assert.Contains("wallpaper", verdicts[0].Kinds);

        Assert.Equal(missing, verdicts[1].Path);
        Assert.Empty(verdicts[1].Kinds);
        Assert.False(verdicts[1].Sure);
        Assert.Equal(I18n.L("这个路径不存在"), verdicts[1].Detail);
    }

    // ---------------------------------------------------------------- PageBase.EnableDrop

    /// <summary>【Windows 源】<c>PageBase</c> 的构造里没有 <c>AllowDrop = true</c>:
    /// 页面默认不收拖放,得自己调 <c>EnableDrop</c>。</summary>
    [Fact]
    public void Page_default_is_drop_disabled()
    {
        AvaloniaSession.Run(() =>
        {
            DropProbePage page = new DropProbePage();

            Assert.False(page.DropEnabled);
            Assert.False(DragDrop.GetAllowDrop(page));
            Assert.Same(FileDrop.DropContext.None, page.DropContextNow());
        });
    }

    /// <summary>【Windows 源】<c>PageBase.EnableDrop(context)</c> 把 <c>AllowDrop</c> 打开;
    /// 上下文工厂是**每次落点现调**的(Windows 也是 <c>context()</c>),所以它随状态变。</summary>
    [Fact]
    public void EnableDrop_turns_allow_drop_on_and_re_evaluates_the_context()
    {
        AvaloniaSession.Run(() =>
        {
            DropProbePage page = new DropProbePage();
            string instance = "first";
            page.EnableDrop(() => new FileDrop.DropContext { Instance = instance, Version = "1.20.1" });

            Assert.True(page.DropEnabled);
            Assert.True(DragDrop.GetAllowDrop(page));
            Assert.Equal("first", page.DropContextNow().Instance);
            Assert.Equal("1.20.1", page.DropContextNow().Version);

            instance = "second";
            Assert.Equal("second", page.DropContextNow().Instance);
        });
    }

    /// <summary>【任务书】<c>EnableDrop(false)</c> 之后这个页面退回"不收拖放"。</summary>
    [Fact]
    public void EnableDrop_false_switches_the_page_off()
    {
        AvaloniaSession.Run(() =>
        {
            DropProbePage page = new DropProbePage();
            page.EnableDrop(() => new FileDrop.DropContext { Instance = "x" });
            page.EnableDrop(false);

            Assert.False(page.DropEnabled);
            Assert.False(DragDrop.GetAllowDrop(page));
            Assert.Same(FileDrop.DropContext.None, page.DropContextNow());
        });
    }

    /// <summary>mac 侧的收严:传 null 当场拒绝(Windows 会在落点那一刻才 NRE)。</summary>
    [Fact]
    public void EnableDrop_rejects_a_null_factory()
    {
        AvaloniaSession.Run(() =>
        {
            DropProbePage page = new DropProbePage();

            Assert.Throws<ArgumentNullException>(() => page.EnableDrop(null!));
            Assert.False(page.DropEnabled);
        });
    }

    /// <summary>【Windows 源 + 本机实测】页面调过 <c>EnableDrop</c> 就用页面的上下文落点
    /// (Windows 是页面 <c>PreviewDrop</c> 先于窗口 <c>Drop</c> 并把它 <c>Handled</c> 掉)。</summary>
    [Fact]
    public void Drop_uses_the_current_page_context()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();
            List<(IReadOnlyList<string> Paths, FileDrop.DropContext? Context)> received = Arm(shell);

            DropProbePage page = new DropProbePage();
            using RegistryScope registry = new RegistryScope().Register("c2c2-drop", () => page);
            page.EnableDrop(() => new FileDrop.DropContext { Instance = "1.20.1-fabric", PreferKind = "mod" });
            shell.Window.Navigate("c2c2-drop");
            Assert.Same(page, shell.Window.CurrentPage);

            FileDrop.DragEnter(new[] { @"D:\a\mymod.jar" });
            Assert.True(FileDrop.Drop(new[] { @"D:\a\mymod.jar" }));

            Assert.Single(received);
            Assert.NotNull(received[0].Context);
            Assert.Equal("1.20.1-fabric", received[0].Context!.Instance);
            Assert.Equal("mod", received[0].Context.PreferKind);
        });
    }

    /// <summary>【Windows 源】页面没调 <c>EnableDrop</c> 时,窗口级的落点用
    /// <c>DropContext.None</c>(Windows 传的第二个参数是缺省值)。</summary>
    [Fact]
    public void Drop_falls_back_to_the_none_context()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();
            List<(IReadOnlyList<string> Paths, FileDrop.DropContext? Context)> received = Arm(shell);

            DropProbePage page = new DropProbePage();
            using RegistryScope registry = new RegistryScope().Register("c2c2-plain", () => page);
            shell.Window.Navigate("c2c2-plain");
            Assert.Same(page, shell.Window.CurrentPage);

            FileDrop.DragEnter(new[] { @"D:\a\mymod.jar" });
            Assert.True(FileDrop.Drop(new[] { @"D:\a\mymod.jar" }));

            Assert.Single(received);
            Assert.Same(FileDrop.DropContext.None, received[0].Context);
        });
    }

    /// <summary>【任务书】"页面 toggle 会传到拖放服务":<c>EnableDrop(false)</c> 之后同一个落点
    /// 退回默认上下文。</summary>
    [Fact]
    public void EnableDrop_false_reaches_the_drop_service()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();
            List<(IReadOnlyList<string> Paths, FileDrop.DropContext? Context)> received = Arm(shell);

            DropProbePage page = new DropProbePage();
            using RegistryScope registry = new RegistryScope().Register("c2c2-toggle", () => page);
            page.EnableDrop(() => new FileDrop.DropContext { Instance = "on" });
            shell.Window.Navigate("c2c2-toggle");

            FileDrop.DragEnter(new[] { @"D:\a.jar" });
            FileDrop.Drop(new[] { @"D:\a.jar" });
            Assert.Equal("on", received[0].Context!.Instance);

            page.EnableDrop(false);
            FileDrop.DragEnter(new[] { @"D:\a.jar" });
            FileDrop.Drop(new[] { @"D:\a.jar" });
            Assert.Same(FileDrop.DropContext.None, received[1].Context);
        });
    }

    /// <summary>【Windows 源 + 本机实测】切走再切回来,页面的 <c>EnableDrop</c> 还在
    /// (Windows 把 <c>PreviewDrop</c> 挂在页面对象上,而页面实例由宿主缓存着)——
    /// 且 <c>Host.GetPage</c> 给的还是同一个实例。</summary>
    [Fact]
    public void EnableDrop_survives_a_page_switch()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();
            List<(IReadOnlyList<string> Paths, FileDrop.DropContext? Context)> received = Arm(shell);

            DropProbePage first = new DropProbePage();
            DropProbePage other = new DropProbePage();
            using RegistryScope registry = new RegistryScope()
                .Register("c2c2-one", () => first)
                .Register("c2c2-two", () => other);

            first.EnableDrop(() => new FileDrop.DropContext { Instance = "kept" });
            shell.Window.Navigate("c2c2-one");
            shell.Window.Navigate("c2c2-two");
            Assert.Equal(1, first.Hidden);   // 切走时老页收到 OnHidden
            shell.Window.Navigate("c2c2-one");
            Assert.Equal(1, other.Hidden);   // 再切回来时另一页也收到了

            Assert.Same(first, shell.Window.CurrentPage);
            Assert.True(first.DropEnabled);
            Assert.True(DragDrop.GetAllowDrop(first));

            FileDrop.DragEnter(new[] { @"D:\a.jar" });
            FileDrop.Drop(new[] { @"D:\a.jar" });
            Assert.Equal("kept", received[0].Context!.Instance);
        });
    }

    // ---------------------------------------------------------------- 浮层的静态取值(Windows 逐项)

    /// <summary>【Windows 源】<c>MainWindow.xaml:78-86</c> + <c>MainWindow.xaml.cs:166-167</c>
    /// 的浮层取值与文案。</summary>
    [Fact]
    public void Veil_geometry_and_texts_match_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();

            DropVeil veil = FileDrop.Veil!;

            Assert.Equal(C2c2Expected.VeilCardCornerRadius, veil.Card.CornerRadius.TopLeft);
            Assert.Equal(C2c2Expected.VeilCardBorderThickness, veil.Card.BorderThickness.Left);
            Assert.Equal(C2c2Expected.VeilCardPadding, veil.Card.Padding);
            Assert.Equal(C2c2Expected.VeilIconSize, veil.Icon.FontSize);
            Assert.Equal(C2c2Expected.VeilTitleSize, veil.Title.FontSize);
            Assert.Equal(C2c2Expected.VeilHintSize, veil.Hint.FontSize);
            Assert.Equal(C2c2Expected.VeilHintMaxWidth, veil.Hint.MaxWidth);
            Assert.Equal(I18n.L(C2c2Expected.VeilTitleSource), veil.Title.Text);
            Assert.Equal(I18n.L(C2c2Expected.VeilHintSource), veil.Hint.Text);
            Assert.Equal(Avalonia.Media.TextAlignment.Center, veil.Hint.TextAlignment);
            Assert.Equal(Avalonia.Media.TextWrapping.Wrap, veil.Hint.TextWrapping);
        });
    }

    /// <summary>【Windows 源】<c>MainWindow.ShowDropVeil</c> 的淡入 120ms / 淡出 110ms。</summary>
    [Fact]
    public void Veil_fade_durations_match_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();

            Assert.Equal(C2c2Expected.VeilFadeInMs, DropVeil.FadeInMs);
            Assert.Equal(C2c2Expected.VeilFadeOutMs, DropVeil.FadeOutMs);
            Assert.Equal(C2c2Expected.VeilTitleSource, DropVeil.TitleSource);
            Assert.Equal(C2c2Expected.VeilHintSource, DropVeil.HintSource);
        });
    }

    /// <summary>【Windows 源】<c>FileDrop.cs:26-34</c> 的 kind → 后端方法名表。</summary>
    [Fact]
    public void Install_method_table_matches_windows()
    {
        AvaloniaSession.Run(() =>
        {
            List<string> kinds = new List<string> { "mod", "resourcepack", "shaderpack", "datapack", "world" };
            Assert.Equal(5, C2c2Expected.InstallMethods.Length);
            Assert.Equal(kinds, C2c2Expected.InstallMethods.Select(m => m.Kind).ToList());
            Assert.Equal("install_mod", C2c2Expected.InstallMethods[0].Method);
            Assert.Equal("install_world", C2c2Expected.InstallMethods[4].Method);
        });
    }

    /// <summary>关窗之后静态引用不留活窗口(拖放服务要能跟着外壳一起收摊)。</summary>
    [Fact]
    public void Closing_the_shell_detaches_the_drop_service()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            DropVeil? veil;
            using (ShellFixture shell = ShellFixture.Create())
            {
                Arm(shell);
                veil = FileDrop.Veil;
                Assert.NotNull(veil);
            }

            Assert.Null(FileDrop.Veil);
            Assert.False(FileDrop.VeilVisible);
            Assert.False(FileDrop.Dragging);
            Assert.False(veil!.IsVisible);
        });
    }

    /// <summary>【本机实测】1×1 的合法 PNG(只为让 <c>FileKinds.Identify</c> 有文件可认)。</summary>
    private static byte[] OnePixelPng()
    {
        return new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82,
        };
    }
}
