// AI5-C3 工具选择回放探针(dev 工具,不进产品构建)。
//
// 它回答一个问题:**这一轮「该有的工具」有没有被 Tools.SelectToolSchemas 下发**。
// 没下发 → 模型要么改用别的办法,要么调一个本轮没声明的工具 → 触发 C2 的 tool_schema_miss 全量重发。
//
// 输出四段:
//   1. 逐条:每轮的下发数、漏选、tools token;
//   2. 汇总:漏选率(每百回合)、最常漏选工具、下发规模分布、C1 前后对比;
//   3. 方案对比(方案 1a / 1a' / 1b / 方案 2,以及「多发一轮」的往返成本、裸写写工具那一支);
//   4. 与记录在案的基线对照;带 --check 时用它当回归闸门(退出码 1 = 不达标)。
//
// 口径与请求集的标注规则见同目录 README.md;历史结论见 progress/AI5-C3.md。
using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;

namespace AiToolSelectionProbe;

/// <summary>一组请求的回放结果。</summary>
internal sealed class SetResult
{
    public string Name = "";
    public int Total;
    public int WithIdeal;
    public int MissRequests;
    public int MissCount;
    public int MissRequestsLegacy;
    public long TokenSum;
    public readonly List<int> ToolCounts = new();
    public readonly Dictionary<string, int> Missed = new();
    public readonly List<string> Rows = new();
    public readonly List<(Req Req, List<string> Picked, List<string> Missed)> Items = new();

    public double MissPer100 => WithIdeal == 0 ? 0 : 100.0 * MissRequests / WithIdeal;
    public double TokenPerTurn => Total == 0 ? 0 : (double)TokenSum / Total;
    public int MedianTools => ToolCounts.Count == 0 ? 0 : ToolCounts[ToolCounts.Count / 2];
}

internal static class Program
{
    /// <summary>1b 补关键词之前(commit d78bd54)的实测,照 progress/AI5-C3.md 抄回来的。</summary>
    private const string PrePatchCommit = "d78bd54";

    /// <summary>1b 之后的实测基线(本提交)。--check 用它当闸门:漏选不许变多,token 不许超 +10%。</summary>
    private const string PostPatchCommit = "（AI5-C3 1b 提交）";

    private static readonly Dictionary<string, (int MissRequests, int WithIdeal, double TokenPerTurn)> Before =
        new()
        {
            ["典型请求集"] = (7, 39, 5150.8),
            ["口语变体集"] = (5, 12, 4967.4),
        };

    private static readonly Dictionary<string, (int MissRequests, int WithIdeal, double TokenPerTurn)> After =
        new()
        {
            // AI5-C3 方案 1b 落地后的实测(补关键词:7 组口语说法 + 一个「卡」)
            ["典型请求集"] = (0, 39, 5188.0),
            ["口语变体集"] = (0, 12, 5073.5),
            ["关键词补丁集"] = (0, 10, 4124.5),
        };

