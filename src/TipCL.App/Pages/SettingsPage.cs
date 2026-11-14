using System;
using TipCL.Controls;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using TipCL.Models;
using TipCL.Services;
using TipCL.Themes;

namespace TipCL.Pages;

/// <summary>
/// C5a:Windows 版 <c>Pages\SettingsPage.cs</c>(70122 字节 / 2012 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// **设置页**(导航 id <c>settings</c>)。附带两个分部件:<c>SettingsPage.Themes.cs</c>(主题包 + 视觉应用 + 颜色数学)、
/// <c>SettingsPage.PhoneSync.cs</c>(手机同步卡片),与 Windows 的三文件切分一一对应(WP10 拆分,见 Windows 的 D-24)。
///
/// 形状(逐条照抄 Windows):左导航 7 段(通用 / 账号 / Java / 下载与网络 / 服务器 / 游戏时长 / 反馈与关于)+
/// 右内容区;「通用」与「反馈与关于」用 <see cref="BlockHost"/> 分组(块 id/标题/顺序逐字照抄),
/// 其余分段内嵌**独立页面**(Windows 也是 <c>new AccountPage()</c> 那样内嵌,本页自持实例、
/// 不与外壳共享,免得同一个控件两个父级)。
///
/// 行为(与 Windows 一致):<c>get_settings</c> 起表 + 控件一改就 500ms 防抖自动保存(<c>save_settings</c> / <c>set_multi_instance</c> /
/// 需要时 <c>set_language</c>);深色/主题色/背景**立即生效**;壁纸走 <see cref="Wallpaper"/> 既有管线;
/// 主题包五个动作(保存 / 加载 / 删除 / 导入 / 导出);空间清理两段(<c>cleaner_preview</c> → 勾选 → <c>cleaner_apply</c>);
/// AI 接入(内置 / 自定义,<c>ai_list_models</c> / <c>test_ai_connection</c> / <c>ai_vision_status</c> / <c>ai_vision_set_mode</c>)、
/// 记忆(ai_memory.md)、MCP Server;检查更新、智能推荐、语言切换预览。
///
/// <b>四类 mac 侧差异(逐条,理由写在行内;请 Lead 裁决的都在这里)</b>:
/// <list type="number">
/// <item><b>内嵌分段走注入缝(A 类)</b>:<c>AccountPage</c> / <c>JavaPage</c> / <c>ServersPage</c> / <c>PlaytimePage</c> /
///   <c>FeedbackPage</c> / <c>FirstRunWizard</c> 六个在 mac 侧**还不存在**(属后续工作包)。照 D-119 / D-134 的先例,
///   本页只留注入缝(<see cref="AccountPageFactory"/> 等);<b>没接缝时显示一句如实的占位说明</b>,不空白、不假装能用。
///   Windows 的 <c>_feedback</c> 是**字段初始化**(构造即 new),mac 侧必须改成"切到第 6 段时才经工厂建" ——
///   否则构造设置页会连带构造一个未移植的页面;</item>
/// <item><b>主题覆盖层落在主题层(B 类,Lead 裁决)</b>:Windows 直接写 <c>App.PaletteDictionary</c>;
///   mac 侧是新建的 <see cref="PaletteOverride"/>(<c>Themes\PaletteOverride.cs</c>),设置页一个字节都不碰资源字典本身;</item>
/// <item><b>外壳成员换名/C 类降级</b>:<c>App.IsDark</c> → <see cref="PaletteOverride.IsDarkNow"/>;
///   <c>App.ApplyTheme(bool)</c> / <c>MainWindow.SetDark(...)</c> → 设 <c>Application.RequestedThemeVariant</c> +
///   <c>Win?.ApplyThemeVisuals()</c>(mac 的外壳按变体自己刷取色,没有 <c>SetDark</c> / <c>SyncThemeIndicator</c> ——
///   mac 的标题栏走系统红绿灯,没有那个图标要同步);<c>App.ThemeVisualsChanged</c> → Avalonia 的
///   <c>Application.ActualThemeVariantChanged</c>;</item>
/// <item><b>D 类(离屏/测试不能碰的东西)走注入缝</b>:重启启动器(<c>Process.Start(自身)</c>=<see cref="ProcessLauncher"/>)、
///   复制 MCP 地址(<c>Clipboard.SetText</c>=<see cref="ClipboardWriter"/>,与 <c>CrashUi.ClipboardWriter</c> 同一模式);
///   壁纸预设缩略图走 C2c2 已落地的 <see cref="ImageDecode"/> + <see cref="Wallpaper.OpenPreset"/>,
///   不自己新写解码。</item>
/// </list>
/// </summary>
public sealed partial class SettingsPage : PageBase
{
    /// <summary>AI 模型列表的结果(Windows 的 <c>ai_list_models</c> 回包;嵌套类一字不改)。</summary>
    internal sealed class AiModelList
    {
        public bool Ok { get; set; }

        public List<string> Models { get; set; } = new List<string>();

        public string Error { get; set; } = "";
    }

    /// <summary>外观附加项(Windows 存在数据目录的 <c>wpf-theme.json</c>;字段一字不改)。</summary>
    internal sealed class ThemeExtras
    {
        public string ThemeColor { get; set; } = "#2E9B6B";

        public string Background { get; set; } = "default";

        public bool FlyAnimation { get; set; } = true;

        public int FlyDurationMs { get; set; } = 620;
    }

    /// <summary>带 key 的下拉项(Windows 的私有 <c>Opt</c>;显示用 <c>Label</c>,落盘用的是 <c>Key</c>)。</summary>
    private sealed class Opt
    {
        public required string Key { get; init; }

        public required string Label { get; init; }

        public override string ToString()
        {
            return Label;
        }
    }

    private const string DefaultColor = "#2E9B6B";

    // 必须排在 _visualColor 前面:静态字段按书写顺序初始化(Windows 同)
    internal static ThemeExtras Extras { get; } = LoadExtras();

    private static string _visualColor = NormalizeColor(Extras.ThemeColor) ?? DefaultColor;

    private static string _visualBackground = NormalizeBackground(Extras.Background);

    /// <summary>【Windows 源】<c>:62-68</c> 新版本默认隔离。</summary>
    private static readonly (string Key, string Label)[] IsoOpts = new(string, string)[4]
    {
        ("none", "关闭(共用实例目录)"),
        ("saves", "隔离存档"),
        ("mods", "隔离 Mod 与配置"),
        ("all", "隔离全部"),
    };

    /// <summary>【Windows 源】<c>:70-77</c> 内存回收器。</summary>
    private static readonly (string Key, string Label)[] GcOpts = new(string, string)[5]
    {
        ("auto", "G1(推荐)"),
        ("g1", "G1"),
        ("g1_tuned", "调优 G1"),
        ("zgc", "ZGC"),
        ("none", "不指定"),
    };

    /// <summary>【Windows 源】<c>:79-86</c> 启动器可见性。</summary>
    private static readonly (string Key, string Label)[] VisOpts = new(string, string)[5]
    {
        ("keep", "保持显示"),
        ("minimize", "最小化"),
        ("hide", "隐藏"),
        ("hide_reopen", "隐藏,退出后重开"),
        ("close", "关闭启动器"),
    };

    /// <summary>【Windows 源】<c>:88-93</c> 启动页主页。</summary>
    private static readonly (string Key, string Label)[] HomeOpts = new(string, string)[3]
    {
        ("news", "Minecraft 新闻"),
        ("custom", "本地 HTML"),
        ("blank", "空白"),
    };

    /// <summary>【Windows 源】<c>:95-99</c> 默认游戏窗口。</summary>
    private static readonly (string Key, string Label)[] WinOpts = new(string, string)[2]
    {
        ("window", "窗口"),
        ("maximize", "全屏"),
    };

    /// <summary>【Windows 源】<c>:101-106</c> 文件下载源。</summary>
    private static readonly (string Key, string Label)[] SrcOpts = new(string, string)[3]
    {
        ("auto", "自动(官方慢则走 BMCLAPI)"),
        ("official", "仅官方"),
        ("bmclapi", "仅 BMCLAPI"),
    };

    /// <summary>【Windows 源】<c>:108-113</c> 社区资源源。</summary>
    private static readonly (string Key, string Label)[] CommOpts = new(string, string)[3]
    {
        ("auto", "自动"),
        ("official", "仅官方"),
        ("mcim", "仅 MCIM"),
    };

    /// <summary>【Windows 源】<c>:115-121</c> 界面背景预设。</summary>
    private static readonly (string Key, string Label)[] BgOpts = new(string, string)[4]
    {
        ("default", "默认"),
        ("eye", "护眼绿"),
        ("warm", "暖米"),
        ("mist", "雾蓝"),
    };

    /// <summary>【Windows 源】<c>:123-127</c> AI 接入方式。</summary>
    private static readonly (string Key, string Label)[] AiModeOpts = new(string, string)[2]
    {
        ("builtin", "内置网关(免配置)"),
        ("custom", "自定义 OpenAI 兼容端点"),
    };

    /// <summary>【Windows 源】<c>:129-137</c> 六个主题色色块(标签 + 十六进制)。</summary>
    private static readonly (string Name, string Hex)[] SwatchPalette = new(string, string)[6]
    {
        ("松绿", "#2E9B6B"),
        ("湛蓝", "#2F72C4"),
        ("紫罗兰", "#7C5CFC"),
        ("暖橙", "#D68A17"),
        ("绯红", "#D64545"),
        ("青碧", "#149B9B"),
    };

    private readonly CheckBox _dark = Ui.Switch();

    private readonly CheckBox _motion = Ui.Switch(value: true);

    private readonly CheckBox _fly = Ui.Switch(value: true);

    private readonly Slider _flyDur = Ui.Sld(200.0, 1200.0, 620.0, 20.0);

    private readonly TextBlock _flyDurLbl = Ui.Txt("620 ms", 12.0, bold: true);

    private readonly SPanel _swatches = Ui.H(8.0);

    private readonly ComboBox _bg = KeyCombo(BgOpts);

    private readonly Slider _threads = Ui.Sld(1.0, 64.0, 8.0);

    private readonly TextBlock _threadsLbl = Ui.Txt("8", 12.0, bold: true);

    private readonly Slider _segs = Ui.Sld(1.0, 16.0, 4.0);

    private readonly TextBlock _segsLbl = Ui.Txt("4", 12.0, bold: true);

    private readonly ComboBox _src = KeyCombo(SrcOpts);

    private readonly ComboBox _comm = KeyCombo(CommOpts);

    private readonly CheckBox _proxy = Ui.Switch(value: true);

    private readonly TextBox _limit = Ui.Input("0", "0", 110.0);

    private readonly Slider _mem = Ui.Sld(512.0, 32768.0, 4096.0, 256.0);

    private readonly TextBlock _memLbl = Ui.Txt("4096 MB", 12.0, bold: true);

    private readonly TextBox _w = Ui.Input("854", "854", 80.0);

    private readonly TextBox _h = Ui.Input("480", "480", 80.0);

    private readonly ComboBox _inst = Ui.Combo(null, null, 200.0);

    private readonly ComboBox _iso = KeyCombo(IsoOpts);

    private readonly ComboBox _gc = KeyCombo(GcOpts);

    private readonly ComboBox _win = KeyCombo(WinOpts);

    private readonly TextBox _jvm = Ui.Input("例如 -XX:+UseG1GC(可空)");

    private readonly CheckBox _shareLib = Ui.Switch();

    private readonly CheckBox _shareAssets = Ui.Switch();

    private readonly CheckBox _multi = Ui.Switch();

    private readonly ComboBox _vis = KeyCombo(VisOpts);

    private readonly ComboBox _home = KeyCombo(HomeOpts);

    private readonly TextBox _homePath = Ui.Input("本地 .html 文件路径(主页选「本地 HTML」时生效)");

    private readonly CheckBox _autoUpdate = Ui.Switch(value: true);

    private readonly ComboBox _lang = KeyCombo(Array.Empty<(string, string)>());

    private readonly TextBlock _langPreview = Ui.Small("");

    private readonly TextBox _gameDir = Ui.Input("游戏目录(.minecraft 的落脚处)");

    private readonly TextBlock _cleanInfo = Ui.Small("还没扫描");

    private readonly ComboBox _themePack = Ui.Combo(null, null, 200.0);

    private readonly TextBlock _themeInfo = Ui.Small("");

    private readonly ComboBox _aiMode = KeyCombo(AiModeOpts);

    private readonly TextBox _aiBase = Ui.Input("https://api.openai.com/v1");

    private readonly TextBox _aiKey = Ui.Input("sk-...");

    private readonly ComboBox _aiModel = Ui.Combo(null, null, 220.0, "模型名", editable: true);

    private readonly Button _aiModelFetch;

    /// <summary>只在自定义模式下显示的行:地址、令牌、模型、上下文窗口、备用模型。</summary>
    private readonly List<Control> _aiCustomRows = new List<Control>();

    /// <summary>上次自动拉模型用的「地址 + 令牌」,没变就不重复拉。</summary>
    private string _aiFetchedFor = "";

    private readonly TextBox _aiCtx = Ui.Input("131072");

    private readonly TextBox _aiFallback = Ui.Input("留空不启用");

    private readonly CheckBox _aiConfirm = Ui.Switch(value: true);

    private readonly TextBox _cfKey = Ui.Input("CurseForge API Key(留空用内置)");

    private bool _cfSet;

    private bool _aiKeySet;

    private bool _cfClear;

