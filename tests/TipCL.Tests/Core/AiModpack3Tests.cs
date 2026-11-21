// AI3-content:整合包遗留补齐 ——
//   4. plan_modpack 候选加 CurseForge:两边按(加权)下载量合并、同一模组去重,Modrinth 有对应版本优先 Modrinth,
//      没有才用 CurseForge 的文件;连不上 CurseForge 时照常只用 Modrinth 并在结果里注明(假源,不联网);
//   5. install_mods_batch 改后台任务:返回 task_id、逐项报进度(第 n/N 个、成功 / 失败)、部分失败,整批仍只确认一次、每项逐一列出。
// 静态注入点(ModpackTools.InstallerHook)在 Dispose 里复原;本程序集已全局关掉测试并行。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TipCL.App.Common;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

/// <summary>带 CurseForge 的假源(只读字典)。</summary>
internal sealed class Mp3FakeSource : IModpackSource
{
    public readonly Dictionary<string, List<object>> MrSearch = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, List<object>> CfSearch = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, PyDict> MrVersion = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, PyDict> CfFile = new(StringComparer.Ordinal);
    public readonly Dictionary<string, PyDict> MrProjects = new(StringComparer.Ordinal);
    public readonly Dictionary<string, PyDict> CfMods = new(StringComparer.Ordinal);
    public bool FailCf;
    public bool CfUnsupported;
    public int CfSearchCalls;

    public PyDict? ModrinthVersionByHash(string sha1) => null;

    public Dictionary<long, PyDict> CurseForgeMatches(IList<long> fingerprints) => new();

    public Dictionary<string, PyDict> CurseForgeMods(IList<string> modIds)
        => modIds.Where(CfMods.ContainsKey).ToDictionary(id => id, id => CfMods[id], StringComparer.Ordinal);

    public List<object> SearchModrinth(string query, string mcVersion, string loader, string? category, int limit)
        => MrSearch.TryGetValue(category != null ? "cat:" + category : "q:" + query, out List<object>? h) ? h.Take(limit).ToList() : new List<object>();

    public PyDict? ModrinthLatestVersion(string project, string mcVersion, string loader)
        => MrVersion.TryGetValue(project, out PyDict? v) ? v : null;

    public Dictionary<string, PyDict> ModrinthProjects(IList<string> ids)
        => ids.Where(MrProjects.ContainsKey).ToDictionary(id => id, id => MrProjects[id], StringComparer.Ordinal);

    public List<object> SearchCurseForge(string query, string mcVersion, string loader, string? category, int limit)
    {
        Interlocked.Increment(ref CfSearchCalls);
        if (CfUnsupported) throw new NotSupportedException("no cf");
        if (FailCf) throw new IOException("curseforge unreachable");
        return CfSearch.TryGetValue(category != null ? "cat:" + category : "q:" + query, out List<object>? h) ? h.Take(limit).ToList() : new List<object>();
    }

    public PyDict? CurseForgeLatestFile(string modId, string mcVersion, string loader)
    {
        if (CfUnsupported) throw new NotSupportedException("no cf");
        return CfFile.TryGetValue(modId, out PyDict? f) ? f : null;
    }

    public static PyDict MrHit(string slug, string title, long downloads)
        => PyDict.Of("project_id", "P_" + slug, "slug", slug, "title", title, "downloads", downloads, "categories", new List<object> { "technology" });

    public static PyDict CfHit(string id, string slug, string title, long downloads, params string[] cats)
        => PyDict.Of("source", "curseforge", "id", id, "slug", slug, "title", title, "downloads", downloads, "summary", title + " summary",
            "cf_categories", (cats.Length > 0 ? cats : new[] { "technology" }).Cast<object>().ToList());

    public static PyDict MrVer(string slug) => PyDict.Of("id", "V_" + slug, "version_number", "1.0", "version_type", "release",
        "files", new List<object> { PyDict.Of("filename", slug + ".jar", "primary", true, "size", 10L) }, "dependencies", new List<object>());

