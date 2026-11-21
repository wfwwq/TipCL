// AI3-stability:原地升级撤回按「执行前后版本目录集合的差集」删(OptiFine / Forge 安装器各种目录形态)+ SRV 核验开关。
//
// 全部不联网:升级走假 IUpgradeSource + 假门面(install_game 当场造版本目录),模型走本机假网关,DNS 走本机 UDP 假服务器。
// 检查点目录走 AiServiceDirs,InstanceUpgrade.SourceFactory 是进程级静态 —— 所以本文件的类都挂 GlobalStateCollection。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TipCL.App.Session;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

/// <summary>造各种形态的版本目录。</summary>
internal static class Ai3Versions
{
    /// <summary>
    /// 在 versions/ 下造一个版本目录:<paramref name="json"/> 为 null 时不写 json(装到一半的半成品);
    /// <paramref name="jsonName"/> 可与目录名不同(有的启动器改了目录名没改 json 名)。
    /// </summary>
    public static string Make(Instance inst, string dirName, string? json, string? jsonName = null)
    {
        string dir = Path.Combine(inst.VersionsDir(), dirName);
        Directory.CreateDirectory(dir);
        if (json != null)
        {
            File.WriteAllText(Path.Combine(dir, (jsonName ?? dirName) + ".json"), json);
        }
        File.WriteAllBytes(Path.Combine(dir, dirName + ".jar"), new byte[] { 0x50, 0x4B, 3, 4 });
        return dir;
    }

    public static string Json(string id, string? inheritsFrom = null, string? jar = null, string? extra = null)
        => "{\"id\":\"" + id + "\"" +
           (inheritsFrom != null ? ",\"inheritsFrom\":\"" + inheritsFrom + "\"" : "") +
           (jar != null ? ",\"jar\":\"" + jar + "\"" : "") +
           (extra != null ? "," + extra : "") +
           ",\"mainClass\":\"x\"}";

    /// <summary>
    /// 「升级」时会出现的几种目录(目标 1.21.1):原版、Forge 新安装器、Forge 老安装器两种命名、
    /// OptiFine 独立版、OptiFine 装在 Forge 上、改过名的整合目录(json 里什么都认不出)、装到一半没写 json 的半成品。
    /// </summary>
    public static readonly string[] InstallShapes =
    {
        "1.21.1",
        "1.21.1-forge-52.0.16",
        "1.21.1-forge1.21.1-52.0.16",
        "Forge 52.0.16",
        "1.21.1-OptiFine_HD_U_J1",
        "OptiFine-on-Forge",
        "MyPack Upgraded",
        "half-installed",
    };

    public static void MakeInstallShapes(Instance inst)
    {
        Make(inst, "1.21.1", Json("1.21.1"));
        Make(inst, "1.21.1-forge-52.0.16", Json("1.21.1-forge-52.0.16", "1.21.1"));
        Make(inst, "1.21.1-forge1.21.1-52.0.16", Json("1.21.1-forge1.21.1-52.0.16", "1.21.1"));
        Make(inst, "Forge 52.0.16", Json("Forge 52.0.16", jar: "1.21.1"));
        Make(inst, "1.21.1-OptiFine_HD_U_J1", Json("1.21.1-OptiFine_HD_U_J1", "1.21.1", "1.21.1"));
        Make(inst, "OptiFine-on-Forge", Json("OptiFine-on-Forge", "1.21.1-forge-52.0.16"));
        Make(inst, "MyPack Upgraded", Json("MyPack Upgraded"));
        Make(inst, "half-installed", null);
    }
}

[Collection(GlobalStateCollection.Name)]
public class UpgradeCheckpointDiffTests
{
    private static PyDict Args(Instance inst) => PyDict.Of("instance", inst.name, "target_version", "1.21.1", "in_place", true);

    private static string Chat() => "ai3up-" + Guid.NewGuid().ToString("N").Substring(0, 8);

    private static bool Has(Instance inst, string name) => Directory.Exists(Path.Combine(inst.VersionsDir(), name));

