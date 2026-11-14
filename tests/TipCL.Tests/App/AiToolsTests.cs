using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TipCL.App.Common;
using TipCL.App.Pages.Ai;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>
/// M4-H P5:AI 子系统(工具表 / 参数解析 / 权限分级 / 三张卡 / 崩溃修复上下文 / 自检)。
/// 这些用例都不联网:工具执行走假门面,事件链走 AiAgent 的公开回调。
/// </summary>
public class AiToolsTests
{
    /// <summary>记录调用的假门面:只读工具调到这里不碰真实数据。</summary>
    private sealed class FakeApi : ToolRunner.IBackend
    {
        public readonly List<(string Method, object? Args)> Calls = new();

        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
        {
            Calls.Add((method, args));
            object? result = method switch
            {
                "get_instances" => new List<object> { PyDict.Of("name", "default", "versions", 1L) },
                "get_installed_versions" => new List<object> { "1.20.1" },
                "get_installed_mod_entries" => new List<object> { PyDict.Of("filename", "sodium-0.5.8.jar", "enabled", true) },
                "get_java_list" => new List<object>(),
                "install_mod" => PyDict.Of("task_id", "task-fake"),
                "install_game" => PyDict.Of("task_id", "task-fake"),
                "download_java" => PyDict.Of("task_id", "task-fake"),
                "launch_game" => PyDict.Of("task_id", "task-fake"),
                "create_instance" => PyDict.Of("ok", true),
                "delete_instance" => PyDict.Of("ok", true),
                "delete_mod" => PyDict.Of("ok", true),
                "disable_mod" => PyDict.Of("ok", true),
                "enable_mod" => PyDict.Of("ok", true),
                _ => new List<object>(),
            };
            if (result is T typed)
            {
                return Task.FromResult<T?>(typed);
            }
            try
            {
                object? converted = PyJson.Loads(PyJson.Dumps(result));
                return Task.FromResult(converted is T ok ? ok : default);
            }
            catch (Exception)
            {
                return Task.FromResult<T?>(default);
            }
        }
    }

    [Fact]
    public async Task SelfTest_AllChecksPass()
    {
        AiSelfTestResult result = await AiSelfTest.VerifyAsync(new FakeApi());
        Assert.True(result.Ok, result.Report());
        Assert.True(result.Passed.Count >= 20, result.Report());
        Assert.Empty(result.Failed);
    }

    [Fact]
    public void ToolSchemas_AreValid_AndBackedByMeta()
    {
        List<object> schemas = Tools.Schemas;
        Assert.NotEmpty(schemas);
        var names = Tools.Names();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.All(names, n => Assert.NotNull(ToolMeta.Get(n)));
        // 关键工具必须在(与 Windows 版同名)
        foreach (string required in new[] { "ask_user", "get_launcher_state", "install_mod", "delete_mod", "launch_game", "update_plan" })
        {
            Assert.Contains(required, names);
        }
        // 风险分级:写工具非只读
        Assert.False(ToolMeta.Get("install_mod")!.@readonly);
        Assert.True(ToolMeta.Get("list_mods")!.@readonly);
    }

    [Fact]
    public void SelectToolSchemas_AddsKeywordTools()
    {
        var messages = new List<object> { PyDict.Of("role", "user", "content", "帮我装个光影包") };
        var picked = Tools.SelectToolSchemas(messages).Select(Tools.SchemaName).ToList();
        Assert.Contains("search_content", picked);
        Assert.Contains("install_shader", picked);
        Assert.Contains("get_launcher_state", picked); // 常驻工具

        var ask = Tools.SelectToolSchemas(new List<object> { PyDict.Of("role", "user", "content", "怎么改内存?") })
            .Select(Tools.SchemaName).ToList();
        Assert.DoesNotContain("delete_instance", ask); // 纯提问不带写工具
    }

