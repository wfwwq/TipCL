using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TipCL.Pages.Layout;

// 布局文档 v2 的模型(设计文档 §3.2 数据契约)。纯模型层:禁 WPF、禁界面依赖,
// 这样 net8 的测试宿主也能直接加载(与 PageLayoutDoc / PageLayoutStore 同理)。
// 分工:
//   LayoutDocV2(本文件) = 结构 + 宽容读取 + 保真写出;未知字段原样保留在 Extra 里(保留但不生效)。
//   LayoutSchema         = 唯一契约真相:白名单 / 类型门禁 / 逐字段钳位。
//   LayoutStoreV2        = 落盘(ui_layout_v2.json)+ 迁移(FromV1)+ 写通 v1。

/// <summary>
/// 布局文档 v2 的信封(文件 <c>&lt;数据目录&gt;/ui_layout_v2.json</c>):
/// <c>{"version":2,"kind":"tipcl-layout","saved_at":..,"app":..,"active_profile":..,"layout":{..},"profiles":{名称: LAYOUT}}</c>。
/// </summary>
public sealed class LayoutV2
{
	/// <summary>信封版本号;门禁见 <see cref="LayoutSchema.CurrentVersion"/>。</summary>
	public int Version { get; set; } = LayoutSchema.CurrentVersion;

	/// <summary>信封类型标识,固定 <c>tipcl-layout</c>(老程序不认这个文件名,只做自我识别)。</summary>
	public string Kind { get; set; } = LayoutSchema.Kind;

	/// <summary>保存时间(ISO 8601 带偏移,如 2026-11-14T09:10:00+08:00);仅信息用途。</summary>
	public string SavedAt { get; set; } = "";

	/// <summary>写盘程序版本(信息用途,不参与门禁)。</summary>
	public string App { get; set; } = "";

	/// <summary>当前生效的命名方案名;空串 = 当前是直接编辑态(§4.C)。</summary>
	public string ActiveProfile { get; set; } = "";

	/// <summary>当前 LAYOUT 对象(v2 的唯一真源)。</summary>
	public LayoutBody Layout { get; set; } = new LayoutBody();

	/// <summary>命名方案表:方案名 → LAYOUT 快照。</summary>
	public Dictionary<string, LayoutBody> Profiles { get; set; } = new Dictionary<string, LayoutBody>(StringComparer.Ordinal);

	/// <summary>信封里未知的顶层字段(原样保留、原样写回,但不生效)。</summary>
	public Dictionary<string, object?> Extra { get; set; } = LayoutJson.Map();

	/// <summary>默认空文档(全部字段取 §3.2 的默认值,没有任何页面)。</summary>
	public static LayoutV2 CreateDefault()
	{
		return new LayoutV2();
	}

	/// <summary>深拷贝(编辑/撤销快照与"缓存不给外部改"都用它)。</summary>
	public LayoutV2 Clone()
	{
		LayoutV2 doc = new LayoutV2
		{
			Version = Version,
			Kind = Kind,
			SavedAt = SavedAt,
			App = App,
			ActiveProfile = ActiveProfile,
			Layout = ((Layout != null) ? Layout.Clone() : new LayoutBody()),
			Extra = LayoutJson.CloneMap(Extra)
		};
		if (Profiles != null)
		{
			foreach (KeyValuePair<string, LayoutBody> kv in Profiles)
			{
				if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
				{
					doc.Profiles[kv.Key] = kv.Value.Clone();
				}
			}
		}
		return doc;
	}

	/// <summary>结构规范化(块 id 去重、z 补顺序索引、widget 补 id);不做钳位,钳位在 <see cref="LayoutSchema.Sanitize"/>。</summary>
	public void Normalize()
	{
		if (Layout == null)
		{
			Layout = new LayoutBody();
		}
		Layout.Normalize();
	}

	/// <summary>命名方案列表(按 Profiles 的插入顺序)。</summary>
	public List<Profile> ListProfiles()
	{
		List<Profile> list = new List<Profile>();
		foreach (KeyValuePair<string, LayoutBody> kv in Profiles)
		{
			list.Add(new Profile(kv.Key, kv.Value));
		}
		return list;
	}

	/// <summary>取方案快照;不存在返回 null。</summary>
	public LayoutBody? GetProfile(string name)
	{
		if (!string.IsNullOrEmpty(name) && Profiles.TryGetValue(name, out LayoutBody? body))
		{
			return body;
		}
		return null;
	}

	/// <summary>按 §3.2 的字段与顺序组装写出字典(PyJson 与 System.Text.Json 都能直接序列化)。</summary>
	public Dictionary<string, object?> ToDict()
	{
		Dictionary<string, object?> dict = LayoutJson.Map();
		dict["version"] = Version;
		dict["kind"] = Kind;
		dict["saved_at"] = SavedAt;
		dict["app"] = App;
		dict["active_profile"] = ActiveProfile;
		dict["layout"] = ((Layout != null) ? Layout.ToDict() : new LayoutBody().ToDict());
		Dictionary<string, object?> profiles = LayoutJson.Map();
		if (Profiles != null)
		{
			foreach (KeyValuePair<string, LayoutBody> kv in Profiles)
			{
				if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
				{
					profiles[kv.Key] = kv.Value.ToDict();
				}
			}
		}
		dict["profiles"] = profiles;
		LayoutJson.MergeExtra(dict, Extra);
		return dict;
	}

	/// <summary>序列化为 JSON 文本(缩进 + 中文不转义,风格对齐 PageLayoutDoc.ToJson)。</summary>
	public string ToJson()
	{
		return JsonSerializer.Serialize(ToDict(), new JsonSerializerOptions
		{
			WriteIndented = true,
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
		});
	}

