// R35 lin-l12(GOAL-layout-v3 §5.1 / §5.2 / §6.4,对照 Windows r34/l12 的 LayoutAddDrawer.cs):
// 「添加」抽屉与区块显示名的**纯规则**(零 Avalonia 依赖,单测直接断言):
//   * LayoutAddDrawer   —— 抽屉宽度 / 网页·按钮全局开关钩子(Q6)/ 组件类型清单 / 能否重复 / 一句说明;
//   * LayoutBlockTitles —— 区块显示标题:自建块取 widgets[] 的 title → 类型名;插件块取插件给的标题;
//                          **任何分支都不返回块 id**(§5.1「永远不显示 id」);浮动标签的灰字后缀「· 组件」。
// 抽屉的界面部分仍在 LayoutEditor.V3.cs(C 的实现,本包只补齐动画 / 空状态虚线框 / 新块光晕)。
using System;
using System.Collections.Generic;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;

namespace TipCL.App.Pages.Layout;

/// <summary>「添加」抽屉的列表规则(§5.2)。</summary>
public static class LayoutAddDrawer
{
	/// <summary>
	/// 网页 / 按钮组件的全局开关(Q6)。Linux 基线已有设置代理 D 的 <see cref="LayoutProfiles.DangerousWidgetsEnabled"/>
	/// (R35-BRIEF §3 第 2 条:沿用,L4 不改名),这里默认直接读它;测试可以替换钩子。
	/// </summary>
	public static Func<bool> DangerWidgetsAllowed = () => LayoutProfiles.DangerousWidgetsEnabled;

	/// <summary>抽屉宽度(效果图 .drawer{width:316px})。</summary>
	public const double DrawerWidth = 316.0;

	/// <summary>滑入 / 滑出时长(效果图 transition:transform .26s ease-out)。</summary>
	public const int SlideMs = 260;

	/// <summary>全局开关当前是否打开(钩子为空或抛异常一律按关处理)。</summary>
	public static bool IsDangerAllowed()
	{
		try
		{
			Func<bool>? hook = DangerWidgetsAllowed;
			return hook != null && hook();
		}
		catch
		{
			return false;
		}
	}

	/// <summary>网页 / 按钮两种「危险」类型。</summary>
	public static bool IsDangerousType(string? type)
	{
		return string.Equals(type, LayoutSchema.WidgetWeb, StringComparison.Ordinal) || string.Equals(type, LayoutSchema.WidgetButton, StringComparison.Ordinal);
	}

	/// <summary>
	/// 同一页可以放多个的类型:效果图 WT 表里 multi:true 的便签 / 分隔线 / 网页 / 按钮;
	/// 其余(时钟 / 快捷启动 / 游戏时长)一页一个,已加过就显示「已添加」并置灰。
	/// </summary>
	public static bool AllowsMultiple(string? type)
	{
		return string.Equals(type, LayoutSchema.WidgetNote, StringComparison.Ordinal) || string.Equals(type, LayoutSchema.WidgetDivider, StringComparison.Ordinal) || IsDangerousType(type);
	}

	/// <summary>抽屉「组件」一节列出的类型:五种安全类型 + 开关打开时的网页 / 按钮(排最后)。</summary>
	public static List<string> Types(bool dangerAllowed)
	{
		List<string> types = new List<string>(WidgetRegistry.SafeTypes);
		if (dangerAllowed)
		{
			types.Add(LayoutSchema.WidgetWeb);
			types.Add(LayoutSchema.WidgetButton);
		}
		return types;
	}

	/// <summary>按当前开关列出的类型。</summary>
	public static List<string> Types()
	{
		return Types(IsDangerAllowed());
	}

