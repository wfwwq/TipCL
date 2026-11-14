using System;
using System.Collections.Generic;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Pages.Layout;

// 布局引擎(纯模型层:禁 WPF、禁界面依赖,net8 测试宿主可直接加载):
//   输入 = 页面 v2 条目(可能没有)+ 页面自己的块白名单(Id + DefaultVisible);
//   输出 = 排布计划(可视顺序 + 行 / 列位 + span),供 WPF 宿主渲染与命中使用。
// 与旧实现的等价性(硬约束 D-11):默认文档(没有 v2 条目)下
//   `LayoutPageState.Load` 的 Order/Hidden 与冻结的 `PageLayoutDoc.Default(ids, defaultVisible)` 逐项相等,
//   并且单列(stack 默认)时每个可见块独占一行 → 宿主走与改动前完全相同的渲染路径。

/// <summary>页面交给引擎的块白名单项(纯模型:不携带 UIElement,便于 net8 单测)。</summary>
public sealed class LayoutBlockRef
{
	/// <summary>块 id(稳定英文 id;可以是内置块,也可以是 <c>custom:&lt;widgetId&gt;</c> / <c>plugin:&lt;plugin&gt;:&lt;blockId&gt;</c>)。</summary>
	public string Id { get; set; } = "";

	/// <summary>默认是否可见(等价 BlockSpec.DefaultVisible;默认布局按它决定 hidden)。</summary>
	public bool DefaultVisible { get; set; } = true;

	/// <summary>按 id 与默认可见性构造。</summary>
	public LayoutBlockRef()
	{
	}

	/// <summary>按 id 与默认可见性构造。</summary>
	public LayoutBlockRef(string id, bool defaultVisible)
	{
		Id = id ?? "";
		DefaultVisible = defaultVisible;
	}
}

/// <summary>块 id 的命名空间分类(§3.2 的兼容命名空间一览)。</summary>
public enum LayoutContentKind
{
	/// <summary>内置块(页面自己构建内容)。</summary>
	Builtin,

	/// <summary>用户自建区块:<c>custom:&lt;widgetId&gt;</c>(§4.F)。</summary>
	Custom,

	/// <summary>插件区块:<c>plugin:&lt;plugin&gt;:&lt;blockId&gt;</c>(§4.F)。</summary>
	Plugin
}

/// <summary>
/// 内容工厂:按块 id 造内容。返回 object(由 WPF 侧强转 UIElement),这样引擎层保持零 WPF 依赖;
/// L6 只要实现本接口并 <see cref="LayoutContentRegistry.Register"/> 即可,不需要再改 BlockHost。
/// </summary>
public interface ILayoutContentProvider
{
	/// <summary>能否为本块 id 造内容(实现方自己判定,通常用命名空间前缀 + 自己的注册表)。</summary>
	bool CanBuild(string blockId);

	/// <summary>造内容;失败返回 null(宿主会显示「不可用」占位)。</summary>
	object? Build(string blockId, string pageId);
}

/// <summary>
/// 内容工厂注册表(§4.F 的口子)。查不到工厂的自建/插件块**不删数据**,由宿主显示「不可用」。
/// </summary>
public static class LayoutContentRegistry
{
	private static readonly object _gate = new object();

	private static readonly List<ILayoutContentProvider> _providers = new List<ILayoutContentProvider>();

	/// <summary>已注册工厂数(测试与诊断用)。</summary>
	public static int Count
	{
		get
		{
			lock (_gate)
			{
				return _providers.Count;
			}
		}
	}

	/// <summary>注册工厂(重复注册同一个实例会被忽略)。</summary>
	public static void Register(ILayoutContentProvider provider)
	{
		if (provider == null)
		{
			return;
		}
		lock (_gate)
		{
			foreach (ILayoutContentProvider item in _providers)
			{
				if (ReferenceEquals(item, provider))
				{
					return;
				}
			}
			_providers.Add(provider);
		}
	}

	/// <summary>注销工厂。</summary>
	public static void Unregister(ILayoutContentProvider provider)
	{
		if (provider == null)
		{
			return;
		}
		lock (_gate)
		{
			_providers.Remove(provider);
		}
	}

	/// <summary>清空注册表(测试用;生产代码不要调)。</summary>
	public static void Clear()
	{
		lock (_gate)
		{
			_providers.Clear();
		}
	}

	/// <summary>是否有工厂能造这个块。</summary>
	public static bool HasProvider(string blockId)
	{
		if (string.IsNullOrEmpty(blockId))
		{
			return false;
		}
		lock (_gate)
		{
			foreach (ILayoutContentProvider provider in _providers)
			{
				try
				{
					if (provider.CanBuild(blockId))
					{
						return true;
					}
				}
				catch
				{
					// 插件工厂自己的异常不能拖垮渲染
				}
			}
		}
		return false;
	}

	/// <summary>造内容;没有工厂或工厂失败返回 null。</summary>
	public static object? Build(string blockId, string pageId)
	{
		if (string.IsNullOrEmpty(blockId))
		{
			return null;
		}
		lock (_gate)
		{
			foreach (ILayoutContentProvider provider in _providers)
			{
				try
				{
					if (!provider.CanBuild(blockId))
					{
						continue;
					}
					return provider.Build(blockId, pageId);
				}
				catch
				{
					return null;
				}
			}
		}
		return null;
	}
}

/// <summary>一个块在排布计划里的槽位(顺序即显示顺序,隐藏项也占位 —— 与 v1 语义一致)。</summary>
public sealed class LayoutSlot
{
	/// <summary>块 id。</summary>
	public string Id { get; set; } = "";

	/// <summary>是否隐藏。</summary>
	public bool Hidden { get; set; }

	/// <summary>占几列(1..计划列数)。</summary>
	public int Span { get; set; } = 1;

	/// <summary>所在行内起始列(0-based,按 span 累加)。</summary>
	public int Column { get; set; }

	/// <summary>所在行号(仅可见块有意义)。</summary>
	public int RowIndex { get; set; } = -1;

	/// <summary>完整顺序里的下标(含隐藏项)。</summary>
	public int SlotIndex { get; set; }

	/// <summary>可视顺序里的下标(隐藏项为 -1)。</summary>
	public int VisualIndex { get; set; } = -1;
}

/// <summary>一行(单列时每行恰好一个可见块 —— 与旧版纵向流等价)。</summary>
public sealed class LayoutRow
{
	/// <summary>本行的可见块(按显示顺序)。</summary>
	public List<LayoutSlot> Slots { get; } = new List<LayoutSlot>();

	/// <summary>本行占用的列数合计。</summary>
	public int SpanSum { get; set; }
}

/// <summary>单页排布计划(引擎输出)。</summary>
public sealed class LayoutPlan
{
	/// <summary>页面 id。</summary>
	public string PageId { get; set; } = "";

	/// <summary>页面模式(v2 原值;L1 只渲染 stack,其余模式按单列兜底)。</summary>
	public string Mode { get; set; } = LayoutSchema.ModeStack;

	/// <summary>stack 模式的有效列数(1 = 单列 = 与旧版完全一致的纵向流)。</summary>
	public int Columns { get; set; } = 1;

	/// <summary>块间距(默认 10,与旧版 BlockHost.Gap 相同)。</summary>
	public double Gap { get; set; } = LayoutSchema.GapDefault;

	/// <summary>内容边距(默认 18,14,18,16,与旧版相同)。</summary>
	public double[] Pad { get; set; } = LayoutSchema.PadDefaultCopy();

	/// <summary>固定行高(0 = 自适应;L1 不做行高,保留原值待 L2 使用)。</summary>
	public double RowH { get; set; }

	/// <summary>完整顺序(含隐藏项);顺序即显示顺序。</summary>
	public List<LayoutSlot> Slots { get; } = new List<LayoutSlot>();

	/// <summary>行(只含可见块)。</summary>
	public List<LayoutRow> Rows { get; } = new List<LayoutRow>();

	/// <summary>可视顺序的块 id。</summary>
	public List<string> VisibleIds { get; } = new List<string>();

	/// <summary>是否单列(= 默认,渲染走与旧版逐像素相同的路径)。</summary>
	public bool IsSingleColumn
	{
		get
		{
			return Columns <= 1;
		}
	}

	/// <summary>取某个块的槽位;不存在返回 null。</summary>
	public LayoutSlot? Slot(string id)
	{
		if (string.IsNullOrEmpty(id))
		{
			return null;
		}
		foreach (LayoutSlot slot in Slots)
		{
			if (string.Equals(slot.Id, id, StringComparison.Ordinal))
			{
				return slot;
			}
		}
		return null;
	}

	/// <summary>可视顺序里某块的下标(不存在或隐藏返回 -1)。</summary>
	public int VisualIndexOf(string id)
	{
		for (int i = 0; i < VisibleIds.Count; i++)
		{
			if (string.Equals(VisibleIds[i], id, StringComparison.Ordinal))
			{
				return i;
			}
		}
		return -1;
	}
}

/// <summary>
/// 单页的编辑状态(纯模型):完整顺序(含隐藏项)+ 隐藏集合 + span + 页级参数。
/// 宿主(BlockHost/LayoutEditor)只持有它,顺序/显隐/span 全在这里变更,再交给引擎算排布、交给 LayoutStoreV2 落盘。
/// </summary>
public sealed class LayoutPageState
{
	/// <summary>页面 id。</summary>
	public string PageId { get; private set; } = "";

	/// <summary>页级参数(mode/cols/row_h/gap/pad);Blocks 由 <see cref="RebuildPage"/> 同步。</summary>
	public PageLayoutV2 Page { get; private set; } = new PageLayoutV2();

	/// <summary>完整顺序(含隐藏项)。</summary>
	public List<string> Order { get; private set; } = new List<string>();

	/// <summary>隐藏集合。</summary>
	public HashSet<string> Hidden { get; private set; } = new HashSet<string>(StringComparer.Ordinal);

	/// <summary>每块 span(1..列数)。</summary>
	public Dictionary<string, int> Spans { get; private set; } = new Dictionary<string, int>(StringComparer.Ordinal);

	/// <summary>v2 文档里是否本来就有这一页(false = 用页面白名单生成的默认布局)。</summary>
	public bool ExistedInDoc { get; private set; }

