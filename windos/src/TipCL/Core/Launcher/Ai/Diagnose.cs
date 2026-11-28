using System;
using System.Collections.Generic;
using System.Linq;
namespace TipCL.Core.Launcher.Ai;

public static class Diagnose
{
	public static PyDict Run(Instance instance, string extraLog = "", string version = "")
	{
		// 版本开启隔离时日志/崩溃报告在 versions\<版本>\ 下（与 --gameDir 一致）
		PyDict pyDict = Crash.AnalyzeInstance(instance, extraLog, manual: true, version);
		List<object> list = new List<object>();
		foreach (PyDict item in Py.ListOr(Py.Or(pyDict.Get("reasons"), new List<object>())))
		{
			List<object> list2 = Py.ListOr(Py.Or(item.Get("extra"), new List<object>()));
			list.Add(PyDict.Of("code", item.Get("code"), "title", Py.Or(item.Get("title"), item.Get("code")), "snippet", (list2.Count > 0) ? Py.Slice(string.Join("\n", list2.Select(Py.Str)), null, 400) : Py.Or(pyDict.Get("summary"), "")));
		}
		object obj = "";
		foreach (object item2 in Py.ListOr(Py.Or(pyDict.Get("files"), new List<object>())))
		{
			if (Py.Lower(Py.Str(item2)).EndsWith("latest.log", StringComparison.Ordinal))
			{
				obj = item2;
				break;
			}
		}
		return PyDict.Of("instance", Py.Or(pyDict.Get("instance"), instance.name), "has_latest", Py.Truthy(pyDict.Get("has_latest")), "has_crash", Py.Truthy(pyDict.Get("has_crash")), "has_hs_err", Py.Truthy(pyDict.Get("has_hs_err")), "latest_path", obj, "crash_path", Py.Or(pyDict.Get("direct_file"), ""), "findings", list, "hint", Py.Or(pyDict.Get("summary"), pyDict.Get("detail"), ""), "detail", Py.Or(pyDict.Get("detail"), ""), "latest_tail", Py.Or(pyDict.Get("log_mc"), ""), "crash_tail", Py.Or(pyDict.Get("log_crash"), ""));
	}

	public static PyDict CollectLogs(Instance instance, string version = "")
	{
		// 版本开启隔离时按 --gameDir 读 versions\<版本>\ 下的日志
		string path = ((version.Length > 0) ? VersionSettings.GameDir(instance, version) : instance.path);
		string text = "";
		string text2 = "";
		string text3 = "";
		string text4 = "";
		string text5 = "";
		string text6 = "";
		string s = "";
		foreach (string item in Crash.CollectFiles(path, null, loose: true))
		{
			string text7 = Crash.Classify(item);
			string text8 = Crash.ReadText(item, 120000L);
			string text9 = Py.Lower(new PPath(item).Name);
			if (text7 == "mc" && text9.StartsWith("latest", StringComparison.Ordinal) && text.Length == 0)
			{
				text = text8;
				text4 = item;
			}
			else if (text7 == "crash" && text2.Length == 0)
			{
				text2 = text8;
				text5 = item;
			}
			else if (text7 == "hs" && text3.Length == 0)
			{
				text3 = text8;
				text6 = item;
			}
			else if (text9 == "debug.log")
			{
				s = text8;
			}
		}
		if (text.Length == 0)
		{
			string text10 = PPath.Join(path, "logs", "latest.log");
			if (LocalFs.IsFile(text10))
			{
				text = Crash.ReadText(text10, 120000L);
				text4 = text10;
			}
		}
		return PyDict.Of("latest_path", text4, "crash_path", text5, "hs_path", text6, "latest", text, "crash", text2, "hs_err", text3, "debug_tail", Py.Slice(s, -20000));
	}

	public static string LogExcerpt(Instance instance, string kind = "latest", int maxChars = 6000, string version = "")
	{
		PyDict pyDict = CollectLogs(instance, version);
		string key = ((kind == "crash") ? "crash" : ((kind == "hs") ? "hs_err" : "latest"));
		string key2 = ((kind == "crash") ? "crash_path" : ((kind == "hs") ? "hs_path" : "latest_path"));
		string text = Py.S(pyDict.Get(key));
		string text2 = Py.S(pyDict.Get(key2));
		if (text.Length == 0)
		{
			return "没有 " + kind + " 日志";
		}
		return "文件: " + text2 + "\n\n" + Py.Slice(text, -maxChars);
	}

	/// <summary>
	/// 会话级可观测性汇总(AI5-O):轮数 / 工具调用数与失败率 / 压缩次数 / tool_schema_miss / 输入输出 token。
	/// 数从会话事件与轨迹现算(见 AiSessionMetrics),这里只负责嵌进诊断导出;
	/// sessionId 空 = 用当前轨迹作用域,再空就 "active"。UI 面板等用户拍板后再做。
	/// </summary>
	public static PyDict SessionMetrics(string sessionId = null)
	{
		try
		{
			return AiSessionMetrics.Build(sessionId).ToPy();
		}
		catch (Exception ex)
		{
			// 诊断导出绝不能因为统计失败而失败:如实报出来,别把整次导出拖垮
			return PyDict.Of("ok", false, "error", Py.ExcStr(ex), "caveats", new List<object> { "会话统计没跑完:" + Py.ExcStr(ex) });
		}
	}

	/// <summary>会话级汇总拼成几行文本(诊断导出落盘时直接写进文件)。</summary>
	public static string SessionMetricsText(string sessionId = null)
	{
		try
		{
			AiSessionMetrics.Result result = AiSessionMetrics.Build(sessionId);
			List<string> lines = result.ToLines();
			foreach (string caveat in result.Caveats)
			{
				lines.Add("- ！ " + caveat);
			}
			return string.Join("\n", lines);
		}
		catch (Exception ex)
		{
			return "会话统计没跑完:" + Py.ExcStr(ex);
		}
	}
}
