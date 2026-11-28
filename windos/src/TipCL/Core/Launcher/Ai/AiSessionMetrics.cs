// AI5-O(会话级可观测性汇总,Windows 树):把已有的两份落盘记录汇成一屏能看的数,交给诊断导出。
//
// i18n:ignore-file —— 本文件是诊断汇总器:产出的是诊断报告正文里的字段标签与 caveat 文字,
// 随导出的报告文本走,不是界面文案,也不随语言切换。
//
// 与 Linux 侧同名同义(见 src/TipCL.Core/Launcher/Ai/AiSessionMetrics.cs),字段口径一致:
//   · Store.LogEvent($TIPCL_HOME/cache/ai_sessions/<会话>.jsonl):TurnStarted / TurnCompleted /
//     TurnFailed / ToolStarted / ToolCompleted / TurnSummary / Usage / CompactBoundary / ModelRetry;
//   · Trace.Record($TIPCL_HOME/cache/ai_trace/YYYYMMDD.jsonl):tool_schema_miss、compact_circuit_break /
//     compact_failure 等排查用的轨迹行(AI5-O 起带 session 字段,见 Trace.SessionScope)。
// Windows 侧本来就在 _record_usage 里落 Usage 事件、在 AgentResult 里落 TurnSummary 事件(Agent.cs:1619、
// :1652),所以这里不需要动 Agent 的记账逻辑 —— 只有轨迹归属需要 Agent 设一次作用域。
// 纯读:不改任何既有事件、不影响对话行为;读不到就如实留 0 并写进 caveats。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace TipCL.Core.Launcher.Ai;

/// <summary>会话级可观测性汇总(轮数 / 工具 / 压缩 / token / 轨迹计数)。</summary>
public static class AiSessionMetrics
{
	/// <summary>一个会话的统计结果,同时是人读的文本行与机器读的 PyDict 的来源。</summary>
	public sealed class Result
	{
		public string Session = "";

		public string SessionFile = "";

		public string TraceDir = "";

		public int TraceDays = 1;

		public bool HasEvents;

		public int TraceFiles;

		public string LastEventAt = "";

		public long TurnsStarted;

		public long TurnsCompleted;

		public long TurnsFailed;

		public long Rounds;

		public bool RoundsFromSummary = true;

		public long ToolStarted;

		public long ToolCompleted;

		public long ToolFailures;

		public long ToolCallsFromSummary;

		public long ToolFailuresFromSummary;

		public double FailureRate;

		public long CompactsAuto;

		public long CompactsReactive;

		public long Compacts => CompactsAuto + CompactsReactive;

		public long CompactFailures;

		public long ToolSchemaMiss;

		public long InputTokens;

		public long OutputTokens;

		public long UsageRequests;

		public long ModelRetries;

		public long Rewinds;

		public PyDict UsageLive = new PyDict();

		public List<string> Caveats = new List<string>();

		/// <summary>机器可读的一份(诊断导出里按字段名取)。</summary>
		public PyDict ToPy()
		{
			List<object> caveats = new List<object>();
			foreach (string caveat in Caveats)
			{
				caveats.Add(caveat);
			}
			return PyDict.Of("session", Session, "session_file", SessionFile, "trace_dir", TraceDir,
				"trace_days", (long)TraceDays, "has_events", HasEvents,
				"turns_started", TurnsStarted, "turns_completed", TurnsCompleted, "turns_failed", TurnsFailed,
				"rounds", Rounds,
				"tool_started", ToolStarted, "tool_completed", ToolCompleted, "tool_calls", ToolStarted,
				"tool_failures", ToolFailures, "tool_failure_rate", FailureRate,
				"tool_calls_from_turn_summary", ToolCallsFromSummary,
				"tool_failures_from_turn_summary", ToolFailuresFromSummary,
				"compacts_auto", CompactsAuto, "compacts_reactive", CompactsReactive, "compacts", Compacts,
				"compact_failures", CompactFailures, "tool_schema_miss", ToolSchemaMiss,
				"input_tokens", InputTokens, "output_tokens", OutputTokens, "usage_requests", UsageRequests,
				"model_retries", ModelRetries, "rewinds", Rewinds,
				"usage_live", UsageLive, "last_event_at", LastEventAt, "trace_files", (long)TraceFiles,
				"caveats", caveats);
		}

