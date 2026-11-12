using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace TipCL.App.Pages;

/// <summary>
/// 皮肤 PNG 的正面视图合成(移植自 Windows 版 Pages/SkinPng.cs,WPF DrawingVisual → 纯像素运算)。
/// 输入 64×64(或 64×32 老皮肤)的 PNG,输出 128×256 的「头 + 身 + 双臂 + 双腿」正面图。
/// 用像素搬运而不是 Avalonia 绘图 API:结果确定、无渲染依赖、可在测试里断言。
/// </summary>
public static class SkinPng
{
    /// <summary>从 data URL(或裸 base64)解出 PNG 字节。</summary>
    public static byte[]? Decode(string? dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl))
            return null;
        string s = dataUrl.StartsWith("data:", StringComparison.Ordinal)
            ? dataUrl.Substring(dataUrl.IndexOf(',') + 1)
            : dataUrl;
        try
        {
            return Convert.FromBase64String(s);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>合成正面视图;PNG 不合法/太小返回 null。</summary>
    public static Bitmap? FrontView(byte[] png, bool slim)
    {
        if (!TryRead(png, out int w, out int h, out byte[] px) || w < 64)
            return null;

        int scale = w / 64;                 // HD 皮肤:每「皮肤像素」在源图里占 scale×scale
        bool legacy = h * 2 <= w;           // 64×32 老皮肤没有独立的左右腿贴图
        int arms = slim ? 3 : 4;            // 细臂(3px 宽)与宽臂(4px 宽)

        const int unit = 8;                 // 1 个皮肤像素 → 8 个输出像素
        int outW = 16 * unit, outH = 32 * unit;
        var canvas = new byte[outW * outH * 4];

        void Part(int sx, int sy, int sw, int sh, double dx, double dy, bool mirror = false)
        {
            int ox = (int)(dx * unit), oy = (int)(dy * unit);
            int dw = sw * unit, dh = sh * unit;
            for (int j = 0; j < dh; j++)
            {
                int syy = sy * scale + (int)((long)j * scale / unit);
                if (syy < 0 || syy >= h) continue;
                for (int i = 0; i < dw; i++)
                {
                    int ux = mirror ? dw - 1 - i : i;
                    int sxx = sx * scale + (int)((long)ux * scale / unit);
                    if (sxx < 0 || sxx >= w) continue;
                    int si = (syy * w + sxx) * 4;
                    if (px[si + 3] == 0) continue;      // 透明处不覆盖已画内容
                    int di = ((oy + j) * outW + (ox + i)) * 4;
                    if (di < 0 || di + 3 >= canvas.Length) continue;
                    canvas[di] = px[si];
                    canvas[di + 1] = px[si + 1];
                    canvas[di + 2] = px[si + 2];
                    canvas[di + 3] = px[si + 3];
                }
            }
        }

        Part(8, 8, 8, 8, 4, 0);                             // 头
        Part(20, 20, 8, 12, 4, 8);                          // 身体
        Part(44, 20, arms, 12, 4 - arms, 8);                // 右臂
        if (legacy) Part(44, 20, arms, 12, 12, 8, mirror: true);  // 老皮肤:右臂贴图镜像当左臂
        else Part(36, 52, arms, 12, 12, 8);                 // 左臂
        Part(4, 20, 4, 12, 4, 20);                          // 右腿
        if (legacy) Part(4, 20, 4, 12, 8, 20, mirror: true);
        else Part(20, 52, 4, 12, 8, 20);                    // 左腿
        Part(40, 8, 8, 8, 4, 0);                            // 头饰/帽子层
        if (!legacy)
        {
            Part(20, 36, 8, 12, 4, 8);                      // 外套层
            Part(44, 36, arms, 12, 4 - arms, 8);
            Part(52, 52, arms, 12, 12, 8);
            Part(4, 36, 4, 12, 4, 20);
            Part(4, 52, 4, 12, 8, 20);
        }

        var wb = new WriteableBitmap(
            new PixelSize(outW, outH), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var fb = wb.Lock())
        {
            var rowBuf = new byte[outW * 4];
            for (int y = 0; y < outH; y++)
            {
                for (int x = 0; x < outW; x++)
                {
                    int si = (y * outW + x) * 4;
                    rowBuf[x * 4 + 0] = canvas[si + 2]; // B
                    rowBuf[x * 4 + 1] = canvas[si + 1]; // G
                    rowBuf[x * 4 + 2] = canvas[si + 0]; // R
                    rowBuf[x * 4 + 3] = canvas[si + 3]; // A
                }
                Marshal.Copy(rowBuf, 0, fb.Address + y * fb.RowBytes, rowBuf.Length);
            }
        }
        return wb;
    }

    /// <summary>把 PNG 读成 RGBA8888 像素缓冲。</summary>
    private static bool TryRead(byte[] png, out int width, out int height, out byte[] rgba)
    {
        width = height = 0;
        rgba = Array.Empty<byte>();
        try
        {
            using var ms = new MemoryStream(png);
            using var bmp = new Bitmap(ms);
            width = bmp.PixelSize.Width;
            height = bmp.PixelSize.Height;
            using var wb = new WriteableBitmap(bmp.PixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using (var fb = wb.Lock())
            {
                bmp.CopyPixels(fb, AlphaFormat.Unpremul);
                rgba = new byte[width * height * 4];
                var rowBuf = new byte[width * 4];
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(fb.Address + y * fb.RowBytes, rowBuf, 0, rowBuf.Length);
                    for (int x = 0; x < width; x++)
                    {
                        int di = (y * width + x) * 4;
                        rgba[di + 0] = rowBuf[x * 4 + 2]; // R
                        rgba[di + 1] = rowBuf[x * 4 + 1]; // G
                        rgba[di + 2] = rowBuf[x * 4 + 0]; // B
                        rgba[di + 3] = rowBuf[x * 4 + 3]; // A
                    }
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
