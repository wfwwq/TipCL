// R30-WP6d G3(lead 追加单:我在 WP6d 报告 §6 自己点出的同款洞,批准后照 G1 的做法补)。
//
// 洞是什么:Windows 树也有"版本号靠手工一次替换"的问题 ——
//   windos/src/TipCL/TipCL.csproj:17 的 `<Version>1.2.0</Version>`(程序集版本 / 文件属性 / 产物内嵌的号)
//   与 windos/src/TipCL/Core/Launcher/AppInfo.cs:9 的 `APP_VERSION`(更新判据 / 关于页 / 插件兼容)
// 两者之间**全树没有任何用例**;`git grep -n 'csproj' -- windos/tests` 的命中全是注释与 ProjectReference。
// 只改一处就会重演 1.1 那次"应用内一个号、产物另一个号"的事故。
//
// 本文件读两处**不同来源**、分别断言,不把它们混成一条证据(lead 明确要求写清读的是哪一份):
//   1) `CsprojVersion_MatchesTheRuntimeAppVersion` —— 真读**磁盘上的 csproj 文本**
//      (`<repo>\windos\src\TipCL\TipCL.csproj`,从测试输出目录往上找 `TipCL.Linux.sln` 定仓库根);
//   2) `LoadedAssembly_IsTheTestOutputCopy_AndCarriesTheSameVersion` —— 真读**测试宿主当下加载的那份
//      net48 `TipCL.exe` 程序集**(`typeof(AppInfo).Assembly`;断言它的路径正是测试输出目录里的副本,
//      不是 `bin\Release\net48` 那个交付件),读它的 `AssemblyInformationalVersion` / `AssemblyFileVersion`。
// 两条各自都能被"只改一处"打红:只改 csproj ⇒ 第 1 条红;只改常量 ⇒ 两条都红。
//
// 不抄第二份常量自我印证:期望值全部来自 `AppInfo.APP_VERSION` 与磁盘文本,没有硬编码 "1.2.0"。
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

public class VersionLandingPointsTests
{
	/// <summary>仓库根下的相对路径(用 '/' 写,拼路径时再换分隔符)。</summary>
	private const string CsprojRelative = "windos/src/TipCL/TipCL.csproj";

	private const string ExeFileName = "TipCL.exe";

	/// <summary>仓库根目录:从测试输出目录往上找 `TipCL.Linux.sln` 当锚点(Linux 树与 Windows 树同仓)。</summary>
	private static string RepoRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		for (int i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
		{
			if (File.Exists(Path.Combine(dir.FullName, "TipCL.Linux.sln"))) return dir.FullName;
		}
		throw new InvalidOperationException(
			"从 " + AppContext.BaseDirectory + " 往上找不到 TipCL.Linux.sln —— 这个守卫用例必须能读到磁盘上的 csproj");
	}

	private static string CsprojPath()
		=> Path.Combine(RepoRoot(), CsprojRelative.Replace('/', Path.DirectorySeparatorChar));

	/// <summary>真读磁盘上 csproj 里的 `&lt;Version&gt;` 文本;读不到就当场判红(守卫的前提没了)。</summary>
	private static string CsprojVersion()
	{
		string path = CsprojPath();
		Assert.True(File.Exists(path), "找不到 " + path + " —— 这个守卫用例必须能读到磁盘上的 csproj");
		XElement version = XDocument.Load(path).Root?.Elements("PropertyGroup").Elements("Version").FirstOrDefault();
		Assert.False(version == null, CsprojRelative + " 里没有 <Version> 元素 —— 守卫的前提没了");
		return version.Value.Trim();
	}

	/// <summary>`<Version>1.2.0</Version>` 生成的 `AssemblyInformationalVersion` 形如 `1.2.0+&lt;commit sha&gt;`
	/// (SourceLink 追加),比较前切掉 `+` 后缀;`AssemblyFileVersion` 是四段 `1.2.0.0`。</summary>
	private static string InformationalVersion(Assembly asm)
	{
		AssemblyInformationalVersionAttribute a =
			asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
		string raw = a == null ? "" : (a.InformationalVersion ?? "");
		int plus = raw.IndexOf('+');
		return (plus >= 0 ? raw.Substring(0, plus) : raw).Trim();
	}

