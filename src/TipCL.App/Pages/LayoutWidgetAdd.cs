using System;
using System.Collections.Generic;
using System.Globalization;
using TipCL.Core.Launcher.Layout;
using TipCL.Services;

namespace TipCL.Pages;

// [R32-MACMORE] 加块(自建区块)的**纯逻辑**部分 —— 零界面依赖,net8 测试宿主可直接断言。
//
// 为什么是 mac 侧新增、而不是从 Windows 冻结件搬:
//   Windows 的 `SettingsLayoutPage.LayoutWidgetRows()`(`:439-450`)里那个「添加自建区块」按钮是
//   **`add.IsEnabled = false`**(L6 未落地,源码注释原文「由下一包(L6)落地后在这里增删改」)。
//   也就是说 Windows 只有入口的位置,没有加块行为;本包按任务书 §1/§3-4 的要求把这条路径**真正打通**
//   (加块入口 → 枚举部件清单 → 添加 1 个 → 上屏 → 落盘 → 重开仍在),落点就是那一节。
//
// 数据契约(与 v2 文档 §4.F 一字不差,不发明新字段):
//   自建块定义 = `layout.widgets[]` 的一条 `WidgetDef`(id / type / title / settings);
//   页里的引用 = `layout.pages[pageId].blocks[]` 的一条 `BlockLayout`,id = `custom:<widgetId>`;
//   类型白名单 = `LayoutSchema.IsKnownWidgetType` 的 7 类,其中 web / button 属危险类型(Q5 默认关闭),
//   加块面板只列 `WidgetRegistry.SafeTypes` 的 5 类安全类型。
//
// 三条硬规矩(与 LayoutProfiles / LayoutJsonEditor 同口径):
//   1) 落盘唯一入口 = LayoutStoreV2(本类只改内存里的 doc,由调用方 Save);
//   2) 校验失败返回中文错误、**不动文档**(不静默吞掉);
//   3) 生成的 id 必须唯一且稳定可读(`w-<yyyyMMddHHmmssfff>-<序号>`),重开/重放不会撞。
public static class LayoutWidgetAdd
{
	/// <summary>页里引用自建块的 id 前缀(与 LayoutEngine.CustomPrefix 同值,此处不重复依赖它)。</summary>
	public const string BlockPrefix = "custom:";

	/// <summary>可以从加块面板添加的类型(顺序即面板顺序):五类安全类型,web / button 不在其中。</summary>
	public static readonly string[] AddableTypes = new string[5]
	{
		LayoutSchema.WidgetNote,
		LayoutSchema.WidgetShortcut,
		LayoutSchema.WidgetClock,
		LayoutSchema.WidgetDivider,
		LayoutSchema.WidgetPlaytime
	};

	/// <summary>`custom:&lt;widgetId&gt;` 形式的块 id。</summary>
	public static string BlockIdOf(string? widgetId)
	{
		return BlockPrefix + (widgetId ?? "");
	}

	/// <summary>类型的中文名(加块面板与 Toast 共用同一份文案)。</summary>
	public static string TypeLabel(string? type)
	{
		return type switch
		{
			LayoutSchema.WidgetNote => I18n.L("便签"),
			LayoutSchema.WidgetShortcut => I18n.L("快捷启动"),
			LayoutSchema.WidgetClock => I18n.L("时钟"),
			LayoutSchema.WidgetDivider => I18n.L("分隔线"),
			LayoutSchema.WidgetPlaytime => I18n.L("游戏时长"),
			LayoutSchema.WidgetWeb => I18n.L("网页(危险)"),
			LayoutSchema.WidgetButton => I18n.L("按钮(危险)"),
			_ => type ?? ""
		};
	}

	/// <summary>「部件清单」那一行的可见文案(枚举给用户看,也是唯一真源 AddableTypes 的投影)。</summary>
	public static string TypeListText()
	{
		List<string> labels = new List<string>(AddableTypes.Length);
		foreach (string type in AddableTypes)
		{
			labels.Add(TypeLabel(type));
		}
		return string.Join(" / ", labels);
	}

	/// <summary>该类型能不能经加块面板添加(危险类型 / 未知类型都不行)。</summary>
	public static bool CanAdd(string? type)
	{
		return !string.IsNullOrEmpty(type) && Array.IndexOf(AddableTypes, type) >= 0;
	}

