using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using TipCL.Models;
using TipCL.Pages;
using TipCL.Services;
using TipCL.Themes;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C5a:设置页(<see cref="SettingsPage"/>,导航 id <c>settings</c>)的注册 / 构造 / 静态结构 /
/// 主操作路由 / 缺件占位 / 主题生效。
///
/// 纪律:每条碰 Avalonia 的用例体都走 <c>AvaloniaSession.Run(...)</c>(D-79);全局静态由 <see cref="C5aScope"/>
/// 快照还原(D-69,含色板覆盖层);对话框一律"先推出来再点"(D-113);等待一律"一边泵一边看条件"(D-120);
/// 后端是按方法名路由的回环假后端(不起真后端、不出网、不启外部进程)。
/// 保存体直接用 <c>SettingsPage.SaveNowAsync()</c> 驱动 —— 真实路径由 500ms 防抖起头,
/// 而离屏的 <c>DispatcherTimer</c> 在那条泵里一次都不 Tick(D-129 实测),死等它必然假红。
/// </summary>
public class C5aSettingsTests
{
    // ================================================================ 注册与外壳

    /// <summary>【任务书】<c>settings</c> 必须由页面自己的模块初始化器注册好,并能按 id 建出真页面。</summary>
    [Fact]
    public void Settings_is_registered_by_its_module_initializer()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            Assert.True(PageRegistry.Contains(SettingsPageRegistration.PageId));
            Assert.Contains(SettingsPageRegistration.PageId, PageRegistry.Ids);
            Assert.IsType<SettingsPage>(PageRegistry.Create(SettingsPageRegistration.PageId));
            // 大小写敏感(D-94)
            Assert.Null(PageRegistry.Create("Settings"));
        });
    }

    /// <summary>【Windows 源】页面 id 常量与导航表一致(<c>NavModel.PageForNavKey["settings"]</c>)。</summary>
    [Fact]
    public void Settings_page_id_matches_the_nav_table()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            Assert.Equal("settings", SettingsPageRegistration.PageId);
            Assert.Equal("settings", NavModel.PageForNavKey["settings"]);
            Assert.Contains(MainWindow.NavDefs, d => d.Id == "settings");
        });
    }

    /// <summary>【任务书】外壳能按 id 解析并建出设置页(不动外壳代码,靠注册表)。</summary>
    [Fact]
    public void The_shell_resolves_the_settings_page_by_id()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            using var shell = ShellFixture.Create();
            Assert.Equal("launch", shell.Window.CurrentPageId);
            shell.Window.Navigate("settings");
            Assert.Equal("settings", shell.Window.CurrentPageId);
            Assert.IsType<SettingsPage>(shell.Window.CurrentPage);
        });
    }

    /// <summary>【任务书】没有后端时构造不许抛、不许卡(离屏现实 D-96:页面渲染它的预加载状态)。</summary>
    [Fact]
    public void Construction_does_not_throw_without_a_backend()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            AppServices.Client = null;
            SettingsPage page = new SettingsPage();
            Assert.NotNull(page.Content);
            Assert.Equal(I18n.L("设置"), page.Title);
        });
    }

    /// <summary>没有 client 时首屏加载如实降级:记一笔 + Toast「加载失败」,异常不抛给外壳。</summary>
    [Fact]
    public void Load_without_a_backend_degrades_instead_of_throwing()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var toasts = new C3cToasts();
            AppServices.Client = null;
            SettingsPage page = new SettingsPage();
            C5aFiles.Load(page);
            Assert.True(toasts.Has(I18n.L("加载失败")), "没有客户端时必须如实报「加载失败」:" + string.Join("|", toasts.Items.Select(t => t.Title + t.Body)));
        });
    }

    // ================================================================ 静态结构

    /// <summary>【Windows 源】<c>:745</c> 左导航七段(顺序与文案一字不改)。</summary>
    [Fact]
    public void Left_nav_is_the_seven_windows_segments()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            List<string> labels = C5aFiles.Descendants(page).OfType<ToggleButton>()
                .Select(b => (b.Content as TextBlock)?.Text ?? "")
                .Where(t => t.Length > 0)
                .ToList();
            Assert.Equal(
                new List<string> { I18n.L("通用"), I18n.L("账号"), "Java", I18n.L("下载与网络"), I18n.L("服务器"), I18n.L("游戏时长"), I18n.L("反馈与关于") },
                labels);
            // 每个都带 Seg.Item 类名(C1b 的类名表)
            ToggleButton first = C5aFiles.NavButton(page, I18n.L("通用"));
            Assert.Contains(StyleKeys.Map("Seg.Item"), first.Classes);
            // 首屏停在第一段
            Assert.True(first.IsChecked);
            // 【Windows 源】:120 宽 + 内边距 (12,8,12,8)
            Assert.Equal(120.0, Assert.IsType<TextBlock>(first.Content).Width);
            Assert.Equal(12.0, first.Padding.Left);
            Assert.Equal(8.0, first.Padding.Top);
        });
    }

    /// <summary>【Windows 源】<c>:720-731</c> 通用段 = <c>BlockHost("settings:general")</c> 的九个块(顺序/标题/内容)。</summary>
    [Fact]
    public void General_segment_is_the_nine_blocks_in_windows_order()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            BlockHost host = C5aFiles.Find<BlockHost>(page, "通用段的区块宿主");
            Assert.Equal(
                new[] { "appearance", "wallpaper", "themePack", "launcher", "game", "storage", "ai", "memory", "phoneSync" },
                host.OrderNow.Select(s => s.Id).ToArray());
            Assert.Equal(
                new[] { "外观", "壁纸", "主题包", "启动器", "游戏", "存储", "AI 助手", "记忆", "手机同步" }.Select(t => I18n.L(t)).ToArray(),
                host.OrderNow.Select(s => s.Title).ToArray());
            Assert.All(host.OrderNow, s => Assert.NotNull(s.Content));
            // Windows 这九个块默认全可见(不像启动页只有新闻块)
            Assert.Empty(host.HiddenNow);
        });
    }

    /// <summary>【Windows 源】<c>:732-735</c> 下载与网络段 = 单块 <c>BlockHost("settings:network")</c>。</summary>
    [Fact]
    public void Network_segment_is_a_single_block_host()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);
            C5aFiles.Click(C5aFiles.NavButton(page, I18n.L("下载与网络")));
            C5aFiles.Settle(2);

            Assert.True(C5aFiles.HasText(page, I18n.L("下载与网络")));
            Assert.True(C5aFiles.HasText(page, I18n.L("下载并发线程数")));
            Assert.True(C5aFiles.HasText(page, I18n.L("文件下载源")));
            // 版面键仍然在(切换分段不该把它弄丢)
            List<BlockHost> hosts = C5aFiles.Descendants(page).OfType<BlockHost>().ToList();
            Assert.Contains(hosts, h => h.OrderNow.Count == 1 && h.OrderNow[0].Id == "network");
        });
    }

    /// <summary>【Windows 源】<c>:572</c> 外观卡的六行(标签 + 提示文案)。</summary>
    [Fact]
    public void Appearance_card_has_the_windows_rows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            foreach (string label in new[] { "深色模式", "主题色", "界面背景", "界面动画", "下载飞入动画", "飞入动画时长" })
            {
                Assert.True(C5aFiles.HasText(page, I18n.L(label)), "外观卡少了「" + label + "」这一行");
            }
            foreach (string hint in new[] { "立即生效", "点一下就换", "预设色调,立即生效", "换页、进度条、悬浮动效", "安装时飞入侧栏任务列表", "毫秒,建议 400–800" })
            {
                Assert.True(C5aFiles.HasText(page, I18n.L(hint)), "外观卡少了提示「" + hint + "」");
            }
            // 三个开关都是 Switch 类名(Ui.Switch 的门面)
            Assert.True(C5aFiles.Descendants(page).OfType<CheckBox>().Count() >= 8);
        });
    }

    /// <summary>【Windows 源】<c>:139-275</c> 七个滑杆的范围 / 默认值 / 步进。</summary>
    [Fact]
    public void Sliders_match_windows_ranges_and_defaults()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            // 标签初值也照 Windows(要在切段**之前**断言:分段是换内容,切过去这行就离开可视树了)
            Assert.True(C5aFiles.HasText(page, "620 ms"));
            Assert.True(C5aFiles.HasText(page, "4096 MB"));
            List<Slider> sliders = C5aFiles.Descendants(page).OfType<Slider>().ToList();
            // 下载与网络那一段的两个滑杆不在可视树里(分段是**换内容**,不是全部挂上)——切过去再收一遍
            C5aFiles.Click(C5aFiles.NavButton(page, I18n.L("下载与网络")));
            C5aFiles.Settle(2);
            sliders.AddRange(C5aFiles.Descendants(page).OfType<Slider>());
            Assert.Equal(7, sliders.Count);
            Slider fly = sliders.Single(s => s.Minimum == 200.0 && s.Maximum == 1200.0);
            Assert.Equal(620.0, fly.Value);
            Assert.Equal(20.0, fly.TickFrequency);
            Slider threads = sliders.Single(s => s.Minimum == 1.0 && s.Maximum == 64.0);
            Assert.Equal(8.0, threads.Value);
            Slider segs = sliders.Single(s => s.Minimum == 1.0 && s.Maximum == 16.0);
            Assert.Equal(4.0, segs.Value);
            Slider mem = sliders.Single(s => s.Minimum == 512.0 && s.Maximum == 32768.0);
            Assert.Equal(4096.0, mem.Value);
            Assert.Equal(256.0, mem.TickFrequency);
            Assert.Equal(10.0, sliders.Single(s => s.Minimum == 1.0 && s.Maximum == 120.0).Value);
            Assert.Equal(0.0, sliders.Single(s => s.Minimum == 0.0 && s.Maximum == 40.0).Value);
            Assert.Equal(0.0, sliders.Single(s => s.Minimum == 0.0 && s.Maximum == 80.0).Value);
            Assert.True(sliders.Count > 0);
        });
    }

    /// <summary>【Windows 源】<c>:62-127</c> 九个带 key 的下拉的选项(顺序与文案)。</summary>
    [Fact]
    public void Combo_options_match_windows_keys_and_labels()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            List<string> allOptions = Options(page);
            // 分段 3(下载与网络)是**换内容**的,那一段的下拉不在可视树里 —— 切过去再收一遍
            C5aFiles.Click(C5aFiles.NavButton(page, I18n.L("下载与网络")));
            C5aFiles.Settle(2);
            allOptions.AddRange(Options(page));
            foreach (string label in new[]
            {
                "关闭(共用实例目录)", "隔离存档", "隔离 Mod 与配置", "隔离全部",
                "G1(推荐)", "调优 G1", "ZGC", "不指定",
                "保持显示", "最小化", "隐藏", "隐藏,退出后重开", "关闭启动器",
                "Minecraft 新闻", "本地 HTML", "空白",
                "窗口", "全屏",
                "自动(官方慢则走 BMCLAPI)", "仅官方", "仅 BMCLAPI",
                "自动", "仅 MCIM",
                "默认", "护眼绿", "暖米", "雾蓝",
                "内置网关(免配置)", "自定义 OpenAI 兼容端点",
                "简体中文", "English",
            })
            {
                Assert.Contains(I18n.L(label), allOptions);
            }
        });
    }

    /// <summary>【Windows 源】<c>:167-273</c> 十三个输入框(占位文案落在 Tag 上,与 Windows 的 <c>Ui.Input</c> 口径一致)。</summary>
    [Fact]
    public void Text_inputs_match_windows_placeholders()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            List<TextBox> boxes = C5aFiles.Descendants(page).OfType<TextBox>().ToList();
            List<string> tags = boxes.Select(b => b.Tag as string ?? "").ToList();
            // 分段 3 的两个输入框(限速 / CurseForge Key)也收进来
            C5aFiles.Click(C5aFiles.NavButton(page, I18n.L("下载与网络")));
            C5aFiles.Settle(2);
            tags.AddRange(C5aFiles.Descendants(page).OfType<TextBox>().Select(b => b.Tag as string ?? ""));
            foreach (string placeholder in new[]
            {
                "0", "854", "480", "例如 -XX:+UseG1GC(可空)", "本地 .html 文件路径(主页选「本地 HTML」时生效)",
                "游戏目录(.minecraft 的落脚处)", "https://api.openai.com/v1", "sk-...", "131072", "留空不启用",
                "CurseForge API Key(留空用内置)", "图片或视频路径,留空则不用壁纸", "壁纸文件夹(设了就轮播,优先于上面那张)",
            })
            {
                Assert.Contains(I18n.L(placeholder), tags);
            }
            // 占位是占位、初值另给(854/480/0 这三个的文本初值与占位相同,是 Windows 自己给的)
            // 7(通用段)+ 2(网络段)+ 4(游戏段也在通用段里)⇒ 13 个输入框,去重后仍是 13 个不同的占位
            Assert.True(tags.Distinct().Count() >= 13, "输入框占位:" + string.Join("|", tags));
        });
    }

    /// <summary>【Windows 源】<c>:129-137</c> + <c>:1085-1121</c> 六个主题色色块,当前色加 2px 描边。</summary>
    [Fact]
    public void Theme_swatches_are_the_six_windows_colours()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            SPanel swatches = C5aFiles.Descendants(page).OfType<Border>()
                .First(b => b.Width == 26.0 && b.Height == 26.0).Parent as SPanel ?? throw new InvalidOperationException("色块容器不在");
            Assert.Equal(6, swatches.Children.Count);
            List<Color> colours = swatches.Children.OfType<Border>()
                .Select(b => ((SolidColorBrush)Assert.IsType<Border>(b.Child).Background!).Color)
                .ToList();
            Assert.Equal(
                new[] { "#2E9B6B", "#2F72C4", "#7C5CFC", "#D68A17", "#D64545", "#149B9B" }.Select(Color.Parse).ToList(),
                colours);
            // 当前色(#2E9B6B,来自默认设置)是唯一带 2px 描边的那个
            List<Border> withBorder = swatches.Children.OfType<Border>().Where(b => b.BorderThickness.Left == 2.0).ToList();
            Assert.Single(withBorder);
            Assert.Equal(Color.Parse("#2E9B6B"), ((SolidColorBrush)Assert.IsType<Border>(withBorder[0].Child).Background!).Color);
        });
    }

    /// <summary>【Windows 源】<c>:635-685</c> 四个内置预设背景缩略图(标签 + Tag 是预设名,顺序一字不改)。</summary>
    [Fact]
    public void Preset_wallpaper_thumbs_are_the_four_windows_presets()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            List<Border> thumbs = C5aFiles.Descendants(page).OfType<Border>()
                .Where(b => b.Tag is string tag && tag.StartsWith("preset-", StringComparison.Ordinal))
                .ToList();
            Assert.Equal(new[] { "preset-morning", "preset-dusk", "preset-night", "preset-sakura" }, thumbs.Select(b => (string)b.Tag!).ToArray());
            // 【Windows 源】:64×40 圆角 6,内容是 Image
            Assert.All(thumbs, b =>
            {
                Assert.Equal(6.0, b.CornerRadius.TopLeft);
                Image img = Assert.IsType<Image>(b.Child);
                Assert.Equal(64.0, img.Width);
                Assert.Equal(40.0, img.Height);
                // C2c2 的内置壁纸真的解得出位图(资源缺失时这条必红)
                Assert.NotNull(img.Source);
            });
            Assert.Equal(I18n.L("晨雾"), ToolTip.GetTip(thumbs[0]) as string);
        });
    }

    /// <summary>【Windows 源】<c>:2006-2011</c> 关于卡片(版本 / 致谢),以及反馈段的组合结构。</summary>
    [Fact]
    public void About_card_and_the_feedback_view_structure()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);
            C5aFiles.Click(C5aFiles.NavButton(page, I18n.L("反馈与关于")));
            C5aFiles.Settle(2);

            Assert.True(C5aFiles.HasText(page, I18n.L("关于")), "「反馈与关于」段里没有「关于」;现有文案:" + string.Join("|", C5aFiles.Texts(page).Select(t => t.Text)));
            Assert.True(C5aFiles.HasText(page, I18n.L("TipCL 启动器")));
            Assert.True(C5aFiles.HasText(page, TipCL.Core.Launcher.AppInfo.APP_VERSION));
            Assert.True(C5aFiles.HasText(page, I18n.L("感谢每一位帮忙测试的朋友")));
            Assert.True(C5aFiles.HasText(page, "@368****168 \u2014 " + I18n.L("帮忙测试了正版(微软)验证")));
            // Windows 把「检查更新」钮复用进这一段的右上角
            Assert.True(C5aFiles.HasButton(page, I18n.L("检查更新")));
        });
    }

    // ================================================================ 注入缝(缺件如实占位)

    /// <summary>
    /// 【A 类差异 · 请 Lead 裁决】四个还没移植的分段(账号 / Java / 服务器 / 游戏时长)显示**如实占位**,
    /// 不空白、不假装能用。接上工厂之后占位换成真页面(下一条验)。
    /// </summary>
    [Fact]
    public void Missing_segments_show_the_honest_placeholder()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            // 【C6b 更正】C6b 的模块初始化器(C6bSegmentRegistration)已经把 Java / 服务器 / 游戏时长 / 反馈四个缝
            // 接到了真页面,「没接缝」这个时代前提对它们不再成立。按 D-126 的做法:**先把前提还原**(临时置空)、
            // 原来那两条断言一字不改地保留;顺带把「确实接了」显式钉下来(断言只增不减)。
            // 替换全局状态的用例**自己**负责还原(这里用 finally;见钉子用例那一条纪律)。
            Func<PageBase>? java = SettingsPage.JavaPageFactory;
            Func<PageBase>? servers = SettingsPage.ServersPageFactory;
            Func<PageBase>? playtime = SettingsPage.PlaytimePageFactory;
            Func<PageBase>? feedback = SettingsPage.FeedbackPageFactory;
            Assert.NotNull(java);
            Assert.NotNull(servers);
            Assert.NotNull(playtime);
            Assert.NotNull(feedback);
            try
            {
                SettingsPage.JavaPageFactory = null;
                SettingsPage.ServersPageFactory = null;
                SettingsPage.PlaytimePageFactory = null;
                SettingsPage.FeedbackPageFactory = null;

                foreach ((string label, string what) in new[]
                {
                    ("账号", "账号"), ("Java", "Java"), ("服务器", "服务器"), ("游戏时长", "游戏时长"), ("反馈与关于", "反馈与关于"),
                })
                {
                    C5aFiles.Click(C5aFiles.NavButton(page, I18n.L(label)));
                    C5aFiles.Settle(2);
                    Assert.True(C5aFiles.HasText(page, I18n.L("「{0}」页还没移植", what)), label + " 段应当显示如实占位");
                    Assert.True(C5aFiles.HasText(page, I18n.L("这一分段内嵌的是一个独立页面,属后续工作包;接上对应的注入缝后这里会自动显示")));
                }
            }
            finally
            {
                SettingsPage.JavaPageFactory = java;
                SettingsPage.ServersPageFactory = servers;
                SettingsPage.PlaytimePageFactory = playtime;
                SettingsPage.FeedbackPageFactory = feedback;
            }
        });
    }

    /// <summary>
    /// 【A 类差异】接上工厂之后真的显示探针页,而且**切到才建**(构造设置页不许连带建内嵌页 ——
    /// Windows 的 <c>_feedback</c> 字段初始化正是要改掉的那一处)。
    /// </summary>
    [Fact]
    public void Segment_factories_are_lazily_built_and_the_probe_shows()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            C5aProbePage.ResetCounters();
            SettingsPage.AccountPageFactory = () => C5aProbePage.Create("账号");
            SettingsPage.FeedbackPageFactory = () => C5aProbePage.Create("反馈");

            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);
            // 构造 + 首屏加载(停在通用段)都不许建内嵌页
            Assert.Equal(0, C5aProbePage.BuiltNow);

            C5aFiles.Click(C5aFiles.NavButton(page, I18n.L("账号")));
            C5aFiles.Settle(2);
            Assert.Equal(1, C5aProbePage.BuiltNow);
            Assert.True(C5aFiles.HasText(page, "探针:账号"));
            Assert.False(C5aFiles.HasText(page, I18n.L("「账号」页还没移植")));

            // 再切回去再切回来:同一段只建一次(与 Windows 的 _views[i] ??= new ... 同意图)
            C5aFiles.Click(C5aFiles.NavButton(page, I18n.L("通用")));
            C5aFiles.Settle(2);
            C5aFiles.Click(C5aFiles.NavButton(page, I18n.L("账号")));
            C5aFiles.Settle(2);
            Assert.Equal(1, C5aProbePage.BuiltNow);

            // 反馈段:视图是 Grid(上标题卡 + 下内嵌页),内嵌页这时才建
            C5aFiles.Click(C5aFiles.NavButton(page, I18n.L("反馈与关于")));
            C5aFiles.Settle(2);
            Assert.Equal(2, C5aProbePage.BuiltNow);
            Assert.True(C5aFiles.HasText(page, "探针:反馈"));
            Assert.True(C5aFiles.HasText(page, I18n.L("关于")));
        });
    }

    /// <summary>
    /// 【D-141 同族的钉子 · 跨包回归】设置页的六个分段注入缝(外加手机同步面板缝)是**进程级静态**:
    /// 用例把它们换成探针页之后,作用域**必须**把进入前的值写回去 —— 否则后一个测试类会莫名看到别人的探针
    /// (本包实测:C6b 的分段用例一开始就被污染)。这条用例进作用域换掉七个缝、出来逐个对拍。
    /// </summary>
    [Fact]
    public void The_segment_injection_seams_are_restored_when_the_scope_exits()
    {
        AvaloniaSession.Run(() =>
        {
            Func<PageBase>? beforeAccount = SettingsPage.AccountPageFactory;
            Func<PageBase>? beforeJava = SettingsPage.JavaPageFactory;
            Func<PageBase>? beforeServers = SettingsPage.ServersPageFactory;
            Func<PageBase>? beforePlaytime = SettingsPage.PlaytimePageFactory;
            Func<PageBase>? beforeFeedback = SettingsPage.FeedbackPageFactory;
            Func<Task>? beforeWizard = SettingsPage.FirstRunWizardRunner;
            Func<Task>? beforePhone = SettingsPage.PhoneSyncPanelRunner;

            using (var scope = C5aScope.Capture())
            {
                SettingsPage.AccountPageFactory = () => C5aProbePage.Create("账号");
                SettingsPage.JavaPageFactory = () => C5aProbePage.Create("Java");
                SettingsPage.ServersPageFactory = () => C5aProbePage.Create("服务器");
                SettingsPage.PlaytimePageFactory = () => C5aProbePage.Create("时长");
                SettingsPage.FeedbackPageFactory = () => C5aProbePage.Create("反馈");
                SettingsPage.FirstRunWizardRunner = () => Task.CompletedTask;
                SettingsPage.PhoneSyncPanelRunner = () => Task.CompletedTask;
            Assert.NotNull(SettingsPage.AccountPageFactory);
            }

            // 纪律:凡是故意替换全局状态的用例(**包括"验证还原"的这条钉子**),自己也要在关键处写回 ——
            // 这里作用域已经写回过一次,下面再逐个对拍;将来谁把作用域的还原改坏了,这条就会红。
            Assert.Same(beforeAccount, SettingsPage.AccountPageFactory);
            Assert.Same(beforeJava, SettingsPage.JavaPageFactory);
            Assert.Same(beforeServers, SettingsPage.ServersPageFactory);
            Assert.Same(beforePlaytime, SettingsPage.PlaytimePageFactory);
            Assert.Same(beforeFeedback, SettingsPage.FeedbackPageFactory);
            Assert.Same(beforeWizard, SettingsPage.FirstRunWizardRunner);
            Assert.Same(beforePhone, SettingsPage.PhoneSyncPanelRunner);
        });
    }

    /// <summary>
    /// 【D-126 前提更正 · task-16】「重新运行启动向导」的接缝:接之前它**没人接**,所以这条用例钉的是
    /// 「点了必须出那条如实降级 Toast(「启动向导还没移植」)」;注册文件 (<c>FirstRunWizardRegistration</c>)
    /// 把缝接到真向导之后,前提换成 **"点了必须真弹出四步向导"**:
    /// 1 缝上挂的就是 <c>FirstRunWizard.ShowAsync</c>(Windows 设置页 <c>SettingsPage.cs:718</c> 那条入口,
    /// <b>不是</b>启动路径的 <c>MaybeShowAsync</c>);2 点一下弹出向导那张卡(标题「欢迎使用 TipCL」+ 第 1 / 4 步);
    /// 3 点「跳过向导」真能关掉;4 缝仍然是一次一读(换成探针就调探针);5 降级 Toast 不该再出现。
    /// </summary>
    [Fact]
    public void First_run_wizard_seam_opens_the_real_wizard()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var toasts = new C3cToasts();
            using var api = C5aFiles.SettingsApi();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            // 1 缝上挂的是真向导的**设置页入口**(不是 null、也不是启动路径的 MaybeShowAsync)。
            Assert.NotNull(SettingsPage.FirstRunWizardRunner);
            Assert.Equal((Func<Task>)FirstRunWizard.ShowAsync, SettingsPage.FirstRunWizardRunner);

            // 2 点「重新运行」:四步向导真的弹出来。它是 Dlg.Panel 推的那张卡片,**不是** DialogCard
            // (C4Files.PanelCard 的注释就写着这条),所以按标题文字认,不用 C3bFiles.WaitDialog。
            C5aFiles.Click(C5aFiles.ByText(page, I18n.L("重新运行")));
            Control wizard = C4Files.PanelCard(I18n.L("欢迎使用 TipCL"));
            Assert.True(
                C4Files.HasText(wizard, I18n.L("第 {0} / {1} 步", 1, 4)),
                "向导第一步的步子计数(第 1 / 4 步)没出现");

            // 3 「跳过向导」真能关掉它(跳过 = 写 first_run=false 后收场,与 Windows 同);浮层不留残留。
            C5aFiles.Click(C5aFiles.ByText(wizard, I18n.L("跳过向导")));
            C5aFiles.Pump(() => C4Files.LayerCount() == 0);

            // 4 缝仍然是"每次点击现读":换成探针再点一次,探针必须被调到(旧用例的这条断言原样保留)。
            int ran = 0;
            SettingsPage.FirstRunWizardRunner = () =>
            {
                ran++;
                return Task.CompletedTask;
            };
            C5aFiles.Click(C5aFiles.ByText(page, I18n.L("重新运行")));
            C5aFiles.Pump(() => ran == 1);

            // 5 降级 Toast 从此不该再出现。
            Assert.False(toasts.Has(I18n.L("启动向导还没移植")));
        });
    }

    /// <summary>【D 类差异】「复制地址」接上剪贴板缝时**绝不碰真剪贴板**,而且拿到的是 MCP 地址。</summary>
    [Fact]
    public void The_clipboard_seam_receives_the_mcp_url()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            List<string> copied = new List<string>();
            SettingsPage.ClipboardWriter = (Control owner, string text) =>
            {
                copied.Add(text);
                return Task.FromResult(true);
            };
            C5aFiles.Click(C5aFiles.ByText(page, I18n.L("复制地址")));
            C5aFiles.Pump(() => copied.Count == 1);
            Assert.Equal(TipCL.Core.Launcher.Ai.McpServer.Url, copied[0]);
            Assert.Contains("127.0.0.1", copied[0], StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// 【U6 前提更正 · D-126】手机同步面板的接缝:U6 落地前它**没人接**,所以这条用例钉的是"点了必须出那条
    /// 如实降级 Toast(「手机同步面板还没移植」)";U6 把真面板 (<see cref="PhoneSyncPanel"/>) 与它的注册文件
    /// (<c>PhoneSyncPanelRegistration</c>) 一起落地后,缝默认就是接上的 ⇒ 前提换成 **"点了必须真弹出真面板"**。
    /// 旧的钉死断言(必须出降级 Toast)随前提一起删掉,替换为下面三条只增不改的补充:
    /// 1 缝上挂的就是真面板本身(注册文件的 <c>[ModuleInitializer]</c> 接上来的);2 点一下真的弹出面板那张卡;
    /// 3 降级 Toast 从此不该再出现。测试环境没连手机,所以面板开口第一句就是「请先在设置里连接手机」。
    /// </summary>
    [Fact]
    public void Phone_sync_panel_seam_opens_the_real_panel()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var toasts = new C3cToasts();
            using var api = C5aFiles.SettingsApi();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            // 1 缝上挂的是真面板(不是 null、也不是探针)。
            Assert.NotNull(SettingsPage.PhoneSyncPanelRunner);
            Assert.Equal((Func<Task>)PhoneSyncPanel.ShowAsync, SettingsPage.PhoneSyncPanelRunner);

            // 2 点「打开同步面板」:面板真的跑起来(测试环境没连手机 ⇒ 它的第一件事就是那句提示卡)。
            // 【本包实测的判据】不能用 Dlg.AnyOpen:C4LayerHost 的宿主 Grid 就是 Dlg.Host,而 C5aFiles.Mount
            // 把页面也挂在那上面 ⇒ AnyOpen 恒为 true。要判"有没有浮层"得数 DialogCard(C5aSettingsTests
            // 别处的浮层断言也是这个口径)。
            Assert.Empty(Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>());
            C5aFiles.Click(C5aFiles.ByText(page, I18n.L("打开同步面板")));
            DialogCard panel = C3bFiles.WaitDialog();
            Assert.Equal(I18n.L("手机同步"), panel.Title);
            Assert.True(
                C3bFiles.Descendants(panel).OfType<TextBox>().Any(box => string.Equals(box.Text, I18n.L("请先在设置里连接手机"), StringComparison.Ordinal)),
                "真面板的第一句提示(请先在设置里连接手机)没出现");

            // 关掉这张卡(它确实是可交互的真对话框),别把浮层留给后面的用例。
            C5aFiles.Click(C5aFiles.ByText(panel, I18n.L("知道了")));
            C5aFiles.Pump(() => !Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Any());

            // 3 降级 Toast 从此不该再出现。
            Assert.False(toasts.Has(I18n.L("手机同步面板还没移植")));
        });
    }

    // ================================================================ 加载与保存的路由

    /// <summary>【Windows 源】<c>:1295-1431</c> 首屏拉的那一批后端方法(名字与参数逐个对)。</summary>
    [Fact]
    public void Load_calls_the_windows_backend_methods()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            List<string> methods = api.Methods();
            Assert.Contains("get_settings", methods);
            Assert.Contains("get_instances", methods);
            Assert.Contains("get_setting", methods);
            Assert.Contains("allow_multi_instance", methods);
            Assert.Contains("available_languages", methods);
            Assert.Contains("get_language", methods);
            Assert.Contains("list_themes", methods);
            Assert.Contains("background_history", methods);
            Assert.Contains("can_undo_background", methods);
            Assert.Equal("default_instance", C5aFiles.ParamAt(api, "get_setting", "key"));
            // 加载完把设置里的值落到控件上(_threads/_segs/_mem/_w/_h)
            Assert.Equal(4096.0, C5aFiles.Descendants(page).OfType<Slider>().Single(s => s.Minimum == 512.0).Value);
        });
    }

    /// <summary>【Windows 源】<c>:1549-1685</c> 保存:整份载荷 + <c>set_multi_instance</c>(逐键抽检)。</summary>
    [Fact]
    public void Save_posts_the_full_windows_payload()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);
            C5aFiles.PumpTask(page.SaveNowAsync());

            // 按**最后一次** save_settings 断言(防抖自动保存可能中途也 Tick 过一次,见 C5aFixtures.ParamAt)
            int last = api.Count("save_settings") - 1;
            Assert.True(last >= 0, "一条 save_settings 都没收到");
            string Saved(string key) => C5aFiles.ParamAt(api, "save_settings", last, "data", key);

            Assert.Equal("false", Saved("ui_dark"));
            Assert.Equal("8", Saved("download_threads"));
            Assert.Equal("4096", Saved("default_memory_mb"));
            Assert.Equal("[854,480]", Saved("default_resolution"));
            Assert.Equal("#2E9B6B", Saved("theme_color"));
            Assert.Equal("default", Saved("ui_bg_preset"));
            Assert.Equal("builtin", Saved("ai_mode"));
            Assert.Equal("131072", Saved("ai_context_window"));
            Assert.Equal("true", Saved("use_system_proxy"));
            Assert.Equal("true", Saved("auto_check_update"));
            Assert.Equal("false", C5aFiles.ParamAt(api, "set_multi_instance", api.Count("set_multi_instance") - 1, "allow"));
            // 密钥留空时**不带**这两个键(Windows 同)
            Assert.False(C5aFiles.ParamHas(api, "save_settings", "curseforge_api_key"));
            Assert.False(C5aFiles.ParamHas(api, "save_settings", "ai_api_key"));
        });
    }

    /// <summary>【Windows 源】<c>:1641-1651</c> 多开开关走 <c>set_multi_instance</c>;打开时载荷为 true。</summary>
    [Fact]
    public void Save_sends_the_multi_instance_switch()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            CheckBox multi = C5aFiles.Descendants(page).OfType<CheckBox>().First();
            Assert.NotNull(multi);
            // 直接改控件状态(不模拟输入设备):找到「允许多开」那一行的开关
            CheckBox switchBox = FindSwitchAfterLabel(page, I18n.L("允许多开"));
            switchBox.IsChecked = true;
            C5aFiles.PumpTask(page.SaveNowAsync());
            Assert.Equal("true", C5aFiles.ParamAt(api, "set_multi_instance", api.Count("set_multi_instance") - 1, "allow"));
        });
    }

    /// <summary>【Windows 源】<c>:1687-1712</c> 语言变了才写 <c>set_language</c>,并问要不要立刻重启(走注入缝,不起真进程)。</summary>
    [Fact]
    public void Save_writes_the_language_change_and_offers_a_restart()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            ComboBox lang = C5aFiles.Descendants(page).OfType<ComboBox>()
                .First(c => c.Items.OfType<object>().Any(o => (o.ToString() ?? "") == I18n.L("English")));
            lang.SelectedIndex = 1;
            C5aFiles.Settle(2);

            List<string> launchedExe = new List<string>();
            SettingsPage.ProcessLauncher = path =>
            {
                launchedExe.Add(path);
                return true;
            };

            Task save = page.SaveNowAsync();
            C5aFiles.Pump(() => api.Count("set_language") >= 1, 9000);
            Assert.Equal("en-US", C5aFiles.ParamAt(api, "set_language", "lang"));

            // 确认框推出来再点(D-113)
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            Assert.Equal(I18n.L("语言已切换"), card.Title);
            C5aFiles.Click(C5aFiles.ByText(card, I18n.L("立即重启")));
            // 【D1 修法 · D-152】点完「立即重启」之后,注入缝(`ProcessLauncher`)是在**续体**里才被调到的,
            // 而那条断言在列表为空时是假红(实测只在 VSTest 通道红过一次)⇒ 先**等这个可观测效果**成立再断言。
            C5aFiles.Pump(() => launchedExe.Count >= 1, 9000);
            C5aFiles.PumpTask(save);
            // 注入缝被调到 = "要重启"那条路真的走了;测试里没有起任何真进程
            Assert.Contains(launchedExe, exe => exe.Length > 0 || exe.Length == 0);
        });
    }

    /// <summary>【Windows 源】<c>:1551-1569</c> 游戏目录被改过时先走 <c>set_game_dir</c>。</summary>
    [Fact]
    public void Save_switches_the_game_directory_through_set_game_dir()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi().Route("set_game_dir", "\"/new/games\"");
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            TextBox dir = C5aFiles.Descendants(page).OfType<TextBox>()
                .First(b => (b.Tag as string) == I18n.L("游戏目录(.minecraft 的落脚处)"));
            dir.Text = "/new/games";
            C5aFiles.PumpTask(page.SaveNowAsync());
            Assert.Equal("/new/games", C5aFiles.ParamAt(api, "set_game_dir", "path"));
            Assert.Equal("/new/games", dir.Text);
        });
    }

    // ================================================================ 主题包 / 检查更新 / 清理 / 推荐 / AI / 壁纸

    /// <summary>【Windows 源】<c>SettingsPage.Themes.cs:23-87</c> 主题包:列表 / 加载 / 保存 / 删除走的后端方法名。</summary>
    [Fact]
    public void Theme_pack_actions_call_the_windows_backend_methods()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi()
                .Route("list_themes", "[{\"name\":\"我的主题\",\"file\":\"a.json\",\"theme_color\":\"#2E9B6B\",\"ui_dark\":false}]")
                .Route("load_theme", "{}")
                .Route("delete_theme", "{}");
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            Assert.True(C5aFiles.HasText(page, I18n.L("共 {0} 个:", 1) + I18n.L("我的主题") + "(" + I18n.L("浅色") + " #2E9B6B)"));

            C5aFiles.Click(C5aFiles.ByText(page, I18n.L("加载")));
            C5aFiles.Pump(() => api.Count("load_theme") >= 1);
            Assert.Equal("我的主题", C5aFiles.ParamAt(api, "load_theme", "name"));
            C5aFiles.Pump(() => toasts.Has(I18n.L("已加载")));

            C5aFiles.Click(C5aFiles.ByText(page, I18n.L("删除")));
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            C5aFiles.Click(C5aFiles.ByText(card, I18n.L("删除")));
            C5aFiles.Pump(() => api.Count("delete_theme") >= 1);
            Assert.Equal("我的主题", C5aFiles.ParamAt(api, "delete_theme", "name"));
        });
    }

    /// <summary>【Windows 源】<c>SettingsPage.Themes.cs:32-56</c>「保存为主题包」先问名字,再 <c>save_settings</c> + <c>save_theme</c>。</summary>
    [Fact]
    public void Saving_a_theme_pack_prompts_then_calls_save_theme()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi().Route("save_theme", "{}");
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);
            // 视觉状态(主题色 / 背景预设)是 SettingsPage 的**进程级静态**,别的用例类可能动过它 ——
            // 这条用例断言的是"保存时把界面上的值原样发出去",所以界面状态必须由本用例自己钉死。
            page.PickSwatch("#2E9B6B");
            ComboBox bg = C5aFiles.Descendants(page).OfType<ComboBox>()
                .First(c => c.Items.OfType<object>().Any(o => (o.ToString() ?? "") == I18n.L("默认")));
            bg.SelectedIndex = 0;
            C5aFiles.Settle(2);

            C5aFiles.Click(C5aFiles.ByText(page, I18n.L("保存为主题包")));
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            Assert.Equal(I18n.L("保存主题包"), card.Title);
            C5aFiles.Descendants(card).OfType<TextBox>().First().Text = "我的主题";
            C5aFiles.Click(C5aFiles.ByText(card, I18n.L("确定")));

            C5aFiles.Pump(() => api.Count("save_theme") >= 1);
            Assert.Equal("我的主题", C5aFiles.ParamAt(api, "save_theme", "name"));
            // 按**最后一次** save_settings 断言:500ms 防抖那条自动保存可能在本用例中途也 Tick 过一次
            // (机器忙时才会,进程内通道因为快所以看不到 —— 本包在 VSTest 通道踩到过)
            int last = api.Count("save_settings") - 1;
            Assert.True(last >= 0, "一条 save_settings 都没收到");
            Assert.Equal("#2E9B6B", C5aFiles.ParamAt(api, "save_settings", last, "data", "theme_color"));
            Assert.Equal("false", C5aFiles.ParamAt(api, "save_settings", last, "data", "ui_dark"));
            Assert.Equal("default", C5aFiles.ParamAt(api, "save_settings", last, "data", "ui_bg_preset"));
            C5aFiles.Pump(() => toasts.Has(I18n.L("已保存")));
        });
    }

    /// <summary>【Windows 源】<c>SettingsPage.Themes.cs:89-118</c>:导入 / 导出都没选文件时如实降级(<c>Dlg</c> 在离屏一律取消)。</summary>
    [Fact]
    public void Import_and_export_theme_degrade_without_a_picker()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi().Route("import_theme", "\"我的主题\"");
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            // 没有主题包时导出的早退分支
            C5aFiles.Click(C5aFiles.ByText(page, I18n.L("导出")));
            C5aFiles.Pump(() => toasts.Has(I18n.L("没有主题包")));
            Assert.Equal(0, api.Count("export_theme"));
            // 导入:没有文件选择器 ⇒ 什么都不做(不调后端)
            C5aFiles.Click(C5aFiles.ByText(page, I18n.L("导入")));
            C5aFiles.Settle(4);
            Assert.Equal(0, api.Count("import_theme"));
        });
    }

    /// <summary>【Windows 源】<c>:1809-1832</c> 检查更新的三条路:没响应 / 已是最新 / 发现新版本并确认下载。</summary>
    [Fact]
    public void Check_update_paths_call_check_update_and_start_self_update()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi()
                .Route("check_update", "{\"has_update\":true,\"message\":\"有新版本\",\"latest\":\"9.9.9\",\"notes\":\"修了不少\",\"url\":\"\"}")
                .Route("start_self_update", "\"task-1\"");
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            // 「检查更新」钮在「反馈与关于」那一段的右上角(Windows 把它复用进那个区块)
            C5aFiles.Click(C5aFiles.NavButton(page, I18n.L("反馈与关于")));
            C5aFiles.Settle(2);
            C5aFiles.Click(C5aFiles.ByText(page, I18n.L("检查更新")));
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            Assert.Equal(I18n.L("发现新版本"), card.Title);
            C5aFiles.Click(C5aFiles.ByText(card, I18n.L("下载更新")));
            C5aFiles.Pump(() => api.Count("start_self_update") >= 1);
            C5aFiles.Pump(() => toasts.Has(I18n.L("正在下载更新")));
        });
    }

    /// <summary>【Windows 源】<c>:1834-1889</c> 清理:先 <c>cleaner_preview</c>,确认后把勾选的类别交给 <c>cleaner_apply</c>。</summary>
    [Fact]
    public void Clean_previews_then_sends_the_selected_kinds()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi()
                .Route("cleaner_preview", "{\"unused_libraries\":[{\"path\":\"a\",\"bytes\":10}],\"parts\":[{\"path\":\"b\",\"bytes\":20}],\"cache\":[{\"path\":\"c\",\"bytes\":30}],\"bytes\":60,\"count\":3}")
                .Route("cleaner_apply", "{\"removed\":3,\"bytes\":60}");
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            C5aFiles.Click(C5aFiles.ByText(page, I18n.L("扫描并清理")));
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            Assert.Equal(I18n.L("清理文件"), card.Title);
            // 三个勾选框默认都勾着(Windows 同),这里只留前两个来验"勾选真的进了载荷"
            List<CheckBox> boxes = C5aFiles.Descendants(card).OfType<CheckBox>().ToList();
            Assert.Equal(3, boxes.Count);
            Assert.All(boxes, b => Assert.True(b.IsChecked == true));
            boxes[2].IsChecked = false;
            C5aFiles.Click(C5aFiles.ByText(card, I18n.L("开始清理")));
            C5aFiles.Pump(() => api.Count("cleaner_apply") >= 1);
            string kinds = C5aFiles.ParamAt(api, "cleaner_apply", "kinds");
            Assert.Contains("unused_libraries", kinds, StringComparison.Ordinal);
            Assert.Contains("parts", kinds, StringComparison.Ordinal);
            Assert.DoesNotContain("cache", kinds, StringComparison.Ordinal);
            C5aFiles.Pump(() => toasts.Has(I18n.L("清理完成")));
        });
    }

    /// <summary>【Windows 源】<c>:1891-1925</c> 智能推荐:弹卡 → 应用 → 内存 / 分辨率 / 回收器落进控件。</summary>
    [Fact]
    public void Recommendation_applies_memory_resolution_and_gc()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi()
                .Route("get_smart_recommendation", "{\"memory_mb\":8192,\"java_major\":17,\"window_width\":1600,\"window_height\":900,\"gc_preset\":\"zgc\",\"cpu_count\":8,\"total_ram_gb\":16}");
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            C5aFiles.Click(C5aFiles.ByText(page, I18n.L("查看推荐")));
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            Assert.Equal(I18n.L("智能推荐"), card.Title);
            C5aFiles.Click(C5aFiles.ByText(card, I18n.L("应用推荐")));
            C5aFiles.Pump(() => toasts.Has(I18n.L("已应用")));

            Assert.Equal(8192.0, C5aFiles.Descendants(page).OfType<Slider>().Single(s => s.Minimum == 512.0).Value);
            Assert.Equal("1600", C5aFiles.Descendants(page).OfType<TextBox>().Single(b => (b.Tag as string) == "854").Text);
            Assert.Equal("900", C5aFiles.Descendants(page).OfType<TextBox>().Single(b => (b.Tag as string) == "480").Text);
        });
    }

    /// <summary>【Windows 源】<c>:1927-1946</c>「测试 AI 连接」把界面上的 AI 设置塞进 <c>settings</c> 发出去。</summary>
    [Fact]
    public void Test_ai_connection_sends_the_settings_dictionary()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi().Route("test_ai_connection", "\"连接正常\"");
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            C5aFiles.Click(C5aFiles.ByText(page, I18n.L("测试 AI 连接")));
            C5aFiles.Pump(() => api.Count("test_ai_connection") >= 1);
            Assert.Equal("builtin", C5aFiles.ParamAt(api, "test_ai_connection", "settings", "ai_mode"));
            Assert.True(C5aFiles.ParamHas(api, "test_ai_connection", "\"ai_base_url\""));
            C5aFiles.Pump(() => toasts.Has(I18n.L("AI 连接成功")));
        });
    }

    /// <summary>【Windows 源】<c>:1233-1293</c> 拉模型:内置模式**不拉**;自定义模式才调 <c>ai_list_models</c> 并填进下拉。</summary>
    [Fact]
    public void Fetch_models_is_skipped_for_the_builtin_gateway_and_runs_for_custom()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi().Route("ai_list_models", "{\"ok\":true,\"models\":[\"gpt-4o\",\"gpt-4o-mini\"],\"error\":\"\"}");
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            // 内置模式:点「刷新模型」什么都不做
            C5aFiles.Click(C5aFiles.ByText(page, I18n.L("刷新模型")));
            C5aFiles.Settle(4);
            Assert.Equal(0, api.Count("ai_list_models"));

            // 切到自定义模式(下拉第 2 项)再点:模型填进下拉
            ComboBox mode = C5aFiles.Descendants(page).OfType<ComboBox>()
                .First(c => c.Items.OfType<object>().Any(o => (o.ToString() ?? "") == I18n.L("自定义 OpenAI 兼容端点")));
            mode.SelectedIndex = 1;
            C5aFiles.Settle(2);
            C5aFiles.Click(C5aFiles.ByText(page, I18n.L("刷新模型")));
            C5aFiles.Pump(() => api.Count("ai_list_models") >= 1);
            ComboBox models = C5aFiles.Descendants(page).OfType<ComboBox>()
                .First(c => c.Items.OfType<object>().Any(o => (o.ToString() ?? "") == "gpt-4o"));
            Assert.Contains("gpt-4o-mini", models.Items.OfType<object>().Select(o => o.ToString()).ToList());
        });
    }

    /// <summary>【Windows 源】<c>:1714-1782</c> 壁纸:应用写 <c>ui_background*</c>、撤销走 <c>undo_background</c>、恢复出厂走 <c>reset_background</c>。</summary>
    [Fact]
    public void Wallpaper_apply_undo_and_reset_call_the_windows_methods()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi()
                .Route("undo_background", "{\"image\":\"/old.png\",\"folder\":\"\"}")
                .Route("reset_background", "{\"image\":\"\",\"folder\":\"\"}");
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            TextBox bgPath = C5aFiles.Descendants(page).OfType<TextBox>().First(b => (b.Tag as string) == I18n.L("图片或视频路径,留空则不用壁纸"));
            bgPath.Text = "/wall.png";
            C5aFiles.PumpTask(page.SaveNowAsync());
            Assert.Equal("/wall.png", C5aFiles.ParamAt(api, "save_settings", api.Count("save_settings") - 1, "data", "ui_background"));

            // 预设背景应用失败:注入缝没接线时也如实报(不静默)
            page.PickPreset("preset-不存在");
            Assert.True(bgPath.Text!.Length >= 0);
            Assert.Contains(C5aFiles.Texts(page), t => (t.Text ?? "").Contains("预设背景应用失败", StringComparison.Ordinal));

            C5aFiles.Click(C5aFiles.ByText(page, I18n.L("撤销上一次")));
            C5aFiles.Pump(() => api.Count("undo_background") >= 1);
            // 撤销回来的值要等续体落地再断言(请求到达 ≠ 续体跑完)
            C5aFiles.Pump(() => bgPath.Text == "/old.png");
            C5aFiles.Pump(() => toasts.Has(I18n.L("已撤销")));

            C5aFiles.Click(C5aFiles.ByText(page, I18n.L("恢复出厂")));
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            C5aFiles.Click(C5aFiles.ByText(card, I18n.L("恢复")));
            C5aFiles.Pump(() => api.Count("reset_background") >= 1);
            C5aFiles.Pump(() => toasts.Has(I18n.L("已恢复出厂壁纸")));
            Assert.Equal("", bgPath.Text);
        });
    }

    // ================================================================ 主题即时生效(B 类:主题层覆盖)

    /// <summary>
    /// 【Windows 源】<c>:1784-1795</c> 深色开关:立刻把主题切过去(mac 没有 <c>SetDark</c>,等价写法 =
    /// **应用级 + 窗口级都设**;只设应用级会被窗口级盖住,这条用例把它钉住)。
    /// </summary>
    [Fact]
    public void The_dark_switch_changes_the_application_variant()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            // 真外壳要在场:Windows 的深色是**窗口级**(MainWindow.SetDark),没有外壳时 mac 走的是
            // "只设应用级变体"那条弱分支 —— 那一条也要能验,但不是这条用例的重点。
            using var shell = ShellFixture.Create();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(shell.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            Assert.False(PaletteOverride.IsDarkNow);
            CheckBox dark = FindSwitchAfterLabel(page, I18n.L("深色模式"));
            dark.IsChecked = true;
            C5aFiles.Pump(() => PaletteOverride.IsDarkNow, 2000);
            // 三处一起看:应用级、窗口级(Windows 的 SetDark 是窗口级的)、以及全局读取口径
            Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
            Assert.Equal(ThemeVariant.Dark, shell.Window.RequestedThemeVariant);
            Assert.True(PaletteOverride.IsDarkNow);

            dark.IsChecked = false;
            C5aFiles.Pump(() => !PaletteOverride.IsDarkNow, 2000);
            Assert.Equal(ThemeVariant.Light, Application.Current.RequestedThemeVariant);
            Assert.Equal(ThemeVariant.Light, shell.Window.RequestedThemeVariant);
            Assert.False(PaletteOverride.IsDarkNow);
        });
    }

    /// <summary>
    /// 【C5a 更正 · 补 C3d 漏掉的消费方】设置页「下载飞入动画」开关必须真的管住 <c>FlyToTasks</c>:
    /// 开着时 chip 进 <c>FlyHost</c>,关掉时**什么都不做**(Windows 的 <c>MainWindow.FlyToTasks</c> 第一句就是
    /// <c>SettingsPage.FlyEnabled</c>)。
    /// </summary>
    [Fact]
    public void Fly_to_tasks_does_nothing_while_the_settings_switch_is_off()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            using var shell = ShellFixture.Create();
            using var clock = new C3dClock();
            Assert.True(clock.On, "动画时钟得先打开,否则 Motion.Enabled 恒假,这一条就验不了 FlyEnabled");

            // 先证明"开着的时候真的放 chip"
            SettingsPage.Extras.FlyAnimation = true;
            shell.Window.FlyToTasks(shell.Window.Sidebar, "飞行");
            C5aFiles.Pump(() => shell.Window.FlyHostPanel.Children.Count == 1);
            Assert.Single(shell.Window.FlyHostPanel.Children);
            C5aFiles.Pump(() => shell.Window.FlyHostPanel.Children.Count == 0);

            // 关掉设置页那个开关:与"关掉动画"同等结果 —— 一行都不画
            SettingsPage.Extras.FlyAnimation = false;
            Assert.False(SettingsPage.FlyEnabled);
            shell.Window.FlyToTasks(shell.Window.Sidebar, "飞行");
            C5aFiles.Settle(6);
            Assert.Empty(shell.Window.FlyHostPanel.Children);
        });
    }

    /// <summary>
    /// 【Windows 源】<c>SettingsPage.Themes.cs:207-267</c> 点色块 → 整组强调色写进**主题层覆盖**
    /// (<see cref="PaletteOverride"/>),全局读取口立刻看得见。
    /// </summary>
    [Fact]
    public void Clicking_a_swatch_applies_the_accent_through_the_palette_layer()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            page.PickSwatch("#2F72C4");
            C5aFiles.Settle(2);

            Assert.True(PaletteOverride.CountNow >= 20, "整组强调色都该进覆盖层,现在只有 " + PaletteOverride.CountNow + " 条");
            Assert.True(PaletteOverride.TryGetNow("C.Accent", out object? accent));
            Assert.Equal(Color.Parse("#2F72C4"), Palette.Color("C.Accent"));
            Assert.False(((Color)accent!).Equals(Color.Parse("#2E9B6B")));
            // B.AccentStrong 是按对比度补偿算过的实色(不是原始色),但必须与 C.Accent 那套同源
            Assert.Equal(ThemeVariant.Light, PaletteOverride.VariantNow);
            Assert.True(Palette.Brush("B.AccentStrong") is ISolidColorBrush);
        });
    }

    /// <summary>【Windows 源】<c>SettingsPage.Themes.cs:159-199</c> 背景预设:写 <c>C.Canvas</c> / <c>B.Canvas</c> / <c>B.PageWash</c>(渐变三站)。</summary>
    [Fact]
    public void Choosing_a_background_preset_writes_the_canvas_and_the_wash()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            ComboBox bg = C5aFiles.Descendants(page).OfType<ComboBox>()
                .First(c => c.Items.OfType<object>().Any(o => (o.ToString() ?? "") == I18n.L("护眼绿")));
            bg.SelectedIndex = 1; // 护眼绿(浅色下 #EBF4EC)
            C5aFiles.Settle(2);

            Assert.True(PaletteOverride.TryGetNow("C.Canvas", out object? canvas));
            Assert.Equal(Color.Parse("#EBF4EC"), (Color)canvas!);
            Assert.True(PaletteOverride.TryGetNow("B.PageWash", out object? wash));
            LinearGradientBrush brush = Assert.IsType<LinearGradientBrush>(wash);
            Assert.Equal(3, brush.GradientStops.Count);
            Assert.Equal(0.55, brush.GradientStops[1].Offset);
            Assert.Equal(Color.Parse("#EFF7F0"), brush.GradientStops[0].Color);
        });
    }

    /// <summary>【Windows 源】<c>SettingsPage.Themes.cs:144-154</c> 主题色是默认绿时**逐键撤掉覆盖**(等价于恢复内置色板)。</summary>
    [Fact]
    public void The_default_accent_removes_the_overrides()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            Color builtin = C1cExpected.ColourOf(Palette.Brush("B.AccentStrong"));
            page.PickSwatch("#D64545");
            C5aFiles.Settle(2);
            Assert.True(PaletteOverride.CountNow > 0);
            Assert.NotEqual(builtin, C1cExpected.ColourOf(Palette.Brush("B.AccentStrong")));

            // 把持久化主题色改回默认绿,再按它整份重刷一遍:覆盖层里那批强调色键被撤掉,颜色回到内置
            Assert.True(SettingsPage.SyncPersistedThemeColor("#2E9B6B"));
            page.PickSwatch("#2E9B6B");
            C5aFiles.Settle(2);
            Assert.Equal(0, PaletteOverride.CountNow);
            Assert.Equal(builtin, C1cExpected.ColourOf(Palette.Brush("B.AccentStrong")));
        });
    }

    /// <summary>【Windows 源】<c>:371-398</c> 外观附加项(<c>wpf-theme.json</c>)落在数据目录 —— 用例里指到 <c>TestHome.Root</c> 之下。</summary>
    [Fact]
    public void Extras_persist_under_the_test_home()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            string dir = Path.Combine(TestHome.Root, "c5a-theme-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            SettingsPage.ExtrasDirOverride = dir;

            Assert.True(SettingsPage.SyncPersistedThemeColor("#D68A17"));
            string file = Path.Combine(dir, "wpf-theme.json");
            Assert.True(File.Exists(file), "外观附加项应当落在数据目录里: " + file);
            string json = File.ReadAllText(file);
            Assert.Contains("\"ThemeColor\": \"#D68A17\"", json, StringComparison.Ordinal);
            Assert.Equal("#D68A17", SettingsPage.Extras.ThemeColor);
            Assert.Equal("#D68A17", SettingsPage.VisualThemeColor);
            // 非法色一律不写(Windows 的 NormalizeColor 口径)
            Assert.False(SettingsPage.SyncPersistedThemeColor("不是颜色"));
            Assert.Null(SettingsPage.NormalizeColor("#12345"));
            Assert.Equal("#2E9B6B", SettingsPage.NormalizeColor("#ff2e9b6b"));
        });
    }

    /// <summary>【Windows 源】<c>:980-1051</c> 记忆卡片:列出 <c>ai_memory.md</c> 的条目,清空要确认。</summary>
    [Fact]
    public void The_memory_card_lists_and_clears_the_memory_file()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi();
            TipCL.Core.Launcher.Ai.FileMemory.Remember("界面偏好:深色");
            TipCL.Core.Launcher.Ai.FileMemory.Remember("修好了 Java 路径");
            using var toasts = new C3cToasts();
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);
            C5aFiles.Click(C5aFiles.NavButton(page, I18n.L("通用")));
            C5aFiles.Settle(2);

            Assert.True(C5aFiles.HasText(page, I18n.L("共 {0} 条,新对话开始时会带给助手", 2)));
            Assert.True(C5aFiles.HasText(page, "界面偏好:深色"));
            Assert.True(C5aFiles.HasText(page, "修好了 Java 路径"));
            // 记忆文件落点在隔离目录里(红线:绝不碰真实数据目录)
            Assert.StartsWith(TestHome.Root, TipCL.Core.Launcher.Ai.FileMemory.FilePath, StringComparison.Ordinal);

            C5aFiles.Click(C5aFiles.ByText(page, I18n.L("清空全部")));
            C3bFiles.WaitDialog();
            DialogCard card = Dlg.Host!.GetLogicalDescendants().OfType<DialogCard>().Last();
            Assert.Equal(I18n.L("清空全部记忆?"), card.Title);
            C5aFiles.Click(C5aFiles.ByText(card, I18n.L("清空")));
            C5aFiles.Pump(() => toasts.Has(I18n.L("已清空记忆")));
            Assert.Null(C5aFiles.Descendants(page).OfType<TextBlock>().FirstOrDefault(t => t.Text == "界面偏好:深色"));
        });
    }

    /// <summary>【Windows 源】<c>:483-489</c> 语言预览:<c>translate</c> 三连调(+ 文案里带「预览」或「切换后界面会变成」)。</summary>
    [Fact]
    public void The_language_preview_calls_translate_three_times()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C5aScope.Capture();
            using var api = C5aFiles.SettingsApi().Route("translate", "\"Settings\"");
            using var harness = C4LayerHost.Show();
            // 读全局状态之前先自己钉死(D-141/D-144):主题变体(应用级 + 窗口级)、主题色、背景预设、外观附加项落点。
            // 收尾由 C5aScope 还原 —— 这样每一条都与"上一条用例留下了什么"无关。
            C5aFiles.PinTheme(harness.Window, ThemeVariant.Light);
            C5aFiles.PinAppearance();
            SettingsPage page = C5aFiles.Mount(harness, new SettingsPage());
            C5aFiles.Load(page);

            ComboBox lang = C5aFiles.Descendants(page).OfType<ComboBox>()
                .First(c => c.Items.OfType<object>().Any(o => (o.ToString() ?? "") == I18n.L("English")));
            lang.SelectedIndex = 1;
            // 等**效果**(预览文案落地)再断言,而不是"泵到调了 3 次就数次数":文案落下说明 3 次 translate 都回来了,
            // 这时候计数才确定(先计数会在续体还没跑完时假红 —— 本包在 VSTest 通道踩到过,见 C5a 的 DECISIONS)。
            string expected = I18n.L("切换后界面会变成:") + "Settings · Settings · Settings" + I18n.L("(重启后全量生效)");
            C5aFiles.Pump(() => C5aFiles.TextsWith(page, expected).Count > 0, 5000);
            Assert.True(C5aFiles.HasText(page, expected), "预览文案没落地;现有文案:" + string.Join("|", C5aFiles.Texts(page).Select(t => t.Text)));
            Assert.Equal(3, api.Count("translate"));
            Assert.Equal("en-US", C5aFiles.ParamAt(api, "translate", "lang"));
        });
    }

    // ---------------------------------------------------------------- 小工具

    /// <summary>按"设置行"的标签文案找到那一行里的开关(Windows 的 <c>Ui.Field(label, control, hint)</c> 结构)。</summary>
    private static CheckBox FindSwitchAfterLabel(Control page, string label)
    {
        TextBlock tag = C5aFiles.TextsWith(page, label).First();
        Grid? row = tag.GetLogicalAncestors().OfType<Grid>().FirstOrDefault(g => g.GetLogicalDescendants().OfType<CheckBox>().Any());
        Assert.True(row != null, "「" + label + "」那一行里没有开关");
        return row!.GetLogicalDescendants().OfType<CheckBox>().First();
    }

    /// <summary>当前可视树里所有下拉的选项文案(分段是**换内容**的,所以要按段分别收)。</summary>
    private static List<string> Options(Control page)
    {
        return C5aFiles.Descendants(page).OfType<ComboBox>()
            .SelectMany(c => c.Items.OfType<object>())
            .Select(o => o.ToString() ?? "")
            .ToList();
    }
}
