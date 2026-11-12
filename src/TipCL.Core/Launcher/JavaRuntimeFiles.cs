using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;

namespace TipCL.Core.Launcher;

/// <summary>
/// mac 上的 Java 运行时落盘细节(B2a)。全部是**纯函数或可注入副作用的函数**:
/// 不联网、不起进程、不读配置。只有在 <c>ExtractJavaArchive</c> / <c>SetExecutable</c> 里才会真正写盘。
///
/// 依据(Lead 于 2026-11-12 18:58 与本次 2026-11-12 19:0x 实测的线上清单,查询地址见 PROGRESS.md 的 B2a 小节):
/// <list type="bullet">
/// <item>Mojang 的 mac 平台键是 <c>mac-os</c> / <c>mac-os-arm64</c>(Windows 版写的 <c>osx</c> / <c>osx-arm64</c> 在清单里不存在)。</item>
/// <item><c>mac-os-arm64</c> 的 <c>java-runtime-gamma</c> 清单有 437 个条目:file 142 / directory 87 / link 208,
/// <c>executable:true</c> 29 个,link 的 <c>target</c> 全是相对路径(<c>../java.base/...</c>);
/// java 可执行文件在 <c>jre.bundle/Contents/Home/bin/java</c>。</item>
/// <item>Adoptium / Zulu 的 mac JRE 是 <c>.tar.gz</c>,解压后 java 在 <c>xxx.jre/Contents/Home/bin/java</c>。</item>
/// </list>
///
/// Windows / Linux 上的调用点都走「原路返回」分支(<see cref="ExtractJavaArchive"/> 的 <c>isMac:false</c>、
/// <see cref="FindJavaUnderRoot"/> 的 <c>!isMac</c>),行为与本包改动前逐字相同。
/// </summary>
internal static class JavaRuntimeFiles
{
	/// <summary>可执行条目的权限位:0755。</summary>
	private const UnixFileMode _Mode755 = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
		| UnixFileMode.GroupRead | UnixFileMode.GroupExecute
		| UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

	/// <summary>其余条目的权限位:0644。</summary>
	private const UnixFileMode _Mode644 = UnixFileMode.UserRead | UnixFileMode.UserWrite
		| UnixFileMode.GroupRead | UnixFileMode.OtherRead;

	/// <summary>只保留 0777:tar 里的 setuid / setgid / sticky 一律丢掉(压缩包不该给安装目录种 setuid)。</summary>
	private const UnixFileMode _PermissionMask = (UnixFileMode)0x1FF;

	/// <summary>
	/// mac 的已知 java 相对路径(顺序 = 优先顺序)。前两条来自线上清单实测,
	/// 第三条是厂商 tar 解到根之后的形态;厂商 tar 的顶层目录名不定,最后由
	/// <see cref="Utils.FindExecutable"/> 的遍历兜底。
	/// </summary>
	private static readonly string[] _MAC_JAVA_RELATIVE_PATHS = new string[5]
	{
		"jre.bundle/Contents/Home/bin/java",
		"Contents/Home/bin/java",
		"bin/java",
		"jre/bin/java",
		"jre/Contents/Home/bin/java",
	};

	/// <summary>压缩包后缀:Windows 是 <c>.zip</c>,其余(<c>osx</c> / <c>linux</c> / 未知)是 <c>.tar.gz</c>。</summary>
	internal static string ArchiveSuffixFor(string os)
	{
		if (os != "windows")
		{
			return ".tar.gz";
		}
		return ".zip";
	}

	/// <summary>
	/// Azul 元数据 API 的 <c>archive_type</c> 参数。
	/// <para>
	/// 改动前 Java.cs 里写死 <c>zip</c>,而落盘文件名走 <see cref="ArchiveSuffixFor"/>(非 Windows 是 <c>.tar.gz</c>),
	/// <c>DownloadManager.ExtractArchive</c> 又是按**文件名后缀**选解压器的 —— mac 上会拿 zip 的字节去走 GZipStream,
	/// 必然失败(见 PROGRESS.md 的 B2a「Zulu 自洽性」)。这里只把 mac 改成 <c>tar.gz</c>;
	/// Windows / Linux / 未知仍逐字返回 <c>zip</c>(红线:这两个平台的 URL 参数不许变)。
	/// </para>
	/// <para>
	/// 线上证据(2026-11-12 19:0x 实测):<c>os=macos&amp;arch=aarch64&amp;java_version=17&amp;archive_type=zip</c> 返回 4 条
	/// 且第一条是 <c>zulu17.70.113-ca-fx-jre17.0.21.0.3-macosx_aarch64.zip</c> → mac 上确实给 zip。
	/// mac + aarch64 + tar.gz 的组合因联网请求预算(≤12)用尽**未能核对**,记在 PROGRESS.md 的未验证项。
	/// </para>
	/// </summary>
	internal static string ZuluArchiveTypeFor(string os)
	{
		if (os == "osx")
		{
			return "tar.gz";
		}
		return "zip";
	}

