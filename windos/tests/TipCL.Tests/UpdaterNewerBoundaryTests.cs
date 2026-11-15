// R30-WP6d G2(源自 veri-docs 核对建议 D9,见 progress/R30-REWORK.md:37):Windows 侧补上
// 「远端 version 与本机**相等** ⇒ 不算更新」的对应用例。
//
// 背景:更新判据是**严格大于** ——
//   windos/src/TipCL/Core/Launcher/Updater.cs:56-59
//     public static bool Newer(object remote, string local = AppInfo.APP_VERSION)
//       => CmpTuple(_parse(remote), _parse(local)) > 0;
//   Linux 侧有等价用例(tests/TipCL.Tests/Core/UpdaterTests.cs:68-84,含 ("1.0.0","1.0.0",false)),
//   而 Windows 侧在此之前**只有** LayoutDefaultTests.VersionCompareTests 的 `1.0.10 / 1.0.9` 两条
//   (`git grep -n 'Newer' -- windos/tests` 只有那两行)—— 「等于 / 小于不算更新」这条边界在
//   Windows 树没有任何用例。
//   这条边界不是学究:清单 `version` 若填成"恰好等于客户端",界面就永远显示「已是最新版本」,
//   表现和"没发版"一模一样(R30-WP0 报告记过这个坑:更新判据只认严格大于)。
//
// 本文件只补判据边界,不动任何产品代码。
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

public class UpdaterNewerBoundaryTests
{
	/// <summary>大于 / 等于 / 小于三条边界一次钉全(与 Linux 侧 `Newer_ComparesSegmentBySegment` 同口径)。</summary>
	[Theory]
	[InlineData("1.3.0", "1.2.0", true)]     // 远端更大(次版本) ⇒ 有更新
	[InlineData("1.2.1", "1.2.0", true)]     // 远端更大(补丁号) ⇒ 有更新
	[InlineData("1.0.10", "1.0.9", true)]    // 两位补丁号按数值比,不是字符串比(照既有的那条用例)
	[InlineData("1.2.0", "1.2.0", false)]    // **等于 ⇒ 不算更新**(本单补的那条)
	[InlineData("1.1.9", "1.2.0", false)]    // 远端更小 ⇒ 不算更新
	[InlineData("1.0.9", "1.0.10", false)]   // 与上面反向,防止把方向搞反
	public void Newer_IsStrictlyGreater(string remote, string local, bool expected)
		=> Assert.Equal(expected, Updater.Newer(remote, local));

	/// <summary>
	/// 用**真**客户端版本号当 `local`(以及 `Newer` 的默认参数):远端填成与客户端同号 ⇒ 不算更新。
	/// 这正是发版流程里最容易踩的一步 —— 清单写"本机这个号"等于没发版。
	/// </summary>
	[Fact]
	public void Newer_RemoteEqualToTheRealClientVersion_IsNotAnUpdate()
	{
		Assert.False(Updater.Newer(AppInfo.APP_VERSION, AppInfo.APP_VERSION));
		Assert.False(Updater.Newer(AppInfo.APP_VERSION));                      // 默认 local = AppInfo.APP_VERSION
		Assert.True(Updater.Newer("9.9.9", AppInfo.APP_VERSION));              // 对照:真的更高才算更新
	}
}
