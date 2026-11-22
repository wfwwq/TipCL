// AI4-launch:启动页 v3 组件区模型(LaunchZone / ZoneEditor)与数据源(LaunchWidgetData / ServerStatusHub)。
// 钉住效果图 launch_v3 的硬规则:4 列网格 74/96/12、三档尺寸、按顺序打包不叠放不留洞、窄窗 4×2→2×2、
// 放不下的收起、拖动松手按顺序重排、撤销重做、旧布局文件(没有 zone / zone 坏了)读得懂。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using TipCL.Core;
using TipCL.Core.Launcher.Layout;
using Xunit;

public class LaunchZoneTests
{
    private static ZoneItem It(string id, string type, string size) => new() { Id = id, Type = type, Size = size };

    [Fact]
    public void GridMetricsMatchMockup()
    {
        Assert.Equal(74, LaunchZone.ColW);
        Assert.Equal(96, LaunchZone.RowH);
        Assert.Equal(12, LaunchZone.Gap);
        Assert.Equal(332, LaunchZone.Width(4));   // .zone width:332px
        Assert.Equal(160, LaunchZone.Width(2));
        Assert.Equal(420, LaunchZone.Height);     // .zone height:420px
        Assert.Equal(4, LaunchZone.ColsFor(1000));
        Assert.Equal(2, LaunchZone.ColsFor(780));
        Assert.Equal("2×1", LaunchZone.DimLabel("s"));
        Assert.Equal("2×2", LaunchZone.DimLabel("m"));
        Assert.Equal("4×2", LaunchZone.DimLabel("l"));
    }

    [Fact]
    public void DefaultsArePlaytimeLargeServerSmallShortcutsSmall()
    {
        var d = LaunchZone.Defaults();
        Assert.Equal(new[] { "playtime:l", "server:s", "shortcuts:s" }, d.Select(i => i.Type + ":" + i.Size));
        var p = LaunchZone.Pack(d, 4);
        Assert.Empty(p.Overflow);
        Assert.Equal((0, 0, 4, 2), (p.Placed["playtime"].X, p.Placed["playtime"].Y, p.Placed["playtime"].W, p.Placed["playtime"].H));
        Assert.Equal((0, 2), (p.Placed["server"].X, p.Placed["server"].Y));
        Assert.Equal((2, 2), (p.Placed["shortcuts"].X, p.Placed["shortcuts"].Y));
    }

    [Fact]
    public void NarrowSwapsLargeToMediumAndStacks()
    {
        var p = LaunchZone.Pack(LaunchZone.Defaults(), 2);
        Assert.Empty(p.Overflow);
        Assert.Equal("m", p.Placed["playtime"].EffSize);
        Assert.Equal((0, 0, 2, 2), (p.Placed["playtime"].X, p.Placed["playtime"].Y, p.Placed["playtime"].W, p.Placed["playtime"].H));
        Assert.Equal((0, 2), (p.Placed["server"].X, p.Placed["server"].Y));
        Assert.Equal((0, 3), (p.Placed["shortcuts"].X, p.Placed["shortcuts"].Y));
    }

    [Fact]
    public void ShortWindowDropsRowsAndOverflows()
    {
        Assert.Equal(4, LaunchZone.RowsFor(420));
        Assert.Equal(4, LaunchZone.RowsFor(2000));
        Assert.Equal(2, LaunchZone.RowsFor(300));
        Assert.Equal(1, LaunchZone.RowsFor(10));
        var p = LaunchZone.Pack(LaunchZone.Defaults(), 4, 2);
        Assert.Equal(new[] { "server", "shortcuts" }, p.Overflow);
    }

    [Fact]
    public void PackNeverOverlapsAndOverflowsWhatDoesNotFit()
    {
        var items = new List<ZoneItem>
        {
            It("a", "playtime", "l"), It("b", "screenshots", "l"), It("c", "clock", "s"),
        };
        var p = LaunchZone.Pack(items, 4);
        Assert.Equal(new[] { "c" }, p.Overflow);
        var cells = new HashSet<(int, int)>();
        foreach (var r in p.Placed.Values)
            for (int y = r.Y; y < r.Y + r.H; y++)
                for (int x = r.X; x < r.X + r.W; x++)
                    Assert.True(cells.Add((x, y)), "overlap at " + x + "," + y);
        // 窄窗口:两个 2×2 + 一个 2×1 只放得下前两个
        var n = LaunchZone.Pack(items, 2);
        Assert.Equal(new[] { "c" }, n.Overflow);
    }

