using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TipCL.Core.Launcher;

/// <summary>
/// 一个版本在 mac 上有多少 arm64 原生库。
/// </summary>
public enum MacNativeKind
{
	/// <summary>这份版本 JSON 里没有任何 mac 原生库(例如只给 windows / linux 提供 natives)。</summary>
	NoMacNatives,

	/// <summary>有 mac 原生库,但只有 x86_64(没有任何 arm64 变体)。</summary>
	X64Only,

	/// <summary>mac 原生库全都有 arm64 变体(Apple 芯片可以直接跑)。</summary>
	Arm64Native,

	/// <summary>一部分库有 arm64、一部分只有 x86_64(见 <see cref="MacNativeSupportReport.LibrariesWithoutArm64"/>)。</summary>
	Mixed
}

/// <summary>
/// <see cref="MacNativeSupport.Analyze(PyDict)"/> 的返回值。
/// </summary>
public sealed class MacNativeSupportReport
{
	/// <summary>判定结论。</summary>
	public MacNativeKind Kind { get; }

	/// <summary>本报告认为「与 mac 有关」的原生库名(已按 rules 过滤,见 <see cref="MacNativeSupport"/> 的注释)。</summary>
	public IReadOnlyList<string> MacNativeLibraries { get; }

	/// <summary>有 arm64 变体的库名。</summary>
	public IReadOnlyList<string> LibrariesWithArm64 { get; }

	/// <summary>只有 x86_64 变体的库名。</summary>
	public IReadOnlyList<string> LibrariesWithoutArm64 { get; }

	internal MacNativeSupportReport(MacNativeKind kind, IReadOnlyList<string> all, IReadOnlyList<string> withArm64, IReadOnlyList<string> withoutArm64)
	{
		Kind = kind;
		MacNativeLibraries = all;
		LibrariesWithArm64 = withArm64;
		LibrariesWithoutArm64 = withoutArm64;
	}

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(Kind).Append(" macNatives=").Append(MacNativeLibraries.Count);
		stringBuilder.Append(" withArm64=").Append(LibrariesWithArm64.Count);
		stringBuilder.Append(" withoutArm64=").Append(LibrariesWithoutArm64.Count);
		if (LibrariesWithoutArm64.Count > 0)
		{
			stringBuilder.Append(" [").Append(string.Join(", ", LibrariesWithoutArm64)).Append(']');
		}
		return stringBuilder.ToString();
	}
}

/// <summary>
/// B2b:纯函数 —— 「这个版本在 mac 上有没有 arm64 原生库」。本包**不接入任何调用点**,
/// 只给 Lead 裁决「Apple 芯片上没有 arm64 原生库的版本怎么跑」提供依据(见 PROGRESS.md 的 B2b 第 5 项)。
///
/// 输入是**合并后的**版本 JSON(<see cref="Manifest.Merge"/> 之后,即 inheritsFrom 已展开),
/// 因为父版本的 libraries 也要算进来。
///
/// 判定规则(以 2026-11-12 19:27 线上取到的 8 份版本 JSON 为准,响应留在 <c>artifacts/b2b/</c>,不入库):
/// <list type="number">
/// <item>只看「与 mac 有关」的库:先按 <c>rules</c> 用注入的 os / arch 过滤(vanilla 这 8 份 JSON 的
/// libraries 里**没有任何** <c>os.arch</c> 规则,实测 OS_ARCH_RULE_HITS = 0,所以 arch 参数目前不影响结论);
/// 再看这个库有没有 mac 原生分类器 —— 三种写法都算:
///   老式 <c>natives: {osx: natives-osx}</c>(可带 <c>${arch}</c>)、
///   <c>downloads.classifiers</c> 里以 <c>natives-osx</c> / <c>natives-macos</c> 开头的键、
///   以及新式「库名第 4 段 classifier」如 <c>org.lwjgl:lwjgl:3.3.1:natives-macos-arm64</c>。</item>
/// <item>分类器后缀带 arm64 / aarch64 的算 arm64 变体;<c>natives-macos-arm64</c> 是 1.19.4 与 26.3 里的写法。</item>
/// <item>没有后缀的老式分类器(<c>natives-osx</c> / <c>natives-macos</c>)算 **x86_64**:
/// 实测这些版本(1.7.10 / 1.8.9 / 1.12.2 / 1.13.2 / 1.16.5 / 1.18.2)的 JSON 里
/// 连一个 arm64 字样都没有,对应的 LWJGL 2.9 / 3.1.6 / 3.2.x 在 mac 上只发 x86_64 包。
/// 这是保守判定:如果真机上发现某个老包其实是 universal 二进制,这个函数要改。</item>
/// <item>结论:<c>NoMacNatives</c>(没有 mac 原生库)/ <c>X64Only</c>(全部只有 x86_64)/
/// <c>Arm64Native</c>(全部都有 arm64)/ <c>Mixed</c>(两者都有,附缺 arm64 的清单)。
/// 注意判定的是「有没有 arm64 变体」,不是「当前代码会不会选到它」——
/// 现状下 <c>Installer.SelectNativeClassifier</c> 在 arm64 上对老式键一律返回 null(见该类注释)。
/// 报告里的库名按 <c>group:artifact</c> 归组:1.19.4 起 <c>natives-macos</c> 与
/// <c>natives-macos-arm64</c> 是**两条独立的 library 条目**(库名第 4 段才是 classifier),
/// 只有按 group:artifact 归组才能回答「这个原生库有没有 arm64 版本」。</item>
/// </list>
/// </summary>
public static class MacNativeSupport
{
	/// <summary>针对 Apple 芯片(osx + arm64)判定。</summary>
	public static MacNativeSupportReport Analyze(PyDict version_json)
	{
		return Analyze(version_json, "osx", "arm64", Utils.OS_VERSION);
	}

