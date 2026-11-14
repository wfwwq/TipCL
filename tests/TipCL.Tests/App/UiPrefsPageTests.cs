// R15 页面接线测试:折叠态 / 收藏必须经 UiPrefs 落 config.json(缺口 B 的页面侧)。
// 页面没给测试留点击入口,这里用反射直接调私有处理器(方法名与 Windows 版同名),
// 控件构造走 TestApp 的无头会话(控件有线程亲和,不能在线程池线程上 new)。
using System;
using System.Reflection;
using TipCL.App.Pages.Library;
using TipCL.App.Pages.Version;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.App;

public class UiPrefsPageTests
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [Fact]
    public void PageGroupToggles_PersistExpanded_ForLibAndVmPrefixes() => TestApp.Run(() =>
    {
        var lib = new LibraryPage();
        object libGroup = Invoke(lib, "BuildGroup", "fabric", "Fabric 版本 (1)", true);
        Invoke(lib, "ToggleGroup", libGroup);
        // 全新 Config 实例读盘:两个页面各写各的前缀
        Assert.False(UiPrefs.Load(new LauncherConfig()).GetExpanded(UiPrefs.LIB_PREFIX + "fabric", true));

        var vm = new VersionManagePage();
        object vmGroup = Invoke(vm, "BuildGroup", "fav", true);
        Invoke(vm, "ToggleGroup", vmGroup);
        UiPrefs fresh = UiPrefs.Load(new LauncherConfig());
        Assert.False(fresh.GetExpanded(UiPrefs.VM_PREFIX + "fav", true));
        Assert.False(fresh.GetExpanded(UiPrefs.LIB_PREFIX + "fabric", true));

        // 再点一次回到展开态,顺带证明翻转也落盘
        Invoke(vm, "ToggleGroup", vmGroup);
        Assert.True(UiPrefs.Load(new LauncherConfig()).GetExpanded(UiPrefs.VM_PREFIX + "fav", false));

        // 收尾:恢复默认(lib 组默认展开,vm 组默认展开)
        var restore = UiPrefs.Load();
        restore.SetExpanded(UiPrefs.LIB_PREFIX + "fabric", true);
        restore.SetExpanded(UiPrefs.VM_PREFIX + "fav", true);
        restore.Save();
    });

    [Fact]
    public void PageStarToggles_PersistSharedVmFavorites() => TestApp.Run(() =>
    {
        string inst = $"r15-page-{Guid.NewGuid():N}";
        try
        {
            // 实例库页收藏
            var lib = new LibraryPage();
            SetField(lib, "_folder", inst);
            Invoke(lib, "ToggleFav", NewVersion(typeof(LibraryPage)));
            Assert.True(UiPrefs.Load(new LauncherConfig()).IsFavorite(inst, "1.20.1"));

            // 版本管理页:同一实例下看到同一条收藏(Windows 的 VmFavorites 是共享表)
            var vm = new VersionManagePage();
            SetField(vm, "_folder", inst);
            Assert.True(Favorite("IsFav", vm, "1.20.1"));
            Invoke(vm, "ToggleFav", NewVersion(typeof(VersionManagePage)));
            UiPrefs fresh = UiPrefs.Load(new LauncherConfig());
            Assert.False(fresh.IsFavorite(inst, "1.20.1"));
            Assert.Empty(fresh.Favorites(inst));
        }
        finally
        {
            var prefs = UiPrefs.Load();
            foreach (string id in prefs.Favorites(inst)) prefs.ToggleFavorite(inst, id);
            prefs.Save();
        }
    });

    // ————————————————— 反射小工具 —————————————————

    // Ver 是页面私有的嵌套类型:构造它并把 Id 填成 "1.20.1"(ToggleFav 只读这个字段)
    private static object NewVersion(Type pageType)
    {
        object ver = Activator.CreateInstance(pageType.GetNestedType("Ver", BindingFlags.NonPublic)!)!;
        SetField(ver, "Id", "1.20.1");
        return ver;
    }

    private static object Invoke(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, All)!.Invoke(target, args)!;

    private static bool Favorite(string name, object target, string id)
        => (bool)Invoke(target, name, id);

    private static void SetField(object target, string name, object value)
        => target.GetType().GetField(name, All)!.SetValue(target, value);
}
