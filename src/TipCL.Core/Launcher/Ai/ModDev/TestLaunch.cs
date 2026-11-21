// AI2-moddev-mcp:run_test_launch —— 把实例当测试环境起一次,等到「进入主菜单」/ 崩溃 / 超时就收工。 // i18n:ignore-file(这里的中文全是回给模型 / 外部 agent 的工具结果)
//
// 流程:驱动起游戏 → 每 PollInterval 读一次游戏输出(启动任务日志 + 实例的 latest.log)→
//   · 命中 MainMenuMarkers(可配置常量,或调用方传 ready_pattern)⇒ ready,关掉游戏;
//   · 进程先退出了 ⇒ crashed(异常退出 / 崩溃报告 / 启动任务失败)或 exited(正常退出但没到主菜单);
//   · 超过 timeout_seconds(默认 180)⇒ timeout,关掉游戏。
// 收尾:日志关键片段(ERROR / Exception / Caused by / mixin 失败等 + 最后若干行)、崩溃报告摘要
// (crash-reports 里本次启动之后的最新一份,经 AiFsTools.FindLog 找,与 get_crash_report 同口径)、
// 诊断(Crash.AnalyzeInstance,与 diagnose_launch 同一个 Core 函数)。
//
// 测试里绝不真启动游戏:驱动是 ITestLaunchDriver 注入点(ModDevHost.LaunchDriverFactory),
// 单测用假驱动模拟进程与日志;默认驱动 BackendLaunchDriver 走 IApi 的 launch_game / stop_game。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>一次探测的结果(驱动给监视循环的快照)。</summary>
public sealed class TestLaunchProbe
{
    /// <summary>到目前为止的游戏输出(全文或尾部均可;监视循环自己找特征行)。</summary>
    public string Log = "";

    /// <summary>游戏进程 / 启动任务是否已经结束。</summary>
    public bool Exited;

    /// <summary>结束时是否成功(启动任务的成败;崩溃时为 false)。还没结束为 null。</summary>
    public bool? Success;

    /// <summary>结束时的说明(启动任务的状态 / 失败原因)。</summary>
    public string Message = "";
}

/// <summary>run_test_launch 的启动驱动(测试注入假的;产品默认 <see cref="BackendLaunchDriver"/>)。</summary>
public interface ITestLaunchDriver
{
    /// <summary>起游戏,返回句柄(默认驱动是启动任务的 task_id)。</summary>
    Task<string> StartAsync(string instance, string version, CancellationToken ct);

    /// <summary>读一次当前状态。</summary>
    TestLaunchProbe Probe(string handle);

    /// <summary>关掉游戏进程。</summary>
    Task StopAsync(string handle);
}

/// <summary>默认驱动:IApi 的 launch_game(返回 task_id)/ stop_game,状态读 HostTaskProbe(启动器任务表)。</summary>
public sealed class BackendLaunchDriver : ITestLaunchDriver
{
    private readonly ToolRunner.IBackend _api;

    public BackendLaunchDriver(ToolRunner.IBackend api) => _api = api;

    public async Task<string> StartAsync(string instance, string version, CancellationToken ct)
    {
        object? res = await _api.CallAsync<object>("launch_game", new { instance, version }, ct).ConfigureAwait(false);
        string id = res is string s ? s : Py.S(Py.Or(Py.D(res).Get("task_id"), Py.D(res).Get("id")));
        if (id.Length == 0)
        {
            throw new LaunchError("启动器没有返回启动任务 id");
        }
        return id;
    }

    public TestLaunchProbe Probe(string handle)
    {
        PyDict? row = ModDevHost.HostTaskProbe?.Invoke(handle);
        if (row == null)
        {
            // 没有任务表可读(无界面宿主):只能看进程在不在
            bool running = false;
            try
            {
                running = Py.Truthy(_api.CallAsync<object>("is_game_running").ConfigureAwait(false).GetAwaiter().GetResult());
            }
            catch (Exception)
            {
            }
            return new TestLaunchProbe { Exited = false, Message = running ? "running" : "" };
        }
        bool finished = Py.Truthy(row.Get("finished"));
        return new TestLaunchProbe
        {
            Log = Py.S(row.Get("log")),
            Exited = finished,
            Success = finished ? Py.Truthy(row.Get("success")) : null,
            Message = Py.S(row.Get("status")),
        };
    }

    public async Task StopAsync(string handle)
    {
        try
        {
            await _api.CallAsync<object>("stop_game").ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }
}

/// <summary>run_test_launch 的监视循环与结果整理。</summary>
public static class TestLaunch
{
    /// <summary>
    /// 「进入主菜单」的日志特征行(子串匹配,任一命中即算)。默认取 1.13+ 原版在加载完资源、显示标题画面时
    /// 打出的那行;老版本(1.12 及以前)是 OpenAL initialized。调用方可用 ready_pattern(正则)覆盖。
    /// </summary>
    public static readonly string[] MainMenuMarkers =
    {
        "Sound engine started",
        "OpenAL initialized",
    };