	/// <summary>
	/// 可注入 os / arch / osVersion 的重载。os 传 <c>osx</c> 时 mac 的分类器别名是
	/// <c>osx</c> 与 <c>macos</c> 两种(与 <see cref="Installer.SelectNativeClassifier"/> 一致)。
	/// </summary>
	internal static MacNativeSupportReport Analyze(PyDict version_json, string os, string arch, string os_version)
	{
		Dictionary<string, bool> dictionary = new Dictionary<string, bool>(StringComparer.Ordinal);
		List<string> list = new List<string>();
		foreach (PyDict item in Installer.Libs(version_json ?? new PyDict()))
		{
			if (Py.Truthy(item.Get("rules")) && !Utils.CheckRules(item.Get("rules"), null, os, arch, os_version))
			{
				continue;
			}
			List<string> list2 = _mac_native_keys(item, os, arch);
			if (list2.Count == 0)
			{
				continue;
			}
			string text = _group_key(item);
			bool flag = list2.Any((string k) => _key_arch(k) == "arm64");
			if (!dictionary.TryGetValue(text, out var value))
			{
				dictionary[text] = flag;
				list.Add(text);
			}
			else if (flag && !value)
			{
				dictionary[text] = true;
			}
		}
		List<string> list3 = new List<string>();
		List<string> list4 = new List<string>();
		foreach (string item2 in list)
		{
			if (dictionary[item2])
			{
				list3.Add(item2);
			}
			else
			{
				list4.Add(item2);
			}
		}
		MacNativeKind kind = ((list.Count == 0) ? MacNativeKind.NoMacNatives : ((list4.Count == 0) ? MacNativeKind.Arm64Native : ((list3.Count == 0) ? MacNativeKind.X64Only : MacNativeKind.Mixed)));
		return new MacNativeSupportReport(kind, list, list3, list4);
	}

	/// <summary>原生库的归组键:<c>group:artifact</c>(1.19.4 起同一原生库的 x64 / arm64 是两条独立条目)。</summary>
	private static string _group_key(PyDict lib)
	{
		string text = Py.Str(Py.Or(lib.Get("name"), ""));
		string[] array = text.Split(':');
		if (array.Length < 2)
		{
			return text;
		}
		return array[0] + ":" + array[1];
	}

	/// <summary>一个库里所有与 mac 有关的原生分类器(去重后的字符串集合,按出现顺序)。</summary>
	private static List<string> _mac_native_keys(PyDict lib, string os, string arch)
	{
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		list2.Add(os);
		if (os == "osx")
		{
			list2.Add("macos");
		}
		PyDict pyDict = Py.DictOr(Py.Or(lib.Get("natives"), new PyDict()));
		foreach (string item in list2)
		{
			object obj = pyDict.Get(item);
			if (Py.Truthy(obj))
			{
				_Add(list, Installer.SubstNativeKey(Py.AsStr(obj, "replace"), arch));
			}
		}
		PyDict pyDict2 = Py.DictOr(Py.Or(Py.DictOr(Py.Or(lib.Get("downloads"), new PyDict())).Get("classifiers"), new PyDict()));
		foreach (string key in pyDict2.Keys)
		{
			string text = Installer.SubstNativeKey(key, arch);
			if (_is_mac_key(text, list2))
			{
				_Add(list, text);
			}
		}
		string[] array = Py.Str(Py.Or(lib.Get("name"), "")).Split(':');
		if (array.Length >= 4 && _is_mac_key(array[3], list2))
		{
			_Add(list, array[3]);
		}
		return list;
	}

	private static void _Add(List<string> keys, string key)
	{
		if (!string.IsNullOrEmpty(key) && !keys.Contains(key))
		{
			keys.Add(key);
		}
	}

	/// <summary>键是不是 mac 的原生分类器(用别名集合判断,不用静态 OS_NAME)。</summary>
	private static bool _is_mac_key(string key, List<string> aliases)
	{
		foreach (string item in aliases)
		{
			if (key == "natives-" + item || key.StartsWith("natives-" + item + "-"))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// 分类器的架构:带 arm64 / aarch64 后缀的算 arm64,带 x64 / x86_64 / amd64 后缀的算 x64,
	/// 没有后缀的老式分类器(<c>natives-osx</c> / <c>natives-macos</c>)按 x64 记(见类注释第 3 条)。
	/// </summary>
	private static string _key_arch(string key)
	{
		string text = "";
		int num = key.LastIndexOf('-');
		if (num >= 0)
		{
			text = key.Substring(num + 1);
		}
		if (text == "arm64" || text == "aarch64")
		{
			return "arm64";
		}
		if (text == "x64" || text == "x86_64" || text == "amd64" || text == "64")
		{
			return "x64";
		}
		if (text == "x86" || text == "i386" || text == "i686" || text == "32")
		{
			return "x86";
		}
		return "x64";
	}
}
