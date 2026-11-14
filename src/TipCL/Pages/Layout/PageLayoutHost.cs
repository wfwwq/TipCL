using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TipCL.Services;

namespace TipCL.Pages.Layout;

// 分栏比例持久化(L4;设计文档 §2.3 的 13 处清单 + §4.E 的实现约定)。四条规矩:
//   1) **默认 = 现状**:没有存储比例时,逐像素沿用各页原有的列定义(见 SplitCatalog 每一处的现状事实),
//      只有用户真的拖动过分隔条,才切成"按比例"的两栏星号布局 —— 所以首屏与改动前一致(D-11);
//   2) 比例的唯一读写入口 = LayoutStoreV2(load → 改 splits[pageId][splitId] → Save),落在
//      ui_layout_v2.json 的 splits 段;v1 文件一个字节的新数据都不承载(D1),本文件不自己写文件;
//   3) 分隔条宽度 = 该处现状已有的留白(SplitSite.Gap),插入分隔条既不新增也不挤压空白;
//   4) 双击分隔条 = 复位(删掉存储比例,回到现状列定义);折叠一侧(实例库收起文件夹栏 / 版本管理内嵌态 /
//      AI 页收起历史栏)时,该侧与分隔条一起收起且**不写比例**。
//
// 纯数据与纯逻辑(SplitKind / SplitSite / SplitCatalog / SplitRatioStore)刻意零 WPF 依赖,
// 这样 net8 测试宿主能直接断言"13 处清单 / 默认=现状 / 钳位 / 复位 / 不写 v1"(见 LayoutSplitTests)。

/// <summary>分栏形态(§2.3 清单里并非每一处都是左右两栏,逐处写明才能解释"为什么这处不插分隔条")。</summary>
public static class SplitKind
{
	/// <summary>左右两栏:可拖分隔条 + 比例记忆(L4 真正接入的形态)。</summary>
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
/// §2.3 清单里一处的现状声明(纯数据、零 WPF 依赖):默认值 / 现状留白 / 拖拽边界 / 为什么这样处理。
/// 这里是"默认比例 = 现状"的可执行事实表,由 LayoutSplitTests 逐条钉住。
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
			Note = "插件页已安装网格 UniformGrid Columns(现状 3)进 pages[\"plugins\"].cols(只在页面声明 mode=grid 时生效,写入方是 L2/L5 的网格编辑) → 见 SplitRatioStore.PageColumns"
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

/// <summary>
/// 分栏比例的唯一读写入口(纯逻辑,零 WPF 依赖):一律走 LayoutStoreV2,自己绝不写文件。
/// 比例在 <see cref="LayoutSchema.Sanitize"/> 里统一夹到 0.15..0.85,本类在"写之前"先夹一次,
/// 让界面上的宽度与落盘值严格一致。
/// </summary>
public static class SplitRatioStore
{
	/// <summary>比例下限(与契约一致)。</summary>
	public const double MinRatio = LayoutSchema.RatioMin;

	/// <summary>比例上限(与契约一致)。</summary>
	public const double MaxRatio = LayoutSchema.RatioMax;

	/// <summary>契约默认比例(无存储、也无现状声明时使用)。</summary>
	public const double DefaultRatio = LayoutSchema.RatioDefault;

	/// <summary>夹到 0.15..0.85;NaN/Infinity 按默认值处理(与 LayoutSchema.Clamp 同口径)。</summary>
	public static double ClampRatio(double ratio)
	{
		return LayoutSchema.Clamp(ratio, MinRatio, MaxRatio, DefaultRatio);
	}

	/// <summary>读该处已存比例;没有存过返回 null(调用方据此沿用现状列定义)。</summary>
	public static double? Read(string? pageId, string? splitId)
	{
		if (string.IsNullOrEmpty(pageId) || string.IsNullOrEmpty(splitId))
		{
			return null;
		}
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			if (doc?.Layout?.Splits == null || !doc.Layout.Splits.TryGetValue(pageId, out SplitState? split) || split == null)
			{
				return null;
			}
			double ratio = split.Get(splitId, double.NaN);
			if (double.IsNaN(ratio))
			{
				return null;
			}
			return ClampRatio(ratio);
		}
		catch
		{
			// 读布局失败(磁盘被占 / 文件坏)按"没存过"处理,界面回落到现状
			return null;
		}
	}

