using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// A4:`Ai\Permission.cs`、`Ai\AiRule.cs`、`Ai\ToolMetaInfo.cs`、`Ai\PermissionResult.cs`
/// (A4 起参与编译)的用例:不联网、不起进程、不读真实目录(规则存储的隔离守卫在 AiHomeIsolationTests 里)。
///
/// 期望值来自读过的源码 Permission.cs:
///   NormalizePermissionMode 63-97(standard→default;full 看 confirm_writes 给 acceptEdits 或 yolo;
///     custom 原样;合法值原样;其它 → default / yolo;空值同理)、
///   PermissionMode 8-41(10 个合法值,IsValid 34-42 走 HashSet)、
///   Decide 177-254(顺序:plan 拦写 → 规则 deny/allow/ask → 只读放行 → 模式分支)、
///   RuleContentFromInput 290-301(按 RULE_CONTENT_KEYS 50 行的顺序取第一个非空字符串)、
///   CoerceRule 303-317(非 PyDict 或 behavior 不合法 → null)、BehaviorOf 280-288(不合法抛 ValueError)、
///   DedupeRules 99-112 / ApplyUpdates 114-117(按 AiRule.Key 去重,key = tool_name + "\0" + 内容)。
/// </summary>
public class AiPermissionTests
{
    private static ToolMetaInfo Meta(string name, bool ro, string side, string risk)
    {
        return new ToolMetaInfo(name, ro, side, risk, false);
    }

    private static PermissionResult Decide(ToolMetaInfo meta, PyDict args, object mode, params AiRule[] rules)
    {
        return Permission.Decide(meta, args, mode, rules);
    }

    // ---------- NormalizePermissionMode ----------

    [Theory]
    [InlineData(null, true, "default")]
    [InlineData("", true, "default")]
    [InlineData("standard", true, "default")]
    [InlineData("full", true, "acceptEdits")]
    [InlineData("full", false, "yolo")]
    [InlineData("custom", true, "custom")]
    [InlineData("custom", false, "custom")]
    [InlineData("yolo", true, "yolo")]
    [InlineData("plan", true, "plan")]
    [InlineData("bypassPermissions", true, "bypassPermissions")]
    [InlineData("nonsense", true, "default")]
    [InlineData("nonsense", false, "yolo")]
    [InlineData(null, false, "yolo")]
    public void NormalizePermissionMode_maps_legacy_and_invalid_values(string? raw, bool confirmWrites, string expected)
    {
        Assert.Equal(expected, Permission.NormalizePermissionMode(raw, confirmWrites));
    }

    [Fact]
    public void PermissionMode_values_are_exactly_the_ten_supported_ones()
    {
        Assert.Equal(10, Permission.PermissionMode.Values.Length);
        Assert.Equal(
            new[] { "default", "plan", "edit", "acceptEdits", "auto", "dontAsk", "autoEdit", "yolo", "bypassPermissions", "build" },
            Permission.PermissionMode.Values);
        foreach (string v in Permission.PermissionMode.Values)
        {
            Assert.True(Permission.PermissionMode.IsValid(v), v);
        }
        Assert.False(Permission.PermissionMode.IsValid("nope"));
        Assert.False(Permission.PermissionMode.IsValid(null));
    }

    // ---------- Decide:模式分支 ----------

    [Fact]
    public void An_unknown_mode_falls_back_to_the_default_branch()
    {
        // _norm_mode 167-175:不认识的模式一律当 default
        Assert.Equal("ask", Decide(Meta("fs_write", false, "write_local", "medium"), null, "not-a-mode").decision);
        Assert.Equal("写操作需要确认", Decide(Meta("fs_write", false, "write_local", "medium"), null, "not-a-mode").reason);
    }

    [Fact]
    public void Default_mode_asks_and_high_risk_has_its_own_reason()
    {
        Assert.Equal("写操作需要确认", Decide(null, null, "default").reason);
        Assert.Equal("ask", Decide(null, null, "default").decision);
        // meta 为 null 时 side_effect 默认 write_local、risk 默认 medium(180-181 行)
        Assert.Equal("高风险操作需要确认", Decide(Meta("t", false, "write_local", "high"), null, "default").reason);
    }

    [Fact]
    public void Readonly_tools_are_always_allowed_in_non_plan_modes()
    {
        PermissionResult r = Decide(Meta("fs_read", true, "read", "low"), null, "default");

        Assert.Equal("allow", r.decision);
        Assert.Equal("只读操作", r.reason);
    }

    [Fact]
    public void Plan_mode_denies_writes_but_still_allows_readonly_tools()
    {
        PermissionResult denied = Decide(Meta("fs_write", false, "write_local", "medium"), null, "plan");
        Assert.Equal("deny", denied.decision);
        // 源码 Permission.cs 187 行用的是全角逗号 U+FF0C(和 D-16 那批 locales 键一个坑),这里用转义写死
        Assert.Equal("当前是「只看不动」模式\uFF0C不能执行这类操作", denied.reason);

        Assert.Equal("allow", Decide(Meta("fs_read", true, "read", "low"), null, "plan").decision);
    }

