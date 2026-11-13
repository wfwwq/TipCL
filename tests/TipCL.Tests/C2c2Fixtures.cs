using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C2c2(页面级视觉服务:拖放 / 背景 / 缩略图 / 加载器图标 / 账号守卫)期望值的**权威副本**,
/// 与 C1a/C1c/C2b/C2c1 同一套做法:期望值来自 Windows 源、任务书与 GOAL,在测试里各留一份,
/// 源码改了而期望没跟着改会立刻红。每条都注明来源:
/// 【Windows 源】/【任务书】/【本机实测】。
/// </summary>
internal static class C2c2Expected
{
    // ---------------------------------------------------------------- 拖放浮层(来源:Windows MainWindow.xaml / .xaml.cs)

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1146</c> —— 淡入 120ms。</summary>
    internal const double VeilFadeInMs = 120.0;

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:1150</c> —— 淡出 110ms。</summary>
    internal const double VeilFadeOutMs = 110.0;

    /// <summary>【Windows 源】<c>MainWindow.xaml:79</c> —— 卡片圆角 10、描边 2、内边距 (20,16,20,18)。</summary>
    internal const double VeilCardCornerRadius = 10.0;

    internal const double VeilCardBorderThickness = 2.0;

    internal static readonly Avalonia.Thickness VeilCardPadding = new Avalonia.Thickness(20.0, 16.0, 20.0, 18.0);

    /// <summary>【Windows 源】<c>MainWindow.xaml:81-83</c> —— 图标 30 / 标题 15 / 提示 12(最宽 360)。</summary>
    internal const double VeilIconSize = 30.0;

    internal const double VeilTitleSize = 15.0;

    internal const double VeilHintSize = 12.0;

    internal const double VeilHintMaxWidth = 360.0;

    /// <summary>【Windows 源】<c>MainWindow.xaml.cs:166-167</c> —— 浮层两条文案。</summary>
    internal const string VeilTitleSource = "松手就装";

    internal const string VeilHintSource = "整合包、模组、资源包、光影、数据包、存档、皮肤、壁纸都认;认不准会问你一句。";

    /// <summary>【Windows 源】<c>FileDrop.cs:26-34</c> 的 kind → 后端方法名表。</summary>
    internal static readonly (string Kind, string Method)[] InstallMethods =
    {
        ("mod", "install_mod"),
        ("resourcepack", "install_resourcepack"),
        ("shaderpack", "install_shader"),
        ("datapack", "install_datapack"),
        ("world", "install_world"),
    };

    // ---------------------------------------------------------------- 背景管线(来源:Windows Services\Wallpaper.cs)

    /// <summary>【Windows 源】<c>Wallpaper.cs:383</c> —— 单图解码宽度 1920。</summary>
    internal const int StillDecodeWidth = 1920;

    /// <summary>【Windows 源】<c>Wallpaper.cs:272-273</c> —— 模糊夹 0-40、遮罩夹 0-80。</summary>
    internal const int BlurMax = 40;

    internal const int DimMax = 80;

    /// <summary>【Windows 源】<c>Wallpaper.cs:201</c> —— 轮播间隔夹 1-1440 分钟。</summary>
    internal const int IntervalMin = 1;

    internal const int IntervalMax = 1440;

    /// <summary>【Windows 源】<c>Wallpaper.cs:167-196</c> —— 6 个设置键与默认值。</summary>
    internal static readonly (string Key, string Kind, string Default)[] SettingKeys =
    {
        ("ui_background", "string", ""),
        ("ui_background_folder", "string", ""),
        ("ui_background_shuffle", "bool", "false"),
        ("ui_background_interval", "int", "10"),
        ("ui_background_blur", "int", "0"),
        ("ui_background_dim", "int", "0"),
    };

