using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TipCL.App.Ui;

namespace TipCL.App.Common;

/// <summary>
/// 圆形头像块(对齐 Windows 版 Services/ThumbTile):主色圆 + 首字母兜底,远程图到达后换成图片。
/// </summary>
internal sealed class AvatarTile : Border
{
    private readonly TextBlock _initial;

    private readonly Image _image;

    private string _current = "";

    private int _gen;

    public AvatarTile(string initial, double size = 40, double radius = 20)
    {
        Width = size;
        Height = size;
        CornerRadius = new CornerRadius(radius);
        ClipToBounds = true;
        _initial = new TextBlock
        {
            Text = string.IsNullOrEmpty(initial) ? "?" : initial[..1].ToUpperInvariant(),
            FontSize = Math.Max(11, size * 0.42),
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _initial.Dyn(TextBlock.ForegroundProperty, "B.OnAccentStrong");
        _image = new Image { Stretch = Stretch.UniformToFill, IsVisible = false };
        var panel = new Panel();
        panel.Children.Add(_initial);
        panel.Children.Add(_image);
        Child = panel;
        this.Dyn(BackgroundProperty, "B.Accent");
    }

    /// <summary>设置远程头像地址;空串即回落首字母。</summary>
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

    /// <summary>直接给位图(自定义皮肤本地渲染用)。</summary>
    public void SetBitmap(Bitmap? bmp)
    {
        _gen++;
        _image.Source = bmp;
        _image.IsVisible = bmp != null;
        _initial.IsVisible = bmp == null;
    }

    private async System.Threading.Tasks.Task LoadAsync(int gen, string url)
    {
        var bmp = await Thumbs.LoadAsync(url, (int)(Width * 2));
        if (gen != _gen || bmp == null)
            return;
        _image.Source = bmp;
        _image.IsVisible = true;
        _initial.IsVisible = false;
    }
}
