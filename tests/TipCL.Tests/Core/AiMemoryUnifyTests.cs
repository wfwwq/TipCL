// AI5-B1:长期记忆只留一份(FileMemory 的 ai_memory.md)。
//
// 复现(修复前):助手侧(AiAgent.SystemMessage / ToolRunner 的 remember / forget)读写
// $TIPCL_HOME/ai-memory.json,界面记忆面板(AiMemoryPanel)与设置页读写 $TIPCL_HOME/ai_memory.md
// (Core 的 FileMemory),两侧零交叉调用 —— 于是「助手记的事面板看不见、面板删掉的条目助手还记着」。
//
// 现在 AiMemory 是 FileMemory 的门面:写入走同一个文件,旧的 ai-memory.json 在第一次访问时
// 幂等迁进 ai_memory.md(迁完改名留档,免得面板删掉的条目下次启动又复活)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class AiMemoryUnifyTests : IDisposable
{
    private sealed class FakeApi : ToolRunner.IBackend
    {
        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
            => Task.FromResult<T?>(default);
    }

    private readonly string _legacyPath = Path.Combine(Utils.ROOT, AiMemory.LegacyFileName);
    private readonly string _migratedPath = Path.Combine(Utils.ROOT, AiMemory.LegacyFileName + AiMemory.MigratedSuffix);
    private readonly List<string> _before;

    public AiMemoryUnifyTests()
    {
        _before = AiMemory.All();
        Clean();
    }

    public void Dispose()
    {
        // 记忆表整份还原(用例共享 ai_memory.md 这份全局状态)
        Clean();
        foreach (string line in _before)
        {
            FileMemory.Remember(line);
        }
    }

    /// <summary>把共享的那一份记忆与两个旧文件都清干净。</summary>
    private void Clean()
    {
        FileMemory.Clear();
        foreach (string path in new[] { _legacyPath, _migratedPath })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }
    }

    private void WriteLegacy(params string[] items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_legacyPath)!);
        File.WriteAllText(_legacyPath, "{\"items\":[" + string.Join(",", items.Select(x => "\"" + x + "\"")) + "]}");
    }

    /// <summary>助手侧与界面侧看到的必须是同一份(修复前是两个不同的文件,内容互不可见)。</summary>
    [Fact]
    public void LegacyJson_IsMigratedIntoTheSameFileThePanelReads()
    {
        WriteLegacy("旧记忆甲", "旧记忆乙");

        List<string> fromAssistant = AiMemory.All();                                   // 触发迁移
        List<string> fromPanel = FileMemory.Entries().Select(FileMemory.TextOf).ToList();

        Assert.Contains("旧记忆甲", fromAssistant);
        Assert.Contains("旧记忆乙", fromAssistant);
        Assert.Contains("旧记忆甲", fromPanel);
        Assert.Contains("旧记忆乙", fromPanel);
        // 迁移是「搬进同一份」而不是「两份都在」:旧文件改名留档,原文件不再被读
        Assert.False(File.Exists(_legacyPath), "迁移后旧的 ai-memory.json 应当改名留档");
        Assert.True(File.Exists(_migratedPath), "迁移后应当留下 ai-memory.json" + AiMemory.MigratedSuffix);
    }

    /// <summary>幂等:重复访问不会重复记;迁完删掉的条目不会在下一次访问时复活。</summary>
    [Fact]
    public void Migration_IsIdempotent_AndDoesNotResurrectDeletedItems()
    {
        WriteLegacy("会被删掉的旧记忆");
        Assert.Contains("会被删掉的旧记忆", AiMemory.All());

        int count = AiMemory.All().Count;
        Assert.Equal(count, AiMemory.All().Count); // 再读一次不翻倍

        Assert.StartsWith("已删除", FileMemory.Forget("会被删掉的旧记忆"), StringComparison.Ordinal);
        Assert.DoesNotContain("会被删掉的旧记忆", AiMemory.All());
        // 关键:留档文件不会把删掉的条目再迁一遍(这就是「迁完改名」这一步的非功能验收)
        Assert.DoesNotContain("会被删掉的旧记忆", FileMemory.Entries());
    }

    /// <summary>坏掉的旧文件不该拖垮对话:当空处理。</summary>
    [Fact]
    public void BrokenLegacyJson_IsIgnored()
    {
        File.WriteAllText(_legacyPath, "{ 这不是 JSON");
        Assert.Empty(AiMemory.All());
        Assert.True(File.Exists(_migratedPath));
    }

    /// <summary>迁移与新增都吃 FileMemory 的上限(统一到 FileMemory 之后是 80 条,原先助手侧是 200)。</summary>
    [Fact]
    public void Migration_RespectsTheSharedEntryCap()
    {
        const int extra = 15;
        WriteLegacy(Enumerable.Range(1, FileMemory.MaxEntries + extra).Select(i => "旧记忆 " + i).ToArray());
        List<string> all = AiMemory.All();
        Assert.Equal(FileMemory.MaxEntries, all.Count);
        // 丢的是最旧的(FileMemory.Remember 的裁剪方向)
        Assert.DoesNotContain("旧记忆 1", all);
        Assert.Contains("旧记忆 " + (FileMemory.MaxEntries + extra), all);
    }

    /// <summary>场景 20:助手侧 remember/forget 写的就是面板读的那一份,系统提示里也看得到。</summary>
    [Fact]
    public async Task RememberAndForgetTools_WriteTheSameFileThePanelReads()
    {
        // yolo 档:B2 之后 remember/forget 是写工具,这条用例只验「写哪一份」,不走确认卡
        var runner = new ToolRunner(new FakeApi()) { PermissionMode = "yolo" };

        string added = await runner.RunAsync(new ToolCall("m1", "remember", PyDict.Of("text", "常玩 1.20.1 Fabric")));
        Assert.True(Py.Truthy(Py.D(PyJson.Loads(added)).Get("ok")), added);
        // 面板读的是同一份文件(FileMemory 的条目带「日期 」前缀,比对用结尾匹配)
        Assert.Contains(FileMemory.Entries(), e => e.EndsWith("常玩 1.20.1 Fabric", StringComparison.Ordinal));
        Assert.Contains("常玩 1.20.1 Fabric", AiMemory.Context());

        // 重复记不重复写(FileMemory 的去重口径)
        await runner.RunAsync(new ToolCall("m2", "remember", PyDict.Of("text", "常玩 1.20.1 Fabric")));
        Assert.Single(FileMemory.Entries());

        string forgotten = await runner.RunAsync(new ToolCall("m3", "forget", PyDict.Of("text", "1.20.1")));
        Assert.Equal(1L, Py.Int(Py.D(PyJson.Loads(forgotten)).Get("forgotten")));
        Assert.DoesNotContain("常玩 1.20.1 Fabric", FileMemory.Entries());
        Assert.Equal("", AiMemory.Context());
    }

    /// <summary>反向:面板/设置页写进 FileMemory 的条目,助手侧的系统提示里立刻看得到(修复前看不见)。</summary>
    [Fact]
    public void PanelWrites_AreVisibleToTheAssistantSystemMessage()
    {
        FileMemory.Remember("显卡是 RTX 3060");
        Assert.Contains("显卡是 RTX 3060", AiMemory.Context());

        string content = Py.S(AiAgent.SystemMessage("demo").Get("content"));
        Assert.Contains("显卡是 RTX 3060", content);
    }
}
