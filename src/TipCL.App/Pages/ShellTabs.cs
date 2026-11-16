using System;
using System.Collections.Generic;
using TipCL.Core.Launcher.Layout;
using TipCL.Services;

namespace TipCL.Pages;

// 来源:Windows 版冻结件 wt-r31-merge @0a304a7
//       windos/src/TipCL/Pages/Layout/ShellTabs.cs(288 行,`namespace TipCL.Pages.Layout`)。
//
// 移植方式:**逐字等价**(本类零 WPF 依赖,只碰 LayoutStoreV2 / LayoutSchema / I18n)。
// 唯一改动是命名空间 `TipCL.Pages.Layout` → `TipCL.Pages`(mac 树 Pages 是平铺的,D7)。
//
// 为什么本包必须移植它(R32-MACMORE §ShellTabs 处置):
//   Windows 的 `SettingsPage.LayoutShellRows()`(`SettingsLayoutPage.cs:LayoutShellRows`)直接调用
//   `ShellTabs.BuiltinKeys / CurrentShell() / IsVisible() / CanHide() / Label() / SetTabVisible() /
//   SetTabs() / SetTitlebar()`。不移植它,"页签与标题栏"这一段就无法忠实落地 ——
//   这是**因果依赖**,不是"顺手补洞"。

/// <summary>
/// 顶栏页签与标题栏的读写口(布局 v2 的 shell 段)。
/// 页签集合/顺序存 <c>ui_layout_v2.json</c> 的 <c>shell.tabs</c>;标题栏四个开关存 <c>shell.titlebar</c>。
/// 契约(与 Windows 逐字一致):
/// <list type="bullet">
/// <item><c>settings</c> 页签**永远可见**且永远在末尾 —— 否则用户没有入口改回来。</item>
/// <item>读出来的顺序先过滤白名单(<see cref="LayoutSchema.IsKnownTab"/>)并去重,再保留用户顺序;空则回落内置五键。</item>
/// <item>所有写操作都是"读整份文档 → 改 shell → 存回",不做增量补丁。</item>
/// </list>
/// </summary>
public static class ShellTabs
{
	/// <summary>启动页页签 key。</summary>
	public const string LaunchKey = "launch";

	/// <summary>实例库页页签 key。</summary>
	public const string LibraryKey = "library";

	/// <summary>下载页页签 key。</summary>
	public const string DownloadKey = "download";

	/// <summary>插件页页签 key。</summary>
	public const string PluginsKey = "plugins";

	/// <summary>设置页页签 key(不可隐藏)。</summary>
	public const string SettingsKey = "settings";

	/// <summary>内置页签集合(顺序 = 出厂顺序)。直接取契约层的默认值副本。</summary>
	public static readonly string[] BuiltinKeys = (string[])LayoutSchema.DefaultTabs.Clone();

	// ───────────────────────── R32-D26:Mac 页集是 shell.tabs 的超集 ─────────────────────────
	//
	// 裁定(R32-PLAN.md 第 128 行):Windows 的 `shell.tabs` 只有 5 个语义键,而 mac 侧栏页集是 7 键
	// (`NavModel.DefaultNavOrder`)⇒ **以 Mac 页集为超集**,5 个语义键全映射,多出 2 页保持原生行为
	// 并登记偏离,**不许为对齐键数删 Mac 页面**。
	//
	// 映射(mac 侧栏没有 library 行、library 页靠 Navigate 进入;`more` 是分区不是页):
	//   launch → launch / library → instance / download → download / plugins → more / settings → settings
	// 多出的 2 页 = ai / tasks(布局 v2 不接管,继续由侧栏原生导航控制)。
	//
	// 注意:**持久化的 `shell.tabs` 仍旧只写 5 个 Windows 语义键**(跨树可读,Windows 认得出),
	// 这里只把"给用户看的页集"扩到 Mac 的 7 键。

	/// <summary>Mac 侧栏页集(7 键;逐字等于 <see cref="NavModel.DefaultNavOrder"/> —— 由单测钉死防漂移)。</summary>
	public static readonly string[] MacPageKeys = new string[7] { "launch", "download", "instance", "ai", "more", "settings", "tasks" };

	/// <summary>Mac 页集里**不由布局 v2 接管**、保持原生侧栏行为的页(多出的 2 页;R32-D26 登记为偏离)。</summary>
	public static readonly string[] MacNativeOnlyKeys = new string[2] { "ai", "tasks" };

