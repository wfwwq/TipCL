// 第 6 轮:把 A/B 这一轮修过的 Core 行为钉成断言(audit/D)。
//
// 覆盖 4 处此前**只有实现、没有守卫**的修复点:
//   1. PyJson 嵌套深度上限    —— 纯递归解析遇到深文档会 StackOverflowException **直接砍进程**,
//      C# 没有 Python 的 RecursionError 可捕获,所以只能靠解析器自己设上限并抛 JSONDecodeError。
//   2. Cleaner.Apply 的返回契约 —— 必须同时给出 removed / bytes / failed / error;
//      `failed`/`error` 是"清理失败要不要告诉用户"的唯一依据,缺了就又变成静默失败。
//   3. LayoutStoreV2 损坏备份 —— 读到损坏 v2 不能顺手删/覆盖(用户数据),也不能永远留在原地:
//      读路径只记"_pending",**下一次写盘前**先改名备份,再写新文件。
//   4. Mods.ContentDir 的版本隔离 —— resourcepacks/shaderpacks/datapacks 的落点必须跟着
//      per-version 的 --gameDir 走,否则开了"版本隔离"的实例里资源包会串台。
//
// 全部无网络、无 UI;写盘的用例都用 TestIsolation 的进程级隔离根(见 TestIsolation.cs)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;
using Xunit;

namespace TipCL.Tests.Core;

