using System;
using System.Collections.Generic;

namespace TipCL.Core.Launcher.Layout;

// 布局文档 v2 的**唯一契约真相**:字段白名单、类型门禁、逐字段钳位、结构上限。
// 设计依据 = docs/layout-v2-design.md §3.2 的数据契约表(每一行都对应下面一个常量或一处 Clamp)。
// 约定:本类型是纯模型层(禁 WPF / 禁界面),Sanitize 就地规范化并返回同一实例,方便链式调用。
// 未知字段一律"保留但不生效":由 LayoutDocV2 的 Extra 承载,Sanitize 不动它们。
public static class LayoutSchema
{
	/// <summary>信封版本号(单页 version 也必须是它,否则该页回默认)。</summary>
	public const int CurrentVersion = 2;

	/// <summary>信封类型标识(不是它就不是本系统的文件)。</summary>
	public const string Kind = "tipcl-layout";

	// ————— 取值枚举(§3.2 的"取值"列) —————

	/// <summary>页面布局模式:一维流(现状)。</summary>
	public const string ModeStack = "stack";

	/// <summary>页面布局模式:多列网格(可跨列跨行)。</summary>
	public const string ModeGrid = "grid";

	/// <summary>页面布局模式:像素自由(仅固定高块开放)。</summary>
	public const string ModeCanvas = "canvas";

	/// <summary>块外观:现状零装饰。</summary>
	public const string StylePlain = "plain";

	/// <summary>块外观:卡片。</summary>
	public const string StyleCard = "card";

	/// <summary>块外观:分隔线。</summary>
	public const string StyleDivider = "divider";

	/// <summary>全局密度:紧凑。</summary>
	public const string DensityCompact = "compact";

	/// <summary>全局密度:标准(默认,与现状等价)。</summary>
	public const string DensityStandard = "standard";

	/// <summary>全局密度:宽松。</summary>
	public const string DensityLoose = "loose";

	/// <summary>导航形态:顶栏页签(默认)。</summary>
	public const string NavModeTabs = "tabs";

	/// <summary>导航形态:左侧栏(§0.1 Q1 已否决复活,字段保留不接线)。</summary>
	public const string NavModeSidebar = "sidebar";

	/// <summary>侧栏靠左。</summary>
	public const string NavSideLeft = "left";

	/// <summary>侧栏靠右。</summary>
	public const string NavSideRight = "right";

	/// <summary>导航风格:精简(沿用 NavModel.StyleCompact)。</summary>
	public const string NavStyleCompact = "compact";

	/// <summary>导航风格:分组(沿用 NavModel.StyleGrouped)。</summary>
	public const string NavStyleGrouped = "grouped";

	/// <summary>自建区块首期白名单(§4.F):便签。</summary>
	public const string WidgetNote = "note";

	/// <summary>自建区块首期白名单(§4.F):快捷启动。</summary>
	public const string WidgetShortcut = "shortcut";

	/// <summary>自建区块首期白名单(§4.F):时钟。</summary>
	public const string WidgetClock = "clock";

	/// <summary>自建区块首期白名单(§4.F):分隔。</summary>
	public const string WidgetDivider = "divider";

	/// <summary>自建区块首期白名单(§4.F):时长概览。</summary>
	public const string WidgetPlaytime = "playtime";

	/// <summary>危险类型(§0.1 Q5):网页,默认关闭。</summary>
	public const string WidgetWeb = "web";

	/// <summary>危险类型(§0.1 Q5):按钮,默认关闭,只允许内部动作白名单。</summary>
	public const string WidgetButton = "button";

	// ————— 钳位表(§3.2 的"默认/钳位"列) —————

	/// <summary>grid 列数可取值。</summary>
	public static readonly int[] ColChoices = new int[5] { 2, 3, 4, 6, 12 };

	/// <summary>grid 列数默认值。</summary>
	public const int ColsDefault = 12;

	/// <summary>侧栏宽度下限(沿用既有后端钳位)。</summary>
	public const double NavWidthMin = 140.0;

