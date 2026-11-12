using System;
using System.Collections.Generic;

namespace TipCL.Packager;

/// <summary>一个 64 位 Mach-O 头的关键字段(只解析打包校验需要的那几个)。</summary>
internal sealed class MachOInfo
{
    /// <summary>magic 的取值:0xFEEDFACF(文件里前四个字节是 CF FA ED FE)。</summary>
    public const uint Magic64Le = 0xFEEDFACF;

    /// <summary>MH_EXECUTE。</summary>
    public const uint FileTypeExecute = 2;

    public const uint CpuTypeArm64 = 0x0100000C;

    public const uint CpuTypeX86_64 = 0x01000007;

    /// <summary>LC_CODE_SIGNATURE。</summary>
    public const uint LcCodeSignature = 0x1D;

    public uint Magic { get; init; }

    public uint CpuType { get; init; }

    public uint CpuSubType { get; init; }

    public uint FileType { get; init; }

    public uint CommandCount { get; init; }

    /// <summary>sizeofcmds:所有 load command 的总字节数。</summary>
    public uint SizeOfCommands { get; init; }

    public uint Flags { get; init; }

    /// <summary>实际解析出来的 load command 的 cmd 值(按出现顺序)。</summary>
    public IReadOnlyList<uint> Commands { get; init; } = Array.Empty<uint>();

    /// <summary>头写了 ncmds 个命令,但字节不够读完 → 后面的命令看不到(不抛异常,如实标记)。</summary>
    public bool CommandsTruncated { get; init; }

    public bool HasCodeSignature
    {
        get
        {
            for (int i = 0; i < Commands.Count; i++)
            {
                if (Commands[i] == LcCodeSignature)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>架构名字(打包输出用):arm64 / x86_64 / 其它按 cputype 的十六进制。</summary>
    public string CpuName => CpuType switch
    {
        CpuTypeArm64 => "arm64",
        CpuTypeX86_64 => "x86_64",
        _ => "cputype:0x" + CpuType.ToString("X8"),
    };
}

/// <summary>
/// 极小的 Mach-O 头解析器。
///
/// 事实来源:任务书 B3 的「File-format facts」——64 位小端 Mach-O 的 magic 是 0xFEEDFACF(字节序 CF FA ED FE),
/// 偏移 4 起是 cputype(arm64 = 0x0100000C、x86_64 = 0x01000007)、cpusubtype、filetype(2 = executable)、
/// ncmds、sizeofcmds、flags、reserved,load command 从偏移 32 开始,每条以 cmd(uint32) + cmdsize(uint32) 打头,
/// cmd == 0x1D 是 LC_CODE_SIGNATURE。这些值本机**没有**从真实 mac 产物上读过(本机没有 mac 产物),
/// 所以只在**结构上**按上面的定义解析;测试用的是按同一份定义手写的合成字节。
///
/// 非 Mach-O(比如脚本、.dll、.json)→ null;字节数不足一个头 → null;头之后字节不够 → 命令截断标记。
/// </summary>
internal static class MachO
{
    /// <summary>64 位头本身的长度。</summary>
    public const int HeaderSize = 32;

    /// <summary>一条 load command 至少要有的字节数(cmd + cmdsize)。</summary>
    public const int CommandHeaderSize = 8;

    public static bool HasMagic(byte[] bytes)
    {
        return bytes != null
            && bytes.Length >= 4
            && bytes[0] == 0xCF
            && bytes[1] == 0xFA
            && bytes[2] == 0xED
            && bytes[3] == 0xFE;
    }

    /// <summary>能解析成一个 64 位 Mach-O 头就返回它,否则返回 null。</summary>
    public static MachOInfo? TryParse(byte[]? bytes)
    {
        if (!HasMagic(bytes!))
        {
            return null;
        }

        byte[] b = bytes!;
        if (b.Length < HeaderSize)
        {
            return null;
        }

        uint magic = ReadUInt32(b, 0);
        uint cpuType = ReadUInt32(b, 4);
        uint cpuSubType = ReadUInt32(b, 8);
        uint fileType = ReadUInt32(b, 12);
        uint ncmds = ReadUInt32(b, 16);
        uint sizeofcmds = ReadUInt32(b, 20);
        uint flags = ReadUInt32(b, 24);

        List<uint> commands = new List<uint>();
        bool truncated = false;
        int offset = HeaderSize;
        for (uint i = 0; i < ncmds; i++)
        {
            if (offset + CommandHeaderSize > b.Length)
            {
                truncated = true;
                break;
            }

            uint cmd = ReadUInt32(b, offset);
            uint cmdSize = ReadUInt32(b, offset + 4);
            commands.Add(cmd);

            if (cmdSize < CommandHeaderSize || offset + (long)cmdSize > b.Length)
            {
                // cmdsize 不可能小于 8;字节不够就是截断,后面不再猜。
                truncated = true;
                break;
            }

            offset += (int)cmdSize;
        }

        return new MachOInfo
        {
            Magic = magic,
            CpuType = cpuType,
            CpuSubType = cpuSubType,
            FileType = fileType,
            CommandCount = ncmds,
            SizeOfCommands = sizeofcmds,
            Flags = flags,
            Commands = commands,
            CommandsTruncated = truncated,
        };
    }

    /// <summary>是不是「Mach-O 可执行文件」(filetype == 2)。非 Mach-O → false。</summary>
    public static bool IsExecutable(byte[]? bytes)
    {
        MachOInfo? info = TryParse(bytes);
        return info != null && info.FileType == MachOInfo.FileTypeExecute;
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
    {
        return (uint)(bytes[offset]
            | (bytes[offset + 1] << 8)
            | (bytes[offset + 2] << 16)
            | (bytes[offset + 3] << 24));
    }
}