[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class CoreHardeningTests
{
    // ————— 1. PyJson 深度上限 —————

    private static string Nested(int depth)
        => new string('[', depth) + new string(']', depth);

    [Fact]
    public void PyJson_AcceptsAReasonableNestingDepth()
    {
        // 100 层:真实数据(布局/词表/AI 会话)远达不到,必须照常解析
        object v = PyJson.Loads(Nested(100));
        Assert.IsType<List<object>>(v);
    }

    [Fact]
    public void PyJson_RejectsPathologicalNestingWithACatchableError()
    {
        // 600 层 > MaxDepth(512):必须抛**可捕获**的 JSONDecodeError,
        // 而不是 StackOverflowException(那种异常在 .NET 里捕不了,进程直接没)
        string deep = Nested(600);
        JSONDecodeError err = Assert.Throws<JSONDecodeError>(() => PyJson.Loads(deep));
        Assert.Contains("depth", err.Message, StringComparison.OrdinalIgnoreCase);

        // LoadsOr 的兜底也必须成立:坏输入 → 默认值,不把异常漏给调用方
        object dflt = PyDict.Of("d", 1L);
        Assert.Same(dflt, PyJson.LoadsOr(deep, dflt));

        // 对象形态同理(走的是另一条 Obj 分支)
        string deepObj = string.Concat(new string('[' , 1), string.Concat(System.Linq.Enumerable.Repeat("{\"a\":", 600)), "1",
            string.Concat(System.Linq.Enumerable.Repeat("}", 600)), "]");
        var objDflt = PyDict.Of("k", "v");
        Assert.Same(objDflt, PyJson.LoadsOr(deepObj, objDflt));
    }

    [Fact]
    public void PyJson_DepthLimitAlsoGuardsMixedNesting()
    {
        // [{[]}] 交替嵌套:数组 + 对象共用同一个 _depth 计数,不能只挡纯数组
        var sb = new StringBuilder();
        for (int i = 0; i < 300; i++) sb.Append("{\"a\":[");
        for (int i = 0; i < 300; i++) sb.Append("]}");
        Assert.Same("fallback", PyJson.LoadsOr(sb.ToString(), "fallback"));
    }

    // ————— 2. Cleaner.Apply 的返回契约 —————

    [Fact]
    public void Cleaner_ApplyAlwaysReportsTheFourContractKeys()
    {
        PyDict result = Cleaner.Apply();

        foreach (string key in new[] { "removed", "bytes", "failed", "error" })
        {
            Assert.True(result.ContainsKey(key), "Cleaner.Apply 的返回缺 " + key + " —— 界面就无从告知清理结果");
        }

        Assert.True(Py.Int(result.Req("removed")) >= 0);
        Assert.True(Py.Int(result.Req("bytes")) >= 0);
        Assert.True(Py.Int(result.Req("failed")) >= 0);

        // 隔离根里没有可清理的东西:failed 必须是 0 且 error 必须是空串
        // (error 里塞一句"没有可清理项"这类话术会让"有没有失败"变成不可判定)
        if (Py.Int(result.Req("failed")) == 0)
        {
            Assert.Equal("", Py.Str(result.Req("error")));
        }
    }

    [Fact]
    public void Cleaner_PreviewIsReadOnlyAndDoesNotDeleteAnything()
    {
        string cache = Path.Combine(Utils.ROOT, "cache");
        Directory.CreateDirectory(cache);
        string part = Path.Combine(cache, "round6-preview.part");
        File.WriteAllText(part, "partial");

        try
        {
            PyDict preview = Cleaner.Preview();
            Assert.True(preview.Count > 0, "Preview 返回空字典,界面连\"能清多少\"都显示不出来");
            Assert.True(File.Exists(part), "Preview 不许删文件(它只是预览)");
        }
        finally
        {
            try { File.Delete(part); } catch (Exception) { }
        }
    }

    // ————— 3. LayoutStoreV2 损坏备份 —————

    [Fact]
    public void LayoutStoreV2_BacksUpCorruptFileBeforeOverwritingIt()
    {
        string dir = Path.Combine(Utils.ROOT, "round6-layout-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        string? before = LayoutStoreV2.DirOverride;
        try
        {
            LayoutStoreV2.DirOverride = dir;
            string v2 = LayoutStoreV2.FilePath;
            Assert.StartsWith(dir, v2, StringComparison.OrdinalIgnoreCase);

            // 坏的 v2:原文必须被保留下来(用户手改坏了也是用户数据)
            const string corrupt = "{ this is not json ";
            File.WriteAllText(v2, corrupt);

            LayoutV2 doc = LayoutStoreV2.Load();
            Assert.NotNull(doc);
            Assert.True(LayoutStoreV2.LastLoadV2Corrupt, "读到损坏 v2 必须被标记(否则界面不知道降级了)");
            Assert.Equal(corrupt, File.ReadAllText(v2));   // 读路径不许动原文件
            Assert.Equal("", LayoutStoreV2.LastCorruptBackup);

            // 下一次写盘前:先把损坏原件改名备份,再写新文件
            Assert.True(LayoutStoreV2.Save(doc), "Save 应当成功");
            string backup = LayoutStoreV2.LastCorruptBackup;
            Assert.False(string.IsNullOrEmpty(backup), "写盘前没有备份损坏原件 —— 用户手改的内容就永久丢了");
            Assert.True(File.Exists(backup), "备份文件不存在:" + backup);
            Assert.Equal(corrupt, File.ReadAllText(backup));

            // 新写的 v2 必须是能解析的、且与刚才读到的文档同版本
            // 新写下去的那份必须是**好文件**:损坏原件已改名,原位换成了合法 v2。
            // (不复用 LayoutStoreV2.Load():它按目录缓存,同目录再读不会重新解析磁盘 ——
            //  LastLoadV2Corrupt 的语义是"上次装载时",不是"磁盘现状"。)
            using var parsed = System.Text.Json.JsonDocument.Parse(File.ReadAllText(v2));
            Assert.Equal(LayoutSchema.Kind, parsed.RootElement.GetProperty("kind").GetString());
            Assert.Equal(LayoutSchema.CurrentVersion, parsed.RootElement.GetProperty("version").GetInt32());
        }
        finally
        {
            LayoutStoreV2.DirOverride = before;
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    }

    // ————— 4. Mods.ContentDir 的版本隔离落点 —————

    [Fact]
    public void Mods_ContentDirFollowsPerVersionGameDirWhenIsolated()
    {
        string name = "round6-" + Guid.NewGuid().ToString("N")[..8];
        Instance inst = Instances.CreateUniqueInstance(name, "游戏");
        try
        {
            // 不开版本隔离:落点就是实例根下的同名目录
            string plain = Mods.ContentDir(inst, "resourcepacks");
            Assert.Equal(Path.Combine(inst.path, "resourcepacks"), plain);

            // 开了版本隔离:落点必须**跟着 --gameDir 走**,不能还是实例根 —— 否则不同版本的
            // 资源包/光影/数据包会互相串台(这正是该修复要挡的)
            const string ver = "1.20.1";
            string isolated = Mods.ContentDir(inst, "resourcepacks", ver);
            Assert.NotEqual(plain, isolated);
            Assert.StartsWith(inst.path, isolated, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(ver, isolated, StringComparison.Ordinal);
            Assert.EndsWith("resourcepacks", isolated, StringComparison.Ordinal);
        }
        finally
        {
            try { inst.Delete(); } catch (Exception) { }
        }
    }
}
