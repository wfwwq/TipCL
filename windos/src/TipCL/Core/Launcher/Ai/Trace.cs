using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace TipCL.Core.Launcher.Ai;

public static class Trace
{
	public static string TRACE_DIR = null;

	public const int KEEP_DAYS = 14;

	/// <summary>
	/// 当前会话 id(AsyncLocal:谁在这个作用域里记轨迹,行里就带上 session 字段)。
	/// AI5-O 加的:轨迹原先只有时间戳,按会话汇总时无从下手。不设作用域时该字段省略,旧格式不变。
	/// </summary>
	private static readonly System.Threading.AsyncLocal<string> _SESSION_NOW = new System.Threading.AsyncLocal<string>();

	/// <summary>在「这本会话」的作用域里执行(AsyncLocal 传递:并行的其它会话不会串进来)。</summary>
	public static IDisposable SessionScope(string sessionId)
	{
		string previous = _SESSION_NOW.Value;
		_SESSION_NOW.Value = string.IsNullOrEmpty(sessionId) ? null : sessionId;
		return new _Scope(delegate
		{
			_SESSION_NOW.Value = previous;
		});
	}

	/// <summary>当前会话 id(没设作用域就是空串)。</summary>
	public static string CurrentSession => _SESSION_NOW.Value ?? "";

	private sealed class _Scope : IDisposable
	{
		private readonly Action _restore;

		private bool _done;

		public _Scope(Action restore)
		{
			_restore = restore;
		}

		public void Dispose()
		{
			if (!_done)
			{
				_done = true;
				_restore();
			}
		}
	}

	private static readonly object _LOCK = new object();

	private static bool _pruned;

	private static string _dir()
	{
		string obj = TRACE_DIR ?? PPath.Join(Utils.ROOT, "cache", "ai_trace");
		Utils.EnsureDir(obj);
		return obj;
	}

	private static void _prune_once()
	{
		if (_pruned)
		{
			return;
		}
		_pruned = true;
		try
		{
			DateTime dateTime = DateTime.UtcNow.AddSeconds(-1209600.0);
			foreach (string item in Directory.EnumerateFiles(Utils.AbsPath(_dir()), "*.jsonl"))
			{
				try
				{
					if (File.GetLastWriteTimeUtc(item) < dateTime)
					{
						File.Delete(item);
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

	public static string ExcType(Exception e)
	{
		if (!(e is PyException ex))
		{
			if (!(e is TargetInvocationException ex2))
			{
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
			}
			else if (ex2.InnerException != null)
			{
				return ExcType(ex2.InnerException);
			}
			return e.GetType().Name;
		}
		return ex.PyType;
	}

	public static void Record(string ev, object round_ = null, string phase = "", string tool_name = "", Exception exc = null, bool? stream_failed = null, long? text_len = null, params object[] extra)
	{
		try
		{
			PyDict pyDict = PyDict.Of("ts", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff"), "event", ev ?? "", "round", (round_ is int num) ? ((object)(long)num) : round_, "phase", phase ?? "", "tool_name", tool_name ?? "");
			// AI5-O:在会话作用域里记的轨迹带上归属,按会话汇总(诊断导出)时才能只数这一会话的行
			if (_SESSION_NOW.Value is string session && session.Length > 0)
			{
				pyDict["session"] = session;
			}
			if (exc != null)
			{
				pyDict["exc_type"] = ExcType(exc);
				pyDict["exc_msg"] = Py.Slice(Py.ExcStr(exc), null, 800);
			}
			if (stream_failed.HasValue)
			{
				pyDict["stream_failed"] = stream_failed.Value;
			}
			if (text_len.HasValue)
			{
				pyDict["text_len"] = text_len.Value;
			}
			if (extra != null)
			{
				for (int i = 0; i + 1 < extra.Length; i += 2)
				{
					pyDict[(string)extra[i]] = Py.Box(extra[i + 1]);
				}
			}
			string path = Utils.AbsPath(PPath.Join(_dir(), DateTime.Now.ToString("yyyyMMdd") + ".jsonl"));
			string text = PyJson.Dumps(pyDict, null, ensureAscii: false);
			lock (_LOCK)
			{
				_prune_once();
				File.AppendAllText(path, text + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			}
		}
		catch (Exception)
		{
		}
	}

	/// <summary>
	/// 读回最近 days 天(含今天)的轨迹行,按时间从早到晚。AI5-O 加的,只服务于会话级汇总:
	/// 坏行跳过、缺文件跳过,绝不抛(同 Record 的口径)。
	/// </summary>
	public static List<PyDict> ReadRecords(int days = 1)
	{
		List<PyDict> list = new List<PyDict>();
		try
		{
			int num = Math.Max(1, Math.Min(days, KEEP_DAYS));
			for (int num2 = num - 1; num2 >= 0; num2--)
			{
				string path = Utils.AbsPath(PPath.Join(_dir(), DateTime.Now.AddDays(-num2).ToString("yyyyMMdd") + ".jsonl"));
				if (!File.Exists(path))
				{
					continue;
				}
				string[] array;
				try
				{
					array = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetString(File.ReadAllBytes(path)).Split('\n');
				}
				catch (Exception)
				{
					continue;
				}
				foreach (string text in array)
				{
					if (text.Length == 0)
					{
						continue;
					}
					try
					{
						if (PyJson.Loads(text) is PyDict pyDict)
						{
							list.Add(pyDict);
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
		return list;
	}
}
