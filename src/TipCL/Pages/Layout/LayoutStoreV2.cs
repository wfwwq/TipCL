using System;
using System.Collections.Generic;
using System.IO;
using TipCL.Core.Launcher;
using TipCL.Services;

namespace TipCL.Pages.Layout;

// 布局 v2 的持久化(设计文档 §3.1 D2/D3 + §3.3 兼容矩阵):
//   真源 = <数据目录>/ui_layout_v2.json(kind=tipcl-layout, version=2);
//   v1(<数据目录>/ui_page_layouts.json)只是**由 v2 单向写通**的兼容投影,永远只有
//   version / kind / pages[id].{version, blocks[{id,hidden}]},老程序与 mac/linux 端口照旧能读。
// 三条硬规矩:
//   1) 读:**绝不抛**:v2 截断/非 JSON/kind/version 不对 → 回落 v1 子集 → 再不行回默认文档;
//   2) 写:先写 v2,成功了才写通 v1(否则 v1 会比 v2 新,用户会看到"改了又回退");两者都原子写(Utils.WriteJson);
//   3) 磁盘写不进去也要吞掉异常并保留内存里的最新值(对齐 PageLayoutStore 的既有范式)。
public static class LayoutStoreV2
{
	/// <summary>v2 文件名(老程序完全不认识这个文件名,不会读也不会删)。</summary>
	public const string FileName = "ui_layout_v2.json";

	/// <summary>v1 文件名(语义冻结,只由 v2 写通)。</summary>
	public const string V1FileName = "ui_page_layouts.json";

	/// <summary>上次装载来源:见 <see cref="LastLoadSource"/>。</summary>
	private static string _lastSource = "";

	private static readonly object _gate = new object();

	private static LayoutV2? _cache;

	private static string? _cacheDir;

	/// <summary>测试注入临时目录用;null 时用真实数据目录(与 PageLayoutStore 同目录)。</summary>
	public static string? DirOverride { get; set; }

	/// <summary>上次装载的来源:见 <see cref="LastLoadSource"/>。</summary>
	public static string LastLoadSource { get { return _lastSource; } }

	/// <summary>布局发生变更(保存后)时触发;设计 §3.1 D5 的 LayoutChanged。</summary>
	public static event Action? LayoutChanged;

	/// <summary>数据目录(与 PageLayoutStore.DirPath 完全一致的回落顺序)。</summary>
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

	/// <summary>v2 文件绝对路径。</summary>
	public static string FilePath { get { return Path.Combine(DirPath, FileName); } }

	/// <summary>v1 文件绝对路径(只读它做迁移,写它只走写通投影)。</summary>
	public static string V1FilePath { get { return Path.Combine(DirPath, V1FileName); } }

	/// <summary>
	/// 读布局(绝不抛):v2 优先;v2 缺失/损坏/版本不对 → 回落 v1 子集;v1 也没有 → 默认文档。
	/// 返回的是副本,调用方随便改都不会污染进程内缓存。
	/// </summary>
	public static LayoutV2 Load()
	{
		lock (_gate)
		{
			string dir = DirPath;
			if (_cache == null || _cacheDir != dir)
			{
				SyncV1DirOverride();
				LayoutV2? doc = ReadV2File(Path.Combine(dir, FileName));
				if (doc != null)
				{
					_cache = doc;
					_lastSource = "v2";
				}
				else
				{
					Dictionary<string, PageLayoutDoc>? v1 = SafeLoadAllV1();
					if (v1 != null && v1.Count != 0)
					{
						_cache = FromV1(v1);
						_lastSource = "v1-fallback";
					}
					else
					{
						_cache = LayoutV2.CreateDefault();
						_lastSource = "default";
					}
				}
				_cacheDir = dir;
			}
			return _cache.Clone();
		}
	}

	/// <summary>
	/// 升级首跑用:只有 v1 时把迁移结果**落成 v2**,返回迁移后的文档。
	/// v1 文件原样不动(它是安全网);已有 v2 时与 <see cref="Load"/> 等价。
	/// </summary>
	public static LayoutV2 LoadMigrating()
	{
		lock (_gate)
		{
			SyncV1DirOverride();
			string dir = DirPath;
			LayoutV2? onDisk = ReadV2File(Path.Combine(dir, FileName));
			if (onDisk != null)
			{
				_cache = onDisk;
				_cacheDir = dir;
				_lastSource = "v2";
				return onDisk.Clone();
			}
			Dictionary<string, PageLayoutDoc>? v1 = SafeLoadAllV1();
			bool hasV1 = v1 != null && v1.Count != 0;
			LayoutV2 migrated = (hasV1 ? FromV1(v1) : LayoutV2.CreateDefault());
			migrated.SavedAt = Timestamp();
			migrated.App = AppVersion();
			_cache = migrated;
			_cacheDir = dir;
			_lastSource = (hasV1 ? "v1-migrated" : "default");
			if (hasV1)
			{
				// 迁移只写 v2:v1 里的 {id,hidden} 与顺序已经是对的,不该动它
				WriteV2File(migrated);
			}
			return migrated.Clone();
		}
	}

