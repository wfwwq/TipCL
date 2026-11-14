// 设置页内部小扩展（M2-B5）：对齐 Windows 版 UiExt 的常用链式调用，
// 只在 Pages.Settings 命名空间内可见，不动 A2 的 UiHelpers。
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TipCL.App.Pages.Settings;

internal static class SettingsExt
{
    public static T M<T>(this T c, double l, double t, double r, double b) where T : Control
    { c.Margin = new Thickness(l, t, r, b); return c; }

    public static T VCenter<T>(this T c) where T : Control
    { c.VerticalAlignment = VerticalAlignment.Center; return c; }

    public static T HCenter<T>(this T c) where T : Control
    { c.HorizontalAlignment = HorizontalAlignment.Center; return c; }

    public static T Center<T>(this T c) where T : Control
    { c.HorizontalAlignment = HorizontalAlignment.Center; c.VerticalAlignment = VerticalAlignment.Center; return c; }

    public static T Left<T>(this T c) where T : Control
    { c.HorizontalAlignment = HorizontalAlignment.Left; return c; }

    public static T Right<T>(this T c) where T : Control
    { c.HorizontalAlignment = HorizontalAlignment.Right; return c; }

    public static TextBlock Wrap(this TextBlock t)
    { t.TextWrapping = TextWrapping.Wrap; return t; }
}
