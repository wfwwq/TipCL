using System;
using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace TipCL.Services;

/// <summary>
/// 加载器官方图标(= Windows <c>Services\LoaderIcons.cs</c>)。
///
/// Windows 侧有 compact / premium 两条分支(靠 <c>#if PREMIUM</c> 选):
/// compact 不嵌图,运行时经 <see cref="Thumbs"/> 下载;premium 用嵌入资源 <c>PremiumAssets\*</c>,
/// 离线也有图,资源缺失时回落首字母方块。
/// **GOAL 定了 mac 版"无体积红线,只出一个版本,等同 premium"**,所以本文件就是 Windows 的
/// premium 那一支(有 <c>Url</c> 的 compact 支不搬 —— 它在 Windows 里也只有 <c>Tile</c> 自己用),
/// 于是嵌入资源缺失 / 名字不认识时,走的就是 premium 的回落:
/// <see cref="Embedded"/> 返回 null → <c>t.SetImage(null)</c> → 首字母方块。
///
/// 逐项照 Windows 抄下来的:文件名表(fabric/forge/neoforge/quilt/vanilla→grass,其余 null)、
/// 解码宽度 <c>clamp(ceil(size)*2, 32, 96)</c>、按 <c>文件名|px</c> 缓存已解码位图(同一图标多处复用)。
///
/// mac 侧的差异:
/// <list type="number">
/// <item><c>pack://application:,,,/TipCL;component/PremiumAssets/</c> → <c>avares://TipCL/PremiumAssets/</c>
///   (Avalonia 的嵌入资源口径);</item>
/// <item><c>BitmapImage</c> + <c>Freeze()</c> → <see cref="ImageDecode"/>(Core 的 Skia 编解码器),
///   解码宽度由我们自己按比例缩放(Skia 那边没有 <c>DecodePixelWidth</c> 这种"边解边缩"的开关);</item>
/// <item>Avalonia 的位图没有 WPF 那种线程亲和性,不需要 <c>Freeze</c>。</item>
/// </list>
/// </summary>
public static class LoaderIcons
{
	/// <summary>嵌入图标解码缓存:同图标多处复用(版本列表每行一张草方块)。</summary>
	private static readonly ConcurrentDictionary<string, Bitmap?> _embedded = new ConcurrentDictionary<string, Bitmap?>();

	/// <summary>加载器名 → 嵌入文件名(【Windows 源】<c>LoaderIcons.cs:52-74</c> 的 <c>Embedded</c> 分支)。</summary>
	internal static string? FileFor(string loader)
	{
		switch (loader.ToLowerInvariant())
		{
		case "fabric":
			return "fabric_gh.png";
		case "forge":
			return "forge_gh.png";
		case "neoforge":
			return "neoforge_gh.png";
		case "quilt":
			return "quilt_gh.png";
		case "":
		case "vanilla":
			return "grass.png";
		default:
			return null;
		}
	}

	/// <summary>瓦片边长 → 解码宽度(【Windows 源】<c>LoaderIcons.cs:40</c> 的
	/// <c>Clamp.Of(Math.Ceiling(size) * 2.0, 32.0, 96.0)</c>)。</summary>
	internal static int PixelSize(double size)
	{
		return (int)Clamp.Of(Math.Ceiling(size) * 2.0, 32.0, 96.0);
	}

	/// <summary>嵌入图标的 avares URI(名字不认识也照拼,与 Windows 拼 pack URI 同形)。</summary>
	internal static string AssetUri(string file)
	{
		return "avares://TipCL/PremiumAssets/" + file;
	}

	/// <summary>
	/// 按加载器名给一张已解码的嵌入位图。名字不认识 / 资源缺失 / 解不出来 → null
	/// (【Windows 源】premium 分支的 <c>catch { return null; }</c>,调用方回落到首字母方块)。
	/// </summary>
	internal static Bitmap? Embedded(string loader, int px)
	{
		string? file = FileFor(loader);
		if (file is null)
		{
			return null;
		}

		return _embedded.GetOrAdd(file + "|" + px, delegate
		{
			try
			{
				using System.IO.Stream stream = AssetLoader.Open(new Uri(AssetUri(file)));
				using System.IO.MemoryStream buffer = new System.IO.MemoryStream();
				stream.CopyTo(buffer);
				return ImageDecode.Decode(buffer.ToArray(), px);
			}
			catch
			{
				return null;
			}
		});
	}

	/// <summary>这个加载器有没有嵌入图标(测试用:不真的解码)。</summary>
	internal static bool HasIcon(string loader)
	{
		return FileFor(loader) is not null;
	}

	/// <summary>清掉解码缓存(测试夹具收尾)。</summary>
	internal static void ClearCache()
	{
		_embedded.Clear();
	}

	/// <summary>
	/// 给一个加载器瓦片(Windows 同名方法;尺寸默认 36,圆角 9)。
	/// 名字不认识 → 没有嵌入图 → 瓦片上显示名字首字母。
	/// </summary>
	public static ThumbTile Tile(string loader, string name, double size = 36.0)
	{
		ThumbTile tile = new ThumbTile(name, size, 9.0);
		tile.SetImage(Embedded(loader, PixelSize(size)));
		return tile;
	}
}
