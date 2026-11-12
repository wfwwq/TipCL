using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TipCL.Core.Launcher;

public static class Shortcut
{
	private const string _BAD_NAME_CHARS = "\\/:*?\"<>|";

	private static string _PROJECT_ROOT => Path.GetDirectoryName(Path.GetFullPath(ExePath()));

	private static string ExePath()
	{
		return Process.GetCurrentProcess().MainModule.FileName;
	}

	public static string SafeFilename(object name)
	{
		StringBuilder stringBuilder = new StringBuilder();
		string text = Py.Strip(Py.Str(Py.Or(name, "")));
		foreach (char c in text)
		{
			stringBuilder.Append(("\\/:*?\"<>|".IndexOf(c) >= 0) ? '_' : c);
		}
		string text2 = Py.Strip(stringBuilder.ToString(), ". ");
		if (text2.Length <= 0)
		{
			return "Minecraft";
		}
		return text2;
	}

	public static string DesktopDir()
	{
		if (Utils.IS_WINDOWS)
		{
			string text = null;
			try
			{
				text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
			}
			catch (Exception)
			{
			}
			if (!string.IsNullOrEmpty(text) && Directory.Exists(text))
			{
				return text;
			}
		}
		string text2 = (Utils.IS_WINDOWS ? Environment.GetEnvironmentVariable("USERPROFILE") : Environment.GetEnvironmentVariable("HOME"));
		if (string.IsNullOrEmpty(text2))
		{
			text2 = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		}
		string[] array = new string[2] { "Desktop", "桌面" };
		foreach (string text3 in array)
		{
			string text4 = PPath.Join(text2, text3);
			if (Directory.Exists(text4))
			{
				return text4;
			}
		}
		return PPath.Norm(text2);
	}

	public static List<string> LauncherCommand()
	{
		string fullPath = Path.GetFullPath(ExePath());
		string text = Path.Combine(Path.GetDirectoryName(fullPath), Utils.IS_WINDOWS ? "TipCL-CLI.exe" : "TipCL-CLI");
		return new List<string> { File.Exists(text) ? text : fullPath };
	}

	public static List<string> LaunchArgs(object instance, object version, object username = null, object account = null)
	{
		List<string> list = new List<string>
		{
			"-i",
			Py.Str(instance),
			"launch",
			Py.Str(version)
		};
		if (Py.Truthy(account))
		{
			list.AddRange(new string[2]
			{
				"--account",
				Py.Str(account)
			});
		}
		else if (Py.Truthy(username))
		{
			list.AddRange(new string[2]
			{
				"--username",
				Py.Str(username)
			});
		}
		return list;
	}

	private static string _quote(string arg)
	{
		if (!arg.Contains(" ") && arg.Length != 0)
		{
			return arg;
		}
		return "\"" + arg + "\"";
	}

	private static string _ps_literal(string t)
	{
		return "'" + t.Replace("'", "''") + "'";
	}

