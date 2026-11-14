// M4-H P7:文件类型识别(统一拖放入口的判据)。
// 来源:Windows 参考仓库 src/TipCL/Services/FileKinds.cs(671 行)。
//
// 口径(见 docs/PARITY.md §5 的 FileDrop/FileKinds 行与 §8):
// - 保留同一批 kind 常量、Label/Action 文案与「Verdict(路径/名字/候选类型/是否确定/说明)」的判定形状;
// - Windows 侧用「扫描 zip 成员 + 读 mrpack / CurseForge manifest + 逐层嵌套探测(MaxNest/ScanLimit)」,
//   Linux 侧只读包内成员名与几个关键 JSON(不递归解包),够区分插件/整合包/资源包/光影/数据包/存档/模组;
// - `wallpaper` 不列入:Linux 启动页用的是「截图 / 内置 hero」背景,没有自定义壁纸消费方
//   (Wallpaper 服务在 P7 已判 mp4 与目录轮播不做),列进来就是假入口。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace TipCL.App.Common;

/// <summary>拖放进来的文件是什么。</summary>
public static class FileKinds
{
    public const string Modpack = "modpack";
    public const string Mod = "mod";
    public const string ResourcePack = "resourcepack";
    public const string ShaderPack = "shaderpack";
    public const string DataPack = "datapack";
    public const string World = "world";
    public const string Skin = "skin";
    public const string Plugin = "plugin";

    /// <summary>可受理的类型(顺序即「认不准」时给用户的候选顺序)。</summary>
    public static readonly string[] AllKinds = { Modpack, Mod, ResourcePack, ShaderPack, DataPack, World, Skin, Plugin };

    public static string Label(string kind) => kind switch
    {
        Modpack => "整合包",
        Mod => "模组",
        ResourcePack => "资源包",
        ShaderPack => "光影",
        DataPack => "数据包",
        World => "存档",
        Skin => "皮肤",
        Plugin => "TipCL 插件",
        _ => kind,
    };

    public static string Action(string kind) => kind switch
    {
        Modpack => "装成一个新版本",
        Mod => "装进实例的 mods/",
        ResourcePack => "装进实例的 resourcepacks/",
        ShaderPack => "装进实例的 shaderpacks/",
        DataPack => "装进实例的 datapacks/",
        World => "装进实例的 saves/",
        Skin => "设为离线账号的皮肤",
        Plugin => "安装到 TipCL 插件目录",
        _ => "不知道放哪儿",
    };

    /// <summary>一次识别结果。</summary>
    public sealed class Verdict
    {
        public string Path = "";
        public string Name = "";
        /// <summary>可能的类型(认准时只有一个)。</summary>
        public List<string> Kinds = new List<string>();
        /// <summary>是否敢确定。</summary>
        public bool Sure;
        /// <summary>给用户看的一句话说明。</summary>
        public string Detail = "";

        /// <summary>候选集合的签名(批量拖入时同签名的文件合并成一个提问)。</summary>
        public string KindSig => string.Join(",", Kinds.OrderBy(x => x, StringComparer.Ordinal));
    }

    public static bool IsImage(string path)
    {
        string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".gif";
    }