    /// <summary>【Windows 源】<c>SettingsPage.cs:635-641</c> —— 四个内置预设(顺序一字不改)。</summary>
    internal static readonly (string File, string Label)[] Presets =
    {
        ("preset-morning", "晨雾"),
        ("preset-dusk", "暮色"),
        ("preset-night", "星夜"),
        ("preset-sakura", "樱粉"),
    };

    // ---------------------------------------------------------------- 缩略图 / 加载器图标

    /// <summary>【Windows 源】<c>Thumbs.cs:13</c> —— 并发闸门 8。</summary>
    internal const int ThumbsMaxParallel = 8;

    /// <summary>【Windows 源】<c>Thumbs.cs:87-90/142</c> —— 内存缓存超过 400 张裁到 300。</summary>
    internal const int ThumbsCacheLimit = 400;

    internal const int ThumbsCacheKept = 300;

    /// <summary>【Windows 源】<c>Thumbs.cs:23</c> —— 失败冷却 5 分钟。</summary>
    internal static readonly TimeSpan ThumbsFailCooldown = TimeSpan.FromMinutes(5.0);

    /// <summary>【Windows 源】<c>Thumbs.cs:126</c> / <c>ThumbTile.cs:44</c> —— 取图宽度 max(16, px*2) / max(16, Width)。</summary>
    internal static int ThumbDecodeWidth(int px)
    {
        return Math.Max(16, px * 2);
    }

    /// <summary>【Windows 源】<c>LoaderIcons.cs:52-74</c> —— 加载器名 → 嵌入图标文件名。</summary>
    internal static readonly (string Loader, string File)[] LoaderFiles =
    {
        ("fabric", "fabric_gh.png"),
        ("forge", "forge_gh.png"),
        ("neoforge", "neoforge_gh.png"),
        ("quilt", "quilt_gh.png"),
        ("", "grass.png"),
        ("vanilla", "grass.png"),
    };

    /// <summary>【Windows 源】<c>LoaderIcons.cs:40</c> —— 解码宽度 clamp(ceil(size)*2, 32, 96)。</summary>
    internal static int LoaderPixelSize(double size)
    {
        return (int)Math.Clamp(Math.Ceiling(size) * 2.0, 32.0, 96.0);
    }

    // ---------------------------------------------------------------- 账号守卫(来源:Windows Services\AccountGate.cs)

    /// <summary>【Windows 源】<c>AccountGate.cs:94</c> —— 名字长度 1-16、不能含空格。</summary>
    internal const int NameMinLength = 1;

    internal const int NameMaxLength = 16;
}

/// <summary>C2c2 的用例碰进程级注入缝(FileDrop / Wallpaper / Thumbs / LoaderIcons / AccountGate 的
/// 静态口子),按 D-103 的纪律归到同一个 collection(彼此不并发),且用例之间不许有顺序依赖。</summary>
[CollectionDefinition("C2c2Globals", DisableParallelization = true)]
public sealed class C2c2GlobalsCollection
{
}

/// <summary>
/// C2c2 的全局静态快照:本包新加的注入缝一律在这里取快照并在 <see cref="Dispose"/> 里还原
/// (D-69 的纪律 —— 全局改动必须在 finally / Dispose 里还回去,否则会串到别的用例)。
///
/// 用法:<c>using C2c2Scope scope = C2c2Scope.Capture();</c>(放在用例体最前面;
/// 与 <see cref="ShellFixture"/> 配合时它先构造、后释放,于是外壳关掉之后才还原这些静态)。
/// </summary>
internal sealed class C2c2Scope : IDisposable
{
    private readonly bool _dropEnabled;
    private readonly Func<bool> _readyGate;
    private readonly FileDrop.DropSink? _sink;
    private readonly Func<TipCL.Models.AccountRow, string, Task<string>>? _skin;
    private readonly Wallpaper.ISettingsSource? _settings;
    private readonly Wallpaper.WallpaperTimer? _timer;
    private readonly Wallpaper.IVideoHost? _video;
    private readonly Action? _backdrop;
    private readonly Action? _sideFrost;
    private readonly Action? _chrome;
    private readonly ImageDecode.BitmapDecoder _decoder;
    private readonly Thumbs.IThumbStore _store;
    private readonly Func<Task<List<TipCL.Models.AccountRow>>> _rows;
    private readonly Func<string, string, Task<string>> _creator;

