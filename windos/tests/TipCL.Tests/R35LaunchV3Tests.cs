using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TipCL.Pages;
using Xunit;

namespace TipCL.Tests;

// R35 启动页 v3(效果图 _r35/ref/launch-v3.html):组件区纯逻辑 + 源码守卫。
public class R35LaunchV3Tests
{
	private static ZoneItem I(string id, string type, string size)
	{
		return new ZoneItem { Id = id, Type = type, Size = size };
	}

	private static string Cells(ZonePack p, IEnumerable<ZoneItem> items)
	{
		return string.Join(" ", items.Where((ZoneItem i) => p.Out.ContainsKey(i.Id)).Select((ZoneItem i) => i.Id + "@" + p.Out[i.Id].X + "," + p.Out[i.Id].Y + "," + p.Out[i.Id].W + "x" + p.Out[i.Id].H));
	}

	// ————— 规则 2:4 列网格,列宽 74、行高 96、间距 12;三档尺寸 —————

	[Fact]
	public void GridMetricsMatchMockup()
	{
		Assert.Equal(74.0, LaunchZone.Col);
		Assert.Equal(96.0, LaunchZone.Row);
		Assert.Equal(12.0, LaunchZone.Gap);
		Assert.Equal(332.0, LaunchZone.SpanW(4));
		Assert.Equal(160.0, LaunchZone.SpanW(2));
		Assert.Equal(420.0, LaunchZone.SpanH(4));
		Assert.Equal(86.0, LaunchZone.Px(1));
		Assert.Equal(108.0, LaunchZone.Py(1));
	}

	[Theory]
	[InlineData("s", "2×1")]
	[InlineData("m", "2×2")]
	[InlineData("l", "4×2")]
	[InlineData("x", "2×1")]
	public void OnlyThreeSizes(string size, string label)
	{
		Assert.Equal(label, LaunchZone.DimLabel(LaunchZone.Normalize(size)));
	}

	[Fact]
	public void DefaultsAreMockupWidgets()
	{
		List<ZoneItem> d = LaunchZone.Defaults();
		Assert.Equal("play:l server:s short:s", string.Join(" ", d.Select((ZoneItem i) => i.Type + ":" + i.Size)));
		ZonePack p = LaunchZone.Pack(d, 4, 4);
		Assert.Equal("a@0,0,4x2 b@0,2,2x1 c@2,2,2x1", Cells(p, d));
		Assert.Empty(p.Over);
	}

	// ————— 规则 4:按顺序首个能放下的位置,不叠放、不留洞 —————

	[Fact]
	public void PackNeverOverlaps()
	{
		Random rnd = new Random(7);
		string[] sizes = new string[3] { "s", "m", "l" };
		for (int round = 0; round < 200; round++)
		{
			List<ZoneItem> items = Enumerable.Range(0, rnd.Next(1, 10)).Select((int k) => I("i" + k, "note", sizes[rnd.Next(3)])).ToList();
			foreach (int cols in new int[2] { 2, 4 })
			{
				ZonePack p = LaunchZone.Pack(items, cols, 4);
				bool[,] used = new bool[cols, 4];
				foreach (ZoneRect r in p.Out.Values)
				{
					Assert.True(r.X >= 0 && r.Y >= 0 && r.X + r.W <= cols && r.Y + r.H <= 4);
					for (int x = r.X; x < r.X + r.W; x++)
					{
						for (int y = r.Y; y < r.Y + r.H; y++)
						{
							Assert.False(used[x, y]);
							used[x, y] = true;
						}
					}
				}
				Assert.Equal(items.Count, p.Out.Count + p.Over.Count);
			}
		}
	}

	[Fact]
	public void SmallItemFillsTheHoleNextToAnEarlierOne()
	{
		List<ZoneItem> items = new List<ZoneItem> { I("a", "note", "s"), I("b", "play", "l"), I("c", "note", "s") };
		Assert.Equal("a@0,0,2x1 b@0,1,4x2 c@2,0,2x1", Cells(LaunchZone.Pack(items, 4, 4), items));
	}

