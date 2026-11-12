using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;

namespace TipCL.Packager;

/// <summary>.ico 目录里的一个图像条目(诊断用)。</summary>
internal sealed class IcoImageInfo
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int BytesInRes { get; init; }

    public required int Offset { get; init; }

    /// <summary>数据开头那 4 个字节是不是 PNG 签名(本机的 .ico 里 7 个条目全是 PNG)。</summary>
    public required bool IsPng { get; init; }
}

/// <summary>.icns 的组装结果(含图标源的诊断信息)。</summary>
internal sealed class IconBuildResult
{
    public required int IcoBytes { get; init; }

    public required IReadOnlyList<IcoImageInfo> IcoImages { get; init; }

    /// <summary>SkiaSharp 解出来的尺寸(它是 .ico 里最大的那张)。</summary>
    public required int SourceWidth { get; init; }

    public required int SourceHeight { get; init; }

    public required string SourceColorType { get; init; }

    public required string SourceAlphaType { get; init; }

    public required IReadOnlyList<IcnsSlot> Slots { get; init; }

    /// <summary>源图小于 128 px 时这里是空数组(调用方必须停手,不许放大)。</summary>
    public required byte[] IcnsBytes { get; init; }
}

/// <summary>
/// 从 `.ico` 生成 `.icns`。这一段**不是纯函数**(要 SkiaSharp 解码、缩放、编 PNG),
/// 所以放在单独文件里,由 <c>Program</c> 调用;纯逻辑(尺寸选择 <see cref="IcnsWriter.SelectSlots"/>、
/// icns 字节组装 <see cref="IcnsWriter.Build"/>、ico 目录解析 <see cref="IcoSizes"/>)都留在
/// <see cref="IcnsWriter"/> 与这里导出成可单测的静态方法。
///
/// `.ico` 目录的结构是**读字节得出的**(2026-11-12,`packaging/TipCL.ico`,94953 字节):
/// 前 6 字节 = reserved(uint16) + type(uint16, 1 = 图标) + count(uint16),之后 count 个 16 字节条目:
/// 宽 / 高 / 调色板数 / 保留(各 1 字节,宽高为 0 表示 256)、planes(uint16)、bitCount(uint16)、
/// 数据长度(uint32 LE)、数据偏移(uint32 LE)。本机这个 .ico 的 7 个条目是
/// 16 / 24 / 32 / 48 / 64 / 128 / 256,全部是 PNG 数据(偏移处的头是 89 50 4E 47)。
/// </summary>
internal static class IconBuilder
{
    private const int IcoHeaderSize = 6;

    private const int IcoEntrySize = 16;

    /// <summary>按上面的结构读 `count` 个条目的宽高(纯函数)。结构不对就抛异常。</summary>
    public static IReadOnlyList<IcoImageInfo> IcoSizes(byte[] ico)
    {
        if (ico.Length < IcoHeaderSize)
        {
            throw new ArgumentException("ico shorter than 6 bytes");
        }

        int count = ico[4] | (ico[5] << 8);
        List<IcoImageInfo> result = new List<IcoImageInfo>(count);
        for (int i = 0; i < count; i++)
        {
            int o = IcoHeaderSize + i * IcoEntrySize;
            if (o + IcoEntrySize > ico.Length)
            {
                throw new ArgumentException("ico directory entry " + i + " is truncated");
            }

            int w = ico[o] == 0 ? 256 : ico[o];
            int h = ico[o + 1] == 0 ? 256 : ico[o + 1];
            int bytes = ico[o + 8] | (ico[o + 9] << 8) | (ico[o + 10] << 16) | (ico[o + 11] << 24);
            int off = ico[o + 12] | (ico[o + 13] << 8) | (ico[o + 14] << 16) | (ico[o + 15] << 24);
            bool isPng = off >= 0
                && off + 4 <= ico.Length
                && ico[off] == 0x89
                && ico[off + 1] == 0x50
                && ico[off + 2] == 0x4E
                && ico[off + 3] == 0x47;

            result.Add(new IcoImageInfo
            {
                Width = w,
                Height = h,
                BytesInRes = bytes,
                Offset = off,
                IsPng = isPng,
            });
        }

        return result;
    }

    /// <summary>解 .ico → 按 <see cref="IcnsWriter.SelectSlots"/> 的规格缩放出 PNG → 组装 icns 字节。</summary>
    public static IconBuildResult Build(string icoPath)
    {
        byte[] ico = File.ReadAllBytes(icoPath);
        IReadOnlyList<IcoImageInfo> images = IcoSizes(ico);

        using SKCodec codec = SKCodec.Create(icoPath)
            ?? throw new InvalidOperationException("SkiaSharp could not open the icon: " + icoPath);

        SKImageInfo info = codec.Info;
        using SKBitmap bitmap = new SKBitmap(info);
        SKCodecResult decode = codec.GetPixels(info, bitmap.GetPixels());
        if (decode != SKCodecResult.Success)
        {
            throw new InvalidOperationException("SkiaSharp decode failed: " + decode);
        }

        int largest = Math.Max(info.Width, info.Height);
        IReadOnlyList<IcnsSlot> slots = IcnsWriter.SelectSlots(largest);
        List<IcnsElement> elements = new List<IcnsElement>(slots.Count);
        foreach (IcnsSlot slot in slots)
        {
            byte[] png;
            if (slot.PixelSize == info.Width && slot.PixelSize == info.Height)
            {
                png = EncodePng(bitmap);
            }
            else
            {
                using SKBitmap scaled = new SKBitmap(
                    new SKImageInfo(slot.PixelSize, slot.PixelSize, info.ColorType, info.AlphaType));
                if (!bitmap.ScalePixels(scaled, SKFilterQuality.High))
                {
                    throw new InvalidOperationException("SkiaSharp scale failed: " + slot.PixelSize);
                }

                png = EncodePng(scaled);
            }

            elements.Add(new IcnsElement { Type = slot.Type, Png = png });
        }

        return new IconBuildResult
        {
            IcoBytes = ico.Length,
            IcoImages = images,
            SourceWidth = info.Width,
            SourceHeight = info.Height,
            SourceColorType = info.ColorType.ToString(),
            SourceAlphaType = info.AlphaType.ToString(),
            Slots = slots,
            IcnsBytes = elements.Count == 0 ? Array.Empty<byte>() : IcnsWriter.Build(elements),
        };
    }

    private static byte[] EncodePng(SKBitmap bitmap)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("SkiaSharp PNG encode returned null");
        return data.ToArray();
    }
}
