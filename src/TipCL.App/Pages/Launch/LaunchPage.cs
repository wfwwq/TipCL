using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using TipCL.Core.Launcher;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using TipCL.App.Common;
using TipCL.App.Shell;
using TipCL.App.Ui;
using UiClass = TipCL.App.Ui.Ui;
using TipCL.Core;
using ShapePath = Avalonia.Controls.Shapes.Path;
using IoPath = System.IO.Path;

namespace TipCL.App.Pages.Launch;

// 启动屏 v2（M2-B1，移植自 Windows 版 Pages/LaunchPage.cs，Avalonia 重写）：
// 左对齐英雄区（kicker + 34px 大标题 + 小标签 + 48 高启动行）+ 截图/hero 背景 + 底部新闻行。
// 与 Windows 版的差异（M2 阶段裁剪，详见 progress/M2-B1.md）：
//   - IMMERSIVE/Wallpaper 沉浸分支不搬；无截图回退嵌入 hero.jpg，再回退渐变底。
//   - 底部区块（新闻/下载任务/游戏时长/便签/日志）走 BlockHost，默认只显示新闻。
public sealed partial class LaunchPage : PageBase
{
    // —— 配置浮层里的数据控件（实例/版本变化走这里，英雄区从这些状态派生） ——
    /// <summary>
    /// 外部预选(照 Windows LaunchPage.PreferInstance):版本管理页双击版本 / 启动该版本时
    /// 先记下来,切到启动页后由 FillCardDataAsync 消费并选中。
    /// </summary>
    private static (string Instance, string Version)? _pending;

    private string _pendingVersion = "";

    /// <summary>请启动页在下次激活时切到该实例(与可选的版本)。</summary>
    public static void Prefer(string instance, string version = "")
        => _pending = (instance, version ?? "");

    private readonly ComboBox _inst = UiClass.Combo();

    private readonly ComboBox _ver = UiClass.Combo();

    private readonly TextBox _user = UiClass.Input("Player");

    private readonly TextBox _server = UiClass.Input("直连服务器 ip:port（可空）");

    private readonly Slider _mem = UiClass.Sld(1024, 32768, 4096, 256);

    private readonly ComboBox _java = UiClass.Combo();

    // Java 下拉的「标签 → 取值」对(取值 = 可执行文件路径,或「自动选择」)
    private List<(string Label, string Value)> _javaOpts = new();

    private readonly TextBlock _memLbl = UiClass.Txt("4096 MB", 12, bold: true);

    // 游戏分辨率(照 Windows 版 _wBox/_hBox):与设置页「默认分辨率」共用设置键 default_resolution,
    // 入口与 Windows 一样落在配置浮层的「更多设置」区(见 LaunchPopups.BuildConfig)
    private readonly TextBox _wBox = UiClass.Input("854", "854", 74);

    private readonly TextBox _hBox = UiClass.Input("480", "480", 74);

    private readonly ProgressBar _prog = new() { Height = 4, Width = 320, Maximum = 100, Opacity = 0.92, IsVisible = false };

    private readonly TextBlock _status = UiClass.Txt("就绪", 12);

    private int _progGen;

    // —— 英雄区 ——
    private readonly TextBlock _kickerText = new()
    {
        FontSize = 12.5,
        MaxWidth = 560,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock _heroTitle = new()
    {
        FontSize = 34,
        FontWeight = FontWeight.ExtraBold,
        MaxWidth = 620,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly StackPanel _chipRow = new() { Orientation = Orientation.Horizontal, Spacing = 6 };

    private Border? _launchShell;

    private readonly Border _stopShell = new()
    {
        CornerRadius = new CornerRadius(12),
        Height = 48,
        Padding = new Thickness(26, 0, 26, 0),
        IsVisible = false,
    };

    private Border? _configPill;

    private StackPanel? _titleRow;

    private StackPanel? _hero;

    // 主按钮内嵌版本全名：细竖线分隔 + 文本
    private readonly Border _goSep = new() { Width = 1, Height = 20, Opacity = 0.45, Margin = new Thickness(2, 0, 2, 0) };

    private readonly TextBlock _goVer = UiClass.Txt("", 13);

    // —— 背景 ——
    private readonly Image _bgImage = new()
    {
        Stretch = Stretch.UniformToFill,
        IsHitTestVisible = false,
        IsVisible = false,
    };

    private readonly Grid _bgShade = new() { IsHitTestVisible = false, IsVisible = false };

    // —— 底部新闻行 ——
    private readonly TextBlock _newsTicker = new()
    {
        FontSize = 12.5,
        MaxWidth = 520,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly StackPanel _newsHost = new() { Spacing = 6 };

    private Border? _newsCard;

    // —— 底部区块(下载任务 / 游戏时长 / 便签 / 日志,默认收起)——
    private readonly StackPanel _taskHost = UiClass.V(6);

    private readonly TextBlock _playTotal = new() { FontSize = 20, FontWeight = FontWeight.SemiBold };

    private readonly TextBox _notes = UiClass.Multi("随手记点什么…");

    private readonly TextBox _log = UiClass.LogBox();

    private readonly List<string> _logLines = new();

    private TipCL.App.Pages.Layout.BlockHost? _blocks;

    // —— 随背景切换配色的半透明块（配置钮 / 联机钮 / 新闻卡 / chips） ——
    private readonly List<Border> _glass = new();

    private readonly List<ShapePath> _glassIcons = new();

    private static readonly IBrush W100 = White(255);

    private static readonly IBrush W80 = White(204);

    private static readonly IBrush W14 = White(36);

    private static readonly IBrush W22 = White(56);

    private bool _onImage;

    private string _bgKey = "";

    private int _bgGen;

    private static Bitmap? _heroCache;

    // —— 数据状态 ——
    private sealed class InstRow
    {
        public string Name = "";

        public string Path = "";

        public List<string> Versions = new();
    }

    private List<InstRow> _instances = new();

    private List<string> _versions = new();

    private int _modCount = -1;

    private string _modKey = "";

    private int _verGen;

    private string? _taskId;

    private bool _syncing;

    private bool _crashShown;

    private readonly DispatcherTimer _memSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };

    private static bool _eventWired;

    public LaunchPage()
        : base("launch", "启动")
    {
        BuildHero();
        WireEvents();
    }

    // 首载统一走 PageBase 生命周期:外壳 Navigate → ShellAttach → OnCreate(只跑一次)
    protected override void OnCreate() => _ = FillCardDataAsync();

    private static IBrush White(byte alpha)
        => new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255));

