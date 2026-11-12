using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using TipCL.Core.Launcher;

namespace TipCL.Core.Plugins;

/// <summary>
/// Node 运行时（DESIGN §3.1）：找 node、按需下载便携版、释放内置宿主脚本、跑 npm install。
/// </summary>
public static class NodeRuntime
{
	public const int MinMajor = 22;

	/// <summary>便携版下载源（按顺序尝试）。</summary>
	public static readonly string[] Mirrors = new string[2] { "https://npmmirror.com/mirrors/node/", "https://nodejs.org/dist/" };

	/// <summary>内置宿主脚本资源前缀（csproj 里 LogicalName = TipCL.Core.plugin-host.%(RecursiveDir)%(Filename)%(Extension)）。</summary>
	public const string HostResourcePrefix = "TipCL.Core.plugin-host.";

	public sealed class NodeInfo
	{
		public string Path;

		public string Version;

		/// <summary>system | portable | env</summary>
		public string Source;

		public long Major
		{
			get
			{
				SemVer v = SemVer.TryParse(Version);
				return (v == null) ? 0 : v.Major;
			}
		}
	}

	private static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

	private static string ExeName => IsWindows ? "node.exe" : "node";

	/// <summary>运行 node --version，失败返回 null。</summary>
	public static string ProbeVersion(string nodePath, int timeoutMs = 8000)
	{
		try
		{
			ProcessStartInfo psi = new ProcessStartInfo(nodePath, "--version")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			using Process p = Process.Start(psi);
			string output = p.StandardOutput.ReadToEnd();
			if (!p.WaitForExit(timeoutMs))
			{
				try
				{
					p.Kill();
				}
				catch (Exception)
				{
				}
				return null;
			}
			string v = output.Trim();
			return SemVer.IsValid(v) ? v.TrimStart('v') : null;
		}
		catch (Exception)
		{
			return null;
		}
	}

	/// <summary>在 PATH 里找可执行文件。</summary>
	public static string FindOnPath(string exe)
	{
		string path = Environment.GetEnvironmentVariable("PATH") ?? "";
		foreach (string dir in path.Split(System.IO.Path.PathSeparator))
		{
			if (string.IsNullOrWhiteSpace(dir))
			{
				continue;
			}
			try
			{
				string p = System.IO.Path.Combine(dir.Trim().Trim('"'), exe);
				if (File.Exists(p))
				{
					return p;
				}
			}
			catch (Exception)
			{
			}
		}
		return null;
	}

	/// <summary>
	/// 找可用的 Node（≥22）：TIPCL_NODE 环境变量 → 系统 PATH → runtime/node。都没有返回 null（需要下载）。
	/// </summary>
	public static NodeInfo Locate(PluginDirs dirs)
	{
		string env = Environment.GetEnvironmentVariable("TIPCL_NODE");
		if (!string.IsNullOrEmpty(env) && File.Exists(env))
		{
			string ve = ProbeVersion(env);
			if (ve != null)
			{
				return new NodeInfo
				{
					Path = env,
					Version = ve,
					Source = "env"
				};
			}
		}
		string sys = FindOnPath(ExeName);
		if (sys != null)
		{
			string v = ProbeVersion(sys);
			if (v != null && SemVer.TryParse(v).Major >= MinMajor)
			{
				return new NodeInfo
				{
					Path = sys,
					Version = v,
					Source = "system"
				};
			}
		}
		string portable = System.IO.Path.Combine(dirs.NodeDir, ExeName);
		if (!IsWindows && !File.Exists(portable))
		{
			portable = System.IO.Path.Combine(dirs.NodeDir, "bin", "node");
		}
		if (File.Exists(portable))
		{
			string v2 = ProbeVersion(portable);
			if (v2 != null && SemVer.TryParse(v2).Major >= MinMajor)
			{
				return new NodeInfo
				{
					Path = portable,
					Version = v2,
					Source = "portable"
				};
			}
		}
		return null;
	}