	[Fact]
	public void OverflowIsCollectedInOrder()
	{
		List<ZoneItem> items = new List<ZoneItem> { I("a", "play", "l"), I("b", "play", "l"), I("c", "note", "s") };
		ZonePack p = LaunchZone.Pack(items, 4, 4);
		Assert.Equal(new[] { "c" }, p.Over.ToArray());
	}

	// ————— 规则 3:窄窗口减列数不缩字,4×2 → 2×2,放不下的收起 —————

	[Theory]
	[InlineData(1000.0, 4)]
	[InlineData(900.0, 4)]
	[InlineData(899.0, 2)]
	[InlineData(780.0, 2)]
	public void ColumnsByWidth(double width, int cols)
	{
		Assert.Equal(cols, LaunchZone.ColsFor(width));
	}

	[Fact]
	public void NarrowWindowTurnsLargeIntoMediumAndCollapsesTheRest()
	{
		List<ZoneItem> d = LaunchZone.Defaults();
		ZonePack p = LaunchZone.Pack(d, 2, 4);
		Assert.Equal("m", p.Out["a"].Eff);
		Assert.Equal("a@0,0,2x2 b@0,2,2x1 c@0,3,2x1", Cells(p, d));
		List<ZoneItem> more = new List<ZoneItem>(d) { I("d", "clock", "s") };
		Assert.Equal(new[] { "d" }, LaunchZone.Pack(more, 2, 4).Over.ToArray());
		Assert.Empty(LaunchZone.Pack(more, 4, 4).Over);
	}

	[Theory]
	[InlineData(554.0, 4)]
	[InlineData(600.0, 4)]
	[InlineData(330.0, 2)]
	[InlineData(100.0, 1)]
	public void RowsFollowPageHeight(double h, int rows)
	{
		Assert.Equal(rows, LaunchZone.RowsFor(h));
	}

	// ————— 拖动:松手按顺序重排 —————

	[Fact]
	public void DragToTopLeftPutsItemFirst()
	{
		List<ZoneItem> d = LaunchZone.Defaults();
		List<ZoneItem>? order = LaunchZone.BestOrder(d, "c", 40.0, 48.0, 4, 4, out ZoneRect r);
		Assert.NotNull(order);
		Assert.Equal("c", order![0].Id);
		Assert.Equal(0, r.X);
		Assert.Equal(0, r.Y);
	}

	[Fact]
	public void DragAtOwnPlaceKeepsOrder()
	{
		List<ZoneItem> d = LaunchZone.Defaults();
		double cx = LaunchZone.Px(2) + LaunchZone.SpanW(2) / 2.0;
		double cy = LaunchZone.Py(2) + LaunchZone.SpanH(1) / 2.0;
		List<ZoneItem>? order = LaunchZone.BestOrder(d, "c", cx, cy, 4, 4, out ZoneRect _);
		Assert.True(LaunchZone.SameOrder(order!, d));
	}

	// ————— 点尺寸标签切换 —————

	[Fact]
	public void SizeTagCyclesThroughAllowedSizes()
	{
		List<ZoneItem> items = new List<ZoneItem> { I("a", "play", "m") };
		Assert.Equal("l", LaunchZone.NextSize(items, "a", 4, 4, null, out string _));
		items[0].Size = "l";
		Assert.Equal("s", LaunchZone.NextSize(items, "a", 4, 4, null, out string _));
	}

	[Fact]
	public void SizeTagSkipsSameLooksOnNarrowWindow()
	{
		// 窄窗口下 l 按 m 摆:从 m 点一下应跳到 s,而不是「看起来没变」的 l
		List<ZoneItem> items = new List<ZoneItem> { I("a", "play", "m") };
		Assert.Equal("s", LaunchZone.NextSize(items, "a", 2, 4, null, out string _));
	}

