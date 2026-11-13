using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace TipCL.Services;

/// <summary>
/// C1c:Windows 版 <c>Services\UiExt.cs</c>(WPF)到 Avalonia 11 的移植。链式小工具集,
/// 方法名、参数顺序与默认值、返回类型(<c>T</c> 自身)全部照抄;只有**框架类型**换了:
/// <c>FrameworkElement</c>/<c>UIElement</c> → <c>Control</c>,
/// <c>DependencyProperty</c> → <c>AvaloniaProperty</c>,
/// <c>Visibility</c> → <c>IsVisible</c>(Avalonia 11 没有 Visibility 属性),
/// <c>ToolTip</c> 属性 → <c>ToolTip.SetTip</c> 附加属性。
///
/// <see cref="Dyn{T}"/>(动态资源)在 Avalonia 侧用
/// <c>Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension</c> —— 它就是 WPF
/// <c>SetResourceReference</c> 的等价物,所以主题变体切换时颜色照样会重取。
/// </summary>
public static class UiExt
{
    /// <summary>把属性绑到色板/令牌键上(WPF 的 <c>SetResourceReference</c>)。</summary>
    public static T Dyn<T>(this T el, AvaloniaProperty p, string key) where T : Control
    {
        el[!p] = new DynamicResourceExtension(key);
        return el;
    }

    public static T M<T>(this T el, double l, double t = 0.0, double r = 0.0, double b = 0.0) where T : Control
    {
        el.Margin = new Thickness(l, t, r, b);
        return el;
    }

    public static T M<T>(this T el, double all) where T : Control
    {
        el.Margin = new Thickness(all);
        return el;
    }

    public static T W<T>(this T el, double w) where T : Control
    {
        el.Width = w;
        return el;
    }

    public static T MinW<T>(this T el, double w) where T : Control
    {
        el.MinWidth = w;
        return el;
    }

    public static T Hh<T>(this T el, double h) where T : Control
    {
        el.Height = h;
        return el;
    }

    public static T At<T>(this T el, int row, int col = 0, int rowSpan = 1, int colSpan = 1) where T : Control
    {
        Grid.SetRow(el, row);
        Grid.SetColumn(el, col);
        if (rowSpan > 1)
        {
            Grid.SetRowSpan(el, rowSpan);
        }

        if (colSpan > 1)
        {
            Grid.SetColumnSpan(el, colSpan);
        }

        return el;
    }

    public static Grid Add(this Grid g, Control el, int row, int col = 0, int rowSpan = 1, int colSpan = 1)
    {
        el.At(row, col, rowSpan, colSpan);
        g.Children.Add(el);
        return g;
    }

    public static T Left<T>(this T el) where T : Control
    {
        el.HorizontalAlignment = HorizontalAlignment.Left;
        return el;
    }

    public static T Right<T>(this T el) where T : Control
    {
        el.HorizontalAlignment = HorizontalAlignment.Right;
        return el;
    }

    public static T Center<T>(this T el) where T : Control
    {
        el.HorizontalAlignment = HorizontalAlignment.Center;
        return el;
    }

    public static T Stretch<T>(this T el) where T : Control
    {
        el.HorizontalAlignment = HorizontalAlignment.Stretch;
        return el;
    }

    public static T VCenter<T>(this T el) where T : Control
    {
        el.VerticalAlignment = VerticalAlignment.Center;
        return el;
    }

    public static T VTop<T>(this T el) where T : Control
    {
        el.VerticalAlignment = VerticalAlignment.Top;
        return el;
    }

    public static T Wrap<T>(this T el) where T : TextBlock
    {
        el.TextWrapping = TextWrapping.Wrap;
        return el;
    }

    public static T Trim<T>(this T el) where T : TextBlock
    {
        el.TextTrimming = TextTrimming.CharacterEllipsis;
        return el;
    }

    public static T Tip<T>(this T el, string tip) where T : Control
    {
        ToolTip.SetTip(el, tip);
        return el;
    }

    public static T Tag2<T>(this T el, object tag) where T : Control
    {
        el.Tag = tag;
        return el;
    }

    /// <summary>WPF 的 <c>Visibility.Collapsed</c> 在 Avalonia 里就是 <c>IsVisible = false</c>。</summary>
    public static T Hide<T>(this T el, bool hidden = true) where T : Control
    {
        el.IsVisible = !hidden;
        return el;
    }

    public static T Show<T>(this T el, bool shown = true) where T : Control
    {
        el.IsVisible = shown;
        return el;
    }

    public static bool Shown(this Control el)
    {
        return el.IsVisible;
    }

    public static string Str(this ComboBox c)
    {
        return (c.SelectedItem as string) ?? "";
    }

    public static void Fill(this ComboBox c, IEnumerable<string> items, string? keep = null)
    {
        // 先快照再 Clear:items 若是 c.Items 的活视图(如 _inst.Items.Cast<string>()),
        // 迭代中 Clear 会丢内容;PlaytimePage:139 等处同样受益
        var list = (items == null) ? new List<string>() : new List<string>(items);
        string? text = keep ?? (c.SelectedItem as string);
        c.Items.Clear();
        foreach (string item in list)
        {
            c.Items.Add(item);
        }

        if (text != null && c.Items.Contains(text))
        {
            c.SelectedItem = text;
        }
        else if (c.Items.Count > 0)
        {
            c.SelectedIndex = 0;
        }
    }
}
