using System;
using System.Collections.Generic;
using System.IO;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;

namespace TipCL.Pages.Layout;

// Pages 侧给 Core 的 v1 注入缝实现(= 重构前 LayoutStoreV2 里那段写通逻辑,一字不改语义):
//   Dir()      = PageLayoutStore.DirPath(BridgeHost.FindRoot() 那套回落);测试注入目录时把
//                PageLayoutStore.DirOverride 同步成 LayoutStoreV2.DirOverride(旧的 SyncV1DirOverride 行为)
//   LoadV1()   = PageLayoutStore.LoadAll()(走它的进程内缓存,与重构前完全一致)
//   SaveV1()   = 原子写 v1 文件(Utils.WriteJson)+ PageLayoutStore.Reload() 让老路径立刻看到新值
// 装配点:App.OnStartup → LayoutStoreV2.V1Bridge = new PagesLayoutV1Bridge();
public sealed class PagesLayoutV1Bridge : ILayoutV1Bridge
{
	/// <summary>数据目录:与重构前的 LayoutStoreV2.DirPath 一致(DirOverride 优先,否则 PageLayoutStore.DirPath)。</summary>
	public string? Dir()
	{
		string? over = LayoutStoreV2.DirOverride;
		if (!string.IsNullOrEmpty(over))
		{
			// 测试注入目录时两个存储必须指同一目录;否则写通会落到用户真实数据目录(危险)
			PageLayoutStore.DirOverride = over;
			return over;
		}
		return PageLayoutStore.DirPath;
	}

	/// <summary>读 v1 投影:走 PageLayoutStore 的缓存(与重构前 LayoutStoreV2.SafeLoadAllV1 一致)。</summary>
	public IReadOnlyDictionary<string, IReadOnlyList<LayoutV1Block>>? LoadV1(string dir)
	{
		try
		{
			Dictionary<string, IReadOnlyList<LayoutV1Block>> pages = new Dictionary<string, IReadOnlyList<LayoutV1Block>>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, PageLayoutDoc> kv in PageLayoutStore.LoadAll())
			{
				if (string.IsNullOrEmpty(kv.Key) || kv.Value == null)
				{
					continue;
				}
				List<LayoutV1Block> blocks = new List<LayoutV1Block>();
				if (kv.Value.Blocks != null)
				{
					foreach (BlockState block in kv.Value.Blocks)
					{
						if (block != null && !string.IsNullOrEmpty(block.Id))
						{
							blocks.Add(new LayoutV1Block
							{
								Id = block.Id,
								Hidden = block.Hidden
							});
						}
					}
				}
				pages[kv.Key] = blocks;
			}
			return pages;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>写 v1 投影:原子写(**不动 PageLayoutStore 的缓存** —— 与重构前的 LayoutStoreV2.Save 完全一致)。</summary>
	public void SaveV1(string dir, IReadOnlyDictionary<string, IReadOnlyList<LayoutV1Block>> pages)
	{
		Utils.WriteJson(Path.Combine(dir, LayoutStoreV2.V1FileName), LayoutStoreV2.V1Envelope(pages));
	}

	/// <summary>让老 v1 路径立刻看到刚写通的投影(清 PageLayoutStore 缓存);由 AI 改布局等明确时点触发。</summary>
	public void RefreshV1Cache()
	{
		PageLayoutStore.Reload();
	}
}

/// <summary>
/// v2 → v1(<c>PageLayoutDoc</c>)的投影适配:Core 的 <see cref="LayoutStoreV2.ProjectV1"/> 返回的是
/// Core 自己的朴素结构,v1 的冻结类型在 Pages 里,适配放在这边(Pages → Core 方向的唯一桥梁)。
/// 导出兼容 v1 的页面布局、以及兼容性测试都用它。
/// </summary>
public static class LayoutV1Projection
{
	/// <summary>把整份 v2 文档投影成 v1 的逐页文档(只含 {id,hidden},顺序即顺序)。</summary>
	public static Dictionary<string, PageLayoutDoc> ProjectV1(LayoutV2? doc)
	{
		Dictionary<string, PageLayoutDoc> pages = new Dictionary<string, PageLayoutDoc>(StringComparer.Ordinal);
		foreach (KeyValuePair<string, IReadOnlyList<LayoutV1Block>> kv in LayoutStoreV2.ProjectV1(doc))
		{
			PageLayoutDoc v1 = new PageLayoutDoc();
			foreach (LayoutV1Block block in kv.Value)
			{
				if (block != null && !string.IsNullOrEmpty(block.Id))
				{
					v1.Blocks.Add(new BlockState
					{
						Id = block.Id,
						Hidden = block.Hidden
					});
				}
			}
			pages[kv.Key] = v1;
		}
		return pages;
	}

	/// <summary>把单页的 v1 文档转成 Core 的朴素块列表(顺序即顺序)。</summary>
	public static List<LayoutV1Block> ToCoreBlocks(PageLayoutDoc? doc)
	{
		List<LayoutV1Block> blocks = new List<LayoutV1Block>();
		if (doc == null || doc.Blocks == null)
		{
			return blocks;
		}
		foreach (BlockState block in doc.Blocks)
		{
			if (block != null && !string.IsNullOrEmpty(block.Id))
			{
				blocks.Add(new LayoutV1Block
				{
					Id = block.Id,
					Hidden = block.Hidden
				});
			}
		}
		return blocks;
	}

	/// <summary>把 v1 的逐页文档转成 Core 的朴素投影(AI 工具与合并逻辑用)。</summary>
	public static Dictionary<string, IReadOnlyList<LayoutV1Block>> ToCore(
		IEnumerable<KeyValuePair<string, PageLayoutDoc>>? pages)
	{
		Dictionary<string, IReadOnlyList<LayoutV1Block>> result = new Dictionary<string, IReadOnlyList<LayoutV1Block>>(StringComparer.Ordinal);
		if (pages == null)
		{
			return result;
		}
		foreach (KeyValuePair<string, PageLayoutDoc> kv in pages)
		{
			if (string.IsNullOrEmpty(kv.Key) || kv.Value == null)
			{
				continue;
			}
			List<LayoutV1Block> blocks = new List<LayoutV1Block>();
			if (kv.Value.Blocks != null)
			{
				foreach (BlockState block in kv.Value.Blocks)
				{
					if (block != null && !string.IsNullOrEmpty(block.Id))
					{
						blocks.Add(new LayoutV1Block
						{
							Id = block.Id,
							Hidden = block.Hidden
						});
					}
				}
			}
			result[kv.Key] = blocks;
		}
		return result;
	}
}
