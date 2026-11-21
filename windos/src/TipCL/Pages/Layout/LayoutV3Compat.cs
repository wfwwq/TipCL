using System;
using System.Collections.Generic;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Pages.Layout;

/// <summary>
/// R35 win-fix2(真机问题修复)的读时兼容规则 —— 纯函数、零 WPF,net8 测试宿主可直接断言。
/// 只影响「怎么呈现」,不改写存档:用户进编辑态并提交一次之后,才按呈现出来的样子落盘(与 r34/l3 canvas 读时推导同口径)。
/// </summary>
public static class LayoutV3Compat
{
	/// <summary>启动页下方内容区的页面 id。</summary>
	public const string LaunchPageId = "launch";

	/// <summary>默认布局里找不到的块(自建组件 / 插件块)在网格里占的列宽:半行。</summary>
	public const int UnknownSpanHalf = 6;

	/// <summary>
	/// 「旧版纵向流存档」:mode = stack 且每一块都没有列跨度(span ≤ 1)。v3 之前的版本(以及 v1 → v2 迁移)写出来的就是这种,
	/// 用户从没在网格里排过它;v3 编辑器里切到「列表」再保存的页,块上会带着网格留下的 span,不算旧存档。
	/// </summary>
	public static bool IsLegacyStack(PageLayoutV2? page)
	{
		if (page == null || !string.Equals(page.Mode, LayoutSchema.ModeStack, StringComparison.Ordinal) || page.Blocks == null || page.Blocks.Count == 0)
		{
			return false;
		}
		foreach (BlockLayout block in page.Blocks)
		{
			if (block != null && block.Span > 1)
			{
				return false;
			}
		}
		return true;
	}

	/// <summary>
	/// 启动页:旧版纵向流存档按 v3 默认网格呈现(效果图 pages3.html:新闻整行,下载任务 / 游戏时长 / 便签两两并排,日志整行)。
	/// 只针对 <see cref="LaunchPageId"/>、且 <paramref name="defaults"/> 是网格时生效;其余情况原样返回。
	/// 块的顺序与显隐完全照存档(用户已经显示出来的块继续显示),只补列宽与位置:
	/// 列宽取默认布局里同 id 的 span(找不到 = 半行),按顺序首个放得下的行左对齐摆放(每块一行高)。
	/// 返回的是副本,入参不动。
	/// </summary>
	public static PageLayoutV2? LaunchLegacyStackAsGrid(string? pageId, PageLayoutV2? page, PageLayoutV2? defaults, Func<string, int>? minColsOf = null)
	{
		if (!string.Equals(pageId, LaunchPageId, StringComparison.Ordinal) || !IsLegacyStack(page) || defaults == null || !string.Equals(defaults.Mode, LayoutSchema.ModeGrid, StringComparison.Ordinal))
		{
			return page;
		}
		PageLayoutV2 copy = page!.Clone();
		int cols = LayoutSchema.SnapCols(defaults.Cols);
		Dictionary<string, int> spans = DefaultSpans(defaults);
		List<int> used = new List<int>();
		int z = 0;
		foreach (BlockLayout block in copy.Blocks)
		{
			if (block == null || string.IsNullOrEmpty(block.Id))
			{
				continue;
			}
			int min = (minColsOf != null) ? minColsOf(block.Id) : 0;
			int span = spans.TryGetValue(block.Id, out int known) ? known : UnknownSpanHalf;
			span = LayoutSchema.ClampInt(span, Math.Min(cols, Math.Max(LayoutCanvasMigration.MinColsFloor, min)), cols);
			block.Z = z++;
			block.Span = span;
			if (block.Hidden)
			{
				// 隐藏块:契约默认矩形 + 列宽(重新显示时网格按 span 自动流)
				block.X = 0.0;
				block.Y = 0.0;
				block.W = 1.0;
				block.H = 1.0;
				continue;
			}
			int row = 0;
			while (row < used.Count && used[row] + span > cols)
			{
				row++;
			}
			if (row == used.Count)
			{
				used.Add(0);
			}
			block.X = used[row];
			block.Y = row;
			block.W = span;
			block.H = 1.0;
			used[row] += span;
		}
		copy.Mode = LayoutSchema.ModeGrid;
		copy.Cols = cols;
		return copy;
	}

	/// <summary>
	/// 网格页里「显示出来时没带列宽」的块(契约默认矩形 0,0,1,1 且 span ≤ 1 —— 旧版本在默认布局还没登记这一块时
	/// 从「添加」里显示它,就会这样落盘,例如实例库的「实例状态」)按默认布局里同 id 的 span 补上,
	/// 自动流时就和默认排布一致,不会缩成一条窄列挤在别的块旁边。默认布局里没有的块不动。返回副本。
	/// </summary>
	public static PageLayoutV2? FillMissingSpans(PageLayoutV2? page, PageLayoutV2? defaults)
	{
		if (page == null || defaults == null || !string.Equals(page.Mode, LayoutSchema.ModeGrid, StringComparison.Ordinal) || page.Blocks == null)
		{
			return page;
		}
		Dictionary<string, int> spans = DefaultSpans(defaults);
		if (spans.Count == 0)
		{
			return page;
		}
		PageLayoutV2? copy = null;
		for (int i = 0; i < page.Blocks.Count; i++)
		{
			BlockLayout block = page.Blocks[i];
			if (block == null || string.IsNullOrEmpty(block.Id) || block.Span > 1 || !IsContractRect(block) || !spans.TryGetValue(block.Id, out int span) || span <= 1)
			{
				continue;
			}
			copy ??= page.Clone();
			copy.Blocks[i].Span = Math.Min(span, LayoutSchema.SnapCols(page.Cols));
		}
		return copy ?? page;
	}

	/// <summary>编辑条左右外边距:页面自己管边距(宿主传 0)时用效果图的 22px(.bodyA padding),否则对齐内容区。</summary>
	public static double EditBarSideMargin(double contentMargin)
	{
		return (double.IsNaN(contentMargin) || contentMargin < 8.0) ? 22.0 : contentMargin;
	}

	private static bool IsContractRect(BlockLayout block)
	{
		return block.X == 0.0 && block.Y == 0.0 && block.W <= 1.0 && block.H <= 1.0;
	}

	private static Dictionary<string, int> DefaultSpans(PageLayoutV2 defaults)
	{
		Dictionary<string, int> spans = new Dictionary<string, int>(StringComparer.Ordinal);
		if (defaults.Blocks == null)
		{
			return spans;
		}
		foreach (BlockLayout block in defaults.Blocks)
		{
			if (block == null || string.IsNullOrEmpty(block.Id))
			{
				continue;
			}
			int span = (block.Span > 1) ? block.Span : (int)Math.Round(block.W);
			if (span > 1)
			{
				spans[block.Id] = span;
			}
		}
		return spans;
	}
}
