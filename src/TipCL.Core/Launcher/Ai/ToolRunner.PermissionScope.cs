// AI5-B3:确认卡上「本对话都允许」选的**作用域**要落盘(原先被丢掉:「仅当前实例 / 所有实例」
// 只在进程内的 _alwaysAllowed 里记一个工具名,重启就失效)。
//
// 落点与权限面板添加规则写的是**同一张表**:config.json 的 ai_permission_rules,
// 判定时由 PermissionRules 读(含 instance 限定 ⇒「仅当前实例」换实例后不生效),
// 所以面板里能看到、能删,重启后按作用域各自生效。
//
// 两条守卫(红线「删除类不给无限定 allow 的既有规则必须保留」):
//   1 CanRememberAlways 的判据与确认卡那个按钮的可见性一致(删除类、fs_write 这类高危写工具不给);
//   2 就算界面传回 always=true,删除类也不会被写进规则表。
using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

public sealed partial class ToolRunner
{
    /// <summary>作用域:所有实例(规则不带 instance 限定)。</summary>
    public const string ScopeGlobal = "global";

    /// <summary>作用域:仅当前实例(规则的 instance 写成当前实例名)。</summary>
    public const string ScopeInstance = "instance";

    /// <summary>
    /// 确认卡「本对话都允许」的作用域回传(与 <see cref="Confirm"/> 配对):宿主在**回答之前**
    /// 把卡片上选中的作用域写进这里,Core 在确认返回之后读它(见 RunAsync)。
    /// 取值 <see cref="ScopeGlobal"/> / <see cref="ScopeInstance"/>;空串或不接这个回调 = 不落盘
    /// (测试与外部 MCP 宿主的口径不变:仍然只在进程内记「以后都允许」)。
    /// </summary>
    public Func<string>? AlwaysScope { get; set; }

    /// <summary>作用域 → 规则的 instance 限定值(global = 不限实例;instance = 当前实例名)。</summary>
    public static string RuleInstanceOf(string? scope, string? instance)
        => scope == ScopeGlobal ? "" : (instance ?? "");

    /// <summary>
    /// 这个工具能不能把「以后都允许」持久化成规则。判据与确认卡那个按钮的可见性一致
    /// (ConfirmCard.AllowAlways:删除类与 fs_write / remove_server 这类高危写工具不给按钮)——
    /// 任何界面传回 always=true 都突破不了它,这是「删除类不给无限定 allow」在执行器侧的落地。
    /// </summary>
    public static bool CanRememberAlways(string tool)
    {
        string name = tool ?? "";
        return name.Length > 0 && AllowsAlways(name) && !NeverAlwaysAllow(name)
            && ToolMeta.Get(name)?.side_effect != "delete";
    }

    /// <summary>
    /// 按作用域把一条 allow 规则写进 config.json 的 ai_permission_rules(config.json 是权限面板
    /// 读写的那一份配置,所以规则会出现在面板的规则列表里,也能在面板里删掉)。
    /// 返回是否真的写了一条:作用域不是 global / instance,或被守卫挡下时返回 false(一个字也不写)。
    /// </summary>
    public static bool RememberScope(string tool, string? scope, string? instance)
    {
        if (scope is not (ScopeGlobal or ScopeInstance) || !CanRememberAlways(tool))
        {
            return false;
        }
        var rules = Py.ListOr(Config.CONFIG.Get("ai_permission_rules")).Select(Py.D).ToList();
        string scoped = RuleInstanceOf(scope, instance);
        // 同一个(工具, 作用域)只留一条;限定参数留空 = 整个工具(与权限面板的去重口径一致)
        rules.RemoveAll(r => Py.S(r.Get("tool")) == tool
            && Py.S(r.Get("instance")) == scoped
            && Py.Strip(Py.S(r.Get("content"))).Length == 0);
        rules.Add(PyDict.Of(
            "tool", tool,
            "behavior", "allow",
            "content", "",
            "instance", scoped));
        Config.CONFIG.Set("ai_permission_rules", rules.Cast<object>().ToList());
        Config.CONFIG.Save();
        return true;
    }
}
