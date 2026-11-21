// AI-logs-fs:提示文字里点名的工具,必须真的注册了(schema + ToolMeta.ALL + ToolRunner 分发三处都有)。
//
// 为什么要它:Linux 的系统提示(AiData)、崩溃修复上下文(AiFixChat)、locales 的「日志自查提醒」都让模型去调
// get_latest_log / get_crash_report,可三处注册表里一处都没有 —— 「交给 AI 修复」时模型会调一个不存在的工具。
// 原来的自检只看「提示文字里有没有 get_latest_log」,恰好把问题盖住了。
//
// 做法(全部基于注册表动态判断,不写死工具清单 —— 其他包新增工具、新增提示都自动纳入):
//   1. 来源:AiData.SystemPrompt()、AiFixChat 的开场上下文与修复会话系统提示(用一份样例报告现生成)、
//      locales 里「跟 AI 工具有关」的条目(提到了任一已注册工具名,或键里带「工具」二字);
//   2. 从来源里抠出所有 snake_case 标识符(如 get_latest_log)与通配写法(如 install_*);
//      排除:某个工具 schema 的参数名(allow_multiple、offset_line…)、文件名(后面紧跟 .json 之类)、
//      mcp_ 开头的外部工具名(运行时才有);
//   3. 每个点名的工具:Tools.Names() 里要有、ToolMeta.Get 要有、ToolRunner.DispatchesAsync 要认;
//      通配写法至少要匹配到一个已注册工具。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>提示文字里点名的工具 ↔ 注册表 的一致性检查(自检与 xunit 共用)。</summary>
public static class AiToolRefs
{
    /// <summary>snake_case 标识符:前面不是标识符/路径字符,后面不是标识符字符、也不是「.扩展名」。</summary>
    private static readonly Regex TokenRx = new(
        @"(?<![A-Za-z0-9_./\\-])([a-z][a-z0-9]*(?:_[a-z0-9]+)+)(?![A-Za-z0-9_*])(?!\.[A-Za-z])",
        RegexOptions.CultureInvariant);

    /// <summary>通配写法:search_* / install_*。</summary>
    private static readonly Regex WildcardRx = new(
        @"(?<![A-Za-z0-9_./\\-])([a-z][a-z0-9]*(?:_[a-z0-9]+)*)_\*",
        RegexOptions.CultureInvariant);

    /// <summary>一处点名:来源说明、名字(通配时是前缀)、是否通配。</summary>
    public sealed record Mention(string Source, string Name, bool Wildcard);

