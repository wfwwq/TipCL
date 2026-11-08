// M3-C1 插件宿主最小落地（进程内）。语义对齐 Windows 版 Core/Plugins/PluginManager.cs 的
// 启停落盘 + 目录/清单/日志约定；Node 宿主进程、RPC 桥、SecretStore 不在本最小实现范围
// （Linux 版暂未内置 Node 运行时，node 插件明确报降级错误）。.NET 插件只做程序集加载校验。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TipCL.Core.Plugins;

/// <summary>插件运行态快照，供页面/门面读取。</summary>
public sealed class PluginInstanceInfo
{
	public string Id = "";
	public string Dir = "";
	public PluginManifest Manifest;
	public bool Enabled;
	/// <summary>running / stopped / disabled / error</summary>
	public string State = "stopped";
	public string Error = "";
}

/// <summary>
/// 进程内插件宿主（最小版）：启停走 PluginStateStore 落盘；事件 Changed 通知界面刷新；
/// UI 贡献从已启用插件的 manifest 汇总。宿主不引入 Node 子进程与 RPC 桥。
/// </summary>
public sealed class PluginHost
{
	/// <summary>插件状态变化（id，"" 表示全量刷新）。UI 线程无关，订阅方自行调度。</summary>
	public event Action<string> Changed;

	private readonly PluginDirs _dirs;
	private readonly PluginStateStore _store;
	private readonly string _tipclVersion;

	/// <summary>当前进程内已启动的插件 id（.NET 插件加载校验成功后进入）。</summary>
	private readonly HashSet<string> _running = new HashSet<string>(StringComparer.Ordinal);

	public PluginHost(PluginDirs dirs = null, string tipclVersion = "0.1.0-m3")
	{
		_dirs = dirs ?? new PluginDirs();
		_store = new PluginStateStore(_dirs.StateFile);
		_store.Load();
		_tipclVersion = tipclVersion;
	}

	public PluginDirs Dirs => _dirs;
	public PluginStateStore StateStore => _store;

	public List<PluginInstanceInfo> Discover()
	{
		_store.Load(); // 拉取外部（页面/其他宿主）对 plugins.json 的改动
		List<PluginInstanceInfo> list = new List<PluginInstanceInfo>();
		try
		{
			if (!Directory.Exists(_dirs.PluginsRoot))
			{
				return list;
			}
			foreach (string dir in Directory.EnumerateDirectories(_dirs.PluginsRoot))
			{
				string name = Path.GetFileName(dir);
				if (name.StartsWith(".", StringComparison.Ordinal))
				{
					continue;
				}
				PluginInstanceInfo info;
				try
				{
					info = InfoOf(name, dir);
				}
				catch (Exception e)
				{
					info = new PluginInstanceInfo { Id = name, Dir = dir, State = "error", Error = Py.ExcStr(e) };
				}
				list.Add(info);
			}
		}
		catch (Exception)
		{
		}
		return list.OrderBy((PluginInstanceInfo i) => i.Id, StringComparer.Ordinal).ToList();
	}

	private PluginInstanceInfo InfoOf(string id, string dir)
	{
		PluginManifest m = PluginManifest.LoadDir(dir);
		PluginRecord r = _store.Get(id);
		bool enabled = r?.Enabled != false;
		bool dev = r?.Source == "dev";
		string state;
		string error = "";
		if (!enabled)
		{
			state = "disabled";
		}
		else if (!m.EngineOk(_tipclVersion, out string engErr))
		{
			state = "error";
			error = engErr ?? "";
		}
		else if (_running.Contains(id))
		{
			state = "running";
		}
		else
		{
			state = "stopped";
		}
		if (dev && state == "stopped")
		{
			state = "dev";
		}
		return new PluginInstanceInfo { Id = id, Dir = dir, Manifest = m, Enabled = enabled, State = state, Error = error };
	}

	public PluginInstanceInfo Info(string id)
	{
		string dir = _dirs.PluginDir(id);
		if (!Directory.Exists(dir))
		{
			return null;
		}
		return InfoOf(id, dir);
	}

	public bool IsRunning(string id)
	{
		return _running.Contains(id);
	}