	/// <summary>
	/// 宽容解析(只做结构读取 + 钳位,不做 kind/version 门禁):字段类型不符合契约时退回默认值,未知字段进 Extra。
	/// 解析结果已过 <see cref="LayoutSchema.Sanitize"/>,调用方拿到的一定是合法文档。
	/// </summary>
	public static LayoutV2 FromJson(JsonElement root)
	{
		LayoutV2 doc = new LayoutV2();
		if (root.ValueKind != JsonValueKind.Object)
		{
			return doc;
		}
		if (root.TryGetProperty("version", out JsonElement ver))
		{
			doc.Version = LayoutJson.Integer(ver, LayoutSchema.CurrentVersion);
		}
		if (root.TryGetProperty("kind", out JsonElement kind))
		{
			doc.Kind = LayoutJson.Text(kind, LayoutSchema.Kind);
		}
		if (root.TryGetProperty("saved_at", out JsonElement savedAt))
		{
			doc.SavedAt = LayoutJson.Text(savedAt, "");
		}
		if (root.TryGetProperty("app", out JsonElement app))
		{
			doc.App = LayoutJson.Text(app, "");
		}
		if (root.TryGetProperty("active_profile", out JsonElement active))
		{
			doc.ActiveProfile = LayoutJson.Text(active, "");
		}
		if (LayoutJson.TryObject(root, "layout", out JsonElement layout))
		{
			doc.Layout = LayoutBody.FromJson(layout);
		}
		if (LayoutJson.TryObject(root, "profiles", out JsonElement profiles))
		{
			foreach (JsonProperty prop in profiles.EnumerateObject())
			{
				if (prop.Name.Length != 0 && prop.Value.ValueKind == JsonValueKind.Object)
				{
					doc.Profiles[prop.Name] = LayoutBody.FromJson(prop.Value);
				}
			}
		}
		doc.Extra = LayoutJson.Object(root);
		doc.Extra.Remove("version");
		doc.Extra.Remove("kind");
		doc.Extra.Remove("saved_at");
		doc.Extra.Remove("app");
		doc.Extra.Remove("active_profile");
		doc.Extra.Remove("layout");
		doc.Extra.Remove("profiles");
		return LayoutSchema.Sanitize(doc);
	}

	/// <summary>
	/// 从文本解析:不是 JSON 对象、kind 不是 <c>tipcl-layout</c>、version 不是 2 都返回 null(不抛)。
	/// 这是"文件读取"的门禁;要"导入失败给中文错误"请用 <see cref="LayoutStoreV2.ParseImport"/>。
	/// </summary>
	public static LayoutV2? FromJsonText(string json)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(json ?? "");
			JsonElement root = jsonDocument.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				return null;
			}
			if (!root.TryGetProperty("kind", out JsonElement kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != LayoutSchema.Kind)
			{
				return null;
			}
			if (!root.TryGetProperty("version", out JsonElement ver) || ver.ValueKind != JsonValueKind.Number || !ver.TryGetInt32(out int version) || version != LayoutSchema.CurrentVersion)
			{
				return null;
			}
			return FromJson(root);
		}
		catch
		{
			// 截断 / 非 JSON / 结构诡异:一律当作"读不到",由调用方回落到 v1 子集或默认文档
			return null;
		}
	}
}

/// <summary>LAYOUT 对象:外壳 + 各页布局 + 分栏比例 + 自建区块 + 响应式变体 + 备注。</summary>
public sealed class LayoutBody
{
	/// <summary>外壳(顶栏页签 / 侧栏字段 / 标题栏元素开关 / 全局密度)。</summary>
	public ShellLayout Shell { get; set; } = new ShellLayout();

	/// <summary>逐页布局:pageId → 页面布局(13 个既有页面 + 未来新增)。</summary>
	public Dictionary<string, PageLayoutV2> Pages { get; set; } = new Dictionary<string, PageLayoutV2>(StringComparer.Ordinal);

	/// <summary>分栏比例:pageId → splitId → 0.15..0.85(§4.E)。</summary>
	public Dictionary<string, SplitState> Splits { get; set; } = new Dictionary<string, SplitState>(StringComparer.Ordinal);

	/// <summary>自建区块定义(§4.F);块引用 id 形如 <c>custom:&lt;widgetId&gt;</c>。</summary>
	public List<WidgetDef> Widgets { get; set; } = new List<WidgetDef>();

	/// <summary>响应式变体:<c>narrow</c> → LAYOUT 子集(§0.1 Q6,首期只写不读)。</summary>
	public Dictionary<string, LayoutBody> Variants { get; set; } = new Dictionary<string, LayoutBody>(StringComparer.Ordinal);

	/// <summary>自由备注(分享识别用:来源 / 推荐主题等),不做契约解释。</summary>
	public Dictionary<string, object?> Meta { get; set; } = LayoutJson.Map();

	/// <summary>LAYOUT 层未知字段(保留但不生效)。</summary>
	public Dictionary<string, object?> Extra { get; set; } = LayoutJson.Map();

	/// <summary>深拷贝。</summary>
	public LayoutBody Clone()
	{
		LayoutBody body = new LayoutBody
		{
			Shell = ((Shell != null) ? Shell.Clone() : new ShellLayout()),
			Meta = LayoutJson.CloneMap(Meta),
			Extra = LayoutJson.CloneMap(Extra)
		};
		if (Pages != null)
		{
			foreach (KeyValuePair<string, PageLayoutV2> kv in Pages)
			{
				if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
				{
					body.Pages[kv.Key] = kv.Value.Clone();
				}
			}
		}
		if (Splits != null)
		{
			foreach (KeyValuePair<string, SplitState> kv2 in Splits)
			{
				if (!string.IsNullOrEmpty(kv2.Key) && kv2.Value != null)
				{
					body.Splits[kv2.Key] = kv2.Value.Clone();
				}
			}
		}
		if (Widgets != null)
		{
			foreach (WidgetDef widget in Widgets)
			{
				if (widget != null)
				{
					body.Widgets.Add(widget.Clone());
				}
			}
		}
		if (Variants != null)
		{
			foreach (KeyValuePair<string, LayoutBody> kv3 in Variants)
			{
				if (!string.IsNullOrEmpty(kv3.Key) && kv3.Value != null)
				{
					body.Variants[kv3.Key] = kv3.Value.Clone();
				}
			}
		}
		return body;
	}

