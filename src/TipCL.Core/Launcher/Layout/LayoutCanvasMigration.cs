// R35 lin-l3(GOAL-layout-v3 §5.3 / D-81 Q1):canvas 模式收敛 —— Linux 树的推导纯函数。
// 参照 Windows r34/l3 @ d195462 的 windos/src/TipCL/Core/Launcher/Layout/LayoutSchema.cs 末尾同名类;
// 本树放进单独的新文件(Core/Launcher/Layout 只许新增,不改既有类型),语义与 Windows 版逐条对齐,
// Linux 专属差异只有一处(见 ConvertInPlace 第 4 条:隐藏块排在可见块之后的行里,沿用基线 CanvasToGrid 的落点)。
using System;
using System.Collections.Generic;

namespace TipCL.Core.Launcher.Layout;

/// <summary>
/// canvas(自由画布)退出界面后,旧文档里的 canvas 页**读取时**按网格推导 ——
/// 顺序先 y 后 x,列宽 <c>span = clamp(round(w × cols), MinCols, cols)</c>。
/// <list type="bullet">
/// <item><see cref="ToGridPage"/>:渲染用。返回**副本**,入参一个字节都不改(文档原样不动)。</item>
/// <item><see cref="ConvertInPlace"/>:提交用 / 内存工作副本用。就地写成 grid。</item>
/// </list>
/// 纪律:纯模型(零 Avalonia),net8 测试宿主直接断言。块一个都不丢:隐藏块、<c>custom:</c> 自建块、
/// <c>plugin:</c> 插件块、页面白名单外的内置块全部保留;标题 / 外观 / 内边距 / 未知字段 / min_h 原样不动,
/// 只改 x / y / w / h / span / z(以及页级 mode / cols / row_h,见 <see cref="ConvertInPlace"/>)。
/// </summary>
public static class LayoutCanvasMigration
{
	/// <summary>推导出来的列宽下限(与编辑器渲染兜底「全局 2 列」同一口径;列数不足 2 时取列数)。</summary>
	public const int MinColsFloor = 2;

	// 判定"下一块在本行最矮块的下沿之下"时的容差(归一化坐标)
	private const double Epsilon = 1e-9;

	/// <summary>是否 canvas 页(只有这种页需要推导)。</summary>
	public static bool IsCanvas(PageLayoutV2? page)
	{
		return page != null && string.Equals(page.Mode, LayoutSchema.ModeCanvas, StringComparison.Ordinal);
	}

	/// <summary>
	/// 归一化宽度 → 网格列宽:<c>clamp(round(w × cols), min(cols, max(2, minCols)), cols)</c>。
	/// 四舍五入取远离零(0.5 列进位);非法宽度(NaN / ∞)按整行处理。
	/// </summary>
	public static int SpanOf(double w, int cols, int minCols)
	{
		int count = Math.Max(1, cols);
		int lower = Math.Min(count, Math.Max(MinColsFloor, minCols));
		if (double.IsNaN(w) || double.IsInfinity(w))
		{
			return count;
		}
		double raw = Math.Round(w * count, MidpointRounding.AwayFromZero);
		int span = (raw >= count) ? count : ((raw <= 0.0) ? 0 : (int)raw);
		return LayoutSchema.ClampInt(span, lower, count);
	}

	/// <summary>
	/// 渲染用推导:canvas 页 → 网格页的**副本**;不是 canvas 页时返回原样副本(null → null)。
	/// <paramref name="minColsOf"/> = 块 id → 该块在本页列数下的最小列数(拿不到传 null,只按 2 列兜底)。
	/// 入参 <paramref name="page"/> 不会被改动。
	/// </summary>
	public static PageLayoutV2? ToGridPage(PageLayoutV2? page, Func<string, int>? minColsOf = null)
	{
		if (page == null)
		{
			return null;
		}
		PageLayoutV2 copy = page.Clone();
		ConvertInPlace(copy, minColsOf);
		return copy;
	}

