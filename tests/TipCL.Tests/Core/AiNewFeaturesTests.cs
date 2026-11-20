// AI-new-features 包的 Core 测试:实例升级 / 启动前预检 / 服务器管理与 SLP / 按硬件推荐光影 / 导出诊断报告。
//
// 全部不联网:升级走假 IUpgradeSource,SLP 走本机回环上的假服务器,硬件 / Java 走注入点;
// 读写都落在 TestIsolation 的临时 TIPCL_HOME(实例用唯一名建、用完删),检查点 / 产物走 AiServiceDirs。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

/// <summary>临时实例(唯一名,Dispose 时整目录删掉)。</summary>
internal sealed class NfTempInstance : IDisposable
{
    public readonly Instance Inst;
    private readonly List<string> _extra = new();

    public NfTempInstance(string prefix = "nf")
    {
        Inst = Instances.CreateUniqueInstance(prefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
    }

    public string Name => Inst.name;

    public string PathOf(params string[] parts) => Path.Combine(new[] { Inst.path }.Concat(parts).ToArray());

    public void Write(string rel, string text)
    {
        string p = PathOf(rel.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    /// <summary>造一个带 fabric.mod.json 的 jar。</summary>
    public void FabricJar(string file, string id, string version = "1.0", string? dependsJson = null)
    {
        string p = PathOf("mods", file);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        using var fs = new FileStream(p, FileMode.Create);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        using var w = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open());
        w.Write("{\"schemaVersion\":1,\"id\":\"" + id + "\",\"version\":\"" + version + "\",\"name\":\"" + id + "\"" +
                (dependsJson != null ? ",\"depends\":" + dependsJson : "") + "}");
    }

    /// <summary>造一个版本 JSON(让 DetectLoader / DetectMcVersion / 预检能认出来)。</summary>
    public void Version(string id, int javaMajor = 17)
    {
        Write("versions/" + id + "/" + id + ".json", "{\"id\":\"" + id + "\",\"type\":\"release\",\"mainClass\":\"x\",\"javaVersion\":{\"component\":\"java-runtime\",\"majorVersion\":" + javaMajor + "}}");
    }

    public void Track(string path) => _extra.Add(path);

    public void Dispose()
    {
        foreach (string p in _extra.Append(Inst.path))
        {
            try
            {
                if (Directory.Exists(p)) Directory.Delete(p, true);
                else if (File.Exists(p)) File.Delete(p);
            }
            catch (Exception) { }
        }
    }
}

/// <summary>假门面:搜索返回固定结果,install_game 记下来。</summary>
internal sealed class NfFakeApi : ToolRunner.IBackend
{
    public readonly List<(string Method, object? Args)> Calls = new();
    public List<object> Shaders = new();

    public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
    {
        Calls.Add((method, args));
        object? result = method switch
        {
            "search_shaders" or "search_resourcepacks" => Shaders,
            "install_game" => PyDict.Of("ok", true),
            _ => new List<object>(),
        };
        if (result is T typed) return Task.FromResult<T?>(typed);
        try
        {
            object? converted = PyJson.Loads(PyJson.Dumps(result));
            return Task.FromResult(converted is T ok ? ok : default);
        }
        catch (Exception) { return Task.FromResult<T?>(default); }
    }
}

public class AiNewFeaturesRegistryTests
{
    [Fact]
    public void AllNewTools_AreInSchemaAndMeta_WithRightRisk()
    {
        List<string> names = Tools.Names();
        foreach (string n in NewFeatureEffects.TOOL_NAMES)
        {
            Assert.Contains(n, names);
            Assert.NotNull(ToolMeta.Get(n));
        }
        Assert.True(ToolMeta.Get("plan_instance_upgrade")!.@readonly);
        Assert.True(ToolMeta.Get("preflight_check")!.@readonly);
        Assert.True(ToolMeta.Get("list_servers")!.@readonly);
        Assert.True(ToolMeta.Get("ping_server")!.@readonly);
        Assert.True(ToolMeta.Get("recommend_shaders")!.@readonly);
        Assert.False(ToolMeta.Get("add_server")!.@readonly);
        Assert.False(ToolMeta.Get("export_diagnostic_report")!.@readonly);
        Assert.Equal("high", ToolMeta.Get("apply_instance_upgrade")!.risk);
        Assert.Equal("delete", ToolMeta.Get("remove_server")!.side_effect);
        // 标签有中文、模板能带参数
        Assert.Equal("启动前预检", Tools.Label("preflight_check"));
        Assert.Equal("升级实例 a → 1.21.1", Tools.Label("apply_instance_upgrade", PyDict.Of("instance", "a", "target_version", "1.21.1")));
    }

    [Fact]
    public void KeywordTable_BringsTheNewToolsIn()
    {
        List<string> Pick(string text) => Tools.SelectToolSchemas(new List<object> { PyDict.Of("role", "user", "content", text) }).Select(Tools.SchemaName).ToList();
        Assert.Contains("plan_instance_upgrade", Pick("帮我把这个整合包升级到 1.21.1"));
        Assert.Contains("ping_server", Pick("这个服务器延迟多少"));
        Assert.Contains("recommend_shaders", Pick("我的电脑适合什么光影"));
        Assert.Contains("preflight_check", Pick("启动前帮我检查一下"));
        Assert.Contains("export_diagnostic_report", Pick("把诊断报告导出给我"));
    }

    [Fact]
    public void SystemPrompt_MentionsTheNewScenarios()
    {
        string p = AiData.SystemPrompt();
        foreach (string n in new[] { "plan_instance_upgrade", "apply_instance_upgrade", "preflight_check", "ping_server", "recommend_shaders", "export_diagnostic_report" })
        {
            Assert.Contains(n, p);
        }
    }

    [Fact]
    public void DangerousNewTools_NeverGetAllowAlways()
    {
        Assert.False(ToolRunner.AllowsAlways("remove_server"));
        Assert.False(ToolRunner.AllowsAlways("apply_instance_upgrade"));
        Assert.True(ToolRunner.AllowsAlways("add_server"));
    }
}

public class ServerPingTests
{
    private sealed class FakeMcServer
    {
        public string Host = "";
        public int Port;
        public int NextState;
        public Task Done = Task.CompletedTask;
    }

    /// <summary>本机回环上的假 MC 服务器:读握手 + 状态请求,回 JSON,再回 pong。</summary>
    private static (int Port, FakeMcServer State) StartServer(string json, bool silent = false)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var state = new FakeMcServer();
        state.Done = Task.Run(async () =>
        {
            try
            {
                using TcpClient client = await listener.AcceptTcpClientAsync();
                NetworkStream s = client.GetStream();
                if (silent)
                {
                    await Task.Delay(2500);
                    return;
                }
                byte[] hs = await ServerPing.ReadPacketAsync(s, CancellationToken.None);
                int o = 0;
                ServerPing.ReadVarInt(hs, ref o); // id
                ServerPing.ReadVarInt(hs, ref o); // protocol
                int len = ServerPing.ReadVarInt(hs, ref o);
                state.Host = Encoding.UTF8.GetString(hs, o, len);
                o += len;
                state.Port = (hs[o] << 8) | hs[o + 1];
                o += 2;
                state.NextState = ServerPing.ReadVarInt(hs, ref o);
                await ServerPing.ReadPacketAsync(s, CancellationToken.None); // status request
                byte[] resp = ServerPing.Packet(0x00, w => ServerPing.WriteString(w, json));
                await s.WriteAsync(resp);
                byte[] ping = await ServerPing.ReadPacketAsync(s, CancellationToken.None);
                byte[] pong = ServerPing.Packet(0x01, w => w.Write(ping, 1, ping.Length - 1));
                await s.WriteAsync(pong);
                await Task.Delay(100);
            }
            finally
            {
                listener.Stop();
            }
        });
        return (port, state);
    }

    private const string StatusJson =
        "{\"version\":{\"name\":\"Paper 1.21.1\",\"protocol\":767},\"players\":{\"max\":100,\"online\":5,\"sample\":[{\"name\":\"§eAlex\",\"id\":\"x\"}]}," +
        "\"description\":{\"text\":\"§6TipCL \",\"extra\":[{\"text\":\"测试服\"}]},\"favicon\":\"data:image/png;base64,AAAA\"}";

    [Fact]
    public async Task Ping_LocalFakeServer_ReturnsLatencyPlayersVersionMotd()
    {
        (int port, FakeMcServer state) = StartServer(StatusJson);
        PyDict r = await ServerPing.PingAsync("127.0.0.1", port, 3000);
        await state.Done;
        Assert.True(Py.Truthy(r.Get("ok")), PyJson.Dumps(r));
        Assert.Equal(5, Py.Int(r.Get("online")));
        Assert.Equal(100, Py.Int(r.Get("max")));
        Assert.Equal("Paper 1.21.1", Py.S(r.Get("version")));
        Assert.Equal(767, Py.Int(r.Get("protocol")));
        Assert.Equal("TipCL 测试服", Py.S(r.Get("motd")));
        Assert.True(Py.Int(r.Get("latency_ms")) >= 0);
        Assert.True(Py.Truthy(r.Get("has_icon")));
        Assert.Equal("Alex", Py.S(Py.ListOr(r.Get("players_sample"))[0]));
        // 握手按协议发:地址 / 端口 / next_state=1(查状态)
        Assert.Equal("127.0.0.1", state.Host);
        Assert.Equal(port, state.Port);
        Assert.Equal(1, state.NextState);
    }

    [Fact]
    public async Task Ping_SilentServer_TimesOut()
    {
        (int port, FakeMcServer state) = StartServer(StatusJson, silent: true);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        PyDict r = await ServerPing.PingAsync("127.0.0.1", port, 400);
        Assert.False(Py.Truthy(r.Get("ok")));
        Assert.Equal("timeout", Py.S(r.Get("error")));
        Assert.True(sw.ElapsedMilliseconds < 2400, "超时没生效:" + sw.ElapsedMilliseconds + " ms");
        await state.Done;
    }

    [Fact]
    public async Task Ping_ClosedPort_IsUnreachable()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        PyDict r = await ServerPing.PingAsync("127.0.0.1", port, 2000);
        Assert.False(Py.Truthy(r.Get("ok")));
        Assert.Contains(Py.S(r.Get("error")), new[] { "unreachable", "timeout" });
    }

