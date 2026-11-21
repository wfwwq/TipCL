// AI-engine-mcp:自定义权限规则按参数匹配(DECISIONS D-24 里「content 记在规则里但暂不参与判定」的那一半)。
// 对位参考:Windows 版 Permission.cs 的 RULE_CONTENT_KEYS / RuleContentFromInput / _matches(NormPathSpelling 后整串相等)/
// Decide 里 deny > allow > ask、删除类「无限定 allow」不生效。Linux 多出的作用实例按「这次调用要动的实例」算。
// 外加 MCP Server 用的 ForceConfirmWrites 闸门。
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

public class AiPermissionContentTests
{
    private sealed class FakeApi : ToolRunner.IBackend
    {
        public readonly List<string> Methods = new();

        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
        {
            lock (Methods)
            {
                Methods.Add(method);
            }
            object result = PyDict.Of("task_id", "task-fake");
            return Task.FromResult(result is T t ? t : default);
        }
    }

    private static ToolRunner Runner(string mode, string instance = "") => new(new FakeApi()) { PermissionMode = mode, Instance = instance };

    private static void WithRules(Action body, params PyDict[] rules)
    {
        object? before = Config.CONFIG.Get("ai_permission_rules");
        try
        {
            Config.CONFIG.Set("ai_permission_rules", new List<object>(rules));
            body();
        }
        finally
        {
            Config.CONFIG.Set("ai_permission_rules", before ?? new List<object>());
        }
    }

    private static PyDict Rule(string tool, string behavior, string content = "", string instance = "")
        => PyDict.Of("tool", tool, "behavior", behavior, "content", content, "instance", instance);

    private static (string, string) Pair(ToolRunner.PermissionVerdict v) => (v.decision, v.reason);

    // ───────────────────────── 取值与归一 ─────────────────────────

    [Fact]
    public void RuleContentFromInput_FollowsTheReferenceKeyOrder()
    {
        Assert.Null(PermissionRules.RuleContentFromInput(null));
        Assert.Null(PermissionRules.RuleContentFromInput(PyDict.Of("instance", "x", "content", "y")));
        // slug 排在 name 前面(参考版 RULE_CONTENT_KEYS 的顺序)
        Assert.Equal("sodium", PermissionRules.RuleContentFromInput(PyDict.Of("name", "钠", "slug", "sodium")));
        Assert.Equal("a.jar", PermissionRules.RuleContentFromInput(PyDict.Of("filename", "a.jar", "name", "n")));
        Assert.Equal("cfg/x.toml", PermissionRules.RuleContentFromInput(PyDict.Of("path", "  cfg/x.toml ", "filename", "f")));
        // 空串跳过,接着看下一个键;非字符串不算
        Assert.Equal("17", PermissionRules.RuleContentFromInput(PyDict.Of("path", "  ", "major", "17")));
        Assert.Null(PermissionRules.RuleContentFromInput(PyDict.Of("major", 17L)));
    }

    [Fact]
    public void Matching_NormalizesSpelling_CaseAndSeparators()
    {
        PyDict rule = Rule("write_mod_config", "allow", "Sodium\\Options.JSON");
        Assert.True(PermissionRules.Matches(rule, "write_mod_config", PyDict.Of("path", "sodium/options.json"), ""));
        Assert.True(PermissionRules.Matches(rule, "write_mod_config", PyDict.Of("path", "./sodium//options.json"), ""));
        Assert.False(PermissionRules.Matches(rule, "write_mod_config", PyDict.Of("path", "sodium/other.json"), ""));
        Assert.False(PermissionRules.Matches(rule, "read_mod_config", PyDict.Of("path", "sodium/options.json"), ""));
        // 有限定参数的规则,遇到没给参数的调用不命中
        Assert.False(PermissionRules.Matches(rule, "write_mod_config", null, ""));
        // 参考版条目形状(toolName / ruleContent)一并认
        Assert.True(PermissionRules.Matches(PyDict.Of("toolName", "install_mod", "ruleContent", "sodium", "behavior", "allow"),
            "install_mod", PyDict.Of("slug", "sodium"), ""));
    }

