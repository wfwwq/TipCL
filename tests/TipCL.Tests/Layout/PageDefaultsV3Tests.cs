// R35 lin-l5:布局 v3 WP-L5(GOAL-layout-v3 §5.5 / D-81 Q4、Q5)的 Linux 落地用例。
//   1 PageDefaultsV3ModelTests —— 4 页新默认布局的纯数据形状(12 列网格、可见块坐标、默认隐藏块只给列跨度)
//                                 + 隐藏块显示出来之后在网格里的落点(两两并排、日志整行,不压块)
//                                 + 「≥2 块才给自定义布局」的纯规则;
//   2 PageDefaultsV3HostTests  —— 无头真宿主:4 页在布局文档里**没有**条目时渲染新默认、**有**条目时完全照文档;
//                                 单块页(settings:network / feedback)右键没有「自定义布局...」;
//                                 启动页编辑态英雄区压暗 + 「主区域固定」标签;改名后的块标题。
// 夹具:LayoutStoreDir(LayoutMoreTests.cs)把布局存储指到临时目录,不碰真实数据根;
// 碰 LayoutStoreV2 静态状态的用例同属「TipCL 真机交互回归」集合(不并行)。
// 无头派发次数刻意压到最少(见 LayoutMoreTests 第 7 节的说明:派发过多会让共享会话不稳)。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TipCL.App.Pages;
using TipCL.App.Pages.Feedback;
using TipCL.App.Pages.Launch;
using TipCL.App.Pages.Layout;
using TipCL.App.Pages.Settings;
using TipCL.Core.Launcher.Layout;
using TipCL.Tests.App;
using Xunit;

namespace TipCL.Tests.Layout;

// ————————————————————————— 1 纯数据 / 纯规则 —————————————————————————

public class PageDefaultsV3ModelTests
{
    private static BlockLayout B(PageLayoutV2 page, string id) => page.Blocks.Single(b => b.Id == id);

    private static void AssertAt(PageLayoutV2 page, string id, int x, int y, int span)
    {
        BlockLayout b = B(page, id);
        Assert.False(b.Hidden, id + " 应默认可见");
        Assert.Equal((double)x, b.X);
        Assert.Equal((double)y, b.Y);
        Assert.Equal((double)span, b.W);
        Assert.Equal(span, b.Span);
    }

    private static void AssertOff(PageLayoutV2 page, string id, int span)
    {
        BlockLayout b = B(page, id);
        Assert.True(b.Hidden, id + " 应默认隐藏");
        Assert.Equal(span, b.Span);
        // 坐标保持契约默认(= 自动流),显示时才找空位
        Assert.True(b.X <= 0.0 && b.Y <= 0.0 && b.W <= 1.0 && b.H <= 1.0);
    }

    [Fact]
    public void AllFourDefaults_AreTwelveColumnGrids()
    {
        foreach (PageLayoutV2 page in new[] { PageDefaultLayouts.Multiplayer(), PageDefaultLayouts.Playtime(), PageDefaultLayouts.Java(), PageDefaultLayouts.LaunchContent() })
        {
            Assert.Equal(LayoutSchema.ModeGrid, page.Mode);
            Assert.Equal(12, page.Cols);
            Assert.Equal(12, PageDefaultLayouts.Cols);
            Assert.Equal(0.0, page.RowH);
            Assert.Equal(LayoutSchema.GapDefault, page.Gap);
            // 块 id 不重复
            Assert.Equal(page.Blocks.Count, page.Blocks.Select(b => b.Id).Distinct().Count());
        }
    }

    [Fact]
    public void Multiplayer_HeaderFull_StateRoomHalves_ActionsFull_PlayersNodes8to4()
    {
        PageLayoutV2 p = PageDefaultLayouts.Multiplayer();
        Assert.Equal(new[] { "header", "state", "room", "actions", "players", "nodes" }, p.Blocks.Select(b => b.Id).ToArray());
        AssertAt(p, "header", 0, 0, 12);
        AssertAt(p, "state", 0, 1, 6);
        AssertAt(p, "room", 6, 1, 6);
        AssertAt(p, "actions", 0, 2, 12);
        AssertAt(p, "players", 0, 3, 8);
        AssertAt(p, "nodes", 8, 3, 4);
        Assert.DoesNotContain(p.Blocks, b => b.Hidden);
        // 页边距沿用文档默认(联机页原来就是 PageBase 的默认边距)
        Assert.Equal(LayoutSchema.PadDefaultCopy(), p.Pad);
    }

