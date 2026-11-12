using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace TipCL.App.Shell;

/// <summary>
/// v2 自定义标题栏(46px):Logo + 顶部 5 页签(启动/实例库/下载/插件/设置)
/// + 右侧 AI 药丸钮 / 主题 / 账号钮 / 最小化 / 最大化 / 关闭。
/// 线宽 1.1 圆头矢量按钮;关闭悬停 #C42B1C 满高白字。
/// </summary>
public partial class TitleBar : UserControl
{
    private const string GeoMax = "M2,0.5 H8 A1.5,1.5 0 0 1 9.5,2 V8 A1.5,1.5 0 0 1 8,9.5 H2 A1.5,1.5 0 0 1 0.5,8 V2 A1.5,1.5 0 0 1 2,0.5 Z";
    private const string GeoRestore = "M1.7,2.5 H6.3 A1.2,1.2 0 0 1 7.5,3.7 V8.3 A1.2,1.2 0 0 1 6.3,9.5 H1.7 A1.2,1.2 0 0 1 0.5,8.3 V3.7 A1.2,1.2 0 0 1 1.7,2.5 Z M2.5,2.5 V1.7 A1.2,1.2 0 0 1 3.7,0.5 H8.3 A1.2,1.2 0 0 1 9.5,1.7 V6.3 A1.2,1.2 0 0 1 8.3,7.5 H7.5";
    private const string GeoMoon = "M13,9.5 A5.5,5.5 0 0 1 6.5,3 A5.5,5.5 0 1 0 13,9.5 Z";
    private const string GeoSun = "M5,8 A3,3 0 1 0 11,8 A3,3 0 1 0 5,8 Z M8,1.5 V2.8 M8,13.2 V14.5 M1.5,8 H2.8 M13.2,8 H14.5 M3.4,3.4 L4.3,4.3 M11.7,11.7 L12.6,12.6 M3.4,12.6 L4.3,11.7 M11.7,4.3 L12.6,3.4";

    /// <summary>页签被点击（参数为页签 id）。</summary>
    public event EventHandler<string>? TabClicked;

    /// <summary>主题切换按钮被点击。</summary>
    public event EventHandler? ThemeToggleClicked;

    /// <summary>AI 药丸钮被点击(对齐 Windows `AiBtn` → 打开 AI 助手)。</summary>
    public event EventHandler? AiClicked;

    /// <summary>账号钮被点击(对齐 Windows `AccBtn` → 打开账号页)。</summary>
    public event EventHandler? AccountClicked;

    public TitleBar()
    {
        InitializeComponent();
        foreach (var (id, title) in NavRegistry.TopTabs)
        {
            var tab = new Button { Classes = { "topTab" }, Content = title, Tag = id };
            tab.Click += (_, _) => TabClicked?.Invoke(this, id);
            TabPanel.Children.Add(tab);
        }

        // 账号钮默认态:未登录 = 首字母圆 + 「未登录」(对齐 Windows `MainWindow` 构造函数)
        AccName.Text = "未登录";
        AccInitial.Text = AccName.Text.Substring(0, 1);
        AiBtn.Click += (_, _) => AiClicked?.Invoke(this, EventArgs.Empty);
        AccBtn.Click += (_, _) => AccountClicked?.Invoke(this, EventArgs.Empty);
        ThemeBtn.Click += (_, _) => ThemeToggleClicked?.Invoke(this, EventArgs.Empty);
        ToolTip.SetTip(ThemeBtn, "切换深色 / 浅色");
        ToolTip.SetTip(MinBtn, "最小化");
        ToolTip.SetTip(MaxBtn, "最大化");
        ToolTip.SetTip(CloseBtn, "关闭");
        MinBtn.Click += (_, _) => (TopLevel.GetTopLevel(this) as Window)!.WindowState = WindowState.Minimized;
        MaxBtn.Click += (_, _) => ToggleMax();
        CloseBtn.Click += (_, _) => (TopLevel.GetTopLevel(this) as Window)!.Close();
    }