    private bool _aiKeyClear;

    private string _cfHint = "";

    private string _aiKeyHint = "";

    private readonly Button _cfReset;

    private readonly Button _aiKeyReset;

    private readonly TextBox _bgPath = Ui.Input("图片或视频路径,留空则不用壁纸");

    private readonly TextBox _bgFolder = Ui.Input("壁纸文件夹(设了就轮播,优先于上面那张)");

    private readonly CheckBox _bgShuffle = Ui.Switch();

    private readonly Slider _bgInterval = Ui.Sld(1.0, 120.0, 10.0);

    private readonly TextBlock _bgIntervalLbl = Ui.Txt("10 分钟", 12.0, bold: true);

    private readonly Slider _bgBlur = Ui.Sld(0.0, 40.0, 0.0);

    private readonly TextBlock _bgBlurLbl = Ui.Txt("0 px", 12.0, bold: true);

    private readonly Slider _bgDim = Ui.Sld(0.0, 80.0, 0.0);

    private readonly TextBlock _bgDimLbl = Ui.Txt("0 %", 12.0, bold: true);

    private readonly TextBlock _bgInfo = Ui.Small("");

    private readonly Button _bgUndo;

    private readonly Button _clean;

    private readonly Button _testAi;

    private readonly Button _rec;

    /// <summary>「检查更新」钮(Windows 把它复用进「反馈与关于」区块的右上角)。</summary>
    private readonly Button _checkUpdate;

    private string _lang0 = "";

    private string _gameDir0 = "";

    private string _color = DefaultColor;

    private bool _sync;

    private CleanerPreview? _cleanPreview;

    /// <summary>【Windows 源】<c>:293-298</c> 会被主题色改写的 24 个键(顺序一字不改)。</summary>
    private static readonly string[] AccentKeys = new string[24]
    {
        "C.Accent", "C.AccentStrong", "C.AccentText", "C.AccentDeep", "C.AccentLite", "B.Accent", "B.AccentStrong", "B.OnAccentStrong", "B.AccentText", "B.AccentTint",
        "B.AccentDeep", "B.AccentLite", "B.AccentSoft", "B.AccentSoft2", "B.OnAccent", "B.Glass", "B.GlassStrong", "B.GlassBorder", "B.Ok", "B.OnOk",
        "B.BannerFill", "B.OnBanner", "B.LogoFill", "B.OnLogo",
    };

    /// <summary>【Windows 源】<c>:300</c> 会被背景预设改写的 3 个键。</summary>
    private static readonly string[] BackgroundKeys = new string[3] { "C.Canvas", "B.Canvas", "B.PageWash" };

    public override string Title => I18n.L("设置");

    /// <summary>【Windows 源】<c>:304-314</c>:下载飞入动画开关(用户偏好 + 系统动画总闸)。</summary>
    internal static bool FlyEnabled
    {
        get
        {
            if (Extras.FlyAnimation)
            {
                return Motion.Enabled;
            }
            return false;
        }
    }

    /// <summary>当前视觉主题色(十六进制大写)。</summary>
    internal static string VisualThemeColor => _visualColor;

    // ================================================================= 注入缝(C5a)

    /// <summary>
    /// 【C5a 注入缝 · 请 Lead 裁决】分段 1 内嵌的账号页(mac 侧还没移植)。
    /// 由**页面自己**的 <c>[ModuleInitializer]</c> 或外壳接上(照 D-125/D-131 的"一次修在一处"口径);
    /// 没接时这一栏显示如实占位,不空白、不假装能用。
    /// </summary>
    internal static Func<PageBase>? AccountPageFactory { get; set; }

    /// <summary>【C5a 注入缝】分段 2 内嵌的 Java 页(mac 侧还没移植)。</summary>
    internal static Func<PageBase>? JavaPageFactory { get; set; }

    /// <summary>【C5a 注入缝】分段 4 内嵌的服务器页(mac 侧还没移植)。</summary>
    internal static Func<PageBase>? ServersPageFactory { get; set; }

    /// <summary>【C5a 注入缝】分段 5 内嵌的游戏时长页(mac 侧还没移植)。</summary>
    internal static Func<PageBase>? PlaytimePageFactory { get; set; }

    /// <summary>【C5a 注入缝】分段 6 内嵌的反馈页(mac 侧还没移植)。**注意**:Windows 是字段初始化
    /// (<c>private readonly FeedbackPage _feedback = new FeedbackPage()</c>),mac 侧改成"切到第 6 段时才建"。</summary>
    internal static Func<PageBase>? FeedbackPageFactory { get; set; }

    /// <summary>【C5a 注入缝】「重新运行启动向导」要跑的向导(mac 侧还没移植)。</summary>
    internal static Func<Task>? FirstRunWizardRunner { get; set; }

    /// <summary>
    /// 【C5a 注入缝 · D 类】写剪贴板(Windows 是静态的 <c>Clipboard.SetText</c>,Avalonia 11 没有那个入口)。
    /// 与 <c>CrashUi.ClipboardWriter</c> 同一模式:测试与离屏换掉它,**绝不碰真剪贴板**。
    /// </summary>
    internal static Func<Control, string, Task<bool>>? ClipboardWriter { get; set; }

    /// <summary>
    /// 【C5a 注入缝 · D 类】拉起一个新进程(Windows 的「立即重启」=<c>Process.Start(自身)</c> + <c>Shutdown()</c>)。
    /// 参数 = 可执行文件路径;返回 false 表示没拉起来(页面如实 Toast 失败)。测试里**绝不真起进程**。
    /// </summary>
    internal static Func<string, bool>? ProcessLauncher { get; set; }

    /// <summary>
    /// 【测试用】外观附加项(<c>wpf-theme.json</c>)的目录覆盖 —— 与 <c>PageLayoutStore.DirOverride</c> 同一套做法,
    /// 让用例把落盘点指到 <c>TestHome.Root</c> 之下。null = 走 <c>BridgeHost.FindRoot()</c>(生产路径)。
    /// </summary>
    internal static string? ExtrasDirOverride { get; set; }

    private static string ExtrasPath
    {
        get
        {
            string? over = ExtrasDirOverride;
            if (over is not null)
            {
                return Path.Combine(over, "wpf-theme.json");
            }

            string path;
            try
            {
                path = BridgeHost.FindRoot();
            }
            catch
            {
                path = AppContext.BaseDirectory;
            }
            return Path.Combine(path, "wpf-theme.json");
        }
    }

    private bool IsBuiltinAi => KeyOf(_aiMode) == "builtin";

    /// <summary>后端持久化的主题色同步进来(返回"真的变了")。</summary>
    internal static bool SyncPersistedThemeColor(string? color)
    {
        string? text = NormalizeColor(color);
        if (text == null)
        {
            return false;
        }
        bool result = !string.Equals(_visualColor, text, StringComparison.OrdinalIgnoreCase);
        _visualColor = text;
        if (!string.Equals(Extras.ThemeColor, text, StringComparison.OrdinalIgnoreCase))
        {
            Extras.ThemeColor = text;
            SaveExtras();
        }
        return result;
    }

    /// <summary>后端持久化的背景预设同步进来(返回"真的变了")。</summary>
    internal static bool SyncPersistedBackground(string? preset)
    {
        if (string.IsNullOrWhiteSpace(preset))
        {
            return false;
        }
        string text = NormalizeBackground(preset.Trim());
        bool result = text != _visualBackground;
        _visualBackground = text;
        if (Extras.Background != text)
        {
            Extras.Background = text;
            SaveExtras();
        }
        return result;
    }

    private static ThemeExtras LoadExtras()
    {
        try
        {
            if (File.Exists(ExtrasPath))
            {
                return JsonSerializer.Deserialize<ThemeExtras>(File.ReadAllText(ExtrasPath)) ?? new ThemeExtras();
            }
        }
        catch
        {
        }
        return new ThemeExtras();
    }

    private static void SaveExtras()
    {
        try
        {
            File.WriteAllText(ExtrasPath, JsonSerializer.Serialize(Extras, new JsonSerializerOptions
            {
                WriteIndented = true,
            }));
        }
        catch
        {
        }
    }

    /// <summary>【Windows 源】<c>:400-420</c>:带 key 的下拉(<c>Style = Ui.S("Combo")</c> → <see cref="StyleKeys.Apply"/>)。</summary>
    private static ComboBox KeyCombo(IEnumerable<(string Key, string Label)> items, double width = 200.0)
    {
        ComboBox comboBox = new ComboBox
        {
            Width = width,
        };
        StyleKeys.Apply(comboBox, "Combo");
        foreach ((string key, string label) in items)
        {
            comboBox.Items.Add(new Opt
            {
                Key = key,
                Label = I18n.L(label),
            });
        }
        if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
        return comboBox;
    }

    /// <summary>按 key 选中(找不到就退回第一项,与 Windows 一致)。</summary>
    private static void SelectKey(ComboBox c, string key)
    {
        foreach (object? item in c.Items)
        {
            if (item is Opt opt && opt.Key == key)
            {
                c.SelectedItem = item;
                return;
            }
        }
        if (c.Items.Count > 0)
        {
            c.SelectedIndex = 0;
        }
    }

    private static string KeyOf(ComboBox c)
    {
        return (c.SelectedItem as Opt)?.Key ?? "";
    }

