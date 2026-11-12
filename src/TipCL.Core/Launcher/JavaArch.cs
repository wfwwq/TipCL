using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TipCL.Core.Launcher;

/// <summary>
/// B2e:Java 的**架构探测**、「这个 Java 能不能跑本次启动的目标架构」的判定,以及安装计划。
///
/// 背景(证据见 PROGRESS.md 的 B2b 第 0 步与 B2d):Apple 芯片上 1.18.2 及更老的版本 mac 原生库
/// 只有 x86_64,<see cref="LaunchArch"/> 会把**目标架构**算成 <c>x64</c>,整份版本只能经 Rosetta 跑。
/// 这时:
/// <list type="bullet">
/// <item>arm64 的 Java 不能用来加载 x86_64 的 <c>.dylib</c>,必须挑 x64 的 Java;</item>
/// <item>x64 的 <c>java</c> 可执行文件在 Apple 芯片上**会自动经 Rosetta 运行**,启动命令里不需要
/// (也不许)加 <c>arch -x86_64</c> 之类的前缀;</item>
/// <item>Mojang 运行时清单与 Adoptium 的下载都要按目标架构取(<c>mac-os</c> / <c>x64</c>),否则
/// 会拿到 arm64 的包,或者干脆拿不到包(<c>mac-os-arm64</c> 没有 <c>jre-legacy</c> / <c>java-runtime-beta</c>)。
/// </list>
///
/// **只在「<c>os == "osx"</c> 且进程架构 <c>== "arm64"</c>」时才做任何判断**,其它情况(Windows /
/// Linux / Intel Mac)所有判定恒为「可用 / 原样」,并且**不读盘、不探测**,行为与改动前逐项相同。
///
/// 探测 Java 架构只读 <c>&lt;JAVA_HOME&gt;/release</c> 这一个文本文件并解析 <c>OS_ARCH=</c> 那一行
/// (mac 的 Mojang 运行时与 Adoptium 的包都带 <c>jre.bundle/Contents/Home/release</c>,见 PROGRESS.md
/// 的 B2e 第 1 项取证),读不到再按启动器自己装的目录名推断;<see cref="Detect(string)"/> 之外全部是
/// 可注入读取委托的纯函数,便于在 Windows 本机做单测。
///
/// 本机是 Windows:**mac 上的真实行为与 <c>release</c> 里 <c>OS_ARCH</c> 的真实取值一律未验证**。
/// </summary>
public static class JavaArch
{
	/// <summary>
	/// 跨架构(Apple 芯片上这个版本只有 x86_64 原生库)时给界面的提示,也是
	/// <c>McLauncher._coerce_java_exe</c> 抛错时追加的同一句。
	/// **不检测 Rosetta 是否已安装**(标志文件的路径本机无法核对,任务书裁决只做无条件提示),也不阻断启动。
	/// </summary>
	internal const string RosettaNote = "该版本没有 Apple 芯片原生库,将使用 Intel(x64)版 Java 经 Rosetta 运行;若系统尚未安装 Rosetta,请先在终端执行:softwareupdate --install-rosetta";

	/// <summary>启动器自己装的 Java 目录名:<c>adoptium-&lt;major&gt;-&lt;arch&gt;</c> / <c>zulu-…</c> / <c>microsoft-…</c>。</summary>
	private static readonly Regex _LAUNCHER_DIR_RE = new Regex("^(adoptium|zulu|microsoft)-\\d+-(x64|x86|arm64)$", RegexOptions.CultureInvariant);

	/// <summary>是不是 Apple 芯片(<c>osx</c> + 进程架构 <c>arm64</c>)。只有它为真时本类才做判断。</summary>
	internal static bool IsAppleSilicon(string os, string processArch)
	{
		return os == "osx" && processArch == "arm64";
	}

	/// <summary>
	/// 是不是**跨架构**:Apple 芯片且目标架构不等于进程架构(即目标 <c>x64</c>)。
	/// 非 Apple 芯片恒为 false。目标架构为 null(理论上不会出现)时按「不等于进程架构」处理,即偏保守。
	/// </summary>
	internal static bool IsCrossArch(string os, string processArch, string targetArch)
	{
		return IsAppleSilicon(os, processArch) && targetArch != processArch;
	}

