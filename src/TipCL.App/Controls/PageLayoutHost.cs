// 来源: Windows 版冻结件 windos/src/TipCL/Pages/Layout/PageLayoutHost.cs(@0a304a7,1071 行;
//       另一参考件 wt-layout@fa1b3d7 同源)。本文件是它的 **Avalonia 移植**:
//         SplitKind / SplitSite / SplitCatalog(:28-289)+ SplitRatioStore(:296-565)+ PageLayoutHost(:569-1071)
//       三段**逐条搬**,语义以 Windows 源为准(含「整页复位 ClearPage」与「清空壳的 Extra 守卫」)。
//
// 为什么 Linux 必须补这一件(R32-ROUND2-SPEC §2.2):
//   Windows 的「分栏比例」是 13 处站点的**可执行事实表** + 可拖分隔条 + 比例记忆;
//   Linux 树此前 `SplitRatioStore`/`SplitPageHost`/`SplitCatalog` **零命中**(PARITY §3-1 在 4783c03 上仍成立),
//   四个页面各自把列宽写死(`G(null,"168,*")` / `Grid("272,18,*")` / `Width=240` + 14 外边距 / `Columns=3`),
//   ⇒ 拖动与记忆这层能力在四处**静默丢失**。本包一次补在共用处。
//
// 三条硬口径(照 Windows 原文,一条不少):
//   1) **默认 = 现状**:没有存储比例时逐像素沿用各页原有列定义(见 SplitCatalog 每条的 Note),只有用户
//      真的拖动过才切成"按比例"的两栏星号布局 —— 所以首屏与改动前逐像素一致;
//   2) 比例的唯一读写入口 = LayoutStoreV2(load → 改 `Layout.Splits[pageId][splitId]` → Save),落在
//      ui_layout_v2.json 的 splits 段;本文件绝不自己写文件;**Linux 树没有 UiPrefs 遗留落点**
//      (Mac 那版有一层 UiPrefs.Splits 迁移,本树 UiPrefs 里本就没有 Splits/Columns 键 ⇒ 不需要);
//   3) 分隔条宽度 = 该处现状已有的留白(SplitSite.Gap),插入分隔条既不新增也不挤压空白;
//      双击分隔条 = 复位(删掉存储比例,回到现状列定义);
//      折叠一侧时该侧与分隔条一起收起且**不写比例**。
//
// WPF → Avalonia 降级(逐条见 R32-LINSPLIT.md 的「知情降级表」):
//   D-01 `Panel.SetZIndex(x,n)` 附加属性 → Avalonia 的 `Visual.ZIndex` 属性;
//   D-02 `FrameworkElement.Visibility` → `Visual.IsVisible`;显隐钩子由 `DependencyPropertyDescriptor`
//        改为 `AvaloniaObject.PropertyChanged` + `Visual.IsVisibleProperty` 判等;
//   D-03 `GridSplitter.Template = TransparentTemplate()`(WPF 用 FrameworkElementFactory 去默认灰底)
//        → Avalonia 侧只设 `Background = Brushes.Transparent`(Fluent 模板把 Background 透传给 Border);
//        实测像素证据见报告 §回归(settings/instance/account 三帧与基线 0 px 差);
//   D-04 `SystemParameters` 的 DPI 分支不做:拖动阈值沿用固定 1.0px(与 R32-PARITY §4.2-a 的既有先例一致);
//   D-05 双击复位:WPF 用 `PreviewMouseLeftButtonDown` + `ClickCount==2` → Avalonia 的
//        `PointerPressed` + `PointerPressedEventArgs.ClickCount == 2`;
//   D-06 复位提示:WPF `AppServices.Window?.Toast(...)` → 本树 `LayoutFx.ShowToast(...)`(无窗口时是空操作);
//   D-07 WPF 的 `RatioFromPixels/PixelsFromRatio` 用 `SystemParameters` 换算像素 → 本树按"可用宽"换算,
//        与 Windows 的 `AvailableWidth() = ActualWidth - Gap` 同一口径(不是新发明,是 Windows 自己的算法);
//   D-08 不做 `SetEmbedded` 的**站点接线**(Linux 没有"实例详情内嵌版本管理"这个页面),
//        但 API 与 `Writable=false` 守卫照搬并单测(可降级点 §2.2.3-2)。
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;

