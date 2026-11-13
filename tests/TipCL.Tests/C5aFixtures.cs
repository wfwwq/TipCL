using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using TipCL.Pages;
using TipCL.Services;
using TipCL.Themes;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C5a(设置页一簇)的全局静态作用域:进作用域时快照、<c>Dispose</c> 时还原(D-69 的纪律)。
///
/// 比 C4 的 <c>C4Scope</c> 多两样(C5a 新增的全局量):
/// <list type="bullet">
/// <item><see cref="PaletteOverride"/> —— 色板可写覆盖层(测试里改主题色,用完必须清干净,否则同进程
///   后面所有用例都会看到被改过的色板);</item>
/// <item><see cref="Motion.ClockOverride"/> —— 只有在用例自己显式打开动画时钟时才还回去(与 C3d 同口径)。</item>
/// </list>
/// 用例必须在 <c>AvaloniaSession.Run(...)</c> 的用例体里建这个作用域(会话线程上一次只跑一个用例体,D-79)。
/// </summary>
internal sealed class C5aScope : IDisposable
{
    private readonly BridgeClient? _client;

    private readonly Action<string, string, ToastKind>? _toastSink;

    private readonly Window? _window;

    private readonly Grid? _dlgHost;

    private readonly Control? _blur;

    private readonly bool _dlgSmoke;

    private readonly Action<Control, Action>? _autoAnswer;

    private readonly Action<string>? _autoCancel;

    private readonly bool _motionUserEnabled;

    private readonly double _motionScale;

    private readonly bool? _clockOverride;

    private readonly ThemeVariant? _variant;

    private readonly IReadOnlyDictionary<string, Func<PageBase>> _registry;

    private readonly Dictionary<string, object> _overrideEntries;

    private readonly string? _extrasDir;

    private readonly string _themeColor;

    private readonly string _background;

    private readonly bool _flyAnimation;

    private readonly int _flyDurationMs;

    private bool _disposed;

    private C5aScope()
    {
        _client = AppServices.Client;
        _toastSink = AppServices.ToastSink;
        _window = AppServices.Window;
        _dlgHost = Dlg.Host;
        _blur = Dlg.BlurTarget;
        _dlgSmoke = Dlg.SmokeActive;
        _autoAnswer = Dlg.SmokeAutoAnswer;
        _autoCancel = Dlg.SmokeAutoCancelSystemDialog;
        _motionUserEnabled = Motion.UserEnabled;
        _motionScale = Motion.Scale;
        _clockOverride = Motion.ClockOverride;
        _variant = Application.Current!.RequestedThemeVariant;
        _registry = PageRegistry.Snapshot();
        // SettingsPage 的**静态视觉状态**也是全局量:主题色 / 背景预设 / 外观附加项 / 落盘目录。
        // 不还原它们的话,"点色块/换背景预设"的用例会把状态漏给后面的用例(本包实测踩到:
        // 保存主题包那条读到了上一条留下的 ui_bg_preset="eye")。
        _extrasDir = SettingsPage.ExtrasDirOverride;
        _themeColor = SettingsPage.VisualThemeColor;
        _background = SettingsPage.Extras.Background;
        _flyAnimation = SettingsPage.Extras.FlyAnimation;
        _flyDurationMs = SettingsPage.Extras.FlyDurationMs;
        // 覆盖层逐条抄下来:不是"清空了事" —— 别的用例(或外壳)先设过什么,收尾时要原样还回去。
        _overrideEntries = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (string key in PaletteOverride.KeysNow)
        {
            if (PaletteOverride.TryGetNow(key, out object? value) && value is not null)
            {
                _overrideEntries[key] = value;
            }
        }
    }

    internal static C5aScope Capture()
    {
        return new C5aScope();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AppServices.Client = _client;
        AppServices.ToastSink = _toastSink;
        AppServices.Window = _window;
        Dlg.Host = _dlgHost;
        Dlg.BlurTarget = _blur;
        Dlg.SmokeActive = _dlgSmoke;
        Dlg.SmokeAutoAnswer = _autoAnswer;
        Dlg.SmokeAutoCancelSystemDialog = _autoCancel;
        Motion.Enabled = _motionUserEnabled;
        Motion.Scale = _motionScale;
        Motion.ClockOverride = _clockOverride;
        Application.Current!.RequestedThemeVariant = _variant;
        PageRegistry.Restore(_registry);
        // 设置页的静态视觉状态:先还落盘目录,再还把界面状态刷回去(顺序不能反 —— 刷回去那一步可能落盘)
        SettingsPage.ExtrasDirOverride = _extrasDir;
        SettingsPage.Extras.FlyAnimation = _flyAnimation;
        SettingsPage.Extras.FlyDurationMs = _flyDurationMs;
        SettingsPage.SyncPersistedThemeColor(_themeColor);
        SettingsPage.SyncPersistedBackground(_background);
        // 覆盖层:先全清(把挂上去的那一层摘下来),再把进作用域之前的那些条目写回去。
        PaletteOverride.Reset();
        foreach (KeyValuePair<string, object> pair in _overrideEntries)
        {
            PaletteOverride.Set(pair.Key, pair.Value);
        }
    }
}

