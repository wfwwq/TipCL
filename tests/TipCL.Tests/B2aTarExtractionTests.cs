using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2a:mac 的 tar.gz 解压(<c>JavaRuntimeFiles.SafeExtractTarGz</c>)。压缩包在测试里现造,
/// 全部落在 <c>TestHome.Root</c> 之下;权限位与符号链接走**假委托**(本机是 Windows,
/// 既改不了 unix 权限位、也不该真建链接),不联网、不起进程。
///
/// 与既有实现(<c>Utils.SafeExtractTargz</c>,本包未改)的两处差异正是本包的验收点:
/// 旧实现 <c>File.WriteAllBytes</c> 落盘、丢掉 tar 里的 mode,且 <c>type '1'/'2'</c> 一律 continue(软链丢失)。
/// </summary>
public class B2aTarExtractionTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(TestHome.Root, "b2a-tar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string MakeTarGz(string dir, string fileName, Action<TarWriter> write)
    {
        string path = Path.Combine(dir, fileName);
        using FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using GZipStream gzip = new GZipStream(stream, CompressionMode.Compress);
        using TarWriter writer = new TarWriter(gzip, TarEntryFormat.Ustar);
        write(writer);
        return path;
    }

    private static UstarTarEntry FileEntry(string name, string content, UnixFileMode mode)
    {
        UstarTarEntry entry = new UstarTarEntry(TarEntryType.RegularFile, name);
        entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        entry.Mode = mode;
        return entry;
    }

    private sealed class Recorder
    {
        public List<KeyValuePair<string, UnixFileMode>> Modes { get; } = new List<KeyValuePair<string, UnixFileMode>>();

        public List<KeyValuePair<string, string>> Links { get; } = new List<KeyValuePair<string, string>>();

        public List<string> Warnings { get; } = new List<string>();

        public JavaRuntimeFiles.TarHooks Hooks()
        {
            return new JavaRuntimeFiles.TarHooks
            {
                IsWindows = false,
                SetMode = delegate(string path, UnixFileMode mode)
                {
                    Modes.Add(new KeyValuePair<string, UnixFileMode>(path, mode));
                },
                CreateLink = delegate(string link, string target)
                {
                    Links.Add(new KeyValuePair<string, string>(link, target));
                },
                Warn = delegate(string message)
                {
                    Warnings.Add(message);
                },
            };
        }
    }

    /// <summary>权限位来自 tar 的 mode(0755 / 0644);文件内容正常落盘。</summary>
    [Fact]
    public void Extraction_preserves_contents_and_tar_modes()
    {
        string dir = TempDir();
        string tar = MakeTarGz(dir, "jre.tar.gz", delegate(TarWriter w)
        {
            w.WriteEntry(FileEntry("zulu-17.jre/Contents/Home/hello.txt", "hello-mac",
                JavaRuntimeFiles.ModeForEntry(executable: false)));
            w.WriteEntry(FileEntry("zulu-17.jre/Contents/Home/bin/java", "#!/bin/sh\n",
                JavaRuntimeFiles.ModeForEntry(executable: true)));
        });
        string dest = Path.Combine(dir, "out");
        Recorder rec = new Recorder();

        JavaRuntimeFiles.SafeExtractTarGz(tar, dest, rec.Hooks());

        string home = Path.Combine(dest, "zulu-17.jre", "Contents", "Home");
        Assert.Equal("hello-mac", File.ReadAllText(Path.Combine(home, "hello.txt")));
        Assert.Equal("#!/bin/sh\n", File.ReadAllText(Path.Combine(home, "bin", "java")));
        Assert.Contains(rec.Modes, kv => kv.Key.EndsWith(Path.Combine("bin", "java"), StringComparison.Ordinal)
            && kv.Value == JavaRuntimeFiles.ModeForEntry(executable: true));
        Assert.Contains(rec.Modes, kv => kv.Key.EndsWith("hello.txt", StringComparison.Ordinal)
            && kv.Value == JavaRuntimeFiles.ModeForEntry(executable: false));
        Assert.Empty(rec.Warnings);
    }

    /// <summary>tar 里 mode 为空(0)时用 0644 兜底,而不是留 0。</summary>
    [Fact]
    public void A_zero_mode_falls_back_to_0644()
    {
        string dir = TempDir();
        string tar = MakeTarGz(dir, "zeromode.tar.gz", delegate(TarWriter w)
        {
            w.WriteEntry(FileEntry("a/b.txt", "x", (UnixFileMode)0));
        });
        Recorder rec = new Recorder();

        JavaRuntimeFiles.SafeExtractTarGz(tar, Path.Combine(dir, "out"), rec.Hooks());

        Assert.Contains(rec.Modes, kv => kv.Key.EndsWith("b.txt", StringComparison.Ordinal)
            && kv.Value == JavaRuntimeFiles.ModeForEntry(executable: false));
    }

    /// <summary>setuid / setgid / sticky 位不许落盘(只保留 0777)。</summary>
    [Fact]
    public void Ioctl_style_bits_are_masked_away()
    {
        string dir = TempDir();
        string tar = MakeTarGz(dir, "setuid.tar.gz", delegate(TarWriter w)
        {
            w.WriteEntry(FileEntry("a/java", "x", (UnixFileMode)0x1ED | UnixFileMode.SetUser | UnixFileMode.SetGroup));
        });
        Recorder rec = new Recorder();

        JavaRuntimeFiles.SafeExtractTarGz(tar, Path.Combine(dir, "out"), rec.Hooks());

        Assert.Contains(rec.Modes, kv => kv.Value == JavaRuntimeFiles.ModeForEntry(executable: true));
        Assert.DoesNotContain(rec.Modes, kv => (kv.Value & UnixFileMode.SetUser) != 0);
        Assert.DoesNotContain(rec.Modes, kv => (kv.Value & UnixFileMode.SetGroup) != 0);
    }

    /// <summary>相对软链按原样交给建链接的出口(线上清单里的形态是 <c>../java.base/LICENSE</c>)。</summary>
    [Fact]
    public void A_relative_symlink_is_handed_to_the_link_creator_verbatim()
    {
        string dir = TempDir();
        string tar = MakeTarGz(dir, "links.tar.gz", delegate(TarWriter w)
        {
            w.WriteEntry(FileEntry("jre.bundle/Contents/Home/legal/java.base/LICENSE", "license-text",
                JavaRuntimeFiles.ModeForEntry(executable: false)));
            UstarTarEntry link = new UstarTarEntry(TarEntryType.SymbolicLink,
                "jre.bundle/Contents/Home/legal/java.compiler/LICENSE");
            link.LinkName = "../java.base/LICENSE";
            w.WriteEntry(link);
        });
        string dest = Path.Combine(dir, "out");
        Recorder rec = new Recorder();

        JavaRuntimeFiles.SafeExtractTarGz(tar, dest, rec.Hooks());

        KeyValuePair<string, string> created = Assert.Single(rec.Links);
        Assert.Equal(Path.Combine(dest, "jre.bundle", "Contents", "Home", "legal", "java.compiler", "LICENSE"),
            Path.GetFullPath(created.Key));
        Assert.Equal("../java.base/LICENSE", created.Value);
        Assert.False(File.Exists(created.Key), "假委托不该真的建出链接来");
    }

    /// <summary>链接目标逃出目标目录 → 跳过并记日志,其余条目照旧落盘。</summary>
    [Fact]
    public void A_symlink_escaping_the_destination_is_skipped_with_a_warning()
    {
        string dir = TempDir();
        string tar = MakeTarGz(dir, "evil-link.tar.gz", delegate(TarWriter w)
        {
            w.WriteEntry(FileEntry("a/keep.txt", "kept", JavaRuntimeFiles.ModeForEntry(executable: false)));
            UstarTarEntry link = new UstarTarEntry(TarEntryType.SymbolicLink, "a/escape");
            link.LinkName = "../../../../etc/passwd";
            w.WriteEntry(link);
        });
        string dest = Path.Combine(dir, "out");
        Recorder rec = new Recorder();

        JavaRuntimeFiles.SafeExtractTarGz(tar, dest, rec.Hooks());

        Assert.Empty(rec.Links);
        Assert.Single(rec.Warnings);
        Assert.Contains("跳过", rec.Warnings[0], StringComparison.Ordinal);
        Assert.Equal("kept", File.ReadAllText(Path.Combine(dest, "a", "keep.txt")));
    }

    /// <summary>条目路径逃出目标目录 → 抛 ValueError(与旧实现一致),且不许在外面留下文件。</summary>
    [Theory]
    [InlineData("../escaped.txt")]
    [InlineData("a/../../escaped.txt")]
    public void An_entry_escaping_the_destination_throws_and_writes_nothing_outside(string entryName)
    {
        string dir = TempDir();
        string tar = MakeTarGz(dir, "escape.tar.gz", delegate(TarWriter w)
        {
            w.WriteEntry(FileEntry(entryName, "bad", JavaRuntimeFiles.ModeForEntry(executable: false)));
        });
        string dest = Path.Combine(dir, "out");
        Recorder rec = new Recorder();

        Assert.Throws<ValueError>(() => JavaRuntimeFiles.SafeExtractTarGz(tar, dest, rec.Hooks()));
        Assert.Empty(rec.Modes);
        Assert.False(File.Exists(Path.Combine(dir, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(TestHome.Root, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(dest, "escaped.txt")));
    }

    /// <summary>
    /// 红线:Windows / Linux 上 <c>ExtractJavaArchive</c> 必须一字不改地走原来的 <c>DownloadManager.ExtractArchive</c>
    /// —— 权限位出口一次都不该被调用,软链也不该被建(旧实现的行为)。
    /// </summary>
    [Fact]
    public void Windows_and_linux_still_go_through_the_old_extractor()
    {
        string dir = TempDir();
        string tar = MakeTarGz(dir, "jre.tar.gz", delegate(TarWriter w)
        {
            w.WriteEntry(FileEntry("zulu-17.jre/Contents/Home/hello.txt", "legacy-path",
                JavaRuntimeFiles.ModeForEntry(executable: true)));
            UstarTarEntry link = new UstarTarEntry(TarEntryType.SymbolicLink, "zulu-17.jre/Contents/Home/link");
            link.LinkName = "../hello.txt";
            w.WriteEntry(link);
        });
        string dest = Path.Combine(dir, "out");
        Recorder rec = new Recorder();

        JavaRuntimeFiles.ExtractJavaArchive(tar, dest, isMac: false, rec.Hooks());

        Assert.Equal("legacy-path", File.ReadAllText(Path.Combine(dest, "zulu-17.jre", "Contents", "Home", "hello.txt")));
        Assert.Empty(rec.Modes);
        Assert.Empty(rec.Links);
    }

    /// <summary>mac 分支按后缀选解压器:.tar.gz 走新实现(hooks 生效);无法识别的后缀抛 DownloadError。</summary>
    [Fact]
    public void Unsupported_archives_still_throw_on_the_mac_branch()
    {
        string dir = TempDir();
        string bogus = Path.Combine(dir, "jre.7z");
        File.WriteAllBytes(bogus, new byte[] { 1, 2, 3 });

        Assert.Throws<DownloadError>(() => JavaRuntimeFiles.ExtractJavaArchive(bogus, Path.Combine(dir, "out"), isMac: true));
    }
}
