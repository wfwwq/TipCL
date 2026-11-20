// 来源: Windows 版参考仓库 src/TipCL/Core/Launcher/Layout/LayoutStoreV2.cs(分支 layout-shell-hooks @ fa1b3d7,
// 并按 lead 的 Tier-1 冻结裁定取审计修复线 wt-audit-fix@33b5fff 的 L2 版本:`Save` 返回 bool)
using System;
using System.Collections.Generic;
using System.IO;

namespace TipCL.Core.Launcher.Layout;

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

	/// <summary>上次装载时 v2 文件是否损坏(解析失败/不可读);此时 <see cref="LastLoadSource"/> 为 v1-fallback/default。</summary>
	private static bool _lastV2Corrupt;

	/// <summary>上次发现损坏 v2 后真的改名备份成功时的备份路径(空串 = 还没备份/没有)。</summary>
	private static string _lastCorruptBackup = "";

	/// <summary>
	/// 读到损坏 v2 但**读路径故意不改名**(见 <see cref="Load"/>:读不写盘,损坏文件不顺手清掉)时记下的原路径:
	/// 下一次写 v2 前必须先把它改名备份,备份不了就拒绝写,绝不覆盖原件。
	/// </summary>
	private static string _pendingCorruptBackup = "";

	private static readonly object _gate = new object();

	private static LayoutV2? _cache;

	private static string? _cacheDir;

	/// <summary>测试注入临时目录用;null 时用注入缝给的目录,再不行用 Core 自己的回落(Utils 根目录)。</summary>
	public static string? DirOverride { get; set; }

	/// <summary>
	/// v1 投影(<c>ui_page_layouts.json</c>)的**注入缝**:Core 不认识 Pages 的 v1 类型
	/// (<c>PageLayoutDoc</c>/<c>PageLayoutStore</c>),由 Pages 侧在启动时装配(<c>App.OnStartup</c>,
	/// 实现见 <c>Pages/Layout/LayoutV1BridgePages.cs</c>);未装配时用 Core 自带的文件读写(单测走这条)。
	/// 装配后行为与重构前完全一致(目录仍来自 PageLayoutStore.DirPath,v1 仍按 v1 语义读写)。
	/// </summary>
	public static ILayoutV1Bridge? V1Bridge { get; set; }

	/// <summary>上次装载的来源:见 <see cref="LastLoadSource"/>。</summary>
	public static string LastLoadSource { get { return _lastSource; } }

	/// <summary>上次装载时 v2 文件是否损坏(解析失败/不可读)。损坏原件会在下一次写 v2 前先改名备份。</summary>
	public static bool LastLoadV2Corrupt { get { return _lastV2Corrupt; } }

	/// <summary>损坏 v2 的备份文件路径(空串 = 没有/尚未备份成功)。</summary>
	public static string LastCorruptBackup { get { return _lastCorruptBackup; } }

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
				dir = DefaultDir();
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
				string v2Path = Path.Combine(dir, FileName);
				LayoutV2? doc = ReadV2File(v2Path, out bool corrupt);
				if (doc != null)
				{
					_cache = doc;
					_lastSource = "v2";
					_lastCorruptBackup = "";
					_pendingCorruptBackup = "";
				}
				else
				{
					if (corrupt)
					{
						// [移植差异] Windows 版在 Load 里直接改名备份;Linux 侧用例冻结了"读路径不写盘"
						// (LayoutV2CompatTests.CorruptV2FallsBackToV1SubsetWithoutThrowing 断言读完之后
						// 损坏文件内容原样还在),所以这里只记待办,改名备份推迟到下一次写 v2 之前做。
						_pendingCorruptBackup = v2Path;
					}
					IReadOnlyDictionary<string, IReadOnlyList<LayoutV1Block>>? v1 = SafeLoadAllV1(dir);
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
				_lastV2Corrupt = (doc == null && corrupt);
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
			string dir = DirPath;
			string v2Path = Path.Combine(dir, FileName);
			LayoutV2? onDisk = ReadV2File(v2Path, out bool corrupt);
			if (onDisk != null)
			{
				_cache = onDisk;
				_cacheDir = dir;
				_lastSource = "v2";
				_lastV2Corrupt = false;
				_lastCorruptBackup = "";
				_pendingCorruptBackup = "";
				return onDisk.Clone();
			}
			if (corrupt)
			{
				// 同 Load:读路径不改名,交给下一次写 v2 前的备份关卡
				_pendingCorruptBackup = v2Path;
			}
			_lastV2Corrupt = corrupt;
			IReadOnlyDictionary<string, IReadOnlyList<LayoutV1Block>>? v1 = SafeLoadAllV1(dir);
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
				WriteV2File(dir, migrated);
			}
			return migrated.Clone();
		}
	}

	/// <summary>
	/// 保存:先原子写 v2,再**由 v2 单向写通 v1**;两次写都吞异常(写不进去也不能崩 UI),
	/// 内存里始终保留最新值,并触发 <see cref="LayoutChanged"/>。返回 v2 文件是否写成功
	/// (想告诉用户"没存上"的调用方用它;不关心的忽略即可)。
	/// </summary>
	public static bool Save(LayoutV2 doc)
	{
		if (doc == null)
		{
			return false;
		}
		bool written;
		lock (_gate)
		{
			LayoutSchema.Sanitize(doc);
			doc.SavedAt = Timestamp();
			doc.App = AppVersion();
			string dir = DirPath;
			_cache = doc.Clone();
			_cacheDir = dir;
			_lastSource = "v2";
			written = WriteV2File(dir, doc);
			if (written)
			{
				// 只有 v2 落地成功才写通 v1:反过来会让 v1 比 v2 新,违反"v2 是唯一真源"
				WriteV1Projection(dir, doc);
			}
		}
		RaiseChanged();
		return written;
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
			string dir = DirPath;
			WriteV1Projection(dir, Load());
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
			_lastV2Corrupt = false;
			_lastCorruptBackup = "";
			_pendingCorruptBackup = "";
		}
	}

	/// <summary>
	/// 让"仍走 v1 的读取"立刻看到刚写通的投影(转发给注入缝;没装配时是空操作)。
	/// 只在明确时点调用(AI 改布局等),不要塞进 <see cref="Save"/> —— 重构前 Save 不碰 v1 缓存的语义要保住。
	/// </summary>
	public static void RefreshV1Cache()
	{
		try
		{
			V1Bridge?.RefreshV1Cache();
		}
		catch
		{
		}
	}

	/// <summary>
	/// v1 → v2 迁移(§3.3 第一行):每个 v1 page 的 blocks 顺序与 hidden **逐项保留**,
	/// 新增字段一律取默认(mode=stack、cols=12、gap=10、style=plain、title=false...)。
	/// </summary>
	public static LayoutV2 FromV1(IReadOnlyDictionary<string, IReadOnlyList<LayoutV1Block>>? pages)
	{
		LayoutV2 doc = LayoutV2.CreateDefault();
		if (pages != null)
		{
			foreach (KeyValuePair<string, IReadOnlyList<LayoutV1Block>> kv in pages)
			{
				if (string.IsNullOrEmpty(kv.Key) || kv.Value == null || doc.Layout.Pages.ContainsKey(kv.Key))
				{
					continue;
				}
				PageLayoutV2 page = new PageLayoutV2();
				foreach (LayoutV1Block block in kv.Value)
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
				doc.Layout.Pages[kv.Key] = page;
			}
		}
		return LayoutSchema.Sanitize(doc);
	}

	/// <summary>直接从 v1 文件迁移(读不到 = 空文档)。</summary>
	public static LayoutV2 FromV1File()
	{
		string dir = DirPath;
		return FromV1(SafeLoadAllV1(dir));
	}

	/// <summary>
	/// v2 → v1 投影:每页只保留 v1 认识的 {id,hidden}(顺序即顺序,隐藏项也占位)。
	/// 返回的是 Core 自己的朴素结构(v1 的 <c>PageLayoutDoc</c> 在 Pages 里,由 Pages 侧适配)。
	/// </summary>
	public static Dictionary<string, IReadOnlyList<LayoutV1Block>> ProjectV1(LayoutV2? doc)
	{
		Dictionary<string, IReadOnlyList<LayoutV1Block>> pages = new Dictionary<string, IReadOnlyList<LayoutV1Block>>(StringComparer.Ordinal);
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
			List<LayoutV1Block> blocks = new List<LayoutV1Block>();
			if (kv.Value.Blocks != null)
			{
				foreach (BlockLayout block in kv.Value.Blocks)
				{
					if (block != null && !string.IsNullOrEmpty(block.Id))
					{
						blocks.Add(new LayoutV1Block
						{
							Id = block.Id,
							Hidden = block.Hidden
						});
					}
				}
			}
			pages[kv.Key] = blocks;
		}
		return pages;
	}

	/// <summary>
	/// 把"顺序 + 显隐"并进某一页(顺序与显隐以前者为准,同一块在 v2 里的其它字段按 id 保留;
	/// v2 里有、新顺序里没有的块按原顺序接到末尾,不丢数据)。Core 侧共用实现:
	/// AI 工具(LayoutTools)与 Pages 的 <c>LayoutEngine.MergeV1</c> 都走它,保证只有一份语义。
	/// </summary>
	public static PageLayoutV2 MergeV1Order(PageLayoutV2? existing, IEnumerable<LayoutV1Block>? ordered)
	{
		PageLayoutV2 page = ((existing != null) ? existing.Clone() : new PageLayoutV2());
		Dictionary<string, BlockLayout> keep = new Dictionary<string, BlockLayout>(StringComparer.Ordinal);
		List<BlockLayout> source = ((page.Blocks != null) ? page.Blocks : new List<BlockLayout>());
		foreach (BlockLayout block in source)
		{
			if (block != null && !string.IsNullOrEmpty(block.Id) && !keep.ContainsKey(block.Id))
			{
				keep[block.Id] = block;
			}
		}
		List<BlockLayout> merged = new List<BlockLayout>();
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		if (ordered != null)
		{
			foreach (LayoutV1Block block2 in ordered)
			{
				if (block2 == null || string.IsNullOrEmpty(block2.Id) || !seen.Add(block2.Id))
				{
					continue;
				}
				BlockLayout block3 = (keep.TryGetValue(block2.Id, out BlockLayout? found) ? found.Clone() : new BlockLayout
				{
					Id = block2.Id
				});
				block3.Id = block2.Id;
				block3.Hidden = block2.Hidden;
				merged.Add(block3);
			}
		}
		foreach (BlockLayout block4 in source)
		{
			if (block4 != null && !string.IsNullOrEmpty(block4.Id) && seen.Add(block4.Id))
			{
				merged.Add(block4.Clone());
			}
		}
		for (int i = 0; i < merged.Count; i++)
		{
			merged[i].Z = i;
		}
		page.Blocks = merged;
		return page;
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
			throw new InvalidOperationException(I18nCore.Tr("布局文件无效"));
		}
		return doc;
	}

	/// <summary>
	/// 写通 v1 的文件内容:{"version":1,"kind":"tipcl-page-layout","pages":{pageId:{version:1,blocks:[{id,hidden}]}}}。
	/// 字段与顺序都照冻结的 v1 契约写死(Core 不认识 Pages 的 v1 类型,这里是唯一的 Core 侧出口)。
	/// </summary>
	public static Dictionary<string, object?> V1Envelope(IReadOnlyDictionary<string, IReadOnlyList<LayoutV1Block>> pages)
	{
		Dictionary<string, object?> outPages = LayoutJson.Map();
		if (pages != null)
		{
			foreach (KeyValuePair<string, IReadOnlyList<LayoutV1Block>> kv in pages)
			{
				if (string.IsNullOrEmpty(kv.Key) || kv.Value == null)
				{
					continue;
				}
				Dictionary<string, object?> page = LayoutJson.Map();
				page["version"] = LayoutV1Json.PageVersion;
				List<object?> blocks = new List<object?>();
				foreach (LayoutV1Block block in kv.Value)
				{
					if (block == null || string.IsNullOrEmpty(block.Id))
					{
						continue;
					}
					Dictionary<string, object?> item = LayoutJson.Map();
					item["id"] = block.Id;
					item["hidden"] = block.Hidden;
					blocks.Add(item);
				}
				page["blocks"] = blocks;
				outPages[kv.Key] = page;
			}
		}
		Dictionary<string, object?> envelope = LayoutJson.Map();
		envelope["version"] = LayoutV1Json.PageVersion;
		envelope["kind"] = LayoutV1Json.Kind;
		envelope["pages"] = outPages;
		return envelope;
	}

	// ————— 内部实现 —————

	private static LayoutV2? ReadV2File(string path, out bool corrupt)
	{
		corrupt = false;
		try
		{
			if (!File.Exists(path))
			{
				return null;
			}
			string text = File.ReadAllText(path);
			LayoutV2? doc = LayoutV2.FromJsonText(text);
			if (doc == null)
			{
				// 只有"连 JSON 对象都不是 / 文本读坏"才算损坏;合法 JSON 只是 kind/version 门禁不过
				// (例如未来版本写的文件)不算损坏 —— 那种情况既不备份也不覆盖,免得毁掉别的版本的数据
				corrupt = IsDamagedJson(text);
			}
			return doc;
		}
		catch
		{
			// 被独占 / 编码坏了 / 没有权限:一律当作"读不到",由 Load 回落(但要按损坏备份)
			corrupt = true;
			return null;
		}
	}

	// 能否解析成一个 JSON 对象:能 = 只是门禁不过(不备份);不能 = 文本/结构损坏(要备份)
	private static bool IsDamagedJson(string? text)
	{
		try
		{
			// Core 的 JSON 一律走自研 PyJson(见 csproj 注释:不引第三方 JSON 包)
			return !(PyJson.Loads(text ?? "") is PyDict);
		}
		catch
		{
			return true;
		}
	}

	/// <summary>把损坏的 v2 原文件改名备份成 <c>ui_layout_v2.json.corrupt-&lt;时间戳&gt;</c>(同名再追加序号)。</summary>
	private static void BackupCorruptV2(string path, out string backup)
	{
		backup = "";
		if (!File.Exists(path))
		{
			// 原件已经不在了(用户自己删了/别的进程搬走了):待办作废,别把后续保存永久卡死
			_pendingCorruptBackup = "";
			return;
		}
		string stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
		string candidate = path + ".corrupt-" + stamp;
		for (int i = 1; i < 1000 && File.Exists(candidate); i++)
		{
			candidate = path + ".corrupt-" + stamp + "-" + i;
		}
		try
		{
			File.Move(path, candidate);
			backup = candidate;
			_pendingCorruptBackup = "";
		}
		catch
		{
			// 改不了名(被独占等):留着待办,下次写 v2 前必须再试,备份不了就不写(绝不覆盖原件)
			_pendingCorruptBackup = path;
		}
	}

	/// <summary>写 v2 前确保"读失败留下的损坏原件"已经备份;返回 false = 备份不了,此时拒绝写入以免覆盖原件。</summary>
	private static bool EnsureCorruptBackedUp()
	{
		string pending = _pendingCorruptBackup;
		if (pending.Length == 0)
		{
			return true;
		}
		if (File.Exists(pending))
		{
			// 复审补充(第七轮):待办是「读的时候坏的」,但从读到写之间**别的进程可能已经把它写好**。
			// 那种情况下再改名搬走就是把一份好文件变成 .corrupt-*,所以先重判一次还坏不坏。
			ReadV2File(pending, out bool stillDamaged);
			if (!stillDamaged)
			{
				_pendingCorruptBackup = "";
				return true;
			}
		}
		BackupCorruptV2(pending, out string backup);
		if (backup.Length != 0)
		{
			_lastCorruptBackup = backup;
			return true;
		}
		// 原件已经不存在 → BackupCorruptV2 已清掉待办,可以照常写
		return _pendingCorruptBackup.Length == 0;
	}

	private static bool WriteV2File(string dir, LayoutV2 doc)
	{
		try
		{
			if (!EnsureCorruptBackedUp())
			{
				// 上一次读到的损坏 v2 还没备份成功:宁可这次不写,也不能把它覆盖掉
				return false;
			}
			Utils.WriteJson(Path.Combine(dir, FileName), doc.ToDict());
			return true;
		}
		catch
		{
			return false;
		}
	}

	// 写通 v1:优先走注入缝(Pages 侧,能顺带刷新 PageLayoutStore 的缓存),否则 Core 直接原子写文件
	private static bool WriteV1Projection(string dir, LayoutV2? doc)
	{
		try
		{
			Dictionary<string, IReadOnlyList<LayoutV1Block>> pages = ProjectV1(doc);
			ILayoutV1Bridge? bridge = V1Bridge;
			if (bridge != null)
			{
				bridge.SaveV1(dir, pages);
				return true;
			}
			Utils.WriteJson(Path.Combine(dir, V1FileName), V1Envelope(pages));
			return true;
		}
		catch
		{
			return false;
		}
	}

	// 读 v1 投影:注入缝优先,否则 Core 自己解析文件(单测走这条)
	private static IReadOnlyDictionary<string, IReadOnlyList<LayoutV1Block>>? SafeLoadAllV1(string dir)
	{
		try
		{
			ILayoutV1Bridge? bridge = V1Bridge;
			if (bridge != null)
			{
				return bridge.LoadV1(dir);
			}
			return LayoutV1Json.ReadPages(Path.Combine(dir, V1FileName));
		}
		catch
		{
			return null;
		}
	}

	// 数据目录的 Core 兜底:注入缝(装配后由 Pages 提供,= PageLayoutStore.DirPath)→ Utils 根目录 → 程序目录
	private static string DefaultDir()
	{
		ILayoutV1Bridge? bridge = V1Bridge;
		if (bridge != null)
		{
			try
			{
				string? fromBridge = bridge.Dir();
				if (!string.IsNullOrEmpty(fromBridge))
				{
					return fromBridge;
				}
			}
			catch
			{
			}
		}
		try
		{
			string root = Utils.ResolveRoot();
			if (!string.IsNullOrEmpty(root))
			{
				return root;
			}
		}
		catch
		{
		}
		return AppContext.BaseDirectory;
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
