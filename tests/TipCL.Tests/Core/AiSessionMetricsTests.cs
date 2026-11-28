// AI5-O 的测试:会话级可观测性汇总(轮数 / 工具调用与失败率 / 压缩次数 / tool_schema_miss / token)。
//
// 全部走临时目录(AiServiceDirs 已把 Store.SessionsDir 与 Trace.TraceDir 指到临时根),
// 不联网、不启动界面、不碰真实 TIPCL_HOME。核心断言是「汇总值 == 手工按原始事件数出来的值」,
// 即任务书 O 包的验收口径:诊断导出里的数必须与原始记录一致。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

[Collection(TipCL.Tests.GlobalStateCollection.Name)]
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

    [Fact]
    public void BuildCountsEveryMetricFromRawEvents()
    {
        using var dirs = new AiServiceDirs();
        string chat = "obs-" + Guid.NewGuid().ToString("N");
        WriteBusySession(chat);

        AiSessionMetrics.Result m = AiSessionMetrics.Build(chat);

        Assert.True(m.HasEvents);
        Assert.Equal(chat, m.Session);
        Assert.Equal(2, m.TurnsStarted);
        Assert.Equal(1, m.TurnsCompleted);
        Assert.Equal(1, m.TurnsFailed);
        // 轮数 = TurnSummary 的 rounds 之和(2 + 3),不是 TurnCompleted 的 2
        Assert.Equal(5, m.Rounds);
        Assert.True(m.RoundsFromSummary);
        // 工具:4 次开始、4 次完成,其中 1 次 ok=false
        Assert.Equal(4, m.ToolStarted);
        Assert.Equal(4, m.ToolCompleted);
        Assert.Equal(1, m.ToolFailures);
        Assert.Equal(0.25, m.FailureRate, 6);
        // TurnSummary 里的工具数与事件流一致
        Assert.Equal(4, m.ToolCallsFromSummary);
        Assert.Equal(1, m.ToolFailuresFromSummary);
        // 压缩:主动(auto)1 次、被动(reactive)1 次
        Assert.Equal(1, m.CompactsAuto);
        Assert.Equal(1, m.CompactsReactive);
        Assert.Equal(2, m.Compacts);
        // token:两条 provider 真值之和
        Assert.Equal(1500, m.InputTokens);
        Assert.Equal(260, m.OutputTokens);
        Assert.Equal(2, m.UsageRequests);
        Assert.Equal(1, m.ModelRetries);
        Assert.Equal(1, m.Rewinds);
        Assert.Equal(0, m.ToolSchemaMiss);
        Assert.False(string.IsNullOrEmpty(m.LastEventAt));
        // 事件流自洽(开始=完成=4、真值 Usage 有 2 条),不该有 caveat
        Assert.Empty(m.Caveats);

        // 机器可读的一份与上面同源同值
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
        using var dirs = new AiServiceDirs();
        string chat = "obs-old-" + Guid.NewGuid().ToString("N");
        // 旧格式:没有 TurnSummary,也没有 Usage;第二次工具调用只有开始没有结束(进程在那一步没了)
        Store.LogEvent(chat, "TurnStarted", PyDict.Of("turn_id", "t1"));
        Store.LogEvent(chat, "ToolStarted", PyDict.Of("turn_id", "t1", "round", 1L, "tool", "a", "tool_call_id", "c1"));
        Store.LogEvent(chat, "ToolCompleted", PyDict.Of("turn_id", "t1", "round", 1L, "tool", "a", "tool_call_id", "c1", "ok", true));
        Store.LogEvent(chat, "ToolStarted", PyDict.Of("turn_id", "t1", "round", 1L, "tool", "b", "tool_call_id", "c2"));
        Store.LogEvent(chat, "TurnCompleted", PyDict.Of("turn_id", "t1", "rounds", 2L, "stop_reason", "completed"));

        AiSessionMetrics.Result m = AiSessionMetrics.Build(chat);

        Assert.Equal(2, m.Rounds); // 退回 TurnCompleted 的 rounds
        Assert.False(m.RoundsFromSummary);
        Assert.Equal(2, m.ToolStarted);
        Assert.Equal(1, m.ToolCompleted);
        Assert.Equal(1, m.ToolFailures); // 只有开始没有结束的那次按失败计
        Assert.Equal(0.5, m.FailureRate, 6);
        Assert.Contains(m.Caveats, c => c.Contains("TurnSummary"));
        Assert.Contains(m.Caveats, c => c.Contains("Usage"));
    }

    [Fact]
    public void MissingSessionFileReportsZeroAndSaysWhy()
    {
        using var dirs = new AiServiceDirs();
        AiSessionMetrics.Result m = AiSessionMetrics.Build("obs-none-" + Guid.NewGuid().ToString("N"));
        Assert.False(m.HasEvents);
        Assert.Equal(0, m.Rounds);
        Assert.Equal(0, m.ToolStarted);
        Assert.Equal(0.0, m.FailureRate, 6);
        Assert.Contains(m.Caveats, c => c.Contains("没有找到会话事件文件"));
    }

    [Fact]
    public void ToolSchemaMissAndCompactFailureAreCountedFromTraceOfThisSessionOnly()
    {
        using var dirs = new AiServiceDirs();
        string chat = "obs-trace-" + Guid.NewGuid().ToString("N");
        string other = "obs-other-" + Guid.NewGuid().ToString("N");

        // 轨迹行靠 Trace.SessionScope 归属:同一线程上先后两本会话,行不会串
        using (Trace.SessionScope(chat))
        {
            Trace.Record("tool_schema_miss", null, "", "", null, null, null, "tools", new List<object> { "fs_write" });
            Trace.Record("tool_schema_miss", null, "", "", null, null, null, "tools", new List<object> { "fs_write" });
            Trace.Record("compact_failed", 2, "", "", new ValueError("摘要为空,拒绝替换历史"));
        }
        using (Trace.SessionScope(other))
        {
            Trace.Record("tool_schema_miss", null, "", "", null, null, null, "tools", new List<object> { "list_mods" });
        }
        // 不在任何会话作用域里记的行:不该被算进任何一本会话
        Trace.Record("hook_error", null, "before", "list_mods", new OSError("x"));

        AiSessionMetrics.Result mine = AiSessionMetrics.Build(chat);
        Assert.Equal(2, mine.ToolSchemaMiss);
        Assert.Equal(1, mine.CompactFailures);

        AiSessionMetrics.Result theirs = AiSessionMetrics.Build(other);
        Assert.Equal(1, theirs.ToolSchemaMiss);
        Assert.Equal(0, theirs.CompactFailures);
    }

    [Fact]
    public async Task SessionScopeFollowsAsyncContinuations()
    {
        using var dirs = new AiServiceDirs();
        string chat = "obs-async-" + Guid.NewGuid().ToString("N");
        string inner = "";
        using (Trace.SessionScope(chat))
        {
            await Task.Delay(1);
            await Task.Yield();
            inner = Trace.CurrentSession;
            Trace.Record("tool_schema_miss", null, "", "", null, null, null, "tools", new List<object>());
        }
        Assert.Equal(chat, inner);
        Assert.Equal("", Trace.CurrentSession); // 作用域结束就还原
        Assert.Equal(1, AiSessionMetrics.Build(chat).ToolSchemaMiss);
    }

    [Fact]
    public void DiagnosticReportCarriesTheMetricsSectionAndMetricsJson()
    {
        using var dirs = new AiServiceDirs();
        using var t = new NfTempInstance("obs-diag");
        string chat = "obs-" + Guid.NewGuid().ToString("N");
        WriteBusySession(chat);

        var convo = new List<object>
        {
            PyDict.Of("role", "user", "content", "帮我看看这个实例"),
            PyDict.Of("role", "assistant", "content", "看完了"),
        };
        string dest = Path.Combine(Path.GetTempPath(), "tipcl-obs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        try
        {
            var args = PyDict.Of("dest_dir", dest, "instance", t.Name);
            string path = DiagnosticReport.ResolveOutPath(args, t.Name);
            PyDict r = DiagnosticReport.Export(t.Inst, convo, path, PyDict.Of("ram_mb", 16384L, "gpu", "NVIDIA GeForce RTX 3060"), null, chat);
            Assert.True(Py.Truthy(r.Get("ok")));

            using ZipArchive zip = ZipFile.OpenRead(path);
            var names = zip.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("report.md", names);
            Assert.Contains("metrics.json", names);

            using var md = new StreamReader(zip.Entries.First(e => e.FullName == "report.md").Open());
            string text = md.ReadToEnd();
            Assert.Contains("会话级统计", text);
            Assert.Contains("轮次= 2 轮开始 / 1 轮完成 / 1 轮失败;轮数合计 5", text);
            Assert.Contains("工具= 4 次调用(完成 4),失败 1 次,失败率 25.0%", text);
            Assert.Contains("压缩= 主动 1 次 / 被动 1 次(失败 0 次)", text);
            Assert.Contains("schema_miss= 0 次", text);
            Assert.Contains("token= 输入 1500 / 输出 260", text);
            // 会话 id 太长时只留前 8 位,报告里不出现完整 id(发出去也不泄露对话归属)
            Assert.DoesNotContain(chat, text);
            Assert.Contains(chat.Substring(0, 8), text);

            using var mj = new StreamReader(zip.Entries.First(e => e.FullName == "metrics.json").Open());
            PyDict metrics = Py.D(PyJson.Loads(mj.ReadToEnd()));
            Assert.Equal(chat, Py.S(metrics.Get("session")));
            Assert.Equal(5L, Py.Int(metrics.Get("rounds")));
            Assert.Equal(4L, Py.Int(metrics.Get("tool_calls")));
            Assert.Equal(1500L, Py.Int(metrics.Get("input_tokens")));
            Assert.Equal(2L, Py.Int(metrics.Get("compacts")));
        }
        finally
        {
            try { Directory.Delete(dest, true); } catch (Exception) { }
        }
    }
}
