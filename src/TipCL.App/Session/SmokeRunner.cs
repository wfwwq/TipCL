using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Threading;
using TipCL.App.Common;
using TipCL.App.Shell;
using TipCL.App.Ui;

namespace TipCL.App.Session;

/// <summary>
/// --smoke 冒烟骨架（M1-A1）：无头平台启动，渲染 MainWindow 并保存 smoke.png 后退出。
/// A5 负责充实冒烟流程（输出路径参数、退出码约定、文档化）。
/// </summary>
internal static class SmokeRunner
{
    public static int Run(AppBuilder builder)
    {
        // SMOKE_NET=<url>:网络探针,打印 .NET 侧的 HTTP 结果后退出(真机网络排障用)
        var netUrl = Environment.GetEnvironmentVariable("SMOKE_NET");
        if (!string.IsNullOrEmpty(netUrl))
            return NetProbe(netUrl);

        // SMOKE_INSTALL=<version>:无头安装 E2E 钩子(真机排障/回归用)
        var installVer = Environment.GetEnvironmentVariable("SMOKE_INSTALL");
        if (!string.IsNullOrEmpty(installVer))
            return InstallProbe(installVer, Environment.GetEnvironmentVariable("SMOKE_INSTANCE") ?? "probe");

        // SMOKE_PROBE=dialog-close:对话框「点按钮能不能关窗」回归探针
        // (真机 defect:点确定/取消后窗口不关、await 永不返回;详见 DialogCloseProbe 注释)
        if (string.Equals(Environment.GetEnvironmentVariable("SMOKE_PROBE"), "dialog-close", StringComparison.Ordinal))
            return DialogCloseProbe(builder);

        // SMOKE_PROBE=popup-theme:启动页浮层里「隐式 ControlTheme 取不到模板」的回归探针
        // (真机 defect:浮层里 TextBox/ComboBox/Slider 一个都画不出来;详见 PopupThemeProbe 注释)
        if (string.Equals(Environment.GetEnvironmentVariable("SMOKE_PROBE"), "popup-theme", StringComparison.Ordinal))
            return PopupThemeProbe(builder);

        // SMOKE_PROBE=widgets:R32-LINT 部件层接线实测（注册表项数 + 每个部件的 id/标题/生效尺寸 + 真造一次内容）
        if (string.Equals(Environment.GetEnvironmentVariable("SMOKE_PROBE"), "widgets", StringComparison.Ordinal))
            return WidgetsProbe(builder);

        // SMOKE_PROBE=widgets-lifetime:R32-LINT 部件层接线实测(走 SetupWithLifetime,
        // 与生产启动同一条路。补上 --smoke 通道够不到的那一半
        if (string.Equals(Environment.GetEnvironmentVariable("SMOKE_PROBE"), "widgets-lifetime", StringComparison.Ordinal))
            return WidgetsProbeWithLifetime(builder);

        int exitCode = 0;
        var output = Path.GetFullPath("smoke.png");

        // 静态截图不该被入场动画影响:FadeIn 会把子项初值设成 Opacity=0 再靠过渡补回,
        // 无头单帧捕获跑不动过渡 → 内容整片不可见。冒烟一律关动画。
        Motion.Enabled = false;

        // SMOKE_TASKS=1:塞两条假任务(进行中 + 失败),供任务页视觉回归(SMOKE_PAGE=tasks)
        if (Environment.GetEnvironmentVariable("SMOKE_TASKS") == "1")
        {
            var running = TaskStore.Add("smoke-run", "安装 26.3(冒烟)");
            running.Indeterminate = false;
            running.Progress = 42;
            running.Status = "下载 client.jar";
            running.Speed = "3.2 MB/s";
            running.Append("下载 client.jar …\n校验 sha1 …");
            var failed = TaskStore.Add("smoke-fail", "安装 fabric-loader(冒烟)");
            TaskStore.Finish(failed, false, "连接超时");
        }

        builder
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .AfterSetup(_ =>
            {
                try
                {
                    // 明暗只由 SMOKE_DARK 决定,且要进(内存里的)配置:外壳构造与设置页加载都按 ui_dark 取明暗,
                    // 只调 SetDark 的话设置页一加载就把深色帧改回浅色
                    TipCL.Core.Launcher.Config.CONFIG.Set("ui_dark", Environment.GetEnvironmentVariable("SMOKE_DARK") == "1");
                    var window = new Shell.MainWindow();
                    var page = Environment.GetEnvironmentVariable("SMOKE_PAGE");
                    window.Show();
                    if (!string.IsNullOrEmpty(page))
                        window.Navigate(page);
                    // SMOKE_SEG=1..6:下载页切到某个目录分段(Mod/整合包/数据包/资源包/光影/世界)
                    if (int.TryParse(Environment.GetEnvironmentVariable("SMOKE_SEG"), out var seg)
                        && window.CurrentPageId == "download"
                        && window.CurrentPage is TipCL.App.Pages.Download.DownloadPage dl)
                        dl.SelectSegment(seg);
                    // SMOKE_SETTINGS_TAB=0..6:设置页切到某个内层导航项(Java / 服务器 / 游戏时长 等)
                    if (int.TryParse(Environment.GetEnvironmentVariable("SMOKE_SETTINGS_TAB"), out var tab)
                        && window.CurrentPageId == "settings"
                        && window.CurrentPage is TipCL.App.Pages.Settings.SettingsPage st)
                        st.SelectTab(tab);
                    // R8:导航后只跑 RunJobs 不够——无头会话里 await / Thread.Sleep 都不触发
                    // Measure/Arrange,必须显式推渲染计时器,布局与重合成才跑得动
                    // (仓库先例:tests/TipCL.Tests/App/InstanceDetailCrashTests.cs 的 PassAsync)。
                    RenderPass();

                    // R8 真机现象:冷启动首批 8 页里前 4 页把上一页(launch)的帧原样写进新文件名。
                    // 成因候选:SMOKE_WAIT 默认 0 时零 settle,CaptureRenderedFrame 抓到尚未重合成的上一帧。
                    // 故未显式给 SMOKE_WAIT 时也补一个最小 settle;显式给了就仍等这么久(原语义)。
                    var waitRaw = Environment.GetEnvironmentVariable("SMOKE_WAIT");
                    var waitGiven = int.TryParse(waitRaw, out var waitSec);
                    var settleMs = waitGiven ? Math.Max(0, waitSec) * 1000 : DefaultSettleMs;
                    Settle(settleMs);

                    if (Environment.GetEnvironmentVariable("SMOKE_PROBE") == "1")
                    {
                        foreach (var v in window.GetVisualDescendants().Prepend(window))
                        {
                            Console.WriteLine($"PROBE {v.GetType().Name} cls=[{string.Join(",", (v as Avalonia.StyledElement)?.Classes ?? [])}] bounds={v.Bounds.Width:F1}x{v.Bounds.Height:F1}@{v.Bounds.X:F0},{v.Bounds.Y:F0}");
                        }
                    }
                    // 捕获前再显式推一次渲染 pass,然后捕获两次、丢弃第一帧、只保存第二帧:
                    // 第一张最有可能就是 R8 那个「尚未重合成的上一帧」。
                    RenderPass();
                    DiscardFirstFrame(window);
                    using var frame = window.CaptureRenderedFrame();
                    if (frame is null)
                    {
                        Console.Error.WriteLine("--smoke：未能捕获渲染帧");
                        exitCode = 1;
                    }
                    else
                    {
                        using var stream = File.Create(output);
                        frame.Save(stream);
                        Console.WriteLine($"--smoke：已输出 {output}");
                    }
                    window.Close();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"--smoke 失败: {ex}");
                    exitCode = 1;
                }
            })
            .SetupWithoutStarting();

