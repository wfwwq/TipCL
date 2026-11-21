// AI3-moddev:模组测试环境遗留四项的回归(全部不联网、不真启动游戏;用假进程 / 假源 / 临时 TIPCL_HOME)。
//   1. 只关自己拉起的进程:LaunchedGames 登记表 + 默认驱动 BackendLaunchDriver 在「允许多开、用户同时在玩别的实例」时
//      只关测试启动自己那个进程(模拟进程覆盖:进主菜单后关、游戏进程还没起来就超时两条路);
//   2. 任务记录落盘:cache/moddev_tasks.json(条数上限、原子写、坏文件不炸),「重启」(ReloadForTests)后
//      get_task_status 仍能查到已结束任务;重启前在跑的标 interrupted,之后旧进程里的任务再结束也不改写;
//   3. 「进入主菜单」特征行表:Vanilla / Fabric / Quilt / Forge / NeoForge 各至少一条正例、一条反例 + 版本段 + ready_pattern 覆盖;
//   4. 依赖回落:Modrinth 找不到 / 出错 ⇒ CurseForge;两边都没有如实报错;前缀只查一边;默认来源链的顺序。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

/// <summary>模拟的游戏进程(不真起进程)。</summary>
internal sealed class FakeGameProc : ITrackedGameProcess
{
    public FakeGameProc(long pid) => Pid = pid;

    public long Pid { get; }

    public volatile bool Killed;

    public int KillCalls;

    public bool HasExited => Killed;

    public void Kill()
    {
        Interlocked.Increment(ref KillCalls);
        Killed = true;
    }
}

/// <summary>
/// 模拟启动器门面:launch_game 像 LaunchCore 一样按任务 id 登记一个模拟进程;cancel_task 记下被取消的任务;
/// Row 给 HostTaskProbe 用(像 TaskStore:被取消且进程已退 ⇒ finished)。stop_game(无参,关「当前游戏」)不该被调用。
/// </summary>
internal sealed class MultiInstanceBackend : ToolRunner.IBackend
{
    public readonly List<string> Methods = new();
    public readonly List<string> Cancelled = new();
    public readonly string TaskId = "task-test-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    public bool RegisterOnLaunch = true;
    public FakeGameProc? TestProc;
    private int _probes;

    public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
    {
        lock (Methods) Methods.Add(method);
        object result;
        switch (method)
        {
            case "launch_game":
                if (RegisterOnLaunch)
                {
                    TestProc = new FakeGameProc(4242);
                    LaunchedGames.Register(TaskId, TestProc, "dev", "v1");
                }
                result = PyDict.Of("task_id", TaskId);
                break;
            case "cancel_task":
                string id = Py.S(args?.GetType().GetProperty("id")?.GetValue(args) ?? "");
                lock (Cancelled) Cancelled.Add(id);
                result = PyDict.Of("ok", true);
                break;
            case "get_instances":
                result = new List<object?>();
                break;
            default:
                result = PyDict.Of("ok", true);
                break;
        }
        return Task.FromResult(result is T t ? t : default);
    }

    public PyDict? Row(string id)
    {
        if (id != TaskId)
        {
            return null;
        }
        int n = Interlocked.Increment(ref _probes);
        bool cancelled;
        lock (Cancelled) cancelled = Cancelled.Contains(id);
        bool exited = cancelled && (TestProc == null || TestProc.HasExited);
        string log = "[main/INFO]: Loading Minecraft 1.20.1";
        if (TestProc != null && n >= 3)
        {
            log += "\n[Render thread/INFO]: Sound engine started";
        }
        return PyDict.Of("title", "启动游戏 v1", "status", exited ? "已取消" : "运行中", "finished", exited, "success", false, "progress", -1L, "log", log);
    }
}

