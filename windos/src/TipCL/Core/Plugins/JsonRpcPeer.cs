using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TipCL.Core.Plugins;

/// <summary>宿主 API 返回给 Node 的错误（error.code / error.message）。</summary>
public sealed class PluginRpcException : Exception
{
	public string Code { get; }

	public object Data2 { get; }

	public PluginRpcException(string code, string message, object data = null)
		: base(message)
	{
		Code = code ?? "error";
		Data2 = data;
	}

	public static PluginRpcException Denied(string plugin, string permission)
	{
		return new PluginRpcException("permission_denied", "插件 " + plugin + " 没有权限 " + permission);
	}
}

/// <summary>
/// 换行分隔 JSON（NDJSON）编解码。一行一个 JSON 值，UTF-8，不带 BOM；字符串里的换行在 JSON 里本来就会被转义。
/// </summary>
public static class Ndjson
{
	public const int MaxLineBytes = 64 * 1024 * 1024;

	private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

	/// <summary>紧凑 JSON（无多余空格）+ '\n'。</summary>
	public static byte[] Encode(object value)
	{
		return Utf8.GetBytes(EncodeLine(value) + "\n");
	}

	public static string EncodeLine(object value)
	{
		string s = PyJson.Dumps(value, null, ensureAscii: false, sortKeys: false, ",", ":");
		// JSON 里本来不会出现裸换行；保险起见再查一次，防止一行被拆成两行
		if (s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0)
		{
			s = s.Replace("\r", "\\r").Replace("\n", "\\n");
		}
		return s;
	}

	public static object Decode(string line)
	{
		line = line.Trim();
		if (line.Length > 0 && line[0] == '\ufeff')
		{
			line = line.Substring(1);
		}
		return PyJson.Loads(line);
	}

	/// <summary>
	/// 从字节流里切行。按字节切（'\n' 不会出现在 UTF-8 多字节字符中间），再整体解码，避免多字节字符被截断。
	/// 流结束时返回 null。
	/// </summary>
	public sealed class LineReader
	{
		private readonly Stream _s;

		private readonly byte[] _buf = new byte[65536];

		private int _len;

		private int _pos;

		private readonly MemoryStream _line = new MemoryStream();

		public LineReader(Stream s)
		{
			_s = s;
		}

		public string ReadLine()
		{
			_line.SetLength(0);
			while (true)
			{
				if (_pos >= _len)
				{
					_len = _s.Read(_buf, 0, _buf.Length);
					_pos = 0;
					if (_len <= 0)
					{
						if (_line.Length > 0)
						{
							return Finish();
						}
						return null;
					}
				}
				int nl = Array.IndexOf(_buf, (byte)10, _pos, _len - _pos);
				if (nl >= 0)
				{
					_line.Write(_buf, _pos, nl - _pos);
					_pos = nl + 1;
					return Finish();
				}
				_line.Write(_buf, _pos, _len - _pos);
				_pos = _len;
				if (_line.Length > MaxLineBytes)
				{
					throw new IOException("NDJSON 行太长");
				}
			}
		}

		private string Finish()
		{
			string s = Utf8.GetString(_line.GetBuffer(), 0, (int)_line.Length);
			if (s.EndsWith("\r", StringComparison.Ordinal))
			{
				s = s.Substring(0, s.Length - 1);
			}
			return s;
		}
	}
}

/// <summary>
/// 双向 JSON-RPC（DESIGN §3.1）：两边都可以发请求、回响应、发通知。
/// <list type="bullet">
/// <item>请求 <c>{"id":7,"method":"...","params":{...}}</c></item>
/// <item>响应 <c>{"id":7,"result":...}</c> / <c>{"id":7,"error":{"code":"...","message":"..."}}</c></item>
/// <item>通知 <c>{"method":"...","params":{...}}</c>（无 id）</item>
/// </list>
/// 收到的请求在线程池上处理（长时间等待的方法如 ui.confirm 不会堵住读线程）。写出整行加锁，保证顺序和原子性。
/// </summary>
public sealed class JsonRpcPeer : IDisposable
{
	private readonly Stream _in;

	private readonly Stream _out;

	private readonly object _writeLock = new object();

