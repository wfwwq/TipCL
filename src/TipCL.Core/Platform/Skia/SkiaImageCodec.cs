using System;
using System.IO;
using SkiaSharp;

namespace TipCL.Core.Platform;

/// <summary>
/// <see cref="IImageCodec"/> 的 Skia 实现(对齐 Windows 的 <c>WpfImageCodec</c>,见 PROGRESS.md 的 B1a 对照表)。
///
/// 与 WPF 版的差别:
/// <list type="bullet">
/// <item>WebP 由 Skia 直接解码,不需要 <c>WebpFallback</c>(那是 Windows 7 上没有 WIC WebP 编解码器的补丁)。</item>
/// <item>EXIF 方向:JPEG 的 EXIF 生效(<c>SKCodec.EncodedOrigin</c>);<b>PNG 的 eXIf 块在 SkiaSharp 2.88.9 上不生效</b>(实测恒为 TopLeft)。</item>
/// <item>没有 STA 线程那套(WPF 的 <c>BitmapSource</c> 有线程亲和性,Skia 位图没有,也就不需要 <c>RunSta</c>)。</item>
/// </list>
/// </summary>
public sealed class SkiaImageCodec : IImageCodec
{
	public IDecodedImage Decode(byte[] data)
	{
		if (data == null)
		{
			throw new ArgumentNullException("data");
		}
		using SKMemoryStream stream = new SKMemoryStream(data);
		using SKCodec codec = SKCodec.Create(stream);
		if (codec == null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
		{
			// 与 WPF 版同一条消息(BitmapDecoder 认不出来时抛的就是它)
			throw new InvalidDataException("cannot identify image file");
		}
		SKImageInfo target = SkiaDecodedImage.DecodeInfo(codec.Info.Width, codec.Info.Height);
		bool alpha = SkiaDecodedImage.HasAlphaChannel(codec.Info);
		SKBitmap bitmap = new SKBitmap(target);
		SKCodecResult result = codec.GetPixels(target, bitmap.GetPixels());
		if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
		{
			bitmap.Dispose();
			throw new InvalidDataException("cannot decode image data (" + result + ")");
		}
		SKBitmap oriented = SkiaDecodedImage.ApplyOrigin(bitmap, codec.EncodedOrigin);
		if (!ReferenceEquals(oriented, bitmap))
		{
			bitmap.Dispose();
			bitmap = oriented;
		}
		if (!alpha)
		{
			SKBitmap opaque = SkiaDecodedImage.DropAlpha(bitmap);
			if (!ReferenceEquals(opaque, bitmap))
			{
				bitmap.Dispose();
				bitmap = opaque;
			}
		}
		return new SkiaDecodedImage(bitmap, alpha);
	}
}
