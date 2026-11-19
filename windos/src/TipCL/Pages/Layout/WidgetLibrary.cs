using System;
using System.Collections.Generic;
using TipCL.Services;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Pages.Layout;

// L6 自建区块的**纯模型生命周期层**(零 WPF):
//   新建 / 改标题 / 改参数 / 删除定义(连带清理全部页面里的 custom:<id> 引用)/ 统计引用数。
// 危险类型(web / button)没有创建入口,也不允许在这里改参数:它们仍然走 WidgetCommon 的显式开启门禁。
// 落盘仍只走 LayoutStoreV2.Save(v2 唯一真源),本类只改传入的内存文档。
public static class WidgetLibrary
{
	/// <summary>标题长度上限(超过就拒绝,避免异常数据撑爆界面)。</summary>
	public const int TitleMaxLength = 64;

	/// <summary>便签正文长度上限(与渲染层 BasicWidgets.Note 的截断一致)。</summary>
	public const int TextMaxLength = 4000;

	/// <summary>快捷启动实例名 / 版本号长度上限(与 ShortcutWidget.Build 的校验一致)。</summary>
	public const int ShortcutMaxLength = 128;

	/// <summary>时钟格式长度上限(与 BasicWidgets.Clock 的回落条件一致)。</summary>
	public const int ClockFormatMaxLength = 32;