    [Fact]
    public void CapturedDiff_RemovesEveryDirCreatedDuringTheTool_WhateverItsShape_AndNothingCreatedAfter()
    {
        using var dirs = new AiServiceDirs();
        using var t = new NfTempInstance("ai3up");
        Ai3Versions.Make(t.Inst, "1.20.1", Ai3Versions.Json("1.20.1"));
        string chat = Chat();
        PyDict args = Args(t.Inst);
        Assert.Equal(1, UpgradeCheckpoint.SnapshotFor(chat, "t1", "apply_instance_upgrade", args, t.Name));

        Ai3Versions.MakeInstallShapes(t.Inst); // 工具执行期间装出来的
        Assert.Equal(1, UpgradeCheckpoint.CaptureAfter(chat, "t1", "apply_instance_upgrade", args, t.Name, "{\"ok\": true, \"install_game\": {\"ok\": true}}"));

        // 工具执行完之后用户自己又装的:即使属于目标版本,也不是这次升级装的
        Ai3Versions.Make(t.Inst, "fabric-loader-0.16.5-1.21.1", Ai3Versions.Json("fabric-loader-0.16.5-1.21.1", "1.21.1"));
        Ai3Versions.Make(t.Inst, "user-own-1.19.2", Ai3Versions.Json("user-own-1.19.2", "1.19.2"));

        PyDict rb = Checkpoint.Rollback(chat, "t1");
        Assert.True(Py.Truthy(rb.Get("ok")), PyJson.Dumps(rb));
        foreach (string name in Ai3Versions.InstallShapes)
        {
            Assert.False(Has(t.Inst, name), "执行期间新建的「" + name + "」应被撤回删掉");
        }
        Assert.True(Has(t.Inst, "1.20.1"), "原来就有的版本不能删");
        Assert.True(Has(t.Inst, "fabric-loader-0.16.5-1.21.1"), "执行完之后才出现的目录不是这次装的");
        Assert.True(Has(t.Inst, "user-own-1.19.2"));
    }

    [Fact]
    public void CapturedDiff_IsWhatTheJournalRecords()
    {
        using var dirs = new AiServiceDirs();
        using var t = new NfTempInstance("ai3up");
        Ai3Versions.Make(t.Inst, "1.20.1", Ai3Versions.Json("1.20.1"));
        string chat = Chat();
        string versions = t.Inst.VersionsDir();
        Assert.True(Py.Truthy(Checkpoint.SnapshotNewChildren(chat, "t1", versions, "1.21.1").Get("ok")));
        Ai3Versions.Make(t.Inst, "1.21.1-OptiFine_HD_U_J1", Ai3Versions.Json("1.21.1-OptiFine_HD_U_J1", "1.21.1"));
        Ai3Versions.Make(t.Inst, "MyPack Upgraded", Ai3Versions.Json("MyPack Upgraded"));
        PyDict cap = Checkpoint.CaptureNewChildren(chat, "t1", versions);
        Assert.True(Py.Truthy(cap.Get("ok")), PyJson.Dumps(cap));
        Assert.Equal(new[] { "1.21.1-OptiFine_HD_U_J1", "MyPack Upgraded" }, Py.ListOr(cap.Get("created")).Select(x => Py.S(x)).OrderBy(x => x, StringComparer.Ordinal));
        // 同一条名单只补记一次;别的轮次 / 别的父目录找不到名单
        Assert.False(Py.Truthy(Checkpoint.CaptureNewChildren(chat, "t1", versions).Get("ok")));
        Assert.False(Py.Truthy(Checkpoint.CaptureNewChildren(chat, "t2", versions).Get("ok")));
        Assert.False(Py.Truthy(Checkpoint.CaptureNewChildren(chat, "t1", t.PathOf("mods")).Get("ok")));
    }

