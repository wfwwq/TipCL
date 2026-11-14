using System;
using System.Collections.Generic;
using TipCL.Services;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Pages.Layout;

// 顶栏页签的**纯逻辑层**,照 LayoutSchema 的约定:禁 WPF,便于 net8 测试宿主直接加载。
//   数据源 = v2 文档的 shell.tabs,即设计 §3.2 的 tabs 字段:顺序 + 显隐,取子集,空则回落默认;
//   本类型只算「什么该显示、显示成什么、什么时候高亮」,真正的可视元素由宿主编排 ——
//   见 MainWindow.RebuildShellTabs:清空 TopTabPanel → 按计划放内置页签 → 把插件动态页签追加在末尾。
// 三条不变量,与改动前的写死页签逐项对齐(依据设计 §4.D 与 §0.1 的 Q1 决策):
//   1. 默认顺序/集合恒为 启动/实例库/下载/插件/设置,即 LayoutSchema.DefaultTabs;
//   2. 空列表一定回落默认 —— 任何情况下都不会出现「一个页签都没有」的壳,用户永远有入口;
//   3. 高亮映射不许变:launch→launch、library→library、download|tasks→download、plugins→plugins、
//      settings|account→settings;其余页面(ai / instance / multiplayer / 插件页)不高亮任何内置页签。
public static class ShellTabs
{
	/// <summary>页签 key:启动。</summary>
	public const string LaunchKey = "launch";

	/// <summary>页签 key:实例库。</summary>
	public const string LibraryKey = "library";

	/// <summary>页签 key:下载。</summary>
	public const string DownloadKey = "download";

	/// <summary>页签 key:插件。</summary>
	public const string PluginsKey = "plugins";

	/// <summary>页签 key:设置。</summary>
	public const string SettingsKey = "settings";

	/// <summary>
	/// 内置页签的 key,顺序即默认顺序。刻意从 <see cref="LayoutSchema.DefaultTabs"/> 复制,
	/// 让「契约默认值」只有一个源头:契约改了这里自动跟上。
	/// </summary>
	public static readonly string[] BuiltinKeys = (string[])LayoutSchema.DefaultTabs.Clone();

	// ————— 计划:顺序 + 显隐 —————

	/// <summary>
	/// 按 shell.tabs 算出**真正要显示**的页签 key 序列:只认合法 key 的白名单、去重、保持用户顺序;
	/// shell 为空 / tabs 为空 / 全部非法 → 回落默认五项(见类型注释里的不变量 2)。
	/// </summary>
	public static List<string> OrderedKeys(ShellLayout? shell)
	{
		List<string> list = new List<string>();
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		if (shell != null && shell.Tabs != null)
		{
			foreach (string key in shell.Tabs)
			{
				if (LayoutSchema.IsKnownTab(key) && seen.Add(key))
				{
					list.Add(key);
				}
			}
		}
		if (list.Count == 0)
		{
			foreach (string key in BuiltinKeys)
			{
				list.Add(key);
			}
		}
		return list;
	}

	/// <summary>该页签当前是否显示,即是否出现在 <see cref="OrderedKeys"/> 里。</summary>
	public static bool IsVisible(ShellLayout? shell, string key)
	{
		return !string.IsNullOrEmpty(key) && OrderedKeys(shell).Contains(key);
	}

	/// <summary>被隐藏的内置页签,按默认顺序;插件动态页签不参与这段计算。</summary>
	public static List<string> HiddenKeys(ShellLayout? shell)
	{
		List<string> visible = OrderedKeys(shell);
		List<string> hidden = new List<string>();
		foreach (string key in BuiltinKeys)
		{
			if (!visible.Contains(key))
			{
				hidden.Add(key);
			}
		}
		return hidden;
	}

	// ————— 文案与去向 —————

	/// <summary>
	/// 页签文案。中文原文(即 I18n 的 key)只出现在本方法里,且一律用 <see cref="I18n.L(string)"/> 包裹 ——
	/// XAML 里原来的五个中文字面量已经删除,文案的唯一来源就是这里;这五个 key 的 en / zh_CN 词条必须成对。
	/// 未知 key 原样返回:插件动态页签自带文案,不经过这里。
	/// </summary>
	public static string Label(string key)
	{
		return key switch
		{
			LaunchKey => I18n.L("启动"), 
			LibraryKey => I18n.L("实例库"), 
			DownloadKey => I18n.L("下载"), 
			PluginsKey => I18n.L("插件"), 
			SettingsKey => I18n.L("设置"), 
			_ => key ?? "", 
		};
	}

