// AI-engine-mcp:AI 工具调用的前后钩子。
// 来源: Windows 版参考仓库 windos/src/TipCL/Core/Launcher/Ai/{Hooks,Hook}.cs —— Register / Unregister / Clear /
// SetEnabled / RunBefore(toolName,args) / RunAfter(toolName,result)、HOOKS_DIR、内置 write_audit 审计钩子,
// 名字与语义逐项照搬:
//   · before 返回 PyDict 就用它替换参数(后面的钩子看到的是替换后的参数),返回 null 表示不改;
//   · after 返回 string 就用它替换结果,返回 null 表示不改;
//   · 任何一个钩子抛异常都**不会**让这次工具调用崩掉:异常收进 errors(「钩子名: 异常」)回传,并记一条
//     hook_error 轨迹,其余钩子照常执行。
// Linux 侧补的一处(参考版的拦截在 PluginAgentBridge.BeforeTool 里,Linux 没有那座桥):before 可以返回
// <see cref="HookBlock"/> 表示**拦下这次调用**,ToolRunner 据此把调用标成 denied 并把理由回给模型。
// 接入点:ToolRunner.RunAsync —— 在权限裁决**之前**跑 RunBefore(用户在确认卡上看到的就是改写后的参数),
// 工具执行完、结果落盘裁剪**之前**跑 RunAfter。会话循环与本机 MCP Server 共用这一个入口,所以外部客户端发起的
// 调用也过同一组钩子。
//
// 插件:Linux 侧插件系统只有文件级管理(安装/启停/日志,D-25),没有插件运行时,也没有 PluginHooks /
// LaunchHooks 这类文件级钩子机制可挂 —— 插件代码不会在本进程里跑,所以这里**不**伪造「插件钩子」,
// 只提供进程内注册口;将来有了宿主,宿主把插件回调包成 Hook 注册进来即可。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>一个钩子(照参考版 Hook):名字 + 执行前回调 + 执行后回调。</summary>
public sealed class Hook
{
    public string name;

    /// <summary>(工具名, 参数副本) → PyDict 替换参数 / <see cref="HookBlock"/> 拦下 / null 不改。</summary>
    public Func<string, PyDict, object?>? before;

    /// <summary>(工具名, 结果文本) → string 替换结果 / null 不改。</summary>
    public Func<string, string, object?>? after;

    public Hook(string name, Func<string, PyDict, object?>? before = null, Func<string, string, object?>? after = null)
    {
        this.name = name;
        this.before = before;
        this.after = after;
    }
}

/// <summary>before 钩子返回它表示「拦下这次调用」(Linux 侧补的拦截口,见 Hooks 文件头)。</summary>
public sealed class HookBlock
{
    public readonly string reason;

    public HookBlock(string reason) => this.reason = reason ?? "";
}

/// <summary>工具前后钩子注册表(照 Windows 版 Hooks)。线程安全:并行执行的只读工具会同时进来。</summary>
public static class Hooks
{
    /// <summary>审计日志目录;null 时用 数据目录/cache/ai_hooks(与参考版同一处)。</summary>
    public static string? HOOKS_DIR = null;

    private static readonly object _LOCK = new();

    private static volatile bool _enabled = true;

    private static readonly List<Hook> _hooks = new() { new Hook("write_audit", _audit_before) };

    public static void Register(Hook h)
    {
        if (h == null)
        {
            return;
        }
        lock (_LOCK)
        {
            _hooks.Add(h);
        }
    }

    public static void Unregister(string name)
    {
        lock (_LOCK)
        {
            _hooks.RemoveAll(h => h.name == name);
        }
    }

    /// <summary>清空全部钩子(含内置审计;参考版同名方法同样语义,测试用)。</summary>
    public static void Clear()
    {
        lock (_LOCK)
        {
            _hooks.Clear();
        }
    }

    /// <summary>把内置审计钩子放回去(Clear 之后恢复出厂;已在就不重复加)。</summary>
    public static void RestoreDefaults()
    {
        lock (_LOCK)
        {
            if (!_hooks.Exists(h => h.name == "write_audit"))
            {
                _hooks.Insert(0, new Hook("write_audit", _audit_before));
            }
        }
    }