	/// <summary>
	/// 保存:先原子写 v2,再**由 v2 单向写通 v1**;两次写都吞异常(写不进去也不能崩 UI),
	/// 内存里始终保留最新值,并触发 <see cref="LayoutChanged"/>。
	/// </summary>
	public static void Save(LayoutV2 doc)
	{
		if (doc == null)
		{
			return;
		}
		lock (_gate)
		{
			LayoutSchema.Sanitize(doc);
			doc.SavedAt = Timestamp();
			doc.App = AppVersion();
			SyncV1DirOverride();
			string dir = DirPath;
			_cache = doc.Clone();
			_cacheDir = dir;
			_lastSource = "v2";
			if (WriteV2File(doc))
			{
				// 只有 v2 落地成功才写通 v1:反过来会让 v1 比 v2 新,违反"v2 是唯一真源"
				WriteV1Projection(doc);
			}
		}
		RaiseChanged();
	}

	// L1 接线须知(本包不改 PageLayoutStore):它按目录在进程内只读一次,没有 Reload()
	// (PageLayoutStore.cs:197-206)。所以写通 v1 文件后,同进程里再走 PageLayoutStore.Load 拿到的是
	// 它自己的旧快照 —— 新版必须把 LayoutStoreV2 当真源读取;若确实要让 v1 路径立刻看到新值,
	// 需要 L1 按设计 §5.2 给 PageLayoutStore 加 Reload()(清缓存),本包已提供 WriteThroughV1() 供其调用。

	/// <summary>把当前真源(v2 缓存/磁盘)重新写通一遍 v1;外部改过 v2 文件后可用它强制同步。</summary>
	public static void WriteThroughV1()
	{
		lock (_gate)
		{
			SyncV1DirOverride();
			WriteV1Projection(Load());
		}
	}

	/// <summary>清掉进程内缓存,下次 <see cref="Load"/> 重新读盘(测试注入与"外部改了文件"的检测用)。</summary>
	public static void ResetCache()
	{
		lock (_gate)
		{
			_cache = null;
			_cacheDir = null;
			_lastSource = "";
		}
	}

	/// <summary>
	/// v1 → v2 迁移(§3.3 第一行):每个 v1 page 的 blocks 顺序与 hidden **逐项保留**,
	/// 新增字段一律取默认(mode=stack、cols=12、gap=10、style=plain、title=false...)。
	/// </summary>
	public static LayoutV2 FromV1(IEnumerable<KeyValuePair<string, PageLayoutDoc>>? pages)
	{
		LayoutV2 doc = LayoutV2.CreateDefault();
		if (pages != null)
		{
			foreach (KeyValuePair<string, PageLayoutDoc> kv in pages)
			{
				if (string.IsNullOrEmpty(kv.Key) || kv.Value == null || doc.Layout.Pages.ContainsKey(kv.Key))
				{
					continue;
				}
				PageLayoutV2 page = new PageLayoutV2();
				if (kv.Value.Blocks != null)
				{
					foreach (BlockState block in kv.Value.Blocks)
					{
						if (block != null && !string.IsNullOrEmpty(block.Id))
						{
							page.Blocks.Add(new BlockLayout
							{
								Id = block.Id,
								Hidden = block.Hidden
							});
						}
					}
				}
				doc.Layout.Pages[kv.Key] = page;
			}
		}
		return LayoutSchema.Sanitize(doc);
	}

	/// <summary>直接从 v1 文件迁移(读不到 = 空文档)。</summary>
	public static LayoutV2 FromV1File()
	{
		SyncV1DirOverride();
		return FromV1(SafeLoadAllV1());
	}

