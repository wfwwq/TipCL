// AI2-moddev-mcp:模组开发测试环境(经 MCP 给外部编程工具用)+ MCP 遗留问题的回归。
//   · jar 元数据:fabric / quilt / forge(mods.toml)/ neoforge(neoforge.mods.toml)正反例、非 zip、没有元数据;
//   · MC 版本约束与加载器兼容判定;
//   · create_test_instance(装游戏 / 依赖走注入点,不联网)、install_local_mod(加载器不匹配拒绝、同 id 替换、
//     非测试实例要 allow_non_dev + 执行器强制确认)、revert_local_mod(按检查点撤回);
//   · run_test_launch 的 成功 / 崩溃 / 超时 三条路(全用假驱动,绝不真启动游戏)+ 并发拒绝 + get_task_status;
//   · MCP:确认等待超时 ⇒ permission_denied + confirm_timeout;随机端口起服务,经 HTTP 调新工具走完一遍开发→测试循环。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

/// <summary>现造各种模组 jar。</summary>
internal static class ModDevJars
{
    public static string Zip(string path, params (string Name, string Text)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, string text) in entries)
        {
            using var w = new StreamWriter(zip.CreateEntry(name).Open());
            w.Write(text);
        }
        return path;
    }

    public static string Fabric(string path, string id, string version = "1.0.0", string mc = "~1.20.1", string moreDeps = "")
        => Zip(path, ("fabric.mod.json",
            "{\"schemaVersion\":1,\"id\":\"" + id + "\",\"version\":\"" + version + "\",\"name\":\"" + id + " mod\",\"depends\":{\"fabricloader\":\">=0.15\",\"minecraft\":\"" + mc + "\"" + moreDeps + "}}"));

    public static string Forge(string path, string id, string version = "2.0.0", string mcRange = "[1.20.1,1.21)", bool neoDep = false, string manifestVersion = "")
    {
        string toml =
            "modLoader=\"javafml\"\nloaderVersion=\"[47,)\"\nlicense=\"MIT\"\n" +
            "[[mods]]\nmodId=\"" + id + "\"\nversion=\"" + version + "\"\ndisplayName=\"" + id + " Forge\"\n" +
            "[[dependencies." + id + "]]\nmodId=\"" + (neoDep ? "neoforge" : "forge") + "\"\nmandatory=true\nversionRange=\"[47,)\"\nordering=\"NONE\"\nside=\"BOTH\"\n" +
            "[[dependencies." + id + "]]\nmodId=\"minecraft\"\nmandatory=true\nversionRange=\"" + mcRange + "\"\nordering=\"NONE\"\nside=\"BOTH\"\n" +
            "[[dependencies." + id + "]]\nmodId=\"jei\"\nmandatory=true\nversionRange=\"[15,)\"\nordering=\"NONE\"\nside=\"CLIENT\"\n" +
            "[[dependencies." + id + "]]\nmodId=\"optionalthing\"\nmandatory=false\nversionRange=\"*\"\nordering=\"NONE\"\nside=\"BOTH\"\n";
        var entries = new List<(string, string)> { ("META-INF/mods.toml", toml) };
        if (manifestVersion.Length > 0)
        {
            entries.Add(("META-INF/MANIFEST.MF", "Manifest-Version: 1.0\r\nImplementation-Version: " + manifestVersion + "\r\n"));
        }
        return Zip(path, entries.ToArray());
    }

    public static string NeoForge(string path, string id, string version = "3.0.0", string mcRange = "[1.21,1.21.2)")
        => Zip(path, ("META-INF/neoforge.mods.toml",
            "modLoader=\"javafml\"\nloaderVersion=\"[4,)\"\nlicense=\"MIT\"\n" +
            "[[mods]]\nmodId=\"" + id + "\"\nversion=\"" + version + "\"\n" +
            "[[dependencies." + id + "]]\nmodId=\"neoforge\"\ntype=\"required\"\nversionRange=\"[21,)\"\n" +
            "[[dependencies." + id + "]]\nmodId=\"minecraft\"\ntype=\"required\"\nversionRange=\"" + mcRange + "\"\n" +
            "[[dependencies." + id + "]]\nmodId=\"curios\"\ntype=\"optional\"\nversionRange=\"*\"\n"));

    public static string Quilt(string path, string id)
        => Zip(path, ("quilt.mod.json",
            "{\"schema_version\":1,\"quilt_loader\":{\"group\":\"x\",\"id\":\"" + id + "\",\"version\":\"0.9\",\"metadata\":{\"name\":\"Q\"},\"depends\":[{\"id\":\"minecraft\",\"versions\":\">=1.20\"},{\"id\":\"qsl\",\"versions\":\"*\"}]}}"));
}

/// <summary>一次用例的隔离环境:临时 jar 目录 + 检查点目录 + 用完删掉的实例 + 注入点 / 时序复原。</summary>
internal sealed class ModDevEnv : IDisposable
{
    public readonly string Dir = Path.Combine(Path.GetTempPath(), "tipcl-moddev-" + Guid.NewGuid().ToString("N").Substring(0, 10));
    private readonly AiServiceDirs _dirs = new();
    private readonly List<string> _instances = new();
    private readonly TimeSpan _poll = TestLaunch.PollInterval;
    private readonly TimeSpan _grace = TestLaunch.StopGrace;
    private readonly string _default = Py.S(Config.CONFIG.Get("default_instance", ""));

    public ModDevEnv()
    {
        Directory.CreateDirectory(Dir);
        ModDevHost.Reset();
        TestLaunch.PollInterval = TimeSpan.FromMilliseconds(20);
        TestLaunch.StopGrace = TimeSpan.FromSeconds(2);
    }

    public string Jar(string name) => Path.Combine(Dir, name);

    /// <summary>建一个实例:dev 控制 .instance.json 的 dev 标记;versionId 非空时造一个版本 JSON(DetectLoader 按名字认加载器)。</summary>
    public Instance Instance(string prefix, bool dev, string loader, string mc = "1.20.1", string versionId = "")
    {
        var meta = dev
            ? PyDict.Of("dev", true, "dev_loader", loader, "mc_version", mc)
            : PyDict.Of("mc_version", mc);
        Instance inst = Instances.CreateUniqueInstance(prefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 6), "t", meta);
        _instances.Add(inst.path);
        if (versionId.Length > 0)
        {
            string vdir = Path.Combine(inst.path, "versions", versionId);
            Directory.CreateDirectory(vdir);
            File.WriteAllText(Path.Combine(vdir, versionId + ".json"), "{\"id\":\"" + versionId + "\",\"type\":\"release\",\"mainClass\":\"x\"}");
            if (dev)
            {
                inst.SetMeta("dev_version", versionId);
            }
        }
        return inst;
    }

    public void Track(Instance inst) => _instances.Add(inst.path);

    public static string Mods(Instance inst) => ModDevOps.ModsDirOf(inst);

    public void Dispose()
    {
        ModDevHost.Reset();
        TestLaunch.PollInterval = _poll;
        TestLaunch.StopGrace = _grace;
        if (Py.S(Config.CONFIG.Get("default_instance", "")) != _default)
        {
            Config.CONFIG.Set("default_instance", _default);
        }
        foreach (string p in _instances.Append(Dir))
        {
            try
            {
                if (Directory.Exists(p)) Directory.Delete(p, true);
            }
            catch (Exception) { }
        }
        _dirs.Dispose();
    }
}

