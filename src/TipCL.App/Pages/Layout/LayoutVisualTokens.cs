// 布局可视化层共用的设计令牌入口(R32-LV 起本包自有;R35 lin-l0 扩成布局 v3 编辑态的令牌门面)。
//
// 历史:GridCanvasPanel / GuideLayer 只要一个主题色 —— Windows 版这两处直接调 LayoutEditor.AccentColor()
// (读资源 "B.Accent",拿不到就回落到 Color.FromRgb(46,155,107));Linux 树的 LayoutEditor.cs 属另一工作包,
// 本文件不引用它的内部成员,故把这一个纯函数抽到这里 —— 取值口径与 Windows 版逐字一致(AccentColor 保持原样)。
//
// R35 lin-l0(布局 v3 · WP-L0,2026-11-20):
//   * 资源键(画刷 B.Edit* / 阴影 Fx.* / 几何 Geo.* / 主题 Layout.*)真身在 Themes/Palette.Light|Dark.axaml 与
//     Themes/Controls.axaml;本文件只放**键名常量 + 尺寸常量 + 找不到资源时的兜底值 + 取用小工具**,
//     让 lin-l12 / lin-l4 / lin-l5 能按 R35-BRIEF §3「跨包接口」第 1 条「TryGetResource + 代码内兜底、找不到不抛异常」来消费。
//   * 兜底值与 Palette 里的值逐字相同(LayoutV3TokensTests 锁住),所以资源没挂上(单测宿主 / 早期构造)时外观也不走样。
//   * 键表与用法见 docs/layout-v3-tokens.md。
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using TipCL.App.Ui;

namespace TipCL.App.Pages.Layout;

/// <summary>布局可视化层(自绘 Panel / 辅助线层 / v3 编辑态外壳)要用的设计令牌与取用工具。</summary>
internal static class LayoutVisualTokens
{
	/// <summary>主题强调色(资源缺失时的兜底色与 Windows 版相同)。</summary>
	public static Color AccentColor()
	{
		if (U.Brush("B.Accent") is SolidColorBrush brush)
		{
			return brush.Color;
		}
		return Color.FromRgb(46, 155, 107);
	}

	// ───────────────────────────── 键名(与 Palette / Controls.axaml 的 x:Key 逐字一致) ─────────────────────────────

	/// <summary>画刷键(Palette.Light / Palette.Dark 成对)。前 9 个是 R35-BRIEF §3 跨包接口第 1 条的契约键。</summary>
	public static class BrushKeys
	{
		/// <summary>块的编辑态虚线框(常态灰)。</summary>
		public const string EditOutline = "B.EditOutline";
		/// <summary>虚线框悬停加深。</summary>
		public const string EditOutlineHover = "B.EditOutlineHover";
		/// <summary>深色背景(启动页)上的白色半透明虚线框。</summary>
		public const string EditOutlineOnDark = "B.EditOutlineOnDark";
		/// <summary>拖动落点框的主题色浅底。</summary>
		public const string EditDropFill = "B.EditDropFill";
		/// <summary>拖动 / 改宽时的淡色列底纹。</summary>
		public const string EditColShade = "B.EditColShade";
		/// <summary>编辑条、浮动标签、块上图标按钮、抽屉的底色。</summary>
		public const string EditBarBg = "B.EditBarBg";
		/// <summary>深色启动页编辑态的压暗遮罩。</summary>
		public const string EditDim = "B.EditDim";
		/// <summary>琥珀浅底(沿用 v2 已有键)。</summary>
		public const string WarnSoft = "B.WarnSoft";
		/// <summary>琥珀字色。</summary>
		public const string WarnInk = "B.WarnInk";

