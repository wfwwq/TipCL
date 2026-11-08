// 来源: Windows 版参考仓库 src/TipCL/Core\Plugins\PluginMarket.cs（M2-B0 续搬）
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using TipCL.Core.Launcher;

namespace TipCL.Core.Plugins;

/// <summary>
/// 插件市场客户端 + 安装器（DESIGN §9）。
/// 安装流程：下载 → 校验 sha256 → 读 zip 里的清单（id/version 必须和索引一致）→ engines / 依赖检查 → 权限确认 →
/// 解压到 plugins/.staging/&lt;id&gt; → 需要时 npm install → 原子替换 plugins/&lt;id&gt;（旧版本先挪到 .backup）→ 加载。任何一步失败都回滚。
/// </summary>
public sealed class PluginMarket
{
	public const string SettingIndexes = "plugin_market_indexes";

	public const string SettingAllowUnverified = "plugin_allow_unverified";

	/// <summary>加速镜像（照 MaiBot git_mirror_service 的语义）：每个镜像独立 enabled + priority 数字，
	/// raw 与 github 下载前缀分开存（前缀含完整被代理域名，拼接 = prefix + 相对路径）。整份镜像列表持久化在 plugin_mirrors。</summary>
	public const string SettingMirrors = "plugin_mirrors";

	/// <summary>官方市场索引（仓库相对路径见 IndexRepoPath）。</summary>
	public const string OfficialIndexUrl = "https://raw.githubusercontent.com/wfwwq/TipCL-market/main/index.json";

	/// <summary>索引在 raw 域名下的相对路径。</summary>
	public const string IndexRepoPath = "/wfwwq/TipCL-market/main/index.json";

	/// <summary>用户自定义镜像的 id 前缀。</summary>
	public const string CustomMirrorPrefix = "custom-";

	public sealed class GhMirror
	{
		public string Id = "";

		public string Label = "";

		/// <summary>raw 前缀（含被代理域名，如 https://gitproxy.mrhjx.cn/https://raw.githubusercontent.com）。</summary>
		public string RawPrefix = "https://raw.githubusercontent.com";

		/// <summary>github 下载前缀（含被代理域名，如 https://github.com）。</summary>
		public string DownloadPrefix = "https://github.com";

		public bool Enabled = true;

		public int Priority = 999;

		public GhMirror Clone()
		{
			return new GhMirror
			{
				Id = Id,
				Label = Label,
				RawPrefix = RawPrefix,
				DownloadPrefix = DownloadPrefix,
				Enabled = Enabled,
				Priority = Priority
			};
		}
	}

	/// <summary>内置默认镜像（语义对齐 MaiBot DEFAULT_MIRRORS：priority 1..6，全部启用）。</summary>
	public static List<GhMirror> DefaultMirrors()
	{
		string[][] defs = new string[6][]
		{
			new string[4] { "gitproxy-mrhjx", "gitproxy.mrhjx.cn 镜像", "https://gitproxy.mrhjx.cn/https://raw.githubusercontent.com", "https://gitproxy.mrhjx.cn/https://github.com" },
			new string[4] { "ghproxy-vip", "ghproxy.vip 镜像", "https://ghproxy.vip/https://raw.githubusercontent.com", "https://ghproxy.vip/https://github.com" },
			new string[4] { "github", "GitHub 官方源", "https://raw.githubusercontent.com", "https://github.com" },
			new string[4] { "gh-proxy", "gh-proxy.com 镜像", "https://gh-proxy.com/https://raw.githubusercontent.com", "https://gh-proxy.com/https://github.com" },
			new string[4] { "v6-gh-proxy", "v6.gh-proxy.org 镜像", "https://v6.gh-proxy.org/https://raw.githubusercontent.com", "https://v6.gh-proxy.org/https://github.com" },
			new string[4] { "cdn-gh-proxy", "cdn.gh-proxy.com 镜像", "https://cdn.gh-proxy.com/https://raw.githubusercontent.com", "https://cdn.gh-proxy.com/https://github.com" }
		};
		List<GhMirror> list = new List<GhMirror>();
		for (int i = 0; i < defs.Length; i++)
		{
			list.Add(new GhMirror
			{
				Id = defs[i][0],
				Label = defs[i][1],
				RawPrefix = defs[i][2],
				DownloadPrefix = defs[i][3],
				Enabled = true,
				Priority = i + 1
			});
		}
		return list;
	}

