using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using TipCL.Themes;

namespace TipCL;

/// <summary>
/// 应用入口。C1a 起:挂 Fluent 主题 + 两份真主题色板(Palette.Light/Dark),
/// 主窗口从色板取色(D-04 的占位色已删)。
/// </summary>
public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());

        // 色板挂进 Application.Resources.ThemeDictionaries(Light / Dark 两份),
        // 代码里按色板键取色走 TipCL.Themes.Palette。
        Palette.Install(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