	/// <summary>权限位纯函数:可执行 → 0755,其余 → 0644。</summary>
	internal static UnixFileMode ModeForEntry(bool executable)
	{
		return executable ? _Mode755 : _Mode644;
	}

	/// <summary><paramref name="candidateAbs"/> 规范化之后是否仍在 <paramref name="rootAbs"/> 之内(含根本身)。</summary>
	internal static bool IsInsideRoot(string rootAbs, string candidateAbs, bool isWindows)
	{
		if (string.IsNullOrEmpty(rootAbs) || string.IsNullOrEmpty(candidateAbs))
		{
			return false;
		}
		string root;
		string candidate;
		try
		{
			root = TrimSeparators(Path.GetFullPath(rootAbs));
			candidate = TrimSeparators(Path.GetFullPath(candidateAbs));
		}
		catch (Exception)
		{
			return false;
		}
		StringComparison comparison = (isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
		if (string.Equals(candidate, root, comparison))
		{
			return true;
		}
		return candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison);
	}

	/// <summary>
	/// 把清单 / 压缩包里的条目名(相对、正斜杠)解析成绝对路径。
	/// 返回 null 表示**拒绝**:条目名为空、是绝对路径、或规范化之后逃出了 <paramref name="rootAbs"/>。
	/// </summary>
	internal static string ResolveEntryPath(string rootAbs, string entryName, bool isWindows)
	{
		if (string.IsNullOrEmpty(rootAbs) || string.IsNullOrEmpty(entryName))
		{
			return null;
		}
		string relative = entryName.Replace('/', Path.DirectorySeparatorChar);
		if (Path.IsPathRooted(relative) || relative.StartsWith("\\\\", StringComparison.Ordinal))
		{
			return null;
		}
		string full;
		try
		{
			full = Path.GetFullPath(Path.Combine(Path.GetFullPath(rootAbs), relative));
		}
		catch (Exception)
		{
			return null;
		}
		return IsInsideRoot(rootAbs, full, isWindows) ? full : null;
	}

	/// <summary>
	/// 解析一个 link 条目。返回值是应交给 <c>File.CreateSymbolicLink</c> 的目标字符串:
	/// <list type="bullet">
	/// <item>相对目标、解析后仍在 root 内 → 原样返回(链接保持相对,安装目录整体移动后依然有效);</item>
	/// <item>绝对目标、且在 root 内 → 返回规范化后的绝对路径;</item>
	/// <item>Windows(<paramref name="isWindows"/> 为 true,保持现有「跳过 link」行为)、
	/// 空目标、条目名非法、目标逃出 root → null(调用方跳过并记日志)。</item>
	/// </list>
	/// </summary>
	internal static string ResolveLinkTarget(string rootAbs, string linkEntryName, string linkTarget, bool isWindows)
	{
		if (isWindows || string.IsNullOrEmpty(linkTarget))
		{
			return null;
		}
		string linkPath = ResolveEntryPath(rootAbs, linkEntryName, isWindows);
		if (linkPath == null)
		{
			return null;
		}
		string resolved;
		try
		{
			resolved = Path.GetFullPath(Path.IsPathRooted(linkTarget)
				? linkTarget
				: Path.Combine(Path.GetDirectoryName(linkPath) ?? rootAbs, linkTarget.Replace('/', Path.DirectorySeparatorChar)));
		}
		catch (Exception)
		{
			return null;
		}
		if (!IsInsideRoot(rootAbs, resolved, isWindows))
		{
			return null;
		}
		if (!Path.IsPathRooted(linkTarget))
		{
			return linkTarget;
		}
		return resolved;
	}

