using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace TipCL.Services;

// Win7/8.1（以及没装“WebP 图像扩展”的 Win10）的 WIC 没有 WebP 解码器，
// Modrinth / CurseForge 的模组、整合包封面大多是 WebP，直接交给 BitmapImage 会抛异常，界面上就一张图都没有。
// 这里先让系统 WIC 解码；失败且确实是 WebP 时，用纯托管的 ImageSharp 转成 PNG 再交给 WPF。
public static class WebpFallback
{
	// 只注册 WebP 解码器：ImageSharp 2.x 已停更，已知漏洞集中在 TIFF/ICC/直方图等用不到的路径，这里不给它们任何入口。
	private static readonly Configuration WebpOnly = new Configuration(new WebpConfigurationModule());

	// RIFF....WEBP
	public static bool IsWebp(byte[]? data)
	{
		return data != null && data.Length >= 12
			&& data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46
			&& data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50;
	}

	// 把 WebP 转成 PNG 字节；不是 WebP 或转换失败时原样返回。
	public static byte[] ToPng(byte[] data)
	{
		if (!IsWebp(data))
		{
			return data;
		}
		try
		{
			using Image image = Image.Load(WebpOnly, data);
			using MemoryStream ms = new MemoryStream();
			image.Save(ms, new PngEncoder());
			return ms.ToArray();
		}
		catch
		{
			return data;
		}
	}

	// 先用系统解码，失败时若是 WebP 就转 PNG 再解一次。decode 需要自己负责异常以外的一切。
	public static T Decode<T>(byte[] data, Func<byte[], T> decode)
	{
		try
		{
			return decode(data);
		}
		catch when (IsWebp(data))
		{
			byte[] png = ToPng(data);
			if (ReferenceEquals(png, data))
			{
				throw;
			}
			return decode(png);
		}
	}
}