	/// <summary>
	/// 装载一页:v2 里有这一页就按它(未知 id 丢弃、缺失 id 按 DefaultVisible 补到末尾,语义等价
	/// PageLayoutDoc.Normalize);没有就用页面白名单生成默认布局(语义等价 PageLayoutDoc.Default)。
	/// </summary>
	public static LayoutPageState Load(string pageId, PageLayoutV2? page, IReadOnlyList<LayoutBlockRef> refs)
	{
		LayoutPageState state = new LayoutPageState
		{
			PageId = pageId ?? "",
			Page = ((page != null) ? page.Clone() : new PageLayoutV2()),
			ExistedInDoc = page != null
		};
		Dictionary<string, LayoutBlockRef> known = new Dictionary<string, LayoutBlockRef>(StringComparer.Ordinal);
		if (refs != null)
		{
			foreach (LayoutBlockRef reference in refs)
			{
				if (reference != null && !string.IsNullOrEmpty(reference.Id))
				{
					known[reference.Id] = reference;
				}
			}
		}
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		if (page != null && page.Blocks != null)
		{
			foreach (BlockLayout block in page.Blocks)
			{
				if (block == null || string.IsNullOrEmpty(block.Id) || !seen.Add(block.Id))
				{
					continue;
				}
				// 内置块必须落在页面白名单里(未知内置 id 丢弃);
				// custom: / plugin: 是布局文档自己的块(§4.F),页面 spec 里可能没有 —— 保留,交给内容工厂或「不可用」占位
				if (!known.ContainsKey(block.Id) && !LayoutEngine.IsImplicitRef(block.Id))
				{
					continue;
				}
				state.Order.Add(block.Id);
				if (block.Hidden)
				{
					state.Hidden.Add(block.Id);
				}
				if (block.Span > 1)
				{
					state.Spans[block.Id] = block.Span;
				}
			}
		}
		// 补齐缺失块:顺着白名单顺序追加到末尾,Hidden = !DefaultVisible(与 PageLayoutDoc.Normalize 一致)
		if (refs != null)
		{
			foreach (LayoutBlockRef reference in refs)
			{
				if (reference == null || string.IsNullOrEmpty(reference.Id) || !seen.Add(reference.Id))
				{
					continue;
				}
				state.Order.Add(reference.Id);
				if (!reference.DefaultVisible)
				{
					state.Hidden.Add(reference.Id);
				}
			}
		}
		state.RebuildPage();
		return state;
	}

	/// <summary>按白名单重建默认布局(= 重置本页;语义等价 PageLayoutDoc.Default)。
	/// 文档里的 custom: / plugin: 块**不删**,按原顺序接到末尾(数据保留,删除入口在 L6 的区块管理里)。</summary>
	public void Reset(IReadOnlyList<LayoutBlockRef> refs)
	{
		List<string> extra = new List<string>();
		foreach (string id in Order)
		{
			if (LayoutEngine.IsImplicitRef(id))
			{
				extra.Add(id);
			}
		}
		Order = new List<string>();
		Hidden = new HashSet<string>(StringComparer.Ordinal);
		Spans = new Dictionary<string, int>(StringComparer.Ordinal);
		if (refs != null)
		{
			foreach (LayoutBlockRef reference in refs)
			{
				if (reference == null || string.IsNullOrEmpty(reference.Id) || Order.Contains(reference.Id))
				{
					continue;
				}
				Order.Add(reference.Id);
				if (!reference.DefaultVisible)
				{
					Hidden.Add(reference.Id);
				}
			}
		}
		foreach (string id2 in extra)
		{
			if (!Order.Contains(id2))
			{
				Order.Add(id2);
			}
		}
		RebuildPage();
	}

	/// <summary>隐藏一个块(隐藏项留在原槽位,恢复时回到原位置)。</summary>
	public void Hide(string id)
	{
		if (!string.IsNullOrEmpty(id) && Order.Contains(id))
		{
			Hidden.Add(id);
			RebuildPage();
		}
	}

	/// <summary>恢复一个块的显示。</summary>
	public void Restore(string id)
	{
		if (!string.IsNullOrEmpty(id) && Hidden.Contains(id))
		{
			Hidden.Remove(id);
			RebuildPage();
		}
	}

	/// <summary>可视顺序里某块的下标(隐藏或不存在返回 -1)。</summary>
	public int VisibleIndexOf(string id)
	{
		int index = 0;
		foreach (string item in Order)
		{
			if (string.Equals(item, id, StringComparison.Ordinal))
			{
				return Hidden.Contains(item) ? -1 : index;
			}
			if (!Hidden.Contains(item))
			{
				index++;
			}
		}
		return -1;
	}

	/// <summary>可视块 id 列表。</summary>
	public List<string> VisibleIds()
	{
		List<string> list = new List<string>();
		foreach (string item in Order)
		{
			if (!Hidden.Contains(item))
			{
				list.Add(item);
			}
		}
		return list;
	}

	/// <summary>
	/// 一维重排(移植自旧 BlockHost 的拖拽算法):把 moving 视作被拖动块,pointerY 是相对列表顶部的坐标,
	/// 逐格与"相邻可见块中点"比较直到不再需要交换。隐藏项保持相对槽位。
	/// </summary>
	public void DragReorder(string movingId, double pointerY, IReadOnlyDictionary<string, double>? heights, double fallbackHeight = 120.0)
	{
		if (string.IsNullOrEmpty(movingId) || VisibleIndexOf(movingId) < 0)
		{
			return;
		}
		List<string> visible = VisibleIds();
		int index = visible.IndexOf(movingId);
		while (index > 0 && pointerY < MidpointY(visible, heights, Page.Gap, index - 1, fallbackHeight))
		{
			MoveVisible(visible, index, -1);
			index--;
		}
		while (index < visible.Count - 1 && pointerY > MidpointY(visible, heights, Page.Gap, index + 1, fallbackHeight))
		{
			MoveVisible(visible, index, 1);
			index++;
		}
		RebuildPage();
	}

	/// <summary>可视序号 k 的块中点 Y(相对列表顶部,含 gap;高度取缓存,缺省 120)。</summary>
	public static double MidpointY(IReadOnlyList<string> visible, IReadOnlyDictionary<string, double>? heights, double gap, int k, double fallbackHeight = 120.0)
	{
		double y = 0.0;
		for (int i = 0; i < visible.Count && i <= k; i++)
		{
			double h = fallbackHeight;
			if (heights != null && heights.TryGetValue(visible[i], out double cached) && cached > 1.0)
			{
				h = cached;
			}
			if (i == k)
			{
				return y + h / 2.0;
			}
			y += h + gap;
		}
		return y;
	}

	/// <summary>把 Order/Hidden/Spans 同步进 Page.Blocks(顺序即顺序,隐藏项也占位)。</summary>
	public void RebuildPage()
	{
		List<BlockLayout> blocks = new List<BlockLayout>();
		for (int i = 0; i < Order.Count; i++)
		{
			string id = Order[i];
			blocks.Add(new BlockLayout
			{
				Id = id,
				Hidden = Hidden.Contains(id),
				Span = (Spans.TryGetValue(id, out int span) ? span : 1),
				Z = i
			});
		}
		Page.Blocks = blocks;
	}

	/// <summary>取页级参数齐全的 PageLayoutV2 副本(用于写回 v2 文档)。</summary>
	public PageLayoutV2 ToPage()
	{
		PageLayoutV2 page = Page.Clone();
		RebuildPage();
		page.Blocks = new List<BlockLayout>(Page.Blocks);
		return page;
	}

	/// <summary>算排布计划。</summary>
	public LayoutPlan Plan()
	{
		RebuildPage();
		return LayoutEngine.Plan(PageId, Page);
	}

	// 交换相邻两位:与旧 BlockHost.MoveVisible 完全一致(_order 里插到目标可见块旁边)
	private void MoveVisible(List<string> visible, int k, int dir)
	{
		string moving = visible[k];
		string other = visible[k + dir];
		Order.Remove(moving);
		int at = Order.IndexOf(other);
		Order.Insert(((dir > 0) ? (at + 1) : at), moving);
		visible.RemoveAt(k);
		visible.Insert(k + dir, moving);
	}
}

/// <summary>布局引擎:把"页面 v2 条目 + 白名单"变成排布计划,并回写顺序/显隐/span。</summary>
public static class LayoutEngine
{
	/// <summary>用户自建区块的 id 前缀(<c>custom:&lt;widgetId&gt;</c>)。</summary>
	public const string CustomPrefix = "custom:";

	/// <summary>插件区块的 id 前缀(<c>plugin:&lt;plugin&gt;:&lt;blockId&gt;</c>)。</summary>
	public const string PluginPrefix = "plugin:";

	/// <summary>块 id 的命名空间分类。</summary>
	public static LayoutContentKind Classify(string? blockId)
	{
		if (string.IsNullOrEmpty(blockId))
		{
			return LayoutContentKind.Builtin;
		}
		if (blockId.StartsWith(CustomPrefix, StringComparison.Ordinal))
		{
			return LayoutContentKind.Custom;
		}
		if (blockId.StartsWith(PluginPrefix, StringComparison.Ordinal))
		{
			return LayoutContentKind.Plugin;
		}
		return LayoutContentKind.Builtin;
	}

	/// <summary>取自建区块引用的 widgetId(<c>custom:&lt;widgetId&gt;</c>);不是自建块返回 null。</summary>
	public static string? WidgetIdOf(string? blockId)
	{
		if (string.IsNullOrEmpty(blockId) || !blockId.StartsWith(CustomPrefix, StringComparison.Ordinal))
		{
			return null;
		}
		string id = blockId.Substring(CustomPrefix.Length);
		return ((id.Length > 0) ? id : null);
	}

	/// <summary>拆插件块 id:<c>plugin:&lt;plugin&gt;:&lt;blockId&gt;</c> → (plugin, blockId);不是插件块返回 false。</summary>
	public static bool TryParsePluginId(string? blockId, out string plugin, out string inner)
	{
		plugin = "";
		inner = "";
		if (string.IsNullOrEmpty(blockId) || !blockId.StartsWith(PluginPrefix, StringComparison.Ordinal))
		{
			return false;
		}
		string rest = blockId.Substring(PluginPrefix.Length);
		int sep = rest.IndexOf(':');
		if (sep <= 0 || sep >= rest.Length - 1)
		{
			return false;
		}
		plugin = rest.Substring(0, sep);
		inner = rest.Substring(sep + 1);
		return true;
	}

	/// <summary>
	/// 是否"布局文档自己的块"(custom: / plugin:):页面 spec 白名单里可以没有它,引擎与宿主都要保留。
	/// </summary>
	public static bool IsImplicitRef(string? blockId)
	{
		return Classify(blockId) != LayoutContentKind.Builtin;
	}

	/// <summary>
	/// 自建块/插件块且当前没有内容工厂能造它 = 「不可用」(数据保留、只显示占位)。
	/// 内置块(页面自己给内容)永远不算不可用。
	/// </summary>
	public static bool IsUnavailable(string? blockId)
	{
		if (Classify(blockId) == LayoutContentKind.Builtin)
		{
			return false;
		}
		return !LayoutContentRegistry.HasProvider(blockId ?? "");
	}

