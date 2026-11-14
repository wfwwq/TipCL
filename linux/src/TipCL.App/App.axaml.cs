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
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            TipCL.App.Pages.Layout.LayoutFx.Wire(); // M2-B4：布局 JSON 导出/导入与 Toast 接线
            var win = new Shell.MainWindow();
            desktop.MainWindow = win;
            // M4-H P4:首启向导 + 反馈同意门(照 Windows 启动顺序);--smoke 时不打扰无头截图。
            if (!Environment.GetCommandLineArgs().Contains("--smoke"))
                _ = win.RunStartupPromptsAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
