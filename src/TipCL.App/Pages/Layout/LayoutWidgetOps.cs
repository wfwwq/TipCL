// R32-LINMORE:自建区块的**纯数据操作**(零 UI 依赖),供设置页「布局 → 自建区块」与测试共用。
// 背景:Windows 冻结件里「添加自建区块」按钮是永久禁用的(SettingsLayoutPage.cs:443 `add.IsEnabled = false`,
// L6 未落地),所以 Windows 侧没有可移植的"加块"实现;Linux 侧按任务书要求补齐,这里把可测的部分抽出来,
// 让「枚举部件 → 添加定义 → 接到页面块列表 → 删除」这条链在 net8 测试宿主里能逐条断言。
using System;
using System.Collections.Generic;
using TipCL.Core.Launcher.Layout;

namespace TipCL.App.Pages.Layout;

/// <summary>自建区块(§4.F)的数据操作:只改传入的文档对象,落盘由调用方走 LayoutStoreV2。</summary>
public static class LayoutWidgetOps
{
	/// <summary>块引用 id(custom:&lt;widgetId&gt;)。</summary>
	public static string BlockIdOf(string widgetId)
	{
		return LayoutEngine.CustomPrefix + (widgetId ?? "");
	}

	/// <summary>
	/// 追加一个自建区块定义(首期安全类型里的一种),返回新的 widget id。
	/// 类型必须是 <see cref="WidgetRegistry.SafeTypes"/> 之一(web / button 属危险类型,要显式开启,不在此列)。
	/// </summary>
	public static string AddWidget(LayoutV2? doc, string type)
	{
		if (doc == null || doc.Layout == null)
		{
			throw new ArgumentNullException(nameof(doc));
		}
		if (Array.IndexOf(WidgetRegistry.SafeTypes, type) < 0)
		{
			throw new ArgumentException("不支持的部件类型:" + type, nameof(type));
		}
		WidgetDef def = new WidgetDef
		{
			Id = LayoutSchema.NewWidgetId(),
			Type = type,
			Title = WidgetUI.TypeLabel(type)
		};
		// 首期类型的默认参数(便签留空 → 内容区显示一句引导;时钟给默认格式)
		if (string.Equals(type, LayoutSchema.WidgetClock, StringComparison.Ordinal))
		{
			def.Settings["format"] = "HH:mm";
		}
		doc.Layout.Widgets.Add(def);
		return def.Id;
	}

	/// <summary>把块引用追加到页面布局末尾(已在列表里则不重复),Z 取当前块数。</summary>
	public static bool AppendBlock(PageLayoutV2? page, string blockId)
	{
		if (page == null || string.IsNullOrEmpty(blockId))
		{
			return false;
		}
		foreach (BlockLayout block in page.Blocks)
		{
			if (block != null && string.Equals(block.Id, blockId, StringComparison.Ordinal))
			{
				return false;
			}
		}
		page.Blocks.Add(new BlockLayout
		{
			Id = blockId,
			Span = 1,
			Z = page.Blocks.Count
		});
		return true;
	}

	/// <summary>删除自建区块定义 + 所有页面里对它的引用;返回被删掉的块引用个数。</summary>
	public static int RemoveWidget(LayoutV2? doc, string widgetId)
	{
		if (doc == null || doc.Layout == null || string.IsNullOrEmpty(widgetId))
		{
			return 0;
		}
		string blockId = BlockIdOf(widgetId);
		int removed = StripBody(doc.Layout, widgetId, blockId);
		if (doc.Profiles != null)
		{
			foreach (LayoutBody profile in doc.Profiles.Values)
			{
				removed += StripBody(profile, widgetId, blockId);
			}
		}
		return removed;
	}

	// 当前布局、方案、响应式变体都可能引用同一个自建块。只扫当前 pages 会把方案里的引用留下。
	// 返回值仍是删掉的块引用数（不计定义本身），与原来的测试口径一致。
	private static int StripBody(LayoutBody? body, string widgetId, string blockId)
	{
		if (body == null)
		{
			return 0;
		}
		if (body.Widgets != null)
		{
			body.Widgets.RemoveAll(w => w == null || string.Equals(w.Id, widgetId, StringComparison.Ordinal));
		}
		int removed = StripPages(body.Pages, blockId);
		if (body.Variants != null)
		{
			foreach (LayoutBody variant in body.Variants.Values)
			{
				if (variant == null)
				{
					continue;
				}
				if (variant.Widgets != null)
				{
					variant.Widgets.RemoveAll(w => w == null || string.Equals(w.Id, widgetId, StringComparison.Ordinal));
				}
				removed += StripPages(variant.Pages, blockId);
			}
		}
		return removed;
	}

	private static int StripPages(Dictionary<string, PageLayoutV2>? pages, string blockId)
	{
		if (pages == null)
		{
			return 0;
		}
		int removed = 0;
		foreach (PageLayoutV2 page in pages.Values)
		{
			if (page == null || page.Blocks == null)
			{
				continue;
			}
			removed += page.Blocks.RemoveAll(b => b != null && string.Equals(b.Id, blockId, StringComparison.Ordinal));
		}
		return removed;
	}

	/// <summary>当前文档里定义的自建区块(按文档顺序,过滤 null)。</summary>
	public static List<WidgetDef> Widgets(LayoutV2? doc)
	{
		List<WidgetDef> list = new List<WidgetDef>();
		if (doc == null || doc.Layout == null || doc.Layout.Widgets == null)
		{
			return list;
		}
		foreach (WidgetDef widget in doc.Layout.Widgets)
		{
			if (widget != null)
			{
				list.Add(widget);
			}
		}
		return list;
	}
}