// M4-H R21e:裁决要认「别名档位」——参考版 Permission.PermissionMode.Values 的 10 档(Permission.cs:30)
// 在 Linux 的 ToolRunner.Decide 里逐条对位,理由文案逐字取自参考版 Permission.cs:220-253。
//
// 红证据:本轮改动前 DecideRaw 的 switch 只有 yolo / plan / acceptEdits 三档,
// auto / autoEdit / build / edit / bypassPermissions / dontAsk 全部落 default 分支 ⇒
// 这些档位的用例会拿到 ("ask","写操作需要确认") / ("ask","高风险操作需要确认"),下面的断言即失败。
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

public class AiPermissionAliasModeTests
{
    private sealed class FakeApi : ToolRunner.IBackend
    {
        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
            => Task.FromResult<T?>(default);
    }

    private static ToolRunner Runner(string mode) => new(new FakeApi()) { PermissionMode = mode };

    private static (string decision, string reason) Pair(ToolRunner.PermissionVerdict v) => (v.decision, v.reason);

    /// <summary>参考版 Permission.PermissionMode.Values 的 10 档(对位时按这个名字集合穷举)。</summary>
    private static readonly string[] ReferenceModes =
    {
        "default", "plan", "edit", "acceptEdits", "auto", "dontAsk", "autoEdit", "yolo", "bypassPermissions", "build",
    };

    // ───────────────────────── yolo / bypassPermissions ─────────────────────────

    [Fact]
    public void NoConfirmModes_AllowEverything_WithReferenceReason()
    {
        foreach (string mode in new[] { "yolo", "bypassPermissions" })
        {
            ToolRunner runner = Runner(mode);
            Assert.Equal(("allow", "免确认模式"), Pair(runner.Decide("install_mod")));
            Assert.Equal(("allow", "免确认模式"), Pair(runner.Decide("delete_instance")));
            Assert.Equal(("allow", "免确认模式"), Pair(runner.Decide("launch_game")));
            // 只读那一支在档位之前,理由仍是「只读操作」
            Assert.Equal(("allow", "只读操作"), Pair(runner.Decide("list_mods")));
        }
    }

    // ───────────────────────── plan ─────────────────────────

    [Fact]
    public void PlanMode_DeniesWrites_NoMatterWhichAliasIsAlsoListed()
    {
        ToolRunner runner = Runner("plan");
        // 理由逐字取自参考版 Permission.cs:187(句中是全角逗号,别改成半角)
        Assert.Equal(("deny", "当前是「只看不动」模式，不能执行这类操作"), Pair(runner.Decide("install_mod")));
        Assert.Equal(("deny", "当前是「只看不动」模式，不能执行这类操作"), Pair(runner.Decide("delete_mod")));
        Assert.Equal(("allow", "只读操作"), Pair(runner.Decide("list_mods")));
    }

    // ───────────────────────── auto / acceptEdits / autoEdit ─────────────────────────

    [Fact]
    public void AutoModes_OnlyDeletesStillAsk()
    {
        foreach (string mode in new[] { "auto", "acceptEdits", "autoEdit" })
        {
            ToolRunner runner = Runner(mode);
            Assert.Equal(("allow", "写操作直接执行"), Pair(runner.Decide("install_mod")));
            Assert.Equal(("allow", "写操作直接执行"), Pair(runner.Decide("write_mod_config")));
            Assert.Equal(("ask", "删除操作仍需确认"), Pair(runner.Decide("delete_mod")));
            Assert.Equal(("ask", "删除操作仍需确认"), Pair(runner.Decide("delete_instance")));
            // 负例:launch 不是 delete,这一档不拦它(与参考版 Permission.cs:225-232 一致)
            Assert.Equal(("allow", "写操作直接执行"), Pair(runner.Decide("launch_game")));
        }
    }

    // ───────────────────────── build ─────────────────────────