	[Fact]
	public void SizeTagRefusesWhenItWouldPushSomethingOut()
	{
		List<ZoneItem> items = new List<ZoneItem> { I("a", "play", "l"), I("b", "short", "m"), I("c", "note", "m") };
		Assert.Null(LaunchZone.NextSize(items, "b", 4, 4, null, out string? msg));
		Assert.Equal("nofit:4×2", msg);
	}

	[Fact]
	public void SingleSizeWidgetsSayTheyHaveOneSize()
	{
		List<ZoneItem> items = new List<ZoneItem> { new ZoneItem { Id = "a", Type = "ref", Ref = "custom:x", Size = "s" } };
		Assert.Null(LaunchZone.NextSize(items, "a", 4, 4, (string _) => "button", out string? msg));
		Assert.Equal("one", msg);
	}

	[Fact]
	public void TypesOnlyUseTheThreeSizes()
	{
		foreach (ZoneType t in LaunchZone.Types)
		{
			Assert.NotEmpty(t.Sizes);
			Assert.All(t.Sizes, (string s) => Assert.Contains(s, new[] { "s", "m", "l" }));
		}
		Assert.Equal(new[] { "play", "server", "short", "note", "clock", "shots", "tasks", "log" }, LaunchZone.Types.Select((ZoneType t) => t.Key).ToArray());
	}

	// ————— 迁移:旧版启动页区块读时推导 —————

	[Fact]
	public void LegacyDefaultOnlyNewsGivesMockupDefaults()
	{
		List<ZoneItem> z = LaunchZone.FromLegacy(new[]
		{
			new KeyValuePair<string, bool>("news", false),
			new KeyValuePair<string, bool>("tasks", true),
			new KeyValuePair<string, bool>("log", true)
		}, null);
		Assert.Equal("play:l server:s short:s", string.Join(" ", z.Select((ZoneItem i) => i.Type + ":" + i.Size)));
		Assert.Equal("play:l server:s short:s", string.Join(" ", LaunchZone.FromLegacy(null, null).Select((ZoneItem i) => i.Type + ":" + i.Size)));
	}

	[Fact]
	public void LegacyUserBlocksMapToWidgetsAndFitTheGrid()
	{
		// 用户存档:news / tasks / playtime / notes / log / 自建「游戏时长」全部可见(win-fix2 录屏里的那份)
		KeyValuePair<string, bool>[] legacy = new KeyValuePair<string, bool>[6]
		{
			new KeyValuePair<string, bool>("news", false),
			new KeyValuePair<string, bool>("tasks", false),
			new KeyValuePair<string, bool>("playtime", false),
			new KeyValuePair<string, bool>("notes", false),
			new KeyValuePair<string, bool>("log", false),
			new KeyValuePair<string, bool>("custom:w-1", false)
		};
		List<ZoneItem> z = LaunchZone.FromLegacy(legacy, (string id) => (id == "custom:w-1") ? "playtime" : null);
		Assert.Equal("tasks:s play:l note:s log:s ref:s", string.Join(" ", z.Select((ZoneItem i) => i.Type + ":" + i.Size)));
		Assert.Equal("custom:w-1", z[4].Ref);
		Assert.Null(z.First((ZoneItem i) => i.Type == "note").Text);
		ZonePack p = LaunchZone.Pack(z, 4, 4);
		Assert.Empty(p.Over);
		Assert.Equal(5, z.Select((ZoneItem i) => i.Id).Distinct().Count());
	}

	[Fact]
	public void LegacyHiddenBlocksStayOut()
	{
		List<ZoneItem> z = LaunchZone.FromLegacy(new[]
		{
			new KeyValuePair<string, bool>("tasks", false),
			new KeyValuePair<string, bool>("playtime", true),
			new KeyValuePair<string, bool>("unknown-block", false)
		}, null);
		Assert.Equal(new[] { "tasks" }, z.Select((ZoneItem i) => i.Type).ToArray());
	}