    private static void Main(string[] args)
    {
        bool check = args.Any(a => a.Equals("--check", StringComparison.OrdinalIgnoreCase));

        List<string> allNames = Tools.Names();
        long fullTokens = TokenOf(Tools.Schemas);
        long coreTokens = TokensOfNames(AiData.CORE_TOOLS);
        long promptTokens = Tokens.EstimateText(AiData.SystemPrompt(null));
        var nonCore = allNames.Where(n => !AiData.CORE_TOOLS.Contains(n)).ToList();
        long dir40 = nonCore.Sum(n => Tokens.EstimateText("- " + n + ": " + FirstLine(SchemaOf(n), 40)));
        long dir30 = nonCore.Sum(n => Tokens.EstimateText("- " + n + ": " + FirstLine(SchemaOf(n), 30)));
        long findLoad = Tokens.EstimateText(FindToolsSchema) + Tokens.EstimateText(LoadToolsSchema);

        Console.WriteLine("工具总数 " + allNames.Count + " ;全量 tools token(估算) " + fullTokens +
                          " ;CORE_TOOLS " + AiData.CORE_TOOLS.Length + " 个 = " + coreTokens + " token");
        Console.WriteLine("系统提示 " + promptTokens + " token ;一行目录(非 CORE " + nonCore.Count + " 条 / 40 字)= " + dir40 +
                          " ;(30 字)= " + dir30 + " ;find_tools+load_tools = " + findLoad);
        Console.WriteLine("估算口径:Tokens.EstimateText(CJK 1 / 其余 0.25);报的是 tools 部分,不含系统提示与历史");

        // 标注自检:ideal 里写了不存在的工具名时直接报出来(否则统计会静默偏低)
        foreach (Req r in AllRequests())
        {
            foreach (string t in r.Ideal)
            {
                if (!allNames.Contains(t))
                {
                    Console.WriteLine("!! " + r.Id + " 的标注里有不存在的工具: " + t);
                }
            }
        }

        var typical = Analyze("典型请求集", RequestSets.Typical, true);
        var variants = Analyze("口语变体集", RequestSets.Variants, true);
        var patch = Analyze("关键词补丁集", RequestSets.KeywordPatch, true);
        var holdOut = Analyze("对抗性留出集", RequestSets.HoldOut, true);

        Console.WriteLine();
        Console.WriteLine("=== 基线对照(1b 补关键词前后) ===");
        Console.WriteLine("集\t有标注\t1b 前漏选(" + PrePatchCommit + ")\t每百回合\t1b 后漏选\t每百回合\t1b 前 tools/轮\t1b 后 tools/轮\t变化");
        foreach (SetResult s in new[] { typical, variants, patch })
        {
            string before = Before.TryGetValue(s.Name, out var b) ? b.MissRequests + "/" + b.WithIdeal : "—";
            string beforeRate = Before.TryGetValue(s.Name, out var b2) ? (100.0 * b2.MissRequests / b2.WithIdeal).ToString("0.0") : "—";
            string beforeToken = Before.TryGetValue(s.Name, out var b3) ? b3.TokenPerTurn.ToString("0.0") : "—";
            string delta = Before.TryGetValue(s.Name, out var b4) && b4.TokenPerTurn > 0
                ? (100.0 * (s.TokenPerTurn - b4.TokenPerTurn) / b4.TokenPerTurn).ToString("+0.0;-0.0") + "%"
                : "—";
            Console.WriteLine(string.Join("\t", s.Name, s.WithIdeal, before, beforeRate,
                s.MissRequests + "/" + s.WithIdeal, s.MissPer100.ToString("0.0"), beforeToken,
                s.TokenPerTurn.ToString("0.0"), delta));
        }
        Console.WriteLine("合并(典型 + 口语变体)\t" + (typical.WithIdeal + variants.WithIdeal) + "\t12/51\t23.5\t" +
                          (typical.MissRequests + variants.MissRequests) + "/" + (typical.WithIdeal + variants.WithIdeal) + "\t" +
                          (100.0 * (typical.MissRequests + variants.MissRequests) / (typical.WithIdeal + variants.WithIdeal)).ToString("0.0") +
                          "\t—\t—\t—");
        Console.WriteLine("对抗性留出集(不参与补词,故意用表外说法):" + holdOut.MissRequests + "/" + holdOut.WithIdeal +
                          " → 每百回合 " + holdOut.MissPer100.ToString("0.0") + "(这就是关键词路线的长尾)");

        PrintSchemes(typical, variants, allNames, fullTokens, coreTokens, findLoad, promptTokens, dir40, dir30);

        if (check)
        {
            Environment.ExitCode = Check(new[] { typical, variants, patch }) ? 0 : 1;
        }
    }

    private static IEnumerable<Req> AllRequests()
        => RequestSets.Typical.Concat(RequestSets.Variants).Concat(RequestSets.KeywordPatch).Concat(RequestSets.HoldOut);

