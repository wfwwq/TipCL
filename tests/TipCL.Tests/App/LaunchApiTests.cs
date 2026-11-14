// M3-C0：IApi 启动/Java/实例内容方法表与 LaunchCore 门面纯逻辑单测（不触网）。
// 注：实例相关用例用唯一实例名，不碰全局 ROOT（并行测试共享进程内 Config/TaskStore）。
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TipCL.App.Common;
using TipCL.App.Common.Launch;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>静默 IApi：不广播 BridgeEvent（避免与其他测试类的事件订阅互扰）。</summary>
internal class SilentApi : IApi
{
#pragma warning disable CS0067
    public event Action<ApiEvent> BridgeEvent;
#pragma warning restore CS0067
    public void RaiseEvent(ApiEvent ev) { }
    public bool CancelTask(string taskId) => false;
    public Task<TaskRow> StartTaskAsync(string title, Func<TaskProgress, CancellationToken, Task> work, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<T> CallAsync<T>(string method, object args = null, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<T> TryCallAsync<T>(string method, T fallback = default, object args = null, CancellationToken ct = default)
        => Task.FromResult(fallback);
}

public class LaunchApiTests : IDisposable
{
    private readonly InProcessApi _api = InProcessApi.Instance;
    private readonly string _inst;
    private readonly string _instPath;

    public LaunchApiTests()
    {
        // 唯一实例目录：进程内 Config 的 instances_dir 可能与其他测试并行使用
        _inst = "c0-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        _instPath = InstancesApi.EnsureInstance(_inst).path;
    }

    public void Dispose()
    {
        try { Directory.Delete(_instPath, recursive: true); } catch (Exception) { }
    }

    private object InstArg => new { instance = _inst };

    // ───────── Java 管理 ─────────

    [Fact]
    public Task GetJavas_ReturnsEmptyList_WithoutInstallDir() => TestApp.RunAsync(async () =>
    {
        var rows = await _api.CallAsync<List<object>>("get_javas");
        Assert.NotNull(rows);
        var installed = await _api.CallAsync<List<object>>("get_java_list");
        Assert.NotNull(installed);
    });

    [Fact]
    public void NormalizeJavaPref_AutoBranches() => TestApp.Run(() =>
    {
        Assert.Equal("自动选择", Py.S(LaunchCore.NormalizeJavaPref("自动选择")));
        Assert.Equal("自动选择", Py.S(LaunchCore.NormalizeJavaPref("auto")));
        Assert.Equal("自动选择", Py.S(LaunchCore.NormalizeJavaPref("default")));
        Assert.Equal("自动选择", Py.S(LaunchCore.NormalizeJavaPref("")));
        Assert.Equal("自动选择", Py.S(LaunchCore.NormalizeJavaPref(null)));
    });

    [Fact]
    public void IsOfflineAccount_OfflineAliases_AreTrue() => TestApp.Run(() =>
    {
        Assert.True(LaunchCore.IsOfflineAccount(""));
        Assert.True(LaunchCore.IsOfflineAccount("离线模式"));
        Assert.False(LaunchCore.IsOfflineAccount("Steve"));
    });

    // ───────── Java 组合框(2026-11-10 补 java_combo_options / get·set_instance_java)─────────

    [Fact]
    public Task JavaComboOptions_StartsWithAutoChoice() => TestApp.RunAsync(async () =>
    {
        var opts = await _api.CallAsync<List<object>>("java_combo_options", InstArg);
        Assert.NotNull(opts);
        Assert.NotEmpty(opts);
        Assert.Equal("自动选择", Py.S(((PyDict)opts[0])["value"]));
        Assert.Equal("自动选择", Py.S(((PyDict)opts[0])["label"]));
    });

    [Fact]
    public Task InstanceJava_RoundTrips_AndAppearsInComboAsSaved() => TestApp.RunAsync(async () =>
    {
        // 默认 =「自动选择」
        Assert.Equal("自动选择", Py.S(await _api.CallAsync<object>("get_instance_java", new { name = _inst })));

        // 保存一个不在已装列表里的路径 → 组合框补一条「已保存 (…)」
        string fake = @"C:\tipcl-test\jdk-17\bin\javaw.exe";
        await _api.CallAsync<object>("set_instance_java", new { name = _inst, java = fake });
        Assert.Equal(fake, Py.S(await _api.CallAsync<object>("get_instance_java", new { name = _inst })));

        var opts = await _api.CallAsync<List<object>>("java_combo_options", InstArg);
        Assert.Contains(opts, o => Py.S(((PyDict)o)["value"]) == fake);
        string label = await _api.CallAsync<string>("java_combo_label_for", new { instance = _inst, options = opts });
        Assert.StartsWith("已保存", label);

        // 归一化别名回「自动选择」(照 Windows normalize_java_pref)
        await _api.CallAsync<object>("set_instance_java", new { name = _inst, java = "auto" });
        Assert.Equal("自动选择", Py.S(await _api.CallAsync<object>("get_instance_java", new { name = _inst })));
    });

    // ───────── 启动 ─────────

    [Fact]
    public Task LaunchGame_WithoutVersion_ThrowsLaunchError() => TestApp.RunAsync(async () =>
    {
        var api = new SilentApi();
        var progress = new TaskProgress("c0-silent", api);
        var args = InProcessApi.ToDict(new { version = "" });
        var ex = await Assert.ThrowsAsync<LaunchError>(
            () => LaunchCore.RunAsync(progress, CancellationToken.None, api, args));
        Assert.Contains("请先选择版本", ex.Message);
    });

    [Fact]
    public Task IsGameRunning_And_Stop_WithoutProcess() => TestApp.RunAsync(async () =>
    {
        Assert.False(await _api.CallAsync<bool>("is_game_running"));
        Assert.False(LaunchCore.IsRunning);
        var r = await _api.CallAsync<PyDict>("stop_game");
        Assert.False(Py.Truthy(r.Req("ok")));
    });

    [Fact]
    public Task PreflightLaunch_MissingVersion_ReportsItems() => TestApp.RunAsync(async () =>
    {
        var pre = await _api.CallAsync<PyDict>("preflight_launch", new { instance = _inst, version = "" });
        Assert.NotNull(pre);
        Assert.NotNull(pre.Get("items"));
        Assert.NotNull(pre.Get("ok"));
    });

    // ───────── 实例内容 / 版本行 ─────────

    [Fact]
    public Task GetVersionRows_EmptyInstance_ReturnsEmptyList() => TestApp.RunAsync(async () =>
    {
        var rows = await _api.CallAsync<List<object>>("get_version_rows", InstArg);
        Assert.NotNull(rows);
        Assert.Empty(rows);
    });

    [Fact]
    public Task GetInstalledResourcepacksAndShaders_EmptyInstance_ReturnEmpty() => TestApp.RunAsync(async () =>
    {
        var packs = await _api.CallAsync<List<object>>("get_installed_resourcepacks", InstArg);
        var shaders = await _api.CallAsync<List<object>>("get_installed_shaders", InstArg);
        Assert.Empty(packs);
        Assert.Empty(shaders);
    });

    [Fact]
    public Task ListSaves_EmptyInstance_ReturnsEmpty() => TestApp.RunAsync(async () =>
    {
        var saves = await _api.CallAsync<List<object>>("list_saves", InstArg);
        Assert.Empty(saves);
    });

    [Fact]
    public Task DeleteResourcepack_MissingFile_DoesNotThrow() => TestApp.RunAsync(async () =>
    {
        await _api.CallAsync<PyDict>("delete_resourcepack", new { instance = _inst, filename = "nope.zip" });
        await _api.CallAsync<PyDict>("delete_shader", new { instance = _inst, filename = "nope.zip" });
    });

    [Fact]
    public Task ImportExistingFolder_RejectsNonGameDir() => TestApp.RunAsync(async () =>
    {
        string tmp = Path.Combine(Path.GetTempPath(), "tipcl-c0-nogame-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(tmp);
        try
        {
            var ex = await Assert.ThrowsAsync<ValueError>(
                () => _api.CallAsync<object>("import_existing_folder", new { path = tmp }));
            Assert.Contains("versions", ex.Message);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch (Exception) { }
        }
    });

    // ───────── 游玩时长（桩转正）─────────

    [Fact]
    public Task Playtime_Methods_ReturnNumbers() => TestApp.RunAsync(async () =>
    {
        object total = await _api.CallAsync<object>("get_total_playtime");
        Assert.NotNull(total);
        object per = await _api.CallAsync<object>("get_playtime", new { instance = _inst });
        Assert.NotNull(per);
        string fmt = await _api.CallAsync<string>("format_playtime", new { seconds = 90L });
        Assert.False(string.IsNullOrEmpty(fmt));
    });

    private static async Task<TaskRow> WaitFinished(string taskId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var row = TaskStore.Get(taskId);
            if (row != null && row.Finished) return row;
            await Task.Delay(50);
        }
        return null;
    }
}
