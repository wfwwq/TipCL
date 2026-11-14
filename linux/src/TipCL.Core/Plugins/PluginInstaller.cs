// M4-H P6:插件安装器(文件级)—— 市场安装 / 本地安装 / 开发链接 / 卸载的真实现。
//
// 来源与口径:Windows 参考仓库 src/TipCL/Core/Plugins/PluginMarket.cs 的
// InstallFromMarket + InstallPackage + SwapAndLoad + LinkDev + Uninstall,
// **去掉两步**:npm install(Node 运行时,Linux 不内置,见 docs/PARITY.md §7.2)
// 与 SwapAndLoad 里的宿主加载/重启(Linux 没有插件运行时)。
// 其余步骤逐条对齐:镜像候选下载 → sha256 → 包内清单 id/version 与索引一致 →
// engines → 依赖检查 → 解压到 plugins/.staging/<id> → 旧版本挪 plugins/.backup/<id> →
// 原子替换 → 写 plugins.json → 失败回滚。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TipCL.Core.Launcher;

namespace TipCL.Core.Plugins;

/// <summary>插件文件级安装器。所有方法都会在失败时清理暂存目录,尽量不留半装状态。</summary>
public sealed class PluginInstaller
{
	private readonly PluginDirs _dirs;
	private readonly PluginStateStore _store;
	private readonly Func<string, string> _installedVersionOf;

	public PluginInstaller(PluginDirs dirs, PluginStateStore store, Func<string, string> installedVersionOf = null)
	{
		_dirs = dirs ?? throw new ValueError("缺少插件目录配置");
		_store = store ?? throw new ValueError("缺少插件状态存储");
		_installedVersionOf = installedVersionOf ?? DefaultInstalledVersion;
	}

	private string DefaultInstalledVersion(string id)
	{
		return Py.S(_store.Get(id)?.Version);
	}

	public PluginDirs Dirs => _dirs;

	public PluginStateStore Store => _store;

	/// <summary>市场安装 / 更新。entry 是 <see cref="PluginMarket.Rows"/> 里的一项(含 download.url/sha256)。</summary>
	public PluginManifest InstallFromMarket(PyDict entry, IList<string> granted = null, Action<string> log = null)
	{
		if (entry == null)
		{
			throw new ValueError(I18nCore.Tr("插件市场里没有这个插件"));
		}
		string id = Py.S(entry.Get("id"));
		string version = Py.S(entry.Get("version"));
		bool verified = Py.Truthy(entry.Get("verified"));
		if (!verified && !PluginMarket.AllowUnverified())
		{
			throw new ValueError(I18nCore.Tr("这是未审核的插件。请先在「插件 → 插件设置」里打开「允许安装未审核插件」。"));
		}
		if (entry.Get("download") is not PyDict dl)
		{
			throw new ValueError(I18nCore.Tr("市场索引里没有下载地址:") + id);
		}
		string url = Py.S(dl.Get("url"));
		string sha = Py.S(dl.Get("sha256"));
		if (url.Length == 0 || sha.Length != 64)
		{
			throw new ValueError(I18nCore.Tr("市场索引里的下载信息不完整:") + id);
		}

		Directory.CreateDirectory(_dirs.StagingRoot);
		string zip = Path.Combine(_dirs.StagingRoot, id + "-" + Safe(version) + ".zip");
		try
		{
			log?.Invoke(I18nCore.Tr("下载插件包"));
			DownloadAny(url, zip, log);
			if (!PluginMarket.Sha256Matches(zip, sha))
			{
				throw new ValueError(I18nCore.Tr("插件包校验失败(sha256 不符),已拒绝安装"));
			}
			return InstallPackage(zip, "market", Py.S(entry.Get("market")), id, version, granted, log);
		}
		finally
		{
			TryDeleteFile(zip);
		}
	}

	/// <summary>
	/// 安装一个插件包(zip 文件或已解开的目录)。市场 / 本地安装共用。
	/// expectId / expectVersion 非空时必须与包内清单一致(市场条目的防串包检查)。
	/// </summary>
	public PluginManifest InstallPackage(string package, string source, string market, string expectId, string expectVersion,
		IList<string> granted = null, Action<string> log = null)
	{
		bool isZip = File.Exists(package);
		bool isDir = !isZip && Directory.Exists(package);
		if (!isZip && !isDir)
		{
			throw new ValueError(I18nCore.Tr("找不到插件包:") + package);
		}

		PluginManifest m = isZip && package.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
			? PluginMarket.ReadZipManifest(package).manifest
			: PluginManifest.LoadDir(package);
		CheckManifest(m, expectId, expectVersion);

		Directory.CreateDirectory(_dirs.StagingRoot);
		string staging = Path.Combine(_dirs.StagingRoot, m.Id);
		TryDeleteDir(staging);
		try
		{
			log?.Invoke(I18nCore.Tr("解压到暂存目录"));
			if (isZip)
			{
				string prefix = PluginMarket.ReadZipManifest(package).prefix;
				Directory.CreateDirectory(staging);
				PluginMarket.ExtractWithPrefix(package, prefix, staging);
			}
			else
			{
				CopyDir(package, staging);
			}
			// 包内清单必须与索引一致(防串包 / 防挂羊头)
			PluginManifest inner = PluginManifest.LoadDir(staging);
			CheckManifest(inner, expectId, expectVersion);

			log?.Invoke(I18nCore.Tr("替换已安装版本"));
			Swap(staging, m.Id);
		}
		catch (Exception)
		{
			TryDeleteDir(staging);
			throw;
		}

		_store.Update(m.Id, delegate (PluginRecord r)
		{
			r.Source = source;
			r.Market = market ?? "";
			r.DevPath = "";
			r.Enabled = true;
			r.Version = m.Version;
			r.InstalledAt = NowText();
			if (granted != null && granted.Count > 0)
			{
				r.GrantedPermissions = granted.Distinct().ToList();
			}
		});
		log?.Invoke(I18nCore.Tr("已安装 ") + m.Id + " " + m.Version);
		return m;
	}