    /// <summary>一出现就判崩溃的特征行(进程可能还没退,先判定再关)。</summary>
    public static readonly string[] CrashMarkers =
    {
        "---- Minecraft Crash Report ----",
        "#@!@# Game crashed!",
        "Crash report saved to",
        "Exception in thread \"main\"",
        "Mixin apply failed",
    };

    /// <summary>默认超时(秒)。</summary>
    public const int DefaultTimeoutSeconds = 180;

    /// <summary>超时的允许范围(秒)。</summary>
    public const int MinTimeoutSeconds = 5;

    public const int MaxTimeoutSeconds = 1800;

    /// <summary>轮询间隔(测试调小)。</summary>
    public static TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>关游戏之后最多等它退出多久(测试调小)。</summary>
    public static TimeSpan StopGrace { get; set; } = TimeSpan.FromSeconds(15);

    private static readonly Regex KeyLineRx = new(
        @"(/ERROR\]|/FATAL\]|\bERROR\b|\bFATAL\b|Exception|Caused by:|Mixin|mixin.*fail|Missing or unsupported mandatory dependencies|Incompatible mod|requires .* but|Could not find required mod)",
        RegexOptions.CultureInvariant);

    /// <summary>挑版本:参数里给了就用;否则实例 meta 的 dev_version;否则实例里只有一个版本时用它。</summary>
    public static string PickVersion(Instance inst, string requested)
    {
        if (requested.Length > 0)
        {
            if (!inst.HasVersion(requested))
            {
                throw new ValueError("实例「" + inst.name + "」里没有版本 " + requested + ",已装: " + string.Join(", ", inst.InstalledIds().Select(Py.S)));
            }
            return requested;
        }
        string dev = Py.S(inst.Meta().Get("dev_version"));
        if (dev.Length > 0 && inst.HasVersion(dev))
        {
            return dev;
        }
        List<string> ids = inst.InstalledIds().Select(Py.S).ToList();
        if (ids.Count == 1)
        {
            return ids[0];
        }
        if (ids.Count == 0)
        {
            throw new ValueError("实例「" + inst.name + "」还没有装任何游戏版本(先 create_test_instance 或 install_game)");
        }
        throw new ValueError("实例「" + inst.name + "」装了多个版本,请用 version 指定: " + string.Join(", ", ids));
    }

    /// <summary>
    /// 跑一次测试启动(在 ModDevTasks 的后台任务里调用)。返回结果字典:outcome = ready / crashed / exited / timeout / launch_failed。
    /// </summary>
    public static async Task<PyDict> RunAsync(Instance inst, string version, int timeoutSeconds, string readyPattern,
        ITestLaunchDriver driver, ModDevTask? task, CancellationToken ct)
    {
        int timeout = Math.Clamp(timeoutSeconds <= 0 ? DefaultTimeoutSeconds : timeoutSeconds, MinTimeoutSeconds, MaxTimeoutSeconds);
        Regex? ready = null;
        if (readyPattern.Length > 0)
        {
            try
            {
                ready = new Regex(readyPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException ex)
            {
                throw new ValueError("ready_pattern 不是合法的正则: " + ex.Message);
            }
        }
        DateTime startedUtc = DateTime.UtcNow;
        var clock = Stopwatch.StartNew();
        string prevDefault = Py.S(Config.CONFIG.Get("default_instance", ""));
        task?.SetStep("启动 " + inst.name + " / " + version, 5);
        string handle;
        try
        {
            handle = await driver.StartAsync(inst.name, version, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Finish(inst, version, "launch_failed", false, clock, "", "", ex.Message, "", startedUtc, false, "");
        }
        task?.Log("启动任务: " + handle);
        task?.SetStep("等待进入主菜单(最长 " + timeout + " 秒)", 10);

        string outcome = "";
        string marker = "";
        string exitMessage = "";
        string log = "";
        bool stopped = false;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                TestLaunchProbe p = SafeProbe(driver, handle);
                log = Merge(p.Log, FileLog(inst, version, startedUtc));
                string hit = FindReady(log, ready);
                if (hit.Length > 0)
                {
                    outcome = "ready";
                    marker = hit;
                    break;
                }
                string crash = FindCrash(log);
                if (crash.Length > 0)
                {
                    outcome = "crashed";
                    marker = crash;
                    exitMessage = p.Message;
                    break;
                }
                if (p.Exited)
                {
                    exitMessage = p.Message;
                    outcome = p.Success == false || HasCrashReport(inst, version, startedUtc) ? "crashed" : "exited";
                    break;
                }
                if (clock.Elapsed.TotalSeconds >= timeout)
                {
                    outcome = "timeout";
                    break;
                }
                int pct = 10 + (int)Math.Min(80, clock.Elapsed.TotalSeconds * 80 / timeout);
                task?.SetStep("等待进入主菜单 · " + (int)clock.Elapsed.TotalSeconds + "s", pct, log: false);
                await Task.Delay(PollInterval, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            // 无论怎么结束(含取消)都把游戏关掉:测试启动不留后台进程
            TestLaunchProbe last = SafeProbe(driver, handle);
            if (!last.Exited)
            {
                task?.Log("关闭游戏进程");
                stopped = true;
                try
                {
                    await driver.StopAsync(handle).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
                var grace = Stopwatch.StartNew();
                while (grace.Elapsed < StopGrace && !SafeProbe(driver, handle).Exited)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(200, Math.Max(10, PollInterval.TotalMilliseconds))), CancellationToken.None).ConfigureAwait(false);
                }
            }
            // launch_game 会把默认实例改成被启动的实例:测试启动不该动用户的默认实例
            try
            {
                if (prevDefault.Length > 0 && Py.S(Config.CONFIG.Get("default_instance", "")) != prevDefault)
                {
                    Config.CONFIG.Set("default_instance", prevDefault);
                    Config.CONFIG.Save();
                }
            }
            catch (Exception)
            {
            }
        }
        TestLaunchProbe final = SafeProbe(driver, handle);
        log = Merge(final.Log, FileLog(inst, version, startedUtc));
        if (exitMessage.Length == 0 && final.Exited && outcome != "ready")
        {
            exitMessage = final.Message;
        }
        task?.SetStep(outcome == "ready" ? "已进入主菜单,游戏已关闭" : "结束:" + outcome, 95);
        return Finish(inst, version, outcome, outcome == "ready", clock, log, marker, exitMessage, handle, startedUtc, stopped, ready?.ToString() ?? "");
    }

