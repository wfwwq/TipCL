using System;
using System.IO;
using TipCL.Core;
using TipCL.Core.Launcher;

namespace TipCL.App.Common;

/// <summary>
/// GUI 进程文件日志:<c>$TIPCL_HOME/logs/tipcl-YYYYMMDD.log</c>(默认 <c>~/.tipcl/logs</c>)。
/// 真机用 <c>nohup ./tipcl</c> 启动时 stdout 常为空,出问题只能靠这个文件事后排障。
/// 同时镜像到 Console;未初始化或创建失败时静默降级,不影响启动。
/// </summary>
public static class AppLog
{
    private static readonly object _lock = new();
    private static StreamWriter? _writer;
    private static string? _file;

    /// <summary>「桌面缺少 AppMenu 服务」这条已知噪声只记一次(见 IsBenignDesktopNoise)。</summary>
    private static bool _desktopNoiseLogged;

    /// <summary>日志文件路径(未初始化或打开失败时为 null)。</summary>
    public static string? FilePath => _file;

    /// <summary>初始化并接管全局未处理异常。可重复调用。</summary>
    public static void Init()
    {
        if (_writer != null) return;
        lock (_lock)
        {
            try
            {
                string dir = Path.Combine(Utils.ROOT, "logs");
                Directory.CreateDirectory(dir);
                _file = Path.Combine(dir, $"tipcl-{DateTime.Now:yyyyMMdd}.log");
                var stream = new FileStream(_file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(stream, System.Text.Encoding.UTF8) { AutoFlush = true };
            }
            catch (Exception)
            {
                _writer = null;
                _file = null;
            }
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Error("unhandled(AppDomain)", e.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            if (IsBenignDesktopNoise(e.Exception))
            {
                // 已知无害的桌面集成噪声:只留**一条** WARN 备查(每开一个窗口都记一条会淹掉日志),
                // 之后同类直接忽略。**不吞**信息 —— 那条 WARN 里带着完整原因。
                if (!_desktopNoiseLogged)
                {
                    _desktopNoiseLogged = true;
                    Warn("unhandled(Task) 已知无害(桌面缺少 AppMenu 服务,后续同类不再记):" + Flatten(e.Exception));
                }
            }
            else
            {
                Error("unhandled(Task)", e.Exception);
            }
            e.SetObserved();
        };

        Info($"=== TipCL {CoreInfo.Version} 启动 pid={Environment.ProcessId} os={Environment.OSVersion} ===");
        Info($"home={SafeRoot()} cwd={Environment.CurrentDirectory}");
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
    {
        Write("ERROR", message);
        if (ex != null) Write("ERROR", ex.ToString());
    }

    private static void Write(string level, string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (_lock)
        {
            try { _writer?.WriteLine(line); } catch (Exception) { /* 磁盘满/权限变化时不再追加 */ }
        }
        try { Console.WriteLine(line); } catch (Exception) { /* 无控制台时忽略 */ }
    }

    private static string SafeRoot()
    {
        try { return Utils.ROOT; } catch (Exception) { return "?"; }
    }

    /// <summary>
    /// 已知无害的「桌面集成噪声」判定:盒子(XFCE)上没有 <c>com.canonical.AppMenu.Registrar</c> 这个
    /// DBus 服务,而 Avalonia 的 X11/FreeDesktop 集成**每开一个新窗口**都会去注册它 —— 于是每开一次
    /// 浮层/对话框就落一条「无主任务异常」(R21b 实测:一次会话 24 条,全是同一条消息)。它不动进程、
    /// 不影响功能,却把「0 unhandled」这个信号淹掉了。
    ///
    /// 判定口径**收得很紧**:必须是 <c>AggregateException</c> 包里**每一个**内层异常都是
    /// Tmds.DBus 的 DBusException,并且消息里带 <c>ServiceUnknown</c> 与
    /// <c>com.canonical.AppMenu.Registrar</c>。其余任何未观察异常照旧按 ERROR 记。
    /// 类型用<strong>字符串比较</strong>而不是引用 Tmds.DBus 类型:那个包是 Avalonia 的传递依赖,
    /// 不该为了让日志降噪而在 Core/App 里硬绑一个我们并不直接使用的类型。
    /// </summary>
    internal static bool IsBenignDesktopNoise(Exception? ex)
    {
        if (ex is not AggregateException agg || agg.InnerExceptions.Count == 0)
        {
            return false;
        }
        foreach (Exception inner in agg.InnerExceptions)
        {
            if (inner.GetType().FullName != "Tmds.DBus.Protocol.DBusException")
            {
                return false;
            }
            string text = Flatten(inner);
            if (!text.Contains("ServiceUnknown", StringComparison.Ordinal)
                || !text.Contains("com.canonical.AppMenu.Registrar", StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>异常(含内层)压成一行文本,给日志与人看。</summary>
    private static string Flatten(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        Exception? cur = ex;
        while (cur != null)
        {
            if (sb.Length > 0) sb.Append(" ---> ");
            sb.Append(cur.GetType().FullName).Append(": ").Append(cur.Message);
            cur = cur.InnerException;
        }
        return sb.ToString();
    }
}

/// <summary>Avalonia 日志汇入 AppLog(替代默认 Trace,让渲染/绑定警告也落文件)。</summary>
public sealed class AppLogSink : Avalonia.Logging.ILogSink
{
    // Avalonia 的 Info 主要是布局/绑定噪声(--smoke 一次就上千行),只收 Warning 以上
    public bool IsEnabled(Avalonia.Logging.LogEventLevel level, string area) => level >= Avalonia.Logging.LogEventLevel.Warning;

    public void Log(Avalonia.Logging.LogEventLevel level, string area, object? source, string messageTemplate)
        => Write(level, area, Render(messageTemplate, Array.Empty<object?>()));

    public void Log(Avalonia.Logging.LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
        => Write(level, area, Render(messageTemplate, propertyValues));

    private static void Write(Avalonia.Logging.LogEventLevel level, string area, string message)
    {
        AppLog.Warn($"[avalonia/{area}] {message}");
    }

    /// <summary>
    /// Avalonia 的模板是**具名**占位符(`An error occurred binding {Property} to {Expression}…`),
    /// `string.Format` 只认位置参数,对具名会抛异常并回落到原模板 → 日志里只剩
    /// 「{Property} to {Expression}」这种没法查的占位符。这里按出现顺序逐个填入 propertyValues。
    /// </summary>
    private static string Render(string template, object?[] values)
    {
        if (string.IsNullOrEmpty(template))
            return template ?? "";
        if (values.Length == 0)
            return template;
        var sb = new System.Text.StringBuilder(template.Length + 64);
        int vi = 0;
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if (c == '{' && i + 1 < template.Length && template[i + 1] == '{')
            {
                sb.Append('{');
                i++;
                continue;
            }
            if (c == '{')
            {
                int close = template.IndexOf('}', i + 1);
                if (close > i)
                {
                    sb.Append(vi < values.Length ? values[vi]?.ToString() ?? "null" : template.Substring(i, close - i + 1));
                    vi++;
                    i = close;
                    continue;
                }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}