    /// <summary>全部已注册 schema 的参数名(这些出现在提示里不算工具名)。</summary>
    public static HashSet<string> ParamNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (object schema in Tools.Schemas)
        {
            PyDict function = Py.D(Py.D(schema)?.Get("function")) ?? new PyDict();
            PyDict parameters = Py.D(function.Get("parameters")) ?? new PyDict();
            PyDict properties = Py.D(parameters.Get("properties")) ?? new PyDict();
            foreach (KeyValuePair<string, object?> kv in properties)
            {
                names.Add(kv.Key);
            }
        }
        return names;
    }

    /// <summary>从一段文字里抠出点名的工具(已排除参数名、文件名、mcp_ 外部工具)。</summary>
    public static List<Mention> Scan(string source, string text, ISet<string>? paramNames = null)
    {
        paramNames ??= ParamNames();
        var result = new List<Mention>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in WildcardRx.Matches(text ?? ""))
        {
            string prefix = m.Groups[1].Value;
            if (seen.Add("*" + prefix))
            {
                result.Add(new Mention(source, prefix, true));
            }
        }
        foreach (Match m in TokenRx.Matches(text ?? ""))
        {
            string name = m.Groups[1].Value;
            if (paramNames.Contains(name) || name.StartsWith("mcp_", StringComparison.Ordinal))
            {
                continue;
            }
            if (seen.Add(name))
            {
                result.Add(new Mention(source, name, false));
            }
        }
        return result;
    }

    /// <summary>样例崩溃报告(只用来把 AiFixChat 的两段文字现生成出来)。</summary>
    private static PyDict SampleReport() => PyDict.Of(
        "instance", "demo", "version", "1.20.1", "exit_code", 1L, "exit_hint", "异常退出",
        "headline", "模组冲突", "summary", "示例摘要", "detail", "示例详情", "direct_file", "/tmp/crash.txt");

    /// <summary>
    /// 要检查的全部文字来源:系统提示、崩溃修复上下文 / 系统提示、locales 里跟 AI 工具有关的条目。
    /// locales 只挑「提到了某个已注册工具,或键里带『工具』」的条目 —— 其余条目里的 snake_case
    /// 多是接口名、配置键(如 preflight_launch、base_url),不是给模型的工具。
    /// </summary>
    public static List<(string Source, string Text)> Sources()
    {
        var sources = new List<(string, string)>
        {
            ("系统提示(AiData)", AiData.SystemPrompt()),
            ("崩溃修复上下文(AiFixChat.BuildCrashContext)", AiFixChat.BuildCrashContext(SampleReport(), "内置网关")),
            ("崩溃修复系统提示(AiFixChat.FixSystemPrompt)", AiFixChat.FixSystemPrompt(SampleReport())),
        };
        var registered = new HashSet<string>(Tools.Names(), StringComparer.Ordinal);
        List<string> files;
        try
        {
            files = CoreResources.ListDir("locales");
        }
        catch (Exception)
        {
            files = new List<string>();
        }
        foreach (string file in files.Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.Ordinal))
        {
            PyDict? table;
            try
            {
                table = PyJson.Loads(CoreResources.ReadText("locales/" + file)) as PyDict;
            }
            catch (Exception)
            {
                table = null;
            }
            if (table == null)
            {
                continue;
            }
            foreach (KeyValuePair<string, object?> kv in table)
            {
                string text = kv.Key + "\n" + Py.S(kv.Value);
                bool aboutTools = kv.Key.Contains("工具", StringComparison.Ordinal)
                    || TokenRx.Matches(text).Any(m => registered.Contains(m.Groups[1].Value));
                if (aboutTools)
                {
                    string head = kv.Key.Length > 16 ? kv.Key.Substring(0, 16) + "…" : kv.Key;
                    sources.Add(("locales/" + file + "「" + head + "」", text));
                }
            }
        }
        return sources;
    }

    /// <summary>全部来源里点名的工具(同一来源内去重)。</summary>
    public static List<Mention> AllMentions()
    {
        HashSet<string> paramNames = ParamNames();
        return Sources().SelectMany(s => Scan(s.Source, s.Text, paramNames)).ToList();
    }

    /// <summary>
    /// 逐个核对点名的工具;返回问题清单(空 = 全部注册齐了)。每条形如
    /// 「系统提示(AiData) 提到 get_latest_log:schema 里没有、ToolMeta.ALL 里没有、ToolRunner 分发不认」。
    /// </summary>
    public static async Task<List<string>> ProblemsAsync(IEnumerable<Mention>? mentions = null)
    {
        List<string> names = Tools.Names();
        var schemaSet = new HashSet<string>(names, StringComparer.Ordinal);
        var dispatchCache = new Dictionary<string, bool>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (Mention mention in mentions ?? AllMentions())
        {
            if (mention.Wildcard)
            {
                if (!names.Any(n => n.StartsWith(mention.Name + "_", StringComparison.Ordinal)))
                {
                    problems.Add(mention.Source + " 提到 " + mention.Name + "_*:没有任何已注册工具匹配");
                }
                continue;
            }
            var missing = new List<string>();
            if (!schemaSet.Contains(mention.Name))
            {
                missing.Add("schema 里没有");
            }
            if (ToolMeta.Get(mention.Name) == null)
            {
                missing.Add("ToolMeta.ALL 里没有");
            }
            if (!dispatchCache.TryGetValue(mention.Name, out bool dispatched))
            {
                dispatched = await ToolRunner.DispatchesAsync(mention.Name).ConfigureAwait(false);
                dispatchCache[mention.Name] = dispatched;
            }
            if (!dispatched)
            {
                missing.Add("ToolRunner 分发不认");
            }
            if (missing.Count > 0)
            {
                problems.Add(mention.Source + " 提到 " + mention.Name + ":" + string.Join("、", missing));
            }
        }
        return problems;
    }
}
