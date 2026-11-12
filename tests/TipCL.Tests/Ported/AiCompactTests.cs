using System.Collections.Generic;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

/// <summary>历史摘要不再每轮都重做一遍。</summary>
public class AiCompactTests
{
	private static List<object> Msgs(int n, int start = 0, int chars = 2)
	{
		List<object> l = new List<object>();
		for (int i = 0; i < n; i++)
		{
			l.Add(PyDict.Of("role", (i % 2 == 0) ? "user" : "assistant", "content", "m" + (start + i) + new string('字', chars)));
		}
		return l;
	}

	[Fact]
	public void ManySmallMessagesUnderBudgetNotSummarized()
	{
		// 200 条短消息、占用远低于窗口一半：按 token 判断，不压
		int calls = 0;
		var (kept, summary) = Agent._trim_history(Msgs(200), _ => { calls++; return "S"; }, 131072L);
		Assert.Equal(200, kept.Count);
		Assert.Null(summary);
		Assert.Equal(0, calls);
	}

	[Fact]
	public void OverBudgetSummarizesOnceThenReuses()
	{
		// 窗口 2000：超过 1000 token 才压，压完原文留 ≤500 token
		int calls = 0;
		List<object> h = Msgs(12, 0, 100);
		var (kept, summary) = Agent._trim_history(h, _ => { calls++; return "S1"; }, 2000L);
		Assert.Equal(1, calls);
		Assert.Equal(4, kept.Count);
		Assert.NotNull(summary);
		Assert.False(Py.Truthy(summary.Get("reused")));
		h.Add(summary);
		h.AddRange(Msgs(2, 100, 100));
		var (kept2, summary2) = Agent._trim_history(h, _ => { calls++; return "S2"; }, 2000L);
		Assert.Equal(1, calls);
		Assert.True(Py.Truthy(summary2.Get("reused")));
		Assert.Equal(6, kept2.Count);
		Assert.StartsWith("m8", (string)((PyDict)kept2[0]).Get("content"));
	}

	[Fact]
	public void LegacyMarkerEveryTurnIsReused()
	{
		// 旧版本每轮都存一条没有 keep 的标记
		List<object> h = Msgs(60);
		h.Add(PyDict.Of("role", "user", "content", "[历史摘要]\nold", "id", "compact_h60"));
		h.AddRange(Msgs(2, 200));
		int calls = 0;
		var (kept, summary) = Agent._trim_history(h, _ => { calls++; return "S"; });
		Assert.Equal(0, calls);
		Assert.True(Py.Truthy(summary.Get("reused")));
		Assert.Equal(26, kept.Count);
	}

	[Fact]
	public void ApiMessagesKeepsCompactMarker()
	{
		List<object> api = Store.ApiMessages(new List<object>
		{
			PyDict.Of("role", "user", "content", "[历史摘要]\nx", "id", "compact_h9", "keep", 24L),
			PyDict.Of("role", "user", "content", "hi", "id", "u1"),
		});
		Assert.Equal("compact_h9", ((PyDict)api[0]).Get("id"));
		Assert.Null(((PyDict)api[1]).Get("id"));
		List<object> wire = Client.PrepareMessages(new PyDict(), api);
		Assert.Null(((PyDict)wire[0]).Get("id"));
		Assert.Null(((PyDict)wire[0]).Get("keep"));
	}
}