public class LaunchedGamesTests
{
    [Fact]
    public void StopTask_OnlyKillsThatTasksProcess_AndUnknownTasksKillNothing()
    {
        string a = "task-a-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        string b = "task-b-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        var user = new FakeGameProc(1111);
        var test = new FakeGameProc(2222);
        try
        {
            LaunchedGames.Register(a, user, "daily", "1.20.1");
            LaunchedGames.Register(b, test, "[dev] x", "v1");
            Assert.Equal(2222, LaunchedGames.PidOf(b));
            Assert.True(LaunchedGames.IsAlive(b));

            Assert.Equal("not_tracked", LaunchedGames.StopTask("task-nope"));
            Assert.Equal("not_tracked", LaunchedGames.StopPid(9999));   // 不是本启动器登记的 PID:不碰
            Assert.False(user.Killed);
            Assert.False(test.Killed);

            Assert.Equal("stopped", LaunchedGames.StopTask(b));
            Assert.True(test.Killed);
            Assert.False(user.Killed);   // 多开时用户自己那一局不受影响
            Assert.Equal("already_exited", LaunchedGames.StopTask(b));
            Assert.Equal(1, test.KillCalls);

            // 注销只认登记的那个进程:同 id 的另一个进程对象收尾不能把它删掉
            LaunchedGames.Unregister(a, new FakeGameProc(3333));
            Assert.Equal(1111, LaunchedGames.PidOf(a));
            Assert.Equal("stopped", LaunchedGames.StopPid(1111));
            LaunchedGames.Unregister(a, user);
            Assert.Equal(0, LaunchedGames.PidOf(a));
        }
        finally
        {
            LaunchedGames.Unregister(a, user);
            LaunchedGames.Unregister(b, test);
        }
    }

    private static async Task<PyDict> RunTool(ToolRunner runner, PyDict args)
    {
        string text = await runner.RunAsync(new ToolCall("t" + Guid.NewGuid().ToString("N").Substring(0, 6), "run_test_launch", args));
        return Py.D(PyJson.Loads(text));
    }

    [Fact]
    public async Task DefaultDriver_MultiInstance_ReadyThenStopsOnlyItsOwnProcess()
    {
        using var env = new ModDevEnv();
        Instance inst = env.Instance("dev", true, "fabric", versionId: "v1");
        string userTask = "task-user-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        var userGame = new FakeGameProc(1111);   // 用户开着「允许多开」同时在玩的另一局
        var backend = new MultiInstanceBackend();
        LaunchedGames.Register(userTask, userGame, "daily", "1.20.1");
        try
        {
            ModDevHost.HostTaskProbe = backend.Row;   // LaunchDriverFactory 不设 ⇒ 用产品默认驱动
            var runner = new ToolRunner(backend) { PermissionMode = "yolo", Instance = inst.name };
            PyDict r = await RunTool(runner, PyDict.Of("instance", inst.name, "wait", true, "timeout_seconds", 30L));
            Assert.Equal("ready", Py.S(r.Get("outcome")));
            Assert.True(Py.Truthy(r.Get("game_stopped_by_tipcl")));
            Assert.Equal(4242L, Py.Int(r.Get("game_pid")));
            Assert.Equal("stopped", Py.S(r.Get("stop_result")));
            Assert.True(backend.TestProc!.Killed);
            Assert.False(userGame.Killed);                          // 没误关用户那一局
            Assert.DoesNotContain("stop_game", backend.Methods);    // 不再调「关当前游戏」
            Assert.Contains(backend.TaskId, backend.Cancelled);     // 启动任务本身也取消(记「已取消」而不是崩溃)
        }
        finally
        {
            LaunchedGames.Unregister(userTask, userGame);
            if (backend.TestProc != null) LaunchedGames.Unregister(backend.TaskId, backend.TestProc);
        }
    }