	/// <summary>`shell.tabs` 的 5 个语义键 → Mac 页键(5 键全映射)。未知键原样返回。</summary>
	public static string MacKeyOf(string tabKey)
	{
		switch (tabKey)
		{
			case LaunchKey:
				return "launch";
			case LibraryKey:
				return "instance";
			case DownloadKey:
				return "download";
			case PluginsKey:
				return "more";
			case SettingsKey:
				return "settings";
			default:
				return tabKey ?? "";
		}
	}

	/// <summary>Mac 页键 → `shell.tabs` 语义键(反向映射;`ai`/`tasks` 这类原生页返回 null)。</summary>
	public static string? TabKeyOfMac(string macKey)
	{
		switch (macKey)
		{
			case "launch":
				return LaunchKey;
			case "instance":
				return LibraryKey;
			case "download":
				return DownloadKey;
			case "more":
				return PluginsKey;
			case "settings":
				return SettingsKey;
			default:
				return null;
		}
	}

	/// <summary>该 Mac 页键是否保持原生行为(即"多出的 2 页":布局 v2 不接管)。</summary>
	public static bool IsMacNativeOnly(string macKey)
	{
		return Array.IndexOf(MacNativeOnlyKeys, macKey) >= 0;
	}

	/// <summary>Mac 页键的显示名(与 mac 侧栏文案同一套词条,避免出现第二份页签名)。</summary>
	public static string MacPageLabel(string macKey)
	{
		switch (macKey)
		{
			case "launch":
				return I18n.L("启动");
			case "download":
				return I18n.L("游戏");
			case "instance":
				return I18n.L("版本管理");
			case "ai":
				return I18n.L("AI 助手");
			case "more":
				return I18n.L("更多");
			case "settings":
				return I18n.L("设置");
			case "tasks":
				return I18n.L("下载任务");
			default:
				return macKey ?? "";
		}
	}

	/// <summary>
	/// 生效的页签顺序:白名单过滤 + 去重(保留首次出现)+ 保留用户顺序;空 → 内置五键。
	/// 无论怎么配,<c>settings</c> 一定在(缺失则追加到末尾)。
	/// </summary>
	public static List<string> OrderedKeys(ShellLayout? shell)
	{
		List<string> list = new List<string>();
		List<string>? tabs = shell?.Tabs;
		if (tabs != null)
		{
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (string key in tabs)
			{
				if (!string.IsNullOrEmpty(key) && LayoutSchema.IsKnownTab(key) && seen.Add(key))
				{
					list.Add(key);
				}
			}
		}
		if (list.Count == 0)
		{
			list.AddRange(BuiltinKeys);
		}
		if (!list.Contains(SettingsKey))
		{
			list.Add(SettingsKey);
		}
		return list;
	}

	/// <summary>该页签能不能被隐藏(设置页签不能:隐藏了就没有入口改回来)。</summary>
	public static bool CanHide(string key)
	{
		return !string.Equals(key, SettingsKey, StringComparison.Ordinal);
	}

	/// <summary>启动时该打开哪个页面:第一个有 pageId 的页签;都没有 → 启动页。</summary>
	public static string StartPageId(ShellLayout? shell)
	{
		foreach (string key in OrderedKeys(shell))
		{
			string? pageId = PageIdOf(key);
			if (!string.IsNullOrEmpty(pageId))
			{
				return pageId!;
			}
		}
		return LaunchKey;
	}

	/// <summary>该页签当前是否可见(空 key 一律 false)。</summary>
	public static bool IsVisible(ShellLayout? shell, string key)
	{
		if (string.IsNullOrEmpty(key))
		{
			return false;
		}
		return OrderedKeys(shell).Contains(key);
	}

	/// <summary>内置页签里当前被隐藏的那些。</summary>
	public static List<string> HiddenKeys(ShellLayout? shell)
	{
		List<string> visible = OrderedKeys(shell);
		List<string> list = new List<string>();
		foreach (string key in BuiltinKeys)
		{
			if (!visible.Contains(key))
			{
				list.Add(key);
			}
		}
		return list;
	}

	/// <summary>页签显示名(未知 key 原样返回)。</summary>
	public static string Label(string key)
	{
		switch (key)
		{
			case LaunchKey:
				return I18n.L("启动");
			case LibraryKey:
				return I18n.L("实例库");
			case DownloadKey:
				return I18n.L("下载");
			case PluginsKey:
				return I18n.L("插件");
			case SettingsKey:
				return I18n.L("设置");
			default:
				return key ?? "";
		}
	}

