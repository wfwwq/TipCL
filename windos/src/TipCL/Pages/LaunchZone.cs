using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace TipCL.Pages;

// 启动页 v3 组件区(效果图 _r35/ref/launch-v3.html,用户定稿,取代 pages3.html 的启动页部分)。
// 规则(效果图原文):
//   · 主区域(实例名 / 启动 / 联机)不进布局系统,是固定层;
//   · 组件区在右侧,4 列网格:列宽 74、行高 96、间距 12;尺寸只有 2×1 / 2×2 / 4×2 三档;
//   · 窗口变窄时减列数不缩字:4 列变 2 列,4×2 换成 2×2 的版式,放不下的收起并提示;
//   · 松手就按顺序重排(首个能放下的位置,先行后列),不叠放、不留洞;没有自由画布。
// 这里只放纯逻辑(不引用 WPF),测试直接调;界面在 LaunchPage.Zone.cs。

/// <summary>组件区里的一个组件。Type:play / server / short / note / clock / shots / tasks / log / ref。</summary>
public sealed class ZoneItem
{
	public string Id { get; set; } = "";

	public string Type { get; set; } = "";

	/// <summary>s = 2×1,m = 2×2,l = 4×2。</summary>
	public string Size { get; set; } = LaunchZone.Small;

	/// <summary>Type == ref 时指向用户自建组件 / 插件区块(custom:&lt;id&gt; / plugin:&lt;p&gt;:&lt;b&gt;)。</summary>
	public string? Ref { get; set; }

	/// <summary>便签自带的文字;null = 用 UiPrefs.Notes(旧版启动页那张便签)。</summary>
	public string? Text { get; set; }

	public ZoneItem Clone()
	{
		return new ZoneItem { Id = Id, Type = Type, Size = Size, Ref = Ref, Text = Text };
	}
}

public struct ZoneRect
{
	public int X;

	public int Y;

	public int W;

	public int H;

	/// <summary>实际版式(窄窗口下 l 按 m 摆)。</summary>
	public string Eff;
}

public sealed class ZonePack
{
	public Dictionary<string, ZoneRect> Out { get; } = new Dictionary<string, ZoneRect>(StringComparer.Ordinal);

	/// <summary>放不下、收起来的组件 id(按顺序)。</summary>
	public List<string> Over { get; } = new List<string>();
}

public sealed class ZoneType
{
	public string Key = "";

	public string Name = "";

	public string Desc = "";

	public string[] Sizes = new string[0];

	public bool Multi;
}

public sealed class WeekPlay
{
	/// <summary>周一 … 周日,单位小时;今天之后为 null。</summary>
	public double?[] Hours = new double?[7];

	public double ThisWeek;

	public double LastWeek;

	public string? Top;

	/// <summary>今天是第几天(0 = 周一)。</summary>
	public int Today;
}

public static class LaunchZone
{
	public const double Col = 74.0;

	public const double Row = 96.0;

	public const double Gap = 12.0;

	public const int WideCols = 4;

	public const int NarrowCols = 2;

	public const int MaxRows = 4;

	/// <summary>页面宽度小于这个值用 2 列(效果图 setWidth:w &lt; 900 ? 2 : 4)。</summary>
	public const double NarrowBelow = 900.0;

	public const string Small = "s";

	public const string Medium = "m";

	public const string Large = "l";

	public const string Kind = "tipcl-launch-zone";

	public const string CodePrefix = "TCLZ1:";

	public const string TypeRef = "ref";

	// 组件库顺序 = 效果图 TYPES 顺序,后面接现有内容映射来的两类(下载任务 / 游戏日志)
	public static readonly IReadOnlyList<ZoneType> Types = new ZoneType[8]
	{
		new ZoneType { Key = "play", Name = "游戏时长", Desc = "本周每天玩了多久", Sizes = new string[3] { Medium, Large, Small } },
		new ZoneType { Key = "server", Name = "服务器状态", Desc = "在线人数、延迟、一键加入", Sizes = new string[2] { Small, Medium } },
		new ZoneType { Key = "short", Name = "快捷入口", Desc = "模组、截图、日志、存档文件夹", Sizes = new string[3] { Small, Medium, Large } },
		new ZoneType { Key = "note", Name = "便签", Desc = "记一句话，可以加多张", Sizes = new string[2] { Small, Medium }, Multi = true },
		new ZoneType { Key = "clock", Name = "时钟", Desc = "当前时间和日期", Sizes = new string[2] { Small, Medium } },
		new ZoneType { Key = "shots", Name = "最近截图", Desc = ".minecraft/screenshots 最新几张", Sizes = new string[2] { Medium, Large } },
		new ZoneType { Key = "tasks", Name = "下载任务", Desc = "正在下载和最近完成的任务", Sizes = new string[2] { Small, Medium } },
		new ZoneType { Key = "log", Name = "游戏日志", Desc = "最近一次启动的输出", Sizes = new string[3] { Small, Medium, Large } }
	};

