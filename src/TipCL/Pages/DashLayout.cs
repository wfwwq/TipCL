using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TipCL.Pages;

public sealed class DashLayout
{
	public const int Version = 1;

	public static readonly int[] GridChoices = new int[5] { 0, 4, 8, 16, 24 };

	public int Grid { get; set; } = 8;

	public List<DashCard> Items { get; set; } = new List<DashCard>();

	public bool Snap => Grid > 0;

	public DashLayout Clone()
	{
		return new DashLayout
		{
			Grid = Grid,
			Items = Items.Select((DashCard c) => c.Clone()).ToList()
		};
	}

	public IEnumerable<DashCard> VisibleItems()
	{
		return from c in Items
			where !c.Hidden
			orderby c.Z
			select c;
	}

	public int NextZ()
	{
		if (Items.Count != 0)
		{
			return Items.Max((DashCard c) => c.Z) + 1;
		}
		return 0;
	}

	public void Normalize()
	{
		HashSet<string> hashSet = new HashSet<string>();
		for (int i = 0; i < Items.Count; i++)
		{
			DashCard dashCard = Items[i];
			string id = dashCard.Id;
			string text = ((id != null && id.Length > 0) ? dashCard.Id : $"{dashCard.Type}-{i}");
			string text2 = text;
			int num = 2;
			while (!hashSet.Add(text2))
			{
				text2 = $"{text}-{num++}";
			}
			dashCard.Id = text2;
		}
		int num2 = 0;
		foreach (DashCard item in Items.OrderBy((DashCard c) => c.Z).ToList())
		{
			item.Z = num2++;
		}
	}

	public Dictionary<string, object?> ToDict()
	{
		Normalize();
		return new Dictionary<string, object>
		{
			["version"] = 1,
			["grid"] = Grid,
			["items"] = Items.Select((DashCard c) => c.ToDict()).ToList()
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
		{
			return Default();
		}
		int val = 8;
		if (e.TryGetProperty("grid", out var value))
		{
			int result;
			if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var value2))
			{
				val = value2;
			}
			else if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out result))
			{
				val = result;
			}
		}
		DashLayout dashLayout = new DashLayout
		{
			Grid = Math.Max(0, val)
		};
		JsonElement value3;
		bool flag = e.TryGetProperty("items", out value3) && value3.ValueKind == JsonValueKind.Array;
		if (flag)
		{
			foreach (JsonElement item in value3.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.Object)
				{
					dashLayout.Items.Add(DashCard.FromJson(item));
				}
			}
		}
		if (dashLayout.Items.Count == 0 && !flag)
		{
			return Default();
		}
		dashLayout.Normalize();
		return dashLayout;
	}

	public static DashLayout? Parse(JsonElement e, IReadOnlyCollection<string> knownTypes)
	{
		if (e.ValueKind != JsonValueKind.Object)
		{
			return null;
		}
		if (!e.TryGetProperty("items", out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
		{
			return null;
		}
		DashLayout dashLayout = FromJson(e);
		dashLayout.Items = dashLayout.Items.Where((DashCard c) => knownTypes.Contains<string>(c.Type)).ToList();
		if (dashLayout.Items.Count <= 0)
		{
			return null;
		}
		return dashLayout;
	}

	// 必须与 UiLayout.DefaultDoc() 完全一致（LayoutDefaultTests 会检查）
	public static DashLayout Default()
	{
		return new DashLayout
		{
			Grid = 8,
			Items = new List<DashCard>
			{
				new DashCard { Id = "banner-main", Type = "banner", X = 0.0, Y = 0.0, W = 1.0, H = 0.26, Z = 0 },
				new DashCard { Id = "config-main", Type = "config", X = 0.0, Y = 0.28, W = 0.42, H = 0.72, Z = 1 },
				new DashCard { Id = "quick-main", Type = "quick", X = 0.435, Y = 0.28, W = 0.565, H = 0.3, Z = 2 },
				new DashCard { Id = "news-main", Type = "news", X = 0.435, Y = 0.595, W = 0.565, H = 0.405, Z = 3 }
			}
		};
	}
}