    public static PyDict CfF(string fileId, string name, params (string Id, string Type)[] deps) => PyDict.Of(
        "file_id", fileId, "filename", name + ".jar", "size", 5L, "display_name", name + " 1.0", "release_type", 1L,
        "dependencies", deps.Select(d => (object)PyDict.Of("mod_id", d.Id, "type", d.Type)).ToList());
}

public class AiModpack3Tests : IDisposable
{
    public void Dispose()
    {
        ModpackTools.InstallerHook = null;
        ModpackSources.Factory = null;
    }

    private static List<PyDict> Cands(PyDict plan) => Py.ListOr(plan.Get("candidates")).Select(x => Py.D(x)).ToList();

    // ───────────────────────────── 4. plan_modpack + CurseForge ─────────────────────────────

    private static Mp3FakeSource MergeSource()
    {
        var s = new Mp3FakeSource();
        s.MrSearch["cat:technology"] = new List<object>
        {
            Mp3FakeSource.MrHit("techa", "Tech A", 1000),
            Mp3FakeSource.MrHit("jei", "Just Enough Items", 500),          // Modrinth 上也有,但没有 1.20.1 + fabric 的版本
        };
        s.CfSearch["cat:technology"] = new List<object>
        {
            Mp3FakeSource.CfHit("101", "jei", "Just Enough Items (JEI)", 100000),
            Mp3FakeSource.CfHit("102", "create", "Create", 50000),
            Mp3FakeSource.CfHit("104", "some-lib", "Some Lib", 40000, "api and library"),   // 库不当候选
            Mp3FakeSource.CfHit("106", "nofile", "No File", 20000),                        // 没有对应文件 → dropped
            Mp3FakeSource.CfHit("105", "techa", "Tech A", 10),                             // 与 Modrinth 的 Tech A 是同一个
        };
        s.MrVersion["techa"] = Mp3FakeSource.MrVer("techa");
        s.CfFile["101"] = Mp3FakeSource.CfF("201", "jei");
        s.CfFile["102"] = Mp3FakeSource.CfF("202", "create", ("103", "required"));
        s.CfMods["103"] = PyDict.Of("name", "Flywheel", "slug", "flywheel");
        return s;
    }

    [Fact]
    public void Planner_MergesCurseForge_ByDownloads_DedupesSameMod_PrefersModrinthVersion()
    {
        Mp3FakeSource src = MergeSource();
        PyDict plan = ModpackPlanner.Plan("1.20.1", "fabric", "科技", "small", null, src);
        Assert.True(Py.Truthy(plan.Get("ok")), PyJson.Dumps(plan));
        List<PyDict> c = Cands(plan);
        // 合并顺序按加权下载量:JEI(CF 10 万)> Create(CF 5 万)> Tech A(Modrinth 1000×5);No File 掉了,库不在
        Assert.Equal(new[] { "jei", "create", "techa" }, c.Select(x => Py.S(x.Get("slug"))).ToArray());
        Assert.Equal(c.Count, c.Select(x => Py.S(x.Get("slug"))).Distinct().Count());   // 同一模组只出现一次

        PyDict jei = c[0];
        Assert.Equal("curseforge", Py.S(jei.Get("source")));       // Modrinth 没有对应版本 → 用 CurseForge 的文件
        Assert.Equal("101", Py.S(jei.Get("id")));
        Assert.Equal("201", Py.S(jei.Get("file_id")));
        Assert.Contains("Modrinth 上也有", Py.S(jei.Get("reason")));
        Assert.Contains("install_mods_batch", Py.S(jei.Get("note")));

        PyDict create = c[1];
        Assert.Equal("curseforge", Py.S(create.Get("source")));
        Assert.StartsWith("CurseForge", Py.S(create.Get("reason")));
        PyDict dep = Py.D(Assert.Single(Py.ListOr(create.Get("dependencies"))));
        Assert.Equal("Flywheel", Py.S(dep.Get("title")));
        Assert.Equal("curseforge", Py.S(dep.Get("source")));
        Assert.False(Py.Truthy(dep.Get("in_list")));
        PyDict extra = Py.D(Assert.Single(Py.ListOr(plan.Get("extra_dependencies"))));
        Assert.Equal("103", Py.S(extra.Get("project_id")));
        Assert.Equal("curseforge", Py.S(extra.Get("source")));

        PyDict techa = c[2];
        Assert.Equal("modrinth", Py.S(techa.Get("source")));       // 两边都有、Modrinth 有版本 → Modrinth
        Assert.Equal("105", Py.S(Py.D(techa.Get("also_on_curseforge")).Get("id")));

        PyDict dropped = Py.D(Assert.Single(Py.ListOr(plan.Get("dropped"))));
        Assert.Equal("nofile", Py.S(dropped.Get("slug")));
        Assert.Equal("curseforge", Py.S(dropped.Get("source")));
        Assert.Equal("ok", Py.S(Py.D(plan.Get("curseforge")).Get("status")));
        Assert.Equal(new[] { "curseforge", "modrinth" }, Py.ListOr(plan.Get("sources")).Select(x => Py.S(x)).OrderBy(x => x).ToArray());
        Assert.Empty(Py.ListOr(plan.Get("notes")));
    }