    private bool _disposed;

    private C2c2Scope()
    {
        _dropEnabled = FileDrop.Enabled;
        _readyGate = FileDrop.ReadyGate;
        _sink = FileDrop.Sink;
        _skin = FileDrop.SkinApplier;
        _settings = Wallpaper.Settings;
        _timer = Wallpaper.TimerFactory;
        _video = Wallpaper.VideoHost;
        _backdrop = Wallpaper.BackdropRefresh;
        _sideFrost = Wallpaper.SideFrostRefresh;
        _chrome = Wallpaper.ChromeInvalidated;
        _decoder = ImageDecode.Decoder;
        _store = Thumbs.Store;
        _rows = AccountGate.RowSource;
        _creator = AccountGate.AccountCreator;
    }

    internal static C2c2Scope Capture()
    {
        return new C2c2Scope();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // 拖放:先把这一轮的拖拽状态收干净,再还原口子。
        FileDrop.DragLeave();
        FileDrop.Enabled = _dropEnabled;
        FileDrop.ReadyGate = _readyGate;
        FileDrop.Sink = _sink;
        FileDrop.SkinApplier = _skin;
        FileDrop.ResetDragState();

        // 背景:先停轮播 / 视频并回到"没有背景",再还原口子。
        Wallpaper.Shutdown();
        Wallpaper.SetSource("");
        Wallpaper.SetEffects(0, 0);
        Wallpaper.Settings = _settings;
        Wallpaper.TimerFactory = _timer;
        Wallpaper.VideoHost = _video;
        Wallpaper.BackdropRefresh = _backdrop;
        Wallpaper.SideFrostRefresh = _sideFrost;
        Wallpaper.ChromeInvalidated = _chrome;
        Wallpaper.Detach();

        // 解码 / 缩略图 / 图标 / 账号。
        ImageDecode.Decoder = _decoder;
        Thumbs.Store = _store;
        Thumbs.Clear();
        LoaderIcons.ClearCache();
        AccountGate.RowSource = _rows;
        AccountGate.AccountCreator = _creator;
    }
}

/// <summary>
/// 探针页:只用来验 <c>PageBase.EnableDrop</c>(C3 之前没有任何真页面)。
/// 不画东西、<c>LoadAsync</c> 立刻完成;记录它被切走 / 切回来的次数好验"页面实例是缓存的"。
/// </summary>
internal sealed class DropProbePage : PageBase
{
    internal int Shown { get; private set; }

    internal int Hidden { get; private set; }

    public override string Title => "拖放探针页";

    public override void OnShown()
    {
        Shown++;
    }

    public override void OnHidden()
    {
        Hidden++;
    }
}

/// <summary>
/// 假的缩略图路径来源:把 url 映射到给定的文件,记录被问过几次
/// (验"先 thumb_path 再 ensure_thumb"那两步,以及内存缓存命中之后不再问)。
/// </summary>
internal sealed class FakeThumbStore : Thumbs.IThumbStore
{
    private readonly Func<string, string> _path;

    internal FakeThumbStore(Func<string, string> path)
    {
        _path = path;
    }

    /// <summary>被问过几次"缓存里有没有"(= 后端的 thumb_path)。</summary>
    internal int CachedCalls { get; private set; }

    /// <summary>被问过几次"去取一张"(= 后端的 ensure_thumb)。</summary>
    internal int EnsureCalls { get; private set; }

    /// <summary>true = 第一次问给空串(逼出 ensure_thumb 那一步)。</summary>
    internal bool MissFirstCacheLookup { get; set; } = true;

