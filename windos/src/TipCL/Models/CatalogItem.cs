using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TipCL.Models;

public sealed class CatalogItem
{
	public string Name { get; set; } = "";

	public string Author { get; set; } = "";

	public long Downloads { get; set; }

	public JsonElement? Id { get; set; }

	public string? Slug { get; set; }

	public string Source { get; set; } = "";

	public string Description { get; set; } = "";

	public List<string>? Tags { get; set; }

	public string Updated { get; set; } = "";

	[JsonPropertyName("game_version")]
	public string GameVersion { get; set; } = "";

	[JsonPropertyName("icon_url")]
	public string IconUrl { get; set; } = "";

	[JsonPropertyName("project_url")]
	public string ProjectUrl { get; set; } = "";

	[JsonIgnore]
	public object? IdValue
	{
		get
		{
			JsonElement? id = Id;
			if (id.HasValue)
			{
				JsonElement valueOrDefault = id.GetValueOrDefault();
				switch (valueOrDefault.ValueKind)
				{
				case JsonValueKind.Number:
				{
					if (!valueOrDefault.TryGetInt64(out var value))
					{
						return valueOrDefault.GetDouble();
					}
					return value;
				}
				case JsonValueKind.String:
					return valueOrDefault.GetString();
				case JsonValueKind.Undefined:
				case JsonValueKind.Null:
					return null;
				default:
					return valueOrDefault.ToString();
				}
			}
			return null;
		}
	}

	[JsonIgnore]
	public string Key => Source + ":" + (Slug ?? IdValue?.ToString() ?? Name);
}
