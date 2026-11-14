using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TipCL.App.Ui;

namespace TipCL.App.Common;

/// <summary>
/// 方形缩略图块(对齐 Windows 版 Services/ThumbTile):浅底方块 + 首字母兜底,远程图到达后换图。
/// 目录页结果卡左侧用它显示项目图标。
/// </summary>
internal sealed class ThumbTile : Border
{
    private readonly TextBlock _initial;

    private readonly Image _image;

    private string _current = "";

    private int _gen;

    public ThumbTile(string initial, double size = 46, double radius = 8)
    {
        Width = size;
        Height = size;
        CornerRadius = new CornerRadius(radius);
        ClipToBounds = true;
        BorderThickness = new Thickness(1);
        this.Dyn(BackgroundProperty, "B.Paper2");
        this.Dyn(BorderBrushProperty, "B.Line");
        _initial = new TextBlock
        {
            Text = string.IsNullOrEmpty(initial) ? "?" : initial[..1].ToUpperInvariant(),
            FontSize = Math.Max(12, size * 0.4),
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _initial.Dyn(TextBlock.ForegroundProperty, "B.InkMuted");
        _image = new Image { Stretch = Stretch.UniformToFill, IsVisible = false };
        var panel = new Panel();
        panel.Children.Add(_initial);
        panel.Children.Add(_image);
        Child = panel;
    }

    /// <summary>设置远程图标地址;空串即回落首字母。</summary>
    public void SetUrl(string? url)
    {
        if (string.Equals(_current, url ?? "", StringComparison.Ordinal))
            return;
        _current = url ?? "";
        int gen = ++_gen;
        _image.IsVisible = false;
        _image.Source = null;
        _initial.IsVisible = true;
        if (_current.Length == 0)
            return;
        _ = LoadAsync(gen, _current);
    }

    private async System.Threading.Tasks.Task LoadAsync(int gen, string url)
    {
        Bitmap? bmp = await Thumbs.LoadAsync(url, (int)(Width * 2));
        if (gen != _gen || bmp == null)
            return;
        _image.Source = bmp;
        _image.IsVisible = true;
        _initial.IsVisible = false;
    }
}
