using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// AI5-I 附带:用户输入侧「要不要动手」的评测集留档。
/// WantsAction 在整棵树里**零调用点**(Linux 侧是把自己的正则内联在 Tools.cs 里决定要不要带写工具),
/// 所以这条只把现状准确率钉在评测集上,防止以后有人接线时悄悄变差;
/// 数据(Data/ai-wants-eval.jsonl)由 I 包的数据提交带进来,不在时不做断言。
/// </summary>
public class AiWantsTests
{
	[Fact]
	public void WantsActionAccuracyOnEvalSet()
	{
		string path = Path.Combine(AppContext.BaseDirectory, "Data", "ai-wants-eval.jsonl");
		if (!File.Exists(path))
		{
			DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TipCL.sln")))
			{
				dir = dir.Parent;
			}
			Assert.NotNull(dir);
			Assert.False(File.Exists(Path.Combine(dir!.FullName, "tests", "TipCL.Tests", "Data", "ai-wants-eval.jsonl")), "评测集在源码树里但没复制到输出目录");
			return;
		}
		int total = 0;
		int wrong = 0;
		int miss = 0;
		int falsePositive = 0;
		foreach (string line in File.ReadAllLines(path, System.Text.Encoding.UTF8))
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}
			using JsonDocument doc = JsonDocument.Parse(line);
			JsonElement root = doc.RootElement;
			total++;
			string text = root.GetProperty("user_input").GetString() ?? "";
			bool expect = root.GetProperty("expect_wants_action").GetBoolean();
			bool got = Agent.WantsAction(text);
			if (expect != got)
			{
				wrong++;
				if (expect)
				{
					miss++;
				}
				else
				{
					falsePositive++;
				}
			}
		}
		Assert.Equal(60, total);
		// 现状留档(2026-11-28 实测):误判 7/60 —— 漏 4 条(该动手没认出来)、误 3 条(只是提问/建议却当成动手)。
		// 它零调用者,本包不接线;这条防的是「以后有人接线时准确率悄悄变差」。
		Assert.True(wrong <= 7, "WantsAction 在评测集上的误判变多了:" + wrong + "/" + total + "(漏 " + miss + " / 误 " + falsePositive + ")");
	}
}