    [Fact]
    public void WithoutCapture_FallsBackToBelongsToTarget_WhichNowKnowsOptiFineAndForgeShapes()
    {
        using var dirs = new AiServiceDirs();
        using var t = new NfTempInstance("ai3up");
        Ai3Versions.Make(t.Inst, "1.20.1", Ai3Versions.Json("1.20.1"));
        Ai3Versions.Make(t.Inst, "forge-52", Ai3Versions.Json("forge-52", "1.21.1")); // 升级前就有的 Forge
        string chat = Chat();
        Assert.Equal(1, UpgradeCheckpoint.SnapshotFor(chat, "t1", "apply_instance_upgrade", Args(t.Inst), t.Name));
        // 进程在执行中被杀:没有补记 created。新出现的目录里:
        string[] belongs =
        {
            "1.21.1",                    // 名字就是目标版本
            "1.21.1-forge-52.0.16",      // Forge 新安装器(inheritsFrom)
            "Forge 52.0.16",             // 很老的 Forge:只有 jar 字段
            "OptiFine 独立",             // OptiFine 合并成独立 json,只有 jar 字段
            "OptiFine-on-Forge",         // OptiFine 装在已有的 Forge 上:inheritsFrom 链 forge-52 → 1.21.1
            "PCL 合并",                  // clientVersion
            "HMCL 合并",                 // patches[game].version
            "renamed-dir",               // 目录名改了、json 名没改
            "1.21.1-OptiFine_HD_U_J1",   // 名字里带完整版本号(json 都没写完)
        };
        Ai3Versions.Make(t.Inst, "1.21.1", Ai3Versions.Json("1.21.1"));
        Ai3Versions.Make(t.Inst, "1.21.1-forge-52.0.16", Ai3Versions.Json("1.21.1-forge-52.0.16", "1.21.1"));
        Ai3Versions.Make(t.Inst, "Forge 52.0.16", Ai3Versions.Json("Forge 52.0.16", jar: "1.21.1"));
        Ai3Versions.Make(t.Inst, "OptiFine 独立", Ai3Versions.Json("OptiFine 独立", jar: "1.21.1"));
        Ai3Versions.Make(t.Inst, "OptiFine-on-Forge", Ai3Versions.Json("OptiFine-on-Forge", "forge-52"));
        Ai3Versions.Make(t.Inst, "PCL 合并", Ai3Versions.Json("PCL 合并", extra: "\"clientVersion\":\"1.21.1\""));
        Ai3Versions.Make(t.Inst, "HMCL 合并", Ai3Versions.Json("HMCL 合并", extra: "\"patches\":[{\"id\":\"game\",\"version\":\"1.21.1\"},{\"id\":\"forge\",\"version\":\"52.0.16\"}]"));
        Ai3Versions.Make(t.Inst, "renamed-dir", Ai3Versions.Json("1.21.1-forge-52.0.16", "1.21.1"), jsonName: "1.21.1-forge-52.0.16");
        Ai3Versions.Make(t.Inst, "1.21.1-OptiFine_HD_U_J1", null);
        // 不属于目标版本的新目录:
        string[] unrelated = { "user-own-1.19.2", "1.21.10", "mystery" };
        Ai3Versions.Make(t.Inst, "user-own-1.19.2", Ai3Versions.Json("user-own-1.19.2", "1.19.2"));
        Ai3Versions.Make(t.Inst, "1.21.10", Ai3Versions.Json("1.21.10"));
        Ai3Versions.Make(t.Inst, "mystery", null);

        PyDict rb = Checkpoint.Rollback(chat, "t1");
        Assert.True(Py.Truthy(rb.Get("ok")), PyJson.Dumps(rb));
        foreach (string name in belongs)
        {
            Assert.False(Has(t.Inst, name), "「" + name + "」属于目标版本,应被删掉");
        }
        foreach (string name in unrelated.Append("1.20.1").Append("forge-52"))
        {
            Assert.True(Has(t.Inst, name), "「" + name + "」不该删");
        }
    }

