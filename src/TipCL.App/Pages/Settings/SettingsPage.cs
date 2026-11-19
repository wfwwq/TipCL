// 设置页（移植自 Windows 版 src/TipCL/Pages/SettingsPage.cs，M2-B5）：
// 左导航 7 项（通用/账号/Java/下载与网络/服务器/游戏时长/反馈与关于，选中白底胶囊粗体）
// + 右侧视图区。「通用」「下载与网络」「反馈与关于」走 B4 的 BlockHost 布局自定义；
// 账号/Java/服务器/游戏时长是本页自持的子页实例（不与 MainWindow 共享，避免同一控件两个父级）。
// 所有控件变更 → QueueSave → 500ms 防抖 DispatcherTimer → save_settings 全量字典（照
// Windows 版 SaveAsync ~L1549 的键表）；文本框 LostFocus/回车才存，防半截路径。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TipCL.App.Common;
using TipCL.App.Controls;
using TipCL.App.Pages.Layout;
using TipCL.App.Shell;
using TipCL.App.Ui;
using UiClass = TipCL.App.Ui.Ui;
using ToastKind = TipCL.App.Common.ToastKind;
using TipCL.Core.Launcher;
using TipCL.Core.Layout;

namespace TipCL.App.Pages.Settings;

public sealed partial class SettingsPage : PageBase
{
    private const string DefaultColor = ThemeService.DefaultAccent;

    private static readonly (string Key, string Label)[] IsoOpts =
    {
        ("none", "关闭（共用实例目录）"),
        ("saves", "隔离存档"),
        ("mods", "隔离 Mod 与配置"),
        ("all", "隔离全部"),
    };

    private static readonly (string Key, string Label)[] GcOpts =
    {
        ("auto", "G1（推荐）"),
        ("g1", "G1"),
        ("g1_tuned", "调优 G1"),
        ("zgc", "ZGC"),
        ("none", "不指定"),
    };

    private static readonly (string Key, string Label)[] VisOpts =
    {
        ("keep", "保持显示"),
        ("minimize", "最小化"),
        ("hide", "隐藏"),
        ("hide_reopen", "隐藏，退出后重开"),
        ("close", "关闭启动器"),
    };

    private static readonly (string Key, string Label)[] HomeOpts =
    {
        ("news", "Minecraft 新闻"),
        ("custom", "本地 HTML"),
        ("blank", "空白"),
    };

    private static readonly (string Key, string Label)[] WinOpts =
    {
        ("window", "窗口"),
        ("maximize", "全屏"),
    };

    private static readonly (string Key, string Label)[] SrcOpts =
    {
        ("auto", "自动（官方慢则走 BMCLAPI）"),
        ("official", "仅官方"),
        ("bmclapi", "仅 BMCLAPI"),
    };

    private static readonly (string Key, string Label)[] CommOpts =
    {
        ("auto", "自动"),
        ("official", "仅官方"),
        ("mcim", "仅 MCIM"),
    };

    private static readonly (string Key, string Label)[] BgOpts =
    {
        ("default", "默认"),
        ("eye", "护眼绿"),
        ("warm", "暖米"),
        ("mist", "雾蓝"),
    };

    private static readonly (string Key, string Label)[] AiModeOpts =
    {
        ("builtin", "内置网关（免配置）"),
        ("custom", "自定义 OpenAI 兼容端点"),
    };

    private static readonly (string Name, string Hex)[] Palette =
    {
        ("松绿", "#2E9B6B"),
        ("湛蓝", "#2F72C4"),
        ("紫罗兰", "#7C5CFC"),
        ("暖橙", "#D68A17"),
        ("绯红", "#D64545"),
        ("青碧", "#149B9B"),
    };

    // ── 外观 ──
    private readonly CheckBox _dark = UiClass.Switch();
    private readonly CheckBox _motion = UiClass.Switch(value: true);
    private readonly CheckBox _fly = UiClass.Switch(value: true);
    private readonly Slider _flyDur = UiClass.Sld(200, 1200, 620, 20);
    private readonly TextBlock _flyDurLbl = UiClass.Txt("620 ms", 12, bold: true);
    private readonly StackPanel _swatches = UiClass.H(8);
    private readonly ComboBox _bg = KeyCombo(BgOpts);

