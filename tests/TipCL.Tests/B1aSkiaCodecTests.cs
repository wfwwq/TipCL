using System;
using System.IO;
using System.Linq;
using SkiaSharp;
using TipCL.Core.Platform;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B1a:<see cref="SkiaImageCodec"/> / <see cref="SkiaDecodedImage"/>。
///
/// 期望值来自 SkiaSharp 2.88.9 的实测行为(见 PROGRESS.md 的 B1a「Skia 与 WPF 语义对照表」),
/// 以及 Windows 的 <c>WpfImageCodec</c> / <c>WpfDecodedImage</c> 源码语义。
/// 图片字节全部在测试里用 Skia 现造(不联网,不读仓库外的文件,除了 webp 夹具)。
/// </summary>
public class B1aSkiaCodecTests
{
    private static readonly SkiaImageCodec Codec = new SkiaImageCodec();

    // ─────────────── 造图工具 ───────────────

    private static SKBitmap Paint(int width, int height, SKColorType colorType, SKAlphaType alphaType, bool transparent)
    {
        SKBitmap bitmap = new SKBitmap(new SKImageInfo(width, height, colorType, alphaType));
        using (SKCanvas canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(transparent ? SKColors.Transparent : SKColors.Red);
            using SKPaint paint = new SKPaint
            {
                Color = transparent ? SKColors.Blue.WithAlpha(128) : SKColors.Blue,
                IsAntialias = false
            };
            canvas.DrawRect(new SKRect(0f, 0f, Math.Max(1, width / 2f), Math.Max(1, height / 2f)), paint);
        }
        return bitmap;
    }

    private static byte[] EncodeImage(SKEncodedImageFormat format, int width, int height, SKColorType colorType, SKAlphaType alphaType, bool transparent, int quality = 100)
    {
        using SKBitmap bitmap = Paint(width, height, colorType, alphaType, transparent);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(format, quality);
        Assert.NotNull(data);
        return data.ToArray();
    }

    private static byte[] Png(bool transparent, int width = 8, int height = 6)
    {
        return EncodeImage(
            SKEncodedImageFormat.Png,
            width,
            height,
            transparent ? SKColorType.Bgra8888 : SKColorType.Rgb888x,
            transparent ? SKAlphaType.Premul : SKAlphaType.Opaque,
            transparent);
    }

    private static byte[] Jpeg(int width = 8, int height = 6)
    {
        // 实测:SkiaSharp 2.88.9 的 JPEG 编码器不吃 Rgb888x(返回 null),要用 N32
        return EncodeImage(SKEncodedImageFormat.Jpeg, width, height, SKColorType.Bgra8888, SKAlphaType.Premul, false, 90);
    }

    private static byte[] Webp(bool transparent, int width = 8, int height = 6)
    {
        return EncodeImage(
            SKEncodedImageFormat.Webp,
            width,
            height,
            transparent ? SKColorType.Rgba8888 : SKColorType.Rgb888x,
            transparent ? SKAlphaType.Unpremul : SKAlphaType.Opaque,
            transparent,
            90);
    }

