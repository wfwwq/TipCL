// AI3-visual:--visual-tour <剧本.json> <输出目录> —— 可点击的无窗口视觉巡检。
// i18n:ignore-file —— 开发者巡检工具,输出给 tour-result.json 的诊断文字,不是用户界面文案。
//
// 与 --smoke 同一条路:Avalonia Headless(UseHeadlessDrawing=false)+ Skia,SetupWithoutStarting,
// **不建任何系统窗口**。在此之上补 --smoke 做不到的事:按剧本点击(按控件 Name 或可见文字找元素、
// 算中心点再 MouseDown/MouseUp,不写死坐标)、右键、按键(含 Ctrl+Z / Esc)、滚动、改窗口尺寸、切主题、截图。
//
// 每一步之后:推渲染计时器 + settle(同 SmokeRunner 的 RenderPass / Settle),截图前再丢一帧(R8 残帧)。
// 动画:Motion.Enabled = false(入场动画一步到位);LayoutTween 的时钟换成「每次渲染 pass 前进 10 秒」的虚拟时钟,
// 任何补间都在下一拍落到终点,截图与墙钟无关。
// 打开链接 / 文件夹一律走 ShellOpen / Crash / Auth 的钩子,只记录不真打开;出网走 TourNet(全部挡下,
// Modrinth 搜索给假结果);点「启动游戏」这类会拉起 Minecraft 的按钮直接拒绝(记 FAIL)。
// 找不到目标控件 = 这一步 FAIL,继续往下跑;每步之后都重写一次 tour-result.json(中途崩了也留得下结果)。
//
// 退出码:0 = 全部步骤 OK;8 = 有 FAIL 步骤(结果照常写出);9 = 剧本 / 参数错误或环境不安全
// (沿用 SMOKE_NET=2 / SMOKE_INSTALL=3 / 4 兜底 / dialog-close=5 / popup-theme=6 / widgets=7 的约定)。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.App.Common;
using TipCL.Core.Launcher;

namespace TipCL.App.Session;

internal static class VisualTour
{
    public const int ExitOk = 0;
    public const int ExitFailures = 8;
    public const int ExitBadInput = 9;

    /// <summary>会拉起 Minecraft 的按钮文字:巡检里一律拒点。</summary>
    internal static readonly string[] LaunchWords = { "启动游戏", "开始游戏", "重新启动", "测试启动", "启动", "仍然启动", "强制启动" };

    /// <summary>入口:<c>--visual-tour &lt;剧本.json&gt; &lt;输出目录&gt;</c>。</summary>
    public static int Run(AppBuilder builder, string[] args)
    {
        int at = Array.IndexOf(args, "--visual-tour");
        string scriptPath = at >= 0 && at + 1 < args.Length ? args[at + 1] : "";
        string outDir = at >= 0 && at + 2 < args.Length ? args[at + 2] : "visual-tour-out";
        try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }

        if (scriptPath.Length == 0 || !File.Exists(scriptPath))
        {
            Console.Error.WriteLine("--visual-tour: script not found: " + scriptPath);
            return ExitBadInput;
        }
        // 只在显式隔离的数据目录里跑:巡检会点按钮、写配置,绝不能落到真实用户目录
        string? home = Environment.GetEnvironmentVariable("TIPCL_HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            Console.Error.WriteLine("--visual-tour: refusing to run without an explicit TIPCL_HOME (it clicks buttons and writes config)");
            return ExitBadInput;
        }

        TourScript script;
        try
        {
            script = TourScriptParser.Parse(File.ReadAllText(scriptPath, Encoding.UTF8));
        }
        catch (FormatException ex)
        {
            Console.Error.WriteLine("--visual-tour: " + ex.Message);
            return ExitBadInput;
        }

        outDir = Path.GetFullPath(outDir);
        Directory.CreateDirectory(outDir);
        var runner = new TourRunner(script, scriptPath, outDir);

        if (script.Fixture)
        {
            try
            {
                VisualFixture.Summary s = VisualFixture.Build(Utils.ROOT, DateTimeOffset.Now);
                runner.FixtureInfo = $"instances={s.Instances} versions={s.Versions} mods={s.Mods} saves={s.Saves} servers={s.Servers} screenshots={s.Screenshots} sessions={s.Sessions}";
                Config.Reset();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("--visual-tour: fixture failed: " + ex.Message);
                return ExitBadInput;
            }
        }

