// AI4-launch:启动页右侧组件区(launch v3)的纯模型 —— 4 列网格、三档尺寸、按顺序打包、拖动重排、
// 撤销重做、读写布局文件。不依赖界面,可单测。
//
// 存储位置:布局文件 v2 的 pages.launch.zone(PageLayoutV2.Extra["zone"],页级未知字段,
// 布局引擎原样保留、不解释),所以旧版本读这份文件不会坏,本版本读旧文件(没有 zone)时回落默认组件。
//   "zone": { "version": 1, "items": [ { "id": "playtime", "type": "playtime", "size": "l", "settings": {} }, ... ] }
//
// 网格规则(launch_v3.html 第 2~4 条):
//   列宽 74 / 行高 96 / 间距 12;4 行;宽窗口 4 列、窄窗口 2 列;
//   尺寸只有 s=2×1 / m=2×2 / l=4×2;2 列时 l 按 m 的版式排(减列不缩字);
//   按列表顺序逐个放到「先行后列第一个放得下」的位置 —— 不叠放;放不下的进 Overflow(收起并提示)。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TipCL.Core.Launcher.Layout;

/// <summary>启动页组件区的常量与尺寸规则。</summary>
public static class LaunchZone
{
    /// <summary>pages.launch 里存组件区的键。</summary>
    public const string ExtraKey = "zone";

    /// <summary>组件区所在页。</summary>
    public const string PageId = "launch";

    /// <summary>zone 对象自己的版本(与布局文件 version 无关)。</summary>
    public const int ZoneVersion = 1;

    public const double ColW = 74;
    public const double RowH = 96;
    public const double Gap = 12;
    public const int Rows = 4;
    public const int ColsWide = 4;
    public const int ColsNarrow = 2;

    /// <summary>窗口宽度低于这个值切成 2 列(效果图 1000 → 4 列、780 → 2 列)。</summary>
    public const double NarrowBelow = 900;

    public const string Small = "s";
    public const string Medium = "m";
    public const string Large = "l";

    public const string TPlaytime = "playtime";
    public const string TServer = "server";
    public const string TShortcuts = "shortcuts";
    public const string TNote = "note";
    public const string TClock = "clock";
    public const string TScreenshots = "screenshots";

    /// <summary>组件库里的顺序(= launch_v3 TYPES 的顺序)。</summary>
    public static readonly IReadOnlyList<string> Types = new[] { TPlaytime, TServer, TShortcuts, TNote, TClock, TScreenshots };

    private static readonly Dictionary<string, string[]> SizeTable = new(StringComparer.Ordinal)
    {
        [TPlaytime] = new[] { Medium, Large, Small },
        [TServer] = new[] { Small, Medium },
        [TShortcuts] = new[] { Small, Medium, Large },
        [TNote] = new[] { Small, Medium },
        [TClock] = new[] { Small, Medium },
        [TScreenshots] = new[] { Medium, Large },
    };

    public static bool IsKnownType(string? type) => type != null && SizeTable.ContainsKey(type);

    /// <summary>这个类型允许的尺寸(切换尺寸按这个顺序轮换)。未知类型返回空。</summary>
    public static IReadOnlyList<string> SizesOf(string type)
        => SizeTable.TryGetValue(type, out var s) ? s : Array.Empty<string>();

    /// <summary>可以放多张的类型(只有便签)。</summary>
    public static bool IsMulti(string type) => type == TNote;

    public static bool IsSize(string? size) => size == Small || size == Medium || size == Large;

    /// <summary>尺寸 → 占几列 × 几行。</summary>
    public static (int W, int H) Dim(string size) => size switch
    {
        Large => (4, 2),
        Medium => (2, 2),
        _ => (2, 1),
    };

