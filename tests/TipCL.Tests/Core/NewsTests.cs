// R16 线上新闻:News 的数据语义(行字段 / 12 条上限 / 160 字截断 / 缓存往返 / 失败降级)。
// 全程不触网:失败路径用「已取消的 DownloadManager」让 FetchJson 在发请求前就抛,
// 与断网 / 超时 / 源返回坏 JSON 走的是同一条 catch(都落在 Fetch 的降级分支上)。
// 缓存文件默认在 TestIsolation 的隔离 TIPCL_HOME 里 —— 但**那是全进程共享的一份**:
// 启动页那条后台「线上新闻」会真发 HTTP 并在成功时写它(LaunchPage.cs:885 → fetch_news → News.Fetch),
// 于是「坏缓存必须是空」这类断言会被那条后台抓取偶尔污染成非空(实测:全量跑里红过两次,
// 报 5+ 条真实新闻;单独跑本类 5/5 绿)。所以本类把自己的缓存指到**私有临时文件**上,
// 用 News.CachePathOverride 这条缝隙,R21f 起生效。
using System;
using System.Collections.Generic;
using System.IO;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.Core;

public class NewsTests : IDisposable
{
    private static readonly string PrivateCache =
        Path.Combine(Path.GetTempPath(), "tipcl-news-tests-" + Guid.NewGuid().ToString("N") + ".json");

    public NewsTests()
    {
        News.CachePathOverride = PrivateCache;
        if (File.Exists(PrivateCache)) File.Delete(PrivateCache);
    }

    public void Dispose()
    {
        News.CachePathOverride = "";
        try { if (File.Exists(PrivateCache)) File.Delete(PrivateCache); } catch (Exception) { }
    }

    /// <summary>
    /// 隔离性守卫:本类的缓存**不许**是那个全进程共享的默认文件 —— 一旦有人把这条缝隙去掉,
    /// 这条用例立刻红(而不是等某次全量跑出个偶发失败)。
    /// </summary>
    [Fact]
    public void CachePath_IsPrivateToThisTestClass()
    {
        Assert.Equal(PrivateCache, News.CACHE);          // 原样用我们设的那份
        // 与「默认那份」比:默认值由 DefaultCachePath() 在同一套表示下取,免得被短路径/长路径这类差异误判
        Assert.NotEqual(DefaultCachePath(), PPath.Abs(News.CACHE));
    }

    /// <summary>临时摘掉覆盖,取「默认那份」缓存的绝对路径(用完原样还原)。</summary>
    private static string DefaultCachePath()
    {
        string saved = News.CachePathOverride;
        try
        {
            News.CachePathOverride = "";
            return PPath.Abs(News.CACHE);
        }
        finally
        {
            News.CachePathOverride = saved;
        }
    }
    // ————————————————— 载荷 → 行 —————————————————

    [Fact]
    public void RowsFrom_MapsMojangEntries_WithFieldsAndTwelveRowCap()
    {
        var entries = new List<object>();
        for (int i = 0; i < 15; i++)
        {
            entries.Add(PyDict.Of(
                "title", "标题" + i,
                "version", "26." + i,
                "shortText", new string('字', 200),
                "image", PyDict.Of("url", "/v2/images/" + i + ".jpg"),
                "date", "2026-11-10T14:20:05.000Z",
                "id", "id-" + i));
        }

        List<object> rows = News._rows_from(PyDict.Of("entries", entries));

        Assert.Equal(12, rows.Count);
        var first = (PyDict)rows[0];
        Assert.Equal("标题0", Py.S(first.Get("title")));
        Assert.Equal("26.0", Py.S(first.Get("version")));
        Assert.Equal("/v2/images/0.jpg", Py.S(first.Get("image")));
        Assert.Equal("2026-11-10", Py.S(first.Get("date")));
        string body = Py.S(first.Get("body"));
        Assert.Equal(163, body.Length); // 160 字 + "..."
        Assert.EndsWith("...", body, StringComparison.Ordinal);
        // 12 条之后的那条不在里面
        Assert.DoesNotContain(rows, r => Py.S(((PyDict)r).Get("title")) == "标题12");
    }

    [Fact]
    public void RowsFrom_ReadsLegacyPatchNotesKey_AndFieldFallbacks()
    {
        // 老版 JSON:顶层 patchNotes;title 缺 → 用 version;shortText 缺 → 用 subtitle;
        // image 直接是字符串;date 缺 → 用 updated_at
        List<object> rows = News._rows_from(PyDict.Of("patchNotes", new List<object>
        {
            PyDict.Of(
                "version", "1.21.4",
                "subtitle", "短句",
                "image", "https://launchercontent.mojang.com/v2/images/1.21.4.jpg",
                "updated_at", "2026-01-02T03:04:05Z"),
        }));

        Assert.Single(rows);
        var row = (PyDict)rows[0];
        Assert.Equal("1.21.4", Py.S(row.Get("title")));
        Assert.Equal("短句", Py.S(row.Get("body")));
        Assert.Equal("https://launchercontent.mojang.com/v2/images/1.21.4.jpg", Py.S(row.Get("image")));
        Assert.Equal("2026-01-02", Py.S(row.Get("date")));

        // 顶层直接是数组(截断过的缓存)也认
        Assert.Single(News._rows_from(new List<object> { PyDict.Of("id", "26-4-snapshot-8") }));
    }