        InstallHooks(runner);

        builder
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .AfterSetup(_ => runner.Execute())
            .SetupWithoutStarting();

        return runner.ExitCode;
    }

    /// <summary>不弹窗、不联网、不起游戏的那几道钩子。</summary>
    private static void InstallHooks(TourRunner runner)
    {
        TourNet.Install();
        ShellOpen.Hook = target => runner.ShellOpens.Add(target);
        Crash.ShellOpenHook = target => runner.ShellOpens.Add(target);
        Auth.OpenBrowserHook = target => runner.ShellOpens.Add(target);
        // 文件选择框:一律当成「用户点了取消」(无头平台本来就没有系统文件框;这里只是让这件事确定下来)
        Dlg.PickFilesOverride = (title, _) =>
        {
            runner.ShellOpens.Add("file-picker(cancelled): " + title);
            return Array.Empty<string>();
        };
        Motion.Enabled = false;
        TourClock.Install();
    }
}

/// <summary>LayoutTween 的虚拟时钟:每次渲染 pass 前进 10 秒 ⇒ 任何补间都在下一拍落到终点,与墙钟无关。</summary>
internal static class TourClock
{
    private static long _ticks;

    public static void Install()
    {
        _ticks = Stopwatch.GetTimestamp();
        TipCL.App.Pages.Layout.LayoutTween.Clock = () => System.Threading.Interlocked.Read(ref _ticks);
    }

    public static void Advance() => System.Threading.Interlocked.Add(ref _ticks, Stopwatch.Frequency * 10);
}

/// <summary>一步的执行结果。</summary>
internal sealed class TourStepResult
{
    public int Index { get; set; }

    public string Op { get; set; } = "";

    public string Status { get; set; } = "OK";

    public string Message { get; set; } = "";

    public string Note { get; set; } = "";

    public string Shot { get; set; } = "";

    public long Ms { get; set; }

    public string Step { get; set; } = "";
}

internal sealed class TourRunner
{
    private readonly TourScript _script;
    private readonly string _scriptPath;
    private readonly string _outDir;
    private readonly List<TourStepResult> _results = new();
    private readonly List<Popup> _openPopups = new();
    private readonly List<string> _unhandled = new();
    private readonly HashSet<string> _launchTasks = new();
    private Shell.MainWindow? _win;
    private DateTime _started;

    public TourRunner(TourScript script, string scriptPath, string outDir)
    {
        _script = script;
        _scriptPath = scriptPath;
        _outDir = outDir;
    }

    public int ExitCode { get; private set; } = VisualTour.ExitOk;

    public string FixtureInfo { get; set; } = "";

    /// <summary>
    /// 是否补上生产启动的应用级接线(语言 / 部件工厂 / 布局 Toast / v1 投影)。--visual-tour 进程里为 true;
    /// 单测里为 false(那些是进程级静态,测试进程里由各自的用例管)。
    /// </summary>
    public bool WireApp { get; set; } = true;

    /// <summary>跑完每一步的结果(单测断言用)。</summary>
    public IReadOnlyList<TourStepResult> Results => _results;

    public List<string> ShellOpens { get; } = new();

    private Shell.MainWindow Win => _win ?? throw new InvalidOperationException("main window not created");

    // ———————— 主流程 ————————

    public void Execute()
    {
        _started = DateTime.Now;
        Window? prevOwner = Dlg.Owner;
        IDisposable? popupWatch = null;
        try
        {
            ExecuteCore(ref popupWatch);
        }
        finally
        {
            popupWatch?.Dispose();
            Dlg.Owner = prevOwner;
        }
    }