    /// <summary>识别一个路径(文件或目录)。认不准时 Kinds 里给出候选,由调用方问用户。</summary>
    public static Verdict Identify(string path)
    {
        var v = new Verdict { Path = path ?? "", Name = Path.GetFileName((path ?? "").TrimEnd('/', '\\')) };
        try
        {
            if (Directory.Exists(path))
            {
                return IdentifyDir(v, path);
            }
            if (!File.Exists(path))
            {
                v.Kinds.Add(Mod);
                v.Detail = "找不到这个文件。";
                return v;
            }
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".mrpack")
            {
                v.Kinds.Add(Modpack);
                v.Sure = true;
                v.Detail = "Modrinth 整合包(.mrpack)";
                return v;
            }
            if (ext == ".zip" || ext == ".jar")
            {
                return IdentifyArchive(v, path, ext == ".jar");
            }
            if (IsImage(path))
            {
                var size = PngSize(path);
                if (size is { } s && ((s.W == 64 && s.H == 64) || (s.W == 64 && s.H == 32)))
                {
                    v.Kinds.Add(Skin);
                    v.Sure = true;
                    v.Detail = $"Minecraft 皮肤({s.W}×{s.H})";
                    return v;
                }
                v.Kinds.Add(Skin);
                v.Detail = "看起来是图片;64×64 / 64×32 的 PNG 才能当皮肤。";
                return v;
            }
            v.Kinds.AddRange(new[] { Mod, ResourcePack, ShaderPack, DataPack, World, Skin, Plugin });
            v.Detail = "这个后缀不认识。";
            return v;
        }
        catch (Exception ex)
        {
            v.Kinds.AddRange(new[] { Mod, ResourcePack, ShaderPack, DataPack, World, Skin, Plugin });
            v.Detail = "读这个文件出错了:" + ex.Message;
            return v;
        }
    }

    private static Verdict IdentifyDir(Verdict v, string dir)
    {
        if (File.Exists(Path.Combine(dir, "tipcl-plugin.json")))
        {
            v.Kinds.Add(Plugin);
            v.Sure = true;
            v.Detail = "带 tipcl-plugin.json 的插件文件夹";
            return v;
        }
        if (File.Exists(Path.Combine(dir, "level.dat")))
        {
            v.Kinds.Add(World);
            v.Sure = true;
            v.Detail = "带 level.dat 的存档文件夹";
            return v;
        }
        bool plainPack = Directory.Exists(Path.Combine(dir, "mods")) || Directory.Exists(Path.Combine(dir, "versions"))
                         || File.Exists(Path.Combine(dir, "mmc-pack.json"));
        if (plainPack)
        {
            v.Kinds.Add(Modpack);
            v.Sure = true;
            v.Detail = "整合包文件夹(mods/ 或 versions/)";
            return v;
        }
        v.Kinds.AddRange(new[] { Modpack, Plugin, World });
        v.Detail = "文件夹里没有能认出来的标志(插件要 tipcl-plugin.json,存档要 level.dat,整合包要有 mods/ 或 versions/)。";
        return v;
    }

    private static Verdict IdentifyArchive(Verdict v, string path, bool jar)
    {
        List<string> names = EntryNames(path);
        bool Has(string target) => names.Any(n => string.Equals(n, target, StringComparison.OrdinalIgnoreCase));
        bool HasPrefix(string prefix) => names.Any(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (Has("tipcl-plugin.json") || HasPrefix("tipcl-plugin.json"))
        {
            v.Kinds.Add(Plugin);
            v.Sure = true;
            v.Detail = "TipCL 插件包(tipcl-plugin.json)";
            return v;
        }
        if (Has("modrinth.index.json"))
        {
            v.Kinds.Add(Modpack);
            v.Sure = true;
            v.Detail = "Modrinth 整合包(modrinth.index.json)";
            return v;
        }
        if (Has("manifest.json") && Has("overrides") || Has("manifest.json") && HasPrefix("modrinth"))
        {
            v.Kinds.Add(Modpack);
            v.Sure = true;
            v.Detail = "CurseForge 整合包(manifest.json)";
            return v;
        }
        if (Has("level.dat"))
        {
            v.Kinds.Add(World);
            v.Sure = true;
            v.Detail = "存档压缩包(level.dat)";
            return v;
        }
        bool isMod = Has("fabric.mod.json") || Has("quilt.mod.json") || Has("mcmod.info")
                     || HasPrefix("META-INF/mods.toml") || HasPrefix("META-INF/neoforge.mods.toml");
        bool hasPackMeta = Has("pack.mcmeta") || HasPrefix("pack.mcmeta");
        if (hasPackMeta && HasPrefix("assets/"))
        {
            v.Kinds.Add(ResourcePack);
            v.Sure = true;
            v.Detail = "资源包(pack.mcmeta + assets/)";
            return v;
        }
        if (hasPackMeta && HasPrefix("data/"))
        {
            v.Kinds.Add(DataPack);
            v.Sure = true;
            v.Detail = "数据包(pack.mcmeta + data/)";
            return v;
        }
        if (HasPrefix("shaders/") && !isMod)
        {
            v.Kinds.Add(ShaderPack);
            v.Sure = true;
            v.Detail = "光影包(shaders/)";
            return v;
        }
        if (isMod)
        {
            v.Kinds.Add(Mod);
            v.Sure = true;
            v.Detail = jar ? "模组 jar" : "模组压缩包";
            return v;
        }
        v.Kinds.AddRange(jar ? new[] { Mod } : new[] { Mod, Modpack, ResourcePack, ShaderPack, DataPack, World });
        v.Detail = "包里的内容认不准。";
        return v;
    }

    /// <summary>zip / jar 的成员名(不递归解包)。</summary>
    internal static List<string> EntryNames(string path)
    {
        var list = new List<string>();
        try
        {
            using var zip = ZipFile.OpenRead(path);
            foreach (var e in zip.Entries)
            {
                string n = e.FullName.Replace('\\', '/');
                if (n.Length > 0) list.Add(n);
                if (list.Count > 5000) break;
            }
        }
        catch (Exception)
        {
        }
        return list;
    }

    /// <summary>PNG 的 IHDR 尺寸(非 PNG 返回 null)。</summary>
    private static (int W, int H)? PngSize(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> head = stackalloc byte[24];
            if (fs.Read(head) < 24) return null;
            if (head[0] != 0x89 || head[1] != 0x50 || head[2] != 0x4E || head[3] != 0x47) return null;
            int w = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
            int h = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
            return (w, h);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
