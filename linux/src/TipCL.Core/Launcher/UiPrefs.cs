// 来源: Windows 版参考仓库 src/TipCL/UiPrefs.cs(该版把它落成独立文件 wpf-ui.json,
// 由 MainWindow.xaml.cs:1427 LoadUiPrefs / :1442 SaveUiPrefs 读写)。
// Linux 侧不另造第二个配置文件:整棵偏好树挂 config.json 的 "ui_prefs" 对象键下,
// 与既有界面键(ui_dark / ui_motion / ui_layout / ui_notes ...)同一份文件、同一条读写路径。
//
// 键布局(snake_case,对齐 config.json 既有风格;Windows 字段名在括号里):
//   ui_prefs.expanded         { "<前缀>:<分组键>" → bool }  (Expanded)  前缀 lib: / vm: 区分页面
//   ui_prefs.vm_favorites     { "<实例名>" → ["<版本 id>", ...] }  (VmFavorites)
//   ui_prefs.catalog_instance "<实例名>"  (CatalogInstance)  下载页当前实例
//
// 三条硬约束:
//   1) 读一律现读 config.json 的内存投影(Config.CONFIG / 传入的 LauncherConfig),
//      **不在静态字段里留副本** —— 否则又会退化成「进程内记忆」(DECISIONS D-19 缺口 B)。
//   2) 缺键 / 空字典 / 类型不符 / config.json 损坏:一律取默认值,绝不抛异常打断启动。
//   3) 写失败只记日志(照 Windows SaveUiPrefs 的空 catch 风格,比它多留一行日志),
//      不影响交互。是否落盘由调用方显式 Save() 决定。
using System;
using System.Collections.Generic;

namespace TipCL.Core.Launcher;

/// <summary>
/// 界面偏好(UiPrefs):分组折叠态 / 版本收藏 / 下载页当前实例。
/// 落 config.json 的 <see cref="KEY"/> 键;读走 Config.CONFIG(现读,不缓存)。
/// </summary>
public sealed class UiPrefs
{
	/// <summary>config.json 里的偏好节点键。</summary>
	public const string KEY = "ui_prefs";

	/// <summary>折叠态前缀:实例库页(照 Windows LibraryPage 的 "lib:")。</summary>
	public const string LIB_PREFIX = "lib:";

	/// <summary>折叠态前缀:版本管理页(照 Windows VersionManagePage 的 "vm:")。</summary>
	public const string VM_PREFIX = "vm:";

	private const string EXPANDED_KEY = "expanded";

	private const string VM_FAVORITES_KEY = "vm_favorites";

	private const string CATALOG_INSTANCE_KEY = "catalog_instance";

	private readonly LauncherConfig _config;

	private readonly PyDict _node;

	private UiPrefs(LauncherConfig config, PyDict node)
	{
		_config = config;
		_node = node;
	}

	/// <summary>
	/// 现读偏好。缺键/类型不符/config.json 读不出来(含 JSON 损坏)时给空节点 + 默认值,不抛。
	/// </summary>
	/// <param name="config">测试用的独立配置实例;null = 进程内 Config.CONFIG。</param>
	public static UiPrefs Load(LauncherConfig config = null)
	{
		try
		{
			LauncherConfig cfg = config ?? Config.CONFIG;
			return new UiPrefs(cfg, Py.DictOr(cfg.Get(KEY)));
		}
		catch (Exception ex)
		{
			// config.json 损坏到 LauncherConfig 都构造不出来(如非法 UTF-8)时,
			// 启动流程照常跑,只是这次偏好全用默认值。
			Utils.log.Warning("读取界面偏好失败(改用默认值): %s", Py.ExcStr(ex));
			return new UiPrefs(null, new PyDict());
		}
	}

	/// <summary>
	/// 把整棵偏好树写回 config.json(经 LauncherConfig.Save,自带 tmp+替换与重试)。
	/// 写失败只记日志,不影响交互。
	/// </summary>
	public void Save()
	{
		try
		{
			LauncherConfig cfg = _config ?? Config.CONFIG;
			cfg.Set(KEY, _node);
			cfg.Save();
		}
		catch (Exception ex)
		{
			Utils.log.Warning("界面偏好写入失败(已忽略): %s", Py.ExcStr(ex));
		}
	}

