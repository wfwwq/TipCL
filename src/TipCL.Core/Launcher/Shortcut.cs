// 来源: Windows 版参考仓库 src/TipCL/Core/Launcher/Shortcut.cs(M4-H P3.5 搬运)。
// Linux 侧改动(见 DECISIONS D-23):
//   1. Windows 的 _create_windows(PowerShell + WScript.Shell 写 .lnk)不搬 —— 它是「本机平台不适用」的分支,
//      Linux 版只保留 .desktop / .command 两条;
//   2. Windows 参考版**自带**的 _create_linux 分支有两个缺陷已修:1) Chmod755 是空方法,写出来的
//      .desktop 没有可执行位;2) workdir / icon 直接沿用 Windows 反斜杠路径进 Path= / Icon= 键。
//      另外补 XDG_DESKTOP_DIR(桌面重定向 / 中文桌面目录)与程序化生成的图标。
// 参考版的 `-i <实例> launch <版本>` 参数在整个 Windows 工程里**没有任何消费者**(App.xaml.cs 只认自检开关),
// 即双击快捷方式只开启动器 —— Linux 侧由 TipCL.App/Session/CommandLine.cs 真的把它做通。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using TipCL.Core;

namespace TipCL.Core.Launcher;

/// <summary>桌面快捷方式:生成 / 删除「双击直接启动某个实例的某个版本」的启动项。</summary>
public static class Shortcut
{
	private const string _BAD_NAME_CHARS = "\\/:*?\"<>|";

	/// <summary>.desktop 文案前缀(中文,与 Windows 版 description 同义)。</summary>
	private const string DescriptionPrefix = "TipCL 启动 ";

	/// <summary>可执行文件自身路径(与 Updater 同款取法:Environment.ProcessPath 优先,MainModule 兜底)。</summary>
	private static string ExePath()
	{
		string p = Environment.ProcessPath;
		if (!string.IsNullOrEmpty(p))
		{
			return p;
		}
		try
		{
			return Process.GetCurrentProcess().MainModule?.FileName ?? "";
		}
		catch (Exception)
		{
			return "";
		}
	}

	/// <summary>启动器可执行文件所在目录(`Path=` 键与图标查找的基准)。</summary>
	public static string ProjectRoot()
	{
		string exe = ExePath();
		if (string.IsNullOrEmpty(exe))
		{
			return Utils.ROOT;
		}
		string dir = Path.GetDirectoryName(Path.GetFullPath(exe)) ?? "";
		return dir.Length == 0 ? Utils.ROOT : dir;
	}

