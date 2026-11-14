using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using TipCL.App.Pages;
using TipCL.App.Pages.Launch;
using TipCL.App.Pages.Download;

namespace TipCL.App.Shell;

/// <summary>
/// v2 主窗口外壳(对齐 Windows `MainWindow.xaml`):900×550(Min 720×480)、
/// 46px 自定义标题栏(Logo + 5 页签 启动/实例库/下载/插件/设置 + 右侧 AI 药丸钮/主题/账号钮/窗口钮),
/// 内容区全幅无侧边导航。AI 与账号由标题栏钮进入(对齐 Windows `AiBtn` / `AccBtn`);
/// Java / 关于 不是顶级页,是设置页内层导航项。
/// </summary>
public partial class MainWindow : Window
{
    private bool _isDark;

    private string _currentId = "";

    private readonly Dictionary<string, Control> _pages = new();

    public MainWindow()
    {
        InitializeComponent();
        TitleBarHost.TabClicked += (_, id) => Navigate(id);
        TitleBarHost.ThemeToggleClicked += (_, _) => ToggleTheme();
        TitleBarHost.AiClicked += (_, _) => Navigate("ai");            // Windows: AiBtn → AiPage
        TitleBarHost.AccountClicked += (_, _) => Navigate("account");  // Windows: AccBtn → AccountPage
        Pages.Library.LibraryPage.NavRequested += id => Navigate(id); // M2-B2:实例库页内跳转(双击版本 → 启动页)
        bool savedDark = false;
        try
        {
            savedDark = TipCL.Core.Py.Truthy(TipCL.Core.Launcher.Config.CONFIG.Get("ui_dark", TipCL.Core.Py.Box(false)));
        }
        catch { }
        SetDark(savedDark);
        Navigate("launch", instant: true);
        // 标题栏账号钮跟随当前账号(账号增删/切换由 ui_changed 事件驱动);
        // crash = 游戏异常退出,弹崩溃界面(M4-H P4)
        TipCL.App.Common.InProcessApi.Instance.BridgeEvent += ev =>
        {
            if (ev.Event == "ui_changed")
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = SyncAccountChipAsync());
            else if (ev.Event == "crash")
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = ShowCrashAsync(ev.TaskId));
            else if (ev.Event == "update_staged")
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = OfferRestartForUpdateAsync(ev.Text, ev.Message));
        };
        _ = SyncAccountChipAsync();
        HookPlugins();
        // 窗口级兜底:拖到没有自己拖放处理的位置也认(M4-H P7 统一入口);页面自己的处理已 Handled,不会重复
        TipCL.App.Common.FileDrop.Attach(this);
    }

    // ───────── 插件外壳接线(M4-H P6/P8) ─────────

    private bool _pluginsHooked;

    /// <summary>
    /// 构造时订阅一次:插件启停/装卸 → 进程内 PluginHost.Changed → PluginUi 快照 → 重建顶部页签
    /// 并让已打开的插件页面更新/失效(对齐 Windows MainWindow.Plugins.cs 的 OnPluginUiChanged)。
    /// </summary>
    private void HookPlugins()
    {
        if (_pluginsHooked) return;
        _pluginsHooked = true;
        TipCL.App.Common.Plugins.PluginRuntime.Changed += _ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshPluginUi);
        TipCL.App.Pages.Plugins.PluginUi.Changed += () =>
            Avalonia.Threading.Dispatcher.UIThread.Post(OnPluginUiChanged);
        RefreshPluginUi();
    }

    /// <summary>重读插件快照(磁盘上装了/删了什么,由 PluginRuntime.Changed 或页面主动触发)。</summary>
    public void RefreshPluginUi()
    {
        try
        {
            TipCL.App.Pages.Plugins.PluginUi.Refresh();
        }
        catch (Exception ex)
        {
            TipCL.App.Common.AppLog.Warn("插件界面快照刷新失败: " + ex.Message);
            return;
        }
        OnPluginUiChanged();
    }

    private void OnPluginUiChanged()
    {
        SyncPluginTabs();
        foreach (var kv in _pages.ToList())
        {
            if (kv.Value is not TipCL.App.Pages.Plugins.PluginHostPage page) continue;
            var c = TipCL.App.Pages.Plugins.PluginUi.FindPage(kv.Key);
            if (c != null)
            {
                page.Update(c);
            }
            else
            {
                page.MarkGone();
                if (_currentId != kv.Key) _pages.Remove(kv.Key);
            }
        }
    }

    /// <summary>把 group:"top" 的插件页面追加成顶部页签(设置页签之后,按 Order 稳定排序)。</summary>
    private void SyncPluginTabs()
    {
        var tabs = TipCL.App.Pages.Plugins.PluginUi.OfKind("page")
            .Where(c => c.Group == "top")
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Plugin, StringComparer.Ordinal)
            .Select(c => (c.PageId, c.Label.Length > 0 ? c.Label : c.Id))
            .ToList();
        TitleBarHost.SetPluginTabs(tabs);
        TitleBarHost.SetActiveTab(_currentId);
    }

    /// <summary>把「当前账号」同步到标题栏账号钮(无账号时回落「未登录」)。</summary>
    private async Task SyncAccountChipAsync()
    {
        try
        {
            var rows = await TipCL.App.Common.InProcessApi.Instance.TryCallAsync<System.Collections.Generic.List<object>>("get_account_rows");
            string name = (rows ?? new System.Collections.Generic.List<object>())
                .Select(r => TipCL.Core.Py.D(r))
                .Where(d => TipCL.Core.Py.Truthy(d.Get("active")))
                .Select(d => TipCL.Core.Py.S(d.Get("name")))
                .FirstOrDefault() ?? "";
            TitleBarHost.SetAccount(name);
        }
        catch (Exception)
        {
            // 账号读取失败不影响外壳
        }
    }

    /// <summary>当前页 id。</summary>
    public string CurrentPageId => _currentId;

    /// <summary>
    /// 崩溃事件(M4-H P4):游戏异常退出时 Core 抛 GameCrashError,InProcessApi 记一份报告并发
    /// `crash` 事件;这里弹 CrashUi,用户点「重新启动」就把那个版本重跑一次。
    /// </summary>
    private async Task ShowCrashAsync(string taskId)
    {
        try
        {
            var report = await TipCL.App.Common.InProcessApi.Instance.CallAsync<TipCL.Core.PyDict>("get_crash", new { task_id = taskId });
            if (report == null || report.Count == 0) return;
            report["task_id"] = taskId;
            bool again = await TipCL.App.Pages.Feedback.CrashUi.ShowAsync(report, gameError: true);
            if (!again) return;
            string instance = TipCL.Core.Py.S(report.Get("instance"));
            string version = TipCL.Core.Py.S(report.Get("version"));
            if (version.Length == 0) return;
            await TipCL.App.Common.InProcessApi.Instance.CallAsync<string>("launch_game", new
            {
                instance,
                version,
            });
            Navigate("tasks");
        }
        catch (Exception ex)
        {
            TipCL.App.Common.AppLog.Warn("崩溃界面没能弹出: " + ex.Message);
        }
    }

    /// <summary>等主窗口可见的上限(ms)。生命周期在 OnFrameworkInitializationCompleted 之后才 Show,
    /// 正常几毫秒内就可见;窗口一直不显示时也不能把启动提示永久挂住。</summary>
    private const int VisibleWaitMs = 15000;

    /// <summary>首启向导 + 反馈同意门(M4-H P4,照 Windows 启动顺序:先向导,再同意门)。</summary>
    public async Task RunStartupPromptsAsync()
    {
        try
        {
            if (!await RunWhenVisibleAsync(this, VisibleWaitMs, RunPromptsAsync))
                TipCL.App.Common.AppLog.Warn("主窗口 " + VisibleWaitMs + "ms 内没有可见,启动提示已跳过");
        }
        catch (Exception ex)
        {
            TipCL.App.Common.AppLog.Warn("启动提示失败: " + ex.Message);
        }
    }

    private async Task RunPromptsAsync()
    {
        await TipCL.App.Pages.Feedback.FirstRunWizard.MaybeShowAsync();
        await TipCL.App.Common.FeedbackConsent.MaybeAskAsync(this);
        await AutoCheckUpdateAsync();
    }

    // ───────── 启动器自更新(R22,照 Windows MainWindow.AutoCheckUpdateAsync / OfferRestartForUpdateAsync) ─────────

    private static bool _autoUpdateChecked;

    /// <summary>启动后按「自动检查更新」设置**静默**检查一次,有新版才问一句(照参考版:等待 3 秒再查)。</summary>
    private async Task AutoCheckUpdateAsync()
    {
        if (_autoUpdateChecked) return;
        _autoUpdateChecked = true;
        try
        {
            if (!TipCL.Core.Py.Truthy(TipCL.Core.Launcher.Config.CONFIG.Get("auto_check_update", true))) return;
            await Task.Delay(3000);
            TipCL.Core.PyDict? info = await TipCL.App.Common.InProcessApi.Instance.TryCallAsync<TipCL.Core.PyDict>("check_update");
            if (info == null || !TipCL.Core.Py.Truthy(info["has_update"])) return;
            string body = ("TipCL " + TipCL.Core.Py.S(info["latest"]) + " 已发布。\n" + TipCL.Core.Py.S(info["notes"])).Trim();
            if (await TipCL.App.Common.Dlg.Confirm("发现新版本", body + "\n\n现在下载更新?下载完会提示你重启。", "下载更新", "以后再说"))
                await TipCL.App.Common.InProcessApi.Instance.TryCallAsync<string>("start_self_update");
        }
        catch (Exception ex)
        {
            // 启动时的静默检查失败不打扰用户,只记一条 WARN(离线是常态)
            TipCL.App.Common.AppLog.Warn("自动检查更新失败: " + ex.Message);
        }
    }

    /// <summary>
    /// 更新包下载好了(外壳订阅的 update_staged 事件)→ 问一句什么时候重启。
    ///
    /// **与参考版的一处有意差别**:Windows 是"先暂存、退出时再用 PowerShell 换 exe"(因为运行中的 exe 换不掉);
    /// Linux 换运行中的二进制是安全的(进程继续用旧 inode / AppImage 用的是解包挂载),所以这里**无论选哪个都当场替换**,
    /// 只是"立即重启"会顺手把新版拉起来。
    /// </summary>
    private async Task OfferRestartForUpdateAsync(string package, string version)
    {
        if (string.IsNullOrWhiteSpace(package)) return;
        bool now = await TipCL.App.Common.Dlg.Confirm("更新已下载", "重启 TipCL 即可更新到 " + (version.Length > 0 ? version : "新版本") + "。", "立即重启", "稍后重启");
        try
        {
            TipCL.Core.PyDict? res = await TipCL.App.Common.InProcessApi.Instance.TryCallAsync<TipCL.Core.PyDict>("apply_update",
                null, new TipCL.Core.PyDict { ["package"] = package, ["restart"] = now });
            if (res == null) return;
            string mode = TipCL.Core.Py.S(res["mode"]);
            if (!TipCL.Core.Py.Truthy(res["ok"]))
            {
                // 目标不可写(.deb 装到 /opt 那种):把"包在哪、怎么装"如实告诉用户,不假装已更新
                await TipCL.App.Common.Dlg.Alert("需要手动安装", TipCL.Core.Py.S(res["message"]));
                return;
            }
            if (now)
            {
                // 新版已经拉起来了,这个进程功成身退
                Close(); // 主窗口关闭即退出(Avalonia 11 的 Application 没有 Shutdown)
            }
            else
            {
                TipCL.App.Common.ToastHost.Show("更新已就绪", mode == "appimage" ? "下次打开就是新版" : "重启启动器即可生效", TipCL.App.Common.ToastKind.Success);
            }
        }
        catch (Exception ex)
        {
            TipCL.App.Common.ToastHost.Show("更新失败", ex.Message, TipCL.App.Common.ToastKind.Error);
        }
    }

    /// <summary>
    /// M4-H R12:两个启动提示都等主窗口**真正可见**之后再跑。`App.OnFrameworkInitializationCompleted` 在
    /// `desktop.MainWindow = win` 之后立刻调本方法,此刻窗口还没 `Show()`,而 Avalonia 的
    /// `ShowDialog(owner)` 对不可见的 owner 直接抛 `Cannot show window with non-visible owner.`。
    /// 真机实测(X11,`first_run=true` 的全新 HOME,修复前产物 tipcl12)抛的是**第一个**提示
    /// (`FirstRunWizard.MaybeShowAsync` → 立刻弹面板),异常中断整个 `RunStartupPromptsAsync`,
    /// 于是排在后面的同意门(它自带 400ms 延迟,单独跑其实弹得出来)一次都轮不到 —— 用户看到的是
    /// 「首启向导和同意门都没出现,日志只有一条 WARN」。照 Windows 是窗口显示 + bridge 连接完成之后
    /// 才 await 这两个提示(`windos/.../MainWindow.xaml.cs:362-369`)。
    /// 返回 false = 等到上限窗口仍不可见(此时不跑提示,由 `RunStartupPromptsAsync` 记一条明确的 WARN)。
    /// </summary>
    internal static async Task<bool> RunWhenVisibleAsync(Window window, int timeoutMs, Func<Task> prompts)
    {
        if (!await WaitUntilVisibleAsync(window, timeoutMs)) return false;
        await prompts();
        return true;
    }

    /// <summary>
    /// 等窗口可见:已可见立刻返回;否则等 `Opened` 或 `IsVisible` 变真,最多 <paramref name="timeoutMs"/> 毫秒。
    /// 用窗口状态判断(不是固定 sleep);上限保证窗口永远不显示时不会永久挂起。
    /// </summary>
    internal static async Task<bool> WaitUntilVisibleAsync(Window window, int timeoutMs)
    {
        if (window.IsVisible) return true;
        // 不在 Opened 处理器里直接跑提示(那时还在 Show() 的调用栈上):让续接回到 Dispatcher 上执行。
        var visible = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnOpened(object? sender, EventArgs e) => visible.TrySetResult(true);
        void OnChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Avalonia.Visual.IsVisibleProperty) visible.TrySetResult(window.IsVisible);
        }
        window.Opened += OnOpened;
        window.PropertyChanged += OnChanged;
        try
        {
            var timeout = Task.Delay(timeoutMs);
            return await Task.WhenAny(visible.Task, timeout) != timeout && await visible.Task;
        }
        finally
        {
            window.Opened -= OnOpened;
            window.PropertyChanged -= OnChanged;
        }
    }

    /// <summary>当前页对象(冒烟脚本按 SMOKE_SEG 切下载页分段时要用)。</summary>
    public TipCL.App.Common.PageBase? CurrentPage => PageHost.Content as TipCL.App.Common.PageBase;

    /// <summary>
    /// 预选实例(与可选版本)后切到启动页(照 Windows LaunchPage.PreferInstance 的用法)。
    /// 页面可能是懒建的,所以先 Navigate 再设预选,保证 FillCardDataAsync 能读到。
    /// </summary>
    public void PreferLaunch(string instance, string version = "")
    {
        TipCL.App.Pages.Launch.LaunchPage.Prefer(instance, version);
        if (_currentId == "launch")
        {
            // 已经停在启动页:直接让页面重新拉一次数据
            (GetPage("launch") as TipCL.App.Common.PageBase)?.ShellAttach(null);
            return;
        }
        Navigate("launch");
    }

    /// <summary>是否深色主题。</summary>
    public bool IsDark => _isDark;

    /// <summary>切换到指定页。</summary>
    public void Navigate(string id, bool instant = false)
    {
        if (_currentId == id) return;
        var page = GetPage(id);
        if (page is null) return;
        if (PageHost.Content is TipCL.App.Common.PageBase prev) prev.ShellDetach();
        _currentId = id;
        PageHost.Content = page;
        (page as TipCL.App.Common.PageBase)?.ShellAttach(null);
        TitleBarHost.SetActiveTab(id);
    }

    /// <summary>
    /// 切换明暗主题(标题栏月牙钮)并写回 ui_dark:设置页加载时以配置为准,
    /// 不写回的话一打开设置页就被改回去,重启也记不住。
    /// </summary>
    public void ToggleTheme()
    {
        SetDark(!_isDark);
        _ = PushDarkAsync(_isDark);
    }

    private static async Task PushDarkAsync(bool dark)
    {
        try
        {
            await TipCL.App.Common.InProcessApi.Instance.CallAsync<object>("save_settings", new
            {
                data = new Dictionary<string, object> { ["ui_dark"] = dark },
            });
        }
        catch (Exception ex)
        {
            TipCL.App.Common.ToastHost.Show("深浅色没能保存", ex.Message, TipCL.App.Common.ToastKind.Warning);
        }
    }

    public void SetDark(bool dark)
    {
        _isDark = dark;
        if (Avalonia.Application.Current is { } app)
            app.RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        // Application 级挂载 = 代码侧取色的唯一真源:A2 的 Ui.ApplyTheme 负责把 Controls.axaml(全部
        // ControlTheme)与本主题 Palette 合并进 Application.Resources。Ui.Dyn / Res.Bind / Ui.Theme /
        // Ui.Res 都是 Application.Current 锚定的,**不挂这一层它们一律解析为 null**
        // (症状:启动按钮不变绿、绿点/下拉箭头不可见、卡片无底色)。
        TipCL.App.Ui.Ui.ApplyTheme(dark);
        var merged = Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(new ResourceInclude((Uri?)null) { Source = new Uri(dark
            ? "avares://TipCL.App/Themes/Palette.Dark.axaml"
            : "avares://TipCL.App/Themes/Palette.Light.axaml") });
        TitleBarHost.SyncThemeIndicator(dark);
    }

    // 页面工厂:键与 Windows `MainWindow` 的 NavDefs / 标题栏钮一致。
    // 尚未移植的页(ai=AI 助手、account=账号)用占位;Java / 关于 在设置页内层导航里,不在此列。
    private Control? GetPage(string id) => id switch
    {
        "launch" => _pages.GetOrAdd(id, () => new LaunchPage()),
        "library" => _pages.GetOrAdd(id, () => new TipCL.App.Pages.Library.LibraryPage()),
        // 「版本管理」= Windows NavDefs 的 "instance"(不在顶部 5 页签里,由启动页版本弹层「管理」/
        // 版本管理相关链接内跳进入)
        "instance" => _pages.GetOrAdd(id, () => new TipCL.App.Pages.Version.VersionManagePage()),
        "download" => _pages.GetOrAdd(id, () => new DownloadPage()),
        "plugins" => _pages.GetOrAdd(id, () => new Pages.Plugins.PluginsPage()),
        "settings" => _pages.GetOrAdd(id, () => new Pages.Settings.SettingsPage()),
        "multiplayer" => _pages.GetOrAdd(id, () => new MultiplayerPage()),
        "ai" => _pages.GetOrAdd(id, () => new TipCL.App.Pages.Ai.AiPage()),
        "account" => _pages.GetOrAdd(id, () => new AccountPage()),
        // 「下载任务」在 Windows 不是顶级页,由下载/安装/目录页内跳入(Navigate("tasks"))
        "tasks" => _pages.GetOrAdd(id, () => new TasksPage()),
        _ => PluginPage(id),
    };

    /// <summary>插件页面(plugin:&lt;plugin&gt;:&lt;id&gt;):由 PluginUi 快照里的贡献建,插件没了返回 null。</summary>
    private Control? PluginPage(string id)
    {
        if (!id.StartsWith("plugin:", StringComparison.Ordinal)) return null;
        var c = TipCL.App.Pages.Plugins.PluginUi.FindPage(id);
        if (c == null) return null;
        return _pages.GetOrAdd(id, () => new TipCL.App.Pages.Plugins.PluginHostPage(c));
    }
}

internal static class PageCacheExtensions
{
    public static T GetOrAdd<T>(this Dictionary<string, Control> dict, string key, Func<T> factory)
        where T : Control
    {
        if (!dict.TryGetValue(key, out var value))
        {
            value = factory();
            dict[key] = value;
        }
        return (T)value;
    }
}
