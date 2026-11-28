// i18n:ignore-file —— 本文件是诊断汇总器:产出的是诊断报告正文里的字段标签与 caveat 文字,
// 随导出的 report.md / metrics.json 走,不是界面文案,也不随语言切换(与 DiagnosticReport.cs
// 里那批报告字面量同一性质,只是本文件字面量更集中)。要清掉这条标记,得先给报告正文本身做词表化。
// AI5-O(会话级可观测性汇总):把已有的两份落盘记录汇成一屏能看的数,交给诊断导出。
//
// 为什么是「汇总」而不是「新记一份」:这几项指标本来就散在两处,只是没人合起来看:
//   · Store.LogEvent($TIPCL_HOME/cache/ai_sessions/<会话>.jsonl):TurnStarted / TurnCompleted /
//     TurnFailed / ToolStarted / ToolCompleted / TurnSummary / Usage / CompactBoundary / ModelRetry;
//   · Trace.Record($TIPCL_HOME/cache/ai_trace/YYYYMMDD.jsonl):tool_schema_miss、compact_failed、
//     retry、mcp_* 等排查用的轨迹行(AI5-O 起带 session 字段,见 Trace.SessionScope)。
// Usage.Get 只有本次进程内的数,重启就没了,所以 token 以会话事件里的 Usage 行为准。
//
// 口径全部照 Windows 版 Agent.cs 的既有事件字段(rounds / tools / tool_failures / elapsed /
// prompt_tokens / completion_tokens / trigger=auto|reactive),两棵树同名同义,便于对照。
// 纯读:不改任何既有事件、不影响对话行为;读不到就如实留 0 并写进 caveats。
using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>会话级可观测性汇总(轮数 / 工具 / 压缩 / token / 轨迹计数)。</summary>
public static class AiSessionMetrics
{
    /// <summary>一个会话的统计结果,同时是人读的 markdown 与机器读的 JSON 的来源。</summary>
    public sealed class Result
    {
        /// <summary>会话 id(Store 的事件文件名,也是诊断导出所属的对话)。</summary>
        public string Session = "";

        /// <summary>统计读取的会话事件文件(绝对路径)。</summary>
        public string SessionFile = "";

        /// <summary>轨迹目录(读 tool_schema_miss 等计数的地方)。</summary>
        public string TraceDir = "";

        /// <summary>读了最近几天(含今天)的轨迹,默认 1。数值会随天数变化,报告里要写明。</summary>
        public int TraceDays = 1;

        /// <summary>事件文件存在(不存在 = 这本会话还没记过任何事件)。</summary>
        public bool HasEvents;

        /// <summary>轨迹目录里找到的文件数。</summary>
        public int TraceFiles;

        /// <summary>事件文件最后一条的时间戳(ISO 8601 精确到毫秒);没有就是空串。</summary>
        public string LastEventAt = "";

        public long TurnsStarted;

        public long TurnsCompleted;

        public long TurnsFailed;

        /// <summary>轮数:各次 TurnSummary 的 rounds 之和(缺 TurnSummary 时退回各次 TurnCompleted 的 rounds 之和)。</summary>
        public long Rounds;

        /// <summary>轮数是否取自 TurnSummary(false = 退回 TurnCompleted,口径略窄,写进 caveats)。</summary>
        public bool RoundsFromSummary = true;

        public long ToolStarted;

        public long ToolCompleted;

        /// <summary>失败的工具调用数:ToolCompleted 里 ok=false 的条数 + 只有 ToolStarted 没有完成的条数。</summary>
        public long ToolFailures;

        public long ToolCallsFromSummary;

        public long ToolFailuresFromSummary;

        /// <summary>失败率 = 失败 / 调用(调用为 0 时为 0)。</summary>
        public double FailureRate;

        public long CompactsAuto;

        public long CompactsReactive;

        /// <summary>压缩次数(主动 + 被动)。</summary>
        public long Compacts => CompactsAuto + CompactsReactive;

        public long CompactFailures;

        public long ToolSchemaMiss;

        public long InputTokens;

        public long OutputTokens;

        /// <summary>provider 给了真值的请求数(Usage 事件条数)。</summary>
        public long UsageRequests;

        public long ModelRetries;

        public long Rewinds;

        /// <summary>本次进程内存里那份 Usage 的数(与落盘值对照用;重启后为 0 属正常)。</summary>
        public PyDict UsageLive = new();

        /// <summary>口径说明 / 本次读不到的原因(空 = 没有要提醒的)。</summary>
        public List<string> Caveats = new();

