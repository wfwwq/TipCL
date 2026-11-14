using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.Services;
using TipCL.Themes;
using Xunit;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;

namespace TipCL.Tests;

/// <summary>
/// C2c2:背景管线(<see cref="Wallpaper"/>)。用例体全在 <see cref="AvaloniaSession.Run(Action)"/> 里跑
/// (D-79);设置来源与轮播计时器、视频后端都走本包留的注入缝,所以不断后端、不联网、也不碰用户的图片
/// (临时文件一律在 <see cref="TestHome"/> 之下)。
/// </summary>
[Collection("C2c2Globals")]
public sealed class C2c2WallpaperTests
{
    // ---------------------------------------------------------------- 状态机

    /// <summary>【Windows 源】<c>Wallpaper.SetSource</c>:存在且能解码的图 → <c>Active = true</c>、没错误。</summary>
    [Fact]
    public void Set_source_with_a_real_image_activates_the_background()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            using ShellFixture shell = ShellFixture.Create();
            string image = C2c2Files.Preset(home, "preset-morning");

            Wallpaper.SetSource(image);

            Assert.True(Wallpaper.Active);
            Assert.Equal(image, Wallpaper.CurrentSource);
            Assert.Equal("", Wallpaper.Error);
            Assert.True(Wallpaper.StillLayer.IsVisible);
            Assert.NotNull(Wallpaper.StillLayer.Source);
        });
    }

    /// <summary>【Windows 源】<c>SetSource("")</c> → 取消背景(<c>Active = false</c>)。</summary>
    [Fact]
    public void Empty_source_turns_the_background_off()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            using ShellFixture shell = ShellFixture.Create();
            string image = C2c2Files.Preset(home, "preset-dusk");

            Wallpaper.SetSource(image);
            Assert.True(Wallpaper.Active);

            Wallpaper.SetSource("");

            Assert.False(Wallpaper.Active);
            Assert.Equal("", Wallpaper.CurrentSource);
            Assert.False(Wallpaper.StillLayer.IsVisible);
            Assert.Null(Wallpaper.StillLayer.Source);
        });
    }

    /// <summary>【Windows 源】路径不存在 → <c>Error = "文件不存在:" + path</c>、不激活。</summary>
    [Fact]
    public void Missing_file_reports_the_windows_error()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            using ShellFixture shell = ShellFixture.Create();
            string missing = Path.Combine(home.Dir, "nope.jpg");

            Wallpaper.SetSource(missing);

            Assert.False(Wallpaper.Active);
            Assert.Equal(I18n.L("文件不存在：") + missing, Wallpaper.Error);
            Assert.False(Wallpaper.StillLayer.IsVisible);
        });
    }

    /// <summary>【任务书】坏图必须走 Windows 的回落(<c>Error</c> + 不激活),不许把异常抛出去。</summary>
    [Fact]
    public void Corrupt_image_falls_back_instead_of_throwing()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            using ShellFixture shell = ShellFixture.Create();
            string broken = C2c2Files.Write(Path.Combine(home.Dir, "broken.png"), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

            Wallpaper.SetSource(broken);

            Assert.False(Wallpaper.Active);
            Assert.Equal(I18n.L("图片读不出来：") + broken, Wallpaper.Error);
            Assert.False(Wallpaper.StillLayer.IsVisible);
        });
    }

    /// <summary>【Windows 源 + mac 差异】同一张图连着设两次是幂等的(<c>path == _source</c> 早退);
    /// 换一张才会重新解码。</summary>
    [Fact]
    public void Setting_the_same_source_twice_is_a_no_op()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            using ShellFixture shell = ShellFixture.Create();
            string first = C2c2Files.Preset(home, "preset-morning");
            string second = C2c2Files.Preset(home, "preset-night");

            Wallpaper.SetSource(first);
            object? bitmap = Wallpaper.StillLayer.Source;
            Wallpaper.SetSource(first);

            Assert.Same(bitmap, Wallpaper.StillLayer.Source);

            Wallpaper.SetSource(second);

            Assert.Equal(second, Wallpaper.CurrentSource);
            Assert.NotSame(bitmap, Wallpaper.StillLayer.Source);
        });
    }

    /// <summary>【任务书】"应用它时没有窗口也不许抛":没挂外壳时设背景 / 设效果 / 主题重刷都只改状态。</summary>
    [Fact]
    public void Applying_without_a_window_does_not_throw()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            Wallpaper.Detach();
            string image = C2c2Files.Preset(home, "preset-sakura");

            Wallpaper.SetSource(image);
            Wallpaper.SetEffects(12, 40);
            Wallpaper.OnThemeChanged();
            Wallpaper.SetWindowActive(false);
            Wallpaper.SetWindowActive(true);

            Assert.True(Wallpaper.Active);
            Assert.Equal(12, Wallpaper.BlurValue);
            Assert.Equal(40, Wallpaper.DimPercent);

            Wallpaper.SetSource("");
            Wallpaper.Shutdown();

            Assert.False(Wallpaper.Active);
        });
    }

    /// <summary>【Windows 源】<c>ReloadAsync</c> 的第一道闸:<c>_host == null</c>(还没挂外壳)直接返回。</summary>
    [Fact]
    public void Reload_without_a_wallpaper_host_returns_immediately()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            FakeWallpaperSettings settings = new FakeWallpaperSettings(new Dictionary<string, object>());
            Wallpaper.Settings = settings;
            Wallpaper.Detach();

            Wallpaper.ReloadAsync().GetAwaiter().GetResult();

            Assert.Empty(settings.Asked);
            Assert.False(Wallpaper.Active);
        });
    }

    /// <summary>【Windows 源】后端没连上(<c>AppServices.Ready == false</c>)时也不读设置
    /// (Windows 是 <c>if (_host != null &amp;&amp; AppServices.Ready)</c>)。</summary>
    [Fact]
    public void Reload_without_a_backend_returns_immediately()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();
            Wallpaper.Settings = null;   // 默认来源 = 后端;测试进程里没有后端

            Wallpaper.ReloadAsync().GetAwaiter().GetResult();

            Assert.False(Wallpaper.Active);
            Assert.Equal("", Wallpaper.Error);
        });
    }

    // ---------------------------------------------------------------- 模糊 / 遮罩

    /// <summary>【Windows 源】<c>SetEffects</c>:模糊夹 0-40、遮罩夹 0-80,遮罩底色取 <c>C.Canvas</c>。</summary>
    [Fact]
    public void Set_effects_clamps_like_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            using ShellFixture shell = ShellFixture.Create();
            Wallpaper.SetSource(C2c2Files.Preset(home, "preset-morning"));

            Wallpaper.SetEffects(100, 100);

            Assert.Equal(C2c2Expected.BlurMax, Wallpaper.BlurValue);
            Assert.Equal(C2c2Expected.DimMax, Wallpaper.DimPercent);
            Assert.NotNull(Wallpaper.StillLayer.Effect);
            Assert.True(Wallpaper.DimLayer.IsVisible);
            Assert.Equal(0.8, ((Avalonia.Media.SolidColorBrush)Wallpaper.DimLayer.Fill!).Opacity, 3);

            Wallpaper.SetEffects(-5, -5);

            Assert.Equal(0, Wallpaper.BlurValue);
            Assert.Equal(0, Wallpaper.DimPercent);
            Assert.Null(Wallpaper.StillLayer.Effect);
            Assert.False(Wallpaper.DimLayer.IsVisible);
        });
    }

    /// <summary>【Windows 源】没有背景时遮罩不显示(<c>_dimPercent &lt;= 0 || !Active</c>)。</summary>
    [Fact]
    public void Dim_is_hidden_when_no_background_is_active()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();

            Wallpaper.SetSource("");
            Wallpaper.SetEffects(0, 50);

            Assert.False(Wallpaper.Active);
            Assert.False(Wallpaper.DimLayer.IsVisible);
        });
    }

    // ---------------------------------------------------------------- 外壳取色(ApplyChrome)

    /// <summary>【Windows 源】<c>ApplyChrome(on: true)</c>:根边框不透明兜底、标题栏玻璃、
    /// 页面底纹透明;取消背景之后回到主题实底、底纹不透明。</summary>
    [Fact]
    public void Chrome_follows_the_wallpaper_state()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            using ShellFixture shell = ShellFixture.Create();
            Rectangle wash = C2bAssert.ByName<Rectangle>(shell.Window, "PageWash");
            Border root = C2bAssert.ByName<Border>(shell.Window, "RootBorder");
            Border topBar = shell.Window.TopBar;

            Assert.Equal(1.0, wash.Opacity);
            Assert.Null(root.Background);

            Wallpaper.SetSource(C2c2Files.Preset(home, "preset-dusk"));

            Assert.Equal(0.0, wash.Opacity);
            Assert.NotNull(root.Background);
            Assert.Equal(Palette.Color("B.GlassStrong"), C1cExpected.ColourOf(topBar.Background));

            Wallpaper.SetSource("");

            Assert.Equal(1.0, wash.Opacity);
            Assert.Null(root.Background);
            Assert.Equal(Palette.Color("B.TitleBar"), C1cExpected.ColourOf(topBar.Background));
        });
    }

    /// <summary>【Windows 源】<c>Attach</c> 的两个图层进了外壳的背景宿主;换一个外壳也不会
    /// "一个控件两个父"(mac 侧一个进程里会有多个外壳,Windows 一辈子只有一个)。</summary>
    [Fact]
    public void Attach_moves_the_layers_between_shells()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture first = ShellFixture.Create();
            Grid hostOfFirst = C2bAssert.ByName<Grid>(first.Window, "WallHost");

            Assert.Contains(Wallpaper.StillLayer, hostOfFirst.Children);
            Assert.Contains(Wallpaper.DimLayer, hostOfFirst.Children);

            using ShellFixture second = ShellFixture.Create();
            Grid hostOfSecond = C2bAssert.ByName<Grid>(second.Window, "WallHost");

            Assert.DoesNotContain(Wallpaper.StillLayer, hostOfFirst.Children);
            Assert.Contains(Wallpaper.StillLayer, hostOfSecond.Children);
            Assert.Contains(Wallpaper.DimLayer, hostOfSecond.Children);
        });
    }

    // ---------------------------------------------------------------- 内置预设背景

    /// <summary>【Windows 源】<c>SettingsPage.cs:635-641</c> 的四个预设(名字与标签)。</summary>
    [Fact]
    public void Preset_table_matches_windows()
    {
        AvaloniaSession.Run(() =>
        {
            Assert.Equal(C2c2Expected.Presets, Wallpaper.Presets);
            Assert.Equal(4, Wallpaper.Presets.Length);
            Assert.Equal("晨雾", Wallpaper.Presets[0].Label);
            Assert.Equal("樱粉", Wallpaper.Presets[3].Label);
        });
    }

    /// <summary>【任务书】四个预设都真的内嵌在程序集里(打不开就说明资源没嵌进去)。</summary>
    [Fact]
    public void Every_preset_is_embedded()
    {
        AvaloniaSession.Run(() =>
        {
            foreach ((string file, string _) in C2c2Expected.Presets)
            {
                using Stream? stream = Wallpaper.OpenPreset(file);
                Assert.NotNull(stream);
                Assert.True(stream!.Length > 1000, file + " 的内嵌字节太少,不像是真图");
            }
        });
    }

    /// <summary>【Windows 源】预设的取址口径:<c>Resources/wallpapers/&lt;name&gt;.jpg</c>
    /// (Windows 是 pack URI,mac 是 avares URI)。</summary>
    [Fact]
    public void Preset_uri_uses_the_windows_path_shape()
    {
        AvaloniaSession.Run(() =>
        {
            Assert.Equal("avares://TipCL/Resources/wallpapers/preset-morning.jpg", Wallpaper.PresetUri("preset-morning"));
            Assert.Equal(Wallpaper.PresetUriPrefix, "avares://TipCL/Resources/wallpapers/");
        });
    }

    /// <summary>【任务书】不认识的预设名走 Windows 的回落:资源打不开(Windows 那边是
    /// <c>GetResourceStream</c> 抛异常,页面在 catch 里报错),释放也失败 —— 但不抛、不留半个文件。</summary>
    [Fact]
    public void Unknown_preset_falls_back_the_windows_way()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();

            Assert.False(Wallpaper.IsKnownPreset("preset-nope"));
            Assert.Null(Wallpaper.OpenPreset("preset-nope"));
            Assert.Equal("", Wallpaper.ReleasePreset("preset-nope"));
            Assert.False(File.Exists(Wallpaper.PresetPath("preset-nope")));

            foreach ((string file, string _) in C2c2Expected.Presets)
            {
                Assert.True(Wallpaper.IsKnownPreset(file), file + " 应当是内置预设");
            }
        });
    }

    /// <summary>【Windows 源】<c>SettingsPage.cs:664</c>:预设释放到数据目录的
    /// <c>wallpapers/&lt;name&gt;.jpg</c>,已经有了就不覆盖。</summary>
    [Fact]
    public void Release_preset_writes_under_the_data_dir_and_never_overwrites()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();

            string destination = Wallpaper.ReleasePreset("preset-night");

            Assert.Equal(Wallpaper.PresetPath("preset-night"), destination);
            Assert.StartsWith(TestHome.Root, destination);
            Assert.True(File.Exists(destination));

            byte[] first = File.ReadAllBytes(destination);
            byte[] sentinel = { 9, 9, 9 };
            File.WriteAllBytes(destination, sentinel);
            Assert.Equal(destination, Wallpaper.ReleasePreset("preset-night"));

            Assert.Equal(sentinel, File.ReadAllBytes(destination));
            Assert.NotEqual(first.Length, sentinel.Length);
        });
    }

    /// <summary>【任务书】预设走"释放到数据目录 → 再走 ui_background 管线"这条路真的能上背景。</summary>
    [Fact]
    public void Released_preset_can_be_applied()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            using ShellFixture shell = ShellFixture.Create();

            string path = Wallpaper.ReleasePreset("preset-morning");
            Wallpaper.SetSource(path);

            Assert.True(Wallpaper.Active);
            Assert.Equal("", Wallpaper.Error);
            Assert.NotNull(Wallpaper.StillLayer.Source);
        });
    }

    // ---------------------------------------------------------------- ReloadAsync / 轮播

    /// <summary>【Windows 源】<c>ReloadAsync</c> 读的六个键与默认值一字不差。</summary>
    [Fact]
    public void Reload_reads_the_six_windows_setting_keys()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using ShellFixture shell = ShellFixture.Create();
            FakeWallpaperSettings settings = new FakeWallpaperSettings(new Dictionary<string, object>());
            Wallpaper.Settings = settings;

            Wallpaper.ReloadAsync().GetAwaiter().GetResult();

            Assert.Equal(
                C2c2Expected.SettingKeys.Select(k => k.Key).ToList(),
                settings.Asked);
            Assert.False(Wallpaper.Active);
            Assert.False(Wallpaper.Rotating);
        });
    }

    /// <summary>【Windows 源】单图设置(<c>ui_background</c>)直接上背景;不是壁纸后缀的值一律当没设。</summary>
    [Fact]
    public void Reload_applies_the_single_image_setting()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            using ShellFixture shell = ShellFixture.Create();
            string image = C2c2Files.Preset(home, "preset-sakura");
            Wallpaper.Settings = new FakeWallpaperSettings(new Dictionary<string, object>
            {
                ["ui_background"] = image,
            });

            Wallpaper.ReloadAsync().GetAwaiter().GetResult();

            Assert.True(Wallpaper.Active);
            Assert.Equal(image, Wallpaper.CurrentSource);
            Assert.False(Wallpaper.Rotating);

            // 不是图片/视频后缀 → Windows 的 FileKinds.IsWallpaper 为假 → 当没设
            Wallpaper.Settings = new FakeWallpaperSettings(new Dictionary<string, object>
            {
                ["ui_background"] = Path.Combine(home.Dir, "notes.txt"),
            });
            Wallpaper.SetSource("");

            Wallpaper.ReloadAsync().GetAwaiter().GetResult();

            Assert.False(Wallpaper.Active);
        });
    }

    /// <summary>【Windows 源】文件夹轮播:扫描目录里能当壁纸的文件、按间隔起计时器、
    /// 到点换下一张(间隔夹 1-1440 分钟);模糊 / 遮罩一起落。</summary>
    [Fact]
    public void Reload_starts_the_folder_rotation_and_advances_on_tick()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            using ShellFixture shell = ShellFixture.Create();
            string folder = C2c2Files.Dir(home, "bg");
            string first = C2c2Files.Preset(home, "preset-morning");
            string second = C2c2Files.Preset(home, "preset-dusk");
            File.Copy(first, Path.Combine(folder, "01.jpg"), overwrite: true);
            File.Copy(second, Path.Combine(folder, "02.jpg"), overwrite: true);
            File.WriteAllText(Path.Combine(folder, "ignore.txt"), "not a wallpaper");

            FakeWallpaperTimer timer = new FakeWallpaperTimer();
            Wallpaper.TimerFactory = timer.Schedule;
            Wallpaper.Settings = new FakeWallpaperSettings(new Dictionary<string, object>
            {
                ["ui_background_folder"] = folder,
                ["ui_background_interval"] = 7,
                ["ui_background_blur"] = 9,
                ["ui_background_dim"] = 30,
            });

            Wallpaper.ReloadAsync().GetAwaiter().GetResult();

            Assert.True(Wallpaper.Rotating);
            Assert.Equal(2, Wallpaper.PlaylistCount);
            Assert.Equal(9, Wallpaper.BlurValue);
            Assert.Equal(30, Wallpaper.DimPercent);
            Assert.Equal(TimeSpan.FromMinutes(7.0), timer.Last.Delay);
            Assert.StartsWith(folder, Wallpaper.CurrentSource);
            Assert.True(File.Exists(Wallpaper.CurrentSource));

            string before = Wallpaper.CurrentSource;
            timer.Fire();

            Assert.NotEqual(before, Wallpaper.CurrentSource);
            Assert.StartsWith(folder, Wallpaper.CurrentSource);
            Assert.Equal(1, timer.LiveCount);
        });
    }

    /// <summary>【Windows 源】轮播间隔会被夹到 1-1440 分钟。</summary>
    [Fact]
    public void Rotation_interval_is_clamped_like_windows()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            using ShellFixture shell = ShellFixture.Create();
            string folder = C2c2Files.Dir(home, "bg");
            File.Copy(C2c2Files.Preset(home, "preset-morning"), Path.Combine(folder, "a.jpg"), overwrite: true);
            FakeWallpaperTimer timer = new FakeWallpaperTimer();
            Wallpaper.TimerFactory = timer.Schedule;
            Wallpaper.Settings = new FakeWallpaperSettings(new Dictionary<string, object>
            {
                ["ui_background_folder"] = folder,
                ["ui_background_interval"] = 99999,
            });

            Wallpaper.ReloadAsync().GetAwaiter().GetResult();

            Assert.Equal(TimeSpan.FromMinutes(C2c2Expected.IntervalMax), timer.Last.Delay);

            Wallpaper.Settings = new FakeWallpaperSettings(new Dictionary<string, object>
            {
                ["ui_background_folder"] = folder,
                ["ui_background_interval"] = 0,
            });
            Wallpaper.ReloadAsync().GetAwaiter().GetResult();

            Assert.Equal(TimeSpan.FromMinutes(C2c2Expected.IntervalMin), timer.All[1].Delay);
            Assert.Equal(1, timer.LiveCount);
        });
    }

    /// <summary>【Windows 源】把轮播文件夹取消(或目录里没有壁纸)→ 停表,单图优先。</summary>
    [Fact]
    public void Dropping_the_folder_stops_the_rotation()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            using ShellFixture shell = ShellFixture.Create();
            string folder = C2c2Files.Dir(home, "bg-empty");
            string image = C2c2Files.Preset(home, "preset-night");
            FakeWallpaperTimer timer = new FakeWallpaperTimer();
            Wallpaper.TimerFactory = timer.Schedule;
            Wallpaper.Settings = new FakeWallpaperSettings(new Dictionary<string, object>
            {
                ["ui_background_folder"] = folder,
                ["ui_background"] = image,
            });

            Wallpaper.ReloadAsync().GetAwaiter().GetResult();

            // 目录是空的 → 退回单图,且没有计时器
            Assert.False(Wallpaper.Rotating);
            Assert.Equal(0, timer.LiveCount);
            Assert.Equal(image, Wallpaper.CurrentSource);
            Assert.True(Wallpaper.Active);
        });
    }

    /// <summary>【Windows 源】<c>Shutdown</c> 停轮播(Windows 是 <c>_rotate.Stop()</c>)。</summary>
    [Fact]
    public void Shutdown_stops_the_rotation_timer()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            using ShellFixture shell = ShellFixture.Create();
            string folder = C2c2Files.Dir(home, "bg");
            File.Copy(C2c2Files.Preset(home, "preset-morning"), Path.Combine(folder, "a.jpg"), overwrite: true);
            FakeWallpaperTimer timer = new FakeWallpaperTimer();
            Wallpaper.TimerFactory = timer.Schedule;
            Wallpaper.Settings = new FakeWallpaperSettings(new Dictionary<string, object>
            {
                ["ui_background_folder"] = folder,
            });

            Wallpaper.ReloadAsync().GetAwaiter().GetResult();
            Assert.True(Wallpaper.Rotating);
            Assert.Equal(1, timer.LiveCount);

            Wallpaper.Shutdown();

            Assert.False(Wallpaper.Rotating);
            Assert.Equal(0, timer.LiveCount);
            Assert.True(timer.All[0].Stopped);
        });
    }

    /// <summary>【本机实测】测试 / 离屏环境里没有推进时钟的人,默认计时器一个都不挂
    /// (与 C2c1 的 Toast 计时器同一条纪律,见 D-105):于是"没有残留计时器"这件事本身就是断言。</summary>
    [Fact]
    public void Default_timer_is_not_armed_without_a_clock()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            using ShellFixture shell = ShellFixture.Create();
            string folder = C2c2Files.Dir(home, "bg");
            File.Copy(C2c2Files.Preset(home, "preset-morning"), Path.Combine(folder, "a.jpg"), overwrite: true);
            Wallpaper.TimerFactory = null;   // 走默认
            Wallpaper.Settings = new FakeWallpaperSettings(new Dictionary<string, object>
            {
                ["ui_background_folder"] = folder,
            });

            Wallpaper.ReloadAsync().GetAwaiter().GetResult();

            Assert.False(Motion.HasClock);
            Assert.False(Wallpaper.Rotating);
            Assert.True(Wallpaper.Active, "轮播不上表,但第一张图照样要上去");
        });
    }

    // ---------------------------------------------------------------- 视频壁纸(mac 侧不支持)

    /// <summary>【mac 差异,已记 DECISIONS】没有播放后端时:视频壁纸不激活,记一句原因,
    /// **绝不假装放上了**(Windows 那边会起 MediaElement 静音循环)。</summary>
    [Fact]
    public void Video_wallpaper_is_reported_as_unsupported()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            using ShellFixture shell = ShellFixture.Create();
            string video = C2c2Files.Write(Path.Combine(home.Dir, "loop.mp4"), new byte[] { 0, 0, 0, 1 });
            Wallpaper.VideoHost = null;

            Wallpaper.SetSource(video);

            Assert.False(Wallpaper.Active);
            Assert.False(Wallpaper.VideoPlaying);
            Assert.Equal(I18n.L("macOS 版还不支持视频壁纸:") + video, Wallpaper.Error);
        });
    }

    /// <summary>【Windows 源 + 注入缝】接上播放后端之后走 Windows 那条路:起播、跟着窗口激活
    /// 播 / 暂停、换图或收摊时停掉。</summary>
    [Fact]
    public void Video_wallpaper_uses_the_injected_player()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            using ShellFixture shell = ShellFixture.Create();
            string video = C2c2Files.Write(Path.Combine(home.Dir, "loop.mp4"), new byte[] { 0, 0, 0, 1 });
            FakeVideoHost host = new FakeVideoHost();
            Wallpaper.VideoHost = host;

            Wallpaper.SetSource(video);

            Assert.True(Wallpaper.Active);
            Assert.True(Wallpaper.VideoPlaying);
            Assert.Equal(Path.GetFullPath(video), Assert.Single(host.Started));
            Assert.True(host.Active.Count >= 1);
            Assert.True(host.Active[host.Active.Count - 1]);

            Wallpaper.SetWindowActive(false);
            Assert.False(host.Active[host.Active.Count - 1]);

            Wallpaper.SetWindowActive(true);
            Assert.True(host.Active[host.Active.Count - 1]);

            Wallpaper.SetSource("");
            Assert.False(Wallpaper.VideoPlaying);
            Assert.Equal(1, host.Stopped);
        });
    }

    /// <summary>【Windows 源】换图时先停视频(Windows <c>StopVideo</c> 在 <c>SetSource</c> 的第二句)。</summary>
    [Fact]
    public void Switching_to_a_still_stops_the_video()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            using ShellFixture shell = ShellFixture.Create();
            string video = C2c2Files.Write(Path.Combine(home.Dir, "loop.mp4"), new byte[] { 0, 0, 0, 1 });
            FakeVideoHost host = new FakeVideoHost();
            Wallpaper.VideoHost = host;
            Wallpaper.SetSource(video);

            Wallpaper.SetSource(C2c2Files.Preset(home, "preset-dusk"));

            Assert.Equal(1, host.Stopped);
            Assert.False(Wallpaper.VideoPlaying);
            Assert.NotNull(Wallpaper.StillLayer.Source);
        });
    }
}
