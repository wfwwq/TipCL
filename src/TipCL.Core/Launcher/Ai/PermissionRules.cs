// AI-engine-mcp:自定义权限规则按参数匹配(补上 DECISIONS D-24 里「content 记在规则里但暂不参与判定」那一半)。
// 来源: Windows 版参考仓库 windos/src/TipCL/Core/Launcher/Ai/Permission.cs 的
//   RULE_CONTENT_KEYS / RuleContentFromInput / _norm_content / _matches / Decide 里的规则段,语义逐条对位:
//   1. 规则的「限定参数」(Linux 的 config 键是 content,参考版是 ruleContent)留空 = 整个工具都算;
//   2. 非空时,取本次调用参数里 RULE_CONTENT_KEYS **按顺序第一个**非空字符串参数,两边都过
//      Utils.NormPathSpelling(大小写折叠、空白归一、像路径的再做 posix 规范化)后**整串相等**才算命中;
//   3. 命中的规则里 deny 优先,其次 allow,最后 ask(禁止 > 允许 > 每次问);
//   4. 删除类工具(side_effect = delete)上**没有限定参数**的 allow 规则不生效 —— 删除不给「整个工具都允许」,
//      必须写明删哪一个(D-24 删除类不给「本对话都允许」的同一口径)。
// Linux 侧多出的「作用范围」:规则的 instance 键非空时只对**这次调用要作用的实例**生效 —— 取参数里的
// instance,没写就是当前会话实例。参考版按会话实例取 per_instance 规则;Linux 按目标实例,这样「只允许对
// 某实例写」不会被模型在参数里换个实例名绕过去。
using System;
using System.Collections.Generic;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>自定义权限规则(config.json 的 ai_permission_rules)的匹配逻辑。</summary>
public static class PermissionRules
{
    /// <summary>规则限定参数取值的候选键(顺序即优先级,与参考版 RULE_CONTENT_KEYS 逐个相同)。</summary>
    public static readonly string[] RULE_CONTENT_KEYS =
    {
        "command", "url", "file_path", "path", "pattern", "filename", "slug", "name", "version", "major",
    };

    /// <summary>从调用参数里取出规则比对用的那一个值(照参考版 RuleContentFromInput);没有返回 null。</summary>
    public static string? RuleContentFromInput(PyDict? args)
    {
        if (args == null)
        {
            return null;
        }
        foreach (string key in RULE_CONTENT_KEYS)
        {
            if (args.Get(key) is string s && Py.Strip(s).Length > 0)
            {
                return Py.Strip(s);
            }
        }
        return null;
    }

    /// <summary>两边统一的拼写归一(照参考版 _norm_content = Utils.NormPathSpelling)。</summary>
    public static string NormContent(object? text) => Utils.NormPathSpelling(text);

    /// <summary>
    /// 一条规则是否命中这次调用(工具名 + 作用实例 + 限定参数)。
    /// <paramref name="targetInstance"/> 是这次调用要作用的实例(参数里的 instance,缺省为会话实例)。
    /// </summary>
    public static bool Matches(PyDict rule, string tool, PyDict? args, string targetInstance)
    {
        if (rule == null || ToolOf(rule) != tool)
        {
            return false;
        }
        string scope = Py.Strip(Py.S(rule.Get("instance")));
        if (scope.Length > 0 && !string.Equals(scope, targetInstance ?? "", StringComparison.Ordinal))
        {
            return false;
        }
        string content = ContentOf(rule);
        if (content.Length == 0)
        {
            return true;
        }
        return NormContent(content) == NormContent(RuleContentFromInput(args));
    }

    /// <summary>
    /// 在规则表里为这次调用挑出生效的行为(allow / deny / ask);没有生效的规则返回空串。
    /// <paramref name="sideEffect"/> 用来执行「删除类的无限定 allow 不生效」那一条。
    /// </summary>
    public static string Pick(IEnumerable<object?> rules, string tool, PyDict? args, string targetInstance, string sideEffect)
    {
        bool deny = false;
        bool allow = false;
        bool ask = false;
        foreach (object? raw in rules ?? Array.Empty<object?>())
        {
            PyDict rule = Py.D(raw);
            string behavior = Py.S(rule.Get("behavior"));
            if (behavior is not ("allow" or "deny" or "ask"))
            {
                continue;
            }
            if (!Matches(rule, tool, args, targetInstance))
            {
                continue;
            }
            bool scoped = ContentOf(rule).Length > 0;
            switch (behavior)
            {
                case "deny":
                    deny = true;
                    break;
                case "allow":
                    if (scoped || sideEffect != "delete")
                    {
                        allow = true;
                    }
                    break;
                default:
                    ask = true;
                    break;
            }
        }
        return deny ? "deny" : allow ? "allow" : ask ? "ask" : "";
    }

    /// <summary>规则的工具名:Linux 面板写 tool;从参考版迁来的条目是 toolName,一并认。</summary>
    public static string ToolOf(PyDict rule) => Py.S(Py.Or(rule.Get("tool"), rule.Get("toolName")));

    /// <summary>规则的限定参数:Linux 面板写 content;参考版条目是 ruleContent,一并认。</summary>
    public static string ContentOf(PyDict rule) => Py.Strip(Py.S(Py.Or(rule.Get("content"), rule.Get("ruleContent"))));

    /// <summary>这次调用要作用的实例:参数里的 instance,没写就是会话实例。</summary>
    public static string TargetInstance(PyDict? args, string sessionInstance)
    {
        string fromArgs = args?.Get("instance") is string s ? Py.Strip(s) : "";
        return fromArgs.Length > 0 ? fromArgs : sessionInstance ?? "";
    }
}
