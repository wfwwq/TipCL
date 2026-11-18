// 布局修复(第三批,Linux 补修)规格 §6-问题20:区块拉窄后的等比缩放(FitBox)。
// Avalonia 的 RenderTransformOrigin 缺省在中心,WPF 在左上角;照搬 WPF 写法会让缩小后的内容向右下偏移、
// 左上角空出一块、右半截照样被裁。这里钉住:缩放后内容的左上角就在 FitBox 的左上角,右下角不越界。
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using TipCL.App.Pages.Layout;
using TipCL.Tests.App;
using Xunit;

public class LayoutFitBoxOriginTests
{
	[Fact]
	public void ScaledContentIsAnchoredTopLeftAndFitsInside()
	{
		TestApp.Run(() =>
		{
			// 一条"矮而宽"的内容(自然宽 400,高 40)塞进 240 宽的 FitBox → 等比缩到 0.6(高于下限 0.55)
			Border wide = new Border { Width = 400.0, Height = 40.0 };
			FitBox fit = new FitBox
			{
				Enabled = true,
				Child = wide,
				Width = 240.0,
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Top
			};
			Window window = new Window { Width = 400.0, Height = 300.0, Content = fit };
			window.Show();
			window.UpdateLayout();
			try
			{
				Point topLeft = wide.TranslatePoint(new Point(0.0, 0.0), fit) ?? new Point(double.NaN, double.NaN);
				Point bottomRight = wide.TranslatePoint(new Point(400.0, 40.0), fit) ?? new Point(double.NaN, double.NaN);
				Assert.Equal(0.0, topLeft.X, 3);
				Assert.Equal(0.0, topLeft.Y, 3);
				// 缩放后的右下角正好落在 FitBox 的右边沿(240),高度按同一比例缩到 24
				Assert.Equal(240.0, bottomRight.X, 3);
				Assert.Equal(24.0, bottomRight.Y, 3);
				Assert.Equal(24.0, fit.Bounds.Height, 3);
			}
			finally
			{
				window.Close();
			}
		});
	}
}
