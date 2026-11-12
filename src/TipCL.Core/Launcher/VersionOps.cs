using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TipCL.Core.Launcher;

public static class VersionOps
{
	private static readonly Regex _ILLEGAL = new Regex("[\\\\/:*?\"<>|\\x00-\\x1f]");

	public static string SanitizeId(object raw)
	{
		string input = _ILLEGAL.Replace(Py.SS(raw), "-");
		input = Py.Strip(Regex.Replace(input, "\\s+", " "), " .");
		if (input.Length == 0)
		{
			throw new VersionOpError("版本名不能为空");
		}
		if (input.Length > 64)
		{
			input = Py.RStrip(input.Substring(0, 64), " .");
		}
		return input;
	}

	public static string UniqueId(Instance instance, object raw)
	{
		string text = SanitizeId(raw);
		string text2 = instance.VersionsDir();
		string text3 = text;
		int num = 2;
		while (LocalFs.Exists(PPath.Join(text2, text3)))
		{
			string text4 = "-" + num;
			string text5 = Py.RStrip(text.Substring(0, Math.Min(text.Length, Math.Max(1, 64 - text4.Length))), " .");
			if (text5.Length == 0)
			{
				text5 = text;
			}
			text3 = text5 + text4;
			num++;
		}
		return text3;
	}

	private static string _vdir(Instance instance, object version_id)
	{
		string text;
		try
		{
			text = Utils.SafeChildPath(instance.VersionsDir(), Utils.SafeVersionId(version_id));
		}
		catch (PathEscapeError pathEscapeError)
		{
			throw new VersionOpError(pathEscapeError.Message);
		}
		if (!LocalFs.IsDir(text))
		{
			throw new VersionOpError(Py.Fmt("版本不存在: {0}", version_id));
		}
		return text;
	}

	private static string _checked_id(object raw)
	{
		try
		{
			return Utils.SafeVersionId(raw);
		}
		catch (PathEscapeError pathEscapeError)
		{
			throw new VersionOpError(pathEscapeError.Message);
		}
	}

	public static string RenameVersion(Instance instance, object old_id0, object new_id0)
	{
		string text = SanitizeId(new_id0);
		string text2 = _checked_id(old_id0);
		if (text == text2)
		{
			return text2;
		}
		string text3 = _vdir(instance, text2);
		string text4 = PPath.Join(instance.VersionsDir(), text);
		if (LocalFs.Exists(text4))
		{
			throw new VersionOpError(Py.Fmt("已存在版本 {0}", text));
		}
		LocalFs.CopyTree(text3, text4);
		try
		{
			_rewrite_ids(text4, text2, text);
			Utils.RemoveTree(text3);
			return text;
		}
		catch (Exception)
		{
			Utils.RemoveTree(text4);
			throw;
		}
	}

	public static string CopyVersion(Instance instance, object old_id, object new_id0)
	{
		string text = SanitizeId(new_id0);
		string src = _vdir(instance, old_id);
		string text2 = PPath.Join(instance.VersionsDir(), text);
		if (LocalFs.Exists(text2))
		{
			throw new VersionOpError(Py.Fmt("已存在版本 {0}", text));
		}
		LocalFs.CopyTree(src, text2);
		_rewrite_ids(text2, Py.Str(old_id), text);
		return text;
	}

