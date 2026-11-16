using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core.Launcher.Layout;
using TipCL.Pages;

namespace TipCL;

/// <summary>
/// R32-MAC-TABS:布局 v2 文档的 <c>shell.tabs</c> → **Mac 侧栏**的接线层
/// (U1 / R32-D26 的裁定:方案 **(a)** —— <c>shell.tabs</c> 当新增真源,只驱动侧栏里能对上的 5 个语义键;
/// Mac 多出的两页(<c>ai</c> / <c>tasks</c>)保持原生 NavConfig 行为,登记进偏离清单)。
///
/// <para><b>为什么需要这一层</b>:Windows 的 <c>ShellTabs</c>(<c>windos/src/TipCL/Pages/Layout/ShellTabs.cs</c>,288 行)
/// 驱动的是**顶栏页签**,而 Mac 是**侧栏**(<c>MainWindow.SyncSidebar</c>),真源本是后端 settings 的
/// <c>ui_nav_*</c>(<see cref="NavConfig"/>)。R32-PARITY-ACCEPT §4 已实测:<c>4d04837</c> 把
/// <c>Pages/ShellTabs.cs</c> 落了盘,但**只被设置页布局视图消费**,<c>MainWindow.cs</c>/<c>NavModel.cs</c>
/// 相对 <c>26097e5</c> 一字未改 ⇒ 侧栏仍由 <c>NavConfig</c> 驱动(U1 未解)。本类补的就是这一段。</para>
///
/// <para><b>跨树映射表(D26:以 Mac 页集为超集,5 个语义键全映射,多出 2 页保持原生)</b></para>
/// <list type="table">
/// <item><term><c>shell.tabs</c> 键(Windows)</term><description>Mac 导航键 / 页面</description></item>
/// <item><term><c>launch</c></term><description><c>launch</c>(启动页)</description></item>
/// <item><term><c>library</c></term><description><c>instance</c>(版本管理;两端的 <c>library</c> 页都**没有侧栏行**,见 <c>LibraryPageRegistration.cs:19</c>)</description></item>
/// <item><term><c>download</c></term><description><c>download</c>(下载区块;Windows 的 <c>HighlightKeyOf("tasks") → download</c> 同义)</description></item>
/// <item><term><c>plugins</c></term><description><c>more</c>(Mac 的插件入口在「更多」分区里,<c>SubDefaultMembers["more"]</c> 含 <c>plugins</c>)</description></item>
/// <item><term><c>settings</c></term><description><c>settings</c>(设置页;唯一不可隐藏的键)</description></item>
/// </list>
/// <para>多出的 2 页 = <c>ai</c>(AI 助手)/ <c>tasks</c>(下载任务):<c>shell.tabs</c> 里没有对应键,
/// 一律按 <see cref="NavConfig"/> 的显隐与位置渲染(不许为对齐键数删 Mac 页面)。</para>
///
/// <para><b>回落语义(默认外观不许变样)</b>:文档里没有 <c>shell.tabs</c>、列表为空、
/// **或列表恰好等于出厂五键且顺序一致**(<c>LayoutStoreV2.Load</c> 在 v2 文件缺失/v1 迁移/首次运行时都
/// 会把 <c>Tabs</c> 填成 <see cref="LayoutSchema.DefaultTabs"/>,"未设置"与"设成出厂值"在文档里不可区分)
/// ⇒ 整段按**未设置**处理,<see cref="NavItems(NavConfig?)"/> 直接返回
/// <c>NavModel.NavItemsFromConfig</c> 的原始结果,一个字节都不改(这就是"与 <c>26097e5</c> 逐像素一致"的机械保证)。</para>
///
/// <para><b>写回(v2 文档 + v1 投影)</b>:<see cref="WriteOrder"/> / <see cref="SetVisible"/> 只把
/// Mac 导航键翻成 <c>shell.tabs</c> 的键再交给 <see cref="ShellTabs"/>(<c>SetTabs</c> / <c>SetTabVisible</c>),
/// 落盘仍走 <c>LayoutStoreV2.Save</c> —— 那里**先原子写 v2、再单向写通 v1 投影**
/// (<c>ui_page_layouts.json</c>,经 <c>PagesLayoutV1Bridge</c>),与 Windows 同一口径。</para>
/// </summary>
public static class ShellTabPlan
{
	/// <summary>跨树映射表:Windows <c>shell.tabs</c> 键 → Mac 导航键(顺序 = 出厂页签顺序,也是写入顺序的默认值)。</summary>
	public static readonly (string TabKey, string NavKey)[] Mapping = new (string, string)[5]
	{
		(ShellTabs.LaunchKey, "launch"),
		(ShellTabs.LibraryKey, "instance"),
		(ShellTabs.DownloadKey, "download"),
		(ShellTabs.PluginsKey, "more"),
		(ShellTabs.SettingsKey, "settings")
	};