	/// <summary>该处是否已有存储比例。</summary>
	public static bool IsStored(string? pageId, string? splitId)
	{
		return Read(pageId, splitId).HasValue;
	}

	/// <summary>按声明与当前可用宽解析出该处应生效的比例(有存储用存储,否则用现状比例,再不行用 0.5)。</summary>
	public static double Resolve(string? pageId, string? splitId)
	{
		double? stored = Read(pageId, splitId);
		if (stored.HasValue)
		{
			return ClampRatio(stored.Value);
		}
		SplitSite? site = SplitCatalog.Find(pageId, splitId);
		if (site != null && site.DefaultRatio > 0.0)
		{
			return ClampRatio(site.DefaultRatio);
		}
		return DefaultRatio;
	}

	/// <summary>
	/// 写该处比例(load → 改 splits → Save,唯一入口):先夹 0.15..0.85 再落盘;失败吞掉并返回 false。
	/// 未声明 Writable 的形态(实例详情内嵌的版本管理)一律不写。
	/// </summary>
	public static bool Write(string? pageId, string? splitId, double ratio)
	{
		if (string.IsNullOrEmpty(pageId) || string.IsNullOrEmpty(splitId))
		{
			return false;
		}
		SplitSite? site = SplitCatalog.Find(pageId, splitId);
		if (site != null && !site.Writable)
		{
			return false;
		}
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			if (doc?.Layout == null)
			{
				return false;
			}
			if (!doc.Layout.Splits.TryGetValue(pageId, out SplitState? split) || split == null)
			{
				split = new SplitState();
				doc.Layout.Splits[pageId] = split;
			}
			split.Ratios[splitId] = ClampRatio(ratio);
			LayoutStoreV2.Save(doc);
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>复位:删掉该处比例(其余分栏不动);返回是否真的删掉了东西。</summary>
	public static bool Clear(string? pageId, string? splitId)
	{
		if (string.IsNullOrEmpty(pageId) || string.IsNullOrEmpty(splitId))
		{
			return false;
		}
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			if (doc?.Layout?.Splits == null || !doc.Layout.Splits.TryGetValue(pageId, out SplitState? split) || split == null)
			{
				return false;
			}
			if (!split.Ratios.Remove(splitId))
			{
				return false;
			}
			// 该页一个比例都不剩就把 pageId 这一层也去掉,文档里不留空壳
			if (split.Ratios.Count == 0 && split.Extra.Count == 0)
			{
				doc.Layout.Splits.Remove(pageId);
			}
			LayoutStoreV2.Save(doc);
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>逐页复位(L5 设置页「布局」用):删掉该页全部比例,返回删掉的条数。</summary>
	public static int ClearPage(string? pageId)
	{
		if (string.IsNullOrEmpty(pageId))
		{
			return 0;
		}
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			if (doc?.Layout?.Splits == null || !doc.Layout.Splits.TryGetValue(pageId, out SplitState? split) || split == null)
			{
				return 0;
			}
			int num = split.Ratios.Count;
			if (num == 0)
			{
				return 0;
			}
			doc.Layout.Splits.Remove(pageId);
			LayoutStoreV2.Save(doc);
			return num;
		}
		catch
		{
			return 0;
		}
	}

	/// <summary>像素宽 → 比例(拖动开始时把现状像素宽换算成比例的入口)。</summary>
	public static double RatioFromPixels(double leftWidth, double available)
	{
		if (available <= 0.0 || double.IsNaN(leftWidth))
		{
			return DefaultRatio;
		}
		return ClampRatio(leftWidth / available);
	}

	/// <summary>比例 → 像素宽(报告里的"默认比例 = 现状"换算用)。</summary>
	public static double PixelsFromRatio(double ratio, double available)
	{
		if (available <= 0.0)
		{
			return 0.0;
		}
		return ClampRatio(ratio) * available;
	}

	/// <summary>
	/// 按该处的现状边界夹取比例:先夹合同 0.15..0.85,再用 LeftMin/LeftMax/RightMin/RightMax 换算出的
	/// 像素边界二次夹取(边界来自现状,例如实例库左卡 300、反馈页右栏 360)。
	/// 窄窗下边界自相矛盾(可用宽撑不下两侧最小值)时退回合同夹取,与现状"越界溢出"的行为一致。
	/// </summary>
	public static double ClampRatioForWidths(double ratio, double available, SplitSite? site)
	{
		double num = ClampRatio(ratio);
		if (site == null || available <= 0.0)
		{
			return num;
		}
		double lo = MinRatio;
		double hi = MaxRatio;
		if (site.LeftMin > 0.0)
		{
			lo = Math.Max(lo, site.LeftMin / available);
		}
		if (site.LeftMax > 0.0)
		{
			hi = Math.Min(hi, site.LeftMax / available);
		}
		if (site.RightMin > 0.0)
		{
			hi = Math.Min(hi, (available - site.RightMin) / available);
		}
		if (site.RightMax > 0.0)
		{
			lo = Math.Max(lo, (available - site.RightMax) / available);
		}
		if (lo > hi)
		{
			return num;
		}
		return Math.Min(Math.Max(num, lo), hi);
	}

	/// <summary>
	/// 拖动结束时的提交(纯逻辑,便于测试):按"左栏像素宽 ÷ 可用宽"算出比例,按该处现状边界夹取后写进 v2,
	/// 返回实际生效的比例;NaN = 没有写(宽度取不到,或该形态不可写,如实例详情内嵌态)。
	/// </summary>
	public static double CommitDrag(string? pageId, string? splitId, double leftWidth, double availableWidth)
	{
		if (double.IsNaN(leftWidth) || double.IsNaN(availableWidth) || leftWidth <= 1.0 || availableWidth <= 1.0)
		{
			return double.NaN;
		}
		SplitSite? site = SplitCatalog.Find(pageId, splitId);
		double num = ClampRatioForWidths(leftWidth / availableWidth, availableWidth, site);
		if (!Write(pageId, splitId, num))
		{
			return double.NaN;
		}
		return num;
	}

	/// <summary>
	/// 网格类布局的列数(§4.E 第 10 处:插件页已安装网格):读 pages[pageId].cols,
	/// 只在页面显式声明 mode=grid 时生效(否则回落各页现状列数),再夹到 [min, max]。
	/// </summary>
	public static int PageColumns(string? pageId, int fallback, int min, int max)
	{
		int num = fallback;
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			if (!string.IsNullOrEmpty(pageId) && doc?.Layout?.Pages != null && doc.Layout.Pages.TryGetValue(pageId, out PageLayoutV2? page) && page != null)
			{
				if (page.Mode == LayoutSchema.ModeGrid && page.Cols > 0)
				{
					num = page.Cols;
				}
			}
		}
		catch
		{
			num = fallback;
		}
		if (num <= 0)
		{
			num = fallback;
		}
		return LayoutSchema.ClampInt(num, min, max);
	}
}