	/// <summary>
	/// 真值表(纯函数):
	/// <list type="bullet">
	/// <item>不是 Apple 芯片 → 恒 true(不改其它平台的行为);</item>
	/// <item>跨架构 → 只有探测为 <c>x64</c> 才 true,**探测不出来(null)按不可用处理** ——
	/// 宁可自动下载一个 x64 的 Java,也不拿 arm64 的 Java 去加载 x86_64 原生库;</item>
	/// <item>Apple 芯片且目标架构等于进程架构 → 恒 true(保持现状:x64 的 Java 也能跑新版本,只是慢)。</item>
	/// </list>
	/// </summary>
	internal static bool Acceptable(string os, string processArch, string targetArch, string detectedJavaArch)
	{
		if (!IsAppleSilicon(os, processArch))
		{
			return true;
		}
		if (targetArch != processArch)
		{
			return detectedJavaArch == "x64";
		}
		return true;
	}

	/// <summary>
	/// 生产入口:非 Apple 芯片时**不探测**(连 <c>release</c> 都不读),直接 true。
	/// </summary>
	internal static bool AcceptableForExecutable(string os, string processArch, string targetArch, string javaExe)
	{
		if (!IsAppleSilicon(os, processArch))
		{
			return true;
		}
		return Acceptable(os, processArch, targetArch, Detect(javaExe));
	}

	/// <summary>
	/// 可注入**读取委托**(「读一个文本文件」,不是「探测架构」)的版本。
	/// 非 Apple 芯片时**不调用 <paramref name="readTextOrNull"/>**(连探测都不做)。
	/// 生产代码请用上面那个不带委托的重载 —— 传错委托(例如把 <see cref="Detect(string)"/> 当读取器传进来)
	/// 会让探测退化成只看目录名,这一条是本包写测试时抓到的真 bug。
	/// </summary>
	internal static bool AcceptableForExecutable(string os, string processArch, string targetArch, string javaExe, Func<string, string> readTextOrNull)
	{
		if (!IsAppleSilicon(os, processArch))
		{
			return true;
		}
		return Acceptable(os, processArch, targetArch, Detect(javaExe, readTextOrNull));
	}

	/// <summary>
	/// 生产入口:用真实文件读取。也见上面那个不带委托的重载 —— 生产代码**不要**自己传读取委托。
	/// </summary>
	internal static List<object> FilterForTarget(List<object> javas, string os, string processArch, string targetArch)
	{
		if (!IsAppleSilicon(os, processArch))
		{
			return javas;
		}
		if (targetArch != processArch)
		{
			List<object> list = new List<object>();
			foreach (object item in javas)
			{
				if (AcceptableForExecutable(os, processArch, targetArch, _ExeOf(item)))
				{
					list.Add(item);
				}
			}
			return list;
		}
		return javas.OrderBy((object item) => Rank(Detect(_ExeOf(item)))).ToList();
	}

	/// <summary>
	/// 目标架构下的候选列表(纯函数,<paramref name="javas"/> 是 <c>ListInstalledJavas()</c> /
	/// <c>FindSystemJavas()</c> 那种每项为含 <c>exe</c> 键的 <see cref="PyDict"/> 的列表):
	/// <list type="bullet">
	/// <item>非 Apple 芯片 → **原样返回同一个列表实例**(顺序、内容都不变,调用方后续的筛选逻辑逐字不变);</item>
	/// <item>跨架构 → 只留探测为 <c>x64</c> 的(探测不出来的去掉);</item>
	/// <item>Apple 芯片且同架构 → 探测为 <c>arm64</c> 或 null 的排在探测为 <c>x64</c> 的前面
	/// (稳定排序,<c>OrderBy</c> 保证其余相对顺序不变)。</item>
	/// </list>
	/// 第二个重载把**读取委托**注入进来供单测用(不是「探测架构」的委托)。
	/// </summary>
	internal static List<object> FilterForTarget(List<object> javas, string os, string processArch, string targetArch, Func<string, string> readTextOrNull)
	{
		if (!IsAppleSilicon(os, processArch))
		{
			return javas;
		}
		if (targetArch != processArch)
		{
			List<object> list = new List<object>();
			foreach (object item in javas)
			{
				if (AcceptableForExecutable(os, processArch, targetArch, _ExeOf(item), readTextOrNull))
				{
					list.Add(item);
				}
			}
			return list;
		}
		return javas.OrderBy((object item) => Rank(Detect(_ExeOf(item), readTextOrNull))).ToList();
	}

