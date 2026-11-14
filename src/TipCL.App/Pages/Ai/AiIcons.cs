using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.Services;

namespace TipCL.Pages.Ai;

/// <summary>
/// C8a:AI 那一族自己的图标与几个小控件(= Windows <c>Pages\Ai\AiIcons.cs</c>,285 行)。
///
/// <b>图标数据全是 SVG path 字符串</b>(35 个 <c>const string</c>,**逐字照抄,一个点都不许改**);
/// 画的方式走项目**现成口径** <see cref="Lucide.Icon(string,double,string)">Lucide.Icon</see>
/// (Windows 这份的 <c>Icon</c> 本来就是转调它)⇒ **不新加依赖、不新写渲染器**。
///
/// 与 Windows 的差异(逐条,均为**本机平台能力**差异,语义尽量对齐):
/// <list type="number">
/// <item><b><c>Path(...)</c> 的缩放方式</b>:WPF 用 <c>LayoutTransform = ScaleTransform</c>(24×24 视图盒整体缩放,
///   **描边宽度一起被缩放**);Avalonia 没有 <c>LayoutTransform</c>,这里用等价的
///   <c>RenderTransform = ScaleTransform(size/24, size/24)</c> —— 同样把描边一起缩放,**观感与 WPF 一致**。</item>
/// <item><b><c>FlatButtonTemplate()</c></b>:WPF 的 <c>FrameworkElementFactory</c> 在 Avalonia 不存在,改用
///   <c>FuncControlTemplate&lt;Button&gt;</c>(项目里已有先例:<c>Ui.cs</c> 的 <c>BareContainer</c>)重建同一棵视觉树
///   (Border 绑定模板父的 Background / BorderBrush / BorderThickness / Padding + 居中的 ContentPresenter)。
///   **模板语义一致**;差别只在实现机制。</item>
/// <item><b>悬停</b>:WPF <c>MouseEnter/MouseLeave</c> → Avalonia <c>PointerEntered/PointerExited</c>(同一时机)。</item>
/// <item><b>资源引用</b>:<c>SetResourceReference</c> → <c>.Dyn(...)</c>(项目统一写法)。</item>
/// <item><b><c>Ring</c> 的几何</b>:<c>PathGeometry</c>/<c>PathFigure</c>/<c>ArcSegment</c> 的构造方式在 Avalonia 是
///   <c>new PathGeometry { Figures = { … } }</c>(没有 WPF 那个接收 Figure 数组的构造函数)——
///   弧的参数(半径、<c>isLargeArc</c>、<c>SweepDirection.Clockwise</c>)一字不改。</item>
/// </list>
/// </summary>
internal static class AiIcons
{
	public const string Spark = "M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8z";

	public const string Layers = "M12 2l10 6-10 6L2 8z M2 14l10 6 10-6";

	public const string Check = "M20 6L9 17l-5-5";

	public const string X = "M18 6L6 18 M6 6l12 12";

	public const string Skip = "M9 6l6 6-6 6";

	public const string ChevDown = "M6 9l6 6 6-6";

	public const string ChevRight = "M9 6l6 6-6 6";

	public const string ArrowUp = "M12 19V5 M5 12l7-7 7 7";

	public const string Undo = "M3 7v6h6 M21 17a9 9 0 0 0-15-6.7L3 13";

	public const string Retry = "M21 12a9 9 0 1 1-3-6.7L21 8 M21 3v5h-5";

	public const string Copy = "M11 9h9a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-9a2 2 0 0 1-2-2v-9a2 2 0 0 1 2-2z M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1";

	public const string ThumbUp = "M7 10v12 M15 5.9L14 10h5.8a2 2 0 0 1 2 2.3l-1.4 8a2 2 0 0 1-2 1.7H7V10l4-8a3 3 0 0 1 4 3.9z";

	public const string ThumbDown = "M17 14V2 M9 18.1L10 14H4.2a2 2 0 0 1-2-2.3l1.4-8A2 2 0 0 1 5.6 2H17v12l-4 8a3 3 0 0 1-4-3.9z";

	public const string Shield = "M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z";

	public const string Cpu = "M7 5h10a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z M9 9h6v6H9z M9 2v3 M15 2v3 M9 19v3 M15 19v3 M2 9h3 M2 15h3 M19 9h3 M19 15h3";

	public const string Plus = "M12 5v14 M5 12h14";

	public const string Panel = "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M9 3v18";

	public const string More = "M12 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2z M19 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2z M5 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2z";

	public const string Branch = "M6 3a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M6 15a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M18 5a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M6 9v6 M18 11a6 6 0 0 1-6 6H9";

	public const string Alert = "M10.3 3.9L1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z M12 9v4 M12 17h.01";

	public const string Trash = "M3 6h18 M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2 M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6";

	public const string Pen = "M12 20h9 M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z";

	public const string Globe = "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18z M3 12h18 M12 3a14 14 0 0 1 0 18 M12 3a14 14 0 0 0 0 18";

	public const string Eye = "M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6z";

