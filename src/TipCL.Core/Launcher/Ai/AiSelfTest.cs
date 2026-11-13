// M4-H P5:AI 子系统自检(移植 Windows 版 Pages/Ai/AiSelfTest.cs 的定位:
// 参考版是一个 `--ai-ui-selftest` CLI,在真机上把 AI 页跑一遍逐项检查 —— WPF 专属,Linux 侧没法照搬。
//
// Linux 版重写为**不依赖界面也不依赖联网**的功能自检:把工具链、事件链、三张卡、
// 权限档位、参数解析这些「能确定的」逐条验一遍,返回通过/失败清单。
// 需要真模型的部分(SendOneAsync)单独走 --ai-selftest --live,不联网时跳过。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>自检结果。</summary>
public sealed class AiSelfTestResult
{
    public readonly List<string> Passed = new();
    public readonly List<string> Failed = new();
    public readonly List<string> Skipped = new();

    public int Total => Passed.Count + Failed.Count + Skipped.Count;

    public bool Ok => Failed.Count == 0;

    /// <summary>给终端/日志看的一行行文本。</summary>
    public string Report()
    {
        var lines = new List<string>();
        foreach (string item in Passed)
        {
            lines.Add("  [OK]   " + item);
        }
        foreach (string item in Skipped)
        {
            lines.Add("  [SKIP] " + item);
        }
        foreach (string item in Failed)
        {
            lines.Add("  [FAIL] " + item);
        }
        lines.Add($"AI 自检:通过 {Passed.Count} / 失败 {Failed.Count} / 跳过 {Skipped.Count}");
        return string.Join("\n", lines);
    }
}