	/// <summary>链接开发文件夹(不复制,记 devPath)。</summary>
	public PluginManifest LinkDev(string dir, IList<string> granted = null)
	{
		PluginManifest m = PluginManifest.LoadDir(dir);
		CheckManifest(m, null, null);
		_store.Update(m.Id, delegate (PluginRecord r)
		{
			r.Source = "dev";
			r.DevPath = Path.GetFullPath(dir);
			r.Market = "";
			r.Enabled = true;
			r.Version = m.Version;
			r.InstalledAt = NowText();
			if (granted != null && granted.Count > 0)
			{
				r.GrantedPermissions = granted.Distinct().ToList();
			}
		});
		return m;
	}

	/// <summary>卸载(或取消开发链接)。deleteData 时连 plugin-data/&lt;id&gt; 一起删。</summary>
	public PluginRecord Uninstall(string id, bool deleteData)
	{
		PluginRecord r = _store.Get(id);
		if (r == null && !Directory.Exists(_dirs.PluginDir(id)))
		{
			throw new ValueError(I18nCore.Tr("插件不存在:") + id);
		}
		// 开发链接只解除记录,开发文件夹本身不动(照参考版语义)
		if (r?.Source != "dev")
		{
			TryDeleteDir(_dirs.PluginDir(id));
			TryDeleteDir(Path.Combine(_dirs.BackupRoot, id));
		}
		if (deleteData)
		{
			TryDeleteDir(_dirs.DataDir(id));
			TryDeleteFile(_dirs.LogFile(id));
			TryDeleteFile(_dirs.SecretsFile(id));
		}
		_store.Remove(id);
		return r;
	}

	/// <summary>清单与 engines / 依赖检查(安装前)。</summary>
	public void CheckManifest(PluginManifest m, string expectId, string expectVersion)
	{
		if (m == null)
		{
			throw new ValueError(I18nCore.Tr("插件包里没有清单"));
		}
		List<string> bad = PluginManifest.Validate(m.Raw);
		if (bad.Count > 0)
		{
			throw new ValueError(I18nCore.Tr("插件清单不合法:") + string.Join("; ", bad));
		}
		if (!string.IsNullOrEmpty(expectId) && m.Id != expectId)
		{
			throw new ValueError(I18nCore.Tr("插件包里的 id 与市场条目不一致:") + m.Id + " / " + expectId);
		}
		if (!string.IsNullOrEmpty(expectVersion) && m.Version != expectVersion)
		{
			throw new ValueError(I18nCore.Tr("插件包里的版本与市场条目不一致:") + m.Version + " / " + expectVersion);
		}
		if (!m.EngineOk(AppInfo.APP_VERSION, out string engErr))
		{
			throw new ValueError(engErr ?? I18nCore.Tr("版本不满足插件要求"));
		}
		foreach (KeyValuePair<string, string> d in m.Dependencies)
		{
			string installed = Py.S(_installedVersionOf(d.Key));
			if (installed.Length == 0)
			{
				throw new ValueError(I18nCore.Tr("需要先安装插件 ") + d.Key + "(" + d.Value + ")");
			}
			if (!SemVer.Satisfies(installed, d.Value))
			{
				throw new ValueError(I18nCore.Tr("插件 ") + d.Key + I18nCore.Tr(" 版本不符:需要 ") + d.Value + I18nCore.Tr(",已装 ") + installed);
			}
		}
	}

	/// <summary>暂存目录 → plugins/&lt;id&gt; 的原子替换(旧版本挪 .backup,失败回滚)。</summary>
	private void Swap(string staging, string id)
	{
		string dest = _dirs.PluginDir(id);
		string backup = Path.Combine(_dirs.BackupRoot, id);
		Directory.CreateDirectory(_dirs.BackupRoot);
		TryDeleteDir(backup);
		bool had = Directory.Exists(dest);
		if (had)
		{
			Directory.Move(dest, backup);
		}
		try
		{
			Directory.Move(staging, dest);
		}
		catch (Exception)
		{
			if (had)
			{
				TryDeleteDir(dest);
				Directory.Move(backup, dest);
			}
			throw;
		}
		TryDeleteDir(backup);
	}

	/// <summary>按镜像优先级逐个候选下载;全失败时抛最后一个错。</summary>
	private void DownloadAny(string url, string dest, Action<string> log)
	{
		Exception last = null;
		bool ok = false;
		foreach (string candidate in PluginMarket.DownloadCandidates(url))
		{
			try
			{
				TryDeleteFile(dest);
				log?.Invoke(I18nCore.Tr("下载 ") + candidate);
				using NetResponse r = Net.Get(candidate, 8.0, 300.0);
				r.RaiseForStatus();
				File.WriteAllBytes(dest, r.Content());
				ok = true;
				break;
			}
			catch (Exception ex) when (!(ex is TaskCancelled))
			{
				last = ex;
				log?.Invoke(I18nCore.Tr("下载失败,尝试其它源:") + ex.Message);
			}
		}
		if (!ok)
		{
			throw last ?? new ValueError(I18nCore.Tr("插件包下载失败"));
		}
	}

	private static string Safe(string s)
	{
		return new string((s ?? "").Where((char c) => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
	}

	public static string NowText()
	{
		return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
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

	private static void TryDeleteDir(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch (Exception)
		{
		}
	}

	private static void TryDeleteFile(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception)
		{
		}
	}
}
