using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using TipCL.Controls;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C9a 那三条登记遗留的**可机器验证部分**(原文见 `PROGRESS.md` 的 C9a「未验证 / 遗留」1/2/3)。
///
/// <list type="number">
/// <item>**`FeedbackPage` 的页级证据**(遗留 1):该页没有独立冒烟帧,切到 `PageLayoutHost` 之后也没加过页级用例。
///   这里把它按 <c>C5cLayoutProbeTests</c> 的同一把尺子挂进 **668 宽的窄页面**里,既钉住"切后仍是两栏"的结构,
///   也**实测**右栏的像素值(探针读几何量,不读截图);</item>
/// <item>**per-site 间距 / 346 vs 360**(遗留 2):`SplitCatalog` 未移植 ⇒ 每处不同间隙那张声明表**没有对应物**
///   (要收口就得改 `mac/src/**` 的产品代码 + 一次像素重定基,不属本测试任务的范围);
///   但 346/360 **不是两个互相矛盾的数字**:它是"Windows 的右列声明值 346" vs "mac 的右列 360 里那 14px 左外边距",
///   两边的**卡片内容宽都是 346** —— 这一条本文件**实测断言**(机制差异已登记,几何等价是可验证的);</item>
/// <item>**真机手感**(遗留 3):"贴着边界点内容被分隔条吃掉"有多明显,只有真机上的人能说;本机无 Mac
///   (`GOAL.md` L41)⇒ 不可验。但它的**客观那一半**可以在这里钉死:设置页 `gap: 0` + 命中宽 8 ⇒ 命中区
///   **边界两侧各压住内容 4px**(实测量,不是照抄注释);剩下的"是否明显"才是真机项。</item>
/// </list>
/// </summary>
public class C9aLeftoverTests
{
	/// <summary>与 C5c 探针同一把尺子:900 宽窗口 − 196px 侧栏 − 36px 页边距 ≈ 668。</summary>
	private const double NarrowPageWidth = 668.0;

	/// <summary>
	/// **遗留 1**:`FeedbackPage` 切到宿主之后**仍是两栏**,右栏声明值 360、gap 0(该页切前就是"两列无间隔带"),
	/// 而且**实测**右栏 360、右卡内容宽 346(= Windows 那边声明的那个 346)。
	/// </summary>
	[Fact]
	public void Feedback_page_keeps_two_columns_and_the_declared_360_right_column()
	{
		AvaloniaSession.Run(() =>
		{
			using var scope = C5bScope.Capture();
			using var toasts = new C3cToasts();
			using var api = new C3cFakeApi()
				.Route("feedback_history", "[]")
				.Route("help_articles", "[]");
			C5bFiles.LayoutDir();

			Grid root = new Grid();
			UiHost window = UiHost.Show(root);
			window.Window.Width = 900.0;
			window.Window.Height = 550.0;
			C5bFiles.Flush(2);
			Border frame = new Border
			{
				Width = NarrowPageWidth,
				HorizontalAlignment = HorizontalAlignment.Left,
				Child = new FeedbackPage(),
			};
			root.Children.Add(frame);
			C5bFiles.Flush(6);
			try
			{
				Control page = (Control)frame.Child!;
				PageLayoutHost host = C5bFiles.Descendants(page).OfType<PageLayoutHost>().Single();

				// 结构:三列(左星 / 间隙 0 / 右 360)且左栏没被收起 ⇒ 两栏都在
				Assert.Equal(3, host.ColumnDefinitions.Count);
				Assert.Equal(GridUnitType.Star, host.ColumnDefinitions[0].Width.GridUnitType);
				Assert.Equal(1.0, host.ColumnDefinitions[0].Width.Value, 3);
				Assert.Equal(0.0, host.ColumnDefinitions[1].Width.Value, 3);
				Assert.Equal(0.0, host.GapWidth);
				Assert.Equal(0.0, host.SplitterHitWidth);
				Assert.Equal(GridUnitType.Pixel, host.ColumnDefinitions[2].Width.GridUnitType);
				Assert.Equal(360.0, host.ColumnDefinitions[2].Width.Value, 3);
				Assert.False(host.LeftCollapsed);
				Assert.True(host.LeftVisible);

				// 实测(探针口径:读 Bounds / ActualWidth,不靠截图猜)
				Control left = host.Children.OfType<Control>().First(c => Grid.GetColumn(c) == 0);
				Control right = host.Children.OfType<Control>().First(c => Grid.GetColumn(c) == 2);
				double leftColumn = host.ColumnDefinitions[0].ActualWidth;
				double rightColumn = host.ColumnDefinitions[2].ActualWidth;
				string measured = "页面宽=" + page.Bounds.Width.ToString("0.#") + " 宿主宽=" + host.Bounds.Width.ToString("0.#") +
					" 左列=" + leftColumn.ToString("0.#") + " 右列=" + rightColumn.ToString("0.#") +
					" 左栏宽=" + left.Bounds.Width.ToString("0.#") + " 右卡宽=" + right.Bounds.Width.ToString("0.#") +
					" 右卡左边距=" + right.Margin.Left.ToString("0.#");
				Assert.True(leftColumn > 200.0, "窄页面里左栏必须还有可用宽度!(" + measured + ")");
				Assert.Equal(360.0, rightColumn, 1);
				Assert.Equal(346.0, right.Bounds.Width, 1);
				Assert.Equal(14.0, right.Margin.Left, 1);
			}
			finally
			{
				window.Dispose();
				UiHost.Flush();
			}
		});
	}