	/// <summary>
	/// 同架构时的排序键:<c>x64</c> 记为 1,<c>arm64</c> / null / 其它记为 0。
	/// 「arm64 或探测不出来的排在 x64 前面」= 优先用原生 Java,只是慢一点也好过经 Rosetta。
	/// </summary>
	internal static int Rank(string detectedJavaArch)
	{
		if (detectedJavaArch == "x64")
		{
			return 1;
		}
		return 0;
	}

	/// <summary>
	/// 安装计划(纯函数)。规则与改动前的 <c>EnsureJavaForVersion</c> 逐项一致,只有跨架构那一格不同:
	/// <list type="bullet">
	/// <item>非跨架构:Mojang 用 <c>PlatformKeyFor(os, 进程架构)</c>(= 原来的静态 <c>PLATFORM_KEY</c>),
	/// Adoptium 的 <c>arch</c> 是 <paramref name="legacy32"/> ? <c>x86</c> : null;</item>
	/// <item>跨架构:Mojang 用 <c>PlatformKeyFor(os, "x64")</c>(mac 上是 <c>mac-os</c>,它有
	/// <c>jre-legacy</c> / <c>java-runtime-beta</c>,而 <c>mac-os-arm64</c> 没有),Adoptium 的 <c>arch</c> 传 <c>x64</c>。</item>
	/// </list>
	/// <c>jre-legacy</c> 仍然不走 Mojang(与改动前一致,那条老运行时的清单形态不同)。
	/// <paramref name="major"/> 只作为计划的上下文带出来(单测按「形态 × 平台组合」逐格断言它),不参与判断。
	/// </summary>
	internal static InstallPlan PlanInstall(string component, long major, string os, string processArch, string targetArch, bool legacy32)
	{
		bool cross = IsCrossArch(os, processArch, targetArch);
		bool tryMojang = !string.IsNullOrEmpty(component) && component != "jre-legacy";
		string platformKey = Java.PlatformKeyFor(os, cross ? "x64" : processArch);
		string adoptiumArch = (cross ? "x64" : (legacy32 ? "x86" : null));
		return new InstallPlan(tryMojang, platformKey, adoptiumArch, targetArch, major);
	}

	/// <summary>
	/// <c>Installer._ensure_any_java()</c> 的兜底大版本:它只是「随便一个能跑的 Java」(用来跑
	/// Forge / NeoForge 的处理器 jar,挑选时**不看版本**),原来写死 8。
	/// 但 Adoptium 对 mac <c>aarch64</c> **没有 8 的包**(线上实测,见 PROGRESS.md 的 B2a 第 1.3 节),
	/// 所以 Apple 芯片上这条兜底必定失败 → 改成 17(有包)。其它平台仍是 8(原值,逐字未变)。
	/// </summary>
	internal static long FallbackMajor(string os, string processArch)
	{
		if (IsAppleSilicon(os, processArch))
		{
			return 17L;
		}
		return 8L;
	}

	/// <summary>
	/// B2c1:<c>Java.JavaForInstaller("forge-legacy")</c> 找不到已装的 Java 8 时要装的那个 Adoptium 包该传什么 arch。
	/// Apple 芯片上返回 <c>"x64"</c>(Adoptium 对 mac <c>aarch64</c> **没有** Java 8 的包,线上实测见 PROGRESS.md 的 B2a;
	/// 而 <c>macos-x64</c> 的 Java 8 有包;Forge 安装器是纯 Java,x64 的 JVM 经 Rosetta 能跑,且 1.17 之前的版本在
	/// Apple 芯片上本来就要用 x64 的 Java 8,装同一个 <c>adoptium-8-x64</c> 目录可以复用,不必再多下一个 Zulu)。
	/// 其余平台返回 <c>null</c> = <c>InstallAdoptium</c> 原来的「用进程架构」,行为逐字不变。
	/// </summary>
	internal static string Legacy8AdoptiumArch(string os, string processArch)
	{
		if (IsAppleSilicon(os, processArch))
		{
			return "x64";
		}
		return null;
	}

