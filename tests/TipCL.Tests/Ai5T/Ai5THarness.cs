// AI5-T:假模型回放测试框架 —— 测试台(假后端 + 桩 + 一轮/多轮的运行与事件收集)。
//
// 一次回放搭起来就是:harness = 假门面(ToolRunner.IBackend)+ 假模型(FakeModel,装在 Client.TestTransport)
// + 桩(confirmFn / askFn / 计划回调)+ 事件收集器。然后 harness.SendAsync("用户说的话") 跑一整个
// AiAgent.RunAsync,拿到 stop_reason、最终文本、请求序列、终态事件条数、工具调用序列。
//
// 目录隔离:检查点 / 会话事件 / 追踪 / 产物 / 钩子全部指到本测试台自己的临时目录,
// 与用户真实数据目录无关(测试程序集已经把 TIPCL_HOME 指到临时根,这里再收窄一层)。
// 用完必须 Dispose:还原这些静态量并卸掉 Client.TestTransport。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;

namespace TipCL.Tests.Ai5T;

/// <summary>一次权限确认的记录(confirmFn 收到的四样东西)。</summary>
internal sealed class ConfirmRecord
{
    public string Name = "";
    public string Label = "";
    public string Reason = "";
    public bool HasPreview;
}

/// <summary>假门面:只做「记录 + 可控返回」,不碰网络也不碰真实实例(照 AiSelfTest 的 RecordingBackend 口径)。</summary>
internal sealed class FakeBackend : ToolRunner.IBackend
{
    private readonly object _gate = new();
    private int _inflight;
    private int _taskSeq;

    /// <summary>执行过的后端方法(按调用开始顺序)。</summary>
    public readonly List<string> Methods = new();

    /// <summary>每次调用开始时的毫秒时刻(判组内并行用)。</summary>
    public readonly List<long> StartedAt = new();

    /// <summary>观测到的并发峰值(并行组执行会大于 1)。</summary>
    public int MaxConcurrency;

    /// <summary>每次后端调用的人为耗时(让并行组的重叠稳定可观测,不靠调度运气)。</summary>
    public int DelayMs;

    /// <summary>按方法名定制结果(返回 null 表示走默认值)。</summary>
    public Func<string, object?, object?>? Handle;

    public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
    {
        int inflight = Interlocked.Increment(ref _inflight);
        lock (_gate)
        {
            Methods.Add(method);
            StartedAt.Add(Environment.TickCount64);
            if (inflight > MaxConcurrency)
            {
                MaxConcurrency = inflight;
            }
        }
        try
        {
            if (DelayMs > 0)
            {
                Thread.Sleep(DelayMs);
            }
            object? result = Handle?.Invoke(method, args) ?? Default(method);
            return Task.FromResult(Convert<T>(result));
        }
        finally
        {
            Interlocked.Decrement(ref _inflight);
        }
    }

    /// <summary>某个后端方法被调用了几次。</summary>
    public int Count(string method)
    {
        lock (_gate) return Methods.Count(m => m == method);
    }

    private object? Default(string method) => method switch
    {
        "get_installed_versions" => new List<object> { "1.20.1" },
        "get_instances" => new List<object> { PyDict.Of("name", "default", "id", "default") },
        "get_installed_mod_entries" => new List<object>
        {
            PyDict.Of("filename", "sodium-fabric-0.5.8.jar", "name", "钠", "enabled", true, "size", 1024L),
        },
        "search_mods" => new List<object> { PyDict.Of("slug", "sodium", "title", "钠(Sodium)", "downloads", 90000000L) },
        "search_modpacks" => new List<object> { PyDict.Of("slug", "rlcraft", "title", "RLCraft") },
        "search_worlds" => new List<object> { PyDict.Of("slug", "world", "title", "示例存档") },
        "search_shaders" => new List<object> { PyDict.Of("slug", "bsl", "title", "BSL Shaders") },
        "search_resourcepacks" => new List<object> { PyDict.Of("slug", "faithful", "title", "Faithful") },
        "search_datapacks" => new List<object> { PyDict.Of("slug", "vanilla-tweaks", "title", "Vanilla Tweaks") },
        "get_version_list" => new List<object> { PyDict.Of("id", "1.20.1", "type", "release") },
        "is_game_running" => false,
        // install_* / download_java / launch_game 走 Start:返回任务 id 字符串
        _ => method.StartsWith("install_", StringComparison.Ordinal)
             || method is "download_java" or "launch_game"
            ? "task-" + Interlocked.Increment(ref _taskSeq)
            : new List<object>(),
    };

