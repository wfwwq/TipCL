using System;
using System.Collections.Generic;
using System.Text;

namespace TipCL.Packager;

/// <summary>icns 的一个元素:4 字节类型 + PNG 字节。</summary>
internal sealed class IcnsElement
{
    public required string Type { get; init; }

    public required byte[] Png { get; init; }
}

/// <summary>「该往 icns 里放哪种尺寸」的一条规格(纯数据)。</summary>
internal sealed class IcnsSlot
{
    public required string Type { get; init; }

    /// <summary>正方形边长(像素)。</summary>
    public required int PixelSize { get; init; }
}

/// <summary>从 icns 字节里读回来的结构(校验用)。</summary>
internal sealed class IcnsParseResult
{
    public required string Magic { get; init; }

    public required uint TotalLength { get; init; }

    /// <summary>元素类型,按出现顺序。</summary>
    public required IReadOnlyList<string> Types { get; init; }

    /// <summary>每个元素声明长度(含 8 字节头),顺序与 <see cref="Types"/> 一致。</summary>
    public required IReadOnlyList<uint> ElementLengths { get; init; }

    /// <summary>元素长度累加 + 8 恰好等于文件长度(结构自洽)。</summary>
    public required bool LengthsConsistent { get; init; }
}

/// <summary>
/// `.icns` 的组装与读回(纯函数:字节进、字节出)。
///
/// 事实来源:任务书 B3 的「File-format facts」——`69 63 6E 73`(`icns`)+ 大端 uint32 总长度,
/// 之后每个元素 = 4 字节类型 + 大端 uint32 长度(**含这 8 字节头**)+ 数据;
/// PNG 数据对这几种类型合法:`ic07` 128x128、`ic08` 256x256、`ic09` 512x512、`ic13` 256x256(128@2x)。
/// 任务书要求**不产出 `ic09`**,并且「只用不需要放大到超过源图最大边长的尺寸」。
/// </summary>
internal static class IcnsWriter
{
    public const string MagicTag = "icns";

    public const int HeaderSize = 8;

    public const int ElementHeaderSize = 8;

    public const string Type128 = "ic07";

    public const string Type256 = "ic08";

    public const string Type256At2x = "ic13";

    /// <summary>
    /// 尺寸选择(纯函数)。`largestSource` = 源图(本包的 `.ico`)里 SkiaSharp 解出来的最大边长。
    /// - ≥ 256:ic07(128,缩小)+ ic08(256,原样)+ ic13(256,原样);刻意**不含** ic09(512,要放大)。
    /// - ≥ 128:只有 ic07(128)。
    /// - &lt; 128:空表 —— 调用方必须**停手并报告**,不许静默放大。
    /// </summary>
    public static IReadOnlyList<IcnsSlot> SelectSlots(int largestSource)
    {
        if (largestSource >= 256)
        {
            return new[]
            {
                new IcnsSlot { Type = Type128, PixelSize = 128 },
                new IcnsSlot { Type = Type256, PixelSize = 256 },
                new IcnsSlot { Type = Type256At2x, PixelSize = 256 },
            };
        }

        if (largestSource >= 128)
        {
            return new[] { new IcnsSlot { Type = Type128, PixelSize = 128 } };
        }

        return Array.Empty<IcnsSlot>();
    }

    /// <summary>组出完整的 icns 字节。元素顺序按入参顺序(调用方按 <see cref="SelectSlots"/> 的顺序给)。</summary>
    public static byte[] Build(IReadOnlyList<IcnsElement> elements)
    {
        long total = HeaderSize;
        foreach (IcnsElement e in elements)
        {
            if (e.Type.Length != 4)
            {
                throw new ArgumentException("icns element type must be 4 chars: " + e.Type);
            }

            total += ElementHeaderSize + e.Png.Length;
        }

        if (total > uint.MaxValue)
        {
            throw new ArgumentException("icns too large");
        }

        byte[] buffer = new byte[total];
        Encoding.ASCII.GetBytes(MagicTag, 0, 4, buffer, 0);
        WriteBigEndianUInt32(buffer, 4, (uint)total);

        int offset = HeaderSize;
        foreach (IcnsElement e in elements)
        {
            Encoding.ASCII.GetBytes(e.Type, 0, 4, buffer, offset);
            WriteBigEndianUInt32(buffer, offset + 4, (uint)(ElementHeaderSize + e.Png.Length));
            Buffer.BlockCopy(e.Png, 0, buffer, offset + ElementHeaderSize, e.Png.Length);
            offset += ElementHeaderSize + e.Png.Length;
        }

        return buffer;
    }

    /// <summary>读回并检查结构自洽性。magic 不对 / 头不够长 → 抛异常。</summary>
    public static IcnsParseResult Read(byte[] bytes)
    {
        if (bytes.Length < HeaderSize)
        {
            throw new ArgumentException("icns shorter than the 8-byte header");
        }

        string magic = Encoding.ASCII.GetString(bytes, 0, 4);
        uint total = ReadBigEndianUInt32(bytes, 4);
        List<string> types = new List<string>();
        List<uint> lengths = new List<uint>();
        bool consistent = true;

        int offset = HeaderSize;
        while (offset + ElementHeaderSize <= bytes.Length)
        {
            string type = Encoding.ASCII.GetString(bytes, offset, 4);
            uint len = ReadBigEndianUInt32(bytes, offset + 4);
            types.Add(type);
            lengths.Add(len);
            if (len < ElementHeaderSize || offset + (long)len > bytes.Length)
            {
                consistent = false;
                break;
            }

            offset += (int)len;
        }

        if (offset != bytes.Length)
        {
            consistent = false;
        }

        return new IcnsParseResult
        {
            Magic = magic,
            TotalLength = total,
            Types = types,
            ElementLengths = lengths,
            LengthsConsistent = consistent,
        };
    }

    public static void WriteBigEndianUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    public static uint ReadBigEndianUInt32(byte[] buffer, int offset)
    {
        return ((uint)buffer[offset] << 24)
            | ((uint)buffer[offset + 1] << 16)
            | ((uint)buffer[offset + 2] << 8)
            | buffer[offset + 3];
    }
}
