// R31-CIFLAKE:把 ubuntu CI 那条间歇红(exit 127)的机制钉进 `dotnet test`,并给测试进程加一道写盘边界。
//
// 事故形态(有 CI 原始日志):`ci.yml` 的 `test (ubuntu-latest)` 里 batch 1/2(App 组)跑绿之后,
// 紧接着的 batch 2/2 **一条都没跑成** —— 日志是
//   /usr/share/dotnet/dotnet: line 1: TipCL-fake-package-bytes-xxx...: command not found
//   ##[error]Process completed with exit code 127.
// 即:runner 上的 dotnet 宿主文件被"假安装包字节"覆盖了,而那段字符串全仓只有一个产地 ——
// 自更新正常路径用例里那份假包(`SelfUpdateFeedbackTests`)。完整链条见 TestWriteGuard 的文件头。
//
// 本文件负责两件事:
//   1 把**事件 → 替换**这一段钉住:泄漏在外面的 MainWindow 实例收到 `update_staged` 会真的走到
//     `apply_update` → `Updater.ApplyStaged()`,所以"更新目标是谁"这件事在测试里必须由测试说了算;
//   2 让写盘边界**当场红**:目标越界就抛 `OutsideIsolationWriteException`,不再表现为另一条命令的 127。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using TipCL.App.Common;
using TipCL.App.Shell;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.App;

public class UpdateTargetIsolationTests
{
    /// <summary>这条用例自己造的包内容(与 SelfUpdateFeedbackTests 那份区分开,断言才有指向性)。</summary>
    private const string ProbePackage = "R31-CIFLAKE-REGRESSION-PACKAGE-" + "zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz";

    private static async Task<bool> PumpAsync(Func<bool> ready, int timeoutMs)
    {
        var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < end)
        {
            if (ready()) return true;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        return ready();
    }