    // ── 壁纸 ──
    private readonly TextBox _bgPath = UiClass.Input("图片或视频路径，留空则不用壁纸");
    private readonly TextBox _bgFolder = UiClass.Input("壁纸文件夹（设了就轮播，优先于上面那张）");
    private readonly CheckBox _bgShuffle = UiClass.Switch();
    private readonly Slider _bgInterval = UiClass.Sld(1, 120, 10);
    private readonly TextBlock _bgIntervalLbl = UiClass.Txt("10 分钟", 12, bold: true);
    private readonly Slider _bgBlur = UiClass.Sld(0, 40, 0);
    private readonly TextBlock _bgBlurLbl = UiClass.Txt("0 px", 12, bold: true);
    private readonly Slider _bgDim = UiClass.Sld(0, 80, 0);
    private readonly TextBlock _bgDimLbl = UiClass.Txt("0 %", 12, bold: true);
    private readonly TextBlock _bgInfo = UiClass.Small("");
    private readonly Button _bgUndo;

    // ── 主题包 ──
    private readonly ComboBox _themePack = UiClass.Combo(null, null, 200);
    private readonly TextBlock _themeInfo = UiClass.Small("");

    // ── 下载与网络 ──
    private readonly Slider _threads = UiClass.Sld(1, 64, 8);
    private readonly TextBlock _threadsLbl = UiClass.Txt("8", 12, bold: true);
    private readonly Slider _segs = UiClass.Sld(1, 16, 4);
    private readonly TextBlock _segsLbl = UiClass.Txt("4", 12, bold: true);
    private readonly ComboBox _src = KeyCombo(SrcOpts);
    private readonly ComboBox _comm = KeyCombo(CommOpts);
    private readonly CheckBox _proxy = UiClass.Switch(value: true);
    private readonly TextBox _limit = UiClass.Input("0", "0", 110);
    private readonly TextBox _cfKey = UiClass.Input("CurseForge API Key（留空用内置）");
    private readonly Button _cfReset;

    // ── 游戏 ──
    private readonly Slider _mem = UiClass.Sld(512, 32768, 4096, 256);
    private readonly TextBlock _memLbl = UiClass.Txt("4096 MB", 12, bold: true);
    private readonly TextBox _w = UiClass.Input("854", "854", 80);
    private readonly TextBox _h = UiClass.Input("480", "480", 80);
    private readonly ComboBox _inst = UiClass.Combo(null, null, 200);
    private readonly ComboBox _iso = KeyCombo(IsoOpts);
    private readonly ComboBox _gc = KeyCombo(GcOpts);
    private readonly ComboBox _win = KeyCombo(WinOpts);
    private readonly TextBox _jvm = UiClass.Input("例如 -XX:+UseG1GC（可空）");
    private readonly CheckBox _shareLib = UiClass.Switch();
    private readonly CheckBox _shareAssets = UiClass.Switch();
    private readonly CheckBox _multi = UiClass.Switch();
    private readonly TextBox _gameDir = UiClass.Input("游戏目录（.minecraft 的落脚处）");
    private readonly TextBlock _cleanInfo = UiClass.Small("还没扫描");
    private readonly Button _clean;
    private readonly Button _rec;

    // ── 启动器 ──
    private readonly ComboBox _vis = KeyCombo(VisOpts);
    private readonly ComboBox _home = KeyCombo(HomeOpts);
    private readonly TextBox _homePath = UiClass.Input("本地 .html 文件路径（主页选「本地 HTML」时生效）");
    private readonly CheckBox _autoUpdate = UiClass.Switch(value: true);
    private readonly ComboBox _lang = KeyCombo(Array.Empty<(string, string)>());
    private readonly TextBlock _langPreview = UiClass.Small("");

