using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace TipCL.Services;

/// <summary>
/// 一块方形拇指瓦片(= Windows <c>Services\ThumbTile.cs</c>):一个圆角方块,有图就显示图,
/// 没有图就显示名字首字母方块。公开面与 Windows 一致:
/// <c>ThumbTile(string text, double size = 42.0, double radius = 10.0)</c> / <c>SetUrl(string?)</c> /
/// <c>SetImage(Bitmap?)</c>。
///
/// 逐项照 Windows 抄下来的取值:圆角 = radius、<c>ClipToBounds</c>、底色 <c>B.AccentSoft</c>、
/// 首字母字号 <c>max(12, size*0.4)</c> 且加粗、颜色 <c>B.AccentDeep</c>;
/// 取图宽度 <c>max(16, Width)</c>;命中缓存直接显示,没命中先显示首字母再异步取;
/// 取回来之后再对一次 token(过期请求直接丢),显示时 180ms 淡入。
///
/// mac 侧的差异:
/// <list type="number">
/// <item><c>SetImage</c> 在 Windows 是 <c>#if PREMIUM</c> 才有(compact 版走 <c>SetUrl</c> 下载);
///   GOAL 定了 mac 版"无体积红线,等同 premium",所以这个成员**恒在**;</item>
/// <item><c>BitmapSource</c> → <see cref="Bitmap"/>;<c>Dispatcher.InvokeAsync</c> →
///   <c>Dispatcher.UIThread.CheckAccess()</c> + <c>Post</c>(同一件事,少一层 await);</item>
/// <item>暴露了一组只读成员给测试读状态(有没有显示图 / 首字母文本),行为一字未改。</item>
/// </list>
/// </summary>
public sealed class ThumbTile : Border
{
	private readonly Image _img = new Image
	{
		Stretch = Stretch.UniformToFill
	};

	private readonly TextBlock _letter;

	private int _token;

	public ThumbTile(string text, double size = 42.0, double radius = 10.0)
	{
		Width = size;
		Height = size;
		CornerRadius = new Avalonia.CornerRadius(radius);
		ClipToBounds = true;
		this.Dyn(Border.BackgroundProperty, "B.AccentSoft");
		_letter = Ui.Txt(string.IsNullOrEmpty(text) ? "?" : text.Substring(0, 1).ToUpperInvariant(), Math.Max(12.0, size * 0.4), bold: true, "B.AccentDeep").Center();
		_letter.VerticalAlignment = VerticalAlignment.Center;
		Child = new Grid
		{
			Children =
			{
				_letter,
				_img
			}
		};
		_img.IsVisible = false;
	}

	/// <summary>图片层(测试断言"显示的是图还是首字母")。</summary>
	internal Image ImageLayer => _img;

	/// <summary>首字母层。</summary>
	internal TextBlock Letter => _letter;

	/// <summary>现在显示的是图吗。</summary>
	internal bool ImageVisible => _img.IsVisible;

	/// <summary>现在显示的是首字母吗。</summary>
	internal bool LetterVisible => _letter.IsVisible;

	/// <summary>图片层当前的位图(没有就是 null)。</summary>
	internal Bitmap? ImageSource => _img.Source as Bitmap;

	/// <summary>已经排到第几号请求(过期请求靠它丢掉)。</summary>
	internal int Token => _token;

	/// <summary>给一张 URL(命中缓存立刻显示,否则先回落首字母再异步取)。</summary>
	public void SetUrl(string? url)
	{
		int token = ++_token;
		int px = (int)Math.Max(16.0, Width);
		if (string.IsNullOrWhiteSpace(url))
		{
			Show(null);
			return;
		}

		Bitmap? cached = Thumbs.Cached(url, px);
		if (cached is not null)
		{
			Show(cached);
			return;
		}

		Show(null);
		_ = LoadAsync(url, px, token);
	}

	/// <summary>直接给一张已解好的位图(LoaderIcons 用;premium 版才有,见类注释差异 1)。</summary>
	public void SetImage(Bitmap? bmp)
	{
		_token++;
		Show(bmp);
	}

	private async Task LoadAsync(string url, int px, int token)
	{
		Bitmap? bmp = await Thumbs.LoadAsync(url, px);
		if (bmp is null || token != _token)
		{
			return;
		}

		if (Dispatcher.UIThread.CheckAccess())
		{
			Apply(bmp, token);
			return;
		}

		Dispatcher.UIThread.Post(delegate
		{
			Apply(bmp, token);
		});
	}

	private void Apply(Bitmap bmp, int token)
	{
		if (token == _token)
		{
			Show(bmp);
			Motion.Fade(_img, 1.0, 180.0);
		}
	}

	private void Show(Bitmap? bmp)
	{
		_img.Source = bmp;
		_img.Opacity = ((bmp is not null) ? 1 : 0);
		_img.IsVisible = bmp is not null;
		_letter.IsVisible = bmp is null;
	}
}
