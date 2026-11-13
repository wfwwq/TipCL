using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace TipCL.Services;

/// <summary>
/// 后端启动阶段(C2d 新增)。主线只有这一条:
/// <c>NotStarted</c> -> <c>Starting</c> -> <c>Ready</c> -> <c>Stopped</c>;
/// 启动失败落在 <c>Failed</c>(此时 <see cref="BackendBootstrap.Error"/> 里有原因,
/// <see cref="BackendBootstrap.Host"/> 是 null)。
/// </summary>
public enum BridgePhase
{
	NotStarted,
	Starting,
	Ready,
	Stopped,
	Failed
}

/// <summary>
/// 进程内后端的启动器与状态机(C2d 新增)。把 Windows `MainWindow.ConnectAsync` 里
/// 「起后端 -> 记下 host/client -> 打开 BridgeReady 总闸」那一段的前半截抽出来,
/// 好让"后端到底有没有真起来"这件事能被单独验证:
/// <list type="bullet">
/// <item>阶段是同步可读的,测试可以在启动途中观察 <c>Starting</c>;</item>
/// <item>启动失败**不抛**给调用方,而是记进 <see cref="Error"/> 并保持"没就绪"
/// (绝不静默变成 ready);</item>
/// <item><see cref="Stop"/> 幂等,顺序固定:先取消在飞的启动,再 Dispose 宿主
/// (关客户端 => 取消事件流循环;停监听 => 端口释放)。</item>
/// </list>
/// 真正造内核的地方是 <see cref="BridgeHost"/> 的两个注入缝(`ApiFactory` / `OnCoreStarted`),
/// 生产路径永远是它们的默认值。
/// </summary>
public sealed class BackendBootstrap : IDisposable
{
	private readonly object _gate = new object();

	private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

	private BridgeHost? _host;

	private CancellationTokenSource? _starting;

	private int _disposed;

	/// <summary>当前阶段(默认"没启动过")。</summary>
	public BridgePhase Phase { get; private set; } = BridgePhase.NotStarted;

	/// <summary>最近一次启动失败的原因(null = 没失败过,或者已经成功启动)。</summary>
	public Exception? Error { get; private set; }

	/// <summary>已就绪的宿主(null = 还没起来)。</summary>
	public BridgeHost? Host
	{
		get
		{
			lock (_gate)
			{
				return _host;
			}
		}
	}

	/// <summary>监听端口(没起来时是 0;生产用 0 让系统挑空闲端口)。</summary>
	public int Port
	{
		get
		{
			BridgeHost? host = Host;
			return (host == null) ? 0 : host.Port;
		}
	}

	/// <summary>数据根(没起来时是空串)。</summary>
	public string Root
	{
		get
		{
			BridgeHost? host = Host;
			return (host == null) ? "" : host.Root;
		}
	}

	/// <summary>是不是已经就绪。</summary>
	public bool IsReady => Phase == BridgePhase.Ready;

	/// <summary>
	/// 启动一次。成功返回宿主;失败返回 null 并把原因记进 <see cref="Error"/>。
	/// 只有"重复启动"(正在启动 / 已经启动)与"已经 Dispose"这两种调用错误会抛异常 ——
	/// 启动本身的失败一律走返回值,好让调用方走诚实失败路径。
	/// </summary>
	public async Task<BridgeHost?> StartAsync(CancellationToken ct = default(CancellationToken))
	{
		CancellationTokenSource cts;
		lock (_gate)
		{
			if (Volatile.Read(ref _disposed) != 0)
			{
				throw new ObjectDisposedException(nameof(BackendBootstrap));
			}
			if (Phase == BridgePhase.Starting)
			{
				throw new InvalidOperationException(I18n.L("后端正在启动中"));
			}
			if (Phase == BridgePhase.Ready)
			{
				throw new InvalidOperationException(I18n.L("后端已经启动了"));
			}
			Phase = BridgePhase.Starting;
			Error = null;
			cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
			_starting = cts;
		}

		try
		{
			BridgeHost host = await BridgeHost.StartAsync(cts.Token).ConfigureAwait(continueOnCapturedContext: false);
			bool accepted;
			lock (_gate)
			{
				accepted = Phase == BridgePhase.Starting;
				if (accepted)
				{
					_host = host;
					Phase = BridgePhase.Ready;
				}
			}
			if (accepted)
			{
				return host;
			}
			// 启动途中被 Stop / Dispose:刚起来的后端立刻收掉,绝不留监听。
			try
			{
				host.Dispose();
			}
			catch (Exception ex)
			{
				Debug.WriteLine("[bridge] 启动被取消后收摊失败: " + ex);
			}
			return null;
		}
		catch (Exception ex)
		{
			lock (_gate)
			{
				_host = null;
				Error = ex;
				if (Phase == BridgePhase.Starting)
				{
					Phase = BridgePhase.Failed;
				}
			}
			// 诚实失败:原因写进调试输出,窗口的 BridgeReady 保持 false。
			Debug.WriteLine("[bridge] 后端启动失败: " + ex);
			return null;
		}
		finally
		{
			lock (_gate)
			{
				if (ReferenceEquals(_starting, cts))
				{
					_starting = null;
				}
			}
			cts.Dispose();
		}
	}

	/// <summary>
	/// 收摊:取消在飞的启动 + Dispose 宿主(关客户端 => 取消事件流循环;停监听 => 端口释放)。
	/// 幂等;从没启动过时阶段保持 <see cref="BridgePhase.NotStarted"/> 不变。
	/// </summary>
	public void Stop()
	{
		CancellationTokenSource? starting;
		BridgeHost? host;
		lock (_gate)
		{
			starting = _starting;
			host = _host;
			_host = null;
			if (Phase != BridgePhase.NotStarted)
			{
				Phase = BridgePhase.Stopped;
			}
		}

		try
		{
			starting?.Cancel();
		}
		catch (Exception)
		{
		}

		if (host != null)
		{
			try
			{
				host.Dispose();
			}
			catch (Exception ex)
			{
				Debug.WriteLine("[bridge] 后端收摊失败: " + ex);
			}
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}
		try
		{
			_lifetime.Cancel();
		}
		catch (Exception)
		{
		}
		Stop();
		_lifetime.Dispose();
	}
}