    private void ExecuteCore(ref IDisposable? popupWatch)
    {
        DispatcherUnhandledExceptionEventHandler onUnhandled = (_, e) =>
        {
            _unhandled.Add(e.Exception.GetType().Name + ": " + e.Exception.Message);
            e.Handled = true;
        };
        try
        {
            Dispatcher.UIThread.UnhandledException += onUnhandled;
            if (WireApp)
            {
                // 生产启动在 App.OnFrameworkInitializationCompleted 里做的接线(SetupWithoutStarting 不会走进去):
                // 语言、部件工厂、布局 JSON 导入导出 / Toast、v1 投影 —— 与生产同序补上
                I18nCore.InitLanguage();
                TipCL.App.Pages.Layout.WidgetRegistry.EnsureRegistered();
                TipCL.App.Pages.Layout.LayoutFx.Wire();
                TipCL.Core.Launcher.Layout.LayoutStoreV2.V1Bridge = new TipCL.App.Pages.Layout.PagesLayoutV1Bridge();
            }
            popupWatch = Popup.IsOpenProperty.Changed.AddClassHandler<Popup, bool>((p, e) =>
            {
                _openPopups.Remove(p);
                if (e.NewValue.GetValueOrDefault()) _openPopups.Add(p);
            });

            bool dark = _script.Theme == "dark";
            Config.CONFIG.Set("ui_dark", dark);
            _win = new Shell.MainWindow
            {
                Width = _script.Width,
                Height = _script.Height,
            };
            Dlg.Owner = _win;
            _win.Show();
            RenderPass();
            Settle(600);
        }
        catch (Exception ex)
        {
            _results.Add(new TourStepResult { Index = -1, Op = "setup", Status = "FAIL", Message = ex.ToString() });
            ExitCode = VisualTour.ExitFailures;
            Dispatcher.UIThread.UnhandledException -= onUnhandled;
            WriteResult();
            return;
        }

        foreach (string problem in _script.Problems)
            Console.WriteLine("TOUR PARSE: " + problem);

        foreach (TourStep step in _script.Steps)
        {
            var r = new TourStepResult { Index = step.Index, Op = step.Op, Note = step.Note, Step = step.Raw };
            var sw = Stopwatch.StartNew();
            int unhandledBefore = _unhandled.Count;
            try
            {
                r.Message = Exec(step, r);
            }
            catch (TourFail f)
            {
                r.Status = "FAIL";
                r.Message = f.Message;
            }
            catch (Exception ex)
            {
                r.Status = "FAIL";
                r.Message = "exception: " + ex.GetType().Name + ": " + ex.Message;
            }
            if (step.Op != "wait" && step.Op != "shot" && step.Op != "dump" && step.Op != "expect")
            {
                Settle(step.Ms >= 0 ? step.Ms : _script.SettleMs);
            }
            if (_unhandled.Count > unhandledBefore)
            {
                r.Status = "FAIL";
                r.Message = (r.Message.Length > 0 ? r.Message + " | " : "") + "unhandled UI exception: " + string.Join(" ; ", _unhandled.Skip(unhandledBefore));
            }
            // 硬护栏:任何一步之后出现了「启动游戏」任务 = 这一步触发了启动(假数据版本没有依赖库,Core 预检会拦下,
            // 但巡检本身绝不该走到这一步)—— 记 FAIL,写明是哪一步
            string launchTitle = I18nCore.Tr("启动游戏");
            var launches = TaskStore.Rows.Where(t => t.Title.StartsWith(launchTitle, StringComparison.Ordinal) && !_launchTasks.Contains(t.Id)).ToList();
            if (launches.Count > 0)
            {
                foreach (TaskRow t in launches) _launchTasks.Add(t.Id);
                r.Status = "FAIL";
                r.Message = (r.Message.Length > 0 ? r.Message + " | " : "") + "this step started a launch task: " + string.Join(", ", launches.Select(t => t.Title + " [" + t.Status + "]"));
            }
            r.Ms = sw.ElapsedMilliseconds;
            _results.Add(r);
            Console.WriteLine($"TOUR [{r.Index,3}] {r.Status,-4} {r.Op,-10} {r.Message}");
            if (r.Status == "FAIL") ExitCode = VisualTour.ExitFailures;
            WriteResult();
        }

        try { CloseAll(); _win.Close(); } catch (Exception) { }
        Dispatcher.UIThread.UnhandledException -= onUnhandled;
        WriteResult();
    }

    private sealed class TourFail : Exception
    {
        public TourFail(string message) : base(message)
        {
        }
    }

