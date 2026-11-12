// M4-H R5:确认链路的断链回归(核心用例)。
//
// 「界面能显示 diff 预览」和「调用点真的把 diff 交给卡片」是两件独立的事:Linux 侧就出现过
// 预览框写好了、而两个调用点都传 `new List<string>()` 的空档 —— 只测 Preview 本身查不出来。
// 所以这里从上往下锁三层:
//   1) 算:写工具的 Preview 必须给出非空、可被 LooksLikeDiff 识别的行(附录在 Core/AiPreviewTests);
//   2) 传:ToolRunner 执行写工具时,确认回调必须拿到这份预览(且此时还没落盘);
//   3) 到卡:驱动页面自己的确认回调,确认卡必须真的拿到那些行。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using TipCL.App.Common;
using TipCL.App.Pages.Ai;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.App;

public class AiConfirmChainTests
{
    /// <summary>写工具要先过确认回调,执行体不会被调到;真调到了也什么都别做。</summary>
    private sealed class FakeApi : ToolRunner.IBackend
    {
        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
            => Task.FromResult<T?>(default);
    }

    private static string NewInstance(string tag)
    {
        string name = "chain-" + tag + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        Directory.CreateDirectory(Instances.GetInstancePath(name));
        return name;
    }

    private static string WriteConfig(string instance, string content)
    {
        string file = Path.Combine(Instances.GetInstancePath(instance), "config", "sodium.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
        return file;
    }

    // ───────────────────────────── 第 2 层:ToolRunner → 确认回调 ─────────────────────────────

    [Fact]
    public async Task WriteTool_ConfirmCallback_ReceivesNonEmptyDiffPreview()
    {
        string inst = NewInstance("runner");
        string file = WriteConfig(inst, "a = 1\n");
        var runner = new ToolRunner(new FakeApi()) { Instance = inst, PermissionMode = "default" };
        PyDict? seen = null;
        string label = "";
        runner.Confirm = (call, reason, preview) =>
        {
            label = reason;
            seen = preview;
            return Task.FromResult((false, false));
        };

        var call = new ToolCall("c1", "write_mod_config",
            PyDict.Of("instance", inst, "path", "sodium.toml", "content", "a = 1\nb = 2\n"));
        await runner.RunAsync(call);

        Assert.Equal(ToolCallStatus.DENIED, call.status);
        Assert.Contains("sodium.toml", label);
        Assert.NotNull(seen);
        Assert.Equal("diff", Py.S(seen!.Get("kind")));

        List<string> lines = Preview.Flatten(seen);
        Assert.NotEmpty(lines);
        Assert.True(AiUiText.LooksLikeDiff(lines));
        Assert.Contains("+b = 2", lines);
        Assert.Contains("@@ 将要写入的变更:", lines);
        // 预览是写之前算的:拒绝之后原来的内容还在
        Assert.Equal("a = 1\n", File.ReadAllText(file));
    }

    [Fact]
    public async Task DeleteTool_ConfirmCallback_ReceivesSummaryPreview()
    {
        string inst = NewInstance("runner-del");
        string mods = Path.Combine(Instances.GetInstancePath(inst), "mods");
        Directory.CreateDirectory(mods);
        File.WriteAllBytes(Path.Combine(mods, "sodium.jar"), new byte[1024]);
        var runner = new ToolRunner(new FakeApi()) { Instance = inst, PermissionMode = "default" };
        PyDict? seen = null;
        runner.Confirm = (_, _, preview) =>
        {
            seen = preview;
            return Task.FromResult((false, false));
        };

        await runner.RunAsync(new ToolCall("c1", "delete_mod", PyDict.Of("instance", inst, "filename", "sodium.jar")));

        Assert.NotNull(seen);
        Assert.Equal("summary", Py.S(seen!.Get("kind")));
        Assert.Contains("- mods/sodium.jar(1.0 KB)", Preview.Flatten(seen));
        Assert.True(File.Exists(Path.Combine(mods, "sodium.jar")));
    }

    [Fact]
    public async Task ReadOnlyTool_NeverAsksForConfirm()
    {
        string inst = NewInstance("runner-read");
        var runner = new ToolRunner(new FakeApi()) { Instance = inst, PermissionMode = "default" };
        int asked = 0;
        runner.Confirm = (_, _, _) =>
        {
            asked++;
            return Task.FromResult((true, true));
        };
        await runner.RunAsync(new ToolCall("c1", "list_mods", PyDict.Of("instance", inst)));
        Assert.Equal(0, asked);
    }

    // ───────────────────────────── 第 3 层:页面 → ConfirmCard ─────────────────────────────

    /// <summary>
    /// 两个页面的确认回调都得把预览交给卡片。走的是页面自己的 `Confirm`(不是另写一遍构造表达式),
    /// 所以哪天有人把调用点改回空列表,这条就会红。
    /// </summary>
    [Fact]
    public void Pages_HandThePreviewToConfirmCard() => TestApp.RunAsync(async () =>
    {
        string inst = NewInstance("page");
        WriteConfig(inst, "a = 1\n");
        PyDict args = PyDict.Of("instance", inst, "path", "sodium.toml", "content", "a = 2\n");

        var page = new AiPage();
        ConfirmCard? card = await DriveConfirm(page.Content as ILogical, page.Runner, args, inst);
        Assert.NotNull(card);
        List<string> lines = card!.PreviewLines.ToList();
        Assert.NotEmpty(lines);
        Assert.True(AiUiText.LooksLikeDiff(lines));
        Assert.Contains("-a = 1", lines);
        Assert.Contains("+a = 2", lines);
        // 真的画出来了:- / + 两行各在自己的框里,并且按 diff 着色(不是只有数据、没有界面)
        Assert.Equal(new Color(40, 214, 69, 69), DiffFrameColor(card, "-a = 1"));
        Assert.Equal(new Color(40, 46, 155, 107), DiffFrameColor(card, "+a = 2"));

        var fix = new AiFixView(InProcessApi.Instance, PyDict.Of("instance", inst));
        ConfirmCard? fixCard = await DriveConfirm(fix, fix.Runner, args, inst);
        Assert.NotNull(fixCard);
        Assert.True(AiUiText.LooksLikeDiff(fixCard!.PreviewLines.ToList()));
        Assert.Contains("+a = 2", fixCard.PreviewLines);
    });

    /// <summary>
    /// 驱动页面自己的确认回调:调完在页面的可视控件树里把那张卡捞出来,读数后判「拒绝」把页面的等待解掉,
    /// 不留悬挂的 waiter(否则下一次换页/清空会把这张卡再判一次)。
    /// </summary>
    private static async Task<ConfirmCard?> DriveConfirm(ILogical? owner, ToolRunner runner, PyDict args, string instance)
    {
        Assert.NotNull(runner.Confirm);
        var call = new ToolCall("c-" + Guid.NewGuid().ToString("N").Substring(0, 6), "write_mod_config", args);
        PyDict? preview = Preview.ChangePreview(call.name, args, instance);
        Task<(bool ok, bool always)> waiting = runner.Confirm!(call, Tools.Label(call.name, args), preview);
        // 页面的确认走 Dispatcher.UIThread.InvokeAsync:排一下队列再找卡(内联执行时这步无副作用)
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        ConfirmCard? card = FindCard(owner);
        card?.Reject();
        await waiting;
        return card;
    }

    /// <summary>
    /// 顺手清掉的同类断链:任务卡的**工具名**原先两个页面都写死空串(卡片靠它决定后续动作),
    /// 现在由 TaskStarted 回调带过来。这里直接驱动页面接好的那个回调,再在页面树里读卡片。
    /// </summary>
    [Fact]
    public void Pages_HandTheToolNameToTaskCard() => TestApp.RunAsync(async () =>
    {
        var page = new AiPage();
        page.Runner.TaskStarted!("tk-page", "安装 1.20.1", "install_game");
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        TaskCard? card = FindCard<TaskCard>(page.Content as ILogical);
        Assert.NotNull(card);
        Assert.Equal("tk-page", card!.TaskId);
        Assert.Equal("install_game", card.Tool);

        var fix = new AiFixView(InProcessApi.Instance, PyDict.Of("instance", "default"));
        fix.Runner.TaskStarted!("tk-fix", "安装 sodium", "install_mod");
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        TaskCard? fixCard = FindCard<TaskCard>(fix);
        Assert.NotNull(fixCard);
        Assert.Equal("install_mod", fixCard!.Tool);
    });

    private static ConfirmCard? FindCard(ILogical? root) => FindCard<ConfirmCard>(root);

    /// <summary>取那一行 diff 所在框的底色(确认卡按 +/- 给底色;拿不到就返回 null)。</summary>
    private static Color? DiffFrameColor(ILogical root, string line)
    {
        TextBlock? text = AllDescendants<TextBlock>(root).FirstOrDefault(t => t.Text == line);
        return (text?.Parent as Border)?.Background is ISolidColorBrush brush ? brush.Color : null;
    }

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

    /// <summary>在页面的逻辑控件树里找第一张指定类型的卡(页面把卡放进停靠区/消息列,不另开窗口)。</summary>
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
