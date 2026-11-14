using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Avalonia;
using TipCL;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// M9:把 task-20 的**影子树探针**(<c>_tmp/tasks/mac-widgets/probe/M8StartupGateProbeTests.cs</c>)收编成常驻回归用例。
///
/// 被守的东西:mac 把 Windows 启动链尾巴上的两次「首启询问」(<see cref="FirstRunWizard.MaybeShowAsync"/> /
/// <see cref="FeedbackConsent.MaybeAskAsync"/>)接在 <c>MainWindow.ReloadCurrentNow()</c> 末尾,并加了
/// <see cref="MainWindow.StartupPromptsAllowed"/> 这道闸(非桌面生命周期 / 冒烟一律不弹)。**闸一坏,两次询问
/// 就会在每个测试与 `--smoke` 里真的弹卡** —— 而那条路径原先只有"受影响用例数 = 0"这种推理,没有断言。
///
/// 三条判据(闭环,每条都能证伪):
/// <list type="number">
/// <item><b>闸门实测</b>:本会话 <c>Application.ApplicationLifetime == null</c> ∧ <c>SmokeState.Active == false</c>
///   ⇒ <see cref="MainWindow.StartupPromptsAllowed"/> 必须为 false(直接调那个函数,不是"看代码认为");</item>
/// <item><b>关闸时生产路径一次都不许问</b>(**能证伪的那条**):反射调真·生产方法
///   <c>MainWindow.ReloadCurrentNow()</c> —— M8 两处接线的落点 —— 然后泵 2.5 秒,断言假后端
///   **一次都没收到**带 <c>first_run</c> 的 <c>get_setting</c>,并且**一张卡都没出现**。
///   这条是**调用级**判据:哪怕后端回 false 导致卡不出现,只要方法被调了它就会红。
///   (反证:把 <c>MainWindow.cs:418</c> 的 <c>if (StartupPromptsAllowed())</c> 改成无条件调用
///   <c>AskStartupQuestions()</c>,本条立刻变红 —— 我在隔离树里复跑过这组 A/B,见报告。)</item>
/// <item><b>对照(证明探针没瞎)</b>:同一套装置下**直接**调那两个入口 ⇒ <c>first_run</c> 真被查、向导卡与
///   同意卡真出现。既然直接调看得见,第 2 条的"没有"就只能是闸门挡的。</item>
/// </list>
///
/// 收编时按正式套件的约定改了三处(影子树里不需要、正式套件里必须):
/// <list type="number">
/// <item>归到 <see cref="C2dFixtures.Collection"/>:本类改的是 <c>BridgeHost.ApiFactory</c> /
///   <c>OnCoreStarted</c> 这类**进程级静态注入缝**,xunit 默认按类并行 ⇒ 不归一个 collection 会与 C2d 四个类互相换掉对方的工厂(夹具注释里记过实测);</item>
/// <item>每条用例进 <see cref="TestHome"/>:数据目录隔离(红线:绝不碰用户真实 <c>%LOCALAPPDATA%\TipCL</c>;反馈同意会去读 <c>config.json</c>);</item>
/// <item>第 2 条加了一条**反空转**断言:<c>ReloadCurrentNow</c> 的第一句是 <c>_host.Current</c>,没有当前页就整段返回 ——
///   那样"没问"会是空转假绿。所以先挂一个探针页并断言这次调用**真的**把当前页补载/展示了一次(计数增加),
///   证明执行确实走到了闸门那一句。</item>
/// </list>
///
/// <b>不写成自动化用例、留真机手验</b>(来自 task-20 的风控提示):反馈同意那一步**没有 <c>first_run</c> 闸门**
/// (与 Windows 同行为),<c>config.json</c> 不可读时会**每次启动都问** ⇒ 需要在真 macOS 上验"配置目录只读"这一情形。
/// </summary>
[Collection(C2dFixtures.Collection)]
public class M9StartupGateTests
{
    /// <summary>泵 <paramref name="ms"/> 毫秒的 Dispatcher 时间(每 20ms 推一次队列;与既有夹具同口径)。</summary>
    private static void PumpFor(int ms)
    {
        int rounds = Math.Max(1, ms / 20);
        for (int i = 0; i < rounds; i++)
        {
            C3bFiles.Flush();
            Thread.Sleep(20);
        }
    }