	public static int W(string? size)
	{
		return (size == Large) ? 4 : 2;
	}

	public static int H(string? size)
	{
		return (size == Medium || size == Large) ? 2 : 1;
	}

	public static string Normalize(string? size)
	{
		return (size == Medium || size == Large) ? size! : Small;
	}

	public static string DimLabel(string? size)
	{
		return W(size) + "×" + H(size);
	}

	public static double Px(int x)
	{
		return x * (Col + Gap);
	}

	public static double Py(int y)
	{
		return y * (Row + Gap);
	}

	public static double SpanW(int n)
	{
		return n * Col + (n - 1) * Gap;
	}

	public static double SpanH(int n)
	{
		return n * Row + (n - 1) * Gap;
	}

	public static int ColsFor(double pageWidth)
	{
		return (pageWidth < NarrowBelow) ? NarrowCols : WideCols;
	}

	/// <summary>页面高度能放几行(上 18 + 组件区 + 10 + 编辑组件按钮 28 + 底部新闻 / AI 条 70;最多 4 行)。</summary>
	public static int RowsFor(double pageHeight)
	{
		if (double.IsNaN(pageHeight) || pageHeight <= 0.0)
		{
			return MaxRows;
		}
		int rows = (int)Math.Floor((pageHeight - 18.0 - 10.0 - 28.0 - 70.0 + Gap) / (Row + Gap));
		return Math.Max(1, Math.Min(MaxRows, rows));
	}

	/// <summary>窄窗口(列数 &lt; 4)下 4×2 换成 2×2 的版式。</summary>
	public static string Eff(string? size, int cols)
	{
		string s = Normalize(size);
		return (cols < WideCols && s == Large) ? Medium : s;
	}

	public static ZoneType? TypeOf(string? key)
	{
		foreach (ZoneType t in Types)
		{
			if (t.Key == key)
			{
				return t;
			}
		}
		return null;
	}

	/// <summary>自建组件 / 插件区块能用的尺寸(按它原来的类型)。</summary>
	public static string[] RefSizes(string? widgetType)
	{
		switch (widgetType)
		{
		case "playtime":
			return new string[3] { Medium, Large, Small };
		case "note":
		case "clock":
		case "shortcut":
			return new string[2] { Small, Medium };
		case "divider":
		case "button":
			return new string[1] { Small };
		default:
			return new string[3] { Small, Medium, Large };
		}
	}

	public static string[] SizesOf(ZoneItem it, Func<string, string?>? refType = null)
	{
		if (it.Type == TypeRef)
		{
			return RefSizes((refType != null && it.Ref != null) ? refType(it.Ref) : null);
		}
		return TypeOf(it.Type)?.Sizes ?? new string[1] { Small };
	}

	/// <summary>按顺序给每个组件找首个能放下的位置(先行后列);放不下的进 Over。保证不叠放。</summary>
	public static ZonePack Pack(IReadOnlyList<ZoneItem> list, int cols, int rows)
	{
		ZonePack pack = new ZonePack();
		bool[] used = new bool[Math.Max(1, cols * rows)];
		foreach (ZoneItem it in list)
		{
			string eff = Eff(it.Size, cols);
			int w = Math.Min(W(eff), cols);
			int h = H(eff);
			bool ok = false;
			for (int y = 0; y <= rows - h && !ok; y++)
			{
				for (int x = 0; x <= cols - w && !ok; x++)
				{
					if (Fits(used, cols, x, y, w, h))
					{
						Mark(used, cols, x, y, w, h);
						pack.Out[it.Id] = new ZoneRect { X = x, Y = y, W = w, H = h, Eff = eff };
						ok = true;
					}
				}
			}
			if (!ok)
			{
				pack.Over.Add(it.Id);
			}
		}
		return pack;
	}

	private static bool Fits(bool[] used, int cols, int x, int y, int w, int h)
	{
		for (int j = y; j < y + h; j++)
		{
			for (int i = x; i < x + w; i++)
			{
				if (used[j * cols + i])
				{
					return false;
				}
			}
		}
		return true;
	}

