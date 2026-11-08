using System;
using System.Threading.Tasks;
using Avalonia;
using UiFacade = TipCL.App.Ui.Ui;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace TipCL.App.Common;

/// <summary>
/// 对话框门面（对齐 Windows 版 Services/Dlg.cs 的常用面：Confirm/Alert/Prompt/文件选择）。
/// Avalonia 用模态子窗口实现；文件对话框走 StorageProvider。
/// 外壳可通过 Owner 注入宿主窗口，缺省取 MainWindow。
/// </summary>
public static class Dlg
{
    /// <summary>宿主（外壳可注入；默认取 MainWindow）。</summary>
    public static Window? Owner { get; set; }

    public static bool AnyOpen { get; private set; }

    private static Window? Resolve(Window? owner)
        => owner ?? Owner
           ?? (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
               ? desktop.MainWindow
               : null);

    /// <summary>确认框。danger=true 时确认钮走危险红。</summary>
    public static Task<bool> Confirm(string title, string body, string? ok = null, string? cancel = null, bool danger = false, Window? owner = null)
        => Ask(title, new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap }, ok, cancel, danger, owner: owner);

    public static Task Alert(string title, string body, Window? owner = null)
        => Ask(title, new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap }, "确定", null, false, owner: owner);

    /// <summary>通用问答框：自定义内容体 + 确定/取消。</summary>
    public static async Task<bool> Ask(string title, Control body, string? ok = null, string? cancel = null, bool danger = false, double width = 460, Window? owner = null)
    {
        var host = Resolve(owner);
        if (host == null) return false;
        var okBtn = new Button { Content = string.IsNullOrEmpty(ok) ? "确定" : ok };
        okBtn[!Button.BackgroundProperty] = Res.Bind(danger ? "B.Danger" : "B.AccentStrong");
        okBtn[!Button.ForegroundProperty] = Res.Bind("B.OnAccentStrong");
        var cancelBtn = new Button { Content = string.IsNullOrEmpty(cancel) ? "取消" : cancel };
        var result = new TaskCompletionSource<bool>();

        okBtn.Click += (_, _) => result.TrySetResult(true);
        cancelBtn.Click += (_, _) => result.TrySetResult(false);

        var win = BuildDialog(title, body, width, new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancelBtn, okBtn },
        });
        win.Closed += (_, _) => result.TrySetResult(false);
        AnyOpen = true;
        await win.ShowDialog(host);
        AnyOpen = false;
        return await result.Task;
    }

    /// <summary>单行输入框。</summary>
    public static async Task<string?> Prompt(string title, string label, string initial = "", string? placeholder = null, Window? owner = null)
    {
        var host = Resolve(owner);
        if (host == null) return null;
        var box = new TextBox { Text = initial, Watermark = placeholder ?? "", MinWidth = 300 };
        var okBtn = new Button { Content = "确定" };
        okBtn[!Button.BackgroundProperty] = Res.Bind("B.AccentStrong");
        okBtn[!Button.ForegroundProperty] = Res.Bind("B.OnAccentStrong");
        var result = new TaskCompletionSource<string?>();
        okBtn.Click += (_, _) => result.TrySetResult(box.Text);
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) result.TrySetResult(box.Text); };

        var win = BuildDialog(title, WrapField(label, box), 460, okBtn);
        win.Closed += (_, _) => result.TrySetResult(null);
        AnyOpen = true;
        await win.ShowDialog(host);
        AnyOpen = false;
        return await result.Task;
    }

    // —— 文件/文件夹（StorageProvider，对齐 Dlg.PickFile/SaveFile/PickFolder） ——

    public static async Task<string?> PickFile(string title = "选择文件", params string[] patterns)
    {
        var host = Resolve(null);
        if (host == null) return null;
        var files = await host.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("文件")
                {
                    Patterns = patterns.Length > 0 ? patterns : new[] { "*.*" },
                },
            },
        });
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    public static async Task<string?> SaveFile(string title, string suggestedName)
    {
        var host = Resolve(null);
        if (host == null) return null;
        var file = await host.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
        });
        return file?.Path.LocalPath;
    }

    public static async Task<string?> PickFolder(string title = "选择文件夹")
    {
        var host = Resolve(null);
        if (host == null) return null;
        var folders = await host.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    // ───────────────────────────── 内部 ─────────────────────────────

    private static StackPanel WrapField(string label, Control input)
        => new()
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = label, FontSize = 13 },
                input,
            },
        };

    private static Window BuildDialog(string title, Control body, double width, Control footer)
    {
        var win = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            Width = width,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.Full,
            MinWidth = width,
        };
        var inner = new StackPanel { Spacing = 14 };
        inner.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = FontWeight.Bold,
            [!TextBlock.ForegroundProperty] = Res.Bind("B.Ink"),
        });
        inner.Children.Add(body);
        inner.Children.Add(footer);
        var card = new Border
        {
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Child = inner,
        };
        card[!Border.BackgroundProperty] = Res.Bind("B.Paper");
        card[!Border.BorderBrushProperty] = Res.Bind("B.Line");
        win.Content = card;
        return win;
    }
}