	/// <summary>结构规范化:块 id 去重、z 补顺序索引、widget 补自动 id。</summary>
	public void Normalize()
	{
		if (Pages != null)
		{
			foreach (PageLayoutV2 page in Pages.Values)
			{
				if (page != null)
				{
					page.Normalize();
				}
			}
		}
		Dictionary<string, int> used = new Dictionary<string, int>(StringComparer.Ordinal);
		if (Widgets == null)
		{
			return;
		}
		for (int i = 0; i < Widgets.Count; i++)
		{
			WidgetDef widget = Widgets[i];
			if (widget == null)
			{
				continue;
			}
			if (string.IsNullOrEmpty(widget.Id))
			{
				widget.Id = LayoutSchema.NewWidgetId();
			}
			if (used.TryGetValue(widget.Id, out int count))
			{
				used[widget.Id] = count + 1;
				widget.Id = widget.Id + "-" + (count + 1).ToString();
			}
			else
			{
				used[widget.Id] = 1;
			}
		}
	}

	/// <summary>按 §3.2 的字段与顺序组装写出字典。</summary>
	public Dictionary<string, object?> ToDict()
	{
		Dictionary<string, object?> dict = LayoutJson.Map();
		dict["shell"] = ((Shell != null) ? Shell.ToDict() : new ShellLayout().ToDict());
		Dictionary<string, object?> pages = LayoutJson.Map();
		if (Pages != null)
		{
			foreach (KeyValuePair<string, PageLayoutV2> kv in Pages)
			{
				if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
				{
					pages[kv.Key] = kv.Value.ToDict();
				}
			}
		}
		dict["pages"] = pages;
		Dictionary<string, object?> splits = LayoutJson.Map();
		if (Splits != null)
		{
			foreach (KeyValuePair<string, SplitState> kv2 in Splits)
			{
				if (!string.IsNullOrEmpty(kv2.Key) && kv2.Value != null)
				{
					splits[kv2.Key] = kv2.Value.ToDict();
				}
			}
		}
		dict["splits"] = splits;
		List<object?> widgets = new List<object?>();
		if (Widgets != null)
		{
			foreach (WidgetDef widget in Widgets)
			{
				if (widget != null)
				{
					widgets.Add(widget.ToDict());
				}
			}
		}
		dict["widgets"] = widgets;
		Dictionary<string, object?> variants = LayoutJson.Map();
		if (Variants != null)
		{
			foreach (KeyValuePair<string, LayoutBody> kv3 in Variants)
			{
				if (!string.IsNullOrEmpty(kv3.Key) && kv3.Value != null)
				{
					variants[kv3.Key] = kv3.Value.ToDict();
				}
			}
		}
		dict["variants"] = variants;
		dict["meta"] = LayoutJson.CloneMap(Meta);
		LayoutJson.MergeExtra(dict, Extra);
		return dict;
	}

	/// <summary>宽容解析 LAYOUT 对象(不做钳位,由 <see cref="LayoutSchema.Sanitize"/> 统一处理)。</summary>
	public static LayoutBody FromJson(JsonElement e)
	{
		LayoutBody body = new LayoutBody();
		if (e.ValueKind != JsonValueKind.Object)
		{
			return body;
		}
		if (LayoutJson.TryObject(e, "shell", out JsonElement shell))
		{
			body.Shell = ShellLayout.FromJson(shell);
		}
		if (LayoutJson.TryObject(e, "pages", out JsonElement pages))
		{
			foreach (JsonProperty prop in pages.EnumerateObject())
			{
				if (prop.Name.Length != 0 && prop.Value.ValueKind == JsonValueKind.Object)
				{
					body.Pages[prop.Name] = PageLayoutV2.FromJson(prop.Value);
				}
			}
		}
		if (LayoutJson.TryObject(e, "splits", out JsonElement splits))
		{
			foreach (JsonProperty prop2 in splits.EnumerateObject())
			{
				if (prop2.Name.Length != 0 && prop2.Value.ValueKind == JsonValueKind.Object)
				{
					body.Splits[prop2.Name] = SplitState.FromJson(prop2.Value);
				}
			}
		}
		if (LayoutJson.TryArray(e, "widgets", out JsonElement widgets))
		{
			foreach (JsonElement item in widgets.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.Object)
				{
					body.Widgets.Add(WidgetDef.FromJson(item));
				}
			}
		}
		if (LayoutJson.TryObject(e, "variants", out JsonElement variants))
		{
			foreach (JsonProperty prop3 in variants.EnumerateObject())
			{
				if (prop3.Name.Length != 0 && prop3.Value.ValueKind == JsonValueKind.Object)
				{
					body.Variants[prop3.Name] = FromJson(prop3.Value);
				}
			}
		}
		if (LayoutJson.TryObject(e, "meta", out JsonElement meta))
		{
			body.Meta = LayoutJson.Object(meta);
		}
		body.Extra = LayoutJson.Object(e);
		body.Extra.Remove("shell");
		body.Extra.Remove("pages");
		body.Extra.Remove("splits");
		body.Extra.Remove("widgets");
		body.Extra.Remove("variants");
		body.Extra.Remove("meta");
		return body;
	}
}