	private static void Mark(bool[] used, int cols, int x, int y, int w, int h)
	{
		for (int j = y; j < y + h; j++)
		{
			for (int i = x; i < x + w; i++)
			{
				used[j * cols + i] = true;
			}
		}
	}

	/// <summary>
	/// 拖动中:把被拖组件插到每个可能的位置,取它落点中心离指针下组件中心最近的那种顺序(放不下的组件每个罚 1000)。
	/// 其它组件保持相对顺序 = 「松手就按顺序重排」。返回 null = 找不到能放下被拖组件的位置。
	/// </summary>
	public static List<ZoneItem>? BestOrder(IReadOnlyList<ZoneItem> items, string dragId, double cx, double cy, int cols, int rows, out ZoneRect rect)
	{
		rect = default(ZoneRect);
		ZoneItem? me = items.FirstOrDefault((ZoneItem i) => i.Id == dragId);
		if (me == null)
		{
			return null;
		}
		List<ZoneItem> others = items.Where((ZoneItem i) => i.Id != dragId).ToList();
		List<ZoneItem>? best = null;
		double bd = double.MaxValue;
		for (int k = 0; k <= others.Count; k++)
		{
			List<ZoneItem> c = new List<ZoneItem>(others.Count + 1);
			c.AddRange(others.Take(k));
			c.Add(me);
			c.AddRange(others.Skip(k));
			ZonePack p = Pack(c, cols, rows);
			if (!p.Out.TryGetValue(me.Id, out var r))
			{
				continue;
			}
			double dx = Px(r.X) + SpanW(r.W) / 2.0 - cx;
			double dy = Py(r.Y) + SpanH(r.H) / 2.0 - cy;
			double d = Math.Sqrt(dx * dx + dy * dy) + p.Over.Count * 1000.0;
			if (d < bd)
			{
				bd = d;
				best = c;
				rect = r;
			}
		}
		return best;
	}

	public static bool SameOrder(IReadOnlyList<ZoneItem> a, IReadOnlyList<ZoneItem> b)
	{
		return a.Count == b.Count && a.Select((ZoneItem i) => i.Id).SequenceEqual(b.Select((ZoneItem i) => i.Id));
	}

	/// <summary>
	/// 点尺寸标签:在本组件允许的尺寸里按顺序找下一个「实际版式不同」的尺寸。
	/// 返回 null 时 message = "one"(窄窗口下只有一种版式)或 "nofit:2×2"(这里放不下),界面据此出提示。
	/// </summary>
	public static string? NextSize(IReadOnlyList<ZoneItem> items, string id, int cols, int rows, Func<string, string?>? refType, out string? message)
	{
		message = null;
		ZoneItem? it = items.FirstOrDefault((ZoneItem i) => i.Id == id);
		if (it == null)
		{
			return null;
		}
		string[] sizes = SizesOf(it, refType);
		string cur = Eff(it.Size, cols);
		int idx = Array.IndexOf(sizes, Normalize(it.Size));
		string next = Normalize(it.Size);
		for (int k = 1; k <= sizes.Length; k++)
		{
			string cand = sizes[((idx < 0 ? 0 : idx) + k) % sizes.Length];
			if (Eff(cand, cols) != cur)
			{
				next = cand;
				break;
			}
		}
		if (next == Normalize(it.Size))
		{
			message = "one";
			return null;
		}
		List<ZoneItem> trial = items.Select((ZoneItem i) => (i.Id == id) ? WithSize(i, next) : i).ToList();
		if (Pack(trial, cols, rows).Over.Count > Pack(items, cols, rows).Over.Count)
		{
			message = "nofit:" + DimLabel(Eff(next, cols));
			return null;
		}
		return next;
	}

	private static ZoneItem WithSize(ZoneItem it, string size)
	{
		ZoneItem c = it.Clone();
		c.Size = size;
		return c;
	}

	/// <summary>新加组件的默认尺寸 = 允许尺寸里面积最小的那档。</summary>
	public static string SmallestSize(string[] sizes)
	{
		return sizes.OrderBy((string s) => W(s) * H(s)).FirstOrDefault() ?? Small;
	}

	/// <summary>效果图默认:本周游戏时长 4×2 + 服务器 2×1 + 快捷入口 2×1。</summary>
	public static List<ZoneItem> Defaults()
	{
		return new List<ZoneItem>
		{
			new ZoneItem { Id = "a", Type = "play", Size = Large },
			new ZoneItem { Id = "b", Type = "server", Size = Small },
			new ZoneItem { Id = "c", Type = "short", Size = Small }
		};
	}