    [Fact]
    public void Planner_CurseForgeUnreachable_FallsBackToModrinthOnly_AndSaysSo()
    {
        Mp3FakeSource src = MergeSource();
        src.FailCf = true;
        src.MrSearch["cat:magic"] = new List<object> { Mp3FakeSource.MrHit("wand", "Wand", 300) };
        src.MrVersion["wand"] = Mp3FakeSource.MrVer("wand");
        PyDict plan = ModpackPlanner.Plan("1.20.1", "fabric", "科技 + 魔法", "small", null, src);
        Assert.True(Py.Truthy(plan.Get("ok")), PyJson.Dumps(plan));
        Assert.All(Cands(plan), x => Assert.Equal("modrinth", Py.S(x.Get("source"))));
        Assert.Equal(new[] { "techa", "wand" }, Cands(plan).Select(x => Py.S(x.Get("slug"))).ToArray());
        PyDict cf = Py.D(plan.Get("curseforge"));
        Assert.Equal("unavailable", Py.S(cf.Get("status")));
        Assert.Contains("unreachable", Py.S(cf.Get("error")));
        Assert.Contains(Py.ListOr(plan.Get("notes")), n => Py.S(n).Contains("连不上 CurseForge"));
        Assert.Contains(Py.ListOr(plan.Get("search_errors")), e => Py.S(Py.D(e).Get("source")) == "curseforge");
        Assert.Equal(1, src.CfSearchCalls);   // 第一次连不上就不再试(每次都要等超时)
    }

    [Fact]
    public void Planner_UnsupportedSource_OrUnmappedCategory_StillWorks()
    {
        Mp3FakeSource src = MergeSource();
        src.CfUnsupported = true;
        PyDict plan = ModpackPlanner.Plan("1.20.1", "fabric", "科技", "small", null, src);
        Assert.True(Py.Truthy(plan.Get("ok")));
        Assert.Equal("unsupported", Py.S(Py.D(plan.Get("curseforge")).Get("status")));
        Assert.Equal(new[] { "techa" }, Cands(plan).Select(x => Py.S(x.Get("slug"))).ToArray());   // Modrinth 的 jei 没版本,CF 又不可用 → dropped
        Assert.NotEmpty(Py.ListOr(plan.Get("notes")));

        // 「装饰」在 CurseForge 没有对应分类:那一组只搜 Modrinth,结果里写明
        var deco = new Mp3FakeSource();
        deco.MrSearch["cat:decoration"] = new List<object> { Mp3FakeSource.MrHit("chairs", "Chairs", 10) };
        deco.MrVersion["chairs"] = Mp3FakeSource.MrVer("chairs");
        PyDict p2 = ModpackPlanner.Plan("1.20.1", "fabric", "装饰", "small", null, deco);
        Assert.Equal(0, deco.CfSearchCalls);
        Assert.Equal("not_searched", Py.S(Py.D(p2.Get("curseforge")).Get("status")));
        Assert.Equal("chairs", Py.S(Cands(p2).Single().Get("slug")));

        // 关键词组 CurseForge 也搜(按原词)
        var kw = new Mp3FakeSource();
        kw.CfSearch["q:create"] = new List<object> { Mp3FakeSource.CfHit("102", "create", "Create", 50000) };
        kw.CfFile["102"] = Mp3FakeSource.CfF("202", "create");
        PyDict p3 = ModpackPlanner.Plan("1.20.1", "fabric", "装饰", "small", new List<string> { "create" }, kw);
        Assert.Contains(Cands(p3), x => Py.S(x.Get("slug")) == "create" && Py.S(x.Get("keyword")) == "create");
    }