	private static string FileVersion(Assembly asm)
	{
		AssemblyFileVersionAttribute a = asm.GetCustomAttribute<AssemblyFileVersionAttribute>();
		return a == null ? "" : (a.Version ?? "");
	}

	/// <summary>
	/// 磁盘上的 `windos/src/TipCL/TipCL.csproj` 的 `<Version>` 必须等于**运行时的版本真源**
	/// `AppInfo.APP_VERSION`(更新判据 / 关于页(`SettingsPage.cs:2030`)/ 插件版本 / 反馈 / sysinfo 都读它)。
	/// </summary>
	[Fact]
	public void CsprojVersion_MatchesTheRuntimeAppVersion()
	{
		Assert.Equal(AppInfo.APP_VERSION, CsprojVersion());
	}

	/// <summary>
	/// csproj 那个号真正的消费者是**产物本身**。这条读的是**测试宿主当下加载的那份程序集**:
	/// 测试工程(`net8.0`)用 `SetTargetFramework=TargetFramework=net48` 引用主工程(`net48`),
	/// 所以加载的就是**测试输出目录里的那份 net48 `TipCL.exe` 副本**(下面第一句断言把这件事钉死,
	/// 免得下一位误以为读的是 `bin\Release\net48` 的交付件或某个 Costura 单文件包)。
	/// </summary>
	[Fact]
	public void LoadedAssembly_IsTheTestOutputCopy_AndCarriesTheSameVersion()
	{
		Assembly asm = typeof(AppInfo).Assembly;
		string expectedPath = Path.Combine(AppContext.BaseDirectory, ExeFileName);

		Assert.True(
			string.Equals(expectedPath, asm.Location, StringComparison.OrdinalIgnoreCase),
			"测试宿主加载的不是测试输出目录里那份 net48 " + ExeFileName + ",而是:" + asm.Location);
		Assert.Equal(ExeFileName, Path.GetFileName(asm.Location));

		Assert.Equal(AppInfo.APP_VERSION, InformationalVersion(asm));
		Assert.Equal(AppInfo.APP_VERSION + ".0", FileVersion(asm));   // <Version>1.2.0</Version> ⇒ 1.2.0.0
	}

	/// <summary>
	/// `docs/RELEASE-DRAFT.md` §1.1 的第 9/10/11 条(`windos/src/TipCL/Core/Launcher/AppInfo.cs` 的
	/// `APP_VERSION` / `LAUNCHER_VERSION` / `__version__`)是**同一份产品版本**,不许单飞。
	/// 若将来确实需要分叉,请在这条用例里显式豁免,而不是让它悄悄漂走。
	/// </summary>
	[Fact]
	public void OtherVersionConstants_FollowTheSameNumber()
	{
		Assert.Equal(AppInfo.APP_VERSION, AppInfo.LAUNCHER_VERSION);
		Assert.Equal(AppInfo.APP_VERSION, AppInfo.__version__);
	}

	/// <summary>
	/// 把「csproj 里那个号」与**更新判据**接上:`Updater.Newer(remote, local = AppInfo.APP_VERSION)`
	/// (`windos/src/TipCL/Core/Launcher/Updater.cs:56`)只认**严格大于** —— 远端 version 等于 csproj 里
	/// 这个号时不算更新(这正是 R30-WP0 记过的坑:清单填成"本机这个号"等于没发版)。
	/// 于是"csproj 的号"与"更新器拿来比的号"被钉成同一个。
	/// </summary>
	[Fact]
	public void UpdateCriterion_ComparesAgainstTheSameNumber()
	{
		string csprojVersion = CsprojVersion();

		Assert.False(Updater.Newer(csprojVersion), "csproj 里那个号被当成'有更新'了 —— 更新判据与版本落点分叉了");
		Assert.False(Updater.Newer(csprojVersion, AppInfo.APP_VERSION));
		Assert.True(Updater.Newer("9.9.9"), "对照:真的更高的版本号必须仍然算更新");   // 防止把方向搞反
	}
}