    [Fact]
    public void BuildMode_DeletesAndLaunchesAsk()
    {
        ToolRunner runner = Runner("build");
        // 侧效应 delete / launch → 问
        Assert.Equal(("ask", "删除/启动操作仍需确认"), Pair(runner.Decide("delete_mod")));
        Assert.Equal(("ask", "删除/启动操作仍需确认"), Pair(runner.Decide("delete_instance")));
        Assert.Equal(("ask", "删除/启动操作仍需确认"), Pair(runner.Decide("launch_game")));
        // 其余(安装 / 下载 / 写配置)→ 直接执行
        Assert.Equal(("allow", "安装/下载类直接执行"), Pair(runner.Decide("install_mod")));
        Assert.Equal(("allow", "安装/下载类直接执行"), Pair(runner.Decide("install_game")));
        Assert.Equal(("allow", "安装/下载类直接执行"), Pair(runner.Decide("download_java")));
        Assert.Equal(("allow", "安装/下载类直接执行"), Pair(runner.Decide("write_mod_config")));
        // 这一档不允许出现默认分支的文案(负例:别把 build 当成 acceptEdits 的同义档)
        Assert.NotEqual("写操作直接执行", runner.Decide("install_mod").reason);
        Assert.NotEqual("删除操作仍需确认", runner.Decide("delete_mod").reason);
    }

    // ───────────────────────── edit ─────────────────────────

    [Fact]
    public void EditMode_OnlyLocalWritesAllow()
    {
        ToolRunner runner = Runner("edit");
        Assert.Equal(("allow", "文件类修改直接执行"), Pair(runner.Decide("write_mod_config")));
        Assert.Equal(("allow", "文件类修改直接执行"), Pair(runner.Decide("install_mod")));
        Assert.Equal(("allow", "文件类修改直接执行"), Pair(runner.Decide("create_instance")));
        // 负例:删除与启动都不是 write_local ⇒ 问,且理由说明「这类操作」
        Assert.Equal(("ask", "这类操作需要确认"), Pair(runner.Decide("delete_mod")));
        Assert.Equal(("ask", "这类操作需要确认"), Pair(runner.Decide("launch_game")));
    }

    // ───────────────────────── dontAsk ─────────────────────────

    [Fact]
    public void DontAskMode_DeniesInsteadOfAsking_ReadOnlyStillAllowed()
    {
        ToolRunner runner = Runner("dontAsk");
        Assert.Equal(("deny", "用户开启了「不询问」模式"), Pair(runner.Decide("install_mod")));
        Assert.Equal(("deny", "用户开启了「不询问」模式"), Pair(runner.Decide("delete_mod")));
        Assert.Equal(("allow", "只读操作"), Pair(runner.Decide("list_mods")));
        // 负例:它绝不是「问一下」这个档
        Assert.NotEqual("ask", runner.Decide("install_mod").decision);
    }

    // ───────────────────────── default / custom / 未知档名 ─────────────────────────

    [Fact]
    public void DefaultCustomAndUnknownModes_KeepRiskBasedAsk()
    {
        foreach (string mode in new[] { "default", "custom", "nope" })
        {
            ToolRunner runner = Runner(mode);
            Assert.Equal(("ask", "写操作需要确认"), Pair(runner.Decide("install_mod")));
            Assert.Equal(("ask", "高风险操作需要确认"), Pair(runner.Decide("launch_game")));
            Assert.Equal(("ask", "高风险操作需要确认"), Pair(runner.Decide("delete_mod")));
            Assert.Equal(("allow", "只读操作"), Pair(runner.Decide("list_mods")));
        }
        // 空串 = 读配置;这里只验归一化,不碰全局 Config
        Assert.Equal("default", ToolRunner.NormalizeMode(null));
        Assert.Equal("default", ToolRunner.NormalizeMode(""));
    }

    // ───────────────────────── 「告知」与「裁决」不打架 ─────────────────────────

