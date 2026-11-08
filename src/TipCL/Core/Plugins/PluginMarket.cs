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

	/// <summary>加速镜像的优先级与开关（有序 id 列表：在列表里 = 开，位置 = 优先级；缺省 = 内置默认顺序全开）。</summary>
	public const string SettingMirrors = "plugin_mirrors";

	/// <summary>官方市场索引。用户未配置时内置使用，并附带几个加速镜像（同一索引，按 id 合并去重）。</summary>
	public const string OfficialIndexUrl = "https://raw.githubusercontent.com/wfwwq/TipCL-market/main/index.json";

	/// <summary>GitHub 加速镜像（前缀代理，同时代理 raw 索引和 github.com 直链）。顺序即默认优先级。</summary>
	public sealed class GhMirror
	{
		public string Id = "";

		public string Label = "";

		/// <summary>前缀代理地址；空串 = GitHub 官方直连。</summary>
		public string Prefix = "";
	}

	public static readonly GhMirror[] BuiltinMirrors = new GhMirror[6]
	{
		new GhMirror
		{
			Id = "gitproxy-mrhjx",
			Label = "gitproxy.mrhjx.cn 镜像",
			Prefix = "https://gitproxy.mrhjx.cn/"
		},
		new GhMirror
		{
			Id = "ghproxy-vip",
			Label = "ghproxy.vip 镜像",
			Prefix = "https://ghproxy.vip/"
		},
		new GhMirror
		{
			Id = "github",
			Label = "GitHub 官方源",
			Prefix = ""
		},
		new GhMirror
		{
			Id = "gh-proxy",
			Label = "gh-proxy.com 镜像",
			Prefix = "https://gh-proxy.com/"
		},
		new GhMirror
		{
			Id = "v6-gh-proxy",
			Label = "v6.gh-proxy.org 镜像",
			Prefix = "https://v6.gh-proxy.org/"
		},
		new GhMirror
		{
			Id = "cdn-gh-proxy",
			Label = "cdn.gh-proxy.com 镜像",
			Prefix = "https://cdn.gh-proxy.com/"
		}
	};

	/// <summary>内置默认（全部启用、按内置顺序）的索引候选，供旧引用与默认展示。</summary>
	public static readonly string[] DefaultIndexUrls = BuiltinMirrors.Select((GhMirror m) => IndexUrlOf(m)).ToArray();

	/// <summary>旧下载镜像列表（纯前缀，保留给老引用；新代码走 DownloadCandidates）。</summary>
	public static readonly string[] GhDownloadMirrors = BuiltinMirrors.Where((GhMirror m) => m.Prefix.Length > 0).Select((GhMirror m) => m.Prefix).ToArray();

	private static string IndexUrlOf(GhMirror m)
	{
		return (m.Prefix.Length == 0) ? OfficialIndexUrl : (m.Prefix + OfficialIndexUrl);
	}

	/// <summary>启用中的镜像，严格按配置顺序；不在列表里的 = 关闭，未知 id 丢弃。纯函数版本供测试。</summary>
	public static List<GhMirror> EnabledMirrors(object? configured)
	{
		List<GhMirror> result = new List<GhMirror>();
		if (configured is IEnumerable<object> ids)
		{
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (string id in ids.Select(Py.S).Select((string s) => s.Trim()))
			{
				GhMirror? m = BuiltinMirrors.FirstOrDefault((GhMirror b) => b.Id == id);
				if (m != null && seen.Add(m.Id))
				{
					result.Add(m);
				}
			}
		}
		return result;
	}

	public static List<GhMirror> EnabledMirrors()
	{
		try
		{
			// 三态：配置缺失 = 内置默认全开；配置存在（哪怕空列表）= 严格按列表（不在 = 关）
			object sentinel = new object();
			object v = Config.CONFIG.Get(SettingMirrors, sentinel);
			if (ReferenceEquals(v, sentinel))
			{
				return new List<GhMirror>(BuiltinMirrors);
			}
			return EnabledMirrors(v);
		}
		catch (Exception)
		{
			return new List<GhMirror>(BuiltinMirrors);
		}
	}

	/// <summary>市场索引候选：按启用镜像优先级（官方源 = 无前缀直连）。</summary>
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
		return EnabledMirrors().Select(IndexUrlOf).ToList();
	}

	/// <summary>插件包的下载候选地址：按启用镜像优先级（官方源 = 原始直链），仅对 GitHub 链接套前缀。纯函数版本供测试。</summary>
	public static List<string> DownloadCandidates(string url, IEnumerable<GhMirror> mirrors)
	{
		bool gh = url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://raw.githubusercontent.com/", StringComparison.OrdinalIgnoreCase);
		List<string> list = new List<string>();
		foreach (GhMirror m in mirrors)
		{
			if (m.Prefix.Length == 0)
			{
				list.Add(url);
			}
			else if (gh)
			{
				list.Add(m.Prefix + url);
			}
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

	private readonly PluginManager _mgr;

	private readonly object _cacheLock = new object();

	private List<PyDict> _cache;

	private List<object> _cacheErrors = new List<object>();

	private DateTime _cacheAt = DateTime.MinValue;

	/// <summary>拉取索引（可在测试里替换）。</summary>
	public Func<string, object> FetchJson = (string url) => Net.GetJson(url, 6.0, 30.0);

	public PluginMarket(PluginManager mgr)
	{
		_mgr = mgr;
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
			PluginInstance p = _mgr.Get(id);
			PluginRecord rec = _mgr.State.Get(id);
			string installedVersion = p?.Manifest?.Version ?? rec?.Version ?? "";
			row["installed"] = p != null;
			row["installedVersion"] = installedVersion;
			row["updatable"] = p != null && rec?.Source != "dev" && installedVersion.Length > 0 && SemVer.Compare(Py.S(e.Get("version")), installedVersion) > 0;
			string eng = (e.Get("engines") is PyDict en) ? Py.S(en.Get("tipcl")) : "";
			row["compatible"] = eng.Length == 0 || SemVer.Satisfies(PluginManager.TipclVersion, eng);
			row["permissionRows"] = PluginManager.PermissionRows(Py.ListOr(e.Get("permissions")).Select(Py.S).Concat((Py.S(e.Get("runtime")) == "dotnet") ? new string[0] : new[] { PluginPermissions.System }).Distinct());
			rows.Add(row);
		}
		List<object> errs;
		lock (_cacheLock)
		{
			errs = _cacheErrors.ToList();
		}
		return PyDict.Of("plugins", rows, "errors", errs, "indexes", IndexUrls().Cast<object>().ToList(), "allowUnverified", AllowUnverified());
	}

	/// <summary>检查更新：给有新版本的插件标上 UpdateAvailable。</summary>
	public void CheckUpdates(bool refresh)
	{
		if (IndexUrls().Count == 0)
		{
			return;
		}
		List<PyDict> entries = Entries(refresh);
		bool changed = false;
		foreach (PluginInstance p in _mgr.Snapshot())
		{
			PluginRecord rec = _mgr.State.Get(p.Id);
			if (rec == null || rec.Source == "dev" || p.Manifest == null)
			{
				continue;
			}
			PyDict e = entries.FirstOrDefault((PyDict x) => Py.S(x.Get("id")) == p.Id);
			string upd = (e != null && SemVer.Compare(Py.S(e.Get("version")), p.Manifest.Version) > 0) ? Py.S(e.Get("version")) : "";
			if (upd != (p.UpdateAvailable ?? ""))
			{
				p.UpdateAvailable = upd;
				changed = true;
			}
		}
		if (changed)
		{
			_mgr.EmitChanged();
		}
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

	/// <summary>从市场安装/更新（在后台任务里调用）。</summary>
	public string InstallFromMarket(TaskContext ctx, string id)
	{
		ctx?.Progress(0L, 5L, I18nCore.Tr("读取插件市场"));
		PyDict e = Find(id, refresh: false) ?? throw new ValueError(I18nCore.Tr("插件市场里没有这个插件：") + id);
		bool verified = Py.Truthy(e.Get("verified"));
		if (!verified && !AllowUnverified())
		{
			throw new ValueError(I18nCore.Tr("这是未审核的插件。请先在设置里打开「允许安装未审核插件」。"));
		}
		PyDict dl = (PyDict)e["download"];
		string sha = Py.S(dl.Get("sha256")).ToLowerInvariant();
		string zip = Path.Combine(_mgr.Dirs.StagingRoot, id + "-" + Py.S(e.Get("version")) + ".zip");
		Directory.CreateDirectory(_mgr.Dirs.StagingRoot);
		ctx?.Progress(1L, 5L, I18nCore.Tr("下载插件"));
		List<string> candidates = DownloadCandidates(Py.S(dl.Get("url")));
		try
		{
			Exception lastErr = null;
			bool downloaded = false;
			foreach (string candidate in candidates)
			{
				try
				{
					try
					{
						File.Delete(zip);
					}
					catch
					{
					}
					ctx?.Log("下载 " + candidate);
					if (_mgr.Backend != null && ctx != null)
					{
						DownloadManager dm = _mgr.Backend._dm(ctx);
						long? size = (dl.Get("size") != null) ? new long?(Py.IntOr(dl.Get("size"), 0L)) : null;
						if (size == 0)
						{
							size = null;
						}
						dm.Download(candidate, zip, null, size, force: true, null, null, sha, null, expand: false);
					}
					else
					{
						using NetResponse r = Net.Get(candidate, 8.0, 300.0);
						r.RaiseForStatus();
						File.WriteAllBytes(zip, r.Content());
					}
					downloaded = true;
					break;
				}
				catch (Exception ex) when (!(ex is TaskCancelled))
				{
					lastErr = ex;
					ctx?.Log(I18nCore.Tr("下载失败，尝试其它源：") + ex.Message);
				}
			}
			if (!downloaded)
			{
				throw lastErr ?? new ValueError(I18nCore.Tr("插件包下载失败"));
			}
			if (!Sha256Matches(zip, sha))
			{
				throw new ValueError(I18nCore.Tr("插件包校验失败（sha256 不符），已拒绝安装"));
			}
			return InstallPackage(ctx, zip, new InstallOptions
			{
				Source = "market",
				Market = Py.S(e.Get("market")),
				ExpectId = id,
				ExpectVersion = Py.S(e.Get("version")),
				Verified = verified
			});
		}
		finally
		{
			try
			{
				File.Delete(zip);
			}
			catch (Exception)
			{
			}
		}
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

	/// <summary>依赖检查：必需依赖必须已安装、版本满足。返回问题列表。</summary>
	public List<string> CheckDependencies(PluginManifest m)
	{
		List<string> errs = new List<string>();
		foreach (KeyValuePair<string, string> d in m.Dependencies)
		{
			PluginInstance p = _mgr.Get(d.Key);
			if (p?.Manifest == null)
			{
				errs.Add("需要先安装插件 " + d.Key + "（" + d.Value + "）");
			}
			else if (!SemVer.Satisfies(p.Manifest.Version, d.Value))
			{
				errs.Add("插件 " + d.Key + " 版本不符：需要 " + d.Value + "，已装 " + p.Manifest.Version);
			}
		}
		return errs;
	}

	/// <summary>
	/// 安装一个包（zip 文件或文件夹）。返回给任务显示的结果文字。失败抛错并回滚。
	/// </summary>
	public string InstallPackage(TaskContext ctx, string package, InstallOptions opt)
	{
		bool isZip = File.Exists(package);
		PluginManifest m = isZip ? ReadZipManifest(package).manifest : PluginManifest.LoadDir(package);
		string prefix = isZip ? ReadZipManifest(package).prefix : "";
		if (opt.ExpectId != null && m.Id != opt.ExpectId)
		{
			throw new ValueError("插件包的 id（" + m.Id + "）和市场里的（" + opt.ExpectId + "）不一致");
		}
		if (opt.ExpectVersion != null && SemVer.Compare(m.Version, opt.ExpectVersion) != 0)
		{
			throw new ValueError("插件包的版本（" + m.Version + "）和市场里的（" + opt.ExpectVersion + "）不一致");
		}
		if (!m.EngineOk(PluginManager.TipclVersion, out string engErr))
		{
			throw new ValueError(engErr);
		}
		List<string> depErrs = CheckDependencies(m);
		if (depErrs.Count > 0)
		{
			throw new ValueError(string.Join("；", depErrs));
		}
		PluginRecord old = _mgr.State.Get(m.Id);
		PluginInstance oldInst = _mgr.Get(m.Id);
		bool isUpdate = oldInst?.Manifest != null;
		List<string> granted = old?.GrantedPermissions ?? new List<string>();
		List<string> newPerms = m.Permissions.Except(granted).ToList();
		if (!opt.SkipConfirm && (newPerms.Count > 0 || !isUpdate))
		{
			ctx?.Progress(2L, 5L, I18nCore.Tr("等待确认权限"));
			string action = isUpdate ? "update" : "install";
			PyDict extra = PyDict.Of("name", m.Name, "version", m.Version, "author", m.Author, "runtime", m.Runtime, "source", opt.Source);
			if (!_mgr.ConfirmPermissions(m.Id, m.Permissions, newPerms, opt.Verified, action, extra))
			{
				throw new TaskCancelled();
			}
		}
		if (opt.Source == "dev")
		{
			return LinkDev(package, m, granted);
		}
		ctx?.Progress(3L, 5L, I18nCore.Tr("解压插件"));
		string staging = Path.Combine(_mgr.Dirs.StagingRoot, m.Id);
		if (Directory.Exists(staging))
		{
			Utils.RemoveTree(staging);
		}
		Directory.CreateDirectory(staging);
		try
		{
			if (isZip)
			{
				ExtractWithPrefix(package, prefix, staging);
			}
			else
			{
				CopyDir(package, staging);
			}
			// 再读一次解出来的清单，确保和校验的一致
			PluginManifest check = PluginManifest.LoadDir(staging);
			if (check.Id != m.Id || check.Version != m.Version)
			{
				throw new ValueError("解压后的清单不一致");
			}
			if (m.NpmInstall)
			{
				ctx?.Progress(4L, 5L, I18nCore.Tr("安装依赖（npm）"));
				NodeRuntime.NodeInfo node = NodeRuntime.Locate(_mgr.Dirs);
				if (node == null)
				{
					if (_mgr.Backend == null || ctx == null)
					{
						throw new RuntimeError("需要 Node " + NodeRuntime.MinMajor + "+ 才能安装依赖");
					}
					node = NodeRuntime.Download(_mgr.Dirs, _mgr.Backend._dm(ctx), delegate(string s)
					{
						ctx.Log(s);
					});
				}
				NodeRuntime.NpmInstall(node.Path, staging, string.IsNullOrEmpty(m.NpmRegistry) ? "https://registry.npmmirror.com" : m.NpmRegistry, delegate(string s)
				{
					ctx?.Log(s);
				}, ctx?.Token ?? default(System.Threading.CancellationToken));
			}
		}
		catch (Exception)
		{
			try
			{
				Utils.RemoveTree(staging);
			}
			catch (Exception)
			{
			}
			throw;
		}
		ctx?.Progress(5L, 5L, I18nCore.Tr("替换并加载"));
		return SwapAndLoad(m, staging, opt, granted, isUpdate);
	}

	private string LinkDev(string dir, PluginManifest m, List<string> granted)
	{
		string full = Path.GetFullPath(dir);
		PluginInstance existing = _mgr.Get(m.Id);
		if (existing != null)
		{
			_mgr.Unload(m.Id);
			_mgr.RemoveInstance(m.Id);
		}
		_mgr.State.Update(m.Id, delegate(PluginRecord r)
		{
			r.Enabled = true;
			r.Version = m.Version;
			r.Source = "dev";
			r.DevPath = full;
			r.Market = "";
			r.InstalledAt = PluginManager.NowText();
			r.GrantedPermissions = m.Permissions.Union(granted).Distinct().ToList();
		});
		_mgr.Discover();
		_mgr.LoadEnabled();
		return I18nCore.Tr("已链接开发目录：") + m.Name + " " + m.Version;
	}

	private string SwapAndLoad(PluginManifest m, string staging, InstallOptions opt, List<string> granted, bool isUpdate)
	{
		string target = _mgr.Dirs.PluginDir(m.Id);
		string backup = Path.Combine(_mgr.Dirs.BackupRoot, m.Id);
		PluginRecord oldRec = _mgr.State.Get(m.Id);
		PyDict oldRecDict = oldRec?.ToDict();
		PluginInstance inst = _mgr.Get(m.Id);
		if (inst != null && inst.IsDotnet && inst.DotNetLoaded)
		{
			// 程序集被占用：放到 .pending，下次启动时替换
			string pending = Path.Combine(_mgr.Dirs.PluginsRoot, ".pending", m.Id);
			if (Directory.Exists(pending))
			{
				Utils.RemoveTree(pending);
			}
			Directory.CreateDirectory(Path.GetDirectoryName(pending));
			Directory.Move(staging, pending);
			_mgr.State.Update(m.Id, delegate(PluginRecord r)
			{
				r.GrantedPermissions = m.Permissions.Union(granted).Distinct().ToList();
				r.Market = opt.Market ?? "";
				r.Source = opt.Source;
			});
			_mgr.SetState(inst, "restart_required", "更新已下载，重启 TipCL 后生效");
			return I18nCore.Tr("已下载，重启 TipCL 后生效");
		}
		if (inst != null && inst.IsNode && (inst.State == "running" || inst.State == "loading"))
		{
			_mgr.Unload(m.Id);
		}
		if (Directory.Exists(backup))
		{
			Utils.RemoveTree(backup);
		}
		Directory.CreateDirectory(_mgr.Dirs.BackupRoot);
		bool movedOld = false;
		try
		{
			if (Directory.Exists(target))
			{
				Directory.Move(target, backup);
				movedOld = true;
			}
			Directory.Move(staging, target);
		}
		catch (Exception)
		{
			if (movedOld && !Directory.Exists(target))
			{
				try
				{
					Directory.Move(backup, target);
				}
				catch (Exception)
				{
				}
			}
			throw;
		}
		_mgr.State.Update(m.Id, delegate(PluginRecord r)
		{
			r.Enabled = true;
			r.Version = m.Version;
			r.Source = opt.Source;
			r.DevPath = "";
			r.Market = opt.Market ?? "";
			r.InstalledAt = PluginManager.NowText();
			r.GrantedPermissions = m.Permissions.Union(granted).Distinct().ToList();
		});
		_mgr.Discover();
		_mgr.LoadEnabled();
		PluginInstance now = _mgr.Get(m.Id);
		if (now != null && now.State == "error" && movedOld)
		{
			// 新版本加载失败：回滚到旧版本
			string err = now.Error;
			_mgr.Unload(m.Id);
			try
			{
				Utils.RemoveTree(target);
				Directory.Move(backup, target);
				if (oldRecDict != null)
				{
					PluginRecord restored = PluginRecord.FromDict(m.Id, oldRecDict);
					_mgr.State.Update(m.Id, delegate(PluginRecord r)
					{
						r.Version = restored.Version;
						r.Source = restored.Source;
						r.Market = restored.Market;
						r.GrantedPermissions = restored.GrantedPermissions;
						r.Enabled = restored.Enabled;
					});
				}
				_mgr.Discover();
				_mgr.LoadEnabled();
			}
			catch (Exception)
			{
			}
			throw new RuntimeError("新版本加载失败，已回滚到旧版本：" + err);
		}
		if (Directory.Exists(backup))
		{
			try
			{
				Utils.RemoveTree(backup);
			}
			catch (Exception)
			{
			}
		}
		if (now != null)
		{
			now.UpdateAvailable = "";
		}
		_mgr.EmitChanged();
		return (isUpdate ? I18nCore.Tr("已更新：") : I18nCore.Tr("已安装：")) + m.Name + " " + m.Version;
	}

	/// <summary>启动时：把 .pending 里等待替换的 .NET 插件换上去。</summary>
	public void ApplyPending()
	{
		string root = Path.Combine(_mgr.Dirs.PluginsRoot, ".pending");
		if (!Directory.Exists(root))
		{
			return;
		}
		foreach (string d in Directory.GetDirectories(root))
		{
			string id = Path.GetFileName(d);
			if (!PluginManifest.IsValidId(id))
			{
				continue;
			}
			try
			{
				PluginManifest m = PluginManifest.LoadDir(d);
				string target = _mgr.Dirs.PluginDir(id);
				if (Directory.Exists(target))
				{
					Utils.RemoveTree(target);
				}
				Directory.Move(d, target);
				_mgr.State.Update(id, delegate(PluginRecord r)
				{
					r.Version = m.Version;
					r.InstalledAt = PluginManager.NowText();
				});
			}
			catch (Exception ex)
			{
				_mgr.LogHost("替换待更新插件 " + id + " 失败：" + ex.Message);
			}
		}
	}

	/// <summary>卸载：先停，再删目录（开发者链接只解除链接）；delete_data 时同时删数据和凭据。</summary>
	public PyDict Uninstall(string id, bool deleteData)
	{
		PluginInstance p = _mgr.Require(id);
		PluginRecord r = _mgr.State.Get(id);
		bool restart = p.IsDotnet && p.DotNetLoaded;
		_mgr.Unload(id);
		_mgr.ClearRegistrations(id, keepManifest: false);
		bool isDev = r != null && r.Source == "dev";
		if (!isDev)
		{
			string dir = _mgr.Dirs.PluginDir(id);
			if (Directory.Exists(dir))
			{
				try
				{
					Utils.RemoveTree(dir);
				}
				catch (Exception)
				{
					if (!restart)
					{
						throw;
					}
					// .NET 程序集被占用：记一个待删，下次启动删
					try
					{
						File.WriteAllText(Path.Combine(dir, ".tipcl-uninstall"), "1");
					}
					catch (Exception)
					{
					}
				}
			}
		}
		if (deleteData)
		{
			try
			{
				string data = _mgr.Dirs.DataDir(id);
				if (Directory.Exists(data))
				{
					Utils.RemoveTree(data);
				}
			}
			catch (Exception)
			{
			}
			p.Secrets?.DeleteAll();
		}
		_mgr.State.Remove(id);
		_mgr.RemoveInstance(id);
		_mgr.EmitChanged();
		_mgr.EmitUiChanged();
		return PyDict.Of("ok", true, "restartRequired", restart);
	}

	/// <summary>启动时删掉标记了待删的目录。</summary>
	public void CleanupUninstalled()
	{
		if (!Directory.Exists(_mgr.Dirs.PluginsRoot))
		{
			return;
		}
		foreach (string d in Directory.GetDirectories(_mgr.Dirs.PluginsRoot))
		{
			if (File.Exists(Path.Combine(d, ".tipcl-uninstall")))
			{
				try
				{
					Utils.RemoveTree(d);
				}
				catch (Exception)
				{
				}
			}
		}
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