	/// <summary>侧栏宽度上限(沿用既有后端钳位)。</summary>
	public const double NavWidthMax = 320.0;

	/// <summary>侧栏宽度默认值。</summary>
	public const double NavWidthDefault = 196.0;

	/// <summary>块间距下限。</summary>
	public const double GapMin = 0.0;

	/// <summary>块间距上限。</summary>
	public const double GapMax = 32.0;

	/// <summary>块间距默认值(现状 BlockHost.Gap = 10)。</summary>
	public const double GapDefault = 10.0;

	/// <summary>边距(页级 / 块级)下限。</summary>
	public const double PadMin = 0.0;

	/// <summary>边距(页级 / 块级)上限。</summary>
	public const double PadMax = 64.0;

	/// <summary>固定行高下限(0 = 按内容自适应)。</summary>
	public const double RowHMin = 0.0;

	/// <summary>固定行高上限(防御性上限,契约本身没写;见报告"推断项")。</summary>
	public const double RowHMax = 4096.0;

	/// <summary>块最小高度下限。</summary>
	public const double MinHMin = 0.0;

	/// <summary>块最小高度上限(防御性上限,契约本身没写;见报告"推断项")。</summary>
	public const double MinHMax = 4096.0;

	/// <summary>分栏比例下限。</summary>
	public const double RatioMin = 0.15;

	/// <summary>分栏比例上限。</summary>
	public const double RatioMax = 0.85;

	/// <summary>分栏比例默认值(拖拽前的中间值)。</summary>
	public const double RatioDefault = 0.5;

	/// <summary>canvas 尺寸下限(归一化)。</summary>
	public const double CanvasMinSize = 0.04;

	/// <summary>canvas 尺寸上限(归一化)。</summary>
	public const double CanvasMaxSize = 1.0;

	/// <summary>canvas 坐标上限(归一化)。</summary>
	public const double CanvasMaxPos = 1.0;

	/// <summary>页数上限(防御性上限:挡住恶意/损坏文档把界面撑爆)。</summary>
	public const int MaxPages = 256;

	/// <summary>单页块数上限(防御性上限)。</summary>
	public const int MaxBlocksPerPage = 512;

	/// <summary>自建区块数上限(防御性上限)。</summary>
	public const int MaxWidgets = 256;

	/// <summary>命名方案数上限(防御性上限)。</summary>
	public const int MaxProfiles = 64;

	/// <summary>响应式变体数上限(首期只写不读)。</summary>
	public const int MaxVariants = 4;

	/// <summary>自定义分组数上限(防御性上限)。</summary>
	public const int MaxNavGroups = 32;

	/// <summary>页级默认边距(对齐 PageBase.ScrollBody 的 18,14,18,16)。</summary>
	private static readonly double[] PadDefault = new double[4] { 18.0, 14.0, 18.0, 16.0 };

	/// <summary>顶栏页签默认顺序/集合(§3.2 的 tabs 默认值)。</summary>
	public static readonly string[] DefaultTabs = new string[5] { "launch", "library", "download", "plugins", "settings" };

	/// <summary>
	/// 合法导航 key(镜像 NavModel.TopKeys ∪ NavModel.AllSubKeys)。
	/// 刻意复制字面量而不引 NavModel:那个类型的静态初始化会构建 I18n 文案表,
	/// 而本类型要在 net8 测试宿主里被加载;L7 接线外壳时若发现漂移,应以 NavModel 为准并对齐这里。
	/// </summary>
	public static readonly string[] NavKeys = new string[10] { "launch", "download", "ai", "more", "tasks", "instance", "account", "multiplayer", "settings", "plugins" };

	/// <summary>导航顺序默认值(镜像 NavModel.DefaultNavOrder)。</summary>
	public static readonly string[] DefaultNavOrder = new string[7] { "launch", "download", "instance", "ai", "more", "settings", "tasks" };

	/// <summary>取页级默认边距的副本(数组是可变的,不能把静态实例交出去)。</summary>
	public static double[] PadDefaultCopy()
	{
		return new double[4] { 18.0, 14.0, 18.0, 16.0 };
	}

