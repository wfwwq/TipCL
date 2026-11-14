using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core.Launcher.Layout;

namespace TipCL.Core.Launcher.Ai;

/// <summary>
/// AI 布局工具(L6):让 AI 读当前布局、改布局。
/// <list type="bullet">
/// <item><c>layout_get</c>(ro=true):返回布局摘要 —— 每个页面的区块顺序与显隐、页面模式、自建区块、分栏比例、顶栏页签、命名方案。</item>
/// <item><c>layout_apply</c>(ro=false, side_effect=write_local):**写**布局。改之前自动把当前布局备份成
/// <c>profiles["AI 备份 &lt;时间&gt;"]</c>(只留最近 5 份),再走 LayoutStoreV2.Save(v2 真源 + 单向写通 v1)。</item>
/// </list>
/// 支持的写法(可组合;至少要给一个):
/// <c>page</c>+<c>blocks</c>(某页顺序/显隐)、<c>mode</c>/<c>cols</c>/<c>gap</c>(该页页级参数)、<c>profile</c>(应用方案)、
/// <c>json</c>(整份 v2 文档)、<c>widgets</c>(新增/覆盖自建块)、<c>shell_tabs</c>(顶栏页签)。
/// 校验失败一律返回中文原因(不把异常抛给模型),成功时返回值里带备份方案名。
/// </summary>
public static class LayoutTools
{
	/// <summary>只读工具名。</summary>
	public const string GetName = "layout_get";

	/// <summary>写工具名(ro=false,走确认通道)。</summary>
	public const string ApplyName = "layout_apply";

	/// <summary>自动备份用的方案名前缀(§4.F / R12)。</summary>
	public const string BackupPrefix = "AI 备份 ";

	/// <summary>最多保留几份 AI 备份(避免 profiles 无限膨胀)。</summary>
	public const int MaxBackups = 5;

	private const int MaxBlocksPerPage = 512;

	private const string SchemasJson = """
	[
	  {
	    "type": "function",
	    "function": {
	      "name": "layout_get",
	      "description": "查看当前界面布局:每个页面的区块顺序与显隐、页面布局模式、自建区块(note/shortcut/clock/divider/playtime)、分栏比例、顶栏页签、命名方案。改布局之前先看它,不要凭记忆改。",
	      "parameters": { "type": "object", "properties": {} }
	    }
	  },
	  {
	    "type": "function",
	    "function": {
	      "name": "layout_apply",
	      "description": "修改界面布局。改之前会自动把当前布局备份成一个命名方案(profiles 里的「AI 备份 …」),用户可以随时切回去。可组合使用:page + blocks 改某一页的区块顺序与显隐;profile 应用一份已保存的方案;json 提交整份布局文档;widgets 新增或覆盖自建区块;shell_tabs 改顶栏页签。改完要告诉用户改了什么、备份叫什么名字。",
	      "parameters": {
	        "type": "object",
	        "properties": {
	          "page": { "type": "string", "description": "要改的页面 id,如 launch / tasks / settings:general" },
	          "blocks": {
	            "type": "array",
	            "description": "该页区块的新顺序;每项 {id, hidden}。没列出的内置区块会按默认可见性补到末尾",
	            "items": {
	              "type": "object",
	              "properties": {
	                "id": { "type": "string" },
	                "hidden": { "type": "boolean" }
	              }
	            }
	          },
	          "mode": { "type": "string", "description": "该页布局模式:stack / grid / canvas" },
	          "cols": { "type": "integer", "description": "该页列数:{2,3,4,6,12}" },
	          "gap": { "type": "number", "description": "该页块间距 0..32,默认 10" },
	          "profile": { "type": "string", "description": "应用一份已保存的命名方案(layout_get 里有方案名)" },
	          "json": { "type": "object", "description": "整份 v2 布局文档(kind=tipcl-layout,version=2)" },
	          "widgets": {
	            "type": "array",
	            "description": "新增或覆盖自建区块定义:每项 {id, type, title, settings};type 只能是 note/shortcut/clock/divider/playtime(web/button 属危险类型,必须 settings.enabled=true)",
	            "items": { "type": "object" }
	          },
	          "shell_tabs": {
	            "type": "array",
	            "description": "顶栏页签的顺序与显隐,取值只能是 launch/library/download/plugins/settings",
	            "items": { "type": "string" }
	          }
	        }
	      }
	    }
	  }
	]
	""";

