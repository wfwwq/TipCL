using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core.Launcher;

namespace TipCL.Core.Plugins;

/// <summary>一个已发现的插件（已安装或开发者链接）。</summary>
public sealed class PluginInstance
{
	public string Id;

	public string Dir;

	public PluginManifest Manifest;

	/// <summary>running | stopped | error | restart_required | loading</summary>
	public volatile string State = "stopped";

	public string Error = "";

	public SecretStore Secrets;

	/// <summary>开发者模式的文件监视。</summary>
	public FileSystemWatcher Watcher;

	public Timer ReloadTimer;

	/// <summary>.NET 插件的上下文（类型是 DotNetPluginContext，这里存 object 以免非 Windows 测试加载 WPF 类型）。</summary>
	public object DotNetContext;

	/// <summary>.NET 插件这次进程里是否加载过（卸载/更新要重启才生效）。</summary>
	public bool DotNetLoaded;

	/// <summary>市场上有更高版本时的版本号。</summary>
	public string UpdateAvailable = "";

	/// <summary>订阅的事件 topic（可带 *）。</summary>
	public readonly HashSet<string> Subscriptions = new HashSet<string>(StringComparer.Ordinal);

	public bool IsNode => Manifest != null && Manifest.IsNode;

	public bool IsDotnet => Manifest != null && Manifest.IsDotnet;
}

/// <summary>插件 UI 贡献（ui.register / manifest contributes）。</summary>
public sealed class UiContribution
{
	public string Plugin;

	public string Kind;

	public string Id;

	public PyDict Spec = new PyDict();

	/// <summary>来自 manifest（重载时不会被运行时注销冲掉）。</summary>
	public bool FromManifest;
}

/// <summary>
/// 插件系统中枢：发现、启停、加载顺序、Node 宿主、事件转发、UI 贡献、确认请求。
/// 单例 <see cref="Instance"/>，由 <see cref="StartGlobal"/> 在内核启动后（后台）创建。
/// </summary>
public sealed class PluginManager
{
	public static PluginManager Instance { get; private set; }

	/// <summary>TipCL 版本（engines.tipcl 比较、TIPCL_VERSION）。</summary>
	public static string TipclVersion => AppInfo.APP_VERSION;

	public readonly BackendApi Backend;

	public readonly EventBus Bus;

	public readonly PluginDirs Dirs;

	public readonly PluginStateStore State;

	public readonly SecretStore HostSecrets;

	public readonly PluginLlm Llm;

	public readonly PluginHostApi Api;

	public readonly PluginMarket Market;

	public readonly PluginSessions Sessions;

	private readonly object _lock = new object();

	private readonly Dictionary<string, PluginInstance> _plugins = new Dictionary<string, PluginInstance>(StringComparer.Ordinal);

	private readonly List<UiContribution> _ui = new List<UiContribution>();

	private readonly ConcurrentDictionary<string, TaskCompletionSource<object>> _waiters = new ConcurrentDictionary<string, TaskCompletionSource<object>>();

	private readonly Dictionary<string, object> _services = new Dictionary<string, object>(StringComparer.Ordinal);

	private readonly RestartPolicy _restart = new RestartPolicy();

	private readonly object _hostLock = new object();

	private NodeHostProcess _host;

	private NodeRuntime.NodeInfo _node;

	private string _hostState = "stopped";

	private string _hostError = "";

	private ManualResetEventSlim _hostHello = new ManualResetEventSlim(false);

	private volatile bool _shuttingDown;

	private Thread _busThread;

	private string _nodeTaskId;

	/// <summary>.NET 插件的事件处理：(plugin, topic, data)。</summary>
	internal Action<string, string, object> DotNetEventSink;

	public PluginManager(BackendApi backend, EventBus bus, PluginDirs dirs = null)
	{
		Backend = backend;
		Bus = bus ?? backend?.Bus ?? new EventBus();
		Dirs = dirs ?? new PluginDirs();
		Directory.CreateDirectory(Dirs.PluginsRoot);
		Directory.CreateDirectory(Dirs.DataRoot);
		Directory.CreateDirectory(Dirs.LogsRoot);
		State = new PluginStateStore(Dirs.StateFile);
		HostSecrets = new SecretStore(Dirs.HostSecretsFile);
		Llm = new PluginLlm(this);
		Api = new PluginHostApi(this);
		Market = new PluginMarket(this);
		Sessions = new PluginSessions(this);
		PluginAttachments.Dirs = () => Dirs;
		PluginHooks.OnError = delegate(string plugin, string msg)
		{
			Log(plugin, "warn", msg);
		};
	}

	// ───────────────────────────── 启动 / 关闭 ─────────────────────────────

	/// <summary>内核启动时调用（BridgeHost.EnsureCore）。立即返回，加载在后台进行，不拖慢启动。</summary>
	public static PluginManager StartGlobal(BackendApi backend)
	{
		lock (typeof(PluginManager))
		{
			if (Instance != null)
			{
				return Instance;
			}
			ThreadPool.GetMinThreads(out int w, out int io);
			ThreadPool.SetMinThreads(Math.Max(w, 32), io);
			PluginManager m = new PluginManager(backend, backend?.Bus);
			Instance = m;
			ChatEventLog.Sink = m.OnChatEvent;
			try
			{
				AppDomain.CurrentDomain.ProcessExit += delegate
				{
					m.Shutdown(TimeSpan.FromSeconds(2.0));
				};
			}
			catch (Exception)
			{
			}
			Thread t = new Thread((ThreadStart)delegate
			{
				try
				{
					Thread.Sleep(300);
					m.StartAll();
				}
				catch (Exception ex)
				{
					m.LogHost("插件系统启动失败：" + ex);
				}
			})
			{
				IsBackground = true,
				Name = "plugins-start"
			};
			t.Start();
			return m;
		}
	}

	/// <summary>发现 + 按依赖顺序加载全部已启用插件，然后后台检查更新。</summary>
	public void StartAll()
	{
		StartBusBridge();
		try
		{
			Market.CleanupUninstalled();
			Market.ApplyPending();
		}
		catch (Exception)
		{
		}
		Discover();
		LoadEnabled();
		ThreadPool.QueueUserWorkItem(delegate
		{
			try
			{
				Thread.Sleep(5000);
				Market.CheckUpdates(refresh: true);
			}
			catch (Exception)
			{
			}
		});
	}

	/// <summary>App 退出：卸载全部插件，关 Node 宿主。</summary>
	public void Shutdown(TimeSpan timeout)
	{
		if (_shuttingDown)
		{
			return;
		}
		_shuttingDown = true;
		foreach (PluginInstance p in Snapshot())
		{
			try
			{
				p.Watcher?.Dispose();
			}
			catch (Exception)
			{
			}
			if (p.IsDotnet && p.DotNetContext != null)
			{
				try
				{
					DotNetUnload(p);
				}
				catch (Exception)
				{
				}
			}
		}
		Llm.AbortAll(null);
		NodeHostProcess h;
		lock (_hostLock)
		{
			h = _host;
			_host = null;
			_hostState = "stopped";
		}
		h?.Shutdown(timeout);
	}

	// ───────────────────────────── 发现 / 状态 ─────────────────────────────

	public List<PluginInstance> Snapshot()
	{
		lock (_lock)
		{
			return _plugins.Values.ToList();
		}
	}

	public PluginInstance Get(string id)
	{
		lock (_lock)
		{
			return (id != null && _plugins.TryGetValue(id, out PluginInstance p)) ? p : null;
		}
	}

	public PluginInstance Require(string id)
	{
		return Get(id) ?? throw new ValueError("没有这个插件：" + id);
	}

