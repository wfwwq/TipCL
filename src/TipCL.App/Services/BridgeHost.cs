using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;

namespace TipCL.Services;

/// <summary>
/// 后端宿主。**mac 版只有进程内一条路径**(见 GOAL §3 与 PROGRESS.md 的 C2a 差异表):
/// <c>BackendApi</c> + <c>EventBus</c> -> <c>BridgeServer</c>(回环 HTTP + SSE)-> <c>BridgeClient</c>。
/// Windows 版的「外部 Python 桥 / tipcl-bridge.exe」整条分支删掉了,连带
/// <c>Payload.EmbeddedPython</c>、<c>FindPython</c>、<c>PythonCandidates</c>、<c>RegisteredPythons</c>(注册表)、
/// <c>ProbePython</c>、<c>KillTree</c>(taskkill)、<c>QuoteArg</c>、<c>FindNativeBridge</c>、<c>Tail</c>、
/// <c>PortRegex</c>、<c>WalkUp</c>/<c>LooksLikeRoot</c> 这些只服务于那条路径的成员。
///
/// 公开面与 Windows 版一致:<c>Client</c> / <c>Port</c> / <c>Root</c> / <c>Backend</c> / <c>Exited</c> /
/// <c>StartAsync</c> / <c>FindRoot</c> / <c>Dispose</c>。
///
/// 测试纪律(D-23):本类的构造函数与 <c>StartAsync</c> 都会造 <c>BackendApi</c>(构造函数无条件向
/// login.live.com 发预热请求),所以**测试里绝不实例化、绝不调用 StartAsync**;可测的只有
/// <see cref="FindRoot"/> / <see cref="NormalizeRoot"/> / <see cref="NewToken"/> / <see cref="ClientBaseUrl"/>
/// 这几个纯函数。
/// </summary>
public sealed class BridgeHost : IDisposable
{
	private static class CoreState
	{
		internal static readonly object Gate = new object();

		internal static BackendApi? Api;

		internal static EventBus? Bus;

		internal static string? Root;
	}

	private BridgeServer? _server;

	private int _disposed;

	public BridgeClient Client { get; }

	public int Port { get; }

	public string Root { get; }

	public string Backend { get; }

	public event EventHandler? Exited;

	private BridgeHost(BridgeClient client, BridgeServer server, int port, string root, string backend)
	{
		Client = client;
		_server = server;
		Port = port;
		Root = root;
		Backend = backend;
		server.Crashed += delegate
		{
			if (Volatile.Read(ref _disposed) == 0)
			{
				this.Exited?.Invoke(this, EventArgs.Empty);
			}
		};
	}

	public static async Task<BridgeHost> StartAsync(CancellationToken ct = default(CancellationToken))
	{
		string root = FindRoot();
		return await StartInProcessAsync(root, ct).ConfigureAwait(continueOnCapturedContext: false);
	}

	private static BackendApi EnsureCore(string root, out EventBus bus)
	{
		lock (CoreState.Gate)
		{
			if (CoreState.Api != null && CoreState.Bus != null)
			{
				bus = CoreState.Bus;
				return CoreState.Api;
			}
			Environment.SetEnvironmentVariable("TIPCL_HOME", root);
			Utils.SetRoot(root);
			Config.Reset();
			Guard.Install(PPath.Join(root, "tipcl-error.log"));
			string text = Py.Strip(Environment.GetEnvironmentVariable("TIPCL_LANG") ?? "").Replace("-", "_");
			if (text.Length == 0)
			{
				I18nCore.InitLanguage();
			}
			else
			{
				if (!I18nCore.AvailableLanguages().ContainsKey(text))
				{
					text = I18nCore.CurrentLanguage();
				}
				I18nCore.CurrentLang = text;
			}
			CoreState.Bus = new EventBus();
			CoreState.Api = new BackendApi(CoreState.Bus);
			CoreState.Root = root;
			TipCL.Core.Launcher.Ai.McpServer.StartIfEnabled(CoreState.Api);
			// 插件系统:后台发现并加载插件,不拖慢启动
			TipCL.Core.Plugins.PluginManager.StartGlobal(CoreState.Api);
			bus = CoreState.Bus;
			return CoreState.Api;
		}
	}

	private static async Task<BridgeHost> StartInProcessAsync(string root, CancellationToken ct)
	{
		string token = NewToken();
		BridgeServer srv = await Task.Run(delegate
		{
			EventBus bus;
			BridgeServer bridgeServer = new BridgeServer(EnsureCore(root, out bus), bus, token);
			bridgeServer.Start();
			return bridgeServer;
		}, ct).ConfigureAwait(continueOnCapturedContext: false);
		int port = srv.Port;
		string backend = I18n.L("C 桥");
		BridgeClient client = new BridgeClient(new Uri(ClientBaseUrl(port)), token);
		for (int i = 0; i < 60; i++)
		{
			try
			{
				if (await client.IsHealthyAsync(ct).ConfigureAwait(continueOnCapturedContext: false))
				{
					client.ConnectEvents();
					return new BridgeHost(client, srv, port, CoreState.Root ?? root, backend);
				}
			}
			catch
			{
			}
			await Task.Delay(100, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		client.Dispose();
		srv.Stop();
		throw new InvalidOperationException(I18n.L("桥已启动但 /health 无响应"));
	}

	/// <summary>
	/// 数据目录。Windows 版会先看 <c>TIPCL_HOME</c>,再从 BaseDirectory/CurrentDirectory/程序集目录往上找
	/// 「带 bridge\server.py 或 native\data\catalog.json」的 payload 根,最后落到 <c>Payload.EnsureRoot()</c>。
	/// mac 版没有 payload(外部 Python 桥已去),唯一的数据目录就是 GOAL 定的
	/// <c>Utils.ResolveRoot()</c>(<c>~/.tipcl</c>,<c>TIPCL_HOME</c> 可覆盖),所以这里直接对齐它。
	/// </summary>
	public static string FindRoot()
	{
		return NormalizeRoot(Utils.ResolveRoot());
	}

	/// <summary>客户端基址。与 Windows 版那句 <c>new Uri($"http://127.0.0.1:{port}/")</c> 逐字一致,
	/// 只是把回环地址取成 <see cref="BridgeServer.LOOPBACK_HOST"/>,便于单测钉住(见 C2a 用例)。</summary>
	internal static string ClientBaseUrl(int port)
	{
		return "http://" + BridgeServer.LOOPBACK_HOST + ":" + port + "/";
	}

	/// <summary>
	/// 规范化数据根:去掉结尾的分隔符(与 Windows 版同一套语义)。Windows 版还有一条
	/// 「<c>C:</c> 这种盘符要补回分隔符」的分支,mac 上没有盘符概念,该分支随外部 Python 桥一起去掉。
	/// 全是分隔符时原样返回(与 Windows 版 <c>text.Length == 0</c> 那条守卫等价,保住 Unix 的 <c>/</c>)。
	/// </summary>
	internal static string NormalizeRoot(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return path;
		}
		string text = path.TrimEnd('\\', '/');
		if (text.Length == 0)
		{
			return path;
		}
		return text;
	}

	/// <summary>32 字节随机数转 64 个十六进制字符(≥ BridgeServer 要求的 32 字符下限)。</summary>
	internal static string NewToken()
	{
		byte[] array = new byte[32];
		using (RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create())
		{
			randomNumberGenerator.GetBytes(array);
		}
		return BitConverter.ToString(array).Replace("-", "");
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}
		Client.Dispose();
		BridgeServer server = _server;
		_server = null;
		if (server != null)
		{
			try
			{
				server.Stop();
			}
			catch
			{
			}
		}
	}
}