	/// <summary>启用并启动插件。node 运行时明确降级；.NET 插件做程序集加载校验。失败回滚状态。</summary>
	public PyDict Start(string id)
	{
		PluginInstanceInfo info = Info(id);
		if (info == null)
		{
			throw new PyException("PluginError", "插件不存在：" + id);
		}
		string err = info.Error;
		if (err.Length == 0 && info.Manifest != null)
		{
			if (info.Manifest.IsNode)
			{
				err = "Linux 版暂未内置 Node 插件宿主，该插件无法在进程内运行（WebView 承载面同样降级）。";
			}
			else if (info.Manifest.IsDotnet)
			{
				try
				{
					ValidateDotNetAssembly(info);
				}
				catch (Exception e)
				{
					err = Py.ExcStr(e);
				}
			}
		}
		if (err.Length > 0)
		{
			AppendLog(id, "[host] 启动失败：" + err);
			Changed?.Invoke(id);
			return PyDict.Of("ok", false, "error", err);
		}
		bool prev = _store.Get(id)?.Enabled != false;
		_store.Update(id, delegate (PluginRecord r)
		{
			r.Enabled = true;
		});
		_running.Add(id);
		AppendLog(id, "[host] 已启动");
		Changed?.Invoke(id);
		if (!prev)
		{
			Changed?.Invoke("");
		}
		return PyDict.Of("ok", true, "state", "running");
	}

	public PyDict Stop(string id)
	{
		if (_store.Get(id) == null && Info(id) == null)
		{
			throw new PyException("PluginError", "插件不存在：" + id);
		}
		_store.Update(id, delegate (PluginRecord r)
		{
			r.Enabled = false;
		});
		_running.Remove(id);
		AppendLog(id, "[host] 已停止");
		Changed?.Invoke(id);
		Changed?.Invoke("");
		return PyDict.Of("ok", true, "state", "disabled");
	}

	/// <summary>程序集加载校验：按 manifest.assembly 相对路径 LoadFrom，EntryType 可解析即算通过。</summary>
	private void ValidateDotNetAssembly(PluginInstanceInfo info)
	{
		string asm = info.Manifest.Assembly;
		if (string.IsNullOrEmpty(asm))
		{
			throw new PyException("PluginError", "manifest 缺少 assembly 字段");
		}
		string path = Path.GetFullPath(Path.Combine(info.Dir, asm.Replace('\\', '/')));
		if (!File.Exists(path))
		{
			throw new PyException("PluginError", "插件程序集不存在：" + asm);
		}
		System.Reflection.Assembly.LoadFrom(path);
		if (!string.IsNullOrEmpty(info.Manifest.EntryType) &&
			!Enumerable.Any(System.Reflection.Assembly.LoadFile(path).GetTypes(), (Type t) => t.FullName == info.Manifest.EntryType))
		{
			throw new PyException("PluginError", "入口类型不存在：" + info.Manifest.EntryType);
		}
	}

	/// <summary>已启用插件的 UI 贡献汇总（kind 分发表数据源）。降级面（page/aiView/overlay）由 App 侧处理。</summary>
	public List<PyDict> UiContributions()
	{
		List<PyDict> list = new List<PyDict>();
		foreach (PluginInstanceInfo info in Discover())
		{
			if (!info.Enabled || info.Manifest == null)
			{
				continue;
			}
			foreach ((string kind, PyDict spec) one in info.Manifest.UiContributions)
			{
				list.Add(PyDict.Of("plugin", info.Id, "kind", one.kind, "id", Py.Strip(Py.S(one.spec.Get("id"))), "spec", one.spec));
			}
		}
		return list;
	}

	public void AppendLog(string id, string line)
	{
		try
		{
			Directory.CreateDirectory(_dirs.LogsRoot);
			File.AppendAllText(_dirs.LogFile(id), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + line + Environment.NewLine);
		}
		catch (Exception)
		{
		}
	}

	public string ReadLog(string id, int maxLines = 200)
	{
		try
		{
			string path = _dirs.LogFile(id);
			if (!File.Exists(path))
			{
				return "";
			}
			string[] lines = File.ReadAllLines(path);
			int start = Math.Max(0, lines.Length - maxLines);
			return string.Join(Environment.NewLine, lines.Skip(start));
		}
		catch (Exception)
		{
			return "";
		}
	}
}
