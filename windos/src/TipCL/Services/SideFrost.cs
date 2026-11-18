using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace TipCL.Services;

public static class SideFrost
{
	public const int DefaultRadius = 30;

	private static Grid? _wallHost;

	private static Border? _side;

	private static Grid? _layer;

	private static readonly Rectangle _frost = new Rectangle
	{
		IsHitTestVisible = false,
		Visibility = Visibility.Collapsed
	};

	private static readonly Rectangle _tint = new Rectangle
	{
		IsHitTestVisible = false
	};

	private static readonly BlurEffect _blur = new BlurEffect
	{
		KernelType = KernelType.Gaussian,
		RenderingBias = RenderingBias.Performance
	};

	private static VisualBrush? _brush;

	// 缩放窗口期间先把模糊摘掉(只留 tint),停手 120ms 再挂回来。
	// 原来的写法是每次 SizeChanged 都整块重算高斯模糊:大窗口/高分屏下拖边框会明显发滞。
	private static readonly DispatcherTimer _settle = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(120)
	};

	private static bool _settled = true;

	public static bool Enabled { get; private set; } = true;

	public static int Radius { get; private set; } = 30;

	public static void Attach(Grid wallHost, Border side, bool enabled, int radius)
	{
		_wallHost = wallHost;
		_side = side;
		Enabled = enabled;
		Radius = Math.Max(8, Math.Min(60, radius));
		UIElement child = side.Child;
		side.Child = null;
		_layer = new Grid
		{
			ClipToBounds = true
		};
		_layer.Children.Add(_frost);
		_layer.Children.Add(_tint);
		if (child != null)
		{
			_layer.Children.Add(child);
		}
		side.Child = _layer;
		_brush = new VisualBrush(wallHost)
		{
			ViewboxUnits = BrushMappingMode.Absolute,
			Stretch = Stretch.Fill,
			AlignmentX = AlignmentX.Left,
			AlignmentY = AlignmentY.Top
		};
		_frost.Fill = _brush;
		_frost.Effect = _blur;
		RenderOptions.SetCachingHint(_brush, CachingHint.Unspecified);
		_settle.Tick += delegate
		{
			_settle.Stop();
			Settle();
		};
		side.SizeChanged += delegate
		{
			BeginResize();
			SyncViewbox();
		};
		wallHost.SizeChanged += delegate
		{
			BeginResize();
			SyncViewbox();
		};
		App.ThemeVisualsChanged -= Refresh;
		App.ThemeVisualsChanged += Refresh;
		SideFrostEx.Install(_wallHost, _side, _layer, _tint);
		Refresh();
	}

	// 缩放中:摘掉模糊(合成器只需平移 tint),避免每一帧都重算 Gaussian。
	private static void BeginResize()
	{
		if (!Enabled)
		{
			return;
		}
		if (_settled)
		{
			_settled = false;
			_frost.Effect = null;
		}
		_settle.Stop();
		_settle.Start();
	}

	// 停手:把模糊挂回来并重算一次 viewbox。
	private static void Settle()
	{
		_settled = true;
		if (Enabled)
		{
			_frost.Effect = _blur;
		}
		SyncViewbox();
	}

	public static void SetEnabled(bool on)
	{
		Enabled = on;
		Refresh();
	}

	public static void SetRadius(int radius)
	{
		Radius = Math.Max(8, Math.Min(60, radius));
		Refresh();
	}

	public static void Refresh()
	{
		if (_side == null || _layer == null)
		{
			SideFrostEx.Update();
		}
		else if (!Enabled)
		{
			_frost.Visibility = Visibility.Collapsed;
			_tint.Fill = null;
			_side.SetResourceReference(Border.BackgroundProperty, "B.Glass");
			SideFrostEx.Update();
		}
		else
		{
			_side.Background = Brushes.Transparent;
			bool flag = true;
			_frost.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
			_blur.Radius = Radius;
			// 从未挂着模糊(或刚被缩放摘掉)时补挂;缩放中保持 null,由 Settle() 收尾
			_frost.Effect = (_settled ? ((Effect)_blur) : null);
			_tint.Fill = TintBrush(flag);
			SyncViewbox();
			SideFrostEx.Update();
		}
	}

	private static Brush TintBrush(bool wall)
	{
		Color color = (Application.Current?.TryFindResource("B.Glass") as SolidColorBrush)?.Color ?? (App.IsDark ? Color.FromRgb(28, 36, 32) : Color.FromRgb(244, 246, 245));
		double opacity = (wall ? (App.IsDark ? 0.46 : 0.52) : (App.IsDark ? 0.62 : 0.66));
		SolidColorBrush solidColorBrush = new SolidColorBrush(color);
		solidColorBrush.Opacity = opacity;
		solidColorBrush.Freeze();
		return solidColorBrush;
	}

	private static void SyncViewbox()
	{
		if (_brush != null && _side != null && _wallHost != null && _frost.Visibility == Visibility.Visible && !(_side.ActualWidth <= 0.0) && !(_side.ActualHeight <= 0.0) && _side.IsVisible)
		{
			Point point;
			try
			{
				point = _side.TranslatePoint(new Point(0.0, 0.0), _wallHost);
			}
			catch (InvalidOperationException)
			{
				return;
			}
			double num = Radius;
			double actualWidth = _side.ActualWidth;
			double actualHeight = _side.ActualHeight;
			double num2 = Math.Max(0.0, point.X - num);
			double num3 = Math.Max(0.0, point.Y - num);
			double num4 = Math.Min(_wallHost.ActualWidth, point.X + actualWidth + num);
			double num5 = Math.Min(_wallHost.ActualHeight, point.Y + actualHeight + num);
			if (!(num4 <= num2) && !(num5 <= num3))
			{
				_brush.Viewbox = new Rect(num2, num3, num4 - num2, num5 - num3);
				_frost.Margin = new Thickness(num2 - point.X, num3 - point.Y, point.X + actualWidth - num4, point.Y + actualHeight - num5);
			}
		}
	}
}