    /// <summary>泛型返回值:先按原类型转,转不过就 JSON 往返一次(照 AiSelfTest.RecordingBackend)。</summary>
    private static T? Convert<T>(object? result)
    {
        if (result is T typed)
        {
            return typed;
        }
        try
        {
            object? converted = PyJson.Loads(PyJson.Dumps(result!));
            return converted is T ok ? ok : default;
        }
        catch (Exception)
        {
            return default;
        }
    }
}

/// <summary>一次 SendAsync 的结果快照(断言就看它)。</summary>
internal sealed class Ai5TRun
{
    /// <summary>本轮命中的 AiAgent 实例(读 stop_reason / TurnId / LastText)。</summary>
    public AiAgent Agent = null!;

    /// <summary>本轮事件(只含本次 SendAsync 产生的)。</summary>
    public List<AiAgentEvent> Events = new();

    /// <summary>本轮的模型请求(流式与非流式都在内)。</summary>
    public List<FakeRequest> Requests = new();

    /// <summary>本轮的权限确认记录。</summary>
    public List<ConfirmRecord> Confirms = new();

    /// <summary>本轮的 ask_user 收到的问题。</summary>
    public List<PyDict> Asks = new();

    /// <summary>RunAsync 抛出的异常(取消时是 OperationCanceledException)。</summary>
    public Exception? Error;

    /// <summary>本轮结束后的对话消息(含系统消息)。</summary>
    public List<object> Messages = new();

    public string StopReason => Agent.stop_reason;

    public string LastText => Agent.LastText;

    /// <summary>工具调用序列(按 tool 事件顺序)。</summary>
    public List<string> ToolSequence => Events.Where(e => e.Kind == "tool").Select(e => e.Name).ToList();

    /// <summary>父回合**自己**的工具调用序列。AI5-S 之后子代理的工具事件会转发到父回合(带 Subagent 标记),
    /// 要单独看父回合的序列就用这个。</summary>
    public List<string> ParentToolSequence => Events.Where(e => e.Kind == "tool" && !e.Subagent).Select(e => e.Name).ToList();

    /// <summary>终态事件(done / error)条数;A1 要求任何结束路径恰好 1 条。</summary>
    public int TerminalCount => Events.Count(e => e.Kind is "done" or "error");

    public int DoneCount => Events.Count(e => e.Kind == "done");

    public int ErrorCount => Events.Count(e => e.Kind == "error");

    /// <summary>error 事件的文案(字段 Error 与 Text 都收,历史上两条路径填的不一样)。</summary>
    public List<string> ErrorTexts => Events.Where(e => e.Kind == "error")
        .Select(e => e.Error.Length > 0 ? e.Error : e.Text).ToList();

    /// <summary>流式请求(第 1 次是主请求,之后是重试)。</summary>
    public List<FakeRequest> StreamRequests => Requests.Where(r => r.Stream).ToList();

    /// <summary>非流式请求(摘要 / 兜底 / 子代理结论)。</summary>
    public List<FakeRequest> OnceRequests => Requests.Where(r => !r.Stream).ToList();

    /// <summary>最后一次流式请求下发的工具名。</summary>
    public List<string> LastStreamTools => StreamRequests.Count == 0
        ? new List<string>()
        : StreamRequests[^1].ToolNames;
}

