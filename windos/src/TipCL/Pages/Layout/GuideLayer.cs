using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Pages.Layout;

// 对齐辅助线 / 框选矩形的绘制层(§4.G)。
// 只画不判:判定与磁性吸附位移全在纯模型 LayoutGuides.Align(阈值 4px),这样 net8 测试宿主能断言,
// 而这一层只负责把结果画到画布上方(IsHitTestVisible=false,不吃任何鼠标事件)。
// 视觉:主题色 10% 透明实线 1px(辅助线)+ 主题色虚线(框选),与编辑态蒙版同一套配色。
public sealed class GuideLayer : FrameworkElement
{
	private readonly List<LayoutGuideLine> _lines = new List<LayoutGuideLine>();

	private bool _hasMarquee;

	private LayoutRect _marquee;

	/// <summary>构造:不吃鼠标事件(拖动期间指针必须留在被拖块上)。</summary>
	public GuideLayer()
	{
		base.IsHitTestVisible = false;
		base.SnapsToDevicePixels = true;
	}

	/// <summary>当前是否画着东西(宿主可据此决定要不要刷新)。</summary>
	public bool IsActive
	{
		get
		{
			return _lines.Count > 0 || _hasMarquee;
		}
	}

	/// <summary>画辅助线(list 为空 = 清掉线,保留框选)。</summary>
	public void ShowLines(IReadOnlyList<LayoutGuideLine>? lines)
	{
		_lines.Clear();
		if (lines != null)
		{
			foreach (LayoutGuideLine line in lines)
			{
				if (line != null)
				{
					_lines.Add(line);
				}
			}
		}
		InvalidateVisual();
	}

	/// <summary>画框选矩形。</summary>
	public void ShowMarquee(LayoutRect rect)
	{
		_hasMarquee = true;
		_marquee = rect;
		InvalidateVisual();
	}

	/// <summary>全清(退出拖拽/框选时调)。</summary>
	public void Clear()
	{
		if (_lines.Count == 0 && !_hasMarquee)
		{
			return;
		}
		_lines.Clear();
		_hasMarquee = false;
		InvalidateVisual();
	}

	/// <summary>
	/// 画辅助线:竖线/横线都从画布内容区的一头拉到另一头(与设计一致:对齐线要贯通整个画布,
	/// 只画"两块之间"那一小段看不出对齐关系)。
	/// </summary>
	protected override void OnRender(DrawingContext dc)
	{
		if (_lines.Count == 0 && !_hasMarquee)
		{
			return;
		}
		Color accent = LayoutEditor.AccentColor();
		if (_lines.Count != 0)
		{
			Pen pen = new Pen(new SolidColorBrush(Color.FromArgb(150, accent.R, accent.G, accent.B)), 1.0);
			pen.Freeze();
			double width = Math.Max(0.0, base.ActualWidth);
			double height = Math.Max(0.0, base.ActualHeight);
			foreach (LayoutGuideLine line in _lines)
			{
				if (line.Vertical)
				{
					double x = Clamp(line.Position, 0.0, width);
					dc.DrawLine(pen, new Point(x + 0.5, 0.0), new Point(x + 0.5, height));
				}
				else
				{
					double y = Clamp(line.Position, 0.0, height);
					dc.DrawLine(pen, new Point(0.0, y + 0.5), new Point(width, y + 0.5));
				}
			}
		}
		if (_hasMarquee)
		{
			Pen pen2 = new Pen(new SolidColorBrush(Color.FromArgb(180, accent.R, accent.G, accent.B)), 1.0);
			pen2.DashStyle = new DashStyle(new double[2] { 4.0, 3.0 }, 0.0);
			pen2.Freeze();
			SolidColorBrush fill = new SolidColorBrush(Color.FromArgb(16, accent.R, accent.G, accent.B));
			fill.Freeze();
			dc.DrawRectangle(fill, pen2, new Rect(_marquee.X, _marquee.Y, Math.Max(0.0, _marquee.W), Math.Max(0.0, _marquee.H)));
		}
	}