    [Fact]
    public void RowsFrom_BrokenPayload_ReturnsEmpty_WithoutThrowing()
    {
        Assert.Empty(News._rows_from(null));
        Assert.Empty(News._rows_from(new PyDict()));
        Assert.Empty(News._rows_from("oops"));
        Assert.Empty(News._rows_from(42L));
        // entries 不是数组 / 里面不是字典:跳过,不当异常
        Assert.Empty(News._rows_from(PyDict.Of("entries", "oops")));
        Assert.Empty(News._rows_from(PyDict.Of("entries", PyDict.Of("a", 1L))));
        Assert.Empty(News._rows_from(PyDict.Of("entries", new List<object> { "not-a-dict", 42L })));
        // 混合载荷:只收字典那几条
        Assert.Single(News._rows_from(new List<object> { "not-a-dict", PyDict.Of("title", "好的一条"), 42L }));
    }

    // ————————————————— 缓存读 —————————————————

    [Fact]
    public void LoadCached_MissingOrBrokenFile_ReturnsEmpty_WithoutThrowing()
    {
        WithCacheBackup(() =>
        {
            string file = PPath.Abs(News.CACHE);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);

            // 1) 缓存不存在
            if (File.Exists(file)) File.Delete(file);
            Assert.Empty(News.LoadCached());

            // 2) 坏 JSON
            File.WriteAllText(file, "{ 这不是 JSON");
            Assert.Empty(News.LoadCached());

            // 3) 非 UTF-8 字节(Utils.ReadJson 对这类内容是原样抛的,这里必须吞掉)
            File.WriteAllBytes(file, new byte[] { 0xFF, 0xFE, 0x00, 0x41 });
            Assert.Empty(News.LoadCached());

            // 4) 合法 JSON 但结构不对
            File.WriteAllText(file, "{\"entries\":\"oops\"}");
            Assert.Empty(News.LoadCached());
            File.WriteAllText(file, "12345");
            Assert.Empty(News.LoadCached());
        });
    }

    [Fact]
    public void LoadCached_RoundTripsRows_AndAcceptsRawPayload()
    {
        WithCacheBackup(() =>
        {
            // 线上写盘的形态:行数组 → 原样读回
            Utils.WriteJson(News.CACHE, Py.L(PyDict.Of("title", "缓存标题", "body", "缓存正文", "version", "26.4", "image", "/v2/i.jpg", "date", "2026-11-10")));
            List<object> rows = News.LoadCached();
            Assert.Single(rows);
            Assert.Equal("缓存标题", Py.S(((PyDict)rows[0]).Get("title")));
            Assert.Equal("缓存正文", Py.S(((PyDict)rows[0]).Get("body")));

            // 也有人手放整份载荷进去 → 现解析
            Utils.WriteJson(News.CACHE, PyDict.Of("entries", Py.L(PyDict.Of("title", "包在 entries 里"))));
            Assert.Equal("包在 entries 里", Py.S(((PyDict)News.LoadCached()[0]).Get("title")));
        });
    }

    // ————————————————— 抓取失败的分级 —————————————————

    [Fact]
    public void Fetch_WhenSourceUnavailable_FallsBackToCacheThenToEmpty()
    {
        // 取消中的 DownloadManager:FetchJson 第一步 RaiseIfCancel 就抛(不发请求、不触网),
        // 与断网 / 超时 / 两条源都失败走的是同一条降级分支
        var dead = new DownloadManager(2L, cancel: () => true);

        WithCacheBackup(() =>
        {
            string file = PPath.Abs(News.CACHE);

            // 1) 拿不到 + 没有缓存 → 空列表(页面照旧显示「暂无新闻」),绝不抛
            if (File.Exists(file)) File.Delete(file);
            Assert.Empty(News.Fetch(dead));

            // 2) 拿不到 + 有缓存 → 回落缓存
            Utils.WriteJson(News.CACHE, Py.L(PyDict.Of("title", "上次抓到的")));
            List<object> cached = News.Fetch(dead);
            Assert.Single(cached);
            Assert.Equal("上次抓到的", Py.S(((PyDict)cached[0]).Get("title")));
            // 回落缓存不该改写缓存文件
            Assert.Equal("上次抓到的", Py.S(((PyDict)News.LoadCached()[0]).Get("title")));

            // 3) 缓存是坏的 → 同样只回空,不抛
            File.WriteAllText(file, "{ 坏缓存");
            Assert.Empty(News.Fetch(dead));
        });
    }

    [Fact]
    public void CachePath_IsUnderRootCacheDir()
    {
        Assert.Equal(PPath.Abs(PPath.Join(Utils.ROOT, "cache", "news.json")), DefaultCachePath());
    }

    // ————————————————— 工具 —————————————————

    /// <summary>备份/还原缓存文件(同进程其它用例也可能读它)。</summary>
    private static void WithCacheBackup(Action body)
    {
        string file = PPath.Abs(News.CACHE);
        byte[] backup = File.Exists(file) ? File.ReadAllBytes(file) : Array.Empty<byte>();
        try
        {
            body();
        }
        finally
        {
            if (backup.Length > 0)
                File.WriteAllBytes(file, backup);
            else if (File.Exists(file))
                File.Delete(file);
        }
    }
}
