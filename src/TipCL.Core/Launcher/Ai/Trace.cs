// M4-H（AI 服务层）：AI 运行时追踪日志（照 Windows 版 Launcher/Ai/Trace.cs 搬运）。
//
// 一行一条 JSON 落在 $TIPCL_HOME/cache/ai_trace/YYYYMMDD.jsonl，保留 14 天。
// 用途与参考版一致:排查「这一轮为什么没出结果」「MCP server 为什么没连上」这类问题，
// 与给用户看的对话日志（Store 的 ai_sessions）分开，不需要用户理解。
using System;
using System.IO;
using System.Reflection;
using System.Text;
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
}
