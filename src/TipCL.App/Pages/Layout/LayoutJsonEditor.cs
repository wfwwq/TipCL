// 来源: Windows 版参考仓库 src/TipCL/Pages/SettingsLayoutPage.cs 里的 LayoutJsonIssue +
// LayoutJsonEditor(wt-r31-merge @ 0a304a7,冻结只读,原文 1291-1670 行)。逐行搬运,差异三处:
//   `I18n.L(x, a)` → `Py.Fmt(I18nCore.Tr(x), a)`;
//   `LayoutJson.TryObject/TryArray` 在 Core 里是 internal(TipCL.Core.csproj 只给 TipCL.Tests 开了
//   InternalsVisibleTo,App 看不见),所以在本文件末尾按原实现复刻一份 private 小工具;
//   命名空间归到 TipCL.App.Pages.Layout。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;

namespace TipCL.App.Pages.Layout;

/// <summary>布局 JSON 编辑器的一条问题(具体字段路径 + 中文原因);Error = 阻止落盘的硬错误。</summary>
public sealed class LayoutJsonIssue
{
	/// <summary>是否硬错误(错误 = 不应用;警告 = 已按契约钳位 / 忽略后照常应用)。</summary>
	public bool Error { get; set; }

	/// <summary>字段路径(如 <c>layout.splits["library"]["folders"]</c>);空串表示整份文档的问题。</summary>
	public string Path { get; set; } = "";

	/// <summary>中文原因。</summary>
	public string Message { get; set; } = "";
}

/// <summary>
/// 布局 JSON 编辑器(设计 §4.G)的校验与应用:纯逻辑、零 UI 依赖。
/// 校验按 §3.2 的契约逐字段来:version/kind 门禁不通过 = 硬错误;白名单外的取值 / 越界数值 =
/// 警告(会由 LayoutSchema.Sanitize 钳位或忽略),错误时**不落盘**。
/// </summary>
public static class LayoutJsonEditor
{
	/// <summary>编辑器文本上限(防御:超大粘贴直接把界面拖死)。</summary>
	public const int MaxTextLength = 524288;

	private static readonly string[] KnownTopKeys = new string[7] { "version", "kind", "saved_at", "app", "active_profile", "layout", "profiles" };

	/// <summary>当前文档的 JSON 文本(编辑器的初始内容)。</summary>
	public static string Text(LayoutV2? doc)
	{
		try
		{
			LayoutV2 body = ((doc != null) ? doc : LayoutStoreV2.Load());
			return body.ToJson();
		}
		catch
		{
			return "{}";
		}
	}

