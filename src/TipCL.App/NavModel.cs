using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Services;

namespace TipCL;

/// <summary>
/// 导航模型:把 <see cref="NavConfig"/> 折叠成侧栏行、分区成员、可见顺序,并提供 pin/unpin/
/// reorder 这些"改配置"的补丁计算。**逐行照搬 Windows 版 <c>NavModel.cs</c>**:
/// 公开成员(常量、静态表、方法名与参数默认值)、键名字符串、默认顺序/置顶/隐藏、分组表、
/// 分区成员规则、可见性规则、排序分支**全部一字不改** —— 后续每个页面包都靠这张表把页面 id 接进导航。
///
/// 唯一不可照搬的是 <c>DependencyProperty</c> 之类 WPF 专有类型,本文件里没有;
/// <c>I18n.L</c> 与 mac 侧同名同义(未初始化时原样返回中文键)。
/// </summary>
public static class NavModel
{
	public sealed record PinResult(Dictionary<string, object?>? Patch, string? Error);

	public const string StyleCompact = "compact";

	public const string StyleGrouped = "grouped";

	public const string DefaultStyle = "grouped";

	public static readonly IReadOnlyDictionary<string, string> StyleLabels = new Dictionary<string, string>
	{
		["compact"] = I18n.L("精简(启动 / 游戏 / 版本管理)"),
		["grouped"] = I18n.L("分组(账户 / 游戏 / 通用)")
	};

	public static readonly string[] SectionIds = new string[2] { "download", "more" };

	public static readonly IReadOnlyDictionary<string, string[]> SubDefaultMembers = new Dictionary<string, string[]>
	{
		["download"] = Array.Empty<string>(),
		["more"] = new string[5] { "instance", "account", "multiplayer", "settings", "plugins" }
	};

	public static readonly HashSet<string> AllSubKeys = new HashSet<string>(SectionIds.SelectMany((string sec) => SubDefaultMembers[sec]));

	public static readonly string[] TopKeys = new string[5] { "launch", "download", "ai", "more", "tasks" };

	public static readonly IReadOnlyDictionary<string, string> SubTitles = new Dictionary<string, string>
	{
		["instance"] = I18n.L("版本管理"),
		["account"] = I18n.L("账号"),
		["multiplayer"] = I18n.L("联机"),
		["settings"] = I18n.L("设置"),
		["plugins"] = I18n.L("插件")
	};

	public static readonly IReadOnlyDictionary<string, string> NavSpecs = new Dictionary<string, string>
	{
		["launch"] = I18n.L("启动"),
		["download"] = I18n.L("游戏"),
		["ai"] = I18n.L("AI 助手"),
		["more"] = I18n.L("更多"),
		["tasks"] = I18n.L("下载任务")
	};

	public const string NavDefaultsVersion = "2026.09-ai-visible";

	public static readonly string[] DefaultNavOrder = new string[7] { "launch", "download", "instance", "ai", "more", "settings", "tasks" };

	public static readonly string[] DefaultNavPinned = new string[2] { "instance", "settings" };

	public static readonly string[] DefaultNavHidden = Array.Empty<string>();

	private static readonly Dictionary<string, string[]>[] LegacyNavFactories = new Dictionary<string, string[]>[1]
	{
		new Dictionary<string, string[]>
		{
			["ui_nav_order"] = new string[6] { "launch", "download", "instance", "more", "settings", "tasks" },
			["ui_nav_pinned"] = new string[2] { "instance", "settings" },
			["ui_nav_hidden"] = new string[1] { "ai" }
		}
	};

	public static readonly string[] BottomKeys = new string[4] { "ai", "more", "settings", "tasks" };