	private static void _rewrite_ids(string dest, string old_id, string new_id)
	{
		foreach (string item in LocalFs.Children(dest))
		{
			PPath pPath = new PPath(item);
			if (LocalFs.IsFile(item) && pPath.Stem == old_id)
			{
				File.Move(PPath.Abs(item), PPath.Abs(PPath.Join(dest, new_id + pPath.Suffix)));
			}
		}
		string text = PPath.Join(dest, new_id + ".json");
		if (!LocalFs.IsFile(text))
		{
			string text2 = PPath.Join(dest, old_id + ".json");
			if (LocalFs.IsFile(text2))
			{
				File.Move(PPath.Abs(text2), PPath.Abs(text));
			}
		}
		if (Utils.ReadJson(text) is PyDict pyDict)
		{
			pyDict["id"] = new_id;
			Utils.WriteJson(text, pyDict);
		}
		string text3 = PPath.Join(dest, "tipcl.json");
		if (LocalFs.IsFile(text3))
		{
			object obj = Utils.ReadJson(text3, new PyDict());
			object obj2 = (Py.Truthy(obj) ? obj : new PyDict());
			((obj2 as PyDict) ?? throw new AttributeError("'" + Py.TypeName(obj2) + "' object has no attribute 'pop'")).Pop("hidden");
			Utils.WriteJson(text3, obj2);
		}
	}

	public static PyDict SetHidden(Instance instance, object version_id, object hidden)
	{
		_vdir(instance, version_id);
		return VersionSettings.Save(instance, version_id, PyDict.Of("hidden", Py.Truthy(hidden)));
	}

	public static bool IsHidden(Instance instance, object version_id)
	{
		return Py.Truthy(VersionSettings.Load(instance, version_id)["hidden"]);
	}

	public static PyDict FolderMap(Instance instance, object version_id0 = null)
	{
		object obj = version_id0;
		if (Py.Truthy(obj))
		{
			obj = _checked_id(obj);
		}
		bool flag = Py.Truthy(obj);
		PyDict settings = (flag ? VersionSettings.Load(instance, obj) : null);
		string text = (flag ? VersionSettings.GameDir(instance, obj, settings) : instance.path);
		string text2 = (flag ? PPath.Join(instance.VersionsDir(), obj) : instance.VersionsDir());
		return PyDict.Of("root", instance.path, "game", text, "mods", flag ? VersionSettings.ModsDir(instance, obj, settings) : PPath.Join(instance.path, "mods"), "saves", PPath.Join(text, "saves"), "screenshots", PPath.Join(text, "screenshots"), "resourcepacks", PPath.Join(text, "resourcepacks"), "shaderpacks", PPath.Join(text, "shaderpacks"), "datapacks", PPath.Join(text, "datapacks"), "logs", PPath.Join(text, "logs"), "crash-reports", PPath.Join(text, "crash-reports"), "version", text2);
	}

	public static string OpenFolder(Instance instance, object version_id = null, object which = null)
	{
		PyDict pyDict = FolderMap(instance, version_id);
		if (which is PyDict || which is List<object>)
		{
			throw new TypeError("unhashable type: '" + Py.TypeName(which) + "'");
		}
		string text = (string)Py.Or((which is string key) ? pyDict[key] : null, pyDict["root"]);
		PPath pPath = new PPath(instance.path).Resolve();
		PPath pPath2 = new PPath(text).Resolve();
		if (!pPath2.Equals(pPath) && !pPath2.IsRelativeTo(pPath.ToString()))
		{
			throw new VersionOpError(Py.Fmt("非法路径: {0}", text));
		}
		Utils.EnsureDir(text);
		if (!Crash.OpenPath(text))
		{
			throw new VersionOpError(Py.Fmt("无法打开: {0}", text));
		}
		return text;
	}