/// <summary>AI 子系统自检。</summary>
public static class AiSelfTest
{
    /// <summary>
    /// 功能自检。<paramref name="backend"/> 为空时用最小假门面(只验证「有没有调对方法」,不碰网络/磁盘)。
    /// </summary>
    public static async Task<AiSelfTestResult> VerifyAsync(ToolRunner.IBackend? backend = null)
    {
        var result = new AiSelfTestResult();
        var api = backend ?? new RecordingBackend();

        void Check(string name, bool ok, string detail = "")
            => (ok ? result.Passed : result.Failed).Add(detail.Length > 0 ? name + " — " + detail : name);

        // 1. 工具表:JSON 能解析、每个 schema 都有名字、每个名字都有元信息
        try
        {
            List<object> schemas = Tools.Schemas;
            Check("工具表可解析", schemas.Count > 0, schemas.Count + " 个工具");
            var names = Tools.Names();
            Check("工具名唯一", names.Distinct().Count() == names.Count);
            var missing = names.Where(n => ToolMeta.Get(n) == null).ToList();
            Check("每个工具都有元信息", missing.Count == 0, missing.Count > 0 ? string.Join(",", missing) : "");
            var orphan = ToolMeta.ALL.Select(m => m.name).Where(n => !names.Contains(n)).ToList();
            Check("元信息没有多余条目", orphan.Count == 0, orphan.Count > 0 ? string.Join(",", orphan) : "");
            Check("核心工具都在表里",
                AiData.CORE_TOOLS.All(n => names.Contains(n)),
                string.Join(",", AiData.CORE_TOOLS.Where(n => !names.Contains(n))));
        }
        catch (Exception ex)
        {
            Check("工具表可解析", false, Py.ExcStr(ex));
        }

        // 2. 参数解析:严格 JSON + 宽容形态
        (PyDict? strictArgs, string strictErr) = Tools.ParseArgs("{\"version\":\"1.20.1\",\"loader\":\"无\"}");
        Check("参数解析(严格 JSON)", strictArgs != null && Py.S(strictArgs.Get("version")) == "1.20.1" && strictErr.Length == 0);
        (PyDict? looseArgs, _) = Tools.ParseArgs("{version: 1.20.1, loader: 'Fabric'}");
        Check("参数解析(没写引号)", looseArgs != null && Py.S(looseArgs.Get("loader")) == "Fabric");
        (PyDict? badArgs, string badErr) = Tools.ParseArgs("[1,2,3]");
        Check("非法参数被拒", badArgs == null && badErr.Length > 0);

        // 3. 风险分级与文案
        Check("只读工具判只读", AiUiText.RiskOf("list_mods", "read") == AiRisk.Read);
        Check("删除工具判删除", AiUiText.RiskOf("delete_mod", "delete") == AiRisk.Delete);
        Check("安装工具判写入", AiUiText.RiskOf("install_mod", "write_local") == AiRisk.Write);
        Check("风险文案中文", AiUiText.RiskText(AiRisk.Delete) == "删除" && AiUiText.RiskText(AiRisk.Read) == "只读");

        // 4. 步骤行文案
        Check("状态前缀可剥", AiUiText.StripState("执行中: 装模组 钠") == "装模组 钠");
        (string label, string arg) = AiUiText.SplitLabel("执行中: 装模组: 钠");
        Check("标签参数可拆", label == "装模组" && arg == "钠", label + "|" + arg);
        Check("步骤摘要", AiUiText.StepSummary(3, 0, false, "").Contains("3 步"));
        Check("token 短显示", AiUiText.Tokens(1500) == "1.5k" && AiUiText.Tokens(999) == "999");

        // 5. 结构化选择:归一化 + 答案回填
        PyDict ask = Tools.NormalizeAskArgs(PyDict.Of("prompt", "选哪个",
            "options", new List<object> { "A", "B" }));
        List<object> questions = Py.ListOr(ask.Get("questions"));
        PyDict question = Py.D(questions[0]);
        var options = Py.ListOr(question.Get("options"));
        Check("选择题至少两项", options.Count >= 2);
        Check("选择题自动补「其他」", options.Any(o => Py.S(Py.D(o).Get("id")) == "other"));
        PyDict answer = Tools.NormalizeAskAnswer(questions,
            PyDict.Of("q1", PyDict.Of("picked", new List<object> { PyDict.Of("id", "opt_0", "label", "A") }, "other", "自定义")));
        Check("答案摘要可读", Py.S(answer.Get("summary")).Contains("A") && Py.S(answer.Get("summary")).Contains("自定义"),
            Py.S(answer.Get("summary")));

        // 6. 权限档位 → 裁决(裁决与理由一起产出,理由见 Core/AiPermissionReasonTests 的逐档断言)
        var runner = new ToolRunner(api) { PermissionMode = "default" };
        Check("默认档写操作要问", runner.Decide("install_mod").decision == "ask");
        Check("默认档写操作的理由", runner.Decide("install_mod").reason.Length > 0, runner.Decide("install_mod").reason);
        Check("只读永不需要确认", runner.Decide("list_mods").decision == "allow");
        runner.PermissionMode = "plan";
        Check("只看不动档拒写", runner.Decide("install_mod").decision == "deny");
        Check("只看不动档理由说明档位", runner.Decide("install_mod").reason.Contains("只看不动"), runner.Decide("install_mod").reason);
        runner.PermissionMode = "yolo";
        Check("全自动档直接允许", runner.Decide("delete_instance").decision == "allow");
        runner.PermissionMode = "acceptEdits";
        Check("写直接执行档:删除仍要问", runner.Decide("delete_mod").decision == "ask" && runner.Decide("install_mod").decision == "allow");

        // 7. 工具执行:只读工具直接跑 + 写工具没接确认回调时被拒绝
        runner.PermissionMode = "default";
        runner.Instance = Py.S(Config.CONFIG.Get("default_instance", "default"));
        var readCall = new ToolCall("c1", "list_instances", new PyDict());
        string readResult = await runner.RunAsync(readCall).ConfigureAwait(false);
        Check("只读工具执行成功", readCall.status == ToolCallStatus.COMPLETED && readResult.Length > 0, readCall.error);
        var writeCall = new ToolCall("c2", "install_mod", new PyDict { ["name"] = "sodium" });
        string writeResult = await runner.RunAsync(writeCall).ConfigureAwait(false);
        Check("没接确认回调时写操作被拒", writeCall.status == ToolCallStatus.DENIED && writeResult.Contains("permission_denied"));
        var unknownCall = new ToolCall("c3", "no_such_tool", new PyDict());
        await runner.RunAsync(unknownCall).ConfigureAwait(false);
        Check("未知工具如实报错", unknownCall.status == ToolCallStatus.FAILED);

        // 8. 崩溃上下文(交给 AI 修复用的开场)
        string context = AiFixChat.BuildCrashContext(PyDict.Of(
            "instance", "demo", "version", "1.20.1", "exit_code", 1L, "exit_hint", "异常退出",
            "headline", "模组冲突", "summary", "Sodium Extra 与 Sodium 不兼容", "direct_file", "/tmp/crash.txt"), "内置网关");
        Check("崩溃上下文含实例与版本", context.Contains("demo") && context.Contains("1.20.1"));
        Check("崩溃上下文含结论", context.Contains("Sodium Extra"));
        Check("崩溃上下文要求自查日志", context.Contains("get_latest_log"));
        Check("修复会话系统提示含约定", AiFixChat.FixSystemPrompt(PyDict.Of("instance", "demo")).Contains("崩溃修复"));

        // 9. 会话循环:假门面 + 假流(不打网络)—— 由 xunit 里的脚本化门面覆盖,这里只验「能构造」
        Check("会话可构造", new AiAgent(new ToolRunner(api), _ => { }) != null);

        // 10. AI 服务层:检查点 / 压缩 / 令牌与用量 / 会话存档 / 产物回读 / MCP 配置
        VerifyServices((name, ok) => Check(name, ok));

        return result;
    }