	/// <summary>两个工具的 schema(由 ExtraTools.Schemas() 合并进工具表)。</summary>
	public static List<object> Schemas()
	{
		return (List<object>)PyJson.Loads(SchemasJson);
	}

	/// <summary>是否本模块负责的工具名。</summary>
	public static bool Handles(string name)
	{
		return name == GetName || name == ApplyName;
	}

	/// <summary>分发(由 ExtraTools.Run 调用)。</summary>
	public static string Run(string name, PyDict args)
	{
		return (name == GetName) ? Get() : Apply(args);
	}

	/// <summary>布局摘要(AI 看的紧凑格式,不含整份方案快照)。</summary>
	public static string Get()
	{
		LayoutV2 doc = LayoutStoreV2.Load();
		Dictionary<string, object?> summary = Map();
		summary["version"] = LayoutSchema.CurrentVersion;
		summary["active_profile"] = doc.ActiveProfile;
		summary["profiles"] = doc.Profiles.Keys.ToList<object?>();
		Dictionary<string, object?> shell = Map();
		shell["tabs"] = doc.Layout.Shell.Tabs.OfType<string>().ToList<object?>();
		shell["density"] = doc.Layout.Shell.Density;
		summary["shell"] = shell;
		Dictionary<string, object?> pages = Map();
		foreach (KeyValuePair<string, PageLayoutV2> kv in doc.Layout.Pages)
		{
			Dictionary<string, object?> page = Map();
			page["mode"] = kv.Value.Mode;
			page["cols"] = kv.Value.Cols;
			page["gap"] = kv.Value.Gap;
			List<object?> blocks = new List<object?>();
			foreach (BlockLayout block in kv.Value.Blocks)
			{
				blocks.Add(block.Hidden ? (block.Id + "(hidden)") : block.Id);
			}
			page["blocks"] = blocks;
			pages[kv.Key] = page;
		}
		summary["pages"] = pages;
		if (doc.Layout.Widgets.Count > 0)
		{
			List<object?> widgets = new List<object?>();
			foreach (WidgetDef widget in doc.Layout.Widgets)
			{
				Dictionary<string, object?> item = Map();
				item["id"] = widget.Id;
				item["type"] = widget.Type;
				item["title"] = widget.Title;
				item["block_id"] = "custom:" + widget.Id;
				if (LayoutSchema.IsDangerousWidgetType(widget.Type))
				{
					item["enabled"] = WidgetEnabled(widget);
				}
				widgets.Add(item);
			}
			summary["widgets"] = widgets;
		}
		if (doc.Layout.Splits.Count > 0)
		{
			Dictionary<string, object?> splits = Map();
			foreach (KeyValuePair<string, SplitState> kv2 in doc.Layout.Splits)
			{
				Dictionary<string, object?> ratios = Map();
				foreach (KeyValuePair<string, double> ratio in kv2.Value.Ratios)
				{
					ratios[ratio.Key] = ratio.Value;
				}
				splits[kv2.Key] = ratios;
			}
			summary["splits"] = splits;
		}
		return PyJson.Dumps(summary, 2, ensureAscii: false);
	}

	/// <summary>
	/// 应用布局改动:先在副本上改 → 校验 → 把**改之前**的布局备份成方案 → Save(v2 + 写通 v1)。
	/// 任何一步失败都返回中文原因,且不落盘(不会留下半成品)。
	/// </summary>
	public static string Apply(PyDict args)
	{
		PyDict input = args ?? new PyDict();
		LayoutV2 current = LayoutStoreV2.Load();
		LayoutV2 next = current.Clone();
		List<string> changes = new List<string>();
		string? error = ApplyOps(next, input, changes);
		if (error != null)
		{
			return error;
		}
		if (changes.Count == 0)
		{
			return "没有识别到要修改的内容:请给 page+blocks(改某一页)、profile(应用方案)、json(整份文档)、widgets 或 shell_tabs 中的一个。";
		}
		string backup = BackupPrefix + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
		next.Profiles[backup] = current.Layout.Clone();
		PruneBackups(next);
		LayoutSchema.Sanitize(next);
		LayoutStoreV2.Save(next);
		// 写通 v1 后清掉老路径的缓存,让还走 v1 的读取立刻看到新值(L0 报告的坑 3);
		// 具体动作在注入缝里(Pages 侧 = PageLayoutStore.Reload),Core 不需要认识它
		LayoutStoreV2.RefreshV1Cache();
		return "已更新布局:" + string.Join(";", changes) + "。改动前的布局已备份为方案「" + backup + "」(可在设置 → 布局里切回)。";
	}

