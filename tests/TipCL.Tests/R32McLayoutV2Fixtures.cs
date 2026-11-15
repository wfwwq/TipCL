using System;
using System.Collections.Generic;
using System.IO;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;
using TipCL.Pages;

namespace TipCL.Tests;

/// <summary>
/// R32-MC 的兼容性回归夹具(Windows 侧对等物 = <c>windos\src\TipCL\Pages\Layout\LayoutV1BridgePages.cs</c>
/// 的 <c>PagesLayoutV1Bridge</c> + <c>LayoutV1Projection</c>;分支 layout-shell-hooks @ fa1b3d7
/// + fix/r31-ui-audit @ 33b5fff)。
///
/// 本包只搬 Core 的 5 个纯模型层文件,**没有**搬 Pages 侧那份生产接线文件 ——
/// 所以下面两个类型是"缝的测试侧镜像",语义逐条照抄,供 <see cref="R32McLayoutV2CompatTests"/>
/// 用;生产接线(<c>App.OnStartup</c> 里给 <see cref="LayoutStoreV2.V1Bridge"/> 赋值)留给后续包,
/// 见 progress/R32-MC.md 的"v1 注入缝留给谁"。
/// </summary>
internal sealed class R32McV1Bridge : ILayoutV1Bridge
{
	/// <summary>数据目录:DirOverride 优先(并同步给 v1 存储,避免两个存储分叉到用户真实数据目录),否则 PageLayoutStore.DirPath。</summary>
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

	/// <summary>读 v1 投影:走 PageLayoutStore 的缓存(与 Windows 侧 PagesLayoutV1Bridge.LoadV1 一致)。</summary>
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
		// Windows 侧是 PageLayoutStore.Reload()(L1 给它补的口子);mac 侧对等物是 ResetCache()
		// (C3a 起就有,语义相同:丢掉内存缓存,下次访问重新读盘)。
		PageLayoutStore.ResetCache();
	}
}

/// <summary>
/// v2 → v1(<see cref="PageLayoutDoc"/>)的投影适配:Core 的 <see cref="LayoutStoreV2.ProjectV1"/>
/// 返回的是 Core 自己的朴素结构,v1 的冻结类型在 App 侧,适配放在这边
/// (App → Core 方向的唯一桥梁)。逐条照抄 Windows 的 <c>LayoutV1Projection.ProjectV1</c>。
/// </summary>
internal static class R32McLayoutV1Projection
{
	/// <summary>把整份 v2 文档投影成 v1 的逐页文档(只含 {id,hidden},顺序即顺序)。</summary>
	internal static Dictionary<string, PageLayoutDoc> ProjectV1(LayoutV2? doc)
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
}