    /// <summary>「2×1」这样的标签(编辑态尺寸按钮、组件库)。</summary>
    public static string DimLabel(string size)
    {
        var (w, h) = Dim(size);
        return w.ToString(CultureInfo.InvariantCulture) + "×" + h.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>实际排版用的尺寸:2 列时 4×2 换成 2×2 的版式。</summary>
    public static string EffSize(string size, int cols) => cols < ColsWide && size == Large ? Medium : size;

    /// <summary>新加组件的默认尺寸:允许尺寸里面积最小的。</summary>
    public static string DefaultSize(string type)
    {
        var sizes = SizesOf(type);
        if (sizes.Count == 0)
            return Small;
        return sizes.OrderBy(s => Dim(s).W * Dim(s).H).First();
    }

    /// <summary>格子坐标 → 像素偏移。</summary>
    public static double OffsetX(int x) => x * (ColW + Gap);

    public static double OffsetY(int y) => y * (RowH + Gap);

    /// <summary>n 个单位连同中间间距的总长。</summary>
    public static double Span(int n, double unit) => n <= 0 ? 0 : n * unit + (n - 1) * Gap;

    public static double Width(int cols) => Span(cols, ColW);

    public static double Height => Span(Rows, RowH);

    /// <summary>按可用宽度定列数。</summary>
    public static int ColsFor(double width) => width > 0 && width < NarrowBelow ? ColsNarrow : ColsWide;

    /// <summary>按可用高度定行数(最多 4 行;窗口太矮时少排几行,放不下的照样收起提示)。</summary>
    public static int RowsFor(double height)
    {
        if (height <= 0 || double.IsNaN(height))
            return Rows;
        int r = (int)Math.Floor((height + Gap) / (RowH + Gap));
        return Math.Clamp(r, 1, Rows);
    }

    /// <summary>默认组件 = 游戏时长 4×2 + 服务器状态 2×1 + 快捷入口 2×1。</summary>
    public static List<ZoneItem> Defaults() => new()
    {
        new ZoneItem { Id = "playtime", Type = TPlaytime, Size = Large },
        new ZoneItem { Id = "server", Type = TServer, Size = Small },
        new ZoneItem { Id = "shortcuts", Type = TShortcuts, Size = Small },
    };

    // —————————————— 打包 ——————————————

    /// <summary>按顺序打包:每个组件放到「先行后列」第一个放得下的位置,保证不叠放;放不下的进 Overflow。</summary>
    public static ZonePack Pack(IReadOnlyList<ZoneItem> items, int cols, int rows = Rows)
    {
        cols = cols < ColsWide ? ColsNarrow : ColsWide;
        rows = Math.Clamp(rows, 1, Rows);
        var occ = new bool[Rows, ColsWide];
        var pack = new ZonePack { Cols = cols, Rows = rows };
        foreach (var it in items)
        {
            string es = EffSize(it.Size, cols);
            var (w, h) = Dim(es);
            bool ok = false;
            for (int y = 0; y <= rows - h && !ok; y++)
            {
                for (int x = 0; x <= cols - w && !ok; x++)
                {
                    if (!Fits(occ, x, y, w, h))
                        continue;
                    for (int j = y; j < y + h; j++)
                        for (int i = x; i < x + w; i++)
                            occ[j, i] = true;
                    pack.Placed[it.Id] = new ZonePlacement { Id = it.Id, X = x, Y = y, W = w, H = h, EffSize = es };
                    pack.Order.Add(it.Id);
                    ok = true;
                }
            }
            if (!ok)
                pack.Overflow.Add(it.Id);
        }
        return pack;
    }

    private static bool Fits(bool[,] occ, int x, int y, int w, int h)
    {
        for (int j = y; j < y + h; j++)
            for (int i = x; i < x + w; i++)
                if (occ[j, i])
                    return false;
        return true;
    }

    /// <summary>
    /// 拖动中:被拖组件的中心点(组件区坐标)落在哪,就把它插到哪个顺序位置 —— 把它插进每个可能的位置各打包一次,
    /// 取它的落点离指针中心最近的那个(每多一个放不下的组件罚 1000,和效果图同一算法)。
    /// 返回新顺序与它的落点;哪里都放不下时返回 null。
    /// </summary>
    public static (List<ZoneItem> Order, ZonePlacement Spot)? DragOrder(IReadOnlyList<ZoneItem> items, string dragId, double centerX, double centerY, int cols, int rows = Rows)
    {
        var me = items.FirstOrDefault(i => i.Id == dragId);
        if (me == null)
            return null;
        var others = items.Where(i => i.Id != dragId).ToList();
        (List<ZoneItem>, ZonePlacement)? best = null;
        double bestD = double.MaxValue;
        for (int k = 0; k <= others.Count; k++)
        {
            var cand = new List<ZoneItem>(others);
            cand.Insert(k, me);
            var p = Pack(cand, cols, rows);
            if (!p.Placed.TryGetValue(dragId, out var r))
                continue;
            double cx = OffsetX(r.X) + Span(r.W, ColW) / 2;
            double cy = OffsetY(r.Y) + Span(r.H, RowH) / 2;
            double d = Math.Sqrt((cx - centerX) * (cx - centerX) + (cy - centerY) * (cy - centerY)) + p.Overflow.Count * 1000;
            if (d < bestD)
            {
                bestD = d;
                best = (cand, r);
            }
        }
        return best;
    }

    // —————————————— 读写布局文件 ——————————————

    /// <summary>
    /// 从布局页读组件区。没有 zone(旧文件 / 第一次)→ 默认组件;旧版启动页里显示着的「便签」区块
    /// 迁成一张便签组件(内容取 legacyNoteText),旧数据里的内容不丢。
    /// zone 存在但内容坏了 → 宽容读取:丢掉无效项,剩下的照用;一个都不剩且 items 不是空数组 → 默认。
    /// </summary>
    public static List<ZoneItem> Read(PageLayoutV2? page, string? legacyNoteText = null)
    {
        if (page?.Extra != null && page.Extra.TryGetValue(ExtraKey, out var raw) && raw is Dictionary<string, object?> zone)
        {
            if (zone.TryGetValue("items", out var itemsRaw) && itemsRaw is List<object?> arr)
            {
                var list = new List<ZoneItem>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var o in arr)
                {
                    var it = ZoneItem.FromPlain(o);
                    if (it == null || !seen.Add(it.Id))
                        continue;
                    if (!IsMulti(it.Type) && list.Any(x => x.Type == it.Type))
                        continue;
                    list.Add(it);
                }
                if (list.Count > 0 || arr.Count == 0)
                    return list; // 用户把组件全移除了也要尊重(空数组)
            }
            return Defaults();
        }
        var defs = Defaults();
        if (page?.Blocks != null)
        {
            var notes = page.Blocks.FirstOrDefault(b => b != null && b.Id == "notes");
            if (notes != null && !notes.Hidden)
            {
                var n = new ZoneItem { Id = "note", Type = TNote, Size = Small };
                n.Settings["text"] = legacyNoteText ?? "";
                defs.Add(n);
            }
        }
        return defs;
    }