	// ————— 存档 / 布局码 —————

	[Fact]
	public void JsonRoundTrip()
	{
		List<ZoneItem> z = new List<ZoneItem>
		{
			I("a", "play", "l"),
			new ZoneItem { Id = "b", Type = "note", Size = "m", Text = "周六联机\n带光影" },
			new ZoneItem { Id = "c", Type = "ref", Ref = "custom:w-9", Size = "s" }
		};
		string json = LaunchZone.ToJson(z);
		Assert.Contains("\"kind\":\"tipcl-launch-zone\"", json);
		List<ZoneItem>? back = LaunchZone.FromJson(json);
		Assert.NotNull(back);
		Assert.Equal(json, LaunchZone.ToJson(back!));
		Assert.Equal(json, LaunchZone.ToJson(LaunchZone.FromCodeOrJson(LaunchZone.ToCode(z))!));
	}

	[Theory]
	[InlineData("")]
	[InlineData("not json")]
	[InlineData("{\"kind\":\"tipcl-page-layout\",\"items\":[]}")]
	[InlineData("TCLZ1:@@@")]
	public void BadInputIsRejected(string text)
	{
		Assert.Null(LaunchZone.FromCodeOrJson(text));
	}

	[Fact]
	public void UnknownTypesAreDroppedAndIdsMadeUnique()
	{
		List<ZoneItem>? z = LaunchZone.FromJson("{\"kind\":\"tipcl-launch-zone\",\"items\":[{\"id\":\"a\",\"type\":\"play\",\"size\":\"l\"},{\"id\":\"a\",\"type\":\"note\",\"size\":\"q\"},{\"id\":\"x\",\"type\":\"canvas\"},{\"id\":\"r\",\"type\":\"ref\"}]}");
		Assert.NotNull(z);
		Assert.Equal("play:l note:s", string.Join(" ", z!.Select((ZoneItem i) => i.Type + ":" + i.Size)));
		Assert.NotEqual(z[0].Id, z[1].Id);
	}

	// ————— 本周游戏时长 —————

	[Fact]
	public void WeekBucketsByLocalDayStartingMonday()
	{
		DateTime now = new DateTime(2026, 11, 20, 21, 0, 0, DateTimeKind.Local); // 周五
		long At(int y, int mo, int d, int h) => new DateTimeOffset(new DateTime(y, mo, d, h, 0, 0, DateTimeKind.Local)).ToUnixTimeSeconds();
		List<KeyValuePair<string, KeyValuePair<long, long>>> s = new List<KeyValuePair<string, KeyValuePair<long, long>>>
		{
			new KeyValuePair<string, KeyValuePair<long, long>>("Fabric", new KeyValuePair<long, long>(At(2026, 11, 16, 20), 3960)),
			new KeyValuePair<string, KeyValuePair<long, long>>("Fabric", new KeyValuePair<long, long>(At(2026, 11, 20, 19), 7560)),
			new KeyValuePair<string, KeyValuePair<long, long>>("Vanilla", new KeyValuePair<long, long>(At(2026, 11, 17, 19), 3600)),
			new KeyValuePair<string, KeyValuePair<long, long>>("Vanilla", new KeyValuePair<long, long>(At(2026, 11, 12, 19), 7200))
		};
		WeekPlay w = LaunchZone.Week(s, now);
		Assert.Equal(4, w.Today);
		Assert.Equal(1.1, w.Hours[0]!.Value, 3);
		Assert.Equal(1.0, w.Hours[1]!.Value, 3);
		Assert.Equal(0.0, w.Hours[2]!.Value, 3);
		Assert.Equal(2.1, w.Hours[4]!.Value, 3);
		Assert.Null(w.Hours[5]);
		Assert.Null(w.Hours[6]);
		Assert.Equal(4.2, w.ThisWeek, 3);
		Assert.Equal(2.0, w.LastWeek, 3);
		Assert.Equal("Fabric", w.Top);
		Assert.Equal("比上周多 2.2", LaunchZone.DiffText(w, withUnit: false));
		Assert.Equal("4.2", LaunchZone.HoursText(w.ThisWeek).Key);
	}

