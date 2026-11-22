// 来源: Windows 版参考仓库 src/TipCL/Pages/SettingsLayoutPage.cs 里的 LayoutBlockEntryCatalog
// (wt-r31-merge @ 0a304a7,冻结只读)。移植时只改一处:条目表按 Linux 树里真实存在的
// BlockHost 宿主重新编排(见下方 [移植差异])。
using System;
using System.Collections.Generic;

namespace TipCL.App.Pages.Layout;

/// <summary>
/// 「页面区块」入口清单:设置页布局视图里每一行对应一个可进编辑态的页面宿主。
/// 纯数据 + 查找,零界面依赖(照 Windows 同名类型)。
/// </summary>
public static class LayoutBlockEntryCatalog
{
	/// <summary>一条入口:<c>PageId</c> = BlockHost 的 pageId;<c>NavId</c> = 要在顶栏切过去的页面 id。</summary>
	public sealed class Entry
	{
		public string PageId { get; init; } = "";

		public string NavId { get; init; } = "";

		/// <summary>设置页内层视图下标;-1 = 该页不是设置页的内层视图。</summary>
		public int SettingsView { get; init; } = -1;

		public string Label { get; init; } = "";

		public bool SingleBlock { get; init; }
	}

	// [移植差异] Windows 的 14 条(stable @ 0a304a7)按 Windows 宿主编目:
	//   settings:general / settings:network / settings:about / java / servers / playtime /
	//   feedback / launch / download / account / tasks / multiplayer / library:main / plugins。
	// Linux 树里没有 java / servers / playtime / feedback / launch / download / settings:about 这些
	// BlockHost 宿主(它们的页仍是传统控件拼装,进不了编辑态 —— 见 R32-LW.md §7),所以不列进去:
	// 列了就会给用户一个点进去只弹「没找到可编辑的区块」的死入口。
	// 反过来 Linux 多出 plugins:market 与六个资源目录页(catalog:<kind>),Windows 侧没有对应宿主。
	public static readonly Entry[] All =
	{
		new Entry { PageId = "settings:general", NavId = "settings", SettingsView = 0, Label = "通用" },
		new Entry { PageId = "settings:network", NavId = "settings", SettingsView = 3, Label = "下载与网络", SingleBlock = true },
		new Entry { PageId = "account", NavId = "account", Label = "账号" },
		new Entry { PageId = "multiplayer", NavId = "multiplayer", Label = "联机" },
		new Entry { PageId = "tasks", NavId = "tasks", Label = "下载任务" },
		new Entry { PageId = "library:main", NavId = "library", Label = "实例库" },
		new Entry { PageId = "plugins", NavId = "plugins", Label = "插件" },
		new Entry { PageId = "plugins:market", NavId = "plugins", Label = "插件 · 市场" },
		new Entry { PageId = "catalog:mod", NavId = "download", Label = "资源目录 · Mod" },
		new Entry { PageId = "catalog:modpack", NavId = "download", Label = "资源目录 · 整合包" },
		new Entry { PageId = "catalog:datapack", NavId = "download", Label = "资源目录 · 数据包" },
		new Entry { PageId = "catalog:resourcepack", NavId = "download", Label = "资源目录 · 资源包" },
		new Entry { PageId = "catalog:shader", NavId = "download", Label = "资源目录 · 光影包" },
		new Entry { PageId = "catalog:world", NavId = "download", Label = "资源目录 · 世界" },
	};

	/// <summary>只有单个区块的页面 id 集合(照 Windows:界面拿它换提示文案)。</summary>
	public static HashSet<string> SingleBlockPageIds()
	{
		HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
		foreach (Entry entry in All)
		{
			if (entry.SingleBlock)
			{
				set.Add(entry.PageId);
			}
		}
		return set;
	}

	/// <summary>按 pageId 找条目(null = 不在清单里)。</summary>
	public static Entry? Find(string? pageId)
	{
		if (string.IsNullOrEmpty(pageId))
		{
			return null;
		}
		foreach (Entry entry in All)
		{
			if (string.Equals(entry.PageId, pageId, StringComparison.Ordinal))
			{
				return entry;
			}
		}
		return null;
	}
}