/// <summary>假启动驱动:每被探测一次吐一行日志;可设「第几次探测时退出」与退出成败;被 Stop 后立即算退出。</summary>
internal sealed class FakeLaunchDriver : ITestLaunchDriver
{
    private readonly object _gate = new();
    public readonly List<string> Script = new();
    public int ExitAfterLines = -1;
    public bool ExitSuccess = true;
    public Action? OnExit;
    public bool FailStart;
    public int Starts;
    public int Stops;
    public string StartedInstance = "";
    public string StartedVersion = "";
    private int _emitted;
    private bool _exited;
    private bool _exitFired;

    public Task<string> StartAsync(string instance, string version, CancellationToken ct)
    {
        Starts++;
        StartedInstance = instance;
        StartedVersion = version;
        if (FailStart)
        {
            throw new InvalidOperationException("没有可用账号");
        }
        return Task.FromResult("launch-" + Starts);
    }

    public TestLaunchProbe Probe(string handle)
    {
        lock (_gate)
        {
            if (!_exited && _emitted < Script.Count)
            {
                _emitted++;
            }
            if (!_exited && ExitAfterLines >= 0 && _emitted >= ExitAfterLines)
            {
                _exited = true;
            }
            if (_exited && !_exitFired)
            {
                _exitFired = true;
                OnExit?.Invoke();
            }
            return new TestLaunchProbe
            {
                Log = string.Join("\n", Script.Take(_emitted)),
                Exited = _exited,
                Success = _exited ? (Stops > 0 || ExitSuccess) : null,
                Message = _exited ? (Stops > 0 ? "stopped" : (ExitSuccess ? "exited" : "游戏崩溃")) : "running",
            };
        }
    }

    public Task StopAsync(string handle)
    {
        lock (_gate)
        {
            Stops++;
            _exited = true;
            _exitFired = true;
        }
        return Task.CompletedTask;
    }
}

internal sealed class ModDevFakeApi : ToolRunner.IBackend
{
    public readonly List<string> Methods = new();

    public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
    {
        lock (Methods) Methods.Add(method);
        object result = method == "get_instances" ? new List<object?>() : PyDict.Of("ok", true);
        return Task.FromResult(result is T t ? t : default);
    }
}

public class ModJarInfoTests
{
    [Fact]
    public void Fabric_ReadsIdVersionMcAndDependencies()
    {
        using var env = new ModDevEnv();
        ModJarInfo info = ModJarInfo.Read(ModDevJars.Fabric(env.Jar("a.jar"), "examplemod", "1.2.3", "~1.20.1", ",\"fabric-api\":\"*\",\"cloth-config\":[\">=11\",\"<13\"]"));
        Assert.True(info.ValidZip);
        Assert.True(info.IsMod, info.Error);
        Assert.Equal("examplemod", info.Id);
        Assert.Equal("1.2.3", info.Version);
        Assert.Equal(new[] { "fabric" }, info.Loaders);
        Assert.Equal("~1.20.1", info.McVersion);
        // fabricloader / minecraft 不算依赖;数组写法拼成 a || b
        Assert.Equal(new[] { "fabric-api", "cloth-config" }, info.Depends.Select(d => Py.S(d.Get("id"))));
        Assert.Equal(">=11 || <13", Py.S(info.Depends[1].Get("version")));
        Assert.Equal("examplemod", Py.S(info.ToDict().Get("id")));
    }

    [Fact]
    public void Fabric_BrokenMetadata_IsNotAMod()
    {
        using var env = new ModDevEnv();
        ModJarInfo info = ModJarInfo.Read(ModDevJars.Zip(env.Jar("b.jar"), ("fabric.mod.json", "{ this is not json")));
        Assert.True(info.ValidZip);
        Assert.False(info.IsMod);
        Assert.Contains("解析失败", info.Error);
    }

    [Fact]
    public void Forge_ModsToml_ReadsRequiredDepsRangeAndManifestVersion()
    {
        using var env = new ModDevEnv();
        ModJarInfo info = ModJarInfo.Read(ModDevJars.Forge(env.Jar("f.jar"), "forgemod", "${file.jarVersion}", manifestVersion: "4.5.6"));
        Assert.True(info.IsMod, info.Error);
        Assert.Equal("forgemod", info.Id);
        Assert.Equal("forgemod Forge", info.Name);
        Assert.Equal("4.5.6", info.Version);       // ${file.jarVersion} 回落到 MANIFEST.MF
        Assert.Equal(new[] { "forge" }, info.Loaders);
        Assert.Equal("[1.20.1,1.21)", info.McVersion);
        // forge 本身不算;mandatory=false 的可选依赖不算
        Assert.Equal(new[] { "jei" }, info.Depends.Select(d => Py.S(d.Get("id"))));
    }

    [Fact]
    public void Forge_ModsTomlWithoutModId_IsRejected()
    {
        using var env = new ModDevEnv();
        ModJarInfo info = ModJarInfo.Read(ModDevJars.Zip(env.Jar("g.jar"), ("META-INF/mods.toml", "modLoader=\"javafml\"\n[[mods]]\nversion=\"1\"\n")));
        Assert.True(info.ValidZip);
        Assert.False(info.IsMod);
        Assert.Contains("id", info.Error);
    }

    [Fact]
    public void NeoForge_BothTomlFlavours_AreRecognised()
    {
        using var env = new ModDevEnv();
        ModJarInfo neo = ModJarInfo.Read(ModDevJars.NeoForge(env.Jar("n.jar"), "neomod"));
        Assert.True(neo.IsMod, neo.Error);
        Assert.Equal(new[] { "neoforge" }, neo.Loaders);
        Assert.Equal("[1.21,1.21.2)", neo.McVersion);
        Assert.Empty(neo.Depends);                 // neoforge 本身不算,optional 的 curios 不算
        // NeoForge 1.20.2-1.20.4 的模组还用 META-INF/mods.toml,但声明依赖 neoforge
        ModJarInfo legacy = ModJarInfo.Read(ModDevJars.Forge(env.Jar("n2.jar"), "neolegacy", neoDep: true));
        Assert.Equal(new[] { "neoforge" }, legacy.Loaders);
    }