    [Fact]
    public async Task DefaultDriver_TimeoutBeforeTheGameProcessExists_CancelsLaunchAndKillsNothingElse()
    {
        using var env = new ModDevEnv();
        Instance inst = env.Instance("dev", true, "fabric", versionId: "v1");
        string userTask = "task-user-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        var userGame = new FakeGameProc(1111);
        var backend = new MultiInstanceBackend { RegisterOnLaunch = false };   // 还在补文件 / 下 Java,游戏进程没起来
        LaunchedGames.Register(userTask, userGame, "daily", "1.20.1");
        try
        {
            ModDevHost.HostTaskProbe = backend.Row;
            var runner = new ToolRunner(backend) { PermissionMode = "yolo", Instance = inst.name };
            PyDict r = await RunTool(runner, PyDict.Of("instance", inst.name, "wait", true, "timeout_seconds", 5L));
            Assert.Equal("timeout", Py.S(r.Get("outcome")));
            Assert.Equal("not_tracked", Py.S(r.Get("stop_result")));
            Assert.Null(r.Get("game_pid"));
            Assert.False(userGame.Killed);
            Assert.Equal(0, userGame.KillCalls);
            Assert.DoesNotContain("stop_game", backend.Methods);
            Assert.Contains(backend.TaskId, backend.Cancelled);
        }
        finally
        {
            LaunchedGames.Unregister(userTask, userGame);
        }
    }
}

/// <summary>临时数据根(TIPCL_HOME):用例里切过去,结束切回并「重启」任务表。</summary>
internal sealed class TempTipclHome : IDisposable
{
    public readonly string Dir = Path.Combine(Path.GetTempPath(), "tipcl-moddev-home-" + Guid.NewGuid().ToString("N").Substring(0, 10));
    private readonly string _prev = Utils.ROOT;

    public TempTipclHome()
    {
        Directory.CreateDirectory(Dir);
        Utils.SetRoot(Dir);
        ModDevTasks.ReloadForTests();
    }

    public void Dispose()
    {
        ModDevTasks.ReloadForTests();
        Utils.SetRoot(_prev);
        try
        {
            Directory.Delete(Dir, true);
        }
        catch (Exception) { }
    }
}

public class ModDevTaskPersistenceTests
{
    private static Task<PyDict> Done(PyDict d) => Task.FromResult(d);

    [Fact]
    public async Task FinishedTask_IsStillQueryableAfterARestart()
    {
        using var home = new TempTipclHome();
        ModDevTask t = ModDevTasks.Start("run_test_launch", "测试启动 x / v1", (task, ct) =>
        {
            task.SetStep("等待进入主菜单", 50);
            task.Log("Sound engine started");
            return Done(PyDict.Of("ok", true, "outcome", "ready", "elapsed_s", 12.5, "log_excerpt", new List<object> { "a", "b" }));
        });
        Assert.True(await ModDevTasks.WaitAsync(t, TimeSpan.FromSeconds(10)));
        Assert.Equal(Path.GetFullPath(Path.Combine(home.Dir, "cache", "moddev_tasks.json")), Path.GetFullPath(ModDevTasks.StorePath));
        Assert.True(File.Exists(ModDevTasks.StorePath));

        ModDevTasks.ReloadForTests();   // 模拟启动器重启:进程内表没了,只剩盘上的
        PyDict s = await ModDevTasks.StatusAsync(t.Id, 10, 0);
        Assert.True(Py.Truthy(s.Get("ok")), PyJson.Dumps(s));
        Assert.Equal("succeeded", Py.S(s.Get("state")));
        Assert.True(Py.Truthy(s.Get("finished")));
        Assert.True(Py.Truthy(s.Get("restored")));
        Assert.Equal("run_test_launch", Py.S(s.Get("kind")));
        PyDict result = Py.D(s.Get("result"));
        Assert.Equal("ready", Py.S(result.Get("outcome")));
        Assert.Equal(new[] { "a", "b" }, Py.ListOr(result.Get("log_excerpt")).Select(Py.S));
        Assert.Contains(Py.ListOr(s.Get("log_tail")).Select(Py.S), l => l.Contains("Sound engine started"));
        Assert.Null(ModDevTasks.RunningOf("run_test_launch"));
        Assert.False(ModDevTasks.Cancel(t.Id));
    }

