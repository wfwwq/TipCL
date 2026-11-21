// R16 线上新闻:News 的数据语义(行字段 / 12 条上限 / 160 字截断 / 缓存往返 / 失败降级)。
// 全程不触网:失败路径用「已取消的 DownloadManager」让 FetchJson 在发请求前就抛,
// 与断网 / 超时 / 源返回坏 JSON 走的是同一条 catch(都落在 Fetch 的降级分支上)。
// 缓存文件默认在 TestIsolation 的隔离 TIPCL_HOME 里 —— 但**那是全进程共享的一份**:
// 启动页那条后台「线上新闻」会真发 HTTP 并在成功时写它(LaunchPage.cs:891 → fetch_news → News.Fetch),
// 于是「坏缓存必须是空」这类断言会被那条后台抓取偶尔污染成非空(实测:全量跑里红过两次,
// 报 5+ 条真实新闻;单独跑本类 5/5 绿)。所以本类把自己的缓存指到**私有临时文件**上,
// 用 News.CachePathOverride 这条缝隙,R21f 起生效。
//
// R30-WP6a(返工单 R-WP5-02):**光把缓存换成私有文件还不够**。那条后台抓取是别的用例留下的
// fire-and-forget 任务,它在**任意时刻**完成,而写盘时读的是「当时生效的那份 News.CACHE」——
// 也就是本类的私有文件;进程级 static 覆盖既挡不住它、也换不来互斥。于是「写 1 条 → 读」之间
// 会被整文件替换成 12 条真实新闻(News.cs:23 MAX_ROWS = 12),Assert.Single 报
// 「The collection contained 12 items」(总验收:Linux 全量 9 轮里红 1 轮;单跑本类 10/10 绿)。
// 修法(仍然只在测试侧):每次观察都在**产品自己那把按路径的锁**里做 ——
// `Utils.JsonFileLock(News.CACHE)` 就是 `Utils.ReadJson` / `Utils.WriteJson` 读写这个文件时用的那把锁,
// 所以后台抓取的 `WriteCache` 只能等我们这一步做完,文件内容在观察窗口内只可能是我们摆的那一份;
// 锁内再按字节复核一次(见 Observe / AssertCacheIs),这条复核是**机制守卫**:
// 将来谁把锁去掉、或者换一条绕开 Utils.WriteJson 的写盘路径,这条用例会当场红,而不是偶发红一次。
// 断言一条没放宽、没有重试、没有 Skip。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.Core;