    /// <summary>调 <c>MainWindow</c> 的私有方法 = 生产代码里含 M8 接线的那一段(它是 private,只能反射)。</summary>
    private static void InvokeReloadCurrentNow(MainWindow window)
    {
        MethodInfo? method = typeof(MainWindow).GetMethod(
            "ReloadCurrentNow",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(window, null);
    }

    /// <summary>假后端收到过的、参数原文里含 <paramref name="fragment"/> 的那些调用(逐次查原文)。</summary>
    private static List<string> ParamsContaining(C3cFakeApi api, string method, string fragment)
    {
        var hits = new List<string>();
        for (int i = 0; i < api.Count(method); i++)
        {
            string parameters = api.ParamsOf(method, i);
            if (parameters.Contains(fragment, StringComparison.Ordinal))
            {
                hits.Add(parameters);
            }
        }

        return hits;
    }

    /// <summary>判据 1:本会话里那道闸必须**是关的**(两条判据都直接测,不看代码)。</summary>
    [Fact]
    public void Startup_gate_is_closed_in_this_session()
    {
        AvaloniaSession.Run(() =>
        {
            // 闸 2 的依据:会话走 AppBuilder.SetupWithoutStarting() ⇒ ApplicationLifetime 恒 null(D-69)。
            Assert.Null(Application.Current!.ApplicationLifetime);
            // 闸 1:测试里没有打开离屏模式。
            Assert.False(Smoke.SmokeState.Active);
            // 合起来 ⇒ 闸门关闭。
            Assert.False(MainWindow.StartupPromptsAllowed());
        });
    }

    /// <summary>
    /// 判据 2(**能证伪的那条**):闸门关着时,真·生产方法 <c>ReloadCurrentNow</c> 走完后
    /// 假后端**一次**带 <c>first_run</c> 的 <c>get_setting</c> 都没有收到,而且一张卡都没出现。
    /// </summary>
    [Fact]
    public void Gated_production_path_asks_nothing()
    {
        AvaloniaSession.Run(() =>
        {
            using TestHome home = new TestHome();
            C2dFixtures.Install();
            try
            {
                using C4LayerHost harness = C4LayerHost.Show();
                ProbePage probe = new ProbePage();
                using RegistryScope registry = new RegistryScope().Register("m9-startup-gate", () => probe);
                using ShellFixture shell = ShellFixture.Create();
                MainWindow window = shell.Window;

                // ReloadCurrentNow 第一句是 _host.Current:没有当前页就整段返回 ⇒ 先挂一页
                window.Navigate("m9-startup-gate");
                Assert.NotNull(window.CurrentPage);
                int shownBefore = probe.Shown;

                using C3cFakeApi api = new C3cFakeApi();

                // 前置(与判据 1 同口径,放在同一条用例里让失败原因自明)
                Assert.Null(Application.Current!.ApplicationLifetime);
                Assert.False(Smoke.SmokeState.Active);
                Assert.False(MainWindow.StartupPromptsAllowed());

                InvokeReloadCurrentNow(window);
                PumpFor(2500);

                // 反空转:这次调用必须真的把当前页 OnShown 了一次(证明执行走到了闸门那一句)
                Assert.True(
                    probe.Shown > shownBefore,
                    "ReloadCurrentNow 没有跑到闸门那一句(当前页没被 OnShown)⇒ 下面的「没问」是空转假绿");

                // 调用级判据:带 first_run 的 get_setting 一次都不许有
                List<string> asked = ParamsContaining(api, "get_setting", "first_run");
                Assert.True(
                    asked.Count == 0,
                    "闸门关着时不该有人查 first_run,实际 " + asked.Count + " 次;假后端收到的方法流水=" + string.Join(",", api.Methods()));
                // 可见副作用:一张卡都没出现
                Assert.Equal(0, C4Files.LayerCount());
                window.StopBackend();
            }
            finally
            {
                C2dFixtures.Restore();
            }
        });
    }

    /// <summary>
    /// 判据 3(**对照**,证明上面那条"没有"不是探针瞎):同一套装置下直接调两个入口 ——
    /// 向导必须真的查 <c>first_run</c> 并弹出向导卡;反馈同意必须真的弹出一张对话框卡。
    /// </summary>
    [Fact]
    public void Control_sees_both_prompts_when_they_actually_run()
    {
        // 对照 A:首启向导 —— 直接调入口 ⇒ 真的查 first_run,并且向导卡真的出现。
        AvaloniaSession.Run(() =>
        {
            using TestHome home = new TestHome();
            using C4LayerHost harness = C4LayerHost.Show();
            using C3cFakeApi api = new C3cFakeApi().Route("get_setting", "true").Route("get_settings", "{}");
            PageBase.Run(FirstRunWizard.MaybeShowAsync, I18n.L("向导没能打开"));
            C4Files.PanelCard(I18n.L("欢迎使用 TipCL"));
            Assert.True(
                ParamsContaining(api, "get_setting", "first_run").Count > 0,
                "对照失败:直接调 MaybeShowAsync 都没查 first_run;方法流水=" + string.Join(",", api.Methods()));
        });

        // 对照 B:反馈同意(它没有 first_run 闸门 —— 见类注释的真机手验项)⇒ 直接调就得出卡。
        AvaloniaSession.Run(() =>
        {
            using TestHome home = new TestHome();
            using C4LayerHost harness = C4LayerHost.Show();
            using C3cFakeApi api = new C3cFakeApi().Route("get_settings", "{}");
            PageBase.Run(FeedbackConsent.MaybeAskAsync, I18n.L("反馈同意没能询问"));
            C3bFiles.WaitDialog();
            Assert.True(C4Files.LayerCount() > 0);
        });
    }
}