    [Fact]
    public async Task RunningTask_BecomesInterruptedAfterARestart_AndTheOrphanCannotRewriteIt()
    {
        using var home = new TempTipclHome();
        var gate = new TaskCompletionSource<bool>();
        ModDevTask orphan = ModDevTasks.Start("create_test_instance", "准备测试实例 [dev] y", async (task, ct) =>
        {
            task.SetStep("安装 Minecraft 1.20.1", 5);
            await gate.Task;
            return PyDict.Of("ok", true, "message", "done");
        });
        // 起任务那一刻就已经落盘(state=running)
        PyDict onDisk = Py.D(Utils.ReadJson(ModDevTasks.StorePath));
        Assert.Contains(Py.ListOr(onDisk.Get("tasks")).Select(Py.D), r => Py.S(r.Get("id")) == orphan.Id && Py.S(r.Get("state")) == "running");

        ModDevTasks.ReloadForTests();
        PyDict s = await ModDevTasks.StatusAsync(orphan.Id, 10, 5);   // 不会傻等:读回来就是已结束
        Assert.Equal("interrupted", Py.S(s.Get("state")));
        Assert.True(Py.Truthy(s.Get("finished")));
        Assert.False(Py.Truthy(s.Get("success")));
        Assert.Contains("重启", Py.S(s.Get("error_detail")));
        Assert.Null(ModDevTasks.RunningOf("create_test_instance"));   // 不再挡住新的同类任务
        PyDict rewritten = Py.D(Utils.ReadJson(ModDevTasks.StorePath));
        Assert.Contains(Py.ListOr(rewritten.Get("tasks")).Select(Py.D), r => Py.S(r.Get("id")) == orphan.Id && Py.S(r.Get("state")) == "interrupted");

        // 上一个「进程」里的任务后来才结束:不会把记录改回 succeeded
        gate.SetResult(true);
        Assert.True(await ModDevTasks.WaitAsync(orphan, TimeSpan.FromSeconds(10)));
        ModDevTasks.ReloadForTests();
        Assert.Equal("interrupted", Py.S((await ModDevTasks.StatusAsync(orphan.Id, 0, 0)).Get("state")));
    }

    [Fact]
    public async Task Records_AreCapped_AtomicallyWritten_AndABrokenFileIsIgnored()
    {
        using var home = new TempTipclHome();
        var tasks = new List<ModDevTask>();
        for (int i = 0; i < ModDevTasks.MaxTasks + 10; i++)
        {
            int n = i;
            tasks.Add(ModDevTasks.Start("run_test_launch", "t" + n, (task, ct) => Done(PyDict.Of("ok", n % 2 == 0, "n", (long)n))));
        }
        foreach (ModDevTask t in tasks)
        {
            Assert.True(await ModDevTasks.WaitAsync(t, TimeSpan.FromSeconds(10)));
        }
        ModDevTasks.Persist();
        PyDict root = Py.D(Utils.ReadJson(ModDevTasks.StorePath));
        List<PyDict> rows = Py.ListOr(root.Get("tasks")).Select(Py.D).ToList();
        Assert.Equal(ModDevTasks.MaxTasks, rows.Count);
        Assert.Equal(tasks.Last().Id, Py.S(rows.Last().Get("id")));        // 留的是最近的
        Assert.DoesNotContain(rows, r => Py.S(r.Get("id")) == tasks[0].Id);
        string cache = Path.GetDirectoryName(ModDevTasks.StorePath)!;
        Assert.Empty(Directory.GetFiles(cache, "*.tmp"));                    // 临时文件 + 原子替换,不留残渣
        ModDevTasks.ReloadForTests();
        Assert.Equal("failed", Py.S((await ModDevTasks.StatusAsync(tasks.Last().Id, 0, 0)).Get("state")));   // 59 号 ok=false
        Assert.Equal("not_found", Py.S((await ModDevTasks.StatusAsync(tasks[0].Id, 0, 0)).Get("error")));

        // 坏文件:读不出来就当没有记录,照样能起新任务并重新写好
        File.WriteAllText(ModDevTasks.StorePath, "{ not json");
        ModDevTasks.ReloadForTests();
        Assert.Equal("not_found", Py.S((await ModDevTasks.StatusAsync(tasks.Last().Id, 0, 0)).Get("error")));
        ModDevTask fresh = ModDevTasks.Start("run_test_launch", "fresh", (task, ct) => Done(PyDict.Of("ok", true)));
        Assert.True(await ModDevTasks.WaitAsync(fresh, TimeSpan.FromSeconds(10)));
        ModDevTasks.ReloadForTests();
        Assert.Equal("succeeded", Py.S((await ModDevTasks.StatusAsync(fresh.Id, 0, 0)).Get("state")));
    }
}

