// M4-H P7:统一拖放入口的识别判据(Common/FileKinds)测试。
// 每个包用临时目录现造,不依赖任何真实文件;不联网、不碰真实 TIPCL_HOME。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using TipCL.App.Common;
using Xunit;

namespace TipCL.Tests.App;

public class FileKindsTests : IDisposable
{
    private readonly string _dir;

    public FileKindsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tipcl-filekinds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { }
    }

    /// <summary>造一个 zip,entries 是「包内路径 → 内容」(目录用结尾 / 表示)。</summary>
    private string Zip(string name, params (string Path, string Content)[] entries)
    {
        string zip = Path.Combine(_dir, name);
        using var fs = File.Create(zip);
        using var z = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (p, c) in entries)
        {
            var e = z.CreateEntry(p);
            using var s = e.Open();
            byte[] bytes = Encoding.UTF8.GetBytes(c);
            s.Write(bytes, 0, bytes.Length);
        }
        return zip;
    }

    private string FileIn(string name, byte[] bytes)
    {
        string p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    private string DirWith(string name, params string[] relFiles)
    {
        string d = Path.Combine(_dir, name);
        foreach (string rel in relFiles)
        {
            string full = Path.Combine(d, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "{}");
        }
        return d;
    }

    [Fact]
    public void PluginZip_IsIdentified_Sure()
    {
        string zip = Zip("p.zip", ("tipcl-plugin.json", "{\"id\":\"a\"}"), ("index.js", ""));
        var v = FileKinds.Identify(zip);
        Assert.True(v.Sure);
        Assert.Equal(new[] { FileKinds.Plugin }, v.Kinds);
    }

    [Fact]
    public void ModrinthModpack_IsIdentified()
    {
        var v = FileKinds.Identify(Zip("m.mrpack", ("modrinth.index.json", "{}")));
        Assert.True(v.Sure);
        Assert.Equal(FileKinds.Modpack, v.Kinds[0]);

        var v2 = FileKinds.Identify(Zip("m2.zip", ("modrinth.index.json", "{}"), ("overrides/mods/x.jar", "")));
        Assert.True(v2.Sure);
        Assert.Equal(FileKinds.Modpack, v2.Kinds[0]);
    }

    [Fact]
    public void ResourcePackAndDataPack_AreSeparatedByContents()
    {
        var rp = FileKinds.Identify(Zip("rp.zip", ("pack.mcmeta", "{}"), ("assets/minecraft/lang/zh_cn.json", "{}")));
        Assert.True(rp.Sure);
        Assert.Equal(FileKinds.ResourcePack, rp.Kinds[0]);

        var dp = FileKinds.Identify(Zip("dp.zip", ("pack.mcmeta", "{}"), ("data/x/functions/a.mcfunction", "")));
        Assert.True(dp.Sure);
        Assert.Equal(FileKinds.DataPack, dp.Kinds[0]);
    }

    [Fact]
    public void ShaderPack_And_World_AreIdentified()
    {
        var sh = FileKinds.Identify(Zip("sh.zip", ("shaders/final.fsh", "")));
        Assert.True(sh.Sure);
        Assert.Equal(FileKinds.ShaderPack, sh.Kinds[0]);

        var w = FileKinds.Identify(Zip("w.zip", ("level.dat", ""), ("region/r.0.0.mca", "")));
        Assert.True(w.Sure);
        Assert.Equal(FileKinds.World, w.Kinds[0]);
    }

    [Fact]
    public void ModJar_IsIdentified()
    {
        string jar = Zip("mod.jar", ("fabric.mod.json", "{}"), ("x/Mod.class", ""));
        var v = FileKinds.Identify(jar);
        Assert.True(v.Sure);
        Assert.Equal(FileKinds.Mod, v.Kinds[0]);

        string forge = Zip("forge.jar", ("META-INF/mods.toml", ""), ("x.class", ""));
        Assert.Equal(FileKinds.Mod, FileKinds.Identify(forge).Kinds[0]);
    }

    [Fact]
    public void Directories_AreIdentified()
    {
        Assert.Equal(FileKinds.Plugin, FileKinds.Identify(DirWith("plug", "tipcl-plugin.json")).Kinds[0]);
        Assert.True(FileKinds.Identify(DirWith("plug", "tipcl-plugin.json")).Sure);
        Assert.Equal(FileKinds.World, FileKinds.Identify(DirWith("world", "level.dat")).Kinds[0]);
        Assert.Equal(FileKinds.Modpack, FileKinds.Identify(DirWith("pack", "mods/a.jar")).Kinds[0]);
    }

    [Fact]
    public void UnknownExtension_IsNotSureAndOffersCandidates()
    {
        string p = FileIn("mystery.bin", new byte[] { 1, 2, 3 });
        var v = FileKinds.Identify(p);
        Assert.False(v.Sure);
        Assert.True(v.Kinds.Count > 1, "认不准时要给候选类型让用户选");
        Assert.Contains(FileKinds.Plugin, v.Kinds);
    }

    [Fact]
    public void PngSkin_IsIdentifiedBySize()
    {
        // 最小 PNG 头:签名 + IHDR 长度/类型 + 宽高(64×64)
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        bytes.AddRange(new byte[] { 0, 0, 0, 13 });
        bytes.AddRange(Encoding.ASCII.GetBytes("IHDR"));
        bytes.AddRange(new byte[] { 0, 0, 0, 64, 0, 0, 0, 64, 8, 6, 0, 0, 0 });
        string png = FileIn("skin.png", bytes.ToArray());

        var v = FileKinds.Identify(png);
        Assert.True(v.Sure);
        Assert.Equal(FileKinds.Skin, v.Kinds[0]);

        // 其它尺寸的图片不给「确定」:用户可以自己选(皮肤之外 Linux 没有别的图片消费方)
        bytes[16] = 0; bytes[17] = 0; bytes[18] = 1; bytes[19] = 44; // 300 宽
        string big = FileIn("big.png", bytes.ToArray());
        Assert.False(FileKinds.Identify(big).Sure);
    }

    [Fact]
    public void EveryKind_HasLabelAndAction()
    {
        foreach (string k in FileKinds.AllKinds)
        {
            Assert.False(string.IsNullOrWhiteSpace(FileKinds.Label(k)), k + " 缺中文标签");
            Assert.NotEqual(k, FileKinds.Label(k));
            Assert.False(string.IsNullOrWhiteSpace(FileKinds.Action(k)), k + " 缺动作说明");
        }
        // 壁纸不在受理类型里(Linux 没有自定义壁纸消费方,列出来是假入口)
        Assert.DoesNotContain("wallpaper", FileKinds.AllKinds);
    }
}
