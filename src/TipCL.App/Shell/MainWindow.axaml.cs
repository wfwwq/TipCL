using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using TipCL.App.Pages;
using TipCL.App.Pages.Launch;

namespace TipCL.App.Shell;

/// <summary>
/// 主窗口外壳：1180×740、40px 自定义标题栏（Logo + 5 页签 + 主题切换 + 窗口按钮）、
/// 196px 分组侧边导航，点击切换右侧 ContentControl 占位页。
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
        NavHost.NavClicked += (_, id) => Navigate(id);
        Pages.Library.LibraryPage.NavRequested += id => Navigate(id); // M2-B2：实例库页内跳转（双击版本 → 启动页）
        SetDark(false);
        Navigate("launch", instant: true);
    }

    /// <summary>当前页 id。</summary>
    public string CurrentPageId => _currentId;

    /// <summary>是否深色主题。</summary>
    public bool IsDark => _isDark;

    /// <summary>切换到指定占位页。</summary>
    public void Navigate(string id, bool instant = false)
    {
        if (_currentId == id) return;
        var page = GetPage(id);
        if (page is null) return;
        _currentId = id;
        PageHost.Content = page;
        NavHost.SetSelected(id);
        TitleBarHost.SetActiveTab(id);
    }

    /// <summary>切换明暗主题。</summary>
    public void ToggleTheme() => SetDark(!_isDark);

    public void SetDark(bool dark)
    {
        _isDark = dark;
        var merged = Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(new ResourceInclude((Uri?)null) { Source = new Uri(dark
            ? "avares://TipCL.App/Themes/Palette.Dark.axaml"
            : "avares://TipCL.App/Themes/Palette.Light.axaml") });
        TitleBarHost.SyncThemeIndicator(dark);
    }

    // M1 占位页工厂；M2 换成真实页面时只改这里
    private Control? GetPage(string id) => id switch
    {
        "launch" => _pages.GetOrAdd(id, () => new LaunchPage() /* M2-B1 */),
        "dashboard" => _pages.GetOrAdd(id, () => new PlaceholderDashboard()),
        "account" => _pages.GetOrAdd(id, () => new PlaceholderAccount()),
        "java" => _pages.GetOrAdd(id, () => new PlaceholderJava()),
        "library" => _pages.GetOrAdd(id, () => new TipCL.App.Pages.Library.LibraryPage()), // M2-B2：真实实例库替换占位
        "download" => _pages.GetOrAdd(id, () => new Download.DownloadPage()), // M2-B3：真实下载页替换占位
        "tasks" => _pages.GetOrAdd(id, () => new PlaceholderTasks()),
        "plugins" => _pages.GetOrAdd(id, () => new Pages.Plugins.PluginsPage()), // M2-B4：真实插件页替换占位
        "settings" => _pages.GetOrAdd(id, () => new Pages.Settings.SettingsPage()), // M2-B5：真实设置页替换占位
        "about" => _pages.GetOrAdd(id, () => new PlaceholderAbout()),
        _ => null,
    };
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
