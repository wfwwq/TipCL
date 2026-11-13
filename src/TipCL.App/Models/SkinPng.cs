using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TipCL.Services;

namespace TipCL.Models;

/// <summary>
/// C5b:Windows 版 <c>Pages\SkinPng.cs</c>(3878 字节 / 146 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// 「把一张皮肤 PNG 拼成正面全身像」。
///
/// <b>这不是字节级搬(实测前提不成立,如实记录)</b>:Windows 那一份整个建立在 WPF 的成像 API 上
/// (<c>DrawingVisual</c> / <c>DrawingContext.DrawImage</c> / <c>CroppedBitmap</c> / <c>RenderTargetBitmap</c> /
/// <c>BitmapImage.CacheOption</c>),Avalonia 侧一个同名等价物都没有,照抄一个字都编不过。
/// 所以这里保留的是**算法与全部常量**,画法换掉:
/// <list type="bullet">
/// <item>解码走 mac 既有的 <see cref="ImageDecode"/>(注入缝 + Core 的 Skia 编解码器),不是 <c>BitmapImage</c>;</item>
/// <item><c>Load</c> 的 <c>PixelWidth &lt; 64 → null</c> 判据逐字保留;</item>
/// <item>12 个部件的 <c>(sx, sy, sw, sh, dx, dy, mirror)</c> 与顺序逐字保留(逻辑画布 16x32);</item>
/// <item>slim 的 3 像素臂宽 / 64x32 旧皮肤的 <c>height*2 &lt;= width</c> 分支逐字保留;</item>
/// <item>裁切:(Windows 是 <c>CroppedBitmap</c> 后 <c>DrawImage</c>)→ 这里用"整图放大后放进一个
///   <c>ClipToBounds</c> 的 <c>Canvas</c> 里、再平移到部件位置",数学上等价,且不需要像素级 API;</item>
/// <item>镜像:<c>ScaleTransform(-1, 1, dx + sw/2, 0)</c> → <c>ScaleTransform(-1, 1)</c> +
///   <c>RenderTransformOrigin = (0.5, 0)</c>(绕自身的竖中线翻,与 Windows 绕部件中心翻等价);</item>
/// <item>放大:<c>BitmapScalingMode.NearestNeighbor</c> → <c>BitmapInterpolationMode.None</c>;</item>
/// <item>拍图:<c>RenderTargetBitmap(128, 256, 96, 96, Pbgra32)</c> → <c>RenderTargetBitmap(128x256, 96dpi)</c>
///   + 手动 <c>Measure</c>/<c>Arrange</c>(Avalonia 渲染前必须先量好)。</item>
/// </list>
/// 任何一步失败都**返回 null**(与 Windows 的 <c>catch { return null; }</c> 同一结果:界面回落成首字母方块,
/// 不把异常抛进界面)。
/// </summary>
internal static class SkinPng
{
	/// <summary>【Windows 源】<c>:11-26</c>:data URL 取出 base64 再解;解不动返回 null。</summary>
	public static byte[]? Decode(string? dataUrl)
	{
		if (string.IsNullOrWhiteSpace(dataUrl))
		{
			return null;
		}

		string base64 = (dataUrl.StartsWith("data:", StringComparison.Ordinal) ? dataUrl.Substring(dataUrl.IndexOf(',') + 1) : dataUrl);
		try
		{
			return Convert.FromBase64String(base64);
		}
		catch (FormatException)
		{
			return null;
		}
	}

	/// <summary>
	/// 【Windows 源】<c>:28-114</c>:按皮肤 PNG 拼出一张 128x256 的正面全身像;
	/// 认不出来 / 小于 64 像素 / 拍不出来都是 null。
	/// </summary>
	public static Bitmap? FrontView(byte[] png, bool slim)
	{
		Bitmap? src = ImageDecode.Decode(png, 0);
		if (src == null)
		{
			return null;
		}

		try
		{
			return Compose(src, slim);
		}
		catch (Exception)
		{
			// 【Windows 源】:与 Load / 拍图失败同口径 —— null,不抛给界面
			return null;
		}
		finally
		{
			// 部件已经画进 RenderTargetBitmap,源图与裁切壳都可以收掉了
			src.Dispose();
		}
	}

