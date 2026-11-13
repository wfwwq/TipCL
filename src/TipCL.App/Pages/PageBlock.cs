using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TipCL.Pages;

// C3a:Windows 版 Pages\PageBlock.cs 的移植(纯模型层,只有 System.Text.Json,不碰任何界面类型)。
// 逐字照抄 Windows 源的语义与成员名:BlockState / PageLayoutDoc(CurrentVersion / Default /
// Normalize / FromJson / ToDict / ToJson)。**这个文件与 Windows 源逐行等价,没有移植差异**
// (System.Text.Json 在 net8 上是 8.0.0.0,与 Windows 主工程 net48 的 8.0.0.5 行为一致;
// 唯一没搬的是 Windows 测试注释里提到的 CS1705 那个 net48 专属理由,见 Ported\PageLayoutTests.cs)。

// 单个区块的用户状态:Id 是区块的稳定英文 id;Hidden = 用户手动隐藏。
// Blocks 数组的顺序即显示顺序(隐藏项也占位,恢复时回到原位置)。
public sealed class BlockState
{
	public string Id { get; set; } = "";

	public bool Hidden { get; set; }
}

// 单页布局文档,对应 JSON:{"version":1,"blocks":[{"id":"..","hidden":false},..]}
// 旧配置缺失 / 损坏 = 全默认布局,调用方拿到 null 自己走 Default,不报错。
public sealed class PageLayoutDoc
{
	public const int CurrentVersion = 1;

	public int Version { get; set; } = CurrentVersion;

	public List<BlockState> Blocks { get; set; } = new List<BlockState>();

	// 默认文档:ids 全部按给出顺序登记,出现在 defaultVisible 里的可见、其余隐藏
	public static PageLayoutDoc Default(IEnumerable<string> ids, IEnumerable<string> defaultVisible)
	{
		HashSet<string> vis = new HashSet<string>(defaultVisible ?? Array.Empty<string>(), StringComparer.Ordinal);
		PageLayoutDoc doc = new PageLayoutDoc();
		foreach (string id in ids ?? Array.Empty<string>())
		{
			if (!string.IsNullOrEmpty(id))
			{
				doc.Blocks.Add(new BlockState
				{
					Id = id,
					Hidden = !vis.Contains(id)
				});
			}
		}
		return doc;
	}

	// 对齐页面当前区块集合:去掉 knownIds 之外的项;
	// 按 defaultVisible 补齐缺失项(Hidden = 不在 defaultVisible 中)到末尾。
	// 已有项保持用户选择(顺序与 Hidden 均不动)。
	public void Normalize(IReadOnlyCollection<string> knownIds, IReadOnlyCollection<string> defaultVisible)
	{
		HashSet<string> known = new HashSet<string>(knownIds ?? Array.Empty<string>(), StringComparer.Ordinal);
		HashSet<string> vis = new HashSet<string>(defaultVisible ?? Array.Empty<string>(), StringComparer.Ordinal);
		Blocks.RemoveAll((BlockState b) => b == null || string.IsNullOrEmpty(b.Id) || !known.Contains(b.Id));
		HashSet<string> have = new HashSet<string>(Blocks.Select((BlockState b) => b.Id), StringComparer.Ordinal);
		foreach (string id in knownIds ?? Array.Empty<string>())
		{
			if (!string.IsNullOrEmpty(id) && !have.Contains(id))
			{
				Blocks.Add(new BlockState
				{
					Id = id,
					Hidden = !vis.Contains(id)
				});
			}
		}
	}

	// 解析失败 / Version != CurrentVersion / 根不是对象 → null。
	// knownIds 过滤未知块;缺失块按 defaultVisible 补齐(交给 Normalize)。
	public static PageLayoutDoc? FromJson(JsonElement e, IReadOnlyCollection<string> knownIds, IReadOnlyCollection<string> defaultVisible)
	{
		if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("version", out JsonElement v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int ver) || ver != CurrentVersion)
		{
			return null;
		}
		PageLayoutDoc doc = new PageLayoutDoc();
		if (e.TryGetProperty("blocks", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
		{
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (JsonElement item in arr.EnumerateArray())
			{
				if (item.ValueKind != JsonValueKind.Object)
				{
					continue;
				}
				string id = ((item.TryGetProperty("id", out JsonElement idEl) && idEl.ValueKind == JsonValueKind.String) ? idEl.GetString() : null) ?? "";
				if (id.Length == 0 || !seen.Add(id))
				{
					continue;
				}
				// hidden 只认 JSON 布尔 true(对齐 DashCard.FromJson 的写法),其余一律可见
				bool hidden = item.TryGetProperty("hidden", out JsonElement hEl) && hEl.ValueKind == JsonValueKind.True;
				doc.Blocks.Add(new BlockState
				{
					Id = id,
					Hidden = hidden
				});
			}
		}
		doc.Normalize(knownIds, defaultVisible);
		return doc;
	}

	public Dictionary<string, object?> ToDict()
	{
		return new Dictionary<string, object?>
		{
			["version"] = Version,
			["blocks"] = Blocks.Select((BlockState b) => (object)new Dictionary<string, object?>
			{
				["id"] = b.Id,
				["hidden"] = b.Hidden
			}).ToList()
		};
	}

	// 写法对齐 DashLayout.ToJson(WriteIndented + UnsafeRelaxedJsonEscaping,中文不转义)
	public string ToJson()
	{
		return JsonSerializer.Serialize(ToDict(), new JsonSerializerOptions
		{
			WriteIndented = true,
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
		});
	}
}
