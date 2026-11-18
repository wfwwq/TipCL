// 新增文件(R32-LV):Windows 版 TipCL 自有控件 SPanel + 它的 V-1 增量(FillChild)在 Linux 树的等价物。
// 来源: Windows 版参考仓库 windos/src/TipCL/Services/SPanel.cs(L1 91 行,含 V-1 的 FillChild / HeightFor)。
// 为什么必须新写:Linux 树里没有 SPanel(页面一律用 Avalonia 的 StackPanel + Spacing),
//   而 V-1 修复("页面主体块在非编辑态吃掉剩余高度",插件页空状态垂直居中)靠的正是 SPanel.FillChild
//   (Windows LayoutEditor.Render 里 `_list.FillChild = ...`;SPanel.cs:19 的属性 + ArrangeOverride 的 HeightFor)。
//   照 R32-WIN-LAYOUT-SPEC §2.8 的建议方案 1:给 Avalonia 版写一个 FillPanel(Panel + Orientation + Spacing + FillChild)。
// 调用面(2 处,与 Windows 逐条对应):
//   1) 布局宿主的纵向块列表(`LayoutEditor._list`):FillChild = 主体块(挂了 LayoutFill.FillsBody 且正好最后一块),
//      其余情况 null ⇒ 排布与改动前逐像素一致;
//   2) 任何需要"顶上 Auto 行 + 主体 * 行"语义、又不想用 Grid 固定行数的地方(块列表是动态的,Grid 行数不固定)。
// WPF → Avalonia 11 映射(本文件):
//   public sealed class SPanel : Panel → FillPanel : Panel(Avalonia.Controls.Panel 存在)
//   Panel.InternalChildren            → Panel.Children
//   UIElement                         → Control
//   Orientation(Vertical/Horizontal)  → 同名(Avalonia.Layout.Orientation)
//   child.Visibility != Visibility.Collapsed → child.IsVisible(Avalonia 的 IsVisible=false 即 Collapsed 语义)
//   child.Measure(Size) / child.Arrange(Rect) / DesiredSize → 同名
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace TipCL.App.Pages.Layout;

/// <summary>
/// 纵向 / 横向的简易排布面板(Windows 版 <c>SPanel</c> 的 Avalonia 等价物:Orientation + Spacing),
/// 外加 V-1 的 <see cref="FillChild"/>。
/// </summary>
public sealed class FillPanel : Panel
{
	/// <summary>排布方向(缺省纵向,与 Windows 版 SPanel 一致)。</summary>
	public Orientation Orientation { get; set; } = Orientation.Vertical;

	/// <summary>子元素间距(缺省 8,与 Windows 版 SPanel 一致)。</summary>
	public double Spacing { get; set; } = 8.0;

	/// <summary>
	/// [layout-fix] V-1:吃掉"剩余高度"的那个子元素(纵向排布;必须是**最后一个可见子元素**)。
	/// 它按 <c>max(自身期望高度, 剩余高度)</c> 排 —— 内容本来就不足一屏时(例如插件页的空状态占位)
	/// 它把剩下的高度全占掉,等价于改版前页面根 Grid 的 <c>Auto,*</c> 第二行。
	/// null(默认)= 与改动前逐像素一致,所有没设它的地方都不受影响。
	/// </summary>
	public Control? FillChild { get; set; }

	/// <summary>
	/// [layout-fix 第三批] 有界填充:为 true 且纵向可用高度有限时,<see cref="FillChild"/> 可以在**任意位置**,
	/// 先量其它子元素(无限高),它拿"可用高 − 其它子元素 − 间距"(≥0)并且**按这个有限高度测量** ——
	/// 语义等价 Grid 的 <c>Auto,*,Auto</c>。自带滚动条的内容(AI 对话区)靠它保住自身滚动与吸底。
	/// 默认 false = 原有行为(FillChild 只按 max(期望, 剩余) 排,且必须是最后一块)。
	/// </summary>
	public bool BoundFill { get; set; }

	// 有界填充这一趟算出来的填充高度(Measure 写、Arrange 读)
	private double _boundFillHeight = -1.0;

