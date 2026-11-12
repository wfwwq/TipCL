using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.App.Common;
using TipCL.App.Pages.Download;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>
/// M4-H P1 内容目录回归(下载页六分段 CatalogPage):
///   1) 六个分段的类别元数据与 IApi 契约名锁定(此前六个分段全是占位);
///   2) 「已装内容」链路(列 / 启停 / 删除)与收藏、导出往返走通真实文件系统;
///   3) DownloadPage 的分段宿主真的把 CatalogPage 挂进内容区(不再是占位空态)。
/// 走网络的方法(search_*)不在这里断言形状,只锁「不再落 NotImplementedException」的存在性由
/// IApi 契约测试覆盖(离线环境下不发起真实搜索)。
/// </summary>
public class CatalogApiTests : IDisposable
{
    private readonly IApi _api = InProcessApi.Instance;
    private readonly string _inst = "ca-" + Guid.NewGuid().ToString("N").Substring(0, 8);

    public CatalogApiTests()
    {
        TestApp.Run(() => _api.CallAsync<object>("create_instance", new { name = _inst }).GetAwaiter().GetResult());
    }

    public void Dispose()
    {
        TestApp.Run(() =>
        {
            try { _api.CallAsync<object>("delete_instance", new { name = _inst }).GetAwaiter().GetResult(); }
            catch (Exception) { }
        });
    }

    private string Dir(string sub)
    {
        string path = Path.Combine(Instances.GetInstancePath(_inst), sub);
        Directory.CreateDirectory(path);
        return path;
    }

    // ───────────────────────────── 类别元数据 ─────────────────────────────

    /// <summary>六个分段(不含原版)的类别键与契约方法名必须与 Windows 版 CatalogKind 一致。</summary>
    [Fact]
    public void CatalogKinds_SixSegments_MapToWindowsContracts()
    {
        var kinds = CatalogKind.All;
        Assert.Equal(6, kinds.Length);
        Assert.Equal(new[] { "mod", "modpack", "datapack", "resourcepack", "shader", "world" },
            kinds.Select(k => k.Key).ToArray());
        Assert.Equal(new[] { "search_mods", "search_modpacks", "search_datapacks", "search_resourcepacks", "search_shaders", "search_worlds" },
            kinds.Select(k => k.SearchMethod).ToArray());
        Assert.Equal(new[] { "install_mod", "install_modpack", "install_datapack", "install_resourcepack", "install_shader", "install_world" },
            kinds.Select(k => k.InstallMethod).ToArray());
        // 标题中文与 Windows 版一致
        Assert.Equal("Mod", kinds[0].Title);
        Assert.Equal("整合包", kinds[1].Title);
        Assert.Equal("数据包", kinds[2].Title);
        Assert.Equal("资源包", kinds[3].Title);
        Assert.Equal("光影包", kinds[4].Title);
        Assert.Equal("世界", kinds[5].Title);
        // 只有 Mod 走侧载检查更新;世界没有「已安装」列表(装好即出现在存档里)
        Assert.Equal("get_installed_mods", CatalogKind.Mod.InstalledMethod);
        Assert.Equal("", CatalogKind.World.InstalledMethod);
    }

    /// <summary>六种类别都必须能构建出带内容的页面(控件构造在无头 UI 线程上)。</summary>
    [Fact]
    public void CatalogPage_Constructs_ForEveryKind() => TestApp.Run(() =>
    {
        foreach (var kind in CatalogKind.All)
        {
            var page = new CatalogPage(kind);
            Assert.Equal("catalog-" + kind.Key, page.PageId);
            Assert.Equal(kind.Title, page.PageTitle);
            Assert.NotNull(page.Content);
        }
    });

