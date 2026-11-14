using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TipCL.Core.Launcher;

public static class Updater
{
	public const string DEFAULT_URL = "https://raw.githubusercontent.com/wfwwq/TipCL/main/update-linux.json";

	private static readonly Regex _SHA256_RE = new Regex("\\A[0-9a-fA-F]{64}\\z", RegexOptions.CultureInvariant);

	public static List<long> _parse(object ver)
	{
		List<long> list = new List<long>();
		string[] array = Py.Str(Py.Truthy(ver) ? ver : "0").Replace("-", ".").Split('.');
		foreach (string obj in array)
		{
			StringBuilder stringBuilder = new StringBuilder();
			string text = obj;
			foreach (char c in text)
			{
				if (char.IsDigit(c))
				{
					stringBuilder.Append((char)(48 + (int)char.GetNumericValue(c)));
				}
			}
			list.Add((stringBuilder.Length > 0) ? long.Parse(stringBuilder.ToString()) : 0);
		}
		while (list.Count < 3)
		{
			list.Add(0L);
		}
		return list.Take(4).ToList();
	}

	private static int CmpTuple(List<long> a, List<long> b)
	{
		for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
		{
			if (a[i] != b[i])
			{
				if (a[i] >= b[i])
				{
					return 1;
				}
				return -1;
			}
		}
		return a.Count.CompareTo(b.Count);
	}

	public static bool Newer(object remote, string local = AppInfo.APP_VERSION)
	{
		return CmpTuple(_parse(remote), _parse(local)) > 0;
	}

	public static string ManifestUrl()
	{
		return Config.UpdateUrl();
	}

	public static bool ValidSha256(object value)
	{
		return _SHA256_RE.IsMatch(Py.Strip(Py.S(value)));
	}

	private static object G(object data, string k)
	{
		if (!Py.Truthy(data))
		{
			return null;
		}
		if (data is PyDict pyDict)
		{
			return pyDict[k];
		}
		throw new AttributeError("'" + Py.TypeName(data) + "' object has no attribute 'get'");
	}

	public static PyDict Check(DownloadManager dm = null)
	{
		string url = ManifestUrl();
		dm = dm ?? new DownloadManager(2L);
		object data;
		try
		{
			data = (JsonFetcher != null) ? JsonFetcher(url) : dm.FetchJson(url, 12L);
		}
		catch (Exception e)
		{
			return PyDict.Of("ok", false, "current", AppInfo.APP_VERSION, "latest", AppInfo.APP_VERSION, "has_update", false, "message", "检查更新失败: " + Py.ExcStr(e), "notes", "", "url", "");
		}
		string text = Py.Str(Py.Or(G(data, "version"), G(data, "latest"), ""));
		bool flag = text.Length > 0 && Newer(text);
		string text2 = Py.Lower(Py.Strip(Py.Str(Py.Or(G(data, "sha256"), ""))));
		bool flag2 = flag && ValidSha256(text2);
		bool flag3 = flag && !flag2;
		return PyDict.Of("ok", !flag3, "current", AppInfo.APP_VERSION, "latest", (text.Length > 0) ? text : AppInfo.APP_VERSION, "has_update", flag2, "message", flag3 ? "更新清单缺少有效 SHA-256，已拒绝自动更新" : (flag ? ("发现 " + text) : "已是最新版本"), "notes", Py.Str(Py.Or(G(data, "notes"), G(data, "changelog"), "")), "url", Py.Str(Py.Or(G(data, "url"), G(data, "download"), "")), "sha256", text2);
	}

	/// <summary>取清单的注入点(测试替身;为空时走真实 HTTP)。与仓库既有的 News.CachePathOverride / MdView.UrlOpener 同一种做法。</summary>
	internal static Func<string, object?>? JsonFetcher { get; set; }

	/// <summary>自身目标的注入点(测试用;为空时按运行形态判定)。</summary>
	internal static string TargetOverride { get; set; } = "";

	/// <summary>
	/// 要接管的文件:**AppImage 走 `$APPIMAGE`** —— 那是磁盘上那个 `.AppImage` 本身,
	/// 不是运行时解包出来的 `/tmp/.mount_*` 挂载路径(用后者替换等于往临时目录写,毫无意义)。
	/// 其它形态(自包含单文件、直接跑的二进制)取进程自己的可执行路径。
	/// </summary>
	public static string UpdateTarget()
	{
		if (TargetOverride.Length > 0)
		{
			return TargetOverride;
		}
		string appImage = (System.Environment.GetEnvironmentVariable("APPIMAGE") ?? "").Trim();
		if (appImage.Length > 0 && File.Exists(appImage))
		{
			return appImage;
		}
		string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
		// 运行中的二进制被删过(常见于"先删后换"的自更新),此时路径已不可用
		return (exe.EndsWith("(deleted)", StringComparison.Ordinal) || !File.Exists(exe)) ? "" : exe;
	}