	// WP4 信息架构:平级入口 → 6 个分组顶级项(WP-S4 删除空「反馈」组)。
	// 分组里直接点名的子页(instance/multiplayer/account/settings)不再进
	// download/more 分区(PinnedFromConfig 去重);plugins 留在「更多」分区,挂在「工具」组下。
	public static readonly (string Title, string[] Keys)[] GroupedNav = new (string, string[])[6]
	{
		(I18n.L("启动"), new string[1] { "launch" }),
		(I18n.L("库"), new string[3] { "instance", "download", "multiplayer" }),
		(I18n.L("社区"), new string[1] { "account" }),
		(I18n.L("AI 助手"), new string[1] { "ai" }),
		(I18n.L("工具"), new string[2] { "tasks", "more" }),
		(I18n.L("设置"), new string[1] { "settings" })
	};

	public static readonly IReadOnlyDictionary<string, string> GroupedLabels = new Dictionary<string, string>
	{
		["download"] = I18n.L("下载"),
		["multiplayer"] = I18n.L("多人联机")
	};

	/// <summary>导航键 → 页面 id。**页面包靠这张表绑定自己的 id**(Windows 同名同值)。</summary>
	public static readonly IReadOnlyDictionary<string, string> PageForNavKey = new Dictionary<string, string>
	{
		["launch"] = "launch",
		["ai"] = "ai",
		["tasks"] = "tasks",
		["instance"] = "instance",
		["account"] = "account",
		["multiplayer"] = "multiplayer",
		["settings"] = "settings",
		["plugins"] = "plugins"
	};

	public static bool IsTop(string key)
	{
		return Array.IndexOf(TopKeys, key) >= 0;
	}

	public static bool IsSection(string key)
	{
		return Array.IndexOf(SectionIds, key) >= 0;
	}

	/// <summary>样式归一化:不认识的样式(含 null)当分组式(Windows 同)。</summary>
	public static string NavStyle(NavConfig? cfg)
	{
		string style = cfg?.Style ?? "";
		return StyleLabels.ContainsKey(style) ? style : "grouped";
	}

	/// <summary>导航键的显示名:先查顶级 <c>NavSpecs</c>,再查子页 <c>SubTitles</c>,都不认就原样返回键。</summary>
	public static string NavLabel(string key)
	{
		if (!NavSpecs.TryGetValue(key, out string? top))
		{
			if (!SubTitles.TryGetValue(key, out string? sub))
			{
				return key;
			}
			return sub;
		}
		return top;
	}

	/// <summary>两个分区的成员表:配置里的先来(去重、去掉已置顶的),再补默认成员(同样去重/去置顶)。</summary>
	public static Dictionary<string, List<string>> SectionMembersFromConfig(NavConfig? cfg)
	{
		HashSet<string> pinned = new HashSet<string>(PinnedFromConfig(cfg));
		Dictionary<string, List<string>> members = SectionIds.ToDictionary((string s) => s, (string _) => new List<string>());
		HashSet<string> seen = new HashSet<string>();
		foreach (string key in SectionIds)
		{
			IEnumerable<string> configured = ((cfg?.SectionMembers != null && cfg.SectionMembers.TryGetValue(key, out List<string>? value)) ? value : null) ?? new List<string>();
			foreach (string item in configured)
			{
				if (AllSubKeys.Contains(item) && !seen.Contains(item) && !pinned.Contains(item))
				{
					seen.Add(item);
					members[key].Add(item);
				}
			}
		}
		foreach (string key2 in SectionIds)
		{
			foreach (string item2 in SubDefaultMembers[key2])
			{
				if (!seen.Contains(item2) && !pinned.Contains(item2))
				{
					seen.Add(item2);
					members[key2].Add(item2);
				}
			}
		}
		return members;
	}

	public static string DefaultSectionFor(string key)
	{
		foreach (string section in SectionIds)
		{
			if (SubDefaultMembers[section].Contains(key))
			{
				return section;
			}
		}
		return "more";
	}

