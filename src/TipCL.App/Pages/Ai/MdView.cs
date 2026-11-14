// 来源: Windows 版参考仓库 src/TipCL/Pages/Ai/MdView.cs(M4-H P5 移植)。
// Markdown 渲染控件。Windows 用 WPF RichTextBox + FlowDocument(Avalonia 没有对应物),
// Linux 侧改用等价做法:解析成块后逐个渲染成控件(段落 = TextBlock.Inlines,
// 列表 = 带缩进的 TextBlock,引用 = 左边框容器,代码块 = 深底只读框 + 复制钮,表格 = Grid)。
// 输入框是两个平台真正不一样的地方,渲染结果(字号 / 缩进 / 配色)按 Windows 的值对齐。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TipCL.App.Common;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Ai;

/// <summary>Markdown 渲染控件(照 Windows 版 MdView 的版面:字号 13.5 / 行高 22 / 末块不留底边距)。</summary>
internal sealed class MdView : UserControl
{
    internal static readonly FontFamily Mono = new("Cascadia Mono, Consolas, DejaVu Sans Mono, Noto Sans Mono CJK SC, monospace");

    /// <summary>链接前景色键:常态 / 悬停(两套 Palette 里都有;悬停换色的理由见 BuildInline 的 R21c 说明)。</summary>
    private const string LinkInk = "B.AccentText";
    private const string LinkHoverInk = "B.Accent";

    private readonly StackPanel _host = new();
    private readonly DispatcherTimer _throttle = new() { Interval = TimeSpan.FromMilliseconds(80) };

    private string _text = "";
    private string? _rendered;
    private bool _pendingLive;

    public string Markdown => _text;

    public MdView()
    {
        FontSize = 13.5;
        _host.Spacing = 0;
        Content = new ScrollViewer
        {
            Content = _host,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };
        _throttle.Tick += (_, _) =>
        {
            _throttle.Stop();
            Render(_pendingLive);
        };
    }

    /// <summary>设置 markdown 内容;live=true 时按 80ms 节流(流式输出用)。</summary>
    public void SetMarkdown(string? text, bool live = false)
    {
        _text = text ?? "";
        if (live)
        {
            _pendingLive = true;
            if (!_throttle.IsEnabled) _throttle.Start();
        }
        else
        {
            _throttle.Stop();
            Render(live: false);
        }
    }

    private void Render(bool live)
    {
        if (_rendered == _text) return;
        _rendered = _text;
        _host.Children.Clear();
        List<MdBlock> blocks;
        try
        {
            blocks = MdParser.Parse(_text);
        }
        catch (Exception)
        {
            blocks = new List<MdBlock>
            {
                new() { Kind = MdBlockKind.Paragraph, Inlines = new List<MdInline> { new() { Kind = MdInlineKind.Text, Text = _text } } },
            };
        }
        for (int i = 0; i < blocks.Count; i++)
        {
            var c = BuildBlock(blocks[i], 0, isLast: i == blocks.Count - 1);
            if (c != null) _host.Children.Add(c);
        }
    }

    // ───────────────────────────── 块 ─────────────────────────────