public class ReadyMarkerTableTests
{
    [Theory]
    // ── Vanilla ──
    [InlineData("vanilla", "1.20.1", "[12:00:01] [Render thread/INFO]: Sound engine started", true)]
    [InlineData("vanilla", "1.8.9", "[12:00:01] [Sound Library Loader/INFO]: [STDOUT]: OpenAL initialized.", true)]
    [InlineData("vanilla", "1.20.1", "[Render thread/INFO]: Error starting SoundSystem. Turning off sounds & music", true)]   // 没声卡也会进标题画面
    [InlineData("vanilla", "1.20.1", "[main/INFO]: Setting user: Dev\n[Render thread/INFO]: Backend library: LWJGL version 3.3.1", false)]
    [InlineData("vanilla", "1.20.1", "[Render thread/INFO]: [STDOUT]: OpenAL initialized.", false)]   // 1.12.2 及以前的写法,对 1.20.1 不算
    // ── Fabric ──
    [InlineData("fabric", "1.20.1", "[main/INFO]: Loading 42 mods:\n[Render thread/INFO]: Sound engine started", true)]
    [InlineData("fabric", "1.20.1", "[main/INFO]: Loading Minecraft 1.20.1 with Fabric Loader 0.16.0\n[main/INFO]: Loading 42 mods:", false)]
    // ── Quilt ──
    [InlineData("quilt", "1.20.1", "[Render thread/INFO]: OpenAL initialized on device OpenAL Soft", true)]
    [InlineData("quilt", "1.20.1", "[main/INFO]: Loading Minecraft 1.20.1 with Quilt Loader 0.26.0\n[main/INFO]: Loading 12 mods:", false)]
    // ── Forge ──
    [InlineData("forge", "1.12.2", "[Client thread/INFO] [FML]: Forge Mod Loader has successfully loaded 5 mods", true)]
    [InlineData("forge", "1.20.1", "[Render thread/INFO] [net.minecraft.client.sounds.SoundEngine/SOUNDS]: Sound engine started", true)]
    [InlineData("forge", "1.20.1", "[main/INFO] [cpw.mods.modlauncher.Launcher/MODLAUNCHER]: ModLauncher running: args [--launchTarget, forgeclient]\n[main/INFO] [net.minecraftforge.fml.loading.ImmediateWindowHandler/]: Loading ImmediateWindowProvider fmlearlywindow", false)]
    [InlineData("forge", "1.20.1", "[Client thread/INFO] [FML]: Forge Mod Loader has successfully loaded 5 mods", false)]   // 旧 FML 的行只对 ≤1.12.2 生效
    // ── NeoForge ──
    [InlineData("neoforge", "1.21.1", "[Render thread/INFO] [com.mojang.blaze3d.audio.Library/]: OpenAL initialized on device OpenAL Soft on Speakers", true)]
    [InlineData("neoforge", "1.21.1", "[Render thread/INFO] [net.minecraft.client.sounds.SoundEngine/SOUNDS]: Sound engine started", true)]
    [InlineData("neoforge", "1.21.1", "[main/INFO] [net.neoforged.fml.loading.ImmediateWindowHandler/]: Loading ImmediateWindowProvider fmlearlywindow\n[main/INFO] [cpw.mods.modlauncher.LaunchServiceHandler/MODLAUNCHER]: Launching target 'forgeclient' with arguments", false)]
    public void EachLoader_PositiveAndNegativeLogs(string loader, string mc, string log, bool ready)
    {
        List<ReadyMarker> markers = ReadyMarkers.For(loader, mc);
        string hit = TestLaunch.FindReady(log, null, markers);
        Assert.True(ready == (hit.Length > 0), loader + " " + mc + " ⇒ " + (hit.Length > 0 ? "命中 " + hit : "没命中"));
    }

