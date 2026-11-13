using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
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
/// C3c(一):注册 + 构造 + 静态结构。
///
/// 每条碰 Avalonia 的用例体都走 <c>AvaloniaSession.Run(...)</c>(D-79 / C1b-fix),没有第二个
/// <c>SetupWithoutStarting</c>;全局静态由 <see cref="C3cScope"/> 快照还原(D-69);
/// 布局/绑定的断言一律**先把页面挂进真窗口**(<c>UiHost.Show</c>)再读值,不猜像素。
/// 期望值的权威副本在 <see cref="C3cExpected"/>;每条都注明来源。
/// </summary>
public class C3cPageStructureTests
{
    // ---------------------------------------------------------------- 注册

    /// <summary>【任务书】页面 id 必须是 <c>launch</c>,且由页面自己的模块初始化器注册好。</summary>
    [Fact]
    public void Launch_is_registered_by_the_pages_module_initializer()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            Assert.True(PageRegistry.Contains(C3cExpected.PageId), "launch 必须在注册表里");
            Assert.Contains(C3cExpected.PageId, PageRegistry.Ids);
        });
    }

    /// <summary>【任务书】注册表能造出真的启动页;id 是**序号比较**、大小写敏感(D-94)。</summary>
    [Fact]
    public void Registry_creates_a_real_launch_page_and_is_case_sensitive()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            PageBase? page = PageRegistry.Create(C3cExpected.PageId);
            Assert.IsType<LaunchPage>(page);
            Assert.Null(PageRegistry.Create("Launch"));
            Assert.Null(PageRegistry.Create("LAUNCH"));
        });
    }

    /// <summary>【本机实测】<c>Register</c> 对同一 id 是覆盖语义,重复注册不抛也不会变成两页。</summary>
    [Fact]
    public void Register_is_idempotent()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            LaunchPageRegistration.Register();
            LaunchPageRegistration.Register();
            Assert.IsType<LaunchPage>(PageRegistry.Create(C3cExpected.PageId));
            Assert.Equal(1, PageRegistry.Ids.Count(id => id == C3cExpected.PageId));
        });
    }

    /// <summary>【Windows 源】页面 id 常量与导航表 / 导航键表一致(<c>MainWindow.NavDefs[0]</c>)。</summary>
    [Fact]
    public void Page_id_constant_matches_the_nav_table()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            Assert.Equal(C3cExpected.PageId, LaunchPageRegistration.PageId);
            Assert.Equal("launch", NavModel.PageForNavKey["launch"]);
            Assert.Equal("launch", MainWindow.NavDefs[0].Id);
            Assert.Equal(C3cExpected.PageId, MainWindow.NavDefs[0].Id);
        });
    }

    /// <summary>【Windows 源】外壳构造函数第一件事就是切「启动」页 —— 注册之后它真的切过去了。</summary>
    [Fact]
    public void The_shell_opens_the_launch_page_by_default()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create();
            Assert.Equal(C3cExpected.PageId, shell.Window.CurrentPageId);
            Assert.IsType<LaunchPage>(shell.Window.CurrentPage);
            Assert.IsType<LaunchPage>(shell.Window.Host.Content);
        });
    }

    /// <summary>【Windows 源】<c>PageHost</c> 同一个 id 只建一次(页面状态靠这个缓存留着)。</summary>
    [Fact]
    public void The_shell_reuses_one_page_instance()
    {
        AvaloniaSession.Run(() =>
        {
            using var shell = ShellFixture.Create();
            PageBase? first = shell.Window.GetPage(C3cExpected.PageId);
            PageBase? second = shell.Window.GetPage(C3cExpected.PageId);
            Assert.NotNull(first);
            Assert.Same(first, second);
            Assert.Equal(C3cExpected.PageId, first!.Id);
        });
    }

    // ---------------------------------------------------------------- 构造

    /// <summary>【任务书】没有后端(离屏现实:<c>AppServices.Client</c> 为 null)时构造也不许抛。</summary>
    [Fact]
    public void Construction_does_not_throw_without_a_backend()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            AppServices.Client = null;
            var page = new LaunchPage();
            Assert.NotNull(page.Content);
            Assert.Empty(PageRegistry.Ids.Where(id => id != C3cExpected.PageId && id != ""));
        });
    }

    /// <summary>【Windows 源】<c>Title =&gt; I18n.L("启动")</c>。</summary>
    [Fact]
    public void Title_is_the_translated_nav_title()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            Assert.Equal(I18n.L(C3cExpected.Title), page.Title);
        });
    }

    /// <summary>【Windows 源】<c>LaunchPage.cs:476-483</c>:五个区块的 id / 标题 / 默认可见性。</summary>
    [Fact]
    public void Blocks_are_the_five_windows_specs_in_order()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            BlockHost host = Assert.IsType<BlockHost>(page.BlocksNow);
            List<BlockSpec> specs = host.OrderNow.ToList();
            Assert.Equal(C3cExpected.BlockIds, specs.Select(s => s.Id).ToArray());
            Assert.Equal(C3cExpected.BlockTitles.Select(t => I18n.L(t)).ToArray(), specs.Select(s => s.Title).ToArray());
            Assert.Equal(C3cExpected.BlockDefaultVisible, specs.Select(s => s.DefaultVisible).ToArray());
            // 每个块都要有内容控件(Windows 也是每个 spec 都给了 Control)
            Assert.All(specs, s => Assert.NotNull(s.Content));
        });
    }

    /// <summary>【Windows 源】默认渲染只出 news 一块(其余 4 块默认收起)。</summary>
    [Fact]
    public void Only_the_news_block_renders_by_default()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            BlockHost host = Assert.IsType<BlockHost>(page.BlocksNow);
            Assert.Equal(new[] { "news" }, C3aTree.RenderedIds(host).ToArray());
            Assert.Equal(
                new[] { "tasks", "playtime", "notes", "log" },
                C3cExpected.BlockIds.Where(id => host.HiddenNow.Contains(id)).ToArray());
        });
    }

    /// <summary>【Windows 源】<c>LaunchPage.cs:485-489</c>:MaxHeight 绑定建起来了(0.62 那条)。</summary>
    [Fact]
    public void Blocks_max_height_binding_exists_and_follows_the_page_height()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            Assert.True(page.BlocksHeightBindingAlive);

            // 没挂窗口时页面 ActualHeight = 0 → 转换器返回 DoNothing → MaxHeight 保持默认(正无穷)。
            BlockHost host = Assert.IsType<BlockHost>(page.BlocksNow);
            Assert.Equal(double.PositiveInfinity, host.Root.MaxHeight);

            using var ui = UiHost.Show(page);
            C3cFiles.Settle();
            double pageHeight = page.Bounds.Height;
            Assert.True(pageHeight > 0.0, "页面挂进窗口后应当有真实高度");
            Assert.Equal(pageHeight * C3cExpected.BlocksMaxHeightPct, host.Root.MaxHeight, 1);
        });
    }

    /// <summary>【Windows 源】<c>LaunchPage.cs:483</c>:内容区块的 contentMargin = (56, 0, 24, 18)。</summary>
    [Fact]
    public void Blocks_content_margin_matches_the_hero()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            BlockHost host = Assert.IsType<BlockHost>(page.BlocksNow);
            SPanel list = C3aTree.ListOf(host);
            Assert.Equal(C3cExpected.BlocksMarginLeft, list.Margin.Left, 3);
            Assert.Equal(C3cExpected.BlocksMarginRight, list.Margin.Right, 3);
            Assert.Equal(C3cExpected.BlocksMarginBottom, list.Margin.Bottom, 3);
        });
    }

    /// <summary>【Windows 源】<c>LaunchPage.cs:491-497</c>:根 Grid 两行(<c>*,Auto</c>),背景层跨两行。</summary>
    [Fact]
    public void Root_grid_is_two_rows_with_the_background_spanned_over_both()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            Grid root = Assert.IsType<Grid>(page.Content);
            Assert.Equal(2, root.RowDefinitions.Count);
            Assert.Equal(GridLength.Star, root.RowDefinitions[0].Height);
            Assert.Equal(GridLength.Auto, root.RowDefinitions[1].Height);

            Assert.Equal(4, root.Children.Count);
            Assert.Equal(2, Grid.GetRowSpan(root.Children[0]));
            Assert.Equal(2, Grid.GetRowSpan(root.Children[1]));
            Assert.Same(page.BlocksNow!.Root, root.Children[3]);
            Assert.Equal(1, Grid.GetRow(root.Children[3]));
        });
    }

    /// <summary>【Windows 源】没有截图时 <c>ApplyHeroTone(false)</c>:背景图与遮罩都收起。</summary>
    [Fact]
    public void Hero_starts_in_the_no_image_tone()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            // 还没跑过 Dispatcher,背景解码的续体一定还没落地(RefreshBackground 里那两次 await)。
            Assert.False(page.OnImageNow);
            Grid root = Assert.IsType<Grid>(page.Content);
            Image bg = Assert.IsType<Image>(root.Children[0]);
            Grid shade = Assert.IsType<Grid>(root.Children[1]);
            Assert.False(bg.IsVisible);
            Assert.False(shade.IsVisible);
            Assert.Equal(2, shade.Children.Count); // 两层渐变遮罩(左→右 + 底→上)
            Assert.All(shade.Children, c => Assert.IsType<Avalonia.Controls.Shapes.Rectangle>(c));
        });
    }

    /// <summary>【Windows 源】<c>ApplyHeroTone(true)</c> 时背景层可见、标题带阴影、小标签行整体重刷。</summary>
    [Fact]
    public void Hero_tone_switches_the_background_and_the_title_shadow()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            using var ui = UiHost.Show(page);
            C3cFiles.Settle();
            Grid root = Assert.IsType<Grid>(page.Content);
            Image bg = Assert.IsType<Image>(root.Children[0]);

            page.ApplyHeroTone(true);
            Assert.True(page.OnImageNow);
            Assert.True(bg.IsVisible);
            Assert.IsType<DropShadowEffect>(C3cFiles.Texts(page).First(t => t.Text == I18n.L(C3cExpected.HeroNoInstance)).Effect);

            page.ApplyHeroTone(false);
            Assert.False(page.OnImageNow);
            Assert.False(bg.IsVisible);
            Assert.Null(C3cFiles.Texts(page).First(t => t.Text == I18n.L(C3cExpected.HeroNoInstance)).Effect);
        });
    }

    /// <summary>【Windows 源】<c>SyncHero</c> 的初始态:没有实例就显示「选择实例」+「就绪」+ 一个内存标签。
    /// 【Windows 源】<c>BuildHero</c> 末尾只调 <c>ApplyHeroTone</c> + <c>SyncHero</c>,**不**调 <c>SyncBanner</c>
    /// —— 所以横幅那两行到 <c>LoadAsync</c>(后端就绪)之前一直是空的。</summary>
    [Fact]
    public void Hero_title_kicker_and_chips_at_startup()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            Assert.Equal(I18n.L(C3cExpected.HeroNoInstance), page.HeroTitleNow);
            Assert.Equal(I18n.L(C3cExpected.ReadyText), page.StatusTextNow);
            Assert.Equal(new[] { "4 GB" }, page.ChipsNow.ToArray());
            Assert.Equal("", page.BannerTitleNow);
            Assert.Equal("", page.BannerSubNow);
        });
    }

    /// <summary>【Windows 源】「就绪」出现两次(kicker 与状态行),kicker 限宽 560、状态行不限宽。</summary>
    [Fact]
    public void Ready_text_appears_in_both_the_kicker_and_the_status_row()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            List<TextBlock> ready = C3cFiles.TextsWith(page, I18n.L(C3cExpected.ReadyText));
            Assert.Equal(2, ready.Count);
            TextBlock kicker = ready.Single(t => t.MaxWidth == C3cExpected.KickerMaxWidth);
            TextBlock status = ready.Single(t => double.IsPositiveInfinity(t.MaxWidth));
            Assert.Equal(C3cExpected.KickerFontSize, kicker.FontSize, 3);
            Assert.Equal(I18n.L(C3cExpected.ReadyText), status.Text);
        });
    }

    /// <summary>【Windows 源】<c>LaunchPage.cs:433-440</c>:空闲(无进度 + 状态是「就绪」)时进度行收起。</summary>
    [Fact]
    public void Status_row_is_hidden_while_idle()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            Assert.False(page.ProgressShownNow);
            SPanel statusRow = Assert.IsType<SPanel>(C3cFiles.Descendants(page)
                .OfType<SPanel>()
                .First(p => p.Children.OfType<ProgressBar>().Any()));
            Assert.False(statusRow.IsVisible);
        });
    }

    /// <summary>
    /// 【Windows 源】状态变更订阅的生命周期:进可视树挂上、出可视树摘掉
    /// (WPF 是 <c>DependencyPropertyDescriptor.AddValueChanged</c>,Avalonia 用 <c>PropertyChanged</c>;
    /// 页面是外壳缓存的长命对象,所以必须能摘 —— 见类注释)。
    /// </summary>
    [Fact]
    public void Status_change_subscriptions_follow_the_visual_tree()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            using var ui = UiHost.Show(page);
            C3cFiles.Settle();
            Assert.True(page.SyncHooksAttached, "进可视树后订阅必须挂上");

            // 摘下来(等价于"切到别的页面"):订阅必须跟着摘掉
            ui.Window.Content = null;
            C3cFiles.Settle();
            Assert.False(page.SyncHooksAttached, "出可视树后订阅必须摘掉");

            // 再挂回去还能挂上(不是"挂过一次就再也不挂")
            ui.Window.Content = page;
            C3cFiles.Settle();
            Assert.True(page.SyncHooksAttached);
        });
    }

    /// <summary>【Windows 源】主操作行:初始显示「启动游戏」外壳、不显示「停止」外壳、停止钮禁用。</summary>
    [Fact]
    public void Launch_shell_and_stop_shell_at_startup()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            Assert.True(page.LaunchShownNow);
            Assert.False(page.StopShownNow);

            Button stop = C3cFiles.ByText(page, I18n.L("停止"));
            Assert.False(stop.IsEnabled);
            Button launch = C3cFiles.ByText(page, I18n.L("启动游戏"));
            Assert.True(launch.IsEnabled);

            Border shell = Assert.IsType<Border>(launch.Parent);
            Assert.Equal(C3cExpected.LaunchShellHeight, shell.Height, 3);
            Assert.Equal(C3cExpected.LaunchShellCorner, shell.CornerRadius.TopLeft, 3);
            Assert.Equal(C3cExpected.LaunchShellPadding, shell.Padding.Left, 3);
            Assert.IsType<DropShadowEffect>(shell.Effect);
        });
    }

    /// <summary>【Windows 源】<c>LaunchPage.cs:395-421</c>:主按钮的两个样式键都落在类名上(C1b 的类名表)。</summary>
    [Fact]
    public void Launch_button_carries_both_expected_class_tokens()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            Button launch = C3cFiles.ByText(page, I18n.L("启动游戏"));
            string primary = StyleKeys.Map("Btn.Primary");
            string ghost = StyleKeys.Map("Btn.Ghost");
            Assert.False(string.IsNullOrEmpty(primary));
            Assert.False(string.IsNullOrEmpty(ghost));
            Assert.Contains(primary, launch.Classes);
            // Windows 在 BuildHero 里把主按钮的样式覆盖成 Btn.Ghost(外层 Border 才是主色)
            Assert.Contains(ghost, launch.Classes);
            Assert.Equal(C3cExpected.LaunchButtonFontSize, launch.FontSize, 3);
        });
    }

    /// <summary>【Windows 源】<c>LaunchPage.cs:363-369</c>:进度条的几何与不透明度。</summary>
    [Fact]
    public void Progress_bar_geometry()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            ProgressBar prog = C3cFiles.Find<ProgressBar>(page, "进度条");
            Assert.Equal(C3cExpected.ProgressHeight, prog.Height, 3);
            Assert.Equal(C3cExpected.ProgressWidth, prog.Width, 3);
            Assert.Equal(C3cExpected.ProgressOpacity, prog.Opacity, 3);
            Assert.False(prog.IsVisible);
        });
    }

    /// <summary>【Windows 源】英雄区 / 大标题 / kicker / 小标签行的尺寸与边距。</summary>
    [Fact]
    public void Hero_geometry()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            Grid root = Assert.IsType<Grid>(page.Content);
            SPanel hero = Assert.IsType<SPanel>(root.Children[2]);
            Assert.Equal(C3cExpected.HeroMaxWidth, hero.MaxWidth, 3);
            Assert.Equal(HorizontalAlignment.Left, hero.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Center, hero.VerticalAlignment);
            Assert.Equal(C3cExpected.HeroMarginLeft, hero.Margin.Left, 3);
            Assert.Equal(C3cExpected.HeroMarginRight, hero.Margin.Right, 3);

            TextBlock title = C3cFiles.TextsWith(page, I18n.L(C3cExpected.HeroNoInstance)).Single();
            Assert.Equal(C3cExpected.HeroTitleFontSize, title.FontSize, 3);
            Assert.Equal(FontWeight.ExtraBold, title.FontWeight);
            Assert.Equal(C3cExpected.HeroTitleMaxWidth, title.MaxWidth, 3);
            Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);

            // 「就绪」出现两次:kicker(限宽 560)与状态行(不限宽)
            TextBlock kicker = C3cFiles.TextsWith(page, I18n.L(C3cExpected.ReadyText))
                .Single(t => t.MaxWidth == C3cExpected.KickerMaxWidth);
            Assert.Equal(C3cExpected.KickerFontSize, kicker.FontSize, 3);
        });
    }

    /// <summary>【Windows 源】<c>LaunchPage.cs:446-471</c>:新闻卡的圆角 / 边框 / 内边距与「新闻」标签。</summary>
    [Fact]
    public void News_card_structure()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            Border tag = C3cFiles.BorderWithText(page, I18n.L("新闻"));
            Assert.Equal(6.0, tag.CornerRadius.TopLeft, 3);
            // 【Windows 源】tag 只设了圆角/内边距/垂直居中,没有描边(描边在新闻卡上)
            Assert.Equal(0.0, tag.BorderThickness.Left, 3);
            Assert.Equal(8.0, tag.Padding.Left, 3);
            Assert.Equal(2.0, tag.Padding.Top, 3);
            Assert.Equal(3.0, tag.Padding.Bottom, 3);
            Assert.Equal(VerticalAlignment.Center, tag.VerticalAlignment);
            Assert.Equal(11.0, Assert.IsType<TextBlock>(tag.Child).FontSize, 3);

            Border card = Assert.IsType<Border>(page.NewsCardNow);
            Assert.Equal(12.0, card.CornerRadius.TopLeft, 3);
            Assert.Equal(1.0, card.BorderThickness.Left, 3);
            Assert.Equal(8.0, card.Padding.Left, 3);
            Assert.Equal(14.0, card.Padding.Right, 3);
            Assert.Equal(HorizontalAlignment.Left, card.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Bottom, card.VerticalAlignment);

            List<TextBlock> tickers = C3cFiles.Texts(page)
                .Where(t => t.FontSize == C3cExpected.NewsTickerFontSize && t.MaxWidth == 520.0)
                .ToList();
            Assert.Single(tickers);
            Assert.Equal(TextTrimming.CharacterEllipsis, tickers[0].TextTrimming);
            // 新闻还没加载:跑马灯是空的(WPF 里 TextBlock.Text 默认 ""、Avalonia 里默认 null,所以判空串)
            Assert.True(string.IsNullOrEmpty(tickers[0].Text));
        });
    }

    /// <summary>【Windows 源】配置浮层里的控件(实例/版本/用户/Java/内存/分辨率/直连/两个登录钮/更多设置)。</summary>
    [Fact]
    public void Config_body_has_the_windows_fields()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            page.OpenPopupForDevShot("config");
            Border shell = Assert.IsType<Border>(page.ConfigShellNow);
            Assert.True(page.ConfigPopupOpenNow);

            List<TextBlock> texts = C3cFiles.Texts(shell);
            foreach (string label in new[] { "实例", "版本", "用户", "Java", "内存", "分辨率", "直连" })
            {
                Assert.Contains(texts, t => t.Text == I18n.L(label));
            }

            Assert.True(C3cFiles.HasButton(shell, I18n.L(C3cExpected.MicrosoftLoginText)));
            Assert.True(C3cFiles.HasButton(shell, I18n.L(C3cExpected.AuthlibLoginText)));
            Assert.True(C3cFiles.HasButton(shell, I18n.L(C3cExpected.MoreSettingsText)));
            Assert.True(C3cFiles.HasButton(shell, I18n.L(C3cExpected.MoreActionsTip)));

            Assert.Equal(4, C3cFiles.Descendants(shell).OfType<ComboBox>().Count());
            // 【Windows 源】BuildConfig 里的输入框只有三个:分辨率 854 / 480 与「直连」;
            // 「用户」那一格放的是账号下拉(<c>_acc</c>),而 `_user`(Player)在这个版本的 Windows 源码里
            // **没有被放进配置面板**(字段保留着,SyncBanner 也不用它)—— 逐字照抄,不"顺手"加进去。
            // 注意 Windows 的调用是 Ui.Input("854", "", 74.0):第 1 个参数是**占位**(mac 侧落在 Tag 上),
            // 第 2 个才是文本 —— 所以 854 / 480 是占位文案,不是初值。
            List<TextBox> boxes = C3cFiles.Descendants(shell).OfType<TextBox>()
                .Where(b => b.FindLogicalAncestorOfType<ComboBox>() == null)
                .ToList();
            Assert.Equal(3, boxes.Count);
            List<string> tags = boxes.Select(b => (b.Tag as string) ?? "(null)").ToList();
            Assert.Contains("854", tags);
            Assert.Contains("480", tags);
            Assert.Contains(I18n.L(C3cExpected.ServerPlaceholder), tags);
            Assert.All(boxes, b => Assert.Equal("", b.Text));
        });
    }

    private static string Describe(Control root)
    {
        List<Control> all = C3cFiles.Descendants(root).ToList();
        return "逻辑树:" + string.Join(" / ", all.Select(c => c.GetType().Name + "#" + all.IndexOf(c)));
    }

    /// <summary>【Windows 源】<c>LaunchPage.cs:1487-1490</c>:内存滑杆的范围 / 步进 / 默认值 + 标签。</summary>
    [Fact]
    public void Memory_slider_range_and_label()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            page.OpenPopupForDevShot("config");
            Slider slider = C3cFiles.Find<Slider>(page.ConfigShellNow!, "内存滑杆");
            Assert.Equal(C3cExpected.MemMin, slider.Minimum, 3);
            Assert.Equal(C3cExpected.MemMax, slider.Maximum, 3);
            Assert.Equal(C3cExpected.MemDefault, slider.Value, 3);
            Assert.Equal(C3cExpected.MemTick, slider.TickFrequency, 3);
            Assert.True(C3cFiles.HasText(page.ConfigShellNow!, C3cExpected.MemLabelDefault));
        });
    }

    /// <summary>【Windows 源】<c>LaunchPage.cs:1503-1505</c>:高级区默认折叠。</summary>
    [Fact]
    public void Advanced_settings_start_collapsed()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            page.OpenPopupForDevShot("config");
            Border shell = Assert.IsType<Border>(page.ConfigShellNow);
            Assert.True(C3cFiles.HasButton(shell, I18n.L(C3cExpected.MoreSettingsText)));

            // Java 那一行在折叠区里:它的某个 SPanel 祖先不可见(而 Java 控件本身照样建好了)
            TextBlock javaLabel = C3cFiles.Texts(shell).First(t => t.Text == I18n.L("Java"));
            SPanel? hidden = javaLabel.GetLogicalAncestors().OfType<SPanel>().FirstOrDefault(p => !p.IsVisible);
            Assert.NotNull(hidden);
            Assert.True(hidden!.Children.Count >= 4, "折叠区里应当有 Java / 内存 / 分辨率 / 直连 四项");
        });
    }

    /// <summary>【Windows 源】<c>LaunchPage.cs:144-150</c>:「停止」外壳的尺寸与主色键落点。</summary>
    [Fact]
    public void Stop_shell_geometry()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            using var ui = UiHost.Show(page);
            C3cFiles.Settle();
            Button stop = C3cFiles.ByText(page, I18n.L("停止"));
            Border shell = Assert.IsType<Border>(stop.Parent);
            Assert.Equal(C3cExpected.StopShellHeight, shell.Height, 3);
            Assert.Equal(C3cExpected.StopShellCorner, shell.CornerRadius.TopLeft, 3);
            Assert.Equal(C3cExpected.StopShellPadding, shell.Padding.Left, 3);
            Assert.False(shell.IsVisible);
            AssertBrush(Palette.Brush("B.DangerSoft"), shell.Background);
        });
    }

    /// <summary>【Windows 源】主按钮外壳底色取 <c>B.AccentStrong</c>(挂进窗口后动态资源才解析)。</summary>
    [Fact]
    public void Launch_shell_uses_the_accent_strong_brush()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            using var ui = UiHost.Show(page);
            C3cFiles.Settle();
            Button launch = C3cFiles.ByText(page, I18n.L("启动游戏"));
            Border shell = Assert.IsType<Border>(launch.Parent);
            AssertBrush(Palette.Brush("B.AccentStrong"), shell.Background);
        });
    }

    /// <summary>【Windows 源】<c>LaunchPage.cs:684-695</c>:chip = 圆角 8 / 描边 1 / 内边距 (10,3,10,4) / 12 号字。</summary>
    [Fact]
    public void Chip_geometry()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            Border chip = C3cFiles.BorderWithText(page, "4 GB");
            Assert.Equal(8.0, chip.CornerRadius.TopLeft, 3);
            Assert.Equal(1.0, chip.BorderThickness.Left, 3);
            Assert.Equal(10.0, chip.Padding.Left, 3);
            Assert.Equal(3.0, chip.Padding.Top, 3);
            Assert.Equal(4.0, chip.Padding.Bottom, 3);
            Assert.Equal(12.0, Assert.IsType<TextBlock>(chip.Child).FontSize, 3);
        });
    }

    /// <summary>【Windows 源】<c>LaunchPage.cs:184-191</c>:三个玻璃块(配置钮 / 联机钮 / 新闻卡)都在 <c>_glass</c> 里
    /// —— 用"切到有图 tone 后它们都变半透明白、切回来又回主题色"来证明。</summary>
    [Fact]
    public void Glass_buttons_follow_the_hero_tone()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            using var ui = UiHost.Show(page);
            C3cFiles.Settle();

            Border configPill = Assert.IsType<Border>(page.ConfigPillNow);
            Border newsCard = Assert.IsType<Border>(page.NewsCardNow);
            Assert.Contains(configPill, page.GlassNow);
            Assert.Contains(newsCard, page.GlassNow);
            Assert.Equal(3, page.GlassNow.Count); // 配置钮 / 联机钮 / 新闻卡

            // 无图 tone:主题纸色(动态资源,挂进窗口后才解析得出)
            AssertBrush(Palette.Brush("B.Paper"), configPill.Background);
            AssertBrush(Palette.Brush("B.Paper"), newsCard.Background);

            // 有图 tone:半透明白(alpha 36/56 那一对)
            page.ApplyHeroTone(true);
            AssertWhiteish(configPill.Background);
            AssertWhiteish(newsCard.Background);
            AssertWhiteish(page.GlassNow[1].Background);

            page.ApplyHeroTone(false);
            AssertBrush(Palette.Brush("B.Paper"), newsCard.Background);
        });
    }

    /// <summary>【Windows 源】旧卡片画布(DashHost)字段保留但**不挂可视树**(WP-U3 后的现状)。</summary>
    [Fact]
    public void Dashboard_host_is_kept_but_not_in_the_tree()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            var page = new LaunchPage();
            Assert.NotNull(page.DashNow);
            Assert.Null(page.DashNow.Parent);
            Assert.False(C3cFiles.Descendants(page).Contains(page.DashNow));
        });
    }

    /// <summary>【Windows 源】<c>LaunchPage.cs:1922</c> 的 <c>#if IMMERSIVE</c>:默认构建不含沉浸代码。</summary>
    [Fact]
    public void Immersive_members_are_not_present()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            Assert.Null(typeof(LaunchPage).GetMethod("SyncImmersive", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public));
            Assert.Null(typeof(LaunchPage).GetMethod("GlassTopBar", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public));
            // OnHidden 在 Windows 里只存在于 IMMERSIVE 那段;mac 侧不重写它(基类的空实现)
            Assert.Equal(typeof(PageBase), typeof(LaunchPage).GetMethod("OnHidden")!.DeclaringType);
        });
    }

    /// <summary>【Windows 源】内嵌 hero.jpg 的解码入口真的能出图(资源缺失时这条必红)。</summary>
    [Fact]
    public void Embedded_hero_image_decodes()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            LaunchPage.ResetHeroCache();
            Avalonia.Media.Imaging.Bitmap? hero = LaunchPage.HeroSourceForTests();
            Assert.NotNull(hero);
            Assert.True(hero!.PixelSize.Width > 0);
            LaunchPage.ResetHeroCache();
        });
    }

    /// <summary>【Windows 源】<c>pack://...</c> 在 mac 侧是 <c>avares://TipCL/PremiumAssets/hero.jpg</c>。</summary>
    [Fact]
    public void Hero_uri_matches_the_mac_embedded_resource()
    {
        AvaloniaSession.Run(() =>
        {
            using var scope = C3cScope.Capture();
            Assert.Equal(C3cExpected.HeroUri, LaunchPage.HeroUri);
            Assert.StartsWith("avares://TipCL/", LaunchPage.HeroUri, StringComparison.Ordinal);
        });
    }

    private static void AssertBrush(IBrush expected, IBrush? actual)
    {
        SolidColorBrush? want = expected as SolidColorBrush;
        SolidColorBrush? got = actual as SolidColorBrush;
        Assert.True(want != null && got != null, "两边都应当是 SolidColorBrush");
        Assert.Equal(want!.Color, got!.Color);
    }

    private static void AssertWhiteish(IBrush? brush)
    {
        SolidColorBrush? got = brush as SolidColorBrush;
        Assert.True(got != null, "有图 tone 下玻璃块应当是半透明白");
        Assert.Equal(255, got!.Color.R);
        Assert.Equal(255, got.Color.G);
        Assert.Equal(255, got.Color.B);
        Assert.True(got.Opacity < 1.0 || got.Color.A < 255, "半透明:alpha 应当小于 255");
    }
}