	public const string EyeOff = "M9.88 9.88a3 3 0 1 0 4.24 4.24 M10.73 5.08A10.43 10.43 0 0 1 12 5c7 0 10 7 10 7a13.16 13.16 0 0 1-1.67 2.68 M6.61 6.61A13.53 13.53 0 0 0 2 12s3 7 10 7a9.74 9.74 0 0 0 5.39-1.61 M2 2l20 20";

	public const string Plug = "M12 22v-5 M9 8V2 M15 8V2 M18 8v5a6 6 0 0 1-12 0V8z";

	public const string Box = "M21 8l-9-5-9 5v8l9 5 9-5z M3 8l9 5 9-5 M12 13v8";

	public const string List = "M8 6h13 M8 12h13 M8 18h13 M3 6h.01 M3 12h.01 M3 18h.01";

	public const string Archive = "M3 3h18a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z M4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8 M10 12h4";

	public const string Clock = "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18z M12 7v5l3 2";

	public const string Search = "M21 21l-4.34-4.34 M11 3a8 8 0 1 0 0 16a8 8 0 1 0 0-16z";

	public const string File = "M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8l6 6v12a2 2 0 0 1-2 2z M14 2v6h6 M8 13h8 M8 17h5";

	public const string Download = "M12 3v12 M6 11l6 6 6-6 M4 21h16";

	public const string Scan = "M3 7V5a2 2 0 0 1 2-2h2 M17 3h2a2 2 0 0 1 2 2v2 M21 17v2a2 2 0 0 1-2 2h-2 M7 21H5a2 2 0 0 1-2-2v-2 M7 12h10";

	public const string Settings = "M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M12 2v3 M12 19v3 M2 12h3 M19 12h3 M4.9 4.9l2.1 2.1 M17 17l2.1 2.1 M4.9 19.1L7 17 M17 7l2.1-2.1";

	/// <summary>【Windows 源】<c>:84-87</c>:图标(走 <see cref="Lucide.Icon"/> 这条现成口径)。</summary>
	public static Path Icon(string data, double size = 16.0, string brushKey = "B.InkMuted")
	{
		return Lucide.Icon(data, size, brushKey);
	}

	/// <summary>【Windows 源】<c>:89-106</c>:24×24 视图盒的描边路径(缩放见类注释第 1 条)。</summary>
	public static Path Path(string data, double size, IBrush brush)
	{
		double scale = size / 24.0;
		return new Path
		{
			Data = Geometry.Parse(data),
			Width = 24.0,
			Height = 24.0,
			Stretch = Stretch.None,
			StrokeThickness = 2.0,
			StrokeLineCap = PenLineCap.Round,
			StrokeJoin = PenLineJoin.Round,
			RenderTransform = new ScaleTransform(scale, scale),
			IsHitTestVisible = false,
			Stroke = brush,
		};
	}

	/// <summary>【Windows 源】<c>:108-135</c>:扁按钮的模板(视觉树照抄,Border 绑模板父的四个属性 + 居中 ContentPresenter)。</summary>
	public static FuncControlTemplate<Button> FlatButtonTemplate()
	{
		return new FuncControlTemplate<Button>((button, _) =>
		{
			Border border = new Border
			{
				CornerRadius = new CornerRadius(8.0),
			};
			// 内层模板控件的四个属性跟着"模板父"(即这个 Button)走 —— 与 WPF 模板里的
			// RelativeSource TemplatedParent 同一个意图;Avalonia 里 FuncControlTemplate 的
			// 第一个参数就是被模板化的那个控件实例。
			border.Bind(Border.BackgroundProperty, button.GetObservable(TemplatedControl.BackgroundProperty));
			border.Bind(Border.BorderBrushProperty, button.GetObservable(TemplatedControl.BorderBrushProperty));
			border.Bind(Border.BorderThicknessProperty, button.GetObservable(TemplatedControl.BorderThicknessProperty));
			border.Bind(Border.PaddingProperty, button.GetObservable(TemplatedControl.PaddingProperty));
			border.Child = new ContentPresenter
			{
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
			};
			return border;
		});
	}

	/// <summary>【Windows 源】<c>:137-179</c>:图标 + 可选文案的"扁按钮"(悬停换底色,pill 时带边框)。</summary>
	public static Button Flat(string icon, string? text, string? tip, Action click, string fg = "B.InkMuted", double iconSize = 14.0, bool pill = false)
	{
		Button b = new Button
		{
			Template = FlatButtonTemplate(),
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(pill ? 1 : 0),
			Padding = (string.IsNullOrEmpty(text) ? new Thickness(5.0) : new Thickness(8.0, 3.0, 9.0, 3.0)),
			Cursor = new Cursor(StandardCursorType.Hand),
			Focusable = false,
		};
		if (pill)
		{
			b.Dyn(Border.BorderBrushProperty, "B.Line");
			b.Dyn(Border.BackgroundProperty, "B.Paper");
		}
		TextBlock? label = (string.IsNullOrEmpty(text) ? null : Ui.Txt(text!, 11.5, bold: false, fg));
		b.Content = Ui.H(5.0, Icon(icon, iconSize, fg).VCenter(), label?.VCenter());
		if (tip != null)
		{
			ToolTip.SetTip(b, tip);
		}
		b.PointerEntered += delegate
		{
			b.Dyn(Border.BackgroundProperty, "B.Hover");
		};
		b.PointerExited += delegate
		{
			if (pill)
			{
				b.Dyn(Border.BackgroundProperty, "B.Paper");
			}
			else
			{
				b.Background = Brushes.Transparent;
			}
		};
		b.Click += delegate
		{
			click();
		};
		return b;
	}