    private Control? BuildBlock(MdBlock b, int depth, bool isLast = false)
    {
        switch (b.Kind)
        {
            case MdBlockKind.Heading:
            {
                var tb = new TextBlock
                {
                    FontWeight = FontWeight.SemiBold,
                    FontSize = b.Level switch { 1 => 18, 2 => 16, 3 => 14.5, _ => 13.5 },
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, depth == 0 ? 6 : 2, 0, isLast ? 0 : 6),
                };
                tb[!TextBlock.ForegroundProperty] = Res.Bind("B.Ink");
                AddInlines(tb.Inlines!, b.Inlines);
                return tb;
            }
            case MdBlockKind.Paragraph:
            {
                var tb = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13.5,
                    LineHeight = 22,
                    Margin = new Thickness(0, 0, 0, isLast ? 0 : 8),
                };
                tb[!TextBlock.ForegroundProperty] = Res.Bind("B.Ink");
                AddInlines(tb.Inlines!, b.Inlines);
                return tb;
            }
            case MdBlockKind.Bullet:
            case MdBlockKind.Ordered:
            {
                var col = new StackPanel { Margin = new Thickness(0, 0, 0, isLast ? 0 : 8) };
                int start = b.Start;
                for (int i = 0; i < b.Items.Count; i++)
                {
                    int d = i < b.ItemDepth.Count ? b.ItemDepth[i] : 0;
                    string bullet = b.Kind == MdBlockKind.Ordered && d == 0
                        ? (start++).ToString() + "."
                        : d == 0 ? "•" : "◦";
                    var row = new Grid { Margin = new Thickness(4 + d * 18, 1, 0, 1), ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                    var mark = new TextBlock { Text = bullet + " ", FontSize = 13.5, VerticalAlignment = VerticalAlignment.Top };
                    mark[!TextBlock.ForegroundProperty] = Res.Bind("B.InkMuted");
                    row.Children.Add(mark);
                    var body = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, LineHeight = 22 };
                    body[!TextBlock.ForegroundProperty] = Res.Bind("B.Ink");
                    AddInlines(body.Inlines!, b.Items[i]);
                    Grid.SetColumn(body, 1);
                    row.Children.Add(body);
                    col.Children.Add(row);
                }
                return col;
            }
            case MdBlockKind.Quote:
            {
                var inner = new StackPanel { Margin = new Thickness(10, 2, 0, 2) };
                foreach (var child in b.Children)
                {
                    var c = BuildBlock(child, depth + 1);
                    if (c != null) inner.Children.Add(c);
                }
                var border = new Border
                {
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Child = inner,
                    Margin = new Thickness(0, 0, 0, isLast ? 0 : 8),
                };
                border[!Border.BorderBrushProperty] = Res.Bind("B.Line");
                return border;
            }
            case MdBlockKind.Rule:
            {
                var rule = new Border { Height = 1, Margin = new Thickness(0, 6, 0, isLast ? 0 : 10) };
                rule[!Border.BackgroundProperty] = Res.Bind("B.Line");
                return rule;
            }
            case MdBlockKind.Code:
            {
                var code = CodeBlock(b.Raw, b.Lang);
                code.Margin = new Thickness(0, 2, 0, isLast ? 0 : 10);
                return code;
            }
            case MdBlockKind.Table:
                return BuildTable(b, isLast);
            default:
                return null;
        }
    }

    private Control BuildTable(MdBlock b, bool isLast)
    {
        int cols = Math.Max(1, b.Header.Count);
        var grid = new Grid { Margin = new Thickness(0, 2, 0, isLast ? 0 : 10) };
        for (int i = 0; i < cols; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerRow = new Border { Background = null };
        headerRow[!Border.BackgroundProperty] = Res.Bind("B.Paper2");
        var hg = new Grid();
        for (int i = 0; i < cols; i++) hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var firstRow = new List<Control> { headerRow };
        for (int j = 0; j < cols; j++)
        {
            var cell = Cell(j < b.Header.Count ? b.Header[j] : new List<MdInline>(), header: true, Align(b, j));
            Grid.SetColumn(cell, j);
            hg.Children.Add(cell);
        }
        headerRow.Child = hg;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(headerRow, 0);
        grid.Children.Add(headerRow);

        int rowIndex = 1;
        foreach (var row in b.Rows)
        {
            var rg = new Grid();
            for (int i = 0; i < cols; i++) rg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int k = 0; k < cols; k++)
            {
                var cell = Cell(k < row.Count ? row[k] : new List<MdInline>(), header: false, Align(b, k));
                Grid.SetColumn(cell, k);
                rg.Children.Add(cell);
            }
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(rg, rowIndex);
            grid.Children.Add(rg);
            rowIndex++;
        }
        return grid;
    }

    private static TextAlignment Align(MdBlock b, int i)
    {
        string a = i < b.Align.Count ? b.Align[i] : "left";
        return a == "center" ? TextAlignment.Center : a == "right" ? TextAlignment.Right : TextAlignment.Left;
    }

    private Control Cell(List<MdInline> inlines, bool header, TextAlignment align)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            TextAlignment = align,
            Margin = new Thickness(10, 6, 14, 6),
        };
        if (header)
        {
            tb.FontWeight = FontWeight.SemiBold;
            tb[!TextBlock.ForegroundProperty] = Res.Bind("B.InkMuted");
        }
        else
        {
            tb[!TextBlock.ForegroundProperty] = Res.Bind("B.Ink");
        }
        AddInlines(tb.Inlines!, inlines);
        var border = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = tb };
        border[!Border.BorderBrushProperty] = Res.Bind("B.LineSoft");
        return border;
    }

    // ───────────────────────────── 行内 ─────────────────────────────

    private void AddInlines(InlineCollection target, List<MdInline> inlines)
    {
        foreach (var x in inlines)
        {
            var inline = BuildInline(x);
            if (inline != null) target.Add(inline);
        }
    }

    private Inline? BuildInline(MdInline x)
    {
        switch (x.Kind)
        {
            case MdInlineKind.Text:
                return new Run(x.Text);
            case MdInlineKind.Break:
                return new LineBreak();
            case MdInlineKind.Code:
            {
                // R21c:P5 记的「Avalonia 的行内不支持背景色(Run 无 Background)」在 11.3.2 上已不成立 ——
                // `TextElement` 有 `BackgroundProperty`(Run 是 Inline → TextElement),而且真会被画出来
                // (无头 Skia 渲染取像素验证:纯红底 Run 数出 1781 个红像素)。
                // 于是照参考版原样来:**同一个属性、同一个键** ——
                // 参考版 Pages/Ai/MdView.cs:340 就是 `SetResourceReference(TextElement.BackgroundProperty, "B.Hover")`。
                // 不用像链接那样包 InlineUIContainer:Border 外壳会把行内码变成一个不可折行的方块
                // (实测 320 宽段落里 80 字长标识符外壳宽 536,会被裁掉),而 Run 照常参与折行、
                // 底色只画不改布局,段落行距与折行行为与改动前完全一致。
                // 底色 B.Hover:浅色 #F2F2F2 / 深色 #2A2A2A,上压 B.AccentText 的对比是 4.7:1 / 7.3:1。
                var run = new Run(" " + x.Text + " ") { FontFamily = Mono, FontSize = 12 };
                run[!TextElement.ForegroundProperty] = Res.Bind("B.AccentText");
                run[!TextElement.BackgroundProperty] = Res.Bind("B.Hover");
                return run;
            }
            case MdInlineKind.Bold:
            {
                var bold = new Bold();
                AddInlines(bold.Inlines, x.Children);
                return bold;
            }
            case MdInlineKind.Italic:
            {
                var italic = new Italic();
                AddInlines(italic.Inlines, x.Children);
                return italic;
            }
            case MdInlineKind.Strike:
            {
                var span = new Span { TextDecorations = TextDecorations.Strikethrough };
                AddInlines(span.Inlines, x.Children);
                return span;
            }
            case MdInlineKind.Link:
            {
                // R17:Avalonia 的 Span 不是 Control(没有 Cursor / ToolTip / 事件),但 **Inline 可以是
                // InlineUIContainer** —— 里面放一个真控件,链接就真的能点(对齐参考版用 WPF Hyperlink 的行为;
                // D-22 记的「链接不可点」这一条平台限制因此不再成立)。
                var inner = new TextBlock
                {
                    TextDecorations = TextDecorations.Underline,
                    FontSize = 13.5,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                inner[!TextBlock.ForegroundProperty] = Res.Bind(LinkInk);
                AddInlines(inner.Inlines!, x.Children);
                // 只对真的点了链接的按下动作做响应;失败不抛(打不开浏览器时用户还能自己复制)
                inner.Cursor = new Cursor(StandardCursorType.Hand);
                ToolTip.SetTip(inner, x.Url);
                inner.PointerReleased += (_, e) =>
                {
                    OpenLink(x.Url);
                    e.Handled = true;
                };
                // R21c:悬停反馈。参考版用 WPF 的 Hyperlink —— 自带 MouseOver 变色,这边得手挂。
                // 两个键都是现成的同族主题色:常态 B.AccentText(浅 #1F7A52 / 深 #79C8A3)、
                // 悬停 B.Accent(浅 #2E9B6B / 深 #3FB07C)——两套主题里都「看得出变化」,
                // 且两者在各自底色上都还有可读对比(浅色档 5.3:1 → 3.5:1,深色档 8.4:1 → 6.1:1)。
                // 用 ![...] = Res.Bind(...) 重新挂观察绑定(不是直接赋画刷):已实测「重挂会顶掉旧绑定」,
                // 所以离开时能干净地回到常态色,而不是把浅色画刷留在深色主题里。
                inner.PointerEntered += (_, _) => inner[!TextBlock.ForegroundProperty] = Res.Bind(LinkHoverInk);
                inner.PointerExited += (_, _) => inner[!TextBlock.ForegroundProperty] = Res.Bind(LinkInk);
                return new InlineUIContainer { Child = inner, BaselineAlignment = BaselineAlignment.Center };
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// 打开外链。默认交给系统浏览器(`xdg-open` / `open` / Windows 的 ShellExecute);
    /// 测试把它替换成收集器,免得真弹浏览器(与各页私有的 OpenUrl 同一套做法)。
    /// </summary>
    internal static Action<string> UrlOpener { get; set; } = OpenExternal;

    /// <summary>按当前平台调系统命令打开 URL;失败只当没打开(不影响对话)。</summary>
    private static void OpenExternal(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else
            {
                Process.Start(OperatingSystem.IsMacOS() ? "open" : "xdg-open", new[] { url });
            }
        }
        catch (Exception)
        {
            // 打不开浏览器不算致命:用户可手动复制地址
        }
    }

    /// <summary>链接被点击:空 URL 忽略,其余交给 <see cref="UrlOpener"/>。</summary>
    internal static void OpenLink(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }
        UrlOpener(url);
    }

    /// <summary>代码块(照 Windows 版 MdView.CodeBlock:深底 + 语言标签 + 复制钮 + 横向滚动只读框)。</summary>
    internal static Control CodeBlock(string code, string lang)
    {
        var box = new TextBox
        {
            Text = code,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = Mono,
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            AcceptsReturn = true,
            Padding = new Thickness(0),
            CaretBrush = Brushes.White,
            Foreground = new SolidColorBrush(Color.FromRgb(216, 237, 227)),
        };
        var scroll = new ScrollViewer
        {
            Content = box,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var langText = UiClass.Txt(string.IsNullOrWhiteSpace(lang) ? "code" : lang, 11);
        langText.Foreground = new SolidColorBrush(Color.FromRgb(140, 176, 160));
        var copy = new Button
        {
            Content = UiClass.Txt("复制", 11, fg: "B.AccentText"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 1, 4, 1),
            Focusable = false,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        copy.Click += (_, _) =>
        {
            var clipboard = Avalonia.Application.Current is { } app
                ? (app.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow is { } w
                    ? TopLevel.GetTopLevel(w)?.Clipboard
                    : null
                : null;
            if (clipboard == null) return;
            _ = clipboard.SetTextAsync(code);
            ToastHost.Show("已复制", "", ToastKind.Success);
        };

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        langText.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(langText);
        Grid.SetColumn(copy, 1);
        head.Children.Add(copy);

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(31, 42, 37)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 7, 12, 10),
            Child = new StackPanel { Spacing = 4, Children = { head, scroll } },
        };
        return border;
    }
}