    [Fact]
    public void Quilt_AndMultiLoaderJar()
    {
        using var env = new ModDevEnv();
        ModJarInfo q = ModJarInfo.Read(ModDevJars.Quilt(env.Jar("q.jar"), "qmod"));
        Assert.True(q.IsMod, q.Error);
        Assert.Equal(new[] { "quilt" }, q.Loaders);
        Assert.Equal(">=1.20", q.McVersion);
        Assert.Equal("qsl", Py.S(Assert.Single(q.Depends).Get("id")));
        string multi = ModDevJars.Zip(env.Jar("m.jar"),
            ("fabric.mod.json", "{\"id\":\"both\",\"version\":\"1\"}"),
            ("META-INF/neoforge.mods.toml", "[[mods]]\nmodId=\"both\"\nversion=\"1\"\n"));
        ModJarInfo m = ModJarInfo.Read(multi);
        Assert.Equal(new[] { "fabric", "neoforge" }, m.Loaders);
        Assert.True(m.CompatibleWith("neoforge", "1.21", out _));
        Assert.True(m.CompatibleWith("fabric", "1.21", out _));
    }

    [Fact]
    public void NotAZip_AndZipWithoutMetadata_AreRejected()
    {
        using var env = new ModDevEnv();
        File.WriteAllText(env.Jar("text.jar"), "hello");
        ModJarInfo bad = ModJarInfo.Read(env.Jar("text.jar"));
        Assert.False(bad.ValidZip);
        Assert.Contains("不是合法的 jar", bad.Error);
        ModJarInfo lib = ModJarInfo.Read(ModDevJars.Zip(env.Jar("lib.jar"), ("com/example/A.class", "x")));
        Assert.True(lib.ValidZip);
        Assert.False(lib.IsMod);
        Assert.Contains("不是可装的模组", lib.Error);
    }

    [Theory]
    [InlineData("*", "1.20.1", true)]
    [InlineData("1.20.1", "1.20.1", true)]
    [InlineData("1.20.1", "1.20.2", false)]
    [InlineData("1.20.x", "1.20.6", true)]
    [InlineData("1.20.x", "1.21", false)]
    [InlineData(">=1.20 <1.21", "1.20.4", true)]
    [InlineData(">=1.20 <1.21", "1.21", false)]
    [InlineData("~1.20.1", "1.20.4", true)]
    [InlineData("~1.20.1", "1.21", false)]
    [InlineData("^1.20", "1.21.4", true)]
    [InlineData("1.19.2 || ~1.20", "1.20.1", true)]
    [InlineData("[1.20.1,1.21)", "1.20.1", true)]
    [InlineData("[1.20.1,1.21)", "1.21", false)]
    [InlineData("(1.20.1,1.21]", "1.20.1", false)]
    [InlineData("[1.21]", "1.21", true)]
    [InlineData("[1.21,)", "1.21.4", true)]
    [InlineData("[1.19,1.20),[1.20.1,)", "1.20.4", true)]
    [InlineData("[1.19,1.20),[1.20.1,)", "1.20", false)]
    public void McMatches_FabricAndForgeSyntax(string constraint, string mc, bool expected)
        => Assert.Equal(expected, ModJarInfo.McMatches(constraint, mc));

    [Fact]
    public void McMatches_UnknownVersionsAreNull_NotFalse()
    {
        Assert.Null(ModJarInfo.McMatches(">=1.20", "24w14a"));
        Assert.Null(ModJarInfo.McMatches("[1.20.1,1.21)", "1.21-pre1"));
        Assert.Null(ModJarInfo.McMatches(">=snapshot", "1.20.1"));
    }

    [Fact]
    public void LoaderCompatibility_Matrix()
    {
        using var env = new ModDevEnv();
        ModJarInfo fabric = ModJarInfo.Read(ModDevJars.Fabric(env.Jar("fa.jar"), "fa"));
        ModJarInfo forge = ModJarInfo.Read(ModDevJars.Forge(env.Jar("fo.jar"), "fo"));
        ModJarInfo neo = ModJarInfo.Read(ModDevJars.NeoForge(env.Jar("ne.jar"), "ne"));
        Assert.True(fabric.CompatibleWith("Fabric", "1.20.1", out _));
        Assert.True(fabric.CompatibleWith("quilt", "1.20.1", out _));      // Quilt 能加载 Fabric 模组
        Assert.False(fabric.CompatibleWith("forge", "1.20.1", out string why));
        Assert.Contains("加载器不匹配", why);
        Assert.True(forge.CompatibleWith("neoforge", "1.20.1", out _));    // NeoForge 1.20.1 = Forge 分支
        Assert.False(forge.CompatibleWith("neoforge", "1.21", out _));
        Assert.False(neo.CompatibleWith("forge", "1.21", out _));
        Assert.False(fabric.CompatibleWith("vanilla", "1.20.1", out string vanilla));
        Assert.Contains("原版", vanilla);
        Assert.False(fabric.CompatibleWith("", "1.20.1", out _));
    }
}

public class ModDevOpsTests
{
    [Fact]
    public void CreateTestInstance_WithoutInstall_MarksDevAndPrefix()
    {
        using var env = new ModDevEnv();
        PyDict r = ModDevOps.CreateTestInstance(PyDict.Of("mc_version", "1.20.1", "loader", "Fabric", "name", "mymod-" + Guid.NewGuid().ToString("N").Substring(0, 4), "install", false));
        var inst = new Instance(Py.S(r.Get("instance")));
        env.Track(inst);
        Assert.True(Py.Truthy(r.Get("ok")));
        Assert.StartsWith("[dev] mymod-", inst.name);
        Assert.Null(r.Get("task_id"));
        PyDict meta = inst.Meta();
        Assert.True(Py.Truthy(meta.Get("dev")));
        Assert.Equal("fabric", Py.S(meta.Get("dev_loader")));
        Assert.Equal("1.20.1", Py.S(meta.Get("mc_version")));
        Assert.True(ModDevOps.IsDev(inst));
        Assert.Equal("fabric", ModDevOps.InstanceLoader(inst));
    }

    [Fact]
    public void CreateTestInstance_RejectsBadArguments()
    {
        using var env = new ModDevEnv();
        Assert.Throws<ValueError>(() => ModDevOps.CreateTestInstance(PyDict.Of("loader", "fabric")));
        Assert.Throws<ValueError>(() => ModDevOps.CreateTestInstance(PyDict.Of("mc_version", "1.20.1", "loader", "liteloaderx")));
        Assert.Throws<ValueError>(() => ModDevOps.CreateTestInstance(PyDict.Of("mc_version", "1.20.1", "loader", "vanilla", "dependencies", new List<object> { "fabric-api" })));
    }