	// ————— 具体改动(每项都往 changes 里记一句中文说明) —————

	private static string? ApplyOps(LayoutV2 next, PyDict args, List<string> changes)
	{
		object jsonValue = args.Get("json");
		if (Py.Truthy(jsonValue))
		{
			string text = ((jsonValue is string s) ? s : PyJson.Dumps(jsonValue, 2, ensureAscii: false));
			LayoutV2 parsed;
			try
			{
				parsed = LayoutStoreV2.ParseImport(text);
			}
			catch (InvalidOperationException ex)
			{
				return "这份布局 JSON 用不了:" + ex.Message;
			}
			next.Layout = parsed.Layout;
			next.Profiles = parsed.Profiles;
			next.ActiveProfile = parsed.ActiveProfile;
			changes.Add("用整份 JSON 替换了布局");
		}
		string profile = Py.S(Py.Or(args.Get("profile"), "")).Trim();
		if (profile.Length > 0)
		{
			if (!next.Profiles.TryGetValue(profile, out LayoutBody? body) || body == null)
			{
				return "找不到名为「" + profile + "」的布局方案;先用 layout_get 看现有方案名。";
			}
			next.Layout = body.Clone();
			next.ActiveProfile = profile;
			changes.Add("应用方案「" + profile + "」");
		}
		string page = Py.S(Py.Or(args.Get("page"), "")).Trim();
		List<object> blocks = ListOrEmpty(args, "blocks");
		bool hasPageParams = args.Get("mode") != null || args.Get("cols") != null || args.Get("gap") != null;
		if (page.Length > 0 && (blocks.Count > 0 || hasPageParams))
		{
			PageLayoutV2? existing = null;
			if (next.Layout.Pages.TryGetValue(page, out PageLayoutV2? found))
			{
				existing = found;
			}
			PageLayoutV2 target = ((existing != null) ? existing.Clone() : new PageLayoutV2());
			if (blocks.Count > 0)
			{
				if (blocks.Count > MaxBlocksPerPage)
				{
					return "单个页面的区块不能超过 " + MaxBlocksPerPage + " 个。";
				}
				List<LayoutV1Block> ordered = new List<LayoutV1Block>();
				foreach (object item in blocks)
				{
					if (!(item is PyDict entry))
					{
						continue;
					}
					string id = Py.S(Py.Or(entry.Get("id"), "")).Trim();
					if (id.Length == 0)
					{
						continue;
					}
					ordered.Add(new LayoutV1Block
					{
						Id = id,
						Hidden = Py.Truthy(entry.Get("hidden"))
					});
				}
				if (ordered.Count == 0)
				{
					return "blocks 里没有有效的区块 id。";
				}
				// 顺序 / 显隐以 AI 给的最新顺序为准;同一块的其它 v2 字段(span / style / settings)按 id 保留
				// (实现只有一份:Core 的 LayoutStoreV2.MergeV1Order)
				target = LayoutStoreV2.MergeV1Order(target, ordered);
			}
			if (args.Get("mode") != null)
			{
				target.Mode = Py.S(Py.Or(args.Get("mode"), "")).Trim();
			}
			if (args.Get("cols") != null)
			{
				target.Cols = (int)Py.Int(args.Get("cols"));
			}
			if (args.Get("gap") != null)
			{
				target.Gap = Py.Float(args.Get("gap"));
			}
			next.Layout.Pages[page] = target;
			changes.Add("页面 " + page + " 的区块顺序/显隐" + (hasPageParams ? "与页面参数" : ""));
		}
		List<object> widgets = ListOrEmpty(args, "widgets");
		if (widgets.Count > 0)
		{
			List<string> touched = new List<string>();
			foreach (object item in widgets)
			{
				if (!(item is PyDict entry))
				{
					continue;
				}
				string id = Py.S(Py.Or(entry.Get("id"), "")).Trim();
				string type = Py.S(Py.Or(entry.Get("type"), "")).Trim();
				if (id.Length == 0)
				{
					// 缺 id 时按类型自动生成(与 LayoutSchema.NewWidgetId 同规则)
					id = LayoutSchema.NewWidgetId();
				}
				if (type.Length == 0)
				{
					return "自建区块 " + id + " 没写 type。";
				}
				if (!LayoutSchema.IsKnownWidgetType(type))
				{
					return "不认识的区块类型「" + type + "」;首期只支持 note / shortcut / clock / divider / playtime(web / button 需显式开启)。";
				}
				WidgetDef? existingWidget = null;
				foreach (WidgetDef w in next.Layout.Widgets)
				{
					if (w != null && string.Equals(w.Id, id, StringComparison.Ordinal))
					{
						existingWidget = w;
						break;
					}
				}
				WidgetDef def = ((existingWidget != null) ? existingWidget : new WidgetDef
				{
					Id = id
				});
				def.Id = id;
				def.Type = type;
				def.Title = Py.S(Py.Or(entry.Get("title"), "")).Trim();
				Dictionary<string, object?> settings = Plain(entry.Get("settings")) as Dictionary<string, object?> ?? new Dictionary<string, object?>(StringComparer.Ordinal);
				def.Settings = settings;
				if (existingWidget == null)
				{
					next.Layout.Widgets.Add(def);
				}
				touched.Add(type + ":" + id);
			}
			if (touched.Count > 0)
			{
				changes.Add("自建区块 " + string.Join("/", touched));
			}
		}
		List<object> tabs = ListOrEmpty(args, "shell_tabs");
		if (tabs.Count > 0)
		{
			List<string> names = new List<string>();
			foreach (object item in tabs)
			{
				string name = Py.S(item).Trim();
				if (name.Length == 0)
				{
					continue;
				}
				if (!LayoutSchema.IsKnownTab(name))
				{
					return "顶栏页签只支持 launch / library / download / plugins / settings,不认识「" + name + "」。";
				}
				if (!names.Contains(name))
				{
					names.Add(name);
				}
			}
			if (names.Count == 0)
			{
				return "shell_tabs 里没有有效的页签名。";
			}
			next.Layout.Shell.Tabs = names;
			changes.Add("顶栏页签 → " + string.Join(" / ", names));
		}
		return null;
	}

