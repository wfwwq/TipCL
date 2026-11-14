using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TipCL.App.Pages.Library;

// 旧卡片画布布局文档的数据模型（搬运自 Windows 版 src/TipCL/Pages/DashLayout.cs + DashCard.cs）。
// 可视树已不挂（Windows 版 LaunchPage 注释明示），M2 只搬数据类供 PageLayoutStore/BlockHost 兼容旧布局文档；
// 画布拖拽 UI 不移植。

/// <summary>单张卡片在画布文档里的状态（搬运自 DashCard.cs）。</summary>
public sealed class DashCard
{
    public string Id { get; set; } = "";

    public string Type { get; set; } = "";

    public double X { get; set; }

    public double Y { get; set; }

    public double W { get; set; } = 0.3;

    public double H { get; set; } = 0.3;

    public int Z { get; set; }

    public bool Hidden { get; set; }

    public JsonElement? Settings { get; set; }

    public DashCard Clone()
    {
        return new DashCard
        {
            Id = Id,
            Type = Type,
            X = X,
            Y = Y,
            W = W,
            H = H,
            Z = Z,
            Hidden = Hidden,
            Settings = Settings?.Clone()
        };
    }

    public static DashCard FromJson(JsonElement e)
    {
        var obj = new DashCard
        {
            Id = Str(e, "id"),
            Type = Str(e, "type"),
            X = Clamp01(Num(e, "x", 0.0)),
            Y = Clamp01(Num(e, "y", 0.0)),
            W = ClampOf(Num(e, "w", 0.3), 0.04, 1.0),
            H = ClampOf(Num(e, "h", 0.3), 0.04, 1.0),
            Z = (int)Num(e, "z", 0.0),
            Hidden = e.TryGetProperty("hidden", out var hEl) && hEl.ValueKind == JsonValueKind.True,
        };
        if (obj.Type.Length == 0) obj.Type = "notes";
        if (e.TryGetProperty("settings", out var sEl) && sEl.ValueKind == JsonValueKind.Object)
            obj.Settings = sEl.Clone();
        return obj;
    }

    public Dictionary<string, object?> ToDict()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = Id,
            ["type"] = Type,
            ["x"] = Math.Round(X, 5),
            ["y"] = Math.Round(Y, 5),
            ["w"] = Math.Round(W, 5),
            ["h"] = Math.Round(H, 5),
            ["z"] = Z,
            ["hidden"] = Hidden,
            ["settings"] = (object?)(Settings.HasValue ? Settings.GetValueOrDefault() : new Dictionary<string, object?>()),
        };
    }

    private static string Str(JsonElement e, string k)
        => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double Num(JsonElement e, string k, double fallback)
    {
        if (!e.TryGetProperty(k, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), out var p)) return p;
        return fallback;
    }

    private static double ClampOf(double v, double min, double max) => Math.Clamp(v, min, max);

    private static double Clamp01(double v) => Math.Clamp(v, 0.0, 1.0);
}

/// <summary>
/// 旧卡片画布布局文档（搬运自 DashLayout.cs，只保留数据模型；Grid 吸附 0/4/8/16/24）。
/// </summary>
public sealed class DashLayout
{
    public const int Version = 1;

    public static readonly int[] GridChoices = { 0, 4, 8, 16, 24 };

    public int Grid { get; set; } = 8;

    public List<DashCard> Items { get; set; } = new();

    public bool Snap => Grid > 0;

    public DashLayout Clone()
    {
        return new DashLayout
        {
            Grid = Grid,
            Items = Items.Select(c => c.Clone()).ToList()
        };
    }

    public IEnumerable<DashCard> VisibleItems()
    {
        return from c in Items
               where !c.Hidden
               orderby c.Z
               select c;
    }

    public int NextZ() => Items.Count == 0 ? 0 : Items.Max(c => c.Z) + 1;

    public void Normalize()
    {
        var used = new HashSet<string>();
        for (int i = 0; i < Items.Count; i++)
        {
            var card = Items[i];
            string text = card.Id.Length > 0 ? card.Id : $"{card.Type}-{i}";
            string candidate = text;
            int n = 2;
            while (!used.Add(candidate))
                candidate = $"{text}-{n++}";
            card.Id = candidate;
        }
        int z = 0;
        foreach (var item in Items.OrderBy(c => c.Z).ToList())
            item.Z = z++;
    }

    public Dictionary<string, object?> ToDict()
    {
        Normalize();
        return new Dictionary<string, object?>
        {
            ["version"] = 1,
            ["grid"] = Grid,
            ["items"] = Items.Select(c => c.ToDict()).ToList(),
        };
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(ToDict(), new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    public static DashLayout FromJson(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
            return Default();
        int grid = 8;
        if (e.TryGetProperty("grid", out var gEl))
        {
            if (gEl.ValueKind == JsonValueKind.Number && gEl.TryGetInt32(out var gi))
                grid = gi;
            else if (gEl.ValueKind == JsonValueKind.String && int.TryParse(gEl.GetString(), out var gs))
                grid = gs;
        }
        var layout = new DashLayout { Grid = Math.Max(0, grid) };
        bool hasItems = e.TryGetProperty("items", out var iEl) && iEl.ValueKind == JsonValueKind.Array;
        if (hasItems)
        {
            foreach (var item in iEl.EnumerateArray())
                if (item.ValueKind == JsonValueKind.Object)
                    layout.Items.Add(DashCard.FromJson(item));
        }
        if (layout.Items.Count == 0 && !hasItems)
            return Default();
        layout.Normalize();
        return layout;
    }

    public static DashLayout? Parse(JsonElement e, IReadOnlyCollection<string> knownTypes)
    {
        if (e.ValueKind != JsonValueKind.Object)
            return null;
        if (!e.TryGetProperty("items", out var iEl) || iEl.ValueKind != JsonValueKind.Array || iEl.GetArrayLength() == 0)
            return null;
        var layout = FromJson(e);
        layout.Items = layout.Items.Where(c => knownTypes.Contains(c.Type)).ToList();
        return layout.Items.Count > 0 ? layout : null;
    }

    // 必须与 Windows 版 UiLayout.DefaultDoc() 一致：渐变横幅卡移出画布（固定启动头），默认 3 卡
    public static DashLayout Default()
    {
        return new DashLayout
        {
            Grid = 8,
            Items = new List<DashCard>
            {
                new DashCard { Id = "config-main", Type = "config", X = 0.0, Y = 0.0, W = 0.42, H = 1.0, Z = 0 },
                new DashCard { Id = "quick-main", Type = "quick", X = 0.435, Y = 0.0, W = 0.565, H = 0.52, Z = 1 },
                new DashCard { Id = "news-main", Type = "news", X = 0.435, Y = 0.53, W = 0.565, H = 0.47, Z = 2 },
            }
        };
    }
}