    /// <summary>把组件区写回布局页(只动 Extra["zone"],其它字段原样)。</summary>
    public static void Write(PageLayoutV2 page, IReadOnlyList<ZoneItem> items)
    {
        page.Extra ??= LayoutJson.Map();
        var arr = new List<object?>();
        foreach (var it in items)
            arr.Add(it.ToDict());
        var zone = LayoutJson.Map();
        zone["version"] = ZoneVersion;
        zone["items"] = arr;
        page.Extra[ExtraKey] = zone;
    }

    /// <summary>布局页里是否已经有组件区数据。</summary>
    public static bool HasZone(PageLayoutV2? page)
        => page?.Extra != null && page.Extra.ContainsKey(ExtraKey);

    /// <summary>给新组件起一个不重复的 id(type,重复时 type2 / type3 …)。</summary>
    public static string NewId(IEnumerable<ZoneItem> items, string type)
    {
        var used = new HashSet<string>(items.Select(i => i.Id), StringComparer.Ordinal);
        if (!used.Contains(type))
            return type;
        for (int n = 2; ; n++)
        {
            string id = type + n.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(id))
                return id;
        }
    }

    public static List<ZoneItem> CloneList(IEnumerable<ZoneItem> items) => items.Select(i => i.Clone()).ToList();

    /// <summary>JSON 子树 → 朴素值(导入布局码用;与布局文件读取同一套规则)。</summary>
    public static object? PlainFromJson(System.Text.Json.JsonElement e) => LayoutJson.Plain(e);

    /// <summary>两份组件列表是否相同。</summary>
    public static bool Same(IReadOnlyList<ZoneItem> a, IReadOnlyList<ZoneItem> b)
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i].Signature() != b[i].Signature())
                return false;
        return true;
    }
}

/// <summary>组件区的一个组件。</summary>
public sealed class ZoneItem
{
    public string Id { get; set; } = "";

    public string Type { get; set; } = "";

    /// <summary>s / m / l。</summary>
    public string Size { get; set; } = LaunchZone.Small;

