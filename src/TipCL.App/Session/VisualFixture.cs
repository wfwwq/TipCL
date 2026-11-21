// AI3-visual:视觉巡检用的固定假数据目录(TIPCL_HOME)。
// i18n:ignore-file —— 这里的中文是假数据(实例名 / 存档名 / 便签),不是界面文案。
//
// 参照 v3 / v2 效果图里的示例数据造出:
//   * 4 个实例:.minecraft(根实例,Fabric 1.20.1,32 个模组)/ 生存 1.21.4(Fabric)/ NeoForge 整合包 / 1.12.2 OptiFine;
//     每个都有版本目录(版本 json + 占位 jar)、合法 zip 的假模组 jar(fabric.mod.json / neoforge.mods.toml / mcmod.info)、
//     存档(gzip NBT 的 level.dat)、servers.dat、截图、日志;
//   * 一个离线账号「Steve」(当前账号);
//   * 本周 + 上周的游戏时长记录(本周 6.2 小时,比上周多 1.4 小时 —— 与 launch_v3 的组件一致);
//   * 版本清单与新闻的**缓存**(Manifest / News 自己「拉不到就用缓存」的机制),巡检进程不联网也有下载页列表和新闻条。
// 不联网、不碰真实用户目录:目标目录必须是空目录,或带本生成器留下的标记文件(此时先清空再重建)。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using TipCL.Core;
using TipCL.Core.Launcher;

namespace TipCL.App.Session;

/// <summary>固定假数据目录生成器。</summary>
internal static class VisualFixture
{
    /// <summary>标记文件:有它才允许清空重建。</summary>
    public const string Marker = ".tipcl-visual-fixture";

    /// <summary>造好的实例名(与效果图同序)。</summary>
    public static readonly string[] InstanceNames = { ".minecraft", "生存 1.21.4", "NeoForge 整合包", "1.12.2 OptiFine" };

    /// <summary>造出来的东西的计数(测试 / 结果文件用)。</summary>
    internal sealed class Summary
    {
        public string Home = "";
        public int Instances;
        public int Versions;
        public int Mods;
        public int Saves;
        public int Servers;
        public int Screenshots;
        public int Sessions;
        public List<string> Files = new();
    }

    private sealed class InstSpec
    {
        public string Name = "";
        public string Mc = "";
        public string Loader = "";      // fabric / neoforge / optifine / ""
        public string LoaderVersion = "";
        public string[] Mods = Array.Empty<string>();
        public string[] Saves = Array.Empty<string>();
        public (string Name, string Ip)[] Servers = Array.Empty<(string, string)>();
        public PyDict? Modpack;

        public string MainVersion => Loader switch
        {
            "fabric" => $"fabric-loader-{LoaderVersion}-{Mc}",
            "neoforge" => $"neoforge-{LoaderVersion}",
            "optifine" => $"{Mc}-OptiFine_{LoaderVersion}",
            _ => Mc,
        };
    }

    private static readonly string[] FabricMods120 =
    {
        "Sodium", "Lithium", "Iris Shaders", "Fabric API", "Mod Menu", "Xaero's Minimap", "Xaero's World Map", "AppleSkin",
        "Jade", "REI", "Cloth Config", "Architectury", "Indium", "Entity Culling", "FerriteCore", "ModernFix",
        "Krypton", "LazyDFU", "Continuity", "Zoomify", "Clumps", "Mouse Tweaks", "Inventory Profiles Next", "Litematica",
        "MaLiLib", "Tweakeroo", "Carpet", "Chat Heads", "Better Third Person", "Sound Physics Remastered", "Dynamic FPS", "Bobby",
    };

    private static readonly string[] FabricMods121 =
    {
        "Sodium", "Lithium", "Fabric API", "Mod Menu", "Jade", "AppleSkin", "Xaero's Minimap", "Simple Voice Chat",
        "FerriteCore", "Entity Culling", "Dynamic FPS", "Cloth Config",
    };