    [Fact]
    public async Task CreateTestInstance_InstallsGameAndDependencies_ThroughInjectionPoints()
    {
        using var env = new ModDevEnv();
        var installed = new List<string>();
        ModDevHost.GameInstaller = (inst, mc, loader, lv, log, ct) =>
        {
            log("fake install " + mc + " " + loader);
            string id = "fabric-loader-0.16.0-" + mc;
            Directory.CreateDirectory(Path.Combine(inst.path, "versions", id));
            File.WriteAllText(Path.Combine(inst.path, "versions", id, id + ".json"), "{\"id\":\"" + id + "\"}");
            return id;
        };
        ModDevHost.DependencyInstaller = (inst, slug, mc, loader, log, ct) =>
        {
            if (slug == "broken") throw new InvalidOperationException("Modrinth 上没有");
            lock (installed) installed.Add(slug + "@" + mc + "/" + loader);
            return slug + ".jar";
        };
        PyDict r = ModDevOps.CreateTestInstance(PyDict.Of("mc_version", "1.20.1", "loader", "fabric", "dependencies", new List<object> { "fabric-api", "broken" }));
        var instance = new Instance(Py.S(r.Get("instance")));
        env.Track(instance);
        string taskId = Py.S(r.Get("task_id"));
        Assert.StartsWith("dev-", taskId);
        PyDict status = await ModDevTasks.StatusAsync(taskId, 50, 10);
        Assert.True(Py.Truthy(status.Get("finished")), PyJson.Dumps(status));
        // 依赖有一个装不上 ⇒ 任务记 failed,但结果里如实写清哪些装好了
        Assert.Equal("failed", Py.S(status.Get("state")));
        PyDict result = Py.D(status.Get("result"));
        Assert.Equal("fabric-loader-0.16.0-1.20.1", Py.S(result.Get("version_id")));
        Assert.Contains("broken", Py.S(result.Get("message")));
        Assert.Equal(new[] { "fabric-api@1.20.1/fabric" }, installed);
        Assert.Equal("fabric-loader-0.16.0-1.20.1", Py.S(instance.Meta().Get("dev_version")));
        Assert.Contains(Py.ListOr(status.Get("log_tail")).Select(Py.S), l => l.Contains("fake install 1.20.1 fabric"));
    }

    [Fact]
    public void InstallLocalMod_RejectsNonJarRelativeMissingInvalidAndNonMod()
    {
        using var env = new ModDevEnv();
        Instance inst = env.Instance("dev", true, "fabric", versionId: "fabric-loader-0.16.0-1.20.1");
        PyDict Run(string path) => ModDevOps.InstallLocalMod(PyDict.Of("jar_path", path, "instance", inst.name), inst.name);
        File.WriteAllText(env.Jar("x.zip"), "z");
        Assert.Equal("bad_arguments", Py.S(Run(env.Jar("x.zip")).Get("error")));
        Assert.Equal("bad_arguments", Py.S(Run("build/libs/a.jar").Get("error")));
        Assert.Equal("not_found", Py.S(Run(env.Jar("nope.jar")).Get("error")));
        File.WriteAllText(env.Jar("text.jar"), "not a zip");
        Assert.Equal("invalid_jar", Py.S(Run(env.Jar("text.jar")).Get("error")));
        Assert.Equal("not_a_mod", Py.S(Run(ModDevJars.Zip(env.Jar("lib.jar"), ("a.class", "x"))).Get("error")));
        Assert.False(Directory.Exists(ModDevEnv.Mods(inst)) && Directory.EnumerateFiles(ModDevEnv.Mods(inst)).Any());
    }

    [Fact]
    public void InstallLocalMod_LoaderMismatch_IsRefusedAndNothingChanges()
    {
        using var env = new ModDevEnv();
        Instance inst = env.Instance("forge", true, "forge", versionId: "1.20.1-forge-47.2.0");
        string jar = ModDevJars.Fabric(env.Jar("fabricmod.jar"), "fabricmod");
        PyDict r = ModDevOps.InstallLocalMod(PyDict.Of("jar_path", jar, "instance", inst.name), "");
        Assert.False(Py.Truthy(r.Get("ok")));
        Assert.Equal("loader_mismatch", Py.S(r.Get("error")));
        Assert.Contains("forge", Py.S(r.Get("message")));
        Assert.Equal("fabricmod", Py.S(Py.D(r.Get("mod")).Get("id")));
        Assert.False(File.Exists(Path.Combine(ModDevEnv.Mods(inst), "fabricmod.jar")));
    }

    [Fact]
    public void InstallLocalMod_ReplacesSameId_ThenRevertRestores()
    {
        using var env = new ModDevEnv();
        Instance inst = env.Instance("dev", true, "fabric", versionId: "fabric-loader-0.16.0-1.20.1");
        string mods = ModDevEnv.Mods(inst);
        ModDevJars.Fabric(Path.Combine(mods, "mymod-1.0.0.jar"), "mymod", "1.0.0");
        ModDevJars.Fabric(Path.Combine(mods, "other.jar"), "other", "5");
        byte[] oldBytes = File.ReadAllBytes(Path.Combine(mods, "mymod-1.0.0.jar"));
        string jar = ModDevJars.Fabric(env.Jar("mymod-1.1.0.jar"), "mymod", "1.1.0", "1.19.4");

        PyDict r = ModDevOps.InstallLocalMod(PyDict.Of("jar_path", jar, "instance", inst.name), "");
        Assert.True(Py.Truthy(r.Get("ok")), PyJson.Dumps(r));
        string changeId = Py.S(r.Get("change_id"));
        Assert.Matches("^lm-[0-9]{14}-[0-9a-f]{6}$", changeId);
        Assert.True(File.Exists(Path.Combine(mods, "mymod-1.1.0.jar")));
        Assert.False(File.Exists(Path.Combine(mods, "mymod-1.0.0.jar")));
        Assert.True(File.Exists(Path.Combine(mods, "other.jar")));
        PyDict replaced = Py.D(Assert.Single(Py.ListOr(r.Get("replaced"))));
        Assert.Equal("1.0.0", Py.S(replaced.Get("version")));
        string backup = Py.S(replaced.Get("moved_to"));
        Assert.True(File.Exists(backup));
        Assert.DoesNotContain(Path.DirectorySeparatorChar + "mods" + Path.DirectorySeparatorChar, backup);   // 备份不在 mods 里,加载器不会扫到
        // jar 声明 1.19.4,实例 1.20.1:不拒,但如实提示
        Assert.Contains(Py.ListOr(r.Get("warnings")).Select(Py.S), w => w.Contains("1.19.4"));

        PyDict undo = ModDevOps.RevertLocalMod(PyDict.Of("change_id", changeId));
        Assert.True(Py.Truthy(undo.Get("ok")), PyJson.Dumps(undo));
        Assert.False(File.Exists(Path.Combine(mods, "mymod-1.1.0.jar")));
        Assert.Equal(oldBytes, File.ReadAllBytes(Path.Combine(mods, "mymod-1.0.0.jar")));
        Assert.True(File.Exists(Path.Combine(mods, "other.jar")));
        Assert.False(File.Exists(backup));
        // 撤回过的不能再撤回一次
        Assert.Equal("not_found", Py.S(ModDevOps.RevertLocalMod(PyDict.Of("change_id", changeId)).Get("error")));
        Assert.Throws<ValueError>(() => ModDevOps.RevertLocalMod(PyDict.Of("change_id", "../../etc")));
    }