	/// <summary>
	/// stack 模式的有效列数:只有显式声明 cols ∈ {2,3} 时才多列,其余(含默认 cols=12)一律单列。
	/// 这条规则保证"默认文档 → 单列纵向流 → 与改动前逐像素等价";grid/canvas 由 L2 接引擎的另外两个模式,
	/// L1 先按单列兜底渲染(不丢字段:mode/cols 原样保留在文档里)。
	/// </summary>
	public static int StackColumns(PageLayoutV2? page)
	{
		if (page == null || page.Mode != LayoutSchema.ModeStack)
		{
			return 1;
		}
		if (page.Cols == 2 || page.Cols == 3)
		{
			return page.Cols;
		}
		return 1;
	}

	/// <summary>装载一页状态(v2 条目可能为 null = 用白名单默认布局)。</summary>
	public static LayoutPageState LoadState(string pageId, PageLayoutV2? page, IReadOnlyList<LayoutBlockRef> refs)
	{
		return LayoutPageState.Load(pageId, page, refs);
	}

	/// <summary>算排布计划(行 / 列位 / 可视顺序)。</summary>
	public static LayoutPlan Plan(string pageId, PageLayoutV2? page)
	{
		LayoutPlan plan = new LayoutPlan
		{
			PageId = pageId ?? "",
			Mode = ((page != null) ? page.Mode : LayoutSchema.ModeStack),
			Columns = StackColumns(page),
			Gap = ((page != null) ? page.Gap : LayoutSchema.GapDefault),
			Pad = LayoutSchema.PadCopy((page != null) ? page.Pad : null),
			RowH = ((page != null) ? page.RowH : 0.0)
		};
		int columns = plan.Columns;
		int slotIndex = 0;
		LayoutRow? row = null;
		if (page != null && page.Blocks != null)
		{
			foreach (BlockLayout block in page.Blocks)
			{
				if (block == null || string.IsNullOrEmpty(block.Id))
				{
					continue;
				}
				LayoutSlot slot = new LayoutSlot
				{
					Id = block.Id,
					Hidden = block.Hidden,
					Span = LayoutSchema.ClampInt(block.Span, 1, columns),
					SlotIndex = slotIndex++
				};
				plan.Slots.Add(slot);
				if (block.Hidden)
				{
					continue;
				}
				if (row == null || (row.Slots.Count > 0 && row.SpanSum + slot.Span > columns))
				{
					row = new LayoutRow();
					plan.Rows.Add(row);
				}
				slot.RowIndex = plan.Rows.Count - 1;
				slot.Column = row.SpanSum;
				slot.VisualIndex = plan.VisibleIds.Count;
				row.SpanSum += slot.Span;
				row.Slots.Add(slot);
				plan.VisibleIds.Add(slot.Id);
			}
		}
		return plan;
	}

	/// <summary>把一页状态写回 v2 文档(没有该页就新建;不动别的页)。</summary>
	public static void ApplyPage(LayoutV2 doc, string pageId, PageLayoutV2 page)
	{
		if (doc == null || string.IsNullOrEmpty(pageId) || page == null)
		{
			return;
		}
		if (doc.Layout == null)
		{
			doc.Layout = new LayoutBody();
		}
		doc.Layout.Pages[pageId] = page;
	}

	/// <summary>把"顺序 + 显隐 + span"写成 v1 子集口径的块列表(顺序即顺序,隐藏项也占位)。</summary>
	public static List<BlockLayout> BuildBlocks(IReadOnlyList<string> order, ICollection<string>? hidden, IReadOnlyDictionary<string, int>? spans)
	{
		List<BlockLayout> blocks = new List<BlockLayout>();
		if (order == null)
		{
			return blocks;
		}
		for (int i = 0; i < order.Count; i++)
		{
			string id = order[i];
			if (string.IsNullOrEmpty(id))
			{
				continue;
			}
			blocks.Add(new BlockLayout
			{
				Id = id,
				Hidden = (hidden != null && hidden.Contains(id)),
				Span = ((spans != null && spans.TryGetValue(id, out int span)) ? span : 1),
				Z = i
			});
		}
		return blocks;
	}

	/// <summary>
	/// 把 v1 单页的 <c>{id,hidden}</c> 与顺序并进 v2 页(导入 v1 语义文件的兼容路径):
	/// 顺序与显隐以 v1 为准;同一个块在 v2 里的其它字段(span / style / settings...)按 id 保留;
	/// v2 里有而 v1 里没有的块按原顺序接到末尾(不丢数据)。
	/// 实现只有一份 —— Core 的 <see cref="LayoutStoreV2.MergeV1Order"/>(那边不认识冻结的 v1 类型,
	/// 所以这里先把 v1 文档转成 Core 的朴素块列表)。
	/// </summary>
	public static PageLayoutV2 MergeV1(PageLayoutV2? existing, PageLayoutDoc? v1)
	{
		return LayoutStoreV2.MergeV1Order(existing, LayoutV1Projection.ToCoreBlocks(v1));
	}
}

// ═══════════════════════════════════════════════════════════════════════════
// L2:grid / canvas 两种画布的纯几何 + 区块外观参数 + 响应式变体 + 编辑会话
//
// 本节**全部零 WPF 依赖**(与文件上半部分同一条纪律):WPF 侧的 GridCanvasPanel / GuideLayer /
// LayoutEditor 只做可视树组装与鼠标手势,坐标换算、自动流、行高自适应、吸附、命中、8 向手柄、
// 对齐辅助线、撤销栈、多选集合、剪贴板、键盘意图全在这里 —— 这样 net8 测试宿主能直接断言
// (见 tests/TipCL.Tests/LayoutCanvasTests.cs 与 LayoutEditorTests.cs)。
//
// 与默认渲染的关系(硬约束 D-11):本节所有函数**只在 mode=grid/canvas 时被调用**;
// mode=stack(缺省)走的是文件上半部分那条老路径,一个像素都不经过这里。
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>纯矩形(引擎层不引 WPF,不能用 <c>System.Windows.Rect</c>)。</summary>
public struct LayoutRect
{
	/// <summary>左上角 X(相对画布内容区,不含页边距)。</summary>
	public double X { get; set; }

	/// <summary>左上角 Y(相对画布内容区)。</summary>
	public double Y { get; set; }

	/// <summary>宽。</summary>
	public double W { get; set; }

	/// <summary>高。</summary>
	public double H { get; set; }

	/// <summary>按左上角与尺寸构造。</summary>
	public LayoutRect(double x, double y, double w, double h)
	{
		X = x;
		Y = y;
		W = w;
		H = h;
	}

	/// <summary>右边界。</summary>
	public double Right
	{
		get
		{
			return X + W;
		}
	}

	/// <summary>下边界。</summary>
	public double Bottom
	{
		get
		{
			return Y + H;
		}
	}

	/// <summary>水平中线。</summary>
	public double CenterX
	{
		get
		{
			return X + W / 2.0;
		}
	}

	/// <summary>垂直中线。</summary>
	public double CenterY
	{
		get
		{
			return Y + H / 2.0;
		}
	}

	/// <summary>点是否落在矩形内(含边界)。</summary>
	public bool Contains(double x, double y)
	{
		return x >= X && x <= Right && y >= Y && y <= Bottom;
	}

	/// <summary>调试用文本。</summary>
	public override string ToString()
	{
		return "[" + X + "," + Y + " " + W + "x" + H + "]";
	}
}

/// <summary>
/// 画布里的一个块槽位:grid 用网格单位(X/Y = 列/行,W/H = 列/行跨度),canvas 用归一化矩形(N*)。
/// 两套坐标同时装在同一个 cell 上 —— 同一份文档在 grid/canvas 之间来回切模式时,**两边的位置都不丢**。
/// </summary>
public sealed class LayoutCell
{
	/// <summary>块 id。</summary>
	public string Id { get; set; } = "";

	/// <summary>grid 起始列(0..cols-W)。</summary>
	public int X { get; set; }

	/// <summary>grid 起始行(≥0,行数自适应增长)。</summary>
	public int Y { get; set; }

	/// <summary>grid 列跨度(1..cols)。</summary>
	public int W { get; set; } = 1;

	/// <summary>grid 行跨度(1..cols)。</summary>
	public int H { get; set; } = 1;

	/// <summary>canvas 归一化左(0..1)。</summary>
	public double NX { get; set; }

	/// <summary>canvas 归一化上(0..1)。</summary>
	public double NY { get; set; }

	/// <summary>canvas 归一化宽(0.04..1)。</summary>
	public double NW { get; set; } = 1.0;

	/// <summary>canvas 归一化高(0.04..1)。</summary>
	public double NH { get; set; } = 1.0;

	/// <summary>缩放下限宽(px;来自块类型默认,兜底 200)。</summary>
	public double MinW { get; set; } = LayoutCanvas.FallbackMinW;

	/// <summary>缩放下限高(px;来自 <c>blocks[].min_h</c> + 类型默认,兜底 120)。</summary>
	public double MinH { get; set; } = LayoutCanvas.FallbackMinH;

	/// <summary>叠放序(canvas 用;顺序索引)。</summary>
	public int Z { get; set; }

	/// <summary>深拷贝。</summary>
	public LayoutCell Clone()
	{
		return new LayoutCell
		{
			Id = Id,
			X = X,
			Y = Y,
			W = W,
			H = H,
			NX = NX,
			NY = NY,
			NW = NW,
			NH = NH,
			MinW = MinW,
			MinH = MinH,
			Z = Z
		};
	}

	/// <summary>取类型默认最小尺寸:块级 <c>min_h</c> 覆盖高,宽用兜底 200(契约没有 min_w)。</summary>
	public static LayoutCell FromBlock(BlockLayout? block)
	{
		LayoutCell cell = new LayoutCell();
		if (block == null)
		{
			return cell;
		}
		cell.Id = block.Id;
		cell.X = Math.Max(0, (int)Math.Round(block.X));
		cell.Y = Math.Max(0, (int)Math.Round(block.Y));
		cell.W = Math.Max(1, (int)Math.Round(block.W));
		cell.H = Math.Max(1, (int)Math.Round(block.H));
		cell.NX = block.X;
		cell.NY = block.Y;
		cell.NW = block.W;
		cell.NH = block.H;
		cell.MinW = LayoutCanvas.FallbackMinW;
		cell.MinH = ((block.MinH > 0.0) ? block.MinH : LayoutCanvas.FallbackMinH);
		cell.Z = block.Z;
		return cell;
	}

	/// <summary>把网格坐标写回块(只动 x/y/w/h,不动 span —— span 是 stack 模式的字段)。</summary>
	public void ApplyGrid(BlockLayout? block)
	{
		if (block != null)
		{
			block.X = X;
			block.Y = Y;
			block.W = W;
			block.H = H;
		}
	}