    // ───────────────────────── 进到裁决 ─────────────────────────

    [Fact]
    public void ContentScopedAllow_OnlyCoversThatPath()
    {
        WithRules(() =>
        {
            ToolRunner runner = Runner("default");
            Assert.Equal(("allow", "规则允许"), Pair(runner.Decide("write_mod_config", PyDict.Of("path", "sodium/options.json"))));
            // 别的路径:规则不命中,按档位(write_mod_config 是 high 风险)
            Assert.Equal(("ask", "高风险操作需要确认"), Pair(runner.Decide("write_mod_config", PyDict.Of("path", "iris.properties"))));
            // 不带参数的旧入口:限定了参数的规则不会被当成「整个工具」
            Assert.Equal(("ask", "高风险操作需要确认"), Pair(runner.Decide("write_mod_config")));
        }, Rule("write_mod_config", "allow", "sodium/options.json"));
    }

    [Fact]
    public void ContentScopedDeny_BlocksOnlyThatMod_EvenInYolo()
    {
        WithRules(() =>
        {
            ToolRunner runner = Runner("yolo");
            Assert.Equal(("deny", "这条操作被你的规则禁止"), Pair(runner.Decide("install_mod", PyDict.Of("name", "OptiFine"))));
            Assert.Equal(("allow", "免确认模式"), Pair(runner.Decide("install_mod", PyDict.Of("name", "sodium"))));
        }, Rule("install_mod", "deny", "optifine"));
    }

    [Fact]
    public void DenyBeatsAllow_AllowBeatsAsk_AmongMatchingRules()
    {
        WithRules(() =>
            Assert.Equal("deny", Runner("default").Decide("disable_mod", PyDict.Of("filename", "a.jar")).decision),
            Rule("disable_mod", "allow"), Rule("disable_mod", "deny", "a.jar"), Rule("disable_mod", "ask"));
        WithRules(() =>
            Assert.Equal(("allow", "规则允许"), Pair(Runner("yolo").Decide("disable_mod", PyDict.Of("filename", "a.jar")))),
            Rule("disable_mod", "ask"), Rule("disable_mod", "allow", "a.jar"));
        WithRules(() =>
            Assert.Equal(("ask", "规则要求先询问"), Pair(Runner("yolo").Decide("disable_mod", PyDict.Of("filename", "b.jar")))),
            Rule("disable_mod", "ask"), Rule("disable_mod", "allow", "a.jar"));
    }

    [Fact]
    public void DeleteTools_IgnoreUnscopedAllow_ButHonourScopedAllow()
    {
        WithRules(() =>
        {
            ToolRunner runner = Runner("default");
            // 「整个删除工具都允许」不生效 → 仍按档位问
            Assert.Equal(("ask", "高风险操作需要确认"), Pair(runner.Decide("delete_mod", PyDict.Of("filename", "x.jar"))));
        }, Rule("delete_mod", "allow"));
        WithRules(() =>
        {
            ToolRunner runner = Runner("default");
            Assert.Equal(("allow", "规则允许"), Pair(runner.Decide("delete_mod", PyDict.Of("filename", "old.jar"))));
            Assert.Equal("ask", runner.Decide("delete_mod", PyDict.Of("filename", "new.jar")).decision);
        }, Rule("delete_mod", "allow", "old.jar"));
        // 删除类的 deny 不受这条限制
        WithRules(() => Assert.Equal("deny", Runner("yolo").Decide("delete_instance", PyDict.Of("name", "main")).decision),
            Rule("delete_instance", "deny"));
    }