	/// <summary>
	/// 读时推导(存档原样不动,编辑器「完成」一次才写新格式):旧版启动页区块 → 组件区。
	/// 新闻是固定新闻条,不进组件区;隐藏的块不迁;旧版没有显示任何组件(默认只有新闻)→ 效果图默认。
	/// 尺寸:游戏时长 4×2(效果图),其余 2×1,保证常见的「任务 + 时长 + 便签 + 日志 + 1 个自建」正好放满 4×4。
	/// </summary>
	public static List<ZoneItem> FromLegacy(IEnumerable<KeyValuePair<string, bool>>? blocks, Func<string, string?>? refType)
	{
		List<ZoneItem> list = new List<ZoneItem>();
		if (blocks != null)
		{
			int n = 0;
			foreach (KeyValuePair<string, bool> b in blocks)
			{
				string id = b.Key ?? "";
				if (b.Value || id.Length == 0 || id == "news")
				{
					continue;
				}
				ZoneItem? it = null;
				switch (id)
				{
				case "tasks":
					it = new ZoneItem { Type = "tasks", Size = Small };
					break;
				case "playtime":
					it = new ZoneItem { Type = "play", Size = Large };
					break;
				case "notes":
					it = new ZoneItem { Type = "note", Size = Small };
					break;
				case "log":
					it = new ZoneItem { Type = "log", Size = Small };
					break;
				default:
					if (id.StartsWith("custom:", StringComparison.Ordinal) || id.StartsWith("plugin:", StringComparison.Ordinal))
					{
						string[] sizes = RefSizes(refType?.Invoke(id));
						it = new ZoneItem { Type = TypeRef, Ref = id, Size = sizes.Contains(Small) ? Small : sizes[0] };
					}
					break;
				}
				if (it != null)
				{
					it.Id = "m" + n++;
					list.Add(it);
				}
			}
			// 游戏时长(4×2)排第一个 = 效果图的常态版式(右侧顶部是带每日柱状图的「本周游戏时长」);其余保持旧顺序
			ZoneItem? play = list.FirstOrDefault((ZoneItem i) => i.Type == "play");
			if (play != null && list.IndexOf(play) > 0)
			{
				list.Remove(play);
				list.Insert(0, play);
			}
		}
		return (list.Count == 0) ? Defaults() : list;
	}

	public static string NewId(IReadOnlyList<ZoneItem> items)
	{
		for (int n = 1; ; n++)
		{
			string id = "n" + n;
			if (!items.Any((ZoneItem i) => i.Id == id))
			{
				return id;
			}
		}
	}

	public static List<ZoneItem> CloneAll(IEnumerable<ZoneItem> items)
	{
		return items.Select((ZoneItem i) => i.Clone()).ToList();
	}

	// ————— 存档(UiPrefs.LaunchZone)/ 导入导出 / 布局码 —————

	public static string ToJson(IReadOnlyList<ZoneItem> items)
	{
		using MemoryStream ms = new MemoryStream();
		using (Utf8JsonWriter w = new Utf8JsonWriter(ms))
		{
			w.WriteStartObject();
			w.WriteString("kind", Kind);
			w.WriteNumber("version", 1);
			w.WriteStartArray("items");
			foreach (ZoneItem it in items)
			{
				w.WriteStartObject();
				w.WriteString("id", it.Id);
				w.WriteString("type", it.Type);
				w.WriteString("size", Normalize(it.Size));
				if (it.Ref != null)
				{
					w.WriteString("ref", it.Ref);
				}
				if (it.Text != null)
				{
					w.WriteString("text", it.Text);
				}
				w.WriteEndObject();
			}
			w.WriteEndArray();
			w.WriteEndObject();
		}
		return Encoding.UTF8.GetString(ms.ToArray());
	}