	/// <summary>拷贝四元边距数组(null → 默认值)。</summary>
	public static double[] PadCopy(double[]? pad)
	{
		double[] result = PadDefaultCopy();
		if (pad != null)
		{
			for (int i = 0; i < 4 && i < pad.Length; i++)
			{
				result[i] = pad[i];
			}
		}
		return result;
	}

	/// <summary>钳位;NaN/Infinity 视为默认值(避免把非法数字写回文件)。</summary>
	public static double Clamp(double value, double min, double max, double dflt)
	{
		if (double.IsNaN(value) || double.IsInfinity(value))
		{
			return dflt;
		}
		if (value < min)
		{
			return min;
		}
		if (value > max)
		{
			return max;
		}
		return value;
	}

	/// <summary>整数钳位。</summary>
	public static int ClampInt(int value, int min, int max)
	{
		if (value < min)
		{
			return min;
		}
		if (value > max)
		{
			return max;
		}
		return value;
	}

	/// <summary>列数吸附到 {2,3,4,6,12}:取最近档,等距时取小档(确定性)。</summary>
	public static int SnapCols(int cols)
	{
		int best = ColChoices[0];
		int bestDist = int.MaxValue;
		foreach (int choice in ColChoices)
		{
			int dist = Math.Abs(cols - choice);
			if (dist < bestDist)
			{
				bestDist = dist;
				best = choice;
			}
		}
		return best;
	}

	/// <summary>生成自建区块 id(w-&lt;时间戳&gt;;同一毫秒内的重名由 Normalize 追加序号)。</summary>
	public static string NewWidgetId()
	{
		return "w-" + DateTime.Now.ToString("yyyyMMddHHmmssfff");
	}

