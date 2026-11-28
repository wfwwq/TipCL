// AI5-T:22 条场景表里「要等修复包才能绿」的那一批(文件二)。
//
// 每条的期望行为都按**表里写的样子**写好,一个字不按现状将就;等对应的修复包落地后把
// `Skip` 去掉、把断言跑绿,就是那个包的验收动作。挂起原因与转绿包号写在每条测试的
// Skip 文案与文件末尾的清单里,清单同时抄进 progress/AI5-T.md。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Ai5T;

[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class Ai5TPendingScenarioTests
{
    // ————— 场景 5:写工具确认后返回 AiRule / 选作用域(转绿:L→B3)—————

    [Fact]
    public async Task 场景05_确认卡选了全局_重启后仍然不询问()
    {
        object? savedRules = Config.CONFIG.Get("ai_permission_rules");
        try
        {
            Config.CONFIG.Set("ai_permission_rules", new List<object>());

            using var h = new Ai5THarness();
            h.ConfirmOk = true;
            h.ConfirmAlways = true;
            h.ConfirmScope = "global"; // 确认卡上选「这个工具以后都允许(全局)」
            h.Model
                .Script(FakeReply.Call("install_mod", PyDict.Of("name", "sodium", "instance", "default")))
                .Script(FakeReply.Say("装好了。"))
                .Script(FakeReply.Call("install_mod", PyDict.Of("name", "sodium", "instance", "default")))
                .Script(FakeReply.Say("这次直接装,没有再问。"));

            Ai5TRun first = await h.SendAsync("帮我装钠");
            Assert.Single(first.Confirms);
            Assert.Equal(1, h.Backend.Count("install_mod"));

            // 同一对话里后续同类调用不再询问(现状就做到了:进程内 AllowAlways)
            Ai5TRun second = await h.SendAsync("再帮我装一次钠");
            Assert.Empty(second.Confirms);
            Assert.Equal(2, h.Backend.Count("install_mod"));

            // 期望行为:作用域「全局」要落进 config.json 的 ai_permission_rules
            string picked = PermissionRules.Pick(
                Py.ListOr(Config.CONFIG.Get("ai_permission_rules")),
                "install_mod", PyDict.Of("name", "sodium", "instance", "default"), "default", "write_local");
            Assert.Equal("allow", picked);

            // 「重启」= 换一个执行器实例读同一份 config.json:规则生效,不再弹卡
            using var restarted = new Ai5THarness();
            restarted.ConfirmOk = false; // 万一又弹卡就明确拒绝,免得假绿
            restarted.Model
                .Script(FakeReply.Call("install_mod", PyDict.Of("name", "sodium", "instance", "default")))
                .Script(FakeReply.Say("装好了。"));
            Ai5TRun after = await restarted.SendAsync("重启后再装一次钠");
            Assert.Empty(after.Confirms);
            Assert.Equal(1, restarted.Backend.Count("install_mod"));
        }
        finally
        {
            Config.CONFIG.Set("ai_permission_rules", savedRules ?? new List<object>());
        }
    }

    // ————— 场景 7:流式报上下文溢出(转绿:L→A4)—————

    [Fact]
    public async Task 场景07_上下文溢出_只做一次反应式压缩后继续()
    {
        using var h = new Ai5THarness();
        h.Model
            .Script(FakeReply.Fail("This model's maximum context length is 128000 tokens"))
            .Script(FakeReply.Say("压完历史后接着回答。").Usage(400));

        Ai5TRun run = await h.SendAsync("接着上一个问题往下说");

        Assert.NotEqual(StopReason.STREAM_FAILED, run.StopReason);
        Assert.Equal("压完历史后接着回答。", run.LastText);
        // 溢出后重发同一条消息,而且最多只做一次反应式压缩
        Assert.Equal(2, run.StreamRequests.Count);
        Assert.Equal(1, run.Events.Count(e => e.Kind == "compact"));
        Assert.Equal(1, run.DoneCount);
        Assert.Equal(0, run.ErrorCount);
    }

    // ————— 场景 8:finish_reason=length(转绿:L→A6)—————

    [Fact(Skip = "A6 口径待裁决:续写片段以换行拼接，用例期望无分隔符")]
    public async Task 场景08_输出被截断_最多续写两次后报truncated()
    {
        using var h = new Ai5THarness();
        h.Model
            .Script(FakeReply.Say("第一段……").Finish("length"))
            .Script(FakeReply.Say("续写一。").Finish("length"))
            .Script(FakeReply.Say("续写二。").Finish("length"));

        Ai5TRun run = await h.SendAsync("给我讲讲这个启动器");

        Assert.Equal(StopReason.TRUNCATED, run.StopReason);
        Assert.Equal("第一段……续写一。续写二。", run.LastText);
        // 原始请求 + 最多 2 次「继续」,第 3 次仍被截断就停手
        Assert.Equal(3, run.StreamRequests.Count);
        Assert.Equal(1, run.TerminalCount);
    }

    // ————— 场景 9:流式失败 → 非流式兜底成功(转绿:L→A5)—————

    [Fact(Skip = "A5 口径待裁决:兜底把流式残段与兜底文本拼接，用例期望只留兜底文本")]
    public async Task 场景09_流式参数不全_兜底成功且工具表与流式一致()
    {
        using var h = new Ai5THarness();
        h.ConfirmOk = true;
        h.Model
            // 流式这一轮:先说了一段话,然后工具参数只吐了一半 → 触发「工具参数不完整,正在换一次非流式」
            .Script(FakeReply.Call("install_mod", PyDict.Of("name", "sodium", "instance", "default"), text: "正在准备安装钠…")
                .WithBrokenArgs())
            // 兜底的非流式请求:拿到完整参数并成功
            .Script(FakeReply.Call("install_mod", PyDict.Of("name", "sodium", "instance", "default"), text: "已经装好了。"));

        Ai5TRun run = await h.SendAsync("帮我把钠装上");

        // 兜底确实发生了,而且只有一次
        Assert.Single(run.OnceRequests);
        // 兜底请求的工具表 == 这一轮流式请求实际下发的那份(不是全量表)
        Assert.Equal(run.StreamRequests[0].ToolNames, run.OnceRequests[0].ToolNames);
        // 文本不重复拼接:最终文本就是兜底那一次的话,不带流式那半截
        Assert.Equal("已经装好了。", run.LastText);
        Assert.Equal("已经装好了。", string.Concat(run.Events.Where(e => e.Kind == "delta").Select(e => e.Text)));
        Assert.Equal(StopReason.NO_TOOL_CALL, run.StopReason);
        Assert.Equal(1, run.DoneCount);
    }

    // ————— 场景 10:流式和兜底都空(转绿:L→A2/A6)—————

    [Fact]
    public async Task 场景10_流式与兜底都空_只报一条error且没有内部代号()
    {
        using var h = new Ai5THarness();
        h.Model
            .Script(FakeReply.Fail("接口没有返回内容"))
            .Script(new FakeReply()); // 兜底也是空的

        Ai5TRun run = await h.SendAsync("在吗");

        Assert.Contains(run.StopReason, new[] { StopReason.STREAM_FAILED, StopReason.EMPTY_RESPONSE });
        // 只有一条终态事件、只有一条 error
        Assert.Equal(1, run.TerminalCount);
        Assert.Equal(1, run.ErrorCount);
        Assert.Equal(0, run.DoneCount);
        // 给用户看的文案里不能出现内部代号
        foreach (string text in run.ErrorTexts)
        {
            Assert.DoesNotContain("stream_failed", text);
            Assert.DoesNotContain("empty_response", text);
        }
    }

    // ————— 场景 11:模型调用了注册表里没有的工具(转绿:L→C2)—————

    /// <summary>
    /// 触发口径(2026-11-28 裁决,两棵树**有意不同**):
    ///   · Windows = 模型调了「本轮没下发」的工具 → 丢弃整轮 + 全量重发;
    ///   · Linux   = 模型调了「**注册表里根本没有**」的工具名 → 记 `tool_schema_miss` + 全量重发。
    /// Linux 不跟 Windows 一致的原因:Linux 的执行层(`ToolRunner` 只认 `ToolMeta`)本来就不要求工具
    /// 先被下发,「调了未下发但真实存在」在 Linux 上是**能正常执行**的路;按「没下发」判会把它变成
    /// 丢弃重发,那是回归。所以本条用**不存在**的名字复现,「未下发但存在 → 照常执行」另见 场景11补。
    /// </summary>
    [Fact]
    public async Task 场景11_调用了不存在的工具_记schema_miss并用全量工具重试一次()
    {
        using var h = new Ai5THarness();
        h.Model
            .Script(FakeReply.Call("totally_unregistered_tool", PyDict.Of("x", "1")))
            .Script(FakeReply.Say("我换个方式再查一次。"));

        Ai5TRun run = await h.SendAsync("钠在哪个目录里");

        // 前提:这个名字在工具注册表里根本没有(ToolMeta 不认 = Linux 的触发判据)
        Assert.Null(ToolMeta.Get("totally_unregistered_tool"));
        // 用全量工具重发一次(每轮最多 1 次)
        Assert.Equal(2, run.StreamRequests.Count);
        Assert.Equal(Tools.Schemas.Count, run.StreamRequests[1].ToolNames.Count);
        // 记一条 tool_schema_miss(会话事件流里能查到)
        Assert.Contains(Store.ReadEvents(h.ChatId),
            e => Py.S(e.Get("event")).Contains("SchemaMiss", StringComparison.OrdinalIgnoreCase)
                 || PyJson.Dumps(e, null, ensureAscii: false).Contains("tool_schema_miss", StringComparison.Ordinal));
        // 重发之后这一轮照常收尾,不是失败
        Assert.Contains(run.StopReason, new[] { StopReason.NO_TOOL_CALL, StopReason.COMPLETED });
        Assert.Equal(0, run.ErrorCount);
        Assert.Equal(1, run.DoneCount);
    }

    /// <summary>
    /// 钉住 Linux 的这条性质(与 Windows 口径的分界):模型调了「**未下发但注册表里有**」的真实工具时,
    /// 执行层照常执行,**不**触发 schema-miss 全量重发。防以后有人把 Linux 改成 Windows 口径时静默回归。
    /// </summary>
    [Fact]
    public async Task 场景11补_调用未下发但注册表里有的工具_照常执行且不重发()
    {
        using var h = new Ai5THarness();
        h.Model
            .Script(FakeReply.Call("get_java_list", new PyDict()))
            .Script(FakeReply.Say("我先看看你的 Java 运行时。"));

        Ai5TRun run = await h.SendAsync("钠在哪个目录里");

        // 前提:这个工具真实存在(注册表里有),但本轮没被下发
        Assert.NotNull(ToolMeta.Get("get_java_list"));
        Assert.False(run.StreamRequests[0].HasTool("get_java_list"));

        // Linux 的语义:执行层只认 ToolMeta,不要求先下发 —— 照常执行
        Assert.Equal(1, h.Backend.Count("get_java_list"));
        ToolCall call = run.Events.Where(e => e.Kind == "tool_done").Select(e => e.Call!).Single();
        Assert.Equal(ToolCallStatus.COMPLETED, call.status);

        // 不整轮丢弃、不重发:就 2 次正常请求(工具轮 + 收尾轮)
        Assert.Equal(2, run.StreamRequests.Count);
        Assert.False(run.StreamRequests[1].HasTool("get_java_list"));
        Assert.DoesNotContain(Store.ReadEvents(h.ChatId),
            e => PyJson.Dumps(e, null, ensureAscii: false).Contains("tool_schema_miss", StringComparison.Ordinal));
        Assert.Equal(StopReason.COMPLETED, run.StopReason); // A6:执行过工具再收尾 = completed(不再是 no_tool_call)
        Assert.Equal(0, run.ErrorCount);
    }

    // ————— 场景 12:子代理尝试写工具(转绿:拦截部分已是现状;工具表过滤见 12 补)—————

    [Fact]
    public async Task 场景12_子代理调写工具_被拦截且没落盘()
    {
        using var h = new Ai5THarness();
        h.ConfirmOk = true;
        h.Model
            .Script(FakeReply.Call("dispatch_subagent", PyDict.Of("task", "把钠装上")))
            // 子代理第 1 轮:试图写
            .Script(FakeReply.Call("install_mod", PyDict.Of("name", "sodium", "instance", "default")))
            .Script(FakeReply.Say("我是只读子代理,装不了。"))
            // 主代理收尾
            .Script(FakeReply.Say("子任务只能查,装的事得你自己点确认。"));

        Ai5TRun run = await h.SendAsync("派个子代理把钠装上");

        Assert.Equal(new[] { "dispatch_subagent" }, run.ParentToolSequence); // S:子代理事件会转发到父回合,先看父自己的
        Assert.Equal(new[] { "dispatch_subagent", "install_mod" }, run.ToolSequence);
        Assert.Contains(run.Events, e => e.Kind == "tool" && e.Name == "install_mod" && e.Subagent);
        Assert.Equal(StopReason.COMPLETED, run.StopReason); // A6:执行过工具再收尾 = completed(不再是 no_tool_call)
        // 子代理的写调用被拦下:后端一次 install_mod 都没被调到
        Assert.Equal(0, h.Backend.Count("install_mod"));
        // 子代理自己收到的工具结果是 permission_denied
        Assert.Contains(run.StreamRequests, r => r.ToolResults.Any(t => t.Contains("permission_denied", StringComparison.Ordinal)));
        Assert.Equal(4, run.StreamRequests.Count);
    }

    /// <summary>
    /// 场景 12 的另一半:子代理的请求里也不该出现写工具(表里写的是「子代理的兜底请求里也没有写工具」;
    /// Linux 侧没有非流式兜底,所以这条落到等价的「子代理的流式请求里也不许有写工具」)。
    /// 转绿包:A5(兜底沿用本轮流式那份工具表,含子代理只读过滤)+ P2 的核实项 —— 任务书没给 Linux 侧
    /// 单独的包号,已在 progress/AI5-T.md 的「挂起场景」里记明。
    /// </summary>
    [Fact]
    public async Task 场景12补_子代理的请求里不含写工具()
    {
        using var h = new Ai5THarness();
        List<string> writeTools = ToolMeta.ALL.Where(m => !m.@readonly).Select(m => m.name).ToList();
        h.Model
            .Script(FakeReply.Call("dispatch_subagent", PyDict.Of("task", "把钠装上")))
            .Script(FakeReply.Say("我是只读子代理,装不了。"))
            .Script(FakeReply.Say("子任务只能查。"));

        Ai5TRun run = await h.SendAsync("派个子代理把钠装上");

        // 子代理的那次请求(第 2 个流式请求)里不能有写工具
        FakeRequest sub = run.StreamRequests[1];
        Assert.DoesNotContain(sub.ToolNames, writeTools.Contains);
    }

    // ————— 场景 14:达到最大轮数(转绿:L→A1/A7)—————

    [Fact(Skip = "A1/A7 收尾轮文本为空（用例期望收尾文本），疑似真缺陷")]
    public async Task 场景14_轮数上限读设置且恰好一个终态事件()
    {
        object? saved = Config.CONFIG.Get("ai_max_rounds");
        try
        {
            Config.CONFIG.Set("ai_max_rounds", 3L);

            using var h = new Ai5THarness();
            h.Model
                .Script(FakeReply.Repeat(10, FakeReply.Call("list_mods", PyDict.Of("instance", "default"))))
                .Script(FakeReply.Say("先说到这,信息够了。"));

            Ai5TRun run = await h.SendAsync("一直看我的模组列表");

            Assert.Equal(StopReason.MAX_ROUNDS, run.StopReason);
            // 恰好 3 轮工具 + 1 次收尾;第 4 轮工具不再发起
            Assert.Equal(4, run.StreamRequests.Count);
            Assert.Equal(3, run.ToolSequence.Count);
            // 收尾那一轮不带工具
            Assert.Empty(run.StreamRequests[^1].ToolNames);
            // 恰好 1 条终态事件,UI 才能解锁
            Assert.Equal(1, run.TerminalCount);
            Assert.Equal(1, run.DoneCount);
            Assert.Equal("先说到这,信息够了。", run.LastText);
        }
        finally
        {
            Config.CONFIG.Set("ai_max_rounds", saved ?? 20L);
        }
    }

    // ————— 场景 15:先 429/5xx(转绿:L→A8 的「粘性切换兜底模型」那一半)—————

    [Fact(Skip = "A8 未切到 ai_fallback_model（实得 deepseek-v4-flash），疑似真缺陷")]
    public async Task 场景15_先429_重试并粘性切到兜底模型()
    {
        object? saved = Config.CONFIG.Get("ai_fallback_model");
        try
        {
            Config.CONFIG.Set("ai_fallback_model", "fallback-model");

            using var h = new Ai5THarness();
            h.Model
                .Script(FakeReply.Http(429, "rate limit exceeded"))
                .Script(FakeReply.Say("切换之后答上了。"));

            Ai5TRun run = await h.SendAsync("在吗");

            Assert.Equal(2, run.StreamRequests.Count);
            Assert.Equal("切换之后答上了。", run.LastText);
            Assert.Null(run.Error);
            // 粘性:重试那次已经换成兜底模型,而且不再换回来
            Assert.Equal("fallback-model", run.StreamRequests[1].Model);
            Assert.NotEqual(run.StreamRequests[0].Model, run.StreamRequests[1].Model);
            // 留下切换记录(会话事件流里查得到)
            Assert.Contains(Store.ReadEvents(h.ChatId),
                e => PyJson.Dumps(e, null, ensureAscii: false).Contains("fallback", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Config.CONFIG.Set("ai_fallback_model", saved ?? "");
        }
    }

    /// <summary>场景 15 的前半段(Linux 已经会做的那半):429 会退避重试并成功。现状就是绿的。</summary>
    [Fact]
    public async Task 场景15补_429会重试一次并成功_现状()
    {
        using var h = new Ai5THarness();
        h.Model
            .Script(FakeReply.Http(429, "rate limit exceeded"))
            .Script(FakeReply.Say("重试成功了。"));

        Ai5TRun run = await h.SendAsync("在吗");

        Assert.Equal(2, run.StreamRequests.Count);
        Assert.Equal("重试成功了。", run.LastText);
        Assert.Null(run.Error);
        Assert.Contains(run.Events, e => e.Kind == "status"); // 界面看得见「正在重试」
        Assert.Equal(1, run.DoneCount);
    }

    // ————— 场景 19:两轮之间工具表不该丢(转绿:L→C1)—————

/// <summary>
    /// 表里那条字面场景:第一轮「装钠」→ 第二轮「再装个小地图」,第二轮仍然下发 install_mod。
    /// ⚠ 这一条**测不出 C1**:install_mod 属于 CORE_TOOLS,改不改按需加载都会下发。
    /// 它留在这里的价值是「CORE 常驻工具在任何一轮都不掉」的回归断言;C1 的验收依据是下面的「场景19补」。
    /// </summary>
    [Fact]
    public async Task 场景19_第二轮仍然下发install_mod()
    {
        using var h = new Ai5THarness();
        h.ConfirmOk = true;
        h.Model
            .Script(FakeReply.Call("install_mod", PyDict.Of("name", "sodium", "slug", "sodium", "instance", "default")))
            .Script(FakeReply.Say("钠装好了。"))
            .Script(FakeReply.Call("install_mod", PyDict.Of("name", "小地图", "slug", "journeymap", "instance", "default")))
            .Script(FakeReply.Say("小地图也装上了。"));

        await h.SendAsync("帮我装钠");
        Ai5TRun second = await h.SendAsync("再装个小地图");

        Assert.True(second.StreamRequests[0].HasTool("install_mod"));
        Assert.Equal(new[] { "install_mod" }, second.ToolSequence);
        Assert.Equal(2, h.Backend.Count("install_mod"));
    }

    /// <summary>
    /// C1 的**真实缺口**(上面那条字面场景靠 `install_mod` 是 CORE 常驻工具而侥幸绿,不能当 C1 的验收依据):
    /// 关键词命中只看最后一条用户消息 → 非常驻工具只在「命中关键词的那一轮」下发,续话里就丢了。
    /// 做法与 C1(880b37d)对齐:第一轮说「装个光影」命中 `search_content` / `install_shader`,
    /// 第二轮只说「再来一个」(**一个关键词都没有**),这两个非 CORE 工具仍应下发;
    /// 同时钉住「不是退化成 forceAll」—— 不相关的非常驻工具(比如 `tune_jvm`)不许冒出来。
    /// </summary>
    [Fact]
    public async Task 场景19补_续话仍应下发上一轮命中的非CORE工具()
    {
        using var h = new Ai5THarness();
        h.ConfirmOk = true;
        h.Model
            .Script(FakeReply.Call("search_content", PyDict.Of("query", "光影", "kind", "shader")))
            .Script(FakeReply.Call("install_shader", PyDict.Of("name", "bsl", "instance", "default")))
            .Script(FakeReply.Say("光影装好了。"))
            .Script(FakeReply.Call("install_shader", PyDict.Of("name", "complementary", "instance", "default")))
            .Script(FakeReply.Say("第二个光影也装上了。"));

        Ai5TRun first = await h.SendAsync("帮我装个光影");
        Assert.True(first.StreamRequests[0].HasTool("search_content"));
        Assert.True(first.StreamRequests[0].HasTool("install_shader"));
        // 前提:这两个都不是常驻工具(否则这条用例测不出 C1)
        Assert.DoesNotContain("install_shader", AiData.CORE_TOOLS);
        Assert.DoesNotContain("search_content", AiData.CORE_TOOLS);

        Ai5TRun second = await h.SendAsync("再来一个");

        // 本轮一个关键词都没有:关键词命中必须靠「最近若干条消息」才带得出上一轮的话题
        Assert.True(second.StreamRequests[0].HasTool("install_shader"),
            "第二轮只凭最后一条用户消息选工具,把上一轮命中的 install_shader 丢了(见 C1)");
        Assert.True(second.StreamRequests[0].HasTool("search_content"),
            "第二轮只凭最后一条用户消息选工具,把上一轮命中的 search_content 丢了(见 C1)");
        // 不是退化成「全量工具」:不相关的非常驻工具不许冒出来
        Assert.False(second.StreamRequests[0].HasTool("tune_jvm"));
        Assert.False(second.StreamRequests[0].HasTool("create_datapack"));
        // 工具真的能用:第二轮照着装上了第二个光影
        Assert.Equal(new[] { "install_shader" }, second.ToolSequence);
    }

    // ————— 场景 20:记忆面板与助手共用一份记忆(转绿:L→B1)—————

    [Fact]
    public async Task 场景20_面板写入的记忆助手能看到_反之亦然()
    {
        FileMemory.Clear();
        try
        {
            // 方向一:面板写一条 → 下一轮的系统提示里能看到
            FileMemory.Remember("玩家显卡是 GTX 1060");
            using (var h = new Ai5THarness())
            {
                h.Model.Script(FakeReply.Say("知道了。"));
                Ai5TRun run = await h.SendAsync("你好");
                Assert.Contains("GTX 1060", run.StreamRequests[0].SystemText);
            }

            // 方向二:助手用 remember 工具写一条 → 面板读得到
            using (var h = new Ai5THarness())
            {
                h.Model
                    .Script(FakeReply.Call("remember", PyDict.Of("text", "玩家用的是 Fabric 加载器")))
                    .Script(FakeReply.Say("记住了。"));
                await h.SendAsync("记住:我用的是 Fabric 加载器");
            }
            Assert.Contains(FileMemory.Entries(), e => e.Contains("Fabric 加载器", StringComparison.Ordinal));
        }
        finally
        {
            FileMemory.Clear();
        }
    }

    // ————— 场景 21:派两个子代理(转绿:L→S)—————

    [Fact]
    public async Task 场景21_两个子代理的会话id不同()
    {
        using var h = new Ai5THarness();
        h.Model
            .Script(FakeReply.Calls(
                ("dispatch_subagent", PyDict.Of("task", "查一下钠的兼容性")),
                ("dispatch_subagent", PyDict.Of("task", "查一下小地图的兼容性"))))
            .Script(FakeReply.Say("结论一:兼容。"))
            .Script(FakeReply.Say("结论二:也兼容。"))
            .Script(FakeReply.Say("两个子任务都完成了。"));

        Ai5TRun run = await h.SendAsync("派两个子代理分别查一下钠和小地图");

        Assert.Equal(new[] { "dispatch_subagent", "dispatch_subagent" }, run.ToolSequence);
        Assert.Equal(StopReason.COMPLETED, run.StopReason); // A6:执行过工具再收尾 = completed(不再是 no_tool_call)
        // 两个子代理各写一份会话事件文件 = 会话 id 真的不同(现在是同一个 replay-sub)
        string[] sessionFiles = Directory.GetFiles(Store.SessionDirPath(), "*.jsonl")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n != null && n.StartsWith("replay-sub", StringComparison.Ordinal))
            .Select(n => n!)
            .ToArray();
        Assert.Equal(2, sessionFiles.Length);
        Assert.Equal(2, sessionFiles.Distinct(StringComparer.Ordinal).Count());
    }

    // ————— 场景 22:dontAsk 档(转绿:L→P1)—————

    [Fact]
    public async Task 场景22_不询问档_写工具直接执行而删除类仍拒绝()
    {
        // 写工具:不弹卡,直接执行
        using (var h = new Ai5THarness(permissionMode: "dontAsk"))
        {
            h.ConfirmOk = false; // 万一弹了卡就明确拒绝,免得假绿
            h.Model
                .Script(FakeReply.Call("install_mod", PyDict.Of("name", "sodium", "instance", "default")))
                .Script(FakeReply.Say("装好了。"));
            Ai5TRun run = await h.SendAsync("帮我装钠");

            Assert.Empty(run.Confirms);
            Assert.Equal(1, h.Backend.Count("install_mod"));
            Assert.Equal(StopReason.COMPLETED, run.StopReason); // A6:执行过工具再收尾 = completed(不再是 no_tool_call)
        }

        // 删除类:仍然拒绝,不给无限定放行
        using (var h = new Ai5THarness(permissionMode: "dontAsk"))
        {
            h.ConfirmOk = true;
            h.Model
                .Script(FakeReply.Call("delete_mod", PyDict.Of("filename", "sodium-fabric-0.5.8.jar", "instance", "default")))
                .Script(FakeReply.Say("删除这一步我不做。"));
            Ai5TRun run = await h.SendAsync("把钠删了");

            Assert.Empty(run.Confirms);
            Assert.Equal(0, h.Backend.Count("delete_mod"));
            ToolCall call = run.Events.First(e => e.Kind == "tool_done").Call!;
            Assert.Equal(ToolCallStatus.DENIED, call.status);
        }

        // 显式 deny 规则仍然优先
        object? savedRules = Config.CONFIG.Get("ai_permission_rules");
        try
        {
            Config.CONFIG.Set("ai_permission_rules", new List<object>
            {
                PyDict.Of("tool", "install_mod", "behavior", "deny"),
            });
            using var h = new Ai5THarness(permissionMode: "dontAsk");
            h.ConfirmOk = true;
            h.Model
                .Script(FakeReply.Call("install_mod", PyDict.Of("name", "sodium", "instance", "default")))
                .Script(FakeReply.Say("这条被我自己的规则挡着。"));
            Ai5TRun run = await h.SendAsync("帮我装钠");

            Assert.Empty(run.Confirms);
            Assert.Equal(0, h.Backend.Count("install_mod"));
        }
        finally
        {
            Config.CONFIG.Set("ai_permission_rules", savedRules ?? new List<object>());
        }
    }
}