    private static readonly string[] NeoForgeMods =
    {
        "Create", "JEI", "Jade", "Mekanism", "Applied Energistics 2", "Botania", "Farmer's Delight", "Waystones",
        "Sophisticated Backpacks", "Iron Chests", "Curios API", "GeckoLib", "Patchouli", "Balm", "Cloth Config", "Architectury",
        "Embeddium", "FerriteCore", "ModernFix", "Supplementaries", "Quark", "Zeta", "Storage Drawers", "Tinkers' Construct",
    };

    private static List<InstSpec> Specs() => new()
    {
        new InstSpec
        {
            Name = ".minecraft", Mc = "1.20.1", Loader = "fabric", LoaderVersion = "0.15.11", Mods = FabricMods120,
            Saves = new[] { "新的世界", "空岛生存" },
            Servers = new[] { ("生存服", "127.0.0.1:25565"), ("小游戏服", "127.0.0.1:25566") },
        },
        new InstSpec
        {
            Name = "生存 1.21.4", Mc = "1.21.4", Loader = "fabric", LoaderVersion = "0.16.9", Mods = FabricMods121,
            Saves = new[] { "生存世界", "创造测试", "和小林的联机存档" },
            Servers = new[] { ("生存服", "127.0.0.1:25565") },
        },
        new InstSpec
        {
            Name = "NeoForge 整合包", Mc = "1.21.1", Loader = "neoforge", LoaderVersion = "21.1.77", Mods = NeoForgeMods,
            Saves = new[] { "机械动力工厂" },
            Modpack = PyDict.Of("name", "NeoForge 整合包", "version", "2.3.0", "mc_version", "1.21.1", "loader", "neoforge", "loader_version", "21.1.77", "source", "modrinth"),
        },
        new InstSpec
        {
            Name = "1.12.2 OptiFine", Mc = "1.12.2", Loader = "optifine", LoaderVersion = "HD_U_G5",
            Saves = new[] { "老版本存档" },
        },
    };

    /// <summary>
    /// 在 <paramref name="home"/> 里造假数据。目录不存在就建;已存在则必须为空或带 <see cref="Marker"/>(带标记先清空)。
    /// 拒绝真实用户目录(%LOCALAPPDATA%\TipCL、~/.tipcl)。
    /// </summary>
    public static Summary Build(string home, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(home)) throw new ArgumentException("fixture home is empty");
        string full = Path.GetFullPath(home);
        foreach (string real in RealHomes())
        {
            if (string.Equals(Path.TrimEndingDirectorySeparator(full), Path.TrimEndingDirectorySeparator(real), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("refusing to write the visual fixture into the real TipCL home: " + full);
        }
        // logs/ 是 AppLog.Init 在进程入口就建好的(日志文件正开着),不算「非空」也不清掉
        bool Keep(string p) => string.Equals(Path.GetFileName(p), "logs", StringComparison.OrdinalIgnoreCase);
        if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any(p => !Keep(p)))
        {
            if (!File.Exists(Path.Combine(full, Marker)))
                throw new InvalidOperationException("fixture home is not empty and has no " + Marker + " marker: " + full);
            foreach (string d in Directory.EnumerateDirectories(full).Where(p => !Keep(p))) Directory.Delete(d, recursive: true);
            foreach (string f in Directory.EnumerateFiles(full)) File.Delete(f);
        }
        Directory.CreateDirectory(full);
        File.WriteAllText(Path.Combine(full, Marker), "TipCL visual fixture " + now.ToString("O", CultureInfo.InvariantCulture) + "\n");

        var sum = new Summary { Home = full };
        string mcRoot = Path.Combine(full, ".minecraft");
        Directory.CreateDirectory(mcRoot);

        WriteConfig(full);
        WriteAccounts(full);
        WriteCaches(full, now, sum);

        long sessions = 0;
        var playtime = new PyDict();
        foreach (InstSpec spec in Specs())
        {
            string dir = spec.Name == ".minecraft" ? mcRoot : Path.Combine(mcRoot, spec.Name);
            BuildInstance(dir, spec, now, sum);
            PyDict pt = PlaytimeFor(spec, now, out int n);
            playtime[spec.Name] = pt;
            sessions += n;
            sum.Instances++;
        }
        WriteJson(Path.Combine(full, Playtime.PLAYTIME_FILE), PyDict.Of("instances", playtime), sum);
        sum.Sessions = (int)sessions;
        return sum;
    }

