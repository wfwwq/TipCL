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
			Source = AppServices.Window,
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
		WheelBy(e.Delta);
	}

	/// <summary>
	/// [layout-fix] 规格 §3-问题3:把平滑滚动抽成公开方法,供外层容器被转交滚轮时走同一套动画
	/// (LayoutShellHooks 判定"最内层滚不动 → 转交外层第一个还能滚的"之后,直接调这里)。
	/// </summary>
	public void WheelBy(int delta)
	{
		if (!Motion.Enabled || base.ScrollableHeight <= 0.0)
		{
			// 自己没有可滚高度时也给出一点反馈,但不做动画(外层要的就是"照做一次")
			ScrollToVerticalOffset(Clamp.Of(base.VerticalOffset - (double)delta * 0.4, 0.0, Math.Max(0.0, base.ScrollableHeight)));
			return;
		}
		double toValue = (_target = Clamp.Of(((_target < 0.0) ? base.VerticalOffset : _target) - (double)delta * 0.95, 0.0, base.ScrollableHeight));
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