		// —— 以下为扩展键(契约外,同样成对;与 Windows r34/l0 同名同值) ——
		public const string EditOutlineHoverOnDark = "B.EditOutlineHoverOnDark";
		public const string EditDropFillOnDark = "B.EditDropFillOnDark";
		public const string EditColShadeOnDark = "B.EditColShadeOnDark";
		/// <summary>编辑条 / 标签 / 图标按钮 / 抽屉的 1px 细描边。</summary>
		public const string EditBarLine = "B.EditBarLine";
		/// <summary>抽屉里每一项、说明框的灰底。</summary>
		public const string EditItemBg = "B.EditItemBg";
		/// <summary>「列表 / 网格」分段的轨道底。</summary>
		public const string EditSegRail = "B.EditSegRail";
		/// <summary>分段选中项的底。</summary>
		public const string EditSegOn = "B.EditSegOn";
		/// <summary>深色玻璃(启动页编辑条 / 标签 / 抽屉)。</summary>
		public const string EditGlass = "B.EditGlass";
		public const string EditGlassLine = "B.EditGlassLine";
		/// <summary>玻璃上的小条(锁形提示、分段轨道)。</summary>
		public const string EditGlassChip = "B.EditGlassChip";
		public const string EditGlassChipLine = "B.EditGlassChipLine";
		/// <summary>玻璃上的白字 / 白色描边。</summary>
		public const string OnEditGlass = "B.OnEditGlass";
		/// <summary>玻璃上的次要白字。</summary>
		public const string OnEditGlassMuted = "B.OnEditGlassMuted";

		/// <summary>R35-BRIEF §3 第 1 条列出的 9 个契约画刷键。</summary>
		public static readonly string[] Contract =
		{
			EditOutline, EditOutlineHover, EditOutlineOnDark, EditDropFill, EditColShade,
			EditBarBg, EditDim, WarnSoft, WarnInk,
		};

		/// <summary>本包新增 / 认领的全部画刷键(契约 + 扩展)。</summary>
		public static readonly string[] All =
		{
			EditOutline, EditOutlineHover, EditOutlineOnDark, EditOutlineHoverOnDark,
			EditDropFill, EditDropFillOnDark, EditColShade, EditColShadeOnDark,
			EditBarBg, EditBarLine, EditItemBg, EditSegRail, EditSegOn, EditDim,
			EditGlass, EditGlassLine, EditGlassChip, EditGlassChipLine, OnEditGlass, OnEditGlassMuted,
			WarnSoft, WarnInk,
		};
	}

	/// <summary>阴影键(BoxShadows,Palette 成对;Avalonia 用 Border.BoxShadow 表达 Windows 的 DropShadowEffect)。</summary>
	public static class ShadowKeys
	{
		public const string EditBar = "Fx.EditBar";
		public const string EditLabel = "Fx.EditLabel";
		public const string Drawer = "Fx.Drawer";
		public const string SegOn = "Fx.SegOn";
		public const string ResizeGrip = "Fx.ResizeGrip";

		public static readonly string[] All = { EditBar, EditLabel, Drawer, SegOn, ResizeGrip };
	}

	/// <summary>矢量图标键(Controls.axaml 里的 StreamGeometry,16×16 视框、描边绘制)。前 6 个是契约键。</summary>
	public static class GeoKeys
	{
		public const string Eye = "Geo.Eye";
		public const string EyeOff = "Geo.EyeOff";
		public const string Lock = "Geo.Lock";
		public const string Undo = "Geo.Undo";
		public const string Redo = "Geo.Redo";
		public const string Grip = "Geo.Grip";
		public const string ListMode = "Geo.ListMode";
		public const string GridMode = "Geo.GridMode";

		public static readonly string[] Contract = { Eye, EyeOff, Lock, Undo, Redo, Grip };

		public static readonly string[] All = { Eye, EyeOff, Lock, Undo, Redo, Grip, ListMode, GridMode };
	}

	/// <summary>ControlTheme 键(Controls.axaml)。前 5 个是契约键;做法 = ControlTheme(赋给 <c>Theme</c>),状态走 Classes。</summary>
	public static class ThemeKeys
	{
		/// <summary>编辑条(Border)。</summary>
		public const string EditBar = "Layout.EditBar";
		/// <summary>浮动标签(Border)。</summary>
		public const string EditLabel = "Layout.EditLabel";
		/// <summary>块上的图标按钮(Button)。</summary>
		public const string IconAct = "Layout.IconAct";
		/// <summary>「添加」抽屉(Border)。</summary>
		public const string Drawer = "Layout.Drawer";
		/// <summary>「列表 / 网格」分段里的一项(ToggleButton / RadioButton)。</summary>
		public const string SegItem = "Layout.SegItem";