    private static IEnumerable<string> RealHomes()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (local.Length > 0) yield return Path.Combine(local, "TipCL");
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 0) yield return Path.Combine(profile, ".tipcl");
    }

    private static void WriteConfig(string home)
    {
        var cfg = new PyDict
        {
            ["instances_dir"] = ".minecraft",
            ["default_instance"] = "",
            ["first_run"] = false,
            ["feedback_consent"] = false,
            ["feedback_heartbeat"] = false,
            ["auto_check_update"] = false,
            ["default_isolation"] = "all",
            ["isolation_defaults"] = "2026.09-isolate-all",
            ["last_user"] = "Steve",
            ["last_user_instance"] = ".minecraft",
            ["ui_dark"] = false,
            ["ui_notes"] = "每周五 20:00 联机,先把整合包更新到 2.3",
            ["download_source"] = "official",
            ["community_source"] = "official",
        };
        Utils.WriteJson(Path.Combine(home, "config.json"), cfg);
    }

    private static void WriteAccounts(string home)
    {
        PyDict steve = PyDict.Of("type", "offline", "name", "Steve", "uuid", "8667ba71-b85a-4004-af54-457a9734eed7", "skin", "steve");
        Utils.WriteJson(Path.Combine(home, "accounts.json"), PyDict.Of("active", "Steve", "accounts", Py.L(steve)));
    }

    // ———————— 缓存:版本清单 + 新闻 ————————

    private static readonly (string Id, string Type, string Date)[] ManifestRows =
    {
        ("26.4-snapshot-7", "snapshot", "2026-11-18"),
        ("26.4-snapshot-6", "snapshot", "2026-11-11"),
        ("26.3", "release", "2026-09-15"),
        ("26.2", "release", "2026-06-16"),
        ("26.1.2", "release", "2026-04-09"),
        ("26.1.1", "release", "2026-04-01"),
        ("26.1", "release", "2026-03-24"),
        ("1.21.11", "release", "2025-12-09"),
        ("1.21.10", "release", "2025-10-07"),
        ("1.21.9", "release", "2025-09-30"),
        ("1.21.8", "release", "2025-07-17"),
        ("1.21.7", "release", "2025-06-30"),
        ("1.21.5", "release", "2025-03-25"),
        ("1.21.4", "release", "2024-12-03"),
        ("1.21.1", "release", "2024-08-08"),
        ("1.20.6", "release", "2024-04-29"),
        ("1.20.4", "release", "2023-12-07"),
        ("1.20.1", "release", "2023-06-12"),
        ("1.19.4", "release", "2023-03-14"),
        ("1.18.2", "release", "2022-02-28"),
        ("1.16.5", "release", "2021-01-15"),
        ("1.12.2", "release", "2017-09-18"),
        ("1.8.9", "release", "2015-12-09"),
        ("b1.7.3", "old_beta", "2011-07-08"),
        ("a1.2.6", "old_alpha", "2010-12-03"),
    };

    private static void WriteCaches(string home, DateTimeOffset now, Summary sum)
    {
        string cache = Path.Combine(home, "cache");
        Directory.CreateDirectory(cache);
        var versions = new List<object>();
        foreach (var (id, type, date) in ManifestRows)
        {
            string t = date + "T10:00:00+00:00";
            versions.Add(PyDict.Of("id", id, "type", type, "url", "https://piston-meta.mojang.com/v1/packages/0000/" + id + ".json",
                "time", t, "releaseTime", t, "sha1", new string('0', 40), "complianceLevel", 1L));
        }
        PyDict manifest = PyDict.Of("latest", PyDict.Of("release", "26.3", "snapshot", "26.4-snapshot-7"), "versions", versions);
        WriteJson(Path.Combine(cache, "version_manifest.json"), manifest, sum);
        // fetched_at = 现在:Manifest 4 小时内直接用缓存,不去拉网
        WriteJson(Path.Combine(cache, "version_manifest.meta"), PyDict.Of("fetched_at", (double)now.ToUnixTimeSeconds(), "url", "fixture"), sum);

        var news = Py.L(
            PyDict.Of("title", "Minecraft 26.4 Snapshot 7 已发布", "body", "新的快照带来了铜傀儡的改动与若干错误修复。", "version", "26.4-snapshot-7", "image", "", "date", "2026-11-18"),
            PyDict.Of("title", "Minecraft 26.3 正式版", "body", "秋季更新已经发布。", "version", "26.3", "image", "", "date", "2026-09-15"),
            PyDict.Of("title", "Minecraft 26.2 正式版", "body", "夏季更新。", "version", "26.2", "image", "", "date", "2026-06-16"));
        WriteJson(Path.Combine(cache, "news.json"), news, sum);
    }

    // ———————— 实例 ————————

    private static void BuildInstance(string dir, InstSpec spec, DateTimeOffset now, Summary sum)
    {
        Directory.CreateDirectory(dir);
        foreach (string d in Instances._STANDARD_DIRS.Distinct()) Directory.CreateDirectory(Path.Combine(dir, d));
        var meta = PyDict.Of("name", spec.Name, "mc_version", spec.Mc, "modpack", spec.Modpack!, "java", Instances.JAVA_AUTO);
        WriteJson(Path.Combine(dir, Instances.INSTANCE_META), meta, sum);

        // 版本:原版 + 加载器版
        string versions = Path.Combine(dir, "versions");
        WriteVersion(versions, spec.Mc, VanillaJson(spec.Mc), sum);
        if (spec.Loader.Length > 0) WriteVersion(versions, spec.MainVersion, LoaderJson(spec), sum);

        // 隔离 = all:模组 / 存档 / 截图 / 日志都在 versions/<主版本>/ 下(与 VersionSettings.GameDir 一致)
        string game = Path.Combine(versions, spec.MainVersion);
        foreach (string d in new[] { "mods", "saves", "resourcepacks", "shaderpacks", "screenshots", "logs", "config" })
            Directory.CreateDirectory(Path.Combine(game, d));

        for (int i = 0; i < spec.Mods.Length; i++)
        {
            WriteModJar(Path.Combine(game, "mods"), spec, spec.Mods[i], i, sum);
        }
        for (int i = 0; i < spec.Saves.Length; i++)
        {
            WriteSave(Path.Combine(game, "saves", spec.Saves[i]), spec.Saves[i], spec.Mc, now.AddHours(-(i * 30 + 3)), sum);
        }
        if (spec.Servers.Length > 0)
        {
            var rows = spec.Servers.Select(s => (object)PyDict.Of("name", s.Name, "ip", s.Ip, "hidden", 0L)).ToList();
            foreach (string target in new[] { dir, game })
            {
                Terracotta.WriteGameServers(Path.Combine(target, "servers.dat"), rows);
                sum.Files.Add(Path.Combine(target, "servers.dat"));
            }
            sum.Servers += spec.Servers.Length;
        }
        // 启动页背景 = 所选版本最新的一张截图(没有才用内置 hero.jpg):.minecraft 是对照效果图的那个实例,
        // 不放截图,否则 64x36 的假截图会被拉伸成整屏背景(看起来像渲染故障)
        int shots = spec.Name == ".minecraft" ? 0 : 3;
        for (int i = 0; i < shots; i++)
        {
            string name = now.AddDays(-i).ToString("yyyy-MM-dd_HH.mm.ss", CultureInfo.InvariantCulture) + ".png";
            File.WriteAllBytes(Path.Combine(game, "screenshots", name), Png(64, 36, i));
            sum.Screenshots++;
        }
        File.WriteAllText(Path.Combine(game, "logs", "latest.log"),
            "[10:00:00] [main/INFO]: Loading Minecraft " + spec.Mc + " with " + (spec.Loader.Length > 0 ? spec.Loader : "vanilla") + "\n"
            + "[10:00:05] [Render thread/INFO]: Stopping!\n", new UTF8Encoding(false));
        if (spec.Loader == "optifine")
        {
            File.WriteAllBytes(Path.Combine(game, "shaderpacks", "BSL_v8.2.09.zip"), Zip(("shaders/composite.fsh", "// fake shader\n")));
            File.WriteAllBytes(Path.Combine(game, "resourcepacks", "Faithful 32x.zip"), Zip(("pack.mcmeta", "{\"pack\":{\"pack_format\":3,\"description\":\"Faithful\"}}")));
        }
    }

    private static PyDict VanillaJson(string mc) => PyDict.Of(
        "id", mc, "type", "release", "time", "2024-01-01T00:00:00+00:00", "releaseTime", "2024-01-01T00:00:00+00:00",
        "mainClass", "net.minecraft.client.main.Main", "minimumLauncherVersion", 21L,
        "assetIndex", PyDict.Of("id", "17", "sha1", new string('0', 40), "size", 1L, "totalSize", 1L, "url", "https://piston-meta.mojang.com/v1/packages/0000/17.json"),
        "assets", "17",
        "javaVersion", PyDict.Of("component", "java-runtime-gamma", "majorVersion", mc.StartsWith("1.12", StringComparison.Ordinal) ? 8L : 17L),
        "downloads", PyDict.Of("client", PyDict.Of("sha1", new string('0', 40), "size", 1L, "url", "https://piston-data.mojang.com/v1/objects/0000/client.jar")),
        "libraries", new List<object>(),
        "arguments", PyDict.Of("game", new List<object>(), "jvm", new List<object>()));

    private static PyDict LoaderJson(InstSpec spec)
    {
        string main = spec.Loader switch
        {
            "fabric" => "net.fabricmc.loader.impl.launch.knot.KnotClient",
            "neoforge" => "cpw.mods.bootstraplauncher.BootstrapLauncher",
            _ => "net.minecraft.launchwrapper.Launch",
        };
        string lib = spec.Loader switch
        {
            "fabric" => "net.fabricmc:fabric-loader:" + spec.LoaderVersion,
            "neoforge" => "net.neoforged:neoforge:" + spec.LoaderVersion,
            _ => "optifine:OptiFine:" + spec.Mc + "_" + spec.LoaderVersion,
        };
        return PyDict.Of("id", spec.MainVersion, "inheritsFrom", spec.Mc, "type", "release",
            "time", "2024-06-01T00:00:00+00:00", "releaseTime", "2024-06-01T00:00:00+00:00",
            "mainClass", main, "libraries", Py.L(PyDict.Of("name", lib)),
            "arguments", PyDict.Of("game", new List<object>(), "jvm", new List<object>()));
    }

    private static void WriteVersion(string versionsDir, string id, PyDict json, Summary sum)
    {
        string d = Path.Combine(versionsDir, id);
        Directory.CreateDirectory(d);
        WriteJson(Path.Combine(d, id + ".json"), json, sum);
        File.WriteAllBytes(Path.Combine(d, id + ".jar"), Zip(("META-INF/MANIFEST.MF", "Manifest-Version: 1.0\n")));
        sum.Versions++;
    }

    private static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (char ch in name.ToLowerInvariant())
        {
            if (ch >= 'a' && ch <= 'z' || ch >= '0' && ch <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    private static void WriteModJar(string modsDir, InstSpec spec, string name, int i, Summary sum)
    {
        string id = Slug(name).Replace('-', '_');
        string ver = (1 + i % 5).ToString(CultureInfo.InvariantCulture) + "." + (i % 7).ToString(CultureInfo.InvariantCulture) + ".0";
        string file = Slug(name) + "-" + ver + "+" + spec.Mc + ".jar";
        byte[] jar;
        if (spec.Loader == "neoforge")
        {
            string toml = "modLoader=\"javafml\"\nloaderVersion=\"[4,)\"\nlicense=\"MIT\"\n[[mods]]\nmodId=\"" + id + "\"\nversion=\"" + ver
                + "\"\ndisplayName=\"" + name + "\"\nauthors=\"Fixture\"\ndescription='''" + name + " (visual fixture)'''\n";
            jar = Zip(("META-INF/neoforge.mods.toml", toml), ("META-INF/MANIFEST.MF", "Manifest-Version: 1.0\n"));
        }
        else
        {
            string json = "{\"schemaVersion\":1,\"id\":\"" + id + "\",\"version\":\"" + ver + "\",\"name\":\"" + name.Replace("\"", "")
                + "\",\"description\":\"" + name.Replace("\"", "") + " (visual fixture)\",\"authors\":[\"Fixture\"],\"environment\":\"*\","
                + "\"depends\":{\"fabricloader\":\">=0.14\",\"minecraft\":\"~" + spec.Mc + "\"}}";
            jar = Zip(("fabric.mod.json", json), ("META-INF/MANIFEST.MF", "Manifest-Version: 1.0\n"));
        }
        // 每 9 个禁用一个(列表里要能看到「已禁用」态)
        if (i % 9 == 8) file += ".disabled";
        File.WriteAllBytes(Path.Combine(modsDir, file), jar);
        sum.Mods++;
    }

    private static void WriteSave(string dir, string levelName, string mc, DateTimeOffset lastPlayed, Summary sum)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "level.dat"), LevelDat(levelName, mc, lastPlayed.ToUnixTimeMilliseconds()));
        File.WriteAllBytes(Path.Combine(dir, "icon.png"), Png(64, 64, levelName.Length));
        Directory.CreateDirectory(Path.Combine(dir, "region"));
        File.WriteAllBytes(Path.Combine(dir, "region", "r.0.0.mca"), new byte[8192]);
        Directory.SetLastWriteTime(dir, lastPlayed.LocalDateTime);
        sum.Saves++;
    }

    // ———————— 游戏时长 ————————

    /// <summary>
    /// 本周(周一起)每天的小时数 + 上周总量。.minecraft 承担效果图里的那组数(一 1.1 / 二 1.6 / 三 0 / 四 1.4 / 五 2.1,
    /// 合计 6.2 小时;上周 4.8 小时 ⇒「比上周多 1.4 小时」),其它实例各给一点。
    /// </summary>
    private static PyDict PlaytimeFor(InstSpec spec, DateTimeOffset now, out int count)
    {
        double[] week = spec.Name switch
        {
            ".minecraft" => new[] { 1.1, 1.6, 0, 1.4, 2.1, 0, 0 },
            "生存 1.21.4" => new[] { 0, 0.5, 0.8, 0, 0, 0, 0 },
            "NeoForge 整合包" => new[] { 0, 0, 0, 0.6, 0, 0, 0 },
            _ => new double[7],
        };
        double lastWeek = spec.Name == ".minecraft" ? 4.8 : 1.0;
        DateTime today = now.LocalDateTime.Date;
        int dow = ((int)today.DayOfWeek + 6) % 7;   // 周一 = 0
        DateTime monday = today.AddDays(-dow);
        var sessions = new List<object>();
        long total = 0;
        void Add(DateTime day, double hours)
        {
            if (hours <= 0) return;
            long dur = (long)Math.Round(hours * 3600);
            DateTime start = day.AddHours(19);
            if (start > now.LocalDateTime) start = now.LocalDateTime.AddSeconds(-dur - 60);
            long ts = new DateTimeOffset(start).ToUnixTimeSeconds();
            sessions.Add(PyDict.Of("start", ts, "duration", dur, "version", spec.MainVersion));
            total += dur;
        }
        Add(monday.AddDays(-5), lastWeek * 0.6);
        Add(monday.AddDays(-3), lastWeek * 0.4);
        for (int d = 0; d < 7 && d <= dow; d++) Add(monday.AddDays(d), week[d]);
        sessions.Sort((a, b) => Py.Int(((PyDict)a)["start"]).CompareTo(Py.Int(((PyDict)b)["start"])));
        count = sessions.Count;
        return PyDict.Of("total", total, "versions", PyDict.Of(spec.MainVersion, total), "sessions", sessions);
    }

    // ———————— 小工具:zip / PNG / NBT ————————

    private static void WriteJson(string path, object data, Summary sum)
    {
        Utils.WriteJson(path, data);
        sum.Files.Add(path);
    }

    internal static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Fastest);
                e.LastWriteTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using Stream s = e.Open();
                byte[] b = new UTF8Encoding(false).GetBytes(text);
                s.Write(b, 0, b.Length);
            }
        }
        return ms.ToArray();
    }

    /// <summary>一张小渐变 PNG(RGB,无滤波)。</summary>
    internal static byte[] Png(int w, int h, int seed)
    {
        var raw = new MemoryStream();
        for (int y = 0; y < h; y++)
        {
            raw.WriteByte(0);
            for (int x = 0; x < w; x++)
            {
                raw.WriteByte((byte)(60 + (x * 3 + seed * 40) % 150));
                raw.WriteByte((byte)(110 + (y * 2 + seed * 25) % 120));
                raw.WriteByte((byte)(90 + seed * 30 % 120));
            }
        }
        byte[] idat;
        using (var z = new MemoryStream())
        {
            using (var zs = new ZLibStream(z, CompressionLevel.Fastest, leaveOpen: true)) raw.WriteTo(zs);
            idat = z.ToArray();
        }
        var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BE32(ihdr, 0, w);
        BE32(ihdr, 4, h);
        ihdr[8] = 8;
        ihdr[9] = 2;
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", idat);
        Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void BE32(byte[] b, int o, int v)
    {
        b[o] = (byte)(v >> 24);
        b[o + 1] = (byte)(v >> 16);
        b[o + 2] = (byte)(v >> 8);
        b[o + 3] = (byte)v;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        BE32(len, 0, data.Length);
        s.Write(len);
        byte[] t = Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        uint crc = Crc32(t, 0xFFFFFFFFu);
        crc = Crc32(data, crc) ^ 0xFFFFFFFFu;
        var c = new byte[4];
        BE32(c, 0, unchecked((int)crc));
        s.Write(c);
    }

    private static uint[]? _crcTable;

    private static uint Crc32(byte[] data, uint crc)
    {
        uint[] table = _crcTable ??= Enumerable.Range(0, 256).Select(n =>
        {
            uint c = (uint)n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            return c;
        }).ToArray();
        foreach (byte b in data) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    /// <summary>最小的 level.dat:gzip 大端 NBT,含 Data.LevelName / LastPlayed / GameType / DataVersion / Version.Name。</summary>
    internal static byte[] LevelDat(string levelName, string mc, long lastPlayedMs)
    {
        var nbt = new MemoryStream();
        var w = new BinaryWriterBE(nbt);
        w.Tag(10, "");
        w.Tag(10, "Data");
        w.Tag(8, "LevelName"); w.Str(levelName);
        w.Tag(4, "LastPlayed"); w.I64(lastPlayedMs);
        w.Tag(3, "GameType"); w.I32(0);
        w.Tag(3, "DataVersion"); w.I32(mc.StartsWith("1.12", StringComparison.Ordinal) ? 1343 : 3700);
        w.Tag(1, "hardcore"); nbt.WriteByte(0);
        w.Tag(4, "RandomSeed"); w.I64(20261121L);
        w.Tag(10, "Version");
        w.Tag(8, "Name"); w.Str(mc);
        w.Tag(3, "Id"); w.I32(3700);
        nbt.WriteByte(0);   // end Version
        nbt.WriteByte(0);   // end Data
        nbt.WriteByte(0);   // end root
        using var outMs = new MemoryStream();
        using (var gz = new GZipStream(outMs, CompressionLevel.Fastest, leaveOpen: true)) nbt.WriteTo(gz);
        return outMs.ToArray();
    }

    private sealed class BinaryWriterBE
    {
        private readonly Stream _s;

        public BinaryWriterBE(Stream s) => _s = s;

        public void Tag(byte type, string name)
        {
            _s.WriteByte(type);
            Str(name);
        }

        public void Str(string v)
        {
            byte[] b = new UTF8Encoding(false).GetBytes(v);
            _s.WriteByte((byte)(b.Length >> 8));
            _s.WriteByte((byte)b.Length);
            _s.Write(b);
        }

        public void I32(int v)
        {
            for (int i = 3; i >= 0; i--) _s.WriteByte((byte)(v >> (i * 8)));
        }

        public void I64(long v)
        {
            for (int i = 7; i >= 0; i--) _s.WriteByte((byte)(v >> (i * 8)));
        }
    }
}
