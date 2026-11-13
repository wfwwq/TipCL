using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using TipCL.Services;
using TipCL.Themes;

namespace TipCL;

/// <summary>
/// 应用入口。C1a 起:挂 Fluent 主题 + 两份真主题色板(Palette.Light/Dark),
/// 主窗口从色板取色(D-04 的占位色已删)。C1b 起再挂控件样式表 Themes\Controls.axaml。
/// </summary>
public sealed class App : Application
{
    /// <summary>C1b 的控件样式表(令牌 + 文本样式 + 控件样式,键名与 Windows 版一一对应)。</summary>
    internal const string ControlsUri = "avares://TipCL/Themes/Controls.axaml";

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());

        // 色板挂进 Application.Resources.ThemeDictionaries(Light / Dark 两份),
        // 代码里按色板键取色走 TipCL.Themes.Palette。
        Palette.Install(this);

        // C1b:控件样式表放在 FluentTheme **之后**,这样同名属性上我们的样式优先级更高
        // (Avalonia 里越晚加入的 Styles 越靠后,后写的胜出)。它自己的令牌与 ControlTheme
        // 落在 Styles.Resources 里,Controls.axaml 内部用 {StaticResource} 取。
        Styles.Add(new StyleInclude(new Uri(ControlsUri)) { Source = new Uri(ControlsUri) });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // C2a 接线:词表初始化 —— 等价 Windows 版 App.xaml.cs 的 I18n.Init(e.Args)。
        // 参数照 Windows 的约定:先 --lang,再 TIPCL_LANG,最后数据目录的 config.json。
        // 只读,不落盘;离屏(--smoke)走的是 SetupWithoutStarting(),不会进到这里,
        // 所以冒烟基准不受影响。注意:它在 Initialize() 之后跑,样式安装顺序(FluentTheme ->
        // Palette -> Controls.axaml)一字未动。
        I18n.Init((ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Args);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
