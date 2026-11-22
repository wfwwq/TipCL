// 来源: Windows 版参考仓库 src/TipCL/Pages/SettingsLayoutPage.cs 里的 LayoutProfiles
// (wt-r31-merge @ 0a304a7,冻结只读,原文 967-1289 行)。逐行搬运,只做两处替换:
//   `I18n.L(x, a)` → `Py.Fmt(I18nCore.Tr(x), a)`(同 Linux 侧既有写法);
//   命名空间归到 TipCL.App.Pages.Layout(Windows 把它塞在 SettingsPage.cs 同一个文件里)。
using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Layout;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;

namespace TipCL.App.Pages.Layout;

/// <summary>
/// 整机布局方案(设计 §4.C):纯逻辑、零界面依赖,落盘一律走 LayoutStoreV2。
/// 方法返回 "" = 成功,否则是给用户看的中文错误;应用方案 = 原子替换整个 layout 再 Save。
/// </summary>
public static class LayoutProfiles
{
	/// <summary>方案名长度上限(防御:避免超长 key 撑爆 JSON);界面与校验共用同一个数。</summary>
	public const int ProfileNameMax = 40;

	/// <summary>方案名列表(文档顺序)。</summary>
	public static List<string> Names()
	{
		try
		{
			return LayoutStoreV2.Load().Profiles.Keys.ToList();
		}
		catch
		{
			return new List<string>();
		}
	}

	/// <summary>方案个数。</summary>
	public static int Count()
	{
		return Names().Count;
	}

	/// <summary>当前激活的方案名(空 = 直接编辑态)。</summary>
	public static string ActiveName()
	{
		try
		{
			return LayoutStoreV2.Load().ActiveProfile ?? "";
		}
		catch
		{
			return "";
		}
	}

