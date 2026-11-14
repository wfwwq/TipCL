// 来源: Windows 版参考仓库 src/TipCL/Pages/ConfirmCard.cs(M4-H P5)。
// 权限确认卡:风险标签(只读/联网/写入/删除)+ 工具标签 + 可选预览(diff 着色)+
// 「允许 / 本对话都允许(仅当前实例|所有实例) / 拒绝」;决定了会冻结并把选择回调出去。
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.App.Common;
using TipCL.Core.Launcher.Ai;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Ai;

/// <summary>权限确认卡(照 Windows 版 ConfirmCard)。</summary>
internal sealed class ConfirmCard : Border
{
    private readonly Action<bool, bool, string> _answer;
    private bool _done;

    internal AiRisk Risk { get; }

    internal bool Answered => _done;

    /// <summary>
    /// 这张卡实际收到的预览行(调用点传进来的那份,未经裁剪)。给回归测试用:确认链路的断链
    /// ——「界面支持预览、调用点却传空列表」—— 只看 Preview 本身是查不出来的,必须断言卡片真拿到了行。
    /// </summary>
    internal IReadOnlyList<string> PreviewLines { get; }

    public ConfirmCard(string tool, string label, string reason, List<string> preview, bool allowAlways,
        Action<bool, bool, string> answer)
    {
        _answer = answer;
        string sideEffect = ToolMeta.Get(tool)?.side_effect ?? "";
        Risk = AiUiText.RiskOf(tool, sideEffect);

        string fgKey = Risk switch
        {
            AiRisk.Delete => "B.Danger",
            AiRisk.Write => "B.Warn",
            AiRisk.Network => "B.Info",
            _ => "B.InkMuted",
        };
        string bgKey = Risk switch
        {
            AiRisk.Delete => "B.DangerSoft",
            AiRisk.Write => "B.WarnSoft",
            AiRisk.Network => "B.InfoSoft",
            _ => "B.Hover",
        };
        string iconData = Risk switch
        {
            AiRisk.Delete => AiIcons.Trash,
            AiRisk.Network => AiIcons.Globe,
            _ => tool.StartsWith("write_", StringComparison.Ordinal) ? AiIcons.Pen
                : (tool.StartsWith("install_", StringComparison.Ordinal) ? AiIcons.Box : AiIcons.Alert),
        };

        var head = UiClass.G(null, "Auto,*");
        head.Add(AiIcons.Icon(AiIcons.Alert, 16, Risk == AiRisk.Delete ? "B.Danger" : "B.Warn").VCenter(), 0, 0);
        var headText = UiClass.Txt("助手要执行一个操作,需要你确认", 13, true, "B.Ink");
        headText.Margin = new Thickness(9, 0, 0, 0);
        head.Add(headText.VCenter(), 0, 1);
        var header = new Border { Padding = new Thickness(14, 10, 14, 10), Child = head };
        header[!Border.BackgroundProperty] = Res.Bind(Risk == AiRisk.Delete ? "B.DangerSoft" : "B.WarnSoft");

        var tag = UiClass.Tag(RiskText(Risk), fgKey, bgKey);
        var labelText = UiClass.Txt(label, 13, true, "B.Ink").Wrap();
        labelText.Margin = new Thickness(9, 0, 10, 0);
        var row = UiClass.G(null, "Auto,*,Auto");
        var rowIcon = AiIcons.Icon(iconData, 15, fgKey);
        rowIcon.Margin = new Thickness(0, 2, 0, 2);
        row.Add(rowIcon, 0, 0);
        row.Add(labelText, 0, 1);
        row.Add(tag.VCenter(), 0, 2);

        var body = UiClass.V(6, row);
        if (Risk == AiRisk.Delete)
        {
            var warn = UiClass.Txt("删除类操作:如果这一轮没有检查点,删掉后无法一键撤回", 12, false, "B.Danger").Wrap();
            warn.Margin = new Thickness(24, 0, 0, 0);
            body.Children.Add(warn);
        }
        var previewLines = preview.Where(l => l != null).ToList();
        PreviewLines = previewLines;
        if (previewLines.Count > 0)
        {
            var box = PreviewBox(previewLines);
            box.Margin = new Thickness(24, 2, 0, 2);
            body.Children.Add(box);
        }
        if (!string.IsNullOrWhiteSpace(reason))
        {
            var reasonText = UiClass.Txt(reason.Trim(), 12, false, "B.InkMuted").Wrap();
            reasonText.Margin = new Thickness(24, 0, 0, 0);
            body.Children.Add(reasonText);
        }

        var bodyHost = new Border
        {
            Padding = new Thickness(14, 11, 14, 11),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = body,
        };
        bodyHost[!Border.BorderBrushProperty] = Res.Bind("B.LineSoft");

        // 按钮的点击回调在局部函数 Done 之后才绑定:局部函数必须先声明才好在 lambda 里引用
        var yes = U.Btn("允许", BtnKind.Primary);
        var always = U.Btn("本对话都允许", BtnKind.Normal);
        ToolTip.SetTip(always, "同类操作以后不再询问");
        var scope = UiClass.Combo(new[] { "仅当前实例", "所有实例" }, "仅当前实例", 110);
        var no = U.Btn("拒绝", BtnKind.Normal);
        var state = UiClass.Txt("", 12, false, "B.InkMuted");
        always.IsVisible = allowAlways;
        scope.IsVisible = allowAlways;

        var actions = UiClass.G(null, "Auto,Auto,Auto,*,Auto");
        actions.Add(yes, 0, 0);
        always.Margin = new Thickness(8, 0, 0, 0);
        actions.Add(always, 0, 1);
        scope.Margin = new Thickness(8, 0, 0, 0);
        actions.Add(scope.VCenter(), 0, 2);
        state.Margin = new Thickness(10, 0, 0, 0);
        actions.Add(state.VCenter(), 0, 3);
        actions.Add(no, 0, 4);
        var actionHost = new Border
        {
            Padding = new Thickness(14, 9, 14, 10),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = actions,
        };
        actionHost[!Border.BorderBrushProperty] = Res.Bind("B.LineSoft");
        actionHost[!Border.BackgroundProperty] = Res.Bind("B.Paper2");

        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        MaxWidth = 760;
        ClipToBounds = true;
        this[!Border.BackgroundProperty] = Res.Bind("B.Paper");
        this[!Border.BorderBrushProperty] = Res.Bind(Risk == AiRisk.Delete ? "B.Danger" : "B.Warn");
        BoxShadow = new BoxShadows(new BoxShadow
        {
            Blur = 18,
            OffsetY = 4,
            Color = Color.FromArgb(26, 0, 0, 0),
        });
        Child = UiClass.V(0, header, bodyHost, actionHost);

        void Done(bool ok, bool remember)
        {
            if (_done)
            {
                return;
            }
            _done = true;
            yes.IsEnabled = false;
            always.IsEnabled = false;
            no.IsEnabled = false;
            scope.IsEnabled = false;
            state.Text = !ok ? "已拒绝" : (remember ? "已记住并允许" : "已允许");
            _answer(ok, remember, scope.SelectedIndex == 1 ? "global" : "instance");
        }

        // 事件接线放在 Done 之后(局部函数要先声明)
        yes.Click += (_, _) => Done(true, false);
        always.Click += (_, _) => Done(true, true);
        no.Click += (_, _) => Done(false, false);
    }

