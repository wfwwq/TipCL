// UiPrefs 持久化测试(R15,补版本文档记的「缺口 B」):
// 分组折叠态 / 版本收藏 / 下载页当前实例必须真落 config.json 的 ui_prefs 键。
// 全部跑在 TestIsolation 的隔离 TIPCL_HOME 里(真文件读写),不碰真实用户数据;
// 用 Guid 造唯一实例名,收尾清掉写进去的键,避免污染同进程的其它用例。
using System;
using System.Collections.Generic;
using System.IO;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.Core;

public class UiPrefsTests
{
    // ————————————————— 版本收藏(VmFavorites)—————————————————

    [Fact]
    public void Favorites_RoundTrip_ThroughFreshConfigInstance()
    {
        string inst = Unique("fav");
        try
        {
            var prefs = UiPrefs.Load();
            Assert.True(prefs.ToggleFavorite(inst, "1.20.1"));
            Assert.True(prefs.ToggleFavorite(inst, "1.20.2"));
            Assert.True(prefs.IsFavorite(inst, "1.20.1"));
            prefs.Save();

            // 全新的 Config 实例:重新读 config.json,而不是复用进程内那份内存配置
            UiPrefs fresh = FreshPrefs();
            Assert.True(fresh.IsFavorite(inst, "1.20.1"));
            Assert.True(fresh.IsFavorite(inst, "1.20.2"));
            Assert.Equal(new[] { "1.20.1", "1.20.2" }, fresh.Favorites(inst));
            Assert.False(fresh.IsFavorite(inst, "1.19.4"));
            // 别的实例不受影响
            Assert.Empty(fresh.Favorites(inst + "-other"));

            // 磁盘上的真实 JSON 也得有(绕开 Config 的内存投影直接读文件)
            List<string> onDisk = FavoritesOnDiskEventually(inst);
            Assert.Contains("1.20.1", onDisk);
            Assert.Contains("1.20.2", onDisk);

            // 再取消一条并落盘 → 新实例只看到剩下的
            var again = UiPrefs.Load();
            Assert.False(again.ToggleFavorite(inst, "1.20.1"));
            again.Save();
            UiPrefs after = FreshPrefs();
            Assert.False(after.IsFavorite(inst, "1.20.1"));
            Assert.True(after.IsFavorite(inst, "1.20.2"));
        }
        finally
        {
            ClearFavorite(inst);
        }
    }

    // 挡「又变回进程内静态表」的回归:写盘和读盘各用一份互不相干的全新 Config 实例。
    // 静态表实现下这里必然失败(第二份实例看不到第一份实例内存里改的东西)。
    [Fact]
    public void Favorites_ReadFromDisk_NotFromInProcessStatics()
    {
        string inst = Unique("disk");
        try
        {
            var writer = UiPrefs.Load(new LauncherConfig());
            Assert.True(writer.ToggleFavorite(inst, "1.21"));
            writer.Save();

            // 换一份全新实例读:只有真落盘才看得见
            UiPrefs reader = FreshPrefs();
            Assert.True(reader.IsFavorite(inst, "1.21"));

            // 反向:第二份实例只在内存里改、不 Save → 第三份实例必须看不见(内存 ≠ 磁盘)
            Assert.True(reader.ToggleFavorite(inst, "1.20.9"));
            Assert.True(reader.IsFavorite(inst, "1.20.9"));
            UiPrefs third = FreshPrefs();
            Assert.False(third.IsFavorite(inst, "1.20.9"));
            Assert.True(third.IsFavorite(inst, "1.21"));
        }
        finally
        {
            ClearFavorite(inst);
        }
    }

    // ————————————————— 分组折叠态(Expanded)—————————————————

