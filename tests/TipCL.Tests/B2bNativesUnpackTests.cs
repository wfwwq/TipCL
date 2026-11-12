using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B2b 第 4 项:natives 的解压与目录形态在 mac 上是否正确(**只核对,不改代码**)。
///
/// 期望值来源:源码 <c>DownloadManager.ExtractJarNatives</c>(1179-1202 行)、
/// <c>Installer.NativesPresent</c>(330-348 行)、<c>Installer.NativesDir</c>(350-357 行)、
/// <c>PPath</c>(非 Windows 分支用正斜杠)。
/// 用例只在 <see cref="TestHome"/> 之下造临时目录与 zip,不联网、不起进程、不碰用户数据。
/// </summary>
public class B2bNativesUnpackTests
{
    /// <summary>造一个 natives jar:mac 的 .dylib / .jnilib、windows 的 .dll、linux 的 .so、以及 META-INF。</summary>
    private static string MakeNativesJar(TestHome home, string fileName, params string[] entries)
    {
        string path = Path.Combine(home.Dir, fileName);
        using FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (string entry in entries)
        {
            ZipArchiveEntry e = zip.CreateEntry(entry);
            using StreamWriter writer = new StreamWriter(e.Open());
            writer.Write("x");
        }
        return path;
    }

    /// <summary>mac 需要的扩展名(.dylib / .jnilib)与 Windows 的 .dll 都要被解出来 —— 代码里没有扩展名过滤。</summary>
    [Fact]
    public void Every_entry_is_extracted_without_an_extension_filter()
    {
        using TestHome home = new TestHome();
        string jar = MakeNativesJar(
            home, "natives-osx.jar",
            "liblwjgl.dylib",
            "liblwjgl.jnilib",
            "lwjgl.dll",
            "liblwjgl.so",
            "META-INF/MANIFEST.MF");
        string dest = Path.Combine(home.Dir, "natives");
        DownloadManager.ExtractJarNatives(jar, dest);
        Assert.True(File.Exists(Path.Combine(dest, "liblwjgl.dylib")));
        Assert.True(File.Exists(Path.Combine(dest, "liblwjgl.jnilib")));
        Assert.True(File.Exists(Path.Combine(dest, "lwjgl.dll")));
        Assert.True(File.Exists(Path.Combine(dest, "liblwjgl.so")));
        Assert.True(File.Exists(Path.Combine(dest, "META-INF", "MANIFEST.MF")));
    }

    /// <summary>exclude 前缀过滤照旧(JSON 里的 <c>extract.exclude</c>)。</summary>
    [Fact]
    public void Exclude_prefixes_are_still_honoured()
    {
        using TestHome home = new TestHome();
        string jar = MakeNativesJar(home, "ex.jar", "liblwjgl.dylib", "META-INF/MANIFEST.MF");
        string dest = Path.Combine(home.Dir, "natives-ex");
        DownloadManager.ExtractJarNatives(jar, dest, new List<string> { "META-INF/" });
        Assert.True(File.Exists(Path.Combine(dest, "liblwjgl.dylib")));
        Assert.False(File.Exists(Path.Combine(dest, "META-INF", "MANIFEST.MF")));
    }

    /// <summary>zip 内的目录条目('/' 结尾)跳过,不建空目录。</summary>
    [Fact]
    public void Directory_entries_are_skipped()
    {
        using TestHome home = new TestHome();
        string jar = MakeNativesJar(home, "dir.jar", "META-INF/", "liblwjgl.dylib");
        string dest = Path.Combine(home.Dir, "natives-dir");
        DownloadManager.ExtractJarNatives(jar, dest);
        Assert.True(File.Exists(Path.Combine(dest, "liblwjgl.dylib")));
        Assert.False(Directory.Exists(Path.Combine(dest, "META-INF")));
    }

    /// <summary>逃出目标目录的条目要抛(下载管理器原有的卫生检查,与平台无关)。</summary>
    [Fact]
    public void Path_escape_is_rejected()
    {
        using TestHome home = new TestHome();
        string jar = MakeNativesJar(home, "evil.jar", "../evil.dylib");
        string dest = Path.Combine(home.Dir, "natives-evil");
        Assert.Throws<DownloadError>(() => DownloadManager.ExtractJarNatives(jar, dest));
    }

    /// <summary>NativesPresent 认 mac 的文件名:<c>liblwjgl.dylib</c> 也算「有 natives」。</summary>
    [Fact]
    public void Natives_present_recognises_mac_library_names()
    {
        using TestHome home = new TestHome();
        string dir = Path.Combine(home.Dir, "natives-mac");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "liblwjgl.dylib"), "x");
        Assert.True(Installer.NativesPresent(dir));
    }

    /// <summary>Windows 的 lwjgl.dll 也认 —— 判断只看文件名前缀(源码 340-344 行)。</summary>
    [Fact]
    public void Natives_present_recognises_windows_library_names()
    {
        using TestHome home = new TestHome();
        string dir = Path.Combine(home.Dir, "natives-win");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "lwjgl.dll"), "x");
        Assert.True(Installer.NativesPresent(dir));
    }

    /// <summary>目录里只有 META-INF 这类文件时不算有 natives(否则 mac 上会误判成启动就绪)。</summary>
    [Fact]
    public void Natives_present_is_false_for_a_directory_without_libraries()
    {
        using TestHome home = new TestHome();
        string dir = Path.Combine(home.Dir, "natives-empty");
        Directory.CreateDirectory(Path.Combine(dir, "META-INF"));
        File.WriteAllText(Path.Combine(dir, "META-INF", "MANIFEST.MF"), "x");
        Assert.False(Installer.NativesPresent(dir));
        Assert.False(Installer.NativesPresent(Path.Combine(home.Dir, "no-such-dir")));
    }

    /// <summary>classpath 分隔符不是写死的:源码用的是 <c>Path.PathSeparator</c>(改动前 576 行),
    /// 本机(Windows)是 <c>;</c>,mac 上 .NET 给的是 <c>:</c> —— mac 侧无需改代码,这条只钉住本机的取值与用法。</summary>
    [Fact]
    public void Classpath_separator_comes_from_the_runtime_not_a_literal()
    {
        Assert.Equal(';', Path.PathSeparator);
        Assert.Equal(";", Path.PathSeparator.ToString());
        PyDict placeholders = PyDict.Of("classpath_separator", Path.PathSeparator.ToString());
        Assert.Equal("a;b", Utils.ReplacePlaceholders("a${classpath_separator}b", placeholders));
        // mac 上同一行代码拿到的是 ":":这里用别的分隔符证明占位符是数据驱动的,不是写死的分号。
        Assert.Equal("a:b", Utils.ReplacePlaceholders("a${classpath_separator}b", PyDict.Of("classpath_separator", ":")));
    }
}