	/// <summary>解析失败 / 不是组件区文档返回 null(调用方退回读时推导)。</summary>
	public static List<ZoneItem>? FromJson(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}
		try
		{
			using JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("kind", out var kind) || kind.GetString() != Kind)
			{
				return null;
			}
			if (!root.TryGetProperty("items", out var arr) || arr.ValueKind != JsonValueKind.Array)
			{
				return null;
			}
			List<ZoneItem> list = new List<ZoneItem>();
			foreach (JsonElement e in arr.EnumerateArray())
			{
				if (e.ValueKind != JsonValueKind.Object)
				{
					continue;
				}
				string type = Str(e, "type") ?? "";
				string? r = Str(e, "ref");
				if ((type != TypeRef && TypeOf(type) == null) || (type == TypeRef && string.IsNullOrEmpty(r)))
				{
					continue; // 不认识的类型丢掉,不让一条坏数据毁掉整个组件区
				}
				string id = Str(e, "id") ?? "";
				if (id.Length == 0 || list.Any((ZoneItem i) => i.Id == id))
				{
					id = NewId(list);
				}
				list.Add(new ZoneItem { Id = id, Type = type, Size = Normalize(Str(e, "size")), Ref = r, Text = Str(e, "text") });
			}
			return list;
		}
		catch
		{
			return null;
		}
	}

	private static string? Str(JsonElement e, string name)
	{
		return (e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String) ? v.GetString() : null;
	}

	public static string ToCode(IReadOnlyList<ZoneItem> items)
	{
		return CodePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(ToJson(items)));
	}

	/// <summary>布局码或整段 JSON 都认。</summary>
	public static List<ZoneItem>? FromCodeOrJson(string? text)
	{
		string t = (text ?? "").Trim();
		if (t.StartsWith(CodePrefix, StringComparison.Ordinal))
		{
			try
			{
				t = Encoding.UTF8.GetString(Convert.FromBase64String(t.Substring(CodePrefix.Length).Trim()));
			}
			catch
			{
				return null;
			}
		}
		return FromJson(t);
	}

	// ————— 「本周游戏时长」:按会话起点归到本地日期,周一为一周第一天 —————

	public static WeekPlay Week(IEnumerable<KeyValuePair<string, KeyValuePair<long, long>>> sessions, DateTime nowLocal)
	{
		WeekPlay wp = new WeekPlay();
		int today = ((int)nowLocal.DayOfWeek + 6) % 7;
		wp.Today = today;
		DateTime monday = nowLocal.Date.AddDays(-today);
		DateTime lastMonday = monday.AddDays(-7);
		double[] hours = new double[7];
		Dictionary<string, double> byInst = new Dictionary<string, double>(StringComparer.Ordinal);
		foreach (KeyValuePair<string, KeyValuePair<long, long>> s in sessions)
		{
			long start = s.Value.Key;
			long dur = s.Value.Value;
			if (dur <= 0)
			{
				continue;
			}
			DateTime at = DateTimeOffset.FromUnixTimeSeconds(start).LocalDateTime;
			double h = dur / 3600.0;
			if (at >= monday && at < monday.AddDays(7))
			{
				hours[(at.Date - monday).Days] += h;
				wp.ThisWeek += h;
				byInst.TryGetValue(s.Key ?? "", out var cur);
				byInst[s.Key ?? ""] = cur + h;
			}
			else if (at >= lastMonday && at < monday)
			{
				wp.LastWeek += h;
			}
		}
		for (int i = 0; i < 7; i++)
		{
			wp.Hours[i] = (i <= today) ? hours[i] : null;
		}
		if (byInst.Count > 0)
		{
			KeyValuePair<string, double> top = byInst.OrderByDescending((KeyValuePair<string, double> kv) => kv.Value).First();
			if (top.Value > 0.0 && top.Key.Length > 0)
			{
				wp.Top = top.Key;
			}
		}
		return wp;
	}

	/// <summary>大数字 + 单位:不到 1 小时按分钟,否则一位小数的小时。</summary>
	public static KeyValuePair<string, string> HoursText(double hours)
	{
		if (hours < 1.0)
		{
			return new KeyValuePair<string, string>(((int)Math.Round(hours * 60.0)).ToString(), "分钟");
		}
		return new KeyValuePair<string, string>(hours.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), "小时");
	}

	/// <summary>「比上周多 1.4 小时」/「比上周少 …」/「和上周一样」。</summary>
	public static string DiffText(WeekPlay wp, bool withUnit)
	{
		double d = wp.ThisWeek - wp.LastWeek;
		if (Math.Abs(d) < 1.0 / 60.0)
		{
			return "和上周一样";
		}
		KeyValuePair<string, string> t = HoursText(Math.Abs(d));
		string unit = (withUnit || t.Value != "小时") ? (" " + t.Value) : "";
		return ((d > 0.0) ? "比上周多 " : "比上周少 ") + t.Key + unit;
	}
}
