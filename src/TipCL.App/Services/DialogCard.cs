using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace TipCL.Services;

/// <summary>
/// 对话框卡片。对应 Windows <c>Services\Dlg.cs</c> 里 <c>Shell</c>(<c>Dlg.cs:185-213</c>)建的那块
/// <c>Border</c>,也就是 <see cref="Dlg.Confirm"/> / <c>Ask</c> / <c>Alert</c> / <c>Prompt</c> /
/// <c>Choose</c> / <c>Form</c> 共用的那张卡片:
/// 标题(<c>Ui.H2</c>)+ 正文(超出时自己滚)+ 底部按钮行(<c>Auto,*,Auto</c> 三行布局),
/// 玻璃底 + 玻璃描边、圆角 10、内边距 (22,18,22,18)、描边 1,
/// 宽度夹在 <c>Min(width, 380)</c> ~ <c>width</c> 之间,并挂 40/0.28/10 的阴影。
///
/// C2a 移植 <c>Dlg</c> 时已把这段**逐行等价**地内联在 <c>Dlg.Shell</c> 里;C2c1 只是把它提成
/// 具名类型,好让"对话框卡片"本身能被单独钉住(标题 / 正文 / 按钮组 / 宽度 / 取色 / 阴影),
/// 并让所有卡片式对话框都渲染在同一张卡片上。**控件树、取值、行为一个字没改**(见 D-107)。
///
/// 注意:Windows 的 <c>Pages\ConfirmCard.cs</c> **不是**这张卡片 —— 它是 AI 页「助手要执行一个操作,
/// 需要你确认」的工具审批卡,只被 <c>AiPage</c> / <c>AiFixWindow</c> 用到,依赖
/// <c>AiRisk</c> / <c>ToolMeta</c> / <c>AiUiText</c> / <c>AiIcons</c> / <c>MdView</c> 这些 AI 页基础设施,
/// 属 AI 全家(C7+)的范围;本包按红线"不许移植任何页面"没有搬它(见 D-107 与 PROGRESS 遗留)。
/// </summary>
internal sealed class DialogCard : Border
{
    /// <summary>【Windows 源】<c>Dlg.Ask</c> 的默认宽度 <c>520.0</c>。</summary>
    internal const double ConfirmWidth = 520.0;

    /// <summary>【Windows 源】<c>Math.Min(width, 380.0)</c> 里那个 380。</summary>
    internal const double MinWidthFloor = 380.0;

    /// <summary>【Windows 源】标题/正文/footer 三行之间的 14px 间距。</summary>
    internal const double SectionGap = 14.0;

    /// <summary>【Windows 源】卡片圆角 10、内边距 (22,18,22,18)、描边 1。</summary>
    internal const double CardCornerRadius = 10.0;

    internal static readonly Thickness CardPadding = new Thickness(22.0, 18.0, 22.0, 18.0);

    /// <summary>【Windows 源】footer 行里按钮之间的间距(Windows 的 <c>Ui.H(8.0, ...)</c>)。</summary>
    internal const double FooterSpacing = 8.0;

    internal DialogCard(string title, Control body, Control? footer, double width)
    {
        Title = title ?? "";
        BodyControl = body ?? throw new ArgumentNullException(nameof(body));
        TitleBlock = Ui.H2(Title).VCenter();
        BodyScroll = new ScrollViewer
        {
            Content = BodyControl,
            Margin = new Thickness(0.0, SectionGap, 0.0, 0.0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        Grid grid = Ui.G("Auto,*,Auto");
        grid.Add(TitleBlock, 0);
        grid.Add(BodyScroll, 1);
        if (footer is not null)
        {
            FooterRow = footer;
            footer.Margin = new Thickness(0.0, SectionGap, 0.0, 0.0);
            grid.Add(footer, 2);
        }

        CornerRadius = new CornerRadius(CardCornerRadius);
        Padding = CardPadding;
        BorderThickness = new Thickness(1.0);
        MaxWidth = width;
        MinWidth = Math.Min(width, MinWidthFloor);
        Child = grid;
        this.Dyn(Border.BackgroundProperty, "B.GlassStrong");
        this.Dyn(Border.BorderBrushProperty, "B.GlassBorder");
        Motion.Shadow(this, 40.0, 0.28, 10.0);
    }

    internal string Title { get; }

    internal TextBlock TitleBlock { get; }

    /// <summary>正文控件(调用方给的那个:文本、输入框、表单、按钮组……)。</summary>
    internal Control BodyControl { get; }

    /// <summary>包住正文的滚动容器(正文超高时滚动,按钮行不被挤出窗口,WP-S11)。</summary>
    internal ScrollViewer BodyScroll { get; }

    /// <summary>底部按钮行;没有 footer 时是 <c>null</c>。</summary>
    internal Control? FooterRow { get; }

    /// <summary>卡片的三行布局(标题/正文/按钮行)。</summary>
    internal Grid Layout => (Grid)Child!;

    /// <summary>
    /// 【Windows 源】<c>Dlg.Ask</c> 的按钮组:取消(普通)+ 确定(主按钮;危险操作时用危险按钮),
    /// 右对齐、间距 8。放这里是为了让"确认卡片该有哪些按钮"能被单独钉住。
    /// </summary>
    internal static ConfirmButtons ConfirmFooter(string? ok, string? cancel, bool danger)
    {
        Button okButton = Ui.Btn(ok ?? I18n.L("确定"), danger ? BtnKind.Danger : BtnKind.Primary);
        Button cancelButton = Ui.Btn(cancel ?? I18n.L("取消"));
        SPanel footer = Ui.H(FooterSpacing, cancelButton, okButton).Right();
        return new ConfirmButtons(cancelButton, okButton, footer);
    }

    /// <summary>确认卡片的按钮组(取消、确定、以及装它们的 footer)。</summary>
    internal readonly struct ConfirmButtons
    {
        internal ConfirmButtons(Button cancel, Button ok, SPanel footer)
        {
            Cancel = cancel;
            Ok = ok;
            Footer = footer;
        }

        /// <summary>取消按钮(Windows 里是 <c>button2</c>)。</summary>
        internal Button Cancel { get; }

        /// <summary>确定按钮(Windows 里是 <c>button</c>)。</summary>
        internal Button Ok { get; }

        /// <summary>右对齐的按钮行。</summary>
        internal SPanel Footer { get; }
    }
}
