using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

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
}
