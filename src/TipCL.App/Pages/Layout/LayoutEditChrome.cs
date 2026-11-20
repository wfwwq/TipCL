// v3(自定义布局 v3 效果图 · 编辑态,Linux 对等落地,代理 C):编辑态外壳用到的小零件。
//   * 图标:撤销 / 重做 / 眼睛(隐藏)/ 列表 / 网格 / 分隔线 —— 全站 IconLib(Common/IconLib.cs,不归本包)里没有这几枚,
//     这里按效果图 tmp/layout3_noimg.html 里的 SVG 路径(16 单位视窗)就地画,画法与 IconLib.Make 一致
//     (1.4 线宽圆头描边、按 16 单位整体等比缩放,不用 Stretch 撑满);
//   * 页名:编辑条标题「编辑「<页名>」页」/ 抽屉标题「添加到「<页名>」页」用的显示名(来自 LayoutBlockEntryCatalog)。
// 只放纯装配代码,不碰布局状态。
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.App.Ui;
using TipCL.Core.Launcher;

namespace TipCL.App.Pages.Layout;

/// <summary>编辑态外壳的图标与小部件(v3)。</summary>
internal static class LayoutEditChrome
{
	public const string UndoData = "M5.5,3.5 L2.5,6.5 L5.5,9.5 M2.5,6.5 H9.5 A4,4 0 0 1 9.5,14.5 H8";

	public const string RedoData = "M10.5,3.5 L13.5,6.5 L10.5,9.5 M13.5,6.5 H6.5 A4,4 0 0 0 6.5,14.5 H8";

	public const string EyeOffData = "M2,2 L14,14 M6.6,3.7 A6.6,6.6 0 0 1 8,3.5 C12,3.5 14.5,8 14.5,8 A12,12 0 0 1 12.5,10.6 M10.1,10.2 A3,3 0 0 1 8,11 C4,11 1.5,8 1.5,8 A12,12 0 0 1 4.4,4.8";

	public const string ListModeData = "M2.5,2.5 H13.5 V6.5 H2.5 Z M2.5,9.5 H13.5 V13.5 H2.5 Z";

	public const string GridModeData = "M2.5,2.5 H13.5 V6.5 H2.5 Z M2.5,9.5 H9 V13.5 H2.5 Z M11,9.5 H13.5 V13.5 H11 Z";

	public const string LineData = "M2,8 H14";

	/// <summary>按 16 单位视窗画一枚描边图标(颜色由调用方绑定 Stroke)。</summary>
	public static Path MakePath(string data, double size)
	{
		Geometry geometry = Geometry.Parse(data);
		if (Math.Abs(size - 16.0) > 0.01)
		{
			geometry.Transform = new ScaleTransform(size / 16.0, size / 16.0);
		}
		return new Path
		{
			Data = geometry,
			Width = size,
			Height = size,
			StrokeThickness = 1.4,
			StrokeLineCap = PenLineCap.Round,
			StrokeJoin = PenLineJoin.Round,
			VerticalAlignment = VerticalAlignment.Center
		};
	}

	/// <summary>一枚跟随主题色键的描边图标(不在按钮里用时)。</summary>
	public static Path Icon(string data, string colorKey, double size)
	{
		Path path = MakePath(data, size);
		path.Dyn(Shape.StrokeProperty, colorKey);
		return path;
	}

	/// <summary>
	/// 图标按钮:外形沿用 U.IconBtn(Btn.Icon 主题、ToolTip、无障碍名),内容换成本文件画的图标,
	/// 描边跟随按钮前景色(hover / 禁用态自动变色,与 UiHelpers.IconBtn 同一写法)。
	/// </summary>
	public static Button IconButton(string data, string tip, EventHandler<RoutedEventArgs>? click, double size = 15.0)
	{
		Button button = U.IconBtn("Info", tip, click, size);
		button.Content = BoundIcon(button, data, size);
		return button;
	}

	/// <summary>放进某个按钮里的图标:描边跟随该按钮的前景色。</summary>
	public static Path BoundIcon(Button owner, string data, double size)
	{
		Path path = MakePath(data, size);
		path[!Shape.StrokeProperty] = owner.GetObservable(Button.ForegroundProperty).ToBinding();
		return path;
	}

	/// <summary>
	/// 页面显示名(编辑条 / 抽屉标题用):入口清单里有就用清单里的名字,没有返回 null(调用方换成「本页」的说法)。
	/// </summary>
	public static string? PageName(string? pageId)
	{
		LayoutBlockEntryCatalog.Entry? entry = LayoutBlockEntryCatalog.Find(pageId);
		if (entry == null || string.IsNullOrWhiteSpace(entry.Label))
		{
			return null;
		}
		return entry.Label;
	}

	/// <summary>编辑条标题:「编辑「下载」页」;不在清单里的页 →「编辑本页」。</summary>
	public static string EditTitle(string? pageId)
	{
		string? name = PageName(pageId);
		return (name == null) ? I18nCore.Tr("编辑本页") : string.Format(I18nCore.Tr("编辑「{0}」页"), name);
	}

	/// <summary>添加抽屉标题:「添加到「下载」页」;不在清单里的页 →「添加到本页」。</summary>
	public static string DrawerTitle(string? pageId)
	{
		string? name = PageName(pageId);
		return (name == null) ? I18nCore.Tr("添加到本页") : string.Format(I18nCore.Tr("添加到「{0}」页"), name);
	}

	/// <summary>浮在卡片左上 / 右上的白色小底板(块名、操作按钮共用的外观)。</summary>
	public static Border Chip(Control child, Thickness padding)
	{
		Border chip = new Border
		{
			CornerRadius = new CornerRadius(8.0),
			BorderThickness = new Thickness(1.0),
			Padding = padding,
			Child = child,
			BoxShadow = new BoxShadows(new BoxShadow
			{
				Blur = 8.0,
				OffsetY = 2.0,
				Color = Color.FromArgb(30, 0, 0, 0)
			})
		};
		chip.Dyn(Border.BackgroundProperty, "B.Paper");
		chip.Dyn(Border.BorderBrushProperty, "B.Line");
		return chip;
	}
}