    [Fact]
    public void PackFillsHolesInOrder()
    {
        // s s 先占第一行,后面的 l 只能放第 2~3 行,再一个 s 回填第 4 行
        var items = new List<ZoneItem> { It("a", "server", "s"), It("b", "clock", "s"), It("c", "playtime", "l"), It("d", "note", "s") };
        var p = LaunchZone.Pack(items, 4);
        Assert.Equal((0, 0), (p.Placed["a"].X, p.Placed["a"].Y));
        Assert.Equal((2, 0), (p.Placed["b"].X, p.Placed["b"].Y));
        Assert.Equal((0, 1), (p.Placed["c"].X, p.Placed["c"].Y));
        Assert.Equal((0, 3), (p.Placed["d"].X, p.Placed["d"].Y));
    }

    [Fact]
    public void DragOrderMovesItemToNearestSlot()
    {
        var items = LaunchZone.Defaults();
        // 把「快捷入口」拖到左上角 → 排到第一个
        var r = LaunchZone.DragOrder(items, "shortcuts", 40, 40, 4);
        Assert.NotNull(r);
        Assert.Equal("shortcuts", r!.Value.Order[0].Id);
        Assert.Equal((0, 0), (r.Value.Spot.X, r.Value.Spot.Y));
        // 拖到下方 → 排到别的组件后面(落在第 2 行起)
        var r2 = LaunchZone.DragOrder(items, "playtime", 300, 380, 4);
        Assert.NotNull(r2);
        Assert.NotEqual("playtime", r2!.Value.Order[0].Id);
        Assert.Equal(1, r2.Value.Spot.Y);
        Assert.Null(LaunchZone.DragOrder(items, "nope", 0, 0, 4));
    }

    [Fact]
    public void EditorUndoRedoResetRevert()
    {
        var ed = new ZoneEditor(LaunchZone.Defaults());
        Assert.False(ed.CanUndo);
        Assert.True(ed.Remove("server"));
        Assert.Equal(2, ed.Items.Count);
        Assert.True(ed.Dirty);
        Assert.True(ed.Undo());
        Assert.Equal(3, ed.Items.Count);
        Assert.True(ed.Redo());
        Assert.Equal(2, ed.Items.Count);
        Assert.True(ed.Reset());
        Assert.Equal(3, ed.Items.Count);
        Assert.False(ed.Reset()); // 已经是默认,不记一步
        Assert.True(ed.Undo());
        Assert.Equal(2, ed.Items.Count);
        ed.Commit();
        Assert.False(ed.CanUndo);
        Assert.False(ed.Dirty);
        ed.Remove("playtime");
        Assert.True(ed.Revert());
        Assert.Equal(2, ed.Items.Count);
        // 撤销栈封顶 50
        var ed2 = new ZoneEditor(LaunchZone.Defaults());
        for (int i = 0; i < 80; i++)
            ed2.Replace(i % 2 == 0 ? new List<ZoneItem>() : LaunchZone.Defaults());
        int n = 0;
        while (ed2.Undo())
            n++;
        Assert.Equal(ZoneEditor.MaxHistory, n);
    }

    [Fact]
    public void EditorCycleSizeRespectsAllowedSizesAndRoom()
    {
        var ed = new ZoneEditor(LaunchZone.Defaults());
        Assert.Equal(ZoneEditor.SizeResult.Changed, ed.CycleSize("server", out string next));
        Assert.Equal("m", next);
        Assert.Equal(ZoneEditor.SizeResult.Changed, ed.CycleSize("server", out next));
        Assert.Equal("s", next);
        // 窄窗口:playtime 的 l/m 排版相同 → 只在 m ↔ s 间换
        ed.Cols = 2;
        Assert.Equal(ZoneEditor.SizeResult.Changed, ed.CycleSize("playtime", out next));
        Assert.Equal("s", next);
        // 放满后加大会被拒绝
        var full = new ZoneEditor(new[] { It("a", "playtime", "l"), It("b", "screenshots", "m"), It("c", "clock", "s"), It("d", "note", "s") });
        Assert.Equal(ZoneEditor.SizeResult.NoRoom, full.CycleSize("c", out _));
        Assert.Equal(ZoneEditor.SizeResult.Missing, full.CycleSize("zz", out _));
    }

