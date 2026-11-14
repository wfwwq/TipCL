using System;
using System.Collections.Concurrent;
using System.Windows.Media.Imaging;

namespace TipCL.Services;

// 加载器官方图标。
// compact：不嵌图（体积红线），运行时经 Thumbs（后端 ensure_thumb → cache_dir/thumbs）下载并落盘缓存；
//          未下载 / 离线 / 失败时 ThumbTile 显示名称首字母方块。全程异步，异常在 Thumbs 内吞掉。
// premium（-p:Premium=true，交接必读 §2.5）：官方图标直接用嵌入资源 PremiumAssets\*，离线也有图，
//          不走运行时下载；资源缺失时仍回落首字母方块。
public static class LoaderIcons
{
#if !PREMIUM
	public static string? Url(string loader)
	{
		switch (loader.ToLowerInvariant())
		{
		case "fabric":
			return "https://github.com/FabricMC.png?size=64";
		case "forge":
			return "https://github.com/MinecraftForge.png?size=64";
		case "neoforge":
			return "https://github.com/neoforged.png?size=64";
		case "quilt":
			return "https://github.com/QuiltMC.png?size=64";
		case "":
		case "vanilla":
			return "https://minecraft.wiki/images/Grass_Block_JE7_BE6.png";
		default:
			return null;
		}
	}
#endif

	public static ThumbTile Tile(string loader, string name, double size = 36.0)
	{
		ThumbTile t = new ThumbTile(name, size, 9.0);
#if PREMIUM
		t.SetImage(Embedded(loader, (int)Clamp.Of(Math.Ceiling(size) * 2.0, 32.0, 96.0)));
#else
		t.SetUrl(Url(loader));
#endif
		return t;
	}

#if PREMIUM
	// 嵌入图标解码缓存：同图标多处复用（版本列表每行一张草方块），冻结后可跨线程共享
	private static readonly ConcurrentDictionary<string, BitmapSource?> _embedded = new ConcurrentDictionary<string, BitmapSource?>();

	private static BitmapSource? Embedded(string loader, int px)
	{
		string file;
		switch (loader.ToLowerInvariant())
		{
		case "fabric":
			file = "fabric_gh.png";
			break;
		case "forge":
			file = "forge_gh.png";
			break;
		case "neoforge":
			file = "neoforge_gh.png";
			break;
		case "quilt":
			file = "quilt_gh.png";
			break;
		case "":
		case "vanilla":
			file = "grass.png";
			break;
		default:
			return null;
		}
		return _embedded.GetOrAdd(file + "|" + px, delegate
		{
			try
			{
				BitmapImage bmp = new BitmapImage();
				bmp.BeginInit();
				bmp.CacheOption = BitmapCacheOption.OnLoad;
				bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
				bmp.DecodePixelWidth = px;
				bmp.UriSource = new Uri("pack://application:,,,/TipCL;component/PremiumAssets/" + file);
				bmp.EndInit();
				bmp.Freeze();
				return (BitmapSource?)bmp;
			}
			catch
			{
				return null;
			}
		});
	}
#endif
}