		/// <summary>块的虚线框(Rectangle;Classes hover / selected / drag)。</summary>
		public const string EditOutline = "Layout.EditOutline";
		public const string DropBox = "Layout.DropBox";
		public const string ColShade = "Layout.ColShade";
		public const string IconActDanger = "Layout.IconAct.Danger";
		public const string ResizeGrip = "Layout.ResizeGrip";
		public const string ResizeGripBottom = "Layout.ResizeGrip.Bottom";
		public const string EditTip = "Layout.EditTip";
		public const string EditTipText = "Layout.EditTipText";
		public const string EditBarTitle = "Layout.EditBarTitle";
		public const string EditHint = "Layout.EditHint";
		public const string EditBarSep = "Layout.EditBarSep";
		public const string EditLabelText = "Layout.EditLabelText";
		public const string EditLabelNote = "Layout.EditLabelNote";
		public const string Count = "Layout.Count";
		public const string CountText = "Layout.CountText";
		public const string SegRail = "Layout.SegRail";
		public const string DrawerTitle = "Layout.DrawerTitle";
		public const string DrawerSection = "Layout.DrawerSection";
		public const string DrawerItem = "Layout.DrawerItem";
		public const string DrawerIcon = "Layout.DrawerIcon";
		public const string DrawerEmpty = "Layout.DrawerEmpty";
		public const string DrawerNote = "Layout.DrawerNote";
		public const string WarnNote = "Layout.WarnNote";
		public const string WarnText = "Layout.WarnText";
		public const string LockNote = "Layout.LockNote";
		public const string Glyph = "Layout.Glyph";
		public const string GlyphSmall = "Layout.Glyph.Small";
		public const string Grip = "Layout.Grip";

		/// <summary>深色玻璃版本的后缀:<c>OnDark("Layout.EditBar")</c> → <c>"Layout.EditBar.OnDark"</c>。</summary>
		public const string OnDarkSuffix = ".OnDark";

		public static readonly string[] Contract = { EditBar, EditLabel, IconAct, Drawer, SegItem };

		/// <summary>有深色玻璃版本(<c>…OnDark</c>)的主题。</summary>
		public static readonly string[] WithOnDark =
		{
			EditBar, EditLabel, IconAct, IconActDanger, Drawer, SegItem, SegRail, EditOutline, DropBox, ColShade,
			EditBarTitle, EditHint, EditBarSep, EditLabelText, EditLabelNote, Grip,
		};

		public static readonly string[] All =
		{
			EditBar, EditLabel, IconAct, Drawer, SegItem,
			EditOutline, DropBox, ColShade, IconActDanger, ResizeGrip, ResizeGripBottom, EditTip, EditTipText,
			EditBarTitle, EditHint, EditBarSep, EditLabelText, EditLabelNote, Count, CountText, SegRail,
			DrawerTitle, DrawerSection, DrawerItem, DrawerIcon, DrawerEmpty, DrawerNote, WarnNote, WarnText, LockNote,
			Glyph, GlyphSmall, Grip,
		};

		/// <summary>主题键的深色玻璃版本键名。</summary>
		public static string OnDark(string key) => key + OnDarkSuffix;
	}

	/// <summary>虚线框状态 Class(加到套了 <c>Layout.EditOutline</c> 的 Rectangle 上)。</summary>
	public static class OutlineClasses
	{
		public const string Hover = "hover";
		public const string Selected = "selected";
		public const string Drag = "drag";
	}

	// ───────────────────────────── 尺寸(效果图 CSS 原值,与 Controls.axaml 一致) ─────────────────────────────

