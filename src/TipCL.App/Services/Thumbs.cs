using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace TipCL.Services;

/// <summary>
/// 缩略图解码 / 缓存(= Windows <c>Services\Thumbs.cs</c>)。公开面与 Windows 一一对应:
/// <c>Cached(url, px)</c> / <c>LoadAsync(url, px)</c>;内部那套也照搬 —— 8 路并发闸门、
/// 内存缓存 400 张触发裁剪到 300、失败记录 5 分钟冷却、同一个 key 的请求合并成一条 in-flight。
///
/// 与 Windows 的差异:
/// <list type="number">
/// <item><c>BitmapSource</c>(WPF)→ Avalonia 的 <see cref="Bitmap"/>,解码统一走
///   <see cref="ImageDecode"/>(Core 的 Skia 编解码器):Windows 那边还要 <c>WebpFallback</c> 兜
///   Win7 没有 WebP 解码器,Skia 直接能解,不需要;</item>
/// <item><b>路径来源做成注入缝</b> <see cref="Store"/>:Windows 的路径由后端给
///   (<c>thumb_path</c> / <c>ensure_thumb</c>),测试不能起后端(D-23),所以默认实现走后端,
///   测试注入 <see cref="LocalThumbStore"/>(根目录给 <c>TestHome.Root</c> 下的目录)
///   —— 这样"缓存命中 / 未命中 / 坏图回落"都能确定性地验,而且**绝不碰用户的图片**;</item>
/// <item><c>DecodePixelWidth = max(16, px*2)</c> 照抄(px 是瓦片边长,×2 是给 Retina 留的余量)。</item>
/// </list>
/// </summary>
public static class Thumbs
{
	/// <summary>同时进行的取图请求上限(Windows 的 <c>MaxParallel</c>)。</summary>
	private const int MaxParallel = 8;

	private static readonly ConcurrentDictionary<string, Bitmap> _mem = new ConcurrentDictionary<string, Bitmap>();

	private static readonly ConcurrentDictionary<string, DateTime> _failed = new ConcurrentDictionary<string, DateTime>();

	private static readonly ConcurrentDictionary<string, Task<Bitmap?>> _inflight = new ConcurrentDictionary<string, Task<Bitmap?>>();

	private static readonly SemaphoreSlim _gate = new SemaphoreSlim(MaxParallel, MaxParallel);

	/// <summary>失败冷却(Windows 的 <c>FailCooldown</c>:5 分钟)。</summary>
	private static readonly TimeSpan FailCooldown = TimeSpan.FromMinutes(5.0);

	/// <summary>取"这张图在本地是哪个文件"的口子(注入缝;默认走后端,见类注释差异 2)。</summary>
	internal interface IThumbStore
	{
		/// <summary>= 后端的 <c>thumb_path</c>:已经缓存过就给本地路径,没有就给空串。</summary>
		Task<string> CachedPathAsync(string url);

		/// <summary>= 后端的 <c>ensure_thumb</c>:让它去取一次,再给本地路径。</summary>
		Task<string> EnsurePathAsync(string url);
	}

	/// <summary>路径来源(null 不允许;测试与离线场景用 <see cref="LocalThumbStore"/>)。</summary>
	internal static IThumbStore Store { get; set; } = new BridgeThumbStore();

	/// <summary>本机缩略图缓存根:与后端 <c>cache_dir/thumbs</c> 同一处(<c>TIPCL_HOME</c> 生效时一起挪)。</summary>
	internal static string CacheRoot
	{
		get
		{
			try
			{
				return Path.Combine(BridgeHost.FindRoot(), "cache", "thumbs");
			}
			catch
			{
				return Path.Combine(AppContext.BaseDirectory, "cache", "thumbs");
			}
		}
	}

	/// <summary>内存里现在有几张(测试断言缓存命中)。</summary>
	internal static int MemoryCount => _mem.Count;

	/// <summary>失败记录里有几条(测试断言坏图进了冷却)。</summary>
	internal static int FailureCount => _failed.Count;

	/// <summary>清掉内存缓存与失败记录(测试夹具收尾;Windows 没有这个方法,进程里缓存活到结束)。</summary>
	internal static void Clear()
	{
		_mem.Clear();
		_failed.Clear();
	}

	/// <summary>缓存里有就直接给(= Windows <c>Cached</c>;没有就是 null)。</summary>
	public static Bitmap? Cached(string url, int px)
	{
		if (!string.IsNullOrEmpty(url))
		{
			return _mem.TryGetValue(Key(url, px), out Bitmap? found) ? found : null;
		}

		return null;
	}

