using System;
using System.Collections.Generic;

namespace TipCL.Pages.Ai;

/// <summary>
/// C8a:AI 那一族的**纯文本小工具**(= Windows <c>Pages\Ai\AiUiText.cs</c>,198 行)—— 状态前缀剥离、
/// 「标签 + 参数」拆分、MCP 工具判定、风险等级推断、diff 行分类、token 数格式化、上下文占比、
/// 步骤摘要、截断、计划进度。
///
/// 本文件**零 UI 依赖**(Windows 那份的 using 也只有 <c>System</c>/<c>Collections.Generic</c>)⇒ 逐字照搬。
///
/// 与 Windows 的差异(逐条):
/// <list type="number">
/// <item>取值里的**全角字符一字不改**:`SplitLabel` 的两种冒号是 <c>'：'(U+FF1A) + ':'(U+003A)</c>,
///   与 Windows 逐码点相同(本机按字符码点核过);</item>
/// <item><b>文案不走 <c>I18n.L</c></b>:Windows 这几处("删除/写入/联网/只读"、"正在执行"、"执行了 {0} 步"…)也是
///   直接写中文字面量 —— 照抄,不擅自加一层语言包装(加了会变成"和原版不一样的行为")。</item>
/// </list>
/// </summary>
internal static class AiUiText
{
	/// <summary>【Windows 源】<c>:8</c> 十个状态前缀(前五个是半角冒号那套、后五个是全角冒号那套)。</summary>
	private static readonly string[] StatePrefixes = new string[10] { "准备:", "执行中:", "完成:", "已跳过:", "失败:", "准备：", "执行中：", "完成：", "已跳过：", "失败：" };

	public static string StripState(string? text)
	{
		string s = (text ?? "").Trim();
		foreach (string prefix in StatePrefixes)
		{
			if (s.StartsWith(prefix, StringComparison.Ordinal))
			{
				return s.Substring(prefix.Length).Trim();
			}
		}
		return s;
	}

	/// <summary>「标签: 参数」拆开(冒号两种都认;http 开头的行不拆)。</summary>
	public static (string Label, string Arg) SplitLabel(string? text)
	{
		string s = StripState(text);
		int at = s.IndexOfAny(new char[2] { '：', ':' });
		if (at > 0 && at <= 16 && at < s.Length - 1)
		{
			string label = s.Substring(0, at).Trim();
			string arg = s.Substring(at + 1).Trim();
			if (label.Length > 0 && arg.Length > 0 && !label.Contains("http"))
			{
				return (Label: label, Arg: arg);
			}
		}
		return (Label: s, Arg: "");
	}

	/// <summary>MCP 工具(<c>mcp__</c> / <c>mcp.</c> / <c>mcp_</c> 三种前缀)。</summary>
	public static bool IsMcp(string? tool)
	{
		string t = tool ?? "";
		if (!t.StartsWith("mcp__", StringComparison.Ordinal) && !t.StartsWith("mcp.", StringComparison.Ordinal))
		{
			return t.StartsWith("mcp_", StringComparison.Ordinal);
		}
		return true;
	}

	/// <summary>风险等级:先看后端给的 <c>sideEffect</c>,再看工具名前缀(判据顺序照抄 Windows)。</summary>
	public static AiRisk RiskOf(string? tool, string? sideEffect)
	{
		string se = sideEffect ?? "";
		if (se == "delete")
		{
			return AiRisk.Delete;
		}
		if (se.StartsWith("write", StringComparison.Ordinal))
		{
			return AiRisk.Write;
		}
		if (se == "network")
		{
			return AiRisk.Network;
		}
		string t = tool ?? "";
		if (t.StartsWith("delete_", StringComparison.Ordinal) || t.StartsWith("remove_", StringComparison.Ordinal))
		{
			return AiRisk.Delete;
		}
		if (t.StartsWith("install_", StringComparison.Ordinal) || t.StartsWith("write_", StringComparison.Ordinal) ||
			t.StartsWith("create_", StringComparison.Ordinal) || t.StartsWith("enable_", StringComparison.Ordinal) ||
			t.StartsWith("disable_", StringComparison.Ordinal) || t == "launch_game" || t == "download_java")
		{
			return AiRisk.Write;
		}
		if (IsMcp(t))
		{
			return AiRisk.Write;
		}
		if (se.Length == 0 || se == "none" || se == "read")
		{
			return AiRisk.Read;
		}
		return AiRisk.Write;
	}

	public static string RiskText(AiRisk r)
	{
		return r switch
		{
			AiRisk.Delete => "删除",
			AiRisk.Write => "写入",
			AiRisk.Network => "联网",
			_ => "只读",
		};
	}

	/// <summary>diff 行的分类:<c>@</c> 头 / <c>-</c> 删 / <c>+</c> 增 / 空格。</summary>
	public static char DiffKind(string? line)
	{
		string s = line ?? "";
		if (s.StartsWith("@@", StringComparison.Ordinal) || s.StartsWith("---", StringComparison.Ordinal) || s.StartsWith("+++", StringComparison.Ordinal))
		{
			return '@';
		}
		if (s.StartsWith("-", StringComparison.Ordinal))
		{
			return '-';
		}
		if (s.StartsWith("+", StringComparison.Ordinal))
		{
			return '+';
		}
		return ' ';
	}

	public static bool LooksLikeDiff(IList<string> lines)
	{
		int n = 0;
		foreach (string line in lines)
		{
			char c = DiffKind(line);
			if (c == '-' || c == '+')
			{
				n++;
			}
		}
		return n > 0;
	}

	/// <summary>token 数的短写法(1.2k / 3.45M)。</summary>
	public static string Tokens(long n)
	{
		if (n < 1000)
		{
			return n.ToString();
		}
		if (n < 1000000)
		{
			return ((double)n / 1000.0).ToString("0.#") + "k";
		}
		return ((double)n / 1000000.0).ToString("0.##") + "M";
	}

	/// <summary>上下文占比(0~1;窗口或输入非法时 0)。</summary>
	public static double ContextRatio(long input, long requests, long window)
	{
		if (window <= 0 || input <= 0)
		{
			return 0.0;
		}
		double ratio = (double)((requests > 0) ? (input / requests) : input) / (double)window;
		if (ratio < 0.0)
		{
			return 0.0;
		}
		if (!(ratio > 1.0))
		{
			return ratio;
		}
		return 1.0;
	}

	/// <summary>「正在执行 / 正在 X / 执行了 N 步 · 用时 1.2s」。</summary>
	public static string StepSummary(int count, double seconds, bool running, string current)
	{
		if (running)
		{
			if (current.Length <= 0)
			{
				return "正在执行";
			}
			return "正在" + Trim(current, 24);
		}
		string time = (seconds >= 0.1)
			? (" · 用时 " + ((seconds < 60.0) ? (seconds.ToString("0.0") + "s") : ((int)(seconds / 60.0) + "m" + (int)(seconds % 60.0) + "s")))
			: "";
		return $"执行了 {count} 步{time}";
	}

	public static string Trim(string? s, int max)
	{
		if (s == null)
		{
			return "";
		}
		if (s.Length <= max)
		{
			return s;
		}
		return s.Substring(0, Math.Max(1, max - 1)) + "...";
	}

	/// <summary>计划进度:状态为 <c>completed</c> / <c>done</c> 的算完成。</summary>
	public static (int Done, int Total) PlanProgress(IList<(string Title, string Status)> items)
	{
		int done = 0;
		foreach ((string Title, string Status) item in items)
		{
			if (item.Status == "completed" || item.Status == "done")
			{
				done++;
			}
		}
		return (Done: done, Total: items.Count);
	}
}
