using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TipCL.Core.Launcher;

/// <summary>
/// 首次运行时寻找用户已有的 .minecraft（启动器同级/上级目录、%APPDATA%、各固定盘根目录），
/// 认得 PCL 的目录（PCL.ini）。优先选真正装了版本（versions/&lt;id&gt;/&lt;id&gt;.json）的目录。
/// </summary>
internal static class GameDirDetector
{
	public static string FindExisting()
	{
		string own = OwnGameDir();
		string weak = null;
		foreach (string item in Candidates())
		{
			try
			{
				if (string.IsNullOrEmpty(item) || !Directory.Exists(item))
				{
					continue;
				}
				string fullPath = Path.GetFullPath(item);
				if (own != null && string.Equals(fullPath.TrimEnd('\\', '/'), own.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				if (CountVersions(fullPath) > 0)
				{
					return fullPath;
				}
				if (weak == null && File.Exists(Path.Combine(fullPath, "PCL.ini")))
				{
					weak = fullPath;
				}
			}
			catch
			{
			}
		}
		return weak;
	}

	/// <summary>versions 下有多少个 &lt;id&gt;/&lt;id&gt;.json，与 Instance.InstalledIds 的判定一致。</summary>
	internal static int CountVersions(string gameDir)
	{
		try
		{
			DirectoryInfo directoryInfo = new DirectoryInfo(Path.Combine(gameDir, "versions"));
			if (!directoryInfo.Exists)
			{
				return 0;
			}
			return directoryInfo.EnumerateDirectories().Take(500).Count((DirectoryInfo d) => File.Exists(Path.Combine(d.FullName, d.Name + ".json")));
		}
		catch
		{
			return 0;
		}
	}

	private static string OwnGameDir()
	{
		try
		{
			return Path.GetFullPath(PPath.Join(Utils.ROOT, ".minecraft"));
		}
		catch
		{
			return null;
		}
	}

	private static List<string> Candidates()
	{
		List<string> list = new List<string>();
		string[] array = new string[2]
		{
			AppDomain.CurrentDomain.BaseDirectory,
			Environment.CurrentDirectory
		};
		foreach (string text in array)
		{
			try
			{
				if (string.IsNullOrEmpty(text))
				{
					continue;
				}
				DirectoryInfo directoryInfo = new DirectoryInfo(text);
				for (int j = 0; j < 4; j++)
				{
					if (directoryInfo == null)
					{
						break;
					}
					list.Add(Path.Combine(directoryInfo.FullName, ".minecraft"));
					directoryInfo = directoryInfo.Parent;
				}
			}
			catch
			{
			}
		}
		try
		{
			string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			if (!string.IsNullOrEmpty(folderPath))
			{
				list.Add(Path.Combine(folderPath, ".minecraft"));
			}
		}
		catch
		{
		}
		try
		{
			foreach (DriveInfo driveInfo in DriveInfo.GetDrives())
			{
				try
				{
					if (driveInfo.DriveType == DriveType.Fixed && driveInfo.IsReady)
					{
						list.Add(Path.Combine(driveInfo.RootDirectory.FullName, ".minecraft"));
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		return list;
	}
}