	/// <summary>新块的默认 settings(各类型只写有意义的字段;空字典也是合法值)。</summary>
	public static Dictionary<string, object?> DefaultSettings(string? type)
	{
		// LayoutJson 是 Core 的内部工具,App 侧只按同一口径(序数比较的字典)构造即可
		Dictionary<string, object?> settings = new Dictionary<string, object?>(StringComparer.Ordinal);
		if (string.Equals(type, LayoutSchema.WidgetClock, StringComparison.Ordinal))
		{
			settings["format"] = "HH:mm";
		}
		return settings;
	}

	/// <summary>现有自建块个数(面板上「已保存 N 个」用它,与文档逐条一致)。</summary>
	public static int Count(LayoutV2? doc)
	{
		return doc?.Layout?.Widgets?.Count ?? 0;
	}

	/// <summary>生成一个文档里没出现过的 widget id(同一毫秒内连点也不会撞)。</summary>
	public static string NewWidgetId(LayoutV2? doc)
	{
		string stem = "w-" + DateTime.Now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
		HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
		if (doc?.Layout?.Widgets != null)
		{
			foreach (WidgetDef widget in doc.Layout.Widgets)
			{
				if (widget != null && !string.IsNullOrEmpty(widget.Id))
				{
					used.Add(widget.Id);
				}
			}
		}
		string id = stem;
		int suffix = 1;
		while (used.Contains(id))
		{
			suffix++;
			id = stem + "-" + suffix.ToString(CultureInfo.InvariantCulture);
		}
		return id;
	}

	/// <summary>
	/// 往文档里加一条自建块定义(只改内存,不落盘)。
	/// 返回 "" = 成功,否则是给用户看的中文错误(此时文档**没有**任何改动)。
	/// </summary>
	public static string AddWidget(LayoutV2? doc, string? type, out WidgetDef? def)
	{
		def = null;
		if (doc?.Layout == null)
		{
			return I18n.L("布局文档不可用");
		}
		if (string.IsNullOrEmpty(type))
		{
			return I18n.L("没有选择部件类型");
		}
		if (!LayoutSchema.IsKnownWidgetType(type))
		{
			return I18n.L("未知的部件类型:{0}", type);
		}
		if (!CanAdd(type))
		{
			return I18n.L("{0}属于危险类型,需在布局 JSON 里显式开启", TypeLabel(type));
		}
		doc.Layout.Widgets ??= new List<WidgetDef>();
		int index = Count(doc) + 1;
		WidgetDef widget = new WidgetDef
		{
			Id = NewWidgetId(doc),
			Type = type,
			Title = I18n.L("自建区块 {0} · {1}", index, TypeLabel(type)),
			Settings = DefaultSettings(type)
		};
		doc.Layout.Widgets.Add(widget);
		def = widget;
		return "";
	}

	/// <summary>页里是否已经有这个块(幂等:重复添加同一 id 不再插第二条)。</summary>
	public static bool HasBlock(PageLayoutV2? page, string? blockId)
	{
		if (page?.Blocks == null || string.IsNullOrEmpty(blockId))
		{
			return false;
		}
		foreach (BlockLayout block in page.Blocks)
		{
			if (block != null && string.Equals(block.Id, blockId, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// 把自建块引用追加到页尾(只改内存,不落盘)。
	/// stack 页:Span = 1(独占一行,与内置块同宽);
	/// grid 页:放到所有块的最下面一条新行、横跨整行(W = 列数),不去挤用户已经摆好的位置。
	/// 返回 "" = 成功,否则中文错误。
	/// </summary>
	public static string AppendBlock(PageLayoutV2? page, string? widgetId)
	{
		if (page == null)
		{
			return I18n.L("目标页面布局不可用");
		}
		if (string.IsNullOrEmpty(widgetId))
		{
			return I18n.L("区块 id 为空");
		}
		page.Blocks ??= new List<BlockLayout>();
		string blockId = BlockIdOf(widgetId);
		if (HasBlock(page, blockId))
		{
			return "";
		}
		BlockLayout block = new BlockLayout
		{
			Id = blockId,
			Span = 1,
			Z = page.Blocks.Count
		};
		if (string.Equals(page.Mode, LayoutSchema.ModeGrid, StringComparison.Ordinal))
		{
			double bottom = 0.0;
			foreach (BlockLayout old in page.Blocks)
			{
				if (old != null)
				{
					bottom = Math.Max(bottom, old.Y + old.H);
				}
			}
			block.X = 0.0;
			block.Y = bottom;
			block.W = Math.Max(1.0, page.Cols);
			block.H = 1.0;
		}
		page.Blocks.Add(block);
		return "";
	}
}