    // 截图遮罩：rgba(10,14,22,a) 线性渐变（launch2.html .sh），stops 为 alpha/offset 对
    private static Rectangle Shade(RelativePoint from, RelativePoint to, params double[] stops)
    {
        var g = new LinearGradientBrush { StartPoint = from, EndPoint = to };
        for (int i = 0; i + 1 < stops.Length; i += 2)
            g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)stops[i], 10, 14, 22), stops[i + 1]));
        return new Rectangle { Fill = g, IsHitTestVisible = false };
    }

    // ————— 启动屏布局 —————

    private void BuildHero()
    {
        _bgShade.Children.Add(Shade(new RelativePoint(0, 0.5, RelativeUnit.Relative), new RelativePoint(1, 0.5, RelativeUnit.Relative), 199, 0, 115, 0.42, 0, 0.75));
        _bgShade.Children.Add(Shade(new RelativePoint(0.5, 1, RelativeUnit.Relative), new RelativePoint(0.5, 0, RelativeUnit.Relative), 140, 0, 0, 0.35));

        // kicker 行：绿点 + 就绪 · 目录
        var dot = new Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
        dot.Dyn(Shape.FillProperty, "B.Ok");
        var kicker = UiClass.H(7, dot, _kickerText);

        // 大标题行：实例名 + 下拉箭头，点击开实例切换浮层
        var chevron = GlassIcon("Chevron", 18);
        chevron.Opacity = 0.7;
        chevron.VerticalAlignment = VerticalAlignment.Center;
        _titleRow = UiClass.H(10, _heroTitle, chevron);
        _titleRow.Cursor = new Cursor(StandardCursorType.Hand);
        _titleRow.Background = Brushes.Transparent;
        _titleRow.HorizontalAlignment = HorizontalAlignment.Left;
        _titleRow.Margin = new Thickness(0, 6, 0, 2);
        _titleRow.PointerReleased += (_, e) => { OpenSwitchPopup(); e.Handled = true; };

        _chipRow.Margin = new Thickness(0, 10, 0, 24);

        // 主按钮：主色 48 高外壳 + Ghost 按钮内容；柔光用 BoxShadow 取主题主色
        var playIcon = IconLib.Make("Play", "B.OnAccentStrong", 17);
        playIcon.VerticalAlignment = VerticalAlignment.Center;
        var goText = UiClass.Txt("启动游戏", 16, bold: true);
        goText.FontWeight = FontWeight.ExtraBold;
        goText.VerticalAlignment = VerticalAlignment.Center;
        _goVer.Opacity = 0.85;
        _goVer.VerticalAlignment = VerticalAlignment.Center;
        _goSep.IsVisible = _goVer.IsVisible = false;
        _goSep.Dyn(Border.BackgroundProperty, "B.OnAccentStrong");
        var launchBtn = new Button
        {
            Theme = UiClass.Theme("Btn.Ghost"),
            Padding = new Thickness(0),
            Content = UiClass.H(10, playIcon, goText, _goSep, _goVer),
        };
        launchBtn.Click += (_, _) => _ = LaunchAsync();
        // 绿底上的文字/图标一律走 OnAccentStrong:按钮自身主题(Btn.Ghost)的 Foreground 会盖掉
        // 英雄区继承来的白字,不显式设就是「深字压绿底」
        launchBtn.Dyn(Button.ForegroundProperty, "B.OnAccentStrong");
        _launchShell = new Border
        {
            CornerRadius = new CornerRadius(12),
            Height = 48,
            Padding = new Thickness(30, 0, 30, 0),
            Child = launchBtn,
        };
        _launchShell.Dyn(Border.BackgroundProperty, "B.AccentStrong");
        Motion.HoverLift(_launchShell, 1.02);

        // 停止按钮：同尺寸 DangerSoft 外壳
        var stopIcon = IconLib.Make("Stop", "B.Danger", 15);
        var stopBtn = new Button
        {
            Theme = UiClass.Theme("Btn.Ghost"),
            Padding = new Thickness(0),
            Content = UiClass.H(8, stopIcon, UiClass.Txt("停止", 15, bold: true)),
        };
        stopBtn.Click += (_, _) => _ = StopAsync();
        _stopShell.Child = stopBtn;
        _stopShell.Dyn(Border.BackgroundProperty, "B.DangerSoft");

        _configPill = GlassButton("Gear", null, OpenConfigPopup);
        ToolTip.SetTip(_configPill, "启动配置");
        var net = GlassButton("Wifi", "联机", () =>
        {
            // Linux 版暂无联机页（Windows 版 Navigate("multiplayer")）
            if (TopLevel.GetTopLevel(this) is MainWindow win)
                win.Navigate("multiplayer");
            Toast("联机", "联机功能将在后续里程碑提供", ToastKind.Info);
        });
        var launchRow = UiClass.H(10, _launchShell, _stopShell, _configPill, net);

        // 进度/状态行：空闲时收起（无 DependencyPropertyDescriptor，改显式调用 UpdateStatusRow）
        var statusRow = UiClass.H(10, _prog, _status);
        _prog.VerticalAlignment = VerticalAlignment.Center;
        _prog.Dyn(ProgressBar.ForegroundProperty, "B.Accent");
        _status.VerticalAlignment = VerticalAlignment.Center;
        statusRow.Margin = new Thickness(0, 14, 0, 0);
        _statusRow = statusRow;
        UpdateStatusRow();

        _hero = UiClass.V(0, kicker, _titleRow, _chipRow, launchRow, statusRow);
        _hero.MaxWidth = 680;
        _hero.HorizontalAlignment = HorizontalAlignment.Left;
        _hero.VerticalAlignment = VerticalAlignment.Center;
        _hero.Margin = new Thickness(56, 0, 24, 0);

        // 底部新闻行：主色「新闻」标签 + 摘要 + 箭头，点击开公告浮层
        var tag = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 2, 8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = UiClass.Txt("新闻", 11, bold: true, "B.OnAccent"),
        };
        tag.Dyn(Border.BackgroundProperty, "B.Accent");
        var arrow = GlassIcon("ChevronRight", 12);
        _newsCard = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 8, 14, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = UiClass.H(10, tag, _newsTicker, arrow),
        };
        Glass(_newsCard);
        _newsCard.PointerReleased += (_, e) => { OpenNewsPopup(); e.Handled = true; };
        var logCard = UiClass.Card(_log, 10);
        logCard.Height = 240;
        _notes.Text = Py.S(Config.CONFIG.Get("ui_notes", ""));
        _notes.LostFocus += (_, _) =>
        {
            Config.CONFIG.Set("ui_notes", _notes.Text ?? "");
            Config.CONFIG.Save();
        };
        // 边距同英雄区(左 56 / 右 24 / 下 18)
        _blocks = new TipCL.App.Pages.Layout.BlockHost("launch", new List<TipCL.App.Pages.Layout.BlockSpec>
        {
            new() { Id = "news", Title = "新闻", Content = _newsCard },
            new() { Id = "tasks", Title = "下载任务", Content = UiClass.Card(_taskHost), DefaultVisible = false },
            new() { Id = "playtime", Title = "游戏时长", Content = UiClass.Card(_playTotal), DefaultVisible = false },
            new() { Id = "notes", Title = "便签", Content = UiClass.Card(_notes), DefaultVisible = false },
            new() { Id = "log", Title = "日志", Content = logCard, DefaultVisible = false },
        }, new Thickness(56, 0, 24, 18));
        // 多块同时展开时限高约 62% 页高,避免把上方英雄区挤没
        PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty && _blocks != null)
                _blocks.MaxHeight = Math.Max(120, Bounds.Height * 0.62);
        };

        // 根布局：背景层铺满两行；英雄区 * / 新闻行 Auto
        var root = UiClass.G("*,Auto");
        root.ClipToBounds = true;
        // 背景铺满整页(含新闻行):只铺第一行会让新闻行落在页面底色上,而新闻行按「有图」
        // 规则用白字 → 白纸白字看不见。对齐 Windows 的整窗壁纸层。
        Grid.SetRowSpan(_bgImage, 2);
        Grid.SetRowSpan(_bgShade, 2);
        root.Add(_bgImage, 0, 0);
        root.Add(_bgShade, 0, 0);
        Grid.SetColumnSpan(_bgImage, 2);
        Grid.SetColumnSpan(_bgShade, 2);
        root.Add(_hero, 0);
        root.Add(_blocks, 1);
        Content = root;
        ApplyHeroTone(onImage: false);
        SyncHero();
    }

    private Control? _statusRow;

    // 半透明玻璃图标钮（48 高圆角 12）；label 为 null 即 48×48 图标钮
    private Border GlassButton(string iconKey, string? label, Action click)
    {
        var icon = GlassIcon(iconKey, 17);
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var text = label == null ? null : UiClass.Txt(label, 14, bold: true);
        if (text != null) text.VerticalAlignment = VerticalAlignment.Center;
        Control content = text == null ? icon : UiClass.H(8, icon, text);
        var b = new Border
        {
            Height = 48,
            MinWidth = 48,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = label == null ? new Thickness(0) : new Thickness(16, 0, 16, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = content,
        };
        Glass(b);
        b.PointerReleased += (_, e) => { click(); e.Handled = true; };
        return b;
    }

    private ShapePath GlassIcon(string key, double size)
    {
        var p = IconLib.Make(key, "B.Ink", size);
        _glassIcons.Add(p);
        return p;
    }

    private void Glass(Border b)
    {
        _glass.Add(b);
        b.PointerEntered += (_, _) => Tone(b, hover: true);
        b.PointerExited += (_, _) => Tone(b, hover: false);
    }

    // 有截图：白字 + 半透明白块；无截图：主题纸色块 + 主题字色
    private void Tone(Border b, bool hover)
    {
        if (_onImage)
        {
            b.Background = hover ? W22 : W14;
            b.BorderBrush = W22;
            b.SetValue(TextElement.ForegroundProperty, W100);
        }
        else
        {
            b.Dyn(Border.BackgroundProperty, hover ? "B.Hover" : "B.Paper");
            b.Dyn(Border.BorderBrushProperty, "B.Line");
            b.Dyn(TextElement.ForegroundProperty, "B.Ink");
        }
    }

    private void ApplyHeroTone(bool onImage)
    {
        _onImage = onImage;
        _bgImage.IsVisible = onImage;
        _bgShade.IsVisible = onImage;
        if (_hero != null)
        {
            if (onImage) _hero.SetValue(TextElement.ForegroundProperty, W100);
            else _hero.Dyn(TextElement.ForegroundProperty, "B.Ink");
        }
        if (onImage)
        {
            _kickerText.Foreground = W80;
            _status.Foreground = W80;
        }
        else
        {
            _kickerText.Dyn(TextBlock.ForegroundProperty, "B.InkMuted");
            _status.Dyn(TextBlock.ForegroundProperty, "B.InkMuted");
        }
        foreach (var b in _glass)
            Tone(b, hover: false);
        foreach (var p in _glassIcons)
        {
            if (onImage) p.Stroke = W100;
            else p.Dyn(Shape.StrokeProperty, "B.Ink");
        }
        foreach (var c in _chipRow.Children)
            if (c is Border chip)
                Tone(chip, hover: false);
        // 主按钮柔光取当前主题主色
        var accent = (UiClass.Brush("B.AccentStrong") as ISolidColorBrush)?.Color ?? Colors.Gray;
        if (_launchShell != null)
        {
            var shadowColor = Color.FromArgb((byte)(onImage ? 115 : 77), accent.R, accent.G, accent.B);
            _launchShell.BoxShadow = new BoxShadows(BoxShadow.Parse($"0 6 20 0 {shadowColor}"));
        }
    }

    private Border Chip(string text)
    {
        var chip = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 3, 10, 4),
            Child = UiClass.Txt(text, 12),
        };
        Tone(chip, hover: false);
        return chip;
    }

    // ————— 背景：实例截图优先 → hero.jpg → 渐变底 —————

    private async Task RefreshBackgroundAsync()
    {
        int gen = ++_bgGen;
        try
        {
            string instance = CurInst();
            string version = CurVer();
            string root = _instances.FirstOrDefault(x => x.Name == instance)?.Path ?? "";
            string skip = _bgKey;
            (string Key, Bitmap? Bmp)? shot = null;
            if (root.Length > 0)
            {
                string[] dirs =
                {
                    IoPath.Combine(root, "screenshots"),
                    version.Length == 0 ? "" : IoPath.Combine(root, "versions", version, "screenshots"),
                };
                shot = await Task.Run(() => LoadShot(dirs, skip));
            }
            if (gen != _bgGen)
                return;
            if (shot != null && shot.Value.Bmp == null)
                return; // 同一张截图，保持现状
            if (shot == null)
            {
                var hero = await Task.Run(HeroSource);
                if (gen != _bgGen)
                    return;
                if (hero != null)
                {
                    _bgKey = "";
                    _bgImage.Source = hero;
                    ApplyHeroTone(onImage: true);
                    return;
                }
            }
            _bgKey = shot?.Key ?? "";
            _bgImage.Source = shot?.Bmp;
            ApplyHeroTone(shot != null);
        }
        catch
        {
            // 背景只是装饰，任何异常都不影响启动页
        }
    }

    // 线程池执行：找最新 png/jpg 截图并解码（不锁文件）；与 skipKey 相同返回 (key, null)；没有/出错返回 null
    private static (string, Bitmap?)? LoadShot(string[] dirs, string skipKey)
    {
        try
        {
            FileInfo? best = null;
            foreach (string d in dirs)
            {
                if (d.Length == 0 || !Directory.Exists(d))
                    continue;
                foreach (var f in new DirectoryInfo(d).EnumerateFiles())
                {
                    string ext = f.Extension.ToLowerInvariant();
                    if ((ext == ".png" || ext == ".jpg" || ext == ".jpeg") && (best == null || f.LastWriteTimeUtc > best.LastWriteTimeUtc))
                        best = f;
                }
            }
            if (best == null)
                return null;
            string key = best.FullName + "|" + best.LastWriteTimeUtc.Ticks;
            if (key == skipKey)
                return (key, null);
            using var fs = new FileStream(best.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bmp = Bitmap.DecodeToWidth(fs, 1600);
            return (key, bmp);
        }
        catch
        {
            return null;
        }
    }

    // 解码嵌入 hero.jpg（launch2 效果图同款全景），只解码一次
    private static Bitmap? HeroSource()
    {
        if (_heroCache == null)
        {
            try
            {
                using var s = Avalonia.Platform.AssetLoader.Open(new Uri("avares://TipCL.App/Assets/hero.jpg"));
                _heroCache = Bitmap.DecodeToWidth(s, 1600);
            }
            catch
            {
                return null;
            }
        }
        return _heroCache;
    }

    // ————— 数据填充 —————

    protected override void OnActivate()
    {
        SyncHero();
        // 设置页可能刚改过分辨率:回到启动页就同步过来,免得两个入口各拿一份旧值互相覆盖
        ApplyResolution();
        ApplyHeroTone(_onImage);
        _ = RefreshBackgroundAsync();
        _ = SyncTaskStateAsync();
        SyncTaskCard();
        if (CurInst().Length > 0)
            _ = ReloadVersionsAsync();
    }

    private async Task FillCardDataAsync()
    {
        _syncing = true;
        try
        {
            _instances = await LoadInstancesAsync();
            var names = _instances.Select(x => x.Name).ToList();
            string preferred = Config.CONFIG.Get("last_user_instance", "") is string s && names.Contains(s) ? s : names.FirstOrDefault() ?? "";
            if (_pending is { } pend)
            {
                _pending = null;
                if (names.Contains(pend.Instance))
                {
                    preferred = pend.Instance;
                    _pendingVersion = pend.Version;
                }
            }
            FillCombo(_inst, names, preferred);
            var user = Py.S(Config.CONFIG.Get("last_user", ""));
            if (string.IsNullOrWhiteSpace(_user.Text))
                _user.Text = user.Length > 0 ? user : "Player";
            double mem = 4096;
            try { mem = Convert.ToDouble(Config.CONFIG.Get("default_memory_mb", 4096), CultureInfo.InvariantCulture); }
            catch { }
            _mem.Value = Math.Clamp(mem, 1024, 32768);
            _memLbl.Text = $"{(int)_mem.Value} MB";
            ApplyResolution();
        }
        finally
        {
            _syncing = false;
        }
        await ReloadVersionsAsync();
        await ReloadJavaAsync(scan: false);
        _ = ReloadJavaAsync(scan: true);
        SyncBanner();
        _ = CountModsAsync();
        _ = LoadNewsAsync();
        _ = LoadPlaytimeAsync();
        SyncTaskCard();
    }

    private static async Task<List<InstRow>> LoadInstancesAsync()
    {
        var rows = await Api.TryCallAsync<List<object>>("get_instances", new List<object>()) ?? new List<object>();
        var list = new List<InstRow>();
        foreach (var r in rows)
        {
            if (r is not PyDict d)
                continue;
            var row = new InstRow { Name = Py.S(d["name"]), Path = Py.S(d["path"]) };
            if (d["versions"] is List<object> vs)
                row.Versions = vs.Select(Py.S).ToList();
            list.Add(row);
        }
        return list;
    }

    private string CurInst() => _inst.SelectedItem as string ?? "";

    private string CurVer() => _ver.SelectedItem as string ?? "";

    private static void FillCombo(ComboBox cb, List<string> items, string? selected)
    {
        var keep = selected ?? (cb.SelectedItem as string);
        cb.Items.Clear();
        foreach (var it in items)
            cb.Items.Add(it);
        cb.SelectedItem = keep != null && items.Contains(keep) ? keep : (items.Count > 0 ? items[0] : null);
    }

    // ————— 分辨率(与设置页共用 default_resolution;照 Windows FillCardData:881 / SaveDisplayPrefsAsync:995) —————

    /// <summary>把设置里的 default_resolution 灌进宽/高输入框;缺键或值不可用时回落 854×480。</summary>
    private void ApplyResolution()
    {
        var (w, h) = LaunchResolution.Read();
        _wBox.Text = w.ToString(CultureInfo.InvariantCulture);
        _hBox.Text = h.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>宽/高输入框 → 设置键 default_resolution 并立即落盘(设置页读的就是这个键)。</summary>
    private void SaveResolution()
    {
        if (_syncing)
            return;
        var (w, h) = LaunchResolution.ParseText(_wBox.Text, _hBox.Text);
        LaunchResolution.Write(w, h);
        // 归一化回框里:手输 "abc" 之后不该还显示原文,避免下次再拿它去写一遍
        _wBox.Text = w.ToString(CultureInfo.InvariantCulture);
        _hBox.Text = h.ToString(CultureInfo.InvariantCulture);
    }

    // ————— Java 选择(照 Windows ReloadJavaAsync / SaveJavaAsync / SelectedJava) —————

    /// <summary>当前 Java 下拉的取值(路径或「自动选择」)。</summary>
    private string SelectedJava()
    {
        string label = _java.SelectedItem as string ?? "自动选择";
        return _javaOpts.FirstOrDefault(o => o.Label == label).Value ?? "自动选择";
    }

    private async Task ReloadJavaAsync(bool scan)
    {
        string inst = CurInst();
        if (inst.Length == 0)
            return;
        var opts = await Api.TryCallAsync<List<object>>("java_combo_options", new List<object>(),
            new PyDict { ["instance"] = inst, ["scan_system"] = scan }) ?? new List<object>();
        if (CurInst() != inst || opts.Count == 0)
            return;
        var parsed = new List<(string Label, string Value)>();
        foreach (var o in opts)
            if (o is PyDict d)
                parsed.Add((Py.S(d["label"]), Py.S(d["value"])));
        string keep = await Api.TryCallAsync("java_combo_label_for",
            "自动选择", new PyDict { ["instance"] = inst, ["options"] = opts }) ?? "自动选择";
        _syncing = true;
        _javaOpts = parsed;
        FillCombo(_java, parsed.Select(o => o.Label).ToList(), keep);
        _syncing = false;
        SyncHero();
    }

    private async Task SaveJavaAsync()
    {
        string inst = CurInst();
        if (inst.Length == 0)
            return;
        await Api.TryCallAsync<object>("set_instance_java", null,
            new PyDict { ["name"] = inst, ["java"] = SelectedJava() });
    }

    private async Task ReloadVersionsAsync()
    {
        string inst = CurInst();
        if (inst.Length == 0)
            return;
        int gen = ++_verGen;
        var list = await Api.TryCallAsync<List<object>>("get_installed_versions", new List<object>(), new PyDict { ["instance"] = inst }) ?? new List<object>();
        var vers = list.Select(Py.S).ToList();
        if (gen != _verGen || CurInst() != inst)
            return;
        _versions = vers;
        _syncing = true;
        string wanted = _ver.SelectedItem as string ?? "";
        if (_pendingVersion.Length > 0 && vers.Contains(_pendingVersion)) wanted = _pendingVersion;
        _pendingVersion = "";
        FillCombo(_ver, vers, wanted);
        _syncing = false;
        SyncBanner();
    }

    // 模组数小标签：同一实例/版本只查一次
    private async Task CountModsAsync()
    {
        string key = CurInst() + "|" + CurVer();
        if (key == _modKey)
            return;
        _modKey = key;
        _modCount = -1;
        var list = await Api.TryCallAsync<List<object>>("get_installed_mod_entries", new List<object>(), new PyDict { ["instance"] = CurInst(), ["version"] = CurVer() });
        if (key == _modKey)
        {
            _modCount = list?.Count ?? 0;
            SyncHero();
        }
    }

    private void SyncBanner()
    {
        if (CurInst().Length == 0 && _instances.Count > 0)
            _status.Text = "还没有实例，先到「实例库」添加";
        else if (_versions.Count == 0 && CurInst().Length > 0)
            _status.Text = "这个实例还没有版本，先去「下载」装一个";
        else
            _status.Text = "就绪";
        SyncHero();
        _ = RefreshBackgroundAsync();
        UpdateStatusRow();
    }

    // 启动屏大标题 / 状态行 / 小标签；数据全来自现有字段，缺什么省什么
    private void SyncHero()
    {
        string instance = CurInst();
        _heroTitle.Text = instance.Length == 0 ? "选择实例" : instance;
        string dir = _instances.FirstOrDefault(x => x.Name == instance)?.Path ?? "";
        _kickerText.Text = "就绪" + (dir.Length == 0 ? "" : " · " + dir);
        _chipRow.Children.Clear();
        string ver = CurVer();
        _goVer.Text = ver;
        bool hasVer = ver.Length > 0;
        _goSep.IsVisible = _goVer.IsVisible = hasVer;
        if (hasVer)
            _chipRow.Children.Add(Chip(LoaderChip(ver)));
        string javaChip = JavaChip();
        if (javaChip.Length > 0)
            _chipRow.Children.Add(Chip(javaChip));
        _chipRow.Children.Add(Chip($"{_mem.Value / 1024.0:0.#} GB"));
        if (_modCount > 0)
            _chipRow.Children.Add(Chip($"{_modCount} 个模组"));
    }

    /// <summary>
    /// Java 小标签:自动 ⇒「Java 自动」;「Java 17 (…)」「jdk1.8.0_301」⇒「Java 17」;
    /// 路径等认不出的一律原样(对齐 Windows SyncHero 的正则)。
    /// </summary>
    private string JavaChip()
    {
        string java = _java.SelectedItem as string ?? "";
        if (java.Length == 0)
            return "";
        if (java.StartsWith("自动", StringComparison.Ordinal))
            return "Java 自动";
        var m = System.Text.RegularExpressions.Regex.Match(
            java, @"(?:java|jdk|jre)\D{0,3}(?:1\.)?(\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? "Java " + m.Groups[1].Value : java;
    }

    /// <summary>chip 首位显示「加载器名 版本」：与 Windows 版 LOADER_TAGS 同前缀表；不匹配回落版本全名。</summary>
    private static string LoaderChip(string ver)
    {
        string low = ver.ToLowerInvariant();
        (string prefix, string label)[] tags =
        {
            ("fabric-loader-", "Fabric"),
            ("neoforge-", "NeoForge"),
            ("optifine-", "OptiFine"),
            ("liteloader-", "LiteLoader"),
            ("quilt-loader-", "Quilt"),
            ("forge-", "Forge"),
        };
        foreach (var (prefix, label) in tags)
        {
            if (!low.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            string rest = ver.Substring(prefix.Length);
            int dash = rest.IndexOf('-');
            string lv = dash > 0 ? rest.Substring(0, dash) : rest;
            return lv.Length > 0 ? label + " " + lv : ver;
        }
        return ver;
    }

    // ————— 新闻行 —————

    private async Task LoadNewsAsync()
    {
        _newsHost.Children.Clear();
        string mode = Py.S(Config.CONFIG.Get("homepage_mode", "news"));
        if (mode == "blank")
        {
            _newsHost.Children.Add(UiClass.Muted("主页已设为空白"));
            _newsTicker.Text = "主页已设为空白";
            return;
        }
        if (mode == "custom")
        {
            string path = Py.S(Config.CONFIG.Get("custom_homepage", ""));
            _newsHost.Children.Clear();
            try
            {
                string text = string.IsNullOrWhiteSpace(path) || !File.Exists(path)
                    ? "未设置自定义主页。到设置 → 启动页主页 填本地 HTML 路径。"
                    : await Task.Run(() => File.ReadAllText(path));
                _newsHost.Children.Add(UiClass.Muted(text.Length > 2000 ? text.Substring(0, 2000) + "…" : text));
                _newsTicker.Text = text.Length > 60 ? text.Substring(0, 60) + "…" : text;
            }
            catch (Exception ex)
            {
                _newsHost.Children.Add(UiClass.Muted("读不了自定义主页：" + ex.Message));
                _newsTicker.Text = "读不了自定义主页";
            }
            return;
        }
        var rows = await Api.TryCallAsync<List<object>>("cached_news", new List<object>()) ?? new List<object>();
        FillNews(rows);
        var fresh = await Api.TryCallAsync<List<object>>("fetch_news", new List<object>());
        if (fresh is { Count: > 0 })
            FillNews(fresh);
    }

    private void FillNews(List<object> rows)
    {
        _newsHost.Children.Clear();
        if (rows.Count == 0)
        {
            _newsHost.Children.Add(UiClass.Muted("暂无新闻"));
            _newsTicker.Text = "暂无新闻";
            return;
        }
        int shown = 0;
        foreach (var r in rows.Take(6))
        {
            if (r is not PyDict d)
                continue;
            string title = Py.S(d["title"]);
            if (title.Length == 0)
                continue;
            var t = UiClass.Txt(title, 12.5, bold: true);
            t.TextWrapping = TextWrapping.Wrap;
            _newsHost.Children.Add(t);
            string body = Py.S(d["body"]);
            if (body.Length > 0)
            {
                var b = UiClass.Txt(body, 12);
                b.Foreground = UiClass.Brush("B.InkMuted");
                b.TextWrapping = TextWrapping.Wrap;
                _newsHost.Children.Add(b);
            }
            shown++;
        }
        if (shown == 0)
        {
            _newsHost.Children.Add(UiClass.Muted("暂无新闻"));
            _newsTicker.Text = "暂无新闻";
            return;
        }
        _newsTicker.Text = Py.S(((rows[0] as PyDict)?["title"]) ?? "");
        Motion.Stagger(_newsHost);
    }

    // ————— 启动流程 —————

    private async Task LaunchAsync()
    {
        string version = CurVer();
        if (version.Length == 0)
        {
            await ReloadVersionsAsync();
            version = CurVer();
        }
        if (version.Length == 0)
        {
            Toast("没有版本", "请先到「下载」安装", ToastKind.Warning);
            (TopLevel.GetTopLevel(this) as MainWindow)?.Navigate("download");
            return;
        }
        string instance = CurInst();
        string account = _user.Text?.Trim() ?? "";
        if (account.Length == 0)
            account = "Player";
        _prog.Value = 0;
        _prog.IsVisible = true;
        _progGen++;
        _status.Text = "准备启动…";
        _logLines.Clear();
        _log.Text = "";
        SetLaunchMode(running: true);
        UpdateStatusRow();
        Config.CONFIG.Set("last_user", account);
        Config.CONFIG.Set("last_user_instance", instance);
        try
        {
            // 分辨率取配置浮层里的宽×高(照 Windows LaunchAsync:1401-1402);版本设置里的
            // window_width/height 优先级更高,由 LaunchFlow.ResolveResolution 处理。
            var (resW, resH) = LaunchResolution.ParseText(_wBox.Text, _hBox.Text);
            var args = new PyDict
            {
                ["instance"] = instance,
                ["version"] = version,
                ["account"] = account,
                ["username"] = account,
                ["memory_mb"] = (int)_mem.Value,
                ["width"] = resW,
                ["height"] = resH,
                ["extra_game_args"] = ExtraArgs()!,
            };
            var row = await Api.StartTaskAsync("启动游戏", async (prog, ct) =>
            {
                _taskId = prog.TaskId; // 先登记，避免任务瞬间失败时 finished 事件找不到 taskId
                prog.Report("正在启动 " + version + "…");
                var ok = await Api.TryCallAsync<object?>("launch_game", null, args, ct);
                if (ok == null)
                    throw new NotImplementedException("启动能力尚未接入（M3 实机业务落地）");
                prog.Report("游戏进程已启动");
            });
            _taskId = row.Id;
        }
        catch (Exception ex)
        {
            SetLaunchMode(running: false);
            _status.Text = "启动失败";
            UpdateStatusRow();
            Toast("启动失败", ex.Message, ToastKind.Error);
        }
    }

    private void SetLaunchMode(bool running)
    {
        if (_launchShell != null)
            _launchShell.IsVisible = !running;
        _stopShell.IsVisible = running;
        _crashShown = false;
    }

    private async Task StopAsync()
    {
        if (_taskId != null)
            Api.CancelTask(_taskId);
        await Task.CompletedTask;
    }

    private string[]? ExtraArgs()
    {
        string text = _server.Text?.Trim() ?? "";
        if (text.Length == 0)
            return null;
        int i = text.LastIndexOf(':');
        if (i > 0)
            return new[] { "--server", text.Substring(0, i), "--port", text.Substring(i + 1) };
        return new[] { "--server", text, "--port", "25565" };
    }

    // ————— 事件流 —————

    private void WireEvents()
    {
        if (_eventWired)
            return;
        _eventWired = true;
        Api.BridgeEvent += ev => Dispatcher.UIThread.Post(() => OnApiEvent(ev));
        TaskStore.Changed += SyncTaskCard;
        _inst.SelectionChanged += (_, _) =>
        {
            if (_syncing)
                return;
            _ = ReloadVersionsAsync();
            _ = ReloadJavaAsync(scan: false);
            _ = CountModsAsync();
        };
        _java.SelectionChanged += (_, _) =>
        {
            if (_syncing)
                return;
            // 实例 Java 偏好:换完即存(照 Windows SaveJavaAsync)
            _ = SaveJavaAsync();
            SyncHero();
        };
        _ver.SelectionChanged += (_, _) =>
        {
            if (_syncing)
                return;
            SyncHero();
        };
        _mem.ValueChanged += (_, e) =>
        {
            _memLbl.Text = $"{(int)e.NewValue} MB";
            SyncHero();
            _memSaveTimer.Stop();
            _memSaveTimer.Start();
        };
        _memSaveTimer.Tick += (_, _) =>
        {
            _memSaveTimer.Stop();
            Config.CONFIG.Set("default_memory_mb", (int)_mem.Value);
        };
        _user.LostFocus += (_, _) => Config.CONFIG.Set("last_user", _user.Text ?? "");
        _server.LostFocus += (_, _) => Config.CONFIG.Set("direct_server", _server.Text ?? "");
        // 分辨率:离开输入框(或回车)即落盘(照 Windows _wBox/_hBox.LostFocus → SaveDisplayPrefsAsync)
        _wBox.LostFocus += (_, _) => SaveResolution();
        _hBox.LostFocus += (_, _) => SaveResolution();
        _wBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) SaveResolution(); };
        _hBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) SaveResolution(); };
    }

    private void OnApiEvent(ApiEvent ev)
    {
        if (ev.Event == "task_added")
            return;
        if (_taskId == null || ev.TaskId != _taskId)
            return;
        switch (ev.Event)
        {
            case "log":
                AppendLog(ev.Text);
                break;
            case "progress":
            {
                Motion.Progress(_prog, ev.Total > 0 ? ev.Current * 100.0 / ev.Total : 0);
                TaskMsg.SplitMsg(ev.Message, out string status, out string speed);
                _status.Text = (status.Length == 0 ? "处理中…" : status) + (speed.Length == 0 ? "" : "    " + speed);
                UpdateStatusRow();
                break;
            }
            case "game_started":
                _status.Text = "游戏进程已启动，正在加载世界…";
                Motion.Progress(_prog, Math.Max(_prog.Value, 92));
                UpdateStatusRow();
                break;
            case "game_exited":
                _status.Text = "游戏已退出";
                HideProgressSoon(0);
                break;
            case "finished":
                SetLaunchMode(running: false);
                _status.Text = ev.Message.Length > 0 ? ev.Message : (ev.Success ? "已完成" : "✘ 失败");
                if (ev.Success)
                    Motion.Progress(_prog, 100);
                HideProgressSoon(ev.Success ? 1200 : 0);
                if (!ev.Success && !_crashShown && ev.Message != "已取消")
                {
                    _crashShown = true;
                    Toast("启动失败", ev.Message, ToastKind.Error);
                }
                _taskId = null;
                _ = LoadPlaytimeAsync();
                break;
        }
    }

    private async Task LoadPlaytimeAsync()
    {
        object? total = await Api.TryCallAsync<object?>("get_total_playtime");
        long seconds = 0;
        try { seconds = Py.Int(total ?? 0L); }
        catch (Exception) { }
        _playTotal.Text = Playtime.FormatDuration(seconds);
    }

    /// <summary>「下载任务」区块:最近 5 条任务的标题 + 进度/结果。</summary>
    private void SyncTaskCard()
    {
        _taskHost.Children.Clear();
        var rows = TaskStore.Rows.Reverse().Take(5).ToList();
        if (rows.Count == 0)
        {
            _taskHost.Children.Add(UiClass.Muted("暂无下载任务"));
            return;
        }
        foreach (var r in rows)
        {
            var title = UiClass.Txt(r.Title, 12);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            var state = UiClass.Small(r.Finished ? (r.Success ? "完成" : "失败") : $"{r.Progress:0}%");
            state.Margin = new Thickness(8, 0, 0, 0);
            var g = UiClass.G(null, "*,Auto");
            g.Add(title, 0, 0);
            g.Add(state, 0, 1);
            _taskHost.Children.Add(g);
        }
    }

    private void AppendLog(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        _logLines.Add(text);
        if (_logLines.Count > 2500)
            _logLines.RemoveRange(0, _logLines.Count - 2500);
        _log.Text = string.Join("\n", _logLines);
        _log.CaretIndex = _log.Text.Length;
    }

    private async void HideProgressSoon(int delayMs)
    {
        int gen = _progGen;
        if (delayMs > 0)
            await Task.Delay(delayMs);
        if (gen == _progGen)
        {
            _prog.IsVisible = false;
            UpdateStatusRow();
        }
    }

    // 空闲（无进度且状态为「就绪」）时状态行收起
    private void UpdateStatusRow()
    {
        if (_statusRow == null)
            return;
        bool idle = !_prog.IsVisible && (string.IsNullOrEmpty(_status.Text) || _status.Text == "就绪");
        _statusRow.IsVisible = !idle;
    }

    // 游戏退出/失败后回到页面时校准按钮态
    private Task SyncTaskStateAsync()
    {
        if (_taskId == null)
            SetLaunchMode(running: false);
        return Task.CompletedTask;
    }
}