		/// <summary>人读的几行(诊断导出里直接拼进文本)。</summary>
		public List<string> ToLines()
		{
			double num = FailureRate * 100.0;
			return new List<string>
			{
				"- 会话= " + ((Session.Length > 0) ? Session : "(空)"),
				"- 轮次= " + TurnsStarted + " 轮开始 / " + TurnsCompleted + " 轮完成 / " + TurnsFailed + " 轮失败;轮数合计 " + Rounds,
				"- 工具= " + ToolStarted + " 次调用(完成 " + ToolCompleted + "),失败 " + ToolFailures + " 次,失败率 " + num.ToString("0.0", CultureInfo.InvariantCulture) + "%",
				"- 压缩= 主动 " + CompactsAuto + " 次 / 被动 " + CompactsReactive + " 次(失败 " + CompactFailures + " 次)",
				"- schema_miss= " + ToolSchemaMiss + " 次(最近 " + TraceDays + " 天轨迹窗口内,按 session 字段归集)",
				"- token= 输入 " + InputTokens + " / 输出 " + OutputTokens + "(来自 " + UsageRequests + " 条 provider 真值记录)",
				"- 重试= " + ModelRetries + " 次;撤回= " + Rewinds + " 次",
				"- 数据来源= " + SessionFile + ";轨迹目录= " + TraceDir,
			};
		}
	}