/// <summary>回放测试台:一次回放 = 一个它。</summary>
internal sealed class Ai5THarness : IDisposable
{
    private readonly object _gate = new();
    private readonly string _dir;
    private readonly string? _savedCheckpoints;
    private readonly string? _savedSessions;
    private readonly string? _savedStore;
    private readonly string? _savedTrace;
    private readonly string? _savedArtifacts;
    private readonly string? _savedHooks;

    /// <summary>AI5-B3 之后,确认 always 会真的往 ai_permission_rules 写规则:进测试台时备份、Dispose 时还原,
    /// 免得规则泄漏到同一进程里后面的用例(假绿/假红)。</summary>
    private readonly object? _savedRules;

    /// <summary>假模型(脚本 + 请求记录)。</summary>
    public readonly FakeModel Model = new();

    /// <summary>假门面(工具执行的后端)。</summary>
    public readonly FakeBackend Backend = new();

    /// <summary>全程事件(跨轮);每轮事件看 <see cref="Ai5TRun.Events"/>。</summary>
    public readonly List<AiAgentEvent> Events = new();

    /// <summary>全程确认记录(跨轮)。</summary>
    public readonly List<ConfirmRecord> Confirms = new();

    /// <summary>全程 ask_user 问题(跨轮)。</summary>
    public readonly List<PyDict> Asks = new();

    /// <summary>计划回调收到的计划(update_plan)。</summary>
    public readonly List<List<(string Title, string Status)>> Plans = new();

    /// <summary>任务卡回调收到的任务 id。</summary>
    public readonly List<string> StartedTasks = new();

    /// <summary>对话消息(第一句时自动插系统消息,与页面口径一致)。</summary>
    public readonly List<object> History = new();

    /// <summary>工具执行器(挂在本测试台上)。</summary>
    public readonly ToolRunner Runner;

    /// <summary>会话 id(检查点与会话事件按它分组)。</summary>
    public string ChatId = "replay";

    /// <summary>会话实例名。</summary>
    public string InstanceName = "default";

    /// <summary>confirmFn 的回答(ok)。</summary>
    public bool ConfirmOk = true;

    /// <summary>confirmFn 的回答(always:本对话都允许)。</summary>
    public bool ConfirmAlways;

    /// <summary>confirmFn 的第三个回答(作用域);Linux 侧现在是 ToolRunner.AlwaysScope,见构造里的接线。</summary>
    public string ConfirmScope = ToolRunner.ScopeInstance;

    /// <summary>askFn 的回答(照 Tools.NormalizeAskAnswer 的形状)。</summary>
    public object? AskAnswer;