	public static string ExportLaunchBat(string dest, IEnumerable<object> cmd, object cwd)
	{
		Utils.EnsureDir(new PPath(dest).Parent.ToString());
		List<string> list = new List<string>
		{
			"@echo off",
			"chcp 65001 >nul",
			"cd /d \"" + Py.Str(cwd) + "\""
		};
		List<string> list2 = new List<string>();
		foreach (object item in cmd)
		{
			string text = Py.Str(item);
			if (text.IndexOfAny(" \t&|<>^\"".ToCharArray()) >= 0)
			{
				list2.Add("\"" + text.Replace("\"", "\\\"") + "\"");
			}
			else
			{
				list2.Add(text);
			}
		}
		list.Add(string.Join(" ", list2));
		list.Add("pause");
		string text2 = string.Join("\r\n", list) + "\r\n";
		if (Utils.IS_WINDOWS)
		{
			text2 = text2.Replace("\n", "\r\n");
		}
		File.WriteAllBytes(PPath.Abs(dest), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text2));
		return new PPath(dest).ToString();
	}

	/// <summary>
	/// B2c3 新接缝(纯函数):导出启动脚本的扩展名。Windows 是 .bat,
	/// macOS 用 .command(Terminal 双击即执行,与 Shortcut 的 mac 分支保持同一选择),
	/// 其余平台用最通用的 .sh。
	/// </summary>
	internal static string ExportScriptExtension(string os)
	{
		if (os == "windows")
		{
			return ".bat";
		}
		if (os == "osx")
		{
			return ".command";
		}
		return ".sh";
	}

	/// <summary>
	/// B2c3 新接缝(纯函数):非 Windows 的启动脚本正文。与 ExportLaunchBat 的差别都是**有意**的:
	/// 没有 chcp(那是 cmd 的事)、没有 pause(双击 .command 时 Terminal 会自己停在窗口里,
	/// 而且退出码要能正常返回)、没有 exec(让 shell 等命令跑完并把退出码交回 Terminal)、
	/// 每个参数各自按 POSIX 规则加单引号、换行一律 LF、结尾有换行。
	/// 不加任何 JVM 参数、不加 arch 前缀 —— 命令表由调用方(McLauncher)拼好原样传进来。
	/// </summary>
	internal static string PosixLaunchScript(IEnumerable<object> cmd, object cwd)
	{
		List<string> list = new List<string> { "#!/bin/sh", "cd " + PosixShell.Quote(Py.Str(cwd)) };
		List<string> list2 = new List<string>();
		if (cmd != null)
		{
			foreach (object item in cmd)
			{
				list2.Add(Py.Str(item));
			}
		}
		list.Add(PosixShell.JoinCommand(list2));
		return string.Join("\n", list) + "\n";
	}

	/// <summary>
	/// B2c3:导出启动脚本的统一入口。Windows 走原来的 ExportLaunchBat(**一个字节都没动**,
	/// 返回值也是它原来那个),其余平台写 PosixLaunchScript 的正文、UTF-8 无 BOM,
	/// 并按上面的扩展名约定补一次可执行位(失败只记警告,不让导出失败),返回值形式与
	/// ExportLaunchBat 一致(PPath 规范化后的路径)。
	/// </summary>
	internal static string ExportLaunchScript(string dest, IEnumerable<object> cmd, object cwd, string os)
	{
		if (os == "windows")
		{
			return ExportLaunchBat(dest, cmd, cwd);
		}
		Utils.EnsureDir(new PPath(dest).Parent.ToString());
		string text = PosixLaunchScript(cmd, cwd);
		File.WriteAllBytes(PPath.Abs(dest), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text));
		JavaRuntimeFiles.SetExecutable(PPath.Abs(dest));
		return new PPath(dest).ToString();
	}

	/// <summary>
	/// B2c3 新接缝(纯函数对注入的 loader 而言):导出启动脚本时交给 Java 挑选的那个版本 JSON。
	/// Windows 上**原样返回 childJson**(与改动前同一个对象,loader 一次都不调,行为逐字不变);
	/// 非 Windows 上先合并继承链 —— 因为 ResolveLaunchJava 要按最终生效的版本 JSON 判架构,
	/// 传未合并的 child 会让 Forge 这类 inheritsFrom 的版本挑错 Java(B2e 的 Lead 裁决)。
	/// 合并抛异常(缺父版本 / 继承环)时退回 childJson,让导出至少能出脚本,由 Java 挑选自己兜底。
	/// </summary>
	internal static PyDict ExportVersionJson(PyDict childJson, Func<string, PyDict> loadParent, bool isWindows)
	{
		if (isWindows)
		{
			return childJson;
		}
		try
		{
			return Manifest.ResolveInherits(childJson, loadParent);
		}
		catch (Exception)
		{
			return childJson;
		}
	}
}
