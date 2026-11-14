using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using TipCL.Models;
using TipCL.Pages;
using TipCL.Services;

namespace TipCL.Smoke;

/// <summary>
/// 离屏截图:Headless 平台 + Skia 真实渲染(<c>UseHeadlessDrawing = false</c>),
/// 把主窗口渲染成 PNG 后退出。任何异常或 PNG 没写出来都返回非 0 并打 stderr。
/// </summary>
internal static class SmokeRunner
{
    internal static int Run(SmokeOptions options)
    {
        try
        {
            string outputPath = Path.GetFullPath(options.Out);
            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();

            // C2b 接线:离屏模式一开,Dlg 的三个挂点(SmokeActive / SmokeAutoAnswer /
            // SmokeAutoCancelSystemDialog)同时接上,收摊时原样还回去 —— 离屏绝不会卡在对话框上。
            using IDisposable smoke = SmokeState.Activate();

            ThemeVariant variant = options.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
            if (Application.Current is { } application)
            {
                application.RequestedThemeVariant = variant;
            }

            // C2b:外壳按 UiPrefs 建标题栏/侧栏,这里注入一份默认偏好 —— 离屏像素不许随
            // 用户数据目录里的 wpf-ui.json 变(生产路径才读盘,见 MainWindow.LoadUiPrefs)。
            var window = new MainWindow(new UiPrefs())
            {
                RequestedThemeVariant = variant,
                PageId = options.Page,
            };

            window.Show();

            // --page:页面还没移植(C3)时 Navigate 静默不动,行为与 A1 起的"只解析"一致;
            // 页面注册进来之后,这里就会真的切过去。
            if (options.Page.Length > 0)
            {
                window.Navigate(options.Page, instant: true);
            }

            window.ApplyThemeVisuals();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            // M7:AI 页的帧证据。离屏不连后端 ⇒ BridgeReady=false ⇒ MainWindow.Navigate 传的 runHooks
            // 是 false(D-92),页面的 EnsureLoadedAsync/OnShown 都不会跑,所以不带 --ai-seed 时
            // AI 页只有"外壳切过去 + 构造函数"这一层;带上它,就用页面自己的四个自测缝
            // (Windows 侧的驱动器是 Pages\Ai\AiSelfTest.cs:433 行)灌一份**确定性**数据再截屏。
            if (options.Page == AiPageRegistration.PageId && window.GetPage(AiPageRegistration.PageId) is AiPage ai)
            {
                if (options.AiSeed)
                {
                    SeedAiPage(ai);
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                }

                ReportAiPage(ai, options.AiSeed);
            }

            using var frame = window.CaptureRenderedFrame();
            if (frame is null)
            {
                Console.Error.WriteLine("[smoke] 渲染失败:CaptureRenderedFrame 返回 null(未拿到渲染帧)");
                return 1;
            }

            frame.Save(outputPath);

            var info = new FileInfo(outputPath);
            if (!info.Exists || info.Length <= 0)
            {
                Console.Error.WriteLine("[smoke] 渲染失败:PNG 未写出或为空 " + outputPath);
                return 1;
            }

            Console.Out.WriteLine(
                "[smoke] ok page=" + (options.Page.Length == 0 ? "(none)" : options.Page) +
                " theme=" + options.Theme +
                " out=" + outputPath +
                " bytes=" + info.Length +
                " issues=" + SmokeState.Issues.Count +
                " pending=" + SmokeState.PendingOperations);

            // M7:把离屏期间记下的问题**打出来**(以前只有"渲染失败"才说话)—— 页面自己吞掉的异常
            // (PageBase.LoadOnceAsync 的 catch、插件收到的坏请求等)都会以 SmokeState.Note 落进 issues,
            // 不打印的话"帧出来了"很容易被误读成"这一页没问题"。仍是 0 退出码:issues 是**证据**,
            // 不是壳子该判失败的条件(壳子只对"没渲染出帧"负责)。
            foreach (string issue in SmokeState.Issues)
            {
                Console.Out.WriteLine("[smoke] issue: " + issue);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[smoke] 渲染失败: " + ex);
            return 1;
        }
    }

    /// <summary>
    /// M7:给 <c>--page ai --ai-seed</c> 灌一份**确定性**的对话(全部走 <see cref="AiPage"/> 已有的自测缝,
    /// 一个字节都不碰后端与真剪贴板):一条压缩分隔线 + 两段历史 + 一条"用户刚说" + 忙态。
    ///
    /// 为什么数据写在这里而不是页面里:Windows 侧的等价物是 <c>Pages\Ai\AiSelfTest.cs</c>(433 行的
    /// 离屏驱动器,由 <c>App.xaml.cs</c> 的自检通道调),它的职责本来就是"喂数据 + 截图 + 断言";
    /// mac 侧这层归离屏宿主,所以留在 <c>Smoke\</c> 里,页面那 4 个缝保持 Windows 的形状不动。
    /// </summary>
    private static void SeedAiPage(AiPage ai)
    {
        AiStoreDto store = new AiStoreDto
        {
            ActiveId = "smoke"
        };
        AiChatDto chat = new AiChatDto
        {
            Id = "smoke",
            Title = I18n.L("生存整合包崩溃排查")
        };
        chat.Messages.Add(new AiMsgDto
        {
            Id = "compact_1",
            Role = "user",
            Content = I18n.L("更早的对话已压缩成摘要")
        });
        chat.Messages.Add(new AiMsgDto
        {
            Id = "m1",
            Role = "user",
            Content = I18n.L("我的「生存整合包 1.20.1」一进世界就崩溃,帮我看看怎么回事")
        });
        chat.Messages.Add(new AiMsgDto
        {
            Id = "m2",
            Role = "assistant",
            Content = I18n.L("读到最后一次崩溃日志里是 `NoSuchMethodError`,多半是模组和游戏版本对不上。要我先扫一遍冲突吗?")
        });
        store.Chats.Add(chat);

        ai.SelfTestLoad(store);
        ai.SelfTestUser(I18n.L("好,帮我换掉,顺便把渲染距离调到 12"));
        ai.SelfTestBusy(on: true);
        ai.SelfTestScrollEnd();
    }

    /// <summary>
    /// M7:把 AI 页此刻的子件数打出来当**证据** —— 帧里有几个气泡不该靠肉眼数,而且"消息区空着"
    /// 到底是"没搬"还是"离屏没跑 LoadAsync"必须能一眼分开(见 <see cref="SeedAiPage"/> 的注释:
    /// 不加 <c>--ai-seed</c> 时页面只走到构造函数,消息区天然是空的)。
    /// </summary>
    private static void ReportAiPage(AiPage ai, bool seeded)
    {
        Console.Out.WriteLine(
            "[smoke] ai: seeded=" + (seeded ? "yes" : "no") +
            " chats=" + ai.SelfTestChatList.Children.Count +
            " messages=" + ai.SelfTestMessages.Children.Count +
            " dock=" + ai.SelfTestDock.Children.Count +
            " placeholder=" + (ai.SelfTestPlaceholder.IsVisible ? "visible" : "hidden") +
            " sendEnabled=" + ai.SelfTestSend.IsEnabled +
            " stopEnabled=" + ai.SelfTestStop.IsEnabled);
    }
}