    /// <summary>宿主换页/清空时用它把这张卡判成「拒绝」,不让工具调用一直挂着。</summary>
    internal void Reject()
    {
        if (_done)
        {
            return;
        }
        _done = true;
        _answer(false, false, "instance");
    }

    private static string RiskText(AiRisk risk) => AiUiText.RiskText(risk);

    /// <summary>预览框(像 diff 就按 +/- 着色;最多 40 行 + 「还有 N 行」)。</summary>
    private static Control PreviewBox(List<string> lines)
    {
        bool diff = AiUiText.LooksLikeDiff(lines);
        var body = UiClass.V(0);
        foreach (string line in lines.Take(40))
        {
            char kind = diff ? AiUiText.DiffKind(line) : ' ';
            var text = new TextBlock
            {
                Text = line,
                FontFamily = MdView.Mono,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(10, 2, 10, 2),
            };
            var frame = new Border { Child = text };
            switch (kind)
            {
                case '-':
                    frame.Background = new SolidColorBrush(Color.FromArgb(40, 214, 69, 69));
                    text[!TextBlock.ForegroundProperty] = Res.Bind("B.Danger");
                    break;
                case '+':
                    frame.Background = new SolidColorBrush(Color.FromArgb(40, 46, 155, 107));
                    text[!TextBlock.ForegroundProperty] = Res.Bind("B.AccentText");
                    break;
                case '@':
                    text[!TextBlock.ForegroundProperty] = Res.Bind("B.InkMuted");
                    break;
                default:
                    text[!TextBlock.ForegroundProperty] = Res.Bind("B.InkSoft");
                    break;
            }
            body.Children.Add(frame);
        }
        if (lines.Count > 40)
        {
            var more = UiClass.Txt($"...还有 {lines.Count - 40} 行", 11.5, false, "B.InkMuted");
            more.Margin = new Thickness(10, 2, 0, 2);
            body.Children.Add(more);
        }
        var scroll = new ScrollViewer
        {
            Content = body,
            MaxHeight = 180,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var host = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0, 4, 0, 4),
            Child = scroll,
            ClipToBounds = true,
        };
        host[!Border.BorderBrushProperty] = Res.Bind("B.LineSoft");
        host[!Border.BackgroundProperty] = Res.Bind("B.Paper2");
        return host;
    }

    /// <summary>这个工具是否给「本对话都允许」(删除类不给;照 Windows 版 AllowAlways)。</summary>
    public static bool AllowAlways(string tool) => tool switch
    {
        "delete_instance" or "delete_mod" => false,
        _ => true,
    };
}