/// <summary>外壳:顶栏页签顺序/显隐 + 侧栏字段(§0.1 Q1 已否决复活侧栏,字段保留不接线)+ 标题栏开关 + 全局密度。</summary>
public sealed class ShellLayout
{
	/// <summary>导航形态:tabs | sidebar;默认 tabs(Q1 只做顶栏页签,sidebar 字段留位)。</summary>
	public string NavMode { get; set; } = LayoutSchema.NavModeTabs;

	/// <summary>侧栏位置:left | right。</summary>
	public string NavSide { get; set; } = LayoutSchema.NavSideLeft;

	/// <summary>侧栏宽度(夹 140..320,沿用既有后端钳位)。</summary>
	public double NavWidth { get; set; } = LayoutSchema.NavWidthDefault;

	/// <summary>仅图标模式。</summary>
	public bool NavIconOnly { get; set; }

	/// <summary>导航风格:compact | grouped(沿用 NavModel 的既有取值)。</summary>
	public string NavStyle { get; set; } = LayoutSchema.NavStyleGrouped;

	/// <summary>导航顺序(只认合法 key,未知丢弃)。</summary>
	public List<string> NavOrder { get; set; } = new List<string>(LayoutSchema.DefaultNavOrder);

	/// <summary>隐藏的导航项。</summary>
	public List<string> NavHidden { get; set; } = new List<string>();

	/// <summary>自定义分组;空 = 用 NavModel 内置分组(§3.2 默认值见 NavModel.GroupedNav)。</summary>
	public List<NavGroupLayout> NavGroups { get; set; } = new List<NavGroupLayout>();

	/// <summary>顶栏页签顺序/显隐;空则回落默认五项。</summary>
	public List<string> Tabs { get; set; } = new List<string>(LayoutSchema.DefaultTabs);

	/// <summary>标题栏元素开关。</summary>
	public TitlebarLayout Titlebar { get; set; } = new TitlebarLayout();

	/// <summary>全局密度:compact | standard | loose。</summary>
	public string Density { get; set; } = LayoutSchema.DensityStandard;

	/// <summary>shell 层未知字段(保留但不生效)。</summary>
	public Dictionary<string, object?> Extra { get; set; } = LayoutJson.Map();

	/// <summary>深拷贝。</summary>
	public ShellLayout Clone()
	{
		ShellLayout shell = new ShellLayout
		{
			NavMode = NavMode,
			NavSide = NavSide,
			NavWidth = NavWidth,
			NavIconOnly = NavIconOnly,
			NavStyle = NavStyle,
			NavOrder = new List<string>(NavOrder ?? new List<string>()),
			NavHidden = new List<string>(NavHidden ?? new List<string>()),
			Tabs = new List<string>(Tabs ?? new List<string>()),
			Titlebar = ((Titlebar != null) ? Titlebar.Clone() : new TitlebarLayout()),
			Density = Density,
			Extra = LayoutJson.CloneMap(Extra)
		};
		if (NavGroups != null)
		{
			foreach (NavGroupLayout group in NavGroups)
			{
				if (group != null)
				{
					shell.NavGroups.Add(group.Clone());
				}
			}
		}
		return shell;
	}

	/// <summary>按 §3.2 的字段与顺序组装写出字典。</summary>
	public Dictionary<string, object?> ToDict()
	{
		Dictionary<string, object?> dict = LayoutJson.Map();
		dict["nav_mode"] = NavMode;
		dict["nav_side"] = NavSide;
		dict["nav_width"] = LayoutJson.Num(NavWidth);
		dict["nav_icon_only"] = NavIconOnly;
		dict["nav_style"] = NavStyle;
		dict["nav_order"] = LayoutJson.ListOf(NavOrder);
		dict["nav_hidden"] = LayoutJson.ListOf(NavHidden);
		List<object?> groups = new List<object?>();
		if (NavGroups != null)
		{
			foreach (NavGroupLayout group in NavGroups)
			{
				if (group != null)
				{
					groups.Add(group.ToDict());
				}
			}
		}
		dict["nav_groups"] = groups;
		dict["tabs"] = LayoutJson.ListOf(Tabs);
		dict["titlebar"] = ((Titlebar != null) ? Titlebar.ToDict() : new TitlebarLayout().ToDict());
		dict["density"] = Density;
		LayoutJson.MergeExtra(dict, Extra);
		return dict;
	}

	/// <summary>宽容解析 shell 对象。</summary>
	public static ShellLayout FromJson(JsonElement e)
	{
		ShellLayout shell = new ShellLayout();
		if (e.ValueKind != JsonValueKind.Object)
		{
			return shell;
		}
		if (e.TryGetProperty("nav_mode", out JsonElement navMode))
		{
			shell.NavMode = LayoutJson.Text(navMode, shell.NavMode);
		}
		if (e.TryGetProperty("nav_side", out JsonElement navSide))
		{
			shell.NavSide = LayoutJson.Text(navSide, shell.NavSide);
		}
		if (e.TryGetProperty("nav_width", out JsonElement navWidth))
		{
			shell.NavWidth = LayoutJson.Number(navWidth, shell.NavWidth);
		}
		if (e.TryGetProperty("nav_icon_only", out JsonElement navIconOnly))
		{
			shell.NavIconOnly = LayoutJson.Bool(navIconOnly, shell.NavIconOnly);
		}
		if (e.TryGetProperty("nav_style", out JsonElement navStyle))
		{
			shell.NavStyle = LayoutJson.Text(navStyle, shell.NavStyle);
		}
		if (e.TryGetProperty("nav_order", out JsonElement navOrder))
		{
			List<string> list = LayoutJson.TextList(navOrder);
			if (list.Count != 0)
			{
				shell.NavOrder = list;
			}
		}
		if (e.TryGetProperty("nav_hidden", out JsonElement navHidden))
		{
			shell.NavHidden = LayoutJson.TextList(navHidden);
		}
		if (LayoutJson.TryArray(e, "nav_groups", out JsonElement navGroups))
		{
			foreach (JsonElement item in navGroups.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.Object)
				{
					shell.NavGroups.Add(NavGroupLayout.FromJson(item));
				}
			}
		}
		if (e.TryGetProperty("tabs", out JsonElement tabs))
		{
			shell.Tabs = LayoutJson.TextList(tabs);
		}
		if (LayoutJson.TryObject(e, "titlebar", out JsonElement titlebar))
		{
			shell.Titlebar = TitlebarLayout.FromJson(titlebar);
		}
		if (e.TryGetProperty("density", out JsonElement density))
		{
			shell.Density = LayoutJson.Text(density, shell.Density);
		}
		shell.Extra = LayoutJson.Object(e);
		shell.Extra.Remove("nav_mode");
		shell.Extra.Remove("nav_side");
		shell.Extra.Remove("nav_width");
		shell.Extra.Remove("nav_icon_only");
		shell.Extra.Remove("nav_style");
		shell.Extra.Remove("nav_order");
		shell.Extra.Remove("nav_hidden");
		shell.Extra.Remove("nav_groups");
		shell.Extra.Remove("tabs");
		shell.Extra.Remove("titlebar");
		shell.Extra.Remove("density");
		return shell;
	}
}