    [Fact]
    public void InstallLocalMod_SameFileNameButDifferentMod_IsNotOverwritten()
    {
        using var env = new ModDevEnv();
        Instance inst = env.Instance("dev", true, "fabric", versionId: "fabric-loader-0.16.0-1.20.1");
        ModDevJars.Fabric(Path.Combine(ModDevEnv.Mods(inst), "mod.jar"), "someone-else");
        PyDict r = ModDevOps.InstallLocalMod(PyDict.Of("jar_path", ModDevJars.Fabric(env.Jar("mod.jar"), "mine"), "instance", inst.name), "");
        Assert.Equal("file_exists", Py.S(r.Get("error")));
        Assert.Equal("someone-else", ModJarInfo.Read(Path.Combine(ModDevEnv.Mods(inst), "mod.jar")).Id);
    }

    [Fact]
    public async Task InstallLocalMod_NonDevInstance_NeedsFlag_AndExecutorAlwaysAsks()
    {
        using var env = new ModDevEnv();
        Instance daily = env.Instance("daily", false, "fabric", versionId: "fabric-loader-0.16.0-1.20.1");
        Instance dev = env.Instance("dev", true, "fabric", versionId: "fabric-loader-0.16.0-1.20.1");
        string jar = ModDevJars.Fabric(env.Jar("m.jar"), "m");

        PyDict refused = ModDevOps.InstallLocalMod(PyDict.Of("jar_path", jar, "instance", daily.name), "");
        Assert.Equal("needs_confirmation", Py.S(refused.Get("error")));
        Assert.Contains("allow_non_dev", Py.S(refused.Get("message")));
        Assert.False(File.Exists(Path.Combine(ModDevEnv.Mods(daily), "m.jar")));

        var reasons = new List<string>();
        var runner = new ToolRunner(new ModDevFakeApi())
        {
            PermissionMode = "yolo",   // 全自动档 + 「本对话都允许」也挡不住非测试实例的确认
            Instance = daily.name,
            Confirm = (call, label, reason, preview) =>
            {
                reasons.Add(reason);
                return Task.FromResult((true, true));
            },
        };
        runner.AllowAlways("install_local_mod");
        var call = new ToolCall("c1", "install_local_mod", PyDict.Of("jar_path", jar, "instance", daily.name, "allow_non_dev", true));
        string text = await runner.RunAsync(call);
        Assert.Equal(ToolCallStatus.COMPLETED, call.status);
        Assert.Contains("change_id", text);
        Assert.Equal(ModDevText.NonDevReason(), Assert.Single(reasons));
        Assert.True(File.Exists(Path.Combine(ModDevEnv.Mods(daily), "m.jar")));

        // 测试实例:全自动档直接执行,不弹
        string devText = await runner.RunAsync(new ToolCall("c2", "install_local_mod", PyDict.Of("jar_path", jar, "instance", dev.name)));
        Assert.Contains("change_id", devText);
        Assert.Single(reasons);
    }

    [Fact]
    public void AffectedPathsAndPreview_DescribeTheSameChange()
    {
        using var env = new ModDevEnv();
        Instance inst = env.Instance("dev", true, "fabric", versionId: "fabric-loader-0.16.0-1.20.1");
        ModDevJars.Fabric(Path.Combine(ModDevEnv.Mods(inst), "mymod-old.jar"), "mymod", "0.9");
        string jar = ModDevJars.Fabric(env.Jar("mymod-new.jar"), "mymod", "1.0");
        var args = PyDict.Of("jar_path", jar, "instance", inst.name);
        List<string> paths = Tools.AffectedPaths("install_local_mod", args, "");
        Assert.Contains(paths, p => p.EndsWith("mymod-new.jar", StringComparison.Ordinal));
        Assert.Contains(paths, p => p.EndsWith("mymod-old.jar", StringComparison.Ordinal));
        Assert.Contains(paths, p => p.Replace('\\', '/').EndsWith(ModDevOps.ReplacedDir, StringComparison.Ordinal));
        PyDict? preview = Preview.ChangePreview("install_local_mod", args, "");
        Assert.NotNull(preview);
        List<string> lines = Py.ListOr(preview!.Get("lines")).Select(Py.S).ToList();
        Assert.Contains(lines, l => l.StartsWith("+ mods/mymod-new.jar", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("- mods/mymod-old.jar", StringComparison.Ordinal));
        Assert.NotNull(Preview.ChangePreview("run_test_launch", PyDict.Of("instance", inst.name), ""));
        Assert.Equal(I18nCore.Tr("装本地模组") + " mymod-new.jar", Tools.Label("install_local_mod", args));
        Assert.Contains(Tools.AffectedPaths("create_test_instance", PyDict.Of("mc_version", "1.20.1", "loader", "fabric"), ""),
            p => Path.GetFileName(p).StartsWith("[dev] 1.20.1-fabric", StringComparison.Ordinal));
    }
}

public class TestLaunchTests
{
    private static async Task<PyDict> RunTool(ToolRunner runner, PyDict args)
    {
        string text = await runner.RunAsync(new ToolCall("t" + Guid.NewGuid().ToString("N").Substring(0, 6), "run_test_launch", args));
        return Py.D(PyJson.Loads(text));
    }

    private static ToolRunner Runner(string instance) => new(new ModDevFakeApi()) { PermissionMode = "yolo", Instance = instance };

    [Fact]
    public async Task Ready_WhenMainMenuMarkerAppears_StopsTheGame()
    {
        using var env = new ModDevEnv();
        Instance inst = env.Instance("dev", true, "fabric", versionId: "fabric-loader-0.16.0-1.20.1");
        var driver = new FakeLaunchDriver();
        driver.Script.AddRange(new[]
        {
            "[main/INFO]: Loading Minecraft 1.20.1 with Fabric Loader 0.16.0",
            "[main/WARN]: Mod mymod uses deprecated API",
            "[Render thread/INFO]: Backend library: LWJGL version 3.3.1",
            "[Render thread/INFO]: Sound engine started",
            "[Render thread/INFO]: never reached",
        });
        ModDevHost.LaunchDriverFactory = _ => driver;
        PyDict r = await RunTool(Runner(inst.name), PyDict.Of("instance", inst.name, "wait", true, "timeout_seconds", 30L));
        Assert.Equal("ready", Py.S(r.Get("outcome")));
        Assert.True(Py.Truthy(r.Get("ok")));
        Assert.Equal("Sound engine started", Py.S(r.Get("marker")));
        Assert.True(Py.Truthy(r.Get("game_stopped_by_tipcl")));
        Assert.Equal(1, driver.Stops);
        Assert.Equal(inst.name, driver.StartedInstance);
        Assert.Equal("fabric-loader-0.16.0-1.20.1", driver.StartedVersion);   // 没给 version:用测试实例装好的那个
        Assert.True(Py.Float(r.Get("elapsed_s")) >= 0);
        Assert.Contains(Py.ListOr(r.Get("log_excerpt")).Select(Py.S), l => l.Contains("Sound engine started"));
        Assert.Null(r.Get("crash"));
        Assert.Equal("succeeded", Py.S(r.Get("state")));
    }

