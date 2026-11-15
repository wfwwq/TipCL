using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TipCL.Pages.Layout;

/// <summary>
/// [layout-fix] 参考件v2-对齐:区块被拉窄后的自适应(Decorator)。
/// 内容是"矮的一行 / 几行"(标题栏、筛选条、页签条这类,自然高度不超过 <see cref="MaxAdaptHeight"/>)
/// 且自然宽度放不下时,整体等比缩小塞进去 —— 而不是把右半截直接裁掉,或者把标题挤成"一个字一行"。
/// 高内容(列表等)不缩放,照常按可用宽度排版。最小缩到 <see cref="MinScale"/>。
/// 默认 Enabled = false(只在画布模式打开),所以 stack 的默认渲染一个像素都不经过缩放分支。
/// </summary>
internal sealed class FitBox : Decorator
{
	/// <summary>超过这个自然高度就当成"高内容",不缩放。</summary>
	public const double MaxAdaptHeight = 170.0;

	/// <summary>最小缩放比(再小就不缩了,宁可裁)。</summary>
	public const double MinScale = 0.55;

	private double _scale = 1.0;

	/// <summary>是否启用自适应(画布模式才开)。</summary>
	public bool Enabled { get; set; }

	protected override Size MeasureOverride(Size available)
	{
		UIElement? child = Child;
		if (child == null)
		{
			return new Size(0.0, 0.0);
		}
		_scale = 1.0;
		if (Enabled && !double.IsInfinity(available.Width) && available.Width > 1.0)
		{
			// 先正常量一次:高内容(列表)且块不算窄时直接用,避免对长列表做无限宽度的额外测量
			child.Measure(available);
			if (child.DesiredSize.Height > MaxAdaptHeight && available.Width >= 360.0)
			{
				return child.DesiredSize;
			}
			child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
			Size natural = child.DesiredSize;
			if (natural.Height > 0.0 && natural.Height <= MaxAdaptHeight && natural.Width > available.Width + 0.5)
			{
				_scale = Math.Max(MinScale, available.Width / natural.Width);
				child.Measure(new Size(available.Width / _scale, double.PositiveInfinity));
				double height = child.DesiredSize.Height * _scale;
				return new Size(available.Width, double.IsInfinity(available.Height) ? height : Math.Min(height, available.Height));
			}
		}
		child.Measure(available);
		return child.DesiredSize;
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		UIElement? child = Child;
		if (child == null)
		{
			return finalSize;
		}
		ClipToBounds = _scale < 0.999;
		if (_scale < 0.999)
		{
			child.RenderTransform = new ScaleTransform(_scale, _scale);
			child.Arrange(new Rect(0.0, 0.0, finalSize.Width / _scale, finalSize.Height / _scale));
		}
		else
		{
			if (child.RenderTransform is ScaleTransform)
			{
				child.RenderTransform = Transform.Identity;
			}
			child.Arrange(new Rect(finalSize));
		}
		return finalSize;
	}
}
