using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TipCL;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C2b(外壳)期望值的**权威副本**:与 C1a/C1c 同一套做法 —— 期望值来自
/// Windows 源(<c>MainWindow.xaml.cs</c> / <c>NavModel.cs</c> / <c>NavConfig.cs</c> / <c>PageBase.cs</c>)
/// 与任务书 / GOAL 的窗口行,在测试里各留一份,源码改了而期望没跟着改会立刻红。
/// 每条都注明来源:【Windows 源】/【任务书】/【本机实测】。
/// </summary>
internal static class C2bExpected
{
    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:106-118</c> 的 <c>NavDefs</c>(页面 id / 标题 / 图标码位,顺序一字不改)。</summary>
    internal static readonly (string Id, string TitleZh, string Glyph)[] NavDefs =
    {
        ("launch", "启动", "\ue7c1"),
        ("library", "实例库", "\ue80a"),
        ("instance", "版本管理", "\ue80a"),
        ("account", "账号", "\ue77b"),
        ("ai", "AI 助手", "\ue99a"),
        ("multiplayer", "联机", "\ue701"),
        ("settings", "设置", "\ue713"),
        ("tasks", "下载任务", "\ue9d5"),
        ("download", "下载", "\ue896"),
        ("plugins", "插件", "\uea86"),
    };

    /// <summary>【Windows 源】<c>NavModel.TopKeys</c>。</summary>
    internal static readonly string[] TopKeys = { "launch", "download", "ai", "more", "tasks" };

    /// <summary>【Windows 源】<c>NavModel.SectionIds</c>。</summary>
    internal static readonly string[] SectionIds = { "download", "more" };

    /// <summary>【Windows 源】<c>NavModel.AllSubKeys</c>(= 两个分区默认成员并集)。</summary>
    internal static readonly string[] AllSubKeys = { "instance", "account", "multiplayer", "settings", "plugins" };

    /// <summary>【Windows 源】<c>NavModel.BottomKeys</c>。</summary>
    internal static readonly string[] BottomKeys = { "ai", "more", "settings", "tasks" };

    /// <summary>【Windows 源】<c>NavModel.DefaultNavOrder</c>。</summary>
    internal static readonly string[] DefaultNavOrder = { "launch", "download", "instance", "ai", "more", "settings", "tasks" };

    /// <summary>【Windows 源】<c>NavModel.DefaultNavPinned</c>。</summary>
    internal static readonly string[] DefaultNavPinned = { "instance", "settings" };

    /// <summary>【Windows 源】<c>NavModel.NavDefaultsVersion</c>。</summary>
    internal const string NavDefaultsVersion = "2026.09-ai-visible";

    /// <summary>【Windows 源】<c>NavModel.GroupedNav</c>(6 组,标题 + 组内键)。</summary>
    internal static readonly (string Title, string[] Keys)[] GroupedNav =
    {
        ("启动", new[] { "launch" }),
        ("库", new[] { "instance", "download", "multiplayer" }),
        ("社区", new[] { "account" }),
        ("AI 助手", new[] { "ai" }),
        ("工具", new[] { "tasks", "more" }),
        ("设置", new[] { "settings" }),
    };

    /// <summary>【Windows 源】<c>NavModel.PageForNavKey</c>(导航键 → 页面 id;分区键不在里面)。</summary>
    internal static readonly (string Key, string PageId)[] PageForNavKey =
    {
        ("launch", "launch"),
        ("ai", "ai"),
        ("tasks", "tasks"),
        ("instance", "instance"),
        ("account", "account"),
        ("multiplayer", "multiplayer"),
        ("settings", "settings"),
        ("plugins", "plugins"),
    };

    /// <summary>
    /// 【Windows 源 + 本机实测】默认配置(分组式、Groups=null)下 <c>SidebarSequence</c> / 可见顺序:
    /// launch → instance → multiplayer → account → ai → tasks → plugins → settings。
    /// 推导:GroupedNavItems 的条目键 = launch, instance, download, multiplayer, account, ai, tasks, more, settings;
    /// 其中 download 分区展开后为空、more 分区展开后只剩 plugins(其余已被置顶占掉);
    /// 于是拼出上面这条顺序(与 Windows <c>VisibleFlatOrder</c> 的算法一致,本机用 mac 的 NavModel 实测同一结果)。
    /// </summary>
    internal static readonly string[] DefaultVisiblePages = { "launch", "instance", "multiplayer", "account", "ai", "tasks", "plugins", "settings" };