	/// <summary>【Windows 源】<c>:181-233</c>:工具图标方块(状态色底 + 右下角状态角标)。</summary>
	public static Grid Tile(Control icon, ToolState state)
	{
		Border tile = new Border
		{
			Width = 24.0,
			Height = 24.0,
			CornerRadius = new CornerRadius(6.0),
			Child = icon.Mid(),
		};
		tile.Dyn(Border.BackgroundProperty, state switch
		{
			ToolState.Failed => "B.DangerSoft",
			ToolState.Skipped => "B.Hover",
			_ => "B.AccentSoft",
		});
		Grid grid = new Grid
		{
			Width = 26.0,
			Height = 26.0,
			VerticalAlignment = VerticalAlignment.Top,
		};
		grid.Children.Add(tile.TopLeft());
		if (state == ToolState.Done || state == ToolState.Failed || state == ToolState.Skipped)
		{
			string data = state switch
			{
				ToolState.Done => Check,
				ToolState.Failed => X,
				_ => Skip,
			};
			Border badge = new Border
			{
				Width = 12.0,
				Height = 12.0,
				CornerRadius = new CornerRadius(6.0),
				BorderThickness = new Thickness(1.5),
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Bottom,
			};
			Path check = Path(data, 7.0, Brushes.White);
			check.StrokeThickness = 4.0;
			badge.Child = check.Mid();
			badge.Dyn(Border.BorderBrushProperty, "B.Paper");
			badge.Dyn(Border.BackgroundProperty, state switch
			{
				ToolState.Done => "B.Accent",
				ToolState.Failed => "B.Danger",
				_ => "B.InkFaint",
			});
			grid.Children.Add(badge);
		}
		return grid;
	}

	/// <summary>【Windows 源】<c>:235-240</c>:居中(WPF 的 <c>FrameworkElement</c> 在 mac 侧对应 <c>Control</c>)。</summary>
	public static T Mid<T>(this T el) where T : Control
	{
		el.HorizontalAlignment = HorizontalAlignment.Center;
		el.VerticalAlignment = VerticalAlignment.Center;
		return el;
	}

	/// <summary>【Windows 源】<c>:242-247</c>:左上角对齐。</summary>
	public static T TopLeft<T>(this T el) where T : Control
	{
		el.HorizontalAlignment = HorizontalAlignment.Left;
		el.VerticalAlignment = VerticalAlignment.Top;
		return el;
	}

	/// <summary>【Windows 源】<c>:249-284</c>:上下文占用的圆环(底环 + 从 12 点顺时针的弧)。</summary>
	public static Grid Ring(double ratio, double size = 18.0)
	{
		Grid grid = new Grid
		{
			Width = size,
			Height = size,
		};
		Ellipse track = new Ellipse
		{
			StrokeThickness = 2.4,
		};
		track.Dyn(Shape.StrokeProperty, "B.Line");
		grid.Children.Add(track);
		if (ratio > 0.001)
		{
			double radius = size / 2.0 - 1.2;
			double center = size / 2.0;
			double sweep = Math.Min(ratio, 0.9999) * 2.0 * Math.PI;
			PathFigure figure = new PathFigure
			{
				StartPoint = new Point(center, center - radius),
				IsClosed = false,
			};
			// Avalonia 的 ArcSegment 无参构造 + 属性赋值(WPF 那个带参数的构造函数在这边没有),
			// 参数值一字不改:半径半径、rotationAngle=0、isLargeArc=sweep>π、顺时针、描边。
			figure.Segments!.Add(new ArcSegment
			{
				Point = new Point(center + radius * Math.Sin(sweep), center - radius * Math.Cos(sweep)),
				Size = new Size(radius, radius),
				RotationAngle = 0.0,
				IsLargeArc = sweep > Math.PI,
				SweepDirection = SweepDirection.Clockwise,
				IsStroked = true,
			});
			Path arc = new Path
			{
				Data = new PathGeometry
				{
					Figures = { figure },
				},
				StrokeThickness = 2.4,
				StrokeLineCap = PenLineCap.Round,
			};
			arc.Dyn(Shape.StrokeProperty, (ratio > 0.85) ? "B.Danger" : ((ratio > 0.65) ? "B.Warn" : "B.Accent"));
			grid.Children.Add(arc);
		}
		return grid;
	}
}
