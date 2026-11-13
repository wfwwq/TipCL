using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TipCL;

/// <summary>
/// 导航配置(存后端 settings 里的 <c>ui_nav_*</c> 键)。照搬 Windows 版 <c>NavConfig.cs</c>:
/// <c>Style</c> / <c>Order</c> / <c>Pinned</c> / <c>Hidden</c> / <c>Groups</c> / <c>SectionMembers</c> /
/// <c>SidebarWidth</c> 七个成员、<c>FromJsonString</c> / <c>FromJson</c> / <c>StrList</c> /
/// <c>Clone</c> / <c>With</c> 五个方法,**名字、键名、默认值、容错行为全部一致**。
///
/// 两处 mac 侧说明:
/// 1. <c>FromJsonString</c> 在 Windows 侧存在的理由是「测试宿主与 net48 的 STJ 程序集版本差异」,
///    mac 只有一个 TFM,但它是 <c>Ported\NavCompatTests</c> 的入口,所以照样保留、照样按 JSON 文本解析;
/// 2. 类型错(比如 <c>ui_nav_groups</c> 写成字符串)一律**当没有**,不抛异常 —— 与 Windows 同一条容错线。
/// </summary>
public sealed class NavConfig
{
	public string? Style { get; set; }

	public List<string> Order { get; set; } = new List<string>();

	public List<string> Pinned { get; set; } = new List<string>();

	public List<string> Hidden { get; set; } = new List<string>();

	public List<NavGroup>? Groups { get; set; }

	public Dictionary<string, List<string>>? SectionMembers { get; set; }

	public int SidebarWidth { get; set; }

	// 测试与脚本便捷入口:从 JSON 文本解析(避免 net8 测试宿主与 net48 的 STJ 程序集版本差异触发 CS1705)
	public static NavConfig FromJsonString(string json)
	{
		using JsonDocument doc = JsonDocument.Parse(json);
		return FromJson(doc.RootElement.Clone());
	}

	public static NavConfig FromJson(JsonElement settings)
	{
		NavConfig navConfig = new NavConfig();
		if (settings.ValueKind != JsonValueKind.Object)
		{
			return navConfig;
		}
		if (settings.TryGetProperty("ui_nav_style", out JsonElement style) && style.ValueKind == JsonValueKind.String)
		{
			navConfig.Style = style.GetString();
		}
		navConfig.Order = StrList(Prop(settings, "ui_nav_order"));
		navConfig.Pinned = StrList(Prop(settings, "ui_nav_pinned"));
		navConfig.Hidden = StrList(Prop(settings, "ui_nav_hidden"));
		if (settings.TryGetProperty("ui_nav_groups", out JsonElement groups) && groups.ValueKind == JsonValueKind.Array)
		{
			navConfig.Groups = new List<NavGroup>();
			foreach (JsonElement item in groups.EnumerateArray())
			{
				if (item.ValueKind != JsonValueKind.Object)
				{
					continue;
				}
				string title = ((item.TryGetProperty("title", out JsonElement t) && t.ValueKind == JsonValueKind.String) ? (t.GetString() ?? "").Trim() : "");
				if (title.Length != 0)
				{
					navConfig.Groups.Add(new NavGroup
					{
						Title = title,
						Keys = StrList(Prop(item, "keys"))
					});
				}
			}
		}
		if (settings.TryGetProperty("ui_section_members", out JsonElement sections) && sections.ValueKind == JsonValueKind.Object)
		{
			navConfig.SectionMembers = new Dictionary<string, List<string>>();
			foreach (string sectionId in NavModel.SectionIds)
			{
				navConfig.SectionMembers[sectionId] = StrList(Prop(sections, sectionId));
			}
		}
		if (settings.TryGetProperty("ui_sidebar_width", out JsonElement width))
		{
			if (width.ValueKind == JsonValueKind.Number && width.TryGetInt32(out int number))
			{
				navConfig.SidebarWidth = number;
			}
			else if (width.ValueKind == JsonValueKind.String && int.TryParse(width.GetString(), out int parsed))
			{
				navConfig.SidebarWidth = parsed;
			}
		}
		return navConfig;
	}

	private static JsonElement? Prop(JsonElement e, string name)
	{
		if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out JsonElement value))
		{
			return null;
		}
		return value;
	}

	/// <summary>字符串数组字段:非数组 / 元素不是字符串 / 空串一律丢掉(与 Windows 一字不差)。</summary>
	public static List<string> StrList(JsonElement? raw)
	{
		List<string> list = new List<string>();
		if (!raw.HasValue)
		{
			return list;
		}
		JsonElement value = raw.GetValueOrDefault();
		if (value.ValueKind != JsonValueKind.Array)
		{
			return list;
		}
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (item.ValueKind == JsonValueKind.String)
			{
				string? text = item.GetString();
				if (text != null && text.Length > 0)
				{
					list.Add(text);
				}
			}
		}
		return list;
	}

	public NavConfig Clone()
	{
		return new NavConfig
		{
			Style = Style,
			Order = new List<string>(Order),
			Pinned = new List<string>(Pinned),
			Hidden = new List<string>(Hidden),
			Groups = Groups?.Select((NavGroup g) => g.Clone()).ToList(),
			SectionMembers = SectionMembers?.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value)),
			SidebarWidth = SidebarWidth
		};
	}

	/// <summary>按 settings 补丁生成新配置(只认六个 <c>ui_nav_*</c> / <c>ui_section_members</c> 键)。</summary>
	public NavConfig With(IReadOnlyDictionary<string, object?> patch)
	{
		NavConfig navConfig = Clone();
		foreach ((string key, object? value) in patch)
		{
			switch (key)
			{
			case "ui_nav_style":
				navConfig.Style = value as string;
				break;
			case "ui_nav_order":
				navConfig.Order = AsList(value);
				break;
			case "ui_nav_pinned":
				navConfig.Pinned = AsList(value);
				break;
			case "ui_nav_hidden":
				navConfig.Hidden = AsList(value);
				break;
			case "ui_nav_groups":
				navConfig.Groups = ((value is IEnumerable<NavGroup> groups) ? groups.Select((NavGroup g) => g.Clone()).ToList() : null);
				break;
			case "ui_section_members":
				navConfig.SectionMembers = ((value is IReadOnlyDictionary<string, List<string>> members)
					? members.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value))
					: null);
				break;
			case "ui_sidebar_width":
				navConfig.SidebarWidth = ((value is int number) ? number : 0);
				break;
			}
		}
		return navConfig;
	}

	private static List<string> AsList(object? value)
	{
		if (value is not IEnumerable<string> source)
		{
			return new List<string>();
		}
		return source.Where(s => !string.IsNullOrEmpty(s)).ToList();
	}
}