	/// <summary>把归一化矩形写回块。</summary>
	public void ApplyCanvas(BlockLayout? block)
	{
		if (block != null)
		{
			block.X = NX;
			block.Y = NY;
			block.W = NW;
			block.H = NH;
		}
	}
}

/// <summary>
/// grid 模式的纯几何(§4.A):cols(2/3/4/6/12)× 自动行,块可跨列跨行。
/// 三条规则:
///   1) **没被画布编辑过的块自动流**(判定 = 契约默认矩形 0,0,1,1):按顺序填满第一行再换行 —— 于是
///      "把一页切成 grid" 的初始结果就是顺序铺满,不会有块堆在 (0,0) 互相压住;
///   2) 显式声明过位置的块按原位放;越界或重叠时先在**同列向下**找第一个空位,再全网格找空位(不丢用户位置);
///   3) 切到 grid 时 stack 的 <c>span</c> 延续成列跨度(W==1 且 span&gt;1 时),用户已有的跨列不丢。
/// </summary>
public static class LayoutGrid
{
	/// <summary>grid 模式的有效列数(只认契约档位 {2,3,4,6,12},非法值吸附到最近档)。</summary>
	public static int Columns(PageLayoutV2? page)
	{
		int cols = ((page != null) ? page.Cols : LayoutSchema.ColsDefault);
		cols = LayoutSchema.SnapCols(cols);
		return Math.Max(1, cols);
	}

	/// <summary>按页面 v2 条目打包出可见块的排版槽位(隐藏项不参与画布)。</summary>
	public static List<LayoutCell> Pack(PageLayoutV2? page)
	{
		List<LayoutCell> cells = new List<LayoutCell>();
		if (page == null || page.Blocks == null)
		{
			return cells;
		}
		int cols = Columns(page);
		Occupancy occupancy = new Occupancy(cols);
		foreach (BlockLayout block in page.Blocks)
		{
			if (block == null || string.IsNullOrEmpty(block.Id) || block.Hidden)
			{
				continue;
			}
			int w = Math.Max(1, (int)Math.Round(block.W));
			if (w <= 1 && block.Span > 1)
			{
				// stack → grid:span 跨列延续成列跨度
				w = block.Span;
			}
			w = LayoutSchema.ClampInt(w, 1, cols);
			int h = LayoutSchema.ClampInt(Math.Max(1, (int)Math.Round(block.H)), 1, Math.Max(1, cols));
			bool hasPosition = !(block.X <= 0.0 && block.Y <= 0.0 && block.W <= 1.0 && block.H <= 1.0);
			int x;
			int y;
			if (hasPosition)
			{
				x = LayoutSchema.ClampInt((int)Math.Round(block.X), 0, Math.Max(0, cols - w));
				y = Math.Max(0, (int)Math.Round(block.Y));
				if (!occupancy.IsFree(x, y, w, h))
				{
					// 原位放不下:先同列向下找,再全网格找(用户位置尽量保留,但不允许压在一起)
					occupancy.FirstFreeInColumn(x, w, y, out y);
					if (!occupancy.IsFree(x, y, w, h))
					{
						occupancy.FirstFree(w, h, y, out x, out y);
					}
				}
			}
			else
			{
				occupancy.FirstFree(w, h, 0, out x, out y);
			}
			occupancy.Fill(x, y, w, h);
			cells.Add(new LayoutCell
			{
				Id = block.Id,
				X = x,
				Y = y,
				W = w,
				H = h,
				NX = block.X,
				NY = block.Y,
				NW = block.W,
				NH = block.H,
				MinW = LayoutCanvas.FallbackMinW,
				MinH = ((block.MinH > 0.0) ? block.MinH : LayoutCanvas.FallbackMinH),
				Z = block.Z
			});
		}
		return cells;
	}

	/// <summary>行数(含跨行块占到的行;空表为 0)。</summary>
	public static int RowCount(IReadOnlyList<LayoutCell>? cells)
	{
		int rows = 0;
		if (cells != null)
		{
			foreach (LayoutCell cell in cells)
			{
				if (cell != null)
				{
					rows = Math.Max(rows, cell.Y + Math.Max(1, cell.H));
				}
			}
		}
		return rows;
	}

	/// <summary>
	/// 行高(两趟测量的第二趟,解"块高不定"):
	///   row_h &gt; 0 → 每行固定 row_h(内容超出按固定高裁,契约语义);
	///   row_h == 0 → 每行高 = max(跨该行的块的"需求高 ÷ 行跨度"),再抬到块最小高与兜底高。
	/// desiredHeights = 宿主第一趟用 Infinite 量出来的每块需求高(纯函数便于单测,传 null 也能算)。
	/// 空行按兜底高占位(显式留空不被压成 0)。
	/// </summary>
	public static double[] RowHeights(IReadOnlyList<LayoutCell>? cells, int rows, double fixedRowH, IReadOnlyDictionary<string, double>? desiredHeights, double fallback)
	{
		int count = Math.Max(0, rows);
		double[] heights = new double[count];
		double floor = ((fallback > 0.0) ? fallback : LayoutCanvas.FallbackMinH);
		if (fixedRowH > 0.0)
		{
			for (int i = 0; i < count; i++)
			{
				heights[i] = fixedRowH;
			}
		}
		else
		{
			for (int j = 0; j < count; j++)
			{
				heights[j] = floor;
			}
			if (cells != null)
			{
				foreach (LayoutCell cell in cells)
				{
					if (cell == null)
					{
						continue;
					}
					double desired = floor;
					if (desiredHeights != null && desiredHeights.TryGetValue(cell.Id, out double measured) && measured > 0.0)
					{
						desired = measured;
					}
					desired = Math.Max(desired, cell.MinH);
					double perRow = desired / Math.Max(1, cell.H);
					for (int k = 0; k < cell.H; k++)
					{
						int row = cell.Y + k;
						if (row >= 0 && row < count && heights[row] < perRow)
						{
							heights[row] = perRow;
						}
					}
				}
			}
		}
		for (int m = 0; m < count; m++)
		{
			if (heights[m] <= 0.0)
			{
				heights[m] = floor;
			}
		}
		return heights;
	}

	/// <summary>列宽(可用宽扣掉列间 gap 后平分;窄到负数时兜底 1px)。</summary>
	public static double ColumnWidth(double availableWidth, int cols, double gap)
	{
		int count = Math.Max(1, cols);
		double usable = availableWidth - Math.Max(0.0, gap) * (count - 1);
		return Math.Max(1.0, usable / count);
	}

	/// <summary>把槽位换成像素矩形(含列/行间 gap,不含页边距)。</summary>
	public static LayoutRect Place(LayoutCell cell, double columnWidth, double[]? rowHeights, double gap)
	{
		if (cell == null)
		{
			return default(LayoutRect);
		}
		double cw = Math.Max(1.0, columnWidth);
		double g = Math.Max(0.0, gap);
		double x = cell.X * (cw + g);
		double y = 0.0;
		if (rowHeights != null)
		{
			for (int i = 0; i < cell.Y && i < rowHeights.Length; i++)
			{
				y += rowHeights[i] + g;
			}
		}
		double w = cw * cell.W + g * Math.Max(0, cell.W - 1);
		double h = 0.0;
		if (rowHeights != null)
		{
			for (int k = 0; k < cell.H; k++)
			{
				int row = cell.Y + k;
				h += ((row >= 0 && row < rowHeights.Length) ? rowHeights[row] : LayoutCanvas.FallbackMinH) + ((k < cell.H - 1) ? g : 0.0);
			}
		}
		else
		{
			h = LayoutCanvas.FallbackMinH;
		}
		return new LayoutRect(x, y, Math.Max(1.0, w), Math.Max(1.0, h));
	}

	/// <summary>整表排布结果(与 cells 一一对应)。</summary>
	public static List<LayoutRect> Rects(IReadOnlyList<LayoutCell>? cells, double columnWidth, double[]? rowHeights, double gap)
	{
		List<LayoutRect> rects = new List<LayoutRect>();
		if (cells != null)
		{
			foreach (LayoutCell cell in cells)
			{
				rects.Add(Place(cell, columnWidth, rowHeights, gap));
			}
		}
		return rects;
	}

	/// <summary>取某块的排布矩形;不存在返回 null。</summary>
	public static LayoutRect? RectOf(IReadOnlyList<LayoutCell>? cells, IReadOnlyList<LayoutRect>? rects, string id)
	{
		if (cells == null || rects == null || string.IsNullOrEmpty(id))
		{
			return null;
		}
		for (int i = 0; i < cells.Count && i < rects.Count; i++)
		{
			if (string.Equals(cells[i].Id, id, StringComparison.Ordinal))
			{
				return rects[i];
			}
		}
		return null;
	}

	/// <summary>命中:返回最上层(z 最大)的槽位下标;没命中返回 -1。</summary>
	public static int HitIndex(IReadOnlyList<LayoutCell>? cells, IReadOnlyList<LayoutRect>? rects, double x, double y)
	{
		int best = -1;
		int bestZ = int.MinValue;
		if (cells == null || rects == null)
		{
			return -1;
		}
		for (int i = 0; i < cells.Count && i < rects.Count; i++)
		{
			if (rects[i].Contains(x, y) && cells[i].Z >= bestZ)
			{
				bestZ = cells[i].Z;
				best = i;
			}
		}
		return best;
	}

	/// <summary>整块拖拽:按网格单位平移,夹在画布内;返回是否真的动了。</summary>
	public static bool MoveCell(LayoutCell? cell, int dx, int dy, int cols, int maxRow)
	{
		if (cell == null)
		{
			return false;
		}
		int count = Math.Max(1, cols);
		int x = LayoutSchema.ClampInt(cell.X + dx, 0, Math.Max(0, count - cell.W));
		int y = Math.Max(0, cell.Y + dy);
		if (maxRow > 0)
		{
			y = Math.Min(y, maxRow);
		}
		if (x == cell.X && y == cell.Y)
		{
			return false;
		}
		cell.X = x;
		cell.Y = y;
		return true;
	}

	/// <summary>
	/// 手柄缩放(按网格单位):'w'/'e' 改列跨度(西侧手柄同时改起始列),'n'/'s' 改行跨度(北侧手柄同时改起始行)。
	/// 列跨度夹在 1..cols,行跨度夹在 1..cols,起始列/行不为负。
	/// </summary>
	public static bool ResizeCell(LayoutCell? cell, string? handle, int dw, int dh, int cols)
	{
		if (cell == null || string.IsNullOrEmpty(handle))
		{
			return false;
		}
		int count = Math.Max(1, cols);
		int x = cell.X;
		int y = cell.Y;
		int w = cell.W;
		int h = cell.H;
		if (handle.IndexOf('w') >= 0)
		{
			int left = LayoutSchema.ClampInt(x + dw, 0, x + w - 1);
			w += x - left;
			x = left;
		}
		if (handle.IndexOf('e') >= 0)
		{
			w = LayoutSchema.ClampInt(w + dw, 1, count - x);
		}
		if (handle.IndexOf('n') >= 0)
		{
			// 北边界不能越过南边界(否则行跨度会算成 0/负数):夹在 [0, y+h-1]
			int bottom = y + h;
			int top = LayoutSchema.ClampInt(y + dh, 0, Math.Max(0, bottom - 1));
			h = bottom - top;
			y = top;
		}
		if (handle.IndexOf('s') >= 0)
		{
			h = LayoutSchema.ClampInt(h + dh, 1, count);
		}
		w = Math.Max(1, w);
		h = Math.Max(1, h);
		if (x == cell.X && y == cell.Y && w == cell.W && h == cell.H)
		{
			return false;
		}
		cell.X = x;
		cell.Y = y;
		cell.W = w;
		cell.H = h;
		return true;
	}