	/// <summary>
	/// 跨架构时在原文案后面追加上 <see cref="RosettaNote"/>;**原文案一字不改**(只是拼接),
	/// 也不改启动命令 / JVM 参数(不加 <c>arch -x86_64</c> 之类的前缀)。非跨架构返回**原字符串本身**。
	/// </summary>
	internal static string WithRosettaNote(string message, string os, string processArch, string targetArch)
	{
		if (!IsCrossArch(os, processArch, targetArch))
		{
			return message;
		}
		return message + " " + RosettaNote;
	}

	/// <summary>
	/// 生产入口:用真实文件读取;读不到、不是文件、或任何异常都当「不知道」(返回 null)。
	/// 不会起进程(mac 上没有「读一个文件就知道架构」之外的可靠手段,任务书明确不许起 <c>java -version</c>)。
	/// </summary>
	public static string Detect(string javaExe)
	{
		return Detect(javaExe, _ReadTextOrNull);
	}

	/// <summary>
	/// 探测一个 Java 可执行文件的架构:返回 <c>"x64"</c> / <c>"arm64"</c> / <c>"x86"</c> / null(不知道)。
	/// 优先读 <c>&lt;JAVA_HOME&gt;/release</c> 的 <c>OS_ARCH=</c>(可靠),读不到或值不认识再按
	/// 启动器自己装的目录名推断(只认 <c>adoptium-8-x64</c> 这类三段名与 <c>…-mac-os[-arm64]</c>);
	/// 两者冲突时以 <c>release</c> 为准。全部读不到返回 null。
	/// </summary>
	internal static string Detect(string javaExe, Func<string, string> readTextOrNull)
	{
		if (string.IsNullOrEmpty(javaExe))
		{
			return null;
		}
		string home = _JavaHomeOf(javaExe);
		if (home != null && readTextOrNull != null)
		{
			string text = null;
			try
			{
				// mac 的 JAVA_HOME 布局固定是 <home>/release,分隔符原样保留(这里只会在 osx 上被调到)。
				text = readTextOrNull(home + "/release");
			}
			catch (Exception)
			{
				text = null;
			}
			string arch = _ParseOsArch(text);
			if (arch != null)
			{
				return arch;
			}
		}
		return _InferFromDirectoryNames(javaExe);
	}

	/// <summary>把 <c>release</c> 里的 <c>OS_ARCH</c> 取值映射成 <c>x64</c> / <c>arm64</c> / <c>x86</c>;不认识返回 null。</summary>
	internal static string MapOsArchValue(string value)
	{
		if (value == null)
		{
			return null;
		}
		switch (value.Trim().ToLowerInvariant())
		{
		case "aarch64":
		case "arm64":
			return "arm64";
		case "x86_64":
		case "amd64":
		case "x64":
			return "x64";
		case "i386":
		case "i486":
		case "i586":
		case "i686":
		case "x86":
			return "x86";
		default:
			return null;
		}
	}

