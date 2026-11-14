using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace TipCL.Services;

public static class SideFrostEx
{
	private static Grid _wall;

	private static Border _side;

	private static Canvas _amb;

	private static Rectangle _ambBase;

	private static Ellipse _b1;

	private static Ellipse _b2;

	private static Ellipse _b3;

	private static Ellipse _b4;

	private static Rectangle _cover;

	private static Rectangle _grain;

	private static Rectangle _sheen;

	private static Rectangle _edge;

	private static ImageBrush _grainBrush;

	public static void Install(Grid wallHost, Border side, Grid layer, Rectangle tint)
	{
		try
		{
			if (wallHost != null && side != null && layer != null)
			{
				_wall = wallHost;
				_side = side;
				_amb = new Canvas();
				_amb.IsHitTestVisible = false;
				_amb.ClipToBounds = false;
				_ambBase = new Rectangle();
				_b1 = new Ellipse();
				_b2 = new Ellipse();
				_b3 = new Ellipse();
				_b4 = new Ellipse();
				_amb.Children.Add(_ambBase);
				_amb.Children.Add(_b1);
				_amb.Children.Add(_b2);
				_amb.Children.Add(_b3);
				_amb.Children.Add(_b4);
				_cover = new Rectangle();
				_cover.SetResourceReference(Shape.FillProperty, "B.PageWash");
				_amb.Children.Add(_cover);
				wallHost.Children.Insert(0, _amb);
				_grainBrush = new ImageBrush(MakeGrain(128));
				_grainBrush.TileMode = TileMode.Tile;
				_grainBrush.Stretch = Stretch.None;
				_grainBrush.ViewportUnits = BrushMappingMode.Absolute;
				_grainBrush.Viewport = new Rect(0.0, 0.0, 128.0, 128.0);
				_grainBrush.Freeze();
				_grain = new Rectangle();
				_grain.IsHitTestVisible = false;
				_grain.Fill = _grainBrush;
				_sheen = new Rectangle();
				_sheen.IsHitTestVisible = false;
				_edge = new Rectangle();
				_edge.IsHitTestVisible = false;
				_edge.Width = 1.0;
				_edge.HorizontalAlignment = HorizontalAlignment.Right;
				int num = layer.Children.IndexOf(tint);
				if (num < 0)
				{
					num = ((layer.Children.Count <= 0) ? (-1) : 0);
				}
				int num2 = num + 1;
				layer.Children.Insert(num2, _sheen);
				layer.Children.Insert(num2 + 1, _grain);
				layer.Children.Insert(num2 + 2, _edge);
				wallHost.SizeChanged += OnSize;
				side.SizeChanged += OnSize;
				Layout();
			}
		}
		catch
		{
		}
	}

	private static void OnSize(object sender, SizeChangedEventArgs e)
	{
		Layout();
	}

	public static void Update()
	{
		try
		{
			if (_amb != null)
			{
				bool enabled = SideFrost.Enabled;
				Visibility visibility = ((!enabled) ? Visibility.Collapsed : Visibility.Visible);
				_amb.Visibility = visibility;
				_grain.Visibility = visibility;
				_sheen.Visibility = visibility;
				_edge.Visibility = visibility;
				if (enabled)
				{
					Paint(App.IsDark);
					Layout();
				}
			}
		}
		catch
		{
		}
	}

	private static Color Accent(bool dark)
	{
		if (Application.Current?.TryFindResource("B.Accent") is SolidColorBrush solidColorBrush)
		{
			return solidColorBrush.Color;
		}
		return Color.FromRgb(46, 155, 107);
	}

	private static Color Mix(Color a, Color b, double t)
	{
		return Color.FromRgb((byte)((double)(int)a.R + (double)(b.R - a.R) * t), (byte)((double)(int)a.G + (double)(b.G - a.G) * t), (byte)((double)(int)a.B + (double)(b.B - a.B) * t));
	}