    private string Exec(TourStep step, TourStepResult r)
    {
        switch (step.Op)
        {
            case "navigate":
                Win.Navigate(step.Page);
                Settle(200);
                if (Win.CurrentPageId != step.Page) throw new TourFail($"navigate: still on '{Win.CurrentPageId}', page '{step.Page}' unknown");
                return "page=" + Win.CurrentPageId;
            case "click":
            case "rightclick":
            case "dblclick":
                return Click(step);
            case "hover":
                {
                    var (c, top, p) = Locate(step.Target!);
                    top.MouseMove(p, RawInputModifiers.None);
                    return Describe(c, p);
                }
            case "key":
                return Key(step.Keys);
            case "type":
                return TypeText(step);
            case "scroll":
                return Scroll(step);
            case "resize":
                Win.Width = step.Width;
                Win.Height = step.Height;
                Settle(300);
                return $"size={Win.ClientSize.Width:F0}x{Win.ClientSize.Height:F0}";
            case "theme":
                {
                    bool dark = step.Theme == "dark";
                    Config.CONFIG.Set("ui_dark", dark);
                    Win.SetDark(dark);
                    return "dark=" + Win.IsDark;
                }
            case "wait":
                Settle(Math.Max(0, step.Ms));
                return "waited " + step.Ms + "ms";
            case "shot":
                {
                    Settle(step.Ms >= 0 ? step.Ms : 250);
                    string file = Shot(step.Name, step.Layer == "main");
                    r.Shot = Path.GetFileName(file);
                    return "shot " + r.Shot + $" ({Win.ClientSize.Width:F0}x{Win.ClientSize.Height:F0}, layers={Layers().Count})";
                }
            case "dump":
                return Dump(step.Name);
            case "expect":
                {
                    Control? c = TourFinder.Find(Layers(), step.Target!);
                    if (step.Absent)
                    {
                        if (c != null) throw new TourFail("expected absent but found: " + step.Target);
                        return "absent: " + step.Target;
                    }
                    if (c == null) throw new TourFail("not found: " + step.Target + Nearby(step.Target!));
                    return "found " + c.GetType().Name;
                }
            case "close":
                return CloseTop();
            case "reset":
                return Reset();
            default:
                throw new TourFail("unknown op '" + step.Op + "'");
        }
    }

    // ———————— 层(主窗口 / 浮层 / 对话框) ————————

    /// <summary>所有可见层,最上层在前:对话框(后开的在前)及其浮层 → 主窗口的浮层 → 主窗口。
    /// 浮层有两种宿主:独立的 PopupRoot(TopLevel)或窗口内的覆盖层(OverlayPopupHost,在宿主窗口的可视树里);
    /// 后一种也单独列成一层放在宿主前面,这样「浮层里的『管理』」优先于「页面上的『管理』」。</summary>
    private List<Visual> Layers()
    {
        var list = new List<Visual>();
        var owners = new List<TopLevel>();
        foreach (Window w in OwnedWindows(Win).Where(w => w.IsVisible).Reverse()) owners.Add(w);
        owners.Add(Win);
        foreach (TopLevel owner in owners)
        {
            foreach (Visual pr in PopupHostsOf(owner).AsEnumerable().Reverse()) list.Add(pr);
            list.Add(owner);
        }
        return list;
    }

    private static IEnumerable<Window> OwnedWindows(Window w)
    {
        foreach (Window o in w.OwnedWindows)
        {
            yield return o;
            foreach (Window oo in OwnedWindows(o)) yield return oo;
        }
    }

    /// <summary>某个宿主上打开着的浮层宿主(PopupRoot 或覆盖层里的 OverlayPopupHost),按打开顺序。</summary>
    private List<Visual> PopupHostsOf(TopLevel owner)
    {
        var roots = new List<Visual>();
        foreach (Popup p in _openPopups.ToList())
        {
            if (!p.IsOpen || p.Host is not Visual host || !host.IsVisible) continue;
            TopLevel? parent = host is TopLevel
                ? (TopLevel.GetTopLevel(p) ?? (p.PlacementTarget is Visual t ? TopLevel.GetTopLevel(t) : null) ?? Win)
                : TopLevel.GetTopLevel(host);
            if (ReferenceEquals(parent, owner) && !roots.Contains(host)) roots.Add(host);
        }
        return roots;
    }

