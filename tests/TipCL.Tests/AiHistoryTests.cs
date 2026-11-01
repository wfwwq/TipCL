using System.Collections.Generic;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

public class AiHistoryTests
{
	[Fact]
	public void FailedTurnsAreNotSentToModel()
	{
		List<object> msgs = new List<object>
		{
			PyDict.Of("role", "user", "content", "你好"),
			PyDict.Of("role", "assistant", "content", "在的"),
			PyDict.Of("role", "user", "content", "1"),
			PyDict.Of("role", "error", "content", "401 Invalid token"),
			PyDict.Of("role", "user", "content", "1"),
			PyDict.Of("role", "error", "content", "401 Invalid token"),
		};
		List<object> api = Store.ApiMessages(msgs);
		Assert.Equal(2, api.Count);
		foreach (object m in api)
		{
			Assert.DoesNotContain("401", (string)((PyDict)m).Get("content"));
		}
	}
}
