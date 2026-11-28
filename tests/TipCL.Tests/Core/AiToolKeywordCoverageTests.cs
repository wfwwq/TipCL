// AI5-C3(方案 1b):补进去的口语说法必须能把对应工具带出来 —— 这些词是按离线回放量出来的漏选补的,
// 数据与逐条依据见 progress/AI5-C3.md,回放探针在 tools/ai-tool-selection-probe。
//
// 这些用例只调纯函数 Tools.SelectToolSchemas,不联网、不碰真实数据。它们锁的是「补过的词不被人手滑删掉」;
// 关键词路线的长尾(没补过的说法照样会漏)属于已知限制,探针的对抗性留出集在量它,这里不锁。
using System.Collections.Generic;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class AiToolKeywordCoverageTests
{
    private static List<string> Picked(string text)
        => Tools.SelectToolSchemas(new List<object> { PyDict.Of("role", "user", "content", text) })
            .Select(Tools.SchemaName).ToList();

    /// <summary>各个漏选工具对应的口语说法(补词表见 Tools.C3Keywords)。</summary>
    [Theory]
    [InlineData("游戏有点卡,帮我看看", "diagnose_lag")]
    [InlineData("好卡", "tune_jvm")]
    [InlineData("一卡一卡的,怎么调", "get_system_info")]
    [InlineData("把那个模组停用了", "disable_mod")]
    [InlineData("帮我把 JEI 卸了", "delete_mod")]
    [InlineData("想把崩溃日志整理一下发给作者", "export_diagnostic_report")]
    [InlineData("把这些日志发到群里问问", "export_diagnostic_report")]
    [InlineData("顺便也发我一份", "export_diagnostic_report")]
    [InlineData("帮我写个说明文件", "fs_write")]
    [InlineData("新建个文件记点东西", "fs_write")]
    [InlineData("这个 jar 是什么模组", "inspect_mod")]
    [InlineData("这个 sodium.jar 装的是哪个版本?", "inspect_mod")]
    [InlineData("游戏说找不到运行环境,帮我弄一下", "get_java_list")]
    public void ColloquialPhrasings_BringTheNeededTool(string text, string expected)
    {
        Assert.Contains(expected, Picked(text));
    }

    /// <summary>
    /// 补词没有放宽「纯提问不给写工具」这条口径:提问式说法照样不带裸写工具,
    /// 但关键词行里配的读/查工具照带(和补词前一致)。
    /// </summary>
    [Fact]
    public void KeywordPatch_DoesNotLoosenThePureQuestionRule()
    {
        List<string> ask = Picked("这游戏为什么会卡?");
        Assert.Contains("get_system_info", ask); // 「卡」命中卡顿那一行
        Assert.DoesNotContain("delete_instance", ask); // 提问不给写工具

        List<string> command = Picked("帮我把这些模组删了");
        Assert.Contains("delete_mod", command); // 下命令才带
    }
}