// M4-H R19:权限裁决要带出「为什么」——(裁决, 理由) 的逐档位/逐规则断言。
//
// 对位参考:Windows 版 Permission.Decide 返回 PermissionResult(decision, reason),
// 理由文案在 Permission.cs 的 switch 里;Linux 版原先只返回 allow/ask/deny,界面上那行理由
// 永远是空的。这里按 Linux **现有的** 档位语义(Modes = default/acceptEdits/plan/yolo/custom)
// 把每一条的裁决与理由都钉住,并且必须有负例:理由不能是「任何一句非空文本都算过」。
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

public class AiPermissionReasonTests
{
    private sealed class FakeApi : ToolRunner.IBackend
    {
        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
            => Task.FromResult<T?>(default);
    }

    private static ToolRunner Runner(string mode) => new(new FakeApi()) { PermissionMode = mode };

    /// <summary>把自定义规则塞进配置,跑完还原(用例共享 Config.CONFIG 这份全局状态)。</summary>
    private static void WithRules(string tool, string behavior, Action body)
    {
        object? before = Config.CONFIG.Get("ai_permission_rules");
        try
        {
            Config.CONFIG.Set("ai_permission_rules", new List<object>
            {
                PyDict.Of("tool", tool, "behavior", behavior),
            });
            body();
        }
        finally
        {
            Config.CONFIG.Set("ai_permission_rules", before ?? new List<object>());
        }
    }

    // ───────────────────────────── 档位 → (裁决, 理由) ─────────────────────────────

    [Fact]
    public void DefaultMode_Writes_FallBackToRiskBasedReason()
    {
        ToolRunner runner = Runner("default");
        // 中风险的写:Windows 版 default 分支给「写操作需要确认」
        Assert.Equal(("ask", "写操作需要确认"), Pair(runner.Decide("install_mod")));
        // 高风险(删除 / 启动类):Windows 版同一分支里先看风险等级
        Assert.Equal(("ask", "高风险操作需要确认"), Pair(runner.Decide("delete_mod")));
        Assert.Equal(("ask", "高风险操作需要确认"), Pair(runner.Decide("launch_game")));
        Assert.Equal(("ask", "高风险操作需要确认"), Pair(runner.Decide("delete_instance")));
        // 只读工具从不问,理由说明它为什么不用问
        Assert.Equal(("allow", "只读操作"), Pair(runner.Decide("list_mods")));
        Assert.Equal(("allow", "只读操作"), Pair(runner.Decide("read_mod_config")));
    }

    [Fact]
    public void PlanMode_DeniesWrites_WithReadOnlyReason()
    {
        ToolRunner runner = Runner("plan");
        ToolRunner.PermissionVerdict denied = runner.Decide("install_mod");
        Assert.Equal("deny", denied.decision);
        Assert.Contains("只看不动", denied.reason);       // 负例:理由必须真的说明是「只看不动」档
        Assert.NotEqual("写操作需要确认", denied.reason);  // 也不能沿用默认档的文案
        Assert.Equal(("allow", "只读操作"), Pair(runner.Decide("list_mods")));
    }

    [Fact]
    public void YoloMode_AllowsWithNoConfirmReason()
    {
        ToolRunner runner = Runner("yolo");
        Assert.Equal(("allow", "免确认模式"), Pair(runner.Decide("delete_instance")));
        Assert.Equal(("allow", "免确认模式"), Pair(runner.Decide("install_mod")));
        Assert.Equal(("allow", "只读操作"), Pair(runner.Decide("list_mods")));
    }

    [Fact]
    public void AcceptEditsMode_OnlyDeletesStillAsk()
    {
        ToolRunner runner = Runner("acceptEdits");
        Assert.Equal(("allow", "写操作直接执行"), Pair(runner.Decide("install_mod")));
        Assert.Equal(("allow", "写操作直接执行"), Pair(runner.Decide("write_mod_config")));
        Assert.Equal(("ask", "删除操作仍需确认"), Pair(runner.Decide("delete_mod")));
        Assert.Equal(("ask", "删除操作仍需确认"), Pair(runner.Decide("delete_instance")));
        // 负例:启动类不是删除,写直接执行档下不拦也就没有「仍需确认」的理由
        Assert.Equal(("allow", "写操作直接执行"), Pair(runner.Decide("launch_game")));
    }

    [Fact]
    public void CustomMode_FallsBackToDefaultReason()
    {
        // Linux 的 custom 档只走自定义规则,规则没命中时按默认判定(与 Windows 的 _norm_mode 一致)
        ToolRunner runner = Runner("custom");
        Assert.Equal(("ask", "写操作需要确认"), Pair(runner.Decide("install_mod")));
        Assert.Equal(("ask", "高风险操作需要确认"), Pair(runner.Decide("launch_game")));
        Assert.Equal(("allow", "只读操作"), Pair(runner.Decide("list_mods")));
    }

    // ───────────────────────────── 自定义规则 → (裁决, 理由) ─────────────────────────────

    [Fact]
    public void Rules_CarryTheirOwnReasons_AndBeatTheMode()
    {
        WithRules("install_mod", "allow", () =>
            Assert.Equal(("allow", "规则允许"), Pair(Runner("default").Decide("install_mod"))));
        WithRules("install_mod", "deny", () =>
            Assert.Equal(("deny", "这条操作被你的规则禁止"), Pair(Runner("yolo").Decide("install_mod"))));
        WithRules("install_mod", "ask", () =>
            Assert.Equal(("ask", "规则要求先询问"), Pair(Runner("yolo").Decide("install_mod"))));
        // 负例:规则只对写的那一个工具生效,别的工具仍按档位算
        WithRules("install_mod", "deny", () =>
        {
            ToolRunner runner = Runner("yolo");
            Assert.Equal(("deny", "这条操作被你的规则禁止"), Pair(runner.Decide("install_mod")));
            Assert.Equal(("allow", "免确认模式"), Pair(runner.Decide("install_game")));
        });
    }

    // ───────────────────────────── 未知工具 ─────────────────────────────

    [Fact]
    public void UnknownTool_IsDeniedWithItsOwnReason()
    {
        // 工具表里没有的名字一律 deny(RunAsync 在更早期就按 unknown_tool 报错,这里只保证理由非空)
        ToolRunner.PermissionVerdict verdict = Runner("default").Decide("no_such_tool");
        Assert.Equal("deny", verdict.decision);
        Assert.False(string.IsNullOrWhiteSpace(verdict.reason));
        Assert.NotEqual("", verdict.reason);
    }

    private static (string decision, string reason) Pair(ToolRunner.PermissionVerdict verdict)
        => (verdict.decision, verdict.reason);
}