	/// <summary>把整表槽位写回页面块(新位置/新尺寸;span 同步成列跨度,stack 模式切回来仍然跨列)。</summary>
	public static void Apply(PageLayoutV2? page, IReadOnlyList<LayoutCell>? cells)
	{
		if (page == null || page.Blocks == null || cells == null)
		{
			return;
		}
		Dictionary<string, LayoutCell> byId = new Dictionary<string, LayoutCell>(StringComparer.Ordinal);
		foreach (LayoutCell cell in cells)
		{
			if (cell != null && !string.IsNullOrEmpty(cell.Id))
			{
				byId[cell.Id] = cell;
			}
		}
		foreach (BlockLayout block in page.Blocks)
		{
			if (block == null || string.IsNullOrEmpty(block.Id) || !byId.TryGetValue(block.Id, out LayoutCell? cell2))
			{
				continue;
			}
			cell2.ApplyGrid(block);
			block.Span = LayoutSchema.ClampInt(cell2.W, 1, Math.Max(1, Columns(page)));
		}
	}

	// 占用表:按行惰性增长(行数自适应),w×h 矩形查询/填充
	private sealed class Occupancy
	{
		private readonly int _cols;

		private readonly List<bool[]> _rows = new List<bool[]>();

		public Occupancy(int cols)
		{
			_cols = Math.Max(1, cols);
		}

		public bool IsFree(int x, int y, int w, int h)
		{
			if (x < 0 || y < 0 || w < 1 || h < 1 || x + w > _cols)
			{
				return false;
			}
			for (int r = y; r < y + h; r++)
			{
				if (r >= _rows.Count)
				{
					continue;
				}
				bool[] row = _rows[r];
				for (int c = x; c < x + w; c++)
				{
					if (row[c])
					{
						return false;
					}
				}
			}
			return true;
		}

		public void Fill(int x, int y, int w, int h)
		{
			for (int r = y; r < y + h; r++)
			{
				bool[] row = Row(r);
				for (int c = x; c < x + w; c++)
				{
					if (c >= 0 && c < _cols)
					{
						row[c] = true;
					}
				}
			}
		}

		public void FirstFree(int w, int h, int startRow, out int x, out int y)
		{
			int from = Math.Max(0, startRow);
			int limit = _rows.Count + h + 2;
			for (int r = from; r <= limit; r++)
			{
				for (int c = 0; c + w <= _cols; c++)
				{
					if (IsFree(c, r, w, h))
					{
						x = c;
						y = r;
						return;
					}
				}
			}
			x = 0;
			y = limit;
		}

		public void FirstFreeInColumn(int x, int w, int startRow, out int y)
		{
			int from = Math.Max(0, startRow);
			int limit = _rows.Count + w + 2;
			for (int r = from; r <= limit; r++)
			{
				if (IsFree(x, r, w, 1))
				{
					y = r;
					return;
				}
			}
			y = limit;
		}

		private bool[] Row(int r)
		{
			while (_rows.Count <= r)
			{
				_rows.Add(new bool[_cols]);
			}
			return _rows[r];
		}
	}
}

/// <summary>
/// canvas 模式的纯几何(§4.A):归一化矩形 + 8 向手柄 + 栅格吸附。
/// 吸附公式**直接移植**旧 <c>DashHost.SnapV</c>(v → round(v × axisPx / step) × step / axisPx,再夹 0..1),
/// 吸附档沿用 <c>DashLayout</c> 的 {0,4,8,16,24}(0 = 不吸附)。
/// </summary>
public static class LayoutCanvas
{
	/// <summary>缩放下限宽兜底(对齐旧 DashHost 的 FallbackMin.W)。</summary>
	public const double FallbackMinW = 200.0;

	/// <summary>缩放下限高兜底(对齐旧 DashHost 的 FallbackMin.H)。</summary>
	public const double FallbackMinH = 120.0;

	/// <summary>默认吸附档(px;0 = 不吸附)。</summary>
	public const double DefaultStep = 8.0;

	/// <summary>宿主给不出有限高时的画布参考高(对齐旧 DashHost 的 600 兜底)。</summary>
	public const double RefHeight = 600.0;

	/// <summary>手柄的命中半径(px)。</summary>
	public const double HandleSize = 11.0;

	/// <summary>没命中任何手柄。</summary>
	public const string HandleNone = "";

	/// <summary>栅格吸附档 {0,4,8,16,24}(沿用 DashLayout.Grid)。</summary>
	public static readonly double[] Steps = new double[5] { 0.0, 4.0, 8.0, 16.0, 24.0 };

	/// <summary>8 向手柄名(与旧 DashHost.CardView.Handles 同名同序)。</summary>
	public static readonly string[] HandleNames = new string[8] { "nw", "n", "ne", "w", "e", "sw", "s", "se" };

	/// <summary>吸附档归一到契约档位;非法值取最近档,0 保持 0(不吸附)。</summary>
	public static double StepOf(double step)
	{
		if (step <= 0.0)
		{
			return 0.0;
		}
		double best = Steps[0];
		double bestDist = double.MaxValue;
		foreach (double choice in Steps)
		{
			if (choice <= 0.0)
			{
				continue;
			}
			double dist = Math.Abs(choice - step);
			if (dist < bestDist)
			{
				bestDist = dist;
				best = choice;
			}
		}
		return best;
	}