	/// <summary>只校验(不落盘)。返回的问题里只要有一条 Error 就不该应用。</summary>
	public static List<LayoutJsonIssue> Inspect(string? json, out LayoutV2? doc)
	{
		doc = null;
		List<LayoutJsonIssue> issues = new List<LayoutJsonIssue>();
		string text = json ?? "";
		if (text.Trim().Length == 0)
		{
			issues.Add(Error("", I18nCore.Tr("内容是空的")));
			return issues;
		}
		if (text.Length > MaxTextLength)
		{
			issues.Add(Error("", Py.Fmt(I18nCore.Tr("内容太长(超过 {0} KB)"), MaxTextLength / 1024)));
			return issues;
		}
		JsonDocument parsed;
		try
		{
			parsed = JsonDocument.Parse(text);
		}
		catch (JsonException ex)
		{
			issues.Add(Error("", Py.Fmt(I18nCore.Tr("不是合法的 JSON:{0}"), ex.Message)));
			return issues;
		}
		catch (Exception ex2)
		{
			issues.Add(Error("", Py.Fmt(I18nCore.Tr("不是合法的 JSON:{0}"), ex2.Message)));
			return issues;
		}
		try
		{
			JsonElement root = parsed.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				issues.Add(Error("", I18nCore.Tr("最外层必须是 JSON 对象")));
				return issues;
			}
			string kind = Text(root, "kind");
			if (kind.Length == 0)
			{
				issues.Add(Error("kind", Py.Fmt(I18nCore.Tr("缺少必填字段 kind(应为 {0})"), LayoutSchema.Kind)));
			}
			else if (kind != LayoutSchema.Kind)
			{
				issues.Add(Error("kind", Py.Fmt(I18nCore.Tr("kind 必须是 {0}(当前 {1})"), LayoutSchema.Kind, kind)));
			}
			if (!root.TryGetProperty("version", out JsonElement version))
			{
				issues.Add(Error("version", Py.Fmt(I18nCore.Tr("缺少必填字段 version(应为 {0})"), LayoutSchema.CurrentVersion)));
			}
			else if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int versionValue) || versionValue != LayoutSchema.CurrentVersion)
			{
				issues.Add(Error("version", Py.Fmt(I18nCore.Tr("version 必须是 {0}(当前 {1})"), LayoutSchema.CurrentVersion, version.ToString())));
			}
			if (issues.Any((LayoutJsonIssue i) => i.Error))
			{
				return issues;
			}
			foreach (JsonProperty prop in root.EnumerateObject())
			{
				if (!KnownTopKeys.Contains(prop.Name))
				{
					issues.Add(Warn(prop.Name, I18nCore.Tr("未知字段:保留但不生效")));
				}
			}
			if (TryObject(root, "layout", out JsonElement layout))
			{
				InspectBody(layout, "layout", issues);
			}
			else
			{
				issues.Add(Warn("layout", I18nCore.Tr("缺少 layout:应用后会得到一份空布局")));
			}
			if (TryObject(root, "profiles", out JsonElement profiles))
			{
				foreach (JsonProperty item in profiles.EnumerateObject())
				{
					if (item.Value.ValueKind == JsonValueKind.Object)
					{
						InspectBody(item.Value, "profiles[\"" + item.Name + "\"]", issues);
					}
				}
			}
			else if (root.TryGetProperty("profiles", out JsonElement rawProfiles) && rawProfiles.ValueKind != JsonValueKind.Object)
			{
				issues.Add(Warn("profiles", I18nCore.Tr("必须是对象:已按空处理")));
			}
			doc = LayoutV2.FromJsonText(text);
			if (doc == null)
			{
				issues.Add(Error("", I18nCore.Tr("布局文件无效")));
			}
		}
		finally
		{
			parsed.Dispose();
		}
		return issues;
	}

	/// <summary>校验 + 落盘:没有硬错误时 LayoutStoreV2.Save;返回问题列表(Error 存在即未落盘)。</summary>
	public static List<LayoutJsonIssue> Apply(string? json, out LayoutV2? doc)
	{
		List<LayoutJsonIssue> issues = Inspect(json, out doc);
		if (issues.Any((LayoutJsonIssue i) => i.Error))
		{
			return issues;
		}
		try
		{
			LayoutStoreV2.Save(((doc != null) ? doc : LayoutStoreV2.Load()));
		}
		catch (Exception ex)
		{
			issues.Add(Error("", ex.Message));
		}
		return issues;
	}

	// ————— LAYOUT 对象逐字段检查 —————

	private static void InspectBody(JsonElement body, string path, List<LayoutJsonIssue> issues)
	{
		if (TryObject(body, "shell", out JsonElement shell))
		{
			InspectShell(shell, path + ".shell", issues);
		}
		if (TryObject(body, "pages", out JsonElement pages))
		{
			foreach (JsonProperty page in pages.EnumerateObject())
			{
				if (page.Value.ValueKind != JsonValueKind.Object)
				{
					issues.Add(Warn(path + ".pages[\"" + page.Name + "\"]", I18nCore.Tr("必须是对象:该页已忽略")));
					continue;
				}
				InspectPage(page.Value, path + ".pages[\"" + page.Name + "\"]", issues);
			}
		}
		if (TryObject(body, "splits", out JsonElement splits))
		{
			foreach (JsonProperty page2 in splits.EnumerateObject())
			{
				if (page2.Value.ValueKind != JsonValueKind.Object)
				{
					issues.Add(Warn(path + ".splits[\"" + page2.Name + "\"]", I18nCore.Tr("必须是对象:该页分栏已忽略")));
					continue;
				}
				foreach (JsonProperty split in page2.Value.EnumerateObject())
				{
					string where = path + ".splits[\"" + page2.Name + "\"][\"" + split.Name + "\"]";
					if (split.Value.ValueKind != JsonValueKind.Number)
					{
						issues.Add(Warn(where, I18nCore.Tr("比例必须是数字:该处已忽略")));
						continue;
					}
					double ratio = split.Value.GetDouble();
					if (ratio < LayoutSchema.RatioMin || ratio > LayoutSchema.RatioMax)
					{
						issues.Add(Warn(where, Py.Fmt(I18nCore.Tr("比例夹到 {0}..{1}(当前 {2})"), LayoutSchema.RatioMin, LayoutSchema.RatioMax, ratio)));
					}
				}
			}
		}
		if (TryArray(body, "widgets", out JsonElement widgets))
		{
			int index = 0;
			foreach (JsonElement widget in widgets.EnumerateArray())
			{
				string where2 = path + ".widgets[" + index + "]";
				index++;
				if (widget.ValueKind != JsonValueKind.Object)
				{
					issues.Add(Warn(where2, I18nCore.Tr("必须是对象:该自建区块已忽略")));
					continue;
				}
				string type = Text(widget, "type");
				if (type.Length > 0 && !LayoutSchema.IsKnownWidgetType(type))
				{
					issues.Add(Warn(where2 + ".type", Py.Fmt(I18nCore.Tr("未知类型 {0}:块会保留为「不可用」"), type)));
				}
			}
		}
	}

	private static void InspectShell(JsonElement shell, string path, List<LayoutJsonIssue> issues)
	{
		CheckChoice(shell, "density", new string[3] { LayoutSchema.DensityCompact, LayoutSchema.DensityStandard, LayoutSchema.DensityLoose }, path, issues);
		CheckChoice(shell, "nav_mode", new string[2] { LayoutSchema.NavModeTabs, LayoutSchema.NavModeSidebar }, path, issues);
		CheckChoice(shell, "nav_side", new string[2] { LayoutSchema.NavSideLeft, LayoutSchema.NavSideRight }, path, issues);
		CheckChoice(shell, "nav_style", new string[2] { LayoutSchema.NavStyleCompact, LayoutSchema.NavStyleGrouped }, path, issues);
		if (shell.TryGetProperty("nav_width", out JsonElement navWidth) && navWidth.ValueKind == JsonValueKind.Number)
		{
			double width = navWidth.GetDouble();
			if (width < LayoutSchema.NavWidthMin || width > LayoutSchema.NavWidthMax)
			{
				issues.Add(Warn(path + ".nav_width", Py.Fmt(I18nCore.Tr("侧栏宽夹到 {0}..{1}(当前 {2})"), LayoutSchema.NavWidthMin, LayoutSchema.NavWidthMax, width)));
			}
		}
		if (shell.TryGetProperty("tabs", out JsonElement tabs))
		{
			if (tabs.ValueKind != JsonValueKind.Array)
			{
				issues.Add(Warn(path + ".tabs", I18nCore.Tr("必须是数组:已回落到默认页签")));
			}
			else
			{
				foreach (JsonElement tab in tabs.EnumerateArray())
				{
					string key = ((tab.ValueKind == JsonValueKind.String) ? (tab.GetString() ?? "") : "");
					if (!LayoutSchema.IsKnownTab(key))
					{
						issues.Add(Warn(path + ".tabs", Py.Fmt(I18nCore.Tr("未知页签 {0}:已丢弃"), key)));
					}
				}
			}
		}
	}

	private static void InspectPage(JsonElement page, string path, List<LayoutJsonIssue> issues)
	{
		if (page.TryGetProperty("version", out JsonElement version))
		{
			if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int num) || num != LayoutSchema.CurrentVersion)
			{
				issues.Add(Warn(path + ".version", Py.Fmt(I18nCore.Tr("单页 version 必须是 {0}:该页已按默认处理"), LayoutSchema.CurrentVersion)));
			}
		}
		if (page.TryGetProperty("mode", out JsonElement mode) && mode.ValueKind == JsonValueKind.String)
		{
			string text = mode.GetString() ?? "";
			if (!LayoutSchema.IsKnownMode(text))
			{
				issues.Add(Warn(path + ".mode", Py.Fmt(I18nCore.Tr("未知模式 {0}:已按 stack 处理"), text)));
			}
		}
		if (page.TryGetProperty("cols", out JsonElement cols) && cols.ValueKind == JsonValueKind.Number && cols.TryGetInt32(out int colValue))
		{
			if (LayoutSchema.SnapCols(colValue) != colValue)
			{
				issues.Add(Warn(path + ".cols", Py.Fmt(I18nCore.Tr("列数只认 2/3/4/6/12:已吸附到 {0}"), LayoutSchema.SnapCols(colValue))));
			}
		}
		CheckRange(page, "gap", LayoutSchema.GapMin, LayoutSchema.GapMax, path, issues);
		CheckRange(page, "row_h", LayoutSchema.RowHMin, LayoutSchema.RowHMax, path, issues);
		if (TryArray(page, "blocks", out JsonElement blocks))
		{
			int index = 0;
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (JsonElement block in blocks.EnumerateArray())
			{
				string where = path + ".blocks[" + index + "]";
				index++;
				if (block.ValueKind != JsonValueKind.Object)
				{
					issues.Add(Warn(where, I18nCore.Tr("必须是对象:该块已忽略")));
					continue;
				}
				string id = Text(block, "id");
				if (id.Length == 0)
				{
					issues.Add(Warn(where + ".id", I18nCore.Tr("缺少块 id:该块已忽略")));
				}
				else if (!seen.Add(id))
				{
					issues.Add(Warn(where + ".id", Py.Fmt(I18nCore.Tr("块 id 重复({0}):重复的那块已忽略"), id)));
				}
				if (block.TryGetProperty("style", out JsonElement style) && style.ValueKind == JsonValueKind.String)
				{
					string styleText = style.GetString() ?? "";
					if (!LayoutSchema.IsKnownStyle(styleText))
					{
						issues.Add(Warn(where + ".style", Py.Fmt(I18nCore.Tr("未知外观 {0}:已按 plain 处理"), styleText)));
					}
				}
				if (block.TryGetProperty("hidden", out JsonElement hidden) && hidden.ValueKind != JsonValueKind.True && hidden.ValueKind != JsonValueKind.False)
				{
					issues.Add(Warn(where + ".hidden", I18nCore.Tr("只认 true / false:已按 false 处理")));
				}
				CheckRange(block, "min_h", LayoutSchema.MinHMin, LayoutSchema.MinHMax, where, issues);
			}
		}
	}

	private static void CheckChoice(JsonElement obj, string name, string[] allowed, string path, List<LayoutJsonIssue> issues)
	{
		if (!obj.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
		{
			return;
		}
		string text = value.GetString() ?? "";
		if (!allowed.Contains(text))
		{
			issues.Add(Warn(path + "." + name, Py.Fmt(I18nCore.Tr("取值不合法({0}):已回落到 {1}"), text, allowed[0])));
		}
	}

	private static void CheckRange(JsonElement obj, string name, double min, double max, string path, List<LayoutJsonIssue> issues)
	{
		if (!obj.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
		{
			return;
		}
		double num = value.GetDouble();
		if (num < min || num > max)
		{
			issues.Add(Warn(path + "." + name, Py.Fmt(I18nCore.Tr("取值超出 {0}..{1}:已夹取(当前 {2})"), min, max, num)));
		}
	}

	private static string Text(JsonElement obj, string name)
	{
		if (obj.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
		{
			return value.GetString() ?? "";
		}
		return "";
	}

	private static LayoutJsonIssue Error(string path, string message)
	{
		return new LayoutJsonIssue
		{
			Error = true,
			Path = path,
			Message = message
		};
	}

	private static LayoutJsonIssue Warn(string path, string message)
	{
		return new LayoutJsonIssue
		{
			Error = false,
			Path = path,
			Message = message
		};
	}

	// ————— Core.LayoutJson 的 TryObject / TryArray 复刻(Core 里是 internal,App 引用不到) —————

	private static bool TryObject(JsonElement e, string name, out JsonElement value)
	{
		if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object)
		{
			return true;
		}
		value = default(JsonElement);
		return false;
	}

	private static bool TryArray(JsonElement e, string name, out JsonElement value)
	{
		if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Array)
		{
			return true;
		}
		value = default(JsonElement);
		return false;
	}
}