// M4-H P5+（AI 服务层）：AI 自己的文件小工具（照 Windows 版 Launcher/Ai/AiFs.cs 搬运）。
//
// 只做三件事：递归列目录（排序稳定）、按平台规矩读写文本、取 mtime。
// 检查点（Checkpoint）与产物（Artifacts）都靠它，所以单独成件而不是散在调用点里。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>AI 子系统内部的路径/文本工具。</summary>
internal static class AiFs
{
    /// <summary>递归列出目录下的全部条目（含目录本身），路径排序保证可复现。符号链接目录不跟进（防环）。</summary>
    public static List<string> RglobSorted(string dir)
    {
        var outp = new List<string>();
        if (LocalFs.IsDir(dir))
        {
            Walk(dir);
        }
        outp.Sort(ComparePaths);
        return outp;

        void Walk(string d)
        {
            foreach (string item in LocalFs.Children(d))
            {
                outp.Add(item);
                if (LocalFs.IsDir(item) && !SingleRoot.IsSymlink(item))
                {
                    Walk(item);
                }
            }
        }
    }

    /// <summary>按路径分段比较（Windows 下不区分大小写，与参考版一致）。</summary>
    public static int ComparePaths(string a, string b)
    {
        List<string> parts = new PPath(a).Parts;
        List<string> parts2 = new PPath(b).Parts;
        int n = Math.Min(parts.Count, parts2.Count);
        for (int i = 0; i < n; i++)
        {
            int cmp = Utils.IS_WINDOWS
                ? string.CompareOrdinal(parts[i].ToLowerInvariant(), parts2[i].ToLowerInvariant())
                : string.CompareOrdinal(parts[i], parts2[i]);
            if (cmp != 0)
            {
                return cmp;
            }
        }
        return parts.Count.CompareTo(parts2.Count);
    }

    /// <summary>写文本（Windows 上把 \n 规整成 \r\n，与参考版一致；Linux 保持 \n）。</summary>
    public static void WriteText(string path, string text)
    {
        if (Utils.IS_WINDOWS)
        {
            text = (text ?? "").Replace("\n", "\r\n");
        }
        File.WriteAllBytes(PPath.Abs(path), new UTF8Encoding(false).GetBytes(text ?? ""));
    }

    /// <summary>读文本（默认严格 UTF-8，坏字节直接抛；<paramref name="replace"/> 时用替换符兜底）。</summary>
    public static string ReadText(string path, bool replace = false)
    {
        byte[] raw = File.ReadAllBytes(PPath.Abs(path));
        string text = replace ? Encoding.UTF8.GetString(raw) : Utils.DecodeUtf8Strict(raw);
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    /// <summary>修改时间（Unix 秒，浮点）。</summary>
    public static double MTime(string path)
    {
        string abs = PPath.Abs(path);
        DateTime t = Directory.Exists(abs) ? Directory.GetLastWriteTimeUtc(abs) : File.GetLastWriteTimeUtc(abs);
        return (t - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks / 10000000.0;
    }
}