	/// <summary>分组布局:配置里的分组先用(只留认识的键、跨组去重),全空则退回 <see cref="GroupedNav"/>;缺的顶级键补进最后一组。</summary>
	public static List<NavGroup> GroupedLayout(NavConfig? cfg)
	{
		List<NavGroup> list = new List<NavGroup>();
		HashSet<string> seen = new HashSet<string>();
		if (cfg?.Groups != null)
		{
			foreach (NavGroup group in cfg.Groups)
			{
				string title = (group.Title ?? "").Trim();
				if (title.Length == 0)
				{
					continue;
				}
				List<string> keys = new List<string>();
				foreach (string key in group.Keys)
				{
					if (!string.IsNullOrEmpty(key) && (IsTop(key) || AllSubKeys.Contains(key)) && seen.Add(key))
					{
						keys.Add(key);
					}
				}
				list.Add(new NavGroup
				{
					Title = title,
					Keys = keys
				});
			}
		}
		if (!list.Any((NavGroup g) => g.Keys.Count > 0))
		{
			return GroupedNav.Select(g => new NavGroup
			{
				Title = g.Title,
				Keys = new List<string>(g.Keys)
			}).ToList();
		}
		List<string> missing = TopKeys.Where((string k) => !seen.Contains(k)).ToList();
		if (missing.Count > 0)
		{
			list[list.Count - 1].Keys.AddRange(missing);
		}
		return list;
	}

	public static Dictionary<string, object?> GroupedLayoutPatch(List<NavGroup> groups)
	{
		return new Dictionary<string, object> { ["ui_nav_groups"] = groups.Select((NavGroup g) => g.Clone()).ToList() };
	}

	/// <summary>在分组内搬动一个键(跨组先移除);目标不在任何组里就返回 false(配置原样不动)。</summary>
	public static bool MoveWithinGroups(List<NavGroup> groups, string key, string target, bool before)
	{
		if (key == target || !groups.Any((NavGroup g) => g.Keys.Contains(target)))
		{
			return false;
		}
		foreach (NavGroup group in groups)
		{
			group.Keys.Remove(key);
		}
		foreach (NavGroup group2 in groups)
		{
			int at = group2.Keys.IndexOf(target);
			if (at >= 0)
			{
				group2.Keys.Insert(at + ((!before) ? 1 : 0), key);
				return true;
			}
		}
		return false;
	}

	/// <summary>置顶(侧栏顶级)的子页键:分组式从分组布局里取,精简式从 <c>ui_nav_pinned</c> 取(都去隐藏)。</summary>
	public static List<string> PinnedFromConfig(NavConfig? cfg)
	{
		HashSet<string> hidden = new HashSet<string>(cfg?.Hidden ?? new List<string>());
		if (NavStyle(cfg) == "grouped")
		{
			return (from k in GroupedLayout(cfg).SelectMany((NavGroup g) => g.Keys)
				where AllSubKeys.Contains(k) && !hidden.Contains(k)
				select k).ToList();
		}
		List<string> list = new List<string>();
		HashSet<string> seen = new HashSet<string>();
		foreach (string item in cfg?.Pinned ?? new List<string>())
		{
			if (AllSubKeys.Contains(item) && seen.Add(item))
			{
				list.Add(item);
			}
		}
		return list;
	}

	private static NavEntry ItemEntry(string key, string label, bool draggable)
	{
		return new NavEntry(NavEntryKind.Item, key, label, IsTop(key), draggable);
	}

	/// <summary>分组式的行表:分组头 + 条目(空组被跳过;分组标签优先)。</summary>
	public static List<NavEntry> GroupedNavItems(NavConfig? cfg)
	{
		HashSet<string> hidden = new HashSet<string>(cfg?.Hidden ?? new List<string>());
		List<NavEntry> list = new List<NavEntry>();
		foreach (NavGroup group in GroupedLayout(cfg))
		{
			List<string> keys = group.Keys.Where((string k) => !hidden.Contains(k)).ToList();
			if (keys.Count == 0)
			{
				continue;
			}
			list.Add(NavEntry.Header(group.Title));
			foreach (string key in keys)
			{
				list.Add(ItemEntry(key, GroupedLabels.TryGetValue(key, out string? label) ? label : NavLabel(key), draggable: false));
			}
		}
		return list;
	}

