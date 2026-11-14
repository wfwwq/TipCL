using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TipCL.Core.Launcher.Layout;

// v1 投影(<数据目录>/ui_page_layouts.json)的**注入缝**与 Core 自带的读写实现。
// 为什么需要它:LayoutStoreV2 要把 v2 单向写通 v1,而 v1 的类型(PageLayoutDoc/PageLayoutStore)在 Pages 里 ——
// Core 不能反向依赖 Pages(否则 mac/linux 移植把 Core/** 复制进独立 TipCL.Core 会编不过,DECISIONS D-32)。
// 所以:Core 只认下面这套朴素结构(id + hidden),由 Pages 侧启动时装配 ILayoutV1Bridge 提供真实实现;
// 没装配时用本文件的文件读写(单测走这条,断言的是"磁盘上的 v1 文件",与装配后的行为一致)。

/// <summary>v1 投影里的一行:<c>{id, hidden}</c>(与冻结的 v1 同义)。</summary>
public sealed class LayoutV1Block
{
	/// <summary>块 id(非空)。</summary>
	public string Id { get; set; } = "";

	/// <summary>是否隐藏。</summary>
	public bool Hidden { get; set; }
}

/// <summary>
/// v1 投影的注入缝(三个能力:取目录 / 读 v1 / 写 v1)。Pages 侧实现见
/// <c>Pages/Layout/LayoutV1BridgePages.cs</c>,由 <c>App.OnStartup</c> 装配到
/// <see cref="LayoutStoreV2.V1Bridge"/>;传进来的 <c>dir</c> 一定是 LayoutStoreV2 解析好的数据目录,
/// 实现方直接用,不要自己再算一遍(否则测试注入目录时会分叉)。
/// </summary>
public interface ILayoutV1Bridge
{
	/// <summary>取数据目录;返回 null = 让 LayoutStoreV2 用自己的兜底(Utils 根目录 → 程序目录)。</summary>
	string? Dir();

	/// <summary>读 v1 投影(整份);读不到返回 null 或空字典,不要抛。</summary>
	IReadOnlyDictionary<string, IReadOnlyList<LayoutV1Block>>? LoadV1(string dir);

	/// <summary>整份写 v1 投影(原子写);失败自己吞掉或抛给 LayoutStoreV2(它会吞),不要留下半截文件。</summary>
	void SaveV1(string dir, IReadOnlyDictionary<string, IReadOnlyList<LayoutV1Block>> pages);

	/// <summary>
	/// 可选能力:让"仍走 v1 的读取"立刻看到刚写通的结果(实现方通常清自己的缓存)。
	/// **刻意不放进 SaveV1**:重构前的 LayoutStoreV2.Save 从不碰 PageLayoutStore 的缓存,
	/// 缓存刷新只发生在明确的时机(AI 改布局、编辑器提交),所以它单独一个方法、由调用方显式触发。
	/// 默认实现可以是空操作。
	/// </summary>
	void RefreshV1Cache();
}

/// <summary>
/// Core 自带的 v1 文件读写:严格照冻结契约解析/生成
/// <c>{"version":1,"kind":"tipcl-page-layout","pages":{pageId:{"version":1,"blocks":[{"id":..,"hidden":..}]}}}</c>。
/// 宽容读取:类型不符就跳过该项,绝不抛(与 Pages 的 PageLayoutStore 同思路)。
/// </summary>
public static class LayoutV1Json
{
	/// <summary>v1 信封版本(冻结值)。</summary>
	public const int Version = 1;

	/// <summary>单页版本(冻结值)。</summary>
	public const int PageVersion = 1;

	/// <summary>v1 信封 kind(冻结值)。</summary>
	public const string Kind = "tipcl-page-layout";

	/// <summary>读 v1 文件;文件不存在/损坏/结构不对都返回 null(由调用方回落默认)。</summary>
	public static IReadOnlyDictionary<string, IReadOnlyList<LayoutV1Block>>? ReadPages(string path)
	{
		try
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
			{
				return null;
			}
			return ParsePages(File.ReadAllText(path));
		}
		catch
		{
			return null;
		}
	}

	/// <summary>解析 v1 文本;不是合法 v1 信封返回 null。</summary>
	public static IReadOnlyDictionary<string, IReadOnlyList<LayoutV1Block>>? ParsePages(string json)
	{
		try
		{
			using JsonDocument document = JsonDocument.Parse(json ?? "");
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				return null;
			}
			if (!root.TryGetProperty("kind", out JsonElement kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != Kind)
			{
				return null;
			}
			if (!root.TryGetProperty("version", out JsonElement version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int v) || v != Version)
			{
				return null;
			}
			Dictionary<string, IReadOnlyList<LayoutV1Block>> pages = new Dictionary<string, IReadOnlyList<LayoutV1Block>>(StringComparer.Ordinal);
			if (!root.TryGetProperty("pages", out JsonElement pageMap) || pageMap.ValueKind != JsonValueKind.Object)
			{
				return pages;
			}
			foreach (JsonProperty page in pageMap.EnumerateObject())
			{
				if (string.IsNullOrEmpty(page.Name) || page.Value.ValueKind != JsonValueKind.Object)
				{
					continue;
				}
				if (!page.Value.TryGetProperty("version", out JsonElement pageVersion) || pageVersion.ValueKind != JsonValueKind.Number || !pageVersion.TryGetInt32(out int pv) || pv != PageVersion)
				{
					continue;
				}
				List<LayoutV1Block> blocks = new List<LayoutV1Block>();
				HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
				if (page.Value.TryGetProperty("blocks", out JsonElement blockArray) && blockArray.ValueKind == JsonValueKind.Array)
				{
					foreach (JsonElement item in blockArray.EnumerateArray())
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
						blocks.Add(new LayoutV1Block
						{
							Id = id,
							Hidden = item.TryGetProperty("hidden", out JsonElement hidden) && hidden.ValueKind == JsonValueKind.True
						});
					}
				}
				pages[page.Name] = blocks;
			}
			return pages;
		}
		catch
		{
			return null;
		}
	}
}