    [Fact]
    public void Expanded_RoundTrip_CoversLibAndVmPrefixes()
    {
        string libKey = UiPrefs.LIB_PREFIX + Unique("fabric");
        string vmKey = UiPrefs.VM_PREFIX + Unique("fav");
        try
        {
            var prefs = UiPrefs.Load();
            prefs.SetExpanded(libKey, false);
            prefs.SetExpanded(vmKey, true);
            prefs.Save();

            UiPrefs fresh = FreshPrefs();
            // 实例库页的 lib: 与版本管理页的 vm: 各自独立,默认值相反也要按记录走
            Assert.False(fresh.GetExpanded(libKey, true));
            Assert.True(fresh.GetExpanded(vmKey, false));
            // 没记过的键 → 默认值,不串味
            Assert.True(fresh.GetExpanded(UiPrefs.LIB_PREFIX + "never-opened", true));
            Assert.False(fresh.GetExpanded(UiPrefs.VM_PREFIX + "never-opened", false));
            Assert.Equal(false, fresh.ExpandedMap()[libKey]);
            Assert.Equal(true, fresh.ExpandedMap()[vmKey]);

            // 翻转后同样往返
            var flip = UiPrefs.Load();
            flip.SetExpanded(libKey, true);
            flip.SetExpanded(vmKey, false);
            flip.Save();
            UiPrefs after = FreshPrefs();
            Assert.True(after.GetExpanded(libKey, false));
            Assert.False(after.GetExpanded(vmKey, true));
        }
        finally
        {
            ClearExpanded(libKey, vmKey);
        }
    }

    // ————————————————— 下载页当前实例(CatalogInstance)—————————————————

    [Fact]
    public void CatalogInstance_RoundTrip_ThroughFreshConfigInstance()
    {
        string before = UiPrefs.Load().CatalogInstance;
        string inst = Unique("catalog");
        try
        {
            var prefs = UiPrefs.Load();
            prefs.CatalogInstance = inst;
            prefs.Save();
            Assert.Equal(inst, FreshPrefs().CatalogInstance);
        }
        finally
        {
            var restore = UiPrefs.Load();
            restore.CatalogInstance = before;
            restore.Save();
        }
    }

    // ————————————————— 缺键 / 空字典 / 类型不符 —————————————————

    [Fact]
    public void MissingEmptyOrWrongTypedNode_FallsBackToDefaults_WithoutThrowing()
    {
        object? before = Config.CONFIG.Get(UiPrefs.KEY);
        try
        {
            // 1) ui_prefs 整个缺失
            Config.CONFIG.Set(UiPrefs.KEY, null);
            UiPrefs missing = UiPrefs.Load();
            Assert.True(missing.GetExpanded(UiPrefs.LIB_PREFIX + "fabric", true));
            Assert.False(missing.IsFavorite("any", "1.20.1"));
            Assert.Equal("", missing.CatalogInstance);
            Assert.Empty(missing.Favorites("any"));
            Assert.Empty(missing.ExpandedMap());

            // 2) 空字典
            Config.CONFIG.Set(UiPrefs.KEY, new PyDict());
            UiPrefs empty = UiPrefs.Load();
            Assert.True(empty.GetExpanded(UiPrefs.VM_PREFIX + "fav", true));
            Assert.Empty(empty.Favorites("any"));

            // 3) 里层键类型全错(手改 JSON 常见)
            Config.CONFIG.Set(UiPrefs.KEY, PyDict.Of(
                "expanded", "not-a-dict",
                "vm_favorites", new List<object> { "not-a-dict" },
                "catalog_instance", 42L));
            UiPrefs broken = UiPrefs.Load();
            Assert.False(broken.GetExpanded(UiPrefs.LIB_PREFIX + "fabric", false));
            Assert.False(broken.IsFavorite("any", "1.20.1"));
            Assert.Equal("42", broken.CatalogInstance);
            // 折叠态里混进非布尔值 → 该键按默认值,其它键照读
            Config.CONFIG.Set(UiPrefs.KEY, PyDict.Of(
                "expanded", PyDict.Of("lib:fabric", "yes", "lib:forge", false)));
            UiPrefs mixed = UiPrefs.Load();
            Assert.True(mixed.GetExpanded("lib:fabric", true));
            Assert.False(mixed.GetExpanded("lib:forge", true));

            // 4) 脏输入(空实例名 / 空版本 id)不落键
            UiPrefs guard = UiPrefs.Load();
            Assert.False(guard.ToggleFavorite("", "1.20.1"));
            Assert.False(guard.ToggleFavorite("inst", ""));
            guard.SetExpanded("", true);
            guard.Save();
            Assert.False(UiPrefs.Load().IsFavorite("", "1.20.1"));
        }
        finally
        {
            Config.CONFIG.Set(UiPrefs.KEY, before);
            Config.CONFIG.Save();
        }
    }