	/// <summary>按当前样式给出侧栏行表(分组式 / 精简式两个分支,顺序与去重规则照 Windows)。</summary>
	public static List<NavEntry> NavItemsFromConfig(NavConfig? cfg)
	{
		if (NavStyle(cfg) == "grouped")
		{
			return GroupedNavItems(cfg);
		}
		List<string> configured = cfg?.Order ?? new List<string>();
		List<string> pinned = PinnedFromConfig(cfg);
		HashSet<string> hidden = new HashSet<string>(cfg?.Hidden ?? new List<string>());
		List<string> order = new List<string>();
		foreach (string item in configured)
		{
			if (IsTop(item) && !order.Contains(item))
			{
				order.Add(item);
			}
			else if (pinned.Contains(item) && !order.Contains(item))
			{
				order.Add(item);
			}
		}
		foreach (string top in TopKeys)
		{
			if (!order.Contains(top))
			{
				order.Add(top);
			}
		}
		List<string> leftover = pinned.Where((string k) => !order.Contains(k)).ToList();
		if (leftover.Count > 0)
		{
			string? anchor = new string[2] { "more", "tasks" }.FirstOrDefault((string a) => order.Contains(a));
			if (anchor != null)
			{
				order.InsertRange(order.IndexOf(anchor), leftover);
			}
			else
			{
				order.AddRange(leftover);
			}
		}
		List<string> visible = order.Where((string k) => !IsTop(k) || !hidden.Contains(k)).ToList();
		int stretchAt = visible.FindIndex((string k) => BottomKeys.Contains(k));
		if (stretchAt <= 0)
		{
			stretchAt = -1;
		}
		List<NavEntry> entries = new List<NavEntry>();
		for (int i = 0; i < visible.Count; i++)
		{
			if (i == stretchAt)
			{
				entries.Add(NavEntry.Stretch);
			}
			entries.Add(ItemEntry(visible[i], NavLabel(visible[i]), draggable: true));
		}
		return entries;
	}

	private static Dictionary<string, string[]> NavFactory()
	{
		return new Dictionary<string, string[]>
		{
			["ui_nav_order"] = DefaultNavOrder,
			["ui_nav_pinned"] = DefaultNavPinned,
			["ui_nav_hidden"] = DefaultNavHidden
		};
	}

	/// <summary>这份配置是不是从没被改过(等于现行默认表或任一历史默认表 —— 老存档要能自动升到新默认)。</summary>
	public static bool NavUntouched(NavConfig? cfg)
	{
		Dictionary<string, List<string>> stored = new Dictionary<string, List<string>>
		{
			["ui_nav_order"] = cfg?.Order ?? new List<string>(),
			["ui_nav_pinned"] = cfg?.Pinned ?? new List<string>(),
			["ui_nav_hidden"] = cfg?.Hidden ?? new List<string>()
		};
		return new Dictionary<string, string[]>[1] { NavFactory() }.Concat(LegacyNavFactories).Any((Dictionary<string, string[]> factory) =>
			factory.All((KeyValuePair<string, string[]> kv) =>
			{
				List<string> list = stored[kv.Key];
				return list.Count == 0 || list.SequenceEqual(kv.Value);
			}));
	}

	/// <summary>恢复导航默认值的补丁(含 <c>ui_nav_defaults</c> 版本戳与两个 null 复位)。</summary>
	public static Dictionary<string, object?> DefaultNavPatch()
	{
		return new Dictionary<string, object>
		{
			["ui_nav_order"] = DefaultNavOrder.ToList(),
			["ui_nav_pinned"] = DefaultNavPinned.ToList(),
			["ui_nav_hidden"] = DefaultNavHidden.ToList(),
			["ui_nav_style"] = "grouped",
			["ui_nav_defaults"] = "2026.09-ai-visible",
			["ui_nav_groups"] = null,
			["ui_section_members"] = null
		};
	}

