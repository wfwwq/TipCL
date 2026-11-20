// AI-engine-mcp:工具前后钩子(照 Windows 版 Hooks)的回归 —— 注册/注销/开关、改写参数与结果、
// 钩子报错只进 errors 不让调用崩、ToolRunner 接线(改写的参数真的传到门面、HookBlock 拦截、审计日志)。
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

namespace TipCL.Tests.Core;

public class AiHooksTests : IDisposable
{
    private readonly List<string> _registered = new();

    public void Dispose()
    {
        foreach (string name in _registered)
        {
            Hooks.Unregister(name);
        }
        Hooks.SetEnabled(true);
        Hooks.RestoreDefaults();
    }

    private string Register(Func<string, PyDict, object?>? before = null, Func<string, string, object?>? after = null)
    {
        string name = "t-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        Hooks.Register(new Hook(name, before, after));
        _registered.Add(name);
        return name;
    }

    /// <summary>记录门面收到的方法与参数(参数按匿名对象的属性读出来)。</summary>
    private sealed class RecordingApi : ToolRunner.IBackend
    {
        public readonly List<(string Method, string Instance)> Calls = new();

        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
        {
            string instance = args?.GetType().GetProperty("instance")?.GetValue(args) as string ?? "";
            lock (Calls)
            {
                Calls.Add((method, instance));
            }
            object result = method.StartsWith("install_", StringComparison.Ordinal)
                ? PyDict.Of("task_id", "task-hook")
                : new List<object?> { method };
            return Task.FromResult(result is T t ? t : default);
        }
    }

    // ───────────────────────── 注册表本身 ─────────────────────────

    [Fact]
    public void RunBefore_RewritesArgs_OnACopy_AndChainsHooks()
    {
        Register(before: (tool, args) => tool == "list_mods" ? new PyDict(args) { ["instance"] = "rewritten" } : null);
        Register(before: (_, args) => new PyDict(args) { ["seen_by_second"] = Py.S(args.Get("instance")) });
        var original = PyDict.Of("instance", "orig");

        (PyDict args, List<string> errors) = Hooks.RunBefore("list_mods", original);

        Assert.Empty(errors);
        Assert.Equal("rewritten", Py.S(args.Get("instance")));
        Assert.Equal("rewritten", Py.S(args.Get("seen_by_second")));   // 后一个钩子看到的是前一个改过的
        Assert.Equal("orig", Py.S(original.Get("instance")));          // 原参数不被就地修改
    }

    [Fact]
    public void ThrowingHook_IsReportedInErrors_AndOtherHooksStillRun()
    {
        string bad = Register(before: (_, _) => throw new InvalidOperationException("前钩子炸了"),
                              after: (_, _) => throw new InvalidOperationException("后钩子炸了"));
        Register(before: (_, args) => new PyDict(args) { ["ok"] = true }, after: (_, r) => r + "+tail");

        (PyDict args, List<string> errors) = Hooks.RunBefore("list_mods", new PyDict());
        Assert.True(Py.Truthy(args.Get("ok")));
        string err = Assert.Single(errors);
        Assert.StartsWith(bad + ": ", err);
        Assert.Contains("前钩子炸了", err);

        (string result, List<string> afterErrors) = Hooks.RunAfter("list_mods", "body");
        Assert.Equal("body+tail", result);
        Assert.Contains("后钩子炸了", Assert.Single(afterErrors));
    }

    [Fact]
    public void Unregister_And_SetEnabled_TurnHooksOff()
    {
        string name = Register(before: (_, a) => new PyDict(a) { ["x"] = 1L }, after: (_, _) => "changed");
        Assert.Contains(name, Hooks.Names());

        Hooks.SetEnabled(false);
        Assert.False(Hooks.RunBefore("list_mods", new PyDict()).args.ContainsKey("x"));
        Assert.Equal("same", Hooks.RunAfter("list_mods", "same").result);
        Hooks.SetEnabled(true);
        Assert.Equal("changed", Hooks.RunAfter("list_mods", "same").result);

        Hooks.Unregister(name);
        Assert.DoesNotContain(name, Hooks.Names());
        Assert.Equal("same", Hooks.RunAfter("list_mods", "same").result);
    }