    /// <summary>
    /// PermissionNote 按参考版给别名同一句;这句话必须与 Decide 的实际裁决一致 ——
    /// 这一条正是本轮要修的病:提示说「写操作会直接执行」,裁决却每次弹确认。
    /// </summary>
    [Fact]
    public void Note_AndDecision_AgreeForEveryReferenceMode()
    {
        foreach (string mode in ReferenceModes)
        {
            string note = ToolRunner.PermissionNote(mode, false);
            ToolRunner runner = Runner(mode);
            if (mode is "yolo" or "bypassPermissions")
            {
                Assert.Contains("全自动", note);
                Assert.Equal("allow", runner.Decide("install_mod").decision);
                continue;
            }
            if (mode == "plan")
            {
                Assert.Contains("只看不动", note);
                Assert.Equal("deny", runner.Decide("install_mod").decision);
                continue;
            }
            if (mode == "dontAsk")
            {
                Assert.Contains("不询问", note);
                Assert.Equal("deny", runner.Decide("install_mod").decision);
                continue;
            }
            if (mode == "edit")
            {
                // edit 档只对 write_local 放行。注意 install_mod 在**两仓** ToolMeta 里都是 write_local
                // (windos\...\ToolMeta.cs:21),所以它在 edit 档下会被放行,而 PermissionNote 那句
                // (照抄参考版)说的是「安装...前会弹确认」—— 这处不自洽是参考版自带的,本轮不擅自改文案,
                // 这里按裁决的真实判据断言。
                Assert.Equal("allow", runner.Decide("write_mod_config").decision);
                Assert.Equal("allow", runner.Decide("install_mod").decision);
                Assert.Equal("ask", runner.Decide("delete_mod").decision);
                Assert.Equal("ask", runner.Decide("launch_game").decision);
                continue;
            }
            if (mode == "default")
            {
                Assert.Contains("都会弹确认", note);
                Assert.Equal("ask", runner.Decide("install_mod").decision);
                Assert.Equal("ask", runner.Decide("delete_mod").decision);
                continue;
            }
            // auto / acceptEdits / autoEdit / build:写直接执行,删除仍会先问
            Assert.Equal("allow", runner.Decide("install_mod").decision);
            Assert.Equal("ask", runner.Decide("delete_mod").decision);
        }
    }
    // ───────────────────────── plan 是硬停;规则与只读仍在档位之前 ─────────────────────────

    [Fact]
    public void PlanHardStop_ThenRules_ThenReadOnly()
    {
        object? before = Config.CONFIG.Get("ai_permission_rules");
        try
        {
            Config.CONFIG.Set("ai_permission_rules", new List<object> { PyDict.Of("tool", "install_mod", "behavior", "allow") });
            // 「只看不动」是硬停:参考版把这一步放在规则**之前**(Permission.cs:186-190),allow 规则
            // 也压不过它 —— R21e 拉平的(此前 Linux 规则在前,plan 能被 allow 规则压过去)。
            Assert.Equal(("deny", "当前是「只看不动」模式，不能执行这类操作"), Pair(Runner("plan").Decide("install_mod")));
            // 其它档位下规则照旧优先:build 档下 install_mod 的裁决理由仍是规则那句
            Assert.Equal(("allow", "规则允许"), Pair(Runner("build").Decide("install_mod")));
            Assert.Equal(("allow", "规则允许"), Pair(Runner("dontAsk").Decide("install_mod")));
            // 只读工具在档位之前:连 plan / dontAsk 都放行
            Assert.Equal(("allow", "只读操作"), Pair(Runner("plan").Decide("list_mods")));
            Assert.Equal(("allow", "只读操作"), Pair(Runner("dontAsk").Decide("list_mods")));
        }
        finally
        {
            Config.CONFIG.Set("ai_permission_rules", before ?? new List<object>());
        }
    }

    // ───────────────────────── MCP 那一支(Linux 独有)─────────────────────────

    /// <summary>
    /// MCP 工具走 Linux 独有的 McpVerdict:bypassPermissions 与 yolo 同等放行(参考版没有这一支)。
    /// 断言方式是不接确认回调跑一次 —— ask 会在「界面没接确认回调」处被拒,放行则继续往下走到路由失败。
    /// </summary>
    [Fact]
    public async Task McpTools_BypassPermissionsAllowsLikeYolo()
    {
        foreach (string mode in new[] { "yolo", "bypassPermissions" })
        {
            var call = new ToolCall("m1", "mcp_demo_echo", new PyDict());
            string text = await Runner(mode).RunAsync(call);
            // 放行 ⇒ 走到路由阶段(没有 server,于是报「没有叫 ... 的 MCP 工具」),
            // 但绝不会是权限那一档的拒绝
            Assert.NotEqual(ToolCallStatus.DENIED, call.status);
            Assert.DoesNotContain("permission_denied", text);
        }
        var asked = new ToolCall("m2", "mcp_demo_echo", new PyDict());
        string askedText = await Runner("default").RunAsync(asked);
        Assert.Equal(ToolCallStatus.DENIED, asked.status);
        Assert.Contains("permission_denied", askedText);

        var planned = new ToolCall("m3", "mcp_demo_echo", new PyDict());
        await Runner("plan").RunAsync(planned);
        Assert.Equal(ToolCallStatus.DENIED, planned.status);
    }
}