	/// <summary>页签 key → 页面 id(只有白名单内的 key 有页面)。</summary>
	public static string? PageIdOf(string key)
	{
		return LayoutSchema.IsKnownTab(key) ? key : null;
	}

	/// <summary>页面 id → 应该高亮哪个页签(<c>instance</c>/<c>more</c> 之类子页会落回某个顶层页签;都不匹配 → null)。</summary>
	public static string? HighlightKeyOf(string pageId)
	{
		switch (pageId)
		{
			case LaunchKey:
				return LaunchKey;
			case LibraryKey:
				return LibraryKey;
			case "download":
				return DownloadKey;
			case "tasks":
				return DownloadKey;
			case "plugins":
				return PluginsKey;
			case "settings":
				return SettingsKey;
			case "account":
				return SettingsKey;
			default:
				return null;
		}
	}

	/// <summary>标题栏「桥接」按钮是否显示(未配置 → 显示)。</summary>
	public static bool ShowBridge(ShellLayout? shell)
	{
		return shell?.Titlebar?.Bridge ?? true;
	}

	/// <summary>标题栏「AI」按钮是否显示(未配置 → 显示)。</summary>
	public static bool ShowAi(ShellLayout? shell)
	{
		return shell?.Titlebar?.Ai ?? true;
	}

	/// <summary>标题栏「主题」按钮是否显示(未配置 → 显示)。</summary>
	public static bool ShowTheme(ShellLayout? shell)
	{
		return shell?.Titlebar?.Theme ?? true;
	}

	/// <summary>标题栏「账号」按钮是否显示(未配置 → 显示)。</summary>
	public static bool ShowAccount(ShellLayout? shell)
	{
		return shell?.Titlebar?.Account ?? true;
	}

	/// <summary>四个标题栏按钮是否至少有一个显示(全关时标题栏可能要整体收掉)。</summary>
	public static bool AnyTitlebarVisible(ShellLayout? shell)
	{
		return ShowBridge(shell) || ShowAi(shell) || ShowTheme(shell) || ShowAccount(shell);
	}

	/// <summary>当前文档的 shell 段(读失败 → 全默认)。</summary>
	public static ShellLayout CurrentShell()
	{
		try
		{
			return LayoutStoreV2.Load().Layout.Shell;
		}
		catch
		{
			return new ShellLayout();
		}
	}

	/// <summary>整体替换页签集合(过滤白名单 + 去重;空 → 回落内置)。返回落盘后的 shell 段。</summary>
	public static ShellLayout SetTabs(IEnumerable<string>? keys)
	{
		LayoutV2 doc = LayoutStoreV2.Load();
		List<string> list = new List<string>();
		if (keys != null)
		{
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (string key in keys)
			{
				if (!string.IsNullOrEmpty(key) && LayoutSchema.IsKnownTab(key) && seen.Add(key))
				{
					list.Add(key);
				}
			}
		}
		doc.Layout.Shell.Tabs = list;
		LayoutStoreV2.Save(doc);
		return CurrentShell();
	}

	/// <summary>显示/隐藏一个页签(隐藏只对 <see cref="CanHide"/> 的键生效)。</summary>
	public static ShellLayout SetTabVisible(string key, bool visible)
	{
		LayoutV2 doc = LayoutStoreV2.Load();
		List<string> list = OrderedKeys(doc.Layout.Shell);
		if (visible)
		{
			if (!list.Contains(key))
			{
				list.Add(key);
			}
		}
		else if (CanHide(key))
		{
			list.Remove(key);
		}
		doc.Layout.Shell.Tabs = list;
		LayoutStoreV2.Save(doc);
		return CurrentShell();
	}

	/// <summary>改标题栏四个开关(传 null 的字段不动)。</summary>
	public static ShellLayout SetTitlebar(bool? bridge = null, bool? ai = null, bool? theme = null, bool? account = null)
	{
		LayoutV2 doc = LayoutStoreV2.Load();
		TitlebarLayout bar = doc.Layout.Shell.Titlebar ?? new TitlebarLayout();
		if (bridge.HasValue)
		{
			bar.Bridge = bridge.Value;
		}
		if (ai.HasValue)
		{
			bar.Ai = ai.Value;
		}
		if (theme.HasValue)
		{
			bar.Theme = theme.Value;
		}
		if (account.HasValue)
		{
			bar.Account = account.Value;
		}
		doc.Layout.Shell.Titlebar = bar;
		LayoutStoreV2.Save(doc);
		return CurrentShell();
	}
}