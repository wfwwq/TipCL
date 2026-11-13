using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using TipCL.Models;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C4:实例管理簇(ModsDialog / SavesDialog / LibraryPage / InstancePage / InstanceDetailPage)的期望值权威副本。
///
/// 每一条都注明来源:
/// <list type="bullet">
/// <item>【Windows 源】= 从 <c>D:\项目\tipcl\windos\tipcl\src\TipCL\Pages\*.cs</c> 逐行读出来的;</item>
/// <item>【任务书】= C4 任务书点名的要求;</item>
/// <item>【本机实测】= 在 mac 侧跑出来 / 从 Avalonia 程序集的元数据里核出来的。</item>
/// </list>
/// </summary>
internal static class C4Expected
{
    // ---------------------------------------------------------------- 注册

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:109</c> 的 <c>NavDef("library", ...)</c>。</summary>
    internal const string LibraryPageId = "library";

    /// <summary>【Windows 源】<c>NavModel.PageForNavKey["instance"]</c> 与 <c>NavDef("instance", ...)</c>。</summary>
    internal const string InstancePageId = "instance";

    // ---------------------------------------------------------------- LibraryPage

    /// <summary>【Windows 源】<c>LibraryPage.cs:79-88</c> 的 <c>Defs</c>:七个分组的键与标题(顺序即页面顺序)。</summary>
    internal static readonly (string Key, string Title)[] LibraryGroups =
    {
        ("fabric", "Fabric 版本"),
        ("forge", "Forge 版本"),
        ("quilt", "Quilt 版本"),
        ("neoforge", "NeoForge 版本"),
        ("optifine", "OptiFine 版本"),
        ("liteloader", "LiteLoader 版本"),
        ("vanilla", "原版版本"),
    };

    /// <summary>【Windows 源】<c>LibraryPage.cs:73-75</c>:加载器键与显示名的对应(下标一一对应)。</summary>
    internal static readonly (string Key, string Loader)[] LibraryLoaders =
    {
        ("fabric", "Fabric"),
        ("forge", "Forge"),
        ("quilt", "Quilt"),
        ("neoforge", "NeoForge"),
        ("optifine", "OptiFine"),
        ("liteloader", "LiteLoader"),
    };

    /// <summary>【Windows 源】<c>LibraryPage.cs:897-948</c> 的左栏「实例操作」菜单(不含分隔线)。</summary>
    internal static readonly string[] LibraryFolderMenuItems =
    {
        "打开文件夹",
        "设为默认实例",
        "选择 Java...",
        "管理模组",
        "存档管理...",
        "重命名...",
        "删除实例",
    };

    /// <summary>【Windows 源】<c>LibraryPage.cs:269-320</c> 的右键「自定义布局...」菜单(3 项,顺序如此)。</summary>
    internal static readonly string[] LibraryLayoutMenuItems = { "自定义布局...", "显示文件夹栏", "导出布局", "导入布局" };

    /// <summary>【Windows 源】<c>LibraryPage.cs:786-802</c> 的 <c>InferType</c> 判据(7 组输入 → 期望类型)。</summary>
    internal static readonly (string Id, string Type)[] InferTypeCases =
    {
        ("1.20.1", "release"),
        ("1.20.1-pre1", "snapshot"),
        ("1.20.1-rc1", "snapshot"),
        ("1.21.2", "release"),
        ("22w13a", "snapshot"),
        ("b1.7.3", "old_beta"),
        ("a1.2.6", "old_alpha"),
        ("rd-132211", "old_alpha"),
        ("c0.30_01c", "old_alpha"),
        ("inf-20100618", "old_alpha"),
    };

    // ---------------------------------------------------------------- InstancePage

    /// <summary>【Windows 源】<c>InstancePage.cs:396-455</c> 的卡片「更多」菜单(不含分隔线,顺序如此)。</summary>
    internal static readonly string[] InstanceMenuItems =
    {
        "打开文件夹",
        "设为默认实例",
        "选择 Java...",
        "挑着更新模组...",
        "后台检查模组更新",
        "导出为整合包",
        "重命名...",
        "删除实例",
    };

    // ---------------------------------------------------------------- InstanceDetailPage