/// <summary>
/// 双栏外壳(设计 §4.E 的统一实现):左栏 + 现状留白宽的分隔条 + 右栏,比例存 splits[pageId][splitId]。
/// 用法:`new PageLayoutHost("library", "folders", new GridLength(1, Star), new GridLength(2, Star))` 后 SetLeft/SetRight;
/// 列定义、留白、拖拽边界一律取自 <see cref="SplitCatalog"/> 的现状声明,页面里不散落新的魔数。
/// </summary>
public sealed class PageLayoutHost : Grid
{
	/// <summary>现状没有留白时的分隔条默认宽度(§4.E:18px)。</summary>
	public const double DefaultGap = 18.0;

	private readonly string _pageId;

	private readonly string _splitId;

	private readonly SplitSite? _site;

	private readonly GridLength _legacyLeft;

	private readonly GridLength _legacyRight;

	private readonly double _gap;

	private readonly ColumnDefinition _colLeft = new ColumnDefinition();

	private readonly ColumnDefinition _colGap = new ColumnDefinition();

	private readonly ColumnDefinition _colRight = new ColumnDefinition();

	private readonly GridSplitter _splitter;

	private readonly Border _line;

	private UIElement? _left;

	private UIElement? _right;

	// 现状尺寸(比例模式要临时改拉伸,复位/折叠时按这里还原)
	private double _leftBaseWidth = double.NaN;