	/// <summary>被 <c>shell.tabs</c> 驱动的 5 个 Mac 导航键(<c>launch / instance / download / more / settings</c>)。</summary>
	public static readonly string[] MappedNavKeys = Mapping.Select(pair => pair.NavKey).ToArray();

	/// <summary>Mac 多出的两页(<c>ai</c> / <c>tasks</c>):<c>shell.tabs</c> 里没有对应键,保持原生 NavConfig 行为。</summary>
	public static readonly string[] NativeNavKeys = new string[2] { "ai", "tasks" };

	/// <summary>Windows 页签键 → Mac 导航键;非映射键(未知 / 空)返回 null。</summary>
	public static string? NavKeyOf(string? tabKey)
	{
		if (string.IsNullOrEmpty(tabKey))
		{
			return null;
		}
		foreach ((string tab, string nav) in Mapping)
		{
			if (string.Equals(tab, tabKey, StringComparison.Ordinal))
			{
				return nav;
			}
		}
		return null;
	}

	/// <summary>Mac 导航键 → Windows 页签键;不属于映射表的键(Mac 多出的两页等)返回 null。</summary>
	public static string? TabKeyOf(string? navKey)
	{
		if (string.IsNullOrEmpty(navKey))
		{
			return null;
		}
		foreach ((string tab, string nav) in Mapping)
		{
			if (string.Equals(nav, navKey, StringComparison.Ordinal))
			{
				return tab;
			}
		}
		return null;
	}

	/// <summary>这个 Mac 导航键是否被 <c>shell.tabs</c> 驱动(5 个映射键为真;<c>ai</c>/<c>tasks</c> 与未知键为假)。</summary>
	public static bool IsMappedNavKey(string? navKey)
	{
		return TabKeyOf(navKey) is not null;
	}