	protected override Size MeasureOverride(Size available)
	{
		_boundFillHeight = -1.0;
		if (BoundFill && Orientation == Orientation.Vertical && FillChild != null && FillChild.IsVisible
			&& Children.Contains(FillChild) && !double.IsInfinity(available.Height))
		{
			return MeasureBoundFill(available);
		}
		double num = 0.0;
		double num2 = 0.0;
		int num3 = 0;
		Size availableSize = ((Orientation == Orientation.Vertical) ? new Size(available.Width, double.PositiveInfinity) : new Size(double.PositiveInfinity, available.Height));
		foreach (Control child in Children)
		{
			child.Measure(availableSize);
			if (child.IsVisible)
			{
				num3++;
				if (Orientation == Orientation.Vertical)
				{
					num = Math.Max(num, child.DesiredSize.Width);
					num2 += child.DesiredSize.Height;
				}
				else
				{
					num2 = Math.Max(num2, child.DesiredSize.Height);
					num += child.DesiredSize.Width;
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
		// 与 Windows 版逐字相同:横向分支里 Math.Min(num, Math.Max(num, available.Width)) 恒等于 num,
		// 纵向分支的 num2 同理 —— 期望尺寸就是内容尺寸(宿主 Arrange 时再按 final 宽/高拉满)。照抄不改写。
		return new Size(double.IsInfinity(available.Width) ? num : Math.Min(num, Math.Max(num, available.Width)), double.IsInfinity(available.Height) ? num2 : num2);
	}

	// [layout-fix 第三批] 有界填充的测量:其它子元素按无限高量,填充块拿剩余高度并按它测量
	private Size MeasureBoundFill(Size available)
	{
		double width = 0.0;
		double used = 0.0;
		int visible = 0;
		Size open = new Size(available.Width, double.PositiveInfinity);
		foreach (Control child in Children)
		{
			if (ReferenceEquals(child, FillChild))
			{
				continue;
			}
			child.Measure(open);
			if (child.IsVisible)
			{
				visible++;
				width = Math.Max(width, child.DesiredSize.Width);
				used += child.DesiredSize.Height;
			}
		}
		used += visible * Spacing;
		_boundFillHeight = Math.Max(0.0, available.Height - used);
		FillChild!.Measure(new Size(available.Width, _boundFillHeight));
		width = Math.Max(width, FillChild.DesiredSize.Width);
		return new Size(double.IsInfinity(available.Width) ? width : Math.Min(width, Math.Max(width, available.Width)), used + _boundFillHeight);
	}

	protected override Size ArrangeOverride(Size final)
	{
		if (_boundFillHeight >= 0.0 && FillChild != null && Orientation == Orientation.Vertical)
		{
			double y = 0.0;
			foreach (Control child in Children)
			{
				if (!child.IsVisible)
				{
					continue;
				}
				double h = (ReferenceEquals(child, FillChild) ? _boundFillHeight : child.DesiredSize.Height);
				child.Arrange(new Rect(0.0, y, final.Width, h));
				y += h + Spacing;
			}
			return final;
		}
		double num = 0.0;
		foreach (Control child in Children)
		{
			if (child.IsVisible)
			{
				if (Orientation == Orientation.Vertical)
				{
					child.Arrange(new Rect(0.0, num, final.Width, HeightFor(child, num, final.Height)));
					num += child.DesiredSize.Height + Spacing;
				}
				else
				{
					child.Arrange(new Rect(num, 0.0, child.DesiredSize.Width, final.Height));
					num += child.DesiredSize.Width + Spacing;
				}
			}
		}
		return final;
	}

	/// <summary>
	/// 纵向排布时某个子元素拿到多高:默认 = 它自己的期望高度(与改动前完全一致);
	/// 只有 <see cref="FillChild"/> 那个子元素会把内容区剩下的高度也吃掉(见该属性的说明)。
	/// </summary>
	private double HeightFor(Control child, double usedBefore, double finalHeight)
	{
		if (!ReferenceEquals(child, FillChild))
		{
			return child.DesiredSize.Height;
		}
		return Math.Max(child.DesiredSize.Height, finalHeight - usedBefore);
	}
}
