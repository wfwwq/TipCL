// 来源: Windows 版参考仓库 Services/Ui.cs 的扩展方法组（M2-B4 按 Avalonia 语义补齐；
// M1-A2 的 UiHelpers 只搬了工厂方法，这些链式扩展 Windows 版插件/设置页大量使用）。
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TipCL.App.Pages.Layout;

/// <summary>控件链式小扩展：对齐 Windows 版 Ui.M / VCenter / Center / Wrap / Trim / Tip。</summary>
public static class UiExt
{
    public static T Mk<T>(this T c, double l, double t, double r, double b) where T : Control
    {
        c.Margin = new Thickness(l, t, r, b);
        return c;
    }

    public static T VCenter<T>(this T c) where T : Control
    {
        c.VerticalAlignment = VerticalAlignment.Center;
        return c;
    }

    public static T HCenter<T>(this T c) where T : Control
    {
        c.HorizontalAlignment = HorizontalAlignment.Center;
        return c;
    }

    public static T Center<T>(this T c) where T : Control
    {
        c.HorizontalAlignment = HorizontalAlignment.Center;
        c.VerticalAlignment = VerticalAlignment.Center;
        return c;
    }

    public static T VTop<T>(this T c) where T : Control
    {
        c.VerticalAlignment = VerticalAlignment.Top;
        return c;
    }

    public static TextBlock Wrap(this TextBlock t)
    {
        t.TextWrapping = TextWrapping.Wrap;
        return t;
    }

    public static TextBlock Trim(this TextBlock t)
    {
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        return t;
    }

    public static T Tip<T>(this T c, object? value) where T : Control
    {
        ToolTip.SetTip(c, value);
        return c;
    }
}