        /// <summary>机器可读的一份(诊断导出会以 metrics.json 一并落盘)。</summary>
        public PyDict ToPy() => PyDict.Of(
            "session", Session,
            "session_file", SessionFile,
            "trace_dir", TraceDir,
            "trace_days", (long)TraceDays,
            "has_events", HasEvents,
            "turns_started", TurnsStarted,
            "turns_completed", TurnsCompleted,
            "turns_failed", TurnsFailed,
            "rounds", Rounds,
            "tool_started", ToolStarted,
            "tool_completed", ToolCompleted,
            "tool_calls", ToolStarted,
            "tool_failures", ToolFailures,
            "tool_failure_rate", FailureRate,
            "tool_calls_from_turn_summary", ToolCallsFromSummary,
            "tool_failures_from_turn_summary", ToolFailuresFromSummary,
            "compacts_auto", CompactsAuto,
            "compacts_reactive", CompactsReactive,
            "compacts", Compacts,
            "compact_failures", CompactFailures,
            "tool_schema_miss", ToolSchemaMiss,
            "input_tokens", InputTokens,
            "output_tokens", OutputTokens,
            "usage_requests", UsageRequests,
            "model_retries", ModelRetries,
            "rewinds", Rewinds,
            "usage_live", UsageLive,
            "last_event_at", LastEventAt,
            "trace_files", (long)TraceFiles,
            "caveats", Caveats.Cast<object>().ToList());

        /// <summary>人读的一段(markdown 片段,诊断报告里单独一节)。</summary>
        public List<string> ToMarkdownLines()
        {
            double rate = FailureRate * 100.0;
            return new List<string>
            {
                "- 会话= " + (Session.Length > 0 ? Session : "(空)"),
                "- 轮次= " + TurnsStarted + " 轮开始 / " + TurnsCompleted + " 轮完成 / " + TurnsFailed + " 轮失败;轮数合计 " + Rounds,
                "- 工具= " + ToolStarted + " 次调用(完成 " + ToolCompleted + "),失败 " + ToolFailures +
                    " 次,失败率 " + rate.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%",
                "- 压缩= 主动 " + CompactsAuto + " 次 / 被动 " + CompactsReactive + " 次(失败 " + CompactFailures + " 次)",
                "- schema_miss= " + ToolSchemaMiss + " 次(最近 " + TraceDays + " 天轨迹窗口内,按 session 字段归集)",
                "- token= 输入 " + InputTokens + " / 输出 " + OutputTokens + "(来自 " + UsageRequests + " 条 provider 真值记录)",
                "- 重试= " + ModelRetries + " 次;撤回= " + Rewinds + " 次",
                "- 数据来源= " + SessionFile + ";轨迹目录= " + TraceDir,
            };
        }
    }