    [Fact]
    public void Table_HasSources_VersionSegments_AndIsTheSourceOfMainMenuMarkers()
    {
        Assert.NotEmpty(ReadyMarkers.Table);
        Assert.All(ReadyMarkers.Table, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Text));
            Assert.True(m.Source.Length > 20, "每条都要写来源: " + m.Text);
        });
        Assert.Contains(ReadyMarkers.Table, m => m.Inferred);
        Assert.Contains(ReadyMarkers.Table, m => !m.Inferred);
        Assert.Equal(ReadyMarkers.AllTexts(), TestLaunch.MainMenuMarkers);

        // 版本段:FML 旧行只在 forge ≤1.12.2;老 OpenAL 行只在 ≤1.12.2
        Assert.Contains(ReadyMarkers.For("forge", "1.7.10"), m => m.Text.StartsWith("Forge Mod Loader", StringComparison.Ordinal));
        Assert.DoesNotContain(ReadyMarkers.For("forge", "1.13.2"), m => m.Text.StartsWith("Forge Mod Loader", StringComparison.Ordinal));
        Assert.DoesNotContain(ReadyMarkers.For("fabric", "1.12.2"), m => m.Text.StartsWith("Forge Mod Loader", StringComparison.Ordinal));
        Assert.DoesNotContain(ReadyMarkers.For("neoforge", "1.21.1"), m => m.Text == "OpenAL initialized");
        // 新版本号(26.x)比 1.21 大
        Assert.Contains(ReadyMarkers.For("vanilla", "26.1"), m => m.Text == "OpenAL initialized on device");
        // 认不出加载器 / 版本:放宽到整张表
        Assert.Equal(ReadyMarkers.Table.Count, ReadyMarkers.For("", "").Count);
        Assert.Equal(ReadyMarkers.Table.Count, ReadyMarkers.For("", "24w14a").Count);

        Assert.Equal(new[] { 1, 20, 1 }, ReadyMarkers.ParseMc("1.20.1"));
        Assert.Equal(new[] { 1, 21 }, ReadyMarkers.ParseMc("1.21-pre1"));
        Assert.Null(ReadyMarkers.ParseMc("24w14a"));
        Assert.Null(ReadyMarkers.ParseMc(""));
    }

    [Fact]
    public void ReadyPattern_OverridesTheWholeTable()
    {
        const string log = "[Render thread/INFO]: Sound engine started\n[Render thread/INFO]: MyMod title ready";
        var custom = new Regex("MyMod title (ready|ok)");
        Assert.Equal("MyMod title ready", TestLaunch.FindReady(log, custom, ReadyMarkers.For("fabric", "1.20.1")));
        Assert.Equal("", TestLaunch.FindReady("[Render thread/INFO]: Sound engine started", custom, ReadyMarkers.For("fabric", "1.20.1")));
    }

    [Fact]
    public async Task RunTestLaunch_LegacyForgeInstance_UsesTheFmlLine()
    {
        using var env = new ModDevEnv();
        Instance inst = env.Instance("dev", true, "forge", mc: "1.12.2", versionId: "1.12.2-forge-14.23.5.2860");
        var driver = new FakeLaunchDriver();
        driver.Script.AddRange(new[]
        {
            "[main/INFO] [LaunchWrapper]: Loading tweak class name net.minecraftforge.fml.common.launcher.FMLTweaker",
            "[Client thread/INFO] [FML]: Forge Mod Loader has successfully loaded 5 mods",
            "[Client thread/INFO]: later",
        });
        ModDevHost.LaunchDriverFactory = _ => driver;
        var runner = new ToolRunner(new ModDevFakeApi()) { PermissionMode = "yolo", Instance = inst.name };
        PyDict r = Py.D(PyJson.Loads(await runner.RunAsync(new ToolCall("f1", "run_test_launch", PyDict.Of("instance", inst.name, "wait", true, "timeout_seconds", 30L)))));
        Assert.Equal("ready", Py.S(r.Get("outcome")));
        Assert.Equal("Forge Mod Loader has successfully loaded", Py.S(r.Get("marker")));
        Assert.Contains(Py.ListOr(r.Get("ready_markers")).Select(Py.S), s => s.StartsWith("Forge Mod Loader", StringComparison.Ordinal));
        Assert.Equal(1, driver.Stops);
    }
}

