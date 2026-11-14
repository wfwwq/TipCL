using System;
using System.Linq;
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

    /// <summary>
    /// 提示框(照 Windows 版 Dlg.Alert):只有**一个「知道了」按钮**,内部结果**恒为 true** ——
    /// 点按钮、Esc、直接关窗都算「知道了」(参考版 Alert 的 dismiss 回调同样是 TrySetResult(true))。
    /// <para>此前这里是 <c>Ask(..., cancel: null)</c>:会多渲染一个「取消」按钮,关掉还会被当成取消。
    /// 6 个调用点(CatalogPage / VersionManagePage / SavesDialog / ModsDialog / FirstRunWizard /
    /// FeedbackView)都是 <c>await Dlg.Alert(...)</c>,且返回值类型本就是不暴露值的 <see cref="Task"/>,
    /// 所以单按钮 + 恒 true 是安全的。</para>
    /// </summary>
    public static async Task Alert(string title, string body, Window? owner = null)
        => await AskCore(title, new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap }, "知道了", null, false, 460, owner, dismissIsOk: true);

    /// <summary>通用问答框：自定义内容体 + 确定/取消。</summary>
    public static Task<bool> Ask(string title, Control body, string? ok = null, string? cancel = null, bool danger = false, double width = 460, Window? owner = null)
        => AskCore(title, body, string.IsNullOrEmpty(ok) ? "确定" : ok, string.IsNullOrEmpty(cancel) ? "取消" : cancel, danger, width, owner);

    /// <summary>
    /// Ask / Alert 的实现体。<paramref name="cancelLabel"/> 为 null 即**不建取消按钮**(Alert 的单按钮形态);
    /// <paramref name="dismissIsOk"/> 决定「直接关窗 / Esc」算确定还是取消(Alert 恒 true)。
    /// 拆到私有层是为了不动 <see cref="Ask"/> 的公开签名。
    /// </summary>
    private static async Task<bool> AskCore(string title, Control body, string okLabel, string? cancelLabel, bool danger, double width, Window? owner, bool dismissIsOk = false)
    {
        var host = Resolve(owner);
        if (host == null) return false;
        var okBtn = new Button { Content = okLabel };
        okBtn[!Button.BackgroundProperty] = Res.Bind(danger ? "B.Danger" : "B.AccentStrong");
        okBtn[!Button.ForegroundProperty] = Res.Bind("B.OnAccentStrong");
        var cancelBtn = cancelLabel == null ? null : new Button { Content = cancelLabel };
        var result = new TaskCompletionSource<bool>();
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        if (cancelBtn != null)
            row.Children.Add(cancelBtn);
        row.Children.Add(okBtn);

        var win = BuildDialog(title, body, width, row);
        // 键盘习惯照 Windows 版:Enter = 确定;有取消按钮时 Esc = 取消,单按钮时 Esc 也算「知道了」
        okBtn.IsDefault = true;
        if (cancelBtn != null) cancelBtn.IsCancel = true;
        else okBtn.IsCancel = true;
        okBtn.Click += (_, _) => Finish(result, true, win);
        if (cancelBtn != null)
            cancelBtn.Click += (_, _) => Finish(result, false, win);
        win.Closed += (_, _) => result.TrySetResult(dismissIsOk);
        AnyOpen = true;
        await win.ShowDialog(host);
        AnyOpen = false;
        return await result.Task;
    }

    /// <summary>
    /// 多选一对话框(照 Windows 版 Dlg.Choose):buttons 依次排列,返回被点按钮的下标;
    /// 直接关窗返回 -1。用于「取消 / 安装最新 / 安装所选」这类三分支确认。
    /// </summary>
    public static async Task<int> Choose(string title, Control body, string[] buttons, double width = 560, Window? owner = null)
    {
        var host = Resolve(owner);
        if (host == null) return -1;
        var result = new TaskCompletionSource<int>();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var made = new Button[buttons.Length];
        for (int i = 0; i < buttons.Length; i++)
        {
            bool primary = i == buttons.Length - 1;
            var b = new Button { Content = buttons[i] };
            if (primary)
            {
                b[!Button.BackgroundProperty] = Res.Bind("B.AccentStrong");
                b[!Button.ForegroundProperty] = Res.Bind("B.OnAccentStrong");
            }
            made[i] = b;
            row.Children.Add(b);
        }
        var win = BuildDialog(title, body, width, row);
        // 点击处理要等 win 存在才挂(要在同一个处理器里关窗,见 Finish)
        for (int i = 0; i < made.Length; i++)
        {
            int idx = i;
            made[i].Click += (_, _) => Finish(result, idx, win);
        }
        if (made.Length > 0)
            made[^1].IsDefault = true;   // 最后一个按钮是主按钮(照 Windows 版 BtnKind.Primary):Enter 走它
        win.Closed += (_, _) => result.TrySetResult(-1);
        AnyOpen = true;
        await win.ShowDialog(host);
        AnyOpen = false;
        return await result.Task;
    }

    /// <summary>
    /// 交互面板(照 Windows 版 Dlg.Panel):只有「关闭」一个按钮,体内可放列表 / 操作行。
    /// 关窗后 Task 才完成,调用方据此刷新。
    /// </summary>
    public static async Task Panel(string title, Control body, double width = 680, Window? owner = null)
    {
        var host = Resolve(owner);
        if (host == null) return;
        body.MaxWidth = Math.Max(240, width - 40);
        var close = new Button { Content = "关闭" };
        close[!Button.BackgroundProperty] = Res.Bind("B.AccentStrong");
        close[!Button.ForegroundProperty] = Res.Bind("B.OnAccentStrong");
        close.IsCancel = true;   // 只补 Esc:面板体里常有输入框,Enter 不该等于「关闭」,故不设 IsDefault
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { close },
        };
        var win = BuildDialog(title, body, width, footer);
        var done = new TaskCompletionSource<bool>();
        close.Click += (_, _) => win.Close();
        win.Closed += (_, _) => done.TrySetResult(true);
        AnyOpen = true;
        await win.ShowDialog(host);
        AnyOpen = false;
        await done.Task;
    }

    /// <summary>
    /// 非模态交互面板(照 Windows 版 Dlg.Panel 的 modeless 语义):调用方拿到句柄后可以
    /// 继续跑自己的流程,需要时用 handle.Close() 关掉,或 await handle.Closed。
    /// </summary>
    public static PanelHandle OpenPanel(string title, Control body, double width = 680, Window? owner = null)
    {
        var host = Resolve(owner);
        body.MaxWidth = Math.Max(240, width - 40);
        var close = new Button { Content = "关闭" };
        close[!Button.BackgroundProperty] = Res.Bind("B.AccentStrong");
        close[!Button.ForegroundProperty] = Res.Bind("B.OnAccentStrong");
        close.IsCancel = true;   // 只补 Esc:面板体里常有输入框,Enter 不该等于「关闭」,故不设 IsDefault
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { close },
        };
        var win = BuildDialog(title, body, width, footer);
        var closed = new TaskCompletionSource<bool>();
        win.Closed += (_, _) => closed.TrySetResult(true);
        close.Click += (_, _) => win.Close();
        if (host != null)
        {
            AnyOpen = true;
            win.Closed += (_, _) => AnyOpen = false;
            win.Show(host);
        }
        return new PanelHandle(win, closed.Task);
    }

    /// <summary>OpenPanel 返回的句柄。</summary>
    public sealed class PanelHandle
    {
        internal PanelHandle(Window window, Task closed)
        {
            Window = window;
            Closed = closed;
        }

        public Window Window { get; }

        public Task Closed { get; }

        public void Close()
        {
            try { Window.Close(); }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// 单行输入框。取消(或直接关窗)返回 null,确定返回输入内容(可能是空串)。
    /// <para>对照 Windows 版 <c>Dlg.Prompt</c>:那边 footer 是 <c>Ui.H(8, button2, button)</c> ——
    /// **取消 + 确定两个按钮**(取消 → null,确定 → 输入内容)。本移植版此前只放了确定按钮,
    /// 用户没有「反悔」出口(Esc 也无效),这里按 Windows 版补齐。
    /// (另一处未对齐的细节:Windows 版确定时会对文本 <c>Trim()</c>,本版仍原样返回 —— 改它会波及
    /// 所有调用方的空串/空格判定,不在本次修复范围内。)</para>
    /// </summary>
    public static async Task<string?> Prompt(string title, string label, string initial = "", string? placeholder = null, Window? owner = null)
    {
        var host = Resolve(owner);
        if (host == null) return null;
        var box = new TextBox { Text = initial, Watermark = placeholder ?? "", MinWidth = 300 };
        var okBtn = new Button { Content = "确定" };
        okBtn[!Button.BackgroundProperty] = Res.Bind("B.AccentStrong");
        okBtn[!Button.ForegroundProperty] = Res.Bind("B.OnAccentStrong");
        var cancelBtn = new Button { Content = "取消" };
        var result = new TaskCompletionSource<string?>();

        var win = BuildDialog(title, WrapField(label, box), 460, new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancelBtn, okBtn },
        });
        okBtn.IsDefault = true;
        cancelBtn.IsCancel = true;
        okBtn.Click += (_, _) => Finish(result, box.Text, win);
        cancelBtn.Click += (_, _) => Finish<string?>(result, null, win);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) Finish(result, box.Text, win);
        };
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

    /// <summary>
    /// 测试/无宿主注入点:非 null 时 <see cref="PickFiles"/> 不弹系统文件对话框,直接用注入的实现
    /// (与 `PageLayoutIO.PickFileDialog` 同一做法,见 Common/Dlg.cs 的对位说明)。生产代码从不设置它。
    /// </summary>
    public static Func<string, string[], string[]>? PickFilesOverride { get; set; }

    /// <summary>
    /// 多选文件(对齐 Windows 版 Dlg.PickFiles:`Multiselect = true`)。
    /// <para>取消时返回**空数组**(不是 null),调用方据此「什么都不做」;</para>
    /// <para>文件类型过滤器与 <see cref="PickFile"/> 同形(单个「文件」类型 + Patterns)。</para>
    /// </summary>
    public static async Task<string[]> PickFiles(string title = "选择文件", params string[] patterns)
    {
        if (PickFilesOverride != null) return PickFilesOverride(title, patterns) ?? Array.Empty<string>();
        var host = Resolve(null);
        if (host == null) return Array.Empty<string>();
        var files = await host.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("文件")
                {
                    Patterns = patterns.Length > 0 ? patterns : new[] { "*.*" },
                },
            },
        });
        return files.Count > 0 ? files.Select(f => f.Path.LocalPath).ToArray() : Array.Empty<string>();
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

    /// <summary>
    /// 收尾一次对话框交互:先落地结果,**再关窗**。
    /// <para>顺序不能反:每个对话框都在 <c>win.Closed</c> 上挂了「兜底 = 取消」的 TrySetResult,
    /// 而 TaskCompletionSource 只认第一次设置 —— 先 Close() 会让兜底抢答(点「确定」却拿到 false);
    /// 先设结果,兜底那次自然成了空操作。</para>
    /// <para>也正因为 TCS 只认首次,「已经收尾过」就用 TrySetResult 的返回值判断:
    /// 键盘上 Enter 可能同时触发输入框 KeyDown 与 IsDefault 按钮,重复调用不会重复关窗。</para>
    /// <para>为什么必须显式关窗:Windows 参考版用 WinForms 图层,设 DialogResult / 图层 Close 会顺带
    /// 关掉图层;移植成 Avalonia 独立窗口后这一步没人代劳。漏掉它的症状就是——按钮点了窗口不动、
    /// <c>await win.ShowDialog(host)</c> 永不返回、调用方以为用户「没作答」。</para>
    /// </summary>
    private static void Finish<T>(TaskCompletionSource<T> tcs, T value, Window win)
    {
        if (!tcs.TrySetResult(value)) return;
        win.Close();
    }

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
