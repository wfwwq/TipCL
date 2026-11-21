// AI2-modpack 包的测试:整合包导出(mrpack / CurseForge)/ 分发许可三类分类 / 禁止分发拒绝 / 一句话造包 / 批量安装。
//
// 全部不联网:Modrinth / CurseForge 走假的 IModpackSource(ModpackSources.Factory 注入点,或直接传参),
// 批量安装走 ModpackTools.InstallerHook;读写都落在 TestIsolation 的临时 TIPCL_HOME(实例用唯一名建、用完删),
// 检查点 / 产物走 AiServiceDirs。会改静态注入点的用例都在同一个类里(xunit 同类串行),Dispose 时复原。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

/// <summary>假的 Modrinth / CurseForge 源(只读字典,线程安全)。</summary>
internal sealed class MpFakeSource : IModpackSource
{
    public readonly Dictionary<string, PyDict> MrBySha1 = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<long, PyDict> CfByFp = new();
    public readonly Dictionary<string, PyDict> CfMods = new(StringComparer.Ordinal);
    public readonly Dictionary<string, List<object>> SearchByKey = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, PyDict> VersionBySlug = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, PyDict> ProjectsById = new(StringComparer.Ordinal);
    public bool FailCurseForge;
    public int MrCalls;

    public PyDict? ModrinthVersionByHash(string sha1)
    {
        Interlocked.Increment(ref MrCalls);
        return MrBySha1.TryGetValue(sha1, out PyDict? v) ? v : null;
    }

    public Dictionary<long, PyDict> CurseForgeMatches(IList<long> fingerprints)
    {
        if (FailCurseForge) throw new IOException("curseforge down");
        return fingerprints.Where(CfByFp.ContainsKey).ToDictionary(fp => fp, fp => CfByFp[fp]);
    }

    public Dictionary<string, PyDict> CurseForgeMods(IList<string> modIds)
        => modIds.Where(CfMods.ContainsKey).ToDictionary(id => id, id => CfMods[id], StringComparer.Ordinal);

    public List<object> SearchModrinth(string query, string mcVersion, string loader, string? category, int limit)
    {
        string key = category != null ? "cat:" + category : "q:" + query;
        return SearchByKey.TryGetValue(key, out List<object>? hits) ? hits.Take(limit).ToList() : new List<object>();
    }

    public PyDict? ModrinthLatestVersion(string project, string mcVersion, string loader)
        => VersionBySlug.TryGetValue(project, out PyDict? v) ? v : null;

    public Dictionary<string, PyDict> ModrinthProjects(IList<string> ids)
        => ids.Where(ProjectsById.ContainsKey).ToDictionary(id => id, id => ProjectsById[id], StringComparer.Ordinal);
}

/// <summary>一个造好的实例:5 个 jar 覆盖三类许可 + config / resourcepacks + fabric 版本 json。</summary>
internal sealed class MpFixture : IDisposable
{
    public readonly NfTempInstance T = new("mp");
    public readonly MpFakeSource Src = new();
    public readonly string OutDir = Path.Combine(Path.GetTempPath(), "tipcl-mp-out-" + Guid.NewGuid().ToString("N"));
    public readonly Dictionary<string, string> Sha1 = new();

    public string Jar(string file) => T.PathOf("mods", file);

    public MpFixture()
    {
        Directory.CreateDirectory(OutDir);
        T.Version("fabric-loader-0.15.11-1.20.1");
        // a = Modrinth 上有;b = 只在 CurseForge,允许分发;c = 只在 CurseForge,禁止分发;d = 两边都没有;e = CurseForge 查不到许可
        foreach (string id in new[] { "a", "b", "c", "d", "e" })
        {
            T.FabricJar(id + ".jar", "mod_" + id, "1.0." + id.Length);
        }
        T.Write("config/a.toml", "x = 1");
        T.Write("config/sub/b.json", "{}");
        T.Write("resourcepacks/pack.zip", "zipdata");
        foreach (string id in new[] { "a", "b", "c", "d", "e" })
        {
            Sha1[id] = Utils.Sha1File(Jar(id + ".jar"));
        }
        Src.MrBySha1[Sha1["a"]] = PyDict.Of(
            "id", "VA", "project_id", "PA",
            "files", new List<object>
            {
                PyDict.Of("url", "https://cdn.modrinth.com/data/PA/versions/VA/a.jar", "filename", "a.jar", "primary", true,
                    "size", new FileInfo(Jar("a.jar")).Length, "hashes", PyDict.Of("sha1", Sha1["a"], "sha512", "ignored")),
            });
        long Fp(string id) => ModUpdate.CfFingerprint(Jar(id + ".jar"));
        Src.CfByFp[Fp("b")] = PyDict.Of("mod_id", "1001", "file_id", "2001", "file_name", "b.jar", "download_url", "");
        Src.CfByFp[Fp("c")] = PyDict.Of("mod_id", "1002", "file_id", "2002", "file_name", "c.jar", "download_url", "");
        Src.CfByFp[Fp("e")] = PyDict.Of("mod_id", "1003", "file_id", "2003", "file_name", "e.jar", "download_url", "");
        Src.CfMods["1001"] = PyDict.Of("name", "B Mod", "slug", "b-mod", "allow_distribution", true, "url", "");
        Src.CfMods["1002"] = PyDict.Of("name", "C Mod", "slug", "c-mod", "allow_distribution", false, "url", "");
        Src.CfMods["1003"] = PyDict.Of("name", "E Mod", "slug", "e-mod", "allow_distribution", null!, "url", "");
    }

