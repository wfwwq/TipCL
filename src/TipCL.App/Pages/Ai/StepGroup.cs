// 来源: Windows 版参考仓库 src/TipCL/Pages/Ai/StepGroup.cs(M4-H P5)。
// 一组工具调用的折叠卡:标题写「正在…/执行了 N 步 · 用时 Xs」,标题行可折叠,超过 3 步自动收起。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.App.Common;
using TipCL.Core.Launcher.Ai;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Ai;

/// <summary>步骤组(照 Windows 版 StepGroup)。</summary>
internal sealed class StepGroup : Border
{
    private readonly StackPanel _body = UiClass.V(0);
    private readonly Border _bodyHost;
    private readonly TextBlock _title;
    private readonly TextBlock _count;
    private readonly Avalonia.Controls.Shapes.Path _chev;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private bool _open = true;
    private bool _running = true;
    private bool _userToggled;

    internal int Count => _body.Children.OfType<ToolLine>().Count();

    internal bool IsOpen => _open;

    internal string TitleText => _title.Text ?? "";

    public StepGroup()
    {
        _title = UiClass.Txt("正在执行", 12.5, true, "B.Ink");
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _count = UiClass.Txt("", 12, false, "B.InkFaint");
        _chev = AiIcons.Icon(AiIcons.ChevDown, 14, "B.InkFaint");
        _chev.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        _chev.RenderTransform = new RotateTransform(0);

        var head = new Grid { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
        head.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        head.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        head.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        head.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        head.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        head.Children.Add(AiIcons.Icon(AiIcons.Layers, 14, "B.InkSoft"));
        Grid.SetColumn(head.Children[0], 0);
        var titleHost = _title;
        titleHost.VerticalAlignment = VerticalAlignment.Center;
        titleHost.Margin = new Thickness(8, 0, 0, 0);
        head.Children.Add(titleHost);
        Grid.SetColumn(titleHost, 1);
        _count.VerticalAlignment = VerticalAlignment.Center;
        _count.Margin = new Thickness(8, 0, 0, 0);
        head.Children.Add(_count);
        Grid.SetColumn(_count, 2);
        _chev.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(_chev);
        Grid.SetColumn(_chev, 4);
        head.Margin = new Thickness(12, 8, 12, 8);
        head.PointerReleased += (_, e) =>
        {
            _userToggled = true;
            SetOpen(!_open);
            e.Handled = true;
        };

        _bodyHost = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(6, 4, 6, 6),
            Child = _body,
        };
        _bodyHost[!Border.BorderBrushProperty] = Res.Bind("B.LineSoft");

        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        BorderBrush = Res.Bind("B.Line") as IBrush;
        this[!Border.BorderBrushProperty] = Res.Bind("B.Line");
        this[!Border.BackgroundProperty] = Res.Bind("B.Paper");
        Child = UiClass.V(0, head, _bodyHost);
        Refresh();
    }

    internal IEnumerable<ToolLine> Lines => _body.Children.OfType<ToolLine>();

    /// <summary>加一行(重复加同一行时只刷新)。</summary>
    public void AddLine(ToolLine line)
    {
        if (line.Parent is Panel panel)
        {
            if (ReferenceEquals(panel, _body))
            {
                Refresh();
                return;
            }
            panel.Children.Remove(line);
        }
        _body.Children.Add(line);
        Refresh();
    }

    /// <summary>子代理区块(留出缩进;P5 的 Linux 侧暂未开子代理,组件保留以免与参考版结构脱节)。</summary>
    public StackPanel AddSubagent(string title)
    {
        var inner = UiClass.V(2);
        var frame = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Margin = new Thickness(35, 2, 0, 4),
            Child = UiClass.V(2, UiExt.Mk(UiClass.H(6, AiIcons.Icon(AiIcons.Branch, 13, "B.InkMuted").VCenter(),
                UiClass.Txt(title, 11.5, false, "B.InkMuted").VCenter()), 6, 3, 0, 3), inner),
        };
        frame[!Border.BorderBrushProperty] = Res.Bind("B.Line");
        _body.Children.Add(frame);
        return inner;
    }

    /// <summary>刷新标题(运行中显示「正在 X」,结束后「执行了 N 步 · 用时 Xs」)。</summary>
    public void Refresh()
    {
        var lines = Lines.ToArray();
        ToolLine? running = lines.LastOrDefault(l => l.State is ToolState.Running or ToolState.Prepare);
        bool busy = _running && running != null;
        _title.Text = AiUiText.StepSummary(lines.Length, _sw.Elapsed.TotalSeconds, busy, running?.LabelText ?? "");
        int failed = lines.Count(l => l.State == ToolState.Failed);
        _count.Text = busy ? $"· {lines.Length} 步" : (failed > 0 ? $"· {failed} 步失败" : "");
        _count[!TextBlock.ForegroundProperty] = Res.Bind(failed > 0 && !busy ? "B.Danger" : "B.InkFaint");
    }

    /// <summary>本轮结束:停表,没被用户手动展开过且步骤超过 3 行时自动收起。</summary>
    public void Complete()
    {
        _running = false;
        _sw.Stop();
        Refresh();
        if (!_userToggled && Count > 3)
        {
            SetOpen(false);
        }
    }

    public void SetOpen(bool open)
    {
        _open = open;
        _bodyHost.IsVisible = open;
        if (_chev.RenderTransform is RotateTransform rotate)
        {
            rotate.Angle = open ? 0 : -90;
        }
    }
}