    /// <summary>【Windows 源】<c>InstanceDetailPage.cs:17</c> 的五个分段(顺序即按钮顺序)。</summary>
    internal static readonly string[] DetailSegs = { "模组", "存档", "资源包", "光影", "设置" };

    // ---------------------------------------------------------------- 对话框(调用名 / 参数名)

    /// <summary>【Windows 源】ModsDialog / SavesDialog / GlobalModsDialog 用到的后端方法名(逐个抄)。</summary>
    internal static readonly string[] ModsDialogMethods =
    {
        "get_mods_targets",
        "get_installed_mod_entries",
        "enable_mod",
        "disable_mod",
        "delete_mod",
        "open_mods_folder",
    };

    internal static readonly string[] SavesDialogMethods =
    {
        "list_saves",
        "list_save_backups",
        "list_media",
        "open_media",
    };

    /// <summary>【Windows 源】SavesDialog 的媒体下拉文案 → 后端 <c>kind</c> 键(<c>:144</c>)。</summary>
    internal static readonly (string Label, string Kind)[] MediaKinds =
    {
        ("截图", "screenshots"),
        ("崩溃报告", "crash-reports"),
        ("日志", "logs"),
    };
}

/// <summary>
/// C4 的全局静态作用域:进作用域时快照、<c>Dispose</c> 时还原(D-69 的纪律)。
///
/// 比 C3c 的 <c>C3cScope</c> 多两样:
/// <list type="bullet">
/// <item><see cref="InstanceDetailPage.SettingsPageFactory"/>(C4 新增的注入缝);</item>
/// <item><see cref="PageLayoutStore.DirOverride"/>(LibraryPage 会真的读写布局文档,必须指到临时目录)。</item>
/// </list>
/// </summary>
internal sealed class C4Scope : IDisposable
{
    private readonly BridgeClient? _client;

    private readonly Action<string, string, ToastKind>? _toast;

    private readonly Window? _window;

    private readonly Grid? _host;

    private readonly Control? _blur;

    private readonly bool _smoke;

    private readonly Action<Control, Action>? _answer;

    private readonly Action<string>? _cancel;

    private readonly bool _motionUserEnabled;

    private readonly double _motionScale;

    private readonly ThemeVariant? _variant;

    private readonly IReadOnlyDictionary<string, Func<PageBase>> _registry;

    private readonly Action<Control, string>? _flyToTasks;

    private readonly Func<string, PageBase>? _settingsFactory;

    private readonly string? _layoutDir;

    private bool _disposed;

    private C4Scope()
    {
        _client = AppServices.Client;
        _toast = AppServices.ToastSink;
        _window = AppServices.Window;
        _host = Dlg.Host;
        _blur = Dlg.BlurTarget;
        _smoke = Dlg.SmokeActive;
        _answer = Dlg.SmokeAutoAnswer;
        _cancel = Dlg.SmokeAutoCancelSystemDialog;
        _motionUserEnabled = Motion.UserEnabled;
        _motionScale = Motion.Scale;
        _variant = Application.Current!.RequestedThemeVariant;
        _registry = PageRegistry.Snapshot();
        _flyToTasks = LaunchPage.FlyToTasksSeam;
        _settingsFactory = InstanceDetailPage.SettingsPageFactory;
        _layoutDir = PageLayoutStore.DirOverride;
    }

    internal static C4Scope Capture()
    {
        return new C4Scope();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AppServices.Client = _client;
        AppServices.ToastSink = _toast;
        AppServices.Window = _window;
        Dlg.Host = _host;
        Dlg.BlurTarget = _blur;
        Dlg.SmokeActive = _smoke;
        Dlg.SmokeAutoAnswer = _answer;
        Dlg.SmokeAutoCancelSystemDialog = _cancel;
        // Motion 的"用户开关"通过 Enabled 的 setter 写(与 C3cScope 完全同一套写法)
        Motion.Enabled = _motionUserEnabled;
        Motion.Scale = _motionScale;
        Application.Current!.RequestedThemeVariant = _variant;
        PageRegistry.Restore(_registry);
        LaunchPage.FlyToTasksSeam = _flyToTasks;
        InstanceDetailPage.SettingsPageFactory = _settingsFactory;
        PageLayoutStore.DirOverride = _layoutDir;
    }
}