    // ————————————————— 整份 config.json 损坏 —————————————————

    [Fact]
    public void CorruptConfigJsonFile_FallsBackToDefaults_WithoutThrowing()
    {
        // 真·坏文件:新的 Config 实例必须给默认值(LauncherConfig 读 JSON 本来就吞 JSONDecodeError),
        // UiPrefs 照常读得到默认值、也写得下去 —— 绝不把损坏文件变成启动异常。
        // 这份文件是同进程所有用例共用的隔离配置:备份原始字节,收尾原样还原
        // (坏内容只在这一个用例里、只跨「构造一次 Config 实例」这一小段)。
        string file = PPath.Abs(Config.CONFIG_FILE);
        byte[] backup = File.Exists(file) ? File.ReadAllBytes(file) : Array.Empty<byte>();
        try
        {
            File.WriteAllText(file, "{ \"ui_prefs\": { \"expanded\": [1,2,,, }");
            LauncherConfig fresh = new LauncherConfig();

            UiPrefs prefs = UiPrefs.Load(fresh);
            Assert.False(prefs.IsFavorite("r15-corrupt", "1.20.1"));
            Assert.True(prefs.GetExpanded(UiPrefs.LIB_PREFIX + "fabric", true));
            Assert.Equal("", prefs.CatalogInstance);
            Assert.Empty(prefs.ExpandedMap());
            // 坏文件之后还能正常改(改这一份实例的内存树,落盘走它自己)
            prefs.SetExpanded(UiPrefs.LIB_PREFIX + "fabric", false);
            prefs.Save();
            Assert.False(UiPrefs.Load(fresh).GetExpanded(UiPrefs.LIB_PREFIX + "fabric", true));
        }
        finally
        {
            if (backup.Length > 0) File.WriteAllBytes(file, backup);
        }
    }

    // ————————————————— 工具 —————————————————

    private static string Unique(string tag) => $"r15-{tag}-{Guid.NewGuid():N}";

    /// <summary>一份全新的 Config 实例(重新读 config.json),用来证明数据真来自磁盘。</summary>
    private static UiPrefs FreshPrefs() => UiPrefs.Load(new LauncherConfig());

    /// <summary>直接读 config.json 里的 ui_prefs.vm_favorites[instance](绕开 Config 的内存投影)。</summary>
    private static List<string> FavoritesOnDisk(string instance)
    {
        var list = new List<string>();
        object? raw = Utils.ReadJson(PPath.Abs(Config.CONFIG_FILE));
        PyDict node = Py.DictOr(Py.DictOr(raw).Get(UiPrefs.KEY));
        foreach (object item in Py.ListOr(Py.DictOr(node.Get("vm_favorites")).Get(instance)))
            list.Add(Py.S(item));
        return list;
    }

    // 配置文件偶发被并发写/杀软短暂占锁(同 ThemeToggleTests 的处理):重试几次再断言
    private static List<string> FavoritesOnDiskEventually(string instance)
    {
        for (int i = 0; i < 20; i++)
        {
            List<string> list = FavoritesOnDisk(instance);
            if (list.Count > 0) return list;
            System.Threading.Thread.Sleep(20);
        }
        return FavoritesOnDisk(instance);
    }

    private static void ClearFavorite(string instance)
    {
        var prefs = UiPrefs.Load();
        foreach (string id in prefs.Favorites(instance))
            prefs.ToggleFavorite(instance, id);
        prefs.Save();
    }

    private static void ClearExpanded(params string[] keys)
    {
        var prefs = UiPrefs.Load();
        // 折叠态没有「删键」入口(线上用不到);这里翻回默认值,留个 Guid 键也无害
        foreach (string key in keys)
            prefs.SetExpanded(key, key.StartsWith(UiPrefs.LIB_PREFIX, StringComparison.Ordinal));
        prefs.Save();
    }
}
