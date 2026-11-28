// AI5-B2:remember / forget 的元信息必须和真实副作用一致。
//
// 复现(修复前):AiToolTypes.cs 里两项都写着 readonly = true,而执行体(ToolRunner.MemoryAdd /
// AiMemory.Forget)确实写文件(ai_memory.md)。后果不只是「少弹一次确认」:
//   * `Tools.IsWriteTool` 就是 MCP Server 判断「这次调用要不要外部确认」的依据,所以经 MCP 调用
//     remember/forget 也不确认;
//   * 并行调度按 readonly 判并行,记忆这种「同一轮里有先后语义」的调用被判成可并行。
// 这里钉住元信息本身,并顺手钉住它带出来的两处判据(写工具 / 不并行)。
using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

public class AiToolMetaSideEffectTests
{
    [Fact]
    public void RememberAndForget_DeclareTheirRealSideEffect()
    {
        foreach (string tool in new[] { "remember", "forget" })
        {
            ToolMetaInfo meta = ToolMeta.Get(tool)!;
            Assert.NotNull(meta);
            Assert.False(meta.@readonly, tool + " 会写 ai_memory.md,不能标成只读");
            Assert.Equal("write_local", meta.side_effect);
            Assert.Equal("low", meta.risk);
            // 写工具的判据:MCP Server 的「外部发起的写操作一律弹确认」读的就是它
            Assert.True(Tools.IsWriteTool(tool), tool + " 必须被当成写工具(否则 MCP 侧不确认)");
        }
    }

    [Fact]
    public void RememberAndForget_AreNoLongerScheduledInParallel()
    {
        // 同一轮里「先记再忘」有先后语义;改标写工具之后它们自然退出并行组(只读才并行)
        foreach (string tool in new[] { "remember", "forget" })
        {
            var call = new ToolCall("c", tool, new PyDict());
            Assert.False(Scheduler.IsParallelizable(call), tool + " 不该与别的调用并行");
        }
        // 负例:真正的只读工具仍然并行(别把整张表都收紧)
        Assert.True(Scheduler.IsParallelizable(new ToolCall("c", "list_mods", new PyDict())));
    }

    [Fact]
    public void ReadOnlyTools_StillDeclareReadOnly()
    {
        // 元信息表整体没被这次改动带偏:只读那批仍然是只读(抽样,含 MCP 判据用到的两个)
        foreach (string tool in new[] { "list_mods", "read_artifact", "fs_read", "get_latest_log" })
        {
            Assert.True(ToolMeta.Get(tool)!.@readonly, tool + " 应当仍是只读");
            Assert.False(Tools.IsWriteTool(tool));
        }
    }

    /// <summary>表里每个工具的名字都唯一(元信息按名字查表,重名会静默丢一条)。</summary>
    [Fact]
    public void ToolMetaNames_AreUnique()
    {
        List<string> names = ToolMeta.ALL.Select(m => m.name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }
}