    /// <summary>
    /// 把还开着的对话框关掉(宿主 = <see cref="Dlg.Owner"/> 的 OwnedWindows + 桌面生命周期里除主窗口之外
    /// 的窗口)。照 `PhoneSyncTests.ResetLeakedDlgState()` / `InstanceDetailCrashTests:312` 的做法:
    /// "有人把泄漏的对话框关掉"正是让上面那条链落地的另一条路(`Closed` 处理器把 Confirm 判成 false)。
    /// </summary>
    private static async Task CloseLeakedDialogsAsync()
    {
        var desktop = Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        for (int i = 0; i < 20 && Dlg.AnyOpen; i++)
        {
            if (Dlg.Owner != null)
                foreach (var w in new List<Window>(Dlg.Owner.OwnedWindows)) { try { w.Close(); } catch (Exception) { } }
            if (desktop != null)
                foreach (var w in new List<Window>(desktop.Windows))
                    if (!ReferenceEquals(w, desktop.MainWindow)) { try { w.Close(); } catch (Exception) { } }
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }

    // ───────────────────────── 1) 事件 → 就地替换:链条本身 ─────────────────────────

    /// <summary>
    /// 坐实"泄漏的 MainWindow 实例会把假包字节写到 `UpdateTarget()` 解析出来的路径上",并证明
    /// 测试进程里这条写**只可能落在隔离根内**。
    ///
    /// 为什么不用"跑两个类、看谁先"的写法:那是靠 xUnit 的类顺序碰运气(本机实测同一提交里
    /// `NavigateTests → SelfUpdateFeedbackTests` 会写出界、反过来则什么都不发生)。
    /// 这里由用例自己造出那个泄漏的 MainWindow,顺序不再参与。
    /// </summary>
    [Fact]
    public void UpdateStaged_ReachingALeakedShellWindow_ReplacesOnlyInsideTheIsolationRoot()
    {
        TestApp.RunAsync(async () =>
        {
            // (a) 事故的前提:产品 `UpdateTarget()` 的兜底 = 测试宿主自己,它不落在隔离根内。
            //     Linux runner 上那就是 `/usr/share/dotnet/dotnet`(CI 日志里被写坏的那个文件)。
            string host = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            Assert.False(TestWriteGuard.IsInsideIsolation(host),
                "测试宿主不该落在隔离根内;这条断言的前提变了,先看 TestWriteGuard 的说明。host=" + host);

            // (b) 守卫对"宿主"这个目标的判断:越界 ⇒ 当场抛(这就是修复前那个写出去的现场)。
            var threw = Assert.Throws<OutsideIsolationWriteException>(
                () => TestWriteGuard.AssertInsideIsolation(host, "更新替换目标(UpdateTarget() 的兜底值)"));
            Assert.Contains(host, threw.Message);
            Assert.Contains(TestWriteGuard.IsolationRoot, threw.Message);

            // (c) 测试进程里 `UpdateTarget()` 的答案必须是隔离根内的沙箱(模块初始化时装好)。
            Assert.Equal(TestWriteGuard.UpdateTargetSandbox, Updater.UpdateTarget());
            TestWriteGuard.AssertInsideIsolation(Updater.UpdateTarget(), "测试进程的更新替换目标");

            // (d) 造一个"别的用例留下的" MainWindow:它是 `update_staged` 的唯一订阅者
            //     (MainWindow.axaml.cs:46-54),并且它 Post 出去的反应会在后续 pump 里落地。
            Window? previousOwner = Dlg.Owner;
            Dlg.Owner = null;   // 无头会话里 Dlg.Resolve() 因而返回 null ⇒ 不弹对话框、直接走 apply_update
            try
            {
                _ = new MainWindow();

                string pkg = Path.Combine(Utils.ROOT, "cache", "TipCL-ciflake-regression.bin");
                Directory.CreateDirectory(Path.GetDirectoryName(pkg)!);
                File.WriteAllBytes(pkg, Encoding.UTF8.GetBytes(ProbePackage));
                TestWriteGuard.AssertInsideIsolation(pkg, "用例自己造的更新包");

                string before = File.ReadAllText(TestWriteGuard.UpdateTargetSandbox);
                Assert.NotEqual(ProbePackage, before);

                InProcessApi.Instance.RaiseEvent(new ApiEvent { Event = "update_staged", Text = pkg, Message = "99.0.9" });

                bool written = await PumpAsync(() => File.ReadAllText(TestWriteGuard.UpdateTargetSandbox) == ProbePackage, 15000);
                if (!written)
                {
                    // 另一条落地路径:对话框真开出来了,等"有人把泄漏的对话框关掉"
                    await CloseLeakedDialogsAsync();
                    written = await PumpAsync(() => File.ReadAllText(TestWriteGuard.UpdateTargetSandbox) == ProbePackage, 15000);
                }

                Assert.True(written,
                    "泄漏的 MainWindow 收到 update_staged 之后没有走到就地替换 —— 这条链的机制变了,请重新核对 " +
                    "MainWindow.axaml.cs:46-54/244-251 与 Api.cs:529-533");
                // 真正要钉住的一句:这次替换落在隔离根内的沙箱里,而不是测试宿主 / runner 的文件上。
                Assert.True(TestWriteGuard.IsInsideIsolation(Updater.UpdateTarget()),
                    "替换目标跑到隔离根外面去了:" + Updater.UpdateTarget());
                Assert.Equal(host, Process.GetCurrentProcess().MainModule?.FileName);
            }
            finally
            {
                Dlg.Owner = previousOwner;
            }
        });
    }

    // ───────────────────────── 2) 写盘守卫本身 ─────────────────────────

    [Fact]
    public void WriteGuard_AcceptsTheIsolationRootItselfAndAnythingInsideIt()
    {
        Assert.True(TestWriteGuard.IsInsideIsolation(TestWriteGuard.IsolationRoot));
        Assert.True(TestWriteGuard.IsInsideIsolation(TestWriteGuard.UpdateTargetSandbox));
        Assert.True(TestWriteGuard.IsInsideIsolation(Path.Combine(Utils.ROOT, "cache", "TipCL-x.bin")));
        TestWriteGuard.AssertInsideIsolation(TestWriteGuard.UpdateTargetSandbox, "沙箱");   // 不抛
    }

    [Fact]
    public void WriteGuard_RejectsAnythingOutside_AndNamesPathAndRoot()
    {
        Assert.False(TestWriteGuard.IsInsideIsolation(""));
        Assert.False(TestWriteGuard.IsInsideIsolation(Path.Combine(Utils.ROOT, "..", "escape.txt")));
        Assert.False(TestWriteGuard.IsInsideIsolation(Path.Combine(Path.GetTempPath(), "outside.txt")));

        // 模拟"runner 上的宿主文件":临时目录里一个 dotnet,隔离根之外
        string fakeHostDir = Path.Combine(Path.GetTempPath(), "fake-runner-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(fakeHostDir);
        string fakeHost = Path.Combine(fakeHostDir, "dotnet");
        File.WriteAllText(fakeHost, "REAL-DOTNET-HOST-BYTES");
        try
        {
            var ex = Assert.Throws<OutsideIsolationWriteException>(
                () => TestWriteGuard.AssertInsideIsolation(fakeHost, "更新替换目标"));
            Assert.Contains(fakeHost, ex.Message);
            Assert.Contains(TestWriteGuard.IsolationRoot, ex.Message);
            Assert.Equal("REAL-DOTNET-HOST-BYTES", File.ReadAllText(fakeHost));   // 守卫只判路径,自己不写
        }
        finally
        {
            try { Directory.Delete(fakeHostDir, true); } catch (Exception) { }
        }
    }

    /// <summary>
    /// 产品侧那条"就地替换"在测试进程里的落点:抄一份包到沙箱旁边再原子改名 —— 全流程都在隔离根内,
    /// 且沙箱内容真的被换成了包(顺便把"替换之后不留 .new"也钉一下)。
    /// </summary>
    [Fact]
    public void ApplyStaged_WithTheProcessSandbox_WritesInsideTheIsolationRootOnly()
    {
        string pkg = Path.Combine(Utils.ROOT, "cache", "TipCL-ciflake-apply.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(pkg)!);
        File.WriteAllBytes(pkg, Encoding.UTF8.GetBytes("R31-CIFLAKE-APPLIED"));
        try
        {
            PyDict res = Updater.ApplyStaged(pkg, restart: false);

            Assert.True(Py.Truthy(res["ok"]), "替换没成功:" + Py.S(res["message"]));
            string target = Py.S(res["target"]);
            TestWriteGuard.AssertInsideIsolation(target, "apply_update 的替换目标");
            Assert.Equal(TestWriteGuard.UpdateTargetSandbox, target);
            Assert.Equal("R31-CIFLAKE-APPLIED", File.ReadAllText(target));
            Assert.False(File.Exists(target + ".new"), "临时文件没清干净");
        }
        finally
        {
            TestWriteGuard.RestoreProcessSandbox();
        }
    }
}
