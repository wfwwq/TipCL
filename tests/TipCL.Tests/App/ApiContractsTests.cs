using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TipCL.App.Common;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>
/// 2026-11-10 补齐的一批 Api 契约(账号 / Java / 服务器 / 清理 / 反馈 / 语言 / 主题包)回归。
/// 这里锁两件事:
///   1) 这些方法**不再落 NotImplementedException**(接线完成);
///   2) 参数里的嵌套匿名对象能变成 PyDict——此前 `save_settings(new { data = new { … } })`
///      因浅转换被判非 PyDict,设置静默不生效且不落盘。
/// </summary>
public class ApiContractsTests : IDisposable
{
    private readonly IApi _api = InProcessApi.Instance;
    private readonly string _inst = "c2-" + Guid.NewGuid().ToString("N").Substring(0, 8);

    public ApiContractsTests()
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

    [Fact]
    public void Terracotta_AndLan_AreWired() => TestApp.RunAsync(async () =>
    {
        var snap = await _api.CallAsync<PyDict>("terracotta_snapshot");
        Assert.NotNull(snap);
        Assert.False(string.IsNullOrEmpty(Py.S(snap!["state"])), "snapshot 缺 state");
        Assert.False(string.IsNullOrEmpty(Py.S(snap!["label"])), "snapshot 缺 label");
        Assert.False(string.IsNullOrEmpty(Py.S(await _api.CallAsync<object>("terracotta_player") ?? "") as string ?? ""));

        var ips = await _api.CallAsync<List<object>>("local_ips");
        Assert.NotNull(ips);
        object? hint = await _api.CallAsync<object>("lan_hint", new { port = 25565L });
        Assert.NotNull(hint);
    });

    [Fact]
    public void SaveSettings_Persists_NestedDict() => TestApp.RunAsync(async () =>
    {
        bool? beforeDark = Config.CONFIG.Get("ui_dark") as bool?;
        try
        {
            await _api.CallAsync<object>("save_settings", new { data = new { ui_dark = true, default_memory_mb = 8192 } });
            Assert.True(Py.Truthy(Config.CONFIG.Get("ui_dark")), "嵌套 data 未落进配置");
            Assert.Equal(8192L, Py.Int(Config.CONFIG.Get("default_memory_mb")));
        }
        finally
        {
            await _api.CallAsync<object>("save_settings", new { data = new { ui_dark = beforeDark ?? false } });
        }
    });

    [Fact]
    public void Servers_AddListUpdateDelete_RoundTrip() => TestApp.RunAsync(async () =>
    {
        var added = await _api.CallAsync<PyDict>("add_server", new
        {
            instance = _inst, name = "测试服", ip = "mc.example.com", port = 25566, description = "备注",
        });
        Assert.NotNull(added);
        Assert.Equal("mc.example.com", Py.S(added!["ip"]));
        Assert.Equal(25566L, Py.Int(added["port"]));

        var rows = await _api.CallAsync<List<object>>("list_servers", new { instance = _inst });
        Assert.Single(rows!);

        await _api.CallAsync<PyDict>("update_server", new
        {
            instance = _inst, index = 0, name = "改名", ip = "a.example.com", port = 25565, description = "",
        });
        var rows2 = await _api.CallAsync<List<object>>("list_servers", new { instance = _inst });
        Assert.Equal("a.example.com", Py.S(Py.D(rows2![0])["ip"]));

        await _api.CallAsync<PyDict>("delete_server", new { instance = _inst, index = 0 });
        var rows3 = await _api.CallAsync<List<object>>("list_servers", new { instance = _inst });
        Assert.Empty(rows3!);
    });

    [Fact]
    public void Java_ListAndDefault_RoundTrip() => TestApp.RunAsync(async () =>
    {
        var rows = await _api.CallAsync<List<object>>("list_java");
        Assert.NotNull(rows); // 机器上可能没装 Java,空列表也算接线正确

        string before = Py.S(Config.CONFIG.Get("default_java", ""));
        try
        {
            string fake = OperatingSystem.IsWindows() ? @"C:\tipcl-c2\jdk\bin\javaw.exe" : "/tmp/tipcl-c2/jdk/bin/java";
            // 路径不存在 → 应抛 ValueError(提示找不到 java),而不是静默写入
            await Assert.ThrowsAsync<ValueError>(() => _api.CallAsync<PyDict>("set_default_java", new { path = fake }));
            Assert.Equal(before, Py.S(Config.CONFIG.Get("default_java", "")));
        }
        finally
        {
            Config.CONFIG.Set("default_java", before);
        }
    });

    [Fact]
    public void Cleaner_Language_Themes_Accounts_AreWired() => TestApp.RunAsync(async () =>
    {
        var preview = await _api.CallAsync<PyDict>("cleaner_preview");
        Assert.NotNull(preview);
        Assert.NotNull(preview!["count"]);

        string lang = (await _api.CallAsync<string>("get_language"))!;
        Assert.False(string.IsNullOrEmpty(lang));
        await _api.CallAsync<PyDict>("set_language", new { lang });

        var langs = await _api.CallAsync<Dictionary<string, string>>("available_languages");
        Assert.NotEmpty(langs!);

        var themes = await _api.CallAsync<List<object>>("list_themes");
        Assert.NotNull(themes);

        var accounts = await _api.CallAsync<List<object>>("get_account_rows");
        Assert.NotNull(accounts);

        Assert.False(await _api.CallAsync<bool>("allow_multi_instance"));

        var rec = await _api.CallAsync<PyDict>("get_smart_recommendation");
        Assert.NotNull(rec);
    });
}
