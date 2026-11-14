using System;
using System.IO;
using System.Threading.Tasks;
using TipCL.App.Shell;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>
/// 标题栏月牙钮切明暗必须写回 ui_dark(Windows 版 SetDark → PushDarkAsync → save_settings)。
/// 设置页加载时以 get_settings 的 ui_dark 为准:不写回的话一打开设置页就被改回去,重启也记不住。
/// </summary>
public class ThemeToggleTests
{
    [Fact]
    public void ToggleTheme_PersistsUiDark_ToConfigFile() => TestApp.RunAsync(async () =>
    {
        var win = new MainWindow();
        bool before = win.IsDark;
        try
        {
            win.ToggleTheme();
            bool dark = win.IsDark;
            Assert.Equal(!before, dark);

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (SavedDark() != dark && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            Assert.Equal(dark, SavedDark());
            Assert.Equal(dark, Py.Truthy(Config.CONFIG.Get("ui_dark", Py.Box(false))));
        }
        finally
        {
            win.SetDark(before);
            Config.CONFIG.Set("ui_dark", before);
            Config.CONFIG.Save();
        }
    });

    private static bool? SavedDark()
    {
        // 这台开发机上配置文件的 tmp+替换偶发被 Defender 短暂占锁(PROGRESS 已记录),
        // 测试里读一次可能拿到读失败/半截文件 → 重试几次,避免把环境噪声当成产品缺陷。
        for (int i = 0; i < 20; i++)
        {
            try
            {
                string file = PPath.Abs(Config.CONFIG_FILE);
                if (!File.Exists(file)) return null;
                return Py.Truthy(Py.D(PyJson.Loads(File.ReadAllText(file))).Get("ui_dark"));
            }
            catch (Exception)
            {
                System.Threading.Thread.Sleep(20);
            }
        }
        return null;
    }
}
