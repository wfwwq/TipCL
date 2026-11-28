// AI5-C1:按需加载工具原先只看最后一条用户消息(Tools.SelectToolSchemas 用 LastUserText),
// 现在改成聚合**最近 12 条** user / tool 消息再匹配关键词,与 Windows 版 Tools._selection_text 对齐。
//
// 这些用例不联网、不碰真实数据:只调 Tools.SelectToolSchemas 这一个纯函数,断言它下发哪些工具名。
// 老口径的复现靠 SelectToolSchemas 的 userText 参数(它正是「只看某一段文本」的分支,等价于原先的 LastUserText)。
using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class AiToolSelectionWindowTests
{
    private static PyDict User(string text) => PyDict.Of("role", "user", "content", text);

    private static PyDict Assistant(string text) => PyDict.Of("role", "assistant", "content", text);

    private static PyDict ToolResult(string name, string text)
        => PyDict.Of("role", "tool", "tool_call_id", "call-1", "name", name, "content", text);

    /// <summary>选一遍工具,返回工具名(userText 非空 = 模拟老口径:只拿这一段文本去匹配)。</summary>
    private static List<string> Picked(List<object> messages, string? userText = null)
        => Tools.SelectToolSchemas(messages, userText).Select(Tools.SchemaName).ToList();

    /// <summary>场景 19:第一轮「装钠」→ 第二轮「再装个小地图」,第二轮仍然下发 install_mod。</summary>
    [Fact]
    public void Scenario19_SecondTurnStillSendsInstallMod()
    {
        var history = new List<object>
        {
            User("装钠"),
            Assistant("好的,我搜一下。"),
            ToolResult("search_mods", "{\"results\":[{\"name\":\"Sodium\",\"slug\":\"sodium\"}]}"),
            User("再装个小地图"),
        };

        List<string> picked = Picked(history);

        Assert.Contains("install_mod", picked); // 第二轮仍然要能装模组
        Assert.Contains("search_mods", picked); // 常驻工具
        Assert.Contains("install_world", picked); // 「地图」这一行关键词仍然带出存档类工具(与 Windows 同一张表)
    }

    /// <summary>
    /// 第二轮的续话里没有关键词和动词时,窗口里的上一轮意图要还在。
    /// 老口径(只看最后一条用户消息)会漏发 search_content / install_shader。
    /// </summary>
    [Fact]
    public void FollowUpWithoutKeywords_KeepsToolsFromTheLastTwelveMessages()
    {
        var history = new List<object>
        {
            User("帮我装个光影包"),
            Assistant("我看看有哪些可用的。"),
            ToolResult("search_content", "{\"results\":[{\"name\":\"BSL Shaders\",\"slug\":\"bsl\"}]}"),
            User("再来一个"),
        };

        // 复现:老口径 —— 只拿「再来一个」匹配,光影类工具一个都带不出来
        List<string> before = Picked(history, userText: "再来一个");
        Assert.DoesNotContain("search_content", before);
        Assert.DoesNotContain("install_shader", before);

        // 修复后:最近 12 条里「帮我装个光影包」还在,工具带回来了
        List<string> after = Picked(history);
        Assert.Contains("search_content", after);
        Assert.Contains("install_shader", after);
    }

    /// <summary>窗口就是最近 12 条:第 13 条(最老的那条)不再参与匹配。</summary>
    [Fact]
    public void OnlyTheLastTwelveMessagesAreScanned()
    {
        var inside = new List<object> { User("帮我装个光影包") };
        for (int i = 0; i < 10; i++)
        {
            inside.Add(ToolResult("noop", "{}"));
        }
        inside.Add(User("再来一个"));
        Assert.Equal(12, inside.Count);
        Assert.Contains("install_shader", Picked(inside));

        // 同样的历史在末尾用户消息之前再插一条空工具结果:最老的那条用户消息被挤出窗口,关键词也就不算了
        var outside = new List<object>(inside);
        outside.Insert(outside.Count - 1, ToolResult("noop", "{}"));
        Assert.Equal(13, outside.Count);
        Assert.DoesNotContain("install_shader", Picked(outside));
    }

    /// <summary>参与匹配的只有 user / tool 两种角色:助手自己的话不算用户意图(与 Windows 一致)。</summary>
    [Fact]
    public void OnlyUserAndToolMessagesFeedTheKeywordMatch()
    {
        var assistantSaidIt = new List<object> { User("你好"), Assistant("我这就帮你装个光影包") };
        Assert.DoesNotContain("install_shader", Picked(assistantSaidIt));

        // 上一轮工具结果里出现过的词,这一轮照样能命中(照 Windows 的 user/tool 口径)
        var toolSaidIt = new List<object> { User("你好"), ToolResult("search_content", "{\"name\":\"BSL Shaders\"}") };
        Assert.Contains("install_shader", Picked(toolSaidIt));
    }

    /// <summary>聚合窗口不会无脑放宽:纯提问仍然不附带「裸写工具」(原有口径保留)。</summary>
    [Fact]
    public void PureQuestion_StillDoesNotBringBareWriteTools()
    {
        var history = new List<object>
        {
            User("帮我安装个整合包"),
            ToolResult("search_modpacks", "{\"results\":[{\"name\":\"RLCraft\"}]}"),
            User("怎么才能装好呢?"),
        };

        List<string> picked = Picked(history);

        Assert.Contains("install_modpack", picked); // 关键词类工具照旧带出来
        Assert.DoesNotContain("delete_instance", picked); // 纯提问不给写工具
    }

    /// <summary>
    /// 「这一轮是不是在下命令」只看本轮用户话:上一轮的命令动词不会让本轮把**全部写工具**带上
    /// (实测那样一轮要多约 7.1k token 的裸写工具,口径对比见 progress/AI5-C1.md)。
    /// 注意区分:上一轮话里的**关键词**(AI5-C3 补的「卸了」)照旧参与命中 → delete_mod 会来;
    /// 这里拦的是「光凭上一轮的命令动词就把 33 个写工具全倒进来」。
    /// </summary>
    [Fact]
    public void EarlierTurnCommand_DoesNotEnableBareWriteTools()
    {
        var history = new List<object>
        {
            User("帮我把 JEI 卸了"), // 上一轮像在下命令,而且「卸了」是 AI5-C3 补进关键词表的说法
            ToolResult("search_mods", "{\"ok\":true}"),
            User("那别的呢"), // 本轮既不是命令,也没有关键词
        };

        List<string> picked = Picked(history);

        Assert.Contains("install_mod", picked); // 常驻工具照旧
        Assert.Contains("delete_mod", picked); // 关键词窗口命中「卸了」→ 该带的照带
        Assert.DoesNotContain("delete_instance", picked); // 但没有因为上一轮的命令动词把裸写工具倒进来
    }

    /// <summary>本轮确实是命令时,关键词表没覆盖的写工具照旧带上(原有口径不变)。</summary>
    [Fact]
    public void CurrentTurnCommand_StillBringsBareWriteTools()
    {
        List<string> picked = Picked(new List<object> { User("把它关掉") });

        Assert.Contains("disable_mod", picked);
        Assert.Contains("delete_instance", picked);
    }
}
