using System;
using System.Collections.Generic;
using System.Text;

namespace TipCL.Packager;

/// <summary>一条校验结果。<see cref="Required"/> 为 false 的是「只报告、不算失败」的项。</summary>
internal sealed class CheckResult
{
    public required string Name { get; init; }

    public required bool Ok { get; init; }

    public required string Detail { get; init; }

    public bool Required { get; init; } = true;
}

/// <summary>校验的汇总结果。</summary>
internal sealed class VerifyResult
{
    public required IReadOnlyList<CheckResult> Checks { get; init; }

    /// <summary>只报告、不判失败的附加行(例如「没有 LC_CODE_SIGNATURE 的 Mach-O 清单」)。</summary>
    public required IReadOnlyList<string> Report { get; init; }

    public required int EntryCount { get; init; }

    public required long TotalUncompressedBytes { get; init; }

    public required int MachOCount { get; init; }

    public required int MachOWithCodeSignature { get; init; }

    public required int MachOWithoutCodeSignature { get; init; }

    /// <summary>所有「必判」项都 OK。</summary>
    public bool AllRequiredPassed
    {
        get
        {
            foreach (CheckResult check in Checks)
            {
                if (check.Required && !check.Ok)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

/// <summary>
/// 对**已经写到磁盘、再从磁盘读回来**的归档做结构校验(纯函数:条目列表进、结果出,不碰磁盘)。
///
/// 校验清单逐项来自任务书 B3 的「Verification performed by the packager」小节:
/// 条目名 / 必填条目 / 权限位 / 两个 apphost 的 Mach-O 架构 / 每个 Mach-O 的 LC_CODE_SIGNATURE 报告 /
/// Info.plist 的键值 / icns 结构 / 启动脚本文本 / 不许有 .pdb 与 .exe / 条目数与体积。
/// </summary>
internal static class BundleVerifier
{
    /// <summary>每个条目读进内存的前缀长度:够读一个 Mach-O 头 + 它的 load command。</summary>
    public const int HeadBytes = 256 * 1024;

    public static VerifyResult Verify(
        IReadOnlyList<TarEntrySnapshot> entries,
        InfoPlistValues expectedPlist,
        IReadOnlyList<IcnsSlot> expectedIconSlots)
    {
        List<CheckResult> checks = new List<CheckResult>();
        List<string> report = new List<string>();

        Dictionary<string, TarEntrySnapshot> byName = new Dictionary<string, TarEntrySnapshot>(StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (TarEntrySnapshot entry in entries)
        {
            byName[entry.Name] = entry;
            totalBytes += entry.Length;
        }

        checks.Add(CheckEntryNames(entries));
        checks.Add(CheckRequiredEntries(byName));
        checks.Add(CheckDirectoryModes(entries));
        checks.Add(CheckExecutableModes(byName));
        checks.Add(CheckDataModes(byName));
        checks.Add(CheckNoGroupOtherWrite(entries));
        checks.Add(CheckHostMachO(byName, BundleLayout.Arm64HostEntry, MachOInfo.CpuTypeArm64, "arm64"));
        checks.Add(CheckHostMachO(byName, BundleLayout.X64HostEntry, MachOInfo.CpuTypeX86_64, "x86_64"));
        checks.Add(CheckInfoPlist(byName, expectedPlist));
        checks.Add(CheckIcns(byName, expectedIconSlots));
        checks.Add(CheckLauncherScript(byName));
        checks.Add(CheckNoPdbOrExe(entries));

        // Mach-O + 代码签名:只报告,不算失败(任务书明确)。
        List<string> machoNames = new List<string>();
        List<string> signed = new List<string>();
        List<string> unsigned = new List<string>();
        foreach (TarEntrySnapshot entry in entries)
        {
            if (entry.IsDirectory)
            {
                continue;
            }

            MachOInfo? info = MachO.TryParse(entry.Head);
            if (info == null)
            {
                continue;
            }

            machoNames.Add(entry.Name);
            if (info.HasCodeSignature)
            {
                signed.Add(entry.Name);
            }
            else
            {
                unsigned.Add(entry.Name);
            }
        }

        checks.Add(new CheckResult
        {
            Name = "code-signature report",
            Ok = true,
            Required = false,
            Detail = "mach-o files=" + machoNames.Count
                + " with LC_CODE_SIGNATURE=" + signed.Count
                + " without=" + unsigned.Count,
        });

        foreach (string name in signed)
        {
            report.Add("  signed: " + name);
        }

        foreach (string name in unsigned)
        {
            report.Add("  unsigned: " + name);
        }

        return new VerifyResult
        {
            Checks = checks,
            Report = report,
            EntryCount = entries.Count,
            TotalUncompressedBytes = totalBytes,
            MachOCount = machoNames.Count,
            MachOWithCodeSignature = signed.Count,
            MachOWithoutCodeSignature = unsigned.Count,
        };
    }

    /// <summary>单条检查的排版行(ASCII,脚本的 console 输出要求见 D-08)。</summary>
    public static string FormatLine(CheckResult check)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append(check.Ok ? "OK  " : "FAIL");
        if (!check.Required)
        {
            sb.Append("* ");
        }
        else
        {
            sb.Append("  ");
        }

        sb.Append(check.Name);
        if (!string.IsNullOrEmpty(check.Detail))
        {
            sb.Append(" -- ").Append(check.Detail);
        }

        return sb.ToString();
    }

    private static CheckResult CheckEntryNames(IReadOnlyList<TarEntrySnapshot> entries)
    {
        List<string> bad = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (TarEntrySnapshot entry in entries)
        {
            string name = entry.Name;
            if (!seen.Add(name))
            {
                bad.Add(name + " [duplicate]");
                continue;
            }

            if (!name.StartsWith(BundleLayout.RootEntry, StringComparison.Ordinal))
            {
                bad.Add(name + " [not under TipCL.app/]");
            }
            else if (name.Contains('\\'))
            {
                bad.Add(name + " [backslash]");
            }
            else if (name.StartsWith("/", StringComparison.Ordinal))
            {
                bad.Add(name + " [absolute]");
            }
            else if (HasDotDotSegment(name))
            {
                bad.Add(name + " [.. segment]");
            }
        }

        return new CheckResult
        {
            Name = "entry names",
            Ok = bad.Count == 0,
            Detail = bad.Count == 0
                ? entries.Count + " entries, all under TipCL.app/ with '/' separators and no '..'"
                : string.Join("; ", bad),
        };
    }

    private static CheckResult CheckRequiredEntries(Dictionary<string, TarEntrySnapshot> byName)
    {
        List<string> missing = new List<string>();
        foreach (string required in BundleLayout.RequiredFileEntries)
        {
            if (!byName.TryGetValue(required, out TarEntrySnapshot? entry) || entry.IsDirectory)
            {
                missing.Add(required);
            }
        }

        foreach (string required in BundleLayout.RequiredDirectoryEntries)
        {
            if (!byName.TryGetValue(required, out TarEntrySnapshot? entry) || !entry.IsDirectory)
            {
                missing.Add(required);
            }
        }

        return new CheckResult
        {
            Name = "required entries",
            Ok = missing.Count == 0,
            Detail = missing.Count == 0
                ? BundleLayout.RequiredFileEntries.Count + " files + "
                    + BundleLayout.RequiredDirectoryEntries.Count + " directories present"
                : "missing: " + string.Join(", ", missing),
        };
    }

    private static CheckResult CheckDirectoryModes(IReadOnlyList<TarEntrySnapshot> entries)
    {
        List<string> bad = new List<string>();
        int count = 0;
        foreach (TarEntrySnapshot entry in entries)
        {
            if (!entry.IsDirectory)
            {
                continue;
            }

            count++;
            if (entry.Mode != BundleLayout.ModeDirectory)
            {
                bad.Add(entry.Name + "=" + Octal(entry.Mode));
            }
        }

        return new CheckResult
        {
            Name = "directory modes (0755)",
            Ok = bad.Count == 0,
            Detail = bad.Count == 0
                ? count + " directories all 0755"
                : string.Join(", ", bad),
        };
    }

    private static CheckResult CheckExecutableModes(Dictionary<string, TarEntrySnapshot> byName)
    {
        string[] executables =
        {
            BundleLayout.LauncherEntry,
            BundleLayout.Arm64HostEntry,
            BundleLayout.X64HostEntry,
        };

        List<string> bad = new List<string>();
        foreach (string name in executables)
        {
            if (!byName.TryGetValue(name, out TarEntrySnapshot? entry))
            {
                bad.Add(name + "=missing");
            }
            else if (entry.Mode != BundleLayout.ModeExecutable)
            {
                bad.Add(name + "=" + Octal(entry.Mode));
            }
        }

        return new CheckResult
        {
            Name = "launcher + apphost modes (0755)",
            Ok = bad.Count == 0,
            Detail = bad.Count == 0 ? "3 executables all 0755" : string.Join(", ", bad),
        };
    }

    private static CheckResult CheckDataModes(Dictionary<string, TarEntrySnapshot> byName)
    {
        string[] dataEntries = { BundleLayout.InfoPlistEntry, BundleLayout.IcnsEntry };
        List<string> bad = new List<string>();
        int dylibCount = 0;
        foreach (TarEntrySnapshot entry in byName.Values)
        {
            if (entry.IsDirectory)
            {
                continue;
            }

            if (entry.Name.EndsWith(".dylib", StringComparison.Ordinal))
            {
                dylibCount++;
                if (entry.Mode != BundleLayout.ModeExecutable)
                {
                    bad.Add(entry.Name + "=" + Octal(entry.Mode) + " [dylib must be 0755]");
                }
            }
        }

        foreach (string name in dataEntries)
        {
            if (!byName.TryGetValue(name, out TarEntrySnapshot? entry))
            {
                bad.Add(name + "=missing");
            }
            else if (entry.Mode != BundleLayout.ModeFile)
            {
                bad.Add(name + "=" + Octal(entry.Mode));
            }
        }

        return new CheckResult
        {
            Name = "data modes (Info.plist + icns 0644, dylibs 0755)",
            Ok = bad.Count == 0,
            Detail = bad.Count == 0
                ? "2 data files 0644, " + dylibCount + " dylibs 0755"
                : string.Join(", ", bad),
        };
    }

    private static CheckResult CheckNoGroupOtherWrite(IReadOnlyList<TarEntrySnapshot> entries)
    {
        List<string> bad = new List<string>();
        foreach (TarEntrySnapshot entry in entries)
        {
            if ((entry.Mode & BundleLayout.GroupOtherWriteMask) != 0)
            {
                bad.Add(entry.Name + "=" + Octal(entry.Mode));
            }
        }

        return new CheckResult
        {
            Name = "no group/other write bits",
            Ok = bad.Count == 0,
            Detail = bad.Count == 0 ? "all " + entries.Count + " entries clean" : string.Join(", ", bad),
        };
    }

    private static CheckResult CheckHostMachO(
        Dictionary<string, TarEntrySnapshot> byName, string name, uint cpuType, string cpuName)
    {
        if (!byName.TryGetValue(name, out TarEntrySnapshot? entry))
        {
            return new CheckResult { Name = "apphost " + cpuName + " (" + name + ")", Ok = false, Detail = "missing" };
        }

        MachOInfo? info = MachO.TryParse(entry.Head);
        if (info == null)
        {
            return new CheckResult
            {
                Name = "apphost " + cpuName + " (" + name + ")",
                Ok = false,
                Detail = "not a 64-bit little-endian Mach-O",
            };
        }

        bool ok = info.CpuType == cpuType && info.FileType == MachOInfo.FileTypeExecute;
        return new CheckResult
        {
            Name = "apphost " + cpuName + " (" + name + ")",
            Ok = ok,
            Detail = "cputype=0x" + info.CpuType.ToString("X8") + " cpusubtype=0x" + info.CpuSubType.ToString("X8")
                + " filetype=" + info.FileType + " ncmds=" + info.CommandCount
                + " sizeofcmds=" + info.SizeOfCommands
                + (info.CommandsTruncated ? " [load commands truncated in head buffer]" : ""),
        };
    }

    private static CheckResult CheckInfoPlist(Dictionary<string, TarEntrySnapshot> byName, InfoPlistValues expected)
    {
        const string name = "Info.plist keys";
        if (!byName.TryGetValue(BundleLayout.InfoPlistEntry, out TarEntrySnapshot? entry) || entry.Full == null)
        {
            return new CheckResult { Name = name, Ok = false, Detail = "entry missing or not read" };
        }

        string text = Encoding.UTF8.GetString(entry.Full);
        Dictionary<string, string> actual;
        try
        {
            actual = InfoPlist.ReadKeyValues(text);
        }
        catch (Exception ex)
        {
            return new CheckResult { Name = name, Ok = false, Detail = "XML parse failed: " + ex.Message };
        }

        Dictionary<string, string> want = InfoPlist.ExpectedKeyValues(expected);
        List<string> bad = new List<string>();
        foreach (KeyValuePair<string, string> pair in want)
        {
            if (!actual.TryGetValue(pair.Key, out string? value))
            {
                bad.Add(pair.Key + "=missing");
            }
            else if (!string.Equals(value, pair.Value, StringComparison.Ordinal))
            {
                bad.Add(pair.Key + "='" + AsciiEscape(value) + "' expected '" + AsciiEscape(pair.Value) + "'");
            }
        }

        List<string> extra = new List<string>();
        foreach (string key in actual.Keys)
        {
            if (!want.ContainsKey(key))
            {
                extra.Add(key);
            }
        }

        return new CheckResult
        {
            Name = name,
            Ok = bad.Count == 0,
            Detail = bad.Count == 0
                ? want.Count + " keys match"
                    + (extra.Count == 0 ? "" : " (+" + extra.Count + " extra: " + string.Join(",", extra) + ")")
                : string.Join(", ", bad),
        };
    }

    private static CheckResult CheckIcns(Dictionary<string, TarEntrySnapshot> byName, IReadOnlyList<IcnsSlot> slots)
    {
        const string name = "TipCL.icns structure";
        if (!byName.TryGetValue(BundleLayout.IcnsEntry, out TarEntrySnapshot? entry) || entry.Full == null)
        {
            return new CheckResult { Name = name, Ok = false, Detail = "entry missing or not read" };
        }

        IcnsParseResult parsed;
        try
        {
            parsed = IcnsWriter.Read(entry.Full);
        }
        catch (Exception ex)
        {
            return new CheckResult { Name = name, Ok = false, Detail = "parse failed: " + ex.Message };
        }

        List<string> bad = new List<string>();
        if (parsed.Magic != IcnsWriter.MagicTag)
        {
            bad.Add("magic='" + AsciiEscape(parsed.Magic) + "'");
        }

        if (parsed.TotalLength != entry.Length)
        {
            bad.Add("length field=" + parsed.TotalLength + " entry size=" + entry.Length);
        }

        if (!parsed.LengthsConsistent)
        {
            bad.Add("element lengths do not tile the file");
        }

        StringBuilder layout = new StringBuilder();
        for (int i = 0; i < parsed.Types.Count; i++)
        {
            if (i > 0)
            {
                layout.Append(' ');
            }

            layout.Append(parsed.Types[i]).Append('(').Append(parsed.ElementLengths[i]).Append(')');
        }

        if (slots.Count != parsed.Types.Count)
        {
            bad.Add("expected " + slots.Count + " elements, found " + parsed.Types.Count);
        }
        else
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (parsed.Types[i] != slots[i].Type)
                {
                    bad.Add("element " + i + " type='" + parsed.Types[i] + "' expected '" + slots[i].Type + "'");
                }
            }
        }

        if (bad.Count == 0)
        {
            // 每个元素的数据必须以 PNG 签名为开头(任务书:PNG 数据对这些类型合法)。
            int offset = IcnsWriter.HeaderSize;
            for (int i = 0; i < parsed.Types.Count; i++)
            {
                int payload = offset + IcnsWriter.ElementHeaderSize;
                if (payload + 8 > entry.Full.Length
                    || entry.Full[payload] != 0x89
                    || entry.Full[payload + 1] != 0x50
                    || entry.Full[payload + 2] != 0x4E
                    || entry.Full[payload + 3] != 0x47)
                {
                    bad.Add("element " + i + " payload is not a PNG");
                }

                offset = payload + (int)(parsed.ElementLengths[i] - IcnsWriter.ElementHeaderSize);
            }
        }

        return new CheckResult
        {
            Name = name,
            Ok = bad.Count == 0,
            Detail = bad.Count == 0
                ? "magic ok, total=" + parsed.TotalLength + " bytes, elements: " + layout
                : string.Join(", ", bad),
        };
    }

    private static CheckResult CheckLauncherScript(Dictionary<string, TarEntrySnapshot> byName)
    {
        const string name = "launcher script bytes";
        if (!byName.TryGetValue(BundleLayout.LauncherEntry, out TarEntrySnapshot? entry) || entry.Full == null)
        {
            return new CheckResult { Name = name, Ok = false, Detail = "entry missing or not read" };
        }

        byte[] expected = LauncherScript.Bytes();
        byte[] actual = entry.Full;
        if (actual.Length != expected.Length)
        {
            return new CheckResult
            {
                Name = name,
                Ok = false,
                Detail = "size " + actual.Length + " expected " + expected.Length,
            };
        }

        for (int i = 0; i < expected.Length; i++)
        {
            if (actual[i] != expected[i])
            {
                return new CheckResult
                {
                    Name = name,
                    Ok = false,
                    Detail = "byte " + i + " = 0x" + actual[i].ToString("X2")
                        + " expected 0x" + expected[i].ToString("X2"),
                };
            }
        }

        bool hasCr = Array.IndexOf(actual, (byte)'\r') >= 0;
        if (hasCr)
        {
            return new CheckResult { Name = name, Ok = false, Detail = "contains CR" };
        }

        return new CheckResult
        {
            Name = name,
            Ok = true,
            Detail = actual.Length + " bytes, LF only, no BOM, byte-identical to the expected text",
        };
    }

    private static CheckResult CheckNoPdbOrExe(IReadOnlyList<TarEntrySnapshot> entries)
    {
        List<string> bad = new List<string>();
        foreach (TarEntrySnapshot entry in entries)
        {
            if (entry.IsDirectory)
            {
                continue;
            }

            if (entry.Name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
                || entry.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                bad.Add(entry.Name);
            }
        }

        return new CheckResult
        {
            Name = "no .pdb / no .exe entries",
            Ok = bad.Count == 0,
            Detail = bad.Count == 0 ? "0 found" : string.Join(", ", bad),
        };
    }

    private static bool HasDotDotSegment(string name)
    {
        string[] parts = name.Split('/');
        foreach (string part in parts)
        {
            if (part == "..")
            {
                return true;
            }
        }

        return false;
    }

    private static string Octal(int mode)
    {
        return "0" + Convert.ToString(mode, 8);
    }

    /// <summary>把非 ASCII 字符换成 `?`(控制台输出必须是 ASCII;要原文就看 tar 里的条目本身)。</summary>
    private static string AsciiEscape(string text)
    {
        StringBuilder sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            sb.Append(c >= 0x20 && c <= 0x7E ? c : '?');
        }

        return sb.ToString();
    }
}