	// AI 备份只留最近 5 份(按名字里的时间倒序),避免 profiles 被撑爆
	private static void PruneBackups(LayoutV2 doc)
	{
		List<string> names = doc.Profiles.Keys.Where((string key) => key.StartsWith(BackupPrefix, StringComparison.Ordinal)).OrderByDescending((string key) => key, StringComparer.Ordinal).ToList();
		for (int i = MaxBackups; i < names.Count; i++)
		{
			doc.Profiles.Remove(names[i]);
		}
	}

	private static bool WidgetEnabled(WidgetDef widget)
	{
		return widget.Settings.TryGetValue("enabled", out object? value) && value is bool flag && flag;
	}

	// 可选数组参数:缺失 / 空 / 类型不对都当"没给"(Py.ListCtor(null) 会抛 TypeError,不能直接调)
	private static List<object> ListOrEmpty(PyDict args, string key)
	{
		object? value = args.Get(key);
		if (value == null || value is string)
		{
			return new List<object>();
		}
		if (value is List<object> list)
		{
			return list;
		}
		if (!Py.Truthy(value))
		{
			return new List<object>();
		}
		return Py.ListCtor(value);
	}

	private static Dictionary<string, object?> Map()
	{
		return new Dictionary<string, object?>(StringComparer.Ordinal);
	}

	// PyDict / List<object> → 契约用的朴素值(字符串 / long / double / bool / 字典 / 列表)
	private static object? Plain(object? value)
	{
		if (value == null)
		{
			return null;
		}
		if (value is PyDict dict)
		{
			Dictionary<string, object?> map = Map();
			foreach (string key in dict.Keys)
			{
				map[key] = Plain(dict[key]);
			}
			return map;
		}
		if (value is string text)
		{
			return text;
		}
		if (value is bool flag)
		{
			return flag;
		}
		if (value is int i)
		{
			return (long)i;
		}
		if (value is long || value is double)
		{
			return value;
		}
		if (value is System.Collections.IEnumerable list)
		{
			List<object?> items = new List<object?>();
			foreach (object item in list)
			{
				items.Add(Plain(item));
			}
			return items;
		}
		return value;
	}
}