    public Task<string> CachedPathAsync(string url)
    {
        CachedCalls++;
        return Task.FromResult(MissFirstCacheLookup ? "" : _path(url));
    }

    public Task<string> EnsurePathAsync(string url)
    {
        EnsureCalls++;
        return Task.FromResult(_path(url));
    }
}

/// <summary>假的视频壁纸播放后端(mac 侧默认不支持,靠它验"接上之后的那条路")。</summary>
internal sealed class FakeVideoHost : Wallpaper.IVideoHost
{
    internal List<string> Started { get; } = new List<string>();

    internal List<bool> Active { get; } = new List<bool>();

    internal int Stopped { get; private set; }

    public void Start(string path)
    {
        Started.Add(path);
    }

    public void SetActive(bool active)
    {
        Active.Add(active);
    }

    public void Stop()
    {
        Stopped++;
    }
}

/// <summary>假的轮播计时器:记账 + 手动点火(与 C2c1 的 <c>FakeToastTimer</c> 同一套,不用 Thread.Sleep)。</summary>
internal sealed class FakeWallpaperTimer
{
    private readonly List<Ticket> _tickets = new List<Ticket>();

    /// <summary>所有发出去过的计时(含已被停掉的)。</summary>
    internal List<Ticket> All => _tickets;

    /// <summary>还活着(没被 Dispose)的计时数 —— 与 <c>Wallpaper.Rotating</c> 应当时刻一致。</summary>
    internal int LiveCount => _tickets.Count(t => !t.Stopped);

    internal sealed class Ticket : IDisposable
    {
        internal Ticket(TimeSpan delay, Action onFire)
        {
            Delay = delay;
            OnFire = onFire;
        }

        internal TimeSpan Delay { get; }

        internal Action OnFire { get; }

        internal bool Stopped { get; private set; }

        public void Dispose()
        {
            Stopped = true;
        }
    }

    /// <summary>注入给 <see cref="Wallpaper"/> 的那个委托。</summary>
    internal IDisposable Schedule(TimeSpan delay, Action onFire)
    {
        Ticket ticket = new Ticket(delay, onFire);
        _tickets.Add(ticket);
        return ticket;
    }

    /// <summary>点火第 <paramref name="index"/> 个还活着的计时(模拟"到点了")。</summary>
    internal Ticket Fire(int index = 0)
    {
        Ticket ticket = _tickets.Where(t => !t.Stopped).ElementAt(index);
        ticket.OnFire();
        return ticket;
    }

    /// <summary>最近一次发出去的那张票。</summary>
    internal Ticket Last => _tickets[_tickets.Count - 1];
}

/// <summary>
/// 假的设置来源(<see cref="Wallpaper.ISettingsSource"/>):把 <c>get_setting</c> 那 6 个键换成一张字典,
/// 顺便记录被问过哪些键(验键名与默认值与 Windows 一致)。
/// </summary>
internal sealed class FakeWallpaperSettings : Wallpaper.ISettingsSource
{
    private readonly Dictionary<string, object> _values;

    internal FakeWallpaperSettings(Dictionary<string, object> values)
    {
        _values = values;
    }

    /// <summary>被问过的键(按顺序)。</summary>
    internal List<string> Asked { get; } = new List<string>();

    public Task<string> GetStringAsync(string key, string fallback)
    {
        Asked.Add(key);
        return Task.FromResult(_values.TryGetValue(key, out object? value) ? (string)value : fallback);
    }

    public Task<bool> GetBoolAsync(string key, bool fallback)
    {
        Asked.Add(key);
        return Task.FromResult(_values.TryGetValue(key, out object? value) ? (bool)value : fallback);
    }

    public Task<int> GetIntAsync(string key, int fallback)
    {
        Asked.Add(key);
        return Task.FromResult(_values.TryGetValue(key, out object? value) ? (int)value : fallback);
    }
}