	/// <summary>
	/// B2c2:便携版 Node 的 (platform, ext) —— 纯函数,可注入 os / 进程架构 / 是否 64 位系统。
	/// 返回 null 表示本平台不支持自动下载(<see cref="Download"/> 抛原来那句 <see cref="RuntimeError"/>)。
	/// <list type="bullet">
	/// <item>Windows:<c>win-x64</c> / <c>win-x86</c> + <c>.zip</c>,与改动前逐字相同
	/// (原来就是 Environment.Is64BitOperatingSystem ? "win-x64" : "win-x86",不看进程架构);</item>
	/// <item>mac:<c>darwin-arm64</c> / <c>darwin-x64</c> + <c>.tar.gz</c>,**按进程架构**取
	/// (Node 是启动器自己用的,与 B2d 的目标架构无关)。包名形态有线上证据:2026-11-12 21:35 抓取的
	/// nodejs.org / npmmirror 两份 SHASUMS256.txt 里都是 node-v22.23.5-darwin-arm64.tar.gz 这样的名字,
	/// 见 PROGRESS.md 的 B2c2 小节;</item>
	/// <item>其它(含 linux,以及 32 位 / 未知的 mac 架构):不支持。</item>
	/// </list>
	/// </summary>
	internal static (string platform, string ext)? DownloadTargetFor(string os, Architecture processArch, bool is64BitOperatingSystem)
	{
		if (os == "windows")
		{
			return (is64BitOperatingSystem ? "win-x64" : "win-x86", ".zip");
		}
		if (os == "osx")
		{
			if (processArch == Architecture.Arm64)
			{
				return ("darwin-arm64", ".tar.gz");
			}
			if (processArch == Architecture.X64)
			{
				return ("darwin-x64", ".tar.gz");
			}
			return null;
		}
		return null;
	}

	/// <summary>
	/// B2c2:便携版 node 可执行文件的落点(纯函数)。
	/// Windows 是 &lt;node 目录&gt;/node.exe;非 Windows 是 &lt;node 目录&gt;/bin/node
	/// —— 与 <see cref="Locate"/> 里已有的「非 Windows 且 &lt;dir&gt;/node 不存在时改看 &lt;dir&gt;/bin/node」一致。
	/// mac 上这个布局**未经真机验证**(只按 Locate 的既有约定实现)。
	/// </summary>
	internal static string PortableExePath(string nodeDir, bool isWindows)
	{
		return isWindows ? System.IO.Path.Combine(nodeDir, "node.exe") : System.IO.Path.Combine(nodeDir, "bin", "node");
	}

	/// <summary>
	/// B2c2:解压临时目录里「含 node 可执行文件的那一层」的探测(纯函数,注入 isWindows 与两个 IO 委托便于单测)。
	/// Windows 看一层子目录里有没有 <c>node.exe</c>;非 Windows 看 <c>bin/node</c>
	/// (tar.gz 顶层是 node-v22.x.y-darwin-*/ 一层目录)。找不到返回 null,调用方按原来的 <c>?? tmp</c> 处理。
	/// <paramref name="listDirs"/> 抛异常时原样往上抛(与改动前 Directory.GetDirectories 的行为一致:
	/// 由 <see cref="Download"/> 的镜像循环 catch 掉)。
	/// </summary>
	internal static string FindExtractedNodeDir(string tmp, bool isWindows, Func<string, string[]> listDirs = null, Func<string, bool> fileExists = null)
	{
		Func<string, string[]> dirs = listDirs ?? ((string d) => Directory.GetDirectories(d));
		Func<string, bool> exists = fileExists ?? ((string p) => File.Exists(p));
		string relative = isWindows ? "node.exe" : System.IO.Path.Combine("bin", "node");
		return dirs(tmp).FirstOrDefault((string d) => exists(System.IO.Path.Combine(d, relative)));
	}