    /// <summary>
    /// 下载页分段宿主:第 1~6 段必须挂上对应的 CatalogPage(而不是占位空态),第 0 段回到原版列表。
    /// 用反射调私有 ShowSeg 避免走真实网络搜索(页面构造只在挂载时才拉数据)。
    /// </summary>
    [Fact]
    public void DownloadPage_Segments_HostCatalogPages() => TestApp.Run(() =>
    {
        var page = new DownloadPage();
        var showSeg = typeof(DownloadPage).GetMethod("ShowSeg", BindingFlags.NonPublic | BindingFlags.Instance);
        var kindsField = typeof(DownloadPage).GetField("Kinds", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(showSeg);
        Assert.NotNull(kindsField);

        var kinds = (CatalogKind?[])kindsField!.GetValue(null)!;
        Assert.Equal(7, kinds.Length);
        Assert.Null(kinds[0]); // 原版分段不是目录页
        Assert.Equal(CatalogKind.All.Select(k => k.Key), kinds.Skip(1).Select(k => k!.Key));

        // 只切到第 1 段并立刻切回,锁「宿主真的换了内容」(一进一退,避免留下在途搜索)
        showSeg!.Invoke(page, new object[] { 1 });
        var host = (ContentControl)typeof(DownloadPage)
            .GetField("_host", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(page)!;
        Assert.IsType<CatalogPage>(host.Content);
        Assert.Equal("catalog-mod", ((CatalogPage)host.Content!).PageId);

        showSeg.Invoke(page, new object[] { 0 });
        Assert.IsNotType<CatalogPage>(host.Content);
    });

    // ───────────────────────────── 已装内容 ─────────────────────────────

    [Fact]
    public void InstalledMods_ListToggleDelete_RoundTrip() => TestApp.RunAsync(async () =>
    {
        string mods = Dir("mods");
        File.WriteAllText(Path.Combine(mods, "demo.jar"), "x");

        var entries = await _api.CallAsync<List<object>>("get_installed_mod_entries", new { instance = _inst, version = "" });
        Assert.NotNull(entries);
        Assert.Contains(entries!, e => Py.S(Py.D(e).Get("filename")) == "demo.jar" && Py.Truthy(Py.D(e).Get("enabled")));

        // 禁用 = 改名为 *.disabled(Core 语义),行里显示的文件名随之变化
        var off = await _api.CallAsync<PyDict>("disable_mod", new { instance = _inst, filename = "demo.jar", version = "" });
        string disabled = Py.S(off!.Get("file"));
        Assert.Equal("demo.jar.disabled", disabled);
        var entries2 = await _api.CallAsync<List<object>>("get_installed_mod_entries", new { instance = _inst, version = "" });
        Assert.Contains(entries2!, e => Py.S(Py.D(e).Get("filename")) == disabled && !Py.Truthy(Py.D(e).Get("enabled")));
        var enabledOnly = await _api.CallAsync<List<object>>("get_installed_mods", new { instance = _inst, version = "" });
        Assert.DoesNotContain("demo.jar", enabledOnly!.Select(Py.S));
        Assert.DoesNotContain(disabled, enabledOnly!.Select(Py.S));

        await _api.CallAsync<PyDict>("enable_mod", new { instance = _inst, filename = disabled, version = "" });
        enabledOnly = await _api.CallAsync<List<object>>("get_installed_mods", new { instance = _inst, version = "" });
        Assert.Contains("demo.jar", enabledOnly!.Select(Py.S));

        // 「已装于版本」下拉的数据源:至少要有实例共享目录一项
        var targets = await _api.CallAsync<List<object>>("get_mods_targets", new { instance = _inst });
        Assert.NotNull(targets);
        Assert.Contains(targets!, t => Py.S(Py.D(t).Get("value")) == "");

        await _api.CallAsync<PyDict>("delete_mod", new { instance = _inst, filename = "demo.jar", version = "" });
        Assert.False(File.Exists(Path.Combine(mods, "demo.jar")));
    });

    [Fact]
    public void InstalledDatapacks_ListAndDelete_RoundTrip() => TestApp.RunAsync(async () =>
    {
        string dir = Dir("datapacks");
        File.WriteAllText(Path.Combine(dir, "demo-pack.zip"), "x");

        var rows = await _api.CallAsync<List<string>>("get_installed_datapacks", new { instance = _inst, version = "" });
        Assert.NotNull(rows);
        Assert.Contains("demo-pack.zip", rows!);

        await _api.CallAsync<PyDict>("delete_datapack", new { instance = _inst, filename = "demo-pack.zip", version = "" });
        rows = await _api.CallAsync<List<string>>("get_installed_datapacks", new { instance = _inst, version = "" });
        Assert.DoesNotContain("demo-pack.zip", rows!);
    });

    // ───────────────────────────── 收藏 ─────────────────────────────

    [Fact]
    public void Favorites_Toggle_RoundTrip() => TestApp.RunAsync(async () =>
    {
        var before = await _api.CallAsync<List<object>>("catalog_favorites");
        Assert.NotNull(before);

        var item = new { name = "测试模组", source = "modrinth", slug = "t-" + _inst, id = (object?)null };
        try
        {
            var after = await _api.CallAsync<List<object>>("toggle_favorite", new { item });
            Assert.NotNull(after);
            Assert.Contains(after!, r => Py.S(Py.D(r).Get("slug")) == "t-" + _inst);
            // 落盘:配置里能看到
            var saved = Py.ListOr(Config.CONFIG.Get("catalog_favorites"));
            Assert.Contains(saved, r => Py.S(Py.D(r).Get("slug")) == "t-" + _inst);

            // 再点一次 = 取消收藏
            var again = await _api.CallAsync<List<object>>("toggle_favorite", new { item });
            Assert.DoesNotContain("t-" + _inst, again!.Select(r => Py.S(Py.D(r).Get("slug"))));
        }
        finally
        {
            Config.CONFIG.Set("catalog_favorites", before!);
            Config.CONFIG.Save();
        }
    });

    // ───────────────────────────── 导出 ─────────────────────────────

    [Fact]
    public void Export_Content_And_ExportMany_RoundTrip() => TestApp.RunAsync(async () =>
    {
        string dir = Dir("datapacks");
        File.WriteAllText(Path.Combine(dir, "exp-pack.zip"), "x");
        string dest = Path.Combine(Path.GetTempPath(), "tipcl-exp-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dest);
        try
        {
            // 记住导出目录(目录页的「上次:…」提示用)
            string remembered = await _api.CallAsync<string>("remember_export_dir", new { path = dest }) ?? "";
            Assert.False(string.IsNullOrEmpty(remembered));

            string one = await _api.CallAsync<string>("export_content", new
            {
                kind = "datapack",
                name = "exp-pack.zip",
                dest_dir = dest,
                version = "",
                instance = _inst,
            }) ?? "";
            Assert.True(File.Exists(one), "单件导出没有落盘: " + one);

            // 批量导出返回 {dir, exported[], failed[]} —— 页面按这个结构呈现成败
            var many = await _api.CallAsync<PyDict>("export_contents", new
            {
                kind = "datapack",
                names = new List<object> { "exp-pack.zip", "不存在.zip" },
                dest_dir = dest,
                version = "",
                instance = _inst,
            });
            Assert.NotNull(many);
            Assert.Single(Py.ListOr(many!.Get("exported")));
            Assert.Single(Py.ListOr(many.Get("failed")));
            Assert.Equal(dest, Py.S(many.Get("dir")));
        }
        finally
        {
            try { Directory.Delete(dest, true); } catch (Exception) { }
        }
    });
}
