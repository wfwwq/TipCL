using System;
using System.IO;
using Avalonia.Controls;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C2c2:缩略图(<see cref="Thumbs"/> / <see cref="ThumbTile"/>)与加载器图标(<see cref="LoaderIcons"/>)。
///
/// 纪律:用例体全在 <see cref="AvaloniaSession.Run(Action)"/> 里跑(D-79);
/// 缩略图的路径来源走 <see cref="Thumbs.IThumbStore"/> 注入缝(默认实现要后端,测试不造后端 —— D-23),
/// 根目录一律给 <see cref="TestHome.Root"/> 之下的目录,因而**绝不碰用户的图片**。
///
/// 取图的同步等待一律经 <see cref="C2c2Files.RunOffThread{T}"/>:解码那一步是
/// <c>await Task.Run(...)</c>,在会话线程上直接 <c>GetResult()</c> 会把续体投回本线程而自锁(D-79)。
/// </summary>
[Collection("C2c2Globals")]
public sealed class C2c2ThumbsTests
{
    /// <summary>隔离目录里的缓存根(红线:测试绝不写用户的图片 / 缓存目录)。</summary>
    private static string CacheRootOf(TestHome home)
    {
        string root = Path.Combine(home.Dir, "cache", "thumbs");
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>一份"每个 url 都指到这张图"的假来源。</summary>
    private static FakeThumbStore StorePointingAt(string path, bool cachedHit = false)
    {
        return new FakeThumbStore(_ => path) { MissFirstCacheLookup = !cachedHit };
    }

    // ---------------------------------------------------------------- Thumbs:缓存与回落

    /// <summary>【任务书】缓存根可注入,而且落在 <see cref="TestHome.Root"/> 之下(红线:不许写用户图片目录)。</summary>
    [Fact]
    public void Cache_root_is_injectable_and_lands_under_the_test_home()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            string root = CacheRootOf(home);
            Thumbs.Store = new FakeThumbStore(url => Path.Combine(root, url.Length.ToString() + ".img"));

            Assert.StartsWith(TestHome.Root, root);
            Assert.StartsWith(TestHome.Root, new Thumbs.LocalThumbStore(root).PathOf("https://example.invalid/a.png"));

            // 产品侧那份默认缓存根也必须在隔离目录里(TIPCL_HOME 生效时与后端的 cache_dir/thumbs 同处)
            Assert.StartsWith(TestHome.Root, Thumbs.CacheRoot);
            Assert.StartsWith(TestHome.Root, new Thumbs.LocalThumbStore(Thumbs.CacheRoot).PathOf("https://example.invalid/b.png"));
        });
    }

    /// <summary>【Windows 源】<c>Thumbs.LoadAsync</c>:内存里没有 → 问后端拿路径 → 解码 → 记进内存缓存;
    /// 第二次直接命中内存,不再问后端。</summary>
    [Fact]
    public void Load_misses_then_hits_the_memory_cache()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            FakeThumbStore store = StorePointingAt(C2c2Files.Preset(home, "preset-morning"), cachedHit: true);
            Thumbs.Store = store;

            Assert.Null(Thumbs.Cached("https://example.invalid/a.png", 20));
            Assert.Equal(0, Thumbs.MemoryCount);

            var bitmap = C2c2Files.RunOffThread(() => Thumbs.LoadAsync("https://example.invalid/a.png", 20));

            Assert.NotNull(bitmap);
            Assert.Equal(1, Thumbs.MemoryCount);
            Assert.Same(bitmap, Thumbs.Cached("https://example.invalid/a.png", 20));

            int calls = store.CachedCalls + store.EnsureCalls;
            var again = C2c2Files.RunOffThread(() => Thumbs.LoadAsync("https://example.invalid/a.png", 20));

            Assert.Same(bitmap, again);
            Assert.Equal(calls, store.CachedCalls + store.EnsureCalls);
        });
    }

    /// <summary>【Windows 源】<c>FetchAsync</c> 的两步:先 <c>thumb_path</c>(给的是空串),
    /// 再 <c>ensure_thumb</c>(受 8 路闸门保护)。</summary>
    [Fact]
    public void Load_asks_thumb_path_first_then_ensure_thumb()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            FakeThumbStore store = StorePointingAt(C2c2Files.Preset(home, "preset-dusk"));
            Thumbs.Store = store;

            Assert.NotNull(C2c2Files.RunOffThread(() => Thumbs.LoadAsync("https://example.invalid/c.png", 24)));

            Assert.Equal(1, store.CachedCalls);
            Assert.Equal(1, store.EnsureCalls);
        });
    }

    /// <summary>【Windows 源】<c>thumb_path</c> 已经给了本地文件时不再 <c>ensure_thumb</c>。</summary>
    [Fact]
    public void Load_skips_ensure_when_the_thumb_is_already_cached_on_disk()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            FakeThumbStore store = StorePointingAt(C2c2Files.Preset(home, "preset-night"), cachedHit: true);
            Thumbs.Store = store;

            Assert.NotNull(C2c2Files.RunOffThread(() => Thumbs.LoadAsync("https://example.invalid/d.png", 24)));

            Assert.Equal(1, store.CachedCalls);
            Assert.Equal(0, store.EnsureCalls);
        });
    }

    /// <summary>【任务书】坏图 → 走 Windows 的回落(null + 记进失败),不抛异常。</summary>
    [Fact]
    public void Corrupt_image_yields_the_windows_fallback()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            string broken = C2c2Files.Write(Path.Combine(CacheRootOf(home), "broken.img"), new byte[] { 9, 8, 7, 6 });
            Thumbs.Store = StorePointingAt(broken, cachedHit: true);

            var bitmap = C2c2Files.RunOffThread(() => Thumbs.LoadAsync("https://example.invalid/broken.png", 24));

            Assert.Null(bitmap);
            Assert.Equal(1, Thumbs.FailureCount);
            Assert.Equal(0, Thumbs.MemoryCount);
            Assert.Null(Thumbs.Cached("https://example.invalid/broken.png", 24));
        });
    }

    /// <summary>【Windows 源】失败冷却期内不再重试(<c>FailCooldown</c> 5 分钟):第二次连后端都不问。</summary>
    [Fact]
    public void Failed_url_is_not_retried_during_the_cooldown()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            FakeThumbStore store = StorePointingAt(Path.Combine(CacheRootOf(home), "missing.img"), cachedHit: true);
            Thumbs.Store = store;

            Assert.Null(C2c2Files.RunOffThread(() => Thumbs.LoadAsync("https://example.invalid/e.png", 24)));
            int after = store.CachedCalls;
            Assert.Equal(1, Thumbs.FailureCount);

            Assert.Null(C2c2Files.RunOffThread(() => Thumbs.LoadAsync("https://example.invalid/e.png", 24)));

            Assert.Equal(after, store.CachedCalls);
            Assert.Equal(TimeSpan.FromMinutes(5.0), C2c2Expected.ThumbsFailCooldown);
        });
    }

    /// <summary>【Windows 源】空 url 直接 null(<c>LoadAsync</c> 的第一句),一次后端都不问。</summary>
    [Fact]
    public void Blank_url_is_rejected_without_touching_the_store()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            FakeThumbStore store = StorePointingAt(C2c2Files.Preset(home, "preset-morning"));
            Thumbs.Store = store;

            Assert.Null(C2c2Files.RunOffThread(() => Thumbs.LoadAsync("", 24)));
            Assert.Null(C2c2Files.RunOffThread(() => Thumbs.LoadAsync("   ", 24)));
            Assert.Null(Thumbs.Cached("", 24));
            Assert.Null(Thumbs.Cached(null!, 24));
            Assert.Equal(0, store.CachedCalls);
            Assert.Equal(0, store.EnsureCalls);
        });
    }

    /// <summary>【Windows 源】<c>DecodePixelWidth = max(16, px*2)</c>:图会按这个宽度缩下来。</summary>
    [Fact]
    public void Decoded_bitmap_is_scaled_to_the_windows_decode_width()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            string image = C2c2Files.Preset(home, "preset-sakura");
            Thumbs.Store = StorePointingAt(image);

            var bitmap = C2c2Files.RunOffThread(() => Thumbs.LoadAsync("https://example.invalid/f.png", 20));

            Assert.NotNull(bitmap);
            Assert.Equal(C2c2Expected.ThumbDecodeWidth(20), bitmap!.PixelSize.Width);

            // 小 px 也一样(16 是下限):px = 1 → 目标宽度仍是 16
            Thumbs.Clear();
            Thumbs.Store = StorePointingAt(image);
            var tiny = C2c2Files.RunOffThread(() => Thumbs.LoadAsync("https://example.invalid/g.png", 1));

            Assert.NotNull(tiny);
            Assert.Equal(16, tiny!.PixelSize.Width);
        });
    }

    /// <summary>【Windows 源】<c>Clear</c> 把内存缓存清掉(Windows 里缓存活到进程结束;mac 侧测试夹具要用)。</summary>
    [Fact]
    public void Clear_drops_the_memory_cache()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            Thumbs.Store = StorePointingAt(C2c2Files.Preset(home, "preset-morning"));

            C2c2Files.RunOffThread(() => Thumbs.LoadAsync("https://example.invalid/h.png", 24));
            Assert.Equal(1, Thumbs.MemoryCount);

            Thumbs.Clear();

            Assert.Equal(0, Thumbs.MemoryCount);
            Assert.Null(Thumbs.Cached("https://example.invalid/h.png", 24));
        });
    }

    /// <summary>【Windows 源】<c>LocalThumbStore</c> 的 url → 路径是纯函数(同 url 同路径,不同 url 不同路径)。</summary>
    [Fact]
    public void Local_store_maps_urls_to_stable_paths()
    {
        AvaloniaSession.Run(() =>
        {
            using TestHome home = new TestHome();
            Thumbs.LocalThumbStore store = new Thumbs.LocalThumbStore(CacheRootOf(home));

            Assert.Equal(store.PathOf("https://a/1.png"), store.PathOf("https://a/1.png"));
            Assert.NotEqual(store.PathOf("https://a/1.png"), store.PathOf("https://a/2.png"));
            Assert.EndsWith(".img", store.PathOf("https://a/1.png"));
            Assert.Equal("", store.PathOf(""));
        });
    }

    // ---------------------------------------------------------------- ThumbTile

    /// <summary>【Windows 源】<c>ThumbTile</c> 的构造:方块 = size、圆角 = radius、首字母大写,
    /// 默认显示首字母(没有图)。</summary>
    [Fact]
    public void Tile_starts_with_the_letter_fallback()
    {
        AvaloniaSession.Run(() =>
        {
            ThumbTile tile = new ThumbTile("steve", 40.0, 12.0);

            Assert.Equal(40.0, tile.Width);
            Assert.Equal(40.0, tile.Height);
            Assert.Equal(12.0, tile.CornerRadius.TopLeft);
            Assert.True(tile.ClipToBounds);
            Assert.Equal("S", tile.Letter.Text);
            Assert.True(tile.LetterVisible);
            Assert.False(tile.ImageVisible);
            Assert.Null(tile.ImageSource);
        });
    }

    /// <summary>【Windows 源】名字为空 → 首字母是 <c>?</c>;长名字只取第一个字母。</summary>
    [Fact]
    public void Tile_falls_back_to_a_question_mark_for_an_empty_name()
    {
        AvaloniaSession.Run(() =>
        {
            Assert.Equal("?", new ThumbTile("", 36.0).Letter.Text);
            Assert.Equal("?", new ThumbTile(null!, 36.0).Letter.Text);
            Assert.Equal("A", new ThumbTile("alexander", 36.0).Letter.Text);
        });
    }

    /// <summary>【Windows 源】<c>SetUrl(null)</c> 直接回落首字母。</summary>
    [Fact]
    public void Set_url_with_nothing_shows_the_letter()
    {
        AvaloniaSession.Run(() =>
        {
            ThumbTile tile = new ThumbTile("alex", 36.0);

            tile.SetUrl(null);

            Assert.True(tile.LetterVisible);
            Assert.False(tile.ImageVisible);
            Assert.Equal(1, tile.Token);
        });
    }

    /// <summary>【任务书】取图期间先显示首字母方块(不能空着、也不能抛)。</summary>
    [Fact]
    public void Tile_shows_the_fallback_while_loading()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            Thumbs.Store = StorePointingAt(C2c2Files.Preset(home, "preset-morning"));
            ThumbTile tile = new ThumbTile("steve", 36.0);

            tile.SetUrl("https://example.invalid/pending.png");

            Assert.True(tile.LetterVisible, "还没取回来的时候显示首字母");
            Assert.False(tile.ImageVisible);
            Assert.Equal(1, tile.Token);
        });
    }

    /// <summary>【Windows 源】缓存命中时 <c>SetUrl</c> 立刻显示图(不走异步那条路)。</summary>
    [Fact]
    public void Set_url_uses_the_cache_immediately()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            Thumbs.Store = StorePointingAt(C2c2Files.Preset(home, "preset-dusk"));
            C2c2Files.RunOffThread(() => Thumbs.LoadAsync("https://example.invalid/cached.png", 36));
            ThumbTile tile = new ThumbTile("steve", 36.0);

            tile.SetUrl("https://example.invalid/cached.png");

            Assert.True(tile.ImageVisible);
            Assert.False(tile.LetterVisible);
            Assert.NotNull(tile.ImageSource);
        });
    }

    /// <summary>【Windows 源 + GOAL】<c>SetImage</c>(premium 支)直接显示给定图标,并把请求号推走
    /// (过期的异步取图回来会被丢掉)。</summary>
    [Fact]
    public void Set_image_wins_over_a_pending_url_load()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            string image = C2c2Files.Preset(home, "preset-night");
            Thumbs.Store = StorePointingAt(image);
            ThumbTile tile = new ThumbTile("steve", 36.0);
            tile.SetUrl("https://example.invalid/slow.png");
            int token = tile.Token;

            tile.SetImage(ImageDecode.DecodeFile(image, 32));

            Assert.True(tile.ImageVisible);
            Assert.False(tile.LetterVisible);
            Assert.Equal(token + 1, tile.Token);
            Assert.NotNull(tile.ImageSource);
        });
    }

    /// <summary>【任务书】取图失败 → 还是首字母方块(Windows 的 <c>Show(null)</c>)。</summary>
    [Fact]
    public void Failed_load_keeps_the_letter_fallback()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            Thumbs.Store = StorePointingAt(Path.Combine(CacheRootOf(home), "nope.img"), cachedHit: true);
            ThumbTile tile = new ThumbTile("steve", 36.0);

            tile.SetUrl("https://example.invalid/missing.png");

            Assert.True(tile.LetterVisible);
            Assert.False(tile.ImageVisible);
        });
    }

    /// <summary>【Windows 源】<c>SetUrl</c> 用的宽度是 <c>max(16, Width)</c>:瓦片 22 → 取图 22 → 解码 44。</summary>
    [Fact]
    public void Tile_uses_its_own_width_as_the_requested_pixel_size()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();
            using TestHome home = new TestHome();
            string image = C2c2Files.Preset(home, "preset-morning");
            Thumbs.Store = StorePointingAt(image);

            var bitmap = C2c2Files.RunOffThread(() => Thumbs.LoadAsync("https://example.invalid/size.png", 22));
            ThumbTile tile = new ThumbTile("steve", 22.0);
            tile.SetUrl("https://example.invalid/size.png");

            Assert.NotNull(bitmap);
            Assert.Equal(C2c2Expected.ThumbDecodeWidth(22), bitmap!.PixelSize.Width);
            Assert.Same(bitmap, tile.ImageSource);
        });
    }

    // ---------------------------------------------------------------- LoaderIcons

    /// <summary>【Windows 源】<c>LoaderIcons.Embedded</c> 的文件名表(大小写不敏感)。</summary>
    [Fact]
    public void Loader_file_table_matches_windows()
    {
        AvaloniaSession.Run(() =>
        {
            foreach ((string loader, string file) in C2c2Expected.LoaderFiles)
            {
                Assert.Equal(file, LoaderIcons.FileFor(loader));
            }

            Assert.Equal("fabric_gh.png", LoaderIcons.FileFor("Fabric"));
            Assert.Equal("neoforge_gh.png", LoaderIcons.FileFor("NEOFORGE"));
            Assert.Null(LoaderIcons.FileFor("rift"));
            Assert.Null(LoaderIcons.FileFor("optifine"));
        });
    }

    /// <summary>【任务书】认识的加载器名都要能解出真图标(premium 口径:官方图标直接内嵌,离线也有)。</summary>
    [Fact]
    public void Known_loaders_resolve_to_embedded_icons()
    {
        AvaloniaSession.Run(() =>
        {
            foreach ((string loader, string _) in C2c2Expected.LoaderFiles)
            {
                Assert.True(LoaderIcons.HasIcon(loader), loader + " 应当有内嵌图标");
                var bitmap = LoaderIcons.Embedded(loader, C2c2Expected.LoaderPixelSize(36.0));

                Assert.NotNull(bitmap);
                Assert.InRange(bitmap!.PixelSize.Width, 1, C2c2Expected.LoaderPixelSize(36.0));
                Assert.True(bitmap.PixelSize.Height > 0);
            }
        });
    }

    /// <summary>【任务书】不认识的名字走 Windows 的回落:没有图标(null),瓦片显示首字母。</summary>
    [Fact]
    public void Unknown_loader_falls_back_to_the_letter_tile()
    {
        AvaloniaSession.Run(() =>
        {
            Assert.False(LoaderIcons.HasIcon("rift"));
            Assert.Null(LoaderIcons.Embedded("rift", 72));

            ThumbTile tile = LoaderIcons.Tile("rift", "Rift", 36.0);

            Assert.True(tile.LetterVisible);
            Assert.False(tile.ImageVisible);
            Assert.Equal("R", tile.Letter.Text);
        });
    }

    /// <summary>【Windows 源】解码宽度 <c>clamp(ceil(size)*2, 32, 96)</c>。</summary>
    [Fact]
    public void Loader_pixel_size_is_clamped_like_windows()
    {
        AvaloniaSession.Run(() =>
        {
            Assert.Equal(32, LoaderIcons.PixelSize(1.0));
            Assert.Equal(72, LoaderIcons.PixelSize(36.0));
            Assert.Equal(96, LoaderIcons.PixelSize(100.0));
            Assert.Equal(C2c2Expected.LoaderPixelSize(22.0), LoaderIcons.PixelSize(22.0));
        });
    }

    /// <summary>【任务书】<c>Tile</c> 给出来的瓦片能被 <see cref="Ui"/> 那一层直接用:
    /// 摆进 <c>Ui.H(...)</c>、挂上真窗口之后在可视树里、且拿到了图。</summary>
    [Fact]
    public void Loader_tile_is_usable_by_the_ui_layer()
    {
        AvaloniaSession.Run(() =>
        {
            ThumbTile tile = LoaderIcons.Tile("vanilla", "原版", 22.0);
            SPanel row = Ui.H(10.0, tile, Ui.Txt("原版", 13.0, bold: true));

            using UiHost window = UiHost.Show(row);

            Assert.Contains(tile, C2c2Visual.Descendants(row));
            Assert.NotNull(tile.ImageSource);
            Assert.True(tile.Width > 0, "瓦片有显式尺寸");
            Assert.True(tile.Bounds.Width > 0, "挂上窗口之后应当有真实尺寸");

            // 图片层与首字母层都还在树里(切换回落时不用重建控件)
            Assert.Contains(tile.Letter, C2c2Visual.Descendants(tile));
            Assert.Contains(tile.ImageLayer, C2c2Visual.Descendants(tile));
        });
    }

    /// <summary>【Windows 源】同一图标多处复用:解码缓存按 <c>文件名|px</c> 记,同尺寸第二次不再重新解。</summary>
    [Fact]
    public void Embedded_icons_are_cached_by_file_and_size()
    {
        AvaloniaSession.Run(() =>
        {
            var first = LoaderIcons.Embedded("forge", 72);

            Assert.NotNull(first);
            Assert.Same(first, LoaderIcons.Embedded("forge", 72));

            LoaderIcons.ClearCache();

            var again = LoaderIcons.Embedded("forge", 72);

            Assert.NotNull(again);
            Assert.NotSame(first, again);
        });
    }

    /// <summary>【mac 差异】取址口径是 avares(Windows 是 <c>pack://application:,,,/TipCL;component/…</c>)。</summary>
    [Fact]
    public void Loader_asset_uri_uses_the_avares_shape()
    {
        AvaloniaSession.Run(() =>
        {
            Assert.Equal("avares://TipCL/PremiumAssets/grass.png", LoaderIcons.AssetUri("grass.png"));
            Assert.Equal("avares://TipCL/PremiumAssets/forge_gh.png", LoaderIcons.AssetUri("forge_gh.png"));
        });
    }

    /// <summary>【本机实测】<c>forge_gh.png</c> 其实是 JPEG 内容(Windows csproj 的注释就这么写):
    /// 解码按文件内容识别,扩展名不影响。</summary>
    [Fact]
    public void Loader_icons_decode_by_content_not_extension()
    {
        AvaloniaSession.Run(() =>
        {
            foreach ((string loader, string _) in C2c2Expected.LoaderFiles)
            {
                var bitmap = LoaderIcons.Embedded(loader, 72);

                Assert.NotNull(bitmap);
                Assert.True(bitmap!.PixelSize.Height > 0);
                Assert.True(bitmap.PixelSize.Width > 0);
            }
        });
    }

    /// <summary>【Windows 源】<c>Tile</c> 的默认尺寸 36 / 圆角 9。</summary>
    [Fact]
    public void Loader_tile_defaults_match_windows()
    {
        AvaloniaSession.Run(() =>
        {
            ThumbTile tile = LoaderIcons.Tile("fabric", "Fabric");

            Assert.Equal(36.0, tile.Width);
            Assert.Equal(36.0, tile.Height);
            Assert.Equal(9.0, tile.CornerRadius.TopLeft);
        });
    }

    /// <summary>【本机实测】<see cref="ImageDecode"/> 对空字节 / null 给 null(不是抛)。</summary>
    [Fact]
    public void Image_decode_returns_null_for_empty_input()
    {
        AvaloniaSession.Run(() =>
        {
            using C2c2Scope scope = C2c2Scope.Capture();

            Assert.Null(ImageDecode.Decode(null, 32));
            Assert.Null(ImageDecode.Decode(Array.Empty<byte>(), 32));
            Assert.Null(ImageDecode.Decode(new byte[] { 1, 2, 3 }, 32));
            Assert.Null(ImageDecode.DecodeFile(Path.Combine(TestHome.Root, "no-such-file.png"), 32));
        });
    }
}
