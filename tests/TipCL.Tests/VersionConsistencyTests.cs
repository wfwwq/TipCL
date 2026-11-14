using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 【版本号守卫】四处必须说同一个版本:
/// <list type="number">
/// <item><c>src\TipCL.App\TipCL.App.csproj</c> 的 <c>&lt;Version&gt;</c>(TipCL.dll 的 AssemblyVersion / FileVersion 来源);</item>
/// <item><c>src\TipCL.Core\TipCL.Core.csproj</c> 的 <c>&lt;Version&gt;</c>(TipCL.Core.dll 同);</item>
/// <item><c>src\TipCL.Core\Launcher\AppInfo.cs</c> 的 <c>APP_VERSION</c>(运行时展示、反馈上报、更新比较都用它);</item>
/// <item>同一个文件里的 <c>LAUNCHER_VERSION</c>(写进 Minecraft 的 <c>launcher_version</c>)。</item>
/// </list>
///
/// <b>为什么需要它</b>:这是"两处手工同步"的约定,而同类约定在别的克隆上真的漂过 —— linux 侧 csproj 长期写
/// <c>0.1.0-m3</c>、而 <c>APP_VERSION</c> 是 <c>1.1.0</c>,直到有人手工去数才发现。mac 侧的 <c>&lt;Version&gt;</c>
/// 是这轮才补上的,补上之后就必须由用例兜住:改版本时漏改任意一处,这里立刻红。
///
/// <b>刻意不硬编码 1.1.0</b>:只断言四处**彼此相等**(将来升版本不用改守卫)。csproj 里**缺** <c>&lt;Version&gt;</c>
/// 要报"缺失"而不是静默跳过(缺了会回落到 SDK 默认 1.0.0.0,那正是这轮补版本号之前的病)。
///
/// 用例体里先用**合成文本**验一遍两个解析器(读得到 / 读不到都验),免得"解析器坏了"被当成"版本一致"。
/// </summary>
public sealed class VersionConsistencyTests
{
	/// <summary>`&lt;Version&gt;x.y.z&lt;/Version&gt;` 里的值;没有这个元素 ⇒ 空串(不静默当默认值)。</summary>
	internal static string ProjectVersionOrEmpty(string projectXml)
	{
		XDocument doc = XDocument.Parse(projectXml);
		XElement? element = doc.Descendants("Version").FirstOrDefault();
		return ((element != null) ? (element.Value ?? "") : "").Trim();
	}

	/// <summary>`public const string NAME = "x";` 里的值;没有这条声明 ⇒ 空串。</summary>
	internal static string ConstStringOrEmpty(string source, string name)
	{
		Match match = Regex.Match(source, "\\b" + Regex.Escape(name) + "\\s*=\\s*\"([^\"]*)\"", RegexOptions.CultureInvariant);
		return match.Success ? match.Groups[1].Value.Trim() : "";
	}

	private static string Show(string value)
	{
		return value.Length > 0 ? "'" + value + "'" : "(missing)";
	}

	[Fact]
	public void Assembly_versions_and_app_info_versions_agree()
	{
		// ---- 1 先验解析器本身(合成文本):读得到的两条 + 读不到的两条
		Assert.Equal("9.9.9", ProjectVersionOrEmpty("<Project><PropertyGroup><Version>9.9.9</Version></PropertyGroup></Project>"));
		Assert.Equal("", ProjectVersionOrEmpty("<Project><PropertyGroup><AssemblyName>TipCL</AssemblyName></PropertyGroup></Project>"));
		Assert.Equal("9.9.9", ConstStringOrEmpty("public const string APP_VERSION = \"9.9.9\";", "APP_VERSION"));
		Assert.Equal("", ConstStringOrEmpty("public const string LAUNCHER_VERSION = \"9.9.9\";", "APP_VERSION"));

		// ---- 2 再读真实文件(缺文件/缺元素一律红,不静默跳过)
		string root = EncodingGuardTests.RepoRoot();
		string appProject = Path.Combine(root, "src", "TipCL.App", "TipCL.App.csproj");
		string coreProject = Path.Combine(root, "src", "TipCL.Core", "TipCL.Core.csproj");
		string appInfo = Path.Combine(root, "src", "TipCL.Core", "Launcher", "AppInfo.cs");
		foreach (string path in new[] { appProject, coreProject, appInfo })
		{
			Assert.True(File.Exists(path), "守卫要读的文件不存在: " + path);
		}

		string appVersion = ProjectVersionOrEmpty(File.ReadAllText(appProject));
		string coreVersion = ProjectVersionOrEmpty(File.ReadAllText(coreProject));
		string appInfoSource = File.ReadAllText(appInfo);
		string appInfoVersion = ConstStringOrEmpty(appInfoSource, "APP_VERSION");
		string launcherVersion = ConstStringOrEmpty(appInfoSource, "LAUNCHER_VERSION");

		string observed =
			"observed: TipCL.App.csproj <Version>=" + Show(appVersion) +
			"; TipCL.Core.csproj <Version>=" + Show(coreVersion) +
			"; AppInfo.APP_VERSION=" + Show(appInfoVersion) +
			"; AppInfo.LAUNCHER_VERSION=" + Show(launcherVersion);

		Assert.False(
			appVersion.Length == 0,
			"TipCL.App.csproj 里缺少 <Version>(缺了会静默回落到 SDK 默认 1.0.0.0 —— 这正是补版本号之前的病)。" + observed);
		Assert.False(
			coreVersion.Length == 0,
			"TipCL.Core.csproj 里缺少 <Version>(同上)。" + observed);
		Assert.False(
			appInfoVersion.Length == 0,
			"AppInfo.cs 里找不到 APP_VERSION 的字面量赋值。Not found: APP_VERSION" + observed);
		Assert.False(
			launcherVersion.Length == 0,
			"AppInfo.cs 里找不到 LAUNCHER_VERSION 的字面量赋值。" + observed);

		Assert.True(
			string.Equals(appVersion, coreVersion, StringComparison.Ordinal) &&
			string.Equals(coreVersion, appInfoVersion, StringComparison.Ordinal) &&
			string.Equals(appInfoVersion, launcherVersion, StringComparison.Ordinal),
			"版本号四处必须相同(改版本时一起改:两个 csproj 的 <Version> + AppInfo 的两个常量)。" + observed);
	}
}