    // ── AI ──
    private readonly ComboBox _aiMode = KeyCombo(AiModeOpts);
    private readonly TextBox _aiBase = UiClass.Input("https://api.openai.com/v1");
    private readonly TextBox _aiKey = UiClass.Input("sk-…");
    private readonly ComboBox _aiModel = new ComboBox { Width = 220 };
    private readonly TextBox _aiCtx = UiClass.Input("131072");
    private readonly TextBox _aiFallback = UiClass.Input("留空不启用");
    private readonly Button _aiKeyReset;
    private readonly List<Control> _aiCustomRows = new();

    // ── 角落:排障小开关(埋在左导航栏最底部,不占任何设置区块、不进布局方案) ──
    private readonly CheckBox _fps = UiClass.Switch();

    // ── 状态 ──
    private string _lang0 = "";
    private string _gameDir0 = "";
    private string _color = DefaultColor;
    private string _cfHint = "";
    private string _aiKeyHint = "";
    private bool _cfSet;
    private bool _aiKeySet;
    private bool _cfClear;
    private bool _aiKeyClear;
    private bool _sync;
    private bool _loaded;

    private readonly TextBlock _saveState = UiClass.Small("");
    private DispatcherTimer? _saveTimer;
    private bool _saving;
    private bool _savePending;

    private readonly List<(Border Pill, TextBlock Text)> _nav = new();
    // R32-LINMORE:7 → 8 —— 第 8 个内层视图「布局」对标 Windows SettingsPage.LayoutView(SettingsLayoutPage.cs)
    private readonly Control?[] _views = new Control?[8];
    private readonly ContentControl _view = new() { Focusable = false };
    private PageBase? _sub;

    // 页标题由基类 PageTitle（"设置"）承担