	/// <summary>编辑条高 46(.ebox)。</summary>
	public const double EditBarHeight = 46.0;
	/// <summary>编辑条与内容区之间的距离 12(#winA.edit .ebar margin-bottom)。</summary>
	public const double EditBarGap = 12.0;
	/// <summary>编辑条内距:左 14 / 右 8。</summary>
	public static readonly Thickness EditBarPadding = new Thickness(14.0, 0.0, 8.0, 0.0);
	/// <summary>浮动标签高 24、离块左 8、上浮 14(.lab)。</summary>
	public const double EditLabelHeight = 24.0;
	public static readonly Thickness EditLabelMargin = new Thickness(8.0, -14.0, 0.0, 0.0);
	/// <summary>块上图标按钮 26×24,按钮间距 4,离块右 8、上浮 14(.acts)。</summary>
	public const double IconActWidth = 26.0;
	public const double IconActHeight = 24.0;
	public const double IconActGap = 4.0;
	public static readonly Thickness IconActsMargin = new Thickness(0.0, -14.0, 8.0, 0.0);
	/// <summary>虚线框外扩 4、常态线宽 1.5、选中线宽 2、虚线 3 2(outline-offset:4px)。</summary>
	public const double OutlineInset = 4.0;
	public const double OutlineThickness = 1.5;
	public const double OutlineSelectedThickness = 2.0;
	public static readonly double[] OutlineDash = { 3.0, 2.0 };
	/// <summary>右边缘手柄 10×36,外伸 9(.rz right:-9px)。</summary>
	public const double ResizeGripWidth = 10.0;
	public const double ResizeGripHeight = 36.0;
	public const double ResizeGripOutset = 9.0;
	/// <summary>「添加」抽屉宽 316(.drawer)。</summary>
	public const double DrawerWidth = 316.0;
	/// <summary>抽屉项图标 40×40(.ditem .di)。</summary>
	public const double DrawerIconSize = 40.0;
	/// <summary>分段项高 24(.sg2 button)。</summary>
	public const double SegItemHeight = 24.0;
	/// <summary>圆角只用两档:控件 8 / 卡片 12。</summary>
	public const double RadiusCtl = 8.0;
	public const double RadiusCard = 12.0;
	/// <summary>启动页编辑态英雄区透明度 45%(.win.edit .hero opacity .45)。</summary>
	public const double HeroEditOpacity = 0.45;
	/// <summary>图标线宽:效果图 svg.i stroke-width 1.6(按 16 视框,随显示尺寸同比缩放)。</summary>
	public const double GlyphStroke16 = 1.6;
	/// <summary>六点拖拽的线宽(按 16 视框;= r 1.1 的圆点)。</summary>
	public const double GripStroke16 = 2.2;
	/// <summary>各图标在效果图里的显示尺寸。</summary>
	public const double GlyphSizeUndoRedo = 15.0;
	public const double GlyphSizeIconAct = 14.0;
	public const double GlyphSizeLock = 13.0;
	public const double GlyphSizeGrip = 12.0;
	public const double GlyphSizeSeg = 13.0;

	// ───────────────────────────── 兜底值(资源没挂上时用;与 Palette 逐字一致) ─────────────────────────────

	/// <summary>浅色兜底(与 Palette.Light.axaml 同值)。</summary>
	public static readonly IReadOnlyDictionary<string, Color> FallbackLight = new Dictionary<string, Color>(StringComparer.Ordinal)
	{
		[BrushKeys.EditOutline] = Color.Parse("#29000000"),
		[BrushKeys.EditOutlineHover] = Color.Parse("#57000000"),
		[BrushKeys.EditOutlineOnDark] = Color.Parse("#59FFFFFF"),
		[BrushKeys.EditOutlineHoverOnDark] = Color.Parse("#B3FFFFFF"),
		[BrushKeys.EditDropFill] = Color.Parse("#E7F5EE"),
		[BrushKeys.EditDropFillOnDark] = Color.Parse("#1FFFFFFF"),
		[BrushKeys.EditColShade] = Color.Parse("#09000000"),
		[BrushKeys.EditColShadeOnDark] = Color.Parse("#0FFFFFFF"),
		[BrushKeys.EditBarBg] = Color.Parse("#FFFFFF"),
		[BrushKeys.EditBarLine] = Color.Parse("#14000000"),
		[BrushKeys.EditItemBg] = Color.Parse("#F2F2F2"),
		[BrushKeys.EditSegRail] = Color.Parse("#E7E7E7"),
		[BrushKeys.EditSegOn] = Color.Parse("#FFFFFF"),
		[BrushKeys.EditDim] = Color.Parse("#800A0E16"),
		[BrushKeys.EditGlass] = Color.Parse("#B80E121B"),
		[BrushKeys.EditGlassLine] = Color.Parse("#24FFFFFF"),
		[BrushKeys.EditGlassChip] = Color.Parse("#24FFFFFF"),
		[BrushKeys.EditGlassChipLine] = Color.Parse("#38FFFFFF"),
		[BrushKeys.OnEditGlass] = Color.Parse("#FFFFFF"),
		[BrushKeys.OnEditGlassMuted] = Color.Parse("#A8FFFFFF"),
		[BrushKeys.WarnSoft] = Color.Parse("#FDF3E2"),
		[BrushKeys.WarnInk] = Color.Parse("#B45309"),
	};

