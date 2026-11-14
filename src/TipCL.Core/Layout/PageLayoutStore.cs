// 来源: Windows 版参考仓库 src/TipCL/Pages/PageLayoutStore.cs（M2-B4 搬运，仅改命名空间；
// Windows 的 BridgeHost.FindRoot() 换成 RootProvider 委托——App 侧接自己的数据目录定位，纯测试环境可不设）
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TipCL.Core.Layout;

// 页面布局持久化（纯模型层，禁 UI）：
//   ui_page_layouts.json = {"version":1,"kind":"tipcl-page-layout","pages":{pageId: PageLayoutDoc}}
// 旧配置缺失 / 损坏 = 全默认布局（Load → null / LoadAll → 空字典），不报错；
// Save 内部吞掉所有异常（写不进去也不能崩 UI），并同步内存缓存。
public static class PageLayoutStore
{
	public const string Kind = "tipcl-page-layout";

	private const string FileName = "ui_page_layouts.json";

	private static readonly object _gate = new object();

	private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	private static Dictionary<string, PageLayoutDoc>? _cache;

	private static string? _cacheDir;

	// 测试注入临时目录用；null 时用真实数据目录
	public static string? DirOverride { get; set; }

	// 数据目录定位钩子：App 启动时设为自己的路径服务；抛异常时回退 AppContext.BaseDirectory
	// （对齐 Windows 版 BridgeHost.FindRoot 的回退语义）。测试里通常配 DirOverride，不走这里。
	public static Func<string>? RootProvider { get; set; }

	public static string DirPath
	{
		get
		{
			string? over = DirOverride;
			string dir;
			if (string.IsNullOrEmpty(over))
			{
				try
				{
					dir = RootProvider?.Invoke() ?? AppContext.BaseDirectory;
				}
				catch
				{
					dir = AppContext.BaseDirectory;
				}
			}
			else
			{
				dir = over;
			}
			try
			{
				Directory.CreateDirectory(dir);
			}
			catch
			{
			}
			return dir;
		}
	}

	public static string FilePath => Path.Combine(DirPath, FileName);

	// 文件缺失 / 损坏 / JSON 里没有这一页 → null；首次访问后走内存缓存
	public static PageLayoutDoc? Load(string pageId)
	{
		lock (_gate)
		{
			Dictionary<string, PageLayoutDoc> pages = Cache();
			if (!string.IsNullOrEmpty(pageId) && pages.TryGetValue(pageId, out PageLayoutDoc? doc))
			{
				return doc;
			}
			return null;
		}
	}

	public static IReadOnlyDictionary<string, PageLayoutDoc> LoadAll()
	{
		lock (_gate)
		{
			return new Dictionary<string, PageLayoutDoc>(Cache());
		}
	}

	// 更新内存缓存 + 原子落盘（写临时文件再 File.Replace/Move，对齐 PluginStateStore 的写法）
	public static void Save(string pageId, PageLayoutDoc doc)
	{
		if (string.IsNullOrEmpty(pageId) || doc == null)
		{
			return;
		}
		lock (_gate)
		{
			try
			{
				Cache()[pageId] = doc;
				WriteFile(FilePath, Cache());
			}
			catch
			{
				// 磁盘写不进去也不能崩 UI：内存里保留最新值，下次保存再试
			}
		}
	}

	// {"version":1,"kind":"tipcl-page-layout","pages":{pageId: PageLayoutDoc.ToDict()}}
	public static string ExportJson(IEnumerable<KeyValuePair<string, PageLayoutDoc>> pages)
	{
		Dictionary<string, object> objs = new Dictionary<string, object>();
		if (pages != null)
		{
			foreach (KeyValuePair<string, PageLayoutDoc> kv in pages)
			{
				if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
				{
					objs[kv.Key] = kv.Value.ToDict();
				}
			}
		}
		Dictionary<string, object> root = new Dictionary<string, object>
		{
			["version"] = PageLayoutDoc.CurrentVersion,
			["kind"] = Kind,
			["pages"] = objs
		};
		return JsonSerializer.Serialize(root, JsonOpts);
	}

	// kind/version 校验失败抛 InvalidOperationException（消息中文）；单页损坏跳过该页
	public static IReadOnlyDictionary<string, PageLayoutDoc> ParseImport(string json)
	{
		JsonDocument doc;
		try
		{
			doc = JsonDocument.Parse(json ?? "");
		}
		catch
		{
			throw new InvalidOperationException(LayoutText.L("布局文件无效"));
		}
		using (doc)
		{
			Dictionary<string, PageLayoutDoc> pages = new Dictionary<string, PageLayoutDoc>();
			JsonElement root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object
				|| !root.TryGetProperty("kind", out JsonElement kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != Kind
				|| !root.TryGetProperty("version", out JsonElement ver) || ver.ValueKind != JsonValueKind.Number || !ver.TryGetInt32(out int v) || v != PageLayoutDoc.CurrentVersion)
			{
				throw new InvalidOperationException(LayoutText.L("布局文件无效"));
			}
			if (root.TryGetProperty("pages", out JsonElement ps) && ps.ValueKind == JsonValueKind.Object)
			{
				foreach (JsonProperty prop in ps.EnumerateObject())
				{
					PageLayoutDoc? d = ParseLoose(prop.Value);
					if (d != null && prop.Name.Length > 0)
					{
						pages[prop.Name] = d;
					}
				}
			}
			return pages;
		}
	}

	// 存储层不知道各页面的合法 id 集合，所以用文档自身的块集合做白名单原样装载；
	// 未知块的真正过滤交给页面加载时的 Normalize（BlockHost.ApplyDoc）
	private static PageLayoutDoc? ParseLoose(JsonElement e)
	{
		List<string> ids = new List<string>();
		if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("blocks", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in arr.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out JsonElement idEl) && idEl.ValueKind == JsonValueKind.String)
				{
					string id = idEl.GetString() ?? "";
					if (id.Length > 0)
					{
						ids.Add(id);
					}
				}
			}
		}
		return PageLayoutDoc.FromJson(e, ids, ids);
	}

	// 缓存跟着目录走：DirOverride / 数据目录变化后自动重读（测试之间互不串扰）
	private static Dictionary<string, PageLayoutDoc> Cache()
	{
		string dir = DirPath;
		if (_cache == null || _cacheDir != dir)
		{
			_cache = ReadFile(Path.Combine(dir, FileName));
			_cacheDir = dir;
		}
		return _cache;
	}

	private static Dictionary<string, PageLayoutDoc> ReadFile(string path)
	{
		Dictionary<string, PageLayoutDoc> pages = new Dictionary<string, PageLayoutDoc>();
		try
		{
			if (!File.Exists(path))
			{
				return pages;
			}
			using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
			JsonElement root = doc.RootElement;
			if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("pages", out JsonElement ps) && ps.ValueKind == JsonValueKind.Object)
			{
				foreach (JsonProperty prop in ps.EnumerateObject())
				{
					PageLayoutDoc? d = ParseLoose(prop.Value);
					if (d != null && prop.Name.Length > 0)
					{
						pages[prop.Name] = d;
					}
				}
			}
		}
		catch
		{
			pages.Clear();
		}
		return pages;
	}

	private static void WriteFile(string path, Dictionary<string, PageLayoutDoc> pages)
	{
		string? dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir))
		{
			Directory.CreateDirectory(dir);
		}
		string tmp = path + ".tmp";
		File.WriteAllText(tmp, ExportJson(pages), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		if (File.Exists(path))
		{
			File.Replace(tmp, path, null);
		}
		else
		{
			File.Move(tmp, path);
		}
	}
}