	/// <summary>取一张图(= Windows <c>LoadAsync</c>;失败 / 离线 / 冷却期内都是 null)。</summary>
	public static Task<Bitmap?> LoadAsync(string url, int px)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return Task.FromResult<Bitmap?>(null);
		}

		string key = Key(url, px);
		if (_mem.TryGetValue(key, out Bitmap? cached))
		{
			return Task.FromResult<Bitmap?>(cached);
		}

		if (_failed.TryGetValue(url, out DateTime failedAt))
		{
			if (DateTime.UtcNow - failedAt < FailCooldown)
			{
				return Task.FromResult<Bitmap?>(null);
			}

			_failed.TryRemove(url, out DateTime _);
		}

		return _inflight.GetOrAdd(key, (string _) => FetchAsync(url, px, key));
	}

	private static string Key(string url, int px)
	{
		return url + "|" + px;
	}

	private static async Task<Bitmap?> FetchAsync(string url, int px, string key)
	{
		try
		{
			string path = await Store.CachedPathAsync(url) ?? "";
			Bitmap? bitmap = await DecodeAsync(path, px);
			if (bitmap is null)
			{
				await _gate.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
				try
				{
					path = await Store.EnsurePathAsync(url) ?? "";
				}
				finally
				{
					_gate.Release();
				}

				bitmap = await DecodeAsync(path, px);
			}

			if (bitmap is null)
			{
				_failed[url] = DateTime.UtcNow;
				return null;
			}

			_mem[key] = bitmap;
			if (_mem.Count > 400)
			{
				Trim();
			}

			return bitmap;
		}
		catch
		{
			// Windows: catch { _failed[url] = now; return null; } —— 取图和网络都不许把异常抛给界面。
			_failed[url] = DateTime.UtcNow;
			return null;
		}
		finally
		{
			_inflight.TryRemove(key, out Task<Bitmap?> _);
		}
	}

	private static Task<Bitmap?> DecodeAsync(string path, int px)
	{
		return Task.Run(delegate
		{
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			{
				return (Bitmap?)null;
			}

			try
			{
				return ImageDecode.DecodeFile(path, Math.Max(16, px * 2));
			}
			catch
			{
				return (Bitmap?)null;
			}
		});
	}

	/// <summary>裁剪:只留最近 300 张(Windows 的 <c>Trim</c> 逐字一致)。</summary>
	private static void Trim()
	{
		foreach (string key in _mem.Keys.Take(_mem.Count - 300).ToList())
		{
			_mem.TryRemove(key, out Bitmap? _);
		}
	}

	/// <summary>后端路径来源(Windows 里就是 <c>AppServices.Client.TryCallAsync("thumb_path"/"ensure_thumb")</c>)。</summary>
	private sealed class BridgeThumbStore : IThumbStore
	{
		public async Task<string> CachedPathAsync(string url)
		{
			return (await AppServices.Client.TryCallAsync("thumb_path", new { url }, "")) ?? "";
		}

		public async Task<string> EnsurePathAsync(string url)
		{
			return (await AppServices.Client.TryCallAsync("ensure_thumb", new { url }, "")) ?? "";
		}
	}

	/// <summary>
	/// 本机目录实现:把 url 映射成 <c>&lt;root&gt;\&lt;sha1(url)&gt;.img</c>。
	/// 给测试用(根目录给 <c>TestHome.Root</c> 下的目录,于是"缓存命中 / 未命中"都能确定性地验),
	/// 也给"后端还没起来时想从本机缓存兜一下"的场景用 —— 真实运行仍然走后端,
	/// 缓存目录口径与 Core 的 <c>cache_dir/thumbs</c> 一致(<c>TIPCL_HOME</c> 一起挪)。
	/// </summary>
	internal sealed class LocalThumbStore : IThumbStore
	{
		internal LocalThumbStore(string root)
		{
			Root = root ?? "";
		}

		/// <summary>这个 store 的根目录(测试断言它落在 TestHome.Root 之下)。</summary>
		internal string Root { get; }

		public Task<string> CachedPathAsync(string url)
		{
			string path = PathOf(url);
			return Task.FromResult((path.Length > 0 && File.Exists(path)) ? path : "");
		}

		public Task<string> EnsurePathAsync(string url)
		{
			return Task.FromResult(PathOf(url));
		}

		/// <summary>url → 本机路径(纯函数,不落盘)。</summary>
		internal string PathOf(string url)
		{
			if (Root.Length == 0 || string.IsNullOrWhiteSpace(url))
			{
				return "";
			}

			return Path.Combine(Root, Hash(url) + ".img");
		}

		private static string Hash(string text)
		{
			using System.Security.Cryptography.SHA1 sha = System.Security.Cryptography.SHA1.Create();
			byte[] digest = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
			return string.Concat(digest.Select((byte b) => b.ToString("x2")));
		}
	}
}