    [Fact]
    public void FlattenChat_AndStripFormatting()
    {
        Assert.Equal("ab", ServerPing.StripFormatting("§la§rb"));
        Assert.Equal("x y z", ServerPing.FlattenChat(PyDict.Of("text", "x ", "extra", new List<object> { "y ", PyDict.Of("text", "z") })));
        PyDict bad = ServerPing.Parse("h", 1, "not json", 0);
        Assert.False(Py.Truthy(bad.Get("ok")));
    }

    [Fact]
    public async Task PingServerTool_RunsWithoutConfirm()
    {
        (int port, FakeMcServer state) = StartServer(StatusJson);
        var runner = new ToolRunner(new NfFakeApi()) { PermissionMode = "default" };
        var call = new ToolCall("p1", "ping_server", PyDict.Of("address", "127.0.0.1:" + port, "timeout_ms", 3000L));
        string text = await runner.RunAsync(call);
        await state.Done;
        Assert.Equal(ToolCallStatus.COMPLETED, call.status);
        Assert.Contains("\"online\": 5", text);
    }
}

public class ServerToolsTests
{
    [Fact]
    public async Task AddListRemove_ThroughConfirmChain_WithPreviewAndCheckpoint()
    {
        using var dirs = new AiServiceDirs();
        using var t = new NfTempInstance("srv");
        var api = new NfFakeApi();
        var runner = new ToolRunner(api) { PermissionMode = "default", Instance = t.Name };

        // 没接确认回调:写操作被拒,servers.dat 不出现
        var denied = new ToolCall("s0", "add_server", PyDict.Of("instance", t.Name, "address", "mc.example.com"));
        await runner.RunAsync(denied);
        Assert.Equal(ToolCallStatus.DENIED, denied.status);
        Assert.False(File.Exists(t.PathOf("servers.dat")));

        var previews = new List<List<string>>();
        int asked = 0;
        runner.Confirm = (call, label, reason, preview) =>
        {
            asked++;
            previews.Add(Preview.Flatten(preview));
            return Task.FromResult((true, true)); // 用户点「本对话都允许」
        };

        // 检查点:受影响路径 = servers.dat + servers.json
        var addArgs = PyDict.Of("instance", t.Name, "name", "测试服", "address", "mc.example.com:25570");
        List<string> paths = Tools.AffectedPaths("add_server", addArgs, t.Name);
        Assert.Contains(paths, p => p.EndsWith("servers.dat", StringComparison.Ordinal));
        Assert.Contains(paths, p => p.EndsWith("servers.json", StringComparison.Ordinal));
        string chat = "chat-" + Guid.NewGuid().ToString("N");
        Assert.True(Py.Truthy(Checkpoint.Snapshot(chat, "t1", paths).Get("ok")));

        var add = new ToolCall("s1", "add_server", addArgs);
        await runner.RunAsync(add);
        Assert.Equal(ToolCallStatus.COMPLETED, add.status);
        Assert.True(File.Exists(t.PathOf("servers.dat")));
        Assert.Contains(previews[0], l => l.Contains("mc.example.com:25570"));

        var list = new ToolCall("s2", "list_servers", PyDict.Of("instance", t.Name));
        string listed = await runner.RunAsync(list);
        Assert.Contains("mc.example.com", listed);
        Assert.Contains("\"count\": 1", listed);

        // 回滚 = servers.dat 消失(快照时还不存在)
        Assert.True(Py.Truthy(Checkpoint.Rollback(chat, "t1").Get("ok")));
        Assert.False(File.Exists(t.PathOf("servers.dat")));

        // 再加一次,然后删:删除类即使点了「本对话都允许」,下次照样要问
        await runner.RunAsync(new ToolCall("s3", "add_server", addArgs));
        int before = asked;
        var rm1 = new ToolCall("s4", "remove_server", PyDict.Of("instance", t.Name, "address", "mc.example.com:25570"));
        await runner.RunAsync(rm1);
        Assert.Equal(ToolCallStatus.COMPLETED, rm1.status);
        Assert.Contains(previews.Last(), l => l.Contains("mc.example.com"));
        Assert.Empty(Servers.ListServers(t.Inst));
        await runner.RunAsync(new ToolCall("s5", "add_server", addArgs));
        var rm2 = new ToolCall("s6", "remove_server", PyDict.Of("instance", t.Name, "index", 0L));
        await runner.RunAsync(rm2);
        Assert.Equal(ToolCallStatus.COMPLETED, rm2.status);
        Assert.Equal(before + 2, asked); // remove_server 两次都问了(add_server 第二次起不再问)
        Assert.False(runner.IsAllowedAlways("remove_server"));

        // 不存在的服务器:失败而不是静默成功
        var rm3 = new ToolCall("s7", "remove_server", PyDict.Of("instance", t.Name, "index", 5L));
        await runner.RunAsync(rm3);
        Assert.Equal(ToolCallStatus.FAILED, rm3.status);
    }
}