    private static TestLaunchProbe SafeProbe(ITestLaunchDriver driver, string handle)
    {
        try
        {
            return driver.Probe(handle) ?? new TestLaunchProbe();
        }
        catch (Exception ex)
        {
            return new TestLaunchProbe { Message = "probe_failed: " + ex.Message };
        }
    }

    /// <summary>启动任务日志与 latest.log 拼在一起找特征(任务日志被裁时 latest.log 是完整的)。</summary>
    private static string Merge(string a, string b)
    {
        if (b.Length == 0)
        {
            return a ?? "";
        }
        if ((a ?? "").Length == 0)
        {
            return b;
        }
        return a + "\n" + b;
    }

    /// <summary>本次启动之后写过的 latest.log(更早的是上一次的,不能用来判定)。</summary>
    private static string FileLog(Instance inst, string version, DateTime startedUtc)
    {
        try
        {
            string path = AiFsTools.FindLog(inst, "latest", version);
            if (path.Length == 0 || !File.Exists(PPath.Abs(path)))
            {
                return "";
            }
            if (File.GetLastWriteTimeUtc(PPath.Abs(path)) < startedUtc.AddSeconds(-2))
            {
                return "";
            }
            return Crash.ReadText(path, 2_000_000);
        }
        catch (Exception)
        {
            return "";
        }
    }

    public static string FindReady(string log, Regex? ready)
    {
        if (ready != null)
        {
            Match m = ready.Match(log ?? "");
            return m.Success ? m.Value : "";
        }
        foreach (string mk in MainMenuMarkers)
        {
            if ((log ?? "").Contains(mk, StringComparison.Ordinal))
            {
                return mk;
            }
        }
        return "";
    }

    public static string FindCrash(string log)
    {
        foreach (string mk in CrashMarkers)
        {
            if ((log ?? "").Contains(mk, StringComparison.Ordinal))
            {
                return mk;
            }
        }
        return "";
    }

    private static string NewCrashReport(Instance inst, string version, DateTime startedUtc)
    {
        try
        {
            string path = AiFsTools.FindLog(inst, "crash", version);
            if (path.Length > 0 && File.Exists(PPath.Abs(path)) && File.GetLastWriteTimeUtc(PPath.Abs(path)) >= startedUtc.AddSeconds(-2))
            {
                return path;
            }
        }
        catch (Exception)
        {
        }
        return "";
    }

    private static bool HasCrashReport(Instance inst, string version, DateTime startedUtc) => NewCrashReport(inst, version, startedUtc).Length > 0;

