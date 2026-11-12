// M4-H R2 测试:斜杠命令的纯逻辑 + 记忆面板 / 上下文占用环能构建。
//
// 斜杠命令的匹配、选择移动、撤回轮次计算都不依赖界面,照 Windows 版 AiChatOps 的用例口径写成 xunit。
using System.Collections.Generic;
using System.Linq;
using Avalonia.LogicalTree;
using TipCL.App.Pages.Ai;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.App;

public class AiChatOpsTests
{
    [Fact]
    public void MatchSlashOnlyForSlashInputWithoutSpace()
    {
        Assert.Empty(AiChatOps.MatchSlash("你好"));
        Assert.Empty(AiChatOps.MatchSlash("/撤回 现在"));
        Assert.Empty(AiChatOps.MatchSlash(null));
        // 只打一个斜杠:全部命令
        Assert.Equal(AiChatOps.Commands.Count, AiChatOps.MatchSlash("/").Count);
    }

    [Fact]
    public void MatchSlashPrefersNameAndAliasPrefix()
    {
        List<SlashCommand> crash = AiChatOps.MatchSlash("/crash");
        Assert.NotEmpty(crash);
        Assert.Equal("崩溃分析", crash[0].Name);

        List<SlashCommand> chinese = AiChatOps.MatchSlash("/撤");
        Assert.Equal("撤回", chinese[0].Name);

        // 别名前缀也算命中
        List<SlashCommand> undo = AiChatOps.MatchSlash("/undo");
        Assert.Equal("撤回", undo[0].Name);
    }

    [Fact]
    public void MatchSlashFallsBackToContains()
    {
        List<SlashCommand> byDesc = AiChatOps.MatchSlash("/性能");
        Assert.Contains(byDesc, c => c.Name == "装Fabric");
    }

    [Fact]
    public void MatchSlashHandlesLocalCommands()
    {
        List<SlashCommand> local = AiChatOps.MatchSlash("/new");
        Assert.Equal("新对话", local[0].Name);
        Assert.Equal("new_chat", local[0].Local);
        Assert.Equal("", local[0].Prompt);
    }

    [Fact]
    public void ExactSlashMatchesWholeInputOnly()
    {
        Assert.Equal("撤回", AiChatOps.ExactSlash("/撤回")!.Name);
        Assert.Equal("撤回", AiChatOps.ExactSlash(" /undo ")!.Name);
        Assert.Null(AiChatOps.ExactSlash("/撤"));
        Assert.Null(AiChatOps.ExactSlash("撤回"));
        Assert.Null(AiChatOps.ExactSlash("/"));
        Assert.Null(AiChatOps.ExactSlash("/撤回 现在"));
    }

    [Fact]
    public void PromptCommandsCarryTheSameWordingAsWelcomeChips()
    {
        SlashCommand crash = AiChatOps.ExactSlash("/崩溃分析")!;
        Assert.Equal("刚才启动失败了,读一下日志告诉我原因", crash.Prompt);
        SlashCommand fabric = AiChatOps.ExactSlash("/fabric")!;
        Assert.Contains("1.20.1 Fabric", fabric.Prompt);
    }

    [Fact]
    public void MoveSelectionWrapsAround()
    {
        Assert.Equal(-1, AiChatOps.MoveSelection(0, 1, 0));
        Assert.Equal(1, AiChatOps.MoveSelection(0, 1, 3));
        Assert.Equal(2, AiChatOps.MoveSelection(0, -1, 3));
        Assert.Equal(0, AiChatOps.MoveSelection(2, 1, 3));
        Assert.Equal(0, AiChatOps.MoveSelection(-1, 1, 3));
        Assert.Equal(2, AiChatOps.MoveSelection(-1, -1, 3));
    }