	/// <summary>
	/// 文档里的 <c>shell.tabs</c> 是否**用户改过**:列表非空,且与出厂 <see cref="LayoutSchema.DefaultTabs"/> 不是逐项相同。
	/// 出厂值 = 未设置(<c>LayoutStoreV2.Load</c> 的默认填充、v1 迁移、v2 缺失都落在这个值上)⇒ 侧栏回落 NavConfig,默认外观零变化。
	/// </summary>
	public static bool HasCustomTabs(ShellLayout? shell)
	{
		List<string>? tabs = shell?.Tabs;
		if (tabs is null || tabs.Count == 0)
		{
			return false;
		}
		string[] defaults = LayoutSchema.DefaultTabs;
		if (tabs.Count != defaults.Length)
		{
			return true;
		}
		for (int i = 0; i < defaults.Length; i++)
		{
			if (!string.Equals(tabs[i], defaults[i], StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// 生效的 Mac 侧栏顺序(只含 5 个映射键,按文档顺序)。**文档里没点名的映射键 = 被隐藏**
	/// (<c>shell.tabs</c> 是"集合"语义,与 Windows <see cref="ShellTabs.OrderedKeys"/> 一致);
	/// <c>settings</c> 由 <see cref="ShellTabs.OrderedKeys"/> 保证恒在末尾,所以它不会进 <see cref="HiddenMapped"/>。
	/// </summary>
	public static List<string> MappedOrder(ShellLayout? shell)
	{
		List<string> order = new List<string>();
		foreach (string tab in ShellTabs.OrderedKeys(shell))
		{
			string? nav = NavKeyOf(tab);
			if (nav is not null && !order.Contains(nav))
			{
				order.Add(nav);
			}
		}
		return order;
	}

	/// <summary>文档把哪几个映射键隐掉了(按映射表顺序;<c>settings</c> 不会出现在这里)。</summary>
	public static List<string> HiddenMapped(ShellLayout? shell)
	{
		List<string> visible = MappedOrder(shell);
		return MappedNavKeys.Where(key => !visible.Contains(key)).ToList();
	}

	/// <summary>当前文档的 shell 段(<see cref="ShellTabs.CurrentShell"/> 的转发,读失败 → 全默认 ⇒ 回落)。</summary>
	public static ShellLayout CurrentShell()
	{
		return ShellTabs.CurrentShell();
	}

	/// <summary>
	/// 侧栏行表:<see cref="NavModel.NavItemsFromConfig"/> 的结果叠加 <c>shell.tabs</c> 的计划
	/// (文档未设置时**原样返回**,不新建对象内容)。
	/// </summary>
	public static List<NavEntry> NavItems(NavConfig? cfg)
	{
		return NavItems(cfg, CurrentShell());
	}

	/// <summary>
	/// 侧栏行表(显式给 shell 段,测试与 RPC 路径用)。
	/// 叠加规则:
	/// <list type="number">
	/// <item>5 个映射键的**显隐**以文档为准(文档里没有的键 = 隐藏,与 Windows 的"集合语义"一致);</item>
	/// <item>5 个映射键之间的**相对顺序**以文档为准,但落点沿用 NavConfig 给它们的位置(槽位置换)
	/// —— 分组头与 Mac 多出的两页因此原地不动;</item>
	/// <item>被抽空的分组头一并抽掉(与 <c>NavModel.GroupedNavItems</c> 的"空组跳过"同一条线),
	/// 撑条按 <c>NavModel</c> 的规则重新压到第一个底部键之前(精简式布局)。</item>
	/// </list>
	/// </summary>
	public static List<NavEntry> NavItems(NavConfig? cfg, ShellLayout? shell)
	{
		if (!HasCustomTabs(shell))
		{
			// 回落:与改动前逐字节等价(默认外观不变样的机械保证)。
			return NavModel.NavItemsFromConfig(cfg);
		}

		// 映射键的显隐真源从此是 shell.tabs ⇒ 先摘掉 NavConfig 对它们的 hidden(否则文档"显示"压不过配置"隐藏")。
		NavConfig? effective = cfg;
		if (cfg is not null)
		{
			List<string> hidden = cfg.Hidden.Where(key => !IsMappedNavKey(key)).ToList();
			if (hidden.Count != cfg.Hidden.Count)
			{
				effective = cfg.Clone();
				effective.Hidden = hidden;
			}
		}

		return Overlay(NavModel.NavItemsFromConfig(effective), shell);
	}

	/// <summary>侧栏上真正看得见的键顺序(条目行;<c>ai</c>/<c>tasks</c> 与 5 个映射键都在里面)。</summary>
	public static List<string> SidebarKeys(NavConfig? cfg, ShellLayout? shell)
	{
		return (from item in NavItems(cfg, shell)
			where item.Kind == NavEntryKind.Item
			select item.Key).ToList();
	}

	// ————————————————————————————— 写回(v2 文档 + v1 投影) —————————————————————————————

	/// <summary>
	/// 按给定的 Mac 侧栏顺序(<b>只认 5 个映射键</b>,其余键忽略)整体替换 <c>shell.tabs</c>。
	/// 落盘走 <see cref="ShellTabs.SetTabs"/> → <c>LayoutStoreV2.Save</c>(先 v2、再单向写通 v1 投影)。
	/// 传空/全是不认识键 → 文档写回出厂五键(= 未设置 ⇒ 侧栏回落 NavConfig)。
	/// </summary>
	public static ShellLayout WriteOrder(IEnumerable<string>? navKeysInOrder)
	{
		List<string> tabs = new List<string>();
		if (navKeysInOrder is not null)
		{
			foreach (string navKey in navKeysInOrder)
			{
				string? tab = TabKeyOf(navKey);
				if (tab is not null && !tabs.Contains(tab))
				{
					tabs.Add(tab);
				}
			}
		}
		return ShellTabs.SetTabs(tabs);
	}

	/// <summary>
	/// 显示/隐藏一个**映射键**对应的侧栏行(写 <c>shell.tabs</c>;<c>settings</c> 不可隐藏)。
	/// 非映射键(Mac 多出的 <c>ai</c> / <c>tasks</c>)返回当前文档、**不写盘** —— 那两页的显隐仍归
	/// <see cref="NavConfig"/>(<c>ui_nav_*</c>)管,这是 D26 的超集规则。
	/// </summary>
	public static ShellLayout SetVisible(string navKey, bool visible)
	{
		string? tab = TabKeyOf(navKey);
		if (tab is null)
		{
			return CurrentShell();
		}
		return ShellTabs.SetTabVisible(tab, visible);
	}

	/// <summary>把 <c>shell.tabs</c> 复位成出厂五键(= 未设置)⇒ 侧栏回到 NavConfig 原生行为。</summary>
	public static ShellLayout ResetToFactory()
	{
		return ShellTabs.SetTabs(LayoutSchema.DefaultTabs);
	}

	// ————————————————————————————— 叠加算法 —————————————————————————————

	private static List<NavEntry> Overlay(IReadOnlyList<NavEntry> entries, ShellLayout? shell)
	{
		List<string> documentOrder = MappedOrder(shell);
		HashSet<string> documentSet = new HashSet<string>(documentOrder, StringComparer.Ordinal);

		// 映射键的**槽位**:基准表里它们出现的位置(上面已摘掉 NavConfig 对它们的 hidden)。
		Dictionary<string, NavEntry> byKey = new Dictionary<string, NavEntry>(StringComparer.Ordinal);
		HashSet<int> liveSlots = new HashSet<int>();
		bool draggable = false;
		for (int i = 0; i < entries.Count; i++)
		{
			NavEntry entry = entries[i];
			if (entry.Kind != NavEntryKind.Item || !IsMappedNavKey(entry.Key))
			{
				continue;
			}
			byKey[entry.Key] = entry;
			draggable = draggable || entry.Draggable;
			if (documentSet.Contains(entry.Key))
			{
				liveSlots.Add(i);
			}
		}

		List<NavEntry> result = new List<NavEntry>();
		bool hadStretch = false;
		int cursor = 0;
		for (int i = 0; i < entries.Count; i++)
		{
			NavEntry entry = entries[i];
			if (entry.Kind == NavEntryKind.Stretch)
			{
				hadStretch = true;
				continue;
			}
			if (entry.Kind == NavEntryKind.Item && IsMappedNavKey(entry.Key))
			{
				if (!liveSlots.Contains(i))
				{
					continue;
				}
				result.Add(Mapped(documentOrder[cursor], byKey, draggable));
				cursor++;
				continue;
			}
			result.Add(entry);
		}

		// 保底:文档点名要显示、但基准表里没有槽位的映射键(自定义分组把它们挤掉了)追加在末尾。
		while (cursor < documentOrder.Count)
		{
			result.Add(Mapped(documentOrder[cursor], byKey, draggable));
			cursor++;
		}

		if (hadStretch)
		{
			RestoreStretch(result);
		}
		return DropEmptyHeaders(result);
	}

	private static NavEntry Mapped(string navKey, Dictionary<string, NavEntry> byKey, bool draggable)
	{
		if (byKey.TryGetValue(navKey, out NavEntry? entry))
		{
			return entry;
		}
		return new NavEntry(NavEntryKind.Item, navKey, NavModel.NavLabel(navKey), NavModel.IsTop(navKey), draggable);
	}

	/// <summary>
	/// 撑条落点照 <c>NavModel.NavItemsFromConfig</c> 的规则重算:第一个底部键之前。
	/// 重排把某个底部键(settings/ai/more/tasks)顶到了第一行时,这个位置是 0 —— 撑条无处可插,
	/// 直接不放(<c>NavModel</c> 的 <c>stretchAt &lt;= 0 → -1</c> 同一条线),登记在偏离清单里。
	/// </summary>
	private static void RestoreStretch(List<NavEntry> entries)
	{
		int at = entries.FindIndex(entry => entry.Kind == NavEntryKind.Item && NavModel.BottomKeys.Contains(entry.Key));
		if (at > 0)
		{
			entries.Insert(at, NavEntry.Stretch);
		}
	}

	/// <summary>抽掉"下面到下一个分组头之前一个条目都没有"的分组头(与 <c>NavModel</c> 的"空组跳过"同一条线)。</summary>
	private static List<NavEntry> DropEmptyHeaders(List<NavEntry> entries)
	{
		List<NavEntry> list = new List<NavEntry>();
		for (int i = 0; i < entries.Count; i++)
		{
			if (entries[i].Kind != NavEntryKind.Header)
			{
				list.Add(entries[i]);
				continue;
			}
			bool hasItem = false;
			for (int j = i + 1; j < entries.Count && entries[j].Kind != NavEntryKind.Header; j++)
			{
				if (entries[j].Kind == NavEntryKind.Item)
				{
					hasItem = true;
					break;
				}
			}
			if (hasItem)
			{
				list.Add(entries[i]);
			}
		}
		return list;
	}
}