    [Fact]
    public async Task Crash_WhenGameDiesBeforeMenu_ReportsCrashSummary()
    {
        using var env = new ModDevEnv();
        Instance inst = env.Instance("dev", true, "fabric", versionId: "fabric-loader-0.16.0-1.20.1");
        var driver = new FakeLaunchDriver { ExitAfterLines = 3, ExitSuccess = false };
        driver.Script.AddRange(new[]
        {
            "[main/INFO]: Loading Minecraft 1.20.1",
            "[main/ERROR]: Failed to apply mixin mymod.mixins.json:TitleScreenMixin",
            "Caused by: java.lang.NoSuchMethodError: 'void net.minecraft.client.gui.screen.TitleScreen.init()'",
        });
        driver.OnExit = () =>
        {
            string dir = Path.Combine(VersionSettings.GameDir(inst, "fabric-loader-0.16.0-1.20.1"), "crash-reports");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "crash-2026-11-21_12.00.00-client.txt"),
                "---- Minecraft Crash Report ----\n// Oops.\n\nTime: 2026-11-21 12:00\nDescription: Initializing game\n\njava.lang.NoSuchMethodError: TitleScreen.init\n\tat mymod.Mixin\n\nA detailed walkthrough of the error\n-- Suspected Mods --\nDetails:\n\tSuspected Mod: mymod 1.1.0\n");
        };
        ModDevHost.LaunchDriverFactory = _ => driver;
        PyDict r = await RunTool(Runner(inst.name), PyDict.Of("instance", inst.name, "wait", true));
        Assert.Equal("crashed", Py.S(r.Get("outcome")));
        Assert.False(Py.Truthy(r.Get("ok")));
        Assert.False(Py.Truthy(r.Get("game_stopped_by_tipcl")));   // 自己退的,TipCL 没去关
        PyDict crash = Py.D(r.Get("crash"));
        Assert.Equal("Initializing game", Py.S(crash.Get("description")));
        Assert.Contains("NoSuchMethodError", Py.S(crash.Get("exception")));
        Assert.Contains(Py.ListOr(crash.Get("suspected_mods")).Select(Py.S), s => s.Contains("mymod"));
        Assert.Contains(Py.ListOr(r.Get("log_excerpt")).Select(Py.S), l => l.Contains("Caused by"));
        Assert.NotNull(r.Get("diagnosis") ?? r.Get("diagnosis_error"));
        Assert.Equal("failed", Py.S(r.Get("state")));
    }

    [Fact]
    public async Task CrashMarkerInLog_EndsEarly_EvenIfProcessStillAlive()
    {
        using var env = new ModDevEnv();
        Instance inst = env.Instance("dev", true, "fabric", versionId: "v1");
        var driver = new FakeLaunchDriver();
        driver.Script.AddRange(new[] { "boot", "#@!@# Game crashed! Crash report saved to: #@!@# crash.txt", "still alive" });
        ModDevHost.LaunchDriverFactory = _ => driver;
        PyDict r = await RunTool(Runner(inst.name), PyDict.Of("instance", inst.name, "version", "v1", "wait", true));
        Assert.Equal("crashed", Py.S(r.Get("outcome")));
        Assert.Equal(1, driver.Stops);
    }

    [Fact]
    public async Task Timeout_WhenNoMarker_StopsTheGame()
    {
        using var env = new ModDevEnv();
        Instance inst = env.Instance("dev", true, "fabric", versionId: "v1");
        var driver = new FakeLaunchDriver();
        driver.Script.AddRange(Enumerable.Range(0, 1000).Select(i => "[main/INFO]: loading " + i));
        ModDevHost.LaunchDriverFactory = _ => driver;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        PyDict r = await RunTool(Runner(inst.name), PyDict.Of("instance", inst.name, "wait", true, "timeout_seconds", 1L));   // 夹到下限 5 秒
        Assert.Equal("timeout", Py.S(r.Get("outcome")));
        Assert.True(sw.Elapsed.TotalSeconds >= 4.5, "超时下限没生效:" + sw.Elapsed);
        Assert.True(Py.Truthy(r.Get("game_stopped_by_tipcl")));
        Assert.Equal(1, driver.Stops);
    }

    [Fact]
    public async Task CustomReadyPattern_AndLaunchFailure()
    {
        using var env = new ModDevEnv();
        Instance inst = env.Instance("dev", true, "fabric", versionId: "v1");
        var driver = new FakeLaunchDriver();
        driver.Script.AddRange(new[] { "a", "[Render thread/INFO]: MyMod title hook ready", "b" });
        ModDevHost.LaunchDriverFactory = _ => driver;
        PyDict r = await RunTool(Runner(inst.name), PyDict.Of("instance", inst.name, "wait", true, "ready_pattern", "MyMod title hook (ready|ok)"));
        Assert.Equal("ready", Py.S(r.Get("outcome")));
        Assert.Equal("MyMod title hook ready", Py.S(r.Get("marker")));

        var failing = new FakeLaunchDriver { FailStart = true };
        ModDevHost.LaunchDriverFactory = _ => failing;
        PyDict f = await RunTool(Runner(inst.name), PyDict.Of("instance", inst.name, "wait", true));
        Assert.Equal("launch_failed", Py.S(f.Get("outcome")));
        Assert.Contains("没有可用账号", Py.S(f.Get("message")));

        PyDict bad = await RunTool(Runner(inst.name), PyDict.Of("instance", inst.name, "ready_pattern", "(unclosed"));
        Assert.Equal("bad_arguments", Py.S(bad.Get("error")));
    }

    [Fact]
    public async Task NoWait_ReturnsTaskId_OnlyOneAtATime_AndGetTaskStatusFollowsIt()
    {
        using var env = new ModDevEnv();
        Instance inst = env.Instance("dev", true, "fabric", versionId: "v1");
        var gate = new ManualResetEventSlim(false);
        var driver = new FakeLaunchDriver();
        driver.Script.AddRange(new[] { "x", "y" });
        driver.Script.Add("Sound engine started");
        ModDevHost.LaunchDriverFactory = _ => new GatedDriver(driver, gate);
        ToolRunner runner = Runner(inst.name);
        PyDict started = await RunTool(runner, PyDict.Of("instance", inst.name));
        Assert.True(Py.Truthy(started.Get("ok")), PyJson.Dumps(started));
        string taskId = Py.S(started.Get("task_id"));
        PyDict busy = await RunTool(runner, PyDict.Of("instance", inst.name));
        Assert.Equal("busy", Py.S(busy.Get("error")));
        Assert.Equal(taskId, Py.S(busy.Get("task_id")));

        string running = await runner.RunAsync(new ToolCall("s1", "get_task_status", PyDict.Of("task_id", taskId)));
        Assert.Contains("\"running\"", running);
        gate.Set();
        string done = await runner.RunAsync(new ToolCall("s2", "get_task_status", PyDict.Of("task_id", taskId, "wait_seconds", 20L)));
        PyDict status = Py.D(PyJson.Loads(done));
        Assert.Equal("succeeded", Py.S(status.Get("state")));
        Assert.Equal("ready", Py.S(Py.D(status.Get("result")).Get("outcome")));
        Assert.Equal("run_test_launch", Py.S(status.Get("kind")));
        PyDict missing = Py.D(PyJson.Loads(await runner.RunAsync(new ToolCall("s3", "get_task_status", PyDict.Of("task_id", "dev-000000000000")))));
        Assert.Equal("not_found", Py.S(missing.Get("error")));
    }

    [Fact]
    public async Task GetTaskStatus_ReadsLauncherTasksThroughTheHostProbe()
    {
        using var env = new ModDevEnv();
        int calls = 0;
        ModDevHost.HostTaskProbe = id => id != "task-abc" ? null : PyDict.Of(
            "title", "安装模组 钠", "status", ++calls < 3 ? "下载中" : "已完成", "finished", calls >= 3, "success", true, "progress", 50L, "log", "a\nb\nc");
        PyDict s = await ModDevTasks.StatusAsync("task-abc", 2, 5);
        Assert.Equal("launcher", Py.S(s.Get("source")));
        Assert.Equal("succeeded", Py.S(s.Get("state")));
        Assert.Equal(new[] { "b", "c" }, Py.ListOr(s.Get("log_tail")).Select(Py.S));
        Assert.True(calls >= 3);
        Assert.Equal("not_found", Py.S((await ModDevTasks.StatusAsync("task-zzz", 2, 0)).Get("error")));
    }

    [Fact]
    public async Task TestLaunch_RestoresTheUsersDefaultInstance()
    {
        using var env = new ModDevEnv();
        Instance inst = env.Instance("dev", true, "fabric", versionId: "v1");
        Config.CONFIG.Set("default_instance", "my-daily");
        var driver = new FakeLaunchDriver();
        driver.Script.Add("Sound engine started");
        ModDevHost.LaunchDriverFactory = _ => new SwitchingDriver(driver, inst.name);
        PyDict r = await RunTool(Runner(inst.name), PyDict.Of("instance", inst.name, "wait", true));
        Assert.Equal("ready", Py.S(r.Get("outcome")));
        Assert.Equal("my-daily", Py.S(Config.CONFIG.Get("default_instance", "")));
    }

    /// <summary>启动前卡在闸门上(模拟游戏还在加载),放行后才吐日志。</summary>
    private sealed class GatedDriver : ITestLaunchDriver
    {
        private readonly FakeLaunchDriver _inner;
        private readonly ManualResetEventSlim _gate;

        public GatedDriver(FakeLaunchDriver inner, ManualResetEventSlim gate)
        {
            _inner = inner;
            _gate = gate;
        }

        public Task<string> StartAsync(string instance, string version, CancellationToken ct) => _inner.StartAsync(instance, version, ct);

        public TestLaunchProbe Probe(string handle) => _gate.IsSet ? _inner.Probe(handle) : new TestLaunchProbe { Message = "running" };

        public Task StopAsync(string handle) => _inner.StopAsync(handle);
    }

    /// <summary>像真的 launch_game 一样把默认实例改成被启动的实例。</summary>
    private sealed class SwitchingDriver : ITestLaunchDriver
    {
        private readonly FakeLaunchDriver _inner;
        private readonly string _inst;

        public SwitchingDriver(FakeLaunchDriver inner, string inst)
        {
            _inner = inner;
            _inst = inst;
        }

        public Task<string> StartAsync(string instance, string version, CancellationToken ct)
        {
            Config.CONFIG.Set("default_instance", _inst);
            return _inner.StartAsync(instance, version, ct);
        }

        public TestLaunchProbe Probe(string handle) => _inner.Probe(handle);

        public Task StopAsync(string handle) => _inner.StopAsync(handle);
    }
}