    private static SetResult Analyze(string name, Req[] set, bool detail)
    {
        var result = new SetResult { Name = name, Total = set.Length };
        foreach (Req r in set)
        {
            List<object> messages = History(r);
            List<string> picked = Names(Tools.SelectToolSchemas(messages));
            // 老口径(只看最后一条用户消息)的对照:userText 覆盖参数正是那条分支
            List<string> legacy = Names(Tools.SelectToolSchemas(messages, r.Text));
            List<string> missed = r.Ideal.Where(t => !picked.Contains(t)).ToList();
            if (r.Ideal.Length > 0)
            {
                result.WithIdeal++;
                if (missed.Count > 0)
                {
                    result.MissRequests++;
                }
                if (r.Ideal.Any(t => !legacy.Contains(t)))
                {
                    result.MissRequestsLegacy++;
                }
            }
            result.MissCount += missed.Count;
            foreach (string m in missed)
            {
                result.Missed[m] = result.Missed.TryGetValue(m, out int c) ? c + 1 : 1;
            }
            long tok = TokensOfNames(picked);
            result.TokenSum += tok;
            result.ToolCounts.Add(picked.Count);
            result.Items.Add((r, picked, missed));
            result.Rows.Add(string.Join("\t", r.Id, r.Topic, r.Ideal.Length, string.Join(",", missed),
                string.Join(",", r.Ideal.Where(t => !legacy.Contains(t))), picked.Count, legacy.Count, tok));
        }
        result.ToolCounts.Sort();

        Console.WriteLine();
        Console.WriteLine("###### " + name + "(" + set.Length + " 条,有标注 " + result.WithIdeal + " 条) ######");
        if (detail)
        {
            Console.WriteLine("id\t场景\tideal数\t漏选(现状)\t漏选(只看最后一条用户消息)\t下发数(现状)\t下发数(老口径)\ttools(现状)");
            foreach (string row in result.Rows)
            {
                Console.WriteLine(row);
            }
        }
        Console.WriteLine("漏选请求 " + result.MissRequests + "/" + result.WithIdeal + " → 每百回合 " + result.MissPer100.ToString("0.0") +
                          " ;漏选工具 " + result.MissCount + " 个 ;老口径(只看最后一条用户消息)漏选请求 " + result.MissRequestsLegacy + "/" + result.WithIdeal);
        if (result.Missed.Count > 0)
        {
            Console.WriteLine("最常漏选:" + string.Join(", ", result.Missed.OrderByDescending(k => k.Value).ThenBy(k => k.Key)
                .Select(k => k.Key + "×" + k.Value + "(" + TokensOfNames(new[] { k.Key }) + " token)")));
        }
        Console.WriteLine("下发工具数 最小 " + result.ToolCounts[0] + " / 中位 " + result.MedianTools + " / 最大 " +
                          result.ToolCounts[result.ToolCounts.Count - 1] + " ;≥50 个的请求 " +
                          result.ToolCounts.Count(c => c >= 50) + "/" + set.Length +
                          " ;tools " + result.TokenPerTurn.ToString("0.0") + " token/轮");
        return result;
    }

    /// <summary>方案对比(典型集与口语变体集,口径与 progress/AI5-C3.md §4 一致)。</summary>
    private static void PrintSchemes(SetResult typical, SetResult variants, List<string> allNames, long fullTokens,
        long coreTokens, long findLoad, long promptTokens, long dir40, long dir30)
    {
        Console.WriteLine();
        Console.WriteLine("=== 方案对比(tools 部分 / 单轮) ===");
        Console.WriteLine("方案\t典型集\t相对现状\t口语变体集\t相对现状");
        var a = ComputeScheme(typical, allNames, fullTokens, coreTokens, findLoad, promptTokens, dir40, dir30);
        var b = ComputeScheme(variants, allNames, fullTokens, coreTokens, findLoad, promptTokens, dir40, dir30);
        Row("现状(本提交)", typical.TokenPerTurn, variants.TokenPerTurn, "—", "—");
        if (typical.Missed.Count == 0 && variants.Missed.Count == 0)
        {
            // 1b 落地后这两集已经没有漏选,方案 1a / 1a' / 1b 与现状同值(没有东西可补)。
            // 方案 1 与方案 2 的取舍数据是「补词前」量出来的,见 progress/AI5-C3.md §4.2(基线 commit d78bd54)。
            Row("方案1a / 1a' / 1b", typical.TokenPerTurn, variants.TokenPerTurn, "+0.0%(已无漏选可补)", "+0.0%");
        }
        else
        {
            Row("方案1a(漏选过的工具并入 CORE)", a.S1a, b.S1a, Pct(a.S1a, a.Now), Pct(b.S1a, b.Now));
            Row("方案1a'(只并入 Top3: " + string.Join("/", a.Top3) + ")", a.S1aTop3, b.S1aTop3, Pct(a.S1aTop3, a.Now), Pct(b.S1aTop3, b.Now));
            Row("方案1b(把剩下的漏选都补上 = 关键词路线上限)", a.S1b, b.S1b, Pct(a.S1b, a.Now), Pct(b.S1b, b.Now));
        }
        Row("方案2(非 CORE 一行目录 40 字 + find/load)", a.S2, b.S2, Pct(a.S2, a.Now), Pct(b.S2, b.Now));
        Row("方案2(目录 30 字)", a.S2b, b.S2b, Pct(a.S2b, a.Now), Pct(b.S2b, b.Now));

        Console.WriteLine();
        Console.WriteLine("往返成本与附带发现:");
        Console.WriteLine("  典型集:系统提示+历史 " + a.ContextPerTurn + " token/轮 ;漏选重发(C2 用全量表)" + a.MissRequests +
                          " 次 × " + a.MissRetryPerTurn + " tools + 上下文 → 每百回合 " + a.MissCostPer100 + " token ;" +
                          "方案2 的 load 往返 " + a.LoadTurns + "/" + typical.Total + " 轮 → 每百回合 " + a.LoadCostPer100 + " token");
        Console.WriteLine("  典型集:收掉「裸写工具」那一支后 tools " + a.BareTokenPerTurn.ToString("0.0") + " token/轮 (" +
                          Pct(a.BareTokenPerTurn, typical.TokenPerTurn) + "),受影响请求 " + a.BareTurns + "/" + typical.Total +
                          " 平均多带 " + a.BareExtraPerTurn + " token");
        Console.WriteLine("  口语变体集:漏选重发 → 每百回合 " + b.MissCostPer100 + " token ;方案2 的 load 往返 " + b.LoadTurns + "/" +
                          variants.Total + " 轮 → 每百回合 " + b.LoadCostPer100 + " token ;收掉裸写工具后 " +
                          b.BareTokenPerTurn.ToString("0.0") + " token/轮 (" + Pct(b.BareTokenPerTurn, variants.TokenPerTurn) + ")");

        void Row(string name, double t, double v, string relT, string relV)
            => Console.WriteLine(string.Join("\t", name, t.ToString("0.0"), relT, v.ToString("0.0"), relV));
    }