public class AiPreflightTests
{
    [Fact]
    public void Check_FindsJavaDepsAndMemoryProblems()
    {
        using var t = new NfTempInstance("pf");
        t.Version("fabric-loader-0.15.11-1.20.5", javaMajor: 21);
        t.FabricJar("needs-api.jar", "needsapi", dependsJson: "{\"fabric-api\":\"*\"}");
        AiPreflight.JavaListHook = () => new List<object> { PyDict.Of("major", 8L, "exe", "java8") };
        try
        {
            PyDict r = AiPreflight.Check(t.Inst, "fabric-loader-0.15.11-1.20.5", 16384, null, PyDict.Of("ram_mb", 8192L));
            var codes = Py.ListOr(r.Get("items")).Select(i => Py.S(Py.D(i).Get("code"))).ToList();
            Assert.Contains("dep_missing", codes);
            Assert.Contains("memory_exceeds_physical", codes);
            Assert.Contains("java_auto_download", codes);
            Assert.False(Py.Truthy(r.Get("ok")));
            Assert.True(Py.Int(r.Get("error_count")) >= 2);
            // 每条都带下一步建议
            PyDict dep = Py.D(Py.ListOr(r.Get("items")).First(i => Py.S(Py.D(i).Get("code")) == "dep_missing"));
            Assert.Contains("fabric-api", Py.S(dep.Get("fix")));

            // 交给 AI 的开场:带实例、版本和每条问题
            string prompt = AiPreflight.BuildFixPrompt(r);
            Assert.Contains(t.Name, prompt);
            Assert.Contains("缺少前置模组", prompt);
            Assert.Contains("分配内存超过物理内存", prompt);
        }
        finally
        {
            AiPreflight.JavaListHook = null;
        }
    }