	/// <summary>深色兜底(与 Palette.Dark.axaml 同值)。</summary>
	public static readonly IReadOnlyDictionary<string, Color> FallbackDark = new Dictionary<string, Color>(StringComparer.Ordinal)
	{
		[BrushKeys.EditOutline] = Color.Parse("#2EFFFFFF"),
		[BrushKeys.EditOutlineHover] = Color.Parse("#5CFFFFFF"),
		[BrushKeys.EditOutlineOnDark] = Color.Parse("#59FFFFFF"),
		[BrushKeys.EditOutlineHoverOnDark] = Color.Parse("#B3FFFFFF"),
		[BrushKeys.EditDropFill] = Color.Parse("#1B3A2C"),
		[BrushKeys.EditDropFillOnDark] = Color.Parse("#1FFFFFFF"),
		[BrushKeys.EditColShade] = Color.Parse("#0AFFFFFF"),
		[BrushKeys.EditColShadeOnDark] = Color.Parse("#0FFFFFFF"),
		[BrushKeys.EditBarBg] = Color.Parse("#2B2B2B"),
		[BrushKeys.EditBarLine] = Color.Parse("#1AFFFFFF"),
		[BrushKeys.EditItemBg] = Color.Parse("#313131"),
		[BrushKeys.EditSegRail] = Color.Parse("#1E1E1E"),
		[BrushKeys.EditSegOn] = Color.Parse("#3A3A3A"),
		[BrushKeys.EditDim] = Color.Parse("#800A0E16"),
		[BrushKeys.EditGlass] = Color.Parse("#B80E121B"),
		[BrushKeys.EditGlassLine] = Color.Parse("#24FFFFFF"),
		[BrushKeys.EditGlassChip] = Color.Parse("#24FFFFFF"),
		[BrushKeys.EditGlassChipLine] = Color.Parse("#38FFFFFF"),
		[BrushKeys.OnEditGlass] = Color.Parse("#FFFFFF"),
		[BrushKeys.OnEditGlassMuted] = Color.Parse("#A8FFFFFF"),
		[BrushKeys.WarnSoft] = Color.Parse("#3A2F1C"),
		[BrushKeys.WarnInk] = Color.Parse("#EBB45E"),
	};