    public void Dispose()
    {
        T.Dispose();
        try { Directory.Delete(OutDir, true); } catch (Exception) { }
    }
}

[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class AiModpackTests : IDisposable
{
    public AiModpackTests()
    {
        ModpackDistribution.ClearCache();
    }

    public void Dispose()
    {
        ModpackSources.Factory = null;
        ModpackTools.InstallerHook = null;
        ModpackDistribution.ClearCache();
    }

    private static List<string> Names(object? list) => Py.ListOr(list!).Select(x => Py.S(Py.D(x).Get("filename"))).OrderBy(x => x, StringComparer.Ordinal).ToList();

    private static Dictionary<string, string> ZipText(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using ZipArchive zip = ZipFile.OpenRead(path);
        foreach (ZipArchiveEntry e in zip.Entries)
        {
            using var r = new StreamReader(e.Open(), Encoding.UTF8);
            result[e.FullName] = r.ReadToEnd();
        }
        return result;
    }

    // ───────────────────────────── 注册表 ─────────────────────────────

    [Fact]
    public async Task Tools_AreRegisteredInSchemaMetaAndDispatch_WithRightRisk()
    {
        List<string> names = Tools.Names();
        foreach (string n in ModpackTools.Names)
        {
            Assert.Contains(n, names);
            Assert.NotNull(ToolMeta.Get(n));
            Assert.True(await ToolRunner.DispatchesAsync(n), n);
        }
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.True(ToolMeta.Get("check_modpack_distribution")!.@readonly);
        Assert.True(ToolMeta.Get("plan_modpack")!.@readonly);
        Assert.Equal("network", ToolMeta.Get("plan_modpack")!.side_effect);
        Assert.False(ToolMeta.Get("export_modpack")!.@readonly);
        Assert.Equal("write_local", ToolMeta.Get("export_modpack")!.side_effect);
        Assert.Equal("high", ToolMeta.Get("install_mods_batch")!.risk);
        Assert.False(ToolRunner.AllowsAlways("export_modpack"));
        Assert.False(ToolRunner.AllowsAlways("install_mods_batch"));
        Assert.Equal("查整合包分发许可", Tools.Label("check_modpack_distribution"));
        Assert.Equal("导出整合包 demo", Tools.Label("export_modpack", PyDict.Of("instance", "demo")));
    }

    [Fact]
    public async Task KeywordsAndSystemPrompt_BringTheFlowIn()
    {
        List<string> Pick(string text) => Tools.SelectToolSchemas(new List<object> { PyDict.Of("role", "user", "content", text) }).Select(Tools.SchemaName).ToList();
        List<string> make = Pick("帮我做一个 1.20.1 fabric 的轻量科技 + 优化整合包");
        Assert.Contains("plan_modpack", make);
        Assert.Contains("install_mods_batch", make);
        Assert.Contains("export_modpack", Pick("把这个实例导出成 mrpack"));
        Assert.Contains("check_modpack_distribution", Pick("这些模组能不能分发"));
        string p = AiData.SystemPrompt();
        foreach (string n in ModpackTools.Names)
        {
            Assert.Contains(n, p);
        }
        Assert.Contains("用户没确认清单之前不要 create_instance", p);
        Assert.Contains("allow_bundled_jars", p);
        Assert.Empty(await AiToolRefs.ProblemsAsync());
    }

    // ───────────────────────────── 分发许可 ─────────────────────────────

    [Fact]
    public void Distribution_Mrpack_SplitsIntoLinkableBundleForbidden()
    {
        using var f = new MpFixture();
        PyDict r = ModpackDistribution.Check(f.T.Inst, "mrpack", f.Src, useCache: false);
        Assert.Equal(new[] { "a.jar" }, Names(r.Get("linkable")));
        Assert.Equal(new[] { "b.jar", "d.jar", "e.jar" }, Names(r.Get("bundle")));
        Assert.Equal(new[] { "c.jar" }, Names(r.Get("forbidden")));
        PyDict Row(string list, string file) => Py.ListOr(r.Get(list)).Select(x => Py.D(x)).Single(x => Py.S(x.Get("filename")) == file);
        Assert.False(Py.Truthy(Row("bundle", "b.jar").Get("needs_consent")));   // 作者允许第三方分发
        Assert.True(Py.Truthy(Row("bundle", "d.jar").Get("needs_consent")));    // 来源不明
        Assert.True(Py.Truthy(Row("bundle", "e.jar").Get("needs_consent")));    // 许可查不到
        Assert.Equal("local", Py.S(Row("bundle", "d.jar").Get("source")));
        Assert.Contains("侵犯", Py.S(Row("bundle", "d.jar").Get("reason")));
        Assert.Contains("allowModDistribution=false", Py.S(Row("forbidden", "c.jar").Get("reason")));
        Assert.Equal(1L, Py.Int(Py.D(r.Get("summary")).Get("forbidden")));
        Assert.Equal(2L, Py.Int(Py.D(r.Get("summary")).Get("bundle_needs_consent")));
        // 本地算出的 sha512 是 128 位十六进制(mrpack 必填)
        Assert.Equal(128, Py.S(Row("linkable", "a.jar").Get("sha512")).Length);
    }

    [Fact]
    public void Distribution_CurseForgeFormat_LinksCfFilesAndBundlesModrinthOnly()
    {
        using var f = new MpFixture();
        PyDict r = ModpackDistribution.Check(f.T.Inst, "curseforge", f.Src, useCache: false);
        Assert.Equal(new[] { "b.jar", "c.jar", "e.jar" }, Names(r.Get("linkable")));
        Assert.Equal(new[] { "a.jar", "d.jar" }, Names(r.Get("bundle")));
        Assert.Empty(Py.ListOr(r.Get("forbidden")));
        PyDict c = Py.ListOr(r.Get("linkable")).Select(x => Py.D(x)).Single(x => Py.S(x.Get("filename")) == "c.jar");
        Assert.Equal("1002", Py.S(c.Get("project_id")));
        Assert.Equal("2002", Py.S(c.Get("file_id")));
        Assert.Contains("第三方启动器", Py.S(c.Get("reason")));
    }

    [Fact]
    public void Distribution_DisallowedHostAndCurseForgeOutage_FallBackToConsent()
    {
        using var f = new MpFixture();
        Assert.True(ModpackDistribution.AllowedMrpackUrl("https://github.com/x/y/releases/download/1/a.jar"));
        Assert.False(ModpackDistribution.AllowedMrpackUrl("https://edge.forgecdn.net/files/1/2/a.jar"));
        Assert.False(ModpackDistribution.AllowedMrpackUrl("http://cdn.modrinth.com/a.jar"));
        f.Src.MrBySha1[f.Sha1["d"]] = PyDict.Of("id", "VD", "project_id", "PD",
            "files", new List<object> { PyDict.Of("url", "https://example.com/d.jar", "primary", true, "hashes", PyDict.Of("sha1", f.Sha1["d"])) });
        f.Src.FailCurseForge = true;
        PyDict r = ModpackDistribution.Check(f.T.Inst, "mrpack", f.Src, useCache: false);
        Assert.Equal(new[] { "a.jar" }, Names(r.Get("linkable")));
        Assert.Empty(Py.ListOr(r.Get("forbidden")));   // CurseForge 查不到时不会误判成「禁止」,也不会误判成「允许」
        Assert.All(Py.ListOr(r.Get("bundle")), x => Assert.True(Py.Truthy(Py.D(x).Get("needs_consent"))));
        Assert.NotEmpty(Py.ListOr(r.Get("lookup_errors")));
    }

    [Fact]
    public void Distribution_CacheIsReusedUntilModsChange()
    {
        using var f = new MpFixture();
        ModpackDistribution.Check(f.T.Inst, "mrpack", f.Src, useCache: false);
        int calls = f.Src.MrCalls;
        ModpackDistribution.Check(f.T.Inst, "mrpack", f.Src, useCache: true);
        Assert.Equal(calls, f.Src.MrCalls);
        f.T.FabricJar("z.jar", "mod_z");
        PyDict r = ModpackDistribution.Check(f.T.Inst, "mrpack", f.Src, useCache: true);
        Assert.True(f.Src.MrCalls > calls);
        Assert.Contains("z.jar", Names(r.Get("bundle")));
    }

    // ───────────────────────────── 导出 ─────────────────────────────

    [Fact]
    public void ExportMrpack_StructureFollowsModrinthSpec()
    {
        using var f = new MpFixture();
        var opt = new ModpackExportOptions { Name = "Demo Pack", Version = "2.0.0", Summary = "测试", Source = f.Src };
        opt.Exclude.Add("c.jar");
        string dest = Path.Combine(f.OutDir, "demo.mrpack");
        PyDict r = ModpackExport.Export(f.T.Inst, dest, opt);
        Assert.True(File.Exists(dest));
        Dictionary<string, string> zip = ZipText(dest);
        PyDict index = Py.D(PyJson.Loads(zip["modrinth.index.json"]));
        Assert.Equal(1L, Py.Int(index.Get("formatVersion")));
        Assert.Equal("minecraft", Py.S(index.Get("game")));
        Assert.Equal("2.0.0", Py.S(index.Get("versionId")));
        Assert.Equal("Demo Pack", Py.S(index.Get("name")));
        Assert.Equal("测试", Py.S(index.Get("summary")));
        List<object> files = Py.ListOr(index.Get("files"));
        Assert.Single(files);
        PyDict a = Py.D(files[0]);
        Assert.Equal("mods/a.jar", Py.S(a.Get("path")));
        Assert.Equal(f.Sha1["a"], Py.S(Py.D(a.Get("hashes")).Get("sha1")));
        Assert.Equal(ModpackDistribution.Sha512File(f.Jar("a.jar")), Py.S(Py.D(a.Get("hashes")).Get("sha512")));
        Assert.Equal("https://cdn.modrinth.com/data/PA/versions/VA/a.jar", Py.S(Py.ListOr(a.Get("downloads")).Single()));
        Assert.Equal(new FileInfo(f.Jar("a.jar")).Length, Py.Int(a.Get("fileSize")));
        PyDict deps = Py.D(index.Get("dependencies"));
        Assert.Equal("1.20.1", Py.S(deps.Get("minecraft")));
        Assert.Equal("0.15.11", Py.S(deps.Get("fabric-loader")));
        Assert.Equal(2, deps.Count);
        // 允许分发的 b 打进 overrides;来源不明的 d / e 默认不打包;排除的 c 不在;链接的 a 不带 jar
        Assert.Contains("overrides/mods/b.jar", zip.Keys);
        Assert.DoesNotContain("overrides/mods/a.jar", zip.Keys);
        Assert.DoesNotContain("overrides/mods/c.jar", zip.Keys);
        Assert.DoesNotContain("overrides/mods/d.jar", zip.Keys);
        Assert.DoesNotContain("overrides/mods/e.jar", zip.Keys);
        Assert.Contains("overrides/config/a.toml", zip.Keys);
        Assert.Contains("overrides/config/sub/b.json", zip.Keys);
        Assert.Contains("overrides/resourcepacks/pack.zip", zip.Keys);
        Assert.Equal(new[] { "d.jar", "e.jar" }, Py.ListOr(r.Get("skipped_jars")).Select(x => Py.S(x)).OrderBy(x => x).ToArray());
        Assert.Equal(1L, Py.Int(r.Get("files_linked")));
        Assert.False(File.Exists(dest + ".part"));
    }

    [Fact]
    public void Export_RefusesWhenForbiddenModsRemain_AndWritesNothing()
    {
        using var f = new MpFixture();
        string dest = Path.Combine(f.OutDir, "x.mrpack");
        var ex = Assert.Throws<ModpackError>(() => ModpackExport.Export(f.T.Inst, dest, new ModpackExportOptions { Source = f.Src }));
        Assert.Contains("c.jar", ex.Message);
        Assert.False(File.Exists(dest));
        Assert.False(File.Exists(dest + ".part"));
    }

    [Fact]
    public void Export_BundlesUnknownJarsOnlyWithConsent_AndHonoursIncludeOverrides()
    {
        using var f = new MpFixture();
        var opt = new ModpackExportOptions { Source = f.Src, AllowBundledJars = true, Overrides = new List<string> { "resourcepacks" } };
        opt.Exclude.Add("c.jar");
        string dest = Path.Combine(f.OutDir, "y.mrpack");
        ModpackExport.Export(f.T.Inst, dest, opt);
        Dictionary<string, string> zip = ZipText(dest);
        Assert.Contains("overrides/mods/d.jar", zip.Keys);
        Assert.Contains("overrides/mods/e.jar", zip.Keys);
        Assert.Contains("overrides/resourcepacks/pack.zip", zip.Keys);
        Assert.DoesNotContain(zip.Keys, k => k.StartsWith("overrides/config/", StringComparison.Ordinal));
        // 不覆盖已有文件
        Assert.Throws<FileExistsError>(() => ModpackExport.Export(f.T.Inst, dest, opt));
    }

    [Fact]
    public void ExportCurseForge_ManifestHasProjectAndFileIds()
    {
        using var f = new MpFixture();
        var opt = new ModpackExportOptions { Format = "curseforge", Name = "CF Pack", Author = "Steve", Source = f.Src };
        string dest = Path.Combine(f.OutDir, "cf.zip");
        ModpackExport.Export(f.T.Inst, dest, opt);
        Dictionary<string, string> zip = ZipText(dest);
        PyDict m = Py.D(PyJson.Loads(zip["manifest.json"]));
        Assert.Equal("minecraftModpack", Py.S(m.Get("manifestType")));
        Assert.Equal(1L, Py.Int(m.Get("manifestVersion")));
        Assert.Equal("overrides", Py.S(m.Get("overrides")));
        Assert.Equal("CF Pack", Py.S(m.Get("name")));
        Assert.Equal("Steve", Py.S(m.Get("author")));
        PyDict mc = Py.D(m.Get("minecraft"));
        Assert.Equal("1.20.1", Py.S(mc.Get("version")));
        PyDict loader = Py.D(Py.ListOr(mc.Get("modLoaders")).Single());
        Assert.Equal("fabric-0.15.11", Py.S(loader.Get("id")));
        Assert.True(Py.Truthy(loader.Get("primary")));
        var files = Py.ListOr(m.Get("files")).Select(x => Py.D(x)).ToList();
        Assert.Equal(3, files.Count);
        Assert.Contains(files, x => Py.Int(x.Get("projectID")) == 1002 && Py.Int(x.Get("fileID")) == 2002 && Py.Truthy(x.Get("required")));
        Assert.IsType<long>(files[0].Get("projectID"));
        Assert.Contains("modlist.html", zip.Keys);
        Assert.DoesNotContain("modrinth.index.json", zip.Keys);
        Assert.DoesNotContain("overrides/mods/a.jar", zip.Keys);   // Modrinth-only 需用户同意
    }

    [Fact]
    public void LegacyUiEntry_UsesTheSameEngine_AndKeepsOldBehaviour()
    {
        using var f = new MpFixture();
        ModpackSources.Factory = () => f.Src;
        string dest = Path.Combine(f.OutDir, "ui.mrpack");
        File.WriteAllText(dest, "old");   // 界面保存对话框已问过覆盖
        string path = ExportPack.ExportMrpack(f.T.Inst, dest);
        Assert.Equal(PPath.Norm(dest), path);
        Dictionary<string, string> zip = ZipText(dest);
        PyDict index = Py.D(PyJson.Loads(zip["modrinth.index.json"]));
        Assert.Equal(new[] { "mods/a.jar" }, Py.ListOr(index.Get("files")).Select(x => Py.S(Py.D(x).Get("path"))).ToArray());
        // 原行为:Modrinth 上没有的一律打进 overrides(不做许可检查)
        foreach (string id in new[] { "b", "c", "d", "e" })
        {
            Assert.Contains("overrides/mods/" + id + ".jar", zip.Keys);
        }
        Assert.Contains("overrides/config/a.toml", zip.Keys);
    }

    // ───────────────────────────── 工具链路 ─────────────────────────────

    [Fact]
    public async Task ExportTool_GoesThroughConfirm_WithFileListPreview_AndCheckpoint()
    {
        using var dirs = new AiServiceDirs();
        using var f = new MpFixture();
        ModpackSources.Factory = () => f.Src;
        var runner = new ToolRunner(new NfFakeApi()) { PermissionMode = "default", Instance = f.T.Name };
        var args = PyDict.Of("instance", f.T.Name, "name", "Tool Pack", "version", "1.2.3", "dest", f.OutDir,
            "exclude", new List<object> { "c.jar" }, "include_overrides", new List<object> { "config" });

        // 没接确认回调:写操作被拒,没有文件
        var denied = new ToolCall("e0", "export_modpack", new PyDict(args));
        await runner.RunAsync(denied);
        Assert.Equal(ToolCallStatus.DENIED, denied.status);
        Assert.Empty(Directory.GetFiles(f.OutDir));

        var previews = new List<List<string>>();
        runner.Confirm = (call, label, reason, preview) =>
        {
            previews.Add(Preview.Flatten(preview));
            return Task.FromResult((true, true)); // 用户点「本对话都允许」
        };
        var callArgs = new PyDict(args);
        List<string> paths = Tools.AffectedPaths("export_modpack", callArgs, f.T.Name);
        string expected = Path.Combine(f.OutDir, "Tool Pack-1.2.3.mrpack");
        Assert.Equal(PPath.Norm(expected), Assert.Single(paths));
        string chat = "chat-" + Guid.NewGuid().ToString("N");
        Assert.True(Py.Truthy(Checkpoint.Snapshot(chat, "t1", paths).Get("ok")));

        var call1 = new ToolCall("e1", "export_modpack", callArgs);
        string text = await runner.RunAsync(call1);
        Assert.Equal(ToolCallStatus.COMPLETED, call1.status);
        Assert.True(File.Exists(expected), text);
        List<string> lines = previews.Single();
        Assert.Contains(lines, l => l.Contains("Tool Pack-1.2.3.mrpack"));
        Assert.Contains(lines, l => l.Contains("mods/a.jar"));                    // 链接下载的
        Assert.Contains(lines, l => l.Contains("overrides/mods/b.jar"));          // 打进包里的
        Assert.Contains(lines, l => l.Contains("overrides/config/a.toml"));       // 配置预设
        Assert.Contains(lines, l => l.Contains("d.jar"));                         // 不打包的也列出来
        Assert.DoesNotContain(lines, l => l.Contains("overrides/resourcepacks")); // include_overrides 只要 config

        // 回滚 = 导出的文件消失(快照时还不存在)
        Assert.True(Py.Truthy(Checkpoint.Rollback(chat, "t1").Get("ok")));
        Assert.False(File.Exists(expected));

        // 点过「本对话都允许」,下一次仍然要问;同名文件不覆盖而是加序号
        await runner.RunAsync(new ToolCall("e2", "export_modpack", new PyDict(args)));
        await runner.RunAsync(new ToolCall("e3", "export_modpack", new PyDict(args)));
        Assert.Equal(3, previews.Count);
        Assert.True(File.Exists(Path.Combine(f.OutDir, "Tool Pack-1.2.3-2.mrpack")));
    }

    [Fact]
    public async Task ExportTool_ForbiddenMods_AreRefused()
    {
        using var dirs = new AiServiceDirs();
        using var f = new MpFixture();
        ModpackSources.Factory = () => f.Src;
        var previews = new List<List<string>>();
        var runner = new ToolRunner(new NfFakeApi())
        {
            PermissionMode = "default",
            Confirm = (call, label, reason, preview) => { previews.Add(Preview.Flatten(preview)); return Task.FromResult((true, false)); },
        };
        var call = new ToolCall("x1", "export_modpack", PyDict.Of("instance", f.T.Name, "dest", f.OutDir));
        string text = await runner.RunAsync(call);
        Assert.Equal(ToolCallStatus.FAILED, call.status);
        Assert.Contains("c.jar", text);
        Assert.Contains(previews.Single(), l => l.Contains("c.jar"));   // 预览里就提示了禁止分发
        Assert.Empty(Directory.GetFiles(f.OutDir));
    }

    [Fact]
    public async Task CheckTool_IsReadOnly_AndReturnsCompactThreeLists()
    {
        using var f = new MpFixture();
        ModpackSources.Factory = () => f.Src;
        var runner = new ToolRunner(new NfFakeApi()) { PermissionMode = "default" };
        var call = new ToolCall("c1", "check_modpack_distribution", PyDict.Of("instance", f.T.Name));
        string text = await runner.RunAsync(call);
        Assert.Equal(ToolCallStatus.COMPLETED, call.status);
        PyDict r = Py.D(PyJson.Loads(text));
        Assert.Equal(new[] { "a.jar" }, Names(r.Get("linkable")));
        Assert.Equal(new[] { "c.jar" }, Names(r.Get("forbidden")));
        Assert.DoesNotContain("sha512", text);
        Assert.Contains("禁止分发", Py.S(r.Get("advice")));
    }

    // ───────────────────────────── 一句话造包 ─────────────────────────────

    private static PyDict Hit(string slug, string id, long downloads, params string[] cats)
        => PyDict.Of("slug", slug, "project_id", id, "title", slug.ToUpperInvariant(), "description", slug + " desc", "downloads", downloads,
            "categories", cats.Cast<object>().ToList());

    private static PyDict Ver(string id, params (string Project, string Type)[] deps)
        => PyDict.Of("id", "v-" + id, "version_number", "1.0", "version_type", "release",
            "files", new List<object> { PyDict.Of("filename", id + ".jar", "primary", true, "size", 1234L, "url", "https://cdn.modrinth.com/" + id + ".jar") },
            "dependencies", deps.Select(d => (object)PyDict.Of("project_id", d.Project, "dependency_type", d.Type)).ToList());

    private static MpFakeSource PlannerSource()
    {
        var s = new MpFakeSource();
        s.SearchByKey["cat:technology"] = new List<object>
        {
            Hit("fabric-api", "P_FAPI", 9999, "library"),
            Hit("techa", "P_TECHA", 900, "technology"),
            Hit("techb", "P_TECHB", 800, "technology"),
        };
        s.SearchByKey["cat:optimization"] = new List<object>
        {
            Hit("sodium", "P_SOD", 5000, "optimization"),
            Hit("embeddium", "P_EMB", 4000, "optimization"),
            Hit("nover", "P_NOV", 3000, "optimization"),
        };
        s.VersionBySlug["techa"] = Ver("techa", ("P_FAPI", "required"));
        s.VersionBySlug["techb"] = Ver("techb", ("P_TECHA", "incompatible"), ("P_FAPI", "required"));
        s.VersionBySlug["sodium"] = Ver("sodium");
        s.VersionBySlug["embeddium"] = Ver("embeddium");
        s.ProjectsById["P_FAPI"] = PyDict.Of("id", "P_FAPI", "slug", "fabric-api", "title", "Fabric API");
        return s;
    }

    [Fact]
    public void Planner_ThemeToCategories_AndSize()
    {
        Assert.Equal(new[] { "technology", "optimization" }, ModpackPlanner.ThemeCategories("轻量科技 + 优化"));
        Assert.Equal(new[] { "magic", "adventure" }, ModpackPlanner.ThemeCategories("魔法冒险"));
        Assert.Equal(8, ModpackPlanner.SizeTarget("", "轻量科技"));
        Assert.Equal(20, ModpackPlanner.SizeTarget("", "科技"));
        Assert.Equal(40, ModpackPlanner.SizeTarget("large", "轻量科技"));
        Assert.Throws<ValueError>(() => ModpackPlanner.Plan("1.20.1", "无", "科技", null, null, new MpFakeSource()));
        Assert.Throws<ValueError>(() => ModpackPlanner.Plan("", "fabric", "科技", null, null, new MpFakeSource()));
    }

    [Fact]
    public void Planner_CandidatesCarryReasonDepsAndConflicts()
    {
        PyDict plan = ModpackPlanner.Plan("1.20.1", "Fabric", "轻量科技 + 优化", "", null, PlannerSource());
        Assert.True(Py.Truthy(plan.Get("ok")), PyJson.Dumps(plan));
        Assert.Equal("small", Py.S(plan.Get("size")));
        var cands = Py.ListOr(plan.Get("candidates")).Select(x => Py.D(x)).ToList();
        Assert.Equal(new[] { "techa", "techb", "sodium", "embeddium" }, cands.Select(c => Py.S(c.Get("slug"))).ToArray());
        Assert.DoesNotContain(cands, c => Py.S(c.Get("slug")) == "fabric-api");   // library 不当候选,作为前置出现
        PyDict techa = cands[0];
        Assert.Contains("technology", Py.S(techa.Get("reason")));
        PyDict dep = Py.D(Py.ListOr(techa.Get("dependencies")).Single());
        Assert.Equal("Fabric API", Py.S(dep.Get("title")));
        Assert.False(Py.Truthy(dep.Get("in_list")));
        PyDict extra = Py.D(Py.ListOr(plan.Get("extra_dependencies")).Single());
        Assert.Equal("P_FAPI", Py.S(extra.Get("project_id")));
        Assert.Equal(2, Py.ListOr(extra.Get("required_by")).Count);
        var conflicts = Py.ListOr(plan.Get("conflicts")).Select(x => Py.D(x)).ToList();
        Assert.Contains(conflicts, c => Py.S(c.Get("kind")) == "incompatible" && !Py.Truthy(c.Get("inferred")));
        Assert.Contains(conflicts, c => Py.S(c.Get("kind")) == "overlap" && Py.Truthy(c.Get("inferred")) && Py.S(c.Get("note")).Contains("SODIUM"));
        Assert.NotEmpty(Py.ListOr(cands[1].Get("conflicts")));   // techb 自己也带着冲突提示
        Assert.Equal("nover", Py.S(Py.D(Py.ListOr(plan.Get("dropped")).Single()).Get("slug")));
    }

    [Fact]
    public async Task PlanTool_IsReadOnly_AndShowsThePlanCard()
    {
        ModpackSources.Factory = PlannerSource;
        var plans = new List<List<(string Title, string Status)>>();
        var runner = new ToolRunner(new NfFakeApi()) { PermissionMode = "default", PlanChanged = p => plans.Add(p) };
        var call = new ToolCall("p1", "plan_modpack", PyDict.Of("mc_version", "1.20.1", "loader", "fabric", "theme", "轻量科技 + 优化"));
        string text = await runner.RunAsync(call);
        Assert.Equal(ToolCallStatus.COMPLETED, call.status);
        List<(string Title, string Status)> steps = plans.Single();
        Assert.Equal(6, steps.Count);
        Assert.Equal("in_progress", steps[0].Status);
        Assert.All(steps.Skip(1), s => Assert.Equal("pending", s.Status));
        Assert.Contains("1.20.1", steps[1].Title);
        Assert.Contains("plan_steps", text);
        Assert.Contains("create_instance", text);   // next 提示了后续流程
    }

    [Fact]
    public async Task BatchInstall_ConfirmsOnce_ListsEveryMod_AndReportsFailures()
    {
        using var dirs = new AiServiceDirs();
        using var t = new NfTempInstance("mpb");
        var installed = new List<string>();
        ModpackTools.InstallerHook = (inst, mod, mc, loader) =>
        {
            string slug = Py.S(mod.Get("slug"));
            if (slug == "bad") throw new ModError("no version");
            lock (installed) installed.Add(slug + "@" + inst.name);
            return PyDict.Of("files", new List<object> { slug + ".jar" });
        };
        var previews = new List<List<string>>();
        var runner = new ToolRunner(new NfFakeApi())
        {
            PermissionMode = "default",
            Confirm = (call, label, reason, preview) => { previews.Add(Preview.Flatten(preview)); return Task.FromResult((true, true)); },
        };
        var mods = new List<object>
        {
            PyDict.Of("slug", "techa", "name", "Tech A"),
            PyDict.Of("slug", "sodium", "name", "Sodium"),
            PyDict.Of("slug", "bad", "name", "Bad Mod"),
            PyDict.Of("slug", "sodium", "name", "Sodium"),   // 重复的只装一次
        };
        List<string> paths = Tools.AffectedPaths("install_mods_batch", PyDict.Of("instance", t.Name, "mods", mods), t.Name);
        Assert.EndsWith("mods", Assert.Single(paths));
        var call = new ToolCall("b1", "install_mods_batch", PyDict.Of("instance", t.Name, "mods", mods));
        string text = await runner.RunAsync(call);
        Assert.Equal(ToolCallStatus.COMPLETED, call.status);
        List<string> lines = previews.Single();
        Assert.Contains(lines, l => l.Contains("Tech A"));
        Assert.Contains(lines, l => l.Contains("Sodium"));
        Assert.Contains(lines, l => l.Contains("Bad Mod"));
        Assert.Equal(new[] { "techa@" + t.Name, "sodium@" + t.Name }, installed.ToArray());
        PyDict r = Py.D(PyJson.Loads(text));
        Assert.False(Py.Truthy(r.Get("ok")));
        Assert.Equal(2, Py.ListOr(r.Get("installed")).Count);
        Assert.Equal("Bad Mod", Py.S(Py.D(Py.ListOr(r.Get("failed")).Single()).Get("name")));
        Assert.Equal("批量安装 3 个模组 → " + t.Name, Tools.Label("install_mods_batch", PyDict.Of("instance", t.Name, "mods", mods)));

        // 点了「本对话都允许」,下一批照样要确认
        await runner.RunAsync(new ToolCall("b2", "install_mods_batch", PyDict.Of("instance", t.Name, "mods", new List<object> { "techb" })));
        Assert.Equal(2, previews.Count);
    }

    [Fact]
    public void LoaderDetection_ReadsInstalledVersionIds()
    {
        using var t = new NfTempInstance("mpl");
        t.Version("1.20.1-forge-47.2.0");
        (string mc, string loader, string lv) = ModpackExport.DetectLoader(t.Inst);
        Assert.Equal(("1.20.1", "forge", "47.2.0"), (mc, loader, lv));
        Assert.Equal("forge", ModpackExport.MrpackLoaderKey("forge"));
        Assert.Equal("quilt-loader", ModpackExport.MrpackLoaderKey("quilt"));
        Assert.Equal("Pack-1.0.0.mrpack", ModpackExport.DefaultFileName("Pack", "1.0.0", "mrpack"));
        Assert.Equal("Pack.zip", ModpackExport.DefaultFileName("Pack", "", "curseforge"));
    }
}