	private double _leftBaseMin;

	private double _leftBaseMax = double.PositiveInfinity;

	private double _rightBaseWidth = double.NaN;

	private double _rightBaseMin;

	private double _rightBaseMax = double.PositiveInfinity;

	private double _storedRatio = double.NaN;

	private bool _leftVisible = true;

	private bool _rightVisible = true;

	private bool _dragging;

	private double _dragLeftStart = -1.0;

	// 只有 SetLeft/SetRight 传过元素时才生效:折叠状态以"孩子自己的 Visibility"为准,
	// 这样页面(或 AI 页自检、以后的新入口)直接改 Visibility 时列宽也会跟着收放
	private bool _syncing;

	public PageLayoutHost(string pageId, string splitId, GridLength left, GridLength right)
	{
		_pageId = pageId ?? "";
		_splitId = splitId ?? "";
		_site = SplitCatalog.Find(_pageId, _splitId);
		_legacyLeft = left;
		_legacyRight = right;
		_gap = ((_site != null && _site.Gap > 0.0) ? _site.Gap : DefaultGap);
		base.ColumnDefinitions.Add(_colLeft);
		base.ColumnDefinitions.Add(_colGap);
		base.ColumnDefinitions.Add(_colRight);
		_line = new Border
		{
			Width = 1.0,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Stretch,
			Visibility = Visibility.Collapsed,
			IsHitTestVisible = false
		};
		_line.SetResourceReference(Border.BackgroundProperty, (object)"B.LineSoft");
		Panel.SetZIndex(_line, 1);
		// 分隔条:透明但可命中(Transparent 画刷参与命中测试,null 不参与),悬停/拖拽时中间才显一条细线
		_splitter = new GridSplitter
		{
			ResizeDirection = GridResizeDirection.Columns,
			ResizeBehavior = GridResizeBehavior.PreviousAndNext,
			ShowsPreview = false,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch,
			Width = double.NaN,
			Height = double.NaN,
			Background = Brushes.Transparent,
			Cursor = Cursors.SizeWE,
			Focusable = false,
			ToolTip = I18n.L("自由拖动") + " · " + I18n.L("恢复默认")
		};
		_splitter.Template = TransparentTemplate();
		_splitter.DragStarted += OnDragStarted;
		_splitter.DragCompleted += OnDragCompleted;
		_splitter.PreviewMouseLeftButtonDown += OnSplitterPressed;
		_splitter.MouseEnter += delegate
		{
			ShowLine(dragging: false);
		};
		_splitter.MouseLeave += delegate
		{
			if (!_dragging)
			{
				HideLine();
			}
		};
		Panel.SetZIndex(_splitter, 2);
		Grid.SetColumn(_line, 1);
		Grid.SetColumn(_splitter, 1);
		base.Children.Add(_line);
		base.Children.Add(_splitter);
		_storedRatio = SplitRatioStore.Read(_pageId, _splitId) ?? double.NaN;
		SyncColumns();
	}

	/// <summary>该处现状声明(null = 未登记的 pageId/splitId,按默认兜底)。</summary>
	public SplitSite? Site => _site;

