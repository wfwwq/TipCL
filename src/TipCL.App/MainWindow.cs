using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using TipCL.Themes;

namespace TipCL;

/// <summary>
/// A1 阶段的空主窗口:标题、初始尺寸、最小尺寸 + 一块随主题变色的底板。
/// C1a 起底板与文字色取自真色板(Palette.Light/Dark 的 B.Canvas / B.Ink),
/// 占位色已删(见 DECISIONS.md D-04)。
/// 标题栏(46px / 红绿灯留白)、导航、页面宿主、系统菜单栏都在 C2 补,
/// 本包只保证「窗口能起来、能被 --smoke 截出图、颜色来自真色板」。
/// </summary>
internal sealed class MainWindow : Window
{
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

    /// <summary>
    /// 按当前主题变体从色板取色刷新底板。<c>--smoke</c> 设完主题后显式调一次。
    /// 取色经 <see cref="Palette"/>:键缺失时给中性灰兜底,不会抛异常。
    /// </summary>
    internal void ApplyThemeVisuals()
    {
        ThemeVariant variant = RequestedThemeVariant == ThemeVariant.Dark || ActualThemeVariant == ThemeVariant.Dark
            ? ThemeVariant.Dark
            : ThemeVariant.Light;

        Background = Palette.Brush("B.Canvas", variant);
        _caption.Foreground = Palette.Brush("B.Ink", variant);
    }
}