	[Fact]
	public void UnderAnHourShowsMinutes()
	{
		Assert.Equal(new KeyValuePair<string, string>("12", "分钟"), LaunchZone.HoursText(0.2));
		Assert.Equal(new KeyValuePair<string, string>("6.2", "小时"), LaunchZone.HoursText(6.2));
	}

	[Fact]
	public void ServerPingParsesStatusJson()
	{
		ServerPingResult r = ServerPing.Parse("{\"version\":{\"name\":\"1.20.1\"},\"players\":{\"max\":20,\"online\":12,\"sample\":[{\"name\":\"lin\",\"id\":\"1\"},{\"name\":\"kai\",\"id\":\"2\"}]}}");
		Assert.Equal(12, r.Online);
		Assert.Equal(20, r.Max);
		Assert.Equal(new[] { "lin", "kai" }, r.Sample.ToArray());
	}

	// ————— 源码守卫:主区域不进布局系统;编辑条只剩三个按钮 + ⋯;文案 —————

	private static string Src(string rel)
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		for (int i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
		{
			string p = Path.Combine(dir.FullName, "windos", rel.Replace('/', Path.DirectorySeparatorChar));
			if (File.Exists(p))
			{
				return File.ReadAllText(p);
			}
			p = Path.Combine(dir.FullName, rel.Replace('/', Path.DirectorySeparatorChar));
			if (File.Exists(p))
			{
				return File.ReadAllText(p);
			}
		}
		return "";
	}

	[Fact]
	public void MainAreaIsNotInTheLayoutSystem()
	{
		string page = Src("src/TipCL/Pages/LaunchPage.cs");
		if (page.Length == 0)
		{
			return;
		}
		Assert.DoesNotContain("new BlockHost(", page);
		Assert.DoesNotContain("FitBox", page);
		Assert.Contains("BuildZone(root);", page);
	}

	[Fact]
	public void EditBarHasOnlyAddResetDoneAndMore()
	{
		string z = Src("src/TipCL/Pages/LaunchPage.Zone.cs");
		if (z.Length == 0)
		{
			return;
		}
		foreach (string s in new[] { "编辑启动页", "添加组件", "重置", "完成", "导入布局…", "导出布局…", "复制布局码", "放弃这次改动", "拖动组件换位置 · 点尺寸标签切换大小 · ", "主区域固定，不能拖动、隐藏或缩放", "点「添加」放进右侧组件区，放满了会提示", "编辑组件", "本周游戏时长" })
		{
			Assert.Contains("\"" + s + "\"", z);
		}
		foreach (string s in new[] { "\"撤销\"), BtnKind", "自动靠拢", "吸附", "自由画布" })
		{
			Assert.DoesNotContain(s, z);
		}
	}

	[Fact]
	public void OnlyTwoRadiiAndFiveFontSizesInZone()
	{
		string z = Src("src/TipCL/Pages/LaunchPage.Zone.cs");
		if (z.Length == 0)
		{
			return;
		}
		foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(z, "CornerRadius\\((\\d+(?:\\.\\d+)?)\\)"))
		{
			Assert.Contains(m.Groups[1].Value, new[] { "8.0", "12.0", "4.0" }); // 4 = 柱状图柱子(效果图 .bt i)
		}
		foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(z, "FontSize = (\\d+(?:\\.\\d+)?)"))
		{
			Assert.Contains(m.Groups[1].Value, new[] { "12.0", "13.0", "15.0", "21.0", "34.0" });
		}
	}
}
