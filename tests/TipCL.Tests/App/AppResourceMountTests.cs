using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TipCL.App.Shell;
using TipCL.App.Ui;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>
/// 回归:代码侧取色依赖 Application 级资源挂载。
/// `Ui.Dyn` / `Res.Bind` / `Ui.Theme` / `Ui.Res` 全部锚定 `Application.Current`,
/// 若启动时没把 Palette + Controls.axaml 合并进 Application.Resources,它们一律解析为 null
/// (症状:启动按钮不变绿、绿点/下拉箭头不可见、卡片无底色)。这里锁住「构造主窗口即完成挂载」。
/// </summary>
public class AppResourceMountTests
{
    [Fact]
    public void MainWindow_MountsPaletteAndControlTheme_AtApplicationLevel() => TestApp.Run(() =>
    {
        _ = new MainWindow();

        var app = Application.Current!;
        Assert.True(app.TryGetResource("B.AccentStrong", null, out var brush), "Application 级缺少调色板键");
        Assert.IsType<SolidColorBrush>(brush);
        Assert.True(app.TryGetResource("Btn.Ghost", null, out _), "Application 级缺少 Controls.axaml 的 ControlTheme");

        // 代码侧取色链路必须真的拿到画刷
        var b = new Border();
        Ui.Dyn(b, Border.BackgroundProperty, "B.AccentStrong");
        Assert.NotNull(b.Background);
    });
}
