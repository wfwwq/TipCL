// 来源: Windows 版参考仓库 src/TipCL/Pages/Ai/AiUiText.cs(M4-H P5 搬到 Core/Ai:
// Linux 侧页面与工具执行器都要用它,放 Core 才不会让 Core 反向依赖 App)。
// 纯字符串逻辑,行为与 Windows 版逐条对齐。
using System;
using System.Collections.Generic;
using System.Linq;

namespace TipCL.Core.Launcher.Ai;

/// <summary>AI 界面用到的文本小工具(工具行文案、风险文案、token 数显示、步骤摘要)。</summary>
public static class AiUiText
{
    /// <summary>
    /// 状态前缀:半角「:」与全角「\uFF1A」两种都认(模型与工具结果里两种都会出现)。
    /// 全角冒号一律用转义写,避免同形异码看不出来 —— 之前两组里混着不同冒号,
    /// 「完成\uFF1A」这种前缀剥不掉,标签就会整串留在左侧。
    /// </summary>
    private static readonly string[] StatePrefixes =
    {
        "准备:", "执行中:", "完成:", "已跳过:", "失败:",
        "准备\uFF1A", "执行中\uFF1A", "完成\uFF1A", "已跳过\uFF1A", "失败\uFF1A",
    };

    /// <summary>去掉「执行中:」这类状态前缀。</summary>
    public static string StripState(string? text)
    {
        string body = (text ?? "").Trim();
        foreach (string prefix in StatePrefixes)
        {
            if (body.StartsWith(prefix, StringComparison.Ordinal))
            {
                return body.Substring(prefix.Length).Trim();
            }
        }
        return body;
    }

    /// <summary>把「标签: 参数」拆开(标签不超过 16 字、不像 URL 才算)。</summary>
    public static (string Label, string Arg) SplitLabel(string? text)
    {
        // 状态前缀先剥掉,所以剩下的第一个冒号才是「标签与参数」之间的分隔符
        string body = StripState(text);
        int cut = body.IndexOfAny(new[] { ':', '\uFF1A' });
        if (cut > 0 && cut <= 16 && cut < body.Length - 1)
        {
            string head = body.Substring(0, cut).Trim();
            string arg = body.Substring(cut + 1).Trim();
            if (head.Length > 0 && arg.Length > 0 && !head.Contains("http"))
            {
                return (head, arg);
            }
        }
        // 没有「标签: 参数」结构(或前缀已经剥掉):整串都是标签
        return (body, "");
    }

    /// <summary>是不是插件(MCP)提供的工具。</summary>
    public static bool IsMcp(string? tool)
    {
        string name = tool ?? "";
        if (name.StartsWith("mcp__", StringComparison.Ordinal) || name.StartsWith("mcp.", StringComparison.Ordinal))
        {
            return true;
        }
        return name.StartsWith("mcp_", StringComparison.Ordinal);
    }

    /// <summary>风险文案(照 Windows 版 AiUiText.RiskText)。</summary>
    public static string RiskText(AiRisk risk) => risk switch
    {
        AiRisk.Delete => "删除",
        AiRisk.Write => "写入",
        AiRisk.Network => "联网",
        _ => "只读",
    };

    /// <summary>按工具与副作用判风险等级(照 Windows 版 AiUiText.RiskOf)。</summary>
    public static AiRisk RiskOf(string? tool, string? sideEffect)
    {
        string side = sideEffect ?? "";
        if (side == "delete")
        {
            return AiRisk.Delete;
        }
        if (side.StartsWith("write", StringComparison.Ordinal))
        {
            return AiRisk.Write;
        }
        if (side is "network")
        {
            return AiRisk.Network;
        }
        string name = tool ?? "";
        if (name.StartsWith("delete_", StringComparison.Ordinal) || name.StartsWith("remove_", StringComparison.Ordinal))
        {
            return AiRisk.Delete;
        }
        if (name.StartsWith("install_", StringComparison.Ordinal)
            || name.StartsWith("write_", StringComparison.Ordinal)
            || name.StartsWith("create_", StringComparison.Ordinal)
            || name.StartsWith("enable_", StringComparison.Ordinal)
            || name.StartsWith("disable_", StringComparison.Ordinal)
            || name is "launch_game" or "download_java")
        {
            return AiRisk.Write;
        }
        if (IsMcp(name))
        {
            return AiRisk.Write;
        }
        if (side.Length == 0 || side is "none" or "read")
        {
            return AiRisk.Read;
        }
        return AiRisk.Write;
    }

    /// <summary>一行 diff 的类型(@ / - / + / 空格)。</summary>
    public static char DiffKind(string? line)
    {
        string body = line ?? "";
        if (body.StartsWith("@@", StringComparison.Ordinal) || body.StartsWith("---", StringComparison.Ordinal) || body.StartsWith("+++", StringComparison.Ordinal))
        {
            return '@';
        }
        if (body.StartsWith("-", StringComparison.Ordinal))
        {
            return '-';
        }
        if (body.StartsWith("+", StringComparison.Ordinal))
        {
            return '+';
        }
        return ' ';
    }

    public static bool LooksLikeDiff(IList<string> lines) => lines.Any(l => DiffKind(l) is '-' or '+');

    /// <summary>token 数的短显示(1500 → 1.5k)。</summary>
    public static string Tokens(long n)
    {
        if (n < 1000)
        {
            return n.ToString();
        }
        if (n < 1_000_000)
        {
            return ((double)n / 1000.0).ToString("0.#") + "k";
        }
        return ((double)n / 1_000_000.0).ToString("0.##") + "M";
    }

    /// <summary>上下文占用比(0..1)。</summary>
    public static double ContextRatio(long input, long requests, long window)
    {
        if (window <= 0 || input <= 0)
        {
            return 0;
        }
        double ratio = (requests > 0 ? (double)input / requests : input) / window;
        return Math.Clamp(ratio, 0, 1);
    }

    /// <summary>步骤组标题(照 Windows 版 StepSummary)。</summary>
    public static string StepSummary(int count, double seconds, bool running, string current)
    {
        if (running)
        {
            return current.Length <= 0 ? "正在执行" : "正在" + Trim(current, 24);
        }
        string elapsed = seconds >= 0.1
            ? " · 用时 " + (seconds < 60 ? seconds.ToString("0.0") + "s" : (int)(seconds / 60) + "m" + (int)(seconds % 60) + "s")
            : "";
        return $"执行了 {count} 步{elapsed}";
    }

    public static string Trim(string? s, int max)
    {
        if (s == null)
        {
            return "";
        }
        return s.Length <= max ? s : s.Substring(0, Math.Max(1, max - 1)) + "…";
    }

    /// <summary>计划进度(完成的项数 / 总数)。</summary>
    public static (int Done, int Total) PlanProgress(IList<(string Title, string Status)> items)
    {
        int done = items.Count(i => i.Status is "completed" or "done");
        return (done, items.Count);
    }
}
