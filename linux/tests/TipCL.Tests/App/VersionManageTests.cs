using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TipCL.App.Common;
using TipCL.App.Pages.Version;
using TipCL.App.Shell;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>
/// M4-H P2 版本管理回归(版本设置 / 隔离切换 / 重命名·复制·隐藏 / 存档目标 / 页面接线)。
/// 夹具:一个真实实例 + 一个只有版本 json 的假版本(不需要完整游戏文件)。
/// </summary>
public class VersionManageTests : IDisposable
{
    private const string Ver = "demo-1.0";

    private readonly IApi _api = InProcessApi.Instance;
    private readonly string _inst = "v2-" + Guid.NewGuid().ToString("N").Substring(0, 8);

    public VersionManageTests()
    {
        TestApp.Run(() =>
        {
            _api.CallAsync<object>("create_instance", new { name = _inst }).GetAwaiter().GetResult();
            string dir = Path.Combine(Instances.GetInstancePath(_inst), "versions", Ver);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Ver + ".json"), $"{{\"id\":\"{Ver}\",\"type\":\"release\"}}");
        });
    }

    public void Dispose()
    {
        TestApp.Run(() =>
        {
            try { _api.CallAsync<object>("delete_instance", new { name = _inst }).GetAwaiter().GetResult(); }
            catch (Exception) { }
        });
    }

    private string VersionsDir() => Path.Combine(Instances.GetInstancePath(_inst), "versions");

    private async Task<List<PyDict>> RowsAsync(bool includeHidden = true)
    {
        var rows = await _api.CallAsync<List<object?>>("get_version_rows", new { instance = _inst, include_hidden = includeHidden }) ?? new();
        return rows.OfType<PyDict>().ToList();
    }

    [Fact]
    public void VersionSettings_SaveAndLoad_RoundTrip() => TestApp.RunAsync(async () =>
    {
        var before = await _api.CallAsync<PyDict>("get_version_settings", new { instance = _inst, version = Ver });
        Assert.NotNull(before);
        Assert.Equal("自动选择", Py.S(before!.Get("java")));

        await _api.CallAsync<PyDict>("save_version_settings", new
        {
            instance = _inst,
            version = Ver,
            data = new
            {
                memory_mb = 8192,
                gc = "zgc",
                server = "1.2.3.4",
                port = "25565",
                pre_launch = "echo hi",
                pre_launch_wait = false,
                window_mode = "fullscreen",
                jvm_args = "-XX:+UseZGC",
                hidden = true,
                isolation = "all",
            },
        });

        var after = await _api.CallAsync<PyDict>("get_version_settings", new { instance = _inst, version = Ver });
        Assert.NotNull(after);
        Assert.Equal(8192, Py.Int(after!.Get("memory_mb")));
        Assert.Equal("zgc", Py.S(after.Get("gc")));
        Assert.Equal("1.2.3.4", Py.S(after.Get("server")));
        Assert.Equal("fullscreen", Py.S(after.Get("window_mode")));
        Assert.True(Py.Truthy(after.Get("hidden")));
        Assert.Equal("all", Py.S(after.Get("isolation")));
        // 落盘:版本目录下的 tipcl.json 真的写了
        Assert.True(File.Exists(Path.Combine(VersionsDir(), Ver, "tipcl.json")));

        // 版本行必须把设置带出来(页面按这些键渲染标签与开关)
        var row = (await RowsAsync()).First(r => Py.S(r.Get("id")) == Ver);
        Assert.True(Py.Truthy(row.Get("hidden")));
        Assert.True(Py.Truthy(row.Get("isolated")));
        Assert.Equal(8192, Py.Int(row.Get("memory_mb")));
        Assert.Equal("完全独立", Py.S(row.Get("isolation_label")));

        // 收尾:取消隐藏,避免影响后续用例的默认可见性断言
        await _api.CallAsync<PyDict>("hide_version", new { instance = _inst, version = Ver, hidden = false });
    });

    [Fact]
    public void VersionIsolation_Toggle_RoundTrip() => TestApp.RunAsync(async () =>
    {
        // 全新版本没有 tipcl.json:隔离走全局 default_isolation(Config 默认 "all")
        Assert.False(string.IsNullOrEmpty(Py.S(await _api.CallAsync<object>("get_version_isolation", new { instance = _inst, version = Ver }) ?? "")));

        await _api.CallAsync<PyDict>("set_version_isolation", new { instance = _inst, version = Ver, mode = "none" });
        Assert.Equal("none", Py.S(await _api.CallAsync<object>("get_version_isolation", new { instance = _inst, version = Ver }) ?? ""));

        var on = await _api.CallAsync<PyDict>("toggle_version_isolation", new { instance = _inst, version = Ver, isolated = true, seed = false });
        Assert.NotNull(on);
        Assert.Equal("all", Py.S(await _api.CallAsync<object>("get_version_isolation", new { instance = _inst, version = Ver }) ?? ""));

        var rows = await RowsAsync();
        Assert.True(Py.Truthy(rows.First(r => Py.S(r.Get("id")) == Ver).Get("isolated")));

        // 版本设置对话框直接写隔离档位(不走 toggle)
        await _api.CallAsync<PyDict>("save_version_settings", new { instance = _inst, version = Ver, data = new { isolation = "saves" } });
        Assert.Equal("saves", Py.S(await _api.CallAsync<object>("get_version_isolation", new { instance = _inst, version = Ver }) ?? ""));

        await _api.CallAsync<PyDict>("toggle_version_isolation", new { instance = _inst, version = Ver, isolated = false });
    });

    [Fact]
    public void VersionOps_RenameCopyHide_RoundTrip() => TestApp.RunAsync(async () =>
    {
        await _api.CallAsync<string>("rename_version", new { instance = _inst, version = Ver, new_id = "demo-1.1" });
        Assert.True(Directory.Exists(Path.Combine(VersionsDir(), "demo-1.1")));
        Assert.False(Directory.Exists(Path.Combine(VersionsDir(), Ver)));
        Assert.True(File.Exists(Path.Combine(VersionsDir(), "demo-1.1", "demo-1.1.json")));

        await _api.CallAsync<string>("copy_version", new { instance = _inst, version = "demo-1.1", new_id = "demo-1.2" });
        Assert.True(Directory.Exists(Path.Combine(VersionsDir(), "demo-1.1")));
        Assert.True(Directory.Exists(Path.Combine(VersionsDir(), "demo-1.2")));

        await _api.CallAsync<PyDict>("hide_version", new { instance = _inst, version = "demo-1.2", hidden = true });
        var visible = await RowsAsync(includeHidden: false);
        Assert.DoesNotContain("demo-1.2", visible.Select(r => Py.S(r.Get("id"))));
        var all = await RowsAsync(includeHidden: true);
        Assert.Contains(all, r => Py.S(r.Get("id")) == "demo-1.2" && Py.Truthy(r.Get("hidden")));

        // 隐藏规整回「不隐藏」,并把版本名还给后续用例
        await _api.CallAsync<PyDict>("hide_version", new { instance = _inst, version = "demo-1.2", hidden = false });
        await _api.CallAsync<string>("rename_version", new { instance = _inst, version = "demo-1.1", new_id = Ver });
        foreach (string leftover in new[] { "demo-1.2" })
        {
            string dir = Path.Combine(VersionsDir(), leftover);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    });

    [Fact]
    public void InstanceJavaLabel_AndGameRoots() => TestApp.RunAsync(async () =>
    {
        string rootName = await _api.CallAsync<string>("game_root_name") ?? "";
        Assert.False(string.IsNullOrEmpty(rootName));

        string rootPath = await _api.CallAsync<string>("game_root_path") ?? "";
        Assert.False(string.IsNullOrEmpty(rootPath));

        string label = await _api.CallAsync<string>("instance_java_label", new { name = _inst }) ?? "";
        Assert.False(string.IsNullOrEmpty(label));

        var accounts = await _api.CallAsync<List<object>>("get_accounts");
        Assert.NotNull(accounts);
    });

    [Fact]
    public void SaveTargets_AndMedia_AreWired() => TestApp.RunAsync(async () =>
    {
        var targets = await _api.CallAsync<List<object?>>("get_saves_targets", new { instance = _inst }) ?? new();
        Assert.NotEmpty(targets);
        Assert.Equal("", Py.S(((PyDict)targets[0]!).Get("value")));

        var media = await _api.CallAsync<List<object?>>("list_media", new { instance = _inst, kind = "screenshots" }) ?? new();
        Assert.NotNull(media);

        var backups = await _api.CallAsync<List<object?>>("list_save_backups", new { instance = _inst }) ?? new();
        Assert.NotNull(backups);
    });

    [Fact]
    public void VersionManagePage_Constructs() => TestApp.Run(() =>
    {
        var page = new VersionManagePage();
        Assert.Equal("instance", page.PageId);
        Assert.Equal("版本管理", page.PageTitle);
        Assert.NotNull(page.Content);
    });

    /// <summary>外壳导航:启动页版本弹层的「管理」要能进版本管理页。</summary>
    [Fact]
    public void Shell_Navigates_ToVersionManage() => TestApp.Run(() =>
    {
        var win = new MainWindow();
        win.Navigate("instance");
        Assert.Equal("instance", win.CurrentPageId);
        Assert.IsType<VersionManagePage>(win.CurrentPage);
    });
}