	/// <summary>汇总一本会话。sessionId 空 = 用 Trace.CurrentSession,再空就 "active"。</summary>
	public static Result Build(string sessionId = null, int traceDays = 1)
	{
		string text = Clean(sessionId);
		string currentSession = Trace.CurrentSession;
		Result result = new Result();
		result.Session = text;
		result.SessionFile = PPath.Abs(PPath.Join(Store.SessionDirPath(), text + ".jsonl"));
		result.TraceDir = Trace.TRACE_DIR ?? PPath.Join(Utils.ROOT, "cache", "ai_trace");
		result.TraceDays = Math.Max(1, traceDays);
		result.UsageLive = Usage.Get(text);
		if (currentSession.Length > 0 && !string.Equals(currentSession, text, StringComparison.Ordinal))
		{
			result.Caveats.Add("轨迹作用域里的会话是「" + currentSession + "」,与本次汇总的会话不一致(报告仍按会话文件来算)");
		}
		List<PyDict> list = Store.ReadEvents(text);
		result.HasEvents = list.Count > 0;
		if (!result.HasEvents)
		{
			result.Caveats.Add("没有找到会话事件文件(这轮对话可能还没跑过,或者事件已被清理规则删掉)");
		}
		long num = 0L;
		long num2 = 0L;
		List<string> list2 = new List<string>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		foreach (PyDict item in list)
		{
			string text2 = Py.S(item.Get("event"));
			if (text2.Length > 0)
			{
				result.LastEventAt = Py.S(item.Get("ts"));
			}
			switch (text2)
			{
			case "TurnStarted":
				result.TurnsStarted++;
				break;
			case "TurnCompleted":
				result.TurnsCompleted++;
				num += Py.Int(Py.Or(item.Get("rounds"), 0L));
				break;
			case "TurnFailed":
				result.TurnsFailed++;
				num += Py.Int(Py.Or(item.Get("rounds"), 0L));
				break;
			case "TurnSummary":
				num2 += Py.Int(Py.Or(item.Get("rounds"), 0L));
				result.ToolCallsFromSummary += Py.Int(Py.Or(item.Get("tools"), 0L));
				result.ToolFailuresFromSummary += Py.Int(Py.Or(item.Get("tool_failures"), 0L));
				break;
			case "ToolStarted":
				result.ToolStarted++;
				list2.Add(Py.S(item.Get("tool_call_id")));
				break;
			case "ToolCompleted":
				result.ToolCompleted++;
				hashSet.Add(Py.S(item.Get("tool_call_id")));
				if (!Py.Truthy(item.Get("ok")))
				{
					result.ToolFailures++;
				}
				break;
			case "ToolFailed":
				// Windows 在工具抛异常时另发一条 ToolFailed(见 Agent.cs:1414);没有 ToolCompleted 跟着,
				// 所以按「只有开始没有结束」在下面统一计一次,这里不动,免得同一失败数两遍。
				break;
			case "CompactBoundary":
				if (Py.S(item.Get("trigger")) == "reactive")
				{
					result.CompactsReactive++;
				}
				else
				{
					result.CompactsAuto++;
				}
				break;
			case "Usage":
				result.InputTokens += Py.Int(Py.Or(item.Get("prompt_tokens"), 0L));
				result.OutputTokens += Py.Int(Py.Or(item.Get("completion_tokens"), 0L));
				result.UsageRequests++;
				break;
			case "ModelRetry":
			case "ModelFallback":
				result.ModelRetries++;
				break;
			case "TurnRewound":
				result.Rewinds++;
				break;
			}
		}
		if (num2 > 0L)
		{
			result.Rounds = num2;
			result.RoundsFromSummary = true;
		}
		else
		{
			result.Rounds = num;
			result.RoundsFromSummary = false;
			if (result.TurnsCompleted + result.TurnsFailed > 0)
			{
				result.Caveats.Add("没有 TurnSummary 事件(旧会话文件),轮数退回 TurnCompleted 的 rounds:" + num + ";工具数退回 ToolStarted:" + result.ToolStarted);
			}
		}
		foreach (string item2 in list2)
		{
			if (!hashSet.Contains(item2))
			{
				result.ToolFailures++;
			}
		}
		result.FailureRate = ((result.ToolStarted > 0) ? ((double)result.ToolFailures / (double)result.ToolStarted) : 0.0);
		if (result.ToolCompleted != result.ToolStarted)
		{
			result.Caveats.Add("工具开始 " + result.ToolStarted + " 次、结束 " + result.ToolCompleted + " 次,差额按失败计(多数是那一轮被中断 / 进程退出)");
		}
		if (result.ToolCallsFromSummary > 0 && result.ToolCallsFromSummary != result.ToolStarted)
		{
			result.Caveats.Add("TurnSummary 记的工具数(" + result.ToolCallsFromSummary + ")与 ToolStarted 事件数(" + result.ToolStarted + ")不一致,报告以事件流为准");
		}
		if (result.UsageRequests == 0 || result.InputTokens + result.OutputTokens == 0)
		{
			result.Caveats.Add("会话事件里没有 provider 真值的 Usage 行,token 显示 0;本次进程内存里的那份见 usage_live");
		}
		List<PyDict> list3;
		try
		{
			list3 = Trace.ReadRecords(result.TraceDays);
		}
		catch (Exception)
		{
			list3 = new List<PyDict>();
		}
		try
		{
			result.TraceFiles = (Directory.Exists(PPath.Abs(result.TraceDir)) ? Directory.GetFiles(PPath.Abs(result.TraceDir), "*.jsonl").Length : 0);
		}
		catch (Exception)
		{
			result.TraceFiles = 0;
		}
		foreach (PyDict item3 in list3)
		{
			if (string.Equals(Py.S(item3.Get("session")), text, StringComparison.Ordinal))
			{
				switch (Py.S(item3.Get("event")))
				{
				case "tool_schema_miss":
					result.ToolSchemaMiss++;
					break;
				case "compact_failed":
				case "compact_failure":
				case "compact_circuit_break":
					result.CompactFailures++;
					break;
				}
			}
		}
		return result;
	}

	private static string Clean(string sessionId)
	{
		string text = Py.Strip(sessionId ?? "");
		if (text.Length == 0)
		{
			text = Py.Strip(Trace.CurrentSession);
		}
		return (text.Length == 0) ? "active" : text;
	}
}