	private static Bitmap? Compose(Bitmap src, bool slim)
	{
		PixelSize size = src.PixelSize;
		// 【Windows 源】:Load 里 PixelWidth < 64 直接算认不出来
		if (size.Width < 64)
		{
			return null;
		}

		double scale = size.Width / 64.0;
		if (scale <= 0.0)
		{
			return null;
		}

		// 【Windows 源】:height*2 <= width ⇒ 64x32 的旧格式皮肤(手臂/腿的排布分支不同)
		bool legacy = size.Height * 2 <= size.Width;
		int arm = (slim ? 3 : 4);
		Canvas canvas = new Canvas { Width = 128.0, Height = 256.0, Background = Brushes.Transparent };
		// 【Windows 源】:Part(...) —— 局部函数名与参数名照抄,位置常量一个不改
		void Part(int sx, int sy, int sw, int sh, double dx, double dy, bool mirror = false)
		{
			int rx = (int)((double)sx * scale);
			int ry = (int)((double)sy * scale);
			int rw = (int)((double)sw * scale);
			int rh = (int)((double)sh * scale);
			// 【Windows 源】:Crop 的越界判据 —— 越界就整块跳过(不是抛)
			if (rx < 0 || ry < 0 || rw <= 0 || rh <= 0 || rx + rw > size.Width || ry + rh > size.Height)
			{
				return;
			}

			// 逻辑坐标 → 8 倍像素:整张皮肤按逻辑尺寸铺开,部件用裁切框取出来
			Image whole = new Image
			{
				Source = src,
				Stretch = Stretch.Fill,
				Width = (double)size.Width / scale * 8.0,
				Height = (double)size.Height / scale * 8.0,
			};
			// 【Windows 源】:DrawImage 的最近邻(BitmapScalingMode.NearestNeighbor ⇒ None)
			RenderOptions.SetBitmapInterpolationMode(whole, BitmapInterpolationMode.None);
			Canvas clip = new Canvas
			{
				Width = sw * 8.0,
				Height = sh * 8.0,
				ClipToBounds = true,
				Children = { whole },
			};
			Canvas.SetLeft(whole, -sx * 8.0);
			Canvas.SetTop(whole, -sy * 8.0);
			if (mirror)
			{
				// 【Windows 源】:ScaleTransform(-1, 1, dx + sw/2, 0) —— 绕部件自己的竖中线翻
				clip.RenderTransform = new ScaleTransform(-1.0, 1.0);
				clip.RenderTransformOrigin = new RelativePoint(0.5, 0.0, RelativeUnit.Relative);
			}

			Canvas.SetLeft(clip, dx * 8.0);
			Canvas.SetTop(clip, dy * 8.0);
			canvas.Children.Add(clip);
		}

		// 【Windows 源】:12 个部件的顺序与常量逐字照抄
		Part(8, 8, 8, 8, 4.0, 0.0);
		Part(20, 20, 8, 12, 4.0, 8.0);
		Part(44, 20, arm, 12, 4 - arm, 8.0);
		if (legacy)
		{
			Part(44, 20, arm, 12, 12.0, 8.0, mirror: true);
		}
		else
		{
			Part(36, 52, arm, 12, 12.0, 8.0);
		}

		Part(4, 20, 4, 12, 4.0, 20.0);
		if (legacy)
		{
			Part(4, 20, 4, 12, 8.0, 20.0, mirror: true);
		}
		else
		{
			Part(20, 52, 4, 12, 8.0, 20.0);
		}

		Part(40, 8, 8, 8, 4.0, 0.0);
		if (!legacy)
		{
			Part(20, 36, 8, 12, 4.0, 8.0);
			Part(44, 36, arm, 12, 4 - arm, 8.0);
			Part(52, 52, arm, 12, 12.0, 8.0);
			Part(4, 36, 4, 12, 4.0, 20.0);
			Part(4, 52, 4, 12, 8.0, 20.0);
		}

		// 【Windows 源】:RenderTargetBitmap(128, 256, 96, 96) —— 输出尺寸与 dpi 一字不改。
		// Avalonia 渲染前必须先量好并排好版,否则拍出来是空白。
		canvas.Measure(new Size(128.0, 256.0));
		canvas.Arrange(new Rect(0.0, 0.0, 128.0, 256.0));
		RenderTargetBitmap frame = new RenderTargetBitmap(new PixelSize(128, 256), new Vector(96.0, 96.0));
		frame.Render(canvas);
		return frame;
	}
}
