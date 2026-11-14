// 来源: Windows 版参考仓库 src/TipCL/Pages/{Bubble.cs,ThinkFold.cs,ToolLine.cs 等}(M4-H P5 精简移植)。
// Linux 侧把「气泡 + 思考折叠」合到一处:角色头像 / 名字 / Markdown 正文 / 复制钮 / 思考折叠行。
// 降到最小:不做工具行与步骤组(P5 剩余项,见 PROGRESS 遗留),但保留版式与配色口径。
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.App.Common;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Ai;

/// <summary>一条对话气泡(用户 / 助手 / 系统提示)。</summary>
internal sealed class AiBubble : Border
{
    private readonly MdView _md = new();
    private readonly TextBlock _thinkText = UiClass.Muted("");
    private readonly Border _thinkBox;
    private readonly TextBlock _note = UiClass.Small("");

    public AiBubble(string role, string text, bool thinking = false)
    {
        bool user = role == "user";

        var avatar = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            VerticalAlignment = VerticalAlignment.Top,
        };
        avatar[!Border.BackgroundProperty] = Res.Bind(user ? "B.AccentSoft" : "B.AccentStrong");
        avatar.Child = IconLib.Make(user ? "User" : "Lightning", user ? "B.AccentText" : "B.OnAccentStrong", 14);

        var name = UiClass.Txt(user ? "你" : "AI 助手", 12, bold: true, user ? "B.InkMuted" : "B.AccentText");

        var copy = UiClass.IconBtn("Copy", "复制这条回复", null, 12);
        copy.Click += (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;
            _ = clipboard.SetTextAsync(_md.Markdown);
            ToastHost.Show("已复制", "", ToastKind.Success);
        };

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        avatar.Margin = new Thickness(0, 0, 8, 0);
        head.Children.Add(avatar);
        name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        head.Children.Add(name);
        Grid.SetColumn(copy, 2);
        head.Children.Add(copy);

        _thinkText.TextWrapping = TextWrapping.Wrap;
        _thinkText.FontSize = 12;
        _thinkBox = new Border
        {
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 6, 0, 0),
            CornerRadius = new CornerRadius(8),
            Child = _thinkText,
            IsVisible = false,
        };
        _thinkBox[!Border.BackgroundProperty] = Res.Bind("B.Paper2");

        _md.SetMarkdown(text);
        _note.IsVisible = false;

        var col = new StackPanel { Spacing = 4, Children = { head, _md, _thinkBox, _note } };
        Child = col;
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(12, 10, 12, 10);
        Margin = new Thickness(user ? 40 : 0, 0, user ? 0 : 40, 8);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        this[!BackgroundProperty] = Res.Bind(user ? "B.AccentSoft" : "B.Paper2");
    }

    /// <summary>当前正文(供历史回填)。</summary>
    public string Markdown() => _md.Markdown;

    /// <summary>流式增量:正文走 Markdown(节流),思考段单独收进折叠块。</summary>
    public void AppendAnswer(string delta)
    {
        _md.SetMarkdown(_md.Markdown + delta, live: true);
    }

    public void SetThinking(string think)
    {
        if (string.IsNullOrEmpty(think))
        {
            _thinkBox.IsVisible = false;
            return;
        }
        _thinkBox.IsVisible = true;
        _thinkText.Text = "思考:" + Environment.NewLine + think.Trim();
    }

    public void SetNote(string note)
    {
        _note.Text = note;
        _note.IsVisible = note.Length > 0;
    }
}
