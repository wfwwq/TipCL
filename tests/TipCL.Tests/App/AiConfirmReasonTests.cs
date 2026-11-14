// M4-H R19:确认卡「为什么需要你确认」那一行的回归(与 R5 的预览断链同源)。
//
// 确认卡本来就有 reason 那一行(空则不渲染),Windows 版由权限裁决带出理由;Linux 侧的断链是:
// ToolRunner.Decide 只给 allow/ask/deny、没有理由文本,两个调用点又把「工具标签」塞进 label 槽、
// reason 槽恒传空串 —— 于是界面上永远看不到「为什么需要你确认」。这里从头到尾锁一遍:
//   1) Core 按档位/规则算出的 (裁决, 理由) 对不对 → Core/AiPermissionReasonTests;
//   2) 走真链路(ToolRunner.RunAsync → 页面的确认回调):确认卡必须拿到 Core 的理由;
//   3) 卡片真的把那行画出来;理由为空时那行不出现(不是只有数据、没有界面);
//   4) 只看不动档根本不弹卡(理由只服务于「真的会问」的情形)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TipCL.App.Common;
using TipCL.App.Pages.Ai;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.App;

public class AiConfirmReasonTests
{
    /// <summary>只读的假门面:本文件的用例都在「要不要执行」这一步停下,执行体不该被调到。</summary>
    private sealed class FakeApi : ToolRunner.IBackend
    {
        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
            => Task.FromResult<T?>(default);
    }

    private static string NewInstance(string tag)
    {
        string name = "reason-" + tag + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        Directory.CreateDirectory(Instances.GetInstancePath(name));
        return name;
    }

    // ───────────────────────────── 卡片层:那一行到底画没画 ─────────────────────────────

    /// <summary>
    /// 卡片收得到理由就画一行、收不到就整行不渲染。判定用「正文里缩进 24、字号 12 的那行」——
    /// 删除类卡片另有一条同样缩进/字号的警告,按文案排掉(本用例用非删除工具,不受影响)。
    /// </summary>
    [Fact]
    public void ConfirmCard_RendersReasonRow_AndOmitsItWhenReasonIsEmpty() => TestApp.RunAsync(async () =>
    {
        var shown = new ConfirmCard("install_mod", "安装模组 sodium", "写操作需要确认",
            new List<string>(), true, (_, _, _) => { });
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.Equal("写操作需要确认", ReasonRow(shown));
        // 标签仍在自己的槽里:理由不许串到标签槽,标签也不许被理由挤掉
        Assert.True(HasText(shown, "安装模组 sodium"));

        var blank = new ConfirmCard("install_mod", "安装模组 sodium", "",
            new List<string>(), true, (_, _, _) => { });
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.Null(ReasonRow(blank));
        // 不是「数据没传」而是「整行没渲染」:正文里确实少一个文本块
        Assert.True(TextCount(blank) < TextCount(shown), $"空理由卡文本块 {TextCount(blank)} 应少于有理由卡 {TextCount(shown)}");
    });

    // ───────────────────────────── 链路层:Core 的理由一路到卡片 ─────────────────────────────

    /// <summary>
    /// 真的跑一次工具调用(RunAsync → 页面的确认回调 → 卡片),断言卡片上那行等于 Core 算出的理由。
    /// 两个页面各一条用例:调用点各写各的,只测一个会漏掉另一个的断链。
    /// </summary>
    [Fact]
    public void AiPage_ShowsTheReasonFromCore_DefaultMode() => TestApp.RunAsync(async () =>
    {
        string inst = NewInstance("default");
        var page = new AiPage();
        page.Runner.Instance = inst;
        page.Runner.PermissionMode = "default";
        ConfirmCard card = await DriveConfirm(page.Content as ILogical, page.Runner,
            new ToolCall("r1", "install_mod", PyDict.Of("name", "sodium", "instance", inst)));
        Assert.Equal("写操作需要确认", ReasonRow(card));
        // 标签是「安装模组 sodium」这类人话,不能和理由混为一谈
        Assert.Contains("sodium", LabelRow(card));
    });

    [Fact]
    public void AiFixView_ShowsTheReasonFromCore_DefaultMode() => TestApp.RunAsync(async () =>
    {
        string inst = NewInstance("fix");
        var fix = new AiFixView(InProcessApi.Instance, PyDict.Of("instance", inst));
        fix.Runner.Instance = inst;
        fix.Runner.PermissionMode = "default";
        ConfirmCard card = await DriveConfirm(fix, fix.Runner,
            new ToolCall("r2", "install_mod", PyDict.Of("name", "sodium", "instance", inst)));
        Assert.Equal("写操作需要确认", ReasonRow(card));
        Assert.Contains("sodium", LabelRow(card));
    });

