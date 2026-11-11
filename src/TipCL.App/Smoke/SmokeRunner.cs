using System;
using System.IO;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Styling;

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

            ThemeVariant variant = options.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
            if (Application.Current is { } application)
            {
                application.RequestedThemeVariant = variant;
            }

            var window = new MainWindow
            {
                RequestedThemeVariant = variant,
                PageId = options.Page,
            };

            window.Show();
            window.ApplyThemeVisuals();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

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
                " bytes=" + info.Length);

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[smoke] 渲染失败: " + ex);
            return 1;
        }
    }
}
