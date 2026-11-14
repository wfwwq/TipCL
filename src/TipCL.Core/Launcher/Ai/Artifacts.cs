// M4-H（AI 服务层）：工具产物落盘与回读（照 Windows 版 Launcher/Ai/Artifacts.cs 搬运）。
//
// 工具结果一长（比如列出几百个模组、读完一份 latest.log）就塞不下上下文，参考版的做法是:
// 超过 8000 字的工具结果整体落盘到 $TIPCL_HOME/cache/ai_results/，
// 只把「文件名 + 总行数 + 前 120 行」回给模型；模型需要细看时再调 read_artifact 分段读回来。
// 保留策略照抄:最多 300 个文件、7 天，超过就删（首次使用时做一次）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>工具结果文件（artifact）的写、读、回收。</summary>
public static class Artifacts
{
    /// <summary>测试可指向临时目录；空 = 用默认位置。</summary>
    public static string? ArtifactsDir;

    /// <summary>回给模型时带几行开头。</summary>
    public const int HeadLines = 120;

    public const int KeepDays = 7;

    public const int KeepFiles = 300;

    private static bool _pruned;

    private static string Dir()
    {
        string dir = ArtifactsDir ?? PPath.Join(Utils.ROOT, "cache", "ai_results");
        Utils.EnsureDir(dir);
        return dir;
    }

    /// <summary>按「最近 7 天 + 最多 300 个」清一遍，返回删掉的文件数。</summary>
    public static long Prune(int days = KeepDays, int keep = KeepFiles)
    {
        long removed = 0;
        try
        {
            List<string> files = Directory
                .EnumerateFiles(PPath.Abs(Dir()), "*.txt")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
            DateTime cutoff = DateTime.UtcNow.AddSeconds(-days * 86400.0);
            for (int i = 0; i < files.Count; i++)
            {
                try
                {
                    if (i >= keep || File.GetLastWriteTimeUtc(files[i]) < cutoff)
                    {
                        File.Delete(files[i]);
                        removed++;
                    }
                }
                catch (Exception)
                {
                }
            }
        }
        catch (Exception)
        {
        }
        return removed;
    }

    private static void PruneOnce()
    {
        if (_pruned)
        {
            return;
        }
        _pruned = true;
        Prune();
    }

    private static string FmtSize(long n)
    {
        if (n >= 1048576)
        {
            return Py.RoundHalfEven((double)n / 1024.0 / 1024.0, 1) + "MB";
        }
        if (n >= 1024)
        {
            return Py.RoundHalfEven((double)n / 1024.0, 1) + "KB";
        }
        return n + "B";
    }

    /// <summary>内容摘要 id（参考版用 SHA1 前 8 位）。</summary>
    public static string ArtifactId(string text)
    {
        using SHA1 sha1 = SHA1.Create();
        return string
            .Concat(sha1.ComputeHash(new UTF8Encoding(false).GetBytes(text ?? "")).Select(b => b.ToString("x2")))
            .Substring(0, 8);
    }

    /// <summary>落盘，返回相对路径（形如 cache/ai_results/20261112-133000-ab12cd34.txt）；失败返回空串。</summary>
    public static string Save(string text, string toolName = "")
    {
        try
        {
            PruneOnce();
            string name = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + ArtifactId(text) + ".txt";
            AiFs.WriteText(PPath.Join(Dir(), name), text ?? "");
            return "cache/ai_results/" + name;
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>落盘并生成「回给模型的那段话」（前 120 行 + 指路）；落盘失败就退化成截断文本。</summary>
    public static string Store(string text, string toolName = "")
    {
        string rel = Save(text, toolName);
        List<string> lines = Py.SplitLines(text ?? "");
        int count = lines.Count;
        long bytes = new UTF8Encoding(false).GetByteCount(text ?? "");
        if (rel.Length == 0)
        {
            return Py.Slice(text, null, 4000) + "\n...(已截断,落盘失败)";
        }
        string head = string.Join("\n", lines.Take(HeadLines));
        return "[结果过长,已存文件] " + rel + "\n总行数 " + count + " / 总字节 " + FmtSize(bytes) +
               "。以下为前 " + Math.Min(HeadLines, count) + " 行:\n" + head;
    }

    /// <summary>按文件名分段读回（offset 从 0 起）。只认目录内的文件名，路径穿越一律拒绝。</summary>
    public static string ReadArtifact(object? relOrName, object? offset = null, object? limit = null)
    {
        string[] parts = Py.Strip(Py.S(relOrName)).Replace("\\", "/").Split('/');
        string name = parts[parts.Length - 1];
        if (name.Length == 0 || name.Contains("/") || name.Contains("..") || name.Contains(":"))
        {
            return "无效的 artifact 名";
        }
        PPath root = new PPath(Dir()).Resolve();
        PPath target = (root / name).Resolve();
        if (!target.Parent.Equals(root))
        {
            return "无效的 artifact 名";
        }
        string text;
        try
        {
            text = AiFs.ReadText(target.ToString());
        }
        catch (Exception)
        {
            return "找不到 artifact: " + name;
        }
        List<string> lines = Py.SplitLines(text);
        long start = Math.Max(0, Py.Int(Py.Or(offset, 0L)));
        long end = Math.Min(lines.Count, start + Math.Max(1, Py.Int(Py.Or(limit, 200L))));
        if (start >= lines.Count)
        {
            return "起点超出范围(共 " + lines.Count + " 行)";
        }
        string body = string.Join("\n", lines.Skip((int)start).Take((int)(end - start)));
        return "[" + name + "] 第 " + (start + 1) + "-" + end + "/" + lines.Count + " 行:\n" + body;
    }
}