	/// <summary>
	/// 把 canvas 页**就地**写成 grid,返回是否真的转换了(不是 canvas 页 = false,什么都不动)。规则:
	/// <list type="number">
	/// <item>全部块(含隐藏)按 (y, x) 稳定排序,块表顺序随之改成这个顺序,z = 新序号;</item>
	/// <item>可见块按行流放置、左对齐:放不下(累计列宽超过 cols)或该块上沿已在本行最矮块的下沿之下时换行;
	///   每块占 1 行高,x / y / w / span = 推导出来的列位 / 行号 / 列宽;</item>
	/// <item>页级:mode = grid,cols = <see cref="LayoutSchema.SnapCols"/>(原 cols),row_h = 0
	///   (canvas 下 row_h 不生效,推导后若保留就会突然变成固定行高);</item>
	/// <item>隐藏块:列宽同样按公式推导,落在可见块之后的行里(x = 0,每块一行)—— 重新显示时出现在页尾;</item>
	/// <item>id 为空的坏块(Sanitize 之后不会出现)原样接在末尾,不丢。</item>
	/// </list>
	/// </summary>
	public static bool ConvertInPlace(PageLayoutV2? page, Func<string, int>? minColsOf = null)
	{
		if (page == null || !IsCanvas(page))
		{
			return false;
		}
		int cols = LayoutSchema.SnapCols(page.Cols);
		List<BlockLayout> source = page.Blocks ?? new List<BlockLayout>();
		List<KeyValuePair<int, BlockLayout>> valid = new List<KeyValuePair<int, BlockLayout>>();
		List<BlockLayout> rest = new List<BlockLayout>();
		for (int i = 0; i < source.Count; i++)
		{
			BlockLayout block = source[i];
			if (block == null)
			{
				continue;
			}
			if (string.IsNullOrEmpty(block.Id))
			{
				rest.Add(block);
				continue;
			}
			valid.Add(new KeyValuePair<int, BlockLayout>(i, block));
		}
		// 稳定排序:先 y 后 x,完全相同时保持原块表顺序
		valid.Sort(delegate(KeyValuePair<int, BlockLayout> a, KeyValuePair<int, BlockLayout> b)
		{
			int byY = Finite(a.Value.Y).CompareTo(Finite(b.Value.Y));
			if (byY != 0)
			{
				return byY;
			}
			int byX = Finite(a.Value.X).CompareTo(Finite(b.Value.X));
			return (byX != 0) ? byX : a.Key.CompareTo(b.Key);
		});
		List<BlockLayout> ordered = new List<BlockLayout>(valid.Count + rest.Count);
		List<KeyValuePair<BlockLayout, int>> hidden = new List<KeyValuePair<BlockLayout, int>>();
		int row = 0;
		int column = 0;
		bool rowHasBlocks = false;
		bool anyVisible = false;
		double rowBottom = double.PositiveInfinity;
		foreach (KeyValuePair<int, BlockLayout> item in valid)
		{
			BlockLayout block = item.Value;
			int min = (minColsOf != null) ? minColsOf(block.Id) : 0;
			int span = SpanOf(block.W, cols, min);
			double top = Finite(block.Y);
			double bottom = top + Math.Max(0.0, Finite(block.H));
			block.Z = ordered.Count;
			ordered.Add(block);
			if (block.Hidden)
			{
				hidden.Add(new KeyValuePair<BlockLayout, int>(block, span));
				continue;
			}
			if (rowHasBlocks && (column + span > cols || top >= rowBottom - Epsilon))
			{
				row++;
				column = 0;
				rowHasBlocks = false;
				rowBottom = double.PositiveInfinity;
			}
			block.X = column;
			block.Y = row;
			block.W = span;
			block.H = 1.0;
			block.Span = span;
			column += span;
			rowHasBlocks = true;
			anyVisible = true;
			rowBottom = Math.Min(rowBottom, bottom);
		}
		int extraRow = anyVisible ? row + 1 : 0;
		foreach (KeyValuePair<BlockLayout, int> pair in hidden)
		{
			BlockLayout block = pair.Key;
			block.X = 0.0;
			block.Y = extraRow++;
			block.W = pair.Value;
			block.H = 1.0;
			block.Span = pair.Value;
		}
		ordered.AddRange(rest);
		page.Blocks = ordered;
		page.Mode = LayoutSchema.ModeGrid;
		page.Cols = cols;
		page.RowH = 0.0;
		return true;
	}

	// NaN / ∞ 一律当 0(Sanitize 之后不会出现,这里只是让排序在坏数据上也是确定的)
	private static double Finite(double value)
	{
		return (double.IsNaN(value) || double.IsInfinity(value)) ? 0.0 : value;
	}
}