    public static void SetEnabled(bool on) => _enabled = on;

    public static bool Enabled => _enabled;

    /// <summary>当前已注册的钩子名(按执行顺序)。</summary>
    public static List<string> Names()
    {
        lock (_LOCK)
        {
            return _hooks.ConvertAll(h => h.name);
        }
    }

    /// <summary>执行前钩子:返回(可能被改写的参数副本, 钩子错误)。原参数不被修改。</summary>
    public static (PyDict args, List<string> errors) RunBefore(string toolName, PyDict? args)
    {
        (PyDict a, List<string> e, _) = RunBeforeEx(toolName, args);
        return (a, e);
    }

    /// <summary>
    /// 执行前钩子(带拦截):第三项非空表示某个钩子返回了 <see cref="HookBlock"/>,调用方应拦下这次调用;
    /// 一旦被拦,后面的钩子不再执行。
    /// </summary>
    public static (PyDict args, List<string> errors, string? blocked) RunBeforeEx(string toolName, PyDict? args)
    {
        PyDict current = args != null ? new PyDict(args) : new PyDict();
        var errors = new List<string>();
        if (!_enabled)
        {
            return (current, errors, null);
        }
        List<Hook> snapshot;
        lock (_LOCK)
        {
            snapshot = new List<Hook>(_hooks);
        }
        foreach (Hook hook in snapshot)
        {
            if (hook.before == null)
            {
                continue;
            }
            try
            {
                object? r = hook.before(toolName, current);
                if (r is PyDict replaced)
                {
                    current = replaced;
                }
                else if (r is HookBlock block)
                {
                    return (current, errors, block.reason.Length > 0 ? block.reason : hook.name);
                }
            }
            catch (Exception ex)
            {
                errors.Add(hook.name + ": " + Py.ExcStr(ex));
                Trace.Record("hook_error", null, "before", toolName, ex);
            }
        }
        return (current, errors, null);
    }

    /// <summary>执行后钩子:返回(可能被改写的结果, 钩子错误)。</summary>
    public static (string result, List<string> errors) RunAfter(string toolName, string? result)
    {
        string current = result ?? "";
        var errors = new List<string>();
        if (!_enabled)
        {
            return (current, errors);
        }
        List<Hook> snapshot;
        lock (_LOCK)
        {
            snapshot = new List<Hook>(_hooks);
        }
        foreach (Hook hook in snapshot)
        {
            if (hook.after == null)
            {
                continue;
            }
            try
            {
                if (hook.after(toolName, current) is string replaced)
                {
                    current = replaced;
                }
            }
            catch (Exception ex)
            {
                errors.Add(hook.name + ": " + Py.ExcStr(ex));
                Trace.Record("hook_error", null, "after", toolName, ex);
            }
        }
        return (current, errors);
    }

    /// <summary>审计日志文件(数据目录/cache/ai_hooks/audit.jsonl,或 HOOKS_DIR 下)。</summary>
    public static string AuditFile => Path.Combine(_audit_dir(), "audit.jsonl");

    private static string _audit_dir()
    {
        string dir = HOOKS_DIR ?? Path.Combine(Utils.ROOT, "cache", "ai_hooks");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>值得审计的:写本机 / 写外部 / 删除 / 启动(与参考版 _audit_worthy 同一份判据)。</summary>
    private static bool _audit_worthy(string toolName)
    {
        ToolMetaInfo? meta = ToolMeta.Get(toolName);
        return meta != null && meta.side_effect is "write_local" or "write_external" or "delete" or "launch";
    }

    private static object? _audit_before(string toolName, PyDict args)
    {
        if (!_audit_worthy(toolName))
        {
            return null;
        }
        PyDict row = PyDict.Of(
            "ts", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            "tool", toolName,
            "args_digest", Py.Slice(PyJson.Dumps(args ?? new PyDict(), null, ensureAscii: false), null, 500));
        lock (_LOCK)
        {
            try
            {
                File.AppendAllText(AuditFile, PyJson.Dumps(row, null, ensureAscii: false) + "\n", new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // 审计写不进去不影响工具执行(参考版同样吞掉)
            }
        }
        return null;
    }
}