	/// <summary>当前是不是 AppImage 形态运行(用于给用户一句准确的提示)。</summary>
	public static bool IsAppImage()
	{
		string appImage = (System.Environment.GetEnvironmentVariable("APPIMAGE") ?? "").Trim();
		return appImage.Length > 0 && File.Exists(appImage);
	}

	/// <summary>
	/// 能不能安全替换:替换是**同目录改名**(rename),所以要的是**目标目录可写**,不是目标文件可写。
	/// 用"在目标目录建一个探针文件再删掉"来实测 —— 比猜权限位可靠(`/opt/TipCL` 就是典型的 root 属主目录)。
	/// </summary>
	public static bool CanReplace(string target)
	{
		try
		{
			if (target.Length == 0 || !File.Exists(target))
			{
				return false;
			}
			string dir = System.IO.Path.GetDirectoryName(target) ?? "";
			if (dir.Length == 0)
			{
				return false;
			}
			string probe = System.IO.Path.Combine(dir, ".tipcl-write-probe-" + Guid.NewGuid().ToString("N").Substring(0, 8));
			File.WriteAllText(probe, "probe");
			File.Delete(probe);
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	/// <summary>
	/// 装更新(Linux 版:**当场替换**,不像 Windows 那样等进程退出再用 PowerShell 换 exe)。
	///
	/// 为什么 Linux 可以当场换:运行中的可执行文件在 Linux 上被换掉是安全的 ——
	/// 进程继续用旧 inode,新文件占的是目录项;AppImage 更宽松(运行时用的是解包挂载,磁盘上那个文件没人占)。
	/// 所以这里下载包 → 同目录写 `*.new` → **原子 rename 覆盖** → 提示重启(或直接重启)。
	///
	/// 目标目录不可写(典型:`.deb` 装到 `/opt/TipCL`)时**不硬来**:返回 `mode=download-only`,
	/// 把包留在下载目录并告诉用户该怎么装完 —— 系统包管理器管的东西不该被应用偷偷替换。
	/// </summary>
	public static PyDict ApplyStaged(string package, bool restart)
	{
		string pkg = Utils.AbsPath(package);
		if (!File.Exists(pkg))
		{
			throw new RuntimeError("更新包不存在: " + pkg);
		}
		string target = UpdateTarget();
		if (target.Length == 0)
		{
			throw new RuntimeError("找不到启动器自身路径");
		}
		if (!CanReplace(target))
		{
			return PyDict.Of(
				"ok", false,
				"mode", "download-only",
				"target", target,
				"package", pkg,
				"message", "更新包已下载: " + pkg + "\n当前文件不可写(" + target + "),通常是用 .deb 装的。请用包管理器安装,或手动替换。");
		}
		string staged = target + ".new";
		File.Copy(pkg, staged, overwrite: true);
		// 权限跟着旧文件走:下载下来的包是 0644,不补可执行位替换完就跑不起来了
		if (!OperatingSystem.IsWindows())
		{
			// CA1416:这两个 API 只在 Unix 上有实现(本移植只跑 Linux,但分析器要求显式守卫)
			try
			{
				File.SetUnixFileMode(staged, File.GetUnixFileMode(target));
			}
			catch (Exception)
			{
			}
		}
		File.Move(staged, target, overwrite: true);
		if (restart)
		{
			try
			{
				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
				{
					FileName = target,
					UseShellExecute = false,
				});
			}
			catch (Exception)
			{
			}
		}
		return PyDict.Of(
			"ok", true,
			"mode", IsAppImage() ? "appimage" : "inplace",
			"target", target,
			"package", pkg,
			"message", restart ? "已更新,正在重新启动。" : "已更新,重启启动器即可生效。");
	}

	public static string Download(PyDict info, DownloadManager dm = null)
	{
		string text = Py.Str(Py.Or(info?["url"], ""));
		if (text.Length == 0)
		{
			throw new RuntimeError("更新清单没有下载地址");
		}
		string text2 = Py.Lower(Py.Strip(Py.Str(Py.Or(info?["sha256"], ""))));
		if (!ValidSha256(text2))
		{
			throw new RuntimeError("更新包缺少有效 SHA-256，已拒绝下载");
		}
		dm = dm ?? new DownloadManager(4L);
		string text3 = PPath.Join(Utils.ROOT, "cache", "TipCL-" + Py.Str(Py.Or(info["latest"], "update")) + ".bin");
		DownloadManager downloadManager = dm;
		string sha = text2;
		downloadManager.Download(text, text3, null, null, force: false, null, null, sha);
		if (Utils.Sha256File(text3).ToLowerInvariant() != text2)
		{
			try
			{
				File.Delete(Utils.AbsPath(text3));
			}
			catch (Exception)
			{
			}
			throw new RuntimeError("更新包 SHA-256 校验失败");
		}
		return text3;
	}
}