    /// <summary>【Windows 源 + 本机实测】默认配置下的侧栏行表("H:" 分组头 / "S" 撑条 / "I:" 条目)。</summary>
    internal static readonly string[] DefaultGroupedRows =
    {
        "H:启动", "I:launch",
        "H:库", "I:instance", "I:download", "I:multiplayer",
        "H:社区", "I:account",
        "H:AI 助手", "I:ai",
        "H:工具", "I:tasks", "I:more",
        "H:设置", "I:settings",
    };

    /// <summary>【任务书】窗口与标题栏尺寸。</summary>
    internal const double WindowWidth = 900.0;

    internal const double WindowHeight = 550.0;

    internal const double WindowMinWidth = 720.0;

    internal const double WindowMinHeight = 480.0;

    internal const double TitleBarHeight = 46.0;

    internal const double TitleBarLeftInset = 78.0;

    /// <summary>【Windows 源】<c>MainWindow.SideMinWidth</c> / <c>SideMaxWidth</c> / <c>SideDefaultWidth</c>。</summary>
    internal const double SideMinWidth = 140.0;

    internal const double SideMaxWidth = 320.0;

    internal const double SideDefaultWidth = 196.0;
}

/// <summary>
/// 记录生命周期调用的假页面(C3 之前没有任何真页面,宿主语义只能靠它验)。
/// 只记数、不画东西,<c>LoadAsync</c> 立刻完成。
/// </summary>
internal sealed class ProbePage : PageBase
{
    /// <summary>生命周期调用流水(每次调用追加一项,用来验"每次切页调一次")。</summary>
    internal List<string> Log { get; } = new List<string>();

    /// <summary>非生命周期状态:用来验"切走再切回来还是同一个实例、状态还在"。</summary>
    internal int Scratch { get; set; }

    internal int Loads { get; private set; }

    internal int Shown { get; private set; }

    internal int Hidden { get; private set; }

    internal int Refreshes { get; private set; }

    public override string Title => "探针页";

    protected override Task LoadAsync()
    {
        Loads++;
        Log.Add("Load");
        return Task.CompletedTask;
    }

    public override Task RefreshAsync()
    {
        Refreshes++;
        Log.Add("Refresh");
        return base.RefreshAsync();
    }

    public override void OnShown()
    {
        Shown++;
        Log.Add("Show");
    }

    public override void OnHidden()
    {
        Hidden++;
        Log.Add("Hide");
    }
}

/// <summary>
/// 外壳夹具:造一个 <see cref="MainWindow"/>、挂进 headless 窗口,并在 <see cref="Dispose"/> 里
/// **把外壳动过的全局静态一律还原**(D-69 的纪律:全局改动必须在 finally/Dispose 里还回去)。
///
/// 必须在 <c>AvaloniaSession.Run(...)</c> 的用例体里使用:会话线程上一次只跑一个用例体,
/// 所以这些全局静态的临时改动不会串到别的用例里。
/// </summary>
internal sealed class ShellFixture : IDisposable
{
    private readonly GlobalSnapshot _snapshot;

    private IDisposable? _smokeScope;

    private bool _closed;

    private ShellFixture(MainWindow window, GlobalSnapshot snapshot)
    {
        Window = window;
        _snapshot = snapshot;
    }

    internal MainWindow Window { get; }

    /// <summary>这个夹具还原的全局静态(C2b 的外壳会写这些,全部在这张表里)。</summary>
    internal sealed class GlobalSnapshot
    {
        private readonly Window? _window;

        private readonly Action<string, string, ToastKind>? _toast;

        private readonly Grid? _dlgHost;

        private readonly Control? _blur;

        private readonly bool _dlgSmoke;

        private readonly Action<Control, Action>? _autoAnswer;

        private readonly Action<string>? _autoCancel;

        private readonly bool _motionEnabled;

        private readonly double _motionScale;

        private readonly ThemeVariant? _variant;

        private readonly IReadOnlyDictionary<string, Func<PageBase>> _registry;