    [Fact]
    public void EditorAddHonoursMultiAndFull()
    {
        var ed = new ZoneEditor(LaunchZone.Defaults());
        Assert.Equal(ZoneEditor.AddResult.AlreadyAdded, ed.Add("playtime", out _));
        Assert.Equal(ZoneEditor.AddResult.Added, ed.Add("note", out var n1, "a"));
        Assert.Equal(ZoneEditor.AddResult.Added, ed.Add("note", out var n2, "b"));
        Assert.NotEqual(n1!.Id, n2!.Id);
        Assert.Equal("s", n1.Size);
        Assert.Equal(ZoneEditor.AddResult.Full, ed.Add("clock", out _));
        Assert.Equal(ZoneEditor.AddResult.Unknown, ed.Add("banner", out _));
    }

    [Fact]
    public void ReadWriteRoundTripsThroughLayoutFile()
    {
        var doc = LayoutV2.CreateDefault();
        var page = new PageLayoutV2();
        var items = LaunchZone.Defaults();
        items.Add(new ZoneItem { Id = "note", Type = "note", Size = "m", Settings = { ["text"] = "周六联机" } });
        LaunchZone.Write(page, items);
        doc.Layout.Pages["launch"] = page;
        string json = doc.ToJson();
        var back = LayoutV2.FromJsonText(json)!;
        var read = LaunchZone.Read(back.Layout.Pages["launch"]);
        Assert.True(LaunchZone.Same(items, read));
        Assert.Equal("周六联机", read[3].Text("text"));
    }

    [Fact]
    public void ReadOldLayoutWithoutZoneFallsBackAndMigratesNotes()
    {
        // 旧版启动页布局:只有区块(新闻 / 便签显示中),没有 zone
        string json = "{\"version\":2,\"kind\":\"tipcl-layout\",\"layout\":{\"pages\":{\"launch\":{\"version\":2,\"mode\":\"canvas\",\"blocks\":[{\"id\":\"news\"},{\"id\":\"notes\",\"hidden\":false},{\"id\":\"log\",\"hidden\":true}]}}}}";
        var doc = LayoutV2.FromJsonText(json);
        Assert.NotNull(doc);
        doc!.Layout.Pages.TryGetValue("launch", out var page);
        var read = LaunchZone.Read(page, "旧便签内容");
        Assert.Equal(new[] { "playtime", "server", "shortcuts", "note" }, read.Select(i => i.Type));
        Assert.Equal("旧便签内容", read[3].Text("text"));
        // 完全没有 launch 页
        Assert.Equal(3, LaunchZone.Read(null).Count);
    }

    [Fact]
    public void ReadToleratesGarbageZone()
    {
        var page = new PageLayoutV2();
        page.Extra["zone"] = "oops";
        Assert.Equal(3, LaunchZone.Read(page).Count);
        var bad = new Dictionary<string, object?>
        {
            ["items"] = new List<object?>
            {
                123,
                new Dictionary<string, object?> { ["id"] = "x", ["type"] = "banner", ["size"] = "s" },
                new Dictionary<string, object?> { ["id"] = "c", ["type"] = "clock", ["size"] = "l", ["future"] = 1L },
                new Dictionary<string, object?> { ["id"] = "c", ["type"] = "note", ["size"] = "s" },
                new Dictionary<string, object?> { ["id"] = "c2", ["type"] = "clock", ["size"] = "s" },
            },
        };
        page.Extra["zone"] = bad;
        var read = LaunchZone.Read(page);
        Assert.Single(read);
        Assert.Equal("s", read[0].Size); // clock 不允许 l → 回落默认尺寸
        Assert.Equal(1L, read[0].Extra["future"]); // 未知字段保留
        // 用户把组件全删光:空数组要尊重
        page.Extra["zone"] = new Dictionary<string, object?> { ["items"] = new List<object?>() };
        Assert.Empty(LaunchZone.Read(page));
    }

    [Fact]
    public void WritePreservesOtherPageFields()
    {
        var page = new PageLayoutV2 { Mode = "grid", Cols = 6 };
        page.Blocks.Add(new BlockLayout { Id = "news" });
        page.Extra["other"] = "keep";
        LaunchZone.Write(page, LaunchZone.Defaults());
        Assert.Equal("grid", page.Mode);
        Assert.Single(page.Blocks);
        Assert.Equal("keep", page.Extra["other"]);
        Assert.True(LaunchZone.HasZone(page));
    }
}