/// <summary>
/// C5a 的公共小工具(找控件 / 点按钮 / 读文案 / 假后端参数)。全部转发给既有的
/// <see cref="C3bFiles"/> / <see cref="C3cFiles"/>,不重新发明一套(D-115:不模拟真实输入设备)。
/// </summary>
internal static class C5aFiles
{
    /// <summary>推几轮 Dispatcher(headless 下要手动推渲染计时器)。</summary>
    internal static void Settle(int rounds = 8)
    {
        C3bFiles.Settle(rounds);
    }

    internal static void Flush(int rounds = 1)
    {
        C3bFiles.Flush(rounds);
    }

    /// <summary>泵到条件成立(Stopwatch 计时 + 上限,理由见 D-120)。</summary>
    internal static void Pump(Func<bool> until, int timeoutMs = 9000)
    {
        C3bFiles.Pump(until, timeoutMs);
    }

    internal static void PumpTask(Task task, int timeoutMs = 9000)
    {
        C3bFiles.PumpTask(task, timeoutMs);
    }

    internal static IEnumerable<Control> Descendants(Control root)
    {
        return C3bFiles.Descendants(root);
    }

    internal static List<TextBlock> Texts(Control root)
    {
        return C3bFiles.Texts(root);
    }

    internal static List<TextBlock> TextsWith(Control root, string text)
    {
        return Texts(root).Where(t => string.Equals(t.Text, text, StringComparison.Ordinal)).ToList();
    }

    internal static bool HasText(Control root, string text)
    {
        return TextsWith(root, text).Count > 0;
    }

    internal static Button ByText(Control root, string text)
    {
        return C3bFiles.ByText(root, text);
    }

    internal static bool HasButton(Control root, string text)
    {
        return C3bFiles.HasButton(root, text);
    }

    internal static void Click(Button button)
    {
        C3bFiles.Click(button);
    }

    /// <summary>按类型找后代(找不到就抛,不静默返回 null)。</summary>
    internal static T Find<T>(Control root, string what) where T : Control
    {
        T? hit = Descendants(root).OfType<T>().FirstOrDefault();
        Assert.True(hit != null, "控件树里找不到 " + what + "(" + typeof(T).Name + ")");
        return hit!;
    }

    /// <summary>两个实色刷子的颜色必须相等(色板键对拍用;不是实色刷子直接失败)。</summary>
    internal static void AssertSameColour(Avalonia.Media.IBrush expected, Avalonia.Media.IBrush? actual, string what)
    {
        Avalonia.Media.Color want = C1cExpected.ColourOf(expected);
        Assert.True(actual != null, what + ":实际是 null");
        Assert.Equal(want, C1cExpected.ColourOf(actual!));
    }

    /// <summary>把一个页面挂进"浮层宿主那个窗口"(与 <c>C4Files.Mount</c> 同一做法,本包自持一份免得跨包耦合)。</summary>
    internal static T Mount<T>(C4LayerHost harness, T page) where T : Control
    {
        harness.Host.Children.Add(page);
        Flush(2);
        return page;
    }

    /// <summary>跑一次页面的首屏加载(把它当外壳在做事)。</summary>
    internal static void Load(PageBase page)
    {
        PumpTask(page.EnsureLoadedAsync());
    }

    /// <summary>
    /// 假后端第 <paramref name="method"/> 次(默认第 0 次)调用收到的参数里,按 <paramref name="path"/> 逐层取值,
    /// **解码后**返回(字符串给原文,其它 JSON 给 <c>GetRawText()</c>;取不到给空串)。
    ///
    /// 为什么要解码:<c>BridgeClient</c> 序列化时会按 STJ 的默认编码器把非 ASCII 转义成 <c>\uXXXX</c>,
    /// 直接 <c>Assert.Contains("甲")</c> 必然找不到(与 C4 同一处坑)。
    /// </summary>
    internal static string ParamAt(C3cFakeApi api, string method, params string[] path)
    {
        return ParamAt(api, method, 0, path);
    }

    /// <summary>
    /// 同上,但指定是第 <paramref name="index"/> 次调用。
    /// 为什么要这个重载:设置页的自动保存是 500ms 防抖(<see cref="TipCL.Pages.SettingsPage"/> 的 QueueSave),
    /// 机器忙的时候那条定时器可能在用例中途真的 Tick 一次 ⇒ 会有**额外的** <c>save_settings</c>;
    /// 断言"我刚点的那条路发出去的载荷"必须按**最后一次**看,而不是第 0 次(本包在 VSTest 通道踩到过)。
    /// </summary>
    internal static string ParamAt(C3cFakeApi api, string method, int index, params string[] path)
    {
        using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(api.ParamsOf(method, index));
        System.Text.Json.JsonElement cur = doc.RootElement;
        foreach (string key in path)
        {
            if (cur.ValueKind != System.Text.Json.JsonValueKind.Object || !cur.TryGetProperty(key, out System.Text.Json.JsonElement next))
            {
                return "";
            }
            cur = next;
        }
        return (cur.ValueKind == System.Text.Json.JsonValueKind.String) ? (cur.GetString() ?? "") : cur.GetRawText();
    }

