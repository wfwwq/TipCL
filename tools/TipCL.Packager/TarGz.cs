using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace TipCL.Packager;

/// <summary>
/// 写 tar 用的一个条目(纯数据 + 一个「怎么把内容打开」的委托,写的时候才读盘)。
/// </summary>
internal sealed class TarSourceEntry
{
    /// <summary>tar 里的名字,`/` 分隔;目录条目必须以 `/` 结尾。</summary>
    public required string Name { get; init; }

    /// <summary>权限位(八进制值,例如 0o755 = 493)。</summary>
    public required int Mode { get; init; }

    public required bool IsDirectory { get; init; }

    public required long Length { get; init; }

    /// <summary>目录条目为 null;文件条目必须是「每次调用都能给出一条从头开始的流」。</summary>
    public Func<Stream>? Open { get; init; }

    public static TarSourceEntry Dir(string name, int mode = BundleLayout.ModeDirectory)
    {
        string n = BundleLayout.NormalizeEntryName(name);
        if (!n.EndsWith("/", StringComparison.Ordinal))
        {
            n += "/";
        }

        return new TarSourceEntry { Name = n, Mode = mode, IsDirectory = true, Length = 0 };
    }

    public static TarSourceEntry File(string name, int mode, long length, Func<Stream> open)
    {
        return new TarSourceEntry
        {
            Name = BundleLayout.NormalizeEntryName(name),
            Mode = mode,
            IsDirectory = false,
            Length = length,
            Open = open,
        };
    }

    public static TarSourceEntry FromBytes(string name, int mode, byte[] bytes)
    {
        return File(name, mode, bytes.Length, () => new MemoryStream(bytes, writable: false));
    }
}

/// <summary>从 tar 里读回来的一个条目(校验用)。</summary>
internal sealed class TarEntrySnapshot
{
    public required string Name { get; init; }

    public required int Mode { get; init; }

    public required long Length { get; init; }

    public required bool IsDirectory { get; init; }

    /// <summary>内容的前 N 字节(判 Mach-O / 读脚本够用)。</summary>
    public required byte[] Head { get; init; }

    /// <summary>完整内容 —— 只有调用方点名的条目才有(其余为 null,免得把上百 MB 拉进内存)。</summary>
    public byte[]? Full { get; init; }
}

/// <summary>
/// 确定性的 tar / tar.gz 写入与读回。
///
/// 事实来源:任务书 B3 的「File-format facts」——`System.Formats.Tar.TarWriter` 可以按条目设 `entry.Mode`,
/// 条目名必须用 `/`。另外按任务书要求:入口按**序数排序**写、mtime 用**固定值**,
/// 这样同一份输入两次写出来的 tar 字节完全相同(压缩前)。
///
/// 固定 mtime 选 2000-01-01T00:00:00Z:可复现,且不落进任何「1970 之前/未来时间」的坑。
///
/// **格式用 Ustar,不用 Pax**(本包实测改动,2026-11-12,探针在 `artifacts/tarprobe/`,不入库):
/// .NET 8 的 `PaxTarEntry` 每次写都会补 `atime` / `ctime` 两条扩展记录,而它们的值取的是**写的那一刻**,
/// 于是同一份输入两次写出的字节不同(第一次实测差在第 533 字节);`PaxTarEntry` 也没有公开的
/// `Atime` / `Ctime` 可以钉死。Ustar 没有扩展头,同样的输入两次写出完全一致(实测)。
/// 代价是条目名不能超过 100 字节 —— 本 bundle 实际最长的名字 83 字节
/// (`.../libSystem.Security.Cryptography.Native.OpenSsl.dylib`),超了会在下面显式抛错而不是静默截断。
/// </summary>
internal static class TarGz
{
    public static readonly DateTimeOffset FixedModificationTime =
        new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Ustar 头里名称字段的字节数。</summary>
    public const int NameMaxBytes = 100;

    /// <summary>写未压缩的 tar(确定性;测试用它做「两次写出来的字节相同」的比对)。</summary>
    public static void WriteTar(IReadOnlyList<TarSourceEntry> entries, Stream output)
    {
        using TarWriter writer = new TarWriter(output, TarEntryFormat.Ustar, leaveOpen: true);
        foreach (TarSourceEntry source in SortOrdinal(entries))
        {
            string name = source.Name;
            if (Encoding.UTF8.GetByteCount(name) > NameMaxBytes)
            {
                throw new InvalidOperationException(
                    "tar entry name is too long for the Ustar format (" + Encoding.UTF8.GetByteCount(name)
                    + " > " + NameMaxBytes + " bytes): " + name);
            }

            UstarTarEntry entry = source.IsDirectory
                ? new UstarTarEntry(TarEntryType.Directory, name)
                : new UstarTarEntry(TarEntryType.RegularFile, name);

            entry.Mode = (UnixFileMode)source.Mode;
            entry.ModificationTime = FixedModificationTime;
            // uid/gid/属主名字显式写死,免得依赖运行环境的默认值(影响确定性)。
            entry.Uid = 0;
            entry.Gid = 0;
            entry.UserName = "";
            entry.GroupName = "";

            if (!source.IsDirectory)
            {
                if (source.Open == null)
                {
                    throw new ArgumentException("file entry without content: " + source.Name);
                }

                entry.DataStream = source.Open();
                if (!entry.DataStream.CanSeek)
                {
                    throw new ArgumentException("file entry stream must be seekable: " + source.Name);
                }
            }

            writer.WriteEntry(entry);
        }
    }

