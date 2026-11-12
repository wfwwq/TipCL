using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TipCL.Packager;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B3:tar / tar.gz 的写入、读回与结构校验的往返用例。
///
/// 假 bundle 建在 <see cref="TestHome.Root"/> 之下(红线:临时文件不许落在仓库外),
/// 形状与权限位判据来自任务书 B3(「Tar entry modes」「Verification performed by the packager」两节)。
/// 这些用例不起进程、不联网、不读真实归档。
/// </summary>
public class B3TarGzTests
{
    private const int Mode755 = 493;   // 0o755

    private const int Mode644 = 420;   // 0o644

    [Fact]
    public void Archive_round_trips_the_names_and_the_modes_of_the_tiny_bundle()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);

        List<TarEntrySnapshot> entries = bundle.WriteAndReadArchive();

        Assert.Equal(21, entries.Count);
        Assert.Contains(entries, e => e.Name == BundleLayout.InfoPlistEntry && e.Mode == Mode644);
        Assert.Contains(entries, e => e.Name == BundleLayout.IcnsEntry && e.Mode == Mode644);
        Assert.Contains(entries, e => e.Name == BundleLayout.LauncherEntry && e.Mode == Mode755);
        Assert.Contains(entries, e => e.Name == BundleLayout.Arm64HostEntry && e.Mode == Mode755);
        Assert.Contains(entries, e => e.Name == BundleLayout.X64HostEntry && e.Mode == Mode755);
        Assert.Contains(entries, e => e.Name == "TipCL.app/Contents/MacOS/arm64/createdump" && e.Mode == Mode755);
        Assert.Contains(entries, e => e.Name == "TipCL.app/Contents/MacOS/x64/libSkiaSharp.dylib" && e.Mode == Mode755);
        Assert.Contains(entries, e => e.Name == "TipCL.app/Contents/MacOS/arm64/TipCL.dll" && e.Mode == Mode644);
    }

    [Fact]
    public void The_top_level_entry_is_the_app_directory()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);

        List<TarEntrySnapshot> entries = bundle.WriteAndReadArchive();

        Assert.Equal("TipCL.app/", entries[0].Name);
        Assert.True(entries[0].IsDirectory);
    }

    [Fact]
    public void Every_directory_entry_is_0755_and_ends_with_a_slash()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);

        List<TarEntrySnapshot> directories = bundle.WriteAndReadArchive().Where(e => e.IsDirectory).ToList();

        Assert.Equal(6, directories.Count);
        foreach (TarEntrySnapshot directory in directories)
        {
            Assert.EndsWith("/", directory.Name, StringComparison.Ordinal);
            Assert.Equal(Mode755, directory.Mode);
            Assert.Equal(0, directory.Length);
        }
    }

    [Fact]
    public void Entries_are_written_in_ordinal_order()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);

        List<string> names = bundle.WriteAndReadArchive().Select(e => e.Name).ToList();
        List<string> sorted = new List<string>(names);
        sorted.Sort(StringComparer.Ordinal);

        Assert.Equal(sorted, names);
    }

    [Fact]
    public void Writing_the_same_input_twice_gives_identical_uncompressed_tar_bytes()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);

        Assert.Equal(bundle.WriteTarBytes(), bundle.WriteTarBytes());
    }

    [Fact]
    public void Writing_the_same_input_twice_gives_identical_tar_gz_bytes()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);

        Assert.Equal(bundle.WriteTarGzBytes(), bundle.WriteTarGzBytes());
    }

    [Fact]
    public void The_archive_is_byte_identical_when_written_twice_to_disk()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);

        bundle.WriteAndReadArchive();
        byte[] first = File.ReadAllBytes(bundle.ArchivePath);
        bundle.WriteAndReadArchive();
        byte[] second = File.ReadAllBytes(bundle.ArchivePath);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Reading_garbage_does_not_silently_produce_entries()
    {
        using MemoryStream stream = new MemoryStream(new byte[] { 0x01, 0x02, 0x03, 0x04 });

        // 期望「抛异常」而不是「空表」:读写通道不许把坏输入当成空归档
        Assert.ThrowsAny<Exception>(() =>
            TarGz.ReadTarGz(stream, BundleVerifier.HeadBytes, B3PackagerFixtures.WantFullEntry));
    }

    [Fact]
    public void Verifier_passes_every_required_check_on_the_tiny_bundle()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);

        VerifyResult result = Verify(bundle, bundle.WriteAndReadArchive());

        Assert.True(result.AllRequiredPassed, Failures(result));
        Assert.All(result.Checks, check => Assert.True(check.Ok, check.Name + ": " + check.Detail));
    }

    [Fact]
    public void Verifier_counts_the_entries_and_the_uncompressed_bytes()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);
        List<TarEntrySnapshot> entries = bundle.WriteAndReadArchive();

        VerifyResult result = Verify(bundle, entries);

        Assert.Equal(21, result.EntryCount);
        Assert.Equal(entries.Sum(e => e.Length), result.TotalUncompressedBytes);

        // 下界:两个架构目录各 560 字节 + icns 80 字节(上面都写死过)+ 启动脚本
        Assert.True(result.TotalUncompressedBytes >= 1120 + 80 + LauncherScript.Bytes().Length,
            "总量看起来不对: " + result.TotalUncompressedBytes);
    }

    [Fact]
    public void Verifier_fails_when_a_required_entry_is_missing()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);
        List<TarEntrySnapshot> entries = B3PackagerFixtures.Drop(
            bundle.WriteAndReadArchive(), BundleLayout.ArchDirEntry("arm64", "TipCL.Core.dll"));

        VerifyResult result = Verify(bundle, entries);

        Assert.False(result.AllRequiredPassed);
        Assert.Contains(result.Checks, c => c.Name == "required entries" && !c.Ok);
    }

    [Fact]
    public void Verifier_fails_when_the_required_apphost_entry_is_only_a_directory()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);
        List<TarEntrySnapshot> entries = bundle.WriteAndReadArchive();
        TarEntrySnapshot host = entries.Single(e => e.Name == BundleLayout.Arm64HostEntry);
        entries = B3PackagerFixtures.Replace(entries, BundleLayout.Arm64HostEntry,
            B3PackagerFixtures.Copy(host, isDirectory: true, mode: Mode755));

        VerifyResult result = Verify(bundle, entries);

        Assert.False(result.AllRequiredPassed);
        Assert.Contains(result.Checks, c => c.Name == "required entries" && !c.Ok);
    }

    [Fact]
    public void Verifier_fails_when_the_launcher_mode_is_0644()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);
        List<TarEntrySnapshot> entries = bundle.WriteAndReadArchive();
        TarEntrySnapshot launcher = entries.Single(e => e.Name == BundleLayout.LauncherEntry);
        entries = B3PackagerFixtures.Replace(entries, BundleLayout.LauncherEntry,
            B3PackagerFixtures.Copy(launcher, mode: Mode644));

        VerifyResult result = Verify(bundle, entries);

        Assert.False(result.AllRequiredPassed);
        Assert.Contains(result.Checks, c => c.Name.StartsWith("launcher + apphost modes", StringComparison.Ordinal) && !c.Ok);
    }

    [Fact]
    public void Verifier_fails_when_a_directory_mode_is_not_0755()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);
        List<TarEntrySnapshot> entries = bundle.WriteAndReadArchive();
        TarEntrySnapshot directory = entries.Single(e => e.Name == BundleLayout.Arm64Dir + "/");
        entries = B3PackagerFixtures.Replace(entries, directory.Name,
            B3PackagerFixtures.Copy(directory, mode: Mode644));

        VerifyResult result = Verify(bundle, entries);

        Assert.False(result.AllRequiredPassed);
        Assert.Contains(result.Checks, c => c.Name.StartsWith("directory modes", StringComparison.Ordinal) && !c.Ok);
    }

    [Fact]
    public void Verifier_fails_when_a_group_write_bit_is_present()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);
        List<TarEntrySnapshot> entries = bundle.WriteAndReadArchive();
        TarEntrySnapshot json = entries.Single(e => e.Name == "TipCL.app/Contents/MacOS/arm64/TipCL.runtimeconfig.json");
        entries = B3PackagerFixtures.Replace(entries, json.Name,
            B3PackagerFixtures.Copy(json, mode: 438));   // 0o666

        VerifyResult result = Verify(bundle, entries);

        Assert.False(result.AllRequiredPassed);
        Assert.Contains(result.Checks, c => c.Name.StartsWith("no group/other write bits", StringComparison.Ordinal) && !c.Ok);
    }

    [Fact]
    public void Verifier_fails_when_a_dylib_is_0644()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);
        List<TarEntrySnapshot> entries = bundle.WriteAndReadArchive();
        TarEntrySnapshot dylib = entries.Single(e => e.Name == "TipCL.app/Contents/MacOS/arm64/libSkiaSharp.dylib");
        entries = B3PackagerFixtures.Replace(entries, dylib.Name,
            B3PackagerFixtures.Copy(dylib, mode: Mode644));

        VerifyResult result = Verify(bundle, entries);

        Assert.False(result.AllRequiredPassed);
        Assert.Contains(result.Checks, c => c.Name.StartsWith("data modes", StringComparison.Ordinal) && !c.Ok);
    }

    [Fact]
    public void Verifier_fails_when_the_launcher_text_differs()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);
        List<TarEntrySnapshot> entries = bundle.WriteAndReadArchive();
        TarEntrySnapshot launcher = entries.Single(e => e.Name == BundleLayout.LauncherEntry);
        byte[] tampered = LauncherScript.Bytes();
        tampered[0] = (byte)'#';
        tampered[1] = (byte)'!';
        tampered[2] = (byte)'/';
        tampered[3] = (byte)'b';
        tampered[4] = (byte)'a';
        tampered[5] = (byte)'s';
        tampered[6] = (byte)'h';   // "#!/bin/bash"
        entries = B3PackagerFixtures.Replace(entries, BundleLayout.LauncherEntry,
            B3PackagerFixtures.Copy(launcher, head: tampered, full: tampered));

        VerifyResult result = Verify(bundle, entries);

        Assert.False(result.AllRequiredPassed);
        Assert.Contains(result.Checks, c => c.Name == "launcher script bytes" && !c.Ok);
    }

    [Fact]
    public void Verifier_fails_when_the_entry_name_escapes_the_app_root()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);
        List<TarEntrySnapshot> entries = bundle.WriteAndReadArchive();
        TarEntrySnapshot json = entries.Single(e => e.Name == "TipCL.app/Contents/MacOS/x64/TipCL.runtimeconfig.json");
        entries = B3PackagerFixtures.Replace(entries, json.Name,
            B3PackagerFixtures.Copy(json, name: "TipCL.app/Contents/../../evil.json"));

        VerifyResult result = Verify(bundle, entries);

        Assert.False(result.AllRequiredPassed);
        Assert.Contains(result.Checks, c => c.Name == "entry names" && !c.Ok);
    }

    [Fact]
    public void Verifier_fails_when_the_apphost_is_not_a_mach_o_executable()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);
        List<TarEntrySnapshot> entries = bundle.WriteAndReadArchive();
        TarEntrySnapshot host = entries.Single(e => e.Name == BundleLayout.X64HostEntry);
        byte[] launcherImage = LauncherScript.Bytes();
        entries = B3PackagerFixtures.Replace(entries, BundleLayout.X64HostEntry,
            B3PackagerFixtures.Copy(host, head: launcherImage));

        VerifyResult result = Verify(bundle, entries);

        Assert.False(result.AllRequiredPassed);
        Assert.Contains(result.Checks, c => c.Name.Contains("x86_64", StringComparison.Ordinal) && !c.Ok);
    }

    [Fact]
    public void Verifier_fails_when_the_apphost_has_the_wrong_cputype()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);
        List<TarEntrySnapshot> entries = bundle.WriteAndReadArchive();
        TarEntrySnapshot host = entries.Single(e => e.Name == BundleLayout.Arm64HostEntry);
        byte[] wrong = B3PackagerFixtures.MachOImage(
            B3PackagerFixtures.CpuX86_64, B3PackagerFixtures.FileTypeExecutable, B3PackagerFixtures.LcCodeSignature);
        entries = B3PackagerFixtures.Replace(entries, BundleLayout.Arm64HostEntry,
            B3PackagerFixtures.Copy(host, head: wrong));

        VerifyResult result = Verify(bundle, entries);

        Assert.False(result.AllRequiredPassed);
        Assert.Contains(result.Checks, c => c.Name.Contains("arm64", StringComparison.Ordinal) && !c.Ok);
    }

    [Fact]
    public void Verifier_fails_when_the_icns_total_length_field_is_wrong()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);
        List<TarEntrySnapshot> entries = bundle.WriteAndReadArchive();
        TarEntrySnapshot icns = entries.Single(e => e.Name == BundleLayout.IcnsEntry);
        byte[] tampered = (byte[])icns.Full!.Clone();
        IcnsWriter.WriteBigEndianUInt32(tampered, 4, (uint)tampered.Length + 4);
        entries = B3PackagerFixtures.Replace(entries, BundleLayout.IcnsEntry,
            B3PackagerFixtures.Copy(icns, head: tampered, full: tampered));

        VerifyResult result = Verify(bundle, entries);

        Assert.False(result.AllRequiredPassed);
        Assert.Contains(result.Checks, c => c.Name.StartsWith("TipCL.icns", StringComparison.Ordinal) && !c.Ok);
    }

    [Fact]
    public void Verifier_fails_when_the_icns_elements_do_not_match_the_expected_slots()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);
        List<TarEntrySnapshot> entries = bundle.WriteAndReadArchive();
        TarEntrySnapshot icns = entries.Single(e => e.Name == BundleLayout.IcnsEntry);
        byte[] twoElements = IcnsWriter.Build(new[]
        {
            new IcnsElement { Type = IcnsWriter.Type128, Png = B3PackagerFixtures.FakePng(64) },
            new IcnsElement { Type = IcnsWriter.Type256, Png = B3PackagerFixtures.FakePng(64) },
        });
        entries = B3PackagerFixtures.Replace(entries, BundleLayout.IcnsEntry,
            B3PackagerFixtures.Copy(icns, head: twoElements, full: twoElements));

        VerifyResult result = Verify(bundle, entries);

        Assert.False(result.AllRequiredPassed);
        Assert.Contains(result.Checks, c => c.Name.StartsWith("TipCL.icns", StringComparison.Ordinal) && !c.Ok);
    }

    [Fact]
    public void Verifier_fails_when_the_info_plist_does_not_parse()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);
        List<TarEntrySnapshot> entries = bundle.WriteAndReadArchive();
        TarEntrySnapshot plist = entries.Single(e => e.Name == BundleLayout.InfoPlistEntry);
        byte[] broken = System.Text.Encoding.UTF8.GetBytes("<plist><dict></plist>");
        entries = B3PackagerFixtures.Replace(entries, BundleLayout.InfoPlistEntry,
            B3PackagerFixtures.Copy(plist, head: broken, full: broken));

        VerifyResult result = Verify(bundle, entries);

        Assert.False(result.AllRequiredPassed);
        Assert.Contains(result.Checks, c => c.Name == "Info.plist keys" && !c.Ok);
    }

    [Fact]
    public void Verifier_fails_when_the_info_plist_has_a_wrong_value()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);
        List<TarEntrySnapshot> entries = bundle.WriteAndReadArchive();
        TarEntrySnapshot plist = entries.Single(e => e.Name == BundleLayout.InfoPlistEntry);
        byte[] wrong = System.Text.Encoding.UTF8.GetBytes(
            B3PackagerFixtures.Utf8Strict(plist.Full!).Replace("<string>APPL</string>", "<string>APPLX</string>", StringComparison.Ordinal));
        entries = B3PackagerFixtures.Replace(entries, BundleLayout.InfoPlistEntry,
            B3PackagerFixtures.Copy(plist, head: wrong, full: wrong));

        VerifyResult result = Verify(bundle, entries);

        Assert.False(result.AllRequiredPassed);
        Assert.Contains(result.Checks, c => c.Name == "Info.plist keys" && !c.Ok);
    }

    [Fact]
    public void Verifier_flags_a_pdb_entry_and_an_exe_entry()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);
        List<TarEntrySnapshot> entries = bundle.WriteAndReadArchive();
        entries.Add(new TarEntrySnapshot
        {
            Name = "TipCL.app/Contents/MacOS/arm64/TipCL.pdb",
            Mode = Mode644,
            Length = 4,
            IsDirectory = false,
            Head = new byte[] { 1, 2, 3, 4 },
        });
        entries.Add(new TarEntrySnapshot
        {
            Name = "TipCL.app/Contents/MacOS/x64/helper.exe",
            Mode = Mode644,
            Length = 4,
            IsDirectory = false,
            Head = new byte[] { 1, 2, 3, 4 },
        });

        VerifyResult result = Verify(bundle, entries);

        Assert.False(result.AllRequiredPassed);
        Assert.Contains(result.Checks, c => c.Name.StartsWith("no .pdb", StringComparison.Ordinal) && !c.Ok);
    }

    [Fact]
    public void Verifier_reports_signed_and_unsigned_mach_o_counts_without_failing()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);

        VerifyResult result = Verify(bundle, bundle.WriteAndReadArchive());

        // 2 个 apphost + 2 个 createdump 带 0x1D;2 个 dylib 不带
        Assert.Equal(6, result.MachOCount);
        Assert.Equal(4, result.MachOWithCodeSignature);
        Assert.Equal(2, result.MachOWithoutCodeSignature);
        Assert.Contains(result.Report, line => line.Contains("libSkiaSharp.dylib", StringComparison.Ordinal));

        CheckResult report = result.Checks.Single(c => c.Name == "code-signature report");
        Assert.False(report.Required);
        Assert.True(report.Ok);
        Assert.Contains("with LC_CODE_SIGNATURE=4", report.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Verifier_fails_on_an_empty_archive()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);

        VerifyResult result = Verify(bundle, new List<TarEntrySnapshot>());

        Assert.False(result.AllRequiredPassed);
        Assert.Equal(0, result.EntryCount);
    }

    [Fact]
    public void Check_lines_start_with_OK_or_FAIL_and_use_ascii_only()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);

        VerifyResult result = Verify(bundle, bundle.WriteAndReadArchive());

        foreach (CheckResult check in result.Checks)
        {
            string line = BundleVerifier.FormatLine(check);
            Assert.True(line.StartsWith("OK", StringComparison.Ordinal) || line.StartsWith("FAIL", StringComparison.Ordinal));
            foreach (char c in line)
            {
                Assert.InRange(c, (char)0x20, (char)0x7E);
            }
        }
    }

    [Fact]
    public void WriteTar_refuses_a_name_that_does_not_fit_the_ustar_name_field()
    {
        using MemoryStream stream = new MemoryStream();
        List<TarSourceEntry> entries = new List<TarSourceEntry>
        {
            TarSourceEntry.FromBytes(
                "TipCL.app/Contents/MacOS/arm64/" + new string('x', 100), Mode644, new byte[] { 1 }),
        };

        // 写 Ustar(而不是 Pax)的代价:名字超过 100 字节必须**显式失败**,不能静默截断
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => TarGz.WriteTar(entries, stream));
        Assert.Contains("too long", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_longest_real_publish_name_still_fits_the_ustar_name_field()
    {
        // 实测:osx-arm64 / osx-x64 的 publish 输出里最长的文件名是这一条(83 字节)
        string longest = "TipCL.app/Contents/MacOS/arm64/libSystem.Security.Cryptography.Native.OpenSsl.dylib";

        Assert.True(longest.Length <= TarGz.NameMaxBytes, "最长名字 " + longest.Length + " 字节");
        Assert.False(BundleLayout.NormalizeEntryName(longest).Contains('\\', StringComparison.Ordinal));
    }

    [Fact]
    public void WriteTarGz_is_deterministic_at_the_byte_level()
    {
        using TestHome home = new TestHome();
        TinyBundle bundle = B3PackagerFixtures.CreateTinyBundle(home.Dir);

        byte[] first = bundle.WriteTarGzBytes();
        byte[] second = bundle.WriteTarGzBytes();

        // gzip 头里也不许出现当前时间(.NET 的 GZipStream 写 MTIME=0)
        Assert.Equal(first, second);
        Assert.Equal(0, first[4]);
        Assert.Equal(0, first[5]);
        Assert.Equal(0, first[6]);
        Assert.Equal(0, first[7]);
    }

    private static VerifyResult Verify(TinyBundle bundle, IReadOnlyList<TarEntrySnapshot> entries)
    {
        return BundleVerifier.Verify(entries, InfoPlist.ForTipCL(), bundle.Slots);
    }

    private static string Failures(VerifyResult result)
    {
        return string.Join(
            "; ",
            result.Checks.Where(c => !c.Ok).Select(c => c.Name + ": " + c.Detail));
    }
}
