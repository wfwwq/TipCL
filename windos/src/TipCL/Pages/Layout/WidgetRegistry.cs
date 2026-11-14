using System;
using System.Collections.Generic;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Pages.Layout;

// 自建区块 / 插件区块的内容工厂(§4.F 的口子)。
// 只实现 L1 冻结的 ILayoutContentProvider,并通过 LayoutContentRegistry.Register 接入 ——
// **不改** BlockHost / LayoutEditor / LayoutEngine / 任何 L0 文件,插件与自建块从此都有落脚点。
//   块 id = custom:<widgetId>            → 五类首期类型(note / shortcut / clock / divider / playtime)+ 危险类型 web / button
//   块 id = plugin:<plugin>:<blockId>    → 插件贡献 kind = block
// 查不到定义 / 插件卸载 → 显示占位,**绝不删用户数据**(删除入口在设置页,归 L5)。
public sealed class WidgetRegistry : ILayoutContentProvider
{
	/// <summary>插件界面贡献的 kind(与 PluginManifest / PluginHostApi / PluginManager 三处白名单一致)。</summary>
	public const string PluginKindBlock = "block";

	/// <summary>首期可以直接添加的自建块类型(Q5:web / button 不在其中,要显式开启)。</summary>
	public static readonly string[] SafeTypes = new string[5]
	{
		LayoutSchema.WidgetNote,
		LayoutSchema.WidgetShortcut,
		LayoutSchema.WidgetClock,
		LayoutSchema.WidgetDivider,
		LayoutSchema.WidgetPlaytime
	};

	private static readonly object _gate = new object();

	private static WidgetRegistry? _instance;

	/// <summary>是否已注册(诊断 / 测试用)。</summary>
	public static bool Registered
	{
		get
		{
			lock (_gate)
			{
				return _instance != null;
			}
		}
	}

	/// <summary>
	/// 一次性注册内容工厂:由 <c>App.OnStartup</c> 显式调用。
	/// net48 上刻意**不用** ModuleInitializer(时机不可控),显式调用最稳;重复调用无副作用。
	/// </summary>
	public static bool EnsureRegistered()
	{
		lock (_gate)
		{
			if (_instance != null)
			{
				return false;
			}
			_instance = new WidgetRegistry();
			LayoutContentRegistry.Register(_instance);
			return true;
		}
	}

	/// <summary>注销(测试用,生产不要调)。</summary>
	public static void Unregister()
	{
		lock (_gate)
		{
			if (_instance != null)
			{
				LayoutContentRegistry.Unregister(_instance);
				_instance = null;
			}
		}
	}

	/// <summary>自建块与插件块都归本工厂;内置块(页面自己构建内容)不认领。</summary>
	public bool CanBuild(string blockId)
	{
		return LayoutEngine.Classify(blockId) != LayoutContentKind.Builtin;
	}

	/// <summary>造内容;返回 null 表示"造不出来",由宿主显示通用「区块不可用」占位。</summary>
	public object? Build(string blockId, string pageId)
	{
		if (LayoutEngine.Classify(blockId) == LayoutContentKind.Plugin)
		{
			return PluginBlocks.Build(blockId, pageId);
		}
		WidgetDef? widget = FindWidget(blockId);
		if (widget == null)
		{
			return WidgetUI.MissingWidget(blockId);
		}
		return WidgetUI.Build(widget, pageId);
	}

	/// <summary>按 <c>custom:&lt;widgetId&gt;</c> 找自建块定义(找不到返回 null)。</summary>
	public static WidgetDef? FindWidget(string blockId)
	{
		string? id = LayoutEngine.WidgetIdOf(blockId);
		if (id == null)
		{
			return null;
		}
		return FindWidgetById(id);
	}

	/// <summary>按 widgetId 找定义(读的是 LayoutStoreV2 的副本,改不到缓存)。</summary>
	public static WidgetDef? FindWidgetById(string widgetId)
	{
		if (string.IsNullOrEmpty(widgetId))
		{
			return null;
		}
		LayoutV2 doc = LayoutStoreV2.Load();
		if (doc.Layout == null || doc.Layout.Widgets == null)
		{
			return null;
		}
		foreach (WidgetDef widget in doc.Layout.Widgets)
		{
			if (widget != null && string.Equals(widget.Id, widgetId, StringComparison.Ordinal))
			{
				return widget;
			}
		}
		return null;
	}

	/// <summary>布局文档里定义的全部自建块(按文档顺序)。</summary>
	public static IReadOnlyList<WidgetDef> AllWidgets()
	{
		LayoutV2 doc = LayoutStoreV2.Load();
		List<WidgetDef> list = new List<WidgetDef>();
		if (doc.Layout != null && doc.Layout.Widgets != null)
		{
			foreach (WidgetDef widget in doc.Layout.Widgets)
			{
				if (widget != null)
				{
					list.Add(widget);
				}
			}
		}
		return list;
	}
}