    /// <summary>汇总一本会话。<paramref name="sessionId"/> 空 = 用 <see cref="Trace.CurrentSession"/>,再空就 "active"。</summary>
    public static Result Build(string? sessionId = null, int traceDays = 1)
    {
        string session = Clean(sessionId);
        string fromScope = Trace.CurrentSession;
        string file = PPath.Join(Store.SessionDirPath(), session + ".jsonl");
        var result = new Result
        {
            Session = session,
            SessionFile = PPath.Abs(file),
            TraceDir = Trace.TraceDir ?? PPath.Join(Utils.ROOT, "cache", "ai_trace"),
            TraceDays = Math.Max(1, traceDays),
            UsageLive = Usage.Get(session),
        };
        if (fromScope.Length > 0 && !string.Equals(fromScope, session, StringComparison.Ordinal))
        {
            result.Caveats.Add("轨迹作用域里的会话是「" + fromScope + "」,与本次汇总的会话不一致(报告仍按会话文件来算)");
        }

        List<PyDict> events = Store.ReadEvents(session);
        result.HasEvents = events.Count > 0;
        if (!result.HasEvents)
        {
            result.Caveats.Add("没有找到会话事件文件(这轮对话可能还没跑过,或者事件已被 30 天 / 4 MB 的清理规则删掉)");
        }

        // 1) 会话事件:轮次 / 工具 / 压缩 / token / 重试 / 撤回
        long turnsRounds = 0;
        long summaryRounds = 0;
        // 未闭合的工具调用靠 tool_call_id 对:只有 ToolStarted 没有 ToolCompleted 的那次算失败
        var startedToolIds = new List<string>();
        var completedToolIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (PyDict ev in events)
        {
            string kind = Py.S(ev.Get("event"));
            if (kind.Length > 0)
            {
                result.LastEventAt = Py.S(ev.Get("ts"));
            }
            switch (kind)
            {
                case "TurnStarted":
                    result.TurnsStarted++;
                    break;
                case "TurnCompleted":
                    result.TurnsCompleted++;
                    turnsRounds += Py.Int(Py.Or(ev.Get("rounds"), 0L));
                    break;
                case "TurnFailed":
                    result.TurnsFailed++;
                    turnsRounds += Py.Int(Py.Or(ev.Get("rounds"), 0L));
                    break;
                case "TurnSummary":
                    summaryRounds += Py.Int(Py.Or(ev.Get("rounds"), 0L));
                    result.ToolCallsFromSummary += Py.Int(Py.Or(ev.Get("tools"), 0L));
                    result.ToolFailuresFromSummary += Py.Int(Py.Or(ev.Get("tool_failures"), 0L));
                    break;
                case "ToolStarted":
                    result.ToolStarted++;
                    startedToolIds.Add(Py.S(ev.Get("tool_call_id")));
                    break;
                case "ToolCompleted":
                    result.ToolCompleted++;
                    completedToolIds.Add(Py.S(ev.Get("tool_call_id")));
                    if (!Py.Truthy(ev.Get("ok")))
                    {
                        result.ToolFailures++;
                    }
                    break;
                case "CompactBoundary":
                    if (Py.S(ev.Get("trigger")) == "reactive")
                    {
                        result.CompactsReactive++;
                    }
                    else
                    {
                        result.CompactsAuto++;
                    }
                    break;
                case "Usage":
                    result.InputTokens += Py.Int(Py.Or(ev.Get("prompt_tokens"), 0L));
                    result.OutputTokens += Py.Int(Py.Or(ev.Get("completion_tokens"), 0L));
                    result.UsageRequests++;
                    break;
                case "ModelRetry":
                    result.ModelRetries++;
                    break;
                case "TurnRewound":
                    result.Rewinds++;
                    break;
            }
        }

        // 轮数优先用 TurnSummary(那是「真正跑了几轮工具循环」的口径,照 Windows 版 TurnSummary 事件);
        // 早期版本的会话文件没有 TurnSummary,退回 TurnCompleted 的 rounds,并在 caveats 里说清。
        if (summaryRounds > 0)
        {
            result.Rounds = summaryRounds;
            result.RoundsFromSummary = true;
        }
        else
        {
            result.Rounds = turnsRounds;
            result.RoundsFromSummary = false;
            if (result.TurnsCompleted + result.TurnsFailed > 0)
            {
                result.Caveats.Add("没有 TurnSummary 事件(旧会话文件),轮数退回 TurnCompleted 的 rounds:"
                    + turnsRounds + ";工具数退回 ToolStarted:" + result.ToolStarted);
            }
        }

        // 「只有开始没有结束」= 进程在那次工具调用里没了;算失败,别让它把失败率稀释掉
        foreach (string id in startedToolIds)
        {
            if (!completedToolIds.Contains(id))
            {
                result.ToolFailures++;
            }
        }
        result.FailureRate = result.ToolStarted > 0 ? (double)result.ToolFailures / result.ToolStarted : 0.0;

        if (result.ToolCompleted != result.ToolStarted)
        {
            result.Caveats.Add("工具开始 " + result.ToolStarted + " 次、结束 " + result.ToolCompleted +
                " 次,差额按失败计(多数是那一轮被中断 / 进程退出)");
        }
        if (result.ToolCallsFromSummary > 0 && result.ToolCallsFromSummary != result.ToolStarted)
        {
            result.Caveats.Add("TurnSummary 记的工具数(" + result.ToolCallsFromSummary + ")与 ToolStarted 事件数(" +
                result.ToolStarted + ")不一致,报告以事件流为准");
        }
        if (result.UsageRequests == 0 || result.InputTokens + result.OutputTokens == 0)
        {
            result.Caveats.Add("会话事件里没有 provider 真值的 Usage 行(旧会话文件、纯估算轮次或全部走了估算分支),"
                + "token 显示 0;本次进程内存里的那份见 usage_live");
        }

        // 2) 轨迹:tool_schema_miss / 压缩失败等排查计数(AI5-O 起轨迹带 session 字段)
        List<PyDict> traceRows;
        try
        {
            traceRows = Trace.ReadRecords(result.TraceDays);
        }
        catch (Exception)
        {
            traceRows = new List<PyDict>();
        }
        try
        {
            result.TraceFiles = System.IO.Directory.Exists(PPath.Abs(result.TraceDir))
                ? System.IO.Directory.GetFiles(PPath.Abs(result.TraceDir), "*.jsonl").Length
                : 0;
        }
        catch (Exception)
        {
            result.TraceFiles = 0;
        }
        foreach (PyDict row in traceRows)
        {
            if (!string.Equals(Py.S(row.Get("session")), session, StringComparison.Ordinal))
            {
                continue;
            }
            switch (Py.S(row.Get("event")))
            {
                case "tool_schema_miss":
                    result.ToolSchemaMiss++;
                    break;
                case "compact_failed":
                case "compact_failure":
                    result.CompactFailures++;
                    break;
            }
        }

        return result;
    }

    private static string Clean(string? sessionId)
    {
        string s = Py.Strip(sessionId ?? "");
        if (s.Length == 0)
        {
            s = Py.Strip(Trace.CurrentSession);
        }
        return s.Length == 0 ? "active" : s;
    }
}