    [Fact]
    public void Check_ExplicitOldJava_IsAnError_AndNearPhysicalMemoryIsAWarning()
    {
        using var t = new NfTempInstance("pf");
        t.Version("1.20.5", javaMajor: 21);
        AiPreflight.JavaMajorHook = _ => 8;
        try
        {
            PyDict r = AiPreflight.Check(t.Inst, "1.20.5", 7000, t.PathOf("no-such-java.exe"), PyDict.Of("ram_mb", 8192L));
            var codes = Py.ListOr(r.Get("items")).Select(i => Py.S(Py.D(i).Get("code"))).ToList();
            Assert.Contains("java_too_old", codes);
            Assert.Contains("memory_near_physical", codes);
            Assert.DoesNotContain("memory_exceeds_physical", codes);
        }
        finally
        {
            AiPreflight.JavaMajorHook = null;
        }
    }

    [Fact]
    public async Task PreflightTool_IsReadOnly_AndPicksVersionWhenOmitted()
    {
        using var t = new NfTempInstance("pf");
        t.Version("1.20.1", javaMajor: 17);
        AiPreflight.JavaListHook = () => new List<object> { PyDict.Of("major", 17L, "exe", "java17") };
        NfHardwareProbe.Hook = () => PyDict.Of("ram_mb", 32768L, "gpu", "NVIDIA GeForce RTX 3060", "vram_mb", 12288L);
        try
        {
            var runner = new ToolRunner(new NfFakeApi()) { PermissionMode = "default" }; // 没接 Confirm:只读照样能跑
            var call = new ToolCall("f1", "preflight_check", PyDict.Of("instance", t.Name, "memory_mb", 4096L));
            string text = await runner.RunAsync(call);
            Assert.Equal(ToolCallStatus.COMPLETED, call.status);
            Assert.Contains("\"version\": \"1.20.1\"", text);
            Assert.DoesNotContain("java_auto_download", text);
            Assert.DoesNotContain("memory_exceeds", text);
            Assert.DoesNotContain("memory_near", text);
        }
        finally
        {
            AiPreflight.JavaListHook = null;
            NfHardwareProbe.Hook = null;
        }
    }

    [Fact]
    public void EnabledSwitch_DefaultsOn_AndPersists()
    {
        object? saved = Config.CONFIG.Get(AiPreflight.ENABLED_KEY);
        try
        {
            Config.CONFIG.data.Remove(AiPreflight.ENABLED_KEY);
            Assert.True(AiPreflight.IsEnabled());
            AiPreflight.SetEnabled(false);
            Assert.False(AiPreflight.IsEnabled());
            AiPreflight.SetEnabled(true);
            Assert.True(AiPreflight.IsEnabled());
        }
        finally
        {
            if (saved == null) Config.CONFIG.data.Remove(AiPreflight.ENABLED_KEY);
            else Config.CONFIG.Set(AiPreflight.ENABLED_KEY, saved);
        }
    }
}