	public static List<string> VisibleSections(NavConfig? cfg)
	{
		HashSet<string> shown = new HashSet<string>(from it in NavItemsFromConfig(cfg)
			where it.Kind == NavEntryKind.Item
			select it.Key);
		return SectionIds.Where(shown.Contains).ToList();
	}

	/// <summary>取消置顶:把这个子页放回某个分区(补丁 + 落点分区)。Windows 的同名方法,分支逻辑照抄。</summary>
	public static (Dictionary<string, object?> Patch, string Section)? UnpinNavConfig(NavConfig? cfg, string key, string? backSection = null, int index = -1)
	{
		List<string> pinned = PinnedFromConfig(cfg);
		if (!pinned.Contains(key))
		{
			return null;
		}
		List<string> remaining = pinned.Where((string k) => k != key).ToList();
		Dictionary<string, object> patch = new Dictionary<string, object>();
		NavConfig baseCfg = cfg ?? new NavConfig();
		NavConfig next;
		if (NavStyle(cfg) == "grouped")
		{
			List<NavGroup> groups = GroupedLayout(cfg);
			foreach (NavGroup group in groups)
			{
				group.Keys.Remove(key);
			}
			foreach ((string patchKey, object? patchValue) in GroupedLayoutPatch(groups))
			{
				patch[patchKey] = patchValue!;
			}
			next = baseCfg.With(patch);
		}
		else
		{
			patch["ui_nav_pinned"] = remaining;
			next = baseCfg.With(new Dictionary<string, object> { ["ui_nav_pinned"] = remaining });
		}
		Dictionary<string, List<string>> members = SectionMembersFromConfig(next);
		string section = ((backSection != null && IsSection(backSection)) ? backSection : (SectionIds.FirstOrDefault((string sec) => members[sec].Contains(key)) ?? DefaultSectionFor(key)));
		int at = index;
		List<string> visible = VisibleSections(next);
		if (!visible.Contains(section))
		{
			if (visible.Count > 0)
			{
				section = visible[0];
				at = -1;
			}
			else
			{
				patch["ui_nav_hidden"] = (cfg?.Hidden ?? new List<string>()).Where((string k) => k != section).ToList();
			}
		}
		Dictionary<string, List<string>> dropped = SectionIds.ToDictionary((string sec) => sec, (string sec) => members[sec].Where((string k) => k != key).ToList());
		List<string> target = dropped[section];
		target.Insert((at >= 0 && at <= target.Count) ? at : target.Count, key);
		patch["ui_section_members"] = dropped;
		return (patch, section);
	}

	/// <summary>侧栏上真正看得见的键顺序(只取条目行)。</summary>
	public static List<string> SidebarSequence(NavConfig? cfg)
	{
		return (from it in NavItemsFromConfig(cfg)
			where it.Kind == NavEntryKind.Item
			select it.Key).ToList();
	}

	public static Dictionary<string, object?> WriteSidebarSequence(IEnumerable<string> seq)
	{
		HashSet<string> seen = new HashSet<string>();
		List<string> list = seq.Where((string k) => (IsTop(k) || AllSubKeys.Contains(k)) && seen.Add(k)).ToList();
		foreach (string top in TopKeys)
		{
			if (!list.Contains(top))
			{
				list.Add(top);
			}
		}
		return new Dictionary<string, object>
		{
			["ui_nav_order"] = list,
			["ui_nav_pinned"] = list.Where(AllSubKeys.Contains).ToList()
		};
	}

	public static Dictionary<string, object?>? ReorderNavConfig(NavConfig? cfg, string key, string target, bool before)
	{
		if (NavStyle(cfg) == "grouped")
		{
			List<NavGroup> groups = GroupedLayout(cfg);
			if (!MoveWithinGroups(groups, key, target, before))
			{
				return null;
			}
			return GroupedLayoutPatch(groups);
		}
		List<string> seq = SidebarSequence(cfg);
		if (key == target || !seq.Contains(key) || !seq.Contains(target))
		{
			return null;
		}
		seq.Remove(key);
		seq.Insert(seq.IndexOf(target) + ((!before) ? 1 : 0), key);
		return WriteSidebarSequence(seq);
	}