	/// <summary>当前是否已有存储比例(有 = 按比例布局,无 = 现状列定义)。</summary>
	public bool HasStoredRatio => !double.IsNaN(_storedRatio);

	/// <summary>分隔条宽度(= 该处现状留白)。</summary>
	public double GapWidth => _gap;

	/// <summary>左栏是否参与双栏(实例库收起文件夹栏 / 版本管理内嵌态 / AI 页收起历史栏时为 false)。</summary>
	public bool LeftVisible => ((_left != null) ? (_left.Visibility == Visibility.Visible) : _leftVisible);

	/// <summary>
	/// 当前生效的比例:已有存储比例用存储值;否则按现状列宽与可用宽现算(拿不到宽度时用声明的现状比例)。
	/// L5 设置页「布局」显示与导出用。
	/// </summary>
	public double CurrentRatio
	{
		get
		{
			if (!double.IsNaN(_storedRatio))
			{
				return _storedRatio;
			}
			double available = AvailableWidth();
			if (available > 1.0)
			{
				double left = _colLeft.ActualWidth;
				if (left > 0.5)
				{
					return SplitRatioStore.ClampRatio(left / available);
				}
			}
			SplitSite? site = _site;
			if (site != null && site.DefaultRatio > 0.0)
			{
				return SplitRatioStore.ClampRatio(site.DefaultRatio);
			}
			return SplitRatioStore.DefaultRatio;
		}
	}

	/// <summary>设置左栏(挂到第 0 列;记录现状尺寸,比例模式下临时改拉伸)。</summary>
	public void SetLeft(UIElement? element)
	{
		if (_left != null)
		{
			UnhookVisibility(_left, OnChildVisibilityChanged);
			base.Children.Remove(_left);
		}
		_left = element;
		if (element != null)
		{
			Grid.SetColumn(element, 0);
			CaptureSize(element, out _leftBaseWidth, out _leftBaseMin, out _leftBaseMax);
			base.Children.Add(element);
			HookVisibility(element, OnChildVisibilityChanged);
		}
		SyncColumns();
	}

	/// <summary>设置右栏(挂到第 2 列)。</summary>
	public void SetRight(UIElement? element)
	{
		if (_right != null)
		{
			UnhookVisibility(_right, OnChildVisibilityChanged);
			base.Children.Remove(_right);
		}
		_right = element;
		if (element != null)
		{
			Grid.SetColumn(element, 2);
			CaptureSize(element, out _rightBaseWidth, out _rightBaseMin, out _rightBaseMax);
			base.Children.Add(element);
			HookVisibility(element, OnChildVisibilityChanged);
		}
		SyncColumns();
	}

	/// <summary>
	/// 展开 / 收起左栏:收起时左栏与分隔条一起占 0 宽(等价于各页原来"把左栏移出可视树 / 列宽置 0"的做法),
	/// 右栏独占整宽;该形态下不做任何比例读写。
	/// </summary>
	public void SetLeftVisible(bool visible)
	{
		_leftVisible = visible;
		if (_left != null)
		{
			_syncing = true;
			try
			{
				_left.Visibility = (visible ? Visibility.Visible : Visibility.Collapsed);
			}
			finally
			{
				_syncing = false;
			}
		}
		SyncColumns();
	}

	/// <summary>展开 / 收起右栏(各页目前没有用到,留给 L5 的整页视图开关)。</summary>
	public void SetRightVisible(bool visible)
	{
		_rightVisible = visible;
		if (_right != null)
		{
			_syncing = true;
			try
			{
				_right.Visibility = (visible ? Visibility.Visible : Visibility.Collapsed);
			}
			finally
			{
				_syncing = false;
			}
		}
		SyncColumns();
	}

	/// <summary>复位:删掉该处存储比例,回到现状列定义(双击分隔条走这里)。</summary>
	public void ResetToDefault()
	{
		if (!LeftVisible || !_rightVisible)
		{
			return;
		}
		bool had = SplitRatioStore.Clear(_pageId, _splitId);
		_storedRatio = double.NaN;
		SyncColumns();
		if (had)
		{
			AppServices.Window?.Toast(I18n.L("已恢复默认"), "", ToastKind.Success);
		}
	}