    public SettingsPage() : base("settings", "设置")
    {
        _bgUndo = UiClass.Btn("撤销上一次", BtnKind.Chip, (_, _) => Toast("撤销失败", "壁纸历史后端 M3 接入", ToastKind.Warning));
        _clean = UiClass.Btn("扫描并清理", BtnKind.Chip, (_, _) => Run(CleanAsync, "清理失败"));
        _rec = UiClass.Btn("查看推荐", BtnKind.Chip, (_, _) => Run(RecommendAsync, "检测失败"));

        // 滑块联动标签
        _flyDur.PropertyChanged += (_, e) => { if (e.Property.Name == nameof(Slider.Value)) _flyDurLbl.Text = $"{(int)_flyDur.Value} ms"; };
        _threads.PropertyChanged += (_, e) => { if (e.Property.Name == nameof(Slider.Value)) _threadsLbl.Text = ((int)_threads.Value).ToString(); };
        _segs.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name != nameof(Slider.Value)) return;
            int n = (int)_segs.Value;
            _segsLbl.Text = n <= 1 ? "关" : n.ToString();
        };
        _mem.PropertyChanged += (_, e) => { if (e.Property.Name == nameof(Slider.Value)) _memLbl.Text = $"{(int)_mem.Value} MB"; };
        _bgInterval.PropertyChanged += (_, e) => { if (e.Property.Name == nameof(Slider.Value)) _bgIntervalLbl.Text = $"{(int)_bgInterval.Value} 分钟"; };
        _bgBlur.PropertyChanged += (_, e) => { if (e.Property.Name == nameof(Slider.Value)) _bgBlurLbl.Text = $"{(int)_bgBlur.Value} px"; };
        _bgDim.PropertyChanged += (_, e) => { if (e.Property.Name == nameof(Slider.Value)) _bgDimLbl.Text = $"{(int)_bgDim.Value} %"; };

        // 外观即时生效
        _dark.IsCheckedChanged += (_, _) => { if (!_sync) ApplyDark(_dark.IsChecked == true); };
        _bg.SelectionChanged += (_, _) => { if (!_sync) ApplyVisuals(); };
        _motion.IsCheckedChanged += (_, _) => Motion.Enabled = _motion.IsChecked == true;

        // 帧率角标:立即生效 + 立即落盘(不走 500ms 防抖 —— 用户点完就关窗口也不该丢)。
        // 装载回填发生在 _sync=true 的窗口里,那种改动不写盘(见 SettingsPage.SaveLoad.LoadAsync)。
        _fps.IsCheckedChanged += (_, _) =>
        {
            if (_sync) return;
            FpsOverlay.SetEnabled(_fps.IsChecked == true);
        };

        // 主题色联动（B0 ThemeService 派生 24+3 键）
        ThemeService.VisualsChanged += SyncAppearanceControls;

        // 秘密行
        _cfReset = UiClass.Btn("恢复内置", BtnKind.Chip, (_, _) =>
        {
            _cfClear = true;
            _cfKey.Text = "";
            SyncSecretBoxes();
            QueueSave();
        });
        _aiKeyReset = UiClass.Btn("清除", BtnKind.Chip, (_, _) =>
        {
            _aiKeyClear = true;
            _aiKey.Text = "";
            SyncSecretBoxes();
            QueueSave();
        });
        _cfKey.PropertyChanged += (_, e) => { if (e.Property.Name == nameof(TextBox.Text) && !string.IsNullOrWhiteSpace(_cfKey.Text)) _cfClear = false; };
        _aiKey.PropertyChanged += (_, e) => { if (e.Property.Name == nameof(TextBox.Text) && !string.IsNullOrWhiteSpace(_aiKey.Text)) _aiKeyClear = false; };

        // 自定义 AI 行显隐
        _aiMode.SelectionChanged += (_, _) => SyncAiRows();

        // ── 通用视图：9 区块 ──
        var appearanceCard = UiClass.Card(Rows(
            UiClass.Field("深色模式", _dark, "立即生效"),
            UiClass.Field("主题色", _swatches, "点一下就换"),
            UiClass.Field("界面背景", _bg, "预设色调，立即生效"),
            UiClass.Field("界面动画", _motion, "换页、进度条、悬浮动效"),
            UiClass.Field("下载飞入动画", _fly, "安装时飞入底部任务条"),
            UiClass.Field("飞入动画时长", SliderRow(_flyDur, _flyDurLbl), "毫秒，建议 400–800")));
        BuildSwatches();

        var wallpaperCard = UiClass.Card(UiClass.V(4, UiClass.Small("图片或 mp4 动态壁纸（Linux 版 M2 只落设置项，渲染管线 M3 接入）").Wrap(),
            Rows(
                UiClass.Field("壁纸文件", _bgPath, "留空就不用壁纸"),
                UiClass.Field("轮播文件夹", _bgFolder, "设了文件夹就按间隔换图，优先于上面那张单图"),
                UiClass.Field("随机顺序", _bgShuffle, "关掉则按文件名顺序轮"),
                UiClass.Field("轮播间隔", SliderRow(_bgInterval, _bgIntervalLbl), "分钟"),
                UiClass.Field("模糊", SliderRow(_bgBlur, _bgBlurLbl), "把壁纸推远一点，文字更好读"),
                UiClass.Field("遮罩浓度", SliderRow(_bgDim, _bgDimLbl), "按当前主题压淡 / 压暗壁纸"),
                UiClass.Field("", UiClass.H(8, _bgUndo, _bgInfo.VCenter())))));

        var themePackCard = UiClass.Card(UiClass.V(4, UiClass.Small("把当前配色、背景、窗口与主页设置整包存下来，随时切回").Wrap(),
            Rows(
                UiClass.Field("已保存", _themePack, "从这里挑一个加载或删除"),
                UiClass.Field("", UiClass.H(7,
                    UiClass.Btn("保存为主题包", BtnKind.Soft, (_, _) => Run(ThemeOpAsync("save_theme"), "保存失败")),
                    UiClass.Btn("加载", BtnKind.Chip, (_, _) => Run(ThemeOpAsync("load_theme"), "加载失败")),
                    UiClass.Btn("删除", BtnKind.Ghost, (_, _) => Run(ThemeOpAsync("delete_theme"), "删除失败")),
                    UiClass.Btn("导入", BtnKind.Chip, (_, _) => Run(ThemeOpAsync("import_theme"), "导入失败")),
                    UiClass.Btn("导出", BtnKind.Chip, (_, _) => Run(ThemeOpAsync("export_theme"), "导出失败")))),
                _themeInfo)));

        var launcherCard = UiClass.Card(Rows(
            UiClass.Field("启动器可见性", _vis, "游戏启动后启动器窗口怎么处理"),
            UiClass.Field("启动页主页", _home, "新闻、自定义 HTML 或留空"),
            UiClass.Field("自定义主页", _homePath, "本地 .html 文件路径"),
            UiClass.Field("自动检查更新", UiClass.H(8, _autoUpdate, UiClass.Btn("检查更新", BtnKind.Chip, (_, _) => Run(CheckUpdateAsync, "检查更新失败"))), "启动时检查启动器更新"),
            UiClass.Field("语言", _lang, "切换界面语言，重启后完全生效"),
            UiClass.Field("", _langPreview)));

        var gameCard = UiClass.Card(Rows(
            UiClass.Field("游戏目录", GameDirRow(), "所有实例的落脚处；切换会校验可写性"),
            UiClass.Field("默认内存", SliderRow(_mem, _memLbl), "新实例的默认 JVM 内存"),
            UiClass.Field("", UiClass.H(8, _rec, UiClass.Small("按本机内存 / CPU 给一组推荐值").VCenter())),
            UiClass.Field("默认分辨率", UiClass.H(6, _w, UiClass.Txt("×").VCenter(), _h), "游戏窗口的默认宽高"),
            UiClass.Field("默认实例", _inst, "安装与启动缺省使用的实例"),
            UiClass.Field("新版本默认隔离", _iso, "安装新版本时写入，可在版本设置里改"),
            UiClass.Field("内存回收器", _gc, "启动时写入 JVM，版本设置可覆盖"),
            UiClass.Field("默认游戏窗口", _win, "可被版本设置覆盖"),
            UiClass.Field("默认 JVM 参数", _jvm, "所有版本都会带上，版本设置可再追加"),
            UiClass.Field("允许多开", _multi, "关掉则游戏已在运行时不再启动第二个")));

        var storageCard = UiClass.Card(Rows(
            UiClass.Field("共享 libraries", _shareLib, "所有实例共享依赖库（节省空间，降低隔离性）"),
            UiClass.Field("共享 assets 资源", _shareAssets, "所有实例共享资源文件（节省空间，降低隔离性）"),
            UiClass.Field("空间清理", UiClass.H(8, _clean, _cleanInfo.VCenter()), "扫描未引用的依赖库、残留 .part 与更新缓存，确认后再删")));

        var aiBaseRow = UiClass.Field("端点地址", _aiBase, "OpenAI 兼容的 base_url，到 /v1 为止");
        var aiKeyRow = UiClass.Field("API Key", SecretRow(_aiKey, _aiKeyReset), "留空不修改；本地端点不需要可不填");
        var aiModelRow = UiClass.Field("模型", UiClass.H(8, _aiModel), "填好地址和令牌后到 AI 对话里测试；拉不到可直接手填");
        var aiCtxRow = UiClass.Field("上下文窗口（token）", _aiCtx, "模型真实窗口未知时按 128k 保守压缩；实测后可改大");
        var aiFallbackRow = UiClass.Field("备用模型（可选）", _aiFallback, "主模型连续 429/5xx 时本回合自动切到它；留空不启用");
        _aiCustomRows.AddRange(new Control[] { aiBaseRow, aiKeyRow, aiModelRow, aiCtxRow, aiFallbackRow });
        var aiCard = UiClass.Card(UiClass.V(4, UiClass.Small("对话里下游戏、装模组、读崩溃日志").Wrap(),
            Rows(
                UiClass.Field("接入方式", _aiMode),
                aiBaseRow, aiKeyRow, aiModelRow, aiCtxRow, aiFallbackRow,
                UiClass.Field("权限", UiClass.Muted("在 AI 对话界面的输入框上方设置")),
                UiClass.Field("", UiClass.Btn("测试 AI 连接", BtnKind.Chip, (_, _) => Run(TestAiAsync, "AI 连接失败")).Left()))));

        // 「通用」= BlockHost("settings:general")，9 个区块（照 Windows 版 _views[0]）
        var general = new BlockHost("settings:general", new List<BlockSpec>
        {
            new() { Id = "appearance", Title = "外观", Content = UiClass.V(10, PTitle("通用"), Gt("外观"), appearanceCard) },
            new() { Id = "wallpaper", Title = "壁纸", Content = UiClass.V(10, Gt("壁纸"), wallpaperCard) },
            new() { Id = "themePack", Title = "主题包", Content = UiClass.V(10, Gt("主题包"), themePackCard) },
            new() { Id = "launcher", Title = "启动器", Content = UiClass.V(10, Gt("启动器"), launcherCard) },
            new() { Id = "game", Title = "游戏", Content = UiClass.V(10, Gt("游戏"), gameCard) },
            new() { Id = "storage", Title = "存储", Content = UiClass.V(10, Gt("存储"), storageCard) },
            new() { Id = "ai", Title = "AI 助手", Content = UiClass.V(10, Gt("AI 助手"), aiCard) },
            new() { Id = "memory", Title = "记忆", Content = UiClass.V(10, Gt("记忆"), BuildMemoryCard()) },
            new() { Id = "phoneSync", Title = "手机同步", Content = UiClass.V(10, Gt("手机同步"), BuildPhoneSyncCard()) },
        }).Root;

        // 「下载与网络」= BlockHost("settings:network") 单块
        var networkCard = UiClass.Card(Rows(
            UiClass.Field("下载并发线程数", SliderRow(_threads, _threadsLbl), "同时下载的文件数量"),
            UiClass.Field("单文件分片数", SliderRow(_segs, _segsLbl), "8MB 以上的大文件拆成几段同时下；1 为关闭"),
            UiClass.Field("文件下载源", _src, "自动测速，官方慢于 4 秒改走 BMCLAPI"),
            UiClass.Field("社区资源源", _comm, "模组 / 整合包：MCIM 国内镜像，挂了可改官方"),
            UiClass.Field("跟随系统代理", _proxy, "默认开。关掉才强制直连"),
            UiClass.Field("下载限速 (KB/s)", _limit.Left(), "0 表示不限制"),
            UiClass.Field("CurseForge Key", SecretRow(_cfKey, _cfReset), "留空不修改；不填用内置 Key")));
        var network = new BlockHost("settings:network", new List<BlockSpec>
        {
            new() { Id = "network", Title = "下载与网络", Content = UiClass.V(10, PTitle("下载与网络"), Gt("下载"), networkCard) },
        }).Root;

        // 「反馈与关于」= Windows FeedbackPage(2026-11-12 M4-H P4 由内联卡片换成真页:
        // 提交反馈 / 我提交过的 / 常见问题 / 关于四块都在 FeedbackView 里,index 6 懒建)
        _views[0] = general;
        _views[3] = network;
        // R32-LINMORE:「布局」是第 8 个视图(自带内容边距,见 SettingsPage.Layout.cs 的 BuildLayoutView)
        _views[LayoutView] = BuildLayoutView();

        // 左导航:0 通用 / 1 账号 / 2 Java / 3 下载与网络 / 4 服务器 / 5 游戏时长 / 6 反馈与关于 / 7 布局
        var nav = UiClass.V(2,
            NavBtn(0, "通用"), NavBtn(1, "账号"), NavBtn(2, "Java"), NavBtn(3, "下载与网络"),
            NavBtn(4, "服务器"), NavBtn(5, "游戏时长"), NavBtn(6, "反馈与关于"), NavBtn(LayoutView, "布局"),
            _saveState.M(12, 10, 0, 0));
        nav.Margin = new Thickness(12, 16, 0, 12);

        // 「显示帧率」埋在左下角(默认关):排障用,刻意不做成设置区块 —— 不占 BlockHost 布局、
        // 不进主题包 / 布局方案,也不在任何页签的正文里,只有往左栏底下看一眼才发现。
        var fpsRow = UiClass.H(6, _fps, UiClass.Small("显示帧率").VCenter());
        fpsRow.Margin = new Thickness(12, 4, 8, 4);
        ToolTip.SetTip(_fps, "在窗口右下角显示界面帧率(排障用)");
        var navColumn = UiClass.G("Auto,*,Auto");
        navColumn.Add(nav, 0, 0);
        navColumn.Add(fpsRow, 2, 0);

        // R32-LINSPLIT:左导航 168px(现状值)+ 18px 分隔条 + 视图。
        // 分隔条占住视图本来就有的 18px 左留白(_view 左移 18px 回到原位)⇒ 首屏逐像素不变。
        // **[Linux 与 Windows 的唯一差别】Windows 的 `_views[]` 顶层没有外边距(18px 在 SettingsPage
        // 自己身上,故 Windows 能直接给 _view 加 -18);Linux 的 18px 在视图**内部**
        // (LayoutEditor 的 `_list.Margin` = PadDefault 左 18,见 LayoutEditor.cs:216)。
        // 所以这里改的是**同一件事的另一头**:把承载视图的 ContentControl 整体左移 18,
        // 视图内部那 18px 原样不动 ⇒ 内容 x 坐标与改动前一致(168 + 18 = 186)。
        // 不做成 Mac 那种 "gap=0 + 命中宽度" 的原因:gap=18 让分隔条自带 18px 命中带、不与内容重叠,
        // 与 Windows 的机制一致(见 R32-LINSPLIT.md 的知情降级表 D-08)。
        // 比例存 v2 splits["settings"]["nav"];双击分隔条复位。
        var root = new PageLayoutHost("settings", "nav", new GridLength(168.0), new GridLength(1.0, GridUnitType.Star));
        _view.Margin = new Thickness(-18.0, 0.0, 0.0, 0.0);
        root.SetLeft(navColumn);
        root.SetRight(_view);
        Body = root;

        WireAutoSave();
        Select(0);
        AttachedToVisualTree += (_, _) => Run(LoadAsync, "设置加载失败");
        DetachedFromVisualTree += (_, _) => FlushSave();
    }

    // ───────────────────────────── 左导航 ─────────────────────────────

    private Border NavBtn(int i, string label)
    {
        // 选中白底胶囊 + 粗体（对照 settings2.png 左栏）；自绘 Border 避开 ToggleButton 模板干扰
        // 尺寸对齐 Windows SettingsPage.NavBtn:文案块定宽 120、内距 12/8、无外边距 →
        // 选中胶囊 156×33(此前 12/9 + 外边距 1 让它变成 146×36,整列节奏跟着变松)
        var text = new TextBlock { Text = label, FontSize = 13.5, Width = 120 };
        var pill = new Border
        {
            CornerRadius = new CornerRadius(8),
            MinWidth = 156,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = text,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        pill.PointerReleased += (_, e) => { Select(i); e.Handled = true; };
        pill.PointerEntered += (_, _) => { if (_navSelected != i) pill.Opacity = 0.75; };
        pill.PointerExited += (_, _) => pill.Opacity = 1;
        _nav.Add((pill, text));
        return pill;
    }

    private int _navSelected;

    /// <summary>切到某个内层导航项(0 通用 / 1 账号 / 2 Java / 3 下载与网络 / 4 服务器 / 5 游戏时长 / 6 反馈与关于);冒烟脚本按 SMOKE_SETTINGS_TAB 调它。</summary>
    public void SelectTab(int i) => Select(i);

    private void Select(int i)
    {
        _navSelected = i;
        for (int k = 0; k < _nav.Count; k++)
        {
            bool on = k == i;
            var (pill, text) = _nav[k];
            if (on)
            {
                pill[!Border.BackgroundProperty] = pill.GetResourceObservable("B.Paper").ToBinding();
                pill.BorderThickness = new Thickness(1);
                pill[!Border.BorderBrushProperty] = pill.GetResourceObservable("B.Line").ToBinding();
            }
            else
            {
                pill.Background = Brushes.Transparent;
                pill.BorderThickness = new Thickness(0);
            }
            pill.Opacity = 1;
            text.FontWeight = on ? FontWeight.Bold : FontWeight.Normal;
            text[!TextBlock.ForegroundProperty] = pill.GetResourceObservable(on ? "B.Ink" : "B.InkMuted").ToBinding();
        }
        // 子页是本页自持的 PageBase 实例，懒创建 + 生命周期转发（照 Windows 版 Select）
        PageBase? sub = i switch
        {
            1 => (PageBase)(_views[1] ??= new AccountView()),
            2 => (PageBase)(_views[2] ??= new JavaView()),
            4 => (PageBase)(_views[4] ??= new ServersView()),
            5 => (PageBase)(_views[5] ??= new PlaytimeView()),
            // 「反馈与关于」= Windows FeedbackPage(2026-11-12 由内联卡片换成真页:M4-H P4)
            6 => (PageBase)(_views[6] ??= new TipCL.App.Pages.Feedback.FeedbackView()),
            _ => null,
        };
        if (!ReferenceEquals(sub, _sub))
        {
            _sub?.ShellDetach();
            _sub = sub;
            sub?.ShellAttach(null);
        }
        // R32-LINMORE:切到「布局」时重建视图(对齐 Windows SettingsPage.Select:`if (i == LayoutView) _views[LayoutView] = BuildLayoutView();`)
        // —— 保证每次进来都读到最新的页签 / 区块 / 方案状态。
        if (i == LayoutView)
        {
            _views[LayoutView] = BuildLayoutView();
        }
        _view.Content = _views[i];
    }

    // ───────────────────────────── 构建小工具 ─────────────────────────────

    private TextBlock PTitle(string t) => new()
    {
        Text = t,
        FontSize = 21,
        FontWeight = FontWeight.Bold,
        Margin = new Thickness(0, 0, 0, 2),
        [!TextBlock.ForegroundProperty] = this.GetResourceObservable("B.Ink").ToBinding(),
    };

    private static TextBlock Gt(string t) => UiClass.GroupTitle(t);

    // settings2 贴图：设置行 = 上下行之间一条通到卡片两端的分隔线
    private static StackPanel Rows(params Control[] rows)
    {
        var p = UiClass.V(0);
        for (int i = 0; i < rows.Length; i++)
        {
            if (i > 0)
            {
                var sep = new Border { Height = 1, Margin = new Thickness(-12, 1, -12, 1) };
                sep[!Border.BackgroundProperty] = sep.GetResourceObservable("B.SegRail").ToBinding();
                p.Children.Add(sep);
            }
            if (rows[i] is Layoutable l) l.MinHeight = 40;
            p.Children.Add(rows[i]);
        }
        return p;
    }

    private static Control SliderRow(Slider s, TextBlock label)
    {
        var g = UiClass.G(null, "*,Auto");
        g.Add(s.VCenter(), 0);
        g.Add(label.M(10, 0, 0, 0).VCenter(), 0, 1);
        return g;
    }

    private static Control SecretRow(TextBox box, Button reset)
    {
        var g = UiClass.G(null, "*,Auto");
        g.Add(box, 0);
        reset.Margin = new Thickness(8, 0, 0, 0);
        reset.VerticalAlignment = VerticalAlignment.Center;
        g.Add(reset, 0, 1);
        return g;
    }

    private Control GameDirRow()
    {
        var browse = UiClass.Btn("浏览…", BtnKind.Chip, (_, _) => Run(BrowseGameDirAsync, "切换失败"));
        var g = UiClass.G(null, "*,Auto");
        g.Add(_gameDir, 0);
        browse.Margin = new Thickness(8, 0, 0, 0);
        browse.VerticalAlignment = VerticalAlignment.Center;
        g.Add(browse, 0, 1);
        return g;
    }

    private static ComboBox KeyCombo(IEnumerable<(string Key, string Label)> items, double width = 200)
    {
        var c = new ComboBox { Width = width };
        foreach (var (key, label) in items)
            c.Items.Add(new Opt(key, label));
        if (c.Items.Count > 0)
            c.SelectedIndex = 0;
        return c;
    }

    private sealed class Opt(string key, string label)
    {
        public string Key => key;
        public override string ToString() => label;
    }

    private static void SelectKey(ComboBox c, string? key)
    {
        foreach (var item in c.Items)
        {
            if (item is Opt opt && opt.Key == key)
            {
                c.SelectedItem = item;
                return;
            }
        }
        if (c.Items.Count > 0)
            c.SelectedIndex = 0;
    }

    private static string KeyOf(ComboBox c) => (c.SelectedItem as Opt)?.Key ?? (c.SelectedItem as string) ?? "";
}
