// AI5-O 的测试(Windows 树):会话级可观测性汇总(轮数 / 工具调用与失败率 / 压缩次数 / tool_schema_miss / token)。
//
// 全部落在 TestHome 的临时数据目录里(Store.SESSIONS_DIR / Trace.TRACE_DIR 都指到它下面),
// 不联网、不启动界面、不碰真实 TIPCL_HOME。核心断言是「汇总值 == 手工按原始事件数出来的值」,
// 即任务书 O 包的验收口径:诊断导出里的数必须与原始记录一致。
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

public class AiSessionMetricsTests
{
	/// <summary>造一段会话事件:2 轮对话 / 4 次工具调用(1 次失败)/ 主动压缩 1 次 + 被动压缩 1 次 / 2 条 Usage。</summary>
	private static void WriteBusySession(string chat)
	{
		Store.LogEvent(chat, "TurnStarted", PyDict.Of("turn_id", "t1", "user_len", 6L));
		Store.LogEvent(chat, "ToolStarted", PyDict.Of("turn_id", "t1", "round", 1L, "tool", "list_mods", "tool_call_id", "c1"));
		Store.LogEvent(chat, "ToolCompleted", PyDict.Of("turn_id", "t1", "round", 1L, "tool", "list_mods", "tool_call_id", "c1", "result_len", 120L, "ok", true));
		Store.LogEvent(chat, "ToolStarted", PyDict.Of("turn_id", "t1", "round", 1L, "tool", "read_file", "tool_call_id", "c2"));
		Store.LogEvent(chat, "ToolCompleted", PyDict.Of("turn_id", "t1", "round", 1L, "tool", "read_file", "tool_call_id", "c2", "result_len", 0L, "ok", false));
		Store.LogEvent(chat, "Usage", PyDict.Of("turn_id", "t1", "prompt_tokens", 1000L, "completion_tokens", 200L, "source", "provider"));
		Store.LogEvent(chat, "CompactBoundary", PyDict.Of("turn_id", "t1", "round", 2L, "trigger", "auto", "pre", 120000L, "post", 40000L));
		Store.LogEvent(chat, "TurnSummary", PyDict.Of("turn_id", "t1", "stop_reason", "no_tool_call", "rounds", 2L, "tools", 2L, "tool_failures", 1L, "elapsed", 3.5));
		Store.LogEvent(chat, "TurnCompleted", PyDict.Of("turn_id", "t1", "rounds", 2L, "stop_reason", "no_tool_call"));

		Store.LogEvent(chat, "TurnStarted", PyDict.Of("turn_id", "t2", "user_len", 4L));
		Store.LogEvent(chat, "ToolStarted", PyDict.Of("turn_id", "t2", "round", 1L, "tool", "fs_write", "tool_call_id", "c3"));
		Store.LogEvent(chat, "ToolStarted", PyDict.Of("turn_id", "t2", "round", 1L, "tool", "fs_write", "tool_call_id", "c4"));
		Store.LogEvent(chat, "ToolCompleted", PyDict.Of("turn_id", "t2", "round", 1L, "tool", "fs_write", "tool_call_id", "c3", "result_len", 10L, "ok", true));
		Store.LogEvent(chat, "ToolCompleted", PyDict.Of("turn_id", "t2", "round", 1L, "tool", "fs_write", "tool_call_id", "c4", "result_len", 10L, "ok", true));
		Store.LogEvent(chat, "Usage", PyDict.Of("turn_id", "t2", "prompt_tokens", 500L, "completion_tokens", 60L, "source", "provider"));
		Store.LogEvent(chat, "ModelRetry", PyDict.Of("turn_id", "t2", "round", 1L, "attempt", 1L, "category", "rate_limited"));
		Store.LogEvent(chat, "CompactBoundary", PyDict.Of("turn_id", "t2", "round", 1L, "trigger", "reactive"));
		Store.LogEvent(chat, "TurnSummary", PyDict.Of("turn_id", "t2", "stop_reason", "max_rounds", "rounds", 3L, "tools", 2L, "tool_failures", 0L, "elapsed", 4.0));
		Store.LogEvent(chat, "TurnFailed", PyDict.Of("turn_id", "t2", "rounds", 3L, "reason", "max_rounds"));
		Store.LogEvent(chat, "TurnRewound", PyDict.Of("turn_id", "t2"));
	}