	/// <summary>
	/// 某类型在本页是不是「已添加」(不可重复且本页已有):<paramref name="typesOnPage"/> 是本页现有自建块的类型。
	/// </summary>
	public static bool IsAdded(string? type, IEnumerable<string?> typesOnPage)
	{
		if (string.IsNullOrEmpty(type) || AllowsMultiple(type) || typesOnPage == null)
		{
			return false;
		}
		foreach (string? onPage in typesOnPage)
		{
			if (string.Equals(onPage, type, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>一句说明(效果图文案)。</summary>
	public static string DescriptionOf(string? type)
	{
		switch (type)
		{
		case LayoutSchema.WidgetNote:
			return I18nCore.Tr("记一句话，可以加多张");
		case LayoutSchema.WidgetShortcut:
			return I18nCore.Tr("一键启动指定实例");
		case LayoutSchema.WidgetClock:
			return I18nCore.Tr("当前时间和日期");
		case LayoutSchema.WidgetPlaytime:
			return I18nCore.Tr("本周每天玩了多久");
		case LayoutSchema.WidgetDivider:
			return I18nCore.Tr("把页面分成两段");
		case LayoutSchema.WidgetWeb:
			return I18nCore.Tr("嵌入一个 https 网页");
		case LayoutSchema.WidgetButton:
			return I18nCore.Tr("点一下打开页面或启动实例");
		default:
			return "";
		}
	}
}

/// <summary>区块显示标题(§5.1):永远不把块 id 显示给用户。</summary>
public static class LayoutBlockTitles
{
	/// <summary>组件标题:widgets[] 里的 title;没有就用类型名(便签 / 时钟…)。</summary>
	public static string WidgetTitle(WidgetDef? widget)
	{
		if (widget != null && !string.IsNullOrWhiteSpace(widget.Title))
		{
			return widget.Title.Trim();
		}
		return WidgetUI.TypeLabel(widget?.Type ?? "");
	}

	/// <summary>
	/// 块的显示标题:页面 spec 给了标题就用它(标题恰好等于 id 的不算);<c>custom:</c> 块取组件标题 → 类型名;
	/// <c>plugin:</c> 块取插件给的标题;都拿不到就用通用名 —— 任何分支都不返回块 id。
	/// </summary>
	public static string Resolve(string? blockId, string? specTitle, Func<string, WidgetDef?>? findWidget, Func<string, string, string?>? pluginTitle)
	{
		string id = blockId ?? "";
		string? widgetId = LayoutEngine.WidgetIdOf(id);
		bool plugin = LayoutEngine.TryParsePluginId(id, out string pluginName, out string inner);
		if (!string.IsNullOrWhiteSpace(specTitle) && !LooksLikeId(specTitle!.Trim(), id, widgetId, plugin ? pluginName : null, plugin ? inner : null))
		{
			return specTitle!.Trim();
		}
		if (widgetId != null)
		{
			WidgetDef? widget = null;
			try
			{
				widget = findWidget?.Invoke(id);
			}
			catch
			{
				widget = null;
			}
			if (widget != null)
			{
				string title = WidgetTitle(widget);
				if (!LooksLikeId(title, id, widgetId, null, null))
				{
					return title;
				}
				return WidgetUI.TypeLabel(widget.Type ?? "");
			}
			return I18nCore.Tr("组件");
		}
		if (plugin)
		{
			string? title = null;
			try
			{
				title = pluginTitle?.Invoke(pluginName, inner);
			}
			catch
			{
				title = null;
			}
			if (!string.IsNullOrWhiteSpace(title))
			{
				string clean = title!.Trim();
				if (!LooksLikeId(clean, id, null, pluginName, inner))
				{
					return clean;
				}
			}
			return I18nCore.Tr("插件");
		}
		return I18nCore.Tr("内容");
	}

	/// <summary>浮动标签上标题后面的灰字后缀:自建组件 =「· 组件」,其它为空。</summary>
	public static string LabelSuffix(string? blockId)
	{
		return (LayoutEngine.WidgetIdOf(blockId) != null) ? I18nCore.Tr("· 组件") : "";
	}

	/// <summary>显示文本里出现了块 id(或它的组成部分)= 不可用作标题。</summary>
	public static bool ContainsId(string? text, string? blockId)
	{
		if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(blockId))
		{
			return false;
		}
		if (text!.IndexOf(blockId!, StringComparison.Ordinal) >= 0)
		{
			return true;
		}
		string? widgetId = LayoutEngine.WidgetIdOf(blockId);
		return widgetId != null && widgetId.Length > 0 && text.IndexOf(widgetId, StringComparison.Ordinal) >= 0;
	}

	private static bool LooksLikeId(string text, string id, string? widgetId, string? plugin, string? inner)
	{
		if (string.Equals(text, id, StringComparison.Ordinal))
		{
			return true;
		}
		if (widgetId != null && (string.Equals(text, widgetId, StringComparison.Ordinal) || text.IndexOf(id, StringComparison.Ordinal) >= 0))
		{
			return true;
		}
		if (plugin != null && (string.Equals(text, plugin, StringComparison.Ordinal) || string.Equals(text, inner, StringComparison.Ordinal) || text.StartsWith("plugin:", StringComparison.Ordinal)))
		{
			return true;
		}
		return text.StartsWith("custom:", StringComparison.Ordinal);
	}
}