    private sealed class SchemeNumbers
    {
        public double Now, S1a, S1aTop3, S1b, S2, S2b;
        public List<string> Top3 = new();
        public int MissRequests, LoadTurns, BareTurns;
        public long ContextPerTurn, MissRetryPerTurn, MissCostPer100, LoadCostPer100;
        public double BareTokenPerTurn, BareExtraPerTurn;
    }

    private static SchemeNumbers ComputeScheme(SetResult s, List<string> allNames, long fullTokens, long coreTokens,
        long findLoad, long promptTokens, long dir40, long dir30)
    {
        var n = new SchemeNumbers { Now = s.TokenPerTurn };
        var missedUnion = s.Missed.Keys.ToList();
        n.Top3 = s.Missed.OrderByDescending(k => k.Value).ThenBy(k => k.Key).Take(3).Select(k => k.Key).ToList();
        long s1a = 0, s1aTop3 = 0, s1b = 0, s2 = 0, s2b = 0, missRetry = 0, ctxSum = 0, bareTokens = 0, bareTokenSum = 0;
        foreach ((Req r, List<string> picked, List<string> missed) in s.Items)
        {
            List<object> messages = History(r);
            long tok = TokensOfNames(picked);
            s1a += TokensOfNames(picked.Concat(missedUnion));
            s1aTop3 += TokensOfNames(picked.Concat(n.Top3));
            s1b += tok + TokensOfNames(missed);
            long loaded = TokensOfNames(r.Ideal.Where(t => !AiData.CORE_TOOLS.Contains(t)));
            s2 += coreTokens + dir40 + findLoad + loaded;
            s2b += coreTokens + dir30 + findLoad + loaded;
            ctxSum += promptTokens + Tokens.EstimateMessages(messages);
            if (missed.Count > 0)
            {
                n.MissRequests++;
                missRetry += fullTokens - tok;
            }
            if (r.Ideal.Any(t => !AiData.CORE_TOOLS.Contains(t)))
            {
                n.LoadTurns++;
            }
            // 「裸写工具」= 非只读、不在 CORE、也不是关键词命中的那部分(靠 ACTION_RE 分支带进来的)
            string windowText = (r.Prev.Length > 0 ? r.Prev + "\n" + r.ToolResult + "\n" : "") + r.Text;
            List<string> keywordOnly = Names(Tools.SelectToolSchemas(messages, windowText + "吗?"));
            List<object> keywordOnlySchemas = Tools.SelectToolSchemas(messages, windowText + "吗?");
            long bare = TokensOfNames(picked.Except(keywordOnly).Where(x => !AiData.CORE_TOOLS.Contains(x)).ToList());
            if (bare > 0)
            {
                n.BareTurns++;
                bareTokens += bare;
            }
            bareTokenSum += TokenOf(keywordOnlySchemas);
        }
        n.S1a = s1a / (double)s.Total;
        n.S1aTop3 = s1aTop3 / (double)s.Total;
        n.S1b = s1b / (double)s.Total;
        n.S2 = s2 / (double)s.Total;
        n.S2b = s2b / (double)s.Total;
        n.ContextPerTurn = ctxSum / s.Total;
        n.MissRetryPerTurn = n.MissRequests > 0 ? missRetry / n.MissRequests : 0;
        double missPer100 = 100.0 * s.MissRequests / Math.Max(1, s.WithIdeal);
        n.MissCostPer100 = (long)(missPer100 * (n.MissRetryPerTurn + n.ContextPerTurn));
        n.LoadCostPer100 = (long)(100.0 * n.LoadTurns / s.Total * n.ContextPerTurn);
        n.BareTokenPerTurn = (double)bareTokenSum / s.Total;
        n.BareExtraPerTurn = n.BareTurns > 0 ? bareTokens / n.BareTurns : 0;
        return n;
    }

