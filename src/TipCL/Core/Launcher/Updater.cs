using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TipCL.Core.Launcher;

public static class Updater
{
	public const string DEFAULT_URL = "https://raw.githubusercontent.com/wfwwq/TipCL/main/update.json";

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

	public static bool Newer(object remote, string local = "1.0.9")
	{
		return CmpTuple(_parse(remote), _parse(local)) > 0;
	}

	public static string ManifestUrl()
	{
		return Py.Strip(Py.Str(Py.Or(Config.CONFIG.Get("update_url"), "https://raw.githubusercontent.com/wfwwq/TipCL/main/update.json")));
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
			data = dm.FetchJson(url, 12L);
		}
		catch (Exception e)
		{
			return PyDict.Of("ok", false, "current", "1.0.9", "latest", "1.0.9", "has_update", false, "message", "检查更新失败: " + Py.ExcStr(e), "notes", "", "url", "");
		}
		string text = Py.Str(Py.Or(G(data, "version"), G(data, "latest"), ""));
		bool flag = text.Length > 0 && Newer(text);
		string text2 = Py.Lower(Py.Strip(Py.Str(Py.Or(G(data, "sha256"), ""))));
		bool flag2 = flag && ValidSha256(text2);
		bool flag3 = flag && !flag2;
		return PyDict.Of("ok", !flag3, "current", "1.0.9", "latest", (text.Length > 0) ? text : "1.0.9", "has_update", flag2, "message", flag3 ? "更新清单缺少有效 SHA-256，已拒绝自动更新" : (flag ? ("发现 " + text) : "已是最新版本"), "notes", Py.Str(Py.Or(G(data, "notes"), G(data, "changelog"), "")), "url", Py.Str(Py.Or(G(data, "url"), G(data, "download"), "")), "sha256", text2);
	}

	/// <summary>
	/// 生成替换脚本：等当前进程退出 → 用下载好的新版覆盖当前 exe → （可选）重新打开。
	/// 用 PowerShell -EncodedCommand，避免中文路径和引号转义问题。
	/// </summary>
	public static string BuildApplyScript(string package, string exe, int pid, bool restart)
	{
		string Q(string s) => "'" + (s ?? "").Replace("'", "''") + "'";
		StringBuilder sb = new StringBuilder();
		sb.Append("$ErrorActionPreference='SilentlyContinue';");
		sb.Append("Wait-Process -Id ").Append(pid).Append(" -Timeout 60;");
		sb.Append("$ok=$false;for($i=0;$i -lt 40 -and -not $ok;$i++){try{Copy-Item -LiteralPath ").Append(Q(package)).Append(" -Destination ").Append(Q(exe)).Append(" -Force -ErrorAction Stop;$ok=$true}catch{Start-Sleep -Milliseconds 500}};");
		sb.Append("if($ok){Remove-Item -LiteralPath ").Append(Q(package)).Append("};");
		if (restart)
		{
			sb.Append("Start-Process -FilePath ").Append(Q(exe)).Append(';');
		}
		return sb.ToString();
	}

	/// <summary>启动后台替换进程。调用方随后应退出启动器。</summary>
	public static void ApplyStaged(string package, bool restart)
	{
		string pkg = Utils.AbsPath(package);
		if (!File.Exists(pkg))
		{
			throw new RuntimeError("更新包不存在: " + pkg);
		}
		System.Diagnostics.Process current = System.Diagnostics.Process.GetCurrentProcess();
		string exe = current.MainModule?.FileName ?? "";
		if (exe.Length == 0)
		{
			throw new RuntimeError("找不到启动器自身路径");
		}
		string script = BuildApplyScript(pkg, exe, current.Id, restart);
		string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
		System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
		{
			FileName = "powershell.exe",
			Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + encoded,
			UseShellExecute = false,
			CreateNoWindow = true
		});
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
