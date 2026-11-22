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
using UiF = TipCL.App.Ui.Ui;

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

	// —————————— [R35 lin-l12] L0 的资源键(R35-BRIEF §3 第 1 条):查得到就用,查不到用兜底,绝不抛异常 ——————————

	/// <summary>资源是否存在(L0 并行在加 B.Edit* / Geo.* / Layout.* 这些键,合并前多半查不到)。</summary>
	public static bool HasResource(string key)
	{
		try
		{
			return UiF.Res(key) != null;
		}
		catch (Exception)
		{
			return false;
		}
	}

	/// <summary>挑一个存在的资源键:<paramref name="key"/> 在就用它,否则用 <paramref name="fallbackKey"/>。</summary>
	public static string KeyOr(string key, string fallbackKey)
	{
		return HasResource(key) ? key : fallbackKey;
	}

	/// <summary>按当前主题解析一把画刷:键 → 兜底键 → 兜底颜色(给频繁切换的状态色用,不挂订阅)。</summary>
	public static IBrush BrushOr(string key, string? fallbackKey, Color fallback)
	{
		try
		{
			if (UiF.Res(key) is IBrush brush)
			{
				return brush;
			}
			if (fallbackKey != null && UiF.Res(fallbackKey) is IBrush second)
			{
				return second;
			}
		}
		catch (Exception)
		{
			// 资源字典正在换主题:落到兜底颜色
		}
		return new SolidColorBrush(fallback);
	}

	/// <summary>L0 的 ControlTheme(Layout.EditBar / Layout.EditLabel / Layout.Drawer / Layout.IconAct …)存在就套上。</summary>
	public static bool TryApplyTheme(StyledElement target, string key)
	{
		try
		{
			if (UiF.Res(key) is Avalonia.Styling.ControlTheme theme && theme.TargetType != null && theme.TargetType.IsInstanceOfType(target))
			{
				target.Theme = theme;
				return true;
			}
		}
		catch (Exception)
		{
		}
		return false;
	}

	/// <summary>
	/// 按 16 单位视窗画一枚描边图标(颜色由调用方绑定 Stroke)。
	/// [R35] <paramref name="geoKey"/> = L0 的 Geo.* 几何资源键;查得到就用它的副本(资源本身不改),查不到用 <paramref name="data"/>。
	/// </summary>
	public static Path MakePath(string data, double size, string? geoKey = null)
	{
		Geometry geometry = ResolveGeometry(geoKey) ?? Geometry.Parse(data);
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

	private static Geometry? ResolveGeometry(string? geoKey)
	{
		if (string.IsNullOrEmpty(geoKey))
		{
			return null;
		}
		try
		{
			return (UiF.Res(geoKey!) as Geometry)?.Clone();
		}
		catch (Exception)
		{
			return null;
		}
	}

	/// <summary>一枚跟随主题色键的描边图标(不在按钮里用时)。</summary>
	public static Path Icon(string data, string colorKey, double size, string? geoKey = null)
	{
		Path path = MakePath(data, size, geoKey);
		path.Dyn(Shape.StrokeProperty, colorKey);
		return path;
	}

	/// <summary>
	/// 图标按钮:外形沿用 U.IconBtn(Btn.Icon 主题、ToolTip、无障碍名),内容换成本文件画的图标,
	/// 描边跟随按钮前景色(hover / 禁用态自动变色,与 UiHelpers.IconBtn 同一写法)。
	/// </summary>
	public static Button IconButton(string data, string tip, EventHandler<RoutedEventArgs>? click, double size = 15.0, string? geoKey = null)
	{
		Button button = U.IconBtn("Info", tip, click, size);
		button.Content = BoundIcon(button, data, size, geoKey);
		return button;
	}

	/// <summary>放进某个按钮里的图标:描边跟随该按钮的前景色。</summary>
	public static Path BoundIcon(Button owner, string data, double size, string? geoKey = null)
	{
		Path path = MakePath(data, size, geoKey);
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

	/// <summary>
	/// 浮在卡片左上 / 右上的白色小底板(块名、操作按钮共用的外观)。
	/// [R35] 底色读 L0 的 B.EditBarBg(兜底 B.Paper);L0 的 Layout.EditLabel ControlTheme 存在时整套换成它。
	/// [R35 lin-int] 套上主题后**不再写**圆角 / 边框 / 内距 / 阴影 / 底色这些本地值(本地值优先级高于主题,会把主题盖掉);
	/// <paramref name="themeKey"/> = null 时只走兜底外观。
	/// </summary>
	public static Border Chip(Control child, Thickness padding, string? themeKey = LayoutVisualTokens.ThemeKeys.EditLabel)
	{
		Border chip = new Border { Child = child };
		if (themeKey != null && LayoutVisualTokens.ApplyTheme(chip, themeKey))
		{
			return chip;
		}
		chip.CornerRadius = new CornerRadius(8.0);
		chip.BorderThickness = new Thickness(1.0);
		chip.Padding = padding;
		chip.BoxShadow = new BoxShadows(new BoxShadow
		{
			Blur = 8.0,
			OffsetY = 2.0,
			Color = Color.FromArgb(30, 0, 0, 0)
		});
		chip.Dyn(Border.BackgroundProperty, KeyOr("B.EditBarBg", "B.Paper"));
		chip.Dyn(Border.BorderBrushProperty, "B.Line");
		return chip;
	}

	/// <summary>
	/// [R35 lin-int] 文字套 L0 的 TextBlock 主题(Layout.EditLabelText / EditHint / DrawerTitle …):
	/// 主题在就只设 Text + Theme(字号 / 字重 / 颜色全交给主题),不在就退回 <c>U.Txt(text, size, bold, fg)</c>。
	/// </summary>
	public static TextBlock ThemedText(string text, string themeKey, double size, bool bold = false, string? fg = null, bool onDark = false)
	{
		TextBlock block = new TextBlock { Text = text };
		if (LayoutVisualTokens.ApplyTheme(block, themeKey, onDark))
		{
			return block;
		}
		return U.Txt(text, size, bold, fg);
	}

	/// <summary>[R35 lin-int] 编辑条 / 抽屉里的描边图标:有 L0 几何就用 <see cref="LayoutVisualTokens.Glyph"/>(线宽按效果图 1.6×size/16),否则旧路径。</summary>
	public static Path GlyphFor(Button owner, string geoKey, string data, double size)
	{
		if (HasResource(geoKey))
		{
			return LayoutVisualTokens.Glyph(geoKey, size, owner);
		}
		return BoundIcon(owner, data, size);
	}
}