    /// <summary>理由跟着档位与副作用走,不是一句写死的常量:写直接执行档下删除类仍要问,理由也不同。</summary>
    [Fact]
    public void Pages_ShowTheReasonFromCore_AcceptEditsDelete() => TestApp.RunAsync(async () =>
    {
        string inst = NewInstance("accept");
        string mods = Path.Combine(Instances.GetInstancePath(inst), "mods");
        Directory.CreateDirectory(mods);
        File.WriteAllBytes(Path.Combine(mods, "sodium.jar"), new byte[512]);

        var page = new AiPage();
        page.Runner.Instance = inst;
        page.Runner.PermissionMode = "acceptEdits";
        ConfirmCard card = await DriveConfirm(page.Content as ILogical, page.Runner,
            new ToolCall("r3", "delete_mod", PyDict.Of("instance", inst, "filename", "sodium.jar")));
        Assert.Equal("删除操作仍需确认", ReasonRow(card));
        Assert.Contains("sodium.jar", LabelRow(card));
        // 理由不是「写操作需要确认」这类默认档文案
        Assert.NotEqual("写操作需要确认", ReasonRow(card));
    });

    /// <summary>只看不动档:不弹卡(也就没有理由一行),工具调用被明确拒掉。</summary>
    [Fact]
    public void PlanMode_DeniesWithoutShowingAnyCard() => TestApp.RunAsync(async () =>
    {
        string inst = NewInstance("plan");
        var page = new AiPage();
        page.Runner.Instance = inst;
        page.Runner.PermissionMode = "plan";

        string result = await page.Runner.RunAsync(
            new ToolCall("r4", "install_mod", PyDict.Of("name", "sodium", "instance", inst)));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.Contains("permission_denied", result);
        Assert.Null(FindCard<ConfirmCard>(page.Content as ILogical));
    });

    // ───────────────────────────── 驱动与读取 ─────────────────────────────

    /// <summary>
    /// 起一次真实调用并等卡片挂上停靠区,读到卡后判「拒绝」把页面的等待解掉(不留悬挂的 waiter)。
    /// 页面的确认走 Dispatcher.UIThread.InvokeAsync:排一次队列再找卡。
    /// </summary>
    private static async Task<ConfirmCard> DriveConfirm(ILogical? owner, ToolRunner runner, ToolCall call)
    {
        Task<string> running = runner.RunAsync(call);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        ConfirmCard? card = FindCard<ConfirmCard>(owner);
        Assert.NotNull(card);
        card!.Reject();
        await running;
        return card;
    }

    private static readonly Thickness Indent = new(24, 0, 0, 0);

    /// <summary>理由那一行的文案(没有就返回 null)。删除类卡片的固定警告行按文案排掉。</summary>
    private static string? ReasonRow(ILogical card) => AllDescendants<TextBlock>(card)
        .Where(t => t.Margin == Indent && Math.Abs(t.FontSize - 12) < 0.01)
        .Select(t => t.Text)
        .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)
            && !t!.StartsWith("删除类操作:", StringComparison.Ordinal));

    /// <summary>工具标签那一行(FontSize 13、加粗,缩进 9/10)。</summary>
    private static string LabelRow(ILogical card) => AllDescendants<TextBlock>(card)
        .Where(t => t.Margin == new Thickness(9, 0, 10, 0) && t.FontSize > 12.5)
        .Select(t => t.Text ?? "")
        .FirstOrDefault(t => t.Length > 0) ?? "";

    private static bool HasText(ILogical card, string text)
        => AllDescendants<TextBlock>(card).Any(t => t.Text == text);

    private static int TextCount(ILogical card) => AllDescendants<TextBlock>(card).Count();

    private static IEnumerable<T> AllDescendants<T>(ILogical root)
    {
        foreach (ILogical child in root.GetLogicalChildren())
        {
            if (child is T found)
            {
                yield return found;
            }
            foreach (T deeper in AllDescendants<T>(child))
            {
                yield return deeper;
            }
        }
    }

    private static T? FindCard<T>(ILogical? root) where T : class
    {
        if (root == null)
        {
            return null;
        }
        foreach (ILogical child in root.GetLogicalChildren())
        {
            if (child is T found)
            {
                return found;
            }
            if (FindCard<T>(child) is { } deeper)
            {
                return deeper;
            }
        }
        return null;
    }
}