	/// <summary>
	/// **遗留 3 的客观那一半**:设置页那种"`gap: 0` + 命中宽 8"的宿主,分隔条的命中区**居中压在边界上**
	/// ⇒ 边界两侧**各压住内容 4px**(Windows 是 18px 带、不重叠;这是已登记的机制差异)。
	/// 本用例实测那 4px:分隔条自己的宽是 8、相对宿主的位置与左右列的实际宽一致。
	/// </summary>
	[Fact]
	public void Splitter_hit_area_overlaps_each_side_of_the_boundary_by_four_pixels()
	{
		AvaloniaSession.Run(() =>
		{
			using var scope = C5bScope.Capture();
			using var toasts = new C3cToasts();
			C5bFiles.LayoutDir();

			Grid root = new Grid();
			UiHost window = UiHost.Show(root);
			try
			{
				PageLayoutHost host = new PageLayoutHost(
					"c9a-leftover", "nav", new GridLength(168.0), new GridLength(1.0, GridUnitType.Star),
					gap: 0.0, splitterHitWidth: 8.0)
				{
					Width = 640.0,
				};
				host.SetLeft(new Border());
				host.SetRight(new Border());
				root.Children.Add(host);
				C5bFiles.Flush(6);

				GridSplitter splitter = C5bFiles.Descendants(host).OfType<GridSplitter>().Single();
				Assert.Equal(8.0, splitter.Width);
				Assert.Equal(HorizontalAlignment.Center, splitter.HorizontalAlignment);

				double boundary = host.ColumnDefinitions[0].ActualWidth + host.ColumnDefinitions[1].ActualWidth;
				Point origin = splitter.TranslatePoint(new Point(0.0, 0.0), host) ?? new Point(double.NaN, double.NaN);
				double x = origin.X;
				double width = splitter.Bounds.Width;
				string measured = "边界x=" + boundary.ToString("0.#") + " 分隔条x=" + x.ToString("0.#") +
					" 分隔条宽=" + width.ToString("0.#") + " 左压=" + (boundary - x).ToString("0.#") +
					" 右压=" + (x + width - boundary).ToString("0.#") + " 宿主宽=" + host.Bounds.Width.ToString("0.#");
				Assert.Equal(168.0, boundary, 1);
				Assert.Equal(8.0, width, 1);
				Assert.Equal(4.0, boundary - x, 1);
				Assert.Equal(4.0, x + width - boundary, 1);
			}
			finally
			{
				window.Dispose();
				UiHost.Flush();
			}
		});
	}
}