	/// <summary>扫描 plugins.json 和 plugins/ 目录，读清单。plugins/ 里有但 plugins.json 没记录的，记为本地插件（默认停用）。</summary>
	public void Discover()
	{
		State.Load();
		HashSet<string> known = new HashSet<string>(StringComparer.Ordinal);
		foreach (PluginRecord r in State.All())
		{
			known.Add(r.Id);
			DiscoverOne(r);
		}
		if (Directory.Exists(Dirs.PluginsRoot))
		{
			foreach (string d in Directory.GetDirectories(Dirs.PluginsRoot))
			{
				string id = Path.GetFileName(d);
				if (id.StartsWith(".", StringComparison.Ordinal) || known.Contains(id) || !PluginManifest.IsValidId(id) || !File.Exists(Path.Combine(d, PluginManifest.FileName)))
				{
					continue;
				}
				try
				{
					PluginManifest m = PluginManifest.LoadDir(d);
					if (m.Id != id)
					{
						continue;
					}
					PluginRecord rec = State.Update(id, delegate(PluginRecord x)
					{
						x.Enabled = false;
						x.Version = m.Version;
						x.Source = "local";
						x.InstalledAt = NowText();
					});
					DiscoverOne(rec);
				}
				catch (Exception)
				{
				}
			}
		}
		EmitChanged();
	}

	private PluginInstance DiscoverOne(PluginRecord r)
	{
		string dir = (r.Source == "dev" && r.DevPath.Length > 0) ? r.DevPath : Dirs.PluginDir(r.Id);
		PluginInstance p;
		lock (_lock)
		{
			if (!_plugins.TryGetValue(r.Id, out p))
			{
				p = new PluginInstance
				{
					Id = r.Id
				};
				_plugins[r.Id] = p;
			}
		}
		p.Dir = dir;
		p.Secrets = new SecretStore(Dirs.SecretsFile(r.Id));
		try
		{
			p.Manifest = PluginManifest.LoadDir(dir);
			if (p.Manifest.Id != r.Id)
			{
				throw new ValueError("清单里的 id（" + p.Manifest.Id + "）和安装目录不一致");
			}
			if (p.State == "error")
			{
				p.State = "stopped";
				p.Error = "";
			}
		}
		catch (Exception ex)
		{
			p.State = "error";
			p.Error = Py.ExcStr(ex);
		}
		if (r.Source == "dev")
		{
			WatchDev(p);
		}
		return p;
	}

	public static string NowText()
	{
		return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
	}

	public List<string> Granted(string id)
	{
		return State.Get(id)?.GrantedPermissions ?? new List<string>();
	}

	public bool HasPermission(string id, string permission)
	{
		PluginRecord r = State.Get(id);
		if (r == null)
		{
			return false;
		}
		return r.GrantedPermissions.Contains(permission);
	}

	public void RequirePermission(string id, string permission)
	{
		if (!HasPermission(id, permission))
		{
			throw PluginRpcException.Denied(id, permission);
		}
	}

	// ───────────────────────────── 加载顺序 ─────────────────────────────

	/// <summary>
	/// 计算加载顺序（依赖在前）。必需依赖缺失/停用/版本不符或循环依赖的插件放进 errors。
	/// 纯函数，测试用：输入 id → (manifest, enabled)。
	/// </summary>
	public static List<string> LoadOrder(Dictionary<string, (PluginManifest manifest, bool enabled)> all, Dictionary<string, string> errors)
	{
		List<string> order = new List<string>();
		Dictionary<string, int> mark = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (string id in all.Keys.OrderBy((string x) => x, StringComparer.Ordinal))
		{
			if (all[id].enabled && all[id].manifest != null)
			{
				Visit(id, new Stack<string>());
			}
		}
		return order;
		bool Visit(string id, Stack<string> path)
		{
			if (mark.TryGetValue(id, out int m))
			{
				if (m == 1)
				{
					errors[id] = "循环依赖：" + string.Join(" → ", path.Reverse().Concat(new[] { id }));
					return false;
				}
				return m == 2;
			}
			mark[id] = 1;
			path.Push(id);
			PluginManifest man = all[id].manifest;
			bool ok = true;
			foreach (KeyValuePair<string, string> dep in man.Dependencies)
			{
				if (!all.TryGetValue(dep.Key, out (PluginManifest manifest, bool enabled) d) || d.manifest == null)
				{
					errors[id] = "缺少依赖插件：" + dep.Key + " " + dep.Value;
					ok = false;
					break;
				}
				if (!d.enabled)
				{
					errors[id] = "依赖插件未启用：" + dep.Key;
					ok = false;
					break;
				}
				if (!SemVer.Satisfies(d.manifest.Version, dep.Value))
				{
					errors[id] = "依赖插件版本不符：需要 " + dep.Key + " " + dep.Value + "，已装 " + d.manifest.Version;
					ok = false;
					break;
				}
				if (!Visit(dep.Key, path))
				{
					if (!errors.ContainsKey(id))
					{
						errors[id] = "依赖插件 " + dep.Key + " 无法加载";
					}
					ok = false;
					break;
				}
			}
			if (ok)
			{
				// 可选依赖：在、启用、版本合适就排在前面；正在访问中（成环）的跳过，不算错
				foreach (KeyValuePair<string, string> od in man.OptionalDependencies)
				{
					bool usable = all.TryGetValue(od.Key, out (PluginManifest manifest, bool enabled) d2) && d2.manifest != null && d2.enabled && SemVer.Satisfies(d2.manifest.Version, od.Value);
					bool inProgress = mark.TryGetValue(od.Key, out int m2) && m2 == 1;
					if (usable && !inProgress)
					{
						Visit(od.Key, path);
					}
				}
			}
			path.Pop();
			mark[id] = ok ? 2 : 3;
			if (ok)
			{
				order.Add(id);
			}
			return ok;
		}
	}

	/// <summary>加载全部已启用插件（Node 插件需要时先起宿主）。</summary>
	public void LoadEnabled()
	{
		lock (_loadLock)
		{
			LoadEnabledLocked();
		}
	}

	private readonly object _loadLock = new object();

	private void LoadEnabledLocked()
	{
		Dictionary<string, (PluginManifest, bool)> all = new Dictionary<string, (PluginManifest, bool)>(StringComparer.Ordinal);
		foreach (PluginInstance p in Snapshot())
		{
			PluginRecord r = State.Get(p.Id);
			all[p.Id] = (p.Manifest, r != null && r.Enabled && p.Manifest != null);
		}
		Dictionary<string, string> errors = new Dictionary<string, string>(StringComparer.Ordinal);
		List<string> order = LoadOrder(all, errors);
		foreach (KeyValuePair<string, string> e in errors)
		{
			PluginInstance p2 = Get(e.Key);
			if (p2 != null)
			{
				SetState(p2, "error", e.Value);
			}
		}
		List<PluginInstance> nodes = new List<PluginInstance>();
		foreach (string id in order)
		{
			PluginInstance p3 = Get(id);
			if (p3 == null)
			{
				continue;
			}
			if (!p3.Manifest.EngineOk(TipclVersion, out string engErr))
			{
				SetState(p3, "error", engErr);
				continue;
			}
			if (p3.IsDotnet)
			{
				LoadDotNet(p3);
			}
			else if (p3.State != "running" || !HostRunning)
			{
				nodes.Add(p3);
				SetState(p3, "loading", "");
			}
		}
		if (nodes.Count > 0)
		{
			EnsureHostAndLoad(nodes);
		}
		EmitChanged();
	}

	// ───────────────────────────── Node 宿主 ─────────────────────────────

	public bool HostRunning
	{
		get
		{
			lock (_hostLock)
			{
				return _host != null && _host.Running && _hostState == "running";
			}
		}
	}

	internal NodeHostProcess Host
	{
		get
		{
			lock (_hostLock)
			{
				return _host;
			}
		}
	}