    /// <summary>浅色时显示月亮（点了变深），深色时显示太阳。</summary>
    public void SyncThemeIndicator(bool dark) => ThemeIco.Data = Geometry.Parse(dark ? GeoSun : GeoMoon);

    /// <summary>同步账号钮:名称 + 首字母圆(空名回落「未登录」,对齐 Windows SyncAccountChipAsync)。</summary>
    public void SetAccount(string? name)
    {
        string n = string.IsNullOrWhiteSpace(name) ? "未登录" : name!.Trim();
        AccName.Text = n;
        AccInitial.Text = n.Substring(0, 1).ToUpperInvariant();
        ToolTip.SetTip(AccBtn, n);
    }

    /// <summary>最大化/还原图标随窗口状态切换。</summary>
    public void SyncMaxGlyph(bool maximized)
    {
        MaxIco.Data = Geometry.Parse(maximized ? GeoRestore : GeoMax);
        ToolTip.SetTip(MaxBtn, maximized ? "还原" : "最大化");
    }

    /// <summary>同步顶部页签高亮（id 不在页签里则全部取消高亮）。</summary>
    public void SetActiveTab(string? id)
    {
        foreach (var child in TabPanel.Children)
        {
            if (child is Button b)
                SetClass(b, "on", Equals(b.Tag, id));
        }
    }

    // ───── 插件动态页签(P6/P8):group:"top" 的插件页面追加到 5 个内置页签之后 ─────

    private readonly System.Collections.Generic.List<Button> _pluginTabs = new();

    /// <summary>重建插件页签(id = plugin:&lt;plugin&gt;:&lt;page&gt;)。传空即清空。</summary>
    public void SetPluginTabs(System.Collections.Generic.IEnumerable<(string Id, string Label)> tabs)
    {
        foreach (var b in _pluginTabs) TabPanel.Children.Remove(b);
        _pluginTabs.Clear();
        foreach (var (id, label) in tabs)
        {
            var tab = new Button { Classes = { "topTab" }, Content = label, Tag = id };
            tab.Click += (_, _) => TabClicked?.Invoke(this, id);
            ToolTip.SetTip(tab, label);
            TabPanel.Children.Add(tab);
            _pluginTabs.Add(tab);
        }
    }

    /// <summary>当前插件页签的 id(测试与失效处理用)。</summary>
    public System.Collections.Generic.IReadOnlyList<string> PluginTabIds
    {
        get
        {
            var ids = new System.Collections.Generic.List<string>();
            foreach (var b in _pluginTabs) ids.Add((string?)b.Tag ?? "");
            return ids;
        }
    }

    internal static void SetClass(Control c, string className, bool on)
    {
        if (on) c.Classes.Add(className);
        else c.Classes.Remove(className);
    }

    // 标题栏整条可拖动窗口；双击切换最大化。按在按钮上时不抢。
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Control src && IsWithinButton(src)) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var window = TopLevel.GetTopLevel(this) as Window;
            if (e.ClickCount == 2)
            {
                ToggleMax();
            }
            else if (window is { WindowState: not WindowState.Maximized })
            {
                window.BeginMoveDrag(e);
            }
        }
    }

    private static bool IsWithinButton(Control source)
    {
        for (Control? c = source; c != null; c = c.Parent as Control)
        {
            if (c is Button) return true;
        }
        return false;
    }

    private void ToggleMax()
    {
        if (TopLevel.GetTopLevel(this) is not Window window) return;
        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (TopLevel.GetTopLevel(this) is Window w)
        {
            w.PropertyChanged += (_, args) =>
            {
                if (args.Property == Window.WindowStateProperty)
                    SyncMaxGlyph(w.WindowState == WindowState.Maximized);
            };
            SyncMaxGlyph(w.WindowState == WindowState.Maximized);
        }
    }
}
