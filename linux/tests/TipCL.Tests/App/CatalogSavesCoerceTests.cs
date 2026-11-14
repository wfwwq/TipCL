// 真机缺陷回归:下载页「装进存档」那个下拉永远打不开(数据包本身装得进去,只是不会进存档)。
//
// 复核出来的链路(四步,每一步都在下面有用例):
//   1) Core 的 list_saves 把 mtime 返回成 **JSON 数字**(Saves.cs:57 → LocalFs.MTimeInt,类型是 long);
//   2) App 侧的 SaveRow.Mtime 声明成 **string**;
//   3) Api.Coerce(序列化 → 反序列化)因此抛 JsonException,旧的 catch 把它整个吞掉并返回 null;
//   4) TryCallAsync 于是回落到空列表,CatalogPage.PickSaveAsync 的
//      `if (saves.Count == 0) return extra;` 提前返回 —— 下拉连内容都没有,更不会弹。
//
// 红证据(修 SaveRow 之前):ListSaves_TryCall_CoercesIntoNonEmptySaveRows 会失败,失败信息里带
// AppLog 尾部,能看到 `JsonException: The JSON value could not be converted to System.String`。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TipCL.App.Common;
using TipCL.App.Pages.Download;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.App;

public class CatalogSavesCoerceTests : IDisposable
{
    private readonly IApi _api = InProcessApi.Instance;
    private readonly string _inst = "sv-" + Guid.NewGuid().ToString("N").Substring(0, 8);

    public CatalogSavesCoerceTests()
    {
        // 日志落在 TestIsolation 指好的临时 TIPCL_HOME 里,不碰真实用户目录(AppLog.Init 幂等)
        AppLog.Init();
        TestApp.Run(() => _api.CallAsync<object>("create_instance", new { name = _inst }).GetAwaiter().GetResult());
        // 造一个真存档目录:list_saves 只列 <实例>/saves/ 下非隐藏的子目录
        string world = Path.Combine(Instances.GetInstancePath(_inst), "saves", "测试世界");
        Directory.CreateDirectory(world);
        File.WriteAllText(Path.Combine(world, "level.dat"), "x");
    }

    public void Dispose()
    {
        TestApp.Run(() =>
        {
            try { _api.CallAsync<object>("delete_instance", new { name = _inst }).GetAwaiter().GetResult(); }
            catch (Exception) { }
        });
    }

    /// <summary>AppLog 尾部若干字节:断言失败时把它带出来,免得「不知道为什么空」。</summary>
    private static string LogTail(int bytes = 1500)
    {
        try
        {
            string? path = AppLog.FilePath;
            if (path == null || !File.Exists(path)) return "(AppLog 未初始化)";
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long skip = Math.Max(0, stream.Length - bytes);
            stream.Seek(skip, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            return "(读日志失败: " + ex.Message + ")";
        }
    }

    // ───────────────────────── 1) Core 那边的真实形状 ─────────────────────────

    /// <summary>
    /// list_saves 的 mtime 是数字(epoch 秒),不是字符串 —— 这条钉住「契约形状」,
    /// 免得以后有人为了迁就 POCO 去改 Core 的返回类型(那是改错了地方)。
    /// </summary>
    [Fact]
    public void ListSavesMtime_IsANumber_NotAString() => TestApp.RunAsync(async () =>
    {
        var rows = await _api.CallAsync<List<object>>("list_saves", new { instance = _inst, version = "" });
        Assert.NotNull(rows);
        Assert.Single(rows!);
        object? mtime = Py.D(rows![0]).Get("mtime");
        Assert.NotNull(mtime);
        Assert.IsNotType<string>(mtime);
        Assert.True(Py.Int(mtime) > 0, "mtime 应该是正的 epoch 秒,实际: " + Py.Str(mtime));
    });

    // ───────────────────────── 2) 能被 Coerce 成非空列表 ─────────────────────────

    /// <summary>
    /// 缺陷本体:TryCallAsync 的 POCO 路径必须能把这批行落成 SaveRow。
    /// 修之前这里拿到的永远是 null(Coerce 静默失败 → 回落 fallback),所以 Count 断言必红。
    /// </summary>
    [Fact]
    public void ListSaves_TryCall_CoercesIntoNonEmptySaveRows() => TestApp.RunAsync(async () =>
    {
        List<SaveRow>? saves = await _api.TryCallAsync<List<SaveRow>>("list_saves", null,
            new { instance = _inst, version = "" });
        Assert.NotNull(saves);
        Assert.True(saves!.Count > 0, "list_saves 经 Coerce 后是空列表 —— 下拉因此不会弹。AppLog 尾部:\n" + LogTail());
        Assert.Equal("测试世界", saves[0].Name);
        // 真实的 mtime 是 epoch 秒,SaveRow 要能接住(数字与数字字符串都算)
        Assert.True(saves[0].Mtime > 0, "Mtime 没解析出来: " + saves[0].Mtime);
    });

    /// <summary>
    /// 抛异常的那条路(CallAsync 而不是 TryCallAsync):Coerce 失败时它会抛 InvalidCastException。
    /// 这条比上一条更硬 —— 它不依赖 fallback 语义,失败就是测试直接红。
    /// </summary>
    [Fact]
    public void ListSaves_CallAsync_CoercesIntoNonEmptySaveRows() => TestApp.RunAsync(async () =>
    {
        List<SaveRow>? saves = await _api.CallAsync<List<SaveRow>>("list_saves", new { instance = _inst, version = "" });
        Assert.NotNull(saves);
        Assert.NotEmpty(saves!);
    });

    // ───────────────────────── 3) 下拉判定那一步 ─────────────────────────

    /// <summary>
    /// CatalogPage.PickSaveAsync(CatalogPage.cs:728-729)的判据就是
    /// `if (saves.Count == 0) return extra;` —— 空列表直接提前返回,下拉(以及那个对话框)
    /// 根本不会建。这里按同一判据断言「非空 ⇒ 会往下走到弹下拉」。
    /// </summary>
    [Fact]
    public void SaveListIsNonEmpty_SoTheDropdownStepDoesNotBail() => TestApp.RunAsync(async () =>
    {
        List<SaveRow>? saves = await _api.TryCallAsync<List<SaveRow>>("list_saves", null,
            new { instance = _inst, version = "" }) ?? new();
        Assert.False(saves.Count == 0,
            "saves.Count == 0 ⇒ CatalogPage.cs:729 提前 return,「装进存档」下拉永远不会弹。AppLog 尾部:\n" + LogTail());
        // 下拉里的候选 = 「不装进存档」+ 每个存档名(与 CatalogPage.cs:730 同形)
        var names = new[] { "不装进存档" }.Concat(saves.Select(s => s.Name)).ToArray();
        Assert.Equal(new[] { "不装进存档", "测试世界" }, names);
    });

    // ───────────────────────── 4) 失败不再静默 ─────────────────────────

    /// <summary>
    /// Coerce 的 catch 必须留痕:拿一个类型上根本对不上的目标(string)去转 list_saves,
    /// 调用方拿到 InvalidCastException,日志里必须有一条带目标类型与原因的 WARN。
    /// </summary>
    [Fact]
    public void CoerceFailure_IsLogged_InsteadOfSilent() => TestApp.RunAsync(async () =>
    {
        await Assert.ThrowsAsync<InvalidCastException>(() =>
            _api.CallAsync<string>("list_saves", new { instance = _inst, version = "" }));

        string tail = LogTail(4000);
        Assert.Contains("API 结果转换失败", tail);
        Assert.Contains("System.String", tail);
    });
}