	private readonly ConcurrentDictionary<long, TaskCompletionSource<object>> _pending = new ConcurrentDictionary<long, TaskCompletionSource<object>>();

	private long _nextId;

	private Thread _reader;

	private volatile bool _closed;

	/// <summary>处理对方发来的请求：返回值作为 result；抛 <see cref="PluginRpcException"/> 返回对应 code。</summary>
	public Func<string, PyDict, object> OnRequest;

	/// <summary>处理对方发来的通知。在读线程上同步调用，要快。</summary>
	public Action<string, PyDict> OnNotification;

	/// <summary>读到流结束或出错时调用一次。</summary>
	public Action<Exception> OnClosed;

	/// <summary>收到无法解析的行（例如插件直接往 stdout 打了东西）。</summary>
	public Action<string> OnGarbage;

	public bool IsClosed => _closed;

	public JsonRpcPeer(Stream input, Stream output)
	{
		_in = input;
		_out = output;
	}

	public void Start()
	{
		_reader = new Thread(ReadLoop)
		{
			IsBackground = true,
			Name = "plugin-rpc-reader"
		};
		_reader.Start();
	}

	private void ReadLoop()
	{
		Exception err = null;
		try
		{
			Ndjson.LineReader lr = new Ndjson.LineReader(_in);
			string line;
			while ((line = lr.ReadLine()) != null)
			{
				if (line.Trim().Length == 0)
				{
					continue;
				}
				object msg;
				try
				{
					msg = Ndjson.Decode(line);
				}
				catch (Exception)
				{
					OnGarbage?.Invoke(line);
					continue;
				}
				if (msg is PyDict d)
				{
					Dispatch(d);
				}
				else
				{
					OnGarbage?.Invoke(line);
				}
			}
		}
		catch (Exception ex)
		{
			err = ex;
		}
		Close(err ?? new EndOfStreamException("对端已关闭"));
	}

	private void Dispatch(PyDict d)
	{
		object id = d.Get("id");
		string method = d.Get("method") as string;
		if (method != null)
		{
			PyDict prm = (d.Get("params") as PyDict) ?? new PyDict();
			if (id == null)
			{
				try
				{
					OnNotification?.Invoke(method, prm);
				}
				catch (Exception)
				{
				}
				return;
			}
			ThreadPool.QueueUserWorkItem(delegate
			{
				HandleRequest(id, method, prm);
			});
			return;
		}
		// 响应
		if (id == null)
		{
			return;
		}
		long key;
		try
		{
			key = Py.Int(id);
		}
		catch (Exception)
		{
			return;
		}
		if (_pending.TryRemove(key, out TaskCompletionSource<object> tcs))
		{
			if (d.Get("error") is PyDict e)
			{
				tcs.TrySetException(new PluginRpcException(Py.S(e.Get("code")).Length > 0 ? Py.S(e.Get("code")) : "error", Py.S(e.Get("message")), e.Get("data")));
			}
			else if (d.ContainsKey("error") && d.Get("error") != null)
			{
				tcs.TrySetException(new PluginRpcException("error", Py.Str(d.Get("error"))));
			}
			else
			{
				tcs.TrySetResult(d.Get("result"));
			}
		}
	}

	private void HandleRequest(object id, string method, PyDict prm)
	{
		PyDict resp;
		try
		{
			Func<string, PyDict, object> h = OnRequest;
			if (h == null)
			{
				throw new PluginRpcException("method_not_found", "未知方法：" + method);
			}
			object result = h(method, prm);
			resp = PyDict.Of("id", id, "result", result);
		}
		catch (Exception ex)
		{
			resp = PyDict.Of("id", id, "error", ErrorOf(ex));
		}
		try
		{
			Write(resp);
		}
		catch (Exception ex2)
		{
			// 结果里有不能序列化的东西：退回一个错误
			try
			{
				Write(PyDict.Of("id", id, "error", PyDict.Of("code", "error", "message", "结果无法序列化：" + ex2.Message)));
			}
			catch (Exception)
			{
			}
		}
	}

