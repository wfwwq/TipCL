namespace TipCL.Core.Launcher;

/// <summary>
/// B2d:**本次启动的目标架构**。版本 JSON 规则里的 <c>os.arch</c>、natives 键里的 <c>${arch}</c>、
/// natives 分类器的架构后缀,指的都是「将要运行游戏的 JVM 的架构」,也就是这里算出来的值,
/// 而不是启动器进程的架构。
///
/// 背景(证据见 PROGRESS.md 的 B2b 第 0 步与第 6 项):Apple 芯片(<c>osx</c> + 进程架构 <c>arm64</c>)上,
/// 1.18.2 及更老的版本 mac 原生库只有 x86_64,必须用 x64 Java 经 Rosetta 运行,相应地 natives 也要选 x86_64 的;
/// 1.19.4 起每个原生库都有 arm64 变体,照常原生运行。
///
/// 判定:只有「<paramref name="os"/> 是 osx」「进程架构是 arm64」「<see cref="MacNativeSupport"/> 判定
/// 这个版本的 mac 原生库是 <see cref="MacNativeKind.X64Only"/> 或 <see cref="MacNativeKind.Mixed"/>」
/// 三个条件同时成立时返回 <c>x64</c>;其余一律返回传入的进程架构原值。
/// 所以 Windows / Linux / Intel Mac 上的结果**恒等于**进程架构。
///
/// 为什么 <see cref="MacNativeKind.Mixed"/> 也归 <c>x64</c>:缺 arm64 变体的那个库在 arm64 JVM 里加载不了,
/// 而观察到的线上数据里每个 mac 原生库都有 x86_64 变体,所以整份版本退到 x64 一定拿得齐。
///
/// 目标架构是**版本的确定函数**(入参只有合并后的版本 JSON 与平台三要素,与已装文件、用户选择、时间无关),
/// 所以同一台机器上同一个版本永远得到同一个值,natives 目录**不需要**按架构分目录 ——
/// B2b 第 6 项担心的「两种架构的 natives 互相覆盖」不成立。
///
/// 整条 Rosetta 路径(B2d 的 natives / 规则 / classpath + B2e 的 Java 侧)**未经真机验证**,本机是 Windows。
/// Java 侧(平台键、厂商安装包、已装 Java 的挑选)由 B2e 处理,本类不做。
/// </summary>
public static class LaunchArch
{
	/// <summary>
	/// 本次启动的目标架构。传 null 版本 JSON、非 osx、进程不是 arm64 时都返回进程架构原值。
	/// </summary>
	public static string For(PyDict mergedVersionJson)
	{
		return For(mergedVersionJson, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION);
	}

	/// <summary>
	/// 可注入 os / 进程架构 / osVersion 的内部重载,便于在 Windows 本机按 mac 的参数做单测。
	/// 逻辑与上面那个公开方法逐字相同(公开方法只负责传 <see cref="Utils"/> 的三个静态字段)。
	/// </summary>
	internal static string For(PyDict mergedVersionJson, string os, string processArch, string osVersion)
	{
		if (os == "osx" && processArch == "arm64")
		{
			MacNativeKind kind = MacNativeSupport.Analyze(mergedVersionJson, os, processArch, osVersion).Kind;
			if (kind == MacNativeKind.X64Only || kind == MacNativeKind.Mixed)
			{
				return "x64";
			}
		}
		return processArch;
	}
}