    [Fact]
    public void Planner_CurseForgeIncompatible_IsReported()
    {
        var s = new Mp3FakeSource();
        s.CfSearch["cat:technology"] = new List<object>
        {
            Mp3FakeSource.CfHit("201", "alpha", "Alpha", 900),
            Mp3FakeSource.CfHit("202", "beta", "Beta", 800),
        };
        s.CfFile["201"] = Mp3FakeSource.CfF("301", "alpha", ("202", "incompatible"));
        s.CfFile["202"] = Mp3FakeSource.CfF("302", "beta");
        PyDict plan = ModpackPlanner.Plan("1.20.1", "forge", "科技", "small", null, s);
        PyDict pair = Py.D(Assert.Single(Py.ListOr(plan.Get("conflicts"))));
        Assert.Equal("incompatible", Py.S(pair.Get("kind")));
        Assert.Contains("CurseForge", Py.S(pair.Get("note")));
        Assert.Equal(new[] { "curseforge" }, Py.ListOr(plan.Get("sources")).Select(x => Py.S(x)).ToArray());
        Assert.Equal(1L, NetModpackSource.CfLoaderType("forge"));
        Assert.Equal(4L, NetModpackSource.CfLoaderType("fabric"));
        Assert.Equal(6L, NetModpackSource.CfLoaderType("neoforge"));
        Assert.Equal("justenoughitems", ModpackPlanner.NormKey("Just-Enough Items"));
    }

    // ───────────────────────────── 5. install_mods_batch 后台任务 ─────────────────────────────

    private static List<object> Mods3() => new()
    {
        PyDict.Of("slug", "techa", "name", "Tech A"),
        PyDict.Of("slug", "bad", "name", "Bad Mod"),
        PyDict.Of("source", "curseforge", "id", "102", "name", "Create"),
    };

    private static void HookFailingBad(List<string>? installed = null)
        => ModpackTools.InstallerHook = (inst, mod, mc, loader) =>
        {
            string key = Py.S(Py.Or(mod.Get("slug"), mod.Get("id")));
            if (key == "bad") throw new ModError("no version");
            if (installed != null) lock (installed) installed.Add(key);
            return PyDict.Of("files", new List<object> { key + ".jar" });
        };

