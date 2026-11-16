using System;
using System.Collections.Generic;

namespace TipCL.Pages;

// 来源:Windows 版冻结件 wt-r31-merge @0a304a7
//       windos/src/TipCL/Pages/SettingsLayoutPage.cs 的 `LayoutBlockEntryCatalog`(:810;纯数据)。
//
// 移植方式:逐字等价(零 WPF 依赖)。命名空间 `TipCL.Pages.Layout` → `TipCL.Pages`。
//
// mac 侧差异(逐条,已实测):
//   1) 14 个入口在 mac 树上**都已有对应的 BlockHost 宿主**(本包把最后两个补齐:
//      library:main 见 LibraryPage.cs、plugins 见 Plugins/PluginsPage.cs)。逐条对照的
//      Windows 构造点见报告 §ShellTabs/SplitCatalog 与 §逐项;`LayoutBlockEntryCatalog_BindsToLiveHosts`
//      测试会遍历 All、按 NavId 起页并在该页里找 pageId 一致的宿主,缺一个就红。
//   2) 另外两处宿主不在入口表里(与 Windows 同口径:入口表是「整页可编辑」清单):
//      catalog:<类型>(资源目录页,pageId 随分段变)与 plugins:market(插件页里的市场子视图,
//      最内层右键优先)。二者仍各自可进编辑态,只是不在设置页的入口清单里。
//   3) 入口表**保留全部 14 条**(与 Windows 同口径:入口表是静态事实);`OpenBlockEditor`
//      找不到宿主时按 Windows 原逻辑弹 "没找到可编辑的区块" 警告,不伪造成功。

/// <summary>
/// 设置页「布局」视图里「页面区块」小节的入口表(纯数据、零界面依赖):
/// 覆盖全仓 12 个 BlockHost 宿主 + [layout-fix] 新增的实例库 / 插件 2 个。
/// <c>LayoutEditor.CanEdit</c> 的入口条件是 specs ≥ 1,单块页同样能进编辑态
/// —— 单块页能调的东西少(外观 / 标题 / 先隐藏),但入口不该缺。
/// </summary>
public static class LayoutBlockEntryCatalog
{
	/// <summary>一个页面区块入口:pageId + 导航 id + 设置页视图下标(-1 = 不在设置页里)+ 文案 key。</summary>
	public sealed class Entry
	{
		/// <summary>布局文档里的 pageId(与 BlockHost 构造时传的 pageId 一致)。</summary>
		public string PageId { get; set; } = "";

		/// <summary>MainWindow 的导航 id(先跳到该页,再找它的 BlockHost)。</summary>
		public string NavId { get; set; } = "";

		/// <summary>该页在设置页里时先切到哪个视图;≥ 0 表示先 Select(该视图)再找宿主,-1 = 不在设置页里。</summary>
		public int SettingsView { get; set; } = -1;

		/// <summary>入口文案的 I18n key(复用既有导航文案)。</summary>
		public string Label { get; set; } = "";

		/// <summary>是否单块页(只有一个块:能调外观 / 标题 / 隐藏,不能重排)。</summary>
		public bool SingleBlock { get; set; }
	}

	/// <summary>14 个宿主:设置页内 7 个 + 顶栏导航 5 个 + [layout-fix] 规格 §4-能力3 新增的实例库 / 插件 2 个。</summary>
	public static readonly Entry[] All = new Entry[14]
	{
		new Entry
		{
			PageId = "settings:general",
			NavId = "settings",
			SettingsView = 0,
			Label = "通用"
		},
		new Entry
		{
			PageId = "settings:network",
			NavId = "settings",
			SettingsView = 3,
			Label = "下载与网络",
			SingleBlock = true
		},
		new Entry
		{
			PageId = "settings:about",
			NavId = "settings",
			SettingsView = 6,
			Label = "反馈与关于",
			SingleBlock = true
		},
		new Entry
		{
			PageId = "java",
			NavId = "settings",
			SettingsView = 2,
			Label = "Java"
		},
		new Entry
		{
			PageId = "servers",
			NavId = "settings",
			SettingsView = 4,
			Label = "服务器"
		},
		new Entry
		{
			PageId = "playtime",
			NavId = "settings",
			SettingsView = 5,
			Label = "游戏时长"
		},
		new Entry
		{
			PageId = "feedback",
			NavId = "settings",
			SettingsView = 6,
			Label = "反馈与帮助"
		},
		new Entry
		{
			PageId = "launch",
			NavId = "launch",
			Label = "启动"
		},
		new Entry
		{
			PageId = "download",
			NavId = "download",
			Label = "下载"
		},
		new Entry
		{
			PageId = "account",
			NavId = "account",
			Label = "账号"
		},
		new Entry
		{
			PageId = "tasks",
			NavId = "tasks",
			Label = "下载任务"
		},
		new Entry
		{
			PageId = "multiplayer",
			NavId = "multiplayer",
			Label = "联机"
		},
		new Entry
		{
			// [layout-fix] 规格 §4-能力3:实例库改成区块宿主(library:main)
			PageId = "library:main",
			NavId = "library",
			Label = "实例库"
		},
		new Entry
		{
			// [layout-fix] 规格 §4-能力3:插件页改成区块宿主(plugins;插件市场的 plugins:market 是子视图)
			PageId = "plugins",
			NavId = "plugins",
			Label = "插件"
		}
	};

	/// <summary>单块页的 pageId 列表(报告与测试用)。</summary>
	public static List<string> SingleBlockPageIds()
	{
		List<string> list = new List<string>();
		foreach (Entry entry in All)
		{
			if (entry.SingleBlock)
			{
				list.Add(entry.PageId);
			}
		}
		return list;
	}

	/// <summary>按 pageId 取入口(没有返回 null)。</summary>
	public static Entry? Find(string? pageId)
	{
		if (string.IsNullOrEmpty(pageId))
		{
			return null;
		}
		foreach (Entry entry in All)
		{
			if (entry.PageId == pageId)
			{
				return entry;
			}
		}
		return null;
	}
}