    /// <summary>整份参数原文里**包含**某个片段(断言键在不在时用,比逐层取值省事)。</summary>
    internal static bool ParamHas(C3cFakeApi api, string method, string fragment)
    {
        return api.ParamsOf(method).Contains(fragment, StringComparison.Ordinal);
    }

    /// <summary>按左导航按钮的文案点它(内容 TextBlock 直接拿文案比,与 Windows 的 label 一字不差)。</summary>
    internal static ToggleButton NavButton(Control page, string label)
    {
        ToggleButton? hit = Descendants(page).OfType<ToggleButton>()
            .FirstOrDefault(b => b.Content is TextBlock t && string.Equals(t.Text, label, StringComparison.Ordinal));
        Assert.True(hit != null, "左导航里找不到「" + label + "」这一段");
        return hit!;
    }

    /// <summary>设置页的假后端:把这一页会碰到的调用**全部**路由好(按方法名路由,不按请求序号 —— D-103)。</summary>
    internal static C3cFakeApi SettingsApi(string? settingsJson = null)
    {
        return new C3cFakeApi()
            .Route("get_settings", settingsJson ?? DefaultSettings)
            .Route("get_instances", "[]")
            .Route("get_setting", "\"\"")
            .Route("allow_multi_instance", "false")
            .Route("available_languages", "{\"zh-CN\":\"简体中文\",\"en-US\":\"English\"}")
            .Route("get_language", "\"zh-CN\"")
            .Route("list_themes", "[]")
            .Route("background_history", "[]")
            .Route("can_undo_background", "false")
            .Route("ai_vision_status", "{\"mode\":\"auto\",\"text\":\"已就绪\"}");
    }

    /// <summary>一份"后端默认设置"(字段名与后端 <c>get_settings</c> 回包一致;用例按需覆写)。</summary>
    internal const string DefaultSettings =
        "{\"ui_dark\":false,\"download_threads\":8,\"download_segments\":4,\"default_memory_mb\":4096," +
        "\"default_resolution\":[854,480],\"download_limit_kbps\":0,\"download_source\":\"auto\"," +
        "\"community_source\":\"auto\",\"use_system_proxy\":true,\"default_isolation\":\"none\"," +
        "\"gc_preset\":\"auto\",\"launcher_visibility\":\"keep\",\"homepage_mode\":\"news\"," +
        "\"window_mode\":\"window\",\"custom_homepage\":\"\",\"default_jvm_args\":\"\",\"share_libraries\":false," +
        "\"share_assets\":false,\"auto_check_update\":true,\"ai_mode\":\"builtin\",\"ai_base_url\":\"\"," +
        "\"ai_model\":\"\",\"ai_context_window\":\"131072\",\"ai_fallback_model\":\"\",\"ai_confirm_writes\":true," +
        "\"game_dir\":\"/games\",\"ui_background\":\"\",\"ui_background_folder\":\"\",\"ui_background_shuffle\":false," +
        "\"ui_background_interval\":10,\"ui_background_blur\":0,\"ui_background_dim\":0," +
        "\"theme_color\":\"#2E9B6B\",\"ui_bg_preset\":\"default\"}";
}

/// <summary>
/// C5a 的分段探针页:给 <c>SettingsPage</c> 的注入缝(<c>AccountPageFactory</c> / <c>FeedbackPageFactory</c> ......)注入,
/// 证明"接上了才显示、而且真的是切到那一段才建"。不是产品代码(D-119 / D-134 的注入缝就是给这个用的)。
///
/// 命名纪律:探针属性**避开** Avalonia 基类已有成员(<c>Loaded</c> / <c>Shown</c> / <c>Title</c> 之类),
/// 一律带 <c>Now</c> 后缀 —— 僵尸踩过一次 <c>Loaded</c> 撞 <c>Control.Loaded</c> 事件(CS0079)把整棵树编译搞红。
/// </summary>
internal sealed class C5aProbePage : PageBase
{
    private readonly string _label;

    internal C5aProbePage(string label)
    {
        _label = label;
        Content = Ui.V(8.0, Ui.Txt("探针:" + label));
    }

    /// <summary>被建出来了几次(证明"切到才建"、且不会重复建)。</summary>
    internal static int BuiltNow { get; private set; }

    /// <summary>这个实例的加载跑过了吗。</summary>
    internal bool RanNow { get; private set; }

    /// <summary>这个实例的 OnShown 进来过吗。</summary>
    internal bool ShownNow { get; private set; }

    public override string Title => "探针:" + _label;

    internal static void ResetCounters()
    {
        BuiltNow = 0;
    }

    internal static C5aProbePage Create(string label)
    {
        BuiltNow++;
        return new C5aProbePage(label);
    }

    protected override Task LoadAsync()
    {
        RanNow = true;
        return Task.CompletedTask;
    }

    public override void OnShown()
    {
        ShownNow = true;
    }
}