    [Fact]
    public async Task ReadOnlyTool_Runs_WriteTool_NeedsConfirm()
    {
        var api = new FakeApi();
        var runner = new ToolRunner(api) { PermissionMode = "default", Instance = "default" };

        var read = new ToolCall("c1", "list_mods", new PyDict { ["instance"] = "default" });
        await runner.RunAsync(read);
        Assert.Equal(ToolCallStatus.COMPLETED, read.status);
        Assert.Contains("sodium-0.5.8.jar", read.result);

        var write = new ToolCall("c2", "delete_mod", new PyDict { ["filename"] = "sodium-0.5.8.jar" });
        await runner.RunAsync(write);
        Assert.Equal(ToolCallStatus.DENIED, write.status);
        Assert.DoesNotContain(api.Calls, c => c.Method == "delete_mod");

        // 接了确认回调且用户点了「本对话都允许」:同一个工具第二次不再问
        int asked = 0;
        runner.Confirm = (_, _, _, _) =>
        {
            asked++;
            return Task.FromResult((true, true));
        };
        var write2 = new ToolCall("c3", "delete_mod", new PyDict { ["filename"] = "sodium-0.5.8.jar" });
        await runner.RunAsync(write2);
        Assert.Equal(ToolCallStatus.COMPLETED, write2.status);
        Assert.Equal(1, asked);
        var write3 = new ToolCall("c4", "delete_mod", new PyDict { ["filename"] = "sodium-0.5.8.jar" });
        await runner.RunAsync(write3);
        Assert.Equal(ToolCallStatus.COMPLETED, write3.status);
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task PermissionModes_DriveDecisions()
    {
        var runner = new ToolRunner(new FakeApi()) { Instance = "default" };

        runner.PermissionMode = "plan";
        var write = new ToolCall("p1", "install_mod", new PyDict { ["name"] = "sodium" });
        await runner.RunAsync(write);
        Assert.Equal(ToolCallStatus.DENIED, write.status);
        // R19b 起被拒的步骤行带出裁决理由(照 Windows 的 denied 工具状态),不再是泛泛的「不允许」
        Assert.Contains("已拒绝", write.error);
        Assert.Contains("只看不动", write.error);

        runner.PermissionMode = "yolo";
        var write2 = new ToolCall("p2", "install_mod", new PyDict { ["name"] = "sodium", ["instance"] = "default" });
        await runner.RunAsync(write2);
        Assert.Equal(ToolCallStatus.COMPLETED, write2.status);
        Assert.Contains("task-fake", write2.result);

        // 自定义规则覆盖档位
        string? before = Config.CONFIG.Get("ai_permission_rules") as string;
        try
        {
            Config.CONFIG.Set("ai_permission_rules", new List<object>
            {
                PyDict.Of("tool", "install_mod", "behavior", "deny"),
            });
            runner.PermissionMode = "yolo";
            Assert.Equal("deny", runner.Decide("install_mod").decision);
            Assert.Equal("allow", runner.Decide("install_game").decision);
        }
        finally
        {
            Config.CONFIG.Set("ai_permission_rules", (object?)before ?? new List<object>());
        }
    }

    [Fact]
    public async Task TaskStarted_Callback_ReportsTaskId()
    {
        var api = new FakeApi();
        var runner = new ToolRunner(api) { PermissionMode = "yolo", Instance = "default" };
        string? taskId = null;
        string? tool = null;
        runner.TaskStarted = (id, _, name) => { taskId = id; tool = name; };
        var call = new ToolCall("t1", "install_game", new PyDict { ["version"] = "1.20.1", ["loader"] = "无", ["instance"] = "default" });
        await runner.RunAsync(call);
        Assert.Equal("task-fake", taskId);
        // 任务卡要靠工具名决定显示哪个后续动作,不能传空串
        Assert.Equal("install_game", tool);
        PyDict started = Py.D(PyJson.Loads(call.result));
        Assert.Contains("下载任务", Py.S(started.Get("message")));
    }

    [Fact]
    public async Task PlanTool_EmitsPlanAndCounts()
    {
        var runner = new ToolRunner(new FakeApi()) { PermissionMode = "yolo", Instance = "default" };
        List<(string Title, string Status)>? plan = null;
        runner.PlanChanged = p => plan = p;
        var call = new ToolCall("pl1", "update_plan", new PyDict
        {
            ["items"] = new List<object>
            {
                PyDict.Of("title", "读日志", "status", "completed"),
                PyDict.Of("title", "禁用冲突模组", "status", "in_progress"),
            },
        });
        await runner.RunAsync(call);
        Assert.NotNull(plan);
        Assert.Equal(2, plan!.Count);
        PyDict result = Py.D(PyJson.Loads(call.result));
        Assert.Equal(1L, Py.Int(result.Get("done")));
        Assert.Equal(2L, Py.Int(result.Get("total")));
    }

    [Fact]
    public void AskUser_NormalizeAndAnswer()
    {
        PyDict normalized = Tools.NormalizeAskArgs(PyDict.Of(
            "title", "选一个实例", "prompt", "装到哪?",
            "options", new List<object> { "默认实例", "新建实例" }));
        List<object> questions = Py.ListOr(normalized.Get("questions"));
        Assert.Single(questions);
        PyDict question = Py.D(questions[0]);
        Assert.Equal("选一个实例", Py.S(normalized.Get("title")));
        Assert.Equal("装到哪?", Py.S(question.Get("prompt")));
        var options = Py.ListOr(question.Get("options")).Select(Py.D).ToList();
        Assert.Equal(3, options.Count); // 两项 + 自动补的「其他」
        Assert.Contains(options, o => Py.S(o.Get("id")) == "other");

        PyDict answer = Tools.NormalizeAskAnswer(questions, PyDict.Of(
            "q1", PyDict.Of("picked", new List<object> { PyDict.Of("id", "opt_0", "label", "默认实例") }, "other", "就叫生存")));
        Assert.Contains("默认实例", Py.S(answer.Get("summary")));
        Assert.Contains("就叫生存", Py.S(answer.Get("summary")));
        // 什么都没选 → 未选
        PyDict empty = Tools.NormalizeAskAnswer(questions, PyDict.Of("q1", PyDict.Of("picked", new List<object>())));
        Assert.Contains("(未选)", Py.S(empty.Get("summary")));

        // 选项不足两项时补「先不选」
        PyDict one = Tools.NormalizeAskArgs(PyDict.Of("prompt", "只有一个选项", "options", new List<object> { "A" }));
        var oneOptions = Py.ListOr(Py.D(Py.ListOr(one.Get("questions"))[0]).Get("options")).Select(Py.D).ToList();
        Assert.DoesNotContain(oneOptions, o => Py.S(o.Get("id")) == "skip");
        Assert.Contains(oneOptions, o => Py.S(o.Get("id")) == "other");
    }

    [Fact]
    public void AiUiText_Helpers()
    {
        Assert.Equal("装模组 钠", AiUiText.StripState("执行中: 装模组 钠"));
        // 全角冒号也是合法的状态前缀(模型与工具结果里两种都会出现)
        Assert.Equal("装模组 钠", AiUiText.SplitLabel("执行中：装模组 钠").Label);
        // 前缀剥掉后剩下「安装 sodium」:没有第二个冒号 → 整串当标签
        Assert.Equal("安装 sodium", AiUiText.SplitLabel("完成: 安装 sodium").Label);
        // 「标签: 参数」结构照旧要拆开
        (string label, string arg) = AiUiText.SplitLabel("search_mods: 钠");
        Assert.Equal("search_mods", label);
        Assert.Equal("钠", arg);
        Assert.True(AiUiText.IsMcp("mcp__demo__tool"));
        Assert.False(AiUiText.IsMcp("install_mod"));
        Assert.Equal(AiRisk.Delete, AiUiText.RiskOf("remove_mod", ""));
        Assert.Equal("1.5k", AiUiText.Tokens(1500));
        Assert.Equal(1.0, AiUiText.ContextRatio(1000, 1, 800));
        Assert.Equal('-', AiUiText.DiffKind("- 老的一行"));
        Assert.Equal('+', AiUiText.DiffKind("+ 新的一行"));
        Assert.True(AiUiText.LooksLikeDiff(new List<string> { "路过的行", "-删掉", "+加上" }));
        (int done, int total) = AiUiText.PlanProgress(new List<(string, string)> { ("a", "completed"), ("b", "pending") });
        Assert.Equal(1, done);
        Assert.Equal(2, total);
    }

    [Fact]
    public void CrashContext_ContainsWhatTheModelNeeds()
    {
        var report = PyDict.Of(
            "instance", "demo", "version", "1.20.1", "exit_code", 1L, "exit_hint", "异常退出",
            "headline", "模组冲突", "summary", "Sodium Extra 与 Sodium 不兼容",
            "detail", "java.lang.NoSuchMethodError", "direct_file", "/tmp/crash.txt", "task_id", "task-1");
        string context = AiFixChat.BuildCrashContext(report, "内置网关");
        Assert.Contains("demo", context);
        Assert.Contains("1.20.1", context);
        Assert.Contains("Sodium Extra", context);
        Assert.Contains("get_latest_log", context);
        Assert.Contains("接口正常可用", context);
        Assert.Contains("自己读日志", context);

        string prompt = AiFixChat.FixSystemPrompt(report);
        Assert.Contains("崩溃修复", prompt);
        Assert.Contains("禁用优先于删除", prompt);

        PyDict opening = AiFixChat.OpeningMessage(report, "内置网关");
        Assert.Equal("user", Py.S(opening.Get("role")));
        Assert.Contains("为什么失败", Py.S(opening.Get("content")));
    }

    [Fact]
    public void Agent_SystemMessage_CarriesIdentityAndContext()
    {
        PyDict system = AiAgent.SystemMessage("demo", "1.20.1");
        string content = Py.S(system.Get("content"));
        Assert.Equal("system", Py.S(system.Get("role")));
        Assert.Contains("TipCL 启动器里的游戏助手", content);
        Assert.Contains("demo", content);
        Assert.DoesNotContain("__LANG_RULE__", content); // 占位符必须被替换
    }

    [Fact]
    public void Cards_Construct_WithoutAWindow()
    {
        // 卡片是纯控件,构造不应依赖窗口(冒烟里由页面渲染)
        TestApp.Run(() =>
        {
            var plan = new PlanCard(new List<(string, string)> { ("读日志", "completed"), ("禁模组", "in_progress") });
            Assert.Equal(1, plan.Done);
            Assert.Equal(2, plan.Total);

            var confirm = new ConfirmCard("delete_mod", "删除模组 sodium.jar", "不可恢复",
                new List<string> { "- sodium.jar" }, allowAlways: false, (_, _, _) => { });
            Assert.Equal(AiRisk.Delete, confirm.Risk);
            Assert.False(confirm.Answered);
            Assert.False(ConfirmCard.AllowAlways("delete_mod"));
            Assert.True(ConfirmCard.AllowAlways("install_mod"));

            var ask = new AskCard("选实例", Tools.NormalizeAskArgs(PyDict.Of("prompt", "装哪?",
                "options", new List<object> { "默认", "新建" })).Get("questions") as List<object> ?? new List<object>(),
                _ => { });
            Assert.NotNull(ask);

            var task = new TaskCard("task-1", "安装 1.20.1", "install_game", "default");
            Assert.Equal("task-1", task.TaskId);
            task.SetProgress(50, 100, "下载 client.jar  |  3 MB/s");
            task.Finish(true, "完成");
            Assert.True(task.Succeeded);

            var line = new ToolLine("执行中: 装模组 钠", "install_mod");
            Assert.Equal(ToolState.Prepare, line.State);
            Assert.Equal("装模组 钠", AiUiText.SplitLabel(line.LabelText).Label);
            line.SetState(ToolState.Done);
            line.SetResult("完成");
            Assert.Equal(ToolState.Done, line.State);
        });
    }

    [Fact]
    public void AiPermissionPanel_ExposesFiveModes()
    {
        Assert.Equal(5, AiPermissionPanel.Modes.Length);
        Assert.Equal(5, AiPermissionPanel.ModeLabels.Length);
        Assert.Contains("plan", AiPermissionPanel.Modes);
        Assert.Contains("yolo", AiPermissionPanel.Modes);
    }

    [Fact]
    public void AiPage_Constructs_AndSetsTitle()
    {
        TestApp.Run(() =>
        {
            var page = new AiPage();
            Assert.Equal("ai", page.PageId);
            Assert.Equal("AI 助手", page.PageTitle);
            Assert.NotNull(page.Content);
        });
    }

    [Fact]
    public void ImageInput_AddsAndLimits()
    {
        TestApp.Run(() =>
        {
            var images = new AiImageInput((_, _, _) => { });
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tipcl-aitest-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tmp);
            try
            {
                var files = new List<string>();
                for (int i = 0; i < 5; i++)
                {
                    string p = System.IO.Path.Combine(tmp, "shot" + i + ".png");
                    System.IO.File.WriteAllBytes(p, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
                    files.Add(p);
                }
                Assert.True(images.Add(files[0]));
                Assert.False(images.Add(files[0]));            // 去重
                Assert.False(images.Add(System.IO.Path.Combine(tmp, "readme.txt"))); // 扩展名不符
                for (int i = 1; i < 4; i++) Assert.True(images.Add(files[i]));
                Assert.Equal(4, images.Count);
                Assert.False(images.Add(files[4]));            // 上限
                Assert.Contains("4 张图", images.ChipText);
                images.Clear();
                Assert.Equal(0, images.Count);
            }
            finally
            {
                try { System.IO.Directory.Delete(tmp, true); } catch (Exception) { }
            }
        });
    }
}