	/// <summary>保证宿主在跑，然后按顺序加载这些 Node 插件。宿主没在跑时启动（可能要先下载 Node，作为后台任务）。</summary>
	private void EnsureHostAndLoad(List<PluginInstance> nodes)
	{
		if (HostRunning)
		{
			foreach (PluginInstance p in nodes)
			{
				LoadNode(p);
			}
			return;
		}
		if (_node == null)
		{
			_node = NodeRuntime.Locate(Dirs);
		}
		if (_node == null)
		{
			if (Backend == null)
			{
				SetHostError("找不到 Node " + NodeRuntime.MinMajor + "+");
				return;
			}
			lock (_hostLock)
			{
				if (_hostState == "downloading")
				{
					return;
				}
				_hostState = "downloading";
			}
			_nodeTaskId = Backend.StartTask(I18nCore.Tr("下载插件运行时（Node.js）"), delegate(TaskContext ctx)
			{
				try
				{
					_node = NodeRuntime.Download(Dirs, Backend._dm(ctx), delegate(string s)
					{
						ctx.Log(s);
					});
				}
				catch (Exception ex)
				{
					SetHostError(Py.ExcStr(ex));
					throw;
				}
				lock (_hostLock)
				{
					_hostState = "stopped";
				}
				// 下载完重新走一遍：宿主起来后加载当前所有该加载的 Node 插件
				LoadEnabled();
				return I18nCore.Tr("插件运行时已就绪");
			});
			return;
		}
		if (StartHost())
		{
			foreach (PluginInstance p2 in nodes)
			{
				LoadNode(p2);
			}
		}
	}

	private void SetHostError(string msg)
	{
		lock (_hostLock)
		{
			_hostState = "error";
			_hostError = msg;
		}
		foreach (PluginInstance p in Snapshot().Where((PluginInstance x) => x.IsNode && (x.State == "loading" || x.State == "running")))
		{
			SetState(p, "error", msg);
		}
		LogHost(msg);
		Bus.Emit("plugins.error", PyDict.Of("plugin", "", "message", msg));
		EmitChanged();
	}

	/// <summary>页面令牌 = HMAC-SHA256(key=宿主令牌, 插件 id) 的前 32 位十六进制（Node 宿主用同样算法校验）。</summary>
	public static string PageTokenFor(string hostToken, string pluginId)
	{
		using HMACSHA256 h = new HMACSHA256(Encoding.UTF8.GetBytes(hostToken ?? ""));
		return Utils.Hex(h.ComputeHash(Encoding.UTF8.GetBytes(pluginId ?? ""))).Substring(0, 32);
	}

	public string PageToken(string pluginId)
	{
		NodeHostProcess h = Host;
		return (h == null) ? "" : PageTokenFor(h.Token, pluginId);
	}

