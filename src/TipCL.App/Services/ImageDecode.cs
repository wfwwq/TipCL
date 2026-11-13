using System;
using System.IO;
using Avalonia.Media.Imaging;
using TipCL.Core.Platform;

namespace TipCL.Services;

/// <summary>
/// 把"一串图片字节"解成能挂到 Avalonia 控件上的位图。Windows 侧这件事散在各处
/// (<c>Wallpaper.LoadImage</c> / <c>Thumbs.DecodeAsync</c> 各自 <c>BitmapImage</c> + <c>WebpFallback</c>),
/// mac 侧收成一个口子:<b>统一走 Core 已有的平台编解码器</b>
/// (<see cref="PlatformServices.ImageCodec"/> = B1a 的 <c>SkiaImageCodec</c>,GOAL 定的"图片走 Skia,
/// WebP 也走 Skia"),解出来再交给 Avalonia 的 <see cref="Bitmap"/> 显示。
///
/// 坏数据一律**返回 null**(Skia 对认不出来的字节抛 <c>InvalidDataException</c>,这里吞掉):
/// Windows 那边也是这个结果 —— 认不出来 → <c>null</c> → 首字母方块 / <c>Error</c>,
/// 而不是把异常抛给界面。
///
/// <see cref="Decoder"/> 是注入缝:测试可以换掉"解码"这一层,不去真的解一张图。
/// </summary>
internal static class ImageDecode
{
	/// <summary>解码口(注入缝):字节 + 目标宽度(0 = 原尺寸)→ 位图;解不出来返回 null。</summary>
	internal delegate Bitmap? BitmapDecoder(byte[] data, int targetWidth);

	/// <summary>当前解码口(默认 = Core 的平台编解码器 + Avalonia 位图)。</summary>
	internal static BitmapDecoder Decoder { get; set; } = DefaultDecoder;

	/// <summary>解一次;任何异常都变成 null(与 Windows <c>catch { return null; }</c> 同)。</summary>
	internal static Bitmap? Decode(byte[]? data, int targetWidth)
	{
		if (data is null || data.Length == 0)
		{
			return null;
		}

		try
		{
			return Decoder(data, targetWidth);
		}
		catch (Exception)
		{
			return null;
		}
	}

	/// <summary>从文件读字节再解;文件不在 / 读不动 / 解不出来都是 null。</summary>
	internal static Bitmap? DecodeFile(string path, int targetWidth)
	{
		try
		{
			return Decode(File.ReadAllBytes(Path.GetFullPath(path)), targetWidth);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static Bitmap? DefaultDecoder(byte[] data, int targetWidth)
	{
		IImageCodec codec = PlatformServices.ImageCodec ?? new SkiaImageCodec();
		using IDecodedImage decoded = codec.Decode(data);
		IDecodedImage sized = decoded;
		if (targetWidth > 0 && decoded.Width > targetWidth)
		{
			int height = Math.Max(1, (int)Math.Round(decoded.Height * (double)targetWidth / decoded.Width));
			sized = decoded.Resize(targetWidth, height);
		}

		try
		{
			// Avalonia 的 Bitmap 只能从流/文件建;编码成 PNG 再给它这一步换来"解码只走 Core 的
			// SkiaImageCodec"这一条纪律(WebP / EXIF 方向都由它负责)。
			using MemoryStream stream = new MemoryStream(sized.EncodePng());
			return new Bitmap(stream);
		}
		finally
		{
			if (!ReferenceEquals(sized, decoded))
			{
				sized.Dispose();
			}
		}
	}
}