    public Ai5THarness(string permissionMode = "default")
    {
        _dir = Path.Combine(Path.GetTempPath(), "tipcl-ai5t-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _savedCheckpoints = Checkpoint.CheckpointsDir;
        _savedSessions = Store.SessionsDir;
        _savedStore = Store.StoreFileOverride;
        _savedTrace = Trace.TraceDir;
        _savedArtifacts = Artifacts.ArtifactsDir;
        _savedHooks = Hooks.HOOKS_DIR;
        _savedRules = Config.CONFIG.Get("ai_permission_rules");
        Checkpoint.CheckpointsDir = Path.Combine(_dir, "checkpoints");
        Store.SessionsDir = Path.Combine(_dir, "sessions");
        Store.StoreFileOverride = Path.Combine(_dir, "ai_chats.json");
        Trace.TraceDir = Path.Combine(_dir, "trace");
        Artifacts.ArtifactsDir = Path.Combine(_dir, "artifacts");
        Hooks.HOOKS_DIR = Path.Combine(_dir, "hooks");
        Directory.CreateDirectory(Checkpoint.CheckpointsDir!);
        Directory.CreateDirectory(Store.SessionsDir!);

        Runner = new ToolRunner(Backend)
        {
            PermissionMode = permissionMode,
            Instance = InstanceName,
            // AI5-B3:确认卡上选的作用域必须转给执行器 —— 不接这个回调,「本对话都允许」只在进程内
            // 生效、一个字都不会写进 ai_permission_rules,换执行器(≈重启)就不再生效。
            AlwaysScope = () => ConfirmScope,
            Confirm = (call, label, reason, preview) =>
            {
                lock (_gate)
                {
                    Confirms.Add(new ConfirmRecord
                    {
                        Name = call.name,
                        Label = label,
                        Reason = reason,
                        HasPreview = preview != null,
                    });
                }
                return Task.FromResult((ConfirmOk, ConfirmAlways));
            },
            AskUser = question =>
            {
                lock (_gate)
                {
                    Asks.Add(question);
                }
                return Task.FromResult(AskAnswer);
            },
            PlanChanged = plan =>
            {
                lock (_gate)
                {
                    Plans.Add(plan);
                }
            },
            TaskStarted = (taskId, _, _) =>
            {
                lock (_gate)
                {
                    StartedTasks.Add(taskId);
                }
            },
        };
        Model.Install();
    }

    /// <summary>本测试台的临时数据目录(检查点 / 会话事件 / 产物都在它下面)。</summary>
    public string DataDir => _dir;

    /// <summary>清掉跨轮累计的事件与记录(同一个测试台跑多组互不相干的回放时用)。</summary>
    public void Reset()
    {
        lock (_gate)
        {
            Events.Clear();
            Confirms.Clear();
            Asks.Clear();
            Plans.Clear();
            StartedTasks.Clear();
        }
        Model.Requests.Clear();
    }

    /// <summary>
    /// 发一句话,跑完一整轮 <see cref="AiAgent.RunAsync"/>。
    /// 第一句时自动在对话开头插一条系统消息(与页面一致);后续句子接着同一个对话。
    /// </summary>
    public async Task<Ai5TRun> SendAsync(string userText, int maxRounds = 0, CancellationToken ct = default)
    {
        if (History.Count == 0)
        {
            History.Add(AiAgent.SystemMessage(InstanceName));
        }
        History.Add(PyDict.Of("role", "user", "content", userText));
        return await RunTurnAsync(History, maxRounds, ct).ConfigureAwait(false);
    }

    /// <summary>直接拿一份现成的对话跑一轮(自定开头 / 继续某个对话时用)。</summary>
    public async Task<Ai5TRun> RunTurnAsync(List<object> messages, int maxRounds = 0, CancellationToken ct = default)
    {
        int eventBase;
        int requestBase;
        int confirmBase;
        int askBase;
        lock (_gate)
        {
            eventBase = Events.Count;
            requestBase = Model.Requests.Count;
            confirmBase = Confirms.Count;
            askBase = Asks.Count;
        }
        var agent = new AiAgent(Runner, ev =>
        {
            lock (_gate)
            {
                Events.Add(ev);
            }
        })
        {
            ChatId = ChatId,
        };
        if (maxRounds > 0)
        {
            agent.MaxRounds = maxRounds;
        }
        Exception? error = null;
        try
        {
            await agent.RunAsync(messages, InstanceName, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error = ex;
        }
        lock (_gate)
        {
            return new Ai5TRun
            {
                Agent = agent,
                Events = Events.Skip(eventBase).ToList(),
                Requests = Model.Requests.Skip(requestBase).ToList(),
                Confirms = Confirms.Skip(confirmBase).ToList(),
                Asks = Asks.Skip(askBase).ToList(),
                Error = error,
                Messages = messages,
            };
        }
    }

    public void Dispose()
    {
        Model.Dispose();
        Checkpoint.CheckpointsDir = _savedCheckpoints;
        Store.SessionsDir = _savedSessions;
        Store.StoreFileOverride = _savedStore;
        Trace.TraceDir = _savedTrace;
        Artifacts.ArtifactsDir = _savedArtifacts;
        Hooks.HOOKS_DIR = _savedHooks;
        Config.CONFIG.Set("ai_permission_rules", _savedRules ?? new List<object>());
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception)
        {
        }
    }
}
