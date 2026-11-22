using System;
using System.Collections.Generic;

namespace TipCL.Pages;

// 来源:Windows 版冻结件 wt-r31-merge @0a304a7
//       windos/src/TipCL/Pages/Layout/PageLayoutHost.cs 的 `SplitKind` / `SplitSite` / `SplitCatalog`
//       (该文件 :28 / :53 / :105;本文件只搬这三段**纯数据**)。
//
// 为什么本包必须移植它(R32-MACMORE §SplitCatalog 处置):
//   Windows 的 `SettingsPage.LayoutSplitRows()`(`SettingsLayoutPage.cs`)硬迭代 `SplitCatalog.All`
//   + `SplitCatalog.Embedded`(通过 All)、并用 `site.IsTwoColumn / DefaultPixels / PixelsSide /
//   LeftMin / Note` 渲染每一行。不移植它,"分栏比例"这一段就无法忠实落地 ——
//   **因果依赖**,不是顺手补洞。mac 侧窗口宽度固定 900×550、13 处里实际接入的两栏少于 Windows,
//   但现状表本身必须完整(它是"默认 = 现状"的唯一可执行事实,也是 SplitRatioStore 的寻址键)。
//
// 与 Windows 的差异(逐条):
//   1) 命名空间 `TipCL.Pages.Layout` → `TipCL.Pages`(mac 树 Pages 平铺,D7)。
//   2) Windows 的 `SplitRatioStore`(同文件 :310+)在 mac 树上**已存在等价物**:
//      `TipCL.Controls.PageLayoutHost.Read / Clear / ClearPage(PageBase.Win?.Prefs, pageId, splitId)`
//      —— 见 `Controls/PageLayoutHost.cs:421/430/469/518/547` 与调用先例
//      `Pages/Plugins/PluginsPage.Installed.cs:74`。故本文件**不重复搬 SplitRatioStore**,
//      布局视图直接调 mac 既有的静态入口(避免两套比例读写口打架)。

/// <summary>分栏形态(§2.3 清单里并非每一处都是左右两栏,逐处写明才能解释"为什么这处不插分隔条")。</summary>
public static class SplitKind
{
	/// <summary>左右两栏:可拖分隔条 + 比例记忆(真正接入的形态)。</summary>
	public const string TwoColumn = "two-column";

	/// <summary>纵向多行(头/内容/底栏等),不存在左右两栏比例。</summary>
	public const string Rows = "rows";

	/// <summary>列表行的模板列宽(列表控件内部),不是页面级分栏。</summary>
	public const string ListRow = "list-row";

	/// <summary>网格列数(UniformGrid 之类),进 pages[].cols,不是左右比例。</summary>
	public const string GridCols = "grid-cols";

	/// <summary>限宽 / 限高(最大宽度、页高百分比),不是分栏。</summary>
	public const string HeightClamp = "height-clamp";

	/// <summary>单列布局,无分栏。</summary>
	public const string SingleCol = "single-col";
}

/// <summary>
/// §2.3 清单里一处的现状声明(纯数据、零界面依赖):默认值 / 现状留白 / 拖拽边界 / 为什么这样处理。
/// 这里是"默认比例 = 现状"的可执行事实表。
/// </summary>
public sealed class SplitSite
{
	/// <summary>§2.3 清单里的序号(1..13);额外形态(如实例详情内嵌版本管理)为 0。</summary>
	public int Index { get; set; }

	/// <summary>页面 id(与 v2 文档 splits 的第一层键一致)。</summary>
	public string PageId { get; set; } = "";

	/// <summary>分栏 id(与 v2 文档 splits[pageId] 的键一致)。</summary>
	public string SplitId { get; set; } = "";

	/// <summary>形态:见 <see cref="SplitKind"/>。</summary>
	public string Kind { get; set; } = SplitKind.TwoColumn;

	/// <summary>现状比例(0 = 该处按像素/星号定义,单一比例表达不了)。</summary>
	public double DefaultRatio { get; set; }

	/// <summary>现状固定像素宽(<see cref="PixelsSide"/> 决定是左栏还是右栏;0 = 非固定像素)。</summary>
	public double DefaultPixels { get; set; }

	/// <summary>固定像素宽属于哪一侧:left | right(仅 DefaultPixels &gt; 0 时有意义)。</summary>
	public string PixelsSide { get; set; } = "left";