/// <summary>导航分组:标题(允许用户改名)+ 成员 key 列表。</summary>
public sealed class NavGroupLayout
{
	/// <summary>分组标题(用户可改;空串按"未命名"处理)。</summary>
	public string Title { get; set; } = "";

	/// <summary>成员导航 key(只认合法 key)。</summary>
	public List<string> Keys { get; set; } = new List<string>();

	/// <summary>未知字段(保留但不生效)。</summary>
	public Dictionary<string, object?> Extra { get; set; } = LayoutJson.Map();

	/// <summary>深拷贝。</summary>
	public NavGroupLayout Clone()
	{
		return new NavGroupLayout
		{
			Title = Title,
			Keys = new List<string>(Keys ?? new List<string>()),
			Extra = LayoutJson.CloneMap(Extra)
		};
	}

	/// <summary>按 {title, keys} 写出。</summary>
	public Dictionary<string, object?> ToDict()
	{
		Dictionary<string, object?> dict = LayoutJson.Map();
		dict["title"] = Title;
		dict["keys"] = LayoutJson.ListOf(Keys);
		LayoutJson.MergeExtra(dict, Extra);
		return dict;
	}

	/// <summary>宽容解析。</summary>
	public static NavGroupLayout FromJson(JsonElement e)
	{
		NavGroupLayout group = new NavGroupLayout();
		if (e.ValueKind != JsonValueKind.Object)
		{
			return group;
		}
		if (e.TryGetProperty("title", out JsonElement title))
		{
			group.Title = LayoutJson.Text(title, "");
		}
		if (e.TryGetProperty("keys", out JsonElement keys))
		{
			group.Keys = LayoutJson.TextList(keys);
		}
		group.Extra = LayoutJson.Object(e);
		group.Extra.Remove("title");
		group.Extra.Remove("keys");
		return group;
	}
}

/// <summary>标题栏元素开关(§4.D:只允许隐藏/显示,D-16 的 AI 入口形态不可回退)。</summary>
public sealed class TitlebarLayout
{
	/// <summary>桥接药丸钮。</summary>
	public bool Bridge { get; set; } = true;

	/// <summary>AI 入口钮。</summary>
	public bool Ai { get; set; } = true;

	/// <summary>主题切换钮。</summary>
	public bool Theme { get; set; } = true;

	/// <summary>账号钮。</summary>
	public bool Account { get; set; } = true;

	/// <summary>未知字段(保留但不生效)。</summary>
	public Dictionary<string, object?> Extra { get; set; } = LayoutJson.Map();

	/// <summary>深拷贝。</summary>
	public TitlebarLayout Clone()
	{
		return new TitlebarLayout
		{
			Bridge = Bridge,
			Ai = Ai,
			Theme = Theme,
			Account = Account,
			Extra = LayoutJson.CloneMap(Extra)
		};
	}

	/// <summary>按 {bridge, ai, theme, account} 写出。</summary>
	public Dictionary<string, object?> ToDict()
	{
		Dictionary<string, object?> dict = LayoutJson.Map();
		dict["bridge"] = Bridge;
		dict["ai"] = Ai;
		dict["theme"] = Theme;
		dict["account"] = Account;
		LayoutJson.MergeExtra(dict, Extra);
		return dict;
	}

	/// <summary>宽容解析。</summary>
	public static TitlebarLayout FromJson(JsonElement e)
	{
		TitlebarLayout bar = new TitlebarLayout();
		if (e.ValueKind != JsonValueKind.Object)
		{
			return bar;
		}
		if (e.TryGetProperty("bridge", out JsonElement bridge))
		{
			bar.Bridge = LayoutJson.Bool(bridge, true);
		}
		if (e.TryGetProperty("ai", out JsonElement ai))
		{
			bar.Ai = LayoutJson.Bool(ai, true);
		}
		if (e.TryGetProperty("theme", out JsonElement theme))
		{
			bar.Theme = LayoutJson.Bool(theme, true);
		}
		if (e.TryGetProperty("account", out JsonElement account))
		{
			bar.Account = LayoutJson.Bool(account, true);
		}
		bar.Extra = LayoutJson.Object(e);
		bar.Extra.Remove("bridge");
		bar.Extra.Remove("ai");
		bar.Extra.Remove("theme");
		bar.Extra.Remove("account");
		return bar;
	}
}

/// <summary>单页布局(§3.2 pages[pageId]):模式 / 栅格 / 页级间距边距 + 块数组(顺序即显示顺序)。</summary>
public sealed class PageLayoutV2
{
	/// <summary>单页版本门禁;必须等于 2,否则该页回默认(与 v1 的单页版本门禁同思路)。</summary>
	public int Version { get; set; } = LayoutSchema.CurrentVersion;

