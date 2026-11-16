// R32-SCROLLBAR-FIX:滚动条滑块「被裁到 1px」保真缺陷的回归测试(mac/Avalonia 树)。
//
// 缺陷(实测,见 progress/R32-SCROLLBAR.md / R32-SCROLLBAR-ACCEPT.md / R32-SCROLLBAR-FIX.md):
//   settings 页主体 = 原生 ScrollViewer(VerticalScrollBarVisibility=Auto),内容 3702px > 视口 478px;
//   本 App 把条宽压到 10px(Controls.axaml 的 ScrollBar:vertical),而 Avalonia 11.3 Fluent 主题给
//   ScrollBar 的 Thumb 的模板值是「布局宽 16 + RenderTransform=scale(0.125,1) + 原点 100%,50%」
//   ⇒ 绘制宽 2.0px 的滑块被"右缘锚定"推到条外(绘制框 x=[898,900) vs 条 [889,899))⇒ **只剩 1 列可见**。
//   Windows 冻结件(windos/src/TipCL/Themes/Controls.xaml:85-99)是「Thumb 占满 10px 轨道 +
//   模板里的 Border 写 Margin=3,2」⇒ 10px 轨道里可见 4px。
//
// 修复口径(只改主题层,不动轨道宽 10 这个既定值):
//   1. ScrollBar:vertical /template/ Thumb → Width=MinWidth=MaxWidth=10、scale(1,1)、原点 50%,50%
//      (必须带 :vertical 伪类选择器才压得住 Fluent 的模板值 —— 普通 ScrollBar /template/ Thumb 实测无效);
//   2. ScrollBar:vertical /template/ Thumb /template/ Border → Margin=3,2(与 Windows 逐字同口径);
//   3. 悬停/按下色 → 模板里那个 Border 的 Background 换成 B.ScrollHover
//      (Fluent 的悬停色是 #66000000 那类半透明填充,混出来是 #939393 / #757575,不是 App 的语义色)。
//
// 三条用例各自都能因「把上面这三处改回旧样式」而变红(三连证据见 R32-SCROLLBAR-FIX.md §三连证伪)。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TipCL.Smoke;
using Xunit;

namespace TipCL.Tests;

public class R32ScrollbarFixTests
{
	/// <summary>轨道宽(App 的既定值,本次修复**不动**它)。</summary>
	private const double TrackWidth = 10.0;

	/// <summary>Windows 口径下的可见滑块厚度:10 - 3 - 3 = 4。</summary>
	private const double VisibleThumbWidth = 4.0;

	/// <summary>Windows 的 ScrollThumb 模板在 Border 上写的纵向让边(Margin="3,2" 里的 2 → 上下共 4)。</summary>
	private const int ThumbLengthInset = 4;

	// ───────────────────────────── 无头会话夹具 ─────────────────────────────

	/// <summary>建一个离屏 settings 窗口并把它布局/渲染跑完(与 --smoke、探针同一条无头路径,不建 OS 窗口)。</summary>
	private static MainWindow SettingsWindow(ThemeVariant variant, double width, double height)
	{
		if (Application.Current is { } app)
		{
			app.RequestedThemeVariant = variant;
		}

		var window = new MainWindow(new UiPrefs { Anim = false })
		{
			RequestedThemeVariant = variant,
			PageId = "settings",
		};
		window.Width = width;
		window.Height = height;
		window.Show();
		window.Navigate("settings", instant: true);
		window.ApplyThemeVisuals();
		Ticks(3);
		using (var warm = window.CaptureRenderedFrame())
		{
			// 逼一次完整布局/渲染(第一帧可能还没重合成)
		}
		Ticks(2);
		return window;
	}

	private static void Ticks(int n)
	{
		for (int i = 0; i < n; i++)
		{
			AvaloniaHeadlessPlatform.ForceRenderTimerTick();
		}
	}

	/// <summary>settings 页主体那条竖滚动条 + 它的滑块 + 滑块模板里那个 Border(真正画出来的东西)。</summary>
	private static (ScrollBar Bar, Thumb Thumb, Border Painted) VerticalBar(Window window)
	{
		ScrollBar bar = window.GetVisualDescendants().OfType<ScrollBar>()
			.Where(s => s.IsVisible && s.Orientation == Orientation.Vertical && s.Bounds.Height > 100)
			.OrderByDescending(s => s.Bounds.Height)
			.First();
		Thumb thumb = bar.GetVisualDescendants().OfType<Thumb>().First();
		Border painted = thumb.GetVisualDescendants().OfType<Border>().First();
		return (bar, thumb, painted);
	}