	/// <summary>是否契约里的吸附档(含 0 = 不吸附)。</summary>
	public static bool IsKnownStep(double step)
	{
		foreach (double choice in Steps)
		{
			if (Math.Abs(choice - step) < 0.001)
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>按页面 v2 条目装载可见块的归一化槽位(隐藏项不参与画布)。</summary>
	public static List<LayoutCell> Load(PageLayoutV2? page)
	{
		List<LayoutCell> cells = new List<LayoutCell>();
		if (page == null || page.Blocks == null)
		{
			return cells;
		}
		int index = 0;
		foreach (BlockLayout block in page.Blocks)
		{
			if (block == null || string.IsNullOrEmpty(block.Id) || block.Hidden)
			{
				continue;
			}
			LayoutCell cell = LayoutCell.FromBlock(block);
			cell.NX = LayoutSchema.Clamp(block.X, 0.0, LayoutSchema.CanvasMaxPos, 0.0);
			cell.NY = LayoutSchema.Clamp(block.Y, 0.0, LayoutSchema.CanvasMaxPos, 0.0);
			cell.NW = LayoutSchema.Clamp(block.W, LayoutSchema.CanvasMinSize, LayoutSchema.CanvasMaxSize, 1.0);
			cell.NH = LayoutSchema.Clamp(block.H, LayoutSchema.CanvasMinSize, LayoutSchema.CanvasMaxSize, 1.0);
			cell.Z = ((block.Z >= 0) ? block.Z : index);
			cells.Add(cell);
			index++;
		}
		return cells;
	}

	/// <summary>归一化矩形 → 像素矩形(按宿主可用尺寸)。</summary>
	public static LayoutRect Place(LayoutCell? cell, double width, double height)
	{
		if (cell == null)
		{
			return default(LayoutRect);
		}
		double w = Math.Max(1.0, width);
		double h = Math.Max(1.0, height);
		return new LayoutRect(cell.NX * w, cell.NY * h, Math.Max(1.0, cell.NW * w), Math.Max(1.0, cell.NH * h));
	}

	/// <summary>整表排布结果(与 cells 一一对应)。</summary>
	public static List<LayoutRect> Rects(IReadOnlyList<LayoutCell>? cells, double width, double height)
	{
		List<LayoutRect> rects = new List<LayoutRect>();
		if (cells != null)
		{
			foreach (LayoutCell cell in cells)
			{
				rects.Add(Place(cell, width, height));
			}
		}
		return rects;
	}

	/// <summary>吸附:移植旧 DashHost.SnapV(step ≤ 0 或轴太小时原样返回)。</summary>
	public static double Snap(double v, double axisPx, double stepPx)
	{
		double value = LayoutSchema.Clamp(v, 0.0, 1.0, 0.0);
		if (stepPx <= 0.0 || axisPx <= 1.0)
		{
			return value;
		}
		return LayoutSchema.Clamp(Math.Round(value * axisPx / stepPx) * stepPx / axisPx, 0.0, 1.0, value);
	}

	/// <summary>点是否命中某个手柄(返回手柄名,未命中返回 <see cref="HandleNone"/>)。</summary>
	public static string HandleAt(LayoutRect rect, double x, double y, double size)
	{
		double s = Math.Max(6.0, size);
		if (x < rect.X - s || x > rect.Right + s || y < rect.Y - s || y > rect.Bottom + s)
		{
			return HandleNone;
		}
		string name = "";
		if (y <= rect.Y + s)
		{
			name += "n";
		}
		else if (y >= rect.Bottom - s)
		{
			name += "s";
		}
		if (x <= rect.X + s)
		{
			name += "w";
		}
		else if (x >= rect.Right - s)
		{
			name += "e";
		}
		return name;
	}

	/// <summary>
	/// 整块拖拽:按像素位移改归一化位置,带吸附与"不许拖出画布"的钳位;返回是否真的动了。
	/// </summary>
	public static bool Move(LayoutCell? cell, double dxPx, double dyPx, double width, double height, double stepPx)
	{
		if (cell == null)
		{
			return false;
		}
		double w = Math.Max(1.0, width);
		double h = Math.Max(1.0, height);
		double nx = Snap(cell.NX + dxPx / w, w, stepPx);
		double ny = Snap(cell.NY + dyPx / h, h, stepPx);
		nx = LayoutSchema.Clamp(nx, 0.0, Math.Max(0.0, 1.0 - cell.NW), 0.0);
		ny = LayoutSchema.Clamp(ny, 0.0, Math.Max(0.0, 1.0 - cell.NH), 0.0);
		if (nx == cell.NX && ny == cell.NY)
		{
			return false;
		}
		cell.NX = nx;
		cell.NY = ny;
		return true;
	}

	/// <summary>
	/// 8 向手柄缩放:逐向手柄改对应边(西/北侧同时改起点),吸附后夹到下限尺寸与画布内。
	/// 下限尺寸 = max(契约最小尺寸, 块类型/块级最小尺寸 ÷ 轴长)。返回是否真的动了。
	/// </summary>
	public static bool Resize(LayoutCell? cell, string? handle, double dxPx, double dyPx, double width, double height, double stepPx)
	{
		if (cell == null || string.IsNullOrEmpty(handle))
		{
			return false;
		}
		double w = Math.Max(1.0, width);
		double h = Math.Max(1.0, height);
		double minW = Math.Min(1.0, Math.Max(LayoutSchema.CanvasMinSize, cell.MinW / w));
		double minH = Math.Min(1.0, Math.Max(LayoutSchema.CanvasMinSize, cell.MinH / h));
		double x = cell.NX;
		double y = cell.NY;
		double rw = cell.NW;
		double rh = cell.NH;
		if (handle.IndexOf('w') >= 0)
		{
			double left = Snap(LayoutSchema.Clamp(cell.NX + dxPx / w, 0.0, cell.NX + cell.NW - minW, 0.0), w, stepPx);
			rw = cell.NX + cell.NW - left;
			x = left;
		}
		if (handle.IndexOf('e') >= 0)
		{
			rw = LayoutSchema.Clamp(Snap(cell.NX + cell.NW + dxPx / w, w, stepPx) - cell.NX, minW, 1.0 - cell.NX, minW);
		}
		if (handle.IndexOf('n') >= 0)
		{
			double top = Snap(LayoutSchema.Clamp(cell.NY + dyPx / h, 0.0, cell.NY + cell.NH - minH, 0.0), h, stepPx);
			rh = cell.NY + cell.NH - top;
			y = top;
		}
		if (handle.IndexOf('s') >= 0)
		{
			rh = LayoutSchema.Clamp(Snap(cell.NY + cell.NH + dyPx / h, h, stepPx) - cell.NY, minH, 1.0 - cell.NY, minH);
		}
		rw = Math.Min(1.0 - x, Math.Max(minW, rw));
		rh = Math.Min(1.0 - y, Math.Max(minH, rh));
		if (x == cell.NX && y == cell.NY && rw == cell.NW && rh == cell.NH)
		{
			return false;
		}
		cell.NX = x;
		cell.NY = y;
		cell.NW = rw;
		cell.NH = rh;
		return true;
	}

	/// <summary>命中:返回最上层(z 最大)的槽位下标;没命中返回 -1。</summary>
	public static int HitIndex(IReadOnlyList<LayoutCell>? cells, IReadOnlyList<LayoutRect>? rects, double x, double y)
	{
		return LayoutGrid.HitIndex(cells, rects, x, y);
	}

	/// <summary>按叠放序(z 升序,同 z 保持原顺序)返回槽位副本 —— 宿主按它决定子元素顺序。</summary>
	public static List<LayoutCell> StackedOrder(IReadOnlyList<LayoutCell>? cells)
	{
		List<LayoutCell> list = new List<LayoutCell>();
		if (cells != null)
		{
			foreach (LayoutCell cell in cells)
			{
				if (cell != null)
				{
					list.Add(cell);
				}
			}
		}
		list.Sort(delegate(LayoutCell a, LayoutCell b)
		{
			return a.Z.CompareTo(b.Z);
		});
		return list;
	}

	/// <summary>把整表槽位写回页面块。</summary>
	public static void Apply(PageLayoutV2? page, IReadOnlyList<LayoutCell>? cells)
	{
		if (page == null || page.Blocks == null || cells == null)
		{
			return;
		}
		Dictionary<string, LayoutCell> byId = new Dictionary<string, LayoutCell>(StringComparer.Ordinal);
		foreach (LayoutCell cell in cells)
		{
			if (cell != null && !string.IsNullOrEmpty(cell.Id))
			{
				byId[cell.Id] = cell;
			}
		}
		foreach (BlockLayout block in page.Blocks)
		{
			if (block != null && !string.IsNullOrEmpty(block.Id) && byId.TryGetValue(block.Id, out LayoutCell? cell2))
			{
				cell2.ApplyCanvas(block);
			}
		}
	}
}

/// <summary>一条对齐辅助线(垂直 = 竖线,位置是画布内容区里的 X;水平 = 横线,位置是 Y)。</summary>
public sealed class LayoutGuideLine
{
	/// <summary>是否竖线。</summary>
	public bool Vertical { get; set; }

	/// <summary>线位置(px,相对画布内容区)。</summary>
	public double Position { get; set; }
}

/// <summary>对齐辅助线的计算结果:磁性吸附位移 + 要画的线。</summary>
public sealed class LayoutGuideResult
{
	/// <summary>X 方向吸附位移(px;0 = 没吸附)。</summary>
	public double Dx { get; set; }

	/// <summary>Y 方向吸附位移(px;0 = 没吸附)。</summary>
	public double Dy { get; set; }

	/// <summary>要画的辅助线(可能是 0~2 条:最多一条竖线 + 一条横线)。</summary>
	public List<LayoutGuideLine> Lines { get; } = new List<LayoutGuideLine>();

	/// <summary>是否命中了任何对齐(位移非 0 或至少一条线)。</summary>
	public bool HasSnap
	{
		get
		{
			return Dx != 0.0 || Dy != 0.0 || Lines.Count > 0;
		}
	}
}

/// <summary>
/// 对齐辅助线(§4.G):把被拖动块与其它块的**左/右/上/下/中线**比对,阈值内取最近的一条做磁性吸附。
/// 纯函数:宿主只负责把线画到 GuideLayer 上,判定与吸附位移全在这里(阈值 4px,与设计一致)。
/// </summary>
public static class LayoutGuides
{
	/// <summary>命中阈值(px)。</summary>
	public const double Threshold = 4.0;

	/// <summary>参与比对的 3 条横坐标(左/中/右)与 3 条纵坐标(上/中/下)。</summary>
	public static LayoutGuideResult Align(LayoutRect moving, IReadOnlyList<LayoutRect>? others, double threshold)
	{
		LayoutGuideResult result = new LayoutGuideResult();
		if (others == null || others.Count == 0)
		{
			return result;
		}
		double limit = ((threshold > 0.0) ? threshold : Threshold);
		double[] mineX = new double[3] { moving.X, moving.CenterX, moving.Right };
		double[] mineY = new double[3] { moving.Y, moving.CenterY, moving.Bottom };
		double bestX = double.MaxValue;
		double bestY = double.MaxValue;
		double dx = 0.0;
		double dy = 0.0;
		double lineX = 0.0;
		double lineY = 0.0;
		bool hasX = false;
		bool hasY = false;
		foreach (LayoutRect other in others)
		{
			double[] theirX = new double[3] { other.X, other.CenterX, other.Right };
			double[] theirY = new double[3] { other.Y, other.CenterY, other.Bottom };
			for (int i = 0; i < 3; i++)
			{
				for (int j = 0; j < 3; j++)
				{
					double delta = theirX[j] - mineX[i];
					double dist = Math.Abs(delta);
					if (dist <= limit && dist < bestX)
					{
						bestX = dist;
						dx = delta;
						lineX = theirX[j];
						hasX = true;
					}
					double delta2 = theirY[j] - mineY[i];
					double dist2 = Math.Abs(delta2);
					if (dist2 <= limit && dist2 < bestY)
					{
						bestY = dist2;
						dy = delta2;
						lineY = theirY[j];
						hasY = true;
					}
				}
			}
		}
		if (hasX)
		{
			result.Dx = dx;
			result.Lines.Add(new LayoutGuideLine
			{
				Vertical = true,
				Position = lineX
			});
		}
		if (hasY)
		{
			result.Dy = dy;
			result.Lines.Add(new LayoutGuideLine
			{
				Vertical = false,
				Position = lineY
			});
		}
		return result;
	}
}

/// <summary>
/// 区块外观参数(§4.B)的纯解析:density 间距档 / style 白名单兜底 / 常态标题开关 / 每块边距。
/// 三条默认都必须等价现状:style=plain(零装饰)、title=false(不冒标题)、pad=页级默认(18,14,18,16)。
/// </summary>
public static class LayoutAppearance
{
	/// <summary>密度间距档:紧凑。</summary>
	public const double CompactSpacing = 6.0;

	/// <summary>密度间距档:标准(= 现状 BlockHost.Gap,所以默认渲染不变)。</summary>
	public const double StandardSpacing = 10.0;

	/// <summary>密度间距档:宽松。</summary>
	public const double LooseSpacing = 14.0;

	/// <summary>密度 → 间距档(非法/空 → 标准档 10,即现状)。</summary>
	public static double SpacingOf(string? density)
	{
		if (string.Equals(density, LayoutSchema.DensityCompact, StringComparison.Ordinal))
		{
			return CompactSpacing;
		}
		if (string.Equals(density, LayoutSchema.DensityLoose, StringComparison.Ordinal))
		{
			return LooseSpacing;
		}
		return StandardSpacing;
	}

	/// <summary>
	/// 有效块间距:页级 gap 一旦**显式偏离缺省值**(≠10)就以页级为准,否则用全局密度的间距档。
	/// 于是"密度缺省 standard"(→10)与改动前的硬编码 Gap=10 逐像素一致。
	/// </summary>
	public static double EffectiveGap(string? density, double pageGap)
	{
		if (Math.Abs(pageGap - LayoutSchema.GapDefault) > 0.001)
		{
			return pageGap;
		}
		return SpacingOf(density);
	}

	/// <summary>块外观(白名单兜底 plain = 现状零装饰)。</summary>
	public static string StyleOf(BlockLayout? block)
	{
		if (block == null || !LayoutSchema.IsKnownStyle(block.Style))
		{
			return LayoutSchema.StylePlain;
		}
		return block.Style;
	}

	/// <summary>是否显示常态块标题(缺省 false = 现状不冒标题)。</summary>
	public static bool ShowsTitle(BlockLayout? block)
	{
		return block != null && block.Title;
	}

	/// <summary>块的有效边距:块级 pad 优先,否则页级 pad,再否则契约默认。</summary>
	public static double[] PadOf(BlockLayout? block, double[]? pagePad)
	{
		if (block != null && block.Pad != null)
		{
			return LayoutSchema.PadCopy(block.Pad);
		}
		return LayoutSchema.PadCopy(pagePad);
	}
}

/// <summary>
/// 响应式变体(§0.1 Q6 首期就做):**纯函数**——给定可用宽度与 variants → 用哪一份 body。
/// 本包只交纯函数 + 接线点说明(见交付报告):界面侧监听窗口宽度需要 MainWindow 侧的一个 seam,
/// 那属于 L7a 的写范围,本包不碰。
/// </summary>
public static class LayoutVariants
{
	/// <summary>窄窗变体的键名(契约 <c>variants.narrow → LAYOUT 子集</c>)。</summary>
	public const string Narrow = "narrow";

	/// <summary>
	/// 窄窗阈值(px)。设计文档没给具体数值 —— 这是**推断值**,取 760:低于它五页顶栏 + 12 列网格在 1080p
	/// 窗口里已经明显拥挤;接入时若想改,只需要改这一个常量(通报 Lead)。
	/// </summary>
	public const double NarrowWidthThreshold = 760.0;

	/// <summary>可用宽度 → 变体名(命中返回 narrow,否则空串 = 用主 body)。</summary>
	public static string NameOf(double availableWidth, double threshold)
	{
		double limit = ((threshold > 0.0) ? threshold : NarrowWidthThreshold);
		if (availableWidth > 0.0 && availableWidth < limit)
		{
			return Narrow;
		}
		return "";
	}

	/// <summary>取变体 body;没有对应变体(或宽窗)返回 null。</summary>
	public static LayoutBody? BodyOf(LayoutBody? body, double availableWidth, double threshold)
	{
		string name = NameOf(availableWidth, threshold);
		if (name.Length == 0 || body == null || body.Variants == null)
		{
			return null;
		}
		if (body.Variants.TryGetValue(name, out LayoutBody? variant) && variant != null)
		{
			return variant;
		}
		return null;
	}

	/// <summary>某页在当前宽度下该用哪份页面布局:变体里有该页就用变体的,否则回落主 body 的(再没有就是 null = 白名单默认)。</summary>
	public static PageLayoutV2? PageOf(LayoutBody? body, string pageId, double availableWidth, double threshold)
	{
		if (body == null || string.IsNullOrEmpty(pageId))
		{
			return null;
		}
		LayoutBody? variant = BodyOf(body, availableWidth, threshold);
		if (variant != null && variant.Pages != null && variant.Pages.TryGetValue(pageId, out PageLayoutV2? fromVariant) && fromVariant != null)
		{
			return fromVariant;
		}
		if (body.Pages != null && body.Pages.TryGetValue(pageId, out PageLayoutV2? fromMain))
		{
			return fromMain;
		}
		return null;
	}
}

/// <summary>
/// 宿主的"当前可用宽度"提示(§0.1 Q6 的接线点,D1 包):窗口尺寸一变,宿主把**内容区实测宽**喂进来,
/// 布局侧据此决定用主 body 还是 <c>variants.narrow</c>。
/// 为什么是静态的:区块宿主 <see cref="LayoutEditor"/> 由各页面自己 new(构造函数不能加参数),
/// 窗口没有别的地方可以把宽度传进去;这个类型只存一个数 + 一个"变体名变了"的事件,不碰任何界面。
/// 宽度的取值纪律(照 0 值语义):<c>Width &lt;= 0</c> = 还不知道可用宽 → 一律不做变体判定(用主 body),
/// 于是"没接线 / 首次布局前"的渲染与改动前完全一致。
/// </summary>
public static class LayoutVariantHost
{
	private static readonly object _gate = new object();

	private static double _width;

	private static double _threshold = LayoutVariants.NarrowWidthThreshold;

	private static string _name = "";

	/// <summary>内容区可用宽(px;≤0 = 未知,不做变体判定)。</summary>
	public static double Width
	{
		get
		{
			lock (_gate)
			{
				return _width;
			}
		}
		set
		{
			Apply(value, _threshold);
		}
	}

	/// <summary>窄窗阈值(px;默认 <see cref="LayoutVariants.NarrowWidthThreshold"/>,可调)。</summary>
	public static double Threshold
	{
		get
		{
			lock (_gate)
			{
				return _threshold;
			}
		}
		set
		{
			Apply(_width, ((value > 0.0) ? value : LayoutVariants.NarrowWidthThreshold));
		}
	}

	/// <summary>当前应生效的变体名(空串 = 主 body)。</summary>
	public static string CurrentName
	{
		get
		{
			lock (_gate)
			{
				return _name;
			}
		}
	}

	/// <summary>变体名发生变化时触发(参数为新变体名);宿主据此让页面区块当场换一套布局。</summary>
	public static event Action<string>? VariantChanged;

	/// <summary>
	/// 更新可用宽(可选同时更新阈值);返回**变体名是否变化**。
	/// 只有真的跨过阈值才返回 true 并触发 <see cref="VariantChanged"/> —— 拖动窗口时不能每帧都重建。
	/// </summary>
	public static bool Apply(double width, double threshold)
	{
		double limit = ((threshold > 0.0) ? threshold : LayoutVariants.NarrowWidthThreshold);
		double value = ((width > 0.0 && !double.IsInfinity(width) && !double.IsNaN(width)) ? width : 0.0);
		string name = LayoutVariants.NameOf(value, limit);
		string previous;
		bool changed;
		lock (_gate)
		{
			previous = _name;
			changed = !string.Equals(previous, name, StringComparison.Ordinal);
			_width = value;
			_threshold = limit;
			_name = name;
		}
		if (changed)
		{
			try
			{
				VariantChanged?.Invoke(name);
			}
			catch
			{
				// 订阅者自己的异常不能影响尺寸变化这条路径
			}
		}
		return changed;
	}

	/// <summary>按当前阈值更新可用宽(宿主最常用的入口)。</summary>
	public static bool Apply(double width)
	{
		return Apply(width, Threshold);
	}

	/// <summary>清回"未知宽 + 默认阈值"(测试用;生产代码不要调)。</summary>
	public static void Reset()
	{
		lock (_gate)
		{
			_width = 0.0;
			_threshold = LayoutVariants.NarrowWidthThreshold;
			_name = "";
		}
	}
}

/// <summary>编辑器要处理的键盘意图(与 WPF 的 Key 解耦,便于单测)。</summary>
public enum LayoutKey
{
	/// <summary>不是布局相关的键。</summary>
	None,

	/// <summary>Esc:取消当前拖拽 / 退出编辑态。</summary>
	Escape,

	/// <summary>Delete:移除选中的自建块(内置块不允许删)。</summary>
	Delete,

	/// <summary>左。</summary>
	Left,

	/// <summary>右。</summary>
	Right,

	/// <summary>上。</summary>
	Up,

	/// <summary>下。</summary>
	Down,

	/// <summary>Ctrl+C:复制选中块(没选中 = 整页)。</summary>
	Copy,

	/// <summary>Ctrl+V:粘贴。</summary>
	Paste,

	/// <summary>Ctrl+Z:撤销。</summary>
	Undo,

	/// <summary>Ctrl+Y / Ctrl+Shift+Z:重做。</summary>
	Redo
}

/// <summary>一次编辑操作的结果(宿主据此决定要不要 Render / 落盘 / 退编辑态)。</summary>
public enum LayoutEditEffect
{
	/// <summary>无操作。</summary>
	None,

	/// <summary>Esc:退出编辑态。</summary>
	ExitEdit,

	/// <summary>Esc:取消当前手势(不退出编辑态)。</summary>
	Cancelled,

	/// <summary>选中块被移动(方向键微调)。</summary>
	Moved,

	/// <summary>选中的自建块被移除。</summary>
	Deleted,

	/// <summary>撤销了一次。</summary>
	Undone,

	/// <summary>重做了一次。</summary>
	Redone,

	/// <summary>复制到剪贴板。</summary>
	Copied,

	/// <summary>粘贴到本页。</summary>
	Pasted
}

/// <summary>
/// 编辑会话(纯模型,零 WPF):多选集合 + 撤销/重做栈 + 剪贴板 + 键盘意图 + 方向键微调。
/// 状态本身(<see cref="LayoutPageState"/>)仍由宿主持有 —— 撤销时宿主用返回的快照重建它,
/// 所以这里只存快照,不做"隐藏的第二份真相"。
/// 撤销栈是**内存快照**,落盘仍然是即时的(每次提交都 Save),保住崩溃安全(§4.G / R6)。
/// </summary>
public sealed class LayoutEditSession
{
	/// <summary>撤销栈默认深度(超出丢最老的)。</summary>
	public const int DefaultCapacity = 50;

	private readonly List<string> _selection = new List<string>();

	private readonly List<PageLayoutV2> _undo = new List<PageLayoutV2>();

	private readonly List<PageLayoutV2> _redo = new List<PageLayoutV2>();

	private readonly List<BlockLayout> _clipboard = new List<BlockLayout>();

	/// <summary>撤销栈深度。</summary>
	public int Capacity { get; set; } = DefaultCapacity;

	/// <summary>当前选中的块 id(按选中顺序)。</summary>
	public IReadOnlyList<string> Selection
	{
		get
		{
			return _selection;
		}
	}

	/// <summary>选中块数。</summary>
	public int SelectedCount
	{
		get
		{
			return _selection.Count;
		}
	}

	/// <summary>撤销栈深度(测试与调试用)。</summary>
	public int UndoCount
	{
		get
		{
			return _undo.Count;
		}
	}

	/// <summary>重做栈深度。</summary>
	public int RedoCount
	{
		get
		{
			return _redo.Count;
		}
	}

	/// <summary>能否撤销。</summary>
	public bool CanUndo
	{
		get
		{
			return _undo.Count > 0;
		}
	}

	/// <summary>能否重做。</summary>
	public bool CanRedo
	{
		get
		{
			return _redo.Count > 0;
		}
	}

	/// <summary>剪贴板里有没有块。</summary>
	public bool HasClipboard
	{
		get
		{
			return _clipboard.Count > 0;
		}
	}

	/// <summary>剪贴板块数。</summary>
	public int ClipboardCount
	{
		get
		{
			return _clipboard.Count;
		}
	}

	// ————— 多选 —————

	/// <summary>是否选中。</summary>
	public bool IsSelected(string? id)
	{
		return !string.IsNullOrEmpty(id) && _selection.Contains(id);
	}

	/// <summary>选择:id 为空则清空;add=true 追加(Ctrl 点选),否则单选替换。</summary>
	public void Select(string? id, bool add)
	{
		if (string.IsNullOrEmpty(id))
		{
			ClearSelection();
			return;
		}
		if (!add)
		{
			_selection.Clear();
		}
		if (!_selection.Contains(id))
		{
			_selection.Add(id);
		}
	}

	/// <summary>Ctrl 点选:已选中则从选择里去掉,否则加入(Shift 同此实现,框选由宿主自己算集合)。</summary>
	public void Toggle(string? id)
	{
		if (string.IsNullOrEmpty(id))
		{
			return;
		}
		if (!_selection.Remove(id))
		{
			_selection.Add(id);
		}
	}

	/// <summary>清空选择。</summary>
	public void ClearSelection()
	{
		_selection.Clear();
	}

	/// <summary>批量选择(框选)。</summary>
	public void SelectMany(IEnumerable<string>? ids)
	{
		_selection.Clear();
		if (ids != null)
		{
			foreach (string id in ids)
			{
				if (!string.IsNullOrEmpty(id) && !_selection.Contains(id))
				{
					_selection.Add(id);
				}
			}
		}
	}

	/// <summary>剔除已经不存在的块(删除/撤销后调用,避免选择里留下幽灵 id)。</summary>
	public void Retain(IEnumerable<string>? alive)
	{
		HashSet<string> keep = new HashSet<string>(StringComparer.Ordinal);
		if (alive != null)
		{
			foreach (string id in alive)
			{
				if (!string.IsNullOrEmpty(id))
				{
					keep.Add(id);
				}
			}
		}
		for (int i = _selection.Count - 1; i >= 0; i--)
		{
			if (!keep.Contains(_selection[i]))
			{
				_selection.RemoveAt(i);
			}
		}
	}

	/// <summary>按给定顺序(通常 = 块的显示顺序)返回选中项。</summary>
	public List<string> OrderedSelection(IEnumerable<string>? order)
	{
		List<string> list = new List<string>();
		if (order != null)
		{
			foreach (string id in order)
			{
				if (IsSelected(id))
				{
					list.Add(id);
				}
			}
		}
		return list;
	}

	// ————— 撤销 / 重做(内存快照;落盘仍即时) —————

	/// <summary>提交前压一份快照(任何会改动布局的操作都先调它);同时清空重做栈。</summary>
	public void PushUndo(PageLayoutV2? current)
	{
		if (current == null)
		{
			return;
		}
		_undo.Add(current.Clone());
		int cap = Math.Max(1, Capacity);
		while (_undo.Count > cap)
		{
			_undo.RemoveAt(0);
		}
		_redo.Clear();
	}

	/// <summary>撤销:返回上一份快照(调用方用它重建页面状态),当前状态进重做栈;没得撤销返回 null。</summary>
	public PageLayoutV2? TakeUndo(PageLayoutV2? current)
	{
		if (_undo.Count == 0)
		{
			return null;
		}
		PageLayoutV2 snapshot = _undo[_undo.Count - 1];
		_undo.RemoveAt(_undo.Count - 1);
		if (current != null)
		{
			_redo.Add(current.Clone());
			int cap = Math.Max(1, Capacity);
			while (_redo.Count > cap)
			{
				_redo.RemoveAt(0);
			}
		}
		return snapshot.Clone();
	}

	/// <summary>重做:返回下一份快照;没得重做返回 null。</summary>
	public PageLayoutV2? TakeRedo(PageLayoutV2? current)
	{
		if (_redo.Count == 0)
		{
			return null;
		}
		PageLayoutV2 snapshot = _redo[_redo.Count - 1];
		_redo.RemoveAt(_redo.Count - 1);
		if (current != null)
		{
			_undo.Add(current.Clone());
			int cap = Math.Max(1, Capacity);
			while (_undo.Count > cap)
			{
				_undo.RemoveAt(0);
			}
		}
		return snapshot.Clone();
	}

	/// <summary>清空两个栈(退出编辑态时用)。</summary>
	public void ClearHistory()
	{
		_undo.Clear();
		_redo.Clear();
	}

	/// <summary>
	/// 丢掉最新的一份撤销快照并返回它:用于"**取消手势**"(拖到一半按 Esc)—— 回滚到起手状态,
	/// 但**不算一次撤销**,也不污染重做栈。栈空返回 null。
	/// </summary>
	public PageLayoutV2? DiscardUndo()
	{
		if (_undo.Count == 0)
		{
			return null;
		}
		PageLayoutV2 snapshot = _undo[_undo.Count - 1];
		_undo.RemoveAt(_undo.Count - 1);
		return snapshot.Clone();
	}

	// ————— 复制 / 粘贴 —————

	/// <summary>
	/// 复制:wholePage=true 或没有选中 → 整页块;否则只复制选中的块(按页面顺序)。
	/// 返回复制到的块数(0 = 什么都没复制,剪贴板保持原样不清空)。
	/// </summary>
	public int CopyFrom(PageLayoutV2? page, bool wholePage)
	{
		if (page == null || page.Blocks == null)
		{
			return 0;
		}
		List<BlockLayout> picked = new List<BlockLayout>();
		foreach (BlockLayout block in page.Blocks)
		{
			if (block == null || string.IsNullOrEmpty(block.Id))
			{
				continue;
			}
			if (wholePage || SelectedCount == 0 || IsSelected(block.Id))
			{
				picked.Add(block.Clone());
			}
		}
		if (picked.Count == 0)
		{
			return 0;
		}
		_clipboard.Clear();
		_clipboard.AddRange(picked);
		return _clipboard.Count;
	}

	/// <summary>
	/// 粘贴到页面末尾:内置块必须落在目标页白名单里(custom: / plugin: 是布局文档自己的块,不受白名单限制),
	/// 而且**按 id 去重** —— 同页重复粘贴不会造出重名块(新增自建块实例属于 L6 的区块管理)。
	/// 返回实际粘贴的块数。
	/// </summary>
	public int PasteInto(PageLayoutV2? page, IReadOnlyList<LayoutBlockRef>? refs)
	{
		if (page == null || _clipboard.Count == 0)
		{
			return 0;
		}
		HashSet<string> known = new HashSet<string>(StringComparer.Ordinal);
		if (refs != null)
		{
			foreach (LayoutBlockRef reference in refs)
			{
				if (reference != null && !string.IsNullOrEmpty(reference.Id))
				{
					known.Add(reference.Id);
				}
			}
		}
		HashSet<string> present = new HashSet<string>(StringComparer.Ordinal);
		if (page.Blocks == null)
		{
			page.Blocks = new List<BlockLayout>();
		}
		foreach (BlockLayout block in page.Blocks)
		{
			if (block != null && !string.IsNullOrEmpty(block.Id))
			{
				present.Add(block.Id);
			}
		}
		int count = 0;
		foreach (BlockLayout source in _clipboard)
		{
			if (source == null || string.IsNullOrEmpty(source.Id) || present.Contains(source.Id))
			{
				continue;
			}
			if (!known.Contains(source.Id) && !LayoutEngine.IsImplicitRef(source.Id))
			{
				continue;
			}
			BlockLayout copy = source.Clone();
			copy.Z = page.Blocks.Count;
			page.Blocks.Add(copy);
			present.Add(copy.Id);
			count++;
		}
		return count;
	}

	// ————— 键盘 —————

	/// <summary>
	/// 键名 → 意图(键名用 WPF <c>Key.ToString()</c> 的形式:Escape / Delete / Left / Right / Up / Down / Z / Y / C / V)。
	/// Ctrl 组合之外只认这几个键,别的返回 <see cref="LayoutKey.None"/>。
	/// </summary>
	public static LayoutKey KeyOf(string? keyName, bool ctrl, bool shift)
	{
		if (string.IsNullOrEmpty(keyName))
		{
			return LayoutKey.None;
		}
		if (ctrl)
		{
			if (string.Equals(keyName, "Z", StringComparison.Ordinal))
			{
				return (shift ? LayoutKey.Redo : LayoutKey.Undo);
			}
			if (string.Equals(keyName, "Y", StringComparison.Ordinal))
			{
				return LayoutKey.Redo;
			}
			if (string.Equals(keyName, "C", StringComparison.Ordinal))
			{
				return LayoutKey.Copy;
			}
			if (string.Equals(keyName, "V", StringComparison.Ordinal))
			{
				return LayoutKey.Paste;
			}
			return LayoutKey.None;
		}
		switch (keyName)
		{
		case "Escape":
			return LayoutKey.Escape;
		case "Delete":
		case "Back":
			return LayoutKey.Delete;
		case "Left":
			return LayoutKey.Left;
		case "Right":
			return LayoutKey.Right;
		case "Up":
			return LayoutKey.Up;
		case "Down":
			return LayoutKey.Down;
		default:
			return LayoutKey.None;
		}
	}

	/// <summary>
	/// 方向键微调(§4.G):grid 模式按**格**走(Shift = 10px 折算,不足一格按一格);
	/// canvas 模式按**吸附格**走(Shift = 10px);stack 模式没有自由坐标,返回 0。
	/// </summary>
	public static int Nudge(PageLayoutV2? page, IEnumerable<string>? ids, int dxDir, int dyDir, bool fine, double canvasW, double canvasH, double stepPx)
	{
		if (page == null || page.Blocks == null || ids == null)
		{
			return 0;
		}
		HashSet<string> targets = new HashSet<string>(StringComparer.Ordinal);
		foreach (string id in ids)
		{
			if (!string.IsNullOrEmpty(id))
			{
				targets.Add(id);
			}
		}
		if (targets.Count == 0)
		{
			return 0;
		}
		int moved = 0;
		if (page.Mode == LayoutSchema.ModeGrid)
		{
			int cols = LayoutGrid.Columns(page);
			double cellW = ((canvasW > 1.0) ? canvasW / Math.Max(1, cols) : 0.0);
			int stepX = 1;
			int stepY = 1;
			if (fine)
			{
				// Shift = 10px:折算成格数,不足一格按一格(否则宽窗口下 Shift 等于没动)
				stepX = Math.Max(1, (int)Math.Round(10.0 / ((cellW > 1.0) ? cellW : 10.0)));
				stepY = stepX;
			}
			foreach (BlockLayout block in page.Blocks)
			{
				if (block == null || !targets.Contains(block.Id) || block.Hidden)
				{
					continue;
				}
				LayoutCell cell = LayoutCell.FromBlock(block);
				cell.W = LayoutSchema.ClampInt(Math.Max(cell.W, block.Span), 1, cols);
				if (LayoutGrid.MoveCell(cell, dxDir * stepX, dyDir * stepY, cols, 0))
				{
					cell.ApplyGrid(block);
					moved++;
				}
			}
			return moved;
		}
		if (page.Mode == LayoutSchema.ModeCanvas)
		{
			double width = Math.Max(1.0, canvasW);
			double height = Math.Max(1.0, canvasH);
			double step = LayoutCanvas.StepOf(stepPx);
			double px = (fine ? 10.0 : ((step > 0.0) ? step : 1.0));
			foreach (BlockLayout block in page.Blocks)
			{
				if (block == null || !targets.Contains(block.Id) || block.Hidden)
				{
					continue;
				}
				LayoutCell cell = LayoutCell.FromBlock(block);
				cell.MinH = ((block.MinH > 0.0) ? block.MinH : LayoutCanvas.FallbackMinH);
				if (LayoutCanvas.Move(cell, dxDir * px, dyDir * px, width, height, 0.0))
				{
					cell.ApplyCanvas(block);
					moved++;
				}
			}
			return moved;
		}
		return 0;
	}
}
