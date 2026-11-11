using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace TipCL.Core.Launcher.Ai;

public sealed class McpClient
{
	public const string PROTOCOL_VERSION = "2024-11-05";

	public const double CONNECT_TIMEOUT = 10.0;

	public const double CALL_TIMEOUT = 60.0;

	public readonly string name;

	public readonly string command;

	public readonly List<string> args;

	public readonly PyDict env;

	private Process _proc;

	private long _next_id = 1L;

	private readonly object _lock = new object();

	public McpClient(string name, string command, List<object> args = null, PyDict env = null)
	{
		this.name = name;
		this.command = command;
		this.args = (args ?? new List<object>()).Select(Py.Str).ToList();
		this.env = env;
	}

	private void _send(PyDict payload)
	{
		string value = PyJson.Dumps(payload, null, ensureAscii: false) + (Utils.IS_WINDOWS ? "\r\n" : "\n");
		_proc.StandardInput.Write(value);
		_proc.StandardInput.Flush();
	}

	private PyDict _recv(long wantId, double timeout)
	{
		double deadline = Py.Monotonic() + timeout;
		bool timedOut = false;
		ManualResetEventSlim answered = new ManualResetEventSlim(initialState: false);
		Process proc = _proc;
		Thread thread = new Thread((ThreadStart)delegate
		{
			if (answered.Wait(TimeSpan.FromSeconds(Math.Max(0.1, deadline - Py.Monotonic()))))
			{
				return;
			}
			timedOut = true;
			try
			{
				if (!proc.HasExited)
				{
					proc.Kill();
				}
			}
			catch (Exception)
			{
			}
		});
		thread.IsBackground = true;
		thread.Start();
		try
		{
			while (true)
			{
				string text;
				try
				{
					text = _proc.StandardOutput.ReadLine();
				}
				catch (Exception)
				{
					text = null;
				}
				if (text == null)
				{
					if (timedOut)
					{
						throw new McpError("MCP server " + name + " 响应超时");
					}
					string text2;
					try
					{
						text2 = (_proc.WaitForExit(200) ? _proc.ExitCode.ToString() : "None");
					}
					catch (Exception)
					{
						text2 = "None";
					}
					throw new McpError("MCP server " + name + " 已退出（exit=" + text2 + "）");
				}
				text = Py.Strip(text);
				if (text.Length != 0)
				{
					object obj;
					try
					{
						obj = PyJson.Loads(text);
					}
					catch (Exception)
					{
						continue;
					}
					if (obj is PyDict pyDict && Py.Eq(pyDict.Get("id"), wantId))
					{
						return pyDict;
					}
					if (Py.Monotonic() > deadline)
					{
						break;
					}
				}
			}
			throw new McpError("MCP server " + name + " 响应超时");
		}
		finally
		{
			answered.Set();
		}
	}

	private PyDict _request(string method, PyDict prms, double timeout)
	{
		PyDict pyDict;
		lock (_lock)
		{
			if (_proc == null)
			{
				throw new McpError("MCP server " + name + " 未连接");
			}
			long num = _next_id++;
			_send(PyDict.Of("jsonrpc", "2.0", "id", num, "method", method, "params", prms ?? new PyDict()));
			pyDict = _recv(num, timeout);
		}
		if (pyDict.Get("error") is PyDict pyDict2)
		{
			throw new McpError("MCP " + method + " 失败: " + Py.Str(pyDict2.Get("message")));
		}
		return (Py.Or(pyDict.Get("result"), new PyDict()) as PyDict) ?? new PyDict();
	}

	public void Connect()
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo
		{
			FileName = command,
			Arguments = Utils.List2CmdLine(args),
			UseShellExecute = false,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			WorkingDirectory = PPath.Abs(Utils.ROOT),
			StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
		};
		if (env != null)
		{
			foreach (KeyValuePair<string, object> item in env)
			{
				processStartInfo.EnvironmentVariables[item.Key] = Py.Str(item.Value);
			}
		}
		try
		{
			_proc = Process.Start(processStartInfo);
		}
		catch (Win32Exception)
		{
			throw new FileNotFoundError("[Errno 2] No such file or directory: " + Py.ReprStr(command));
		}
		try
		{
			_proc.StandardError.BaseStream.CopyToAsync(Stream.Null);
		}
		catch (Exception)
		{
		}
		_request("initialize", PyDict.Of("protocolVersion", "2024-11-05", "capabilities", new PyDict(), "clientInfo", PyDict.Of("name", "TipCL", "version", "1.0")), 10.0);
		lock (_lock)
		{
			_send(PyDict.Of("jsonrpc", "2.0", "method", "notifications/initialized"));
		}
	}

	public void Close()
	{
		try
		{
			if (_proc != null)
			{
				try
				{
					_proc.StandardInput.Close();
				}
				catch (Exception)
				{
				}
				try
				{
					if (!_proc.HasExited && !_proc.WaitForExit(2000))
					{
						_proc.Kill();
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
		_proc = null;
	}

	public List<object> ListTools()
	{
		return (_request("tools/list", new PyDict(), 10.0).Get("tools") as List<object>) ?? new List<object>();
	}

	public string CallTool(string toolName, PyDict arguments)
	{
		PyDict pyDict = _request("tools/call", PyDict.Of("name", toolName, "arguments", arguments ?? new PyDict()), 60.0);
		if (Py.Truthy(pyDict.Get("isError")))
		{
			throw new McpError(Py.Slice(Py.Str(Py.Or(pyDict.Get("content"), "tool error")), null, 300));
		}
		List<string> list = new List<string>();
		foreach (object item in Py.ListOr(Py.Or(pyDict.Get("content"), new List<object>())))
		{
			if (item is PyDict pyDict2 && Py.Eq(pyDict2.Get("type"), "text"))
			{
				list.Add(Py.S(pyDict2.Get("text")));
			}
		}
		if (list.Count <= 0)
		{
			return PyJson.Dumps(pyDict, null, ensureAscii: false);
		}
		return string.Join("\n", list);
	}
}