    [Fact]
    public void BackgroundInstall_AlsoRemovesTargetDirsThatAppearAfterTheTool()
    {
        using var dirs = new AiServiceDirs();
        using var t = new NfTempInstance("ai3up");
        string chat = Chat();
        PyDict args = Args(t.Inst);
        UpgradeCheckpoint.SnapshotFor(chat, "t1", "apply_instance_upgrade", args, t.Name);
        Ai3Versions.Make(t.Inst, "MyPack Upgraded", Ai3Versions.Json("MyPack Upgraded"));
        // install_game 回了 task_id:版本目录要等后台任务跑完才出现
        UpgradeCheckpoint.CaptureAfter(chat, "t1", "apply_instance_upgrade", args, t.Name, "{\"ok\": true, \"install_game\": {\"ok\": true, \"task_id\": \"task-42\"}}");
        Ai3Versions.Make(t.Inst, "1.21.1", Ai3Versions.Json("1.21.1"));
        Ai3Versions.Make(t.Inst, "1.21.1-OptiFine_HD_U_J1", Ai3Versions.Json("1.21.1-OptiFine_HD_U_J1", "1.21.1"));
        Ai3Versions.Make(t.Inst, "user-own-1.19.2", Ai3Versions.Json("user-own-1.19.2", "1.19.2"));

        Assert.True(Py.Truthy(Checkpoint.Rollback(chat, "t1").Get("ok")));
        Assert.False(Has(t.Inst, "MyPack Upgraded"));
        Assert.False(Has(t.Inst, "1.21.1"));
        Assert.False(Has(t.Inst, "1.21.1-OptiFine_HD_U_J1"));
        Assert.True(Has(t.Inst, "user-own-1.19.2"));
    }

    [Fact]
    public void Helpers_VersionToken_AndBackgroundDetection()
    {
        Assert.True(Checkpoint.HasVersionToken("1.21.1-OptiFine_HD_U_J1", "1.21.1"));
        Assert.True(Checkpoint.HasVersionToken("OptiFine 1.21.1", "1.21.1"));
        Assert.True(Checkpoint.HasVersionToken("fabric-loader-0.16.5-1.21.1", "1.21.1"));
        Assert.False(Checkpoint.HasVersionToken("1.21.10", "1.21.1"));
        Assert.False(Checkpoint.HasVersionToken("1.21.1.2", "1.21.1"));
        Assert.False(Checkpoint.HasVersionToken("11.21.1", "1.21.1"));
        Assert.False(Checkpoint.HasVersionToken("1.21.1", "1.21"));
        Assert.True(Checkpoint.HasVersionToken("1.21-OptiFine", "1.21"));
        Assert.False(UpgradeCheckpoint.InstallWentBackground(null));
        Assert.False(UpgradeCheckpoint.InstallWentBackground("not json"));
        Assert.False(UpgradeCheckpoint.InstallWentBackground("{\"install_game\": {\"ok\": true}}"));
        Assert.True(UpgradeCheckpoint.InstallWentBackground("{\"install_game\": {\"ok\": true, \"task_id\": \"x\"}}"));
        // 复制模式 / 别的工具:不补记
        Assert.Equal(0, UpgradeCheckpoint.CaptureAfter("c", "t", "install_mod", PyDict.Of("instance", "x"), "x", "{}"));
    }
}

/// <summary>端到端:AiAgent 真跑一轮 apply_instance_upgrade(假网关 + 假门面),撤回删掉执行期间装出来的各种目录。</summary>
[Collection(GlobalStateCollection.Name)]
public class UpgradeCheckpointAgentTests : IDisposable
{
    private static readonly string[] Keys = { "ai_mode", "ai_base_url", "ai_model", "ai_api_key", "ai_permission_mode", "ai_permission_rules" };
    private readonly Dictionary<string, object?> _before = new();

    public UpgradeCheckpointAgentTests()
    {
        foreach (string k in Keys)
        {
            _before[k] = Config.CONFIG.Get(k);
        }
    }

    public void Dispose()
    {
        foreach (string k in Keys)
        {
            object? v = _before[k];
            Config.CONFIG.Set(k, v ?? (k == "ai_permission_rules" ? new List<object>() : ""));
        }
        InstanceUpgrade.SourceFactory = null;
    }