    [Fact]
    public void Playtime_TotalRangeHalves_ListFull_HeaderHidden()
    {
        PageLayoutV2 p = PageDefaultLayouts.Playtime();
        AssertAt(p, "total", 0, 0, 6);
        AssertAt(p, "range", 6, 0, 6);
        AssertAt(p, "list", 0, 1, 12);
        AssertOff(p, "header", 12);
        // 设置内层子页原来的 18,6,18,16
        Assert.Equal(new[] { 18.0, 6.0, 18.0, 16.0 }, p.Pad);
        // 默认隐藏块排在最后
        Assert.Equal("header", p.Blocks.Last().Id);
    }

    [Fact]
    public void Java_InstallDefaultsHalves_ListFull_IntroHidden()
    {
        PageLayoutV2 p = PageDefaultLayouts.Java();
        AssertAt(p, "install", 0, 0, 6);
        AssertAt(p, "defaults", 6, 0, 6);
        AssertAt(p, "list", 0, 1, 12);
        AssertOff(p, "intro", 12);
        Assert.Equal(new[] { 18.0, 6.0, 18.0, 16.0 }, p.Pad);
        Assert.Equal("intro", p.Blocks.Last().Id);
    }

    [Fact]
    public void Launch_NewsFull_OthersHidden()
    {
        PageLayoutV2 p = PageDefaultLayouts.LaunchContent();
        Assert.Equal(new[] { "news", "tasks", "playtime", "notes", "log" }, p.Blocks.Select(b => b.Id).ToArray());
        AssertAt(p, "news", 0, 0, 12);
        AssertOff(p, "tasks", 6);
        AssertOff(p, "playtime", 6);
        AssertOff(p, "notes", 6);
        AssertOff(p, "log", 12);
    }

    [Fact]
    public void Launch_ShowingAllHidden_PairsSideBySide_LogFullRow_NoOverlap()
    {
        PageLayoutV2 p = PageDefaultLayouts.LaunchContent();
        foreach (BlockLayout b in p.Blocks) b.Hidden = false;
        List<LayoutCell> cells = LayoutGrid.Pack(p);
        LayoutCell C(string id) => cells.Single(c => c.Id == id);
        Assert.Equal((0, 0, 12), (C("news").X, C("news").Y, C("news").W));
        Assert.Equal((0, 1, 6), (C("tasks").X, C("tasks").Y, C("tasks").W));
        Assert.Equal((6, 1, 6), (C("playtime").X, C("playtime").Y, C("playtime").W));
        Assert.Equal((0, 2, 6), (C("notes").X, C("notes").Y, C("notes").W));
        Assert.Equal((0, 3, 12), (C("log").X, C("log").Y, C("log").W));
        AssertNoOverlap(cells);
    }

    [Fact]
    public void Defaults_PackWithoutOverlap_AndHiddenBlocksStayOffCanvas()
    {
        foreach (PageLayoutV2 page in new[] { PageDefaultLayouts.Multiplayer(), PageDefaultLayouts.Playtime(), PageDefaultLayouts.Java(), PageDefaultLayouts.LaunchContent() })
        {
            List<LayoutCell> cells = LayoutGrid.Pack(page);
            Assert.Equal(page.Blocks.Count(b => !b.Hidden), cells.Count);
            AssertNoOverlap(cells);
            // 可见块就在默认写的位置上(Pack 不挪)
            foreach (LayoutCell cell in cells)
            {
                BlockLayout b = page.Blocks.Single(x => x.Id == cell.Id);
                Assert.Equal((int)b.X, cell.X);
                Assert.Equal((int)b.Y, cell.Y);
                Assert.Equal((int)b.W, cell.W);
            }
        }
    }

