// 来源: Windows 版参考仓库 src/TipCL/Pages/Layout/LayoutV1BridgePages.cs(分支 layout-shell-hooks @ fa1b3d7,
// R32-LC 随布局 v2 模型层移植;Windows 侧是 Pages 生产件,Linux 侧没有 Pages 层,故先落成测试侧的等价装配)。
// 为什么单独一个文件:L2 的 LayoutEngineTests(lin-engine 包)也直接 new 它 —— 原来它写在 LayoutV2CompatTests.cs
// 里,导致「只搬走 LayoutV2CompatTests.cs」就编不过(无法单独量基线条数)。抽出来之后两边都不再互相牵连;
// 等后面的包把真正的 Pages/App 侧桥(LayoutV1Projection + Linux 版 PagesLayoutV1Bridge)接上,本文件应随之退役。
using System;
using System.Collections.Generic;
using System.IO;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;
using TipCL.Core.Layout;

/// <summary>
/// v1 注入缝的 Linux 实现(Windows 侧对等物 = Pages/Layout/LayoutV1BridgePages.cs 的 PagesLayoutV1Bridge)。
/// 语义逐条照抄:
///   Dir()     = PageLayoutStore.DirPath(DirOverride 优先,且同步给 v1 存储,避免两个存储分叉到真实数据目录);
///   LoadV1()  = PageLayoutStore.LoadAll() + 只搬 {id,hidden}(PageLayoutStore 的缓存不重读);
///   SaveV1()  = 原子写 v1 文件(Utils.WriteJson + LayoutStoreV2.V1Envelope);
///   RefreshV1Cache() = 让"仍走 v1 的读取"立刻看到刚写通的结果。
/// 移植差异(1 处,必须显式记录):Linux 的 PageLayoutStore 没有 Reload()/WriteThrough 刷新缓存的口子
/// (Windows 侧为 L1 补过 Reload;Linux 侧 M2 搬运时只留了 RootProvider)。这里用
/// "DirOverride 清空 + 空串再重设"让它的 Cache() 判定目录变化从而重读磁盘,不新增任何产品 API。
/// </summary>
public sealed class LinuxV1Bridge : ILayoutV1Bridge
{
	/// <summary>数据目录:DirOverride 优先(并同步给 PageLayoutStore),否则 PageLayoutStore.DirPath。</summary>
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

	/// <summary>让 v1 路径立刻看到刚写通的投影(Linux 侧:清 PageLayoutStore 的目录缓存)。</summary>
	public void RefreshV1Cache()
	{
		string dir = PageLayoutStore.DirOverride ?? "";
		PageLayoutStore.DirOverride = "";
		PageLayoutStore.DirOverride = dir;
	}
}
