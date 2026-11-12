using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TipCL.Packager;

namespace TipCL.Tests;

/// <summary>
/// B3 打包工具的测试夹具。
///
/// 两条来源纪律(每条用例都在注释里标明期望值属于哪一种):
/// - **任务书**:按 GOAL.md / B3 任务书里「File-format facts」与「Bundle layout」「Tar entry modes」
///   小节逐字写的字段定义与权限位;
/// - **实测**:本机读字节得出的结论(`packaging\TipCL.ico` 的 7 个条目、SkiaSharp 2.88.9 解出
///   256x256、真实 publish 输出的 apphost 名字等,记录见 PROGRESS.md 的 B3 小节)。
///
/// 本文件里的 Mach-O / icns 字节一律是**按字段定义手写的合成数据**,不是 mac 产物上抄下来的
/// ——本机没有 mac 产物。测试不起进程、不联网,临时文件全部落在 <see cref="TestHome.Root"/> 之下。
/// </summary>
internal static class B3PackagerFixtures
{
    /// <summary>cputype:arm64(任务书 B3「File-format facts」)。</summary>
    internal const uint CpuArm64 = 0x0100000C;

    /// <summary>cputype:x86_64(同上)。</summary>
    internal const uint CpuX86_64 = 0x01000007;

    internal const uint FileTypeExecutable = 2;

    internal const uint FileTypeDylib = 6;

    /// <summary>LC_CODE_SIGNATURE(同上)。</summary>
    internal const uint LcCodeSignature = 0x1D;

    internal static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    /// <summary>
    /// 合成一个 64 位小端 Mach-O:32 字节头(`CF FA ED FE` + cputype + cpusubtype + filetype +
    /// ncmds + sizeofcmds + flags + reserved)+ 每条 8 字节(cmd + cmdsize=8)的 load command。
    /// </summary>
    internal static byte[] MachOImage(uint cpuType, uint fileType, params uint[] commands)
    {
        byte[] buffer = new byte[MachO.HeaderSize + (MachO.CommandHeaderSize * commands.Length)];
        WriteUInt32(buffer, 0, MachOInfo.Magic64Le);
        WriteUInt32(buffer, 4, cpuType);
        WriteUInt32(buffer, 8, 3);                 // cpusubtype:用例不断言它,给个非 0 值
        WriteUInt32(buffer, 12, fileType);
        WriteUInt32(buffer, 16, (uint)commands.Length);
        WriteUInt32(buffer, 20, (uint)(MachO.CommandHeaderSize * commands.Length));
        WriteUInt32(buffer, 24, 0);                // flags
        WriteUInt32(buffer, 28, 0);                // reserved
        for (int i = 0; i < commands.Length; i++)
        {
            WriteUInt32(buffer, MachO.HeaderSize + (i * MachO.CommandHeaderSize), commands[i]);
            WriteUInt32(buffer, MachO.HeaderSize + (i * MachO.CommandHeaderSize) + 4, MachO.CommandHeaderSize);
        }

        return buffer;
    }

    /// <summary>假 PNG:icns 的结构校验只看元素数据开头是不是 PNG 签名(任务书要求 PNG 负载)。</summary>
    internal static byte[] FakePng(int size)
    {
        byte[] bytes = new byte[Math.Max(size, 8)];
        bytes[0] = 0x89;
        bytes[1] = 0x50;
        bytes[2] = 0x4E;
        bytes[3] = 0x47;
        return bytes;
    }

    /// <summary>一段确定性的填充字节(不含 PNG 签名、也不是 Mach-O)。</summary>
    internal static byte[] Filler(int size, byte seed)
    {
        byte[] bytes = new byte[size];
        for (int i = 0; i < size; i++)
        {
            bytes[i] = (byte)(seed + i);
        }

        return bytes;
    }

