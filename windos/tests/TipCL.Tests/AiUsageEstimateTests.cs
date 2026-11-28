using System.Collections.Generic;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// AI5-D7:没有 provider usage 时,用量估算以前每轮都拿**整段** messages 估一遍再累加,
/// 回合越多估算值越虚高。现在只累加本轮新增的消息。
/// </summary>
public class AiUsageEstimateTests
{
	private static List<object> Msgs(int n, int start = 0, int chars = 80)
	{
		List<object> list = new List<object>();
		for (int i = 0; i < n; i++)
		{
			list.Add(PyDict.Of("role", (i % 2 == 0) ? "user" : "assistant", "content", "m" + (start + i) + new string('字', chars)));
		}
		return list;
	}

	private static long Full(List<object> messages)
	{
		return Tokens.EstimateMessages(messages);
	}

	[Fact]
	public void FirstRoundCountsEverything()
	{
		List<object> messages = Msgs(4);
		var (delta, nextBase) = Agent.EstimateNewMessages(messages, 0L);
		Assert.Equal(Full(messages), delta);
		Assert.Equal(4L, nextBase);
	}

	[Fact]
	public void NextRoundCountsOnlyNewMessages()
	{
		List<object> messages = Msgs(4);
		var (first, baseAfterFirst) = Agent.EstimateNewMessages(messages, 0L);
		long fullBefore = Full(messages);
		// 第二轮:模型回了 tool_calls,再补一条 tool 结果
		messages.Add(PyDict.Of("role", "assistant", "content", null, "tool_calls", new List<object>()));
		messages.Add(PyDict.Of("role", "tool", "tool_call_id", "c1", "name", "list_mods", "content", new string('字', 200)));
		var (second, baseAfterSecond) = Agent.EstimateNewMessages(messages, baseAfterFirst);
		long fullAfter = Full(messages);
		// 只算新增部分:两轮加起来 == 整段算一次
		Assert.Equal(fullAfter, first + second);
		Assert.True(second < fullAfter, "第二轮不该把历史再算一遍");
		Assert.Equal(fullAfter - fullBefore, second);
		Assert.Equal((long)messages.Count, baseAfterSecond);
	}

	[Fact]
	public void CompactionResetsToAFullEstimate()
	{
		List<object> messages = Msgs(10);
		var (_, baseAfter) = Agent.EstimateNewMessages(messages, 0L);
		// 压缩把消息表换成更短的一段
		List<object> compacted = Msgs(3, 100);
		var (delta, nextBase) = Agent.EstimateNewMessages(compacted, baseAfter);
		Assert.Equal(Full(compacted), delta);
		Assert.Equal(3L, nextBase);
	}

	[Fact]
	public void StaleBaseIsTreatedAsZero()
	{
		List<object> messages = Msgs(2);
		var (delta, nextBase) = Agent.EstimateNewMessages(messages, 99L);
		Assert.Equal(Full(messages), delta);
		Assert.Equal(2L, nextBase);
	}
}