	/// <summary>是否是可创建的安全类型(web / button 永远不在其中)。</summary>
	public static bool IsSafeType(string? type)
	{
		if (string.IsNullOrEmpty(type))
		{
			return false;
		}
		foreach (string safe in WidgetRegistry.SafeTypes)
		{
			if (string.Equals(safe, type, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>该类型的主参数字段名(divider / playtime 无参数返回空串)。</summary>
	public static string ParameterKey(string? type)
	{
		if (string.Equals(type, LayoutSchema.WidgetNote, StringComparison.Ordinal))
		{
			return "text";
		}
		if (string.Equals(type, LayoutSchema.WidgetShortcut, StringComparison.Ordinal))
		{
			return "instance";
		}
		if (string.Equals(type, LayoutSchema.WidgetClock, StringComparison.Ordinal))
		{
			return "format";
		}
		return "";
	}

	/// <summary>读取主参数(用于编辑表单回填;没有返回空串)。</summary>
	public static string ParameterOf(WidgetDef? widget)
	{
		if (widget == null)
		{
			return "";
		}
		string key = ParameterKey(widget.Type);
		return ((key.Length > 0) ? WidgetCommon.Str(widget.Settings, key, "") : "");
	}

	/// <summary>读取快捷启动的可选版本号(其它类型返回空串)。</summary>
	public static string VersionOf(WidgetDef? widget)
	{
		return ((widget != null && string.Equals(widget.Type, LayoutSchema.WidgetShortcut, StringComparison.Ordinal)) ? WidgetCommon.Str(widget.Settings, "version", "") : "");
	}

	/// <summary>
	/// 新建一个定义(尚未加入文档):类型必须是 5 种安全类型之一,标题必填,参数按类型做最小校验。
	/// 返回 null 时 <paramref name="error"/> 是可直接显示的中文原因。
	/// </summary>
	public static WidgetDef? Create(string type, string title, string parameter, string version, out string error)
	{
		error = "";
		if (!IsSafeType(type))
		{
			error = I18n.L("自建区块类型不受支持:{0}", type ?? "");
			return null;
		}
		string cleanTitle = Trim(title);
		if (cleanTitle.Length == 0)
		{
			error = I18n.L("标题不能为空");
			return null;
		}
		if (cleanTitle.Length > TitleMaxLength)
		{
			error = I18n.L("标题不能超过 {0} 个字符", TitleMaxLength);
			return null;
		}
		WidgetDef widget = new WidgetDef
		{
			Id = LayoutSchema.NewWidgetId(),
			Type = type,
			Title = cleanTitle
		};
		string reason;
		if (!TryFillSettings(widget, parameter, version, out reason))
		{
			error = reason;
			return null;
		}
		return widget;
	}

	/// <summary>
	/// 新建并分配一个文档内唯一的 id;返回 null 与中文原因。追加到 <c>doc.Layout.Widgets</c> 由调用方做
	/// (本方法不改页面,新建的块默认不加到任何页面)。
	/// </summary>
	public static WidgetDef? CreateFor(LayoutV2? doc, string type, string title, string parameter, string version, out string error)
	{
		WidgetDef? widget = Create(type, title, parameter, version, out error);
		if (widget != null)
		{
			widget.Id = UniqueId(doc);
		}
		return widget;
	}

	/// <summary>改标题(危险类型也允许改标题;类型与参数不动)。</summary>
	public static bool TryRename(WidgetDef? widget, string title, out string error)
	{
		error = "";
		if (widget == null)
		{
			error = I18n.L("自建区块不存在");
			return false;
		}
		string clean = Trim(title);
		if (clean.Length == 0)
		{
			error = I18n.L("标题不能为空");
			return false;
		}
		if (clean.Length > TitleMaxLength)
		{
			error = I18n.L("标题不能超过 {0} 个字符", TitleMaxLength);
			return false;
		}
		widget.Title = clean;
		return true;
	}

	/// <summary>
	/// 改参数:只动 5 种安全类型的已知键,其它自定义键原样保留。
	/// 危险类型(web / button)拒绝 —— 它们的 url / enabled / action 必须在布局 JSON 里显式配置。
	/// </summary>
	public static bool TryUpdateSettings(WidgetDef? widget, string parameter, string version, out string error)
	{
		error = "";
		if (widget == null)
		{
			error = I18n.L("自建区块不存在");
			return false;
		}
		if (!IsSafeType(widget.Type))
		{
			error = I18n.L("危险类型的参数请在布局 JSON 里编辑");
			return false;
		}
		return TryFillSettings(widget, parameter, version, out error);
	}

	/// <summary>统计某个定义被多少个页面引用(custom:&lt;id&gt; 出现在 pages[].blocks 里;含窄窗变体)。</summary>
	public static int CountReferences(LayoutV2? doc, string widgetId)
	{
		if (doc == null || string.IsNullOrEmpty(widgetId))
		{
			return 0;
		}
		string blockId = LayoutEngine.CustomPrefix + widgetId;
		int count = 0;
		foreach (LayoutBody body in Bodies(doc))
		{
			if (body.Pages == null)
			{
				continue;
			}
			foreach (PageLayoutV2 page in body.Pages.Values)
			{
				if (page == null || page.Blocks == null)
				{
					continue;
				}
				foreach (BlockLayout block in page.Blocks)
				{
					if (block != null && string.Equals(block.Id, blockId, StringComparison.Ordinal))
					{
						count++;
						break;
					}
				}
			}
		}
		return count;
	}

	/// <summary>
	/// 删除定义:从所有 LayoutBody 的 Widgets 移除,并从所有页面移除 <c>custom:&lt;id&gt;</c> 块引用。
	/// 返回被清掉的页面引用数;文档里原本没有这个定义时返回 -1(调用方据此提示)。
	/// </summary>
	public static int Delete(LayoutV2? doc, string widgetId)
	{
		if (doc == null || string.IsNullOrEmpty(widgetId))
		{
			return -1;
		}
		string blockId = LayoutEngine.CustomPrefix + widgetId;
		if (!HasDefinition(doc, widgetId))
		{
			return -1;
		}
		int removedRefs = 0;
		foreach (LayoutBody body in Bodies(doc))
		{
			if (body.Widgets != null)
			{
				for (int i = body.Widgets.Count - 1; i >= 0; i--)
				{
					WidgetDef? widget = body.Widgets[i];
					if (widget != null && string.Equals(widget.Id, widgetId, StringComparison.Ordinal))
					{
						body.Widgets.RemoveAt(i);
					}
				}
			}
			if (body.Pages == null)
			{
				continue;
			}
			foreach (PageLayoutV2 page in body.Pages.Values)
			{
				if (page?.Blocks == null)
				{
					continue;
				}
				for (int j = page.Blocks.Count - 1; j >= 0; j--)
				{
					BlockLayout? block = page.Blocks[j];
					if (block != null && string.Equals(block.Id, blockId, StringComparison.Ordinal))
					{
						page.Blocks.RemoveAt(j);
						removedRefs++;
					}
				}
			}
		}
		return removedRefs;
	}

	private static bool HasDefinition(LayoutV2 doc, string widgetId)
	{
		foreach (LayoutBody body in Bodies(doc))
		{
			if (body.Widgets == null)
			{
				continue;
			}
			foreach (WidgetDef widget in body.Widgets)
			{
				if (widget != null && string.Equals(widget.Id, widgetId, StringComparison.Ordinal))
				{
					return true;
				}
			}
		}
		return false;
	}

	// 按类型把参数写进 Settings;note / shortcut 的必填项为空时拒绝
	private static bool TryFillSettings(WidgetDef widget, string parameter, string version, out string error)
	{
		error = "";
		string value = (parameter ?? "").Trim();
		if (string.Equals(widget.Type, LayoutSchema.WidgetNote, StringComparison.Ordinal))
		{
			if (value.Length == 0)
			{
				error = I18n.L("便签内容不能为空");
				return false;
			}
			if (value.Length > TextMaxLength)
			{
				error = I18n.L("便签内容不能超过 {0} 个字符", TextMaxLength);
				return false;
			}
			widget.Settings["text"] = value;
			return true;
		}
		if (string.Equals(widget.Type, LayoutSchema.WidgetShortcut, StringComparison.Ordinal))
		{
			if (value.Length == 0)
			{
				error = I18n.L("实例名不能为空");
				return false;
			}
			if (value.Length > ShortcutMaxLength)
			{
				error = I18n.L("实例名 / 版本号都不应超过 128 个字符");
				return false;
			}
			string ver = (version ?? "").Trim();
			if (ver.Length > ShortcutMaxLength)
			{
				error = I18n.L("实例名 / 版本号都不应超过 128 个字符");
				return false;
			}
			widget.Settings["instance"] = value;
			if (ver.Length > 0)
			{
				widget.Settings["version"] = ver;
			}
			else
			{
				widget.Settings.Remove("version");
			}
			return true;
		}
		if (string.Equals(widget.Type, LayoutSchema.WidgetClock, StringComparison.Ordinal))
		{
			string format = ((value.Length == 0) ? "HH:mm" : value);
			if (format.Length > ClockFormatMaxLength)
			{
				error = I18n.L("时间格式不能超过 {0} 个字符", ClockFormatMaxLength);
				return false;
			}
			widget.Settings["format"] = format;
			return true;
		}
		// divider / playtime:无参数,不动 Settings
		return true;
	}

	private static string Trim(string? text)
	{
		return ((text == null) ? "" : text.Trim());
	}

	// 根 body + 各变体(递归;变体深度有上限,防手工构造的环)
	private static IEnumerable<LayoutBody> Bodies(LayoutV2 doc)
	{
		if (doc.Layout == null)
		{
			yield break;
		}
		foreach (LayoutBody body in BodyAndVariants(doc.Layout, 0))
		{
			yield return body;
		}
	}

	private static IEnumerable<LayoutBody> BodyAndVariants(LayoutBody body, int depth)
	{
		yield return body;
		if (depth >= 8 || body.Variants == null)
		{
			yield break;
		}
		foreach (LayoutBody variant in body.Variants.Values)
		{
			if (variant == null)
			{
				continue;
			}
			foreach (LayoutBody nested in BodyAndVariants(variant, depth + 1))
			{
				yield return nested;
			}
		}
	}

	// 文档内唯一 id:时间戳 id 撞车时补 -2 / -3...
	private static string UniqueId(LayoutV2? doc)
	{
		string id = LayoutSchema.NewWidgetId();
		if (doc?.Layout?.Widgets == null || doc.Layout.Widgets.Count == 0)
		{
			return id;
		}
		HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
		foreach (WidgetDef widget in doc.Layout.Widgets)
		{
			if (widget != null && !string.IsNullOrEmpty(widget.Id))
			{
				used.Add(widget.Id);
			}
		}
		if (!used.Contains(id))
		{
			return id;
		}
		int suffix = 2;
		while (used.Contains(id + "-" + suffix))
		{
			suffix++;
		}
		return id + "-" + suffix;
	}
}