    /// <summary>加一个 tar 条目:文件**真的落盘**(写归档时才读它),权限位走 <see cref="BundleLayout.ModeForFile"/>。</summary>
    internal static void AddFile(List<TarSourceEntry> entries, string directory, string fileName, string entryName, byte[] content)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, content);
        int mode = BundleLayout.ModeForFile(entryName, content);
        entries.Add(TarSourceEntry.File(entryName, mode, content.Length, () => File.OpenRead(path)));
    }

    /// <summary>
    /// 在 <paramref name="parentDir"/>(必须落在 <see cref="TestHome.Root"/> 之下)造一棵**极小的假 bundle**:
    /// 六个目录 + 两个架构目录下的若干文件 + Info.plist / 启动脚本 / icns 三件套。
    /// 它的形状按任务书 B3「Bundle layout」小节;<c>TipCL.dll</c>、<c>TipCL.Core.dll</c>、
    /// <c>createdump</c>、<c>libSkiaSharp.dylib</c> 这些名字来自本机真实 publish 输出的盘点结果。
    /// </summary>
    internal static TinyBundle CreateTinyBundle(string parentDir)
    {
        string root = Path.Combine(parentDir, "tiny-app");
        Directory.CreateDirectory(root);

        IReadOnlyList<IcnsSlot> slots = new[]
        {
            new IcnsSlot { Type = IcnsWriter.Type128, PixelSize = 128 },
        };

        List<TarSourceEntry> entries = new List<TarSourceEntry>();
        foreach (string directory in BundleLayout.RequiredDirectoryEntries)
        {
            entries.Add(TarSourceEntry.Dir(directory));
        }

        foreach (string arch in BundleLayout.ArchNames)
        {
            uint cpu = arch == "arm64" ? CpuArm64 : CpuX86_64;
            string bin = Path.Combine(root, "Contents", "MacOS", arch);
            string prefix = BundleLayout.MacOsDir + "/" + arch + "/";

            // apphost:filetype=2 且带 LC_CODE_SIGNATURE(.NET 的 apphost 在 mac 上有签名命令,
            // 本机 publish 出来的真实 TipCL 头里就有 —— 见 PROGRESS.md 的 B3 代码签名一节)
            AddFile(entries, bin, "TipCL", prefix + "TipCL", MachOImage(cpu, FileTypeExecutable, LcCodeSignature));
            AddFile(entries, bin, "TipCL.dll", prefix + "TipCL.dll", Filler(256, 0x10));
            AddFile(entries, bin, "TipCL.Core.dll", prefix + "TipCL.Core.dll", Filler(128, 0x20));
            AddFile(entries, bin, "TipCL.runtimeconfig.json", prefix + "TipCL.runtimeconfig.json", Filler(64, 0x30));
            AddFile(entries, bin, "createdump", prefix + "createdump", MachOImage(cpu, FileTypeExecutable, LcCodeSignature));
            AddFile(entries, bin, "libSkiaSharp.dylib", prefix + "libSkiaSharp.dylib", MachOImage(cpu, FileTypeDylib));
        }

        entries.Add(TarSourceEntry.FromBytes(
            BundleLayout.InfoPlistEntry, BundleLayout.ModeFile, InfoPlist.BuildBytes(InfoPlist.ForTipCL())));
        entries.Add(TarSourceEntry.FromBytes(
            BundleLayout.LauncherEntry, BundleLayout.ModeExecutable, LauncherScript.Bytes()));
        entries.Add(TarSourceEntry.FromBytes(
            BundleLayout.IcnsEntry,
            BundleLayout.ModeFile,
            IcnsWriter.Build(new[] { new IcnsElement { Type = IcnsWriter.Type128, Png = FakePng(64) } })));

        return new TinyBundle
        {
            Root = root,
            Entries = entries,
            Slots = slots,
            ArchivePath = Path.Combine(parentDir, "tiny-bundle.tar.gz"),
            TarPath = Path.Combine(parentDir, "tiny-bundle.tar"),
        };
    }

    /// <summary>读回归档时要点名整份读进内存的条目(与 <c>Program</c> 给 <c>TreeTarGz</c> 的口径一致)。</summary>
    internal static bool WantFullEntry(string name)
    {
        return name == BundleLayout.InfoPlistEntry
            || name == BundleLayout.IcnsEntry
            || name == BundleLayout.LauncherEntry;
    }

    /// <summary>复制一个读回来的条目并改掉若干字段(校验「故意写坏」的用例用)。</summary>
    internal static TarEntrySnapshot Copy(
        TarEntrySnapshot entry,
        string? name = null,
        int? mode = null,
        byte[]? head = null,
        byte[]? full = null,
        bool? isDirectory = null)
    {
        return new TarEntrySnapshot
        {
            Name = name ?? entry.Name,
            Mode = mode ?? entry.Mode,
            Length = entry.Length,
            IsDirectory = isDirectory ?? entry.IsDirectory,
            Head = head ?? entry.Head,
            Full = full ?? entry.Full,
        };
    }

    /// <summary>把条目表里第一个匹配 <paramref name="name"/> 的条目换成改写后的版本。</summary>
    internal static List<TarEntrySnapshot> Replace(
        IReadOnlyList<TarEntrySnapshot> entries, string name, TarEntrySnapshot replacement)
    {
        List<TarEntrySnapshot> result = new List<TarEntrySnapshot>();
        foreach (TarEntrySnapshot entry in entries)
        {
            result.Add(entry.Name == name ? replacement : entry);
        }

        return result;
    }

    /// <summary>丢掉名字匹配 <paramref name="name"/> 的条目。</summary>
    internal static List<TarEntrySnapshot> Drop(IReadOnlyList<TarEntrySnapshot> entries, string name)
    {
        List<TarEntrySnapshot> result = new List<TarEntrySnapshot>();
        foreach (TarEntrySnapshot entry in entries)
        {
            if (entry.Name != name)
            {
                result.Add(entry);
            }
        }

        return result;
    }

    /// <summary>从测试输出目录往上找仓库根,返回仓库内某个文件的路径(只读,不写)。</summary>
    internal static string RepoPath(params string[] relative)
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string[] parts = new string[relative.Length + 1];
            parts[0] = dir.FullName;
            Array.Copy(relative, 0, parts, 1, relative.Length);
            string candidate = Path.Combine(parts);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("仓库内找不到文件: " + string.Join("/", relative));
    }

    /// <summary>把字节按 UTF-8 严格解码(用来断言「确实是合法 UTF-8」)。</summary>
    internal static string Utf8Strict(byte[] bytes)
    {
        return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
    }
}

