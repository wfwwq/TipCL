// M4-H R19b(主 agent 补):两个「配置里有开关、代码里没人读」的收口。
//
// 1) `ai_permission_dont_ask`:Linux 的 Config 一直写着这个键的默认值(Config.cs:122)却没人读,
//    而 Windows 版在 Agent 里消费它(Agent.cs:571 / :1149-1152):需要确认的写操作直接拒绝,
//    理由「用户开启了「不询问」模式」。这里按同一语义钉住,并且必须有负例(allow 不受影响)。
// 2) 被拒的步骤行要显示理由:`ToolRunner` 把裁决理由写进 `ToolCall.error`,界面侧
//    `AiPage.Summarize` 对被拒的调用返回它(照 Windows 的 denied 工具状态)。
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TipCL.App.Pages.Ai;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

public class AiPermissionDontAskTests
{
    private sealed class FakeApi : ToolRunner.IBackend
    {
        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
            => Task.FromResult<T?>(default);
    }

    private static ToolRunner Runner(string mode) => new(new FakeApi()) { PermissionMode = mode };

    /// <summary>设置 / 还原 ai_permission_dont_ask(用例共享 Config.CONFIG 这份全局状态)。</summary>
    private static void WithDontAsk(bool value, Action body)
    {
        object? before = Config.CONFIG.Get("ai_permission_dont_ask");
        try
        {
            Config.CONFIG.Set("ai_permission_dont_ask", value);
            body();
        }
        finally
        {
            Config.CONFIG.Set("ai_permission_dont_ask", before ?? false);
        }
    }

    [Fact]
    public void DontAsk_TurnsAskIntoDeny_WithWindowsWording()
    {
        // 开关关着:写操作照旧要问(基线,防止这条用例在开关恒真的实现下也「通过」)
        WithDontAsk(false, () =>
            Assert.Equal(("ask", "写操作需要确认"), Pair(Runner("default").Decide("install_mod"))));

        // 开关打开:同样的调用变成拒绝,理由与 Windows 逐字相同
        WithDontAsk(true, () =>
        {
            ToolRunner runner = Runner("default");
            Assert.Equal(("deny", "用户开启了「不询问」模式"), Pair(runner.Decide("install_mod")));
            // 高风险(删除类)在 default 档本来就要问 → 同样被这条开关拒掉
            Assert.Equal(("deny", "用户开启了「不询问」模式"), Pair(runner.Decide("delete_mod")));
            // 档位不接受规则覆盖:自定义规则说 allow 时不该被「不询问」改写
            object? before = Config.CONFIG.Get("ai_permission_rules");
            try
            {
                Config.CONFIG.Set("ai_permission_rules", new List<object>
                {
                    PyDict.Of("tool", "install_mod", "behavior", "allow"),
                });
                Assert.Equal(("allow", "规则允许"), Pair(runner.Decide("install_mod")));
            }
            finally
            {
                Config.CONFIG.Set("ai_permission_rules", before ?? new List<object>());
            }
        });
    }

    [Fact]
    public void DontAsk_LeavesAllowsAndReadOnlyAlone()
    {
        WithDontAsk(true, () =>
        {
            // 只读与 yolo 档本来就是 allow:开关不该把它们变成拒绝
            Assert.Equal(("allow", "只读操作"), Pair(Runner("default").Decide("list_mods")));
            Assert.Equal(("allow", "免确认模式"), Pair(Runner("yolo").Decide("install_mod")));
            // plan 档本来就是 deny:理由仍应是「只看不动」,不该被换成「不询问」
            // (这里只钉裁决与关键片段:整句的逐字符对位由 Core/AiPermissionReasonTests 负责,
            //  避免本文件里再抄一遍全角逗号 —— 抄错就是一条假失败,见 R19 的教训)
            ToolRunner.PermissionVerdict plan = Runner("plan").Decide("install_mod");
            Assert.Equal("deny", plan.decision);
            Assert.Contains("只看不动", plan.reason);
            Assert.DoesNotContain("不询问", plan.reason);
        });
    }

    [Fact]
    public async Task DeniedCall_CarriesTheReasonForTheStepLine()
    {
        await WithDontAskAsync(true, async () =>
        {
            ToolRunner runner = Runner("default");
            var call = new ToolCall("d1", "install_mod",
                PyDict.Of("name", "sodium", "instance", "default"));
            string result = await runner.RunAsync(call);

            Assert.Equal(ToolCallStatus.DENIED, call.status);
            // 界面读的是 call.error(见 AiPage.Summarize);理由必须一路带到这里
            Assert.Contains("不询问", call.error);
            // 模型可见的文本保持原样(不因为这次改动变脸)
            Assert.Contains("permission_denied", result);
        });
    }

    [Fact]
    public void Summarize_ForDeniedCall_ShowsTheReason_NotTheRawResult()
    {
        MethodInfo? summarize = typeof(AiPage).GetMethod("Summarize",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(summarize);

        var denied = new ToolCall("s1", "install_mod", PyDict.Of("name", "sodium"));
        denied.Mark(ToolCallStatus.DENIED, result: "{\"code\":\"permission_denied\"}", error: "已拒绝:写操作需要确认");
        Assert.Equal("已拒绝:写操作需要确认", summarize!.Invoke(null, new object[] { denied }));

        // 没有理由时给一句兜底,不能显示原始 JSON
        var bare = new ToolCall("s2", "install_mod", PyDict.Of("name", "sodium"));
        bare.Mark(ToolCallStatus.DENIED, result: "{\"code\":\"permission_denied\"}");
        Assert.Equal("已拒绝", summarize.Invoke(null, new object[] { bare }));

        // 负例:完成态的摘要逻辑没被这次改动带偏(仍取结果而不取 error)
        var done = new ToolCall("s3", "install_mod", PyDict.Of("name", "sodium"));
        done.Mark(ToolCallStatus.COMPLETED, result: "装好了", error: "不该被显示");
        Assert.Equal("装好了", summarize.Invoke(null, new object[] { done }));
    }

    private static async Task WithDontAskAsync(bool value, Func<Task> body)
    {
        object? before = Config.CONFIG.Get("ai_permission_dont_ask");
        try
        {
            Config.CONFIG.Set("ai_permission_dont_ask", value);
            await body().ConfigureAwait(false);
        }
        finally
        {
            Config.CONFIG.Set("ai_permission_dont_ask", before ?? false);
        }
    }

    private static (string decision, string reason) Pair(ToolRunner.PermissionVerdict verdict)
        => (verdict.decision, verdict.reason);
}
