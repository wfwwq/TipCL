using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace TipCL.Core.Launcher;

public static class GlobalMods
{
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool CreateSymbolicLinkW(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);

	private static bool IsSymlink(string p)
	{
		return IsSymlink(p, Utils.IS_WINDOWS, null);
	}

	/// <summary>
	/// B2c1:<paramref name="isWindows"/> 为 true 时**逐字**是改动前的实现(先看
	/// <see cref="FileAttributes.ReparsePoint"/>,读属性抛异常就返回 false)。
	/// 非 Windows 上改用「链接目标不为 null」判断:Windows 的
	/// <c>ReparsePoint</c> 是 NTFS 重解析点语义,在 mac 上既不看 <c>lstat</c> 也对**悬空链接**不成立;
	/// <see cref="FileSystemInfo.LinkTarget"/> 对正常链接与悬空链接都返回目标字符串。
	/// <paramref name="readLinkTarget"/> 为 null 时走真实文件系统(<see cref="FileSystemInfo.LinkTarget"/>);
	/// 任何异常返回 false(与改动前一致)。
	/// </summary>
	internal static bool IsSymlink(string p, bool isWindows, Func<string, string> readLinkTarget)
	{
		if (!isWindows)
		{
			try
			{
				return (readLinkTarget ?? LinkTargetOrDefault)(p) != null;
			}
			catch (Exception)
			{
				return false;
			}
		}
		try
		{
			FileInfo fileInfo = new FileInfo(p);
			return (fileInfo.Exists || Directory.Exists(p)) ? ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0) : ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0);
		}
		catch (Exception)
		{
			return false;
		}
	}

	/// <summary>
	/// 生产实现:读一个路径上的符号链接目标(null = 不是符号链接)。
	/// 用 <see cref="FileInfo"/> 而不是 <see cref="DirectoryInfo"/>:悬空的**目录**链接上
	/// <c>Directory.Exists</c> 是 false,而两者取到的 <c>LinkTarget</c> 相同。
	/// </summary>
	private static string LinkTargetOrDefault(string p)
	{
		FileSystemInfo info = Directory.Exists(p) ? new DirectoryInfo(p) : new FileInfo(p);
		return info.LinkTarget;
	}

	public static bool _link_file(string dest, string src)
	{
		return _link_file(dest, src, Utils.IS_WINDOWS, null);
	}

	/// <summary>
	/// B1b:加了平台分支 + 可注入的「建链接」委托(委托为 null 时用 <see cref="CreateSymbolicLinkDefault"/>),
	/// 这样「非 Windows 分支」在 Windows 机器上也能被测到,而公开的 <see cref="_link_file(string, string)"/>
	/// 签名与 Windows 行为一字不变。
	/// 原来是无条件调 kernel32 的 CreateSymbolicLinkW:mac 上抛 DllNotFoundException 被下面的 catch 吞掉,
	/// 静默退化成复制文件(见 PROGRESS.md 的 A3「运行时差异清点」D2)。建链接失败后回退 File.Copy 的兜底逻辑不变。
	/// </summary>
	internal static bool _link_file(string dest, string src, bool isWindows, Func<string, string, bool> link)
	{
		string text = Utils.AbsPath(dest);
		string text2 = Utils.AbsPath(src);
		if (File.Exists(text) || Directory.Exists(text) || IsSymlink(text))
		{
			return false;
		}
		Utils.EnsureDir(new PPath(dest).Parent.ToString());
		try
		{
			bool flag;
			if (isWindows)
			{
				flag = CreateSymbolicLinkW(text, text2, 2);
			}
			else
			{
				flag = (link ?? CreateSymbolicLinkDefault)(text, text2);
			}
			if (flag)
			{
				return true;
			}
			throw new IOException("symlink failed");
		}
		catch (Exception)
		{
			try
			{
				File.Copy(text2, text);
				File.SetLastWriteTimeUtc(text, File.GetLastWriteTimeUtc(text2));
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}
	}

	/// <summary>非 Windows 的建链接实现:(链接路径, 目标路径)。.NET 在 Unix 上走 symlink(2)。</summary>
	private static bool CreateSymbolicLinkDefault(string linkPath, string targetPath)
	{
		File.CreateSymbolicLink(linkPath, targetPath);
		return true;
	}

	public static long Apply(string game_mods_dir)
	{
		string text = Root();
		Utils.EnsureDir(game_mods_dir);
		if (!LocalFs.IsDir(text))
		{
			return 0L;
		}
		long num = 0L;
		foreach (string item in LocalFs.SortedChildren(text))
		{
			string name = new PPath(item).Name;
			if (LocalFs.IsFile(item) && name.ToLowerInvariant().EndsWith(".jar") && _link_file(PPath.Join(game_mods_dir, name), item))
			{
				num++;
			}
		}
		return num;
	}

	public static string Root()
	{
		string text = Py.SS(Config.CONFIG.Get("global_mods_dir"));
		if (text.Length > 0)
		{
			return PPath.Norm(text);
		}
		return PPath.Join(Utils.ROOT, "shared", "mods");
	}

	public static List<object> ListEntries()
	{
		string text = Root();
		List<object> list = new List<object>();
		if (!LocalFs.IsDir(text))
		{
			return list;
		}
		foreach (string item in LocalFs.SortedChildren(text))
		{
			if (LocalFs.IsFile(item))
			{
				string name = new PPath(item).Name;
				string text2 = name.ToLowerInvariant();
				if (text2.EndsWith(".jar"))
				{
					list.Add(PyDict.Of("filename", name, "enabled", true, "bytes", LocalFs.Size(item)));
				}
				else if (text2.EndsWith(".jar.disabled") || text2.EndsWith(".disabled"))
				{
					list.Add(PyDict.Of("filename", name, "enabled", false, "bytes", LocalFs.Size(item)));
				}
			}
		}
		return list;
	}

	public static string SetEnabled(object filename, object enabled)
	{
		string text = Root();
		PPath pPath = new PPath(text) / Py.AsStr(filename, "__fspath__");
		if (!pPath.IsFile())
		{
			throw new FileNotFoundError(Py.Str(filename));
		}
		string name = pPath.Name;
		if (Py.Truthy(enabled))
		{
			if (name.ToLowerInvariant().EndsWith(".disabled"))
			{
				PPath pPath2 = pPath.WithName(name.Substring(0, name.Length - ".disabled".Length));
				if (pPath2.Exists())
				{
					throw new OSError("启用失败，已存在: " + pPath2.Name);
				}
				Utils.OsReplace(pPath.Abs(), pPath2.Abs());
				return pPath2.Name;
			}
			return name;
		}
		if (name.ToLowerInvariant().EndsWith(".disabled"))
		{
			return name;
		}
		PPath pPath3 = pPath.WithName(name + ".disabled");
		if (pPath3.Exists())
		{
			throw new OSError("禁用失败，已存在: " + pPath3.Name);
		}
		Utils.OsReplace(pPath.Abs(), pPath3.Abs());
		return pPath3.Name;
	}
}