/// <summary>
/// C4 的公共小工具:泵、找控件、点按钮、拿 <c>Dlg.Panel</c> 推出来的那张卡片、驱动菜单项。
/// 全部**不模拟真实输入设备**(D-115);等待一律"一边泵 Dispatcher 一边看条件"(Stopwatch + 9 秒上限,D-120)。
/// </summary>
internal static class C4Files
{
    // ---------------------------------------------------------------- 泵与查找(转发既有夹具)

    internal static void Flush(int rounds = 1)
    {
        C3bFiles.Flush(rounds);
    }

    internal static void Pump(Func<bool> until, int timeoutMs = 9000)
    {
        C3bFiles.Pump(until, timeoutMs);
    }

    internal static void PumpTask(Task task, int timeoutMs = 9000)
    {
        C3bFiles.PumpTask(task, timeoutMs);
    }

    internal static void Settle(int rounds = 8)
    {
        C3bFiles.Settle(rounds);
    }

    internal static IEnumerable<Control> Descendants(Control root)
    {
        return C3bFiles.Descendants(root);
    }

    internal static List<TextBlock> Texts(Control root)
    {
        return C3bFiles.Texts(root);
    }

    /// <summary>文案完全相等的 TextBlock(C3b 只给了 FindText,按整份清单筛更顺手)。</summary>
    internal static List<TextBlock> TextsWith(Control root, string text)
    {
        return Texts(root).Where(t => string.Equals(t.Text, text, StringComparison.Ordinal)).ToList();
    }

    internal static bool HasText(Control root, string text)
    {
        return TextsWith(root, text).Count > 0;
    }