    [Fact]
    public void InstanceScope_FollowsTheInstanceTheCallTouches()
    {
        WithRules(() =>
        {
            // 会话实例是 survival,参数没写实例 ⇒ 作用在 survival ⇒ 规则命中
            ToolRunner runner = Runner("default", "survival");
            Assert.Equal("allow", runner.Decide("install_mod", PyDict.Of("name", "sodium")).decision);
            // 参数里换成别的实例 ⇒ 规则不命中(不能靠参数绕过「只允许对某实例写」)
            Assert.Equal("ask", runner.Decide("install_mod", PyDict.Of("name", "sodium", "instance", "creative")).decision);
            // 会话在别的实例、参数点名 survival ⇒ 命中
            Assert.Equal("allow", Runner("default", "creative").Decide("install_mod", PyDict.Of("name", "x", "instance", "survival")).decision);
        }, Rule("install_mod", "allow", "", "survival"));
    }

    [Fact]
    public async Task RunAsync_UsesTheCallArguments_ForRuleMatching()
    {
        await WithRulesAsync(async () =>
        {
            var api = new FakeApi();
            var runner = new ToolRunner(api) { PermissionMode = "default", Instance = "default" };
            var allowed = new ToolCall("p1", "disable_mod", PyDict.Of("filename", "Allowed.JAR"));
            await runner.RunAsync(allowed);
            Assert.Equal(ToolCallStatus.COMPLETED, allowed.status);

            // 不匹配的文件:要确认,但没接确认回调 ⇒ 如实拒绝,门面没被调用
            var other = new ToolCall("p2", "disable_mod", PyDict.Of("filename", "other.jar"));
            string denied = await runner.RunAsync(other);
            Assert.Equal(ToolCallStatus.DENIED, other.status);
            Assert.Contains("permission_denied", denied);
            Assert.Equal(new[] { "disable_mod" }, api.Methods);
        }, Rule("disable_mod", "allow", "allowed.jar"));
    }

    // ───────────────────────── 外部写操作闸门(MCP Server 用) ─────────────────────────

    [Fact]
    public async Task ForceConfirmWrites_TurnsAllowIntoAsk_AndNeverRemembersAlways()
    {
        var api = new FakeApi();
        var reasons = new List<string>();
        var runner = new ToolRunner(api)
        {
            PermissionMode = "yolo",
            Instance = "default",
            ForceConfirmWrites = true,
            Confirm = (_, _, reason, _) =>
            {
                reasons.Add(reason);
                return Task.FromResult((true, true));     // 用户点了「以后都允许」也不该被记住
            },
        };
        await runner.RunAsync(new ToolCall("w1", "enable_mod", PyDict.Of("filename", "a.jar")));
        await runner.RunAsync(new ToolCall("w2", "enable_mod", PyDict.Of("filename", "a.jar")));
        Assert.Equal(new[] { ToolRunner.ExternalWriteReason, ToolRunner.ExternalWriteReason }, reasons);
        Assert.False(runner.IsAllowedAlways("enable_mod"));
        Assert.Equal(2, api.Methods.Count);

        // 只读不受影响;拒绝类裁决照旧拒绝(不会变成「问一下」)
        var read = new ToolCall("r1", "list_instances", new PyDict());
        await runner.RunAsync(read);
        Assert.Equal(ToolCallStatus.COMPLETED, read.status);
        runner.PermissionMode = "plan";
        var planned = new ToolCall("w3", "enable_mod", PyDict.Of("filename", "a.jar"));
        await runner.RunAsync(planned);
        Assert.Equal(ToolCallStatus.DENIED, planned.status);
        Assert.Equal(2, reasons.Count);
    }

    private static async Task WithRulesAsync(Func<Task> body, params PyDict[] rules)
    {
        object? before = Config.CONFIG.Get("ai_permission_rules");
        try
        {
            Config.CONFIG.Set("ai_permission_rules", new List<object>(rules));
            await body();
        }
        finally
        {
            Config.CONFIG.Set("ai_permission_rules", before ?? new List<object>());
        }
    }
}