	private bool StartHost()
	{
		lock (_hostLock)
		{
			if (_host != null && _host.Running)
			{
				return _hostState == "running";
			}
			if (_shuttingDown)
			{
				return false;
			}
			_hostState = "starting";
			_hostError = "";
		}
		string script;
		try
		{
			script = NodeRuntime.ExtractHost(Dirs);
		}
		catch (Exception ex)
		{
			SetHostError("释放 Node 宿主脚本失败：" + Py.ExcStr(ex));
			return false;
		}
		string token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
		NodeHostProcess host = new NodeHostProcess(_node.Path, script, token);
		Dictionary<string, string> env = new Dictionary<string, string>
		{
			["TIPCL_HOST_TOKEN"] = token,
			["TIPCL_HOME"] = Dirs.Home,
			["TIPCL_VERSION"] = TipclVersion,
			["TIPCL_LANG"] = SafeLang(),
			["TIPCL_PLUGIN_DATA_ROOT"] = Dirs.DataRoot,
			["DSH_HOME"] = Dirs.DataRoot,
			["NODE_NO_WARNINGS"] = "1"
		};
		string nodeDir = Path.GetDirectoryName(_node.Path);
		env["PATH"] = nodeDir + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? "");
		host.OnStderr = OnHostStderr;
		host.OnExit = delegate(int code)
		{
			OnHostExit(host, code);
		};
		ManualResetEventSlim hello = new ManualResetEventSlim(false);
		_hostHello = hello;
		try
		{
			host.Start(env, (string m, PyDict prm) => Api.HandleRequest(host, m, prm), delegate(string m, PyDict prm)
			{
				Api.HandleNotification(host, m, prm);
			});
		}
		catch (Exception ex2)
		{
			SetHostError("启动 Node 宿主失败：" + ex2.Message);
			return false;
		}
		lock (_hostLock)
		{
			_host = host;
		}
		if (!hello.Wait(TimeSpan.FromSeconds(30.0)) || !host.Running)
		{
			host.OnExit = null;
			host.Kill();
			lock (_hostLock)
			{
				if (_host == host)
				{
					_host = null;
				}
			}
			SetHostError("Node 宿主没有响应（30 秒内没收到 host.hello）");
			return false;
		}
		lock (_hostLock)
		{
			_hostState = "running";
		}
		LogHost("Node 宿主已启动：pid " + host.Pid + "，端口 " + host.WebPort + "，Node " + _node.Version);
		EmitUiChanged();
		return true;
	}

	/// <summary>Node 宿主发来 host.hello（PluginHostApi 调）。</summary>
	internal PyDict OnHostHello(NodeHostProcess host, PyDict prm)
	{
		host.WebPort = (int)Py.IntOr(prm.Get("webPort"), 0L);
		host.HostVersion = Py.S(prm.Get("hostVersion"));
		_hostHello.Set();
		return PyDict.Of("tipclVersion", TipclVersion, "lang", SafeLang(), "theme", Theme(), "home", Dirs.Home);
	}

	private void OnHostExit(NodeHostProcess host, int code)
	{
		lock (_hostLock)
		{
			if (_host != host)
			{
				return;
			}
			_host = null;
			_hostState = "stopped";
		}
		if (_shuttingDown)
		{
			return;
		}
		LogHost("Node 宿主退出（退出码 " + code + "）");
		List<PluginInstance> nodes = Snapshot().Where((PluginInstance p) => p.IsNode && (p.State == "running" || p.State == "loading")).ToList();
		foreach (PluginInstance p2 in nodes)
		{
			ClearRegistrations(p2.Id, keepManifest: false);
		}
		if (!_restart.RecordCrash())
		{
			SetHostError(I18nCore.Tr("Node 宿主 30 秒内崩溃超过 3 次，已停止。请查看插件日志。"));
			return;
		}
		foreach (PluginInstance p3 in nodes)
		{
			SetState(p3, "loading", "");
		}
		EmitChanged();
		ThreadPool.QueueUserWorkItem(delegate
		{
			Thread.Sleep(1000);
			if (!_shuttingDown)
			{
				LoadEnabled();
			}
		});
	}

	private void OnHostStderr(string plugin, string text)
	{
		if (plugin != null && Get(plugin) != null)
		{
			Log(plugin, "info", text);
		}
		else
		{
			LogHost(text);
		}
	}

	/// <summary>手动重启宿主（运行时页的“重启”）。</summary>
	public void RestartHost()
	{
		_restart.Reset();
		NodeHostProcess h;
		lock (_hostLock)
		{
			h = _host;
			_host = null;
			_hostState = "stopped";
		}
		if (h != null)
		{
			h.OnExit = null;
			h.Shutdown(TimeSpan.FromSeconds(5.0));
		}
		foreach (PluginInstance p in Snapshot().Where((PluginInstance x) => x.IsNode))
		{
			ClearRegistrations(p.Id, keepManifest: false);
		}
		LoadEnabled();
	}

	// ───────────────────────────── 单个插件加载 / 卸载 ─────────────────────────────

	public PyDict EffectiveConfig(PluginInstance p)
	{
		PyDict cfg = p.Manifest?.ConfigDefaults() ?? new PyDict();
		PluginRecord r = State.Get(p.Id);
		if (r != null)
		{
			foreach (KeyValuePair<string, object> kv in r.Config)
			{
				if (p.Manifest == null || p.Manifest.Config.ContainsKey(kv.Key))
				{
					cfg[kv.Key] = kv.Value;
				}
			}
		}
		if (p.Manifest != null)
		{
			foreach (KeyValuePair<string, object> kv2 in p.Manifest.Config)
			{
				if (p.Manifest.IsSecretConfig(kv2.Key))
				{
					string v = p.Secrets?.Get("config:" + kv2.Key);
					cfg[kv2.Key] = v ?? cfg.Get(kv2.Key) ?? "";
				}
			}
		}
		return cfg;
	}

	/// <summary>manifest 里静态声明的预设和 UI 先登记上（有对应权限才登记）。</summary>
	private void RegisterManifestContributions(PluginInstance p)
	{
		PluginManifest m = p.Manifest;
		if (HasPermission(p.Id, PluginPermissions.AiPresets))
		{
			foreach (PyDict d in m.AgentPresets)
			{
				try
				{
					AgentPresets.Register(AgentPreset.FromDict(p.Id, d, p.Dir));
				}
				catch (Exception ex)
				{
					Log(p.Id, "warn", "预设注册失败：" + Py.ExcStr(ex));
				}
			}
		}
		foreach ((string kind, PyDict spec) in m.UiContributions)
		{
			string perm = (kind == "overlay") ? PluginPermissions.UiOverlay : PluginPermissions.Ui;
			if (HasPermission(p.Id, perm))
			{
				try
				{
					RegisterUi(p.Id, kind, spec, fromManifest: true);
				}
				catch (Exception ex2)
				{
					Log(p.Id, "warn", "界面注册失败：" + Py.ExcStr(ex2));
				}
			}
		}
	}

	private void LoadNode(PluginInstance p)
	{
		NodeHostProcess h = Host;
		if (h == null || !HostRunning)
		{
			SetState(p, "error", _hostError.Length > 0 ? _hostError : "Node 宿主没有运行");
			return;
		}
		SetState(p, "loading", "");
		RegisterManifestContributions(p);
		PluginRecord r = State.Get(p.Id);
		PyDict prm = PyDict.Of("id", p.Id, "dir", p.Dir, "dataDir", Dirs.DataDir(p.Id), "main", p.Manifest.Main.Cast<object>().ToList(), "client", p.Manifest.Client, "manifest", p.Manifest.Raw, "config", EffectiveConfig(p), "granted", (r?.GrantedPermissions ?? new List<string>()).Cast<object>().ToList(), "dev", r != null && r.Source == "dev");
		prm["pageToken"] = PageToken(p.Id);
		prm["cacheDir"] = Dirs.CacheDir(p.Id);
		prm["logFile"] = Dirs.LogFile(p.Id);
		Directory.CreateDirectory(Dirs.DataDir(p.Id));
		try
		{
			object res = h.Peer.Request("plugin.load", prm, TimeSpan.FromSeconds(120.0));
			PyDict rd = res as PyDict;
			if (rd != null && rd.ContainsKey("ok") && !Py.Truthy(rd.Get("ok")))
			{
				ClearRegistrations(p.Id, keepManifest: false);
				SetState(p, "error", Py.S(rd.Get("error")));
				Bus.Emit("plugins.error", PyDict.Of("plugin", p.Id, "message", Py.S(rd.Get("error"))));
				return;
			}
			SetState(p, "running", "");
			Log(p.Id, "info", "已加载 " + p.Manifest.Version);
		}
		catch (Exception ex)
		{
			ClearRegistrations(p.Id, keepManifest: false);
			string msg = (ex is PluginRpcException pre) ? pre.Message : ex.Message;
			SetState(p, "error", "加载失败：" + msg);
			Bus.Emit("plugins.error", PyDict.Of("plugin", p.Id, "message", msg));
		}
	}

	/// <summary>撤销插件在 TipCL 侧的全部登记（工具、预设、钩子、UI、订阅、LLM 请求）。</summary>
	public void ClearRegistrations(string id, bool keepManifest)
	{
		PluginTools.UnregisterPlugin(id);
		AgentPresets.UnregisterPlugin(id);
		PluginHooks.UnregisterPlugin(id);
		Llm.AbortAll(id);
		PluginInstance p = Get(id);
		if (p != null)
		{
			lock (p.Subscriptions)
			{
				p.Subscriptions.Clear();
			}
		}
		bool changed;
		lock (_ui)
		{
			changed = _ui.RemoveAll((UiContribution u) => u.Plugin == id && (!keepManifest || !u.FromManifest)) > 0;
		}
		if (changed)
		{
			EmitUiChanged();
		}
	}

	private void UnloadNode(PluginInstance p)
	{
		NodeHostProcess h = Host;
		if (h != null && h.Running)
		{
			try
			{
				h.Peer.Request("plugin.unload", PyDict.Of("id", p.Id), TimeSpan.FromSeconds(15.0));
			}
			catch (Exception ex)
			{
				Log(p.Id, "warn", "卸载时出错：" + ex.Message);
			}
		}
		ClearRegistrations(p.Id, keepManifest: false);
		SetState(p, "stopped", "");
	}

	/// <summary>停止插件（不改启用状态）。.NET 插件只能标记“重启后生效”。</summary>
	public void Unload(string id)
	{
		PluginInstance p = Require(id);
		if (p.IsDotnet)
		{
			if (p.DotNetLoaded)
			{
				DotNetUnload(p);
				SetState(p, "restart_required", "重启 TipCL 后生效");
			}
			return;
		}
		UnloadNode(p);
		// 停掉依赖它的插件
		foreach (PluginInstance dep in Snapshot().Where((PluginInstance x) => x.Manifest != null && x.Manifest.Dependencies.ContainsKey(id) && x.State == "running"))
		{
			Unload(dep.Id);
			SetState(dep, "error", "依赖插件 " + id + " 已停止");
		}
		EmitChanged();
	}

	/// <summary>重新读清单并加载（开发者模式热重载、plugins_reload、plugin.reload）。</summary>
	public void Reload(string id)
	{
		PluginInstance p = Require(id);
		if (p.IsDotnet && p.DotNetLoaded)
		{
			SetState(p, "restart_required", "重启 TipCL 后生效");
			EmitChanged();
			return;
		}
		if (p.IsNode && (p.State == "running" || p.State == "loading"))
		{
			UnloadNode(p);
		}
		PluginRecord r = State.Get(id);
		if (r != null)
		{
			DiscoverOne(r);
		}
		if (r != null && r.Enabled)
		{
			LoadEnabled();
		}
		else
		{
			EmitChanged();
		}
	}

	public void SetState(PluginInstance p, string state, string error)
	{
		bool changed = p.State != state || p.Error != (error ?? "");
		p.State = state;
		p.Error = error ?? "";
		if (state == "error" && !string.IsNullOrEmpty(error))
		{
			Log(p.Id, "error", error);
		}
		if (changed)
		{
			EmitChanged();
		}
	}

	// ───────────────────────────── .NET 插件 ─────────────────────────────

	private void LoadDotNet(PluginInstance p)
	{
		if (p.DotNetLoaded)
		{
			if (p.State != "running")
			{
				SetState(p, "restart_required", "重启 TipCL 后生效");
			}
			return;
		}
		RegisterManifestContributions(p);
		try
		{
			DotNetLoad(p);
			p.DotNetLoaded = true;
			SetState(p, "running", "");
		}
		catch (Exception ex)
		{
			ClearRegistrations(p.Id, keepManifest: false);
			SetState(p, "error", ".NET 插件加载失败：" + ((ex.InnerException != null) ? ex.InnerException.Message : ex.Message));
		}
	}

	// 单独的不内联方法：只有真的有 .NET 插件时才会去加载 WPF 相关类型
	[MethodImpl(MethodImplOptions.NoInlining)]
	private void DotNetLoad(PluginInstance p)
	{
		p.DotNetContext = DotNetPluginLoader.Load(this, p);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void DotNetUnload(PluginInstance p)
	{
		DotNetPluginLoader.Unload(this, p);
	}

	// ───────────────────────────── 启用 / 停用 / 卸载 ─────────────────────────────

	/// <summary>
	/// 启用/停用。启用时如果还有没授予的权限，先发 plugins.confirm（后台等用户点），同意后再启用。
	/// 返回 {ok, pending?, state}。
	/// </summary>
	public PyDict SetEnabled(string id, bool enabled)
	{
		PluginInstance p = Require(id);
		if (!enabled)
		{
			State.Update(id, delegate(PluginRecord r)
			{
				r.Enabled = false;
			});
			Unload(id);
			EmitChanged();
			return PyDict.Of("ok", true, "state", p.State);
		}
		if (p.Manifest == null)
		{
			return PyDict.Of("ok", false, "error", p.Error);
		}
		List<string> missing = p.Manifest.Permissions.Except(Granted(id)).ToList();
		if (missing.Count == 0)
		{
			State.Update(id, delegate(PluginRecord r)
			{
				r.Enabled = true;
			});
			ThreadPool.QueueUserWorkItem(delegate
			{
				LoadEnabled();
			});
			return PyDict.Of("ok", true, "state", "loading");
		}
		ThreadPool.QueueUserWorkItem(delegate
		{
			bool ok = ConfirmPermissions(id, p.Manifest.Permissions, missing, verified: true, "enable");
			if (!ok)
			{
				return;
			}
			State.Update(id, delegate(PluginRecord r)
			{
				r.Enabled = true;
				foreach (string m in p.Manifest.Permissions)
				{
					if (!r.GrantedPermissions.Contains(m))
					{
						r.GrantedPermissions.Add(m);
					}
				}
			});
			LoadEnabled();
		});
		return PyDict.Of("ok", true, "pending", true, "state", p.State);
	}

	// ───────────────────────────── 确认请求（权限 / ui.confirm / ui.prompt） ─────────────────────────────

	/// <summary>界面没有在监听事件时直接返回默认值，免得永远卡住。</summary>
	private bool UiListening => Bus.SubscriberCount > 1 || TestUiListening;

	internal static bool TestUiListening;

	/// <summary>
	/// 发 plugins.confirm {request_id, plugin, permissions, newPermissions, verified, action, warnings}，等 plugins_confirm。
	/// 超时 10 分钟按拒绝处理。
	/// </summary>
	public bool ConfirmPermissions(string plugin, List<string> permissions, List<string> newPermissions, bool verified, string action, PyDict extra = null)
	{
		string rid = "pc_" + Guid.NewGuid().ToString("N").Substring(0, 16);
		TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
		_waiters[rid] = tcs;
		try
		{
			PyDict ev = PyDict.Of("request_id", rid, "plugin", plugin, "permissions", PermissionRows(permissions), "newPermissions", (newPermissions ?? new List<string>()).Cast<object>().ToList(), "verified", verified, "action", action, "system", permissions.Contains(PluginPermissions.System));
			if (extra != null)
			{
				ev.Update(extra);
			}
			Bus.Emit("plugins.confirm", ev);
			if (!tcs.Task.Wait(TimeSpan.FromMinutes(10.0)))
			{
				return false;
			}
			return Py.Truthy(tcs.Task.Result);
		}
		finally
		{
			_waiters.TryRemove(rid, out _);
		}
	}

	public static List<object> PermissionRows(IEnumerable<string> permissions)
	{
		return (permissions ?? Enumerable.Empty<string>()).Select((string p) => (object)PyDict.Of("id", p, "label", I18nCore.Tr(PluginPermissions.Describe(p)), "danger", p == PluginPermissions.System)).ToList();
	}

	/// <summary>plugins_confirm / plugins_ui_reply 用：把结果交给等待者。</summary>
	public bool Reply(string requestId, object result)
	{
		if (requestId != null && _waiters.TryRemove(requestId, out TaskCompletionSource<object> tcs))
		{
			tcs.TrySetResult(result);
			return true;
		}
		return false;
	}

	/// <summary>
	/// 发 plugins.ui_request。wait=true 时等 plugins_ui_reply（最多 10 分钟；界面没在听时立刻返回 dflt）。
	/// </summary>
	public object UiRequest(string plugin, string kind, PyDict payload, bool wait, object dflt = null, CancellationToken ct = default(CancellationToken))
	{
		string rid = "ui_" + Guid.NewGuid().ToString("N").Substring(0, 16);
		PyDict ev = PyDict.Of("request_id", rid, "plugin", plugin, "kind", kind);
		if (payload != null)
		{
			foreach (KeyValuePair<string, object> kv in payload)
			{
				if (kv.Key != "plugin")
				{
					ev[kv.Key] = kv.Value;
				}
			}
		}
		if (!wait)
		{
			Bus.Emit("plugins.ui_request", ev);
			return null;
		}
		if (!UiListening)
		{
			return dflt;
		}
		TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
		_waiters[rid] = tcs;
		try
		{
			Bus.Emit("plugins.ui_request", ev);
			try
			{
				if (!tcs.Task.Wait((int)TimeSpan.FromMinutes(10.0).TotalMilliseconds, ct))
				{
					return dflt;
				}
			}
			catch (OperationCanceledException)
			{
				return dflt;
			}
			return tcs.Task.Result;
		}
		finally
		{
			_waiters.TryRemove(rid, out _);
		}
	}

	// ───────────────────────────── UI 贡献 ─────────────────────────────

	public void RegisterUi(string plugin, string kind, PyDict spec, bool fromManifest = false)
	{
		string id = Py.S(spec.Get("id"));
		if (kind == "settings")
		{
			id = "settings";
		}
		if (id.Length == 0)
		{
			throw new PluginRpcException("invalid_params", kind + " 缺少 id");
		}
		if ((kind == "page" || kind == "aiView" || kind == "dashCard" || kind == "overlay" || kind == "settings") && Py.S(spec.Get("view")).Length == 0)
		{
			throw new PluginRpcException("invalid_params", kind + " 缺少 view");
		}
		lock (_ui)
		{
			_ui.RemoveAll((UiContribution u) => u.Plugin == plugin && u.Kind == kind && u.Id == id);
			_ui.Add(new UiContribution
			{
				Plugin = plugin,
				Kind = kind,
				Id = id,
				Spec = spec.Copy(),
				FromManifest = fromManifest
			});
		}
		EmitUiChanged();
	}

	public void UnregisterUi(string plugin, string kind, string id)
	{
		bool changed;
		lock (_ui)
		{
			changed = _ui.RemoveAll((UiContribution u) => u.Plugin == plugin && (string.IsNullOrEmpty(kind) || u.Kind == kind) && u.Id == id) > 0;
		}
		if (changed)
		{
			EmitUiChanged();
		}
	}

	public UiContribution FindUi(string plugin, string kind, string id)
	{
		lock (_ui)
		{
			return _ui.FirstOrDefault((UiContribution u) => u.Plugin == plugin && u.Kind == kind && u.Id == id);
		}
	}

	/// <summary>浮层状态更新（ui.overlayUpdate）：合并进 spec，再发 plugins.ui_request kind=overlay。</summary>
	public void UpdateOverlay(string plugin, string id, PyDict changes)
	{
		UiContribution u = FindUi(plugin, "overlay", id) ?? throw new PluginRpcException("not_found", "没有这个浮层：" + id);
		foreach (string k in new[] { "visible", "width", "height", "anchor", "margin", "clickThrough" })
		{
			if (changes.ContainsKey(k))
			{
				u.Spec[k] = changes[k];
			}
		}
		PyDict payload = PyDict.Of("id", id, "spec", u.Spec.Copy());
		foreach (KeyValuePair<string, object> kv in changes)
		{
			payload[kv.Key] = kv.Value;
		}
		UiRequest(plugin, "overlay", payload, wait: false);
	}

	/// <summary>视图地址：http://127.0.0.1:&lt;port&gt;/__tipcl/view/&lt;plugin&gt;/&lt;view&gt;?k=&lt;pageToken&gt;</summary>
	public string ViewUrl(string plugin, string view)
	{
		NodeHostProcess h = Host;
		if (h == null || h.WebPort <= 0 || string.IsNullOrEmpty(view))
		{
			return "";
		}
		return "http://127.0.0.1:" + h.WebPort + "/__tipcl/view/" + Uri.EscapeDataString(plugin) + "/" + Uri.EscapeDataString(view) + "?k=" + PageToken(plugin);
	}

	/// <summary>plugins_ui()：全部运行中插件的 UI 贡献（含 .NET 原生入口，native:true）。</summary>
	public List<object> UiRows()
	{
		List<UiContribution> list;
		lock (_ui)
		{
			list = _ui.ToList();
		}
		List<object> rows = new List<object>();
		foreach (UiContribution u in list)
		{
			PluginInstance p = Get(u.Plugin);
			if (p == null || p.State != "running")
			{
				continue;
			}
			string view = Py.S(u.Spec.Get("view"));
			PyDict row = PyDict.Of("plugin", u.Plugin, "kind", u.Kind, "id", u.Id, "label", Py.S(Py.Or(u.Spec.Get("label"), p.Manifest?.Name)), "icon", IconOf(p, Py.S(u.Spec.Get("icon"))), "order", Py.IntOr(u.Spec.Get("order"), 100L), "url", ViewUrl(u.Plugin, view), "spec", u.Spec.Copy(), "native", false, "key", "plugin:" + u.Plugin + "/" + u.Id);
			rows.Add(row);
		}
		try
		{
			rows.AddRange(NativeUiRows());
		}
		catch (Exception)
		{
		}
		return rows.OrderBy((object r) => Py.Int(((PyDict)r)["order"])).ToList();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static List<object> NativeUiRows()
	{
		List<object> rows = new List<object>();
		foreach (TipCL.Plugins.NativeUiEntry e in TipCL.Plugins.PluginUiRegistry.Entries)
		{
			PyDict spec = PyDict.Of("id", e.Id, "label", e.Label, "icon", e.Icon ?? "", "order", e.Order, "group", e.Group ?? "more", "defaultW", e.DefaultW, "defaultH", e.DefaultH, "presets", (e.Presets ?? new List<string>()).Cast<object>().ToList());
			rows.Add(PyDict.Of("plugin", e.Plugin, "kind", e.Kind, "id", e.Id, "label", e.Label ?? e.Id, "icon", e.Icon ?? "", "order", e.Order, "url", "", "spec", spec, "native", true, "key", "plugin:" + e.Plugin + "/" + e.Id));
		}
		return rows;
	}

	/// <summary>图标：Fluent 字形原样；相对插件目录的图片转 data URL（≤256KB）；http(s) 原样。</summary>
	public string IconOf(PluginInstance p, string icon)
	{
		if (string.IsNullOrEmpty(icon))
		{
			return "";
		}
		if (icon.Length <= 2 || icon.StartsWith("http://", StringComparison.Ordinal) || icon.StartsWith("https://", StringComparison.Ordinal) || icon.StartsWith("data:", StringComparison.Ordinal))
		{
			return icon;
		}
		if (p?.Dir == null || !PluginManifest.IsSafeRelPath(icon))
		{
			return "";
		}
		try
		{
			string path = Path.Combine(p.Dir, icon);
			FileInfo fi = new FileInfo(path);
			if (!fi.Exists || fi.Length > 262144)
			{
				return "";
			}
			byte[] data = File.ReadAllBytes(path);
			string mime = TipCL.Core.Launcher.Ai.Images.SniffMime(data) ?? (icon.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml" : "application/octet-stream");
			return "data:" + mime + ";base64," + Convert.ToBase64String(data);
		}
		catch (Exception)
		{
			return "";
		}
	}

	// ───────────────────────────── 事件 ─────────────────────────────

	/// <summary>topic 需要的权限（null = 不需要）。</summary>
	public static string TopicPermission(string topic)
	{
		if (topic == null)
		{
			return PluginPermissions.Launcher;
		}
		if (topic.StartsWith("ai.", StringComparison.Ordinal))
		{
			return PluginPermissions.AiSessions;
		}
		if (topic == "theme.changed" || topic == "plugins.changed" || topic == "credentials.updated" || topic == "config.changed")
		{
			return null;
		}
		return PluginPermissions.Launcher;
	}

	/// <summary>启动器事件名 → 插件 topic。</summary>
	public static string MapBusTopic(string ev)
	{
		switch (ev)
		{
		case "game_started":
			return "game.started";
		case "game_exited":
			return "game.exited";
		case "crash":
			return "game.crash";
		default:
			return ev;
		}
	}

	public void Subscribe(string plugin, IEnumerable<string> topics)
	{
		PluginInstance p = Require(plugin);
		foreach (string t in topics)
		{
			if (string.IsNullOrEmpty(t))
			{
				continue;
			}
			// 明确的 topic 没权限直接拒绝；通配的在投递时过滤
			if (t.IndexOf('*') < 0)
			{
				string perm = TopicPermission(t);
				if (perm != null)
				{
					RequirePermission(plugin, perm);
				}
			}
			lock (p.Subscriptions)
			{
				p.Subscriptions.Add(t);
			}
		}
	}

	public void Unsubscribe(string plugin, IEnumerable<string> topics)
	{
		PluginInstance p = Get(plugin);
		if (p == null)
		{
			return;
		}
		lock (p.Subscriptions)
		{
			foreach (string t in topics)
			{
				p.Subscriptions.Remove(t);
			}
		}
	}

	/// <summary>
	/// 把事件发给订阅了的插件：Node 插件合成一条 <c>event</c> 通知（params.plugins 列出收件插件），.NET 插件直接回调。
	/// only 不为空时只发给这个插件（credentials.updated / config.changed，无需订阅）。
	/// </summary>
	public void Publish(string topic, object data, string only = null)
	{
		List<string> nodeTargets = new List<string>();
		List<string> dotnetTargets = new List<string>();
		foreach (PluginInstance p in Snapshot())
		{
			if (p.State != "running" && p.State != "loading")
			{
				continue;
			}
			if (only != null)
			{
				if (p.Id != only)
				{
					continue;
				}
			}
			else
			{
				bool match;
				lock (p.Subscriptions)
				{
					match = p.Subscriptions.Any((string s) => AgentPresets.GlobMatch(s, topic));
				}
				if (!match)
				{
					continue;
				}
				string perm = TopicPermission(topic);
				if (perm != null && !HasPermission(p.Id, perm))
				{
					continue;
				}
			}
			if (p.IsNode)
			{
				nodeTargets.Add(p.Id);
			}
			else
			{
				dotnetTargets.Add(p.Id);
			}
		}
		if (nodeTargets.Count > 0)
		{
			NodeHostProcess h = Host;
			if (h != null && h.Running)
			{
				try
				{
					h.Peer.Notify("event", PyDict.Of("topic", topic, "data", data, "plugins", nodeTargets.Cast<object>().ToList()));
				}
				catch (Exception)
				{
				}
			}
		}
		foreach (string id in dotnetTargets)
		{
			try
			{
				DotNetEventSink?.Invoke(id, topic, data);
			}
			catch (Exception)
			{
			}
		}
	}

	/// <summary>有没有插件订阅了这个 topic（game.log 这种高频事件先问一下）。</summary>
	public bool AnySubscriber(string topic)
	{
		foreach (PluginInstance p in Snapshot())
		{
			if (p.State != "running")
			{
				continue;
			}
			lock (p.Subscriptions)
			{
				if (p.Subscriptions.Any((string s) => AgentPresets.GlobMatch(s, topic)))
				{
					return true;
				}
			}
		}
		return false;
	}

	/// <summary>从 EventBus 读启动器事件转给插件（ai.event 不走这里，见 <see cref="OnChatEvent"/>）。</summary>
	private void StartBusBridge()
	{
		if (_busThread != null)
		{
			return;
		}
		BlockingCollection<PyDict> q = Bus.Subscribe();
		_busThread = new Thread((ThreadStart)delegate
		{
			try
			{
				foreach (PyDict item in q.GetConsumingEnumerable())
				{
					if (_shuttingDown)
					{
						break;
					}
					string ev = Py.S(item.Get("event"));
					if (ev.Length == 0 || ev == "ai.event" || ev.StartsWith("plugins.", StringComparison.Ordinal) && ev != "plugins.changed")
					{
						continue;
					}
					try
					{
						Publish(MapBusTopic(ev), item.Get("data"));
					}
					catch (Exception)
					{
					}
				}
			}
			catch (Exception)
			{
			}
			finally
			{
				Bus.Unsubscribe(q);
			}
		})
		{
			IsBackground = true,
			Name = "plugins-bus"
		};
		_busThread.Start();
	}

	/// <summary>ChatEventLog.Sink：AI 事件同步发给 Node（保证顺序），同时发到 EventBus 给界面。</summary>
	private void OnChatEvent(string chatId, PyDict ev)
	{
		PyDict data = PyDict.Of("chatId", chatId, "event", ev);
		try
		{
			Publish("ai.event", data);
		}
		catch (Exception)
		{
		}
		Bus.Emit("ai.event", data);
		if (Py.Eq(ev.Get("type"), "turn/end"))
		{
			try
			{
				Sessions.OnTurnEnded(chatId);
			}
			catch (Exception)
			{
			}
		}
	}

	/// <summary>游戏日志一行（launch 路径直接调，不经过 EventBus，免得刷爆界面队列）。</summary>
	public static void GameLog(string instance, string line)
	{
		PluginManager m = Instance;
		if (m == null || !m.AnySubscriber("game.log"))
		{
			return;
		}
		m.Publish("game.log", PyDict.Of("instance", instance, "line", line));
	}

	public void EmitChanged()
	{
		Bus.Emit("plugins.changed", new PyDict());
	}

	public void EmitUiChanged()
	{
		Bus.Emit("plugins.ui_changed", new PyDict());
	}

	// ───────────────────────────── 配置 / 凭据 ─────────────────────────────

	public void SetConfig(string id, string key, object value)
	{
		PluginInstance p = Require(id);
		if (p.Manifest == null)
		{
			throw new ValueError(p.Error);
		}
		object v = p.Manifest.CoerceConfig(key, value);
		if (p.Manifest.IsSecretConfig(key))
		{
			p.Secrets.Set("config:" + key, Py.S(v));
		}
		else
		{
			State.Update(id, delegate(PluginRecord r)
			{
				r.Config[key] = v;
			});
		}
		NodeHostProcess h = Host;
		if (p.IsNode && h != null && h.Running && p.State == "running")
		{
			try
			{
				h.Peer.Notify("plugin.configChanged", PyDict.Of("id", id, "key", key, "value", v));
			}
			catch (Exception)
			{
			}
		}
		// Node 宿主自己由 plugin.configChanged 产生 config.changed，这里只给 .NET 插件发，免得重复
		if (p.IsDotnet)
		{
			Publish("config.changed", PyDict.Of("key", key, "value", v), id);
		}
	}

	public void SetCredential(string id, string name, string value)
	{
		PluginInstance p = Require(id);
		if (string.IsNullOrEmpty(name) || name.StartsWith("config:", StringComparison.Ordinal))
		{
			throw new ValueError("凭据名不合法：" + name);
		}
		p.Secrets.Set(name, value);
		Publish("credentials.updated", PyDict.Of("name", name, "configured", !string.IsNullOrEmpty(value)), id);
	}

	/// <summary>声明的凭据 + 已存的其他凭据：[{name, label, description, configured}]（不含值）。</summary>
	public List<object> CredentialRows(PluginInstance p)
	{
		List<object> rows = new List<object>();
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		HashSet<string> stored = new HashSet<string>(p.Secrets.Names(), StringComparer.Ordinal);
		foreach (PyDict c in p.Manifest?.Credentials ?? new List<PyDict>())
		{
			string n = Py.S(c.Get("name"));
			if (n.Length == 0 || !seen.Add(n))
			{
				continue;
			}
			rows.Add(PyDict.Of("name", n, "label", Py.S(Py.Or(c.Get("label"), n)), "description", Py.S(c.Get("description")), "configured", stored.Contains(n), "declared", true));
		}
		foreach (string n2 in stored)
		{
			if (!n2.StartsWith("config:", StringComparison.Ordinal) && seen.Add(n2))
			{
				rows.Add(PyDict.Of("name", n2, "label", n2, "description", "", "configured", true, "declared", false));
			}
		}
		return rows;
	}

	// ───────────────────────────── 跨插件服务（.NET） ─────────────────────────────

	public void Provide(string name, object service)
	{
		lock (_services)
		{
			if (service == null)
			{
				_services.Remove(name);
			}
			else
			{
				_services[name] = service;
			}
		}
	}

	public object GetService(string name)
	{
		lock (_services)
		{
			return _services.TryGetValue(name, out object s) ? s : null;
		}
	}

	// ───────────────────────────── 日志 ─────────────────────────────

	private static readonly object LogLock = new object();

	public void Log(string plugin, string level, string text)
	{
		if (string.IsNullOrEmpty(plugin) || !PluginManifest.IsValidId(plugin))
		{
			LogHost(text);
			return;
		}
		AppendLog(Dirs.LogFile(plugin), level, text);
		PluginRecord r = State.Get(plugin);
		if (r != null && r.Source == "dev")
		{
			Bus.Emit("plugins.log", PyDict.Of("plugin", plugin, "level", level, "text", text));
		}
	}

	public void LogHost(string text)
	{
		AppendLog(Path.Combine(Dirs.LogsRoot, "_host.log"), "info", text);
	}

	private static void AppendLog(string path, string level, string text)
	{
		lock (LogLock)
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				FileInfo fi = new FileInfo(path);
				if (fi.Exists && fi.Length > 2097152)
				{
					string old = path + ".1";
					if (File.Exists(old))
					{
						File.Delete(old);
					}
					File.Move(path, old);
				}
				File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + text + Environment.NewLine, new UTF8Encoding(false));
			}
			catch (Exception)
			{
			}
		}
	}

	/// <summary>日志 id 是否指 Node 宿主自身（"host"，兼容旧的 "_host" 和空串）。</summary>
	public static bool IsHostLogId(string id)
	{
		return string.IsNullOrEmpty(id) || id == "host" || id == "_host";
	}

	/// <summary>读日志尾部 n 行。</summary>
	public string LogTail(string id, int lines)
	{
		string path = IsHostLogId(id) ? Path.Combine(Dirs.LogsRoot, "_host.log") : Dirs.LogFile(id);
		if (!File.Exists(path))
		{
			return "";
		}
		try
		{
			using FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			long take = Math.Min(fs.Length, 1048576L);
			fs.Seek(-take, SeekOrigin.End);
			byte[] buf = new byte[take];
			int read = 0;
			while (read < take)
			{
				int n = fs.Read(buf, read, (int)take - read);
				if (n <= 0)
				{
					break;
				}
				read += n;
			}
			string[] all = Encoding.UTF8.GetString(buf, 0, read).Replace("\r\n", "\n").Split('\n');
			return string.Join("\n", all.Skip(Math.Max(0, all.Length - Math.Max(1, lines) - 1))).TrimEnd('\n');
		}
		catch (Exception ex)
		{
			return "读日志失败：" + ex.Message;
		}
	}

	// ───────────────────────────── 开发者模式热重载 ─────────────────────────────

	private void WatchDev(PluginInstance p)
	{
		if (p.Watcher != null || p.Dir == null || !Directory.Exists(p.Dir))
		{
			return;
		}
		try
		{
			FileSystemWatcher w = new FileSystemWatcher(p.Dir)
			{
				IncludeSubdirectories = true,
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
			};
			FileSystemEventHandler onChange = delegate(object s, FileSystemEventArgs e)
			{
				OnDevChange(p, e.FullPath);
			};
			w.Changed += onChange;
			w.Created += onChange;
			w.Deleted += onChange;
			w.Renamed += delegate(object s, RenamedEventArgs e)
			{
				OnDevChange(p, e.FullPath);
			};
			w.EnableRaisingEvents = true;
			p.Watcher = w;
		}
		catch (Exception ex)
		{
			Log(p.Id, "warn", "无法监视开发目录：" + ex.Message);
		}
	}

	/// <summary>开发目录里哪些变化不触发重载。</summary>
	public static bool IgnoredDevPath(string root, string full)
	{
		string rel = full.Length > root.Length ? full.Substring(root.Length).Replace('\\', '/').TrimStart('/') : "";
		string[] segs = rel.Split('/');
		if (segs.Any((string s) => s == "node_modules" || s == ".git" || s == ".cache" || s == ".turbo"))
		{
			return true;
		}
		string name = segs.LastOrDefault() ?? "";
		return name.EndsWith("~", StringComparison.Ordinal) || name.EndsWith(".tmp", StringComparison.Ordinal) || name.EndsWith(".swp", StringComparison.Ordinal) || name.StartsWith(".#", StringComparison.Ordinal);
	}

	private void OnDevChange(PluginInstance p, string path)
	{
		if (IgnoredDevPath(p.Dir, path) || _shuttingDown)
		{
			return;
		}
		lock (p)
		{
			if (p.ReloadTimer == null)
			{
				p.ReloadTimer = new Timer(delegate
				{
					try
					{
						PluginRecord r = State.Get(p.Id);
						if (r != null && r.Enabled)
						{
							Log(p.Id, "info", "检测到文件变化，重新加载");
							Reload(p.Id);
						}
					}
					catch (Exception ex)
					{
						Log(p.Id, "error", "热重载失败：" + ex.Message);
					}
				}, null, -1, -1);
			}
			// 防抖：最后一次变化 600ms 后才重载
			p.ReloadTimer.Change(600, -1);
		}
	}

	// ───────────────────────────── 杂项 ─────────────────────────────

	private static string SafeLang()
	{
		try
		{
			return I18nCore.CurrentLanguage();
		}
		catch (Exception)
		{
			return "zh_CN";
		}
	}

	/// <summary>主题（host.hello / tipcl.theme）：{mode, colors:{bg, fg, card, border, accent, muted, danger}, font}。</summary>
	public static PyDict Theme()
	{
		bool dark = false;
		string accent = "#2E9B6B";
		try
		{
			dark = Py.Truthy(Config.CONFIG.Get("ui_dark", false));
			string c = Py.S(Config.CONFIG.Get("theme_color"));
			if (c.StartsWith("#", StringComparison.Ordinal) && (c.Length == 7 || c.Length == 4))
			{
				accent = c;
			}
		}
		catch (Exception)
		{
		}
		PyDict colors = dark ? PyDict.Of("bg", "#1E1F22", "fg", "#E6E6E6", "card", "#2A2B2F", "border", "#3A3B40", "accent", accent, "muted", "#9A9CA3", "danger", "#E5534B") : PyDict.Of("bg", "#F5F6F7", "fg", "#1F2328", "card", "#FFFFFF", "border", "#E1E4E8", "accent", accent, "muted", "#6A737D", "danger", "#D73A49");
		return PyDict.Of("mode", dark ? "dark" : "light", "colors", colors, "font", "\"Microsoft YaHei UI\", \"Segoe UI\", sans-serif");
	}

	/// <summary>plugins_runtime_status()</summary>
	public PyDict RuntimeStatus()
	{
		NodeHostProcess h = Host;
		NodeRuntime.NodeInfo n = _node;
		bool needNode = Snapshot().Any((PluginInstance p) => p.IsNode && State.Get(p.Id)?.Enabled == true);
		string hostState;
		string hostError;
		lock (_hostLock)
		{
			hostState = _hostState;
			hostError = _hostError;
		}
		return PyDict.Of("node", (n == null) ? null : PyDict.Of("path", n.Path, "version", n.Version, "source", n.Source), "nodeRequired", needNode, "host", PyDict.Of("state", hostState, "error", hostError, "pid", (h != null) ? ((long)h.Pid) : 0L, "port", (h != null) ? ((long)h.WebPort) : 0L, "version", h?.HostVersion ?? "", "startedAt", (h != null) ? h.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : ""), "downloadTask", _nodeTaskId ?? "", "home", Dirs.Home);
	}

	/// <summary>plugins_list() 一行。</summary>
	public PyDict InfoRow(PluginInstance p)
	{
		PluginRecord r = State.Get(p.Id) ?? new PluginRecord
		{
			Id = p.Id
		};
		PluginManifest m = p.Manifest;
		bool hasSettings = m != null && (m.Config.Count > 0 || m.Credentials.Count > 0 || FindUi(p.Id, "settings", "settings") != null);
		return PyDict.Of("id", p.Id, "name", m?.Name ?? p.Id, "version", m?.Version ?? r.Version, "author", m?.Author ?? "", "description", m?.Description ?? "", "icon", IconOf(p, m?.Icon), "runtime", m?.Runtime ?? "", "enabled", r.Enabled, "state", p.State, "error", p.Error ?? "", "source", r.Source, "permissions", (m?.Permissions ?? new List<string>()).Cast<object>().ToList(), "granted", r.GrantedPermissions.Cast<object>().ToList(), "hasSettings", hasSettings, "devPath", r.DevPath ?? "", "updateAvailable", p.UpdateAvailable ?? "", "homepage", m?.Homepage ?? "", "installedAt", r.InstalledAt ?? "", "market", r.Market ?? "", "dir", p.Dir ?? "", "presets", AgentPresets.All().Where((AgentPreset x) => x.Plugin == p.Id).Select((AgentPreset x) => (object)x.ToInfo()).ToList(), "tools", PluginTools.All().Where((PluginToolEntry x) => x.Plugin == p.Id).Select((PluginToolEntry x) => (object)x.Name).ToList());
	}

	/// <summary>测试用：直接放进一个插件实例。</summary>
	internal void AddForTest(PluginInstance p)
	{
		lock (_lock)
		{
			_plugins[p.Id] = p;
		}
	}

	internal void RemoveInstance(string id)
	{
		PluginInstance p;
		lock (_lock)
		{
			if (!_plugins.TryGetValue(id, out p))
			{
				return;
			}
			_plugins.Remove(id);
		}
		try
		{
			p.Watcher?.Dispose();
		}
		catch (Exception)
		{
		}
		try
		{
			p.ReloadTimer?.Dispose();
		}
		catch (Exception)
		{
		}
	}

	/// <summary>测试用：替换全局实例。</summary>
	internal static void SetInstanceForTest(PluginManager m)
	{
		Instance = m;
	}
}