	/// <summary>点页签要导航到的页面 id:内置页签的 key 与页面 id 同名,与改动前的 Click 目标逐项一致。</summary>
	public static string? PageIdOf(string key)
	{
		return LayoutSchema.IsKnownTab(key) ? key : null;
	}

	/// <summary>
	/// 当前页面 → 需要高亮(IsChecked)的页签 key;null 表示不高亮任何内置页签。
	/// 这是改动前 SyncTabs 里五行的**逐项等价改写**,映射不许改(设计 §4.D 与本包验收第 1 条)。
	/// </summary>
	public static string? HighlightKeyOf(string? pageId)
	{
		return pageId switch
		{
			LaunchKey => LaunchKey, 
			LibraryKey => LibraryKey, 
			"download" => DownloadKey, 
			"tasks" => DownloadKey, 
			PluginsKey => PluginsKey, 
			"settings" => SettingsKey, 
			"account" => SettingsKey, 
			_ => null, 
		};
	}

	// ————— 标题栏元素:只显隐,形态不可改(D-16 是用户定案) —————

	/// <summary>桥接药丸钮是否显示,默认 true。</summary>
	public static bool ShowBridge(ShellLayout? shell)
	{
		return shell?.Titlebar?.Bridge ?? true;
	}

	/// <summary>AI 入口钮是否显示,默认 true;只控制显隐,右侧玻璃药丸形态与 Ctrl+K 一律不动。</summary>
	public static bool ShowAi(ShellLayout? shell)
	{
		return shell?.Titlebar?.Ai ?? true;
	}

	/// <summary>主题切换钮是否显示,默认 true。</summary>
	public static bool ShowTheme(ShellLayout? shell)
	{
		return shell?.Titlebar?.Theme ?? true;
	}

	/// <summary>账号钮是否显示,默认 true。</summary>
	public static bool ShowAccount(ShellLayout? shell)
	{
		return shell?.Titlebar?.Account ?? true;
	}

	/// <summary>标题栏四项里是否至少有一项要显示:全关时宿主把四项与窗口按钮之间的分隔线一起收起。</summary>
	public static bool AnyTitlebarVisible(ShellLayout? shell)
	{
		return ShowBridge(shell) || ShowAi(shell) || ShowTheme(shell) || ShowAccount(shell);
	}

	// ————— 读取与写回:唯一入口 = LayoutStoreV2,本类型绝不自己碰文件 —————

	/// <summary>读当前外壳(shell);读失败一律回默认 —— 界面不能因为读布局而空掉。</summary>
	public static ShellLayout CurrentShell()
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			if (doc != null && doc.Layout != null && doc.Layout.Shell != null)
			{
				return doc.Layout.Shell;
			}
		}
		catch
		{
			// 落到默认
		}
		return new ShellLayout();
	}

	/// <summary>
	/// 写页签顺序/显隐:load → 改 shell → <see cref="LayoutStoreV2.Save"/>,白名单、去重与
	/// 「空列表回落默认」都由 LayoutSchema.Sanitize 负责。返回落盘后的外壳。
	/// </summary>
	public static ShellLayout SetTabs(IEnumerable<string>? order)
	{
		LayoutV2 doc = LayoutStoreV2.Load();
		List<string> keys = new List<string>();
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		if (order != null)
		{
			foreach (string key in order)
			{
				if (LayoutSchema.IsKnownTab(key) && seen.Add(key))
				{
					keys.Add(key);
				}
			}
		}
		doc.Layout.Shell.Tabs = keys;
		LayoutStoreV2.Save(doc);
		return CurrentShell();
	}

	/// <summary>显示 / 隐藏单个内置页签,其余顺序不变;返回落盘后的外壳。</summary>
	public static ShellLayout SetTabVisible(string key, bool visible)
	{
		LayoutV2 doc = LayoutStoreV2.Load();
		List<string> keys = OrderedKeys(doc.Layout.Shell);
		if (visible)
		{
			if (!keys.Contains(key))
			{
				keys.Add(key);
			}
		}
		else
		{
			keys.Remove(key);
		}
		doc.Layout.Shell.Tabs = keys;
		LayoutStoreV2.Save(doc);
		return CurrentShell();
	}

	/// <summary>只改传入的标题栏开关(null 表示该项不动);返回落盘后的外壳。</summary>
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
