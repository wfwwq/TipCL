// 帧率角标(FPS)的三条守卫:
//   1) 默认关 —— 出厂配置里 ui_show_fps 必须是 false(用户明确要求「默认关闭」);
//   2) 关着的时候角标真的不在画面上,打开才可见,而且钉在右下角(不许吃掉命中,见 FpsOverlay 注释);
//   3) 开关要落盘 —— 重启后设置页读回来还是开着的(否则又变成「进程内记忆」)。
//
// 隔离:只改运行态的用例一律只调 FpsOverlay.Apply(不写 config.json),收尾还原;
// 需要证明落盘的用例显式 SetEnabled 并在 finally 里把原值写回去,不留脏配置给同进程的其他用例。
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using TipCL.App.Common;
using TipCL.App.Shell;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.App;

public class FpsOverlayTests
{
    [Fact]
    public void DefaultConfig_ShowsFpsOff()
    {
        // 出厂默认必须是关:角标属于排障开关,不能默认糊在用户脸上
        Assert.False(Py.Truthy(Config.DEFAULT_CONFIG.Get("ui_show_fps")));
    }

    [Fact]
    public void MainWindow_HostsHiddenCornerOverlay_ThatFollowsTheToggle() => TestApp.RunAsync(async () =>
    {
        bool prev = FpsOverlay.Enabled;
        MainWindow? win = null;
        try
        {
            FpsOverlay.Apply(false); // 只动运行态,不写盘

            win = new MainWindow();
            var overlay = FpsOverlay.OverlayOf(win);
            Assert.NotNull(overlay);
            Assert.False(overlay!.IsVisible);

            // 角落:右下角对齐 —— 别哪天被改成盖在正中间
            Assert.Equal(HorizontalAlignment.Right, overlay.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Bottom, overlay.VerticalAlignment);

            FpsOverlay.Apply(true);
            Assert.True(overlay.IsVisible);

            FpsOverlay.Apply(false);
            Assert.False(overlay.IsVisible);

            await Task.CompletedTask;
        }
        finally
        {
            // 先关循环再关窗口:开着的话 500ms 采样定时器还挂在这扇窗口上
            FpsOverlay.Apply(false);
            win?.Close();
            FpsOverlay.Apply(prev);
        }
    });

    [Fact]
    public void SetEnabled_PersistsToConfigFile() => TestApp.RunAsync(async () =>
    {
        bool prev = FpsOverlay.Enabled;
        try
        {
            FpsOverlay.SetEnabled(true);

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (SavedFps() != true && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            Assert.True(SavedFps());
            Assert.True(Py.Truthy(Config.CONFIG.Get(FpsOverlay.ConfigKey)));
        }
        finally
        {
            FpsOverlay.SetEnabled(prev); // 写回原值,别污染同进程里的其他用例
        }
    });

    /// <summary>读 config.json 的开关值。配置文件偶发被 Defender 短暂占锁,重试几次再判红。</summary>
    private static bool? SavedFps()
    {
        for (int i = 0; i < 20; i++)
        {
            try
            {
                string file = PPath.Abs(Config.CONFIG_FILE);
                if (!File.Exists(file)) return null;
                return Py.Truthy(Py.D(PyJson.Loads(File.ReadAllText(file))).Get(FpsOverlay.ConfigKey));
            }
            catch (Exception)
            {
                System.Threading.Thread.Sleep(20);
            }
        }
        return null;
    }
}