public class LaunchWidgetDataTests
{
    private static PyDict Sessions(params (DateTime StartUtc, long Dur)[] s)
    {
        var list = new List<object>();
        foreach (var (st, d) in s)
            list.Add(PyDict.Of("start", (long)(st - DateTime.UnixEpoch).TotalSeconds, "duration", d, "version", "1.20.1"));
        return PyDict.Of("total", 0L, "versions", new PyDict(), "sessions", list);
    }

    [Fact]
    public void WeekSplitsSessionsPerLocalDay()
    {
        var tz = TimeZoneInfo.Utc;
        var now = new DateTime(2026, 11, 20, 18, 0, 0); // 周五
        var all = PyDict.Of(
            "Fabric 1.20.1", Sessions((new DateTime(2026, 11, 16, 20, 0, 0), 3600), (new DateTime(2026, 11, 19, 23, 0, 0), 7200)),
            "原版", Sessions((new DateTime(2026, 11, 10, 10, 0, 0), 5400), (new DateTime(2026, 11, 20, 9, 0, 0), 1800)));
        var w = LaunchWidgetData.Week(all, now, tz);
        Assert.Equal(4, w.Today);
        Assert.Equal(1.0, w.Hours[0], 3);   // 周一 1h
        Assert.Equal(1.0, w.Hours[3], 3);   // 周四 23:00 起 2h → 周四 1h
        Assert.Equal(1.5, w.Hours[4], 3);   //               + 周五 1h + 原版 0.5h
        Assert.Equal(3.5, w.TotalHours, 3);
        Assert.Equal(1.5, w.LastWeekHours, 3);
        Assert.Equal("Fabric 1.20.1", w.TopInstance);
        Assert.True(w.AnyRecord);
    }

    [Fact]
    public void WeekToleratesGarbage()
    {
        var w = LaunchWidgetData.Week("nope", DateTime.Now);
        Assert.False(w.AnyRecord);
        var bad = PyDict.Of("x", PyDict.Of("sessions", new List<object> { "s", PyDict.Of("start", "a", "duration", 5L), PyDict.Of("start", 10L, "duration", -1L) }));
        Assert.Equal(0, LaunchWidgetData.Week(bad, DateTime.Now).TotalHours);
        Assert.Equal("6.2", LaunchWidgetData.Hours1(6.2));
        Assert.Equal("6", LaunchWidgetData.Hours1(6.01));
    }

    [Fact]
    public void FriendlyVersionNames()
    {
        Assert.Equal("Fabric 1.20.1", LaunchWidgetData.FriendlyVersion("fabric-loader-0.15.11-1.20.1"));
        Assert.Equal("Fabric 1.21.4", LaunchWidgetData.FriendlyVersion("fabric-loader-0.16.9-1.21.4", "1.21.4"));
        Assert.Equal("Minecraft 1.20.1", LaunchWidgetData.FriendlyVersion("1.20.1"));
        Assert.Equal("Minecraft 26.4-snapshot-7", LaunchWidgetData.FriendlyVersion("26.4-snapshot-7"));
        Assert.Equal("Forge 1.20.1", LaunchWidgetData.FriendlyVersion("1.20.1-forge-47.2.0"));
        Assert.Equal("NeoForge 1.21.1", LaunchWidgetData.FriendlyVersion("neoforge-21.1.77"));
        Assert.Equal("NeoForge 1.21", LaunchWidgetData.FriendlyVersion("neoforge-21.0.10"));
        Assert.Equal("OptiFine 1.12.2", LaunchWidgetData.FriendlyVersion("1.12.2-OptiFine_HD_U_G5"));
        Assert.Equal("我的生存包", LaunchWidgetData.FriendlyVersion("我的生存包", "1.20.1"));
        Assert.Equal("我的生存包", LaunchWidgetData.FriendlyVersion("我的生存包"));
        Assert.Equal("", LaunchWidgetData.FriendlyVersion(""));
    }

    [Fact]
    public void ServerStatusHubCanBeFaked()
    {
        var old = ServerStatusHub.Source;
        try
        {
            var fake = new FixedServerStatusSource(new ServerStatus { Name = "生存服", Host = "192.168.1.20", Port = 25565, Ok = true, Online = 12, Max = 20, LatencyMs = 38 });
            ServerStatusHub.Source = fake;
            var s = ServerStatusHub.Source.FetchAsync("x", CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal("192.168.1.20", s.Address);
            Assert.Equal(12, s.Online);
            Assert.Equal(1, fake.Calls);
            Assert.Equal("h:25566", new ServerStatus { Host = "h", Port = 25566 }.Address);
        }
        finally
        {
            ServerStatusHub.Source = old;
        }
    }
}
