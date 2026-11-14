using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using UiFacade = TipCL.App.Ui.Ui;

namespace TipCL.App.Common;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// 轻提示门面（照 Windows 版 PageBase.Toast 的调用形态）：右上角浮层，2.6s 自动消失。
/// 归属 Common，不动 Shell；宿主窗口由 Application 当前 MainWindow 解析。
/// </summary>
public static class ToastHost
{
    /// <summary>宿主（外壳可注入自定义 TopLevel；默认取 MainWindow）。</summary>
    public static TopLevel? Owner { get; set; }

    private static TopLevel? Resolve()
    {
        if (Owner != null) return Owner;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    public static void Show(string title, string body = "", ToastKind kind = ToastKind.Info)
    {
        var host = Resolve();
        if (host == null) return;
        Dispatcher.UIThread.Post(() => ShowCore(host, title, body, kind));
    }

    private static IBrush KindBrush(ToastKind kind)
        => kind switch
        {
            ToastKind.Success => UiFacade.Brush("B.Ok") ?? Brushes.Green,
            ToastKind.Warning => UiFacade.Brush("B.Warn") ?? Brushes.Orange,
            ToastKind.Error => UiFacade.Brush("B.Danger") ?? Brushes.Red,
            _ => UiFacade.Brush("B.Accent") ?? Brushes.DodgerBlue,
        };

    private static void ShowCore(TopLevel host, string title, string body, ToastKind kind)
    {
        var layer = host is Visual root ? OverlayLayer.GetOverlayLayer(root) : null;
        if (layer == null) return;

        var bar = new Border
        {
            Background = KindBrush(kind),
            CornerRadius = new CornerRadius(6),
            Width = 4,
            Margin = new Thickness(0, 0, 6, 0),
        };
        var textCol = new StackPanel { Spacing = 2 };
        textCol.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.Bold,
            FontSize = 13,
            [!TextBlock.ForegroundProperty] = Res.Bind("B.OnAccentStrong"),
        });
        if (!string.IsNullOrEmpty(body))
        {
            textCol.Children.Add(new TextBlock
            {
                Text = body,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 300,
                Opacity = 0.85,
                [!TextBlock.ForegroundProperty] = Res.Bind("B.OnAccentStrong"),
            });
        }
        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(0),
            ClipToBounds = true,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { bar, new Border { Padding = new Thickness(4, 10, 14, 10), Child = textCol } },
            },
        };
        card[!Border.BackgroundProperty] = Res.Bind("B.AccentSoft");
        card[!Border.BorderBrushProperty] = Res.Bind("B.Line");

        var wrapper = new Border
        {
            Child = card,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 52, 16, 0),
            Opacity = 0,
        };
        layer.Children.Add(wrapper);
        Motion.FadeIn(wrapper, ms: 180, fromY: -8);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Motion.FadeOut(wrapper, ms: 140, () => layer.Children.Remove(wrapper));
        };
        timer.Start();
    }
}
