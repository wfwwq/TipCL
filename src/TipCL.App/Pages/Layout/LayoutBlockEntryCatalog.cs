// 来源: Windows 版参考仓库 src/TipCL/Pages/SettingsLayoutPage.cs 里的 LayoutBlockEntryCatalog
// (wt-r31-merge @ 0a304a7,冻结只读)。移植时只改一处:条目表按 Linux 树里真实存在的
// BlockHost 宿主重新编排(见下方 [移植差异])。
using System;
using System.Collections.Generic;
using TipCL.Core.Launcher;

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

		/// <summary>下载页分段下标(0 原版 / 1..6 六个目录);-1 = 不是下载页分段。</summary>
		public int Segment { get; init; } = -1;

		/// <summary>插件页内层页签(market / dev;空 = 不用切页签)。</summary>
		public string Tab { get; init; } = "";

		public string Label { get; init; } = "";

		public bool SingleBlock { get; init; }

		/// <summary>
		/// [layout-fix 第三批] 这一页要先由用户打开到具体对象才会出现(例:版本安装页要先点一个版本)。
		/// 非空 = 跳过去之后找不到宿主时给的提示正文(已经过文案门面前的中文原文)。
		/// </summary>
		public string NeedsContext { get; init; } = "";
	}

	// [移植差异] Windows 的 14 条(stable @ 0a304a7)按 Windows 宿主编目:
	//   settings:general / settings:network / settings:about / java / servers / playtime /
	//   feedback / launch / download / account / tasks / multiplayer / library:main / plugins。
	// [R32-LINMORE] 本包把 Linux 缺的宿主补齐后,这里对齐到 15 条(Windows 的 14 条 − settings:about):
	//   1) 新增 download(原版分段)/ java / servers / playtime / feedback —— 它们的 BlockHost 在本包接的
	//      (DownloadPage 的 _vanillaView、SettingsSubView.UseBlockHost);
	//   2) **仍缺 settings:about**:Linux 的「关于」卡留在 FeedbackView 的 feedback 内容块里,
	//      没有独立的 about 宿主(规格 §2.1-10 的方案 B,理由见 FeedbackView 构造函数注释)。
	//      列进去只会给用户一个点进去弹「没找到可编辑的区块」的死入口,所以不列;这是本包唯一一处
	//      「与 Windows 的入口条数不等」,登记在 R32-LINMORE.md 的偏离清单。
	//   3) Linux 多出 plugins:market 与六个资源目录页(catalog:<kind>),Windows 侧没有对应宿主。
	// 下载页 / 插件页的宿主只有在对应分段 / 页签被选中后才进可视树,所以 Segment / Tab 两个字段负责先切过去。
	public static readonly Entry[] All =
	{
		new Entry { PageId = "settings:general", NavId = "settings", SettingsView = 0, Label = I18nCore.Tr("通用") },
		new Entry { PageId = "settings:network", NavId = "settings", SettingsView = 3, Label = I18nCore.Tr("下载与网络"), SingleBlock = true },
		new Entry { PageId = "java", NavId = "settings", SettingsView = 2, Label = "Java" },
		new Entry { PageId = "servers", NavId = "settings", SettingsView = 4, Label = I18nCore.Tr("服务器") },
		new Entry { PageId = "playtime", NavId = "settings", SettingsView = 5, Label = I18nCore.Tr("游戏时长") },
		new Entry { PageId = "feedback", NavId = "settings", SettingsView = 6, Label = I18nCore.Tr("反馈与帮助") },
		new Entry { PageId = "launch", NavId = "launch", Label = I18nCore.Tr("启动") },
		new Entry { PageId = "download", NavId = "download", Segment = 0, Label = I18nCore.Tr("下载") },
		new Entry { PageId = "account", NavId = "account", Label = I18nCore.Tr("账号") },
		new Entry { PageId = "multiplayer", NavId = "multiplayer", Label = I18nCore.Tr("联机") },
		new Entry { PageId = "tasks", NavId = "tasks", Label = I18nCore.Tr("下载任务") },
		new Entry { PageId = "library:main", NavId = "library", Label = I18nCore.Tr("实例库") },
		new Entry { PageId = "plugins", NavId = "plugins", Label = I18nCore.Tr("插件") },
		new Entry { PageId = "plugins:market", NavId = "plugins", Tab = "market", Label = "插件 · 市场" },
		new Entry { PageId = "catalog:mod", NavId = "download", Segment = 1, Label = "资源目录 · Mod" },
		new Entry { PageId = "catalog:modpack", NavId = "download", Segment = 2, Label = "资源目录 · 整合包" },
		new Entry { PageId = "catalog:datapack", NavId = "download", Segment = 3, Label = "资源目录 · 数据包" },
		new Entry { PageId = "catalog:resourcepack", NavId = "download", Segment = 4, Label = "资源目录 · 资源包" },
		new Entry { PageId = "catalog:shader", NavId = "download", Segment = 5, Label = "资源目录 · 光影包" },
		new Entry { PageId = "catalog:world", NavId = "download", Segment = 6, Label = "资源目录 · 世界" },
		// [layout-fix 第三批] 新拆成区块的四页:AI 助手 / 版本安装 / 版本管理 / 设置 → 布局
		new Entry { PageId = "ai", NavId = "ai", Label = I18nCore.Tr("AI 助手") },
		new Entry { PageId = "install", NavId = "download", Segment = 0, Label = I18nCore.Tr("版本安装"), NeedsContext = I18nCore.Tr("先在「下载 → 原版」里点一个版本进入安装页,再在页面上右键「自定义布局...」") },
		new Entry { PageId = "version-manage", NavId = "instance", Label = I18nCore.Tr("版本管理") },
		new Entry { PageId = "settings:layout", NavId = "settings", SettingsView = 7, Label = I18nCore.Tr("设置 · 布局") },
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