    private sealed class EmptySource : IUpgradeSource
    {
        public PyDict? Identify(string jarPath) => null;

        public PyDict? FindVersion(PyDict ident, string mcVersion, string loader) => null;

        public List<object> Alternatives(string name, string mcVersion, string loader, int limit) => new();

        public void Download(PyDict version, string destPath) => throw new InvalidOperationException("no downloads in this test");
    }

    /// <summary>假门面:install_game 当场在实例 versions/ 里造出 OptiFine / Forge 各种形态的目录。</summary>
    private sealed class InstallingApi : ToolRunner.IBackend
    {
        private readonly Instance _inst;
        public int Installs;

        public InstallingApi(Instance inst) => _inst = inst;

        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
        {
            object result = new List<object>();
            if (method == "install_game")
            {
                Interlocked.Increment(ref Installs);
                Ai3Versions.MakeInstallShapes(_inst);
                result = PyDict.Of("ok", true);
            }
            if (result is T typed)
            {
                return Task.FromResult<T?>(typed);
            }
            object? converted = PyJson.Loads(PyJson.Dumps(result));
            return Task.FromResult(converted is T ok ? ok : default);
        }
    }

    private static string Sse(string json) => "data: " + json + "\n";

    [Fact]
    public async Task AgentRun_ThenRewind_RemovesOptiFineAndForgeDirsInstalledByTheUpgrade()
    {
        using var dirs = new AiServiceDirs();
        using var t = new NfTempInstance("ai3e2e");
        Ai3Versions.Make(t.Inst, "1.20.1", Ai3Versions.Json("1.20.1"));
        InstanceUpgrade.SourceFactory = () => new EmptySource();
        string argsJson = "{\\\"instance\\\":\\\"" + t.Name + "\\\",\\\"target_version\\\":\\\"1.21.1\\\",\\\"in_place\\\":true}";
        string round1 =
            Sse("{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"u1\",\"type\":\"function\",\"function\":{\"name\":\"apply_instance_upgrade\",\"arguments\":\"" + argsJson + "\"}}]}}]}") +
            Sse("{\"choices\":[{\"delta\":{},\"finish_reason\":\"tool_calls\"}]}") +
            "data: [DONE]\n";
        string round2 =
            Sse("{\"choices\":[{\"delta\":{\"content\":\"done\"}}]}") +
            Sse("{\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}") +
            "data: [DONE]\n";
        using var server = new SequencedSseServer(round1, round2);
        Config.CONFIG.Set("ai_mode", "custom");
        Config.CONFIG.Set("ai_base_url", server.BaseUrl);
        Config.CONFIG.Set("ai_model", "ai3-upgrade-model");
        Config.CONFIG.Set("ai_api_key", "test-key");
        Config.CONFIG.Set("ai_permission_mode", "default");
        Config.CONFIG.Set("ai_permission_rules", new List<object>());

        var api = new InstallingApi(t.Inst);
        var runner = new ToolRunner(api) { PermissionMode = "default", Instance = t.Name };
        runner.Confirm = (_, _, _, _) => Task.FromResult((true, false));
        var agent = new AiAgent(runner, _ => { })
        {
            ChatId = "ai3e2e-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            MaxRounds = 3,
        };
        await agent.RunAsync(new List<object> { PyDict.Of("role", "user", "content", "帮我把这个实例原地升级到 1.21.1") }, t.Name);

        Assert.Equal(1, api.Installs);
        foreach (string name in Ai3Versions.InstallShapes)
        {
            Assert.True(Directory.Exists(Path.Combine(t.Inst.VersionsDir(), name)), "install_game 应已造出 " + name);
        }
        // 之后用户自己装的(属于目标版本也不算这次的)
        Ai3Versions.Make(t.Inst, "fabric-loader-0.16.5-1.21.1", Ai3Versions.Json("fabric-loader-0.16.5-1.21.1", "1.21.1"));

        PyDict rb = Checkpoint.Rollback(agent.ChatId, agent.TurnId);
        Assert.True(Py.Truthy(rb.Get("ok")), PyJson.Dumps(rb));
        foreach (string name in Ai3Versions.InstallShapes)
        {
            Assert.False(Directory.Exists(Path.Combine(t.Inst.VersionsDir(), name)), "撤回后「" + name + "」应被删掉");
        }
        Assert.True(Directory.Exists(Path.Combine(t.Inst.VersionsDir(), "1.20.1")));
        Assert.True(Directory.Exists(Path.Combine(t.Inst.VersionsDir(), "fabric-loader-0.16.5-1.21.1")));
    }
}