    [Theory]
    [InlineData("yolo", "write_local", "allow", "免确认模式")]
    [InlineData("bypassPermissions", "write_local", "allow", "免确认模式")]
    [InlineData("auto", "write_local", "allow", "写操作直接执行")]
    [InlineData("autoEdit", "write_local", "allow", "写操作直接执行")]
    [InlineData("acceptEdits", "write_local", "allow", "写操作直接执行")]
    [InlineData("auto", "delete", "ask", "删除操作仍需确认")]
    [InlineData("acceptEdits", "delete", "ask", "删除操作仍需确认")]
    [InlineData("build", "write_local", "allow", "安装/下载类直接执行")]
    [InlineData("build", "install", "allow", "安装/下载类直接执行")]
    [InlineData("build", "delete", "ask", "删除/启动操作仍需确认")]
    [InlineData("build", "launch", "ask", "删除/启动操作仍需确认")]
    [InlineData("edit", "write_local", "allow", "文件类修改直接执行")]
    [InlineData("edit", "install", "ask", "这类操作需要确认")]
    [InlineData("dontAsk", "write_local", "deny", "用户开启了「不询问」模式")]
    public void Mode_branches_return_the_documented_decision(string mode, string sideEffect, string decision, string reason)
    {
        PermissionResult r = Decide(Meta("t", false, sideEffect, "medium"), null, mode);

        Assert.Equal(decision, r.decision);
        Assert.Equal(reason, r.reason);
    }

    // ---------- Decide:规则 ----------

    [Fact]
    public void A_deny_rule_wins_and_reports_its_rule_id()
    {
        PermissionResult r = Decide(Meta("fs_write", false, "write_local", "medium"), null, "yolo",
            new AiRule("fs_write", null, "deny"));

        Assert.Equal("deny", r.decision);
        Assert.Equal("这条操作被你的规则禁止", r.reason);
        Assert.Equal("rule.fs_write.deny", r.rule_id);
    }

    [Fact]
    public void Deny_is_evaluated_before_allow_so_it_always_wins()
    {
        PermissionResult r = Decide(Meta("fs_write", false, "write_local", "medium"), null, "default",
            new AiRule("fs_write", null, "allow"),
            new AiRule("fs_write", null, "deny"));

        Assert.Equal("deny", r.decision);
        Assert.Equal("rule.fs_write.deny", r.rule_id);
    }

    [Fact]
    public void An_allow_rule_with_content_allows_the_matching_input()
    {
        PyDict args = PyDict.Of("name", "hello");

        PermissionResult r = Decide(Meta("run", false, "write_local", "medium"), args, "default",
            new AiRule("run", "hello", "allow"));

        Assert.Equal("allow", r.decision);
        Assert.Equal("规则允许", r.reason);
        Assert.Equal("rule.run.allow", r.rule_id);
    }

    [Fact]
    public void A_rule_for_another_tool_or_another_content_does_not_apply()
    {
        PyDict args = PyDict.Of("name", "hello");

        Assert.Equal("deny", Decide(Meta("other_tool", false, "write_local", "medium"), args, "dontAsk",
            new AiRule("run", "hello", "allow")).decision);
        Assert.Equal("deny", Decide(Meta("run", false, "write_local", "medium"), args, "dontAsk",
            new AiRule("run", "different", "allow")).decision);
    }

    [Fact]
    public void An_ask_rule_reports_that_it_needs_confirmation()
    {
        PermissionResult r = Decide(Meta("run", false, "write_local", "medium"), null, "yolo",
            new AiRule("run", null, "ask"));

        Assert.Equal("ask", r.decision);
        Assert.Equal("规则要求先询问", r.reason);
        Assert.Equal("rule.run.ask", r.rule_id);
    }

    [Fact]
    public void A_wildcard_allow_rule_never_bypasses_deletes()
    {
        // Decide 201 行:allow 规则没有 rule_content 且 side_effect == "delete" 时被跳过
        PermissionResult r = Decide(Meta("run", false, "delete", "medium"), null, "default",
            new AiRule("run", null, "allow"));

        Assert.Equal("ask", r.decision);
        Assert.Equal("写操作需要确认", r.reason);
        Assert.Equal("", r.rule_id);
    }

    [Fact]
    public void A_wildcard_allow_rule_still_applies_to_non_delete_side_effects()
    {
        PermissionResult r = Decide(Meta("run", false, "write_local", "medium"), null, "default",
            new AiRule("run", null, "allow"));

        Assert.Equal("allow", r.decision);
        Assert.Equal("rule.run.allow", r.rule_id);
    }

    [Fact]
    public void A_wildcard_deny_rule_is_not_skipped_for_deletes()
    {
        PermissionResult r = Decide(Meta("run", false, "delete", "medium"), null, "default",
            new AiRule("run", null, "deny"));

        Assert.Equal("deny", r.decision);
        Assert.Equal("rule.run.deny", r.rule_id);
    }

    // ---------- RuleContentFromInput ----------