	/// <summary>
	/// 给定一个运行时安装根目录,返回 java 可执行文件路径;找不到返回 null(与 <see cref="Utils.FindExecutable"/> 同语义)。
	/// <para>
	/// <paramref name="isWindows"/> / <paramref name="isMac"/> 都是注入的,所以 mac 分支能在 Windows 上被单测;
	/// Windows 与 Linux(<c>isMac:false</c>)一律原样转调 <see cref="Utils.FindExecutable"/>,行为趋同于改动前。
	/// </para>
	/// </summary>
	internal static string FindJavaUnderRoot(string root, bool isWindows, bool isMac)
	{
		if (isWindows || !isMac)
		{
			return Utils.FindExecutable(root);
		}
		if (string.IsNullOrEmpty(root) || !new PPath(root).IsDir())
		{
			return null;
		}
		foreach (string relative in _MAC_JAVA_RELATIVE_PATHS)
		{
			string candidate = PPath.Join(root, relative);
			if (new PPath(candidate).IsFile())
			{
				return candidate;
			}
		}
		return Utils.FindExecutable(root);
	}

	/// <summary>把文件的权限位设成 0755。Windows 上直接返回(该 API 在 Windows 抛 PlatformNotSupportedException)。</summary>
	internal static void SetExecutable(string path)
	{
		if (OperatingSystem.IsWindows() || string.IsNullOrEmpty(path))
		{
			return;
		}
		try
		{
			File.SetUnixFileMode(Utils.AbsPath(path), _Mode755);
		}
		catch (Exception ex)
		{
			Utils.log.Warning("设置可执行权限失败 %s: %s", path, Py.ExcStr(ex));
		}
	}