	/// <summary>内置默认的索引候选（官方 + 镜像），供旧引用与默认展示。</summary>
	public static readonly string[] DefaultIndexUrls = DefaultMirrors().Select((GhMirror m) => m.RawPrefix + IndexRepoPath).ToArray();

	/// <summary>旧下载镜像列表（纯前缀，保留给老引用；新代码走 DownloadCandidates）。</summary>
	public static readonly string[] GhDownloadMirrors = DefaultMirrors().Where((GhMirror m) => m.DownloadPrefix != "https://github.com").Select((GhMirror m) => m.DownloadPrefix + "/").ToArray();

	/// <summary>从配置对象（List&lt;PyDict&gt; 或缺省）解析镜像列表：enabled 过滤 + priority 排序 + 去重。纯函数供测试。</summary>
	public static List<GhMirror> EnabledMirrors(object? configured)
	{
		List<GhMirror> all = new List<GhMirror>();
		if (configured is IEnumerable<object> items)
		{
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (PyDict item in items.OfType<PyDict>())
			{
				string id = Py.S(item.Get("id")).Trim();
				if (id.Length == 0 || !seen.Add(id))
				{
					continue;
				}
				all.Add(new GhMirror
				{
					Id = id,
					Label = Py.Or(item.Get("name"), id).ToString() ?? id,
					RawPrefix = Py.S(item.Get("raw_prefix")),
					DownloadPrefix = Py.S(item.Get("download_prefix")),
					Enabled = Py.Truthy(item.Get("enabled", true)),
					Priority = (int)Py.Int(Py.Or(item.Get("priority"), 999L))
				});
			}
		}
		else
		{
			all = DefaultMirrors();
		}
		return all.Where((GhMirror m) => m.Enabled && m.RawPrefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && m.DownloadPrefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase)).OrderBy((GhMirror m) => m.Priority).ThenBy((GhMirror m) => m.Id, StringComparer.Ordinal).ToList();
	}

	public static List<GhMirror> EnabledMirrors()
	{
		try
		{
			// 三态：配置缺失 = 内置默认全开；配置存在 = 严格按存储内容（含用户改过的 enabled/priority/自定义镜像）
			object sentinel = new object();
			object v = Config.CONFIG.Get(SettingMirrors, sentinel);
			if (ReferenceEquals(v, sentinel))
			{
				return DefaultMirrors();
			}
			List<GhMirror> parsed = EnabledMirrors(v);
			return (parsed.Count > 0) ? parsed : DefaultMirrors();
		}
		catch (Exception)
		{
			return DefaultMirrors();
		}
	}

	/// <summary>市场索引候选：按启用镜像优先级（官方源也是普通条目，前缀 + 相对路径）。</summary>
	public static List<string> IndexUrls()
	{
		List<string> custom = new List<string>();
		try
		{
			custom = Py.ListOr(Config.CONFIG.Get(SettingIndexes)).Select(Py.S).Select((string s) => s.Trim()).Where((string s) => s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || File.Exists(s)).Distinct().ToList();
		}
		catch (Exception)
		{
		}
		if (custom.Count > 0)
		{
			return custom;
		}
		return EnabledMirrors().Select((GhMirror m) => m.RawPrefix + IndexRepoPath).ToList();
	}

	/// <summary>插件包的下载候选地址：按启用镜像优先级（官方源 = 原始直链），仅对 GitHub 链接套前缀。纯函数版本供测试。</summary>
	/// <summary>下载候选：GitHub raw / release 链接按镜像 priority 逐个换成「下载前缀 + 相对路径」；非 GitHub 直链原样一个候选。纯函数供测试。</summary>
	public static List<string> DownloadCandidates(string url, IEnumerable<GhMirror> mirrors)
	{
		string path = null;
		if (url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
		{
			path = url.Substring("https://github.com".Length);
		}
		else if (url.StartsWith("https://raw.githubusercontent.com/", StringComparison.OrdinalIgnoreCase))
		{
			path = url.Substring("https://raw.githubusercontent.com".Length);
		}
		List<string> list = new List<string>();
		if (path == null)
		{
			list.Add(url);
			return list;
		}
		foreach (GhMirror m in mirrors)
		{
			string prefix = (m.DownloadPrefix.EndsWith("/", StringComparison.Ordinal) ? m.DownloadPrefix : m.DownloadPrefix + "/");
			list.Add(prefix.TrimEnd('/') + path);
		}
		if (list.Count == 0)
		{
			list.Add(url);
		}
		return list.Distinct().ToList();
	}

	public static List<string> DownloadCandidates(string url)
	{
		return DownloadCandidates(url, EnabledMirrors());
	}

	// M2-B0：PluginManager（插件宿主运行时）未随 M2 落地，已安装信息改为注入。
	/// <summary>按 id 查已安装插件版本（没有则空串），由宿主侧注入。</summary>
	public Func<string, string> InstalledVersionOf = (string id) => "";

	private readonly object _cacheLock = new object();

	private List<PyDict> _cache;

	private List<object> _cacheErrors = new List<object>();

	private DateTime _cacheAt = DateTime.MinValue;

	/// <summary>拉取索引（可在测试里替换）。</summary>
	public Func<string, object> FetchJson = (string url) => Net.GetJson(url, 6.0, 30.0);

	/// <summary>构造。M2-B0 起不再依赖 PluginManager；保留无参构造。</summary>
	public PluginMarket()
	{
	}

	/// <summary>权限行（照 Windows 版 PluginManager.PermissionRows 的展示契约）。</summary>
	public static List<object> PermissionRows(IEnumerable<string> permissions)
	{
		return (permissions ?? Array.Empty<string>()).Select((string p) => (object)PyDict.Of("id", p, "label", I18nCore.Tr(PluginPermissions.Describe(p)), "danger", p == PluginPermissions.System)).ToList();
	}

	public static bool AllowUnverified()
	{
		try
		{
			return Py.Truthy(Config.CONFIG.Get(SettingAllowUnverified, false));
		}
		catch (Exception)
		{
			return false;
		}
	}

	/// <summary>
	/// 按顺序合并多个索引：同一个 id 先到先得。每项补上 market（来源索引地址）和 marketName。
	/// 不合法的条目（没有 id、id 不合法、没有下载地址或 sha256）丢弃。纯函数。
	/// </summary>
	public static List<PyDict> MergeIndexes(IEnumerable<(string url, PyDict index)> indexes)
	{
		List<PyDict> r = new List<PyDict>();
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		foreach ((string url, PyDict index) in indexes)
		{
			if (index == null)
			{
				continue;
			}
			string marketName = Py.S(index.Get("name"));
			foreach (PyDict e in Py.ListOr(index.Get("plugins")).OfType<PyDict>())
			{
				string id = Py.S(e.Get("id"));
				if (!PluginManifest.IsValidId(id) || !SemVer.IsValid(Py.S(e.Get("version"))))
				{
					continue;
				}
				PyDict dl = e.Get("download") as PyDict;
				if (dl == null || Py.S(dl.Get("url")).Length == 0 || Py.S(dl.Get("sha256")).Length != 64)
				{
					continue;
				}
				if (!seen.Add(id))
				{
					continue;
				}
				PyDict c = e.Copy();
				c["market"] = url;
				c["marketName"] = marketName;
				c["verified"] = Py.Truthy(e.Get("verified"));
				r.Add(c);
			}
		}
		return r;
	}

	/// <summary>拉全部索引并合并（结果缓存 10 分钟）。</summary>
	public List<PyDict> Entries(bool refresh)
	{
		lock (_cacheLock)
		{
			if (!refresh && _cache != null && DateTime.UtcNow - _cacheAt < TimeSpan.FromMinutes(10.0))
			{
				return _cache;
			}
		}
		List<(string, PyDict)> got = new List<(string, PyDict)>();
		List<object> errors = new List<object>();
		foreach (string url in IndexUrls())
		{
			try
			{
				object j = File.Exists(url) ? PyJson.Loads(File.ReadAllText(url, Encoding.UTF8).TrimStart('\ufeff')) : FetchJson(url);
				if (!(j is PyDict d) || Py.IntOr(d.Get("schema"), 1L) != 1)
				{
					throw new ValueError("索引格式不对（schema 必须是 1）");
				}
				got.Add((url, d));
			}
			catch (Exception ex)
			{
				errors.Add(PyDict.Of("market", url, "error", (ex is PyException) ? Py.ExcStr(ex) : ex.Message));
			}
		}
		List<PyDict> merged = MergeIndexes(got);
		lock (_cacheLock)
		{
			_cache = merged;
			_cacheErrors = errors;
			_cacheAt = DateTime.UtcNow;
		}
		return merged;
	}

	public PyDict Find(string id, bool refresh = false)
	{
		return Entries(refresh).FirstOrDefault((PyDict e) => Py.S(e.Get("id")) == id) ?? ((!refresh) ? Entries(refresh: true).FirstOrDefault((PyDict e) => Py.S(e.Get("id")) == id) : null);
	}

	/// <summary>plugins_market()：合并后的列表，每项加 installed / installedVersion / updatable / compatible。</summary>
	public PyDict Rows(bool refresh)
	{
		List<PyDict> entries = Entries(refresh);
		List<object> rows = new List<object>();
		foreach (PyDict e in entries)
		{
			PyDict row = e.Copy();
			string id = Py.S(e.Get("id"));
			string installedVersion = InstalledVersionOf(id) ?? "";
			row["installed"] = installedVersion.Length > 0;
			row["installedVersion"] = installedVersion;
			row["updatable"] = installedVersion.Length > 0 && SemVer.Compare(Py.S(e.Get("version")), installedVersion) > 0;
			string eng = (e.Get("engines") is PyDict en) ? Py.S(en.Get("tipcl")) : "";
			row["compatible"] = eng.Length == 0 || SemVer.Satisfies(AppInfo.APP_VERSION, eng);
			row["permissionRows"] = PermissionRows(Py.ListOr(e.Get("permissions")).Select(Py.S).Concat((Py.S(e.Get("runtime")) == "dotnet") ? new string[0] : new[] { PluginPermissions.System }).Distinct());
			rows.Add(row);
		}
		List<object> errs;
		lock (_cacheLock)
		{
			errs = _cacheErrors.ToList();
		}
		return PyDict.Of("plugins", rows, "errors", errs, "indexes", IndexUrls().Cast<object>().ToList(), "allowUnverified", AllowUnverified());
	}

	// ───────────────────────────── 安装 ─────────────────────────────

	/// <summary>sha256 校验（大小写不敏感）。</summary>
	public static bool Sha256Matches(string file, string expected)
	{
		if (string.IsNullOrEmpty(expected) || !File.Exists(file))
		{
			return false;
		}
		return string.Equals(Utils.Sha256File(file), expected.Trim(), StringComparison.OrdinalIgnoreCase);
	}

	public sealed class InstallOptions
	{
		/// <summary>market | local | dev</summary>
		public string Source = "local";

		public string Market = "";

		public string ExpectId;

		public string ExpectVersion;

		public bool Verified = true;

		/// <summary>跳过权限确认（测试 / 已确认过）。</summary>
		public bool SkipConfirm;
	}

	/// <summary>从 zip 里找清单：根目录，或者唯一一层子目录里。返回 (清单, 包内前缀)。</summary>
	public static (PluginManifest manifest, string prefix) ReadZipManifest(string zipPath)
	{
		using ZipArchive z = ZipFile.OpenRead(zipPath);
		ZipArchiveEntry hit = z.GetEntry(PluginManifest.FileName);
		string prefix = "";
		if (hit == null)
		{
			List<ZipArchiveEntry> cands = z.Entries.Where((ZipArchiveEntry x) => x.FullName.Replace('\\', '/').Count((char c) => c == '/') == 1 && x.FullName.Replace('\\', '/').EndsWith("/" + PluginManifest.FileName, StringComparison.Ordinal)).ToList();
			if (cands.Count != 1)
			{
				throw new ValueError(I18nCore.Tr("插件包里没有 tipcl-plugin.json"));
			}
			hit = cands[0];
			prefix = hit.FullName.Replace('\\', '/').Substring(0, hit.FullName.Length - PluginManifest.FileName.Length);
		}
		using StreamReader sr = new StreamReader(hit.Open(), Encoding.UTF8);
		return (PluginManifest.Parse(sr.ReadToEnd()), prefix);
	}

	/// <summary>依赖检查：必需依赖必须已安装、版本满足。返回问题列表。
	/// M2-B0：宿主运行时（PluginManager）未随 M2 落地，已安装版本经 InstalledVersionOf 注入。</summary>
	public List<string> CheckDependencies(PluginManifest m)
	{
		List<string> errs = new List<string>();
		foreach (KeyValuePair<string, string> d in m.Dependencies)
		{
			string installed = InstalledVersionOf(d.Key);
			if (installed.Length == 0)
			{
				errs.Add("需要先安装插件 " + d.Key + "（" + d.Value + "）");
			}
			else if (!SemVer.Satisfies(installed, d.Value))
			{
				errs.Add("插件 " + d.Key + " 版本不符：需要 " + d.Value + "，已装 " + installed);
			}
		}
		return errs;
	}

	private static void ExtractWithPrefix(string zipPath, string prefix, string dest)
	{
		string root = Path.GetFullPath(dest).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		using ZipArchive z = ZipFile.OpenRead(zipPath);
		foreach (ZipArchiveEntry e in z.Entries)
		{
			string name = e.FullName.Replace('\\', '/');
			if (!name.StartsWith(prefix, StringComparison.Ordinal))
			{
				continue;
			}
			string rel = name.Substring(prefix.Length);
			if (rel.Length == 0)
			{
				continue;
			}
			string full = Path.GetFullPath(Path.Combine(dest, rel.Replace('/', Path.DirectorySeparatorChar)));
			if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && full.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar != root)
			{
				throw new ValueError("插件包含非法路径：" + name);
			}
			if (((e.ExternalAttributes >> 16) & 0xF000) == 0xA000)
			{
				continue;
			}
			if (name.EndsWith("/", StringComparison.Ordinal))
			{
				Directory.CreateDirectory(full);
				continue;
			}
			Directory.CreateDirectory(Path.GetDirectoryName(full));
			using Stream s = e.Open();
			using FileStream fs = new FileStream(full, FileMode.Create, FileAccess.Write);
			s.CopyTo(fs);
		}
	}

	private static void CopyDir(string src, string dst)
	{
		Directory.CreateDirectory(dst);
		foreach (string f in Directory.GetFiles(src))
		{
			File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
		}
		foreach (string d in Directory.GetDirectories(src))
		{
			string name = Path.GetFileName(d);
			if (name == ".git")
			{
				continue;
			}
			CopyDir(d, Path.Combine(dst, name));
		}
	}
}
