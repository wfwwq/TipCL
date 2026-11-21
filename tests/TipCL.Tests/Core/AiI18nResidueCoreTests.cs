// AI3-i18n:上一轮剩下的 i18n 残留(Core 侧)。
//   1. 老 Preflight.CheckLaunch 的条目(标题 / 说明)与 Conflict.ScanConflicts 的 message 接 Tr:
//      en 下不含 CJK、占位都填上;切回中文后与改动前逐字相同(期望串照改动前的拼接方式手写)。
//      这些文字也是 preflight_check / scan_mod_conflicts 的工具结果 —— 与界面共用,跟界面语言走(D-82)。
//   2. AI 步骤行前缀(准备: / 完成: …)与步骤组标题走词表;AiUiText.StripState 认译文前缀。
// 切语言是进程全局的,本类放进不并行的集合里跑,免得别的用例在 en 窗口里读到英文。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

[Collection("TipCL 真机交互回归")]
public class AiI18nResidueCoreTests
{
    private static readonly AsyncLocal<bool> Mine = new();

    /// <summary>在 <paramref name="dir"/> 里造一个只含 fabric.mod.json 的 jar(内容任意)。</summary>
    internal static void FabricJarAt(string dir, string file, string modJson)
    {
        Directory.CreateDirectory(dir);
        using var fs = new FileStream(Path.Combine(dir, file), FileMode.Create);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        using var w = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open());
        w.Write(modJson);
    }

    internal static string ModJson(string id, string? depends = null, string? breaks = null)
        => "{\"schemaVersion\":1,\"id\":\"" + id + "\",\"version\":\"1.0\",\"name\":\"" + id + "\""
           + (depends != null ? ",\"depends\":" + depends : "")
           + (breaks != null ? ",\"breaks\":" + breaks : "") + "}";

    /// <summary>一套会让老预检几乎每类条目都报出来的模组(重复 / 缺 Fabric API / 缺依赖 / 互斥 / 解压成文件夹)。</summary>
    internal static void BadMods(string modsDir)
    {
        FabricJarAt(modsDir, "dup-a.jar", ModJson("dup"));
        FabricJarAt(modsDir, "dup-b.jar", ModJson("dup"));
        FabricJarAt(modsDir, "needsapi.jar", ModJson("needsapi", depends: "{\"fabric-api\":\"*\"}"));
        FabricJarAt(modsDir, "needsfoo.jar", ModJson("needsfoo", depends: "{\"foo\":\"*\"}"));
        FabricJarAt(modsDir, "breaker.jar", ModJson("breaker", breaks: "{\"dup\":\"*\"}"));
        Directory.CreateDirectory(Path.Combine(modsDir, "unzipped"));
    }

    private sealed class Scene : IDisposable
    {
        public readonly NfTempInstance T = new("i18n3");
        public readonly string Idx5 = "i18n3a-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        public readonly string Idx6 = "i18n3b-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        public readonly string Idx7 = "i18n3c-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        public readonly string NoJava;
        private readonly List<string> _files = new();

        public Scene()
        {
            // 1.20.1:缺 1 个库、资源索引缺失、没有客户端 jar、名字像原版但 mods 里有 jar
            T.Write("versions/1.20.1/1.20.1.json",
                "{\"id\":\"1.20.1\",\"type\":\"release\",\"mainClass\":\"x\",\"assetIndex\":{\"id\":\"" + Idx5 + "\"},"
                + "\"libraries\":[{\"name\":\"com.example:lib1:1.0\",\"downloads\":{\"artifact\":{\"path\":\"com/example/lib1/1.0/lib1-1.0.jar\",\"sha1\":\"abc\",\"size\":1}}}]}");
            // 1.20.2:资源索引是空的;1.20.3:资源索引里的 10 个对象全不在
            T.Write("versions/1.20.2/1.20.2.json", "{\"id\":\"1.20.2\",\"type\":\"release\",\"mainClass\":\"x\",\"assetIndex\":{\"id\":\"" + Idx6 + "\"}}");
            T.Write("versions/1.20.3/1.20.3.json", "{\"id\":\"1.20.3\",\"type\":\"release\",\"mainClass\":\"x\",\"assetIndex\":{\"id\":\"" + Idx7 + "\"}}");
            WriteIndex(Idx6, "{\"objects\":{}}");
            string objects = string.Join(",", Enumerable.Range(0, 10).Select(i => "\"o" + i + "\":{\"hash\":\"ff" + i.ToString("x2") + "0000000000000000000000000000000000\",\"size\":1}"));
            WriteIndex(Idx7, "{\"objects\":{" + objects + "}}");
            BadMods(Utils.AbsPath(VersionSettings.ModsDir(T.Inst, "1.20.1")));
            NoJava = T.PathOf("nojava", "java.exe");
        }

        private void WriteIndex(string id, string json)
        {
            string p = Path.Combine(Utils.AbsPath(T.Inst.AssetsDir()), "indexes", id + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, json);
            _files.Add(p);
        }

        public void Dispose()
        {
            foreach (string f in _files)
            {
                try { File.Delete(f); } catch (Exception) { }
            }
            T.Dispose();
        }
    }

    /// <summary>挂上磁盘 / 物理内存钩子(只对本异步流生效,别的用例照常走真实探测)跑一遍老预检,按 code 收条目。</summary>
    private static Dictionary<string, PyDict> Run(Instance inst, string version, long freeMb, long availMb, string java = "")
    {
        Func<string, long?>? savedFree = Preflight.FreeBytesHook;
        Func<long?>? savedAvail = Preflight.AvailPhysMbHook;
        Mine.Value = true;
        Preflight.FreeBytesHook = p => Mine.Value ? freeMb * 1048576L : savedFree?.Invoke(p);
        Preflight.AvailPhysMbHook = () => Mine.Value ? availMb : savedAvail?.Invoke();
        try
        {
            PyDict r = Preflight.CheckLaunch(inst, version, 4096, java);
            var map = new Dictionary<string, PyDict>();
            foreach (object? raw in Py.ListOr(r.Get("items")))
            {
                PyDict it = Py.D(raw);
                map.TryAdd(Py.S(it.Get("code")), it);
            }
            return map;
        }
        finally
        {
            Preflight.FreeBytesHook = savedFree;
            Preflight.AvailPhysMbHook = savedAvail;
            Mine.Value = false;
        }
    }

    private static Dictionary<string, PyDict> All(Scene s)
    {
        var all = new Dictionary<string, PyDict>();
        void Merge(Dictionary<string, PyDict> m)
        {
            foreach (var kv in m) all.TryAdd(kv.Key, kv.Value);
        }
        Merge(Run(s.T.Inst, "1.20.1", 100, 1024, s.NoJava));
        Merge(Run(s.T.Inst, "1.20.2", 1000, 1L << 20));
        Merge(Run(s.T.Inst, "1.20.3", 1000, 1L << 20));
        Merge(Run(s.T.Inst, "", 1L << 20, 1L << 20));
        Merge(Run(s.T.Inst, "nope", 1L << 20, 1L << 20));
        return all;
    }

    private static readonly string[] Codes =
    {
        "no_version", "no_version_json", "no_client_jar", "disk_low", "disk_warn", "mod_unzipped", "vanilla_mods",
        "java_missing", "memory_high", "libs_missing", "assets_index_missing", "assets_index_empty", "assets_missing",
        "mod_duplicate", "mod_breaks", "mod_missing_fabric_api", "mod_missing_dep",
    };

    [Fact]
    public void OldPreflightItems_InEnglish_HaveNoCjk_AndChineseIsVerbatim()
    {
        using var s = new Scene();
        Dictionary<string, PyDict> en = new();
        I18nUxCjk.InLang("en", () => en = All(s));
        foreach (string code in Codes)
        {
            Assert.True(en.ContainsKey(code), "场景没触发 " + code + ";实际:" + string.Join(",", en.Keys));
        }
        // 说明里的路径 / 文件名是数据(都是 ASCII 的临时目录),只查我们写的文字
        var texts = en.Values.SelectMany(i => new[] { Py.S(i.Get("title")), Py.S(i.Get("detail")) }).ToList();
        I18nUxCjk.AssertNone(texts, "老预检条目");
        Assert.DoesNotContain(texts, x => x.Contains('{') || x.Contains('}')); // 占位都填上了
        Assert.Equal("Not enough disk space", Py.S(en["disk_low"].Get("title")));
        Assert.Equal("Only about 100 MB left on the instance's drive; at least 512 MB is needed", Py.S(en["disk_low"].Get("detail")));
        Assert.Contains("dup ×2 (dup-a.jar, dup-b.jar)", Py.S(en["mod_duplicate"].Get("detail")));
        Assert.Equal("needsapi requires Fabric API", Py.S(en["mod_missing_fabric_api"].Get("detail")));
        Assert.StartsWith("needsfoo is missing foo", Py.S(en["mod_missing_dep"].Get("detail")));

        Dictionary<string, PyDict> zh = new();
        I18nUxCjk.InLang("zh_CN", () => zh = All(s));
        string T(string code) => Py.S(zh[code].Get("title"));
        string D(string code) => Py.S(zh[code].Get("detail"));
        Assert.Equal("未选择版本", T("no_version"));
        Assert.Equal("请先到「下载 → 原版游戏」安装版本", D("no_version"));
        Assert.Equal("版本未安装", T("no_version_json"));
        Assert.Equal("找不到 nope 的版本 JSON", D("no_version_json"));
        Assert.Equal("客户端 jar 未直接找到", T("no_client_jar"));
        Assert.Equal(PPath.Join(s.T.Inst.VersionsDir(), "1.20.1", "1.20.1.jar") + "（若为继承版本，启动时会解析父版本）", D("no_client_jar"));
        Assert.Equal("磁盘空间不足", T("disk_low"));
        Assert.Equal("实例所在盘仅剩约 100 MB，至少需要 512 MB", D("disk_low"));
        Assert.Equal("磁盘空间偏低", T("disk_warn"));
        Assert.Equal("实例所在盘剩余约 1000 MB，建议清理后再装大整合包", D("disk_warn"));
        Assert.Equal("Mods 被解压成了文件夹", T("mod_unzipped"));
        Assert.Equal("直接放整个 .jar/.zip 即可。请删掉这些文件夹：\n - unzipped", D("mod_unzipped"));
        Assert.Equal("原版版本不会加载模组", T("vanilla_mods"));
        Assert.Equal("mods 里有 5 个 jar，但当前版本名像原版。请安装 Fabric/Forge 等加载器。", D("vanilla_mods"));
        Assert.Equal("指定的 Java 不存在", T("java_missing"));
        Assert.Equal(s.NoJava, D("java_missing"));
        Assert.Equal("分配内存接近可用物理内存", T("memory_high"));
        Assert.Equal("游戏 4096 MB，系统当前可用约 1024 MB，可能触发交换卡顿", D("memory_high"));
        Assert.Equal("依赖库缺失", T("libs_missing"));
        Assert.Equal("缺少以下库文件，请到版本页点「修复」：\n - lib1-1.0.jar", D("libs_missing"));
        Assert.Equal("资源索引缺失", T("assets_index_missing"));
        Assert.Equal("找不到 assets/indexes/" + s.Idx5 + ".json，请修复该版本。", D("assets_index_missing"));
        Assert.Equal("资源索引为空", T("assets_index_empty"));
        Assert.Equal(s.Idx6 + ".json 没有 objects，游戏可能缺材质/音效。", D("assets_index_empty"));
        Assert.Equal("游戏资源大量缺失", T("assets_missing"));
        Assert.Equal("抽样 10 个资源缺 10 个（约估全量缺 10/10）。请修复该版本。", D("assets_missing"));
        Assert.Equal("重复安装同一模组", T("mod_duplicate"));
        Assert.Equal("同 id 装了多份会导致启动失败：\n - dup ×2（dup-a.jar, dup-b.jar）", D("mod_duplicate"));
        Assert.Equal("模组互相冲突", T("mod_breaks"));
        Assert.Equal("元数据声明不兼容：\n - breaker ↔ dup", D("mod_breaks"));
        Assert.Equal("缺少 Fabric API", T("mod_missing_fabric_api"));
        Assert.Equal("needsapi 需要 Fabric API", D("mod_missing_fabric_api"));
        Assert.Equal("可能缺少模组依赖", T("mod_missing_dep"));
        Assert.Equal("needsfoo 缺少 foo\n（若实际由其它 jar 提供可忽略）", D("mod_missing_dep"));
    }

    [Fact]
    public void OldPreflight_ReadyItem_FollowsTheLanguage()
    {
        using var t = new NfTempInstance("i18n3ok");
        t.Write("versions/1.20.4/1.20.4.json", "{\"id\":\"1.20.4\",\"type\":\"release\",\"mainClass\":\"x\"}");
        t.Write("versions/1.20.4/1.20.4.jar", "jar");
        PyDict? en = null;
        I18nUxCjk.InLang("en", () => en = Run(t.Inst, "1.20.4", 1L << 20, 1L << 20)["ready"]);
        Assert.Equal("Pre-launch check passed", Py.S(en!.Get("title")));
        Assert.Equal("No blocking problems found", Py.S(en.Get("detail")));
        PyDict? zh = null;
        I18nUxCjk.InLang("zh_CN", () => zh = Run(t.Inst, "1.20.4", 1L << 20, 1L << 20)["ready"]);
        Assert.Equal("预检通过", Py.S(zh!.Get("title")));
        Assert.Equal("未发现阻塞问题", Py.S(zh.Get("detail")));
        Assert.Equal("ok", Py.S(zh.Get("level"))); // level / code 是数据,不随语言变
    }

    private static NfTempInstance ConflictInstance()
    {
        var t = new NfTempInstance("i18n3cf");
        t.Version("fabric-loader-0.15.11-1.20.5", javaMajor: 21);
        BadMods(t.PathOf("mods"));
        // 启动预检看的是「该版本实际生效的 mods 目录」(隔离时不是实例的 mods),两处都放一份
        string effective = Utils.AbsPath(VersionSettings.ModsDir(t.Inst, "fabric-loader-0.15.11-1.20.5"));
        if (!string.Equals(Path.GetFullPath(effective), Path.GetFullPath(t.PathOf("mods")), StringComparison.OrdinalIgnoreCase))
        {
            BadMods(effective);
        }
        string forge = t.PathOf("mods", "forgey.jar");
        using (var fs = new FileStream(forge, FileMode.Create))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("META-INF/mods.toml").Open()))
        {
            w.Write("modLoader=\"javafml\"\n[[mods]]\nmodId=\"forgey\"\n");
        }
        return t;
    }

    [Fact]
    public void ConflictMessages_InEnglish_HaveNoCjk_AndChineseIsVerbatim()
    {
        using NfTempInstance t = ConflictInstance();
        List<PyDict> Issues() => Py.ListOr(Conflict.ScanConflicts(t.Inst).Get("issues")).Select(Py.D).ToList();
        List<PyDict> en = new();
        I18nUxCjk.InLang("en", () => en = Issues());
        Assert.Contains(en, i => Py.S(i.Get("type")) == "loader_mismatch");
        I18nUxCjk.AssertNone(en.Select(i => Py.S(i.Get("message"))), "scan_mod_conflicts 的 message");
        Assert.Contains(en, i => Py.S(i.Get("message")) == "Mod dup is installed 2 times");
        Assert.Contains(en, i => Py.S(i.Get("message")) == "needsfoo is missing dependency foo");

        List<string> zh = new();
        I18nUxCjk.InLang("zh_CN", () => zh = Issues().Select(i => Py.S(i.Get("message"))).ToList());
        Assert.Contains("模组 dup 装了 2 份", zh);
        Assert.Contains("needsapi 需要 Fabric API", zh);
        Assert.Contains("needsfoo 缺少依赖 foo", zh);
        Assert.Contains("breaker 与 dup 不兼容", zh);
        Assert.Contains("forgey.jar 是 forge 模组，当前实例是 fabric", zh);
        // type / need / file 是数据:两种语言下一样
        Assert.Equal(en.Select(i => Py.S(i.Get("type")) + "|" + Py.S(i.Get("need")) + "|" + Py.S(i.Get("file"))),
            Issues().Select(i => Py.S(i.Get("type")) + "|" + Py.S(i.Get("need")) + "|" + Py.S(i.Get("file"))));
    }

    [Fact]
    public void AiPreflightReport_InEnglish_HasNoCjkAnywhere()
    {
        // 启动页提示条 / 交给 AI 的开场 / preflight_check 的工具结果用的是同一份报告:整份(含来自老代码的条目)都不许有中文
        using NfTempInstance t = ConflictInstance();
        AiPreflight.JavaListHook = () => new List<object> { PyDict.Of("major", 21L, "exe", "java21") };
        try
        {
            I18nUxCjk.InLang("en", () =>
            {
                PyDict r = AiPreflight.Check(t.Inst, "fabric-loader-0.15.11-1.20.5", 4096, null, PyDict.Of("ram_mb", 65536L));
                var items = Py.ListOr(r.Get("items")).Select(Py.D).ToList();
                Assert.Contains(items, i => Py.S(i.Get("code")) == "dep_missing");
                Assert.Contains(items, i => Py.S(i.Get("code")) == "mod_unzipped");
                var texts = items.SelectMany(i => new[] { Py.S(i.Get("title")), Py.S(i.Get("detail")), Py.S(i.Get("fix")) }).ToList();
                texts.Add(Py.S(r.Get("summary")));
                I18nUxCjk.AssertNone(texts, "预检报告(含老 Preflight / Conflict 条目)");
                I18nUxCjk.AssertNone(AiPreflight.BuildFixPrompt(r).Split('\n'), "交给 AI 的开场");
            });
        }
        finally
        {
            AiPreflight.JavaListHook = null;
        }
    }

    [Fact]
    public void StepPrefixesAndSummary_FollowTheLanguage_AndStripStateKnowsBoth()
    {
        var args = PyDict.Of("name", "sodium", "instance", "demo");
        I18nUxCjk.InLang("en", () =>
        {
            string label = Tools.Label("install_mod", args);
            foreach (string zhPrefix in new[] { "准备:", "执行中:", "完成:", "已跳过:", "失败:" })
            {
                string prefix = I18nCore.Tr(zhPrefix);
                Assert.False(I18nUxCjk.Has(prefix), zhPrefix + " → " + prefix);
                Assert.Equal(label, AiUiText.StripState(prefix + label));
                Assert.Equal(label, AiUiText.SplitLabel(prefix + label).Label);
            }
            Assert.Equal("Preparing:", I18nCore.Tr("准备:"));
            Assert.Equal("Done:", I18nCore.Tr("完成:"));
            string done = AiUiText.StepSummary(3, 1.5, false, "");
            Assert.False(I18nUxCjk.Has(done), done);
            Assert.Contains("3 steps", done);
            Assert.False(I18nUxCjk.Has(AiUiText.StepSummary(0, 0, true, label)));
            Assert.False(I18nUxCjk.Has(AiUiText.StepSummary(0, 0, true, "")));
        });
        I18nUxCjk.InLang("zh_CN", () =>
        {
            Assert.Equal("准备:", I18nCore.Tr("准备:"));
            Assert.Equal("完成:", I18nCore.Tr("完成:"));
            Assert.Equal("装模组 钠", AiUiText.StripState("执行中: 装模组 钠"));
            Assert.Equal("装模组 钠", AiUiText.StripState("完成\uFF1A装模组 钠"));
            Assert.Equal("执行了 3 步 · 用时 " + 1.5.ToString("0.0") + "s", AiUiText.StepSummary(3, 1.5, false, ""));
            Assert.Equal("执行了 2 步", AiUiText.StepSummary(2, 0, false, ""));
            Assert.Equal("正在装模组", AiUiText.StepSummary(1, 0, true, "装模组"));
            Assert.Equal("正在执行", AiUiText.StepSummary(0, 0, true, ""));
        });
        // 英文前缀只在英文界面下才剥(中文界面下 Done: 不是状态前缀,原样留着)
        I18nUxCjk.InLang("zh_CN", () => Assert.Equal("Done: x", AiUiText.StripState("Done: x")));
    }
}
