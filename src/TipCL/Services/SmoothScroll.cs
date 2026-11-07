using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace TipCL.Services;

public sealed class SmoothScroll : ScrollViewer
{
	public static readonly DependencyProperty OffsetProperty = DependencyProperty.Register("Offset", typeof(double), typeof(SmoothScroll), new PropertyMetadata(0.0, delegate(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		((SmoothScroll)d).ScrollToVerticalOffset((double)e.NewValue);
	}));

	/// <summary>MaxHeight = 窗口高 × pct，且不低于 floor：矮窗下内层滚动区收缩而不是留白（S11 外层已钉底，两者配合）。</summary>
	public void BindWindowMaxHeight(double pct, double floor)
	{
		SetBinding(MaxHeightProperty, new System.Windows.Data.Binding("ActualHeight")
		{
			Source = PageBase.Win,
			Converter = new Ui.WindowPctConverter(pct),
			ConverterParameter = floor
		});
	}

	private double _target = -1.0;

	public double Offset
	{
		get
		{
			return (double)GetValue(OffsetProperty);
		}
		set
		{
			SetValue(OffsetProperty, value);
		}
	}

	public SmoothScroll()
	{
		base.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
		base.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
		base.CanContentScroll = false;
		base.PanningMode = PanningMode.VerticalOnly;
	}

	protected override void OnMouseWheel(MouseWheelEventArgs e)
	{
		if (!Motion.Enabled || base.ScrollableHeight <= 0.0)
		{
			base.OnMouseWheel(e);
			return;
		}
		e.Handled = true;
		double toValue = (_target = Clamp.Of(((_target < 0.0) ? base.VerticalOffset : _target) - (double)e.Delta * 0.95, 0.0, base.ScrollableHeight));
		DoubleAnimation doubleAnimation = new DoubleAnimation(base.VerticalOffset, toValue, TimeSpan.FromMilliseconds(230.0))
		{
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		doubleAnimation.Completed += delegate
		{
			_target = -1.0;
		};
		BeginAnimation(OffsetProperty, doubleAnimation);
	}
}