    [Fact]
    public void BuiltInAuditHook_IsRegisteredByDefault_AndClearRestoreWork()
    {
        Assert.Contains("write_audit", Hooks.Names());
        Hooks.Clear();
        Assert.Empty(Hooks.Names());
        Hooks.RestoreDefaults();
        Assert.Equal("write_audit", Hooks.Names().First());
    }

    // ───────────────────────── 接到 ToolRunner ─────────────────────────

    [Fact]
    public async Task ToolRunner_PassesRewrittenArgsToTheBackend_AndRewritesResult()
    {
        Register(before: (tool, args) => tool == "list_mods" ? new PyDict(args) { ["instance"] = "from-hook" } : null,
                 after: (tool, r) => tool == "list_mods" ? r + "\n[hooked]" : null);
        var api = new RecordingApi();
        var runner = new ToolRunner(api) { Instance = "session" };
        var call = new ToolCall("h1", "list_mods", PyDict.Of("instance", "model-said"));

        string result = await runner.RunAsync(call);

        Assert.Equal(ToolCallStatus.COMPLETED, call.status);
        Assert.Equal(("get_installed_mod_entries", "from-hook"), Assert.Single(api.Calls));
        Assert.EndsWith("[hooked]", result);
        Assert.Equal("from-hook", Py.S(call.args.Get("instance")));
    }

    [Fact]
    public async Task ToolRunner_HookBlock_DeniesTheCall_WithoutTouchingTheBackend()
    {
        Register(before: (tool, _) => tool == "list_mods" ? new HookBlock("插件不让查") : null);
        var api = new RecordingApi();
        var call = new ToolCall("h2", "list_mods", new PyDict());

        string result = await new ToolRunner(api).RunAsync(call);

        Assert.Equal(ToolCallStatus.DENIED, call.status);
        Assert.Contains("插件不让查", call.error);
        Assert.Contains("permission_denied", result);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public async Task ToolRunner_HookErrors_DoNotBreakTheCall_AndAreReportedToTheHost()
    {
        string bad = Register(before: (_, _) => throw new InvalidOperationException("boom-before"),
                              after: (_, _) => throw new InvalidOperationException("boom-after"));
        var reported = new List<(string Phase, string Error)>();
        var runner = new ToolRunner(new RecordingApi())
        {
            HookErrors = (_, phase, errors) => reported.AddRange(errors.Select(e => (phase, e))),
        };
        var call = new ToolCall("h3", "list_instances", new PyDict());

        string result = await runner.RunAsync(call);

        Assert.Equal(ToolCallStatus.COMPLETED, call.status);
        Assert.Contains("get_instances", result);
        Assert.Equal(2, reported.Count);
        Assert.Equal("before", reported[0].Phase);
        Assert.StartsWith(bad, reported[0].Error);
        Assert.Equal("after", reported[1].Phase);
        Assert.Contains("boom-after", reported[1].Error);
    }

    [Fact]
    public async Task AuditHook_RecordsWriteTools_ButNotReads()
    {
        string? savedDir = Hooks.HOOKS_DIR;
        string dir = Path.Combine(Path.GetTempPath(), "tipcl-hooks-" + Guid.NewGuid().ToString("N"));
        Hooks.HOOKS_DIR = dir;
        try
        {
            var runner = new ToolRunner(new RecordingApi()) { PermissionMode = "yolo", Instance = "default" };
            await runner.RunAsync(new ToolCall("r", "list_mods", new PyDict()));
            await runner.RunAsync(new ToolCall("w", "install_mod", PyDict.Of("name", "sodium", "instance", "default")));

            string[] lines = File.ReadAllLines(Hooks.AuditFile);
            PyDict row = Py.D(PyJson.Loads(Assert.Single(lines)));
            Assert.Equal("install_mod", Py.S(row.Get("tool")));
            Assert.Contains("sodium", Py.S(row.Get("args_digest")));
        }
        finally
        {
            Hooks.HOOKS_DIR = savedDir;
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    }
}
