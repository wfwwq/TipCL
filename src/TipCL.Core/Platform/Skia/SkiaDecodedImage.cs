using System;
using SkiaSharp;

namespace TipCL.Core.Platform;

/// <summary>
/// <see cref="IDecodedImage"/> 的 Skia 实现(对齐 Windows 的 <c>WpfDecodedImage</c>,见 PROGRESS.md 的 B1a 对照表)。
///
/// 约定:解码后的位图一定落在「带 alpha 的 Bgra8888/Premul」或「不带 alpha 的 Rgb888x/Opaque」两种形态之一
/// (对齐 WPF 的 Pbgra32 / Bgr24),<see cref="HasAlpha"/> 与之一致。
/// </summary>
internal sealed class SkiaDecodedImage : IDecodedImage
{
	private SKBitmap _bmp;

	private readonly bool _alpha;

	internal SkiaDecodedImage(SKBitmap bitmap, bool alpha)
	{
		_bmp = bitmap;
		_alpha = alpha;
	}

	public int Width => _bmp.Width;

	public int Height => _bmp.Height;

	public bool HasAlpha => _alpha;

	/// <summary>
	/// 解码目标格式。实测(SkiaSharp 2.88.9)只能解到 N32:直接要求
	/// <c>Rgb888x/Opaque</c> 目标会得到 <c>SKCodecResult.InvalidConversion</c>,
	/// 所以统一解到 Bgra8888/Premul,再对「无 alpha 通道」的图做 <see cref="DropAlpha"/> 转换。
	/// </summary>
	internal static SKImageInfo DecodeInfo(int width, int height)
	{
		return new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
	}

	/// <summary>
	/// 该图是否带 alpha 通道。
	/// 实测:SkiaSharp 2.88.9 的 <c>SKCodec.Info.ColorType</c> 恒为 Bgra8888
	/// (PNG 的 RGB / RGBA 都报 Bgra8888),所以 alpha 信息只能从 <c>AlphaType</c> 拿:
	/// 不透明格式(JPEG / RGB PNG / 无 alpha 的 WebP)报 <c>Opaque</c>,
	/// 带 alpha 通道或 tRNS/透明调色板的报 <c>Unpremul</c>。
	/// 与 WPF 的 <c>DetectAlpha</c>(按像素格式判断)等价,边角差异见 PROGRESS.md 对照表。
	/// </summary>
	internal static bool HasAlphaChannel(SKImageInfo info)
	{
		if (info.ColorType == SKColorType.Alpha8)
		{
			return true;
		}
		return info.AlphaType != SKAlphaType.Opaque;
	}

	/// <summary>JPEG 质量钳制到 [1, 100](与 WPF 版的 <c>Math.Max(1, Math.Min(100, quality))</c> 一致)。</summary>
	internal static int ClampQuality(int quality)
	{
		return Math.Max(1, Math.Min(100, quality));
	}

	/// <summary>去掉 alpha 通道(Rgb888x/Opaque),对齐 WPF 的 <c>FormatConvertedBitmap(...Bgr24...)</c>。</summary>
	internal static SKBitmap DropAlpha(SKBitmap source)
	{
		SKBitmap copy = source.Copy(SKColorType.Rgb888x);
		if (copy == null)
		{
			// 转换不成就留着原图:宁可多一个 alpha 通道,也不要丢内容
			return source;
		}
		return copy;
	}

