// R16 启动页分辨率字段:与设置页共用设置键 default_resolution。
// 覆盖三件事:读写往返(含真落盘)、与设置页同源(save_settings / get_settings 双向)、
// 缺键与异常值回落默认 854×480(含页面输入框那条路)。
// 注:Config 是进程内单例,每个用例收尾都把 default_resolution 还原,免得污染其它用例。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.App.Common;
using TipCL.App.Pages.Launch;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.App;

public class LaunchResolutionTests
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private readonly IApi _api = InProcessApi.Instance;

    // ————————————————— 与设置页同源 —————————————————

    [Fact]
    public void LaunchPageAndSettingsPage_ShareTheSameSettingKey() => TestApp.RunAsync(async () =>
    {
        object? before = Snapshot();
        try
        {
            // 1) 设置页保存那条路:save_settings { data: { default_resolution } } → 启动页读得到
            //    (SettingsPage.SaveLoad.cs:322 就是这么写的)
            await _api.CallAsync<object>("save_settings",
                new { data = new { default_resolution = new[] { 1280, 720 } } });
            Assert.Equal((1280, 720), LaunchResolution.Read());

            // 2) 启动页写入那条路 → 设置页装载用的 get_settings 读得到同一个键
            //    (SettingsPage.SaveLoad.cs:99 就是这么读的)
            LaunchResolution.Write(1366, 768);
            var cfg = await _api.CallAsync<LauncherConfig>("get_settings");
            Assert.NotNull(cfg);
            List<object> seq = Py.ListOr(cfg.Get("default_resolution"));
            Assert.Equal(2, seq.Count);
            Assert.Equal(1366L, Py.Int(seq[0]));
            Assert.Equal(768L, Py.Int(seq[1]));

            // 3) 真落盘:换一份全新的 Config 实例(重读 config.json)也得看到同样的值
            var fresh = new LauncherConfig();
            List<object> onDisk = Py.ListOr(fresh.Get("default_resolution"));
            Assert.Equal(1366L, Py.Int(onDisk[0]));
            Assert.Equal(768L, Py.Int(onDisk[1]));

            // 4) 反向再确认一次:设置页改回 → 启动页读到新值(谁后改谁生效,不残留旧值)
            await _api.CallAsync<object>("save_settings",
                new { data = new { default_resolution = Py.L(1920, 1080) } });
            Assert.Equal((1920, 1080), LaunchResolution.Read());
        }
        finally
        {
            Restore(before);
        }
    });

    // ————————————————— 缺键 / 异常值回落 —————————————————

    [Fact]
    public void MissingOrBrokenValue_FallsBackTo854x480()
    {
        object? before = Snapshot();
        try
        {
            // 缺键(老 config.json / 手删)
            Config.CONFIG.data.Pop("default_resolution");
            Assert.Equal((854, 480), LaunchResolution.Read());
            Assert.Null(Config.CONFIG.Get("default_resolution"));

            // 结构不对 / 元素异常
            foreach (object broken in new[]
                     {
                         (object)"1280x720",                        // 字符串
                         (object)1280L,                             // 标量
                         (object)new PyDict(),                      // 字典
                         (object)new List<object>(),                // 空序列
                         (object)new List<object> { 1280L },        // 只有一个元素
                         (object)new List<object> { "abc", "def" }, // 非数字
                         (object)new List<object> { -1L, 0L },       // 非正数
                         (object)new List<object?> { null, null },   // None
                         (object)true,                              // 布尔
                     })
            {
                Config.CONFIG.Set("default_resolution", broken);
                Assert.Equal((854, 480), LaunchResolution.Read());
            }

            // 半坏:能认的槽位照用,认不出的那个才回落(宽认得出、高认不出)
            Config.CONFIG.Set("default_resolution", new List<object> { 1280L, "x" });
            Assert.Equal((1280, 480), LaunchResolution.Read());

            // 正常形态:数字字符串 / double 照 Py.Int 语义收下
            Config.CONFIG.Set("default_resolution", new List<object> { "1920", 1080.0 });
            Assert.Equal((1920, 1080), LaunchResolution.Read());

            // 合法的空槽位写法:0 / 负数不算合法(游戏侧 0 = 用游戏默认,这里不做那个语义)
            Config.CONFIG.Set("default_resolution", Py.L(854, 480));
            Assert.Equal((854, 480), LaunchResolution.Read());

            // 写入侧同样归一化:非法值不会落到设置里
            Assert.Equal((854, 480), LaunchResolution.Write(0, -3));
            Assert.Equal((854, 480), LaunchResolution.Read());
        }
        finally
        {
            Restore(before);
        }
    }

    // ————————————————— 启动页输入框 —————————————————

    [Fact]
    public void LaunchPageResolutionBoxes_ReadAndWriteTheSharedKey() => TestApp.Run(() =>
    {
        object? before = Snapshot();
        try
        {
            LaunchResolution.Write(1280, 720);

            var page = new LaunchPage();

            // 装载:设置里的值进输入框(ApplyResolution 由 FillCardDataAsync / OnActivate 调用)
            Invoke(page, "ApplyResolution");
            Assert.Equal("1280", Box(page, "_wBox").Text);
            Assert.Equal("720", Box(page, "_hBox").Text);

            // 用户改框 → SaveResolution 立即写进设置键(设置页随后读到的就是它)
            Box(page, "_wBox").Text = "1600";
            Box(page, "_hBox").Text = "900";
            Invoke(page, "SaveResolution");
            Assert.Equal((1600, 900), LaunchResolution.Read());

            // 手输垃圾 → 回落 854×480,并且把归一化后的值写回框里
            Box(page, "_wBox").Text = "abc";
            Box(page, "_hBox").Text = "-5";
            Invoke(page, "SaveResolution");
            Assert.Equal((854, 480), LaunchResolution.Read());
            Assert.Equal("854", Box(page, "_wBox").Text);
            Assert.Equal("480", Box(page, "_hBox").Text);

            // 清空(TextBox.Text 可以为 null)也不炸:同样回落默认
            Box(page, "_wBox").Text = null;
            Box(page, "_hBox").Text = "";
            Invoke(page, "SaveResolution");
            Assert.Equal((854, 480), LaunchResolution.Read());

            // 设置页改过之后,启动页再装载会同步过来(两个入口不各拿一份旧值)
            LaunchResolution.Write(1024, 768);
            Invoke(page, "ApplyResolution");
            Assert.Equal("1024", Box(page, "_wBox").Text);
            Assert.Equal("768", Box(page, "_hBox").Text);

            // 入口本身确实在配置浮层里(与 Windows BuildConfig:1504 一样,排在 Java / 内存 之后),
            // 且就是页面上那一对输入框,不是另建的一对
            var cfgBody = (Control)Invoke(page, "BuildConfig");
            List<Visual> tree = cfgBody.GetVisualDescendants().ToList();
            Assert.Contains("分辨率", tree.OfType<TextBlock>().Select(t => t.Text));
            Assert.Contains(tree, v => ReferenceEquals(v, Box(page, "_wBox")));
            Assert.Contains(tree, v => ReferenceEquals(v, Box(page, "_hBox")));
        }
        finally
        {
            Restore(before);
        }
    });

    // ————————————————— 启动时怎么用 —————————————————

    /// <summary>
    /// 启动链边界:LaunchCore 就是拿「框里的宽高」去调 LaunchFlow.ResolveResolution 的
    /// (LaunchCore.cs:192),版本设置里的 window_width/height 优先级更高。这里把这条关系钉住。
    /// </summary>
    [Fact]
    public void ResolveResolution_UsesConfiguredValue_UnlessVersionSettingsOverride()
    {
        var (w, h) = LaunchResolution.ParseText("1600", "900");
        Assert.Equal((1600, 900), (w, h));

        // 版本设置没写窗口大小 → 用配置的值
        object[] plain = LaunchFlow.ResolveResolution(PyDict.Of("window_mode", "window"), w, h);
        Assert.Equal(1600L, Py.Int(plain[0]));
        Assert.Equal(900L, Py.Int(plain[1]));

        // 版本设置写了 → 版本设置优先(照 Windows ResolveResolution)
        object[] pinned = LaunchFlow.ResolveResolution(
            PyDict.Of("window_mode", "window", "window_width", 1920L, "window_height", 1080L), w, h);
        Assert.Equal(1920L, Py.Int(pinned[0]));
        Assert.Equal(1080L, Py.Int(pinned[1]));

        // 全屏模式:不足 1280×720 时抬到下限
        object[] full = LaunchFlow.ResolveResolution(PyDict.Of("window_mode", "fullscreen"), 800, 600);
        Assert.Equal(1280L, Py.Int(full[0]));
        Assert.Equal(720L, Py.Int(full[1]));
    }

    // ————————————————— 浮层里的入口 —————————————————

    /// <summary>
    /// 分辨率入口在配置浮层的「更多设置」区里(照 Windows BuildConfig:1504 的位置),
    /// 所以浮层必须真的打得开:这条同时钉住 OpenConfigPopup 里那句把 BuildConfig 的返回值
    /// 强转成 Border 的历史写法 —— 真返回的是 StackPanel,一点齿轮就 InvalidCastException。
    /// </summary>
    [Fact]
    public void ConfigPopup_OpensAndExposesTheResolutionField() => TestApp.Run(() =>
    {
        var page = new LaunchPage();
        var win = new Window { Content = page, Width = 900, Height = 550 };
        win.Show();
        try
        {
            Invoke(page, "OpenConfigPopup"); // 必须不抛
            Render();

            var popup = (Popup?)page.GetType().GetField("_configPopup", All)!.GetValue(page);
            Assert.NotNull(popup);
            Assert.True(popup!.IsOpen);

            var shell = (Control)popup.Child!;
            List<Visual> tree = shell.GetVisualDescendants().ToList();
            Assert.Contains("启动配置", tree.OfType<TextBlock>().Select(t => t.Text));
            Assert.Contains("分辨率", tree.OfType<TextBlock>().Select(t => t.Text));
            Assert.Contains(tree, v => ReferenceEquals(v, Box(page, "_wBox")));
            Assert.Contains(tree, v => ReferenceEquals(v, Box(page, "_hBox")));

            // 分辨率收在默认折叠的「更多设置」里(与 Java / 内存 / 直连同组),展开按钮在面板上
            Assert.Contains("更多设置", tree.OfType<Button>().Select(b => (b.Content as TextBlock)?.Text));
        }
        finally
        {
            win.Close();
        }
    });

    // ————————————————— 工具 —————————————————

    /// <summary>推一次真布局(无头会话里 RunJobs 不触发 Measure/Arrange,要显式 tick 渲染计时器)。</summary>
    private static void Render()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();
    }

    private static object? Snapshot() => Config.CONFIG.Get("default_resolution");

    /// <summary>还原进程内共享的那份 Config(缺省键还原成默认值,而不是写个 null 进 config.json)。</summary>
    private static void Restore(object? before)
    {
        Config.CONFIG.Set("default_resolution",
            before ?? Py.L(LaunchResolution.DEFAULT_WIDTH, LaunchResolution.DEFAULT_HEIGHT));
        Config.CONFIG.Save();
    }

    private static TextBox Box(object page, string field)
        => (TextBox)page.GetType().GetField(field, All)!.GetValue(page)!;

    private static object Invoke(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, All)!.Invoke(target, args)!;
}