public class ModDevRegistryTests
{
    [Fact]
    public async Task NewTools_AreRegisteredInSchemaMetaAndDispatch()
    {
        List<string> names = Tools.Names();
        foreach (string n in ModDevSchemas.TOOL_NAMES)
        {
            Assert.Contains(n, names);
            Assert.NotNull(ToolMeta.Get(n));
            Assert.True(await ToolRunner.DispatchesAsync(n), n + " 分发不认");
            Assert.NotEqual(n, Tools.Label(n));     // 有中文标签
        }
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.True(ToolMeta.Get("get_task_status")!.@readonly);
        Assert.True(ToolMeta.Get("get_task_status")!.long_running);   // 读共享任务表:不进并行组
        Assert.False(Scheduler.IsParallelizable(new ToolCall("x", "get_task_status", new PyDict())));
        Assert.Equal("launch", ToolMeta.Get("run_test_launch")!.side_effect);
        Assert.True(ToolMeta.Get("run_test_launch")!.long_running);
        Assert.Equal("delete", ToolMeta.Get("revert_local_mod")!.side_effect);
        Assert.False(ToolRunner.AllowsAlways("revert_local_mod"));
        Assert.True(ToolRunner.AllowsAlways("install_local_mod"));
        Assert.Empty(await AiToolRefs.ProblemsAsync());
    }

    [Fact]
    public void KeywordTable_BringsTheModDevToolsIn_OnlyWhenAskedFor()
    {
        List<string> Pick(string text) => Tools.SelectToolSchemas(new List<object> { PyDict.Of("role", "user", "content", text) }).Select(Tools.SchemaName).ToList();
        List<string> dev = Pick("帮我建一个 1.20.1 fabric 的测试实例,装我本地 jar 跑一下测试启动");
        Assert.Contains("create_test_instance", dev);
        Assert.Contains("install_local_mod", dev);
        Assert.Contains("run_test_launch", dev);
        Assert.DoesNotContain("install_local_mod", Pick("内存设多少合适"));
    }
}

public class McpModDevTests
{
    private static string Rpc(int id, string method, string paramsJson = "{}")
        => "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"" + method + "\",\"params\":" + paramsJson + "}";

