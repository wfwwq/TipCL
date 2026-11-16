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

        // R32-MWG:v1 → v2 的注入缝接线(等价 Windows App.OnStartup 里的
        // LayoutStoreV2.V1Bridge = new PagesLayoutV1Bridge())。
        // 装配前 Core 走自带的 v1 文件读写(单测就是这条路);装配后 v2 文档的写通
        // 落到 mac 自己的 ui_page_layouts.json,老 v1 代码(PageLayoutStore 的调用方)立刻看到新值。
        // 幂等;必须在任何页面构造之前装配,否则首个 LayoutEditor 可能先读到未投影的 v1。
        TipCL.Core.Launcher.Layout.LayoutStoreV2.V1Bridge = new TipCL.Pages.PagesLayoutV1Bridge();

        // R32-MWG:自建区块 / 插件区块的内容工厂接线(§4.F 的口子)。
        // 等价 Windows 版 App.OnStartup 里的 WidgetRegistry.EnsureRegistered():
        // LayoutEditor.ResolveContent 对 custom:/plugin: 块会查 LayoutContentRegistry.Build,
        // 没有这一步,所有自建 / 插件区块都会退化成「区块不可用」占位。
        // 幂等,重复调用无副作用;必须放在窗口构建之前(首页可能马上就带自建块)。
        TipCL.Pages.WidgetRegistry.EnsureRegistered();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow window = new MainWindow();
            desktop.MainWindow = window;
            // C2d:真桌面生命周期里启动进程内后端,起来之后窗口的 BridgeReady 总闸才打开。
            // 失败只记日志、窗口保持未就绪;离屏 --smoke 走 SetupWithoutStarting,不进这条路径
            // (见 DECISIONS.md D-102)。
            window.StartBackend();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