	/// <summary>取方案快照(null = 不存在)。</summary>
	public static LayoutBody? Snapshot(string? name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}
		try
		{
			return LayoutStoreV2.Load().GetProfile(name);
		}
		catch
		{
			return null;
		}
	}

	/// <summary>方案名校验:"" = 可用。</summary>
	public static string ValidateName(string? name, string? ignoreExisting)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return I18nCore.Tr("方案名不能为空");
		}
		string trimmed = name.Trim();
		if (trimmed.Length > ProfileNameMax)
		{
			return Py.Fmt(I18nCore.Tr("方案名太长(最多 {0} 个字)"), ProfileNameMax);
		}
		if (trimmed != ignoreExisting && Names().Contains(trimmed))
		{
			return Py.Fmt(I18nCore.Tr("方案「{0}」已经存在"), trimmed);
		}
		return "";
	}

	/// <summary>新建方案:fromCurrent = true 时以当前 layout 为基线(§4.C「新建(以当前为基线)」)。</summary>
	public static string Create(string? name, bool fromCurrent)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			string error = ValidateName(name, null);
			if (error.Length > 0)
			{
				return error;
			}
			string trimmed = name!.Trim();
			doc.Profiles[trimmed] = (fromCurrent ? doc.Layout.Clone() : new LayoutBody());
			doc.ActiveProfile = trimmed;
			LayoutStoreV2.Save(doc);
			return "";
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	/// <summary>用给定快照建方案(导入整机方案时用)。</summary>
	public static string CreateFrom(string? name, LayoutBody? body)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			string error = ValidateName(name, null);
			if (error.Length > 0)
			{
				return error;
			}
			string trimmed = name!.Trim();
			doc.Profiles[trimmed] = ((body != null) ? body.Clone() : new LayoutBody());
			doc.ActiveProfile = trimmed;
			LayoutStoreV2.Save(doc);
			return "";
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	/// <summary>
	/// 应用方案 / 导入整机方案前自动留底的方案名(同名覆盖,只留一份):当前布局没原样存在激活方案里
	/// (手调过,或本来就是直接编辑态)时,换掉之前先存到这里,换错了可以在方案列表里切回。
	/// </summary>
	public static string PreviousName => I18nCore.Tr("上一次布局");

	/// <summary>应用方案:原子替换 layout(整块换掉)并把 active_profile 记成它。</summary>
	public static string Apply(string? name)
	{
		return Apply(name, out _);
	}

	/// <summary>同上;<paramref name="kept"/> = 替换前把当前布局留底成的方案名(不必留底时为 "")。</summary>
	public static string Apply(string? name, out string kept)
	{
		kept = "";
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			LayoutBody? body = doc.GetProfile(name ?? "");
			if (body == null)
			{
				return Py.Fmt(I18nCore.Tr("布局方案「{0}」不存在"), name ?? "");
			}
			string target = name!.Trim();
			LayoutBody next = body.Clone();
			kept = KeepPrevious(doc);
			doc.Layout = next;
			// 应用的正是「上一次布局」= 和当前布局对调:这个名字里现在存的是刚换下来的那份,不再是正在用的
			doc.ActiveProfile = ((kept.Length > 0 && string.Equals(kept, target, StringComparison.Ordinal)) ? "" : target);
			LayoutStoreV2.Save(doc);
			return "";
		}
		catch (Exception ex)
		{
			kept = "";
			return ex.Message;
		}
	}

	/// <summary>直接替换当前布局(导入整机方案):原子替换 + 回到直接编辑态(active_profile 清空)。</summary>
	public static string ApplySnapshot(LayoutBody? body)
	{
		return ApplySnapshot(body, out _);
	}

	/// <summary>同上;<paramref name="kept"/> 含义同 <see cref="Apply(string, out string)"/>。</summary>
	public static string ApplySnapshot(LayoutBody? body, out string kept)
	{
		kept = "";
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			kept = KeepPrevious(doc);
			doc.Layout = ((body != null) ? body.Clone() : new LayoutBody());
			doc.ActiveProfile = "";
			LayoutStoreV2.Save(doc);
			return "";
		}
		catch (Exception ex)
		{
			kept = "";
			return ex.Message;
		}
	}

	// 当前布局已经原样存在激活方案里就不必留底;否则存成「上一次布局」(覆盖上一份),返回留底的方案名
	private static string KeepPrevious(LayoutV2 doc)
	{
		LayoutBody? active = doc.GetProfile(doc.ActiveProfile ?? "");
		if (active != null && SameBody(active, doc.Layout))
		{
			return "";
		}
		string name = PreviousName;
		doc.Profiles[name] = doc.Layout.Clone();
		return name;
	}

	private static bool SameBody(LayoutBody a, LayoutBody b)
	{
		return string.Equals(System.Text.Json.JsonSerializer.Serialize(a.ToDict()), System.Text.Json.JsonSerializer.Serialize(b.ToDict()), StringComparison.Ordinal);
	}

	// ————— [LAYOUT-V3-D] 设置 → 布局 v3 用到的小工具(纯逻辑,零界面依赖) —————

	/// <summary>
	/// 把当前布局留底成「上一次布局」并落盘(当前布局已原样存在激活方案里时不留底);返回留底的方案名,
	/// 不必留底 / 失败时为 ""。「页面 → 全部恢复默认」之前调用,恢复错了可以从「方案」里切回。
	/// </summary>
	public static string KeepCurrentAsPrevious()
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			string kept = KeepPrevious(doc);
			if (kept.Length > 0)
			{
				LayoutStoreV2.Save(doc);
			}
			return kept;
		}
		catch (Exception)
		{
			return "";
		}
	}

	/// <summary>
	/// 方案卡片上那一行说明:「N 个页面自定义 · 标准间距」;没有自定义页面时写「页面都是默认排列」。
	/// </summary>
	public static string Describe(LayoutBody? body)
	{
		if (body == null)
		{
			return "";
		}
		int pages = body.Pages?.Count ?? 0;
		string density = (body.Shell?.Density) switch
		{
			LayoutSchema.DensityCompact => I18nCore.Tr("紧凑间距"),
			LayoutSchema.DensityLoose => I18nCore.Tr("宽松间距"),
			_ => I18nCore.Tr("标准间距")
		};
		string head = (pages > 0) ? Py.Fmt(I18nCore.Tr("{0} 个页面自定义"), pages) : I18nCore.Tr("页面都是默认排列");
		return head + " · " + density;
	}

	/// <summary>
	/// 是不是「AI 改布局前」自动留的备份。Linux 目前没有 AI 排版,所以只认两种标记:
	/// 方案元数据 meta.ai_backup = true / meta.source = "ai",或方案名以 AI 备份的固定前缀开头;都没有就不是。
	/// </summary>
	public static bool IsAiBackup(string? name, LayoutBody? body)
	{
		if (body?.Meta != null)
		{
			if (body.Meta.TryGetValue("ai_backup", out object? flag) && IsTrue(flag))
			{
				return true;
			}
			if (body.Meta.TryGetValue("source", out object? source) && string.Equals(source?.ToString(), "ai", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		string trimmed = (name ?? "").Trim();
		foreach (string prefix in AiBackupPrefixes)
		{
			if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	// 方案名前缀只用于识别(与 Windows 的 AI 备份命名一致),不是界面文案
	private static readonly string[] AiBackupPrefixes = new string[4]
	{
		"AI 备份", // i18n:ignore 识别用前缀,不是界面文案
		"AI备份", // i18n:ignore 识别用前缀,不是界面文案
		"AI 改布局前", // i18n:ignore 识别用前缀,不是界面文案
		"AI backup"
	};

	private static bool IsTrue(object? value)
	{
		if (value is bool on)
		{
			return on;
		}
		return string.Equals(value?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
	}

	// ————— [R35 lin-l4] GOAL §5.4-4 方案卡片:显示名 / 一行信息 / 删除守卫 / 排序(纯逻辑,单测钉住) —————

	/// <summary>
	/// AI 改布局前自动备份的方案名前缀(与 Windows <c>LayoutTools.BackupPrefix</c> 同值,后面跟时间)。
	/// 卡片靠它把显示名改成「AI 改布局前 + 时间」。
	/// </summary>
	public const string AiBackupPrefix = "AI 备份 "; // i18n:ignore 识别用前缀,不是界面文案

	/// <summary>方案卡片上的显示名:以 <see cref="AiBackupPrefix"/> 开头的写成「AI 改布局前 MM-dd HH:mm」,其余原样。</summary>
	public static string DisplayName(string? name)
	{
		string text = name ?? "";
		if (!text.StartsWith(AiBackupPrefix, StringComparison.Ordinal))
		{
			return text;
		}
		string suffix = text.Substring(AiBackupPrefix.Length).Trim();
		if (DateTime.TryParseExact(suffix, new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyyMMdd-HHmmss" }, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime when))
		{
			suffix = when.ToString("MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
		}
		return Py.Fmt(I18nCore.Tr("AI 改布局前 {0}"), suffix).Trim();
	}

	/// <summary>
	/// 方案卡片上的一行信息。Linux 的方案(<see cref="LayoutBody"/>)没有「保存时间」字段 ⇒ 普通方案只写
	/// 「N 个页面自定义过 · X间距」(降级,不编造时间);AI 备份 / 自动留底写来源(AI 备份的时间已在显示名里)。
	/// </summary>
	public static string CardInfo(string? name, LayoutBody? body)
	{
		if (IsAiBackup(name, body))
		{
			return I18nCore.Tr("AI 改布局之前自动存的一份");
		}
		if (string.Equals(name, PreviousName, StringComparison.Ordinal))
		{
			return I18nCore.Tr("切换方案或恢复默认之前自动留的底");
		}
		int pages = (body?.Pages != null) ? body.Pages.Count(kv => !string.IsNullOrEmpty(kv.Key) && kv.Value != null) : 0;
		string first = (pages > 0) ? Py.Fmt(I18nCore.Tr("{0} 个页面自定义过"), pages) : I18nCore.Tr("各页面都是默认排列");
		string density = (body?.Shell?.Density) switch
		{
			LayoutSchema.DensityCompact => I18nCore.Tr("紧凑间距"),
			LayoutSchema.DensityLoose => I18nCore.Tr("宽松间距"),
			_ => I18nCore.Tr("标准间距")
		};
		return first + " · " + density;
	}

	/// <summary>正在使用的方案(= active_profile)不能删除;<see cref="Delete"/> 本身语义不变,守卫在界面层调用。</summary>
	public static bool CanDelete(string? name, string? activeName)
	{
		if (string.IsNullOrEmpty(name))
		{
			return false;
		}
		return !string.Equals(name, activeName ?? "", StringComparison.Ordinal);
	}

	/// <summary>
	/// 方案卡片的显示顺序:普通方案(文档顺序)→「上一次布局」自动留底 → AI 备份(新的在前,名字里带时间可直接倒序)。
	/// </summary>
	public static List<string> OrderForCards(IEnumerable<string>? names)
	{
		List<string> all = (names ?? Array.Empty<string>()).Where(n => !string.IsNullOrEmpty(n)).ToList();
		List<string> normal = new List<string>();
		List<string> previous = new List<string>();
		List<string> ai = new List<string>();
		foreach (string n in all)
		{
			if (IsAiBackup(n, null))
			{
				ai.Add(n);
			}
			else if (string.Equals(n, PreviousName, StringComparison.Ordinal))
			{
				previous.Add(n);
			}
			else
			{
				normal.Add(n);
			}
		}
		ai.Sort((a, b) => string.CompareOrdinal(b, a));
		normal.AddRange(previous);
		normal.AddRange(ai);
		return normal;
	}

	/// <summary>config.json 顶层键:网页 / 按钮这类危险组件是否允许添加(本机偏好,不进布局文件、不随方案走)。</summary>
	public const string DangerousWidgetsKey = "layout_dangerous_widgets";

	/// <summary>
	/// 「设置 → 布局 → 高级 → 网页和按钮组件」总开关,默认关。只决定编辑态「添加」抽屉里给不给网页 / 按钮;
	/// 每个已存在的危险组件仍要各自 settings.enabled = true 才渲染(导入进来的照旧默认关闭)。
	/// 读写失败一律当「关」,不影响界面。
	/// </summary>
	public static bool DangerousWidgetsEnabled
	{
		get
		{
			try
			{
				return IsTrue(TipCL.Core.Launcher.Config.CONFIG.Get(DangerousWidgetsKey, false));
			}
			catch (Exception)
			{
				return false;
			}
		}
		set
		{
			try
			{
				LauncherConfig cfg = TipCL.Core.Launcher.Config.CONFIG;
				cfg.Set(DangerousWidgetsKey, value);
				cfg.Save();
			}
			catch (Exception)
			{
				// 偏好写不进去只影响下次启动
			}
		}
	}

	/// <summary>重命名方案(被激活的方案跟着改 active_profile)。</summary>
	public static string Rename(string? oldName, string? newName)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			if (string.IsNullOrEmpty(oldName) || !doc.Profiles.ContainsKey(oldName))
			{
				return Py.Fmt(I18nCore.Tr("布局方案「{0}」不存在"), oldName ?? "");
			}
			string error = ValidateName(newName, oldName);
			if (error.Length > 0)
			{
				return error;
			}
			string trimmed = newName!.Trim();
			if (trimmed == oldName)
			{
				return "";
			}
			// 保序重建:方案列表的显示顺序不能因为改名而跳
			Dictionary<string, LayoutBody> rebuilt = new Dictionary<string, LayoutBody>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, LayoutBody> kv in doc.Profiles)
			{
				rebuilt[(kv.Key == oldName) ? trimmed : kv.Key] = kv.Value;
			}
			doc.Profiles = rebuilt;
			if (doc.ActiveProfile == oldName)
			{
				doc.ActiveProfile = trimmed;
			}
			LayoutStoreV2.Save(doc);
			return "";
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	/// <summary>复制方案。</summary>
	public static string Duplicate(string? name, string? newName)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			LayoutBody? body = doc.GetProfile(name ?? "");
			if (body == null)
			{
				return Py.Fmt(I18nCore.Tr("布局方案「{0}」不存在"), name ?? "");
			}
			string error = ValidateName(newName, null);
			if (error.Length > 0)
			{
				return error;
			}
			doc.Profiles[newName!.Trim()] = body.Clone();
			LayoutStoreV2.Save(doc);
			return "";
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	/// <summary>删除方案(删的是当前激活方案时,active_profile 回到直接编辑态)。</summary>
	public static string Delete(string? name)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			if (string.IsNullOrEmpty(name) || !doc.Profiles.Remove(name))
			{
				return Py.Fmt(I18nCore.Tr("布局方案「{0}」不存在"), name ?? "");
			}
			if (doc.ActiveProfile == name)
			{
				doc.ActiveProfile = "";
			}
			LayoutStoreV2.Save(doc);
			return "";
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	/// <summary>
	/// 把 v1 页面布局(冻结格式,只有 {id,hidden} 与顺序)并进当前文档的 pages:按 pageId 覆盖,
	/// 其余页不动;不落盘(调用方决定)。返回并入的页数。
	/// </summary>
	public static int MergeV1Pages(LayoutV2? doc, IReadOnlyDictionary<string, PageLayoutDoc>? pages)
	{
		if (doc?.Layout == null || pages == null)
		{
			return 0;
		}
		int num = 0;
		foreach (KeyValuePair<string, PageLayoutDoc> kv in pages)
		{
			if (string.IsNullOrEmpty(kv.Key) || kv.Value == null)
			{
				continue;
			}
			// 单页 v1 文档 → Core 的朴素块列表 → FromV1(顺序与显隐逐项保留)
			LayoutV2 migrated = LayoutStoreV2.FromV1(new Dictionary<string, IReadOnlyList<LayoutV1Block>>(StringComparer.Ordinal)
			{
				[kv.Key] = LayoutV1Projection.ToCoreBlocks(kv.Value)
			});
			if (migrated.Layout.Pages.TryGetValue(kv.Key, out PageLayoutV2? page) && page != null)
			{
				doc.Layout.Pages[kv.Key] = page;
				num++;
			}
		}
		return num;
	}
}