	/// <summary>页面布局模式:stack | grid | canvas。</summary>
	public string Mode { get; set; } = LayoutSchema.ModeStack;

	/// <summary>grid 模式列数(只认 {2,3,4,6,12},其余按最近档吸附)。</summary>
	public int Cols { get; set; } = LayoutSchema.ColsDefault;

	/// <summary>grid/canvas 行高:0 = 按内容自适应,&gt;0 = 固定行高 px。</summary>
	public double RowH { get; set; }

	/// <summary>块间距(现状硬编码 BlockHost.Gap = 10)。</summary>
	public double Gap { get; set; } = LayoutSchema.GapDefault;

	/// <summary>内容边距 [左, 上, 右, 下](现状 DefaultPad = 18,14,18,16,对齐 PageBase.ScrollBody)。</summary>
	public double[] Pad { get; set; } = LayoutSchema.PadDefaultCopy();

	/// <summary>块数组:顺序即显示顺序(隐藏项也占位,与 v1 语义一致)。</summary>
	public List<BlockLayout> Blocks { get; set; } = new List<BlockLayout>();

	/// <summary>page 层未知字段(保留但不生效)。</summary>
	public Dictionary<string, object?> Extra { get; set; } = LayoutJson.Map();

	/// <summary>深拷贝。</summary>
	public PageLayoutV2 Clone()
	{
		PageLayoutV2 page = new PageLayoutV2
		{
			Version = Version,
			Mode = Mode,
			Cols = Cols,
			RowH = RowH,
			Gap = Gap,
			Pad = LayoutSchema.PadCopy(Pad),
			Extra = LayoutJson.CloneMap(Extra)
		};
		if (Blocks != null)
		{
			foreach (BlockLayout block in Blocks)
			{
				if (block != null)
				{
					page.Blocks.Add(block.Clone());
				}
			}
		}
		return page;
	}

	/// <summary>结构规范化:块 id 去重、丢弃空 id、z 补顺序索引。</summary>
	public void Normalize()
	{
		List<BlockLayout> list = new List<BlockLayout>();
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		if (Blocks != null)
		{
			foreach (BlockLayout block in Blocks)
			{
				if (block != null && !string.IsNullOrEmpty(block.Id) && seen.Add(block.Id))
				{
					list.Add(block);
				}
			}
		}
		Blocks = list;
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i].Z < 0)
			{
				list[i].Z = i;
			}
		}
	}

	/// <summary>按 §3.2 的字段与顺序组装写出字典。</summary>
	public Dictionary<string, object?> ToDict()
	{
		Dictionary<string, object?> dict = LayoutJson.Map();
		dict["version"] = Version;
		dict["mode"] = Mode;
		dict["cols"] = Cols;
		dict["row_h"] = LayoutJson.Num(RowH);
		dict["gap"] = LayoutJson.Num(Gap);
		dict["pad"] = LayoutJson.PadList(Pad);
		List<object?> blocks = new List<object?>();
		if (Blocks != null)
		{
			foreach (BlockLayout block in Blocks)
			{
				if (block != null)
				{
					blocks.Add(block.ToDict());
				}
			}
		}
		dict["blocks"] = blocks;
		LayoutJson.MergeExtra(dict, Extra);
		return dict;
	}

	/// <summary>宽容解析单页对象(不做钳位)。</summary>
	public static PageLayoutV2 FromJson(JsonElement e)
	{
		PageLayoutV2 page = new PageLayoutV2();
		if (e.ValueKind != JsonValueKind.Object)
		{
			return page;
		}
		if (e.TryGetProperty("version", out JsonElement ver))
		{
			page.Version = LayoutJson.Integer(ver, LayoutSchema.CurrentVersion);
		}
		if (e.TryGetProperty("mode", out JsonElement mode))
		{
			page.Mode = LayoutJson.Text(mode, page.Mode);
		}
		if (e.TryGetProperty("cols", out JsonElement cols))
		{
			page.Cols = LayoutJson.Integer(cols, page.Cols);
		}
		if (e.TryGetProperty("row_h", out JsonElement rowH))
		{
			page.RowH = LayoutJson.Number(rowH, page.RowH);
		}
		if (e.TryGetProperty("gap", out JsonElement gap))
		{
			page.Gap = LayoutJson.Number(gap, page.Gap);
		}
		if (e.TryGetProperty("pad", out JsonElement pad))
		{
			page.Pad = LayoutJson.Pad(pad, LayoutSchema.PadDefaultCopy());
		}
		if (LayoutJson.TryArray(e, "blocks", out JsonElement blocks))
		{
			foreach (JsonElement item in blocks.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.Object)
				{
					page.Blocks.Add(BlockLayout.FromJson(item));
				}
			}
		}
		page.Extra = LayoutJson.Object(e);
		page.Extra.Remove("version");
		page.Extra.Remove("mode");
		page.Extra.Remove("cols");
		page.Extra.Remove("row_h");
		page.Extra.Remove("gap");
		page.Extra.Remove("pad");
		page.Extra.Remove("blocks");
		return page;
	}
}

/// <summary>单块布局(§3.2 pages[].blocks[i]):v1 的 <c>{id,hidden}</c> 是它的子集,同名同义。</summary>
public sealed class BlockLayout
{
	/// <summary>块 id(非空、去重;未知 id 由页面白名单过滤,不由存储层丢)。</summary>
	public string Id { get; set; } = "";

	/// <summary>隐藏(与 v1 同义:只认 JSON 布尔 true)。</summary>
	public bool Hidden { get; set; }

	/// <summary>位置 X(grid: 0..cols-1;canvas: 归一化 0..1)。</summary>
	public double X { get; set; }

