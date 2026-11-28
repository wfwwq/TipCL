using System.Collections.Generic;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 「卡顿」说法的路由守卫(2026-11-28 追加提交):Windows 的关键词表原来只有「卡顿 / 很卡 / 掉帧 / 帧数 / fps / lag」,
/// 用户说「好卡」「有点卡」「一卡一卡」「太卡了」时首轮不会下发 get_system_info / diagnose_lag / tune_jvm ——
/// 模型拿不到诊断工具,只能先瞎猜或者反过来问一堆。现在补一个「卡」,与 Linux 侧同一口径。
///
/// 命中与否走**真实调用** <see cref="Tools.SelectToolSchemas" />(它是纯函数,不碰后端),
/// 所以断言的是「这些工具真的被下发」,而不是「表里有个字」。
/// </summary>
public class AiLagKeywordTests
{
	private static List<string> Selected(string userText)
	{
		List<object> messages = new List<object> { PyDict.Of("role", "user", "content", userText) };
		return Tools.SelectToolSchemas(messages).Select(Tools.SchemaName).ToList();
	}

	[Theory]
	[InlineData("好卡")]
	[InlineData("有点卡")]
	[InlineData("一卡一卡")]
	[InlineData("太卡了,怎么办")]
	[InlineData("卡得不行")]
	[InlineData("游戏好卡啊,一直在掉帧")] // 这条本来就能命中(掉帧),留作回归
	public void LagPhrasesSelectDiagnoseTools(string text)
	{
		List<string> names = Selected(text);
		Assert.Contains("get_system_info", names);
		Assert.Contains("diagnose_lag", names);
		Assert.Contains("tune_jvm", names);
	}

	[Fact]
	public void UnrelatedMessageDoesNotSelectDiagnoseTools()
	{
		List<string> names = Selected("你好,这个启动器怎么用");
		Assert.DoesNotContain("diagnose_lag", names);
		Assert.DoesNotContain("tune_jvm", names);
	}

	[Fact]
	public void HangWordingAlsoSelectsDiagnoseTools()
	{
		// 「卡住不动」既是常见说法、也确实是要诊断的场景,一并覆盖(get_latest_log 本来就在常驻工具里)
		List<string> names = Selected("游戏启动后就卡住不动了");
		Assert.Contains("diagnose_lag", names);
		Assert.Contains("get_system_info", names);
	}

	[Fact]
	public void KnownOverInclusionIsReadOnlyToolsOnly()
	{
		// 取舍记录:「卡」是常用字,「显卡」「卡片」也会命中。命中的三个工具都是只读 / 建议类
		// (tune_jvm 只有 apply=true 才写),不会弹确认、不落盘;代价只是这一轮请求多 ~3 个工具。
		List<string> names = Selected("我的显卡是 RTX 4060");
		Assert.Contains("get_system_info", names);
		Assert.True(ToolMeta.Get("get_system_info").@readonly);
		Assert.True(ToolMeta.Get("diagnose_lag").@readonly);
	}

	[Fact]
	public void KeywordRowStillPointsAtTheThreeDiagnoseTools()
	{
		(string[] keys, string[] extra) row = ExtraTools.Keywords.First((k) => k.keys.Contains("卡"));
		Assert.Equal(new string[3] { "get_system_info", "diagnose_lag", "tune_jvm" }, row.extra);
	}
}