public class ShaderAdvisorTests
{
    [Theory]
    [InlineData("Intel(R) UHD Graphics 620", 0L, 16384L, "low")]
    [InlineData("AMD Radeon(TM) Graphics", 512L, 16384L, "low")]
    [InlineData("NVIDIA GeForce GTX 1060 6GB", 6144L, 8192L, "mid")]
    [InlineData("NVIDIA GeForce GTX 1650", 4096L, 16384L, "mid")]
    [InlineData("NVIDIA GeForce RTX 4070", 12288L, 32768L, "high")]
    [InlineData("AMD Radeon RX 6700 XT", 0L, 16000L, "high")]
    [InlineData("NVIDIA GeForce RTX 3080", 10240L, 4096L, "low")]
    public void HardwareTier(string gpu, long vram, long ram, string expected)
    {
        Assert.Equal(expected, ShaderAdvisor.HardwareTier(PyDict.Of("gpu", gpu, "vram_mb", vram, "ram_mb", ram), out string reason));
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void ItemTier_ByNameAndResolution()
    {
        Assert.Equal("high", ShaderAdvisor.ItemTier(PyDict.Of("name", "Rethinking Voxels"), "shader"));
        Assert.Equal("low", ShaderAdvisor.ItemTier(PyDict.Of("name", "MakeUp - Ultra Fast"), "shader"));
        Assert.Equal("mid", ShaderAdvisor.ItemTier(PyDict.Of("name", "Complementary Reimagined"), "shader"));
        Assert.Equal("high", ShaderAdvisor.ItemTier(PyDict.Of("name", "Faithful 128x"), "resourcepack"));
        Assert.Equal("mid", ShaderAdvisor.ItemTier(PyDict.Of("name", "Stay True", "description", "64x textures"), "resourcepack"));
        Assert.Equal("low", ShaderAdvisor.ItemTier(PyDict.Of("name", "Faithful 32x"), "resourcepack"));
    }

    private static List<object> Rows() => new()
    {
        PyDict.Of("name", "SEUS PTGI", "slug", "seus-ptgi", "source", "curseforge"),
        PyDict.Of("name", "Complementary Reimagined", "slug", "complementary-reimagined", "source", "modrinth"),
        PyDict.Of("name", "Sildur's Vibrant Lite", "slug", "sildurs-lite", "source", "modrinth"),
        PyDict.Of("name", "BSL Shaders", "slug", "bsl-shaders", "source", "modrinth"),
    };

    [Fact]
    public void Recommend_LowEndMachine_GetsLightShadersAndHeavyAreFlagged()
    {
        PyDict r = ShaderAdvisor.Recommend(Rows(), PyDict.Of("gpu", "Intel(R) Iris(R) Xe Graphics", "ram_mb", 8192L), "shader");
        Assert.Equal("low", Py.S(Py.D(r.Get("hardware")).Get("tier")));
        var picks = Py.ListOr(r.Get("recommended")).Select(x => Py.S(Py.D(x).Get("slug"))).ToList();
        Assert.Equal(new[] { "sildurs-lite" }, picks);
        var heavy = Py.ListOr(r.Get("too_heavy")).Select(x => Py.S(x)).ToList();
        Assert.Contains("SEUS PTGI", heavy);
        Assert.Contains("BSL Shaders", heavy);
    }

    [Fact]
    public void Recommend_HighEndMachine_PutsHeavyFirstThenFallsBack()
    {
        PyDict r = ShaderAdvisor.Recommend(Rows(), PyDict.Of("gpu", "NVIDIA GeForce RTX 4080", "vram_mb", 16384L, "ram_mb", 32768L), "shader");
        var picks = Py.ListOr(r.Get("recommended")).Select(x => Py.S(Py.D(x).Get("slug"))).ToList();
        Assert.Equal("seus-ptgi", picks[0]);
        Assert.Equal(4, picks.Count);
        Assert.Empty(Py.ListOr(r.Get("too_heavy")));
    }

    [Fact]
    public async Task RecommendTool_UsesSearchAndDetectsIris()
    {
        using var t = new NfTempInstance("sh");
        t.Version("fabric-loader-0.15.11-1.20.1");
        t.FabricJar("iris-1.7.2.jar", "iris");
        var api = new NfFakeApi { Shaders = Rows() };
        NfHardwareProbe.Hook = () => PyDict.Of("gpu", "NVIDIA GeForce GTX 1660", "vram_mb", 6144L, "ram_mb", 16384L);
        try
        {
            var runner = new ToolRunner(api) { PermissionMode = "plan", Instance = t.Name }; // 只看不动档也能跑:只读
            var call = new ToolCall("r1", "recommend_shaders", PyDict.Of("instance", t.Name));
            string text = await runner.RunAsync(call);
            Assert.Equal(ToolCallStatus.COMPLETED, call.status);
            Assert.Contains(api.Calls, c => c.Method == "search_shaders");
            PyDict r = Py.D(PyJson.Loads(text));
            Assert.True(Py.Truthy(Py.D(r.Get("shader_loader")).Get("iris")));
            Assert.True(Py.Truthy(Py.D(r.Get("shader_loader")).Get("ready")));
            Assert.NotEmpty(Py.ListOr(r.Get("recommended")));
        }
        finally
        {
            NfHardwareProbe.Hook = null;
        }
    }

    [Fact]
    public void ShaderLoader_HintsWhatToInstall()
    {
        using var t = new NfTempInstance("sh");
        t.Version("1.20.1-forge-47.2.0");
        PyDict info = ShaderAdvisor.ShaderLoader(t.Inst);
        Assert.False(Py.Truthy(info.Get("ready")));
        Assert.Contains("Oculus", Py.S(info.Get("hint")));
    }
}

public class DiagnosticReportTests
{
    [Fact]
    public void Redact_MasksTokensPathsEmailsUuidsAndNames()
    {
        string user = Environment.UserName;
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string raw = string.Join("\n",
            "\"accessToken\": \"eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ4eHgifQ.c2lnbmF0dXJlMTIz\"",
            "accessToken=SECRET-abc-123 clientToken: ct-999999",
            "--username Alice --uuid 123e4567-e89b-12d3-a456-426614174000 --accessToken tok_ABCDEF123456",
            "Authorization: Bearer abcdefghijklmnop123456",
            @"C:\Users\Alice\AppData\Roaming\.minecraft\mods",
            "/home/alice/.minecraft/logs/latest.log",
            "contact: alice.smith@example.com",
            "api_key=sk-1234567890abcdef",
            home + Path.DirectorySeparatorChar + "Desktop",
            "user is " + user + " here");
        string red = DiagnosticReport.Redact(raw, new[] { "Alice" });
        foreach (string leak in new[] { "SECRET-abc-123", "ct-999999", "tok_ABCDEF123456", "abcdefghijklmnop123456", "eyJhbGci", "Alice", "alice", "example.com",
                     "123e4567", "sk-1234567890abcdef", home })
        {
            Assert.False(red.Contains(leak, StringComparison.OrdinalIgnoreCase), "泄露了「" + leak + "」:\n" + red);
        }
        if (user.Length >= 3 && !new[] { "user", "admin", "root", "test" }.Contains(user.ToLowerInvariant()))
        {
            Assert.DoesNotContain(user, red, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("<user>", red);
        Assert.Contains("***", red);
        Assert.Contains("<email>", red);
        Assert.Contains(".minecraft", red); // 路径结构保留,只抹用户名
    }

    [Fact]
    public void ConversationSummary_SkipsSystem_KeepsUserToolsAndAnswers()
    {
        var convo = new List<object>
        {
            PyDict.Of("role", "system", "content", "SYSTEM PROMPT SHOULD NOT LEAK"),
            PyDict.Of("role", "user", "content", "启动就闪退,帮我看看"),
            PyDict.Of("role", "assistant", "content", "", "tool_calls", new List<object> { PyDict.Of("id", "1", "function", PyDict.Of("name", "diagnose_launch", "arguments", "{}")) }),
            PyDict.Of("role", "tool", "content", "{\"headline\":\"缺少 Fabric API\"}"),
            PyDict.Of("role", "assistant", "content", "原因是缺 Fabric API,装上就好"),
        };
        List<string> lines = DiagnosticReport.ConversationSummary(convo);
        string all = string.Join("\n", lines);
        Assert.DoesNotContain("SYSTEM PROMPT", all);
        Assert.Contains("启动就闪退", all);
        Assert.Contains("诊断启动", all); // 工具名翻成中文标签
        Assert.Contains("缺 Fabric API", all);
    }

    private static NfTempInstance CrashedInstance()
    {
        var t = new NfTempInstance("diag");
        t.Version("fabric-loader-0.15.11-1.20.1");
        t.FabricJar("sodium-0.5.8.jar", "sodium");
        t.Write("logs/latest.log", string.Join("\n",
            "[12:00:00] [main/INFO]: Loading Minecraft 1.20.1 with Fabric Loader",
            "[12:00:01] [main/INFO]: Setting user: Alice",
            @"[12:00:01] [main/INFO]: Game dir C:\Users\Alice\AppData\Roaming\.minecraft",
            "[12:00:02] [main/INFO]: accessToken=SECRET-abc-123",
            "[12:00:03] [main/ERROR]: Incompatible mods found!",
            "java.lang.RuntimeException: Mod sodium requires fabric-api",
            "\tat net.fabricmc.loader.impl.FabricLoaderImpl.load(FabricLoaderImpl.java:100)"));
        return t;
    }

    [Fact]
    public void Export_Zip_HasReportAndLogs_AllRedacted()
    {
        using var t = CrashedInstance();
        string dest = Path.Combine(Path.GetTempPath(), "tipcl-diag-" + Guid.NewGuid().ToString("N"));
        t.Track(dest);
        var convo = new List<object> { PyDict.Of("role", "user", "content", "我是 Alice,游戏崩了,路径 C:\\Users\\Alice\\Desktop") };
        var args = PyDict.Of("dest_dir", dest, "instance", t.Name);
        string path = DiagnosticReport.ResolveOutPath(args, t.Name);
        Assert.Equal(path, DiagnosticReport.ResolveOutPath(args, t.Name)); // 同一次调用三处取到同一个路径
        PyDict r = DiagnosticReport.Export(t.Inst, convo, path, PyDict.Of("ram_mb", 16384L, "gpu", "NVIDIA GeForce RTX 3060"), new[] { "Alice" });
        Assert.True(Py.Truthy(r.Get("ok")));
        Assert.EndsWith(".zip", path);
        using ZipArchive zip = ZipFile.OpenRead(path);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("report.md", names);
        Assert.Contains("logs/latest.log", names);
        foreach (ZipArchiveEntry e in zip.Entries)
        {
            using var sr = new StreamReader(e.Open());
            string text = sr.ReadToEnd();
            Assert.DoesNotContain("SECRET-abc-123", text);
            Assert.DoesNotContain("Alice", text);
            if (e.FullName == "report.md")
            {
                Assert.Contains("排查过程摘要", text);
                Assert.Contains("游戏崩了", text);
                Assert.Contains("sodium-0.5.8.jar", text);
                Assert.Contains("RTX 3060", text);
                Assert.Contains("requires fabric-api", text); // 日志关键片段进了报告
            }
        }
        // 绝不覆盖:再导一次换新名字
        var args2 = PyDict.Of("dest_dir", dest, "instance", t.Name, "_out_path", path);
        Assert.Throws<FileExistsError>(() => DiagnosticReport.Export(t.Inst, convo, Py.S(args2.Get("_out_path"))));
    }

    [Fact]
    public async Task ExportTool_GoesThroughConfirm_WritesMarkdown_AndCheckpointPathMatches()
    {
        using var dirs = new AiServiceDirs();
        using var t = CrashedInstance();
        string dest = Path.Combine(Path.GetTempPath(), "tipcl-diag-" + Guid.NewGuid().ToString("N"));
        t.Track(dest);
        var runner = new ToolRunner(new NfFakeApi()) { PermissionMode = "default", Instance = t.Name };
        runner.ConversationProvider = () => new List<object> { PyDict.Of("role", "user", "content", "帮我导出报告") };
        List<string>? preview = null;
        runner.Confirm = (_, _, _, p) =>
        {
            preview = Preview.Flatten(p);
            return Task.FromResult((true, false));
        };
        var call = new ToolCall("e1", "export_diagnostic_report", PyDict.Of("instance", t.Name, "dest_dir", dest, "format", "md"));
        List<string> affected = Tools.AffectedPaths(call.name, call.args, t.Name); // AiAgent 在执行前就是这么算检查点的
        string text = await runner.RunAsync(call);
        Assert.Equal(ToolCallStatus.COMPLETED, call.status);
        PyDict r = Py.D(PyJson.Loads(text));
        string path = Py.S(r.Get("path"));
        Assert.Equal(affected.Single(), path);
        Assert.Contains(preview!, l => l.Contains(path));
        Assert.True(File.Exists(path));
        string md = File.ReadAllText(path);
        Assert.Contains("帮我导出报告", md);
        Assert.DoesNotContain("SECRET-abc-123", md);
    }
}

public class InstanceUpgradeTests
{
    /// <summary>假源:alpha 有新版(并多了一个前置)、beta 当前文件就兼容、gamma 目标版本没有、delta 认不出。</summary>
    private sealed class FakeSource : IUpgradeSource
    {
        public readonly List<string> Downloads = new();

        public PyDict? Identify(string jarPath) => Path.GetFileName(jarPath) switch
        {
            "alpha-1.0.jar" => PyDict.Of("source", "modrinth", "project_id", "P-alpha", "version_id", "va1", "version_number", "1.0", "deps", new List<object>()),
            "beta-2.0.jar" => PyDict.Of("source", "modrinth", "project_id", "P-beta", "version_id", "vb2", "version_number", "2.0", "deps", new List<object>()),
            "gamma-1.0.jar" => PyDict.Of("source", "curseforge", "project_id", "123", "version_id", "f1", "version_number", "1.0", "deps", new List<object>()),
            _ => null,
        };

        public PyDict? FindVersion(PyDict ident, string mc, string loader)
        {
            Assert.Equal("1.21.1", mc);
            Assert.Equal("fabric", loader);
            return Py.S(ident.Get("project_id")) switch
            {
                "P-alpha" => PyDict.Of("version_id", "va2", "version_number", "2.0", "filename", "alpha-2.0.jar", "url", "https://example.invalid/alpha", "sha1", "", "size", 0L, "deps", new List<object> { "P-depx" }),
                "P-beta" => PyDict.Of("version_id", "vb2", "version_number", "2.0", "filename", "beta-2.0.jar", "url", "https://example.invalid/beta", "sha1", "", "size", 0L, "deps", new List<object>()),
                _ => null,
            };
        }

        public List<object> Alternatives(string name, string mc, string loader, int limit)
            => new() { PyDict.Of("name", "Gamma Reborn", "slug", "gamma-reborn", "source", "modrinth") };

        public void Download(PyDict version, string destPath)
        {
            Downloads.Add(Py.S(version.Get("filename")));
            File.WriteAllText(destPath, "new:" + Py.S(version.Get("filename")));
        }
    }

    private static NfTempInstance Pack()
    {
        var t = new NfTempInstance("pack");
        t.Version("fabric-loader-0.15.11-1.20.1");
        t.FabricJar("alpha-1.0.jar", "alpha");
        t.FabricJar("beta-2.0.jar", "beta");
        t.FabricJar("gamma-1.0.jar", "gamma");
        t.FabricJar("delta.jar", "delta");
        t.Write("config/alpha.toml", "a = 1");
        t.Write("saves/World1/level.dat", "level");
        t.Write("options.txt", "fov:90");
        return t;
    }

    [Fact]
    public void Plan_ClassifiesEveryMod_AndReportsDependencyChanges()
    {
        using var t = Pack();
        PyDict plan = InstanceUpgrade.Plan(t.Inst, "1.21.1", null, new FakeSource());
        Assert.Equal("1.20.1", Py.S(plan.Get("from_mc")));
        Assert.Equal("fabric", Py.S(plan.Get("to_loader")));
        PyDict sum = Py.D(plan.Get("summary"));
        Assert.Equal(1, Py.Int(sum.Get("upgrade")));
        Assert.Equal(1, Py.Int(sum.Get("compatible")));
        Assert.Equal(1, Py.Int(sum.Get("missing")));
        Assert.Equal(1, Py.Int(sum.Get("unknown")));
        PyDict gamma = Py.D(Py.ListOr(plan.Get("mods")).First(m => Py.S(Py.D(m).Get("filename")) == "gamma-1.0.jar"));
        Assert.Equal("missing", Py.S(gamma.Get("status")));
        Assert.Equal("gamma-reborn", Py.S(Py.D(Py.ListOr(gamma.Get("alternatives"))[0]).Get("slug")));
        PyDict dep = Py.D(Py.ListOr(plan.Get("dependency_changes")).Single());
        Assert.Contains("P-depx", Py.ListOr(dep.Get("need_install")).Select(x => Py.S(x)));
        // 计划被缓存,确认卡预览能把逐个模组的变化列出来
        PyDict? pv = Preview.ChangePreview("apply_instance_upgrade", PyDict.Of("instance", t.Name, "target_version", "1.21.1"), t.Name);
        List<string> lines = Preview.Flatten(pv);
        Assert.Contains(lines, l => l.Contains("↑") && l.Contains("1.0 → 2.0"));
        Assert.Contains(lines, l => l.Contains("✗"));
        Assert.Contains(lines, l => l.Contains("P-depx"));
        Assert.Contains(lines, l => l.Contains("原实例") && l.Contains("不动"));
    }

    [Fact]
    public void Apply_CopyMode_LeavesOriginalUntouched()
    {
        using var t = Pack();
        var src = new FakeSource();
        var args = PyDict.Of("instance", t.Name, "target_version", "1.21.1");
        string newName = InstanceUpgrade.NewInstanceName(args, t.Name);
        t.Track(Instances.GetInstancePath(newName));
        var installs = new List<(string, string, string)>();
        var steps = new List<int>();
        PyDict r = InstanceUpgrade.Apply(t.Inst, args, (mc, loader, inst) =>
        {
            installs.Add((mc, loader, inst));
            return PyDict.Of("ok", true);
        }, steps.Add, src);

        Assert.True(Py.Truthy(r.Get("ok")), PyJson.Dumps(r));
        Assert.Equal(newName, Py.S(r.Get("target_instance")));
        Assert.Equal(new[] { ("1.21.1", "Fabric", newName) }, installs);
        Assert.Equal(new[] { 0, 1, 2, 3 }, steps);
        string newRoot = Instances.GetInstancePath(newName);
        Assert.True(File.Exists(Path.Combine(newRoot, "config", "alpha.toml")));
        Assert.True(File.Exists(Path.Combine(newRoot, "saves", "World1", "level.dat")));
        Assert.True(File.Exists(Path.Combine(newRoot, "options.txt")));
        var newMods = Directory.GetFiles(Path.Combine(newRoot, "mods")).Select(f => Path.GetFileName(f)!).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "alpha-2.0.jar", "beta-2.0.jar" }, newMods);
        Assert.Equal("new:alpha-2.0.jar", File.ReadAllText(Path.Combine(newRoot, "mods", "alpha-2.0.jar")));
        // 原实例一个不少
        var oldMods = Directory.GetFiles(t.PathOf("mods")).Select(f => Path.GetFileName(f)!).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "alpha-1.0.jar", "beta-2.0.jar", "delta.jar", "gamma-1.0.jar" }, oldMods);
        Assert.Equal(2, Py.ListOr(r.Get("mods_skipped")).Count);
    }

    [Fact]
    public void Apply_InPlace_ReplacesUpgradedAndDisablesMissing()
    {
        using var t = Pack();
        var args = PyDict.Of("instance", t.Name, "target_version", "1.21.1", "in_place", true);
        PyDict r = InstanceUpgrade.Apply(t.Inst, args, (_, _, _) => PyDict.Of("ok", true), null, new FakeSource());
        Assert.Equal("in_place", Py.S(r.Get("mode")));
        var mods = Directory.GetFiles(t.PathOf("mods")).Select(f => Path.GetFileName(f)!).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "alpha-2.0.jar", "beta-2.0.jar", "delta.jar.disabled", "gamma-1.0.jar.disabled" }, mods);
        // 原地模式的检查点:mods 目录 + 实例元数据
        List<string> paths = NewFeatureEffects.AffectedPaths("apply_instance_upgrade", args, t.Name);
        Assert.Contains(paths, p => p.EndsWith("mods", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyTool_ConfirmPlanCardAndCheckpointRollbackRemovesNewInstance()
    {
        using var dirs = new AiServiceDirs();
        using var t = Pack();
        var src = new FakeSource();
        InstanceUpgrade.SourceFactory = () => src;
        try
        {
            var api = new NfFakeApi();
            var plans = new List<List<(string Title, string Status)>>();
            bool? offeredAlways = null;
            var runner = new ToolRunner(api) { PermissionMode = "default", Instance = t.Name, PlanChanged = p => plans.Add(p) };
            runner.Confirm = (_, label, _, _) =>
            {
                offeredAlways = ToolRunner.AllowsAlways("apply_instance_upgrade");
                return Task.FromResult((true, true));
            };
            var call = new ToolCall("u1", "apply_instance_upgrade", PyDict.Of("instance", t.Name, "target_version", "1.21.1"));
            // AiAgent 的顺序:先按受影响路径做检查点,再执行
            List<string> affected = Tools.AffectedPaths(call.name, call.args, t.Name);
            string newRoot = affected.Single();
            t.Track(newRoot);
            Assert.False(Directory.Exists(newRoot));
            string chat = "chat-" + Guid.NewGuid().ToString("N");
            Assert.True(Py.Truthy(Checkpoint.Snapshot(chat, "t1", affected).Get("ok")));

            string text = await runner.RunAsync(call);
            Assert.Equal(ToolCallStatus.COMPLETED, call.status);
            Assert.False(offeredAlways);
            Assert.False(runner.IsAllowedAlways("apply_instance_upgrade"));
            PyDict r = Py.D(PyJson.Loads(text));
            Assert.Equal(Instances.GetInstancePath(Py.S(r.Get("target_instance"))), newRoot);
            Assert.Contains(api.Calls, c => c.Method == "install_game");
            Assert.True(Directory.Exists(newRoot));
            // 计划卡:4 步,最后一次全部完成
            Assert.Equal(4, plans.Last().Count);
            Assert.All(plans.Last(), s => Assert.Equal("completed", s.Status));
            Assert.Contains(plans, p => p.Any(s => s.Status == "in_progress"));

            // 撤回 = 新实例整个消失,原实例不动
            Assert.True(Py.Truthy(Checkpoint.Rollback(chat, "t1").Get("ok")));
            Assert.False(Directory.Exists(newRoot));
            Assert.True(File.Exists(t.PathOf("mods", "alpha-1.0.jar")));
        }
        finally
        {
            InstanceUpgrade.SourceFactory = null;
        }
    }

    [Fact]
    public async Task PlanTool_IsReadOnly_AndMissingInstanceFails()
    {
        using var t = Pack();
        InstanceUpgrade.SourceFactory = () => new FakeSource();
        try
        {
            var runner = new ToolRunner(new NfFakeApi()) { PermissionMode = "plan" };
            var ok = new ToolCall("q1", "plan_instance_upgrade", PyDict.Of("instance", t.Name, "target_version", "1.21.1"));
            string text = await runner.RunAsync(ok);
            Assert.Equal(ToolCallStatus.COMPLETED, ok.status);
            Assert.Contains("\"upgrade\": 1", text);

            var bad = new ToolCall("q2", "plan_instance_upgrade", PyDict.Of("instance", "nf-does-not-exist-" + Guid.NewGuid().ToString("N").Substring(0, 6), "target_version", "1.21.1"));
            await runner.RunAsync(bad);
            Assert.Equal(ToolCallStatus.FAILED, bad.status);
        }
        finally
        {
            InstanceUpgrade.SourceFactory = null;
        }
    }
}