	private static string Chat(string prefix)
	{
		return prefix + "-" + Guid.NewGuid().ToString("N");
	}

	[Fact]
	public void CountsEveryMetricFromRawEvents()
	{
		using TestHome home = new TestHome();
		string chat = Chat("obs");
		WriteBusySession(chat);
		AiSessionMetrics.Result m = AiSessionMetrics.Build(chat);
		Assert.True(m.HasEvents);
		Assert.Equal(chat, m.Session);
		Assert.Equal(2L, m.TurnsStarted);
		Assert.Equal(1L, m.TurnsCompleted);
		Assert.Equal(1L, m.TurnsFailed);
		// 轮数 = TurnSummary 的 rounds 之和(2 + 3),不是 TurnCompleted 的 2
		Assert.Equal(5L, m.Rounds);
		Assert.True(m.RoundsFromSummary);
		Assert.Equal(4L, m.ToolStarted);
		Assert.Equal(4L, m.ToolCompleted);
		Assert.Equal(1L, m.ToolFailures);
		Assert.Equal(0.25, m.FailureRate, 6);
		Assert.Equal(4L, m.ToolCallsFromSummary);
		Assert.Equal(1L, m.ToolFailuresFromSummary);
		Assert.Equal(1L, m.CompactsAuto);
		Assert.Equal(1L, m.CompactsReactive);
		Assert.Equal(2L, m.Compacts);
		Assert.Equal(1500L, m.InputTokens);
		Assert.Equal(260L, m.OutputTokens);
		Assert.Equal(2L, m.UsageRequests);
		Assert.Equal(1L, m.ModelRetries);
		Assert.Equal(1L, m.Rewinds);
		Assert.Equal(0L, m.ToolSchemaMiss);
		Assert.False(string.IsNullOrEmpty(m.LastEventAt));
		Assert.Empty(m.Caveats);

		PyDict py = m.ToPy();
		Assert.Equal(5L, Py.Int(py.Get("rounds")));
		Assert.Equal(4L, Py.Int(py.Get("tool_calls")));
		Assert.Equal(1L, Py.Int(py.Get("tool_failures")));
		Assert.Equal(2L, Py.Int(py.Get("compacts")));
		Assert.Equal(1500L, Py.Int(py.Get("input_tokens")));
		Assert.Equal(260L, Py.Int(py.Get("output_tokens")));
		Assert.Equal(chat, Py.S(py.Get("session")));
	}

	[Fact]
	public void UnfinishedToolCallCountsAsFailure_AndOldSessionFallsBackToTurnCompleted()
	{
		using TestHome home = new TestHome();
		string chat = Chat("obs-old");
		Store.LogEvent(chat, "TurnStarted", PyDict.Of("turn_id", "t1"));
		Store.LogEvent(chat, "ToolStarted", PyDict.Of("turn_id", "t1", "round", 1L, "tool", "a", "tool_call_id", "c1"));
		Store.LogEvent(chat, "ToolCompleted", PyDict.Of("turn_id", "t1", "round", 1L, "tool", "a", "tool_call_id", "c1", "ok", true));
		Store.LogEvent(chat, "ToolStarted", PyDict.Of("turn_id", "t1", "round", 1L, "tool", "b", "tool_call_id", "c2"));
		Store.LogEvent(chat, "TurnCompleted", PyDict.Of("turn_id", "t1", "rounds", 2L, "stop_reason", "completed"));
		AiSessionMetrics.Result m = AiSessionMetrics.Build(chat);
		Assert.Equal(2L, m.Rounds);
		Assert.False(m.RoundsFromSummary);
		Assert.Equal(2L, m.ToolStarted);
		Assert.Equal(1L, m.ToolCompleted);
		Assert.Equal(1L, m.ToolFailures);
		Assert.Equal(0.5, m.FailureRate, 6);
		Assert.Contains(m.Caveats, c => c.Contains("TurnSummary"));
		Assert.Contains(m.Caveats, c => c.Contains("Usage"));
	}

