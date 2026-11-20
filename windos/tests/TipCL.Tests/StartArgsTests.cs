using System;
using System.Collections.Generic;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 桌面快捷方式「直接启动」参数解析(第七轮复审新增):
/// Windows 侧以前没人消费 <c>-i &lt;实例&gt; launch &lt;版本&gt;</c>,落地时最容易在畸形输入上翻车。
/// 这里把 <see cref="StartArgs.TryParse"/> 的边界逐条钉死 —— 尤其"自检参数不许被误当成 -i"。
/// </summary>
public class StartArgsTests
{
	private static bool Parse(out string instance, out string version, params string[] args)
	{
		return StartArgs.TryParse(args, out instance, out version);
	}

	[Fact]
	public void ParsesTheShapeShortcutLaunchArgsProduces()
	{
		Assert.True(Parse(out string inst, out string ver, "-i", "我的实例", "launch", "1.20.1"));
		Assert.Equal("我的实例", inst);
		Assert.Equal("1.20.1", ver);
	}

	[Fact]
	public void IgnoresOtherFlagsBeforeAndAfter()
	{
		Assert.True(Parse(out string inst, out string ver, "--no-elevate", "-i", "inst", "launch", "1.20.1", "--account", "Steve"));
		Assert.Equal("inst", inst);
		Assert.Equal("1.20.1", ver);
	}

	[Fact]
	public void RejectsIncompleteSequences()
	{
		string i;
		string v;
		Assert.False(StartArgs.TryParse(new string[1] { "-i" }, out i, out v));
		Assert.False(StartArgs.TryParse(new string[2] { "-i", "inst" }, out i, out v));
		Assert.False(StartArgs.TryParse(new string[3] { "-i", "inst", "launch" }, out i, out v));
		// launch 写错 / 大小写不对 / 缺实例名 —— 都不算直接启动请求
		Assert.False(StartArgs.TryParse(new string[4] { "-i", "inst", "Launch", "1.20.1" }, out i, out v));
		Assert.False(StartArgs.TryParse(new string[4] { "-i", "", "launch", "1.20.1" }, out i, out v));
		Assert.False(StartArgs.TryParse(new string[4] { "-i", "   ", "launch", "1.20.1" }, out i, out v));
		Assert.False(StartArgs.TryParse(null, out i, out v));
		Assert.False(StartArgs.TryParse(new string[0], out i, out v));
	}

	[Fact]
	public void NeverMistakesSelfCheckFlagsForTheStartSwitch()
	{
		// 自检/截图类参数(见 App.SelfCheckArgs)绝对不能被当成立即启动
		foreach (string flag in new string[6] { "--smoke", "--shot", "--design-sheet", "--i18n-check", "--ime-check", "--ai-ui-selftest" })
		{
			Assert.False(StartArgs.TryParse(new string[1] { flag }, out string i, out string v), flag + " 不该触发直接启动");
		}
		// 连 --i18n-check 里那个 "-i" 片段也不能被 -i 规则吃掉(逐项 Ordinal 比较)
		Assert.False(StartArgs.TryParse(new string[2] { "--i18n-check", "launch" }, out string i2, out string v2));
	}

	[Fact]
	public void TrimsAndAllowsEmptyVersion()
	{
		Assert.True(Parse(out string inst, out string ver, "-i", "  inst  ", "launch", "   "));
		Assert.Equal("inst", inst);
		Assert.Equal("", ver); // 版本为空 = 由后端按当前实例默认版本决定
	}

	[Fact]
	public void TakesTheFirstCompleteSequenceAndSkipsBrokenOnes()
	{
		// 第一个 -i 后面不是 launch(畸形)→ 跳过,继续找下一个完整序列
		Assert.True(Parse(out string inst, out string ver, "-i", "bad", "launch2", "x", "-i", "good", "launch", "1.19.4"));
		Assert.Equal("good", inst);
		Assert.Equal("1.19.4", ver);
	}
}