	private static double Clamp(double value, double min, double max)
	{
		if (value < min)
		{
			return min;
		}
		if (value > max)
		{
			return max;
		}
		return value;
	}

	/// <summary>
	/// R34 v3:把画布自己的落点占位框改成「主题色浅底 + 虚线框」(深色启动页:白色半透明 + 白色虚线)。
	/// 占位框的位置 / 动画仍由 GridCanvasPanel 管,这里只给它换一身外观(加一个虚线描边子元素),
	/// 重复调用无副作用。
	/// </summary>
	public static void DecorateDropBoxes(Panel? box, bool darkSurface)
	{
		if (box == null)
		{
			return;
		}
		foreach (UIElement child in box.Children)
		{
			if (child is DropPlaceholder placeholder)
			{
				DecorateDropBox(placeholder, darkSurface);
			}
		}
	}

	private static void DecorateDropBox(Border placeholder, bool darkSurface)
	{
		placeholder.BorderThickness = new Thickness(0.0);
		placeholder.CornerRadius = new CornerRadius(12.0);
		if (darkSurface)
		{
			placeholder.Background = LayoutEditLook.Frozen(Color.FromArgb(31, 255, 255, 255));
		}
		else
		{
			LayoutEditLook.Bind(placeholder, Border.BackgroundProperty, "B.EditDropFill", "B.AccentSoft");
		}
		if (placeholder.Child is Rectangle)
		{
			return;
		}
		Rectangle dash = new Rectangle
		{
			RadiusX = 12.0,
			RadiusY = 12.0,
			StrokeThickness = 1.5,
			StrokeDashArray = new DoubleCollection { 3.0, 2.0 },
			IsHitTestVisible = false,
			SnapsToDevicePixels = true
		};
		if (darkSurface)
		{
			dash.Stroke = Brushes.White;
		}
		else
		{
			dash.SetResourceReference(Shape.StrokeProperty, "B.Accent");
		}
		placeholder.Child = dash;
	}
}

/// <summary>
/// R34 v3:拖动 / 改宽度时画在卡片**下面**的淡色列底纹(每列一条圆角浅条),让"按整列对齐"看得见。
/// 只在手势进行中显示;不吃鼠标。列宽 / 间距取自画布(宿主每次开始手势时喂进来)。
/// </summary>
public sealed class EditColumnShade : FrameworkElement
{
	private int _cols;

	private double _columnWidth;

	private double _gap;

	private bool _dark;

	public EditColumnShade()
	{
		base.IsHitTestVisible = false;
		base.SnapsToDevicePixels = true;
	}

	/// <summary>当前是否显示。</summary>
	public bool IsShown => _cols > 0;

	/// <summary>显示列底纹(cols ≤ 1 或列宽未知时不画)。</summary>
	public void Show(int cols, double columnWidth, double gap, bool dark)
	{
		_cols = (cols > 1 && columnWidth > 1.0) ? cols : 0;
		_columnWidth = columnWidth;
		_gap = Math.Max(0.0, gap);
		_dark = dark;
		InvalidateVisual();
	}

	/// <summary>收起。</summary>
	public void Hide()
	{
		if (_cols == 0)
		{
			return;
		}
		_cols = 0;
		InvalidateVisual();
	}

	protected override void OnRender(DrawingContext dc)
	{
		if (_cols <= 0)
		{
			return;
		}
		double height = Math.Max(0.0, base.ActualHeight);
		if (height <= 1.0)
		{
			return;
		}
		Brush brush = _dark ? LayoutEditLook.Frozen(Color.FromArgb(15, 255, 255, 255)) : LayoutEditLook.BrushOf("B.EditColShade", "B.SegRail", Color.FromArgb(9, 0, 0, 0));
		for (int i = 0; i < _cols; i++)
		{
			double x = i * (_columnWidth + _gap);
			dc.DrawRoundedRectangle(brush, null, new Rect(x, 0.0, _columnWidth, height), 8.0, 8.0);
		}
	}
}