	private static string _ReadTextOrNull(string path)
	{
		try
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
			{
				return null;
			}
			return File.ReadAllText(path);
		}
		catch (Exception)
		{
			return null;
		}
	}

	/// <summary>
	/// 从 <c>release</c> 的文本里取 <c>OS_ARCH</c> 那一行(<c>OS_ARCH="aarch64"</c> 这种,值可能有引号)。
	/// 找不到该行、值为空、或值不认识都返回 null(由调用方接着走目录名推断)。
	/// </summary>
	private static string _ParseOsArch(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}
		foreach (string raw in text.Split('\n'))
		{
			string line = raw.Trim();
			int eq = line.IndexOf('=');
			if (eq <= 0)
			{
				continue;
			}
			if (line.Substring(0, eq).Trim() != "OS_ARCH")
			{
				continue;
			}
			string arch = MapOsArchValue(_Unquote(line.Substring(eq + 1).Trim()));
			if (arch != null)
			{
				return arch;
			}
		}
		return null;
	}

	private static string _Unquote(string value)
	{
		if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[value.Length - 1] == value[0])
		{
			return value.Substring(1, value.Length - 2);
		}
		return value;
	}

	/// <summary>
	/// 按启动器自己装的目录名推断架构,路径**原样切分**('/' 与 '\' 都算分隔符,不做平台规范化,
	/// 这样在 Windows 本机上按 mac 路径做单测时得到的字符串与 mac 上一致)。
	/// 命中的是**最靠前**的那一段。只认本启动器自己会创建的两种名字形态,别的目录名一律不认识。
	/// </summary>
	private static string _InferFromDirectoryNames(string javaExe)
	{
		foreach (string seg in javaExe.Split(new char[2] { '/', '\\' }))
		{
			if (seg.Length == 0)
			{
				continue;
			}
			Match match = _LAUNCHER_DIR_RE.Match(seg);
			if (match.Success)
			{
				return match.Groups[2].Value;
			}
			if (seg.EndsWith("-mac-os-arm64", StringComparison.Ordinal))
			{
				return "arm64";
			}
			if (seg.EndsWith("-mac-os", StringComparison.Ordinal))
			{
				return "x64";
			}
		}
		return null;
	}

	/// <summary><c>&lt;java_home&gt;/bin/java</c> → <c>&lt;java_home&gt;</c>(文本切分,原样保留分隔符);推不出来返回 null。</summary>
	private static string _JavaHomeOf(string javaExe)
	{
		string path = javaExe.TrimEnd('/', '\\');
		int a = _LastSeparatorBefore(path, path.Length);
		if (a <= 0)
		{
			return null;
		}
		int b = _LastSeparatorBefore(path, a);
		if (b < 0)
		{
			return null;
		}
		string home = path.Substring(0, b);
		return (home.Length == 0) ? null : home;
	}

	private static int _LastSeparatorBefore(string s, int end)
	{
		for (int i = end - 1; i >= 0; i--)
		{
			if (s[i] == '/' || s[i] == '\\')
			{
				return i;
			}
		}
		return -1;
	}

	/// <summary>列表项里的 java 可执行文件路径(不是 <see cref="PyDict"/> 或没有 <c>exe</c> 时当「不知道」)。</summary>
	private static string _ExeOf(object item)
	{
		if (item is PyDict pyDict)
		{
			return Py.S(pyDict.Get("exe"));
		}
		return null;
	}

	/// <summary>
	/// 安装计划的数据(见 <see cref="PlanInstall"/>)。字段的含义与
	/// <c>EnsureJavaForVersion</c> 里那两行一一对应。
	/// </summary>
	internal sealed class InstallPlan
	{
		internal InstallPlan(bool tryMojang, string mojangPlatformKey, string adoptiumArch, string targetArch, long major)
		{
			TryMojang = tryMojang;
			MojangPlatformKey = mojangPlatformKey;
			AdoptiumArch = adoptiumArch;
			TargetArch = targetArch;
			Major = major;
		}

		/// <summary>先试 <c>InstallMojangRuntime</c> 与否(原逻辑:component 有值且不是 <c>jre-legacy</c>)。</summary>
		internal bool TryMojang { get; }

		/// <summary>Mojang 运行时清单的平台键(跨架构时是 <c>PlatformKeyFor(os, "x64")</c>)。</summary>
		internal string MojangPlatformKey { get; }

		/// <summary>传给 <c>InstallAdoptium</c> 的 <c>arch</c>(null = 沿用 <c>Utils.ARCH</c>)。</summary>
		internal string AdoptiumArch { get; }

		/// <summary>本次启动的目标架构(只作为上下文,不参与判断)。</summary>
		internal string TargetArch { get; }

		/// <summary>版本要求的 Java 大版本(只作为上下文,不参与判断)。</summary>
		internal long Major { get; }
	}
}
