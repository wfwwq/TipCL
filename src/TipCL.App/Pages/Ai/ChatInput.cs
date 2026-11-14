// 来源: Windows 版参考仓库 src/TipCL/Pages/ChatInput.cs(M4-H P5 精简移植)。
// 底部输入区:多行输入(Enter 发送 / Shift+Enter 换行)+ 发送/停止钮 + 模型与提示行。
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using TipCL.App.Common;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Ai;

/// <summary>AI 对话输入区。</summary>
internal sealed class ChatInput : Border
{
    public TextBox Box { get; } = UiClass.Multi("问点什么…(Enter 发送,Shift+Enter 换行)", "", 72);

    public Button Send { get; } = UiClass.Btn("发送", BtnKind.Primary);

    public Button Stop { get; } = UiClass.Btn("停止", BtnKind.Ghost);

    public TextBlock Hint { get; } = UiClass.Small("");

    /// <summary>输入框按键钩子(图片粘贴等;置 e.Handled 即吞掉这次按键,照 Windows 版 PreviewKeyDown)。</summary>
    public Action<KeyEventArgs>? Paste { get; set; }

    /// <summary>
    /// 发送键之前先过一遍的钩子(斜杠候选的上下键/Tab/Esc;返回 true 表示这次按键已被处理)。
    /// 在 Enter 发送之前调用,所以「Enter 选中候选」与「Enter 直接发送」不会打架。
    /// </summary>
    public Func<KeyEventArgs, bool>? PreKey { get; set; }

    /// <summary>文本变化(斜杠候选据此刷新)。</summary>
    public Action? TextChanged { get; set; }

    public ChatInput(Action onSend, Action onStop)
    {
        Send.Click += (_, _) => onSend();
        Stop.Click += (_, _) => onStop();
        Stop.IsVisible = false;
        Box.KeyDown += (_, e) =>
        {
            Paste?.Invoke(e);
            if (e.Handled)
            {
                return;
            }
            if (PreKey != null && PreKey(e))
            {
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                e.Handled = true;
                onSend();
            }
        };
        Box.TextChanged += (_, _) => TextChanged?.Invoke();

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        Box.AcceptsReturn = true;
        row.Children.Add(Box);
        Send.Margin = new Avalonia.Thickness(8, 0, 0, 0);
        Send.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(Send, 1);
        row.Children.Add(Send);
        Stop.Margin = new Avalonia.Thickness(8, 0, 0, 0);
        Stop.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(Stop, 2);
        row.Children.Add(Stop);

        Hint.Margin = new Avalonia.Thickness(2, 4, 0, 0);
        Child = new StackPanel { Spacing = 0, Children = { row, Hint } };
        Padding = new Avalonia.Thickness(12, 10, 12, 10);
        CornerRadius = new Avalonia.CornerRadius(10);
        this[!BackgroundProperty] = Res.Bind("B.Paper");
        this[!BorderBrushProperty] = Res.Bind("B.Line");
        BorderThickness = new Avalonia.Thickness(1);
    }

    public string Text
    {
        get => Box.Text ?? "";
        set => Box.Text = value;
    }
}