	/// <summary>两栏之间的现状留白,同时就是分隔条宽度(0 = 该处现状没有留白,不插分隔条)。</summary>
	public double Gap { get; set; }

	/// <summary>左栏列宽下限(0 = 不限;取现状设计宽,避免被拖到挤坏内容)。</summary>
	public double LeftMin { get; set; }

	/// <summary>左栏列宽上限(0 = 不限)。</summary>
	public double LeftMax { get; set; }

	/// <summary>右栏列宽下限(0 = 不限;如反馈页右栏现状固定 360)。</summary>
	public double RightMin { get; set; }

	/// <summary>右栏列宽上限(0 = 不限)。</summary>
	public double RightMax { get; set; }

	/// <summary>是否允许写比例(false = 该形态只读,如实例详情内嵌的版本管理)。</summary>
	public bool Writable { get; set; } = true;

	/// <summary>现状事实与该处处理方式的说明(报告与代码里同一份口径)。</summary>
	public string Note { get; set; } = "";

	/// <summary>是不是左右两栏(只有两栏才插分隔条)。</summary>
	public bool IsTwoColumn => Kind == SplitKind.TwoColumn;
}

/// <summary>
/// §2.3「分栏:13 处,0 个可拖分隔条,0 处记忆」清单的可执行版本。
/// 13 条与设计文档表格逐行对应(Index 即行号);实例详情内嵌的版本管理是额外一条(Index = 0)。
/// </summary>
public static class SplitCatalog
{
	/// <summary>13 处现状清单(设计 §2.3 表格逐行)。</summary>
	public static readonly SplitSite[] All = new SplitSite[13]
	{
		new SplitSite
		{
			Index = 1,
			PageId = "library",
			SplitId = "folders",
			Kind = SplitKind.TwoColumn,
			DefaultRatio = 1.0 / 3.0,
			PixelsSide = "left",
			Gap = 18.0,
			LeftMin = 300.0,
			Note = "实例库左栏现状 \"*,18,2*\"(星号 1:2 = 比例 1/3),左卡 MinWidth 300 / MaxWidth 380 原样保留;分隔条直接复用现状那 18px 空白列,首屏逐像素不变"
		},
		new SplitSite
		{
			Index = 2,
			PageId = "settings",
			SplitId = "nav",
			Kind = SplitKind.TwoColumn,
			DefaultPixels = 168.0,
			PixelsSide = "left",
			Gap = 18.0,
			LeftMin = 168.0,
			Note = "设置页左导航现状 \"168,*\";两个视图顶层本来就有 18px 左留白,分隔条占住那段留白、视图左移 18px 回到原位,首屏逐像素不变"
		},
		new SplitSite
		{
			Index = 3,
			PageId = "instance:detail",
			SplitId = "seg",
			Kind = SplitKind.Rows,
			Note = "实例详情分段是横向 rail(模组/存档/资源包/光影/设置),不是左右栏 → 本页没有可存的分栏比例;其中「设置」段内嵌的版本管理见 instance:settings/vm"
		},
		new SplitSite
		{
			Index = 4,
			PageId = "launch",
			SplitId = "hero",
			Kind = SplitKind.HeightClamp,
			Note = "启动页是英雄区 MaxWidth 680 + 内容区 MaxHeight=页高 62% 的限宽/限高,不是左右分栏;且 LaunchPage 属 L1 写范围,本包不动"
		},
		new SplitSite
		{
			Index = 5,
			PageId = "download",
			SplitId = "row",
			Kind = SplitKind.ListRow,
			Note = "下载页原版段行内 \"130,*,110,Auto\" 是版本列表行的模板列宽(列表控件内部),不是页面分栏;原版段整体已是 BlockHost 块系统,顺序/显隐由 v2 pages 管"
		},
		new SplitSite
		{
			Index = 6,
			PageId = "version-manage",
			SplitId = "nav",
			Kind = SplitKind.TwoColumn,
			DefaultPixels = 272.0,
			PixelsSide = "left",
			Gap = 18.0,
			LeftMin = 272.0,
			Note = "版本管理页现状 \"272,18,*\";分隔条复用现状那 18px 空白列,首屏逐像素不变。内嵌态(实例详情第 5 段)由 SetLeftVisible(false) 收起左栏与分隔条,且本形态不写比例"
		},
		new SplitSite
		{
			Index = 7,
			PageId = "ai",
			SplitId = "history",
			Kind = SplitKind.TwoColumn,
			DefaultPixels = 220.0,
			PixelsSide = "left",
			Gap = 10.0,
			LeftMin = 220.0,
			Note = "AI 页历史栏现状固定 Width=220(自带 1px 右边框作分隔线);分隔条复用主区顶部操作条的 10px 左内边距,主区左移 10px 回到原位,首屏逐像素不变。历史栏收起时左栏与分隔条一起收起"
		},
		new SplitSite
		{
			Index = 8,
			PageId = "feedback",
			SplitId = "side",
			Kind = SplitKind.TwoColumn,
			DefaultPixels = 360.0,
			PixelsSide = "right",
			Gap = 14.0,
			RightMin = 360.0,
			Note = "反馈页现状 \"*,360\",右卡自带 14px 左外边距;该 14px 改为分隔条、卡片外边距去掉,两栏几何完全等价(右栏仍 346 宽、左栏宽度不变)"
		},
		new SplitSite
		{
			Index = 9,
			PageId = "account",
			SplitId = "hero",
			Kind = SplitKind.TwoColumn,
			DefaultPixels = 240.0,
			PixelsSide = "left",
			Gap = 14.0,
			LeftMin = 240.0,
			Note = "账号页头像卡现状 Width=240 + 14px 右外边距;该 14px 改为分隔条,列几何完全等价。比例模式下卡片改拉伸(MinWidth 240),复位后回到 240 固定宽"
		},
		new SplitSite
		{
			Index = 10,
			PageId = "plugins",
			SplitId = "cols",
			Kind = SplitKind.GridCols,
			Note = "插件页已安装网格 UniformGrid Columns(现状 3)进 pages[\"plugins\"].cols(只在页面声明 mode=grid 时生效,写入方是 L2/L5 的网格编辑) → 见 PageLayoutHost.PageColumns"
		},
		new SplitSite
		{
			Index = 11,
			PageId = "settings:about",
			SplitId = "about",
			Kind = SplitKind.Rows,
			Note = "设置页「反馈与关于」现状 \"Auto,*\" 是「标题卡 + 反馈页」两行,纵向,没有左右分栏"
		},
		new SplitSite
		{
			Index = 12,
			PageId = "catalog",
			SplitId = "list",
			Kind = SplitKind.SingleCol,
			Note = "下载页另外 6 段内嵌的 CatalogPage 是单列 + 26px 外边距,没有分栏;本包只给它补了稳定 pageId(CatalogPage.LayoutPageId)供 L2/L5 寻址"
		},
		new SplitSite
		{
			Index = 13,
			PageId = "install",
			SplitId = "frame",
			Kind = SplitKind.Rows,
			Note = "安装页现状 \"Auto,*,Auto\" 是「头部 / 滚动内容 / 底栏」三行,纵向;底栏 Auto、内容 * 吸收全部余量,不存在可存储的比例"
		}
	};