	/// <summary>某个视觉(含 RenderTransform)画到窗口坐标里的矩形。</summary>
	private static Rect PaintedRect(Window window, Visual visual)
	{
		Point p0 = visual.TranslatePoint(new Point(0, 0), window)!.Value;
		Point p1 = visual.TranslatePoint(new Point(visual.Bounds.Width, visual.Bounds.Height), window)!.Value;
		return new Rect(Math.Min(p0.X, p1.X), Math.Min(p0.Y, p1.Y),
			Math.Abs(p1.X - p0.X), Math.Abs(p1.Y - p0.Y));
	}

	private static Rect BarRect(Window window, ScrollBar bar)
	{
		Point p = bar.TranslatePoint(new Point(0, 0), window)!.Value;
		return new Rect(p.X, p.Y, bar.Bounds.Width, bar.Bounds.Height);
	}

	/// <summary>滑块**可见**矩形 = 画出来的矩形 ∩ 滚动条自身(子元素被条边界裁剪)。</summary>
	private static Rect VisibleThumbRect(Window window, ScrollBar bar, Visual painted)
	{
		Rect a = PaintedRect(window, painted);
		Rect b = BarRect(window, bar);
		double x0 = Math.Max(a.X, b.X), x1 = Math.Min(a.Right, b.Right);
		double y0 = Math.Max(a.Y, b.Y), y1 = Math.Min(a.Bottom, b.Bottom);
		return new Rect(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
	}

	/// <summary>渲染帧里该列的像素(R 分量);<paramref name="x"/> 是窗口坐标列号。</summary>
	private static byte[] ColumnPixels(WriteableBitmap frame, int x, int y0, int y1)
	{
		using ILockedFramebuffer fb = frame.Lock();
		int rowBytes = fb.RowBytes;
		bool bgra = fb.Format != PixelFormat.Rgba8888;
		var result = new byte[Math.Max(0, y1 - y0 + 1)];
		byte[] one = new byte[4];
		for (int y = y0; y <= y1; y++)
		{
			Marshal.Copy(IntPtr.Add(fb.Address, y * rowBytes + x * 4), one, 0, 4);
			result[y - y0] = bgra ? one[2] : one[0];
		}
		return result;
	}

	/// <summary>
	/// 一列里「滑块像素」的行数与颜色:先取本列众数当背景(纸面 / 页面底色),再把偏离背景 &gt;6 的行算作滑块,
	/// 这些行自身的众数就是滑块色。口径与 R32-SCROLLBAR-FIX.md 的逐列像素扫描逐字相同。
	/// </summary>
	private static (int Rows, int Fg) ThumbInColumn(WriteableBitmap frame, int x, Rect barRect)
	{
		byte[] px = ColumnPixels(frame, x, (int)Math.Floor(barRect.Y), (int)Math.Ceiling(barRect.Bottom) - 1);
		var hist = new Dictionary<int, int>();
		foreach (byte b in px)
		{
			hist[b] = hist.TryGetValue(b, out int c) ? c + 1 : c;
		}
		int bg = hist.OrderByDescending(kv => kv.Value).First().Key;
		var fg = new Dictionary<int, int>();
		int rows = 0;
		foreach (byte b in px)
		{
			if (Math.Abs(b - bg) > 6)
			{
				rows++;
				fg[b] = fg.TryGetValue(b, out int c) ? c + 1 : c;
			}
		}
		return (rows, fg.Count == 0 ? -1 : fg.OrderByDescending(kv => kv.Value).First().Key);
	}

	/// <summary>条内「可见列 → (行数, 前景色)」;只留行数 &gt;= minRows 的列。</summary>
	private static SortedDictionary<int, (int Rows, int Fg)> ThumbColumns(WriteableBitmap frame, Rect barRect, int minRows = 10)
	{
		var result = new SortedDictionary<int, (int Rows, int Fg)>();
		int x0 = (int)Math.Floor(barRect.X);
		int x1 = (int)Math.Ceiling(barRect.Right) - 1;
		for (int x = x0; x <= x1; x++)
		{
			(int rows, int fg) = ThumbInColumn(frame, x, barRect);
			if (rows >= minRows)
			{
				result[x] = (rows, fg);
			}
		}
		return result;
	}

	// ───────────────────────────── 1. 可见宽度 = 4px ─────────────────────────────

	/// <summary>
	/// 10px 轨道里的滑块必须**完整可见**且是 Windows 的 4px 厚(不是修复前的 1px,也不是 Fluent 画出来的 2.0px);
	/// 同时钉住轨道宽 10 这个既定值没被改动。
	/// </summary>
	[Fact]
	public void Settings_scrollbar_thumb_is_visible_4px_thick_inside_the_10px_track()
	{
		AvaloniaSession.Run(() =>
		{
			using var smoke = SmokeState.Activate();
			using var scope = C3cScope.Capture();
			Window window = SettingsWindow(ThemeVariant.Light, 900, 550);
			try
			{
				(ScrollBar bar, Thumb thumb, Border painted) = VerticalBar(window);

				// 轨道:既定 10px(修复不许改这个值)
				Assert.Equal(10.0, bar.Bounds.Width);
				Assert.Equal(TrackWidth, bar.Width);
				Assert.Equal(TrackWidth, bar.MinWidth);

				// 滑块布局:占满轨道(Windows 的 Thumb 同样占满轨道,4px 是 Border 的 Margin 收出来的)
				Assert.Equal(TrackWidth, thumb.Bounds.Width);
				Assert.Equal(TrackWidth, thumb.Width);

				// Fluent 的 scale(0.125,1) + 原点 100%,50% 必须被压掉(否则绘制宽只有 2.0,还会被推到条外)
				Matrix m = thumb.RenderTransform?.Value ?? Matrix.Identity;
				Assert.Equal(1.0, m.M11, 6);
				Assert.Equal(1.0, m.M22, 6);
				Assert.Equal(0.0, m.M31, 6);

				// 画出来的(模板里那个 Border)∩ 条 = 可见滑块:4.0 宽,不是 1.0(修前)/2.0(未压掉缩放)
				Rect barRect = BarRect(window, bar);
				Rect visible = VisibleThumbRect(window, bar, painted);
				Assert.Equal(VisibleThumbWidth, visible.Width, 3);
				Assert.Equal(3.0, visible.X - barRect.X, 3);      // 左让 3
				Assert.Equal(3.0, barRect.Right - visible.Right, 3); // 右让 3

				// 像素级:渲染帧里恰有 4 列滑块色,落在条内偏移 3..6(修前只有 1 列,在偏移 9)
				using WriteableBitmap frame = window.CaptureRenderedFrame()!;
				SortedDictionary<int, (int Rows, int Fg)> cols = ThumbColumns(frame, barRect);
				int barLeft = (int)Math.Round(barRect.X);
				Assert.Equal(new[] { barLeft + 3, barLeft + 4, barLeft + 5, barLeft + 6 }, cols.Keys.ToArray());
				Assert.All(cols.Values, v => Assert.Equal(0xCF, v.Fg));   // B.Scroll(浅色 #CFCFCF)
			}
			finally
			{
				window.Close();
			}
		});
	}

	// ───────────────────────────── 2. 悬停色 = B.ScrollHover ─────────────────────────────

	/// <summary>
	/// 悬停时滑块改用 Windows 那套 B.ScrollHover(#B0B0B0 浅 / #555555 深),而不是 Fluent 自带那层
	/// 半透明填充(浅 #66000000 混出 #939393、深 #40FFFFFF 混出 #757575)。
	/// </summary>
	[Fact]
	public void Settings_scrollbar_thumb_hover_uses_the_windows_hover_color()
	{
		AvaloniaSession.Run(() =>
		{
			using var smoke = SmokeState.Activate();
			using var scope = C3cScope.Capture();
			Check(ThemeVariant.Light, 0xB0);
			Check(ThemeVariant.Dark, 0x55);

			void Check(ThemeVariant variant, byte expectedR)
			{
				Window window = SettingsWindow(variant, 900, 550);
				try
				{
					(ScrollBar bar, Thumb thumb, Border painted) = VerticalBar(window);
					Rect barRect = BarRect(window, bar);
					Rect visible = VisibleThumbRect(window, bar, painted);
					window.MouseMove(new Point(visible.X + visible.Width / 2, visible.Y + visible.Height / 2));
					Ticks(20);

					// 属性层:滑块模板里那个 Border 的 Background 就是 B.ScrollHover(不透明)
					var brush = Assert.IsType<SolidColorBrush>(painted.Background);
					Assert.Equal(byte.MaxValue, brush.Color.A);
					Assert.Equal(expectedR, brush.Color.R);
					Assert.Equal(expectedR, brush.Color.G);
					Assert.Equal(expectedR, brush.Color.B);

					// 像素层:可见的 4 列都变成悬停色
					using WriteableBitmap frame = window.CaptureRenderedFrame()!;
					SortedDictionary<int, (int Rows, int Fg)> cols = ThumbColumns(frame, barRect);
					int barLeft = (int)Math.Round(barRect.X);
					Assert.Equal(new[] { barLeft + 3, barLeft + 4, barLeft + 5, barLeft + 6 }, cols.Keys.ToArray());
					Assert.All(cols.Values, v => Assert.Equal(expectedR, v.Fg));
				}
				finally
				{
					window.Close();
				}
			}
		});
	}

	// ───────────────────────────── 3. 长度 = ceil 比例律 ─────────────────────────────

	/// <summary>
	/// 滑块长度由 视口/内容 比例决定:`槽高 = ceil(轨道高 × 视口高 / 内容高)`;
	/// Windows 口径下**可见**高度还要再让掉上下各 2(Margin="3,2")= 槽高 - 4。
	/// 两个窗高各验一次(900×550 → 58/54;900×750 → 119/115),证明是比例律而不是常数。
	///
	/// 口径来源:比例律是 **ceil**(不是 round)—— 独立验收人实测 6/6 命中 ceil、
	/// round 在其中 4 个点算错(见 progress/R32-SCROLLBAR-ACCEPT.md 的纠正)。
	/// </summary>
	[Fact]
	public void Settings_scrollbar_thumb_length_follows_the_ceil_ratio_law()
	{
		AvaloniaSession.Run(() =>
		{
			using var smoke = SmokeState.Activate();
			using var scope = C3cScope.Capture();
			Check(900, 550, slot: 58);
			Check(900, 750, slot: 119);

			void Check(double width, double height, int slot)
			{
				Window window = SettingsWindow(ThemeVariant.Light, width, height);
				try
				{
					(ScrollBar bar, Thumb thumb, Border painted) = VerticalBar(window);
					Track track = bar.GetVisualDescendants().OfType<Track>().First();

					double viewport = track.ViewportSize;
					double extent = track.Maximum + track.ViewportSize;
					Assert.True(extent > viewport, "内容没有溢出,这条用例的前提(有滚动条)不成立");

					int expected = (int)Math.Ceiling(track.Bounds.Height * viewport / extent);
					Assert.Equal(slot, expected);                                  // 比例律本身(ceil 口径)
					Assert.Equal(expected, (int)Math.Round(thumb.Bounds.Height));  // 滑块槽高

					Rect barRect = BarRect(window, bar);
					Rect visible = VisibleThumbRect(window, bar, painted);
					Assert.Equal(expected - ThumbLengthInset, (int)Math.Round(visible.Height)); // Windows 让掉上下各 2

					// 像素行数:可见那 4 列里,滑块色的行数 = 可见高度
					using WriteableBitmap frame = window.CaptureRenderedFrame()!;
					SortedDictionary<int, (int Rows, int Fg)> cols = ThumbColumns(frame, barRect);
					int barLeft = (int)Math.Round(barRect.X);
					Assert.Equal(new[] { barLeft + 3, barLeft + 4, barLeft + 5, barLeft + 6 }, cols.Keys.ToArray());
					Assert.All(cols.Values, v =>
					{
						Assert.Equal(expected - ThumbLengthInset, v.Rows);
						Assert.Equal(0xCF, v.Fg);
					});
				}
				finally
				{
					window.Close();
				}
			}
		});
	}
}