        return exitCode;
    }

    /// <summary>未显式给 SMOKE_WAIT 时的最小 settle 毫秒数:给外壳一次重新测量 + 重合成的机会。</summary>
    private const int DefaultSettleMs = 400;

    /// <summary>
    /// 显式推一次渲染 pass:计时器 tick + 跑 Dispatcher 队列。无头会话里只 RunJobs 不会触发
    /// Measure/Arrange,布局、模板、重合成都要靠这次 tick 才会跑(仓库先例见 DECISIONS.md D-39)。
    /// </summary>
    private static void RenderPass()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// settle:ms 毫秒内反复推渲染 pass(tick + RunJobs),让异步灌进来的数据、待跑布局与
    /// 重合成落地。ms 为 0 时仍至少推两次渲染 pass(不会退化成「什么都不做」)。
    /// </summary>
    private static void Settle(int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(Math.Max(0, ms));
        do
        {
            RenderPass();
            if (DateTime.UtcNow >= until) break;
            System.Threading.Thread.Sleep(30);
        } while (true);
    }

    /// <summary>捕获一次并丢弃(它最可能就是上一页的残留帧);失败只记一笔,不让冒烟直接退出。</summary>
    private static void DiscardFirstFrame(Window window)
    {
        try
        {
            using var warm = window.CaptureRenderedFrame();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"--smoke:丢弃的首帧捕获失败(忽略): {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// SMOKE_PROBE=dialog-close:对话框「点按钮到底关不关窗」回归探针(真机回归用)。
    ///
    /// 背景(真机 Linux/X11):反馈同意框弹出来之后,真的用鼠标点「暂不同意 / 同意」窗口不关、
    /// 调用方 <c>await</c> 永不返回,配置也写不进去;只有 WM_DELETE_WINDOW(xdotool windowclose)能关掉,
    /// 关掉后调用方把它当成「取消」。根因是 <c>Common/Dlg.cs</c> 里 Ask/Choose/Prompt 的按钮 Click 只
    /// <c>result.TrySetResult(...)</c> —— WinForms 版由 DialogResult / 图层 Close 顺带完成的「关窗」这一步,
    /// 移植时只留在了 <c>Panel</c> 上(所以首启向导能关、确认框关不掉)。
    ///
    /// 探针走真实路径(不直接摸 Dlg 内部):真开一个 <see cref="Dlg.Confirm"/>,在它自己的窗口里按
    /// 视觉树找到文案为「确定 / 取消」的 Button 并 RaiseEvent(Click)(等价于真鼠标点它),断言:
    ///   1) 窗口真的关掉(<c>IsVisible=false</c>、不再挂在宿主 OwnedWindows 上、Dlg.AnyOpen 归位);
    ///   2) 调用方 Task 在超时内返回,**且值符合按钮语义**(确定=true / 取消=false)。
    /// 最后验兜底语义:直接 Close() 窗口(等价 WM_DELETE_WINDOW)时 Task 必须返回 false。
    ///
    /// 失败大声失败:每条打 <c>DIALOG-CLOSE FAIL: ...</c> 并以退出码 5 结束
    /// (沿用 SMOKE_NET=2 / SMOKE_INSTALL=3 的约定;4 是 Program.Main 的兜底异常码)。
    /// </summary>
    private static int DialogCloseProbe(AppBuilder builder)
    {
        const int TimeoutMs = 5000;
        var problems = new System.Collections.Generic.List<string>();

        builder
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .AfterSetup(_ =>
            {
                Window? host = null;
                try
                {
                    Motion.Enabled = false;
                    TipCL.Core.Launcher.Config.CONFIG.Set("ui_dark", Environment.GetEnvironmentVariable("SMOKE_DARK") == "1");
                    host = new Shell.MainWindow();
                    host.Show();
                    RenderPass();
                    Console.WriteLine("DIALOG-CLOSE: 宿主窗口已显示");

                    CheckClick("确定", wmClose: false, expected: true);
                    CheckClick("取消", wmClose: false, expected: false);
                    CheckClick("确定", wmClose: true, expected: false);   // 只关窗 = 取消(兜底语义)
                }
                catch (Exception ex)
                {
                    problems.Add("探针自身抛异常: " + ex);
                }
                finally
                {
                    try { host?.Close(); } catch (Exception) { }
                }

                // 开一个确认框,等到窗口出现,再按 label 找按钮点它(或只关窗),逐条断言。
                void CheckClick(string label, bool wmClose, bool expected)
                {
                    var what = wmClose ? "只关窗(WM_DELETE_WINDOW)" : $"点「{label}」";
                    var task = Dlg.Confirm("探针", $"{what}应关窗", "确定", "取消", owner: host);
                    var dlg = PumpFor(() => host!.OwnedWindows.FirstOrDefault(), TimeoutMs);
                    if (dlg is null)
                    {
                        problems.Add($"{what}:对话框窗口没弹出来(ShowDialog 没跑起来)");
                        return;
                    }

                    if (wmClose)
                    {
                        dlg.Close();
                    }
                    else
                    {
                        var btn = PumpFor(() => ButtonByText(dlg, label), TimeoutMs);
                        if (btn is null)
                        {
                            problems.Add($"{what}:对话框里找不到文案为「{label}」的按钮");
                            Cleanup();
                            return;
                        }
                        Console.WriteLine($"DIALOG-CLOSE: 找到「{label}」按钮 bounds={btn.Bounds.Width:F0}x{btn.Bounds.Height:F0}@{btn.Bounds.X:F0},{btn.Bounds.Y:F0} enabled={btn.IsEnabled}");
                        btn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    }

                    if (!PumpFor(() => task.IsCompleted, TimeoutMs))
                        problems.Add($"{what}:Task 超时没返回(await 卡住;窗口可见={dlg.IsVisible})");
                    else if (task.GetAwaiter().GetResult() != expected)
                        problems.Add($"{what}:Task 返回 {task.Result},应为 {expected}");
                    else
                        Console.WriteLine($"DIALOG-CLOSE: {what} → Task={task.Result}");

                    if (PumpFor(() => !dlg.IsVisible, 1000))
                        Console.WriteLine($"DIALOG-CLOSE: {what}后窗口已关闭");
                    else
                        problems.Add($"{what}:窗口没关(IsVisible 仍为 true)");

                    if (!PumpFor(() => !Dlg.AnyOpen, 1000))
                        problems.Add($"{what}:Dlg.AnyOpen 仍为 true,模态状态没退出");

                    if (!PumpFor(() => !host!.OwnedWindows.Contains(dlg), 1000))
                        problems.Add($"{what}:窗口还挂在宿主 OwnedWindows 上");

                    Cleanup();

                    // 清场:万一没关成,别把模态窗口留给后面的探针
                    void Cleanup()
                    {
                        if (!dlg.IsVisible) return;
                        try { dlg.Close(); } catch (Exception) { }
                        PumpFor(() => !dlg.IsVisible, 1000);
                    }
                }
            })
            .SetupWithoutStarting();

        if (problems.Count == 0)
        {
            Console.WriteLine("DIALOG-CLOSE OK");
            return 0;
        }
        foreach (var p in problems) Console.Error.WriteLine("DIALOG-CLOSE FAIL: " + p);
        return 5;
    }

    /// <summary>推渲染 pass 直到条件成立;超过 timeoutMs 就返回「最终是否成立」。</summary>
    private static bool PumpFor(Func<bool> ready, int timeoutMs)
    {
        var until = DateTime.UtcNow.AddMilliseconds(Math.Max(0, timeoutMs));
        while (true)
        {
            if (ready()) return true;
            if (DateTime.UtcNow >= until) return ready();
            RenderPass();
            System.Threading.Thread.Sleep(20);
        }
    }

    /// <summary><see cref="PumpFor(Func{bool}, int)"/> 的取值版:等到值非 null 就返回它,超时返回 null。</summary>
    private static T? PumpFor<T>(Func<T?> value, int timeoutMs) where T : class
    {
        T? found = null;
        PumpFor(() => (found = value()) != null, timeoutMs);
        return found;
    }

    /// <summary>对话框里按文案找按钮:先视觉树(已布局),找不到再退到逻辑树。</summary>
    private static Button? ButtonByText(Window dlg, string text)
        => dlg.GetVisualDescendants().OfType<Button>()
             .Concat(dlg.GetLogicalDescendants().OfType<Button>())
             .FirstOrDefault(b => b.Content as string == text);

    /// <summary>
    /// SMOKE_PROBE=popup-theme:启动页浮层里「靠隐式 ControlTheme 取模板的控件一个都画不出来」的回归探针。
    ///
    /// 真机现象(Linux/X11):启动页 → 齿轮 → 启动配置浮层展开「更多设置」后,浮层里 TextBlock 正常,
    /// 但 TextBox(分辨率 854/480)、ComboBox(实例/版本/用户/Java)、Slider(内存)全都不渲染 ——
    /// 逐像素扫描那几行只有空白。主窗口里的 plain ComboBox/Slider 却正常。
    ///
    /// 机制:<c>Ui.Input/Combo/Sld</c> 建的是**没有显式 Theme** 的裸控件,模板来自 FluentTheme 的
    /// 隐式 ControlTheme;隐式查找走「控件 → 逻辑/可视父链 → TopLevel → Application.Styles」这条
    /// 宿主链。而 <c>Ui.Btn/Switch/Multi</c> 是 `Theme = Ui.Theme(key)`(构造时从 Application 直接取
    /// 到的 ControlTheme 对象)、`Dyn()` 走 `Application.GetResourceObservable`,两者都不依赖宿主链 ——
    /// 所以「显式的能画、隐式的画不出」正好对应宿主链断掉。
    /// 可疑断点:<c>LaunchPopups.MakePopup</c> 建了 Popup 却没挂进任何树。
    ///
    /// 四组对照(A/D/B 必须全绿;C 是机制对照,预期**一直**失败,不参与退出码):
    ///   A 同形裸控件放进主窗口里已有的面板(在树);
    ///   B **真实**「启动配置」浮层里的数据控件(真机同一条路径);
    ///   C 同形裸控件放进「不挂树的 Popup」(= 修复前 MakePopup 的样子);
    ///   D 同形裸控件放进「挂进页面树的 Popup」(= 修复后 MakePopup 的样子)。
    /// 每条打印 类型 / 显式还是隐式主题 / 有没有模板 / 隐式主题查找中不中 / Bounds / DesiredSize /
    /// 可见性 / 有没有视觉根 —— 这就是「谁失效」的硬证据。
    ///
    /// 失败大声失败:每条打 `POPUP-THEME FAIL: ...` 并以退出码 6 结束
    /// (沿用 SMOKE_NET=2 / SMOKE_INSTALL=3 / dialog-close=5 的约定;4 是 Program.Main 兜底异常码)。
    /// </summary>
    private static int PopupThemeProbe(AppBuilder builder)
    {
        const int TimeoutMs = 5000;
        var problems = new System.Collections.Generic.List<string>();
        var rows = new System.Collections.Generic.List<(string Group, string Label, Control Control)>();

        builder
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .AfterSetup(_ =>
            {
                try
                {
                    Motion.Enabled = false;
                    TipCL.Core.Launcher.Config.CONFIG.Set("ui_dark", Environment.GetEnvironmentVariable("SMOKE_DARK") == "1");
                    var window = new Shell.MainWindow();
                    window.Show();
                    window.Navigate("launch");
                    PumpFor(() => window.CurrentPage is TipCL.App.Pages.Launch.LaunchPage, TimeoutMs);
                    RenderPass();
                    if (window.CurrentPage is not TipCL.App.Pages.Launch.LaunchPage page)
                    {
                        problems.Add("启动页没起来(CurrentPage 不是 LaunchPage),探针没法继续");
                        window.Close();
                        return;
                    }
                    if (page.Content is not Panel pageRoot)
                    {
                        problems.Add("启动页根内容不是 Panel,探针没法挂对照组");
                        window.Close();
                        return;
                    }

                    Console.WriteLine("=== POPUP-THEME 探针:浮层里靠隐式 ControlTheme 取模板的控件 ===");
                    Console.WriteLine("列含义:主题=显式(控件自带 Theme)/隐式(靠 FluentTheme 隐式查找);"
                        + "模板=模板有没有套上;隐式主题查找=控件现在的查找链能否拿到 {x:Type 控件} 的 ControlTheme;"
                        + "OK=有模板+有高度+可见");

                    // —— A:在树对照组(挂进主窗口里已有的面板)——
                    var (aPanel, aKids) = BuildProbeGroup();
                    pageRoot.Children.Add(aPanel);
                    for (int i = 0; i < aKids.Length; i++)
                        rows.Add(("A", ProbeLabels[i], aKids[i]));
                    RenderPass();
                    // 顺手证一条前提:应用级的隐式 ControlTheme 是存在的(在树控件查得到),不是「压根没配主题」
                    var aLookup = aKids[0].TryFindResource(aKids[0].GetType(), out var aTheme);
                    Console.WriteLine($"POPUP-THEME A 组裸 TextBox 的隐式主题查找={(aLookup ? "中" : "无")}"
                        + (aTheme != null ? $" [拿到 {aTheme.GetType().Name}]" : " [没拿到]"));

                    // —— B:真实「启动配置」浮层 ——
                    var configPopup = page.OpenConfigPopupForProbe();
                    var bRoot = configPopup.Child as Control;
                    PumpFor(() => bRoot != null && bRoot.GetVisualRoot() != null, TimeoutMs);
                    RenderPass();
                    // 真机复现路径:先点开「更多设置」(Java/内存/分辨率/直连都在里面)
                    // 从浮层 Child 往下找(不依赖 Popup 是否把 Child 暴露成逻辑子级)
                    var more = bRoot == null ? null : ButtonByTextLogical(bRoot, "更多设置");
                    if (more is null)
                        problems.Add("真实配置浮层里没找到「更多设置」按钮,内存/分辨率那几行还是折叠的");
                    else
                        more.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    PumpFor(() => bRoot != null && bRoot.Bounds.Height > 0, TimeoutMs);
                    RenderPass();
                    foreach (var (label, control) in page.ConfigProbeControls())
                        rows.Add(("B", label, control));
                    // B 的正对照:浮层里显式设了 Theme 的按钮(修复前也该是好的 → 证明不是「浮层整体没画」)
                    if (bRoot != null)
                    {
                        var bPos = FirstByType<TemplatedControl>(bRoot, c => c.Theme != null);
                        if (bPos != null) rows.Add(("B", "显式主题控件(正对照)", bPos));
                    }

                    // —— C/D:同形控件的「不挂树 Popup」 vs 「挂树 Popup」 ——
                    var (cPanel, cKids) = BuildProbeGroup();
                    var cPopup = NewProbePopup(pageRoot, page, cPanel, attach: false);
                    cPopup.IsOpen = true;
                    PumpFor(() => cPanel.GetVisualRoot() != null, TimeoutMs);
                    RenderPass();
                    for (int i = 0; i < cKids.Length; i++)
                        rows.Add(("C", ProbeLabels[i], cKids[i]));

                    var (dPanel, dKids) = BuildProbeGroup();
                    var dPopup = NewProbePopup(pageRoot, page, dPanel, attach: true);
                    dPopup.IsOpen = true;
                    PumpFor(() => dPanel.GetVisualRoot() != null, TimeoutMs);
                    RenderPass();
                    for (int i = 0; i < dKids.Length; i++)
                        rows.Add(("D", ProbeLabels[i], dKids[i]));

                    // —— 打印 + 判定 ——
                    Console.WriteLine("组  控件                 类型         主题  模板  隐式主题查找 Bounds(WxH)   Desired(WxH)  可见/有效  视觉根  判定");
                    var byGroup = new System.Collections.Generic.Dictionary<string, (int Ok, int Total)>();
                    for (int i = 0; i < rows.Count; i++)
                    {
                        var (group, label, control) = rows[i];
                        var ok = PrintRow(group, label, control);
                        byGroup.TryGetValue(group, out var acc);
                        byGroup[group] = (acc.Ok + (ok ? 1 : 0), acc.Total + 1);
                    }
                    foreach (var g in new[] { "A", "B", "C", "D" })
                        if (byGroup.TryGetValue(g, out var acc))
                            Console.WriteLine($"POPUP-THEME 结论 {g}: {acc.Ok}/{acc.Total} 个控件「有模板 + 有高度 + 可见」");

                    // A/B/D 必须全绿
                    foreach (var g in new[] { "A", "B", "D" })
                    {
                        if (!byGroup.TryGetValue(g, out var acc) || acc.Ok != acc.Total)
                            problems.Add($"组 {g} 有控件没画出模板/尺寸({(acc.Total == 0 ? "无数据" : $"{acc.Ok}/{acc.Total}")})");
                    }
                    // C 是机制对照:不挂树的 Popup 若也全绿,说明「挂树才能继承宿主链」这条结论不成立
                    if (byGroup.TryGetValue("C", out var c) && c.Total > 0 && c.Ok == c.Total)
                        Console.WriteLine("POPUP-THEME 注意:组 C(不挂树的 Popup)竟然全绿 —— 说明本机平台上未挂树也继承到了宿主链,根因结论需要重判");

                    var cHost = cPanel.GetVisualRoot();
                    var dHost = dPanel.GetVisualRoot();
                    Console.WriteLine($"POPUP-THEME 宿主: 页面根 Bounds={pageRoot.Bounds.Width:F0}x{pageRoot.Bounds.Height:F0} "
                        + $"A 面板 root={aPanel.GetVisualRoot()?.GetType().Name} "
                        + $"C(master 未挂树)root={(cHost == null ? "无" : cHost.GetType().Name)}, 与主窗同根={ReferenceEquals(cHost, window)}, popup.Bounds={cPopup.Bounds.Width:F0}x{cPopup.Bounds.Height:F0} "
                        + $"D(已挂树)root={(dHost == null ? "无" : dHost.GetType().Name)}, 与主窗同根={ReferenceEquals(dHost, window)}, popup.Bounds={dPopup.Bounds.Width:F0}x{dPopup.Bounds.Height:F0} "
                        + $"B 浮层 root={configPopup.Child?.GetVisualRoot()?.GetType().Name ?? "无"}, 与主窗同根={ReferenceEquals(configPopup.Child?.GetVisualRoot(), window)}, popup.Bounds={configPopup.Bounds.Width:F0}x{configPopup.Bounds.Height:F0}");

                    cPopup.IsOpen = false;
                    dPopup.IsOpen = false;
                    window.Close();
                }
                catch (Exception ex)
                {
                    problems.Add("探针自身抛异常: " + ex);
                }
            })
            .SetupWithoutStarting();

        if (problems.Count == 0)
        {
            Console.WriteLine("POPUP-THEME OK");
            return 0;
        }
        foreach (var p in problems) Console.Error.WriteLine("POPUP-THEME FAIL: " + p);
        return 6;
    }

    /// <summary>
    /// SMOKE_PROBE=widgets 的部件层接线实测探针(通道 A:--smoke 的 SetupWithoutStarting)。
    ///
    /// 为什么需要它:Linux 侧没有「加块 / 选部件」界面(Windows 的 SettingsLayoutPage.LayoutWidgetRows
    /// 与写 custom: 引用的 LayoutTools 都还没移植),无头截图通道够不到那个交互。于是把「部件到底进不进得来」
    /// 拆成几条可量的:注册表插没插电、注册了几个工厂、「选部件」白名单里每类的 id + 中文标题、文档里每个
    /// 部件的定义、以及每个 custom:&lt;id&gt; 真造一次内容后是什么控件、带什么文字、在页面里占多大
    /// (BlockLayout 的 W/H;没写进页面就是默认 1.0 x 1.0)。外加文档里没有定义的 custom:&lt;ghost&gt; 做对照。
    ///
    /// 实测发现(详见 progress/R32-LINT.md §4):--smoke 走的 SetupWithoutStarting **只装平台服务、
    /// 不初始化应用** —— App.OnFrameworkInitializationCompleted() 一次都不被调用,因此本通道里注册表
    /// 必然没插电,custom:&lt;id&gt; 一律造不出内容。这条边界由本通道原样记录(见 NOTE 行),正面的
    /// 「部件真进得来」由同一个 dll 的通道 B(SMOKE_PROBE=widgets-lifetime,SetupWithLifetime,
    /// 与生产启动同一条路)给出。
    ///
    /// 退出码:0 = 本通道下所有「应该成立」的断言成立;7 = 有任一条不成立(沿用 SMOKE_NET=2 /
    /// SMOKE_INSTALL=3 / 4 兜底 / dialog-close=5 / popup-theme=6 的约定)。
    /// </summary>
    private static int WidgetsProbe(AppBuilder builder)
    {
        var problems = new System.Collections.Generic.List<string>();

        builder
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .AfterSetup(_ =>
            {
                try
                {
                    Motion.Enabled = false;
                    TipCL.Core.Launcher.Config.CONFIG.Set("ui_dark", Environment.GetEnvironmentVariable("SMOKE_DARK") == "1");
                    var window = new Shell.MainWindow();
                    window.Show();
                    var pageId = Environment.GetEnvironmentVariable("SMOKE_PAGE");
                    if (string.IsNullOrEmpty(pageId)) pageId = "launch";
                    window.Navigate(pageId);
                    RenderPass();
                    Settle(DefaultSettleMs);
                    RenderPass();

                    Console.WriteLine("=== WIDGETS 通道 A:--smoke(SetupWithoutStarting,进程里没有 classic desktop lifetime)===");
                    WidgetsReport(problems, window, pageId);
                    SaveWidgetsFrame(window);
                    window.Close();
                }
                catch (Exception ex)
                {
                    problems.Add("探针自身抛异常: " + ex);
                }
            })
            .SetupWithoutStarting();

        return WidgetsVerdict(problems, "A:--smoke / setup-without-starting(无 lifetime 通道)");
    }

    /// <summary>
    /// SMOKE_PROBE=widgets-lifetime 的部件层接线实测探针(通道 B:SetupWithLifetime)。
    ///
    /// 与生产启动同一条路:SetupWithLifetime 会装上 classic desktop lifetime 并让应用完成初始化 ——
    /// App.axaml.cs 里 lifetime 判断之外的 EnsureRegistered() 与判断之内的 V1Bridge 注入都会跑,
    /// 主窗口也由 App 自己建(desktop.MainWindow),所以这里量到的是**真宿主启动后的部件层**。
    /// 平台仍是 headless(无头平台 + Skia):不出现真窗口、不抢前台焦点。
    /// </summary>
    private static int WidgetsProbeWithLifetime(AppBuilder builder)
    {
        var problems = new System.Collections.Generic.List<string>();

        Console.WriteLine("=== WIDGETS 通道 B:SetupWithLifetime(与生产启动同路:有 classic desktop lifetime)===");
        try
        {
            var lifetime = new Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime();
            builder
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithLifetime(lifetime);

            Motion.Enabled = false;
            TipCL.Core.Launcher.Config.CONFIG.Set("ui_dark", Environment.GetEnvironmentVariable("SMOKE_DARK") == "1");

            var window = lifetime.MainWindow as Shell.MainWindow;
            var pageId = Environment.GetEnvironmentVariable("SMOKE_PAGE");
            if (string.IsNullOrEmpty(pageId)) pageId = "launch";
            if (window != null)
            {
                window.Show();
                window.Navigate(pageId);
            }
            RenderPass();
            Settle(DefaultSettleMs);
            RenderPass();

            WidgetsReport(problems, window, pageId);
            if (window != null) SaveWidgetsFrame(window);
            window?.Close();
        }
        catch (Exception ex)
        {
            problems.Add("探针自身抛异常: " + ex);
        }

        return WidgetsVerdict(problems, "B:setup-with-lifetime(生产启动通道)");
    }

    /// <summary>
    /// 两条通道共用的实测体:注册表状态/工厂数 + 五类内置部件的 id 与中文标题 + 文档里每个部件的定义
    /// 与其生效尺寸 + 每个 custom:&lt;id&gt; 真造一次内容 + 屏幕可视树里核对标记文本与占位对照。
    /// </summary>
    private static void WidgetsReport(System.Collections.Generic.List<string> problems, Shell.MainWindow? window, string pageId)
    {
        Console.WriteLine($"WIDGETS TIPCL_HOME={TipCL.Core.Launcher.Utils.ResolveRoot()}");
        Console.WriteLine($"WIDGETS Store.FilePath={TipCL.Core.Launcher.Layout.LayoutStoreV2.FilePath}");
        Console.WriteLine($"WIDGETS Store.LastLoadSource={TipCL.Core.Launcher.Layout.LayoutStoreV2.LastLoadSource}");
        var lifetime = Avalonia.Application.Current?.ApplicationLifetime;
        var cfgLang = TipCL.Core.Launcher.Config.CONFIG.Get("language", "");
        Console.WriteLine($"WIDGETS App={Avalonia.Application.Current?.GetType().FullName} Lifetime={(lifetime == null ? "(null)" : lifetime.GetType().Name)} I18n.CurrentLang={TipCL.Core.Launcher.I18nCore.CurrentLang} Config.language=[{cfgLang}]");

        bool registered = TipCL.App.Pages.Layout.WidgetRegistry.Registered;
        int factories = TipCL.App.Pages.Layout.LayoutContentRegistry.Count;
        Console.WriteLine($"WIDGETS Registry.Registered={registered} ContentFactories={factories}");
        if (!registered)
        {
            Console.WriteLine("WIDGETS NOTE: 本进程里 WidgetRegistry 没插电 —— App.OnFrameworkInitializationCompleted() 一次都没被调用(Avalonia 的 SetupWithoutStarting 只装平台服务、不初始化应用;只有 SetupWithLifetime 那条路才会走进去)。");
            Console.WriteLine("WIDGETS NOTE: 因此本通道下 custom:<id> 一律造不出内容,Build 类断言不参与判定;部件真进得来的正面证据见通道 B。");
        }
        else if (factories < 1)
        {
            problems.Add("WidgetRegistry 注册了但 LayoutContentRegistry 里一个工厂都没有");
        }

        // 「选部件」列表该有的东西:五类内置部件的 id + 中文标题(Windows 由设置页列出来)。与注册表插没插电无关。
        Console.WriteLine($"WIDGETS SafeTypes={TipCL.App.Pages.Layout.WidgetRegistry.SafeTypes.Length}");
        foreach (var t in TipCL.App.Pages.Layout.WidgetRegistry.SafeTypes)
        {
            var label = TipCL.App.Pages.Layout.WidgetUI.TypeLabel(t);
            Console.WriteLine($"WIDGET-TYPE type={t} label={label}");
            if (string.IsNullOrEmpty(label) || label == t) problems.Add($"类型 {t} 没有中文标题");
        }

        var doc = TipCL.Core.Launcher.Layout.LayoutStoreV2.Load();
        var widgets = TipCL.App.Pages.Layout.WidgetRegistry.AllWidgets();
        Console.WriteLine($"WIDGETS WidgetDefs={widgets.Count}");
        foreach (var w in widgets)
        {
            var settings = (w.Settings == null) ? "" : string.Join(",", w.Settings.Select(kv => kv.Key + "=" + kv.Value));
            Console.WriteLine($"WIDGET id={w.Id} type={w.Type} title={w.Title} settings={{{settings}}}");
        }

        var marker = Environment.GetEnvironmentVariable("SMOKE_MARKER") ?? "";
        var ghostId = Environment.GetEnvironmentVariable("SMOKE_GHOST");
        if (string.IsNullOrEmpty(ghostId)) ghostId = "custom:r32lint-ghost";
        var ph = TipCL.Core.Launcher.I18nCore.Tr("自建区块不可用"); // 占位标题按当前语言取(en 语言下是 Custom block unavailable)

        if (!registered)
        {
            Console.WriteLine("WIDGET-BUILD 跳过(注册表没插电,见上面的 NOTE);本通道只作为「没有 lifetime 时部件层拿不到内容」的边界记录");
        }
        else
        {
            if (widgets.Count == 0) problems.Add("布局文档里一个部件定义都没有(种子没读进来?)");
            foreach (var w in widgets)
            {
                var blockId = "custom:" + w.Id;
                var built = TipCL.App.Pages.Layout.LayoutContentRegistry.Build(blockId, pageId);
                var kind = (built == null) ? "null" : built.GetType().Name;
                var texts = TextsOf(built);
                Console.WriteLine($"WIDGET-BUILD block={blockId} hasProvider={TipCL.App.Pages.Layout.LayoutContentRegistry.HasProvider(blockId)} built={kind} size={EffectiveSize(doc, blockId)} texts=[{texts}]");
                if (built == null) problems.Add($"{blockId} 造不出内容");
                else if (texts.Contains(ph)) problems.Add($"{blockId} 落了占位,部件没进来");
            }

            // 对照组:文档里没定义的 custom:<ghost> 必须只拿占位(证明上面那条不是「什么都占位」)
            var ghostBuilt = TipCL.App.Pages.Layout.LayoutContentRegistry.Build(ghostId, pageId);
            var ghostTexts = TextsOf(ghostBuilt);
            Console.WriteLine($"WIDGET-BUILD block={ghostId} defined=False hasProvider={TipCL.App.Pages.Layout.LayoutContentRegistry.HasProvider(ghostId)} built={(ghostBuilt == null ? "null" : ghostBuilt.GetType().Name)} size={EffectiveSize(doc, ghostId)} texts=[{ghostTexts}]");
            if (ghostBuilt == null || !ghostTexts.Contains(ph))
                problems.Add($"{ghostId} 没拿到「{ph}」占位 —— 对照组失效");
        }

        // 屏上实测:种子便签的标记文本必须出现在这一页的可视树里(部件真的画进来了)
        if (window == null)
        {
            Console.WriteLine("WIDGETS 没有窗口(App 没建 / 没给 MainWindow),跳过屏上核对");
            return;
        }
        var blocks = window.GetVisualDescendants().OfType<TextBlock>().ToList();
        if (marker.Length > 0)
        {
            var hits = blocks.Count(t => (t.Text ?? "").Contains(marker, StringComparison.Ordinal));
            var ghostHits = blocks.Count(t => (t.Text ?? "").Contains(ph, StringComparison.Ordinal));
            Console.WriteLine($"WIDGETS VisualTextBlocks={blocks.Count} MarkerHits={hits} GhostPlaceholderHits={ghostHits} page={window.CurrentPageId}");
            foreach (var t in blocks.Where(t => (t.Text ?? "").Contains(marker, StringComparison.Ordinal)))
                Console.WriteLine($"WIDGETS 屏上命中: \"{t.Text}\" Bounds={t.Bounds.Width:F0}x{t.Bounds.Height:F0}");
            if (hits == 0 && registered) problems.Add("可视树里找不到种子便签的标记文本 —— 部件没渲染进页面");
            else if (hits == 0) Console.WriteLine("WIDGETS NOTE: marker 没上屏 —— 本通道注册表没插电,属预期");
            else if (ghostHits == 0) problems.Add("对照组的「自建区块不可用」没上屏 —— 未定义块的占位通路失效");
        }
        else
        {
            Console.WriteLine($"WIDGETS 未给 SMOKE_MARKER,跳过屏上文本核对(当前页 {window.CurrentPageId},可视树 TextBlock {blocks.Count} 个)");
        }
    }

    /// <summary>
    /// 给部件探针存一张屏上截图(CWD 下的 SMOKE_PNG,缺省 smoke-widgets.png)。
    /// 与主冒烟一致:捕获前推一次渲染 pass,并先弃一帧(R8:第一张常是尚未重合成的上一帧)。
    /// </summary>
    private static void SaveWidgetsFrame(Window window)
    {
        var png = Environment.GetEnvironmentVariable("SMOKE_PNG");
        if (string.IsNullOrEmpty(png)) png = "smoke-widgets.png";
        try
        {
            RenderPass();
            DiscardFirstFrame(window);
            using var frame = window.CaptureRenderedFrame();
            if (frame is null)
            {
                Console.Error.WriteLine("WIDGETS: 未能捕获渲染帧");
                return;
            }
            var full = Path.GetFullPath(png);
            using var stream = File.Create(full);
            frame.Save(stream);
            Console.WriteLine($"WIDGETS 截图={full} bytes={new FileInfo(full).Length}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WIDGETS: 截图失败 {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>两条通道共用的判定尾巴:0 = 本通道该成立的都成立;7 = 有任一条不成立。</summary>
    private static int WidgetsVerdict(System.Collections.Generic.List<string> problems, string mode)
    {
        if (problems.Count == 0)
        {
            Console.WriteLine($"WIDGETS OK mode={mode}");
            return 0;
        }
        foreach (var p in problems) Console.Error.WriteLine("WIDGETS FAIL: " + p);
        return 7;
    }

    private static string TextsOf(object? content)
    {
        if (content is not Control root) return "-";
        try
        {
            var texts = root.GetLogicalDescendants().OfType<TextBlock>()
                .Select(t => t.Text ?? "").Where(t => t.Length > 0).ToList();
            return string.Join(" | ", texts);
        }
        catch (Exception)
        {
            return "-";
        }
    }

    /// <summary>该块在文档各页面里的生效尺寸(没写进任何页面时说明用的是 BlockLayout 默认 1.0 x 1.0)。</summary>
    private static string EffectiveSize(TipCL.Core.Launcher.Layout.LayoutV2? doc, string blockId)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (doc?.Layout?.Pages != null)
        {
            foreach (var kv in doc.Layout.Pages)
            {
                if (kv.Value?.Blocks == null) continue;
                foreach (var b in kv.Value.Blocks)
                {
                    if (b != null && string.Equals(b.Id, blockId, StringComparison.Ordinal))
                        parts.Add($"{kv.Key}:{b.W:F2}x{b.H:F2}" + (b.Hidden ? "(hidden)" : ""));
                }
            }
        }
        if (parts.Count == 0) parts.Add("未入页面,默认 1.00x1.00");
        return string.Join(",", parts);
    }

    /// <summary>探针里的同形控件组:裸 TextBox / 裸 ComboBox / 裸 Slider / 显式主题 Button / 裸 TextBlock。</summary>
    private static readonly string[] ProbeLabels =
        { "裸 TextBox", "裸 ComboBox", "裸 Slider", "显式主题 Button", "裸 TextBlock" };

    private static (StackPanel Panel, Control[] Kids) BuildProbeGroup()
    {
        var input = TipCL.App.Ui.Ui.Input("854", "854", 74);
        var combo = TipCL.App.Ui.Ui.Combo(new[] { "自动", "17" }, "自动");
        var slider = TipCL.App.Ui.Ui.Sld(0, 100, 50);
        var btn = TipCL.App.Ui.Ui.Btn("x", TipCL.App.Ui.BtnKind.Soft);
        var txt = new TextBlock { Text = "对照组文本" };
        var panel = TipCL.App.Ui.Ui.V(6, input, combo, slider, btn, txt);
        return (panel, new Control[] { input, combo, slider, btn, txt });
    }

    /// <summary>照修复前的 MakePopup 造 Popup;<paramref name="attach"/> 决定要不要挂进页面树。</summary>
    private static Popup NewProbePopup(Panel pageRoot, Control target, Control content, bool attach)
    {
        var shell = new Border { Width = 360, Padding = new Thickness(12), Child = content };
        shell.Dyn(Border.BackgroundProperty, "B.Paper");
        shell.Dyn(Border.BorderBrushProperty, "B.Line");
        var p = new Popup
        {
            PlacementTarget = target,
            Placement = PlacementMode.Bottom,
            IsLightDismissEnabled = true,
            Child = shell,
        };
        if (attach)
            pageRoot.Children.Add(p);
        return p;
    }

    /// <summary>按文案在**逻辑树**里找按钮(不依赖模板/布局,模板坏掉时也找得到)。</summary>
    private static Button? ButtonByTextLogical(Control root, string text)
        => root.GetLogicalDescendants().OfType<Button>()
             .FirstOrDefault(b => (b.Content as string) == text
                               || (b.Content as TextBlock)?.Text == text);

    private static T? FirstByType<T>(Control root, Func<T, bool> predicate) where T : Control
    {
        if (root is T self && predicate(self)) return self;
        return root.GetLogicalDescendants().OfType<T>().FirstOrDefault(predicate);
    }

    /// <summary>打一行证据并返回该控件是否「有模板 + 有高度 + 可见」。</summary>
    private static bool PrintRow(string group, string label, Control c)
    {
        bool templated = c is TemplatedControl;
        var template = (c as TemplatedControl)?.Template;
        // 隐式 ControlTheme 的查找用的是 TryFindResource(它连 IStyleHost.Styles 一起查,
        // FluentTheme 的隐式主题就在那里):TryGetResource 只查资源字典,在这里永远是空。
        bool lookup = c.TryFindResource(c.GetType(), out var found);
        bool ok = c.Bounds.Height > 0 && c.IsEffectivelyVisible && (!templated || template != null);
        Console.WriteLine(
            $"POPUP-THEME {group} {label,-20} {c.GetType().Name,-12} "
            + $"{(c.Theme != null ? "显式" : "隐式")} "
            + $"{(templated ? (template != null ? "有" : "无") : "  -")}   "
            + $"{(lookup ? "中" : "无")}          "
            + $"{c.Bounds.Width,4:F0}x{c.Bounds.Height,-4:F0}   {c.DesiredSize.Width,4:F0}x{c.DesiredSize.Height,-4:F0}   "
            + $"{c.IsVisible}/{(c.IsEffectivelyVisible ? "1" : "0")}       "
            + $"{(c.GetVisualRoot() != null ? "有" : "无")}      "
            + $"{(ok ? "OK  " : "FAIL")}"
            + (lookup && found != null ? $" [隐式主题={found.GetType().Name}]" : ""));
        return ok;
    }

    private static int InstallProbe(string version, string instance)
    {
        try
        {
            InProcessApi.Instance.BridgeEvent += ev =>
            {
                if (ev.Event is "progress" or "log" or "finished" or "failed")
                    Console.WriteLine($"INSTALL {ev.Event}: {ev.Message}{ev.Text} ({ev.Current}/{ev.Total})");
            };
            InProcessApi.Instance.CallAsync<object?>("create_instance", new { name = instance }).GetAwaiter().GetResult();
            InProcessApi.Instance.CallAsync<object?>("install_game", new
            {
                version, loader = "", loader_version = (string?)null, instance,
            }).GetAwaiter().GetResult();
            Console.WriteLine("INSTALL OK");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("INSTALL FAIL " + ex.GetType().FullName + ": " + ex.Message);
            for (var e = ex.InnerException; e != null; e = e.InnerException)
                Console.WriteLine("  inner: " + e.GetType().Name + ": " + e.Message);
            Console.WriteLine(ex.StackTrace);
            return 3;
        }
    }

    private static int NetProbe(string url)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TipCL/" + TipCL.Core.Launcher.AppInfo.APP_VERSION);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var resp = http.GetAsync(url).GetAwaiter().GetResult();
            var body = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            Console.WriteLine($"NETPROBE ok status={(int)resp.StatusCode} bytes={body.Length} ms={sw.ElapsedMilliseconds}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"NETPROBE fail {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null) Console.WriteLine("NETPROBE inner: " + ex.InnerException.Message);
            return 2;
        }
    }
}
