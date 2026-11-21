// AI2-i18n-ux 包的 Core 测试:
//   1. 新工具标签 / 确认卡预览 / 启动前预检条目在 en 下不含 CJK(切语言真能改变它们);
//   2. apply_instance_upgrade 的阶段进度(核对模组 → 复制实例 → 安装版本 → 替换模组 → 预检)按顺序报出,最后真跑预检;
//   3. 实例级内存 / GC 的读写入口(InstanceJvmSettings):分别设置、分别清除、非法值拒绝、不凭空造实例。
// 读写都落在 TestIsolation 的临时 TIPCL_HOME(NfTempInstance 唯一名、用完删);不联网(升级走假源)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

/// <summary>en 下渲染文字不许出现 CJK(与 tools/i18n-audit.py 的 CJK 判据同区间)。</summary>
internal static class I18nUxCjk
{
    public static bool Has(string? s)
    {
        foreach (char ch in s ?? "")
        {
            int o = ch;
            if ((o >= 0x4E00 && o <= 0x9FFF) || (o >= 0x3400 && o <= 0x4DBF) || (o >= 0x3000 && o <= 0x303F) || (o >= 0xFF00 && o <= 0xFFEF))
            {
                return true;
            }
        }
        return false;
    }

    public static void AssertNone(IEnumerable<string> texts, string what)
    {
        var bad = texts.Where(Has).ToList();
        Assert.True(bad.Count == 0, what + " 在 en 下仍有中文:" + string.Join(" | ", bad));
    }

    public static void InLang(string lang, Action body)
    {
        string prev = I18nCore.CurrentLang;
        try
        {
            I18nCore.CurrentLang = lang;
            body();
        }
        finally
        {
            I18nCore.CurrentLang = prev;
        }
    }

    public static async Task InLangAsync(string lang, Func<Task> body)
    {
        string prev = I18nCore.CurrentLang;
        try
        {
            I18nCore.CurrentLang = lang;
            await body();
        }
        finally
        {
            I18nCore.CurrentLang = prev;
        }
    }
}

