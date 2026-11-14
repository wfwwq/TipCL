// 任务 A(导入多选)回归:三处「导入」入口 —— 下载页「导入本地文件」、反馈页「添加附件」、
// 模组对话框「导入模组」——都必须能把多选到的每个文件逐个导入,并把成败汇总成人能读的一句话。
// 文件选择不弹系统对话框:用 Dlg.PickFilesOverride 注入选择结果(生产代码从不设置它);
// 导入本身走真实链路,源文件与落盘位置都是真实临时目录/真实实例目录,不 mock 文件系统。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.App.Common;
using TipCL.App.Pages.Download;
using TipCL.App.Pages.Feedback;
using TipCL.App.Pages.Version;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.App;

public class MultiPickImportTests : IDisposable
{
    private readonly IApi _api = InProcessApi.Instance;
    private readonly string _inst = "mp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    private readonly string _src;

    public MultiPickImportTests()
    {
        TestApp.Run(() => _api.CallAsync<object>("create_instance", new { name = _inst }).GetAwaiter().GetResult());
        _src = Path.Combine(Path.GetTempPath(), "tipcl-mp-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(_src);
    }

    public void Dispose()
    {
        TestApp.Run(() =>
        {
            try { _api.CallAsync<object>("delete_instance", new { name = _inst }).GetAwaiter().GetResult(); }
            catch (Exception) { }
        });
        try { Directory.Delete(_src, true); } catch (Exception) { }
    }

    // ───────────────────────────── 夹具 ─────────────────────────────

    private string InstDir() => Instances.GetInstancePath(_inst);

    private string DataPacks() => Path.Combine(InstDir(), "datapacks");

    private string Mods() => Path.Combine(InstDir(), "mods");

    /// <summary>造一个真实临时源文件(内容随便:三处入口都只按名字搬运,不解析内容)。</summary>
    private string SrcFile(string name, string content)
    {
        string p = Path.Combine(_src, name);
        File.WriteAllText(p, content);
        return p;
    }

    /// <summary>注入这一次的文件选择结果(空数组 = 用户取消)。</summary>
    private sealed class Pick : IDisposable
    {
        private readonly Func<string, string[], string[]>? _prev;

        public Pick(string[] files, Action<string[]>? onCall = null)
        {
            _prev = Dlg.PickFilesOverride;
            Dlg.PickFilesOverride = (_, patterns) => { onCall?.Invoke(patterns); return files; };
        }

        public void Dispose() => Dlg.PickFilesOverride = _prev;
    }

    /// <summary>页面未挂载外壳时「实例」下拉是空的,直接把实例塞进去(等价用户选中该实例)。</summary>
    private static void SetInstance(CatalogPage page, string inst)
    {
        var field = typeof(CatalogPage).GetField("_inst", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        var combo = (ComboBox)field!.GetValue(page)!;
        combo.Items.Clear();
        combo.Items.Add(inst);
        combo.SelectedIndex = 0;
    }

    private static int Attachments(FeedbackView view)
    {
        var field = typeof(FeedbackView).GetField("_attachments", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return ((List<string>)field!.GetValue(view)!).Count;
    }

    /// <summary>等待落盘:install_* 是「后台任务 + 立刻返回 task_id」,搬运在后台线程完成。</summary>
    private static async Task WaitForAsync(Func<bool> ready, string why)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (ready()) return;
            await Task.Delay(60);
        }
        Assert.True(ready(), why);
    }

    private string[] DataPackZips() => Directory.Exists(DataPacks()) ? Directory.GetFiles(DataPacks(), "*.zip") : Array.Empty<string>();
    private string[] ModJars() => Directory.Exists(Mods()) ? Directory.GetFiles(Mods(), "*.jar") : Array.Empty<string>();

    // ───────────────────────── 1) Dlg.PickFiles 契约 ─────────────────────────

    /// <summary>多选返回全部选中项;取消(空选择)返回**空数组**而不是 null。</summary>
    [Fact]
    public void PickFiles_Cancel_ReturnsEmptyArray_NotNnull() => TestApp.RunAsync(async () =>
    {
        using (new Pick(Array.Empty<string>()))
        {
            var canceled = await Dlg.PickFiles("导入模组", "*.jar");
            Assert.NotNull(canceled);
            Assert.Empty(canceled);
        }
        using (new Pick(new[] { "a.jar", "b.jar" }))
        {
            var picked = await Dlg.PickFiles("导入模组", "*.jar");
            Assert.Equal(new[] { "a.jar", "b.jar" }, picked);
        }
    });

    /// <summary>下载页把该分段的文件类型过滤器原样交给多选(与 PickFile 同形的 patterns)。</summary>
    [Fact]
    public void Catalog_ImportLocal_PassesKindPatternsToPicker() => TestApp.RunAsync(async () =>
    {
        var page = new CatalogPage(CatalogKind.Datapack);
        SetInstance(page, _inst);
        string[]? seen = null;
        using (new Pick(Array.Empty<string>(), patterns => seen = patterns))
        {
            await page.ImportLocalAsync();
        }
        Assert.Equal(new[] { "*.zip" }, seen);
    });

    // ───────────────────── 2) 下载页「导入本地文件」 ─────────────────────

    /// <summary>多选两个 zip:两个都要进实例 datapacks 目录(逐个导入,不是只认第一个)。</summary>
    [Fact]
    public void Catalog_ImportLocal_TwoFiles_ImportsEach() => TestApp.RunAsync(async () =>
    {
        var page = new CatalogPage(CatalogKind.Datapack);
        SetInstance(page, _inst);
        string a = SrcFile("mp-pack-a.zip", "A");
        string b = SrcFile("mp-pack-b.zip", "B");

        using (new Pick(new[] { a, b }))
        {
            await page.ImportLocalAsync();
        }

        await WaitForAsync(() => DataPackZips().Length == 2, "两个数据包都要落进实例 datapacks 目录");
        Assert.Contains("mp-pack-a.zip", DataPackZips().Select(Path.GetFileName));
        Assert.Contains("mp-pack-b.zip", DataPackZips().Select(Path.GetFileName));
    });

    /// <summary>单选一个 zip:行为与改动前一致 —— 只装这一个,不多不少。</summary>
    [Fact]
    public void Catalog_ImportLocal_SingleFile_ImportsExactlyOne() => TestApp.RunAsync(async () =>
    {
        var page = new CatalogPage(CatalogKind.Datapack);
        SetInstance(page, _inst);
        string a = SrcFile("mp-pack-one.zip", "A");

        using (new Pick(new[] { a }))
        {
            await page.ImportLocalAsync();
        }

        await WaitForAsync(() => DataPackZips().Length == 1, "单选只装一个数据包");
        Assert.Equal("mp-pack-one.zip", Path.GetFileName(DataPackZips()[0]));
    });

    /// <summary>取消(空数组):什么都不做、不报错(没有新文件落盘)。</summary>
    [Fact]
    public void Catalog_ImportLocal_Cancel_DoesNothing() => TestApp.RunAsync(async () =>
    {
        var page = new CatalogPage(CatalogKind.Datapack);
        SetInstance(page, _inst);
        using (new Pick(Array.Empty<string>()))
        {
            await page.ImportLocalAsync();
        }
        await Task.Delay(200);
        Assert.Empty(DataPackZips());
    });

    // ───────────────────── 3) 反馈页「添加附件」 ─────────────────────

    /// <summary>多选两个附件:两个都上传到暂存目录(逐个上传)。</summary>
    [Fact]
    public void Feedback_Attach_TwoFiles_BothUploaded() => TestApp.RunAsync(async () =>
    {
        var view = new FeedbackView();
        string a = SrcFile("mp-log-a.log", "hello");
        string b = SrcFile("mp-shot-b.png", "png-bytes");

        using (new Pick(new[] { a, b }))
        {
            await view.AttachAsync();
        }

        string uploads = Path.Combine(Utils.ROOT, "uploads");
        Assert.True(File.Exists(Path.Combine(uploads, "mp-log-a.log")), "第一个附件没上传");
        Assert.True(File.Exists(Path.Combine(uploads, "mp-shot-b.png")), "第二个附件没上传");
        Assert.Equal(2, Attachments(view));
    });

    /// <summary>单选一个附件:与改动前一致 —— 附件列表里正好一个。</summary>
    [Fact]
    public void Feedback_Attach_SingleFile_AddsExactlyOne() => TestApp.RunAsync(async () =>
    {
        var view = new FeedbackView();
        string a = SrcFile("mp-log-one.log", "hello");

        using (new Pick(new[] { a }))
        {
            await view.AttachAsync();
        }

        Assert.Equal(1, Attachments(view));
        Assert.True(File.Exists(Path.Combine(Utils.ROOT, "uploads", "mp-log-one.log")));
    });

    /// <summary>取消(空数组):什么都不做、不报错。</summary>
    [Fact]
    public void Feedback_Attach_Cancel_DoesNothing() => TestApp.RunAsync(async () =>
    {
        var view = new FeedbackView();
        using (new Pick(Array.Empty<string>()))
        {
            await view.AttachAsync();
        }
        Assert.Equal(0, Attachments(view));
    });

    // ───────────────────── 4) 模组对话框「导入模组」 ─────────────────────

    /// <summary>多选两个 jar:两个都要排队装进实例 mods 目录。</summary>
    [Fact]
    public void ModsDialog_Import_TwoFiles_ImportsEach() => TestApp.RunAsync(async () =>
    {
        string a = SrcFile("mp-mod-a.jar", "jarA");
        string b = SrcFile("mp-mod-b.jar", "jarB");

        ImportBatch batch;
        using (new Pick(new[] { a, b }))
        {
            batch = await ModsDialog.ImportJarsFromPickerAsync(_api, _inst);
        }

        Assert.Equal(2, batch.Total);
        Assert.Equal(2, batch.OkCount);
        Assert.False(batch.AnyFailed);
        await WaitForAsync(() => ModJars().Length == 2, "两个 jar 都要落进实例 mods 目录");
    });

    /// <summary>单选一个 jar:与改动前一致(一个任务、一个文件、名字正确)。</summary>
    [Fact]
    public void ModsDialog_Import_SingleFile_QueuesOne() => TestApp.RunAsync(async () =>
    {
        string a = SrcFile("mp-mod-one.jar", "jarA");

        ImportBatch batch;
        using (new Pick(new[] { a }))
        {
            batch = await ModsDialog.ImportJarsFromPickerAsync(_api, _inst);
        }

        Assert.Equal(1, batch.Total);
        Assert.Equal("mp-mod-one.jar", batch.Ok[0]);
        await WaitForAsync(() => ModJars().Length == 1, "单选只装一个 jar");
    });

    /// <summary>取消(空数组):一个任务都不排,也不报错。</summary>
    [Fact]
    public void ModsDialog_Import_Cancel_DoesNothing() => TestApp.RunAsync(async () =>
    {
        ImportBatch batch;
        using (new Pick(Array.Empty<string>()))
        {
            batch = await ModsDialog.ImportJarsFromPickerAsync(_api, _inst);
        }

        Assert.True(batch.Canceled);
        Assert.Equal(0, batch.OkCount);
        Assert.False(batch.AnyFailed);
        await Task.Delay(200);
        Assert.Empty(ModJars());
    });

    // ───────────────────── 5) 汇总口径 ─────────────────────

    /// <summary>多选汇总:成功/失败个数 + 失败是哪个文件、为什么;单选与取消不加汇总。</summary>
    [Fact]
    public void ImportBatch_Summary_ListsFailuresByNameAndReason()
    {
        var many = new ImportBatch(3);
        many.Success("a.zip");
        many.Success("b.zip");
        many.Failure("c.zip", "数据包不存在: c.zip");
        Assert.Equal("导入 3 个:成功 2,失败 1\nc.zip:数据包不存在: c.zip", many.SummaryText());

        var ok = new ImportBatch(2);
        ok.Success("a.zip");
        ok.Success("b.zip");
        Assert.Equal("导入 2 个:成功 2", ok.SummaryText());

        // 单个文件 / 取消都不加汇总:这两种情况由入口按改动前的原样提示
        var one = new ImportBatch(1);
        one.Failure("x.zip", "boom");
        Assert.Equal("", one.SummaryText());
        Assert.Equal("", new ImportBatch(0).SummaryText());
    }

    /// <summary>导入失败的文件名与原因都要能被读出来(不是一句「导入失败」)。</summary>
    [Fact]
    public void ImportBatch_Failure_KeepsNameAndReason()
    {
        var batch = new ImportBatch(2);
        batch.Success("ok.jar");
        batch.Failure("bad.jar", "本地文件只支持 .jar 模组");
        Assert.Equal(1, batch.OkCount);
        Assert.Equal(1, batch.FailedCount);
        Assert.Equal("bad.jar", batch.Failures[0].Name);
        Assert.Equal("本地文件只支持 .jar 模组", batch.Failures[0].Reason);
    }
}