    /// <summary>
    /// 服务层自检（M4-H 新增）。会落盘的那几件（检查点 / 产物 / 会话事件）一律指到临时目录，
    /// 检完复原 —— 用户机器上的数据目录不该被一次自检弄脏。
    /// </summary>
    private static void VerifyServices(Action<string, bool> check)
    {
        string? savedCheckpoints = Checkpoint.CheckpointsDir;
        string? savedArtifacts = Artifacts.ArtifactsDir;
        string? savedTrace = Trace.TraceDir;
        string? savedSessions = Store.SessionsDir;
        string? savedStore = Store.StoreFileOverride;
        string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tipcl-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            System.IO.Directory.CreateDirectory(tmp);
            Checkpoint.CheckpointsDir = System.IO.Path.Combine(tmp, "checkpoints");
            Artifacts.ArtifactsDir = System.IO.Path.Combine(tmp, "results");
            Trace.TraceDir = System.IO.Path.Combine(tmp, "trace");
            Store.SessionsDir = System.IO.Path.Combine(tmp, "sessions");
            Store.StoreFileOverride = System.IO.Path.Combine(tmp, "ai_chats.json");
            System.IO.Directory.CreateDirectory(Checkpoint.CheckpointsDir!);
            System.IO.Directory.CreateDirectory(Artifacts.ArtifactsDir!);
            System.IO.Directory.CreateDirectory(Trace.TraceDir!);
            System.IO.Directory.CreateDirectory(Store.SessionsDir!);

            // token 估算
            check("token 估算:中文按字(" + Tokens.EstimateText("你好") + ")", Tokens.EstimateText("你好") == 3);
            check("token 估算:英文按 0.25", Tokens.EstimateText("abcd") == 2);
            var st = new TokenState();
            Tokens.UpdateFromUsage(st, PyDict.Of("prompt_tokens", 500L, "completion_tokens", 20L), 1);
            check("token 真值可校准", st.source == "provider_usage" && st.base_tokens == 500 && st.last_output_tokens == 20);

            // 用量统计
            string chatId = "selftest-" + Guid.NewGuid().ToString("N");
            Usage.Add(chatId, PyDict.Of("prompt_tokens", 100L, "completion_tokens", 5L), 0, 0);
            PyDict usageRow = Usage.Get(chatId);
            check("用量统计:真值入账", Py.Int(usageRow.Get("input")) == 100 && Py.Int(usageRow.Get("real_requests")) == 1 &&
                                   Py.S(usageRow.Get("source")) == "provider_usage");
            Usage.Reset(chatId);

            // 压缩:判定 + 整段摘要 + 微压缩
            var cfg = new AutoConfig { context_window = 8192, buffer_tokens = 8180, keep_recent_messages = 4 };
            var convo = new List<object> { PyDict.Of("role", "system", "content", "sys") };
            for (int i = 0; i < 20; i++)
            {
                convo.Add(PyDict.Of("role", "user", "content", "第 " + i + " 句" + new string('中', 60)));
                convo.Add(PyDict.Of("role", "assistant", "content", "回答 " + i));
            }
            check("压缩:超阈值会触发", Compact.ShouldAutocompact(convo, cfg, new CompactState(), null).should);
            CompactionResult compacted = Compact.CompactConversation(convo, cfg, _ => "摘要正文");
            check("压缩:保留系统消息与最近对话(" + compacted.pre_token_count + " → " + compacted.post_token_count + ")",
                Py.S(((PyDict)compacted.messages[0]).Get("role")) == "system" &&
                Py.S(((PyDict)compacted.messages[1]).Get("content")).Contains("[历史摘要]") &&
                compacted.post_token_count < compacted.pre_token_count);
            try
            {
                Compact.CompactConversation(convo, cfg, _ => "   ");
                check("压缩:空摘要被拒", false);
            }
            catch (ValueError)
            {
                check("压缩:空摘要被拒", true);
            }
            var toolHeavy = new List<object>();
            for (int i = 0; i < 8; i++)
            {
                toolHeavy.Add(PyDict.Of("role", "user", "content", "问 " + i));
                toolHeavy.Add(PyDict.Of("role", "tool", "content", "结果 " + i + new string('中', 200)));
            }
            MicroResult micro = Compact.Microcompact(toolHeavy,
                new MicroConfig { threshold_tokens = 1, keep_recent_tool_results = 2, min_token_savings = 1 }, 0);
            check("微压缩:清旧工具结果(" + micro.decision_reason + ")", micro.changed && micro.cleared_count == 6 && micro.tokens_saved > 0);

            // 检查点回滚
            string file = System.IO.Path.Combine(tmp, "work", "config.toml");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            System.IO.File.WriteAllText(file, "原始内容");
            PyDict snap = Checkpoint.Snapshot(chatId, "t1", new[] { file });
            System.IO.File.WriteAllText(file, "被改了");
            PyDict rolled = Checkpoint.Rollback(chatId, "t1");
            check("检查点:快照与回滚", Py.Truthy(snap.Get("ok")) && Py.Truthy(rolled.Get("ok")) &&
                                   System.IO.File.ReadAllText(file) == "原始内容" &&
                                   Py.Int(rolled.Get("restored_files")) == 1);

            // 会话存档 + 回退
            PyDict data = Store.Load();
            PyDict chat = Store.EnsureChat(data, chatId, "自检对话");
            chat["messages"] = new List<object>
            {
                PyDict.Of("role", "user", "content", "第一句"),
                PyDict.Of("role", "assistant", "content", "回答"),
                PyDict.Of("role", "user", "content", "第二句"),
            };
            Store.Save(data);
            Store.LogEvent(chatId, "TurnStarted", PyDict.Of("turn_id", "t2"));
            Checkpoint.Snapshot(chatId, "t2", new[] { file });
            System.IO.File.WriteAllText(file, "第二轮改的");
            PyDict rewind = Rewind.RewindLastRound(chatId);
            List<object> left = Py.ListOr(Store.GetChat(Store.Load(), chatId)!.Get("messages"));
            check("回退:对话截断 + 文件还原(剩 " + left.Count + " 条)",
                Py.Truthy(rewind.Get("truncated")) &&
                System.IO.File.ReadAllText(file) == "原始内容" &&
                left.Count == 2);

            // 产物落盘 + 回读
            string reply = Artifacts.Store(string.Join("\n", Enumerable.Range(1, 300).Select(i => "行 " + i)), "selftest");
            string artifactName = new PPath(Artifacts.Save("a\nb\nc", "selftest")).Name;
            check("产物:超长结果落盘", reply.Contains("已存文件") && reply.Contains("总行数 300"));
            check("产物:可回读(" + Artifacts.ReadArtifact(artifactName, 1L, 1L) + ")", Artifacts.ReadArtifact(artifactName, 1L, 1L).Contains("第 2-2/3 行"));
            check("产物:路径穿越被拒", Artifacts.ReadArtifact("a:b.txt") == "无效的 artifact 名");

            // MCP 配置与路由
            List<PyDict> servers = Mcp.ServerConfigs(PyDict.Of("ai_mcp_servers", new List<object>
            {
                PyDict.Of("name", "files", "command", "npx"),
                PyDict.Of("name", "非法 名字", "command", "x"),
            }));
            check("MCP:配置解析过滤非法项", servers.Count == 1 && Py.S(servers[0]["name"]) == "files");
            check("MCP:工具名识别", Mcp.IsMcpTool("mcp_files_read") && !Mcp.IsMcpTool("list_mods"));
            try
            {
                Mcp.RouteCall(new List<McpClient>(), "mcp_files_read", new PyDict());
                check("MCP:未知 server 报错", false);
            }
            catch (McpError)
            {
                check("MCP:未知 server 报错", true);
            }

            // 新工具已进表
            check("新工具已进表", Tools.Names().Contains("read_artifact") && Tools.Names().Contains("dispatch_subagent"));
            check("新工具有元信息", ToolMeta.Get("read_artifact") is { @readonly: true } && ToolMeta.Get("dispatch_subagent") is { @readonly: true });
        }
        catch (Exception ex)
        {
            check("服务层自检异常:" + Py.ExcStr(ex), false);
        }
        finally
        {
            Checkpoint.CheckpointsDir = savedCheckpoints;
            Artifacts.ArtifactsDir = savedArtifacts;
            Trace.TraceDir = savedTrace;
            Store.SessionsDir = savedSessions;
            Store.StoreFileOverride = savedStore;
            try { System.IO.Directory.Delete(tmp, true); } catch (Exception) { }
        }
    }

    /// <summary>
    /// 真模型自检:单轮非流式对话,返回模型原话。
    /// 需要已配置可用的端点,失败抛 AIClientError(调用方决定是报错还是跳过)。
    /// </summary>
    public static string SendOne(string prompt = "只回复两个字:收到", CancellationToken ct = default)
    {
        PyDict settings = Config.CONFIG.data;
        var messages = new List<object>
        {
            PyDict.Of("role", "user", "content", prompt),
        };
        PyDict reply = Client.ChatOnce(settings, messages, null!, 0.0, null!);
        return Py.S(Py.D(reply).Get("content"));
    }

    /// <summary>
    /// 记录调用的假门面(自检用):只读工具调到这里不会碰真实数据。
    /// 泛型返回值走「先按原类型转,转不过就按 JSON 反序列化」两条路 —— 调用方普遍用
    /// `CallAsync&lt;List&lt;object?&gt;&gt;`,直接 `(T?)list` 会在运行时抛 InvalidCastException。
    /// </summary>
    private sealed class RecordingBackend : ToolRunner.IBackend
    {
        public readonly List<string> Calls = new();

        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
        {
            Calls.Add(method);
            object? result = method switch
            {
                "get_instances" => new List<object> { PyDict.Of("name", "default", "versions", 1L) },
                "get_installed_versions" => new List<object> { "1.20.1" },
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
}