[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class AiI18nUxLabelTests
{
    /// <summary>覆盖各工具模板里出现过的全部参数名;值全是 ASCII,这样输出里出现的中文只可能来自没接线的文案。</summary>
    private static PyDict AsciiArgs() => PyDict.Of(
        "instance", "demo", "name", "Alex", "path", "options.txt", "version", "1.20.1", "loader", "fabric",
        "query", "sodium", "filename", "sodium.jar", "major", "17", "kind", "logs", "target_version", "1.21.1",
        "address", "mc.example.org", "index", "0", "dest_dir", "/tmp/out", "username", "Steve", "action", "host",
        "backup_name", "World1-20261121.zip", "apply", true, "text", "x", "task", "t");

    [Fact]
    public void EveryToolLabel_InEnglish_HasNoCjk_WithAndWithoutArgs()
    {
        I18nUxCjk.InLang("en", () =>
        {
            List<string> names = Tools.Names();
            Assert.True(names.Count >= 65, "工具表不全:" + names.Count);
            var texts = new List<string>();
            foreach (string n in names)
            {
                texts.Add(n + " => " + Tools.Label(n));
                texts.Add(n + " => " + Tools.Label(n, AsciiArgs()));
                texts.Add(n + " => " + Tools.Label(n, new PyDict()));
            }
            I18nUxCjk.AssertNone(texts, "工具标签");
            // 抽查几条确实翻成了英文(不是原样回落成工具名)
            Assert.Equal("Upgrade instance demo → 1.21.1", Tools.Label("apply_instance_upgrade", AsciiArgs()));
            Assert.Equal("Pre-launch check", Tools.Label("preflight_check"));
            Assert.Equal("Switch account Alex", Tools.Label("switch_account", AsciiArgs()));
            Assert.Equal("Search mods \"sodium\"", Tools.Label("search_mods", AsciiArgs()));
        });
    }

    [Fact]
    public void ToolLabels_InChinese_AreUnchanged()
    {
        I18nUxCjk.InLang("zh_CN", () =>
        {
            Assert.Equal("读最新日志", Tools.Label("get_latest_log"));
            Assert.Equal("列文件 mods", Tools.Label("fs_list", PyDict.Of("path", "mods")));
            Assert.Equal("删除服务器 #2", Tools.Label("remove_server", PyDict.Of("index", 2L)));
            Assert.Equal("导出诊断报告", Tools.Label("export_diagnostic_report", PyDict.Of("dest_dir", "")));
            Assert.Equal("搜模组「钠」", Tools.Label("search_mods", PyDict.Of("query", "钠")));
            Assert.Equal("联机:host", Tools.Label("multiplayer", PyDict.Of("action", "host")));
        });
    }

    [Fact]
    public void ExternalWriteReason_FollowsLanguage()
    {
        I18nUxCjk.InLang("en", () => Assert.False(I18nUxCjk.Has(ToolRunner.ExternalWriteReason)));
        I18nUxCjk.InLang("zh_CN", () => Assert.Equal("外部 MCP 客户端发起的写操作需要你确认", ToolRunner.ExternalWriteReason));
    }

    [Fact]
    public void NewFeaturePreviews_InEnglish_HaveNoCjk()
    {
        using var t = new NfTempInstance("i18nux");
        t.Version("fabric-loader-0.15.11-1.20.1");
        I18nUxCjk.InLang("en", () =>
        {
            var texts = new List<string>();
            void Take(PyDict? row)
            {
                Assert.NotNull(row);
                texts.Add(Py.S(row!.Get("head")));
                texts.AddRange(Py.ListOr(row.Get("lines")).Select(x => Py.S(x)));
            }
            Take(NewFeatureEffects.Preview("add_server", PyDict.Of("instance", t.Name, "address", "mc.example.org"), t.Name));
            Take(NewFeatureEffects.Preview("remove_server", PyDict.Of("instance", t.Name, "index", 0L), t.Name));
            Take(NewFeatureEffects.Preview("export_diagnostic_report", PyDict.Of("instance", t.Name, "dest_dir", Path.GetTempPath()), t.Name));
            Take(NewFeatureEffects.Preview("apply_instance_upgrade", PyDict.Of("instance", t.Name, "target_version", "1.21.1"), t.Name));
            Take(NewFeatureEffects.Preview("apply_instance_upgrade", PyDict.Of("instance", t.Name, "target_version", "1.21.1", "in_place", true), t.Name));
            // 导出路径本身可能落在含中文的目录里,那是数据不是文案 —— 只查我们写的那几行
            texts = texts.Where(x => !x.Contains(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)).ToList();
            I18nUxCjk.AssertNone(texts, "确认卡预览");
            Assert.Contains(texts, x => x.StartsWith("Server to add", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void PreflightItems_InEnglish_HaveNoCjk_AndChineseStaysTheSame()
    {
        using var t = new NfTempInstance("i18nux");
        t.Version("fabric-loader-0.15.11-1.20.5", javaMajor: 21);
        t.FabricJar("needs-api.jar", "needsapi", dependsJson: "{\"fabric-api\":\"*\"}");
        AiPreflight.JavaListHook = () => new List<object> { PyDict.Of("major", 8L, "exe", "java8") };
        try
        {
            string[] ours = { "dep_missing", "memory_exceeds_physical", "java_auto_download" };
            I18nUxCjk.InLang("en", () =>
            {
                PyDict r = AiPreflight.Check(t.Inst, "fabric-loader-0.15.11-1.20.5", 16384, null, PyDict.Of("ram_mb", 8192L));
                var items = Py.ListOr(r.Get("items")).Select(Py.D).Where(i => ours.Contains(Py.S(i.Get("code")))).ToList();
                Assert.Equal(3, items.Count);
                var texts = new List<string> { Py.S(r.Get("summary")) };
                foreach (PyDict it in items)
                {
                    texts.Add(Py.S(it.Get("title")));
                    texts.Add(Py.S(it.Get("fix")));
                    if (Py.S(it.Get("code")) != "dep_missing")
                    {
                        texts.Add(Py.S(it.Get("detail"))); // dep_missing 的说明来自 Conflict.ScanConflicts(既有 Core,本包未动)
                    }
                }
                I18nUxCjk.AssertNone(texts, "预检条目");
                // 交给 AI 的开场:框架句(首三行 + 末行)也走词表
                List<string> prompt = AiPreflight.BuildFixPrompt(r).Split('\n').ToList();
                I18nUxCjk.AssertNone(prompt.Take(3).Append(prompt.Last()), "预检开场");
            });
            I18nUxCjk.InLang("zh_CN", () =>
            {
                PyDict r = AiPreflight.Check(t.Inst, "fabric-loader-0.15.11-1.20.5", 16384, null, PyDict.Of("ram_mb", 8192L));
                PyDict mem = Py.D(Py.ListOr(r.Get("items")).First(i => Py.S(Py.D(i).Get("code")) == "memory_exceeds_physical"));
                Assert.Equal("分配内存超过物理内存", Py.S(mem.Get("title")));
                Assert.Equal("游戏分配 16384 MB,本机物理内存只有约 8192 MB,JVM 会起不来或疯狂换页", Py.S(mem.Get("detail")));
                Assert.Contains("预检结果(", AiPreflight.BuildFixPrompt(r));
            });
        }
        finally
        {
            AiPreflight.JavaListHook = null;
        }
    }
}

[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class AiI18nUxUpgradeStageTests
{
    /// <summary>空模组的假源(升级只走 核对 → 复制 → 安装 → 换模组 → 预检 这条骨架)。</summary>
    private sealed class EmptySource : IUpgradeSource
    {
        public PyDict? Identify(string jarPath) => null;
        public PyDict? FindVersion(PyDict ident, string mcVersion, string loader) => null;
        public List<object> Alternatives(string name, string mcVersion, string loader, int limit) => new();
        public void Download(PyDict version, string destPath) => File.WriteAllText(destPath, "x");
    }

    [Fact]
    public void StageText_IsLocalized()
    {
        I18nUxCjk.InLang("en", () =>
        {
            for (int i = 0; i < ToolRunner.UpgradeStageCount; i++)
            {
                Assert.False(I18nUxCjk.Has(ToolRunner.UpgradeStageText(i, false)));
                Assert.False(I18nUxCjk.Has(ToolRunner.UpgradeStageText(i, true)));
            }
            Assert.Equal("Step 2/5: Copying instance", ToolRunner.UpgradeStageText(1, false));
        });
        I18nUxCjk.InLang("zh_CN", () =>
        {
            Assert.Equal("第 1/5 步:核对模组", ToolRunner.UpgradeStageText(0, false));
            Assert.Equal("第 5/5 步:预检", ToolRunner.UpgradeStageText(4, false));
            Assert.Equal("第 2/5 步:准备原地升级", ToolRunner.UpgradeStageText(1, true));
        });
    }

    private static async Task<(List<(string Tool, string Text)> Stages, PyDict Result, ToolCall Call)> RunUpgradeAsync(NfTempInstance t, Func<Instance, PyDict> preflight)
    {
        var args = PyDict.Of("instance", t.Name, "target_version", "1.21.1");
        t.Track(Instances.GetInstancePath(InstanceUpgrade.NewInstanceName(args, t.Name)));
        InstanceUpgrade.SourceFactory = () => new EmptySource();
        ToolRunner.UpgradePreflightHook = preflight;
        try
        {
            var stages = new List<(string, string)>();
            var runner = new ToolRunner(new NfFakeApi())
            {
                PermissionMode = "default",
                Instance = t.Name,
                ToolProgress = (tool, text) => { lock (stages) stages.Add((tool, text)); },
            };
            runner.Confirm = (_, _, _, _) => Task.FromResult((true, false));
            var call = new ToolCall("up-" + Guid.NewGuid().ToString("N").Substring(0, 6), "apply_instance_upgrade", args);
            string text = await runner.RunAsync(call);
            return (stages, Py.D(PyJson.Loads(text)), call);
        }
        finally
        {
            InstanceUpgrade.SourceFactory = null;
            ToolRunner.UpgradePreflightHook = null;
        }
    }

    [Fact]
    public async Task ApplyUpgrade_ReportsEveryStageInOrder_AndRunsPreflightOnTheNewInstance()
    {
        using var dirs = new AiServiceDirs();
        using var t = new NfTempInstance("i18nux-up");
        t.Version("fabric-loader-0.15.11-1.20.1");
        string? checkedInstance = null;
        var (stages, result, call) = await RunUpgradeAsync(t, inst =>
        {
            checkedInstance = inst.name;
            return PyDict.Of("ok", true, "error_count", 0L, "warn_count", 1L, "summary", "one warning", "items", new List<object>());
        });

        Assert.Equal(ToolCallStatus.COMPLETED, call.status);
        Assert.All(stages, s => Assert.Equal("apply_instance_upgrade", s.Tool));
        var expected = Enumerable.Range(0, ToolRunner.UpgradeStageCount).Select(i => ToolRunner.UpgradeStageText(i, false)).ToList();
        Assert.Equal(expected, stages.Select(s => s.Text).ToList());
        // 预检是真跑在升级出来的那个实例上,结论进了工具结果
        Assert.Equal(Py.S(result.Get("target_instance")), checkedInstance);
        PyDict pre = Py.D(result.Get("preflight"));
        Assert.Equal("one warning", Py.S(pre.Get("summary")));
        Assert.Equal(1L, Py.Int(pre.Get("warn_count")));
    }

    [Fact]
    public async Task ApplyUpgrade_PreflightFailure_DoesNotFailTheUpgrade()
    {
        using var dirs = new AiServiceDirs();
        using var t = new NfTempInstance("i18nux-up");
        t.Version("fabric-loader-0.15.11-1.20.1");
        var (_, result, call) = await RunUpgradeAsync(t, _ => throw new InvalidOperationException("boom"));
        Assert.Equal(ToolCallStatus.COMPLETED, call.status);
        PyDict pre = Py.D(result.Get("preflight"));
        Assert.False(Py.Truthy(pre.Get("ok")));
        Assert.Contains("boom", Py.S(pre.Get("error")));
        Assert.True(Directory.Exists(Instances.GetInstancePath(Py.S(result.Get("target_instance")))));
    }

    [Fact]
    public async Task ApplyUpgrade_WithoutHook_RunsTheRealPreflight()
    {
        using var dirs = new AiServiceDirs();
        using var t = new NfTempInstance("i18nux-up");
        t.Version("fabric-loader-0.15.11-1.20.1");
        // 不注入:走真的 AiPreflight.Check(新实例没装版本 ⇒ 预检会报问题,但工具照样完成)
        var (_, result, call) = await RunUpgradeAsync(t, null!);
        Assert.Equal(ToolCallStatus.COMPLETED, call.status);
        PyDict pre = Py.D(result.Get("preflight"));
        Assert.True(pre.ContainsKey("summary"), PyJson.Dumps(pre));
        Assert.True(Py.S(pre.Get("summary")).Length > 0);
    }
}

public class InstanceJvmSettingsTests
{
    /// <summary>
    /// 坏 jar 不能把文件锁住:升级收尾的预检会扫新实例的模组(冲突扫描 → Utils.OpenZip),以前打不开的 jar 会泄漏 FileStream,
    /// Windows 上接着删模组 / 检查点回滚就失败(全量里 InstanceUpgradeTests 的回滚用例就是这么红的)。
    /// </summary>
    [Fact]
    public void OpenZip_OnABrokenJar_ReleasesTheFile()
    {
        string path = Path.Combine(Path.GetTempPath(), "tipcl-broken-" + Guid.NewGuid().ToString("N") + ".jar");
        File.WriteAllText(path, "not a zip");
        Assert.ThrowsAny<Exception>(() => Utils.OpenZip(path));
        File.Delete(path); // 流没关的话 Windows 上这里抛 IOException
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Save_SetsAndClearsEachKeyIndependently()
    {
        using var t = new NfTempInstance("jvm");
        PyDict eff = InstanceJvmSettings.Save(t.Inst, 6144, "g1_tuned");
        Assert.Equal("instance", Py.S(eff.Get("memory_source")));
        Assert.Equal(6144L, Py.Int(eff.Get("memory_mb")));
        Assert.Equal("instance", Py.S(eff.Get("gc_source")));
        Assert.Equal(((long?)6144, "g1_tuned"), InstanceJvmSettings.Read(t.Inst));

        // 只清 GC:内存保留
        InstanceJvmSettings.Save(t.Inst, 6144, null);
        Assert.Equal(((long?)6144, (string?)null), InstanceJvmSettings.Read(t.Inst));
        // 只清内存
        eff = InstanceJvmSettings.Save(t.Inst, null, "zgc");
        Assert.Equal(((long?)null, "zgc"), InstanceJvmSettings.Read(t.Inst));
        Assert.NotEqual("instance", Py.S(eff.Get("memory_source")));
        string meta = File.ReadAllText(t.PathOf(".instance.json"));
        Assert.DoesNotContain("memory_mb", meta);
        // 与 tune_jvm 写同一处:JvmTuning 读得到
        Assert.Equal("zgc", JvmTuning.InstanceGc(t.Inst));
        // 其它元数据(java 偏好)不受影响
        Assert.Contains("\"java\"", meta);
    }

    [Fact]
    public void Save_RejectsBadValues_AndNeverCreatesAnInstance()
    {
        using var t = new NfTempInstance("jvm");
        Assert.Throws<ValueError>(() => InstanceJvmSettings.Save(t.Inst, 100, null));
        Assert.Throws<ValueError>(() => InstanceJvmSettings.Save(t.Inst, 999999, null));
        Assert.Throws<ValueError>(() => InstanceJvmSettings.Save(t.Inst, 4096, "cms"));
        Assert.Equal(((long?)null, (string?)null), InstanceJvmSettings.Read(t.Inst));

        string bogus = "no-such-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        Assert.Throws<InstanceError>(() => InstanceJvmSettings.Save(new Instance(bogus), 4096, "g1"));
        Assert.False(Directory.Exists(Instances.GetInstancePath(bogus)));
    }

    [Fact]
    public void ParseMemory_AcceptsBlankAndMb()
    {
        Assert.Null(InstanceJvmSettings.ParseMemory(""));
        Assert.Null(InstanceJvmSettings.ParseMemory("   "));
        Assert.Equal(4096L, InstanceJvmSettings.ParseMemory("4096"));
        Assert.Equal(3072L, InstanceJvmSettings.ParseMemory(" 3072 MB "));
        Assert.Throws<ValueError>(() => InstanceJvmSettings.ParseMemory("4G"));
    }

    [Fact]
    public void LaunchOrder_VersionThenInstanceThenGlobal()
    {
        using var t = new NfTempInstance("jvm");
        t.Version("1.20.1");
        InstanceJvmSettings.Save(t.Inst, 5120, "g1");
        PyDict eff = JvmTuning.Effective(t.Inst, "1.20.1");
        Assert.Equal("instance", Py.S(eff.Get("memory_source")));
        // 版本层只要「跟随」(gc 为空)就不挡实例层 —— 版本设置对话框新增的「跟随实例 / 全局」写的就是空串
        VersionSettings.Save(t.Inst, "1.20.1", PyDict.Of("gc", ""));
        eff = JvmTuning.Effective(t.Inst, "1.20.1");
        Assert.Equal("instance", Py.S(eff.Get("gc_source")));
        Assert.Equal("g1", Py.S(eff.Get("gc_preset")));
        VersionSettings.Save(t.Inst, "1.20.1", PyDict.Of("gc", "zgc", "memory_mb", 2048L));
        eff = JvmTuning.Effective(t.Inst, "1.20.1");
        Assert.Equal("version", Py.S(eff.Get("memory_source")));
        Assert.Equal("version", Py.S(eff.Get("gc_source")));
    }
}