	private static Brush Blob(Color c, double alpha)
	{
		RadialGradientBrush radialGradientBrush = new RadialGradientBrush();
		radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255.0 * alpha), c.R, c.G, c.B), 0.0));
		radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255.0 * alpha * 0.55), c.R, c.G, c.B), 0.45));
		radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1.0));
		radialGradientBrush.Freeze();
		return radialGradientBrush;
	}

	private static void Paint(bool dark)
	{
		Color color = Accent(dark);
		Color c = Color.FromRgb(56, 178, 196);
		Color c2 = Color.FromRgb(236, 196, 104);
		Color c3 = Mix(color, Colors.White, 0.45);
		double num = (dark ? 0.62 : 1.0);
		SolidColorBrush solidColorBrush = new SolidColorBrush(dark ? Color.FromRgb(20, 26, 23) : Color.FromRgb(236, 242, 239));
		solidColorBrush.Freeze();
		_ambBase.Fill = solidColorBrush;
		_b1.Fill = Blob(color, 0.78 * num);
		_b2.Fill = Blob(c, 0.62 * num);
		_b3.Fill = Blob(c2, 0.6 * num);
		_b4.Fill = Blob(c3, 0.7 * num);
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush();
		linearGradientBrush.StartPoint = new Point(0.0, 0.0);
		linearGradientBrush.EndPoint = new Point(0.35, 1.0);
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(dark ? 22u : 70u), byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.42));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.8));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(dark ? 10u : 28u), byte.MaxValue, byte.MaxValue, byte.MaxValue), 1.0));
		linearGradientBrush.Freeze();
		_sheen.Fill = linearGradientBrush;
		SolidColorBrush solidColorBrush2 = new SolidColorBrush(Color.FromArgb((byte)(dark ? 34u : 120u), byte.MaxValue, byte.MaxValue, byte.MaxValue));
		solidColorBrush2.Freeze();
		_edge.Fill = solidColorBrush2;
		_grain.Opacity = (dark ? 0.55 : 0.85);
	}

	private static void Place(Ellipse e, double cx, double cy, double d)
	{
		e.Width = d;
		e.Height = d;
		Canvas.SetLeft(e, cx - d / 2.0);
		Canvas.SetTop(e, cy - d / 2.0);
	}

	private static void Layout()
	{
		if (_amb == null || _wall == null || _side == null)
		{
			return;
		}
		double actualWidth = _wall.ActualWidth;
		double actualHeight = _wall.ActualHeight;
		if (actualWidth <= 0.0 || actualHeight <= 0.0)
		{
			return;
		}
		double num = ((_side.ActualWidth > 0.0) ? _side.ActualWidth : 196.0);
		double num2 = 40.0;
		try
		{
			num2 = _side.TranslatePoint(new Point(0.0, 0.0), _wall).Y;
			if (_side.ActualWidth > 0.0)
			{
				num = _side.ActualWidth;
			}
		}
		catch
		{
		}
		double num3 = Math.Max(200.0, actualHeight - num2);
		_ambBase.Width = actualWidth;
		_ambBase.Height = actualHeight;
		double num4 = Math.Max(300.0, num * 1.9);
		Place(_b1, num * 0.15, num2 + num3 * 0.12, num4);
		Place(_b2, num * 0.95, num2 + num3 * 0.42, num4 * 0.95);
		Place(_b3, num * 0.05, num2 + num3 * 0.7, num4 * 1.05);
		Place(_b4, num * 0.85, num2 + num3 * 0.98, num4);
		Canvas.SetLeft(_cover, num);
		Canvas.SetTop(_cover, num2);
		_cover.Width = Math.Max(0.0, actualWidth - num);
		_cover.Height = Math.Max(0.0, actualHeight - num2);
	}

	private static BitmapSource MakeGrain(int n)
	{
		byte[] array = new byte[n * n * 4];
		uint num = 2654435769u;
		for (int i = 0; i < n * n; i++)
		{
			num ^= num << 13;
			num ^= num >> 17;
			num ^= num << 5;
			bool flag = ((byte)(num & 0xFF) & 1) == 0;
			byte b = (byte)(((num >> 8) & 0xFF) % 14);
			array[i * 4 + 2] = (array[i * 4 + 1] = (array[i * 4] = (byte)((flag ? 255 : 0) * b / 255)));
			array[i * 4 + 3] = b;
		}
		BitmapSource bitmapSource = BitmapSource.Create(n, n, 96.0, 96.0, PixelFormats.Pbgra32, null, array, n * 4);
		bitmapSource.Freeze();
		return bitmapSource;
	}
}
