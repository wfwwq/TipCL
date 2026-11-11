using System;
using System.IO;
using System.Text;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using TipCL.Core.Launcher.Sync;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// A4 的隔离回归守卫。红线:绝不许碰用户真实的 %LOCALAPPDATA%\TipCL。
///
/// A4 搬进来的代码里有 4 处**每次都直接调 Utils.ResolveRoot()**、不走缓存的 Utils.ROOT
/// (Ai\FileMemory.cs:23、Ai\McpServer.cs:58、Ai\VisionCaps.cs:37、Sync\PhoneLink.cs:58)。
/// TestHomeIsolation.cs 的 [ModuleInitializer] 把 TIPCL_HOME 指到 bin 下的 test-home\{Guid},
/// 所以这些代码应当全部落在 TestHome.Root 里。本文件用两种方式各测一遍:
///   1) 断言公开的路径属性直接以 TestHome.Root 开头(FileMemory.FilePath);
///   2) 私有的路径属性就用「调用一次会落盘的公开方法,再断言文件出现在 TestHome.Root 下」的办法
///      (VisionCaps.Mode 的 setter、Permission.SaveRuleStore),不写反射。
/// PhoneLink 没有「不联网就会落盘」的公开方法(写状态只在 Connect/Remember 里,而那要联网),
/// 所以对它是**反向**证明:先往 TestHome.Root 里放一份 phone_link.json,
/// 再看公开的 LastAddress/LastToken/LastDevice 能不能把它读出来。
///
/// 注意:`Ai\McpServer.cs` 因为 A5 的后端依赖被排除编译(见 TipCL.Core.csproj),
/// 本包没法给它写守卫用例;它的 PrefPath 与上面三处是同一形状,等 A5 恢复后补。
///
/// 本文件所有落盘都发生在 TestHome.Root 下的文件/子目录里,用完就删。
/// </summary>
public class AiSyncHomeIsolationTests
{
    // ---------- FileMemory ----------

    [Fact]
    public void FileMemory_path_is_the_isolated_test_home()
    {
        Assert.StartsWith(TestHome.Root, FileMemory.FilePath, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(TestHome.Root, "ai_memory.md"), FileMemory.FilePath);
        Assert.StartsWith(AppContext.BaseDirectory, FileMemory.FilePath, StringComparison.Ordinal);
    }