    /// <summary>组件自己的设置(便签文字等);未知键原样保留。</summary>
    public Dictionary<string, object?> Settings { get; set; } = LayoutJson.Map();

    /// <summary>项层未知字段(保留)。</summary>
    public Dictionary<string, object?> Extra { get; set; } = LayoutJson.Map();

    public ZoneItem Clone() => new()
    {
        Id = Id,
        Type = Type,
        Size = Size,
        Settings = LayoutJson.CloneMap(Settings),
        Extra = LayoutJson.CloneMap(Extra),
    };

    public string Text(string key, string dflt = "")
        => Settings.TryGetValue(key, out var v) && v is string s ? s : dflt;

    public string Signature()
        => Id + "|" + Type + "|" + Size + "|" + string.Join(";", Settings.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "=" + Convert.ToString(k.Value, CultureInfo.InvariantCulture)));

    public Dictionary<string, object?> ToDict()
    {
        var d = LayoutJson.Map();
        d["id"] = Id;
        d["type"] = Type;
        d["size"] = Size;
        d["settings"] = LayoutJson.CloneMap(Settings);
        LayoutJson.MergeExtra(d, Extra);
        return d;
    }

    /// <summary>宽容解析:不是对象 / 缺 id / 类型未知 → null;尺寸不在该类型允许范围 → 取该类型默认尺寸。</summary>
    public static ZoneItem? FromPlain(object? o)
    {
        if (o is not Dictionary<string, object?> d)
            return null;
        string id = d.TryGetValue("id", out var idv) && idv is string ids ? ids.Trim() : "";
        string type = d.TryGetValue("type", out var tv) && tv is string ts ? ts.Trim() : "";
        if (id.Length == 0 || id.Length > 64 || !LaunchZone.IsKnownType(type))
            return null;
        string size = d.TryGetValue("size", out var sv) && sv is string ss ? ss : "";
        if (!LaunchZone.SizesOf(type).Contains(size))
            size = LaunchZone.DefaultSize(type);
        var it = new ZoneItem { Id = id, Type = type, Size = size };
        if (d.TryGetValue("settings", out var st) && st is Dictionary<string, object?> sm)
            it.Settings = LayoutJson.CloneMap(sm);
        foreach (var kv in d)
        {
            if (kv.Key is "id" or "type" or "size" or "settings")
                continue;
            it.Extra[kv.Key] = LayoutJson.ClonePlain(kv.Value);
        }
        return it;
    }
}

/// <summary>一个组件的落位(格子坐标)。</summary>
public sealed class ZonePlacement
{
    public string Id { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }

    /// <summary>实际排版尺寸(2 列时 l → m)。</summary>
    public string EffSize { get; set; } = LaunchZone.Small;
}

/// <summary>一次打包的结果。</summary>
public sealed class ZonePack
{
    public int Cols { get; set; }
    public int Rows { get; set; } = LaunchZone.Rows; { get; } = new(StringComparer.Ordinal);
    public List<string> Order { get; } = new();
    public List<string> Overflow { get; } = new();
}

/// <summary>编辑态的组件列表 + 撤销 / 重做(最多 50 步)。所有改动都经这里,界面只负责显示。</summary>
public sealed class ZoneEditor
{
    private readonly List<List<ZoneItem>> _undo = new();
    private readonly List<List<ZoneItem>> _redo = new();
    public const int MaxHistory = 50;

    public ZoneEditor(IEnumerable<ZoneItem> items)
    {
        Items = LaunchZone.CloneList(items);
        Saved = LaunchZone.CloneList(items);
    }

    public List<ZoneItem> Items { get; private set; }

    /// <summary>进入编辑态时(或上次「完成」时)的状态,「放弃这次改动」回到这里。</summary>
    public List<ZoneItem> Saved { get; private set; }

    public int Cols { get; set; } = LaunchZone.ColsWide;

    public int Rows { get; set; } = LaunchZone.Rows; => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public bool Dirty => !LaunchZone.Same(Items, Saved);

    public ZonePack Pack() => LaunchZone.Pack(Items, Cols, Rows);

    private void Push()
    {
        _undo.Add(LaunchZone.CloneList(Items));
        if (_undo.Count > MaxHistory)
            _undo.RemoveAt(0);
        _redo.Clear();
    }