	// ———————————————————————— 分组折叠态 ————————————————————————

	/// <summary>读折叠态;没记过或值不是布尔(手改坏 JSON)→ 用默认值。</summary>
	public bool GetExpanded(string key, bool dflt)
	{
		if (string.IsNullOrEmpty(key))
		{
			return dflt;
		}
		object raw = Py.DictOr(_node.Get(EXPANDED_KEY)).Get(key);
		return raw is bool open ? open : dflt;
	}

	/// <summary>记折叠态(只改内存,落盘请调 <see cref="Save"/>)。</summary>
	public void SetExpanded(string key, bool open)
	{
		if (string.IsNullOrEmpty(key))
		{
			return;
		}
		PyDict map = Py.DictOr(_node.Get(EXPANDED_KEY));
		map[key] = open;
		_node[EXPANDED_KEY] = map;
	}

	/// <summary>整份折叠态快照(排查 / 测试用;非布尔项按丢弃处理,与 GetExpanded 同口径)。</summary>
	public Dictionary<string, bool> ExpandedMap()
	{
		var map = new Dictionary<string, bool>(StringComparer.Ordinal);
		foreach (KeyValuePair<string, object> kv in Py.DictOr(_node.Get(EXPANDED_KEY)))
		{
			if (kv.Value is bool open)
			{
				map[kv.Key] = open;
			}
		}
		return map;
	}

	// ———————————————————————— 版本收藏(VmFavorites)————————————————————————

	/// <summary>某实例收藏的版本 id(只读副本;没这个实例 → 空表)。</summary>
	public List<string> Favorites(string instance)
	{
		var list = new List<string>();
		foreach (object item in FavoritesNode(instance, create: false))
		{
			string id = Py.S(item);
			if (id.Length > 0)
			{
				list.Add(id);
			}
		}
		return list;
	}

	/// <summary>这个版本在不在该实例的收藏里。</summary>
	public bool IsFavorite(string instance, string versionId)
	{
		if (string.IsNullOrEmpty(instance) || string.IsNullOrEmpty(versionId))
		{
			return false;
		}
		return FindFavorite(FavoritesNode(instance, create: false), versionId) >= 0;
	}

	/// <summary>收藏 ⇄ 取消收藏(只改内存,落盘请调 <see cref="Save"/>)。返回切换后的状态。</summary>
	public bool ToggleFavorite(string instance, string versionId)
	{
		if (string.IsNullOrEmpty(instance) || string.IsNullOrEmpty(versionId))
		{
			// 空实例名/空版本 id 是脏输入:不落键,免得写出一堆空表
			return false;
		}
		List<object> list = FavoritesNode(instance, create: true);
		int at = FindFavorite(list, versionId);
		if (at >= 0)
		{
			list.RemoveAt(at);
			return false;
		}
		list.Add(versionId);
		return true;
	}

	// 取(必要时建)某实例的收藏数组;数组挂在节点上,改它即改配置树
	private List<object> FavoritesNode(string instance, bool create)
	{
		PyDict map = Py.DictOr(_node.Get(VM_FAVORITES_KEY));
		_node[VM_FAVORITES_KEY] = map;
		if (map.Get(instance) is List<object> list)
		{
			return list;
		}
		if (!create)
		{
			return new List<object>();
		}
		list = new List<object>();
		map[instance] = list;
		return list;
	}

	private static int FindFavorite(List<object> list, string versionId)
	{
		for (int i = 0; i < list.Count; i++)
		{
			if (string.Equals(Py.S(list[i]), versionId, StringComparison.Ordinal))
			{
				return i;
			}
		}
		return -1;
	}

	// ———————————————————————— 下载页当前实例(CatalogInstance)————————————————————————

	/// <summary>下载页当前实例名(没记过 = 空串)。只改内存,落盘请调 <see cref="Save"/></summary>
	public string CatalogInstance
	{
		get => Py.S(_node.Get(CATALOG_INSTANCE_KEY));
		set => _node[CATALOG_INSTANCE_KEY] = value ?? "";
	}
}