	/// <summary>从 SHASUMS256.txt 里挑出 v22 win-x64 zip 的文件名和 sha256。</summary>
	public static (string file, string sha256) PickFromShasums(string shasums, string platform = "win-x64", string ext = ".zip")
	{
		foreach (string line in (shasums ?? "").Split('\n'))
		{
			string[] parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length == 2 && parts[1].StartsWith("node-v", StringComparison.Ordinal) && parts[1].EndsWith("-" + platform + ext, StringComparison.Ordinal) && parts[0].Length == 64)
			{
				return (parts[1], parts[0].ToLowerInvariant());
			}
		}
		return (null, null);
	}

	/// <summary>
	/// 下载便携版 Node 22 LTS 到 runtime/node（走 TipCL 下载器，校验 SHASUMS256）。只支持 Windows x64（其他平台抛错）。
	/// B2c2 更正:上面那句「只支持 Windows x64」已不再成立 —— 现在 Windows 装 win-x64 / win-x86 的 zip,
	/// mac 装 darwin-arm64 / darwin-x64 的 tar.gz(见 DownloadTargetFor,包名形态有线上 SHASUMS256.txt 证据),
	/// 其余平台仍然抛 RuntimeError。Windows 的取值与文案逐字未变。
	/// </summary>
	public static NodeInfo Download(PluginDirs dirs, DownloadManager dm, Action<string> log)
	{
		(string platform, string ext)? target = DownloadTargetFor(Utils.OS_NAME, RuntimeInformation.ProcessArchitecture, Environment.Is64BitOperatingSystem);
		if (target == null)
		{
			throw new RuntimeError("当前系统不支持自动下载 Node，请自行安装 Node " + MinMajor + " 或更高版本");
		}
		string platform = target.Value.platform;
		string ext = target.Value.ext;
		Exception last = null;
		foreach (string mirror in Mirrors)
		{
			try
			{
				string baseUrl = mirror + "latest-v" + MinMajor + ".x/";
				log?.Invoke("获取 Node 版本信息：" + baseUrl);
				string shasums = Net.GetText(baseUrl + "SHASUMS256.txt", 6.0, 30.0);
				var (file, sha) = PickFromShasums(shasums, platform, ext);
				if (file == null)
				{
					throw new RuntimeError("SHASUMS256.txt 里没有 " + platform + " 的 " + (ext == ".zip" ? "zip" : ext));
				}
				string zip = System.IO.Path.Combine(dirs.RuntimeRoot, ".download", file);
				log?.Invoke("下载 " + file);
				dm.Download(baseUrl + file, zip, null, null, force: false, null, null, sha, null, expand: false);
				if (!string.Equals(Utils.Sha256File(zip), sha, StringComparison.OrdinalIgnoreCase))
				{
					try
					{
						File.Delete(zip);
					}
					catch (Exception)
					{
					}
					throw new RuntimeError("Node 安装包校验失败（sha256 不符）");
				}
				string tmp = dirs.NodeDir + ".tmp";
				if (Directory.Exists(tmp))
				{
					Utils.RemoveTree(tmp);
				}
				log?.Invoke("解压 Node…");
				if (ext == ".zip")
				{
					Utils.SafeExtractZip(zip, tmp);
				}
				else
				{
					JavaRuntimeFiles.SafeExtractTarGz(zip, tmp);
				}
				// zip 里是 node-v22.x.y-win-x64/ 一层目录;tar.gz 里是 node-v22.x.y-darwin-*/ 一层目录,可执行文件在其下 bin/node
				string inner = FindExtractedNodeDir(tmp, IsWindows) ?? tmp;
				if (Directory.Exists(dirs.NodeDir))
				{
					Utils.RemoveTree(dirs.NodeDir);
				}
				Directory.Move(inner, dirs.NodeDir);
				if (Directory.Exists(tmp))
				{
					Utils.RemoveTree(tmp);
				}
				try
				{
					File.Delete(zip);
				}
				catch (Exception)
				{
				}
				string exe = PortableExePath(dirs.NodeDir, IsWindows);
				string v = ProbeVersion(exe) ?? file.Split('-')[1].TrimStart('v');
				log?.Invoke("Node " + v + " 已就绪");
				return new NodeInfo
				{
					Path = exe,
					Version = v,
					Source = "portable"
				};
			}
			catch (Exception ex) when (!(ex is TaskCancelled) && !(ex is OperationCanceledException))
			{
				last = ex;
				log?.Invoke("从 " + mirror + " 下载失败：" + Py.ExcStr(ex));
			}
		}
		throw new RuntimeError("下载 Node 失败：" + ((last != null) ? Py.ExcStr(last) : "未知错误"));
	}

	/// <summary>内置宿主脚本资源：相对路径 → 资源名。</summary>
	public static Dictionary<string, string> HostResources()
	{
		Dictionary<string, string> r = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (string n in typeof(NodeRuntime).Assembly.GetManifestResourceNames())
		{
			if (n.StartsWith(HostResourcePrefix, StringComparison.Ordinal))
			{
				string rel = n.Substring(HostResourcePrefix.Length).Replace('\\', '/');
				if (rel.Length > 0 && PluginManifest.IsSafeRelPath(rel))
				{
					r[rel] = n;
				}
			}
		}
		return r;
	}

	/// <summary>
	/// 把内置宿主脚本释放到 runtime/plugin-host。内容哈希写在 .version 里，相同就不覆盖。
	/// 环境变量 TIPCL_PLUGIN_HOST_DIR 指向开发目录时直接用它（宿主开发用）。返回 host.mjs 路径。
	/// </summary>
	public static string ExtractHost(PluginDirs dirs)
	{
		string devDir = Environment.GetEnvironmentVariable("TIPCL_PLUGIN_HOST_DIR");
		if (!string.IsNullOrEmpty(devDir) && File.Exists(System.IO.Path.Combine(devDir, "host.mjs")))
		{
			return System.IO.Path.Combine(devDir, "host.mjs");
		}
		Dictionary<string, string> res = HostResources();
		if (res.Count == 0)
		{
			string existing = System.IO.Path.Combine(dirs.HostDir, "host.mjs");
			if (File.Exists(existing))
			{
				return existing;
			}
			throw new RuntimeError("程序里没有内置 Node 宿主脚本（plugin-host）");
		}
		string hash;
		using (SHA256 sha = SHA256.Create())
		{
			foreach (KeyValuePair<string, string> kv in res.OrderBy((KeyValuePair<string, string> x) => x.Key, StringComparer.Ordinal))
			{
				byte[] nameBytes = Encoding.UTF8.GetBytes(kv.Key + "\n");
				sha.TransformBlock(nameBytes, 0, nameBytes.Length, null, 0);
				byte[] data = CoreResources.ReadByResourceName(kv.Value) ?? new byte[0];
				sha.TransformBlock(data, 0, data.Length, null, 0);
			}
			sha.TransformFinalBlock(new byte[0], 0, 0);
			hash = Utils.Hex(sha.Hash);
		}
		string verFile = System.IO.Path.Combine(dirs.HostDir, ".version");
		string hostMjs = System.IO.Path.Combine(dirs.HostDir, "host.mjs");
		if (File.Exists(verFile) && File.ReadAllText(verFile).Trim() == hash && File.Exists(hostMjs))
		{
			return hostMjs;
		}
		string tmp = dirs.HostDir + ".tmp";
		if (Directory.Exists(tmp))
		{
			Utils.RemoveTree(tmp);
		}
		Directory.CreateDirectory(tmp);
		foreach (KeyValuePair<string, string> kv2 in res)
		{
			string dest = System.IO.Path.Combine(tmp, kv2.Key.Replace('/', System.IO.Path.DirectorySeparatorChar));
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest));
			File.WriteAllBytes(dest, CoreResources.ReadByResourceName(kv2.Value) ?? new byte[0]);
		}
		File.WriteAllText(System.IO.Path.Combine(tmp, ".version"), hash);
		if (Directory.Exists(dirs.HostDir))
		{
			// 旧宿主可能还在跑（文件被占用）：失败就逐个覆盖
			try
			{
				Utils.RemoveTree(dirs.HostDir);
			}
			catch (Exception)
			{
			}
		}
		if (!Directory.Exists(dirs.HostDir))
		{
			Directory.Move(tmp, dirs.HostDir);
		}
		else
		{
			CopyTree(tmp, dirs.HostDir);
			Utils.RemoveTree(tmp);
		}
		return hostMjs;
	}

	public static void CopyTree(string src, string dst)
	{
		Directory.CreateDirectory(dst);
		foreach (string f in Directory.GetFiles(src))
		{
			File.Copy(f, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(f)), overwrite: true);
		}
		foreach (string d in Directory.GetDirectories(src))
		{
			CopyTree(d, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(d)));
		}
	}

	/// <summary>npm-cli.js 的位置（Windows：&lt;node 目录&gt;/node_modules/npm；Unix：&lt;prefix&gt;/lib/node_modules/npm）。</summary>
	public static string FindNpmCli(string nodePath)
	{
		string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(nodePath));
		string[] candidates = new string[3]
		{
			System.IO.Path.Combine(dir, "node_modules", "npm", "bin", "npm-cli.js"),
			System.IO.Path.Combine(dir, "..", "lib", "node_modules", "npm", "bin", "npm-cli.js"),
			System.IO.Path.Combine(dir, "..", "node_modules", "npm", "bin", "npm-cli.js")
		};
		return candidates.Select(System.IO.Path.GetFullPath).FirstOrDefault(File.Exists);
	}

	/// <summary>
	/// 在插件目录跑 <c>npm install --omit=dev</c>（可选 --registry）。输出逐行交给 log；token 取消时杀进程。
	/// </summary>
	public static void NpmInstall(string nodePath, string dir, string registry, Action<string> log, CancellationToken token)
	{
		string cli = FindNpmCli(nodePath);
		List<string> args = new List<string>();
		string file;
		if (cli != null)
		{
			file = nodePath;
			args.Add(cli);
		}
		else
		{
			file = FindOnPath(IsWindows ? "npm.cmd" : "npm") ?? throw new RuntimeError("找不到 npm（Node 安装不完整）");
		}
		args.AddRange(new[] { "install", "--omit=dev", "--no-audit", "--no-fund", "--loglevel=error" });
		if (!string.IsNullOrEmpty(registry))
		{
			if (!registry.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !registry.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			{
				throw new ValueError("npm registry 必须是 http(s) 地址");
			}
			args.Add("--registry=" + registry);
		}
		log?.Invoke("npm install（" + dir + "）");
		int code = RunCancellable(file, args, dir, log, token, TimeSpan.FromMinutes(15.0));
		if (code != 0)
		{
			throw new RuntimeError("npm install 失败（退出码 " + code + "）");
		}
	}

	/// <summary>跑一个进程，逐行回传输出；取消时杀掉整棵进程树。返回退出码。</summary>
	public static int RunCancellable(string file, IList<string> args, string cwd, Action<string> log, CancellationToken token, TimeSpan timeout)
	{
		ProcessStartInfo psi = new ProcessStartInfo(file, Utils.List2CmdLine(args))
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			WorkingDirectory = cwd,
			StandardOutputEncoding = new UTF8Encoding(false),
			StandardErrorEncoding = new UTF8Encoding(false)
		};
		using Process p = new Process
		{
			StartInfo = psi
		};
		p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				log?.Invoke(e.Data);
			}
		};
		p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				log?.Invoke(e.Data);
			}
		};
		p.Start();
		p.BeginOutputReadLine();
		p.BeginErrorReadLine();
		DateTime deadline = DateTime.UtcNow + timeout;
		while (!p.WaitForExit(200))
		{
			if (token.IsCancellationRequested || DateTime.UtcNow > deadline)
			{
				KillTree(p);
				if (token.IsCancellationRequested)
				{
					throw new TaskCancelled();
				}
				throw new TimeoutError("进程超时：" + file);
			}
		}
		p.WaitForExit();
		return p.ExitCode;
	}

	public static void KillTree(Process p)
	{
		try
		{
			if (IsWindows)
			{
				using Process k = Process.Start(new ProcessStartInfo("taskkill", "/PID " + p.Id + " /T /F")
				{
					UseShellExecute = false,
					CreateNoWindow = true
				});
				k?.WaitForExit(5000);
			}
		}
		catch (Exception)
		{
		}
		try
		{
			if (!p.HasExited)
			{
				p.Kill();
			}
		}
		catch (Exception)
		{
		}
	}
}
