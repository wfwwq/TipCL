using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TipCL.Core.Launcher.Layout;
using TipCL.Services;

namespace TipCL.Pages;

// 来源:Windows 版冻结件 wt-r31-merge @0a304a7
//       windos/src/TipCL/Pages/SettingsLayoutPage.cs 的 `LayoutProfiles`(:975 起;纯逻辑)。
//
// 移植方式:逐字等价。命名空间 `TipCL.Pages.Layout` → `TipCL.Pages`。
// 依赖面已逐条核对在 mac 树上存在:
//   LayoutStoreV2.Load()/Save()/FromV1()、LayoutV2.Profiles/ActiveProfile/GetProfile/Clone、
//   LayoutBody.Clone()/ToDict()、PageLayoutV2、LayoutV1Projection.ToCoreBlocks()、
//   PageLayoutDoc(I18n.L 带参数重载)。
// 唯一显式写出的命名空间差异:用 `System.Text.Json.JsonSerializer` 全名(与 Windows 同写法)。

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
			return I18n.L("方案名不能为空");
		}
		string trimmed = name.Trim();
		if (trimmed.Length > ProfileNameMax)
		{
			return I18n.L("方案名太长(最多 {0} 个字)", ProfileNameMax);
		}
		if (trimmed != ignoreExisting && Names().Contains(trimmed))
		{
			return I18n.L("方案「{0}」已经存在", trimmed);
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
	public static string PreviousName => I18n.L("上一次布局");

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
				return I18n.L("布局方案「{0}」不存在", name ?? "");
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

	/// <summary>重命名方案(被激活的方案跟着改 active_profile)。</summary>
	public static string Rename(string? oldName, string? newName)
	{
		try
		{
			LayoutV2 doc = LayoutStoreV2.Load();
			if (string.IsNullOrEmpty(oldName) || !doc.Profiles.ContainsKey(oldName))
			{
				return I18n.L("布局方案「{0}」不存在", oldName ?? "");
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
				return I18n.L("布局方案「{0}」不存在", name ?? "");
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
				return I18n.L("布局方案「{0}」不存在", name ?? "");
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