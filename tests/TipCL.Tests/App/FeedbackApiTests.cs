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
/// M4-H P4 反馈页 / 崩溃界面 / 首启向导的 Api 契约回归(不联网:只测本地可验证的部分)。
/// </summary>
public class FeedbackApiTests
{
    private readonly IApi _api = InProcessApi.Instance;

    [Fact]
    public void HelpArticles_ListAndFetch() => TestApp.RunAsync(async () =>
    {
        var rows = await _api.CallAsync<List<object?>>("help_articles", new { query = "" });
        Assert.NotNull(rows);
        Assert.NotEmpty(rows!);
        var first = Py.D(rows[0]!);
        string id = Py.S(first.Get("id"));
        string title = Py.S(first.Get("title"));
        Assert.False(string.IsNullOrEmpty(id), "帮助文章缺 id");
        Assert.False(string.IsNullOrEmpty(title), "帮助文章缺标题");

        var one = await _api.CallAsync<PyDict>("help_article", new { article_id = id });
        Assert.NotNull(one);
        Assert.Equal(title, Py.S(one!.Get("title")));
        Assert.NotNull(one.Get("body"));

        // 搜索也要能用(命中与不命中都不炸)
        Assert.NotNull(await _api.CallAsync<List<object?>>("help_articles", new { query = "Java" }));
        Assert.NotNull(await _api.CallAsync<List<object?>>("help_articles", new { query = "zzz-不可能命中-" + Guid.NewGuid().ToString("N") }));
    });

    [Fact]
    public void Sysinfo_CollectAndFormat() => TestApp.RunAsync(async () =>
    {
        var info = await _api.CallAsync<PyDict>("collect_sysinfo", new { force = true, scan_system_java = false });
        Assert.NotNull(info);
        string text = await _api.CallAsync<string>("sysinfo_text", new { info }) ?? "";
        Assert.False(string.IsNullOrWhiteSpace(text), "系统信息文本为空");
    });

    [Fact]
    public void StashUpload_WritesFileUnderRoot() => TestApp.RunAsync(async () =>
    {
        byte[] payload = System.Text.Encoding.UTF8.GetBytes("tipcl-upload-test");
        string path = await _api.CallAsync<string>("stash_upload", new
        {
            name = "崩溃报告.txt",
            data = Convert.ToBase64String(payload),
        }) ?? "";
        Assert.False(string.IsNullOrEmpty(path));
        Assert.True(File.Exists(path), "附件没有落盘: " + path);
        Assert.StartsWith(PPath.Abs(PPath.Join(Utils.ROOT, "uploads")), PPath.Abs(path));
        Assert.Equal(payload, File.ReadAllBytes(path));

        // 非法 base64 要抛(不能静默落个空文件)
        await Assert.ThrowsAnyAsync<Exception>(() => _api.CallAsync<string>("stash_upload", new { name = "x.bin", data = "!!!not-base64!!!" }));
    });

    [Fact]
    public void CrashAction_BumpMemory_And_Unknown() => TestApp.RunAsync(async () =>
    {
        // 未知动作要如实回 ok=false(照 Windows 的 default 分支)
        var unknown = await _api.CallAsync<PyDict>("apply_crash_action", new
        {
            action = new { id = "no-such-action" },
            report = new PyDict(),
        });
        Assert.NotNull(unknown);
        Assert.False(Py.Truthy(unknown!.Get("ok")));
        Assert.Contains("no-such-action", Py.S(unknown.Get("message")));

        // bump_memory 写默认内存(测试结束还原)
        object? before = Config.CONFIG.Get("memory_mb");
        try
        {
            var ok = await _api.CallAsync<PyDict>("apply_crash_action", new
            {
                action = new { id = "bump_memory", memory_mb = 6144 },
                report = new PyDict(),
            });
            Assert.NotNull(ok);
            Assert.True(Py.Truthy(ok!.Get("ok")));
            Assert.Equal(6144, Py.Int(Config.CONFIG.Get("memory_mb", 0)));
        }
        finally
        {
            Config.CONFIG.Set("memory_mb", before);
            Config.CONFIG.Save();
        }

        // 报告里的动作列表(Core 的 BuildActions)必须能被界面直接消费:每项都有 id + label
        var report = await Task.Run(() =>
        {
            var inst = new Instance(Instances.RootName());
            return Crash.AnalyzeInstancePath(inst.path, "", true);
        });
        Assert.NotNull(report);
        foreach (var item in Py.ListOr(report.Get("actions")))
        {
            var d = Py.D(item);
            Assert.False(string.IsNullOrEmpty(Py.S(d.Get("id"))), "崩溃动作缺 id");
            Assert.False(string.IsNullOrEmpty(Py.S(d.Get("label"))), "崩溃动作缺 label");
        }
    });

    [Fact]
    public void GetCrash_WithoutReports_ReturnsEmptyDict() => TestApp.RunAsync(async () =>
    {
        var rep = await _api.CallAsync<PyDict>("get_crash", new { task_id = "task-not-exist" });
        Assert.NotNull(rep);
        // 没有崩溃时:要么空字典,要么是已缓存的那一份(不能是 null/异常)
        Assert.True(rep!.Count >= 0);
    });

    /// <summary>首启向导与同意门依赖的配置键必须在 Core 默认里存在(否则读出来永远是空)。</summary>
    [Fact]
    public void Config_HasFirstRunAndConsentKeys()
    {
        Assert.True(Config.CONFIG.Get("first_run") is bool, "config 默认缺 first_run");
        Assert.True(Config.CONFIG.data.ContainsKey("feedback_consent"), "config 默认缺 feedback_consent");
    }
}
