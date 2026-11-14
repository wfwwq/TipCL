using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace TipCL.App.Common;

/// <summary>
/// 远程图片加载 + 进程内缓存(对齐 Windows 版 Services/Thumbs 的常用面)。
/// 用于账号头像/皮肤预览这类小图;失败返回 null,调用方自行回落占位。
/// </summary>
internal static class Thumbs
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);

    private static readonly object Gate = new();

    public static async Task<Bitmap?> LoadAsync(string? url, int decodeWidth = 0)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        lock (Gate)
            if (Cache.TryGetValue(url!, out var hit))
                return hit;
        Bitmap? bmp = null;
        try
        {
            byte[] bytes = await Http.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            bmp = decodeWidth > 0 ? Bitmap.DecodeToWidth(ms, decodeWidth) : new Bitmap(ms);
        }
        catch (Exception)
        {
            bmp = null;
        }
        lock (Gate)
            Cache[url!] = bmp;
        return bmp;
    }
}