    /// <summary>日志关键片段:命中关键行的(带前后各 1 行,最多 30 处)+ 最后 20 行。</summary>
    public static List<string> KeyLines(string log, int maxHits = 30, int tail = 20)
    {
        List<string> lines = (log ?? "").Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).ToList();
        var keep = new SortedSet<int>();
        int hits = 0;
        for (int i = 0; i < lines.Count && hits < maxHits; i++)
        {
            if (KeyLineRx.IsMatch(lines[i]) || MainMenuMarkers.Any(m => lines[i].Contains(m, StringComparison.Ordinal)))
            {
                hits++;
                for (int j = Math.Max(0, i - 1); j <= Math.Min(lines.Count - 1, i + 1); j++)
                {
                    keep.Add(j);
                }
            }
        }
        for (int i = Math.Max(0, lines.Count - tail); i < lines.Count; i++)
        {
            keep.Add(i);
        }
        var result = new List<string>();
        int prev = -2;
        foreach (int i in keep)
        {
            if (prev >= 0 && i != prev + 1)
            {
                result.Add("...");
            }
            string l = lines[i];
            result.Add(l.Length > 400 ? l.Substring(0, 400) + "…" : l);
            prev = i;
        }
        return result;
    }

    /// <summary>崩溃报告摘要:Description / 第一行异常 / 可疑模组段落的前几行。</summary>
    public static PyDict? CrashSummary(string path)
    {
        if (path.Length == 0)
        {
            return null;
        }
        string text;
        try
        {
            text = Crash.ReadText(path, 400_000);
        }
        catch (Exception)
        {
            return null;
        }
        List<string> lines = text.Replace("\r", "").Split('\n').ToList();
        string description = lines.FirstOrDefault(l => l.StartsWith("Description:", StringComparison.Ordinal))?.Substring("Description:".Length).Trim() ?? "";
        int descAt = lines.FindIndex(l => l.StartsWith("Description:", StringComparison.Ordinal));
        string exception = "";
        if (descAt >= 0)
        {
            exception = lines.Skip(descAt + 1).FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
        }
        var suspects = new List<object>();
        int sus = lines.FindIndex(l => l.Contains("Suspected Mod", StringComparison.OrdinalIgnoreCase));
        if (sus >= 0)
        {
            foreach (string l in lines.Skip(sus).Take(6))
            {
                if (l.Trim().Length > 0)
                {
                    suspects.Add(l.Trim());
                }
            }
        }
        return PyDict.Of(
            "file", Path.GetFileName(path),
            "path", path,
            "description", description,
            "exception", exception.Length > 300 ? exception.Substring(0, 300) + "…" : exception,
            "suspected_mods", suspects,
            "head", lines.Take(40).Cast<object>().ToList());
    }

    private static PyDict Finish(Instance inst, string version, string outcome, bool success, Stopwatch clock, string log, string marker,
        string exitMessage, string handle, DateTime startedUtc, bool stopped, string readyPattern)
    {
        var result = PyDict.Of(
            "ok", success,
            "outcome", outcome,
            "success", success,
            "instance", inst.name,
            "version", version,
            "elapsed_s", Math.Round(clock.Elapsed.TotalSeconds, 1),
            "launch_task_id", handle,
            "marker", marker,
            "ready_markers", readyPattern.Length > 0 ? new List<object> { readyPattern } : MainMenuMarkers.Cast<object>().ToList(),
            "game_stopped_by_tipcl", stopped,
            "log_excerpt", KeyLines(log).Cast<object>().ToList());
        if (exitMessage.Length > 0)
        {
            result["exit_message"] = exitMessage;
        }
        result["message"] = outcome switch
        {
            "ready" => "游戏已进入主菜单,测试通过;TipCL 已关闭游戏进程",
            "crashed" => "游戏崩溃或异常退出,看 crash / diagnosis / log_excerpt 定位",
            "exited" => "游戏在进入主菜单之前退出了(没有崩溃报告)",
            "timeout" => "超时还没进入主菜单;TipCL 已关闭游戏进程。可以加大 timeout_seconds,或用 ready_pattern 指定主菜单特征行",
            _ => "没能启动: " + exitMessage,
        };
        if (!success)
        {
            string crashPath = NewCrashReport(inst, version, startedUtc);
            if (CrashSummary(crashPath) is { } summary)
            {
                result["crash"] = summary;
            }
            try
            {
                PyDict report = Crash.AnalyzeInstance(inst, "", true, version);
                result["diagnosis"] = PyDict.Of(
                    "headline", Py.S(report.Get("headline")),
                    "summary", Py.S(report.Get("summary")),
                    "reasons", Py.ListOr(report.Get("reasons")).Take(6).Select(r => (object)PyDict.Of(
                        "code", Py.S(Py.D(r).Get("code")),
                        "title", Py.S(Py.D(r).Get("title")),
                        "extra", Py.S(Py.D(r).Get("extra")))).ToList());
            }
            catch (Exception ex)
            {
                result["diagnosis_error"] = ex.Message;
            }
        }
        return result;
    }
}
