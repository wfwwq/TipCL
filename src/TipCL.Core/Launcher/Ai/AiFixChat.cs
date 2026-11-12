// M4-H P5:崩溃「交给 AI 修复」的会话体(移植 Windows 版 Pages/AiFixWindow.cs 的 BuildCrashContext +
// 修复会话的语义;UI 宿主在 App/Pages/Ai/AiFixWindow.cs)。
//
// 与 AiPage 同一个 AiAgent/ToolRunner;区别只在于:开场白是崩溃上下文 + 一条「边查边说、能修就动手」的
// 指令,并且工具集里带上读日志/崩溃报告/冲突扫描这几个诊断工具(它们都是只读,不会弹确认)。
using System;
using System.Collections.Generic;
using System.Text;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;

namespace TipCL.Core.Launcher.Ai;

/// <summary>崩溃修复会话:构造开场消息(照 Windows 版 AiFixWindow.BuildCrashContext)。</summary>
public static class AiFixChat
{
    /// <summary>
    /// 把崩溃报告压成给模型看的上下文。<paramref name="iface"/> 是当前接入方式的说明
    /// (内置网关 / 自定义端点),照参考版告诉模型「接口是好的,别拿接口当借口」。
    /// </summary>
    public static string BuildCrashContext(PyDict report, string iface = "")
    {
        report ??= new PyDict();
        var parts = new List<string> { "【启动器注入 · 对用户不可见】本次对话的崩溃上下文:" };

        var facts = new List<string>();
        string instance = Py.S(report.Get("instance"));
        string version = Py.S(report.Get("version"));
        if (instance.Length > 0)
        {
            facts.Add("实例:" + instance);
        }
        if (version.Length > 0)
        {
            facts.Add("版本:" + version);
        }
        object? exitCode = report.Get("exit_code");
        if (Py.Truthy(exitCode) || exitCode is long)
        {
            string hint = Py.S(report.Get("exit_hint"));
            facts.Add("退出码:" + Py.Str(exitCode) + (hint.Length > 0 ? "(" + hint + ")" : ""));
        }
        if (facts.Count > 0)
        {
            parts.Add(string.Join("\n", facts));
        }

        foreach ((string key, string label, int limit) in new[]
                 {
                     ("headline", "现象", 400),
                     ("summary", "摘要", 600),
                     ("detail", "详细信息", 4000),
                 })
        {
            string text = Clip(Py.S(report.Get(key)), limit);
            if (text.Length > 0)
            {
                parts.Add(label + ":\n" + text);
            }
        }

        string directFile = Py.S(report.Get("direct_file"));
        if (directFile.Length > 0)
        {
            parts.Add("完整日志文件:" + directFile);
        }

        parts.Add("日志自查提醒:上面的日志只是截断摘录。你有读取日志和检查实例的工具" +
                  "(get_latest_log、get_crash_report、diagnose_launch、scan_mod_conflicts、inspect_mod、list_mods 等)," +
                  "请主动调用工具读取完整日志核实原因,不要以「没有日志」「无法访问日志」为由拒绝分析。");
        if (iface.Length > 0)
        {
            parts.Add("接口状态:当前对话通过启动器内置的" + iface + "接入,接口正常可用。不要声称「没有配置接口 / 接口不可用」。");
        }
        parts.Add("如果问题能通过改配置、禁用模组等方式修复,请直接动手(写操作会先征求我同意);需要更多上下文就自己读日志文件。" +
                  "边查边说:有阶段性发现或换步骤时,先用一两句话告诉我,再继续调用工具。最后用一句话给我结论和下一步建议。");
        return string.Join("\n\n", parts);

        static string Clip(string s, int n)
        {
            string text = (s ?? "").Trim();
            return text.Length > n ? text.Substring(0, n) + "…(已截断)" : text;
        }
    }

    /// <summary>开场消息(系统消息之后的第二条 user 消息)。</summary>
    public static PyDict OpeningMessage(PyDict report, string iface = "")
        => PyDict.Of("role", "user", "content", BuildCrashContext(report, iface) + "\n\n帮我看看这次启动为什么失败,能修就修。");

    /// <summary>
    /// 修复会话专属的系统提示词:在通用提示词后面追加「只做与这次崩溃有关的修复」的约束。
    /// </summary>
    public static string FixSystemPrompt(PyDict report)
    {
        string instance = Py.S(report?.Get("instance"));
        string version = Py.S(report?.Get("version"));
        var sb = new StringBuilder(AiData.SystemPrompt());
        sb.Append("\n\n# 本次会话的额外约定\n");
        sb.Append("- 这是一次「崩溃修复」会话,目标是让 " + (instance.Length > 0 ? instance : "默认实例")
            + (version.Length > 0 ? " / " + version : "") + " 能正常启动。\n");
        sb.Append("- 只做与这次崩溃有关的操作:读日志、查冲突、禁用可疑模组、改配置、必要时补装缺失依赖。\n");
        sb.Append("- 删文件前先在回答里说清要删什么、为什么;禁用优先于删除。\n");
        sb.Append("- 每次动作后给出一句「现在的情况 + 下一步」,不要攒到最后一起说。\n");
        return sb.ToString();
    }
}