    private static bool Check(SetResult[] sets)
    {
        bool ok = true;
        Console.WriteLine();
        Console.WriteLine("=== 回归闸门(--check) ===");
        foreach (SetResult s in sets)
        {
            if (!After.TryGetValue(s.Name, out var baseline))
            {
                Console.WriteLine("  - " + s.Name + ":没有记录基线,跳过");
                continue;
            }
            bool missOk = s.MissRequests <= baseline.MissRequests;
            bool tokenOk = baseline.TokenPerTurn <= 0 || s.TokenPerTurn <= baseline.TokenPerTurn * 1.10;
            Console.WriteLine("  " + (missOk && tokenOk ? "PASS" : "FAIL") + " " + s.Name +
                              " :漏选请求 " + s.MissRequests + "(基线 " + baseline.MissRequests + ") ;tools " +
                              s.TokenPerTurn.ToString("0.0") + "(基线 " + baseline.TokenPerTurn.ToString("0.0") + ",上限 ×1.10)");
            ok &= missOk && tokenOk;
        }
        if (sets.Any(s => After.TryGetValue(s.Name, out var b) && b.TokenPerTurn <= 0))
        {
            Console.WriteLine("  (基线里的 token 还是 0 = 还没回填;先在干净树上跑一遍 --check 把 After 常量填上)");
        }
        return ok;
    }

    private static string Pct(double value, double baseline)
        => baseline == 0 ? "—" : (100.0 * (value - baseline) / baseline).ToString("+0.0;-0.0") + "%";

    // ---------------- 以下与生产代码一样的装配 / 计量 ----------------

    private static List<object> History(Req r)
    {
        var messages = new List<object> { PyDict.Of("role", "system", "content", "(system prompt)") };
        if (r.Prev.Length > 0)
        {
            messages.Add(PyDict.Of("role", "user", "content", r.Prev));
            messages.Add(PyDict.Of("role", "assistant", "content", "好的,我看一下。"));
            messages.Add(PyDict.Of("role", "tool", "tool_call_id", "call-1", "name", "search", "content", r.ToolResult));
        }
        messages.Add(PyDict.Of("role", "user", "content", r.Text));
        return messages;
    }

    private static List<string> Names(IEnumerable<object> schemas)
        => schemas.Select(Tools.SchemaName).Where(n => n.Length > 0).ToList();

    private static long TokenOf(IEnumerable<object> schemas)
        => schemas.Sum(s => Tokens.EstimateText(PyJson.Dumps(s)));

    private static object SchemaOf(string name) => Tools.Schemas.First(s => Tools.SchemaName(s) == name);

    private static long TokensOfNames(IEnumerable<string> names)
        => names.Distinct().Sum(n => Tokens.EstimateText(PyJson.Dumps(SchemaOf(n))));

    /// <summary>工具描述的第一句(截到 max 字),用于模拟「一行目录」。</summary>
    private static string FirstLine(object schema, int max)
    {
        PyDict fn = Py.D(Py.D(schema).Get("function"));
        string desc = Py.S(fn.Get("description"));
        int cut = desc.IndexOf('。');
        if (cut > 0)
        {
            desc = desc.Substring(0, cut);
        }
        desc = desc.Replace("\n", " ").Trim();
        return desc.Length <= max ? desc : desc.Substring(0, max);
    }

    // 方案 2 才需要这两个 schema;这里只是量它们的 token 成本,不进生产代码。
    private const string FindToolsSchema =
        "{\"type\":\"function\",\"function\":{\"name\":\"find_tools\",\"description\":\"在工具目录里按关键词找工具。要用的工具不在手上时先用它找到名字,再用 load_tools 取完整用法\",\"parameters\":{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"description\":\"要干什么,如「禁用模组」\"},\"limit\":{\"type\":\"integer\",\"description\":\"最多返回几个,默认 5\"}},\"required\":[\"query\"]}}}";

    private const string LoadToolsSchema =
        "{\"type\":\"function\",\"function\":{\"name\":\"load_tools\",\"description\":\"取指定工具的完整参数说明\",\"parameters\":{\"type\":\"object\",\"properties\":{\"names\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"工具名,可传多个\"}},\"required\":[\"names\"]}}}";
}