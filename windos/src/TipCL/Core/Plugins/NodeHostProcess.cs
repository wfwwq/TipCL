using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TipCL.Core.Plugins;

/// <summary>
/// Node 宿主进程（DESIGN §3.1）：<c>node host.mjs</c>，stdin/stdout 走 NDJSON JSON-RPC，stderr 按 <c>[plugin-id]</c> 前缀分流到插件日志。
/// 崩溃重启策略由 <see cref="PluginManager"/> 决定（<see cref="RestartPolicy"/>）。
/// </summary>
public sealed class NodeHostProcess : IDisposable
{
	private static readonly Regex PrefixRe = new Regex("^\\[([a-z0-9][a-z0-9\\-_.]{1,63})\\]\\s?", RegexOptions.CultureInvariant);

	private Process _proc;

	private Thread _errThread;

	private int _exitedFlag;

	public JsonRpcPeer Peer { get; private set; }

	public string NodePath { get; }

	public string HostScript { get; }

	public string Token { get; }

	public int Pid { get; private set; }

	/// <summary>宿主 Web 服务器端口（host.hello 里报上来）。</summary>
	public int WebPort { get; set; }

	public string HostVersion { get; set; } = "";

	public DateTime StartedAt { get; private set; }

	/// <summary>stderr 一行：(插件 id 或 null, 文本)。</summary>
	public Action<string, string> OnStderr;

	/// <summary>进程退出（只触发一次）：退出码（拿不到时 -1）。</summary>
	public Action<int> OnExit;

	public bool Running
	{
		get
		{
			try
			{
				return _proc != null && !_proc.HasExited && Volatile.Read(ref _exitedFlag) == 0;
			}
			catch (Exception)
			{
				return false;
			}
		}
	}

	public NodeHostProcess(string nodePath, string hostScript, string token)
	{
		NodePath = nodePath;
		HostScript = hostScript;
		Token = token;
	}

	/// <summary>
	/// 启动进程并开始读协议。env 会加到当前环境上。<paramref name="onRequest"/> / <paramref name="onNotify"/> 处理 Node 发来的消息。
	/// </summary>
	public void Start(IDictionary<string, string> env, Func<string, PyDict, object> onRequest, Action<string, PyDict> onNotify, string cwd = null)
	{
		ProcessStartInfo psi = new ProcessStartInfo(NodePath)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardErrorEncoding = new UTF8Encoding(false),
			WorkingDirectory = cwd ?? Path.GetDirectoryName(HostScript)
		};
		// 参数：--enable-source-maps 便于插件报错定位
		psi.Arguments = "--enable-source-maps \"" + HostScript + "\"";
		if (env != null)
		{
			foreach (KeyValuePair<string, string> kv in env)
			{
				psi.EnvironmentVariables[kv.Key] = kv.Value;
			}
		}
		_proc = new Process
		{
			StartInfo = psi,
			EnableRaisingEvents = true
		};
		_proc.Exited += delegate
		{
			FireExit();
		};
		_proc.Start();
		StartedAt = DateTime.UtcNow;
		Pid = _proc.Id;
		Peer = new JsonRpcPeer(_proc.StandardOutput.BaseStream, _proc.StandardInput.BaseStream)
		{
			OnRequest = onRequest,
			OnNotification = onNotify,
			OnGarbage = delegate(string line)
			{
				OnStderr?.Invoke(null, "[stdout] " + line);
			},
			OnClosed = delegate
			{
				// stdout 关了基本就是进程退出；稍等进程真正退出再报
				ThreadPool.QueueUserWorkItem(delegate
				{
					try
					{
						if (!_proc.WaitForExit(3000))
						{
							// 宿主关了 stdout 却没退出：当它坏了
							Kill();
						}
					}
					catch (Exception)
					{
					}
					FireExit();
				});
			}
		};
		Peer.Start();
		_errThread = new Thread(ErrLoop)
		{
			IsBackground = true,
			Name = "plugin-host-stderr"
		};
		_errThread.Start();
	}

	private void ErrLoop()
	{
		try
		{
			StreamReader r = _proc.StandardError;
			string line;
			while ((line = r.ReadLine()) != null)
			{
				var (plugin, text) = SplitPrefix(line);
				try
				{
					OnStderr?.Invoke(plugin, text);
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

	/// <summary>"[dsh-game] xxx" → ("dsh-game", "xxx")；没有前缀 → (null, 原文)。</summary>
	public static (string plugin, string text) SplitPrefix(string line)
	{
		Match m = PrefixRe.Match(line ?? "");
		if (m.Success)
		{
			return (m.Groups[1].Value, line.Substring(m.Length));
		}
		return (null, line ?? "");
	}

	private void FireExit()
	{
		if (Interlocked.Exchange(ref _exitedFlag, 1) != 0)
		{
			return;
		}
		int code = -1;
		try
		{
			if (_proc.HasExited)
			{
				code = _proc.ExitCode;
			}
		}
		catch (Exception)
		{
		}
		try
		{
			Peer?.Dispose();
		}
		catch (Exception)
		{
		}
		try
		{
			OnExit?.Invoke(code);
		}
		catch (Exception)
		{
		}
	}

	/// <summary>优雅退出：发 host.shutdown（宿主卸载全部插件后退出），超时就杀。</summary>
	public void Shutdown(TimeSpan timeout)
	{
		if (!Running)
		{
			return;
		}
		// 先把 OnExit 摘掉：主动关闭不算崩溃
		OnExit = null;
		try
		{
			Peer.Request("host.shutdown", new PyDict(), timeout);
		}
		catch (Exception)
		{
		}
		try
		{
			if (!_proc.WaitForExit((int)Math.Max(500.0, timeout.TotalMilliseconds / 2)))
			{
				Kill();
			}
		}
		catch (Exception)
		{
		}
		FireExit();
	}

	public void Kill()
	{
		try
		{
			if (_proc != null && !_proc.HasExited)
			{
				NodeRuntime.KillTree(_proc);
			}
		}
		catch (Exception)
		{
		}
	}

	public void Dispose()
	{
		OnExit = null;
		Kill();
		FireExit();
		try
		{
			_proc?.Dispose();
		}
		catch (Exception)
		{
		}
	}
}

/// <summary>崩溃重启策略：<see cref="Window"/> 内超过 <see cref="MaxCrashes"/> 次就不再重启（DESIGN §3.1：30 秒内超过 3 次）。</summary>
public sealed class RestartPolicy
{
	public int MaxCrashes = 3;

	public TimeSpan Window = TimeSpan.FromSeconds(30.0);

	private readonly List<DateTime> _crashes = new List<DateTime>();

	public Func<DateTime> Now = () => DateTime.UtcNow;

	/// <summary>记录一次崩溃，返回是否还应该重启。</summary>
	public bool RecordCrash()
	{
		lock (_crashes)
		{
			DateTime now = Now();
			_crashes.Add(now);
			_crashes.RemoveAll((DateTime t) => now - t > Window);
			return _crashes.Count <= MaxCrashes;
		}
	}

	public void Reset()
	{
		lock (_crashes)
		{
			_crashes.Clear();
		}
	}
}