        private GlobalSnapshot()
        {
            _window = AppServices.Window;
            _toast = AppServices.ToastSink;
            _dlgHost = Dlg.Host;
            _blur = Dlg.BlurTarget;
            _dlgSmoke = Dlg.SmokeActive;
            _autoAnswer = Dlg.SmokeAutoAnswer;
            _autoCancel = Dlg.SmokeAutoCancelSystemDialog;
            _motionEnabled = Motion.UserEnabled;
            _motionScale = Motion.Scale;
            _variant = Application.Current!.RequestedThemeVariant;
            _registry = PageRegistry.Snapshot();
        }

        internal static GlobalSnapshot Capture()
        {
            return new GlobalSnapshot();
        }

        internal void Restore()
        {
            PageRegistry.Restore(_registry);
            AppServices.Window = _window;
            AppServices.ToastSink = _toast;
            Dlg.Host = _dlgHost;
            Dlg.BlurTarget = _blur;
            Dlg.SmokeActive = _dlgSmoke;
            Dlg.SmokeAutoAnswer = _autoAnswer;
            Dlg.SmokeAutoCancelSystemDialog = _autoCancel;
            Motion.Enabled = _motionEnabled;
            Motion.Scale = _motionScale;
            Application.Current!.RequestedThemeVariant = _variant;
        }
    }

    /// <summary>造外壳并挂进窗口;<paramref name="prefs"/> 永远注入(测试绝不读用户数据目录)。</summary>
    /// <param name="prefs">注入的偏好(默认一份默认值)。</param>
    /// <param name="smoke">是否顺带打开离屏模式(<c>SmokeState.Activate</c>:Dlg 的三个挂点全接上)。</param>
    internal static ShellFixture Create(UiPrefs? prefs = null, bool smoke = false)
    {
        AvaloniaSession.Ensure();
        IDisposable? smokeScope = smoke ? Smoke.SmokeState.Activate() : null;
        MainWindow? window = null;
        ShellFixture? fixture = null;
        try
        {
            // 注意顺序:先造外壳再存全局快照是不行的 —— 外壳的构造**本身**就会写
            // AppServices / Dlg / Motion / Application.RequestedThemeVariant / 注册表,
            // 所以快照必须在外壳构造之前取(见 ShellFixture 的字段与 Dispose)。
            GlobalSnapshot snapshot = GlobalSnapshot.Capture();
            window = new MainWindow(prefs ?? new UiPrefs());
            fixture = new ShellFixture(window, snapshot);
            window.Show();
            UiHost.Flush();
        }
        catch
        {
            if (window is not null)
            {
                try
                {
                    window.Close();
                }
                catch
                {
                }
            }
            fixture?.Dispose();
            smokeScope?.Dispose();
            throw;
        }
        fixture._smokeScope = smokeScope;
        return fixture;
    }

    public void Dispose()
    {
        if (_closed)
        {
            return;
        }
        _closed = true;
        try
        {
            Window.Close();
        }
        catch (Exception)
        {
        }
        _snapshot.Restore();
        _smokeScope?.Dispose();
        UiHost.Flush();
    }
}

/// <summary>页面注册表的临时作用域:用完把注册表还原成进来时的样子(全局静态必须还原)。</summary>
internal sealed class RegistryScope : IDisposable
{
    private readonly IReadOnlyDictionary<string, Func<PageBase>> _snapshot;

    internal RegistryScope()
    {
        _snapshot = PageRegistry.Snapshot();
    }

    internal RegistryScope Register(string id, Func<PageBase> factory)
    {
        PageRegistry.Register(id, factory);
        return this;
    }

    public void Dispose()
    {
        PageRegistry.Restore(_snapshot);
    }
}

/// <summary>常用的断言小工具。</summary>
internal static class C2bAssert
{
    /// <summary>按 name 找可视子树里的控件。</summary>
    internal static T ByName<T>(Visual root, string name) where T : Control
    {
        return root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
    }

    /// <summary>把侧栏/导航行表压成 "H:标题" / "S" / "I:键" 这样的字符串,便于整表对拍。</summary>
    internal static List<string> DescribeRows(IEnumerable<NavEntry> entries)
    {
        return entries.Select(e => e.Kind switch
        {
            NavEntryKind.Header => "H:" + e.Label,
            NavEntryKind.Stretch => "S",
            _ => "I:" + e.Key,
        }).ToList();
    }
}