	/// <summary>
	/// 按 EXIF 方向把位图摆正,对齐 WPF 的 <c>WpfImageCodec.ApplyOrientation</c>
	/// (<c>TopLeft</c> 直接返回原实例,调用方据此判断要不要 Dispose 旧的)。
	/// 映射按「图像 90° 顺时针 = (x,y) → (h-1-y, x)」的像素矩形约定实现,单测逐像素对拍。
	/// </summary>
	internal static SKBitmap ApplyOrigin(SKBitmap source, SKEncodedOrigin origin)
	{
		bool known = origin is SKEncodedOrigin.TopRight
			|| origin is SKEncodedOrigin.BottomRight
			|| origin is SKEncodedOrigin.BottomLeft
			|| origin is SKEncodedOrigin.LeftTop
			|| origin is SKEncodedOrigin.RightTop
			|| origin is SKEncodedOrigin.RightBottom
			|| origin is SKEncodedOrigin.LeftBottom;
		if (!known)
		{
			return source;
		}
		int width = source.Width;
		int height = source.Height;
		bool swapped = origin is SKEncodedOrigin.LeftTop
			|| origin is SKEncodedOrigin.RightTop
			|| origin is SKEncodedOrigin.RightBottom
			|| origin is SKEncodedOrigin.LeftBottom;
		SKBitmap result = new SKBitmap(new SKImageInfo(swapped ? height : width, swapped ? width : height, source.ColorType, source.AlphaType));
		using SKCanvas canvas = new SKCanvas(result);
		switch (origin)
		{
		case SKEncodedOrigin.TopRight:
			canvas.Translate(width, 0f);
			canvas.Scale(-1f, 1f);
			break;
		case SKEncodedOrigin.BottomRight:
			canvas.Translate(width, height);
			canvas.RotateDegrees(180f);
			break;
		case SKEncodedOrigin.BottomLeft:
			canvas.Translate(0f, height);
			canvas.Scale(1f, -1f);
			break;
		case SKEncodedOrigin.LeftTop:
			canvas.RotateDegrees(90f);
			canvas.Scale(1f, -1f);
			break;
		case SKEncodedOrigin.RightTop:
			canvas.Translate(height, 0f);
			canvas.RotateDegrees(90f);
			break;
		case SKEncodedOrigin.RightBottom:
			canvas.Translate(height, width);
			canvas.RotateDegrees(270f);
			canvas.Scale(1f, -1f);
			break;
		case SKEncodedOrigin.LeftBottom:
			canvas.Translate(0f, width);
			canvas.RotateDegrees(270f);
			break;
		}
		canvas.DrawBitmap(source, 0f, 0f);
		return result;
	}

	public IDecodedImage Resize(int width, int height)
	{
		if (width <= 0 || height <= 0)
		{
			throw new ArgumentException("width/height must be positive");
		}
		SKBitmap resized = new SKBitmap(new SKImageInfo(width, height, _bmp.ColorType, _bmp.AlphaType));
		using (SKCanvas canvas = new SKCanvas(resized))
		{
			using SKPaint paint = new SKPaint
			{
				// 对应 WPF 的 BitmapScalingMode.HighQuality
				FilterQuality = SKFilterQuality.High
			};
			canvas.DrawBitmap(_bmp, new SKRect(0f, 0f, width, height), paint);
		}
		return new SkiaDecodedImage(resized, _alpha);
	}

	public byte[] EncodePng()
	{
		using SKImage image = SKImage.FromBitmap(_bmp);
		using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
		if (data == null)
		{
			throw new InvalidOperationException("Skia PNG encoder returned no data");
		}
		return data.ToArray();
	}

	/// <summary>
	/// JPEG。<paramref name="quality"/> 与 WPF 一样钳到 [1, 100];
	/// 实测 SkiaSharp 2.88.9 的 JPEG 编码器不吃 Rgb888x(返回 null),所以先转 Bgra8888。
	/// 透明像素不铺底色(WPF 转 Bgr24 同样不铺底色),具体取色规则见 PROGRESS.md 的对照表。
	/// </summary>
	public byte[] EncodeJpeg(int quality)
	{
		int q = ClampQuality(quality);
		SKBitmap source = _bmp;
		SKBitmap converted = null;
		if (source.ColorType != SKColorType.Bgra8888)
		{
			converted = source.Copy(SKColorType.Bgra8888);
			if (converted != null)
			{
				source = converted;
			}
		}
		try
		{
			using SKImage image = SKImage.FromBitmap(source);
			using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, q);
			if (data == null)
			{
				throw new InvalidOperationException("Skia JPEG encoder returned no data");
			}
			return data.ToArray();
		}
		finally
		{
			converted?.Dispose();
		}
	}

	public void Dispose()
	{
		_bmp?.Dispose();
		_bmp = null;
	}
}