/// <summary>
/// R34 v3 编辑态外观的资源查找:新键由 WP-L0 在 Controls.xaml / 两个 Palette 里提供(并行开发,本分支未必有),
/// 所以一律"先查新键、查不到退回既有键或代码内兜底",**绝不**用找不到就抛异常的 FindResource。
/// 矢量图标(眼睛划线 / 撤销 / 重做 / 六点拖拽 / 锁)在 Ico 字体里没有,兜底几何直接照效果图的 SVG 路径写。
/// </summary>
public static class LayoutEditLook
{
	/// <summary>眼睛(16×16 视框,描边)。</summary>
	public const string PathEye = "M1.5,8 C1.5,8 4,3.5 8,3.5 C12,3.5 14.5,8 14.5,8 C14.5,8 12,12.5 8,12.5 C4,12.5 1.5,8 1.5,8 Z M6,8 A2,2 0 1 0 10,8 A2,2 0 1 0 6,8 Z";

	/// <summary>眼睛划线(16×16 视框,描边)。</summary>
	public const string PathEyeOff = "M2,2 L14,14 M6.6,3.7 A6.6,6.6 0 0 1 8,3.5 C12,3.5 14.5,8 14.5,8 A12,12 0 0 1 12.5,10.6 M10.1,10.2 A3,3 0 0 1 8,11 C4,11 1.5,8 1.5,8 A12,12 0 0 1 4.4,4.8";

	/// <summary>撤销(16×16 视框,描边)。</summary>
	public const string PathUndo = "M5.5,3.5 L2.5,6.5 L5.5,9.5 M2.5,6.5 L9.5,6.5 A4,4 0 0 1 9.5,14.5 L8,14.5";

	/// <summary>重做(16×16 视框,描边)。</summary>
	public const string PathRedo = "M10.5,3.5 L13.5,6.5 L10.5,9.5 M13.5,6.5 L6.5,6.5 A4,4 0 0 0 6.5,14.5 L8,14.5";

	/// <summary>锁(16×16 视框,描边)。</summary>
	public const string PathLock = "M5,7 L11,7 A1.5,1.5 0 0 1 12.5,8.5 L12.5,12 A1.5,1.5 0 0 1 11,13.5 L5,13.5 A1.5,1.5 0 0 1 3.5,12 L3.5,8.5 A1.5,1.5 0 0 1 5,7 Z M5.5,7 L5.5,5 A2.5,2.5 0 0 1 10.5,5 L10.5,7";

	/// <summary>六点拖拽(16×16 视框;兜底用小圆点,填充绘制)。</summary>
	public const string PathGrip = "M4.9,4 A1.1,1.1 0 1 0 7.1,4 A1.1,1.1 0 1 0 4.9,4 Z M8.9,4 A1.1,1.1 0 1 0 11.1,4 A1.1,1.1 0 1 0 8.9,4 Z M4.9,8 A1.1,1.1 0 1 0 7.1,8 A1.1,1.1 0 1 0 4.9,8 Z M8.9,8 A1.1,1.1 0 1 0 11.1,8 A1.1,1.1 0 1 0 8.9,8 Z M4.9,12 A1.1,1.1 0 1 0 7.1,12 A1.1,1.1 0 1 0 4.9,12 Z M8.9,12 A1.1,1.1 0 1 0 11.1,12 A1.1,1.1 0 1 0 8.9,12 Z";

	private static readonly Dictionary<string, Geometry> _fallbackGeometry = new Dictionary<string, Geometry>(StringComparer.Ordinal);

