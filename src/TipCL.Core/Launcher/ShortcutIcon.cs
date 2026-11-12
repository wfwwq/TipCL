// M4-H P3.5:桌面快捷方式的图标(Linux .desktop 的 Icon= 键需要一个真实存在的图标文件)。
// 不引入图片资源:按像素程序化生成一张 128×128 的 PNG(深绿圆角方块 + 浅绿「T」),
// 落在 $TIPCL_HOME/tipcl.png,PNG 编码只依赖 System.IO.Compression.ZLibStream(无第三方包)。
// Windows 参考版给的是 `icon.ico` / exe 自身(Windows 认 exe 内嵌图标),Linux 桌面环境不认,故换成本文件。
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using TipCL.Core;

namespace TipCL.Core.Launcher;

/// <summary>快捷方式图标:程序化生成的 PNG。</summary>
public static class ShortcutIcon
{
    /// <summary>图标边长(像素)。</summary>
    public const int Size = 128;

    /// <summary>背景色(品牌绿 #2E9B6B 压深一档,深底上的白字对比度足够)。</summary>
    private static readonly (byte R, byte G, byte B) Bg = (0x1E, 0x7A, 0x52);

    /// <summary>字母色(近白)。</summary>
    private static readonly (byte R, byte G, byte B) Fg = (0xF2, 0xFB, 0xF6);

    /// <summary>图标落地路径($TIPCL_HOME/tipcl.png)。</summary>
    public static string IconPath() => PPath.Join(Utils.ROOT, "tipcl.png");

    /// <summary>
    /// 确保图标存在并返回其路径(存在且内容合法就直接复用,否则重新生成)。
    /// 生成失败返回空串 —— 调用方(Shortcut)据此省略 Icon= 键,而不是写一个指向不存在文件的图标。
    /// </summary>
    public static string Ensure()
    {
        try
        {
            string path = IconPath();
            if (File.Exists(path) && IsValidPng(path))
            {
                return path;
            }
            string abs = PPath.Abs(path);
            Directory.CreateDirectory(PPath.Abs(new PPath(path).Parent.ToString()));
            File.WriteAllBytes(abs, Render());
            return path;
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>渲染 PNG(不落盘,供测试与 Ensure 共用)。</summary>
    public static byte[] Render()
    {
        byte[] rgba = Rgba();
        var raw = new byte[(Size * 4 + 1) * Size];
        for (int y = 0; y < Size; y++)
        {
            int rowStart = y * (Size * 4 + 1);
            raw[rowStart] = 0; // 每行的过滤器:0 = None
            Array.Copy(rgba, y * Size * 4, raw, rowStart + 1, Size * 4);
        }
        return Png(raw, Size, Size);
    }

    /// <summary>图标像素:圆角方块 + 居中的「T」。</summary>
    private static byte[] Rgba()
    {
        var px = new byte[Size * Size * 4];
        double radius = Size * 0.22;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int i = (y * Size + x) * 4;
                double alpha = RoundedCoverage(x, y, Size, Size, radius);
                if (alpha <= 0)
                {
                    continue;
                }
                var c = InGlyphT(x, y) ? Fg : Bg;
                px[i] = c.R;
                px[i + 1] = c.G;
                px[i + 2] = c.B;
                px[i + 3] = (byte)Math.Round(alpha * 255);
            }
        }
        return px;
    }

    /// <summary>圆角矩形覆盖率(每个像素 2×2 超采样,边缘不毛刺)。</summary>
    private static double RoundedCoverage(int x, int y, int w, int h, double r)
    {
        double hits = 0;
        for (int sy = 0; sy < 2; sy++)
        {
            for (int sx = 0; sx < 2; sx++)
            {
                double px = x + 0.25 + sx * 0.5;
                double py = y + 0.25 + sy * 0.5;
                if (InsideRounded(px, py, w, h, r))
                {
                    hits++;
                }
            }
        }
        return hits / 4.0;
    }

    private static bool InsideRounded(double px, double py, int w, int h, double r)
    {
        if (px < 0 || py < 0 || px > w || py > h)
        {
            return false;
        }
        double cx = Math.Min(Math.Max(px, r), w - r);
        double cy = Math.Min(Math.Max(py, r), h - r);
        double dx = px - cx;
        double dy = py - cy;
        return dx * dx + dy * dy <= r * r;
    }

    /// <summary>「T」字形(上横 + 中竖),按 128 网格的比例画。</summary>
    private static bool InGlyphT(int x, int y)
    {
        int barLeft = (int)(Size * 0.26);
        int barRight = (int)(Size * 0.74);
        int barTop = (int)(Size * 0.30);
        int barBottom = (int)(Size * 0.41);
        int stemLeft = (int)(Size * 0.44);
        int stemRight = (int)(Size * 0.56);
        int stemBottom = (int)(Size * 0.75);
        bool bar = x >= barLeft && x < barRight && y >= barTop && y < barBottom;
        bool stem = x >= stemLeft && x < stemRight && y >= barTop && y < stemBottom;
        return bar || stem;
    }

    /// <summary>最小 PNG 编码器(真彩 + alpha,无隔行,单 IDAT)。</summary>
    private static byte[] Png(byte[] rawScanlines, int width, int height)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

        var ihdr = new byte[13];
        WriteBe(ihdr, 0, width);
        WriteBe(ihdr, 4, height);
        ihdr[8] = 8;  // 位深
        ihdr[9] = 6;  // 颜色类型:真彩 + alpha
        ihdr[10] = 0; // 压缩方法
        ihdr[11] = 0; // 过滤方法
        ihdr[12] = 0; // 隔行:无
        Chunk(ms, "IHDR", ihdr);

        using (var comp = new MemoryStream())
        {
            using (var z = new ZLibStream(comp, CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(rawScanlines, 0, rawScanlines.Length);
            }
            Chunk(ms, "IDAT", comp.ToArray());
        }

        Chunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        byte[] len = new byte[4];
        WriteBe(len, 0, data.Length);
        s.Write(len, 0, 4);
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes, 0, 4);
        s.Write(data, 0, data.Length);
        uint crc = Crc32(typeBytes, data);
        byte[] crcBytes = new byte[4];
        WriteBe(crcBytes, 0, unchecked((int)crc));
        s.Write(crcBytes, 0, 4);
    }

    private static void WriteBe(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] a, byte[] b)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte x in a)
        {
            c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        }
        foreach (byte x in b)
        {
            c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        }
        return c ^ 0xFFFFFFFFu;
    }

    /// <summary>PNG 头 + IHDR 尺寸校验(用于「已有图标是否还能用」的判断)。</summary>
    public static bool IsValidPng(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var head = new byte[24];
            if (fs.Read(head, 0, 24) != 24)
            {
                return false;
            }
            for (int i = 0; i < 8; i++)
            {
                if (head[i] != new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }[i])
                {
                    return false;
                }
            }
            return head[12] == 'I' && head[13] == 'H' && head[14] == 'D' && head[15] == 'R';
        }
        catch (Exception)
        {
            return false;
        }
    }
}