    [Fact]
    public void RoundsToRewindCountsNonSteerUserMessages()
    {
        var messages = new List<(string Role, string Id, string Content)>
        {
            ("user", "u1", "第一句"),
            ("assistant", "a1", "回答"),
            ("user", "u2", "第二句"),
            ("user", "steer_9", "插一句"),
            ("assistant", "a2", "回答二"),
        };
        // 「第二句」是最后一轮(插队消息不算轮) → 1 轮;「第一句」之后还有两轮非插队用户消息 → 2 轮
        Assert.Equal(1, AiChatOps.RoundsToRewind(messages, "第二句"));
        Assert.Equal(2, AiChatOps.RoundsToRewind(messages, "第一句"));
        // 目标不存在
        Assert.Equal(-1, AiChatOps.RoundsToRewind(messages, "没说过的话"));
        // 目标是插队消息:不能作为撤回锚点
        Assert.Equal(-1, AiChatOps.RoundsToRewind(messages, "插一句"));
        Assert.Equal(-1, AiChatOps.RoundsToRewind(null, "x"));
    }

    [Fact]
    public void RoundsToRewindCanTargetRepeatedMessages()
    {
        var messages = new List<(string Role, string Id, string Content)>
        {
            ("user", "u1", "同一句"),
            ("assistant", "a1", "回答"),
            ("user", "u2", "同一句"),
            ("assistant", "a2", "回答二"),
        };
        Assert.Equal(1, AiChatOps.RoundsToRewind(messages, "同一句"));
        Assert.Equal(2, AiChatOps.RoundsToRewind(messages, "同一句", occurrenceFromEnd: 1));
    }

    [Fact]
    public void ToolResultParsersCoverBothPunctuationStyles()
    {
        Assert.Equal("a;b", AiChatOps.ForgottenOf("已删除 2 条: a;b"));
        Assert.Equal("a;b", AiChatOps.ForgottenOf("已删除 2 条： a;b"));
        Assert.Null(AiChatOps.ForgottenOf("已记住: x"));
        Assert.Null(AiChatOps.ForgottenOf(""));
        Assert.Equal("1.20.1-fabric", AiChatOps.InstalledVersionOf("版本安装完成: 1.20.1-fabric"));
        Assert.Equal("1.20.1", AiChatOps.InstalledVersionOf("版本安装完成 1.20.1"));
        Assert.Equal("", AiChatOps.InstalledVersionOf("装好了"));
    }
}

public class AiPageResidualTests
{
    [Fact]
    public void MemoryPanel_Constructs_AndReadsFileMemory() => TestApp.Run(() =>
    {
        var panel = new AiMemoryPanel();
        Assert.NotNull(panel.Child);
    });

    [Fact]
    public void SlashMenu_ShowsAndHides() => TestApp.Run(() =>
    {
        var menu = new SlashMenu();
        Assert.False(menu.IsVisible);
        menu.Show(AiChatOps.MatchSlash("/"), 0);
        Assert.True(menu.IsVisible);
        menu.Hide();
        Assert.False(menu.IsVisible);
        // 空候选要自动收起
        menu.Show(new List<SlashCommand>(), -1);
        Assert.False(menu.IsVisible);
    });

    [Fact]
    public void AiPage_RendersUsageRingAndSlashMenu() => TestApp.Run(() =>
    {
        var page = new AiPage();
        Assert.NotNull(page.Content);
        List<Avalonia.Controls.Control> controls = page
            .GetLogicalDescendants()
            .OfType<Avalonia.Controls.Control>()
            .ToList();
        // 头部有占用环的宿主 + 占用百分比标签,底部有斜杠候选条(默认收起)
        Assert.Contains(controls, c => c is SlashMenu);
        Assert.Contains(controls, c => c is Avalonia.Controls.TextBlock t && (t.Text ?? "").Contains('%'));
    });

    [Fact]
    public void UsageRatioUsesWindowAndRequests()
    {
        // 每轮平均输入 / 窗口 = 占用比例(照 AiUiText.ContextRatio)
        Assert.Equal(0.5, AiUiText.ContextRatio(5000, 1, 10000), 3);
        Assert.Equal(0.5, AiUiText.ContextRatio(10000, 2, 10000), 3);
        Assert.Equal(0.0, AiUiText.ContextRatio(0, 0, 10000), 3);
        Assert.Equal(1.0, AiUiText.ContextRatio(99999, 1, 10000), 3);
        Assert.Equal("1.5k", AiUiText.Tokens(1500));
    }
}