    /// <summary>写 gzip 压缩的 tar(.tar.gz)。</summary>
    public static void WriteTarGz(IReadOnlyList<TarSourceEntry> entries, Stream output)
    {
        using GZipStream gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true);
        WriteTar(entries, gzip);
    }

    /// <summary>按条目名做序数排序 —— 确定性归档的唯一顺序来源。</summary>
    public static List<TarSourceEntry> SortOrdinal(IReadOnlyList<TarSourceEntry> entries)
    {
        List<TarSourceEntry> copy = new List<TarSourceEntry>(entries);
        copy.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return copy;
    }

    /// <summary>
    /// 读回一个 tar 流。<paramref name="headSize"/> 限制每个条目读进内存的前缀长度;
    /// <paramref name="wantFull"/> 点名的条目才整份读进内存(校验只需要 Info.plist / 脚本 / icns 三个)。
    /// 每个条目的内容都会被完整消费(不依赖 reader 帮我们跳过),这样读到的是干净的顺序。
    /// </summary>
    public static List<TarEntrySnapshot> ReadEntries(
        Stream tarStream, int headSize, Func<string, bool>? wantFull = null)
    {
        List<TarEntrySnapshot> result = new List<TarEntrySnapshot>();
        using TarReader reader = new TarReader(tarStream, leaveOpen: true);
        byte[] scratch = new byte[64 * 1024];
        TarEntry? entry;
        while ((entry = reader.GetNextEntry(copyData: false)) != null)
        {
            // PAX 的扩展头条目('x' / 'g')不是内容条目:读者把它们当条目交出来时跳过。
            if (entry.EntryType == TarEntryType.ExtendedAttributes
                || entry.EntryType == TarEntryType.GlobalExtendedAttributes)
            {
                continue;
            }

            string name = entry.Name;
            bool isDirectory = entry.EntryType == TarEntryType.Directory
                || name.EndsWith("/", StringComparison.Ordinal);
            long length = isDirectory ? 0 : entry.Length;

            byte[] head = Array.Empty<byte>();
            byte[]? full = null;
            if (entry.DataStream != null && length > 0)
            {
                bool whole = wantFull != null && wantFull(name);
                if (whole)
                {
                    if (length > int.MaxValue)
                    {
                        throw new InvalidDataException("entry too large to read: " + name);
                    }

                    full = new byte[length];
                    entry.DataStream.ReadExactly(full, 0, full.Length);
                    head = full.Length <= headSize ? full : Slice(full, headSize);
                }
                else
                {
                    int headLen = (int)Math.Min(headSize, length);
                    head = new byte[headLen];
                    entry.DataStream.ReadExactly(head, 0, headLen);
                    Drain(entry.DataStream, length - headLen, scratch);
                }
            }

            result.Add(new TarEntrySnapshot
            {
                Name = name,
                Mode = (int)entry.Mode,
                Length = length,
                IsDirectory = isDirectory,
                Head = head,
                Full = full,
            });
        }

        return result;
    }

    /// <summary>读出整个 tar.gz 并展开成条目列表。</summary>
    public static List<TarEntrySnapshot> ReadTarGz(
        Stream gzipStream, int headSize, Func<string, bool>? wantFull = null)
    {
        using GZipStream gzip = new GZipStream(gzipStream, CompressionMode.Decompress, leaveOpen: true);
        return ReadEntries(gzip, headSize, wantFull);
    }

    private static byte[] Slice(byte[] source, int length)
    {
        byte[] result = new byte[length];
        Buffer.BlockCopy(source, 0, result, 0, length);
        return result;
    }

    private static void Drain(Stream stream, long remaining, byte[] scratch)
    {
        while (remaining > 0)
        {
            int want = (int)Math.Min(scratch.Length, remaining);
            int read = stream.Read(scratch, 0, want);
            if (read <= 0)
            {
                throw new InvalidDataException("tar entry content ended early");
            }

            remaining -= read;
        }
    }
}