    private static int PngIhdrColorType(byte[] png)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png.Take(8).ToArray());
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png, 12, 4));
        return png[25];
    }

    // ─────────────── 解码:尺寸与 HasAlpha ───────────────

    [Fact]
    public void Decode_null_throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Codec.Decode(null));
    }

    [Theory]
    [InlineData(8, 6)]
    [InlineData(1, 1)]
    [InlineData(37, 3)]
    public void Decode_png_without_an_alpha_channel_reports_size_and_no_alpha(int width, int height)
    {
        using IDecodedImage image = Codec.Decode(Png(transparent: false, width, height));

        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        Assert.False(image.HasAlpha);
    }

    [Fact]
    public void Decode_png_with_an_alpha_channel_has_alpha()
    {
        using IDecodedImage image = Codec.Decode(Png(transparent: true));

        Assert.Equal(8, image.Width);
        Assert.Equal(6, image.Height);
        Assert.True(image.HasAlpha);
    }

    /// <summary>
    /// 有色度通道但像素全不透明的 PNG:WPF 按「像素格式带 alpha 通道」判 → HasAlpha=true。
    /// Skia 报 Unpremul,结论一致(对照表的边角差异只出现在 RGB + tRNS 这种组合上)。
    /// </summary>
    [Fact]
    public void Decode_png_with_an_alpha_channel_but_opaque_pixels_still_has_alpha()
    {
        byte[] opaqueRgba = EncodeImage(SKEncodedImageFormat.Png, 4, 3, SKColorType.Bgra8888, SKAlphaType.Premul, false);

        Assert.Equal(6, PngIhdrColorType(opaqueRgba));   // 编码出来确实是 RGBA
        using IDecodedImage image = Codec.Decode(opaqueRgba);
        Assert.True(image.HasAlpha);
    }

    [Fact]
    public void Decode_jpeg_has_no_alpha()
    {
        using IDecodedImage image = Codec.Decode(Jpeg(11, 7));

        Assert.Equal(11, image.Width);
        Assert.Equal(7, image.Height);
        Assert.False(image.HasAlpha);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Decode_webp_reports_alpha_from_the_bitstream(bool transparent, bool expectedAlpha)
    {
        using IDecodedImage image = Codec.Decode(Webp(transparent, 5, 9));

        Assert.Equal(5, image.Width);
        Assert.Equal(9, image.Height);
        Assert.Equal(expectedAlpha, image.HasAlpha);
    }

    /// <summary>1x1 透明 GIF(WIC 在 Windows 上能解,GIF 也在 Images.SniffMime 的清单里)。</summary>
    [Fact]
    public void Decode_gif_with_transparency_works()
    {
        byte[] gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

        using IDecodedImage image = Codec.Decode(gif);

        Assert.Equal(1, image.Width);
        Assert.Equal(1, image.Height);
        Assert.True(image.HasAlpha);
    }

    [Fact]
    public void Decode_hand_built_bmp_works()
    {
        byte[] bmp = BuildOnePixelBmp();

        using IDecodedImage image = Codec.Decode(bmp);

        Assert.Equal(1, image.Width);
        Assert.Equal(1, image.Height);
        Assert.False(image.HasAlpha);
    }

    [Fact]
    public void Decode_reads_the_real_webp_fixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "webp-sample.webp");
        Assert.True(File.Exists(path), "夹具必须拷到输出目录: " + path);
        byte[] data = File.ReadAllBytes(path);
        Assert.Equal(4880, data.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(data, 0, 4));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(data, 8, 4));

        using IDecodedImage image = Codec.Decode(data);

        Assert.True(image.Width > 0);
        Assert.True(image.Height > 0);
        Assert.Equal(128, image.Width);    // libwebp test.webp 是 128x128
        Assert.Equal(128, image.Height);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 })]
    public void Decode_garbage_throws_InvalidDataException(byte[] data)
    {
        Assert.Throws<InvalidDataException>(() => Codec.Decode(data));
    }

    [Fact]
    public void Decode_a_truncated_png_throws_InvalidDataException()
    {
        byte[] png = Png(transparent: false, 16, 16);

        // 只留签名 + IHDR:Skia 认得出是 PNG,但解不出像素
        byte[] truncated = png.Take(33).ToArray();

        Assert.Throws<InvalidDataException>(() => Codec.Decode(truncated));
    }

    // ─────────────── Resize ───────────────

    [Fact]
    public void Resize_scales_and_keeps_the_alpha_flag()
    {
        using IDecodedImage source = Codec.Decode(Png(transparent: true, 200, 100));

        using IDecodedImage resized = source.Resize(50, 25);

        Assert.Equal(50, resized.Width);
        Assert.Equal(25, resized.Height);
        Assert.True(resized.HasAlpha);
    }

    [Fact]
    public void Resize_of_an_opaque_image_re_encodes_as_a_truecolor_png()
    {
        using IDecodedImage source = Codec.Decode(Png(transparent: false, 40, 20));

        using IDecodedImage resized = source.Resize(10, 5);

        Assert.False(resized.HasAlpha);
        Assert.Equal(2, PngIhdrColorType(resized.EncodePng()));   // 不带 alpha 通道 → color type 2(对齐 WPF 转 Bgr24)
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, -1)]
    public void Resize_rejects_non_positive_sizes(int width, int height)
    {
        using IDecodedImage source = Codec.Decode(Png(transparent: false, 4, 4));

        Assert.Throws<ArgumentException>(() => source.Resize(width, height));
    }

    // ─────────────── 编码 ───────────────

    [Fact]
    public void EncodePng_writes_a_png_that_decodes_back_to_the_same_size()
    {
        using IDecodedImage image = Codec.Decode(Png(transparent: true, 12, 9));

        byte[] encoded = image.EncodePng();

        Assert.Equal(6, PngIhdrColorType(encoded));
        using IDecodedImage again = Codec.Decode(encoded);
        Assert.Equal(12, again.Width);
        Assert.Equal(9, again.Height);
        Assert.True(again.HasAlpha);
    }

    [Fact]
    public void EncodePng_of_an_opaque_jpeg_writes_a_truecolor_png_without_alpha()
    {
        using IDecodedImage image = Codec.Decode(Jpeg(6, 4));

        byte[] encoded = image.EncodePng();

        Assert.Equal(2, PngIhdrColorType(encoded));
        using IDecodedImage again = Codec.Decode(encoded);
        Assert.False(again.HasAlpha);
    }

    [Fact]
    public void EncodeJpeg_writes_a_jpeg_that_decodes_back_to_the_same_size()
    {
        using IDecodedImage image = Codec.Decode(Png(transparent: false, 14, 10));

        byte[] encoded = image.EncodeJpeg(85);

        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, encoded.Take(3).ToArray());
        using IDecodedImage again = Codec.Decode(encoded);
        Assert.Equal(14, again.Width);
        Assert.Equal(10, again.Height);
        Assert.False(again.HasAlpha);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(1000, 100)]
    public void ClampQuality_is_1_to_100(int input, int expected)
    {
        Assert.Equal(expected, SkiaDecodedImage.ClampQuality(input));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void EncodeJpeg_accepts_out_of_range_quality_after_clamping(int quality)
    {
        using IDecodedImage image = Codec.Decode(Png(transparent: false, 8, 8));

        byte[] encoded = image.EncodeJpeg(quality);

        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, encoded.Take(3).ToArray());
    }

    [Fact]
    public void EncodeJpeg_of_an_image_with_alpha_does_not_throw()
    {
        // Images._shrink 只会对 HasAlpha=false 的图走 JPEG,这里只保证「不会因为透明像素炸掉」
        using IDecodedImage image = Codec.Decode(Png(transparent: true, 8, 8));

        byte[] encoded = image.EncodeJpeg(80);

        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, encoded.Take(3).ToArray());
        using IDecodedImage again = Codec.Decode(encoded);
        Assert.False(again.HasAlpha);
    }

    // ─────────────── EXIF 方向 ───────────────

    private static SKBitmap Gradient(int width, int height)
    {
        SKBitmap bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, new SKColor((byte)(10 + x), (byte)(100 + y * 10), 200, 255));
            }
        }
        return bitmap;
    }

    /// <summary>EXIF 方向 1..8 的定义:输出像素 (x,y) 取自源图的哪个像素。</summary>
    private static (int X, int Y) SourceOf(SKEncodedOrigin origin, int width, int height, int x, int y)
    {
        switch (origin)
        {
            case SKEncodedOrigin.TopLeft:
                return (x, y);
            case SKEncodedOrigin.TopRight:
                return (width - 1 - x, y);
            case SKEncodedOrigin.BottomRight:
                return (width - 1 - x, height - 1 - y);
            case SKEncodedOrigin.BottomLeft:
                return (x, height - 1 - y);
            case SKEncodedOrigin.LeftTop:
                return (y, x);
            case SKEncodedOrigin.RightTop:
                return (y, height - 1 - x);
            case SKEncodedOrigin.RightBottom:
                return (width - 1 - y, height - 1 - x);
            case SKEncodedOrigin.LeftBottom:
                return (width - 1 - y, x);
            default:
                return (x, y);
        }
    }

    [Theory]
    [InlineData(SKEncodedOrigin.TopLeft)]
    [InlineData(SKEncodedOrigin.TopRight)]
    [InlineData(SKEncodedOrigin.BottomRight)]
    [InlineData(SKEncodedOrigin.BottomLeft)]
    [InlineData(SKEncodedOrigin.LeftTop)]
    [InlineData(SKEncodedOrigin.RightTop)]
    [InlineData(SKEncodedOrigin.RightBottom)]
    [InlineData(SKEncodedOrigin.LeftBottom)]
    public void ApplyOrigin_maps_every_pixel_according_to_the_exif_definition(SKEncodedOrigin origin)
    {
        const int width = 5;
        const int height = 3;
        bool swapped = origin == SKEncodedOrigin.LeftTop
            || origin == SKEncodedOrigin.RightTop
            || origin == SKEncodedOrigin.RightBottom
            || origin == SKEncodedOrigin.LeftBottom;
        using SKBitmap source = Gradient(width, height);

        using SKBitmap result = SkiaDecodedImage.ApplyOrigin(source, origin);

        Assert.Equal(swapped ? height : width, result.Width);
        Assert.Equal(swapped ? width : height, result.Height);
        for (int y = 0; y < result.Height; y++)
        {
            for (int x = 0; x < result.Width; x++)
            {
                (int sx, int sy) = SourceOf(origin, width, height, x, y);
                Assert.Equal(source.GetPixel(sx, sy), result.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void ApplyOrigin_TopLeft_returns_the_same_instance()
    {
        using SKBitmap source = Gradient(3, 2);

        SKBitmap result = SkiaDecodedImage.ApplyOrigin(source, SKEncodedOrigin.TopLeft);

        Assert.Same(source, result);
    }

    /// <summary>
    /// 实测差异:SkiaSharp 2.88.9 <b>不认 PNG 的 eXIf 块</b>(恒 TopLeft),只认 JPEG 的 EXIF。
    /// 这条用例把「不认」这个事实钉住,免得以后升级 Skia 时悄悄变了没人发现。
    /// </summary>
    [Fact]
    public void Png_exif_chunk_is_not_honoured_by_SkiaSharp_2_88()
    {
        byte[] png = Png(transparent: false, 4, 2);
        byte[] tagged = InjectExifPng(png, 6);

        using IDecodedImage image = Codec.Decode(tagged);

        Assert.Equal(4, image.Width);
        Assert.Equal(2, image.Height);
    }

    // ─────────────── Dispose ───────────────

    [Fact]
    public void Dispose_is_idempotent()
    {
        IDecodedImage image = Codec.Decode(Png(transparent: true));

        image.Dispose();
        image.Dispose();
    }

    [Fact]
    public void HasAlpha_and_Size_are_still_readable_before_Dispose()
    {
        IDecodedImage image = Codec.Decode(Png(transparent: true, 3, 5));
        try
        {
            Assert.Equal(3, image.Width);
            Assert.Equal(5, image.Height);
            Assert.True(image.HasAlpha);
        }
        finally
        {
            image.Dispose();
        }
    }

    // ─────────────── 手工构造的图片 ───────────────

    private static byte[] BuildOnePixelBmp()
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream);
        writer.Write(new byte[] { 0x42, 0x4D });        // "BM"
        writer.Write(58);                               // 文件大小
        writer.Write(0);
        writer.Write(54);                               // 像素偏移
        writer.Write(40);                               // DIB 头大小
        writer.Write(1);                                // 宽
        writer.Write(1);                                // 高
        writer.Write((short)1);                         // planes
        writer.Write((short)24);                        // bpp
        writer.Write(0);                                // 压缩
        writer.Write(4);                                // 像素数据大小
        writer.Write(0x0B13);
        writer.Write(0x0B13);
        writer.Write(0);
        writer.Write(0);
        writer.Write(new byte[] { 0xFF, 0x00, 0x00, 0x00 });   // BGR + 补齐
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>把一段最小 TIFF(只有 Orientation 标签)作为 eXIf 块插进 PNG 的签名之后。</summary>
    private static byte[] InjectExifPng(byte[] png, ushort orientation)
    {
        byte[] payload = TiffWithOrientation(orientation);
        using MemoryStream stream = new MemoryStream();
        stream.Write(png, 0, 8);
        byte[] length = { (byte)(payload.Length >> 24), (byte)(payload.Length >> 16), (byte)(payload.Length >> 8), (byte)payload.Length };
        stream.Write(length, 0, 4);
        byte[] type = System.Text.Encoding.ASCII.GetBytes("eXIf");
        stream.Write(type, 0, 4);
        stream.Write(payload, 0, payload.Length);
        uint crc = Crc32(type, payload);
        stream.Write(new byte[] { (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc }, 0, 4);
        stream.Write(png, 8, png.Length - 8);
        return stream.ToArray();
    }

    private static byte[] TiffWithOrientation(ushort orientation)
    {
        return new byte[]
        {
            0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08,                // 大端,IFD0 在偏移 8
            0x00, 0x01,                                                   // 1 个条目
            0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01,                // tag 0x0112 SHORT count 1
            (byte)(orientation >> 8), (byte)(orientation & 0xFF), 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00                                        // 下一个 IFD
        };
    }

    private static uint Crc32(byte[] type, byte[] payload)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte value in type.Concat(payload))
        {
            crc ^= value;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }
        return crc ^ 0xFFFFFFFFu;
    }
}
