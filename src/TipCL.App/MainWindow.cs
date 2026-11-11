using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace TipCL;

/// <summary>
/// A1 阶段的空主窗口:标题、初始尺寸、最小尺寸 + 一块随主题变色的底板。
/// 标题栏(46px / 红绿灯留白)、导航、页面宿主、系统菜单栏都在 C 阶段补,
/// 本包只保证「窗口能起来、能被 --smoke 截出图」。
/// </summary>
internal sealed class MainWindow : Window
{
    // A1 占位配色。真正的 Palette.Light/Dark(C1)会替换掉这两个色值,
    // 这里的作用是让明暗两帧截图确实不同(见 DECISIONS.md D-04)。
    private static readonly Color LightBackground = Color.Parse("#F7F7F8");
    private static readonly Color LightForeground = Color.Parse("#1A1A1E");
    private static readonly Color DarkBackground = Color.Parse("#1B1B1F");
    private static readonly Color DarkForeground = Color.Parse("#F2F2F4");

    private readonly TextBlock _caption;

    public MainWindow()
    {
        Title = "TipCL";
        Width = 900;
        Height = 550;
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _caption = new TextBlock
        {
            Text = "TipCL",
            FontSize = 28,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Content = new Border { Child = _caption };

        ApplyThemeVisuals();
        ActualThemeVariantChanged += (_, _) => ApplyThemeVisuals();
    }

    /// <summary>
    /// --smoke 解析到的页面 id。A1 只解析并保存,不做任何页面切换(页面在 C 阶段才有)。
    /// </summary>
    public string? PageId { get; init; }

    /// <summary>按当前主题变体刷新占位配色。--smoke 设完主题后显式调一次。</summary>
    internal void ApplyThemeVisuals()
    {
        var requested = RequestedThemeVariant ?? ThemeVariant.Default;
        bool dark = requested == ThemeVariant.Dark || ActualThemeVariant == ThemeVariant.Dark;

        Background = new SolidColorBrush(dark ? DarkBackground : LightBackground);
        _caption.Foreground = new SolidColorBrush(dark ? DarkForeground : LightForeground);
    }
}
