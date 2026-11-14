using System;
using System.Windows;
using System.Windows.Controls;

namespace TipCL.Services;

public sealed class SPanel : Panel
{
	public Orientation Orientation { get; set; } = Orientation.Vertical;

	public double Spacing { get; set; } = 8.0;

	protected override Size MeasureOverride(Size available)
	{
		double num = 0.0;
		double num2 = 0.0;
		int num3 = 0;
		Size availableSize = ((Orientation == Orientation.Vertical) ? new Size(available.Width, double.PositiveInfinity) : new Size(double.PositiveInfinity, available.Height));
		foreach (UIElement internalChild in base.InternalChildren)
		{
			internalChild.Measure(availableSize);
			if (internalChild.Visibility != Visibility.Collapsed)
			{
				num3++;
				if (Orientation == Orientation.Vertical)
				{
					num = Math.Max(num, internalChild.DesiredSize.Width);
					num2 += internalChild.DesiredSize.Height;
				}
				else
				{
					num2 = Math.Max(num2, internalChild.DesiredSize.Height);
					num += internalChild.DesiredSize.Width;
				}
			}
		}
		double num4 = (double)Math.Max(0, num3 - 1) * Spacing;
		if (Orientation == Orientation.Vertical)
		{
			num2 += num4;
		}
		else
		{
			num += num4;
		}
		return new Size(double.IsInfinity(available.Width) ? num : Math.Min(num, Math.Max(num, available.Width)), double.IsInfinity(available.Height) ? num2 : num2);
	}

	protected override Size ArrangeOverride(Size final)
	{
		double num = 0.0;
		foreach (UIElement internalChild in base.InternalChildren)
		{
			if (internalChild.Visibility != Visibility.Collapsed)
			{
				if (Orientation == Orientation.Vertical)
				{
					internalChild.Arrange(new Rect(0.0, num, final.Width, internalChild.DesiredSize.Height));
					num += internalChild.DesiredSize.Height + Spacing;
				}
				else
				{
					internalChild.Arrange(new Rect(num, 0.0, internalChild.DesiredSize.Width, final.Height));
					num += internalChild.DesiredSize.Width + Spacing;
				}
			}
		}
		return final;
	}
}