    [Fact]
    public void Playtime_And_Java_ShowingHiddenHeader_LandsBelowWithoutOverlap()
    {
        foreach (PageLayoutV2 page in new[] { PageDefaultLayouts.Playtime(), PageDefaultLayouts.Java() })
        {
            foreach (BlockLayout b in page.Blocks) b.Hidden = false;
            List<LayoutCell> cells = LayoutGrid.Pack(page);
            Assert.Equal(page.Blocks.Count, cells.Count);
            AssertNoOverlap(cells);
            LayoutCell shown = cells.Single(c => c.Id == page.Blocks.Last().Id);
            Assert.Equal(12, shown.W);
            Assert.Equal(2, shown.Y);
        }
    }

    [Fact]
    public void ForPage_MapsExactlyTheFourPages_AndReturnsFreshInstances()
    {
        Assert.NotNull(PageDefaultLayouts.ForPage("multiplayer"));
        Assert.NotNull(PageDefaultLayouts.ForPage("playtime"));
        Assert.NotNull(PageDefaultLayouts.ForPage("java"));
        Assert.NotNull(PageDefaultLayouts.ForPage("launch"));
        foreach (string other in new[] { "library:main", "account", "servers", "download", "settings:general", "", "bogus" })
        {
            Assert.Null(PageDefaultLayouts.ForPage(other));
        }
        Assert.Null(PageDefaultLayouts.ForPage(null));
        PageLayoutV2 a = PageDefaultLayouts.Java();
        PageLayoutV2 b = PageDefaultLayouts.Java();
        Assert.NotSame(a, b);
        a.Blocks[0].X = 5;
        Assert.Equal(0.0, b.Blocks[0].X);
    }

    [Fact]
    public void LoadState_WithoutDocEntry_UsesDefault_WithDocEntry_UsesDoc()
    {
        // 与 LayoutEditor.LoadFromStore 同一条规则:effective = docPage ?? defaultPage
        var refs = new List<LayoutBlockRef>
        {
            new("header", false), new("total", true), new("range", true), new("list", true),
        };
        LayoutPageState fresh = LayoutEngine.LoadState("playtime", PageDefaultLayouts.Playtime(), refs, false);
        Assert.Equal(LayoutSchema.ModeGrid, fresh.Page.Mode);
        Assert.Contains("header", fresh.Hidden);
        Assert.Equal(new[] { "total", "range", "list" }, fresh.VisibleIds().ToArray());
        Assert.False(fresh.ExistedInDoc);

        var doc = new PageLayoutV2 { Mode = LayoutSchema.ModeStack };
        foreach (string id in new[] { "list", "header", "range", "total" }) doc.Blocks.Add(new BlockLayout { Id = id });
        LayoutPageState saved = LayoutEngine.LoadState("playtime", doc, refs, true);
        Assert.Equal(LayoutSchema.ModeStack, saved.Page.Mode);
        Assert.Empty(saved.Hidden);
        Assert.Equal(new[] { "list", "header", "range", "total" }, saved.Order.ToArray());
        Assert.True(saved.ExistedInDoc);
    }

    [Fact]
    public void LayoutMenuRule_NeedsAtLeastTwoBlocks_AndRespectsSingleBlockCatalog()
    {
        Assert.Equal(2, BlockHostRules.MinBlocksForLayoutMenu);
        Assert.False(BlockHostRules.OffersLayoutMenu(0));
        Assert.False(BlockHostRules.OffersLayoutMenu(1));
        Assert.True(BlockHostRules.OffersLayoutMenu(2));
        Assert.True(BlockHostRules.OffersLayoutMenu(5));
        // 入口表里标了单块的页:块数再多也不给
        Assert.False(BlockHostRules.OffersLayoutMenu("settings:network", 1));
        Assert.False(BlockHostRules.OffersLayoutMenu("settings:network", 3));
        Assert.False(BlockHostRules.OffersLayoutMenu("feedback", 2));
        // 普通页按块数
        Assert.True(BlockHostRules.OffersLayoutMenu("launch", 5));
        Assert.True(BlockHostRules.OffersLayoutMenu("multiplayer", 6));
        Assert.False(BlockHostRules.OffersLayoutMenu("somepage", 1));
        Assert.True(BlockHostRules.OffersLayoutMenu(null, 2));
    }