/// <summary>C2c2 用的小工具:临时文件(一律落在 <see cref="TestHome.Root"/> 之下)与 Dispatcher 泵。</summary>
internal static class C2c2Files
{
    /// <summary>造一个临时目录(在 <see cref="TestHome"/> 的实例目录里)。</summary>
    internal static string Dir(TestHome home, string name)
    {
        string dir = Path.Combine(home.Dir, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>把一段字节写成文件(用于"坏图"这类用例)。</summary>
    internal static string Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>把内嵌的预设背景释放成一份真图(不去读图片字节进用例,只走管线)。</summary>
    internal static string Preset(TestHome home, string name)
    {
        string dir = Dir(home, "wallpapers");
        string destination = Path.Combine(dir, name + ".jpg");
        if (File.Exists(destination))
        {
            return destination;
        }

        using Stream? source = Wallpaper.OpenPreset(name);
        Assert.NotNull(source);
        using FileStream target = File.Create(destination);
        source!.CopyTo(target);
        return destination;
    }

    /// <summary>
    /// 在**线程池**上把一段异步取图跑完再同步取回结果。
    ///
    /// 为什么不能直接在会话线程上 <c>GetResult()</c>:<c>Thumbs.FetchAsync</c> 里有
    /// <c>await Task.Run(...)</c>(解码),在会话线程上 await 会把续体投回 Dispatcher,
    /// 而会话线程正被用例体占着 —— 正是 D-79 说的那种自锁。
    /// 从池上发起就没有捕获到 Dispatcher 的 <c>SynchronizationContext</c>,续体留在池上,
    /// 这也与生产环境(UI 线程不阻塞、取图照常回来)一致。
    /// </summary>
    internal static T RunOffThread<T>(Func<Task<T>> work)
    {
        return Task.Run(work).GetAwaiter().GetResult();
    }

    /// <summary>把 Dispatcher 排队的活儿跑几轮(点完按钮之后等续体时用;不 sleep、可确定)。</summary>
    internal static void Pump(Func<bool> until, int rounds = 64)
    {
        for (int i = 0; i < rounds && !until(); i++)
        {
            UiHost.Flush();
        }
    }

    /// <summary>
    /// 当前对话框的卡片。
    /// **必须"先把对话框推出来、再点按钮"**:<c>Dlg.Ask</c> 是 <c>layer = Push(...)</c> 之后才
    /// <c>button.Click += …</c> 的,而离屏的 <c>SmokeAutoAnswer</c> 恰恰是在 <c>Push</c> 里面被调 ——
    /// 在那一刻点确定,TrySetResult 的处理器还没装上,点了也白点(本包实测踩到,见 PROGRESS)。
    /// </summary>
    internal static DialogCard Dialog()
    {
        UiHost.Flush();
        Assert.NotNull(Dlg.Host);
        return DlgHarness.Descendants(Dlg.Host!).OfType<DialogCard>().Last();
    }

    /// <summary>程序化点一个按文字找的按钮(不模拟真实输入设备)。</summary>
    internal static void Click(DialogCard card, string text)
    {
        DlgHarness.Click(DlgHarness.ButtonByText(card, text));
    }

    /// <summary>从控件树里找一个 TextBox(对话框里那个输入框;ComboBox 模板里的不算)。</summary>
    internal static TextBox Input(Control root)
    {
        return root.GetLogicalDescendants()
            .OfType<TextBox>()
            .First(box => box.FindLogicalAncestorOfType<ComboBox>() is null);
    }

    /// <summary>对话框里的下拉框(皮肤选择)。</summary>
    internal static ComboBox Combo(Control root)
    {
        return root.GetLogicalDescendants().OfType<ComboBox>().First();
    }
}

/// <summary>重放一下 Avalonia 的 VisualTree 扩展(测试常用;与 C2bAssert 同一套实现,单独放一份免得跨包依赖)。</summary>
internal static class C2c2Visual
{
    internal static IEnumerable<Control> Descendants(Control root)
    {
        return root.GetLogicalDescendants().OfType<Control>();
    }
}
