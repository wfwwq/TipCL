using System;
using System.IO;
using System.Linq;
using SkiaSharp;
using TipCL.Core.Launcher.Ai;
using TipCL.Core.Platform;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B1a:<c>Launcher\Ai\Images</c> 接上真编解码器后的行为(接之前 <c>PlatformServices.ImageCodec</c> 是 null,
/// <c>_shrink</c> 直接原样返回)。
///
/// 红线:改动全局静态的用例必须在 finally 里恢复原值;所有落盘都在 <see cref="TestHome.Root"/> 下。
/// </summary>
public class B1aImagesCodecTests
{
    private static byte[] MakePng(int width, int height, bool transparent)
    {
        SKBitmap bitmap = new SKBitmap(new SKImageInfo(
            width,
            height,
            transparent ? SKColorType.Bgra8888 : SKColorType.Rgb888x,
            transparent ? SKAlphaType.Premul : SKAlphaType.Opaque));
        using (SKCanvas canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(transparent ? SKColors.Transparent : SKColors.Green);
            using SKPaint paint = new SKPaint { Color = SKColors.Blue, IsAntialias = false };
            canvas.DrawRect(new SKRect(0f, 0f, width, height / 2f), paint);
        }
        using (bitmap)
        using (SKImage image = SKImage.FromBitmap(bitmap))
        using (SKData data = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            return data.ToArray();
        }
    }

    private static byte[] Payload(string dataUrl)
    {
        int index = dataUrl.IndexOf("base64,", StringComparison.Ordinal);
        Assert.True(index > 0, "不是 data URL: " + dataUrl.Substring(0, Math.Min(40, dataUrl.Length)));
        return Convert.FromBase64String(dataUrl.Substring(index + "base64,".Length));
    }

    [Fact]
    public void EncodeBytes_shrinks_an_opaque_image_to_the_max_edge()
    {
        IImageCodec previous = PlatformServices.ImageCodec;
        try
        {
            PlatformServices.ImageCodec = new SkiaImageCodec();

            string url = Images.EncodeBytes(MakePng(400, 200, transparent: false), "image/png", 64, 8L * 1024 * 1024);

            // 不透明 → 走 JPEG 阶梯(Images.cs 102-121 行)
            Assert.StartsWith("data:image/jpeg;base64,", url, StringComparison.Ordinal);
            using IDecodedImage decoded = new SkiaImageCodec().Decode(Payload(url));
            Assert.Equal(64, decoded.Width);
            Assert.Equal(32, decoded.Height);
            Assert.True(Math.Max(decoded.Width, decoded.Height) <= 64);
        }
        finally
        {
            PlatformServices.ImageCodec = previous;
        }
    }

    [Fact]
    public void EncodeBytes_keeps_the_png_path_for_images_with_alpha()
    {
        IImageCodec previous = PlatformServices.ImageCodec;
        try
        {
            PlatformServices.ImageCodec = new SkiaImageCodec();

            string url = Images.EncodeBytes(MakePng(400, 200, transparent: true), "image/png", 64, 8L * 1024 * 1024);

            Assert.StartsWith("data:image/png;base64,", url, StringComparison.Ordinal);
            using IDecodedImage decoded = new SkiaImageCodec().Decode(Payload(url));
            Assert.Equal(64, decoded.Width);
            Assert.Equal(32, decoded.Height);
            Assert.True(decoded.HasAlpha);
        }
        finally
        {
            PlatformServices.ImageCodec = previous;
        }
    }

    [Fact]
    public void EncodeBytes_returns_the_original_bytes_when_no_codec_is_installed()
    {
        IImageCodec previous = PlatformServices.ImageCodec;
        try
        {
            PlatformServices.ImageCodec = null;
            byte[] png = MakePng(120, 60, transparent: false);

            string url = Images.EncodeBytes(png, "image/png", 32, 8L * 1024 * 1024);

            Assert.StartsWith("data:image/png;base64,", url, StringComparison.Ordinal);
            Assert.Equal(png, Payload(url));
        }
        finally
        {
            PlatformServices.ImageCodec = previous;
        }
    }

    [Fact]
    public void EncodeBytes_rejects_something_that_is_not_an_image()
    {
        Assert.Throws<ImageError>(() => Images.EncodeBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, null, 1568, 4194304L));
    }

    [Fact]
    public void SniffMime_recognises_the_formats_the_codec_decodes()
    {
        Assert.Equal("image/png", Images.SniffMime(MakePng(2, 2, transparent: false)));

        using IDecodedImage gif = new SkiaImageCodec().Decode(Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7"));
        Assert.Equal(1, gif.Width);
    }

    [Fact]
    public void The_codec_can_be_replaced_without_touching_the_rest_of_the_pipeline()
    {
        IImageCodec previous = PlatformServices.ImageCodec;
        try
        {
            PlatformServices.ImageCodec = new SkiaImageCodec();
            Assert.Same(PlatformServices.ImageCodec, PlatformServices.ImageCodec);

            byte[] png = MakePng(30, 30, transparent: true);
            string url = Images.EncodeBytes(png, "image/png", 0, 4L * 1024 * 1024);

            Assert.StartsWith("data:image/png;base64,", url, StringComparison.Ordinal);
            Assert.True(Directory.Exists(TestHome.Root));
        }
        finally
        {
            PlatformServices.ImageCodec = previous;
        }
    }
}
