using System;
using System.IO;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher;

namespace TipCL.Services;

internal static class Payload
{
	private static readonly object Gate = new object();

	private static string? _root;

	// mac 侧的一处最小改动(见 PROGRESS.md 的 C2a 差异表):Windows 版这里是 %LOCALAPPDATA%\TipCL;
	// 非 Windows 上 Environment.SpecialFolder.LocalApplicationData 是 ~/.local/share,不是 GOAL 定的
	// mac 数据目录。mac 版唯一的数据目录是 Utils.ResolveRoot()(~/.tipcl,可用 TIPCL_HOME 覆盖),
	// 所以这里对齐它 —— 否则会凭空多出一个 ~/.local/share/TipCL 第二数据根。
	public static string DefaultRoot => Utils.ResolveRoot();

	public static string? ActiveRoot => _root;

	// mac 侧去掉了 Windows 版的 EmbeddedPython():它返回 <root>\runtime\python\python.exe,
	// 是外部 Python 桥的内嵌解释器;按 GOAL 的决定 mac 版没有外部 Python 桥(见 C2a 小节
	// 「去掉的分支」表),所以成员与唯一调用点(BridgeHost.FindPython)一起删掉。
	// 公开面:Payload 本身是 internal,删除不改变任何公开面。

	internal static bool BackdoorRequested()
	{
		if (!string.Equals(Environment.GetEnvironmentVariable("TIPCL_BRIDGE"), "python", StringComparison.OrdinalIgnoreCase))
		{
			return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TIPCL_BRIDGE_EXE"));
		}
		return true;
	}

	public static string? EnsureRoot()
	{
		lock (Gate)
		{
			if (_root != null)
			{
				return _root;
			}
			string defaultRoot = DefaultRoot;
			try
			{
				Directory.CreateDirectory(defaultRoot);
			}
			catch (Exception)
			{
			}
			if (!BackdoorRequested())
			{
				CleanupLegacy(defaultRoot);
			}
			WriteLocales(defaultRoot);
			return _root = defaultRoot;
		}
	}

	internal static bool HasLegacy(string root)
	{
		try
		{
			if (Directory.Exists(Path.Combine(root, "runtime")) || Directory.Exists(Path.Combine(root, "bridge")) || File.Exists(Path.Combine(root, ".payload-stamp")))
			{
				return true;
			}
			string path = Path.Combine(root, "mclauncher");
			if (Directory.Exists(path))
			{
				return Directory.EnumerateFileSystemEntries(path).Any((string e) => !string.Equals(Path.GetFileName(e), "locales", StringComparison.OrdinalIgnoreCase));
			}
		}
		catch (Exception)
		{
		}
		return false;
	}

	internal static void CleanupLegacy(string root)
	{
		if (!HasLegacy(root))
		{
			return;
		}
		string[] array = new string[4] { "runtime", "mclauncher", "bridge", ".payload-stamp" };
		foreach (string path in array)
		{
			string path2 = Path.Combine(root, path);
			try
			{
				if (Directory.Exists(path2))
				{
					Directory.Delete(path2, recursive: true);
				}
				else if (File.Exists(path2))
				{
					File.Delete(path2);
				}
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
	}

	internal static void WriteLocales(string root)
	{
		try
		{
			string text = Path.Combine(root, "mclauncher", "locales");
			foreach (string item in CoreResources.ListDir("locales"))
			{
				byte[] array = CoreResources.ReadBytes("locales/" + item);
				if (array == null)
				{
					continue;
				}
				string text2 = Path.Combine(text, item);
				try
				{
					if (!File.Exists(text2) || !File.ReadAllBytes(text2).SequenceEqual(array))
					{
						Directory.CreateDirectory(text);
						string text3 = text2 + ".tmp";
						File.WriteAllBytes(text3, array);
						if (File.Exists(text2))
						{
							File.Replace(text3, text2, null);
						}
						else
						{
							File.Move(text3, text2);
						}
					}
				}
				catch (IOException)
				{
				}
				catch (UnauthorizedAccessException)
				{
				}
			}
		}
		catch (Exception)
		{
		}
	}
}
