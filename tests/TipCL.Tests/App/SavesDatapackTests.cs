// 任务 B(数据包装进存档)回归:Core/Launcher/Saves.cs 的 InstallDatapackIntoSave 逐条路径 ——
//   源:<实例>/datapacks(隔离版本优先 <实例>/versions/<版本>/datapacks,与 get_installed_datapacks 的列表一致)
//   目标:<游戏目录>/saves/<存档>/datapacks/<文件名>(游戏目录按 VersionSettings.GameDir:隔离版本 = <实例>/versions/<版本>)
//   同名:就地覆盖(不产生副本);存档目录不存在:报「存档不存在」而不是凭空造一个空壳存档;
//   数据包不存在:报「数据包不存在: <文件名>」。
// 全部用真实临时目录 / 真实实例目录,不 mock 文件系统;走 IApi 契约(页面同一条链路)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TipCL.App.Common;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.App;

public class SavesDatapackTests : IDisposable
{
    private const string Ver = "dp-1.0";

    private readonly IApi _api = InProcessApi.Instance;
    private readonly string _inst = "dp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    private readonly string _tmp;

    public SavesDatapackTests()
    {
        TestApp.Run(() =>
        {
            _api.CallAsync<object>("create_instance", new { name = _inst }).GetAwaiter().GetResult();
            string dir = Path.Combine(InstDir(), "versions", Ver);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Ver + ".json"), $"{{\"id\":\"{Ver}\",\"type\":\"release\"}}");
        });
        _tmp = Path.Combine(Path.GetTempPath(), "tipcl-dp-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(_tmp);
    }

    public void Dispose()
    {
        TestApp.Run(() =>
        {
            try { _api.CallAsync<object>("delete_instance", new { name = _inst }).GetAwaiter().GetResult(); }
            catch (Exception) { }
        });
        try { Directory.Delete(_tmp, true); } catch (Exception) { }
    }

    /// <summary>造一个真实临时数据包文件(下载页「导入本地」传进来的就是这种文件,名字 = 落盘名)。</summary>
    private string TempZip(string name, string content)
    {
        string p = Path.Combine(_tmp, name);
        File.WriteAllText(p, content);
        return p;
    }

    // ───────────────────────────── 夹具 ─────────────────────────────

    private string InstDir() => Instances.GetInstancePath(_inst);

    /// <summary>在实例目录下造一个目录/文件并返回其绝对路径(真实文件系统)。</summary>
    private string Dir(params string[] parts)
    {
        string p = Path.Combine(new[] { InstDir() }.Concat(parts).ToArray());
        Directory.CreateDirectory(p);
        return p;
    }

    private string FileIn(string content, params string[] parts)
    {
        string p = Path.Combine(new[] { InstDir() }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    private string VersionSaves(string save) => Path.Combine(InstDir(), "versions", Ver, "saves", save);

    private Task<string?> Install(string filename, string saveName, string version)
        => _api.CallAsync<string>("install_datapack_into_save", new
        {
            instance = _inst,
            filename,
            save_name = saveName,
            version,
        });

    // ───────────────────────── 1) 非隔离版本 ─────────────────────────

    /// <summary>version 为空 = 游戏目录就是实例根:源 <实例>/datapacks,目标 <实例>/saves/&lt;存档&gt;/datapacks。</summary>
    [Fact]
    public void Install_InSharedVersion_UsesInstanceSaves() => TestApp.RunAsync(async () =>
    {
        FileIn("A", "datapacks", "dp-a.zip");
        string world = Dir("saves", "WorldA");

        string dest = await Install("dp-a.zip", "WorldA", "") ?? "";

        Assert.Equal(Path.Combine(world, "datapacks", "dp-a.zip"), dest);
        Assert.Equal("A", File.ReadAllText(dest));
        // 源文件仍在原处(是复制,不是搬运)
        Assert.True(File.Exists(Path.Combine(InstDir(), "datapacks", "dp-a.zip")));
    });

    /// <summary>同名文件已存在:就地覆盖(不产生副本),返回值仍是同一个目标路径。</summary>
    [Fact]
    public void Install_SameNameExists_OverwritesInPlace() => TestApp.RunAsync(async () =>
    {
        FileIn("NEW", "datapacks", "dp-same.zip");
        string target = FileIn("OLD", "saves", "WorldB", "datapacks", "dp-same.zip");

        string dest = await Install("dp-same.zip", "WorldB", "") ?? "";

        Assert.Equal(target, dest);
        Assert.Equal("NEW", File.ReadAllText(target));
        Assert.Single(Directory.GetFiles(Path.Combine(InstDir(), "saves", "WorldB", "datapacks")));
    });

    /// <summary>存档目录不存在:报「存档不存在」,并且**不**凭空造一个只有 datapacks/ 的空壳存档。</summary>
    [Fact]
    public void Install_MissingSave_ThrowsAndCreatesNothing() => TestApp.RunAsync(async () =>
    {
        FileIn("C", "datapacks", "dp-c.zip");

        var ex = await Assert.ThrowsAsync<SaveError>(() => Install("dp-c.zip", "NoSuchWorld", ""));
        Assert.Contains("存档不存在", ex.Message);
        Assert.False(Directory.Exists(Path.Combine(InstDir(), "saves", "NoSuchWorld")));
    });

    /// <summary>数据包不在源目录:报「数据包不存在: &lt;文件名&gt;」(失败形态与 DeleteSave/OpenSave 一致)。</summary>
    [Fact]
    public void Install_MissingDatapack_ReportsFileName() => TestApp.RunAsync(async () =>
    {
        Dir("saves", "WorldD");

        var ex = await Assert.ThrowsAsync<SaveError>(() => Install("nope.zip", "WorldD", ""));
        Assert.Contains("数据包不存在", ex.Message);
        Assert.Contains("nope.zip", ex.Message);
    });

    // ───────────────────────── 2) 隔离版本 ─────────────────────────

    /// <summary>
    /// 隔离版本(isolation=saves):游戏目录 = &lt;实例&gt;/versions/&lt;版本&gt;,
    /// 源优先取该版本自己的 datapacks(与 get_installed_datapacks 带 version 的列表同一个目录),
    /// 目标落在 &lt;实例&gt;/versions/&lt;版本&gt;/saves/&lt;存档&gt;/datapacks。
    /// </summary>
    [Fact]
    public void Install_IsolatedVersion_UsesVersionDatapacksAndVersionSaves() => TestApp.RunAsync(async () =>
    {
        await _api.CallAsync<PyDict>("save_version_settings", new { instance = _inst, version = Ver, data = new { isolation = "saves" } });
        FileIn("ISO", "versions", Ver, "datapacks", "dp-iso.zip");
        Dir("versions", Ver, "saves", "WorldIso");

        // 「列」与「装」必须指同一个目录:带 version 的列表里能看到这个数据包
        var listed = await _api.CallAsync<List<object>>("get_installed_datapacks", new { instance = _inst, version = Ver }) ?? new();
        Assert.Contains("dp-iso.zip", listed.Select(Py.S));

        string dest = await Install("dp-iso.zip", "WorldIso", Ver) ?? "";

        Assert.Equal(Path.Combine(VersionSaves("WorldIso"), "datapacks", "dp-iso.zip"), dest);
        Assert.Equal("ISO", File.ReadAllText(dest));
        // 没有写到实例根的 saves 里(隔离版本各有各的存档目录)
        Assert.False(Directory.Exists(Path.Combine(InstDir(), "saves", "WorldIso")));
    });

    /// <summary>
    /// 隔离版本 + 数据包只在实例共享目录(存档管理页列的就是实例根的 datapacks):
    /// 也要能装进该版本的存档 —— 目标仍按隔离版本算,源回退到实例根。
    /// </summary>
    [Fact]
    public void Install_IsolatedVersion_SharedSourceFallsBack() => TestApp.RunAsync(async () =>
    {
        await _api.CallAsync<PyDict>("save_version_settings", new { instance = _inst, version = Ver, data = new { isolation = "saves" } });
        FileIn("SHARED", "datapacks", "dp-shared.zip");
        Dir("versions", Ver, "saves", "WorldIso2");

        string dest = await Install("dp-shared.zip", "WorldIso2", Ver) ?? "";

        Assert.Equal(Path.Combine(VersionSaves("WorldIso2"), "datapacks", "dp-shared.zip"), dest);
        Assert.Equal("SHARED", File.ReadAllText(dest));
    });

    /// <summary>隔离版本 + 存档目录不存在:同样报「存档不存在」,不造空壳存档。</summary>
    [Fact]
    public void Install_IsolatedVersion_MissingSave_Throws() => TestApp.RunAsync(async () =>
    {
        await _api.CallAsync<PyDict>("save_version_settings", new { instance = _inst, version = Ver, data = new { isolation = "saves" } });
        FileIn("X", "versions", Ver, "datapacks", "dp-x.zip");

        var ex = await Assert.ThrowsAsync<SaveError>(() => Install("dp-x.zip", "NoSuchIso", Ver));
        Assert.Contains("存档不存在", ex.Message);
        Assert.False(Directory.Exists(VersionSaves("NoSuchIso")));
    });

    // ───────────────── 3) 下载页「装进存档」选项真的落盘 ─────────────────

    /// <summary>
    /// 下载页安装数据包时问的「装进存档」:选了哪个存档,数据包就必须真进那个存档的 datapacks,
    /// 而不是只把存档名传下去(改前 Core 侧从不读这个键)。
    /// </summary>
    [Fact]
    public void InstallTask_WithSaveOption_CopiesIntoSave() => TestApp.RunAsync(async () =>
    {
        string src = TempZip("dp-local.zip", "LOCAL");
        Dir("saves", "WorldE");
        string taskId = await _api.CallAsync<string>("install_datapack", new
        {
            name = "dp-local.zip",
            instance = _inst,
            extra = new { path = src, source = "本地", save = "WorldE" },
        }) ?? "";
        Assert.False(string.IsNullOrEmpty(taskId));
        await _api.CallAsync<PyDict>("wait_task", new { task_id = taskId, timeout = 60 });

        Assert.True(File.Exists(Path.Combine(InstDir(), "datapacks", "dp-local.zip")), "数据包没装进实例 datapacks");
        string inSave = Path.Combine(InstDir(), "saves", "WorldE", "datapacks", "dp-local.zip");
        Assert.True(File.Exists(inSave), "选了「装进存档」但没进存档:" + inSave);
        Assert.Equal("LOCAL", File.ReadAllText(inSave));
    });

    /// <summary>隔离版本 + 「装进存档」:数据包装进 &lt;实例&gt;/versions/&lt;版本&gt;/datapacks,再复制进该版本的存档。</summary>
    [Fact]
    public void InstallTask_WithSaveOption_OnIsolatedVersion() => TestApp.RunAsync(async () =>
    {
        await _api.CallAsync<PyDict>("save_version_settings", new { instance = _inst, version = Ver, data = new { isolation = "saves" } });
        string src = TempZip("dp-iso-local.zip", "ISOLOCAL");
        Dir("versions", Ver, "saves", "WorldF");

        string taskId = await _api.CallAsync<string>("install_datapack", new
        {
            name = "dp-iso-local.zip",
            instance = _inst,
            extra = new { path = src, source = "本地", save = "WorldF", version = Ver },
        }) ?? "";
        await _api.CallAsync<PyDict>("wait_task", new { task_id = taskId, timeout = 60 });

        Assert.True(File.Exists(Path.Combine(InstDir(), "versions", Ver, "datapacks", "dp-iso-local.zip")), "数据包没装进隔离版本的 datapacks");
        string inSave = Path.Combine(VersionSaves("WorldF"), "datapacks", "dp-iso-local.zip");
        Assert.True(File.Exists(inSave), "选了「装进存档」但没进隔离版本的存档:" + inSave);
        Assert.Equal("ISOLOCAL", File.ReadAllText(inSave));
    });
}