    internal static void AssertNoOverlap(IReadOnlyList<LayoutCell> cells)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            for (int j = i + 1; j < cells.Count; j++)
            {
                LayoutCell a = cells[i];
                LayoutCell b = cells[j];
                bool overlap = a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;
                Assert.False(overlap, a.Id + " 与 " + b.Id + " 重叠");
            }
        }
    }
}

// ————————————————————————— 2 无头真宿主 —————————————————————————

[Collection("TipCL 真机交互回归")]
public class PageDefaultsV3HostTests
{
    private static List<BlockSpec> Specs(BlockHost host)
    {
        var specs = (System.Collections.IEnumerable)typeof(LayoutEditor)
            .GetField("_specs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host.Editor)!;
        return specs.Cast<BlockSpec>().ToList();
    }

    private static string TitleOf(BlockHost host, string id) => Specs(host).Single(s => s.Id == id).Title;

    private static BlockHost FindHost(Control root, string pageId)
    {
        if (root is BlockHost self && self.PageId == pageId) return self;
        BlockHost? host = root.GetLogicalDescendants().OfType<BlockHost>().FirstOrDefault(h => h.PageId == pageId)
            ?? root.GetVisualDescendants().OfType<BlockHost>().FirstOrDefault(h => h.PageId == pageId);
        Assert.NotNull(host);
        return host!;
    }

    // 4 页的宿主(每次都新建页面 ⇒ 每次都重新从布局文档装载)
    private static Dictionary<string, BlockHost> BuildFour()
    {
        var map = new Dictionary<string, BlockHost>(StringComparer.Ordinal);
        map["multiplayer"] = FindHost(new MultiplayerPage(), "multiplayer");
        map["playtime"] = FindHost(new PlaytimeView(), "playtime");
        map["java"] = FindHost(new JavaView(), "java");
        var launch = new LaunchPage();
        Assert.NotNull(launch.Blocks);
        map["launch"] = launch.Blocks!;
        return map;
    }

    private static void AssertGridAt(LayoutPageState s, string id, int x, int y, int w)
    {
        BlockLayout b = s.Page.Blocks.Single(z => z.Id == id);
        Assert.Equal((double)x, b.X);
        Assert.Equal((double)y, b.Y);
        Assert.Equal((double)w, b.W);
        Assert.DoesNotContain(id, s.Hidden);
    }

    [Fact]
    public void FourPages_NoDocEntry_RenderNewDefaults_DocEntry_FollowsDocExactly()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("v3defaults");
        TestApp.Run(() =>
        {
            // —— 文档里没有这 4 页 ⇒ 新默认 ——
            Dictionary<string, BlockHost> fresh = BuildFour();
            foreach (KeyValuePair<string, BlockHost> pair in fresh)
            {
                LayoutPageState st = pair.Value.Editor.State;
                Assert.False(st.ExistedInDoc, pair.Key);
                Assert.Equal(LayoutSchema.ModeGrid, st.Page.Mode);
                Assert.Equal(12, st.Page.Cols);
            }
            LayoutPageState mp = fresh["multiplayer"].Editor.State;
            AssertGridAt(mp, "header", 0, 0, 12);
            AssertGridAt(mp, "state", 0, 1, 6);
            AssertGridAt(mp, "room", 6, 1, 6);
            AssertGridAt(mp, "actions", 0, 2, 12);
            AssertGridAt(mp, "players", 0, 3, 8);
            AssertGridAt(mp, "nodes", 8, 3, 4);
            Assert.Empty(mp.Hidden);

            LayoutPageState pt = fresh["playtime"].Editor.State;
            AssertGridAt(pt, "total", 0, 0, 6);
            AssertGridAt(pt, "range", 6, 0, 6);
            AssertGridAt(pt, "list", 0, 1, 12);
            Assert.Equal(new[] { "header" }, pt.Hidden.ToArray());

            LayoutPageState jv = fresh["java"].Editor.State;
            AssertGridAt(jv, "install", 0, 0, 6);
            AssertGridAt(jv, "defaults", 6, 0, 6);
            AssertGridAt(jv, "list", 0, 1, 12);
            Assert.Equal(new[] { "intro" }, jv.Hidden.ToArray());

            LayoutPageState la = fresh["launch"].Editor.State;
            AssertGridAt(la, "news", 0, 0, 12);
            Assert.Equal(new[] { "news" }, la.VisibleIds().ToArray());
            Assert.Equal(new[] { "log", "notes", "playtime", "tasks" }, la.Hidden.OrderBy(x => x, StringComparer.Ordinal).ToArray());

            // 只是读到空气时的默认值:没有写进任何文件
            LayoutV2 before = LayoutStoreV2.Load();
            foreach (string id in fresh.Keys)
            {
                Assert.False(before.Layout.Pages.ContainsKey(id), id + " 不该被默默写进文档");
            }

            // —— 文档里有这 4 页(老用户存过的纵向流、自己的顺序与显隐)⇒ 完全照文档 ——
            var docOrders = new Dictionary<string, string[]>
            {
                ["multiplayer"] = new[] { "nodes", "players", "actions", "room", "state", "header" },
                ["playtime"] = new[] { "header", "list", "range", "total" },
                ["java"] = new[] { "intro", "list", "defaults", "install" },
                ["launch"] = new[] { "log", "news", "tasks", "playtime", "notes" },
            };
            var docHidden = new Dictionary<string, string>
            {
                ["multiplayer"] = "room",
                ["playtime"] = "total",
                ["java"] = "defaults",
                ["launch"] = "news",
            };
            LayoutV2 doc = LayoutStoreV2.Load();
            foreach (KeyValuePair<string, string[]> pair in docOrders)
            {
                var page = new PageLayoutV2 { Mode = LayoutSchema.ModeStack };
                foreach (string id in pair.Value)
                {
                    page.Blocks.Add(new BlockLayout { Id = id, Hidden = id == docHidden[pair.Key] });
                }
                doc.Layout.Pages[pair.Key] = page;
            }
            Assert.True(LayoutStoreV2.Save(doc));
            LayoutStoreV2.ResetCache();

            Dictionary<string, BlockHost> saved = BuildFour();
            foreach (KeyValuePair<string, BlockHost> pair in saved)
            {
                LayoutPageState st = pair.Value.Editor.State;
                Assert.True(st.ExistedInDoc, pair.Key);
                Assert.Equal(LayoutSchema.ModeStack, st.Page.Mode);
                Assert.Equal(docOrders[pair.Key], st.Order.ToArray());
                Assert.Equal(new[] { docHidden[pair.Key] }, st.Hidden.ToArray());
            }
            // 默认里隐藏的「概览 / 说明」在文档里是可见的 ⇒ 照文档显示
            Assert.Contains("header", saved["playtime"].Editor.State.VisibleIds());
            Assert.Contains("intro", saved["java"].Editor.State.VisibleIds());
            // 启动页文档里把日志排第一、新闻藏起来 ⇒ 照文档
            Assert.Equal("log", saved["launch"].Editor.State.VisibleIds()[0]);
        });
    }