	/// <summary>置顶(或移进另一组)。分区只剩一个子页时拒绝并把原因交回界面(Windows 同文案)。</summary>
	public static PinResult? PinNavConfig(NavConfig? cfg, string key, string? target = null, bool before = true)
	{
		if (!AllSubKeys.Contains(key))
		{
			return null;
		}
		if (PinnedFromConfig(cfg).Contains(key))
		{
			return null;
		}
		Dictionary<string, List<string>> members = SectionMembersFromConfig(cfg);
		string? source = SectionIds.FirstOrDefault((string sec) => members[sec].Contains(key));
		if (source != null && members[source].Count <= 1)
		{
			return new PinResult(null, I18n.L("该分区只剩这一个子页,移走会变空栏;先在「自定义分区」里补充其它子页"));
		}
		Dictionary<string, object> patch = new Dictionary<string, object>();
		if (NavStyle(cfg) == "grouped")
		{
			List<NavGroup> groups = GroupedLayout(cfg);
			string anchor = ((target != null && groups.Any((NavGroup g) => g.Keys.Contains(target))) ? target : "more");
			if (!MoveWithinGroups(groups, key, anchor, !(anchor == target) || before))
			{
				groups[groups.Count - 1].Keys.Add(key);
			}
			foreach ((string patchKey, object? patchValue) in GroupedLayoutPatch(groups))
			{
				patch[patchKey] = patchValue!;
			}
		}
		else
		{
			List<string> seq = SidebarSequence(cfg);
			string? anchor = ((target != null && seq.Contains(target)) ? target : (seq.Contains("more") ? "more" : null));
			if (anchor != null)
			{
				seq.Insert(seq.IndexOf(anchor) + ((anchor == target && !before) ? 1 : 0), key);
			}
			else
			{
				seq.Add(key);
			}
			foreach ((string patchKey, object? patchValue) in WriteSidebarSequence(seq))
			{
				patch[patchKey] = patchValue!;
			}
		}
		if (source != null)
		{
			patch["ui_section_members"] = SectionIds.ToDictionary((string sec) => sec, (string sec) => members[sec].Where((string k) => k != key).ToList());
		}
		return new PinResult(patch, null);
	}

	/// <summary>把拖拽得到的顶级顺序与置顶集合合回一条顺序表(Windows 同算法)。</summary>
	public static List<string> MergeNavOrder(NavConfig? cfg, IEnumerable<string> topOrder, IReadOnlyCollection<string> pinned)
	{
		List<string> seq = SidebarSequence(cfg);
		Queue<string> queue = new Queue<string>(topOrder);
		List<string> merged = new List<string>();
		foreach (string item in seq)
		{
			if (AllSubKeys.Contains(item))
			{
				if (pinned.Contains(item))
				{
					merged.Add(item);
				}
			}
			else if (queue.Count > 0)
			{
				merged.Add(queue.Dequeue());
			}
		}
		merged.AddRange(queue);
		foreach (string item2 in pinned)
		{
			if (!merged.Contains(item2))
			{
				merged.Add(item2);
			}
		}
		return merged;
	}

	/// <summary>RPC 参数化:分组列表转成 <c>{title, keys}</c> 的字典数组(Windows 同)。</summary>
	public static Dictionary<string, object?> ToRpcArgs(IReadOnlyDictionary<string, object?> patch)
	{
		Dictionary<string, object> args = new Dictionary<string, object>();
		foreach ((string key, object? value) in patch)
		{
			args[key] = (value is IEnumerable<NavGroup> source)
				? source.Select((NavGroup g) => new Dictionary<string, object>
				{
					["title"] = g.Title,
					["keys"] = new List<string>(g.Keys)
				}).ToList()
				: value!;
		}
		return args;
	}
}
