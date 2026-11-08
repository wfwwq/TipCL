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
            desktop.MainWindow = new Shell.MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
