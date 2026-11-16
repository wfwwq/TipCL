using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TipCL.App.Shell;

namespace TipCL.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // R30-WP3b:语言设置接线。此前整棵 Linux 树没有任何地方调用 I18nCore.InitLanguage(),
        // 于是 CurrentLang 永远是字段初值 "zh_CN" —— 设置页写进 config.json 的 language
        // 从来没有人读回来,界面上的「重启后完全生效」是空承诺。
        // 这里在建任何窗口/页面之前补上这一次调用(必须在 lifetime 判断之外:
        // --smoke 走 SetupWithoutStarting(),此时没有 classic desktop lifetime,
        // 放进 if 里就覆盖不到无头截图路径,本项修复也就无法自证)。
        TipCL.Core.Launcher.I18nCore.InitLanguage();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            TipCL.App.Pages.Layout.LayoutFx.Wire(); // M2-B4：布局 JSON 导出/导入与 Toast 接线
            // R32-LW:注入 v1 投影实现(对齐 windos App.xaml.cs:97 LayoutStoreV2.V1Bridge = new PagesLayoutV1Bridge())。
            // 必须在建窗口之前装配:v2 首次加载/迁移时就要能读旧的 ui_page_layouts.json,并把 v2 写回 v1。
            TipCL.Core.Launcher.Layout.LayoutStoreV2.V1Bridge = new TipCL.App.Pages.Layout.PagesLayoutV1Bridge();
            var win = new Shell.MainWindow();
            desktop.MainWindow = win;
            // M4-H P4:首启向导 + 反馈同意门(照 Windows 启动顺序);--smoke 时不打扰无头截图。
            if (!Environment.GetCommandLineArgs().Contains("--smoke"))
                _ = win.RunStartupPromptsAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