	private static string _create_windows(string name, string target, string arguments, string workdir, string icon, string description)
	{
		string text = PPath.Join(DesktopDir(), name + ".lnk");
		List<string> list = new List<string>
		{
			"$ErrorActionPreference = 'Stop'",
			"$sh = New-Object -ComObject WScript.Shell",
			"$sc = $sh.CreateShortcut(" + _ps_literal(text) + ")",
			"$sc.TargetPath = " + _ps_literal(target),
			"$sc.Arguments = " + _ps_literal(arguments),
			"$sc.WorkingDirectory = " + _ps_literal(workdir),
			"$sc.Description = " + _ps_literal(description)
		};
		if (icon.Length > 0)
		{
			list.Add("$sc.IconLocation = " + _ps_literal(icon));
		}
		list.Add("$sc.Save()");
		string text2 = Path.Combine(Path.GetTempPath(), "tipcl_shortcut_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".ps1");
		try
		{
			File.WriteAllText(text2, string.Join("\n", list), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			using Process process = Process.Start(new ProcessStartInfo("powershell", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + text2 + "\"")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardError = true,
				RedirectStandardOutput = true
			});
			Task<string> task = process.StandardOutput.ReadToEndAsync();
			string text3 = process.StandardError.ReadToEnd();
			process.WaitForExit();
			if (process.ExitCode != 0)
			{
				string text4 = Py.Strip((text3.Length > 0) ? text3 : task.Result);
				throw new ShortcutError((text4.Length > 0) ? Py.Slice(text4, 0, 400) : "PowerShell 创建快捷方式失败");
			}
		}
		finally
		{
			try
			{
				File.Delete(text2);
			}
			catch (Exception)
			{
			}
		}
		if (!File.Exists(PPath.Abs(text)))
		{
			throw new ShortcutError("快捷方式未生成: " + text);
		}
		return text;
	}

	/// <summary>
	/// B2c3:原来这里是**空方法** —— 生成的 .command / .desktop 从来没有可执行位,
	/// mac 上双击 .command 会被系统当成普通文本(或直接报没有执行权限)。
	/// 复用同程序集里已有的 JavaRuntimeFiles.SetExecutable:非 Windows 设 0755,
	/// Windows 上第一句就返回(Windows 生成的是 .lnk,走 _create_windows,根本不经过这里)。
	/// Linux 的 .desktop 因此也第一次拿到执行位 —— 任务书接受,已记入 PROGRESS.md。
	/// </summary>
	private static void Chmod755(string path)
	{
		JavaRuntimeFiles.SetExecutable(path);
	}

	private static string _create_linux(string name, string target, string arguments, string workdir, string icon, string description)
	{
		string text = PPath.Join(DesktopDir(), name + ".desktop");
		List<string> list = new List<string>
		{
			"[Desktop Entry]",
			"Type=Application",
			"Name=" + name,
			"Comment=" + description,
			"Exec=" + Py.Strip(_quote(target) + " " + arguments),
			"Path=" + workdir,
			"Terminal=false",
			"Categories=Game;"
		};
		if (icon.Length > 0)
		{
			list.Add("Icon=" + icon);
		}
		File.WriteAllText(PPath.Abs(text), string.Join("\n", list) + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		Chmod755(PPath.Abs(text));
		return text;
	}

	/// <summary>
	/// B2c3 新接缝(纯函数):拼 macOS 上双击即用的 .command 脚本正文。三条要点:
	/// 1. description 里有用户可控文本(实例名 / 版本号),其中的 CR / LF 一律换成空格 ——
	///    注释只占一行,不能让换行把后面的命令顶出注释;
	/// 2. workdir / target / 每个参数都走 PosixShell.Quote 的单引号规则,
	///    用户可控文本里带单引号、$、反引号、双引号、分号、空格都不可能跳出引号;
	/// 3. 行尾只有 LF、最后有一个换行(写盘的编码由调用方保证:UTF-8 无 BOM)。
	/// </summary>
	internal static string MacCommandScript(string description, string workdir, string target, IEnumerable<string> args)
	{
		List<string> list = new List<string>
		{
			"#!/bin/sh",
			"# " + _one_line(description),
			"cd " + PosixShell.Quote(workdir)
		};
		List<string> list2 = new List<string> { target };
		if (args != null)
		{
			list2.AddRange(args);
		}
		list.Add("exec " + PosixShell.JoinCommand(list2));
		return string.Join("\n", list) + "\n";
	}

	/// <summary>把任意文本压成单行:CRLF / CR / LF 三种换行都换成空格(null 按空串)。</summary>
	private static string _one_line(string text)
	{
		return (text ?? "").Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
	}

	/// <summary>
	/// macOS 分支。第七个参数 rawArgs 是本包新加的:上面那份预拼接的 arguments 走的是
	/// cmd 风格的 _quote,在 sh 里不安全,所以 mac 分支要原始参数表、自己按 POSIX 规则加引号。
	/// arguments 参数**有意不用**(Windows / Linux 两个分支拿到的仍是逐字相同的值)。
	/// </summary>
	private static string _create_macos(string name, string target, string arguments, string workdir, string icon, string description, IEnumerable<string> rawArgs)
	{
		string text = PPath.Join(DesktopDir(), name + ".command");
		File.WriteAllText(PPath.Abs(text), MacCommandScript(description, workdir, target, rawArgs), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		Chmod755(PPath.Abs(text));
		return text;
	}

	public static string CreateLaunchShortcut(object instance, object version, object username = null, object account = null, object name = null)
	{
		if (!Py.Truthy(version))
		{
			throw new ShortcutError("请先选择要创建快捷方式的版本");
		}
		List<string> list = LauncherCommand();
		string target = list[0];
		// B2c3:先落成 List —— Windows / Linux 两个分支照旧用预拼接的 arguments(值与改动前
		// 逐字相同),macOS 分支另外拿到**没拼接过**的原始参数表,自己按 POSIX 规则加引号。
		List<string> rawArgs = list.Skip(1).Concat(LaunchArgs(instance, version, username, account)).ToList();
		string arguments = string.Join(" ", rawArgs.Select(_quote));
		string name2 = SafeFilename(Py.Truthy(instance) ? Py.Or(name, Py.Str(version) + " - " + Py.Str(instance)) : version);
		string icon = "";
		string[] array = new string[2]
		{
			Path.Combine(_PROJECT_ROOT, "icon.ico"),
			ExePath()
		};
		foreach (string text in array)
		{
			if (Utils.IS_WINDOWS && File.Exists(text))
			{
				icon = text;
				break;
			}
		}
		string description = "TipCL 启动 " + Py.Str(Py.Or(instance, "default")) + " / " + Py.Str(version);
		string pROJECT_ROOT = _PROJECT_ROOT;
		if (Utils.IS_WINDOWS)
		{
			return _create_windows(name2, target, arguments, pROJECT_ROOT, icon, description);
		}
		if (Utils.IS_MAC)
		{
			return _create_macos(name2, target, arguments, pROJECT_ROOT, icon, description, rawArgs);
		}
		return _create_linux(name2, target, arguments, pROJECT_ROOT, icon, description);
	}

	public static bool RemoveLaunchShortcut(object name)
	{
		string text = SafeFilename(name);
		string text2 = (Utils.IS_WINDOWS ? ".lnk" : (Utils.IS_MAC ? ".command" : ".desktop"));
		string path = PPath.Abs(PPath.Join(DesktopDir(), text + text2));
		if (File.Exists(path))
		{
			File.Delete(path);
			return true;
		}
		return false;
	}
}
