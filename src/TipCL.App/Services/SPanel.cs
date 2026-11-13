using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace TipCL.Services;

/// <summary>
/// C1c:Windows 版 <c>Services\SPanel.cs</c>(WPF)到 Avalonia 11 的移植。
///
/// 语义逐条对齐 WPF 那份:
/// - 纵向:宽取子项最大期望宽,高是各子项期望高之和 + <see cref="Spacing"/> ×(可见子项数 − 1);
/// - 横向:高取子项最大期望高,宽是各子项期望宽之和 + 间距;
/// - 被隐藏的子项(<c>IsVisible == false</c>,对应 WPF 的 <c>Visibility.Collapsed</c>)既不量也不排,
///   也**不占**间距。
///
/// 与 WPF 原版的两处框架差异(不是设计差异):
/// 1. WPF 的 <c>Panel.InternalChildren</c> 在 Avalonia 里是 <c>Panel.Children</c>(<c>Control</c> 集合);
/// 2. WPF 的 <c>UIElement.Measure/Arrange</c> 在 Avalonia 里是 <c>Layoutable.Measure/Arrange</c>,
///    参数与返回类型(<see cref="Size"/>)一致。
///
/// 另外这里给两个属性加了 <c>InvalidateMeasure()</c>:WPF 原版是普通 CLR 属性(Panel 不会因为赋值重排),
/// 但那只是"没人在排完布局之后再改它"的巧合;Avalonia 的布局缓存更容易命中,显式失效更稳,
/// 公开成员(类型/名字)与 WPF 完全一致。
/// </summary>
public sealed class SPanel : Panel
{
    private Orientation _orientation = Orientation.Vertical;

    private double _spacing = 8.0;

    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation != value)
            {
                _orientation = value;
                InvalidateMeasure();
            }
        }
    }

    public double Spacing
    {
        get => _spacing;
        set
        {
            if (Math.Abs(_spacing - value) > double.Epsilon)
            {
                _spacing = value;
                InvalidateMeasure();
            }
        }
    }

    protected override Size MeasureOverride(Size available)
    {
        double main = 0.0;
        double cross = 0.0;
        int visible = 0;
        Size childAvailable = Orientation == Orientation.Vertical
            ? new Size(available.Width, double.PositiveInfinity)
            : new Size(double.PositiveInfinity, available.Height);

        foreach (Control child in Children)
        {
            child.Measure(childAvailable);
            if (!child.IsVisible)
            {
                continue;
            }

            visible++;
            if (Orientation == Orientation.Vertical)
            {
                cross = Math.Max(cross, child.DesiredSize.Width);
                main += child.DesiredSize.Height;
            }
            else
            {
                cross = Math.Max(cross, child.DesiredSize.Height);
                main += child.DesiredSize.Width;
            }
        }

        double gaps = Math.Max(0, visible - 1) * Spacing;
        main += gaps;

        return Orientation == Orientation.Vertical
            ? new Size(double.IsInfinity(available.Width) ? cross : Math.Min(cross, Math.Max(cross, available.Width)), main)
            : new Size(main, double.IsInfinity(available.Height) ? cross : cross);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double offset = 0.0;
        foreach (Control child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            if (Orientation == Orientation.Vertical)
            {
                child.Arrange(new Rect(0.0, offset, final.Width, child.DesiredSize.Height));
                offset += child.DesiredSize.Height + Spacing;
            }
            else
            {
                child.Arrange(new Rect(offset, 0.0, child.DesiredSize.Width, final.Height));
                offset += child.DesiredSize.Width + Spacing;
            }
        }

        return final;
    }
}