/// <summary>`--srv-check` 的参数与输出(只对本机假 DNS;真网络核验见 progress/AI3-stability.md)。</summary>
public class SrvCheckCliTests
{
    [Fact]
    public void Parse_DefaultsAndNames()
    {
        var (names, resolvers, timeout, error) = SrvCheckCli.Parse(new[] { "--srv-check" });
        Assert.Equal("", error);
        Assert.Empty(resolvers);
        Assert.Equal(3000, timeout);
        Assert.Contains("_minecraft._tcp.hypixel.net", names);
        Assert.Contains(SrvCheckCli.DefaultGenericName, names);

        (names, resolvers, timeout, error) = SrvCheckCli.Parse(new[] { "--srv-check", "play.example.test.", "_sip._tcp.example.test", "--dns", "8.8.8.8", "--dns", "127.0.0.1:5353", "--timeout", "800" });
        Assert.Equal("", error);
        Assert.Equal(new[] { "_minecraft._tcp.play.example.test", "_sip._tcp.example.test" }, names);
        Assert.Equal(new[] { "8.8.8.8:53", "127.0.0.1:5353" }, resolvers.Select(r => r.ToString()));
        Assert.Equal(800, timeout);

        Assert.NotEqual("", SrvCheckCli.Parse(new[] { "--srv-check", "--dns", "nope" }).Error);
        Assert.NotEqual("", SrvCheckCli.Parse(new[] { "--srv-check", "--timeout", "0" }).Error);
        Assert.NotEqual("", SrvCheckCli.Parse(new[] { "--srv-check", "--bogus" }).Error);
        Assert.Equal(SrvCheckCli.BadArgs, SrvCheckCli.Run(new[] { "--srv-check", "--dns" }, new StringWriter()));
    }

    [Fact]
    public void Run_AgainstLocalFakeDns_ReportsRecordsAndStatuses()
    {
        using var dns = new FakeDns((id, name) => name switch
        {
            "_minecraft._tcp.play.example.test" => DnsSrvTests.SrvResponse(id, name, 0, (10, 5, 25570, "mc.example.test", false)),
            "_minecraft._tcp.gone.example.test" => DnsSrvTests.SrvResponse(id, name, 3),
            _ => null, // 不回 = 超时
        });
        var output = new StringWriter();
        int code = SrvCheckCli.Run(new[] { "--srv-check", "--dns", dns.EndPoint.ToString(), "--timeout", "600", "play.example.test", "gone.example.test", "silent.example.test" }, output);
        string text = output.ToString();
        Assert.Equal(0, code);
        Assert.Contains("_minecraft._tcp.play.example.test -> ok 10 5 25570 mc.example.test", text);
        Assert.Contains("_minecraft._tcp.gone.example.test -> rcode=3", text);
        Assert.Contains("_minecraft._tcp.silent.example.test -> timeout", text);
        Assert.Contains("queries=3 answered=2 with_records=1", text);
        Assert.DoesNotContain("[resolve]", text); // 显式 --dns 时不走系统 DNS

        using var mute = new FakeDns((_, _) => null);
        Assert.Equal(SrvCheckCli.NoAnswer, SrvCheckCli.Run(new[] { "--srv-check", "--dns", mute.EndPoint.ToString(), "--timeout", "200", "x.example.test" }, new StringWriter()));
    }
}
