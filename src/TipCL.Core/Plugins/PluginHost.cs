// M3-C1 插件宿主最小落地(进程内);M4-H P6 改为「文件级真做 + 运行时不假装」。
// 语义对齐 Windows 版 Core/Plugins/PluginManager.cs 的启停落盘 + 目录/清单/日志约定;
// Node 宿主进程、RPC 桥、.NET 插件加载、SecretStore 都不在 Linux 版范围内
// (理由见 docs/PARITY.md §7.2):插件文件可以安装/更新/卸载,但**不会有插件代码在本进程里跑**,
// 所以 Start() 一律如实报错,不再出现「加载校验通过 = 运行中」的假成功。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TipCL.Core.Plugins;

/// <summary>插件运行态快照,供页面/门面读取。</summary>
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
/// 进程内插件宿主:启停走 PluginStateStore 落盘;事件 Changed 通知界面刷新;
/// UI 贡献从已启用插件的 manifest 汇总。宿主不引入 Node 子进程与 RPC 桥,
/// 安装/卸载走 <see cref="PluginInstaller"/>(文件级真实现)。
/// </summary>
public sealed class PluginHost
{
	/// <summary>插件状态变化(id,"" 表示全量刷新)。UI 线程无关,订阅方自行调度。</summary>
	public event Action<string> Changed;

	private readonly PluginDirs _dirs;
	private readonly PluginStateStore _store;
	private readonly string _tipclVersion;
	private readonly Lazy<PluginInstaller> _installer;

	/// <summary>当前进程内已启动的插件 id(仅用于状态展示;Linux 版没有可跑插件的运行时)。</summary>
	private readonly HashSet<string> _running = new HashSet<string>(StringComparer.Ordinal);

	public PluginHost(PluginDirs dirs = null, string tipclVersion = "1.1.0")
	{
		_dirs = dirs ?? new PluginDirs();
		_store = new PluginStateStore(_dirs.StateFile);
		_store.Load();
		_tipclVersion = tipclVersion;
		_installer = new Lazy<PluginInstaller>(() => new PluginInstaller(_dirs, _store,
			(string id) => _store.Get(id)?.Version ?? ""));
	}

	public PluginDirs Dirs => _dirs;
	public PluginStateStore StateStore => _store;
	public PluginInstaller Installer => _installer.Value;

	public List<PluginInstanceInfo> Discover()
	{
		_store.Load(); // 拉取外部(页面/其他宿主)对 plugins.json 的改动
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

	/// <summary>启用并启动插件。Linux 版没有插件运行时,见 <see cref="NoRuntimeReason"/>。</summary>
	public PyDict Start(string id)
	{
		PluginInstanceInfo info = Info(id);
		if (info == null)
		{
			throw new PyException("PluginError", "插件不存在:" + id);
		}
		string err = info.Error;
		if (err.Length == 0 && info.Manifest != null)
		{
			err = NoRuntimeReason(info.Manifest);
		}
		if (err.Length > 0)
		{
			AppendLog(id, "[host] 启动失败:" + err);
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

	/// <summary>
	/// 启用 / 禁用(对齐参考版 plugins_set_enabled):落盘状态照改,并如实报告「能不能跑」。
	/// 参考版启用时还会做权限确认与宿主启动;Linux 没有宿主,所以只改文件级状态。
	/// </summary>
	public PyDict SetEnabled(string id, bool enabled)
	{
		PluginInstanceInfo info = Info(id);
		if (info == null)
		{
			throw new PyException("PluginError", "插件不存在:" + id);
		}
		_store.Update(id, delegate (PluginRecord r)
		{
			r.Enabled = enabled;
		});
		if (!enabled)
		{
			_running.Remove(id);
		}
		string reason = enabled ? NoRuntimeReason(info.Manifest) : "";
		AppendLog(id, enabled ? "[host] 已启用" : "[host] 已禁用");
		Changed?.Invoke(id);
		Changed?.Invoke("");
		return PyDict.Of("ok", true, "enabled", enabled, "runnable", reason.Length == 0, "reason", reason);
	}

	public PyDict Stop(string id)
	{
		if (_store.Get(id) == null && Info(id) == null)
		{
			throw new PyException("PluginError", "插件不存在:" + id);
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

	/// <summary>
	/// 插件为什么在 Linux 上跑不起来(没有宿主)。返回空串表示「能跑」——目前任何插件都返回非空。
	/// 这是本轮把「假成功」改掉的地方:参考版的 .NET 插件只是把程序集 LoadFrom 进来做一次校验,
	/// 从未调用它的 Apply 回调,插件其实什么都没做。
	/// </summary>
	public static string NoRuntimeReason(PluginManifest m)
	{
		if (m == null)
		{
			return "插件清单读不出来。";
		}
		if (m.IsDotnet)
		{
			return "Linux 版不能运行 .NET 插件:参考版的插件契约是 net48 的 TipCL 程序集 + WPF 界面类型,本进程既引用不到也加载不了。插件文件可以安装、保留、卸载,但不会被执行。";
		}
		return "Linux 版没有内置 Node 插件宿主(不引入 Node / CEF / WebKitGTK 依赖),插件不能在进程内运行。插件文件可以安装、保留、卸载,但不会被执行。";
	}

	// ───────────────── 安装 / 卸载(文件级,真实现) ─────────────────

	/// <summary>市场安装 / 更新(进度与日志经 log 回调回传,由页面/任务包装成后台任务)。</summary>
	public PluginManifest InstallFromMarket(PyDict entry, IList<string> granted = null, Action<string> log = null)
	{
		PluginManifest m = Installer.InstallFromMarket(entry, granted, log);
		_store.Load();
		Changed?.Invoke(m.Id);
		Changed?.Invoke("");
		return m;
	}

	/// <summary>本地安装(zip 或文件夹)或开发链接(dev=true 只记录路径,不复制)。</summary>
	public PluginManifest InstallLocal(string path, bool dev = false, IList<string> granted = null, Action<string> log = null)
	{
		PluginManifest m = dev
			? Installer.LinkDev(path, granted)
			: Installer.InstallPackage(path, "local", "", null, null, granted, log);
		_store.Load();
		AppendLog(m.Id, dev ? "[host] 已链接开发文件夹:" + path : "[host] 已本地安装:" + path);
		Changed?.Invoke(m.Id);
		Changed?.Invoke("");
		return m;
	}

	public PyDict Uninstall(string id, bool deleteData)
	{
		PluginRecord r = Installer.Uninstall(id, deleteData);
		_running.Remove(id);
		_store.Load();
		Changed?.Invoke(id);
		Changed?.Invoke("");
		return PyDict.Of("ok", true, "source", r?.Source ?? "");
	}

	/// <summary>重新读一遍状态(参考版的 plugins_reload 是宿主重启插件进程;Linux 只重读落盘状态)。</summary>
	public PyDict Reload(string id)
	{
		if (Info(id) == null && _store.Get(id) == null)
		{
			throw new PyException("PluginError", "插件不存在:" + id);
		}
		_store.Load();
		Changed?.Invoke(id);
		Changed?.Invoke("");
		return PyDict.Of("ok", true, "state", Info(id)?.State ?? "disabled");
	}

	/// <summary>已启用插件的 UI 贡献汇总(kind 分发表数据源)。降级面(page/aiView/overlay)由 App 侧处理。</summary>
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