	/// <summary>位置 Y(grid: 0..cols-1;canvas: 归一化 0..1)。</summary>
	public double Y { get; set; }

	/// <summary>尺寸 W(grid: 1..cols;canvas: 0.04..1)。</summary>
	public double W { get; set; } = 1.0;

	/// <summary>尺寸 H(grid: 1..cols;canvas: 0.04..1)。</summary>
	public double H { get; set; } = 1.0;

	/// <summary>stack 模式下占几列(1..cols,对齐 §4.A 的"多列栈模式")。</summary>
	public int Span { get; set; } = 1;

	/// <summary>外观:plain(现状零装饰) | card | divider。</summary>
	public string Style { get; set; } = LayoutSchema.StylePlain;

	/// <summary>常态是否显示块标题。</summary>
	public bool Title { get; set; }

	/// <summary>每块边距覆盖;null = 用页级 pad。</summary>
	public double[]? Pad { get; set; }

	/// <summary>块最小高度(拖拽缩放下限;0 = 用类型默认)。</summary>
	public double MinH { get; set; }

	/// <summary>叠放序(canvas 用);-1 = 未设置,Normalize 时填顺序索引。</summary>
	public int Z { get; set; } = -1;

	/// <summary>每块自由参数(自建区块 / 插件块用)。</summary>
	public Dictionary<string, object?> Settings { get; set; } = LayoutJson.Map();

	/// <summary>block 层未知字段(保留但不生效)。</summary>
	public Dictionary<string, object?> Extra { get; set; } = LayoutJson.Map();

	/// <summary>深拷贝。</summary>
	public BlockLayout Clone()
	{
		return new BlockLayout
		{
			Id = Id,
			Hidden = Hidden,
			X = X,
			Y = Y,
			W = W,
			H = H,
			Span = Span,
			Style = Style,
			Title = Title,
			Pad = ((Pad != null) ? LayoutSchema.PadCopy(Pad) : null),
			MinH = MinH,
			Z = Z,
			Settings = LayoutJson.CloneMap(Settings),
			Extra = LayoutJson.CloneMap(Extra)
		};
	}

	/// <summary>按 §3.2 的字段与顺序组装写出字典(pad 为 null 时不写该字段)。</summary>
	public Dictionary<string, object?> ToDict()
	{
		Dictionary<string, object?> dict = LayoutJson.Map();
		dict["id"] = Id;
		dict["hidden"] = Hidden;
		dict["x"] = LayoutJson.Num(X);
		dict["y"] = LayoutJson.Num(Y);
		dict["w"] = LayoutJson.Num(W);
		dict["h"] = LayoutJson.Num(H);
		dict["span"] = Span;
		dict["style"] = Style;
		dict["title"] = Title;
		if (Pad != null)
		{
			dict["pad"] = LayoutJson.PadList(Pad);
		}
		dict["min_h"] = LayoutJson.Num(MinH);
		dict["z"] = Z;
		dict["settings"] = LayoutJson.CloneMap(Settings);
		LayoutJson.MergeExtra(dict, Extra);
		return dict;
	}

	/// <summary>宽容解析单块对象(不做钳位)。</summary>
	public static BlockLayout FromJson(JsonElement e)
	{
		BlockLayout block = new BlockLayout();
		if (e.ValueKind != JsonValueKind.Object)
		{
			return block;
		}
		if (e.TryGetProperty("id", out JsonElement id))
		{
			block.Id = LayoutJson.Text(id, "");
		}
		if (e.TryGetProperty("hidden", out JsonElement hidden))
		{
			block.Hidden = LayoutJson.Bool(hidden, false);
		}
		if (e.TryGetProperty("x", out JsonElement x))
		{
			block.X = LayoutJson.Number(x, block.X);
		}
		if (e.TryGetProperty("y", out JsonElement y))
		{
			block.Y = LayoutJson.Number(y, block.Y);
		}
		if (e.TryGetProperty("w", out JsonElement w))
		{
			block.W = LayoutJson.Number(w, block.W);
		}
		if (e.TryGetProperty("h", out JsonElement h))
		{
			block.H = LayoutJson.Number(h, block.H);
		}
		if (e.TryGetProperty("span", out JsonElement span))
		{
			block.Span = LayoutJson.Integer(span, block.Span);
		}
		if (e.TryGetProperty("style", out JsonElement style))
		{
			block.Style = LayoutJson.Text(style, block.Style);
		}
		if (e.TryGetProperty("title", out JsonElement title))
		{
			block.Title = LayoutJson.Bool(title, false);
		}
		if (e.TryGetProperty("pad", out JsonElement pad) && pad.ValueKind == JsonValueKind.Array)
		{
			block.Pad = LayoutJson.Pad(pad, LayoutSchema.PadDefaultCopy());
		}
		if (e.TryGetProperty("min_h", out JsonElement minH))
		{
			block.MinH = LayoutJson.Number(minH, block.MinH);
		}
		if (e.TryGetProperty("z", out JsonElement z))
		{
			block.Z = LayoutJson.Integer(z, block.Z);
		}
		if (LayoutJson.TryObject(e, "settings", out JsonElement settings))
		{
			block.Settings = LayoutJson.Object(settings);
		}
		block.Extra = LayoutJson.Object(e);
		block.Extra.Remove("id");
		block.Extra.Remove("hidden");
		block.Extra.Remove("x");
		block.Extra.Remove("y");
		block.Extra.Remove("w");
		block.Extra.Remove("h");
		block.Extra.Remove("span");
		block.Extra.Remove("style");
		block.Extra.Remove("title");
		block.Extra.Remove("pad");
		block.Extra.Remove("min_h");
		block.Extra.Remove("z");
		block.Extra.Remove("settings");
		return block;
	}
}

