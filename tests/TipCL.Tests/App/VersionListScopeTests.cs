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

/// <summary>
/// M4-H R9 缺陷 B 回归:界面的版本列表与「启动预检」必须看同一份目录。
///
/// 真机现象(R8 盒子):「版本管理 / 实例库」列的是 `<TIPCL_HOME>/.minecraft/versions`(根实例)里的
/// `1.20.1`,而 CLI 启动预检找的是**实例目录** `<TIPCL_HOME>/.minecraft/26.3/versions` →
/// 界面上看得见的版本,启动时报「版本未安装」;该实例自己的 `26.3` 反而不在列表里。
///
/// 语义核对(不是「两个不同的语义」,是同一个语义被算成了两个路径):
///   * Core 的 `Instance(name)`:name 为空 → 落到 `Instances.RootName()`(= `<TIPCL_HOME>/.minecraft`),
///     所以 `get_version_rows` **不传 instance** 拿到的是根实例的 versions;
///   * 页面自己的兜底扫描(`LibraryPage.ScanLocalVersionsAsync(_folder)`、
///     `InstanceDetailPage.LoadVersionsAsync` 里的 `new Instance(_inst).InstalledIds()`)与行操作
///     (uninstall/rename/copy/hide 都传 `instance = _folder` / `_inst`)用的都是**实例目录**;
///   * 启动预检同样只看实例目录。
/// 三处同语义、一处走岔 → 修三个调用点(带上 instance),不是改语义。
/// 本文件锁死这条 API 语义;三个调用点的改动由「修了什么」一节 + 这条语义共同约束。
/// </summary>
public class VersionListScopeTests
{
    private const string InstVer = "only-in-instance-1.0";
    private const string RootVer = "only-in-root-1.0";

    private readonly IApi _api = InProcessApi.Instance;
    private readonly string _inst = "r9b-" + Guid.NewGuid().ToString("N")[..8];

    private static void MakeVersion(string versionsDir, string id)
    {
        string dir = Path.Combine(versionsDir, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, id + ".json"),
            $"{{\"id\":\"{id}\",\"type\":\"release\",\"mainClass\":\"net.minecraft.client.main.Main\",\"libraries\":[]}}");
    }

    private async Task SetupAsync()
    {
        await _api.CallAsync<object>("create_instance", new { name = _inst });
        MakeVersion(Path.Combine(Instances.GetInstancePath(_inst), "versions"), InstVer);
        MakeVersion(Path.Combine(Instances.RootDir(), "versions"), RootVer);
        await _api.TryCallAsync<object>("save_settings", null, new { data = new { default_instance = _inst } });
    }

    private async Task TeardownAsync()
    {
        try { await _api.CallAsync<object>("delete_instance", new { name = _inst }); } catch (Exception) { }
        try
        {
            string d = Path.Combine(Instances.RootDir(), "versions", RootVer);
            if (Directory.Exists(d)) Directory.Delete(d, true);
        }
        catch (Exception) { }
    }

    private static List<string> RowIds(IEnumerable<object?> rows)
        => rows.OfType<PyDict>().Select(d => Py.S(d.Get("id"))).Where(s => s.Length > 0).ToList();

    /// <summary>不传 instance = 根实例;传 instance = 实例目录。页面/预检必须是后者。</summary>
    [Fact]
    public void GetVersionRows_IsInstanceScoped() => TestApp.RunAsync(async () =>
    {
        await SetupAsync();
        try
        {
            var root = RowIds(await _api.CallAsync<List<object?>>("get_version_rows", new { include_hidden = true }) ?? new());
            Assert.Contains(RootVer, root);
            Assert.DoesNotContain(InstVer, root);

            var mine = RowIds(await _api.CallAsync<List<object?>>("get_version_rows", new { instance = _inst, include_hidden = true }) ?? new());
            Assert.Contains(InstVer, mine);
            Assert.DoesNotContain(RootVer, mine);

            // 启动预检看的就是这个实例目录(同一语义、同一份清单)
            var inst = new Instance(_inst);
            Assert.Equal(InstVer, Py.S(inst.InstalledIds().FirstOrDefault() ?? ""));
        }
        finally { await TeardownAsync(); }
    });
}
