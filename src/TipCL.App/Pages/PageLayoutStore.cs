using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TipCL.Services;

namespace TipCL.Pages;

// C3a:Windows 版 Pages\PageLayoutStore.cs 的移植(纯模型层,禁 WPF;mac 侧同样不碰任何界面类型)。
//   ui_page_layouts.json = {"version":1,"kind":"tipcl-page-layout","pages":{pageId: PageLayoutDoc}}
// 旧配置缺失 / 损坏 = 全默认布局(Load → null / LoadAll → 空字典),不报错;
// Save 内部吞掉所有异常(写不进去也不能崩 UI),并同步内存缓存。
//
// mac 侧两处移植差异(逐条记在 PROGRESS.md 的 C3a 差异表):
// 1. 数据目录:Windows 用 BridgeHost.FindRoot()(payload 根),mac 版没有 payload,
//    BridgeHost.FindRoot() 就是 Utils.ResolveRoot()(~/.tipcl,TIPCL_HOME 可覆盖)。
//    调用形状与 try/catch 兜底一字未改。
// 2. 原子落盘:Windows 是 File.Replace(tmp, path, null),mac 改成
//    File.Move(tmp, path, overwrite: true) —— 见 DECISIONS.md D-116(File.Replace 在 Unix
//    上的支持情况本机不可验证,而 Move/overwrite 是两个平台都有的原子改名)。语义不变:
//    先写 .tmp 再改名,不会留下半截文件。
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

	// 测试注入临时目录用;null 时用真实数据目录
	public static string? DirOverride { get; set; }

	public static string DirPath
	{
		get
		{
			string? over = DirOverride;
			string dir;
			if (string.IsNullOrEmpty(over))
			{
				// 对齐 MainWindow.PrefsPath 的写法:FindRoot 失败回退 BaseDirectory
				try
				{
					dir = BridgeHost.FindRoot();
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

	// 文件缺失 / 损坏 / JSON 里没有这一页 → null;首次访问后走内存缓存
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

	// 更新内存缓存 + 原子落盘(写临时文件再改名)
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
				// 磁盘写不进去也不能崩 UI:内存里保留最新值,下次保存再试
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

	// kind/version 校验失败抛 InvalidOperationException(消息中文);单页损坏跳过该页
	public static IReadOnlyDictionary<string, PageLayoutDoc> ParseImport(string json)
	{
		JsonDocument doc;
		try
		{
			doc = JsonDocument.Parse(json ?? "");
		}
		catch
		{
			throw new InvalidOperationException(I18n.L("布局文件无效"));
		}
		using (doc)
		{
			Dictionary<string, PageLayoutDoc> pages = new Dictionary<string, PageLayoutDoc>();
			JsonElement root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object
				|| !root.TryGetProperty("kind", out JsonElement kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != Kind
				|| !root.TryGetProperty("version", out JsonElement ver) || ver.ValueKind != JsonValueKind.Number || !ver.TryGetInt32(out int v) || v != PageLayoutDoc.CurrentVersion)
			{
				throw new InvalidOperationException(I18n.L("布局文件无效"));
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

	// 存储层不知道各页面的合法 id 集合,所以用文档自身的块集合做白名单原样装载;
	// 未知块的真正过滤交给页面加载时的 Normalize(BlockHost.ApplyDoc)
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

	// 缓存跟着目录走:DirOverride / 数据目录变化后自动重读(测试之间互不串扰)
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

	// mac 差异 2:WriteAllText 的编码写法一字未改(UTF-8 无 BOM),改名那一步见类注释。
	private static void WriteFile(string path, Dictionary<string, PageLayoutDoc> pages)
	{
		string? dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir))
		{
			Directory.CreateDirectory(dir);
		}
		string tmp = path + ".tmp";
		File.WriteAllText(tmp, ExportJson(pages), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		File.Move(tmp, path, overwrite: true);
	}

	// ————— 测试用注入缝(C3a 新增,不影响上面任何语义) —————

	/// <summary>
	/// 丢掉内存缓存,强制下一次访问重新读盘。生产代码不需要它(缓存跟着 <see cref="DirPath"/> 走);
	/// 测试用它来模拟"换了一台机器 / 重启了应用"之后再读盘的能力。
	/// </summary>
	internal static void ResetCache()
	{
		lock (_gate)
		{
			_cache = null;
			_cacheDir = null;
		}
	}
}