    [Fact]
    public void InstallBatch_ReportsProgressPerItem_AndPartialFailure()
    {
        using var t = new NfTempInstance("mb3");
        HookFailingBad();
        var events = new List<ModpackTools.BatchEvent>();
        PyDict r = ModpackTools.InstallBatch(t.Inst, PyDict.Of("instance", t.Name, "mods", Mods3()), null, events.Add);
        Assert.Equal(new[] { "start 1", "done 1 True", "start 2", "done 2 False", "start 3", "done 3 True" },
            events.Select(e => e.Phase == "start" ? "start " + e.Index : "done " + e.Index + " " + e.Ok).ToArray());
        Assert.All(events, e => Assert.Equal(3, e.Total));
        Assert.Contains("no version", events[3].Error);
        Assert.Equal("Bad Mod", events[3].Name);
        Assert.False(Py.Truthy(r.Get("ok")));
        Assert.Equal(2, Py.ListOr(r.Get("installed")).Count);
        Assert.Contains("Bad Mod", ModpackPreview.BatchFinish(r));
        Assert.Contains("Bad Mod", ModpackPreview.BatchEventText(events[3]));
        Assert.Contains("1/3", ModpackPreview.BatchEventText(events[1]).Replace("(", "").Replace(")", " "));

        // 取消:两个模组之间检查,一个都不装
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => ModpackTools.InstallBatch(t.Inst, PyDict.Of("mods", Mods3()), null, null, cts.Token));
    }

    /// <summary>假宿主:install_mods_batch 起一个后台 Task 跑真正的批量安装,立刻回任务 id(同 App 的 Api)。</summary>
    private sealed class BatchHost : ToolRunner.IBackend
    {
        public readonly List<PyDict> Payloads = new();
        public readonly List<ModpackTools.BatchEvent> Events = new();
        public Task<PyDict>? Work;

        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
        {
            if (method != "install_mods_batch")
            {
                return Task.FromResult<T?>(default);
            }
            PyDict payload = Py.D(args!);
            Payloads.Add(payload);
            var gate = new ManualResetEventSlim(false);
            Work = Task.Run(() =>
            {
                gate.Wait(5000);   // 工具先返回,后台才开始装
                return ModpackTools.InstallBatch(ModpackTools.BatchInstance(payload), payload, null, e => { lock (Events) Events.Add(e); });
            });
            object id = "task-batch-1";
            gate.Set();
            return Task.FromResult((T?)id);
        }
    }

    [Fact]
    public async Task BatchTool_ReturnsTaskIdAtOnce_ConfirmsOnce_ListsEveryMod_TaskCardGetsIt()
    {
        using var dirs = new AiServiceDirs();
        using var t = new NfTempInstance("mb4");
        var installed = new List<string>();
        HookFailingBad(installed);
        var host = new BatchHost();
        var previews = new List<List<string>>();
        var started = new List<(string Id, string Label, string Tool)>();
        var runner = new ToolRunner(host)
        {
            PermissionMode = "default",
            Confirm = (call, label, reason, preview) => { previews.Add(Preview.Flatten(preview)); return Task.FromResult((true, true)); },
            TaskStarted = (id, label, tool) => started.Add((id, label, tool)),
        };
        var call = new ToolCall("b1", "install_mods_batch", PyDict.Of("instance", t.Name, "mods", Mods3()));
        string text = await runner.RunAsync(call);
        Assert.Equal(ToolCallStatus.COMPLETED, call.status);
        PyDict r = Py.D(PyJson.Loads(text));
        Assert.True(Py.Truthy(r.Get("background")));
        Assert.Equal("task-batch-1", Py.S(r.Get("task_id")));
        Assert.Equal(new[] { "Tech A", "Bad Mod", "Create" }, Py.ListOr(r.Get("mods")).Select(m => Py.S(Py.D(m).Get("name"))).ToArray());
        Assert.Contains("get_task_status", Py.S(r.Get("next")));
        // 整批只确认一次、每项逐一列出;确认卡说明了后台进度
        List<string> lines = previews.Single();
        Assert.Contains(lines, l => l.Contains("Tech A"));
        Assert.Contains(lines, l => l.Contains("Bad Mod"));
        Assert.Contains(lines, l => l.Contains("Create"));
        Assert.Contains(lines, l => l.Contains(I18nCore.Tr("确认后在后台逐个安装，任务卡上显示第几个、每个成功还是失败")));
        // 任务卡:同一个 id、工具名、标题
        (string id, string label, string tool) = started.Single();
        Assert.Equal(("task-batch-1", "install_mods_batch"), (id, tool));
        Assert.Equal(ModpackPreview.BatchTaskTitle(3, t.Name), label);
        Assert.Equal(t.Name, Py.S(host.Payloads.Single().Get("instance")));

        PyDict done = await host.Work!;
        Assert.Equal(6, host.Events.Count);
        Assert.Equal(new[] { true, false, true }, host.Events.Where(e => e.Phase == "done").Select(e => e.Ok).ToArray());
        Assert.False(Py.Truthy(done.Get("ok")));
        Assert.Equal(new[] { "techa", "102" }, installed.ToArray());

        // 空清单:立刻报错,不起任务
        var bad = new ToolCall("b2", "install_mods_batch", PyDict.Of("instance", t.Name, "mods", new List<object>()));
        await runner.RunAsync(bad);
        Assert.Equal(ToolCallStatus.FAILED, bad.status);
        Assert.Single(host.Payloads);
        // 点过「本对话都允许」,下一批照样要确认
        int before = previews.Count;
        await runner.RunAsync(new ToolCall("b3", "install_mods_batch", PyDict.Of("instance", t.Name, "mods", new List<object> { "techb" })));
        Assert.Equal(before + 1, previews.Count);
        await host.Work!;   // 等后台装完再复原注入点(否则会走到真的下载)
        Assert.Equal(2, host.Payloads.Count);
    }

    private static TaskRow WaitFinished(string id)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < until)
        {
            TaskRow? row = TaskStore.Get(id);
            if (row is { Finished: true })
            {
                return row;
            }
            Thread.Sleep(50);
        }
        throw new TimeoutException("task " + id + " did not finish");
    }

    [Fact]
    public async Task HostApi_BatchTask_ReportsEachItemToTheTaskRow_PartialFailureFailsTheTask()
    {
        using var t = new NfTempInstance("mb5");
        HookFailingBad();
        string id = (await InProcessApi.Instance.CallAsync<string>("install_mods_batch", PyDict.Of("instance", t.Name, "mods", Mods3())))!;
        Assert.StartsWith("task-", id);
        TaskRow row = WaitFinished(id);
        Assert.Equal(ModpackPreview.BatchTaskTitle(3, t.Name), row.Title);
        Assert.False(row.Success);
        Assert.Contains("Bad Mod", row.Status);
        string log = row.LogText;
        Assert.Contains(ModpackPreview.BatchEventText(new ModpackTools.BatchEvent("done", 1, 3, "Tech A", true, "")), log);
        Assert.Contains(ModpackPreview.BatchEventText(new ModpackTools.BatchEvent("done", 3, 3, "Create", true, "")), log);
        Assert.Contains("Bad Mod", log);
        Assert.Contains("no version", log);
    }

    [Fact]
    public async Task HostApi_BatchTask_AllSucceed_FinishesWithSummary()
    {
        using var t = new NfTempInstance("mb6");
        HookFailingBad();
        var mods = new List<object> { PyDict.Of("slug", "techa", "name", "Tech A"), PyDict.Of("slug", "techb", "name", "Tech B") };
        string id = (await InProcessApi.Instance.CallAsync<string>("install_mods_batch", PyDict.Of("instance", t.Name, "mods", mods)))!;
        TaskRow row = WaitFinished(id);
        Assert.True(row.Success);
        Assert.Equal(ModpackPreview.BatchFinish(PyDict.Of("installed", new List<object> { 1L, 2L }, "failed", new List<object>())), row.Status);
        Assert.Equal(100.0, row.Progress);

        // 实例不存在 / 清单为空:不起任务,直接报错
        await Assert.ThrowsAnyAsync<Exception>(() => InProcessApi.Instance.CallAsync<string>("install_mods_batch",
            PyDict.Of("instance", "no-such-" + Guid.NewGuid().ToString("N"), "mods", mods)));
        await Assert.ThrowsAnyAsync<Exception>(() => InProcessApi.Instance.CallAsync<string>("install_mods_batch",
            PyDict.Of("instance", t.Name, "mods", new List<object>())));
    }
}