	/// <summary>
	/// 按 Mojang 运行时清单里的一个 <c>type: link</c> 条目建符号链接。
	/// 目标逃出安装根目录(例如 <c>../../</c> 指到外面)时**跳过并记日志**,不抛异常。
	/// Windows 上保持改动前的行为(跳过),返回 false。
	/// </summary>
	internal static bool TryCreateRuntimeLink(string installRootAbs, string entryKey, string target, bool isWindows, Action<string> warn = null)
	{
		if (isWindows)
		{
			return false;
		}
		string linkPath = ResolveEntryPath(installRootAbs, entryKey, isWindows);
		if (linkPath == null)
		{
			Warn(warn, "Mojang 运行时 link 条目的路径非法,已跳过: " + entryKey);
			return false;
		}
		string linkTarget = ResolveLinkTarget(installRootAbs, entryKey, target, isWindows);
		if (linkTarget == null)
		{
			Warn(warn, "Mojang 运行时 link 目标不在安装目录内,已跳过: " + entryKey + " -> " + (target ?? ""));
			return false;
		}
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(linkPath));
		}
		catch (Exception ex)
		{
			Warn(warn, "Mojang 运行时 link 的父目录建立失败,已跳过: " + entryKey + " (" + Py.ExcStr(ex) + ")");
			return false;
		}
		CreateLink(linkPath, linkTarget, null, isWindows);
		return true;
	}

	/// <summary>
	/// 解压厂商的 Java 压缩包。mac 走本文件里保留权限位 / 软链的实现,
	/// Windows / Linux(<c>isMac:false</c>)一字不改地走 <see cref="DownloadManager.ExtractArchive"/>。
	/// </summary>
	internal static void ExtractJavaArchive(string archivePath, string dest, bool isMac, TarHooks hooks = null)
	{
		if (!isMac)
		{
			DownloadManager.ExtractArchive(archivePath, dest);
			return;
		}
		string name = new PPath(archivePath).Name.ToLowerInvariant();
		if (name.EndsWith(".zip", StringComparison.Ordinal) || name.EndsWith(".jar", StringComparison.Ordinal))
		{
			// mac 上 Adoptium / Zulu 的 JRE 都是 .tar.gz;真拿到 zip 就沿用原有解压器(只丢权限位,不引入新风险)。
			DownloadManager.ExtractArchive(archivePath, dest);
			return;
		}
		if (!name.EndsWith(".tar.gz", StringComparison.Ordinal) && !name.EndsWith(".tgz", StringComparison.Ordinal))
		{
			throw new DownloadError("不支持的压缩格式: " + new PPath(archivePath).Name);
		}
		SafeExtractTarGz(archivePath, dest, hooks);
	}

	/// <summary>
	/// 解 tar.gz:保留文件权限位、按清单建符号链接、条目路径与链接目标都不许逃出 <paramref name="dest"/>。
	/// 与 <see cref="Utils.SafeExtractTargz"/> 的两处差异(见 PROGRESS.md 的 B2a「tar.gz 解压现状」):
	/// 权限位从 tar 的 mode 落盘、软链条目会真的建出来(旧实现两者都丢)。
	/// 条目路径逃出根目录时抛 <see cref="ValueError"/>(与旧实现一致)。
	/// </summary>
	internal static void SafeExtractTarGz(string archivePath, string dest, TarHooks hooks = null)
	{
		TarHooks h = hooks ?? new TarHooks();
		bool isWindows = h.IsWindows ?? OperatingSystem.IsWindows();
		Utils.EnsureDir(dest);
		string rootAbs = Utils.AbsPath(dest);
		List<KeyValuePair<string, string>> links = new List<KeyValuePair<string, string>>();
		using (FileStream stream = new FileStream(Utils.AbsPath(archivePath), FileMode.Open, FileAccess.Read))
		{
			using GZipStream gzip = new GZipStream(stream, CompressionMode.Decompress);
			using TarReader reader = new TarReader(gzip);
			TarEntry? entry;
			while ((entry = reader.GetNextEntry()) != null)
			{
				string target = ResolveEntryPath(rootAbs, entry.Name, isWindows);
				if (target == null)
				{
					throw new ValueError("压缩包包含非法路径: " + entry.Name);
				}
				switch (entry.EntryType)
				{
				case TarEntryType.Directory:
					Directory.CreateDirectory(target);
					break;
				case TarEntryType.RegularFile:
				case TarEntryType.V7RegularFile:
					Directory.CreateDirectory(Path.GetDirectoryName(target));
					using (FileStream output = new FileStream(target, FileMode.Create, FileAccess.Write))
					{
						entry.DataStream?.CopyTo(output);
					}
					ApplyMode(h, target, entry.Mode);
					break;
				case TarEntryType.SymbolicLink:
				{
					string linkTarget = ResolveLinkTarget(rootAbs, entry.Name, entry.LinkName, isWindows);
					if (linkTarget == null)
					{
						Warn(h.Warn, "压缩包中的符号链接被跳过(目标不在目标目录内或在 Windows 上): " + entry.Name + " -> " + (entry.LinkName ?? ""));
						break;
					}
					Directory.CreateDirectory(Path.GetDirectoryName(target));
					links.Add(new KeyValuePair<string, string>(target, linkTarget));
					break;
				}
				default:
					Warn(h.Warn, "压缩包中的条目类型被跳过: " + entry.EntryType + " " + entry.Name);
					break;
				}
			}
		}
		foreach (KeyValuePair<string, string> link in links)
		{
			CreateLink(link.Key, link.Value, h.CreateLink, isWindows);
		}
	}

	private static void ApplyMode(TarHooks hooks, string path, UnixFileMode mode)
	{
		UnixFileMode masked = mode & _PermissionMask;
		if (masked == 0)
		{
			masked = ModeForEntry(executable: false);
		}
		if (hooks.SetMode != null)
		{
			hooks.SetMode(path, masked);
			return;
		}
		if (OperatingSystem.IsWindows())
		{
			return;
		}
		try
		{
			File.SetUnixFileMode(path, masked);
		}
		catch (Exception ex)
		{
			Warn(hooks.Warn, "设置权限位失败 " + path + ": " + Py.ExcStr(ex));
		}
	}

	private static void CreateLink(string linkPath, string linkTarget, Action<string, string> createLink, bool isWindows)
	{
		if (createLink != null)
		{
			createLink(linkPath, linkTarget);
			return;
		}
		if (isWindows || OperatingSystem.IsWindows())
		{
			return;
		}
		try
		{
			File.CreateSymbolicLink(linkPath, linkTarget);
		}
		catch (Exception ex)
		{
			Utils.log.Warning("建立符号链接失败 %s -> %s: %s", linkPath, linkTarget, Py.ExcStr(ex));
		}
	}

	private static void Warn(Action<string> warn, string message)
	{
		if (warn != null)
		{
			warn(message);
			return;
		}
		Utils.log.Warning("%s", message);
	}

	private static string TrimSeparators(string path)
	{
		return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
	}

	/// <summary>解压的副作用出口。测试注入假委托即可在 Windows 上验证 mac 分支(不真的改权限位 / 建链接)。</summary>
	internal sealed class TarHooks
	{
		/// <summary>权限位出口;null 时走 <c>File.SetUnixFileMode</c>(非 Windows)。</summary>
		public Action<string, UnixFileMode> SetMode { get; set; }

		/// <summary>建链接出口;null 时走 <c>File.CreateSymbolicLink</c>(非 Windows)。</summary>
		public Action<string, string> CreateLink { get; set; }

		/// <summary>日志出口;null 时走 <c>Utils.log.Warning</c>。</summary>
		public Action<string> Warn { get; set; }

		/// <summary>是否按 Windows 语义解析路径;null 时按当前真实平台。</summary>
		public bool? IsWindows { get; set; }
	}
}