	/// <summary>窗口尺寸变了:比例模式下按新的可用宽重新夹取(不改存储值,窄窗也不会把面板挤到 0)。</summary>
	protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
	{
		base.OnRenderSizeChanged(sizeInfo);
		if (!double.IsNaN(_storedRatio) && LeftVisible && _rightVisible)
		{
			ApplyRatioColumns();
		}
	}

	// ————— 内部实现 —————

	// 单栏(折叠)时左栏与分隔条都占 0 宽;双栏时按"有存比例 → 星号比例 / 无存比例 → 现状列定义"
	private void SyncColumns()
	{
		_syncing = true;
		try
		{
			bool leftShown = LeftVisible;
			bool rightShown = ((_right != null) ? (_right.Visibility == Visibility.Visible) : _rightVisible);
			if (!leftShown || !rightShown)
			{
				HideLine();
				_splitter.Visibility = Visibility.Collapsed;
				_colGap.Width = new GridLength(0.0);
				RestoreSize(_left, _leftBaseWidth, _leftBaseMin, _leftBaseMax);
				RestoreSize(_right, _rightBaseWidth, _rightBaseMin, _rightBaseMax);
				_colLeft.Width = (leftShown ? new GridLength(1.0, GridUnitType.Star) : new GridLength(0.0));
				_colRight.Width = ((leftShown || !rightShown) ? new GridLength(0.0) : new GridLength(1.0, GridUnitType.Star));
				return;
			}
			_splitter.Visibility = Visibility.Visible;
			_colGap.Width = new GridLength(_gap);
			if (double.IsNaN(_storedRatio))
			{
				RestoreSize(_left, _leftBaseWidth, _leftBaseMin, _leftBaseMax);
				RestoreSize(_right, _rightBaseWidth, _rightBaseMin, _rightBaseMax);
				_colLeft.Width = _legacyLeft;
				_colRight.Width = _legacyRight;
				return;
			}
			ApplyRatioColumns();
		}
		finally
		{
			_syncing = false;
		}
	}

	// 页面直接改孩子 Visibility 时(例如 AI 页自检脚本)也要跟着收放列宽
	private void OnChildVisibilityChanged(object? sender, EventArgs e)
	{
		if (!_syncing)
		{
			SyncColumns();
		}
	}

