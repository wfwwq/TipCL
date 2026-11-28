// AI5-B4:崩溃修复面板要有自己的会话 id(轮次 / 检查点作用域不与主对话混在一起)。
//
// 复现(修复前):AiFixView 构造 AiAgent 时没设 ChatId,落到默认值 "active" —— 与主对话同域。
// Rewind 按会话事件流数轮次、按 chatId + turn 回滚检查点(Rewind.cs:99-120),所以主对话点
// 「撤回上一轮」会退到修复面板刚跑完的那一轮上。
using System;
using System.Collections.Generic;
using TipCL.App.Common;
using TipCL.App.Pages.Ai;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.App;

[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class AiFixChatIdTests
{
    /// <summary>修复面板跑的那一轮,用的 agent 必须是修复面板自己的会话 id。</summary>
    [Fact]
    public void FixPanel_AgentUsesItsOwnChatId() => TestApp.RunAsync(() =>
    {
        var fix = new AiFixView(InProcessApi.Instance, PyDict.Of("instance", "demo"));
        AiAgent agent = fix.NewAgent();

        Assert.Equal(AiFixView.FixChatId, agent.ChatId);
        // 主对话的 id 是 AiAgent 的默认值 "active":两个面板必须错开
        Assert.NotEqual(new AiAgent(fix.Runner, _ => { }).ChatId, agent.ChatId);
        return System.Threading.Tasks.Task.CompletedTask;
    });

    /// <summary>
    /// 「撤回上一轮」是**按会话 id** 数轮次的:修复面板的轮次进不了主对话的候选表(反之亦然)。
    /// 这是 B4 的验收口径(修复面板跑一轮后,主对话的撤回不受影响)在可观测层的形式。
    /// </summary>
    [Fact]
    public void Rewind_Candidates_AreScopedToTheChatId()
    {
        string main = "b4-main-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        string fix = AiFixView.FixChatId;
        List<string> fixBefore = Rewind.TurnCandidates(fix);

        Store.LogEvent(main, "TurnStarted", PyDict.Of("turn_id", "t-main-1", "user_len", 1L));
        Store.LogEvent(fix, "TurnStarted", PyDict.Of("turn_id", "t-fix-1", "user_len", 1L));

        Assert.Contains("t-main-1", Rewind.TurnCandidates(main));
        Assert.DoesNotContain("t-fix-1", Rewind.TurnCandidates(main));
        Assert.Contains("t-fix-1", Rewind.TurnCandidates(fix));
        Assert.DoesNotContain("t-main-1", Rewind.TurnCandidates(fix));
        Assert.Equal(fixBefore.Count + 1, Rewind.TurnCandidates(fix).Count);
    }
}