/// <summary>假依赖来源:available 里有的就往 mods 目录写一个 jar;没有抛「找不到」;Fail 非空时一律抛它(模拟网络错误)。</summary>
internal sealed class FakeDependencySource : IDependencySource
{
    private readonly HashSet<string> _available;
    public readonly List<string> Calls = new();
    public Exception? Fail;

    public FakeDependencySource(string name, params string[] available)
    {
        Name = name;
        _available = new HashSet<string>(available, StringComparer.OrdinalIgnoreCase);
    }

    public string Name { get; }

    public string Install(Instance inst, string slug, string mc, string loader, string modsDir, Action<string> log, CancellationToken ct)
    {
        lock (Calls) Calls.Add(slug);
        if (Fail != null)
        {
            throw Fail;
        }
        if (!_available.Contains(slug))
        {
            throw new DependencyNotFoundException(Name + " 上没有 " + slug);
        }
        string file = slug + "-" + Name + ".jar";
        File.WriteAllText(Path.Combine(PPath.Abs(modsDir), file), "x");
        return file;
    }
}

public class ModDevDependencyFallbackTests
{
    private static string FakeGame(Instance inst, string mc, string loader, string lv, Action<string> log, CancellationToken ct)
    {
        string id = "fabric-loader-0.16.0-" + mc;
        Directory.CreateDirectory(Path.Combine(inst.path, "versions", id));
        File.WriteAllText(Path.Combine(inst.path, "versions", id, id + ".json"), "{\"id\":\"" + id + "\"}");
        return id;
    }

    private static async Task<(PyDict Status, Instance Inst)> Create(ModDevEnv env, params string[] deps)
    {
        PyDict r = ModDevOps.CreateTestInstance(PyDict.Of("mc_version", "1.20.1", "loader", "fabric", "dependencies", deps.Cast<object>().ToList()));
        var inst = new Instance(Py.S(r.Get("instance")));
        env.Track(inst);
        PyDict status = await ModDevTasks.StatusAsync(Py.S(r.Get("task_id")), 80, 20);
        Assert.True(Py.Truthy(status.Get("finished")), PyJson.Dumps(status));
        return (status, inst);
    }

    private static PyDict Dep(PyDict status, string slug)
        => Py.ListOr(Py.D(status.Get("result")).Get("dependencies")).Select(Py.D).First(d => Py.S(d.Get("slug")) == slug);