	public static PyDict ErrorOf(Exception ex)
	{
		if (ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null)
		{
			ex = tie.InnerException;
		}
		if (ex is PluginRpcException pre)
		{
			PyDict d = PyDict.Of("code", pre.Code, "message", pre.Message);
			if (pre.Data2 != null)
			{
				d["data"] = pre.Data2;
			}
			return d;
		}
		if (ex is PermissionError)
		{
			return PyDict.Of("code", "permission_denied", "message", Py.ExcStr(ex));
		}
		if (ex is RpcError re && re.Message.StartsWith("unknown method", StringComparison.Ordinal))
		{
			return PyDict.Of("code", "method_not_found", "message", re.Message);
		}
		if (ex is TimeoutException || ex is TimeoutError)
		{
			return PyDict.Of("code", "timeout", "message", ex.Message);
		}
		string code = (ex is PyException pe) ? pe.GetType().Name : "error";
		return PyDict.Of("code", code, "message", (ex is PyException) ? Py.ExcStr(ex) : ex.Message);
	}

	/// <summary>写一条消息（整行，加锁）。</summary>
	public void Write(PyDict msg)
	{
		if (_closed)
		{
			throw new IOException("连接已关闭");
		}
		byte[] bytes = Ndjson.Encode(msg);
		lock (_writeLock)
		{
			_out.Write(bytes, 0, bytes.Length);
			_out.Flush();
		}
	}

	public void Notify(string method, PyDict prm)
	{
		Write(PyDict.Of("method", method, "params", prm ?? new PyDict()));
	}

	/// <summary>发请求，等响应。超时或取消时抛 <see cref="TimeoutException"/> / <see cref="OperationCanceledException"/>。</summary>
	public Task<object> RequestAsync(string method, PyDict prm, TimeSpan? timeout = null, CancellationToken ct = default(CancellationToken))
	{
		long id = Interlocked.Increment(ref _nextId);
		TaskCompletionSource<object> tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pending[id] = tcs;
		if (_closed)
		{
			_pending.TryRemove(id, out _);
			tcs.TrySetException(new IOException("Node 宿主未连接"));
			return tcs.Task;
		}
		try
		{
			Write(PyDict.Of("id", id, "method", method, "params", prm ?? new PyDict()));
		}
		catch (Exception ex)
		{
			_pending.TryRemove(id, out _);
			tcs.TrySetException(ex);
			return tcs.Task;
		}
		if (timeout.HasValue && timeout.Value > TimeSpan.Zero && timeout.Value != Timeout.InfiniteTimeSpan)
		{
			CancellationTokenSource cts = new CancellationTokenSource(timeout.Value);
			cts.Token.Register(delegate
			{
				if (_pending.TryRemove(id, out TaskCompletionSource<object> t))
				{
					t.TrySetException(new TimeoutException(method + " 超时"));
				}
				cts.Dispose();
			});
			tcs.Task.ContinueWith(delegate
			{
				try
				{
					cts.Dispose();
				}
				catch (Exception)
				{
				}
			}, TaskScheduler.Default);
		}
		if (ct.CanBeCanceled)
		{
			ct.Register(delegate
			{
				if (_pending.TryRemove(id, out TaskCompletionSource<object> t2))
				{
					t2.TrySetCanceled();
				}
			});
		}
		return tcs.Task;
	}

	/// <summary>同步版请求。异常原样抛出（不包 AggregateException）。</summary>
	public object Request(string method, PyDict prm, TimeSpan? timeout = null, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			return RequestAsync(method, prm, timeout, ct).GetAwaiter().GetResult();
		}
		catch (TaskCanceledException)
		{
			throw new OperationCanceledException();
		}
	}

	private void Close(Exception why)
	{
		if (_closed)
		{
			return;
		}
		_closed = true;
		foreach (KeyValuePair<long, TaskCompletionSource<object>> kv in _pending)
		{
			if (_pending.TryRemove(kv.Key, out TaskCompletionSource<object> t))
			{
				t.TrySetException(new IOException("Node 宿主连接断开"));
			}
		}
		try
		{
			OnClosed?.Invoke(why);
		}
		catch (Exception)
		{
		}
	}

	public void Dispose()
	{
		Close(new ObjectDisposedException("JsonRpcPeer"));
		try
		{
			_out.Dispose();
		}
		catch (Exception)
		{
		}
		try
		{
			_in.Dispose();
		}
		catch (Exception)
		{
		}
	}
}
