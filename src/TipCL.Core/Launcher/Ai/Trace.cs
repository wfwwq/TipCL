// M4-H（AI 服务层）：AI 运行时追踪日志（照 Windows 版 Launcher/Ai/Trace.cs 搬运）。
//
// 一行一条 JSON 落在 $TIPCL_HOME/cache/ai_trace/YYYYMMDD.jsonl，保留 14 天。
// 用途与参考版一致:排查「这一轮为什么没出结果」「MCP server 为什么没连上」这类问题，
// 与给用户看的对话日志（Store 的 ai_sessions）分开，不需要用户理解。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>AI 运行轨迹（只在排查问题时看）。</summary>
public static class Trace
{
    /// <summary>测试可指向临时目录；空 = 用默认位置。</summary>
    public static string? TraceDir;

    public const int KeepDays = 14;

    private static readonly object Lock = new();

    private static bool _pruned;

    /// <summary>
    /// 当前会话 id(AsyncLocal:谁在这个作用域里记轨迹,行里就带上 <c>session</c> 字段)。
    /// AI5-O 加的:轨迹原先只有时间戳,按会话汇总时无从下手。
    /// 不设作用域时该字段省略 —— 旧日志格式与既有调用点行为完全不变。
    /// </summary>
    private static readonly AsyncLocal<string?> SessionNow = new();

    /// <summary>在「这本会话」的作用域里执行(AsyncLocal 传递:并行的其它会话不会串进来)。</summary>
    public static IDisposable SessionScope(string? sessionId)
    {
        string? previous = SessionNow.Value;
        SessionNow.Value = string.IsNullOrEmpty(sessionId) ? null : sessionId;
        return new Scope(() => SessionNow.Value = previous);
    }

    /// <summary>当前会话 id(没设作用域就是空串)。</summary>
    public static string CurrentSession => SessionNow.Value ?? "";

    private sealed class Scope : IDisposable
    {
        private readonly Action _restore;

        private bool _done;

        public Scope(Action restore) => _restore = restore;

        public void Dispose()
        {
            if (!_done)
            {
                _done = true;
                _restore();
            }
        }
    }

    private static string Dir()
    {
        string dir = TraceDir ?? PPath.Join(Utils.ROOT, "cache", "ai_trace");
        Utils.EnsureDir(dir);
        return dir;
    }

    private static void PruneOnce()
    {
        if (_pruned)
        {
            return;
        }
        _pruned = true;
        try
        {
            DateTime cutoff = DateTime.UtcNow.AddSeconds(-86400.0 * KeepDays);
            foreach (string f in Directory.EnumerateFiles(Utils.AbsPath(Dir()), "*.jsonl"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(f) < cutoff)
                    {
                        File.Delete(f);
                    }
                }
                catch (Exception)
                {
                }
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>异常 → Python 风格的异常类名（照参考版；日志里用名字比用 .NET 类型更短）。</summary>
    public static string ExcType(Exception e)
    {
        if (e is PyException py)
        {
            return py.PyType;
        }
        if (e is TargetInvocationException tie && tie.InnerException != null)
        {
            return ExcType(tie.InnerException);
        }
        if (e is FileNotFoundException || e is DirectoryNotFoundException)
        {
            return "FileNotFoundError";
        }
        if (e is UnauthorizedAccessException)
        {
            return "PermissionError";
        }
        if (e is IOException)
        {
            return "OSError";
        }
        if (e is NotImplementedException)
        {
            return "NotImplementedError";
        }
        if (e is OperationCanceledException)
        {
            return "CancelledError";
        }
        if (e is ArgumentException || e is FormatException)
        {
            return "ValueError";
        }
        if (e is InvalidCastException || e is NullReferenceException)
        {
            return "TypeError";
        }
        return e.GetType().Name;
    }

    /// <summary>记一条轨迹（任何异常都吞掉：追踪日志本身绝不能拖垮对话）。</summary>
    public static void Record(
        string ev,
        object? round = null,
        string phase = "",
        string tool_name = "",
        Exception? exc = null,
        bool? stream_failed = null,
        long? text_len = null,
        params object[]? extra)
    {
        try
        {
            var row = PyDict.Of(
                "ts", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff"),
                "event", ev ?? "",
                "round", round is int n ? (long)n : round,
                "phase", phase ?? "",
                "tool_name", tool_name ?? "");
            // AI5-O:在会话作用域里记的轨迹带上归属,按会话汇总时就能只数这一会话的行
            if (SessionNow.Value is string session && session.Length > 0)
            {
                row["session"] = session;
            }
            if (exc != null)
            {
                row["exc_type"] = ExcType(exc);
                row["exc_msg"] = Py.Slice(Py.ExcStr(exc), null, 800);
            }
            if (stream_failed.HasValue)
            {
                row["stream_failed"] = stream_failed.Value;
            }
            if (text_len.HasValue)
            {
                row["text_len"] = text_len.Value;
            }
            if (extra != null)
            {
                for (int i = 0; i + 1 < extra.Length; i += 2)
                {
                    row[Py.Str(extra[i])] = Py.Box(extra[i + 1]);
                }
            }
            string path = Utils.AbsPath(PPath.Join(Dir(), DateTime.Now.ToString("yyyyMMdd") + ".jsonl"));
            string line = PyJson.Dumps(row, null, ensureAscii: false);
            lock (Lock)
            {
                PruneOnce();
                File.AppendAllText(path, line + "\n", new UTF8Encoding(false));
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// 读回最近 <paramref name="days"/> 天(含今天)的轨迹行,按时间从早到晚。
    /// AI5-O 加的,只服务于会话级汇总:坏行跳过、读不到的日期跳过,绝不抛(同 <see cref="Record"/> 的口径)。
    /// </summary>
    public static List<PyDict> ReadRecords(int days = 1)
    {
        var rows = new List<PyDict>();
        try
        {
            int span = Math.Max(1, Math.Min(days, KeepDays));
            for (int i = span - 1; i >= 0; i--)
            {
                string path = Utils.AbsPath(PPath.Join(Dir(), DateTime.Now.AddDays(-i).ToString("yyyyMMdd") + ".jsonl"));
                if (!File.Exists(path))
                {
                    continue;
                }
                string[] lines;
                try
                {
                    lines = new UTF8Encoding(false, false).GetString(File.ReadAllBytes(path)).Split('\n');
                }
                catch (Exception)
                {
                    continue;
                }
                foreach (string line in lines)
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }
                    try
                    {
                        if (PyJson.Loads(line) is PyDict row)
                        {
                            rows.Add(row);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }
        catch (Exception)
        {
        }
        return rows;
    }
}