	private static void HookVisibility(UIElement element, EventHandler handler)
	{
		DependencyPropertyDescriptor? descriptor = DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(UIElement));
		if (descriptor != null)
		{
			descriptor.AddValueChanged(element, handler);
		}
	}

	private static void UnhookVisibility(UIElement element, EventHandler handler)
	{
		DependencyPropertyDescriptor? descriptor = DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(UIElement));
		if (descriptor != null)
		{
			descriptor.RemoveValueChanged(element, handler);
		}
	}

	// 比例模式:两栏都改星号(原来固定像素宽的孩子改成拉伸 + MinWidth,否则拖动只多出空白)
	private void ApplyRatioColumns()
	{
		double available = AvailableWidth();
		double ratio = SplitRatioStore.ClampRatioForWidths(_storedRatio, available, _site);
		StretchChild(_left, _leftBaseWidth, _leftBaseMin);
		StretchChild(_right, _rightBaseWidth, _rightBaseMin);
		_colLeft.Width = new GridLength(ratio, GridUnitType.Star);
		_colRight.Width = new GridLength(1.0 - ratio, GridUnitType.Star);
	}

	// 可用宽 = 本外壳宽 - 分隔条;拿不到实际宽度时按 0 处理(调用方据此走声明值)
	private double AvailableWidth()
	{
		double width = base.ActualWidth;
		if (width <= 1.0)
		{
			return 0.0;
		}
		return width - _gap;
	}

	private static void CaptureSize(UIElement element, out double width, out double min, out double max)
	{
		width = double.NaN;
		min = 0.0;
		max = double.PositiveInfinity;
		if (element is FrameworkElement framework)
		{
			width = framework.Width;
			min = framework.MinWidth;
			max = framework.MaxWidth;
		}
	}

	private static void StretchChild(UIElement? element, double baseWidth, double baseMin)
	{
		if (!double.IsNaN(baseWidth) && element is FrameworkElement framework)
		{
			framework.MinWidth = Math.Max(baseMin, baseWidth);
			framework.Width = double.NaN;
		}
	}

	private static void RestoreSize(UIElement? element, double baseWidth, double baseMin, double baseMax)
	{
		if (!double.IsNaN(baseWidth) && element is FrameworkElement framework)
		{
			framework.Width = baseWidth;
			framework.MinWidth = baseMin;
			framework.MaxWidth = baseMax;
		}
	}

	private void OnDragStarted(object sender, DragStartedEventArgs e)
	{
		_dragging = true;
		_dragLeftStart = _colLeft.ActualWidth;
		// Auto 列先落成等值的像素列(视觉不变):GridSplitter 拖动时只需处理"一侧像素 + 一侧星号"
		if (_colLeft.Width.IsAuto && _colLeft.ActualWidth > 1.0)
		{
			_colLeft.Width = new GridLength(_colLeft.ActualWidth);
		}
		if (_colRight.Width.IsAuto && _colRight.ActualWidth > 1.0)
		{
			_colRight.Width = new GridLength(_colRight.ActualWidth);
		}
		ShowLine(dragging: true);
	}

	private void OnDragCompleted(object sender, DragCompletedEventArgs e)
	{
		_dragging = false;
		ShowLineIfHovered();
		if (e.Canceled || !_leftVisible || !_rightVisible)
		{
			_dragLeftStart = -1.0;
			return;
		}
		double available = AvailableWidth();
		double left = _colLeft.ActualWidth;
		double start = _dragLeftStart;
		_dragLeftStart = -1.0;
		if (available <= 1.0 || left <= 1.0)
		{
			return;
		}
		// 单击分隔条也会走一次 0 位移的拖拽:位移不足 1px 不写比例(免得随手点一下就把页面切成比例模式)
		if (start > 0.0 && Math.Abs(left - start) < 1.0)
		{
			return;
		}
		double ratio = SplitRatioStore.CommitDrag(_pageId, _splitId, left, available);
		if (!double.IsNaN(ratio))
		{
			_storedRatio = ratio;
			SyncColumns();
		}
	}

	// 双击分隔条 = 复位(先把预览事件吃掉,免得 GridSplitter 又起一次拖拽)
	private void OnSplitterPressed(object sender, MouseButtonEventArgs e)
	{
		if (e.ClickCount == 2)
		{
			e.Handled = true;
			ResetToDefault();
		}
	}

	private void ShowLine(bool dragging)
	{
		_line.SetResourceReference(Border.BackgroundProperty, (object)(dragging ? "B.Accent" : "B.LineSoft"));
		_line.Visibility = Visibility.Visible;
	}

	private void ShowLineIfHovered()
	{
		if (_splitter.IsMouseOver)
		{
			ShowLine(dragging: false);
		}
		else
		{
			HideLine();
		}
	}

	private void HideLine()
	{
		_line.Visibility = Visibility.Collapsed;
	}

	// 空模板:去掉 GridSplitter 默认样式里的灰底/边框,分隔条只靠细线提示(常态完全隐形,首屏与改动前一致)
	private static ControlTemplate TransparentTemplate()
	{
		FrameworkElementFactory factory = new FrameworkElementFactory(typeof(Border));
		factory.SetValue(Border.BackgroundProperty, (object)Brushes.Transparent);
		ControlTemplate template = new ControlTemplate(typeof(GridSplitter));
		template.VisualTree = factory;
		return template;
	}
}