namespace TipCL.App.Controls;

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
/// §2.3 清单里一处的现状声明(纯数据、零界面依赖):默认值 / 现状留白 / 拖拽边界 / 为什么这样处理。
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
/// <b>表本身照 Windows 全量搬</b>(它既是"默认 = 现状"的唯一可执行事实,也是 SplitRatioStore 的寻址键);
/// Linux 侧真正接线的只有 4 处,其余条目**如实保留 Windows 的判定**(Rows / ListRow / GridCols /
/// HeightClamp / SingleCol),这样"为什么这处不是可拖分栏"在 Linux 侧同样是可读事实(见每条的 Note)。
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
/// 分栏比例的唯一读写入口(纯逻辑,零界面依赖):一律走 LayoutStoreV2,自己绝不写文件。
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
			return LayoutStoreV2.Save(doc);
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// 复位:删掉该处比例(其余分栏不动);返回是否真的删掉了东西。
	/// 「清空壳的 Extra 守卫」(Windows `:419`):该页比例一个不剩时,**只有**连未知字段 <c>Extra</c>
	/// 也为空才把 pageId 这一层一并去掉 —— 有 Extra 就留着壳,免得把用户文档里的未知字段顺手删掉
	/// (宽容读取的承诺:未知字段保留)。
	/// </summary>
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

	/// <summary>
	/// 逐页复位(L5 设置页「布局」用):删掉该页**全部**比例,返回删掉的条数(该页没有比例 / 没有这一页 ⇒ 0)。
	/// 与"清一处"不同:这一层**连未知字段 Extra 一起删掉**(整页复位就是要它干净 —— Windows `:451`
	/// 直接 `Splits.Remove(pageId)`,不看 Extra)。
	/// </summary>
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
	/// 像素边界二次夹取(边界来自现状,例如实例库左卡 300、版本管理左栏 272、账号页头像卡 240)。
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
/// 用法:`new PageLayoutHost("version-manage", "nav", new GridLength(272.0), new GridLength(1.0, GridUnitType.Star))`
/// 后 SetLeft/SetRight;列定义、留白、拖拽边界一律取自 <see cref="SplitCatalog"/> 的现状声明,
/// 页面里不散落新的魔数。
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

	private Control? _left;

	private Control? _right;

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

	// 只有 SetLeft/SetRight 传过元素时才生效:折叠状态以"孩子自己的可见性"为准,
	// 这样页面(或页面自检、以后的新入口)直接改 IsVisible 时列宽也会跟着收放
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
			IsVisible = false,
			IsHitTestVisible = false
		};
		_line.Dyn(Border.BackgroundProperty, "B.LineSoft");
		// [D-01] WPF 用 Panel.SetZIndex 附加属性;Avalonia 里是 Visual.ZIndex 属性
		_line.ZIndex = 1;
		// 分隔条:透明但可命中(Transparent 画刷参与命中测试,null 不参与),悬停/拖拽时中间才显一条细线
		// [D-03] WPF 另给了空模板去掉默认灰底;Avalonia 的 Fluent 模板把 Background 透传给 Border,
		//        故只需把 Background 设成 Transparent(像素证据见报告 §回归:三帧 0 px 差)
		_splitter = new GridSplitter
		{
			ResizeDirection = GridResizeDirection.Columns,
			ResizeBehavior = GridResizeBehavior.PreviousAndNext,
			ShowsPreview = false,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch,
			Background = Brushes.Transparent,
			Cursor = new Cursor(StandardCursorType.SizeWestEast),
			Focusable = false
		};
		ToolTip.SetTip(_splitter, I18nCore.Tr("自由拖动") + " · " + I18nCore.Tr("恢复默认"));
		_splitter.DragStarted += OnDragStarted;
		_splitter.DragCompleted += OnDragCompleted;
		_splitter.PointerPressed += OnSplitterPressed;
		_splitter.PointerEntered += delegate
		{
			ShowLine(dragging: false);
		};
		_splitter.PointerExited += delegate
		{
			if (!_dragging)
			{
				HideLine();
			}
		};
		// [D-01] Avalonia:Visual.ZIndex 属性
		_splitter.ZIndex = 2;
		SetColumn(_line, 1);
		SetColumn(_splitter, 1);
		base.Children.Add(_line);
		base.Children.Add(_splitter);
		_storedRatio = SplitRatioStore.Read(_pageId, _splitId) ?? double.NaN;
		SyncColumns();
		// [降级 D-07] WPF 覆写 OnRenderSizeChanged;Avalonia 侧订阅 SizeChanged(同一时点:拿到新宽度后)
		SizeChanged += delegate
		{
			if (!double.IsNaN(_storedRatio) && LeftVisible && RightVisible)
			{
				ApplyRatioColumns();
			}
		};
	}

	/// <summary>该处现状声明(null = 未登记的 pageId/splitId,按默认兜底)。</summary>
	public SplitSite? Site => _site;

	/// <summary>当前是否已有存储比例(有 = 按比例布局,无 = 现状列定义)。</summary>
	public bool HasStoredRatio => !double.IsNaN(_storedRatio);

	/// <summary>分隔条宽度(= 该处现状留白)。</summary>
	public double GapWidth => _gap;

	/// <summary>左栏是否参与双栏(折叠一侧时为 false)。</summary>
	public bool LeftVisible => ((_left != null) ? _left.IsVisible : _leftVisible);

	/// <summary>右栏是否参与双栏。</summary>
	public bool RightVisible => ((_right != null) ? _right.IsVisible : _rightVisible);

	/// <summary>分隔条是否在可视树里且可见(折叠态应为 false ⇒ 拖动入口不存在;测试与页面自检用)。</summary>
	public bool SplitterVisible => _splitter.IsVisible;

	/// <summary>分隔条实体的当前排布位置(X / 宽度来自真实 Measure/Arrange,测试读它验命中区几何)。</summary>
	public Rect SplitterBounds => _splitter.Bounds;

	/// <summary>分隔条落在第几列(应为 1 = 留白列)。</summary>
	public int SplitterColumn => GetColumn(_splitter);

	/// <summary>悬停细线当前是否可见(静止时应为 false ⇒ 零像素变化的证据)。</summary>
	public bool HoverLineVisible => _line.IsVisible;

	/// <summary>间隙列的当前声明宽(折叠态为 0)。</summary>
	public double GapColumnWidth => _colGap.Width.Value;

	/// <summary>当前列宽快照(左 / 留白 / 右),测试与报告读真实几何用。</summary>
	public (GridLength Left, GridLength Gap, GridLength Right) DeclaredColumns =>
		(_colLeft.Width, _colGap.Width, _colRight.Width);

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
	public void SetLeft(Control? element)
	{
		if (_left != null)
		{
			_left.PropertyChanged -= OnChildPropertyChanged;
			base.Children.Remove(_left);
		}
		_left = element;
		if (element != null)
		{
			SetColumn(element, 0);
			CaptureSize(element, out _leftBaseWidth, out _leftBaseMin, out _leftBaseMax);
			base.Children.Add(element);
			// [D-02] WPF 用 DependencyPropertyDescriptor 听 Visibility;Avalonia 直接听 PropertyChanged
			element.PropertyChanged += OnChildPropertyChanged;
		}
		SyncColumns();
	}

	/// <summary>设置右栏(挂到第 2 列)。</summary>
	public void SetRight(Control? element)
	{
		if (_right != null)
		{
			_right.PropertyChanged -= OnChildPropertyChanged;
			base.Children.Remove(_right);
		}
		_right = element;
		if (element != null)
		{
			SetColumn(element, 2);
			CaptureSize(element, out _rightBaseWidth, out _rightBaseMin, out _rightBaseMax);
			base.Children.Add(element);
			element.PropertyChanged += OnChildPropertyChanged;
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
				_left.IsVisible = visible;
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
				_right.IsVisible = visible;
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
		if (!LeftVisible || !RightVisible)
		{
			return;
		}
		bool had = SplitRatioStore.Clear(_pageId, _splitId);
		_storedRatio = double.NaN;
		SyncColumns();
		if (had)
		{
			// [D-06] WPF 是 AppServices.Window?.Toast(...);本树走 LayoutFx(无窗口时是空操作)
			LayoutFx.ShowToast(I18nCore.Tr("已恢复默认"), "", TipCL.Core.Layout.ToastKind.Success);
		}
	}

	/// <summary>
	/// 【测试缝】拖动松手那条路的入口(与真实拖动**同一段代码**):真实路径是
	/// <c>GridSplitter.DragCompleted</c> → <see cref="OnDragCompleted"/>;这里只把"松手"这一步
	/// 直接暴露给用例,不去模拟鼠标拖拽。
	/// </summary>
	internal void CommitDragForTest()
	{
		OnDragCompleted(_splitter, new VectorEventArgs());
	}

	/// <summary>【测试缝】双击分隔条那条路的入口(与真实路径同一段代码)。</summary>
	internal void ResetToDefaultForTest()
	{
		ResetToDefault();
	}

	// ————— 内部实现 —————

	// 单栏(折叠)时左栏与分隔条都占 0 宽;双栏时按"有存比例 → 星号比例 / 无存比例 → 现状列定义"
	private void SyncColumns()
	{
		_syncing = true;
		try
		{
			// 左列的 ColumnDefinition.MinWidth 同步左孩子的 MinWidth。
			// 星号列的可用宽不够时,左卡片的 MinWidth 会让列撑开、右侧让位,而不是被 Grid 直接裁掉。
			// 折叠态下左列宽为 0,不设下限。
			SyncLeftMinWidth();
			bool leftShown = LeftVisible;
			bool rightShown = RightVisible;
			if (!leftShown || !rightShown)
			{
				HideLine();
				_splitter.IsVisible = false;
				_colGap.Width = new GridLength(0.0);
				RestoreSize(_left, _leftBaseWidth, _leftBaseMin, _leftBaseMax);
				RestoreSize(_right, _rightBaseWidth, _rightBaseMin, _rightBaseMax);
				_colLeft.Width = (leftShown ? new GridLength(1.0, GridUnitType.Star) : new GridLength(0.0));
				_colRight.Width = ((leftShown || !rightShown) ? new GridLength(0.0) : new GridLength(1.0, GridUnitType.Star));
				return;
			}
			_splitter.IsVisible = true;
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

	// 页面直接改孩子 IsVisible 时(例如页面自检脚本)也要跟着收放列宽
	// [D-02] WPF 用 DependencyPropertyDescriptor 的 AddValueChanged;Avalonia 听 PropertyChanged
	private void OnChildPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
	{
		if (!_syncing && e.Property == Visual.IsVisibleProperty)
		{
			SyncColumns();
		}
	}

	/// <summary>
	/// 把左孩子的 MinWidth 同步给左列(折叠态 / 宽度不可用 → 0)。
	/// 这是"其他还在用左右分栏的页面"(设置导航、版本管理、账号)的通用加固:
	/// 列不够宽时由列撑开、右侧让位,不再裁掉内容。只加下限,不动孩子自己的尺寸。
	/// </summary>
	private void SyncLeftMinWidth()
	{
		double min = 0.0;
		if (_leftVisible && _left != null)
		{
			double wanted = _left.MinWidth;
			if (!double.IsNaN(wanted) && !double.IsInfinity(wanted) && wanted > 0.0)
			{
				min = wanted;
			}
		}
		if (Math.Abs(_colLeft.MinWidth - min) > 0.001)
		{
			_colLeft.MinWidth = min;
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
		double width = base.Bounds.Width;
		if (width <= 1.0)
		{
			return 0.0;
		}
		return width - _gap;
	}

	private static void CaptureSize(Control element, out double width, out double min, out double max)
	{
		width = double.NaN;
		min = 0.0;
		max = double.PositiveInfinity;
		width = element.Width;
		min = element.MinWidth;
		max = element.MaxWidth;
	}

	private static void StretchChild(Control? element, double baseWidth, double baseMin)
	{
		if (!double.IsNaN(baseWidth) && element != null)
		{
			element.MinWidth = Math.Max(baseMin, baseWidth);
			element.Width = double.NaN;
		}
	}

	private static void RestoreSize(Control? element, double baseWidth, double baseMin, double baseMax)
	{
		if (!double.IsNaN(baseWidth) && element != null)
		{
			element.Width = baseWidth;
			element.MinWidth = baseMin;
			element.MaxWidth = baseMax;
		}
	}

	private void OnDragStarted(object? sender, VectorEventArgs e)
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

	private void OnDragCompleted(object? sender, VectorEventArgs e)
	{
		_dragging = false;
		ShowLineIfHovered();
		// [D-09] WPF 的 DragCompletedEventArgs 有 Canceled(拖动被取消);Avalonia 的 VectorEventArgs
		// 没有该位 ⇒ 本树只在"折叠/宽度取不到/位移不足"三种情况下不写比例(见下),取消语义由
		// GridSplitter 自己不再派发 DragCompleted 表达。
		if (!_leftVisible || !_rightVisible)
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
	// [D-05] WPF 是 PreviewMouseLeftButtonDown + e.ClickCount == 2;Avalonia 走 PointerPressed
	private void OnSplitterPressed(object? sender, PointerPressedEventArgs e)
	{
		if (e.ClickCount == 2)
		{
			e.Handled = true;
			ResetToDefault();
		}
	}

	private void ShowLine(bool dragging)
	{
		_line.Dyn(Border.BackgroundProperty, dragging ? "B.Accent" : "B.LineSoft");
		_line.IsVisible = true;
	}

	private void ShowLineIfHovered()
	{
		if (_splitter.IsPointerOver)
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
		_line.IsVisible = false;
	}
}