    /// <summary>只取独立 TopLevel 的浮层(截图合成用;覆盖层浮层已经在宿主窗口的帧里)。</summary>
    private List<TopLevel> PopupRootsOf(TopLevel owner) => PopupHostsOf(owner).OfType<TopLevel>().ToList();

    // ———————— 点击 / 按键 / 输入 / 滚动 ————————

    private (Control Control, TopLevel Top, Point Point) Locate(TourSelector sel)
    {
        Control? c = TourFinder.Find(Layers(), sel);
        if (c == null) throw new TourFail("not found: " + sel + Nearby(sel));
        BringIntoViewIfNeeded(c);
        if (TopLevel.GetTopLevel(c) is not TopLevel top) throw new TourFail("found but detached from any TopLevel: " + sel);
        Point? p = TourFinder.ClickPoint(top, c);
        if (p is not Point pt) throw new TourFail("found but cannot compute its center: " + sel);
        return (c, top, pt);
    }

    private void BringIntoViewIfNeeded(Control c)
    {
        if (TopLevel.GetTopLevel(c) is not TopLevel top) return;
        Point? p = TourFinder.CenterInTop(c);
        bool outside = p is not Point pt || pt.X < 0 || pt.Y < 0 || pt.X > top.ClientSize.Width || pt.Y > top.ClientSize.Height;
        if (!outside)
        {
            // 在窗口里,但可能被所在的 ScrollViewer 裁掉
            ScrollViewer? sv = c.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
            if (sv != null)
            {
                Point? inSv = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), sv);
                outside = inSv is not Point q || q.Y < 0 || q.Y > sv.Viewport.Height || q.X < 0 || q.X > sv.Viewport.Width;
            }
        }
        if (outside)
        {
            c.BringIntoView();
            Settle(150);
        }
    }

    private string Click(TourStep step)
    {
        var (c, top, p) = Locate(step.Target!);
        string? label = LabelOf(c);
        if (label != null && VisualTour.LaunchWords.Contains(label))
            throw new TourFail($"refused: '{label}' would launch Minecraft (visual tour never starts the game)");
        var (hitOk, hitType) = TourFinder.HitCheck(top, c, p);
        MouseButton button = step.Op == "rightclick" ? MouseButton.Right : MouseButton.Left;
        top.MouseMove(p, RawInputModifiers.None);
        top.MouseDown(p, button, RawInputModifiers.None);
        top.MouseUp(p, button, RawInputModifiers.None);
        if (step.Op == "dblclick")
        {
            top.MouseDown(p, button, RawInputModifiers.None);
            top.MouseUp(p, button, RawInputModifiers.None);
        }
        RenderPass();
        string msg = Describe(c, p) + (hitOk ? "" : $" [warn: hit-test landed on {hitType}, not the target]");
        return msg;
    }

    /// <summary>目标或它所在按钮的文字(拒点「启动游戏」用)。</summary>
    private static string? LabelOf(Control c)
    {
        string? own = TourFinder.OwnText(c);
        Button? btn = c as Button ?? c.GetVisualAncestors().OfType<Button>().FirstOrDefault();
        if (btn != null)
        {
            string? bt = TourFinder.OwnText(btn)
                ?? btn.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            if (bt != null) return TourFinder.Normalize(bt);
        }
        return own == null ? null : TourFinder.Normalize(own);
    }

    private static string Describe(Control c, Point p)
        => $"{c.GetType().Name}{(string.IsNullOrEmpty(c.Name) ? "" : "#" + c.Name)} at {p.X:F0},{p.Y:F0}";

    private TopLevel FocusTop()
    {
        List<Visual> layers = Layers();
        // 键盘给最上面的窗口(对话框优先;浮层不抢键盘焦点,Esc 由宿主窗口路由给浮层)
        foreach (Visual v in layers)
        {
            if (v is Window w) return w;
        }
        return Win;
    }

    private string Key(string keys)
    {
        var (main, ctrl, shift, alt, meta) = TourScriptParser.ParseKeys(keys);
        if (main.Length == 0 || !Enum.TryParse(main, ignoreCase: false, out Key key))
            throw new TourFail("unknown key: " + keys);
        RawInputModifiers mods = RawInputModifiers.None;
        if (ctrl) mods |= RawInputModifiers.Control;
        if (shift) mods |= RawInputModifiers.Shift;
        if (alt) mods |= RawInputModifiers.Alt;
        if (meta) mods |= RawInputModifiers.Meta;
        PhysicalKey phys = Enum.TryParse(main == "Enter" ? "Enter" : main, out PhysicalKey pk) ? pk : PhysicalKey.None;
        if (main.Length == 2 && main[0] == 'D' && char.IsDigit(main[1]) && Enum.TryParse("Digit" + main[1], out PhysicalKey dk)) phys = dk;
        string? symbol = main.Length == 1 ? main.ToLowerInvariant() : null;
        TopLevel top = FocusTop();
        top.KeyPress(key, mods, phys, symbol);
        top.KeyRelease(key, mods, phys, symbol);
        RenderPass();
        return $"{keys} -> {top.GetType().Name}";
    }

    private string TypeText(TourStep step)
    {
        TopLevel top = FocusTop();
        if (step.Target != null && !step.Target.IsEmpty)
        {
            var (c, t, p) = Locate(step.Target);
            t.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
            t.MouseUp(p, MouseButton.Left, RawInputModifiers.None);
            if (c is TextBox tb) tb.Focus();
            else c.GetVisualAncestors().OfType<TextBox>().FirstOrDefault()?.Focus();
            RenderPass();
            top = t;
        }
        top.KeyTextInput(step.Text);
        RenderPass();
        return "typed " + step.Text.Length + " chars";
    }

    private string Scroll(TourStep step)
    {
        if (step.ScrollTo != null && !step.ScrollTo.IsEmpty)
        {
            Control? c = TourFinder.Find(Layers(), step.ScrollTo);
            if (c == null) throw new TourFail("scroll target not found: " + step.ScrollTo + Nearby(step.ScrollTo));
            c.BringIntoView();
            RenderPass();
            ScrollViewer? sv0 = TourFinder.NearestScrollViewer(c);
            return "brought into view: " + step.ScrollTo + (sv0 == null ? "" : $" (offset {sv0.Offset.Y:F0}/{sv0.Extent.Height:F0})");
        }
        ScrollViewer? sv;
        if (step.Target != null && !step.Target.IsEmpty)
        {
            Control? host = TourFinder.Find(Layers(), step.Target);
            if (host == null) throw new TourFail("scroll host not found: " + step.Target + Nearby(step.Target));
            sv = TourFinder.NearestScrollViewer(host) ?? host.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s.Extent.Height > s.Viewport.Height + 1);
        }
        else
        {
            sv = Layers().Select(TourFinder.LargestScrollable).FirstOrDefault(s => s != null);
        }
        if (sv == null) throw new TourFail("no scrollable area (content fits in the viewport)");
        double max = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
        double y = step.ScrollTop ? 0 : Math.Clamp(sv.Offset.Y + step.Screens * sv.Viewport.Height, 0, max);
        sv.Offset = new Vector(sv.Offset.X, y);
        RenderPass();
        return $"offset {sv.Offset.Y:F0}/{max:F0} (viewport {sv.Viewport.Height:F0})";
    }

    // ———————— 关闭 / 复位 ————————

    private string CloseTop()
    {
        // 先关最上面的对话框上的浮层 / 对话框,再关主窗口的浮层(与用户点空白处 / 按 Esc 的层次一致)
        foreach (Visual v in Layers())
        {
            if (ReferenceEquals(v, Win)) break;
            Popup? p = _openPopups.LastOrDefault(x => x.IsOpen && ReferenceEquals(x.Host, v));
            if (p != null)
            {
                p.IsOpen = false;
                Settle(150);
                return "closed popup";
            }
            if (v is Window w)
            {
                w.Close();
                Settle(200);
                return "closed dialog '" + w.Title + "'";
            }
        }
        throw new TourFail("nothing to close (no dialog / popup open)");
    }

    private void CloseAll()
    {
        for (int i = 0; i < 10; i++)
        {
            try { CloseTop(); }
            catch (TourFail) { return; }
        }
    }

    /// <summary>回到干净状态:关掉对话框 / 浮层、退出布局编辑态、按剧本起始尺寸 / 主题、回启动页。</summary>
    private string Reset()
    {
        CloseAll();
        var done = new TourSelector { Text = I18nCore.Tr("完成编辑") };
        Control? d = TourFinder.Find(Layers(), done);
        string extra = "";
        if (d != null)
        {
            var (_, top, p) = Locate(done);
            top.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
            top.MouseUp(p, MouseButton.Left, RawInputModifiers.None);
            Settle(200);
            extra = " (left layout edit mode)";
        }
        Win.Width = _script.Width;
        Win.Height = _script.Height;
        bool dark = _script.Theme == "dark";
        Config.CONFIG.Set("ui_dark", dark);
        Win.SetDark(dark);
        Win.Navigate("launch");
        Settle(300);
        return "reset" + extra;
    }

    // ———————— 截图 ————————

    private string Shot(string name, bool mainOnly)
    {
        string file = Path.Combine(_outDir, name + ".png");
        RenderPass();
        List<Visual> layers = Layers();
        foreach (Visual v in layers)
        {
            if (v is TopLevel t) Discard(t);
        }
        using Bitmap? main = Win.CaptureRenderedFrame();
        if (main == null) throw new TourFail("could not capture the main window frame");
        if (mainOnly || !layers.Any(v => v is TopLevel && !ReferenceEquals(v, Win)))
        {
            using FileStream fs = File.Create(file);
            main.Save(fs);
            return file;
        }

        // 合成:主窗口 → 主窗口的浮层(按打开顺序)→ 对话框(居中于主窗口)→ 对话框的浮层
        PixelSize size = main.PixelSize;
        using var rtb = new RenderTargetBitmap(size, new Vector(96, 96));
        var frames = new List<IDisposable>();
        try
        {
            using (DrawingContext ctx = rtb.CreateDrawingContext())
            {
                ctx.DrawImage(main, new Rect(0, 0, size.Width, size.Height));
                DrawPopups(ctx, Win, new Point(0, 0), frames);
                foreach (Window dlg in OwnedWindows(Win).Where(w => w.IsVisible))
                {
                    Bitmap? f = dlg.CaptureRenderedFrame();
                    if (f == null) continue;
                    frames.Add(f);
                    // 半透明压暗主窗口(对话框是模态的,示意用户此时只能操作对话框)
                    ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), new Rect(0, 0, size.Width, size.Height));
                    var origin = new Point(Math.Max(0, (size.Width - f.PixelSize.Width) / 2.0), Math.Max(0, (size.Height - f.PixelSize.Height) / 2.0));
                    ctx.DrawImage(f, new Rect(origin, new Size(f.PixelSize.Width, f.PixelSize.Height)));
                    DrawPopups(ctx, dlg, origin, frames, PositionOf(dlg));
                    // 对话框也单独存一张(完整尺寸,不被主窗口裁)
                    using FileStream dfs = File.Create(Path.Combine(_outDir, name + ".dialog.png"));
                    f.Save(dfs);
                }
            }
            using FileStream fs2 = File.Create(file);
            rtb.Save(fs2);
        }
        finally
        {
            foreach (IDisposable d in frames) d.Dispose();
        }
        return file;
    }

    private void DrawPopups(DrawingContext ctx, TopLevel owner, Point ownerOrigin, List<IDisposable> frames, PixelPoint? ownerPos = null)
    {
        PixelPoint basePos = ownerPos ?? new PixelPoint(0, 0);
        foreach (TopLevel pr in PopupRootsOf(owner))
        {
            Bitmap? f = pr.CaptureRenderedFrame();
            if (f == null) continue;
            frames.Add(f);
            PixelPoint pos = PositionOf(pr) ?? new PixelPoint(0, 0);
            var at = new Point(ownerOrigin.X + pos.X - basePos.X, ownerOrigin.Y + pos.Y - basePos.Y);
            ctx.DrawImage(f, new Rect(at, new Size(f.PixelSize.Width, f.PixelSize.Height)));
        }
    }

    /// <summary>无头平台上 TopLevel 的「屏幕」位置(浮层定位器写进去的就是相对宿主客户区的位置)。反射读,读不到返回 null。</summary>
    private static PixelPoint? PositionOf(TopLevel t)
    {
        try
        {
            object? impl = t.PlatformImpl;
            object? v = impl?.GetType().GetProperty("Position")?.GetValue(impl);
            return v is PixelPoint p ? p : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Discard(TopLevel t)
    {
        try
        {
            using Bitmap? warm = t.CaptureRenderedFrame();
        }
        catch (Exception)
        {
            // 丢弃帧失败无所谓
        }
    }

    // ———————— dump / 诊断 ————————

    private string Dump(string name)
    {
        var sb = new StringBuilder();
        int n = 0;
        foreach (Visual layer in Layers())
        {
            sb.AppendLine("=== layer " + layer.GetType().Name + (layer is Window w && !ReferenceEquals(w, Win) ? " '" + w.Title + "'" : ""));
            foreach (string line in TourFinder.Describe(layer))
            {
                sb.AppendLine(line);
                n++;
            }
        }
        File.WriteAllText(Path.Combine(_outDir, name + ".tree.txt"), sb.ToString(), new UTF8Encoding(false));
        return $"dumped {n} elements to {name}.tree.txt";
    }

    /// <summary>找不到时附上「长得像的」可见文字,方便改剧本。</summary>
    private string Nearby(TourSelector sel)
    {
        string probe = sel.Text.Length > 0 ? sel.Text : sel.Contains.Length > 0 ? sel.Contains : sel.Tip;
        if (probe.Length == 0) return "";
        string key = probe.Length > 2 ? probe.Substring(0, 2) : probe;
        var near = new List<string>();
        foreach (Visual layer in Layers())
        {
            foreach (Visual v in layer.GetVisualDescendants())
            {
                if (v is not Control c || !TourFinder.IsShown(c)) continue;
                string? t = TourFinder.OwnText(c) ?? TourFinder.TipOf(c);
                if (t != null && t.Contains(key, StringComparison.Ordinal) && !near.Contains(t)) near.Add(TourFinder.Normalize(t));
                if (near.Count >= 6) break;
            }
        }
        return near.Count == 0 ? $" (page={Win.CurrentPageId}, nothing similar visible)" : $" (page={Win.CurrentPageId}, similar: {string.Join(" | ", near)})";
    }

    // ———————— 渲染推进 ————————

    private static void RenderPass()
    {
        TourClock.Advance();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Settle(int ms)
    {
        DateTime until = DateTime.UtcNow.AddMilliseconds(Math.Max(0, ms));
        do
        {
            RenderPass();
            if (DateTime.UtcNow >= until) break;
            System.Threading.Thread.Sleep(30);
        } while (true);
        RenderPass();
    }

    // ———————— 结果 ————————

    private void WriteResult()
    {
        int fail = _results.Count(r => r.Status == "FAIL");
        var payload = new Dictionary<string, object?>
        {
            ["script"] = Path.GetFullPath(_scriptPath),
            ["outDir"] = _outDir,
            ["home"] = SafeRoot(),
            ["started"] = _started.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["finished"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["fixture"] = FixtureInfo,
            ["parseProblems"] = _script.Problems,
            ["summary"] = new Dictionary<string, object>
            {
                ["steps"] = _script.Steps.Count,
                ["ran"] = _results.Count(r => r.Index >= 0),
                ["ok"] = _results.Count(r => r.Status == "OK"),
                ["fail"] = fail,
                ["shots"] = _results.Count(r => r.Shot.Length > 0),
            },
            ["steps"] = _results,
            ["shellOpensSuppressed"] = ShellOpens,
            ["network"] = TourNet.Log.Select(x => x.Action + " " + x.Url).Distinct().ToList(),
        };
        try
        {
            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            File.WriteAllText(Path.Combine(_outDir, "tour-result.json"), json, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("--visual-tour: could not write tour-result.json: " + ex.Message);
        }
    }

    private static string SafeRoot()
    {
        try { return Utils.ROOT; } catch (Exception) { return "?"; }
    }
}