    private static string Call(int id, string tool, object args)
        => Rpc(id, "tools/call", "{\"name\":\"" + tool + "\",\"arguments\":" + JsonSerializer.Serialize(args) + "}");

    private static JsonElement Parse(string? json)
    {
        Assert.NotNull(json);
        using JsonDocument doc = JsonDocument.Parse(json!);
        return doc.RootElement.Clone();
    }

    private static string Text(JsonElement r) => r.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString() ?? "";

    private static bool IsError(JsonElement r) => r.GetProperty("result").GetProperty("isError").GetBoolean();

    [Fact]
    public void ConfirmTimeout_IsDeniedWithReason_AndSignalsTheHostToCloseTheDialog()
    {
        using var env = new ModDevEnv();
        var api = new ModDevFakeApi();
        CancellationToken seen = default;
        var never = new TaskCompletionSource<(bool, bool)>();
        var server = new McpHttpServer
        {
            ConfirmTimeout = TimeSpan.FromMilliseconds(300),
            RunnerFactory = () => new ToolRunner(api)
            {
                PermissionMode = "yolo",
                Confirm = (call, label, reason, preview) =>
                {
                    seen = McpConfirmGate.CurrentToken;   // 宿主在同步开头读
                    return never.Task;                     // 用户一直不点
                },
            },
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        JsonElement r = Parse(server.Dispatch(Call(1, "enable_mod", new { filename = "a.jar" })));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
        Assert.True(IsError(r));
        string text = Text(r);
        Assert.Contains("permission_denied", text);
        Assert.Contains("confirm_timeout", text);
        Assert.DoesNotContain("enable_mod", api.Methods);
        Assert.True(seen.CanBeCanceled);
        Assert.True(seen.IsCancellationRequested);   // 宿主据此关掉还开着的确认框
        Assert.Equal(TimeSpan.FromSeconds(120), McpServer.ConfirmTimeout);   // 产品默认 120 秒
    }

    [Fact]
    public void ConfirmAnsweredInTime_IsNotATimeout()
    {
        var api = new ModDevFakeApi();
        var server = new McpHttpServer
        {
            ConfirmTimeout = TimeSpan.FromSeconds(30),
            RunnerFactory = () => new ToolRunner(api) { Confirm = async (_, _, _, _) => { await Task.Delay(50); return (false, false); } },
        };
        string text = Text(Parse(server.Dispatch(Call(1, "enable_mod", new { filename = "a.jar" }))));
        Assert.Contains("permission_denied", text);
        Assert.DoesNotContain("confirm_timeout", text);
    }

    [Fact]
    public async Task Http_DevLoop_CreateInstallTestRevert_AllThroughMcp()
    {
        using var env = new ModDevEnv();
        var api = new ModDevFakeApi();
        var confirms = new List<string>();
        var driver = new FakeLaunchDriver();
        driver.Script.AddRange(new[] { "[main/INFO]: Loading", "[Render thread/INFO]: Sound engine started" });
        ModDevHost.LaunchDriverFactory = _ => driver;
        using var server = new McpHttpServer
        {
            RunnerFactory = () => new ToolRunner(api)
            {
                PermissionMode = "default",
                Instance = "",
                Confirm = (call, _, reason, _) =>
                {
                    lock (confirms) confirms.Add(call.name);
                    return Task.FromResult((true, true));
                },
            },
        };
        Assert.True(server.Start(0), server.LastError);   // 随机空闲端口
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        async Task<JsonElement> Post(string body)
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using HttpResponseMessage resp = await http.PostAsync(server.Url, content);
            Assert.Equal(200, (int)resp.StatusCode);
            return Parse(await resp.Content.ReadAsStringAsync());
        }

        JsonElement list = await Post(Rpc(1, "tools/list"));
        var tools = list.GetProperty("result").GetProperty("tools").EnumerateArray().ToDictionary(t => t.GetProperty("name").GetString() ?? "", t => t);
        foreach (string n in ModDevSchemas.TOOL_NAMES)
        {
            Assert.True(tools.ContainsKey(n), n);
        }
        Assert.True(tools["get_task_status"].GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
        Assert.True(tools["revert_local_mod"].GetProperty("annotations").GetProperty("destructiveHint").GetBoolean());

        // 1. 建测试实例(不装游戏本体;再手动造一个版本 JSON 代替真安装)
        JsonElement created = await Post(Call(2, "create_test_instance", new { mc_version = "1.20.1", loader = "fabric", install = false }));
        Assert.False(IsError(created), Text(created));
        PyDict c = Py.D(PyJson.Loads(Text(created)));
        var inst = new Instance(Py.S(c.Get("instance")));
        env.Track(inst);
        Assert.True(ModDevOps.IsDev(inst));
        string vid = "fabric-loader-0.16.0-1.20.1";
        Directory.CreateDirectory(Path.Combine(inst.path, "versions", vid));
        File.WriteAllText(Path.Combine(inst.path, "versions", vid, vid + ".json"), "{\"id\":\"" + vid + "\"}");

        // 2. 装外部工具编译出来的 jar
        string jar = ModDevJars.Fabric(env.Jar("examplemod-1.0.0.jar"), "examplemod");
        JsonElement installed = await Post(Call(3, "install_local_mod", new { jar_path = jar, instance = inst.name }));
        Assert.False(IsError(installed), Text(installed));
        string changeId = Py.S(Py.D(PyJson.Loads(Text(installed))).Get("change_id"));
        Assert.True(File.Exists(Path.Combine(ModDevOps.ModsDirOf(inst), "examplemod-1.0.0.jar")));

        // 3. 测试启动(假驱动),等结果
        JsonElement launched = await Post(Call(4, "run_test_launch", new { instance = inst.name, wait = true }));
        Assert.False(IsError(launched), Text(launched));
        PyDict l = Py.D(PyJson.Loads(Text(launched)));
        Assert.Equal("ready", Py.S(l.Get("outcome")));
        JsonElement status = await Post(Call(5, "get_task_status", new { task_id = Py.S(l.Get("task_id")) }));
        Assert.False(IsError(status));
        Assert.Contains("succeeded", Text(status));

        // 查不存在的任务:如实报错
        Assert.True(IsError(await Post(Call(6, "get_task_status", new { task_id = "dev-nope" }))));

        // 4. 撤回
        JsonElement reverted = await Post(Call(7, "revert_local_mod", new { change_id = changeId }));
        Assert.False(IsError(reverted), Text(reverted));
        Assert.False(File.Exists(Path.Combine(ModDevOps.ModsDirOf(inst), "examplemod-1.0.0.jar")));

        // 外部写操作每次都确认(建实例 / 装 jar / 测试启动 / 撤回各一次),只读查询不确认
        Assert.Equal(new[] { "create_test_instance", "install_local_mod", "run_test_launch", "revert_local_mod" }, confirms);
        server.Stop();
    }
}