    [Fact]
    public void SingleBlockPages_HaveNoLayoutMenu_MultiBlockPagesDo_RenamedTitles_LaunchHeroEditLook()
    {
        using LayoutStoreDir dir = new LayoutStoreDir("v3single");
        TestApp.Run(() =>
        {
            // —— Q5:单块页 ——
            var network = new BlockHost("settings:network", new List<BlockSpec>
            {
                new() { Id = "network", Title = "下载与网络", Content = new Border() },
            });
            Assert.Equal(1, network.SpecCount);
            Assert.False(network.OffersLayoutMenu);
            Assert.False(network.Editor.CanEdit);
            Assert.False(network.EnterEdit());
            Assert.False(network.IsEditing);

            // 随便一个只有一块的页(不在入口表里)也一样
            var lone = new BlockHost("r35:lone", new List<BlockSpec> { new() { Id = "only", Title = "唯一", Content = new Border() } });
            Assert.False(lone.OffersLayoutMenu);
            Assert.False(lone.EnterEdit());

            // 「反馈与帮助」(Linux 的 settings:about 对应页,入口表标了单块)
            BlockHost feedback = FindHost(new FeedbackView(), "feedback");
            Assert.False(feedback.OffersLayoutMenu);
            Assert.False(feedback.Editor.CanEdit);
            Assert.False(feedback.EnterEdit());

            // 两块的页才有
            var two = new BlockHost("r35:two", new List<BlockSpec>
            {
                new() { Id = "a", Title = "甲", Content = new Border() },
                new() { Id = "b", Title = "乙", Content = new Border() },
            });
            Assert.True(two.OffersLayoutMenu);
            Assert.True(two.Editor.CanEdit);

            // 全局右键钩子只在「可编辑」的宿主里挑目标 ⇒ 单块页上右键不会出现「自定义布局...」
            var panel = new StackPanel();
            panel.Children.Add(network);
            panel.Children.Add(lone);
            Assert.Empty(LayoutShellHooks.FindHosts(panel));
            panel.Children.Add(two);
            Assert.Equal(new[] { "r35:two" }, LayoutShellHooks.FindHosts(panel).Select(h => h.PageId).ToArray());

            // —— §5.5 改名(只改 Title,不改 id) ——
            BlockHost mp = FindHost(new MultiplayerPage(), "multiplayer");
            Assert.True(mp.OffersLayoutMenu);
            Assert.Equal("联机状态", TitleOf(mp, "state"));
            Assert.Equal("房间", TitleOf(mp, "room"));
            Assert.Equal("快捷操作", TitleOf(mp, "actions"));
            Assert.Equal("玩家", TitleOf(mp, "players"));
            BlockHost pt = FindHost(new PlaytimeView(), "playtime");
            Assert.Equal("累计时长", TitleOf(pt, "total"));
            Assert.Equal("时间范围", TitleOf(pt, "range"));
            Assert.Equal("明细", TitleOf(pt, "list"));
            Assert.False(Specs(pt).Single(s => s.Id == "header").DefaultVisible);
            BlockHost jv = FindHost(new JavaView(), "java");
            Assert.Equal("已检测的 Java", TitleOf(jv, "list"));
            Assert.False(Specs(jv).Single(s => s.Id == "intro").DefaultVisible);
            BlockHost sv = FindHost(new ServersView(), "servers");
            Assert.Equal("实例", TitleOf(sv, "instance"));
            BlockHost ac = FindHost(new AccountPage(), "account");
            Assert.Equal("账号列表", TitleOf(ac, "list"));

            // —— 启动页:内容区 5 块(只有新闻可见)仍可编辑;编辑态英雄区 45% + 「主区域固定」 ——
            var launch = new LaunchPage();
            BlockHost blocks = launch.Blocks!;
            Assert.Equal(5, blocks.SpecCount);
            Assert.True(blocks.OffersLayoutMenu);
            Assert.False(launch.HeroLock.IsVisible);
            Assert.Equal(1.0, launch.Hero!.Opacity);
            var seen = new List<bool>();
            blocks.EditingChanged += seen.Add;
            Assert.True(blocks.EnterEdit());
            Assert.Equal(new[] { true }, seen.ToArray());
            Assert.Equal(LaunchPage.HeroEditOpacity, launch.Hero!.Opacity);
            Assert.Equal(0.45, LaunchPage.HeroEditOpacity);
            Assert.False(launch.Hero!.IsHitTestVisible);
            Assert.True(launch.HeroLock.IsVisible);
            Assert.False(launch.HeroLock.IsHitTestVisible);
            Assert.Contains("主区域固定", ((TextBlock)launch.HeroLock.Child!).Text);
            // 编辑条「完成」/ Esc / 全局右键「完成编辑」都直接调编辑器:下一次同步就还原
            blocks.Editor.ExitEdit();
            blocks.SyncEditing();
            Assert.Equal(new[] { true, false }, seen.ToArray());
            Assert.Equal(1.0, launch.Hero!.Opacity);
            Assert.True(launch.Hero!.IsHitTestVisible);
            Assert.False(launch.HeroLock.IsVisible);
            // 幂等
            blocks.SyncEditing();
            Assert.Equal(2, seen.Count);
        });
    }
}