    /// <summary>把整份列表换掉(拖动松手、重置、放弃改动、导入),记一步撤销。没变化就不记。</summary>
    public bool Replace(IEnumerable<ZoneItem> next)
    {
        var list = LaunchZone.CloneList(next);
        if (LaunchZone.Same(list, Items))
            return false;
        Push();
        Items = list;
        return true;
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
            return false;
        _redo.Add(LaunchZone.CloneList(Items));
        Items = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
            return false;
        _undo.Add(LaunchZone.CloneList(Items));
        Items = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        return true;
    }

    public bool Reset() => Replace(LaunchZone.Defaults());

    public bool Revert() => Replace(Saved);

    /// <summary>「完成」:当前状态成为新的已保存状态(撤销栈清空)。</summary>
    public void Commit()
    {
        Saved = LaunchZone.CloneList(Items);
        _undo.Clear();
        _redo.Clear();
    }

    /// <summary>移除组件。</summary>
    public bool Remove(string id)
    {
        if (!Items.Any(i => i.Id == id))
            return false;
        Push();
        Items = Items.Where(i => i.Id != id).Select(i => i.Clone()).ToList();
        return true;
    }

    /// <summary>切换尺寸的结果。</summary>
    public enum SizeResult { Changed, OnlyOne, NoRoom, Missing }

    /// <summary>
    /// 在允许尺寸里轮换到下一个「实际排版不同」的尺寸;换了之后放不下的组件变多就拒绝(NoRoom)。
    /// 2 列时 l 与 m 排版相同,所以只在 s / m 之间换。
    /// </summary>
    public SizeResult CycleSize(string id, out string next)
    {
        next = "";
        var it = Items.FirstOrDefault(i => i.Id == id);
        if (it == null)
            return SizeResult.Missing;
        var sizes = LaunchZone.SizesOf(it.Type);
        string cur = LaunchZone.EffSize(it.Size, Cols);
        int idx = Math.Max(0, sizes.ToList().IndexOf(it.Size));
        string cand = it.Size;
        for (int k = 1; k <= sizes.Count; k++)
        {
            string c = sizes[(idx + k) % sizes.Count];
            if (LaunchZone.EffSize(c, Cols) != cur)
            {
                cand = c;
                break;
            }
        }
        if (cand == it.Size)
            return SizeResult.OnlyOne;
        next = cand;
        var trial = LaunchZone.CloneList(Items);
        trial.First(i => i.Id == id).Size = cand;
        if (LaunchZone.Pack(trial, Cols, Rows).Overflow.Count > Pack().Overflow.Count)
            return SizeResult.NoRoom;
        Push();
        Items = trial;
        return SizeResult.Changed;
    }

    public enum AddResult { Added, AlreadyAdded, Full, Unknown }

    /// <summary>从组件库添加:非多张类型已存在 → AlreadyAdded;加进去放不下 → Full(不加)。</summary>
    public AddResult Add(string type, out ZoneItem? added, string? noteText = null)
    {
        added = null;
        if (!LaunchZone.IsKnownType(type))
            return AddResult.Unknown;
        if (!LaunchZone.IsMulti(type) && Items.Any(i => i.Type == type))
            return AddResult.AlreadyAdded;
        var it = new ZoneItem { Id = LaunchZone.NewId(Items, type), Type = type, Size = LaunchZone.DefaultSize(type) };
        if (type == LaunchZone.TNote)
            it.Settings["text"] = noteText ?? "";
        var trial = LaunchZone.CloneList(Items);
        trial.Add(it);
        if (LaunchZone.Pack(trial, Cols, Rows).Overflow.Contains(it.Id))
            return AddResult.Full;
        Push();
        Items = trial;
        added = it;
        return AddResult.Added;
    }

    /// <summary>改组件设置(便签文字)。常态下也能改,不进撤销栈,同时写进 Saved;返回是否有变化。</summary>
    public bool SetSetting(string id, string key, object? value)
    {
        var it = Items.FirstOrDefault(i => i.Id == id);
        if (it == null)
            return false;
        if (it.Settings.TryGetValue(key, out var old) && Equals(old, value))
            return false;
        it.Settings[key] = value;
        var s = Saved.FirstOrDefault(i => i.Id == id);
        if (s != null)
            s.Settings[key] = value;
        return true;
    }
}