    /// <summary>按类型找一个后代(找不到就抛,不静默返回 null)。</summary>
    internal static T Find<T>(Control root, string what) where T : Control
    {
        T? hit = Descendants(root).OfType<T>().FirstOrDefault();
        Assert.True(hit != null, "页面里找不到 " + what + "(" + typeof(T).Name + ")");
        return hit!;
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

    /// <summary>跑一次页面的首屏加载(把它当外壳在做事)。</summary>
    internal static void Load(PageBase page)
    {
        C3bFiles.PumpTask(page.EnsureLoadedAsync());
    }

    // ---------------------------------------------------------------- Dlg 层

    /// <summary>现在压在 <c>Dlg.Host</c> 上的浮层数(每层一个 wrap-Grid)。</summary>
    internal static int LayerCount()
    {
        return Dlg.Host?.Children.OfType<Grid>().Count() ?? 0;
    }

    /// <summary>某一层里的那张卡片(<c>wrap → holder → card</c>,与 Dlg.Push 的结构一一对应)。</summary>
    private static Control? CardAt(int index)
    {
        Grid? wrap = Dlg.Host?.Children.OfType<Grid>().ElementAtOrDefault(index);
        Grid? holder = wrap?.Children.OfType<Grid>().LastOrDefault();
        return holder?.Children.OfType<Control>().FirstOrDefault();
    }

    /// <summary>
    /// 泵到由 <c>Dlg.Panel</c> 推出的那张卡片出现并返回它(它**不是** <c>DialogCard</c>,
    /// 所以不能用 <c>C3bFiles.WaitDialog</c>)。按标题文字认,免得把 <c>Dlg.Busy</c> 的卡片认成它。
    /// </summary>
    internal static Control PanelCard(string title)
    {
        Control? found = null;
        Pump(() =>
        {
            found = Enumerable.Range(0, LayerCount())
                .Select(CardAt)
                .LastOrDefault(c => c != null && HasText(c, title));
            return found != null;
        });
        return found!;
    }

    /// <summary>点面板右上角的「关闭」按钮并把这一层关掉。</summary>
    internal static void ClosePanel(Control panel)
    {
        int before = LayerCount();
        Click(ByText(panel, I18n.L("关闭")));
        Pump(() => LayerCount() < before);
    }

    /// <summary>对话框(Confirm / Ask)那一步:泵出卡片 → 点指定文案的按钮(**点完不等那个任务**)。</summary>
    internal static Control AnswerDialog(string buttonText)
    {
        C3bFiles.WaitDialog();
        Control card = CardAt(Math.Max(0, LayerCount() - 1))!;
        Click(ByText(card, buttonText));
        Settle(4);
        return card;
    }

    // ---------------------------------------------------------------- 菜单

    /// <summary>菜单顶层项的文案(分隔线记作 <c>"—"</c>)。</summary>
    internal static List<string> MenuHeads(ContextMenu menu)
    {
        List<string> heads = new List<string>();
        foreach (object? item in menu.Items)
        {
            if (item is MenuItem mi)
            {
                heads.Add(mi.Header as string ?? "");
            }
            else if (item is Separator)
            {
                heads.Add("—");
            }
        }

        return heads;
    }

    /// <summary>按文案取菜单项(找不到就抛,不静默返回 null)。</summary>
    internal static MenuItem MenuItemByHead(ContextMenu menu, string head)
    {
        MenuItem? hit = menu.Items.OfType<MenuItem>().FirstOrDefault(mi => string.Equals(mi.Header as string, head, StringComparison.Ordinal));
        Assert.True(hit != null, "菜单里没有「" + head + "」这一项");
        return hit!;
    }

    /// <summary>程序化点一个菜单项(与按钮一样走 <c>RaiseEvent</c>,不模拟输入设备)。</summary>
    internal static void Invoke(MenuItem item)
    {
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    /// <summary>取页面/卡片里某个按键名点进去的控件 —— 按文案或自动化名找按钮(MenuItem 也走这个)。</summary>
    internal static MenuItem? FindMenuItem(ContextMenu menu, string head)
    {
        return menu.Items.OfType<MenuItem>().FirstOrDefault(mi => string.Equals(mi.Header as string, head, StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- 数据小工具

    /// <summary>造一条 <c>get_version_rows</c> 的行(字段名与后端 / <see cref="VersionRowDto"/> 一致)。</summary>
    internal static string VersionRowJson(string id, string loader = "", bool isolated = true, int mods = 0, string isolationLabel = "", string java = "", string mc = "1.20.1")
    {
        return "{\"id\":\"" + id + "\",\"loader\":\"" + loader + "\",\"mc\":\"" + mc + "\",\"isolated\":" + (isolated ? "true" : "false") +
               ",\"mods\":" + mods + ",\"isolation_label\":\"" + isolationLabel + "\",\"java\":\"" + java + "\"}";
    }

    /// <summary>造一条 <c>get_instances</c> 的行。</summary>
    internal static string InstanceJson(string name, int versions = 1, string pack = "", string mc = "1.20.1", string javaLabel = "", string path = "")
    {
        return "{\"name\":\"" + name + "\",\"path\":\"" + path + "\",\"versions\":" + versions + ",\"mc\":\"" + mc + "\",\"pack\":\"" + pack +
               "\",\"mc_version\":\"" + mc + "\",\"java_label\":\"" + javaLabel + "\"}";
    }

    /// <summary>
    /// 把 <c>PageLayoutStore</c> 指到临时目录(1 号红线:测试的落盘必须落在仓库内)。
    /// **不** <c>new TestHome()</c> —— 那会改 <c>TIPCL_HOME</c> 环境变量并需要 Dispose,
    /// 会给同进程里的隔离用例留下脏状态(本包实测踩到过);这里只要 <see cref="TestHome.Root"/>。
    /// </summary>
    internal static string LayoutDir()
    {
        string dir = System.IO.Path.Combine(TestHome.Root, "layout-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        PageLayoutStore.DirOverride = dir;
        PageLayoutStore.ResetCache();
        return dir;
    }

    // ---------------------------------------------------------------- 假后端的参数读取

    /// <summary>
    /// 假后端第 <paramref name="index"/> 次收到的参数里,顶层键 <paramref name="key"/> 的**解码后**值。
    ///
    /// 为什么要解码:<c>BridgeClient</c> 序列化时按 STJ 的默认编码器把非 ASCII **转义**成 <c>\uXXXX</c>,
    /// 所以中文值在参数原文里不是「甲」而是「\u7532」—— 直接 <c>Assert.Contains("甲")</c> 必然找不到
    /// (本包实测踩到)。按 JSON 解析后取值才与页面里写的东西可比。
    /// </summary>
    internal static string ParamString(C3cFakeApi api, string method, string key, int index = 0)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(api.ParamsOf(method, index));
        return ReadString(doc.RootElement, key);
    }

    /// <summary>同上,但键在 <paramref name="parent"/> 那层里(例如 <c>data.default_instance</c>)。</summary>
    internal static string ParamStringIn(C3cFakeApi api, string method, string parent, string key, int index = 0)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(api.ParamsOf(method, index));
        return doc.RootElement.TryGetProperty(parent, out System.Text.Json.JsonElement p) ? ReadString(p, key) : "";
    }

    /// <summary>断言某个参数值(解码后),失败消息里带上方法名、键名与**实际值**。</summary>
    internal static void AssertParam(C3cFakeApi api, string method, string key, string expected, int index = 0)
    {
        string actual = ParamString(api, method, key, index);
        Assert.True(string.Equals(actual, expected, StringComparison.Ordinal),
            method + "(" + index + ") 的 " + key + " 期望「" + expected + "」,实际「" + actual + "」");
    }

    private static string ReadString(System.Text.Json.JsonElement obj, string key)
    {
        if (obj.ValueKind != System.Text.Json.JsonValueKind.Object || !obj.TryGetProperty(key, out System.Text.Json.JsonElement el))
        {
            return "";
        }

        return (el.ValueKind == System.Text.Json.JsonValueKind.String) ? (el.GetString() ?? "") : el.GetRawText();
    }

    // ---------------------------------------------------------------- 页面挂载

    /// <summary>
    /// 把一个页面挂进"浮层宿主那个窗口"(不是另开一个窗口)—— 这样页面与 <c>Dlg</c> 的浮层在**同一棵树**上,
    /// 既有渲染尺寸,又能点到页面里弹出的对话框。
    /// </summary>
    internal static T Mount<T>(C4LayerHost harness, T page) where T : Control
    {
        harness.Host.Children.Add(page);
        Flush(2);
        return page;
    }
}

/// <summary>
/// C4 的浮层宿主:把 <see cref="Dlg.Host"/> 指到一个挂在真窗口里的 Grid,用完还原(全局静态纪律,D-69)。
///
/// 与 C2a 的 <c>DlgHarness</c> 是同一套做法,只多暴露一个**那个真窗口** ——
/// <c>SmoothScroll.BindWindowMaxHeight(Window?, pct, floor)</c> 的可注入重载要的是窗口(不是 Grid),
/// 本包有两条用例要在"页面自己的 ScrollViewer"上绑一次以钉住 <c>0.5/220</c> 与 <c>0.4/180</c> 这两个常量。
/// </summary>
internal sealed class C4LayerHost : IDisposable
{
    private readonly Grid? _previousHost;

    private readonly Control? _previousBlur;

    private readonly UiHost _window;

    private C4LayerHost(UiHost window, Grid host, Grid? previousHost, Control? previousBlur)
    {
        _window = window;
        Host = host;
        _previousHost = previousHost;
        _previousBlur = previousBlur;
    }

    /// <summary>浮层宿主 Grid(交给 <c>Dlg.Host</c> 的那个)。</summary>
    internal Grid Host { get; }

    /// <summary>承载它的真窗口(<c>BindWindowMaxHeight</c> 的可注入宿主)。</summary>
    internal Window Window => _window.Window;

    internal static C4LayerHost Show()
    {
        Grid host = new Grid();
        UiHost window = UiHost.Show(host);
        Grid? previousHost = Dlg.Host;
        Control? previousBlur = Dlg.BlurTarget;
        Dlg.Host = host;
        Dlg.BlurTarget = null;
        return new C4LayerHost(window, host, previousHost, previousBlur);
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
/// C4 用的探针页:给 <c>InstanceDetailPage.SettingsPageFactory</c> 注入,证明"接上了就真的嵌进去"。
/// 不是产品代码,只在测试里用(D-119 / D-134 的注入缝就是给这个用的)。
/// </summary>
internal sealed class SettingsProbePage : PageBase
{
    internal bool Loaded { get; private set; }

    internal bool Shown { get; private set; }

    /// <summary>工厂收到的实例名(证明参数是页面传下来的,不是写死的)。</summary>
    internal string Instance { get; private set; } = "";

    internal SettingsProbePage(string instance)
    {
        Instance = instance;
        Content = Ui.V(8.0, Ui.Txt("探针设置页"));
    }

    public override string Title => "探针设置页";

    protected override Task LoadAsync()
    {
        Loaded = true;
        return Task.CompletedTask;
    }

    public override void OnShown()
    {
        Shown = true;
    }
}