	/// <summary>实例详情第 5 段内嵌的版本管理(§4.E:与顶级实现同一套,但内嵌态隐藏分隔条且不写比例)。</summary>
	public static readonly SplitSite Embedded = new SplitSite
	{
		Index = 0,
		PageId = "instance:settings",
		SplitId = "vm",
		Kind = SplitKind.TwoColumn,
		DefaultPixels = 272.0,
		PixelsSide = "left",
		Gap = 18.0,
		LeftMin = 272.0,
		Writable = false,
		Note = "实例详情「设置」段内嵌的版本管理:内嵌态必须隐藏分隔条且不写比例(Writable=false),回落双栏时按 272px 现状还原"
	};

	/// <summary>按 pageId + splitId 取现状声明;不是已知分栏返回 null(调用方走默认兜底,不抛)。</summary>
	public static SplitSite? Find(string? pageId, string? splitId)
	{
		if (string.IsNullOrEmpty(pageId) || string.IsNullOrEmpty(splitId))
		{
			return null;
		}
		foreach (SplitSite site in All)
		{
			if (site.PageId == pageId && site.SplitId == splitId)
			{
				return site;
			}
		}
		if (Embedded.PageId == pageId && Embedded.SplitId == splitId)
		{
			return Embedded;
		}
		return null;
	}

	/// <summary>13 处里真正是左右两栏、因而插了分隔条的条数(报告里"已接入"的口径)。</summary>
	public static int TwoColumnCount()
	{
		int num = 0;
		foreach (SplitSite site in All)
		{
			if (site.IsTwoColumn)
			{
				num++;
			}
		}
		return num;
	}
}