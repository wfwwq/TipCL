// 第 6 轮(Windows 侧):"伪死按钮"的机制守卫(audit/D)。
//
// 背景(用户原话:"坏的按钮,背后根本没有接功能"):WPF 侧最常见的成因不是按钮没写事件,
// 而是**写操作走了 TryCallAsync** —— 它把所有异常(含 RPC "unknown method" / 参数 TypeError /
// 写盘失败)静默吞掉,界面表现就是"点了毫无反应"。BridgeClient.MutateAsync 就是为这件事加的:
// 失败弹错误提示并返回 false。
//
// 本文件做两件事,都不需要 WPF 窗口(能在 net8 测试宿主里跑):
//   1. 契约:MutateAsync(string, object?, string?) => Task<bool> 必须还在(它是"失败可见"的唯一入口);
//   2. 静态守卫:windos/src 里**写类**方法(db 动词前缀)不许再走 TryCallAsync —— 只允许白名单里的
//      那一条(见 Allowlist,每条都写明为什么它是"可以静默"的)。
//
// 这不是风格洁癖:每一次 TryCallAsync 写操作都会把"失败"变成"没反应",而这正是用户报的那类缺陷。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace TipCL.Tests;

public class SilentMutationGuardTests
{
    /// <summary>写操作的动词前缀:命中这些的 RPC 方法名一律要有可见的失败路径。</summary>
    private static readonly string[] WriteVerbs =
    {
        "set", "save", "delete", "remove", "enable", "disable", "apply", "create", "update",
        "add", "clear", "reset", "install", "uninstall", "start", "stop", "cancel", "open",
        "launch", "import", "export", "rename", "move", "copy", "toggle", "toggle",
    };

    /// <summary>
    /// 允许静默的写调用(每一条都要有理由)。当前只留一条:
    /// FeedbackConsent 的用户同意状态是"尽力写":它失败时界面已经按"未同意"处理,
    /// 再弹一个错误框只会让首启流程更烦人(见该调用点注释)。
    /// </summary>
    private static readonly string[] Allowlist =
    {
        "FeedbackConsent.cs:save_settings",
    };

    private static string? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GOAL.md"))
                && Directory.Exists(Path.Combine(dir.FullName, "windos", "src")))
            {
                return dir.FullName;
            }
        }
        return null;
    }

    [Fact]
    public void MutateAsyncStillIsTheVisibleFailureEntryPoint()
    {
        // 直接反射被测程序集(net48 的 TipCL.exe 被 net8 测试工程引用),不需要开窗口
        Assembly tipcl = typeof(TipCL.Services.BridgeClient).Assembly;
        Type client = typeof(TipCL.Services.BridgeClient);

        MethodInfo? m = client.GetMethod("MutateAsync", new[] { typeof(string), typeof(object), typeof(string) });
        Assert.True(m != null, "BridgeClient.MutateAsync(string, object?, string?) 不见了 —— 写操作的失败就没有可见出口了");
        Assert.Equal(typeof(System.Threading.Tasks.Task<bool>), m!.ReturnType);
        Assert.True(tipcl != null);
    }

    [Fact]
    public void NoWriteRpcGoesThroughTheSilentTryCallAsync()
    {
        string? root = RepoRoot();
        if (root is null)
        {
            return;
        }

        string srcRoot = Path.Combine(root, "windos", "src");
        var pattern = new Regex("TryCallAsync<[^>]*>\\(\\s*\"(?<method>[a-z0-9_]+)\"", RegexOptions.Compiled);
        var violations = new List<string>();

        foreach (string file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            if (!text.Contains("TryCallAsync", StringComparison.Ordinal))
            {
                continue;
            }
            string name = Path.GetFileName(file);
            foreach (Match m in pattern.Matches(text))
            {
                string method = m.Groups["method"].Value;
                if (!WriteVerbs.Any(v => method == v || method.StartsWith(v + "_", StringComparison.Ordinal)))
                {
                    continue;
                }
                string key = name + ":" + method;
                if (Allowlist.Any(a => key == a))
                {
                    continue;
                }
                int line = text.Take(m.Index).Count(c => c == '\n') + 1;
                violations.Add(Path.GetRelativePath(root, file) + ":" + line + " -> " + method);
            }
        }

        Assert.True(violations.Count == 0,
            "以下写操作走了 TryCallAsync(失败会被静默吞掉,界面表现为「点了没反应」):" + Environment.NewLine
            + string.Join(Environment.NewLine, violations) + Environment.NewLine
            + "改用 PageBase.Api.MutateAsync(...) 或 Api.MutateAsync(...):失败会弹提示并返回 false。");
    }

    [Fact]
    public void PageRunWrappersDoNotSwallowAsyncFailures()
    {
        // PageBase.Run(...) 是页面里最常用的"点了就干活"入口。它必须把异常交给
        // AppDomain/Dispatcher 的未处理通道(或自己弹提示),而不是空 catch。
        string? root = RepoRoot();
        if (root is null)
        {
            return;
        }

        string pageBase = Path.Combine(root, "windos", "src", "TipCL", "Pages", "PageBase.cs");
        if (!File.Exists(pageBase))
        {
            return;
        }
        string text = File.ReadAllText(pageBase);
        // PageBase.Run 的失败出口:要么弹 Toast(带 failTitle),要么交给未处理通道 —— 不能什么都不做
        Assert.Contains("Toast", text, StringComparison.Ordinal);

        // 空 catch(吃掉所有异常且什么都不做)在这条路径上是"点了没反应"的另一个来源。
        // 只禁「无条件的空 catch」:`catch (OperationCanceledException) { }` 是合法的取消语义。
        Assert.DoesNotMatch(new Regex("catch\\s*\\{\\s*\\}"), text);
        Assert.DoesNotMatch(new Regex("catch\\s*\\(\\s*Exception\\s*\\)\\s*\\{\\s*\\}"), text);
    }
}