	/// <summary>
	/// v2 → v1 投影:只保留 v1 认识的 {id,hidden}(用冻结的 v1 类型组装,历史字段一个字都不多加)。
	/// </summary>
	public static Dictionary<string, PageLayoutDoc> ProjectV1(LayoutV2? doc)
	{
		Dictionary<string, PageLayoutDoc> pages = new Dictionary<string, PageLayoutDoc>(StringComparer.Ordinal);
		if (doc == null || doc.Layout == null || doc.Layout.Pages == null)
		{
			return pages;
		}
		foreach (KeyValuePair<string, PageLayoutV2> kv in doc.Layout.Pages)
		{
			if (string.IsNullOrEmpty(kv.Key) || kv.Value == null)
			{
				continue;
			}
			PageLayoutDoc v1 = new PageLayoutDoc();
			if (kv.Value.Blocks != null)
			{
				foreach (BlockLayout block in kv.Value.Blocks)
				{
					if (block != null && !string.IsNullOrEmpty(block.Id))
					{
						v1.Blocks.Add(new BlockState
						{
							Id = block.Id,
							Hidden = block.Hidden
						});
					}
				}
			}
			pages[kv.Key] = v1;
		}
		return pages;
	}

	/// <summary>导出 JSON 文本(分享/整机方案用;先钳位再序列化,不改调用方对象)。</summary>
	public static string ExportJson(LayoutV2? doc)
	{
		LayoutV2 copy = ((doc != null) ? doc.Clone() : LayoutV2.CreateDefault());
		LayoutSchema.Sanitize(copy);
		return copy.ToJson();
	}

	/// <summary>
	/// 导入校验:kind 必须是 tipcl-layout、version 必须是 2,再逐字段白名单过滤 + 钳位。
	/// 非法文件抛 <see cref="InvalidOperationException"/>(消息中文),不返回半成品。
	/// </summary>
	public static LayoutV2 ParseImport(string json)
	{
		LayoutV2? doc = LayoutV2.FromJsonText(json);
		if (doc == null)
		{
			throw new InvalidOperationException(I18n.L("布局文件无效"));
		}
		return doc;
	}

	/// <summary>写通 v1 的文件内容:{"version":1,"kind":"tipcl-page-layout","pages":{pageId:{version,blocks}}}。</summary>
	public static Dictionary<string, object?> V1Envelope(LayoutV2? doc)
	{
		Dictionary<string, object?> pages = LayoutJson.Map();
		foreach (KeyValuePair<string, PageLayoutDoc> kv in ProjectV1(doc))
		{
			pages[kv.Key] = kv.Value.ToDict();
		}
		Dictionary<string, object?> envelope = LayoutJson.Map();
		envelope["version"] = PageLayoutDoc.CurrentVersion;
		envelope["kind"] = PageLayoutStore.Kind;
		envelope["pages"] = pages;
		return envelope;
	}

	// ————— 内部实现 —————

	// 测试注入目录时,v1 存储也必须指到同一目录;否则写通会落到用户真实数据目录(危险)
	private static void SyncV1DirOverride()
	{
		string? over = DirOverride;
		if (!string.IsNullOrEmpty(over))
		{
			PageLayoutStore.DirOverride = over;
		}
	}

	private static LayoutV2? ReadV2File(string path)
	{
		try
		{
			if (!File.Exists(path))
			{
				return null;
			}
			return LayoutV2.FromJsonText(File.ReadAllText(path));
		}
		catch
		{
			// 被独占 / 编码坏了 / 没有权限:一律当作"读不到",由 Load 回落
			return null;
		}
	}

	private static bool WriteV2File(LayoutV2 doc)
	{
		try
		{
			Utils.WriteJson(Path.Combine(DirPath, FileName), doc.ToDict());
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool WriteV1Projection(LayoutV2? doc)
	{
		try
		{
			Utils.WriteJson(Path.Combine(DirPath, V1FileName), V1Envelope(doc));
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static Dictionary<string, PageLayoutDoc>? SafeLoadAllV1()
	{
		try
		{
			Dictionary<string, PageLayoutDoc> pages = new Dictionary<string, PageLayoutDoc>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, PageLayoutDoc> kv in PageLayoutStore.LoadAll())
			{
				if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
				{
					pages[kv.Key] = kv.Value;
				}
			}
			return pages;
		}
		catch
		{
			return null;
		}
	}

	private static void RaiseChanged()
	{
		try
		{
			LayoutChanged?.Invoke();
		}
		catch
		{
			// 订阅者自己的异常不能影响保存结果
		}
	}

	private static string Timestamp()
	{
		return DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz");
	}

	private static string AppVersion()
	{
		try
		{
			Version? version = typeof(LayoutStoreV2).Assembly.GetName().Version;
			if (version == null)
			{
				return "";
			}
			if (version.Revision > 0)
			{
				return version.ToString();
			}
			return version.Major + "." + version.Minor + "." + version.Build;
		}
		catch
		{
			return "";
		}
	}
}