    public SettingsPage()
    {
        // 【Avalonia 差异】Windows 分别接 Checked / Unchecked 两个事件;Avalonia 11.3 把这两个标记成
        // 过时(CS0618),等价物是 IsCheckedChanged + 判 IsChecked(与 C4 的处置同一口径)。
        _dark.IsCheckedChanged += delegate
        {
            if (!_sync)
            {
                ApplyDark(_dark.IsChecked == true);
            }
        };
        _bg.SelectionChanged += delegate
        {
            if (!_sync)
            {
                ApplyVisuals(_color, KeyOf(_bg));
            }
        };
        _flyDur.ValueChanged += delegate(object? _, RangeBaseValueChangedEventArgs e)
        {
            _flyDurLbl.Text = $"{(int)e.NewValue} ms";
        };
        _threads.ValueChanged += delegate(object? _, RangeBaseValueChangedEventArgs e)
        {
            _threadsLbl.Text = ((int)e.NewValue).ToString();
        };
        _segs.ValueChanged += delegate(object? _, RangeBaseValueChangedEventArgs e)
        {
            int num = (int)e.NewValue;
            _segsLbl.Text = ((num <= 1) ? I18n.L("关") : num.ToString());
        };
        _mem.ValueChanged += delegate(object? _, RangeBaseValueChangedEventArgs e)
        {
            _memLbl.Text = $"{(int)e.NewValue} MB";
        };
        _lang.SelectionChanged += delegate
        {
            if (!_sync)
            {
                PageBase.Run(PreviewLanguageAsync);
            }
        };
        _aiMode.SelectionChanged += delegate
        {
            SyncAiRows();
        };
        _aiKey.Tag = "sk-...";
        _aiBase.Tag = "https://api.openai.com/v1";
        // Windows 是 LostFocus(TextField 失焦)。Avalonia 的 TextBox 没有 LostFocus 事件(它有
        // LostFocus 只是作为路由事件名),等价物是 LostFocusEvent 的订阅 —— 用法与 Windows 一致。
        _aiBase.LostFocus += delegate
        {
            AutoFetchAiModels();
        };
        _aiKey.LostFocus += delegate
        {
            AutoFetchAiModels();
        };
        _cfReset = Ui.Btn(I18n.L("恢复内置"), BtnKind.Chip, delegate
        {
            _cfClear = true;
            _cfKey.Text = "";
            SyncSecretBoxes();
            QueueSave();
        });
        _aiKeyReset = Ui.Btn(I18n.L("清除"), BtnKind.Chip, delegate
        {
            _aiKeyClear = true;
            _aiKey.Text = "";
            SyncSecretBoxes();
            QueueSave();
        });
        _cfKey.TextChanged += delegate
        {
            if (!string.IsNullOrWhiteSpace(_cfKey.Text))
            {
                _cfClear = false;
            }
        };
        _aiKey.TextChanged += delegate
        {
            if (!string.IsNullOrWhiteSpace(_aiKey.Text))
            {
                _aiKeyClear = false;
            }
        };
        BuildSwatches();
        Button browseHome = Ui.Btn(I18n.L("浏览..."), BtnKind.Chip, delegate
        {
            string? text = Dlg.PickFile(I18n.L("网页 (*.html;*.htm)|*.html;*.htm|全部文件|*.*"), I18n.L("选择自定义主页"));
            if (text != null)
            {
                _homePath.Text = text;
            }
        });
        Grid grid = Ui.G(null, "*,Auto");
        grid.Add(_homePath, 0);
        grid.Add(browseHome.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
        Button browseDir = Ui.Btn(I18n.L("浏览..."), BtnKind.Chip, delegate
        {
            PageBase.Run(BrowseGameDirAsync, I18n.L("切换失败"));
        }, "\ue8b7");
        Grid grid2 = Ui.G(null, "*,Auto");
        grid2.Add(_gameDir, 0);
        grid2.Add(browseDir.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
        _checkUpdate = Ui.Btn(I18n.L("检查更新"), BtnKind.Chip, delegate
        {
            PageBase.Run(CheckUpdateAsync, I18n.L("检查更新失败"));
        }, "\ue72c");
        _clean = Ui.Btn(I18n.L("扫描并清理"), BtnKind.Chip, delegate
        {
            PageBase.Run(CleanAsync, I18n.L("清理失败"));
        }, "\uea99");
        _testAi = Ui.Btn(I18n.L("测试 AI 连接"), BtnKind.Chip, delegate
        {
            PageBase.Run(TestAiAsync, I18n.L("AI 连接失败"));
        }, "\ue99a");
        _aiModelFetch = Ui.Btn(I18n.L("刷新模型"), BtnKind.Chip, delegate
        {
            PageBase.Run(FetchAiModelsAsync, I18n.L("拉取模型列表失败"));
        }, "\ue72c");
        _rec = Ui.Btn(I18n.L("查看推荐"), BtnKind.Chip, delegate
        {
            PageBase.Run(RecommendAsync, I18n.L("检测失败"));
        }, "\ue945");
        Border border = Ui.Card(Rows(
            Ui.Field(I18n.L("深色模式"), _dark, I18n.L("立即生效")),
            Ui.Field(I18n.L("主题色"), _swatches, I18n.L("点一下就换")),
            Ui.Field(I18n.L("界面背景"), _bg, I18n.L("预设色调，立即生效")),
            Ui.Field(I18n.L("界面动画"), _motion, I18n.L("换页、进度条、悬浮动效")),
            Ui.Field(I18n.L("下载飞入动画"), _fly, I18n.L("安装时飞入侧栏任务列表")),
            Ui.Field(I18n.L("飞入动画时长"), SliderRow(_flyDur, _flyDurLbl), I18n.L("毫秒，建议 400–800"))));
        _bgInterval.ValueChanged += delegate(object? _, RangeBaseValueChangedEventArgs e)
        {
            _bgIntervalLbl.Text = I18n.L("{0} 分钟", (int)e.NewValue);
        };
        _bgBlur.ValueChanged += delegate(object? _, RangeBaseValueChangedEventArgs e)
        {
            _bgBlurLbl.Text = $"{(int)e.NewValue} px";
            if (!_sync)
            {
                Wallpaper.SetEffects((int)_bgBlur.Value, (int)_bgDim.Value);
            }
        };
        _bgDim.ValueChanged += delegate(object? _, RangeBaseValueChangedEventArgs e)
        {
            _bgDimLbl.Text = $"{(int)e.NewValue} %";
            if (!_sync)
            {
                Wallpaper.SetEffects((int)_bgBlur.Value, (int)_bgDim.Value);
            }
        };
        Button pickWall = Ui.Btn(I18n.L("选图片 / 视频..."), BtnKind.Chip, delegate
        {
            string? text = Dlg.PickFile(I18n.L("壁纸 (*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif;*.mp4;*.mkv;*.webm;*.mov)") + I18n.L("|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif;*.mp4;*.mkv;*.webm;*.mov|全部文件|*.*"), I18n.L("选择壁纸"));
            if (text != null)
            {
                _bgPath.Text = text;
                _bgFolder.Clear();
                PageBase.Run(ApplyWallpaperAsync, I18n.L("壁纸没能应用"));
            }
        }, "\ueb9f");
        Button pickWallFolder = Ui.Btn(I18n.L("选文件夹..."), BtnKind.Chip, delegate
        {
            string? text = Dlg.PickFolder(I18n.L("选择壁纸文件夹"));
            if (text != null)
            {
                _bgFolder.Text = text;
                PageBase.Run(ApplyWallpaperAsync, I18n.L("壁纸没能应用"));
            }
        }, "\ue8b7");
        Button clearWall = Ui.Btn(I18n.L("关闭壁纸"), BtnKind.Ghost, delegate
        {
            _bgPath.Clear();
            _bgFolder.Clear();
            PageBase.Run(ApplyWallpaperAsync, I18n.L("壁纸没能关闭"));
        }, "\ue711");
        _bgUndo = Ui.Btn(I18n.L("撤销上一次"), BtnKind.Chip, delegate
        {
            PageBase.Run(UndoWallpaperAsync, I18n.L("撤销失败"));
        }, "\ue72c");
        Button resetWall = Ui.Btn(I18n.L("恢复出厂"), BtnKind.Chip, delegate
        {
            PageBase.Run(ResetWallpaperAsync, I18n.L("恢复失败"));
        }, "\uea99");
        Grid grid3 = Ui.G(null, "*,Auto");
        grid3.Add(_bgPath, 0);
        grid3.Add(pickWall.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
        Grid grid4 = Ui.G(null, "*,Auto");
        grid4.Add(_bgFolder, 0);
        grid4.Add(pickWallFolder.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
        // 内置默认背景(Resources/wallpapers):点缩略图释放到数据目录 wallpapers/ 再走既有 ui_background 管线,
        // 因此撤销 / 恢复出厂 / 轮播等行为与手动选文件完全一致。
        // 【mac 差异】Windows 用 pack URI + BitmapImage;mac 走 C2c2 已落地的 Wallpaper 预设面
        // (Presets / ReleasePreset / OpenPreset)与 <see cref="ImageDecode"/> 解码,不自己新写解码。
        SPanel bgThumbs = Ui.H(8.0);
        foreach ((string bgFile, string bgLabel) in Wallpaper.Presets)
        {
            Border thumb = BuildPresetThumb(bgFile, bgLabel);
            bgThumbs.Children.Add(thumb);
        }
        Border border2 = Ui.Card(Ui.V(4.0, Ui.Small(I18n.L("图片或 mp4 动态壁纸；也可以直接把文件拖进窗口")).Wrap(), Rows(
            Ui.Field(I18n.L("预设背景"), bgThumbs, I18n.L("点一下就换，可再叠加模糊与遮罩")),
            Ui.Field(I18n.L("壁纸文件"), grid3, I18n.L("留空就不用壁纸。视频会静音循环播放")),
            Ui.Field(I18n.L("轮播文件夹"), grid4, I18n.L("设了文件夹就按间隔换图，优先于上面那张单图")),
            Ui.Field(I18n.L("随机顺序"), _bgShuffle, I18n.L("关掉则按文件名顺序轮")),
            Ui.Field(I18n.L("轮播间隔"), SliderRow(_bgInterval, _bgIntervalLbl), I18n.L("分钟")),
            Ui.Field(I18n.L("模糊"), SliderRow(_bgBlur, _bgBlurLbl), I18n.L("把壁纸推远一点，文字更好读；随拖随看")),
            Ui.Field(I18n.L("遮罩浓度"), SliderRow(_bgDim, _bgDimLbl), I18n.L("按当前主题压淡 / 压暗壁纸；随拖随看")),
            Ui.Field("", Ui.H(8.0, clearWall, _bgUndo, resetWall, _bgInfo.VCenter())))));
        Border border3 = Ui.Card(Ui.V(4.0, Ui.Small(I18n.L("把当前配色、背景、窗口与主页设置整包存下来，随时切回")).Wrap(), Rows(
            Ui.Field(I18n.L("已保存"), _themePack, I18n.L("从这里挑一个加载或删除")),
            Ui.Field("", Ui.H(7.0,
                Ui.Btn(I18n.L("保存为主题包"), BtnKind.Soft, delegate
                {
                    PageBase.Run(SaveThemeAsync, I18n.L("保存失败"));
                }, "\ue74e"),
                Ui.Btn(I18n.L("加载"), BtnKind.Chip, delegate
                {
                    PageBase.Run(LoadThemeAsync, I18n.L("加载失败"));
                }, "\ue790"),
                Ui.Btn(I18n.L("删除"), BtnKind.Ghost, delegate
                {
                    PageBase.Run(DeleteThemeAsync, I18n.L("删除失败"));
                }, "\ue74d"),
                Ui.Btn(I18n.L("导入"), BtnKind.Chip, delegate
                {
                    PageBase.Run(ImportThemeAsync, I18n.L("导入失败"));
                }, "\ue8b5"),
                Ui.Btn(I18n.L("导出"), BtnKind.Chip, delegate
                {
                    PageBase.Run(ExportThemeAsync, I18n.L("导出失败"));
                }, "\uede1")))), _themeInfo));
        Border border4 = Ui.Card(Rows(
            Ui.Field(I18n.L("下载并发线程数"), SliderRow(_threads, _threadsLbl), I18n.L("同时下载的文件数量")),
            Ui.Field(I18n.L("单文件分片数"), SliderRow(_segs, _segsLbl), I18n.L("8MB 以上的大文件拆成几段同时下；1 为关闭")),
            Ui.Field(I18n.L("文件下载源"), _src, I18n.L("自动测速，官方慢于 4 秒改走 BMCLAPI")),
            Ui.Field(I18n.L("社区资源源"), _comm, I18n.L("模组 / 整合包：MCIM 国内镜像，挂了可改官方")),
            Ui.Field(I18n.L("跟随系统代理"), _proxy, I18n.L("默认开。关掉才强制直连")),
            Ui.Field(I18n.L("下载限速 (KB/s)"), _limit.Left(), I18n.L("0 表示不限制")),
            Ui.Field("CurseForge Key", SecretRow(_cfKey, _cfReset), I18n.L("留空不修改；不填用内置 Key"))));
        Border border5 = Ui.Card(Rows(
            Ui.Field(I18n.L("游戏目录"), grid2, I18n.L("所有实例的落脚处；切换会校验可写性")),
            Ui.Field(I18n.L("默认内存"), SliderRow(_mem, _memLbl), I18n.L("新实例的默认 JVM 内存")),
            Ui.Field("", Ui.H(8.0, _rec, Ui.Small(I18n.L("按本机内存 / CPU 给一组推荐值")).VCenter())),
            Ui.Field(I18n.L("默认分辨率"), Ui.H(6.0, _w, Ui.Txt("×").VCenter(), _h), I18n.L("游戏窗口的默认宽高")),
            Ui.Field(I18n.L("默认实例"), _inst, I18n.L("安装与启动缺省使用的实例")),
            Ui.Field(I18n.L("新版本默认隔离"), _iso, I18n.L("安装新版本时写入，可在版本设置里改")),
            Ui.Field(I18n.L("内存回收器"), _gc, I18n.L("启动时写入 JVM，版本设置可覆盖")),
            Ui.Field(I18n.L("默认游戏窗口"), _win, I18n.L("可被版本设置覆盖")),
            Ui.Field(I18n.L("默认 JVM 参数"), _jvm, I18n.L("所有版本都会带上，版本设置可再追加")),
            Ui.Field(I18n.L("允许多开"), _multi, I18n.L("关掉则游戏已在运行时不再启动第二个"))));
        Border border6 = Ui.Card(Rows(
            Ui.Field(I18n.L("共享 libraries"), _shareLib, I18n.L("所有实例共享依赖库（节省空间，降低隔离性）")),
            Ui.Field(I18n.L("共享 assets 资源"), _shareAssets, I18n.L("所有实例共享资源文件（节省空间，降低隔离性）")),
            Ui.Field(I18n.L("空间清理"), Ui.H(8.0, _clean, _cleanInfo.VCenter()), I18n.L("扫描未引用的依赖库、残留 .part 与更新缓存，确认后再删"))));
        Grid aiBaseRow = Ui.Field(I18n.L("端点地址"), _aiBase, I18n.L("OpenAI 兼容的 base_url，到 /v1 为止"));
        Grid aiKeyRow = Ui.Field("API Key", SecretRow(_aiKey, _aiKeyReset), I18n.L("留空不修改；本地端点不需要可不填"));
        Grid aiModelRow = Ui.Field(I18n.L("模型"), Ui.H(8.0, _aiModel, _aiModelFetch.VCenter()), I18n.L("填好地址和令牌后自动拉取；拉不到可直接手填"));
        Grid aiCtxRow = Ui.Field(I18n.L("上下文窗口（token）"), _aiCtx, I18n.L("模型真实窗口未知时按 128k 保守压缩；实测后可改大"));
        Grid aiFallbackRow = Ui.Field(I18n.L("备用模型（可选）"), _aiFallback, I18n.L("主模型连续 429/5xx 时本回合自动切到它；留空不启用"));
        _aiCustomRows.AddRange(new Control[5] { aiBaseRow, aiKeyRow, aiModelRow, aiCtxRow, aiFallbackRow });
        Border border7 = Ui.Card(Ui.V(4.0, Ui.Small(I18n.L("对话里下游戏、装模组、读崩溃日志")).Wrap(), Rows(
            Ui.Field(I18n.L("接入方式"), _aiMode),
            aiBaseRow,
            aiKeyRow,
            aiModelRow,
            aiCtxRow,
            aiFallbackRow,
            Ui.Field(I18n.L("权限"), Ui.Muted(I18n.L("在 AI 对话界面的输入框上方设置"))),
            BuildVisionField(),
            BuildMcpField(),
            Ui.Field("", _testAi.Left()))));
        Border border8 = Ui.Card(Rows(
            Ui.Field(I18n.L("启动器可见性"), _vis, I18n.L("游戏启动后启动器窗口怎么处理")),
            Ui.Field(I18n.L("启动页主页"), _home, I18n.L("新闻、自定义 HTML 或留空")),
            Ui.Field(I18n.L("自定义主页"), grid, I18n.L("本地 .html 文件路径")),
            Ui.Field(I18n.L("自动检查更新"), _autoUpdate, I18n.L("启动时检查启动器更新")),
            Ui.Field(I18n.L("语言"), _lang, I18n.L("切换界面语言，重启后完全生效")),
            Ui.Field("", _langPreview),
            Ui.Field(I18n.L("启动向导"), Ui.Btn(I18n.L("重新运行"), BtnKind.Chip, delegate
            {
                PageBase.Run(OpenWizardAsync, I18n.L("向导没能打开"));
            }, "\ue7c1").Left(), I18n.L("目录、下载源、内存、隔离会再问一遍，最后一步还会指一遍容易错过的功能"))));
        WireAutoSave();
        Border generalCard = border;
        // 布局自定义:每个「分组小标题 + 卡片」包成一个区块(标题留在块内,重排/显隐时跟着走),
        // 块内间距对齐原 ScrollBody 的 10,默认顺序与可见性和旧版完全一致。
        _views[0] = new BlockHost("settings:general", new List<BlockSpec>
        {
            new BlockSpec { Id = "appearance", Title = I18n.L("外观"), Content = Ui.V(10.0, PTitle(I18n.L("通用")), Gt(I18n.L("外观")), generalCard) },
            new BlockSpec { Id = "wallpaper", Title = I18n.L("壁纸"), Content = Ui.V(10.0, Gt(I18n.L("壁纸")), border2) },
            new BlockSpec { Id = "themePack", Title = I18n.L("主题包"), Content = Ui.V(10.0, Gt(I18n.L("主题包")), border3) },
            new BlockSpec { Id = "launcher", Title = I18n.L("启动器"), Content = Ui.V(10.0, Gt(I18n.L("启动器")), border8) },
            new BlockSpec { Id = "game", Title = I18n.L("游戏"), Content = Ui.V(10.0, Gt(I18n.L("游戏")), border5) },
            new BlockSpec { Id = "storage", Title = I18n.L("存储"), Content = Ui.V(10.0, Gt(I18n.L("存储")), border6) },
            new BlockSpec { Id = "ai", Title = I18n.L("AI 助手"), Content = Ui.V(10.0, Gt(I18n.L("AI 助手")), border7) },
            new BlockSpec { Id = "memory", Title = I18n.L("记忆"), Content = Ui.V(10.0, Gt(I18n.L("记忆")), BuildMemoryCard()) },
            new BlockSpec { Id = "phoneSync", Title = I18n.L("手机同步"), Content = Ui.V(10.0, Gt(I18n.L("手机同步")), BuildPhoneSyncCard()) },
        }).Root;
        _views[3] = new BlockHost("settings:network", new List<BlockSpec>
        {
            new BlockSpec { Id = "network", Title = I18n.L("下载与网络"), Content = Ui.V(10.0, PTitle(I18n.L("下载与网络")), Gt(I18n.L("下载")), border4) },
        }).Root;
        SPanel nav = Ui.V(2.0,
            NavBtn(0, I18n.L("通用")),
            NavBtn(1, I18n.L("账号")),
            NavBtn(2, "Java"),
            NavBtn(3, I18n.L("下载与网络")),
            NavBtn(4, I18n.L("服务器")),
            NavBtn(5, I18n.L("游戏时长")),
            NavBtn(6, I18n.L("反馈与关于")),
            _saveState.M(12.0, 10.0, 0.0, 0.0));
        nav.Margin = new Thickness(12.0, 16.0, 0.0, 12.0);
        // 【C9a】两栏交给共用的 PageLayoutHost(Windows `SettingsPage.cs:753`:`"settings"` / `"nav"` / 168 + 右栏 `*`)。
        // 间距传 **0**:mac 切前是 `"168,*"`(两列、无 18px 间隔带),而 Windows 那条带又被它自己的
        // `_view.Margin = new Thickness(-18,0,0,0)`(`:754`)抵掉了 ⇒ **gap 0 的可见结果与 Windows 等价**,
        // 且让"默认渲染逐字节不变"这条硬口径继续成立。分隔条因此自带 **8px 命中宽度**(居中压在边界上,
        // 静止透明、细线不显示 ⇒ 零像素变化,但能拖、能双击复位、比例能落盘)。
        // 【per-site 间距登记 · D2 真机对照项】Windows 的 per-site 间距来自**未移植**的 `SplitCatalog`;
        // 等它落地后 `AccountPage`(gap 0)与本页(gap 0 + 命中宽 8)都要回来按 Windows 校正(会改变像素)。
        PageLayoutHost root = new PageLayoutHost("settings", "nav", new GridLength(168.0), new GridLength(1.0, GridUnitType.Star), gap: 0.0, splitterHitWidth: 8.0);
        root.SetLeft(nav);
        root.SetRight(_view);
        base.Content = root;
        Select(0);
    }

    // ================================================================= 左导航与内嵌分段

    /// <summary>左导航:0 通用 / 1 账号 / 2 Java / 3 下载与网络 / 4 服务器 / 5 游戏时长 / 6 反馈与关于。</summary>
    private readonly ToggleButton[] _nav = new ToggleButton[7];

    private readonly Control?[] _views = new Control?[7];

    private readonly ContentControl _view = new ContentControl
    {
        Focusable = false,
    };

    /// <summary>当前内嵌的子页(Windows 的 <c>_sub</c>);分段 0/3 没有子页。</summary>
    private PageBase? _sub;

    /// <summary>
    /// 各段内嵌子页的实例缓存(Windows 是 <c>_views[1] ??= new AccountPage()</c> —— mac 侧因为要经注入缝,
    /// 单独用一个数组存,**不能**和 <see cref="_views"/>(段视图)共用一个格子:
    /// 分段 6 的段视图是个 Grid(上标题卡 + 下内嵌页),两者共用一个格子会让 Grid 被内嵌页顶掉(本包实测踩到)。
    /// </summary>
    private readonly PageBase?[] _subs = new PageBase?[7];

    /// <summary>「反馈与关于」那一段的视图(Grid;懒建一次)。</summary>
    private Grid? _aboutView;

    private ToggleButton NavBtn(int i, string label)
    {
        ToggleButton b = new ToggleButton
        {
            Content = new TextBlock
            {
                Text = label,
                Width = 120.0,
            },
            Padding = new Thickness(12.0, 8.0, 12.0, 8.0),
            Margin = new Thickness(0.0),
        };
        StyleKeys.Apply(b, "Seg.Item");
        b.Click += delegate
        {
            Select(i);
        };
        _nav[i] = b;
        return b;
    }

    /// <summary>
    /// 切到第 <paramref name="i"/> 段。子页实例**按需**建(<see cref="SubPageFor"/>):
    /// Windows 里 1/2/4/6 是"切到才 new"、5 也是,只有 <c>_feedback</c> 是构造时就 new ——
    /// mac 侧因为那六个页面都还没移植,统一改成"切到才经工厂建"(A 类差异 1)。
    /// </summary>
    private void Select(int i)
    {
        for (int k = 0; k < 7; k++)
        {
            _nav[k].IsChecked = k == i;
        }
        PageBase? sub = SubPageFor(i);
        if (sub != _sub)
        {
            _sub?.OnHidden();
            _sub = sub;
            ShowSub();
        }
        _view.Content = ViewFor(i);
    }

    /// <summary>这一段的子页(没有 / 还没移植 → null)。结果缓存进 <see cref="_views"/>,与 Windows 的 <c>_views[i] ??= new ...</c> 同意图。</summary>
    private PageBase? SubPageFor(int i)
    {
        if (i is not (1 or 2 or 4 or 5 or 6))
        {
            return null;
        }
        if (_subs[i] is PageBase built)
        {
            return built;
        }
        Func<PageBase>? factory = i switch
        {
            1 => AccountPageFactory,
            2 => JavaPageFactory,
            4 => ServersPageFactory,
            5 => PlaytimePageFactory,
            _ => FeedbackPageFactory,
        };
        PageBase? page = factory?.Invoke();
        if (page != null)
        {
            _subs[i] = page;
        }
        return page;
    }

    /// <summary>这一段的视图(分段 0/3/6 是块宿主或组合,其余是子页;子页缺件时给如实占位)。</summary>
    private Control ViewFor(int i)
    {
        switch (i)
        {
            case 0:
            case 3:
                return _views[i]!;
            case 6:
                return _aboutView ??= BuildAboutView();
            default:
                return SubPageFor(i) ?? MissingSectionFor(i);
        }
    }

    /// <summary>「反馈与关于」视图:顶层是 Grid 双行(上标题卡 + 下反馈页),边距仍由内部自管(宿主边距传 0 防叠加)。</summary>
    private Grid BuildAboutView()
    {
        Grid about = Ui.G("Auto,*");
        about.Add(new BlockHost("settings:about", new List<BlockSpec>
        {
            new BlockSpec
            {
                Id = "about",
                Title = I18n.L("反馈与关于"),
                Content = Ui.V(10.0, PTitle(I18n.L("反馈与关于")), BuildAboutCard(), _checkUpdate.Right()).M(18.0, 14.0, 18.0, 0.0),
            },
        }, new Thickness(0.0)).Root, 0);
        about.Add(SubPageFor(6) ?? MissingSectionFor(6), 1);
        return about;
    }

    /// <summary>
    /// 【A 类差异 · 请 Lead 裁决】缺件分段的**如实占位**:说清这一栏内嵌的是哪个页面、它属后续工作包,
    /// 以及接上注入缝之后会自动出现。不是空白,也不假装能用(D-119 / D-134 的同一口径)。
    /// </summary>
    private static Control MissingSectionFor(int i)
    {
        string what = i switch
        {
            1 => I18n.L("账号"),
            2 => "Java",
            4 => I18n.L("服务器"),
            5 => I18n.L("游戏时长"),
            _ => I18n.L("反馈与关于"),
        };
        return Ui.Empty("\ue80a", I18n.L("「{0}」页还没移植", what), I18n.L("这一分段内嵌的是一个独立页面,属后续工作包;接上对应的注入缝后这里会自动显示"));
    }

    private void ShowSub()
    {
        PageBase? sub = _sub;
        if (sub != null && AppServices.Ready)
        {
            PageBase.Run(async delegate
            {
                await sub.EnsureLoadedAsync();
                sub.OnShown();
            });
        }
    }

    public override void OnShown()
    {
        SyncAppearanceControls();
        AttachThemeListener();
        ShowSub();
        // 【mac 差异】Windows 把「视觉状态刷新 / 记忆列表刷新」挂在 <c>base.Loaded</c> 上(控件进可视树各刷一次)。
        // mac 侧用本页的 OnShown(切到设置页时),语义一致且不依赖控件的 Loaded 时机。
        RefreshVisionStatus();
        RefreshMemoryList();
        SyncWallpaperInfo();
    }

    public override void OnHidden()
    {
        _sub?.OnHidden();
        DetachThemeListener();
        // Windows 的 <c>base.Unloaded</c>:离开设置页时把还没落盘的改动立刻存掉
        if (_saveTimer is not null && _saveTimer.IsEnabled)
        {
            _saveTimer.Stop();
            PageBase.Run(AutoSaveAsync, I18n.L("保存失败"));
        }
    }

    public override void OnEvent(BridgeEvent ev)
    {
        _sub?.OnEvent(ev);
    }

    private static TextBlock PTitle(string t)
    {
        TextBlock block = new TextBlock
        {
            Text = t,
            Margin = new Thickness(0.0, 0.0, 0.0, 2.0),
        };
        StyleKeys.Apply(block, "T.PageTitle");
        return block;
    }

    private static TextBlock Gt(string t)
    {
        TextBlock block = new TextBlock
        {
            Text = t,
        };
        StyleKeys.Apply(block, "T.GroupTitle");
        return block;
    }

    /// <summary>settings2 贴图:设置行 = 40px 高、上下行之间一条通到卡片两端的分隔线。</summary>
    private static SPanel Rows(params Control[] rows)
    {
        SPanel panel = Ui.V(0.0);
        for (int i = 0; i < rows.Length; i++)
        {
            if (i > 0)
            {
                Border sep = new Border
                {
                    Height = 1.0,
                    Margin = new Thickness(-12.0, 1.0, -12.0, 1.0),
                };
                sep.Dyn(Border.BackgroundProperty, "B.SegRail");
                panel.Children.Add(sep);
            }
            rows[i].MinHeight = 40.0;
            panel.Children.Add(rows[i]);
        }
        return panel;
    }

    private static Control SliderRow(Slider s, TextBlock label)
    {
        Grid grid = Ui.G(null, "*,Auto");
        grid.Add(s.VCenter(), 0);
        grid.Add(label.M(10.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
        return grid;
    }

    private static Control SecretRow(TextBox box, Button reset)
    {
        Grid grid = Ui.G(null, "*,Auto");
        grid.Add(box, 0);
        reset.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
        reset.VerticalAlignment = VerticalAlignment.Center;
        grid.Add(reset, 0, 1);
        return grid;
    }

    /// <summary>内置预设背景的缩略图(Windows 用 <c>pack://</c> + <c>BitmapImage</c>;mac 走 Wallpaper 预设面 + ImageDecode)。</summary>
    private Border BuildPresetThumb(string file, string label)
    {
        Image img = new Image
        {
            Width = 64.0,
            Height = 40.0,
            Stretch = Stretch.UniformToFill,
        };
        try
        {
            using Stream? stream = Wallpaper.OpenPreset(file);
            if (stream is not null)
            {
                using MemoryStream buffer = new MemoryStream();
                stream.CopyTo(buffer);
                img.Source = ImageDecode.Decode(buffer.ToArray(), 128);
            }
        }
        catch
        {
            // 预设资源缺失:缩略图空着,点了之后 ReleasePreset 会如实报"应用失败"(与 Windows 的 catch 同口径)。
        }
        Border thumb = new Border
        {
            CornerRadius = new CornerRadius(6.0),
            Child = img,
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = file,
            Margin = new Thickness(0.0, 0.0, 2.0, 0.0),
        };
        ToolTip.SetTip(thumb, I18n.L(label));
        thumb.PointerReleased += delegate
        {
            PickPreset(file);
        };
        return thumb;
    }

    /// <summary>
    /// 点一个内置预设背景缩略图(Windows 是 <c>thumb.MouseLeftButtonUp</c> 里的那一段:
    /// 释放到数据目录 → 填进壁纸文件框 → 走既有壁纸管线)。
    /// 【C5a 测试缝】同 <see cref="PickSwatch"/>:处理器体抽成 internal 成员(D-127)。
    /// </summary>
    internal void PickPreset(string file)
    {
        try
        {
            string dst = Wallpaper.ReleasePreset(file);
            if (string.IsNullOrEmpty(dst))
            {
                _bgInfo.Text = I18n.L("预设背景应用失败：") + file;
                return;
            }
            _bgPath.Text = dst;
            _bgFolder.Clear();
            PageBase.Run(ApplyWallpaperAsync, I18n.L("壁纸没能应用"));
        }
        catch (Exception ex)
        {
            _bgInfo.Text = I18n.L("预设背景应用失败：") + ex.Message;
        }
    }

    // ================================================================= AI 视觉 / 记忆 / MCP

    private TextBlock _visionStatus = Ui.Muted(I18n.L("检测中..."));

    private ComboBox _visionMode = null!;

    private bool _visionSyncing = true;

    /// <summary>【Windows 源】<c>:920-978</c>:识图模式行(自动检测 / 支持 / 不支持 + 重新检测)。</summary>
    private Grid BuildVisionField()
    {
        TextBlock status = Ui.Muted(I18n.L("检测中..."));
        _visionStatus = status;
        ComboBox mode = KeyCombo(new(string, string)[3]
        {
            ("auto", I18n.L("自动检测")),
            ("on", I18n.L("支持")),
            ("off", I18n.L("不支持")),
        }, 120.0);
        _visionMode = mode;
        SelectKey(mode, "auto");
        mode.SelectionChanged += delegate
        {
            if (_visionSyncing)
            {
                return;
            }
            PageBase.Run(async delegate
            {
                await PageBase.Api.TryCallAsync<JsonElement>("ai_vision_set_mode", new
                {
                    mode = KeyOf(mode),
                });
                await RefreshVisionStatusAsync(force: false);
            });
        };
        Button detect = Ui.Btn(I18n.L("重新检测"), BtnKind.Chip, delegate
        {
            PageBase.Run(() => RefreshVisionStatusAsync(force: true), I18n.L("检测失败"));
        });
        _visionSyncing = false;
        return Ui.Field(I18n.L("识图"), Ui.H(8.0, mode, detect.VCenter(), status.VCenter()), I18n.L("自动检测：先查公开模型能力库，查不到就发张小图实测；也可以手动指定"));
    }

    /// <summary>OnShown 触发的那次刷新(= Windows 挂在 <c>base.Loaded</c> 上的那一次)。</summary>
    private void RefreshVisionStatus()
    {
        if (_visionMode is null)
        {
            return;
        }
        PageBase.Run(() => RefreshVisionStatusAsync(force: false));
    }

    private async Task RefreshVisionStatusAsync(bool force)
    {
        TextBlock status = _visionStatus;
        status.Text = force ? I18n.L("检测中...") : status.Text;
        try
        {
            Dictionary<string, JsonElement> settings = (await PageBase.Api.TryCallAsync("get_settings", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>();
            Dictionary<string, object> dictionary = settings.ToDictionary<KeyValuePair<string, JsonElement>, string, object>(
                (KeyValuePair<string, JsonElement> kv) => kv.Key,
                (KeyValuePair<string, JsonElement> kv) => kv.Value);
            FillAiSettings(dictionary);
            JsonElement r = await PageBase.Api.CallAsync<JsonElement>("ai_vision_status", new
            {
                settings = dictionary,
                force,
            });
            if (r.TryGetProperty("mode", out JsonElement m))
            {
                _visionSyncing = true;
                SelectKey(_visionMode, m.GetString() ?? "auto");
                _visionSyncing = false;
            }
            status.Text = r.TryGetProperty("text", out JsonElement t) ? (t.GetString() ?? "") : "";
        }
        catch (Exception ex)
        {
            status.Text = I18n.L("检测失败：") + ex.Message;
        }
    }

    private SPanel _memoryList = null!;

    private TextBlock _memoryCount = Ui.Muted("");

    private Button _memoryClear = null!;

    /// <summary>【Windows 源】<c>:980-1051</c>:记忆卡片(列出 ai_memory.md,可编辑 / 删除 / 清空)。</summary>
    private Border BuildMemoryCard()
    {
        SPanel list = Ui.V(4.0);
        _memoryList = list;
        TextBlock count = Ui.Muted("");
        _memoryCount = count;
        RenderMemory();
        Button clear = Ui.Btn(I18n.L("清空全部"), BtnKind.Danger, delegate
        {
            PageBase.Run(async delegate
            {
                if (await Dlg.Confirm(I18n.L("清空全部记忆？"), I18n.L("助手记下的偏好、配置和修好过的问题都会删除，无法恢复。"), I18n.L("清空"), null, danger: true))
                {
                    int n = TipCL.Core.Launcher.Ai.FileMemory.Clear();
                    AppServices.Toast(I18n.L("已清空记忆"), I18n.L("删除了 {0} 条", n), ToastKind.Success);
                }
                RenderMemory();
            });
        });
        _memoryClear = clear;
        Button refresh = Ui.Btn(I18n.L("刷新"), BtnKind.Chip, delegate
        {
            RenderMemory();
        }, "\ue72c");
        return Ui.Card(Rows(
            Ui.Field(I18n.L("记忆"), Ui.H(8.0, refresh, clear), I18n.L("AI 助手跨对话记住的偏好和配置（数据目录 ai_memory.md，一行一条）")),
            count,
            list));
    }

    private void RefreshMemoryList()
    {
        if (_memoryList is not null)
        {
            RenderMemory();
        }
    }

    /// <summary>【Windows 源】<c>:986-1021</c>:重画记忆列表(逐条 = 日期 + 正文 + 编辑 / 删除)。</summary>
    private void RenderMemory()
    {
        _memoryList.Children.Clear();
        List<string> entries = TipCL.Core.Launcher.Ai.FileMemory.Entries();
        _memoryCount.Text = (entries.Count == 0)
            ? I18n.L("还没有记忆。对话里说出偏好或修好问题后，助手会自己记下")
            : I18n.L("共 {0} 条，新对话开始时会带给助手", entries.Count);
        if (_memoryClear is not null)
        {
            _memoryClear.IsEnabled = entries.Count > 0;
        }
        for (int i = 0; i < entries.Count; i++)
        {
            int index = i;
            string entry = entries[i];
            string body = TipCL.Core.Launcher.Ai.FileMemory.TextOf(entry);
            string date = (entry.Length > body.Length) ? entry.Substring(0, entry.Length - body.Length).Trim() : "";
            TextBlock text = Ui.Txt(body, 12.5, bold: false, "B.Ink").Wrap();
            Grid row = Ui.G(null, "Auto,*,Auto,Auto");
            row.Add(Ui.Txt(date, 11.5, bold: false, "B.InkFaint").M(0.0, 0.0, 10.0, 0.0).VCenter(), 0);
            row.Add(text.VCenter(), 0, 1);
            row.Add(Ui.Btn(I18n.L("编辑"), BtnKind.Chip, delegate
            {
                PageBase.Run(async delegate
                {
                    string? edited = await Dlg.Prompt(I18n.L("编辑记忆"), I18n.L("内容"), body);
                    if (edited != null && MemoryEntryStillThere(index, entry))
                    {
                        TipCL.Core.Launcher.Ai.FileMemory.Update(index, edited);
                    }
                    RenderMemory();
                });
            }).M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 2);
            row.Add(Ui.Btn(I18n.L("删除"), BtnKind.Ghost, delegate
            {
                if (MemoryEntryStillThere(index, entry))
                {
                    TipCL.Core.Launcher.Ai.FileMemory.RemoveAt(index);
                }
                RenderMemory();
            }).M(4.0, 0.0, 0.0, 0.0).VCenter(), 0, 3);
            _memoryList.Children.Add(Ui.Pane(row, "B.Paper2", 8.0, 8.0));
        }
    }

    /// <summary>【Windows 源】<c>:1022-1031</c>:下标还可能指到同一条吗(文件被改过就 Toast 并刷新,不动手)。</summary>
    private static bool MemoryEntryStillThere(int index, string entry)
    {
        List<string> now = TipCL.Core.Launcher.Ai.FileMemory.Entries();
        if (index < now.Count && now[index] == entry)
        {
            return true;
        }
        AppServices.Toast(I18n.L("记忆已变化"), I18n.L("文件刚被改过，已刷新列表"), ToastKind.Warning);
        return false;
    }

    private TextBlock _mcpStatus = Ui.Muted("");

    private CheckBox _mcpSwitch = null!;

    /// <summary>【Windows 源】<c>:1053-1083</c>:MCP Server 行(开关 + 复制地址 + 状态)。</summary>
    private Grid BuildMcpField()
    {
        TextBlock status = Ui.Muted("");
        _mcpStatus = status;
        CheckBox sw = Ui.Switch(TipCL.Core.Launcher.Ai.McpServer.Enabled && TipCL.Core.Launcher.Ai.McpServer.Running);
        _mcpSwitch = sw;
        sw.IsCheckedChanged += delegate
        {
            bool on = sw.IsChecked == true;
            bool running = TipCL.Core.Launcher.Ai.McpServer.SetEnabled(on);
            if (on && !running)
            {
                sw.IsChecked = false;
            }
            RefreshMcpStatus();
        };
        Button copy = Ui.Btn(I18n.L("复制地址"), BtnKind.Chip, null);
        copy.Click += delegate
        {
            PageBase.Run(() => CopyTextAsync(copy, TipCL.Core.Launcher.Ai.McpServer.Url));
        };
        RefreshMcpStatus();
        return Ui.Field("MCP Server", Ui.H(8.0, sw, copy.VCenter(), status.VCenter()), I18n.L("让 Claude、Cursor 等外部 AI 直接调用启动器的工具；只监听本机"));
    }

    private void RefreshMcpStatus()
    {
        _mcpStatus.Text = TipCL.Core.Launcher.Ai.McpServer.Running
            ? TipCL.Core.Launcher.Ai.McpServer.Url
            : (string.IsNullOrEmpty(TipCL.Core.Launcher.Ai.McpServer.LastError) ? I18n.L("未开启") : I18n.L("启动失败：") + TipCL.Core.Launcher.Ai.McpServer.LastError);
    }

    /// <summary>
    /// 【D 类差异 · 请 Lead 裁决】写剪贴板:接上 <see cref="ClipboardWriter"/> 就用它(测试 / 离屏),
    /// 否则走 Avalonia 的平台剪贴板(<c>TopLevel.Clipboard</c>)—— 失败一律当没发生(与 Windows 的 <c>try/catch</c> 同口径)。
    /// </summary>
    private static async Task CopyTextAsync(Control owner, string text)
    {
        Func<Control, string, Task<bool>>? sink = ClipboardWriter;
        if (sink is not null)
        {
            await sink(owner, text);
            return;
        }
        if (Dlg.SmokeActive)
        {
            // 离屏(--smoke)绝不碰真剪贴板(与 CrashUi 同一口径)
            return;
        }
        try
        {
            TopLevel? top = TopLevel.GetTopLevel(owner);
            if (top?.Clipboard is null)
            {
                return;
            }
            await top.Clipboard.SetTextAsync(text);
        }
        catch (Exception)
        {
        }
    }

    // ================================================================= 外观控件的双向同步

    private void BuildSwatches()
    {
        _swatches.Children.Clear();
        foreach ((string name, string hex) in SwatchPalette)
        {
            bool selectedSwatch = string.Equals(_color, hex, StringComparison.OrdinalIgnoreCase);
            Border border = new Border
            {
                Width = 26.0,
                Height = 26.0,
                CornerRadius = new CornerRadius(10.0),
                Padding = new Thickness(3.0),
                BorderThickness = new Thickness(selectedSwatch ? 2 : 0),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            border.Dyn(Border.BorderBrushProperty, "B.AccentDeep");
            border.Child = new Border
            {
                CornerRadius = new CornerRadius(8.0),
                Background = new SolidColorBrush(Parse(hex)),
            };
            ToolTip.SetTip(border, I18n.L(name));
            string pick = hex;
            border.PointerReleased += delegate
            {
                PickSwatch(pick);
            };
            _swatches.Children.Add(border);
        }
    }

    /// <summary>
    /// 点一个主题色色块(Windows 是 <c>border.MouseLeftButtonUp</c> 里的那一段)。
    /// 【C5a 测试缝】事件处理器只留薄壳、处理器体抽成 internal 成员 —— 同 D-115 / D-127 的口径:
    /// 用例不构造 <c>PointerReleasedEventArgs</c>(它的构造函数在 Avalonia 11 里不公开,
    /// 硬 <c>RaiseEvent</c> 一个 <c>RoutedEventArgs</c> 会 InvalidCastException,本包实测踩到)。
    /// </summary>
    internal void PickSwatch(string hex)
    {
        _color = hex;
        BuildSwatches();
        ApplyVisuals(_color, KeyOf(_bg));
        QueueSave();
    }

    /// <summary>【Windows 源】<c>:1123-1147</c>:外壳改了主题 / 主题色时把本页的控件同步过来(不再反向触发)。</summary>
    private void SyncAppearanceControls()
    {
        bool sync = _sync;
        _sync = true;
        try
        {
            if (_dark.IsChecked != PaletteOverride.IsDarkNow)
            {
                _dark.IsChecked = PaletteOverride.IsDarkNow;
            }
            if (!string.Equals(_color, _visualColor, StringComparison.OrdinalIgnoreCase))
            {
                _color = _visualColor;
                BuildSwatches();
            }
            if (KeyOf(_bg) != _visualBackground)
            {
                SelectKey(_bg, _visualBackground);
            }
        }
        finally
        {
            _sync = sync;
        }
    }

    // 【mac 差异】Windows 在构造函数里 <c>App.ThemeVisualsChanged += SyncAppearanceControls;</c> 且**永不摘**。
    // mac 用 Avalonia 的 Application.ActualThemeVariantChanged,并且在本页 OnShown/OnHidden 之间挂/摘 ——
    // 页面是外壳缓存的长命对象,但测试里会造出很多个实例,静态事件挂着它们会把旧控件一起留在内存里。
    private bool _themeListenerAttached;

    private void AttachThemeListener()
    {
        if (_themeListenerAttached || Application.Current is not { } app)
        {
            return;
        }
        app.ActualThemeVariantChanged += OnThemeVariantChanged;
        _themeListenerAttached = true;
    }

    private void DetachThemeListener()
    {
        if (!_themeListenerAttached || Application.Current is not { } app)
        {
            return;
        }
        app.ActualThemeVariantChanged -= OnThemeVariantChanged;
        _themeListenerAttached = false;
    }

    private void OnThemeVariantChanged(object? sender, EventArgs e)
    {
        SyncAppearanceControls();
    }

    private void SyncAiRows()
    {
        foreach (Control row in _aiCustomRows)
        {
            row.IsVisible = !IsBuiltinAi;
        }
        if (!_sync)
        {
            AutoFetchAiModels();
        }
    }

    private string AiModeWire()
    {
        if (!IsBuiltinAi)
        {
            return "custom";
        }
        return "builtin";
    }

    /// <summary>把界面上的 AI 接入设置写进要发给后端的设置字典(测试连接、拉模型、视觉检测共用)。</summary>
    private void FillAiSettings(Dictionary<string, object> dictionary)
    {
        dictionary["ai_mode"] = AiModeWire();
        dictionary["ai_base_url"] = _aiBase.Text?.Trim() ?? "";
        dictionary["ai_api_key"] = _aiKey.Text?.Trim() ?? "";
        dictionary["ai_model"] = AiModelValue();
    }

    private static string AiModeFromBackend(Dictionary<string, JsonElement> s)
    {
        if (!(GetStr(s, "ai_mode", "builtin").Trim().ToLowerInvariant() == "custom"))
        {
            return "builtin";
        }
        return "custom";
    }

    /// <summary>自定义模式下地址或令牌变了就自动拉一次模型列表;内置模式模型固定,不拉。</summary>
    private void AutoFetchAiModels()
    {
        if (IsBuiltinAi)
        {
            return;
        }
        string text = _aiBase.Text?.Trim() ?? "";
        if (text.Length == 0)
        {
            return;
        }
        string text2 = text + "\n" + (_aiKey.Text?.Trim() ?? "");
        if (!(text2 == _aiFetchedFor))
        {
            _aiFetchedFor = text2;
            PageBase.Run(() => FetchAiModelsAsync(manual: false));
        }
    }

    private string AiModelValue()
    {
        string? text = _aiModel.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            return text;
        }
        return _aiModel.SelectedItem as string ?? "";
    }

    private void SetAiModel(string? name)
    {
        string text = name?.Trim() ?? "";
        if (text.Length != 0)
        {
            if (!_aiModel.Items.Contains(text))
            {
                _aiModel.Items.Add(text);
            }
            _aiModel.SelectedItem = text;
            _aiModel.Text = text;
        }
    }

    private Task FetchAiModelsAsync()
    {
        return FetchAiModelsAsync(manual: true);
    }

    private async Task FetchAiModelsAsync(bool manual)
    {
        if (IsBuiltinAi)
        {
            return;
        }
        Dictionary<string, JsonElement> obj = (await PageBase.Api.TryCallAsync("get_settings", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>();
        Dictionary<string, object> dictionary = new Dictionary<string, object>();
        foreach (KeyValuePair<string, JsonElement> item in obj)
        {
            dictionary[item.Key] = item.Value;
        }
        FillAiSettings(dictionary);
        AiModelList? aiModelList;
        using (manual ? Dlg.Busy(I18n.L("正在拉取模型列表...")) : null)
        {
            aiModelList = await PageBase.Api.TryCallAsync<AiModelList>("ai_list_models", new
            {
                settings = dictionary,
            });
        }
        if (aiModelList == null)
        {
            PageBase.Toast(I18n.L("拉取失败"), I18n.L("没能从网关取到模型列表，可直接手填模型名"), ToastKind.Warning);
            return;
        }
        string text = AiModelValue();
        _aiModel.Items.Clear();
        foreach (string model in aiModelList.Models)
        {
            if (!string.IsNullOrWhiteSpace(model))
            {
                _aiModel.Items.Add(model);
            }
        }
        if (text.Length > 0)
        {
            SetAiModel(text);
        }
        else if (_aiModel.Items.Count > 0)
        {
            _aiModel.SelectedIndex = 0;
        }
        if (aiModelList.Ok && aiModelList.Models.Count > 0)
        {
            if (!manual)
            {
                return;
            }
            PageBase.Toast(I18n.L("模型列表已刷新"), I18n.L("共 {0} 个可用模型", aiModelList.Models.Count), ToastKind.Success);
        }
        else
        {
            PageBase.Toast(I18n.L("没取到模型"), string.IsNullOrEmpty(aiModelList.Error) ? I18n.L("网关没返回模型列表，可直接手填模型名") : aiModelList.Error, ToastKind.Warning);
        }
    }

    // ================================================================= 加载

    protected override async Task LoadAsync()
    {
        _sync = true;
        try
        {
            Dictionary<string, JsonElement>? dictionary = await PageBase.Api.TryCallAsync<Dictionary<string, JsonElement>>("get_settings");
            bool backendDark = PaletteOverride.IsDarkNow;
            if (dictionary != null)
            {
                if (dictionary.TryGetValue("ui_dark", out JsonElement value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
                {
                    backendDark = value.GetBoolean();
                }
                _dark.IsChecked = backendDark;
                _threads.Value = Clamp.Of(GetInt(dictionary, "download_threads", 8), 1, 64);
                _segs.Value = Clamp.Of(GetInt(dictionary, "download_segments", 4), 1, 16);
                _mem.Value = Clamp.Of(GetInt(dictionary, "default_memory_mb", 4096), 512, 32768);
                if (dictionary.TryGetValue("default_resolution", out JsonElement value2) && value2.ValueKind == JsonValueKind.Array && value2.GetArrayLength() >= 2)
                {
                    _w.Text = value2[0].ToString();
                    _h.Text = value2[1].ToString();
                }
                _limit.Text = GetInt(dictionary, "download_limit_kbps", 0).ToString();
                SelectKey(_src, GetStr(dictionary, "download_source", "auto"));
                SelectKey(_comm, GetStr(dictionary, "community_source", "auto"));
                _proxy.IsChecked = GetBool(dictionary, "use_system_proxy", def: true);
                SelectKey(_iso, GetStr(dictionary, "default_isolation", "none"));
                SelectKey(_gc, GetStr(dictionary, "gc_preset", "auto"));
                SelectKey(_vis, GetStr(dictionary, "launcher_visibility", "keep"));
                SelectKey(_home, GetStr(dictionary, "homepage_mode", "news"));
                SelectKey(_win, GetStr(dictionary, "window_mode", "window"));
                _homePath.Text = GetStr(dictionary, "custom_homepage");
                _jvm.Text = GetStr(dictionary, "default_jvm_args");
                _shareLib.IsChecked = GetBool(dictionary, "share_libraries");
                _shareAssets.IsChecked = GetBool(dictionary, "share_assets");
                _autoUpdate.IsChecked = GetBool(dictionary, "auto_check_update", def: true);
                ApplySecretState(dictionary);
                SelectKey(_aiMode, AiModeFromBackend(dictionary));
                _aiBase.Text = GetStr(dictionary, "ai_base_url");
                SetAiModel(GetStr(dictionary, "ai_model"));
                _aiCtx.Text = GetStr(dictionary, "ai_context_window", "131072");
                _aiFallback.Text = GetStr(dictionary, "ai_fallback_model");
                _aiConfirm.IsChecked = GetBool(dictionary, "ai_confirm_writes", def: true);
                _gameDir0 = GetStr(dictionary, "game_dir");
                if (string.IsNullOrEmpty(_gameDir0))
                {
                    _gameDir0 = GetStr(dictionary, "root");
                }
                _gameDir.Text = _gameDir0;
                _bgPath.Text = GetStr(dictionary, "ui_background");
                _bgFolder.Text = GetStr(dictionary, "ui_background_folder");
                _bgShuffle.IsChecked = GetBool(dictionary, "ui_background_shuffle");
                _bgInterval.Value = Clamp.Of(GetInt(dictionary, "ui_background_interval", 10), 1, 120);
                _bgBlur.Value = Clamp.Of(GetInt(dictionary, "ui_background_blur", 0), 0, 40);
                _bgDim.Value = Clamp.Of(GetInt(dictionary, "ui_background_dim", 0), 0, 80);
                _bgIntervalLbl.Text = I18n.L("{0} 分钟", (int)_bgInterval.Value);
                _bgBlurLbl.Text = $"{(int)_bgBlur.Value} px";
                _bgDimLbl.Text = $"{(int)_bgDim.Value} %";
                SyncPersistedThemeColor(GetStr(dictionary, "theme_color"));
                SyncPersistedBackground(GetStr(dictionary, "ui_bg_preset"));
            }
            else
            {
                _dark.IsChecked = PaletteOverride.IsDarkNow;
            }
            SyncWallpaperInfo();
            SyncAiRows();
            AutoFetchAiModels();
            List<InstanceInfo> source = (await PageBase.Api.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>();
            _inst.Fill(source.Select((InstanceInfo i) => i.Name));
            string text = await PageBase.Api.TryCallAsync("get_setting", new Dictionary<string, object>
            {
                ["key"] = "default_instance",
                ["default"] = "",
            }, "");
            if (!string.IsNullOrEmpty(text) && _inst.Items.Contains(text))
            {
                _inst.SelectedItem = text;
            }
            _multi.IsChecked = await PageBase.Api.TryCallAsync("allow_multi_instance", null, fallback: false);
            Dictionary<string, string>? dictionary2 = await PageBase.Api.TryCallAsync<Dictionary<string, string>>("available_languages");
            if (dictionary2 != null && dictionary2.Count > 0)
            {
                _lang.Items.Clear();
                foreach (KeyValuePair<string, string> item in dictionary2)
                {
                    _lang.Items.Add(new Opt
                    {
                        Key = item.Key,
                        Label = item.Value,
                    });
                }
            }
            _lang0 = (await PageBase.Api.TryCallAsync("get_language", null, "")) ?? "";
            SelectKey(_lang, _lang0);
            _motion.IsChecked = Motion.UserEnabled;
            _fly.IsChecked = Extras.FlyAnimation;
            _flyDur.Value = Clamp.Of(Extras.FlyDurationMs, 200, 1200);
            _flyDurLbl.Text = $"{(int)_flyDur.Value} ms";
            _color = IsValidColor(_visualColor) ? _visualColor : DefaultColor;
            BuildSwatches();
            _visualBackground = NormalizeBackground(Extras.Background);
            SelectKey(_bg, _visualBackground);
            if (backendDark != PaletteOverride.IsDarkNow)
            {
                ApplyDark(backendDark);
            }
            else
            {
                ApplyVisuals();
            }
        }
        finally
        {
            _sync = false;
        }
        _loadedNow = true;
        _wallpaperSig = WallpaperSig();
        await ReloadThemesAsync();
    }

    // ── 自动保存:控件一改就排一次保存(500ms 防抖,拖滑块不会狂写),保存即生效 ──

    private readonly TextBlock _saveState = Ui.Small("");

    private DispatcherTimer? _saveTimer;

    private bool _loadedNow;

    private bool _saving;

    private bool _savePending;

    private string _wallpaperSig = "";

    private void WireAutoSave()
    {
        foreach (CheckBox item in new CheckBox[10] { _dark, _motion, _fly, _proxy, _shareLib, _shareAssets, _multi, _autoUpdate, _aiConfirm, _bgShuffle })
        {
            item.IsCheckedChanged += delegate
            {
                QueueSave();
            };
        }
        foreach (ComboBox item2 in new ComboBox[12] { _bg, _src, _comm, _inst, _iso, _gc, _win, _vis, _home, _lang, _aiMode, _aiModel })
        {
            item2.SelectionChanged += delegate
            {
                QueueSave();
            };
        }
        _aiModel.LostFocus += delegate
        {
            QueueSave();
        };
        foreach (Slider item3 in new Slider[7] { _flyDur, _threads, _segs, _mem, _bgInterval, _bgBlur, _bgDim })
        {
            item3.ValueChanged += delegate
            {
                QueueSave();
            };
        }
        // 文本框打字时不存,离开输入框(或回车)再存,免得存进半截路径
        foreach (TextBox item4 in new TextBox[13] { _limit, _w, _h, _jvm, _homePath, _gameDir, _aiBase, _aiKey, _aiCtx, _aiFallback, _cfKey, _bgPath, _bgFolder })
        {
            item4.LostFocus += delegate
            {
                QueueSave();
            };
            TextBox box = item4;
            item4.KeyDown += delegate(object? _, KeyEventArgs e)
            {
                if (e.Key == Key.Enter)
                {
                    QueueSave();
                }
            };
        }
    }

    /// <summary>界面有改动时调用:防抖后自动保存。加载中(_sync)或页面还没加载完不触发。</summary>
    private void QueueSave()
    {
        if (_sync || !_loadedNow)
        {
            return;
        }
        if (_saveTimer == null)
        {
            _saveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500.0),
            };
            _saveTimer.Tick += delegate
            {
                _saveTimer.Stop();
                PageBase.Run(AutoSaveAsync, I18n.L("保存失败"));
            };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
        _saveState.Text = I18n.L("保存中...");
    }

    private async Task AutoSaveAsync()
    {
        if (_saving)
        {
            _savePending = true;
            return;
        }
        _saving = true;
        try
        {
            do
            {
                _savePending = false;
                await SaveAsync(auto: true);
            }
            while (_savePending);
            _saveState.Text = I18n.L("已自动保存");
        }
        catch
        {
            _saveState.Text = I18n.L("保存失败");
            throw;
        }
        finally
        {
            _saving = false;
        }
    }

    private string WallpaperSig()
    {
        return string.Join("|", _bgPath.Text?.Trim() ?? "", _bgFolder.Text?.Trim() ?? "", _bgShuffle.IsChecked == true, (int)_bgInterval.Value);
    }

    private Task SaveAsync()
    {
        return SaveAsync(auto: false);
    }

    /// <summary>
    /// C5a 测试缝(同 D-115 / D-127 的口径:事件处理器只留薄壳,处理器体抽成 internal 成员):
    /// 立刻走一次"非自动"保存 —— 真实路径由 500ms 防抖的 <see cref="QueueSave"/> 起头,
    /// 而离屏(headless)的 <c>DispatcherTimer</c> 在那条泵里一次都不 Tick(D-129 实测),
    /// 用例因此直接驱动保存体,断言的是同一套 save_settings 载荷。
    /// </summary>
    internal Task SaveNowAsync()
    {
        return SaveAsync(auto: false);
    }

    private async Task SaveAsync(bool auto)
    {
        string typed = _gameDir.Text?.Trim() ?? "";
        if (typed.Length > 0 && typed != _gameDir0)
        {
            try
            {
                _gameDir0 = (await PageBase.Api.CallAsync<string>("set_game_dir", new
                {
                    path = typed,
                })) ?? typed;
                _gameDir.Text = _gameDir0;
            }
            catch (Exception ex)
            {
                _gameDir.Text = _gameDir0;
                PageBase.Toast(I18n.L("游戏目录无效"), ex.Message, ToastKind.Error);
                Motion.Shake(_gameDir);
                return;
            }
        }
        bool dark = _dark.IsChecked == true;
        Dictionary<string, object> dictionary = new Dictionary<string, object>();
        dictionary["ui_dark"] = dark;
        dictionary["download_threads"] = (int)_threads.Value;
        dictionary["download_segments"] = (int)_segs.Value;
        dictionary["default_memory_mb"] = (int)_mem.Value;
        dictionary["default_resolution"] = new int[2]
        {
            Int(_w.Text, 854),
            Int(_h.Text, 480),
        };
        dictionary["download_source"] = KeyOf(_src);
        dictionary["community_source"] = KeyOf(_comm);
        dictionary["use_system_proxy"] = _proxy.IsChecked == true;
        dictionary["download_limit_kbps"] = Int(_limit.Text, 0);
        dictionary["default_isolation"] = KeyOf(_iso);
        dictionary["default_jvm_args"] = _jvm.Text?.Trim() ?? "";
        dictionary["gc_preset"] = KeyOf(_gc);
        dictionary["launcher_visibility"] = KeyOf(_vis);
        dictionary["homepage_mode"] = KeyOf(_home);
        dictionary["custom_homepage"] = _homePath.Text?.Trim() ?? "";
        dictionary["window_mode"] = KeyOf(_win);
        dictionary["auto_check_update"] = _autoUpdate.IsChecked == true;
        dictionary["share_libraries"] = _shareLib.IsChecked == true;
        dictionary["share_assets"] = _shareAssets.IsChecked == true;
        dictionary["default_instance"] = _inst.Str();
        dictionary["ai_mode"] = AiModeWire();
        dictionary["ai_base_url"] = _aiBase.Text?.Trim() ?? "";
        dictionary["ai_model"] = AiModelValue();
        dictionary["ai_context_window"] = int.TryParse(_aiCtx.Text?.Trim(), out int result) ? Clamp.Of(result, 8192, 2000000) : 131072;
        dictionary["ai_fallback_model"] = _aiFallback.Text?.Trim() ?? "";
        dictionary["ui_background"] = _bgPath.Text?.Trim() ?? "";
        dictionary["ui_background_folder"] = _bgFolder.Text?.Trim() ?? "";
        dictionary["ui_background_shuffle"] = _bgShuffle.IsChecked == true;
        dictionary["ui_background_interval"] = (int)_bgInterval.Value;
        dictionary["ui_background_blur"] = (int)_bgBlur.Value;
        dictionary["ui_background_dim"] = (int)_bgDim.Value;
        dictionary["theme_color"] = _color;
        dictionary["ui_bg_preset"] = KeyOf(_bg);
        dictionary["ui_motion"] = _motion.IsChecked == true;
        dictionary["ui_fly_animation"] = _fly.IsChecked == true;
        dictionary["ui_fly_duration_ms"] = (int)_flyDur.Value;
        string text = _cfKey.Text?.Trim() ?? "";
        if (text.Length > 0)
        {
            dictionary["curseforge_api_key"] = text;
        }
        else if (_cfClear)
        {
            dictionary["curseforge_api_key_clear"] = true;
        }
        // 切回内置网关时保留自定义地址和令牌,下次切回来不用重填
        string text2 = _aiKey.Text?.Trim() ?? "";
        if (text2.Length > 0)
        {
            dictionary["ai_api_key"] = text2;
        }
        else if (_aiKeyClear)
        {
            dictionary["ai_api_key_clear"] = true;
        }
        await PageBase.Api.CallAsync<object>("save_settings", new
        {
            data = dictionary,
        });
        Dictionary<string, JsonElement>? dictionary3 = await PageBase.Api.TryCallAsync<Dictionary<string, JsonElement>>("get_settings");
        if (dictionary3 != null)
        {
            ApplySecretState(dictionary3);
        }
        await PageBase.Api.CallAsync<object>("set_multi_instance", new
        {
            allow = _multi.IsChecked == true,
        });
        string lang = KeyOf(_lang);
        bool langChanged = !string.IsNullOrEmpty(lang) && lang != _lang0;
        if (langChanged)
        {
            await PageBase.Api.TryCallAsync<object>("set_language", new
            {
                lang,
            });
            _lang0 = lang;
        }
        Extras.ThemeColor = _color;
        Extras.Background = KeyOf(_bg);
        Extras.FlyAnimation = _fly.IsChecked == true;
        Extras.FlyDurationMs = (int)_flyDur.Value;
        SaveExtras();
        if (dark != PaletteOverride.IsDarkNow)
        {
            ApplyDark(dark);
        }
        Motion.Enabled = _motion.IsChecked == true;
        ApplyVisuals();
        // 只有壁纸文件 / 文件夹 / 轮播参数变了才重载,免得每次自动保存都让视频壁纸闪一下
        string sig = WallpaperSig();
        if (!auto || sig != _wallpaperSig)
        {
            _wallpaperSig = sig;
            await Wallpaper.ReloadAsync();
            SyncWallpaperInfo();
        }
        PageBase.Win?.SaveUiPrefs();
        if (!auto)
        {
            // Windows 是 AppServices.Window?.Toast(...)(只有外壳在时才提示);mac 的 AppServices.Window 是
            // Avalonia 的 Window(没有 Toast 成员),等价写法 = 判窗口在不在,再走 AppServices.Toast。
            if (AppServices.Window is not null)
            {
                AppServices.Toast(I18n.L("设置已保存"), I18n.L("外观与动效已立即生效"), ToastKind.Success);
            }
        }
        if (langChanged)
        {
            await OfferLanguageRestartAsync();
        }
    }

    private async Task OfferLanguageRestartAsync()
    {
        if (!(await Dlg.Confirm(I18n.L("语言已切换"), I18n.L("界面文字要重启启动器才会全部变成新语言。是否现在重启？"), I18n.L("立即重启"), I18n.L("稍后"))))
        {
            PageBase.Toast(I18n.L("语言已切换"), I18n.L("下次启动启动器后界面才会变成新语言"));
            return;
        }
        try
        {
            string text = Assembly.GetEntryAssembly()?.Location ?? "";
            if (string.IsNullOrEmpty(text))
            {
                text = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            }
            if (!RestartSelf(text))
            {
                throw new InvalidOperationException("拉起新进程失败");
            }
        }
        catch (Exception ex)
        {
            PageBase.Toast(I18n.L("重启失败"), I18n.L("无法拉起新进程，请手动关闭后重新打开") + "\n" + ex.Message, ToastKind.Error);
        }
    }

    /// <summary>
    /// 【D 类差异 · 请 Lead 裁决】Windows 的「立即重启」= <c>Process.Start(new ProcessStartInfo(自身){UseShellExecute=true})</c>
    /// 然后 <c>Application.Current.Shutdown()</c>。mac 侧:
    /// <list type="bullet">
    /// <item>起进程走注入缝 <see cref="ProcessLauncher"/>(接了就用它 —— 测试与离屏**绝不真起进程**);</item>
    /// <item>退出走 Avalonia 的生命周期(<c>IClassicDesktopStyleApplicationLifetime.Shutdown</c>);
    ///   离屏 / headless 没有桌面生命周期 ⇒ 只起进程、不退出(如实,不假装)。</item>
    /// </list>
    /// **这条只有真机能完整验证**(PROGRESS 的未验证项里写了)。
    /// </summary>
    private static bool RestartSelf(string exePath)
    {
        Func<string, bool>? launcher = ProcessLauncher;
        bool started;
        if (launcher is not null)
        {
            started = launcher(exePath);
        }
        else
        {
            if (string.IsNullOrEmpty(exePath))
            {
                return false;
            }
            Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory,
            });
            started = true;
        }
        if (started && Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        return started;
    }

    private async Task ApplyWallpaperAsync()
    {
        await PageBase.Api.CallAsync<object>("save_settings", new
        {
            data = new Dictionary<string, object>
            {
                ["ui_background"] = _bgPath.Text?.Trim() ?? "",
                ["ui_background_folder"] = _bgFolder.Text?.Trim() ?? "",
                ["ui_background_shuffle"] = _bgShuffle.IsChecked == true,
                ["ui_background_interval"] = (int)_bgInterval.Value,
                ["ui_background_blur"] = (int)_bgBlur.Value,
                ["ui_background_dim"] = (int)_bgDim.Value,
            },
        });
        await Wallpaper.ReloadAsync();
        SyncWallpaperInfo();
    }

    private void SyncWallpaperInfo()
    {
        PageBase.Run(SyncWallpaperInfoAsync);
    }

    private async Task SyncWallpaperInfoAsync()
    {
        List<string> history = (await PageBase.Api.TryCallAsync("background_history", null, new List<string>())) ?? new List<string>();
        bool isEnabled = await PageBase.Api.TryCallAsync("can_undo_background", null, fallback: false);
        _bgUndo.IsEnabled = isEnabled;
        ToolTip.SetTip(_bgUndo, history.Count == 0 ? null : I18n.L("上一张：{0}", history[history.Count - 1]));
        if (!string.IsNullOrEmpty(Wallpaper.Error))
        {
            _bgInfo.Text = Wallpaper.Error;
            return;
        }
        string text = _bgFolder.Text?.Trim() ?? "";
        string text2 = _bgPath.Text?.Trim() ?? "";
        string text3 = (text.Length > 0)
            ? I18n.L("正在轮播文件夹里的图片")
            : ((text2.Length == 0) ? I18n.L("当前没有壁纸，界面走主题底色") : (FileKinds.IsVideo(text2) ? I18n.L("当前是动态壁纸（静音循环）") : I18n.L("当前是静态背景图")));
        _bgInfo.Text = (history.Count == 0) ? text3 : I18n.L("{0} · 历史 {1} 张可退", text3, history.Count);
    }

    private async Task UndoWallpaperAsync()
    {
        BackgroundState? prev = await PageBase.Api.CallAsync<BackgroundState>("undo_background");
        if (prev != null)
        {
            _sync = true;
            _bgPath.Text = prev.Image;
            _bgFolder.Text = prev.Folder;
            _sync = false;
            await Wallpaper.ReloadAsync();
            await SyncWallpaperInfoAsync();
            PageBase.Toast(I18n.L("已撤销"), (string.IsNullOrEmpty(prev.Image) && string.IsNullOrEmpty(prev.Folder)) ? I18n.L("退回到没有壁纸") : ((prev.Image.Length > 0) ? prev.Image : prev.Folder), ToastKind.Success);
        }
    }

    private async Task ResetWallpaperAsync()
    {
        if (await Dlg.Confirm(I18n.L("恢复出厂壁纸"), I18n.L("会清掉当前壁纸与轮播文件夹；这一组仍会进历史，可以撤销回来。"), I18n.L("恢复"), I18n.L("取消")))
        {
            BackgroundState? backgroundState = await PageBase.Api.CallAsync<BackgroundState>("reset_background");
            _sync = true;
            _bgPath.Text = backgroundState?.Image ?? "";
            _bgFolder.Text = backgroundState?.Folder ?? "";
            _sync = false;
            await Wallpaper.ReloadAsync();
            await SyncWallpaperInfoAsync();
            PageBase.Toast(I18n.L("已恢复出厂壁纸"), I18n.L("想退回去点「撤销上一次」"), ToastKind.Success);
        }
    }

    /// <summary>
    /// 【Windows 源】<c>:1784-1795</c>:切深色。
    /// 【mac 差异】Windows 走 <c>MainWindow.SetDark(dark)</c> / <c>App.ApplyTheme(dark)</c>;
    /// mac 的外壳是两个都没有(它按 <c>ActualThemeVariantChanged</c> 自己刷取色),等价写法 = 设应用变体 + 让外壳重刷一次。
    /// </summary>
    private static void ApplyDark(bool dark)
    {
        ThemeVariant variant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        // 【C5a 更正】Windows 走的是 MainWindow.SetDark(dark),**窗口级**主题;只设应用级变体是不够的 ——
        // 窗口自己一旦有 RequestedThemeVariant(外壳 ApplyThemeState / 测试的 UiHost.Show 都会设),
        // 它会盖住应用级那个值 ⇒ ActualThemeVariant 不变 ⇒ 勾选框被 SyncAppearanceControls 翻回去,
        // 表现成"点了深色开关又跳回浅色"(本包在进程内通道实测定到)。
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = variant;
        }
        MainWindow? win = PageBase.Win;
        if (win is not null)
        {
            win.RequestedThemeVariant = variant;
            win.ApplyThemeVisuals();
        }
    }

    private async Task BrowseGameDirAsync()
    {
        string? path = Dlg.PickFolder(I18n.L("选择游戏目录"));
        if (path != null)
        {
            _gameDir0 = (await PageBase.Api.CallAsync<string>("set_game_dir", new
            {
                path,
            })) ?? path;
            _gameDir.Text = _gameDir0;
            PageBase.Toast(I18n.L("已切换目录"), _gameDir0, ToastKind.Success);
            await LoadAsync();
        }
    }

    private Task OpenWizardAsync()
    {
        Func<Task>? runner = FirstRunWizardRunner;
        if (runner is null)
        {
            // A 类缺件:没有接缝时如实说明,不假装打开了向导
            PageBase.Toast(I18n.L("启动向导还没移植"), I18n.L("它属于后续工作包;接上 FirstRunWizardRunner 之后这里就能正常打开"), ToastKind.Warning);
            return Task.CompletedTask;
        }
        return runner();
    }

    private async Task CheckUpdateAsync()
    {
        UpdateInfo? updateInfo;
        using (Dlg.Busy(I18n.L("正在检查更新...")))
        {
            updateInfo = await PageBase.Api.TryCallAsync<UpdateInfo>("check_update");
        }
        if (updateInfo == null)
        {
            PageBase.Toast(I18n.L("检查失败"), I18n.L("更新源没有响应"), ToastKind.Warning);
            return;
        }
        if (!updateInfo.HasUpdate)
        {
            PageBase.Toast(I18n.L("已是最新"), string.IsNullOrEmpty(updateInfo.Message) ? I18n.L("当前就是最新版本") : updateInfo.Message, ToastKind.Success);
            return;
        }
        string text = (updateInfo.Version + "\n" + updateInfo.Notes).Trim();
        if (await Dlg.Confirm(I18n.L("发现新版本"), text + I18n.L("\n\n现在下载更新?"), I18n.L("下载更新"), I18n.L("以后再说")))
        {
            await PageBase.Api.StartTaskAsync("start_self_update");
            PageBase.Toast(I18n.L("正在下载更新"), I18n.L("下载完会提示你重启完成更新"), ToastKind.Success);
        }
    }

    private async Task CleanAsync()
    {
        _clean.IsEnabled = false;
        _clean.Content = I18n.L("扫描中...");
        try
        {
            _cleanPreview = await PageBase.Api.CallAsync<CleanerPreview>("cleaner_preview");
        }
        finally
        {
            _clean.IsEnabled = true;
            _clean.Content = Ui.H(6.0, Ui.Glyph("\uea99", 12.5), Ui.Txt(I18n.L("扫描并清理")));
        }
        CleanerPreview? cleanPreview = _cleanPreview;
        if (cleanPreview == null || cleanPreview.Count == 0)
        {
            _cleanInfo.Text = I18n.L("没有可清理的文件");
            PageBase.Toast(I18n.L("很干净"), I18n.L("没有找到可清理的文件"), ToastKind.Success);
            return;
        }
        _cleanInfo.Text = I18n.L("可清理 {0} 个文件 · {1}", cleanPreview.Count, Fmt.Size(cleanPreview.Bytes));
        CheckBox libs = Ui.Check(I18n.L("未引用依赖库（{0} 个 · {1}）", cleanPreview.UnusedLibraries.Count, Fmt.Size(cleanPreview.UnusedLibraries.Sum((CleanerEntry x) => x.Bytes))), value: true);
        CheckBox parts = Ui.Check(I18n.L("残留 .part（{0} 个 · {1}）", cleanPreview.Parts.Count, Fmt.Size(cleanPreview.Parts.Sum((CleanerEntry x) => x.Bytes))), value: true);
        CheckBox cache = Ui.Check(I18n.L("更新缓存（{0} 个 · {1}）", cleanPreview.Cache.Count, Fmt.Size(cleanPreview.Cache.Sum((CleanerEntry x) => x.Bytes))), value: true);
        SPanel body = Ui.V(8.0, Ui.Muted(I18n.L("共 {0} 个文件，约 {1}。删掉的文件下次用到会自动重新下载。", cleanPreview.Count, Fmt.Size(cleanPreview.Bytes))), libs, parts, cache);
        if (!(await Dlg.Ask(I18n.L("清理文件"), body, I18n.L("开始清理"), I18n.L("取消"), danger: true)))
        {
            return;
        }
        List<string> list = new List<string>();
        if (libs.IsChecked == true)
        {
            list.Add("unused_libraries");
        }
        if (parts.IsChecked == true)
        {
            list.Add("parts");
        }
        if (cache.IsChecked == true)
        {
            list.Add("cache");
        }
        if (list.Count != 0)
        {
            CleanerResult? cleanerResult;
            using (Dlg.Busy(I18n.L("正在清理...")))
            {
                cleanerResult = await PageBase.Api.CallAsync<CleanerResult>("cleaner_apply", new
                {
                    kinds = list,
                });
            }
            _cleanInfo.Text = I18n.L("已清理 {0} 个文件 · {1}", cleanerResult?.Removed ?? 0, Fmt.Size(cleanerResult?.Bytes ?? 0));
            PageBase.Toast(I18n.L("清理完成"), _cleanInfo.Text, ToastKind.Success);
        }
    }

    private async Task RecommendAsync()
    {
        _rec.IsEnabled = false;
        SmartRecommendation? rec;
        try
        {
            using (Dlg.Busy(I18n.L("正在读取硬件信息...")))
            {
                rec = await PageBase.Api.CallAsync<SmartRecommendation>("get_smart_recommendation");
            }
        }
        finally
        {
            _rec.IsEnabled = true;
        }
        if (rec == null)
        {
            return;
        }
        SPanel body = Ui.V(6.0, Ui.Muted(I18n.L("本机：{0} GB 内存 / {1} 核 CPU", rec.TotalRamGb, rec.CpuCount)), Ui.Txt(I18n.L("推荐内存 {0} MB", rec.MemoryMb), 14.0, bold: true), Ui.Small(I18n.L("推荐 Java {0} · 窗口 {1}×{2} · 回收器 {3}", rec.JavaMajor, rec.WindowWidth, rec.WindowHeight, rec.GcPreset)));
        if (await Dlg.Ask(I18n.L("智能推荐"), body, I18n.L("应用推荐")))
        {
            _mem.Value = Clamp.Of(rec.MemoryMb, 512, 32768);
            if (rec.WindowWidth > 0)
            {
                _w.Text = rec.WindowWidth.ToString();
            }
            if (rec.WindowHeight > 0)
            {
                _h.Text = rec.WindowHeight.ToString();
            }
            SelectKey(_gc, string.IsNullOrEmpty(rec.GcPreset) ? "auto" : rec.GcPreset);
            PageBase.Toast(I18n.L("已应用"), I18n.L("内存已设为 {0} MB，保存设置后生效", rec.MemoryMb), ToastKind.Success);
        }
    }

    private async Task TestAiAsync()
    {
        _testAi.IsEnabled = false;
        _testAi.Content = I18n.L("测试中...");
        try
        {
            Dictionary<string, JsonElement> obj = (await PageBase.Api.TryCallAsync("get_settings", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>();
            Dictionary<string, object> dictionary = obj.ToDictionary<KeyValuePair<string, JsonElement>, string, object>(
                (KeyValuePair<string, JsonElement> kv) => kv.Key,
                (KeyValuePair<string, JsonElement> kv) => kv.Value);
            FillAiSettings(dictionary);
            string text = await PageBase.Api.CallAsync<string>("test_ai_connection", new
            {
                settings = dictionary,
            });
            PageBase.Toast(I18n.L("AI 连接成功"), text ?? "", ToastKind.Success);
        }
        finally
        {
            _testAi.IsEnabled = true;
            _testAi.Content = Ui.H(6.0, Ui.Glyph("\ue99a", 12.5), Ui.Txt(I18n.L("测试 AI 连接")));
        }
    }

    private async Task PreviewLanguageAsync()
    {
        string lang = KeyOf(_lang);
        if (string.IsNullOrEmpty(lang))
        {
            _langPreview.Text = "";
            return;
        }
        string[] value = await Task.WhenAll(new string[3] { "设置", "启动游戏", "下载任务" }.Select((string k) => PageBase.Api.TryCallAsync("translate", new
        {
            key = k,
            lang,
        }, I18n.L(k))));
        _langPreview.Text = (lang == _lang0)
            ? (I18n.L("预览：") + string.Join(" · ", value))
            : (I18n.L("切换后界面会变成：") + string.Join(" · ", value) + I18n.L("（重启后全量生效）"));
    }

    private void ApplySecretState(Dictionary<string, JsonElement> s)
    {
        _cfSet = GetBool(s, "curseforge_api_key_set");
        _cfHint = GetStr(s, "curseforge_api_key_hint");
        _aiKeySet = GetBool(s, "ai_api_key_set");
        _aiKeyHint = GetStr(s, "ai_api_key_hint");
        _cfClear = _aiKeyClear = false;
        _cfKey.Text = "";
        _aiKey.Text = "";
        SyncSecretBoxes();
    }

    private void SyncSecretBoxes()
    {
        _cfKey.Tag = _cfClear
            ? I18n.L("已恢复为内置 Key")
            : (_cfSet ? string.Format(I18n.L("已保存自己的 Key（{0}），留空不修改"), _cfHint) : I18n.L("CurseForge API Key（留空用内置）"));
        _aiKey.Tag = _aiKeyClear
            ? I18n.L("令牌已清除")
            : (_aiKeySet ? string.Format(I18n.L("已保存自己的令牌（{0}），留空不修改"), _aiKeyHint) : "sk-...");
        _cfReset.IsVisible = _cfSet && !_cfClear;
        _aiKeyReset.IsVisible = _aiKeySet && !_aiKeyClear;
    }

    private static bool GetBool(Dictionary<string, JsonElement> s, string k, bool def = false)
    {
        if (!s.TryGetValue(k, out JsonElement value) || (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
        {
            return def;
        }
        return value.GetBoolean();
    }

    private static int GetInt(Dictionary<string, JsonElement> s, string k, int def)
    {
        if (!s.TryGetValue(k, out JsonElement value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int parsed))
        {
            return def;
        }
        return parsed;
    }

    private static string GetStr(Dictionary<string, JsonElement> s, string k, string def = "")
    {
        if (!s.TryGetValue(k, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            return def;
        }
        return value.GetString() ?? def;
    }

    private static int Int(string? s, int fallback)
    {
        if (!int.TryParse(s?.Trim(), out int result) || result < 0)
        {
            return fallback;
        }
        return result;
    }

    /// <summary>【Windows 源】<c>:2006-2011</c>:关于卡片(版本 + 致谢)。</summary>
    private static Border BuildAboutCard()
    {
        TextBlock thanks = Ui.Muted("@368****168 \u2014 " + I18n.L("帮忙测试了正版（微软）验证"));
        thanks.TextWrapping = TextWrapping.Wrap;
        return Ui.Card(Ui.V(2.0, Ui.Section(I18n.L("关于"), I18n.L("TipCL 启动器")), Ui.Field(I18n.L("版本"), Ui.Muted(TipCL.Core.Launcher.AppInfo.APP_VERSION)), Ui.Field(I18n.L("致谢"), thanks, I18n.L("感谢每一位帮忙测试的朋友"))));
    }
}