	/// <summary>阴影兜底(浅色档,与 Palette.Light.axaml 同值;深色档见 <see cref="FallbackShadowDark"/>)。</summary>
	public static readonly IReadOnlyDictionary<string, string> FallbackShadowLight = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		[ShadowKeys.EditBar] = "0 1 3 0 #0F000000",
		[ShadowKeys.EditLabel] = "0 2 8 0 #1F000000",
		[ShadowKeys.Drawer] = "-12 0 32 0 #24000000",
		[ShadowKeys.SegOn] = "0 1 2 0 #14000000",
		[ShadowKeys.ResizeGrip] = "0 1 4 0 #33000000",
	};

	public static readonly IReadOnlyDictionary<string, string> FallbackShadowDark = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		[ShadowKeys.EditBar] = "0 2 10 0 #59000000",
		[ShadowKeys.EditLabel] = "0 2 8 0 #66000000",
		[ShadowKeys.Drawer] = "-12 0 32 0 #80000000",
		[ShadowKeys.SegOn] = "0 1 2 0 #4D000000",
		[ShadowKeys.ResizeGrip] = "0 1 4 0 #80000000",
	};

	/// <summary>几何兜底(与 Controls.axaml 的 StreamGeometry 同一串路径)。</summary>
	public static readonly IReadOnlyDictionary<string, string> FallbackGeometry = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		[GeoKeys.Eye] = "M1.5,8 C1.5,8 4,3.5 8,3.5 C12,3.5 14.5,8 14.5,8 C14.5,8 12,12.5 8,12.5 C4,12.5 1.5,8 1.5,8 Z M10,8 A2,2 0 1 1 6,8 A2,2 0 1 1 10,8 Z",
		[GeoKeys.EyeOff] = "M2,2 L14,14 M6.6,3.7 A6.6,6.6 0 0 1 8,3.5 C12,3.5 14.5,8 14.5,8 A12,12 0 0 1 12.5,10.6 M10.1,10.2 A3,3 0 0 1 8,11 C4,11 1.5,8 1.5,8 A12,12 0 0 1 4.4,4.8",
		[GeoKeys.Lock] = "M5,7 L11,7 A1.5,1.5 0 0 1 12.5,8.5 L12.5,12 A1.5,1.5 0 0 1 11,13.5 L5,13.5 A1.5,1.5 0 0 1 3.5,12 L3.5,8.5 A1.5,1.5 0 0 1 5,7 Z M5.5,7 L5.5,5 A2.5,2.5 0 0 1 10.5,5 L10.5,7",
		[GeoKeys.Undo] = "M5.5,3.5 L2.5,6.5 L5.5,9.5 M2.5,6.5 L9.5,6.5 A4,4 0 0 1 9.5,14.5 L8,14.5",
		[GeoKeys.Redo] = "M10.5,3.5 L13.5,6.5 L10.5,9.5 M13.5,6.5 L6.5,6.5 A4,4 0 0 0 6.5,14.5 L8,14.5",
		[GeoKeys.Grip] = "M6,3.95 L6,4.05 M10,3.95 L10,4.05 M6,7.95 L6,8.05 M10,7.95 L10,8.05 M6,11.95 L6,12.05 M10,11.95 L10,12.05",
		[GeoKeys.ListMode] = "M2.5,2.5 L13.5,2.5 L13.5,6.5 L2.5,6.5 Z M2.5,9.5 L13.5,9.5 L13.5,13.5 L2.5,13.5 Z",
		[GeoKeys.GridMode] = "M2.5,2.5 L13.5,2.5 L13.5,6.5 L2.5,6.5 Z M2.5,9.5 L9,9.5 L9,13.5 L2.5,13.5 Z M11,9.5 L13.5,9.5 L13.5,13.5 L11,13.5 Z",
	};

	// ───────────────────────────── 取用工具(全部不抛异常) ─────────────────────────────

	/// <summary>当前是否深色(用于挑兜底档;读不到时按浅色)。</summary>
	private static bool IsDark
	{
		get
		{
			try
			{
				return TipCL.App.Ui.Ui.IsDark;
			}
			catch (Exception)
			{
				return false;
			}
		}
	}

	/// <summary>按键取资源对象;Application 不在 / 键不存在 / 取值抛异常都返回 null。</summary>
	public static object? Resource(string key)
	{
		try
		{
			Application? app = Application.Current;
			if (app != null && app.TryGetResource(key, null, out object? value) && value != null && value != AvaloniaProperty.UnsetValue)
			{
				return value;
			}
		}
		catch (Exception)
		{
			// 资源字典正在替换(明暗切换):当成取不到
		}
		return null;
	}

	/// <summary>某个令牌键的兜底色(按当前明暗挑档;未登记的键返回 null)。</summary>
	public static Color? FallbackColor(string key, bool? dark = null)
	{
		IReadOnlyDictionary<string, Color> table = (dark ?? IsDark) ? FallbackDark : FallbackLight;
		return table.TryGetValue(key, out Color c) ? c : null;
	}

	/// <summary>取画刷:资源里有就用资源(同一实例,跟随明暗替换要用 <see cref="DynOr"/>);没有就用登记的兜底色,再没有用 <paramref name="fallback"/>。</summary>
	public static IBrush BrushOr(string key, Color? fallback = null)
	{
		if (Resource(key) is IBrush brush)
		{
			return brush;
		}
		Color c = FallbackColor(key) ?? fallback ?? Colors.Transparent;
		return new SolidColorBrush(c);
	}

	/// <summary>取颜色(自绘层 DrawingContext 用):资源是纯色画刷就取它的颜色(连同 Opacity 折进 Alpha),否则兜底。</summary>
	public static Color ColorOr(string key, Color? fallback = null)
	{
		if (Resource(key) is ISolidColorBrush solid)
		{
			Color c = solid.Color;
			double op = solid.Opacity;
			if (op < 1.0)
			{
				c = Color.FromArgb((byte)Math.Round(c.A * Math.Clamp(op, 0.0, 1.0)), c.R, c.G, c.B);
			}
			return c;
		}
		return FallbackColor(key) ?? fallback ?? Colors.Transparent;
	}

	/// <summary>
	/// 把画刷键动态绑到属性上(明暗切换即时跟随);资源**不存在**时改为一次性设兜底色,
	/// 避免资源观察流推 UnsetValue 把属性清空。
	/// </summary>
	public static T DynOr<T>(T target, AvaloniaProperty property, string key, Color? fallback = null) where T : AvaloniaObject
	{
		if (Resource(key) != null)
		{
			return target.Dyn(property, key);
		}
		target.SetValue(property, new SolidColorBrush(FallbackColor(key) ?? fallback ?? Colors.Transparent));
		return target;
	}

	/// <summary>取阴影:资源里有就用,没有用登记的兜底(按明暗挑档)。</summary>
	public static BoxShadows ShadowOr(string key)
	{
		if (Resource(key) is BoxShadows shadows)
		{
			return shadows;
		}
		IReadOnlyDictionary<string, string> table = IsDark ? FallbackShadowDark : FallbackShadowLight;
		if (table.TryGetValue(key, out string? text))
		{
			try
			{
				return BoxShadows.Parse(text);
			}
			catch (Exception)
			{
				// 兜底串本身坏了(单测会抓):退回无阴影
			}
		}
		return default;
	}

	/// <summary>取 ControlTheme(找不到返回 null)。</summary>
	public static ControlTheme? Theme(string key)
	{
		return Resource(key) as ControlTheme;
	}

	/// <summary>
	/// 给元素套主题:<paramref name="onDark"/> 为 true 时先找 <c>key.OnDark</c>,没有就退回 <paramref name="key"/>;
	/// 都找不到返回 false(调用方自己写兜底外观)。目标类型与主题 TargetType 不匹配时也返回 false,不抛异常。
	/// </summary>
	public static bool ApplyTheme(StyledElement element, string key, bool onDark = false)
	{
		if (element == null)
		{
			return false;
		}
		ControlTheme? theme = (onDark ? Theme(ThemeKeys.OnDark(key)) : null) ?? Theme(key);
		if (theme == null)
		{
			return false;
		}
		if (theme.TargetType != null && !theme.TargetType.IsAssignableFrom(element.GetType()))
		{
			return false;
		}
		try
		{
			element.Theme = theme;
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	/// <summary>取 16×16 视框的图标几何(总是新实例:资源里的 Clone 一份,资源缺失时按兜底路径解析)。</summary>
	public static Geometry? GeometryOf(string key)
	{
		try
		{
			if (Resource(key) is Geometry g)
			{
				return g.Clone();
			}
			if (FallbackGeometry.TryGetValue(key, out string? data))
			{
				return Geometry.Parse(data);
			}
		}
		catch (Exception)
		{
			// 路径解析失败:返回 null,调用方不画这枚图标
		}
		return null;
	}

	/// <summary>
	/// 按显示尺寸画一枚描边图标(效果图口径:16 视框整体等比缩到 <paramref name="size"/>,线宽 1.6 也同比缩放;
	/// Geo.Grip 用 2.2)。<paramref name="owner"/> 给了就让描边跟随它的前景色(悬停 / 禁用 / 选中自动变色),
	/// 否则绑 <paramref name="strokeKey"/>(缺省 B.InkSoft)。几何取不到时返回一个空 Path(占位不报错)。
	/// </summary>
	public static Path Glyph(string key, double size, TemplatedControl? owner = null, string strokeKey = "B.InkSoft")
	{
		Geometry? geometry = GeometryOf(key);
		double scale = size / 16.0;
		if (geometry != null && Math.Abs(scale - 1.0) > 0.001)
		{
			geometry.Transform = new ScaleTransform(scale, scale);
		}
		double stroke16 = string.Equals(key, GeoKeys.Grip, StringComparison.Ordinal) ? GripStroke16 : GlyphStroke16;
		Path path = new Path
		{
			Data = geometry,
			Width = size,
			Height = size,
			Stretch = Stretch.None,
			StrokeThickness = stroke16 * scale,
			StrokeLineCap = PenLineCap.Round,
			StrokeJoin = PenLineJoin.Round,
			VerticalAlignment = VerticalAlignment.Center,
			IsHitTestVisible = false,
		};
		if (owner != null)
		{
			path[!Shape.StrokeProperty] = owner.GetObservable(TemplatedControl.ForegroundProperty).ToBinding();
		}
		else
		{
			DynOr(path, Shape.StrokeProperty, strokeKey, Color.Parse("#565656"));
		}
		return path;
	}
}