    [Fact]
    public async Task NotOnModrinth_FallsBackToCurseForge_AndMissingEverywhereIsReportedHonestly()
    {
        using var env = new ModDevEnv();
        var modrinth = new FakeDependencySource("modrinth", "fabric-api");
        var curse = new FakeDependencySource("curseforge", "cf-only");
        ModDevHost.GameInstaller = FakeGame;
        ModDevHost.DependencySources = new IDependencySource[] { modrinth, curse };
        (PyDict status, Instance inst) = await Create(env, "fabric-api", "cf-only", "nowhere");

        Assert.Equal("failed", Py.S(status.Get("state")));   // 有一个两边都没有
        PyDict api = Dep(status, "fabric-api");
        Assert.True(Py.Truthy(api.Get("ok")));
        Assert.Equal("modrinth", Py.S(api.Get("source")));
        PyDict cf = Dep(status, "cf-only");
        Assert.True(Py.Truthy(cf.Get("ok")));
        Assert.Equal("curseforge", Py.S(cf.Get("source")));
        PyDict firstTry = Py.D(Py.ListOr(cf.Get("tried"))[0]);
        Assert.Equal("modrinth", Py.S(firstTry.Get("source")));
        Assert.True(Py.Truthy(firstTry.Get("not_found")));
        Assert.True(File.Exists(Path.Combine(PPath.Abs(ModDevOps.ModsDirOf(inst)), "cf-only-curseforge.jar")));
        PyDict none = Dep(status, "nowhere");
        Assert.False(Py.Truthy(none.Get("ok")));
        Assert.Contains("都找不到", Py.S(none.Get("error")));
        Assert.Contains("Modrinth", Py.S(none.Get("error")));
        Assert.Contains("CurseForge", Py.S(none.Get("error")));
        Assert.Equal(2, Py.ListOr(none.Get("tried")).Count);
        Assert.Contains("nowhere", Py.S(Py.D(status.Get("result")).Get("message")));
        Assert.DoesNotContain("fabric-api", curse.Calls);   // Modrinth 有就不去 CurseForge
        Assert.Equal(new[] { "cf-only", "nowhere" }, curse.Calls);
    }

    [Fact]
    public async Task ModrinthError_StillTriesCurseForge_AndPrefixesPinOneSource()
    {
        using var env = new ModDevEnv();
        var modrinth = new FakeDependencySource("modrinth", "sodium") { Fail = new System.Net.Http.HttpRequestException("连接超时") };
        var curse = new FakeDependencySource("curseforge", "sodium", "jei", "238222");
        ModDevHost.GameInstaller = FakeGame;
        ModDevHost.DependencySources = new IDependencySource[] { modrinth, curse };
        (PyDict status, Instance _) = await Create(env, "sodium", "curseforge:jei", "cf:238222", "modrinth:onlymr");

        PyDict sodium = Dep(status, "sodium");
        Assert.True(Py.Truthy(sodium.Get("ok")));
        Assert.Equal("curseforge", Py.S(sodium.Get("source")));
        Assert.False(Py.Truthy(Py.D(Py.ListOr(sodium.Get("tried"))[0]).Get("not_found")));   // 是出错,不是找不到
        Assert.Equal("curseforge", Py.S(Dep(status, "curseforge:jei").Get("source")));
        Assert.Equal("curseforge", Py.S(Dep(status, "cf:238222").Get("source")));
        PyDict mrOnly = Dep(status, "modrinth:onlymr");
        Assert.False(Py.Truthy(mrOnly.Get("ok")));
        Assert.DoesNotContain("CurseForge", Py.S(mrOnly.Get("error")));
        Assert.Equal(new[] { "sodium", "onlymr" }, modrinth.Calls);   // 带 curseforge: 前缀的没去 Modrinth
        Assert.DoesNotContain("onlymr", curse.Calls);
    }

    [Fact]
    public void DefaultSourceChain_IsModrinthThenCurseForge_AndDependencySpecsParse()
    {
        Assert.Equal(new[] { "modrinth", "curseforge" }, ModDevHost.DefaultDependencySources().Select(s => s.Name));
        Assert.Equal(("", "fabric-api"), ModDevHost.ParseDependency(" fabric-api "));
        Assert.Equal(("curseforge", "jei"), ModDevHost.ParseDependency("CurseForge:jei"));
        Assert.Equal(("curseforge", "238222"), ModDevHost.ParseDependency("cf:238222"));
        Assert.Equal(("modrinth", "sodium"), ModDevHost.ParseDependency("mr:sodium"));
        Assert.Equal(("", "weird:thing"), ModDevHost.ParseDependency("weird:thing"));
        ModDevHost.DependencySources = new IDependencySource[] { new FakeDependencySource("x") };
        ModDevHost.Reset();
        Assert.Null(ModDevHost.DependencySources);
    }
}