/// <summary>单页分栏比例:splitId → 0.15..0.85(§4.E)。</summary>
public sealed class SplitState
{
	/// <summary>分隔条比例表(splitId → ratio)。</summary>
	public Dictionary<string, double> Ratios { get; set; } = new Dictionary<string, double>(StringComparer.Ordinal);

	/// <summary>未知字段(保留但不生效)。</summary>
	public Dictionary<string, object?> Extra { get; set; } = LayoutJson.Map();

	/// <summary>深拷贝。</summary>
	public SplitState Clone()
	{
		SplitState split = new SplitState
		{
			Extra = LayoutJson.CloneMap(Extra)
		};
		if (Ratios != null)
		{
			foreach (KeyValuePair<string, double> kv in Ratios)
			{
				if (!string.IsNullOrEmpty(kv.Key))
				{
					split.Ratios[kv.Key] = kv.Value;
				}
			}
		}
		return split;
	}

	/// <summary>取比例;不存在返回默认值。</summary>
	public double Get(string splitId, double dflt)
	{
		if (!string.IsNullOrEmpty(splitId) && Ratios.TryGetValue(splitId, out double ratio))
		{
			return ratio;
		}
		return dflt;
	}

	/// <summary>按 splitId → ratio 平铺写出(PyJson 只支持标量值,不能嵌套 Dictionary&lt;string,double&gt;)。</summary>
	public Dictionary<string, object?> ToDict()
	{
		Dictionary<string, object?> dict = LayoutJson.Map();
		if (Ratios != null)
		{
			foreach (KeyValuePair<string, double> kv in Ratios)
			{
				if (!string.IsNullOrEmpty(kv.Key))
				{
					dict[kv.Key] = LayoutJson.Num(kv.Value);
				}
			}
		}
		LayoutJson.MergeExtra(dict, Extra);
		return dict;
	}

	/// <summary>宽容解析:只认数字值,其余(嵌套对象等)进 Extra。</summary>
	public static SplitState FromJson(JsonElement e)
	{
		SplitState split = new SplitState();
		if (e.ValueKind != JsonValueKind.Object)
		{
			return split;
		}
		foreach (JsonProperty prop in e.EnumerateObject())
		{
			if (prop.Name.Length == 0)
			{
				continue;
			}
			if (prop.Value.ValueKind == JsonValueKind.Number)
			{
				split.Ratios[prop.Name] = LayoutJson.Number(prop.Value, LayoutSchema.RatioDefault);
			}
			else
			{
				split.Extra[prop.Name] = LayoutJson.Plain(prop.Value);
			}
		}
		return split;
	}
}

/// <summary>自建区块定义(§4.F);块引用 id 形如 <c>custom:&lt;id&gt;</c>。</summary>
public sealed class WidgetDef
{
	/// <summary>widget id(空则由 Normalize 自动补 <c>w-&lt;时间戳&gt;</c>)。</summary>
	public string Id { get; set; } = "";

	/// <summary>类型:首期白名单 note | shortcut | clock | divider | playtime;web | button 默认关闭(Q5)。</summary>
	public string Type { get; set; } = "";

	/// <summary>块标题(用户可改)。</summary>
	public string Title { get; set; } = "";

	/// <summary>各类型自有参数(如 shortcut:{instance:..}、clock:{format:"HH:mm"})。</summary>
	public Dictionary<string, object?> Settings { get; set; } = LayoutJson.Map();

	/// <summary>未知字段(保留但不生效)。</summary>
	public Dictionary<string, object?> Extra { get; set; } = LayoutJson.Map();

	/// <summary>深拷贝。</summary>
	public WidgetDef Clone()
	{
		return new WidgetDef
		{
			Id = Id,
			Type = Type,
			Title = Title,
			Settings = LayoutJson.CloneMap(Settings),
			Extra = LayoutJson.CloneMap(Extra)
		};
	}

	/// <summary>按 §3.2 的字段与顺序组装写出字典。</summary>
	public Dictionary<string, object?> ToDict()
	{
		Dictionary<string, object?> dict = LayoutJson.Map();
		dict["id"] = Id;
		dict["type"] = Type;
		dict["title"] = Title;
		dict["settings"] = LayoutJson.CloneMap(Settings);
		LayoutJson.MergeExtra(dict, Extra);
		return dict;
	}

	/// <summary>宽容解析。</summary>
	public static WidgetDef FromJson(JsonElement e)
	{
		WidgetDef widget = new WidgetDef();
		if (e.ValueKind != JsonValueKind.Object)
		{
			return widget;
		}
		if (e.TryGetProperty("id", out JsonElement id))
		{
			widget.Id = LayoutJson.Text(id, "");
		}
		if (e.TryGetProperty("type", out JsonElement type))
		{
			widget.Type = LayoutJson.Text(type, "");
		}
		if (e.TryGetProperty("title", out JsonElement title))
		{
			widget.Title = LayoutJson.Text(title, "");
		}
		if (LayoutJson.TryObject(e, "settings", out JsonElement settings))
		{
			widget.Settings = LayoutJson.Object(settings);
		}
		widget.Extra = LayoutJson.Object(e);
		widget.Extra.Remove("id");
		widget.Extra.Remove("type");
		widget.Extra.Remove("title");
		widget.Extra.Remove("settings");
		return widget;
	}
}

/// <summary>命名布局方案(profiles 里的一项):方案名 + LAYOUT 快照。</summary>
public sealed class Profile
{
	/// <summary>方案名(profiles 的 key)。</summary>
	public string Name { get; set; } = "";

	/// <summary>方案快照。</summary>
	public LayoutBody Layout { get; set; } = new LayoutBody();

	/// <summary>空方案。</summary>
	public Profile()
	{
	}

	/// <summary>按名字与快照构造。</summary>
	public Profile(string name, LayoutBody layout)
	{
		Name = name ?? "";
		Layout = layout ?? new LayoutBody();
	}
}
