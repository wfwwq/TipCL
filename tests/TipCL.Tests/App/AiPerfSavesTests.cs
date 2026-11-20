// AI-perf-saves 回归:卡顿优化(get_system_info / diagnose_lag / tune_jvm)+ 存档备份
// (list_saves / backup_save / list_save_backups / restore_save_backup)。
//
// 全部走真实临时目录(测试程序集的隔离 TIPCL_HOME)与真实实例目录,不 mock 文件系统;
// 宿主门面只假一件事:is_game_running(测「游戏运行中拒绝写存档」)。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TipCL.App.Common;
using TipCL.App.Pages.Ai;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.App;

public class AiPerfSavesTests : IDisposable
{
    private readonly IApi _api = InProcessApi.Instance;
    private readonly string _inst = "perf-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    private readonly string _tmp;
    private readonly object? _savedDefaultMem;
    private readonly object? _savedGc;

    public AiPerfSavesTests()
    {
        TestApp.Run(() => _api.CallAsync<object>("create_instance", new { name = _inst }).GetAwaiter().GetResult());
        _tmp = Path.Combine(Path.GetTempPath(), "tipcl-perf-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(_tmp);
        _savedDefaultMem = Config.CONFIG.Get("default_memory_mb");
        _savedGc = Config.CONFIG.Get("gc_preset");
    }

    public void Dispose()
    {
        Config.CONFIG.Set("default_memory_mb", _savedDefaultMem!);
        Config.CONFIG.Set("gc_preset", _savedGc!);
        TestApp.Run(() =>
        {
            try { _api.CallAsync<object>("delete_instance", new { name = _inst }).GetAwaiter().GetResult(); }
            catch (Exception) { }
        });
        try { Directory.Delete(_tmp, true); } catch (Exception) { }
    }

    // ───────────────────────────── 夹具 ─────────────────────────────

    /// <summary>宿主门面:只回答 is_game_running(可切换),其余空。</summary>
    private sealed class FakeBackend : ToolRunner.IBackend
    {
        public bool Running;
        public readonly List<string> Calls = new();

        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
        {
            Calls.Add(method);
            if (method == "is_game_running" && Running is T b)
            {
                return Task.FromResult<T?>(b);
            }
            return Task.FromResult<T?>(default);
        }
    }

    private string InstDir() => Instances.GetInstancePath(_inst);

    private Instance Inst() => new(_inst);

    private string World(string name, string levelText)
    {
        string dir = Path.Combine(InstDir(), "saves", name);
        Directory.CreateDirectory(Path.Combine(dir, "region"));
        File.WriteAllText(Path.Combine(dir, "level.dat"), levelText);
        File.WriteAllText(Path.Combine(dir, "region", "r.0.0.mca"), "region-" + levelText);
        return dir;
    }

    private void Mods(params string[] files)
    {
        string dir = Path.Combine(InstDir(), "mods");
        Directory.CreateDirectory(dir);
        foreach (string f in files)
        {
            File.WriteAllBytes(Path.Combine(dir, f), new byte[] { 0x50, 0x4B, 0x03, 0x04 });
        }
    }

    private ToolRunner Runner(FakeBackend api, string mode = "yolo") => new(api) { PermissionMode = mode, Instance = _inst };

    private static PyDict Json(string text) => Py.D(PyJson.Loads(text));

    private static string ZipEntryText(string zip, string entrySuffix)
    {
        using ZipArchive archive = ZipFile.OpenRead(zip);
        ZipArchiveEntry entry = archive.Entries.First(e => e.FullName.Replace('\\', '/').EndsWith(entrySuffix, StringComparison.Ordinal));
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    // ───────────────────────────── SystemInfo(纯函数)─────────────────────────────

    [Fact]
    public void Meminfo_PrefersMemAvailable_AndFallsBackToMemFree()
    {
        PyDict mem = SystemInfo.ParseMeminfo("MemTotal:       16384000 kB\nMemFree:          512000 kB\nMemAvailable:    8192000 kB\n");
        Assert.Equal(16000L, Py.Int(mem.Get("total_mb")));
        Assert.Equal(8000L, Py.Int(mem.Get("avail_mb")));
        Assert.Equal(50L, Py.Int(mem.Get("load_percent")));

        PyDict old = SystemInfo.ParseMeminfo("MemTotal: 2048000 kB\nMemFree: 1024000 kB\n");
        Assert.Equal(1000L, Py.Int(old.Get("avail_mb")));
        Assert.Equal(0L, Py.Int(SystemInfo.ParseMeminfo("").Get("total_mb")));
    }

    [Fact]
    public void Cpuinfo_CountsPhysicalCoresNotSockets()
    {
        // 1 插槽 2 核 4 线程:Sysinfo 按 physical id 去重会数成 1,这里必须是 2
        string x86 = string.Join("\n", Enumerable.Range(0, 4).Select(i =>
            $"processor\t: {i}\nmodel name\t: Intel(R) Core(TM) i5-7200U CPU @ 2.50GHz\nphysical id\t: 0\ncore id\t\t: {i / 2}\ncpu cores\t: 2\n"));
        PyDict cpu = SystemInfo.ParseCpuinfo(x86);
        Assert.Equal("Intel(R) Core(TM) i5-7200U CPU @ 2.50GHz", Py.S(cpu.Get("name")));
        Assert.Equal(4L, Py.Int(cpu.Get("cores_logical")));
        Assert.Equal(2L, Py.Int(cpu.Get("cores_physical")));

        // ARM:没有 model name / core id,名称从 Model 取,物理核数如实记 0(不知道)
        PyDict arm = SystemInfo.ParseCpuinfo("processor\t: 0\nBogoMIPS\t: 108.00\n\nprocessor\t: 1\nBogoMIPS\t: 108.00\n\nModel\t\t: Raspberry Pi 4 Model B Rev 1.4\n");
        Assert.Equal("Raspberry Pi 4 Model B Rev 1.4", Py.S(arm.Get("name")));
        Assert.Equal(2L, Py.Int(arm.Get("cores_logical")));
        Assert.Equal(0L, Py.Int(arm.Get("cores_physical")));
    }

    [Fact]
    public void Lspci_ParsesBothShapes_AndKeepsOnlyDisplayDevices()
    {
        string mm = "00:02.0 \"VGA compatible controller\" \"Intel Corporation\" \"UHD Graphics 620\" -r07 \"Lenovo\" \"Device 2258\"\n"
                    + "00:1f.3 \"Audio device\" \"Intel Corporation\" \"Sunrise Point-LP HD Audio\" -r21 \"Lenovo\" \"Device 2258\"\n"
                    + "01:00.0 \"3D controller\" \"NVIDIA Corporation\" \"GP108M [GeForce MX150]\" -ra1 \"Lenovo\" \"Device 2258\"\n";
        List<object> gpus = SystemInfo.ParseLspci(mm);
        Assert.Equal(2, gpus.Count);
        Assert.Equal("Intel Corporation UHD Graphics 620", Py.S(Py.D(gpus[0]).Get("name")));
        Assert.Equal("NVIDIA", Py.S(Py.D(gpus[1]).Get("vendor")));

        List<object> plain = SystemInfo.ParseLspci("06:00.0 VGA compatible controller: Advanced Micro Devices, Inc. [AMD/ATI] Navi 23 [Radeon RX 6600] (rev c7)\n");
        Assert.Single(plain);
        Assert.Equal("Advanced Micro Devices, Inc. [AMD/ATI] Navi 23 [Radeon RX 6600]", Py.S(Py.D(plain[0]).Get("name")));
        Assert.Equal("AMD", Py.S(Py.D(plain[0]).Get("vendor")));
    }

    [Fact]
    public void Drm_And_NvidiaProc_AreReadFromSysfs_AndMergedWithoutLspci()
    {
        string drm = Path.Combine(_tmp, "sys", "class", "drm");
        void Card(string name, string vendor, string device, string driver, string? vram = null)
        {
            string dev = Path.Combine(drm, name, "device");
            Directory.CreateDirectory(dev);
            File.WriteAllText(Path.Combine(dev, "vendor"), vendor + "\n");
            File.WriteAllText(Path.Combine(dev, "device"), device + "\n");
            File.WriteAllText(Path.Combine(dev, "uevent"), "DRIVER=" + driver + "\nPCI_CLASS=30000\n");
            if (vram != null)
            {
                File.WriteAllText(Path.Combine(dev, "mem_info_vram_total"), vram);
            }
        }
        Card("card0", "0x8086", "0x5917", "i915");
        Card("card1", "0x1002", "0x73ff", "amdgpu", (8L * 1024 * 1024 * 1024).ToString());
        Card("card2", "0x10de", "0x1d10", "nvidia");
        Directory.CreateDirectory(Path.Combine(drm, "card0-HDMI-A-1")); // 输出口,不是显卡

        List<object> fromDrm = SystemInfo.GpusFromDrm(drm);
        Assert.Equal(3, fromDrm.Count);
        Assert.Equal("i915", Py.S(Py.D(fromDrm[0]).Get("driver")));
        Assert.Equal(8192L, Py.Int(Py.D(fromDrm[1]).Get("vram_mb")));
        Assert.Equal("AMD", Py.S(Py.D(fromDrm[1]).Get("vendor")));

        // 真机上目录名是 PCI 地址(0000:01:00.0);Windows 跑测试时文件名不许带冒号,换成同形的下划线
        string nvRoot = Path.Combine(_tmp, "proc", "driver", "nvidia", "gpus", OperatingSystem.IsWindows() ? "0000_01_00.0" : "0000:01:00.0");
        Directory.CreateDirectory(nvRoot);
        File.WriteAllText(Path.Combine(nvRoot, "information"), "Model: \t\t NVIDIA GeForce MX150\nIRQ:   142\n");
        List<object> nv = SystemInfo.GpusFromNvidiaProc(Path.GetDirectoryName(nvRoot)!);
        Assert.Equal("NVIDIA GeForce MX150", Py.S(Py.D(nv.Single()).Get("name")));

        // 没装 lspci:NVIDIA 那张卡用 /proc 的型号,其余用厂商 + 设备号
        List<object> merged = SystemInfo.MergeLinuxGpus(new List<object>(), fromDrm, nv);
        Assert.Contains(merged, g => Py.S(Py.D(g).Get("name")) == "NVIDIA GeForce MX150");
        Assert.Contains(merged, g => Py.S(Py.D(g).Get("name")).StartsWith("Intel GPU", StringComparison.Ordinal));

        // 装了 lspci:用它的名字,驱动 / 显存从 /sys 补上
        List<object> withLspci = SystemInfo.MergeLinuxGpus(
            SystemInfo.ParseLspci("06:00.0 VGA compatible controller: AMD Radeon RX 6600\n"), fromDrm, nv);
        PyDict amd = Py.D(withLspci.Single());
        Assert.Equal("amdgpu", Py.S(amd.Get("driver")));
        Assert.Equal(8192L, Py.Int(amd.Get("vram_mb")));
    }

    [Fact]
    public async Task GetSystemInfo_ReturnsWindowsShapedFields()
    {
        var call = new ToolCall("s1", "get_system_info", new PyDict());
        string text = await Runner(new FakeBackend()).RunAsync(call);
        Assert.Equal(ToolCallStatus.COMPLETED, call.status);
        PyDict r = Json(text);
        Assert.True(Py.Int(r.Get("total_mb")) > 0, text);
        Assert.Equal(Py.Int(r.Get("total_mb")), Py.Int(Py.D(r.Get("memory")).Get("total_mb")));
        Assert.True(r.ContainsKey("avail_mb"));
        Assert.True(Py.Int(Py.D(r.Get("cpu")).Get("cores_logical")) > 0);
        Assert.IsType<List<object>>(r.Get("gpus"));
        Assert.IsType<List<object>>(r.Get("java"));
        Assert.Contains("CPU ", Py.S(r.Get("text")));
        Assert.Contains("内存 ", Py.S(r.Get("text")));
        Assert.True(ToolMeta.Get("get_system_info")!.@readonly);
    }

    // ───────────────────────────── 推荐值 / 生效设置 / 启动链 ─────────────────────────────

    [Fact]
    public void Recommendations_MatchWindowsTable()
    {
        Assert.Equal(3072, JvmTuning.RecommendMemory(16384, 0));
        Assert.Equal(4096, JvmTuning.RecommendMemory(16384, 30));
        Assert.Equal(6144, JvmTuning.RecommendMemory(16384, 60));
        Assert.Equal(8192, JvmTuning.RecommendMemory(16384, 150));
        Assert.Equal(10240, JvmTuning.RecommendMemory(32768, 250));
        Assert.Equal(2816, JvmTuning.RecommendMemory(4096, 200)); // 封顶电脑内存 70%,再按 256 取整
        Assert.Equal("auto", JvmTuning.RecommendGc(2048, 17));
        Assert.Equal("g1_tuned", JvmTuning.RecommendGc(6144, 17));
        Assert.Equal("zgc", JvmTuning.RecommendGc(12288, 21));
    }

    [Fact]
    public void Effective_Layers_GlobalThenInstanceThenVersion()
    {
        Config.CONFIG.Set("default_memory_mb", 3072L);
        Config.CONFIG.Set("gc_preset", "g1");
        PyDict g = JvmTuning.Effective(Inst());
        Assert.Equal(3072L, Py.Int(g.Get("memory_mb")));
        Assert.Equal("global", Py.S(g.Get("memory_source")));
        Assert.Equal("g1", Py.S(g.Get("gc_preset")));

        JvmTuning.ApplyToInstance(Inst(), 6144, "g1_tuned");
        PyDict meta = Inst().Meta();
        Assert.Equal(6144L, Py.Int(meta.Get("memory_mb")));
        Assert.Equal("g1_tuned", Py.S(meta.Get("gc_preset")));
        Assert.Equal("自动选择", Py.S(meta.Get("java"))); // 实例原有字段不丢
        PyDict i = JvmTuning.Effective(Inst());
        Assert.Equal("instance", Py.S(i.Get("memory_source")));
        Assert.Equal("instance", Py.S(i.Get("gc_source")));

        const string ver = "perf-1.0";
        Directory.CreateDirectory(Path.Combine(InstDir(), "versions", ver));
        File.WriteAllText(Path.Combine(InstDir(), "versions", ver, ver + ".json"), "{\"id\":\"" + ver + "\",\"type\":\"release\"}");
        VersionSettings.Save(Inst(), ver, PyDict.Of("memory_mb", 2048L, "gc", "zgc"));
        PyDict v = JvmTuning.Effective(Inst(), ver);
        Assert.Equal(2048L, Py.Int(v.Get("memory_mb")));
        Assert.Equal("version", Py.S(v.Get("memory_source")));
        Assert.Equal("zgc", Py.S(v.Get("gc_preset")));
        Assert.Contains(JvmTuning.VersionOverrides(Inst()), o => Py.S(Py.D(o).Get("version")) == ver);

        Assert.Throws<ValueError>(() => JvmTuning.ApplyToInstance(Inst(), 100, "g1"));
        Assert.Throws<ValueError>(() => JvmTuning.ApplyToInstance(Inst(), 4096, "parallel"));
    }

    /// <summary>
    /// 启动链真的读实例设置:LaunchFlow.Prepare(LaunchCore.RunAsync 用的就是它)给出的内存 / JVM 参数,
    /// 再交给 McLauncher.BuildLaunchCommand 拼出的命令行,调优前后 -Xmx 与 GC 参数都跟着变。
    /// Java 用 Java.VersionOutputHook 冒充(不需要本机装 Java)。
    /// </summary>
    [Fact]
    public void LaunchCommand_ChangesAfterTuning()
    {
        const string ver = "perf-launch-1.0";
        string verDir = Path.Combine(InstDir(), "versions", ver);
        Directory.CreateDirectory(Path.Combine(verDir, "natives"));
        File.WriteAllText(Path.Combine(verDir, ver + ".json"),
            "{\"id\":\"" + ver + "\",\"type\":\"release\",\"javaVersion\":{\"majorVersion\":17},"
            + "\"mainClass\":\"net.minecraft.client.main.Main\",\"libraries\":[],\"arguments\":{\"jvm\":[],\"game\":[\"--version\",\"${version_name}\"]}}");
        File.WriteAllBytes(Path.Combine(verDir, ver + ".jar"), new byte[] { 0x50, 0x4B, 0x03, 0x04 });
        string java = Path.Combine(_tmp, "jdk-17", "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
        Directory.CreateDirectory(Path.GetDirectoryName(java)!);
        File.WriteAllText(java, "fake");

        Config.CONFIG.Set("default_memory_mb", 3072L);
        Config.CONFIG.Set("gc_preset", "none");
        Func<string, string>? savedHook = Java.VersionOutputHook;
        Java.VersionOutputHook = p => p == java ? "openjdk version \"17.0.9\" 2023-10-17" : (savedHook?.Invoke(p) ?? "");
        try
        {
            List<string> Command()
            {
                PyDict prep = LaunchFlow.Prepare(Inst(), ver, new List<object>(), Config.CONFIG.Get("default_memory_mb"));
                var props = PyDict.Of("name", "Steve", "uuid", "00000000000000000000000000000000", "token", "0", "user_type", "legacy");
                return McLauncher.BuildLaunchCommand(Inst(), ver, props, java, prep["memory_mb"], 854L, 480L,
                    (List<object>)prep["extra_game_args"], prep["jvm_args"], (string)prep["game_dir"]).cmd;
            }

            List<string> before = Command();
            Assert.Contains("-Xmx3072M", before);
            Assert.DoesNotContain("-XX:+AlwaysPreTouch", before);

            PyDict r = PerfSavesTools.RunTune(PyDict.Of("instance", _inst, "apply", true, "memory_mb", 2048L, "gc", "g1_tuned"), _inst);
            Assert.True(Py.Truthy(r.Get("applied")), PyJson.Dumps(r));

            List<string> after = Command();
            Assert.Contains("-Xmx2048M", after);
            Assert.DoesNotContain("-Xmx3072M", after);
            Assert.Contains("-XX:+AlwaysPreTouch", after);   // 调优 G1 才有的参数
            Assert.Contains("-XX:+UseG1GC", after);
        }
        finally
        {
            Java.VersionOutputHook = savedHook;
        }
    }

    // ───────────────────────────── diagnose_lag ─────────────────────────────

    [Fact]
    public void DiagnoseLag_ReportsMemoryModsAndLogSignals()
    {
        Config.CONFIG.Set("default_memory_mb", 2048L);
        Config.CONFIG.Set("gc_preset", "auto");
        Mods(Enumerable.Range(0, 70).Select(i => "filler-" + i + ".jar").Append("sodium-fabric-0.5.8.jar").ToArray());
        var sys = PyDict.Of("total_mb", 16384L, "avail_mb", 1024L,
            "gpus", new List<object> { PyDict.Of("name", "Intel UHD", "vendor", "Intel"), PyDict.Of("name", "GeForce", "vendor", "NVIDIA") });
        string log = "[12:00:00] [Server thread/WARN]: Can't keep up! Is the server overloaded? Running 5012ms or 100 ticks behind\n"
                     + "[12:00:05] [Server thread/WARN]: Can't keep up! Is the server overloaded? Running 2500ms or 50 ticks behind\n"
                     + "java.lang.OutOfMemoryError: Java heap space\n";
        PyDict r = JvmTuning.DiagnoseLag(Inst(), null, sys, log);

        Assert.Equal(2048L, Py.Int(r.Get("memory_mb")));
        Assert.Equal("auto", Py.S(r.Get("gc_preset")));
        Assert.Equal(71L, Py.Int(r.Get("mod_count")));
        Assert.Equal(6144L, Py.Int(r.Get("recommended_memory_mb")));
        Assert.Equal("g1_tuned", Py.S(r.Get("recommended_gc")));
        PyDict perf = Py.D(r.Get("perf_mods"));
        Assert.Single(Py.ListOr(perf.Get("installed")));
        Assert.Contains(Py.ListOr(perf.Get("missing")), m => Py.S(m).Contains("FerriteCore"));
        PyDict logInfo = Py.D(r.Get("log"));
        Assert.Equal(2L, Py.Int(logInfo.Get("cant_keep_up")));
        Assert.Equal(5012L, Py.Int(logInfo.Get("worst_behind_ms")));
        string issues = string.Join("\n", Py.ListOr(r.Get("issues")).Select(Py.Str));
        Assert.Contains("分配偏少", issues);
        Assert.Contains("跟不上 2 次", issues);
        Assert.Contains("OutOfMemoryError", issues);
        Assert.Contains("FerriteCore", issues);
        Assert.Contains("可用内存", issues);
        string suggestions = string.Join("\n", Py.ListOr(r.Get("suggestions")).Select(Py.Str));
        Assert.Contains("tune_jvm", suggestions);
        Assert.Contains("独显", suggestions);
        Assert.Contains("游戏分配 2048 MB", Py.S(r.Get("text")));
    }

    [Fact]
    public async Task DiagnoseLag_Tool_ReadsLatestLogFromInstance()
    {
        Directory.CreateDirectory(Path.Combine(InstDir(), "logs"));
        File.WriteAllText(Path.Combine(InstDir(), "logs", "latest.log"),
            "[Server thread/WARN]: Can't keep up! Is the server overloaded? Running 3000ms or 60 ticks behind\n");
        var call = new ToolCall("d1", "diagnose_lag", PyDict.Of("instance", _inst));
        PyDict r = Json(await Runner(new FakeBackend()).RunAsync(call));
        Assert.Equal(ToolCallStatus.COMPLETED, call.status);
        Assert.True(Py.Truthy(Py.D(r.Get("log")).Get("found")));
        Assert.Equal(1L, Py.Int(Py.D(r.Get("log")).Get("cant_keep_up")));
        Assert.True(r.ContainsKey("memory_mb") && r.ContainsKey("gc_preset") && r.ContainsKey("suggestions"));
    }

    // ───────────────────────────── tune_jvm ─────────────────────────────

    [Fact]
    public async Task TuneJvm_WithoutApply_OnlySuggests()
    {
        var call = new ToolCall("t1", "tune_jvm", PyDict.Of("instance", _inst));
        PyDict r = Json(await Runner(new FakeBackend()).RunAsync(call));
        Assert.False(Py.Truthy(r.Get("applied")));
        Assert.Contains("还没改", Py.S(r.Get("message")));
        Assert.Null(JvmTuning.InstanceMemory(Inst()));
        Assert.Empty(Tools.AffectedPaths("tune_jvm", PyDict.Of("instance", _inst), _inst));
    }

    [Fact]
    public async Task TuneJvm_Apply_GoesThroughConfirmWithBeforeAfterPreview()
    {
        Config.CONFIG.Set("default_memory_mb", 4096L);
        Config.CONFIG.Set("gc_preset", "auto");
        var runner = Runner(new FakeBackend(), "default");
        PyDict? seen = null;
        string seenLabel = "";
        runner.Confirm = (_, label, _, preview) =>
        {
            seenLabel = label;
            seen = preview;
            return Task.FromResult((true, false));
        };
        var args = PyDict.Of("instance", _inst, "apply", true, "memory_mb", 3072L, "gc", "g1_tuned");
        var call = new ToolCall("t2", "tune_jvm", args);
        PyDict r = Json(await runner.RunAsync(call));

        Assert.NotNull(seen);
        Assert.Equal("diff", Py.S(seen!.Get("kind")));
        List<string> lines = Preview.Flatten(seen);
        Assert.Contains(lines, l => l.StartsWith("- ", StringComparison.Ordinal) && l.Contains("4096"));
        Assert.Contains(lines, l => l.StartsWith("+ ", StringComparison.Ordinal) && l.Contains("3072"));
        Assert.Contains(lines, l => l.Contains(".instance.json"));
        Assert.Contains(_inst, seenLabel);
        Assert.True(Py.Truthy(r.Get("applied")), PyJson.Dumps(r));
        Assert.Equal(3072L, JvmTuning.InstanceMemory(Inst()));
        Assert.Equal("g1_tuned", JvmTuning.InstanceGc(Inst()));
        // 检查点覆盖的正是这份实例设置
        Assert.Equal(new[] { JvmTuning.InstanceMetaPath(Inst()) }, Tools.AffectedPaths("tune_jvm", args, _inst));
    }

    [Fact]
    public void TuneJvm_Checkpoint_RollsBackInstanceSettings()
    {
        string? savedDir = Checkpoint.CheckpointsDir;
        Checkpoint.CheckpointsDir = Path.Combine(_tmp, "checkpoints");
        try
        {
            var args = PyDict.Of("instance", _inst, "apply", true, "memory_mb", 2048L, "gc", "zgc");
            string chat = "perf-" + Guid.NewGuid().ToString("N");
            PyDict snap = Checkpoint.Snapshot(chat, "t1", Tools.AffectedPaths("tune_jvm", args, _inst));
            Assert.True(Py.Truthy(snap.Get("ok")));
            PerfSavesTools.RunTune(args, _inst);
            Assert.Equal(2048L, JvmTuning.InstanceMemory(Inst()));
            PyDict rolled = Checkpoint.Rollback(chat, "t1");
            Assert.True(Py.Truthy(rolled.Get("ok")));
            Assert.Null(JvmTuning.InstanceMemory(Inst()));
            Assert.Null(JvmTuning.InstanceGc(Inst()));
        }
        finally
        {
            Checkpoint.CheckpointsDir = savedDir;
        }
    }

    [Fact]
    public void TuneJvm_RefusesUnsafeMemory_AndUnknownInstance()
    {
        PyDict tooHigh = PerfSavesTools.RunTune(PyDict.Of("instance", _inst, "apply", true, "memory_mb", 8192L), _inst, totalMbOverride: 4096);
        Assert.False(Py.Truthy(tooHigh.Get("ok")));
        Assert.Equal("memory_too_high", Py.S(tooHigh.Get("error")));
        Assert.Null(JvmTuning.InstanceMemory(Inst()));

        PyDict badGc = PerfSavesTools.RunTune(PyDict.Of("instance", _inst, "apply", true, "memory_mb", 2048L, "gc", "parallel"), _inst, totalMbOverride: 16384);
        Assert.False(Py.Truthy(badGc.Get("ok")));
        Assert.Null(JvmTuning.InstanceGc(Inst()));

        string bogus = "no-such-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        Assert.Throws<ValueError>(() => PerfSavesTools.RunTune(PyDict.Of("instance", bogus, "apply", true, "memory_mb", 2048L), _inst));
        Assert.False(Directory.Exists(Instances.GetInstancePath(bogus)), "不能凭空造出实例目录");
    }

    // ───────────────────────────── 存档备份 / 回滚 ─────────────────────────────

    [Fact]
    public async Task ListSaves_BackupSave_ListBackups()
    {
        World("生存", "v1");
        var runner = Runner(new FakeBackend());
        List<object> saves = Py.ListOr(PyJson.Loads(await runner.RunAsync(new ToolCall("l1", "list_saves", PyDict.Of("instance", _inst)))));
        PyDict row = Py.D(saves.Single());
        Assert.Equal("生存", Py.S(row.Get("name")));
        Assert.True(row.ContainsKey("size_mb") && row.ContainsKey("modified"));

        var backup = new ToolCall("b1", "backup_save", PyDict.Of("instance", _inst, "name", "生存"));
        PyDict r = Json(await runner.RunAsync(backup));
        Assert.True(Py.Truthy(r.Get("ok")), PyJson.Dumps(r));
        string zip = Py.S(r.Get("path"));
        Assert.True(File.Exists(zip));
        Assert.StartsWith(Path.GetFullPath(Path.Combine(InstDir(), "backups")), Path.GetFullPath(zip));
        Assert.Equal("v1", ZipEntryText(zip, "生存/level.dat"));

        List<object> backups = Py.ListOr(PyJson.Loads(await runner.RunAsync(new ToolCall("l2", "list_save_backups", PyDict.Of("instance", _inst, "name", "生存")))));
        Assert.Equal(Py.S(r.Get("backup_name")), Py.S(Py.D(backups.Single()).Get("name")));
        Assert.Equal("生存", Py.S(Py.D(backups.Single()).Get("save")));
        List<object> none = Py.ListOr(PyJson.Loads(await runner.RunAsync(new ToolCall("l3", "list_save_backups", PyDict.Of("instance", _inst, "name", "别的")))));
        Assert.Empty(none);
        // 只新增 zip:不进检查点(理由见 PerfSavesTools.AffectedPaths)
        Assert.Empty(Tools.AffectedPaths("backup_save", PyDict.Of("instance", _inst, "name", "生存"), _inst));
    }

    [Fact]
    public async Task Restore_Overwrite_BacksUpCurrentStateFirst()
    {
        string world = World("生存", "v1");
        var runner = Runner(new FakeBackend());
        PyDict first = Json(await runner.RunAsync(new ToolCall("b1", "backup_save", PyDict.Of("instance", _inst, "name", "生存"))));
        string backupName = Py.S(first.Get("backup_name"));

        // 之后又玩了一会儿:存档变成 v2,还多了个新文件
        File.WriteAllText(Path.Combine(world, "level.dat"), "v2");
        File.WriteAllText(Path.Combine(world, "new.txt"), "later");

        var args = PyDict.Of("instance", _inst, "backup_name", backupName, "overwrite", true);
        Assert.Equal(new[] { Path.GetFullPath(world) }, Tools.AffectedPaths("restore_save_backup", args, _inst).Select(p => Path.GetFullPath(p)).ToArray());
        PyDict r = Json(await runner.RunAsync(new ToolCall("r1", "restore_save_backup", args)));
        Assert.True(Py.Truthy(r.Get("ok")), PyJson.Dumps(r));

        // 回滚到了 v1,新文件没了
        Assert.Equal("v1", File.ReadAllText(Path.Combine(world, "level.dat")));
        Assert.False(File.Exists(Path.Combine(world, "new.txt")));
        // 回滚前的 v2 状态被先打成了 zip
        string safety = Py.S(r.Get("safety_backup"));
        Assert.NotEqual("", safety);
        Assert.NotEqual(backupName, safety);
        string safetyZip = Path.Combine(InstDir(), "backups", safety);
        Assert.Equal("v2", ZipEntryText(safetyZip, "生存/level.dat"));
        Assert.Equal("later", ZipEntryText(safetyZip, "生存/new.txt"));
        Assert.Contains("回滚前已把当前存档备份为", Py.S(r.Get("message")));
    }

    [Fact]
    public void Restore_WithoutOverwrite_GoesToNewFolder_AndStillBacksUp()
    {
        string world = World("创造", "v1");
        PyDict b = Saves.BackupSave(Inst(), "创造");
        File.WriteAllText(Path.Combine(world, "level.dat"), "v2");

        PyDict plan = SaveBackups.PlanRestore(Inst(), Py.S(b.Get("name")), null, overwrite: false);
        Assert.Equal("创造-还原", Py.S(plan.Get("target")));
        Assert.True(Py.Truthy(plan.Get("will_backup_current")));

        PyDict r = PerfSavesTools.Restore(PyDict.Of("instance", _inst, "backup_name", b.Get("name")), _inst);
        Assert.Equal("创造-还原", Py.S(r.Get("restored_to")));
        Assert.Equal("v1", File.ReadAllText(Path.Combine(InstDir(), "saves", "创造-还原", "level.dat")));
        Assert.Equal("v2", File.ReadAllText(Path.Combine(world, "level.dat"))); // 现有存档不动
        Assert.NotEqual("", Py.S(r.Get("safety_backup")));

        // 再来一次:名字顺延成 -还原2(预测与执行同一套算法)
        PyDict plan2 = SaveBackups.PlanRestore(Inst(), Py.S(b.Get("name")), null, overwrite: false);
        Assert.Equal("创造-还原2", Py.S(plan2.Get("target")));
        PyDict r2 = PerfSavesTools.Restore(PyDict.Of("instance", _inst, "backup_name", b.Get("name")), _inst);
        Assert.Equal("创造-还原2", Py.S(r2.Get("restored_to")));

        Assert.Throws<SaveError>(() => PerfSavesTools.Restore(PyDict.Of("instance", _inst, "backup_name", "nope.zip"), _inst));
        Assert.Throws<SaveError>(() => SaveBackups.PlanRestore(Inst(), "../x.zip", null, true));
    }

    [Fact]
    public async Task GameRunning_RefusesBackupAndRestore_WithoutTouchingDisk()
    {
        string world = World("生存", "v1");
        PyDict b = Saves.BackupSave(Inst(), "生存");
        File.WriteAllText(Path.Combine(world, "level.dat"), "v2");
        int zipsBefore = Directory.GetFiles(Path.Combine(InstDir(), "backups")).Length;

        var api = new FakeBackend { Running = true };
        var runner = Runner(api);
        PyDict r1 = Json(await runner.RunAsync(new ToolCall("g1", "backup_save", PyDict.Of("instance", _inst, "name", "生存"))));
        PyDict r2 = Json(await runner.RunAsync(new ToolCall("g2", "restore_save_backup",
            PyDict.Of("instance", _inst, "backup_name", b.Get("name"), "overwrite", true))));
        Assert.Equal(ToolRunner.GameRunningError, Py.S(r1.Get("error")));
        Assert.Equal(ToolRunner.GameRunningError, Py.S(r2.Get("error")));
        Assert.Contains("is_game_running", api.Calls);
        Assert.Equal(zipsBefore, Directory.GetFiles(Path.Combine(InstDir(), "backups")).Length);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(world, "level.dat")));

        // 退出游戏后就能回滚
        api.Running = false;
        PyDict r3 = Json(await runner.RunAsync(new ToolCall("g3", "restore_save_backup",
            PyDict.Of("instance", _inst, "backup_name", b.Get("name"), "overwrite", true))));
        Assert.True(Py.Truthy(r3.Get("ok")), PyJson.Dumps(r3));
        Assert.Equal("v1", File.ReadAllText(Path.Combine(world, "level.dat")));
    }

    [Fact]
    public async Task Restore_IsNeverAllowedForTheWholeChat()
    {
        World("生存", "v1");
        PyDict b = Saves.BackupSave(Inst(), "生存");
        var runner = Runner(new FakeBackend(), "default");
        int asked = 0;
        PyDict? preview = null;
        runner.Confirm = (_, _, reason, p) =>
        {
            asked++;
            preview = p;
            Assert.Contains("高风险", reason);
            return Task.FromResult((true, true)); // 就算界面传了「本对话都允许」
        };
        var args = PyDict.Of("instance", _inst, "backup_name", b.Get("name"), "overwrite", true);
        await runner.RunAsync(new ToolCall("n1", "restore_save_backup", args));
        await runner.RunAsync(new ToolCall("n2", "restore_save_backup", args));
        Assert.Equal(2, asked);
        Assert.False(runner.IsAllowedAlways("restore_save_backup"));
        Assert.False(ConfirmCard.AllowAlways("restore_save_backup"));
        Assert.True(ConfirmCard.AllowAlways("backup_save"));
        List<string> lines = Preview.Flatten(preview);
        Assert.Contains(lines, l => l.Contains(Py.S(b.Get("name"))));
        Assert.Contains(lines, l => l.Contains("zip")); // 「回滚前先把当前存档备份成 zip」

        // 对照:backup_save 点了「本对话都允许」就不再问
        int backupAsked = 0;
        runner.Confirm = (_, _, _, _) =>
        {
            backupAsked++;
            return Task.FromResult((true, true));
        };
        await runner.RunAsync(new ToolCall("n3", "backup_save", PyDict.Of("instance", _inst, "name", "生存")));
        await runner.RunAsync(new ToolCall("n4", "backup_save", PyDict.Of("instance", _inst, "name", "生存")));
        Assert.Equal(1, backupAsked);
    }

    // ───────────────────────────── 工具表 / 关键词 / 风险 ─────────────────────────────

    [Fact]
    public void Tools_AreRegistered_WithWindowsNamesAndRisk()
    {
        List<string> names = Tools.Names();
        foreach (string n in PerfSavesTools.Names)
        {
            Assert.Contains(n, names);
            Assert.NotNull(ToolMeta.Get(n));
        }
        Assert.Equal("write_local", ToolMeta.Get("tune_jvm")!.side_effect);
        Assert.Equal("medium", ToolMeta.Get("tune_jvm")!.risk);
        Assert.Equal("low", ToolMeta.Get("backup_save")!.risk);
        Assert.Equal("delete", ToolMeta.Get("restore_save_backup")!.side_effect);
        Assert.Equal("high", ToolMeta.Get("restore_save_backup")!.risk);
        PyDict restore = Py.D(Py.D(Tools.Schemas.Select(Py.D).First(s => Tools.SchemaName(s) == "restore_save_backup").Get("function")).Get("parameters"));
        Assert.Contains("backup_name", Py.ListOr(restore.Get("required")).Select(Py.Str));
        Assert.Equal("查看电脑配置", Tools.Label("get_system_info"));
        Assert.Equal("备份存档 生存", Tools.Label("backup_save", PyDict.Of("name", "生存")));
    }

    [Fact]
    public void Keywords_BringInTheRightTools()
    {
        List<string> lag = Tools.SelectToolSchemas(new List<object> { PyDict.Of("role", "user", "content", "好卡") }).Select(Tools.SchemaName).ToList();
        Assert.Contains("get_system_info", lag);
        Assert.Contains("diagnose_lag", lag);
        Assert.Contains("tune_jvm", lag);
        List<string> save = Tools.SelectToolSchemas(new List<object> { PyDict.Of("role", "user", "content", "备份一下我的生存存档") }).Select(Tools.SchemaName).ToList();
        Assert.Contains("backup_save", save);
        Assert.Contains("list_saves", save);
        Assert.Contains("restore_save_backup", save);
        Assert.Contains("backup_save", AiData.SystemPrompt());
        Assert.Contains("diagnose_lag", AiData.SystemPrompt());
    }
}
