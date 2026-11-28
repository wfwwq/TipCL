using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// AI5-D9:压缩状态缓存从「满 16 个就整表清空」改成 LRU(只淘汰最久未访问的那一个)。
/// 整表清空会让正在用的会话丢掉 CheckRapidRefill 的连击保护和连续失败计数。
/// </summary>
public class AiCompactStateLruTests
{
	/// <summary>用带前缀的 17 个会话 id,并先把它们清出缓存,免得和别的用例互相干扰。</summary>
	private static string[] SessionIds()
	{
		string[] ids = new string[17];
		for (int i = 0; i < ids.Length; i++)
		{
			ids[i] = "ai5-d9-lru-" + i.ToString("D2") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
		}
		foreach (string id in ids)
		{
			Agent.ForgetCompactState(id);
		}
		return ids;
	}

	[Fact]
	public void CapacityStaysAtSixteen()
	{
		Assert.Equal(16, Agent.COMPACT_STATES_MAX);
	}

	[Fact]
	public void SeventeenthSessionEvictsOnlyTheLeastRecentlyUsed()
	{
		string[] ids = SessionIds();
		// 先访问前 16 个,每个打个标记好认
		for (int i = 0; i < 16; i++)
		{
			Agent._compact_state_for(ids[i]).consecutive_failures = 100L + i;
		}
		// 再摸一次 ids[0],让它变成「最近用过」;此时最久没用的应该是 ids[1]
		Agent._compact_state_for(ids[0]).consecutive_failures = 7L;
		// 第 17 个会话进来 → 只该淘汰 ids[1]
		Agent._compact_state_for(ids[16]).consecutive_failures = 5L;
		List<string> cached = Agent.CompactStateSessions();
		Assert.Equal(16, cached.Count);
		Assert.DoesNotContain(ids[1], cached);
		Assert.Contains(ids[0], cached);
		Assert.Contains(ids[16], cached);
		// 状态是原来那一份(不是重建的):标记还在
		Assert.Equal(7L, Agent._compact_state_for(ids[0]).consecutive_failures);
		Assert.Equal(5L, Agent._compact_state_for(ids[16]).consecutive_failures);
		// 被淘汰的那个重新访问时是干净的新状态
		Assert.Equal(0L, Agent._compact_state_for(ids[1]).consecutive_failures);
		foreach (string id in ids)
		{
			Agent.ForgetCompactState(id);
		}
	}

	[Fact]
	public void ReaccessKeepsHotSessionFromBeingEvicted()
	{
		string[] ids = SessionIds();
		for (int i = 0; i < 16; i++)
		{
			Agent._compact_state_for(ids[i]);
		}
		// 反复用 ids[0]:它永远是最近访问的,后面再进几个新会话也轮不到它被淘汰
		for (int round = 0; round < 4; round++)
		{
			Agent._compact_state_for(ids[0]).rapid_refills = 42L;
			Agent._compact_state_for(ids[16]).rapid_refills = 1L;
			Agent.ForgetCompactState(ids[16]);
		}
		List<string> cached = Agent.CompactStateSessions();
		Assert.Contains(ids[0], cached);
		Assert.Equal(42L, Agent._compact_state_for(ids[0]).rapid_refills);
		foreach (string id in ids)
		{
			Agent.ForgetCompactState(id);
		}
	}

	[Fact]
	public void ForgetRemovesOnlyThatSession()
	{
		string[] ids = SessionIds();
		Agent._compact_state_for(ids[0]).consecutive_failures = 3L;
		Agent._compact_state_for(ids[1]).consecutive_failures = 4L;
		Agent.ForgetCompactState(ids[0]);
		Assert.DoesNotContain(ids[0], Agent.CompactStateSessions());
		Assert.Contains(ids[1], Agent.CompactStateSessions());
		Assert.Equal(4L, Agent._compact_state_for(ids[1]).consecutive_failures);
		foreach (string id in ids)
		{
			Agent.ForgetCompactState(id);
		}
	}
}