/// <summary>一棵落在磁盘上的极小假 bundle + 它的 tar 条目表。</summary>
internal sealed class TinyBundle
{
    internal required string Root { get; init; }

    internal required List<TarSourceEntry> Entries { get; init; }

    internal required IReadOnlyList<IcnsSlot> Slots { get; init; }

    internal required string ArchivePath { get; init; }

    internal required string TarPath { get; init; }

    /// <summary>写 gzip 归档(用生产代码的写入口)。</summary>
    internal byte[] WriteTarGzBytes()
    {
        using MemoryStream stream = new MemoryStream();
        TarGz.WriteTarGz(Entries, stream);
        return stream.ToArray();
    }

    /// <summary>写未压缩 tar(确定性比对用)。</summary>
    internal byte[] WriteTarBytes()
    {
        using MemoryStream stream = new MemoryStream();
        TarGz.WriteTar(Entries, stream);
        return stream.ToArray();
    }

    /// <summary>把 tar.gz 写到磁盘再按生产代码的口径读回来。</summary>
    internal List<TarEntrySnapshot> WriteAndReadArchive()
    {
        File.WriteAllBytes(ArchivePath, WriteTarGzBytes());
        using FileStream stream = File.OpenRead(ArchivePath);
        return TarGz.ReadTarGz(stream, BundleVerifier.HeadBytes, B3PackagerFixtures.WantFullEntry);
    }
}
