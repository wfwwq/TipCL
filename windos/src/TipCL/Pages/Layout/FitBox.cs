using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TipCL.Pages.Layout;

/// <summary>
/// 窄宽等比缩放容器:内容自然高度不超过 <see cref="MaxNaturalHeight"/>(标题栏、筛选条、页签条这类矮内容)
/// 且可用宽不够时,整体等比缩小塞进去(最小 55%),而不是裁掉右半截或把标题挤成竖排。
/// 高内容(列表)不缩放 —— 列表按宽度重新排版才是对的。
/// <see cref="MaxNaturalHeight"/> ≤ 0 时是纯透传(等价于没有这一层):纵向流(stack)与"占满高度"的区块走这条路,
/// 常态渲染因此与改动前逐像素一致。
/// </summary>
public sealed class FitBox : Decorator
{
	private double _minScale = 0.55;

	private double _maxNaturalHeight = 170.0;

	/// <summary>缩放下限(0.55 = 最窄缩到 55%)。</summary>
	public double MinScale
	{
		get
		{
			return _minScale;
		}
		set
		{
			// 改了缩放参数必须重新量(否则要等下一次无关的布局失效才生效)
			if (Math.Abs(value - _minScale) < 1e-9)
			{
				return;
			}
			_minScale = value;
			InvalidateMeasure();
		}
	}

	/// <summary>只有"矮内容"才缩放:自然高度超过这个值的(列表等)照常按宽度排版;≤ 0 = 不缩放(纯透传)。</summary>
	public double MaxNaturalHeight
	{
		get
		{
			return _maxNaturalHeight;
		}
		set
		{
			if (Math.Abs(value - _maxNaturalHeight) < 1e-9)
			{
				return;
			}
			_maxNaturalHeight = value;
			InvalidateMeasure();
		}
	}

	private double _scale = 1.0;

	// 我们自己挂上去的缩放变换(只在缩放时挂;内容原有的 RenderTransform 记在 _original,不缩放时原样还回去)
	private Transform? _applied;

	private Transform? _original;

	// 内容自己的 RenderTransformOrigin:我们缩放期间会把它临时设成 (0,0),撤销时必须还回去(否则恢复 1.0 后原点永久变成 (0,0))
	private Point _originalOrigin;

	private bool _originSaved;

	public double Scale => _scale;

	protected override Size MeasureOverride(Size available)
	{
		UIElement? child = Child;
		if (child == null)
		{
			return default(Size);
		}
		_scale = 1.0;
		if (MaxNaturalHeight <= 0.0)
		{
			// 透传:与 Decorator 默认行为相同(宽度照常拉伸,高度约束原样下传)
			child.Measure(available);
			return child.DesiredSize;
		}
		double width = ((double.IsInfinity(available.Width) || available.Width <= 0.0) ? 900.0 : available.Width);
		child.Measure(new Size(width, double.PositiveInfinity));
		Size want = child.DesiredSize;
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
		if (Math.Abs(_scale - 1.0) < 1e-6)
		{
			RestoreTransform(child);
			// 不缩放:按最终尺寸排(宽度拉伸到格子宽 —— 原来按 DesiredSize 排,卡片会缩成内容宽)
			child.Arrange(new Rect(0.0, 0.0, final.Width, Math.Max(final.Height, (MaxNaturalHeight <= 0.0) ? 0.0 : child.DesiredSize.Height)));
			return final;
		}
		Size want = child.DesiredSize;
		child.Arrange(new Rect(0.0, 0.0, want.Width, want.Height));
		if (_applied == null || !ReferenceEquals(child.RenderTransform, _applied))
		{
			// 内容自己的变换(RenderTransform)不丢:它被原样放进 TransformGroup 的最里层,
			// 悬停上浮 / 淡入位移这类动画作用的还是同一个变换实例,照常生效;我们只在外层再加一层等比缩放。
			// 原点在同一次缩放期间只记一次(内容中途换了变换也不覆盖已经记下的原点)。
			_original = child.RenderTransform;
			if (!_originSaved)
			{
				_originalOrigin = child.RenderTransformOrigin;
				_originSaved = true;
			}
		}
		TransformGroup group = new TransformGroup();
		if (_original != null && !(_original is MatrixTransform { Matrix: { IsIdentity: not false } }))
		{
			group.Children.Add(_original);
		}
		group.Children.Add(new ScaleTransform(_scale, _scale));
		// 缩放以内容左上角为原点(外层缩放的语义);原值已记下,恢复时还原
		child.RenderTransformOrigin = new Point(0.0, 0.0);
		child.RenderTransform = group;
		_applied = group;
		return new Size(Math.Min(final.Width, want.Width * _scale), want.Height * _scale);
	}

	// 只撤销我们自己挂的缩放;内容自己的变换(悬停上浮、淡入位移)一概不碰
	private void RestoreTransform(UIElement child)
	{
		if (_applied == null)
		{
			_originSaved = false;
			return;
		}
		if (ReferenceEquals(child.RenderTransform, _applied))
		{
			child.RenderTransform = _original ?? Transform.Identity;
		}
		// 只还原我们自己设成 (0,0) 的原点;内容在我们之后自己改过就尊重它的值
		if (_originSaved && child.RenderTransformOrigin == new Point(0.0, 0.0))
		{
			child.RenderTransformOrigin = _originalOrigin;
		}
		_applied = null;
		_original = null;
		_originSaved = false;
	}
}