[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class NewsTests : IDisposable
{
    private static readonly string PrivateCache =
        Path.Combine(Path.GetTempPath(), "tipcl-news-tests-" + Guid.NewGuid().ToString("N") + ".json");

    public NewsTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PrivateCache)!);
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
    // ————————————————— 缓存文件上的观察(受保护) —————————————————

    /// <summary>本类私有缓存文件的绝对路径(= 本用例期间生效的那份 News.CACHE)。</summary>
    private static string CacheFile => PPath.Abs(News.CACHE);

    /// <summary>
    /// 在私有缓存文件上做一次**受保护**的观察,顺序是:
    ///   1) 先拿产品读写这个文件用的那把按路径锁(<see cref="Utils.JsonFileLock"/>)——
    ///      启动页残留的后台抓取(它写盘走 <c>Utils.WriteJson</c>)此刻只能等,
    ///      所以它在本次观察期间**进不来**;
    ///   2) 摆好这次要看的状态:establish 返回「盘上应该长什么样」的基准字节
    ///      (null = 文件必须不存在),摆盘也走产品的读写路径;
    ///   3) 调 SUT;
    ///   4) 按字节复核文件仍是「我们摆的那一份」—— 这是机制守卫,不是容忍:
    ///      若有人绕开那把锁改写它,这里当场红(而不是让断言去读到别人的内容)。
    /// 对**返回值**的断言都在锁外做,和以前一字不差。
    /// </summary>
    private static T Observe<T>(Func<byte[]?> establish, Func<T> body, string label)
    {
        lock (Utils.JsonFileLock(News.CACHE))
        {
            byte[]? expected = establish();
            T result = body();
            AssertCacheIs(expected, label);
            return result;
        }
    }

    /// <summary>机制守卫:观察期间文件必须一直是我们摆的那份(见 <see cref="Observe"/> 的注释)。</summary>
    private static void AssertCacheIs(byte[]? expected, string label)
    {
        if (expected is null)
        {
            Assert.False(File.Exists(CacheFile),
                label + ":观察期间有人往本用例的私有缓存里写了东西(" + CacheFile + ")");
            return;
        }
        Assert.True(File.Exists(CacheFile),
            label + ":观察期间本用例的私有缓存不见了(" + CacheFile + ")");
        Assert.True(File.ReadAllBytes(CacheFile).AsSpan().SequenceEqual(expected),
            label + ":观察期间本用例的私有缓存被别人改写了(" + CacheFile + ")");
    }

    /// <summary>摆好一份 JSON 载荷(走产品同一条写盘路径 Utils.WriteJson),并返回盘上的基准字节。</summary>
    private static byte[] WritePayload(object payload)
    {
        Utils.WriteJson(News.CACHE, payload);
        return File.ReadAllBytes(CacheFile);
    }

    /// <summary>摆好一份「坏内容 / 非 UTF-8」的原始字节,并返回同一份字节当基准。</summary>
    private static byte[] WriteRawBytes(byte[] bytes)
    {
        File.WriteAllBytes(CacheFile, bytes);
        return bytes;
    }

    /// <summary>摆好「缓存文件不存在」,基准 = null。</summary>
    private static byte[]? Absent()
    {
        if (File.Exists(CacheFile)) File.Delete(CacheFile);
        return null;
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
        // 1) 缓存不存在
        Assert.Empty(Observe(Absent, () => News.LoadCached(), "缓存不存在"));

        // 2) 坏 JSON
        Assert.Empty(Observe(() => WriteRawBytes(Encoding.UTF8.GetBytes("{ 这不是 JSON")),
            () => News.LoadCached(), "坏 JSON"));

        // 3) 非 UTF-8 字节(Utils.ReadJson 对这类内容是原样抛的,这里必须吞掉)
        Assert.Empty(Observe(() => WriteRawBytes(new byte[] { 0xFF, 0xFE, 0x00, 0x41 }),
            () => News.LoadCached(), "非 UTF-8"));

        // 4) 合法 JSON 但结构不对
        Assert.Empty(Observe(() => WriteRawBytes(Encoding.UTF8.GetBytes("{\"entries\":\"oops\"}")),
            () => News.LoadCached(), "结构不对"));
        Assert.Empty(Observe(() => WriteRawBytes(Encoding.UTF8.GetBytes("12345")),
            () => News.LoadCached(), "顶层是数字"));
    }

    [Fact]
    public void LoadCached_RoundTripsRows_AndAcceptsRawPayload()
    {
        // 线上写盘的形态:行数组 → 原样读回
        List<object> rows = Observe(
            () => WritePayload(Py.L(PyDict.Of("title", "缓存标题", "body", "缓存正文", "version", "26.4", "image", "/v2/i.jpg", "date", "2026-11-10"))),
            () => News.LoadCached(), "行数组往返");
        Assert.Single(rows);
        Assert.Equal("缓存标题", Py.S(((PyDict)rows[0]).Get("title")));
        Assert.Equal("缓存正文", Py.S(((PyDict)rows[0]).Get("body")));

        // 也有人手放整份载荷进去 → 现解析
        List<object> wrapped = Observe(
            () => WritePayload(PyDict.Of("entries", Py.L(PyDict.Of("title", "包在 entries 里")))),
            () => News.LoadCached(), "整份载荷");
        Assert.Equal("包在 entries 里", Py.S(((PyDict)wrapped[0]).Get("title")));
    }

    // ————————————————— 抓取失败的分级 —————————————————

    [Fact]
    public void Fetch_WhenSourceUnavailable_FallsBackToCacheThenToEmpty()
    {
        // 取消中的 DownloadManager:FetchJson 第一步 RaiseIfCancel 就抛(不发请求、不触网),
        // 与断网 / 超时 / 两条源都失败走的是同一条降级分支
        var dead = new DownloadManager(2L, cancel: () => true);

        // 1) 拿不到 + 没有缓存 → 空列表(页面照旧显示「暂无新闻」),绝不抛
        Assert.Empty(Observe(Absent, () => News.Fetch(dead), "抓取失败且无缓存"));

        // 2) 拿不到 + 有缓存 → 回落缓存
        //    「回落缓存不该改写缓存文件」这条现在由 Observe 的字节复核担保(锁内保证没人插进来)
        var (cached, again) = Observe(
            () => WritePayload(Py.L(PyDict.Of("title", "上次抓到的"))),
            () => (News.Fetch(dead), News.LoadCached()), "回落缓存");
        Assert.Single(cached);
        Assert.Equal("上次抓到的", Py.S(((PyDict)cached[0]).Get("title")));
        Assert.Equal("上次抓到的", Py.S(((PyDict)again[0]).Get("title")));

        // 3) 缓存是坏的 → 同样只回空,不抛
        Assert.Empty(Observe(() => WriteRawBytes(Encoding.UTF8.GetBytes("{ 坏缓存")),
            () => News.Fetch(dead), "坏缓存"));
    }

    [Fact]
    public void CachePath_IsUnderRootCacheDir()
    {
        Assert.Equal(PPath.Abs(PPath.Join(Utils.ROOT, "cache", "news.json")), DefaultCachePath());
    }
}
