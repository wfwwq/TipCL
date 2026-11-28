// AI5-B3:确认卡上选的作用域要落盘(「本对话都允许」原先只在进程内记一个工具名,重启就失效)。
//
// 复现(修复前):ConfirmCard 已经算出 "global" | "instance",但两个调用点都写成 `(ok, always, _)`
// 把作用域丢掉;执行器只在 _alwaysAllowed(HashSet)里记住工具名 ⇒ 换实例照样放行、重启又从头问。
//
// 修法:按作用域写进 config.json 的 ai_permission_rules(PermissionRules 已经认这张表,含 instance
// 限定),面板按同一张表显示。删除类 / 高危写工具不给这条捷径(红线:删除类不给无限定 allow)。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class AiPermissionScopeTests : IDisposable
{
    private sealed class FakeApi : ToolRunner.IBackend
    {
        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
            => Task.FromResult<T?>(default);
    }

    private readonly object? _beforeRules;

    public AiPermissionScopeTests()
    {
        _beforeRules = Config.CONFIG.Get("ai_permission_rules");
        Config.CONFIG.Set("ai_permission_rules", new List<object>());
    }

    public void Dispose() => Config.CONFIG.Set("ai_permission_rules", _beforeRules ?? new List<object>());

    private static List<PyDict> Rules()
        => Py.ListOr(Config.CONFIG.Get("ai_permission_rules")).Select(Py.D).ToList();

    private static ToolRunner Runner(string instance, string mode = "default")
        => new(new FakeApi()) { Instance = instance, PermissionMode = mode };

    /// <summary>「所有实例」:规则不带 instance 限定 ⇒ 换一个实例也放行(重启后仍生效)。</summary>
    [Fact]
    public void GlobalScope_AllowsTheToolForEveryInstance()
    {
        Assert.True(ToolRunner.RememberScope("disable_mod", ToolRunner.ScopeGlobal, "inst-a"));

        List<PyDict> rules = Rules();
        Assert.Single(rules);
        Assert.Equal("disable_mod", Py.S(rules[0].Get("tool")));
        Assert.Equal("allow", Py.S(rules[0].Get("behavior")));
        Assert.Equal("", Py.S(rules[0].Get("instance")));

        // 模拟重启 / 换实例:裁决直接读落盘的规则表
        Assert.Equal(("allow", "规则允许"), Pair(Runner("inst-b").Decide("disable_mod", PyDict.Of("instance", "inst-b"))));
    }

    /// <summary>「仅当前实例」:规则带 instance 限定 ⇒ 只对那个实例生效。</summary>
    [Fact]
    public void InstanceScope_OnlyAppliesToThatInstance()
    {
        Assert.True(ToolRunner.RememberScope("disable_mod", ToolRunner.ScopeInstance, "inst-a"));
        Assert.Equal("inst-a", Py.S(Rules()[0].Get("instance")));

        Assert.Equal("allow", Runner("inst-a").Decide("disable_mod").decision);
        // 会话实例不同 ⇒ 仍然要问(这条正是修复前丢掉作用域时最明显的行为差)
        Assert.Equal("ask", Runner("inst-b").Decide("disable_mod").decision);
        // 参数里明确作用到别的实例 ⇒ 也要问
        Assert.Equal("ask", Runner("inst-a").Decide("disable_mod", PyDict.Of("instance", "inst-b")).decision);
        Assert.Equal("allow", Runner("inst-a").Decide("disable_mod", PyDict.Of("instance", "inst-a")).decision);
    }

    /// <summary>同一个(工具, 作用域)重复写只留一条(与权限面板的去重口径一致)。</summary>
    [Fact]
    public void RememberScope_DoesNotDuplicateRules()
    {
        Assert.True(ToolRunner.RememberScope("disable_mod", ToolRunner.ScopeGlobal, "inst-a"));
        Assert.True(ToolRunner.RememberScope("disable_mod", ToolRunner.ScopeGlobal, "inst-a"));
        Assert.Single(Rules());

        // 同一个工具 + 不同作用域是两条不同的规则,可以并存
        Assert.True(ToolRunner.RememberScope("disable_mod", ToolRunner.ScopeInstance, "inst-a"));
        Assert.Equal(2, Rules().Count);
    }

    /// <summary>红线:删除类与「不给本对话都允许」的高危写工具一个字也不写。</summary>
    [Theory]
    [InlineData("delete_mod")]
    [InlineData("delete_instance")]
    [InlineData("restore_save_backup")]
    [InlineData("fs_write")]
    [InlineData("remove_server")]
    [InlineData("apply_instance_upgrade")]
    [InlineData("export_modpack")]
    [InlineData("install_mods_batch")]
    [InlineData("write_kubejs_script")]
    [InlineData("revert_local_mod")]
    public void RememberScope_RefusesDeleteAndDangerousTools(string tool)
    {
        Assert.False(ToolRunner.CanRememberAlways(tool), tool + " 不该给「本对话都允许」");
        Assert.False(ToolRunner.RememberScope(tool, ToolRunner.ScopeGlobal, "inst-a"), tool + " 不该被写进规则表");
        Assert.Empty(Rules());
        // 没被写进规则 ⇒ 换实例/重启后照样要确认
        Assert.Equal("ask", Runner("inst-a").Decide(tool, PyDict.Of("instance", "inst-a")).decision);
    }

    /// <summary>作用域取值不合法(空 / 生造)时不写规则 —— 别让「没选」变成「所有实例」。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("everywhere")]
    public void RememberScope_RejectsUnknownScope(string scope)
    {
        Assert.False(ToolRunner.RememberScope("disable_mod", scope, "inst-a"));
        Assert.Empty(Rules());
    }

    /// <summary>作用域为空串(宿主没接 AlwaysScope)时也不写规则 —— 老宿主(测试 / 外部 MCP)口径不变。</summary>
    [Fact]
    public void RememberScope_RejectsMissingScope()
    {
        Assert.False(ToolRunner.RememberScope("disable_mod", null, "inst-a"));
        Assert.Empty(Rules());
    }

    /// <summary>
    /// 端到端(执行器侧):确认卡回「本对话都允许」+ 作用域 = 所有实例 ⇒ 规则落盘,下一次(新执行器,
    /// 相当于重启)不再弹卡。宿主没接 AlwaysScope 时保持老行为:只在进程内记住,不落盘。
    /// </summary>
    [Fact]
    public async Task ConfirmWithAlways_PersistsTheChosenScope()
    {
        var scoped = new ToolRunner(new FakeApi())
        {
            Instance = "inst-a",
            PermissionMode = "default",
            Confirm = (_, _, _, _) => Task.FromResult((true, true)),
            AlwaysScope = () => ToolRunner.ScopeGlobal,
        };
        await scoped.RunAsync(new ToolCall("c1", "disable_mod", PyDict.Of("instance", "inst-a", "filename", "a.jar")));

        Assert.Single(Rules());
        Assert.Equal("", Py.S(Rules()[0].Get("instance")));
        // 进程内也记住了(本轮后续调用不再问),但真正跨会话的是规则
        Assert.True(scoped.IsAllowedAlways("disable_mod"));
        Assert.Equal("allow", new ToolRunner(new FakeApi()) { Instance = "inst-b", PermissionMode = "default" }
            .Decide("disable_mod").decision);

        // 对照(负例):没有 AlwaysScope 回调的宿主仍然不落盘
        Config.CONFIG.Set("ai_permission_rules", new List<object>());
        var unscoped = new ToolRunner(new FakeApi())
        {
            Instance = "inst-a",
            PermissionMode = "default",
            Confirm = (_, _, _, _) => Task.FromResult((true, true)),
        };
        await unscoped.RunAsync(new ToolCall("c2", "disable_mod", PyDict.Of("instance", "inst-a", "filename", "a.jar")));
        Assert.Empty(Rules());
        Assert.True(unscoped.IsAllowedAlways("disable_mod"));
    }

    /// <summary>
    /// 确认卡答「本对话都允许」但工具是删除类:这次照旧执行(用户点了允许),但**一个字也不写进规则表**
    /// —— 界面本来就不给删除类显示那个按钮,执行器这边再守一道(红线:删除类不给无限定 allow)。
    /// </summary>
    [Fact]
    public async Task ConfirmWithAlways_OnDeleteTool_WritesNothing()
    {
        var runner = new ToolRunner(new FakeApi())
        {
            Instance = "inst-a",
            PermissionMode = "default",
            Confirm = (_, _, _, _) => Task.FromResult((true, true)),
            AlwaysScope = () => ToolRunner.ScopeGlobal,
        };
        var call = new ToolCall("d1", "delete_mod", PyDict.Of("instance", "inst-a", "filename", "a.jar"));
        await runner.RunAsync(call);

        Assert.Equal(ToolCallStatus.COMPLETED, call.status);   // 用户点了允许,这一步照做
        Assert.Empty(Rules());                                  // 但不落盘
        Assert.False(ToolRunner.CanRememberAlways("delete_mod"));
    }

    private static (string decision, string reason) Pair(ToolRunner.PermissionVerdict verdict)
        => (verdict.decision, verdict.reason);
}