	public static string SafeFilename(object name)
	{
		StringBuilder stringBuilder = new StringBuilder();
		string text = Py.Strip(Py.Str(Py.Or(name, "")));
		foreach (char c in text)
		{
			stringBuilder.Append((_BAD_NAME_CHARS.IndexOf(c) >= 0) ? '_' : c);
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
		if (WinPaths)
		{
			try
			{
				string special = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
				if (!string.IsNullOrEmpty(special) && Directory.Exists(special))
				{
					return special;
				}
			}
			catch (Exception)
			{
			}
		}
		string home = WinPaths
			? Environment.GetEnvironmentVariable("USERPROFILE")
			: Environment.GetEnvironmentVariable("HOME");
		if (string.IsNullOrEmpty(home))
		{
			home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		}

		// Linux:XDG 桌面目录(桌面重定向 / 非英文桌面目录名)
		if (!WinPaths)
		{
			string xdg = XdgDesktopDir(home);
			if (!string.IsNullOrEmpty(xdg) && Directory.Exists(xdg))
			{
				return PPath.Norm(xdg);
			}
		}

		string[] candidates = (WinPaths || Utils.IS_MAC)
			? new string[1] { "Desktop" }
			: new string[4] { "Desktop", "桌面", "デスクトップ", "바탕 화면" };
		foreach (string name in candidates)
		{
			string candidate = PPath.Join(home, name);
			if (Directory.Exists(candidate))
			{
				return candidate;
			}
		}
		return PPath.Norm(home);
	}

	/// <summary>读 XDG 桌面目录:`$XDG_DESKTOP_DIR` → `~/.config/user-dirs.dirs` → 空(交给调用方回落)。</summary>
	private static string XdgDesktopDir(string home)
	{
		string env = Environment.GetEnvironmentVariable("XDG_DESKTOP_DIR");
		if (!string.IsNullOrEmpty(env))
		{
			return env;
		}
		string configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
		if (string.IsNullOrEmpty(configHome))
		{
			if (string.IsNullOrEmpty(home))
			{
				return "";
			}
			configHome = PPath.Join(home, ".config");
		}
		string file = PPath.Join(configHome, "user-dirs.dirs");
		if (!File.Exists(file))
		{
			return "";
		}
		try
		{
			foreach (string raw in File.ReadAllLines(file))
			{
				string line = Py.Strip(raw);
				if (!line.StartsWith("XDG_DESKTOP_DIR", StringComparison.Ordinal))
				{
					continue;
				}
				int eq = line.IndexOf('=');
				if (eq < 0)
				{
					continue;
				}
				string val = Py.Strip(line[(eq + 1)..], "\"");
				if (val.StartsWith("$HOME", StringComparison.Ordinal))
				{
					val = PPath.Join(home, val.Substring("$HOME".Length));
				}
				return val;
			}
		}
		catch (Exception)
		{
			// 配置文件读不动就按默认候选目录走
		}
		return "";
	}

	/// <summary>启动器的命令行(照 Windows 版:优先同级 TipCL-CLI,否则自身)。</summary>
	public static List<string> LauncherCommand()
	{
		string exe = Path.GetFullPath(ExePath());
		string cli = Path.Combine(Path.GetDirectoryName(exe) ?? "", Utils.IS_WINDOWS ? "TipCL-CLI.exe" : "TipCL-CLI");
		return new List<string> { File.Exists(cli) ? cli : exe };
	}

	/// <summary>启动参数:**必须与 CommandLine.Parse 的接受形状严格一致**(`-i &lt;实例&gt; launch &lt;版本&gt; [--account X|--username Y]`)。</summary>
	public static List<string> LaunchArgs(object instance, object version, object? username = null, object? account = null)
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

	/// <summary>
	/// 是否走 Windows 路径(view 名 / DesktopDir / 快捷方式后缀/ 扩展名)。
	/// 默认跟随 <see cref="M1:TipCL.Core.Launcher.Utils.IS_WINDOWS"/>;Windows 开发机上跑本工程的无头测试时,
	/// 可用环境变量 `TIPCL_SHORTCUT_POSIX=1` 强制走 Linux 分支,以便断言 .desktop 内容(交付目标是 Linux)。
	/// </summary>
	private static bool WinPaths
		=> Utils.IS_WINDOWS && Environment.GetEnvironmentVariable("TIPCL_SHORTCUT_POSIX") != "1";

	/// <summary>`.desktop` 换行约定:键值里不能出现裸换行,统一替换成空格。</summary>
	private static string Line(string value) => Py.Str(value).Replace("\r", " ").Replace("\n", " ");

	/// <summary>Unix 路径规整:Windows 反斜杠路径写进 .desktop 是无效路径(参考版的缺陷之一)。</summary>
	private static string UnixPath(string path)
		=> WinPaths ? path : path.Replace('\\', '/');

	private static void Chmod755(string path)
	{
		if (OperatingSystem.IsWindows())
		{
			return;
		}
		try
		{
			string abs = PPath.Abs(path);
			File.SetUnixFileMode(abs, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
				| UnixFileMode.GroupRead | UnixFileMode.GroupExecute
				| UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
		}
		catch (Exception)
		{
			// 文件系统不支持权限位(如挂载的 FAT):不影响 .desktop 本身
		}
	}

	/// <summary>
	/// 写 Linux .desktop(照参考版 _create_linux,Exec 行参数由 LaunchArgs 产出)。
	/// 生成后按 File.Exists 复核,写不出来直接抛 ShortcutError(参考版 Windows 分支的同一约定)。
	/// </summary>
	private static string _create_linux(string name, string target, string arguments, string workdir, string icon, string description)
	{
		string path = PPath.Join(DesktopDir(), name + ".desktop");
		var lines = new List<string>
		{
			"[Desktop Entry]",
			"Type=Application",
			"Name=" + Line(name),
			"Comment=" + Line(description),
			// 目标可执行文件也过一遍路径规整:Linux 版在 Windows 上跑(测试/交叉验证)时,
			// ProcessPath 是反斜杠路径,写进 Exec= 后在 Linux 上不是可执行路径
			"Exec=" + Py.Strip(Quote(UnixPath(target)) + " " + arguments),
			"Path=" + UnixPath(workdir),
			"Terminal=false",
			"Categories=Game;"
		};
		if (icon.Length > 0)
		{
			lines.Add("Icon=" + UnixPath(icon));
		}
		Directory.CreateDirectory(PPath.Abs(new PPath(path).Parent.ToString()));
		File.WriteAllText(PPath.Abs(path), string.Join("\n", lines) + "\n", new UTF8Encoding(false));
		Chmod755(PPath.Abs(path));
		if (!File.Exists(PPath.Abs(path)))
		{
			throw new ShortcutError("快捷方式未生成: " + path);
		}
		return path;
	}

	/// <summary>macOS 的 .command(package 内嵌的启动脚本,双击可用)。</summary>
	private static string _create_macos(string name, string target, string arguments, string workdir, string icon, string description)
	{
		string path = PPath.Join(DesktopDir(), name + ".command");
		var lines = new List<string>
		{
			"#!/bin/sh",
			"# " + Line(description),
			"cd " + Quote(workdir) + " || exit 1",
			"exec " + Quote(target) + " " + arguments,
			""
		};
		Directory.CreateDirectory(PPath.Abs(new PPath(path).Parent.ToString()));
		File.WriteAllText(PPath.Abs(path), string.Join("\n", lines), new UTF8Encoding(false));
		Chmod755(PPath.Abs(path));
		if (!File.Exists(PPath.Abs(path)))
		{
			throw new ShortcutError("快捷方式未生成: " + path);
		}
		return path;
	}

	/// <summary>shell / .desktop 双引号引用(照参考版 _quote:只在含空格或为空时加引号)。</summary>
	private static string Quote(string arg)
	{
		if (!arg.Contains(" ") && arg.Length != 0)
		{
			return arg;
		}
		return "\"" + arg + "\"";
	}

	private static string _create_unsupported()
	{
		throw new ShortcutError(I18nCore.Tr("当前平台不支持创建桌面快捷方式"));
	}

	/// <summary>
	/// 创建「直接启动某实例某版本」的快捷方式,返回快捷方式绝对路径(照 Windows 版 create_desktop_shortcut)。
	/// </summary>
	public static string CreateLaunchShortcut(object instance, object version, object? username = null, object? account = null, object? name = null)
	{
		if (!Py.Truthy(version))
		{
			throw new ShortcutError(I18nCore.Tr("请先选择要创建快捷方式的版本"));
		}
		List<string> command = LauncherCommand();
		string target = command[0];
		string arguments = string.Join(" ", command.Skip(1).Concat(LaunchArgs(instance, version, username, account)).Select(Quote));
		string safeName = SafeFilename(Py.Truthy(instance)
			? Py.Or(name, Py.Str(version) + " - " + Py.Str(instance))
			: version);
		string description = DescriptionPrefix + Py.Str(Py.Or(instance, "default")) + " / " + Py.Str(version);
		string workdir = ProjectRoot();
		string icon = ShortcutIcon.Ensure();
		if (Utils.IS_MAC)
		{
			return _create_macos(safeName, target, arguments, workdir, icon, description);
		}
		if (WinPaths)
		{
			// Windows 参考实现的 PowerShell/.lnk 分支不搬(见文件头);本分支只用于「在 Windows 上跑 Linux 版」时给出明确报错
			return _create_unsupported();
		}
		return _create_linux(safeName, target, arguments, workdir, icon, description);
	}

	/// <summary>删除快捷方式(照 remove_desktop_shortcut);不存在返回 false。</summary>
	public static bool RemoveLaunchShortcut(object? name)
	{
		string safe = SafeFilename(Py.Or(name, ""));
		if (WinPaths)
		{
			return false;
		}
		string path = PPath.Abs(PPath.Join(DesktopDir(), safe + ShortcutExt()));
		if (File.Exists(path))
		{
			File.Delete(path);
			return true;
		}
		return false;
	}

	/// <summary>快捷方式默认文件名(不给 name 时用的那个),供 UI 的「删除」与测试共用。</summary>
	public static string ShortcutName(object? instance, object? version)
		=> SafeFilename(Py.Truthy(instance) ? Py.Str(version) + " - " + Py.Str(instance) : version);

	/// <summary>按扩展名取快捷方式文件后缀(.desktop / .command)。</summary>
	private static string ShortcutExt() => Utils.IS_MAC ? ".command" : ".desktop";

	/// <summary>快捷方式文件路径(不判断存在性)。</summary>
	public static string ShortcutPath(object? instance, object? version)
		=> ShortcutPathForName(ShortcutName(instance, version));

	/// <summary>按快捷方式文件名(不带扩展名)取路径。</summary>
	public static string ShortcutPathForName(object? name)
		=> PPath.Join(DesktopDir(), SafeFilename(Py.Or(name, "")) + ShortcutExt());

	/// <summary>该实例/版本的快捷方式是否已经存在(UI 据此把菜单项在「创建 / 删除」之间切换)。</summary>
	public static bool Exists(object? instance, object? version)
	{
		if (WinPaths)
		{
			return false;
		}
		string path = PPath.Abs(ShortcutPath(instance, version));
		return File.Exists(path);
	}
}
