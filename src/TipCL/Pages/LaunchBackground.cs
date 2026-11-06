using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace TipCL.Pages;

// 启动页 v2 背景：取游戏目录 screenshots 里最新一张截图（不嵌任何图片进 exe）。
// 全部 IO / 解码在线程池完成，任何异常静默返回 null（回退到页面原有渐变底）。
internal static class LaunchBackground
{
	private static readonly string[] Exts = new string[3] { ".png", ".jpg", ".jpeg" };

	/// <summary>在候选目录里找最新截图；返回 (路径, 修改时间 ticks)，没有则 null。</summary>
	public static Task<Tuple<string, long>?> FindLatestAsync(IEnumerable<string> dirs)
	{
		return Task.Run(delegate
		{
			Tuple<string, long>? best = null;
			try
			{
				foreach (string dir in dirs)
				{
					if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
					{
						continue;
					}
					foreach (string file in Directory.EnumerateFiles(dir))
					{
						string ext = Path.GetExtension(file).ToLowerInvariant();
						if (Array.IndexOf(Exts, ext) < 0)
						{
							continue;
						}
						long t = File.GetLastWriteTimeUtc(file).Ticks;
						if (best == null || t > best.Item2)
						{
							best = Tuple.Create(file, t);
						}
					}
				}
			}
			catch
			{
			}
			return best;
		});
	}

	/// <summary>解码为宽度不超过 maxWidth 的冻结位图（OnLoad，不锁文件）；失败返回 null。</summary>
	public static Task<BitmapSource?> DecodeAsync(string path, int maxWidth)
	{
		return Task.Run(delegate
		{
			try
			{
				using FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
				// 先探原图宽度，只缩不放
				int srcWidth = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0].PixelWidth;
				fs.Position = 0L;
				BitmapImage bmp = new BitmapImage();
				bmp.BeginInit();
				bmp.CacheOption = BitmapCacheOption.OnLoad;
				bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
				if (srcWidth > maxWidth)
				{
					bmp.DecodePixelWidth = maxWidth;
				}
				bmp.StreamSource = fs;
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
}