	[Fact]
	public void MissingSessionFileReportsZeroAndSaysWhy()
	{
		using TestHome home = new TestHome();
		AiSessionMetrics.Result m = AiSessionMetrics.Build(Chat("obs-none"));
		Assert.False(m.HasEvents);
		Assert.Equal(0L, m.Rounds);
		Assert.Equal(0L, m.ToolStarted);
		Assert.Equal(0.0, m.FailureRate, 6);
		Assert.Contains(m.Caveats, c => c.Contains("没有找到会话事件文件"));
	}

	[Fact]
	public void ToolSchemaMissIsCountedFromTraceOfThisSessionOnly()
	{
		using TestHome home = new TestHome();
		string chat = Chat("obs-trace");
		string other = Chat("obs-other");
		using (Trace.SessionScope(chat))
		{
			Trace.Record("tool_schema_miss", null, "", "", null, null, null, "tools", new List<object> { "fs_write" });
			Trace.Record("tool_schema_miss", null, "", "", null, null, null, "tools", new List<object> { "fs_write" });
			Trace.Record("compact_failure", 2, "", "", new ValueError("摘要为空,拒绝替换历史"));
		}
		using (Trace.SessionScope(other))
		{
			Trace.Record("tool_schema_miss", null, "", "", null, null, null, "tools", new List<object> { "list_mods" });
		}
		// 不在任何会话作用域里记的行:不该被算进任何一本会话
		Trace.Record("hook_error", null, "before", "list_mods", new OSError("x"));
		AiSessionMetrics.Result mine = AiSessionMetrics.Build(chat);
		Assert.Equal(2L, mine.ToolSchemaMiss);
		Assert.Equal(1L, mine.CompactFailures);
		AiSessionMetrics.Result theirs = AiSessionMetrics.Build(other);
		Assert.Equal(1L, theirs.ToolSchemaMiss);
		Assert.Equal(0L, theirs.CompactFailures);
	}

	[Fact]
	public async Task SessionScopeFollowsAsyncContinuations()
	{
		using TestHome home = new TestHome();
		string chat = Chat("obs-async");
		string inner = "";
		using (Trace.SessionScope(chat))
		{
			await Task.Delay(1);
			await Task.Yield();
			inner = Trace.CurrentSession;
			Trace.Record("tool_schema_miss", null, "", "", null, null, null, "tools", new List<object>());
		}
		Assert.Equal(chat, inner);
		Assert.Equal("", Trace.CurrentSession);
		Assert.Equal(1L, AiSessionMetrics.Build(chat).ToolSchemaMiss);
	}

	[Fact]
	public void DiagnoseExposesTheSessionMetricsAndText()
	{
		using TestHome home = new TestHome();
		string chat = Chat("obs-diag");
		WriteBusySession(chat);
		PyDict py = Diagnose.SessionMetrics(chat);
		Assert.Equal(chat, Py.S(py.Get("session")));
		Assert.Equal(5L, Py.Int(py.Get("rounds")));
		Assert.Equal(4L, Py.Int(py.Get("tool_calls")));
		Assert.Equal(1L, Py.Int(py.Get("tool_failures")));
		Assert.Equal(2L, Py.Int(py.Get("compacts")));
		Assert.Equal(1500L, Py.Int(py.Get("input_tokens")));
		Assert.Equal(260L, Py.Int(py.Get("output_tokens")));
		string text = Diagnose.SessionMetricsText(chat);
		Assert.Contains("轮次= 2 轮开始 / 1 轮完成 / 1 轮失败;轮数合计 5", text);
		Assert.Contains("工具= 4 次调用(完成 4),失败 1 次,失败率 25.0%", text);
		Assert.Contains("压缩= 主动 1 次 / 被动 1 次(失败 0 次)", text);
		Assert.Contains("token= 输入 1500 / 输出 260", text);
	}
}