	/// <summary>应用级资源里有没有这个键(没有 Application 时一律 false)。</summary>
	public static bool Has(string key)
	{
		try
		{
			return !string.IsNullOrEmpty(key) && Application.Current?.TryFindResource(key) != null;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// 动态绑定画刷:新键存在就引用新键,否则引用既有的兜底键(两者都是 DynamicResource,主题切换照常跟随)。
	/// </summary>
	public static void Bind(FrameworkElement element, DependencyProperty property, string key, string fallbackKey)
	{
		if (element == null || property == null)
		{
			return;
		}
		element.SetResourceReference(property, Has(key) ? key : fallbackKey);
	}

	/// <summary>取画刷:新键 → 兜底键 → 代码常量(给 OnRender 这类不能用 DynamicResource 的地方)。</summary>
	public static Brush BrushOf(string key, string fallbackKey, Color fallback)
	{
		try
		{
			if (Application.Current?.TryFindResource(key) is Brush brush)
			{
				return brush;
			}
			if (!string.IsNullOrEmpty(fallbackKey) && Application.Current?.TryFindResource(fallbackKey) is Brush brush2)
			{
				return brush2;
			}
		}
		catch
		{
		}
		return Frozen(fallback);
	}

	/// <summary>冻结的纯色画刷。</summary>
	public static SolidColorBrush Frozen(Color color)
	{
		SolidColorBrush brush = new SolidColorBrush(color);
		brush.Freeze();
		return brush;
	}

	/// <summary>套样式:键存在且 TargetType 兼容才套(TargetType 不匹配时 WPF 会抛异常,这里直接跳过)。返回是否套上。</summary>
	public static bool TryApplyStyle(FrameworkElement element, string key)
	{
		if (element == null)
		{
			return false;
		}
		try
		{
			if (Application.Current?.TryFindResource(key) is Style style && (style.TargetType == null || style.TargetType.IsAssignableFrom(element.GetType())))
			{
				element.Style = style;
				return true;
			}
		}
		catch
		{
		}
		return false;
	}

	/// <summary>矢量图标几何:优先 WP-L0 的 <c>Geo.*</c> 资源,否则用兜底路径(解析一次后缓存)。</summary>
	public static Geometry GeometryOf(string key, string fallbackPath)
	{
		try
		{
			if (Application.Current?.TryFindResource(key) is Geometry geometry)
			{
				return geometry;
			}
		}
		catch
		{
		}
		if (!_fallbackGeometry.TryGetValue(fallbackPath, out Geometry? parsed))
		{
			try
			{
				parsed = Geometry.Parse(fallbackPath);
				parsed.Freeze();
			}
			catch
			{
				parsed = Geometry.Empty;
			}
			_fallbackGeometry[fallbackPath] = parsed;
		}
		return parsed;
	}

	/// <summary>
	/// 矢量图标元素:16×16 视框缩放到 <paramref name="size"/>,圆头描边(线宽 1.5,对齐窗口按钮的画法);
	/// <paramref name="fill"/> = 同色填充(六点拖拽)。颜色走 DynamicResource。
	/// </summary>
	public static FrameworkElement Icon(string key, string fallbackPath, double size, string brushKey, bool fill = false)
	{
		Path path = new Path
		{
			Data = GeometryOf(key, fallbackPath),
			StrokeThickness = 1.5,
			StrokeStartLineCap = PenLineCap.Round,
			StrokeEndLineCap = PenLineCap.Round,
			StrokeLineJoin = PenLineJoin.Round,
			SnapsToDevicePixels = false
		};
		path.SetResourceReference(Shape.StrokeProperty, brushKey);
		if (fill)
		{
			path.SetResourceReference(Shape.FillProperty, brushKey);
			path.StrokeThickness = 0.0;
		}
		Canvas canvas = new Canvas
		{
			Width = 16.0,
			Height = 16.0
		};
		canvas.Children.Add(path);
		return new Viewbox
		{
			Width = size,
			Height = size,
			Stretch = Stretch.Uniform,
			Child = canvas,
			IsHitTestVisible = false,
			VerticalAlignment = VerticalAlignment.Center
		};
	}
}
