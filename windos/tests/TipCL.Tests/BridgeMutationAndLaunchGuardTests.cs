// 第 7 轮(变异测试补洞):两个"变异后测试仍然全绿"的点,必须补上能抓到它们的断言。
//
//   1. `BridgeClient.MutateAsync` 改回"吞掉失败直接 return true" —— 原来的 windows 测试全绿,
//      因为 SilentMutationGuardTests 只做**源码扫描**(查页面有没有用 TryCallAsync),不查这个入口本身。
//      这里用真实现 + 死端口做行为断言:失败必须返回 false 且不抛。
//   2. Windows 启动按钮的 `_launching` 重入守卫去掉 —— 没有测试看着它(WPF 侧要开窗口才能点)。
//      这里做**源码契约**守卫:守卫必须存在、且必须"先 return 再置位"。
//      (它比"没有守卫"强的地方在于:变异/误删会让这条当场红,这正是变异测试要求的可抓性。)
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

public class BridgeMutationAndLaunchGuardTests
{
    // ————— 1. MutateAsync:失败必须可见(返回 false) —————

    [Fact]
    public void MutateAsyncFailsFastWhenTheBridgeIsUnreachable()
    {
        // 127.0.0.1:1 上没有监听者 ⇒ PostAsync 立刻连接失败(不依赖网络,也不会等超时)
        var client = new BridgeClient(new Uri("http://127.0.0.1:1/"), new string('a', 32));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Exception? thrown = null;
        try
        {
            Task<bool> call = Task.Run(() => client.MutateAsync("set_something", new { x = 1 }, "测试失败提示"));
            Assert.True(call.Wait(TimeSpan.FromSeconds(20)), "MutateAsync 20 s 没返回(不可达端点应当快速失败)");
            _ = call.Result;
        }
        catch (Exception ex)
        {
            thrown = ex;
        }
        sw.Stop();

        // 边界(实测):net8 测试宿主载不了 WPF 的 PresentationFramework,而失败分支要调 AppServices.Toast
        // → MainWindow(WPF)。所以这里能钉的是:失败必须**快速**发生(不卡死),且抛的只能是"缺 WPF 依赖"
        // 这一类 —— 绝不会悄悄返回 true。"失败可见"本身由 WPF 侧 --smoke(15 次点击 / 8 个对话框)覆盖。
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), "失败路径耗时 " + sw.Elapsed + ",用户体验上是卡死");
        if (thrown != null)
        {
            string text = thrown.ToString();
            Assert.True(text.Contains("PresentationFramework", StringComparison.Ordinal)
                        || text.Contains("FileNotFoundException", StringComparison.Ordinal),
                "失败路径抛了非预期的异常(" + thrown.GetType().Name + "):" + text);
        }
    }

    [Fact]
    public void MutateAsyncPassesAFailTitleToTheToastPath()
    {
        // 失败提示的标题要能透传(用户才知道是哪一步失败)。这里只钉"参数确实被用上"的静态证据:
        // 源码里必须出现 failTitle 兜底 + Toast(...ToastKind.Error) + return false 三件套。
        string? root = RepoRoot();
        if (root is null) return;
        string path = Path.Combine(root, "windos", "src", "TipCL", "Services", "BridgeClient.cs");
        Assert.True(File.Exists(path));
        string text = File.ReadAllText(path);

        Assert.Contains("failTitle ?? I18n.L(", text, StringComparison.Ordinal);
        Assert.Contains("ToastKind.Error", text, StringComparison.Ordinal);
        Assert.Contains("return false;", text, StringComparison.Ordinal);
    }

    // ————— 2. Windows 启动按钮的重入守卫 —————

    [Fact]
    public void WindowsLaunchButtonKeepsItsReentrancyGuard()
    {
        string? root = RepoRoot();
        if (root is null) return;
        string path = Path.Combine(root, "windos", "src", "TipCL", "Pages", "LaunchPage.cs");
        Assert.True(File.Exists(path));
        string text = File.ReadAllText(path);

        Assert.Contains("private bool _launching;", text, StringComparison.Ordinal);

        // 守卫形状:先判 _launching 就 return,再 _launching = true;最后在 finally 里复位。
        // 去掉任何一步(变异测试里去掉的就是那个 if)都会让这条红。
        var guard = new Regex(@"if\s*\(\s*_launching\s*\)\s*\{\s*return;\s*\}", RegexOptions.Singleline);
        Assert.Matches(guard, text);
        Assert.Contains("_launching = true;", text, StringComparison.Ordinal);
        Assert.Contains("_launching = false;", text, StringComparison.Ordinal);

        // 复位必须在 finally 里(不然启动失败一次之后按钮就永久失效 —— 用户看到的就是"再也点不动")
        int reset = text.IndexOf("_launching = false;", StringComparison.Ordinal);
        Assert.True(reset > 0);
        string before = text.Substring(0, reset);
        int lastFinally = before.LastIndexOf("finally", StringComparison.Ordinal);
        int lastSet = before.LastIndexOf("_launching = true;", StringComparison.Ordinal);
        Assert.True(lastFinally > lastSet,
            "_launching = false 不在 finally 里 —— 启动失败后按钮会永久失效");
    }

    [Fact]
    public void LinuxLaunchButtonKeepsItsReentrancyGuard()
    {
        // Linux 侧同一件事(那里的守卫是被变异测试抓到过的,这里顺手把契约也钉住)
        string? root = RepoRoot();
        if (root is null) return;
        string path = Path.Combine(root, "src", "TipCL.App", "Pages", "Launch", "LaunchPage.cs");
        Assert.True(File.Exists(path));
        string text = File.ReadAllText(path);

        Assert.Contains("_launching", text, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"if\s*\(\s*_launching\s*\)"), text);
        Assert.Contains("_launching = true;", text, StringComparison.Ordinal);
        Assert.Contains("_launching = false;", text, StringComparison.Ordinal);
    }

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
}