	/// <summary>是否合法导航 key。</summary>
	public static bool IsKnownNavKey(string? key)
	{
		if (string.IsNullOrEmpty(key))
		{
			return false;
		}
		foreach (string item in NavKeys)
		{
			if (string.Equals(item, key, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>是否合法顶栏页签 key。</summary>
	public static bool IsKnownTab(string? key)
	{
		if (string.IsNullOrEmpty(key))
		{
			return false;
		}
		foreach (string item in DefaultTabs)
		{
			if (string.Equals(item, key, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>是否已知自建区块类型(未知类型不丢,只标记为"不可用",避免删用户数据)。</summary>
	public static bool IsKnownWidgetType(string? type)
	{
		if (string.IsNullOrEmpty(type))
		{
			return false;
		}
		return string.Equals(type, WidgetNote, StringComparison.Ordinal)
			|| string.Equals(type, WidgetShortcut, StringComparison.Ordinal)
			|| string.Equals(type, WidgetClock, StringComparison.Ordinal)
			|| string.Equals(type, WidgetDivider, StringComparison.Ordinal)
			|| string.Equals(type, WidgetPlaytime, StringComparison.Ordinal)
			|| string.Equals(type, WidgetWeb, StringComparison.Ordinal)
			|| string.Equals(type, WidgetButton, StringComparison.Ordinal);
	}

	/// <summary>是否危险类型(web / button):默认不进候选列表,需在设置里显式开启(§0.1 Q5)。</summary>
	public static bool IsDangerousWidgetType(string? type)
	{
		return string.Equals(type, WidgetWeb, StringComparison.Ordinal) || string.Equals(type, WidgetButton, StringComparison.Ordinal);
	}

	/// <summary>是否已知布局模式。</summary>
	public static bool IsKnownMode(string? mode)
	{
		return string.Equals(mode, ModeStack, StringComparison.Ordinal)
			|| string.Equals(mode, ModeGrid, StringComparison.Ordinal)
			|| string.Equals(mode, ModeCanvas, StringComparison.Ordinal);
	}

	/// <summary>是否已知块外观。</summary>
	public static bool IsKnownStyle(string? style)
	{
		return string.Equals(style, StylePlain, StringComparison.Ordinal)
			|| string.Equals(style, StyleCard, StringComparison.Ordinal)
			|| string.Equals(style, StyleDivider, StringComparison.Ordinal);
	}

	/// <summary>是否已知密度档。</summary>
	public static bool IsKnownDensity(string? density)
	{
		return string.Equals(density, DensityCompact, StringComparison.Ordinal)
			|| string.Equals(density, DensityStandard, StringComparison.Ordinal)
			|| string.Equals(density, DensityLoose, StringComparison.Ordinal);
	}

	/// <summary>
	/// 就地规范化整份文档:门禁(version/kind)、结构(via <see cref="LayoutV2.Normalize"/>)、逐字段钳位、白名单过滤。
	/// 未知字段保持不动(保留但不生效)。
	/// </summary>
	public static LayoutV2 Sanitize(LayoutV2 doc)
	{
		if (doc == null)
		{
			return LayoutV2.CreateDefault();
		}
		doc.Version = CurrentVersion;
		doc.Kind = Kind;
		doc.SavedAt ??= "";
		doc.App ??= "";
		doc.ActiveProfile ??= "";
		doc.Extra ??= LayoutJson.Map();
		if (doc.Layout == null)
		{
			doc.Layout = new LayoutBody();
		}
		doc.Normalize();
		SanitizeBody(doc.Layout, 0);
		Dictionary<string, LayoutBody> profiles = new Dictionary<string, LayoutBody>(StringComparer.Ordinal);
		if (doc.Profiles != null)
		{
			foreach (KeyValuePair<string, LayoutBody> kv in doc.Profiles)
			{
				if (string.IsNullOrEmpty(kv.Key) || kv.Value == null || profiles.Count >= MaxProfiles)
				{
					continue;
				}
				SanitizeBody(kv.Value, 0);
				profiles[kv.Key] = kv.Value;
			}
		}
		doc.Profiles = profiles;
		// 悬空的 active_profile(方案已被删)按"直接编辑态"处理,避免界面上出现找不到的方案名
		if (doc.ActiveProfile.Length != 0 && !doc.Profiles.ContainsKey(doc.ActiveProfile))
		{
			doc.ActiveProfile = "";
		}
		return doc;
	}

	/// <summary>就地规范化 LAYOUT 对象;depth = 0 表示允许一层 variants(&gt;0 时清掉嵌套 variants,避免递归膨胀)。</summary>
	public static LayoutBody SanitizeBody(LayoutBody body, int depth)
	{
		if (body == null)
		{
			return new LayoutBody();
		}
		if (body.Shell == null)
		{
			body.Shell = new ShellLayout();
		}
		SanitizeShell(body.Shell);
		Dictionary<string, PageLayoutV2> pages = new Dictionary<string, PageLayoutV2>(StringComparer.Ordinal);
		if (body.Pages != null)
		{
			foreach (KeyValuePair<string, PageLayoutV2> kv in body.Pages)
			{
				if (string.IsNullOrEmpty(kv.Key) || kv.Value == null || pages.Count >= MaxPages)
				{
					continue;
				}
				PageLayoutV2? page = SanitizePage(kv.Value);
				if (page != null)
				{
					pages[kv.Key] = page;
				}
			}
		}
		body.Pages = pages;
		Dictionary<string, SplitState> splits = new Dictionary<string, SplitState>(StringComparer.Ordinal);
		if (body.Splits != null)
		{
			foreach (KeyValuePair<string, SplitState> kv2 in body.Splits)
			{
				if (string.IsNullOrEmpty(kv2.Key) || kv2.Value == null)
				{
					continue;
				}
				splits[kv2.Key] = SanitizeSplit(kv2.Value);
			}
		}
		body.Splits = splits;
		List<WidgetDef> widgets = new List<WidgetDef>();
		if (body.Widgets != null)
		{
			foreach (WidgetDef widget in body.Widgets)
			{
				if (widget != null && widgets.Count < MaxWidgets)
				{
					widgets.Add(SanitizeWidget(widget));
				}
			}
		}
		body.Widgets = widgets;
		body.Normalize();
		Dictionary<string, LayoutBody> variants = new Dictionary<string, LayoutBody>(StringComparer.Ordinal);
		if (depth == 0 && body.Variants != null)
		{
			foreach (KeyValuePair<string, LayoutBody> kv3 in body.Variants)
			{
				if (string.IsNullOrEmpty(kv3.Key) || kv3.Value == null || variants.Count >= MaxVariants)
				{
					continue;
				}
				variants[kv3.Key] = SanitizeBody(kv3.Value, depth + 1);
			}
		}
		body.Variants = variants;
		body.Meta ??= LayoutJson.Map();
		body.Extra ??= LayoutJson.Map();
		return body;
	}

	/// <summary>就地规范化外壳:白名单 + 钳位。</summary>
	public static ShellLayout SanitizeShell(ShellLayout shell)
	{
		if (shell == null)
		{
			return new ShellLayout();
		}
		if (shell.NavMode != NavModeTabs && shell.NavMode != NavModeSidebar)
		{
			shell.NavMode = NavModeTabs;
		}
		if (shell.NavSide != NavSideLeft && shell.NavSide != NavSideRight)
		{
			shell.NavSide = NavSideLeft;
		}
		shell.NavWidth = Clamp(shell.NavWidth, NavWidthMin, NavWidthMax, NavWidthDefault);
		if (shell.NavStyle != NavStyleCompact && shell.NavStyle != NavStyleGrouped)
		{
			shell.NavStyle = NavStyleGrouped;
		}
		shell.NavOrder = FilterKeys(shell.NavOrder, IsKnownNavKey);
		if (shell.NavOrder.Count == 0)
		{
			shell.NavOrder = new List<string>(DefaultNavOrder);
		}
		shell.NavHidden = FilterKeys(shell.NavHidden, IsKnownNavKey);
		List<NavGroupLayout> groups = new List<NavGroupLayout>();
		if (shell.NavGroups != null)
		{
			foreach (NavGroupLayout group in shell.NavGroups)
			{
				if (group == null || groups.Count >= MaxNavGroups)
				{
					continue;
				}
				group.Keys = FilterKeys(group.Keys, IsKnownNavKey);
				group.Title ??= "";
				group.Extra ??= LayoutJson.Map();
				if (group.Keys.Count != 0)
				{
					groups.Add(group);
				}
			}
		}
		shell.NavGroups = groups;
		shell.Tabs = FilterKeys(shell.Tabs, IsKnownTab);
		if (shell.Tabs.Count == 0)
		{
			shell.Tabs = new List<string>(DefaultTabs);
		}
		if (shell.Titlebar == null)
		{
			shell.Titlebar = new TitlebarLayout();
		}
		shell.Titlebar.Extra ??= LayoutJson.Map();
		if (!IsKnownDensity(shell.Density))
		{
			shell.Density = DensityStandard;
		}
		shell.Extra ??= LayoutJson.Map();
		return shell;
	}

	/// <summary>就地规范化单页;返回 null 表示该页 version 门禁不过 → 整页按默认处理(从文档里去掉)。</summary>
	public static PageLayoutV2? SanitizePage(PageLayoutV2 page)
	{
		if (page == null || page.Version != CurrentVersion)
		{
			return null;
		}
		if (!IsKnownMode(page.Mode))
		{
			page.Mode = ModeStack;
		}
		page.Cols = SnapCols(page.Cols);
		page.RowH = Clamp(page.RowH, RowHMin, RowHMax, 0.0);
		page.Gap = Clamp(page.Gap, GapMin, GapMax, GapDefault);
		page.Pad = SanitizePad(page.Pad, PadDefaultCopy());
		List<BlockLayout> blocks = new List<BlockLayout>();
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		if (page.Blocks != null)
		{
			foreach (BlockLayout block in page.Blocks)
			{
				if (block == null || string.IsNullOrEmpty(block.Id) || !seen.Add(block.Id) || blocks.Count >= MaxBlocksPerPage)
				{
					continue;
				}
				blocks.Add(SanitizeBlock(block, page.Mode, page.Cols, blocks.Count));
			}
		}
		page.Blocks = blocks;
		for (int i = 0; i < blocks.Count; i++)
		{
			blocks[i].Z = ClampInt(blocks[i].Z, 0, Math.Max(0, blocks.Count - 1));
		}
		page.Extra ??= LayoutJson.Map();
		return page;
	}

	/// <summary>就地规范化单块(按页面模式选择钳位档位;index = 顺序索引)。</summary>
	public static BlockLayout SanitizeBlock(BlockLayout block, string mode, int cols, int index)
	{
		if (block == null)
		{
			return new BlockLayout();
		}
		int colMax = Math.Max(1, cols);
		if (mode == ModeCanvas)
		{
			block.X = Clamp(block.X, 0.0, CanvasMaxPos, 0.0);
			block.Y = Clamp(block.Y, 0.0, CanvasMaxPos, 0.0);
			block.W = Clamp(block.W, CanvasMinSize, CanvasMaxSize, 1.0);
			block.H = Clamp(block.H, CanvasMinSize, CanvasMaxSize, 1.0);
		}
		else
		{
			// stack 与 grid 共用栅格档位:stack 只认 span,x/y/w/h 保持默认(0,0,1,1)时不受影响
			block.X = Clamp(block.X, 0.0, colMax - 1, 0.0);
			block.Y = Clamp(block.Y, 0.0, colMax - 1, 0.0);
			block.W = Clamp(block.W, 1.0, colMax, 1.0);
			block.H = Clamp(block.H, 1.0, colMax, 1.0);
		}
		block.Span = ClampInt(block.Span, 1, colMax);
		if (!IsKnownStyle(block.Style))
		{
			block.Style = StylePlain;
		}
		if (block.Pad != null)
		{
			block.Pad = SanitizePad(block.Pad, PadDefaultCopy());
		}
		block.MinH = Clamp(block.MinH, MinHMin, MinHMax, 0.0);
		if (block.Z < 0)
		{
			block.Z = index;
		}
		block.Settings ??= LayoutJson.Map();
		block.Extra ??= LayoutJson.Map();
		return block;
	}

	/// <summary>就地规范化分栏比例(夹 0.15..0.85)。</summary>
	public static SplitState SanitizeSplit(SplitState split)
	{
		if (split == null)
		{
			return new SplitState();
		}
		Dictionary<string, double> ratios = new Dictionary<string, double>(StringComparer.Ordinal);
		if (split.Ratios != null)
		{
			foreach (KeyValuePair<string, double> kv in split.Ratios)
			{
				if (!string.IsNullOrEmpty(kv.Key))
				{
					ratios[kv.Key] = Clamp(kv.Value, RatioMin, RatioMax, RatioDefault);
				}
			}
		}
		split.Ratios = ratios;
		split.Extra ??= LayoutJson.Map();
		return split;
	}

	/// <summary>就地规范化自建区块(id 缺失由 Normalize 补;未知类型保留不删,只靠 <see cref="IsKnownWidgetType"/> 判断可用性)。</summary>
	public static WidgetDef SanitizeWidget(WidgetDef widget)
	{
		if (widget == null)
		{
			return new WidgetDef();
		}
		widget.Id ??= "";
		widget.Type ??= "";
		widget.Title ??= "";
		widget.Settings ??= LayoutJson.Map();
		widget.Extra ??= LayoutJson.Map();
		return widget;
	}

	/// <summary>四元边距逐项钳位(null → 默认)。</summary>
	private static double[] SanitizePad(double[]? pad, double[] dflt)
	{
		double[] result = new double[4];
		for (int i = 0; i < 4; i++)
		{
			double value = ((pad != null && i < pad.Length) ? pad[i] : dflt[i]);
			result[i] = Clamp(value, PadMin, PadMax, dflt[i]);
		}
		return result;
	}

	/// <summary>按白名单过滤并去重(保持原顺序)。</summary>
	private static List<string> FilterKeys(List<string>? keys, Func<string?, bool> allowed)
	{
		List<string> list = new List<string>();
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		if (keys != null)
		{
			foreach (string key in keys)
			{
				if (allowed(key) && seen.Add(key))
				{
					list.Add(key);
				}
			}
		}
		return list;
	}
}