    [Fact]
    public void FileMemory_Remember_writes_ai_memory_md_under_the_isolated_test_home()
    {
        string path = FileMemory.FilePath;
        try
        {
            if (File.Exists(path)) File.Delete(path);

            string result = FileMemory.Remember("A4 isolation probe");

            Assert.Equal("已记住: A4 isolation probe", result);
            Assert.True(File.Exists(path), "记忆文件必须落在 " + path);
            Assert.StartsWith(TestHome.Root, path, StringComparison.Ordinal);

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            // 源码 FileMemory.cs 49 行的表头用的是全角括号 U+FF08/U+FF09 与全角逗号 U+FF0C(同 D-16 的坑)
            Assert.Equal("# TipCL AI 记忆\uFF08一行一条\uFF0C可直接编辑或删除\uFF09", lines[0]);
            Assert.Contains("- " + DateTime.Now.ToString("yyyy-MM-dd") + " A4 isolation probe", File.ReadAllText(path, Encoding.UTF8));
            // TextOf 100-108 行会把条目开头的 "yyyy-MM-dd " 去掉
            Assert.Equal("A4 isolation probe", FileMemory.TextOf(FileMemory.Entries()[0]));

            // 重复记同样的内容不会写第二条(Remember 91 行)
            Assert.StartsWith("已经记过了", FileMemory.Remember("A4 isolation probe"), StringComparison.Ordinal);
            Assert.Single(FileMemory.Entries());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ---------- VisionCaps(私有 PrefPath,用会落盘的公开 setter 证明) ----------

    [Fact]
    public void VisionCaps_Mode_setter_writes_ai_vision_json_under_the_isolated_test_home()
    {
        string path = Path.Combine(TestHome.Root, "ai_vision.json");
        string original = VisionCaps.Mode;
        try
        {
            VisionCaps.Mode = VisionCaps.ModeOff;

            Assert.True(File.Exists(path), "视能力设置必须落在 " + path);
            Assert.StartsWith(TestHome.Root, new FileInfo(path).FullName, StringComparison.Ordinal);
            Assert.Equal(VisionCaps.ModeOff, VisionCaps.Mode);

            // mode 是 off 时 Detect 在 97 行就返回,不会联网(88-103 行)
            VisionCaps.Result off = VisionCaps.Detect(new PyDict(), force: false);
            Assert.False(off.Supported);
            Assert.Equal("manual", off.Source);

            VisionCaps.Mode = VisionCaps.ModeOn;
            VisionCaps.Result on = VisionCaps.Detect(new PyDict(), force: false);
            Assert.True(on.Supported);
            Assert.Equal("manual", on.Source);

            // 写的是 JSON(77-78 行:Directory.CreateDirectory(Root) + File.WriteAllText)
            Assert.Contains("\"mode\"", File.ReadAllText(path, Encoding.UTF8), StringComparison.Ordinal);
        }
        finally
        {
            VisionCaps.Mode = string.IsNullOrEmpty(original) ? VisionCaps.ModeAuto : original;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ---------- PhoneLink(私有 StatePath,用公开读取端反向证明) ----------

    [Fact]
    public void PhoneLink_reads_phone_link_json_from_the_isolated_test_home()
    {
        string path = Path.Combine(TestHome.Root, "phone_link.json");
        try
        {
            if (File.Exists(path)) File.Delete(path);

            // 隔离目录里没有文件时,三个公开读取端都应当是空(Load 76-77 行返回全新的 State)
            Assert.Equal("", PhoneLink.LastAddress);
            Assert.Equal("", PhoneLink.LastToken);
            Assert.Equal("", PhoneLink.LastDevice);

            File.WriteAllText(
                path,
                "{\"address\":\"127.0.0.1:27531\",\"token\":\"token-a4\",\"device\":\"Pixel-A4\",\"bases\":{}}",
                new UTF8Encoding(false));

            // 能读到我们刚写进 TestHome.Root 的内容 → 证明 StatePath 就在隔离目录里
            Assert.Equal("127.0.0.1:27531", PhoneLink.LastAddress);
            Assert.Equal("token-a4", PhoneLink.LastToken);
            Assert.Equal("Pixel-A4", PhoneLink.LastDevice);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ---------- Permission(私有 _store_path,用会落盘的公开方法证明) ----------

    [Fact]
    public void Permission_SaveRuleStore_writes_ai_permissions_json_under_the_isolated_test_home()
    {
        string saved = Permission.PERMISSIONS_FILE;
        try
        {
            Permission.PERMISSIONS_FILE = null;   // 走默认路径:_store_path 319-322 = PPath.Join(Utils.ROOT, "ai_permissions.json")

            Permission.SaveRuleStore(Permission.LoadRuleStore());

            // 只断言「文件出现在 TestHome.Root 下」,不猜路径拼写(PPath.Join 与 Path.Combine 的写法可能不同)
            string[] found = Directory.GetFiles(TestHome.Root, "ai_permissions.json", SearchOption.AllDirectories);
            Assert.Single(found);
            Assert.StartsWith(TestHome.Root, found[0], StringComparison.Ordinal);

            PyDict store = Permission.LoadRuleStore();
            PyDict global = (PyDict)store.Get("global");
            Assert.NotNull(global);
            Assert.NotNull(global.Get("allow"));
            Assert.NotNull(global.Get("deny"));
            Assert.NotNull(global.Get("ask"));
        }
        finally
        {
            Permission.PERMISSIONS_FILE = saved;
            foreach (string f in Directory.GetFiles(TestHome.Root, "ai_permissions.json", SearchOption.AllDirectories))
            {
                File.Delete(f);
            }
        }
    }
}
