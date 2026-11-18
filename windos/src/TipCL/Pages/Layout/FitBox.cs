using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TipCL.Pages.Layout;

/// <summary>
/// 窄宽等比缩放容器:内容自然高度不超过 <see cref="MaxNaturalHeight"/>(标题栏、筛选条、页签条这类矮内容)
/// 且可用宽不够时,整体等比缩小塞进去(最小 55%),而不是裁掉右半截或把标题挤成竖排。
/// 高内容(列表)不缩放 —— 列表按宽度重新排版才是对的。
/// 只在网格 / 自由画布编辑态下由 LayoutCard 挂上,常态渲染不受影响。
/// </summary>
public sealed class FitBox : Decorator
{
	/// <summary>缩放下限(0.55 = 最窄缩到 55%)。</summary>
	public double MinScale { get; set; } = 0.55;

	/// <summary>只有"矮内容"才缩放:自然高度超过这个值的(列表等)照常按宽度排版。</summary>
	public double MaxNaturalHeight { get; set; } = 170.0;

	private double _scale = 1.0;

	public double Scale => _scale;

	protected override Size MeasureOverride(Size available)
	{
		UIElement? child = Child;
		if (child == null)
		{
			return default(Size);
		}
		double width = ((double.IsInfinity(available.Width) || available.Width <= 0.0) ? 900.0 : available.Width);
		child.Measure(new Size(width, double.PositiveInfinity));
		Size want = child.DesiredSize;
		_scale = 1.0;
		if (want.Height <= MaxNaturalHeight && want.Width > width && width > 0.0)
		{
			_scale = Math.Max(MinScale, width / want.Width);
		}
		double w = Math.Min(width, want.Width * _scale);
		double h = want.Height * _scale;
		return new Size(w, h);
	}

	protected override Size ArrangeOverride(Size final)
	{
		UIElement? child = Child;
		if (child == null)
		{
			return final;
		}
		Size want = child.DesiredSize;
		child.Arrange(new Rect(0.0, 0.0, want.Width, want.Height));
		if (Math.Abs(_scale - 1.0) < 1e-6)
		{
			child.RenderTransform = Transform.Identity;
		}
		else
		{
			TransformGroup group = new TransformGroup();
			if (child.RenderTransform != null && !(child.RenderTransform is MatrixTransform { Matrix: { IsIdentity: not false } }))
			{
				group.Children.Add(child.RenderTransform);
			}
			group.Children.Add(new ScaleTransform(_scale, _scale));
			child.RenderTransformOrigin = new Point(0.0, 0.0);
			child.RenderTransform = group;
		}
		return new Size(Math.Min(final.Width, want.Width * _scale), want.Height * _scale);
	}
}