    [Fact]
    public void RuleContentFromInput_picks_the_first_key_of_the_documented_order()
    {
        // RULE_CONTENT_KEYS 50:command, url, file_path, path, pattern, filename, slug, name, version, major
        Assert.Equal("ls", Permission.RuleContentFromInput(PyDict.Of("path", "/x", "command", "ls")));
        Assert.Equal("https://a", Permission.RuleContentFromInput(PyDict.Of("file_path", "/x", "url", "https://a")));
        Assert.Equal("/x", Permission.RuleContentFromInput(PyDict.Of("path", "/x")));
    }

    [Fact]
    public void RuleContentFromInput_skips_blanks_and_non_strings()
    {
        Assert.Null(Permission.RuleContentFromInput(PyDict.Of("command", "   ", "path", "")));
        Assert.Null(Permission.RuleContentFromInput(PyDict.Of("path", 42)));
        Assert.Null(Permission.RuleContentFromInput(new PyDict()));
        Assert.Null(Permission.RuleContentFromInput(null));
        // 两端空白会被去掉(297 行 Py.Strip)
        Assert.Equal("x", Permission.RuleContentFromInput(PyDict.Of("command", "  x  ")));
    }

    // ---------- BehaviorOf / CoerceRule / AiRule ----------

    [Theory]
    [InlineData("allow")]
    [InlineData("deny")]
    [InlineData("ask")]
    public void BehaviorOf_accepts_the_three_behaviors(string value)
    {
        Assert.Equal(value, Permission.BehaviorOf(value));
        Assert.Equal(3, Permission.BEHAVIORS.Length);
    }

    [Fact]
    public void BehaviorOf_throws_ValueError_for_anything_else()
    {
        Assert.Throws<ValueError>(() => Permission.BehaviorOf("maybe"));
    }

    [Fact]
    public void CoerceRule_builds_an_AiRule_and_defaults_the_behavior()
    {
        AiRule r = Permission.CoerceRule(PyDict.Of("toolName", "run", "behavior", "deny", "ruleContent", "ls -la"));

        Assert.NotNull(r);
        Assert.Equal("run", r.tool_name);
        Assert.Equal("deny", r.behavior);
        Assert.Equal("ls -la", r.rule_content);

        // 没有 behavior 时默认 allow(311 行 Py.Or(..., "allow"))
        AiRule d = Permission.CoerceRule(PyDict.Of("toolName", "run"));
        Assert.Equal("allow", d.behavior);
        Assert.Null(d.rule_content);
    }

    [Fact]
    public void CoerceRule_returns_null_for_bad_input()
    {
        Assert.Null(Permission.CoerceRule("not a dict"));
        Assert.Null(Permission.CoerceRule(null));
        // behavior 不合法 → BehaviorOf 抛 ValueError → 被 313-316 行吞掉变 null
        Assert.Null(Permission.CoerceRule(PyDict.Of("toolName", "run", "behavior", "maybe")));
    }

    [Fact]
    public void AiRule_Key_is_tool_name_plus_null_plus_content()
    {
        Assert.Equal("run\0ls", new AiRule("run", "ls").Key());
        Assert.Equal("run\0", new AiRule("run").Key());
        // 空字符串内容等同于没有内容(Py.Truthy("") 为假)
        Assert.Equal("run\0", new AiRule("run", "").Key());
        Assert.Equal("instance", new AiRule("run").scope);
    }

    // ---------- DedupeRules / ApplyUpdates ----------

    [Fact]
    public void DedupeRules_keeps_the_first_of_each_key_and_the_original_order()
    {
        List<AiRule> list = Permission.DedupeRules(new object[]
        {
            new AiRule("a", "1", "allow"),
            new AiRule("b", "2", "deny"),
            new AiRule("a", "1", "ask"),   // 与第一条同 key → 丢掉
            new AiRule("a", "3", "ask")
        });

        Assert.Equal(3, list.Count);
        Assert.Equal("allow", list[0].behavior);
        Assert.Equal("b", list[1].tool_name);
        Assert.Equal("a", list[2].tool_name);
        Assert.Equal("3", list[2].rule_content);
    }

    [Fact]
    public void DedupeRules_coerces_raw_dicts_and_ignores_junk()
    {
        List<AiRule> list = Permission.DedupeRules(new object[]
        {
            PyDict.Of("toolName", "run", "ruleContent", "x"),
            "junk",
            null,
            PyDict.Of("toolName", "run", "ruleContent", "x")
        });

        Assert.Single(list);
        Assert.Equal("run", list[0].tool_name);
    }

    [Fact]
    public void DedupeRules_tolerates_null()
    {
        Assert.Empty(Permission.DedupeRules(null));
    }

    [Fact]
    public void ApplyUpdates_appends_new_rules_and_dedupes_against_the_old_ones()
    {
        List<AiRule> merged = Permission.ApplyUpdates(
            new[] { new AiRule("a", "1", "allow") },
            new[] { new AiRule("a", "1", "deny"), new AiRule("b", "2", "ask") });

        Assert.Equal(2, merged.Count);
        Assert.Equal("allow", merged[0].behavior);   // 老的先出现,老的赢
        Assert.Equal("b", merged[1].tool_name);
        Assert.Empty(Permission.ApplyUpdates(null, null));
    }
}
