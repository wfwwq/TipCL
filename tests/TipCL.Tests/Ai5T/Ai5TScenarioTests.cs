// AI5-T:22 条场景表里「现状应当就是绿的」那一批(文件一)。
//
// 表来源:`windos/docs/改造工程:2611/AI-agent-合并任务书.md` §7 的 AI5-T 场景表(中立只读共享目录
// `C:\tipcl-ai5\_shared\AI-agent-合并任务书.md` 同名)。场景编号与表一一对应,测试名写在前面。
//
// 每条都按表的要求断言五样东西里适用的那几样:stop_reason、最终文本、请求序列(含 tools 集合与 model)、
// 终态事件条数、工具调用序列 —— 不是只看「有没有报错」。
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

/// <summary>真机交互密集(改全局静态量、装/卸假传输):挂串行集合,不与别的类并行。</summary>
[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class Ai5TScenarioTests
{
    // ————— 场景 1:纯文字回复 → no_tool_call,只有 1 条 done —————

    [Fact]
    public async Task 场景01_纯文字回复_只有一条done()
    {
        using var h = new Ai5THarness();
        h.Model.Script(FakeReply.Say("好的,我先帮你看看。").Usage(120));

        Ai5TRun run = await h.SendAsync("你好");

        Assert.Equal(StopReason.NO_TOOL_CALL, run.StopReason);
        Assert.Equal("好的,我先帮你看看。", run.LastText);
        Assert.Equal(1, run.DoneCount);
        Assert.Equal(1, run.TerminalCount);
        Assert.Equal(0, run.ErrorCount);
        Assert.Empty(run.ToolSequence);

        // 请求序列:就一次流式请求,模型名来自 ResolveEndpoint,工具表来自按需选择
        Assert.Single(run.Requests);
        FakeRequest req = run.StreamRequests[0];
        Assert.Equal(Defaults.DEFAULT_MODEL, req.Model);
        Assert.Equal(Tools.SelectToolSchemas(req.Messages).Count, req.ToolNames.Count);
        // 「你好」既没命中有词工具、也不是下命令:这一批写工具不该出现
        Assert.False(req.HasTool("fs_write"));
        Assert.False(req.HasTool("tune_jvm"));
        Assert.True(req.HasTool("list_mods"));

        // provider 给了 usage,页面才有占用环
        Assert.Contains(run.Events, e => e.Kind == "usage");
    }

    // ————— 场景 2:单个只读工具 → 文字 —————

    /// <summary>
    /// 表里场景 2 的期望是 `completed`。A6 落地之前 Linux 的 `StopReason.COMPLETED` 从来没被赋过值
    /// (那时给的是 `no_tool_call`),A6 之后「执行过工具再收尾」就是 `completed`,与表一致。
    /// </summary>
    [Fact]
    public async Task 场景02_单个只读工具后纯文字_结束原因是completed()
    {
        using var h = new Ai5THarness();
        h.Model
            .Script(FakeReply.Call("list_mods", PyDict.Of("instance", "default")).Usage(200))
            .Script(FakeReply.Say("你装了 1 个模组:钠。").Usage(260));

        Ai5TRun run = await h.SendAsync("看看我装了哪些模组");

        Assert.Equal(StopReason.COMPLETED, run.StopReason);
        Assert.Equal("你装了 1 个模组:钠。", run.LastText);
        Assert.Equal(new[] { "list_mods" }, run.ToolSequence);
        Assert.Equal(2, run.StreamRequests.Count);
        Assert.Equal(1, run.TerminalCount);
        Assert.Equal(1, run.DoneCount);
        // 第二轮请求里带着第一条工具结果:模型能看见工具真的跑了什么
        Assert.Contains(run.StreamRequests[1].ToolResults, t => t.Contains("sodium", StringComparison.Ordinal));
    }

    /// <summary>
    /// 场景 2 的同形状回归(原先是「Linux 现状是 no_tool_call」的现状钉,A6 落地后口径已对齐,
    /// 这里跟着改成断言 A6 的规则本身):**执行过工具**再收尾 = `completed`,不是 `no_tool_call`。
    /// 与上面那条的分工:上面是任务书场景 2 的原样,这条钉 A6 的判据(有没有跑过工具)。
    /// </summary>
    [Fact]
    public async Task 场景02补_执行过工具的回合收尾是completed()
    {
        using var h = new Ai5THarness();
        h.Model
            .Script(FakeReply.Call("list_mods", PyDict.Of("instance", "default")))
            .Script(FakeReply.Say("你装了 1 个模组:钠。"));

        Ai5TRun run = await h.SendAsync("看看我装了哪些模组");

        Assert.Equal(StopReason.COMPLETED, run.StopReason); // A6:执行过工具再收尾 = completed(不再是 no_tool_call)
        Assert.Equal(new[] { "list_mods" }, run.ToolSequence);
        Assert.Equal(1, run.DoneCount);
    }

    // ————— 场景 3:一轮多个工具,部分并行 —————

    [Fact]
    public async Task 场景03_一轮多工具_分组符合Scheduler且结果顺序正确()
    {
        using var h = new Ai5THarness();
        h.Backend.DelayMs = 60; // 让并行组的重叠稳定可观测(不依赖调度运气)

        h.Model
            .Script(FakeReply.Calls(
                ("list_mods", PyDict.Of("instance", "default")),
                ("search_mods", PyDict.Of("query", "sodium")),
                ("list_installed_versions", PyDict.Of("instance", "default")),
                ("install_mod", PyDict.Of("name", "sodium", "instance", "default")),
                ("list_mods", PyDict.Of("instance", "default"))))
            .Script(FakeReply.Say("钠装好了,你原本装了 1 个模组。"));

        Ai5TRun run = await h.SendAsync("帮我把钠装上,顺便看看装了哪些模组");

        var expected = new[] { "list_mods", "search_mods", "list_installed_versions", "install_mod", "list_mods" };
        Assert.Equal(expected, run.ToolSequence);
        Assert.Equal(StopReason.COMPLETED, run.StopReason); // A6:执行过工具再收尾 = completed(不再是 no_tool_call)
        Assert.Equal(1, run.DoneCount);

        // 分组:前三个只读相邻可并行 → 一组;写工具自己一组;最后一个只读又一组
        var calls = expected
            .Select((name, i) => new ToolCall("c" + i, name, new PyDict()))
            .ToList();
        List<List<ToolCall>> groups = Scheduler.Group(calls);
        Assert.Equal(new[] { 3, 1, 1 }, groups.Select(g => g.Count).ToArray());
        Assert.True(h.Backend.MaxConcurrency >= 2,
            "组内 3 个只读调用应当真的并行,观测到的并发峰值 = " + h.Backend.MaxConcurrency);

        // 组内并行的三个都在 install_mod 之前起跑(没有偷偷串成一条线)
        Assert.Equal(5, h.Backend.Methods.Count);
        int installAt = h.Backend.Methods.IndexOf("install_mod");
        Assert.Equal(3, installAt);
        Assert.True(h.Backend.StartedAt[installAt] - h.Backend.StartedAt[0] >= 30,
            "前三个只读调用应当同组并行(间隔 " + (h.Backend.StartedAt[installAt] - h.Backend.StartedAt[0]) + " ms)");
        Assert.True(h.Backend.StartedAt[4] > h.Backend.StartedAt[installAt]);

        // 结果顺序:回填进对话的 tool 消息与 tool_call 顺序一一对应
        List<string> results = run.StreamRequests[1].ToolResults;
        Assert.Equal(5, results.Count);
        Assert.Contains("sodium", results[0]);        // list_mods
        Assert.Contains("sodium", results[1]);        // search_mods
        Assert.Contains("1.20.1", results[2]);        // list_installed_versions
        Assert.Contains("task-", results[3]);         // install_mod 的任务卡
        Assert.Contains("sodium", results[4]);        // 最后一个 list_mods
    }

    // ————— 场景 4:写工具,用户拒绝 —————

    [Fact]
    public async Task 场景04_写工具被拒_不落盘且轮次继续()
    {
        using var h = new Ai5THarness();
        h.ConfirmOk = false;

        h.Model
            .Script(FakeReply.Call("install_mod", PyDict.Of("name", "sodium", "instance", "default")))
            .Script(FakeReply.Say("好的,那我不装了。"));

        Ai5TRun run = await h.SendAsync("帮我把钠装上");

        // 轮次继续:模型收到了拒绝结果,并且接着把话说完了
        Assert.Equal(StopReason.COMPLETED, run.StopReason); // A6:执行过工具再收尾 = completed(不再是 no_tool_call)
        Assert.Equal("好的,那我不装了。", run.LastText);
        Assert.Equal(2, run.StreamRequests.Count);
        Assert.Equal(1, run.DoneCount);
        Assert.Equal(0, run.ErrorCount);

        // 模型收到的是权限拒绝,不是别的错
        List<string> results = run.StreamRequests[1].ToolResults;
        Assert.Single(results);
        Assert.Contains("permission_denied", results[0]);

        // 没有落盘:后端一次 install_mod 都没被调到
        Assert.Equal(0, h.Backend.Count("install_mod"));

        // 确认卡弹了一次,带得出「为什么需要你确认」的理由
        Assert.Single(run.Confirms);
        Assert.Equal("install_mod", run.Confirms[0].Name);
        Assert.True(run.Confirms[0].Reason.Length > 0);
        Assert.True(run.Confirms[0].Label.Length > 0);

        // 工具状态是 denied(界面靠它显示「已拒绝」),并且带得出理由 ——
        // 只验「有没有」不验具体措辞:文案归 i18n / 词表那几个包管,回放测试不该被措辞变动打红
        ToolCall call = run.Events.First(e => e.Kind == "tool_done").Call!;
        Assert.Equal(ToolCallStatus.DENIED, call.status);
        Assert.True(call.error.Length > 0, "被拒的步骤行必须带得出理由");
    }

    // ————— 场景 6:update_plan 之后计划未获批准 —————

    /// <summary>
    /// 表里场景 6 的树标记是「W(L 🕓)」,期望是「没有执行任何写操作,也没有生成检查点」。
    /// 核实结论(写进 progress/AI5-T.md):Linux 侧**没有**计划审批流程 —— 全仓 grep
    /// `plan_approval` / `计划审批` 只命中词表里的一句文案;Linux 的等价保证是「plan 档下写工具一律 deny」
    /// (ToolRunner.DecideRaw 的第一条硬停),所以本条按 Linux 的等价口径断言,转绿包 P2 只做 Windows。
    /// </summary>
    [Fact]
    public async Task 场景06_计划档_写操作一律不执行()
    {
        using var h = new Ai5THarness(permissionMode: "plan");

        h.Model
            .Script(FakeReply.Call("update_plan", PyDict.Of("items", new List<object>
            {
                PyDict.Of("title", "装钠", "status", "pending"),
                PyDict.Of("title", "验证启动", "status", "pending"),
            })))
            .Script(FakeReply.Call("install_mod", PyDict.Of("name", "sodium", "instance", "default")))
            .Script(FakeReply.Say("这一步没获批,我先不动你的游戏目录。"));

        Ai5TRun run = await h.SendAsync("帮我装个钠,先给我看计划");

        // 计划本身记下来了(update_plan 回调)
        Assert.Single(h.Plans);
        Assert.Equal(2, h.Plans[0].Count);

        // 写操作没有执行
        Assert.Equal(0, h.Backend.Count("install_mod"));
        ToolCall install = run.Events
            .Where(e => e.Kind == "tool_done" && e.Name == "install_mod")
            .Select(e => e.Call!)
            .Single();
        Assert.Equal(ToolCallStatus.DENIED, install.status);
        Assert.True(install.error.Length > 0, "被拒的步骤行必须带得出理由");
        // 模型收到的是权限拒绝(错误码是结构字段,不是易变的措辞)
        Assert.Contains(run.StreamRequests[^1].ToolResults, t => t.Contains("permission_denied", StringComparison.Ordinal));

        // 计划档下没有确认卡
        Assert.Empty(run.Confirms);
    }

    /// <summary>
    /// 场景 6 的另一半:期望里写着「也没有生成检查点」。**这一半 Linux 不满足**(任务书没覆盖这条,
    /// 已按红线 5 记进 progress/AI5-T.md 的「超出任务书的发现」一节):
    /// AiAgent.ExecuteOneAsync 是先 `SnapshotForTool` 再 `_runner.RunAsync` 判权限的,
    /// 于是被拒的写工具也已经在 journal 里留下了一条操作 —— 「撤回上一轮」会因此多出一个空回合。
    /// 本包不改生产代码(除 Client.cs 的注入点),所以这里挂起;没有对应的转绿包号,建议并进 A1 或单独立包。
    /// </summary>
    [Fact]
    public async Task 场景06补_被拒的写工具不该留下检查点()
    {
        using var h = new Ai5THarness(permissionMode: "plan");

        h.Model
            .Script(FakeReply.Call("install_mod", PyDict.Of("name", "sodium", "instance", "default")))
            .Script(FakeReply.Say("这一步没获批。"));

        Ai5TRun run = await h.SendAsync("帮我装个钠");

        Assert.False(Checkpoint.TurnHasOps(h.ChatId, run.Agent.TurnId),
            "本轮没有写操作落盘,不该留下检查点;journal = " + Journal(h));
    }

    /// <summary>检查点 journal 的原文(断言失败时当证据用)。</summary>
    private static string Journal(Ai5THarness h)
    {
        string path = Path.Combine(h.DataDir, "checkpoints", h.ChatId, "journal.json");
        return File.Exists(path) ? File.ReadAllText(path) : "(没有 journal)";
    }

    // ————— 场景 13:中途取消 —————

    [Fact]
    public async Task 场景13_工具执行中途取消_结束原因是cancelled()
    {
        using var h = new Ai5THarness();
        using var cts = new CancellationTokenSource();
        // 在工具执行期间取消:list_mods 是单调用组,执行体就在会话线程上
        h.Backend.Handle = (method, _) =>
        {
            if (method == "get_installed_mod_entries")
            {
                cts.Cancel();
            }
            return null;
        };
        h.Model
            .Script(FakeReply.Call("list_mods", PyDict.Of("instance", "default")))
            .Script(FakeReply.Say("这句话不该出现"));

        Ai5TRun run = await h.SendAsync("看看我装了哪些模组", ct: cts.Token);

        Assert.Equal(StopReason.CANCELLED, run.StopReason);
        Assert.IsAssignableFrom<OperationCanceledException>(run.Error);
        // 取消不是「报错」:不能再给一条 done,也别把半截回复当结论
        Assert.Equal(0, run.DoneCount);
        Assert.Single(run.Requests);
    }

    /// <summary>
    /// 场景 13 的另一半:取消落在**流式过程中**(请求已经发出、响应还在消费时点停止)。
    /// A1 之前这条路会发 2 条 error、`stop_reason` 记成 `stream_failed`;A1 收敛终态事件之后,
    /// 用户主动取消就是 `cancelled`(本条原先钉的是 A1 落地前的现状,现在改成断言新口径)。
    /// </summary>
    [Fact]
    public async Task 场景13补_流式过程中取消_记成cancelled()
    {
        using var h = new Ai5THarness();
        using var cts = new CancellationTokenSource();
        h.Model.OnRequest = _ => cts.Cancel();
        h.Model.Script(FakeReply.Say("还没吐出来就停了"));

        Ai5TRun run = await h.SendAsync("在吗", ct: cts.Token);

        Assert.Equal(StopReason.CANCELLED, run.StopReason); // A1:取消的回合记 cancelled(不再是 stream_failed)
        Assert.Equal(0, run.DoneCount);
        // A1/A2 之后不再重复报错:内部代号也不该出现在给用户看的文案里
        Assert.DoesNotContain(run.ErrorTexts, t => t.Contains("stream_failed", StringComparison.Ordinal));
        Assert.True(run.TerminalCount <= 1, "取消至多留一条终态事件,实测 " + run.TerminalCount + " 条");
    }

    // ————— 场景 16:「给 1.20.1 Fabric 装上钠和小地图」—————

    [Fact]
    public async Task 场景16_装模组流程_search到ask再到install两次()
    {
        using var h = new Ai5THarness();
        h.ConfirmOk = true;
        h.AskAnswer = PyDict.Of("q1", PyDict.Of(
            "picked", new List<object> { PyDict.Of("id", "opt_0", "label", "都装") },
            "other", ""));

        h.Model
            .Script(FakeReply.Call("search_mods", PyDict.Of("query", "钠")))
            .Script(FakeReply.Call("ask_user", PyDict.Of(
                "prompt", "两个都装吗?",
                "options", new List<object> { "都装", "只要钠" })))
            .Script(FakeReply.Calls(
                ("install_mod", PyDict.Of("name", "sodium", "slug", "sodium", "instance", "default")),
                ("install_mod", PyDict.Of("name", "小地图", "slug", "journeymap", "instance", "default"))))
            .Script(FakeReply.Say("钠和小地图都开始装了,可以在「下载任务」里看进度。"));

        Ai5TRun run = await h.SendAsync("给 1.20.1 Fabric 装上钠和小地图");

        Assert.Equal(new[] { "search_mods", "ask_user", "install_mod", "install_mod" }, run.ToolSequence);
        Assert.Equal(StopReason.COMPLETED, run.StopReason); // A6:执行过工具再收尾 = completed(不再是 no_tool_call)
        Assert.Equal(1, run.DoneCount);
        Assert.Equal(0, run.ErrorCount);

        // 第一轮就下发了这条流程要用的三样工具(关键词路由 + 常驻工具)
        FakeRequest first = run.StreamRequests[0];
        Assert.True(first.HasTool("search_mods"));
        Assert.True(first.HasTool("ask_user"));
        Assert.True(first.HasTool("install_mod"));

        // ask_user 收到的是归一化后的题面,并且答案回填进了工具结果
        Assert.Single(run.Asks);
        Assert.NotEmpty(Py.ListOr(run.Asks[0].Get("questions")));
        string askResult = run.StreamRequests[2].ToolResults
            .Single(t => t.Contains("\"answers\"", StringComparison.Ordinal));
        Assert.Contains("opt_0", askResult);
        // 工具结果里的中文是按 \uXXXX 转义的:解出来再看,别拿裸 JSON 找中文
        // C2(AI5):回给模型的工具结果前面多了一行来源前缀(如「[来源: ...]」),解析前先跳到第一个 '{'
        Assert.StartsWith("[来源:", askResult);
        Assert.Contains("都装", PyJson.Dumps(PyJson.Loads(askResult.Substring(askResult.IndexOf('{'))), null, ensureAscii: false));

        // 两次安装都真的发起了任务
        Assert.Equal(2, h.Backend.Count("install_mod"));
        Assert.Equal(2, h.StartedTasks.Count);
    }

    // ————— 场景 17:「游戏闪退了」—————

    [Fact]
    public async Task 场景17_闪退流程_日志到诊断()
    {
        using var h = new Ai5THarness();
        h.Model
            .Script(FakeReply.Calls(
                ("get_crash_report", PyDict.Of("instance", "default")),
                ("get_latest_log", PyDict.Of("instance", "default"))))
            .Script(FakeReply.Call("diagnose_launch", PyDict.Of("instance", "default")))
            .Script(FakeReply.Say("看起来是 Java 版本不对,建议换成 17。"));
        Ai5TRun run = await h.SendAsync("游戏闪退了");

        Assert.Equal(new[] { "get_crash_report", "get_latest_log", "diagnose_launch" }, run.ToolSequence);
        Assert.Equal(StopReason.COMPLETED, run.StopReason); // A6:执行过工具再收尾 = completed(不再是 no_tool_call)
        Assert.Equal("看起来是 Java 版本不对,建议换成 17。", run.LastText);
        Assert.Equal(1, run.DoneCount);
        Assert.Equal(0, run.ErrorCount);

        // 这三样都在第一轮就下发了(全是常驻工具)
        foreach (string name in new[] { "get_crash_report", "get_latest_log", "diagnose_launch" })
        {
            Assert.True(run.StreamRequests[0].HasTool(name), name + " 应当在下发的工具表里");
        }
        // 只读流程不弹确认卡
        Assert.Empty(run.Confirms);
    }

    // ————— 场景 18:「好卡」—————

    [Fact]
    public async Task 场景18_卡顿流程_系统信息到诊断再到调JVM()
    {
        using var h = new Ai5THarness();
        h.ConfirmOk = true;
        h.Model
            .Script(FakeReply.Call("get_system_info", new PyDict()))
            .Script(FakeReply.Call("diagnose_lag", PyDict.Of("instance", "default")))
            .Script(FakeReply.Call("tune_jvm", PyDict.Of("instance", "default", "memory_mb", 4096L)))
            .Script(FakeReply.Say("已经把内存调到 4G 了,启动一次试试。"));

        Ai5TRun run = await h.SendAsync("好卡");

        Assert.Equal(new[] { "get_system_info", "diagnose_lag", "tune_jvm" }, run.ToolSequence);
        Assert.Equal(StopReason.COMPLETED, run.StopReason); // A6:执行过工具再收尾 = completed(不再是 no_tool_call)
        Assert.Equal("已经把内存调到 4G 了,启动一次试试。", run.LastText);
        Assert.Equal(1, run.DoneCount);
        Assert.Equal(0, run.ErrorCount);

        // 「好卡」命中了卡顿关键词,三样工具都在第一轮下发
        foreach (string name in new[] { "get_system_info", "diagnose_lag", "tune_jvm" })
        {
            Assert.True(run.StreamRequests[0].HasTool(name), name + " 应当在下发的工具表里");
        }
        // tune_jvm 是写工具:默认档要确认一次,确认之后才真的执行
        Assert.Single(run.Confirms);
        Assert.Equal("tune_jvm", run.Confirms[0].Name);
        // tune_jvm 直接读写真机配置(不走 IApi 门面),所以这里看工具状态而不是后端调用次数:
        // 确认过了就不该是「被拒」
        ToolCall tune = run.Events
            .Where(e => e.Kind == "tool_done" && e.Name == "tune_jvm")
            .Select(e => e.Call!)
            .Single();
        Assert.NotEqual(ToolCallStatus.DENIED, tune.status);
    }

    // ————— 兜底:假模型脚本没用完 = 少写了脚本,别当通过 —————
}
